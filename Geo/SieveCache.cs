using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A cache bounded by a total weight, evicting by SIEVE (Zhang, Yang, Yue, Vigfusson, Rashmi: "SIEVE is Simpler than LRU", NSDI 2024). The entries are in one list from the newest (the head) to the
    /// oldest (the tail); a hit only sets the entry's visited bit. When room is needed a hand goes from where it stopped last (the tail at first) toward the newer entries, clears the visited bits it passes
    /// and evicts the first entry that was not visited, and starts again from the tail when it has gone past the head. A pass over many entries that are met once is evicted without pushing out
    /// the ones that are used again. An entry has a weight (bytes, or 1 for a count); the total never goes over the budget, and eviction goes on until a new entry fits. An entry that weighs more
    /// than the budget alone is not kept. Pure; not thread-safe (the surface cache is used from the main thread, GlyphCache keeps one under its lock); no allocation per call once the slots have grown to what is held (DESIGN-surface-cache.md).
    /// </summary>
    public sealed class SieveCache<TKey, TValue> where TKey : IEquatable<TKey>
    {
        private const int None = -1;

        private struct Node
        {
            public TKey Key;
            public TValue Value;
            public long Weight;
            public bool Visited;

            /// <summary>The next entry toward the head (the newer one); for a free slot the next free slot.</summary>
            public int Newer;

            public int Older;
        }

        private readonly Dictionary<TKey, int> m_Index = new Dictionary<TKey, int>();
        private Node[] m_Nodes = new Node[16];
        private int m_Slots, m_Free = None, m_Head = None, m_Tail = None, m_Hand = None;

        public SieveCache(long budget)
        {
            Budget = budget < 1 ? 1 : budget;
        }

        public long Budget { get; }

        /// <summary>The total weight of what is held.</summary>
        public long Weight { get; private set; }

        public int Count => m_Index.Count;

        /// <summary>How many calls of TryGet found an entry and how many did not (the counts go with Clear).</summary>
        public int Hits { get; private set; }

        public int Misses { get; private set; }

        public bool TryGet(TKey key, out TValue value)
        {
            if (m_Index.TryGetValue(key, out int at))
            {
                m_Nodes[at].Visited = true;
                value = m_Nodes[at].Value;
                Hits++;
                return true;
            }

            value = default;
            Misses++;
            return false;
        }

        /// <summary>
        /// Keeps a value with its weight, evicting what has to go to make room. An entry that is there with the same weight takes the new value and counts as visited; with another weight it is taken
        /// out and put in as a new entry. False when the weight is more than the whole budget (then nothing is evicted and nothing is kept, the old value of the key included).
        /// </summary>
        public bool Set(TKey key, TValue value, long weight = 1)
        {
            if (weight < 0)
                weight = 0;
            if (m_Index.TryGetValue(key, out int at))
            {
                if (m_Nodes[at].Weight == weight && weight <= Budget)
                {
                    m_Nodes[at].Value = value;
                    m_Nodes[at].Visited = true;
                    return true;
                }

                Remove(at);
            }

            if (weight > Budget)
                return false;
            while (Weight + weight > Budget)
                Evict();
            Insert(key, value, weight);
            return true;
        }

        public void Clear()
        {
            m_Index.Clear();
            Array.Clear(m_Nodes, 0, m_Slots);
            m_Slots = 0;
            m_Free = m_Head = m_Tail = m_Hand = None;
            Weight = 0;
            Hits = Misses = 0;
        }

        private void Evict()
        {
            int at = m_Hand != None ? m_Hand : m_Tail;
            while (m_Nodes[at].Visited)
            {
                m_Nodes[at].Visited = false;
                at = m_Nodes[at].Newer != None ? m_Nodes[at].Newer : m_Tail;
            }

            // The hand stays where the entry was: Remove moves it on to the newer one (none: it starts from the tail next time).
            m_Hand = at;
            Remove(at);
        }

        private void Insert(TKey key, TValue value, long weight)
        {
            int at;
            if (m_Free != None)
            {
                at = m_Free;
                m_Free = m_Nodes[at].Newer;
            }
            else
            {
                if (m_Slots == m_Nodes.Length)
                    Array.Resize(ref m_Nodes, m_Slots * 2);
                at = m_Slots++;
            }

            m_Nodes[at] = new Node { Key = key, Value = value, Weight = weight, Newer = None, Older = m_Head };
            if (m_Head != None)
                m_Nodes[m_Head].Newer = at;
            else
                m_Tail = at;
            m_Head = at;
            m_Index[key] = at;
            Weight += weight;
        }

        /// <summary>Takes an entry out of the list and the index; the hand, when it was on it, goes on to the newer one.</summary>
        private void Remove(int at)
        {
            Node node = m_Nodes[at];
            if (m_Hand == at)
                m_Hand = node.Newer;
            if (node.Older != None)
                m_Nodes[node.Older].Newer = node.Newer;
            else
                m_Tail = node.Newer;
            if (node.Newer != None)
                m_Nodes[node.Newer].Older = node.Older;
            else
                m_Head = node.Older;
            m_Index.Remove(node.Key);
            Weight -= node.Weight;
            m_Nodes[at] = new Node { Newer = m_Free };
            m_Free = at;
        }
    }
}
