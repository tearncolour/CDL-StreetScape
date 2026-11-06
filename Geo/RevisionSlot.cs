using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// One value kept for as long as a revision counter stands still (the road network's: NetRevision) and while it is not full: asked with a newer revision it is made again. The junction signs' reading
    /// builds a graph of the roads from the game (62 car lanes and their turns: 2 to 4 ms on the main thread, 16 ms the first time in the owner's 0.12.73 log) every time the cursor crossed a 6 m cell;
    /// the graph is kept, and only the walk over it, which depends on where the sign is, is made again.
    /// </summary>
    public sealed class RevisionSlot<T> where T : class
    {
        private T m_Value;
        private int m_Revision;

        public int Made { get; private set; }

        public int Hits { get; private set; }

        /// <summary>The value for this revision: the kept one when the revision is the same and it is not full (the caller's size limit), else a new one from make. A make that throws keeps the old value as it was.</summary>
        public T Get(int revision, Func<T> make, Func<T, bool> full = null)
        {
            if (m_Value != null && m_Revision == revision && (full == null || !full(m_Value)))
            {
                Hits++;
                return m_Value;
            }

            T fresh = make();
            m_Value = fresh;
            m_Revision = revision;
            Made++;
            return fresh;
        }

        /// <summary>Lets go of the value (a read failed half way: what it held may be partial).</summary>
        public void Clear() => m_Value = null;
    }
}
