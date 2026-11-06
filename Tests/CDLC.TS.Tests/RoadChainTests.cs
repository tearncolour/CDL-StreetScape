using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The markings of 0.12.2: a chain of road segments (RoadChain: lines across segments, cut at junctions) and the automatic marking of a stretch by GB 5768.3 (AutoMarking: lines joined through nodes,
    /// edge lines by 5.4.4 and 5.4.5, guide lane lines, arrows, stop and give-way lines at the junctions).
    /// </summary>
    internal static class RoadChainTests
    {
        public static void RunAll()
        {
            Program.Run("road chain: pieces, directions and distances", Basics);
            Program.Run("road chain: a bend at a node is smooth", Bend);
            Program.Run("road chain: where a line may lie (spans)", Spans);
            Program.Run("auto marking: the lines along a stretch", LinesAlong);
            Program.Run("auto marking: edge lines by 5.4.4 and 5.4.5", EdgeRules);
            Program.Run("auto marking: a signalled junction", SignalledJunction);
            Program.Run("auto marking: the other end, stop and yield, nothing controlling", OtherEnds);
            Program.Run("auto marking: short approaches and lanes of many ways", Limits);
            Program.Run("auto marking: the arrows and the settings", ArrowsAndSettings);
            Program.Run("auto marking: an oblique junction: the lines reach the slanted mouth and crosswalk (0.12.51)", Oblique);
            Program.Run("road chain: no line across a gap between pieces, a click on the road at its level (0.12.52)", Gaps);
            Program.Run("auto marking: the stop line where the game draws none, stepped at a slanted crosswalk (0.12.54)", StopLines);
            Program.Run("road chain: the edge changes over a few metres at a node with a small gap in its lanes (0.12.60)", EdgeAtNode);
        }

        /// <summary>
        /// 0.12.60, the owner's barrier over a ramp's joint: two segments of half widths 4.5 and 4.0 whose lanes leave 1 m between them (the second's start 0.5 m in) put the edge 0.5 m nearer in 1 m, and the
        /// barrier made that step in one chord (14 degrees). Where the gap is under 6 m the change is spread over 6 m; a junction's wide gap is as it was, over the gap.
        /// </summary>
        private static void EdgeAtNode()
        {
            var a = Piece(P(0f, 0f), P(0f, 50f), false, One, OneBack, after: ChainJoint.Pass, laneTo: 49.5f);
            a.HalfWidth = 4.5f;
            var b = Piece(P(0f, 50f), P(0f, 100f), false, One, OneBack, laneFrom: 0.5f);
            b.HalfWidth = 4f;
            var chain = new RoadChain(new[] { a, b });
            var profile = new EdgeProfile(chain, 1);
            float before = profile.At(30f), after = profile.At(70f);
            Program.Check(Math.Abs(before - BarrierAuto.EdgeOf(a, 1)) < 1e-4f && Math.Abs(after - BarrierAuto.EdgeOf(b, 1)) < 1e-4f && before - after > 0.4f, $"away from the node each piece has its own edge ({before:0.00} m, {after:0.00} m)");
            float steepest = 0f;
            for (float s = 40f; s < 60f; s += 0.1f)
                steepest = Math.Max(steepest, Math.Abs(profile.At(s + 0.1f) - profile.At(s)) / 0.1f);
            Program.Check(steepest < 0.2f, $"the edge moves at most {steepest * 100f:0} % across the node (a step in 1 m is 75 %)");

            // A junction 12 m wide between two pieces: the change is over the junction, as it was.
            var c = Piece(P(0f, 0f), P(0f, 50f), false, One, OneBack, after: ChainJoint.Pass, laneTo: 44f);
            c.HalfWidth = 4.5f;
            var d = Piece(P(0f, 50f), P(0f, 100f), false, One, OneBack, laneFrom: 6f);
            d.HalfWidth = 4f;
            var wide = new EdgeProfile(new RoadChain(new[] { c, d }), 1);
            Program.Check(Math.Abs(wide.At(44f) - BarrierAuto.EdgeOf(c, 1)) < 1e-4f && Math.Abs(wide.At(56f) - BarrierAuto.EdgeOf(d, 1)) < 1e-4f && Math.Abs(wide.At(50f) - (wide.At(44f) + wide.At(56f)) * 0.5f) < 1e-3f,
                $"over a junction's 12 m the change is over its 12 m: {wide.At(44f):0.00}, {wide.At(50f):0.00}, {wide.At(56f):0.00} m");
        }

        /// <summary>
        /// 0.12.52, the owner's picture of a noise barrier drawn as a V across the carriageways: a piece that does not start where the one before it ends breaks the chain (no span, line or
        /// projection across the gap, the right at each side its own piece's); a place is projected on the road at its level where the chain passes over itself, and on the piece clicked; a line
        /// that follows the edge from well inside it stays on its side where the road narrows.
        /// </summary>
        private static void Gaps()
        {
            var whole = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 50f), false, One, OneBack, after: ChainJoint.Pass), Piece(P(0f, 50f), P(0f, 100f), false, One, OneBack) });
            Program.Check(whole.Breaks.Count == 0 && whole.Pieces[0].After == ChainJoint.Pass, "pieces that meet are one chain");

            // The second piece starts 30 m to the side of the first's end (a segment joined to the node along its middle, taken as the next one).
            var a = Piece(P(0f, 0f), P(0f, 50f), false, One, OneBack, after: ChainJoint.Pass);
            var b = Piece(P(30f, 50f), P(30f, 100f), false, One, OneBack);
            var chain = new RoadChain(new[] { a, b });
            Program.Check(chain.Breaks.Count == 1 && Math.Abs(chain.Breaks[0] - 50f) < 1e-3f && a.After == ChainJoint.End, "broken at 50 m, the first piece ends the road there: " + string.Join(",", chain.Breaks));
            Program.Near(chain.Length, 100f + RoadChain.BreakLength, 1e-3f, "the gap takes BreakLength of the chain");
            string described = chain.Describe(10f, 90f);
            Program.Check(described.Contains("breaks at 50.0") && described.Contains("(30.0, 0.0, 50.0) to (30.0, 0.0, 100.0)"), "the log names the break and where each piece runs: " + described);
            List<ChainSpan> spans = chain.Spans(-1.75f, false);
            Program.Check(spans.Count == 2 && spans[0].To <= 50f + 1e-3f && spans[1].From >= b.S0 - 1e-3f, "a line's spans stop at the gap: " + string.Join(" ", spans.ConvertAll(sp => $"{sp.From:0.0}-{sp.To:0.0}")));
            float longest = 0f;
            foreach (ChainSpan part in RoadChain.Clip(spans, 10f, 90f))
            {
                List<Vec3> line = chain.Line(part.From, part.To, -1.75f, 0f);
                for (int i = 1; i < line.Count; i++)
                    longest = Math.Max(longest, Vec3.DistanceXZ(line[i - 1], line[i]));
            }

            Program.Check(longest <= MarkingRuns.Step * 1.25f + 0.01f, "the line drawn over it has no step longer than its own: " + longest);
            Vec3 right = chain.RightAt(49.9f), after = chain.RightAt(b.S0 + 0.1f);
            Program.Check(right.X > 0.99f && after.X > 0.99f, $"each side of the gap the right is its piece's: {right}, {after}");
            chain.Project(P(15f, 50f), out float l);
            Program.Check(Math.Abs(l) > 10f, "a place in the gap is not on the chain (it would have been on the line joining the pieces): " + l);

            // A road that comes back over itself 8 m up: from (0, 0, 0) north 100 m, east 50 m, back west across the first piece at z = 50 and on.
            var low = Piece(P(0f, 0f), P(0f, 100f), false, One, OneBack, after: ChainJoint.Pass);
            var turn = Piece(P(0f, 100f), new Vec3(50f, 8f, 100f), false, One, OneBack, after: ChainJoint.Pass);
            var down = Piece(new Vec3(50f, 8f, 100f), new Vec3(50f, 8f, 50f), false, One, OneBack, after: ChainJoint.Pass);
            var over = Piece(new Vec3(50f, 8f, 50f), new Vec3(-50f, 8f, 50f), false, One, OneBack);
            var loop = new RoadChain(new[] { low, turn, down, over });
            var cursor = new Vec3(0.6f, 0f, 50f);
            float flat = loop.Project(cursor, out _), level = loop.ProjectLevel(cursor, out float levelLateral);
            Program.Check(flat > over.S0, $"in the ground plane the place is on the road above it ({flat:0.0} m)");
            Program.Check(Math.Abs(level - 50f) < 0.01f && Math.Abs(levelLateral - 0.6f) < 0.01f, $"at its level, on the road it is on: {level:0.00} m, {levelLateral:+0.00;-0.00} m");
            Program.Near(loop.ProjectLevel(new Vec3(0.6f, 8f, 50f), out _), over.S0 + 49.4f, 0.01f, "a place up there is on the road up there (0.6 m short of its middle)");
            Program.Near(loop.ProjectLevel(new Vec3(0.6f, 8f, 50f), out _, low.S0, low.S1), 50f, 0.01f, "on the piece clicked when it is given");
            Program.Near(loop.ProjectLevel(cursor, out _, 1e4f, 2e4f), 50f, 0.01f, "a range with none of the chain in it: all of it");

            // A barrier put 7 m inside the edge of a road 16 m wide follows the edge onto a piece 4 m wide: it stays left of the centre line.
            var wide = Piece(P(0f, 0f), P(0f, 50f), false, Two, TwoBack, after: ChainJoint.Pass);
            wide.HalfWidth = 8f;
            var narrow = Piece(P(0f, 50f), P(0f, 100f), false, One, new float[0]);
            narrow.HalfWidth = 2f;
            var narrowing = new RoadChain(new[] { wide, narrow });
            var profile = new EdgeProfile(narrowing, -1);
            float offset = 0.65f - profile.At(10f);
            Program.Check(Math.Abs(profile.LateralAt(10f, offset) + 0.65f) < 1e-3f, "where it was put, as put: " + profile.LateralAt(10f, offset));
            Program.Check(profile.LateralAt(90f, offset) <= -EdgeProfile.MinFromCentre + 1e-4f, $"on the narrow piece it keeps to its side ({profile.LateralAt(90f, offset):0.00} m, the edge {-profile.At(90f):0.00} m; {-(profile.At(90f) + offset):+0.00;-0.00} m before 0.12.52, across it)");
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        /// <summary>A straight piece from a to b, lanes with its direction at the forward laterals and against it at the backward.</summary>
        private static ChainPiece Piece(Vec3 a, Vec3 b, bool reversed, float[] forward, float[] backward, float kmh = 50f, ChainJoint after = ChainJoint.End, float laneFrom = float.NaN, float laneTo = float.NaN)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(a, b), Reversed = reversed, Kmh = kmh, HasSpeed = true, After = after, LaneFrom = laneFrom, LaneTo = laneTo };
            piece.Forward.AddRange(forward);
            piece.Backward.AddRange(backward);
            return piece;
        }

        private static readonly float[] Two = { 1.75f, 5.25f }, TwoBack = { -1.75f, -5.25f }, One = { 1.75f }, OneBack = { -1.75f };

        private static void Basics()
        {
            // The second piece runs the other way (its own start is the chain's far end): its lanes with its direction are the chain's against.
            var a = Piece(P(0f, 0f), P(0f, 50f), false, One, OneBack, after: ChainJoint.Pass, laneFrom: 2f, laneTo: 50f);
            var b = Piece(P(0f, 100f), P(0f, 50f), true, new[] { 1.75f, 5.25f }, new[] { -1.75f }, laneFrom: 3f, laneTo: 50f);
            var chain = new RoadChain(new[] { a, b });
            Program.Near(chain.Length, 100f, 1e-3f, "two pieces of 50 m");
            Program.Near(a.S0, 0f, 1e-4f, "the first starts at 0");
            Program.Near(b.S0, 50f, 1e-3f, "the second at 50");
            Program.Near(a.LanesS0, 2f, 1e-3f, "the first's lanes start 2 m in");
            // The second's own lanes run from 3 m (its start, the chain's far end) to its end: in the chain from 50 to 97.
            Program.Near(b.LanesS0, 50f, 1e-3f, "a reversed piece's lanes in the chain: from its own end");
            Program.Near(b.LanesS1, 97f, 1e-3f, "to 3 m before the chain's end");
            Program.Check(b.With.Count == 1 && Math.Abs(b.With[0] - 1.75f) < 1e-5f, "the reversed piece's lanes against it are the chain's with it, on the right: " + string.Join(",", b.With));
            Program.Check(b.Against.Count == 2 && b.Against.Contains(-1.75f) && b.Against.Contains(-5.25f), "and its own lanes are the chain's against it, on the left");
            Vec3 p = chain.PointAt(75f, 1.75f);
            Program.Check(Math.Abs(p.X - 1.75f) < 1e-3f && Math.Abs(p.Z - 75f) < 1e-3f, "a point 1.75 m right of the chain at 75 m: " + p);
            float s = chain.Project(P(2f, 30f), out float lateral);
            Program.Near(s, 30f, 1e-3f, "a place projects to its distance");
            Program.Near(lateral, 2f, 1e-3f, "and its lateral");
            Program.Near(chain.Project(P(-1f, 200f), out _), 100f, 1e-3f, "beyond the end: the end");
            Program.Check(chain.PieceAt(10f) == 0 && chain.PieceAt(60f) == 1 && chain.PieceAt(100f) == 1, "the piece a distance is on");
            Vec3 h = chain.HeadingAt(80f);
            Program.Check(Math.Abs(h.Z - 1f) < 1e-4f, "the heading is the chain's, not the reversed piece's own");
            List<Vec3> line = chain.Line(10f, 90f, -1.75f, 0.2f);
            Program.Check(line.Count >= 40 && Math.Abs(line[0].Z - 10f) < 1e-3f && Math.Abs(line[line.Count - 1].Z - 90f) < 1e-3f && Math.Abs(line[0].Y - 0.2f) < 1e-4f, "a line along it, over the node, raised");
            Program.Check(line.TrueForAll(q => Math.Abs(q.X + 1.75f) < 1e-3f), "at its lateral all the way");
            List<Vec3> back = chain.Line(90f, 10f, 0f, 0f);
            Program.Check(back[0].Z > back[back.Count - 1].Z, "drawn backwards when asked");
            Program.Check(chain.Line(10f, 10.5f, 0f, 0f).Count == 0, "too short: nothing");
        }

        private static void Bend()
        {
            // Two pieces at 30 degrees: a line 3 m right of the chain has no jump at the node, and keeps 3 m from the centre line.
            var a = Piece(P(0f, 0f), P(0f, 40f), false, One, OneBack, after: ChainJoint.Pass);
            float angle = 30f * (float)Math.PI / 180f;
            var b = Piece(P(0f, 40f), P(40f * (float)Math.Sin(angle), 40f + 40f * (float)Math.Cos(angle)), false, One, OneBack);
            var chain = new RoadChain(new[] { a, b });
            List<Vec3> line = chain.Line(20f, 60f, 3f, 0f);
            float worst = 0f;
            for (int i = 1; i < line.Count; i++)
                worst = Math.Max(worst, Vec3.DistanceXZ(line[i - 1], line[i]));
            Program.Check(worst < MarkingRuns.Step + 0.3f, "no jump at the bend: the longest step " + worst);
            foreach (Vec3 q in line)
            {
                chain.Project(q, out float lateral);
                Program.Check(Math.Abs(lateral - 3f) < 0.2f, "3 m right of the centre line: " + lateral);
            }
        }

        private static void Spans()
        {
            // Three pieces: a node it passes through, then a junction; lanes 2 m short of each junction.
            var a = Piece(P(0f, 0f), P(0f, 40f), false, One, OneBack, after: ChainJoint.Pass, laneFrom: 0f, laneTo: 40f);
            var b = Piece(P(0f, 40f), P(0f, 80f), false, One, OneBack, after: ChainJoint.Junction, laneFrom: 0f, laneTo: 36f);
            var c = Piece(P(0f, 80f), P(0f, 120f), false, One, OneBack, laneFrom: 4f, laneTo: 40f);
            b.JoinLeft = true;
            var chain = new RoadChain(new[] { a, b, c });
            List<ChainSpan> lane = chain.Spans(0f, false);
            Program.Check(lane.Count == 2, "a line is cut at the junction: " + string.Join(" ", lane));
            Program.Near(lane[0].From, 0f, 1e-3f, "from the start");
            Program.Near(lane[0].To, 76f, 1e-3f, "to the lanes' end before the junction, through the node");
            Program.Near(lane[1].From, 84f, 1e-3f, "again from the lanes' start after it");
            Program.Near(lane[1].To, 120f, 1e-3f, "to the end");
            Program.Check(chain.Spans(3.5f, true).Count == 1, "an edge line on the right, where no road joins, goes on through");
            Program.Check(chain.Spans(-3.5f, true).Count == 2, "on the left, where one does, it is cut");
            Program.Near(RoadChain.Snap(lane, 77f, 1.5f), 76f, 1e-4f, "a place in the junction: the nearest end");
            Program.Near(RoadChain.Snap(lane, 75f, 1.5f), 76f, 1e-4f, "near an end: the end");
            Program.Near(RoadChain.Snap(lane, 50f, 1.5f), 50f, 1e-4f, "elsewhere: itself");
            Program.Check(float.IsNaN(RoadChain.Snap(new List<ChainSpan>(), 3f, 1f)), "no span: none");
            List<ChainSpan> parts = RoadChain.Clip(lane, 10f, 110f);
            Program.Check(parts.Count == 2 && Math.Abs(parts[0].From - 10f) < 1e-4f && Math.Abs(parts[0].To - 76f) < 1e-4f && Math.Abs(parts[1].To - 110f) < 1e-4f, "a line from 10 to 110: two parts, the junction left out");
            List<ChainSpan> backParts = RoadChain.Clip(lane, 110f, 10f);
            Program.Check(backParts.Count == 2 && backParts[0].From > backParts[0].To && backParts[0].From > backParts[1].From, "drawn back: the parts in its order, each backwards");
            Program.Check(RoadChain.Clip(lane, 77f, 83f).Count == 0, "inside the junction: nothing");
        }

        private static AutoOptions Opt(bool cross = false) { AutoOptions o = AutoOptions.Default; o.CrossLines = cross; return o; }

        /// <summary>
        /// A road of two lanes each way, 8 m either side of its curve, 100 m, meeting a junction at its end at a slant: its mouth at 96 m on its left edge and 102 m on its right (the piece 104 m long);
        /// the game's crosswalk along the slant, its near side 0.5 m past the mouth all across. reversed: the same road drawn the other way (the chain from the junction).
        /// </summary>
        private static (RoadChain, ChainApproach) Slanted(bool reversed)
        {
            ChainPiece piece = reversed ? Piece(P(0f, 104f), P(0f, 0f), true, Two, TwoBack, 50f, ChainJoint.End, 8f, 104f) : Piece(P(0f, 0f), P(0f, 104f), false, Two, TwoBack, 50f, ChainJoint.End, 0f, 96f);
            piece.HalfWidth = 8f;
            if (reversed)
            {
                // Its own start is the junction: its left there is the chain's right.
                piece.StartLeft = 2f;
                piece.StartRight = 8f;
                piece.EndLeft = piece.EndRight = 104f;
            }
            else
            {
                piece.StartLeft = piece.StartRight = 0f;
                piece.EndLeft = 96f;
                piece.EndRight = 102f;
            }

            var chain = new RoadChain(new[] { piece });
            var approach = new ChainApproach { StopS = chain.Pieces[0].LanesS1, WithChain = true, Control = JunctionControl.Signals };
            approach.Lanes.Add(new ApproachLane { Lateral = 1.75f, Straight = true, Left = true });
            approach.Lanes.Add(new ApproachLane { Lateral = 5.25f, Straight = true, Right = true });
            // The near side 0.5 m past the slanted mouth: 0.5 at the left edge, 6.5 at the right (the mouth's slope, 6 m over 16 m).
            for (int k = 0; k <= 8; k++)
            {
                float x = -8f + 2f * k;
                approach.CrosswalkEdge.Add((x, 0.5f + 6f * (x + 8f) / 16f));
            }

            approach.CrosswalkAt = 0.5f;
            return (chain, approach);
        }

        private static void Oblique()
        {
            (RoadChain chain, ChainApproach approach) = Slanted(false);
            ChainPiece piece = chain.Pieces[0];
            Program.Near(piece.LanesS1, 96f, 1e-3f, "the lanes' common end: the nearer corner");
            Program.Near(piece.LanesS1At(-8f), 96f, 1e-3f, "the mouth at the left edge");
            Program.Near(piece.LanesS1At(8f), 102f, 1e-3f, "at the right edge");
            Program.Near(piece.LanesS1At(4f), 100.5f, 1e-3f, "and between them on the straight slant");
            Program.Near(piece.LanesS1At(20f), 102f, 1e-3f, "held at a corner beyond it");
            Program.Near(piece.LanesS0At(3f), 0f, 1e-3f, "the square start as it was");
            List<ChainSpan> spans = chain.Spans(5.25f, false);
            Program.Check(spans.Count == 1 && Math.Abs(spans[0].To - piece.LanesS1At(5.25f)) < 1e-3f, "a line's span reaches the mouth at its lateral: " + string.Join(";", spans));
            // The segment drawn from the junction (its own start is the chain's end): the same mouth.
            (RoadChain back, _) = Slanted(true);
            ChainPiece flipped = back.Pieces[0];
            Program.Near(flipped.LanesS1, 104f - 8f, 1e-3f, "reversed: the lanes end at the nearer corner along the chain (104 - 8)");
            Program.Near(flipped.LanesS1At(8f), 104f - 2f, 1e-3f, "the chain's right is the segment's left there: 2 m from its start");
            Program.Near(flipped.LanesS1At(-8f), 104f - 8f, 1e-3f, "and its left the segment's right");
            Program.Near(flipped.LanesS0At(0f), 0f, 1e-3f, "its square end at the chain's start");

            // Marked with the signals' stop line: the approach's lines at the stop line (square, 1.5 m before the crosswalk's nearest point over each lane: stepped, 0.12.54); the other way's lines and the
            // centre line where the crosswalk crosses each, 0.5 m before it.
            AutoPlan plan = AutoMarking.Plan(chain, new[] { approach }, Opt(true));
            AutoStamp stop = plan.Stamps.Find(s => s.Id == "mk.stop" && s.Lateral < 3.5f);
            float nearestOverLanes = 0.5f + 6f * 8f / 16f;
            Program.Near(stop.S, 96f + nearestOverLanes - AutoMarking.StopBeforeCrosswalk - AutoMarking.StopWidth * 0.5f, 1e-3f, "the stop line's first lane before the crosswalk's nearest point over it (6.4.4), square to them");
            float stopEdge = stop.S - AutoMarking.StopWidth * 0.5f;
            AutoStamp outer = plan.Stamps.Find(s => s.Id == "mk.stop" && s.Lateral > 3.5f);
            AutoRun edge = Runs(plan, "mk.line.edge").Find(r => r.Lateral > 0f);
            Program.Check(edge == null || Math.Abs(edge.S1 - (outer.S - AutoMarking.StopWidth * 0.5f)) < 1e-3f, "the approach's edge line at its lane's piece of the stop line");
            AutoRun departing = Runs(plan, "mk.line.lane").Find(r => r.Lateral < 0f);
            float at = departing.LateralAt(departing.S1);
            Program.Near(departing.S1, 96f + Math.Min(piece.LanesS1At(at) - 96f, approach.CrosswalkNearAt(at) - AutoMarking.CrosswalkClear), 1e-3f, $"the other way's lane line reaches the slanted crosswalk at its lateral ({departing.S1:0.00} m)");
            Program.Check(departing.S1 > 96f + 1.5f, "past the nearer corner, which the lines stopped at before: " + departing.S1);

            // The game's crossing lines kept: the guide lines still end square where the stop line would be.
            AutoPlan without = AutoMarking.Plan(chain, new[] { approach }, Opt(false));
            AutoRun ownGuide = Runs(without, "mk.line.guidelane").Find(r => Math.Abs(r.Lateral - 3.5f) < 1e-3f);
            Program.Check(!without.Stamps.Exists(s => s.Id == "mk.stop") && ownGuide != null && Math.Abs(ownGuide.S1 - stopEdge) < 1e-3f, "the game's crossing lines kept: the guide lines end where the stop line would be");
            // Without a stop line by the standard (nothing controls the approach): every line, the guide lines too, reaches the crosswalk where it crosses it.
            approach.Control = JunctionControl.None;
            AutoPlan open = AutoMarking.Plan(chain, new[] { approach }, Opt(true));
            Program.Check(!open.Stamps.Exists(s => s.Id == "mk.stop"), "no stop line where nothing controls the approach");
            foreach (AutoRun run in open.Runs)
            {
                if (run.S1 < 90f)
                    continue;
                float x = run.LateralAt(run.S1);
                float want = 96f + Math.Min(piece.LanesS1At(x) - 96f, approach.CrosswalkNearAt(x) - AutoMarking.CrosswalkClear);
                Program.Check(Math.Abs(run.S1 - want) < 0.02f, $"{run.Id} at {x:+0.00;-0.00} m ends 0.5 m before the slanted crosswalk: {run.S1:0.00} of {want:0.00}");
            }

            AutoRun guide = Runs(open, "mk.line.guidelane").Find(r => Math.Abs(r.Lateral - 3.5f) < 1e-3f);
            Program.Check(guide != null && Math.Abs(guide.S1 - (96f + 0.5f + 6f * 11.5f / 16f - AutoMarking.CrosswalkClear)) < 0.02f, "the guide lane line too, at its own lateral: " + (guide != null ? guide.S1.ToString("0.00") : "none"));
            Program.Near(approach.CrosswalkNearest(1f, 7f), 0.5f + 6f * 9f / 16f, 1e-4f, "the crosswalk's nearest between two laterals");
            var none = new ChainApproach { CrosswalkAt = 2f };
            Program.Check(none.CrosswalkNearAt(5f) == 2f && none.CrosswalkNearest(0f, 1f) == 2f, "a crosswalk not sampled across: its nearest everywhere");
        }

        private static List<AutoRun> Runs(AutoPlan plan, string id) => plan.Runs.FindAll(r => r.Id == id);

        /// <summary>
        /// 0.12.54, the owner's picture of a signalled approach with no stop line ("停止线怎么没了"): the game's North American style draws none, and the game's lines across were kept. The standard's stop
        /// line is drawn where the game has none (ChainApproach.GameCrossLine); at a slanted crosswalk it steps lane by lane, each lane's piece 1.5 m before the crosswalk there (GB 51038 13.5.4).
        /// </summary>
        private static void StopLines()
        {
            (RoadChain chain, ChainApproach approach) = Slanted(false);
            approach.GameCrossLine = true;
            Program.Check(!AutoMarking.Plan(chain, new[] { approach }, Opt(false)).Stamps.Exists(s => s.Id == "mk.stop"), "the game's lines across kept, and the game has one: none of ours");
            approach.GameCrossLine = false;
            AutoPlan plan = AutoMarking.Plan(chain, new[] { approach }, Opt(false));
            List<AutoStamp> stops = plan.Stamps.FindAll(s => s.Id == "mk.stop");
            Program.Check(stops.Count == 2, "the game draws none here: the standard's, a piece for each of the two lanes: " + stops.Count);
            stops.Sort((a, b) => a.Lateral.CompareTo(b.Lateral));
            float half = AutoMarking.StopWidth * 0.5f;
            for (int k = 0; k < stops.Count; k++)
            {
                AutoStamp s = stops[k];
                float a = s.Lateral - s.Span * 0.5f, b = s.Lateral + s.Span * 0.5f;
                Program.Near(s.S, 96f + approach.CrosswalkNearest(a, b) - AutoMarking.StopBeforeCrosswalk - half, 1e-3f, $"lane {k + 1}'s piece 1.5 m before the crosswalk over it ({a:0.0} to {b:0.0} m)");
                Program.Check(s.Lanes == 1 && s.WithChain, $"lane {k + 1}'s piece is one lane's, square to the lanes");
            }

            Program.Near(stops[0].Span + stops[1].Span, 7f, 1e-3f, "together from the centre line over both lanes");
            Program.Check(stops[1].S - stops[0].S > AutoMarking.StopStep, "the outer lane's piece nearer the junction, where the crosswalk is: " + (stops[1].S - stops[0].S).ToString("0.00"));
            AutoRun guide = Runs(plan, "mk.line.guidelane").Find(r => Math.Abs(r.Lateral - 3.5f) < 1e-3f);
            Program.Near(guide.S1, stops[0].S - half, 1e-3f, "the guide line between them ends at the piece farther back");
            foreach (AutoStamp arrow in plan.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow", StringComparison.Ordinal) && s.S > 80f))
            {
                AutoStamp own = arrow.Lateral < 3.5f ? stops[0] : stops[1];
                Program.Near(arrow.S, own.S - half - AutoMarking.ArrowBeforeStop - AutoMarking.ArrowLength(chain.Pieces[0], Opt(false)), 1e-3f, $"the first arrow at {arrow.Lateral:0.00} m before its own lane's piece ({own.S:0.0})");
            }

            // A square crosswalk: one line, as before.
            var square = new ChainApproach { StopS = approach.StopS, WithChain = true, Control = JunctionControl.Signals, CrosswalkAt = 1f, GameCrossLine = false };
            square.Lanes.AddRange(approach.Lanes);
            List<AutoStamp> one = AutoMarking.Plan(chain, new[] { square }, Opt(false)).Stamps.FindAll(s => s.Id == "mk.stop");
            Program.Check(one.Count == 1 && one[0].Lanes == 2, "a square crosswalk: one stop line over both lanes");
            // Nothing controls it: none, whatever the game draws.
            approach.Control = JunctionControl.None;
            Program.Check(!AutoMarking.Plan(chain, new[] { approach }, Opt(false)).Stamps.Exists(s => s.Id == "mk.stop"), "no stop line where nothing controls the approach");
        }

        private static void LinesAlong()
        {
            // A road of two lanes each way, 50 km/h, in two pieces with a node between: one line of each kind from end to end.
            var a = Piece(P(0f, 0f), P(0f, 60f), false, Two, TwoBack, 50f, ChainJoint.Pass, 0f, 60f);
            var b = Piece(P(0f, 60f), P(0f, 120f), false, Two, TwoBack, 50f, ChainJoint.End, 0f, 58f);
            var chain = new RoadChain(new[] { a, b });
            AutoPlan plan = AutoMarking.Plan(chain, null, Opt());
            List<AutoRun> centre = Runs(plan, "mk.line.doubleyellow");
            Program.Check(centre.Count == 1 && Math.Abs(centre[0].Lateral) < 1e-3f, "a double yellow line in the middle (6.1.2: two lanes each way)");
            Program.Near(centre[0].S0, 0f, 1e-3f, "from the start");
            Program.Near(centre[0].S1, 118f, 1e-3f, "through the node to the lanes' end");
            List<AutoRun> lanes = Runs(plan, "mk.line.lane");
            Program.Check(lanes.Count == 2 && lanes.TrueForAll(r => Math.Abs(Math.Abs(r.Lateral) - 3.5f) < 1e-3f && r.Setting("pattern") == "24"), "a lane line each way, 2-4 below 60 km/h: " + string.Join("; ", lanes));
            List<AutoRun> edges = Runs(plan, "mk.line.edge");
            Program.Check(edges.Count == 2 && edges.Exists(r => r.Lateral > 6.9f && r.Setting("side") == "r") && edges.Exists(r => r.Lateral < -6.9f && r.Setting("side") == "r"), "an edge line on each side, on the right of its traffic (four lanes: 5.4.4)");
            Program.Check(plan.Stamps.Count == 0, "no junction: no marks");

            // 80 km/h: 6-9.
            var fast = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, Two, TwoBack, 80f) });
            Program.Check(Runs(AutoMarking.Plan(fast, null, Opt()), "mk.line.lane").TrueForAll(r => r.Setting("pattern") == "69"), "6-9 at 60 km/h or more");

            // One lane each way: the dashed yellow centre line.
            var narrow = new RoadChain(new[] { Piece(P(0f, 0f), P(0f, 100f), false, One, OneBack) });
            AutoPlan narrowPlan = AutoMarking.Plan(narrow, null, Opt());
            Program.Check(Runs(narrowPlan, "mk.line.centre").Count == 1 && Runs(narrowPlan, "mk.line.doubleyellow").Count == 0, "two lanes in all: the crossable centre line (5.1.3)");

            // The lanes change at the node: the lane lines of the wider piece stop there.
            var c = Piece(P(0f, 0f), P(0f, 60f), false, Two, TwoBack, 50f, ChainJoint.Pass);
            var d = Piece(P(0f, 60f), P(0f, 120f), false, One, OneBack, 50f);
            AutoPlan change = AutoMarking.Plan(new RoadChain(new[] { c, d }), null, Opt());
            Program.Check(Runs(change, "mk.line.lane").TrueForAll(r => r.S1 <= 60.01f), "the lane lines end where their lanes do");
            Program.Check(Runs(change, "mk.line.doubleyellow").Count == 1 && Runs(change, "mk.line.centre").Count == 1, "the double yellow becomes the dashed centre line where the lanes are one each way");
        }

        private static void EdgeRules()
        {
            // One lane each way with kerbs both sides: no edge lines (5.4.5); without kerbs: edge lines.
            var kerbed = Piece(P(0f, 0f), P(0f, 100f), false, One, OneBack);
            kerbed.FootwayLeft = kerbed.FootwayRight = true;
            Program.Check(Runs(AutoMarking.Plan(new RoadChain(new[] { kerbed }), null, Opt()), "mk.line.edge").Count == 0, "a kerbed two-lane road: no edge lines");
            var rural = Piece(P(0f, 0f), P(0f, 100f), false, One, OneBack);
            Program.Check(Runs(AutoMarking.Plan(new RoadChain(new[] { rural }), null, Opt()), "mk.line.edge").Count == 2, "without kerbs: both");
            var oneSide = Piece(P(0f, 0f), P(0f, 100f), false, One, OneBack);
            oneSide.FootwayRight = true;
            List<AutoRun> left = Runs(AutoMarking.Plan(new RoadChain(new[] { oneSide }), null, Opt()), "mk.line.edge");
            Program.Check(left.Count == 1 && left[0].Lateral < 0f, "a kerb on the right only: the left edge line");
            // A reversed piece's kerbs are on the chain's other side.
            var reversed = Piece(P(0f, 100f), P(0f, 0f), true, One, OneBack);
            reversed.FootwayRight = true;
            List<AutoRun> flipped = Runs(AutoMarking.Plan(new RoadChain(new[] { reversed }), null, Opt()), "mk.line.edge");
            Program.Check(flipped.Count == 1 && flipped[0].Lateral > 0f, "a reversed piece: its right is the chain's left: " + string.Join(";", flipped));
            // Four lanes with kerbs: edge lines all the same (5.4.4).
            var wide = Piece(P(0f, 0f), P(0f, 100f), false, Two, TwoBack);
            wide.FootwayLeft = wide.FootwayRight = true;
            Program.Check(Runs(AutoMarking.Plan(new RoadChain(new[] { wide }), null, Opt()), "mk.line.edge").Count == 2, "four lanes: edge lines");
            AutoOptions all = Opt();
            all.Edges = EdgeLines.All;
            Program.Check(Runs(AutoMarking.Plan(new RoadChain(new[] { kerbed }), null, all), "mk.line.edge").Count == 2, "all: both");
            AutoOptions none = Opt();
            none.Edges = EdgeLines.None;
            Program.Check(Runs(AutoMarking.Plan(new RoadChain(new[] { wide }), null, none), "mk.line.edge").Count == 0, "none: none");
            // Parking beside: the dashed edge line (5.4.6).
            var parked = Piece(P(0f, 0f), P(0f, 100f), false, One, OneBack);
            parked.ParkingRight = true;
            List<AutoRun> dashed = Runs(AutoMarking.Plan(new RoadChain(new[] { parked }), null, Opt()), "mk.line.edge");
            Program.Check(dashed.Exists(r => r.Lateral > 0f && r.Setting("form") == "dashed") && dashed.Exists(r => r.Lateral < 0f && r.Setting("form") == null), "parking on the right: that edge line dashed");
        }

        /// <summary>A road of two lanes each way, 100 m, its lanes ending 2 m before a signalled junction at the chain's end; a crosswalk 0.5 m beyond.</summary>
        private static (RoadChain, ChainApproach) Signalled(float length = 100f)
        {
            var piece = Piece(P(0f, 0f), P(0f, length), false, Two, TwoBack, 50f, ChainJoint.End, 0f, length - 2f);
            var chain = new RoadChain(new[] { piece });
            var approach = new ChainApproach { StopS = length - 2f, WithChain = true, Control = JunctionControl.Signals, CrosswalkAt = 0.5f };
            approach.Lanes.Add(new ApproachLane { Lateral = 1.75f, Straight = true, Left = true, UTurn = true });
            approach.Lanes.Add(new ApproachLane { Lateral = 5.25f, Straight = true, Right = true });
            return (chain, approach);
        }

        private static void SignalledJunction()
        {
            (RoadChain chain, ChainApproach approach) = Signalled();
            AutoPlan plan = AutoMarking.Plan(chain, new[] { approach }, Opt(true));
            AutoStamp stop = plan.Stamps.Find(s => s.Id == "mk.stop");
            Program.Check(stop != null && stop.WithChain, "a stop line for the signals (6.4)");
            float back = AutoMarking.StopBeforeCrosswalk + AutoMarking.StopWidth * 0.5f - 0.5f;
            Program.Near(stop.S, 98f - back, 1e-3f, "1.5 m before the crosswalk (6.4.4: 1 m to 3 m)");
            Program.Near(stop.Span, 7f, 1e-3f, "from the centre line over the approach's lanes (6.4.3)");
            Program.Near(stop.Lateral, 3.5f, 1e-3f, "its middle");
            float stopEdge = stop.S - AutoMarking.StopWidth * 0.5f;
            List<AutoRun> guide = Runs(plan, "mk.line.guidelane");
            Program.Check(guide.Count == 1 && Math.Abs(guide[0].Lateral - 3.5f) < 1e-3f, "a guide lane line between the approach's lanes (6.10)");
            Program.Near(guide[0].S1, stopEdge, 1e-3f, "up to the stop line");
            Program.Near(guide[0].Length, AutoMarking.MinGuide, 1e-3f, "30 m long (6.10.3)");
            List<AutoRun> laneLine = Runs(plan, "mk.line.lane").FindAll(r => r.Lateral > 0f);
            Program.Check(laneLine.Count == 1 && Math.Abs(laneLine[0].S1 - guide[0].S0) < 1e-3f, "the dashed lane line stops where the guide lane line begins");
            AutoRun centre = Runs(plan, "mk.line.doubleyellow")[0];
            Program.Near(centre.S1, stopEdge, 1e-3f, "the centre line ends at the stop line");
            AutoRun departing = Runs(plan, "mk.line.lane").Find(r => r.Lateral < 0f);
            Program.Near(departing.S1, 98f, 1e-3f, "the other way's lane line ends at the lanes' end (the crosswalk is beyond)");
            AutoRun edge = Runs(plan, "mk.line.edge").Find(r => r.Lateral > 0f);
            Program.Near(edge.S1, stopEdge, 1e-3f, "the approach's edge line ends at the stop line");

            List<AutoStamp> arrows = plan.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal));
            Program.Check(arrows.Count == 4, "two groups of two arrows (5.13.6): " + arrows.Count);
            Program.Check(arrows.FindAll(s => s.Id == "mk.arrow.straightleft" && Math.Abs(s.Lateral - 1.75f) < 1e-3f).Count == 2, "straight or left in the left lane (a U-turn goes with the left arrow)");
            Program.Check(arrows.FindAll(s => s.Id == "mk.arrow.straightright" && Math.Abs(s.Lateral - 5.25f) < 1e-3f).Count == 2, "straight or right in the right lane");
            float length = AutoMarking.ArrowLength(chain.Pieces[0]);
            float firstTail = stopEdge - AutoMarking.ArrowBeforeStop - length;
            Program.Check(arrows.Exists(s => Math.Abs(s.S - firstTail) < 1e-3f), "the first group's head 2 m before the stop line (1 m to 5 m)");
            Program.Check(arrows.Exists(s => Math.Abs(s.S - guide[0].S0) < 1e-3f), "the second group's tail level with the start of the guide lane line");
            Program.Check(arrows.TrueForAll(s => s.WithChain), "facing the approach's traffic");

            // A preview group 30 m before the second.
            AutoOptions preview = Opt(true);
            preview.Preview = true;
            AutoPlan withPreview = AutoMarking.Plan(chain, new[] { approach }, preview);
            List<AutoStamp> more = withPreview.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal));
            Program.Check(more.Count == 6 && more.Exists(s => Math.Abs(s.S - (guide[0].S0 - AutoMarking.PreviewGap - length)) < 1e-3f), "a third group 30 m before the second");

            // The game's crossing lines kept: no stop line of ours, the rest the same.
            AutoPlan kept = AutoMarking.Plan(chain, new[] { approach }, Opt(false));
            Program.Check(!kept.Stamps.Exists(s => s.Id == "mk.stop") && Runs(kept, "mk.line.guidelane").Count == 1 && kept.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal)).Count == 4, "keeping the game's: guide lines and arrows, no stop line");
            AutoRun keptCentre = Runs(kept, "mk.line.doubleyellow")[0];
            Program.Near(keptCentre.S1, 98f, 1e-3f, "and the lines run to the lanes' end (the crosswalk is beyond it)");

            // No junction marking at all.
            AutoOptions plain = Opt(true);
            plain.Junctions = false;
            Program.Check(AutoMarking.Plan(chain, new[] { approach }, plain).Stamps.Count == 0, "without the junctions: no marks");
            Program.Check(plan.Runs.TrueForAll(r => r.S1 - r.S0 >= MarkingRuns.MinLength), "every line at least a metre long");
        }

        private static void OtherEnds()
        {
            // The approach at the chain's start: traffic against the chain, its lanes on the left (negative laterals), from the driver's left the larger lateral first.
            var piece = Piece(P(0f, 0f), P(0f, 100f), false, Two, TwoBack, 50f, ChainJoint.End, 3f, 100f);
            var chain = new RoadChain(new[] { piece });
            var approach = new ChainApproach { StopS = 3f, WithChain = false, Control = JunctionControl.Stop };
            approach.Lanes.Add(new ApproachLane { Lateral = -1.75f, Left = true });
            approach.Lanes.Add(new ApproachLane { Lateral = -5.25f, Straight = true, Right = true });
            AutoPlan plan = AutoMarking.Plan(chain, new[] { approach }, Opt(true));
            AutoStamp line = plan.Stamps.Find(s => s.Id == "mk.giveway.stop");
            Program.Check(line != null && !line.WithChain, "a stop sign: the stop-and-give-way line (线53), facing against the chain");
            Program.Near(line.S, 3f + AutoMarking.StopWidth * 0.5f, 1e-3f, "at the lanes' end when there is no crosswalk");
            Program.Near(line.Lateral, -3.5f, 1e-3f, "over the left lanes");
            Program.Check(line.Lanes == 2, "two lanes for its words");
            List<AutoRun> guide = Runs(plan, "mk.line.guidelane");
            Program.Check(guide.Count == 1 && Math.Abs(guide[0].Lateral + 3.5f) < 1e-3f && Math.Abs(guide[0].S0 - (line.S + AutoMarking.StopWidth * 0.5f)) < 1e-3f, "the guide line from the stop line on");
            List<AutoStamp> arrows = plan.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal));
            Program.Check(arrows.Exists(s => s.Id == "mk.arrow.left") && arrows.Exists(s => s.Id == "mk.arrow.straightright") && arrows.TrueForAll(s => !s.WithChain), "the arrows of the approach, facing it");
            float stopEdge = line.S + AutoMarking.StopWidth * 0.5f;
            float length = AutoMarking.ArrowLength(piece);
            Program.Check(arrows.Exists(s => Math.Abs(s.S - (stopEdge + AutoMarking.ArrowBeforeStop + length)) < 1e-3f), "the first group: tail further from the junction, the head 2 m from the stop line");
            AutoRun centre = Runs(plan, "mk.line.doubleyellow")[0];
            Program.Near(centre.S0, stopEdge, 1e-3f, "the centre line starts at the stop line");

            var yield = new ChainApproach { StopS = 3f, WithChain = false, Control = JunctionControl.Yield };
            yield.Lanes.AddRange(approach.Lanes);
            Program.Check(AutoMarking.Plan(chain, new[] { yield }, Opt(true)).Stamps.Exists(s => s.Id == "mk.giveway.yield"), "a yield: the yield line (线54)");
            var free = new ChainApproach { StopS = 3f, WithChain = false, Control = JunctionControl.None };
            free.Lanes.AddRange(approach.Lanes);
            AutoPlan freePlan = AutoMarking.Plan(chain, new[] { free }, Opt(true));
            Program.Check(!freePlan.Stamps.Exists(s => s.Id == "mk.stop") && freePlan.Notes.Exists(n => n.Contains("nothing controls")), "nothing controls it: no stop line, and a note");
            AutoOptions stopAll = Opt(true);
            stopAll.StopUncontrolled = true;
            Program.Check(AutoMarking.Plan(chain, new[] { free }, stopAll).Stamps.Exists(s => s.Id == "mk.stop"), "unless asked");
        }

        private static void Limits()
        {
            // An approach of 25 m: the guide lines cover all but 5 m of it, with a note; the arrows still fit.
            (RoadChain chain, ChainApproach approach) = Signalled(27f);
            AutoPlan plan = AutoMarking.Plan(chain, new[] { approach }, Opt(true));
            List<AutoRun> guide = Runs(plan, "mk.line.guidelane");
            Program.Check(guide.Count == 1 && guide[0].Length < AutoMarking.MinGuide && guide[0].Length > 15f, "a short approach: shorter guide lines: " + (guide.Count > 0 ? guide[0].Length.ToString("0.0") : "none"));
            Program.Check(plan.Notes.Exists(n => n.Contains("30 m")), "and a note");
            Program.Check(plan.Stamps.Exists(s => s.Id == "mk.arrow.straightleft"), "the arrows near the stop line");
            // Very short: no arrows.
            (RoadChain tiny, ChainApproach tinyApproach) = Signalled(8f);
            AutoPlan tinyPlan = AutoMarking.Plan(tiny, new[] { tinyApproach }, Opt(true));
            Program.Check(!tinyPlan.Stamps.Exists(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal)) && tinyPlan.Notes.Exists(n => n.Contains("shorter than an arrow")), "too short for an arrow: none, and a note");
            // A lane that goes three ways: no arrow (5.13.5).
            (RoadChain c3, ChainApproach a3) = Signalled();
            a3.Lanes[0].Right = true;
            AutoPlan p3 = AutoMarking.Plan(c3, new[] { a3 }, Opt(true));
            Program.Check(!p3.Stamps.Exists(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal) && Math.Abs(s.Lateral - 1.75f) < 1e-3f) && p3.Notes.Exists(n => n.Contains("5.13.5")), "a lane of three ways: no arrow, a note");
            // A longer guide length is kept; less than 30 is 30.
            AutoOptions longer = Opt(true);
            longer.GuideLength = 50f;
            Program.Near(Runs(AutoMarking.Plan(Signalled().Item1, new[] { Signalled().Item2 }, longer), "mk.line.guidelane")[0].Length, 50f, 1e-3f, "50 m when asked");
            AutoOptions shorter = Opt(true);
            shorter.GuideLength = 10f;
            Program.Near(Runs(AutoMarking.Plan(Signalled().Item1, new[] { Signalled().Item2 }, shorter), "mk.line.guidelane")[0].Length, 30f, 1e-3f, "never under 30 m");
            Program.Check(AutoMarking.Plan(null, null, Opt()).Runs.Count == 0 && AutoMarking.Plan(new RoadChain(new ChainPiece[0]), null, Opt()).Runs.Count == 0, "no chain: nothing");
        }

        private static void ArrowsAndSettings()
        {
            ApproachLane L(bool s, bool l, bool r, bool u) => new ApproachLane { Straight = s, Left = l, Right = r, UTurn = u };
            Program.Check(AutoMarking.ArrowFor(L(true, false, false, false)) == "mk.arrow.straight", "straight");
            Program.Check(AutoMarking.ArrowFor(L(false, true, false, false)) == "mk.arrow.left" && AutoMarking.ArrowFor(L(false, false, true, false)) == "mk.arrow.right", "left, right");
            Program.Check(AutoMarking.ArrowFor(L(false, false, false, true)) == "mk.arrow.uturn", "U-turn");
            Program.Check(AutoMarking.ArrowFor(L(true, true, false, false)) == "mk.arrow.straightleft" && AutoMarking.ArrowFor(L(true, false, true, false)) == "mk.arrow.straightright", "two ways");
            Program.Check(AutoMarking.ArrowFor(L(false, true, false, true)) == "mk.arrow.leftuturn" && AutoMarking.ArrowFor(L(true, false, false, true)) == "mk.arrow.straightuturn" && AutoMarking.ArrowFor(L(false, true, true, false)) == "mk.arrow.leftright", "the others of Table 1");
            Program.Check(AutoMarking.ArrowFor(L(true, true, true, false)) == null && AutoMarking.ArrowFor(L(false, false, false, false)) == null, "three ways, or none: no arrow");
            foreach (string id in new[] { "mk.arrow.straight", "mk.arrow.left", "mk.arrow.right", "mk.arrow.uturn", "mk.arrow.straightleft", "mk.arrow.straightright", "mk.arrow.leftuturn", "mk.arrow.straightuturn", "mk.arrow.leftright", "mk.stop", "mk.giveway.stop", "mk.giveway.yield", "mk.line.guidelane" })
                Program.Check(SignCatalog.ById(id) != null, id + " is in the catalogue");

            SignDef auto = SignCatalog.ById(AutoMarking.Id);
            Program.Check(auto != null && auto.IsMarking && auto.Template != null, "the automatic marking is a marking of the panel");
            AutoOptions o = AutoMarking.OptionsOf(auto.Template.Default());
            Program.Check(o.Junctions && !o.CrossLines && o.Arrows && !o.Preview && Math.Abs(o.GuideLength - 30f) < 1e-4f && o.Edges == EdgeLines.Auto && !o.StopUncontrolled, "its defaults: junctions, arrows, 30 m, the game's crossing lines kept");
            GuideSpec all = auto.Template.Default().With(AutoMarking.KeepKey, "all").With(AutoMarking.GuideKey, "5").With(AutoMarking.EdgeKey, "none").With(AutoMarking.PreviewKey, "1");
            AutoOptions p = AutoMarking.OptionsOf(all);
            Program.Check(p.CrossLines && Math.Abs(p.GuideLength - 30f) < 1e-4f && p.Edges == EdgeLines.None && p.Preview, "clearing all draws the crossing lines; 5 m is held to 30");
            MarkingPlan picture = MarkingTemplates.PlanOf(auto, null, false);
            Program.Check(picture != null && picture.Parts.Count > 0, "its picture has paint");
        }
    }
}
