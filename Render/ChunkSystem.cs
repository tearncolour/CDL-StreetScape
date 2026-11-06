using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Serialization.Entities;
using Game;
using Game.Rendering;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace CDLC.TS.Render
{
    /// <summary>
    /// Draws every placed item. The entities with a TsItem are the truth; this system keeps a copy of them as plain records in a store with an index by map tile (ItemStore), and builds one mesh per
    /// tile and material, so the nearby map is a few hundred renderers whatever the number of signs. Since 0.7.0 it is made for thousands of signs:
    ///  - the items are read from the game one change at a time (and in slices, when all of them are), not all of them every 2.5 s;
    ///  - only the tiles near the camera are built, each at the detail its distance asks for (TileStreamer): near tiles have full-size pictures, middle ones half-size, far ones small pictures and
    ///    supports in reduced detail; a tile that is too far has nothing built at all, and its memory is given back;
    ///  - a tile is built on a worker thread (TileWorker); the main thread only makes the Unity objects from the result, a part at a time, within a budget;
    ///  - the pictures of the faces are painted on another worker (FacePainter) and put on the materials when they are ready (TsMaterials.PumpFaces).
    /// The renderers are GameObjects in layer 0, the one route the other CDL mods have proven in this game. Main thread.
    /// </summary>
    internal sealed partial class ChunkSystem : GameSystemBase
    {
        /// <summary>The margin that stops a built tile flickering at the edge of its reach (the tiers' own limits, and the far limit that hides a tile, are in StreamOptions).</summary>
        private const float kHysteresis = 0.1f;
        private const int kVisibilityFrames = 20;
        private const int kNightFrames = 30;
        private const int kPruneFrames = 180;
        private const int kBudgetFrames = 60;

        // What the main thread may spend in a frame on each kind of work (ms); each does at least one unit when there is one, so that nothing waits for ever. 0.12.72 (the owner's profile showed 1.00 ms in
        // the first minutes after a load, "<1ms" asked for): they add up to 0.9, and they no longer grow with LoadFactor, which made them 3 ms while the frames were fast; a load is slower for it.
        private const double kSyncMs = 0.25, kApplyMs = 0.35, kFaceMs = 0.2, kDropMs = 0.1;

        /// <summary>
        /// What the main thread may spend in an update on the stages of the baked-detail materials the ready tiles ask for (0.12.76; ms): a stage that begins within it is not cut short, so a frame is
        /// the longest single stage (the compression of a map) and not the whole material: the owner's 0.12.75 log had 40.7 ms for one part whose material was made in it.
        /// </summary>
        private const double kWarmMs = 1.0;
        private const int kMaxJobs = 3;

        /// <summary>A new plan for changed items waits this many frames or this many seconds (Cadence): 6 frames are 0.5 s under the loading screen's 11 to 20 frames a second.</summary>
        private const int kPlanFrames = 6;
        private const float kPlanSeconds = 0.1f;

        /// <summary>A full read of the items is made now and then as a net under the changes (every minute at 60 fps), and when the count of items is not the store's.</summary>
        private const int kSafetyFrames = 3600;

        /// <summary>More changed items than this in one frame are read as a full read, in slices, rather than at once.</summary>
        private const int kBigChange = 256;

        private const int kMaxFailures = 3;

        /// <summary>
        /// Nothing is planned while the first full read of the items is under way (its slices are some 2000 items a frame, so a big city takes a second or two): the tiles would be built from what has
        /// been read so far and built again as more of it comes. After this many frames of it, tiles are planned anyway.
        /// </summary>
        private const int kFirstReadFrames = 180;

        /// <summary>A tile that is built again (a sign was edited or hidden) gets this many times the budget for its objects: the old one is on screen until the new one is complete, and the wait is seen.</summary>
        private const double kReplaceFactor = 3.0;

        private sealed class TileView
        {
            public readonly List<GameObject> Objects = new List<GameObject>();
            public readonly HashSet<string> Faces = new HashSet<string>();
            public Vec3 Origin;
            public bool Visible = true;
            public int Vertices;

            /// <summary>What its meshes take on the graphics card (Footprint.MeshBytes): for the panel's figures.</summary>
            public long MeshBytes;
            public TileTier Tier;
            public int Version;
        }

        private sealed class Job
        {
            public Task<TileResult> Task;
            public TileTier Tier;
            public int Version;
        }

        /// <summary>The tile whose meshes are being made into Unity objects, a part or two a frame.</summary>
        private sealed class Applying
        {
            public TileResult Result;
            public int Next;
            public int Index;
            public readonly List<GameObject> Objects = new List<GameObject>();
            public readonly HashSet<string> Faces = new HashSet<string>();
            public int Vertices;
            public long MeshBytes;
            public double Ms;

            /// <summary>The tile has a view that this one replaces.</summary>
            public bool Replaces;

            /// <summary>The materials of this tile that are known to be there (their parts need no more asking), and since when (Time.realtimeSinceStartup) a part has been waiting for its material; negative when none.</summary>
            public readonly HashSet<string> Known = new HashSet<string>();
            public float WaitSince = -1f;
        }

        private readonly HashSet<string> m_AskedScratch = new HashSet<string>();

        internal static ChunkSystem Instance { get; private set; }

        private EntityQuery m_Items;
        private EntityQuery m_Changed;
        private readonly ItemStore<Entity> m_Store = new ItemStore<Entity>();
        private readonly Dictionary<TileKey, TileView> m_Views = new Dictionary<TileKey, TileView>();
        private readonly HashSet<TileKey> m_Stale = new HashSet<TileKey>();
        private readonly HashSet<TileKey> m_DirtyScratch = new HashSet<TileKey>();
        private readonly Dictionary<TileKey, Job> m_Jobs = new Dictionary<TileKey, Job>();
        private readonly Dictionary<TileKey, KeyValuePair<int, int>> m_Failures = new Dictionary<TileKey, KeyValuePair<int, int>>();

        /// <summary>The tiles that came out of a build with nothing to draw (every item of an unknown type), at which version: not built again until their items change.</summary>
        private readonly Dictionary<TileKey, int> m_EmptyAt = new Dictionary<TileKey, int>();
        private readonly Queue<TileResult> m_Ready = new Queue<TileResult>();
        private readonly Queue<TileKey> m_DropQueue = new Queue<TileKey>();
        private readonly List<TileWish> m_ToBuild = new List<TileWish>();
        private readonly List<TileKey> m_ToDrop = new List<TileKey>();
        private readonly HashSet<Entity> m_Traffic = new HashSet<Entity>();

        /// <summary>The text screens that follow the game (src = auto), kept as the items are read, like the traffic screens: the reader of the roads ahead goes over these only.</summary>
        private readonly HashSet<Entity> m_Auto = new HashSet<Entity>();
        private int m_SeenAutoChanges;

        /// <summary>The areas that clear the game's markings (mk.clear), kept as the items are read, for ClearMarkingsSystem; ClearChanges goes up when one is placed, changed or gone.</summary>
        private readonly HashSet<Entity> m_Clears = new HashSet<Entity>();
        private static uint s_ClearHash;
        private readonly List<Entity> m_RemovedScratch = new List<Entity>();
        private readonly HashSet<Entity> m_AppliedHidden = new HashSet<Entity>();
        private Applying m_Applying;
        private Func<TileKey, TileTier> m_TierOf;
        private Func<TileKey, bool> m_IsStale, m_HasItems;
        /// <summary>The draw distance the player's preferences file holds (0.12.103): set when the mod loads, before this system is made.</summary>
        internal static float StartingViewDistance = StreamOptions.DefaultFarMetres;

        private float m_FarMetres = StreamOptions.ClampFar(StartingViewDistance);
        private StreamOptions m_Options = StreamOptions.WithFar(StartingViewDistance);
        private bool m_Replan;
        private float m_Scale = 1f;
        private int m_LoggedScales;

        private GameObject m_Root;
        private CameraUpdateSystem m_Camera;
        private LightingSystem m_Lighting;
        private int m_Frame;
        private int m_Epoch;
        private float m_CamX, m_CamZ;
        private bool m_HaveCamera;

        /// <summary>Where the camera looks at the ground (the orbit camera's pivot) when it is the orbit camera that is active: the point the player watches, which can be hundreds of metres from the camera itself.</summary>
        internal bool TryLookAt(out float x, out float z)
        {
            x = z = 0f;
            var orbit = m_Camera.orbitCameraController;
            if (orbit == null || !ReferenceEquals(m_Camera.activeCameraController, orbit))
                return false;
            Vector3 pivot = orbit.pivot;
            x = pivot.x;
            z = pivot.z;
            return !float.IsNaN(x) && !float.IsNaN(z);
        }

        /// <summary>Where the camera was at the last plan (the ground of the posts is made nearest it first: AreaFitSystem).</summary>
        internal bool TryCamera(out float x, out float z)
        {
            x = m_CamX;
            z = m_CamZ;
            return m_HaveCamera;
        }

        /// <summary>
        /// How many times its budget the main thread may give the work that is waiting while a loaded city's signs are made (0.12.27; the owner: "主要还是加载时间优化"): up to three times while the frames are
        /// fast, less as they slow down (by their average time), 1 when they are slow, when the signs round the camera are complete (the load's report) or kLoadSeconds after the load. Since 0.12.72 only
        /// the area fits use it (AreaFitSystem): this system's own budgets are fixed.
        /// </summary>
        internal static float LoadFactor { get; private set; } = 1f;

        private const float kLoadSeconds = 120f, kReportSeconds = 180f;

        /// <summary>
        /// After a load a tile that has not been built waits for the grounds of the posts near the camera, at most this long after the items were read (s): built before them it was built again when they
        /// came, its objects made twice and its posts seen to jump. A tile that is built is built again as before.
        /// </summary>
        private const float kHoldSeconds = 1.5f;
        private float m_ReadAt = -1f;
        private float m_LoadedAt = -1f, m_FrameSeconds = 1f / 30f;
        private bool m_LoadReported;
        private double m_LoadMainMs;
        private int m_LoadBuiltBefore, m_LoadPaintedBefore, m_LoadGroundsBefore;

        // The full read of the items in slices.
        private bool m_Resync = true;
        private bool m_PassActive, m_PassAgain;
        private int m_PassCursor, m_PassLength = -1, m_PassFrames;
        private double m_PassMs;
        private int m_NextMismatchCheck;
        private bool m_WarnedMismatch;
        private bool m_LoggedFirstRead;
        private int m_SeenStoreChanges, m_SeenTrafficChanges;
        private static bool s_HiddenChanged;

        private int m_LoggedBuilds, m_LoggedDrops, m_LoggedErrors;
        private int m_Built, m_Dropped, m_Applied, m_Rebuilt;
        private double m_BuildMsTotal, m_WorkerMsTotal;

        /// <summary>The tiles made into objects so far, and how many of those replaced a tile that was already drawn (the load's hold tells how many it spent on a rebuild).</summary>
        internal int BuiltTotal => m_Built;

        internal int RebuiltTotal => m_Rebuilt;

        /// <summary>The finished builds that were thrown away because an item of their tile was written while they were built.</summary>
        internal int SupersededTotal => m_Superseded;

        /// <summary>
        /// Asked of a tile that is to be built: true while the road follow is still writing its items (FollowScheduler.IsBursting), so that the build waits for the last of them and is not thrown away at the
        /// next write. A tile is not held longer than kMaxHoldFrames, whatever the follow says.
        /// </summary>
        internal Func<TileKey, bool> HoldBuild { get; set; }

        private const int kMaxHoldFrames = 90;
        private readonly Dictionary<TileKey, int> m_HeldSince = new Dictionary<TileKey, int>();
        private int m_Superseded;

        internal IReadOnlyDictionary<Entity, ItemRecord> Records => m_Store;

        /// <summary>The store of the items, for the questions about a place or a line (the tool).</summary>
        internal ItemStore<Entity> Store => m_Store;

        /// <summary>The traffic-state screens (items), kept as the items are read, so that the reader of the road network need not look through all the items every second.</summary>
        internal IReadOnlyCollection<Entity> TrafficScreens => m_Traffic;

        /// <summary>The text screens that follow the game (VmsAuto), for the system that reads the road ahead of each.</summary>
        internal IReadOnlyCollection<Entity> AutoScreens => m_Auto;

        internal bool IsAuto(Entity entity) => m_Auto.Contains(entity);

        internal IReadOnlyCollection<Entity> ClearItems => m_Clears;

        /// <summary>Whether an item is an mk.clear area (ClearItems also has the area markings that hide the game's markings under them).</summary>
        internal static bool IsClearArea(in ItemRecord record) => record.TypeHash == ClearHash && ClearHash != 0u;

        /// <summary>How many of ClearItems are mk.clear areas.</summary>
        internal int ClearAreaCount
        {
            get
            {
                int count = 0;
                foreach (Entity entity in m_Clears)
                {
                    if (m_Store.TryGetValue(entity, out ItemRecord record) && IsClearArea(record))
                        count++;
                }

                return count;
            }
        }

        internal int ClearChanges { get; private set; }

        private static uint ClearHash
        {
            get
            {
                if (s_ClearHash == 0u)
                    s_ClearHash = SignCatalog.ById(VanillaClear.Id)?.Hash ?? 0u;
                return s_ClearHash;
            }
        }

        /// <summary>Goes up when the items are dropped (a city is left): whatever was made from the items of an earlier one (tile versions start again from 1) is out of date.</summary>
        internal int Epoch => m_Epoch;

        internal int RendererCount { get; private set; }
        internal int VertexCount { get; private set; }
        internal double LastBuildMs { get; private set; }

        /// <summary>Tiles that are to be built, being built, or waiting to be made into objects.</summary>
        internal int PendingTiles => m_ToBuild.Count + m_Jobs.Count + m_Ready.Count + (m_Applying != null ? 1 : 0);

        /// <summary>
        /// Raised whenever the items change in any way that is drawn (an item placed, deleted, moved or edited, what a live screen shows) and when the items are dropped (a city is loaded or left).
        /// What TsApi.Revision tells other mods. Synced is false until the items of the city have been read once.
        /// </summary>
        internal int Revision { get; private set; }

        internal bool Synced { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_Items = GetEntityQuery(ComponentType.ReadOnly<TsItem>());

            // A SECOND query, different by its description. GetEntityQuery returns one and the same query for equal descriptions, so 0.1.0 and 0.1.1
            // (which asked twice for the same one) put the changed-version filter on BOTH: reading the items then gave only the items in chunks that
            // had changed since the last frame, the record list was emptied on the next frame, and the signs were built and torn down at every click
            // (and the signs of a loaded city were never read). m_Items must stay unfiltered; only m_Changed has the filter.
            m_Changed = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TsItem>() },
                None = new[] { ComponentType.ReadOnly<Game.Common.Deleted>() }
            });
            m_Changed.SetChangedVersionFilter(ComponentType.ReadOnly<TsItem>());
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_Lighting = World.GetOrCreateSystemManaged<LightingSystem>();
            m_TierOf = TierOf;
            m_IsStale = key => m_Stale.Contains(key);
            m_HasItems = key => m_Store.HasItems(key);
            m_NotBuilt = key => !m_Views.ContainsKey(key);

            // The dots of the LED screens (variable-message signs) come from the painter's picture, which only the game side has (in the fonts SignFonts chose when the mod was loaded).
            SignBuilder.LedGrids = LedScreens.Grid;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            DestroyAll();
            Instance = null;
            base.OnDestroy();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            DestroyAll();
            DestroyItems(purpose);
        }

        /// <summary>
        /// The items of the city that is left are destroyed before the next one is read (0.12.5). The game's own clearing before a load (Game.Serialization.ClearSystem) destroys only the entities with one
        /// of its own components (PrefabRef and nineteen others); an item is an entity with TsItem alone, so until now the signs and markings of one city stayed in the world when another was loaded in the
        /// same session, were drawn in it, and were written into its file when it was saved (the autosave too). Found by the Racing mod, whose areas did that in the owner's game on
        /// 21 Oct. GameManager.LoadSimulationData calls this right before the next city's data is read, for every purpose (a saved game, a new game, the editor).
        /// </summary>
        private void DestroyItems(Purpose purpose)
        {
            // What was hidden was the items of the city that is left (the tool shows them instead): a new city starts with everything drawn.
            s_Hidden.Clear();
            m_AppliedHidden.Clear();
            try
            {
                int count = m_Items.CalculateEntityCount();
                if (count > 0)
                    EntityManager.DestroyEntity(m_Items);
                // The tool's history is of the city that is left (0.12.50).
                PlacementTool.ForgetHistory();
                Mod.log.Info($"Before loading ({purpose}): {count} item(s) of the city that was open destroyed, so that they do not go into the next one.");
            }
            catch (Exception ex)
            {
                Mod.log.Error("Destroying the items before a load failed: " + ex);
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Resync = true;
            m_LoadedAt = UnityEngine.Time.realtimeSinceStartup;
            m_ReadAt = -1f;
            m_LoadReported = false;
            m_LoadMainMs = 0.0;
            m_LoadBuiltBefore = m_Built;
            m_LoadPaintedBefore = FacePainter.Painted;
            m_LoadGroundsBefore = AreaFitSystem.Instance != null ? AreaFitSystem.Instance.GroundsMade : 0;
        }

        /// <summary>The budgets' factor for this frame (LoadFactor), from the average time of the frames: three times under 20 ms, twice under 28 ms, else as they are.</summary>
        private void UpdateLoadFactor()
        {
            m_FrameSeconds += (Mathf.Clamp(UnityEngine.Time.unscaledDeltaTime, 0f, 0.25f) - m_FrameSeconds) * 0.1f;
            bool loading = m_LoadedAt >= 0f && !m_LoadReported && UnityEngine.Time.realtimeSinceStartup - m_LoadedAt < kLoadSeconds;
            LoadFactor = !loading ? 1f : m_FrameSeconds < 0.020f ? 3f : m_FrameSeconds < 0.028f ? 2f : 1f;
        }

        /// <summary>
        /// Once after a load: how long the signs round the camera took to be complete (every tile wanted is built from its items as they are, their pictures are on, the posts' grounds near the camera are
        /// made) and what it cost, or, after kReportSeconds, what was still waiting then. The figure the loading time is judged by.
        /// </summary>
        private void ReportLoad(double frameMs)
        {
            if (m_LoadedAt < 0f || m_LoadReported)
                return;
            m_LoadMainMs += frameMs;
            AreaFitSystem fits = AreaFitSystem.Instance;
            float seconds = UnityEngine.Time.realtimeSinceStartup - m_LoadedAt;
            bool complete = Synced && !m_PassActive && m_HaveCamera && m_ToBuild.Count == 0 && m_Jobs.Count == 0 && m_Ready.Count == 0 && m_Applying == null && m_Stale.Count == 0
                && FacePainter.Waiting == 0 && FacePainter.Pending == 0 && (fits == null || fits.NearWaiting == 0);
            if (!complete && seconds < kReportSeconds)
                return;
            m_LoadReported = true;
            LoadFactor = 1f;
            if (m_Store.Count == 0)
                return;
            int built = m_Built - m_LoadBuiltBefore, painted = FacePainter.Painted - m_LoadPaintedBefore, grounds = fits != null ? fits.GroundsMade - m_LoadGroundsBefore : 0;
            Mod.log.Info($"Load: {(complete ? "the signs round the camera were complete" : "the signs round the camera were not complete")} {seconds:0.0} s after the city was loaded: {m_Store.Count} item(s), "
                + $"{m_Views.Count} tile(s) built, {Math.Max(0, built - m_Views.Count)} of them again, {painted} picture(s) painted ({FacePainter.Encoded} as BC1 so far, {TsMaterials.FaceTextureCount} held, "
                + $"{TsMaterials.FaceTextureBytes / 1048576f:0.0} MB), {grounds} post ground(s) made; {TsMaterials.WarmSummary}; the main thread gave it {m_LoadMainMs:0} ms here and {(fits != null ? fits.LoadMs : 0.0):0} ms in the area fits ({fits?.ReadSummary}; {fits?.DescribeWorker()}); "
                + (complete ? "" : $"still waiting: {m_ToBuild.Count} tile(s) to build, {m_Jobs.Count} on workers, {m_Ready.Count + (m_Applying != null ? 1 : 0)} to make, {m_Stale.Count} stale, {FacePainter.Waiting} picture(s) to paint and {FacePainter.Pending} to put on, {fits?.Describe()}; ")
                + $"frames {m_FrameSeconds * 1000f:0} ms on average at the end.");
        }

        /// <summary>
        /// What is still to be made of the signs round the camera, for the load's hold on the loading screen (the same test as ReportLoad's, counted): known once the items have been read and the camera
        /// is known. The tiles to build, on workers, to make into objects, stale; the pictures to paint and encode.
        /// </summary>
        internal int FirstUseWaiting(out bool known)
        {
            known = Synced && !m_PassActive && m_HaveCamera;
            return m_ToBuild.Count + m_Jobs.Count + m_Ready.Count + (m_Applying != null ? 1 : 0) + m_Stale.Count;
        }

        /// <summary>Every tile is built again from the items as they are, those that are on a worker too (the road paint's experiment changed what a build makes). Returns how many tiles.</summary>
        internal int RebuildAll() => m_Store.BumpAll();

        private TileTier TierOf(TileKey key) => m_Views.TryGetValue(key, out TileView view) ? view.Tier : TileTier.None;

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance.gameMode != GameMode.Game)
            {
                if (m_Root != null)
                    m_Root.SetActive(false);
                return;
            }

            if (m_Root != null && !m_Root.activeSelf)
                m_Root.SetActive(true);

            m_Frame++;
            var clock = Stopwatch.StartNew();
            UpdateLoadFactor();

            // Each step on its own: one that throws is logged (ten times in all) and the others go on, rather than the whole system failing at every frame.
            try { Sync(clock); } catch (Exception ex) { Fail("reading the items", ex); }
            double syncMs = clock.Elapsed.TotalMilliseconds;

            try
            {
                Plan();
                StartJobs();
                CollectJobs();
            }
            catch (Exception ex)
            {
                Fail("planning the tiles", ex);
            }

            double planMs = clock.Elapsed.TotalMilliseconds - syncMs;

            try
            {
                MeshUpload.RetryCleanup();
                TsMaterials.PumpMarking();
                m_ApplyWorstMs = 0.0;
                m_WarmMs = TsMaterials.WarmStep(LoadBudget.Scaled(kWarmMs));
                ApplyStep(LoadBudget.Scaled(kApplyMs));
                DropStep(LoadBudget.Scaled(kDropMs));
            }
            catch (Exception ex)
            {
                Fail("making the objects", ex);
            }

            double applyMs = clock.Elapsed.TotalMilliseconds - syncMs - planMs;

            int faces = 0;
            try { faces = TsMaterials.PumpFaces(LoadBudget.Scaled(kFaceMs)); } catch (Exception ex) { Fail("putting the pictures on", ex); }
            double total = clock.Elapsed.TotalMilliseconds;
            ReportLoad(total);
            LastBuildMs = applyMs;

            if (m_Frame % kVisibilityFrames == 0)
                UpdateVisibility();
            if (m_Frame % kNightFrames == 0)
                UpdateNight();
            if (m_Frame % kBudgetFrames == 0)
                AdaptBudget();
            if (m_Frame % kPruneFrames == 0)
                PruneFaces();

            SlowLog.Add("ChunkSystem", total, SlowLog.IsSlow(total) ? $"read {syncMs:0.0} ms, plan {planMs:0.0} ms, make objects {applyMs:0.0} ms, detail material stages {m_WarmMs:0.0} ms, {faces} picture(s) put on{(applyMs >= 8.0 ? $", the slowest part {m_ApplyWorstMs:0.0} ms ({m_ApplyWorstPart}, {m_ApplyWorstVertices} vertices{(m_ApplyWorstMade ? ", a material made in it" : "")})" : "")}; {m_Store.Count} item(s), {m_Views.Count} tile(s) built, {m_Jobs.Count} building, {m_Ready.Count} ready, {FacePainter.Waiting} picture(s) waiting" : null);
            SlowLog.Tick();
        }

        private int m_LoggedFailures;

        /// <summary>The slowest part made in this update's making of objects, for the slow line: nothing bounds a part but its size, and what made one slow (a material made for the first time, a big mesh) was not in the log.</summary>
        private double m_ApplyWorstMs;
        private string m_ApplyWorstPart = "";
        private int m_ApplyWorstVertices;
        private bool m_ApplyWorstMade;

        /// <summary>What this update's stages of the detail materials took, for the slow line.</summary>
        private double m_WarmMs;

        /// <summary>The materials of a tile that has been built are asked for at once, while it waits in the queue to be made into objects: the ones with baked detail start to be made, a stage an update.</summary>
        private void AskForMaterials(TileResult result)
        {
            m_AskedScratch.Clear();
            foreach (PreparedPart part in result.Parts)
            {
                if (m_AskedScratch.Add(part.Material))
                    TsMaterials.Need(part.Material);
            }

            m_AskedScratch.Clear();
        }

        private void Fail(string stage, Exception ex)
        {
            if (m_LoggedFailures++ < 10)
                Mod.log.Error($"Chunks: {stage} failed: {ex}");
        }

        // ------------------------------------------------------------------ reading the items

        /// <summary>An item as the renderer and the tool's picking know it.</summary>
        internal static ItemRecord ToRecord(in TsItem item) =>
            new ItemRecord(item.TypeHash, new Vec3(item.Position.x, item.Position.y, item.Position.z), item.Yaw, item.SupportHash, item.Spec.Length > 0 ? item.Spec.ToString() : null, item.Reach, item.Group, item.Slot, item.Lane, item.OffsetX) { Manual = item.Manual, Auto = item.Auto, Turn = item.Turn, Back = item.Back, Tilt = BoardTilt.Radians(item.TiltSteps), Rise = item.Rise, Clearance = item.Clearance, Style = item.Style };

        private static readonly HashSet<Entity> s_Hidden = new HashSet<Entity>();

        /// <summary>
        /// The items that are not drawn (the tool shows the support with a sign put on it in their place). A change makes the renderer rebuild the tiles that changed. An empty set shows everything.
        /// </summary>
        internal static void SetHidden(IEnumerable<Entity> entities)
        {
            var next = new HashSet<Entity>(entities ?? new Entity[0]);
            if (next.SetEquals(s_Hidden))
                return;
            s_Hidden.Clear();
            s_Hidden.UnionWith(next);
            s_HiddenChanged = true;
        }

        /// <summary>
        /// Brings the store up to date with the game's items, within the budget: the items that changed are read at once (a few), a lot of changes or a count that does not match start a full read, and
        /// a full read goes on a slice at a time. What is read marks the tiles it changes.
        /// </summary>
        private void Sync(Stopwatch clock)
        {
            // The changes since the last frame. The filter is on the query, which is only read through here: the changes of a frame in which this was not asked would be lost.
            if (!m_Changed.IsEmpty)
            {
                if (m_Changed.CalculateEntityCount() > kBigChange)
                {
                    // Too many to read in a frame: a full read, in slices. One that is running is not started again (it might never end if the changes go on): it goes once more when it is done.
                    if (m_PassActive)
                        m_PassAgain = true;
                    else
                        m_Resync = true;
                }
                else
                {
                    ReadChanged();
                }
            }

            if (m_SeenTrafficChanges != TrafficMap.Changes)
            {
                m_SeenTrafficChanges = TrafficMap.Changes;
                RefreshTraffic();
            }

            if (m_SeenAutoChanges != VmsAuto.Changes)
            {
                m_SeenAutoChanges = VmsAuto.Changes;
                RefreshAuto();
            }

            if (s_HiddenChanged)
            {
                s_HiddenChanged = false;
                ApplyHidden();
            }

            // The area markings whose fit to the road was made: read again, with it.
            if (AreaFitSystem.Instance != null && AreaFitSystem.Instance.TakeReady(m_FitReady))
            {
                foreach (Entity entity in m_FitReady)
                {
                    if (EntityManager.Exists(entity) && EntityManager.HasComponent<TsItem>(entity))
                        Put(entity, EntityManager.GetComponentData<TsItem>(entity));
                }
            }

            if (!m_PassActive && (m_Resync || m_Frame % kSafetyFrames == 0 || (m_Frame >= m_NextMismatchCheck && m_Store.Count != m_Items.CalculateEntityCount())))
            {
                m_Resync = false;
                m_NextMismatchCheck = m_Frame + 120;
                StartPass();
            }

            if (m_PassActive)
                StepPass(clock);

            if (m_Store.Changes != m_SeenStoreChanges)
            {
                m_SeenStoreChanges = m_Store.Changes;
                Revision++;
            }

            // The tiles that changed: a tile that is built is stale and is built again; one that is not built needs nothing (it is built from the items as they are when it is wanted).
            m_Store.TakeDirty(m_DirtyScratch);
            if (m_DirtyScratch.Count > 0)
            {
                foreach (TileKey key in m_DirtyScratch)
                {
                    if (m_Views.ContainsKey(key))
                        m_Stale.Add(key);
                }

                m_DirtyScratch.Clear();
            }
        }

        /// <summary>
        /// Items that were destroyed in the same frame as the ones that replace them (the batch interface, 0.12.67): forgotten now. The count check that finds a deletion compares the number of
        /// items, and an equal number of new ones would hide it; a full read after it is not wanted either, so the removal is done as the pass's end does it. The tool's delete and undo tell it the
        /// same way (0.12.78; the owner: a deleted sign took one or two seconds to go): the count check runs once in 120 frames, and only then did the tile start to be built again.
        /// </summary>
        internal void Forget(List<Entity> gone)
        {
            foreach (Entity entity in gone)
                Forget(entity);
        }

        internal void Forget(Entity entity)
        {
            m_Store.Remove(entity);
            if (m_Clears.Remove(entity))
                ClearChanges++;
            m_Traffic.Remove(entity);
            ForgetAuto(entity);
        }

        /// <summary>
        /// Destroys items of the world that other code (the panel's restore, the save's leak check) takes away, and tells the store in the same call: until the count check found them (once in 120
        /// frames) the store held records of entities that were gone, which the tool then read.
        /// </summary>
        internal void Destroy(List<Entity> items)
        {
            foreach (Entity item in items)
            {
                if (EntityManager.Exists(item))
                    EntityManager.DestroyEntity(item);
            }

            Forget(items);
        }

        /// <summary>An automatic screen leaves the renderer: what the reader published for it goes too, unless another screen has taken the index (the world reuses an index with the next version).</summary>
        private void ForgetAuto(Entity entity)
        {
            if (!m_Auto.Remove(entity))
                return;
            foreach (Entity other in m_Auto)
            {
                if (other.Index == entity.Index)
                    return;
            }

            VmsAuto.Remove(entity.Index);
        }

        private void Put(Entity entity, in TsItem item)
        {
            TsBatchSystem.Note(entity, item.BatchId);
            ItemRecord record = ToRecord(item);
            // The settings of a traffic-state screen as it is drawn: its own, with the number the reader publishes under and the revision of what it published (never saved).
            if (TrafficMap.IsTrafficScreen(item.TypeHash))
            {
                record.Spec = TrafficMap.Decorate(record.Spec, entity.Index, TrafficMap.RevisionOf(entity.Index));
                m_Traffic.Add(entity);
            }
            else
            {
                m_Traffic.Remove(entity);
            }

            // An automatic screen (text, speed, lane) is drawn with what the road ahead says (VmsAuto.Decorate); a screen that was changed back to manual stops being one.
            if (VmsAuto.IsAutoScreen(item.TypeHash, record.Spec ?? ""))
            {
                m_Auto.Add(entity);
                record.Spec = VmsAuto.Decorate(item.TypeHash, record.Spec, VmsAuto.Get(entity.Index));
            }
            else
            {
                m_Auto.Remove(entity);
            }

            record.Hidden = s_Hidden.Contains(entity);
            // An area marking is drawn fitted to the road under it once its fit is made (AreaFitSystem; 0.12.4).
            record.Fit = AreaFitSystem.Instance != null ? AreaFitSystem.Instance.FitFor(entity, record) : null;
            // A sign's posts stand on the ground under each of them once it is read (0.12.5).
            record.Ground = AreaFitSystem.Instance != null ? AreaFitSystem.Instance.GroundFor(entity, record) : null;
            // A marking lies on the ground as it is now once that is read (0.12.32).
            record.Laid = AreaFitSystem.Instance != null ? AreaFitSystem.Instance.LaidFor(entity, record) : null;
            // An area that clears the game's markings (mk.clear, and an area marking over them: VanillaClear.OfArea): a change of it (not every read of its chunk) is told to ClearMarkingsSystem.
            bool clear = (item.TypeHash == ClearHash && ClearHash != 0u) || AreaFitRules.IsAreaHash(item.TypeHash);
            if (clear)
            {
                if (!m_Clears.Contains(entity) || !m_Store.TryGetValue(entity, out ItemRecord was) || !was.Position.Equals(record.Position) || was.Yaw != record.Yaw || was.Spec != record.Spec)
                    ClearChanges++;
                m_Clears.Add(entity);
            }
            else if (m_Clears.Remove(entity))
            {
                ClearChanges++;
            }

            m_Store.Upsert(entity, record);
        }

        private void ReadChanged()
        {
            using (NativeArray<Entity> entities = m_Changed.ToEntityArray(Allocator.Temp))
            using (NativeArray<TsItem> items = m_Changed.ToComponentDataArray<TsItem>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                    Put(entities[i], items[i]);
            }
        }

        private void StartPass()
        {
            m_Store.BeginPass();
            m_PassActive = true;
            m_PassCursor = 0;
            m_PassLength = -1;
            m_PassFrames = 0;
            m_PassMs = 0.0;
        }

        private void StepPass(Stopwatch clock)
        {
            var slice = Stopwatch.StartNew();
            m_PassFrames++;
            using (NativeArray<Entity> entities = m_Items.ToEntityArray(Allocator.Temp))
            {
                // Items were added or deleted since the last slice: the order has moved, so the read starts again (the pass keeps what it has read; the items that are gone are found at the end).
                if (m_PassLength >= 0 && entities.Length != m_PassLength)
                {
                    m_Store.BeginPass();
                    m_PassCursor = 0;
                }

                m_PassLength = entities.Length;
                int n = 0;
                while (m_PassCursor < entities.Length)
                {
                    Entity entity = entities[m_PassCursor++];
                    if (EntityManager.HasComponent<TsItem>(entity))
                        Put(entity, EntityManager.GetComponentData<TsItem>(entity));
                    if ((++n & 31) == 0 && clock.Elapsed.TotalMilliseconds > LoadBudget.Scaled(kSyncMs))
                        break;
                }

                m_PassMs += slice.Elapsed.TotalMilliseconds;
                if (m_PassCursor < entities.Length)
                    return;
            }

            m_RemovedScratch.Clear();
            int removed = m_Store.EndPass(m_RemovedScratch);
            foreach (Entity gone in m_RemovedScratch)
            {
                TsBatchSystem.Gone(gone);
                if (m_Clears.Remove(gone))
                    ClearChanges++;
                m_Traffic.Remove(gone);
                ForgetAuto(gone);
            }
            m_RemovedScratch.Clear();
            m_PassActive = false;
            Synced = true;
            if (m_PassAgain)
            {
                m_PassAgain = false;
                m_Resync = true;
            }

            int actual = m_Items.CalculateEntityCount();
            if (!m_LoggedFirstRead)
            {
                m_LoggedFirstRead = true;
                Mod.log.Info($"Chunks: the first read of the items: {m_Store.Count} item(s) in {m_Store.TileCount} tile(s), {m_PassMs:0.0} ms over {m_PassFrames} frame(s) ({removed} gone).");
            }

            if (actual != m_Store.Count && !m_WarnedMismatch)
            {
                m_WarnedMismatch = true;
                Mod.log.Error($"Chunks: a full read ended with {m_Store.Count} item(s) but the query counts {actual}; the item query must not have a filter.");
            }
        }

        private readonly List<Entity> m_FitReady = new List<Entity>();

        private void RefreshAuto()
        {
            if (m_Auto.Count == 0)
                return;
            foreach (Entity entity in new List<Entity>(m_Auto))
            {
                if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                {
                    ForgetAuto(entity);
                    continue;
                }

                // From the item itself (its own settings), not from the record, which holds the lines of the last message.
                Put(entity, EntityManager.GetComponentData<TsItem>(entity));
            }
        }

        private void RefreshTraffic()
        {
            if (m_Traffic.Count == 0)
                return;
            foreach (Entity entity in new List<Entity>(m_Traffic))
            {
                if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                {
                    m_Traffic.Remove(entity);
                    continue;
                }

                TsItem item = EntityManager.GetComponentData<TsItem>(entity);
                // A board can have been replaced since it was registered. Read its saved settings again: the record already contains runtime fields, and it may now be another kind of board.
                Put(entity, item);
            }
        }

        private void ApplyHidden()
        {
            // Only the items whose state changed are touched: those that were hidden and are not, and those that are now.
            var changed = new List<Entity>();
            foreach (Entity entity in m_AppliedHidden)
            {
                if (!s_Hidden.Contains(entity))
                    changed.Add(entity);
            }

            foreach (Entity entity in s_Hidden)
            {
                if (!m_AppliedHidden.Contains(entity))
                    changed.Add(entity);
            }

            m_AppliedHidden.Clear();
            m_AppliedHidden.UnionWith(s_Hidden);
            foreach (Entity entity in changed)
            {
                if (!m_Store.TryGetValue(entity, out ItemRecord record))
                    continue;
                record.Hidden = s_Hidden.Contains(entity);
                m_Store.Upsert(entity, record);
            }
        }

        // ------------------------------------------------------------------ which tiles are built

        /// <summary>The camera has moved this far (m) since the last plan: a new plan is made.</summary>
        private const float kPlanMoveMetres = 8f;

        /// <summary>A plan is made at least this often (frames) even when nothing moved: a tile that is built grows stale, a tier that was not reachable is, and so on.</summary>
        private const int kPlanSafetyFrames = 120;

        private int m_PlannedAtFrame = -1000, m_PlannedStoreChanges = -1;
        private float m_PlanX, m_PlanZ, m_PlannedAtSeconds = -1000f;
        private Predicate<TileKey> m_NotBuilt;

        private void Plan()
        {
            Camera camera = m_Camera.activeCamera;
            if (camera == null)
                return;
            if (!Synced && m_PassActive && m_PassFrames < kFirstReadFrames)
                return;
            Vector3 at = camera.transform.position;

            // A plan costs a look at the tiles round the camera (a hundred or so): it is made when the camera has moved, when the items changed, when a built tile is stale, and now and then.
            bool moved = !m_HaveCamera || Mathf.Abs(at.x - m_PlanX) > kPlanMoveMetres || Mathf.Abs(at.z - m_PlanZ) > kPlanMoveMetres;
            bool changed = m_Store.Changes != m_PlannedStoreChanges || m_Replan;
            if (!moved && !changed && m_Stale.Count == 0 && m_Frame - m_PlannedAtFrame < kPlanSafetyFrames)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!moved && !Cadence.Due(m_Frame - m_PlannedAtFrame, now - m_PlannedAtSeconds, kPlanFrames, kPlanSeconds))
                return;

            m_PlannedAtFrame = m_Frame;
            m_PlannedAtSeconds = now;
            m_PlannedStoreChanges = m_Store.Changes;
            m_Replan = false;
            m_PlanX = m_CamX = at.x;
            m_PlanZ = m_CamZ = at.z;
            m_HaveCamera = true;
            m_ToBuild.Clear();
            m_ToDrop.Clear();
            TileStreamer.PlanAround(m_HasItems, m_Views.Keys, m_TierOf, at.x, at.z, m_Options, m_ToBuild, m_ToDrop, m_IsStale, m_Store.LongReachTiles);
            foreach (TileKey key in m_ToDrop)
            {
                if (m_Views.ContainsKey(key) && !m_DropQueue.Contains(key))
                    m_DropQueue.Enqueue(key);
                m_Stale.Remove(key);
            }

            // A stale tile that is not built is not stale: it is built from the items as they are when it is wanted.
            if (m_Stale.Count > 0)
                m_Stale.RemoveWhere(m_NotBuilt);
        }

        /// <summary>Whether a tile is on its way to being drawn: being built on a worker, built and waiting for its objects, or having them made.</summary>
        private bool Busy(TileKey key)
        {
            if (m_Jobs.ContainsKey(key) || (m_Applying != null && m_Applying.Result.Key.Equals(key)))
                return true;
            foreach (TileResult ready in m_Ready)
            {
                if (ready.Key.Equals(key))
                    return true;
            }

            return false;
        }

        /// <summary>Whether the road follow is writing the tile's items still (HoldBuild), for no longer than kMaxHoldFrames since it was first asked.</summary>
        private bool Held(TileKey key)
        {
            if (HoldBuild == null || !HoldBuild(key))
            {
                if (m_HeldSince.Count > 0)
                    m_HeldSince.Remove(key);
                return false;
            }

            if (!m_HeldSince.TryGetValue(key, out int since))
                m_HeldSince[key] = since = m_Frame;
            return m_Frame - since < kMaxHoldFrames;
        }

        /// <summary>Whether a tile's first build waits for the posts' grounds near the camera (kHoldSeconds).</summary>
        private bool HoldFirstBuilds()
        {
            if (m_LoadedAt < 0f || m_LoadReported || !Synced)
                return false;
            if (m_ReadAt < 0f)
                m_ReadAt = UnityEngine.Time.realtimeSinceStartup;
            AreaFitSystem fits = AreaFitSystem.Instance;
            return fits != null && fits.NearWaiting > 0 && UnityEngine.Time.realtimeSinceStartup - m_ReadAt < kHoldSeconds;
        }

        private void StartJobs()
        {
            bool hold = HoldFirstBuilds();
            int i = 0, kept = 0;
            int maxJobs = LoadBudget.Loading ? kMaxJobs * 2 : kMaxJobs;
            while (m_Jobs.Count < maxJobs && i < m_ToBuild.Count)
            {
                TileWish wish = m_ToBuild[i++];
                if (Busy(wish.Key))
                    continue;
                if (Held(wish.Key))
                {
                    m_ToBuild[kept++] = wish;
                    continue;
                }

                if (hold && !m_Views.ContainsKey(wish.Key))
                {
                    // Kept at the front, in its order, for when the grounds are there.
                    m_ToBuild[kept++] = wish;
                    continue;
                }

                int nowVersion = m_Store.VersionOf(wish.Key);
                if (m_Failures.TryGetValue(wish.Key, out KeyValuePair<int, int> failed) && failed.Key >= kMaxFailures && failed.Value == nowVersion)
                {
                    // A tile that could not be built three times as it is now is left as it is (the log says why): it is not asked for again until its items change, and it is not stale for ever.
                    m_Stale.Remove(wish.Key);
                    continue;
                }

                if (m_EmptyAt.TryGetValue(wish.Key, out int emptyVersion) && emptyVersion == nowVersion)
                {
                    m_Stale.Remove(wish.Key);
                    continue;
                }

                var items = new List<ItemRecord>();
                int version = m_Store.Snapshot(wish.Key, items, wish.Tier == TileTier.Horizon);
                if (items.Count == 0)
                {
                    m_Stale.Remove(wish.Key);
                    continue;
                }

                m_Jobs[wish.Key] = new Job { Task = TileWorker.Start(wish.Key, wish.Tier, version, m_Epoch, items), Tier = wish.Tier, Version = version };
                m_Stale.Remove(wish.Key);
            }

            if (i > kept)
                m_ToBuild.RemoveRange(kept, i - kept);
        }

        private List<TileKey> m_Finished;

        private void CollectJobs()
        {
            if (m_Jobs.Count == 0)
                return;
            m_Finished = m_Finished ?? new List<TileKey>();
            m_Finished.Clear();
            foreach (KeyValuePair<TileKey, Job> pair in m_Jobs)
            {
                if (pair.Value.Task.IsCompleted)
                    m_Finished.Add(pair.Key);
            }

            foreach (TileKey key in m_Finished)
            {
                Job job = m_Jobs[key];
                m_Jobs.Remove(key);
                TileResult result;
                try
                {
                    result = job.Task.Result;
                }
                catch (Exception ex)
                {
                    result = new TileResult { Key = key, Tier = job.Tier, Version = job.Version, Epoch = m_Epoch, Error = ex.ToString() };
                }

                if (result.Epoch != m_Epoch)
                    continue;
                m_WorkerMsTotal += result.Ms;
                if (result.Error != null)
                {
                    int count = m_Failures.TryGetValue(key, out KeyValuePair<int, int> failed) && failed.Value == result.Version ? failed.Key + 1 : 1;
                    m_Failures[key] = new KeyValuePair<int, int>(count, result.Version);
                    if (m_LoggedErrors++ < 10)
                        Mod.log.Error($"Chunks: building tile {key} ({result.Tier}) failed ({count} of {kMaxFailures}): {result.Error}");
                    continue;
                }

                m_Failures.Remove(key);
                if (result.Version != m_Store.VersionOf(key))
                {
                    // The tile changed while it was being built: this is out of date. A tile that is built is stale again; one that is not is built from what it is now when it is wanted.
                    m_Superseded++;
                    if (m_Views.ContainsKey(key))
                        m_Stale.Add(key);
                    continue;
                }

                AskForMaterials(result);
                m_Ready.Enqueue(result);
            }
        }

        // ------------------------------------------------------------------ making the objects

        /// <summary>Makes the Unity objects of the tiles that were built on the workers, a part at a time within the budget; a tile is shown (and the one it replaces is destroyed) when all its parts are there.</summary>
        private void ApplyStep(double budgetMs)
        {
            var clock = Stopwatch.StartNew();
            bool any = false;
            int partsDone = 0, madeNow = 0;
            while (true)
            {
                if (m_Applying == null)
                {
                    if (m_Ready.Count == 0)
                        return;
                    TileResult result = m_Ready.Dequeue();
                    if (result.Epoch != m_Epoch || !Wanted(result))
                        continue;
                    m_Applying = new Applying { Result = result, Replaces = m_Views.ContainsKey(result.Key) };
                }

                EnsureRoot();
                Applying a = m_Applying;
                Vec3 origin = Tiles.Centre(a.Result.Key);
                while (a.Next < a.Result.Parts.Count)
                {
                    PreparedPart part = a.Result.Parts[a.Next];
                    if (!a.Known.Contains(part.Material))
                    {
                        MaterialNeed need = TsMaterials.Need(part.Material);
                        if (need == MaterialNeed.Have)
                        {
                            a.Known.Add(part.Material);
                        }
                        else
                        {
                            float now = UnityEngine.Time.realtimeSinceStartup;
                            if (need == MaterialNeed.Wait && a.WaitSince < 0f)
                                a.WaitSince = now;
                            GateAction gate = MaterialGate.Decide(need, clock.Elapsed.TotalMilliseconds, partsDone, madeNow, a.Replaces ? budgetMs * kReplaceFactor : budgetMs, a.WaitSince < 0f ? 0.0 : now - a.WaitSince);
                            if (gate != GateAction.Go)
                                return;
                        }
                    }

                    var step = Stopwatch.StartNew();
                    int madeBefore = TsMaterials.MadeCount;
                    bool isLed = part.Material.StartsWith(ChunkBuilder.LedPrefix, StringComparison.Ordinal);
                    bool isPaint = ChunkBuilder.IsMarkingMaterial(part.Material);
                    bool isGlass = part.Material.StartsWith(ChunkBuilder.SolidPrefix, StringComparison.Ordinal) && SolidGlass.Is(ChunkBuilder.SolidColourOf(part.Material));
                    Material material = isLed ? TsMaterials.LedMaterial(int.TryParse(part.Material.Substring(ChunkBuilder.LedPrefix.Length), out int colour) ? colour : 0)
                        : TsMaterials.OfBody(part.Material) ?? TsMaterials.Face(part.Material);
                    if (material != null)
                    {
                        // The lit dots are small and glow: they do not cast shadows. The objects stay hidden until the whole tile is there.
                        // The lit dots are small and glow, and what is far away is far beyond the shadow distance: neither casts shadows (every caster is drawn again for each cascade of the sun). Nor
                        // does a noise barrier's glass (0.12.49): clear sheets over a road would throw a solid shadow on it. The far and the horizon tiles cast none: beyond the shadow distance (0.12.73).
                        GameObject go = CreateRenderer($"CDLTS tile {a.Result.Key} {part.Material} #{a.Index++}", part.Mesh, material, origin, !isLed && !isPaint && !isGlass && a.Result.Tier >= TileTier.Mid);
                        a.Objects.Add(go);
                        if (FaceDetail.IsFace(part.Material))
                            a.Faces.Add(part.Material);
                        a.Vertices += part.Mesh.VertexCount;
                        a.MeshBytes += Footprint.MeshBytes(part.Mesh.VertexCount, part.Mesh.Indices.Length);
                    }

                    a.Next++;
                    a.WaitSince = -1f;
                    partsDone++;
                    if (TsMaterials.MadeCount != madeBefore)
                        madeNow++;
                    double took = step.Elapsed.TotalMilliseconds;
                    a.Ms += took;
                    if (took > m_ApplyWorstMs)
                    {
                        m_ApplyWorstMs = took;
                        m_ApplyWorstPart = part.Material;
                        m_ApplyWorstVertices = part.Mesh.VertexCount;
                        m_ApplyWorstMade = TsMaterials.MadeCount != madeBefore;
                    }

                    any = true;
                    if (clock.Elapsed.TotalMilliseconds >= (a.Replaces ? budgetMs * kReplaceFactor : budgetMs))
                        return;
                }

                bool replaced = a.Replaces;
                if (replaced)
                    m_Rebuilt++;
                FinishTile(a);
                m_Applying = null;
                if (any && clock.Elapsed.TotalMilliseconds >= (replaced ? budgetMs * kReplaceFactor : budgetMs))
                    return;
            }
        }

        /// <summary>Whether a tile that was built is still wanted at the tier it was built for (the camera may have gone far while it was being made).</summary>
        private bool Wanted(TileResult result)
        {
            if (!m_HaveCamera)
                return true;
            TileTier have = TierOf(result.Key);
            TileTier want = TileStreamer.Wanted(TileStreamer.DistanceToTile(result.Key, m_CamX, m_CamZ), have, m_Options, m_Store.HasLongReach(result.Key));
            return want != TileTier.None;
        }

        private void FinishTile(Applying a)
        {
            TileKey key = a.Result.Key;
            if (m_Views.TryGetValue(key, out TileView old))
            {
                DestroyObjects(old);
                m_Views.Remove(key);
            }

            if (a.Objects.Count == 0 || !m_Store.HasItems(key))
            {
                if (a.Objects.Count == 0 && m_Store.HasItems(key))
                    m_EmptyAt[key] = a.Result.Version;
                DestroyObjects(a.Objects);

                return;
            }

            var view = new TileView { Origin = Tiles.Centre(key), Tier = a.Result.Tier, Version = a.Result.Version, Vertices = a.Vertices, MeshBytes = a.MeshBytes };
            view.Objects.AddRange(a.Objects);
            view.Faces.UnionWith(a.Faces);
            view.Visible = ShouldShow(view, false);
            foreach (GameObject go in view.Objects)
                go.SetActive(view.Visible);
            m_Views[key] = view;

            // The items changed while the objects were being made: the tile is built again.
            if (m_Store.VersionOf(key) != a.Result.Version)
                m_Stale.Add(key);

            m_Built++;
            m_Applied++;
            m_BuildMsTotal += a.Ms;
            RendererCount = 0;
            VertexCount = 0;
            foreach (TileView v in m_Views.Values)
            {
                RendererCount += v.Objects.Count;
                VertexCount += v.Vertices;
            }

            if (m_LoggedBuilds++ < 16)
                Mod.log.Info($"Chunks: tile {key} built ({a.Result.Tier}) from {a.Result.Items} item(s): {view.Objects.Count} renderer(s), {view.Vertices} vertices, {a.Result.Skipped} skipped; {a.Result.Ms:0.0} ms on the worker, {a.Ms:0.0} ms of objects on the main thread.");
        }

        private void DropStep(double budgetMs)
        {
            var clock = Stopwatch.StartNew();
            while (m_DropQueue.Count > 0)
            {
                TileKey key = m_DropQueue.Dequeue();
                if (m_Views.TryGetValue(key, out TileView view))
                {
                    DestroyObjects(view);
                    m_Views.Remove(key);
                    m_Dropped++;
                    if (m_LoggedDrops++ < 8)
                        Mod.log.Info($"Chunks: tile {key} dropped (out of reach).");
                }

                if (clock.Elapsed.TotalMilliseconds >= budgetMs)
                    break;
            }
        }

        private GameObject CreateRenderer(string name, PreparedMesh data, Material material, Vec3 origin, bool castShadows = true)
        {
            Mesh mesh = null;
            GameObject go = null;
            try
            {
                mesh = MeshUpload.ToMesh(data, name);
                go = new GameObject(name);
                go.SetActive(false);
                go.hideFlags = HideFlags.DontSave;
                go.layer = 0;
                go.transform.SetParent(m_Root.transform, false);
                go.transform.position = new Vector3(origin.X, origin.Y, origin.Z);
                MeshUpload.AddRenderer(go, mesh, material, castShadows);
                return go;
            }
            catch (Exception error)
            {
                MeshUpload.CleanupFailed(mesh, go, error);
                throw;
            }
        }

        private void EnsureRoot()
        {
            if (m_Root != null)
                return;
            GameObject root = null;
            try
            {
                root = new GameObject("CDLTS");
                root.hideFlags = HideFlags.DontSave;
                m_Root = root;
            }
            catch (Exception error)
            {
                MeshUpload.CleanupFailed(null, root, error);
                throw;
            }
        }

        private static void DestroyObjects(TileView view)
        {
            DestroyObjects(view.Objects);
        }

        private static void DestroyObjects(List<GameObject> objects)
        {
            MeshUpload.DestroyAll(objects);
            objects.Clear();
        }

        // ------------------------------------------------------------------ upkeep

        /// <summary>
        /// Every guide board with other settings is a picture of its own. The pictures of boards that are not on a tile that is built any more (edited to something else, deleted, too far) are dropped
        /// here, every ten seconds or so: what is kept is every face of a tile that is built or being built, and the face of the preview.
        /// </summary>
        private void PruneFaces()
        {
            var keep = new HashSet<string>();
            foreach (TileView view in m_Views.Values)
                keep.UnionWith(view.Faces);
            if (m_Applying != null)
                keep.UnionWith(m_Applying.Faces);
            foreach (TileResult ready in m_Ready)
            {
                foreach (PreparedPart part in ready.Parts)
                {
                    if (FaceDetail.IsFace(part.Material))
                        keep.Add(part.Material);
                }
            }

            if (PlacementTool.PreviewFaceKeys != null)
                keep.UnionWith(PlacementTool.PreviewFaceKeys);
            TsMaterials.Prune(keep);
        }

        /// <summary>
        /// Keeps the pictures inside their memory budget: while they take more than it, the near and middle tiers reach less far (and the faces of the tiles that fall to a lower tier are dropped by the
        /// next prune); when they take well under it the tiers come back. Once a second.
        /// </summary>
        private void AdaptBudget()
        {
            float next = TextureBudget.Next(m_Scale, TsMaterials.FaceTextureBytes, TextureBudget.DefaultBytes);
            if (Math.Abs(next - m_Scale) < 1e-6f)
                return;
            if (m_LoggedScales++ < 12)
                Mod.log.Info($"Chunks: the pictures take {TsMaterials.FaceTextureBytes / 1048576} MB of a budget of {TextureBudget.DefaultBytes / 1048576} MB: the near and middle distances are {(next < m_Scale ? "cut to" : "raised to")} {next * 100f:0} % ({m_Options.NearMetres * next / m_Scale:0} m and {m_Options.MidMetres * next / m_Scale:0} m).");
            m_Scale = next;
            m_Options = TextureBudget.Scaled(StreamOptions.WithFar(m_FarMetres), m_Scale);
        }

        /// <summary>
        /// The draw distance of the signs and markings (0.12.103: the panel's display setting; StreamOptions.WithFar brings it into range): the tiles are planned again at once, so that the tiles that
        /// come into reach are built and those that fall out of it are given back.
        /// </summary>
        internal void SetViewDistance(float metres)
        {
            float far = StreamOptions.ClampFar(metres);
            if (Math.Abs(far - m_FarMetres) < 1e-3f)
                return;
            m_FarMetres = far;
            m_Options = TextureBudget.Scaled(StreamOptions.WithFar(far), m_Scale);
            m_Replan = true;
            Mod.log.Info($"Chunks: the draw distance of the signs and markings is {far:0} m (the near and middle tiers {m_Options.NearMetres:0} m and {m_Options.MidMetres:0} m).");
        }

        internal float ViewDistance => m_FarMetres;

        /// <summary>
        /// What the panel's figures are made from (0.12.104, Footprint.Build): the tiles that have items and the tiles that are built, each with its distance from the camera, its items, its tier and what its
        /// meshes take; the renderers; the items and what their records take (an estimate by their number and their text). Nothing before the camera is known: the plan has not run.
        /// </summary>
        internal void CollectFootprint(FootprintInput into)
        {
            into.Tiles.Clear();
            into.Renderers = 0;
            into.Items = m_Store.Count;
            long itemBytes = 0;
            foreach (KeyValuePair<Entity, ItemRecord> pair in m_Store)
                itemBytes += Footprint.ItemBytes(pair.Value.Spec?.Length ?? 0);
            into.ItemBytes = itemBytes;
            if (!m_HaveCamera)
                return;
            foreach (TileKey key in m_Store.TileKeys)
            {
                m_Views.TryGetValue(key, out TileView view);
                into.Tiles.Add(new TileCost { Distance = TileStreamer.DistanceToTile(key, m_CamX, m_CamZ), Items = m_Store.ItemCount(key), Tier = view?.Tier ?? TileTier.None, MeshBytes = view?.MeshBytes ?? 0L });
            }

            foreach (KeyValuePair<TileKey, TileView> pair in m_Views)
            {
                into.Renderers += pair.Value.Objects.Count;
                // A built tile whose items are gone is held until the plan drops it.
                if (m_Store.ItemCount(pair.Key) == 0)
                    into.Tiles.Add(new TileCost { Distance = TileStreamer.DistanceToTile(pair.Key, m_CamX, m_CamZ), Items = 0, Tier = pair.Value.Tier, MeshBytes = pair.Value.MeshBytes });
            }
        }

        private bool ShouldShow(TileView view, bool wasVisible)
        {
            if (!m_HaveCamera)
                return true;
            float dx = view.Origin.X - m_CamX, dz = view.Origin.Z - m_CamZ;
            float distance = Mathf.Sqrt(dx * dx + dz * dz) - Tiles.Size * 0.7f;
            // A horizon tile is shown as far as it is built (its own limit); the others up to the far limit.
            float reach = view.Tier == TileTier.Horizon ? m_Options.HorizonMetres : m_Options.FarMetres;
            return TileStreamer.InReach(distance, reach, wasVisible, kHysteresis);
        }

        private void UpdateVisibility()
        {
            Camera camera = m_Camera.activeCamera;
            if (camera == null)
                return;

            Vector3 at = camera.transform.position;
            m_CamX = at.x;
            m_CamZ = at.z;
            m_HaveCamera = true;
            foreach (TileView view in m_Views.Values)
            {
                bool show = ShouldShow(view, view.Visible);
                if (show == view.Visible)
                    continue;

                view.Visible = show;
                foreach (GameObject go in view.Objects)
                {
                    if (go != null)
                        go.SetActive(show);
                }
            }
        }

        private void UpdateNight()
        {
            // The brightness of the sun, not the lighting state (which flips back and forth for a moment at dawn); in steps of 1/32 so that
            // the materials are written only when it matters.
            float b = Mathf.Clamp01(m_Lighting.dayLightBrightness / 0.3f);
            float day = b * b * (3f - 2f * b);
            float night = Mathf.Round((1f - day) * 32f) / 32f;
            TsMaterials.SetNight(night);
        }

        private void DestroyAll()
        {
            m_Epoch++;
            foreach (TileView view in m_Views.Values)
                DestroyObjects(view);
            m_Views.Clear();
            if (m_Applying != null)
            {
                DestroyObjects(m_Applying.Objects);
                m_Applying = null;
            }

            m_Store.Clear();
            m_Stale.Clear();
            m_DirtyScratch.Clear();
            m_Jobs.Clear();
            m_Failures.Clear();
            m_EmptyAt.Clear();
            m_Ready.Clear();
            m_DropQueue.Clear();
            m_ToBuild.Clear();
            m_ToDrop.Clear();
            m_Traffic.Clear();
            m_Auto.Clear();
            if (m_Clears.Count > 0)
                ClearChanges++;
            m_Clears.Clear();
            VmsAuto.Clear();
            m_AppliedHidden.Clear();
            m_PassActive = false;
            m_PassLength = -1;
            m_LoggedFirstRead = false;
            RendererCount = 0;
            VertexCount = 0;
            m_Resync = true;
            m_HaveCamera = false;
            Revision++;
            Synced = false;
            if (m_Root != null)
                UnityEngine.Object.Destroy(m_Root);
            m_Root = null;
            TsMaterials.Clear();
            ChunkBuilder.ClearCaches();
            // The paint's plans and the font the words are made from are let go with the city (the font is read again when the next word is drawn).
            CDLC.TS.Geo.MarkingTemplates.ClearCaches();
            CDLC.TS.Geo.MarkingLetters.Release();
            // The text boxes' plans, glyphs and fonts go with the city too.
            CDLC.TS.Geo.TextPlans.Clear();
            CDLC.TS.Geo.TextFonts.Release();
            MeshUpload.RetryCleanup();
        }
    }
}
