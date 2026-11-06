using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Game.Net;
using Unity.Entities;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// A marking that runs along a road, drawn inside a junction (0.12.0): with the cursor on a junction the line follows one of the junction's car lanes (the game's curve of it, its sub-lanes with NodeLane)
    /// instead of a road segment: the lane nearest the cursor, and of it the left side, the middle or the right side, whichever is nearest. Both clicks are taken on that lane's curve, so a junction guide line
    /// (线8) follows the turn the traffic makes. The line is cut into items as on a road (MarkingRuns). Read only.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private bool m_RunOnLane;
        private Entity m_RunLane;
        private ArcTable m_RunLaneArc;

        /// <summary>The node under the cursor (the raycast's owner or entity), or none.</summary>
        private Entity NodeUnder(Entity owner, Entity hitEntity)
        {
            if (owner != Entity.Null && EntityManager.HasComponent<Node>(owner) && EntityManager.HasBuffer<Game.Net.SubLane>(owner))
                return owner;
            if (hitEntity != Entity.Null && EntityManager.HasComponent<Node>(hitEntity) && EntityManager.HasBuffer<Game.Net.SubLane>(hitEntity))
                return hitEntity;
            return Entity.Null;
        }

        /// <summary>How far (m) before a junction, on the road leading to it, a junction line can be started: it takes the junction's lane that begins there (0.12.43).</summary>
        private const float kJunctionReach = 3f;

        /// <summary>
        /// The junction a line that follows a junction's lanes is started in: the node under the cursor (the raycast's owner when it is on a junction's part), or the junction at the end of the road
        /// under the cursor when the cursor is within kJunctionReach of where the road's lanes end there; none otherwise.
        /// </summary>
        private Entity JunctionAt(Entity owner, Entity hitEntity, Entity edge, EdgeInfo info, bool onRoad, Vec3 cursor)
        {
            Entity under = NodeUnder(owner, hitEntity);
            if (under != Entity.Null)
                return under;

            if (!onRoad || !RoadReader.TryDrawnSpan(EntityManager, edge, info.Arc, out float s0, out float s1) || !EntityManager.TryGetComponent(edge, out Edge ends))
                return Entity.Null;
            float s = info.Arc.SAt(info.Curve.ClosestT(cursor));
            Entity node = s < s0 + kJunctionReach ? ends.m_Start : s > s1 - kJunctionReach ? ends.m_End : Entity.Null;
            return node != Entity.Null && RoadReader.IsJunction(EntityManager, node) && EntityManager.HasBuffer<Game.Net.SubLane>(node) ? node : Entity.Null;
        }

        private static Bezier3 Bezier(Curve c) => new Bezier3(RoadReader.ToVec(c.m_Bezier.a), RoadReader.ToVec(c.m_Bezier.b), RoadReader.ToVec(c.m_Bezier.c), RoadReader.ToVec(c.m_Bezier.d));

        /// <summary>The car lane of a node nearest a point, its curve and width; false when the node has none.</summary>
        private bool NearestNodeLane(Entity node, Vec3 at, out Entity best, out Bezier3 curve, out float width)
        {
            best = Entity.Null;
            curve = default;
            width = MarkingSnap.DefaultLane;
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<Game.Net.SubLane> lanes))
                return false;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < lanes.Length; i++)
            {
                Entity lane = lanes[i].m_SubLane;
                if (lane == Entity.Null || !EntityManager.HasComponent<Game.Net.CarLane>(lane) || EntityManager.HasComponent<MasterLane>(lane) || !EntityManager.TryGetComponent(lane, out Curve c) || c.m_Length < 1f)
                    continue;
                Bezier3 b = Bezier(c);
                float d = Vec3.DistanceXZ(b.Point(b.ClosestT(at)), at);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = lane;
                    curve = b;
                }
            }

            if (best == Entity.Null)
                return false;
            width = WaitingReader.LaneWidth(EntityManager, best);
            return true;
        }

        /// <summary>A line along a junction's lane: the first click picks the lane and the side, the second the end; true when this handled the frame.</summary>
        private bool UpdateLaneRun(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity node)
        {
            if (!m_RunActive)
            {
                if (node == Entity.Null || !NearestNodeLane(node, cursorV, out Entity lane, out Bezier3 curve, out float width))
                    return false;
                var arc = new ArcTable(curve);
                float t = curve.ClosestT(cursorV);
                Vec3 on = curve.Point(t);
                float lateral = Vec3.Dot(cursorV - on, curve.Tangent(t).HeadingXZ().RightXZ());
                float place = 0f;
                foreach (float candidate in new[] { -width * 0.5f, 0f, width * 0.5f })
                {
                    if (Math.Abs(candidate - lateral) < Math.Abs(place - lateral))
                        place = candidate;
                }

                float s = arc.SAt(t);
                float surface = hit.m_HitPosition.y - hit.m_Position.y;
                if (m_WantText)
                    Debug = $"line start in a junction: lane {lane.Index}:{lane.Version} ({arc.Length:0.0} m, {width:0.00} m wide), {s:0.0} m along it, {(place < 0f ? "its left side" : place > 0f ? "its right side" : "its middle")}";
                SetHint("mark-run-start");
                HidePreview();
                List<Vec3> stub = MarkingRuns.Line(arc, Mathf.Max(0f, s - 6f), Mathf.Min(arc.Length, s + 6f), place, surface);
                DrawMarkingOverlay(PointOn(arc, s, place, surface), 0f, true, stub);
                if (click)
                {
                    m_RunActive = true;
                    m_RunOnLane = true;
                    m_RunLane = lane;
                    m_RunLaneArc = arc;
                    m_RunEdge = Entity.Null;
                    m_RunStart = SnapToEnds(arc, s);
                    m_RunLateral = place;
                    m_RunSurface = surface;
                    m_RunKey = "";
                    m_Outcome = $"line started on junction lane {lane.Index}:{lane.Version} at {m_RunStart:0.0} m, {place:+0.00;-0.00} m from its middle";
                    Mod.log.Info("Tool: " + m_Outcome + ".");
                }

                return true;
            }

            if (!m_RunOnLane)
                return false;
            if (m_RunLane == Entity.Null || !EntityManager.Exists(m_RunLane) || m_RunLaneArc == null)
            {
                Mod.log.Info("Tool: the junction lane of the line that was started is gone; the line is cancelled.");
                ResetRun();
                HidePreview();
                m_Outcome = "the junction lane of the line is gone";
                return true;
            }

            ArcTable laneArc = m_RunLaneArc;
            float end = SnapToEnds(laneArc, MarkingRuns.ProjectS(laneArc, cursorV));
            float endStep = Mathf.Round(end * 2f) * 0.5f;
            string spec = MarkingSpec(template, null, false);
            string key = $"lane {m_RunLane.Index}:{m_RunLane.Version}|{m_RunStart:0.00}|{endStep:0.0}|{m_RunLateral:0.00}|{m_RunSurface:0.000}|{spec}|{MarkingLetters.Version}";
            if (key != m_RunKey)
            {
                m_RunKey = key;
                m_RunLine = MarkingRuns.Line(laneArc, m_RunStart, end, m_RunLateral, m_RunSurface);
                m_RunDraw.Clear();
                m_RunDraw.Add(OverlayLine.Simplify(m_RunLine));
                m_RunItems = MarkingRuns.Items(m_RunLine, MarkingTemplates.PathBudget(template, spec));
                m_RunLength = Mathf.Abs(end - m_RunStart);
                m_RunTotal = WaitingArea.Length(m_RunLine);
                DestroyPreview();
                if (m_RunItems.Count > 0 && m_RunItems.Count <= kRunPreviewItems)
                {
                    BuildRunPreview(def, template, spec, m_RunItems);
                    m_PreviewKey = "run|" + def.Hash + "|" + key;
                }
            }

            bool valid = m_RunItems.Count > 0 && m_RunLength >= MarkingRuns.MinLength && m_RunLength <= MarkingRuns.MaxLength;
            int count = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : 0;
            string note = !valid ? (m_RunLength < MarkingRuns.MinLength ? "too short" : "no line") : count + m_RunItems.Count > kMaxItems ? "too many signs" : "";
            valid &= note.Length == 0;
            if (m_Preview != null)
            {
                m_Preview.SetActive(valid);
                m_Preview.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            if (m_WantText)
                Debug = $"line in a junction: lane {m_RunLane.Index}:{m_RunLane.Version}, {m_RunStart:0.0} m to {end:0.0} m = {m_RunLength:0.0} m, {m_RunItems.Count} item(s)" + (valid ? "" : "; " + note + " [cannot place]");
            SetHint("mark-run-end", m_RunItems.Count, m_RunLength, MarkingRuns.MaxLength, 0f, false, false, "", !valid);
            DrawMarkingOverlay(PointOn(laneArc, end, m_RunLateral, m_RunSurface), 0f, valid, null, m_RunDraw);
            if (!click)
                return true;
            if (!valid)
            {
                m_Outcome = "refused: " + note;
                return true;
            }

            foreach (RunItem item in m_RunItems)
                Place(def, MarkingTemplates.WithRun(template, spec, item.Path, item.Start, m_RunTotal), 0u, 0f, item.Position, item.Yaw, Entity.Null, 0f, "a piece of a line in a junction");
            m_Outcome = $"placed a line of {m_RunLength:0.0} m in a junction as {m_RunItems.Count} item(s)";
            Mod.log.Info("Tool: " + m_Outcome + $" along lane {m_RunLane.Index}:{m_RunLane.Version}, {def.Id}.");
            ResetRun();
            DestroyPreview();
            return true;
        }
    }
}
