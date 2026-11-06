using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>Why the load's hold on the loading screen ended.</summary>
    public enum LoadEnd
    {
        /// <summary>Not begun, or still running.</summary>
        None,

        /// <summary>Every source of work had nothing left for long enough.</summary>
        Done,

        /// <summary>The hard cap: whatever was left stays for the game to do after the screen.</summary>
        Cap,

        /// <summary>Something threw; the screen is let go at once.</summary>
        Error,

        /// <summary>The game finished the loading by itself (the screen went before the work was done), or another load began.</summary>
        Screen,

        /// <summary>The mod is being disposed.</summary>
        Disposed,

        /// <summary>The restore found nothing to put back: nothing to wait for.</summary>
        NoWork
    }

    /// <summary>
    /// The state of the mod's first-time work under the loading screen (0.12.78): the loading screen of the game waits for trackers it can see; this decides when the mod's one is complete. The work is
    /// a list of sources, each saying how much of it is waiting now; a source that is not yet known (the system has not read what it works on) holds the gate; the gate is done when every source has
    /// had nothing waiting for StableUpdates updates in a row, and it is ended, whatever else is true, by the hard cap, by an error, by the end of the loading and by a new load. A hung loading screen
    /// would be the worst bug this mod could have: nothing here waits for anything that is not bounded by CapSeconds. Pure; the clock is given.
    /// </summary>
    public sealed class LoadGate
    {
        public const double DefaultCapSeconds = 15.0;
        public const int StableUpdates = 3;

        private sealed class Source
        {
            public string Name;
            public float Weight;
            public int Total, Pending;
            public bool Known;
            public double Ms;
        }

        private readonly Func<double> m_Now;
        private readonly double m_Cap;
        private readonly List<Source> m_Sources = new List<Source>();
        private double m_Start;
        private int m_StableRun;
        private string m_Detail = "";

        public LoadGate(Func<double> now, double capSeconds = DefaultCapSeconds)
        {
            m_Now = now ?? throw new ArgumentNullException(nameof(now));
            m_Cap = capSeconds;
        }

        /// <summary>True from Begin to the end.</summary>
        public bool Running { get; private set; }

        public LoadEnd End { get; private set; }

        /// <summary>Seconds from Begin to the end (or to now while running).</summary>
        public double Seconds => (Running ? m_Now() : m_EndedAt) - m_Start;

        private double m_EndedAt;

        /// <summary>
        /// Begins a hold. Nothing is begun when the switch is off, when it is not a saved game being loaded into the game, or when it is already running (never twice for one load).
        /// </summary>
        public bool Begin(bool enabled, bool loadGame, bool gameMode)
        {
            if (!enabled || !loadGame || !gameMode || Running)
                return false;
            m_Sources.Clear();
            m_Start = m_Now();
            m_StableRun = 0;
            m_Detail = "";
            Running = true;
            End = LoadEnd.None;
            return true;
        }

        /// <summary>Adds a source of work (once; the same name is the same source). Weight says how much of the progress it is.</summary>
        public void Add(string name, float weight)
        {
            if (Find(name) == null)
                m_Sources.Add(new Source { Name = name, Weight = Math.Max(0.0001f, weight) });
        }

        /// <summary>What a source has waiting now. known false: it cannot say yet (it holds the gate). The total is the most that was waiting at once, so that progress is what has been taken from it.</summary>
        public void Report(string name, int pending, bool known = true)
        {
            Source source = Find(name);
            if (source == null || !Running)
                return;
            source.Known = known;
            source.Pending = Math.Max(0, pending);
            if (known && source.Pending > source.Total)
                source.Total = source.Pending;
        }

        /// <summary>The main-thread time a source's work took while the gate ran (for the end's line).</summary>
        public void AddMs(string name, double ms)
        {
            Source source = Find(name);
            if (source != null)
                source.Ms += ms;
        }

        /// <summary>The progress now, 0 to 1: the weighted share of what each source has taken from its waiting work; 1 when ended.</summary>
        public float Progress
        {
            get
            {
                if (!Running)
                    return End == LoadEnd.None ? 0f : 1f;
                float weights = 0f, sum = 0f;
                foreach (Source source in m_Sources)
                {
                    weights += source.Weight;
                    sum += source.Weight * Fraction(source);
                }

                // Never 1 while running: the tracker is complete only when the gate has ended.
                return weights <= 0f ? 0f : Math.Min(0.999f, sum / weights);
            }
        }

        private static float Fraction(Source source)
        {
            if (!source.Known)
                return 0f;
            return source.Total <= 0 ? 1f : 1f - (float)source.Pending / source.Total;
        }

        /// <summary>
        /// Call every frame (and after the sources have been reported): ends the hold when it is done or the cap is over. True while the hold goes on.
        /// </summary>
        public bool Step()
        {
            if (!Running)
                return false;
            if (m_Now() - m_Start >= m_Cap)
            {
                Finish(LoadEnd.Cap);
                return false;
            }

            bool quiet = m_Sources.Count > 0;
            foreach (Source source in m_Sources)
            {
                if (!source.Known || source.Pending > 0)
                {
                    quiet = false;
                    break;
                }
            }

            m_StableRun = quiet ? m_StableRun + 1 : 0;
            if (m_StableRun >= StableUpdates)
            {
                Finish(LoadEnd.Done);
                return false;
            }

            return true;
        }

        /// <summary>Ends the hold for a reason that is not the gate's own (an error, the screen going, a new load, a dispose, nothing to put back). Does nothing when it is not running.</summary>
        public void Stop(LoadEnd reason, string detail = "")
        {
            if (!Running)
                return;
            m_Detail = detail ?? "";
            Finish(reason);
        }

        private void Finish(LoadEnd reason)
        {
            m_EndedAt = m_Now();
            Running = false;
            End = reason;
        }

        /// <summary>What was still waiting when it ended, per source: "tiles 3/12" (pending of the most waiting), for the log.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;
            foreach (Source source in m_Sources)
            {
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(source.Name).Append(' ');
                text.Append(source.Known ? source.Pending.ToString(c) + " of " + source.Total.ToString(c) + " waiting" : "not known");
                text.Append(", ").Append(source.Ms.ToString("0", c)).Append(" ms");
            }

            return text.Length == 0 ? "no source" : text.ToString();
        }

        /// <summary>What each source has waiting now (-1: not known yet), in the order they were added: the log's timeline of the hold.</summary>
        public void Snapshot(List<KeyValuePair<string, int>> into)
        {
            into.Clear();
            foreach (Source source in m_Sources)
                into.Add(new KeyValuePair<string, int>(source.Name, source.Known ? source.Pending : -1));
        }

        /// <summary>Everything still waiting, in items, summed over the known sources.</summary>
        public int WaitingNow
        {
            get
            {
                int sum = 0;
                foreach (Source source in m_Sources)
                    sum += source.Known ? source.Pending : 0;
                return sum;
            }
        }

        public string Detail => m_Detail;

        private Source Find(string name)
        {
            foreach (Source source in m_Sources)
            {
                if (source.Name == name)
                    return source;
            }

            return null;
        }
    }

    /// <summary>
    /// What a per-frame budget of the mod's systems is while the first-time work runs under the loading screen: the player sees no frame there, so the budgets grow (six times), but never to
    /// more than MaxMs a frame for one system, so that the loading screen still moves. The steady budgets are not touched: a system asks for Scaled(its own).
    /// </summary>
    public static class LoadBudget
    {
        public const double Factor = 6.0, MaxMs = 14.0;

        /// <summary>Whether the budgets are the loading ones: set by the mod's load system for as long as its hold on the loading screen runs.</summary>
        public static bool Loading;

        public static double Scaled(double steadyMs) => Scaled(steadyMs, Loading);

        public static double Scaled(double steadyMs, bool loading)
        {
            if (!loading || steadyMs <= 0.0)
                return steadyMs;
            return Math.Max(steadyMs, Math.Min(MaxMs, steadyMs * Factor));
        }

        /// <summary>The recent time of a frame (ms) while the hold runs, an average (NoteFrame); 0 when not known.</summary>
        public static double FrameMs;

        /// <summary>How fast the frame average follows a new frame, and the longest frame (ms) that counts: one long frame (a collection) must not open the budgets for the next ones.</summary>
        private const double kAverage = 0.2, kLongestFrameMs = 500.0;

        public static void NoteFrame(double seconds)
        {
            double ms = Math.Min(kLongestFrameMs, Math.Max(0.0, seconds * 1000.0));
            FrameMs = FrameMs <= 0.0 ? ms : FrameMs + (ms - FrameMs) * kAverage;
        }

        /// <summary>
        /// A budget that follows how slow the frames are (0.12.80): what Scaled gives, or share of the recent frame time up to capMs, whichever is more, while the hold runs. A frame under the loading
        /// screen is 100 ms and more, six times a small budget is then a fraction of a percent of it; the work done per second is what the budget is for, and it is not below Scaled's, so a fast frame
        /// rate gives exactly what it gave.
        /// </summary>
        public static double ByFrame(double steadyMs, double share, double capMs) => ByFrame(steadyMs, share, capMs, Loading, FrameMs);

        public static double ByFrame(double steadyMs, double share, double capMs, bool loading, double frameMs)
        {
            double scaled = Scaled(steadyMs, loading);
            if (!loading || steadyMs <= 0.0)
                return scaled;
            return Math.Max(scaled, Math.Min(capMs, share * frameMs));
        }

        /// <summary>
        /// The budget of the fits' take step (AreaFitSystem.TakeFinished): the submits stop when 48 requests are out (WorkerFull), so the takes set the pace of the whole pass. At 1.5 ms of a 110 ms frame
        /// they were 5 to 7 a frame: the owner's 0.12.79 log drained 66 near fits a second while the pass used 8 of its 12 ms and no frame was slow. A twentieth of the frame, at most 6 ms.
        /// </summary>
        public const double TakeShare = 0.05, TakeCapMs = 6.0;

        public static double Take(double steadyMs) => ByFrame(steadyMs, TakeShare, TakeCapMs);

        public static double Take(double steadyMs, bool loading, double frameMs) => ByFrame(steadyMs, TakeShare, TakeCapMs, loading, frameMs);
    }
}
