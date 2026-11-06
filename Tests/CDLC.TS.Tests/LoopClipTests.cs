using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>Loops cut to a convex polygon (LoopClip, 0.12.74): what is inside stays, what is outside goes, a concave loop that leaves and enters again is two loops with no bridge, a hole that reaches the line opens, and the result is what a point-by-point test says.</summary>
    internal static class LoopClipTests
    {
        public static void RunAll()
        {
            Program.Run("loop clip: inside stays as it is, outside goes, a crossing loop is cut to the line", Basic);
            Program.Run("loop clip: a concave loop that leaves and enters again is two loops with no bridge", Concave);
            Program.Run("loop clip: a hole that reaches the line opens into the outer loop; a hole inside stays", Holes);
            Program.Run("loop clip: a loop round the whole clip polygon, touching, and a clip line along an edge", Corners);
            Program.Run("loop clip: random loops with holes against random convex polygons agree with a point-by-point test", Random);
            Program.Run("loop clip: a loop that is not counter-clockwise is refused", Refused);
            Program.Run("loop clip: overlapping loops cannot be paired and are refused", Overlapping);
            Program.Run("loop clip: a clip polygon with a repeated corner is the same polygon", Repeated);
        }

        private static void Overlapping()
        {
            // Two outer loops that overlap (material twice over): both leave through the same stretch of the line and the pairing has one entering point for two chains.
            var loops = new List<List<Point2>> { Rect(3f, 2f, 8f, 6f), Rect(4f, 4f, 9f, 8f) };
            Program.Check(LoopClip.ToConvex(loops, Rect(0f, 0f, 5f, 10f)) == null, "two overlapping rectangles over the line: null");
        }

        private static void Repeated()
        {
            var clip = new List<Point2> { new Point2(0f, 0f), new Point2(10f, 0f), new Point2(10f, 0f), new Point2(10f, 10f), new Point2(0f, 10f), new Point2(0f, 0f) };
            List<List<Point2>> cut = LoopClip.ToConvex(One(Rect(6f, 3f, 14f, 8f)), clip);
            Program.Check(cut != null && cut.Count == 1, "a rectangle over the edge of a polygon with repeated corners is cut: " + (cut == null ? "null" : cut.Count.ToString()));
            Program.Near(Total(cut), 4f * 5f, 1e-4f, "its area is what is inside");
        }

        private static List<Point2> Rect(float x0, float y0, float x1, float y1) => new List<Point2> { new Point2(x0, y0), new Point2(x1, y0), new Point2(x1, y1), new Point2(x0, y1) };

        private static List<Point2> Clockwise(List<Point2> loop)
        {
            var copy = new List<Point2>(loop);
            copy.Reverse();
            return copy;
        }

        private static float Total(List<List<Point2>> loops)
        {
            float sum = 0f;
            foreach (List<Point2> loop in loops)
                sum += CutOutline.Area(loop);
            return sum;
        }

        private static List<List<Point2>> One(List<Point2> loop) => new List<List<Point2>> { loop };

        private static bool Inside(List<List<Point2>> loops, double x, double y)
        {
            bool inside = false;
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    if ((loop[i].Y > y) != (loop[j].Y > y) && x < (loop[j].X - loop[i].X) * (y - loop[i].Y) / (loop[j].Y - loop[i].Y) + loop[i].X)
                        inside = !inside;
                }
            }

            return inside;
        }

        private static bool InConvex(IList<Point2> convex, double x, double y)
        {
            for (int i = 0; i < convex.Count; i++)
            {
                Point2 a = convex[i], b = convex[(i + 1) % convex.Count];
                if ((b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X) < 0.0)
                    return false;
            }

            return true;
        }

        private static void Basic()
        {
            List<Point2> clip = Rect(0f, 0f, 10f, 10f);
            List<List<Point2>> inner = One(Rect(2f, 2f, 5f, 6f));
            List<List<Point2>> same = LoopClip.ToConvex(inner, clip);
            Program.Check(same != null && same.Count == 1 && ReferenceEquals(same[0], inner[0]), "a loop wholly inside is the loop itself");
            Program.Check(LoopClip.ToConvex(One(Rect(20f, 20f, 30f, 30f)), clip).Count == 0, "a loop wholly outside is gone");
            Program.Check(LoopClip.ToConvex(One(Rect(-5f, 0f, -1f, 10f)), clip).Count == 0, "and one beside it");

            List<List<Point2>> cut = LoopClip.ToConvex(One(Rect(6f, 3f, 14f, 8f)), clip);
            Program.Check(cut != null && cut.Count == 1 && cut[0].Count == 4, "a rectangle over the edge is a rectangle: " + (cut == null ? "null" : cut.Count + " loop(s)"));
            Program.Near(Total(cut), 4f * 5f, 1e-4f, "its area is what is inside, orientation kept counter-clockwise");

            List<List<Point2>> corner = LoopClip.ToConvex(One(Rect(8f, 8f, 15f, 15f)), clip);
            Program.Check(corner != null && corner.Count == 1 && corner[0].Count == 4, "over a corner: four points");
            Program.Near(Total(corner), 4f, 1e-4f, "area 2 x 2");

            List<List<Point2>> big = LoopClip.ToConvex(One(Rect(-50f, -50f, 50f, 50f)), clip);
            Program.Check(big != null && big.Count == 1 && big[0].Count == 4, "a loop round the whole polygon is the polygon: " + (big == null ? "null" : big[0].Count.ToString()));
            Program.Near(Total(big), 100f, 1e-3f, "area 100");

            // A triangle polygon as the clip.
            var triangle = new List<Point2> { new Point2(0f, 0f), new Point2(10f, 0f), new Point2(0f, 10f) };
            List<List<Point2>> halved = LoopClip.ToConvex(One(Rect(-5f, -5f, 15f, 15f)), triangle);
            Program.Check(halved != null && halved.Count == 1 && halved[0].Count == 3, "a big square cut to a triangle is the triangle");
            Program.Near(Total(halved), 50f, 1e-3f, "area 50");
        }

        private static void Concave()
        {
            // A U: two arms (x 0 to 2 and 4 to 6, from y 0 to 8) joined by a base (y 0 to 2), counter-clockwise. Cut at y = 5 from above: the arms stay as two separate pieces only if there is no bridge.
            var u = new List<Point2>
            {
                new Point2(0f, 0f), new Point2(6f, 0f), new Point2(6f, 8f), new Point2(4f, 8f), new Point2(4f, 2f), new Point2(2f, 2f), new Point2(2f, 8f), new Point2(0f, 8f)
            };
            List<List<Point2>> whole = One(u);
            Program.Near(Total(whole), 6f * 2f + 2f * 6f * 2f, 1e-4f, "the U has the area it should");
            // Keep the part above y = 3 (the two arms' tops): a clip polygon that is the band y 3 to 20.
            List<List<Point2>> top = LoopClip.ToConvex(whole, Rect(-1f, 3f, 7f, 20f));
            Program.Check(top != null && top.Count == 2, "the tops of the two arms are two loops, not one with a bridge: " + (top == null ? "null" : top.Count.ToString()));
            Program.Near(Total(top), 2f * 2f * 5f, 1e-4f, "area of two arms of 2 x 5");
            foreach (List<Point2> loop in top)
                Program.Check(loop.Count == 4, "each is a rectangle: " + loop.Count + " points");
            // The bottom keeps the U's base and the feet of the arms, one loop.
            List<List<Point2>> bottom = LoopClip.ToConvex(whole, Rect(-1f, -1f, 7f, 3f));
            Program.Check(bottom != null && bottom.Count == 1, "the bottom is one loop");
            Program.Near(Total(bottom), 6f * 2f + 2f * 2f * 1f, 1e-4f, "area of the base and the arms' feet");

            // An E-like comb cut to a band across all its teeth: one loop per tooth.
            var comb = new List<Point2>
            {
                new Point2(0f, 0f), new Point2(10f, 0f), new Point2(10f, 10f), new Point2(8f, 10f), new Point2(8f, 3f), new Point2(6f, 3f), new Point2(6f, 10f), new Point2(4f, 10f), new Point2(4f, 3f), new Point2(2f, 3f), new Point2(2f, 10f), new Point2(0f, 10f)
            };
            List<List<Point2>> teeth = LoopClip.ToConvex(One(comb), Rect(-1f, 5f, 11f, 9f));
            Program.Check(teeth != null && teeth.Count == 3, "three teeth across a band are three loops: " + (teeth == null ? "null" : teeth.Count.ToString()));
            Program.Near(Total(teeth), 3f * 2f * 4f, 1e-4f, "three of 2 x 4");
        }

        private static void Holes()
        {
            // A square with a square hole (clockwise). Cut by a line through the hole: the hole opens into a notch and the result is one loop.
            List<List<Point2>> frame = new List<List<Point2>> { Rect(0f, 0f, 10f, 10f), Clockwise(Rect(3f, 3f, 7f, 7f)) };
            Program.Near(Total(frame), 100f - 16f, 1e-4f, "the frame has area 84");
            List<List<Point2>> cut = LoopClip.ToConvex(frame, Rect(5f, -1f, 11f, 11f));
            Program.Check(cut != null && cut.Count == 1, "a hole reached by the line is one loop: " + (cut == null ? "null" : cut.Count.ToString()));
            Program.Near(Total(cut), 5f * 10f - 2f * 4f, 1e-4f, "the right half less the half of the hole: 42");
            Program.Check(cut[0].Count == 8, "a C of eight corners: " + cut[0].Count);

            // A hole wholly inside the clip stays a hole (clockwise).
            List<List<Point2>> keep = LoopClip.ToConvex(frame, Rect(-1f, -1f, 11f, 11f));
            Program.Check(keep != null && keep.Count == 2 && Total(keep) < 84.01f && Total(keep) > 83.99f, "a hole inside the clip stays");
            // A hole outside the clip is gone with the part of the outer loop that is.
            List<List<Point2>> away = LoopClip.ToConvex(frame, Rect(-1f, -1f, 2f, 11f));
            Program.Check(away != null && away.Count == 1, "a hole beyond the line: one loop");
            Program.Near(Total(away), 2f * 10f, 1e-4f, "area 20");
            // A ring cut in half by a line across its middle: two arcs (C shapes) that each have one loop.
            List<List<Point2>> ring = new List<List<Point2>> { Circle(0f, 0f, 10f, 48, false), Circle(0f, 0f, 6f, 48, true) };
            float ringArea = Total(ring);
            List<List<Point2>> upper = LoopClip.ToConvex(ring, Rect(-20f, 0f, 20f, 20f));
            Program.Check(upper != null && upper.Count == 1, "the upper half of a ring is one loop: " + (upper == null ? "null" : upper.Count.ToString()));
            Program.Near(Total(upper), ringArea * 0.5f, ringArea * 0.002f, "half the ring");
            List<List<Point2>> strip = LoopClip.ToConvex(ring, Rect(-20f, -1f, 20f, 1f));
            Program.Check(strip != null && strip.Count == 2, "a strip across the ring's middle is its two sides: " + (strip == null ? "null" : strip.Count.ToString()));
        }

        private static List<Point2> Circle(float cx, float cy, float r, int n, bool clockwise)
        {
            var points = new List<Point2>();
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                points.Add(new Point2(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r)));
            }

            if (clockwise)
                points.Reverse();
            return points;
        }

        private static void Corners()
        {
            List<Point2> clip = Rect(0f, 0f, 10f, 10f);
            // A loop that only touches the clip at a corner, and one that shares an edge with it, from outside.
            Program.Check(LoopClip.ToConvex(One(Rect(10f, 10f, 14f, 14f)), clip).Count == 0, "touching at a corner: nothing");
            Program.Check(LoopClip.ToConvex(One(Rect(10f, 2f, 14f, 6f)), clip).Count == 0, "sharing an edge from outside: nothing");
            // Sharing an edge from inside: the loop is kept whole.
            List<List<Point2>> flush = LoopClip.ToConvex(One(Rect(0f, 0f, 10f, 4f)), clip);
            Program.Check(flush != null && flush.Count == 1 && flush[0].Count == 4, "a loop along the clip's edge, inside it: kept: " + (flush == null ? "null" : flush.Count.ToString()));
            Program.Near(Total(flush), 40f, 1e-4f, "area 40");
            // A loop with a vertex on the clip line and the rest outside.
            var tip = new List<Point2> { new Point2(10f, 5f), new Point2(14f, 3f), new Point2(14f, 7f) };
            Program.Check(LoopClip.ToConvex(One(tip), clip).Count == 0, "a tip on the line: nothing");
            // Two loops side by side, one inside and one crossing.
            List<List<Point2>> two = LoopClip.ToConvex(new List<List<Point2>> { Rect(1f, 1f, 3f, 3f), Rect(8f, 1f, 12f, 3f) }, clip);
            Program.Check(two != null && two.Count == 2, "an island inside and one crossing: both");
            Program.Near(Total(two), 4f + 2f * 2f, 1e-4f, "area 8");
            // Many tiny clip edges (a circle of 64 sides) and a loop wholly inside.
            List<Point2> round = Circle(0f, 0f, 10f, 64, false);
            Program.Check(LoopClip.ToConvex(One(Rect(-2f, -2f, 2f, 2f)), round)[0].Count == 4, "a loop inside a 64-gon is untouched");
            List<List<Point2>> chopped = LoopClip.ToConvex(One(Rect(-20f, -20f, 20f, 20f)), round);
            Program.Check(chopped != null && chopped.Count == 1 && chopped[0].Count == 64, "a big square cut to a 64-gon is the 64-gon: " + (chopped == null ? "null" : chopped[0].Count.ToString()));
            Program.Check(LoopClip.ToConvex(new List<List<Point2>>(), clip).Count == 0, "no loops: none");
        }

        /// <summary>A star-shaped loop round (cx, cy): the radius changes with the angle, so it is simple whatever the numbers are.</summary>
        private static List<Point2> Blob(System.Random rng, float cx, float cy, float rmin, float rmax, int n)
        {
            var points = new List<Point2>();
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * (i + 0.5 * rng.NextDouble()) / n;
                double r = rmin + (rmax - rmin) * rng.NextDouble();
                points.Add(new Point2(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r)));
            }

            return points;
        }

        private static void Random()
        {
            var rng = new System.Random(20261102);
            int checkedPoints = 0, wrong = 0, nulls = 0;
            for (int round = 0; round < 300; round++)
            {
                var loops = new List<List<Point2>> { Blob(rng, 0f, 0f, 6f, 14f, 9 + rng.Next(12)) };
                if (rng.Next(2) == 0)
                {
                    // A hole in the material, and sometimes an island in the hole: the loops never overlap.
                    List<Point2> hole = Blob(rng, 0f, 0f, 1.5f, 4.5f, 6 + rng.Next(6));
                    loops.Add(Clockwise(hole));
                    List<Point2> island = Blob(rng, 0f, 0f, 0.1f, 0.3f, 5);
                    if (rng.Next(3) == 0 && island.TrueForAll(p => Inside(new List<List<Point2>> { hole }, p.X, p.Y)))
                        loops.Add(island);
                }
                // A random convex polygon: points on a circle, in order (counter-clockwise).
                int sides = 3 + rng.Next(8);
                float cx = (float)(rng.NextDouble() * 10.0 - 5.0), cy = (float)(rng.NextDouble() * 10.0 - 5.0), cr = 3f + (float)rng.NextDouble() * 12f;
                var angles = new List<double>();
                for (int i = 0; i < sides; i++)
                    angles.Add(rng.NextDouble() * 2.0 * Math.PI);
                angles.Sort();
                var convex = new List<Point2>();
                foreach (double a in angles)
                    convex.Add(new Point2(cx + (float)(Math.Cos(a) * cr), cy + (float)(Math.Sin(a) * cr)));
                if (CutOutline.Area(convex) < 1f)
                    continue;
                List<List<Point2>> result = LoopClip.ToConvex(loops, convex);
                if (result == null)
                {
                    nulls++;
                    continue;
                }

                for (int s = 0; s < 400; s++)
                {
                    double x = rng.NextDouble() * 40.0 - 20.0, y = rng.NextDouble() * 40.0 - 20.0;
                    bool expected = Inside(loops, x, y) && InConvex(convex, x, y), got = Inside(result, x, y);
                    checkedPoints++;
                    if (expected != got)
                        wrong++;
                }
            }

            Program.Check(wrong == 0, $"{checkedPoints} points tested against the loops and the polygon: {wrong} disagree");
            Program.Check(nulls == 0, "no random cut was refused: " + nulls);
        }

        private static void Refused()
        {
            List<Point2> clip = Rect(0f, 0f, 10f, 10f);
            Program.Check(LoopClip.ToConvex(One(Clockwise(Rect(5f, 2f, 15f, 8f))), clip) == null, "a clockwise outer loop crossing the line cannot be paired: null");
            Program.Check(LoopClip.ToConvex(One(Clockwise(Rect(2f, 2f, 8f, 8f))), clip) != null, "one that is wholly inside needs no pairing");
        }
    }
}
