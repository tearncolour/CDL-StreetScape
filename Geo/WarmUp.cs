using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace CDLC.TS.Geo
{
    public enum WarmThread
    {
        /// <summary>Run on the main thread, a call at a time, inside a budget (Unity objects, the game's ECS).</summary>
        Main,

        /// <summary>Run once, in order, on the thread the host gives (pure code: JIT, caches, files).</summary>
        Worker
    }

    public enum WarmState
    {
        Waiting,
        Running,
        Done,

        /// <summary>It threw; it is not tried again.</summary>
        Failed,

        /// <summary>It was dropped: too slow on the main thread, or the hold ended before it was done.</summary>
        Skipped
    }

    /// <summary>
    /// One first-use cost moved under the loading screen. Main: Run is called again and again and says true when it is finished (it must do a small slice a call); Worker: Run is called once.
    /// Evidence says which line of which log the step is for; a step with none does not belong in the list.
    /// </summary>
    public sealed class WarmStep
    {
        public readonly string Name, Evidence;
        public readonly WarmThread Thread;
        public readonly Func<bool> Run;
        internal int StateValue;
        internal int CallCount, Strikes;
        internal double TotalMs, WorstMs;
        internal string Error = "";

        public WarmStep(string name, WarmThread thread, string evidence, Func<bool> run)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Thread = thread;
            if (string.IsNullOrWhiteSpace(evidence))
                throw new ArgumentException("a warm-up step is for a cost a log shows: say which", nameof(evidence));
            Evidence = evidence;
            Run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public WarmState State => (WarmState)Volatile.Read(ref StateValue);

        public bool Finished => State >= WarmState.Done;

        public int Calls => Volatile.Read(ref CallCount);

        public double Ms => Volatile.Read(ref TotalMs);

        public double Worst => Volatile.Read(ref WorstMs);

        public string Failure => Error;
    }

    /// <summary>
    /// The warm-up of the first-use costs under the loading screen (0.12.78), after the items are back: a list of steps, each a cost the logs show on the main thread or a latency the first use pays,
    /// done where it hurts nobody. Main steps share a per-frame budget, a call at a time, round robin (at least one call a frame, so that they cannot starve); worker steps run once, in order, on
    /// the one thread the host starts. A step that throws is Failed and left; a main step whose calls are slow (SlowCallMs) twice in a row is dropped, so that it cannot take the screen's frames;
    /// Cancel (the hold ended: cap, error, the screen going, dispose) drops what is left, and a worker stops before its next step. Nothing here waits for anything: Waiting is a count the load's gate reads.
    /// Pure; the clock and the way a worker is started are given.
    /// </summary>
    public sealed class WarmUp
    {
        public const double DefaultSlowCallMs = 30.0;
        public const int SlowStrikes = 2;

        /// <summary>The most calls one Pump makes, whatever the clock says: a step that never finishes, on a clock that does not move, cannot hang the frame.</summary>
        private const int kMaxCalls = 4096;

        private readonly Func<double> m_NowMs;
        private readonly Action<Action> m_StartWorker;
        private readonly double m_SlowCallMs;
        private readonly List<WarmStep> m_Steps = new List<WarmStep>();
        private int m_Started, m_Cancelled;
        private int m_Next;

        public WarmUp(Func<double> nowMs, Action<Action> startWorker, double slowCallMs = DefaultSlowCallMs)
        {
            m_NowMs = nowMs ?? throw new ArgumentNullException(nameof(nowMs));
            m_StartWorker = startWorker ?? throw new ArgumentNullException(nameof(startWorker));
            m_SlowCallMs = slowCallMs;
        }

        public IReadOnlyList<WarmStep> Steps => m_Steps;

        public bool Cancelled => Volatile.Read(ref m_Cancelled) != 0;

        /// <summary>Adds a step; only before the first Pump (the worker's list is fixed when it starts). False when it was too late, or the name is there already.</summary>
        public bool Add(WarmStep step)
        {
            if (step == null || Volatile.Read(ref m_Started) != 0)
                return false;
            foreach (WarmStep other in m_Steps)
            {
                if (other.Name == step.Name)
                    return false;
            }

            m_Steps.Add(step);
            return true;
        }

        /// <summary>The steps not yet done, failed or dropped (the worker's included).</summary>
        public int Waiting
        {
            get
            {
                int waiting = 0;
                foreach (WarmStep step in m_Steps)
                {
                    if (!step.Finished)
                        waiting++;
                }

                return waiting;
            }
        }

        public int Failed => Count(WarmState.Failed);

        public int Skipped => Count(WarmState.Skipped);

        private int Count(WarmState state)
        {
            int n = 0;
            foreach (WarmStep step in m_Steps)
            {
                if (step.State == state)
                    n++;
            }

            return n;
        }

        /// <summary>
        /// Call every update while the hold runs: starts the worker's steps (once) and gives the main steps their calls within budgetMs. The call that crosses the budget is the last of the update; there is
        /// always one. Returns the milliseconds the main steps took.
        /// </summary>
        public double Pump(double budgetMs)
        {
            if (Cancelled)
                return 0.0;
            StartWorker();
            double begin = m_NowMs();
            for (int calls = 0; calls < kMaxCalls; calls++)
            {
                if (calls > 0 && m_NowMs() - begin >= budgetMs)
                    break;
                WarmStep step = NextMain();
                if (step == null)
                    break;
                CallMain(step);
            }

            return m_NowMs() - begin;
        }

        /// <summary>The next main step that is not finished, after the one called last (round robin); null when there is none.</summary>
        private WarmStep NextMain()
        {
            for (int i = 0; i < m_Steps.Count; i++)
            {
                int at = (m_Next + i) % m_Steps.Count;
                WarmStep step = m_Steps[at];
                if (step.Thread == WarmThread.Main && !step.Finished)
                {
                    m_Next = (at + 1) % m_Steps.Count;
                    return step;
                }
            }

            return null;
        }

        private void CallMain(WarmStep step)
        {
            Volatile.Write(ref step.StateValue, (int)WarmState.Running);
            double start = m_NowMs();
            bool finished = false;
            string error = null;
            try
            {
                finished = step.Run();
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }

            double ms = m_NowMs() - start;
            Note(step, ms);
            if (error != null)
            {
                step.Error = error;
                Volatile.Write(ref step.StateValue, (int)WarmState.Failed);
                return;
            }

            if (finished)
            {
                Volatile.Write(ref step.StateValue, (int)WarmState.Done);
                return;
            }

            step.Strikes = ms >= m_SlowCallMs ? step.Strikes + 1 : 0;
            if (step.Strikes >= SlowStrikes)
            {
                step.Error = $"dropped: {step.Strikes} calls in a row of {m_SlowCallMs:0} ms or more";
                Volatile.Write(ref step.StateValue, (int)WarmState.Skipped);
                return;
            }

            Volatile.Write(ref step.StateValue, (int)WarmState.Waiting);
        }

        private static void Note(WarmStep step, double ms)
        {
            Interlocked.Increment(ref step.CallCount);
            Volatile.Write(ref step.TotalMs, step.TotalMs + ms);
            if (ms > step.WorstMs)
                Volatile.Write(ref step.WorstMs, ms);
        }

        private void StartWorker()
        {
            if (Interlocked.Exchange(ref m_Started, 1) != 0)
                return;
            var mine = new List<WarmStep>();
            foreach (WarmStep step in m_Steps)
            {
                if (step.Thread == WarmThread.Worker)
                    mine.Add(step);
            }

            if (mine.Count == 0)
                return;
            try
            {
                m_StartWorker(() => RunWorker(mine));
            }
            catch (Exception ex)
            {
                foreach (WarmStep step in mine)
                {
                    step.Error = "the worker could not be started: " + ex.Message;
                    Volatile.Write(ref step.StateValue, (int)WarmState.Failed);
                }
            }
        }

        private void RunWorker(List<WarmStep> steps)
        {
            foreach (WarmStep step in steps)
            {
                if (Cancelled)
                    return;
                Volatile.Write(ref step.StateValue, (int)WarmState.Running);
                double start = m_NowMs();
                string error = null;
                try
                {
                    step.Run();
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + ": " + ex.Message;
                }

                Note(step, m_NowMs() - start);
                // The hold may have ended while the step ran: what it did is kept (every step is a cache fill), but it is no longer waited for (it stays Skipped).
                if (error != null)
                    step.Error = error;
                Interlocked.CompareExchange(ref step.StateValue, error != null ? (int)WarmState.Failed : (int)WarmState.Done, (int)WarmState.Running);
            }
        }

        /// <summary>Drops what is not finished: the hold ended. A worker stops before its next step; the one that is running ends by itself and is not waited for.</summary>
        public void Cancel()
        {
            Interlocked.Exchange(ref m_Cancelled, 1);
            foreach (WarmStep step in m_Steps)
            {
                int state = Volatile.Read(ref step.StateValue);
                while (state < (int)WarmState.Done)
                {
                    int seen = Interlocked.CompareExchange(ref step.StateValue, (int)WarmState.Skipped, state);
                    if (seen == state)
                        break;
                    state = seen;
                }
            }
        }

        /// <summary>For the log: each step's state, milliseconds and calls ("text-plan done 6 ms (worker)").</summary>
        public string Describe()
        {
            if (m_Steps.Count == 0)
                return "no step";
            var text = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;
            foreach (WarmStep step in m_Steps)
            {
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(step.Name).Append(' ').Append(step.State.ToString().ToLowerInvariant());
                if (step.Calls > 0)
                    text.Append(' ').Append(step.Ms.ToString("0", c)).Append(" ms in ").Append(step.Calls.ToString(c)).Append(" call(s), the worst ").Append(step.Worst.ToString("0.0", c)).Append(" ms");
                text.Append(step.Thread == WarmThread.Worker ? " (worker)" : " (main)");
                if (step.Failure.Length > 0)
                    text.Append(": ").Append(step.Failure);
            }

            return text.ToString();
        }
    }
}
