using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Game.Common;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// A board on a building, moved by the arrow keys and fitted to the facade (0.12.76; the owner: "可以选择不放托架框，然后能支持放到建筑顶部。同时按方向上下左右可以调整在建筑立面上的位置"): left and
    /// right along the facade as the screen shows (along the arc of a curved one), up and down its lower edge, Shift the larger step; on a roof the arrows move it across the roof; a text board on a curved
    /// facade is bent to it (the facade's points read by a few rays across the board, ArcFit); on a roof it stands upright, facing the camera (the turn keys turn it) and not overhanging the edge by more
    /// than RoofFit.Tolerance. The editor's four buttons do the same for the item being edited. Nothing here can be run outside the game: the pure parts are FacadeMove, RoofFit and ArcFit.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>What the arrow keys have moved the board being placed: along the facade and up (m), or across the roof in the world's x and z (m). Forgotten when the board is placed or the tool is left.</summary>
        private float m_FacadeAlong, m_FacadeLift, m_RoofDx, m_RoofDz;

        private RaycastSystem m_FacadeRays;
        private readonly object m_FacadeContext = new object();
        private int m_FacadeAsked, m_FacadeAskedFrame, m_FacadeLastAsk = -1000, m_FacadeUnanswered;
        private Entity m_FacadeBuilding;
        private Vec3 m_FacadeAskedAt, m_FacadeAskedNormal;
        private float m_FacadeAskedWidth, m_FacadeRadius;
        private bool m_FacadeBroken;
        private readonly FacadeSampling.Log m_FacadeLog = new FacadeSampling.Log();

        /// <summary>A fit asked for from the editor (the item on its wall): the rays are asked in one frame and read in the next, and the radius is written to the item's settings.</summary>
        private Entity m_FitEntity;
        private int m_FitAskedFrame, m_FitAsked, m_FitTries;
        private Vec3 m_FitPoint, m_FitNormal;
        private float m_FitWidth;
        private Entity m_FitBuilding;

        /// <summary>The roof support chosen in the panel.</summary>
        private static bool RoofChosen()
        {
            SupportDef support = SupportCatalog.ById(SelectedSupport);
            return support != null && support.Roof;
        }

        private void ResetFacade()
        {
            m_FacadeAlong = m_FacadeLift = m_RoofDx = m_RoofDz = 0f;
            m_FacadeAsked = 0;
            m_FacadeRadius = 0f;
        }

        private static Vec3 GroundOf(Vector3 v)
        {
            float l = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            return l < 1e-4f ? Vec3.Zero : new Vec3(v.x / l, 0f, v.z / l);
        }

        /// <summary>The arrow keys of this frame added to what the board has been moved: along the facade and up, or across the roof (up key forward).</summary>
        private void ReadFacadeNudge(bool roof, Vec3 normal)
        {
            if (m_ArrowX == 0 && m_ArrowY == 0)
                return;
            Camera camera = m_Camera.activeCamera;
            if (camera == null)
                return;
            Vec3 right = GroundOf(camera.transform.right), forward = GroundOf(camera.transform.forward);
            if (roof)
            {
                (float dx, float dz) = FacadeMove.AcrossRoof(0f, 0f, right.X, right.Z, forward.X, forward.Z, m_ArrowX, -m_ArrowY, m_Shift);
                m_RoofDx = Mathf.Clamp(m_RoofDx + dx, -FacadeMove.MaxShift, FacadeMove.MaxShift);
                m_RoofDz = Mathf.Clamp(m_RoofDz + dz, -FacadeMove.MaxShift, FacadeMove.MaxShift);
                return;
            }

            Vec3 tangent = WallMount.Along(normal);
            if (m_ArrowX != 0)
                m_FacadeAlong = FacadeMove.Accumulate(m_FacadeAlong, m_ArrowX * FacadeMove.SenseOf(tangent.X, tangent.Z, right.X, right.Z), m_Shift);
            if (m_ArrowY != 0)
                m_FacadeLift = WallHeight.Lift(m_FacadeLift, -m_ArrowY * m_LiftStep);
        }

        /// <summary>The board's place and lower edge with the arrow keys' moves on the wall: along the facade (an arc where the wall is curved) and up.</summary>
        private void ApplyFacade(ref Vec3 position, ref float yaw, ref float lower, Vec3 normal, float radius, float highest, ref HeightBound bound)
        {
            if (m_FacadeAlong != 0f)
            {
                FacadePlace moved = FacadeMove.Along(position.X, position.Z, normal.X, normal.Z, radius, m_FacadeAlong);
                position = new Vec3(moved.X, position.Y, moved.Z);
                yaw += moved.Yaw - WallMount.FlatYaw(normal);
            }

            if (m_FacadeLift != 0f)
            {
                float moved = (float)Math.Round((lower + m_FacadeLift) * 10f) / 10f;
                lower = WallHeight.Limit(moved, highest, out HeightBound held);
                if (held != HeightBound.None)
                    bound = held;
            }
        }

        // ------------------------------------------------------------------ a curved facade

        /// <summary>
        /// The radius of the facade under the cursor for a text board (m, signed; 0 flat or unknown), from rays across the board's width: the points they hit on the same building are fitted to a circle
        /// (FacadeSampling, ArcFit). The rays are asked for in one frame and read in the next (the game's RaycastSystem answers them before the tools of that frame and clears the answer in the one after:
        /// 0.12.77 read two frames on, got nothing every time and said nothing: the owner, 2 Nov); a new ask is made when the cursor's place on the wall has moved by half a metre or is another building's.
        /// Every ask's answer is in the log (the first thirty), and so is each reason for not asking.
        /// </summary>
        private float FacadeRadiusAt(Vec3 point, Vec3 normal, float width, Entity building, bool text, bool wallFlat)
        {
            FacadeSkip skip = FacadeSampling.Why(TextSupports.Enabled, m_FacadeBroken, text, wallFlat, building != Entity.Null);
            if (skip != FacadeSkip.None)
            {
                if (text && m_FacadeLog.Skipped(skip))
                    Mod.log.Info("Facade: not read: " + FacadeSampling.SkipText(skip) + ".");
                return 0f;
            }

            try
            {
                m_FacadeRays = m_FacadeRays ?? World.GetOrCreateSystemManaged<RaycastSystem>();
                int frame = UnityEngine.Time.frameCount;
                if (m_FacadeAsked > 0)
                {
                    FacadeWait wait = FacadeSampling.Next(frame - m_FacadeAskedFrame, AnswerHeld(m_FacadeAsked));
                    if (wait == FacadeWait.Read)
                        ReadFacadeAnswer();
                    else if (wait == FacadeWait.Late)
                        FacadeAnswerLate(frame);
                }

                float moved = m_FacadeLastAsk < 0 ? float.MaxValue : Vec3.DistanceXZ(point, m_FacadeAskedAt);
                if (FacadeSampling.ShouldAsk(m_FacadeAsked > 0, frame - m_FacadeLastAsk, moved, m_FacadeBuilding != building, m_FacadeUnanswered))
                    AskFacade(point, normal, width, building);
            }
            catch (Exception ex)
            {
                m_FacadeBroken = true;
                m_FacadeRadius = 0f;
                Mod.log.Warn("Facade: the curve of the wall could not be read (" + ex.GetType().Name + ": " + ex.Message + "): boards stay flat on walls.");
            }

            return m_FacadeBuilding == building ? m_FacadeRadius : 0f;
        }

        /// <summary>Whether the game has an answer of the length that was asked for under this tool's context (read in the frame after the ask).</summary>
        private bool AnswerHeld(int asked)
        {
            NativeArray<RaycastResult> results = m_FacadeRays.GetResult(m_FacadeContext);
            return results.IsCreated && results.Length == asked;
        }

        private void FacadeAnswerLate(int frame)
        {
            m_FacadeUnanswered++;
            if (m_FacadeLog.Attempt())
                Mod.log.Info($"Facade: the game's answer to {m_FacadeAsked} ray(s) asked in frame {m_FacadeAskedFrame} was not there in frame {frame}: {m_FacadeUnanswered} unanswered so far.");
            m_FacadeAsked = 0;
        }

        /// <summary>The rays of an ask: straight in at the wall from out in front of it through the plan's places, with what the tool's own raycast is set to (the tool's disable flags are left out: the answer is wanted whatever the tool is doing).</summary>
        private bool AddFacadeRays(Vec3 point, Vec3 normal, float width, out int asked)
        {
            asked = 0;
            RaycastFlags flags = m_ToolRaycastSystem.raycastFlags & ~(RaycastFlags.ToolDisable | RaycastFlags.UIDisable);
            (float ex, float ey, float ez) = FacadeSampling.Eye(point.X, point.Y, point.Z, normal.X, normal.Z);
            foreach (FacadeRay ray in FacadeSampling.Plan(ex, ey, ez, point.X, point.Y, point.Z, normal.X, normal.Z, width))
            {
                m_FacadeRays.AddInput(m_FacadeContext, new RaycastInput
                {
                    m_Line = new Colossal.Mathematics.Line3.Segment { a = new float3(ray.FromX, ray.FromY, ray.FromZ), b = new float3(ray.ToX, ray.ToY, ray.ToZ) },
                    m_Owner = Entity.Null,
                    m_TypeMask = TypeMask.StaticObjects,
                    m_Flags = flags,
                    m_CollisionMask = m_ToolRaycastSystem.collisionMask
                });
                asked++;
            }

            return asked > 0;
        }

        private void AskFacade(Vec3 point, Vec3 normal, float width, Entity building)
        {
            if (!AddFacadeRays(point, normal, width, out int asked))
                return;
            m_FacadeAsked = asked;
            m_FacadeAskedFrame = m_FacadeLastAsk = UnityEngine.Time.frameCount;
            m_FacadeAskedAt = point;
            m_FacadeAskedNormal = normal;
            m_FacadeAskedWidth = width;
            m_FacadeBuilding = building;
        }

        /// <summary>What the rays hit, as FacadeSampling wants it: whether it hit, whether on the building the board is on, where, and the steepness of the surface there.</summary>
        private List<FacadeHit> FacadeHitsOf(Entity building)
        {
            NativeArray<RaycastResult> results = m_FacadeRays.GetResult(m_FacadeContext);
            var hits = new List<FacadeHit>(results.Length);
            for (int i = 0; i < results.Length; i++)
            {
                RaycastResult r = results[i];
                if (r.m_Owner == Entity.Null)
                {
                    hits.Add(new FacadeHit(false, false, 0f, 0f, 0f, 0f));
                    continue;
                }

                Entity hit = BuildingOf(EntityManager, r.m_Hit.m_HitEntity);
                if (hit == Entity.Null)
                    hit = BuildingOf(EntityManager, r.m_Owner);
                float3 direction = r.m_Hit.m_HitDirection;
                float length = math.length(direction);
                hits.Add(new FacadeHit(true, hit == building && building != Entity.Null, r.m_Hit.m_HitPosition.x, r.m_Hit.m_HitPosition.y, r.m_Hit.m_HitPosition.z, length > 1e-4f ? direction.y / length : 0f));
            }

            return hits;
        }

        private void ReadFacadeAnswer()
        {
            FacadeVerdict verdict = FacadeSampling.Decide(FacadeHitsOf(m_FacadeBuilding), m_FacadeAskedAt.X, m_FacadeAskedAt.Z, m_FacadeAskedNormal.X, m_FacadeAskedNormal.Z, m_FacadeAskedWidth);
            m_FacadeRadius = verdict.Bent ? verdict.Radius : 0f;
            m_FacadeUnanswered = 0;
            m_FacadeAsked = 0;
            if (m_FacadeLog.Attempt())
                Mod.log.Info("Facade: " + verdict.Text() + ".");
        }

        // ------------------------------------------------------------------ fitting a board that stands

        /// <summary>Whether the editor offers "fit to the wall" for the board of this sign on this support: a text board on a flat wall support (not the roof's), the supports on.</summary>
        internal static bool CanFitFacade(SignDef def, SupportDef support) =>
            TextSupports.Enabled && TextSupports.IsText(def) && support != null && support.Kind == SupportKind.WallFlat && !support.Roof;

        /// <summary>The editor's button: the wall under the board being edited is read again and its radius written to the board's settings (0 when the wall is flat). Done in the tool's update (ServeFacadeFit).</summary>
        internal void FitFacadeNow()
        {
            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            if (!CanFitFacade(def, SupportCatalog.ByHash(item.SupportHash)))
                return;
            m_FitEntity = EditEntity;
            m_FitAsked = 0;
            m_FitTries = 0;
        }

        /// <summary>The building the middle ray of the last answer hit (the one under the board), Entity.Null when it hit none.</summary>
        private Entity MiddleBuilding()
        {
            NativeArray<RaycastResult> results = m_FacadeRays.GetResult(m_FacadeContext);
            if (!results.IsCreated || results.Length == 0)
                return Entity.Null;
            RaycastResult r = results[results.Length / 2];
            if (r.m_Owner == Entity.Null)
                return Entity.Null;
            Entity building = BuildingOf(EntityManager, r.m_Hit.m_HitEntity);
            return building != Entity.Null ? building : BuildingOf(EntityManager, r.m_Owner);
        }

        /// <summary>
        /// The editor's fit, a step a frame: the rays are asked in this update and read in the next (three tries), the verdict is written as the board's radius and logged. The board's place is the point on
        /// its wall at the height of its middle; its facing is the wall's outward normal.
        /// </summary>
        private void ServeFacadeFit()
        {
            if (m_FitEntity == Entity.Null)
                return;
            if (m_FitEntity != EditEntity || !TryReadEdit(out TsItem item))
            {
                m_FitEntity = Entity.Null;
                return;
            }

            try
            {
                m_FacadeRays = m_FacadeRays ?? World.GetOrCreateSystemManaged<RaycastSystem>();
                int frame = UnityEngine.Time.frameCount;
                if (m_FitAsked == 0)
                {
                    SignDef def = SignCatalog.ByHash(item.TypeHash);
                    float width = def != null ? def.Width : 4f, height = def != null ? def.Height : 1f;
                    m_FitNormal = new Vec3(Mathf.Sin(item.Yaw), 0f, Mathf.Cos(item.Yaw));
                    m_FitPoint = new Vec3(item.Position.x, item.Position.y + item.Reach + height * 0.5f, item.Position.z);
                    m_FitWidth = width;
                    if (!AddFacadeRays(m_FitPoint, m_FitNormal, width, out m_FitAsked))
                    {
                        m_FitEntity = Entity.Null;
                        return;
                    }

                    m_FitAskedFrame = frame;
                    return;
                }

                FacadeWait wait = FacadeSampling.Next(frame - m_FitAskedFrame, AnswerHeld(m_FitAsked));
                if (wait == FacadeWait.Wait)
                    return;
                if (wait == FacadeWait.Late)
                {
                    m_FitAsked = 0;
                    if (++m_FitTries >= 3)
                    {
                        Mod.log.Info("Facade: fit to the wall: the game did not answer three times; the board is as it was.");
                        m_FitEntity = Entity.Null;
                    }

                    return;
                }

                Entity building = MiddleBuilding();
                FacadeVerdict verdict = building == Entity.Null
                    ? new FacadeVerdict { Why = "no building behind the board's middle" }
                    : FacadeSampling.Decide(FacadeHitsOf(building), m_FitPoint.X, m_FitPoint.Z, m_FitNormal.X, m_FitNormal.Z, m_FitWidth);
                float radius = verdict.Bent ? verdict.Radius : 0f;
                EditField(EditorTarget, "arc", radius.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                Mod.log.Info("Facade: fit to the wall (editor): " + verdict.Text() + (building == Entity.Null ? "" : $"; building {building.Index}:{building.Version}") + ".");
                m_FitEntity = Entity.Null;
                m_FitAsked = 0;
            }
            catch (Exception ex)
            {
                m_FitEntity = Entity.Null;
                Mod.log.Warn("Facade: fit to the wall failed (" + ex.GetType().Name + ": " + ex.Message + ").");
            }
        }

        /// <summary>The settings of a text board with the radius of the wall it is on (m; 0 leaves the settings as they were).</summary>
        private static string WithFacadeRadius(SignDef def, string spec, float radius)
        {
            if (radius == 0f || def?.Template == null || !TextSupports.IsText(def))
                return spec;
            return def.Template.Parse(spec).With("arc", radius.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).ToString();
        }

        // ------------------------------------------------------------------ a roof

        /// <summary>The roof under the cursor: the point the ray hit where the surface faces up (the normal's y over RoofMinNormalY) and the building. False when the ray hit no building's roof.</summary>
        private bool TryRoof(out Vec3 point, out Entity building)
        {
            point = Vec3.Zero;
            building = Entity.Null;
            if (!GetRaycastResult(out Entity owner, out RaycastHit hit))
                return false;
            building = BuildingOf(EntityManager, hit.m_HitEntity);
            if (building == Entity.Null)
                building = BuildingOf(EntityManager, owner);
            if (building == Entity.Null)
                return false;
            if (BuildingSurface.Classify(hit.m_HitDirection.x, hit.m_HitDirection.y, hit.m_HitDirection.z) != SurfaceKind.Roof)
                return false;
            point = RoadReader.ToVec(hit.m_HitPosition);
            return true;
        }

        /// <summary>How far the board's footprint reaches outside the building's bounds (m), or NaN when the bounds cannot be read (then only the hit point says where the roof is).</summary>
        private float RoofOverhang(Entity building, Vec3 position, float yaw, float width, float behind)
        {
            try
            {
                if (!EntityManager.TryGetComponent(building, out Game.Objects.Transform transform) || !EntityManager.TryGetComponent(building, out PrefabRef prefab)
                    || !EntityManager.TryGetComponent(prefab.m_Prefab, out ObjectGeometryData geometry))
                    return float.NaN;
                float3 forward = math.mul(transform.m_Rotation, new float3(0f, 0f, 1f));
                float buildingYaw = Mathf.Atan2(forward.x, forward.z);
                return RoofFit.Overhang(transform.m_Position.x, transform.m_Position.z, buildingYaw, geometry.m_Bounds.min.x, geometry.m_Bounds.max.x, geometry.m_Bounds.min.z, geometry.m_Bounds.max.z,
                    position.X, position.Z, yaw, width * 0.5f, 0.12f, behind);
            }
            catch (Exception)
            {
                return float.NaN;
            }
        }

        private int m_RoofLogs;
        private string m_RoofLast = "";

        /// <summary>The building's own box as the overhang test reads it, for the log: where it stands, how it is turned, its extent in its own frame.</summary>
        private string RoofBoundsText(Entity building)
        {
            try
            {
                if (!EntityManager.TryGetComponent(building, out Game.Objects.Transform transform) || !EntityManager.TryGetComponent(building, out PrefabRef prefab)
                    || !EntityManager.TryGetComponent(prefab.m_Prefab, out ObjectGeometryData geometry))
                    return "the building's bounds are not readable";
                float3 forward = math.mul(transform.m_Rotation, new float3(0f, 0f, 1f));
                return $"the building's box: at ({transform.m_Position.x:0.0}, {transform.m_Position.z:0.0}) turned {Mathf.Atan2(forward.x, forward.z) * 57.29578f:0} deg, x {geometry.m_Bounds.min.x:0.0}..{geometry.m_Bounds.max.x:0.0}, y {geometry.m_Bounds.min.y:0.0}..{geometry.m_Bounds.max.y:0.0}, z {geometry.m_Bounds.min.z:0.0}..{geometry.m_Bounds.max.z:0.0}";
            }
            catch (Exception ex)
            {
                return "the building's bounds could not be read (" + ex.GetType().Name + ")";
            }
        }

        /// <summary>The lower edge of a text board above the roof when it is put there (m): the legs' height.</summary>
        internal const float RoofLowerEdge = 0.5f;

        /// <summary>
        /// Place mode with the roof support (0.12.76): a text light box stands upright on the roof under the cursor, its lower edge RoofLowerEdge up (the up and down keys change it), facing the camera (the
        /// turn keys turn it), moved across the roof by the arrow keys; refused where it would overhang the roof's edge by more than RoofFit.Tolerance. Any other board is refused.
        /// </summary>
        private void UpdatePlaceRoof(SignChoice choice, SupportDef chosen, bool click)
        {
            SignDef def = choice.Def;
            m_NearPosts.Clear();
            string spec = GuideTemplates.ResolveSpeed(def, Draft, 0f, false, false);
            SignInstance instance = SignInstances.Of(def, spec);
            if (!TextSupports.Enabled)
            {
                HidePreview();
                SetHint("roof-off", 0, 0f, 0f, 0f, false, false, chosen.Zh, true);
                Debug = "The text supports are switched off (features.textSupports).";
                m_Outcome = "refused: " + Debug;
                return;
            }

            if (!TextSupports.IsText(instance.Def))
            {
                HidePreview();
                SetHint("roof-text", 0, 0f, 0f, 0f, false, false, chosen.Zh, true);
                Debug = $"{def.Id} does not stand on a roof: only the text light box.";
                m_Outcome = "refused: " + Debug;
                return;
            }

            if (!TryRoof(out Vec3 point, out Entity building))
            {
                HidePreview();
                SetHint("roof-none", 0, 0f, 0f, 0f, false, false, chosen.Zh, true);
                Debug = "Move the cursor onto the top of a building.";
                m_Outcome = "ignored: no building's roof under the cursor";
                return;
            }

            ReadFacadeNudge(true, Vec3.Zero);
            Camera camera = m_Camera.activeCamera;
            Vector3 eye = camera.transform.position;
            var position = new Vec3(point.X + m_RoofDx, point.Y, point.Z + m_RoofDz);
            Vec3 toEye = GroundOf(new Vector3(eye.x - position.X, 0f, eye.z - position.Z));
            float yaw = (toEye.LengthXZ < 1e-4f ? 0f : WallMount.FlatYaw(toEye)) + (m_Flip ? Mathf.PI : 0f) + m_Rotation;
            float lower = RoofLowerEdge;
            float top = lower + instance.Def.Height;
            float behind = TextRoofFrame.DepthFor(top, instance.Def.Width);
            float overhang = RoofOverhang(building, position, yaw, instance.Def.Width, behind);
            bool known = !float.IsNaN(overhang);
            bool valid = !known || overhang <= RoofFit.Tolerance;
            var records = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records : null;
            if (records != null && records.Count >= kMaxItems)
                valid = false;
            string note = m_WantText ? $"roof of building {building.Index}:{building.Version}, height {point.Y:0.0}, lower edge {lower:0.0} m, {(known ? $"overhang {overhang:0.00} m" : "the building's bounds are not known: only the hit point says where the roof is")}" : "";
            string decision = valid ? "fits" : known && overhang > RoofFit.Tolerance ? $"refused: overhang {overhang:0.00} m is over {RoofFit.Tolerance:0.0} m" : "refused: too many signs";
            if (m_RoofLogs < 30 && (click || decision != m_RoofLast))
            {
                m_RoofLogs++;
                Mod.log.Info($"Roof: {(click ? "clicked, " : "")}{decision}; building {building.Index}:{building.Version}, hit at ({point.X:0.0}, {point.Y:0.0}, {point.Z:0.0}), board {instance.Def.Width:0.0} x {instance.Def.Height:0.0} m, rear frame {behind:0.0} m deep, facing {yaw * 57.29578f:0} deg; {RoofBoundsText(building)}.");
            }

            m_RoofLast = decision;

            if (m_WantText)
                Debug = note + (valid ? "" : " [cannot place]");
            SetHint(valid ? "roof" : "roof-edge", 0, lower, overhang, 0f, false, false, chosen.Zh, !valid);
            ShowPreview(instance, chosen.Hash, lower, position, yaw);
            DrawOverlay(false, null, 0f, default, position, yaw, valid, def);
            if (!click)
                return;
            if (valid)
            {
                Place(def, spec, chosen.Hash, lower, position, yaw, Entity.Null, 0f, note);
                ResetFacade();
                m_Outcome = "placed on a roof: " + note;
            }
            else
            {
                m_Outcome = "refused: " + note;
            }
        }

        // ------------------------------------------------------------------ the item being edited

        /// <summary>
        /// The arrow keys, and the editor's four buttons (NudgeBoard), for a board that is on a building: along the facade (an arc where the text is bent: its radius is in its settings) and up and down
        /// its lower edge; on a roof across the roof as the camera sees it. Every board of the support moves with it.
        /// </summary>
        private void MoveOnFacade(TsItem item, SupportDef support)
        {
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            Camera camera = m_Camera.activeCamera;
            if (def == null || camera == null)
                return;
            Vec3 right = GroundOf(camera.transform.right), forward = GroundOf(camera.transform.forward);
            Vec3 position = RoadReader.ToVec(item.Position);
            float yaw = item.Yaw, reach = item.Reach;
            if (support.Roof)
            {
                (float x, float z) = FacadeMove.AcrossRoof(position.X, position.Z, right.X, right.Z, forward.X, forward.Z, m_ArrowX, -m_ArrowY, m_Shift);
                position = new Vec3(x, position.Y, z);
            }
            else
            {
                Vec3 normal = WallMount.NormalOf(support.Kind, item.Yaw, support.Side);
                float radius = 0f;
                if (support.Kind == SupportKind.WallFlat && def.Template != null && TextSupports.IsText(def))
                    radius = (float)def.Template.Parse(item.SpecText).Number("arc", 0.0);
                if (m_ArrowX != 0)
                {
                    Vec3 tangent = WallMount.Along(normal);
                    float along = m_ArrowX * FacadeMove.SenseOf(tangent.X, tangent.Z, right.X, right.Z) * FacadeMove.StepOf(m_Shift);
                    FacadePlace moved = FacadeMove.Along(position.X, position.Z, normal.X, normal.Z, radius, along);
                    position = new Vec3(moved.X, position.Y, moved.Z);
                    yaw += moved.Yaw - WallMount.FlatYaw(normal);
                }
            }

            // On a wall up and down move the lower edge; on a roof they are forward and back (above), and the editor's height buttons set the lower edge.
            if (m_ArrowY != 0 && !support.Roof)
            {
                float height = reach > 0f ? reach : def.BottomClearance;
                reach = FacadeMove.Height(height, -m_ArrowY * m_LiftStep, WallMount.MinHeight, WallHeight.Ceiling);
            }

            foreach (KeyValuePair<Entity, ItemRecord> member in MembersOf(EditEntity, item))
            {
                TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                each.Position = new float3(position.X, position.Y, position.Z);
                each.Yaw = yaw;
                each.Reach = reach;
                WriteItem(member.Key, each);
            }

            EditorRevision++;
            Mod.log.Info($"Edit: the sign {def.Id} on {(support.Roof ? "the roof" : "the facade")} moved by the arrow keys: lower edge {reach:0.0} m, at {position.X:0.0}, {position.Z:0.0}.");
        }
    }
}
