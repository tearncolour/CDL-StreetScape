using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The steps a road's curve needs (Flatten), the bands made at them (SurfaceSection.BandsAt, BandsAdaptive), the zones next to a junction, and the simplified lines (LineSimplify): 0.12.73, pure.</summary>
    internal static class AdaptiveTests
    {
        private const float Eps = 2e-4f;

        public static void RunAll()
        {
            Program.Run("adaptive: Wang's count of known curves, and its limits", Counts);
            Program.Run("adaptive: the polyline of the steps is within the tolerance of the curve (random cubics, plan and height)", Bound);
            Program.Run("adaptive: Bands is what it was, BandsAt with the same places is Bands, and the layout of BandsAdaptive", Layout);
            Program.Run("adaptive: the stretches of a part meet exactly, a shared curve with another list is within the tolerance, a part has one list", Seams);
            Program.Run("adaptive: the zones next to a junction are on a regular fine grid, and only there", Zoned);
            Program.Run("simplify: a line keeps what the tolerances ask of it, its ends and a point every so many metres", Simplified);
            Program.Run("simplify: degenerate lines are safe", Degenerate);
        }

        // ------------------------------------------------------------------ curves

        private static Bezier3 Cubic(float x0, float z0, float x1, float z1, float x2, float z2, float x3, float z3, float y0 = 0f, float y1 = 0f, float y2 = 0f, float y3 = 0f) =>
            new Bezier3(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1), new Vec3(x2, y2, z2), new Vec3(x3, y3, z3));

        /// <summary>The two sides of a road whose centre line is a random cubic: steps of 8 to 30 m turning up to 40 degrees at each control point, heights moving up to 1 m, the sides 1.75 m each way.</summary>
        private static (Bezier3 Left, Bezier3 Right) Road(Random random)
        {
            var c = new Vec3[4];
            double heading = random.NextDouble() * Math.PI * 2.0;
            double x = 0.0, z = 0.0, y = 0.0;
            for (int i = 0; i < 4; i++)
            {
                c[i] = new Vec3((float)x, (float)y, (float)z);
                double length = 8.0 + random.NextDouble() * 22.0;
                heading += (random.NextDouble() - 0.5) * 0.7;
                x += Math.Sin(heading) * length;
                z += Math.Cos(heading) * length;
                y += (random.NextDouble() - 0.5) * 2.0;
            }

            var left = new Vec3[4];
            var right = new Vec3[4];
            for (int i = 0; i < 4; i++)
            {
                Vec3 ahead = (c[Math.Min(3, i + 1)] - c[Math.Max(0, i - 1)]).HeadingXZ();
                Vec3 normal = new Vec3(-ahead.Z, 0f, ahead.X);
                left[i] = c[i] + normal * 1.75f;
                right[i] = c[i] - normal * 1.75f;
            }

            return (new Bezier3(left[0], left[1], left[2], left[3]), new Bezier3(right[0], right[1], right[2], right[3]));
        }

        private static void Counts()
        {
            // A cubic whose control points make the second differences (10, 0, 0) and (-10, 0, 0): n = ceil(sqrt(0.75 * 10 / 0.01)) = ceil(27.39) = 28.
            Bezier3 bend = Cubic(0f, 0f, 0f, 10f, 10f, 20f, 10f, 30f);
            Program.Check(Flatten.Segments(bend, 0.01f, 1f) == 28, "a bend of second difference 10 m at 10 mm: 28 steps: " + Flatten.Segments(bend, 0.01f, 1f));
            // Heights alone: second differences of 2 m: ceil(sqrt(0.75 * 2 / 0.005)) = ceil(17.32) = 18; the plan is straight.
            Bezier3 crest = Cubic(0f, 0f, 0f, 10f, 0f, 20f, 0f, 30f, 0f, 0f, 2f, 2f);
            Program.Check(Flatten.Segments(crest, 1f, 0.005f) == 18, "a crest of second difference 2 m at 5 mm: 18 steps: " + Flatten.Segments(crest, 1f, 0.005f));
            Program.Check(Flatten.Segments(crest, 0.001f, 100f) == 1, "the plan's tolerance does not count the height's second differences");
            Program.Check(Flatten.Segments(bend, 0.01f, 1f, 10) == 10, "never more than max");
            Program.Check(Flatten.Segments(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 3f, 80f)), 0.001f, 0.001f) == 1, "a straight, whatever the tolerance, is one step: " + Flatten.Segments(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 3f, 80f)), 0.001f, 0.001f));
            Program.Check(Flatten.Segments(Cubic(0f, 0f, float.NaN, 10f, 10f, 20f, 10f, 30f), 0.01f, 0.01f) == 1, "a control point that is not a number: one step (the band's finite check drops it)");
            Program.Check(Flatten.Segments(bend, 0f, 0f) == 1 && Flatten.Segments(bend, float.NaN, -1f) == 1, "a tolerance that is not above zero asks for nothing more than one step");
            Program.Check(Flatten.Segments(bend, 0.01f, 1f, 0) == 1, "max below one is one");

            // The pair has the count of the one that needs more, in equal steps.
            Bezier3 straight = Bezier3.Line(new Vec3(3f, 0f, 0f), new Vec3(3f, 0f, 30f));
            float[] t = Flatten.Params(straight, bend, 0.01f, 1f);
            bool equal = t.Length == 29 && t[0] == 0f && t[28] == 1f;
            for (int i = 0; i < t.Length && equal; i++)
                equal &= Math.Abs(t[i] - i / 28f) < 1e-6f;
            Program.Check(equal, "the pair takes the larger count in equal steps from 0 to 1: " + t.Length);
            Program.Check(Flatten.Params(straight, straight, 0.01f, 0.005f).Length == 2, "two straights: the ends only");
        }

        /// <summary>The polyline of a curve at places of the parameter against the curve at 1000 parameters: the worst distance in the plan and in height, at the same parameter (what Wang's bound is about).</summary>
        private static void Deviation(Bezier3 curve, float[] t, out float plan, out float height)
        {
            plan = height = 0f;
            int k = 0;
            for (int s = 0; s <= 1000; s++)
            {
                float u = s / 1000f;
                while (k + 2 < t.Length && t[k + 1] < u)
                    k++;
                float span = t[k + 1] - t[k];
                Vec3 a = curve.Point(t[k]), b = curve.Point(t[k + 1]), on = Vec3.Lerp(a, b, span > 0f ? (u - t[k]) / span : 0f), at = curve.Point(u);
                plan = Math.Max(plan, Vec3.DistanceXZ(on, at));
                height = Math.Max(height, Math.Abs(on.Y - at.Y));
            }
        }

        private static void Bound()
        {
            var random = new Random(20261102);
            int tight = 0;
            float worstPlan = 0f, worstHeight = 0f;
            for (int i = 0; i < 300; i++)
            {
                (Bezier3 left, Bezier3 right) = Road(random);
                float tolXz = i % 3 == 0 ? 0.02f : 0.01f, tolY = i % 3 == 0 ? 0.01f : 0.005f;
                float[] t = Flatten.Params(left, right, tolXz, tolY);
                foreach (Bezier3 curve in new[] { left, right })
                {
                    Deviation(curve, t, out float plan, out float height);
                    if (plan > tolXz + Eps || height > tolY + Eps)
                        tight++;
                    worstPlan = Math.Max(worstPlan, plan / tolXz);
                    worstHeight = Math.Max(worstHeight, height / tolY);
                }

                // A place between the sides (any share of the width) has second differences that are a mix of theirs: the same count holds.
                Bezier3 middle = new Bezier3(Vec3.Lerp(left.A, right.A, 0.37f), Vec3.Lerp(left.B, right.B, 0.37f), Vec3.Lerp(left.C, right.C, 0.37f), Vec3.Lerp(left.D, right.D, 0.37f));
                Deviation(middle, t, out float mp, out float mh);
                if (mp > tolXz + Eps || mh > tolY + Eps)
                    tight++;
            }

            Program.Check(tight == 0, $"300 random roads: every side and a place between them within the tolerance at every parameter ({tight} over; the worst is {worstPlan:0.00} of the plan's and {worstHeight:0.00} of the height's)");
            Program.Check(worstPlan > 0.3f, "and the bound is not a loose one: the worst reaches " + worstPlan.ToString("0.00") + " of the tolerance");
        }

        // ------------------------------------------------------------------ bands

        /// <summary>Bands as it was before 0.12.73 (a copy, to hold the new one to its output).</summary>
        private static List<SurfaceStrip> OldBands(Func<float, Vec3> left, Func<float, Vec3> right, PaintableStretch stretch, int samples, string source)
        {
            var bands = new List<SurfaceStrip>();
            if (left == null || right == null || samples < 1)
                return bands;
            var band = new SurfaceStrip { Source = source };
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vec3 l = left(t), r = right(t);
                band.Add(Vec3.Lerp(l, r, stretch.F0) + new Vec3(0f, stretch.Y, 0f), Vec3.Lerp(l, r, stretch.F1) + new Vec3(0f, stretch.Y, 0f));
            }

            bool finite = true;
            foreach (Vec3 p in band.Left)
                finite &= p.IsFinite;
            foreach (Vec3 p in band.Right)
                finite &= p.IsFinite;
            if (finite)
                bands.Add(band);
            return bands;
        }

        private static bool Same(List<SurfaceStrip> a, List<SurfaceStrip> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int k = 0; k < a.Count; k++)
            {
                if (a[k].Source != b[k].Source || a[k].Left.Count != b[k].Left.Count || a[k].Right.Count != b[k].Right.Count)
                    return false;
                for (int i = 0; i < a[k].Left.Count; i++)
                {
                    if (a[k].Left[i].X != b[k].Left[i].X || a[k].Left[i].Y != b[k].Left[i].Y || a[k].Left[i].Z != b[k].Left[i].Z
                        || a[k].Right[i].X != b[k].Right[i].X || a[k].Right[i].Y != b[k].Right[i].Y || a[k].Right[i].Z != b[k].Right[i].Z)
                        return false;
                }
            }

            return true;
        }

        private static void Layout()
        {
            var random = new Random(3);
            bool same = true, at = true;
            for (int i = 0; i < 40; i++)
            {
                (Bezier3 left, Bezier3 right) = Road(random);
                var stretch = new PaintableStretch((float)random.NextDouble() * 0.4f, 0.6f + (float)random.NextDouble() * 0.4f, (float)random.NextDouble() * 0.2f - 0.1f);
                int samples = 1 + random.Next(30);
                Func<float, Vec3> l = t => left.Point(t), r = t => right.Point(t);
                same &= Same(SurfaceSection.Bands(l, r, stretch, samples, "x"), OldBands(l, r, stretch, samples, "x"));
                var uniform = new float[samples + 1];
                for (int k = 0; k <= samples; k++)
                    uniform[k] = k / (float)samples;
                at &= Same(SurfaceSection.BandsAt(l, r, stretch, uniform, "x"), OldBands(l, r, stretch, samples, "x"));
            }

            Program.Check(same, "Bands gives what it gave before it was made through BandsAt, to the last bit");
            Program.Check(at, "BandsAt at the places of the steps is Bands");
            Program.Check(SurfaceSection.Bands(null, t => Vec3.Zero, new PaintableStretch(0f, 1f, 0f), 4, "x").Count == 0 && SurfaceSection.Bands(t => Vec3.Zero, t => Vec3.Zero, new PaintableStretch(0f, 1f, 0f), 0, "x").Count == 0, "no curve or no step: no band, as before");
            Program.Check(SurfaceSection.BandsAt(t => Vec3.Zero, t => Vec3.Zero, new PaintableStretch(0f, 1f, 0f), new[] { 0f }, "x").Count == 0 && SurfaceSection.BandsAt(t => Vec3.Zero, t => Vec3.Zero, new PaintableStretch(0f, 1f, 0f), null, "x").Count == 0, "fewer than two places: no band");

            (Bezier3 a, Bezier3 b) = Road(new Random(9));
            var half = new PaintableStretch(0.1f, 0.6f, 0.05f);
            List<SurfaceStrip> made = SurfaceSection.BandsAdaptive(a, b, half, 0.01f, 0.005f, "part 7");
            SurfaceStrip band = made.Count == 1 ? made[0] : null;
            Program.Check(band != null && band.Source == "part 7" && band.Left.Count == band.Right.Count && band.Left.Count >= 2, "one band with the source, both sides of one length");
            Vec3 l0 = Vec3.Lerp(a.A, b.A, 0.1f) + new Vec3(0f, 0.05f, 0f), r0 = Vec3.Lerp(a.A, b.A, 0.6f) + new Vec3(0f, 0.05f, 0f);
            Vec3 l1 = Vec3.Lerp(a.D, b.D, 0.1f) + new Vec3(0f, 0.05f, 0f), r1 = Vec3.Lerp(a.D, b.D, 0.6f) + new Vec3(0f, 0.05f, 0f);
            Program.Check(band != null && Vec3.DistanceXZ(band.Left[0], l0) < 1e-5f && Vec3.DistanceXZ(band.Right[0], r0) < 1e-5f && Vec3.DistanceXZ(band.Left[band.Left.Count - 1], l1) < 1e-5f && Vec3.DistanceXZ(band.Right[band.Right.Count - 1], r1) < 1e-5f
                && Math.Abs(band.Left[0].Y - l0.Y) < 1e-5f, "it starts and ends where Bands does, with the stretch's share of the width and its height");
            // A gentle road, whose open part needs few steps: a zone at each end of 12 m at 2 m is what adds steps.
            Bezier3 gentleLeft = Cubic(-3.5f, 0f, -3.5f, 20f, -3f, 40f, -3f, 60f), gentleRight = Cubic(3.5f, 0f, 3.5f, 20f, 4f, 40f, 4f, 60f);
            List<SurfaceStrip> plain = SurfaceSection.BandsAdaptive(gentleLeft, gentleRight, half, 0.01f, 0.005f, "x"), zoned = SurfaceSection.BandsAdaptive(gentleLeft, gentleRight, half, 0.01f, 0.005f, "x", new Zones(true, true));
            Program.Check(plain.Count == 1 && zoned.Count == 1 && zoned[0].Left.Count > plain[0].Left.Count + 6, "BandsAdaptive hands the zones to the places: more steps with a zone at each end than without: " + (zoned.Count == 1 ? zoned[0].Left.Count - 1 : -1) + " against " + (plain.Count == 1 ? plain[0].Left.Count - 1 : -1));
            Bezier3 broken = Cubic(0f, 0f, float.NaN, 10f, 10f, 20f, 10f, 30f);
            Program.Check(SurfaceSection.BandsAdaptive(broken, b, half, 0.01f, 0.005f, "x").Count == 0, "a curve that is not a number makes no band, as with Bands");
        }

        private static void Seams()
        {
            (Bezier3 left, Bezier3 right) = Road(new Random(11));
            float[] t = Flatten.Params(left, right, 0.01f, 0.005f);
            // The stretches of one part are cut at the same places: the edge one has is the edge the next has.
            var first = new PaintableStretch(0f, 0.5f, 0.02f);
            var second = new PaintableStretch(0.5f, 1f, 0.02f);
            SurfaceStrip a = SurfaceSection.BandsAdaptive(left, right, first, 0.01f, 0.005f, "a")[0], b = SurfaceSection.BandsAdaptive(left, right, second, 0.01f, 0.005f, "b")[0];
            bool exact = a.Right.Count == b.Left.Count && a.Right.Count == t.Length;
            for (int i = 0; i < a.Right.Count && exact; i++)
                exact &= a.Right[i].X == b.Left[i].X && a.Right[i].Y == b.Left[i].Y && a.Right[i].Z == b.Left[i].Z;
            Program.Check(exact, "two stretches of one part meet along the same polyline, point for point");

            // A curve shared by two parts that need other counts of their other sides: its two polylines differ by no more than the tolerance (each is within it of the curve).
            Bezier3 shared = Cubic(0f, 0f, 0f, 20f, 8f, 40f, 8f, 60f);
            Bezier3 gentle = Cubic(4f, 0f, 4f, 20f, 4f, 40f, 4f, 60f), sharp = Cubic(-6f, 0f, -14f, 22f, -2f, 44f, 2f, 58f);
            float[] one = Flatten.Params(shared, gentle, 0.01f, 0.005f), other = Flatten.Params(sharp, shared, 0.01f, 0.005f);
            Program.Check(one.Length != other.Length, "the two parts need other counts (" + (one.Length - 1) + " and " + (other.Length - 1) + ")");
            float gap = Gap(shared, one, other);
            Program.Check(gap <= 0.01f + Eps, $"the polylines of the shared curve are {gap * 1000f:0.0} mm apart at most, within the plan's 10 mm");
            Program.Check(gap > 0.0005f, "and they are apart: this is the sliver that a part's own list avoids: " + (gap * 1000f).ToString("0.0") + " mm");

            // A junction part: one list for all its curves, so that its halves meet along the middle line with no sliver.
            float[] part = Flatten.PartParams(new[] { gentle, shared, sharp }, 0.01f, 0.005f);
            Program.Check(Gap(shared, part, part) < 1e-5f, "one list for the part: no gap along the line between its halves");
            int most = Math.Max(Flatten.Segments(gentle, 0.01f, 0.005f), Math.Max(Flatten.Segments(shared, 0.01f, 0.005f), Flatten.Segments(sharp, 0.01f, 0.005f)));
            Program.Check(part.Length - 1 == Math.Max(16, most), "as many steps as the curve that needs most, and 16 at least: " + (part.Length - 1) + " of " + most);
            Program.Check(Flatten.PartParams(new[] { Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 9f)) }, 0.01f, 0.005f).Length == 17, "a straight junction part keeps its 16 steps");
            Program.Check(Flatten.PartParams(new[] { sharp, gentle }, 0.01f, 0.005f).Length == Flatten.PartParams(new[] { gentle, sharp }, 0.01f, 0.005f).Length, "the order of the curves does not matter");
            Program.Check(Flatten.PartParams(new[] { Cubic(0f, 0f, 0f, 10f, 10f, 20f, 10f, 30f) }, 0.01f, 1f, 4, 8).Length == 9, "never more than max, and the floor can be lower");
        }

        /// <summary>The worst distance in the plan from a vertex of one polyline of the curve to the other polyline of it, either way.</summary>
        private static float Gap(Bezier3 curve, float[] a, float[] b)
        {
            float worst = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                float[] from = pass == 0 ? a : b, to = pass == 0 ? b : a;
                foreach (float u in from)
                {
                    Vec3 p = curve.Point(u);
                    float best = float.MaxValue;
                    for (int j = 0; j + 1 < to.Length; j++)
                    {
                        Vec3 q0 = curve.Point(to[j]), q1 = curve.Point(to[j + 1]);
                        float dx = q1.X - q0.X, dz = q1.Z - q0.Z, l2 = dx * dx + dz * dz;
                        float s = l2 > 1e-12f ? Math.Max(0f, Math.Min(1f, ((p.X - q0.X) * dx + (p.Z - q0.Z) * dz) / l2)) : 0f;
                        float ex = q0.X + s * dx - p.X, ez = q0.Z + s * dz - p.Z;
                        best = Math.Min(best, (float)Math.Sqrt(ex * ex + ez * ez));
                    }

                    worst = Math.Max(worst, best);
                }
            }

            return worst;
        }

        // ------------------------------------------------------------------ zones

        private static void Zoned()
        {
            // A gentle road of 60 m (an open part of a few steps) with a junction at its start: 12 m of 2 m steps, then the open road.
            Bezier3 left = Cubic(-3.5f, 0f, -3.5f, 20f, -3f, 40f, -3f, 60f), right = Cubic(3.5f, 0f, 3.5f, 20f, 4f, 40f, 4f, 60f);
            float[] open = Flatten.Params(left, right, 0.01f, 0.005f);
            float[] start = Flatten.Params(left, right, 0.01f, 0.005f, new Zones(true, false));
            float[] end = Flatten.Params(left, right, 0.01f, 0.005f, new Zones(false, true));
            float[] both = Flatten.Params(left, right, 0.01f, 0.005f, new Zones(true, true));
            float length = Math.Max(Length(left), Length(right));
            Program.Check(Increasing(open) && Increasing(start) && Increasing(end) && Increasing(both), "the places rise, from 0 to 1, with or without zones");
            bool exactEnd = true;
            foreach (float zoneLength in new[] { 5f, 7f, 11f, 12f, 13f, 17f })
            {
                foreach (float zoneStep in new[] { 0.7f, 1.3f, 2f, 3.1f })
                    exactEnd &= Flatten.Params(left, right, 0.01f, 0.005f, new Zones(true, true, zoneLength, zoneStep))[Flatten.Params(left, right, 0.01f, 0.005f, new Zones(true, true, zoneLength, zoneStep)).Length - 1] == 1f;
            }

            Program.Check(exactEnd, "the last place is exactly 1 whatever the zone and step (rounding in the steps does not leave it short)");
            Program.Check(Flatten.Params(left, right, 0.01f, 0.005f, Zones.None).Length == open.Length && Flatten.Params(left, right, 0.01f, 0.005f, new Zones(false, false)).Length == open.Length, "no flags, no zone");
            Program.Check(SpacingIn(start, 0f, 12f / length, length) <= 2f + 1e-3f, "the zone at the start has steps of 2 m at most: " + SpacingIn(start, 0f, 12f / length, length).ToString("0.00"));
            Program.Check(SpacingIn(end, 1f - 12f / length, 1f, length) <= 2f + 1e-3f, "the zone at the end the same");
            Program.Check(SpacingIn(start, 12f / length + 0.01f, 1f, length) > 2f, "and the open road beyond it has longer ones: " + SpacingIn(start, 12f / length + 0.01f, 1f, length).ToString("0.0"));
            Program.Check(SpacingIn(end, 0f, 1f - 12f / length - 0.01f, length) > 2f, "also at the other end when only the end has a zone");
            Program.Check(start.Length > open.Length && both.Length > start.Length, "a zone has more steps than none: " + open.Length + ", " + start.Length + ", " + both.Length);
            Program.Check(NearAny(start, 12f / length), "the zone ends at a place, so that the open road's grid begins at it");

            // A curve no longer than its zones is all zone, at the step.
            Bezier3 shortLeft = Cubic(-3.5f, 0f, -3.5f, 3f, -3.5f, 6f, -3.5f, 9f), shortRight = Cubic(3.5f, 0f, 3.5f, 3f, 3.5f, 6f, 3.5f, 9f);
            float[] all = Flatten.Params(shortLeft, shortRight, 0.01f, 0.005f, new Zones(true, false));
            Program.Check(all.Length - 1 == (int)Math.Ceiling(Length(shortLeft) / 2f) && SpacingIn(all, 0f, 1f, Length(shortLeft)) <= 2f + 1e-3f, "a half of 9 m with a zone of 12 m: steps of 2 m all along: " + (all.Length - 1));
            // The longer side sets the zone: the outer side of a bend is longer, and no step of it is longer than the step.
            Bezier3 outer = Cubic(0f, 0f, 0f, 10f, 10f, 20f, 10f, 30f), inner = Cubic(6f, 0f, 6f, 6f, 6.5f, 12f, 6.5f, 18f);
            float[] bend = Flatten.Params(inner, outer, 0.01f, 0.005f, new Zones(true, true, 10f, 2f));
            Program.Check(SpacingIn(bend, 0f, 8f / Length(outer), Length(outer)) <= 2f + 0.05f && SpacingIn(bend, 1f - 8f / Length(outer), 1f, Length(outer)) <= 2f + 0.05f, "the zone is measured on the longer side (here the second one): " + SpacingIn(bend, 0f, 8f / Length(outer), Length(outer)).ToString("0.00"));
            float longer = Length(outer);
            var inside = new List<float>();
            for (int i = 1; i < bend.Length; i++)
            {
                if (bend[i - 1] >= 10f / longer - 1e-5f && bend[i] <= 1f - 10f / longer + 1e-5f)
                    inside.Add(bend[i] - bend[i - 1]);
            }

            bool equalSteps = inside.Count >= 2;
            foreach (float step in inside)
                equalSteps &= Math.Abs(step - inside[0]) < 1e-5f;
            Program.Check(equalSteps, "and the open road between the zones is on equal steps of its own: " + inside.Count + " of them");

            // Zones that mean nothing are not zones.
            Program.Check(Flatten.Params(left, right, 0.01f, 0.005f, new Zones(true, true, 0f, 2f)).Length == open.Length && Flatten.Params(left, right, 0.01f, 0.005f, new Zones(true, true, 12f, 0f)).Length == open.Length, "a zone with no length or no step is none");
            float[] nan = Flatten.Params(Cubic(0f, 0f, float.NaN, 10f, 10f, 20f, 10f, 30f), right, 0.01f, 0.005f, new Zones(true, true));
            Program.Check(nan.Length == 2 && nan[0] == 0f && nan[1] == 1f, "a curve that is not a number is one step, zones or not");
            Program.Check(Flatten.Params(left, right, 0.01f, 0.005f, new Zones(true, true, 1000f, 0.0001f)).Length <= 130, "a step of a tenth of a millimetre does not make a million places");
        }

        private static float Length(Bezier3 c)
        {
            float total = 0f;
            Vec3 last = c.A;
            for (int i = 1; i <= 16; i++)
            {
                Vec3 p = c.Point(i / 16f);
                total += (p - last).Length;
                last = p;
            }

            return total;
        }

        private static bool Increasing(float[] t)
        {
            if (t.Length < 2 || t[0] != 0f || t[t.Length - 1] != 1f)
                return false;
            for (int i = 1; i < t.Length; i++)
            {
                if (!(t[i] > t[i - 1]))
                    return false;
            }

            return true;
        }

        private static bool NearAny(float[] t, float u)
        {
            foreach (float x in t)
            {
                if (Math.Abs(x - u) < 1e-5f)
                    return true;
            }

            return false;
        }

        /// <summary>The longest step (m) between places that are both in from..to, taking the curve as length metres long and the parameter as uniform on it.</summary>
        private static float SpacingIn(float[] t, float from, float to, float length)
        {
            float worst = 0f;
            for (int i = 1; i < t.Length; i++)
            {
                if (t[i - 1] >= from - 1e-6f && t[i] <= to + 1e-6f)
                    worst = Math.Max(worst, (t[i] - t[i - 1]) * length);
            }

            return worst;
        }

        // ------------------------------------------------------------------ lines

        private static void Simplified()
        {
            // A straight line of 100 m sampled every metre: only its ends, and a point every 10 m at most when asked.
            var straight = new List<Vec3>();
            for (int i = 0; i <= 100; i++)
                straight.Add(new Vec3(0f, 0.01f * i, i));
            List<Vec3> two = LineSimplify.Simplify(straight, 0.01f, 0.005f, 1000f);
            Program.Check(two.Count == 2 && two[0].Z == 0f && two[1].Z == 100f, "a straight keeps its two ends: " + two.Count);
            List<Vec3> chords = LineSimplify.Simplify(straight, 0.01f, 0.005f, 10f);
            bool longest = true;
            for (int i = 1; i < chords.Count; i++)
                longest &= (chords[i] - chords[i - 1]).Length <= 10.0001f * 1.01f;
            Program.Check(longest && chords.Count >= 11 && chords.Count <= 21 && chords[0].Z == 0f && chords[chords.Count - 1].Z == 100f, "with a longest chord of 10 m: no chord longer, and about a point every 10 m: " + chords.Count);

            // A bump of 2 cm in the plan in the middle of 20 m: kept at a tolerance of 1 cm, dropped at 5.
            var bump = new List<Vec3>();
            for (int i = 0; i <= 20; i++)
                bump.Add(new Vec3(i == 10 ? 0.02f : 0f, 0f, i));
            Program.Check(LineSimplify.Simplify(bump, 0.01f, 0.01f, 100f).Exists(p => p.X == 0.02f) && LineSimplify.Simplify(bump, 0.05f, 0.01f, 100f).Count == 2, "a bump of 2 cm in the plan: kept at 1 cm, dropped at 5 cm");
            // A step of 2 cm in height.
            var step = new List<Vec3>();
            for (int i = 0; i <= 20; i++)
                step.Add(new Vec3(0f, i == 10 ? 0.02f : 0f, i));
            Program.Check(LineSimplify.Simplify(step, 0.05f, 0.01f, 100f).Exists(p => p.Y == 0.02f) && LineSimplify.Simplify(step, 0.05f, 0.05f, 100f).Count == 2, "a bump of 2 cm in height: kept at 1 cm, dropped at 5 cm, whatever the plan's tolerance");
            // The point nearest is the chord's own end, not the line through it: a point that lies beyond the end of the chord is far from it.
            var past = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 12f), new Vec3(0.001f, 0f, 10f) };
            Program.Check(LineSimplify.Simplify(past, 0.05f, 0.05f, 100f).Count == 3, "a point beyond the end of its chord is measured from the end, not from the line");

            // Random lines: every dropped point is within both tolerances of the chord that replaced it, and the ends are kept.
            var random = new Random(77);
            int outside = 0, total = 0, dropped = 0;
            for (int line = 0; line < 120; line++)
            {
                var points = new List<Vec3>();
                double heading = random.NextDouble() * 6.28, x = 0.0, z = 0.0, y = 0.0, turn = (random.NextDouble() - 0.5) * 0.04;
                for (int i = 0; i < 60; i++)
                {
                    points.Add(new Vec3((float)(x + (random.NextDouble() - 0.5) * 0.004), (float)(y + (random.NextDouble() - 0.5) * 0.004), (float)(z + (random.NextDouble() - 0.5) * 0.004)));
                    heading += turn * (1.0 + random.NextDouble());
                    x += Math.Sin(heading);
                    z += Math.Cos(heading);
                    y += (random.NextDouble() - 0.5) * 0.02;
                }

                float tolXz = line % 2 == 0 ? 0.02f : 0.01f, tolY = line % 2 == 0 ? 0.01f : 0.005f, chord = line % 3 == 0 ? 8f : 16f;
                List<Vec3> kept = LineSimplify.Simplify(points, tolXz, tolY, chord);
                dropped += points.Count - kept.Count;
                // Where each kept point is in the line (the points are all different).
                var at = new List<int> { 0 };
                for (int j = 1; j < kept.Count; j++)
                {
                    int i = at[j - 1] + 1;
                    while (i < points.Count && !(points[i].X == kept[j].X && points[i].Y == kept[j].Y && points[i].Z == kept[j].Z))
                        i++;
                    at.Add(i);
                }

                if (kept[0].X != points[0].X || at[at.Count - 1] != points.Count - 1)
                {
                    outside++;
                    continue;
                }

                for (int j = 1; j < kept.Count; j++)
                {
                    Vec3 from = kept[j - 1], to = kept[j];
                    float dx = to.X - from.X, dz = to.Z - from.Z, l2 = dx * dx + dz * dz;
                    for (int i = at[j - 1] + 1; i < at[j]; i++)
                    {
                        float u = l2 > 1e-12f ? Math.Max(0f, Math.Min(1f, ((points[i].X - from.X) * dx + (points[i].Z - from.Z) * dz) / l2)) : 0f;
                        float ex = from.X + u * dx - points[i].X, ez = from.Z + u * dz - points[i].Z;
                        total++;
                        if (Math.Sqrt(ex * ex + ez * ez) > tolXz + Eps || Math.Abs(points[i].Y - (from.Y + u * (to.Y - from.Y))) > tolY + Eps)
                            outside++;
                    }

                    // A chord over the longest is only where there was no point to split it at.
                    if (at[j] - at[j - 1] >= 2 && (to - from).Length > chord + 1e-3f)
                        outside++;
                }
            }

            Program.Check(outside == 0, $"120 random lines of 60 points: every point is within both tolerances of its chord, the ends are kept and no chord is over the longest ({outside} wrong of {total})");
            Program.Check(dropped > 120 * 20, "and the tolerances do drop points: " + dropped + " of " + 120 * 60);
            Program.Check(LineSimplify.Simplify(straight, 0.01f, 0.005f, 1000f) != straight, "a new list, never the caller's");
        }

        private static void Degenerate()
        {
            var one = new List<Vec3> { new Vec3(1f, 2f, 3f) };
            Program.Check(LineSimplify.Simplify(one, 0.01f, 0.01f, 10f).Count == 1 && LineSimplify.Simplify(new List<Vec3>(), 0.01f, 0.01f, 10f).Count == 0, "one point or none: as it is");
            var two = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 5f) };
            Program.Check(LineSimplify.Simplify(two, 0.01f, 0.01f, 10f).Count == 2, "two points: as they are");
            var same = new List<Vec3>();
            for (int i = 0; i < 8; i++)
                same.Add(new Vec3(4f, 1f, 4f));
            Program.Check(LineSimplify.Simplify(same, 0.01f, 0.01f, 10f).Count == 2, "points all on one place (a line of no length): its two ends");
            var collinear = new List<Vec3>();
            for (int i = 0; i <= 30; i++)
                collinear.Add(new Vec3(i * 0.5f, i * 0.1f, i * 1.5f));
            Program.Check(LineSimplify.Simplify(collinear, 0.001f, 0.001f, 1000f).Count == 2, "collinear points in space: its two ends");
            var broken = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, float.NaN, 5f), new Vec3(0f, 0f, 10f), new Vec3(0f, 0f, 15f) };
            Program.Check(LineSimplify.Simplify(broken, 0.01f, 0.01f, 10f).Count == 4, "a point that is not a number: the line as it is, for its caller to deal with");
            // Only with the NaN's check does it come back whole: without it the comparisons are false and the point is dropped or the line split on it.
            var brokenFlat = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, float.NaN, 5f), new Vec3(0f, 0f, 10f) };
            Program.Check(LineSimplify.Simplify(brokenFlat, 0.01f, 0.01f, 100f).Count == 3, "a NaN between two good points is not dropped as if it were on the chord");
            var line = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 5f), new Vec3(0f, 0f, 10f) };
            Program.Check(LineSimplify.Simplify(line, 0f, 0.01f, 10f).Count == 3 && LineSimplify.Simplify(line, 0.01f, -1f, 10f).Count == 3 && LineSimplify.Simplify(line, 0.01f, 0.01f, float.NaN).Count == 3, "a tolerance or a longest chord that is not a number above zero: nothing is dropped");
            Program.Check(LineSimplify.Simplify(line, 0.01f, 0.01f, 0.0001f).Count == 3, "a longest chord under 0.1 m is 0.1 m: the points that are there are kept, none are made");
            var doubled = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 20f), new Vec3(0f, 0f, 0f) };
            Program.Check(LineSimplify.Simplify(doubled, 0.01f, 0.01f, 100f).Count == 3, "a line that goes out and comes back to its start keeps its turn");
        }
    }
}
