using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using static CDLC.TS.Tests.ShapeFixture;

namespace CDLC.TS.Tests
{
    /// <summary>The outline of a custom picture's opaque pixels at the picture's own resolution (ContourTrace, 0.12.73): shapes with a known outline, thin strokes, holes, saddles, specks.</summary>
    internal static class ContourTraceTests
    {
        public static void RunAll()
        {
            Program.Run("contour: a rectangle keeps its four corners on the pixel boundary, at any tolerance", Corners);
            Program.Run("contour: a circle stays within the tolerance of its pixels and has fewer points as it grows", Round);
            Program.Run("contour: a hole is clockwise and inside its outer loop, and the triangles cover the area between", Holes);
            Program.Run("contour: pixels that touch at a corner are two loops, in whole-or-nothing and in graded alpha", Saddles);
            Program.Run("contour: a thin stroke and a thin ring survive where the 192-cell mask loses them", Thin);
            Program.Run("contour: graded alpha is traced to the threshold's crossing, specks are dropped, the result is the same twice", Graded);
        }

        private static List<List<Point2>> Trace(byte[] whole, int w, int h, float tolerance = 0.5f, float minArea = 0f) =>
            ContourTrace.Trace(whole, w, h, false, CustomMask.OpaqueAlpha, tolerance, minArea, 0f, h, 1f, 1f);

        private static float Signed(List<List<Point2>> loops)
        {
            float sum = 0f;
            foreach (List<Point2> loop in loops)
                sum += CutOutline.Area(loop);
            return sum;
        }

        private static byte[] Plane(int w, int h, Func<int, int, byte> alpha)
        {
            var plane = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                    plane[y * w + x] = alpha(x, y);
            }

            return plane;
        }

        private static void Corners()
        {
            byte[] box = Plane(32, 24, (x, y) => (byte)(x >= 4 && x < 24 && y >= 3 && y < 15 ? 255 : 0));
            foreach (float tolerance in new[] { 0f, 0.5f, 2f })
            {
                List<List<Point2>> loops = Trace(box, 32, 24, tolerance);
                bool exact = loops.Count == 1 && loops[0].Count == 4;
                if (exact)
                {
                    foreach (Point2 p in loops[0])
                        exact &= (p.X == 4f || p.X == 24f) && (p.Y == 24f - 3f || p.Y == 24f - 15f);
                }

                Program.Check(exact, $"tolerance {tolerance}: one loop of four points on the pixel corners: {loops.Count} loop(s), {(loops.Count > 0 ? loops[0].Count : 0)} point(s)");
                Program.Check(loops.Count == 1 && Math.Abs(CutOutline.Area(loops[0]) - 240f) < 1e-3f, "its area is 20 x 12 pixels and its orientation counter-clockwise (y up): " + (loops.Count > 0 ? CutOutline.Area(loops[0]) : 0f));
            }

            // The frame is applied: a pixel of 0.5 by 0.25 in a frame whose top left is at (-1, 2).
            List<List<Point2>> framed = ContourTrace.Trace(box, 32, 24, false, CustomMask.OpaqueAlpha, 0.5f, 0f, -1f, 2f, 0.5f, 0.25f);
            Program.Check(framed.Count == 1 && framed[0].Count == 4 && Math.Abs(CutOutline.Area(framed[0]) - 240f * 0.125f) < 1e-3f, "the pixel size and the frame scale the loop");
            bool corner = false;
            foreach (Point2 p in framed[0])
                corner |= Math.Abs(p.X - (-1f + 4f * 0.5f)) < 1e-4f && Math.Abs(p.Y - (2f - 3f * 0.25f)) < 1e-4f;
            Program.Check(corner, "the top left corner of the rectangle is at the pixel (4, 3)'s corner in the frame");
            Program.Check(Trace(Plane(8, 8, (x, y) => 0), 8, 8).Count == 0 && Trace(Plane(8, 8, (x, y) => 255), 8, 8).Count == 1, "no opaque pixel, no loop; a picture that is all opaque is its own rectangle");
        }

        private static float Worst(List<Point2> loop, double cx, double cy, double r)
        {
            float worst = 0f;
            foreach (Point2 p in loop)
                worst = Math.Max(worst, (float)Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - r));
            return worst;
        }

        private static void Round()
        {
            const int size = 200;
            byte[] whole = Whole(Alpha(Disc(100, 100, 80), size, size));
            int previous = int.MaxValue;
            foreach (float tolerance in new[] { 0.25f, 0.5f, 1f, 2f })
            {
                List<List<Point2>> loops = Trace(whole, size, size, tolerance);
                // The vertices are corners of the pixel staircase, on the pixels' outside or inside corners: at most about 0.7 pixel from the circle, plus the tolerance that DP may add.
                float worst = loops.Count == 1 ? Worst(loops[0], 100, size - 100, 80) : 99f;
                Program.Check(loops.Count == 1 && worst <= 1.6f + tolerance, $"tolerance {tolerance}: the points are within {worst:0.00} px of the circle");
                Program.Check(loops[0].Count < previous, $"tolerance {tolerance}: {loops[0].Count} points, fewer than {previous}");
                previous = loops[0].Count;
            }

            List<List<Point2>> fine = Trace(whole, size, size, 0.5f);
            double disc = Math.PI * 80 * 80, opaque = Count(Mask(whole, size, size, 1));
            Program.Check(Math.Abs(Signed(fine) - opaque) < 0.005 * opaque, $"the area is that of the opaque pixels within 0.5 %: {Signed(fine):0} of {opaque:0}");
            Program.Check(Math.Abs(Signed(fine) - disc) < 0.02 * disc, $"and the disc's within 2 % (the import drops the pixels it does not wholly cover): {Signed(fine):0} of {disc:0}");
        }

        private static void Holes()
        {
            byte[] ring = Whole(Alpha(Minus(Rectangle(4, 4, 60, 44), Disc(32, 24, 12)), 64, 48));
            List<List<Point2>> loops = Trace(ring, 64, 48, 0.5f);
            Program.Check(loops.Count == 2, "a rectangle with a round hole is two loops: " + loops.Count);
            List<Point2> outer = CutOutline.Area(loops[0]) > 0f ? loops[0] : loops[1], hole = CutOutline.Area(loops[0]) > 0f ? loops[1] : loops[0];
            Program.Check(CutOutline.Area(outer) > 0f && CutOutline.Area(hole) < 0f, $"the outer counter-clockwise ({CutOutline.Area(outer):0}), the hole clockwise ({CutOutline.Area(hole):0})");
            bool inside = true;
            foreach (Point2 p in hole)
                inside &= p.X > 4f && p.X < 60f && p.Y > 48f - 44f && p.Y < 48f - 4f;
            Program.Check(inside, "the hole is inside the outer loop");
            var points = new List<Point2>();
            var triangles = new List<int>();
            CutOutline.Triangulate(loops, points, triangles);
            float covered = 0f, negative = 0f;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Point2 a = points[triangles[t]], b = points[triangles[t + 1]], c = points[triangles[t + 2]];
                float area = ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) * 0.5f;
                covered += area;
                negative += area < 1e-6f ? 1f : 0f;
            }

            Program.Check(Math.Abs(covered - Signed(loops)) < 0.01f * Signed(loops) && negative == 0f, $"the triangles are all counter-clockwise and cover the loops' area ({covered:0.0} of {Signed(loops):0.0})");
            bool[] covered4 = Mask(loops, 64, 48, 4);
            Program.Check(Count(covered4) > 0 && !covered4[(48 * 4 / 2) * 64 * 4 + 64 * 4 / 2] && covered4[(48 * 4 / 2) * 64 * 4 + 8 * 4], "the middle of the hole is not covered by the loops, the ring is");
        }

        private static void Saddles()
        {
            byte[] diagonal = Plane(6, 6, (x, y) => (byte)((x == 2 && y == 2) || (x == 3 && y == 3) ? 255 : 0));
            List<List<Point2>> whole = Trace(diagonal, 6, 6, 0f);
            Program.Check(whole.Count == 2 && Math.Abs(Signed(whole) - 2f) < 1e-3f, $"two pixels that meet at a corner are two loops (whole-or-nothing): {whole.Count}");
            // The same in graded alpha: 255 and 253 are opaque, 100 is clear; the saddle's mean (127) is below the threshold, so the pixels stay apart.
            byte[] graded = Plane(6, 6, (x, y) => (byte)((x == 2 && y == 2) ? 255 : (x == 3 && y == 3) ? 253 : 100));
            List<List<Point2>> apart = Trace(graded, 6, 6, 0f);
            Program.Check(apart.Count == 2 && Simple(apart), $"graded: two loops, no crossing: {apart.Count}");
            // A checkerboard of opaque and clear pixels: every inner corner is a saddle; the loops must not cross.
            byte[] board = Plane(8, 8, (x, y) => (byte)((x + y) % 2 == 0 ? 255 : 0));
            List<List<Point2>> loops = Trace(board, 8, 8, 0f);
            Program.Check(loops.Count == 32 && Simple(loops) && Math.Abs(Signed(loops) - 32f) < 1e-3f, $"a checkerboard of 32 opaque pixels is 32 loops of area 1, none crossing: {loops.Count}");
            byte[] boardGraded = Plane(8, 8, (x, y) => (byte)((x + y) % 2 == 0 ? 255 : 1));
            List<List<Point2>> gradedLoops = Trace(boardGraded, 8, 8, 0f);
            Program.Check(gradedLoops.Count == 32 && Simple(gradedLoops), $"the same checkerboard with graded alpha (clear 1): 32 loops, none crossing: {gradedLoops.Count}");
        }

        /// <summary>No two edges of the loops cross (touching at an end is not crossing).</summary>
        private static bool Simple(List<List<Point2>> loops)
        {
            var edges = new List<(Point2 A, Point2 B)>();
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++)
                    edges.Add((loop[i], loop[(i + 1) % loop.Count]));
            }

            for (int i = 0; i < edges.Count; i++)
            {
                for (int j = i + 1; j < edges.Count; j++)
                {
                    if (Crosses(edges[i].A, edges[i].B, edges[j].A, edges[j].B))
                        return false;
                }
            }

            return true;
        }

        private static bool Crosses(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 1e-6f && d2 < -1e-6f) || (d1 < -1e-6f && d2 > 1e-6f)) && ((d3 > 1e-6f && d4 < -1e-6f) || (d3 < -1e-6f && d4 > 1e-6f));
        }

        private static float Cross(Point2 a, Point2 b, Point2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

        private static void Thin()
        {
            // A 1024 x 640 picture with a 2-pixel stroke (an L) and a ring 3 pixels wide: the mask of 192 cells (a cell is opaque only when all its pixels are) holds none of the stroke.
            const int w = 1024, h = 640;
            Shape stroke = Union(Rectangle(200, 100, 202, 500), Rectangle(200, 498, 700, 500));
            Shape ring = Ring(800, 320, 100, 3);
            byte[] whole = Whole(Alpha(Union(stroke, ring), w, h));
            var rgba = Rgba(whole);
            CustomMask mask = CustomMask.FromAlpha(rgba, w, h);
            int cells = 0;
            for (int i = 0; i < mask.Cells.Length; i++)
                cells += mask.Cells[i] ? 1 : 0;
            Program.Check(mask != null && cells == 0, $"the 192-cell mask has no opaque cell for a 2-pixel stroke and a 3-pixel ring: {cells}");
            List<List<Point2>> loops = Trace(whole, w, h, 0.5f, 4f);
            bool[] board = Mask(loops, w, h, 1), opaque = Mask(whole, w, h, 1);
            int kept = Count(opaque) - Minus(opaque, board);
            Program.Check(Count(opaque) > 3000 && kept > 0.9 * Count(opaque), $"the contour keeps {kept} of the {Count(opaque)} opaque pixels of the stroke and the ring");
            Program.Check(loops.Count == 3, "the L and the ring's two edges are three loops: " + loops.Count);
        }

        private static void Graded()
        {
            // A column of alpha going 0, 100, 255 across: with the threshold 250 the crossing lies 0.97 of the way from the 100 to the 255 pixel centre.
            int w = 6, h = 4;
            byte[] plane = Plane(w, h, (x, y) => (byte)(x < 2 ? 0 : x == 2 ? 100 : 255));
            List<List<Point2>> loops = ContourTrace.Trace(plane, w, h, false, 250, 0f, 0f, 0f, h, 1f, 1f);
            float left = float.MaxValue;
            foreach (Point2 p in loops[0])
                left = Math.Min(left, p.X);
            float expected = 2.5f + (250f - 100f) / (255f - 100f);
            Program.Check(loops.Count == 1 && Math.Abs(left - expected) < 1e-3f, $"the edge is where the alpha reaches the threshold between two pixel centres: {left:0.000} (expected {expected:0.000})");
            // The same picture twice gives the same loops.
            List<List<Point2>> again = ContourTrace.Trace(plane, w, h, false, 250, 0f, 0f, 0f, h, 1f, 1f);
            bool same = again.Count == loops.Count;
            for (int i = 0; same && i < loops.Count; i++)
            {
                same = loops[i].Count == again[i].Count;
                for (int k = 0; same && k < loops[i].Count; k++)
                    same = loops[i][k].X == again[i][k].X && loops[i][k].Y == again[i][k].Y;
            }

            Program.Check(same, "the same picture traced twice gives the same points");

            // A graded ring: the outer loop counter-clockwise, the hole clockwise, and their triangles (CutOutline.Triangulate) cover the ring, as for whole-or-nothing alpha.
            byte[] ring = Alpha(Ring(32, 32, 28, 10), 64, 64);
            List<List<Point2>> ringLoops = ContourTrace.Trace(ring, 64, 64, false, 128, 0.5f, 0f, 0f, 64f, 1f, 1f);
            float outerArea = 0f, holeArea = 0f;
            foreach (List<Point2> loop in ringLoops)
            {
                float a = CutOutline.Area(loop);
                outerArea = Math.Max(outerArea, a);
                holeArea = Math.Min(holeArea, a);
            }

            var points = new List<Point2>();
            var triangles = new List<int>();
            CutOutline.Triangulate(ringLoops, points, triangles);
            float covered = 0f;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Point2 a = points[triangles[t]], b = points[triangles[t + 1]], c = points[triangles[t + 2]];
                covered += ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) * 0.5f;
            }

            double exactRing = Math.PI * (28.0 * 28 - 18.0 * 18);
            Program.Check(ringLoops.Count == 2 && outerArea > 0f && holeArea < 0f && Math.Abs(covered - exactRing) < 0.02 * exactRing, $"graded ring: two loops (+{outerArea:0}, {holeArea:0}), the triangles cover {covered:0} of the ring's {exactRing:0}");

            // Specks: a lone pixel and a 2 x 2 block, beside a 10 x 10 block, with a minimum area of 4 pixels: the lone pixel goes, the block stays.
            byte[] specks = Plane(24, 12, (x, y) => (byte)((x >= 2 && x < 12 && y >= 1 && y < 11) || (x == 15 && y == 5) || (x >= 18 && x < 20 && y >= 5 && y < 7) ? 255 : 0));
            Program.Check(Trace(specks, 24, 12, 0f, 0f).Count == 3 && Trace(specks, 24, 12, 0f, 4f).Count == 2 && Trace(specks, 24, 12, 0f, 5f).Count == 1, "a speck below the minimum area is dropped, one at it is kept");
        }
    }
}
