using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// What is asked for now and done at another time (NextFrameUpdateSystem: the entities that are given Updated at the first modification phase of the next frame): each once, in the order asked, and
    /// only those still alive when it is taken. Main thread.
    /// </summary>
    public sealed class Deferred<T>
    {
        private readonly List<T> m_Items = new List<T>();
        private readonly HashSet<T> m_Seen = new HashSet<T>();

        public int Count => m_Items.Count;

        public void Add(T item)
        {
            if (m_Seen.Add(item))
                m_Items.Add(item);
        }

        public List<T> Take(Func<T, bool> alive)
        {
            var taken = new List<T>(m_Items.Count);
            foreach (T item in m_Items)
            {
                if (alive(item))
                    taken.Add(item);
            }

            Clear();
            return taken;
        }

        public void Clear()
        {
            m_Items.Clear();
            m_Seen.Clear();
        }
    }
}
