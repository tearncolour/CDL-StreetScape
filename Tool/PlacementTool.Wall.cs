using System;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// Putting a sign on a building's wall (0.12.2): with a wall support chosen (flat on the wall, or on a bracket) the tool's ray takes the static objects too (InitializeRaycast), and a hit on a
    /// building or a part of one, where the surface is a wall, is where the sign goes. WallMount makes the facing (a flat board faces out; a bracket's board faces along the wall, toward the camera, and
    /// \ turns it the other way) and the height (the board's middle at the cursor; the standard height when the cursor is near it). The ground is the building's own height. Read only: nothing
    /// of the building is changed, and the sign does not follow it (a building that is demolished leaves the sign where it was).
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private TerrainSystem m_Terrain;
        private int m_WallLogs, m_SurfaceLogs;
        private SurfacePlan m_SurfaceLast;
        private float m_HitDirX, m_HitDirY, m_HitDirZ;

        private int m_WallHeightLogs;
        private HeightBound m_WallHeightBound;

        /// <summary>The support chosen in the panel goes on a wall.</summary>
        private static bool WallChosen()
        {
            SupportDef support = SupportCatalog.ById(SelectedSupport);
            return support != null && SupportKinds.OnWall(support.Kind);
        }

        /// <summary>The building an entity is or is a part of (its owners, a few levels up); Entity.Null when there is none.</summary>
        private static Entity BuildingOf(EntityManager em, Entity entity)
        {
            for (int i = 0; i < 8 && entity != Entity.Null && em.Exists(entity); i++)
            {
                if (em.HasComponent<Game.Buildings.Building>(entity))
                    return entity;
                if (!em.TryGetComponent(entity, out Owner owner))
                    break;
                entity = owner.m_Owner;
            }

            return Entity.Null;
        }

        /// <summary>
        /// The height of the ground under a point of a building's wall: the building's own (the game levels the ground of its lot to it), else the terrain's; never above the point. The building's is read
        /// first since it costs nothing, where the terrain's heights may make the frame wait for their copy from the graphics card after the ground changed.
        /// </summary>
        private float GroundAt(float3 at, Entity building)
        {
            float y = float.NaN;
            if (EntityManager.TryGetComponent(building, out Game.Objects.Transform transform))
                y = transform.m_Position.y;
            if (float.IsNaN(y) || float.IsInfinity(y))
            {
                m_Terrain = m_Terrain ?? World.GetOrCreateSystemManaged<TerrainSystem>();
                TerrainHeightData data = m_Terrain.GetHeightData(true);
                if (data.isCreated)
                    y = TerrainUtils.SampleHeight(ref data, at);
            }

            if (float.IsNaN(y) || float.IsInfinity(y))
                y = at.y;
            return Math.Min(y, at.y);
        }

        /// <summary>
        /// The highest a lower edge may be on a building (WallHeight.Highest): its prefab's bounds top over the foot of the wall, from the ECS (the transform, the prefab's ObjectGeometryData: no mesh is read);
        /// not known when the components are not there (then WallHeight's ceiling).
        /// </summary>
        private float HighestOn(Entity building, float ground)
        {
            float top = float.NaN;
            try
            {
                if (EntityManager.TryGetComponent(building, out Game.Objects.Transform transform) && EntityManager.TryGetComponent(building, out PrefabRef prefab)
                    && EntityManager.TryGetComponent(prefab.m_Prefab, out ObjectGeometryData geometry))
                    top = transform.m_Position.y + geometry.m_Bounds.max.y - ground;
            }
            catch (Exception)
            {
            }

            return WallHeight.Highest(top);
        }

        /// <summary>The wall under the cursor: the point the ray hit, the wall's level outward normal, the ground under the point and the building. False when the ray hit no building's wall.</summary>
        private bool TryWall(out Vec3 point, out Vec3 normal, out float ground, out Entity building, out Entity hitEntity)
        {
            point = Vec3.Zero;
            normal = Vec3.Zero;
            ground = 0f;
            building = Entity.Null;
            hitEntity = Entity.Null;
            Camera camera = m_Camera.activeCamera;
            if (camera == null || !GetRaycastResult(out Entity owner, out RaycastHit hit))
                return false;
            hitEntity = hit.m_HitEntity;
            m_HitDirX = hit.m_HitDirection.x;
            m_HitDirY = hit.m_HitDirection.y;
            m_HitDirZ = hit.m_HitDirection.z;
            building = BuildingOf(EntityManager, hit.m_HitEntity);
            if (building == Entity.Null)
                building = BuildingOf(EntityManager, owner);
            if (building == Entity.Null)
                return false;
            Line3.Segment ray = ToolRaycastSystem.CalculateRaycastLine(camera);
            if (!WallMount.TryNormal(RoadReader.ToVec(hit.m_HitDirection), RoadReader.ToVec(ray.b - ray.a), out normal))
                return false;
            point = RoadReader.ToVec(hit.m_HitPosition);
            ground = GroundAt(hit.m_HitPosition, building);
            return true;
        }

        /// <summary>
        /// Place mode with a wall support: the sign on the wall under the cursor (or the reason it cannot go there), its preview, the line of help, and a click places it. The item stands on the wall at the
        /// height of the ground, faces as WallMount says and keeps the height of its lower edge as its reach; it has no road.
        /// </summary>
        private void UpdatePlaceWall(SignChoice choice, SupportDef chosen, bool click)
        {
            // A text board goes on what is under the cursor, a wall or a roof, whichever of the two supports is chosen (0.12.77; the owner: "还有建筑物屋顶还是不能放置": he had picked the wall support and
            // over a roof it said a hint only).
            if (chosen.Kind == SupportKind.WallFlat)
            {
                SurfacePlan plan = SurfaceUnderCursor(TextSupports.IsText(choice.Def), chosen.Roof);
                if (plan == SurfacePlan.Roof && !chosen.Roof)
                    chosen = SupportCatalog.ById(SupportCatalog.WallRoof) ?? chosen;
                else if (plan == SurfacePlan.Wall && chosen.Roof)
                    chosen = SupportCatalog.ById(SupportCatalog.WallFlat) ?? chosen;
            }

            if (chosen.Roof)
            {
                UpdatePlaceRoof(choice, chosen, click);
                return;
            }

            SignDef def = choice.Def;
            m_NearPosts.Clear();
            if (JunctionFill.CanRead(def.Id))
                SetReadNote(null);
            // No road: the band of a draft that says "auto" is the one for no road (40 to 70 km/h).
            string spec = GuideTemplates.ResolveSpeed(def, Draft, 0f, false, false);
            SignInstance instance = SignInstances.Of(def, spec);
            if (!SupportPlanner.FitsWall(instance.Def, chosen.Kind))
            {
                HidePreview();
                SetHint("wall-large", 0, 0f, 0f, 0f, false, true, chosen.Zh, true);
                Debug = $"{def.Id} is too large for a wall bracket.";
                m_Outcome = "refused: " + Debug;
                return;
            }

            if (!TryWall(out Vec3 point, out Vec3 normal, out float ground, out Entity building, out Entity hitEntity))
            {
                HidePreview();
                // A roof under the cursor is told apart from no building at all (0.12.76: the roof support stands a text on it).
                bool onRoof = TryRoof(out _, out _);
                SetHint(onRoof ? "wall-roof" : "wall-none", 0, 0f, 0f, 0f, false, chosen.Kind == SupportKind.WallBracket, chosen.Zh, true);
                Debug = onRoof ? "That is a roof: a text light box stands on it with the roof support." : "Move the cursor onto a building's wall.";
                m_Outcome = "ignored: no building's wall under the cursor";
                return;
            }

            float standard = instance.Def.BottomClearance;
            float highest = HighestOn(building, ground);
            float asked = WallMount.Asked(point.Y - ground, instance.Def.Height, standard);
            float lower = WallHeight.Limit(asked, highest, out HeightBound bound);
            SupportDef support = chosen;
            float yaw;
            if (chosen.Kind == SupportKind.WallBracket)
            {
                // Toward the camera along the wall, or the other way with \ ; the arm out of the wall either way (the mirror when it reaches to -X).
                Vector3 eye = m_Camera.activeCamera.transform.position;
                int facing = WallMount.FacingToward(normal, point, new Vec3(eye.x, eye.y, eye.z)) * (m_Flip ? -1 : 1);
                yaw = WallMount.BracketYaw(normal, facing, out int side);
                SupportDef mirror = SupportCatalog.ById(chosen.MirrorId);
                if (mirror != null && (side < 0) != (chosen.Side < 0))
                    support = mirror;
            }
            else
            {
                yaw = WallMount.FlatYaw(normal);
            }

            var position = new Vec3(point.X, ground, point.Z);
            // The arrow keys move the board on the facade (0.12.76), along the arc where a text board is bent to a curved wall; the radius is read from rays across the board.
            float radius = FacadeRadiusAt(point, normal, instance.Def.Width, building, TextSupports.IsText(instance.Def), chosen.Kind == SupportKind.WallFlat);
            ReadFacadeNudge(false, normal);
            float cursorLower = lower;
            ApplyFacade(ref position, ref yaw, ref lower, normal, radius, highest, ref bound);
            if (m_WallHeightLogs < 12 && (click || (bound != HeightBound.None && bound != m_WallHeightBound)))
            {
                m_WallHeightLogs++;
                Mod.log.Info($"Wall: {WallHeight.Describe(point.Y, ground, asked, lower, highest, bound)}{(lower != cursorLower ? $" (the keys moved it from {cursorLower:0.0})" : "")}; building {building.Index}:{building.Version}.");
            }

            m_WallHeightBound = bound;
            if (radius != 0f)
            {
                spec = WithFacadeRadius(def, spec, radius);
                instance = SignInstances.Of(def, spec);
            }

            bool valid = true;
            string note = m_WantText ? $"wall of building {building.Index}:{building.Version} (hit {hitEntity.Index}), hit direction ({m_HitDirX:0.00}, {m_HitDirY:0.00}, {m_HitDirZ:0.00}), level outward normal x {normal.X:0.00} z {normal.Z:0.00}, ground {ground:0.0}, lower edge {lower:0.0} m (standard {standard:0.0}), {support.Id}{(radius != 0f ? $", wall radius {radius:0.0} m" : "")}" : "";
            var records = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records : null;
            if (records != null && records.Count >= kMaxItems)
            {
                valid = false;
                if (m_WantText)
                    note += "; too many signs";
            }

            if (m_WallLogs < 12 && click)
            {
                m_WallLogs++;
                Mod.log.Info($"Wall: {note}.");
            }

            if (m_WantText)
                Debug = note + (valid ? "" : " [cannot place]");
            SetHint("wall", 0, lower, standard, 0f, false, chosen.Kind == SupportKind.WallBracket, chosen.Zh, !valid);
            ShowPreview(instance, support.Hash, lower, position, yaw);
            DrawOverlay(false, null, 0f, default, position, yaw, valid, def);
            if (!click)
                return;
            if (valid)
            {
                Place(def, spec, support.Hash, lower, position, yaw, Entity.Null, 0f, note);
                ResetFacade();
                m_Outcome = "placed on a wall: " + note;
            }
            else
            {
                m_Outcome = "refused: " + note;
            }
        }

        /// <summary>
        /// What is under the cursor, a wall or a roof, and what a board does with it (BuildingSurface): the hit's normal, the building it is on, whether the board is a text light box and which support
        /// is chosen. Written to the log when it changes (the first thirty), so that a "cannot place" can be told from the ray.
        /// </summary>
        private SurfacePlan SurfaceUnderCursor(bool text, bool roofSupport)
        {
            if (!GetRaycastResult(out Entity owner, out RaycastHit hit))
                return SurfacePlan.Nothing;
            Entity building = BuildingOf(EntityManager, hit.m_HitEntity);
            if (building == Entity.Null)
                building = BuildingOf(EntityManager, owner);
            float3 direction = hit.m_HitDirection;
            SurfaceKind surface = BuildingSurface.Classify(direction.x, direction.y, direction.z);
            SurfacePlan plan = BuildingSurface.Plan(building != Entity.Null, surface, text, TextSupports.Enabled, roofSupport);
            if (plan != m_SurfaceLast && m_SurfaceLogs < 30)
            {
                m_SurfaceLogs++;
                Mod.log.Info(BuildingSurface.Describe(direction.x, direction.y, direction.z, hit.m_HitEntity.Index, building != Entity.Null, surface, plan, text, roofSupport));
            }

            m_SurfaceLast = plan;
            return plan;
        }
    }
}
