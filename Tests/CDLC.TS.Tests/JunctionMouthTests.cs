using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.67 (the owner: "岔路节点还是会空"): a wall beside the chain at a junction is open at the mouths of the roads that join on its side, not from the end of the lanes before the node to the start
    /// of those after it (64 m in the owner's log). The chain made up: a road north (+Z) 400 m long with a junction at 200 m whose lanes end at 170 and begin at 230 (a node of 60 m), a road 14 m
    /// wide joining. The reading of the game's roads (ChainReader, ECS) is not here.
    /// </summary>
    internal static class JunctionMouthTests
    {
        public static void RunAll()
        {
            Program.Run("junction mouths: a T junction opens the wall over the joining road's width, not over the node", TJunction);
            Program.Run("junction mouths: a cross junction, a road on the other side, two roads on one side", Sides);
            Program.Run("junction mouths: a slanted road opens 2 w / sin wide and moves along", Slanted);
            Program.Run("junction mouths: where it cannot be worked out the old cut is used", Fallbacks);
            Program.Run("junction mouths: the openings themselves (merged, kept in the node, none left behind)", Openings);
            Program.Run("junction mouths: the markings, Cut and Through are as they were", Others);
            Program.Run("junction mouths: the automatic barriers follow it", AutoBarriers);
        }

        private static readonly Vec3 Up = new Vec3(0f, 0f, 1f);

        /// <summary>The chain: two pieces of 200 m, the first ending in a junction whose lanes end at 170 m, the second's beginning at 230; mouths of the roads that join (right, left).</summary>
        private static RoadChain Chain(IEnumerable<ChainMouth> mouths, bool joinRight, bool joinLeft, bool known = true, float turn = 0f, float half = 7f)
        {
            ChainPiece Piece(Vec3 a, Vec3 d, ChainJoint after, float laneFrom = float.NaN, float laneTo = float.NaN)
            {
                var piece = new ChainPiece { Curve = Bezier3.Line(a, d), Kmh = 80f, HasSpeed = true, After = after, LaneFrom = laneFrom, LaneTo = laneTo, HalfWidth = half };
                piece.Forward.Add(1.75f);
                piece.Backward.Add(-1.75f);
                return piece;
            }

            ChainPiece first = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 200f), ChainJoint.Junction, laneTo: 170f);
            first.JoinRight = joinRight;
            first.JoinLeft = joinLeft;
            first.Mouths.AddRange(mouths);
            first.MouthsKnown = known;
            Vec3 end = turn == 0f ? new Vec3(0f, 0f, 400f) : new Vec3((float)Math.Sin(turn) * 200f, 0f, 200f + (float)Math.Cos(turn) * 200f);
            ChainPiece second = Piece(new Vec3(0f, 0f, 200f), end, ChainJoint.End, laneFrom: 30f);
            return new RoadChain(new[] { first, second });
        }

        /// <summary>The chain's right at the junction (the sign of "right" is the chain's, whatever the axes are).</summary>
        private static Vec3 Right() => Up.RightXZ();

        private static ChainMouth Mouth(int side, float width, float angle = (float)(Math.PI / 2), bool forward = true)
        {
            // angle: between the road and the chain's direction (90 degrees: at a right angle); forward: leaning the way the chain runs.
            Vec3 dir = Up * (float)(Math.Cos(angle) * (forward ? 1.0 : -1.0)) + Right() * (float)(Math.Sin(angle) * side);
            return new ChainMouth { Dir = dir.HeadingXZ(), HalfWidth = width * 0.5f };
        }

        private static string Show(List<ChainSpan> spans) => string.Join(" ", spans.ConvertAll(s => $"{s.From:0.0}-{s.To:0.0}"));

        private static void TJunction()
        {
            RoadChain old = Chain(new[] { Mouth(1, 14f) }, true, false, known: false);
            List<ChainSpan> before = old.Spans(8f, JunctionMode.Side);
            Program.Check(before.Count == 2 && Math.Abs(before[0].To - 170f) < 0.01f && Math.Abs(before[1].From - 230f) < 0.01f, "the old cut: from the end of the lanes to the start of the lanes: " + Show(before));
            RoadChain chain = Chain(new[] { Mouth(1, 14f) }, true, false);
            List<ChainSpan> spans = chain.Spans(8f, JunctionMode.Side);
            Program.Check(spans.Count == 2 && Math.Abs(spans[0].To - 192.5f) < 0.02f && Math.Abs(spans[1].From - 207.5f) < 0.02f, "open over 14 m and 0.5 m each end of it: " + Show(spans));
            Program.Check(spans[1].From - spans[0].To < 16f && spans[0].To > 170f && spans[1].From < 230f, $"{spans[1].From - spans[0].To:0.0} m, not the node's 60 m");
            // The wall stands at another lateral (it follows the edge): a road at a right angle opens the same.
            List<ChainSpan> edge = chain.Spans(8f, JunctionMode.Side, false, s => 6.65f);
            Program.Check(edge.Count == 2 && Math.Abs(edge[0].To - 192.5f) < 0.02f, "a wall that follows the edge at 6.65 m: " + Show(edge));
            // The left wall is not cut: no road joins on the left.
            List<ChainSpan> left = chain.Spans(-8f, JunctionMode.Side);
            Program.Check(left.Count == 1 && left[0].To - left[0].From > 350f, "the wall on the other side goes through: " + Show(left));
        }

        private static void Sides()
        {
            RoadChain cross = Chain(new[] { Mouth(1, 14f), Mouth(-1, 10f) }, true, true);
            List<ChainSpan> right = cross.Spans(8f, JunctionMode.Side), left = cross.Spans(-8f, JunctionMode.Side);
            Program.Check(right.Count == 2 && Math.Abs(right[1].From - right[0].To - 15f) < 0.05f, "a cross junction, the right wall open over its road (14 m + 1): " + Show(right));
            Program.Check(left.Count == 2 && Math.Abs(left[1].From - left[0].To - 11f) < 0.05f, "the left wall over its own road (10 m + 1): " + Show(left));
            // Two roads on the right leaning 30 degrees forward and back (a five-way): at 8 m out they open over x 5.9..21.7 and -21.7..-5.9, a piece of wall between them.
            ChainMouth far = Mouth(1, 8f, (float)(Math.PI / 6), true), near = Mouth(1, 8f, (float)(Math.PI / 6), false);
            List<ChainSpan> five = Chain(new[] { far, near }, true, false).Spans(8f, JunctionMode.Side);
            Program.Check(five.Count == 3 && Math.Abs(five[1].To - five[1].From - 10.8f) < 0.1f, "two roads that open apart: three stretches of wall, 10.8 m of it between the openings: " + Show(five));
            // Two that open over each other are one.
            List<ChainSpan> one = Chain(new[] { Mouth(1, 14f), Mouth(1, 14f, (float)(Math.PI / 2.2)) }, true, false).Spans(8f, JunctionMode.Side);
            Program.Check(one.Count == 2, "two roads whose mouths overlap are one opening: " + Show(one));
            // A joining road on the right does not open the left wall of a cross junction twice.
            Program.Check(cross.Spans(8f, JunctionMode.Side).Count == 2, "once");
        }

        private static void Slanted()
        {
            // 60 degrees to the chain, leaning forward: x in [(a L - w) / b, (a L + w) / b] with a = 0.5, b = 0.866, w = 7, L = 8.
            RoadChain forward = Chain(new[] { Mouth(1, 14f, (float)(Math.PI / 3), true) }, true, false);
            List<ChainSpan> spans = forward.Spans(8f, JunctionMode.Side);
            float lo = 200f + (0.5f * 8f - 7f) / 0.8660254f - 0.5f, hi = 200f + (0.5f * 8f + 7f) / 0.8660254f + 0.5f;
            Program.Check(spans.Count == 2 && Math.Abs(spans[0].To - lo) < 0.05f && Math.Abs(spans[1].From - hi) < 0.05f, $"leaning forward: open {lo:0.0} to {hi:0.0}: " + Show(spans));
            Program.Near(spans[1].From - spans[0].To, 14f / 0.8660254f + 1f, 0.06f, "its width is 2 w / sin + the margins");
            // Leaning back it moves the other way.
            List<ChainSpan> back = Chain(new[] { Mouth(1, 14f, (float)(Math.PI / 3), false) }, true, false).Spans(8f, JunctionMode.Side);
            Program.Check(back.Count == 2 && Math.Abs(back[0].To - (200f - (0.5f * 8f + 7f) / 0.8660254f - 0.5f)) < 0.05f, "leaning back: " + Show(back));
            // A wall further out is further along: at 12 m it opens 2 m later than at 8 m (a / b times 4).
            List<ChainSpan> out12 = forward.Spans(8f, JunctionMode.Side, false, s => 12f);
            Program.Check(out12.Count == 2 && Math.Abs(out12[0].To - spans[0].To - 4f * 0.5f / 0.8660254f) < 0.05f, "a wall that stands at 12 m is where the road's strip crosses 12 m: " + Show(out12));
        }

        private static void Fallbacks()
        {
            void Old(string why, RoadChain chain)
            {
                List<ChainSpan> spans = chain.Spans(8f, JunctionMode.Side);
                Program.Check(spans.Count == 2 && Math.Abs(spans[0].To - 170f) < 0.05f && Math.Abs(spans[1].From - 230f) < 0.05f, why + ": cut by the lanes: " + Show(spans));
            }

            Old("the mouths are not known (a roundabout, a road that could not be read)", Chain(new[] { Mouth(1, 14f) }, true, false, known: false));
            Old("a road that joins at 20 degrees", Chain(new[] { Mouth(1, 14f, (float)(Math.PI / 9)) }, true, false));
            Old("a thin road at 20 degrees (its opening, 16 to 28 m along, lies in the node: only the angle rules it out)", Chain(new[] { Mouth(1, 4f, (float)(Math.PI / 9)) }, true, false));
            Old("a road at 30 degrees whose mouth runs out of the node (-6 to 34 m along, the node ends at 30)", Chain(new[] { Mouth(1, 20f, (float)(Math.PI / 6)) }, true, false));
            Old("a road with no width", Chain(new[] { Mouth(1, 0.2f) }, true, false));
            Old("the chain turns 60 degrees at the node", Chain(new[] { Mouth(1, 14f) }, true, false, turn: (float)(Math.PI / 3)));
            Old("a road so wide that its mouth is out of the node", Chain(new[] { Mouth(1, 80f) }, true, false));
            Old("the junction is joined on the right and the only mouth known is on the left", Chain(new[] { Mouth(-1, 14f) }, true, false));
            Old("no mouth at all", Chain(new ChainMouth[0], true, false));
            // The turn that is allowed: 30 degrees.
            List<ChainSpan> bend = Chain(new[] { Mouth(1, 14f) }, true, false, turn: (float)(Math.PI / 6)).Spans(8f, JunctionMode.Side);
            Program.Check(bend.Count == 2 && bend[1].From - bend[0].To < 20f, "a bend of 30 degrees still opens at the mouth: " + Show(bend));
        }

        private static void Openings()
        {
            var openings = new List<ChainSpan>();
            // SideSpans alone does not show a merge (a wall cut twice in one place is cut once), so the openings are read here.
            RoadChain two = Chain(new[] { Mouth(1, 14f), Mouth(1, 14f, (float)(Math.PI / 2.2)) }, true, false);
            Program.Check(JunctionMouths.TryOpenings(two.Pieces[0], two.Pieces[1], 8f, 1, openings) && openings.Count == 1 && openings[0].To > openings[0].From, "two roads that open over each other give one opening, " + Show(openings));
            // A mouth that goes 0.3 m beyond the end of the lanes (inside the tolerance of 1 m) is shortened to the node, so its two ends are the node's.
            RoadChain wide = Chain(new[] { Mouth(1, 59.6f) }, true, false);
            Program.Check(JunctionMouths.TryOpenings(wide.Pieces[0], wide.Pieces[1], 8f, 1, openings) && openings.Count == 1 && Math.Abs(openings[0].From - 170f) < 0.001f && Math.Abs(openings[0].To - 230f) < 0.001f, "a mouth 0.3 m beyond the node is the node: " + Show(openings));
            // A refusal leaves nothing behind in the list, and a wall on the wrong side, or one on the centre line, is refused.
            RoadChain one = Chain(new[] { Mouth(1, 14f) }, true, false);
            Program.Check(!JunctionMouths.TryOpenings(one.Pieces[0], one.Pieces[1], 8f, -1, openings) && openings.Count == 0, "the wall on the side no road joins: refused, and the list is empty");
            Program.Check(!JunctionMouths.TryOpenings(one.Pieces[0], one.Pieces[1], 0f, 1, openings) && !JunctionMouths.TryOpenings(one.Pieces[0], one.Pieces[1], float.NaN, 1, openings), "a wall on the centre line or at NaN: refused");
            Program.Check(!JunctionMouths.TryOpenings(one.Pieces[0], null, 8f, 1, openings) && !JunctionMouths.TryOpenings(null, one.Pieces[1], 8f, 1, openings), "no piece: refused");
        }

        private static void Others()
        {
            RoadChain chain = Chain(new[] { Mouth(1, 14f) }, true, false);
            List<ChainSpan> marking = chain.Spans(8f, true);
            Program.Check(marking.Count == 2 && Math.Abs(marking[0].To - 170f) < 0.05f && Math.Abs(marking[1].From - 230f) < 0.05f, "an edge line (the markings): cut by the lanes as before: " + Show(marking));
            List<ChainSpan> cut = chain.Spans(8f, JunctionMode.Cut);
            Program.Check(cut.Count == 2 && Math.Abs(cut[0].To - 170f) < 0.05f && Math.Abs(cut[1].From - 230f) < 0.05f, "'cut at every junction': by the lanes: " + Show(cut));
            Program.Check(chain.Spans(8f, JunctionMode.Through).Count == 1, "'through': whole");
            List<ChainSpan> median = chain.Spans(0.5f, JunctionMode.Side, true);
            Program.Check(median.Count == 1, "a median wall (cut where both sides join): whole where one does: " + Show(median));
        }

        private static void AutoBarriers()
        {
            RoadChain chain = Chain(new[] { Mouth(1, 14f) }, true, false);
            List<BarrierRun> runs = BarrierAuto.Plan(chain, RoadClass.National, true, false);
            List<BarrierRun> onRight = runs.FindAll(r => r.EdgeSide > 0), onLeft = runs.FindAll(r => r.EdgeSide < 0);
            Program.Check(onRight.Count == 2 && onLeft.Count == 1, $"W-beams: two stretches on the side a road joins, one on the other: {onRight.Count}, {onLeft.Count}");
            if (onRight.Count == 2)
            {
                float opening = onRight[1].S0 - onRight[0].S1;
                Program.Check(opening > 14f && opening < 16f, $"the opening is the road's {opening:0.0} m, not the node's 60 m");
            }

            // A road at 60 degrees: where it opens depends on where the wall stands, and the auto barrier stands on the carriageway edge, not at 1 m (SideRuns passes the edge's lateral).
            RoadChain slanted = Chain(new[] { Mouth(1, 14f, (float)(Math.PI / 3), true) }, true, false);
            float edge = new EdgeProfile(slanted, 1).LateralAt(200f, 0f);
            List<BarrierRun> slantedRuns = BarrierAuto.Plan(slanted, RoadClass.National, true, false).FindAll(r => r.EdgeSide > 0);
            float expected = 200f + (0.5f * edge - 7f) / 0.8660254f - 0.5f;
            Program.Check(slantedRuns.Count == 2 && Math.Abs(slantedRuns[0].S1 - expected) < 0.1f, $"slanted: the wall ends at {(slantedRuns.Count > 0 ? slantedRuns[0].S1 : float.NaN):0.0}, where the strip crosses the edge at {edge:0.00} m ({expected:0.0}), not at 1 m ({200f + (0.5f - 7f) / 0.8660254f - 0.5f:0.0})");
        }
    }
}
