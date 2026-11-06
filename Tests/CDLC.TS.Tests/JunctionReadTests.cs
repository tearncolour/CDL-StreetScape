using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The junction ahead of a sign, read from a made-up road graph (JunctionRead), and the junction signs filled in from it (JunctionFill): the form, the names of each direction, the lanes, the notes.</summary>
    internal static class JunctionReadTests
    {
        public static void RunAll()
        {
            Program.Run("junction read: the first junction ahead, over the bends", Finding);
            Program.Run("junction read: the roads that leave it and the direction each has", Directions);
            Program.Run("junction read: the lanes of the approach, from the driver's left", Lanes);
            Program.Run("junction read: a graph as the game's reader builds it (lanes, no speed ratios)", GameBuilt);
            Program.Run("junction fill: 路1 form, names, the crossing road, the compass", FillDiagram);
            Program.Run("junction fill: 路3, 路6 and 路7", FillOthers);
            Program.Run("junction fill: 路4 lanes and arrows", FillLanes);
            Program.Run("junction fill: notes, long names, nothing to fill", Notes);
        }

        private sealed class Graph : ITrafficGraph
        {
            public readonly Dictionary<int, TwEdge> Edges = new Dictionary<int, TwEdge>();
            public readonly Dictionary<int, List<int>> Nodes = new Dictionary<int, List<int>>();

            public TwEdge Add(int id, int a, int b, string name, int road, params float[] xz)
            {
                var edge = new TwEdge { Id = id, StartNode = a, EndNode = b, Points = xz, RatioForward = 1f, RatioBackward = 1f, Name = name, Road = road };
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

        /// <summary>The approach edge 1 runs north from (0, -400) to the junction at node 2 (0, 300); the roads that leave node 2: 2 ahead, 3 to the left, 4 to the right.</summary>
        private static Graph Cross(string ahead = "学府路", string left = "公滨路", string right = "黄河路", int roadLeft = 2, int roadRight = 3)
        {
            var g = new Graph();
            g.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            g.Add(2, 2, 3, ahead, 4, 0, 300, 0, 700);
            g.Add(3, 2, 4, left, roadLeft, 0, 300, -400, 300);
            g.Add(4, 2, 5, right, roadRight, 0, 300, 400, 300);
            return g;
        }

        private static JrResult Read(Graph g, float z = 0f, float hx = 0f, float hz = 1f, float x = 0f) => JunctionRead.Read(g, 1, x, z, hx, hz, JrOptions.Default);

        private static SignDef Def(string id) => SignCatalog.ChoiceById(id).Def;

        private static string Fill(string id, string spec, JrResult r, out JrNote note) => JunctionFill.Apply(Def(id), spec, r, out note);

        private static GuideSpec Parsed(string id, string spec) => Def(id).Template.Parse(spec);

        /// <summary>
        /// The game's reader (GameRoadGraph with the lanes read) gives an edge lanes and no speed ratios: since 0.12.29 the ratios are RIE's. HasLanes was the ratios' sign, so every edge was no road to the walk
        /// and the junction of the owner's city ("the road ends before a junction", 2 Nov) was never found; the fixtures above set ratios and could not tell.
        /// </summary>
        private static void GameBuilt()
        {
            Graph g = Cross();
            foreach (TwEdge edge in g.Edges.Values)
            {
                edge.RatioForward = edge.RatioBackward = -1f;
                edge.Lanes.Add(new TwLane { Lateral = 1.5f, WithEdge = true });
                edge.Lanes.Add(new TwLane { Lateral = -1.5f, WithEdge = false });
            }

            JrResult unread = Read(g);
            Program.Check(!unread.Found && unread.Why == "the road ends before a junction", "with lanes and no ratios the walk sees no road at all: " + unread.Why);

            foreach (TwEdge edge in g.Edges.Values)
                edge.DirectionsFromLanes();
            JrResult r = Read(g);
            Program.Check(r.Found && r.ApproachEdge == 1 && r.Arms.Count == 3, "once the directions are taken from the lanes the crossroads is found with its three roads: " + r.Why + " " + r.Arms.Count);
            Program.Near(r.Distance, 300f, 0.01f, "at 300 m: " + r.Distance);
            Program.Check(r.Arms.TrueForAll(a => a.CanLeave), "a road with a lane each way can be left");

            // One way: only lanes with the edge (a road that leaves the junction) and only against it (a road that only comes in).
            var oneWay = new TwEdge();
            oneWay.Lanes.Add(new TwLane { WithEdge = true });
            oneWay.DirectionsFromLanes();
            Program.Check(oneWay.RatioForward >= 0f && oneWay.RatioBackward < 0f && oneWay.HasLanes, "lanes with the edge only: forward has lanes, backward has none");
            var inbound = new TwEdge();
            inbound.Lanes.Add(new TwLane { WithEdge = false });
            inbound.Lanes.Add(new TwLane { WithEdge = false });
            inbound.DirectionsFromLanes();
            Program.Check(inbound.RatioForward < 0f && inbound.RatioBackward >= 0f && inbound.HasLanes, "lanes against the edge only: the other way round");
            var none = new TwEdge();
            none.DirectionsFromLanes();
            Program.Check(!none.HasLanes, "an edge with no car lane is no road to the junction signs");
            var again = new TwEdge { RatioForward = 0.3f, RatioBackward = 0.7f };
            again.Lanes.Add(new TwLane { WithEdge = true });
            again.Lanes.Add(new TwLane { WithEdge = false });
            again.DirectionsFromLanes();
            Program.Check(again.RatioForward == 1f && again.RatioBackward == 1f, "free flow where there are lanes: the junction signs do not use the speed");
        }

        private static void Finding()
        {
            JrResult r = Read(Cross());
            Program.Check(r.Found && r.Why == "", "a crossroads ahead is found");
            Program.Near(r.Distance, 300f, 0.01f, "300 m from the sign at the origin to the junction at 300: " + r.Distance);
            Program.Check(r.ApproachEdge == 1 && r.OwnName == "长江路" && r.OwnRoad == 1, "the road it comes by and its name");
            Program.Near(Read(Cross(), 120f).Distance, 180f, 0.01f, "a sign 120 m on is 180 m from it");
            Program.Near(Read(Cross(), 0f, 0f, 1f, 7f).Distance, 300f, 0.01f, "a sign standing beside the road (7 m east of its centre line) is as far along it");

            // Bends (nodes of two roads) are passed and their length counts.
            var bends = new Graph();
            bends.Add(1, 1, 2, "长江路", 1, 0, -100, 0, 100);
            bends.Add(2, 2, 3, "长江路", 1, 0, 100, 0, 250);
            bends.Add(3, 3, 4, "长江路", 1, 0, 250, 0, 420);
            bends.Add(4, 4, 5, "学府路", 4, 0, 420, 0, 800);
            bends.Add(5, 4, 6, "公滨路", 2, 0, 420, -300, 420);
            bends.Add(6, 4, 7, "黄河路", 3, 0, 420, 300, 420);
            JrResult overBends = JunctionRead.Read(bends, 1, 0f, 0f, 0f, 1f, JrOptions.Default);
            Program.Check(overBends.Found && Math.Abs(overBends.Distance - 420f) < 0.01f, "over two bends to the junction at 420 m: " + overBends.Distance);
            Program.Check(overBends.ApproachEdge == 3, "the last edge before the junction is the approach (the lanes are read from it): " + overBends.ApproachEdge);
            Program.Check(overBends.OwnName == "长江路", "its name");

            // The road ends first; the range runs out; a sign that is not on a road.
            var dead = new Graph();
            dead.Add(1, 1, 2, "死路", 1, 0, -100, 0, 200);
            JrResult none = JunctionRead.Read(dead, 1, 0f, 0f, 0f, 1f, JrOptions.Default);
            Program.Check(!none.Found && none.Why == "the road ends before a junction", "a dead end: " + none.Why);
            var far = new Graph();
            far.Add(1, 1, 2, "长路", 1, 0, -100, 0, 400);
            far.Add(2, 2, 3, "长路", 1, 0, 400, 0, 900);
            far.Add(3, 3, 4, "长路", 1, 0, 900, 0, 1300);
            far.Add(4, 4, 5, "甲路", 2, 0, 1300, -300, 1300);
            far.Add(5, 4, 6, "乙路", 3, 0, 1300, 300, 1300);
            JrResult tooFar = JunctionRead.Read(far, 1, 0f, 0f, 0f, 1f, JrOptions.Default);
            Program.Check(!tooFar.Found && tooFar.Why.StartsWith("no junction within"), "a junction 1300 m on is out of range: " + tooFar.Why);
            JrOptions wide = JrOptions.Default;
            wide.Range = 2000f;
            Program.Check(JunctionRead.Read(far, 1, 0f, 0f, 0f, 1f, wide).Found, "with a wider range it is found");
            Program.Check(!JunctionRead.Read(Cross(), 99, 0f, 0f, 0f, 1f, JrOptions.Default).Found && JunctionRead.Read(Cross(), 99, 0f, 0f, 0f, 1f, JrOptions.Default).Why == "the sign is not on a road", "an edge that is not a road");
            Program.Check(!JunctionRead.Read(null, 1, 0f, 0f, 0f, 1f, JrOptions.Default).Found && !JunctionRead.Read(Cross(), 1, 0f, 0f, 0f, 0f, JrOptions.Default).Found, "no graph, no direction");

            // The other way: traffic going south on the same road (the sign faces north), the junction is at the start of the edge.
            var both = Cross();
            both.Add(6, 1, 7, "南街", 6, 0, -400, 400, -400);
            both.Add(7, 1, 8, "北街", 7, 0, -400, -400, -400);
            both.Add(8, 1, 9, "南路", 8, 0, -400, 0, -800);
            JrResult south = Read(both, 0f, 0f, -1f);
            Program.Check(south.Found && Math.Abs(south.Distance - 400f) < 0.01f && south.ApproachEdge == 1, "going south it is the junction at the other end, 400 m: " + south.Distance);
            Program.Check(south.Has(JrDirection.Ahead) && south.Has(JrDirection.Left) && south.Has(JrDirection.Right), "a crossroads from the other side too");
            Program.Check(NamesOf(south, JrDirection.Ahead) == "南路" && NamesOf(south, JrDirection.Left) == "南街" && NamesOf(south, JrDirection.Right) == "北街", "and the sides are swapped: going south, east is on the left: " + NamesOf(south, JrDirection.Ahead) + " " + NamesOf(south, JrDirection.Left) + " " + NamesOf(south, JrDirection.Right));
        }

        private static string NamesOf(JrResult r, JrDirection d) => string.Join("/", JunctionFill.NamesOf(r, d));

        private static void Directions()
        {
            JrResult r = Read(Cross());
            Program.Check(r.Arms.Count == 3, "three roads leave the crossroads: " + r.Arms.Count);
            Program.Check(NamesOf(r, JrDirection.Ahead) == "学府路" && NamesOf(r, JrDirection.Left) == "公滨路" && NamesOf(r, JrDirection.Right) == "黄河路", "ahead, left (west), right (east) for traffic going north: " + NamesOf(r, JrDirection.Ahead) + " " + NamesOf(r, JrDirection.Left) + " " + NamesOf(r, JrDirection.Right));
            foreach (JrArm arm in r.Arms)
                Program.Check(arm.Direction == (arm.Edge == 2 ? JrDirection.Ahead : arm.Edge == 3 ? JrDirection.Left : JrDirection.Right) && Math.Abs(Math.Abs(arm.Angle) - (arm.Edge == 2 ? 0f : 90f)) < 0.5f, "arm " + arm.Edge + " is " + arm.Direction + " at " + arm.Angle + " degrees");
            Program.Check(r.Arms.Find(a => a.Edge == 3).Angle > 0f && r.Arms.Find(a => a.Edge == 4).Angle < 0f, "the left is positive, the right negative");

            // The forms.
            Program.Check(JunctionFill.FormOf(r) == "x", "crossroads");
            var t = new Graph();
            t.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            t.Add(3, 2, 4, "公滨路", 2, 0, 300, -400, 300);
            t.Add(4, 2, 5, "黄河路", 3, 0, 300, 400, 300);
            Program.Check(JunctionFill.FormOf(Read(t)) == "t", "T junction: left and right");
            var right = new Graph();
            right.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            right.Add(2, 2, 3, "学府路", 4, 0, 300, 0, 700);
            right.Add(4, 2, 5, "黄河路", 3, 0, 300, 400, 300);
            Program.Check(JunctionFill.FormOf(Read(right)) == "r", "ahead and right");
            var left = new Graph();
            left.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            left.Add(2, 2, 3, "学府路", 4, 0, 300, 0, 700);
            left.Add(3, 2, 4, "公滨路", 2, 0, 300, -400, 300);
            Program.Check(JunctionFill.FormOf(Read(left)) == "l", "ahead and left");

            // The sectors: 25 degrees off straight is still ahead, 45 is a side, an arm that turns back (170 degrees) is not shown.
            Program.Check(JunctionRead.DirectionOf(0f, JrOptions.Default) == JrDirection.Ahead && JunctionRead.DirectionOf(30f, JrOptions.Default) == JrDirection.Ahead && JunctionRead.DirectionOf(-30f, JrOptions.Default) == JrDirection.Ahead, "up to 30 degrees: ahead");
            Program.Check(JunctionRead.DirectionOf(31f, JrOptions.Default) == JrDirection.Left && JunctionRead.DirectionOf(149f, JrOptions.Default) == JrDirection.Left && JunctionRead.DirectionOf(-31f, JrOptions.Default) == JrDirection.Right && JunctionRead.DirectionOf(-149f, JrOptions.Default) == JrDirection.Right, "31 to 149 degrees: a side");
            Program.Check(JunctionRead.DirectionOf(150f, JrOptions.Default) == JrDirection.Back && JunctionRead.DirectionOf(-170f, JrOptions.Default) == JrDirection.Back && JunctionRead.DirectionOf(180f, JrOptions.Default) == JrDirection.Back, "150 degrees or more: back");

            var skew = new Graph();
            skew.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            skew.Add(2, 2, 3, "学府路", 4, 0, 300, 100, 700);          // a little to the right: ahead
            skew.Add(3, 2, 4, "公滨路", 2, 0, 300, -300, 400);          // up and to the left (about 18 degrees from the west: a side)
            skew.Add(4, 2, 5, "黄河路", 3, 0, 300, 300, 300);
            skew.Add(5, 2, 6, "回头路", 5, 0, 300, 60, 0);              // turns back (about 166 degrees)
            JrResult s = Read(skew);
            Program.Check(NamesOf(s, JrDirection.Ahead) == "学府路" && NamesOf(s, JrDirection.Left) == "公滨路" && NamesOf(s, JrDirection.Right) == "黄河路", "a skew junction: " + NamesOf(s, JrDirection.Ahead) + " " + NamesOf(s, JrDirection.Left) + " " + NamesOf(s, JrDirection.Right));
            Program.Check(s.Arms.Find(a => a.Edge == 5).Direction == JrDirection.Back, "a road that turns back is not a way on");

            // A one-way road that comes into the junction cannot be taken: not shown, and does not make a form.
            var oneWay = Cross();
            oneWay.Edges[3].RatioForward = -1f;   // the left road runs from the junction to the west... its only lanes are against the edge, i.e. into the junction
            oneWay.Edges[3].RatioBackward = 1f;
            JrResult o = Read(oneWay);
            Program.Check(!o.Has(JrDirection.Left) && o.Has(JrDirection.Right) && JunctionFill.FormOf(o) == "r", "a road that only comes in is not a way out: " + JunctionFill.FormOf(o));
            // The same edge drawn the other way (it ends at the junction) with lanes with the edge: it comes in, too.
            var ends = new Graph();
            ends.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            ends.Add(2, 2, 3, "学府路", 4, 0, 300, 0, 700);
            TwEdge inbound = ends.Add(3, 4, 2, "公滨路", 2, -400, 300, 0, 300);
            inbound.RatioForward = 1f;
            inbound.RatioBackward = -1f;
            Program.Check(!Read(ends).Has(JrDirection.Left), "an edge that ends at the junction with lanes only with it comes in");
            inbound.RatioBackward = 1f;
            inbound.RatioForward = -1f;
            Program.Check(Read(ends).Has(JrDirection.Left), "with lanes against it, it leaves");

            // More than one road in a direction: the one with more lanes first, two names at most, each once, the unnamed left out.
            var many = Cross();
            many.Add(9, 2, 9, "支路甲", 9, 0, 300, 200, 700);           // ahead-ish to the right: ahead (about 27 degrees) too
            many.Add(10, 2, 10, "", 10, 0, 300, 5, 800);                // unnamed, straight ahead
            many.Add(11, 2, 11, "学府路", 4, 0, 300, 0, 900);           // the same name again
            many.Edges[9].Lanes.Add(new TwLane { Lateral = 1f, WithEdge = true });
            many.Edges[9].Lanes.Add(new TwLane { Lateral = 4f, WithEdge = true });
            many.Edges[9].Lanes.Add(new TwLane { Lateral = 7f, WithEdge = true });
            JrResult m = Read(many);
            string ahead = NamesOf(m, JrDirection.Ahead);
            Program.Check(ahead == "支路甲/学府路", "the road with more lanes first, the same name once, the unnamed road left out, two at most: " + ahead);
            Program.Check(JunctionFill.NamesOf(m, JrDirection.Left).Count == 1, "one name to the left");

            // The same name on two roads of a direction is one name, and a third name does not fit (8.1.4 c: two at most).
            var same = Cross();
            same.Add(9, 2, 9, "学府路", 4, 0, 300, 5, 800);              // the same name again, nearly straight ahead
            same.Add(10, 2, 10, "乙路", 11, 0, 300, 100, 700);          // a second name, 20 degrees off
            same.Add(11, 2, 11, "丙路", 12, 0, 300, -150, 600);         // a third name, 26 degrees off the other way
            JrResult sm = Read(same);
            Program.Check(NamesOf(sm, JrDirection.Ahead) == "学府路/丙路" || NamesOf(sm, JrDirection.Ahead) == "学府路/乙路", "the same name once, then one more, not three: " + NamesOf(sm, JrDirection.Ahead));
            Program.Check(JunctionFill.NamesOf(sm, JrDirection.Ahead).Count == 2 && JunctionFill.NamesOf(sm, JrDirection.Ahead)[0] != JunctionFill.NamesOf(sm, JrDirection.Ahead)[1], "two names, different");

            // The cross road: left and right of one road (the same road, or the same name) is a cross road; two roads are not.
            Program.Check(JunctionFill.CrossName(Read(Cross())) == "", "two different roads on the sides are no cross road");
            Program.Check(JunctionFill.CrossName(Read(Cross("学府路", "学院路", "学院路", 7, 7))) == "学院路", "one road on both sides");
            Program.Check(JunctionFill.CrossName(Read(Cross("学府路", "学院路", "学院路", 7, 8))) == "学院路", "two edges with the same name");
            Program.Check(JunctionFill.CrossName(Read(Cross("学府路", "", "", 0, 0))) == "", "no names: no cross road");
            Program.Check(JunctionFill.CrossName(Read(t)) == "" && JunctionFill.CrossName(Read(right)) == "", "needs both sides");
        }

        private static List<TwLane> LaneSet(bool withEdge, params (float lateral, int moves)[] lanes)
        {
            var list = new List<TwLane>();
            foreach ((float lateral, int moves) in lanes)
                list.Add(new TwLane { Lateral = lateral, WithEdge = withEdge, Moves = moves });
            return list;
        }

        private static void Lanes()
        {
            // Going north (with the edge): lanes on the east side of its centre line (positive lateral); the driver's left is the smallest lateral.
            Graph g = Cross();
            g.Edges[1].Lanes.AddRange(LaneSet(true, (8.5f, JrMoves.Right), (1.5f, JrMoves.Left | JrMoves.UTurn), (5f, JrMoves.Ahead)));
            g.Edges[1].Lanes.AddRange(LaneSet(false, (-1.5f, JrMoves.Ahead), (-5f, JrMoves.Right)));
            JrResult r = Read(g);
            Program.Check(r.LaneMoves.Count == 3, "three lanes with the traffic: " + r.LaneMoves.Count);
            Program.Check(r.LaneMoves[0] == (JrMoves.Left | JrMoves.UTurn) && r.LaneMoves[1] == JrMoves.Ahead && r.LaneMoves[2] == JrMoves.Right, "from the driver's left: left, ahead, right: " + string.Join(",", r.LaneMoves));

            // Going south (against the edge): the lanes on the west side; the driver's left is the largest lateral, the one nearest the centre line.
            var both = Cross();
            both.Add(6, 1, 7, "南街", 6, 0, -400, 400, -400);
            both.Add(7, 1, 8, "北街", 7, 0, -400, -400, -400);
            both.Add(8, 1, 9, "南路", 8, 0, -400, 0, -800);
            both.Edges[1].Lanes.AddRange(LaneSet(true, (1.5f, JrMoves.Ahead)));
            both.Edges[1].Lanes.AddRange(LaneSet(false, (-8.5f, JrMoves.Right), (-1.5f, JrMoves.Left), (-5f, JrMoves.Ahead)));
            JrResult south = Read(both, 0f, 0f, -1f);
            Program.Check(south.LaneMoves.Count == 3 && south.LaneMoves[0] == JrMoves.Left && south.LaneMoves[1] == JrMoves.Ahead && south.LaneMoves[2] == JrMoves.Right, "going south: " + string.Join(",", south.LaneMoves));

            // No lanes read: an empty list (the fill makes one lane for each way out).
            Program.Check(Read(Cross()).LaneMoves.Count == 0, "no lanes read: none");
            // The arrows.
            foreach ((int moves, string arrow) in new[] { (JrMoves.Ahead, "s"), (JrMoves.Left, "l"), (JrMoves.Right, "r"), (JrMoves.Ahead | JrMoves.Left, "sl"), (JrMoves.Ahead | JrMoves.Right, "sr"), (JrMoves.Left | JrMoves.Right, "l"), (JrMoves.Ahead | JrMoves.Left | JrMoves.Right, "s"), (JrMoves.UTurn, "l"), (JrMoves.Left | JrMoves.UTurn, "l"), (JrMoves.Ahead | JrMoves.UTurn, "s"), (0, "l") })
                Program.Check(JunctionFill.ArrowOf(moves) == arrow, "moves " + moves + " is the arrow " + arrow + ": " + JunctionFill.ArrowOf(moves));
        }

        private static void FillDiagram()
        {
            string spec = Def("gb.guide8.01").Template.Default().ToString();
            string made = Fill("gb.guide8.01", spec, Read(Cross()), out JrNote note);
            Program.Check(made != null, "路1 is filled in");
            GuideSpec s = Parsed("gb.guide8.01", made);
            Program.Check(s.Get("form") == "x" && s.Get("to1") == "学府路" && s.Get("tl1") == "公滨路" && s.Get("tr1") == "黄河路" && s.Get("xroad") == "", "form, ahead, left, right: " + made);
            Program.Check(s.Get("to2") == "" && s.Get("tl2") == "" && s.Get("tr2") == "", "the second informations are empty");
            Program.Check(s.Get("dir") == "北", "the compass mark follows the way the traffic goes (north)");
            Program.Check(note.Zh.Contains("前方 300 m") && note.En.Contains("300 m ahead") && note.Zh.Contains("学府路") && note.En.Contains("公滨路"), "the note says what was read: " + note.Zh + " / " + note.En);

            // Going east, west, south: the mark; the player's empty mark is left alone.
            var east = new Graph();
            east.Add(1, 1, 2, "东西路", 1, -400, 0, 300, 0);
            east.Add(2, 2, 3, "甲路", 2, 300, 0, 700, 0);
            east.Add(3, 2, 4, "乙路", 3, 300, 0, 300, 300);
            east.Add(4, 2, 5, "丙路", 4, 300, 0, 300, -300);
            JrResult re = JunctionRead.Read(east, 1, 0f, 0f, 1f, 0f, JrOptions.Default);
            GuideSpec se = Parsed("gb.guide8.01", Fill("gb.guide8.01", spec, re, out _));
            Program.Check(se.Get("dir") == "东" && se.Get("form") == "x" && se.Get("tl1") == "乙路" && se.Get("tr1") == "丙路", "going east: the mark is 东, north of the road is on the left: " + se.Get("dir") + " " + se.Get("tl1") + " " + se.Get("tr1"));
            string noMark = Def("gb.guide8.01").Template.Parse(spec).With("dir", "").ToString();
            Program.Check(Parsed("gb.guide8.01", Fill("gb.guide8.01", noMark, Read(Cross()), out _)).Get("dir") == "", "an empty compass mark stays empty");

            // One crossing road: the hexagon form, the name on the bar, the ends empty, and a note.
            JrResult one = Read(Cross("学府路", "学院路", "学院路", 7, 7));
            string hexagon = Fill("gb.guide8.01", spec, one, out JrNote hexNote);
            GuideSpec h = Parsed("gb.guide8.01", hexagon);
            Program.Check(h.Get("xroad") == "学院路" && h.Get("tl1") == "" && h.Get("tr1") == "" && h.Get("to1") == "学府路" && h.Get("form") == "x", "the crossing road is on the bar, the ends are empty: " + hexagon);
            Program.Check(hexNote.ZhNotes.Count >= 1 && hexNote.ZhNotes[0].Contains("箭杆"), "and a note says why the ends are empty");

            // A T: no ahead; the old text of the ahead field is cleared.
            var t = new Graph();
            t.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            t.Add(3, 2, 4, "公滨路", 2, 0, 300, -400, 300);
            t.Add(4, 2, 5, "黄河路", 3, 0, 300, 400, 300);
            GuideSpec st = Parsed("gb.guide8.01", Fill("gb.guide8.01", spec, Read(t), out _));
            Program.Check(st.Get("form") == "t" && st.Get("to1") == "" && st.Get("tl1") == "公滨路" && st.Get("tr1") == "黄河路", "a T: no ahead name (the default 'G2' is cleared)");

            // A junction with one way on (only a left bend) is not drawable: nothing is filled in, and the spec is the player's.
            var bend = new Graph();
            bend.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            bend.Add(3, 2, 4, "公滨路", 2, 0, 300, -400, 300);
            bend.Add(5, 2, 6, "回路", 5, 0, 300, 40, 0);
            string nothing = Fill("gb.guide8.01", spec, Read(bend), out JrNote nn);
            Program.Check(nothing == null && nn.Zh.Length > 0 && nn.En.Length > 0, "one way on: nothing filled in, with a note");

            // A written spec that is not the template's own settings (garbage) is still filled in.
            Program.Check(Fill("gb.guide8.01", ";;==x", Read(Cross()), out _) != null, "a garbled spec is the defaults and is filled in");
        }

        private static void FillOthers()
        {
            // 路3: the three rows.
            string made = Fill("gb.guide8.03", Def("gb.guide8.03").Template.Default().ToString(), Read(Cross()), out JrNote note);
            GuideSpec s = Parsed("gb.guide8.03", made);
            Program.Check(s.Get("to1") == "学府路" && s.Get("tl1") == "公滨路" && s.Get("tr1") == "黄河路", "路3: the rows: " + made);
            var t = new Graph();
            t.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            t.Add(3, 2, 4, "公滨路", 2, 0, 300, -400, 300);
            t.Add(4, 2, 5, "黄河路", 3, 0, 300, 400, 300);
            GuideSpec st = Parsed("gb.guide8.03", Fill("gb.guide8.03", Def("gb.guide8.03").Template.Default().ToString(), Read(t), out _));
            Program.Check(st.Get("to1") == "" && st.Get("tl1") == "公滨路", "路3 at a T: no ahead row");

            // 路6: at the junction the road that crosses it; elsewhere the road itself.
            string name6 = Def("gb.guide8.06").Template.Default().ToString();
            GuideSpec atJunction = Parsed("gb.guide8.06", Fill("gb.guide8.06", name6, Read(Cross("学府路", "学院路", "学院路", 7, 7), 280f), out _));
            Program.Check(atJunction.Get("name") == "学院路", "at the junction (20 m before it) 路6 names the crossing road: " + atJunction.Get("name"));
            GuideSpec crossTwo = Parsed("gb.guide8.06", Fill("gb.guide8.06", name6, Read(Cross(), 280f), out _));
            Program.Check(crossTwo.Get("name") == "公滨路", "two different roads cross: the left one is named: " + crossTwo.Get("name"));
            GuideSpec away = Parsed("gb.guide8.06", Fill("gb.guide8.06", name6, Read(Cross(), 0f), out _));
            Program.Check(away.Get("name") == "长江路", "300 m before the junction 路6 names the road it stands on: " + away.Get("name"));
            var unnamed = Cross();
            unnamed.Edges[1].Name = "";
            Program.Check(Fill("gb.guide8.06", name6, Read(unnamed, 0f), out JrNote un) == null && un.Zh.Contains("没有名字"), "a road with no name: nothing filled in");

            // 路7: by the form the player chose.
            string name7 = Def("gb.guide8.07").Template.Default().ToString();
            foreach ((string form, string expect) in new[] { ("r", "黄河路"), ("l", "公滨路"), ("u", "学府路") })
                Program.Check(Parsed("gb.guide8.07", Fill("gb.guide8.07", Def("gb.guide8.07").Template.Parse(name7).With("form", form).ToString(), Read(Cross(), 280f), out _)).Get("name") == expect, "路7 form " + form + " names " + expect);
            Program.Check(Parsed("gb.guide8.07", Fill("gb.guide8.07", Def("gb.guide8.07").Template.Parse(name7).With("form", "b").ToString(), Read(Cross("学府路", "学院路", "学院路", 7, 7), 280f), out _)).Get("name") == "学院路", "路7 form b (both sides) names the cross road");
            Program.Check(Fill("gb.guide8.07", Def("gb.guide8.07").Template.Parse(name7).With("form", "u").ToString(), Read(t, 280f), out JrNote no) == null && no.Zh.Length > 0, "路7 ahead at a T: no road ahead, nothing filled in");
        }

        private static void FillLanes()
        {
            string spec = Def("gb.guide8.04").Template.Default().ToString();
            // Four lanes read: left, ahead, ahead, right.
            Graph g = Cross();
            g.Edges[1].Lanes.AddRange(LaneSet(true, (1.5f, JrMoves.Left), (5f, JrMoves.Ahead), (8.5f, JrMoves.Ahead), (12f, JrMoves.Right)));
            string made = Fill("gb.guide8.04", spec, Read(g), out JrNote note);
            GuideSpec s = Parsed("gb.guide8.04", made);
            Program.Check(s.Get("n") == "4" && s.Get("a1") == "l" && s.Get("a2") == "s" && s.Get("a3") == "s" && s.Get("a4") == "r", "four lanes and their arrows: " + made);
            Program.Check(s.Get("to1") == "公滨路" && s.Get("to2") == "学府路" && s.Get("to3") == "" && s.Get("to4") == "黄河路", "the name once over the two lanes that lead to the same place: " + s.Get("to1") + "/" + s.Get("to2") + "/" + s.Get("to3") + "/" + s.Get("to4"));
            Program.Check(s.Get("a5") == "s" && s.Get("to5") == "" && s.Get("to6") == "", "the lanes past the last are empty");
            Program.Check(note.Zh.Contains("4 条车道") && note.Zh.Contains("↰") && note.En.Contains("4 lane"), "the note shows the arrows: " + note.Zh);

            // The strip of arrows has no names.
            GuideSpec strip = Parsed("gb.guide8.04b", Fill("gb.guide8.04b", Def("gb.guide8.04b").Template.Default().ToString(), Read(g), out _));
            Program.Check(strip.Get("n") == "4" && strip.Get("a1") == "l" && strip.Get("a4") == "r" && strip.Get("to1") == "" && strip.Get("to2") == "" && strip.Get("to4") == "", "the strip: four arrows, no names");

            // Lanes with two turns.
            var combined = Cross();
            combined.Edges[1].Lanes.AddRange(LaneSet(true, (1.5f, JrMoves.Left | JrMoves.Ahead), (5f, JrMoves.Ahead | JrMoves.Right)));
            GuideSpec c = Parsed("gb.guide8.04", Fill("gb.guide8.04", spec, Read(combined), out _));
            Program.Check(c.Get("n") == "2" && c.Get("a1") == "sl" && c.Get("a2") == "sr" && c.Get("to1") == "学府路" && c.Get("to2") == "", "ahead-and-turn lanes are named for ahead, once: " + c.Get("to1") + "/" + c.Get("to2"));

            // Not read: one lane for each way out.
            string unknown = Fill("gb.guide8.04", spec, Read(Cross()), out JrNote nl);
            GuideSpec u = Parsed("gb.guide8.04", unknown);
            Program.Check(u.Get("n") == "3" && u.Get("a1") == "l" && u.Get("a2") == "s" && u.Get("a3") == "r" && u.Get("to1") == "公滨路" && u.Get("to2") == "学府路" && u.Get("to3") == "黄河路", "lanes not read: a lane for each way out, left, ahead, right: " + unknown);
            Program.Check(nl.ZhNotes.Exists(x => x.Contains("车道没有读到")) && nl.EnNotes.Exists(x => x.Contains("were not read")), "and a note says so");

            // More than six lanes: the longest run of the same arrow is shortened.
            var wide = Cross();
            wide.Edges[1].Lanes.AddRange(LaneSet(true, (1f, JrMoves.Left), (3f, JrMoves.Ahead), (5f, JrMoves.Ahead), (7f, JrMoves.Ahead), (9f, JrMoves.Ahead), (11f, JrMoves.Ahead), (13f, JrMoves.Right), (15f, JrMoves.Right)));
            JrResult rw = Read(wide);
            List<int> row = JunctionFill.LaneRow(rw, out bool known, out int shortened);
            Program.Check(known && row.Count == 6 && shortened == 2, "eight lanes are six: " + row.Count + ", shortened " + shortened);
            Program.Check(JunctionFill.ArrowOf(row[0]) == "l" && JunctionFill.ArrowOf(row[row.Count - 1]) == "r", "the turns at the ends are kept");
            int ahead = 0;
            foreach (int m in row)
                ahead += JunctionFill.ArrowOf(m) == "s" ? 1 : 0;
            Program.Check(ahead == 3, "the ahead lanes took the cut (five became three): " + ahead);
            GuideSpec w = Parsed("gb.guide8.04", Fill("gb.guide8.04", spec, rw, out JrNote nw));
            Program.Check(w.Get("n") == "6" && nw.ZhNotes.Exists(x => x.Contains("少画了 2 条")), "the sign has six and says it drew two fewer");
            var alternating = Cross();
            alternating.Edges[1].Lanes.AddRange(LaneSet(true, (1f, JrMoves.Left), (3f, JrMoves.Ahead), (5f, JrMoves.Right), (7f, JrMoves.Ahead | JrMoves.Left), (9f, JrMoves.Ahead | JrMoves.Right), (11f, JrMoves.Left), (13f, JrMoves.Right), (15f, JrMoves.Ahead)));
            List<int> alt = JunctionFill.LaneRow(Read(alternating), out _, out int cut);
            Program.Check(alt.Count == 6 && cut == 2, "lanes with no run to shorten are cut from the right: " + alt.Count + " " + cut);

            // Nothing: no lanes and no way out.
            var dead = new Graph();
            dead.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 300);
            dead.Add(3, 2, 4, "回路甲", 3, 0, 300, 40, 0);
            dead.Add(4, 2, 5, "回路乙", 4, 0, 300, -40, 0);
            Program.Check(Fill("gb.guide8.04", spec, Read(dead), out _) == null, "no lanes and no way on: nothing filled in");
        }

        private static void Notes()
        {
            string spec1 = Def("gb.guide8.01").Template.Default().ToString();
            // How far the sign stands from the junction is not remarked on (the owner, 4 Nov: "把这个和类似的删了"; the notes of GB 5768.2 8.3.2.4, 8.3.3 and 8.3.4 are gone): no note at 450, 300, 110, 50 or 0 m.
            var longRoad = new Graph();
            longRoad.Add(1, 1, 2, "长江路", 1, 0, -400, 0, 450);
            longRoad.Add(2, 2, 3, "学府路", 4, 0, 450, 0, 900);
            longRoad.Add(3, 2, 4, "公滨路", 2, 0, 450, -400, 450);
            longRoad.Add(4, 2, 5, "黄河路", 3, 0, 450, 400, 450);
            foreach (string id in new[] { "gb.guide8.01", "gb.guide8.03", "gb.guide8.06", "gb.guide8.07" })
            {
                string spec = Def(id).Template.Default().ToString();
                foreach ((string where, JrResult read) in new[] { ("450 m", JunctionRead.Read(longRoad, 1, 0f, 0f, 0f, 1f, JrOptions.Default)), ("300 m", Read(Cross(), 0f)), ("110 m", Read(Cross(), 190f)), ("50 m", Read(Cross(), 250f)), ("at it", Read(Cross(), 300f)) })
                {
                    Fill(id, spec, read, out JrNote n);
                    Program.Check(n.ZhNotes.Count == 0 && n.EnNotes.Count == 0, id + " " + where + " from the junction: no note about where it stands: " + string.Join("|", n.ZhNotes));
                }
            }

            // A name over seven characters is cut and says so.
            var longName = Cross("学府大道西延伸段北路", "公滨路", "黄河路");
            string cut = Fill("gb.guide8.01", spec1, Read(longName, 100f), out JrNote cutNote);
            Program.Check(Parsed("gb.guide8.01", cut).Get("to1") == "学府大道西延伸" && cutNote.ZhNotes.Exists(x => x.Contains("超过 7 个字")) && cutNote.EnNotes.Exists(x => x.Contains("longer than 7")), "a ten-character name is cut to seven, with a note: " + Parsed("gb.guide8.01", cut).Get("to1"));

            // A sign that is not one of these, nothing read, nothing at all.
            Program.Check(JunctionFill.Apply(Def("gb.prohib.09"), "", Read(Cross()), out JrNote not) == null && not.Zh.Length > 0, "a sign that is not read gets a note");
            Program.Check(JunctionFill.Apply(null, "", Read(Cross()), out _) == null, "no sign");
            Program.Check(Fill("gb.guide8.01", spec1, null, out JrNote nothing) == null && nothing.Zh.Length > 0, "no reading: a note");
            Program.Check(!JunctionFill.CanRead("gb.guide8.02") && JunctionFill.CanRead("gb.guide8.04b") && JunctionFill.CanRead("gb.guide8.07"), "which signs can be read");
            foreach (string id in JunctionFill.Ids)
                Program.Check(SignCatalog.ChoiceById(id) != null, id + " is in the catalogue");

            // The reasons, in both languages.
            foreach (string why in new[] { "the sign is not on a road", "the road ends before a junction", "no junction within 600 m", "no junction within reach", "no road or no direction", "reading failed", "something else" })
                Program.Check(JunctionFill.WhyText(why, true).Length > 0 && JunctionFill.WhyText(why, false).Length > 0 && JunctionFill.WhyText(why, true) != JunctionFill.WhyText(why, false), "the reason '" + why + "' is said in both languages");
            Program.Check(JunctionFill.WhyText("no junction within 600 m", true) == "600 m 内没有路口", "with the distance");
        }
    }
}
