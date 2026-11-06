using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Mathematics;
using Game.Rendering;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// Placing a road marking (GB 5768.3, decoration only: nothing here reads or changes a lane's rules). A marking of one piece (an arrow, a stop line, a crosswalk, a parking bay) is a stamp: on a road it
    /// goes into the lane under the cursor, or over the lanes of its direction, or over the whole road (MarkingSnap), faces the traffic of that lane, takes the road's grade, and a click places it. A marking
    /// that runs along a road (a lane line, an edge line, a hatched island's edge) is drawn with two clicks: the first sets where it starts and across the road where it lies (on the line between two
    /// lanes, or the edge, that is near the cursor), the second where it ends; the line follows the road's curve and is cut into items (MarkingRuns) of the length a setting can hold. A right click or Esc
    /// first cancels the line that was started, then leaves the tool.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>Two markings of the same kind this near (m) are one marking put twice: the second click is refused.</summary>
        private const float kSameMarking = 0.15f;

        /// <summary>A line of more pieces than this is previewed by its overlay line, not its paint.</summary>
        private const int kRunPreviewItems = 20;

        /// <summary>A run's end or start this near (m) to an end of its road is taken to be at that end, so that a line reaches the junction.</summary>
        private const float kRunEndSnap = 1.5f;

        /// <summary>A kerb line started this near (m) the face of the road's kerb goes on it (0.12.33).</summary>
        private const float kKerbSnap = 1f;

        private bool m_RunActive;
        private Entity m_RunEdge;

        /// <summary>
        /// The road the line was started on (0.12.2): the chain of its segments through the nodes and across the junctions where it goes on straight (ChainReader), so that the end follows the road past
        /// the segment it began on; and the stretches of the chain the line may lie on (it is cut at a junction, where the lanes end and begin again; an edge line goes on along a side no road joins).
        /// </summary>
        private ChainRead m_RunRead;
        private List<ChainSpan> m_RunSpans = new List<ChainSpan>();

        /// <summary>A barrier's run (0.12.60): the distances along its road where a barrier of its kind ends already on the line it runs along (RunJoins), so that its start and end are drawn to them.</summary>
        private bool m_RunBarrier;
        private List<float> m_RunJoins = new List<float>();
        private readonly List<List<Vec3>> m_RunParts = new List<List<Vec3>>();

        /// <summary>The parts of the run as the overlay draws them (OverlayLine), made when the run changes.</summary>
        private readonly List<List<Vec3>> m_RunDraw = new List<List<Vec3>>();
        private float m_RunStart;
        private float m_RunLateral;

        /// <summary>A roadside barrier's run follows the road's edge on this side (0: it keeps its lateral), at this distance beyond it (0.12.16).</summary>
        private int m_RunEdgeSide;
        private float m_RunEdgeOffset;

        /// <summary>For the log of the line's start: it was put at the road's edge instead of the lane's edge line (BarrierAuto.RoadsideSpot), and where that line was.</summary>
        private string m_RunAtEdge = "";
        private EdgeProfile m_RunEdgeProfile;
        private float m_RunSurface;

        /// <summary>How high the road's kerb is that a kerb line was put on (m); NaN when it was put where the cursor was.</summary>
        private float m_RunKerbRise = float.NaN;

        /// <summary>How many times a kerb line drawn along a road piece the game is still loading was said in the log (3 at the most).</summary>
        private static int s_KerbWarned;

        /// <summary>The kerbs on the side of the chain a kerb line put on a kerb is on (0.12.49, KerbFeet): the line is snapped to them.</summary>
        private List<KerbLine> m_RunKerbFeet = new List<KerbLine>();

        /// <summary>
        /// How far (m) the player moved the line away from the road's middle with the arrow keys (0.12.22; the owner: "左右方向键微调...即靠近/远离道路"): ← nearer, → farther, kNudgeStep a press (Shift
        /// kNudgeCoarse). Kept from line to line until another entry is chosen; a line that follows the road's edge keeps that much more distance from it.
        /// </summary>
        private float m_RunNudge;
        private const float kNudgeStep = 0.1f, kNudgeCoarse = 0.5f, kMaxNudge = 6f;
        private int m_NudgeLogs;

        /// <summary>A lateral (m right of the road's line) moved by the nudge away from the road's middle (to the right when it is on the middle).</summary>
        private float Nudged(float lateral) => lateral + (lateral < 0f ? -m_RunNudge : m_RunNudge);

        /// <summary>The arrow keys of this frame (ReadArrows) on the nudge, for a line along the road and for the automatic barriers.</summary>
        private void ReadNudge()
        {
            if (m_ArrowX == 0)
                return;
            float step = m_Shift ? kNudgeCoarse : kNudgeStep;
            float was = m_RunNudge;
            m_RunNudge = Mathf.Round(Mathf.Clamp(m_RunNudge + m_ArrowX * step, -kMaxNudge, kMaxNudge) * 100f) / 100f;
            if (m_NudgeLogs++ < 60)
                Mod.log.Info($"Tool: line distance from the road (arrow keys) {was:0.00} -> {m_RunNudge:0.00} m.");
        }

        // What was worked out for the line being drawn, kept while the end of it does not move: the cursor moves a pixel at a time and the items of a long line are many.
        private string m_RunKey = "";
        private List<RunItem> m_RunItems = new List<RunItem>();
        private List<Vec3> m_RunLine = new List<Vec3>();
        private float m_RunLength;
        private float m_RunTotal;

        /// <summary>
        /// The tip of a strip's V lines for the traffic it lies over: the lane nearest the strip's middle (its line plus half its width to the side it is on, across the road) runs with the edge or against
        /// it; the line was drawn with the edge (alongEdge) or against it. The Vs point to the line's start when the traffic runs from the start to the end.
        /// </summary>
        private string StripTip(GuideTemplate template, string spec, List<float> forward, List<float> backward, bool alongEdge)
        {
            GuideSpec parsed = template.Parse(spec);
            if (!parsed.Is("fill", "chevron") || forward == null || backward == null)
                return parsed.ToString();
            float width = (float)Math.Max(0.3, Math.Min(12.0, parsed.Number("width", 1.5)));
            // The strip's side is the line's right or left as it was drawn; seen along the edge that is the same when the line goes with the edge.
            float side = (parsed.Is("side", "l") ? -1f : 1f) * (alongEdge ? 1f : -1f);
            MarkingSpot lane = MarkingSnap.Stamp(MarkingAnchor.Lane, forward, backward, Nudged(m_RunLateral) + side * width * 0.5f);
            if (!lane.HadLanes)
                return parsed.ToString();
            return parsed.With("tip", lane.Forward == alongEdge ? "s" : "e").ToString();
        }

        /// <summary>Whether a line that runs along a road has been started (its first click made).</summary>
        internal static bool RunStarted => Instance != null && Instance.m_RunActive;

        private void ResetRun()
        {
            // An island being drawn is given up with the line, and so is the stretch chosen for the automatic marking: all are what the tool was in the middle of.
            ResetPolygon();
            ForgetAutoChoice();
            ResetCut();
            m_RunActive = false;
            m_BendStarted = false;
            m_RunOnLane = false;
            m_RunLane = Entity.Null;
            m_RunLaneArc = null;
            m_RunEdge = Entity.Null;
            m_RunRead = null;
            m_Stand?.Clear();
            m_RunSpans.Clear();
            m_RunJoins.Clear();
            m_RunBarrier = false;
            ResetLine();
            m_RunParts.Clear();
            m_RunDraw.Clear();
            m_RunKey = "";
            m_RunItems.Clear();
            m_RunLine.Clear();
            m_RunLength = 0f;
            m_RunKerbRise = float.NaN;
            m_RunKerbFeet.Clear();
            m_RunEdgeProfile = null;
            ForgetRunPreview();
        }

        /// <summary>A right click or Esc while a line is started cancels that line and nothing else (the tool stays); so it lets go a stretch chosen for the automatic marking. True when it did.</summary>
        private bool CancelRun()
        {
            if (m_AutoSelected != Entity.Null)
            {
                Mod.log.Info("Tool: the stretch chosen for the automatic marking is let go.");
                ForgetAutoChoice();
                m_Outcome = "the chosen stretch is let go";
                return true;
            }

            if (!m_RunActive)
                return CancelPolygon();
            Mod.log.Info("Tool: the marking line that was started is cancelled.");
            ResetRun();
            HidePreview();
            m_Outcome = "the line that was started is cancelled";
            return true;
        }

        private static float GradeAlong(Bezier3 curve, float t, float yaw)
        {
            Vec3 tangent = curve.Tangent(t);
            float horizontal = tangent.LengthXZ;
            if (horizontal < 1e-4f)
                return 0f;
            Vec3 heading = tangent.HeadingXZ();
            float along = heading.X * Mathf.Sin(yaw) + heading.Z * Mathf.Cos(yaw);
            return Mathf.Clamp(tangent.Y / horizontal * along, -0.3f, 0.3f);
        }

        // ------------------------------------------------------------------ place

        private void UpdatePlaceMarking(SignChoice choice, bool click)
        {
            SignDef def = choice.Def;
            GuideTemplate template = def.Template;
            EndAttach();
            bool run = MarkingTemplates.IsRun(template), bends = MarkingTemplates.Bends(template);
            // A line started for another kind of marking is let go (a bent marking drawn along the road shares the line's state: PlacementTool.Bend).
            if (m_RunActive && (run ? m_BendStarted : !(bends && m_BendStarted)))
                ResetRun();
            if (run || bends || def.Id == BarrierAuto.Id)
                ReadNudge();

            long lap = System.Diagnostics.Stopwatch.GetTimestamp();
            bool hasRay = GetRaycastResult(out Entity owner, out RaycastHit hit);
            SlowLog.Since("Tool: the game's raycast result", lap);
            if (!hasRay)
            {
                HidePreview();
                Debug = "Move the cursor over a road or the ground.";
                SetHint("road");
                m_Outcome = "ignored: the raycast found neither a road nor the ground under the cursor";
                return;
            }

            lap = System.Diagnostics.Stopwatch.GetTimestamp();
            RoadBelowGround(ref owner, ref hit);
            float3 cursor = hit.m_HitPosition;
            Vec3 cursorV = RoadReader.ToVec(cursor);
            Entity edge = Entity.Null;
            EdgeInfo info = null;
            bool onRoad = false;
            if (RoadReader.TryResolveEdge(EntityManager, owner, hit.m_HitEntity, cursor, out edge))
            {
                info = EdgeInfo.Of(EntityManager, edge);
                onRoad = info.CurveOk;
            }

            // An island is drawn corner by corner; any other marking leaves an island that was being drawn (an area that clears the game's markings may be drawn so too).
            bool clear = def.Id == VanillaClear.Id;
            if (clear)
                UpdateClear(def, template, click, cursorV, hit, owner, edge, info, onRoad);
            else if (def.Id == AutoMarking.Id)
                UpdateAuto(def, template, click, cursorV, hit, edge, info, onRoad, Draft);
            else if (def.Id == BarrierAuto.Id)
                UpdateBarrierAuto(template, click, hit, edge, onRoad);
            else if (def.Id == MarkingTemplates.WaitingId)
                UpdateWaiting(def, template, click, cursorV, hit, edge, info, onRoad);
            else if (MarkingTemplates.IsDrawnArea(def.Id))
                UpdatePolygon(def, template, click, cursorV, hit, owner, edge, info, onRoad);
            else if (run && MarkingTemplates.FollowsJunctionLanes(def.Id) && (m_RunOnLane || !m_RunActive)
                && UpdateLaneRun(def, template, click, cursorV, hit, m_RunActive ? Entity.Null : JunctionAt(owner, hit.m_HitEntity, edge, info, onRoad, cursorV)))
            {
                // A line inside a junction, along one of its lanes (PlacementTool.LaneRun.cs).
            }
            else if (run)
                UpdateRun(def, template, click, cursorV, hit, edge, info, onRoad);
            else if (bends && (m_RunActive || MarkingTemplates.DrawsAlong(template, template.Parse(Draft)) || (onRoad && m_Rotation == 0f)))
                UpdateBend(def, template, click, cursorV, hit, edge, info, onRoad);
            else
                UpdateStamp(def, template, click, cursorV, hit, edge, info, onRoad);
            if (!MarkingTemplates.IsDrawnArea(def.Id) && !clear && m_PolyPoints.Count > 0)
                ResetPolygon();
            if (def.Id != AutoMarking.Id)
                m_AutoKey = "";
            if (def.Id != BarrierAuto.Id)
                m_BarrierKey = "";
            SlowLog.Since("Tool: placing a marking (road, lanes, preview, overlay)", lap);
        }

        /// <summary>The settings a marking is placed with: the draft, with the arrows' size taken from the road's speed.</summary>
        private string MarkingSpec(GuideTemplate template, EdgeInfo info, bool onRoad)
        {
            float kmh = 0f;
            bool hasSpeed = onRoad && info.TrySpeed(EntityManager, out kmh, out _);
            return MarkingTemplates.ResolveSize(template, Draft, hasSpeed ? kmh : 0f, hasSpeed);
        }

        private void UpdateStamp(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity edge, EdgeInfo info, bool onRoad)
        {
            // The gore of an exit or an entry is made from the fork when the cursor is near one.
            bool gore = def.Id == "mk.gore";
            if (gore && onRoad && UpdateGoreAtFork(def, template, click, cursorV, hit, edge))
                return;
            Vec3 position;
            float yaw;
            float grade = 0f;
            float edgeT = 0f;
            string note;
            MarkingSpot spot = default;
            float fitSpan = 0f;
            int fitLanes = 0;
            if (onRoad)
            {
                Bezier3 curve = info.Curve;
                float t = curve.ClosestT(cursorV);
                Vec3 onCentre = curve.Point(t);
                Vec3 heading = curve.Tangent(t).HeadingXZ();
                Vec3 right = heading.RightXZ();
                float cursorLateral = Vec3.Dot(cursorV - onCentre, right);
                List<float> forward = null, backward = null;
                info.TryLaterals(EntityManager, out forward, out backward);
                // A marking that spans lanes (a stop line, a crosswalk) takes its length across from them, and stands in their middle.
                spot = MarkingSnap.Fit(MarkingSnap.AnchorOf(def.Id), forward, backward, cursorLateral, out fitSpan, out fitLanes);
                position = onCentre + right * spot.Lateral;
                // The raycast puts the centre line at m_Position and the surface at m_HitPosition: their difference is the height of the surface.
                position.Y = onCentre.Y + (hit.m_HitPosition.y - hit.m_Position.y);
                yaw = EdgeSnap.YawOf(heading) + (spot.Forward ? 0f : Mathf.PI) + (m_Flip ? Mathf.PI : 0f) + m_Rotation;
                grade = GradeAlong(curve, t, yaw);
                edgeT = t;
                note = m_WantText ? $"marking {def.Id} on road {edge.Index}:{edge.Version}, {spot.Lateral:+0.00;-0.00} m from the centre line ({(spot.Snapped ? "snapped to the lanes" : "at the cursor")}, {(spot.Forward ? "with" : "against")} the edge), grade {grade * 100f:0.0} %" : "";
            }
            else
            {
                edge = Entity.Null;
                position = cursorV;
                yaw = (m_Flip ? Mathf.PI : 0f) + m_Rotation;
                note = m_WantText ? $"marking {def.Id} on the ground (no road under the cursor)" : "";
            }

            string spec = MarkingTemplates.WithFit(template, MarkingTemplates.WithGrade(template, MarkingSpec(template, info, onRoad), grade), fitSpan, fitLanes);
            // Laid on what is under it, not flat at the cursor's height (0.12.30): a hump across a crowned road, an island down a slope.
            if (MarkingTemplates.Drapes(def.Id))
                spec = MarkingTemplates.WithGround(template, spec, GroundUnder(def, spec, position, yaw));
            bool valid = true;
            var records = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records : null;
            if (records != null)
            {
                if (records.Count >= kMaxItems)
                {
                    valid = false;
                    if (m_WantText)
                        note += "; too many signs";
                }
                else
                {
                    float nearest = NearestSameHere(position, def.Hash);
                    if (nearest < kSameMarking)
                    {
                        valid = false;
                        if (m_WantText)
                            note += $"; the same marking is {nearest:0.00} m away";
                    }
                }
            }

            if (m_WantText)
                Debug = note + (valid ? "" : " [cannot place]");
            SetHint(gore ? "mark-gore-free" : "mark-stamp", 0, 0f, 0f, 0f, false, false, "", !valid);
            if (gore && m_WantText && !onRoad)
                m_ForkProblem = "no road under the cursor";
            if (gore && m_WantText)
                Debug += " (no fork: " + m_ForkProblem + ")";
            ShowStampPreview(def, spec, position, yaw);
            DrawMarkingOverlay(position, yaw, valid, null);
            if (click)
            {
                if (valid)
                {
                    Place(def, spec, 0u, 0f, position, yaw, edge, edgeT, note);
                    m_Outcome = "placed: " + note;
                }
                else
                {
                    m_Outcome = "refused: " + note;
                }
            }
        }

        private void UpdateRun(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity edge, EdgeInfo info, bool onRoad)
        {
            if (!m_RunActive)
            {
                if (!onRoad)
                {
                    HidePreview();
                    Debug = "A marking that runs along a road: move the cursor over a road.";
                    SetHint("mark-run-road", 0, 0f, 0f, 0f, false, false, "", true);
                    if (click)
                        m_Outcome = "refused: no road under the cursor";
                    return;
                }

                Bezier3 curve = info.Curve;
                float t = curve.ClosestT(cursorV);
                Vec3 onCentre = curve.Point(t);
                Vec3 right = curve.Tangent(t).HeadingXZ().RightXZ();
                float cursorLateral = Vec3.Dot(cursorV - onCentre, right);
                info.TryLaterals(EntityManager, out List<float> forward, out List<float> backward);
                // A kerb line lies on the face of the road's kerb near the cursor (0.12.33), or where the cursor is (a kerb the cross-section does not have); the others lie on the lines the lanes make.
                bool kerb = MarkingTemplates.IsKerbLine(def.Id);
                float kerbRise = float.NaN;
                if (kerb && RoadReader.CompositionKerb(EntityManager, edge, cursorLateral, out float kerbLateral, out float rise) && Mathf.Abs(kerbLateral - cursorLateral) < kKerbSnap)
                {
                    cursorLateral = kerbLateral;
                    kerbRise = rise;
                }

                bool barrier = def.Group == MarkingTemplates.BarrierGroup;
                // A barrier may also stand on a kerb or a median between the carriageways (0.12.22: a BRT road's separators).
                MarkingSpot spot = kerb ? MarkingSnap.FreeLine(cursorLateral, !float.IsNaN(kerbRise))
                    : barrier ? MarkingSnap.BarrierLine(forward, backward, RoadReader.CompositionRaised(EntityManager, edge), cursorLateral) : MarkingSnap.Line(forward, backward, cursorLateral);
                // A roadside barrier lies at the road's edge, not on the painted edge line of the outermost lane (0.12.101); the click below does the same, so that what is shown is what is placed.
                if (barrier && !spot.OnRaised && !template.Parse(Draft).Is("where", "m"))
                    spot = BarrierAuto.RoadsideSpot(spot, forward, backward, BarrierAuto.EdgeOfRoad(info.Width * 0.5f, forward, backward, spot.Lateral >= 0f ? 1 : -1));
                float startLateral = Nudged(spot.Lateral);
                float s = SnapToEnds(info.Arc, info.Arc.SAt(t));
                // A line started in a junction's part begins where the junction does (RoadChain.Snap at the click): shown there. A side line and a barrier may go on through a junction (their spans).
                if (!barrier && !IsSideLine(def.Id))
                    s = JunctionStart(edge, info.Arc, s);
                float surface = kerb ? 0f : hit.m_HitPosition.y - hit.m_Position.y;
                if (m_WantText)
                    Debug = $"line start: road {edge.Index}:{edge.Version}, {s:0.0} m along it, {startLateral:+0.00;-0.00} m from the centre line ({(spot.OnRaised ? "on a kerb between the carriageways" : spot.Snapped ? "on a lane line or the edge" : "at the cursor")}{(m_RunNudge != 0f ? $", moved {m_RunNudge:+0.00;-0.00} m" : "")})";
                SetHint("mark-run-start", 0, 0f, 0f, m_RunNudge);
                HidePreview();
                // A short stretch of the line where it will lie, and the ring where it starts; a kerb line's on its kerb.
                List<Vec3> stub = MarkingRuns.Line(info.Arc, Mathf.Max(0f, s - 6f), Mathf.Min(info.Arc.Length, s + 6f), startLateral, surface);
                Vec3 ring = PointOn(info.Arc, s, startLateral, surface);
                if (!float.IsNaN(kerbRise))
                {
                    List<KerbLine> feet = KerbFeet(new[] { edge }, p =>
                    {
                        float pt = curve.ClosestT(p);
                        return Vec3.Dot(p - curve.Point(pt), curve.Tangent(pt).HeadingXZ().RightXZ());
                    }, startLateral);
                    stub = Kerbs.SnapToFeet(stub, feet, m_RunNudge, Kerbs.SnapReach + Mathf.Abs(m_RunNudge));
                    ring = Kerbs.SnapToFeet(new[] { ring }, feet, m_RunNudge, Kerbs.SnapReach + Mathf.Abs(m_RunNudge))[0];
                }

                DrawMarkingOverlay(ring, 0f, true, stub);
                if (click)
                {
                    // The road the line follows: the chain of segments from this one, through the nodes and across the junctions where it goes on straight; the start on it, at the lanes' end when near one.
                    ChainRead read = ChainReader.ReadEachWay(EntityManager, edge, true);
                    if (read.Chain == null || read.Chain.Pieces.Count == 0)
                    {
                        m_Outcome = "refused: the road could not be read";
                        return;
                    }

                    // On the segment clicked (0.12.52): the chain read both ways may pass over or under it, or beside it, elsewhere.
                    ChainPiece clicked = read.PieceOf(edge);
                    float chainS = read.Chain.ProjectLevel(float.IsNaN(kerbRise) ? cursorV : onCentre + right * cursorLateral, out float chainLateral, clicked?.S0 ?? float.MinValue, clicked?.S1 ?? float.MaxValue);
                    ChainPiece under = read.Chain.Pieces[read.Chain.PieceAt(chainS)];
                    MarkingSpot along = kerb ? MarkingSnap.FreeLine(chainLateral) : barrier ? MarkingSnap.BarrierLine(under.With, under.Against, under.RaisedAcross, chainLateral) : MarkingSnap.Line(under.With, under.Against, chainLateral);
                    GuideSpec drafted = template.Parse(Draft);
                    // A barrier goes on or stops at the junctions as its setting says (0.12.16), and leaves a crosswalk across the road free (0.12.15: BarrierAuto.CrosswalkClearance each side of it).
                    List<ChainSpan> spans = barrier ? null : read.Chain.Spans(along.Lateral, IsSideLine(def.Id));
                    // A roadside barrier (not in a median or on a kerb between the carriageways, nearer the edge than the middle) keeps its distance from the road's edge, through a merge onto the road it goes on along.
                    m_RunEdgeSide = 0;
                    m_RunEdgeProfile = null;
                    m_RunAtEdge = "";
                    if (barrier && !drafted.Is("where", "m") && !along.OnRaised)
                    {
                        int side = along.Lateral >= 0f ? 1 : -1;
                        var profile = new EdgeProfile(read.Chain, side);
                        float edgeHere = profile.At(chainS);
                        float onLine = along.Lateral;
                        along = BarrierAuto.RoadsideSpot(along, under.With, under.Against, edgeHere);
                        m_RunAtEdge = along.Lateral != onLine ? $", at the road's edge instead of the lane line at {onLine:+0.00;-0.00} m" : "";
                        if (Mathf.Abs(along.Lateral) > edgeHere * 0.5f)
                        {
                            m_RunEdgeSide = side;
                            m_RunEdgeOffset = Mathf.Abs(along.Lateral) - edgeHere;
                            m_RunEdgeProfile = profile;
                        }
                    }
                    // A barrier is open at a junction where a road joins on its side at that road's mouth, as far as the mouths are known (0.12.67), along the line it will stand on: the edge's when it follows it.
                    if (barrier)
                    {
                        EdgeProfile followed = m_RunEdgeProfile;
                        float offsetNow = m_RunEdgeOffset;
                        spans = CutAtCrossings(read.Chain.Spans(along.Lateral, BarrierAuto.JunctionOf(drafted), drafted.Is("where", "m"), followed != null ? (System.Func<float, float>)(s => followed.LateralAt(s, offsetNow)) : null, BarrierAuto.EndsAtNodes(drafted)), read.Chain);
                    }

                    m_RunBarrier = barrier;
                    m_RunJoins = barrier ? RunJoins.Of(read.Chain, PlacedRuns(def), along.Lateral) : new List<float>();
                    float snapped = SnapRunEnd(spans, chainS);
                    m_RunActive = true;
                    m_RunEdge = edge;
                    m_RunRead = read;
                    m_RunSpans = spans;
                    m_RunStart = float.IsNaN(snapped) ? chainS : snapped;
                    m_RunLateral = along.Lateral;
                    m_RunSurface = surface;
                    m_RunKerbRise = kerbRise;
                    m_RunKerbFeet = float.IsNaN(kerbRise) ? new List<KerbLine>() : KerbFeet(read.Edges, p =>
                    {
                        read.Chain.Project(p, out float l);
                        return l;
                    }, along.Lateral);
                    m_RunKey = "";
                    m_Outcome = $"line started on road {edge.Index}:{edge.Version} ({read.Log}) at {m_RunStart:0.0} m, {along.Lateral:+0.00;-0.00} m from the centre line{(along.OnRaised ? " (on a kerb between the carriageways)" : "")}{m_RunAtEdge}{(m_RunNudge != 0f ? $", moved {m_RunNudge:+0.00;-0.00} m" : "")}";
                    Mod.log.Info("Tool: " + m_Outcome + ".");
                }

                return;
            }

            // The line is started: its end follows the cursor along the road it was started on, past the segment it began on.
            RoadChain chain = m_RunRead?.Chain;
            if (chain == null || m_RunRead.Edges.Exists(e => !RoadReader.IsLiveEdge(EntityManager, e)))
            {
                Mod.log.Info("Tool: the road of the line that was started is gone; the line is cancelled.");
                ResetRun();
                HidePreview();
                SetHint("mark-run-road", 0, 0f, 0f, 0f, false, false, "", true);
                Debug = "The road the line was started on is gone.";
                m_Outcome = "the road of the line is gone";
                return;
            }

            // Where the line lies across the road now: as started, moved by the arrow keys.
            float lateral = Nudged(m_RunLateral), edgeOffset = m_RunEdgeOffset + m_RunNudge;
            float raw = chain.ProjectLevel(cursorV, out _);
            float snappedEnd = SnapRunEnd(m_RunSpans, raw);
            float end = float.IsNaN(snappedEnd) ? raw : snappedEnd;
            // The end moves in steps of 0.5 m: the line and its items are made again only when it has.
            float endStep = Mathf.Round(end * 2f) * 0.5f;
            Entity endEdge = m_RunRead.EdgeAt(end);
            EdgeInfo runInfo = EdgeInfo.Of(EntityManager, endEdge);
            string spec = MarkingSpec(template, runInfo, runInfo.CurveOk);
            ChainPiece mid = chain.Pieces[chain.PieceAt((m_RunStart + end) * 0.5f)];
            // The Vs of a strip point to the traffic in the lane under its middle: to the start of the line when that traffic runs the way the line was drawn.
            if (def.Id == "mk.strip")
                spec = StripTip(template, spec, mid.With, mid.Against, end >= m_RunStart);
            // The kerb is on the side of the road the line was started on: on the right of the way the line runs when that is the right of the road and the line goes with the road, or the left and against it.
            // As high as the road's kerb when it was put on one.
            if (MarkingTemplates.IsKerbLine(def.Id))
            {
                GuideSpec kerbSpec = template.Parse(spec).With("side", (lateral > 0f) == (end >= m_RunStart) ? "r" : "l");
                spec = (float.IsNaN(m_RunKerbRise) ? kerbSpec : kerbSpec.With("height", Mathf.RoundToInt(m_RunKerbRise * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToString();
            }
            string key = $"{m_RunEdge.Index}:{m_RunEdge.Version}|{m_RunRead.Edges.Count}|{m_RunStart:0.00}|{endStep:0.0}|{lateral:0.00}|{m_RunSurface:0.000}|{m_RunEdgeSide}|{edgeOffset:0.00}|{spec}|{MarkingLetters.Version}";
            if (key != m_RunKey)
            {
                m_RunKey = key;
                long made = System.Diagnostics.Stopwatch.GetTimestamp();
                // The line in the stretches of the road it may lie on, in the order it is drawn; a dashed line goes on in its pattern over the gap of a junction.
                m_RunParts.Clear();
                m_RunDraw.Clear();
                m_RunItems = new List<RunItem>();
                m_RunLine = new List<Vec3>();
                bool barrierRun = def.Group == MarkingTemplates.BarrierGroup;
                foreach (ChainSpan part in RoadChain.Clip(m_RunSpans, m_RunStart, end))
                {
                    bool endMoves = Mathf.Abs(part.To - end) < 0.001f;
                    System.Func<float, float> across = m_RunEdgeSide != 0 ? s => m_RunEdgeProfile.LateralAt(s, edgeOffset) : s => lateral;
                    // A barrier's line is cut where the road bends (BarrierPath); the interval at a moving end is not, so that the points before it stay where they are as the end moves.
                    List<Vec3> line = barrierRun ? chain.LineAdaptive(part.From, part.To, across, m_RunSurface, !endMoves)
                        : m_RunEdgeSide != 0 ? chain.Line(part.From, part.To, across, m_RunSurface) : chain.Line(part.From, part.To, lateral, m_RunSurface);
                    // A barrier stands on what is under it (0.12.20) and paint lies on it (0.12.32), not at the height of the road's centre line. The part the end is in (its To is the end) keeps no key of
                    // its end: its points come at the same distances from its start however far the end is, and only the last one moves.
                    string partKey = $"{m_RunEdge.Index}:{m_RunEdge.Version}|{part.From:0.00}|{(part.To >= part.From ? "+" : "-")}{(endMoves ? "end" : part.To.ToString("0.00"))}|{lateral:0.00}|{m_RunEdgeSide}|{edgeOffset:0.00}|{m_RunSurface:0.000}";
                    // A kerb line is on its kerb where the game draws it (0.12.49), at the carriageway's heights in front of the kerb, as it is laid when placed (MarkingGround.AtFoot).
                    if (MarkingTemplates.IsKerbLine(def.Id))
                    {
                        if (m_RunKerbFeet.Count > 0)
                            line = Kerbs.SnapToFeet(line, m_RunKerbFeet, m_RunNudge, Kerbs.SnapReach + Mathf.Abs(m_RunNudge));
                        int provisional = SurfaceReader.ProvisionalReads;
                        line = MarkingGround.AtFoot(line, template.Parse(spec).Is("side", "r"), l => OnSurface(l, partKey, endMoves, true));
                        if (SurfaceReader.ProvisionalReads != provisional && s_KerbWarned++ < 3)
                            Mod.log.Warn($"Marking ground: a kerb line is drawn along a road piece that the game is still loading, so its foot is read at the kerb's top and it is placed there ({SurfaceReader.KerbSummary()}); draw it again when no road piece waits.");
                    }
                    else
                        line = OnSurface(line, partKey, endMoves, !barrierRun);
                    if (line.Count < 2)
                        continue;
                    m_RunParts.Add(line);
                    m_RunDraw.Add(OverlayLine.Simplify(line));
                    m_RunLine.AddRange(line);
                    float offset = Mathf.Abs(part.From - m_RunStart);
                    float partLength = 0f;
                    for (int i = 1; i < line.Count; i++)
                        partLength += Vec3.DistanceXZ(line[i - 1], line[i]);
                    foreach (RunItem item in MarkingRuns.Items(line, MarkingTemplates.PathBudget(template, spec)))
                    {
                        RunItem shifted = item;
                        if (barrierRun)
                        {
                            shifted.PartStart = item.Start;
                            shifted.PartLength = partLength;
                        }

                        shifted.Start += offset;
                        m_RunItems.Add(shifted);
                    }
                }

                m_RunLength = Mathf.Abs(end - m_RunStart);
                m_RunTotal = m_RunLength;
                // A long line (more than kRunPreviewItems pieces) is shown by the overlay's line alone.
                SetRunPreview(template, spec, m_RunItems.Count <= kRunPreviewItems ? m_RunItems : null);
                SlowLog.Since("Tool: a run's line and items", made);
            }

            bool valid = m_RunItems.Count > 0 && m_RunLength >= MarkingRuns.MinLength && m_RunLength <= MarkingRuns.MaxLength;
            string note = "";
            int count = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : 0;
            if (valid && count + m_RunItems.Count > kMaxItems)
            {
                valid = false;
                note = "too many signs";
            }
            else if (!valid)
            {
                note = m_RunLength < MarkingRuns.MinLength ? "too short" : m_RunLength > MarkingRuns.MaxLength ? "too long" : "no line";
            }

            long shown = System.Diagnostics.Stopwatch.GetTimestamp();
            StepRunPreview(template, valid);
            SlowLog.Since("Tool: a run's preview meshes", shown);

            int segments = 0;
            if (m_RunItems.Count > 0)
                segments = Math.Abs(chain.PieceAt(end) - chain.PieceAt(m_RunStart)) + 1;
            if (m_WantText)
                Debug = $"line: road {m_RunEdge.Index}:{m_RunEdge.Version} ({m_RunRead.Log}), {m_RunStart:0.0} m to {end:0.0} m = {m_RunLength:0.0} m over {segments} segment(s) in {m_RunParts.Count} part(s), {m_RunItems.Count} item(s)" + (valid ? "" : "; " + note + " [cannot place]");
            SetHint("mark-run-end", m_RunItems.Count, m_RunLength, MarkingRuns.MaxLength, m_RunNudge, false, false, "", !valid);
            DrawMarkingOverlay(chain.PointAt(end, m_RunEdgeSide != 0 ? m_RunEdgeProfile.LateralAt(end, edgeOffset) : lateral, m_RunSurface), 0f, valid, null, m_RunDraw);
            if (click)
            {
                if (!valid)
                {
                    m_Outcome = "refused: " + note;
                    return;
                }

                int placed = 0;
                float sign = end >= m_RunStart ? 1f : -1f;
                foreach (RunItem item in m_RunItems)
                {
                    // Each piece names the segment it starts on.
                    Entity at = m_RunRead.EdgeAt(m_RunStart + sign * item.Start);
                    EdgeInfo atInfo = EdgeInfo.Of(EntityManager, at);
                    Place(def, WithRunOf(template, spec, item), 0u, 0f, item.Position, item.Yaw, at, atInfo.CurveOk ? atInfo.Curve.ClosestT(item.Position) : 0f, "a piece of a line");
                    placed++;
                }

                m_Outcome = $"placed a line of {m_RunLength:0.0} m over {segments} segment(s) in {m_RunParts.Count} part(s) as {placed} item(s)";
                Mod.log.Info("Tool: " + m_Outcome + $" from road {m_RunEdge.Index}:{m_RunEdge.Version}, {def.Id}.");
                LogRunRoad(chain, end, lateral, edgeOffset);
                ResetRun();
                DestroyPreview();
            }
        }

        /// <summary>
        /// The road a placed line went along, for the log (0.12.52, after the owner's picture of a barrier drawn as a V across the carriageways): the pieces it covers, where it ran across them, and
        /// the longest step between two of its points (along the road they are MarkingRuns.Step apart).
        /// </summary>
        private void LogRunRoad(RoadChain chain, float end, float lateral, float edgeOffset)
        {
            float longest = 0f;
            Vec3 from = default, to = default;
            foreach (List<Vec3> part in m_RunParts)
            {
                for (int i = 1; i < part.Count; i++)
                {
                    float d = Vec3.DistanceXZ(part[i - 1], part[i]);
                    if (d > longest)
                    {
                        longest = d;
                        from = part[i - 1];
                        to = part[i];
                    }
                }
            }

            float LateralAt(float s) => m_RunEdgeSide != 0 ? m_RunEdgeProfile.LateralAt(s, edgeOffset) : lateral;
            float middle = (m_RunStart + end) * 0.5f;
            // The stretches the line was drawn in (0.12.64): what is between two of them is a junction it was cut at.
            string drawn = string.Join(", ", RoadChain.Clip(m_RunSpans, m_RunStart, end).ConvertAll(span => $"{span.From:0.0}-{span.To:0.0}"));
            Mod.log.Info($"Tool: the line's road: {chain.Describe(m_RunStart, end)}; drawn in {drawn} m; across it {LateralAt(m_RunStart):+0.00;-0.00} m at the start, {LateralAt(middle):+0.00;-0.00} m half way, {LateralAt(end):+0.00;-0.00} m at the end"
                + (m_RunEdgeSide != 0 ? $" (along the edge, {edgeOffset:+0.00;-0.00} m off it)" : "") + $"; longest step {longest:0.0} m"
                + (longest > 3f * MarkingRuns.Step ? $", from ({from.X:0.0}, {from.Z:0.0}) to ({to.X:0.0}, {to.Z:0.0})" : "") + ".");
        }

        private static List<ChainSpan> CutAtCrossings(List<ChainSpan> spans, RoadChain chain)
        {
            var cut = new List<ChainSpan>();
            foreach (ChainSpan span in spans)
            {
                foreach ((float s0, float s1) in BarrierAuto.CutAt(span.From, span.To, chain.Crossings))
                {
                    if (s1 - s0 >= MarkingRuns.MinLength)
                        cut.Add(new ChainSpan(s0, s1));
                }
            }

            return cut;
        }

        /// <summary>The settings of a run's item: a marking's place along the whole line, a barrier's along its own part (RunItem.PartLength).</summary>
        private string WithRunOf(GuideTemplate template, string spec, RunItem item) =>
            item.PartLength > 0f ? MarkingTemplates.WithRun(template, spec, item.Path, item.PartStart, item.PartLength) : MarkingTemplates.WithRun(template, spec, item.Path, item.Start, m_RunTotal);

        /// <summary>A line along the side of the carriageway (an edge line, a kerb line): it goes on along a side of a junction no other road joins.</summary>
        private static bool IsSideLine(string id) => id == "mk.line.edge" || MarkingTemplates.IsKerbLine(id);

        /// <summary>The outer kerbs of segments on one side of a line (0.12.49): those whose middle is at a lateral (lateralOf) of the sign of side; the other side's are as parallel and would be as near.</summary>
        private List<KerbLine> KerbFeet(IEnumerable<Entity> edges, Func<Vec3, float> lateralOf, float side)
        {
            var feet = new List<KerbLine>();
            foreach (Entity e in edges)
            {
                foreach (KerbLine l in KerbReader.Lines(EntityManager, e, false))
                {
                    if (lateralOf(l.Foot[l.Foot.Count / 2]) * side > 0f)
                        feet.Add(l);
                }
            }

            return feet;
        }

        /// <summary>A distance along a segment moved out of the part of a junction at either end into where the segment's lanes are (TryDrawnSpan); as it is at a plain node or outside them.</summary>
        private float JunctionStart(Entity edge, ArcTable arc, float s)
        {
            // TryDrawnSpan is false for a segment that is not a live edge, so it has its ends.
            if (!RoadReader.TryDrawnSpan(EntityManager, edge, arc, out float s0, out float s1))
                return s;
            Game.Net.Edge ends = EntityManager.GetComponentData<Game.Net.Edge>(edge);
            if (s < s0 && RoadReader.IsJunction(EntityManager, ends.m_Start))
                return s0;
            if (s > s1 && RoadReader.IsJunction(EntityManager, ends.m_End))
                return s1;
            return s;
        }

        /// <summary>
        /// The start or the end of a run drawn to what it should meet: a barrier's to the end of a barrier of its kind that is there on its line, else to the edge of a junction, within RunJoins.Reach
        /// (0.12.60: kRunEndSnap, 1.5 m, was all of it, and a second line of a wall started where it was clicked, over the first's last 9 m); a marking's to the edge of a junction within kRunEndSnap.
        /// </summary>
        private float SnapRunEnd(List<ChainSpan> spans, float s)
        {
            if (!m_RunBarrier)
                return RoadChain.Snap(spans, s, kRunEndSnap);
            float joined = RunJoins.Snap(m_RunJoins, s);
            return float.IsNaN(joined) ? RoadChain.Snap(spans, s, RunJoins.Reach) : joined;
        }

        /// <summary>The runs of this kind that are placed, with their paths: what a new one's start and end are drawn to.</summary>
        private static IEnumerable<RunJoins.Item> PlacedRuns(SignDef def)
        {
            ChunkSystem chunks = ChunkSystem.Instance;
            if (chunks == null || def.Template == null)
                yield break;
            foreach (ItemRecord record in chunks.Records.Values)
            {
                if (record.TypeHash != def.Hash)
                    continue;
                string path = def.Template.Parse(record.Spec ?? "").Get(RunPath.Key);
                if (!string.IsNullOrEmpty(path))
                    yield return new RunJoins.Item { Path = path, Position = record.Position, Yaw = record.Yaw };
            }
        }

        /// <summary>A distance along a curve, moved to the start or the end of it when it is within kRunEndSnap of it.</summary>
        private static float SnapToEnds(ArcTable arc, float s)
        {
            if (s < kRunEndSnap)
                return 0f;
            if (arc.Length - s < kRunEndSnap)
                return arc.Length;
            return s;
        }

        private static Vec3 PointOn(ArcTable arc, float s, float lateral, float surface)
        {
            float t = arc.TAt(s);
            Vec3 p = arc.Curve.Point(t) + arc.Curve.Tangent(t).HeadingXZ().RightXZ() * lateral;
            return new Vec3(p.X, p.Y + surface, p.Z);
        }

        // ------------------------------------------------------------------ preview and overlay

        /// <summary>The preview of a marking of one piece: its paint, where it will be (the item's frame, turned and moved as the item will be).</summary>
        private void ShowStampPreview(SignDef def, string spec, Vec3 position, float yaw)
        {
            // An area marking is shown as it will be drawn: fitted to the road under it (0.12.4), so its preview is made again when it moves half a metre or turns.
            bool fitted = MarkingTemplates.WantsFit(def, spec);
            string key = "mark|" + def.Hash + "|" + spec + "|" + MarkingLetters.Version
                + (fitted ? $"|{Mathf.Round(position.X * 2f)}|{Mathf.Round(position.Y * 2f)}|{Mathf.Round(position.Z * 2f)}|{Mathf.Round(yaw * 100f)}" : "");
            if (m_Preview == null || m_PreviewKey != key)
            {
                DestroyPreview();
                MarkingPlan plan = MarkingTemplates.PlanOf(def, spec, false);
                m_Preview = CreatePreviewRoot();
                PreviewFaceKeys = new string[0];
                AreaFit fit = fitted ? PreviewFit(def, spec, position, yaw) : null;
                if (plan != null)
                {
                    foreach (MarkingPart part in plan.Parts)
                        AddPartRenderer(ChunkBuilder.PartMaterial(part), fit != null && !part.Solid.HasValue ? fit.Drape(part.Mesh) : part.Mesh);
                }

                m_PreviewKey = key;
            }

            m_Preview.SetActive(true);
            m_Preview.transform.SetPositionAndRotation(new Vector3(position.X, position.Y, position.Z), Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f));
        }

        /// <summary>The preview of a line that runs along a road: all its items' paint in one mesh for each colour, in world coordinates (the preview object stays at the origin).</summary>
        private void BuildRunPreview(SignDef def, GuideTemplate template, string spec, List<RunItem> items)
        {
            m_Preview = CreatePreviewRoot();
            PreviewFaceKeys = new string[0];
            var materials = new Dictionary<string, MeshBuilder>();
            foreach (RunItem item in items)
            {
                MarkingPlan plan = MarkingTemplates.PlanOf(template, WithRunOf(template, spec, item), false);
                if (plan == null)
                    continue;
                foreach (MarkingPart part in plan.Parts)
                {
                    string material = ChunkBuilder.PartMaterial(part);
                    if (!materials.TryGetValue(material, out MeshBuilder mesh))
                        materials[material] = mesh = new MeshBuilder();
                    mesh.Append(part.Mesh, item.Position, item.Yaw);
                }
            }

            foreach (KeyValuePair<string, MeshBuilder> pair in materials)
                AddPartRenderer(pair.Key, pair.Value);
        }

        /// <summary>A part of a marking's preview in its material (ChunkBuilder.PartMaterial): paint above the road by the placed marking's height (ChunkBuilder.MarkingRaise), a barrier's solid on it.</summary>
        private GameObject AddPartRenderer(string materialName, MeshBuilder mesh)
        {
            Material material = TsMaterials.OfBody(materialName);
            if (material == null || mesh.VertexCount == 0)
                return null;
            bool paint = ChunkBuilder.IsMarkingMaterial(materialName);
            return AddPreviewRenderer(paint ? "paint" : "solid", mesh, paint ? "CDLTS preview paint" : "CDLTS preview solid", material, paint ? ChunkBuilder.MarkingRaise : 0f);
        }

        private Game.Net.SearchSystem m_FitNetSearch;
        private Game.Prefabs.PrefabSystem m_FitPrefabs;
        private Game.Simulation.TerrainSystem m_FitTerrain;

        /// <summary>The fit of an area marking to the road for the preview (AreaFitSystem.Compute); null when it cannot be made.</summary>
        private AreaFit PreviewFit(SignDef def, string spec, Vec3 position, float yaw)
        {
            try
            {
                m_FitNetSearch = m_FitNetSearch ?? World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
                m_FitPrefabs = m_FitPrefabs ?? World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
                m_FitTerrain = m_FitTerrain ?? World.GetOrCreateSystemManaged<Game.Simulation.TerrainSystem>();
                AreaFit fit = AreaFitSystem.Compute(EntityManager, m_FitNetSearch, m_FitPrefabs, m_FitTerrain, def, spec, position, yaw, out _, out _, out _, out _);
                if (m_WantText && fit != null)
                    m_FitNote = fit.Note;
                return fit;
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Tool: the preview's fit to the road failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>What the last fit of a preview was made from (for the line of what the tool sees).</summary>
        private string m_FitNote = "";

        /// <summary>The ring where a marking goes, an arrow along the way its traffic runs (a marking of one piece), and the line of a run (or its parts, cut at the junctions it crosses).</summary>
        private void DrawMarkingOverlay(Vec3 position, float yaw, bool valid, List<Vec3> line, List<List<Vec3>> parts = null)
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            var colour = valid ? new UnityEngine.Color(0.2f, 1f, 0.35f, 0.9f) : new UnityEngine.Color(1f, 0.2f, 0.15f, 0.9f);
            var at = new float3(position.X, position.Y + 0.1f, position.Z);
            buffer.DrawCircle(colour, at, 0.6f);
            if (line == null && parts == null)
            {
                var look = new float3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * 1.6f;
                buffer.DrawLine(colour, new Line3.Segment(at, at + look), 0.12f);
                return;
            }

            var lines = parts ?? new List<List<Vec3>> { line };
            foreach (List<Vec3> each in lines)
            {
                for (int i = 1; i < each.Count; i++)
                {
                    var a = new float3(each[i - 1].X, each[i - 1].Y + 0.1f, each[i - 1].Z);
                    var b = new float3(each[i].X, each[i].Y + 0.1f, each[i].Z);
                    buffer.DrawLine(colour, new Line3.Segment(a, b), 0.1f);
                }
            }

            List<Vec3> first = lines.Count > 0 ? lines[0] : null;
            if (m_RunActive && first != null && first.Count > 0)
                buffer.DrawCircle(new UnityEngine.Color(0.35f, 0.8f, 1f, 0.95f), new float3(first[0].X, first[0].Y + 0.1f, first[0].Z), 0.5f);
        }
    }
}
