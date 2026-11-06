using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Tests;
using static CDLC.TS.Tests.ShapeFixture;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// The outline of a custom picture's board in cut mode (BENCH_CUTSHAPE=1): the pipeline of 0.12.72 (CustomMask of 192 cells, a board grid of 160 cells, CutOutline.Trace, Douglas-Peucker at one cell)
    /// against the contour of the picture's own pixels (ContourTrace) on pictures of known shape. The areas are counted on a grid of half a pixel; the reference is the picture as the import stores it
    /// (alpha whole or nothing, opaque iff every sample of its 4 x 4 is inside the shape) and the exact shape. Then what the mesh costs (the face, the back and the rim of Prims.LoopsFace and LoopsRim).
    /// </summary>
    internal static class CutShape
    {
        private const int Scale = 2;

        private sealed class Picture
        {
            public string Name;
            public int W, H;
            public Shape Exact;
            public byte[] Graded, Whole;
            public bool[] ExactMask, BinaryMask;
        }

        private sealed class Outcome
        {
            public List<List<Point2>> Loops;
            public double Ms;
        }

        public static void Run()
        {
            foreach ((int w, int h) in new[] { (1024, 640), (256, 256), (64, 64) })
            {
                Console.WriteLine();
                Console.WriteLine($"picture {w} x {h}: board = picture, one unit a pixel. loops, points, triangles of the face; ms for the whole path; kept = share of the opaque pixels the board covers;");
                Console.WriteLine("over = board outside the opaque pixels (px2); exact-eroded / exact-over = the exact shape not covered / the board outside it (px2, % of the shape); H = the farthest the board's");
                Console.WriteLine("outline is from the opaque pixels' outline, either way (px).");
                Console.WriteLine("shape      pipeline        loops points  tris     ms   kept%   over   eroded%  exact-over%   H(px)");
                foreach (Picture p in Pictures(w, h))
                {
                    Row(p, "current (192/160)", (pic) => Current(pic.Whole, pic.W, pic.H));
                    foreach (float tol in new[] { 0.25f, 0.5f, 1f, 2f })
                        Row(p, $"pixels tol {tol:0.00}", (pic) => ContourTrace.Trace(pic.Whole, pic.W, pic.H, false, CustomMask.OpaqueAlpha, tol, ContourTrace.DefaultMinArea, 0f, pic.H, 1f, 1f));
                    Row(p, "graded 250 t0.5", (pic) => ContourTrace.Trace(pic.Graded, pic.W, pic.H, false, 250, 0.5f, ContourTrace.DefaultMinArea, 0f, pic.H, 1f, 1f));
                    Row(p, "graded 128 t0.5", (pic) => ContourTrace.Trace(pic.Graded, pic.W, pic.H, false, 128, 0.5f, ContourTrace.DefaultMinArea, 0f, pic.H, 1f, 1f));
                }
            }

            Console.WriteLine();
            Console.WriteLine("mesh cost of a board of 1024 x 640 (face + back = LoopsFace twice; rim = LoopsRim flat or smooth (curves under 40 degrees share vertices)): vertices / triangles");
            Console.WriteLine("shape      pipeline        loops points    face+back v/t        rim flat v/t        rim smooth v/t        total flat v/t");
            var pictures = new List<Picture>(Pictures(1024, 640)) { Make("text", 1024, 640, Text(1024, 640)) };
            foreach (Picture p in pictures)
            {
                Cost(p, "current (192/160)", Current(p.Whole, p.W, p.H));
                foreach (float tol in new[] { 0.25f, 0.5f, 1f, 2f })
                    Cost(p, $"pixels tol {tol:0.00}", ContourTrace.Trace(p.Whole, p.W, p.H, false, CustomMask.OpaqueAlpha, tol, ContourTrace.DefaultMinArea, 0f, p.H, 1f, 1f));
                Cost(p, "graded 250 t0.5", ContourTrace.Trace(p.Graded, p.W, p.H, false, 250, 0.5f, ContourTrace.DefaultMinArea, 0f, p.H, 1f, 1f));
            }
        }

        private static IEnumerable<Picture> Pictures(int w, int h)
        {
            double m = Math.Min(w, h);
            yield return Make("rectangle", w, h, Rectangle(0.1 * w + 0.3, 0.15 * h + 0.3, 0.9 * w - 0.2, 0.85 * h - 0.4));
            yield return Make("rounded", w, h, RoundedRectangle(0.1 * w + 0.3, 0.15 * h + 0.3, 0.9 * w - 0.2, 0.85 * h - 0.4, 0.12 * m));
            yield return Make("circle", w, h, Disc(w / 2.0, h / 2.0, 0.4 * m));
            yield return Make("star", w, h, Star(w / 2.0, h / 2.0 + 0.02 * h, 0.45 * m, 0.18 * m, 5));
            yield return Make("hole", w, h, Minus(Rectangle(0.08 * w + 0.4, 0.1 * h + 0.4, 0.92 * w - 0.3, 0.9 * h - 0.3), Disc(w / 2.0, h / 2.0, 0.2 * m)));
            double x0 = 0.25 * w, x1 = 0.75 * w, y0 = 0.15 * h, y1 = 0.85 * h, t = 0.12 * h;
            yield return Make("E (thick)", w, h, Union(Rectangle(x0, y0, x0 + 0.12 * w, y1), Rectangle(x0, y0, x1, y0 + t), Rectangle(x0, (y0 + y1) / 2 - t / 2, x1 - 0.08 * w, (y0 + y1) / 2 + t / 2), Rectangle(x0, y1 - t, x1, y1)));
            double lx = Math.Round(0.2 * w), ly0 = Math.Round(0.15 * h), ly1 = Math.Round(0.85 * h);
            yield return Make("L (2 px)", w, h, Union(Rectangle(lx, ly0, lx + 2, ly1), Rectangle(lx, ly1 - 2, Math.Round(0.75 * w), ly1)));
            yield return Make("ring (3 px)", w, h, Ring(w / 2.0, h / 2.0, 0.4 * m, 3));
        }

        /// <summary>Many loops: a grid of small letters (an E, a ring, a bar) as a line of text is.</summary>
        private static Shape Text(int w, int h)
        {
            var parts = new List<Shape>();
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 12; col++)
                {
                    double x = 30 + col * 80.3, y = 30 + row * 100.4;
                    switch ((row + col) % 3)
                    {
                        case 0:
                            parts.Add(Union(Rectangle(x, y, x + 10, y + 56), Rectangle(x, y, x + 40, y + 10), Rectangle(x, y + 23, x + 32, y + 33), Rectangle(x, y + 46, x + 40, y + 56)));
                            break;
                        case 1:
                            parts.Add(Ring(x + 22, y + 28, 24, 7));
                            break;
                        default:
                            parts.Add(Union(Rectangle(x, y + 20, x + 44, y + 36), Rectangle(x + 14, y, x + 22, y + 56)));
                            break;
                    }
                }
            }

            return Union(parts.ToArray());
        }

        private static Picture Make(string name, int w, int h, Shape shape)
        {
            byte[] graded = Alpha(shape, w, h);
            byte[] whole = Whole(graded);
            return new Picture { Name = name, W = w, H = h, Exact = shape, Graded = graded, Whole = whole, ExactMask = Mask(shape, w, h, Scale), BinaryMask = Mask(whole, w, h, Scale) };
        }

        /// <summary>The cut of 0.12.72 as Custom.Cut does it for a board the size of the picture: the picture's mask, 160 cells on the longer side sampled at their centres, CutOutline.Trace.</summary>
        private static List<List<Point2>> Current(byte[] whole, int w, int h)
        {
            CustomMask mask = CustomMask.FromAlpha(Rgba(whole), w, h);
            var loops = new List<List<Point2>>();
            if (mask == null)
                return loops;
            float cell = Math.Max(w, h) / 160f;
            int nx = Math.Max(1, (int)Math.Ceiling(w / cell - 1e-3f)), ny = Math.Max(1, (int)Math.Ceiling(h / cell - 1e-3f));
            float cw = (float)w / nx, ch = (float)h / ny;
            var cells = new bool[nx * ny];
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                    cells[j * nx + i] = mask.AtUv((i + 0.5f) * cw / w, (j + 0.5f) * ch / h);
            }

            return CutOutline.Trace((i, j) => cells[j * nx + i], nx, ny, cw, ch, 0f, h);
        }

        private static void Row(Picture p, string pipe, Func<Picture, List<List<Point2>>> trace)
        {
            List<List<Point2>> loops = null;
            double[] times = new double[5];
            for (int r = -1; r < 5; r++)
            {
                var clock = Stopwatch.StartNew();
                loops = trace(p);
                var points = new List<Point2>();
                var triangles = new List<int>();
                CutOutline.Triangulate(loops, points, triangles);
                if (r >= 0)
                    times[r] = clock.Elapsed.TotalMilliseconds;
            }

            Array.Sort(times);
            var allPoints = new List<Point2>();
            var allTriangles = new List<int>();
            CutOutline.Triangulate(loops, allPoints, allTriangles);
            int vertices = 0;
            foreach (List<Point2> loop in loops)
                vertices += loop.Count;
            bool[] board = Mask(loops, p.W, p.H, Scale);
            double opaque = Count(p.BinaryMask), exact = Count(p.ExactMask);
            double kept = 100.0 * (opaque - Minus(p.BinaryMask, board)) / Math.Max(1.0, opaque);
            double over = Minus(board, p.BinaryMask) / (double)(Scale * Scale);
            double eroded = 100.0 * Minus(p.ExactMask, board) / Math.Max(1.0, exact), exactOver = 100.0 * Minus(board, p.ExactMask) / Math.Max(1.0, exact);
            Console.WriteLine($"{p.Name,-10} {pipe,-17} {loops.Count,4} {vertices,6} {allTriangles.Count / 3,5} {times[2],7:0.0} {kept,7:0.0} {over,6:0} {eroded,8:0.00} {exactOver,10:0.00} {Hausdorff(loops, p),8:0.00}");
        }

        /// <summary>The farthest the board's outline is from the opaque pixels' outline, and the farthest the pixels' outline is from the board's (pixels): the larger.</summary>
        private static double Hausdorff(List<List<Point2>> loops, Picture p)
        {
            var boundary = new List<(double X0, double Y0, double X1, double Y1)>();
            int w = p.W, h = p.H;
            bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && p.Whole[y * w + x] != 0;
            for (int y = 0; y <= h; y++)
            {
                for (int x = 0; x <= w; x++)
                {
                    if (Opaque(x, y) != Opaque(x, y - 1))
                        boundary.Add((x, h - y, x + 1, h - y));
                    if (Opaque(x, y) != Opaque(x - 1, y))
                        boundary.Add((x, h - y, x, h - y - 1));
                }
            }

            var buckets = new Dictionary<long, List<int>>();
            for (int i = 0; i < boundary.Count; i++)
            {
                long key = (long)Math.Floor(boundary[i].X0 / 16) * 100003L + (long)Math.Floor(boundary[i].Y0 / 16);
                if (!buckets.TryGetValue(key, out List<int> list))
                    buckets[key] = list = new List<int>();
                list.Add(i);
            }

            double worst = 0.0;
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    Point2 a = loop[i], b = loop[(i + 1) % loop.Count];
                    double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                    int steps = Math.Max(1, (int)Math.Ceiling(length / 0.5));
                    for (int s = 0; s <= steps; s++)
                    {
                        double x = a.X + (b.X - a.X) * s / steps, y = a.Y + (b.Y - a.Y) * s / steps, best = double.MaxValue;
                        long cx = (long)Math.Floor(x / 16), cy = (long)Math.Floor(y / 16);
                        // The square of cells round the point is searched, wider until what was found is nearer than the square's edge (less the 1 px by which a segment's first end may be off its cell).
                        for (int reach = 1; reach <= 64; reach *= 2)
                        {
                            for (long gx = cx - reach; gx <= cx + reach; gx++)
                            {
                                for (long gy = cy - reach; gy <= cy + reach; gy++)
                                {
                                    if (!buckets.TryGetValue(gx * 100003L + gy, out List<int> near))
                                        continue;
                                    foreach (int k in near)
                                        best = Math.Min(best, Distance(x, y, boundary[k].X0, boundary[k].Y0, boundary[k].X1, boundary[k].Y1));
                                }
                            }

                            if (best <= reach * 16 - 1)
                                break;
                        }

                        if (best < double.MaxValue)
                            worst = Math.Max(worst, best);
                    }
                }
            }

            foreach ((double X0, double Y0, double X1, double Y1) in boundary)
            {
                double x = (X0 + X1) / 2, y = (Y0 + Y1) / 2, best = double.MaxValue;
                foreach (List<Point2> loop in loops)
                {
                    for (int i = 0; i < loop.Count; i++)
                    {
                        Point2 a = loop[i], b = loop[(i + 1) % loop.Count];
                        best = Math.Min(best, Distance(x, y, a.X, a.Y, b.X, b.Y));
                    }
                }

                if (best < double.MaxValue)
                    worst = Math.Max(worst, best);
            }

            return worst;
        }

        private static double Distance(double x, double y, double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0, l2 = dx * dx + dy * dy;
            double t = l2 > 1e-12 ? Math.Max(0.0, Math.Min(1.0, ((x - x0) * dx + (y - y0) * dy) / l2)) : 0.0;
            double px = x0 + t * dx - x, py = y0 + t * dy - y;
            return Math.Sqrt(px * px + py * py);
        }

        private static void Cost(Picture p, string pipe, List<List<Point2>> loops)
        {
            var face = new MeshBuilder();
            var back = new MeshBuilder();
            Prims.LoopsFace(face, loops, 0.03f, true, 0f, 0f, (x, y) => (x, y));
            Prims.LoopsFace(back, loops, 0f, false, 0f, 0f, null);
            var flat = new MeshBuilder();
            var smooth = new MeshBuilder();
            Prims.LoopsRim(flat, loops, 0f, 0.03f, 0f, 0f, false);
            Prims.LoopsRim(smooth, loops, 0f, 0.03f, 0f, 0f, true);
            int points = 0;
            foreach (List<Point2> loop in loops)
                points += loop.Count;
            int fv = face.VertexCount + back.VertexCount, ft = face.TriangleCount + back.TriangleCount;
            Console.WriteLine($"{p.Name,-10} {pipe,-17} {loops.Count,4} {points,6}   {fv,7}/{ft,-7}   {flat.VertexCount,7}/{flat.TriangleCount,-7}   {smooth.VertexCount,7}/{smooth.TriangleCount,-7}   {fv + flat.VertexCount,8}/{ft + flat.TriangleCount,-8}");
        }
    }
}
