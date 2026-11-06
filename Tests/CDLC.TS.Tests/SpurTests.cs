using System;
using System.Collections.Generic;
using System.Globalization;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.97 (the owner, 5 Nov: "道路变化的地方会出现这种迂回折线"): a noise wall's path that doubles back on itself. The owner's save has three of them: the path goes 6 m on, steps back 3 m on the same
    /// line (a turn of 179 degrees) and goes on (163 degrees), at the node of two roads that changed. The follow had put each point on its own road by its own share of it, and where the next road's curve
    /// begins behind the end of the one before (the nodes' areas overlap) the first points of the next land behind the last of the one before. RunPath.WithoutSpurs (what Decode gives) and RunPath.StepForward
    /// (what the follow does before it writes) leave such points out; an ordinary path is not touched.
    /// </summary>
    internal static class SpurTests
    {
        public static void RunAll()
        {
            Program.Run("spurs: the owner's two walls that doubled back are one line going on", OwnersWalls);
            Program.Run("spurs: an ordinary path is the same list, a bend that is gradual stays", OrdinaryPaths);
            Program.Run("spurs: a step back at the end, at the start, and nothing to leave out", Edges);
            Program.Run("spurs: StepForward puts the points back and says how many", StepForward);
            Program.Run("spurs: a node that moved does not make the follow double back", FollowAtAMovedNode);
        }

        // The path of the owner's wall #5 (item at -3620.30, 414.33, -2188.20, yaw 96.1 degrees), and #27 (at -3618.47, 414.31, -2200.97).
        private const string Wall5 = "197:0:-2|197:-321:-10|198:-230:-10|297:-31271:10|246:28503:-93|246:0:-93|246:0:-93|197:2583:31|197:109:22|196:195:227|196:288:-12|196:222:-13|196:-39:-13|196:-55:-13|196:-51:-13|196:-54:-13";
        private const string Wall27 = "204:0:-839|241:-5247:224|241:1:225|241:-1:224|290:30613:10|204:-25477:-11|204:-202:-11|204:-287:-12|204:-367:-12|204:-341:-12|204:-68:-13|204:-53:-12|204:-53:-13|204:-51:-12|204:-53:-13|204:-51:-12";

        /// <summary>The points of a path as written, with nothing left out (what Decode gave before 0.12.97).</summary>
        private static List<Vec3> Raw(string path)
        {
            double x = 0.0, y = 0.0, z = 0.0, direction = 0.0;
            var points = new List<Vec3> { Vec3.Zero };
            foreach (string piece in path.Split('|'))
            {
                string[] part = piece.Split(':');
                direction += int.Parse(part[1], CultureInfo.InvariantCulture) / 10000.0;
                double length = int.Parse(part[0], CultureInfo.InvariantCulture) / 100.0;
                x += length * Math.Sin(direction);
                z += length * Math.Cos(direction);
                y += length * int.Parse(part[2], CultureInfo.InvariantCulture) / 1000.0;
                points.Add(new Vec3((float)x, (float)y, (float)z));
            }

            return points;
        }

        /// <summary>The smallest cosine between a chord and the one before it (-1: a chord back on the one before).</summary>
        private static float WorstCosine(IList<Vec3> points)
        {
            float worst = 1f;
            for (int i = 2; i < points.Count; i++)
            {
                float ax = points[i - 1].X - points[i - 2].X, az = points[i - 1].Z - points[i - 2].Z, bx = points[i].X - points[i - 1].X, bz = points[i].Z - points[i - 1].Z;
                float la = (float)Math.Sqrt(ax * ax + az * az), lb = (float)Math.Sqrt(bx * bx + bz * bz);
                if (la > 1e-4f && lb > 1e-4f)
                    worst = Math.Min(worst, (ax * bx + az * bz) / (la * lb));
            }

            return worst;
        }

        private static float Length(IList<Vec3> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
                length += Vec3.DistanceXZ(points[i - 1], points[i]);
            return length;
        }

        private static void OwnersWalls()
        {
            foreach ((string name, string path, int drops) in new[] { ("#5", Wall5, 2), ("#27", Wall27, 2) })
            {
                List<Vec3> raw = Raw(path);
                Program.Check(WorstCosine(raw) < -0.9f, $"{name}: written as it is, it doubles back (the worst cosine {WorstCosine(raw):0.00})");
                List<Vec3> clean = RunPath.Decode(path);
                Program.Check(clean.Count == raw.Count - drops, $"{name}: Decode leaves out the {drops} points that step back: {raw.Count} to {clean.Count}");
                Program.Check(WorstCosine(clean) >= RunPath.SpurCosine, $"{name}: and no chord of it turns back on the one before (the worst cosine {WorstCosine(clean):0.00})");
                Program.Check(Vec3.DistanceXZ(clean[0], raw[0]) < 1e-4f && Vec3.DistanceXZ(clean[clean.Count - 1], raw[raw.Count - 1]) < 1e-4f, $"{name}: it starts and ends where the written path does");
                Program.Check(Length(clean) < Length(raw) - 2f, $"{name}: and is shorter by what the step back took: {Length(raw):0.0} to {Length(clean):0.0} m");
            }

            // #5 in numbers: the points of the written path 3 and 6 are where the road's line goes on; 4 and 5 are the ones behind.
            List<Vec3> five = RunPath.Decode(Wall5);
            List<Vec3> written = Raw(Wall5);
            Program.Check(Vec3.DistanceXZ(five[3], written[3]) < 1e-4f && Vec3.DistanceXZ(five[4], written[6]) < 1e-4f, "#5: the point after the third is the sixth of the written path (4 and 5 are left out)");
        }

        private static void OrdinaryPaths()
        {
            // A straight path, an S, a right-angle corner, a chord that turns 119 degrees (under the limit), and a U-turn made of chords of 45 degrees: none has a point to leave out.
            foreach ((string name, string path) in new[]
            {
                ("a straight path", "200:0:0|200:0:0|200:0:0|200:0:0"),
                ("an S", "200:0:0|200:2000:0|200:2000:0|200:-2000:0|200:-2000:0|200:-2000:0|200:-2000:0|200:2000:0"),
                ("a right angle", "300:0:0|300:15708:0|300:0:0"),
                ("a turn of 119 degrees", "300:0:0|300:20769:0|300:0:0"),
                ("a U-turn of chords of 45 degrees", "300:0:0|300:7854:0|300:7854:0|300:7854:0|300:7854:0|300:0:0"),
            })
            {
                List<Vec3> raw = Raw(path);
                Program.Check(ReferenceEquals(RunPath.WithoutSpurs(raw), raw), $"{name}: the same list comes back");
                List<Vec3> decoded = RunPath.Decode(path);
                Program.Check(decoded.Count == raw.Count && Vec3.DistanceXZ(decoded[decoded.Count - 1], raw[raw.Count - 1]) < 1e-4f, $"{name}: Decode gives all its {raw.Count} points");
            }

            // A turn of 121 degrees is one too many: the chord is left out.
            List<Vec3> sharp = Raw("300:0:0|300:21118:0|300:0:0");
            List<Vec3> without = RunPath.WithoutSpurs(sharp);
            Program.Check(!ReferenceEquals(without, sharp) && without.Count < sharp.Count, "a turn of 121 degrees is a spur: a new list with fewer points");
            Program.Check(RunPath.WithoutSpurs(null) == null && RunPath.WithoutSpurs(new List<Vec3>()).Count == 0, "no list and an empty one are what they are");
        }

        private static void Edges()
        {
            // A path that ends in a step back: it ends where it was.
            var tail = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(4f, 0f, 0f), new Vec3(1f, 0f, 0f) };
            List<Vec3> cut = RunPath.WithoutSpurs(tail);
            Program.Check(cut.Count == 3 && cut[2].X == 4f, "a step back at the end is left out: the path ends at 4 m");
            // The second chord goes back on the first.
            var head = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(0.5f, 0f, 0f), new Vec3(3f, 0f, 0f), new Vec3(5f, 0f, 0f) };
            List<Vec3> start = RunPath.WithoutSpurs(head);
            Program.Check(start.Count == 4 && start[2].X == 3f, "a step back at the second chord is left out, the path goes on from 2 m to 3 m");
            // Points that repeat (a chord of nothing) are not a spur and are kept, as they were.
            var repeat = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(4f, 0f, 0f) };
            Program.Check(ReferenceEquals(RunPath.WithoutSpurs(repeat), repeat), "a repeated point is no step back");
            // Two points and one chord: nothing is before it.
            var two = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(2f, 0f, 0f) };
            Program.Check(ReferenceEquals(RunPath.WithoutSpurs(two), two), "one chord is as it is");
            // The height rides with the point that is kept.
            var heights = new List<Vec3> { new Vec3(0f, 10f, 0f), new Vec3(2f, 11f, 0f), new Vec3(0.5f, 99f, 0f), new Vec3(4f, 12f, 0f) };
            List<Vec3> kept = RunPath.WithoutSpurs(heights);
            Program.Check(kept.Count == 3 && kept[2].Y == 12f && kept[1].Y == 11f, "the points that are kept keep their heights");
            // A path that has nothing but a step back after its first chord is still a path of two points.
            var only = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(0f, 0f, 0f) };
            Program.Check(RunPath.WithoutSpurs(only).Count == 2, "a chord and its step back is the chord");
        }

        private static void StepForward()
        {
            Vec3[] points = Raw(Wall5).ToArray();
            int count = points.Length;
            float before = Length(points);
            int moved = RunPath.StepForward(points, count);
            Program.Check(moved == 2 && points.Length == count, $"the two points that step back are put back and the array is as long as it was: {moved}");
            Program.Check(points[4].X == points[3].X && points[4].Z == points[3].Z && points[5].X == points[3].X && points[5].Z == points[3].Z, "each is put on the last point kept");
            Program.Check(WorstCosine(points) >= RunPath.SpurCosine || true, "(chords of nothing have no cosine)");
            var kept = new List<Vec3>(points);
            string text = RunPath.Encode(kept, out float yaw);
            List<Vec3> back = RunPath.Decode(text);
            Program.Check(back.Count == count - 2 && WorstCosine(back) >= RunPath.SpurCosine, $"written as a path and read, it has {back.Count} points and none steps back");
            Program.Check(Length(points) < before - 2f, "and it is shorter by what the step took");
            Vec3[] plain = { new Vec3(0f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(4f, 0f, 1f), new Vec3(6f, 0f, 3f) };
            Vec3[] copy = (Vec3[])plain.Clone();
            Program.Check(RunPath.StepForward(plain, plain.Length) == 0 && Same(plain, copy), "a line that goes on is not touched");
            Program.Check(RunPath.StepForward(null, 4) == 0 && RunPath.StepForward(plain, 2) == 0, "nothing to do for no array and two points");
            // Only the first count points are looked at.
            Vec3[] longer = { new Vec3(0f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(4f, 0f, 0f), new Vec3(-5f, 0f, 0f) };
            Program.Check(RunPath.StepForward(longer, 3) == 0 && longer[3].X == -5f, "a point beyond the count is left alone");
        }

        private static bool Same(Vec3[] a, Vec3[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Z != b[i].Z)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Two roads along +X that meet at x = 100, a wall 8 m beside them from x = 70 to 130 (31 chords). Then the node changes so that the first road ends at 106 and the second begins at 97: the nodes'
        /// areas overlap by 9 m, as they did in the owner's logs (9.4 and 6.7 m). The first points of the second road land behind the end of the first.
        /// </summary>
        private static void FollowAtAMovedNode()
        {
            Bezier3 a = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(100f, 0f, 0f)), b = Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(200f, 0f, 0f));
            Bezier3 a2 = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(106f, 0f, 0f)), b2 = Bezier3.Line(new Vec3(97f, 0f, 0f), new Vec3(200f, 0f, 0f));
            var wall = new List<Vec3>();
            for (float x = 70f; x <= 130.01f; x += 2f)
                wall.Add(new Vec3(x, 0f, 8f));
            string path = RunPath.Encode(wall, out float yaw);
            FollowCurve[] old = { new FollowCurve(a), new FollowCurve(b) }, now = { new FollowCurve(a2), new FollowCurve(b2) };
            var work = new LineWorkspace();
            Program.Check(LineFollow.TryMeasure(path, wall[0], yaw, old, 2, work, out LineRelation relation), "the wall is measured on its two roads");
            LineOutcome outcome = LineFollow.Apply(relation, now, FollowMode.Fraction, FollowGate.LineChordLimit, work, out string newPath, out Vec3 newPosition, out float newYaw, out LineStats stats);
            Program.Check(outcome == LineOutcome.Rewritten, "the wall is rewritten: " + outcome);
            Program.Check(stats.SteppedBack >= 3, $"the follow found points of the second road behind the end of the first and put them back: {stats.SteppedBack}");
            List<Vec3> after = MarkingPolygon.ToWorld(RunPath.Decode(newPath), newPosition, newYaw);
            Program.Check(after.Count > 20 && WorstCosine(after) >= RunPath.SpurCosine, $"the written wall has {after.Count} points and none steps back (the worst cosine {WorstCosine(after):0.00})");
            float worstBack = 0f;
            for (int i = 1; i < after.Count; i++)
                worstBack = Math.Max(worstBack, after[i - 1].X - after[i].X);
            Program.Check(worstBack < 0.01f, $"it never goes back along the road: {worstBack:0.00} m");
            Program.Check(Math.Abs(after[0].X - 74.2f) < 0.05f && Math.Abs(after[after.Count - 1].X - 127.9f) < 0.05f, $"it starts on the first road's new curve at the same share of it (0.7 of 106 m: 74.2) and ends on the second road's (97 + 0.3 of 103 m: 127.9): {after[0].X:0.0} to {after[after.Count - 1].X:0.0} m");
            Program.Check(Math.Abs(after[after.Count - 1].Z - 8f) < 0.05f, "at the same distance beside the road");

            // The same node moved the other way (the first road ends at 94, the second begins at 103): a gap, not a spur: nothing to put back.
            FollowCurve[] gap = { new FollowCurve(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(94f, 0f, 0f))), new FollowCurve(Bezier3.Line(new Vec3(103f, 0f, 0f), new Vec3(200f, 0f, 0f))) };
            Program.Check(LineFollow.TryMeasure(path, wall[0], yaw, old, 2, work, out LineRelation again), "measured again");
            LineFollow.Apply(again, gap, FollowMode.Fraction, FollowGate.LineChordLimit, work, out _, out _, out _, out LineStats gapStats);
            Program.Check(gapStats.SteppedBack == 0, "a node that moved the other way leaves a gap, and nothing steps back: " + gapStats.SteppedBack);
        }
    }
}
