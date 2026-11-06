using System;
using System.Collections.Generic;
using System.Globalization;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Mathematics;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// Placing a left-turn waiting area from the junction (mk.waiting): the cursor on an approach; the lane under it says which way the traffic runs, and so which junction it leads to. WaitingReader reads the
    /// left-turn lanes there, WaitingArea.AutoLength the length that keeps clear of the oncoming traffic, and the settings are the draft's with what was read (the lanes, their width, the left line's colour, the
    /// length, the axis); the preview is the paint itself and a click places it. What was read is kept while the cursor stays on the same approach.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private string m_WaitKey = "";
        private WaitingRead m_WaitRead;
        private string m_WaitSpec = "";
        private string m_WaitProblem = "";
        private Vec3 m_WaitPosition;
        private float m_WaitYaw, m_WaitLength;
        private int m_WaitLogs;

        private void UpdateWaiting(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity edge, EdgeInfo info, bool onRoad)
        {
            if (!onRoad)
            {
                HidePreview();
                SetHint("mark-waiting", 0, 0f, 0f, 0f, false, false, "road", true);
                if (m_WantText)
                    Debug = "Left-turn waiting area: move the cursor over the approach of a junction.";
                if (click)
                    m_Outcome = "refused: no road under the cursor";
                return;
            }

            // Which way the traffic under the cursor runs: that of the lane nearest it.
            Bezier3 curve = info.Curve;
            float t = curve.ClosestT(cursorV);
            Vec3 onCentre = curve.Point(t);
            float lateral = Vec3.Dot(cursorV - onCentre, curve.Tangent(t).HeadingXZ().RightXZ());
            info.TryLaterals(EntityManager, out List<float> forwardLanes, out List<float> backwardLanes);
            MarkingSpot spot = MarkingSnap.Stamp(MarkingAnchor.Lane, forwardLanes, backwardLanes, lateral);
            string key = $"{edge.Index}:{edge.Version}|{spot.Forward}|{Draft}";
            if (key != m_WaitKey)
            {
                m_WaitKey = key;
                ReadWaiting(template, edge, spot.Forward);
            }

            bool valid = m_WaitProblem.Length == 0 && (ChunkSystem.Instance == null || ChunkSystem.Instance.Records.Count < kMaxItems);
            if (m_WantText)
                Debug = valid ? $"left-turn waiting area: {m_WaitRead.Lanes} lane(s) of {m_WaitRead.LaneWidth:0.00} m, {m_WaitLength:0.0} m long; {m_WaitRead.Log}" : "left-turn waiting area: " + (m_WaitProblem.Length > 0 ? m_WaitProblem : "too many signs") + " [cannot place]";
            SetHint("mark-waiting", m_WaitRead != null ? m_WaitRead.Lanes : 0, m_WaitLength, 0f, 0f, m_WaitRead != null && m_WaitRead.LeftYellow, m_WaitRead != null && m_WaitRead.AllOthers,
                m_WaitProblem.StartsWith("room", StringComparison.Ordinal) ? "room" : m_WaitProblem.Length > 0 ? "none" : "", !valid);
            if (valid)
                ShowStampPreview(def, m_WaitSpec, m_WaitPosition, m_WaitYaw);
            else
                HidePreview();
            DrawWaitingOverlay(valid);
            if (!click)
                return;
            if (!valid)
            {
                m_Outcome = "refused: " + (m_WaitProblem.Length > 0 ? m_WaitProblem : "too many signs");
                return;
            }

            Place(def, m_WaitSpec, 0u, 0f, m_WaitPosition, m_WaitYaw, edge, t, "a left-turn waiting area read from the junction");
            m_Outcome = $"placed a left-turn waiting area of {m_WaitRead.Lanes} lane(s), {m_WaitLength:0.0} m";
            Mod.log.Info("Tool: " + m_Outcome + "; " + m_WaitRead.Log + ".");
        }

        private void ReadWaiting(GuideTemplate template, Entity edge, bool forward)
        {
            WaitingFor(template, Draft, edge, forward, out m_WaitRead, out m_WaitSpec, out m_WaitPosition, out m_WaitYaw, out m_WaitLength, out m_WaitProblem);
            if (m_WaitProblem.Length == 0 && m_WaitLogs++ < 10)
                Mod.log.Info($"Waiting area read: edge {edge.Index}:{edge.Version} {(forward ? "with" : "against")} it: {m_WaitRead.Log}; {m_WaitLength:0.0} m.");
        }

        /// <summary>
        /// The left-turn waiting area of the traffic on an edge that runs with it (forward) or against it, from a draft of mk.waiting: what was read, the settings with it, where the item stands and faces,
        /// its length; the problem ("" when there is none). The placing of mk.waiting and the automatic marking (0.12.2) share it.
        /// </summary>
        private bool WaitingFor(GuideTemplate template, string draftText, Entity edge, bool forward, out WaitingRead read, out string spec, out Vec3 position, out float yaw, out float length, out string problem)
        {
            spec = "";
            position = Vec3.Zero;
            yaw = 0f;
            length = 0f;
            problem = "";
            read = WaitingReader.Read(EntityManager, edge, forward);
            if (!read.Ok)
            {
                problem = read.Why;
                return false;
            }

            GuideSpec draft = template.Parse(draftText);
            float start = (float)Math.Max(0.0, Math.Min(20.0, draft.Number("start", 3.5)));
            length = WaitingArea.AutoLength(read.Axis, read.Lanes, read.LaneWidth, start, read.Others, read.HalfWidths);
            if (length < 3f)
            {
                problem = $"room for {length:0.0} m only before the oncoming traffic (3 m at least)";
                return false;
            }

            string axis = RunPath.Encode(read.Axis, out yaw);
            if (axis.Length == 0)
            {
                problem = "the left turn's path could not be written";
                return false;
            }

            position = read.Axis[0];
            spec = draft.With("lanes", read.Lanes.ToString(CultureInfo.InvariantCulture))
                .With("lane", Math.Round(read.LaneWidth, 2).ToString("0.##", CultureInfo.InvariantCulture))
                .With("len", Math.Round(length, 1).ToString("0.#", CultureInfo.InvariantCulture))
                .With("left", read.LeftYellow ? "y" : "w")
                .With(MarkingTemplates.WaitingAxisKey, axis).ToString();
            length += start;
            return true;
        }

        /// <summary>The axis of the area, and thin grey lines where the oncoming traffic runs (what its length keeps clear of).</summary>
        private void DrawWaitingOverlay(bool valid)
        {
            if (m_WaitRead == null || !m_WaitRead.Ok)
                return;
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            var axisColour = valid ? new UnityEngine.Color(0.2f, 1f, 0.35f, 0.9f) : new UnityEngine.Color(1f, 0.2f, 0.15f, 0.9f);
            var otherColour = new UnityEngine.Color(0.75f, 0.75f, 0.8f, 0.5f);
            float3 F(Vec3 p) => new float3(p.X, p.Y + 0.12f, p.Z);
            for (int i = 1; i < m_WaitRead.Axis.Count; i++)
                buffer.DrawLine(axisColour, new Line3.Segment(F(m_WaitRead.Axis[i - 1]), F(m_WaitRead.Axis[i])), 0.1f);
            foreach (IList<Vec3> lane in m_WaitRead.Others)
            {
                for (int i = 1; i < lane.Count; i++)
                    buffer.DrawLine(otherColour, new Line3.Segment(F(lane[i - 1]), F(lane[i])), 0.06f);
            }
        }
    }
}
