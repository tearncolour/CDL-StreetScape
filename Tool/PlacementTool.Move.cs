using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CDLC.TS
{
    /// <summary>
    /// Changing a support that stands (0.12.1): moving it along its road (it follows the road's curve and turns with it) or toward or away from the road, and setting the span of a gantry or the arm of a
    /// cantilever from the real road (Carriageway: a gantry from the kerb on one side to the kerb on the other, an arm over the middle of the lanes of its traffic) or by steps. Every board of the support
    /// moves with it. The editor's buttons call these.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>The steps of the move buttons (m): 1 and 3 along the road (0.5 and 5 m, positive toward the traffic that sees the sign), 2 and 4 across (0.1 and 1 m, positive away from the road).</summary>
        internal static void MoveStep(int code, out float along, out float across)
        {
            along = code == 1 ? 0.5f : code == -1 ? -0.5f : code == 3 ? 5f : code == -3 ? -5f : 0f;
            across = code == 2 ? 0.1f : code == -2 ? -0.1f : code == 4 ? 1f : code == -4 ? -1f : code == 6 ? WallHeight.CoarseButton : code == -6 ? -WallHeight.CoarseButton : 0f;
        }

        /// <summary>The code of the editor's button that turns a wall bracket's board to face the other way along the wall (0.12.2).</summary>
        internal const int TurnOnWall = 5;

        /// <summary>
        /// Moves the support being edited by one of the steps of MoveStep. On a wall (0.12.2) "along" is along the wall (+ to the right of one who faces the wall) and "across" is up and down (+ up: the
        /// height of the lower edge, between WallMount.MinHeight and WallHeight.Ceiling); TurnOnWall turns a bracket's board to face the other way along the wall.
        /// </summary>
        internal void MoveSupport(int code)
        {
            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            if (def == null || def.IsMarking)
                return;
            SupportDef support = SupportCatalog.ByHash(item.SupportHash);
            if (support != null && SupportKinds.OnWall(support.Kind))
            {
                MoveOnWall(item, def, support, code);
                return;
            }

            MoveStep(code, out float along, out float across);
            MoveAlongAcross(item, def, along, across);
        }

        /// <summary>Moves the support (every board of it) along its road by along and away from the road's centre line by across (m); the road's own curve if it can be read, else the sign's own frame.</summary>
        private void MoveAlongAcross(TsItem item, SignDef def, float along, float across)
        {
            if (along == 0f && across == 0f)
                return;

            Vec3 position = RoadReader.ToVec(item.Position);
            float yaw = item.Yaw;
            float edgeT = item.EdgeT;
            var facing = new Vec3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            EdgeInfo info = RoadReader.IsLiveEdge(EntityManager, item.Edge) ? EdgeInfo.Of(EntityManager, item.Edge) : null;
            if (info != null && info.CurveOk)
            {
                // Along the road's own curve, at the same distance from its centre line and the same height over it; the support turns as the road does.
                Bezier3 curve = info.Curve;
                ArcTable arc = info.Arc;
                float t = curve.ClosestT(position);
                Vec3 on = curve.Point(t);
                Vec3 heading = curve.Tangent(t).HeadingXZ();
                float lateral = Vec3.Dot(position - on, heading.RightXZ());
                float height = position.Y - on.Y;
                float sense = SupportShift.Sense(yaw, heading.X, heading.Z);
                float s = Mathf.Clamp(arc.SAt(t) + sense * along, 0f, arc.Length);
                float lateral2 = lateral + (lateral >= 0f ? 1f : -1f) * across;
                float t2 = arc.TAt(s);
                Vec3 on2 = curve.Point(t2);
                Vec3 heading2 = curve.Tangent(t2).HeadingXZ();
                Vec3 moved = on2 + heading2.RightXZ() * lateral2;
                position = new Vec3(moved.X, on2.Y + height, moved.Z);
                yaw += EdgeSnap.YawOf(heading2) - EdgeSnap.YawOf(heading);
                edgeT = t2;
            }
            else
            {
                // Off a road: in the sign's own frame (its +X points to the driver's left, toward the road for a sign at the right side).
                var x = new Vec3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
                position = position + facing * along - x * across;
            }

            foreach (KeyValuePair<Entity, ItemRecord> member in MembersOf(EditEntity, item))
            {
                TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                each.Position = new float3(position.X, position.Y, position.Z);
                each.Yaw = yaw;
                each.EdgeT = edgeT;
                WriteItem(member.Key, each);
            }

            EditorRevision++;
            Mod.log.Info($"Edit: moved the support of {def.Id} {along:+0.0;-0.0} m along the road and {across:+0.0;-0.0} m across it, to {position}.");
        }

        /// <summary>
        /// The margin the edited support stands at (the one placing would have used, KerbMargin.Of), for a post on a road's side; false for any other support, for a post on a strip between the carriageways
        /// (the margin it works out to is out of range) and for a road that cannot be read.
        /// </summary>
        internal bool TryEditedKerb(out float margin)
        {
            margin = float.NaN;
            return Mode == 2 && TryReadEdit(out TsItem item) && TryKerbOf(item, out margin);
        }

        /// <summary>The margin of a placed post (what TryEditedKerb says of the support being edited), also for the one the eyedropper picked.</summary>
        private bool TryKerbOf(TsItem item, out float margin)
        {
            margin = float.NaN;
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            SupportDef support = SupportCatalog.ByHash(item.SupportHash);
            if (def == null || def.IsMarking || support == null || !KerbMargin.Edits(support.Kind))
                return false;
            EdgeInfo info = RoadReader.IsLiveEdge(EntityManager, item.Edge) ? EdgeInfo.Of(EntityManager, item.Edge) : null;
            if (info == null || !info.CurveOk)
                return false;
            Vec3 position = RoadReader.ToVec(item.Position);
            float t = info.Curve.ClosestT(position);
            float lateral = Vec3.Dot(position - info.Curve.Point(t), info.Curve.Tangent(t).HeadingXZ().RightXZ());
            SupportLayout layout = SupportLayouts.Of(def, support);
            float reachOut = Mathf.Max(0f, layout.InnerReach - kReferenceReach);
            float atDefault = info.Carriageway(EntityManager).PostDistance(lateral >= 0f ? 1 : -1, layout.InnerReach, Carriageway.DefaultMargin, reachOut);
            margin = KerbMargin.Of(lateral, atDefault);
            return !float.IsNaN(margin);
        }

        /// <summary>
        /// The slider and the box of the editor: the edited post (of a post, an arm or a gantry) moved across its road to stand at this margin (one undo step per call, as the move buttons). A gantry whose
        /// span the player has not set takes the span placing would have given it at that margin: its far post moves out with the near one.
        /// </summary>
        internal void SetEditedKerb(float target)
        {
            if (!TryEditedKerb(out float current) || !TryReadEdit(out TsItem item))
                return;
            float across = KerbMargin.Across(target, current);
            if (Mathf.Abs(across) < 0.005f)
                return;
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            MoveAlongAcross(item, def, 0f, across);
            SupportDef support = SupportCatalog.ByHash(item.SupportHash);
            if (support.Kind == SupportKind.Gantry && !item.ReachByHand && TryReadEdit(out TsItem moved))
                RefitSpanToMargin(moved, SupportLayouts.Of(def, support), KerbMargin.Clamp(target));
        }

        /// <summary>The span of the gantry the item is on as placing would have made it at this margin, kept as its own length (BaseReach) so that the boards it carries still make it longer when they need it.</summary>
        private void RefitSpanToMargin(TsItem item, SupportLayout layout, float margin)
        {
            EdgeInfo info = RoadReader.IsLiveEdge(EntityManager, item.Edge) ? EdgeInfo.Of(EntityManager, item.Edge) : null;
            if (info == null || !info.CurveOk)
                return;
            Vec3 position = RoadReader.ToVec(item.Position);
            float t = info.Curve.ClosestT(position);
            float lateral = Vec3.Dot(position - info.Curve.Point(t), info.Curve.Tangent(t).HeadingXZ().RightXZ());
            float reachOut = Mathf.Max(0f, layout.InnerReach - kReferenceReach);
            float span = SupportShift.SpanAtMargin(info.Carriageway(EntityManager), lateral >= 0f ? 1 : -1, layout.InnerReach, margin, reachOut);
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            foreach (KeyValuePair<Entity, ItemRecord> member in members)
            {
                TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                each.BaseReach = span;
                each.Flags |= TsItem.FlagBaseReach;
                WriteItem(member.Key, each);
            }

            RefitReach(EntitiesOf(members, Entity.Null), "moved to the margin");
        }

        /// <summary>The entity being edited when the position along was last read, and where it stood then: the sliders and the boxes of the position along count from there (0.12.85).</summary>
        private Entity m_ShiftEntity;

        private float m_ShiftArc;
        private Vec3 m_ShiftAt;

        /// <summary>The place the edited support stood at when it was picked: noted the first time it is read, and let go when the edit ends or another support is picked.</summary>
        private void NoteShiftStart(Vec3 position, float arc)
        {
            if (m_ShiftEntity == EditEntity)
                return;
            m_ShiftEntity = EditEntity;
            m_ShiftArc = arc;
            m_ShiftAt = position;
        }

        /// <summary>Whether the sign of an edited item is a board of a support (not a marking) and the support is known; the support's kind is what decides the controls.</summary>
        private static bool EditedSupport(in TsItem item, out SignDef def, out SupportDef support)
        {
            def = SignCatalog.ByHash(item.TypeHash);
            support = SupportCatalog.ByHash(item.SupportHash);
            return def != null && !def.IsMarking && support != null;
        }

        /// <summary>
        /// The position of the edited support along its road (m from where it stood when it was picked, + toward the traffic that sees it, as the buttons count) or along its wall (+ to the right facing the
        /// wall); false for a support these controls do not move (SupportShift.Along) and for a road that cannot be read.
        /// </summary>
        internal bool TryEditedAlong(out float shown)
        {
            shown = float.NaN;
            if (Mode != 2 || !TryReadEdit(out TsItem item) || !EditedSupport(item, out _, out SupportDef support) || !SupportShift.Along(support.Kind))
                return false;
            Vec3 position = RoadReader.ToVec(item.Position);
            if (SupportKinds.OnWall(support.Kind))
            {
                NoteShiftStart(position, 0f);
                shown = SupportShift.Displacement(position, m_ShiftAt, WallMount.Along(WallMount.NormalOf(support.Kind, item.Yaw, support.Side)));
                return true;
            }

            EdgeInfo info = RoadReader.IsLiveEdge(EntityManager, item.Edge) ? EdgeInfo.Of(EntityManager, item.Edge) : null;
            if (info == null || !info.CurveOk)
                return false;
            float t = info.Curve.ClosestT(position);
            float arc = info.Arc.SAt(t);
            NoteShiftStart(position, arc);
            Vec3 heading = info.Curve.Tangent(t).HeadingXZ();
            shown = SupportShift.Displacement(arc, m_ShiftArc, SupportShift.Sense(item.Yaw, heading.X, heading.Z));
            return true;
        }

        /// <summary>The slider and the box of the position along: the edited support moved along its road or its wall to this position (one undo step per call; the writes within two seconds are one).</summary>
        internal void SetEditedAlong(float target)
        {
            if (!TryEditedAlong(out float shown) || !TryReadEdit(out TsItem item) || !EditedSupport(item, out SignDef def, out SupportDef support))
                return;
            float along = SupportShift.AlongDelta(target, shown);
            if (Mathf.Abs(along) < 0.005f)
                return;
            if (SupportKinds.OnWall(support.Kind))
                MoveOnWallBy(item, def, support, along, 0f);
            else
                MoveAlongAcross(item, def, along, 0f);
        }

        /// <summary>The distance of the edited support from its road's centre line (m) and the road's half width (the slider goes SupportShift.CentrePast beyond it), for a support the margin does not describe (SupportShift.Across); false for any other.</summary>
        internal bool TryEditedCentre(out float distance, out float roadHalf)
        {
            distance = roadHalf = float.NaN;
            if (Mode != 2 || !TryReadEdit(out TsItem item) || !EditedSupport(item, out _, out SupportDef support) || SupportShift.Across(support.Kind, TryEditedKerb(out _)) != ShiftAcross.Centre)
                return false;
            EdgeInfo info = RoadReader.IsLiveEdge(EntityManager, item.Edge) ? EdgeInfo.Of(EntityManager, item.Edge) : null;
            if (info == null || !info.CurveOk)
                return false;
            Vec3 position = RoadReader.ToVec(item.Position);
            float t = info.Curve.ClosestT(position);
            distance = Mathf.Abs(Vec3.Dot(position - info.Curve.Point(t), info.Curve.Tangent(t).HeadingXZ().RightXZ()));
            roadHalf = info.Width * 0.5f;
            return true;
        }

        /// <summary>The slider and the box of the distance from the centre line: the edited support moved across its road to stand at this distance, on the side it stands on.</summary>
        internal void SetEditedCentre(float target)
        {
            if (!TryEditedCentre(out float distance, out float roadHalf) || !TryReadEdit(out TsItem item))
                return;
            float across = SupportShift.CentreDelta(target, distance, roadHalf);
            if (Mathf.Abs(across) < 0.005f)
                return;
            MoveAlongAcross(item, SignCatalog.ByHash(item.TypeHash), 0f, across);
        }

        /// <summary>The height of the lower edge of the board on a wall being edited (m), for the editor's box and slider; false for any other support.</summary>
        internal bool TryEditedWallHeight(out float lower)
        {
            lower = float.NaN;
            if (Mode != 2 || !TryReadEdit(out TsItem item) || !EditedSupport(item, out SignDef def, out SupportDef support) || !SupportShift.Height(support.Kind))
                return false;
            lower = item.Reach > 0f ? item.Reach : def.BottomClearance;
            return true;
        }

        /// <summary>The slider and the box of the lower edge of a board on a wall: moved up or down to this height, between the least and the most a board is put at.</summary>
        internal void SetEditedWallHeight(float target)
        {
            if (!TryEditedWallHeight(out float lower) || !TryReadEdit(out TsItem item) || !EditedSupport(item, out SignDef def, out SupportDef support))
                return;
            float across = SupportShift.HeightDelta(target, lower);
            if (Mathf.Abs(across) < 0.005f)
                return;
            MoveOnWallBy(item, def, support, 0f, across);
        }

        /// <summary>The < and > keys in edit mode: one step (direction +1 away from the road, -1 toward it).</summary>
        private void StepEditedKerb(int direction)
        {
            if (TryEditedKerb(out float current))
                SetEditedKerb(KerbMargin.Stepped(current, direction));
        }

        /// <summary>A step of the editor's buttons for a sign on a wall: along it, up or down, or (a bracket) turned to face the other way. Every board of the support moves with it.</summary>
        private void MoveOnWall(TsItem item, SignDef def, SupportDef support, int code)
        {
            if (code != TurnOnWall)
            {
                MoveStep(code, out float along, out float across);
                MoveOnWallBy(item, def, support, along, across);
                return;
            }

            if (support.Kind != SupportKind.WallBracket)
                return;
            float yaw = WallMount.Convert(support.Kind, support.Side, item.Yaw, support.Kind, -WallMount.FacingOf(item.Yaw, support.Side), out int side);
            SupportDef turned = (side < 0) != (support.Side < 0) ? SupportCatalog.ById(support.MirrorId) : support;
            if (turned == null)
                return;
            WriteOnWall(item, def, RoadReader.ToVec(item.Position), yaw, item.Reach, turned.Hash, "turned to face the other way along the wall");
        }

        /// <summary>A board on a wall moved along the wall by along (m, + to the right facing it) and its lower edge by across (m, + up), as the buttons, the sliders and the boxes ask.</summary>
        private void MoveOnWallBy(TsItem item, SignDef def, SupportDef support, float along, float across)
        {
            if (along == 0f && across == 0f)
                return;
            float reach = item.Reach;
            Vec3 position = RoadReader.ToVec(item.Position) + WallMount.Along(WallMount.NormalOf(support.Kind, item.Yaw, support.Side)) * along;
            if (across != 0f)
            {
                float height = reach > 0f ? reach : def.BottomClearance;
                reach = Mathf.Clamp(Mathf.Round((height + across) * 10f) / 10f, WallMount.MinHeight, WallHeight.Ceiling);
            }

            WriteOnWall(item, def, position, item.Yaw, reach, item.SupportHash, $"moved {along:+0.0;-0.0} m along the wall, lower edge {reach:0.0} m");
        }

        /// <summary>Writes the place, the facing, the lower edge and the support of a board on a wall to every board of its support.</summary>
        private void WriteOnWall(TsItem item, SignDef def, Vec3 position, float yaw, float reach, uint hash, string what)
        {
            foreach (KeyValuePair<Entity, ItemRecord> member in MembersOf(EditEntity, item))
            {
                TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                each.Position = new float3(position.X, position.Y, position.Z);
                each.Yaw = yaw;
                each.Reach = reach;
                each.SupportHash = hash;
                WriteItem(member.Key, each);
            }

            EditorRevision++;
            Mod.log.Info($"Edit: the sign {def.Id} on the wall {what}.");
        }

        /// <summary>
        /// The span or arm a support has from the real road where it stands: a gantry from its post to one as far out beyond the kerb on the other side; a cantilever's arm over the middle of the lanes of the
        /// traffic that sees it. 0 when the road cannot be read, or for a double cantilever (its post stands in the traffic).
        /// </summary>
        private float FittedReach(TsItem item, EditorView view) => FittedReach(item, view.Support, view.Layout, RecordsOf(MembersOf(EditEntity, item)));

        /// <summary>As above, for a support and its layout, with the records of its boards (for the width the arm must carry over the lanes).</summary>
        private float FittedReach(TsItem item, SupportDef support, SupportLayout layout, List<ItemRecord> records)
        {
            EdgeInfo info = RoadReader.IsLiveEdge(EntityManager, item.Edge) ? EdgeInfo.Of(EntityManager, item.Edge) : null;
            if (info == null || !info.CurveOk)
                return 0f;
            Vec3 position = RoadReader.ToVec(item.Position);
            float t = info.Curve.ClosestT(position);
            Vec3 on = info.Curve.Point(t);
            float lateral = Vec3.Dot(position - on, info.Curve.Tangent(t).HeadingXZ().RightXZ());
            int side = lateral >= 0f ? 1 : -1;
            Carriageway carriageway = info.Carriageway(EntityManager);
            if (support.Kind == SupportKind.Gantry)
            {
                float far = carriageway.PostDistance(-side, layout.InnerReach, Carriageway.DefaultMargin, 0f);
                return Mathf.Round(Mathf.Clamp(Mathf.Abs(lateral) + far, SupportLayouts.MinSpan, SupportLayouts.MaxSpan) * 10f) / 10f;
            }

            if (SupportKinds.OneWayArm(support.Kind) && info.TryLaterals(EntityManager, out List<float> forward, out List<float> backward))
            {
                AssemblyPlan plan = ItemGroups.PlanOf(records, out List<AssemblyBoard> ordered);
                float width = plan != null && ordered.Count > 0 ? Assembly.TotalDef(ordered, true).Width : SignCatalog.ByHash(item.TypeHash).Width;
                // Over the lanes of the traffic the sign faces on the side the arm reaches to (a cantilever on a median, 0.12.25); the roadside's lanes when there are none.
                Vec3 heading = info.Curve.Tangent(t).HeadingXZ();
                float sin = Mathf.Sin(item.Yaw), cos = Mathf.Cos(item.Yaw);
                bool along = sin * heading.X + cos * heading.Z < 0f;
                int toward = Vec3.Dot(new Vec3(cos, 0f, -sin) * layout.ArmSide, heading.RightXZ()) >= 0f ? 1 : -1;
                List<float> over = StripPost.Beyond(along ? forward : backward, lateral, toward);
                return Carriageway.CantileverArm(lateral, over.Count > 0 ? over : side > 0 ? forward : backward, width);
            }

            return 0f;
        }

        /// <summary>The entities of the members of a support, but one (Entity.Null: all of them).</summary>
        private static List<Entity> EntitiesOf(List<KeyValuePair<Entity, ItemRecord>> members, Entity but)
        {
            var list = new List<Entity>(members.Count);
            foreach (KeyValuePair<Entity, ItemRecord> member in members)
            {
                if (member.Key != but)
                    list.Add(member.Key);
            }

            return list;
        }

        /// <summary>
        /// After a board of a hung support was taken off, moved or changed (the owner: "悬架原本的路牌删除或者移动之后，长度应该能自动缩小", and in 0.12.11 "挪动路牌悬臂长度没变"):
        /// - an arm (cantilever, double cantilever) is as long as the boards need where they are now, in half metres (0.12.12; 0.12.9 to 0.12.11 kept the length it was placed with as the least, so a board
        ///   moved in left the arm as it was). A length the player set in the editor (FlagReachByHand) is kept as the least instead. When no board has a place of its own the arm stays as it is.
        /// - a gantry keeps its own span (BaseReach, from the road when it was placed; for an item of 0.12.8 or before the road's span now, FittedReach) as the least: its posts stand by the kerbs.
        /// </summary>
        private void RefitReach(List<Entity> entities, string why)
        {
            var members = new List<KeyValuePair<Entity, ItemRecord>>();
            foreach (Entity entity in entities)
            {
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<TsItem>(entity))
                    members.Add(new KeyValuePair<Entity, ItemRecord>(entity, ChunkSystem.ToRecord(EntityManager.GetComponentData<TsItem>(entity))));
            }

            if (members.Count == 0)
                return;
            members = ItemGroups.Sorted(members, Comparer<Entity>.Default);
            TsItem lead = EntityManager.GetComponentData<TsItem>(members[0].Key);
            List<ItemRecord> records = RecordsOf(members);
            AssemblyPlan plan = ItemGroups.PlanOf(records, out List<AssemblyBoard> ordered);
            if (plan == null || !plan.Hung)
                return;
            bool gantry = plan.Support.Kind == SupportKind.Gantry, known = lead.HasBaseReach;
            float reach, own;
            if (gantry || lead.ReachByHand)
            {
                own = known ? lead.BaseReach : FittedReach(lead, plan.Support, plan.Layout, records);
                if (!known && own <= 0f)
                    return;
                reach = Assembly.ReachFor(ordered, plan.Support, own);
            }
            else
            {
                float need = Assembly.RequiredReach(ordered, plan.Support.Kind);
                if (need <= 0f)
                    return;
                own = 0f;
                reach = Mathf.Min(SupportLayouts.MaxSpan, Mathf.Ceil(need * 2f) / 2f);
            }

            if (Mathf.Abs(reach - lead.Reach) < 1e-3f)
                return;
            float before = plan.Layout.ArmLength;
            foreach (KeyValuePair<Entity, ItemRecord> member in members)
            {
                TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                each.Reach = reach;
                if (gantry || lead.ReachByHand)
                {
                    each.BaseReach = own;
                    each.Flags |= TsItem.FlagBaseReach;
                }

                WriteItem(member.Key, each);
            }

            EditorRevision++;
            float after = Assembly.Plan(ordered, plan.Support, reach).Layout.ArmLength;
            Mod.log.Info($"Edit: {why}: the {(gantry ? "span" : "arm")} of the support is {after:0.0} m (was {before:0.0} m; {(gantry || lead.ReachByHand ? $"its own {own:0.0} m{(known ? "" : ", read from the road")}" : "as long as its boards need")}).");
        }
    }
}
