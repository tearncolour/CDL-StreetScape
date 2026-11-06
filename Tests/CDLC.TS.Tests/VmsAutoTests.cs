using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The automatic content of the text screens (0.8.0): the facts from the road ahead, the message, and how a message replaces the lines of a screen that follows the game.</summary>
    internal static class VmsAutoTests
    {
        public static void RunAll()
        {
            Program.Run("auto screen: the walk gives every stretch its distance, length and accident", Walk);
            Program.Run("auto screen: the facts are those of the sign's own road ahead", Facts);
            Program.Run("auto screen: the words of a distance", Distances);
            Program.Run("auto screen: the message by priority, with lines a screen can show", Messages);
            Program.Run("auto screen: the lines of a screen that follows the game, and the ones that do not", Decorating);
            Program.Run("auto screen: the editor asks for the lines only when the screen is manual", Editor);
            Program.Run("auto screen: the live messages", Live);
        }

        // ------------------------------------------------------------------ a straight road with a cross road, by hand

        private sealed class Graph : ITrafficGraph
        {
            public readonly Dictionary<int, TwEdge> Edges = new Dictionary<int, TwEdge>();
            public readonly Dictionary<int, List<int>> Nodes = new Dictionary<int, List<int>>();

            public TwEdge Add(int id, int from, int to, float x0, float z0, float x1, float z1, int road, string name, float ratio = 1f, byte accident = 0)
            {
                var edge = new TwEdge { Id = id, StartNode = from, EndNode = to, Points = new[] { x0, z0, x1, z1 }, RatioForward = ratio, RatioBackward = ratio, Road = road, Name = name, Accident = accident };
                Edges[id] = edge;
                foreach (int node in new[] { from, to })
                {
                    if (!Nodes.TryGetValue(node, out List<int> list))
                        Nodes[node] = list = new List<int>();
                    list.Add(id);
                }

                return edge;
            }

            public bool TryEdge(int id, out TwEdge edge) => Edges.TryGetValue(id, out edge);

            public IReadOnlyList<int> EdgesAt(int node) => Nodes.TryGetValue(node, out List<int> list) ? list : new List<int>();
        }

        /// <summary>A road along +Z of 300 m stretches (nodes 1 to 5), a cross road at node 3 (edges 10 and 11), the sign at the start of the first.</summary>
        private static Graph Road(byte accidentOn3 = 0, float ratio2 = 1f, float ratio3 = 1f, float ratioCross = 1f)
        {
            var g = new Graph();
            g.Add(1, 1, 2, 0, 0, 0, 300, 100, "中山路");
            g.Add(2, 2, 3, 0, 300, 0, 600, 100, "中山路", ratio2);
            g.Add(3, 3, 4, 0, 600, 0, 900, 100, "中山路", ratio3, accidentOn3);
            g.Add(4, 4, 5, 0, 900, 0, 1200, 100, "中山路");
            g.Add(10, 3, 6, 0, 600, 300, 600, 200, "长江路", ratioCross, TmAccident.Active);
            g.Add(11, 3, 7, 0, 600, -300, 600, 200, "长江路", ratioCross);
            return g;
        }

        private static TmNetwork Walk(Graph g, TwOptions? options = null) =>
            TrafficWalk.Walk(g, 1, 0f, 0f, 0f, 1f, options ?? new TwOptions { Junctions = 3, Range = 2000f, MaxEdges = 60, Stub = 200f, Behind = 20f },
                (key, ratio) => TrafficMap.LevelOf(ratio, 0.7f, 0.4f));

        private static void Walk()
        {
            TmNetwork net = Walk(Road(TmAccident.Secured, 0.2f));
            Program.Check(net.Known && net.Edges.Count >= 5, "the walk made the stretches: " + net.Edges.Count);
            TmEdge e1 = net.Edges.Find(e => e.Road == 100 && e.Distance < 1f), e2 = net.Edges.Find(e => e.Road == 100 && Math.Abs(e.Distance - 300f) < 1f), e3 = net.Edges.Find(e => e.Road == 100 && Math.Abs(e.Distance - 600f) < 1f);
            Program.Check(e1 != null && Math.Abs(e1.Length - 300f) < 1f, "the sign's own stretch starts at 0 and is 300 m long");
            Program.Check(e2 != null && e2.Level == TrafficLevel.Jam && Math.Abs(e2.Length - 300f) < 1f, "the next starts at 300 m and is a jam (ratio 0.2)");
            Program.Check(e3 != null && e3.Accident == TmAccident.Secured, "the third starts at 600 m and has the secured accident");
            TmEdge cross = net.Edges.Find(e => e.Road == 200 && e.Accident == TmAccident.Active);
            Program.Check(cross != null && Math.Abs(cross.Distance - 600f) < 1f, "a cross road's stretch starts at the junction, 600 m on, with its own accident");
            Program.Check(net.Edges.Find(e => e.Road == 100 && e.Accident == TmAccident.None && e.Distance < 1f) != null, "an edge with no accident has none");
        }

        private static void Facts()
        {
            VmsFacts none = VmsAuto.FactsOf(Walk(Road()), VmsAuto.DefaultRange);
            Program.Check(!none.Any, "nothing ahead on a free road: but the cross road has an accident, and it is not ahead");
            Program.Check(none.AccidentAt < 0f, "an accident on the cross road is not an accident on this road");

            VmsFacts accident = VmsAuto.FactsOf(Walk(Road(TmAccident.Active)), VmsAuto.DefaultRange);
            Program.Check(Math.Abs(accident.AccidentAt - 600f) < 1f && !accident.AccidentSecured, "an accident on the third stretch is 600 m ahead: " + accident.AccidentAt);
            Program.Check(VmsAuto.FactsOf(Walk(Road(TmAccident.Secured)), VmsAuto.DefaultRange).AccidentSecured, "and is secured when the police are there");
            Program.Check(VmsAuto.FactsOf(Walk(Road(TmAccident.Active)), 500f).AccidentAt < 0f, "an accident farther than the range is not ahead");

            // Two accidents ahead: it is the nearer one that is said (and its police), not the farther.
            Graph two = Road(TmAccident.Secured);
            two.Edges[2].Accident = TmAccident.Active;
            VmsFacts nearer = VmsAuto.FactsOf(Walk(two), VmsAuto.DefaultRange);
            Program.Check(Math.Abs(nearer.AccidentAt - 300f) < 1f && !nearer.AccidentSecured, "of two accidents the nearer one is the fact: " + nearer.AccidentAt + " secured " + nearer.AccidentSecured);
            Graph twoJams = Road(0, 0.2f, 0.2f);
            twoJams.Edges[4].RatioForward = 0.1f;
            twoJams.Edges[4].RatioBackward = 0.1f;
            Program.Check(Math.Abs(VmsAuto.FactsOf(Walk(twoJams), VmsAuto.DefaultRange).JamAt - 300f) < 1f, "of several jams the nearest one starts it");

            VmsFacts jam = VmsAuto.FactsOf(Walk(Road(0, 0.2f, 0.2f)), VmsAuto.DefaultRange);
            Program.Check(Math.Abs(jam.JamAt - 300f) < 1f && Math.Abs(jam.JamLength - 600f) < 2f, "two jammed stretches in a row are one jam of 600 m, from 300 m: " + jam.JamAt + " / " + jam.JamLength);

            VmsFacts apart = VmsAuto.FactsOf(Walk(Road(0, 0.2f, 1f)), VmsAuto.DefaultRange);
            Program.Check(Math.Abs(apart.JamLength - 300f) < 2f, "a jam of one stretch is 300 m: " + apart.JamLength);

            VmsFacts slow = VmsAuto.FactsOf(Walk(Road(0, 0.5f)), VmsAuto.DefaultRange);
            Program.Check(Math.Abs(slow.SlowAt - 300f) < 1f && slow.JamAt < 0f, "a stretch at half speed is slow, not a jam");

            // A jam on the cross road is not on this road.
            VmsFacts cross = VmsAuto.FactsOf(Walk(Road(0, 1f, 1f, 0.1f)), VmsAuto.DefaultRange);
            Program.Check(cross.JamAt < 0f && cross.SlowAt < 0f, "a jam on the cross road is not ahead on this road");

            // No road id: the name decides.
            Graph g = Road(0, 0.2f);
            foreach (TwEdge e in g.Edges.Values)
            {
                if (e.Road == 100)
                    e.Road = 0;
            }

            Program.Check(VmsAuto.FactsOf(Walk(g), VmsAuto.DefaultRange).JamAt > 0f, "with no road id the same name is the same road");
            foreach (TwEdge e in g.Edges.Values)
                e.Name = e.Road == 200 ? "长江路" : "";
            Program.Check(VmsAuto.FactsOf(Walk(g), VmsAuto.DefaultRange).JamAt < 0f, "and with neither id nor name only the sign's own stretch is on its road");

            // Nothing read.
            Program.Check(!VmsAuto.FactsOf(null, 100f).Any && !VmsAuto.FactsOf(new TmNetwork(), 100f).Any, "no network, no facts");
            Program.Check(VmsFacts.None.AccidentAt < 0f && VmsFacts.None.JamAt < 0f && VmsFacts.None.SlowAt < 0f, "none is none");
        }

        private static void Distances()
        {
            foreach ((float metres, string text) in new[] { (0f, "50米"), (10f, "50米"), (74f, "50米"), (76f, "100米"), (149f, "150米"), (199f, "200米"), (230f, "200米"), (260f, "300米"), (449f, "400米"), (451f, "500米"), (940f, "900米"), (960f, "1.0公里"), (999f, "1.0公里"), (1000f, "1.0公里"), (1240f, "1.2公里"), (1250f, "1.3公里"), (3949f, "3.9公里") })
                Program.Check(VmsAuto.Distance(metres) == text, metres + " m is said as " + text + ": " + VmsAuto.Distance(metres));
            float last = 0f;
            for (float d = 0f; d < 4000f; d += 7f)
            {
                string said = VmsAuto.Distance(d);
                float value = said.EndsWith("公里", StringComparison.Ordinal) ? float.Parse(said.Substring(0, said.Length - 2), System.Globalization.CultureInfo.InvariantCulture) * 1000f : float.Parse(said.Substring(0, said.Length - 1), System.Globalization.CultureInfo.InvariantCulture);
                Program.Check(value >= last && Math.Abs(value - Math.Max(d, 50f)) <= 60f, "the distance never goes down as the real one goes up, and is near it: " + d + " -> " + said);
                last = value;
            }
        }

        private static void Messages()
        {
            Program.Check(!VmsAuto.Decide(VmsFacts.None).On && VmsAuto.Decide(VmsFacts.None).Lines.Length == 0 && VmsAuto.Decide(VmsFacts.None) == VmsMessage.Off, "nothing ahead: the screen is off (4.11.1)");

            var accident = new VmsFacts { AccidentAt = 600f, JamAt = 300f, JamLength = 900f, SlowAt = 100f };
            VmsMessage m = VmsAuto.Decide(accident);
            Program.Check(m.On && m.Kind == "accident" && m.Class == "warn" && m.Lines[0] == "前方600米事故", "an accident beats a jam and slow traffic: " + string.Join("/", m.Lines));
            VmsMessage secured = VmsAuto.Decide(new VmsFacts { AccidentAt = 600f, AccidentSecured = true, JamAt = -1f, SlowAt = -1f });
            Program.Check(secured.Kind == "accident-secured" && secured.Lines.Length == 3 && secured.Lines[1] == "警方已到场", "an accident with the police there says so");

            VmsMessage jam = VmsAuto.Decide(new VmsFacts { AccidentAt = -1f, JamAt = 300f, JamLength = 900f, SlowAt = 100f });
            Program.Check(jam.Kind == "jam" && jam.Class == "warn" && jam.Lines[0] == "前方300米拥堵" && jam.Lines[1].StartsWith("拥堵约", StringComparison.Ordinal), "a long jam says how long: " + string.Join("/", jam.Lines));
            VmsMessage shortJam = VmsAuto.Decide(new VmsFacts { AccidentAt = -1f, JamAt = 300f, JamLength = 100f, SlowAt = -1f });
            Program.Check(shortJam.Lines.Length == 2 && shortJam.Lines[1] == "请减速慢行", "a short jam does not");
            VmsMessage slow = VmsAuto.Decide(new VmsFacts { AccidentAt = -1f, JamAt = -1f, SlowAt = 450f });
            Program.Check(slow.Kind == "slow" && slow.Class == "info" && slow.Lines[0] == "前方500米车多", "slow traffic is general information (green): " + string.Join("/", slow.Lines));

            // Every message every screen can get has lines a text screen takes: at most four, none longer than a line of the field.
            var rng = new Random(3);
            int checkedMessages = 0;
            for (int i = 0; i < 3000; i++)
            {
                var facts = new VmsFacts
                {
                    AccidentAt = rng.Next(3) == 0 ? -1f : (float)(rng.NextDouble() * 4000),
                    AccidentSecured = rng.Next(2) == 0,
                    JamAt = rng.Next(3) == 0 ? -1f : (float)(rng.NextDouble() * 4000),
                    JamLength = (float)(rng.NextDouble() * 3000),
                    SlowAt = rng.Next(3) == 0 ? -1f : (float)(rng.NextDouble() * 4000)
                };
                VmsMessage message = VmsAuto.Decide(facts);
                Program.Check(message.Lines.Length <= 4 && message.On == (message.Lines.Length > 0) && (message.Class == "warn" || message.Class == "info"), "a message has at most four lines and a known class");
                foreach (string line in message.Lines)
                    Program.Check(line.Length <= VmsAuto.MaxLine && line.Length > 0, "the line '" + line + "' fits the screen (" + line.Length + " characters)");
                checkedMessages++;
            }

            Program.Check(checkedMessages == 3000, "all checked");
            Program.Check(m.SameAs(VmsAuto.Decide(accident)) && !m.SameAs(jam) && !m.SameAs(null) && !VmsMessage.Off.SameAs(m), "the same facts give the same message, others do not");
        }

        private static void Decorating()
        {
            SignDef def = SignCatalog.ById(VmsAuto.TextTemplateId);
            GuideSpec manual = def.Template.Default();
            Program.Check(manual.Get("src") == "manual", "a text screen is manual by default");
            Program.Check(def.Template.Parse("l1=甲;l2=乙;l3=;l4=;cls=info;h=40").Get("src") == "manual", "a screen saved before this field existed is manual");
            Program.Check(!VmsAuto.IsAutoScreen(def.Hash, manual.ToString()), "and is not followed");

            string auto = manual.With("src", "auto").With("l1", "我的话").ToString();
            Program.Check(VmsAuto.IsAutoScreen(def.Hash, auto) && !VmsAuto.IsAutoScreen(SignCatalog.ById("gb.prohib.09").Hash, auto), "an automatic text screen is one; another kind of sign is not");

            // A manual screen is drawn as it is, whatever the game says.
            Program.Check(VmsAuto.Decorate(def.Hash, manual.ToString(), new VmsMessage { On = true, Class = "warn", Lines = new[] { "x" } }) == manual.ToString(), "a manual screen is not changed");

            // An automatic one takes the message, and goes dark with none.
            VmsMessage message = VmsAuto.Decide(new VmsFacts { AccidentAt = 600f, AccidentSecured = false, JamAt = -1f, SlowAt = -1f });
            GuideSpec shown = def.Template.Parse(VmsAuto.Decorate(def.Hash, auto, message));
            Program.Check(shown.Get("l1") == "前方600米事故" && shown.Get("l2") == "请减速慢行" && shown.Get("l3") == "" && shown.Get("l4") == "" && shown.Get("cls") == "warn" && shown.Get("src") == "auto", "the lines and the colour are the message's: " + shown.ToString());
            GuideSpec dark = def.Template.Parse(VmsAuto.Decorate(def.Hash, auto, VmsMessage.Off));
            Program.Check(dark.Get("l1") == "" && dark.Get("l2") == "", "off: the lines are empty (the player's own are not shown)");
            GuideSpec unread = def.Template.Parse(VmsAuto.Decorate(def.Hash, auto, null));
            Program.Check(unread.Get("l1") == "", "not read yet: dark too");
            Program.Check(def.Template.Parse(auto).Get("l1") == "我的话", "the item's own settings are not changed by drawing it");

            // The drawn screen is a screen of the message's text, and an empty one is the empty screen (off).
            SignInstance lit = SignInstances.Of(def, VmsAuto.Decorate(def.Hash, auto, message)), off = SignInstances.Of(def, VmsAuto.Decorate(def.Hash, auto, VmsMessage.Off));
            Program.Check(lit.IsLed && off.IsLed && lit.Def.Width > 0f && off.Def.Width > 0f, "both are screens");
            Program.Check(lit.Key != off.Key && lit.Board.Main.Elements.Count > off.Board.Main.Elements.Count, "the lit one has text and the dark one none");

            // Every message gives a board that is a screen of a sensible size.
            foreach (VmsMessage each in new[] { message, VmsAuto.Decide(new VmsFacts { AccidentAt = 1500f, AccidentSecured = true, JamAt = -1f, SlowAt = -1f }), VmsAuto.Decide(new VmsFacts { AccidentAt = -1f, JamAt = 3200f, JamLength = 1000f, SlowAt = -1f }), VmsAuto.Decide(new VmsFacts { AccidentAt = -1f, JamAt = -1f, SlowAt = 90f }) })
            {
                SignInstance one = SignInstances.Of(def, VmsAuto.Decorate(def.Hash, auto, each));
                Program.Check(one.Def.Width > 1f && one.Def.Width < 12f && one.Def.Height > 0.5f && one.Def.Height < 6f, each.Kind + ": the screen is " + one.Def.Width + " x " + one.Def.Height + " m");
            }
        }

        private static void Editor()
        {
            SignDef def = SignCatalog.ById(VmsAuto.TextTemplateId);
            GuideSpec manual = def.Template.Default(), auto = manual.With("src", "auto");
            foreach (string key in new[] { "l1", "l2", "l3", "l4", "cls" })
            {
                GuideField field = def.Template.FieldOf(key);
                Program.Check(field.Visible != null && field.Visible(manual) && !field.Visible(auto), key + " is asked for when the screen is manual and not when it is automatic");
            }

            foreach (string key in new[] { "src", "h", "speed" })
            {
                GuideField field = def.Template.FieldOf(key);
                Program.Check(field.Visible == null || (field.Visible(manual) && field.Visible(auto)), key + " is asked for either way");
            }

            Program.Check(def.Template.FieldOf("src").Choices.Length == 2 && def.Template.FieldOf("src").Default == "manual", "two sources, manual the default");
            Program.Check(def.Template.Fields[def.Template.Fields.Length - 1].Key == "speed" && Array.FindIndex(def.Template.Fields, f => f.Key == "src") > Array.FindIndex(def.Template.Fields, f => f.Key == "h"), "the new field comes after the old ones (a saved spec reads the same)");
        }

        private static void Live()
        {
            VmsAuto.Clear();
            int changes = VmsAuto.Changes;
            Program.Check(VmsAuto.Get(77) == null, "nothing is known of a screen that was not read");
            VmsMessage a = VmsAuto.Decide(new VmsFacts { AccidentAt = 600f, JamAt = -1f, SlowAt = -1f });
            Program.Check(VmsAuto.Set(77, a) && VmsAuto.Changes == changes + 1 && ReferenceEquals(VmsAuto.Get(77), a), "a new message is a change");
            Program.Check(!VmsAuto.Set(77, VmsAuto.Decide(new VmsFacts { AccidentAt = 600f, JamAt = -1f, SlowAt = -1f })) && VmsAuto.Changes == changes + 1, "the same message again is not");
            Program.Check(VmsAuto.Set(77, VmsMessage.Off) && VmsAuto.Changes == changes + 2 && !VmsAuto.Get(77).On, "another message is, and off is a message");
            Program.Check(VmsAuto.Set(78, a) && VmsAuto.Changes == changes + 3, "another screen");
            VmsAuto.Remove(77);
            Program.Check(VmsAuto.Get(77) == null && VmsAuto.Get(78) != null, "removed");
            VmsAuto.Clear();
            Program.Check(VmsAuto.Get(78) == null && VmsAuto.Changes == changes + 4, "clear says so once");
            VmsAuto.Clear();
            Program.Check(VmsAuto.Changes == changes + 4, "and clearing nothing is no change");
        }
    }
}
