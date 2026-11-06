using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Pictures made of shapes with an exact outline, for the tests and the bench of the cut of a custom picture's board (ContourTrace): a shape is a function of a point of the picture (pixels, x right, y
    /// down), its alpha is the share of 4 by 4 samples inside it, and the import's rule (opaque iff alpha >= 250, so every sample inside) makes it whole-or-nothing. Masks of the loops it traces are made by
    /// scanline on a grid finer than the pixels, to count areas.
    /// </summary>
    internal static class ShapeFixture
    {
        public const int Samples = 4;

        public delegate bool Shape(double x, double y);

        public static Shape Rectangle(double x0, double y0, double x1, double y1) => (x, y) => x >= x0 && x < x1 && y >= y0 && y < y1;

        public static Shape RoundedRectangle(double x0, double y0, double x1, double y1, double r) => (x, y) =>
        {
            if (x < x0 || x >= x1 || y < y0 || y >= y1)
                return false;
            double cx = x < x0 + r ? x0 + r : x > x1 - r ? x1 - r : x, cy = y < y0 + r ? y0 + r : y > y1 - r ? y1 - r : y;
            return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
        };

        public static Shape Disc(double cx, double cy, double r) => (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

        public static Shape Ring(double cx, double cy, double outer, double width) => (x, y) =>
        {
            double d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            return d <= outer * outer && d >= (outer - width) * (outer - width);
        };

        public static Shape Minus(Shape a, Shape b) => (x, y) => a(x, y) && !b(x, y);

        public static Shape Union(params Shape[] shapes) => (x, y) =>
        {
            foreach (Shape s in shapes)
            {
                if (s(x, y))
                    return true;
            }

            return false;
        };

        /// <summary>A star with the given number of points, the first straight up.</summary>
        public static Shape Star(double cx, double cy, double outer, double inner, int points)
        {
            var polygon = new double[points * 4];
            for (int i = 0; i < points * 2; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI / points, r = i % 2 == 0 ? outer : inner;
                polygon[i * 2] = cx + r * Math.Cos(angle);
                polygon[i * 2 + 1] = cy + r * Math.Sin(angle);
            }

            return (x, y) =>
            {
                bool inside = false;
                int n = polygon.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double xi = polygon[i * 2], yi = polygon[i * 2 + 1], xj = polygon[j * 2], yj = polygon[j * 2 + 1];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                        inside = !inside;
                }

                return inside;
            };
        }

        /// <summary>The alpha plane of a shape: 255 times the share of Samples by Samples points inside it in each pixel.</summary>
        public static byte[] Alpha(Shape shape, int w, int h)
        {
            var alpha = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                            inside += shape(x + (sx + 0.5) / Samples, y + (sy + 0.5) / Samples) ? 1 : 0;
                    }

                    alpha[y * w + x] = (byte)Math.Round(255.0 * inside / (Samples * Samples));
                }
            }

            return alpha;
        }

        /// <summary>The import's rule: opaque (255) iff alpha >= CustomMask.OpaqueAlpha, else clear.</summary>
        public static byte[] Whole(byte[] alpha)
        {
            var whole = new byte[alpha.Length];
            for (int i = 0; i < alpha.Length; i++)
                whole[i] = alpha[i] >= CustomMask.OpaqueAlpha ? (byte)255 : (byte)0;
            return whole;
        }

        public static byte[] Rgba(byte[] alpha)
        {
            var rgba = new byte[alpha.Length * 4];
            for (int i = 0; i < alpha.Length; i++)
                rgba[i * 4 + 3] = alpha[i];
            return rgba;
        }

        /// <summary>
        /// The cells (scale by scale to a pixel, rows from the top) inside the loops (even-odd, so a hole is out), the loops in the frame of a picture of h pixels traced with its top at y = h, y up, one
        /// unit to a pixel.
        /// </summary>
        public static bool[] Mask(List<List<Point2>> loops, int w, int h, int scale)
        {
            var mask = new bool[w * scale * h * scale];
            var crossings = new List<double>();
            for (int row = 0; row < h * scale; row++)
            {
                double y = h - (row + 0.5) / scale;
                crossings.Clear();
                foreach (List<Point2> loop in loops)
                {
                    for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                    {
                        Point2 a = loop[j], b = loop[i];
                        if ((a.Y > y) != (b.Y > y))
                            crossings.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                    }
                }

                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    int from = Math.Max(0, (int)Math.Ceiling(crossings[k] * scale - 0.5)), to = Math.Min(w * scale - 1, (int)Math.Ceiling(crossings[k + 1] * scale - 0.5) - 1);
                    for (int c = from; c <= to; c++)
                        mask[row * w * scale + c] = true;
                }
            }

            return mask;
        }

        public static bool[] Mask(Shape shape, int w, int h, int scale)
        {
            var mask = new bool[w * scale * h * scale];
            for (int row = 0; row < h * scale; row++)
            {
                for (int c = 0; c < w * scale; c++)
                    mask[row * w * scale + c] = shape((c + 0.5) / scale, (row + 0.5) / scale);
            }

            return mask;
        }

        public static bool[] Mask(byte[] whole, int w, int h, int scale)
        {
            var mask = new bool[w * scale * h * scale];
            for (int row = 0; row < h * scale; row++)
            {
                for (int c = 0; c < w * scale; c++)
                    mask[row * w * scale + c] = whole[(row / scale) * w + c / scale] != 0;
            }

            return mask;
        }

        public static int Count(bool[] mask)
        {
            int n = 0;
            foreach (bool b in mask)
                n += b ? 1 : 0;
            return n;
        }

        /// <summary>How many cells are in a and not in b (or, with both set, in a and b / in either).</summary>
        public static int Minus(bool[] a, bool[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++)
                n += a[i] && !b[i] ? 1 : 0;
            return n;
        }
    }
}
