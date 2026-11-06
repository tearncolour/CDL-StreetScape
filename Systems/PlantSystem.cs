using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;
using ObjTransform = Game.Objects.Transform;

namespace CDLC.TS
{
    /// <summary>A plant of the game the mod put in a planter (0.12.32): the item and which of its plants. Not saved: the entity has LivePath, and the plants are made again from the items after a load.</summary>
    public struct TsPlant : IComponentData
    {
        public Entity Item;
        public int Index;

        /// <summary>Where it is and what it is (the plant is made again when this changes).</summary>
        public uint Hash;
    }

    /// <summary>
    /// The prefabs of the planters' plants (0.12.32; the owner: "可摆放绿化植物用游戏物件") and of the road greening's (0.12.49: its hedge pieces and street trees, GreenPlants): copies of the game's plants
    /// that the game does not hide where they overlap a road (OverrideSystem hides every object whose geometry is Overridable where it meets a road segment, footways and medians too, and a planter
    /// stands on those), not in any menu and never locked. Added once, in the PrefabUpdate phase before PrefabInitializeSystem (a prefab added anywhere else is never initialized, see
    /// ToolbarTabSystem); PlantSystem clears the flag when they are initialized. A name the game does not have is left out and logged.
    /// </summary>
    internal sealed partial class PlantPrefabSystem : GameSystemBase
    {
        /// <summary>The game's plants the mod may put: the planters' (the flowering ones first) and the greening's.</summary>
        internal static readonly string[] kPlants = GreenPlants.All;

        internal const string kSuffix = " (CDLTS planter)";

        internal static readonly List<PrefabBase> Copies = new List<PrefabBase>();

        private static readonly string[] kTypes = { "PlantObject", "TreeObject", "StaticObjectPrefab", "ObjectGeometryPrefab" };

        private PrefabSystem m_Prefabs;
        private bool m_Done;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (m_Done || GameManager.instance == null || GameManager.instance.gameMode != GameMode.Game)
                return;
            m_Done = true;
            var made = new List<string>();
            foreach (string name in kPlants)
            {
                PrefabBase original = Find(name);
                if (original == null)
                    continue;
                PrefabBase copy = original.Clone(name + kSuffix);
                copy.Remove<ObsoleteIdentifiers>();
                copy.Remove<UIObject>();
                copy.Remove<SpawnableObject>();
                foreach (ComponentBase component in copy.components.ToArray())
                {
                    if (component is UnlockableBase)
                        copy.Remove(component.GetType());
                }

                if (m_Prefabs.AddPrefab(copy))
                {
                    Copies.Add(copy);
                    made.Add(name);
                }
            }

            var missing = new List<string>();
            foreach (string name in kPlants)
            {
                if (!made.Contains(name))
                    missing.Add(name);
            }

            Mod.log.Info($"Planter plants: copies of {made.Count} of the game's {kPlants.Length} plants added ({string.Join(", ", made)})" + (missing.Count > 0 ? $"; not found or not added: {string.Join(", ", missing)}." : "."));
        }

        private PrefabBase Find(string name)
        {
            foreach (string type in kTypes)
            {
                if (m_Prefabs.TryGetPrefab(new PrefabID(type, name), out PrefabBase prefab) && prefab != null)
                    return prefab;
            }

            return null;
        }
    }

    /// <summary>
    /// The game's plants in the planters (0.12.32) and the road greening (0.12.49): for every item of MarkingTemplates.PlantedIds (with the planted noise barrier of 0.12.49) a plant at each of its plan's props (MarkingPlan.BakedProps), laid with
    /// the item (MarkingGround), one of PlantPrefabSystem's copies of the kind it asks for (GreenPlants) that fits its room, chosen by the place so that it is the same after every load; a hedge piece
    /// keeps the line's heading (turned end for end at most), a tree is adult, or a teen where a guard holds it; a kind none of whose prefabs the game has gets nothing (logged once). The plants are the game's objects, created as its own tools
    /// leave a placed object (the prefab's archetype, Transform, PrefabRef, the seed), with TsPlant and LivePath (not saved); they are made again when the item changes and given Deleted when it goes.
    /// Main thread, Modification1 (where the game's object systems see the new entities). 0.12.72 (the owner's profile: 2.36 ms in the first minutes): every step is cut at a deadline. A pass over the items
    /// (what is wanted, what stands: PlantLedger) starts when the items changed (ChunkSystem.Revision) or every kSafetyFrames, and goes on a slice a frame; the plants are made kMakeMs a frame (it was
    /// 400 a frame, 10 ms or more); the scan of the city's objects for plants of a save is cut the same way. The system's time is in the log's summary and its slow lines (SlowLog "PlantSystem").
    /// </summary>
    internal sealed partial class PlantSystem : GameSystemBase
    {
        /// <summary>No pass starts less than this many frames after the one before it, and one starts anyway after kSafetyFrames (a plant the game took away is found by it).</summary>
        private const int kMinPassFrames = 60;
        private const int kSafetyFrames = 1800;

        /// <summary>What a frame may spend (ms): on a pass over the items and the scan of a save's objects; on making plants. Each does at least one unit when there is one.</summary>
        private const double kPassMs = 0.3, kMakeMs = 0.4;

        private sealed class Kind
        {
            /// <summary>The game's prefab it is a copy of.</summary>
            public string Name;
            public Entity Prefab;
            public EntityArchetype Archetype;
            public bool Seeded, Tree;

            /// <summary>About how wide the plant is (m): its collision box is half its mesh (ObjectInitializeSystem).</summary>
            public float Width;
        }

        private enum Phase { Idle, Offering, Sweeping }

        private readonly List<Kind> m_Kinds = new List<Kind>();

        /// <summary>The copies of each kind of PropAnchor, the narrowest first (made when first asked).</summary>
        private readonly Dictionary<string, List<Kind>> m_Families = new Dictionary<string, List<Kind>>();

        private readonly HashSet<string> m_Lacking = new HashSet<string>();
        private readonly HashSet<uint> m_Planted = new HashSet<uint>();
        private readonly PlantLedger<(Entity, int), Entity> m_Ledger = new PlantLedger<(Entity, int), Entity>();
        private readonly List<Entity> m_Gone = new List<Entity>();
        private readonly List<KeyValuePair<Entity, ItemRecord>> m_Records = new List<KeyValuePair<Entity, ItemRecord>>();

        /// <summary>
        /// The plans of the planters' records that were not made yet, made on workers (0.12.75): the pass made them on the main thread, 3 to 30 ms for a long planter line in the owner's 0.12.73 log, and a
        /// check between records cannot cut one short. A record whose plan is being made holds the pass where it is; nothing is offered for it until the plan is there.
        /// </summary>
        private readonly WarmSet<(string, string)> m_Warm = new WarmSet<(string, string)>();
        private double m_OfferWorstMs;
        private int m_OfferWaits;
        private Func<Entity, bool> m_Alive;
        private PrefabSystem m_Prefabs;
        private EntityQuery m_Objects;
        private Phase m_Phase;
        private int m_Frame, m_Cursor, m_SeenRevision = -1, m_LastPass = -kSafetyFrames, m_NotReadyFrames;
        private bool m_Ready, m_Logged, m_CheckLoaded, m_Scanning;

        // The scan of the city's objects for plants a save brought (RemoveLoaded): the arrays are kept between frames, and the entities found are taken away at the end.
        private NativeArray<Entity> m_ScanEntities;
        private NativeArray<PrefabRef> m_ScanPrefabs;
        private int m_ScanCursor;
        private readonly HashSet<Entity> m_ScanCopies = new HashSet<Entity>();
        private readonly List<Entity> m_Loaded = new List<Entity>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Alive = plant => EntityManager.Exists(plant);
            m_Objects = GetEntityQuery(ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.Object>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
            foreach (string id in MarkingTemplates.PlantedIds)
                m_Planted.Add(SignCatalog.HashOf(id));
        }

        [Preserve]
        protected override void OnDestroy()
        {
            EndScan();
            base.OnDestroy();
        }

        /// <summary>The state of the last city was let go at the end of the deserialization (LoadWarmSystem.Early): the end of the loading does not do it again (it would forget the plants made since).</summary>
        private bool m_ResetEarly;

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_ResetEarly = LoadWarmSystem.Early(serializationContext);
            if (m_ResetEarly)
                ResetForLoad(Purpose.LoadGame, GameMode.Game);
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (m_ResetEarly)
            {
                m_ResetEarly = false;
                return;
            }

            ResetForLoad(purpose, mode);
        }

        private void ResetForLoad(Purpose purpose, GameMode mode)
        {
            // The city's objects were all cleared (ClearSystem): the plants are made again from the items.
            EndScan();
            m_Ledger.Clear();
            m_Records.Clear();
            m_Gone.Clear();
            m_Warm.Clear();
            m_Phase = Phase.Idle;
            m_SeenRevision = -1;
            m_LastPass = -kSafetyFrames;
            m_Frame = 0;
            m_NotReadyFrames = 0;
            m_CheckLoaded = mode == GameMode.Game && purpose == Purpose.LoadGame;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance.gameMode != GameMode.Game || ChunkSystem.Instance == null || !ChunkSystem.Instance.Synced)
                return;
            long start = Stopwatch.GetTimestamp();
            if (!m_Ready && !Prepare())
            {
                m_NotReadyFrames++;
                return;
            }

            m_Frame++;
            m_OfferWorstMs = 0.0;
            int made = 0;
            long passDeadline = start + MsToTicks(LoadBudget.Scaled(kPassMs));
            if (m_CheckLoaded)
            {
                // Nothing is made before the objects of a save have been looked through: a plant made now would be taken for one the save brought.
                ScanLoaded(passDeadline);
            }
            else
            {
                Pass(passDeadline);
                made = Make(Stopwatch.GetTimestamp() + MsToTicks(LoadBudget.Scaled(kMakeMs)));
            }

            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            SlowLog.Add("PlantSystem", ms, SlowLog.IsSlow(ms) ? $"{made} made, {m_Ledger.PendingCount} waiting, {m_Ledger.Count} standing, {m_Phase}{(m_CheckLoaded ? ", looking through the objects of the save" : "")}, the worst record offered took {m_OfferWorstMs:0.0} ms, {m_OfferWaits} wait(s) for a plan so far, {m_Warm.Started} plan(s) made on workers, {m_Warm.Fallbacks} on this thread" : null);
        }

        /// <summary>The objects of the save not looked through yet, for the load's hold on the loading screen (the plants themselves are made after it: they are a few a frame, and one is not worth a held screen).</summary>
        internal int FirstUseWaiting => PlantWait.Waiting(m_CheckLoaded, m_Ready, PlantPrefabSystem.Copies.Count, m_NotReadyFrames) ? 1 : 0;

        private static long MsToTicks(double ms) => (long)(ms * Stopwatch.Frequency / 1000.0);

        /// <summary>The copies once the game has initialized them: their flag that lets the game hide them on a road cleared, their archetype for a lone object. False until they are.</summary>
        private bool Prepare()
        {
            if (PlantPrefabSystem.Copies.Count == 0)
                return false;
            foreach (PrefabBase copy in PlantPrefabSystem.Copies)
            {
                if (!m_Prefabs.TryGetEntity(copy, out Entity prefab) || EntityManager.HasComponent<Created>(prefab) || !EntityManager.HasComponent<ObjectData>(prefab)
                    || !EntityManager.HasComponent<ObjectGeometryData>(prefab))
                    return false;
            }

            foreach (PrefabBase copy in PlantPrefabSystem.Copies)
            {
                m_Prefabs.TryGetEntity(copy, out Entity prefab);
                ObjectGeometryData geometry = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
                geometry.m_Flags &= ~(GeometryFlags.Overridable | GeometryFlags.Brushable);
                EntityManager.SetComponentData(prefab, geometry);
                EntityArchetype source = EntityManager.GetComponentData<ObjectData>(prefab).m_Archetype;
                if (!source.Valid)
                    continue;
                var types = new List<ComponentType>();
                bool seeded = false, tree = false;
                using (NativeArray<ComponentType> sourceTypes = source.GetComponentTypes(Allocator.Temp))
                {
                    foreach (ComponentType type in sourceTypes)
                    {
                        if (type == ComponentType.ReadWrite<Owner>() || type == ComponentType.ReadWrite<Game.Tools.Temp>() || type == ComponentType.ReadWrite<Attached>())
                            continue;
                        seeded |= type == ComponentType.ReadWrite<Game.Common.PseudoRandomSeed>();
                        tree |= type == ComponentType.ReadWrite<Tree>();
                        types.Add(type);
                    }
                }

                ComponentType[] own = { ComponentType.ReadWrite<ObjTransform>(), ComponentType.ReadWrite<PrefabRef>(), ComponentType.ReadWrite<TsPlant>(), ComponentType.ReadWrite<LivePath>(),
                    ComponentType.ReadWrite<Created>(), ComponentType.ReadWrite<Updated>() };
                foreach (ComponentType type in own)
                {
                    if (!types.Contains(type))
                        types.Add(type);
                }

                m_Kinds.Add(new Kind
                {
                    Name = copy.name.EndsWith(PlantPrefabSystem.kSuffix, StringComparison.Ordinal) ? copy.name.Substring(0, copy.name.Length - PlantPrefabSystem.kSuffix.Length) : copy.name,
                    Prefab = prefab,
                    Archetype = EntityManager.CreateArchetype(types.ToArray()),
                    Seeded = seeded,
                    Tree = tree,
                    Width = 2f * Math.Max(geometry.m_Size.x, geometry.m_Size.z)
                });
            }

            m_Kinds.Sort((a, b) => a.Width.CompareTo(b.Width));
            m_Ready = true;
            var names = new List<string>();
            foreach (Kind kind in m_Kinds)
                names.Add($"{kind.Name} {kind.Width:0.00} m");
            Mod.log.Info("Planter plants: " + (m_Kinds.Count > 0 ? string.Join(", ", names) + " (the game does not hide them on a road)." : "none of the game's plants could be used."));
            return true;
        }

        /// <summary>
        /// The plants a save brought (0.12.51; the owner: "加检测"): LivePath keeps them out of a save, and none of the copies is placed but by this system, so after a load, before any is made,
        /// an object of a copy came from a save written by a game whose serializer no longer leaves LivePath out. It is not this system's (TsPlant is not saved), and the items' plants are made
        /// again: it is taken away, and the store notes it for the panel. 0.12.72: the objects of the city are gone through a slice of a frame at a time (a city has a hundred thousand of them and the
        /// loop was one piece of several ms), and the entities may have gone between two slices.
        /// </summary>
        private void ScanLoaded(long deadline)
        {
            if (!m_Scanning)
            {
                m_ScanCopies.Clear();
                foreach (Kind kind in m_Kinds)
                    m_ScanCopies.Add(kind.Prefab);
                m_ScanEntities = m_Objects.ToEntityArray(Allocator.Persistent);
                m_ScanPrefabs = m_Objects.ToComponentDataArray<PrefabRef>(Allocator.Persistent);
                m_ScanCursor = 0;
                m_Scanning = true;
            }

            int n = 0;
            while (m_ScanCursor < m_ScanEntities.Length)
            {
                if (m_ScanCopies.Contains(m_ScanPrefabs[m_ScanCursor].m_Prefab))
                    m_Loaded.Add(m_ScanEntities[m_ScanCursor]);
                m_ScanCursor++;
                if ((++n & 255) == 0 && Stopwatch.GetTimestamp() >= deadline)
                    return;
            }

            m_Loaded.RemoveAll(entity => !EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity));
            if (m_Loaded.Count > 0)
            {
                using (var array = new NativeArray<Entity>(m_Loaded.ToArray(), Allocator.Temp))
                    EntityManager.AddComponent<Deleted>(array);
                SaveDataSystem.Instance?.Warn("plants", m_Loaded.Count);
                Mod.log.Warn($"Planter plants: the save held {m_Loaded.Count} plant(s) of this mod's greening (the game's serializer no longer leaves out LivePath?): taken away; the items' plants are made again.");
            }

            EndScan();
            m_CheckLoaded = false;
        }

        private void EndScan()
        {
            if (m_ScanEntities.IsCreated)
                m_ScanEntities.Dispose();
            if (m_ScanPrefabs.IsCreated)
                m_ScanPrefabs.Dispose();
            m_Scanning = false;
            m_Loaded.Clear();
        }

        /// <summary>
        /// The pass over the items, a slice a frame: when the items changed or it has been long, the planters' records are listed (Offering), each is asked what plants it wants and the ledger takes away what
        /// stands wrongly and queues what is missing; then what stands and was asked for by no record is taken away (Sweeping).
        /// </summary>
        private void Pass(long deadline)
        {
            if (m_Kinds.Count == 0)
                return;
            ChunkSystem chunks = ChunkSystem.Instance;
            if (m_Phase == Phase.Idle)
            {
                bool changed = chunks.Revision != m_SeenRevision && m_Frame - m_LastPass >= kMinPassFrames;
                if (!changed && m_Frame - m_LastPass < kSafetyFrames)
                    return;
                m_SeenRevision = chunks.Revision;
                m_LastPass = m_Frame;
                m_Records.Clear();
                foreach (KeyValuePair<Entity, ItemRecord> pair in chunks.Records)
                {
                    if (m_Planted.Contains(pair.Value.TypeHash) && !pair.Value.Hidden)
                        m_Records.Add(pair);
                }

                m_Cursor = 0;
                m_Ledger.BeginPass();
                m_Phase = Phase.Offering;
            }

            if (m_Phase == Phase.Offering)
            {
                while (m_Cursor < m_Records.Count)
                {
                    long offered = Stopwatch.GetTimestamp();
                    if (!Offer(m_Records[m_Cursor].Key, m_Records[m_Cursor].Value))
                    {
                        // Its plan is being made on a worker: the pass stays at this record, and the plans of the next few are asked for meanwhile.
                        m_OfferWaits++;
                        WarmAhead();
                        Release();
                        return;
                    }

                    m_OfferWorstMs = Math.Max(m_OfferWorstMs, (Stopwatch.GetTimestamp() - offered) * 1000.0 / Stopwatch.Frequency);
                    m_Cursor++;
                    if (Stopwatch.GetTimestamp() >= deadline)
                    {
                        Release();
                        return;
                    }
                }

                m_Records.Clear();
                m_Phase = Phase.Sweeping;
            }

            if (m_Phase == Phase.Sweeping && m_Ledger.Sweep(deadline, m_Gone))
                m_Phase = Phase.Idle;
            Release();
        }

        /// <summary>The plants the ledger gave up (changed, taken by the game, or asked for by no item now) are given Deleted.</summary>
        private void Release()
        {
            foreach (Entity plant in m_Gone)
            {
                if (EntityManager.Exists(plant) && !EntityManager.HasComponent<Deleted>(plant))
                    EntityManager.AddComponent<Deleted>(plant);
            }

            m_Gone.Clear();
        }

        /// <summary>What every prop of one planter item wants: a plant there, as the hash of its place, heading, index and kind says (it is made again when any changes: another species is another tree).</summary>
        /// <summary>The plan of a record's marking: false while a worker is making it (nothing is made here then); the caller asks again.</summary>
        private bool PlanReady(SignDef def, string spec, out MarkingPlan plan)
        {
            string text = spec ?? "";
            bool kept = MarkingTemplates.TryKept(def, text, out plan);
            switch (m_Warm.Ask((def?.Id ?? "", text), kept, () => MarkingTemplates.PlanOf(def, text)))
            {
                case Warmth.Ready:
                    return true;
                case Warmth.Fallback:
                    plan = MarkingTemplates.PlanOf(def, text);
                    return true;
                default:
                    plan = null;
                    return false;
            }
        }

        /// <summary>Asks for the plans of the records just after the cursor, so that the workers make them while the pass waits for the first.</summary>
        private void WarmAhead()
        {
            int end = Math.Min(m_Records.Count, m_Cursor + 5);
            for (int i = m_Cursor + 1; i < end; i++)
            {
                ItemRecord next = m_Records[i].Value;
                SignDef def = SignCatalog.ByHash(next.TypeHash);
                string text = (next.Laid != null ? next.Laid.Spec : next.Spec) ?? "";
                if (!MarkingTemplates.TryKept(def, text, out _))
                    m_Warm.Ask((def?.Id ?? "", text), false, () => MarkingTemplates.PlanOf(def, text));
            }
        }

        /// <summary>Offers what a record wants planted; false when its plan is not there yet (nothing was offered: the pass asks again).</summary>
        private bool Offer(Entity item, in ItemRecord record)
        {
            if (!PlanReady(SignCatalog.ByHash(record.TypeHash), record.Laid != null ? record.Laid.Spec : record.Spec, out MarkingPlan plan))
                return false;
            if (plan == null)
                return true;
            Vec3 origin = record.Position + new Vec3(0f, record.Laid != null ? record.Laid.Dy : 0f, 0f);
            float s = (float)Math.Sin(record.Yaw), c = (float)Math.Cos(record.Yaw);
            for (int i = 0; i < plan.BakedProps.Count; i++)
            {
                PropAnchor anchor = plan.BakedProps[i];
                Vec3 p = anchor.Local;
                var at = new Vec3(p.X * c + p.Z * s + origin.X, p.Y + origin.Y, -p.X * s + p.Z * c + origin.Z);
                float yaw = record.Yaw + anchor.Yaw;
                m_Ledger.Offer((item, i), new PlantWish { At = at, Yaw = yaw, Hash = PlantHash.Of(at, yaw, (uint)i, anchor.Kind), Anchor = anchor }, m_Alive, m_Gone);
            }

            return true;
        }

        /// <summary>Makes the plants that are wanted and not there, until the deadline (one at least): how many.</summary>
        private int Make(long deadline)
        {
            int made = 0;
            while (m_Ledger.TryTake(out (Entity, int) key, out PlantWish wish))
            {
                string wantedKind = wish.Anchor.Kind;
                Kind kind = Choose(wantedKind, wish.Anchor.Room, wish.Hash);
                if (kind == null)
                    continue;
                Entity plant = EntityManager.CreateEntity(kind.Archetype);
                float turn = GreenPlants.KeepsHeading(wantedKind) ? (wish.Hash & 1u) * (float)Math.PI : (wish.Hash % 628u) / 100f;
                var transform = new ObjTransform(new float3(wish.At.X, wish.At.Y, wish.At.Z), quaternion.RotateY(wish.Yaw + turn));
                EntityManager.SetComponentData(plant, new PrefabRef(kind.Prefab));
                EntityManager.SetComponentData(plant, transform);
                EntityManager.SetComponentData(plant, new TsPlant { Item = key.Item1, Index = key.Item2, Hash = wish.Hash });
                if (kind.Seeded)
                    EntityManager.SetComponentData(plant, new Game.Common.PseudoRandomSeed((ushort)(wish.Hash & 0xFFFF)));
                if (kind.Tree)
                    EntityManager.SetComponentData(plant, new Tree { m_State = GreenPlants.IsYoung(wantedKind) ? TreeState.Teen : TreeState.Adult, m_Growth = 128 });
                m_Ledger.Placed(key, plant, wish.Hash);
                made++;
                if (Stopwatch.GetTimestamp() >= deadline)
                    break;
            }

            if (m_Ledger.PendingCount == 0 && !m_Logged && m_Ledger.Made > 0)
            {
                m_Logged = true;
                Mod.log.Info($"Planter plants: {m_Ledger.Count} plant(s) in the planters ({m_Ledger.Made} made, {m_Ledger.Gone} taken away so far).");
            }

            return made;
        }

        /// <summary>
        /// The plant of a kind for a place with room metres across: among the copies of the kind (GreenPlants.PrefabsOf), the narrowest that are no more than three times as wide (the game's are wide; they
        /// spill over), by the place's hash; null when the game has none of them.
        /// </summary>
        private Kind Choose(string kind, float room, uint hash)
        {
            kind = kind ?? "";
            if (!m_Families.TryGetValue(kind, out List<Kind> family))
            {
                family = new List<Kind>();
                foreach (string name in GreenPlants.PrefabsOf(kind))
                    family.AddRange(m_Kinds.FindAll(k => k.Name == name));
                family.Sort((a, b) => a.Width.CompareTo(b.Width));
                m_Families[kind] = family;
            }

            if (family.Count == 0)
            {
                if (m_Lacking.Add(kind))
                    Mod.log.Warn($"Planter plants: the game has none of the plants of the kind \"{kind}\" ({string.Join(", ", GreenPlants.PrefabsOf(kind))}); nothing is put there.");
                return null;
            }

            int fit = 0;
            while (fit < family.Count && family[fit].Width <= Math.Max(room * 3f, family[0].Width))
                fit++;
            return family[(int)(hash % (uint)Math.Max(1, fit))];
        }
    }
}
