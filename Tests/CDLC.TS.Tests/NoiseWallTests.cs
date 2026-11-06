using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The path a barrier is laid along (0.12.84, the owner's noise walls: "弧度拟合不正确", and a wall along a railway that was wavy with slivers under its base): BarrierPath cuts the points of a line where the
    /// road bends, FootProfile holds the heights of a foot that no surface gives to a grade and a bounded wiggle, and SurfaceHeights.Put uses it for a barrier's line (not for paint).
    /// </summary>
    internal static class NoiseWallTests
    {
        public static void RunAll()
        {
            Program.Run("noise wall: the sag of a chord, and the chord a radius allows", Sag);
            Program.Run("noise wall: a line along a circle is within the tolerance; a straight or gentle one is the points Line gives", Circle);
            Program.Run("noise wall: LineAdaptive on a bend of a chain, measured against the true offset circle", ChainBend);
            Program.Run("noise wall: a moving end leaves the points before it where they are", MovingEnd);
            Program.Run("noise wall: a line carried to a new curve is refined by the map", Refined);
            Program.Run("noise wall: the foot: free points held to a grade and a wiggle, the points on a surface never moved", Foot);
            Program.Run("noise wall: beside a track on an embankment the foot does not jump between the terrain and the line's own height", Embankment);
            Program.Run("noise wall: the points filled between two surfaces are held, not limited", BridgedStaysHeld);
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        /// <summary>The distance of a point from a circle of the radius r about (cx, 0, 0) in the ground plane.</summary>
        private static float Off(Vec3 p, float cx, float r) => Math.Abs((float)Math.Sqrt((p.X - cx) * (p.X - cx) + p.Z * p.Z) - r);

        /// <summary>The worst distance from the circle of the points along every chord of the line (nine on each).</summary>
        private static float Worst(List<Vec3> line, float cx, float r)
        {
            float worst = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                for (int k = 0; k <= 8; k++)
                    worst = Math.Max(worst, Off(Vec3.Lerp(line[i - 1], line[i], k / 8f), cx, r));
            }

            return worst;
        }

        private static void Sag()
        {
            Program.Near(BarrierPath.Sag(2f, 15f), 0.03339f, 1e-4f, "a 2 m chord on 15 m sags 3.3 cm");
            Program.Near(BarrierPath.Sag(2f, 10f), 0.05013f, 1e-4f, "a 2 m chord on 10 m sags 5.0 cm");
            Program.Near(BarrierPath.Sag(3f, 15f), 0.07519f, 1e-4f, "a 3 m post bay on 15 m sags 7.5 cm");
            Program.Near(BarrierPath.Sag(4f, 25f), 0.08013f, 1e-4f, "a 4 m panel on 25 m sags 8.0 cm");
            Program.Check(BarrierPath.Sag(0f, 15f) == 0f && BarrierPath.Sag(2f, 0f) == 0f && BarrierPath.Sag(2f, -3f) == 0f, "no sag without a chord or a radius");
            Program.Check(BarrierPath.Sag(30f, 10f) == 10f, "a chord longer than the circle's diameter sags the radius");
            Program.Near(BarrierPath.ChordFor(17f, 0.03f), 2.0f, 0.02f, "17 m is the radius at which a 2 m chord is within 3 cm");
            Program.Check(BarrierPath.ChordFor(10f, 0.03f) < 1.6f && BarrierPath.ChordFor(10f, 0.03f) > 1.5f, "10 m wants chords of 1.55 m");
            Program.Check(BarrierPath.ChordFor(0.01f, 0.03f) == 0f, "a radius under the tolerance has no chord");
            Program.Near(BarrierPath.Sag(BarrierPath.ChordFor(25f, 0.03f), 25f), 0.03f, 1e-4f, "ChordFor and Sag are inverse");
        }

        private static void Circle()
        {
            // The table of the owner's question (printed): what a line of 2 m chords is from the arc, and what the cut line is.
            foreach (float r in new[] { 10f, 15f, 25f, 50f, 100f })
            {
                Func<float, Vec3> on = s => P(r - r * (float)Math.Cos(s / r), r * (float)Math.Sin(s / r));
                float length = Math.Min(60f, r * 1.5f);
                var uniform = new List<Vec3>();
                for (float s = 0f; s <= length + 1e-3f; s += 2f)
                    uniform.Add(on(s));
                List<Vec3> cut = BarrierPath.Sample(on, 0f, length);
                float before = Worst(uniform, r, r), after = Worst(cut, r, r);
                Console.WriteLine($"  noise wall: radius {r,5:0} m: 2 m chords {before * 100f,5:0.0} cm off the arc ({uniform.Count} points), cut line {after * 100f,5:0.0} cm ({cut.Count} points)");
                Program.Check(after <= BarrierPath.Tolerance + 1e-3f, $"radius {r:0}: the cut line is within the tolerance: {after * 100f:0.0} cm");
                if (r <= 15f)
                    Program.Check(before > BarrierPath.Tolerance && cut.Count > uniform.Count, $"radius {r:0}: 2 m chords are over the tolerance ({before * 100f:0.0} cm) and the line has more points");
                else
                    Program.Check(cut.Count == uniform.Count || cut.Count == uniform.Count + 1, $"radius {r:0}: a gentle bend keeps the points of 2 m chords ({cut.Count} against {uniform.Count})");
            }

            // A straight line: the points Line gives (every 2 m, the last interval joined to the one before it when shorter than half a metre, the end last).
            List<Vec3> straight = BarrierPath.Sample(s => P(0f, s), 3f, 14.3f);
            Program.Check(straight.Count == 7 && straight[0].Z == 3f && straight[6].Z == 14.3f && Math.Abs(straight[1].Z - 5f) < 1e-5f, $"a straight line: a point every 2 m and the end ({straight.Count} points)");
            List<Vec3> backwards = BarrierPath.Sample(s => P(0f, s), 14.3f, 3f);
            Program.Check(backwards.Count == 7 && backwards[0].Z == 14.3f && backwards[6].Z == 3f && Math.Abs(backwards[1].Z - 12.3f) < 1e-5f, "backwards from the larger distance");
            // A last interval of under half a metre is joined to the one before it, as Line does; one of 1.3 m is not.
            Program.Check(BarrierPath.Sample(s => P(0f, s), 0f, 10.3f).Count == 6 && BarrierPath.Sample(s => P(0f, s), 0f, 11.3f).Count == 7, "the last interval of 0.3 m is joined to the one before it, one of 1.3 m is not");
            // The last point is the end itself, to the bit, whatever the start and the length.
            bool exact = true;
            for (int k = 0; k < 400; k++)
            {
                float a = 0.1f + k * 0.37f, b = a + 1.1f + (k % 17) * 0.9f + 0.0131f * k;
                List<Vec3> pts = BarrierPath.Sample(s => P(0f, s), a, b);
                exact &= pts.Count > 1 && pts[pts.Count - 1].Z == b && pts[0].Z == a;
            }

            Program.Check(exact, "the first and the last point are the distances asked for, to the bit");
            Program.Check(BarrierPath.Sample(s => P(0f, s), 0f, 0.5f).Count == 0 && BarrierPath.Sample(s => P(0f, s), 0f, 4000f).Count == 0, "under a metre or over MarkingRuns.MaxLength: nothing");
            Program.Check(BarrierPath.Sample(null, 0f, 10f).Count == 0 && BarrierPath.Sample(s => P(float.NaN, s), 0f, 10f).Count == 0 && BarrierPath.Sample(s => P(0f, s), float.NaN, 10f).Count == 0, "no function, a function of NaN, a start of NaN: nothing");

            // A bend of 4 m radius wants chords of 0.5 m: two cuts of a 2 m interval, never under the shortest.
            Func<float, Vec3> tight = s => P(4f - 4f * (float)Math.Cos(s / 4f), 4f * (float)Math.Sin(s / 4f));
            List<Vec3> around = BarrierPath.Sample(tight, 0f, 8f);
            float shortest = float.MaxValue;
            for (int i = 1; i < around.Count; i++)
                shortest = Math.Min(shortest, Vec3.DistanceXZ(around[i - 1], around[i]));
            Program.Check(Worst(around, 4f, 4f) <= BarrierPath.Tolerance + 1e-3f && shortest >= BarrierPath.MinStep - 1e-4f && around.Count == 17, $"radius 4: within the tolerance with chords of 0.5 m ({around.Count} points, the shortest {shortest:0.00} m)");

            // An interval whose curve crosses its chord at the middle (an S of 10 cm each way over 2 m) is seen by the quarter points only.
            List<Vec3> ess = BarrierPath.Sample(s => P(0.1f * (float)Math.Sin(Math.PI * s), s), 0f, 2f);
            Program.Check(ess.Count > 2, $"an S that is on its chord at the middle is cut ({ess.Count} points)");

            // An S: the middle of each interval is on its chord, the quarter points are not.
            Func<float, Vec3> s2 = s => P((float)Math.Sin(s / 2.5f) * 1.2f, s);
            List<Vec3> wave = BarrierPath.Sample(s2, 0f, 20f);
            var coarse = new List<Vec3>();
            for (float s = 0f; s <= 20f + 1e-3f; s += 2f)
                coarse.Add(s2(s));
            Program.Check(wave.Count > coarse.Count, $"a curve that crosses its chord at the middle is cut (the quarter points see it): {wave.Count} against {coarse.Count} points");
        }

        /// <summary>A circle of the radius r about (r, 0, 0) from the origin heading +Z, as four quarter-turns of a chain of Bezier pieces (the right of the chain's direction is toward the centre).</summary>
        private static RoadChain Circle(float r)
        {
            var pieces = new List<ChainPiece>();
            float k = 4f / 3f * (float)Math.Tan(Math.PI / 8.0) * r;
            for (int q = 0; q < 4; q++)
            {
                double a0 = q * Math.PI / 2.0, a1 = (q + 1) * Math.PI / 2.0;
                Vec3 p0 = P(r - r * (float)Math.Cos(a0), r * (float)Math.Sin(a0)), p1 = P(r - r * (float)Math.Cos(a1), r * (float)Math.Sin(a1));
                Vec3 t0 = P((float)Math.Sin(a0), (float)Math.Cos(a0)), t1 = P((float)Math.Sin(a1), (float)Math.Cos(a1));
                pieces.Add(new ChainPiece { Curve = new Bezier3(p0, p0 + t0 * k, p1 - t1 * k, p1), Reversed = false, Kmh = 50f, HasSpeed = true, After = q == 3 ? ChainJoint.End : ChainJoint.Pass });
            }

            return new RoadChain(pieces);
        }

        private static void ChainBend()
        {
            // The chain's distance is along its centre line: a line at a lateral outside the bend has chords longer than 2 m (2 (R + L) / R), one inside has them shorter. The wall is at 5.5 m to each side.
            const float lateral = 5.5f;
            foreach (float road in new[] { 10f, 15f, 25f, 50f, 100f })
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    float wall = road - side * lateral;
                    RoadChain chain = Circle(road);
                    float to = Math.Min(80f, road * 1.5f);
                    Func<float, float> at = s => side * lateral;
                    List<Vec3> line = chain.Line(10f, 10f + to, side * lateral, 0f), adaptive = chain.LineAdaptive(10f, 10f + to, at, 0f);
                    float before = Worst(line, road, wall), after = Worst(adaptive, road, wall);
                    Console.WriteLine($"  noise wall: road radius {road,5:0} m, wall {(side < 0f ? "outside" : "inside ")} on radius {wall,5:0.0} m: Line {before * 100f,5:0.0} cm ({line.Count} points), LineAdaptive {after * 100f,5:0.0} cm ({adaptive.Count} points)");
                    Program.Check(after <= BarrierPath.Tolerance + 0.02f, $"road {road:0}, side {side:+0;-0}: the line is within the tolerance (and the chain's own 1 m centre samples): {after * 100f:0.0} cm");
                    if (before <= BarrierPath.Tolerance)
                    {
                        bool same = line.Count == adaptive.Count;
                        for (int i = 0; same && i < line.Count; i++)
                            same = line[i].X == adaptive[i].X && line[i].Y == adaptive[i].Y && line[i].Z == adaptive[i].Z;
                        Program.Check(same, $"road {road:0}, side {side:+0;-0}: where the chords are within the tolerance LineAdaptive is Line, point for point");
                    }
                    else if (before > 0.045f)
                    {
                        Program.Check(adaptive.Count > line.Count && after < before, $"road {road:0}, side {side:+0;-0}: a tight bend gets more points and a smaller error ({before * 100f:0.0} to {after * 100f:0.0} cm)");
                    }
                    else
                    {
                        // What is left over 3 cm here is the chain's own centre line, a point a metre (Step) that the line's chords cannot see.
                        Program.Check(adaptive.Count >= line.Count && after <= before + 1e-6f, $"road {road:0}, side {side:+0;-0}: no worse than Line ({before * 100f:0.0} to {after * 100f:0.0} cm)");
                    }
                }
            }

            RoadChain tightRoad = Circle(10f);
            int refinedAll = tightRoad.LineAdaptive(10f, 24f, s => -5.5f, 0f).Count, notLast = tightRoad.LineAdaptive(10f, 24f, s => -5.5f, 0f, refineLast: false).Count;
            Program.Check(notLast < refinedAll, $"through the chain the interval at a moving end is not cut: {notLast} points, {refinedAll} with it");

            // The distances are the chain's: past its end the line stops at the end (it is not carried on at the chain's last point).
            RoadChain whole = Circle(10f);
            List<Vec3> beyond = whole.LineAdaptive(whole.Length - 6f, whole.Length + 40f, s => 0f, 0f);
            Program.Check(beyond.Count > 2 && beyond.Count <= 8 && Vec3.DistanceXZ(beyond[beyond.Count - 1], beyond[beyond.Count - 2]) > 0.4f, $"a line asked for past the chain's end stops at it ({beyond.Count} points, none repeated)");

            RoadChain bend = Circle(30f);
            Program.Check(bend.LineAdaptive(0f, 0.5f, s => 0f, 0f).Count == 0 && bend.LineAdaptive(5f, float.NaN, s => 0f, 0f).Count == 0 && bend.LineAdaptive(0f, 10f, null, 0f).Count == 0, "short, NaN and no lateral: nothing");
        }

        private static void MovingEnd()
        {
            // The tool keeps what it reads under the points of a line whose end follows the cursor by the points' places (SurfaceHeights windows): with the interval at the end not cut, the points before it
            // are the same for every end that has the same seeds; with it cut they are not.
            const float r = 10f;
            Func<float, Vec3> on = s => P(r - r * (float)Math.Cos(s / r), r * (float)Math.Sin(s / r));
            List<Vec3> plain = BarrierPath.Sample(on, 0f, 14f, refineLast: false), cut = BarrierPath.Sample(on, 0f, 14f);
            Program.Check(plain.Count == 14 && cut.Count == 15, $"on 10 m: seven seeds, the end and six cuts without the last interval cut ({plain.Count}), one more with it ({cut.Count})");
            List<Vec3> first = null;
            foreach (float end in new[] { 10.6f, 11f, 11.4f, 11.8f })
            {
                List<Vec3> moving = BarrierPath.Sample(on, 0f, end, refineLast: false);
                Program.Check(moving.Count == 12, $"end at {end:0.0} m: eleven points up to the last seed and the end ({moving.Count})");
                Program.Check(Math.Abs(moving[11].Z - on(end).Z) < 1e-4f, "the last point is the end");
                if (first == null)
                {
                    first = moving;
                    continue;
                }

                bool same = true;
                for (int i = 0; i < 11; i++)
                    same &= moving[i].X == first[i].X && moving[i].Z == first[i].Z;
                Program.Check(same, $"end at {end:0.0} m: the eleven points before the end are those of the first end");
            }

            List<Vec3> refined = BarrierPath.Sample(on, 0f, 10.6f);
            Program.Check(refined.Count == 12 && Worst(refined, r, r) <= BarrierPath.Tolerance + 1e-3f, $"a last interval of 0.6 m needs no cut: {refined.Count} points, within the tolerance");
        }

        private static void Refined()
        {
            // Four points 2.5 m apart on a straight line, carried by a map that bends it into a circle of 10 m: the chords of the plain map are 6 cm off, the refined line is within the tolerance.
            const float r = 10f;
            var old = new List<Vec3>();
            for (int i = 0; i <= 4; i++)
                old.Add(P(0f, i * 2.5f));
            Func<Vec3, Vec3> bend = p => P(r - (r - p.X) * (float)Math.Cos(p.Z / r), (r - p.X) * (float)Math.Sin(p.Z / r));
            var plain = new List<Vec3>();
            foreach (Vec3 p in old)
                plain.Add(bend(p));
            List<Vec3> refined = BarrierPath.Refine(old, bend);
            float before = Worst(plain, r, r), after = Worst(refined, r, r);
            Console.WriteLine($"  noise wall: refine: the plain map {before * 100f:0.0} cm off the arc ({plain.Count} points), refined {after * 100f:0.0} cm ({refined.Count} points)");
            Program.Check(before > BarrierPath.Tolerance && after <= BarrierPath.Tolerance + 1e-3f, "the refined line is within the tolerance where the plain map is not");
            Program.Check(refined.Count > plain.Count, "chords are cut");
            bool kept = true;
            foreach (Vec3 p in plain)
                kept &= refined.Exists(q => q.X == p.X && q.Z == p.Z);
            Program.Check(kept, "every mapped point of the old line is kept");
            List<Vec3> same = BarrierPath.Refine(old, p => p);
            Program.Check(same.Count == old.Count, "an identity map cuts nothing");
            Program.Check(BarrierPath.FitsPath(BarrierPath.Refine(old, p => p)) && !BarrierPath.FitsPath(null) && !BarrierPath.FitsPath(new List<Vec3> { P(0f, 0f) }), "a line of five points is a path, a point and nothing are not");
            var many = new List<Vec3>();
            for (int i = 0; i <= RunPath.MaxPieces + 1; i++)
                many.Add(P(0f, i));
            Program.Check(!BarrierPath.FitsPath(many) && BarrierPath.FitsPath(many.GetRange(0, RunPath.MaxPieces + 1)), $"{RunPath.MaxPieces} pieces (the points one more) are a path, one more piece is not");
            Program.Check(BarrierPath.Refine(null, bend).Count == 0 && BarrierPath.Refine(old, null).Count == 0 && BarrierPath.Refine(new List<Vec3>(), bend).Count == 0, "no points or no map: nothing");
            Program.Check(BarrierPath.Refine(new List<Vec3> { P(0f, 0f) }, bend).Count == 1, "one point is one point");
        }

        private static float Steepest(List<Vec3> line)
        {
            float worst = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                float run = Vec3.DistanceXZ(line[i - 1], line[i]);
                worst = Math.Max(worst, Math.Abs(line[i].Y - line[i - 1].Y) / Math.Max(run, 1e-4f));
            }

            return worst;
        }

        private static List<Vec3> At(params float[] heights)
        {
            var line = new List<Vec3>();
            for (int i = 0; i < heights.Length; i++)
                line.Add(P(i * 2f, 0f, heights[i]));
            return line;
        }

        private static bool[] Held(int n, params int[] at)
        {
            var held = new bool[n];
            foreach (int i in at)
                held[i] = true;
            return held;
        }

        private static void Foot()
        {
            Program.Check(FootProfile.MaxGrade == 0.5f && FootProfile.Wiggle == 0.1f && FootProfile.NoiseMax == 0.5f, "the grade is 0.5, the wiggle 10 cm and a step starts at 50 cm");

            // Flat, a slope within the grade and a gentle wave: bit for bit as given.
            foreach (List<Vec3> line in new[] { At(0f, 0f, 0f, 0f, 0f), At(0f, 0.8f, 1.6f, 2.4f, 3.2f, 4f), At(0f, 0.05f, -0.05f, 0.04f, -0.04f, 0f) })
            {
                List<Vec3> copy = new List<Vec3>(line);
                bool moved = FootProfile.Smooth(copy, new bool[copy.Count]);
                bool same = true;
                for (int i = 0; i < line.Count; i++)
                    same &= copy[i].Y == line[i].Y;
                Program.Check(!moved && same, "a foot within the grade and the wiggle is left as it is");
            }

            // A step of 3 m: the higher side is lowered into a ramp of the grade, the lower side stays (the wall sinks into the ground and never floats over it), whichever way the step goes.
            List<Vec3> up = At(-3f, -3f, -3f, -3f, 0f, 0f, 0f, 0f, 0f);
            bool movedUp = FootProfile.Smooth(up, new bool[up.Count]);
            Program.Check(movedUp && up[3].Y == -3f && Math.Abs(up[4].Y + 2f) < 1e-4f && Math.Abs(up[5].Y + 1f) < 1e-4f && up[6].Y == 0f && Math.Abs(Steepest(up) - 0.5f) < 1e-4f, $"a step up of 3 m is a ramp of 0.5 on the higher side ({up[4].Y:0.0}, {up[5].Y:0.0}, {up[6].Y:0.0})");
            List<Vec3> down = At(0f, 0f, 0f, 0f, -3f, -3f, -3f, -3f, -3f);
            FootProfile.Smooth(down, new bool[down.Count]);
            Program.Check(down[8].Y == -3f && Math.Abs(down[3].Y + 2f) < 1e-4f && Math.Abs(down[2].Y + 1f) < 1e-4f && down[1].Y == 0f && Math.Abs(Steepest(down) - 0.5f) < 1e-4f, $"and a step down is the same ramp on its higher (left) side ({down[1].Y:0.0}, {down[2].Y:0.0}, {down[3].Y:0.0})");

            // A spike at the first or the last point does not drag the line with it; a single dip is widened into a valley at the grade and never filled.
            List<Vec3> first = At(9f, 0f, 0f, 0f, 0f);
            FootProfile.Smooth(first, new bool[5]);
            Program.Check(Math.Abs(first[0].Y - 1f) < 1e-4f && first[1].Y == 0f && first[4].Y == 0f, "a spike at the start is cut to the grade, the rest not lifted");
            List<Vec3> last = At(0f, 0f, 0f, 0f, 9f);
            FootProfile.Smooth(last, new bool[5]);
            Program.Check(Math.Abs(last[4].Y - 1f) < 1e-4f && last[0].Y == 0f, "a spike at the end is cut to the grade");
            List<Vec3> dip = At(0f, 0f, -5f, 0f, 0f);
            FootProfile.Smooth(dip, new bool[5]);
            Program.Check(dip[2].Y == -5f && Math.Abs(dip[1].Y + 4f) < 1e-4f && Math.Abs(dip[0].Y + 3f) < 1e-4f && Math.Abs(dip[3].Y + 4f) < 1e-4f && Steepest(dip) <= 0.5f + 1e-4f, "a dip is widened into a valley of the grade, its bottom where it was");

            // A ripple (a point 10 to 50 cm from the straight line between its neighbours) is brought to 10 cm; a long zigzag needs several rounds.
            List<Vec3> zig = At(0f, 0.2f, -0.2f, 0.2f, -0.2f, 0.2f, -0.2f, 0.2f, -0.2f, 0.2f, -0.2f, 0.2f, 0f);
            FootProfile.Smooth(zig, new bool[zig.Count]);
            float worst = 0f;
            for (int i = 1; i + 1 < zig.Count; i++)
                worst = Math.Max(worst, Math.Abs(zig[i].Y - (zig[i - 1].Y + zig[i + 1].Y) * 0.5f));
            Program.Check(worst <= 0.1f + 0.02f && zig[0].Y == 0f, $"a zigzag of 0.2 m each way is within 10 cm of the straight line between neighbours: {worst:0.000} m");
            // One ripple of 30 cm between two flat neighbours is brought to exactly the wiggle above the straight line, up or down.
            List<Vec3> one = At(0f, 0.3f, 0f);
            FootProfile.Smooth(one, new bool[3]);
            List<Vec3> oneDown = At(0f, -0.3f, 0f);
            FootProfile.Smooth(oneDown, new bool[3]);
            Program.Check(Math.Abs(one[1].Y - 0.1f) < 1e-5f && Math.Abs(oneDown[1].Y + 0.1f) < 1e-5f, $"a ripple of 30 cm is brought to 10 cm, up and down ({one[1].Y:0.000}, {oneDown[1].Y:0.000})");
            // Next to a held point a ripple is not touched: the held height is a fact, and the free point is judged by free neighbours only.
            List<Vec3> beside = At(0f, 0.3f, 0f, 0f);
            FootProfile.Smooth(beside, Held(4, 0));
            Program.Check(beside[1].Y == 0.3f, "a free point next to a held one keeps its ripple");
            // Ripples that are not a regular zigzag need several rounds: two profiles of 14 random points within 40 cm (seeds 2 and 335 of System.Random), on which one round leaves 35 and 57 cm of the line
            // and the rounds bring it to the wiggle.
            foreach (int seed in new[] { 2, 335 })
            {
                var r2 = new Random(seed);
                var hh = new List<float>();
                for (int i = 0; i < 14; i++)
                    hh.Add((float)(r2.NextDouble() * 0.8 - 0.4));
                List<Vec3> one1 = At(hh.ToArray()), many = At(hh.ToArray());
                FootProfile.Smooth(one1, new bool[14], 1);
                FootProfile.Smooth(many, new bool[14]);
                float wo = 0f, wm = 0f;
                for (int i = 1; i + 1 < 14; i++)
                {
                    wo = Math.Max(wo, Math.Abs(one1[i].Y - (one1[i - 1].Y + one1[i + 1].Y) * 0.5f));
                    wm = Math.Max(wm, Math.Abs(many[i].Y - (many[i - 1].Y + many[i + 1].Y) * 0.5f));
                }

                Program.Check(wo > 0.3f && wm <= 0.1f + 1e-3f, $"random profile {seed}: one round leaves {wo:0.00} m, the rounds {wm:0.00} m");
            }

            List<Vec3> small = At(0f, 0f, 0f, 0.6f, 0.6f, 0.6f);
            FootProfile.Smooth(small, new bool[6]);
            Program.Check(small[0].Y == 0f && small[2].Y > 0.1f && small[3].Y < 0.55f && small[5].Y == 0.6f, $"a step of 60 cm is a ripple (each corner is 30 cm from the straight line): rounded into a ramp ({small[2].Y:0.00}, {small[3].Y:0.00}, {small[4].Y:0.00})");
            List<Vec3> corner = At(0f, 0f, 0f, 1.2f, 1.2f, 1.2f);
            FootProfile.Smooth(corner, new bool[6]);
            Program.Check(corner[2].Y == 0f && Math.Abs(corner[3].Y - 1f) < 1e-4f && corner[4].Y == 1.2f, "a step of 1.2 m (each corner 60 cm from the line, over NoiseMax) is not rounded: the higher side is lowered to the grade, the lower not raised");

            // The points on a surface never move, whatever their neighbours do; where a line leaves a surface for the terrain the step stays.
            List<Vec3> pinned = At(0f, 5f, 0f, 5f, 0f, 5f);
            FootProfile.Smooth(pinned, Held(6, 0, 1, 2, 3, 4, 5));
            Program.Check(pinned[1].Y == 5f && pinned[2].Y == 0f && pinned[5].Y == 5f, "held points (all of them) are not moved");
            List<Vec3> leaves = At(0f, 0f, -2f, -2f, -2f, -2f);
            bool movedLeaves = FootProfile.Smooth(leaves, Held(6, 0, 1));
            Program.Check(!movedLeaves && leaves[2].Y == -2f && leaves[1].Y == 0f, "a free run that starts 2 m below the surface it left keeps the step");
            List<Vec3> inside = At(0f, 0f, 0f, 3f, 3f, 3f, 0f);
            bool movedInside = FootProfile.Smooth(inside, Held(7, 0, 6));
            Program.Check(movedInside && inside[0].Y == 0f && inside[6].Y == 0f && inside[1].Y == 0f && inside[2].Y == 0f && inside[5].Y == 3f && Math.Abs(inside[3].Y - 1f) < 1e-4f && Math.Abs(inside[4].Y - 2f) < 1e-4f, $"a step between free neighbours is a ramp ({inside[3].Y:0.0}, {inside[4].Y:0.0}); the points next to held ones keep their heights");

            // Nothing to do or nothing to say.
            Program.Check(!FootProfile.Smooth(null, new bool[2]) && !FootProfile.Smooth(At(0f), new bool[1]) && !FootProfile.Smooth(At(0f, 1f), null) && !FootProfile.Smooth(At(0f, 9f, 0f), new bool[2]), "a missing line, a point, no flags, flags of the wrong number: nothing");
            List<Vec3> xz = At(0f, 4f, 0f);
            xz[1] = P(xz[0].X, xz[0].Z, 4f);
            FootProfile.Smooth(xz, new bool[3]);
            Program.Check(xz.TrueForAll(p => !float.IsNaN(p.Y) && !float.IsInfinity(p.Y)), "points at the same place stay finite");
        }

        private static void BridgedStaysHeld()
        {
            // A road at 0 m, a hole, a road 8 m higher: the surface is taken up to NearSurface (2.5 m) beyond its edge, so the line is on it at x 100 and 110, and over the 10 m between them it is the straight line
            // (0.12.60), grade 0.8 and over the foot's 0.5: not a ramp the foot's limit makes of it.
            var low = new SurfaceStrip();
            for (float x = -100f; x <= 99f; x += 2f)
                low.Add(new Vec3(x, 0f, -5f), new Vec3(x, 0f, 5f));
            var high = new SurfaceStrip();
            for (float x = 111f; x <= 300f; x += 2f)
                high.Add(new Vec3(x, 8f, -5f), new Vec3(x, 8f, 5f));
            var strips = new List<SurfaceStrip> { low, high };
            SurfaceHeights.ReadSurface read = (minX, minZ, maxX, maxZ, originY) => AreaFit.FromStrips(strips, new Vec3(0f, originY, 0f), 0f, minX, maxX, minZ, maxZ);
            var line = new List<Vec3>();
            for (int i = 0; i <= 75; i++)
                line.Add(P(i * 2f, 0f, i * 2f < 104f ? 0f : 8f));
            List<Vec3> put = new SurfaceHeights().Put(line, "bridge", line.Count, read, (x, y, z) => float.NaN);
            float worst = 0f;
            foreach (Vec3 p in put)
            {
                if (p.X > 100f && p.X < 110f)
                    worst = Math.Max(worst, Math.Abs(p.Y - 8f * (p.X - 100f) / 10f));
            }

            Program.Check(worst < 0.05f, $"the points filled between two surfaces keep the straight line between them: {worst * 100f:0.0} cm off");
        }

        private static void Embankment()
        {
            // A wall 5.5 m from the centre line of a track whose pieces are 4 m wide (the surface is 3.5 m inside the foot: farther than SurfaceHeights.NearSurface), on an embankment 1:1.5: the terrain 3.5 m
            // beyond the edge is 2.33 m under the track; where the track bends out by 0.2 m the terrain is 3.0 m under it and the old rule (the terrain's when within MaxGroundStep, else the line's own height)
            // gave 2.5 m apart for two neighbours. The terrain here is a zigzag of cells too.
            List<Vec3> line = new List<Vec3>();
            for (int i = 0; i < 60; i++)
                line.Add(P(i * 2f, 5.5f, 0f));
            Func<float, float, float, float> ground = (x, y, z) =>
            {
                int i = (int)Math.Round(x / 2f);
                float sway = (i / 6) % 2 == 0 ? 0f : 0.9f;
                return -2.45f - sway - ((i % 2 == 0) ? 0.08f : -0.08f);
            };
            SurfaceHeights.ReadSurface none = (a, b, c, d, e) => null;
            List<Vec3> paint = new SurfaceHeights().Put(line, "paint", line.Count, none, ground, bridge: false);
            List<Vec3> wall = new SurfaceHeights().Put(line, "wall", line.Count, none, ground);
            float paintSteep = Steepest(paint), wallSteep = Steepest(wall);
            Console.WriteLine($"  noise wall: embankment: the steepest step between neighbours, paint {paintSteep:0.00} (as before), wall {wallSteep:0.00} (a step of {paintSteep * 2f:0.0} m in 2 m)");
            Program.Check(paintSteep > 1.0f, $"the old rule steps {paintSteep * 2f:0.0} m between two neighbours 2 m apart: the test has what it is about");
            Program.Check(wallSteep <= 0.5f + 1e-3f, $"the wall's foot is within the grade: {wallSteep:0.00}");
            bool inside = true;
            foreach (Vec3 p in wall)
                inside &= p.Y <= 0f + 1e-4f && p.Y >= -3.5f;
            Program.Check(inside, "and stays between the track's height and the terrain's");

            // On a surface nothing changes: a line wholly on road is what Put gave (the other tests of SurfaceHeights hold the numbers); here a line with no terrain at all keeps its heights.
            List<Vec3> flat = new List<Vec3>();
            for (int i = 0; i < 20; i++)
                flat.Add(P(i * 2f, 5.5f, 1.25f));
            List<Vec3> kept = new SurfaceHeights().Put(flat, "flat", flat.Count, none, (x, y, z) => float.NaN);
            Program.Check(kept.TrueForAll(p => p.Y == 1.25f), "no terrain and no surface: the line's own height, as before");
        }
    }
}
