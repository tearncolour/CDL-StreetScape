using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Mathematics;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// A sign placed along a line (0.12.69, SignLine; the owner, of 警46 线形诱导标: "这个应该可以线性摆放"). With the sign's setting "线性摆放" on, a click that would place the sign on a post beside a road
    /// sets where the line starts instead (what the click would have made is kept: the support, its reach, the settings, the side of the road), and the cursor then sets the end along the road it was
    /// started on, through the nodes (the chain the lines of markings follow, RunPath's: ChainReader); the second click places a sign at the start and at every spacing toward the end, each on its own
    /// post made as a click at that place makes it (SignLine.PostSnap), all in one step of the history. A right click or Esc cancels the line (CancelRun). It shares the run's state with the lines of
    /// markings (m_RunActive, m_RunRead, m_RunStart, m_RunSpans, ResetRun). Only a sign on a post of its own makes a line (SignLine.CanStand). Main thread; the plan is made again only when the end has moved
    /// half a metre or the spacing changed.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>The run being drawn is a line of signs (and the settings below are of it).</summary>
        private bool m_LineSign;

        /// <summary>What the first click made, kept for every sign of the line: its settings (the line settings put back), its support and how far its post stands from the road, its height by hand.</summary>
        private string m_LineSpec = "";
        private uint m_LineSupportHash;
        private float m_LineInner, m_LineOut, m_LineHeight = float.NaN;

        /// <summary>The side of the road's chain the line is on: +1 the right of the way the chain runs, -1 the left.</summary>
        private int m_LineSide = 1;

        /// <summary>One sign of the line: where it stands, which way it faces, the segment it is on and where on it.</summary>
        private struct LineSign
        {
            public Vec3 Position;
            public float Yaw;
            public Entity Edge;
            public float EdgeT;
        }

        private readonly List<LineSign> m_LineSigns = new List<LineSign>();
        private string m_LineKey = "";
        private float m_LineGap, m_LineRadius, m_LineLength;
        private bool m_LineCapped;
        private int m_LineSkipped;

        /// <summary>Whether the click about to be made starts a line of signs and not a sign (the sign's setting is on, it is by a road, on a post of its own and not on a strip between the carriageways).</summary>
        private static bool StartsLine(SignDef def, string spec, SupportDef support, bool onRoad, bool hosted, string stripNote) =>
            onRoad && !hosted && stripNote.Length == 0 && SignLine.CanStand(support.Kind) && SignLine.On(def, spec);

        private void ResetLine()
        {
            m_LineSign = false;
            m_LineKey = "";
            m_LineSigns.Clear();
        }

        /// <summary>The first click: the line starts where the sign would have stood, on its side of the road.</summary>
        private void StartLine(SignDef def, string spec, uint supportHash, SupportLayout planned, float reachOut, float placeHeight, Entity edge, EdgeSnapResult snap, Vec3 position, float surface, string note)
        {
            ChainRead read = ChainReader.ReadEachWay(EntityManager, edge, true);
            if (read.Chain == null || read.Chain.Pieces.Count == 0)
            {
                m_Outcome = "refused: the road could not be read";
                return;
            }

            // On the segment clicked (0.12.52): the chain read both ways may pass over or under it elsewhere.
            ChainPiece clicked = read.PieceOf(edge);
            float chainS = read.Chain.ProjectLevel(position, out float chainLateral, clicked?.S0 ?? float.MinValue, clicked?.S1 ?? float.MaxValue);
            ChainPiece under = read.Chain.Pieces[read.Chain.PieceAt(chainS)];
            int side = clicked != null ? SignLine.EdgeSide(snap.Side, clicked.Reversed) : chainLateral >= 0f ? 1 : -1;
            // The signs stand along the side of the road as a barrier at the edge does: through a junction on a side no road joins, and not in the way of a crosswalk.
            List<ChainSpan> spans = CutAtCrossings(read.Chain.Spans(side * (under.HalfWidth + 1f), true), read.Chain);
            float snapped = SnapRunEnd(spans, chainS);
            m_RunActive = true;
            m_LineSign = true;
            m_RunEdge = edge;
            m_RunRead = read;
            m_RunSpans = spans;
            m_RunStart = float.IsNaN(snapped) ? chainS : snapped;
            m_RunLateral = chainLateral;
            m_RunSurface = surface;
            m_LineSide = side;
            m_LineSpec = SignLine.Stripped(def, spec);
            m_LineSupportHash = supportHash;
            m_LineStyle = m_PlaceStyleNow;
            m_LineInner = planned.InnerReach;
            m_LineOut = reachOut;
            m_LineHeight = placeHeight;
            m_LineKey = "";
            m_Outcome = $"line of signs started on road {edge.Index}:{edge.Version} ({read.Log}) at {m_RunStart:0.0} m, on its {(side > 0 ? "right" : "left")}; {note}";
            Mod.log.Info("Tool: " + m_Outcome + ".");
        }

        /// <summary>The line is started: its end follows the cursor along the road, the signs are shown, and the second click places them.</summary>
        private void UpdateLine(SignDef def, bool click)
        {
            m_PlaceStyleNow = m_LineStyle;
            RoadChain chain = m_RunRead?.Chain;
            if (chain == null || m_RunRead.Edges.Exists(e => !RoadReader.IsLiveEdge(EntityManager, e)))
            {
                Mod.log.Info("Tool: the road of the line of signs that was started is gone; the line is cancelled.");
                ResetRun();
                HidePreview();
                SetHint("road");
                Debug = "The road the line was started on is gone.";
                m_Outcome = "the road of the line is gone";
                return;
            }

            if (!GetRaycastResult(out _, out RaycastHit hit))
            {
                HidePreview();
                SetHint("road");
                m_Outcome = "ignored: the raycast found neither a road nor the ground under the cursor";
                return;
            }

            long lap = System.Diagnostics.Stopwatch.GetTimestamp();
            Vec3 cursorV = RoadReader.ToVec(hit.m_HitPosition);
            float raw = chain.ProjectLevel(cursorV, out _);
            float snappedEnd = SnapRunEnd(m_RunSpans, raw);
            float end = float.IsNaN(snappedEnd) ? raw : snappedEnd;
            // The end moves in steps of 0.5 m: the signs are worked out again only when it has (or the spacing, or the turn of the signs, has changed).
            float endStep = Mathf.Round(end * 2f) * 0.5f;
            float typed = SignLine.TypedGap(def, Draft);
            string key = $"{m_RunEdge.Index}:{m_RunEdge.Version}|{m_RunRead.Edges.Count}|{m_RunStart:0.00}|{endStep:0.0}|{typed:0.##}|{m_LineSpec}|{m_Rotation:0.00}|{m_Flip}|{Margin:0.00}";
            if (key != m_LineKey)
            {
                m_LineKey = key;
                PlanLine(def, chain, end, typed);
                SlowLog.Since("Tool: a line of signs (plan)", lap);
            }

            bool valid = m_LineSigns.Count > 0;
            string note = "";
            int count = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : 0;
            if (valid && count + m_LineSigns.Count > kMaxItems)
            {
                valid = false;
                note = "too many signs";
            }
            else if (!valid)
            {
                note = "no place for a sign";
            }

            if (m_WantText)
                Debug = $"line of signs: road {m_RunEdge.Index}:{m_RunEdge.Version} ({m_RunRead.Log}), {m_RunStart:0.0} m to {end:0.0} m = {m_LineLength:0.0} m, every {m_LineGap:0.#} m, {m_LineSigns.Count} sign(s)"
                    + (m_LineSkipped > 0 ? $", {m_LineSkipped} left out (another of the kind is there)" : "") + (m_LineCapped ? $", cut at {SignLine.MaxItems}" : "") + (valid ? "" : "; " + note + " [cannot place]");
            SetHint("line-end", m_LineSigns.Count, m_LineLength, m_LineGap, 0f, false, false, "", !valid, m_LineCapped ? SignLine.MaxItems : 0);

            long shown = System.Diagnostics.Stopwatch.GetTimestamp();
            if (m_LineSigns.Count > 0)
            {
                // The sign that is made at the end of the line, the whole of it: the others are marked on the road.
                LineSign last = m_LineSigns[m_LineSigns.Count - 1];
                ShowPreview(SignInstances.Of(def, m_LineSpec), m_LineSupportHash, 0f, last.Position, last.Yaw, m_LineHeight);
            }
            else
            {
                HidePreview();
            }

            SlowLog.Since("Tool: a line of signs (preview)", shown);
            DrawLineOverlay(valid, chain, end);
            if (!click)
                return;
            if (!valid)
            {
                m_Outcome = "refused: " + note;
                return;
            }

            foreach (LineSign sign in m_LineSigns)
                Place(def, m_LineSpec, m_LineSupportHash, 0f, sign.Position, sign.Yaw, sign.Edge, sign.EdgeT, "a sign of a line", 0, float.IsNaN(m_LineHeight) ? 0f : m_LineHeight);
            m_Outcome = $"placed a line of {m_LineSigns.Count} sign(s) over {m_LineLength:0.0} m, every {m_LineGap:0.#} m";
            Mod.log.Info("Tool: " + m_Outcome + $" from road {m_RunEdge.Index}:{m_RunEdge.Version} ({m_RunRead.Log}), {def.Id}; {(typed > 0f ? "spacing typed" : $"spacing automatic (tightest radius {(m_LineRadius >= float.MaxValue ? "none: straight" : m_LineRadius.ToString("0") + " m")}, borrowed from the delineators' Table 7)")}"
                + (m_LineSkipped > 0 ? $"; {m_LineSkipped} left out" : "") + (m_LineCapped ? $"; cut at {SignLine.MaxItems}" : "") + ".");
            ResetRun();
            DestroyPreview();
        }

        /// <summary>The signs of the line from its start to this end (made again when the key changes): the spacing, the stations along the chain, and for each the place a click there would give.</summary>
        private void PlanLine(SignDef def, RoadChain chain, float end, float typed)
        {
            m_LineSigns.Clear();
            m_LineLength = Mathf.Abs(end - m_RunStart);
            m_LineRadius = MarkingStuds.MinRadius(chain.Line(m_RunStart, end, 0f, 0f));
            m_LineGap = SignLine.Spacing(typed, m_LineRadius);
            List<float> stations = SignLine.Stations(m_RunSpans, m_RunStart, end, m_LineGap, SignLine.MaxItems, out m_LineCapped);
            m_LineSkipped = 0;
            foreach (float s in stations)
            {
                Entity edge = m_RunRead.EdgeAt(s);
                EdgeInfo info = EdgeInfo.Of(EntityManager, edge);
                if (!info.CurveOk)
                    continue;
                ChainPiece piece = chain.Pieces[chain.PieceAt(s)];
                int roadSide = SignLine.EdgeSide(m_LineSide, piece.Reversed);
                Carriageway carriageway = info.Carriageway(EntityManager);
                float postDistance = carriageway.PostDistance(roadSide, m_LineInner, Margin, m_LineOut);
                EdgeSnapResult snap = SignLine.PostSnap(info.Arc, info.Width, chain.PointAt(s, 0f), roadSide, postDistance, m_RunSurface);
                // A sign of the kind already stands there (a line drawn again over one): not a second one on top of it.
                if (def != null && NearestSameHere(snap.Position, def.Hash) < Spacing.MinDistance)
                {
                    m_LineSkipped++;
                    continue;
                }

                m_LineSigns.Add(new LineSign { Position = snap.Position, Yaw = snap.Yaw + (m_Flip ? Mathf.PI : 0f) + m_Rotation, Edge = edge, EdgeT = snap.T });
            }
        }

        /// <summary>A ring and the way it faces at each sign of the line, the start ringed in blue, and the way along the road to the end.</summary>
        private void DrawLineOverlay(bool valid, RoadChain chain, float end)
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            var colour = valid ? new UnityEngine.Color(0.2f, 1f, 0.35f, 0.9f) : new UnityEngine.Color(1f, 0.2f, 0.15f, 0.9f);
            foreach (LineSign sign in m_LineSigns)
            {
                var at = new float3(sign.Position.X, sign.Position.Y + 0.1f, sign.Position.Z);
                buffer.DrawCircle(colour, at, 0.45f);
                buffer.DrawLine(colour, new Line3.Segment(at, at + new float3(Mathf.Sin(sign.Yaw), 0f, Mathf.Cos(sign.Yaw)) * 1.2f), 0.08f);
            }

            Vec3 from = chain.PointAt(m_RunStart, 0f, m_RunSurface), to = chain.PointAt(end, 0f, m_RunSurface);
            buffer.DrawCircle(new UnityEngine.Color(0.35f, 0.8f, 1f, 0.95f), new float3(from.X, from.Y + 0.1f, from.Z), 0.7f);
            buffer.DrawCircle(colour, new float3(to.X, to.Y + 0.1f, to.Z), 0.7f);
        }
    }
}
