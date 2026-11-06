using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The FollowCurve of each curve version, made once however many items and lines ask for it (0.12.74 follow work): keyed by the curve's fingerprint and checked against its numbers, so that a hash that
    /// collides is a miss and never another curve's table. Two generations bound it: when the newer holds Capacity tables it becomes the older and the older is dropped, so a drag that makes a new version of
    /// each edge every frame holds at most 2 x Capacity tables (about 1 KB each) and the versions of the last frames, which an undo or a drag back asks for again, are still there. One owner at a time (the
    /// tables it gives are immutable and may be read by any thread). Pure.
    /// </summary>
    public sealed class FollowCache
    {
        private readonly int m_Capacity;
        private Dictionary<ulong, FollowCurve> m_Newer = new Dictionary<ulong, FollowCurve>();
        private Dictionary<ulong, FollowCurve> m_Older = new Dictionary<ulong, FollowCurve>();

        public int Hits { get; private set; }

        public int Misses { get; private set; }

        public int Count => m_Newer.Count + m_Older.Count;

        public FollowCache(int capacity = 256) => m_Capacity = capacity < 1 ? 1 : capacity;

        public FollowCurve Get(Bezier3 curve)
        {
            ulong key = FollowCurve.PrintOf(curve);
            if (m_Newer.TryGetValue(key, out FollowCurve found) && found.IsCurve(curve))
            {
                Hits++;
                return found;
            }

            if (m_Older.TryGetValue(key, out found) && found.IsCurve(curve))
            {
                Hits++;
                Keep(key, found);
                return found;
            }

            Misses++;
            found = new FollowCurve(curve);
            Keep(key, found);
            return found;
        }

        private void Keep(ulong key, FollowCurve table)
        {
            if (m_Newer.Count >= m_Capacity)
            {
                Dictionary<ulong, FollowCurve> recycled = m_Older;
                m_Older = m_Newer;
                m_Newer = recycled;
                m_Newer.Clear();
            }

            m_Newer[key] = table;
        }

        public void Clear()
        {
            m_Newer.Clear();
            m_Older.Clear();
        }
    }
}
