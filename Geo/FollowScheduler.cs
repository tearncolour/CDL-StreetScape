using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>One item to follow, at the version of its road's curve the follow is to make it for.</summary>
    public struct FollowJob
    {
        public int Item;
        public int Version;
    }

    /// <summary>
    /// When the items of a road that changed are made to follow it (0.12.74 follow work): an event says an edge has a new curve version, the pool (edge to its items) turns it into pending items at once (one
    /// hash lookup, nothing is polled), and each frame the scheduler hands out as many of them as a budget allows, the ones nearest the camera or the cursor first, a line's items always together. An item that is
    /// pending when a newer version of its road comes is not queued twice: it is made for the newest (coalesced), so a drag that makes a version every frame costs one follow of an item per frame it is taken,
    /// not one per version. The clock is the caller's: a cost in ns for each item (measured offline, or the game's slow-frame figures) is charged against the budget, so the schedule is a pure function of the
    /// events, the focus and the numbers given. At least one item (or one whole line) is taken whenever any is pending, so that nothing waits for ever. Two ways to use it: Take for work done on the main thread
    /// (follow and write in one go), or TakeAll for a worker to follow and Deliver / ApplyReady for the main thread to write what the worker made (the results of items that a newer version superseded are dropped).
    /// One owner at a time, no statics. Pure.
    /// </summary>
    public sealed class FollowScheduler
    {
        /// <summary>The distance rings the priority is bucketed in (up to 25, 50, 100, 200, 400, 800, 1600 m and beyond): no sort, one pass.</summary>
        public const int Rings = 8;

        private struct Item
        {
            public int Group, Tile;
            public float X, Z, CostNs;
            public int Want, Done, Ready, InFlight;
            public int PendingAt, ReadyAt, WroteAt;
            public bool IsPending, IsReady;
            public bool Live;
        }

        private Item[] m_Items = new Item[16];
        private readonly Dictionary<int, List<int>> m_Pool = new Dictionary<int, List<int>>();
        private readonly Dictionary<int, List<int>> m_Groups = new Dictionary<int, List<int>>();
        private readonly List<int> m_Pending = new List<int>();
        private readonly List<int> m_Ready = new List<int>();
        private readonly List<int>[] m_Bucket;
        private readonly List<int>[] m_ReadyBucket;
        private float m_CameraX, m_CameraZ, m_CursorX, m_CursorZ;
        private bool m_HasCursor;
        private int m_Frame;
        private readonly Dictionary<int, TileState> m_TileState = new Dictionary<int, TileState>();
        private readonly List<int> m_Touched = new List<int>();
        private readonly List<int> m_TouchedStart = new List<int>();
        private readonly List<bool> m_TouchedRemaining = new List<bool>();
        private readonly Dictionary<int, int> m_TouchedAt = new Dictionary<int, int>();
        private readonly Dictionary<int, int> m_Rank = new Dictionary<int, int>();
        private readonly Dictionary<int, float> m_KeyD2 = new Dictionary<int, float>();
        private double[] m_SortKeys = new double[64];
        private int[] m_SortIds = new int[64];
        private List<int>[] m_RankBucket, m_RankReadyBucket;
        private int m_LastEventFrame = int.MinValue / 2;
        private readonly Dictionary<int, GroupInfo> m_GroupInfo = new Dictionary<int, GroupInfo>();

        /// <summary>What a pick finds out about a line once: whether any of its items waits, and the nearest bucket one of them is in.</summary>
        private struct GroupInfo
        {
            public bool Waiting;
            public int Nearest;
        }

        private struct TileState
        {
            public int BurstStart, LastEnd;
            public bool Bursting;
        }

        /// <summary>
        /// The gate on the items of one tile of the map (0: none): the items of a tile are written in a burst, and the next burst is not before this many frames after the last write of the one before. The renderer
        /// builds a tile on a worker and drops the result if an item of the tile changed meanwhile, so a tile whose items are written every frame is never shown anew until the writes stop; with a gate longer than a
        /// build the tile is shown at every burst. Items of a closed tile wait (and go on coalescing: they are written for the newest version when the gate opens). A burst the budget or the cap cut goes on in the
        /// next frames without waiting for the gate.
        /// </summary>
        public int TileGateFrames { get; set; }

        /// <summary>The most buckets a priority order by tiles has: the tiles farther than the sixty-third nearest share the last.</summary>
        public const int MaxRanks = 64;

        /// <summary>
        /// The order is by the tile an item is in (the nearest tile of the camera first, the tile's items together, one tile after another) and not by each item's own distance: a tile is drawn when it is
        /// built, whole, so an item written ahead of the others of its tile is not seen sooner, and a tile finished is one rebuild earlier. Items with no tile are ranked by their own distance among the tiles.
        /// </summary>
        public bool TileOrder { get; set; }

        /// <summary>A line with more items than this is taken a few at a time (each is a unit of its own, in the order they wait), not all in one frame: 0 keeps every line whole.</summary>
        public int AtomicGroupMax { get; set; }

        /// <summary>The budget (ns of the costs) the items within NearMetres of the camera may have over the budget given: what is looked at is followed first and faster, the rest at the base rate.</summary>
        public double NearBoostNs { get; set; }

        /// <summary>How far from the camera an item (or its tile's nearest edge) is near, for NearBoostNs (m).</summary>
        public float NearMetres { get; set; } = 200f;

        /// <summary>The frames since an event reached an item of the pool: a road that is dragged has one every frame, a road that was dropped none.</summary>
        public int QuietFrames(int frame) => frame - m_LastEventFrame;

        /// <summary>The south-west corner of a tile the scheduler is given as FollowWatch.TileOf packs it (x and z of the tile, 4096 added to each, 13 bits each).</summary>
        public static void TileOrigin(int tile, out float x, out float z)
        {
            x = ((tile >> 13) - 4096) * Tiles.Size;
            z = ((tile & 8191) - 4096) * Tiles.Size;
        }

        /// <summary>An item that has waited this many frames is taken before any that has not, whatever its distance: the ones far from the camera are not starved by a drag that goes on.</summary>
        public int MaxWaitFrames { get; set; } = 30;

        /// <summary>How many times an event found its item pending already and only raised the version it is to be made for.</summary>
        public long Coalesced { get; private set; }

        /// <summary>How many items wait to be followed.</summary>
        public int PendingCount => m_Pending.Count;

        /// <summary>How many results wait to be written.</summary>
        public int ReadyCount => m_Ready.Count;

        public int ItemCount { get; private set; }

        public FollowScheduler()
        {
            m_Bucket = new List<int>[Rings];
            m_ReadyBucket = new List<int>[Rings];
            for (int i = 0; i < Rings; i++)
            {
                m_Bucket[i] = new List<int>();
                m_ReadyBucket[i] = new List<int>();
            }
        }

        /// <summary>An item of the pool: its place (for the priority), the cost of following it (ns) and its group (a line's items share a non-zero group and are taken together; 0 is none).</summary>
        public void Add(int item, int group, float x, float z, float costNs, int tile = -1)
        {
            if (item >= m_Items.Length)
                Array.Resize(ref m_Items, Math.Max(item + 1, m_Items.Length * 2));
            if (m_Items[item].Live)
                Remove(item);
            m_Items[item] = new Item { Group = group, Tile = tile, X = x, Z = z, CostNs = costNs, Live = true, WroteAt = int.MinValue / 2 };
            ItemCount++;
            if (group != 0)
            {
                if (!m_Groups.TryGetValue(group, out List<int> members))
                    m_Groups[group] = members = new List<int>();
                members.Add(item);
            }
        }

        /// <summary>The item stands by this road (a line item by each road it runs along): an event of the road reaches it.</summary>
        public void Link(int item, int edge)
        {
            if (!m_Pool.TryGetValue(edge, out List<int> items))
                m_Pool[edge] = items = new List<int>();
            if (!items.Contains(item))
                items.Add(item);
        }

        public void Remove(int item)
        {
            if (item < 0 || item >= m_Items.Length || !m_Items[item].Live)
                return;
            Item gone = m_Items[item];
            if (gone.Group != 0 && m_Groups.TryGetValue(gone.Group, out List<int> members))
            {
                members.Remove(item);
                if (members.Count == 0)
                    m_Groups.Remove(gone.Group);
            }

            foreach (KeyValuePair<int, List<int>> pair in m_Pool)
                pair.Value.Remove(item);
            m_Pending.Remove(item);
            m_Ready.Remove(item);
            m_Items[item] = default;
            ItemCount--;
        }

        /// <summary>The place of an item changed (a write moved it): the priority uses it.</summary>
        public void Moved(int item, float x, float z)
        {
            m_Items[item].X = x;
            m_Items[item].Z = z;
        }

        /// <summary>The camera and the cursor, where the items nearest are followed first.</summary>
        public void SetFocus(float cameraX, float cameraZ, bool hasCursor, float cursorX, float cursorZ)
        {
            m_CameraX = cameraX;
            m_CameraZ = cameraZ;
            m_HasCursor = hasCursor;
            m_CursorX = cursorX;
            m_CursorZ = cursorZ;
        }

        /// <summary>The edge has a new curve: its items are pending for this version (one lookup of the pool). How many items were found.</summary>
        public int EdgeChanged(int edge, int version, int frame)
        {
            m_Frame = frame;
            if (!m_Pool.TryGetValue(edge, out List<int> items))
                return 0;
            if (items.Count > 0)
                m_LastEventFrame = frame;
            foreach (int item in items)
            {
                if (version <= m_Items[item].Want)
                    continue;
                m_Items[item].Want = version;
                if (m_Items[item].IsPending)
                {
                    Coalesced++;
                    continue;
                }

                m_Items[item].IsPending = true;
                m_Items[item].PendingAt = frame;
                m_Pending.Add(item);
            }

            return items.Count;
        }

        /// <summary>The frame the item has waited since (for a lag figure), or -1 when it is not pending.</summary>
        public int PendingSince(int item) => m_Items[item].IsPending ? m_Items[item].PendingAt : -1;

        private int RingOf(Item item, int frame, bool ready)
        {
            if (frame - (ready ? item.ReadyAt : item.PendingAt) >= MaxWaitFrames)
                return 0;
            float d2 = Distance2(item);
            if (d2 <= 625f)
                return 0;
            if (d2 <= 2500f)
                return 1;
            if (d2 <= 10000f)
                return 2;
            if (d2 <= 40000f)
                return 3;
            if (d2 <= 160000f)
                return 4;
            if (d2 <= 640000f)
                return 5;
            return d2 <= 2560000f ? 6 : 7;
        }

        /// <summary>The squared distance (m) of an item from the camera or the cursor, whichever is nearer: its own, or in a tile order that of its tile's nearest edge.</summary>
        private float Distance2(in Item item)
        {
            if (TileOrder && item.Tile >= 0)
            {
                TileOrigin(item.Tile, out float x0, out float z0);
                float cx = Outside(m_CameraX, x0), cz = Outside(m_CameraZ, z0), d2 = cx * cx + cz * cz;
                if (m_HasCursor)
                {
                    float ux = Outside(m_CursorX, x0), uz = Outside(m_CursorZ, z0);
                    d2 = Math.Min(d2, ux * ux + uz * uz);
                }

                return d2;
            }

            float dx = item.X - m_CameraX, dz = item.Z - m_CameraZ, own = dx * dx + dz * dz;
            if (m_HasCursor)
            {
                float ex = item.X - m_CursorX, ez = item.Z - m_CursorZ;
                own = Math.Min(own, ex * ex + ez * ez);
            }

            return own;
        }

        /// <summary>How far a coordinate is outside the tile's span [origin, origin + Tiles.Size] (0 inside it).</summary>
        private static float Outside(float v, float origin) => v < origin ? origin - v : v > origin + Tiles.Size ? v - origin - Tiles.Size : 0f;

        private static int KeyOf(int item, in Item it) => it.Tile >= 0 ? it.Tile : -(item + 1);

        /// <summary>The bucket an item is taken from: its ring, or in a tile order the rank of its tile; an item that has waited long is in the first.</summary>
        private int BucketOf(int item, in Item it, int frame, bool ready)
        {
            if (!TileOrder)
                return RingOf(it, frame, ready);
            if (frame - (ready ? it.ReadyAt : it.PendingAt) >= MaxWaitFrames)
                return 0;
            return Math.Min(m_Rank[KeyOf(item, it)], MaxRanks - 1);
        }

        private List<int>[] RankBuckets(bool ready)
        {
            if (m_RankBucket == null)
            {
                m_RankBucket = new List<int>[MaxRanks];
                m_RankReadyBucket = new List<int>[MaxRanks];
                for (int i = 0; i < MaxRanks; i++)
                {
                    m_RankBucket[i] = new List<int>();
                    m_RankReadyBucket[i] = new List<int>();
                }
            }

            return ready ? m_RankReadyBucket : m_RankBucket;
        }

        /// <summary>Ranks the tiles (and the items with no tile) of a list by their distance, nearest first: one sort of the few distinct ones.</summary>
        private void RankTiles(List<int> list)
        {
            m_KeyD2.Clear();
            foreach (int item in list)
            {
                int key = KeyOf(item, m_Items[item]);
                if (!m_KeyD2.TryGetValue(key, out _))
                    m_KeyD2.Add(key, Distance2(m_Items[item]));
            }

            int n = m_KeyD2.Count;
            if (m_SortKeys.Length < n)
            {
                m_SortKeys = new double[Math.Max(n, m_SortKeys.Length * 2)];
                m_SortIds = new int[m_SortKeys.Length];
            }

            int i = 0;
            foreach (KeyValuePair<int, float> pair in m_KeyD2)
            {
                m_SortKeys[i] = pair.Value + (double)(pair.Key & 0xFFFFF) * 1e-7;
                m_SortIds[i++] = pair.Key;
            }

            Array.Sort(m_SortKeys, m_SortIds, 0, n);
            m_Rank.Clear();
            for (i = 0; i < n; i++)
                m_Rank[m_SortIds[i]] = i;
        }

        /// <summary>
        /// The pending items to follow now on the main thread: ring by ring from the focus, in the order they became pending within a ring, a line's items together (a line is in the ring of its nearest
        /// item), until the next would pass budgetNs (summed costs) or cap items; the first is always taken. The items are in flight until Wrote (follow and write done) or Cancel. How many were taken.
        /// </summary>
        public int Take(int frame, double budgetNs, int cap, List<FollowJob> into)
        {
            m_Frame = frame;
            return Pick(m_Pending, m_Bucket, false, true, frame, budgetNs, cap, into);
        }

        /// <summary>The pending items for a worker to follow: all of them up to cap (no budget: the worker's time is not the frame's), in the same order.</summary>
        public int TakeAll(int frame, int cap, List<FollowJob> into)
        {
            m_Frame = frame;
            return Pick(m_Pending, m_Bucket, false, false, frame, double.MaxValue, cap, into);
        }

        /// <summary>The worker made the item's result for this version: it waits to be written (a result of a version an earlier result already passed is dropped).</summary>
        public void Deliver(int item, int version, int frame)
        {
            m_Frame = frame;
            if (!m_Items[item].Live)
                return;
            // A newer follow of the item is under way already: this result is of a version that is superseded.
            if (version < m_Items[item].InFlight)
                return;
            m_Items[item].InFlight = 0;
            if (version <= m_Items[item].Done || version < m_Items[item].Ready)
                return;
            m_Items[item].Ready = version;
            if (!m_Items[item].IsReady)
            {
                m_Items[item].IsReady = true;
                m_Items[item].ReadyAt = frame;
                m_Ready.Add(item);
            }

            // A newer version came while the worker was at it: the item follows again, for that one.
            if (m_Items[item].Want > version && !m_Items[item].IsPending)
            {
                m_Items[item].IsPending = true;
                m_Items[item].PendingAt = frame;
                m_Pending.Add(item);
            }
        }

        /// <summary>The results to write now on the main thread, nearest first, within budgetNs of writeNs each and cap items, a line together; the first is always taken. Written is to be called for each.</summary>
        public int ApplyReady(int frame, double writeNs, double budgetNs, int cap, List<FollowJob> into)
        {
            m_Frame = frame;
            return Pick(m_Ready, m_ReadyBucket, true, true, frame, budgetNs, cap, into, writeNs);
        }

        private int Pick(List<int> list, List<int>[] buckets, bool ready, bool gated, int frame, double budgetNs, int cap, List<FollowJob> into, double fixedNs = -1.0)
        {
            if (list.Count == 0 || cap <= 0)
                return 0;
            if (TileOrder)
            {
                buckets = RankBuckets(ready);
                RankTiles(list);
            }

            for (int r = 0; r < buckets.Length; r++)
                buckets[r].Clear();
            // One pass: each item to the bucket of its nearest member when it has a group.
            m_Touched.Clear();
            m_GroupInfo.Clear();
            foreach (int item in list)
            {
                ref Item it = ref m_Items[item];
                if (gated && !GateOpen(it, frame))
                    continue;
                int ring = BucketOf(item, it, frame, ready);
                if (it.Group != 0)
                {
                    if (!m_GroupInfo.TryGetValue(it.Group, out GroupInfo info))
                    {
                        info = new GroupInfo { Nearest = int.MaxValue };
                        if (m_Groups.TryGetValue(it.Group, out List<int> members))
                        {
                            foreach (int other in members)
                            {
                                if (!(ready ? m_Items[other].IsReady : m_Items[other].IsPending))
                                    continue;
                                info.Waiting = true;
                                info.Nearest = Math.Min(info.Nearest, BucketOf(other, m_Items[other], frame, ready));
                            }
                        }

                        m_GroupInfo[it.Group] = info;
                    }

                    // A long line that is taken a few items at a time goes tile by tile with the rest: each of its items by its own tile.
                    if (info.Waiting && !Sliced(it.Group))
                        ring = Math.Min(ring, info.Nearest);
                }

                buckets[ring].Add(item);
            }

            int taken = 0;
            double spent = 0.0;
            bool stop = false;
            float nearD2 = NearMetres * NearMetres;
            for (int r = 0; r < buckets.Length && !stop; r++)
            {
                foreach (int item in buckets[r])
                {
                    if (!(ready ? m_Items[item].IsReady : m_Items[item].IsPending) || (gated && !GateOpen(m_Items[item], frame)))
                        continue;
                    // The item and the other waiting members of its line: all or none (a long line is taken a few at a time: AtomicGroupMax).
                    int unit = 1;
                    double cost = fixedNs >= 0.0 ? fixedNs : m_Items[item].CostNs;
                    List<int> members = null;
                    if (m_Items[item].Group != 0 && m_Groups.TryGetValue(m_Items[item].Group, out members))
                    {
                        unit = 0;
                        cost = 0.0;
                        foreach (int other in members)
                        {
                            if (ready ? m_Items[other].IsReady : m_Items[other].IsPending)
                            {
                                unit++;
                                cost += fixedNs >= 0.0 ? fixedNs : m_Items[other].CostNs;
                            }
                        }

                        if (Sliced(m_Items[item].Group))
                        {
                            members = null;
                            unit = 1;
                            cost = fixedNs >= 0.0 ? fixedNs : m_Items[item].CostNs;
                        }
                    }

                    double limit = NearBoostNs > 0.0 && Distance2(m_Items[item]) <= nearD2 ? budgetNs + NearBoostNs : budgetNs;
                    if (taken > 0 && (spent + cost > limit || taken + unit > cap))
                    {
                        stop = true;
                        break;
                    }

                    if (members == null)
                        Release(item, ready, gated, into);
                    else
                    {
                        foreach (int other in members)
                        {
                            if (ready ? m_Items[other].IsReady : m_Items[other].IsPending)
                                Release(other, ready, gated, into);
                        }
                    }

                    taken += unit;
                    spent += cost;
                    if (taken >= cap)
                    {
                        stop = true;
                        break;
                    }
                }
            }

            // A tile written to starts a burst when it had none; the burst is over when every item that was waiting at its start was written, and the gate is closed from then.
            if (gated && TileGateFrames > 0 && m_Touched.Count > 0)
            {
                m_TouchedStart.Clear();
                m_TouchedRemaining.Clear();
                m_TouchedAt.Clear();
                for (int i = 0; i < m_Touched.Count; i++)
                {
                    m_TileState.TryGetValue(m_Touched[i], out TileState state);
                    m_TouchedStart.Add(state.Bursting ? state.BurstStart : frame);
                    m_TouchedRemaining.Add(false);
                    m_TouchedAt[m_Touched[i]] = i;
                }

                foreach (int item in list)
                {
                    Item it = m_Items[item];
                    if (it.Tile >= 0 && (ready ? it.IsReady : it.IsPending) && m_TouchedAt.TryGetValue(it.Tile, out int at) && it.WroteAt < m_TouchedStart[at])
                        m_TouchedRemaining[at] = true;
                }

                for (int i = 0; i < m_Touched.Count; i++)
                {
                    m_TileState.TryGetValue(m_Touched[i], out TileState state);
                    state.BurstStart = m_TouchedStart[i];
                    state.Bursting = m_TouchedRemaining[i];
                    if (!state.Bursting)
                        state.LastEnd = frame;
                    m_TileState[m_Touched[i]] = state;
                }
            }

            // The items that were released leave the list in one pass.
            int kept = 0;
            for (int i = 0; i < list.Count; i++)
            {
                int item = list[i];
                if (ready ? m_Items[item].IsReady : m_Items[item].IsPending)
                    list[kept++] = item;
            }

            list.RemoveRange(kept, list.Count - kept);
            return taken;
        }

        /// <summary>Whether a line is taken a few items at a time: it has more items than AtomicGroupMax (all of them, not the ones that wait: a line does not become whole as its items are written).</summary>
        private bool Sliced(int group) => AtomicGroupMax > 0 && m_Groups.TryGetValue(group, out List<int> members) && members.Count > AtomicGroupMax;

        /// <summary>Whether the item may be written now: its tile has no gate, or is in a burst and the item was not written in it yet, or the gate has been open since the last burst ended.</summary>
        private bool GateOpen(in Item item, int frame)
        {
            if (TileGateFrames <= 0 || item.Tile < 0 || !m_TileState.TryGetValue(item.Tile, out TileState state))
                return true;
            return state.Bursting ? item.WroteAt < state.BurstStart : frame - state.LastEnd >= TileGateFrames;
        }

        private void Release(int item, bool ready, bool write, List<FollowJob> into)
        {
            int tile = m_Items[item].Tile;
            if (tile >= 0 && !m_Touched.Contains(tile))
                m_Touched.Add(tile);
            if (write)
                m_Items[item].WroteAt = m_Frame;
            if (ready)
            {
                into.Add(new FollowJob { Item = item, Version = m_Items[item].Ready });
                m_Items[item].IsReady = false;
            }
            else
            {
                m_Items[item].IsPending = false;
                m_Items[item].InFlight = m_Items[item].Want;
                into.Add(new FollowJob { Item = item, Version = m_Items[item].Want });
            }
        }

        /// <summary>The item was followed and written for this version (on the main thread, or from a worker's result): it has nothing to do until a newer version of its road comes. Returns whether one has.</summary>
        public bool Wrote(int item, int version)
        {
            m_Items[item].Done = Math.Max(m_Items[item].Done, version);
            m_Items[item].InFlight = 0;
            if (m_Items[item].Want > m_Items[item].Done && !m_Items[item].IsPending && !m_Items[item].IsReady)
            {
                m_Items[item].IsPending = true;
                m_Items[item].PendingAt = m_Frame;
                m_Pending.Add(item);
                return true;
            }

            return false;
        }

        /// <summary>The version the item was last written for.</summary>
        public int DoneVersion(int item) => m_Items[item].Done;

        /// <summary>Nothing is waiting: every item is written for the newest version of its road.</summary>
        public bool Settled => m_Pending.Count == 0 && m_Ready.Count == 0;

        /// <summary>Whether any item of the group (a line's pieces) waits to be followed or to be written: the follow of the line is not over.</summary>
        public bool GroupWaiting(int group)
        {
            if (!m_Groups.TryGetValue(group, out List<int> members))
                return false;
            foreach (int member in members)
            {
                if (m_Items[member].IsPending || m_Items[member].IsReady)
                    return true;
            }

            return false;
        }

        /// <summary>Whether items of the tile are being written now (a burst that the budget or the cap has cut goes on in the next frames): a build of the tile that starts meanwhile is dropped when the next item is written.</summary>
        public bool IsBursting(int tile) => m_TileState.TryGetValue(tile, out TileState state) && state.Bursting;
    }
}
