using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CDLC.TS.Geo
{
    /// <summary>A job made, with what came of it. Error is set when the cut threw (Result is then empty).</summary>
    public struct FitDone
    {
        public FitJob Job;
        public FitResult Result;
        public Exception Error;

        /// <summary>How long it waited for a thread (ms).</summary>
        public double WaitedMs;
    }

    /// <summary>
    /// The cuts of the roads' surface made off the main thread (0.12.74). Not LatestWorker: every job is wanted (an area, a post, a marking each need their own), so they are made in the order they were
    /// given, by at most Threads tasks at a time; the results are taken from the main thread, in the order they were made. Clear (a city is loaded or left) forgets what waits and what is still being made,
    /// whose results are then not given. Pure.
    /// </summary>
    public sealed class FitWorker
    {
        public const int DefaultThreads = 2;

        private struct Waiting
        {
            public FitJob Job;
            public int Epoch;
            public long At;
        }

        private readonly object m_Lock = new object();
        private readonly Queue<Waiting> m_Queue = new Queue<Waiting>();
        private readonly Queue<FitDone> m_Done = new Queue<FitDone>();
        private readonly int m_Threads;
        private int m_Running, m_Epoch, m_InFlight;
        private long m_NextId;
        private long m_Submitted, m_Taken, m_Failed, m_Dropped;
        private double m_WorkerMs, m_WorstMs;
        private int m_MostInFlight;

        public FitWorker(int threads = DefaultThreads)
        {
            m_Threads = Math.Max(1, threads);
        }

        /// <summary>Jobs given and not yet taken: waiting, being made, or made and not taken.</summary>
        public int InFlight
        {
            get
            {
                lock (m_Lock)
                    return m_InFlight;
            }
        }

        /// <summary>Since the start (Clear keeps them): jobs given and taken, those whose cut threw, the time the cuts took on the threads (ms), the longest one, the most jobs in flight at once, and cuts that were made and dropped because Clear came first.</summary>
        public (long Submitted, long Taken, long Failed, double WorkerMs, double WorstMs, int MostInFlight, long Dropped) Counters
        {
            get
            {
                lock (m_Lock)
                    return (m_Submitted, m_Taken, m_Failed, m_WorkerMs, m_WorstMs, m_MostInFlight, m_Dropped);
            }
        }

        /// <summary>A job to be made; its Id is set. Main thread (or any: the lock covers it).</summary>
        public long Submit(FitJob job)
        {
            bool start = false;
            lock (m_Lock)
            {
                job.Id = ++m_NextId;
                m_Queue.Enqueue(new Waiting { Job = job, Epoch = m_Epoch, At = Stopwatch.GetTimestamp() });
                m_Submitted++;
                m_InFlight++;
                if (m_InFlight > m_MostInFlight)
                    m_MostInFlight = m_InFlight;
                if (m_Running < m_Threads)
                {
                    m_Running++;
                    start = true;
                }
            }

            if (start)
                Task.Run((Action)Loop);
            return job.Id;
        }

        /// <summary>The next job that was made, in the order they were made; false when none is.</summary>
        public bool TryTake(out FitDone done)
        {
            lock (m_Lock)
            {
                if (m_Done.Count == 0)
                {
                    done = default;
                    return false;
                }

                done = m_Done.Dequeue();
                m_InFlight--;
                m_Taken++;
                return true;
            }
        }

        /// <summary>What waits, is being made and was made is forgotten; what is being made now is dropped when it is done.</summary>
        public void Clear()
        {
            lock (m_Lock)
            {
                m_Epoch++;
                m_Queue.Clear();
                m_Done.Clear();
                m_InFlight = 0;
            }
        }

        private void Loop()
        {
            while (true)
            {
                Waiting next;
                lock (m_Lock)
                {
                    if (m_Queue.Count == 0)
                    {
                        m_Running--;
                        return;
                    }

                    next = m_Queue.Dequeue();
                }

                var done = new FitDone { Job = next.Job, WaitedMs = (Stopwatch.GetTimestamp() - next.At) * 1000.0 / Stopwatch.Frequency };
                try
                {
                    done.Result = FitCut.Run(next.Job);
                }
                catch (Exception ex)
                {
                    done.Error = ex;
                }

                lock (m_Lock)
                {
                    if (next.Epoch != m_Epoch)
                    {
                        m_Dropped++;
                        continue;
                    }

                    double ms = done.Result.CutMs + done.Result.RuleMs;
                    m_WorkerMs += ms;
                    if (ms > m_WorstMs)
                        m_WorstMs = ms;
                    if (done.Error != null)
                        m_Failed++;
                    m_Done.Enqueue(done);
                }
            }
        }
    }
}
