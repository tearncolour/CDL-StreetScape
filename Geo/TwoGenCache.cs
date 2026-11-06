using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A cache that holds at most twice its capacity and throws away what has not been used lately, without a list of uses: two generations. New and used entries go into the young one; when it is
    /// full it becomes the old one and the old one is dropped. An entry that is not used for a whole generation is gone; one that is used stays. Until 0.7.0 the caches of the meshes and of the
    /// laid-out boards were emptied altogether when they were full, so that a city with more kinds of board than the cache held built every one of them again at every tile. Not thread safe:
    /// the callers lock.
    /// </summary>
    public sealed class TwoGenCache<TKey, TValue>
    {
        private Dictionary<TKey, TValue> m_Young = new Dictionary<TKey, TValue>();
        private Dictionary<TKey, TValue> m_Old = new Dictionary<TKey, TValue>();
        private readonly int m_Capacity;

        public TwoGenCache(int capacity)
        {
            m_Capacity = capacity < 1 ? 1 : capacity;
        }

        public int Capacity => m_Capacity;

        /// <summary>How many entries are held (between the capacity and twice it once it has been filled).</summary>
        public int Count => m_Young.Count + m_Old.Count;

        public bool TryGet(TKey key, out TValue value)
        {
            if (m_Young.TryGetValue(key, out value))
                return true;
            if (m_Old.TryGetValue(key, out value))
            {
                // Used again: it moves to the young generation and stays.
                Set(key, value);
                return true;
            }

            return false;
        }

        public void Set(TKey key, TValue value)
        {
            if (!m_Young.ContainsKey(key) && m_Young.Count >= m_Capacity)
            {
                m_Old = m_Young;
                m_Young = new Dictionary<TKey, TValue>();
            }

            m_Young[key] = value;
            m_Old.Remove(key);
        }

        public void Clear()
        {
            m_Young.Clear();
            m_Old.Clear();
        }
    }

    /// <summary>
    /// TwoGenCache measured by what the entries weigh (the vertices of a marking's paint) instead of how many there are: the young generation turns old when its weight passes the budget, so at most twice the
    /// budget is held. A city's lines are each a different setting (their path), and a fixed count either held too few of them (a tile that changes detail as the camera moves made them all again) or, of big
    /// ones, too much. Not thread safe: the callers lock.
    /// </summary>
    public sealed class WeightedTwoGenCache<TKey, TValue>
    {
        private Dictionary<TKey, TValue> m_Young = new Dictionary<TKey, TValue>();
        private Dictionary<TKey, TValue> m_Old = new Dictionary<TKey, TValue>();
        private readonly long m_Budget;
        private readonly System.Func<TValue, long> m_Weight;
        private long m_YoungWeight, m_OldWeight;

        public WeightedTwoGenCache(long budget, System.Func<TValue, long> weight)
        {
            m_Budget = budget < 1 ? 1 : budget;
            m_Weight = weight;
        }

        public int Count => m_Young.Count + m_Old.Count;

        /// <summary>What is held now (at most twice the budget, and one entry more).</summary>
        public long Weight => m_YoungWeight + m_OldWeight;

        public bool TryGet(TKey key, out TValue value)
        {
            if (m_Young.TryGetValue(key, out value))
                return true;
            if (m_Old.TryGetValue(key, out value))
            {
                Set(key, value);
                return true;
            }

            return false;
        }

        public void Set(TKey key, TValue value)
        {
            long w = System.Math.Max(1L, m_Weight(value));
            if (m_Young.TryGetValue(key, out TValue had))
            {
                m_YoungWeight -= System.Math.Max(1L, m_Weight(had));
            }
            else if (m_YoungWeight + w > m_Budget && m_Young.Count > 0)
            {
                m_Old = m_Young;
                m_OldWeight = m_YoungWeight;
                m_Young = new Dictionary<TKey, TValue>();
                m_YoungWeight = 0;
            }

            m_Young[key] = value;
            m_YoungWeight += w;
            if (m_Old.TryGetValue(key, out TValue old))
            {
                m_Old.Remove(key);
                m_OldWeight -= System.Math.Max(1L, m_Weight(old));
            }
        }

        public void Clear()
        {
            m_Young.Clear();
            m_Old.Clear();
            m_YoungWeight = 0;
            m_OldWeight = 0;
        }
    }
}
