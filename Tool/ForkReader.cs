using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;
using Bezier3 = CDLC.TS.Snap.Bezier3;

namespace CDLC.TS
{
    /// <summary>What ForkReader found at a junction near the cursor: a fork where a ramp leaves (an exit) or joins (an entry) a carriageway, with the edges of the two carriageways on the gore's side.</summary>
    internal sealed class ForkRead
    {
        public bool Found;

        /// <summary>Why there is no fork (for the panel's line and the log).</summary>
        public string Why = "";

        public bool Exit;
        public Entity Node, MainEdge, RampEdge, SingleEdge;
        public Vec3 NodePosition;

        /// <summary>+1 when the ramp is to the right of the through carriageway looking away from the junction, -1 to the left; and whether it is to the right of the way the traffic runs.</summary>
        public int RampSideAway;
        public bool RampOnRight;

        /// <summary>The through carriageway's edge and the ramp's on the gore's side, from the junction outward (GoreLayout.Solve takes them so), and the third road's centre line the way its traffic runs.</summary>
        public List<Vec3> MainAway = new List<Vec3>(), RampAway = new List<Vec3>(), SingleCentre = new List<Vec3>();

        /// <summary>The edges came from the game's road geometry (the sides of the road as it is drawn); false: from the centre line and the width of the road's cross-section.</summary>
        public bool FromGeometry;

        public int MainLanes, RampLanes, SingleLanes;

        /// <summary>What was read, for the log (the first readings of a session are written: it is the probe of how the game keeps a fork).</summary>
        public string Report = "";
    }

    /// <summary>
    /// Reads a fork of the road network for the gore of 线65 (Fig. 94): at the junction nearest the cursor on the road under it, three roads that each run one way, one going in and two going out (an exit)
    /// or two in and one out (an entry). The through carriageway is the one of the two with more lanes (the one that goes on most straight through the junction when they have as many), the other is the ramp.
    /// The edges on the gore's side are the game's own sides of each road's pieces (EdgeGeometry: the start and the end half of the edge, each with its left and right curve, along the edge's direction),
    /// or, when a road has none, its centre line moved out by half its width. Read only. Main thread.
    /// </summary>
    internal static class ForkReader
    {
        private const int kSamples = 12;

        private sealed class Arm
        {
            public Entity Edge;
            public bool Away;
            public int Forward, Backward;
            public float Width;
            public Bezier3 Curve;
            public List<Vec3> CentreAway = new List<Vec3>();
            public Vec3 HeadingAway;
            public bool Outgoing => Away ? Forward > 0 && Backward == 0 : Backward > 0 && Forward == 0;
            public bool Incoming => Away ? Backward > 0 && Forward == 0 : Forward > 0 && Backward == 0;
            public int Lanes => Math.Max(Forward, Backward);
        }

        private static Vec3 V(float3 v) => new Vec3(v.x, v.y, v.z);

        private static Bezier3 B(Bezier4x3 b) => new Bezier3(V(b.a), V(b.b), V(b.c), V(b.d));

        private static void Sample(List<Vec3> into, Bezier3 curve, bool skipFirst)
        {
            for (int i = skipFirst ? 1 : 0; i <= kSamples; i++)
                into.Add(curve.Point(i / (float)kSamples));
        }

        private static string F(Vec3 p) => string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", p.X, p.Y, p.Z);

        private static bool IsRoad(EntityManager em, Entity e) => RoadReader.IsLiveEdge(em, e) && em.HasComponent<Road>(e) && !em.HasComponent<Game.Tools.Temp>(e);

        /// <summary>The fork at the junction of the road under the cursor that is nearest to it, within reach (m) of the cursor; Found false and Why when there is none.</summary>
        internal static ForkRead Find(EntityManager em, Entity edge, Vec3 cursor, float reach)
        {
            var read = new ForkRead();
            try
            {
                Read(em, edge, cursor, reach, read);
            }
            catch (Exception ex)
            {
                // Whatever the game's data does, placing a marking must not fail because of it.
                read.Found = false;
                read.Why = "reading failed: " + ex.GetType().Name;
                read.Report = "Fork read failed: " + ex;
            }

            return read;
        }

        private static void Read(EntityManager em, Entity edge, Vec3 cursor, float reach, ForkRead read)
        {
            if (!IsRoad(em, edge) || !em.TryGetComponent(edge, out Edge ends))
            {
                read.Why = "no road under the cursor";
                return;
            }

            // The junction of this road nearest to the cursor.
            Entity node = Entity.Null;
            float best = float.MaxValue;
            foreach (Entity n in new[] { ends.m_Start, ends.m_End })
            {
                if (n == Entity.Null || !em.TryGetComponent(n, out Node data))
                    continue;
                float d = Vec3.DistanceXZ(V(data.m_Position), cursor);
                if (d < best)
                {
                    best = d;
                    node = n;
                    read.NodePosition = V(data.m_Position);
                }
            }

            if (node == Entity.Null || best > reach)
            {
                read.Why = node == Entity.Null ? "the road has no junction" : string.Format(CultureInfo.InvariantCulture, "the nearest junction is {0:0} m away", best);
                return;
            }

            read.Node = node;
            if (!em.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> connected))
            {
                read.Why = "the junction has no roads";
                return;
            }

            var arms = new List<Arm>();
            var forward = new List<float>();
            var backward = new List<float>();
            for (int i = 0; i < connected.Length; i++)
            {
                Entity e = connected[i].m_Edge;
                if (!IsRoad(em, e) || !RoadReader.EndsAt(em, e, node) || !em.TryGetComponent(e, out Edge eEnds) || !RoadReader.TryRead(em, e, out Bezier3 curve, out float width))
                    continue;
                var arm = new Arm { Edge = e, Away = eEnds.m_Start == node, Width = width, Curve = curve };
                RoadReader.TryLaneLaterals(em, e, forward, backward);
                arm.Forward = MarkingSnap.Merged(forward).Count;
                arm.Backward = MarkingSnap.Merged(backward).Count;
                Sample(arm.CentreAway, curve, false);
                if (!arm.Away)
                    arm.CentreAway.Reverse();
                // The heading away from the junction a few metres out (the first piece of a curve that bends hard at the junction says little).
                arm.HeadingAway = (arm.CentreAway[Math.Min(3, arm.CentreAway.Count - 1)] - arm.CentreAway[0]).HeadingXZ();
                arms.Add(arm);
            }

            var report = new StringBuilder();
            report.Append($"Fork probe: node {node.Index}:{node.Version} at {F(read.NodePosition)}, {arms.Count} road(s):");
            foreach (Arm a in arms)
                report.Append($" [edge {a.Edge.Index}:{a.Edge.Version} {(a.Away ? "starts" : "ends")} here, lanes {a.Forward} with / {a.Backward} against, width {a.Width:0.0} m, heading away {F(a.HeadingAway)}]");

            if (arms.Count != 3)
            {
                read.Why = $"the junction has {arms.Count} roads, not 3";
                read.Report = report.ToString();
                return;
            }

            List<Arm> outgoing = arms.FindAll(a => a.Outgoing), incoming = arms.FindAll(a => a.Incoming);
            if (outgoing.Count + incoming.Count != 3)
            {
                read.Why = "a road at the junction runs both ways";
                read.Report = report.ToString();
                return;
            }

            if (incoming.Count == 1 && outgoing.Count == 2)
                read.Exit = true;
            else if (incoming.Count == 2 && outgoing.Count == 1)
                read.Exit = false;
            else
            {
                read.Why = $"{incoming.Count} road(s) come in and {outgoing.Count} go out: not an exit or an entry";
                read.Report = report.ToString();
                return;
            }

            Arm single = read.Exit ? incoming[0] : outgoing[0];
            List<Arm> pair = read.Exit ? outgoing : incoming;
            // The through carriageway: more lanes; as many, the one that goes on straightest from the single road (its heading away against the single's).
            Arm main = pair[0], ramp = pair[1];
            float straight0 = -Vec3.Dot(pair[0].HeadingAway, single.HeadingAway), straight1 = -Vec3.Dot(pair[1].HeadingAway, single.HeadingAway);
            if (pair[1].Lanes > pair[0].Lanes || (pair[1].Lanes == pair[0].Lanes && straight1 > straight0))
            {
                main = pair[1];
                ramp = pair[0];
            }

            read.MainEdge = main.Edge;
            read.RampEdge = ramp.Edge;
            read.SingleEdge = single.Edge;
            read.MainLanes = main.Lanes;
            read.RampLanes = ramp.Lanes;
            read.SingleLanes = single.Lanes;
            read.RampSideAway = Vec3.Dot(ramp.HeadingAway, main.HeadingAway.RightXZ()) >= 0f ? 1 : -1;
            // Looking the way the traffic runs: for an exit that is away from the junction, for an entry toward it, and right and left change.
            read.RampOnRight = read.Exit ? read.RampSideAway > 0 : read.RampSideAway < 0;

            // The single road's centre line the way its traffic runs.
            read.SingleCentre = new List<Vec3>(single.CentreAway);
            if (read.Exit)
                read.SingleCentre.Reverse();

            // The edges on the gore's side: the main's on the ramp's side, the ramp's on the main's.
            bool fromGeometry = Side(em, main, read.RampSideAway, read.MainAway) & Side(em, ramp, -read.RampSideAway, read.RampAway);
            if (!fromGeometry)
            {
                read.MainAway.Clear();
                read.RampAway.Clear();
                Offset(main, read.RampSideAway, read.MainAway);
                Offset(ramp, -read.RampSideAway, read.RampAway);
            }

            read.FromGeometry = fromGeometry;
            read.Found = read.MainAway.Count >= 2 && read.RampAway.Count >= 2;
            if (!read.Found)
                read.Why = "the roads' edges could not be read";

            report.Append($" -> {(read.Exit ? "exit" : "entry")}: main edge {main.Edge.Index} ({main.Lanes} lanes), ramp edge {ramp.Edge.Index} ({ramp.Lanes} lanes) on the {(read.RampOnRight ? "right" : "left")} of the traffic; the single road edge {single.Edge.Index}.");
            report.Append($" Sides from {(fromGeometry ? "EdgeGeometry" : "the centre line and half the width")}: main {F(read.MainAway[0])} .. {F(read.MainAway[read.MainAway.Count - 1])}, ramp {F(read.RampAway[0])} .. {F(read.RampAway[read.RampAway.Count - 1])}.");
            // For comparison, the other way of reading them, and what the game keeps for the junction's part of each road (the node geometry: not used yet; its sides' ends are logged to learn which is which).
            var altMain = new List<Vec3>();
            var altRamp = new List<Vec3>();
            Offset(main, read.RampSideAway, altMain);
            Offset(ramp, -read.RampSideAway, altRamp);
            report.Append($" From the centre lines: main {F(altMain[0])} .. {F(altMain[altMain.Count - 1])}, ramp {F(altRamp[0])} .. {F(altRamp[altRamp.Count - 1])}.");
            foreach (Arm a in new[] { main, ramp, single })
                report.Append(" " + NodeGeometry(em, a));
            read.Report = report.ToString();
        }

        /// <summary>One side of an arm from the game's geometry of its edge, away from the junction: sideAway +1 the right looking away from it. False when the edge has none.</summary>
        private static bool Side(EntityManager em, Arm arm, int sideAway, List<Vec3> into)
        {
            if (!em.TryGetComponent(arm.Edge, out EdgeGeometry geometry))
                return false;
            if (!(geometry.m_Start.m_Length.x > 0f || geometry.m_Start.m_Length.y > 0f) && !(geometry.m_End.m_Length.x > 0f || geometry.m_End.m_Length.y > 0f))
                return false;
            // Along the edge's own direction the right is the right; seen from an end node looking away, the edge runs the other way and right is its left.
            bool right = arm.Away ? sideAway > 0 : sideAway < 0;
            var line = new List<Vec3>();
            Sample(line, B(right ? geometry.m_Start.m_Right : geometry.m_Start.m_Left), false);
            Sample(line, B(right ? geometry.m_End.m_Right : geometry.m_End.m_Left), true);
            if (!arm.Away)
                line.Reverse();
            foreach (Vec3 p in line)
            {
                if (!p.IsFinite)
                    return false;
            }

            into.AddRange(line);
            return into.Count >= 2;
        }

        /// <summary>One side of an arm from its centre line moved out by half its width, away from the junction.</summary>
        private static void Offset(Arm arm, int sideAway, List<Vec3> into)
        {
            into.AddRange(MarkingFills.Offset(arm.CentreAway, sideAway * arm.Width * 0.5f));
        }

        /// <summary>The ends of the curves the game keeps for the junction's part of an arm (StartNodeGeometry or EndNodeGeometry), for the log.</summary>
        private static string NodeGeometry(EntityManager em, Arm arm)
        {
            EdgeNodeGeometry g;
            if (arm.Away)
            {
                if (!em.TryGetComponent(arm.Edge, out StartNodeGeometry start))
                    return $"[edge {arm.Edge.Index}: no StartNodeGeometry]";
                g = start.m_Geometry;
            }
            else
            {
                if (!em.TryGetComponent(arm.Edge, out EndNodeGeometry end))
                    return $"[edge {arm.Edge.Index}: no EndNodeGeometry]";
                g = end.m_Geometry;
            }

            return $"[edge {arm.Edge.Index} node part: left.left {F(V(g.m_Left.m_Left.a))}->{F(V(g.m_Left.m_Left.d))}, right.right {F(V(g.m_Right.m_Right.a))}->{F(V(g.m_Right.m_Right.d))}, middle {F(V(g.m_Middle.a))}->{F(V(g.m_Middle.d))}, radius {g.m_MiddleRadius:0.00}]";
        }
    }
}
