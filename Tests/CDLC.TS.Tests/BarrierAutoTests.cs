using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The barriers by the kind of road (0.12.15, BarrierAuto): the kind, the lines of each kind, joined through nodes, cut at junctions and crosswalks.</summary>
    internal static class BarrierAutoTests
    {
        public static void RunAll()
        {
            Program.Run("auto barriers: the kind of a road", Kinds);
            Program.Run("auto barriers: the lines of each kind", Lines);
            Program.Run("auto barriers: through nodes, at junctions and crosswalks", Cuts);
            Program.Run("auto barriers: at merges and through junctions (0.12.16)", Merges);
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static ChainPiece Piece(float z0, float z1, float[] forward, float[] backward, float kmh = 50f, bool kerbs = false, float half = 8f, ChainJoint after = ChainJoint.End, float laneFrom = float.NaN, float laneTo = float.NaN)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(P(0f, z0), P(0f, z1)), Kmh = kmh, HasSpeed = true, After = after, LaneFrom = laneFrom, LaneTo = laneTo, HalfWidth = half, FootwayLeft = kerbs, FootwayRight = kerbs };
            piece.Forward.AddRange(forward);
            piece.Backward.AddRange(backward);
            return piece;
        }

        private static readonly float[] Two = { 1.75f, 5.25f }, TwoBack = { -1.75f, -5.25f }, One = { 1.75f }, OneBack = { -1.75f };

        private static void Kinds()
        {
            var city = new RoadChain(new[] { Piece(0f, 100f, Two, TwoBack, 50f, true) });
            var fast = new RoadChain(new[] { Piece(0f, 100f, Two, TwoBack, 80f) });
            var slow = new RoadChain(new[] { Piece(0f, 100f, One, OneBack, 40f, false, 4f) });
            Program.Check(BarrierAuto.Classify(true, city) == RoadClass.Highway, "the game's highway rules: a motorway");
            Program.Check(BarrierAuto.Classify(false, city) == RoadClass.City, "footways on both sides: a city road");
            Program.Check(BarrierAuto.Classify(false, fast) == RoadClass.National, "no footways, 80 km/h: a national road");
            Program.Check(BarrierAuto.Classify(false, slow) == RoadClass.Village, "no footways, 40 km/h: a village road");
            GuideTemplate t = MarkingTemplates.ById(BarrierAuto.Id);
            Program.Check(t != null && t.Group == MarkingTemplates.BarrierGroup && !MarkingTemplates.IsRun(t), "the brush is in the barriers' group and is not a line");
            Program.Check(BarrierAuto.ClassOf(t.Parse("road=vil"), true, city) == RoadClass.Village && BarrierAuto.ClassOf(t.Default(), false, fast) == RoadClass.National, "the kind chosen in the panel, else the automatic one");
        }

        private static void Lines()
        {
            // A motorway of two lanes each way, 16 m wide, the opposing lanes close (3.5 m apart): W-beams on both sides inside the edge, a concrete median on the centre line.
            List<BarrierRun> hw = BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, Two, TwoBack, 120f) }), RoadClass.Highway, true, true);
            Program.Check(hw.Count == 3, "three lines: " + hw.Count);
            BarrierRun right = hw.Find(r => r.Lateral > 3f), left = hw.Find(r => r.Lateral < -3f), median = hw.Find(r => Math.Abs(r.Lateral) < 0.5f);
            Program.Check(right != null && right.Id == MarkingTemplates.WBeamId && right.Setting("where") == "l", "the right side: a W-beam, the traffic on its left");
            Program.Check(left != null && left.Id == MarkingTemplates.WBeamId && left.Setting("where") == "r", "the left side: the traffic on its right");
            Program.Near(right.Lateral, 8f - BarrierAuto.EdgeInset, 1e-4f, "inside the road's edge");
            Program.Check(median != null && median.Id == MarkingTemplates.ConcreteBarrierId && median.Setting("where") == "m", "a concrete median where the opposing lanes are close");
            // A wide median: a W-beam in it.
            List<BarrierRun> wide = BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, new[] { 4f, 7.5f }, new[] { -4f, -7.5f }, 120f, false, 11f) }), RoadClass.Highway, true, true);
            Program.Check(wide.Exists(r => r.Id == MarkingTemplates.WBeamId && r.Setting("where") == "m" && Math.Abs(r.Lateral) < 0.01f), "a wide median: a W-beam in both directions");
            // One-way: no median.
            Program.Check(!BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, Two, new float[0], 120f) }), RoadClass.Highway, true, true).Exists(r => r.Setting("where") == "m"), "one way: no median");
            // The options: sides off, median off.
            Program.Check(BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, Two, TwoBack, 120f) }), RoadClass.Highway, false, true).Count == 1, "no sides: the median alone");
            Program.Check(BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, Two, TwoBack, 120f) }), RoadClass.Highway, true, false).Count == 2, "no median: the sides alone");

            // A national road of one lane each way: sides only (a median only with two lanes each way).
            List<BarrierRun> nat = BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, One, OneBack, 80f, false, 5f) }), RoadClass.National, true, true);
            Program.Check(nat.Count == 2 && nat.TrueForAll(r => r.Id == MarkingTemplates.WBeamId), "a national road of one lane each way: W-beams by both sides");

            // A city road: a median fence where two lanes go each way; none with one each way; no sides.
            List<BarrierRun> city = BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, Two, TwoBack, 50f, true) }), RoadClass.City, true, true);
            Program.Check(city.Count == 1 && city[0].Id == MarkingTemplates.CityFenceId && Math.Abs(city[0].Lateral) < 0.01f, "a city road of two lanes each way: a median fence");
            Program.Check(BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, One, OneBack, 50f, true) }), RoadClass.City, true, true).Count == 0, "one lane each way: none");
            Program.Check(BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, new[] { 4f, 7.5f }, new[] { -4f, -7.5f }, 50f, true, 12f) }), RoadClass.City, true, true).Count == 0, "a planted median: no fence");

            // A village road: warning posts by both sides, every 10 m, beyond the lanes.
            List<BarrierRun> vil = BarrierAuto.Plan(new RoadChain(new[] { Piece(0f, 100f, One, OneBack, 40f, false, 2.5f) }), RoadClass.Village, true, true);
            Program.Check(vil.Count == 2 && vil.TrueForAll(r => r.Id == MarkingTemplates.WarningPostId && r.Setting("gap") == "10"), "a village road: warning posts by both sides");
            // Beyond the lanes' middles (1.75 m) and not outside the road, which is 2.5 m to a side (0.12.65: the rule of half a lane and 30 cm put them 3.7 m out, 1.2 m off a road 5 m wide).
            Program.Check(vil.TrueForAll(r => Math.Abs(r.Lateral) >= 1.75f && Math.Abs(r.Lateral) <= 2.5f + 1e-4f), "beyond the lanes on a narrow road, and on it: " + string.Join(", ", vil.ConvertAll(r => r.Lateral.ToString("0.00"))));
        }

        private static ChainPiece Joined(ChainPiece p, bool left, bool right)
        {
            p.JoinLeft = left;
            p.JoinRight = right;
            return p;
        }

        private static void Merges()
        {
            // A ramp of one lane (8 m wide) merging from the left into a motorway of two lanes (16 m wide): the main road joins the chain on its left at the junction.
            RoadChain Merge() => new RoadChain(new[]
            {
                Joined(Piece(0f, 60f, new[] { 0f }, new float[0], 80f, false, 4f, ChainJoint.Junction, 0f, 50f), true, false),
                Piece(60f, 160f, new[] { 2f, 6f }, new float[0], 80f, false, 8f, ChainJoint.End, 8f, 100f)
            });

            // Auto (Side): the right side goes on through the merge, the left (where the main road joins) is cut.
            List<BarrierRun> side = BarrierAuto.Plan(Merge(), RoadClass.Highway, true, true, JunctionMode.Side);
            List<BarrierRun> right = side.FindAll(r => r.EdgeSide > 0), left = side.FindAll(r => r.EdgeSide < 0);
            Program.Check(right.Count == 1 && Math.Abs(right[0].S0) < 1e-3f && Math.Abs(right[0].S1 - 160f) < 1e-3f, $"the outer side goes on through the merge: {right.Count} line(s)");
            Program.Check(left.Count == 2, "the side the road joins is cut: " + left.Count);
            // It follows the road's edge: the ramp's (3.65 m) then the motorway's (7.65 m), changing smoothly over the junction.
            RoadChain chain = Merge();
            Program.Near(right[0].LateralAt(chain, 20f), 3.65f, 1e-3f, "along the ramp's edge");
            Program.Near(right[0].LateralAt(chain, 120f), 6f + 1.625f + 0.3f, 1e-3f, "along the motorway's edge after the merge (its outer lane's edge and 30 cm)");
            float mid = right[0].LateralAt(chain, 59f);
            Program.Check(mid > 3.65f && mid < 7.925f, "in between over the junction: " + mid);
            float worst = 0f;
            for (float s = 40f; s < 80f; s += 0.5f)
                worst = Math.Max(worst, Math.Abs(right[0].LateralAt(chain, s + 0.5f) - right[0].LateralAt(chain, s)));
            Program.Check(worst < 0.35f, "no jump: at most " + worst + " m in half a metre");

            // Cut: both sides stop at the junction; Through: both go on.
            Program.Check(BarrierAuto.Plan(Merge(), RoadClass.Highway, true, false, JunctionMode.Cut).Count == 4, "cut: two pieces each side");
            Program.Check(BarrierAuto.Plan(Merge(), RoadClass.Highway, true, false, JunctionMode.Through).Count == 2, "through: one line each side");

            // A median at a junction joined on one side goes on (Side), at a crossroads it is cut; Through goes on at both.
            RoadChain Crossing(bool left, bool right) => new RoadChain(new[]
            {
                Joined(Piece(0f, 60f, Two, TwoBack, 120f, false, 8f, ChainJoint.Junction, 0f, 50f), left, right),
                Piece(60f, 120f, Two, TwoBack, 120f, false, 8f, ChainJoint.End, 10f, 60f)
            });
            bool Median(List<BarrierRun> runs, int count) => runs.FindAll(r => r.Setting("where") == "m").Count == count;
            Program.Check(Median(BarrierAuto.Plan(Crossing(false, true), RoadClass.Highway, false, true, JunctionMode.Side), 1), "a median goes on where a road joins one side");
            Program.Check(Median(BarrierAuto.Plan(Crossing(true, true), RoadClass.Highway, false, true, JunctionMode.Side), 2), "and is cut at a crossroads");
            Program.Check(Median(BarrierAuto.Plan(Crossing(true, true), RoadClass.Highway, false, true, JunctionMode.Through), 1), "through: on across a crossroads too");
            Program.Check(Median(BarrierAuto.Plan(Crossing(false, true), RoadClass.Highway, false, true, JunctionMode.Cut), 2), "cut: at every junction");

            // 0.12.17: a hand-drawn median in the automatic mode: on through a junction joined on one side (either side), cut at a crossroads.
            Program.Check(Crossing(true, false).Spans(0f, JunctionMode.Side, true).Count == 1 && Crossing(false, true).Spans(0f, JunctionMode.Side, true).Count == 1, "a median on through a junction joined on the left or the right");
            Program.Check(Crossing(true, true).Spans(0f, JunctionMode.Side, true).Count == 2, "and cut at a crossroads");
            Program.Check(Crossing(true, false).Spans(0f, JunctionMode.Side, false).Count == 2, "(a line on the left is cut where a road joins on the left)");

            // The chain's spans by mode, and the mode from the settings.
            RoadChain m = Merge();
            Program.Check(m.Spans(1f, JunctionMode.Through).Count == 1 && m.Spans(-1f, JunctionMode.Side).Count == 2 && m.Spans(1f, JunctionMode.Side).Count == 1 && m.Spans(1f, JunctionMode.Cut).Count == 2, "the spans by mode");
            GuideTemplate wbeam = MarkingTemplates.ById(MarkingTemplates.WBeamId);
            Program.Check(BarrierAuto.JunctionOf(wbeam.Default()) == JunctionMode.Side && BarrierAuto.JunctionOf(wbeam.Parse("jn=through")) == JunctionMode.Through && BarrierAuto.JunctionOf(wbeam.Parse("jn=cut")) == JunctionMode.Cut,
                "a barrier's setting: auto by default");
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                if (t.Group == MarkingTemplates.BarrierGroup && MarkingTemplates.IsRun(t))
                    Program.Check(t.FieldOf(BarrierAuto.JunctionKey) != null, t.Id + ": has the junction setting");
            }
        }

        private static void Cuts()
        {
            // Two pieces through a node: one line each side, all along; with a junction between them: cut where the lanes end and start.
            var pass = new RoadChain(new[] { Piece(0f, 50f, One, OneBack, 80f, false, 5f, ChainJoint.Pass, 2f, 50f), Piece(50f, 100f, One, OneBack, 80f, false, 5f, ChainJoint.End, 0f, 48f) });
            List<BarrierRun> through = BarrierAuto.Plan(pass, RoadClass.National, true, false);
            Program.Check(through.Count == 2 && through.TrueForAll(r => Math.Abs(r.S0 - 2f) < 1e-3f && Math.Abs(r.S1 - 98f) < 1e-3f), "through a node: one line each side, from the lanes' start to their end");
            var junction = new RoadChain(new[] { Piece(0f, 50f, One, OneBack, 80f, false, 5f, ChainJoint.Junction, 0f, 40f), Piece(50f, 100f, One, OneBack, 80f, false, 5f, ChainJoint.End, 10f, 50f) });
            List<BarrierRun> cut = BarrierAuto.Plan(junction, RoadClass.National, true, false, JunctionMode.Cut);
            Program.Check(cut.Count == 4 && cut.Exists(r => Math.Abs(r.S1 - 40f) < 1e-3f) && cut.Exists(r => Math.Abs(r.S0 - 60f) < 1e-3f), "at a junction: cut from where the lanes end to where they start again");

            // A crosswalk from 30 to 34 m: the lines stop a metre before it and go on a metre after.
            var walked = new RoadChain(new[] { Piece(0f, 100f, Two, TwoBack, 50f, true) });
            walked.Crossings.Add(new ChainCrossing(30f, 34f, false));
            List<BarrierRun> fence = BarrierAuto.Plan(walked, RoadClass.City, true, true);
            Program.Check(fence.Count == 2 && Math.Abs(fence[0].S1 - 29f) < 1e-3f && Math.Abs(fence[1].S0 - 35f) < 1e-3f, $"cut round a crosswalk: {fence.Count} part(s)");
            List<(float, float)> parts = BarrierAuto.CutAt(0f, 10f, new[] { new ChainCrossing(-5f, 0.5f, false), new ChainCrossing(9f, 20f, false) });
            Program.Check(parts.Count == 1 && Math.Abs(parts[0].Item1 - 1.5f) < 1e-4f && Math.Abs(parts[0].Item2 - 8f) < 1e-4f, "crosswalks at the ends shorten it");
            Program.Check(BarrierAuto.CutAt(0f, 10f, null).Count == 1, "no crosswalks: as it is");
        }
    }
}
