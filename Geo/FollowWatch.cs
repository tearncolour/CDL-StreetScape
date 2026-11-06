using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Stage 1 of the road follow (0.12.74, DESIGN-follow-road.md): what the items of a road WOULD do when the road's curve changes, worked out and reported, never written. An item is linked to its roads when the
    /// system finds it beside them (the relation is measured then, against the curve then, and kept as it is); when the game says a road was updated the items of that road are queued once each (a drag that
    /// updates a road every frame does not queue an item that is waiting already) and followed a few at a time within the caller's time; the outcome of each is counted in a report that is handed out when the
    /// burst is over. The tables are FollowCurve (one per curve, shared by the items of a road), the single items go through FollowBatch and the lines through LineFollow on a copy of their relation, so that
    /// nothing a follow would have written is remembered as written. Pure: the system gives it the roads' curves and the items' places by delegates. One owner, the main thread.
    /// Stage 2 and 3 (Writing: roadFollow on): the same follow is run for the items a FollowScheduler hands out (the nearest to the camera and the cursor first, a group together, a tile's items in bursts with a gate
    /// between them, a budget a frame) and what it finds is given back as FollowWrite for the system to write; the relation is kept as it was measured (the road's shape changes, the relation does not) and
    /// remembers only where the item was put (so that an item moved by someone else is told from one that was put), which makes a road moved away and back give every item its place again.
    /// </summary>
    public sealed class FollowWatch<TItem, TEdge> where TItem : IEquatable<TItem> where TEdge : IEquatable<TEdge>
    {
        /// <summary>The longest a line that was followed may have grown (share of its length): over it the chords are too long for the text and the sag of a bend is too big, and the line is for a rebuild.</summary>
        public const float MaxGrowth = FollowGate.LengthChange;

        /// <summary>A burst that has lasted this many frames is reported though it is not over (a drag goes on for as long as the player drags).</summary>
        public const int MaxBurstFrames = 60;

        /// <summary>Where the item is now: false when it is gone. The path is wanted (and given) only for a line.</summary>
        public delegate bool PlaceOf(TItem item, bool wantPath, out Vec3 position, out float yaw, out string path);

        /// <summary>Where a host (a lamp post, a signal pole, a building) is now and which way it faces: false when it is gone or has no place.</summary>
        public delegate bool HostPoseOf(TEdge host, out Vec3 position, out float yaw);

        private sealed class Entry
        {
            public TEdge Hint;
            public TEdge[] Roads;
            public FollowCurve[] Baselines;

            /// <summary>A single item's relation.</summary>
            public RoadRelation Relation;

            /// <summary>A line's; null for a single item.</summary>
            public LineRelation Line;

            /// <summary>A road of the item is gone or has no shape: the relation is of no use until the item is linked again.</summary>
            public bool Stale;

            /// <summary>Where the item was when it was measured, on the baseline curve: what a rigid motion of the road carries (the relation's PlacedAt moves with each write).</summary>
            public Vec3 BaseAt;

            public float BaseYaw;

            /// <summary>The scheduler's number of the item (Writing only), and its group.</summary>
            public int Id = -1;

            public int Group;

            /// <summary>What the scheduler was told this item costs (ns).</summary>
            public float Cost;

            /// <summary>The item is fixed to an object that is not a road (the hint is that object): the pose it had when the item was linked.</summary>
            public bool Hosted;

            public Vec3 HostPos;

            public float HostYaw;

            /// <summary>
            /// A piece of a run (a wall) as the phase of its settings has it: where it starts along the whole line and the line's length (cm; -1: it has none), the length it had when the phase was made, and its
            /// length now (m). The pieces of a run are the entries of one group that have a phase; see LinePhases.
            /// </summary>
            public long PhaseCm = -1, TotalCm;

            public float LengthAtPhase, LengthNow;
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly TEdge Edge;
            public readonly FollowCurve Baseline;

            public EdgeKey(TEdge edge, FollowCurve baseline)
            {
                Edge = edge;
                Baseline = baseline;
            }

            public bool Equals(EdgeKey other) => Edge.Equals(other.Edge) && ReferenceEquals(Baseline, other.Baseline);

            public override bool Equals(object other) => other is EdgeKey key && Equals(key);

            public override int GetHashCode() => Edge.GetHashCode() * 31 + RuntimeHelpers.GetHashCode(Baseline);
        }

        private readonly Dictionary<TItem, Entry> m_Entries = new Dictionary<TItem, Entry>();
        private readonly Dictionary<TEdge, HashSet<TItem>> m_ItemsOf = new Dictionary<TEdge, HashSet<TItem>>();
        private readonly Queue<TEdge> m_Dirty = new Queue<TEdge>();
        private readonly Queue<TItem> m_Pending = new Queue<TItem>();
        private readonly HashSet<TItem> m_Queued = new HashSet<TItem>();
        private readonly FollowBatch m_Batch = new FollowBatch();
        private readonly LineWorkspace m_Work = new LineWorkspace();
        private readonly Dictionary<EdgeKey, int> m_BatchEdges = new Dictionary<EdgeKey, int>();
        private readonly Dictionary<TEdge, FollowCurve> m_Now = new Dictionary<TEdge, FollowCurve>();
        private readonly Dictionary<int, List<TItem>> m_PhasedGroups = new Dictionary<int, List<TItem>>();
        private readonly HashSet<int> m_PhaseDirty = new HashSet<int>(), m_PhaseDone = new HashSet<int>();
        private readonly Dictionary<TItem, int> m_WriteAt = new Dictionary<TItem, int>();
        private readonly List<Entry> m_BatchEntries = new List<Entry>();
        private readonly List<TItem> m_BatchItems = new List<TItem>();
        private readonly List<Vec3> m_BatchAt = new List<Vec3>();
        private readonly List<float> m_BatchYaw = new List<float>();
        private readonly List<TItem> m_Dead = new List<TItem>();
        private FollowCurve[] m_LineNows = new FollowCurve[8];
        private FollowReport m_Report = new FollowReport();

        // Writing: the scheduler and the numbers it knows the items and the roads by.
        private FollowScheduler m_Scheduler = new FollowScheduler();
        private readonly Dictionary<TEdge, int> m_EdgeIds = new Dictionary<TEdge, int>();
        private readonly Dictionary<int, int> m_EdgeDirtyAt = new Dictionary<int, int>();
        private readonly List<TItem> m_ItemOfId = new List<TItem>();
        private readonly Stack<int> m_FreeIds = new Stack<int>();
        private readonly List<FollowJob> m_Jobs = new List<FollowJob>();
        private List<FollowWrite<TItem>> m_Writes;
        private int m_Version;

        /// <summary>
        /// The cost (ns) the scheduler charges for following and writing a single item and a line, until the caller's measure says another. Until 0.12.82 these were 2.5 and 12 microseconds, what the pure
        /// follow costs; on the main thread of the owner's game an item cost 130 microseconds on average (his log of 3 Nov: 814 ms in 886 frames with work for 6,283 items looked at; 100 to 500 a written item),
        /// so the pacing, which can correct the costs by 20 times at most, ran 2 to 3 times over its budget and the first frame of a burst was far over.
        /// </summary>
        public const float SingleNs = 100000f, LineNs = 150000f;

        /// <summary>Whether the follow writes (the roadFollow preference on): set before any item is linked; the items are then known to a FollowScheduler.</summary>
        public bool Writing { get; set; }

        /// <summary>The items and the costs (ns, as the scheduler was told them) of the last StepWrite: the system compares them with the time it took, to keep its budget in real time.</summary>
        public int LastTaken { get; private set; }

        public double LastCostNs { get; private set; }

        /// <summary>The time the last StepWrite spent picking the items (ms): the scheduler's own cost, which the budget does not count.</summary>
        public double LastTakeMs { get; private set; }

        /// <summary>The scheduler of a Writing watch: its TileGateFrames, MaxWaitFrames and focus are the system's to set.</summary>
        public FollowScheduler Scheduler => m_Scheduler;

        /// <summary>The totals since the city was loaded.</summary>
        public FollowTotals Totals { get; private set; } = new FollowTotals();

        /// <summary>How many items are linked (have a relation).</summary>
        public int Count => m_Entries.Count;

        /// <summary>Nothing waits: no road to look up and no item to follow.</summary>
        public bool Settled => m_Dirty.Count == 0 && m_Pending.Count == 0 && (!Writing || m_Scheduler.Settled);

        /// <summary>Whether the item is linked to this road as its hint and its relation is usable: the relation is measured once and kept, so an item that is linked already is not measured again.</summary>
        public bool IsLinkedTo(TItem item, TEdge hint) => m_Entries.TryGetValue(item, out Entry entry) && !entry.Stale && entry.Hint.Equals(hint);

        /// <summary>The item stands beside this road (curve table baseline, its own place and yaw): its relation is measured now. False when the road has no shape or the place is not a number.</summary>
        public bool LinkSingle(TItem item, TEdge edge, FollowCurve baseline, Vec3 position, float yaw, int group = 0)
        {
            Forget(item);
            if (baseline == null || !baseline.TryMeasure(position, yaw, -1f, out RoadRelation relation, out _))
            {
                Totals.LinkFailed++;
                return false;
            }

            Register(item, new Entry { Hint = edge, Roads = new[] { edge }, Baselines = new[] { baseline }, Relation = relation, BaseAt = position, BaseYaw = yaw, Group = group }, position, SingleNs);
            Totals.SinglesLinked++;
            if (Math.Abs(relation.Height) > RoadLevels.LevelGap)
                Totals.NoteOtherLevel(item + " on " + edge, relation.Height, false);
            return true;
        }

        /// <summary>The line item runs along these roads (their tables as they are now; hint is the road the item names): each point's relation to them is measured now. False when the path is not a line or a road has no shape.</summary>
        public bool LinkLine(TItem item, TEdge hint, TEdge[] roads, FollowCurve[] baselines, string path, Vec3 position, float yaw, int group = 0, long phaseCm = -1, long totalCm = 0)
        {
            Forget(item);
            if (!LineFollow.TryMeasure(path, position, yaw, baselines, roads.Length, m_Work, out LineRelation line))
            {
                Totals.LinkFailed++;
                return false;
            }

            float length = LineFollow.WrittenLength(line);
            bool phased = group != 0 && phaseCm >= 0 && totalCm > 0;
            Register(item, new Entry { Hint = hint, Roads = roads, Baselines = baselines, Line = line, Group = group, PhaseCm = phased ? phaseCm : -1, TotalCm = totalCm, LengthAtPhase = length, LengthNow = length }, position, LineNs);
            if (phased)
            {
                if (!m_PhasedGroups.TryGetValue(group, out List<TItem> members))
                    m_PhasedGroups[group] = members = new List<TItem>();
                members.Add(item);
            }

            Totals.LinesLinked++;
            Totals.StackedPoints += line.Stacked;
            float farthest = 0f;
            for (int j = 0; j < line.Count; j++)
                farthest = Math.Abs(line.Height[j]) > Math.Abs(farthest) ? line.Height[j] : farthest;
            if (Math.Abs(farthest) > RoadLevels.LevelGap)
                Totals.NoteOtherLevel(item + " on " + hint, farthest, true);
            return true;
        }

        /// <summary>
        /// The item is fixed to a host (a lamp post, a signal pole, a building: it goes with the host, not with a road): the host's pose and the item's are kept as they are now, and when the host's pose
        /// changes the item is put where the same motion takes it (the relation is fixed, so a host moved away and back gives the item its place again). The host is a key of the same pool as a road:
        /// Dirty(host) says its pose changed, EdgeGone(host) that it is gone. False when a pose is not a number.
        /// </summary>
        public bool LinkHosted(TItem item, TEdge host, Vec3 hostPosition, float hostYaw, Vec3 position, float yaw, int group = 0)
        {
            Forget(item);
            if (!hostPosition.IsFinite || !position.IsFinite || float.IsNaN(hostYaw) || float.IsInfinity(hostYaw) || float.IsNaN(yaw) || float.IsInfinity(yaw))
            {
                Totals.LinkFailed++;
                return false;
            }

            Register(item, new Entry
            {
                Hint = host, Roads = new[] { host }, Baselines = new FollowCurve[1], Relation = default(RoadRelation).Placed(position, yaw), BaseAt = position, BaseYaw = yaw,
                Hosted = true, HostPos = hostPosition, HostYaw = hostYaw, Group = group
            }, position, SingleNs);
            Totals.HostedLinked++;
            return true;
        }

        /// <summary>Whether the item is fixed to a host and the link is of use (the host is not gone).</summary>
        public bool IsHostLinked(TItem item) => m_Entries.TryGetValue(item, out Entry entry) && entry.Hosted && !entry.Stale;

        private void Register(TItem item, Entry entry, Vec3 at, float costNs)
        {
            m_Entries[item] = entry;
            foreach (TEdge road in entry.Roads)
            {
                if (!m_ItemsOf.TryGetValue(road, out HashSet<TItem> items))
                    m_ItemsOf[road] = items = new HashSet<TItem>();
                items.Add(item);
            }

            if (!Writing)
                return;
            entry.Id = m_FreeIds.Count > 0 ? m_FreeIds.Pop() : m_ItemOfId.Count;
            if (entry.Id == m_ItemOfId.Count)
                m_ItemOfId.Add(item);
            else
                m_ItemOfId[entry.Id] = item;
            entry.Cost = costNs;
            m_Scheduler.Add(entry.Id, entry.Group, at.X, at.Z, costNs, TileOf(at));
            foreach (TEdge road in entry.Roads)
                m_Scheduler.Link(entry.Id, EdgeId(road));
        }

        /// <summary>The number of the tile of the map a place is in, for the gate (never negative: the map is a few hundred tiles across).</summary>
        public static int TileOf(Vec3 at) => TileOf(Tiles.Of(at.X, at.Z));

        /// <summary>The number of a tile of the map (FollowScheduler.TileOrigin is its inverse).</summary>
        public static int TileOf(TileKey key) => (Math.Max(0, Math.Min(8191, key.X + 4096)) << 13) | Math.Max(0, Math.Min(8191, key.Z + 4096));

        private int EdgeId(TEdge edge)
        {
            if (!m_EdgeIds.TryGetValue(edge, out int id))
                m_EdgeIds[edge] = id = m_EdgeIds.Count;
            return id;
        }

        /// <summary>The item is not one of a road any more (an orphan, a wall sign it became, destroyed): its relation goes. An item that is not linked is left alone.</summary>
        public void Forget(TItem item)
        {
            if (!m_Entries.TryGetValue(item, out Entry entry))
                return;
            m_Entries.Remove(item);
            foreach (TEdge road in entry.Roads)
            {
                if (m_ItemsOf.TryGetValue(road, out HashSet<TItem> items) && items.Remove(item) && items.Count == 0)
                    m_ItemsOf.Remove(road);
            }

            if (entry.PhaseCm >= 0 && m_PhasedGroups.TryGetValue(entry.Group, out List<TItem> pieces) && pieces.Remove(item))
            {
                // The pieces that are left are a shorter run: their phases are not made again for a run that is cut.
                if (pieces.Count == 0)
                    m_PhasedGroups.Remove(entry.Group);
                else
                    m_PhaseDirty.Remove(entry.Group);
            }

            if (entry.Id >= 0)
            {
                m_Scheduler.Remove(entry.Id);
                m_ItemOfId[entry.Id] = default;
                m_FreeIds.Push(entry.Id);
            }
        }

        /// <summary>
        /// Whether the system is to leave the item alone for now: a road of it was updated within holdFrames, or it waits for its follow. While a road is dragged its items trail it by metres, and a check that
        /// looks for the road beside the item would take them for orphans and drop their relation.
        /// </summary>
        public bool Holds(TItem item, int frame, int holdFrames)
        {
            if (!Writing || !m_Entries.TryGetValue(item, out Entry entry) || entry.Id < 0)
                return false;
            if (m_Scheduler.PendingSince(entry.Id) >= 0)
                return true;
            foreach (TEdge road in entry.Roads)
            {
                if (m_EdgeIds.TryGetValue(road, out int id) && m_EdgeDirtyAt.TryGetValue(id, out int at) && frame - at <= holdFrames)
                    return true;
            }

            return false;
        }

        /// <summary>The road is gone: its items have no road to follow until they are linked again (the system links those that a road is in reach of at once).</summary>
        public void EdgeGone(TEdge edge)
        {
            if (!m_ItemsOf.TryGetValue(edge, out HashSet<TItem> items))
                return;
            m_ItemsOf.Remove(edge);
            foreach (TItem item in items)
            {
                if (m_Entries.TryGetValue(item, out Entry entry))
                    entry.Stale = true;
            }
        }

        /// <summary>Takes the items that are not alive out (destroyed by the player, by undo, by a load). How many.</summary>
        public int Prune(Func<TItem, bool> alive)
        {
            m_Dead.Clear();
            foreach (TItem item in m_Entries.Keys)
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

        /// <summary>The game says the road was updated (its curve may be another): its items are looked for in the next Step. One append, no lookup.</summary>
        public void Dirty(TEdge edge) => m_Dirty.Enqueue(edge);

        /// <summary>
        /// The roads that were said to change give their items to the queue, and as many queued items as maxItems and outOfTime allow (the first is always taken) are followed, in the order they were queued: a
        /// single item through the batch, a line on its roads' tables. curveOf gives a road's table now (null when it is gone or has no shape: its items go stale). True when an item was followed (a road without items of ours costs a lookup and is not work worth a report).
        /// </summary>
        public bool Step(Func<TEdge, FollowCurve> curveOf, PlaceOf placeOf, FollowMode mode, int maxItems, Func<bool> outOfTime)
        {
            m_Now.Clear();
            int edges = 0;
            while (m_Dirty.Count > 0)
            {
                TEdge edge = m_Dirty.Dequeue();
                if (!m_ItemsOf.ContainsKey(edge))
                    Totals.UpdatedNoItems++;
                if (m_ItemsOf.TryGetValue(edge, out HashSet<TItem> items))
                {
                    m_Report.Edges++;
                    foreach (TItem item in items)
                    {
                        if (m_Queued.Add(item))
                            m_Pending.Enqueue(item);
                    }
                }

                if (++edges % 256 == 0 && outOfTime())
                    break;
            }

            m_Batch.Clear();
            m_BatchEdges.Clear();
            m_BatchEntries.Clear();
            m_BatchItems.Clear();
            m_BatchAt.Clear();
            m_BatchYaw.Clear();
            int taken = 0;
            while (m_Pending.Count > 0 && taken < maxItems)
            {
                if (taken > 0 && outOfTime())
                    break;
                TItem item = m_Pending.Dequeue();
                m_Queued.Remove(item);
                if (!m_Entries.TryGetValue(item, out Entry entry))
                    continue;
                if (entry.Stale)
                {
                    m_Report.Stale++;
                    continue;
                }

                taken++;
                if (entry.Hosted)
                    continue;
                if (entry.Line == null)
                    Gather(item, entry, curveOf, placeOf);
                else
                    FollowLine(item, entry, curveOf, placeOf, mode);
            }

            RunBatch(mode);
            return taken > 0;
        }

        /// <summary>
        /// One frame of a Writing watch: the roads that were said to change give their items to the scheduler (one lookup each, a new version for each), the scheduler hands out the items to follow in this frame
        /// (nearest the focus first, a group together, a tile only when its gate is open, within budgetNs of the costs it was given and cap items, the first always), each is followed as Step follows it, and what an
        /// item is to be written as is added to writes (the relation of a written item remembers the place it was put at). True when an item was followed.
        /// </summary>
        public bool StepWrite(Func<TEdge, FollowCurve> curveOf, PlaceOf placeOf, FollowMode mode, int frame, double budgetNs, int cap, List<FollowWrite<TItem>> writes, HostPoseOf hostPoseOf = null)
        {
            m_Now.Clear();
            while (m_Dirty.Count > 0)
            {
                TEdge edge = m_Dirty.Dequeue();
                if (!m_EdgeIds.TryGetValue(edge, out int edgeId))
                {
                    Totals.UpdatedNoItems++;
                    continue;
                }

                m_EdgeDirtyAt[edgeId] = frame;
                if (m_Scheduler.EdgeChanged(edgeId, ++m_Version, frame) > 0)
                    m_Report.Edges++;
            }

            m_Jobs.Clear();
            long pickStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int taken = m_Scheduler.Take(frame, budgetNs, cap, m_Jobs);
            LastTakeMs = (System.Diagnostics.Stopwatch.GetTimestamp() - pickStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            LastTaken = taken;
            LastCostNs = 0.0;
            if (taken == 0)
                return false;
            m_Writes = writes;
            m_Batch.Clear();
            m_BatchEdges.Clear();
            m_BatchEntries.Clear();
            m_BatchItems.Clear();
            m_BatchAt.Clear();
            m_BatchYaw.Clear();
            foreach (FollowJob job in m_Jobs)
            {
                TItem item = m_ItemOfId[job.Item];
                if (!m_Entries.TryGetValue(item, out Entry entry))
                    continue;
                LastCostNs += entry.Cost;
                if (entry.Stale)
                {
                    m_Report.Stale++;
                    continue;
                }

                if (entry.Hosted)
                    FollowHosted(item, entry, hostPoseOf, placeOf);
                else if (entry.Line == null)
                    Gather(item, entry, curveOf, placeOf);
                else
                    FollowLine(item, entry, curveOf, placeOf, mode);
            }

            RunBatch(mode);
            RebasePhases();
            m_Writes = null;
            foreach (FollowJob job in m_Jobs)
                m_Scheduler.Wrote(job.Item, job.Version);
            return true;
        }

        /// <summary>An item fixed to a host: the motion that takes the host's pose when the item was linked to its pose now takes the item from where it was linked.</summary>
        private void FollowHosted(TItem item, Entry entry, HostPoseOf hostPoseOf, PlaceOf placeOf)
        {
            m_Report.Hosted++;
            if (hostPoseOf == null || !hostPoseOf(entry.Hint, out Vec3 hostAt, out float hostYaw) || !hostAt.IsFinite || float.IsNaN(hostYaw))
            {
                entry.Stale = true;
                m_Report.AddSingle(FollowOutcome.Unmeasurable, 0f, 0f, null);
                return;
            }

            if (!placeOf(item, false, out Vec3 at, out float yaw, out _))
                return;
            RoadRelation placed = entry.Relation;
            if ((at - placed.PlacedAt).Length > RoadRelation.PlacedTolerance || Math.Abs(RoadRelation.Wrap(yaw - placed.PlacedYaw)) > 1e-3f)
            {
                // Moved by someone else: fixed to the host again where it stands.
                entry.HostPos = hostAt;
                entry.HostYaw = hostYaw;
                entry.BaseAt = at;
                entry.BaseYaw = yaw;
                entry.Relation = placed.Placed(at, yaw);
                m_Report.AddSingle(FollowOutcome.Detached, 0f, 0f, null);
                return;
            }

            RigidMotion motion = RigidMotion.Between(entry.HostPos, entry.HostYaw, hostAt, hostYaw);
            Vec3 target = motion.Map(entry.BaseAt);
            float turned = motion.MapYaw(entry.BaseYaw);
            if ((target - at).Length <= RoadRelation.WriteTolerance && Math.Abs(RoadRelation.Wrap(turned - yaw)) <= 1e-4f)
            {
                m_Report.AddSingle(FollowOutcome.Unchanged, 0f, 0f, null);
                return;
            }

            float move = (target - at).Length;
            float bound = FollowGate.Bound((hostAt - entry.HostPos).Length, Vec3.DistanceXZ(entry.BaseAt, entry.HostPos), RoadRelation.Wrap(hostYaw - entry.HostYaw));
            m_Report.AddSingle(FollowOutcome.Moved, move, bound, move > bound ? item + " (fixed to a host)" : null);
            if (m_Writes == null)
                return;
            m_Writes.Add(new FollowWrite<TItem> { Item = item, Position = target, Yaw = turned });
            entry.Relation = placed.Placed(target, turned);
            m_Report.Written++;
            if (entry.Id >= 0)
                m_Scheduler.Moved(entry.Id, target.X, target.Z);
        }

        private FollowCurve Now(TEdge edge, Func<TEdge, FollowCurve> curveOf)
        {
            if (!m_Now.TryGetValue(edge, out FollowCurve now))
                m_Now[edge] = now = curveOf(edge);
            return now;
        }

        private void Gather(TItem item, Entry entry, Func<TEdge, FollowCurve> curveOf, PlaceOf placeOf)
        {
            FollowCurve now = Now(entry.Roads[0], curveOf);
            if (now == null || !now.IsUsable)
            {
                entry.Stale = true;
                m_Report.AddSingle(FollowOutcome.Unmeasurable, 0f, 0f, null);
                return;
            }

            if (!placeOf(item, false, out Vec3 at, out float yaw, out _))
                return;
            var key = new EdgeKey(entry.Roads[0], entry.Baselines[0]);
            if (!m_BatchEdges.TryGetValue(key, out int edgeIndex))
            {
                edgeIndex = m_Batch.AddEdge(entry.Baselines[0]);
                m_Batch.SetNow(edgeIndex, now, true);
                m_BatchEdges[key] = edgeIndex;
            }

            m_Batch.Add(edgeIndex, entry.Relation, entry.BaseAt, entry.BaseYaw, at, yaw);
            m_BatchEntries.Add(entry);
            m_BatchItems.Add(item);
            m_BatchAt.Add(at);
            m_BatchYaw.Add(yaw);
        }

        /// <summary>Runs the batch and counts what each single item would do; an item that someone moved is measured again where it stands.</summary>
        private void RunBatch(FollowMode mode)
        {
            if (m_Batch.Count == 0)
                return;
            m_Batch.Run(mode, 0, m_Batch.Count);
            for (int i = 0; i < m_Batch.Count; i++)
            {
                Entry entry = m_BatchEntries[i];
                FollowOutcome outcome = m_Batch.Result(i, out Vec3 position, out float yaw);
                float move = 0f, bound = 0f, controls = 0f, turned = 0f;
                if (outcome == FollowOutcome.Moved)
                {
                    FollowCurve now = m_Now[entry.Roads[0]];
                    move = (position - m_BatchAt[i]).Length;
                    controls = FollowGate.ControlMove(entry.Baselines[0].Curve, now.Curve);
                    turned = RoadRelation.Wrap(yaw - entry.Relation.PlacedYaw);
                    bound = FollowGate.Bound(controls, entry.Relation.Lateral, turned, entry.Relation.Along);
                }
                else if (outcome == FollowOutcome.Detached && m_Now[entry.Roads[0]].TryMeasure(m_BatchAt[i], m_BatchYaw[i], -1f, out RoadRelation again, out _))
                {
                    entry.Relation = again;
                    entry.Baselines[0] = m_Now[entry.Roads[0]];
                    entry.BaseAt = m_BatchAt[i];
                    entry.BaseYaw = m_BatchYaw[i];
                }

                if (outcome == FollowOutcome.Moved && m_Writes != null)
                {
                    m_Writes.Add(new FollowWrite<TItem> { Item = m_BatchItems[i], Position = position, Yaw = yaw });
                    entry.Relation = entry.Relation.Placed(position, yaw);
                    m_Report.Written++;
                    if (entry.Id >= 0)
                        m_Scheduler.Moved(entry.Id, position.X, position.Z);
                }

                m_Report.AddSingle(outcome, move, bound, move > bound ? FollowReport.DescribeSingle(m_BatchItems[i].ToString(), controls, entry.Relation.Lateral, entry.Relation.Along, turned) : null);
            }

            m_Report.Rigid += m_Batch.RigidItems;
            m_Report.Full += m_Batch.FullItems;
        }

        private void FollowLine(TItem item, Entry entry, Func<TEdge, FollowCurve> curveOf, PlaceOf placeOf, FollowMode mode)
        {
            int roads = entry.Roads.Length;
            if (m_LineNows.Length < roads)
                m_LineNows = new FollowCurve[Math.Max(roads, m_LineNows.Length * 2)];
            for (int j = 0; j < roads; j++)
            {
                FollowCurve now = Now(entry.Roads[j], curveOf);
                if (now == null || !now.IsUsable)
                {
                    entry.Stale = true;
                    m_Report.AddLine(LineOutcome.Failed, 0f, 0f, 1f, false, null);
                    return;
                }

                m_LineNows[j] = now;
            }

            if (!placeOf(item, true, out Vec3 at, out float yaw, out string path))
                return;
            LineRelation line = entry.Line;
            if (path != line.Path || (at - line.Position).Length > RoadRelation.PlacedTolerance || Math.Abs(RoadRelation.Wrap(yaw - line.Yaw)) > 1e-3f)
            {
                // Edited or moved by someone else: the relation is measured again where the line is, on the roads as they are now.
                m_Report.LinesDetached++;
                var tables = new FollowCurve[roads];
                Array.Copy(m_LineNows, tables, roads);
                if (LineFollow.TryMeasure(path, at, yaw, tables, roads, m_Work, out LineRelation again))
                {
                    entry.Line = again;
                    entry.Baselines = tables;
                }
                else
                    entry.Stale = true;
                return;
            }

            float lengthBefore = entry.LengthNow;
            LineRelation next = line.Copy();
            LineOutcome outcome = LineFollow.Apply(next, m_LineNows, mode, FollowGate.LineChordLimit, m_Work, out string newPath, out Vec3 newPosition, out float newYaw, out LineStats stats);
            float move = 0f, bound = 0f, ratio = 1f, turned = 0f, lineLateral = 0f, lineControls = 0f;
            if (stats.SteppedBack > 0)
                m_Report.LinesSteppedBack++;
            if (outcome == LineOutcome.PoseOnly || outcome == LineOutcome.Rewritten)
            {
                // The first line.Count mapped points are the old points' images, whether or not the line was cut into more chords.
                float before = 0f, after = 0f, lateral = 0f, controls = 0f;
                for (int j = 0; j < line.Count; j++)
                {
                    move = Math.Max(move, (m_Work.Mapped[j] - line.Written[j]).Length);
                    lateral = Math.Max(lateral, (float)Math.Sqrt(line.Lateral[j] * line.Lateral[j] + line.Along[j] * line.Along[j]));
                    if (j > 0)
                    {
                        before += Vec3.DistanceXZ(line.Written[j - 1], line.Written[j]);
                        after += Vec3.DistanceXZ(m_Work.Mapped[j - 1], m_Work.Mapped[j]);
                    }
                }

                for (int j = 0; j < roads; j++)
                    controls = Math.Max(controls, FollowGate.ControlMove(entry.Baselines[j].Curve, m_LineNows[j].Curve));
                ratio = before > 1e-4f ? after / before : 1f;
                turned = RoadRelation.Wrap(newYaw - line.Yaw);
                bound = FollowGate.Bound(controls, lateral, turned);
                lineLateral = lateral;
                lineControls = controls;
            }

            m_Report.AddLine(outcome, move, bound, ratio, stats.Wanted > RunPath.MaxPieces, move > bound ? FollowReport.DescribeLine(item.ToString(), lineControls, lineLateral, turned, ratio) : null);
            if (m_Writes == null || (outcome != LineOutcome.PoseOnly && outcome != LineOutcome.Rewritten))
                return;
            if (ratio > 1f + MaxGrowth || stats.Wanted > RunPath.MaxPieces)
            {
                // Not followed: its relation is as it was and it stays where it is, to be made again from its road.
                m_Report.LinesNeedRebuild++;
                return;
            }

            entry.Line = next;
            if (entry.PhaseCm >= 0)
            {
                entry.LengthNow = LineFollow.WrittenLength(next);
                if (Math.Abs(entry.LengthNow - lengthBefore) >= 0.005f)
                    m_PhaseDirty.Add(entry.Group);
            }

            m_Writes.Add(new FollowWrite<TItem> { Item = item, Position = newPosition, Yaw = newYaw, Path = outcome == LineOutcome.Rewritten ? newPath : null, IsLine = true });
            m_Report.LinesWritten++;
            if (entry.Id >= 0)
                m_Scheduler.Moved(entry.Id, newPosition.X, newPosition.Z);
        }

        /// <summary>
        /// The runs whose pieces changed length in this frame get their phases and total made again from the lengths the pieces have now (LinePhases): a piece that was written this frame has them in its write, a
        /// piece that was not followed (its road did not change) gets a write of its own that leaves its place alone. A run that is not laid end to end (cut by hand, or two sets of pieces that look alike) is left.
        /// </summary>
        private void RebasePhases()
        {
            if (m_PhaseDirty.Count == 0)
                return;
            m_WriteAt.Clear();
            for (int i = 0; i < m_Writes.Count; i++)
                m_WriteAt[m_Writes[i].Item] = i;
            m_PhaseDone.Clear();
            foreach (int group in m_PhaseDirty)
            {
                // A line that is followed over several frames (the budget, the slices, the tile gates) is made again once, when its last piece has been followed: not for each frame's share of it.
                if (m_Scheduler.GroupWaiting(group))
                    continue;
                m_PhaseDone.Add(group);
                if (!m_PhasedGroups.TryGetValue(group, out List<TItem> members))
                    continue;
                int n = members.Count;
                var phases = new long[n];
                var atPhase = new float[n];
                var now = new float[n];
                var entries = new Entry[n];
                bool whole = true;
                for (int i = 0; i < n; i++)
                {
                    if (!m_Entries.TryGetValue(members[i], out Entry e) || e.Stale)
                    {
                        whole = false;
                        break;
                    }

                    entries[i] = e;
                    phases[i] = e.PhaseCm;
                    atPhase[i] = e.LengthAtPhase;
                    now[i] = e.LengthNow;
                }

                var made = new long[n];
                if (!whole || !LinePhases.TryRebase(phases, atPhase, now, entries[0].TotalCm, made, out long total))
                {
                    m_Report.RunsNotRebased++;
                    continue;
                }

                for (int i = 0; i < n; i++)
                {
                    Entry e = entries[i];
                    bool changed = made[i] != e.PhaseCm || total != e.TotalCm;
                    if (changed)
                    {
                        if (m_WriteAt.TryGetValue(members[i], out int at))
                        {
                            FollowWrite<TItem> write = m_Writes[at];
                            write.HasPhase = true;
                            write.PhaseCm = made[i];
                            write.TotalCm = total;
                            m_Writes[at] = write;
                        }
                        else
                        {
                            m_Writes.Add(new FollowWrite<TItem> { Item = members[i], Position = e.Line.Position, Yaw = e.Line.Yaw, IsLine = true, HasPhase = true, PhaseCm = made[i], TotalCm = total, PhaseOnly = true });
                            m_WriteAt[members[i]] = m_Writes.Count - 1;
                        }

                        m_Report.PhasesWritten++;
                    }

                    e.PhaseCm = made[i];
                    e.TotalCm = total;
                    e.LengthAtPhase = e.LengthNow;
                }
            }

            foreach (int group in m_PhaseDone)
                m_PhaseDirty.Remove(group);
        }

        /// <summary>The burst's report when it is over (nothing waits) or has gone on for MaxBurstFrames frames; null while it is going on or when nothing was done. The system adds the frames' times with NoteFrame.</summary>
        public FollowReport Finished()
        {
            if (m_Report.Frames == 0 || (!Settled && m_Report.Frames < MaxBurstFrames))
                return null;
            FollowReport done = m_Report;
            m_Report = new FollowReport();
            Totals.Add(done);
            return done;
        }

        /// <summary>The time of a frame in which Step did work.</summary>
        public void NoteFrame(double ms) => m_Report.NoteFrame(ms);

        /// <summary>The time of a frame in which StepWrite did work, with where it went: the pick is the last StepWrite's, the writing and the garbage collections the system's.</summary>
        public void NoteFrame(double ms, int frame, double seconds, double writeMs, int collections, bool boosted) => m_Report.NoteFrame(ms, frame, seconds, LastTakeMs, writeMs, collections, boosted);

        /// <summary>Forgets everything (a city is loaded: its items and roads are not these).</summary>
        public void Clear()
        {
            m_Entries.Clear();
            m_ItemsOf.Clear();
            m_Dirty.Clear();
            m_Pending.Clear();
            m_Queued.Clear();
            m_Report = new FollowReport();
            Totals = new FollowTotals();
            var scheduler = new FollowScheduler
            {
                TileGateFrames = m_Scheduler.TileGateFrames, MaxWaitFrames = m_Scheduler.MaxWaitFrames, TileOrder = m_Scheduler.TileOrder, AtomicGroupMax = m_Scheduler.AtomicGroupMax, NearMetres = m_Scheduler.NearMetres
            };
            m_Scheduler = scheduler;
            m_EdgeIds.Clear();
            m_EdgeDirtyAt.Clear();
            m_ItemOfId.Clear();
            m_FreeIds.Clear();
            m_Jobs.Clear();
            m_PhasedGroups.Clear();
            m_PhaseDirty.Clear();
            m_Version = 0;
        }
    }
}
