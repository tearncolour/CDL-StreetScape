using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The outline of the opaque part of a picture at the picture's own resolution (0.12.73; the owner: custom pictures in cut mode). CustomMask and Custom.Cut go from the picture to a grid of at most
    /// 192 cells, from that to a grid of 160 cells on the board, and trace the second one: the outline is eroded by up to a cell (5 px of a 1024 px picture) and a stroke or a hole narrower than a cell
    /// is gone. Here the loops come from the pixels themselves, in the frame CutOutline.Trace gives (outer counter-clockwise, holes clockwise, y up; pixel (x, y) is the square from (x, y) to (x + 1, y + 1)
    /// before the frame is applied), and their points are dropped to a tolerance in pixels (Douglas-Peucker, CutOutline.Simplify: coarse where the edge is straight, fine where it bends). Pure.
    /// A picture of whole-or-nothing alpha (what the import stores: opaque iff alpha >= CustomMask.OpaqueAlpha) has nothing to interpolate: its outline is the boundary of its opaque pixel squares,
    /// where the picture's opaque area really ends, so a rectangle keeps its corners (joining the mid-edge points of marching squares would cut each corner by half a pixel). A picture with graded
    /// alpha is traced by marching squares on the alpha field, the vertex of each crossing at the linear interpolation of the threshold between the two pixel centres. Marching squares as described by
    /// Lorensen and Cline (1987) for the 2-D case; nothing is taken from Potrace (GPL).
    /// </summary>
    public static class ContourTrace
    {
        /// <summary>How far (pixels) a point may be from the line it is dropped for: the steps of a slanting edge go (up to 0.7 px off its line), a corner stays.</summary>
        public const float DefaultTolerance = 0.5f;

        /// <summary>The smallest loop kept (pixels squared, a hole or an island): a speck smaller than 2 by 2 pixels is noise.</summary>
        public const float DefaultMinArea = 4f;

        /// <summary>
        /// The loops of the pixels with alpha >= threshold of an image (RGBA, or an alpha plane when rgba is false; rows from the top), outside the image clear. A pixel is pixelW by pixelH, the image's
        /// top left corner at (left, top) of the frame the loops are in (y up). tolerance: pixels; minArea: pixels squared.
        /// </summary>
        public static List<List<Point2>> Trace(byte[] data, int width, int height, bool rgba, byte threshold, float tolerance, float minArea, float left, float top, float pixelW, float pixelH)
        {
            var loops = new List<List<Point2>>();
            int step = rgba ? 4 : 1, shift = rgba ? 3 : 0;
            if (data == null || width < 1 || height < 1 || data.Length < width * height * step)
                return loops;
            bool graded = false;
            for (int i = shift; i < width * height * step && !graded; i += step)
                graded = data[i] != 0 && data[i] != 255;
            float tol = tolerance * Math.Min(pixelW, pixelH), least = minArea * pixelW * pixelH;
            List<List<Point2>> raw = graded
                ? Marching(data, width, height, step, shift, threshold, left, top, pixelW, pixelH)
                : CutOutline.Trace((i, j) => data[(j * width + i) * step + shift] >= threshold, width, height, pixelW, pixelH, left, top, 0f);
            foreach (List<Point2> loop in raw)
            {
                if (Math.Abs(CutOutline.Area(loop)) < least)
                    continue;
                List<Point2> kept = DropCollinear(CutOutline.Simplify(loop, tol), 1e-3f * Math.Min(pixelW, pixelH));
                if (kept.Count >= 3)
                    loops.Add(kept);
            }

            return loops;
        }

        /// <summary>
        /// Marching squares on the lattice of the pixel centres, the frame of one pixel of clear round the image closing every loop. A cell's crossings are found going clockwise round it (top, right,
        /// bottom, left); a segment goes from the crossing where that walk leaves the opaque side to the one where it enters it, which keeps the opaque side on the right as the picture is seen:
        /// that is clockwise for an outer loop, so each loop is reversed when it is taken, and outer loops are counter-clockwise (y up) as CutOutline.Trace has them. At a saddle (two opposite corners opaque) the corners are joined through the middle only when the mean of the four is at least the threshold: otherwise
        /// they are two loops that touch at no point, as CutOutline.Trace has two cells that meet at a corner.
        /// </summary>
        private static List<List<Point2>> Marching(byte[] data, int width, int height, int step, int shift, byte threshold, float left, float top, float pw, float ph)
        {
            int stride = width + 2;
            int A(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? 0 : data[(y * width + x) * step + shift];
            // The id of the lattice edge from pixel (x, y) to (x + 1, y) is 2 * index, to (x, y + 1) 2 * index + 1.
            int Horizontal(int x, int y) => 2 * ((y + 1) * stride + x + 1);
            int Vertical(int x, int y) => 2 * ((y + 1) * stride + x + 1) + 1;
            var next = new Dictionary<int, int>();
            var ids = new int[4];
            var out0 = new bool[4];
            for (int j = -1; j < height; j++)
            {
                for (int i = -1; i < width; i++)
                {
                    int tl = A(i, j), tr = A(i + 1, j), br = A(i + 1, j + 1), bl = A(i, j + 1);
                    bool a = tl >= threshold, b = tr >= threshold, c = br >= threshold, d = bl >= threshold;
                    if (a == b && b == c && c == d)
                        continue;
                    int n = 0;
                    if (a != b) { ids[n] = Horizontal(i, j); out0[n++] = a; }
                    if (b != c) { ids[n] = Vertical(i + 1, j); out0[n++] = b; }
                    if (c != d) { ids[n] = Horizontal(i, j + 1); out0[n++] = c; }
                    if (d != a) { ids[n] = Vertical(i, j); out0[n++] = d; }
                    if (n == 2)
                    {
                        int from = out0[0] ? 0 : 1;
                        next[ids[from]] = ids[1 - from];
                        continue;
                    }

                    bool joined = (tl + tr + br + bl) / 4f >= threshold;
                    for (int k = 0; k < 4; k++)
                    {
                        if (out0[k])
                            next[ids[k]] = ids[joined ? (k + 1) % 4 : (k + 3) % 4];
                    }
                }
            }

            Point2 At(int id)
            {
                int index = id >> 1, x = index % stride - 1, y = index / stride - 1;
                int qx = (id & 1) == 0 ? x + 1 : x, qy = (id & 1) == 0 ? y : y + 1;
                int p = A(x, y), q = A(qx, qy);
                // Where the alpha, going linearly from one centre to the other, is the threshold; the two are on different sides of it, so q differs from p.
                float t = Math.Max(0f, Math.Min(1f, (threshold - p) / (float)(q - p)));
                float px = x + 0.5f + t * (qx - x), py = y + 0.5f + t * (qy - y);
                return new Point2(left + px * pw, top - py * ph);
            }

            var loops = new List<List<Point2>>();
            while (next.Count > 0)
            {
                int start = 0;
                foreach (int k in next.Keys)
                {
                    start = k;
                    break;
                }

                var loop = new List<Point2>();
                int at = start;
                while (next.TryGetValue(at, out int to))
                {
                    loop.Add(At(at));
                    next.Remove(at);
                    at = to;
                }

                if (loop.Count >= 3)
                {
                    loop.Reverse();
                    loops.Add(loop);
                }
            }

            return loops;
        }

        /// <summary>Loops traced at a finer tolerance, dropped to a coarser one (pixels; the loops are in pixel units): the board of a picture that would otherwise cost too many triangles (PictureCut).</summary>
        public static List<List<Point2>> Relax(List<List<Point2>> loops, float tolerance)
        {
            var result = new List<List<Point2>>(loops.Count);
            foreach (List<Point2> loop in loops)
            {
                List<Point2> kept = DropCollinear(CutOutline.Simplify(loop, tolerance), 1e-3f);
                if (kept.Count >= 3)
                    result.Add(kept);
            }

            return result;
        }

        /// <summary>The loop without the points that are on the line through their neighbours (to eps): a straight edge that DP kept a point on, as its anchor, is one edge.</summary>
        private static List<Point2> DropCollinear(List<Point2> loop, float eps)
        {
            var points = new List<Point2>(loop);
            for (bool changed = true; changed && points.Count > 3;)
            {
                changed = false;
                for (int i = 0; i < points.Count && points.Count > 3; i++)
                {
                    Point2 a = points[(i + points.Count - 1) % points.Count], b = points[i], c = points[(i + 1) % points.Count];
                    float lx = c.X - a.X, ly = c.Y - a.Y, length = (float)Math.Sqrt(lx * lx + ly * ly);
                    float d = length > 1e-9f ? Math.Abs((b.X - a.X) * ly - (b.Y - a.Y) * lx) / length : 0f;
                    if (d <= eps)
                    {
                        points.RemoveAt(i--);
                        changed = true;
                    }
                }
            }

            return points;
        }
    }
}
