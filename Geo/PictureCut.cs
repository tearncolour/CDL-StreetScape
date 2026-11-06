using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What PictureCut.Make made of a picture's contour for one board.</summary>
    public sealed class PictureCutResult
    {
        /// <summary>The loops of the board, in the part's frame (centred, y up). Null when the contour could not be used (Reason); empty when no part of the picture is on the board.</summary>
        public List<List<Point2>> Loops;

        public string Reason = "";

        /// <summary>The tolerance (pixels) the loops were dropped to: 0.5, or more where the board would have cost too many triangles.</summary>
        public float Tolerance;

        public int Vertices;

        /// <summary>The triangles the board costs as it is built: the face, the back (two fills of the loops) and the edge (two for each side).</summary>
        public int Triangles;

        public double Ms;
    }

    /// <summary>
    /// The board of a picture cut from the contour of the picture's own pixels (0.12.74; DESIGN-wiring-plan.md step D, the owner's option A: the cut follows the picture's edge to half a pixel where the 192-cell
    /// mask erodes it by up to five and loses a stroke narrower than a cell). The contour is put where the picture is drawn on the board (its box, and for a cropped fill the part of it that shows), cut to
    /// that box and to the part's outline, and dropped from 0.5 pixel toward 1 pixel while the board would cost more than the budget. Pure.
    /// </summary>
    public static class PictureCut
    {
        /// <summary>The most triangles a cut board may cost (the owner delegated the figure: 6,000).</summary>
        public const int TriangleBudget = 6000;

        /// <summary>The tolerances tried (pixels), the finest first.</summary>
        public static readonly float[] Tolerances = { 0.5f, 0.75f, 1f };

        /// <summary>
        /// The loops of the board. outline: the part's outline (or the convex shape the cut is within), counter-clockwise, in the part's frame; the picture is the box (imageX, imageY, imageW, imageH) from the part's top
        /// left (y down), shown whole or, with crop, as much of it as the box's proportions take (CustomSigns.SourceOf). Never throws for a bad contour: the reason is in the result.
        /// </summary>
        public static PictureCutResult Make(PictureContour contour, IList<Point2> outline, float partW, float partH, float imageX, float imageY, float imageW, float imageH, bool crop, float pictureAspect, int budget = TriangleBudget)
        {
            long start = Stopwatch.GetTimestamp();
            var result = new PictureCutResult();
            try
            {
                Fill(result, contour, outline, partW, partH, imageX, imageY, imageW, imageH, crop, pictureAspect, budget);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IndexOutOfRangeException || ex is ArithmeticException)
            {
                result.Loops = null;
                result.Reason = "the cut threw " + ex.GetType().Name;
            }

            result.Ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            return result;
        }

        private static void Fill(PictureCutResult result, PictureContour contour, IList<Point2> outline, float partW, float partH, float imageX, float imageY, float imageW, float imageH, bool crop, float pictureAspect, int budget)
        {
            if (contour == null || contour.Width < 1 || contour.Height < 1 || outline == null || outline.Count < 3 || imageW <= 0f || imageH <= 0f)
            {
                result.Reason = "no contour";
                return;
            }

            CustomSigns.SourceOf(pictureAspect, imageH / Math.Max(1e-4f, imageW), crop, out float u0, out float v0, out float u1, out float v1);
            float x0 = -partW * 0.5f + imageX, y1 = partH * 0.5f - imageY;
            var rect = new List<Point2> { new Point2(x0, y1 - imageH), new Point2(x0 + imageW, y1 - imageH), new Point2(x0 + imageW, y1), new Point2(x0, y1) };
            var shape = new List<Point2>(outline);
            if (CutOutline.Area(shape) < 0f)
                shape.Reverse();
            float sx = imageW / (u1 - u0), sy = imageH / (v1 - v0);
            for (int step = 0; step < Tolerances.Length; step++)
            {
                float tolerance = Tolerances[step];
                bool last = step == Tolerances.Length - 1;
                var board = new List<List<Point2>>();
                foreach (List<Point2> loop in contour.Relaxed(tolerance))
                {
                    var points = new List<Point2>(loop.Count);
                    foreach (Point2 p in loop)
                        points.Add(new Point2(x0 + (p.X - u0) * sx, y1 - ((1f - p.Y) - v0) * sy));
                    board.Add(points);
                }

                List<List<Point2>> cut = LoopClip.ToConvex(board, rect);
                cut = cut == null ? null : LoopClip.ToConvex(cut, shape);
                if (cut == null)
                {
                    result.Reason = "the loops did not pair up along the clip";
                    return;
                }

                int vertices = 0;
                foreach (List<Point2> loop in cut)
                    vertices += loop.Count;
                // The triangles of loops are at least their points less two a loop, and the board has them twice and a quad for each edge: a lower bound of its cost, so a board that is surely over is not filled.
                if (!last && 4 * vertices - 4 * cut.Count > budget)
                    continue;
                int triangles = 0;
                if (!CountTriangles(cut, out triangles))
                {
                    result.Reason = "the triangles do not fill the loops";
                    return;
                }

                int cost = 2 * triangles + 2 * vertices;
                if (cost > budget && !last)
                    continue;
                result.Tolerance = tolerance;
                result.Vertices = vertices;
                result.Triangles = cost;
                if (cost > budget)
                {
                    result.Reason = "over the budget at " + tolerance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " px: " + cost + " triangles";
                    return;
                }

                result.Loops = cut;
                return;
            }
        }

        /// <summary>The number of triangles that fill the loops; false when they do not cover what the loops enclose (the area of the triangles against the loops' own).</summary>
        private static bool CountTriangles(List<List<Point2>> loops, out int triangles)
        {
            var points = new List<Point2>();
            var indices = new List<int>();
            CutOutline.Triangulate(loops, points, indices);
            triangles = indices.Count / 3;
            double filled = 0.0, enclosed = 0.0;
            for (int t = 0; t + 2 < indices.Count; t += 3)
            {
                Point2 a = points[indices[t]], b = points[indices[t + 1]], c = points[indices[t + 2]];
                filled += Math.Abs(((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X)) * 0.5;
            }

            foreach (List<Point2> loop in loops)
                enclosed += CutOutline.Area(loop);
            return loops.Count == 0 || Math.Abs(filled - enclosed) <= 1e-3 * Math.Max(Math.Abs(enclosed), 1e-6) + 1e-9;
        }

        /// <summary>
        /// How two sets of loops (the same convention: material on the left) differ: their areas, and the area that only the old one has and that only the new one has, found along rows (the middle of each of
        /// rows rows across the sets' common box; the ends of each stretch are exact). An estimate, to about the height of a row along the edges.
        /// </summary>
        public static void Compare(List<List<Point2>> old, List<List<Point2>> now, int rows, out double areaOld, out double areaNew, out double oldOnly, out double newOnly)
        {
            areaOld = areaNew = oldOnly = newOnly = 0.0;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (List<List<Point2>> set in new[] { old, now })
            {
                if (set == null)
                    continue;
                foreach (List<Point2> loop in set)
                {
                    foreach (Point2 p in loop)
                    {
                        minY = Math.Min(minY, p.Y);
                        maxY = Math.Max(maxY, p.Y);
                    }
                }
            }

            if (!(maxY > minY) || rows < 1)
                return;
            double step = (maxY - minY) / (double)rows;
            List<double>[] a = Crossings(old, minY, step, rows), b = Crossings(now, minY, step, rows);
            for (int r = 0; r < rows; r++)
            {
                a[r].Sort();
                b[r].Sort();
                double la = Total(a[r]), lb = Total(b[r]);
                double both = Overlap(a[r], b[r]);
                areaOld += la * step;
                areaNew += lb * step;
                oldOnly += (la - both) * step;
                newOnly += (lb - both) * step;
            }
        }

        private static List<double>[] Crossings(List<List<Point2>> loops, float minY, double step, int rows)
        {
            var at = new List<double>[rows];
            for (int r = 0; r < rows; r++)
                at[r] = new List<double>();
            if (loops == null)
                return at;
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    Point2 p = loop[i], q = loop[(i + 1) % loop.Count];
                    if (p.Y == q.Y)
                        continue;
                    double lo = Math.Min(p.Y, q.Y), hi = Math.Max(p.Y, q.Y);
                    int from = Math.Max(0, (int)Math.Ceiling((lo - minY) / step - 0.5)), to = Math.Min(rows - 1, (int)Math.Floor((hi - minY) / step - 0.5));
                    for (int r = from; r <= to; r++)
                    {
                        double y = minY + (r + 0.5) * step;
                        if (y < lo || y >= hi)
                            continue;
                        at[r].Add(p.X + (q.X - p.X) * (y - p.Y) / (q.Y - p.Y));
                    }
                }
            }

            return at;
        }

        private static double Total(List<double> crossings)
        {
            double sum = 0.0;
            for (int i = 0; i + 1 < crossings.Count; i += 2)
                sum += crossings[i + 1] - crossings[i];
            return sum;
        }

        private static double Overlap(List<double> a, List<double> b)
        {
            double sum = 0.0;
            for (int i = 0; i + 1 < a.Count; i += 2)
            {
                for (int j = 0; j + 1 < b.Count; j += 2)
                {
                    double lo = Math.Max(a[i], b[j]), hi = Math.Min(a[i + 1], b[j + 1]);
                    if (hi > lo)
                        sum += hi - lo;
                    if (b[j] > a[i + 1])
                        break;
                }
            }

            return sum;
        }
    }
}
