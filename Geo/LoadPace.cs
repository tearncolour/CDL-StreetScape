using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// What the load's hold looked like in time (0.12.79): the owner's 0.12.78 hold took 6.3 s and its log could not say what it waited for. The gaps between the updates under the loading screen (a
    /// frame that did not come shows as a gap), the garbage collections that ran in the longest gap, when each source was last busy, and what waited every half second. Pure; the clock is given.
    /// </summary>
    public sealed class LoadPace
    {
        /// <summary>A gap between two updates this long (s) counts as a stall.</summary>
        public const double StallSeconds = 0.25;

        public const double SampleSeconds = 0.5;
        public const int MaxSamples = 40;

        /// <summary>The upper edges (s) of the buckets the gaps are counted in; the last bucket is everything above the last edge.</summary>
        private static readonly double[] kEdges = { 0.02, 0.04, 0.08, 0.16, 0.32 };

        private readonly Func<double> m_Now;
        private readonly int[] m_Buckets = new int[kEdges.Length + 1];
        private readonly List<string> m_Names = new List<string>();
        private readonly List<double> m_LastBusy = new List<double>();
        private readonly List<string> m_Samples = new List<string>();
        private double m_Start, m_Last, m_NextSample, m_Longest, m_LongestFrom;
        private int m_Updates, m_Stalls;
        private int[] m_Collections = new int[3], m_LongestCollections = new int[3];
        private bool m_Begun;

        public LoadPace(Func<double> now) => m_Now = now ?? throw new ArgumentNullException(nameof(now));

        public int Updates => m_Updates;

        /// <summary>Starts a hold: the first gap is counted from here. gen0, gen1 and gen2 are the game's collection counts now.</summary>
        public void Begin(int gen0, int gen1, int gen2)
        {
            m_Start = m_Last = m_Now();
            m_NextSample = 0.0;
            m_Longest = m_LongestFrom = 0.0;
            m_Updates = m_Stalls = 0;
            m_Collections = new[] { gen0, gen1, gen2 };
            m_LongestCollections = new int[3];
            Array.Clear(m_Buckets, 0, m_Buckets.Length);
            m_Names.Clear();
            m_LastBusy.Clear();
            m_Samples.Clear();
            m_Begun = true;
        }

        /// <summary>One update has begun: the gap since the one before is counted, and the collections that ran in it are kept when it is the longest.</summary>
        public void Tick(int gen0, int gen1, int gen2)
        {
            if (!m_Begun)
                return;
            double now = m_Now();
            double gap = Math.Max(0.0, now - m_Last);
            int bucket = 0;
            while (bucket < kEdges.Length && gap > kEdges[bucket])
                bucket++;
            m_Buckets[bucket]++;
            m_Updates++;
            if (gap >= StallSeconds)
                m_Stalls++;
            if (gap > m_Longest)
            {
                m_Longest = gap;
                m_LongestFrom = m_Last - m_Start;
                m_LongestCollections = new[] { gen0 - m_Collections[0], gen1 - m_Collections[1], gen2 - m_Collections[2] };
            }

            m_Collections = new[] { gen0, gen1, gen2 };
            m_Last = now;
        }

        /// <summary>What each source has waiting (-1: not known yet), in the order the sources were added; call once an update, after they were reported.</summary>
        public void Sample(IReadOnlyList<KeyValuePair<string, int>> sources)
        {
            if (!m_Begun)
                return;
            double t = m_Now() - m_Start;
            while (m_Names.Count < sources.Count)
            {
                m_Names.Add(sources[m_Names.Count].Key);
                m_LastBusy.Add(-1.0);
            }

            bool sample = t >= m_NextSample && m_Samples.Count < MaxSamples;
            var line = sample ? new StringBuilder(t.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s ") : null;
            for (int i = 0; i < sources.Count; i++)
            {
                int pending = sources[i].Value;
                if (pending != 0)
                    m_LastBusy[i] = t;
                if (sample)
                    line.Append(i > 0 ? "/" : "").Append(pending < 0 ? "?" : pending.ToString(CultureInfo.InvariantCulture));
            }

            if (!sample)
                return;
            m_Samples.Add(line.ToString());
            m_NextSample = (Math.Floor(t / SampleSeconds) + 1.0) * SampleSeconds;
        }

        /// <summary>One line for the log; empty when no update was seen.</summary>
        public string Describe()
        {
            if (!m_Begun || m_Updates == 0)
                return "";
            CultureInfo c = CultureInfo.InvariantCulture;
            double end = m_Now() - m_Start;
            var text = new StringBuilder();
            text.Append(m_Updates.ToString(c)).Append(" update(s) in ").Append(end.ToString("0.0", c)).Append(" s: half within ").Append(Within(0.5)).Append(" ms, 95 % within ").Append(Within(0.95))
                .Append(" ms, ").Append(m_Stalls.ToString(c)).Append(" gap(s) of ").Append((StallSeconds * 1000.0).ToString("0", c)).Append(" ms or more, the longest ").Append((m_Longest * 1000.0).ToString("0", c))
                .Append(" ms from ").Append(m_LongestFrom.ToString("0.0", c)).Append(" s (garbage collections in it, generation 0/1/2: ").Append(m_LongestCollections[0].ToString(c)).Append('/')
                .Append(m_LongestCollections[1].ToString(c)).Append('/').Append(m_LongestCollections[2].ToString(c)).Append(')');

            int last = -1;
            double lastAt = -1.0;
            var busy = new StringBuilder();
            for (int i = 0; i < m_Names.Count; i++)
            {
                if (m_LastBusy[i] < 0.0)
                    continue;
                busy.Append(busy.Length > 0 ? ", " : "").Append(m_Names[i]).Append(' ').Append(m_LastBusy[i].ToString("0.0", c)).Append(" s");
                if (m_LastBusy[i] > lastAt)
                {
                    lastAt = m_LastBusy[i];
                    last = i;
                }
            }

            if (last >= 0)
                text.Append("; last busy: ").Append(busy).Append("; the last was ").Append(m_Names[last]).Append(", the gate went on ").Append(Math.Max(0.0, end - lastAt).ToString("0.00", c)).Append(" s after it");
            if (m_Samples.Count > 0)
                text.Append("; waiting (").Append(string.Join("/", m_Names)).Append(") every ").Append(SampleSeconds.ToString("0.0", c)).Append(" s: ").Append(string.Join(", ", m_Samples));
            return text.ToString();
        }

        /// <summary>The smallest edge (ms) that holds the given share of the gaps; ">320" when it is in the last bucket.</summary>
        private string Within(double share)
        {
            int total = 0;
            foreach (int count in m_Buckets)
                total += count;
            int seen = 0;
            for (int i = 0; i < kEdges.Length; i++)
            {
                seen += m_Buckets[i];
                if (seen >= share * total)
                    return (kEdges[i] * 1000.0).ToString("0", CultureInfo.InvariantCulture);
            }

            return ">" + (kEdges[kEdges.Length - 1] * 1000.0).ToString("0", CultureInfo.InvariantCulture);
        }
    }
}
