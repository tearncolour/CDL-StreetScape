using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// A board on another mod's signal arm (0.12.51, SupportKind.AttachedArm; the owner via the RIE session: "允许红绿灯竖杆和横杆上挂路牌"): the arm nearest to the cursor within HostPick.ArmReach
    /// (TsExtension.OtherHosts, HostFinder.ArmsAround), the board's middle where the cursor is along it, moved into a stretch the arm keeps free (HostArm.Free) and clear of the boards already on it; the
    /// board faces the way the arm's heads do (\ turns it to the arm's other side). The item stands on the arm's axis at the board's middle and keeps the arm's radius there as its reach; it goes with
    /// the arm when the other mod moves or turns the pole (0.12.106, ItemRoadSystem.OtherHosts), and an arm taken away leaves the board, which the editor says is without its arm.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>The room kept between two boards side by side on an arm (m).</summary>
        private const float kArmGap = 0.1f;

        /// <summary>The support chosen in the panel is a signal's arm.</summary>
        private static bool ArmChosen()
        {
            SupportDef support = SupportCatalog.ById(SelectedSupport);
            return support != null && SupportKinds.OnArm(support.Kind);
        }

        /// <summary>The stretches of an arm the boards already on it take (from, to in m from its root, with kArmGap each side), but the one of the item but (the board being moved).</summary>
        private List<(float From, float To)> TakenOnArm(HostArm arm, Entity but = default)
        {
            var taken = new List<(float From, float To)>();
            var records = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records : null;
            if (records == null)
                return taken;
            foreach (KeyValuePair<Entity, ItemRecord> pair in records)
            {
                ItemRecord record = pair.Value;
                SupportDef support = SupportCatalog.ByHash(record.SupportHash);
                if (support == null || !SupportKinds.OnArm(support.Kind) || pair.Key == but)
                    continue;
                float t = HostPick.AlongArm(arm, record.Position);
                SignDef def = SignCatalog.ByHash(record.TypeHash);
                if (float.IsNaN(t) || def == null)
                    continue;
                float half = SignInstances.Of(def, record.Spec).Def.Width * 0.5f + kArmGap;
                taken.Add((t - half, t + half));
            }

            return taken;
        }

        /// <summary>The steps of a board on an arm (m): along it, and up or down; with Shift the coarse ones.</summary>
        private const float kArmStep = 0.1f, kArmStepCoarse = 0.5f;

        /// <summary>
        /// The arrow keys and the editor's four buttons for the board on a signal's arm (0.12.57; the owner via the RIE session: "路牌在红绿灯支架上也应该支持左右位置和上下位置调整"): left and right
        /// (as the screen shows) along the arm to the next free place (HostPick.StepOnArm), up and down by kArmStep within the room the arm's heads leave above and below its axis (HostArm.Room,
        /// HostPick.DropRange), its place along the arm kept inside it. The board stays on the arm's axis; how far it is moved up or down is its OffsetX (+ up).
        /// </summary>
        private void MoveBoardOnArm(TsItem item)
        {
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            if (def == null)
                return;
            var at = new Vec3(item.Position.x, item.Position.y, item.Position.z);
            float sin = Mathf.Sin(item.Yaw), cos = Mathf.Cos(item.Yaw);
            HostArm arm = default;
            bool found = false;
            foreach (HostArm each in Hosts.ArmsAround(item.Position.x, item.Position.z, item.Position.y, World))
            {
                if (!float.IsNaN(HostPick.AlongArm(each, at)) && Math.Abs(each.DirX * sin + each.DirZ * cos) < 0.26f)
                {
                    arm = each;
                    found = true;
                    break;
                }
            }

            if (!found)
                return;
            SignInstance instance = SignInstances.Of(def, item.Spec.Length > 0 ? item.Spec.ToString() : null);
            float width = instance.Def.Width, height = instance.Def.Height, extra = instance.Def.ExtraTop;
            float along = HostPick.AlongArm(arm, at), dy = item.OffsetX;
            if (m_ArrowX != 0)
            {
                // The screen's left and right as the sign's X (+ the viewer's left), and the arm's own way in it.
                float armX = arm.DirX * cos - arm.DirZ * sin;
                int direction = BeamDirection(item.Yaw) * (armX >= 0f ? 1 : -1);
                if (direction == 0)
                    return;
                arm.Free = HostPick.WithoutTaken(arm.Free != null && arm.Free.Length >= 2 ? arm.Free : new[] { 0.1f, arm.Length - 0.1f }, TakenOnArm(arm, EditEntity));
                float moved = HostPick.StepOnArm(arm, along, width, direction, m_Shift ? kArmStepCoarse : kArmStep);
                if (float.IsNaN(moved))
                    return;
                along = Mathf.Round(moved * 100f) / 100f;
            }
            else
            {
                dy -= m_ArrowY * (m_Shift ? kArmStepCoarse : kArmStep);
            }

            // Where it is now the room is that of its place (restated for the place it moves to), its up or down kept within it.
            HostPick.RoomAt(arm, along, out float below, out float above);
            HostPick.DropRange(height, extra, below, above, out float lowest, out float highest);
            dy = Mathf.Round(Mathf.Clamp(dy, lowest, highest) * 100f) / 100f;
            var position = new float3(arm.X + arm.DirX * along, arm.Y, arm.Z + arm.DirZ * along);
            float reach = Mathf.Round(arm.RadiusAt(along) * 1000f) / 1000f;
            if (position.Equals(item.Position) && Mathf.Abs(dy - item.OffsetX) < 1e-4f)
                return;
            item.Position = position;
            item.OffsetX = dy;
            item.Reach = reach;
            WriteItem(EditEntity, item);
            EditorRevision++;
            Mod.log.Info($"Edit: the board on arm #{arm.Id} is {along:0.00} m along it, {dy:+0.00;-0.00;0.00} m up (room {-below:0.00} to {above:0.00} m under and over the arm's axis).");
        }

        /// <summary>
        /// Place mode with a signal's arm chosen: the board on the arm near the cursor (or the reason it cannot go there: too large for an arm, no arm near, no room on it), its preview, the line of help,
        /// and a click places it.
        /// </summary>
        private void UpdatePlaceOnArm(SignChoice choice, SupportDef chosen, bool click)
        {
            SignDef def = choice.Def;
            m_NearPosts.Clear();
            if (JunctionFill.CanRead(def.Id))
                SetReadNote(null);
            if (!GetRaycastResult(out Entity owner, out RaycastHit hit))
            {
                HidePreview();
                SetHint("arm-none", 0, HostPick.ArmReach, 0f, 0f, false, false, chosen.Zh, true);
                m_Outcome = "ignored: the raycast found nothing under the cursor";
                return;
            }

            // The size band of a sign that says "auto" is the road's under the cursor (the arm is over it).
            Unity.Mathematics.float3 cursor = hit.m_HitPosition;
            float kmh = 0f;
            bool hasSpeed = false, highway = false;
            if (RoadReader.TryResolveEdge(EntityManager, owner, hit.m_HitEntity, cursor, out Entity edge))
            {
                hasSpeed = EdgeInfo.Of(EntityManager, edge).TrySpeed(EntityManager, out kmh, out RoadReader.SpeedReport report);
                highway = hasSpeed && report.Highway;
            }

            string spec = GuideTemplates.ResolveSpeed(def, Draft, hasSpeed ? kmh : 0f, hasSpeed, highway);
            SignInstance instance = SignInstances.Of(def, spec);
            if (!SupportPlanner.FitsArm(instance.Def))
            {
                HidePreview();
                SetHint("arm-large", 0, SupportPlanner.MaxArmWidth, SupportPlanner.MaxArmHeight, 0f, false, false, chosen.Zh, true);
                m_Outcome = $"refused: {def.Id} ({instance.Def.Width:0.0} x {instance.Def.Height:0.0} m) is too large for a signal arm";
                return;
            }

            IList<HostArm> arms = Hosts.ArmsAround(cursor.x, cursor.z, cursor.y, World);
            int pick = HostPick.NearestArm(arms, cursor.x, cursor.z, HostPick.ArmReach, out float along, out float distance);
            if (pick < 0)
            {
                HidePreview();
                SetHint("arm-none", 0, HostPick.ArmReach, 0f, 0f, false, false, chosen.Zh, true);
                m_Outcome = $"ignored: no signal arm within {HostPick.ArmReach:0} m of the cursor ({arms.Count} arm(s) near)";
                return;
            }

            HostArm arm = arms[pick];
            float width = instance.Def.Width;
            arm.Free = HostPick.WithoutTaken(arm.Free != null && arm.Free.Length >= 2 ? arm.Free : new[] { 0.1f, arm.Length - 0.1f }, TakenOnArm(arm));
            float at = HostPick.PlaceOnArm(arm, along, width);
            if (float.IsNaN(at))
            {
                HidePreview();
                SetHint("arm-space", 0, width, 0f, 0f, false, false, chosen.Zh, true);
                m_Outcome = $"refused: no stretch of arm #{arm.Id} holds a board {width:0.0} m wide";
                return;
            }

            var position = new Vec3(arm.X + arm.DirX * at, arm.Y, arm.Z + arm.DirZ * at);
            float yaw = Mathf.Atan2(arm.FaceX, arm.FaceZ) + (m_Flip ? Mathf.PI : 0f);
            float reach = Mathf.Round(arm.RadiusAt(at) * 1000f) / 1000f;
            bool valid = true;
            string note = m_WantText ? $"on arm #{arm.Id} of another mod, {at:0.00} m from its root (cursor {along:0.00} m, {distance:0.00} m off it), arm radius {reach:0.000} m{(m_Flip ? ", on its back" : "")}" : "";
            var records = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records : null;
            if (records != null && records.Count >= kMaxItems)
            {
                valid = false;
                if (m_WantText)
                    note += "; too many signs";
            }

            if (m_WantText)
                Debug = note + (valid ? "" : " [cannot place]");
            SetHint("arm", 0, at, 0f, 0f, false, false, chosen.Zh, !valid);
            ShowPreview(instance, chosen.Hash, reach, position, yaw);
            DrawOverlay(false, null, 0f, default, position, yaw, valid, def);
            if (!click)
                return;
            if (valid)
            {
                Place(def, spec, chosen.Hash, reach, position, yaw, Entity.Null, 0f, note);
                m_Outcome = "placed on a signal arm: " + note;
            }
            else
            {
                m_Outcome = "refused: " + note;
            }
        }
    }
}
