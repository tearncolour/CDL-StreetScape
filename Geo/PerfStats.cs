using System;
using System.Globalization;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// What one system's updates cost over a stretch of time, for the log's summary: how many, their sum and worst, how many were over 1 ms and over 8 ms, and a histogram that answers "what share of
    /// the updates is under this": the 0.12.73 log had only the average and the worst, and the owner's goal (every system under 1 ms a frame on the main thread) lies between the two. Add allocates
    /// nothing: it runs every frame for every system.
    /// </summary>
    public sealed class PerfStats
    {
        /// <summary>The upper edges of the histogram's buckets (ms); a time of an edge or more is in the next bucket, and the last bucket has no upper edge.</summary>
        private static readonly double[] s_Edges = { 0.05, 0.1, 0.2, 0.3, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 12.0, 16.0, 24.0, 32.0, 64.0, 128.0 };

        private readonly int[] m_Buckets = new int[s_Edges.Length + 1];

        public int Calls { get; private set; }

        public int OverOneMs { get; private set; }

        public int OverEightMs { get; private set; }

        public double TotalMs { get; private set; }

        public double MaxMs { get; private set; }

        public double AverageMs => Calls > 0 ? TotalMs / Calls : 0.0;

        public void Add(double ms)
        {
            if (double.IsNaN(ms) || ms < 0.0)
                ms = 0.0;
            Calls++;
            TotalMs += ms;
            if (ms > MaxMs)
                MaxMs = ms;
            if (ms >= 1.0)
                OverOneMs++;
            if (ms >= 8.0)
                OverEightMs++;
            int bucket = 0;
            while (bucket < s_Edges.Length && ms >= s_Edges[bucket])
                bucket++;
            m_Buckets[bucket]++;
        }

        /// <summary>
        /// A time that at least the share p (0 to 1) of the updates did not exceed: the upper edge of the bucket the p-th update is in, so the true value is at most this (the worst, when it is in the
        /// last bucket, which has no edge). 0 with no update.
        /// </summary>
        public double PercentileUpperMs(double p)
        {
            if (Calls == 0)
                return 0.0;
            long need = (long)Math.Ceiling(Math.Min(1.0, Math.Max(0.0, p)) * Calls);
            if (need < 1)
                need = 1;
            long seen = 0;
            for (int i = 0; i < m_Buckets.Length; i++)
            {
                seen += m_Buckets[i];
                if (seen >= need)
                    return i < s_Edges.Length ? Math.Min(s_Edges[i], MaxMs) : MaxMs;
            }

            return MaxMs;
        }

        /// <summary>Adds what another set of stats holds (the window into the session's).</summary>
        public void Merge(PerfStats other)
        {
            Calls += other.Calls;
            OverOneMs += other.OverOneMs;
            OverEightMs += other.OverEightMs;
            TotalMs += other.TotalMs;
            if (other.MaxMs > MaxMs)
                MaxMs = other.MaxMs;
            for (int i = 0; i < m_Buckets.Length; i++)
                m_Buckets[i] += other.m_Buckets[i];
        }

        public void Reset()
        {
            Calls = 0;
            OverOneMs = 0;
            OverEightMs = 0;
            TotalMs = 0.0;
            MaxMs = 0.0;
            Array.Clear(m_Buckets, 0, m_Buckets.Length);
        }

        /// <summary>The share of the updates that took 1 ms or more, in percent.</summary>
        public double OverOnePercent => Calls > 0 ? 100.0 * OverOneMs / Calls : 0.0;

        /// <summary>
        /// One line for the summary: the load (ms of main thread a second the system takes: its sum over the seconds of the window), the average, the 50th, 95th and 99th percentile (upper bounds),
        /// the worst, and how many updates were over 1 ms (with their share) and over 8 ms. A star marks a system over the owner's goal: more than 1 % of its updates take 1 ms or more.
        /// </summary>
        public string Line(string system, double windowSeconds, string worstText = "")
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            double load = windowSeconds > 0.0 ? TotalMs / windowSeconds : 0.0;
            return string.Format(c,
                "{0}{1}: {2} call(s), load {3:0.0} ms/s, average {4:0.00}, p50 <= {5:0.##}, p95 <= {6:0.##}, p99 <= {7:0.##}, worst {8:0.0} ms{9}, over 1 ms {10} ({11:0.0} %), over 8 ms {12}",
                OverOnePercent > 1.0 ? "* " : "", system, Calls, load, AverageMs, PercentileUpperMs(0.5), PercentileUpperMs(0.95), PercentileUpperMs(0.99), MaxMs, worstText, OverOneMs, OverOnePercent, OverEightMs);
        }
    }
}
