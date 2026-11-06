using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Collections;
using Colossal.Entities;
using Game.Common;
using Game.Prefabs;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ObjectTransform = Game.Objects.Transform;

namespace CDLC.TS
{
    /// <summary>
    /// Items that are fixed to an object that is not a road (0.12.75, the follow's host side): a sign on the game's own lamp post or signal pole (SupportKind.Attached; lamp ads on a lamp post are such signs)
    /// and a sign on a building's wall. They do not go with a road: the game moves a lamp post by writing a new Transform on the same entity (SubObjectSystem reuses the nearest old sub-object of the same prefab
    /// and adds Updated when the Transform differs), and Move It moves a building the same way, so the host's Transform is the event. The host is found when the item is linked (the game's static object search
    /// tree: the nearest lamp post or signal pole within HostPick.Fixed of the item, as HostMissing finds it, or the building whose box the item is within a metre and a half of) and kept; when its pose
    /// changes the item goes by the same motion (FollowWatch.LinkHosted). Another mod's pole or arm is not the game's and has no event: not followed.
    /// </summary>
    internal sealed partial class ItemRoadSystem
    {
        private enum HostKind
        {
            None,
            Post,
            Building,
            Foreign
        }

        /// <summary>How far from a building's box (m) a wall sign's place may be to be on that building: the wall's bracket and the box of a mesh that is not the wall.</summary>
        private const float kWallReach = 1.5f;

        /// <summary>How far above or below the item a lamp post's foot may be (m): HostFinder's tolerance for a post on a slope.</summary>
        private const float kPostHeight = 2.5f;

        private const float kHostRetrySeconds = 60f;
        private const int kHostMissedLogged = 5;

        private EntityQuery m_UpdatedHosts, m_DeletedHosts, m_Posts;
        private Game.Objects.SearchSystem m_ObjectSearch;
        private FollowWatch<Entity, Entity>.HostPoseOf m_HostPoseOf;
        private readonly Dictionary<Entity, float> m_HostRetry = new Dictionary<Entity, float>();
        private int m_HostMissed;

        private void CreateHosts()
        {
            ComponentType[] hosts = { ComponentType.ReadOnly<Game.Objects.StreetLight>(), ComponentType.ReadOnly<Game.Objects.TrafficLight>(), ComponentType.ReadOnly<Game.Buildings.Building>() };
            m_UpdatedHosts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ObjectTransform>(), ComponentType.ReadOnly<Updated>() },
                Any = hosts,
                None = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_DeletedHosts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Deleted>() },
                Any = hosts,
                None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_Posts = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ObjectTransform>() },
                Any = new[] { ComponentType.ReadOnly<Game.Objects.StreetLight>(), ComponentType.ReadOnly<Game.Objects.TrafficLight>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            m_ObjectSearch = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
            m_HostPoseOf = HostPose;
        }

        /// <summary>What an item is fixed to, read from its support as the editor reads it (the planner's choice for a lamp ad is the game's lamp post).</summary>
        private static HostKind HostKindOf(TsItem item)
        {
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            SupportKind kind = def == null ? SupportKind.SinglePole : SupportCatalog.For(def, item.SupportHash).Kind;
            if (SupportKinds.OnWall(kind))
                return HostKind.Building;
            if (kind == SupportKind.Attached)
                return HostKind.Post;
            return SupportKinds.Hosted(kind) ? HostKind.Foreign : HostKind.None;
        }

        /// <summary>The hosts the game updated this frame (a Transform written) and those it deleted are told to the follow, once each, when anything is linked.</summary>
        private void DrainHosts()
        {
            if (m_Follow.Count == 0)
                return;
            if (!m_DeletedHosts.IsEmptyIgnoreFilter)
            {
                using (var gone = m_DeletedHosts.ToEntityArray(Unity.Collections.Allocator.Temp))
                {
                    foreach (Entity host in gone)
                        m_Follow.EdgeGone(host);
                }
            }

            if (!m_UpdatedHosts.IsEmptyIgnoreFilter)
            {
                using (var moved = m_UpdatedHosts.ToEntityArray(Unity.Collections.Allocator.Temp))
                {
                    foreach (Entity host in moved)
                        m_Follow.Dirty(host);
                }
            }
        }

        private bool HostPose(Entity host, out Vec3 position, out float yaw)
        {
            position = default;
            yaw = 0f;
            if (m_OtherHosts.TryGetValue(host, out OtherHost other))
            {
                position = other.At;
                yaw = other.Yaw;
                return true;
            }

            if (!EntityManager.Exists(host) || EntityManager.HasComponent<Deleted>(host) || !EntityManager.TryGetComponent(host, out ObjectTransform transform))
                return false;
            position = new Vec3(transform.m_Position.x, transform.m_Position.y, transform.m_Position.z);
            float3 forward = math.mul(transform.m_Rotation, new float3(0f, 0f, 1f));
            yaw = math.atan2(forward.x, forward.z);
            return position.IsFinite && !float.IsNaN(yaw);
        }

        /// <summary>
        /// The item is fixed to its host: linked when it is not yet (or its host was deleted and it is stale), the host looked for once a minute while there is none. An item that has no host to be found keeps
        /// its place and is logged (the first few).
        /// </summary>
        private void EnsureHostLink(Entity entity, TsItem item, HostKind kind, float now)
        {
            if (m_Follow.IsHostLinked(entity))
                return;
            if (m_HostRetry.TryGetValue(entity, out float notBefore) && now < notBefore)
                return;
            Entity host = kind == HostKind.Building ? FindBuilding(item) : FindPost(item);
            if (host == Entity.Null || !HostPose(host, out Vec3 hostAt, out float hostYaw))
            {
                m_HostRetry[entity] = now + kHostRetrySeconds + entity.Index % 16;
                if (m_HostMissed++ < kHostMissedLogged)
                    Mod.log.Info($"Follow: the item {entity.Index}:{entity.Version} on {(kind == HostKind.Building ? "a building's wall" : "a lamp post or signal pole")} at ({item.Position.x:0.#}, {item.Position.z:0.#}) has no {(kind == HostKind.Building ? "building within " + kWallReach + " m" : "post within " + HostPick.Fixed + " m")}: it is not followed.");
                return;
            }

            m_HostRetry.Remove(entity);
            m_Follow.LinkHosted(entity, host, hostAt, hostYaw, new Vec3(item.Position.x, item.Position.y, item.Position.z), item.Yaw, unchecked((int)item.Group));
        }

        /// <summary>The objects of the game's static search tree whose box touches a square round the item's place, in m_Found.</summary>
        private void ObjectsAround(TsItem item, float reach)
        {
            NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_ObjectSearch.GetStaticSearchTree(true, out JobHandle dependencies);
            dependencies.Complete();
            m_Found.Clear();
            var iterator = new EdgeIterator { Box = new Colossal.Mathematics.Bounds2(new float2(item.Position.x - reach, item.Position.z - reach), new float2(item.Position.x + reach, item.Position.z + reach)), Found = m_Found };
            tree.Iterate(ref iterator);
        }

        /// <summary>
        /// The nearest lamp post or signal pole within HostPick.Fixed of the item (its foot is where the item stands), as HostMissing finds it: by the game's static search tree, and when the tree has none there
        /// (that lamps are in it is read from the game's code, not seen) by a walk over every post of the city, which is what HostFinder does.
        /// </summary>
        private Entity FindPost(TsItem item)
        {
            ObjectsAround(item, HostPick.Fixed + 1f);
            Entity best = NearestPost(m_Found, item, out float nearest);
            if (best != Entity.Null)
                return best;
            using (Unity.Collections.NativeArray<Entity> all = m_Posts.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                m_Found.Clear();
                foreach (Entity post in all)
                    m_Found.Add(post);
            }

            best = NearestPost(m_Found, item, out nearest);
            if (best != Entity.Null && m_HostMissed++ < kHostMissedLogged)
                Mod.log.Info($"Follow: the game's static search tree did not have the post at ({item.Position.x:0.#}, {item.Position.z:0.#}); it was found by a walk over the posts ({nearest:0.00} m): the tree is not what the posts are looked up in.");
            return best;
        }

        private Entity NearestPost(List<Entity> candidates, TsItem item, out float nearest)
        {
            Entity best = Entity.Null;
            nearest = HostPick.Fixed * HostPick.Fixed;
            foreach (Entity candidate in candidates)
            {
                if (!EntityManager.Exists(candidate) || !(EntityManager.HasComponent<Game.Objects.StreetLight>(candidate) || EntityManager.HasComponent<Game.Objects.TrafficLight>(candidate))
                    || !EntityManager.TryGetComponent(candidate, out ObjectTransform transform) || System.Math.Abs(transform.m_Position.y - item.Position.y) > kPostHeight)
                    continue;
                float dx = transform.m_Position.x - item.Position.x, dz = transform.m_Position.z - item.Position.z, d2 = dx * dx + dz * dz;
                if (d2 <= nearest)
                {
                    nearest = d2;
                    best = candidate;
                }
            }

            nearest = (float)System.Math.Sqrt(nearest);
            return best;
        }

        /// <summary>The building whose box (the prefab's bounds in the building's own frame) the item's place is nearest to, within kWallReach of it.</summary>
        private Entity FindBuilding(TsItem item)
        {
            ObjectsAround(item, kWallReach + 2f);
            Entity best = Entity.Null;
            float nearest = kWallReach;
            foreach (Entity candidate in m_Found)
            {
                if (!EntityManager.Exists(candidate) || !EntityManager.HasComponent<Game.Buildings.Building>(candidate) || !EntityManager.TryGetComponent(candidate, out ObjectTransform transform)
                    || !EntityManager.TryGetComponent(candidate, out PrefabRef prefab) || !EntityManager.TryGetComponent(prefab.m_Prefab, out ObjectGeometryData geometry))
                    continue;
                float3 local = math.mul(math.inverse(transform.m_Rotation), item.Position - transform.m_Position);
                float3 centre = (geometry.m_Bounds.min + geometry.m_Bounds.max) * 0.5f, half = (geometry.m_Bounds.max - geometry.m_Bounds.min) * 0.5f;
                float distance = math.length(math.max(math.abs(local - centre) - half, 0f));
                if (distance < nearest)
                {
                    nearest = distance;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
