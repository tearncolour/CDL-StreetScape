using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CDLC.TS.Geo
{
    /// <summary>The comparison of one part, or the reason it could not be made.</summary>
    public struct ShadowDone
    {
        public PartComparison Comparison;
        public Exception Error;
    }

    /// <summary>
    /// Makes a part's shadow comparison (0.12.75): the adaptive sampler lays the part from the job's curves, and the two are measured against each other (SamplingCheck). Pure and without state: any
    /// number of threads may run jobs at once, and a job is only read. The owner's first log of the shadow compared 22 of 1,095 parts because the comparison was made on the main thread inside a
    /// budget of 0.2 ms a frame; here it costs the frame nothing.
    /// </summary>
    public static class ShadowRunner
    {
        public static PartComparison Run(ShadowJob job)
        {
            long begin = Stopwatch.GetTimestamp();
            var probe = new SamplingProbe();
            var bands = new List<SurfaceStrip>();
            if (job.Node)
            {
                bands = PartSampling.NodeBands(job.Pieces, job.Source, probe);
            }
            else
            {
                foreach (ShadowHalf half in job.Halves)
                    bands.AddRange(PartSampling.HalfBands(half.Left, half.Right, job.Stretches, half.Zones, job.Source, probe));
            }

            long laid = Stopwatch.GetTimestamp();
            SamplingMeasure measure = SamplingCheck.Measure(probe);
            CornerResult corners = SamplingCheck.Corners(job.FixedBands, bands, SamplingStats.CornerLimit);
            return new PartComparison
            {
                Part = job.Part,
                FixedMs = job.FixedMs,
                AdaptiveMs = (laid - begin) * 1000.0 / Stopwatch.Frequency,
                FixedPieces = SamplingCheck.Pieces(job.FixedBands),
                AdaptivePieces = SamplingCheck.Pieces(bands),
                Measure = measure,
                Corners = corners,
                FixedArea = SamplingCheck.PlanArea(job.FixedBands),
                AdaptiveArea = SamplingCheck.PlanArea(bands),
                ShadowMs = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency
            };
        }
    }

    /// <summary>
    /// The shadow comparisons made on one background thread, in the order they were given, and handed back to the main thread (which adds them to SamplingStats: that stays single threaded). Clear
    /// (a city is loaded or left) forgets what waits and drops what is being made. Pure.
    /// </summary>
    public sealed class ShadowWorker
    {
        private readonly object m_Lock = new object();
        private readonly Queue<(ShadowJob Job, int Epoch)> m_Queue = new Queue<(ShadowJob, int)>();
        private readonly Queue<ShadowDone> m_Done = new Queue<ShadowDone>();
        private bool m_Running;
        private int m_Epoch, m_Outstanding;
        private long m_Submitted, m_Failed;
        private double m_WorkerMs;

        /// <summary>Jobs given and not yet taken: waiting, being made, or made and not taken.</summary>
        public int Outstanding
        {
            get
            {
                lock (m_Lock)
                    return m_Outstanding;
            }
        }

        public long Submitted
        {
            get
            {
                lock (m_Lock)
                    return m_Submitted;
            }
        }

        public long Failed
        {
            get
            {
                lock (m_Lock)
                    return m_Failed;
            }
        }

        /// <summary>The time the comparisons took on the thread (ms).</summary>
        public double WorkerMs
        {
            get
            {
                lock (m_Lock)
                    return m_WorkerMs;
            }
        }

        public void Submit(ShadowJob job)
        {
            bool start = false;
            lock (m_Lock)
            {
                m_Queue.Enqueue((job, m_Epoch));
                m_Submitted++;
                m_Outstanding++;
                if (!m_Running)
                {
                    m_Running = true;
                    start = true;
                }
            }

            if (start)
                Task.Run((Action)Loop);
        }

        public bool TryTake(out ShadowDone done)
        {
            lock (m_Lock)
            {
                if (m_Done.Count == 0)
                {
                    done = default;
                    return false;
                }

                done = m_Done.Dequeue();
                m_Outstanding--;
                return true;
            }
        }

        public void Clear()
        {
            lock (m_Lock)
            {
                m_Epoch++;
                m_Queue.Clear();
                m_Done.Clear();
                m_Outstanding = 0;
            }
        }

        private void Loop()
        {
            while (true)
            {
                (ShadowJob Job, int Epoch) next;
                lock (m_Lock)
                {
                    if (m_Queue.Count == 0)
                    {
                        m_Running = false;
                        return;
                    }

                    next = m_Queue.Dequeue();
                }

                var done = new ShadowDone();
                try
                {
                    done.Comparison = ShadowRunner.Run(next.Job);
                }
                catch (Exception ex)
                {
                    done.Error = ex;
                }

                lock (m_Lock)
                {
                    if (next.Epoch != m_Epoch)
                        continue;
                    m_WorkerMs += done.Comparison.ShadowMs;
                    if (done.Error != null)
                        m_Failed++;
                    m_Done.Enqueue(done);
                }
            }
        }
    }
}
