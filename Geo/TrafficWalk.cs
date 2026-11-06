using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>One car lane of an edge: where it is across the edge, which way it runs, and what it leads to at the end it runs to (JrMoves bits; 0 when not read).</summary>
    public struct TwLane
    {
        /// <summary>The distance (m) from the edge's centre line to the lane's middle, positive to the right of the edge's direction.</summary>
        public float Lateral;

        /// <summary>The lane runs the way the edge does (from its start to its end), else against it.</summary>
        public bool WithEdge;

        public int Moves;
    }

    /// <summary>One road edge as the walk of a traffic-state screen needs it (the game's reader fills it from the edge, a test by hand).</summary>
    public sealed class TwEdge
    {
        /// <summary>The car lanes of the edge (not the junctions' master lanes), for the junction signs; empty when they were not read.</summary>
        public List<TwLane> Lanes = new List<TwLane>();

        public int Id;

        /// <summary>The nodes at the start and at the end of the edge (the ends of Points).</summary>
        public int StartNode, EndNode;

        /// <summary>x, z pairs in the world (m), from the start of the edge to its end.</summary>
        public float[] Points = new float[0];

        /// <summary>The speed ratio (1 free, 0 stopped) of the lanes that run with the edge (start to end) and against it; below 0 when the edge has no car lane that way.</summary>
        public float RatioForward = -1f, RatioBackward = -1f;

        /// <summary>The road's name ("" when it has none) and the road it belongs to (0 when not known).</summary>
        public string Name = "";

        public int Road;

        /// <summary>A traffic accident on this edge (TmAccident): the game keeps it on the road edge.</summary>
        public byte Accident;

        public bool HasLanes => RatioForward >= 0f || RatioBackward >= 0f;

        /// <summary>The edge runs by the game's highway rules (a motorway or expressway prefab, ramps too: a car lane of it has CarLaneFlags.Highway), and how far its ends are from the ground (m, the larger of the two; 0 when not read): for the junctions-ahead board.</summary>
        public bool Highway;

        public float Rise;

        /// <summary>Cars can go one way only on this edge (lanes with it or against it, not both).</summary>
        public bool OneWay => (RatioForward >= 0f) != (RatioBackward >= 0f);

        /// <summary>For a graph whose speeds are not read (the junction signs'): a direction has lanes when a car lane runs that way, and its ratio stands for free flow; below 0 where there is none.</summary>
        public void DirectionsFromLanes()
        {
            bool with = false, against = false;
            foreach (TwLane lane in Lanes)
            {
                if (lane.WithEdge)
                    with = true;
                else
                    against = true;
            }

            RatioForward = with ? 1f : -1f;
            RatioBackward = against ? 1f : -1f;
        }
    }

    /// <summary>The road network the walk reads: an edge by its id, and the edges at a node.</summary>
    public interface ITrafficGraph
    {
        /// <summary>The edge; false when it is gone or is not a road.</summary>
        bool TryEdge(int id, out TwEdge edge);

        /// <summary>The ids of the edges that end or start at the node.</summary>
        IReadOnlyList<int> EdgesAt(int node);
    }

    /// <summary>The limits of the walk of a traffic-state screen.</summary>
    public struct TwOptions
    {
        /// <summary>How many junctions ahead, along any way the traffic can take.</summary>
        public int Junctions;

        /// <summary>The farthest the walk goes from the sign along the roads (m).</summary>
        public float Range;

        /// <summary>The most stretches the screen gets.</summary>
        public int MaxEdges;

        /// <summary>How far a road is followed past the last junction, on the roads that leave it (m).</summary>
        public float Stub;

        /// <summary>How far behind the sign a stretch may go before it is cut (m): the screen has the sign at its foot.</summary>
        public float Behind;

        public static TwOptions Default => new TwOptions { Junctions = 3, Range = 800f, MaxEdges = 80, Stub = 160f, Behind = 20f };
    }

    /// <summary>
    /// The walk that makes the network of a traffic-state screen: from the edge the sign stands at, the way the traffic that sees it goes, along the roads to the junctions ahead (a node with three or
    /// more roads is a junction; a node with two is only a bend or a change of road), every road that leaves each junction as a stretch with the congestion of the way it would be driven, the nearest
    /// first, until the junctions or the range or the number of stretches run out. Pure: the graph is an interface, the level of a stretch is decided by the caller (it keeps the previous level for the hysteresis).
    /// </summary>
    public static class TrafficWalk
    {
        private struct Front
        {
            public int Node, Via, Junctions;
            public float Distance, Since;
        }

        /// <summary>Which level the caller gives a stretch: the key says which edge and which way (TrafficWalk.KeyOf), the ratio is the speed ratio of the lanes that way.</summary>
        public delegate TrafficLevel Classify(long key, float ratio);

        /// <summary>The key of an edge driven in a direction, for the caller's memory of levels.</summary>
        public static long KeyOf(int edge, bool forward) => (long)edge * 2L + (forward ? 1L : 0L);

        public static TmNetwork Walk(ITrafficGraph graph, int startEdge, float originX, float originZ, float headX, float headZ, TwOptions options, Classify classify)
        {
            var net = new TmNetwork { OriginX = originX, OriginZ = originZ, HeadX = headX, HeadZ = headZ };
            float hl = (float)Math.Sqrt(headX * headX + headZ * headZ);
            if (hl < 1e-6f || graph == null || !graph.TryEdge(startEdge, out TwEdge first) || first.Points == null || first.Points.Length < 4)
                return net;
            headX /= hl;
            headZ /= hl;
            net.HeadX = headX;
            net.HeadZ = headZ;

            // The sign's own edge: from where the sign is to the end the traffic runs to.
            Nearest(first.Points, originX, originZ, out int segment, out float along);
            float tx = first.Points[segment * 2 + 2] - first.Points[segment * 2], tz = first.Points[segment * 2 + 3] - first.Points[segment * 2 + 1];
            bool forward = tx * headX + tz * headZ >= 0f;
            var run = new List<float> { first.Points[segment * 2] + tx * along, first.Points[segment * 2 + 1] + tz * along };
            if (forward)
            {
                for (int i = segment + 1; i * 2 + 1 < first.Points.Length; i++)
                {
                    run.Add(first.Points[i * 2]);
                    run.Add(first.Points[i * 2 + 1]);
                }
            }
            else
            {
                for (int i = segment; i >= 0; i--)
                {
                    run.Add(first.Points[i * 2]);
                    run.Add(first.Points[i * 2 + 1]);
                }
            }

            net.Known = true;
            var visited = new HashSet<int> { startEdge };
            float firstLength = Length(run.ToArray());
            AddStretch(net, run.ToArray(), first, forward, classify, 0f);

            var fronts = new List<Front> { new Front { Node = forward ? first.EndNode : first.StartNode, Via = startEdge, Distance = firstLength, Junctions = 0, Since = 0f } };
            while (fronts.Count > 0 && net.Edges.Count < options.MaxEdges)
            {
                // The nearest first, so that when the stretches run out it is the far ones that are left out.
                int best = 0;
                for (int i = 1; i < fronts.Count; i++)
                {
                    if (fronts[i].Distance < fronts[best].Distance)
                        best = i;
                }

                Front front = fronts[best];
                fronts.RemoveAt(best);

                var roads = new List<TwEdge>();
                foreach (int id in graph.EdgesAt(front.Node))
                {
                    if (graph.TryEdge(id, out TwEdge edge) && edge.HasLanes && edge.Points != null && edge.Points.Length >= 4)
                        roads.Add(edge);
                }

                bool junction = roads.Count >= 3;
                int junctions = front.Junctions + (junction ? 1 : 0);
                // The roads past the last junction are followed only a little way and only along roads that do not branch: the screen shows the junctions, not the rest of the city.
                bool last = junctions >= options.Junctions;
                if (junction && junctions > options.Junctions)
                    continue;
                float since = junction ? 0f : front.Since;

                foreach (TwEdge edge in roads)
                {
                    if (edge.Id == front.Via || visited.Contains(edge.Id) || net.Edges.Count >= options.MaxEdges)
                        continue;

                    bool leaves = edge.StartNode == front.Node;
                    float[] points = leaves ? edge.Points : Reverse(edge.Points);
                    points = Ahead(points, originX, originZ, headX, headZ, options.Behind);
                    if (points.Length < 4)
                        continue;
                    visited.Add(edge.Id);
                    AddStretch(net, points, edge, leaves, classify, front.Distance);

                    // A road is followed on when the traffic can drive it that way (a one-way road that comes into the junction is shown but not followed).
                    float ratio = leaves ? edge.RatioForward : edge.RatioBackward;
                    if (ratio < 0f)
                        continue;
                    float length = Length(points);
                    float distance = front.Distance + length;
                    float stub = since + length;
                    if (distance > options.Range || (last && stub > options.Stub))
                        continue;
                    fronts.Add(new Front { Node = leaves ? edge.EndNode : edge.StartNode, Via = edge.Id, Distance = distance, Junctions = junctions, Since = stub });
                }
            }

            return net;
        }

        private static void AddStretch(TmNetwork net, float[] points, TwEdge edge, bool forward, Classify classify, float distance)
        {
            float ratio = forward ? edge.RatioForward : edge.RatioBackward;
            bool way = forward;
            if (ratio < 0f)
            {
                // No lane that way (a one-way road that comes into the junction): the road is there and has the state of the way it is driven.
                ratio = forward ? edge.RatioBackward : edge.RatioForward;
                way = !forward;
            }

            TrafficLevel level = classify != null ? classify(KeyOf(edge.Id, way), ratio) : TrafficMap.LevelOf(ratio, 0.7f, 0.4f);
            net.Edges.Add(new TmEdge { Points = points, Level = level, Name = edge.Name ?? "", Road = edge.Road, Distance = distance, Length = Length(points), Accident = edge.Accident });
        }

        private static float[] Reverse(float[] points)
        {
            var r = new float[points.Length];
            int n = points.Length / 2;
            for (int i = 0; i < n; i++)
            {
                r[i * 2] = points[(n - 1 - i) * 2];
                r[i * 2 + 1] = points[(n - 1 - i) * 2 + 1];
            }

            return r;
        }

        /// <summary>The points up to the first one that is more than behind metres behind the sign; the stretch is dropped (fewer than two points) when it starts behind the sign.</summary>
        private static float[] Ahead(float[] points, float originX, float originZ, float headX, float headZ, float behind)
        {
            var kept = new List<float>();
            for (int i = 0; i + 1 < points.Length; i += 2)
            {
                float v = (points[i] - originX) * headX + (points[i + 1] - originZ) * headZ;
                if (v < -behind)
                {
                    if (kept.Count == 0)
                        return new float[0];
                    break;
                }

                kept.Add(points[i]);
                kept.Add(points[i + 1]);
            }

            return kept.ToArray();
        }

        private static float Length(float[] points)
        {
            float length = 0f;
            for (int i = 0; i + 3 < points.Length; i += 2)
            {
                float dx = points[i + 2] - points[i], dz = points[i + 3] - points[i + 1];
                length += (float)Math.Sqrt(dx * dx + dz * dz);
            }

            return length;
        }

        /// <summary>The segment of a polyline nearest to a point, and how far along it (0 to 1) the nearest point is.</summary>
        private static void Nearest(float[] points, float x, float z, out int segment, out float along)
        {
            segment = 0;
            along = 0f;
            float best = float.MaxValue;
            for (int i = 0; i + 3 < points.Length; i += 2)
            {
                float dx = points[i + 2] - points[i], dz = points[i + 3] - points[i + 1];
                float length2 = dx * dx + dz * dz;
                float t = length2 > 1e-9f ? Math.Max(0f, Math.Min(1f, ((x - points[i]) * dx + (z - points[i + 1]) * dz) / length2)) : 0f;
                float px = points[i] + dx * t - x, pz = points[i + 1] + dz * t - z;
                float distance = px * px + pz * pz;
                if (distance < best)
                {
                    best = distance;
                    segment = i / 2;
                    along = t;
                }
            }
        }
    }
}
