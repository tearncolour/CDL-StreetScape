using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A small cache whose entries are good for a time (0.7.1): the tool reads what the game keeps about the road under the cursor (its curve, its speed limit, its lanes, a few dozen entity reads and
    /// some strings) at every frame the cursor is over it, and all of it changes once in minutes, not once in 16 ms. An entry older than the time to live is made again; at most max entries are held
    /// (the expired ones go first, and when none has expired the cache starts again). The clock is the caller's (seconds), so that it is tested without waiting. Not thread safe: main thread.
    /// </summary>
    public sealed class ExpiringCache<TKey, TValue> where TValue : class
    {
        private struct Slot
        {
            public TValue Value;
            public float At;
        }

        private readonly Dictionary<TKey, Slot> m_Slots = new Dictionary<TKey, Slot>();
        private readonly float m_Ttl;
        private readonly int m_Max;
        private List<TKey> m_Scratch;

        public ExpiringCache(float timeToLiveSeconds, int max)
        {
            m_Ttl = timeToLiveSeconds;
            m_Max = max < 1 ? 1 : max;
        }

        public int Count => m_Slots.Count;

        /// <summary>How many calls found a good entry, and how many had to make one.</summary>
        public int Hits { get; private set; }

        public int Misses { get; private set; }

        /// <summary>The value for a key: the one that was made less than the time to live ago, or a new one from make (whose result is kept).</summary>
        public TValue Get(TKey key, float now, Func<TValue> make)
        {
            if (m_Slots.TryGetValue(key, out Slot slot) && now - slot.At < m_Ttl && now >= slot.At)
            {
                Hits++;
                return slot.Value;
            }

            Misses++;
            TValue value = make();
            if (!m_Slots.ContainsKey(key) && m_Slots.Count >= m_Max)
                MakeRoom(now);
            m_Slots[key] = new Slot { Value = value, At = now };
            return value;
        }

        /// <summary>Forgets one entry (what it describes changed).</summary>
        public void Remove(TKey key) => m_Slots.Remove(key);

        public void Clear() => m_Slots.Clear();

        private void MakeRoom(float now)
        {
            m_Scratch = m_Scratch ?? new List<TKey>();
            m_Scratch.Clear();
            foreach (KeyValuePair<TKey, Slot> pair in m_Slots)
            {
                if (now - pair.Value.At >= m_Ttl || now < pair.Value.At)
                    m_Scratch.Add(pair.Key);
            }

            foreach (TKey key in m_Scratch)
                m_Slots.Remove(key);
            if (m_Slots.Count >= m_Max)
                m_Slots.Clear();
        }
    }
}
