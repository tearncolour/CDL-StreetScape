using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>Where a road leaves a junction, as the driver who comes up to it sees it.</summary>
    public enum JrDirection
    {
        /// <summary>Not a way the driver can take (the road he came by, or one that turns back on it): not shown.</summary>
        Back = 0,
        Ahead = 1,
        Left = 2,
        Right = 3
    }

    /// <summary>The movements a lane leads to at the end of its road (bits).</summary>
    public static class JrMoves
    {
        public const int Left = 1, Ahead = 2, Right = 4, UTurn = 8;
    }

    /// <summary>One road that leaves the junction ahead.</summary>
    public sealed class JrArm
    {
        public int Edge;

        /// <summary>The road it belongs to (the game's aggregate of its edges), 0 when not known; and its name ("" when it has none).</summary>
        public int Road;

        public string Name = "";

        /// <summary>Degrees from the way the driver is going: 0 straight on, positive to his left, negative to his right (-180 to 180).</summary>
        public float Angle;

        /// <summary>How many car lanes the road has (how important it looks); at least 1.</summary>
        public int Lanes = 1;

        /// <summary>Whether traffic can leave the junction by it (a one-way road that comes into the junction cannot be taken).</summary>
        public bool CanLeave = true;

        public JrDirection Direction;

        /// <summary>Cars go one way only on it, and it runs by the highway rules (0.12.78, for the junctions-ahead board: a ramp is a one-way road of the highway kind); Rise is how far its ends are from the ground (m), read for the log only.</summary>
        public bool OneWay, Highway;

        public float Rise;

        /// <summary>Set by JunctionSequence: a one-way road of the highway kind (a ramp).</summary>
        public bool IsRamp;
    }

    /// <summary>What was found ahead of a sign.</summary>
    public sealed class JrResult
    {
        /// <summary>False when no junction was found ahead (Why says what stopped the search).</summary>
        public bool Found;

        public string Why = "";

        /// <summary>The distance from the sign to the junction (m) along the road, the edge the traffic comes to it by, and the name and road of that edge.</summary>
        public float Distance;

        public int ApproachEdge;

        /// <summary>The node of the junction, whether the road it comes by runs by the highway rules (0.12.78), and the junctions after it along the road when they were asked for (JunctionSequence).</summary>
        public int Node;

        public bool ApproachHighway;
        public JrSequence Sequence;

        public string OwnName = "";
        public int OwnRoad;

        /// <summary>The roads that leave it (not the one the traffic came by), each with its direction.</summary>
        public readonly List<JrArm> Arms = new List<JrArm>();

        /// <summary>The lanes of the road at the junction, from the driver's left, each as the bits of JrMoves; empty when the lanes were not read.</summary>
        public readonly List<int> LaneMoves = new List<int>();

        /// <summary>The way the traffic goes (a unit vector in the ground plane: x east, z north).</summary>
        public float HeadX, HeadZ = 1f;

        public bool Has(JrDirection direction)
        {
            foreach (JrArm arm in Arms)
            {
                if (arm.Direction == direction)
                    return true;
            }

            return false;
        }
    }

    /// <summary>The limits of the search for the junction ahead.</summary>
    public struct JrOptions
    {
        /// <summary>The farthest the search goes along the road from the sign (m).</summary>
        public float Range;

        /// <summary>How far from the junction a road's direction is taken (m): a road may curve at its mouth.</summary>
        public float Sample;

        /// <summary>A road within this many degrees of straight on is ahead; between this and Side it is left or right; beyond Side it turns back.</summary>
        public float AheadHalf, Side;

        public static JrOptions Default => new JrOptions { Range = 600f, Sample = 12f, AheadHalf = 30f, Side = 150f };
    }

    /// <summary>
    /// The junction ahead of a sign, read from the road graph (the same ITrafficGraph the traffic-state screen walks): the first node ahead with three roads or more (a node with two is a bend
    /// and is passed), the roads that leave it with the direction each has to a driver coming up to it, the lanes he comes by and where each leads, and the junction behind the sign. Pure:
    /// the graph is an interface, so that all of it is checked offline; the game's reader fills the graph and nothing here touches the game.
    /// </summary>
    public static class JunctionRead
    {
        private const float Epsilon = 1e-4f;

        /// <summary>
        /// Reads the junction ahead of a sign that stands at (originX, originZ) on the edge startEdge and faces the traffic that goes the way (headX, headZ). Found is false, with the reason, when the road
        /// ends first or the range runs out.
        /// </summary>
        public static JrResult Read(ITrafficGraph graph, int startEdge, float originX, float originZ, float headX, float headZ, JrOptions options)
        {
            var result = new JrResult();
            float hl = (float)Math.Sqrt(headX * headX + headZ * headZ);
            if (graph == null || hl < 1e-6f)
            {
                result.Why = "no road or no direction";
                return result;
            }

            headX /= hl;
            headZ /= hl;
            result.HeadX = headX;
            result.HeadZ = headZ;
            if (options.Range <= 0f)
                options = JrOptions.Default;

            Walk(graph, startEdge, originX, originZ, headX, headZ, options, out Reach ahead, out string why);
            if (ahead == null)
            {
                result.Why = why;
                return result;
            }

            result.Found = true;
            result.Distance = ahead.Distance;
            result.ApproachEdge = ahead.Edge.Id;
            result.Node = ahead.Node;
            result.ApproachHighway = ahead.Edge.Highway;
            result.OwnName = ahead.Edge.Name ?? "";
            result.OwnRoad = ahead.Edge.Road;
            if (string.IsNullOrWhiteSpace(result.OwnName) && graph.TryEdge(startEdge, out TwEdge first) && first.Road == ahead.Edge.Road)
                result.OwnName = first.Name ?? "";

            // The way the driver is going as he comes to the node: along the road's last part.
            Direction(ahead.Edge.Points, ahead.TowardsEnd, true, options.Sample, out float ax, out float az);

            foreach (int id in graph.EdgesAt(ahead.Node))
            {
                if (id == ahead.Edge.Id || !graph.TryEdge(id, out TwEdge e) || !e.HasLanes || e.Points == null || e.Points.Length < 4)
                    continue;
                bool leaves = e.StartNode == ahead.Node;
                if (leaves && e.EndNode == ahead.Node)
                    continue; // a loop that starts and ends at the junction
                Direction(e.Points, !leaves, false, options.Sample, out float dx, out float dz);
                float angle = (float)(Math.Atan2(ax * dz - az * dx, ax * dx + az * dz) * 180.0 / Math.PI);
                var arm = new JrArm
                {
                    Edge = e.Id,
                    Road = e.Road,
                    Name = (e.Name ?? "").Trim(),
                    Angle = angle,
                    Lanes = Math.Max(1, e.Lanes != null ? e.Lanes.Count : 0),
                    CanLeave = leaves ? e.RatioForward >= 0f : e.RatioBackward >= 0f,
                    OneWay = e.OneWay,
                    Highway = e.Highway,
                    Rise = e.Rise
                };
                arm.Direction = !arm.CanLeave ? JrDirection.Back : DirectionOf(angle, options);
                result.Arms.Add(arm);
            }

            // The lanes the traffic comes by, from the driver's left.
            if (ahead.Edge.Lanes != null)
            {
                var lanes = new List<TwLane>();
                foreach (TwLane lane in ahead.Edge.Lanes)
                {
                    if (lane.WithEdge == ahead.TowardsEnd)
                        lanes.Add(lane);
                }

                // Lateral is positive to the right of the edge's direction: to a driver going along it the left is the smaller, going against it the left is the larger.
                lanes.Sort((a, b) => ahead.TowardsEnd ? a.Lateral.CompareTo(b.Lateral) : b.Lateral.CompareTo(a.Lateral));
                foreach (TwLane lane in lanes)
                    result.LaneMoves.Add(lane.Moves);
            }

            return result;
        }

        /// <summary>The direction a road has to the driver from its angle: ahead within AheadHalf, left or right up to Side, otherwise back.</summary>
        public static JrDirection DirectionOf(float angle, JrOptions options)
        {
            if (options.AheadHalf <= 0f)
                options = JrOptions.Default;
            float a = Math.Abs(angle);
            if (a <= options.AheadHalf)
                return JrDirection.Ahead;
            if (a >= options.Side)
                return JrDirection.Back;
            return angle > 0f ? JrDirection.Left : JrDirection.Right;
        }

        // ------------------------------------------------------------------ the walk to the junction

        private sealed class Reach
        {
            public TwEdge Edge;
            public bool TowardsEnd;
            public int Node;
            public float Distance;
        }

        /// <summary>From the sign along the road the way (hx, hz): over the bends to the first node of three roads or more.</summary>
        private static void Walk(ITrafficGraph graph, int startEdge, float ox, float oz, float hx, float hz, JrOptions options, out Reach reach, out string why)
        {
            reach = null;
            why = "";
            if (!graph.TryEdge(startEdge, out TwEdge edge) || edge.Points == null || edge.Points.Length < 4)
            {
                why = "the sign is not on a road";
                return;
            }

            Nearest(edge.Points, ox, oz, out int segment, out float along);
            float tx = edge.Points[segment * 2 + 2] - edge.Points[segment * 2], tz = edge.Points[segment * 2 + 3] - edge.Points[segment * 2 + 1];
            bool towardsEnd = tx * hx + tz * hz >= 0f;
            float distance = RemainingLength(edge.Points, segment, along, towardsEnd);
            int node = towardsEnd ? edge.EndNode : edge.StartNode;
            var visited = new HashSet<int> { edge.Id };
            for (int step = 0; step < 40; step++)
            {
                var roads = new List<TwEdge>();
                foreach (int id in graph.EdgesAt(node))
                {
                    if (graph.TryEdge(id, out TwEdge e) && e.HasLanes && e.Points != null && e.Points.Length >= 4)
                        roads.Add(e);
                }

                if (roads.Count >= 3)
                {
                    reach = new Reach { Edge = edge, TowardsEnd = towardsEnd, Node = node, Distance = distance };
                    return;
                }

                if (distance > options.Range)
                {
                    why = "no junction within " + options.Range.ToString("0") + " m";
                    return;
                }

                TwEdge next = null;
                foreach (TwEdge e in roads)
                {
                    if (e.Id != edge.Id && !visited.Contains(e.Id))
                        next = e;
                }

                if (next == null)
                {
                    why = "the road ends before a junction";
                    return;
                }

                visited.Add(next.Id);
                towardsEnd = next.StartNode == node;
                node = towardsEnd ? next.EndNode : next.StartNode;
                distance += Length(next.Points);
                edge = next;
            }

            why = "no junction within reach";
        }

        // ------------------------------------------------------------------ geometry on a polyline of x, z pairs

        private static float Length(float[] p)
        {
            float length = 0f;
            for (int i = 0; i + 3 < p.Length; i += 2)
                length += (float)Math.Sqrt((p[i + 2] - p[i]) * (p[i + 2] - p[i]) + (p[i + 3] - p[i + 1]) * (p[i + 3] - p[i + 1]));
            return length;
        }

        /// <summary>The segment of the polyline nearest to a point and how far along it (0 to 1) the nearest point is.</summary>
        private static void Nearest(float[] p, float x, float z, out int segment, out float along)
        {
            segment = 0;
            along = 0f;
            float best = float.MaxValue;
            for (int i = 0; i + 3 < p.Length; i += 2)
            {
                float dx = p[i + 2] - p[i], dz = p[i + 3] - p[i + 1], l2 = dx * dx + dz * dz;
                float t = l2 > 1e-9f ? Math.Max(0f, Math.Min(1f, ((x - p[i]) * dx + (z - p[i + 1]) * dz) / l2)) : 0f;
                float px = p[i] + dx * t - x, pz = p[i + 1] + dz * t - z, d = px * px + pz * pz;
                if (d < best)
                {
                    best = d;
                    segment = i / 2;
                    along = t;
                }
            }
        }

        /// <summary>The length of the polyline from a point of it (segment, along) to its end (towardsEnd) or its start.</summary>
        private static float RemainingLength(float[] p, int segment, float along, bool towardsEnd)
        {
            float length = 0f;
            int n = p.Length / 2;
            float sx = p[segment * 2 + 2] - p[segment * 2], sz = p[segment * 2 + 3] - p[segment * 2 + 1];
            float whole = (float)Math.Sqrt(sx * sx + sz * sz);
            if (towardsEnd)
            {
                length += whole * (1f - along);
                for (int i = segment + 1; i + 1 < n; i++)
                    length += (float)Math.Sqrt((p[i * 2 + 2] - p[i * 2]) * (p[i * 2 + 2] - p[i * 2]) + (p[i * 2 + 3] - p[i * 2 + 1]) * (p[i * 2 + 3] - p[i * 2 + 1]));
            }
            else
            {
                length += whole * along;
                for (int i = segment - 1; i >= 0; i--)
                    length += (float)Math.Sqrt((p[i * 2 + 2] - p[i * 2]) * (p[i * 2 + 2] - p[i * 2]) + (p[i * 2 + 3] - p[i * 2 + 1]) * (p[i * 2 + 3] - p[i * 2 + 1]));
            }

            return length;
        }

        /// <summary>
        /// The unit direction of a polyline near one of its ends, taken over the last sample metres of it (a road may curve at the mouth of a junction). atEnd: the end of the polyline at the
        /// junction is its last point, else its first. into: the direction is that of a vehicle coming up to the junction (toward the end); else of one leaving it (away from the end).
        /// </summary>
        private static void Direction(float[] p, bool atEnd, bool into, float sample, out float dx, out float dz)
        {
            int n = p.Length / 2;
            float ex = atEnd ? p[(n - 1) * 2] : p[0], ez = atEnd ? p[(n - 1) * 2 + 1] : p[1];
            float px = ex, pz = ez, run = 0f;
            for (int k = 1; k < n; k++)
            {
                int i = atEnd ? n - 1 - k : k, j = atEnd ? i + 1 : i - 1;
                float x0 = p[j * 2], z0 = p[j * 2 + 1], x1 = p[i * 2], z1 = p[i * 2 + 1];
                float seg = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                if (seg < Epsilon)
                    continue;
                if (run + seg >= sample)
                {
                    float f = (sample - run) / seg;
                    px = x0 + (x1 - x0) * f;
                    pz = z0 + (z1 - z0) * f;
                    run = sample;
                    break;
                }

                run += seg;
                px = x1;
                pz = z1;
            }

            // From the point back from the end to the end is the way into the junction; the reverse is the way out.
            float vx = ex - px, vz = ez - pz;
            if (!into)
            {
                vx = -vx;
                vz = -vz;
            }

            float l = (float)Math.Sqrt(vx * vx + vz * vz);
            if (l < Epsilon)
            {
                // A road with no length to speak of: the whole polyline's first to last point.
                vx = p[(n - 1) * 2] - p[0];
                vz = p[(n - 1) * 2 + 1] - p[1];
                if (atEnd != into)
                {
                    vx = -vx;
                    vz = -vz;
                }

                l = (float)Math.Sqrt(vx * vx + vz * vz);
            }

            dx = l > Epsilon ? vx / l : 0f;
            dz = l > Epsilon ? vz / l : 1f;
        }
    }
}
