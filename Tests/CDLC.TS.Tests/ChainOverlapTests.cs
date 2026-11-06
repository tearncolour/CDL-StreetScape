using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.65 (the owner: "分析为什么有的地方放置会被切断" with the log of 1 Nov 18:49): the line of a noise wall was cut over 11.4 m at a plain node of two segments of a road 29 m wide, and over
    /// another in the same place on the other side. The log has the coordinates: the next piece started 9.4 m (6.7 m) BEFORE where the one before it ended, on its own centre line (0.03 m off it).
    /// The chain took that for a gap of more than a metre (MaxJoinGap) and broke there, and a break cuts every line, through-lines too. A piece that starts on the line of the one before it, heading
    /// the same way, at the same level, up to MaxOverlap back, is the same road drawn twice over the stretch: the chain goes on from there.
    /// </summary>
    internal static class ChainOverlapTests
    {
        public static void RunAll()
        {
            Program.Run("chain overlap: a piece that starts on the line of the one before it, behind its end, is joined from there", Joined);
            Program.Run("chain overlap: what is not the same road is still a break", StillBreaks);
            Program.Run("chain overlap: the edge of a road is not outside the road", EdgeWithinRoad);
            Program.Run("chain overlap: a plain node the reader went through is joined up to 4 m apart (0.12.66)", PlainNode);
            Program.Run("chain overlap: a plain node of a wide road is joined as far apart as the road is wide, straight on (0.12.97)", WideNode);
            Program.Run("chain overlap: a node whose pieces are a metre or two to the side of each other does not throw the line back (0.12.98)", JoggedNode);
        }

        /// <summary>The line of a wall along a chain at a lateral, at every metre; how far it ever goes back along heading (m), and its longest chord.</summary>
        private static void Walk(RoadChain chain, Func<float, float> lateral, Vec3 heading, out float back, out float longest, out List<Vec3> line)
        {
            line = chain.LineAdaptive(0f, chain.Length, lateral, 0f);
            back = 0f;
            longest = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                back = Math.Max(back, -Vec3.Dot(line[i] - line[i - 1], heading));
                longest = Math.Max(longest, Vec3.DistanceXZ(line[i - 1], line[i]));
            }
        }

        /// <summary>
        /// The owner's log of 5 Nov 19:42 (0.12.97): a noise wall along road 316290, two pieces at a plain node of two roads: #316291, 12 m wide, ends at (-4019.0, -1966.7); #1015829, 16 m wide, starts at
        /// (-4020.2, -1968.3): 2 m to the side of it and no distance along it (heading dot 0.999). The chain joins them (Pass, within the reach), and the right of the samples either side of the join was taken
        /// from the chord across it, 64 degrees off the road: at a lateral of 7.86 m the line was thrown 4.6 m back along the road and forward again, a hook in the placed wall (its path: turns of +150 and -175 degrees).
        /// </summary>
        private static void JoggedNode()
        {
            // The owner's two pieces, as straight lines (the real curves are nearly straight there), half widths 6 and 8.
            ChainPiece a = Piece(P(-4073.3f, -1926.8f), P(-4019.0f, -1966.7f), false, ChainJoint.Pass, 6f);
            a.ReaderJoint = ChainJoint.Pass;
            ChainPiece b = Piece(P(-4020.2f, -1968.3f), P(-3884.3f, -2076.6f), false, ChainJoint.End, 8f);
            var chain = new RoadChain(new[] { a, b });
            Program.Check(chain.Breaks.Count == 0 && a.After == ChainJoint.Pass, "the owner's node: no break, the joint is Pass: " + chain.Breaks.Count);
            Vec3 heading = (P(-4019.0f, -1966.7f) - P(-4073.3f, -1926.8f)).HeadingXZ();
            var edge = new EdgeProfile(chain, 1);
            Walk(chain, s => edge.LateralAt(s, 0.21f), heading, out float back, out float longest, out List<Vec3> line);
            Program.Check(line.Count > 100 && back < 0.05f, $"a wall at the road's edge there (7.9 m, then 5.9 m) never goes back along the road: {back:0.00} m back");
            Program.Check(longest < 4f, $"and has no step over 4 m (the hook was 4.6 m back and forward again): {longest:0.0} m");
            // Away from the node the line is where it was: the lateral 7.86 m right of the first piece's line far from the node.
            Vec3 right = heading.RightXZ();
            Vec3 first = line[3];
            Program.Check(Math.Abs(Vec3.Dot(first - P(-4073.3f, -1926.8f), right) - (edge.LateralAt(3f, 0.21f))) < 0.05f, "far from the node the wall is the lateral beside the road, as before");

            // The same with a plain 2 m side step, a lateral of 8 m and no road to follow.
            RoadChain Step(float side, float along)
            {
                ChainPiece c = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass, 6f);
                c.ReaderJoint = ChainJoint.Pass;
                return new RoadChain(new[] { c, Piece(P(side, 100f + along), P(side, 200f + along), false, ChainJoint.End, 6f) });
            }

            foreach ((float side, float along) in new[] { (2f, 0f), (-2f, 0f), (1.2f, 0.5f), (3f, 1f), (-3f, -0.5f), (1f, 2f) })
            {
                RoadChain jog = Step(side, along);
                Walk(jog, s => 8f, new Vec3(0f, 0f, 1f), out float jogBack, out float jogLongest, out _);
                Program.Check(jog.Breaks.Count == 0 && jogBack < 0.05f && jogLongest < 4.2f, $"a step of {side:+0.0;-0.0} m aside and {along:+0.0;-0.0} m along: no break, no step back ({jogBack:0.00} m), longest chord {jogLongest:0.0} m");
            }

            // The line crosses the node from one piece's lateral to the other's: 8 m right of the first (x = 8), 8 m right of the second (x = 10).
            RoadChain two = Step(2f, 0f);
            Vec3 before = two.PointAt(95f, 8f), after = two.PointAt(105f, 8f);
            Program.Check(Math.Abs(before.X - 8f) < 0.01f && Math.Abs(after.X - 10f) < 0.01f, $"it ends on each piece's own side of the road: {before.X:0.00} then {after.X:0.00}");
            Vec3 on = two.PointAt(100f, 8f);
            Program.Check(on.X > 7.99f && on.X < 10.01f && on.Z > 98f && on.Z < 102f, $"and at the node itself it is between them and on the road's length: ({on.X:0.00}, {on.Z:0.00})");

            // A join that is NOT skewed keeps the smooth right of 0.12.97 and before: the line at the last sample of the first piece is the sample plus the right of the chord from the sample before it to
            // the first sample of the next piece, to the centimetre. (a) The chord runs along the first piece and the next turns 20 degrees; (b) a small step of 0.15 m aside over 1.5 m along (6 degrees off
            // both pieces); (c) the same with the step to the other side.
            foreach ((string what, float side, float along, float turn) in new[] { ("(a) a gap of 4 m along the road and a turn of 20 degrees", 0f, 4f, 20f), ("(b) 0.15 m aside over 1.5 m along", 0.15f, 1.5f, 0f), ("(c) 0.15 m the other way", -0.15f, 1.5f, 0f), ("(d) 0.5 m aside over 1.5 m along (the chord to the next piece’s first sample, a metre on, is 11 degrees off both: under the limit of 15)", 0.5f, 1.5f, 0f) })
            {
                float rad = turn * (float)Math.PI / 180f;
                ChainPiece head = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass, 6f);
                head.ReaderJoint = ChainJoint.Pass;
                var next = Piece(P(side, 100f + along), P(side + 100f * (float)Math.Sin(rad), 100f + along + 100f * (float)Math.Cos(rad)), false, ChainJoint.End, 6f);
                var smooth = new RoadChain(new[] { head, next });
                Program.Check(smooth.Breaks.Count == 0, what + ": no break");
                // The first sample of the next piece is a metre along it (its start is not a sample when it joins).
                Vec3 firstOfNext = next.Curve.Point(next.Arc.TAt(1f));
                Vec3 centreHeading = (firstOfNext - P(0f, 99f)).HeadingXZ();
                Vec3 expected = P(0f, 100f) + centreHeading.RightXZ() * 8f;
                Vec3 got = smooth.PointAt(100f, 8f);
                Program.Check(Vec3.DistanceXZ(expected, got) < 0.01f, $"{what}: the line at the end of the first piece is on the smooth right, as it was ({got.X:0.000}, {got.Z:0.000} against {expected.X:0.000}, {expected.Z:0.000})");
            }

            // Over the limit (1 m aside over 1.5 m along: the chord is 22 degrees off both pieces) the join is skewed: the last sample of the first piece takes its own piece’s right, x = 8 exactly.
            ChainPiece headOver = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass, 6f);
            headOver.ReaderJoint = ChainJoint.Pass;
            var over = new RoadChain(new[] { headOver, Piece(P(1f, 101.5f), P(1f, 201.5f), false, ChainJoint.End, 6f) });
            Program.Check(over.Breaks.Count == 0 && Math.Abs(over.PointAt(100f, 8f).X - 8f) < 1e-3f && Math.Abs(over.PointAt(100f, 8f).Z - 100f) < 1e-3f, $"a step of 22 degrees is skewed: the line at the end of the first piece is its own right of it: ({over.PointAt(100f, 8f).X:0.000}, {over.PointAt(100f, 8f).Z:0.000})");

            // A last piece under a metre long has one sample only: nothing after it to take a direction from, and the chain is made without an exception and has finite points.
            ChainPiece headTiny = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass, 6f);
            headTiny.ReaderJoint = ChainJoint.Pass;
            RoadChain tiny = null;
            string thrown = "";
            try
            {
                tiny = new RoadChain(new[] { headTiny, Piece(P(2f, 100f), P(2f, 100.6f), false, ChainJoint.End, 6f) });
            }
            catch (Exception ex)
            {
                thrown = ex.GetType().Name;
            }

            Program.Check(thrown.Length == 0 && tiny != null && tiny.Length > 99f && tiny.PointAt(tiny.Length, 8f).IsFinite, "a last piece under a metre long (one sample) at a jog is made without an exception: " + thrown);
            if (tiny != null)
            {
                Walk(tiny, s => 8f, new Vec3(0f, 0f, 1f), out float tinyBack, out float tinyLongest, out _);
                Program.Check(tinyBack < 0.05f && tinyLongest < 4.2f, $"and its line does not hook there either: back {tinyBack:0.00} m, longest chord {tinyLongest:0.0} m");
            }

            // The same tiny piece with a break after it (the next piece is 40 m off): it takes the way the piece before it was heading, and nothing is made from the jump to the next.
            ChainPiece headBroken = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass, 6f);
            headBroken.ReaderJoint = ChainJoint.Pass;
            var cut = new RoadChain(new[] { headBroken, Piece(P(2f, 100f), P(2f, 100.6f), false, ChainJoint.End, 6f), Piece(P(40f, 300f), P(40f, 400f), false, ChainJoint.End, 6f) });
            Walk(cut, s => 8f, new Vec3(0f, 0f, 1f), out float cutBack, out _, out List<Vec3> cutLine);
            bool finite = cutLine.Count > 50;
            foreach (Vec3 q in cutLine)
                finite &= q.IsFinite;
            Program.Check(cut.Breaks.Count == 1 && finite && cutBack < 0.05f, $"a tiny piece before a break: one break, finite points, no step back ({cutBack:0.00} m)");

            // A piece of no length between two others (two samples on one point), on a road that runs east so that "north", the answer for no direction, is wrong: the line (8 m to its right, z = -8 and -6
            // beside the pieces at z = 0 and 2) stays beside it everywhere and is finite.
            ChainPiece headZero = Piece(P(0f, 0f), P(100f, 0f), false, ChainJoint.Pass, 6f);
            headZero.ReaderJoint = ChainJoint.Pass;
            ChainPiece zero = Piece(P(100f, 0f), P(100f, 0f), false, ChainJoint.Pass, 6f);
            zero.ReaderJoint = ChainJoint.Pass;
            var withZero = new RoadChain(new[] { headZero, zero, Piece(P(100f, 2f), P(200f, 2f), false, ChainJoint.End, 6f) });
            // It is a break (a piece with no length has no heading to go on with), as it always was.
            bool zeroOk = withZero.Breaks.Count == 1;
            List<Vec3> zeroLine = withZero.LineAdaptive(0f, withZero.Length, s => 8f, 0f);
            foreach (Vec3 q in zeroLine)
                zeroOk &= q.IsFinite && q.Z < -5.7f && q.Z > -8.3f;
            Program.Check(zeroOk && zeroLine.Count > 100, "a piece of no length in the chain: the line stays beside the road, finite, no point sent north");

            // What must not change: a straight road with no step (every point at x = 8), a bend of 20 degrees with no step, and the 4.2 m bridge of 0.12.97 with 0.3 m aside.
            RoadChain straight = Step(0f, 0f);
            Walk(straight, s => 8f, new Vec3(0f, 0f, 1f), out float sBack, out float sLongest, out List<Vec3> sLine);
            bool allOn = true;
            foreach (Vec3 p in sLine)
                allOn &= Math.Abs(p.X - 8f) < 1e-3f;
            Program.Check(allOn && sBack < 1e-3f && sLongest < 2.1f, "a straight road with no step: every point of the line at 8 m, as before");
            ChainPiece c1 = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass, 6f);
            c1.ReaderJoint = ChainJoint.Pass;
            float cos = (float)Math.Cos(20.0 * Math.PI / 180.0), sin = (float)Math.Sin(20.0 * Math.PI / 180.0);
            var bend = new RoadChain(new[] { c1, Piece(P(0f, 100f), P(100f * sin, 100f + 100f * cos), false, ChainJoint.End, 6f) });
            Walk(bend, s => 8f, new Vec3(0f, 0f, 1f), out float bBack, out float bLongest, out _);
            // The inside of a corner folds a little by itself (the right is the bisector of two chords of a metre): 0.41 m at a lateral of 8 m and 20 degrees, as it always did; the join is not skewed (its chord
            // runs along the next piece), and the fold is exactly what it was before 0.12.98.
            Program.Check(bend.Breaks.Count == 0 && Math.Abs(bBack - 0.41f) < 0.02f && bLongest < 2.3f, $"a bend of 20 degrees with no step keeps the smooth right, as it was: back {bBack:0.00} m (its own inside fold), longest chord {bLongest:0.0} m");
            RoadChain bridge = Step(0.3f, 4.2f);
            Walk(bridge, s => 8f, new Vec3(0f, 0f, 1f), out float gBack, out float gLongest, out _);
            Program.Check(bridge.Breaks.Count == 0 && gBack < 0.05f && gLongest < 6.5f, $"the 4.2 m gap of 0.12.97 stays one straight chord: back {gBack:0.00} m, longest {gLongest:0.0} m");
        }

        /// <summary>
        /// The owner's log of 5 Nov 18:41: two segments of a road 12 m wide at a plain node of two roads ended 4.2 m apart (heading dot 1.00, 0.3 m off the line, the reader found Pass), a wall of 1087 m was
        /// drawn in 3063.8 to 2902.5 m and 2890.5 to 1976.2 m, a hole of 12 m, and a save of his has runs that stop 4.1 and 2.0 m short of the next.
        /// </summary>
        private static void WideNode()
        {
            RoadChain Two(ChainJoint reader, float along, float side, float half = 6f, float nextHalf = 6f)
            {
                ChainPiece a = Piece(P(0f, 0f), P(0f, 100f), false, reader, half);
                a.ReaderJoint = reader;
                ChainPiece b = Piece(P(side, 100f + along), P(side, 200f + along), false, ChainJoint.End, nextHalf);
                return new RoadChain(new[] { a, b });
            }

            RoadChain owner = Two(ChainJoint.Pass, 4.2f, 0.3f);
            Program.Check(owner.Breaks.Count == 0 && owner.Pieces[0].After == ChainJoint.Pass, "the owner's node: 4.2 m apart on its own line, a road 12 m wide: no break, the joint is Pass: " + owner.Breaks.Count);
            List<ChainSpan> spans = owner.Spans(5.65f, JunctionMode.Side, true);
            Program.Check(spans.Count == 1 && spans[0].To - spans[0].From > 195f, "a wall along it is one span, not cut over the node: " + string.Join(" ", spans));
            List<Vec3> line = owner.Line(0f, owner.Length, 5.65f, 0f);
            float worstBack = 0f, longest = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                worstBack = Math.Max(worstBack, line[i - 1].Z - line[i].Z);
                longest = Math.Max(longest, Vec3.DistanceXZ(line[i - 1], line[i]));
            }

            Program.Check(line.Count > 2 && worstBack < 0.01f && longest < 7f, $"and its line goes straight across: no step back ({worstBack:0.00} m), the longest chord {longest:0.0} m");
            Program.Check(owner.Bridges.Count == 1 && Math.Abs(owner.Bridges[0].Gap - 4.2f) < 0.05f && Math.Abs(owner.Bridges[0].S - 100f) < 0.5f, "the chain says where it went across a node gap and how wide it was: " + owner.Bridges.Count);
            Program.Check(owner.Describe(0f, 200f).Contains("went across node gaps at") && owner.Describe(0f, 200f).Contains("4.2 m"), "and the log of the line has it: " + owner.Describe(0f, 200f));
            Program.Check(Two(ChainJoint.Pass, 3f, 0.5f).Bridges.Count == 0 && !Two(ChainJoint.Pass, 3f, 0.5f).Describe(0f, 200f).Contains("went across"), "a gap of the old reach is not a bridge and says nothing");
            Program.Check(Two(ChainJoint.Pass, 11f, 0.5f).Breaks.Count == 0, "11 m apart at a road 12 m wide, 0.5 m off the line: joined (the reach is the road's width)");
            Program.Check(Two(ChainJoint.Pass, 13f, 0.5f).Breaks.Count == 1, "13 m apart at a road 12 m wide: a break");
            Program.Check(Two(ChainJoint.Pass, 13f, 0.5f, 7f, 7f).Breaks.Count == 0, "13 m apart at a road 14 m wide: joined");
            Program.Check(Two(ChainJoint.Pass, 40f, 0f, 20f, 20f).Breaks.Count == 1, "40 m apart even at a road 40 m wide: a break (the cap is 30 m)");
            Program.Check(Two(ChainJoint.Pass, 29f, 0f, 20f, 20f).Breaks.Count == 0, "29 m apart at a road 40 m wide: joined");
            Program.Check(Two(ChainJoint.Pass, 8f, 2.5f).Breaks.Count == 1, "8 m apart and 2.5 m off the line: a jog, a break");
            Program.Check(Two(ChainJoint.Pass, 8f, 1.4f).Breaks.Count == 0, "8 m apart and 1.4 m off the line: joined");
            Program.Check(Two(ChainJoint.Junction, 8f, 0f).Breaks.Count == 1, "a junction with the same gap is a break as before");
            Program.Check(Two(ChainJoint.End, 8f, 0f).Breaks.Count == 1, "so is a joint the reader did not go through");
            Program.Check(Two(ChainJoint.Pass, -6f, 1.2f).Breaks.Count == 1, "6 m BEHIND the end and 1.2 m beside the line (too far off it for the overlap rule): a break, not a join that steps the line back");
            Program.Check(Two(ChainJoint.Pass, -6f, 0f).Breaks.Count == 0, "6 m behind on the line itself is the overlap of 0.12.65: joined as it was");
            Program.Check(Two(ChainJoint.Pass, 4f, 0.5f).Breaks.Count == 0 && Two(ChainJoint.Pass, 3f, 2f).Breaks.Count == 0, "what 0.12.66 joined is joined still, the lateral rule does not touch it");
            // A piece that starts 8 m ahead on the line but a level apart (a deck): a break.
            ChainPiece c = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass);
            c.ReaderJoint = ChainJoint.Pass;
            Program.Check(new RoadChain(new[] { c, Piece(P(0f, 108f, 6f), P(0f, 200f, 6f), false, ChainJoint.End) }).Breaks.Count == 1, "8 m ahead and 6 m higher: another level, a break");
            // The same node heading 30 degrees off: not the road going on.
            ChainPiece d = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass);
            d.ReaderJoint = ChainJoint.Pass;
            Program.Check(new RoadChain(new[] { d, Piece(P(2f, 108f), P(60f, 190f), false, ChainJoint.End) }).Breaks.Count == 1, "8 m ahead but a bend of 35 degrees: a break");
            // Starting 6 m BEHIND the end on the line is the overlap case (0.12.65), and starting 6 m ahead while heading back is no continuation.
            Program.Check(new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), Piece(P(0f, 106f), P(0f, 200f), true, ChainJoint.End) }).Breaks.Count == 1, "6 m ahead, the piece's curve running the other way and no reader join: a break as before");
        }

        private static ChainPiece Piece(Vec3 a, Vec3 d, bool reversed, ChainJoint after, float half = 14.5f)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(a, d), Reversed = reversed, Kmh = 80f, HasSpeed = true, After = after, HalfWidth = half };
            piece.Forward.Add(1.75f);
            piece.Backward.Add(-1.75f);
            return piece;
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static void Joined()
        {
            // The owner's first cut: 100 m north, then a piece that starts 9.4 m before the end, on the same line, and goes on 100 m (run reversed: its curve is the other way round, as 902153r was).
            foreach (bool reversed in new[] { false, true })
            {
                ChainPiece a = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass);
                ChainPiece b = reversed ? Piece(P(0f, 200f), P(0f, 90.6f), true, ChainJoint.End) : Piece(P(0f, 90.6f), P(0f, 200f), false, ChainJoint.End);
                var chain = new RoadChain(new[] { a, b });
                string how = reversed ? "reversed" : "forward";
                Program.Check(chain.Breaks.Count == 0 && a.After == ChainJoint.Pass, $"{how}: no break, the joint after the first piece is as it was ({a.After}), {chain.Breaks.Count} break(s)");
                Program.Near(b.Skip, 9.4f, 0.01f, $"{how}: the 9.4 m it overlaps are not used");
                Program.Near(chain.Length, 200f, 0.05f, $"{how}: the chain is the road's 200 m, not 209.4");
                Program.Near(b.S0, 100f, 0.05f, $"{how}: the second piece starts where the first ends");
                List<ChainSpan> spans = chain.Spans(14.05f, JunctionMode.Side);
                Program.Check(spans.Count == 1 && spans[0].To - spans[0].From > 195f, $"{how}: a line along it is one span: {string.Join(" ", spans)}");
                List<Vec3> line = chain.Line(0f, chain.Length, 14.05f, 0f);
                float worstBack = 0f, longest = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    worstBack = Math.Max(worstBack, line[i - 1].Z - line[i].Z);
                    longest = Math.Max(longest, Vec3.DistanceXZ(line[i - 1], line[i]));
                }

                Program.Check(worstBack < 0.01f && longest < 2.6f, $"{how}: the line does not step back at the join ({worstBack:0.00} m) and has no step over 2 m ({longest:0.0} m)");
                Program.Check(Math.Abs(chain.PointAt(100f, 0f).Z - 100f) < 0.05f && Math.Abs(chain.PointAt(150f, 0f).Z - 150f) < 0.05f, $"{how}: a distance of the chain is that far along the road, either side of the join");
                // The lanes of the second piece: its curve's 15 m are the chain's 5.6 m after the join.
                ChainPiece c = reversed ? Piece(P(0f, 200f), P(0f, 90.6f), true, ChainJoint.End) : Piece(P(0f, 90.6f), P(0f, 200f), false, ChainJoint.End);
                c.LaneFrom = reversed ? float.NaN : 15f;
                c.LaneTo = reversed ? 94f : float.NaN;
                var withLanes = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), c });
                Program.Check(c.LanesS0 >= c.S0 - 1e-3f && c.LanesS1 <= c.S1 + 1e-3f && c.LanesS0 <= c.LanesS1, $"{how}: the lanes of the piece are inside it ({c.LanesS0:0.0} to {c.LanesS1:0.0} of {c.S0:0.0} to {c.S1:0.0})");
                Program.Check(!reversed || Math.Abs(c.LanesS1 - 195.4f) < 0.1f || c.LanesS1 <= c.S1, $"{how}: lanes at the far end");
            }
        }

        private static void StillBreaks()
        {
            // 30 m to the side (the owner's V across the carriageways, 0.12.52).
            var side = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), Piece(P(30f, 100f), P(30f, 200f), false, ChainJoint.End) });
            Program.Check(side.Breaks.Count == 1, "a piece 30 m to the side breaks the chain");
            // Behind the end and on the same line, but 6 m beside it: the other carriageway, a different road.
            var parallel = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), Piece(P(6f, 90.6f), P(6f, 200f), false, ChainJoint.End) });
            Program.Check(parallel.Breaks.Count == 1, "a piece that starts 6 m beside the line is a break: " + parallel.Breaks.Count);
            // Starts on the line but goes back the way it came: a fold, not a continuation.
            var back = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), Piece(P(0f, 95f), P(0f, 0f), false, ChainJoint.End) });
            Program.Check(back.Breaks.Count == 1, "a piece that heads back the way the chain came is a break: " + back.Breaks.Count);
            // Starts 5 m beyond the end and runs back through it, the other way (the road on the other side of a node): its curve passes the end of the chain, but it does not go on from it.
            var through = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), Piece(P(0f, 105f), P(0f, 0f), false, ChainJoint.End) });
            Program.Check(through.Breaks.Count == 1, "a piece that crosses the end of the chain heading back is a break: " + through.Breaks.Count);
            // Starts 40 m before the end: more than any node's overlap.
            var far = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), Piece(P(0f, 60f), P(0f, 200f), false, ChainJoint.End) });
            Program.Check(far.Breaks.Count == 1, "a piece that starts 40 m back is a break: " + far.Breaks.Count);
            // On the line but another level (a deck over the road).
            var over = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), Piece(P(0f, 90.6f, 8f), P(0f, 200f, 8f), false, ChainJoint.End) });
            Program.Check(over.Breaks.Count == 1, "a piece 8 m above is a break: " + over.Breaks.Count);
            // And the join exactly where it ends is no overlap.
            var plain = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass), Piece(P(0f, 100f), P(0f, 200f), false, ChainJoint.End) });
            Program.Check(plain.Breaks.Count == 0 && plain.Pieces[1].Skip == 0f, "a piece that starts where the one before ends is as it was");
        }

        /// <summary>The owner's first line of the log of 1 Nov 21:14: two roads of 12 and 16 m, the second starting 2.1 m from the end of the first, 2.0 m off its line, and 54 m of lanes lost.</summary>
        private static void PlainNode()
        {
            RoadChain Two(ChainJoint reader, float side)
            {
                ChainPiece a = Piece(P(0f, 0f), P(0f, 100f), false, reader, 6f);
                a.ReaderJoint = reader;
                ChainPiece b = Piece(P(side, 100.6f), P(side, 200f), false, ChainJoint.End, 8f);
                return new RoadChain(new[] { a, b });
            }

            RoadChain plain = Two(ChainJoint.Pass, 2f);
            Program.Check(plain.Breaks.Count == 0 && plain.Pieces[0].After == ChainJoint.Pass, "2 m to the side at a plain node the reader went through: no break, the joint is Pass: " + plain.Breaks.Count);
            List<ChainSpan> spans = plain.Spans(6f, JunctionMode.Side);
            Program.Check(spans.Count == 1 && spans[0].To - spans[0].From > 190f, "and a line along it is one span: " + string.Join(" ", spans));
            Program.Check(Two(ChainJoint.Pass, 5f).Breaks.Count == 1, "5 m to the side is a break, even at a plain node");
            Program.Check(Two(ChainJoint.Junction, 2f).Breaks.Count == 1, "a junction with the same gap is a break as before");
            Program.Check(Two(ChainJoint.End, 2f).Breaks.Count == 1, "so is a joint the reader did not go through");
            RoadChain broken = Two(ChainJoint.Pass, 30f);
            Program.Check(broken.BreakNotes.Count == 1 && broken.BreakNotes[0].Contains("starts") && broken.BreakNotes[0].Contains("Pass"), "a break says why it is one: " + (broken.BreakNotes.Count > 0 ? broken.BreakNotes[0] : ""));
            Program.Check(broken.Describe(0f, 200f).Contains("breaks at") && broken.Describe(0f, 200f).Contains("heading dot"), "and the log of the line has it");
            // A bend of 60 degrees is not the road going on.
            ChainPiece c = Piece(P(0f, 0f), P(0f, 100f), false, ChainJoint.Pass);
            c.ReaderJoint = ChainJoint.Pass;
            ChainPiece d = Piece(P(3f, 100f), P(89f, 150f), false, ChainJoint.End);
            Program.Check(new RoadChain(new[] { c, d }).Breaks.Count == 1, "a bend of 60 degrees at a plain node is a break");
        }

        private static void EdgeWithinRoad()
        {
            // The owner's third line: a road 9 m wide whose second piece had a lane read at 3.56 m (lane 1.75 and 0.3 beyond: 5.61 m), while the road is 4.5 m to a side: the wall went to 5.67 m at its end.
            ChainPiece wide = Piece(P(0f, 0f), P(0f, 60f), false, ChainJoint.Pass, 4.5f);
            ChainPiece odd = Piece(P(0f, 60f), P(0f, 120f), false, ChainJoint.End, 4.5f);
            odd.Forward.Clear();
            odd.Forward.Add(3.56f);
            var chain = new RoadChain(new[] { wide, odd });
            float first = BarrierAuto.EdgeOf(wide, 1), second = BarrierAuto.EdgeOf(odd, 1);
            Program.Check(Math.Abs(first - 4.15f) < 0.01f, $"the first piece's edge is the road's less the inset: {first:0.00}");
            Program.Check(second <= 4.5f + 1e-3f, $"a lane read beyond the road's half width does not put the edge outside the road: {second:0.00} m (the road is 4.5)");
            // A road whose 4 m lanes fill it (the 0.12.16 case): the lanes' outer edge is the road's edge, and the wall is not over the lanes.
            ChainPiece motorway = Piece(P(0f, 0f), P(0f, 60f), false, ChainJoint.End, 8f);
            motorway.Forward.Clear();
            motorway.Forward.Add(6f);
            float edge = BarrierAuto.EdgeOf(motorway, 1);
            Program.Check(edge >= 7.5f && edge <= 8f + 1e-3f, $"lanes that fill the road keep the wall at its edge, not over them: {edge:0.00} m of 8");
            var profile = new EdgeProfile(chain, 1);
            float steepest = 0f;
            for (float s = 40f; s < 80f; s += 0.1f)
                steepest = Math.Max(steepest, Math.Abs(profile.At(s + 0.1f) - profile.At(s)) / 0.1f);
            Program.Check(profile.At(100f) <= 4.5f + 1e-3f && steepest < 0.2f, $"the profile along the two pieces stays in the road ({profile.At(100f):0.00} m) and changes slowly ({steepest * 100f:0} %)");
        }
    }
}
