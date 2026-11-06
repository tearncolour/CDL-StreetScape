using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>What was read for a left-turn waiting area: the axis (world points of the leftmost waiting lane's turn), the waiting lanes, their width, the colour of the left line, the oncoming lanes.</summary>
    internal sealed class WaitingRead
    {
        public bool Ok;
        public string Why = "";
        public Entity Node;
        public List<Vec3> Axis = new List<Vec3>();
        public int Lanes;
        public float LaneWidth = 3.5f;
        public bool LeftYellow = true;

        /// <summary>The lanes of the oncoming traffic through the junction (world points) and their half widths: what the area must keep clear of.</summary>
        public readonly List<IList<Vec3>> Others = new List<IList<Vec3>>();
        public readonly List<float> HalfWidths = new List<float>();

        /// <summary>No oncoming traffic was found (a T junction): the length is cut by every other movement through the junction instead.</summary>
        public bool AllOthers;

        public string Log = "";
    }

    /// <summary>
    /// Reads a junction for a left-turn waiting area (mk.waiting), read only: from the approach under the cursor (the edge and the way its traffic runs), the junction lanes of the node it leads to that start
    /// where the approach's car lanes end (the same path node, as GameRoadGraph reads where a lane leads) and turn left (CarLaneFlags.TurnLeft or GentleTurnLeft); the approach lanes that have one are the
    /// waiting lanes (from the driver's left, at most three), the leftmost one's turn is the axis (the tightest when it has several). The oncoming traffic is the junction lanes that start heading within 45
    /// degrees of the opposite way. Main thread.
    /// </summary>
    internal static class WaitingReader
    {
        private const CarLaneFlags kLeft = CarLaneFlags.TurnLeft | CarLaneFlags.GentleTurnLeft;
        private const CarLaneFlags kUTurn = CarLaneFlags.UTurnLeft | CarLaneFlags.UTurnRight;

        private struct ApproachLane
        {
            public Entity Entity;
            public float Lateral;
            public Lane Lane;
            public float Width;
        }

        private static Bezier3 B(Curve c) => new Bezier3(RoadReader.ToVec(c.m_Bezier.a), RoadReader.ToVec(c.m_Bezier.b), RoadReader.ToVec(c.m_Bezier.c), RoadReader.ToVec(c.m_Bezier.d));

        /// <summary>The width of a lane (m): its prefab's, with the widening a junction gives it (NodeLane, the mean of its two ends); a lane's default when none is read.</summary>
        internal static float LaneWidth(EntityManager em, Entity lane)
        {
            if (em.TryGetComponent(lane, out PrefabRef prefab) && em.TryGetComponent(prefab.m_Prefab, out NetLaneData data) && data.m_Width > 0.5f)
                return data.m_Width + (em.TryGetComponent(lane, out NodeLane node) ? Math.Max(0f, (node.m_WidthOffset.x + node.m_WidthOffset.y) * 0.5f) : 0f);
            return MarkingSnap.DefaultLane;
        }

        private static List<Vec3> Points(Curve curve)
        {
            Bezier3 b = B(curve);
            int n = Math.Max(8, Math.Min(32, (int)Math.Ceiling(curve.m_Length / 1.2f)));
            var points = new List<Vec3>(n + 1);
            for (int i = 0; i <= n; i++)
                points.Add(b.Point(i / (float)n));
            return points;
        }

        /// <summary>The waiting area for the traffic on edge that runs with it (forward) or against it, into the node at that end.</summary>
        internal static WaitingRead Read(EntityManager em, Entity edge, bool forward)
        {
            var read = new WaitingRead();
            if (!RoadReader.IsLiveEdge(em, edge) || !em.TryGetComponent(edge, out Edge ends) || !em.TryGetComponent(edge, out Curve edgeCurve) || !em.TryGetBuffer(edge, true, out DynamicBuffer<Game.Net.SubLane> edgeLanes))
            {
                read.Why = "no road";
                return read;
            }

            read.Node = forward ? ends.m_End : ends.m_Start;
            if (!em.TryGetBuffer(read.Node, true, out DynamicBuffer<Game.Net.SubLane> nodeLanes))
            {
                read.Why = "the road has no junction at that end";
                return read;
            }

            // The approach: the car lanes of the edge that run toward the node, from the driver's left.
            Bezier3 edgeBezier = B(edgeCurve);
            var approach = new List<ApproachLane>();
            for (int i = 0; i < edgeLanes.Length; i++)
            {
                Entity lane = edgeLanes[i].m_SubLane;
                if (!RoadReader.IsMotorLane(em, lane) || !em.TryGetComponent(lane, out Curve laneCurve) || !em.TryGetComponent(lane, out Lane data))
                    continue;
                LaneMath.Across(edgeBezier, B(laneCurve), out float lateral, out bool with);
                // 0.12.7: not the half of a lane that ends in the segment's middle (it does not run into the junction).
                if (with == forward && !RoadReader.EndsInsideEdge(em, data, edgeLanes))
                    approach.Add(new ApproachLane { Entity = lane, Lateral = lateral, Lane = data, Width = LaneWidth(em, lane) });
            }

            // The driver's left is the smaller lateral for traffic with the edge, the larger against it.
            approach.Sort((a, b) => forward ? a.Lateral.CompareTo(b.Lateral) : b.Lateral.CompareTo(a.Lateral));
            if (approach.Count == 0)
            {
                read.Why = "no car lane runs that way into the junction";
                return read;
            }

            // The junction lanes, and which approach lane each continues.
            var waiting = new List<int>();
            Entity axisLane = Entity.Null;
            float axisLength = float.MaxValue;
            var fromApproach = new HashSet<Entity>();
            Vec3 heading = B(em.GetComponentData<Curve>(approach[0].Entity)).Tangent(1f).HeadingXZ();
            for (int k = 0; k < approach.Count; k++)
            {
                bool hasLeft = false;
                for (int i = 0; i < nodeLanes.Length; i++)
                {
                    Entity lane = nodeLanes[i].m_SubLane;
                    if (lane == Entity.Null || !em.TryGetComponent(lane, out Game.Net.CarLane car) || em.HasComponent<MasterLane>(lane) || !em.TryGetComponent(lane, out Lane data)
                        || !data.m_StartNode.EqualsIgnoreCurvePos(approach[k].Lane.m_EndNode))
                        continue;
                    fromApproach.Add(lane);
                    if ((car.m_Flags & kLeft) == 0 || (car.m_Flags & kUTurn) != 0)
                        continue;
                    hasLeft = true;
                    if (waiting.Count == 0 && em.TryGetComponent(lane, out Curve c) && c.m_Length < axisLength)
                    {
                        axisLength = c.m_Length;
                        axisLane = lane;
                    }
                }

                if (hasLeft && waiting.Count < 3 && (waiting.Count == 0 || waiting[waiting.Count - 1] == k - 1))
                    waiting.Add(k);
            }

            if (waiting.Count == 0 || axisLane == Entity.Null)
            {
                read.Why = "no lane of this approach turns left here";
                return read;
            }

            read.Lanes = waiting.Count;
            read.LaneWidth = Math.Max(2.5f, Math.Min(5f, approach[waiting[0]].Width));
            read.LeftYellow = waiting[0] == 0;
            read.Axis = Points(em.GetComponentData<Curve>(axisLane));

            // The oncoming traffic: junction lanes not of this approach whose start heads back toward it (within 45 degrees of the opposite way).
            var oncoming = new List<Entity>();
            var others = new List<Entity>();
            for (int i = 0; i < nodeLanes.Length; i++)
            {
                Entity lane = nodeLanes[i].m_SubLane;
                if (lane == Entity.Null || fromApproach.Contains(lane) || !em.HasComponent<Game.Net.CarLane>(lane) || em.HasComponent<MasterLane>(lane) || !em.TryGetComponent(lane, out Curve c))
                    continue;
                others.Add(lane);
                Vec3 start = B(c).Tangent(0f).HeadingXZ();
                if (start.X * heading.X + start.Z * heading.Z < -0.7071f)
                    oncoming.Add(lane);
            }

            read.AllOthers = oncoming.Count == 0;
            foreach (Entity lane in read.AllOthers ? others : oncoming)
            {
                read.Others.Add(Points(em.GetComponentData<Curve>(lane)));
                read.HalfWidths.Add(LaneWidth(em, lane) * 0.5f);
            }

            read.Ok = true;
            read.Log = $"node {read.Node.Index}:{read.Node.Version}: {approach.Count} approach lane(s), waiting lanes {string.Join(",", waiting)} (from the driver's left), lane width {read.LaneWidth:0.00} m, axis {axisLength:0.0} m, " +
                $"{(read.LeftYellow ? "the leftmost lane (yellow line)" : "not the leftmost lane (white line)")}, {oncoming.Count} oncoming lane(s) of {others.Count} other(s){(read.AllOthers ? " (none oncoming: every other movement counts)" : "")}";
            return read;
        }
    }
}
