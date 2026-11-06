using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>What to do with an item whose road was deleted (RoadGone.Verdict).</summary>
    public enum RoadGoneVerdict
    {
        /// <summary>The item stays: a road is in reach (the road was replaced, split or widened, or another is beside it), or it is not an item of a road.</summary>
        Keep,

        /// <summary>No road is in reach: the item goes with its road.</summary>
        Remove
    }

    /// <summary>
    /// Items that lose their road (0.12.72; the owner: "放置了我们景物的道路删除后，放置的对应条目也应该删除而不是留在原地"; 0.12.73: "删除不应该轮询，应该使用哈希池存储，删除某个道路之后立刻触发查找删除").
    /// The pool is a hash of the roads, each with the set of its items: ItemRoadSystem tells which road an item stands by whenever it finds the road it names holding or finds another (NoteLinked), so an
    /// item that was never linked (the orphans of an old save) is never in the pool and is never removed. Nothing asks the roads whether they are still there: the system is told by the game, which marks
    /// an edge Deleted for the rest of the frame, and EdgeDied moves that road's items out of the pool and into the queue at once (one lookup of the hash, no scan of the items); Drain decides them in
    /// the same update, at most MaxPerUpdate, the rest in the next ones. Items are not saved with this, and the pool is cleared when a city is loaded. Pure, main thread.
    /// </summary>
    public sealed class RoadGone<TItem, TEdge> where TItem : IEquatable<TItem> where TEdge : IEquatable<TEdge>
    {
        /// <summary>At most this many items are decided in one update (the rest come in the next ones): a big road must not make a hitch. The removals of one update are one step of Ctrl+Z.</summary>
        public const int MaxPerUpdate = 20;

        /// <summary>An item whose road died, with that road (for the log).</summary>
        public struct Waiting
        {
            public TItem Item;
            public TEdge Road;
        }

        private readonly Dictionary<TItem, TEdge> m_RoadOf = new Dictionary<TItem, TEdge>();
        private readonly Dictionary<TEdge, HashSet<TItem>> m_ItemsOf = new Dictionary<TEdge, HashSet<TItem>>();
        private readonly HashSet<TItem> m_Queued = new HashSet<TItem>();
        private readonly Queue<Waiting> m_Queue = new Queue<Waiting>();
        private readonly List<TItem> m_Dead = new List<TItem>();

        /// <summary>How many items are known to stand by a road (linked this session).</summary>
        public int Linked => m_RoadOf.Count;

        /// <summary>How many roads have items in the pool.</summary>
        public int Roads => m_ItemsOf.Count;

        /// <summary>How many items wait to be decided.</summary>
        public int Waits => m_Queue.Count;

        /// <summary>The item stands by this road: it is found beside the road it names, or it was given another. Counts it as linked for the rest of the session.</summary>
        public void NoteLinked(TItem item, TEdge road)
        {
            if (m_RoadOf.TryGetValue(item, out TEdge was))
            {
                if (was.Equals(road))
                    return;
                Leave(item, was);
            }

            m_RoadOf[item] = road;
            if (!m_ItemsOf.TryGetValue(road, out HashSet<TItem> items))
                m_ItemsOf[road] = items = new HashSet<TItem>();
            items.Add(item);
        }

        /// <summary>The item has no road any more that is known (it was found beside none, or it is gone): it leaves the pool. An item that is not in it is left alone.</summary>
        public void Forget(TItem item)
        {
            if (m_RoadOf.TryGetValue(item, out TEdge road))
                Leave(item, road);
        }

        private void Leave(TItem item, TEdge road)
        {
            m_RoadOf.Remove(item);
            if (m_ItemsOf.TryGetValue(road, out HashSet<TItem> items) && items.Remove(item) && items.Count == 0)
                m_ItemsOf.Remove(road);
        }

        /// <summary>Takes the items that are not alive (destroyed by the player, by undo, by a load) out of the pool, so that it does not grow with every item that was ever made. How many.</summary>
        public int Prune(Func<TItem, bool> alive)
        {
            m_Dead.Clear();
            foreach (TItem item in m_RoadOf.Keys)
            {
                if (!alive(item))
                    m_Dead.Add(item);
            }

            foreach (TItem item in m_Dead)
                Forget(item);
            int dead = m_Dead.Count;
            m_Dead.Clear();
            return dead;
        }

        /// <summary>
        /// The road is gone: its items leave the pool and wait to be decided, now (an item that waits already is not queued twice). One lookup of the hash, whatever the number of items in the city.
        /// How many items were queued.
        /// </summary>
        public int EdgeDied(TEdge road)
        {
            if (!m_ItemsOf.TryGetValue(road, out HashSet<TItem> items))
                return 0;
            m_ItemsOf.Remove(road);
            int queued = 0;
            foreach (TItem item in items)
            {
                m_RoadOf.Remove(item);
                if (m_Queued.Add(item))
                {
                    m_Queue.Enqueue(new Waiting { Item = item, Road = road });
                    queued++;
                }
            }

            return queued;
        }

        /// <summary>What to do with an item whose road died: Keep or Remove (an item that is gone, or one that a road is beside now, is Keep).</summary>
        public delegate RoadGoneVerdict Decide(Waiting waiting);

        /// <summary>
        /// The items that wait, the one that waited longest first, at most max of them (the rest are done in the next update): each is decided; one to Remove goes to removed. How many were decided.
        /// </summary>
        public int Drain(int max, Decide decide, List<Waiting> removed)
        {
            int decided = 0;
            while (decided < max && m_Queue.Count > 0)
            {
                Waiting waiting = m_Queue.Dequeue();
                m_Queued.Remove(waiting.Item);
                decided++;
                if (decide(waiting) == RoadGoneVerdict.Remove)
                    removed.Add(waiting);
            }

            return decided;
        }

        /// <summary>
        /// What the look at an item found. An item that is not an item of a road (exempt: a sign on a building's wall stands where it was put, whatever happens to the roads) or has a road in reach
        /// stays; with neither, it goes with its road.
        /// </summary>
        public static RoadGoneVerdict Verdict(bool exempt, bool roadInReach) => exempt || roadInReach ? RoadGoneVerdict.Keep : RoadGoneVerdict.Remove;

        /// <summary>Forgets everything (a city is loaded: its items and roads are not these).</summary>
        public void Clear()
        {
            m_RoadOf.Clear();
            m_ItemsOf.Clear();
            m_Queued.Clear();
            m_Queue.Clear();
            m_Dead.Clear();
        }
    }
}
