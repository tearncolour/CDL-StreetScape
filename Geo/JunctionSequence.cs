using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>How a junction ahead is drawn on the junctions-ahead board (gb.guide8.01c).</summary>
    public enum JrKind
    {
        AtGrade,
        Interchange
    }

    /// <summary>One junction of a sequence: how far along the road, the roads that leave it, and what kind it is judged to be.</summary>
    public sealed class JrStop
    {
        /// <summary>The distance from the sign along the road to the junction (m).</summary>
        public float Total;

        /// <summary>The reading of the junction (its node, the road it comes by); the first stop's is the reading the sequence was made for.</summary>
        public JrResult Read;

        /// <summary>The roads that leave it or come in by a ramp, with the direction each has to the driver; a junction made of two close nodes (the two carriageways of a crossing road) has the roads of both.</summary>
        public readonly List<JrArm> Arms = new List<JrArm>();

        public JrKind Kind;

        /// <summary>How many nodes this junction was made of (2 or more when close ones were joined).</summary>
        public int Nodes = 1;

        public bool Has(bool left)
        {
            foreach (JrArm arm in Arms)
            {
                if (JunctionSequence.SideOf(arm) == (left ? JrDirection.Left : JrDirection.Right))
                    return true;
            }

            return false;
        }

        /// <summary>The arm that goes on along the road, null when the road does not go on.</summary>
        public JrArm Through;
    }

    /// <summary>The junctions ahead of a sign, nearest first, as far as were asked for and found; Why says what ended the walk early.</summary>
    public sealed class JrSequence
    {
        public readonly List<JrStop> Stops = new List<JrStop>();
        public string Why = "";
    }

    /// <summary>
    /// The junctions after the first one, along the road the driver is on (0.12.78, for the junctions-ahead board): from each junction the road goes on by its through arm (the road that has the
    /// same name or the same road, else the one that is most nearly straight on), to the next node of three roads or more, with JunctionRead's own walk; junctions that are less than MergeGap apart
    /// are one (a crossing road with two carriageways meets the road at two nodes); and each junction is judged to be an interchange when a one-way road of the highway kind joins or leaves it (a
    /// ramp), at grade otherwise. **The rule is a guess from what the game's road network says (a ramp is a one-way highway road); nothing of it was seen on a real city yet.** Pure.
    /// </summary>
    public static class JunctionSequence
    {
        /// <summary>The most junctions the board shows and the farthest along the road the walk goes (m).</summary>
        public const int Max = 3;

        public const float Range = 2000f, MergeGap = 40f;

        /// <summary>A one-way road that runs by the highway rules (its own, or the road it joins) is a ramp: the mark of an interchange.</summary>
        public static bool IsRamp(JrArm arm, bool approachHighway) => arm.OneWay && (arm.Highway || approachHighway);

        /// <summary>The side an arm is on, as the board draws it: a ramp by the sign of its angle (a ramp that joins comes in from behind), any other road by its direction; Back and Ahead are no side.</summary>
        public static JrDirection SideOf(JrArm arm)
        {
            if (arm.IsRamp)
                return arm.Angle > 0f ? JrDirection.Left : JrDirection.Right;
            return arm.Direction == JrDirection.Left || arm.Direction == JrDirection.Right ? arm.Direction : JrDirection.Back;
        }

        /// <summary>The arm the road goes on by: one that can be left and is ahead, the same road first, then the same name, then the straightest, then the widest.</summary>
        public static JrArm ThroughArm(JrResult r, IEnumerable<JrArm> arms)
        {
            JrArm best = null;
            int bestRank = -1;
            foreach (JrArm arm in arms)
            {
                if (arm.Direction != JrDirection.Ahead || !arm.CanLeave)
                    continue;
                int rank = r.OwnRoad != 0 && arm.Road == r.OwnRoad ? 2 : r.OwnName.Length > 0 && arm.Name == r.OwnName ? 1 : 0;
                if (best == null || rank > bestRank || (rank == bestRank && (Math.Abs(arm.Angle) < Math.Abs(best.Angle) - 1e-3f || (Math.Abs(Math.Abs(arm.Angle) - Math.Abs(best.Angle)) <= 1e-3f && arm.Lanes > best.Lanes))))
                {
                    best = arm;
                    bestRank = rank;
                }
            }

            return best;
        }

        /// <summary>An interchange has a ramp among its roads.</summary>
        public static JrKind KindOf(JrStop stop)
        {
            foreach (JrArm arm in stop.Arms)
            {
                if (arm.IsRamp)
                    return JrKind.Interchange;
            }

            return JrKind.AtGrade;
        }

        /// <summary>The road goes on by one arm, and the other roads that are one-way highway roads are ramps; read is the last node of the junction.</summary>
        private static void Settle(JrStop stop, JrResult read)
        {
            stop.Read = read;
            stop.Through = ThroughArm(read, read.Arms);
            foreach (JrArm arm in stop.Arms)
                arm.IsRamp = arm != stop.Through && IsRamp(arm, read.ApproachHighway);
            stop.Kind = KindOf(stop);
        }

        private static JrStop StopOf(JrResult read, float total)
        {
            var stop = new JrStop { Total = total };
            stop.Arms.AddRange(read.Arms);
            Settle(stop, read);
            return stop;
        }

        /// <summary>The sequence of the one junction that was read, for a reading that did not go on (the first stop alone).</summary>
        public static JrSequence OfFirst(JrResult first)
        {
            var sequence = new JrSequence();
            if (first != null && first.Found)
                sequence.Stops.Add(StopOf(first, first.Distance));
            return sequence;
        }

        /// <summary>
        /// The sequence of junctions ahead from the first one that was read (its JrResult is the first stop). More are found by going on along the through arm; the walk ends at max junctions, at Range, at a
        /// road that does not go on, at a junction that is not found, and when it comes to a road it has been on.
        /// </summary>
        public static JrSequence Read(ITrafficGraph graph, JrResult first, JrOptions options, int max = Max)
        {
            var sequence = new JrSequence();
            if (graph == null || first == null || !first.Found)
                return sequence;
            JrStop current = StopOf(first, first.Distance);
            sequence.Stops.Add(current);
            var visited = new HashSet<int> { first.ApproachEdge };
            float along = first.Distance;
            while (true)
            {
                JrArm through = current.Through;
                if (through == null)
                {
                    sequence.Why = "the road does not go on";
                    break;
                }

                if (!Leave(graph, current.Read.Node, through, out float ox, out float oz, out float hx, out float hz))
                {
                    sequence.Why = "the road ahead is not known";
                    break;
                }

                JrResult next = JunctionRead.Read(graph, through.Edge, ox, oz, hx, hz, options);
                if (!next.Found || !visited.Add(next.ApproachEdge))
                {
                    sequence.Why = next.Found ? "the road comes back on itself" : next.Why;
                    break;
                }

                along += next.Distance;
                if (along > Range)
                {
                    sequence.Why = "no more within " + Range.ToString("0") + " m";
                    break;
                }

                // Joined nodes do not count against the junctions asked for: a full sequence still takes the second carriageway's node.
                if (next.Distance < MergeGap)
                {
                    // Two nodes of one crossing: the arm that led to the second is no road of the junction, the second's roads are.
                    current.Arms.RemoveAll(arm => arm.Edge == through.Edge);
                    current.Arms.AddRange(next.Arms);
                    current.Nodes++;
                    Settle(current, next);
                }
                else
                {
                    if (sequence.Stops.Count >= max)
                        break;
                    current = StopOf(next, along);
                    sequence.Stops.Add(current);
                }
            }

            return sequence;
        }

        /// <summary>Where a road leaves a node: its edge, the place of the node on it and the way it leaves.</summary>
        private static bool Leave(ITrafficGraph graph, int node, JrArm arm, out float ox, out float oz, out float hx, out float hz)
        {
            ox = oz = hx = hz = 0f;
            if (!graph.TryEdge(arm.Edge, out TwEdge e) || e.Points == null || e.Points.Length < 4)
                return false;
            float[] p = e.Points;
            int n = p.Length / 2;
            if (e.StartNode == node)
            {
                ox = p[0];
                oz = p[1];
                hx = p[2] - p[0];
                hz = p[3] - p[1];
            }
            else
            {
                ox = p[(n - 1) * 2];
                oz = p[(n - 1) * 2 + 1];
                hx = p[(n - 2) * 2] - ox;
                hz = p[(n - 2) * 2 + 1] - oz;
            }

            return hx * hx + hz * hz > 1e-8f;
        }
    }
}
