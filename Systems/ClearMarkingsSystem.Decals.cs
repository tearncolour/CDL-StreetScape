using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// The game's own pictures on the road inside the areas (0.12.4; the owner: "清除地面标线同时应该清除地面的原装地面贴图，比如图里的指引箭头等等"): the lane arrows and the words and symbols of reserved lanes
    /// are not lanes but objects the game makes for a road segment (Game.Objects.SecondaryObjectSystem: a secondary object owned by the segment, its prefab a decal with LaneDirectionData or TrafficSignData).
    /// They cannot be hidden with Overridden as the lines are: the game's own Objects.OverrideSystem takes that away again from an object that can be overridden (GeometryFlags.Overridable) whenever the
    /// object is updated, and it would be updated every frame by both. So they are deleted (Deleted, as the game deletes the secondary objects it no longer wants); when an area goes or shrinks, the
    /// road segments and junctions where it was are updated, and the game makes their objects again (those another area still covers are deleted again when they appear). Read from the game's code (1.3).
    /// Only decals that carry a lane's meaning go: street lights, signs and signals on posts, and plain decals (covers, stains) stay.
    /// </summary>
    internal sealed partial class ClearMarkingsSystem
    {
        private EntityQuery m_DecalsChanged;
        private Game.Objects.SearchSystem m_ObjectSearch;

        /// <summary>For each object prefab met: whether it is a picture of the road's markings (1) or not (0).</summary>
        private readonly Dictionary<Entity, bool> m_DecalPrefab = new Dictionary<Entity, bool>();
        private readonly Dictionary<Entity, int> m_DecalSeen = new Dictionary<Entity, int>();
        private bool m_LoggedDecals;

        /// <summary>How many of the game's pictures on the road were taken away this session (for the log and the panel).</summary>
        internal int DecalsRemoved { get; private set; }

        private void CreateDecals()
        {
            m_DecalsChanged = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Objects.Secondary>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Game.Objects.Transform>() },
                Any = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_ObjectSearch = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
        }

        /// <summary>
        /// After the areas changed: the pictures inside the areas there are now go (all of them after loading, else those in the boxes that changed), and where an area went or changed the roads are updated
        /// so that the game draws their pictures again.
        /// </summary>
        private string ReconcileDecals(Dictionary<Entity, ClearRegion> before, Dictionary<Entity, ClearRegion> now, List<ClearBox> boxes, bool full)
        {
            var gone = new List<ClearBox>();
            if (!full && before != null)
            {
                foreach (KeyValuePair<Entity, ClearRegion> pair in before)
                {
                    if (!now.TryGetValue(pair.Key, out ClearRegion still) || !still.SameAs(pair.Value))
                        gone.Add(pair.Value.Box);
                }
            }

            int removed = boxes.Count > 0 && !m_SavePaused ? RemoveDecalsIn(boxes) : 0;
            int roads = gone.Count > 0 ? RedrawRoadsIn(gone, true) : 0;
            return removed + roads == 0 ? "" : $"{removed} picture(s) of the game on the road taken away, {roads} road piece(s) asked to draw theirs again";
        }

        /// <summary>The pictures the game made this frame, while there is an area: those inside one go.</summary>
        private string LookAtNewDecals()
        {
            var candidates = new List<Entity>();
            using (NativeArray<Entity> changed = m_DecalsChanged.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in changed)
                    candidates.Add(e);
            }

            int removed = RemoveCovered(candidates);
            return removed == 0 ? "" : $"{removed} new picture(s) of the game on the road taken away";
        }

        /// <summary>The roads under every area are updated, and the game draws their pictures again (before the areas are deleted: the restore button; a save: PauseForSave, which asks at once).</summary>
        private int RestoreDecals(IEnumerable<ClearRegion> regions, bool nextFrame)
        {
            var boxes = new List<ClearBox>();
            foreach (ClearRegion region in regions)
                boxes.Add(region.Box);
            return boxes.Count > 0 ? RedrawRoadsIn(boxes, nextFrame) : 0;
        }

        /// <summary>How many of the game's lane arrows and words lie under the areas now (what a save holds of them, 0.12.38: the log says whether the game made them again in time).</summary>
        private int DecalsUnderAreas()
        {
            var boxes = new List<ClearBox>();
            foreach (ClearRegion region in m_RegionList)
                boxes.Add(region.Box);
            return CoveredDecals(FoundIn(boxes)).Count;
        }

        private int RemoveDecalsIn(List<ClearBox> boxes) => RemoveCovered(FoundIn(boxes));

        private List<Entity> FoundIn(List<ClearBox> boxes)
        {
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_ObjectSearch.GetStaticSearchTree(true, out JobHandle dependencies);
            dependencies.Complete();
            var found = new List<Entity>();
            foreach (ClearBox box in boxes)
            {
                var iterator = new BoxIterator { Box = new Bounds2(new float2(box.MinX - 1f, box.MinZ - 1f), new float2(box.MaxX + 1f, box.MaxZ + 1f)), Found = found };
                tree.Iterate(ref iterator);
            }

            var unique = new List<Entity>();
            var seen = new HashSet<Entity>();
            foreach (Entity e in found)
            {
                if (seen.Add(e))
                    unique.Add(e);
            }

            return unique;
        }

        /// <summary>Deletes those of the objects that are pictures of the road's markings, owned by a road piece, inside an area that takes the markings of that piece.</summary>
        private int RemoveCovered(List<Entity> objects)
        {
            List<Entity> doomed = CoveredDecals(objects);
            if (doomed.Count > 0)
            {
                using (var array = new NativeArray<Entity>(doomed.ToArray(), Allocator.Temp))
                    EntityManager.AddComponent<Deleted>(array);
                DecalsRemoved += doomed.Count;
            }

            if (!m_LoggedDecals && m_DecalSeen.Count > 0)
                LogDecals();
            return doomed.Count;
        }

        /// <summary>Those of the objects that are pictures of the road's markings, owned by a road piece, inside an area that takes the markings of that piece.</summary>
        private List<Entity> CoveredDecals(List<Entity> objects)
        {
            var doomed = new List<Entity>();
            foreach (Entity e in objects)
            {
                if (!EntityManager.Exists(e) || !EntityManager.HasComponent<Game.Objects.Secondary>(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Game.Tools.Temp>(e))
                    continue;
                if (!EntityManager.TryGetComponent(e, out PrefabRef prefabRef) || !EntityManager.TryGetComponent(e, out Owner owner) || !EntityManager.TryGetComponent(e, out Game.Objects.Transform transform))
                    continue;
                if (!IsMarkingDecal(prefabRef.m_Prefab))
                    continue;
                Entity o = owner.m_Owner;
                MarkingOwner kind = o == Entity.Null || !EntityManager.Exists(o) ? MarkingOwner.Other : EntityManager.HasComponent<Edge>(o) ? MarkingOwner.Edge : EntityManager.HasComponent<Node>(o) ? MarkingOwner.Node : MarkingOwner.Other;
                var marking = new VanillaMarking { X = transform.m_Position.x, Y = transform.m_Position.y, Z = transform.m_Position.z, Owner = kind, Crossing = false };
                if (VanillaClear.Covered(m_RegionList, marking))
                    doomed.Add(e);
            }

            return doomed;
        }

        /// <summary>
        /// Updates the road segments and junctions whose boxes meet the boxes: the game makes their secondary objects (and lanes) again. Returns how many. nextFrame: from this system's own update, at the end
        /// of Modification4B, the game's SecondaryObjectSystem has run already and Updated would be taken off at the end of the frame unseen, so it is given at the start of the next one.
        /// </summary>
        private int RedrawRoadsIn(List<ClearBox> boxes, bool nextFrame)
        {
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_Search.GetNetSearchTree(true, out JobHandle dependencies);
            dependencies.Complete();
            var found = new List<Entity>();
            foreach (ClearBox box in boxes)
            {
                var iterator = new BoxIterator { Box = new Bounds2(new float2(box.MinX - 1f, box.MinZ - 1f), new float2(box.MaxX + 1f, box.MaxZ + 1f)), Found = found };
                tree.Iterate(ref iterator);
            }

            var roads = new List<Entity>();
            var seen = new HashSet<Entity>();
            foreach (Entity e in found)
            {
                if (!seen.Add(e) || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) || EntityManager.HasComponent<Game.Tools.Temp>(e))
                    continue;
                if ((EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<Node>(e)) && EntityManager.HasComponent<Road>(e))
                    roads.Add(e);
            }

            if (roads.Count > 0 && nextFrame)
            {
                NextFrameUpdateSystem.Instance.Mark(roads);
            }
            else if (roads.Count > 0)
            {
                using (var array = new NativeArray<Entity>(roads.ToArray(), Allocator.Temp))
                    EntityManager.AddComponent<Updated>(array);
            }

            return roads.Count;
        }

        /// <summary>
        /// Whether an object prefab is a picture of the road's markings: it means something for a lane (LaneDirectionData: the arrows; TrafficSignData: the words and symbols of reserved lanes) and every mesh of it
        /// is a decal (a sign on a post has a mesh that is not). Kept for the session; the names met are logged once.
        /// </summary>
        private bool IsMarkingDecal(Entity prefab)
        {
            if (!m_DecalPrefab.TryGetValue(prefab, out bool known))
            {
                known = false;
                if ((EntityManager.HasComponent<LaneDirectionData>(prefab) || EntityManager.HasComponent<TrafficSignData>(prefab)) && EntityManager.TryGetBuffer(prefab, true, out DynamicBuffer<SubMesh> meshes) && meshes.Length > 0)
                {
                    known = true;
                    for (int i = 0; i < meshes.Length && known; i++)
                    {
                        known = EntityManager.TryGetComponent(meshes[i].m_SubMesh, out MeshData mesh) && (mesh.m_State & MeshFlags.Decal) != 0;
                    }
                }

                m_DecalPrefab[prefab] = known;
            }

            if (!m_LoggedDecals)
            {
                m_DecalSeen.TryGetValue(prefab, out int count);
                m_DecalSeen[prefab] = count + 1;
            }

            return known;
        }

        private void LogDecals()
        {
            m_LoggedDecals = true;
            var lines = new List<string>();
            foreach (KeyValuePair<Entity, int> pair in m_DecalSeen)
                lines.Add($"  \"{NameOf(pair.Key)}\": {(m_DecalPrefab.TryGetValue(pair.Key, out bool decal) && decal ? "a picture of the markings: taken away inside an area" : "kept")}, met {pair.Value} time(s)");
            Mod.log.Info("Clear: the game's secondary objects met inside the areas (what each was taken for):\n" + string.Join("\n", lines));
        }
    }
}
