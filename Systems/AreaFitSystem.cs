using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// Fits the area markings to the roads under them (0.12.4, AreaFit): the island, the box grid, the simplified grid and the gore are painted only where the road can be painted and lie on its surface.
    /// The renderer asks for an item's fit when it reads the item (FitFor); one that is not made yet, or was made for the item as it was, is made here, a few at a time within a budget, and the renderer
    /// reads those items again (TakeReady). When a road segment or junction is changed or removed, the areas over it are fitted again. Nothing is saved: the outline is the item's, the fit comes from the
    /// roads as they are (an area placed before 0.12.4 is fitted too). Main thread, in the main loop.
    /// </summary>
    internal sealed partial class AreaFitSystem : GameSystemBase
    {
        /// <summary>The time (ms) it may take of a frame, one item over at most (0.12.59: 3.0 before, 6.0 while a load goes on; the owner's profile showed it at 3.84 ms).</summary>
        private const double kBudgetMs = 2.0;
        private const int kSettleFrames = 8;

        /// <summary>
        /// The queues are done nearest the camera first (0.12.27, NearFirst): what is within this of it (m: the farthest tiles are drawn to 700 m), the rest when the camera comes near. They are ordered
        /// again when the camera has moved this far, or now and then while entries wait that are not ordered.
        /// </summary>
        private const float kNearMetres = 800f, kReorderMetres = 64f;

        /// <summary>While the city's signs are being made after a load (ChunkSystem.LoadFactor) the budget grows, up to this.</summary>
        private const double kMaxBudgetMs = 4.0;

        /// <summary>How many entries at the head of each queue are within kNearMetres, ordered (the rest wait), and where the camera was when they were ordered.</summary>
        private int m_QueueNear, m_GroundNear, m_OrderedFrame, m_Frame;
        private float m_OrderedAt;

        /// <summary>How many entries of each queue were there when it was last ordered (less those done since): what is queued after them has not been ordered yet.</summary>
        private int m_QueueOrdered, m_GroundOrdered;
        private float m_OrderX = float.NaN, m_OrderZ = float.NaN;

        /// <summary>The main thread's time of this system since the city was loaded (ms), for ChunkSystem's report of the load.</summary>
        internal double LoadMs { get; private set; }

        /// <summary>The fits and grounds that wait within reach of the camera or are not ordered yet (after a load: all of them, until the first ordering), and the ones made and not yet handed to the renderer.</summary>
        internal int NearWaiting => NearQueued + m_Ready.Count;

        /// <summary>What is queued is not waited for while the reads wait for road pieces the game is still loading (m_KerbHeld): the load's hold on the loading screen would else wait on the game's own loading.</summary>
        private int NearQueued => (m_KerbHeld ? 0 : m_QueueNear + m_GroundNear + m_LaidNear + (m_Queue.Count - m_QueueOrdered) + (m_GroundQueue.Count - m_GroundOrdered) + (m_LaidQueue.Count - m_LaidOrdered)) + Outstanding;

        internal int GroundsMade => m_GroundsMade;

        internal static AreaFitSystem Instance { get; private set; }

        private sealed class Entry
        {
            public string Key = "";
            public AreaFit Fit;
            public float MinX, MinZ, MaxX, MaxZ;
            public bool Boxed;

            /// <summary>The fit was made for Key, whatever it came to: no fit is a result too (an area with no paint), and is not asked for again until the item changes.</summary>
            public bool Made;

            /// <summary>Where the item is (for the order of the queue).</summary>
            public float X, Z;
        }

        private readonly Dictionary<Entity, Entry> m_Entries = new Dictionary<Entity, Entry>();
        private readonly List<Entity> m_Queue = new List<Entity>();
        private readonly HashSet<Entity> m_Queued = new HashSet<Entity>();
        private readonly List<Entity> m_Ready = new List<Entity>();
        private EntityQuery m_NetChanged;
        private Game.Net.SearchSystem m_NetSearch;
        private PrefabSystem m_Prefabs;
        private TerrainSystem m_Terrain;
        private readonly TerrainGate m_TerrainGate = new TerrainGate();
        private int m_SettleFrames;
        private int m_Made, m_Errors, m_NoFit, m_RoadChanges;

        /// <summary>
        /// A read takes a road piece whose mesh the game is still loading as one surface (SurfaceReader: reading it then breaks the game's loading of it for good). So while a piece waits, the reads wait
        /// (KerbHold, this long at the most), and what is made all the same is noted (ProvisionalLedger) and made again once a piece becomes known.
        /// </summary>
        private const double kKerbHoldSeconds = 20.0;

        private const int kAreaKind = 0, kGroundKind = 1, kLaidKind = 2;
        private readonly KerbHold m_KerbHold = new KerbHold(kKerbHoldSeconds);
        private readonly ProvisionalLedger<(int Kind, Entity Item)> m_Provisional = new ProvisionalLedger<(int, Entity)>();
        private readonly List<(int Kind, Entity Item)> m_Redo = new List<(int, Entity)>();
        private bool m_KerbHeld, m_KerbWasBusy;

        /// <summary>How many items that came to nothing (no fit, nothing to lay) are written to the log, to say which ones they are.</summary>
        private const int kNoneLogged = 10;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_NetChanged = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Road>() },
                Any = new[] { ComponentType.ReadOnly<Updated>(), ComponentType.ReadOnly<Deleted>() },
                None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_NetSearch = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        /// <summary>The state of the last city was let go at the end of the deserialization (LoadWarmSystem.Early): the end of the loading does not do it again.</summary>
        private bool m_ResetEarly;

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_ResetEarly = LoadWarmSystem.Early(serializationContext);
            if (m_ResetEarly)
                ResetForLoad();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (m_ResetEarly)
            {
                m_ResetEarly = false;
                return;
            }

            ResetForLoad();
        }

        private void ResetForLoad()
        {
            m_Entries.Clear();
            m_Queue.Clear();
            m_Queued.Clear();
            m_Ready.Clear();
            m_Grounds.Clear();
            m_GroundQueue.Clear();
            m_GroundQueued.Clear();
            m_SameSpot.Clear();
            ClearLaid();
            m_SettleFrames = 0;
            m_QueueNear = m_GroundNear = m_QueueOrdered = m_GroundOrdered = 0;
            m_OrderX = m_OrderZ = float.NaN;
            LoadMs = 0.0;
            m_KerbHold.Reset();
            m_KerbHeld = m_KerbWasBusy = false;
            m_Provisional.Clear(SurfaceReader.KerbEpoch);
            m_Redo.Clear();
            SurfaceReader.Reset();
            ResetWorker();
        }

        /// <summary>The key of what a fit was made for: the item's kind, settings and place.</summary>
        private static string KeyOf(in ItemRecord record) => $"{record.TypeHash}|{record.Spec}|{record.Position.X:R}|{record.Position.Y:R}|{record.Position.Z:R}|{record.Yaw:R}";

        /// <summary>
        /// The fit of an item as it is now, for the renderer: null for an item that is not a fitted area marking, or whose fit is not made yet (it is then queued, and drawn as its outline says until the fit
        /// is ready).
        /// </summary>
        internal AreaFit FitFor(Entity entity, in ItemRecord record)
        {
            if (!AreaFitRules.IsAreaHash(record.TypeHash))
            {
                if (m_Entries.Count > 0)
                    m_Entries.Remove(entity);
                return null;
            }

            SignDef def = SignCatalog.ByHash(record.TypeHash);
            if (def == null || !def.IsMarking || !MarkingTemplates.IsFittedArea(def.Id) || !MarkingTemplates.WantsFit(def, record.Spec))
            {
                m_Entries.Remove(entity);
                return null;
            }

            string key = KeyOf(record);
            if (m_Entries.TryGetValue(entity, out Entry entry) && entry.Key == key)
            {
                if (!entry.Made)
                    Queue(entity);
                return entry.Fit;
            }

            if (entry == null)
                m_Entries[entity] = entry = new Entry();
            entry.Key = key;
            entry.Fit = null;
            entry.Made = false;
            entry.X = record.Position.X;
            entry.Z = record.Position.Z;
            Queue(entity);
            return null;
        }

        private int m_FramesSinceRelease;
        private float m_ReleasedAt = -1f;

        /// <summary>
        /// The items whose fit or ground was made (the renderer reads them again); false when there are none. Given in batches (when nothing waits within reach of the camera, or every 30 frames or half
        /// a second, Cadence.BatchDue; 30 frames are 1.5 to 2.7 s under the loading screen): every item read again rebuilds its tile, and a city's signs are grounded a few at a time after loading. Until
        /// 0.12.27 a batch waited for the whole queue, the far signs too.
        /// </summary>
        internal bool TakeReady(List<Entity> into)
        {
            into.Clear();
            m_FramesSinceRelease++;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (m_ReleasedAt < 0f)
                m_ReleasedAt = now;
            if (!Cadence.BatchDue(m_Ready.Count, NearQueued, m_FramesSinceRelease, now - m_ReleasedAt))
                return false;
            into.AddRange(m_Ready);
            m_Ready.Clear();
            m_FramesSinceRelease = 0;
            m_ReleasedAt = now;
            return true;
        }

        /// <summary>What the reads of the roads' surface have cost since the load (SurfaceReader.Summary), for the load's line in the log.</summary>
        internal string ReadSummary => SurfaceReader.Summary();

        internal string Describe() =>
            $"{m_RoadChanges} change(s) of roads; area fits: {m_Entries.Count} area(s), {m_Queue.Count} waiting ({m_QueueNear} near the camera), {m_Made} made; post grounds: {m_Grounds.Count} sign(s), {m_GroundQueue.Count} waiting ({m_GroundNear} near the camera), {m_GroundsMade} made; {DescribeLaid()}; {ReadSummary}; {DescribeWorker()}";

        /// <summary>Where a queued item is: an area's place, or the place of a sign whose ground is asked for; null when it is not known (it is then dropped where it is looked at).</summary>
        private (float X, float Z)? FitPlace(Entity entity) => m_Entries.TryGetValue(entity, out Entry entry) ? (entry.X, entry.Z) : ((float, float)?)null;

        private (float X, float Z)? GroundPlace(Entity entity) => m_Grounds.TryGetValue(entity, out GroundEntry entry) ? (entry.Key.Item1, entry.Key.Item3) : ((float, float)?)null;

        /// <summary>Orders the queues nearest the camera first when it has moved or entries wait that are not ordered; with no camera every entry counts as near (as before).</summary>
        private void OrderQueues()
        {
            m_Frame++;
            ChunkSystem chunks = ChunkSystem.Instance;
            if (chunks == null || !chunks.TryCamera(out float cx, out float cz))
            {
                m_QueueNear = m_QueueOrdered = m_Queue.Count;
                m_GroundNear = m_GroundOrdered = m_GroundQueue.Count;
                m_LaidNear = m_LaidOrdered = m_LaidQueue.Count;
                return;
            }

            bool moved = float.IsNaN(m_OrderX) || Math.Abs(cx - m_OrderX) > kReorderMetres || Math.Abs(cz - m_OrderZ) > kReorderMetres;
            // New entries are ordered soon (at once when nothing near waits); the far ones that wait for the camera are not looked at again until it moves.
            bool fresh = m_Queue.Count > m_QueueOrdered || m_GroundQueue.Count > m_GroundOrdered || m_LaidQueue.Count > m_LaidOrdered;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!moved && !Cadence.OrderDue(fresh, m_QueueNear + m_GroundNear + m_LaidNear, m_Frame - m_OrderedFrame, now - m_OrderedAt))
                return;
            m_OrderedAt = now;
            m_QueueNear = NearFirst.Order(m_Queue, FitPlace, cx, cz, kNearMetres);
            m_GroundNear = NearFirst.Order(m_GroundQueue, GroundPlace, cx, cz, kNearMetres);
            m_LaidNear = NearFirst.Order(m_LaidQueue, LaidPlace, cx, cz, kNearMetres);
            m_QueueOrdered = m_Queue.Count;
            m_GroundOrdered = m_GroundQueue.Count;
            m_LaidOrdered = m_LaidQueue.Count;
            m_OrderX = cx;
            m_OrderZ = cz;
            m_OrderedFrame = m_Frame;
        }

        private void Queue(Entity entity)
        {
            if (!m_AreaOut.IsPending(entity) && m_Queued.Add(entity))
                m_Queue.Add(entity);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance.gameMode != GameMode.Game)
                return;
            ChunkSystem chunks = ChunkSystem.Instance;
            if (chunks == null || !chunks.Synced)
                return;
            // After loading, the roads' geometry is made in the first frames: the areas wait for it.
            if (m_SettleFrames < kSettleFrames)
            {
                m_SettleFrames++;
                return;
            }

            var clock = Stopwatch.StartNew();
            try
            {
                if (m_Entries.Count + m_Grounds.Count + m_Laid.Count > 0 && !m_NetChanged.IsEmptyIgnoreFilter)
                    RoadsChanged();
                WatchKerbs();
                OrderQueues();
                SurfaceReader.DrainShadow();
                // The finishing of an item reads the terrain, and the game's read blocks until the card's read back of a changed height map is done: while it is not, the reads of the roads and the taking wait
                // (the results of the worker are still collected) and the frames in between cost nothing; after TerrainGate.MaxHoldFrames the work goes on and blocks as it did.
                bool hasWork = m_Queue.Count + m_GroundQueue.Count + m_LaidQueue.Count + m_LaidContinue.Count + Outstanding + m_Worker.InFlight > 0;
                if (hasWork && (m_KerbHeld || m_TerrainGate.Hold(TerrainWait.Pending(m_Terrain))))
                {
                    TakeFinished(false);
                }
                else
                {
                    TakeFinished();
                    // Near the camera first; while a loaded city's signs are being made, more of a frame (ChunkSystem.LoadFactor, by how fast the frames are).
                    double budget = LoadBudget.Scaled(Math.Min(kMaxBudgetMs, kBudgetMs * ChunkSystem.LoadFactor));
                    int index = 0;
                    while (index < m_QueueNear && clock.Elapsed.TotalMilliseconds < budget && !WorkerFull)
                    {
                        Entity entity = m_Queue[index++];
                        m_Queued.Remove(entity);
                        int provisional = SurfaceReader.ProvisionalReads;
                        if (Async)
                            SubmitArea(entity);
                        else
                            Make(entity);
                        NoteProvisional(kAreaKind, entity, provisional);
                    }

                    m_Queue.RemoveRange(0, index);
                    m_QueueNear -= index;
                    m_QueueOrdered -= index;
                    index = 0;
                    while (index < m_GroundNear && clock.Elapsed.TotalMilliseconds < budget && !WorkerFull)
                    {
                        Entity entity = m_GroundQueue[index++];
                        m_GroundQueued.Remove(entity);
                        int provisional = SurfaceReader.ProvisionalReads;
                        if (Async)
                            SubmitGround(entity);
                        else
                            MakeGround(entity);
                        NoteProvisional(kGroundKind, entity, provisional);
                    }

                    m_GroundQueue.RemoveRange(0, index);
                    m_GroundNear -= index;
                    m_GroundOrdered -= index;
                    MakeLaid(clock, budget);
                    if (hasWork)
                    {
                        if (WorkerFull)
                            m_PassFull++;
                        else if (clock.Elapsed.TotalMilliseconds >= budget)
                            m_PassBudget++;
                        else
                            m_PassDry++;
                    }
                }
            }
            catch (Exception ex)
            {
                if (m_Errors++ < 10)
                    Mod.log.Error("Fitting the area markings failed: " + ex);
                m_Queue.Clear();
                m_Queued.Clear();
                m_GroundQueue.Clear();
                m_GroundQueued.Clear();
                m_LaidQueue.Clear();
                m_LaidQueued.Clear();
                m_QueueNear = m_GroundNear = m_LaidNear = m_QueueOrdered = m_GroundOrdered = m_LaidOrdered = 0;
                DropOutstanding();
                AsyncFailed();
            }

            double ms = clock.Elapsed.TotalMilliseconds;
            LoadMs += ms;
            SlowLog.Add("AreaFitSystem", ms, SlowLog.IsSlow(ms) ? Describe() : null);
        }

        /// <summary>The road pieces the game was still loading are tried again, the reads wait for them (m_KerbHeld) while there are some, and what was made without them is made again as they become known.</summary>
        private void WatchKerbs()
        {
            SurfaceReader.Poll(EntityManager);
            int busy = SurfaceReader.KerbBusy;
            m_KerbHeld = m_KerbHold.Holds(busy, UnityEngine.Time.realtimeSinceStartupAsDouble);
            if (m_KerbHold.TakeExpiry())
                Mod.log.Info($"Kerbs: {busy} road piece(s) are still being loaded by the game after {kKerbHoldSeconds:0} s: the reads go on and take them as one surface; what they made is made again when they are known.");
            if (busy > 0)
            {
                m_KerbWasBusy = true;
            }
            else if (m_KerbWasBusy)
            {
                m_KerbWasBusy = false;
                Mod.log.Info("Kerbs: no road piece waits for the game any more; " + SurfaceReader.KerbSummary() + ".");
            }

            if (!m_Provisional.TakeRedo(SurfaceReader.KerbEpoch, m_Redo))
                return;
            // As RoadsChanged: the heights read of the roads and the spot's kept ground are forgotten with the items.
            ForgetStand();
            foreach ((int kind, Entity entity) in m_Redo)
            {
                if (kind == kAreaKind)
                {
                    Queue(entity);
                }
                else if (kind == kLaidKind)
                {
                    QueueLaid(entity);
                }
                else if (m_Grounds.TryGetValue(entity, out GroundEntry entry))
                {
                    m_SameSpot.Remove(entry.Key);
                    foreach (KeyValuePair<Entity, GroundEntry> pair in m_Grounds)
                    {
                        if (pair.Value.Key.Equals(entry.Key))
                            QueueGround(pair.Key);
                    }
                }
            }

            Mod.log.Info($"Kerbs: {m_Redo.Count} item(s) made while a road piece was not known are made again.");
            m_Redo.Clear();
        }

        /// <summary>The item was made by a read that took a road piece as one surface (the count of such reads moved while it was made): it is made again when a piece becomes known.</summary>
        private void NoteProvisional(int kind, Entity entity, int provisionalBefore)
        {
            if (SurfaceReader.ProvisionalReads == provisionalBefore)
                return;
            m_Provisional.Note((kind, entity));
            // A marking is laid in the worker's way from reads made in slices before its lay: the lay itself sees no count move, so the item carries what its reads were.
            if (kind == kLaidKind && m_Laid.TryGetValue(entity, out LaidEntry laid))
                laid.Provisional = true;
        }

        /// <summary>The areas over a road segment or junction that was changed or removed are fitted again.</summary>
        private void RoadsChanged()
        {
            using (NativeArray<Entity> changed = m_NetChanged.ToEntityArray(Allocator.Temp))
            {
                var boxes = new List<(float, float, float, float)>();
                foreach (Entity e in changed)
                {
                    if (EntityManager.TryGetComponent(e, out EdgeGeometry edge))
                        boxes.Add((edge.m_Bounds.min.x, edge.m_Bounds.min.z, edge.m_Bounds.max.x, edge.m_Bounds.max.z));
                    else if (EntityManager.TryGetComponent(e, out NodeGeometry node))
                        boxes.Add((node.m_Bounds.min.x, node.m_Bounds.min.z, node.m_Bounds.max.x, node.m_Bounds.max.z));
                    else if (EntityManager.TryGetComponent(e, out Node position))
                        boxes.Add((position.m_Position.x - 20f, position.m_Position.z - 20f, position.m_Position.x + 20f, position.m_Position.z + 20f));
                }

                if (boxes.Count == 0)
                    return;
                m_RoadChanges++;
                StaleOutstanding(boxes);
                LaidRoadsChanged(boxes);
                // The roads changed under the signs' posts: their grounds are made again (not the same spot's kept one).
                foreach (KeyValuePair<Entity, GroundEntry> pair in m_Grounds)
                {
                    GroundEntry entry = pair.Value;
                    if (!entry.Boxed)
                        continue;
                    foreach ((float x0, float z0, float x1, float z1) in boxes)
                    {
                        if (x1 >= entry.MinX - 2f && x0 <= entry.MaxX + 2f && z1 >= entry.MinZ - 2f && z0 <= entry.MaxZ + 2f)
                        {
                            m_SameSpot.Remove(entry.Key);
                            QueueGround(pair.Key);
                            break;
                        }
                    }
                }

                foreach (KeyValuePair<Entity, Entry> pair in m_Entries)
                {
                    Entry entry = pair.Value;
                    if (!entry.Boxed)
                        continue;
                    foreach ((float x0, float z0, float x1, float z1) in boxes)
                    {
                        if (x1 >= entry.MinX - 2f && x0 <= entry.MaxX + 2f && z1 >= entry.MinZ - 2f && z0 <= entry.MaxZ + 2f)
                        {
                            Queue(pair.Key);
                            break;
                        }
                    }
                }
            }
        }

        private void Make(Entity entity)
        {
            if (!m_Entries.TryGetValue(entity, out Entry entry))
                return;
            if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
            {
                m_Entries.Remove(entity);
                return;
            }

            ItemRecord record = ChunkSystem.ToRecord(item);
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            long lap = Stopwatch.GetTimestamp();
            AreaFit fit = Compute(EntityManager, m_NetSearch, m_Prefabs, m_Terrain, def, record.Spec, record.Position, record.Yaw, out float minX, out float minZ, out float maxX, out float maxZ,
                ShadowObserver);
            double ms = (Stopwatch.GetTimestamp() - lap) * 1000.0 / Stopwatch.Frequency;
            // One item is the unit of the budget: a slow one is told apart from a frame of many.
            SlowLog.Add("AreaFitSystem: one area's fit", ms, SlowLog.IsSlow(ms) ? $"{def?.Id} at ({record.Position.X:0}, {record.Position.Z:0}), {fit?.Note}" : null);
            CompleteArea(entity, entry, record, def, fit, minX, minZ, maxX, maxZ);
        }

        /// <summary>The area's fit is made (or it came to none): kept for the item, and the renderer is told to read it again.</summary>
        private void CompleteArea(Entity entity, Entry entry, in ItemRecord record, SignDef def, AreaFit fit, float minX, float minZ, float maxX, float maxZ)
        {
            bool had = entry.Fit != null;
            entry.Key = KeyOf(record);
            entry.Fit = fit;
            entry.Made = true;
            entry.MinX = minX;
            entry.MinZ = minZ;
            entry.MaxX = maxX;
            entry.MaxZ = maxZ;
            entry.Boxed = fit != null;
            m_Made++;
            if (m_Made <= 20 && fit != null)
                Mod.log.Info($"Area fit: item {entity.Index}:{entity.Version} ({def?.Id}) on {fit.Note}.");
            else if (fit == null && m_NoFit++ < kNoneLogged)
                Mod.log.Info($"Area fit: item {entity.Index}:{entity.Version} ({def?.Id}) has no paint to fit.");
            // The renderer has the item without a fit already: it is read again for one it did not have or loses.
            if (fit != null || had)
                m_Ready.Add(entity);
        }

        /// <summary>What an area's fit is made from, read from the game on the main thread: the plan, the box of its paint (world) and the bands of road surface under it.</summary>
        private sealed class AreaRead
        {
            public MarkingPlan Plan;
            public float MinX, MinZ, MaxX, MaxZ;
            public SurfaceReader.Read Read;
        }

        /// <summary>The plan of an area marking at a place and the roads under the box of its paint (SurfaceReader); null when the marking has no paint.</summary>
        private static AreaRead ReadArea(EntityManager em, Game.Net.SearchSystem search, PrefabSystem prefabs, SignDef def, string spec, Vec3 position, float yaw)
        {
            MarkingPlan plan = def == null ? null : MarkingTemplates.PlanOf(def, spec);
            if (plan == null || plan.Parts.Count == 0 || !(plan.MaxX > plan.MinX) || !(plan.MaxZ > plan.MinZ))
                return null;
            List<Vec3> corners = MarkingPolygon.ToWorld(new List<Vec3> { new Vec3(plan.MinX, 0f, plan.MinZ), new Vec3(plan.MaxX, 0f, plan.MinZ), new Vec3(plan.MaxX, 0f, plan.MaxZ), new Vec3(plan.MinX, 0f, plan.MaxZ) }, position, yaw);
            var area = new AreaRead { Plan = plan, MinX = float.MaxValue, MinZ = float.MaxValue, MaxX = float.MinValue, MaxZ = float.MinValue };
            foreach (Vec3 c in corners)
            {
                area.MinX = Math.Min(area.MinX, c.X);
                area.MinZ = Math.Min(area.MinZ, c.Z);
                area.MaxX = Math.Max(area.MaxX, c.X);
                area.MaxZ = Math.Max(area.MaxZ, c.Z);
            }

            area.Read = SurfaceReader.Around(em, search, prefabs, area.MinX, area.MinZ, area.MaxX, area.MaxZ);
            return area;
        }

        private static FitJob AreaJob(AreaRead area, SignDef def, string spec, Vec3 position, float yaw) => new FitJob
        {
            Strips = area.Read.Strips,
            Origin = position,
            Yaw = yaw,
            MinX = area.Plan.MinX,
            MaxX = area.Plan.MaxX,
            MinZ = area.Plan.MinZ,
            MaxZ = area.Plan.MaxZ,
            Rule = new AreaRule { Plan = area.Plan, Spec = spec, Def = def }
        };

        private static int s_PartLogged;

        /// <summary>
        /// The fit of an area marking of the given settings at a place (also for the tool's preview): the roads under the box of its paint are read (SurfaceReader); the area is painted on the road only when
        /// its own place is on the road or the road covers at least AreaFitRules.MinCover of it, else it is laid on the ground. The box (world) is given back. Null when the marking has no paint.
        /// observe is given each cut made here (the worker's shadow compares it with its own).
        /// </summary>
        internal static AreaFit Compute(EntityManager em, Game.Net.SearchSystem search, PrefabSystem prefabs, TerrainSystem terrain, SignDef def, string spec, Vec3 position, float yaw,
            out float minX, out float minZ, out float maxX, out float maxZ, Action<FitJob, FitResult> observe = null)
        {
            minX = minZ = maxX = maxZ = 0f;
            AreaRead area = ReadArea(em, search, prefabs, def, spec, position, yaw);
            if (area == null)
                return null;
            minX = area.MinX;
            minZ = area.MinZ;
            maxX = area.MaxX;
            maxZ = area.MaxZ;
            var cut = default(FitResult);
            if (area.Read.Strips.Count > 0)
            {
                FitJob job = AreaJob(area, def, spec, position, yaw);
                cut = FitCut.Run(job);
                SurfaceReader.NoteFitMs(false, cut.CutMs);
                observe?.Invoke(job, cut);
            }

            return FinishArea(em, search, prefabs, terrain, area, cut, position, yaw);
        }

        /// <summary>What comes of an area once its road is cut (cut.Fit is null when there were no bands): the cut itself when the area is on the road, else the ground's heights under it. Main thread: the roads and the terrain.</summary>
        private static AreaFit FinishArea(EntityManager em, Game.Net.SearchSystem search, PrefabSystem prefabs, TerrainSystem terrain, AreaRead area, FitResult cut, Vec3 position, float yaw)
        {
            if (cut.Fit != null && cut.OnRoad)
            {
                // An area not wholly on the road: what the road was read as is written to the log (the first twenty), to tell a cut that is right (a kerb, grass) from one that is not.
                if (cut.Cover < 0.97f && s_PartLogged < 20)
                {
                    s_PartLogged++;
                    SurfaceReader.Around(em, search, prefabs, area.MinX, area.MinZ, area.MaxX, area.MaxZ, false, true);
                }

                return cut.Fit;
            }

            // On no road: the ground's heights.
            MarkingPlan plan = area.Plan;
            TerrainHeightData data = TerrainWait.HeightData(terrain);
            float baseY = position.Y;
            Func<float, float, float> heightAt = (x, z) =>
            {
                if (!data.isCreated)
                    return 0f;
                Vec3 w = MarkingPolygon.ToWorld(new List<Vec3> { new Vec3(x, 0f, z) }, position, yaw)[0];
                return TerrainUtils.SampleHeight(ref data, new float3(w.X, w.Y, w.Z)) - baseY;
            };
            return AreaFit.FromGround(heightAt, plan.MinX, plan.MaxX, plan.MinZ, plan.MaxZ);
        }
    }
}
