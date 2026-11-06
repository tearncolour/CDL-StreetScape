using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Collections;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// Keeps the road hint of the items honest. A road that is split, replaced or bulldozed gets new edge entities, so the edge an item names
    /// can be gone or can have moved away; the item's own position is the truth and stays where it is. A few items are checked per round:
    /// when the named edge is still there and the item is still beside it nothing is done; otherwise the nearest edge by position is
    /// taken as the new hint, and when there is none within reach the item is marked as an orphan (and keeps its place).
    /// Since 0.6.0 this costs well under 2 ms an update (0.6.0's own slow-frame log showed the old version at 0.6 to 3.3 s a round): the nearest edge is found with the boxes of the edges
    /// (EdgeHint, tested against the brute force), an item that found no road waits a minute before it looks again, the table of edges is made once in 20 s and not for each item, and every
    /// update stops starting work after a millisecond (what is left is done in the next update).
    /// Since 0.12.72 an item that stood by a road in this session and loses it is removed (RoadGone; the owner: "放置了我们景物的道路删除后，放置的对应条目也应该删除而不是留在原地"). Since 0.12.73 (the
    /// owner: "删除不应该轮询，应该使用哈希池存储，删除某个道路之后立刻触发查找删除") nothing asks the roads whether they are alive: the game marks an edge Deleted for the rest of the frame in which a road is
    /// bulldozed, replaced, split or merged, the system runs after the game's net search tree took that frame's changes (SystemUpdatePhase.ModificationEnd), looks the edge up in the pool (road to its items)
    /// and decides each of its items in the same update with the tree: a road in reach (the replacement, made in the same frame, is in the tree) keeps it, linked to that road; none removes it. A sign on a
    /// building's wall is not an item of a road and stays; an item that was never linked (an orphan of an old save) is never in the pool.
    /// </summary>
    internal sealed partial class ItemRoadSystem : GameSystemBase
    {
        private const int kRoundFrames = 15;
        private const int kItemsPerRound = 12;

        /// <summary>No new item and no new table is started in an update once this much of it has gone (ms); an item that is being worked on is finished.</summary>
        private const double kBudgetMs = 0.4;

        /// <summary>The table of edges is only started with this much of the budget left (ms): it is one piece of work that cannot be cut.</summary>
        private const double kTableStartMs = 0.5;

        private EntityQuery m_Items;
        private EntityQuery m_Edges;

        /// <summary>The items written since the last frame (a change-version filter; its own query: the same components as m_Items would be the same query, and the filter would be on both).</summary>
        private EntityQuery m_ItemsWritten;

        private readonly RoundGate m_Gate = new RoundGate();
        private bool m_ItemsEdited;
        private int m_RoundsSkipped;
        private int m_Frame;
        private int m_Cursor;
        private bool m_Full;
        private int m_Remaining = -1;
        internal static int Relinked { get; private set; }
        internal static int Orphans { get; private set; }

        /// <summary>The items removed since the city was loaded because their road was deleted.</summary>
        internal static int RemovedWithRoad { get; private set; }

        internal static ItemRoadSystem Instance { get; private set; }

        /// <summary>The pool: each road with its items. Edges that are marked Deleted (the game's own word for a road that goes) are looked up in it.</summary>
        private readonly RoadGone<Entity, Entity> m_Roads = new RoadGone<Entity, Entity>();
        private readonly List<RoadGone<Entity, Entity>.Waiting> m_Doomed = new List<RoadGone<Entity, Entity>.Waiting>();
        private readonly Dictionary<Entity, int> m_DoomedPerRoad = new Dictionary<Entity, int>();
        private EntityQuery m_DeletedEdges;

        /// <summary>Edges, nodes and the roads' aggregates (their names) that were made, updated or deleted this frame: the road network changed (NetRevision).</summary>
        private EntityQuery m_NetChanged;
        private Game.Net.SearchSystem m_NetSearch;
        private RoadGone<Entity, Entity>.Decide m_Decide;
        private Func<Entity, bool> m_Exists;
        private NativeQuadTree<Entity, QuadTreeBoundsXZ> m_Tree;
        private bool m_TreeRead;
        private readonly List<Entity> m_Found = new List<Entity>();
        private int m_RoadFailures;

        private readonly Dictionary<Entity, float> m_NotBefore = new Dictionary<Entity, float>();
        private EdgeBoxes m_Boxes = new EdgeBoxes();
        private Entity[] m_TableEdges = new Entity[1024];
        private float m_TableAt = -1000f;
        private bool m_TableValid;

        // The table of edges is made over several updates (0.7.1: in one piece it was 7.8 ms with 40 000 edges): a copy of the edges and their curves is taken, and the boxes are made from it a few
        // thousand at a time; the table that is there stays in use until the new one is complete.
        private bool m_Building;
        private NativeArray<Entity> m_BuildEdges;
        private NativeArray<Curve> m_BuildCurves;
        private EdgeBoxes m_BuildBoxes = new EdgeBoxes();
        private Entity[] m_BuildTable = new Entity[1024];
        private int m_BuildCursor;
        private float m_BuildStarted;
        private const double kBuildSliceMs = 0.4;
        private const int kBuildCheckEvery = 256;
        private readonly List<int> m_Scratch = new List<int>();
        private Func<int, Bezier3> m_CurveOf;

        // What the first check after loading found and cost, and what the last update did (for the log).
        private int m_FirstAlreadyOrphan, m_FirstScans, m_FirstWaits, m_FirstUpdates;
        private double m_FirstMs, m_FirstTableMs;
        private int m_UpdateScans, m_UpdateWaits;
        private double m_UpdateTableMs;
        private int m_TableBuilds;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Items = GetEntityQuery(ComponentType.ReadOnly<TsItem>());
            m_ItemsWritten = GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<TsItem>() }, None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>() } });
            m_ItemsWritten.SetChangedVersionFilter(ComponentType.ReadOnly<TsItem>());
            m_Edges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_DeletedEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_NetChanged = GetEntityQuery(
                new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Edge>() }, Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>() }, None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>() } },
                new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Node>() }, Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>() }, None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>() } },
                new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Aggregate>() }, Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<BatchesUpdated>() } });
            m_NetSearch = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            m_CurveOf = CurveOf;
            m_Decide = Decide;
            m_Exists = EntityManager.Exists;
            CreateFollow();
            Instance = this;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            AbandonBuild();
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            NetRevision.Bump();
            m_Full = true;
            m_Remaining = -1;
            m_Cursor = 0;
            Relinked = 0;
            Orphans = 0;
            RemovedWithRoad = 0;
            m_Roads.Clear();
            LoadFollow();
            m_Gate.Reset();
            m_ItemsEdited = false;
            m_NotBefore.Clear();
            m_TableValid = false;
            AbandonBuild();
            m_FirstAlreadyOrphan = m_FirstScans = m_FirstWaits = m_FirstUpdates = 0;
            m_FirstMs = m_FirstTableMs = 0.0;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance.gameMode != GameMode.Game)
                return;
            m_TreeRead = false;
            long stage = Stopwatch.GetTimestamp();
            if (!m_NetChanged.IsEmptyIgnoreFilter)
                NetRevision.Bump();
            SlowLog.Since("ItemRoadSystem: net revision", stage);
            stage = Stopwatch.GetTimestamp();
            try
            {
                StepRoads();
            }
            catch (Exception ex)
            {
                if (m_RoadFailures++ < 5)
                    Mod.log.Error("Items: the check of the deleted roads failed: " + ex);
            }

            SlowLog.Since("ItemRoadSystem: step roads", stage);
            stage = Stopwatch.GetTimestamp();
            try
            {
                StepFollow();
            }
            catch (Exception ex)
            {
                if (m_FollowFailures++ < 5)
                    Mod.log.Error("Items: the follow of the updated roads failed: " + ex);
            }

            SlowLog.Since("ItemRoadSystem: step follow", stage);

            // Every frame: the filter is relative to the system's last update, so a write is seen in the frame after it only.
            if (!m_ItemsWritten.IsEmpty)
                m_ItemsEdited = true;
            if (!m_Full && ++m_Frame % kRoundFrames != 0)
                return;
            stage = Stopwatch.GetTimestamp();
            bool needed = m_Gate.Needed(m_Full || m_Building, NetRevision.Value, m_Items.CalculateEntityCount(), m_ItemsEdited);
            m_ItemsEdited = false;
            SlowLog.Since("ItemRoadSystem: round gate", stage);
            if (!needed)
            {
                m_RoundsSkipped++;
                return;
            }

            var clock = Stopwatch.StartNew();
            bool full = m_Full;
            m_UpdateScans = 0;
            m_UpdateWaits = 0;
            m_UpdateTableMs = 0.0;
            try
            {
                Round(clock);
            }
            catch (Exception ex)
            {
                Mod.log.Error("Item road check failed: " + ex);
            }

            double ms = clock.Elapsed.TotalMilliseconds;
            if (full)
            {
                m_FirstMs += ms;
                m_FirstUpdates++;
            }

            SlowLog.Add("ItemRoadSystem", ms, SlowLog.IsSlow(ms) ? $"{(full ? "the first check after loading" : "a round")}: {m_UpdateScans} search(es) for a road, {m_UpdateWaits} item(s) waiting, table of edges {m_UpdateTableMs:0.0} ms, {m_Boxes.Count} edge(s), {m_RoundsSkipped} idle round(s) before it" : null);
            m_RoundsSkipped = 0;
        }

        private void Round(Stopwatch clock)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            long stage = Stopwatch.GetTimestamp();
            StepBuild(now, clock);
            SlowLog.Since("ItemRoadSystem: round, table of edges", stage);
            stage = Stopwatch.GetTimestamp();
            using (NativeArray<Entity> entities = m_Items.ToEntityArray(Allocator.Temp))
            {
                SlowLog.Since("ItemRoadSystem: round, item array", stage);
                if (entities.Length == 0)
                {
                    m_Full = false;
                    m_Remaining = -1;
                    m_Gate.Checked(0, 0);
                    return;
                }

                if (m_Full && m_Remaining < 0)
                    m_Remaining = entities.Length;
                if (m_NotBefore.Count > 4 * entities.Length + 64)
                    m_NotBefore.Clear();
                // The items that were destroyed stay in the pool until they are taken out: that is done when it holds far more than there are items.
                if (m_Roads.Linked > 4 * entities.Length + 256)
                {
                    m_Roads.Prune(m_Exists);
                    m_Follow.Prune(m_Exists);
                    m_FollowSkipped.RemoveWhere(skipped => !EntityManager.Exists(skipped));
                }

                int max = m_Full ? m_Remaining : Math.Min(kItemsPerRound, entities.Length);
                int done = 0;
                m_LinkTicks = 0L;
                stage = Stopwatch.GetTimestamp();
                while (done < max)
                {
                    if (done > 0 && clock.Elapsed.TotalMilliseconds >= LoadBudget.Scaled(kBudgetMs))
                        break;
                    if (!Process(entities[(m_Cursor + done) % entities.Length], now, clock))
                        break;
                    done++;
                }

                double checkMs = (Stopwatch.GetTimestamp() - stage) * 1000.0 / Stopwatch.Frequency;
                SlowLog.Add("ItemRoadSystem: round, checking items", checkMs, SlowLog.IsSlow(checkMs) ? $"{done} of {entities.Length} item(s), {m_UpdateScans} searched for a road, {m_UpdateWaits} waiting" : null);
                SlowLog.Add("ItemRoadSystem: round, of which linking", m_LinkTicks * 1000.0 / Stopwatch.Frequency);
                m_Cursor = (m_Cursor + done) % entities.Length;
                m_Gate.Checked(done, entities.Length);
                if (m_Full)
                {
                    m_Remaining -= done;
                    if (m_Remaining <= 0)
                    {
                        m_Full = false;
                        m_Remaining = -1;
                        Mod.log.Info($"Items: first check of {entities.Length} item(s): {Relinked} relinked, {Orphans} newly without a road in reach, {m_FirstAlreadyOrphan} were without a road already; " +
                                     $"{m_FirstScans} searched for a road, {m_FirstWaits} waiting; {m_TableBuilds} table(s) of {m_Boxes.Count} edge(s), made over {m_LastTableSeconds:0.0} s; {m_FirstMs:0.0} ms in {m_FirstUpdates} update(s).");
                    }
                }
            }
        }

        /// <summary>Checks one item. False when it could not be started now (the table of edges is needed and there is no time for it): it is taken first in the next update.</summary>
        private bool Process(Entity entity, float now, Stopwatch clock)
        {
            // An item whose road is being moved trails it (the follow writes it a few frames late): it is not an orphan, and its relation is not to be dropped.
            if (m_FollowWrites && m_Follow.Holds(entity, m_FollowFrame, kFollowHoldFrames))
                return true;
            TsItem item = EntityManager.GetComponentData<TsItem>(entity);
            // A sign on a wall is no sign of a road, and is not beside one as a rule: it is fixed to its building (or post) here, whatever the road check below says.
            if (m_FollowWrites)
            {
                HostKind hostKind = HostKindOf(item);
                if (hostKind == HostKind.Post || hostKind == HostKind.Building)
                    EnsureHostLink(entity, item, hostKind, now);
            }

            m_NotBefore.TryGetValue(entity, out float notBefore);
            switch (EdgeHint.Decide(IsBeside(item), item.Orphan, now, notBefore))
            {
                case EdgeCheck.Beside:
                    if (item.Orphan)
                    {
                        item.Flags = (byte)(item.Flags & ~TsItem.FlagOrphan);
                        EntityManager.SetComponentData(entity, item);
                    }

                    m_NotBefore.Remove(entity);
                    NoteLinked(entity, item, item.Edge);
                    return true;

                case EdgeCheck.Wait:
                    m_UpdateWaits++;
                    m_FirstWaits += m_Full ? 1 : 0;
                    return true;
            }

            // The hint does not hold: look for the nearest edge by position, with the table of edges (made once for all the items of the next seconds).
            if (!EnsureTable(now, clock))
                return false;
            m_UpdateScans++;
            m_FirstScans += m_Full ? 1 : 0;
            if (FindRoad(item, out Entity edge, out float t))
            {
                Link(entity, item, edge, t);
                Relinked++;
                m_NotBefore.Remove(entity);
                return true;
            }

            // No road in reach: the item keeps its place, and looks again in a minute (a little later for each, so that they do not all come due in one update).
            if (m_Full && item.Orphan)
                m_FirstAlreadyOrphan++;
            m_NotBefore[entity] = now + EdgeHint.OrphanRetrySeconds + entity.Index % 16;
            m_Roads.Forget(entity);
            m_Follow.Forget(entity);
            if (!item.Orphan)
            {
                item.Edge = Entity.Null;
                item.Flags |= TsItem.FlagOrphan;
                Orphans++;
                EntityManager.SetComponentData(entity, item);
            }

            return true;
        }

        /// <summary>The nearest live edge of the table within reach of the item (the search that makes an item an orphan when it finds none).</summary>
        private bool FindRoad(TsItem item, out Entity edge, out float t)
        {
            int index = EdgeHint.Nearest(m_Boxes, m_CurveOf, item.Position.x, item.Position.y, item.Position.z, EdgeHint.Beside, m_Scratch, out t, out _);
            edge = index >= 0 ? m_TableEdges[index] : Entity.Null;
            return index >= 0 && RoadReader.IsLiveEdge(EntityManager, edge);
        }

        /// <summary>The item is given the road as its hint, and is no orphan.</summary>
        private void Link(Entity entity, TsItem item, Entity edge, float t)
        {
            item.Edge = edge;
            item.EdgeT = t;
            item.Flags = (byte)(item.Flags & ~TsItem.FlagOrphan);
            EntityManager.SetComponentData(entity, item);
            NoteLinked(entity, item, edge);
        }

        /// <summary>
        /// The boxes of the edges of the map. True when there is a table to search (one that is older than EdgeHint.TableSeconds is used while a new one is being made over the next updates); false
        /// when there is none yet (the first one is being made: the item looks again in the next update).
        /// </summary>
        private bool EnsureTable(float now, Stopwatch clock)
        {
            if (m_TableValid && now - m_TableAt < EdgeHint.TableSeconds)
                return true;
            if (!m_Building && clock.Elapsed.TotalMilliseconds <= kTableStartMs)
                StartBuild(now);
            return m_TableValid;
        }

        private void StartBuild(float now)
        {
            var timer = Stopwatch.StartNew();
            m_BuildEdges = m_Edges.ToEntityArray(Allocator.Persistent);
            m_BuildCurves = m_Edges.ToComponentDataArray<Curve>(Allocator.Persistent);
            m_BuildBoxes.Clear();
            if (m_BuildTable.Length < m_BuildEdges.Length)
                m_BuildTable = new Entity[Math.Max(m_BuildEdges.Length, m_BuildTable.Length * 2)];
            m_BuildCursor = 0;
            m_BuildStarted = now;
            m_Building = true;
            m_UpdateTableMs += timer.Elapsed.TotalMilliseconds;
        }

        /// <summary>Makes a few thousand more boxes of the table that is being made, within the slice's time; when all are made the new table replaces the old.</summary>
        private void StepBuild(float now, Stopwatch clock)
        {
            if (!m_Building)
                return;
            var timer = Stopwatch.StartNew();
            int count = m_BuildEdges.Length;
            while (m_BuildCursor < count)
            {
                int end = Math.Min(count, m_BuildCursor + kBuildCheckEvery);
                for (int i = m_BuildCursor; i < end; i++)
                {
                    var b = m_BuildCurves[i].m_Bezier;
                    m_BuildBoxes.Add(b.a.x, b.a.z, b.b.x, b.b.z, b.c.x, b.c.z, b.d.x, b.d.z);
                    m_BuildTable[i] = m_BuildEdges[i];
                }

                m_BuildCursor = end;
                if (timer.Elapsed.TotalMilliseconds >= LoadBudget.Scaled(kBuildSliceMs))
                    break;
            }

            m_UpdateTableMs += timer.Elapsed.TotalMilliseconds;
            if (m_BuildCursor < count)
                return;

            // Complete: the new table replaces the old, and what the old one held is kept to be used for the next build.
            EdgeBoxes boxes = m_Boxes;
            Entity[] table = m_TableEdges;
            m_Boxes = m_BuildBoxes;
            m_TableEdges = m_BuildTable;
            m_BuildBoxes = boxes;
            m_BuildTable = table;
            m_TableAt = now;
            m_TableValid = true;
            m_TableBuilds++;
            m_FirstTableMs += m_UpdateTableMs;
            m_LastTableEdges = count;
            m_LastTableSeconds = now - m_BuildStarted;
            AbandonBuild();
        }

        private int m_LastTableEdges;
        private float m_LastTableSeconds;

        private void AbandonBuild()
        {
            m_Building = false;
            if (m_BuildEdges.IsCreated)
                m_BuildEdges.Dispose();
            if (m_BuildCurves.IsCreated)
                m_BuildCurves.Dispose();
            m_BuildCursor = 0;
        }

        /// <summary>The curve of an edge of the table; an edge that is gone is a curve far away, which no item is near.</summary>
        private Bezier3 CurveOf(int index)
        {
            if (RoadReader.TryRead(EntityManager, m_TableEdges[index], out Bezier3 curve, out _))
                return curve;
            var far = new Vec3(1e9f, 0f, 1e9f);
            return new Bezier3(far, far, far, far);
        }

        /// <summary>
        /// The roads that were deleted, and their items (0.12.73): the edges the game marked Deleted this frame are looked up in the pool, and the items of each are decided in this update (Decide), at most
        /// RoadGone.MaxPerUpdate; those that are removed go as the panel's button removes orphans: through the tool, so that Ctrl+Z brings them back (all of one update are one step), with a line in the
        /// log for each road. Nothing is done in an update in which no edge was deleted and no item waits: the query is empty, and that is all it costs.
        /// </summary>
        private void StepRoads()
        {
            bool deleted = !m_DeletedEdges.IsEmptyIgnoreFilter;
            if (!deleted && m_Roads.Waits == 0)
                return;
            long start = Stopwatch.GetTimestamp();
            int queued = 0;
            if (deleted)
            {
                using (NativeArray<Entity> edges = m_DeletedEdges.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity edge in edges)
                    {
                        queued += m_Roads.EdgeDied(edge);
                        m_Follow.EdgeGone(edge);
                    }
                }
            }

            m_Doomed.Clear();
            m_StayedPerRoad.Clear();
            m_Roads.Drain(RoadGone<Entity, Entity>.MaxPerUpdate, m_Decide, m_Doomed);
            if (m_Doomed.Count > 0)
                RemoveWithRoad();
            foreach (KeyValuePair<Entity, (int Count, float Distance, float Overrun)> road in m_StayedPerRoad)
                Mod.log.Info($"Items: road {road.Key.Index}:{road.Key.Version} was deleted: {road.Value.Count} item(s) stay with a road in reach (the farthest {road.Value.Distance:0.0} m from its centre line, {road.Value.Overrun:0.0} m past its end).");
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            SlowLog.Add("ItemRoadSystem: roads deleted", ms, SlowLog.IsSlow(ms) ? $"{queued} item(s) of the deleted roads queued, {m_Doomed.Count} removed, {m_Roads.Waits} waiting, {m_Roads.Roads} road(s) in the pool" : null);
        }

        /// <summary>
        /// What becomes of an item whose road died. One that is gone needs nothing; a sign on a building's wall is not an item of a road; else the net search tree (it has this frame's changes: the edges
        /// that replace the deleted one are in it, the deleted one is not) is asked for a road beside it, and the one found is the item's hint as in a round.
        /// </summary>
        private RoadGoneVerdict Decide(RoadGone<Entity, Entity>.Waiting waiting)
        {
            Entity entity = waiting.Item;
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                return RoadGoneVerdict.Keep;
            TsItem item = EntityManager.GetComponentData<TsItem>(entity);
            bool exempt = OnWall(item);
            Entity edge = Entity.Null;
            float t = 0f;
            float distance = 0f, overrun = 0f;
            bool inReach = !exempt && FindBeside(item, out edge, out t, out distance, out overrun);
            if (inReach)
            {
                Link(entity, item, edge, t);
                Relinked++;
                m_NotBefore.Remove(entity);
                m_StayedPerRoad.TryGetValue(waiting.Road, out (int Count, float Distance, float Overrun) stayed);
                m_StayedPerRoad[waiting.Road] = (stayed.Count + 1, Math.Max(stayed.Distance, distance), Math.Max(stayed.Overrun, overrun));
            }

            return RoadGone<Entity, Entity>.Verdict(exempt, inReach);
        }

        /// <summary>The item is fixed to a building's wall (flat on it, or on a bracket): its place is the building's, and a road's going changes nothing of it. The support is read now, so that an item edited to or from a wall is judged as it is.</summary>
        private static bool OnWall(TsItem item)
        {
            SupportDef support = SupportCatalog.ByHash(item.SupportHash);
            return support != null && SupportKinds.OnWall(support.Kind);
        }

        private struct EdgeIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Colossal.Mathematics.Bounds2 Box;
            public List<Entity> Found;

            public bool Intersect(QuadTreeBoundsXZ bounds) => Colossal.Mathematics.MathUtils.Intersect(bounds.m_Bounds.xz, Box);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (Colossal.Mathematics.MathUtils.Intersect(bounds.m_Bounds.xz, Box))
                    Found.Add(item);
            }
        }

        /// <summary>The game's net search tree, read once in an update (the system runs after the frame's changes were taken into it).</summary>
        private void EnsureTree()
        {
            if (m_TreeRead)
                return;
            m_Tree = m_NetSearch.GetNetSearchTree(true, out JobHandle dependencies);
            dependencies.Complete();
            m_TreeRead = true;
        }

        /// <summary>
        /// The nearest road that takes the item over (EdgeHint.TakesOver: beside it, and not past its end by more than EdgeHint.TakeOverReach) among the edges of the game's net search tree round it;
        /// how far from its centre line the item is and how far past its end. The tree is the game's own, updated in the modification phases before this system runs (SearchSystem, Modification5),
        /// so a road made in the frame in which the old one was deleted is there.
        /// </summary>
        private bool FindBeside(TsItem item, out Entity edge, out float t, out float distance, out float overrun)
        {
            EnsureTree();
            m_Found.Clear();
            float reach = EdgeHint.Beside + 1f;
            var iterator = new EdgeIterator { Box = new Colossal.Mathematics.Bounds2(new float2(item.Position.x - reach, item.Position.z - reach), new float2(item.Position.x + reach, item.Position.z + reach)), Found = m_Found };
            m_Tree.Iterate(ref iterator);
            var at = new Vec3(item.Position.x, item.Position.y, item.Position.z);
            edge = Entity.Null;
            t = 0f;
            distance = 0f;
            overrun = 0f;
            m_BesideRoads.Clear();
            m_BesideDistance.Clear();
            m_BesideHeight.Clear();
            m_BesideAlong.Clear();
            m_BesideOverrun.Clear();
            foreach (Entity candidate in m_Found)
            {
                if (EntityManager.HasComponent<Edge>(candidate) && RoadReader.TryRead(EntityManager, candidate, out Bezier3 curve, out float width)
                    && EdgeHint.TakesOver(curve, width, at, out float along, out float away, out float past))
                {
                    m_BesideRoads.Add(candidate);
                    m_BesideDistance.Add(away);
                    m_BesideHeight.Add(curve.Point(along).Y);
                    m_BesideAlong.Add(along);
                    m_BesideOverrun.Add(past);
                }
            }

            // The road at the item's own level of those that lie over one another (RoadLevels): a post on a viaduct belongs to the viaduct, not to the road on the ground under it.
            int pick = RoadLevels.Pick(m_BesideDistance, m_BesideHeight, m_BesideRoads.Count, at.Y);
            if (pick < 0)
                return false;
            edge = m_BesideRoads[pick];
            t = m_BesideAlong[pick];
            distance = m_BesideDistance[pick];
            overrun = m_BesideOverrun[pick];
            return true;
        }

        private readonly List<Entity> m_BesideRoads = new List<Entity>();
        private readonly List<float> m_BesideDistance = new List<float>(), m_BesideHeight = new List<float>(), m_BesideAlong = new List<float>(), m_BesideOverrun = new List<float>();
        private readonly Dictionary<Entity, (int Count, float Distance, float Overrun)> m_StayedPerRoad = new Dictionary<Entity, (int Count, float Distance, float Overrun)>();

        private void RemoveWithRoad()
        {
            m_DoomedPerRoad.Clear();
            foreach (RoadGone<Entity, Entity>.Waiting waiting in m_Doomed)
            {
                if (PlacementTool.Instance != null)
                    PlacementTool.Instance.DestroyItem(waiting.Item);
                else if (EntityManager.Exists(waiting.Item))
                    EntityManager.DestroyEntity(waiting.Item);
                m_DoomedPerRoad.TryGetValue(waiting.Road, out int count);
                m_DoomedPerRoad[waiting.Road] = count + 1;
            }

            RemovedWithRoad += m_Doomed.Count;
            foreach (KeyValuePair<Entity, int> road in m_DoomedPerRoad)
                Mod.log.Info($"Items: road {road.Key.Index}:{road.Key.Version} was deleted and no road was in reach: removed {road.Value} item(s) with it ({RemovedWithRoad} since the city was loaded; Ctrl+Z brings them back).");
        }

        /// <summary>An item the tool has just made stands by the road it was placed at: it is known as linked without waiting for a round, so that a road deleted soon after takes it too.</summary>
        internal static void NoteMade(Entity item, Entity road)
        {
            if (Instance != null && road != Entity.Null)
                Instance.NoteLinked(item, Instance.EntityManager.GetComponentData<TsItem>(item), road);
        }

        private bool IsBeside(TsItem item) =>
            RoadReader.TryRead(EntityManager, item.Edge, out Bezier3 curve, out float width) && EdgeHint.IsBeside(curve, width, new Vec3(item.Position.x, item.Position.y, item.Position.z), out _, out _);
    }
}
