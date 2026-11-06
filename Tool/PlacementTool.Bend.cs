using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Unity.Entities;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// The markings of one piece that lie along the road (0.12.49, MarkingTemplates.Bends; the owner: "停车线/公交岛划线可以跟随弯道/用线吸附放置"): parking bays, the bus stops, the no-parking lines and the
    /// distance chevrons. Their line is found where the cursor is: on the road's kerb when it is near (the bays' kerb side on it, a bus stop's kerb its width away from its lane-side line), else on the
    /// lane line or the edge near the cursor, a chevron in its lane; it follows the road through its plain nodes (ChainReader, not across junctions) and, on a kerb, the kerb's foot where the game draws
    /// it (Kerbs.SnapToFeet). One click puts the marking there bent along that line (MarkingBend); drawn along the road (MarkingBend.Drawn) the first click sets the start, the second the end, and the
    /// marking is as long as the line (as many bays as it holds). The arrow keys move the line toward the kerb side of the marking and away, as they move a line along the road.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>A bent marking's kerb side goes on the road's kerb when the cursor is within this (m) of where it would be.</summary>
        private const float kBendKerbReach = 1.5f;

        /// <summary>The line of a bent marking drawn along the road was started by this tool (m_RunActive is shared with the lines along the road).</summary>
        private bool m_BendStarted;

        /// <summary>The started bent marking: its line on the kerb (and the kerb's lateral on the chain) or on the line at m_RunLateral, and the way it heads along the chain (+1 with it).</summary>
        private bool m_BendOnKerb;
        private float m_BendKerbLateral;
        private int m_BendHeading;

        /// <summary>The chain under the cursor, read again only when the segment there changes; and the kerbs of one side of it (KerbFeet), read again when the chain or the side changes.</summary>
        private ChainRead m_BendChain;
        private Entity m_BendChainEdge;
        private ChainRead m_BendFeetChain;
        private int m_BendFeetSide;
        private List<KerbLine> m_BendFeet;

        /// <summary>Where a bent marking lies across the road at a place of the chain (BendLine).</summary>
        private struct BendSpot
        {
            public float S, Lateral, KerbLateral;
            public bool OnKerb;
            public int Heading;
        }

        private ChainRead BendChain(Entity edge)
        {
            if (m_BendChain == null || m_BendChainEdge != edge || m_BendChain.Edges.Exists(e => !RoadReader.IsLiveEdge(EntityManager, e)))
            {
                m_BendChain = ChainReader.ReadEachWay(EntityManager, edge, false);
                m_BendChainEdge = edge;
            }

            return m_BendChain;
        }

        /// <summary>
        /// Where the line of a bent marking is at the cursor: on the kerb of its side when the cursor is within kBendKerbReach of where the marking's kerb side would be on it, else on the lane line near
        /// the cursor (its kerb side outward), a chevron in the middle of the lane, heading with that lane's traffic.
        /// </summary>
        private BendSpot SpotAt(RoadChain chain, Entity edge, EdgeInfo info, Vec3 cursorV, float kerbAt)
        {
            ChainPiece clicked = chain.Pieces.Find(p => p.Id == edge.Index);
            var spot = new BendSpot { S = chain.ProjectLevel(cursorV, out float lateral, clicked?.S0 ?? float.MinValue, clicked?.S1 ?? float.MaxValue) };
            ChainPiece piece = chain.Pieces[chain.PieceAt(spot.S)];
            if (float.IsNaN(kerbAt))
            {
                MarkingSpot lane = MarkingSnap.Stamp(MarkingAnchor.Lane, piece.With, piece.Against, lateral);
                spot.Lateral = lane.Lateral;
                spot.Heading = lane.Forward ? 1 : -1;
                return spot;
            }

            Bezier3 curve = info.Curve;
            float t = curve.ClosestT(cursorV);
            Vec3 onCentre = curve.Point(t), right = curve.Tangent(t).HeadingXZ().RightXZ();
            if (RoadReader.CompositionKerb(EntityManager, edge, Vec3.Dot(cursorV - onCentre, right), out float kerbOnEdge, out _))
            {
                chain.Project(onCentre + right * kerbOnEdge, out float kerb);
                if (Mathf.Abs(lateral - kerb) <= kerbAt + kBendKerbReach)
                {
                    spot.OnKerb = true;
                    spot.KerbLateral = kerb;
                    spot.Heading = kerb >= 0f ? 1 : -1;
                    spot.Lateral = kerb - spot.Heading * kerbAt;
                    return spot;
                }
            }

            MarkingSpot line = MarkingSnap.Line(piece.With, piece.Against, lateral);
            spot.Lateral = line.Lateral;
            spot.Heading = line.Lateral >= 0f ? 1 : -1;
            return spot;
        }

        /// <summary>
        /// The line a bent marking is laid along, in the world, from s heading (+1 with the chain) for length: on the kerb (snapped to its foot, the nudge outward) and moved the kerb's offset into the
        /// road, or at the spot's lateral moved by the nudge toward the marking's kerb side. Empty when the chain has not that much road.
        /// </summary>
        private List<Vec3> BendLine(ChainRead read, BendSpot spot, int heading, float s, float length, float kerbAt, float surface)
        {
            RoadChain chain = read.Chain;
            float s1 = s + heading * length;
            if (!spot.OnKerb)
                return chain.Line(s, s1, spot.Lateral + spot.Heading * m_RunNudge, surface);
            List<Vec3> kerb = chain.Line(s, s1, spot.KerbLateral, surface);
            if (kerb.Count < 2)
                return kerb;
            int side = spot.KerbLateral >= 0f ? 1 : -1;
            if (m_BendFeet == null || m_BendFeetChain != read || m_BendFeetSide != side)
            {
                m_BendFeet = KerbFeet(read.Edges, p =>
                {
                    chain.Project(p, out float l);
                    return l;
                }, side);
                m_BendFeetChain = read;
                m_BendFeetSide = side;
            }

            if (m_BendFeet.Count > 0)
                kerb = Kerbs.SnapToFeet(kerb, m_BendFeet, m_RunNudge, Kerbs.SnapReach + Mathf.Abs(m_RunNudge));
            // The kerb is on the marking's right as it heads (on its left when it was turned round): its line is kerbAt to the other side.
            return kerbAt > 0f ? MarkingFills.Offset(kerb, heading == spot.Heading ? -kerbAt : kerbAt) : kerb;
        }

        private void UpdateBend(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity edge, EdgeInfo info, bool onRoad)
        {
            GuideSpec draft = template.Parse(MarkingSpec(template, info, onRoad)).With(MarkingBend.Key, "").With(MarkingBend.FillKey, "");
            bool drawn = MarkingTemplates.DrawsAlong(template, draft);
            float kerbAt = MarkingTemplates.KerbAt(template, draft);
            if (m_RunActive && (!drawn || m_RunRead?.Chain == null || m_RunRead.Edges.Exists(e => !RoadReader.IsLiveEdge(EntityManager, e))))
                ResetRun();
            if (!m_RunActive && !onRoad)
            {
                HidePreview();
                SetHint("mark-run-road", 0, 0f, 0f, 0f, false, false, "", true);
                m_Outcome = "ignored: a marking that lies along the road needs a road under the cursor";
                return;
            }

            ChainRead read;
            BendSpot spot;
            int heading;
            float start, length;
            float surface = onRoad ? hit.m_HitPosition.y - hit.m_Position.y : m_RunSurface;
            if (m_RunActive)
            {
                // Drawn: from the start to where the cursor is along the chain; the marking heads the way its kerb side needs, from whichever end that leaves.
                read = m_RunRead;
                spot = new BendSpot { S = m_RunStart, Lateral = m_RunLateral, KerbLateral = m_BendKerbLateral, OnKerb = m_BendOnKerb, Heading = m_BendHeading };
                heading = spot.Heading * (m_Flip ? -1 : 1);
                float end = read.Chain.ProjectLevel(cursorV, out _);
                length = Mathf.Abs(end - m_RunStart);
                start = (end - m_RunStart) * heading >= 0f ? m_RunStart : end;
                surface = m_RunSurface;
            }
            else
            {
                read = BendChain(edge);
                if (read.Chain == null || read.Chain.Pieces.Count == 0)
                {
                    HidePreview();
                    m_Outcome = "refused: the road could not be read";
                    return;
                }

                spot = SpotAt(read.Chain, edge, info, cursorV, kerbAt);
                heading = spot.Heading * (m_Flip ? -1 : 1);
                start = spot.S;
                // One click: as long as the marking is along the road, and a metre more so that its far end is not on the line's last turn.
                MarkingPlan straight = MarkingTemplates.PlanOf(template, draft.ToString());
                length = drawn ? 0f : (straight != null ? straight.MaxZ : 0f) + 1f;
            }

            List<Vec3> line = length >= MarkingRuns.MinLength ? BendLine(read, spot, heading, start, length, kerbAt, surface) : new List<Vec3>();
            if (!m_RunActive && drawn)
            {
                // The start of a marking drawn along the road: the ring where it starts and a short stretch of its line.
                List<Vec3> stub = BendLine(read, spot, heading, spot.S, 6f, kerbAt, surface);
                Vec3 ring = stub.Count > 0 ? stub[0] : read.Chain.PointAt(spot.S, spot.Lateral, surface);
                HidePreview();
                DrawMarkingOverlay(ring, 0f, true, stub);
                SetHint("mark-bend-start", 0, 0f, 0f, m_RunNudge);
                if (click)
                {
                    m_RunActive = true;
                    m_BendStarted = true;
                    m_RunRead = read;
                    m_RunEdge = edge;
                    m_RunStart = spot.S;
                    m_RunLateral = spot.Lateral;
                    m_RunSurface = surface;
                    m_BendOnKerb = spot.OnKerb;
                    m_BendKerbLateral = spot.KerbLateral;
                    m_BendHeading = spot.Heading;
                    m_Outcome = $"marking along the road started on road {edge.Index}:{edge.Version} at {spot.S:0.0} m, {spot.Lateral:+0.00;-0.00} m from the centre line{(spot.OnKerb ? " (on the kerb)" : "")}";
                    Mod.log.Info("Tool: " + m_Outcome + ".");
                }

                return;
            }

            Vec3 position;
            float yaw;
            string spec = draft.ToString();
            if (line.Count >= 2)
            {
                string bend = MarkingBend.Encode(line, out yaw);
                position = line[0];
                GuideSpec bent = template.Parse(spec).With(MarkingBend.Key, bend);
                if (drawn)
                    bent = bent.With(MarkingBend.FillKey, Mathf.RoundToInt(length * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture));
                spec = bent.ToString();
            }
            else
            {
                position = read.Chain.PointAt(start, spot.Lateral, surface);
                Vec3 ahead = read.Chain.HeadingAt(start) * heading;
                yaw = Mathf.Atan2(ahead.X, ahead.Z);
            }

            spec = MarkingTemplates.WithGround(template, spec, GroundUnder(def, spec, position, yaw, false));
            Entity startEdge = read.EdgeAt(start);
            bool valid = line.Count >= 2 && (!drawn || length >= MarkingRuns.MinLength);
            var records = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records : null;
            if (records != null && (records.Count >= kMaxItems || NearestSameHere(position, def.Hash) < kSameMarking))
                valid = false;
            if (m_WantText)
                Debug = $"marking {def.Id} along road {startEdge.Index}:{startEdge.Version} from {start:0.0} m for {length:0.0} m, {spot.Lateral:+0.00;-0.00} m from the centre line{(spot.OnKerb ? " (on the kerb)" : "")}" + (valid ? "" : " [cannot place]");
            if (drawn)
                SetHint("mark-bend-end", 0, length, 0f, m_RunNudge, false, false, "", !valid);
            else
                SetHint("mark-bend", 0, 0f, 0f, m_RunNudge, false, false, "", !valid);
            ShowStampPreview(def, spec, position, yaw);
            DrawMarkingOverlay(position, yaw, valid, line);
            if (!click)
                return;
            if (!valid)
            {
                m_Outcome = "refused: the marking along the road has no room here";
                return;
            }

            float edgeT = RoadReader.IsLiveEdge(EntityManager, startEdge) ? EdgeInfo.Of(EntityManager, startEdge).Curve.ClosestT(position) : 0f;
            Place(def, spec, 0u, 0f, position, yaw, startEdge, edgeT, $"marking {def.Id} along the road, {length:0.0} m");
            m_Outcome = $"placed {def.Id} along the road for {length:0.0} m{(spot.OnKerb ? " on the kerb" : "")}";
            if (m_RunActive)
                ResetRun();
        }
    }
}
