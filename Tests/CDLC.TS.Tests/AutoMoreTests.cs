using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The automatic marking's second part (0.12.2): transitions and change lines where the lanes change (7.1, 5.7), no passing where the sight is short (6.1.3, 6.1.4, Table A.1), bus and cycle lanes (6.9.1,
    /// 5.4.5, 5.15.2), crosswalks between junctions (5.8.3 to 5.8.6), the junction guide lines (5.6) and the word on the road (5.14.1).
    /// </summary>
    internal static class AutoMoreTests
    {
        public static void RunAll()
        {
            Program.Run("auto marking: a lane ends (7.1 narrowing, 5.7 change line)", LaneEnds);
            Program.Run("auto marking: a lane begins, and a turn lane before a junction", LaneBegins);
            Program.Run("auto marking: the lanes of the other way change", OtherSide);
            Program.Run("sight distance: Table A.1", SightTable);
            Program.Run("sight distance: the same as crossing every line with every place", SightSame);
            Program.Run("auto marking: no passing on a bend and over a crest", NoPassing);
            Program.Run("auto marking: bus lanes", BusLanes);
            Program.Run("auto marking: cycle lanes", BikeLanes);
            Program.Run("auto marking: a crosswalk between junctions", MidCrossing);
            Program.Run("junction guide lines (5.6)", GuideLines);
            Program.Run("the word on the road (5.14.1)", Word);
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static ChainPiece Piece(Bezier3 curve, float[] forward, float[] backward, float kmh = 60f, ChainJoint after = ChainJoint.End)
        {
            var piece = new ChainPiece { Curve = curve, Kmh = kmh, HasSpeed = true, After = after };
            piece.Forward.AddRange(forward);
            piece.Backward.AddRange(backward);
            return piece;
        }

        private static ChainPiece Straight(float z0, float z1, float[] forward, float[] backward, float kmh = 60f, ChainJoint after = ChainJoint.End) =>
            Piece(Bezier3.Line(P(0f, z0), P(0f, z1)), forward, backward, kmh, after);

        private static readonly float[] Three = { 1.75f, 5.25f, 8.75f }, Two = { 1.75f, 5.25f }, TwoBack = { -1.75f, -5.25f }, ThreeBack = { -1.75f, -5.25f, -8.75f }, One = { 1.75f }, OneBack = { -1.75f };

        private static AutoOptions Opt(bool cross = false)
        {
            AutoOptions o = AutoOptions.Default;
            o.CrossLines = cross;
            o.Edges = EdgeLines.All;
            return o;
        }

        private static List<AutoRun> Runs(AutoPlan plan, string id) => plan.Runs.FindAll(r => r.Id == id);

        private static void LaneEnds()
        {
            // Three lanes with the chain to two at a node at 200 m, 60 km/h.
            var chain = new RoadChain(new[] { Straight(0f, 200f, Three, TwoBack, 60f, ChainJoint.Pass), Straight(200f, 400f, Two, TwoBack, 60f) });
            AutoPlan plan = AutoMarking.Plan(chain, null, Opt());
            float length = MarkingTransition.Length(60f, 3.5f, true);
            Program.Near(length, 60f * 60f * 3.5f / 155f, 1e-3f, "formula 1 at 60 km/h and 3.5 m");
            AutoRun edge = Runs(plan, "mk.line.edge").Find(r => r.Lateral > 0f);
            Program.Check(edge != null && edge.Shifts && Math.Abs(edge.S0) < 1e-3f && Math.Abs(edge.S1 - 400f) < 1e-3f, "the right edge line is one line over the node, moving: " + edge);
            Program.Near(edge.ShiftS1, 200f, 1e-3f, "the move ends at the node (the road is narrower after it)");
            Program.Near(edge.ShiftS0, 200f - length, 1e-3f, "and starts the transition's length before");
            Program.Near(edge.LateralAt(100f), 10.5f, 1e-3f, "at 10.5 m before it");
            Program.Near(edge.LateralAt(300f), 7f, 1e-3f, "at 7 m after it");
            float mid = edge.LateralAt(200f - length * 0.5f);
            Program.Check(mid < 10.5f && mid > 7f, "in between in the middle: " + mid);
            Program.Near(edge.LateralAt(edge.ShiftS0 + 0.01f), 10.5f, 1e-3f, "no jump where it starts (the S has no slope there)");
            List<AutoRun> change = Runs(plan, "mk.line.change");
            Program.Check(change.Count == 1 && Math.Abs(change[0].Lateral - 7f) < 1e-3f, "the line of the lane that ends is the change line (7.1.3)");
            Program.Near(change[0].S1, 200f, 1e-3f, "to the end of the transition");
            Program.Near(change[0].S0, 200f - length - AutoMarking.ChangeLead, 1e-3f, "from the change lead before it");
            AutoRun before = Runs(plan, "mk.line.lane").Find(r => Math.Abs(r.Lateral - 7f) < 1e-3f);
            Program.Check(before != null && Math.Abs(before.S1 - change[0].S0) < 1e-3f && Math.Abs(before.S0) < 1e-3f, "and the lane line before it is the ordinary one");
            AutoRun through = Runs(plan, "mk.line.lane").Find(r => Math.Abs(r.Lateral - 3.5f) < 1e-3f);
            Program.Check(through != null && through.S1 > 399f && !through.Shifts, "the lane line between the lanes that go on goes on");
            AutoOptions off = Opt();
            off.Transitions = false;
            Program.Check(!AutoMarking.Plan(chain, null, off).Runs.Exists(r => r.Shifts || r.Id == "mk.line.change"), "without transitions: none");
        }

        private static void LaneBegins()
        {
            // Two lanes to three at 200 m: the edge line moves out after the node over 25 m; the new lane's line starts where it ends.
            var chain = new RoadChain(new[] { Straight(0f, 200f, Two, TwoBack, 60f, ChainJoint.Pass), Straight(200f, 400f, Three, TwoBack, 60f) });
            AutoPlan plan = AutoMarking.Plan(chain, null, Opt());
            AutoRun edge = Runs(plan, "mk.line.edge").Find(r => r.Lateral > 0f);
            Program.Check(edge != null && edge.Shifts, "the edge line moves");
            Program.Near(edge.ShiftS0, 200f, 1e-3f, "from the node (the road is wider after it)");
            Program.Near(edge.ShiftS1, 200f + MarkingTransition.WideningDefault, 1e-3f, "over 25 m (7.1.2: 20 to 30)");
            Program.Near(edge.LateralAt(390f), 10.5f, 1e-3f, "out to the new edge");
            AutoRun added = Runs(plan, "mk.line.lane").Find(r => Math.Abs(r.Lateral - 7f) < 1e-3f);
            Program.Check(added != null && Math.Abs(added.S0 - 225f) < 1e-3f && Math.Abs(added.AddedAt - 200f) < 1e-3f, "the new lane's line starts where the widening ends: " + added);

            // The same 60 m before a signalled junction: a turn lane, its line the change line from the node to the guide lane line (5.7.3).
            var shortEnd = Straight(200f, 260f, Three, TwoBack, 60f);
            var junctionChain = new RoadChain(new[] { Straight(0f, 200f, Two, TwoBack, 60f, ChainJoint.Pass), shortEnd });
            var approach = new ChainApproach { StopS = 260f, WithChain = true, Control = JunctionControl.Signals };
            approach.Lanes.Add(new ApproachLane { Lateral = 1.75f, Left = true });
            approach.Lanes.Add(new ApproachLane { Lateral = 5.25f, Straight = true });
            approach.Lanes.Add(new ApproachLane { Lateral = 8.75f, Straight = true, Right = true });
            AutoPlan withJunction = AutoMarking.Plan(junctionChain, new[] { approach }, Opt(true));
            AutoRun turn = Runs(withJunction, "mk.line.change").Find(r => Math.Abs(r.Lateral - 7f) < 1e-3f);
            AutoRun guide = Runs(withJunction, "mk.line.guidelane").Find(r => Math.Abs(r.Lateral - 7f) < 1e-3f);
            Program.Check(turn != null && guide != null, "a change line and a guide lane line on the new lane's line");
            Program.Near(turn.S0, 200f, 1e-3f, "the change line from where the lane begins");
            Program.Near(turn.S1, guide.S0, 1e-3f, "to the guide lane line");
        }

        private static void OtherSide()
        {
            // The other way has three lanes before the node and two after: it runs from 400 to 0, so it gets wider at the node; the move is on its far side of the node (s below 200).
            var chain = new RoadChain(new[] { Straight(0f, 200f, Two, ThreeBack, 60f, ChainJoint.Pass), Straight(200f, 400f, Two, TwoBack, 60f) });
            AutoPlan plan = AutoMarking.Plan(chain, null, Opt());
            AutoRun edge = Runs(plan, "mk.line.edge").Find(r => r.Lateral < 0f);
            Program.Check(edge != null && edge.Shifts, "the left edge moves");
            Program.Near(edge.ShiftS1, 200f, 1e-3f, "the move ends at the node");
            Program.Near(edge.ShiftS0, 175f, 1e-3f, "25 m of widening before it in the chain's distance: after the node in that traffic's way");
            AutoRun added = Runs(plan, "mk.line.lane").Find(r => Math.Abs(r.Lateral + 7f) < 1e-3f);
            Program.Check(added != null && Math.Abs(added.S1 - 175f) < 1e-3f && Math.Abs(added.AddedAt - 200f) < 1e-3f, "its new lane's line ends (in the chain's distance) where the widening is: " + added);
            // And two to three of the other way: it loses a lane going from 400 to 0.
            var drop = new RoadChain(new[] { Straight(0f, 200f, Two, TwoBack, 60f, ChainJoint.Pass), Straight(200f, 400f, Two, ThreeBack, 60f) });
            AutoPlan dropPlan = AutoMarking.Plan(drop, null, Opt());
            AutoRun dropEdge = Runs(dropPlan, "mk.line.edge").Find(r => r.Lateral < 0f);
            float length = MarkingTransition.Length(60f, 3.5f, true);
            Program.Near(dropEdge.ShiftS0, 200f, 1e-3f, "the other way narrows: the move is after the node in the chain's distance (before it in that way)");
            Program.Near(dropEdge.ShiftS1, 200f + length, 1e-3f, "by the formula's length");
            AutoRun change = Runs(dropPlan, "mk.line.change").Find(r => r.Lateral < 0f);
            Program.Check(change != null && Math.Abs(change.S0 - 200f) < 1e-3f && Math.Abs(change.S1 - (200f + length + AutoMarking.ChangeLead)) < 1e-3f, "and its change line: " + change);
            Program.Check(AutoMarking.Plan(chain, null, Opt()).Runs.FindAll(r => r.Id == "mk.line.doubleyellow").Count == 1, "the centre line stays one line");
            // A centre line that moves: the direction that loses room has the move before the node in its way.
            AutoMarking.TransitionPlace(0f, 1.75f, true, out bool n1, out bool a1);
            AutoMarking.TransitionPlace(0f, -1.75f, true, out bool n2, out bool a2);
            Program.Check(n1 && !a1 && n2 && a2, "a centre line moving right moves before the node, left after it");
        }

        private static void SightTable()
        {
            Program.Near(SightDistance.Overtaking(80f), 550f, 1e-3f, "80 km/h: 550 m");
            Program.Near(SightDistance.Overtaking(60f), 350f, 1e-3f, "60: 350");
            Program.Near(SightDistance.Overtaking(50f), 325f, 1e-3f, "50: 325");
            Program.Near(SightDistance.Overtaking(40f), 200f, 1e-3f, "40: 200");
            Program.Near(SightDistance.Overtaking(30f), 150f, 1e-3f, "30: 150");
            Program.Near(SightDistance.Overtaking(45f), 262.5f, 1e-3f, "between: interpolated");
            Program.Near(SightDistance.Overtaking(20f), 100f, 1e-3f, "below: extrapolated");
            Program.Check(float.IsNaN(SightDistance.Overtaking(100f)) && float.IsNaN(SightDistance.Overtaking(0f)), "none at 100 (a divided road) or for no speed");
            Program.Near(SightDistance.StoppingSight(60f), 75f, 1e-3f, "the stopping sight distance at 60: 75 m");
            Program.Near(SightDistance.StoppingSight(110f), 185f, 1e-3f, "at 110: 185 m");
        }

        /// <summary>The sight distance by brute force: every line from the eye crossed with every place in between (what 0.12.3's cone of directions must give again).</summary>
        private static float[] SightByEveryLine(RoadChain chain, bool withChain, float driverLateral, float targetLateral, Func<float, float> clearance, float max)
        {
            float step = SightDistance.Step;
            int n = (int)Math.Floor(chain.Length / step) + 1;
            var centre = new Vec3[n];
            var right = new Vec3[n];
            var room = new float[n];
            for (int k = 0; k < n; k++)
            {
                float s = Math.Min(chain.Length, k * step);
                centre[k] = chain.PointAt(s, 0f);
                right[k] = chain.RightAt(s);
                room[k] = clearance(s);
            }

            int reach = (int)Math.Ceiling(max / step);
            var result = new float[n];
            int dir = withChain ? 1 : -1;
            for (int i = 0; i < n; i++)
            {
                Vec3 eye = centre[i] + right[i] * driverLateral + new Vec3(0f, SightDistance.EyeHeight, 0f);
                int seen = 0;
                for (int d = 1; d <= reach; d++)
                {
                    int j = i + dir * d;
                    if (j < 0 || j >= n)
                    {
                        seen = reach;
                        break;
                    }

                    Vec3 target = centre[j] + right[j] * targetLateral + new Vec3(0f, SightDistance.ObjectHeight, 0f);
                    double ux = target.X - eye.X, uz = target.Z - eye.Z;
                    double rise = (target.Y - eye.Y) / Math.Sqrt(ux * ux + uz * uz);
                    bool clear = true;
                    for (int m = 1; m < d && clear; m++)
                    {
                        // Where the line from the eye meets the line across the place k: eye + tau u = centre + sigma right.
                        int k = i + dir * m;
                        double wx = centre[k].X - eye.X, wz = centre[k].Z - eye.Z, rx = right[k].X, rz = right[k].Z;
                        double det = -ux * rz + rx * uz;
                        if (Math.Abs(det) < 1e-12)
                        {
                            clear = false;
                            break;
                        }

                        double tau = (-wx * rz + rx * wz) / det, sigma = (ux * wz - wx * uz) / det;
                        double slope = (centre[k].Y - eye.Y) / Math.Sqrt(wx * wx + wz * wz);
                        if (!(tau > 0.0) || Math.Abs(sigma) > room[k] + 1e-6 || rise < slope - 1e-9)
                            clear = false;
                    }

                    if (!clear)
                        break;
                    seen = d;
                }

                result[i] = Math.Min(max, seen * step);
            }

            return result;
        }

        private static void SightSame()
        {
            var roads = new List<(string, RoadChain)>
            {
                ("straight", new RoadChain(new[] { Straight(0f, 900f, One, OneBack, 60f) })),
                ("crest", new RoadChain(new[] { Piece(new Bezier3(P(0f, 0f, 0f), P(0f, 300f, 20f), P(0f, 500f, 20f), P(0f, 800f, 0f)), One, OneBack, 60f) })),
                ("sag", new RoadChain(new[] { Piece(new Bezier3(P(0f, 0f, 20f), P(0f, 300f, 0f), P(0f, 500f, 0f), P(0f, 800f, 20f)), One, OneBack, 60f) })),
                ("bend", new RoadChain(new[] { Piece(new Bezier3(P(0f, 0f), P(0f, 120f), P(0f, 200f), P(120f, 200f)), One, OneBack, 60f) })),
                ("S bends over a hill", new RoadChain(new[]
                {
                    Piece(new Bezier3(P(0f, 0f, 0f), P(0f, 150f, 8f), P(80f, 200f, 12f), P(150f, 300f, 10f)), One, OneBack, 60f, ChainJoint.Pass),
                    Piece(new Bezier3(P(150f, 300f, 10f), P(220f, 400f, 8f), P(60f, 450f, 4f), P(40f, 650f, 0f)), One, OneBack, 60f, ChainJoint.Pass),
                    Piece(new Bezier3(P(40f, 650f, 0f), P(20f, 850f, -4f), P(-150f, 850f, 2f), P(-200f, 1000f, 6f)), One, OneBack, 60f),
                })),
                ("a hairpin", new RoadChain(new[] { Piece(new Bezier3(P(0f, 0f), P(0f, 160f), P(60f, 160f), P(60f, 0f)), One, OneBack, 40f) })),
            };
            foreach ((string name, RoadChain chain) in roads)
            {
                foreach (float room in new[] { 2f, 6f, 14f })
                {
                    foreach (bool with in new[] { true, false })
                    {
                        float driver = with ? 1.75f : -1.75f;
                        float[] fast = SightDistance.Ahead(chain, with, driver, -driver, s => room, 550f);
                        float[] slow = SightByEveryLine(chain, with, driver, -driver, s => room, 550f);
                        int differ = 0, worst = 0;
                        for (int i = 0; i < Math.Min(fast.Length, slow.Length); i++)
                        {
                            if (Math.Abs(fast[i] - slow[i]) > 1e-3f)
                            {
                                differ++;
                                if (Math.Abs(fast[i] - slow[i]) > Math.Abs(fast[worst] - slow[worst]))
                                    worst = i;
                            }
                        }

                        Program.Check(fast.Length == slow.Length && differ == 0,
                            $"{name}, room {room} m, {(with ? "with" : "against")} the chain: {differ} of {fast.Length} place(s) differ" + (differ > 0 ? $" (worst at {worst}: {fast[worst]} against {slow[worst]})" : ""));
                    }
                }
            }
        }

        private static void NoPassing()
        {
            // A long straight road of one lane each way at 60 km/h: both may overtake everywhere.
            var straight = new RoadChain(new[] { Straight(0f, 1200f, One, OneBack, 60f) });
            AutoPlan plain = AutoMarking.Plan(straight, null, Opt());
            Program.Check(Runs(plain, "mk.line.centre").Count == 1 && Runs(plain, "mk.line.yellowsingle").Count == 0 && Runs(plain, "mk.line.yellowsd").Count == 0, "a straight road: the crossable centre line");

            // Over a crest 15 m high: no one sees over it near the top.
            var crest = new RoadChain(new[] { Piece(new Bezier3(P(0f, 0f, 0f), P(0f, 300f, 20f), P(0f, 500f, 20f), P(0f, 800f, 0f)), One, OneBack, 60f) });
            AutoPlan hill = AutoMarking.Plan(crest, null, Opt());
            List<AutoRun> solid = Runs(hill, "mk.line.yellowsingle");
            Program.Check(solid.Count >= 1, "over the crest a single yellow solid line (6.1.4): " + string.Join(" | ", hill.Runs.FindAll(r => r.Id.Contains("yellow") || r.Id == "mk.line.centre")));
            Program.Check(solid.Exists(r => r.S0 < 400f && r.S1 > 400f), "at the top");
            Program.Check(Runs(hill, "mk.line.yellowsd").Count >= 1, "and the solid-and-dashed line where one way sees far enough (6.1.3)");
            var all = hill.Runs.FindAll(r => r.Id == "mk.line.centre" || r.Id == "mk.line.yellowsd" || r.Id == "mk.line.yellowsingle");
            all.Sort((a, b) => a.S0.CompareTo(b.S0));
            bool joined = true;
            for (int i = 1; i < all.Count; i++)
                joined &= Math.Abs(all[i].S0 - all[i - 1].S1) < 0.01f;
            Program.Check(joined && Math.Abs(all[0].S0) < 0.01f, "the zones follow one another without a gap");
            Program.Check(all.TrueForAll(r => r.Length >= AutoMarking.MinZone - SightDistance.Step || r == all[0] || r == all[all.Count - 1]), "no zone shorter than 30 m inside");
            // The dashed side is that of the traffic that may overtake: climbing toward the crest with the chain one cannot see over it; past the top, going down, one sees the road ahead.
            AutoRun before = all.Find(r => r.Id == "mk.line.yellowsd" && r.S1 <= 420f);
            if (before != null)
                Program.Check(before.Setting("side") == "l", "before the top the traffic against the chain (going down) may overtake: dashed on its side, the left: " + before);

            // A tight bend: the inside hides the view.
            var bend = new RoadChain(new[] { Piece(new Bezier3(P(0f, 0f), P(0f, 120f), P(0f, 200f), P(120f, 200f)), One, OneBack, 60f) });
            Program.Check(AutoMarking.Plan(bend, null, Opt()).Runs.Exists(r => r.Id == "mk.line.yellowsingle" || r.Id == "mk.line.yellowsd"), "on a bend no passing too");
            AutoOptions off = Opt();
            off.NoPassing = false;
            Program.Check(!AutoMarking.Plan(crest, null, off).Runs.Exists(r => r.Id == "mk.line.yellowsingle"), "unless it is off");
            // Two lanes each way: a double yellow line, untouched.
            Program.Check(!AutoMarking.Plan(new RoadChain(new[] { Piece(crest.Pieces[0].Curve, Two, TwoBack, 60f) }), null, Opt()).Runs.Exists(r => r.Id == "mk.line.yellowsingle"), "a road of two lanes each way keeps its double yellow line");

            Program.Check(AutoMarking.PassingClass(true, true) == 0 && AutoMarking.PassingClass(true, false) == 1 && AutoMarking.PassingClass(false, true) == 2 && AutoMarking.PassingClass(false, false) == 3, "the classes");
            var classes = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 3, 3, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
            AutoMarking.SmoothZones(classes, 5f);
            Program.Check(classes[8] == 3 && classes[9] == 3, "a short no-passing zone is kept (the stricter)");
            var lone = new[] { 3, 3, 3, 3, 3, 3, 3, 0, 3, 3, 3, 3, 3, 3 };
            AutoMarking.SmoothZones(lone, 5f);
            Program.Check(lone[7] == 3, "a short passing zone between no-passing zones is no-passing");
        }

        private static void BusLanes()
        {
            // Two lanes with the chain, the right one a bus lane; one against; kerbs both sides.
            var piece = Straight(0f, 700f, Two, OneBack, 50f);
            piece.BusForward.Add(5.25f);
            piece.FootwayLeft = piece.FootwayRight = true;
            var chain = new RoadChain(new[] { piece });
            AutoOptions o = AutoOptions.Default;
            AutoPlan plan = AutoMarking.Plan(chain, null, o);
            List<AutoRun> reserved = Runs(plan, "mk.line.reserved");
            Program.Check(reserved.Count == 1 && Math.Abs(reserved[0].Lateral - 3.5f) < 1e-3f, "the line beside the bus lane is the bus lane line (线59)");
            Program.Check(!Runs(plan, "mk.line.lane").Exists(r => Math.Abs(r.Lateral - 3.5f) < 0.01f), "not the ordinary lane line");
            Program.Check(Runs(plan, "mk.line.edge").Exists(r => r.Lateral > 6.9f), "the outermost bus lane keeps its edge line (Fig. 87), kerb or not");
            Program.Check(!Runs(plan, "mk.line.edge").Exists(r => r.Lateral < 0f), "the other side, three lanes and a kerb, has none (5.4.5)");
            List<AutoStamp> words = plan.Stamps.FindAll(s => s.Id == MarkingTemplates.WordId);
            Program.Check(words.Count == 3 && words.TrueForAll(s => Math.Abs(s.Lateral - 5.25f) < 1e-3f && s.WithChain && s.Settings.Exists(kv => kv.Key == "text" && kv.Value == "公交")), "公交 in the bus lane: " + words.Count);
            words.Sort((a, b) => a.S.CompareTo(b.S));
            Program.Near(words[0].S, AutoMarking.MarkLead, 1e-3f, "at its start");
            Program.Near(words[1].S - words[0].S, AutoMarking.BusWordEvery, 1e-3f, "and again every 300 m");
            // A bus lane against the chain: from the far end.
            var back = Straight(0f, 200f, One, TwoBack, 50f);
            back.BusBackward.Add(-5.25f);
            AutoStamp first = AutoMarking.Plan(new RoadChain(new[] { back }), null, o).Stamps.Find(s => s.Id == MarkingTemplates.WordId);
            Program.Check(first != null && !first.WithChain && Math.Abs(first.S - (200f - AutoMarking.MarkLead)) < 1e-3f, "against the chain its start is the far end");
            AutoOptions off = o;
            off.Special = false;
            Program.Check(!AutoMarking.Plan(chain, null, off).Runs.Exists(r => r.Id == "mk.line.reserved"), "off: none");
        }

        private static void BikeLanes()
        {
            var piece = Straight(0f, 400f, One, OneBack, 40f);
            piece.BikeForward.Add(4.5f);
            piece.BikeBackward.Add(-4.5f);
            piece.BikeWidth = 2f;
            piece.FootwayLeft = piece.FootwayRight = true;
            var chain = new RoadChain(new[] { piece });
            AutoPlan plan = AutoMarking.Plan(chain, null, AutoOptions.Default);
            List<AutoRun> edges = Runs(plan, "mk.line.edge");
            Program.Check(edges.Count == 2 && edges.Exists(r => Math.Abs(r.Lateral - 3.5f) < 0.01f) && edges.Exists(r => Math.Abs(r.Lateral + 3.5f) < 0.01f), "the lines between the motor and the cycle lanes (5.4.5), kerbs or not: " + string.Join(";", edges));
            var open = Straight(0f, 400f, One, OneBack, 40f);
            open.BikeForward.Add(4.5f);
            open.BikeBackward.Add(-4.5f);
            open.BikeWidth = 2f;
            List<AutoRun> openEdges = Runs(AutoMarking.Plan(new RoadChain(new[] { open }), null, AutoOptions.Default), "mk.line.edge");
            Program.Check(openEdges.Count == 4 && openEdges.Exists(r => Math.Abs(r.Lateral - 5.5f) < 0.01f) && openEdges.Exists(r => Math.Abs(r.Lateral + 5.5f) < 0.01f), "without kerbs the cycle lanes' outer edges too");
            List<AutoStamp> marks = plan.Stamps.FindAll(s => s.Id == "mk.sym.bicycle");
            Program.Check(marks.Count == 6 && marks.TrueForAll(s => s.Settings.Exists(kv => kv.Key == "width" && kv.Value == "100") && s.Settings.Exists(kv => kv.Key == "arrow" && kv.Value == "1")), "the cycle mark with its arrow, half the lane wide (5.15.1): " + marks.Count);
            Program.Check(marks.FindAll(s => s.WithChain && Math.Abs(s.Lateral - 4.5f) < 1e-3f).Count == 3 && marks.FindAll(s => !s.WithChain && Math.Abs(s.Lateral + 4.5f) < 1e-3f).Count == 3, "each way in its lane, every 150 m");
            // The stop line covers the cycle lane (6.4.3).
            var end = Straight(0f, 100f, One, OneBack, 40f);
            end.BikeForward.Add(4.5f);
            end.BikeWidth = 2f;
            var approach = new ChainApproach { StopS = 100f, WithChain = true, Control = JunctionControl.Signals };
            approach.Lanes.Add(new ApproachLane { Lateral = 1.75f, Straight = true });
            AutoStamp stop = AutoMarking.Plan(new RoadChain(new[] { end }), new[] { approach }, Opt(true)).Stamps.Find(s => s.Id == "mk.stop");
            Program.Check(stop != null && Math.Abs(stop.Lateral + stop.Span * 0.5f - 5.5f) < 1e-3f && Math.Abs(stop.Lateral - stop.Span * 0.5f) < 1e-3f, "the stop line from the centre line over the cycle lane: " + stop);
        }

        private static void MidCrossing()
        {
            var chain = new RoadChain(new[] { Straight(0f, 200f, Two, TwoBack, 50f, ChainJoint.Pass), Straight(200f, 400f, Two, TwoBack, 50f) });
            chain.Crossings.Add(new ChainCrossing(198.5f, 201.5f, false));
            AutoPlan kept = AutoMarking.Plan(chain, null, Opt(false));
            Program.Check(kept.Runs.TrueForAll(r => r.S1 <= 198.5f - AutoMarking.CrosswalkClear + 1e-3f || r.S0 >= 201.5f + AutoMarking.CrosswalkClear - 1e-3f), "the lines break at the crosswalk (5.8.5)");
            Program.Check(!kept.Stamps.Exists(s => s.Id == "mk.crosswalk") && kept.Notes.Exists(n => n.Contains("crosswalk")), "the game's crosswalk is kept, with a note");
            AutoPlan plan = AutoMarking.Plan(chain, null, Opt(true));
            AutoStamp walk = plan.Stamps.Find(s => s.Id == "mk.crosswalk");
            Program.Check(walk != null && Math.Abs(walk.S - 200f) < 1e-3f && Math.Abs(walk.Span - 14f) < 1e-3f && Math.Abs(walk.Lateral) < 1e-3f, "the crosswalk over the whole road: " + walk);
            Program.Check(walk.Settings.Exists(kv => kv.Key == "len" && kv.Value == "3"), "3 m across a segment (5.8.3)");
            List<AutoStamp> stops = plan.Stamps.FindAll(s => s.Id == "mk.stop");
            Program.Check(stops.Count == 2 && stops.Exists(s => s.WithChain && Math.Abs(s.S - (198.5f - AutoMarking.StopBeforeCrosswalk - AutoMarking.StopWidth * 0.5f)) < 1e-3f) && stops.Exists(s => !s.WithChain && Math.Abs(s.S - (201.5f + AutoMarking.StopBeforeCrosswalk + AutoMarking.StopWidth * 0.5f)) < 1e-3f), "a stop line before it each way (5.8.6)");
            List<AutoStamp> diamonds = plan.Stamps.FindAll(s => s.Id == "mk.diamond");
            Program.Check(diamonds.Count == 8, "no signal: two diamonds in each of the four lanes (Fig. 14): " + diamonds.Count);
            float stopEdge = 198.5f - AutoMarking.StopBeforeCrosswalk - AutoMarking.StopWidth;
            Program.Check(diamonds.Exists(s => s.WithChain && Math.Abs(s.S - (stopEdge - AutoMarking.DiamondNear)) < 1e-3f) && diamonds.Exists(s => s.WithChain && Math.Abs(s.S - (stopEdge - AutoMarking.DiamondNear - AutoMarking.DiamondGap)) < 1e-3f), "30 m and 45 m before the stop line");
            AutoRun centre = plan.Runs.Find(r => r.Id == "mk.line.doubleyellow" && r.S1 < 200f);
            Program.Check(centre != null && centre.S1 <= stopEdge + 1e-3f, "the approach's lines end at the stop line");
            chain.Crossings[0] = new ChainCrossing(198.5f, 201.5f, true);
            Program.Check(!AutoMarking.Plan(chain, null, Opt(true)).Stamps.Exists(s => s.Id == "mk.diamond"), "with signals no diamonds");
            AutoOptions off = Opt(true);
            off.Crossings = false;
            Program.Check(AutoMarking.Plan(chain, null, off).Runs.Exists(r => r.S0 < 199f && r.S1 > 201f), "off: the lines run through");
        }

        private static List<Vec3> Arc(float radius, float startX, int n = 24)
        {
            // A left turn of a quarter circle: from (startX, 0) heading +Z, turning left (to -X) about the centre (startX - radius, 0).
            var points = new List<Vec3>();
            for (int i = 0; i <= n; i++)
            {
                double a = Math.PI * 0.5 * i / n;
                points.Add(P(startX - radius + radius * (float)Math.Cos(a), radius * (float)Math.Sin(a)));
            }

            return points;
        }

        private static void GuideLines()
        {
            List<Vec3> inner = Arc(15f, 1.75f), outer = Arc(18.5f, 5.25f);
            Program.Check(!JunctionGuide.Wanted(JunctionGuides.Auto, new List<List<Vec3>> { inner }), "one short left turn: none (5.6.3)");
            Program.Check(JunctionGuide.Wanted(JunctionGuides.Auto, new List<List<Vec3>> { inner, outer }), "two left-turn lanes: yes");
            Program.Check(JunctionGuide.Wanted(JunctionGuides.Auto, new List<List<Vec3>> { Arc(40f, 1.75f) }), "a large junction: yes");
            Program.Check(JunctionGuide.Wanted(JunctionGuides.All, new List<List<Vec3>> { inner }) && !JunctionGuide.Wanted(JunctionGuides.None, new List<List<Vec3>> { inner, outer }), "all, none");
            List<GuideLine> lines = JunctionGuide.Lines(new List<List<Vec3>> { inner, outer }, new List<float> { 3.5f, 3.5f }, true, 3f);
            Program.Check(lines.Count == 2 && lines[0].Yellow && !lines[1].Yellow, "on the left of the first, yellow; between the two, white (5.6.4)");
            // The first is 1.75 m to the left of its turn: on the circle of radius 13.25.
            foreach (Vec3 p in lines[0].Points)
                Program.Check(Math.Abs(Vec3.DistanceXZ(p, P(1.75f - 15f, 0f)) - 13.25f) < 0.15f, "the left line on the inside of the turn: " + Vec3.DistanceXZ(p, P(1.75f - 15f, 0f)));
            float length0 = WaitingArea.Length(lines[0].Points), full = (float)(Math.PI * 0.5 * 13.25);
            Program.Check(length0 < full - 3f - JunctionGuide.EndClear + 0.5f && length0 > full - 3f - JunctionGuide.EndClear - 1f, "from 3 m in to 5 m before the end: " + length0 + " of " + full);
            Program.Check(!JunctionGuide.Lines(new List<List<Vec3>> { inner }, new List<float> { 3.5f }, false, 1f)[0].Yellow, "not the leftmost lane: white");
            Program.Check(JunctionGuide.Lines(new List<List<Vec3>> { inner }, null, true, 30f).Count == 0, "nothing left after the start: none");
            SignDef def = SignCatalog.ById("mk.line.junction");
            Program.Check(def != null && def.Template.FieldOf("colour") != null, "the junction guide line takes its colour");
        }

        private static void Word()
        {
            SignDef def = SignCatalog.ById(MarkingTemplates.WordId);
            Program.Check(def != null && def.IsMarking, "the word on the road is in the catalogue");
            GuideTemplate t = def.Template;
            MarkingPlan plan = MarkingTemplates.PlanOf(t, t.Parse(t.Default().ToString()).With("text", "公交").With("band", "mid").ToString(), false);
            Program.Check(plan != null && plan.Parts.Count > 0, "it has paint");
            var texts = new List<MarkingText>();
            foreach (MarkingShape shape in t.Marking(t.Parse(t.Default().ToString()).With("text", "快速公交").With("band", "mid")).Shapes)
                texts.AddRange(shape.Texts);
            Program.Check(texts.Count == 4 && texts[0].Text == "快" && texts[3].Text == "交", "the characters in order: " + texts.Count);
            Program.Check(texts[0].Z < texts[1].Z && texts[1].Z < texts[2].Z, "from near to far along the traffic (5.14.1)");
            Program.Near(texts[0].Length, 3f, 1e-4f, "3 m high, the smallest of the middle band (Table 3)");
            Program.Near(texts[0].Width, 1f, 1e-4f, "a third as wide");
            Program.Near(texts[1].Z - texts[0].Z, 3f + 2f, 1e-4f, "2 m apart");
            Program.Check(texts[0].Z - texts[0].Length * 0.5f > -1e-3f, "starting at the item");
            MarkingShape large = t.Marking(t.Parse(t.Default().ToString()).With("band", "high").With("scale", "large")).Shapes[0];
            Program.Near(large.Texts[0].Length, 9f, 1e-4f, "9 m at the largest of the high band");
            Program.Check(MarkingTemplates.ResolveSize(t, t.Default().ToString(), 30f, true).Contains("band=low"), "auto: the band of the road's speed");
        }
    }
}
