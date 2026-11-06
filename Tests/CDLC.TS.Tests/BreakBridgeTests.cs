using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.100 (the owner, 5 Nov 23:0x: "这种岔路口会折角", a noise wall in "连续通过" mode across a five-road junction, with a photo): the log of the placement (23:00:56) has the chain of three pieces,
    /// #104827r and #104844r of half width 14.5 (the second ending at (-2872.9, -2761.5)) and #991989 of half width 4.0 starting at (-2867.0, -2761.0), 6.0 m away (5.9 m to the side of the line the
    /// first was heading on, heading 19 degrees more to the right): a break of 0.1 m in the chain's distance. The wall went across it in one chord of 6.2 m against the way it ran, turns of 87 and
    /// -130 degrees, and the wall before the break had been drawn 1.4 m in from the edge of the wide road by the blend of the lanes' gap. The chain runs north (+Z), right is +X.
    /// </summary>
    internal static class BreakBridgeTests
    {
        public static void RunAll()
        {
            Program.Run("break bridge: the owner's junction, the wall drawn the way the chain runs", OwnersJunction);
            Program.Run("break bridge: the owner's junction, drawn the other way (as he drew it, from the narrow road)", OwnersJunctionBackwards);
            Program.Run("break bridge: the profile holds each piece's edge to the break", HoldsToTheBreak);
            Program.Run("break bridge: a road that widens and one on the other side of the same junction", OtherWays);
            Program.Run("break bridge: two breaks in one line, drawn each way: each is cut on the side of the narrower road", TwoBreaks);
            Program.Run("break bridge: a break straight on keeps its line, and a line that does not cross one is as it was", Straight);
            Program.Run("break bridge: the join of two stretches", Joins);
        }

        private static ChainPiece Piece(Vec3 a, Vec3 d, ChainJoint after, float half)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(a, d), Reversed = false, Kmh = 80f, HasSpeed = true, After = after, HalfWidth = half };
            piece.Forward.Add(half * 0.25f);
            piece.Backward.Add(-half * 0.25f);
            piece.ReaderJoint = after;
            return piece;
        }

        /// <summary>The chain of the owner's log: the wide road in two pieces, and the narrow one that starts 6 m to the side after a junction.</summary>
        private static RoadChain Owners(float halfWide = 14.5f, float halfNarrow = 4.0f)
        {
            ChainPiece a0 = Piece(new Vec3(-2851.4f, 404.9f, -2858.7f), new Vec3(-2869.6f, 405.0f, -2789.1f), ChainJoint.Pass, halfWide);
            ChainPiece a1 = Piece(new Vec3(-2869.6f, 405.0f, -2789.1f), new Vec3(-2872.9f, 405.0f, -2761.5f), ChainJoint.Junction, halfWide);
            ChainPiece b = Piece(new Vec3(-2867.0f, 405.0f, -2761.0f), new Vec3(-2846.3f, 405.0f, -2701.2f), ChainJoint.End, halfNarrow);
            return new RoadChain(new[] { a0, a1, b });
        }

        private static List<Vec3> Wall(RoadChain chain, int side, float offset, float s0, float s1)
        {
            var profile = new EdgeProfile(chain, side);
            return chain.LineAdaptive(s0, s1, s => profile.LateralAt(s, offset), 0f);
        }

        /// <summary>The sharpest turn from one chord to the next (degrees) and the longest chord (m).</summary>
        private static void Shape(IList<Vec3> line, out float turn, out float longest)
        {
            turn = 0f;
            longest = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                longest = Math.Max(longest, Vec3.DistanceXZ(line[i - 1], line[i]));
                if (i >= 2)
                {
                    Vec3 before = (line[i - 1] - line[i - 2]).HeadingXZ(), after = (line[i] - line[i - 1]).HeadingXZ();
                    turn = Math.Max(turn, (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vec3.Dot(before, after)))) * 180.0 / Math.PI));
                }
            }
        }

        /// <summary>How far a point is out from a piece's right edge (m, negative: inside the road) in the piece's own frame, and how far along it.</summary>
        private static void InFrame(ChainPiece piece, Vec3 p, out float across, out float along)
        {
            Vec3 start = piece.Curve.Point(0f), heading = (piece.Curve.Point(1f) - start).HeadingXZ();
            across = Vec3.Dot(p - start, heading.RightXZ());
            along = Vec3.Dot(p - start, heading);
        }

        private static void Measure(RoadChain chain, List<Vec3> wall, float inset, out float worstWide, out float worstNarrow)
        {
            ChainPiece wide = chain.Pieces[1], narrow = chain.Pieces[2];
            float edgeWide = BarrierAuto.EdgeOf(wide, 1), edgeNarrow = BarrierAuto.EdgeOf(narrow, 1);
            worstWide = float.MaxValue;
            worstNarrow = float.MaxValue;
            float wideLength = (wide.Curve.Point(1f) - wide.Curve.Point(0f)).LengthXZ;
            foreach (Vec3 p in wall)
            {
                InFrame(wide, p, out float acrossWide, out float alongWide);
                InFrame(narrow, p, out float acrossNarrow, out float alongNarrow);
                if (alongWide <= wideLength)
                    worstWide = Math.Min(worstWide, acrossWide - (edgeWide + inset));
                if (alongNarrow >= 0f)
                    worstNarrow = Math.Min(worstNarrow, acrossNarrow - (edgeNarrow + inset));
            }
        }

        private static void OwnersJunction()
        {
            RoadChain chain = Owners();
            Program.Check(chain.Breaks.Count == 1 && chain.BrokenAfter(1) && !chain.BrokenAfter(0) && !chain.BrokenAfter(2) && !chain.BrokenAfter(-1), $"the chain has one break, after the second piece: {chain.Breaks.Count}");
            float brk = chain.Pieces[1].S1;
            Program.Check(Math.Abs(chain.Pieces[2].S0 - brk - RoadChain.BreakLength) < 1e-3f && Vec3.DistanceXZ(chain.Pieces[1].Curve.Point(1f), chain.Pieces[2].Curve.Point(0f)) > 5.9f, "it is 0.1 m in the chain's distance and 6 m on the ground");
            // What the line was without the bridge: made across the break by the chain's distance, with the plateaus of the profile.
            var profile = new EdgeProfile(chain, 1);
            List<Vec3> raw = BarrierPath.Sample(s => chain.PointAt(s, profile.LateralAt(s, 0.2f)), brk - 40f, brk + 40f);
            Shape(raw, out float rawTurn, out float rawLongest);
            Program.Check(rawTurn > 60f && rawLongest > 4.5f, $"without the bridge the wall makes the log's hook: a turn of {rawTurn:0} degrees and a chord of {rawLongest:0.0} m (the log: 87 and -130, 6.2 m)");

            List<Vec3> wall = Wall(chain, 1, 0.2f, brk - 40f, brk + 40f);
            Shape(wall, out float turn, out float longest);
            Program.Check(wall.Count > 30 && turn < 12f && longest < 3f, $"with it the wall is smooth: the sharpest turn {turn:0.0} degrees, the longest chord {longest:0.0} m ({wall.Count} points)");
            bool forward = true;
            for (int i = 1; i < wall.Count; i++)
                forward &= wall[i].Z > wall[i - 1].Z;
            Program.Check(forward, "and every chord goes on north: none goes back against the way the wall runs");
            Console.WriteLine($"  break bridge: the owner's junction, the wall in the 60 m round the break (x, z): " + string.Join(" ", wall.FindAll(p => p.Z > -2790f && p.Z < -2735f).ConvertAll(p => $"({p.X:0.0},{p.Z:0.0})")));
            Measure(chain, wall, 0.2f, out float worstWide, out float worstNarrow);
            Program.Check(worstWide > -0.05f, $"the wall is nowhere inside the edge of the wide road before it ends: the least margin {worstWide:0.000} m");
            Program.Check(worstNarrow > -0.05f, $"and nowhere inside the edge of the narrow road from where it starts: the least margin {worstNarrow:0.000} m");
            // It holds the wide road's edge to the end of that road (the bridge begins there: the narrow road is inside it), then comes in.
            InFrame(chain.Pieces[0], wall[0], out float first, out _);
            Program.Check(Math.Abs(first - (BarrierAuto.EdgeOf(chain.Pieces[0], 1) + 0.2f)) < 0.05f, $"it starts at the wide road's edge plus 0.2 m: {first:0.00} m out");
            Vec3 end = wall[wall.Count - 1];
            InFrame(chain.Pieces[2], end, out float last, out _);
            Program.Check(Math.Abs(last - (BarrierAuto.EdgeOf(chain.Pieces[2], 1) + 0.2f)) < 0.05f, $"and ends at the narrow road's edge plus 0.2 m: {last:0.00} m out");
            // The bridge makes the same line however far the stretches reach.
            List<Vec3> longer = Wall(chain, 1, 0.2f, brk - 70f, brk + 60f);
            float nearest = 0f;
            foreach (Vec3 p in wall)
            {
                float d = float.MaxValue;
                for (int i = 1; i < longer.Count; i++)
                    d = Math.Min(d, DistanceToSegment(p, longer[i - 1], longer[i]));
                nearest = Math.Max(nearest, d);
            }

            Program.Check(nearest < 0.1f, $"a wall that reaches farther on either side is on the same line where they overlap (within {nearest:0.000} m)");
        }

        private static float DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z, l2 = dx * dx + dz * dz;
            float t = l2 < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / l2));
            float fx = a.X + dx * t - p.X, fz = a.Z + dz * t - p.Z;
            return (float)Math.Sqrt(fx * fx + fz * fz);
        }

        private static void OwnersJunctionBackwards()
        {
            // As he drew it: from the narrow road (2965 m in the log) over the junction to the wide one.
            RoadChain chain = Owners();
            float brk = chain.Pieces[1].S1;
            List<Vec3> wall = Wall(chain, 1, 0.2f, brk + 40f, brk - 40f);
            Shape(wall, out float turn, out float longest);
            Program.Check(wall.Count > 30 && turn < 12f && longest < 3f, $"drawn from the narrow road: the sharpest turn {turn:0.0} degrees, the longest chord {longest:0.0} m ({wall.Count} points)");
            bool south = true;
            for (int i = 1; i < wall.Count; i++)
                south &= wall[i].Z < wall[i - 1].Z;
            Program.Check(south, "every chord goes on south");
            List<Vec3> forwardWall = Wall(chain, 1, 0.2f, brk - 40f, brk + 40f);
            bool same = forwardWall.Count == wall.Count;
            for (int i = 0; same && i < wall.Count; i++)
                same &= Vec3.DistanceXZ(wall[i], forwardWall[forwardWall.Count - 1 - i]) < 0.3f;
            Program.Check(same, "and it is the line of the other way, point for point, within 0.3 m");
            Measure(chain, wall, 0.2f, out float worstWide, out float worstNarrow);
            Program.Check(worstWide > -0.05f && worstNarrow > -0.05f, $"outside both roads: the least margins {worstWide:0.000} and {worstNarrow:0.000} m");
            // A wall that is started past the break and stops before the bridge is done is cut at its own end.
            List<Vec3> shortLine = Wall(chain, 1, 0.2f, brk + 8f, brk - 8f);
            Shape(shortLine, out float shortTurn, out _);
            Program.Check(shortLine.Count >= 2 && shortTurn < 50f && shortTurn > BreakBridge.MaxTurn, $"a wall of 16 m across the break: {shortLine.Count} points, the sharpest turn {shortTurn:0} degrees (it has not the room for a gentle one)");
        }

        private static void HoldsToTheBreak()
        {
            RoadChain chain = Owners();
            var profile = new EdgeProfile(chain, 1);
            float brk = chain.Pieces[1].S1;
            float wide = BarrierAuto.EdgeOf(chain.Pieces[1], 1), narrow = BarrierAuto.EdgeOf(chain.Pieces[2], 1);
            Program.Check(Math.Abs(profile.At(brk - 0.3f) - wide) < 1e-3f && Math.Abs(profile.At(brk - 13f) - wide) < 1e-3f, $"the wide road's edge is held to the break: {profile.At(brk - 13f):0.00} 13 m before it, {profile.At(brk - 0.3f):0.00} at 0.3 m, {wide:0.00} as it is");
            Program.Check(Math.Abs(profile.At(chain.Pieces[2].S0 + 0.3f) - narrow) < 1e-3f && Math.Abs(profile.At(chain.Pieces[2].S0 + 30f) - narrow) < 1e-3f, $"and the narrow road's from where it starts: {profile.At(chain.Pieces[2].S0 + 0.3f):0.00}, {narrow:0.00} as it is");
            // A node that is not a break keeps the blend (the through nodes of the other tests).
            ChainPiece a = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.Pass, 6f);
            ChainPiece b = Piece(new Vec3(0f, 0f, 100f), new Vec3(0f, 0f, 200f), ChainJoint.End, 8f);
            var joined = new RoadChain(new[] { a, b });
            var joinedProfile = new EdgeProfile(joined, 1);
            Program.Check(joined.Breaks.Count == 0 && !joined.BrokenAfter(0) && joinedProfile.At(98.5f) < joinedProfile.At(103f) && joinedProfile.At(98.5f) > joinedProfile.At(50f), $"a node that is no break still blends the edges across it: {joinedProfile.At(98.5f):0.00} to {joinedProfile.At(103f):0.00}");
        }

        private static void OtherWays()
        {
            // The same junction the other way round for the wall: the left side (the narrow road is 6 m to the right of the wide one's line, so its left edge is 16 m in from the wide road's).
            RoadChain chain = Owners();
            float brk = chain.Pieces[1].S1;
            List<Vec3> left = Wall(chain, -1, 0.2f, brk - 60f, brk + 60f);
            Shape(left, out float turn, out float longest);
            bool forward = left.Count > 20;
            for (int i = 1; i < left.Count; i++)
                forward &= left[i].Z > left[i - 1].Z;
            Program.Check(forward && turn < 25f && longest < 3.5f, $"the left wall, whose edges are 16 m apart across the break, goes on north all the way: the sharpest turn {turn:0} degrees, the longest chord {longest:0.0} m");
            // A narrow road that widens: the first road narrow, the second wide and 6 m to the side.
            ChainPiece a = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.Junction, 4f);
            ChainPiece b = Piece(new Vec3(5.9f, 0f, 106f), new Vec3(5.9f, 0f, 206f), ChainJoint.End, 14.5f);
            var widening = new RoadChain(new[] { a, b });
            float wb = widening.Pieces[0].S1;
            List<Vec3> wall = Wall(widening, 1, 0.2f, wb - 50f, wb + 50f);
            Shape(wall, out float wTurn, out float wLongest);
            bool north = wall.Count > 30;
            for (int i = 1; i < wall.Count; i++)
                north &= wall[i].Z > wall[i - 1].Z;
            // The wide road's right edge is 14.5 - 0.35 + 0.2 = 14.35 from its line, which is at x = 5.9: x = 20.25; the narrow road's: 4 - 0.35 + 0.2 = 3.85.
            float outsideNarrow = float.MaxValue, outsideWide = float.MaxValue;
            foreach (Vec3 p in wall)
            {
                if (p.Z <= 100f)
                    outsideNarrow = Math.Min(outsideNarrow, p.X - 3.85f);
                if (p.Z >= 106f)
                    outsideWide = Math.Min(outsideWide, p.X - (5.9f + BarrierAuto.EdgeOf(widening.Pieces[1], 1) + 0.2f));
            }

            Program.Check(widening.Breaks.Count == 1 && north && wTurn <= BreakBridge.MaxTurn + 0.5f && wLongest < 3f, $"a road that widens across a break 6 m to the side: the sharpest turn {wTurn:0.0} degrees, the longest chord {wLongest:0.0} m, always north");
            Program.Check(outsideNarrow > -0.05f && outsideWide > -0.05f, $"and the wall is outside both: the least margins {outsideNarrow:0.000} and {outsideWide:0.000} m");
        }

        /// <summary>The worst margin (m) a wall has to the right edge of each piece of a straight chain along +Z, for the points that are beside the piece (along it): negative is inside the road.</summary>
        private static float Margin(RoadChain chain, List<Vec3> wall, float inset)
        {
            float worst = float.MaxValue;
            foreach (ChainPiece piece in chain.Pieces)
            {
                float edge = BarrierAuto.EdgeOf(piece, 1) + inset;
                Vec3 start = piece.Curve.Point(0f), end = piece.Curve.Point(1f);
                foreach (Vec3 p in wall)
                {
                    if (p.Z >= start.Z && p.Z <= end.Z)
                        worst = Math.Min(worst, p.X - (start.X + edge));
                }
            }

            return worst;
        }

        private static void TwoBreaks()
        {
            // A wide road, a narrow one 6 m to the right of it after a break, a wide one again after another: the wall steps in at the first break (the narrow road's stretch is cut after it) and out at the
            // second (cut before it); with the cut taken from the wrong break one of the two would run through a road.
            ChainPiece a = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.Junction, 14.5f);
            ChainPiece b = Piece(new Vec3(6f, 0f, 106f), new Vec3(6f, 0f, 206f), ChainJoint.Junction, 4f);
            ChainPiece c = Piece(new Vec3(0f, 0f, 212f), new Vec3(0f, 0f, 312f), ChainJoint.End, 14.5f);
            var chain = new RoadChain(new[] { a, b, c });
            Program.Check(chain.Breaks.Count == 2 && chain.BrokenAfter(0) && chain.BrokenAfter(1), "the chain has two breaks");
            List<Vec3> forward = Wall(chain, 1, 0.2f, 40f, chain.Length - 30f), backward = Wall(chain, 1, 0.2f, chain.Length - 30f, 40f);
            foreach ((string way, List<Vec3> wall) in new[] { ("forward", forward), ("backward", backward) })
            {
                Shape(wall, out float turn, out float longest);
                bool onward = wall.Count > 60;
                for (int i = 1; i < wall.Count; i++)
                    onward &= way == "forward" ? wall[i].Z > wall[i - 1].Z : wall[i].Z < wall[i - 1].Z;
                float margin = Margin(chain, wall, 0.2f);
                Program.Check(onward && turn <= BreakBridge.MaxTurn + 0.5f && longest < 3f && margin > -0.05f, $"{way}: always on, the sharpest turn {turn:0.0} degrees, the longest chord {longest:0.0} m, outside every road (the least margin {margin:0.000} m)");
            }
        }

        private static void Straight()
        {
            // Two pieces on one line with a gap of 5 m between them (a junction's node): the wall goes across in a straight chord, with no points of a curve put in it.
            ChainPiece a = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.Junction, 6f);
            ChainPiece b = Piece(new Vec3(0f, 0f, 105f), new Vec3(0f, 0f, 205f), ChainJoint.End, 6f);
            var chain = new RoadChain(new[] { a, b });
            float brk = chain.Pieces[0].S1;
            List<Vec3> wall = Wall(chain, 1, 0.2f, 50f, 150f);
            Shape(wall, out float turn, out float longest);
            bool straight = wall.Count > 40;
            foreach (Vec3 p in wall)
                straight &= Math.Abs(p.X - 5.85f) < 0.01f;
            Program.Check(chain.Breaks.Count == 1 && straight && turn < 0.5f && longest < 5.2f, $"a break straight on: the wall is on one line, a turn of {turn:0.0} degrees, the chord across it {longest:0.0} m");
            int expected = (int)Math.Floor((brk - 50f) / 2f) + 1 + (int)Math.Floor((150f - chain.Pieces[1].S0) / 2f) + 2;
            Program.Check(Math.Abs(wall.Count - expected) <= 2, $"with no point put into the chord: {wall.Count} points (about {expected})");
            // A line that does not cross the break is the Sample of the chain's distance, as it was.
            List<Vec3> before = Wall(chain, 1, 0.2f, 10f, 90f);
            var profile = new EdgeProfile(chain, 1);
            List<Vec3> plain = BarrierPath.Sample(s => chain.PointAt(s, profile.LateralAt(s, 0.2f)), 10f, 90f);
            bool same = before.Count == plain.Count;
            for (int i = 0; same && i < before.Count; i++)
                same &= Vec3.DistanceXZ(before[i], plain[i]) < 1e-4f;
            Program.Check(same, "a line that does not cross a break is the line it was");
            // One that ends inside the break's 0.1 m, or starts there, is not one that crosses it.
            Program.Check(chain.LineAdaptive(brk - 30f, brk + 0.05f, s => 5.85f, 0f).Count >= 2 && chain.LineAdaptive(brk + 0.05f, brk + 30f, s => 5.85f, 0f).Count >= 2, "a line that ends or starts inside the break's 0.1 m is made as before");
            // A line that starts at the end of the piece before the break is not across it; and one with a stretch of under a metre across it keeps that stretch.
            List<Vec3> atBreak = chain.LineAdaptive(brk, brk + 30f, s => 5.85f, 0f);
            bool apart = atBreak.Count >= 2;
            for (int i = 1; i < atBreak.Count; i++)
                apart &= Vec3.DistanceXZ(atBreak[i - 1], atBreak[i]) > 0.01f;
            List<Vec3> asSampled = BarrierPath.Sample(s => chain.PointAt(s, 5.85f), brk, brk + 30f);
            bool unchanged = atBreak.Count == asSampled.Count;
            for (int i = 0; unchanged && i < atBreak.Count; i++)
                unchanged &= Vec3.DistanceXZ(atBreak[i], asSampled[i]) < 1e-4f;
            Program.Check(apart && unchanged, $"a line that starts exactly at the end of the piece before the break is not across it: the line it was, no point twice ({atBreak.Count} points)");
            List<Vec3> sliver = chain.LineAdaptive(brk - 30f, brk + RoadChain.BreakLength + 0.6f, s => 5.85f, 0f);
            Program.Check(sliver.Count >= 2 && Math.Abs(sliver[sliver.Count - 1].Z - (chain.Pieces[1].Curve.Point(0f).Z + 0.6f)) < 0.05f, "a stretch of 0.6 m after the break (under the metre a line is) is kept: the line ends where it was asked to");
            // Over the length of a line, or under it: nothing, as before.
            Program.Check(chain.LineAdaptive(brk - 0.4f, brk + 0.4f, s => 5.85f, 0f).Count == 0, "a line of less than a metre across a break: none, as before");
        }

        private static List<Vec3> Run(float x0, float z0, float x1, float z1, float y0 = 0f, float y1 = 0f)
        {
            var line = new List<Vec3>();
            float length = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            int n = (int)Math.Ceiling(length / 2f);
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                line.Add(new Vec3(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, z0 + (z1 - z0) * t));
            }

            return line;
        }

        private static void Joins()
        {
            // Aligned, 6 m apart: nothing cut, the stretches end to end.
            List<Vec3> a = Run(0f, 0f, 0f, 40f), b = Run(0f, 46f, 0f, 86f);
            List<Vec3> joined = BreakBridge.Join(a, b);
            Program.Check(joined.Count == a.Count + b.Count && Vec3.DistanceXZ(joined[a.Count - 1], a[a.Count - 1]) < 1e-4f && Vec3.DistanceXZ(joined[a.Count], b[0]) < 1e-4f, $"aligned, 6 m apart: nothing is cut and no point is put in ({joined.Count} points of {a.Count + b.Count})");

            // The owner's: the end of the wall on the wide road, the start of the one on the narrow road, 4.7 m to the side and 2.4 m back, heading 19 degrees more to the right.
            Vec3 pa = new Vec3(-2859.0f, 405f, -2759.8f), headA = new Vec3(-0.119f, 0f, 0.993f).HeadingXZ();
            Vec3 pb = new Vec3(-2863.65f, 405f, -2762.2f), headB = new Vec3(0.327f, 0f, 0.945f).HeadingXZ();
            List<Vec3> wide = new List<Vec3>(), narrow = new List<Vec3>();
            for (int i = 20; i >= 0; i--)
                wide.Add(pa - headA * (2f * i));
            for (int i = 0; i <= 20; i++)
                narrow.Add(pb + headB * (2f * i));
            joined = BreakBridge.Join(wide, narrow, BreakBridge.Cut.After);
            Shape(joined, out float turn, out float longest);
            bool forward = true;
            for (int i = 1; i < joined.Count; i++)
                forward &= joined[i].Z > joined[i - 1].Z;
            Program.Check(forward && turn <= BreakBridge.MaxTurn + 0.5f && longest < 3f, $"the owner's numbers: goes on north, the sharpest turn {turn:0.0} degrees, the longest chord {longest:0.0} m");
            Program.Check(Vec3.DistanceXZ(joined[wide.Count - 1], pa) < 1e-3f, "the wall on the wide road is kept to its end (the narrow road is the one that is cut back)");
            int firstKept = narrow.FindIndex(p => joined.Exists(q => Vec3.DistanceXZ(p, q) < 1e-4f));
            Program.Check(firstKept >= 4 && firstKept <= 12 && Vec3.DistanceXZ(joined[joined.Count - 1], narrow[narrow.Count - 1]) < 1e-3f, $"the narrow road's wall is cut back by about {2 * firstKept} m: it was 4.7 m aside and 19 degrees round (8 to 24 m)");
            // With either free to be cut the same pair is found where it is the cheapest: the narrow road's wall is cut, the wide road's is not, unless a cut of the wide road's alone is as short.
            List<Vec3> either = BreakBridge.Join(wide, narrow);
            Shape(either, out float eitherTurn, out _);
            Program.Check(eitherTurn <= BreakBridge.MaxTurn + 0.5f, $"free to cut either: the sharpest turn {eitherTurn:0.0} degrees");
            // Only the wide road's wall may be cut: it is cut, the narrow road's is whole.
            List<Vec3> onlyBefore = BreakBridge.Join(wide, narrow, BreakBridge.Cut.Before);
            Program.Check(Vec3.DistanceXZ(onlyBefore[onlyBefore.Count - 1 - (narrow.Count - 1)], narrow[0]) < 1e-3f, "told that only the first stretch may be cut, the second is kept whole");
            // When nothing that is allowed goes forward, the other stretch may be cut: the second of 2 m, the first on a line beside it.
            List<Vec3> blocked = BreakBridge.Join(Run(0f, 0f, 0f, 40f), Run(-3f, 38f, -3f, 40f), BreakBridge.Cut.After);
            Program.Check(blocked.Count >= 2 && Vec3.DistanceXZ(blocked[blocked.Count - 1], new Vec3(-3f, 0f, 40f)) < 1e-3f && blocked.FindIndex(p => Vec3.DistanceXZ(p, new Vec3(0f, 0f, 40f)) < 1e-4f) < 0, "and where the stretch allowed is too short to go forward, the other is cut instead");

            // A right angle: the stretches are cut back by the radius, both.
            joined = BreakBridge.Join(Run(0f, 0f, 0f, 40f), Run(4f, 44f, 44f, 44f));
            Shape(joined, out turn, out longest);
            Program.Check(turn <= BreakBridge.MaxTurn + 0.5f && longest < 3f && Vec3.DistanceXZ(joined[0], new Vec3(0f, 0f, 0f)) < 1e-3f && Vec3.DistanceXZ(joined[joined.Count - 1], new Vec3(44f, 0f, 44f)) < 1e-3f, $"a right angle: the sharpest turn {turn:0.0} degrees, the longest chord {longest:0.0} m, both ends kept");
            // Cut back the same on both sides when it is the same on both.
            Vec3 leave = joined.Find(p => Math.Abs(p.X) > 0.05f && p.Z < 40f);
            Program.Check(leave.X != 0f && leave.Z > 20f && leave.Z < 36f, $"the first stretch is left at {leave.Z:0.0} m of 40");

            // Stretches too short for a gentle curve: the gentlest there is, finite, both ends kept.
            joined = BreakBridge.Join(Run(0f, 0f, 0f, 5f), Run(3f, 8f, 8f, 8f));
            bool finite = joined.Count >= 2;
            foreach (Vec3 p in joined)
                finite &= p.IsFinite;
            Program.Check(finite && Vec3.DistanceXZ(joined[0], new Vec3(0f, 0f, 0f)) < 1e-3f && Vec3.DistanceXZ(joined[joined.Count - 1], new Vec3(8f, 0f, 8f)) < 1e-3f, $"stretches of 5 m at a right angle: {joined.Count} points, finite, both ends kept");

            // The second stretch starts behind the first one's end on a line that is not the first's: none goes forward at no trim; one does, farther on.
            joined = BreakBridge.Join(Run(0f, 0f, 0f, 40f), Run(-3f, 38f, -3f, 78f));
            Shape(joined, out turn, out _);
            bool up = true;
            for (int i = 1; i < joined.Count; i++)
                up &= joined[i].Z > joined[i - 1].Z - 1e-4f;
            Program.Check(up && turn <= BreakBridge.MaxTurn + 0.5f, $"a stretch that starts 2 m behind the end of the one before and 3 m to the side: goes on, the sharpest turn {turn:0.0} degrees");

            // A road that widens by 24 m on the side the wall is on, with only the narrow road's wall to cut: the curve needs 40 m of it.
            joined = BreakBridge.Join(Run(0f, 0f, 0f, 100f), Run(24f, 104f, 24f, 204f), BreakBridge.Cut.Before);
            Shape(joined, out turn, out longest);
            Program.Check(turn <= BreakBridge.MaxTurn + 0.5f && longest < 3f && Vec3.DistanceXZ(joined[joined.Count - 1], new Vec3(24f, 0f, 204f)) < 1e-3f, $"24 m across with only the first stretch to cut: the sharpest turn {turn:0.0} degrees, the longest chord {longest:0.0} m");

            // Heights: from the end of the first to the start of the second, between them, and the points of both stretches are untouched where they are kept.
            joined = BreakBridge.Join(Run(0f, 0f, 0f, 40f, 0f, 4f), Run(5f, 46f, 5f, 86f, 10f, 14f));
            bool rising = true;
            float biggestStep = 0f;
            for (int i = 1; i < joined.Count; i++)
            {
                rising &= joined[i].Y >= joined[i - 1].Y - 1e-4f;
                biggestStep = Math.Max(biggestStep, joined[i].Y - joined[i - 1].Y);
            }

            Program.Check(rising && biggestStep < 1f && Math.Abs(joined[0].Y) < 1e-4f && Math.Abs(joined[joined.Count - 1].Y - 14f) < 1e-3f, $"the height goes from the first stretch's to the second's without a step back, and without a step of its own: the biggest {biggestStep:0.00} m");

            // A stretch that comes back the way the first one went: nothing goes forward, the stretches are put end to end as they were (not a loop made of them).
            List<Vec3> there = Run(0f, 0f, 0f, 40f), back = Run(0f, 46f, 0f, 6f);
            joined = BreakBridge.Join(there, back);
            Program.Check(joined.Count == there.Count + back.Count && Vec3.DistanceXZ(joined[there.Count], back[0]) < 1e-4f, $"a stretch that comes back along the first: end to end, as they were ({joined.Count} points of {there.Count + back.Count})");

            // Which stretch may be cut: only the one that is told. The second is short: with the first cut back as well a gentle curve is found, with it kept whole the gentlest there is.
            List<Vec3> longFirst = Run(0f, 0f, 0f, 100f), shortSecond = Run(8f, 104f, 8f, 108f);
            List<Vec3> onlySecond = BreakBridge.Join(longFirst, shortSecond, BreakBridge.Cut.After), eitherCut = BreakBridge.Join(longFirst, shortSecond);
            Program.Check(Vec3.DistanceXZ(onlySecond[longFirst.Count - 1], longFirst[longFirst.Count - 1]) < 1e-4f, "told that only the second may be cut, the first is whole");
            Program.Check(eitherCut.FindIndex(p => Vec3.DistanceXZ(p, longFirst[longFirst.Count - 1]) < 1e-4f) < 0, "free to cut either, the first is cut back to find the gentle curve the second has not the room for");

            // Nothing, one point, and nothing to join.
            Program.Check(BreakBridge.Join(null, null).Count == 0 && BreakBridge.Join(a, null).Count == a.Count && BreakBridge.Join(null, b).Count == b.Count && BreakBridge.Join(new List<Vec3>(), b).Count == b.Count, "no stretch, or one: the other as it is");
            Program.Check(BreakBridge.Join(new List<Vec3> { new Vec3(0f, 0f, 0f) }, b).Count == 1 + b.Count, "a stretch of one point has no heading: the stretches are put end to end");
        }
    }
}
