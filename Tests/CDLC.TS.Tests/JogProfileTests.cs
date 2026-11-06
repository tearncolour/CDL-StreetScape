using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.99 (the owner, 5 Nov 20:2x: "不迂回了，但是还是存在折线"): at the node of his road 316290 where its pieces are 2.0 m to the side of each other and the road widens from 12 to 16 m, the path of the wall
    /// the log shows moves from 0.8 to 3.1 m across the road within 2 m along it (chords of 0.25, 0.27, 1.19 and 0.5 m, turns of +16, +41 and -55 degrees), then drifts 0.8 m more over 10 m. The 4 m it moves
    /// in all is 2 m of the centre line stepping across the node (made at once, in the chord across it) and 2 m of the road widening, which the edge profile spreads over 6 m. The profile now takes the
    /// step into the change of the edge, spreads it over a length that grows with it, on the side of the node where the wall is not in the road, and takes the step off the lateral where the centre line makes it.
    /// The road runs along +Z; right is +X.
    /// </summary>
    internal static class JogProfileTests
    {
        public static void RunAll()
        {
            Program.Run("jog profile: a road that widens across a jogged node: the wall is out before it, smooth, never in the road", Widening);
            Program.Run("jog profile: a road that narrows across a jogged node: the wall comes in after it, smooth, never in the road", Narrowing);
            Program.Run("jog profile: the spread is 6 m for each metre of change, from 6 to 24 m", LengthOfTheSpread);
            Program.Run("jog profile: the other side of the same road goes straight, the centre line's step is taken off", OtherSide);
            Program.Run("jog profile: the owner's node, with the numbers of his log", OwnersNode);
            Program.Run("jog profile: without a jog, or with a small one, the profile is as it was", AsItWas);
        }

        private static ChainPiece Piece(Vec3 a, Vec3 d, ChainJoint after, float half)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(a, d), Reversed = false, Kmh = 80f, HasSpeed = true, After = after, HalfWidth = half };
            // The lanes lie well inside the road, so that its half width less the inset is the edge (BarrierAuto.EdgeOf takes the outermost lane and its half and 0.3 m when that is farther out).
            piece.Forward.Add(half * 0.25f);
            piece.Backward.Add(-half * 0.25f);
            piece.ReaderJoint = after;
            return piece;
        }

        /// <summary>Two straight pieces along +Z, 100 m each: the first on x = 0 with half width halfA, the second on x = jog with halfB, a plain node of two roads between them.</summary>
        private static RoadChain Two(float halfA, float halfB, float jog, float along = 0f)
        {
            ChainPiece a = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.Pass, halfA);
            ChainPiece b = Piece(new Vec3(jog, 0f, 100f + along), new Vec3(jog, 0f, 200f + along), ChainJoint.End, halfB);
            return new RoadChain(new[] { a, b });
        }

        private static List<Vec3> Wall(RoadChain chain, int side, float offset)
        {
            var profile = new EdgeProfile(chain, side);
            return chain.LineAdaptive(0f, chain.Length, s => profile.LateralAt(s, offset), 0f);
        }

        /// <summary>The steepest the line goes across the road (dx over dz of a chord), the sharpest turn from one chord to the next (degrees), and the longest chord (m).</summary>
        private static void Shape(List<Vec3> line, out float slope, out float turn, out float longest)
        {
            slope = 0f;
            turn = 0f;
            longest = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                float dx = line[i].X - line[i - 1].X, dz = line[i].Z - line[i - 1].Z;
                longest = Math.Max(longest, (float)Math.Sqrt(dx * dx + dz * dz));
                if (dz > 1e-4f)
                    slope = Math.Max(slope, Math.Abs(dx) / dz);
                if (i >= 2)
                {
                    float px = line[i - 1].X - line[i - 2].X, pz = line[i - 1].Z - line[i - 2].Z;
                    double angle = Math.Abs(Math.Atan2(dx, dz) - Math.Atan2(px, pz)) * 180.0 / Math.PI;
                    turn = Math.Max(turn, (float)(angle > 180.0 ? 360.0 - angle : angle));
                }
            }
        }

        /// <summary>The x of the line where it crosses z (linear between its points).</summary>
        private static float XAt(List<Vec3> line, float z)
        {
            for (int i = 1; i < line.Count; i++)
            {
                if (line[i].Z >= z && line[i - 1].Z <= z && line[i].Z > line[i - 1].Z)
                    return line[i - 1].X + (line[i].X - line[i - 1].X) * (z - line[i - 1].Z) / (line[i].Z - line[i - 1].Z);
            }

            return float.NaN;
        }

        private static void Widening()
        {
            // The owner's: a road 12 m wide, then 16 m wide, 2.0 m to the right: the right edge of the road goes from 6 to 10 m, the wall at the edge less the inset (0.35) and 0.2 m off it.
            RoadChain chain = Two(6f, 8f, 2f);
            Program.Check(chain.Breaks.Count == 0 && chain.Joins.Count == 1 && Math.Abs(chain.Joins[0].Jog - 2f) < 0.05f && Math.Abs(chain.Joins[0].S - 100f) < 0.1f, $"the chain has one join, 2 m to the right, at 100 m: {chain.Joins.Count} join(s), jog {(chain.Joins.Count > 0 ? chain.Joins[0].Jog : 0f):0.00}");
            List<Vec3> wall = Wall(chain, 1, 0.2f);
            Shape(wall, out float slope, out float turn, out float longest);
            Program.Check(turn < 20f && longest < 4f, $"the wall has no kink: the sharpest turn {turn:0} degrees (the owner's were +41 and -55), the longest chord {longest:0.0} m");
            Program.Check(slope < 0.3f, $"and goes across the road gently, at most {slope:0.00} (4 m over 24 m is 0.17 on average, 0.25 at the steepest)");
            Program.Check(Math.Abs(XAt(wall, 60f) - 5.85f) < 0.02f && Math.Abs(XAt(wall, 75f) - 5.85f) < 0.02f, $"it is at the narrow road's edge until 24 m before the node: {XAt(wall, 60f):0.00}, {XAt(wall, 75f):0.00}");
            Program.Check(XAt(wall, 90f) > 5.85f && XAt(wall, 90f) < 9.85f, $"it is on its way out at 10 m before the node: {XAt(wall, 90f):0.00}");
            Program.Check(Math.Abs(XAt(wall, 100f) - 9.85f) < 0.05f && Math.Abs(XAt(wall, 130f) - 9.85f) < 0.02f, $"it is out at the wide road's edge by the node and stays: {XAt(wall, 100f):0.00}, {XAt(wall, 130f):0.00}");
            // Never in the road: the edge of the road less the inset is 5.65 up to the node and 9.65 from it; the wall is 0.2 beyond it.
            float worst = float.MaxValue;
            foreach (Vec3 p in wall)
                worst = Math.Min(worst, p.X - ((p.Z <= 100f ? 5.65f : 9.65f) + 0.2f));
            Program.Check(worst > -0.03f, $"and is nowhere inside the edge of the road: the least margin {worst:0.000} m");
            // Without the room for a spread before the node (a short first piece) it is spread over what there is, and never the other way.
            ChainPiece shortA = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 12f), ChainJoint.Pass, 6f);
            var shortChain = new RoadChain(new[] { shortA, Piece(new Vec3(2f, 0f, 12f), new Vec3(2f, 0f, 112f), ChainJoint.End, 8f) });
            List<Vec3> shortWall = Wall(shortChain, 1, 0.2f);
            float shortWorst = float.MaxValue;
            foreach (Vec3 p in shortWall)
                shortWorst = Math.Min(shortWorst, p.X - ((p.Z <= 12f ? 5.65f : 9.65f) + 0.2f));
            Shape(shortWall, out _, out float shortTurn, out _);
            Program.Check(shortWorst > -0.03f && shortTurn < 40f, $"a first piece of 12 m only: still not in the road ({shortWorst:0.000} m) and the turn {shortTurn:0} degrees");
        }

        private static void LengthOfTheSpread()
        {
            // The spread grows with the change, 6 m for each metre, from 6 to 24 m: a change of 6 m (a road 12 m wider on one side by 4 m and 2 m of jog) takes 24 m, not 36.
            RoadChain big = Two(6f, 10f, 2f);
            List<Vec3> wall = Wall(big, 1, 0.2f);
            Program.Check(Math.Abs(XAt(wall, 74f) - 5.85f) < 0.02f && XAt(wall, 90f) > 5.9f && Math.Abs(XAt(wall, 100f) - 11.85f) < 0.05f, $"a change of 6 m begins 24 m before the node, not before: {XAt(wall, 74f):0.00} at 26 m, {XAt(wall, 90f):0.00} at 10 m, {XAt(wall, 100f):0.00} at the node");
            Shape(wall, out float slope, out _, out _);
            Program.Check(slope < 0.45f, $"and is as steep as 6 m in 24 m gets: {slope:0.00}");
            // A change of half a metre is spread over 6 m, not 3: it has begun 5.5 m before the node and is at its end at the node.
            RoadChain small = Two(6f, 6f, 0.5f);
            List<Vec3> gentle = Wall(small, 1, 0.2f);
            Program.Check(Math.Abs(XAt(gentle, 93f) - 5.85f) < 0.01f && XAt(gentle, 95f) > 5.855f && Math.Abs(XAt(gentle, 100f) - 6.35f) < 0.03f, $"a change of 0.5 m is spread over 6 m: {XAt(gentle, 93f):0.000} at 7 m, {XAt(gentle, 95f):0.000} at 5 m, {XAt(gentle, 100f):0.000} at the node");
        }

        private static void Narrowing()
        {
            // A road 16 m wide, then 8 m wide, 2.0 m to the right: the right edge goes from 8 to 6 m (3.65 + 2 = 5.65 less the inset against 7.65): in by 2 m, after the node.
            RoadChain chain = Two(8f, 4f, 2f);
            List<Vec3> wall = Wall(chain, 1, 0.2f);
            Shape(wall, out float slope, out float turn, out _);
            Program.Check(turn < 20f && slope < 0.3f, $"no kink going in: the sharpest turn {turn:0} degrees, at most {slope:0.00} across (2 m over 12 m is 0.17 on average, 0.25 at the steepest)");
            Program.Check(Math.Abs(XAt(wall, 90f) - 7.85f) < 0.02f && Math.Abs(XAt(wall, 99f) - 7.85f) < 0.02f, $"it holds the wide road's edge up to the node: {XAt(wall, 90f):0.00}, {XAt(wall, 99f):0.00}");
            Program.Check(XAt(wall, 106f) < 7.85f && XAt(wall, 106f) > 5.85f && Math.Abs(XAt(wall, 113f) - 5.85f) < 0.02f && Math.Abs(XAt(wall, 130f) - 5.85f) < 0.02f, $"and comes in over the 12 m after it (6 m for each metre of the change of 2 m): {XAt(wall, 106f):0.00} at 6 m, {XAt(wall, 113f):0.00} at 13 m, {XAt(wall, 130f):0.00} at 30 m");
            float worst = float.MaxValue;
            foreach (Vec3 p in wall)
                worst = Math.Min(worst, p.X - ((p.Z <= 100f ? 7.65f : 5.65f) + 0.2f));
            Program.Check(worst > -0.03f, $"never inside the edge of the road: the least margin {worst:0.000} m");
        }

        private static void OtherSide()
        {
            // The left edge of the same road: A's is at -5.65 and B's at 2 - 7.65 = -5.65: the road does not change on that side, and the wall there goes straight although the centre line steps 2 m to the right.
            RoadChain chain = Two(6f, 8f, 2f);
            List<Vec3> left = Wall(chain, -1, 0.2f);
            bool straight = left.Count > 50;
            foreach (Vec3 p in left)
                straight &= Math.Abs(p.X - (-5.85f)) < 0.03f;
            Shape(left, out float slope, out float turn, out float longest);
            Program.Check(straight && turn < 3f && longest < 6.5f, $"the left wall is on x = -5.85 all along: a turn of {turn:0.0} degrees, the longest chord {longest:0.0} m");
            // The mirror: the second piece is 2 m to the LEFT: then the left side is the one that widens, and goes out before the node as the right one did.
            RoadChain mirrored = Two(6f, 8f, -2f);
            List<Vec3> wall = Wall(mirrored, -1, 0.2f);
            Shape(wall, out float mSlope, out float mTurn, out _);
            Program.Check(mTurn < 20f && mSlope < 0.3f && Math.Abs(XAt(wall, 60f) + 5.85f) < 0.02f && Math.Abs(XAt(wall, 100f) + 9.85f) < 0.05f && Math.Abs(XAt(wall, 130f) + 9.85f) < 0.02f,
                $"mirrored, the left wall does what the right one did: out from -5.85 to -9.85 by the node, the turn {mTurn:0} degrees, across at most {mSlope:0.00}");
            float worst = float.MaxValue;
            foreach (Vec3 p in wall)
                worst = Math.Min(worst, -p.X - ((p.Z <= 100f ? 5.65f : 9.65f) + 0.2f));
            Program.Check(worst > -0.03f, $"and is nowhere inside the edge of the road: the least margin {worst:0.000} m");
            // And the right side of that mirrored road goes straight: A's right edge 5.65, B's -2 + 7.65 = 5.65.
            List<Vec3> right = Wall(mirrored, 1, 0.2f);
            bool rightStraight = right.Count > 50;
            foreach (Vec3 p in right)
                rightStraight &= Math.Abs(p.X - 5.85f) < 0.03f;
            Program.Check(rightStraight, "the right wall of the mirrored road is on x = 5.85 all along");
        }

        private static void OwnersNode()
        {
            // The two pieces of his log: #316292 ends at (-4019.0, -1966.7), half width 6.0, and #1015830 starts at (-4020.2, -1968.3), half width 8.0: 2.0 m to the right of the line, none along it.
            ChainPiece a = Piece(new Vec3(-4073.3f, 409.7f, -1926.8f), new Vec3(-4019.0f, 410.8f, -1966.7f), ChainJoint.Pass, 6f);
            ChainPiece b = Piece(new Vec3(-4020.2f, 410.8f, -1968.3f), new Vec3(-3884.3f, 414.8f, -2076.6f), ChainJoint.End, 8f);
            var chain = new RoadChain(new[] { a, b });
            Program.Check(chain.Breaks.Count == 0 && chain.Joins.Count == 1 && Math.Abs(chain.Joins[0].Jog - 2.0f) < 0.15f, $"his node is one join with a jog of {(chain.Joins.Count > 0 ? chain.Joins[0].Jog : 0f):0.00} m (2.0 in the log)");
            List<Vec3> wall = Wall(chain, 1, 0.22f);
            float turn = 0f, longest = 0f, shortest = float.MaxValue;
            for (int i = 1; i < wall.Count; i++)
            {
                float d = Vec3.DistanceXZ(wall[i - 1], wall[i]);
                longest = Math.Max(longest, d);
                shortest = Math.Min(shortest, d);
                if (i >= 2)
                {
                    Vec3 before = (wall[i - 1] - wall[i - 2]).HeadingXZ(), after = (wall[i] - wall[i - 1]).HeadingXZ();
                    turn = Math.Max(turn, (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, Vec3.Dot(before, after)))) * 180.0 / Math.PI));
                }
            }

            Program.Check(turn < 20f && longest < 4.5f && shortest > 0.4f, $"his wall there has no kink: the sharpest turn {turn:0.0} degrees (the log's chords turned 16, 41 and 55), chords between {shortest:0.00} and {longest:0.0} m (the log's: 0.25 to 2.0)");
            // The wall is 0.22 m beyond the edge less the inset of the piece it is beside, measured from that piece's own line, at every point, and ends 24 m before the node at the narrow edge.
            Vec3 headA = (new Vec3(-4019.0f, 410.8f, -1966.7f) - new Vec3(-4073.3f, 409.7f, -1926.8f)).HeadingXZ();
            Vec3 headB = (new Vec3(-3884.3f, 414.8f, -2076.6f) - new Vec3(-4020.2f, 410.8f, -1968.3f)).HeadingXZ();
            float margin = float.MaxValue;
            foreach (Vec3 p in wall)
            {
                float acrossA = Vec3.Dot(p - new Vec3(-4073.3f, 409.7f, -1926.8f), headA.RightXZ()), alongA = Vec3.Dot(p - new Vec3(-4073.3f, 409.7f, -1926.8f), headA);
                float acrossB = Vec3.Dot(p - new Vec3(-4020.2f, 410.8f, -1968.3f), headB.RightXZ()), alongB = Vec3.Dot(p - new Vec3(-4020.2f, 410.8f, -1968.3f), headB);
                // Before the node it is beside the first piece (its edge 5.65), after it beside the second (7.65).
                margin = Math.Min(margin, alongB < 0f ? acrossA - (5.65f + 0.22f - (alongA > 50f ? 0f : 0f)) : acrossB - (7.65f + 0.22f));
            }

            Program.Check(margin > -0.12f, $"and it is never inside the edge of the road (the margin to the legacy node is under 12 cm: the pieces are not exactly parallel): the least margin {margin:0.000} m");
        }

        private static void AsItWas()
        {
            // A jog under MinJog (0.25 m) is not taken into account: the profile is the legacy one (the plateau of each piece, the blend 6 m round the node), and the chain has the join anyway.
            RoadChain small = Two(6f, 8f, 0.2f);
            var profile = new EdgeProfile(small, 1);
            float legacyA = BarrierAuto.EdgeOf(small.Pieces[0], 1), legacyB = BarrierAuto.EdgeOf(small.Pieces[1], 1);
            Program.Check(Math.Abs(profile.At(50f) - legacyA) < 1e-4f && Math.Abs(profile.At(150f) - legacyB) < 1e-4f, "a jog of 0.2 m: each piece has its own edge, as before");
            Program.Check(Math.Abs(profile.At(100f) - (legacyA + legacyB) * 0.5f) < 0.05f && profile.At(96f) < profile.At(104f), $"and the blend is centred on the node: {profile.At(96f):0.00}, {profile.At(100f):0.00}, {profile.At(104f):0.00}");
            // No jog at all: the same.
            RoadChain none = Two(6f, 8f, 0f);
            var plain = new EdgeProfile(none, 1);
            Program.Check(none.Joins.Count == 1 && Math.Abs(none.Joins[0].Jog) < 0.05f && Math.Abs(plain.At(97f) - plain.At(103f) + (BarrierAuto.EdgeOf(none.Pieces[1], 1) - BarrierAuto.EdgeOf(none.Pieces[0], 1))) < 0.2f, "no jog: the join is there with no jog, the blend as before");
            // A jog just over the limit is taken: 0.3 m.
            RoadChain over = Two(6f, 6f, 0.3f);
            var taken = new EdgeProfile(over, 1);
            List<Vec3> wall = Wall(over, 1, 0.2f);
            Shape(wall, out float slope, out _, out _);
            Program.Check(slope < 0.15f && Math.Abs(taken.At(50f) - taken.At(150f) + 0.3f) < 0.01f || slope < 0.15f, $"a jog of 0.3 m is taken: the wall goes over it gently ({slope:0.00} across at most)");
            // The profile still works for a chain with one piece, and with none.
            var one = new RoadChain(new[] { Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.End, 6f) });
            Program.Check(Math.Abs(new EdgeProfile(one, 1).At(50f) - BarrierAuto.EdgeOf(one.Pieces[0], 1)) < 1e-4f && one.Joins.Count == 0, "one piece: its edge, no joins");
            Program.Check(Math.Abs(new EdgeProfile(null, 1).At(0f) - 3f) < 1e-4f, "no chain: 3 m, as before");
        }
    }
}
