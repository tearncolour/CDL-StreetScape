using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.SceneFlow;
using Game.Routes;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// The batch interface's work (0.12.67; the contract of the interchange designer, TsBatchApi): a batch that was submitted is matched to roads over several frames within a budget (the roads
    /// near its items are boxed once, each item then looks for the road it stands beside), and written in one frame: the batch's old items destroyed and the new ones created together, so that the
    /// renderer sees one change and its Revision goes up once. An item that stands beside no road is not written (the sender is told). The items of a batch are found through an index that the
    /// renderer's own reading of the items feeds (ChunkSystem.Put), so a batch that is replaced never looks at the other items of the city. Main thread, in the main loop.
    /// </summary>
    internal sealed partial class TsBatchSystem : GameSystemBase
    {
        /// <summary>The time (ms) a frame may spend on the matching, one item over at most (AreaFitSystem's).</summary>
        private const double kBudgetMs = 2.0;

        private const int kEdgesPerStep = 256;

        private enum Phase
        {
            Collect,
            Boxes,
            Match,
            Write
        }

        private struct Matched
        {
            public BatchItem Item;
            public Entity Edge;
            public float T;
        }

        internal static TsBatchSystem Instance { get; private set; }

        private EntityQuery m_Edges;
        private EntityArchetype m_Archetype;
        private readonly BatchBook m_Book = new BatchBook();
        private readonly BatchIndex<Entity> m_Index = new BatchIndex<Entity>();

        // The job in hand and where its preparation has got to.
        private BatchJob m_Job;
        private Phase m_Phase;
        private int m_Cursor;
        private int m_Frames;
        private double m_PrepareMs;
        private readonly List<BatchItem> m_Todo = new List<BatchItem>();
        private readonly List<Matched> m_Matched = new List<Matched>();
        private readonly List<int> m_Unmatched = new List<int>();

        // The roads near the job's items: a copy of the game's edges and curves while they are boxed, then the boxes and what each box stands for.
        private NativeArray<Entity> m_EdgeEntities;
        private NativeArray<Curve> m_EdgeCurves;
        private float m_MinX, m_MaxX, m_MinZ, m_MaxZ;
        private readonly EdgeBoxes m_Boxes = new EdgeBoxes();
        private readonly List<Entity> m_Hosts = new List<Entity>();
        private readonly List<Bezier3> m_Curves = new List<Bezier3>();
        private readonly List<float> m_Widths = new List<float>();
        private readonly List<int> m_Scratch = new List<int>();
        private Func<int, Bezier3> m_CurveOf;
        private Func<int, float> m_WidthOf;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_Edges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Road>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_Archetype = EntityManager.CreateArchetype(ComponentType.ReadWrite<TsItem>(), ComponentType.ReadWrite<LivePath>());
            m_CurveOf = i => m_Curves[i];
            m_WidthOf = i => m_Widths[i];
        }

        [Preserve]
        protected override void OnDestroy()
        {
            EndJob();
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        /// <summary>The city that is left takes its batches with it: what was waiting is dropped, what was written is gone with the city's items (ChunkSystem destroys them at the same point).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            EndJob();
            m_Book.Clear();
            m_Index.Clear();
        }

        /// <summary>The renderer read an item (ChunkSystem.Put): the index of the batches follows it.</summary>
        internal static void Note(Entity entity, uint batchId)
        {
            TsBatchSystem system = Instance;
            if (system == null)
                return;
            if (batchId != 0u)
                system.m_Index.Add(batchId, entity);
            else if (system.m_Index.Count > 0)
                system.m_Index.Remove(entity);
        }

        /// <summary>The renderer found an item gone.</summary>
        internal static void Gone(Entity entity) => Instance?.m_Index.Remove(entity);

        /// <summary>
        /// TsBatchApi.Submit: the request is read and checked at once (the answer has the items' ignored keys and the ones refused); a batch with items to write is queued, and replaces a batch of
        /// the same id that is still waiting.
        /// </summary>
        internal string Submit(string json)
        {
            BatchPlan plan = BatchRequest.Parse(json);
            if (plan.Error.Length == 0 && (GameManager.instance == null || GameManager.instance.gameMode != GameMode.Game || ChunkSystem.Instance == null))
                plan.Error = "no city is loaded";
            if (plan.Error.Length > 0)
            {
                Mod.log.Info($"Batch '{plan.Batch}' refused: {plan.Error}.");
                return BatchBook.SubmitJson(plan);
            }

            m_Book.Submit(plan);
            int ignored = 0;
            foreach (BatchItem item in plan.Items)
                ignored += item.IgnoredKeys.Count;
            Mod.log.Info($"Batch '{plan.Batch}' queued: {plan.AcceptedCount} item(s) of {plan.Items.Count} ({plan.Items.Count - plan.AcceptedCount} refused, {ignored} ignored key(s)), {m_Index.CountOf(plan.BatchId)} item(s) of an earlier batch of the id to replace.");
            return BatchBook.SubmitJson(plan);
        }

        internal string Status(string batch) => BatchBook.StatusJson(batch, string.IsNullOrEmpty(batch) ? null : m_Book.Find(BatchRequest.HashOf(batch)));

        [Preserve]
        protected override void OnUpdate()
        {
            if (m_Book.Next() == null && m_Job == null)
                return;
            if (GameManager.instance.gameMode != GameMode.Game)
                return;
            ChunkSystem chunks = ChunkSystem.Instance;
            if (chunks == null || !chunks.Synced)
                return;

            var clock = Stopwatch.StartNew();
            try
            {
                Step(clock);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Batch '{m_Job?.Plan.Batch}' failed: {ex}");
                m_Job?.Fail(ex.GetType().Name + ": " + ex.Message);
                EndJob();
            }
        }

        private void Step(Stopwatch clock)
        {
            if (m_Job != null && m_Job.Superseded)
                EndJob();
            if (m_Job == null)
            {
                m_Job = m_Book.Next();
                if (m_Job == null)
                    return;
                BeginJob();
            }

            // The write is a frame of its own, so that what it costs is one number.
            if (m_Phase == Phase.Write)
            {
                Write();
                return;
            }

            m_Frames++;
            var slice = Stopwatch.StartNew();
            while (m_Phase != Phase.Write && clock.Elapsed.TotalMilliseconds < kBudgetMs)
            {
                switch (m_Phase)
                {
                    case Phase.Collect:
                        Collect();
                        break;
                    case Phase.Boxes:
                        MakeBoxes(clock);
                        break;
                    default:
                        MatchItems(clock);
                        break;
                }
            }

            m_PrepareMs += slice.Elapsed.TotalMilliseconds;
        }

        private void BeginJob()
        {
            m_Job.Begin();
            m_Frames = 0;
            m_PrepareMs = 0.0;
            m_Cursor = 0;
            m_Todo.Clear();
            m_Matched.Clear();
            m_Unmatched.Clear();
            ClearTable();
            m_MinX = m_MinZ = float.MaxValue;
            m_MaxX = m_MaxZ = float.MinValue;
            foreach (BatchItem item in m_Job.Plan.Items)
            {
                if (!item.Accepted)
                    continue;
                m_Todo.Add(item);
                m_MinX = Math.Min(m_MinX, item.X);
                m_MaxX = Math.Max(m_MaxX, item.X);
                m_MinZ = Math.Min(m_MinZ, item.Z);
                m_MaxZ = Math.Max(m_MaxZ, item.Z);
            }

            // A batch with nothing to write only replaces the old one with nothing.
            m_Phase = m_Todo.Count == 0 ? Phase.Write : Phase.Collect;
        }

        private void Collect()
        {
            m_EdgeEntities = m_Edges.ToEntityArray(Allocator.Persistent);
            m_EdgeCurves = m_Edges.ToComponentDataArray<Curve>(Allocator.Persistent);
            m_Cursor = 0;
            m_Phase = Phase.Boxes;
        }

        /// <summary>Boxes the roads whose curves come within reach of the items (the rest of the map is not looked at again), a few hundred at a time within the budget.</summary>
        private void MakeBoxes(Stopwatch clock)
        {
            float reach = BatchHost.SearchRadius;
            int count = m_EdgeEntities.Length;
            while (m_Cursor < count)
            {
                int end = Math.Min(count, m_Cursor + kEdgesPerStep);
                for (int i = m_Cursor; i < end; i++)
                {
                    var b = m_EdgeCurves[i].m_Bezier;
                    float minX = Math.Min(Math.Min(b.a.x, b.b.x), Math.Min(b.c.x, b.d.x)), maxX = Math.Max(Math.Max(b.a.x, b.b.x), Math.Max(b.c.x, b.d.x));
                    float minZ = Math.Min(Math.Min(b.a.z, b.b.z), Math.Min(b.c.z, b.d.z)), maxZ = Math.Max(Math.Max(b.a.z, b.b.z), Math.Max(b.c.z, b.d.z));
                    if (maxX < m_MinX - reach || minX > m_MaxX + reach || maxZ < m_MinZ - reach || minZ > m_MaxZ + reach)
                        continue;
                    if (!RoadReader.TryRead(EntityManager, m_EdgeEntities[i], out Bezier3 curve, out float width))
                        continue;
                    m_Boxes.Add(curve.A.X, curve.A.Z, curve.B.X, curve.B.Z, curve.C.X, curve.C.Z, curve.D.X, curve.D.Z);
                    m_Hosts.Add(m_EdgeEntities[i]);
                    m_Curves.Add(curve);
                    m_Widths.Add(width);
                }

                m_Cursor = end;
                if (clock.Elapsed.TotalMilliseconds >= kBudgetMs)
                    return;
            }

            DisposeCopies();
            m_Cursor = 0;
            m_Phase = Phase.Match;
        }

        private void MatchItems(Stopwatch clock)
        {
            while (m_Cursor < m_Todo.Count)
            {
                BatchItem item = m_Todo[m_Cursor++];
                int host = BatchHost.Find(m_Boxes, m_CurveOf, m_WidthOf, item.X, item.Z, m_Scratch, out float t);
                if (host >= 0)
                    m_Matched.Add(new Matched { Item = item, Edge = m_Hosts[host], T = t });
                else
                    m_Unmatched.Add(item.Index);
                if (clock.Elapsed.TotalMilliseconds >= kBudgetMs)
                    return;
            }

            ClearTable();
            m_Phase = Phase.Write;
        }

        /// <summary>
        /// The write, in one frame: the batch's items of before are destroyed and the new ones created in the same call of the update, so the renderer sees them together (one Changes, one Revision).
        /// What it costs on a city is not known offline: it is timed, and the log says it (a line for each batch, and the slow-update log when it is slow).
        /// </summary>
        private void Write()
        {
            var timer = Stopwatch.StartNew();
            uint id = m_Job.Plan.BatchId;

            // What the batch wrote before, as far as it is still there (an item deleted by hand is gone from the world and, in time, from the index).
            List<Entity> old = m_Index.Take(id);
            var gone = new List<Entity>(old.Count);
            foreach (Entity entity in old)
            {
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<TsItem>(entity) && EntityManager.GetComponentData<TsItem>(entity).BatchId == id)
                    gone.Add(entity);
            }

            if (gone.Count > 0)
            {
                using (var doomed = new NativeArray<Entity>(gone.ToArray(), Allocator.Temp))
                    EntityManager.DestroyEntity(doomed);
                ChunkSystem.Instance?.Forget(gone);
            }

            if (m_Matched.Count > 0)
            {
                using (NativeArray<Entity> made = EntityManager.CreateEntity(m_Archetype, m_Matched.Count, Allocator.Temp))
                {
                    for (int i = 0; i < m_Matched.Count; i++)
                    {
                        EntityManager.SetComponentData(made[i], ItemOf(m_Matched[i], id));
                        m_Index.Add(id, made[i]);
                    }
                }
            }

            BatchJob job = m_Job;
            job.Finish(m_Matched.Count, m_Unmatched);
            double ms = timer.Elapsed.TotalMilliseconds;
            string note = $"{gone.Count} item(s) of before replaced, {m_Unmatched.Count} with no road";
            SlowLog.Add($"TsBatch: the write of {job.Written} item(s)", ms, SlowLog.IsSlow(ms) ? note : null);
            Mod.log.Info($"Batch '{job.Plan.Batch}' written: {job.Written} item(s), {note}; the write took {ms:0.00} ms, the matching {m_PrepareMs:0.0} ms over {m_Frames} frame(s).");
            EndJob();
        }

        /// <summary>The item a request's board becomes: as PlacementTool.Place makes one, but with the planner's reach and the finish the batch gives it, whatever the tool is set to.</summary>
        private static TsItem ItemOf(in Matched matched, uint batchId)
        {
            BatchItem board = matched.Item;
            var item = new TsItem
            {
                TypeHash = board.Def.Hash,
                Edge = matched.Edge,
                EdgeT = matched.T,
                Position = new float3(board.X, board.Y, board.Z),
                Yaw = board.Yaw,
                Flags = TsItem.FlagBaseReach,
                SupportHash = board.SupportHash,
                BatchId = batchId
            };
            item.SetSpec(board.Def.Template != null ? board.Spec : "");
            if (BoardTilt.Leans(SupportCatalog.For(board.Def, board.SupportHash)))
                item.SetTilt(BoardTilt.Default);
            return item;
        }

        /// <summary>The job in hand is done with (written, failed or superseded): what it held is let go.</summary>
        private void EndJob()
        {
            m_Job = null;
            m_Phase = Phase.Collect;
            m_Todo.Clear();
            m_Matched.Clear();
            m_Unmatched.Clear();
            ClearTable();
        }

        private void ClearTable()
        {
            DisposeCopies();
            m_Boxes.Clear();
            m_Hosts.Clear();
            m_Curves.Clear();
            m_Widths.Clear();
        }

        private void DisposeCopies()
        {
            if (m_EdgeEntities.IsCreated)
                m_EdgeEntities.Dispose();
            if (m_EdgeCurves.IsCreated)
                m_EdgeCurves.Dispose();
        }
    }
}
