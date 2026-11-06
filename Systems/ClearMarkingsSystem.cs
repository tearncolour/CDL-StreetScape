using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// Hides the game's own road markings inside the areas the player placed (mk.clear, VanillaClear). A marking of the game is a lane with a mesh whose prefab is a secondary lane (lane lines, edge lines,
    /// stop lines, parking lines), owned by a road segment or a junction; such a lane that carries Game.Common.Overridden is not drawn: PreCullingSystem gives it the debug mask only, and the game's
    /// Net.OverrideSystem only ever adds or takes it away on fences (read from the game's code, 1.3). The tags are taken off for the writing of a save (TakeTagsForSave, 0.12.38) and the areas,
    /// restored from the store after a load, hide the markings again; a lane the game makes again (a road that is changed) is a new entity, and is looked at when it appears.
    ///
    /// Runs in Modification4B, after the barrier that makes the game's new lanes: a marking that appears is hidden before it is drawn once, and the game's search tree and culling, which run later in the
    /// frame, see the change (Updated is added with it). Work is done only when something changed: the areas (all of them after loading, else the boxes where they differ), or new and changed markings
    /// while there is an area. Nothing is read or changed when no area was ever placed. Decoration only: the markings' lanes carry no traffic.
    /// </summary>
    internal sealed partial class ClearMarkingsSystem : GameSystemBase
    {
        private const int kMaxErrors = 10;

        /// <summary>The frames after loading before the first look: the game fills its search tree of lanes in the first update.</summary>
        private const int kSettleFrames = 4;

        internal static ClearMarkingsSystem Instance { get; private set; }

        private EntityQuery m_Changed;
        private EntityQuery m_Hidden;
        private EntityQuery m_LanePrefabs;
        private Game.Net.SearchSystem m_Search;
        private PrefabSystem m_PrefabSystem;

        private Dictionary<Entity, ClearRegion> m_Regions = new Dictionary<Entity, ClearRegion>();
        private readonly List<ClearRegion> m_RegionList = new List<ClearRegion>();
        private int m_SeenChanges = -1;

        /// <summary>TsItem's order version when the areas were last looked at (an item created or destroyed changes it).</summary>
        private int m_SeenOrder = -1;
        private bool m_Loaded;
        private int m_SettleFrames;
        private bool m_RestoreWanted;
        private int m_Errors;

        /// <summary>What each lane prefab is to the clearing: not a marking, a marking along the lanes, or a line across them (null until it is asked for).</summary>
        private readonly Dictionary<Entity, sbyte> m_PrefabKind = new Dictionary<Entity, sbyte>();
        private HashSet<Entity> m_CrossingPrefabs;
        private const sbyte kNotMarking = -1, kAlong = 0, kAcross = 1;

        // For the log: the names of the marking prefabs met, with how many of each and what they were taken for (the names say what is a line across the traffic; read in the game).
        private readonly Dictionary<Entity, int[]> m_PrefabSeen = new Dictionary<Entity, int[]>();
        private bool m_LoggedPrefabs;
        private readonly List<Entity> m_Candidates = new List<Entity>();
        private readonly HashSet<Entity> m_CandidateSet = new HashSet<Entity>();
        private readonly List<Entity> m_ToHide = new List<Entity>(), m_ToShow = new List<Entity>();

        /// <summary>The areas there are now, in the world (for the tool's overlay). Main thread.</summary>
        internal IReadOnlyList<ClearRegion> Regions => m_RegionList;

        /// <summary>Invalidates the tool's cached 'already cleared' result after restoration, movement or a change of areas.</summary>
        internal int Revision { get; private set; }

        /// <summary>How many of the game's markings carry the tag now (counted when it changes and after loading), and what the last look did: for the panel.</summary>
        internal int HiddenNow { get; private set; }

        internal string LastNote { get; private set; } = "";

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_Changed = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<LaneGeometry>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<Game.Net.UtilityLane>(), ComponentType.ReadOnly<Game.Net.TrackLane>() }
            });
            m_Hidden = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Overridden>(), ComponentType.ReadOnly<Lane>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<LaneGeometry>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<Game.Net.UtilityLane>(), ComponentType.ReadOnly<Game.Net.TrackLane>() }
            });
            m_LanePrefabs = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<SecondaryNetLane>());
            m_Search = World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            CreateDecals();
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        /// <summary>How many lanes carry the tag in the world now (this mod's and another mod's).</summary>
        internal int TaggedNow() => m_Hidden.CalculateEntityCount();

        internal string PrefabNameOf(Entity prefab) => NameOf(prefab);

        /// <summary>The first look at the areas has not been made (or the areas changed since): for the load's hold on the loading screen.</summary>
        internal int FirstUseWaiting(ChunkSystem chunks) => (m_Loaded ? 1 : 0) + (chunks != null && chunks.ClearChanges != m_SeenChanges ? 1 : 0);

        /// <summary>The state of the last city was let go at the end of the deserialization (LoadWarmSystem.Early): the end of the loading does not do it again (it would forget what was hidden since).</summary>
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
            m_Regions = new Dictionary<Entity, ClearRegion>();
            m_RegionList.Clear();
            Revision++;
            m_SeenChanges = -1;
            m_SeenOrder = -1;
            m_Loaded = true;
            m_KeptByOthers = 0;
            m_SettleFrames = 0;
            // A save the game never said was done (0.12.51, SaveDataSystem.Unreported) does not leave the next city's clearing paused.
            m_SavePaused = false;
            m_RestoreWanted = false;
            m_LateTotal = m_LateLogs = 0;
            m_AreasHide = true;
            m_Census.Reset(UnityEngine.Time.realtimeSinceStartupAsDouble);
            m_PrefabKind.Clear();
            m_CrossingPrefabs = null;
            m_PrefabSeen.Clear();
            m_LoggedPrefabs = false;
            HiddenNow = 0;
            LastNote = "";
            ClearTraceLog.Reset();
        }

        /// <summary>The panel's button: every area is deleted and every marking of the game that carries the tag is drawn again (what to do before the mod is taken away). Done in the next update.</summary>
        /// <summary>Whether the area markings hide the game's markings under them (VanillaClear.OfArea); off after the restore button until the next load.</summary>
        private bool m_AreasHide = true;

        internal void RestoreAll()
        {
            m_RestoreWanted = true;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance.gameMode != GameMode.Game)
                return;
            ChunkSystem chunks = ChunkSystem.Instance;
            if (chunks == null || !chunks.Synced)
                return;
            if (m_Loaded && ++m_SettleFrames < kSettleFrames)
                return;

            var clock = Stopwatch.StartNew();
            string what = null;
            try
            {
                if (m_RestoreWanted)
                {
                    m_RestoreWanted = false;
                    what = Restore(chunks);
                }

                // An area deleted (with the tool or the restore button) is gone from the world at once; the renderer's store notices it a little later. Each area is looked at when an item came or went
                // (the order version of TsItem: 0.12.3; the automatic marking places hundreds of areas, and looking at them all at every frame was most of this system's time).
                bool gone = false;
                int order = EntityManager.GetComponentOrderVersion<TsItem>();
                if (order != m_SeenOrder)
                {
                    m_SeenOrder = order;
                    foreach (Entity entity in m_Regions.Keys)
                    {
                        if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                        {
                            gone = true;
                            break;
                        }
                    }
                }

                if (m_Loaded || gone || chunks.ClearChanges != m_SeenChanges)
                {
                    bool full = m_Loaded;
                    m_Loaded = false;
                    m_SeenChanges = chunks.ClearChanges;
                    what = Reconcile(chunks, full);
                }

                if (m_RegionList.Count > 0 && !m_Changed.IsEmptyIgnoreFilter)
                    what = (what == null ? "" : what + "; ") + LookAtNew();
                if (m_RegionList.Count > 0 && !m_SavePaused && !m_DecalsChanged.IsEmptyIgnoreFilter)
                {
                    string decals = LookAtNewDecals();
                    if (decals.Length > 0)
                        what = (what == null ? "" : what + "; ") + decals;
                }

                string census = StepCensus(chunks);
                if (census != null)
                    what = (what == null ? "" : what + "; ") + census;
            }
            catch (Exception ex)
            {
                if (m_Errors++ < kMaxErrors)
                    Mod.log.Error("Clearing the game's markings failed: " + ex);
            }

            double ms = clock.Elapsed.TotalMilliseconds;
            if (what != null)
                LastNote = what;
            SlowLog.Add("ClearMarkingsSystem", ms, SlowLog.IsSlow(ms) ? what ?? "" : null);
        }

        // ------------------------------------------------------------------ a save (0.12.38, DESIGN-save-data.md 4.1)

        /// <summary>From the game's "saving" to its "saved": no picture of the game is taken away, so that those it makes again are in the save.</summary>
        private bool m_SavePaused;

        /// <summary>How many road pieces the save's start asked to make their lane arrows again: what TakeTagsForSave compares the arrows it finds with.</summary>
        private int m_AskedRoads;

        /// <summary>The game is about to write a save (and waits a frame first): the roads under the areas are updated, so that the game makes their lane arrows again for the save.</summary>
        internal void PauseForSave()
        {
            m_SavePaused = true;
            int roads = m_AskedRoads = RestoreDecals(m_RegionList, false);
            if (roads > 0)
                Mod.log.Info($"Clear: a save: {roads} road piece(s) under the areas asked to make their lane arrows again for it.");
        }

        /// <summary>The save is written: the pictures under the areas are taken away again.</summary>
        internal void ResumeAfterSave()
        {
            m_SavePaused = false;
            m_AskedRoads = 0;
            var boxes = new List<ClearBox>();
            foreach (ClearRegion region in m_RegionList)
                boxes.Add(region.Box);
            if (boxes.Count > 0)
                RemoveDecalsIn(boxes);
        }

        /// <summary>
        /// The tags the areas put on the game's markings, taken off for the writing of a save (SaveDataSystem.BeforeWrite) without Updated, so that nothing is drawn differently before they are given
        /// back (GiveTagsBack, right after the writing). The tags another mod put (TsExtension.OthersHideMarking) are its own and stay.
        /// </summary>
        internal List<Entity> TakeTagsForSave()
        {
            var taken = new List<Entity>();
            if (m_RegionList.Count == 0)
                return taken;
            int tagged = 0, others = 0;
            using (NativeArray<Entity> hidden = m_Hidden.ToEntityArray(Allocator.Temp))
            {
                tagged = hidden.Length;
                foreach (Entity lane in hidden)
                {
                    if (WantsHidden(lane))
                        taken.Add(lane);
                    else if (TsExtension.HidesMarking(lane))
                        others++;
                }
            }

            if (taken.Count > 0)
            {
                using (var array = new NativeArray<Entity>(taken.ToArray(), Allocator.Temp))
                    EntityManager.RemoveComponent<Overridden>(array);
            }

            int arrows = DecalsUnderAreas();
            Mod.log.Info($"Clear: a save: {taken.Count} of the game's markings written as shown, hidden again after the writing; {arrows} lane arrow(s) under the areas in it ({m_AskedRoads} road piece(s) asked for them); the tag was on {tagged} lane(s) in all: {taken.Count} taken by an area, {others} by another mod, {tagged - taken.Count - others} by no area now.");
            if (m_AskedRoads > 0 && arrows == 0)
                Mod.log.Warn("Clear: a save: none of the lane arrows came back before the writing: the save holds none under the areas, and without this mod the game makes them again only when the road is updated.");
            return taken;
        }

        internal void GiveTagsBack(List<Entity> lanes)
        {
            var back = new List<Entity>(lanes.Count);
            foreach (Entity lane in lanes)
            {
                if (EntityManager.Exists(lane) && !EntityManager.HasComponent<Overridden>(lane))
                    back.Add(lane);
            }

            if (back.Count > 0)
            {
                using (var array = new NativeArray<Entity>(back.ToArray(), Allocator.Temp))
                    EntityManager.AddComponent<Overridden>(array);
            }
        }

        // ------------------------------------------------------------------ the areas

        private Dictionary<Entity, ClearRegion> ReadRegions(ChunkSystem chunks)
        {
            var regions = new Dictionary<Entity, ClearRegion>();
            foreach (Entity entity in chunks.ClearItems)
            {
                // An item deleted this frame (the restore button) is still in the store until its next read.
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<TsItem>(entity) && chunks.Records.TryGetValue(entity, out ItemRecord record))
                {
                    ClearRegion region = ClearRegion.Of(record);
                    if (region == null && m_AreasHide)
                        region = ClearRegion.OfArea(record);
                    else if (region != null && string.IsNullOrEmpty(SignCatalog.ByHash(record.TypeHash)?.Template?.Parse(record.Spec).Get(ClearElevation.Key)))
                    {
                        TsItem legacy = EntityManager.GetComponentData<TsItem>(entity);
                        if (RoadReader.IsLiveEdge(EntityManager, legacy.Edge))
                        {
                            EdgeInfo road = EdgeInfo.Of(EntityManager, legacy.Edge);
                            if (road.CurveOk)
                                region = region.AlongLegacyRoad(road.Curve, record.Position);
                        }
                    }
                    if (region != null)
                        regions[entity] = region;
                }
            }

            return regions;
        }

        /// <summary>
        /// Brings the tags up to date with the areas: after loading, every marking in every area and every marking that carries the tag (one that is in no area any more is drawn again); after a change, the
        /// markings in the boxes where the areas differ.
        /// </summary>
        private string Reconcile(ChunkSystem chunks, bool full)
        {
            var clock = Stopwatch.StartNew();
            Dictionary<Entity, ClearRegion> now = ReadRegions(chunks);
            Dictionary<Entity, ClearRegion> before = m_Regions;
            List<ClearBox> boxes = full ? new List<ClearBox>() : VanillaClear.Changes(m_Regions, now);
            if (full)
            {
                foreach (ClearRegion region in now.Values)
                    boxes.Add(region.Box);
            }

            m_Regions = now;
            m_RegionList.Clear();
            m_RegionList.AddRange(now.Values);
            Revision++;
            m_Candidates.Clear();
            m_CandidateSet.Clear();
            if (boxes.Count > 0)
                CollectInBoxes(boxes);
            if (full && !m_Hidden.IsEmptyIgnoreFilter)
            {
                using (NativeArray<Entity> hidden = m_Hidden.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity e in hidden)
                    {
                        if (m_CandidateSet.Add(e))
                            m_Candidates.Add(e);
                    }
                }
            }

            if (m_Candidates.Count == 0 && !full)
                return $"{m_RegionList.Count} area(s), nothing to look at";
            int looked = Decide(m_Candidates, out int markings);
            Apply(full ? "the first pass after the load" : "a pass over changed areas");
            if (full)
                HiddenNow = m_Hidden.CalculateEntityCount();
            string decals = ReconcileDecals(before, now, boxes, full);
            string note = $"{(full ? "after loading" : "the areas changed")}: {m_RegionList.Count} area(s), {boxes.Count} box(es) looked at, {looked} lane(s) of which {markings} marking(s), {m_ToHide.Count} hidden, {m_ToShow.Count} drawn again{(decals.Length > 0 ? "; " + decals : "")}, {clock.Elapsed.TotalMilliseconds:0.0} ms";
            if (full || m_ToHide.Count + m_ToShow.Count > 0 || decals.Length > 0)
                Mod.log.Info("Clear: " + note + ".");
            if (!m_LoggedPrefabs && m_PrefabSeen.Count > 0)
                LogPrefabs();
            m_ToHide.Clear();
            m_ToShow.Clear();
            return note;
        }

        /// <summary>The markings the game made or changed this frame, while there is an area: hidden when an area takes them, drawn again when it does not (a road moved out of its area).</summary>
        private string LookAtNew()
        {
            m_Candidates.Clear();
            using (NativeArray<Entity> changed = m_Changed.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in changed)
                    m_Candidates.Add(e);
            }

            Decide(m_Candidates, out int markings);
            int hidden = m_ToHide.Count, shown = m_ToShow.Count;
            Apply("a look at new lanes");
            m_ToHide.Clear();
            m_ToShow.Clear();
            if (!m_LoggedPrefabs && m_PrefabSeen.Count > 0)
                LogPrefabs();
            return $"{m_Candidates.Count} new or changed lane(s), {markings} marking(s): {hidden} hidden, {shown} drawn again";
        }

        /// <summary>The panel's button: the areas are deleted (their items) and every tagged marking is drawn again.</summary>
        private string Restore(ChunkSystem chunks)
        {
            // The game's pictures on the road: the roads under the areas draw theirs again.
            int roads = RestoreDecals(m_RegionList, true);
            var items = new List<Entity>();
            foreach (Entity item in chunks.ClearItems)
            {
                if (chunks.Records.TryGetValue(item, out ItemRecord record) && ChunkSystem.IsClearArea(record))
                    items.Add(item);
            }

            chunks.Destroy(items);

            // Restoring is what is done before the mod is removed: the area markings stop hiding the game's markings under them too, until the city is loaded again.
            m_AreasHide = false;

            m_ToShow.Clear();
            if (!m_Hidden.IsEmptyIgnoreFilter)
            {
                using (NativeArray<Entity> hidden = m_Hidden.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity e in hidden)
                    {
                        if (Kind(e, out _, out _) != kNotMarking && !KeptByOthers(e))
                            m_ToShow.Add(e);
                    }
                }
            }

            int shown = m_ToShow.Count;
            Apply("the restore");
            m_ToShow.Clear();
            m_Regions = new Dictionary<Entity, ClearRegion>();
            m_RegionList.Clear();
            Revision++;
            HiddenNow = 0;
            string note = $"restored: {items.Count} area(s) deleted, {shown} marking(s) of the game drawn again, {roads} road piece(s) asked to draw their pictures (arrows) again";
            Mod.log.Info("Clear: " + note + ".");
            return note;
        }

        // ------------------------------------------------------------------ the lanes

        private struct BoxIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Box;
            public List<Entity> Found;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Box);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Box))
                    Found.Add(item);
            }
        }

        /// <summary>The lanes whose boxes meet any of the boxes (the game's search tree of lanes), each once.</summary>
        private void CollectInBoxes(List<ClearBox> boxes)
        {
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_Search.GetLaneSearchTree(true, out JobHandle dependencies);
            dependencies.Complete();
            var found = new List<Entity>();
            foreach (ClearBox box in boxes)
            {
                var iterator = new BoxIterator { Box = new Bounds2(new float2(box.MinX - 1f, box.MinZ - 1f), new float2(box.MaxX + 1f, box.MaxZ + 1f)), Found = found };
                tree.Iterate(ref iterator);
            }

            foreach (Entity e in found)
            {
                if (m_CandidateSet.Add(e))
                    m_Candidates.Add(e);
            }
        }

        /// <summary>For each lane: whether it is a marking and whether an area takes it; those whose tag is to change go into m_ToHide and m_ToShow. Returns how many lanes were looked at.</summary>
        private int Decide(List<Entity> lanes, out int markings)
        {
            markings = 0;
            foreach (Entity e in lanes)
            {
                sbyte kind = Kind(e, out MarkingOwner owner, out float3 middle);
                if (kind == kNotMarking)
                    continue;
                markings++;
                var marking = new VanillaMarking { X = middle.x, Y = middle.y, Z = middle.z, Owner = owner, Crossing = kind == kAcross };
                bool want = VanillaClear.Covered(m_RegionList, marking);
                bool has = EntityManager.HasComponent<Overridden>(e);
                if (want && !has)
                    m_ToHide.Add(e);
                else if (!want && has && !KeptByOthers(e))
                    m_ToShow.Add(e);
            }

            return lanes.Count;
        }

        private int m_KeptByOthers;

        /// <summary>
        /// Whether another mod wants a marking hidden that no area of this mod takes (CDL : Racing's circuit: the markings protocol v1, asked through the extension since 0.12.29, TsExtension): its
        /// tag stays, so that the two do not undo each other (0.12.6; Racing 0.2.8
        /// hid its circuit's markings and this system took the tag off them again in the same frame). Counted for the log.
        /// </summary>
        private bool KeptByOthers(Entity lane)
        {
            if (!TsExtension.HidesMarking(lane))
                return false;
            if (m_KeptByOthers++ == 0)
                Mod.log.Info("Clear: a marking that another mod hides (CDL : Racing's circuit) keeps its tag; such markings are left to it from now on.");
            return true;
        }

        /// <summary>
        /// The markings protocol v1 (TsApi.WantsMarkingHidden): whether an area of this mod takes a marking lane of the game (what Decide asks), whatever its tag is now. False for a lane that is not a
        /// marking, or when no area is placed. Main thread.
        /// </summary>
        internal bool WantsHidden(Entity lane)
        {
            if (m_RegionList.Count == 0)
                return false;
            sbyte kind = Kind(lane, out MarkingOwner owner, out float3 middle);
            if (kind == kNotMarking)
                return false;
            return VanillaClear.Covered(m_RegionList, new VanillaMarking { X = middle.x, Y = middle.y, Z = middle.z, Owner = owner, Crossing = kind == kAcross });
        }

        /// <summary>What a lane is: not a marking (no mesh, a fence or a pipe, a track, a lane of traffic, one in the middle of being made), a marking along the lanes or one across them; its owner and middle.</summary>
        private sbyte Kind(Entity e, out MarkingOwner owner, out float3 middle)
        {
            owner = MarkingOwner.Other;
            middle = default;
            if (!EntityManager.Exists(e) || !EntityManager.HasComponent<LaneGeometry>(e) || EntityManager.HasComponent<Game.Net.UtilityLane>(e) || EntityManager.HasComponent<Game.Net.TrackLane>(e)
                || EntityManager.HasComponent<Game.Tools.Temp>(e) || EntityManager.HasComponent<Deleted>(e))
                return kNotMarking;
            if (!EntityManager.TryGetComponent(e, out PrefabRef prefabRef) || !EntityManager.TryGetComponent(e, out Curve curve) || !EntityManager.TryGetComponent(e, out Owner own))
                return kNotMarking;
            sbyte kind = PrefabKind(prefabRef.m_Prefab);
            if (kind == kNotMarking)
                return kNotMarking;
            Entity o = own.m_Owner;
            owner = o == Entity.Null || !EntityManager.Exists(o) ? MarkingOwner.Other : EntityManager.HasComponent<Edge>(o) ? MarkingOwner.Edge : EntityManager.HasComponent<Node>(o) ? MarkingOwner.Node : MarkingOwner.Other;
            middle = MathUtils.Position(curve.m_Bezier, 0.5f);
            if (!m_LoggedPrefabs)
            {
                if (!m_PrefabSeen.TryGetValue(prefabRef.m_Prefab, out int[] seen))
                    m_PrefabSeen[prefabRef.m_Prefab] = seen = new int[3];
                seen[(int)owner]++;
            }

            return kind;
        }

        /// <summary>
        /// What a lane prefab is: a marking when the game made it a secondary lane (NetLaneData's Secondary flag: the lane prefabs with a SecondaryLane component); a line across the lanes when the game puts it
        /// across them (it is a crossing lane of some lane prefab's SecondaryNetLane list) or its name says so (VanillaClear.IsCrossingName); kept for the session.
        /// </summary>
        private sbyte PrefabKind(Entity prefab)
        {
            if (m_PrefabKind.TryGetValue(prefab, out sbyte known))
                return known;
            sbyte kind = kNotMarking;
            if (EntityManager.TryGetComponent(prefab, out NetLaneData data) && (data.m_Flags & LaneFlags.Secondary) != 0)
            {
                if (m_CrossingPrefabs == null)
                    m_CrossingPrefabs = ReadCrossingPrefabs();
                kind = m_CrossingPrefabs.Contains(prefab) || VanillaClear.IsCrossingName(NameOf(prefab)) ? kAcross : kAlong;
            }

            m_PrefabKind[prefab] = kind;
            return kind;
        }

        /// <summary>Whether a lane prefab is one of the game's markings across the traffic (a stop or give-way line, PrefabKind): the automatic marking asks it of each approach (0.12.54).</summary>
        internal bool IsCrossMarking(Entity prefab) => PrefabKind(prefab) == kAcross;

        /// <summary>The secondary lane prefabs the game puts across the lanes of some lane prefab (SecondaryNetLane with the crossing flag): stop lines, give-way lines and the like.</summary>
        private HashSet<Entity> ReadCrossingPrefabs()
        {
            var set = new HashSet<Entity>();
            using (NativeArray<Entity> prefabs = m_LanePrefabs.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity prefab in prefabs)
                {
                    if (!EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<SecondaryNetLane> list))
                        continue;
                    for (int i = 0; i < list.Length; i++)
                    {
                        if ((list[i].m_Flags & SecondaryNetLaneFlags.Crossing) != 0)
                            set.Add(list[i].m_Lane);
                    }
                }
            }

            return set;
        }

        private string NameOf(Entity prefab)
        {
            try
            {
                return m_PrefabSystem.TryGetPrefab(prefab, out PrefabBase p) && p != null ? p.name : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private void LogPrefabs()
        {
            m_LoggedPrefabs = true;
            var lines = new List<string>();
            foreach (KeyValuePair<Entity, int[]> pair in m_PrefabSeen)
            {
                sbyte kind = m_PrefabKind.TryGetValue(pair.Key, out sbyte k) ? k : kNotMarking;
                bool crossingList = m_CrossingPrefabs != null && m_CrossingPrefabs.Contains(pair.Key);
                lines.Add($"  \"{NameOf(pair.Key)}\": {(kind == kAcross ? "across" : "along")} ({(crossingList ? "the game puts it across lanes" : "not a crossing lane of the game")}{(VanillaClear.IsCrossingName(NameOf(pair.Key)) ? ", the name says across" : "")}), {pair.Value[1]} of segments, {pair.Value[2]} of junctions, {pair.Value[0]} of other owners");
            }

            Mod.log.Info("Clear: the game's marking prefabs met (what each was taken for):\n" + string.Join("\n", lines));
        }

        /// <summary>Puts the tag on m_ToHide and takes it off m_ToShow, with Updated so that the game's culling and search tree see it this frame.</summary>
        private void Apply(string source)
        {
            if (m_ToHide.Count > 0)
            {
                using (var array = new NativeArray<Entity>(m_ToHide.ToArray(), Allocator.Temp))
                {
                    EntityManager.AddComponent<Overridden>(array);
                    EntityManager.AddComponent<Updated>(array);
                }

                ClearTraceLog.Hid(EntityManager, m_ToHide, source);
            }

            if (m_ToShow.Count > 0)
            {
                using (var array = new NativeArray<Entity>(m_ToShow.ToArray(), Allocator.Temp))
                {
                    EntityManager.RemoveComponent<Overridden>(array);
                    EntityManager.AddComponent<Updated>(array);
                }
            }

            if (m_ToHide.Count + m_ToShow.Count > 0)
                HiddenNow = m_Hidden.CalculateEntityCount();
        }

        private int m_LateTotal, m_LateLogs;

        /// <summary>
        /// The late pass (ClearLateSystem, the end of ModificationEnd, 0.12.86): a lane the game made or updated in this frame that an area takes and that carries no tag is tagged again. Between this
        /// mod's pass of Modification4B and the clean-up another mod's system may take the tag off an updated lane: Anarchy's RemoveOverridenSystem does it, in the first place of ModificationEnd, to every
        /// lane that has an owner, Updated and the tag, under the default and the bulldozer tools, and Move It's MIT_RemoveOverriddenSystem to every updated entity that has it while its tool runs (the owner's log of 4 Nov: 8 of 8, 38 of 38 lanes had lost the tag in the frame they were hidden in, and
        /// the game drew the lane). The lane keeps the Updated of this frame, so the game's culling reads the tag again. A lane that is shown on purpose is not covered by an area any more and is left. Returns how many.
        /// </summary>
        internal int ReHideLate()
        {
            if (m_RegionList.Count == 0 || m_Changed.IsEmptyIgnoreFilter)
                return 0;
            m_ToHide.Clear();
            using (NativeArray<Entity> changed = m_Changed.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity lane in changed)
                {
                    bool tagged = EntityManager.HasComponent<Overridden>(lane);
                    if (LateRehide.Needs(tagged, !tagged && WantsHidden(lane)))
                        m_ToHide.Add(lane);
                }
            }

            int count = m_ToHide.Count;
            if (count == 0)
                return 0;
            using (var array = new NativeArray<Entity>(m_ToHide.ToArray(), Allocator.Temp))
                EntityManager.AddComponent<Overridden>(array);
            ClearTraceLog.Hid(EntityManager, m_ToHide, "the late pass");
            m_ToHide.Clear();
            HiddenNow = m_Hidden.CalculateEntityCount();
            m_LateTotal += count;
            if (m_LateLogs++ < 6)
                Mod.log.Info($"Clear: late pass: {count} lane(s) that an area takes had lost the tag in this frame, after this mod's pass of Modification4B: tagged again ({m_LateTotal} in all since the load).");
            return count;
        }
    }
}
