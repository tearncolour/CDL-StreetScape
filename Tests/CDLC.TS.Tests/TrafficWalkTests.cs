using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The walk that makes a traffic-state screen's network from the road graph: the junctions counted, the range, the stubs, the directions, the levels, and the settings and signature around it.</summary>
    internal static class TrafficWalkTests
    {
        private sealed class Graph : ITrafficGraph
        {
            public readonly Dictionary<int, TwEdge> Edges = new Dictionary<int, TwEdge>();
            public readonly Dictionary<int, List<int>> Nodes = new Dictionary<int, List<int>>();

            public TwEdge Add(int id, int start, int end, float[] points, float forward = 1f, float backward = 1f, string name = "", int road = 0)
            {
                var edge = new TwEdge { Id = id, StartNode = start, EndNode = end, Points = points, RatioForward = forward, RatioBackward = backward, Name = name, Road = road };
                Edges[id] = edge;
                foreach (int node in new[] { start, end })
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

        // Nodes: 1 (0,-100), 2 = B (0,300), 3 = C (0,600), 4 = D (0,900), 5 (0,1200); sides 10..15; the sign stands at (0,0) on edge 1; the traffic goes north.
        private static Graph Grid(bool swap = false)
        {
            float[] P(params float[] xz)
            {
                if (!swap)
                    return xz;
                var r = new float[xz.Length];
                for (int i = 0; i < xz.Length; i += 2)
                {
                    r[i] = xz[i + 1];
                    r[i + 1] = xz[i];
                }

                return r;
            }

            var g = new Graph();
            g.Add(1, 1, 2, P(0, -100, 0, 300), name: "东海西路", road: 1);
            g.Add(2, 2, 3, P(0, 300, 0, 600), name: "东海西路", road: 1);
            g.Add(3, 2, 10, P(0, 300, -300, 300), forward: 0.2f, backward: 1f, name: "延安三路", road: 2);   // leaves B at its start: driven forward
            g.Add(4, 11, 2, P(300, 300, 0, 300), forward: 1f, backward: 0.2f, name: "延安三路", road: 2);    // ends at B: leaving B is driven backward
            g.Add(5, 3, 4, P(0, 600, 0, 900), name: "东海西路", road: 1);
            g.Add(6, 3, 12, P(0, 600, -300, 600), name: "山东路", road: 3);
            g.Add(7, 13, 3, P(300, 600, 0, 600), forward: 0.9f, backward: -1f, name: "山东路", road: 3);     // one way, into C
            g.Add(8, 4, 5, P(0, 900, 0, 1200), name: "东海西路", road: 1);
            g.Add(9, 4, 14, P(0, 900, -300, 900), name: "太平角四路", road: 4);
            g.Add(10, 4, 15, P(0, 900, 300, 900), name: "太平角四路", road: 4);
            return g;
        }

        private static int Count(TmNetwork net, string name)
        {
            int n = 0;
            foreach (TmEdge e in net.Edges)
                n += e.Name == name ? 1 : 0;
            return n;
        }

        private static TwOptions Options(int junctions = 3, float range = 800f, int max = 80) => new TwOptions { Junctions = junctions, Range = range, MaxEdges = max, Stub = 160f, Behind = 20f };

        public static void RunAll()
        {
            Program.Run("traffic walk: the junctions ahead are counted and the roads that leave them drawn", Junctions);
            Program.Run("traffic walk: the level is of the way a road is driven", Levels);
            Program.Run("traffic walk: range, number of stretches and roads behind the sign", Limits);
            Program.Run("traffic walk: the direction of the sign and of the edge", Directions);
            Program.Run("traffic walk: a ring, a missing edge, no heading", Odd);
            Program.Run("traffic walk: the settings, the decoration and the signature", Around);
        }

        private static void Junctions()
        {
            // Two junctions ahead: the edge of the sign, the three roads at B, the three at C; D's are not.
            Graph g = Grid();
            TmNetwork two = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(2), null);
            Program.Check(two.Known, "known");
            Program.Check(two.Edges.Count == 7, "two junctions: 7 stretches (the sign's, 3 at B, 3 at C): " + two.Edges.Count);
            Program.Check(Count(two, "东海西路") == 3 && Count(two, "延安三路") == 2 && Count(two, "山东路") == 2 && Count(two, "太平角四路") == 0, "which: " + Count(two, "东海西路") + "," + Count(two, "延安三路") + "," + Count(two, "山东路"));

            // Three: D's three as well. One: only B's.
            // D is 900 m from the sign along the road: a range of 1000.
            TmNetwork three = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(3, 1000f), null);
            Program.Check(three.Edges.Count == 10 && Count(three, "太平角四路") == 2, "three junctions: 10 stretches: " + three.Edges.Count);
            TmNetwork one = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(1), null);
            Program.Check(one.Edges.Count == 4, "one junction: the sign's and the three at B: " + one.Edges.Count);

            // The first stretch begins at the sign (z 0), not at the start of its edge (z -100), and ends at B.
            TmEdge first = three.Edges[0];
            Program.Near(first.Points[1], 0f, 0.01f, "the stretch starts at the sign: " + first.Points[1]);
            Program.Near(first.Points[first.Points.Length - 1], 300f, 0.01f, "and ends at the junction");

            // Roads that leave a junction start at it, whichever way their edge was drawn: 4 is stored from the east to B, drawn from B to the east.
            TmEdge east = null;
            foreach (TmEdge e in three.Edges)
            {
                if (e.Name == "延安三路" && e.Points[e.Points.Length - 2] > 0f)
                    east = e;
            }

            Program.Check(east != null && Math.Abs(east.Points[0]) < 0.01f && Math.Abs(east.Points[2] - 300f) < 0.01f, "a road drawn the other way is turned to leave the junction");

            // A bend in the road is not a junction: a node with two roads does not count, and the walk goes on through it.
            Graph bend = new Graph();
            bend.Add(1, 1, 2, new float[] { 0, -100, 0, 100 });
            bend.Add(2, 2, 3, new float[] { 0, 100, 0, 200 });      // node 2 has two roads: a bend
            bend.Add(3, 3, 4, new float[] { 0, 200, 0, 300 });      // node 3 has two roads
            bend.Add(4, 4, 5, new float[] { 0, 300, 0, 400 });
            bend.Add(5, 4, 6, new float[] { 0, 300, 100, 300 });    // node 4 is the first junction
            TmNetwork past = TrafficWalk.Walk(bend, 1, 0f, 0f, 0f, 1f, Options(1), null);
            Program.Check(past.Edges.Count == 5, "through two bends to the junction, then its exits: " + past.Edges.Count);

            // After the last junction a road is followed only a little way (the stub): a road of short edges is, one of a long edge is not.
            var longEdges = Grid();
            TmNetwork stubs = TrafficWalk.Walk(longEdges, 1, 0f, 0f, 0f, 1f, Options(2), null);
            Program.Check(Count(stubs, "东海西路") == 3 && stubs.Edges.TrueForAll(e => e.Road != 4), "a road of 300 m past the last junction is not followed (the stub is 160 m)");
            var shorts = new Graph();
            shorts.Add(1, 1, 2, new float[] { 0, -100, 0, 100 });
            shorts.Add(2, 2, 3, new float[] { 0, 100, 0, 150 });
            shorts.Add(3, 2, 6, new float[] { 0, 100, 80, 100 });
            shorts.Add(4, 3, 4, new float[] { 0, 150, 0, 220 });
            shorts.Add(5, 4, 5, new float[] { 0, 220, 0, 600 });
            TmNetwork short1 = TrafficWalk.Walk(shorts, 1, 0f, 0f, 0f, 1f, Options(1), null);
            Program.Check(short1.Edges.Count == 5, "short edges past the junction are followed up to the stub: " + short1.Edges.Count);
            // 50 + 70 = 120 m from the junction by the end of edge 4; edge 5 would take it to 500 m, past 160.
            bool longest = false;
            foreach (TmEdge e in short1.Edges)
                longest |= e.Points[e.Points.Length - 1] > 300f;
            Program.Check(longest, "the edge that ends past the stub is drawn but its end is not followed: it is the last (" + short1.Edges.Count + ")");
        }

        private static void Levels()
        {
            Graph g = Grid();
            var keys = new List<long>();
            var ratios = new List<float>();
            TmNetwork net = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(2), (key, ratio) =>
            {
                keys.Add(key);
                ratios.Add(ratio);
                return TrafficMap.LevelOf(ratio, 0.7f, 0.4f);
            });

            TrafficLevel LevelOfRoad(int road, Func<TmEdge, bool> pick)
            {
                foreach (TmEdge e in net.Edges)
                {
                    if (e.Road == road && pick(e))
                        return e.Level;
                }

                return TrafficLevel.Unknown;
            }

            // Edge 3 (west) is driven forward at 0.2: a jam. Edge 4 (east) is driven backward at 0.2 as it leaves B: a jam. The main road is free.
            Program.Check(LevelOfRoad(2, e => e.Points[e.Points.Length - 2] < 0f) == TrafficLevel.Jam, "the west road, driven with its edge at 0.2");
            Program.Check(LevelOfRoad(2, e => e.Points[e.Points.Length - 2] > 0f) == TrafficLevel.Jam, "the east road, driven against its edge at 0.2");
            Program.Check(LevelOfRoad(1, e => true) == TrafficLevel.Free, "the main road is free");

            // The one-way road into C has no lane away from C: it is shown with the state of the way it is driven (0.9: free), and was asked about with the key of that way.
            Program.Check(LevelOfRoad(3, e => e.Points[e.Points.Length - 2] > 0f) == TrafficLevel.Free, "the one-way road into the junction is shown, free");
            Program.Check(keys.Contains(TrafficWalk.KeyOf(7, true)) && !keys.Contains(TrafficWalk.KeyOf(7, false)), "its key is of the way it is driven");
            Program.Check(keys.Contains(TrafficWalk.KeyOf(3, true)) && keys.Contains(TrafficWalk.KeyOf(4, false)), "the keys are of the edge and the way: " + string.Join(",", keys));
            Program.Check(TrafficWalk.KeyOf(5, true) != TrafficWalk.KeyOf(5, false) && TrafficWalk.KeyOf(5, true) != TrafficWalk.KeyOf(6, true), "keys are distinct");

            // The one-way road is not followed: nothing beyond node 13.
            TmNetwork deep = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(3, 1000f), null);
            Program.Check(deep.Edges.Count == 10, "a one-way road into a junction is shown and not followed: " + deep.Edges.Count);

            // The default classification (no caller) is the plain one.
            TmNetwork plain = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(2), null);
            Program.Check(plain.Edges[0].Level == TrafficLevel.Free, "the default level of a free road is Free");
        }

        private static void Limits()
        {
            Graph g = Grid();
            // Range: 500 m from the sign. The sign's stretch ends at B (300 m); the roads at B are 300 m long: drawn, not followed.
            TmNetwork range = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(5, 500f), null);
            Program.Check(range.Edges.Count == 4, "range 500: the sign's stretch and what leaves B: " + range.Edges.Count);
            TmNetwork wide = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(5, 4000f), null);
            Program.Check(wide.Edges.Count == 10, "a wide range gives the whole grid: " + wide.Edges.Count);

            // The number of stretches: the nearest are kept.
            TmNetwork capped = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, Options(5, 4000f, 5), null);
            Program.Check(capped.Edges.Count == 5, "capped at 5: " + capped.Edges.Count);
            Program.Check(capped.Edges[0].Road == 1 && Count(capped, "太平角四路") == 0, "the nearest are kept, the far ones left out");

            // A road that goes back behind the sign (the other carriageway) is left out; one that goes a little way behind (a bend) is cut.
            var behind = new Graph();
            behind.Add(1, 1, 2, new float[] { 0, -100, 0, 300 });
            behind.Add(2, 2, 3, new float[] { 0, 300, 0, 600 });
            behind.Add(3, 2, 4, new float[] { 0, 300, -10, 100, -10, -300 });
            behind.Add(4, 2, 5, new float[] { 0, 300, 30, 200, 60, -5, 60, -400 });
            TmNetwork bnet = TrafficWalk.Walk(behind, 1, 0f, 0f, 0f, 1f, Options(2), null);
            Program.Check(bnet.Edges.Count == 4, "the stretch of the sign, the road on and the two roads that turn back, cut where they pass 20 m behind the sign: " + bnet.Edges.Count);
            Program.Check(bnet.Edges[2].Points.Length == 4 && bnet.Edges[3].Points.Length == 6, "cut at the first point more than 20 m behind: " + bnet.Edges[2].Points.Length + ", " + bnet.Edges[3].Points.Length);
            foreach (TmEdge e in bnet.Edges)
            {
                for (int i = 1; i < e.Points.Length; i += 2)
                    Program.Check(e.Points[i] >= -20f, "no point more than 20 m behind the sign: " + e.Points[i]);
            }

            // The first edge: a sign part-way along it. The sign at z=100 on an edge from -100 to 300.
            TmNetwork mid = TrafficWalk.Walk(g, 1, 0f, 100f, 0f, 1f, Options(1), null);
            Program.Near(mid.Edges[0].Points[1], 100f, 0.01f, "starts where the sign is: " + mid.Edges[0].Points[1]);
            // The sign at the side of the road (its position 8 m to the side) is projected on the road.
            TmNetwork side = TrafficWalk.Walk(g, 1, 8f, 100f, 0f, 1f, Options(1), null);
            Program.Near(side.Edges[0].Points[1], 100f, 0.01f, "a sign beside the road starts at its place along it: " + side.Edges[0].Points[1]);
        }

        private static void Directions()
        {
            // The traffic going south on the same road: from the sign down to the dead end at node 1.
            Graph g = Grid();
            TmNetwork south = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, -1f, Options(3, 1000f), null);
            Program.Check(south.Edges.Count == 1 && south.Edges[0].Points[south.Edges[0].Points.Length - 1] < -99f, "heading south: the stretch down to the end of the edge, nothing past it: " + south.Edges.Count);

            // The traffic going east on the same graph turned onto its side (x and z swapped): the same shape.
            Graph turned = Grid(true);
            TmNetwork east = TrafficWalk.Walk(turned, 1, 0f, 0f, 1f, 0f, Options(3, 1000f), null);
            Program.Check(east.Edges.Count == 10, "heading east on the turned grid: 10 stretches: " + east.Edges.Count);
            Program.Near(east.Edges[0].Points[0], 0f, 0.01f, "starts at the sign");
            Program.Near(east.Edges[0].Points[east.Edges[0].Points.Length - 2], 300f, 0.01f, "and runs east");

            // The heading is stored as a unit vector, whatever was given.
            TmNetwork scaled = TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 7f, Options(2), null);
            Program.Near(scaled.HeadZ, 1f, 1e-5f, "the heading is made a unit vector");
            TmNetwork slant = TrafficWalk.Walk(g, 1, 0f, 0f, 0.3f, 0.9f, Options(2), null);
            Program.Check(slant.Edges.Count == 7, "a heading a little off the road's is still the road's way: " + slant.Edges.Count);

            // A one-way road that runs against the traffic the sign faces: the road is shown with the state of its own way.
            var against = new Graph();
            against.Add(1, 1, 2, new float[] { 0, -100, 0, 300 }, forward: -1f, backward: 0.3f);
            TmNetwork a = TrafficWalk.Walk(against, 1, 0f, 0f, 0f, 1f, Options(2), (key, ratio) => TrafficMap.LevelOf(ratio, 0.7f, 0.4f));
            Program.Check(a.Edges.Count == 1 && a.Edges[0].Level == TrafficLevel.Jam, "the edge has lanes only against the way: shown with its own state");
        }

        private static void Odd()
        {
            // A ring of roads (roundabout): the walk ends.
            var ring = new Graph();
            ring.Add(1, 1, 2, new float[] { 0, -100, 0, 100 });
            ring.Add(2, 2, 3, new float[] { 0, 100, 60, 160 });
            ring.Add(3, 3, 4, new float[] { 60, 160, 0, 220 });
            ring.Add(4, 4, 2, new float[] { 0, 220, 0, 100 });
            ring.Add(5, 3, 5, new float[] { 60, 160, 200, 160 });
            TmNetwork net = TrafficWalk.Walk(ring, 1, 0f, 0f, 0f, 1f, Options(5), null);
            Program.Check(net.Known && net.Edges.Count <= 5 && net.Edges.Count >= 3, "a ring ends: " + net.Edges.Count);

            // A loop edge (one node at both ends) is taken once.
            var loop = new Graph();
            loop.Add(1, 1, 2, new float[] { 0, -100, 0, 100 });
            loop.Add(2, 2, 2, new float[] { 0, 100, 50, 150, 0, 200, -50, 150, 0, 100 });
            loop.Add(3, 2, 3, new float[] { 0, 100, 0, 400 });
            TmNetwork l = TrafficWalk.Walk(loop, 1, 0f, 0f, 0f, 1f, Options(3), null);
            Program.Check(l.Edges.Count <= 4, "a loop edge does not repeat: " + l.Edges.Count);

            // Nothing: a missing edge, an edge with no points, a heading of zero, a null graph.
            Graph g = Grid();
            Program.Check(!TrafficWalk.Walk(g, 99, 0f, 0f, 0f, 1f, Options(), null).Known, "a missing edge is unknown");
            Program.Check(!TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 0f, Options(), null).Known, "no heading is unknown");
            Program.Check(!TrafficWalk.Walk(null, 1, 0f, 0f, 0f, 1f, Options(), null).Known, "no graph is unknown");
            var empty = new Graph();
            empty.Add(1, 1, 2, new float[0]);
            Program.Check(!TrafficWalk.Walk(empty, 1, 0f, 0f, 0f, 1f, Options(), null).Known, "an edge without points is unknown");

            // An edge with a single segment, an edge of zero length at the sign, a neighbour with no lanes (a footpath).
            var thin = new Graph();
            thin.Add(1, 1, 2, new float[] { 0, 0, 0, 0 });
            thin.Add(2, 2, 3, new float[] { 0, 0, 0, 100 }, forward: -1f, backward: -1f);
            TmNetwork t = TrafficWalk.Walk(thin, 1, 0f, 0f, 0f, 1f, Options(), null);
            Program.Check(t.Known && t.Edges.Count == 1, "a zero length edge does not throw, an edge without car lanes is not a road: " + t.Edges.Count);

            // Many edges at one node (a star of 40): capped, not thrown.
            var star = new Graph();
            star.Add(1, 1, 2, new float[] { 0, -100, 0, 50 });
            for (int i = 0; i < 40; i++)
                star.Add(10 + i, 2, 100 + i, new float[] { 0, 50, (float)Math.Cos(i) * 100f, 50f + (float)Math.Abs(Math.Sin(i)) * 100f });
            TmNetwork s = TrafficWalk.Walk(star, 1, 0f, 0f, 0f, 1f, Options(3, 800f, 30), null);
            Program.Check(s.Edges.Count == 30, "a star of 40 roads is capped at the number of stretches: " + s.Edges.Count);
        }

        private static void Around()
        {
            // The item and the revision.
            Program.Check(TrafficMap.IsTrafficScreen(SignCatalog.HashOf(TrafficMap.TemplateId)) && !TrafficMap.IsTrafficScreen(SignCatalog.HashOf("gb.vms.lane")), "which hash is a traffic screen");
            string decorated = TrafficMap.Decorate("", 12345, 7);
            GuideTemplate template = SignCatalog.ChoiceById(TrafficMap.TemplateId).Def.Template;
            GuideSpec back = GuideSpec.Parse(template, decorated);
            Program.Check(back.Get("id") == "12345" && back.Get("rev") == "7", "the id and the revision are in the settings: " + decorated);
            string custom = TrafficMap.Decorate(template.Default().With("size", "l").With("jn", "5").ToString(), 1, 2);
            GuideSpec c = GuideSpec.Parse(template, custom);
            Program.Check(c.Get("size") == "l" && c.Get("jn") == "5" && c.Get("id") == "1" && c.Get("rev") == "2", "the item's own settings are kept");
            // Decorating again replaces, it does not add.
            string again = TrafficMap.Decorate(decorated, 12345, 8);
            Program.Check(GuideSpec.Parse(template, again).Get("rev") == "8" && again.Split(new[] { "rev=" }, StringSplitOptions.None).Length == 2, "the revision is replaced: " + again);
            Program.Check(Math.Abs(decorated.Length - again.Length) <= 1, "and the length does not grow");

            // The settings the reader takes.
            TrafficMap.Settings d = TrafficMap.SettingsOf("");
            Program.Check(d.Junctions == 3 && d.Range == 800f && Math.Abs(d.SlowBelow - 0.7f) < 1e-5f && Math.Abs(d.JamBelow - 0.4f) < 1e-5f, "defaults");
            TrafficMap.Settings custom2 = TrafficMap.SettingsOf(template.Default().With("jn", "5").With("range", "2000").With("slow", "80").With("jam", "50").ToString());
            Program.Check(custom2.Junctions == 5 && custom2.Range == 2000f && Math.Abs(custom2.SlowBelow - 0.8f) < 1e-5f && Math.Abs(custom2.JamBelow - 0.5f) < 1e-5f, "settings read");
            TrafficMap.Settings odd = TrafficMap.SettingsOf(template.Default().With("slow", "30").With("jam", "90").ToString());
            Program.Check(odd.SlowBelow == 0.3f && odd.JamBelow <= odd.SlowBelow - 0.05f + 1e-5f && odd.JamBelow >= 0.05f, "a jam limit above the slow limit is brought under it: " + odd.SlowBelow + "/" + odd.JamBelow);
            TrafficMap.Settings tiny = TrafficMap.SettingsOf(template.Default().With("slow", "5").With("jam", "1").ToString());
            Program.Check(tiny.SlowBelow == 0.2f && tiny.JamBelow >= 0.05f && tiny.JamBelow < tiny.SlowBelow, "limits are kept sane: " + tiny.SlowBelow + "/" + tiny.JamBelow);
            Program.Check(TrafficMap.SettingsOf("garbage;;==").Junctions == 3, "garbage is the defaults");

            // The signature: the same picture, the same number; another picture, another.
            TmNetwork a = TrafficMap.Demo();
            TmNetwork b = TrafficMap.Demo();
            Program.Check(TrafficMap.Signature(a) == TrafficMap.Signature(b), "the same picture has the same signature");
            b.Edges[1].Level = TrafficLevel.Jam;
            Program.Check(TrafficMap.Signature(a) != TrafficMap.Signature(b), "a level changed");
            TmNetwork c2 = TrafficMap.Demo();
            c2.Edges[0].Name = "东海东路";
            Program.Check(TrafficMap.Signature(a) != TrafficMap.Signature(c2), "a name changed");
            TmNetwork d2 = TrafficMap.Demo();
            d2.Edges[2].Points[3] += 5f;
            Program.Check(TrafficMap.Signature(a) != TrafficMap.Signature(d2), "a point moved by 5 m");
            TmNetwork e2 = TrafficMap.Demo();
            e2.Edges[2].Points[3] += 0.2f;
            Program.Check(TrafficMap.Signature(a) == TrafficMap.Signature(e2), "a point moved by 20 cm is the same picture");
            TmNetwork f2 = TrafficMap.Demo();
            f2.Edges.RemoveAt(0);
            Program.Check(TrafficMap.Signature(a) != TrafficMap.Signature(f2), "a stretch gone");
            Program.Check(TrafficMap.Signature(null) == TrafficMap.Signature(new TmNetwork()), "nothing is nothing");

            // The registry forgets an item without announcing a change.
            int id = 424242;
            TrafficMap.Set(id, a, 3);
            int changes = TrafficMap.Changes;
            TrafficMap.Remove(id);
            Program.Check(TrafficMap.Get(id) == null && TrafficMap.RevisionOf(id) == 0 && TrafficMap.Changes == changes, "removed, and nothing is drawn differently");
            TrafficMap.Clear();
        }
    }
}
