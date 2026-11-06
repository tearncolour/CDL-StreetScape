using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    public enum SlowWrite
    {
        No,
        First,
        Record
    }

    /// <summary>
    /// Which slow updates of the mod's systems are written to the log. The first few of each system are (what it was doing while a city loaded) and so is every later update that is a record: at least
    /// RecordFloorMs and RecordFactor times the system's worst slow update so far, written or not. 0.12.65's rule (the first 8 lines of a system) never told the worst frames of a session: the owner's
    /// 0.12.73 log had a tool frame of 141.5 ms and no line for it.
    /// </summary>
    public sealed class SlowPolicy
    {
        public const double RecordFloorMs = 30.0, RecordFactor = 1.5;

        private sealed class Entry
        {
            public int First, Records;
            public double Peak;
        }

        private readonly Dictionary<string, Entry> m_Systems = new Dictionary<string, Entry>();
        private readonly int m_FirstEach, m_RecordEach, m_FirstTotal, m_RecordTotal;
        private int m_FirstUsed, m_RecordUsed;

        public SlowPolicy(int firstEach = 6, int recordEach = 6, int firstTotal = 120, int recordTotal = 40)
        {
            m_FirstEach = firstEach;
            m_RecordEach = recordEach;
            m_FirstTotal = firstTotal;
            m_RecordTotal = recordTotal;
        }

        /// <summary>Whether this slow update (the caller has judged it slow) is written, and as what.</summary>
        public SlowWrite Judge(string system, double ms)
        {
            if (!m_Systems.TryGetValue(system, out Entry entry))
                m_Systems[system] = entry = new Entry();
            double before = entry.Peak;
            if (ms > before)
                entry.Peak = ms;
            if (entry.First < m_FirstEach && m_FirstUsed < m_FirstTotal)
            {
                entry.First++;
                m_FirstUsed++;
                return SlowWrite.First;
            }

            if (ms >= RecordFloorMs && ms >= before * RecordFactor && entry.Records < m_RecordEach && m_RecordUsed < m_RecordTotal)
            {
                entry.Records++;
                m_RecordUsed++;
                return SlowWrite.Record;
            }

            return SlowWrite.No;
        }
    }
}
