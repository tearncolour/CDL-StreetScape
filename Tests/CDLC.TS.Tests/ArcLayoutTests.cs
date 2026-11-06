using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    internal static class ArcLayoutTests
    {
        public static void RunAll()
        {
            Program.Run("arc: a circle fitted to the points of a facade", Fitting);
            Program.Run("arc: points that cannot make a circle stay flat, with the reason", Refusals);
            Program.Run("arc: noisy points and a point that is far off", Noise);
            Program.Run("arc: the surface puts a point at the same angle on every layer", Surface);
            Program.Run("arc: a point on the surface is read back to the layout", Inverse);
            Program.Run("arc: a rail is cut into chords that keep to the arc", Chords);
            Program.Run("arc: a glyph is set on the surface by its middle", Glyphs);
        }

        /// <summary>Points on a circle of radius r whose centre is (0, centreZ) seen from above, over an angle span about the nearest point to the viewer (outward +Z for a convex wall).</summary>
        private static List<(float X, float Z)> Circle(double r, double centreZ, double span, int n, bool convex = true)
        {
            var points = new List<(float, float)>();
            for (int i = 0; i < n; i++)
            {
                double a = -span / 2 + span * i / (n - 1);
                double x = r * Math.Sin(a);
                double z = convex ? centreZ + r * Math.Cos(a) : centreZ - r * Math.Cos(a);
                points.Add(((float)x, (float)z));
            }

            return points;
        }

        private static void Fitting()
        {
            // A round tower seen from outside (the viewer at +Z): the wall bulges toward him, the centre is behind it.
            ArcFitResult tower = ArcFit.Fit(Circle(12, -12, 0.8, 7), 0f, 1f, 6f);
            Program.Check(tower.Ok && tower.Why == "", "a tower is fitted: " + tower.Why);
            Program.Near(tower.Radius, 12f, 0.01f, "a convex wall has a positive radius of 12: " + tower.Radius);
            Program.Check(tower.Residual < 1e-3f, "the residual is nothing for exact points: " + tower.Residual);

            // A concave front: the centre is on the viewer's side.
            ArcFitResult concave = ArcFit.Fit(Circle(12, 12, 0.8, 7, false), 0f, 1f, 6f);
            Program.Check(concave.Ok, "a concave front is fitted: " + concave.Why);
            Program.Near(concave.Radius, -12f, 0.01f, "a concave wall has a negative radius: " + concave.Radius);

            // The viewer on the other side turns the sign.
            ArcFitResult behind = ArcFit.Fit(Circle(12, -12, 0.8, 7), 0f, -1f, 6f);
            Program.Near(behind.Radius, -12f, 0.01f, "seen from behind the same points are concave: " + behind.Radius);

            // Three points are enough; far from the origin the fit is as good (the sums are centred).
            ArcFitResult three = ArcFit.Fit(Circle(30, -30, 0.4, 3).Select(p => (p.X + 5000f, p.Z + 4000f)).ToList(), 0f, 1f, 6f);
            Program.Check(three.Ok && Math.Abs(three.Radius - 30f) < 0.5f, "three points far from the origin: " + three.Radius + " " + three.Why);

            // The radius bounds and the sagitta test.
            Program.Check(!ArcFit.Fit(Circle(1.5, -1.5, 2.4, 9), 0f, 1f, 1f).Ok, "a circle of 1.5 m is tighter than the bound");
            ArcFitResult wide = ArcFit.Fit(Circle(250, -250, 0.1, 9), 0f, 1f, 6f);
            Program.Check(!wide.Ok && wide.Why.Contains("flat enough"), "a circle of 250 m is flat: " + wide.Why);
            Program.Check(!ArcFit.Fit(Circle(150, -150, 0.12, 9), 0f, 1f, 3f).Ok, "R = 150 under a 3 m board is 7 mm off a plane: flat");
            Program.Check(ArcFit.Fit(Circle(150, -150, 0.12, 9), 0f, 1f, 12f).Ok, "R = 150 under a 12 m board is 120 mm off a plane: bent");
            Program.Near((float)ArcFit.Sagitta(100f, 10f), 0.125f, 1e-3f, "the sagitta of a 10 m chord on R = 100");
            Program.Near((float)ArcFit.Sagitta(-100f, 10f), 0.125f, 1e-3f, "the same for a concave wall");
            Program.Check(ArcFit.Sagitta(3f, 100f) == 3.0, "a chord wider than the circle is the radius");
        }

        private static void Refusals()
        {
            var line = new List<(float, float)> { (0f, 0f), (1f, 0f), (2f, 0f), (3f, 0f) };
            Program.Check(ArcFit.Fit(line, 0f, 1f, 3f).Why.Contains("line"), "points on a line stay flat: " + ArcFit.Fit(line, 0f, 1f, 3f).Why);
            ArcFitResult two = ArcFit.Fit(new List<(float, float)> { (0f, 0f), (3f, 0f) }, 0f, 1f, 3f);
            Program.Check(!two.Ok && two.Why.Contains("fewer than 3"), "two points do not make a circle: " + two.Why);
            Program.Check(!ArcFit.Fit(null, 0f, 1f, 3f).Ok && !ArcFit.Fit(new List<(float, float)>(), 0f, 1f, 3f).Ok, "no points");
            string tight = ArcFit.Fit(Circle(12, -12, 0.05, 5), 0f, 1f, 3f).Why;
            Program.Check(tight.Contains("spread"), "points over 0.6 m are too few to tell: " + tight);
            Program.Check(!ArcFit.Fit(Circle(12, -12, 0.8, 7), 0f, 0f, 3f).Ok, "no outward direction: flat");
            var nan = new List<(float, float)> { (float.NaN, 0f), (1f, 0f), (float.PositiveInfinity, 1f), (2f, 0.5f) };
            Program.Check(!ArcFit.Fit(nan, 0f, 1f, 3f).Ok, "points that are not numbers do not count");
            // Points that fit no circle: a zigzag.
            var zigzag = new List<(float, float)>();
            for (int i = 0; i < 9; i++)
                zigzag.Add((i * 0.4f, i % 2 == 0 ? 0f : 0.5f));
            ArcFitResult bad = ArcFit.Fit(zigzag, 0f, 1f, 3f);
            Program.Check(!bad.Ok && bad.Residual > ArcFit.MaxResidual * 0.5f, "a zigzag fits no circle: " + bad.Why);
        }

        private static void Noise()
        {
            var random = new Random(7);
            List<(float, float)> noisy = Circle(20, -20, 0.6, 15).Select(p => (p.X + (float)(random.NextDouble() - 0.5) * 0.02f, p.Z + (float)(random.NextDouble() - 0.5) * 0.02f)).ToList();
            ArcFitResult fit = ArcFit.Fit(noisy, 0f, 1f, 6f);
            Program.Check(fit.Ok && Math.Abs(fit.Radius - 20f) < 3f, "1 cm of noise: R = " + fit.Radius + " " + fit.Why);

            // One point 1 m off (a ray that hit a balcony): it is dropped at the second pass.
            List<(float X, float Z)> outlier = Circle(20, -20, 0.6, 15);
            outlier[7] = (outlier[7].X, outlier[7].Z + 1f);
            ArcFitResult dropped = ArcFit.Fit(outlier, 0f, 1f, 6f);
            Program.Check(dropped.Ok && Math.Abs(dropped.Radius - 20f) < 1f, "an outlier is dropped: R = " + dropped.Radius + " " + dropped.Why);
            Program.Check(dropped.Kept == 14 && fit.Kept >= 14, "and only that one: " + dropped.Kept + " of 15 kept");

            // Half the points off: no circle.
            List<(float X, float Z)> mess = Circle(20, -20, 0.6, 10);
            for (int i = 0; i < 10; i += 2)
                mess[i] = (mess[i].X, mess[i].Z + 0.4f * (i % 4 == 0 ? 1 : -1));
            Program.Check(!ArcFit.Fit(mess, 0f, 1f, 6f).Ok, "half of the points off by 40 cm: no circle");

            // At most 40 % of the points are dropped: half of them far off (alternately 2 m each way) is no circle, or one fitted to at least 60 % of the points.
            List<(float X, float Z)> half = Circle(20, -20, 0.6, 10);
            for (int i = 0; i < 10; i += 2)
                half[i] = (half[i].X, half[i].Z + (i % 4 == 0 ? 2f : -2f));
            ArcFitResult limited = ArcFit.Fit(half, 0f, 1f, 6f);
            Program.Check(!limited.Ok || limited.Kept >= 6, "the cap on dropped points: " + limited.Ok + " kept " + limited.Kept);

            // For any cloud, a fit that is accepted was made of at least 60 % of the points.
            var cloud = new Random(11);
            int accepted = 0, tooFew = 0;
            for (int trial = 0; trial < 300; trial++)
            {
                int n = 6 + cloud.Next(10);
                List<(float X, float Z)> points = Circle(20, -20, 0.5 + cloud.NextDouble() * 0.3, n);
                int bad = (int)Math.Round(n * (0.4 + cloud.NextDouble() * 0.3));
                var order = Enumerable.Range(0, n).OrderBy(_ => cloud.Next()).ToList();
                for (int k = 0; k < bad; k++)
                {
                    int at = order[k];
                    float off = (float)((0.4 + cloud.NextDouble() * 2.5) * (cloud.Next(2) == 0 ? 1 : -1));
                    points[at] = (points[at].X, points[at].Z + off);
                }

                ArcFitResult r = ArcFit.Fit(points, 0f, 1f, 6f);
                if (!r.Ok)
                    continue;
                accepted++;
                if (r.Kept < (int)Math.Ceiling(n * 0.6))
                    tooFew++;
            }

            Program.Check(tooFew == 0, "no accepted fit is made of under 60 % of the points: " + tooFew + " of " + accepted + " accepted");

            // Twelve points, five of them far off by a metre to a hundred: five drops would leave seven, under the share (eight): refused, or fitted to eight or more.
            List<(float X, float Z)> twelve = Circle(20, -20, 0.6, 12);
            float[] far = { 100f, -60f, 30f, -20f, 10f };
            for (int i = 0; i < 5; i++)
                twelve[i * 2 + 1] = (twelve[i * 2 + 1].X, twelve[i * 2 + 1].Z + far[i]);
            ArcFitResult twelveFit = ArcFit.Fit(twelve, 0f, 1f, 6f);
            Program.Check(!twelveFit.Ok || twelveFit.Kept >= 8, "five of twelve far off: refused or fitted to eight or more: " + twelveFit.Ok + " kept " + twelveFit.Kept);

            // Outliers of graded size (a geometric run from 0.9 m down to 0.1 m, in a random order): each in turn is the farthest and dominates the fit, so the loop would go on dropping; the cap
            // stops it. Over many clouds, the drop that goes past the cap happens, and a fit that is accepted still has 60 % of the points.
            var graded = new Random(5);
            int pastCap = 0, gradedAccepted = 0, gradedShort = 0;
            for (int trial = 0; trial < 1500; trial++)
            {
                int n = 8 + graded.Next(9);
                List<(float X, float Z)> points = Circle(20, -20, 0.5 + graded.NextDouble() * 0.3, n);
                int bad = (int)Math.Round(n * (0.4 + graded.NextDouble() * 0.3));
                var order = Enumerable.Range(0, n).OrderBy(_ => graded.Next()).ToList();
                for (int k = 0; k < bad; k++)
                {
                    double size = 0.9 * Math.Pow(0.1 / 0.9, bad <= 1 ? 0.0 : k / (double)(bad - 1));
                    points[order[k]] = (points[order[k]].X, points[order[k]].Z + (float)(size * (graded.Next(2) == 0 ? 1 : -1)));
                }

                ArcFitResult r = ArcFit.Fit(points, 0f, 1f, 6f);
                if (r.Ok)
                {
                    gradedAccepted++;
                    if (r.Kept < (int)Math.Ceiling(n * 0.6))
                        gradedShort++;
                    if (r.Kept == (int)Math.Ceiling(n * 0.6))
                        pastCap++;
                }
            }

            Program.Check(gradedShort == 0, "graded outliers: no accepted fit under 60 % of the points: " + gradedShort + " of " + gradedAccepted);
            Program.Check(pastCap > 0, "graded outliers: the cap is reached by some fits (else this test is of nothing): " + pastCap + " of " + gradedAccepted);

            // Five of ten far off, each by its own distance: dropped one at a time they would leave five points, which is under the share kept: the cloud is refused.
            List<(float X, float Z)> five = Circle(20, -20, 0.6, 10);
            float[] offs = { 0.8f, -1.5f, 2.2f, -0.6f, 3.1f };
            for (int i = 0; i < 5; i++)
                five[i * 2] = (five[i * 2].X, five[i * 2].Z + offs[i]);
            ArcFitResult fiveFit = ArcFit.Fit(five, 0f, 1f, 6f);
            Program.Check(!fiveFit.Ok || fiveFit.Kept >= 6, "half of the points far off: refused or fitted to six or more: " + fiveFit.Ok + " kept " + fiveFit.Kept);
        }

        private static void Surface()
        {
            foreach (float radius in new[] { 12f, -12f, 40f, -40f })
            {
                var surface = new ArcSurface(radius, 0.1f);
                Program.Check(!surface.Flat, radius + " is bent");
                ArcPoint origin = surface.At(0f, 0.1f);
                Program.Check(Math.Abs(origin.X) < 1e-6f && Math.Abs(origin.Z - 0.1f) < 1e-5f && origin.Theta == 0f, "at the middle the layer is d in front of the wall: " + origin.Z);
                foreach (float x in new[] { -4f, -1.5f, 0.7f, 3f, 5f })
                {
                    // Every layer's point at one x is on the same normal: the distance between layers is the difference of d.
                    ArcPoint wall = surface.At(x, 0f), back = surface.At(x, 0.02f), face = surface.At(x, 0.1f);
                    float stud = (float)Math.Sqrt((face.X - back.X) * (face.X - back.X) + (face.Z - back.Z) * (face.Z - back.Z));
                    Program.Near(stud, 0.08f, 1e-4f, radius + " at " + x + ": a stud is 8 cm");
                    float gap = (float)Math.Sqrt((back.X - wall.X) * (back.X - wall.X) + (back.Z - wall.Z) * (back.Z - wall.Z));
                    Program.Near(gap, 0.02f, 1e-4f, radius + " at " + x + ": a spacer is 2 cm to the wall");
                    Program.Check(wall.Theta == face.Theta, "one angle for every layer");
                    // The wall is on the circle round (0, -R).
                    float ring = (float)Math.Sqrt(wall.X * wall.X + (wall.Z + radius) * (wall.Z + radius));
                    Program.Near(ring, Math.Abs(radius), 1e-3f, radius + ": the wall is on its circle");
                }

                // The wall's z recedes at the sides of a convex wall and comes toward the viewer on a concave one.
                float side = surface.WallZ(3f);
                Program.Check(radius > 0f ? side < 0f : side > 0f, radius + ": the wall at 3 m is at " + side);
                Program.Near(surface.WallZ(3f), surface.WallZ(-3f), 1e-5f, "symmetric");
                // The letters keep their spacing on the face layer: a step of 1 cm is a step of 1 cm.
                ArcPoint a = surface.At(2f, 0.1f), b = surface.At(2.01f, 0.1f);
                Program.Near((float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z)), 0.01f, 2e-5f, radius + ": the face layer keeps its length");
            }

            var flat = new ArcSurface(0f, 0.1f);
            Program.Check(flat.Flat && flat.At(2f, 0.1f).X == 2f && flat.At(2f, 0.1f).Z == 0.1f && flat.Theta(5f) == 0f, "flat is flat");
            Program.Check(new ArcSurface(1.5f, 0.1f).Flat && new ArcSurface(250f, 0.1f).Flat && new ArcSurface(float.NaN, 0.1f).Flat, "radii outside the bounds are flat");
            Program.Check(new ArcSurface(2f, 1.2f).Flat, "a circle that the layers would run through is flat");
            // The turn at the sides: a convex wall turns a piece at +x toward +x, a concave one toward -x.
            Program.Check(new ArcSurface(12f, 0.1f).Theta(3f) > 0.2f && new ArcSurface(-12f, 0.1f).Theta(3f) < -0.2f, "the angle follows the side the wall turns");
        }

        private static void Inverse()
        {
            foreach (float radius in new[] { 12f, -12f, 60f, -60f })
            {
                var surface = new ArcSurface(radius, 0.08f);
                foreach (float x in new[] { -5f, -2f, 0f, 1.3f, 4.5f })
                {
                    foreach (float d in new[] { 0f, 0.05f, 0.08f })
                    {
                        ArcPoint p = surface.At(x, d);
                        Program.Near(surface.LayoutX(p.X, p.Z, d), x, 2e-3f, radius + " at " + x + " d " + d + ": read back");
                    }
                }
            }

            Program.Check(new ArcSurface(0f, 0.1f).LayoutX(3f, 0.2f, 0.1f) == 3f, "flat: x");
        }

        private static void Chords()
        {
            foreach (float radius in new[] { 3f, -3f, 12f, 80f })
            {
                var surface = new ArcSurface(radius, 0.05f);
                List<(ArcPoint A, ArcPoint B)> pieces = ArcLayout.Chords(surface, -2.5f, 3f, 0.05f, 0.004f);
                Program.Near(pieces[0].A.X, surface.At(-2.5f, 0.05f).X, 1e-5f, radius + ": the first chord starts at the rail's start");
                Program.Near(pieces[pieces.Count - 1].B.X, surface.At(3f, 0.05f).X, 1e-5f, radius + ": the last ends at its end");
                bool joined = true, sag = true;
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (i > 0)
                        joined &= Math.Abs(pieces[i].A.X - pieces[i - 1].B.X) < 1e-5f && Math.Abs(pieces[i].A.Z - pieces[i - 1].B.Z) < 1e-5f;
                    // The middle of the chord is within the sag of the arc: its distance from the circle's centre.
                    float mx = (pieces[i].A.X + pieces[i].B.X) * 0.5f, mz = (pieces[i].A.Z + pieces[i].B.Z) * 0.5f;
                    float ring = (float)Math.Sqrt(mx * mx + (mz + radius) * (mz + radius));
                    sag &= Math.Abs(ring - Math.Abs(radius + 0.05f)) <= 0.0045f;
                }

                Program.Check(joined && sag, radius + ": " + pieces.Count + " chords, joined, each within 4 mm of the arc");
                Program.Check(radius == 80f ? pieces.Count < 40 : pieces.Count >= 2, radius + ": " + pieces.Count + " chords");
            }

            List<(ArcPoint A, ArcPoint B)> flat = ArcLayout.Chords(new ArcSurface(0f, 0.05f), 3f, -1f, 0.05f);
            Program.Check(flat.Count == 1 && flat[0].A.X == -1f && flat[0].B.X == 3f, "flat is one chord, whichever way it is given");
            Program.Check(ArcLayout.Chords(new ArcSurface(12f, 0.05f), 1f, 1f, 0.05f).Count == 1, "a rail of no length is one");
        }

        private static void Glyphs()
        {
            var surface = new ArcSurface(10f, 0.1f);
            ArcGlyph g = ArcLayout.Place(surface, 1.0f, 0.6f, 0.1f);
            ArcPoint expected = surface.At(1.3f, 0.1f);
            Program.Check(g.X == expected.X && g.Z == expected.Z && g.Theta == expected.Theta && g.Theta > 0f, "a glyph is set by the middle of its advance");
            ArcGlyph flat = ArcLayout.Place(new ArcSurface(0f, 0.1f), 1.0f, 0.6f, 0.1f);
            Program.Check(flat.X == 1.3f && flat.Z == 0.1f && flat.Theta == 0f, "flat: where it was");
        }
    }
}
