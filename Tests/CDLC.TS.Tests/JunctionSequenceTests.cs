using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The junctions after the first one along the road (JunctionSequence) on made-up road graphs: an at-grade crossroads after a crossroads, a T, a diamond interchange's two ramps, ramps on both sides of
    /// one junction, a bend, a dead end, the range, the number of junctions, one-way streets that are no ramps, two nodes of one crossing; and the junctions-ahead board filled in from them (JunctionFill).
    /// </summary>
    internal static class JunctionSequenceTests
    {
        public static void RunAll()
        {
            Program.Run("junction sequence: two crossroads one after the other", Crossroads);
            Program.Run("junction sequence: a T, a bend, a dead end and a road that is not on a road", Ends);
            Program.Run("junction sequence: the range and the number of junctions", Limits);
            Program.Run("junction sequence: ramps make an interchange, one-way streets do not", Ramps);
            Program.Run("junction sequence: the road goes on by the same road, then the same name, then the straightest", Through);
            Program.Run("junction sequence: two close nodes are one junction", Joined);
            Program.Run("junction sequence: a loop does not go round twice", Loop);
            Program.Run("junction sequence: a through road stored backwards and at an angle", Diagonal);
            Program.Run("junction sequence fill: the board's settings from an at-grade sequence", FillAtGrade);
            Program.Run("junction sequence fill: an interchange, names, what is not known, nothing to draw", FillInterchange);
        }

        private sealed class Graph : ITrafficGraph
        {
            public readonly Dictionary<int, TwEdge> Edges = new Dictionary<int, TwEdge>();
            public readonly Dictionary<int, List<int>> Nodes = new Dictionary<int, List<int>>();

            /// <summary>An edge from node a to node b; oneWay: cars go with the edge only; highway: it runs by the highway rules.</summary>
            public TwEdge Add(int id, int a, int b, string name, int road, float[] xz, bool oneWay = false, bool highway = false)
            {
                var edge = new TwEdge { Id = id, StartNode = a, EndNode = b, Points = xz, RatioForward = 1f, RatioBackward = oneWay ? -1f : 1f, Name = name, Road = road, Highway = highway };
                Edges[id] = edge;
                foreach (int node in new[] { a, b })
                {
                    if (!Nodes.TryGetValue(node, out List<int> list))
                        Nodes[node] = list = new List<int>();
                    if (!list.Contains(id))
                        list.Add(id);
                }

                return edge;
            }

            public bool TryEdge(int id, out TwEdge edge) => Edges.TryGetValue(id, out edge);

            public IReadOnlyList<int> EdgesAt(int node) => Nodes.TryGetValue(node, out List<int> list) ? list : (IReadOnlyList<int>)new int[0];
        }

        private static float[] P(params float[] xz) => xz;

        private static JrResult First(Graph g, float z = 0f, int edge = 1) => JunctionRead.Read(g, edge, 0f, z, 0f, 1f, JrOptions.Default);

        private static JrSequence Seq(Graph g, int max = JunctionSequence.Max, float z = 0f)
        {
            JrResult first = First(g, z);
            return JunctionSequence.Read(g, first, JrOptions.Default, max);
        }

        /// <summary>The road north from (0, -400) with a crossroads every 400 m from z = 300: node n has the crossing roads left (10 + n) and right (20 + n).</summary>
        private static Graph Chain(int junctions, float step = 400f)
        {
            var g = new Graph();
            g.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 300));
            for (int i = 0; i < junctions; i++)
            {
                float z = 300f + i * step;
                int node = 2 + i * 3;
                g.Add(100 + i, node, node + 3, "长江路", 1, P(0, z, 0, z + step));
                g.Add(10 + i, node, node + 1, "左路" + (i + 1), 2 + i, P(0, z, -400, z));
                g.Add(20 + i, node, node + 2, "右路" + (i + 1), 20 + i, P(0, z, 400, z));
            }

            return g;
        }

        private static void Crossroads()
        {
            Graph g = Chain(2);
            JrSequence s = Seq(g);
            Program.Check(s.Stops.Count == 2, "a crossroads and another: two stops: " + s.Stops.Count + " " + s.Why);
            Program.Near(s.Stops[0].Total, 300f, 0.01f, "the first is 300 m from the sign");
            Program.Near(s.Stops[1].Total, 700f, 0.01f, "the second 400 m on from it: " + s.Stops[1].Total);
            Program.Check(s.Stops[0].Kind == JrKind.AtGrade && s.Stops[1].Kind == JrKind.AtGrade, "both are at grade");
            Program.Check(s.Stops[0].Has(true) && s.Stops[0].Has(false) && s.Stops[1].Has(true) && s.Stops[1].Has(false), "each has a road on the left and on the right");
            Program.Check(s.Stops[0].Through != null && s.Stops[0].Through.Edge == 100 && s.Stops[1].Through != null && s.Stops[1].Through.Edge == 101, "and the road goes on from each");
            Program.Check(s.Stops[0].Arms.Exists(a => a.Name == "左路1") && s.Stops[1].Arms.Exists(a => a.Name == "右路2"), "the roads of each junction are its own");
            Program.Check(!s.Stops[1].Arms.Exists(a => a.Edge == 100), "the road that came from the first junction is not an arm of the second");
            Program.Near(s.Stops[0].Read.Distance, 300f, 0.01f, "the first stop is the junction that was read");

            // The same roads with the through edges stored the other way round (from the far node to the near one): the same junctions.
            Graph reversed = Chain(2);
            foreach (int id in new[] { 100, 101 })
            {
                TwEdge e = reversed.Edges[id];
                (e.StartNode, e.EndNode) = (e.EndNode, e.StartNode);
                float[] p = e.Points;
                e.Points = P(p[2], p[3], p[0], p[1]);
            }

            reversed.Nodes.Clear();
            foreach (TwEdge e in reversed.Edges.Values)
            {
                foreach (int node in new[] { e.StartNode, e.EndNode })
                {
                    if (!reversed.Nodes.TryGetValue(node, out List<int> list))
                        reversed.Nodes[node] = list = new List<int>();
                    list.Add(e.Id);
                }
            }

            JrSequence back = Seq(reversed);
            Program.Check(back.Stops.Count == 2 && Math.Abs(back.Stops[1].Total - 700f) < 0.01f && back.Stops[1].Arms.Exists(a => a.Name == "左路2"), "edges stored backwards: the same two junctions at the same distances: " + back.Stops.Count + " " + back.Why);

            // A sign nearer the first junction changes the first stop only.
            JrSequence near = Seq(g, JunctionSequence.Max, 100f);
            Program.Near(near.Stops[0].Total, 200f, 0.01f, "from 100 m on: 200 m to the first");
            Program.Near(near.Stops[1].Total, 600f, 0.01f, "and 600 m to the second");

            // A sign that reads the first only gets the first.
            JrSequence one = JunctionSequence.OfFirst(First(g));
            Program.Check(one.Stops.Count == 1 && Math.Abs(one.Stops[0].Total - 300f) < 0.01f, "OfFirst: the one junction that was read");
            Program.Check(JunctionSequence.OfFirst(null).Stops.Count == 0 && JunctionSequence.OfFirst(new JrResult()).Stops.Count == 0, "and nothing when nothing was read");
        }

        private static void Ends()
        {
            // A T: the road ends in it: one stop, the road does not go on.
            var t = new Graph();
            t.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 300));
            t.Add(2, 2, 3, "公滨路", 2, P(0, 300, -400, 300));
            t.Add(3, 2, 4, "黄河路", 3, P(0, 300, 400, 300));
            JrSequence s = Seq(t);
            Program.Check(s.Stops.Count == 1 && s.Stops[0].Through == null && s.Why == "the road does not go on", "a T ends the sequence: " + s.Stops.Count + " " + s.Why);
            Program.Check(s.Stops[0].Has(true) && s.Stops[0].Has(false), "with a road on each side");

            // A bend and then nothing: no junction at all.
            var bend = new Graph();
            bend.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 100));
            bend.Add(2, 2, 3, "长江路", 1, P(0, 100, 0, 300));
            JrSequence none = JunctionSequence.Read(bend, First(bend), JrOptions.Default);
            Program.Check(none.Stops.Count == 0, "no junction ahead: no stops");
            Program.Check(JunctionSequence.Read(null, new JrResult(), JrOptions.Default).Stops.Count == 0 && JunctionSequence.Read(bend, null, JrOptions.Default).Stops.Count == 0, "no graph and no reading: no stops");

            // A crossroads and then a road that ends before another junction.
            Graph dead = Chain(1);
            JrSequence end = Seq(dead);
            Program.Check(end.Stops.Count == 1 && end.Why.Length > 0, "a road that ends after the first junction: one stop and the reason: " + end.Why);

            // A through road whose edge is not in the graph.
            Graph gone = Chain(2);
            JrResult read = First(gone);
            gone.Edges.Remove(100);
            JrSequence lost = JunctionSequence.Read(gone, read, JrOptions.Default);
            Program.Check(lost.Stops.Count == 1 && lost.Why == "the road ahead is not known", "an edge the graph does not know ends it: " + lost.Why);
        }

        private static void Limits()
        {
            Graph g = Chain(6);
            Program.Check(Seq(g).Stops.Count == 3, "six crossroads, three asked for by default: " + Seq(g).Stops.Count);
            Program.Check(Seq(g, 1).Stops.Count == 1 && Seq(g, 2).Stops.Count == 2 && Seq(g, 5).Stops.Count == 5, "and as many as are asked for");
            Program.Check(Seq(g, 4).Stops.Count == 4 && Seq(g, 4).Stops[3].Total > Seq(g, 4).Stops[2].Total, "in order of distance");

            // 590 m between junctions (the walk's own range is 600 m between two): 300, 890, 1480, then 2070 is past the 2000 m of the sequence.
            Graph far = Chain(5, 590f);
            JrSequence s = Seq(far, 5);
            Program.Check(s.Stops.Count == 3 && s.Stops[2].Total < JunctionSequence.Range && s.Why.StartsWith("no more within"), "the range ends the sequence: " + s.Stops.Count + " " + s.Why);
            // The walk's own range (600 m) is for the bends between two junctions: a bend 700 m on and no junction before it ends the sequence.
            Graph bend = Chain(1);
            bend.Edges[100].EndNode = 50;
            bend.Edges[100].Points = P(0, 300, 0, 1000);
            bend.Nodes[5] = new List<int>();
            bend.Nodes[50] = new List<int> { 100, 101 };
            bend.Add(101, 50, 51, "长江路", 1, P(0, 1000, 0, 1900));
            JrSequence gap = Seq(bend, 5);
            Program.Check(gap.Stops.Count == 1 && gap.Why == "no junction within 600 m", "a bend after more than the walk's range ends the sequence: " + gap.Stops.Count + " " + gap.Why);
        }

        /// <summary>The highway north with an off-ramp to the right at z = 300 and an on-ramp from the right at z = 700.</summary>
        private static Graph Diamond(bool highway = true, bool rampsOneWay = true)
        {
            var g = new Graph();
            g.Add(1, 1, 2, "G2", 1, P(0, -400, 0, 300), true, highway);
            g.Add(2, 2, 4, "G2", 1, P(0, 300, 0, 700), true, highway);
            g.Add(3, 2, 3, "东站路", 5, P(0, 300, 60, 420), rampsOneWay, highway);
            g.Add(4, 5, 4, "东站路", 5, P(60, 580, 0, 700), rampsOneWay, highway);
            g.Add(5, 4, 6, "G2", 1, P(0, 700, 0, 1100), true, highway);
            return g;
        }

        private static void Ramps()
        {
            JrSequence s = Seq(Diamond());
            Program.Check(s.Stops.Count == 2, "an off-ramp and an on-ramp: two stops: " + s.Stops.Count + " " + s.Why);
            Program.Check(s.Stops[0].Kind == JrKind.Interchange && s.Stops[1].Kind == JrKind.Interchange, "both are interchanges");
            JrArm off = s.Stops[0].Arms.Find(a => a.Edge == 3), on = s.Stops[1].Arms.Find(a => a.Edge == 4);
            Program.Check(off != null && off.IsRamp && off.Direction == JrDirection.Ahead, "the off-ramp leaves at a small angle (it is 'ahead' to the walk) and is a ramp: " + (off == null ? "none" : off.Angle.ToString("0")));
            Program.Check(on != null && on.IsRamp && !on.CanLeave && on.Direction == JrDirection.Back, "the on-ramp only comes in (it is behind the driver to the walk) and is a ramp: " + (on == null ? "none" : on.Angle.ToString("0")));
            Program.Check(s.Stops[0].Has(false) && !s.Stops[0].Has(true) && s.Stops[1].Has(false) && !s.Stops[1].Has(true), "both ramps are on the right");
            Program.Check(s.Stops[0].Through != null && s.Stops[0].Through.Edge == 2, "the road goes on by the highway, not by the ramp that is as straight as it nearly: " + s.Stops[0].Through?.Edge);

            // The same roads with no highway rules: one-way streets, no ramps.
            JrSequence streets = Seq(Diamond(false));
            Program.Check(streets.Stops.Count == 2 && streets.Stops.TrueForAll(x => x.Kind == JrKind.AtGrade) && streets.Stops.TrueForAll(x => x.Arms.TrueForAll(a => !a.IsRamp)), "one-way streets that are not highway roads are at grade");

            // A city road to an expressway on-ramp: the ramp is the highway kind, the road is not: an interchange.
            Graph city = Diamond(false);
            city.Edges[4].Highway = true;
            city.Edges[3].Highway = true;
            JrSequence mixed = Seq(city);
            Program.Check(mixed.Stops[0].Kind == JrKind.Interchange && mixed.Stops[1].Kind == JrKind.Interchange, "a highway ramp off a city road is an interchange");

            // Two-way roads on a highway are not ramps (a ramp is one-way): the same junction at grade.
            Graph twoWay = Diamond(true, false);
            JrSequence plain = Seq(twoWay);
            Program.Check(plain.Stops[0].Kind == JrKind.AtGrade && plain.Stops[1].Kind == JrKind.AtGrade, "a two-way road off a highway is no ramp");

            // A one-way side street at a plain crossroads.
            Graph oneWay = Chain(2);
            oneWay.Edges[10].RatioBackward = -1f;
            JrSequence side = Seq(oneWay);
            Program.Check(side.Stops[0].Kind == JrKind.AtGrade && side.Stops[1].Kind == JrKind.AtGrade, "a one-way street is not a ramp");
            Program.Check(side.Stops[0].Has(true), "and is still a road on its side");

            Program.Check(JunctionSequence.IsRamp(new JrArm { OneWay = true, Highway = true }, false) && JunctionSequence.IsRamp(new JrArm { OneWay = true }, true) && !JunctionSequence.IsRamp(new JrArm { OneWay = true }, false) && !JunctionSequence.IsRamp(new JrArm { Highway = true }, true), "IsRamp: one-way and highway, on the arm or on its road");
            Program.Check(JunctionSequence.SideOf(new JrArm { Direction = JrDirection.Left }) == JrDirection.Left && JunctionSequence.SideOf(new JrArm { Direction = JrDirection.Ahead }) == JrDirection.Back && JunctionSequence.SideOf(new JrArm { Direction = JrDirection.Right }) == JrDirection.Right, "SideOf: a road's side is its direction");
            Program.Check(JunctionSequence.SideOf(new JrArm { IsRamp = true, Direction = JrDirection.Ahead, Angle = 20f }) == JrDirection.Left && JunctionSequence.SideOf(new JrArm { IsRamp = true, Direction = JrDirection.Back, Angle = -160f }) == JrDirection.Right, "SideOf: a ramp's side is the sign of its angle, whatever the walk called it");
        }

        private static void Through()
        {
            // Two roads go ahead at the junction: the same road, though it bends more, wins over a straighter other road.
            var g = new Graph();
            g.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 300));
            g.Add(2, 2, 3, "长江路", 1, P(0, 300, 20, 700));
            g.Add(3, 2, 4, "学府路", 7, P(0, 300, 0, 700));
            g.Add(4, 3, 5, "长江路", 1, P(20, 700, 20, 900));
            g.Add(5, 3, 6, "公滨路", 8, P(20, 700, -300, 700));
            g.Add(6, 3, 7, "黄河路", 9, P(20, 700, 300, 700));
            g.Edges[2].Name = "别路";
            JrSequence s = Seq(g);
            Program.Check(s.Stops[0].Through.Edge == 2, "the same road (its number) wins over the straighter one, whatever its name: " + s.Stops[0].Through.Edge);

            // No road number: the same name.
            g.Edges[2].Name = "长江路";
            g.Edges[2].Road = 0;
            g.Edges[1].Road = 0;
            JrSequence byName = Seq(g);
            Program.Check(byName.Stops[0].Through.Edge == 2, "the same name when the road is not known: " + byName.Stops[0].Through.Edge);

            // Neither: the straightest.
            g.Edges[2].Name = "别路";
            JrSequence straight = Seq(g);
            Program.Check(straight.Stops[0].Through.Edge == 3, "otherwise the straightest: " + straight.Stops[0].Through.Edge);

            // Equally straight: the one with more lanes.
            var lanes = new Graph();
            lanes.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 300));
            lanes.Add(2, 2, 3, "甲路", 2, P(0, 300, 0, 700));
            lanes.Add(3, 2, 4, "乙路", 3, P(0, 300, 0, 700));
            lanes.Add(4, 2, 5, "丙路", 4, P(0, 300, -300, 300));
            lanes.Edges[3].Lanes.Add(new TwLane());
            lanes.Edges[3].Lanes.Add(new TwLane());
            Program.Check(Seq(lanes).Stops[0].Through.Edge == 3, "equally straight: the wider road");
        }

        private static void Joined()
        {
            // A crossing with two carriageways: the left one meets the road at z = 300, the right one 25 m on; then a crossroads at z = 800.
            var g = new Graph();
            g.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 300));
            g.Add(2, 2, 3, "长江路", 1, P(0, 300, 0, 325));
            g.Add(3, 2, 4, "公滨路", 2, P(0, 300, -400, 300));
            g.Add(4, 3, 5, "公滨路", 2, P(0, 325, 400, 325));
            g.Add(5, 3, 6, "长江路", 1, P(0, 325, 0, 800));
            g.Add(6, 6, 7, "长江路", 1, P(0, 800, 0, 1200));
            g.Add(7, 6, 8, "学府路", 3, P(0, 800, -400, 800));
            g.Add(8, 6, 9, "学院路", 4, P(0, 800, 400, 800));
            JrSequence s = Seq(g);
            Program.Check(s.Stops.Count == 2 && s.Stops[0].Nodes == 2 && s.Stops[1].Nodes == 1, "the two nodes are one junction: " + s.Stops.Count + " stops, " + s.Stops[0].Nodes + " nodes");
            Program.Near(s.Stops[0].Total, 300f, 0.01f, "at the first of them");
            Program.Check(s.Stops[0].Has(true) && s.Stops[0].Has(false), "with a road on the left (of the first node) and on the right (of the second)");
            Program.Check(!s.Stops[0].Arms.Exists(a => a.Edge == 2), "the short road between the nodes is no arm");
            Program.Check(s.Stops[0].Through != null && s.Stops[0].Through.Edge == 5, "the road goes on from the second node");
            Program.Near(s.Stops[1].Total, 800f, 0.01f, "the next junction is measured from the first node and the road: " + s.Stops[1].Total);

            // The joining does not use up a junction of those asked for.
            JrSequence one = Seq(g, 1);
            Program.Check(one.Stops.Count == 1 && one.Stops[0].Nodes == 2 && one.Stops[0].Has(false), "one asked for: the junction is whole");

            // Ramps on both sides of the highway 30 m apart: one interchange with a ramp on each side.
            var h = new Graph();
            h.Add(1, 1, 2, "G2", 1, P(0, -400, 0, 300), true, true);
            h.Add(2, 2, 3, "G2", 1, P(0, 300, 0, 330), true, true);
            h.Add(3, 2, 4, "西站路", 5, P(0, 300, -60, 420), true, true);
            h.Add(4, 3, 5, "东站路", 6, P(0, 330, 60, 450), true, true);
            h.Add(5, 3, 6, "G2", 1, P(0, 330, 0, 900), true, true);
            JrSequence both = Seq(h);
            Program.Check(both.Stops.Count == 1 && both.Stops[0].Kind == JrKind.Interchange && both.Stops[0].Has(true) && both.Stops[0].Has(false), "ramps on both sides: one interchange with both: " + both.Stops.Count);
        }

        private static void Loop()
        {
            // A ring: the road goes on from the second junction round a bend and comes back to the approach of the first (edge 8 is 400 m, the walk's range between two junctions is 600 m).
            var g = new Graph();
            g.Add(1, 1, 2, "环路", 1, P(0, -100, 0, 100));
            g.Add(2, 2, 3, "环路", 1, P(0, 100, 0, 250));
            g.Add(3, 2, 4, "公滨路", 2, P(0, 100, -400, 100));
            g.Add(4, 2, 5, "黄河路", 3, P(0, 100, 400, 100));
            g.Add(5, 3, 6, "环路", 1, P(0, 250, 0, 300));
            g.Add(6, 3, 7, "学府路", 4, P(0, 250, -400, 250));
            g.Add(7, 3, 8, "学院路", 5, P(0, 250, 400, 250));
            JrSequence plain = Seq(g, 5);
            Program.Check(plain.Stops.Count == 2 && plain.Why == "the road ends before a junction", "without the ring: two junctions and a road that ends: " + plain.Stops.Count + " " + plain.Why);
            g.Add(8, 6, 1, "环路", 1, P(0, 300, 0, -100));
            JrSequence ring = Seq(g, 5);
            Program.Check(ring.Stops.Count == 2 && ring.Why == "the road comes back on itself", "the ring ends the sequence at the approach it started from: " + ring.Stops.Count + " " + ring.Why);
            var ids = new HashSet<int>();
            foreach (JrStop stop in ring.Stops)
                Program.Check(ids.Add(stop.Read.ApproachEdge), "no junction is read twice");
        }

        private static void Diagonal()
        {
            // The road runs north-east; its through edge is stored from the far node to the junction and leaves a little east of the diagonal: the way it leaves is read from the end of the edge.
            var g = new Graph();
            g.Add(1, 1, 2, "长江路", 1, P(-300, -300, 0, 0));
            g.Add(2, 3, 2, "长江路", 1, P(400, 350, 0, 0));
            g.Add(3, 2, 4, "公滨路", 2, P(0, 0, -300, 300));
            g.Add(4, 2, 5, "黄河路", 3, P(0, 0, 300, -300));
            g.Add(5, 3, 6, "长江路", 1, P(400, 350, 800, 700));
            g.Add(6, 3, 7, "学府路", 4, P(400, 350, 100, 650));
            g.Add(7, 3, 8, "学院路", 5, P(400, 350, 700, 50));
            JrResult first = JunctionRead.Read(g, 1, -150f, -150f, 0.7071f, 0.7071f, JrOptions.Default);
            JrSequence s = JunctionSequence.Read(g, first, JrOptions.Default);
            Program.Check(s.Stops.Count == 2, "the second junction is found along the stored-backwards edge: " + s.Stops.Count + " " + s.Why);
            if (s.Stops.Count < 2)
                return;
            Program.Check(s.Stops[0].Through != null && s.Stops[0].Through.Edge == 2, "it is the through road");
            Program.Near(s.Stops[1].Total, 212.13f + (float)Math.Sqrt(400f * 400f + 350f * 350f), 0.1f, "at the end of it: " + s.Stops[1].Total);
        }

        // ------------------------------------------------------------------ the board filled in

        private static SignDef Def => SignCatalog.ChoiceById(RoadGuideSigns.MultiId).Def;

        private static GuideSpec Filled(JrResult r, out JrNote note, string spec = null)
        {
            string made = JunctionFill.Apply(Def, spec ?? Def.Template.Default().ToString(), r, out note);
            return made == null ? null : Def.Template.Parse(made);
        }

        private static JrResult Read(Graph g, int max = JunctionSequence.Max, float z = 0f)
        {
            JrResult r = First(g, z);
            r.Sequence = JunctionSequence.Read(g, r, JrOptions.Default, max);
            return r;
        }

        private static void FillAtGrade()
        {
            Program.Check(JunctionFill.CanRead(RoadGuideSigns.MultiId) && JunctionFill.WantsSequence(RoadGuideSigns.MultiId) && !JunctionFill.WantsSequence("gb.guide8.01"), "the board reads the road and wants the sequence; 路1 does not");
            JrResult r = Read(Chain(2));
            GuideSpec s = Filled(r, out JrNote note);
            Program.Check(s != null, "the board is filled in: " + note.Zh);
            Program.Check(s.Get("n") == "2" && s.Get("road") == "长江路", "two junctions and the road's name");
            Program.Check(s.Get("k1") == "p" && s.Get("b1") == "b" && s.Get("lz1") == "左路1" && s.Get("rz1") == "右路1", "the first: at grade, roads on both sides, their names: " + s.Get("lz1") + " " + s.Get("rz1"));
            Program.Check(s.Get("k2") == "p" && s.Get("lz2") == "左路2" && s.Get("rz2") == "右路2", "the second");
            Program.Check(s.Get("le1") == "" && s.Get("re1") == "" && s.Get("xz1") == "" && s.Get("xe1") == "" && s.Get("le2") == "" && s.Get("re2") == "", "no English lines and no interchange name (the board's defaults are another city's)");
            Program.Check(s.Get("lz3") == "" && s.Get("rz3") == "" && s.Get("xz3") == "" && s.Get("xe3") == "", "the junction that was not read is emptied of the defaults' names: " + s.Get("xz3"));
            Program.Check(s.Get("dz") == "长江路" && s.Get("de") == "" && s.Get("cz") == "" && s.Get("ce") == "", "the road ahead is the road that goes on; no plate");
            Program.Check(s.Get("dir") == "北" || s.Get("dir") == CDLC.TS.Geo.TrafficMap.CompassOf(r.HeadX, r.HeadZ), "the compass mark is kept as a mark: " + s.Get("dir"));
            Program.Check(note.Zh.Contains("2 个路口") && note.Zh.Contains("平面") && note.En.Contains("2 junction"), "the note says what was read: " + note.Zh);
            Program.Check(note.ZhNotes.Count == note.EnNotes.Count && note.ZhNotes.Count >= 1, "with notes in both languages");

            // The board that was filled in is a board that lays out.
            GuideBoard board = Def.Template.Layout(s);
            Program.Check(board.Main.W > 0f && board.Main.H > 0f, "and the result is drawn");
            Program.Check(Def.Template.Layout(Def.Template.Default()).Main.H > board.Main.H, "two junctions are a lower board than three");

            // Only the left or only the right.
            var t = new Graph();
            t.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 300));
            t.Add(2, 2, 3, "长江路", 1, P(0, 300, 0, 700));
            t.Add(3, 2, 4, "公滨路", 2, P(0, 300, -400, 300));
            GuideSpec left = Filled(Read(t), out _);
            Program.Check(left.Get("n") == "1" && left.Get("b1") == "l" && left.Get("lz1") == "公滨路" && left.Get("rz1") == "", "a road on the left only: " + left.Get("b1"));
            t.Edges.Remove(3);
            t.Nodes[2].Remove(3);
            t.Add(3, 2, 4, "黄河路", 3, P(0, 300, 400, 300));
            GuideSpec right = Filled(Read(t), out _);
            Program.Check(right.Get("b1") == "r" && right.Get("rz1") == "黄河路" && right.Get("lz1") == "", "and on the right only");

            // A reading without the sequence is the first junction alone.
            JrResult alone = First(Chain(2));
            GuideSpec solo = Filled(alone, out JrNote soloNote);
            Program.Check(solo != null && solo.Get("n") == "1" && solo.Get("lz1") == "左路1", "no sequence: the first junction alone: " + soloNote.Zh);

            // A name the board holds is kept whole (the owner's 克莱斯特伍德公路 was cut to 克莱斯特伍德公 by the old limit of seven); one it cannot hold is left empty and the note says so, never cut to a word that is no name.
            Graph whole = Chain(1);
            whole.Edges[10].Name = "克莱斯特伍德公路";
            GuideSpec kept9 = Filled(Read(whole), out JrNote wholeNote);
            Program.Check(kept9.Get("lz1") == "克莱斯特伍德公路" && !wholeNote.ZhNotes.Exists(x => x.Contains("没有填写")), "a name of nine characters is kept whole: " + kept9.Get("lz1"));
            Graph longName = Chain(1);
            longName.Edges[10].Name = "中山南二路东延伸线北段辅路";
            GuideSpec cut = Filled(Read(longName), out JrNote cutNote);
            Program.Check(cut.Get("lz1") == "" && cutNote.ZhNotes.Exists(x => x.Contains("没有填写")) && cutNote.EnNotes.Exists(x => x.Contains("not filled in")), "a name too long for the board is left empty and said so, in both languages: [" + cut.Get("lz1") + "]");

            // What the player set that the reading does not decide is kept.
            string spec = Def.Template.Default().With("speed", "30").ToString();
            GuideSpec kept = Filled(Read(Chain(2)), out _, spec);
            Program.Check(kept.Get("speed") == "30", "the band the player chose is kept");
            Program.Check(Filled(Read(Chain(2)), out _, Def.Template.Default().With("dir", "").ToString()).Get("dir") == "", "an empty compass mark stays empty");
            Program.Check(Filled(null, out JrNote nothing) == null && nothing.Zh.Length > 0, "nothing read: nothing filled in, and the note says so");
            Program.Check(Filled(new JrResult { Why = "the road ends before a junction" }, out JrNote ends) == null && ends.Zh.Contains("路口之前"), "no junction found: the reason: " + ends.Zh);
        }

        private static void FillInterchange()
        {
            JrResult r = Read(Diamond());
            GuideSpec s = Filled(r, out JrNote note);
            Program.Check(s != null && s.Get("n") == "2", "a diamond's two ramps are two junctions: " + note.Zh);
            Program.Check(s.Get("k1") == "d" && s.Get("k2") == "d", "both are interchanges drawn with ramps");
            Program.Check(s.Get("b1") == "r" && s.Get("b2") == "r", "with the ramps on the right");
            Program.Check(s.Get("xz1") == "东站路立交" && s.Get("xz2") == "东站路立交", "named after the ramps' road: " + s.Get("xz1"));
            Program.Check(s.Get("rz1") == "" && s.Get("lz1") == "", "the ramps' road is not also the name of a side road");
            Program.Check(s.Get("dz") == "G2" && note.Zh.Contains("互通") && note.En.Contains("interchange"), "the road ahead and the note");
            Program.Check(note.ZhNotes.Exists(x => x.Contains("匝道")), "the note says how an interchange is judged");
            GuideBoard board = Def.Template.Layout(s);
            Program.Check(board.Main.W > 0f, "and the board is drawn");

            // An interchange whose ramps have no name has no name; a name that already ends in 立交 is not doubled; a name with no Chinese is kept as it is.
            Graph unnamed = Diamond();
            unnamed.Edges[3].Name = "";
            unnamed.Edges[4].Name = "";
            Program.Check(Filled(Read(unnamed), out _).Get("xz1") == "", "ramps with no name: no name for the interchange");
            Graph done = Diamond();
            done.Edges[3].Name = "东站立交";
            Program.Check(Filled(Read(done), out _).Get("xz1") == "东站立交", "not doubled");
            Graph linked = Diamond();
            linked.Edges[3].Name = "东站互通";
            Program.Check(Filled(Read(linked), out _).Get("xz1") == "东站互通", "a name that ends in 互通 is not given 立交 either");
            Graph latin = Diamond();
            latin.Edges[3].Name = "Ramp 12";
            Program.Check(Filled(Read(latin), out _).Get("xz1") == "Ramp 12", "a name with no Chinese is not given 立交");

            // A name made up for an interchange (the road's name and 立交) that would be longer than the board's ten characters is left empty, never cut; a short one is made.
            Graph longRamp = Diamond();
            longRamp.Edges[3].Name = "克莱斯特伍德公路南段";
            longRamp.Edges[4].Name = "克莱斯特伍德公路南段";
            GuideSpec made = Filled(Read(longRamp), out JrNote madeNote);
            Program.Check(made != null && made.Get("xz1") == "", "a made-up interchange name of twelve characters is left empty: [" + (made != null ? made.Get("xz1") : "null") + "]");
            Program.Check(madeNote.ZhNotes.Exists(x => x.Contains("没有填写") && x.Contains("立交")) && madeNote.EnNotes.Exists(x => x.Contains("not filled in")), "and the note says so, in both languages (the made-up name is in it)");
            Graph edge = Diamond();
            edge.Edges[3].Name = "克莱斯特伍德公路";
            edge.Edges[4].Name = "克莱斯特伍德公路";
            Program.Check(Filled(Read(edge), out _).Get("xz1") == "克莱斯特伍德公路立交", "and one of exactly ten is made");
            Graph shortRamp = Diamond();
            shortRamp.Edges[3].Name = "东站路";
            shortRamp.Edges[4].Name = "东站路";
            Program.Check(Filled(Read(shortRamp), out _).Get("xz1") == "东站路立交", "and a short one is made");

            // Ramps on both sides at one junction: both sides, the left ramp's name first.
            var h = new Graph();
            h.Add(1, 1, 2, "G2", 1, P(0, -400, 0, 300), true, true);
            h.Add(2, 2, 3, "G2", 1, P(0, 300, 0, 330), true, true);
            h.Add(3, 2, 4, "西站路", 5, P(0, 300, -60, 420), true, true);
            h.Add(4, 3, 5, "东站路", 6, P(0, 330, 60, 450), true, true);
            h.Add(5, 3, 6, "G2", 1, P(0, 330, 0, 900), true, true);
            GuideSpec both = Filled(Read(h), out JrNote bothNote);
            Program.Check(both.Get("n") == "1" && both.Get("k1") == "d" && both.Get("b1") == "b" && both.Get("xz1") == "西站路立交", "ramps on both sides: " + both.Get("b1") + " " + both.Get("xz1"));
            Program.Check(bothNote.ZhNotes.Exists(x => x.Contains("按一个画")) && bothNote.EnNotes.Exists(x => x.Contains("as one")), "the joining is noted in both languages");

            // A junction with a ramp and an ordinary road: the ordinary road is a side road with its name.
            Graph mix = Diamond();
            mix.Add(10, 2, 20, "公滨路", 9, P(0, 300, -400, 300));
            GuideSpec side = Filled(Read(mix), out _);
            Program.Check(side.Get("k1") == "d" && side.Get("b1") == "b" && side.Get("lz1") == "公滨路" && side.Get("rz1") == "" && side.Get("xz1") == "东站路立交", "a ramp and an ordinary road: " + side.Get("b1") + " " + side.Get("lz1"));

            // A fork whose roads both go on has nothing to draw.
            var fork = new Graph();
            fork.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 300));
            fork.Add(2, 2, 3, "甲路", 2, P(0, 300, 0, 700));
            fork.Add(3, 2, 4, "乙路", 3, P(0, 300, 5, 700));
            JrNote forkNote;
            Program.Check(Filled(Read(fork), out forkNote) == null && forkNote.Zh.Contains("没有可以画的路口"), "a fork with no road to a side: nothing is filled in: " + forkNote.Zh);

            // A fork before a crossroads: the fork is left out and said so.
            var both2 = new Graph();
            both2.Add(1, 1, 2, "长江路", 1, P(0, -400, 0, 300));
            both2.Add(2, 2, 3, "长江路", 1, P(0, 300, 0, 700));
            both2.Add(3, 2, 4, "乙路", 3, P(0, 300, 5, 700));
            both2.Add(4, 3, 5, "长江路", 1, P(0, 700, 0, 1100));
            both2.Add(5, 3, 6, "左路", 4, P(0, 700, -400, 700));
            both2.Add(6, 3, 7, "右路", 5, P(0, 700, 400, 700));
            GuideSpec skipped = Filled(Read(both2), out JrNote skipNote);
            Program.Check(skipped != null && skipped.Get("n") == "1" && skipped.Get("lz1") == "左路" && skipNote.ZhNotes.Exists(x => x.Contains("没有画")), "the fork is left out, the crossroads is the first: " + skipNote.Zh);
        }
    }
}
