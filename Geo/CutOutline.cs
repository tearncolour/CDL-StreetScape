using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Which cells of a player's picture are opaque (0.12.30; the owner: "PNG应该支持透明通道，但是不允许半透明，半透明一律纯透明处理"): a grid over the picture, a cell opaque only when every pixel in it is
    /// opaque (OpaqueAlpha), so that the board cut from it never shows what is not the picture. Kept beside the picture as a small file (Encode: "TSM1", the width and height as two bytes each, then the cells a
    /// bit each, row by row from the top). Pure.
    /// </summary>
    public sealed class CustomMask
    {
        /// <summary>The longest side of a mask (cells): a board of a metre is cut to about half a centimetre.</summary>
        public const int MaxSide = 192;

        /// <summary>
        /// The least alpha that is opaque: 250 of 255, not 255, because many pictures that are opaque to the eye are stored at 254 (the owner's first picture was, all of it); below it a pixel is clear.
        /// </summary>
        public const byte OpaqueAlpha = 250;

        public int Width, Height;
        public bool[] Cells;

        public bool At(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Cells[y * Width + x];

        /// <summary>Whether the cell under (u, v) of the picture (0 to 1 each way, v down) is opaque.</summary>
        public bool AtUv(float u, float v) => At((int)Math.Floor(u * Width), (int)Math.Floor(v * Height));

        /// <summary>
        /// The mask of a picture's alpha (RGBA, rows from the top): cells of at most MaxSide on the longer side, each opaque when all its pixels are (OpaqueAlpha). Null when every pixel is opaque (the
        /// picture needs no cut).
        /// </summary>
        public static CustomMask FromAlpha(byte[] rgba, int width, int height)
        {
            bool any = false;
            for (int i = 3; i < rgba.Length && !any; i += 4)
                any = rgba[i] < OpaqueAlpha;
            if (!any)
                return null;
            float scale = Math.Min(1f, (float)MaxSide / Math.Max(width, height));
            int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
            var mask = new CustomMask { Width = w, Height = h, Cells = new bool[w * h] };
            for (int cy = 0; cy < h; cy++)
            {
                int y0 = cy * height / h, y1 = Math.Max(y0 + 1, (cy + 1) * height / h);
                for (int cx = 0; cx < w; cx++)
                {
                    int x0 = cx * width / w, x1 = Math.Max(x0 + 1, (cx + 1) * width / w);
                    bool opaque = true;
                    for (int y = y0; y < y1 && opaque; y++)
                    {
                        for (int x = x0; x < x1 && opaque; x++)
                            opaque = rgba[(y * width + x) * 4 + 3] >= OpaqueAlpha;
                    }

                    mask.Cells[cy * w + cx] = opaque;
                }
            }

            return mask;
        }

        public byte[] Encode()
        {
            var data = new byte[8 + (Width * Height + 7) / 8];
            data[0] = (byte)'T';
            data[1] = (byte)'S';
            data[2] = (byte)'M';
            data[3] = (byte)'1';
            data[4] = (byte)(Width >> 8);
            data[5] = (byte)Width;
            data[6] = (byte)(Height >> 8);
            data[7] = (byte)Height;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i])
                    data[8 + i / 8] |= (byte)(1 << (i % 8));
            }

            return data;
        }

        /// <summary>The mask of a file; null when it is not one.</summary>
        public static CustomMask Decode(byte[] data)
        {
            if (data == null || data.Length < 8 || data[0] != 'T' || data[1] != 'S' || data[2] != 'M' || data[3] != '1')
                return null;
            int w = data[4] << 8 | data[5], h = data[6] << 8 | data[7];
            if (w < 1 || h < 1 || w > MaxSide || h > MaxSide || data.Length != 8 + (w * h + 7) / 8)
                return null;
            var mask = new CustomMask { Width = w, Height = h, Cells = new bool[w * h] };
            for (int i = 0; i < mask.Cells.Length; i++)
                mask.Cells[i] = (data[8 + i / 8] & (1 << (i % 8))) != 0;
            return mask;
        }
    }

    /// <summary>
    /// The outline of the opaque cells of a grid, as the board of a player's picture is cut (0.12.30): the edges between opaque and clear cells joined into closed loops (an outer one counter-clockwise, a
    /// hole clockwise, y up), their steps smoothed by dropping the points that lie within a fraction of a cell of the line through their neighbours; and the triangles that fill them. Pure.
    /// </summary>
    public static class CutOutline
    {
        /// <summary>How far (cells) a point may be from the line it is dropped for: the steps of a slanting edge go (they are up to 0.7 of a cell off its line), a corner stays.</summary>
        public const float Tolerance = 1f;

        /// <summary>
        /// The loops round the opaque cells of a w by h grid (cell (i, j) opaque when opaque(i, j); j counts down from the top). A cell is cellW by cellH; the grid's top left corner is at (left, top) of
        /// the frame the loops are in (y up: row j is between top - j cellH and top - (j + 1) cellH). tolerance: how far (cells) a point may be off the line it is dropped for (0.12.49: 0 keeps every turn,
        /// for cells of different sizes traced by their indices, LampBoxes.Rects).
        /// </summary>
        public static List<List<Point2>> Trace(Func<int, int, bool> opaque, int w, int h, float cellW, float cellH, float left, float top, float tolerance = Tolerance)
        {
            bool At(int i, int j) => i >= 0 && j >= 0 && i < w && j < h && opaque(i, j);

            // The boundary edges, each from one corner to the next with the opaque cell on its left (y up): corners are (i, j) with j counted down, so an edge's direction is kept in grid steps.
            var next = new Dictionary<long, List<long>>();
            long Key(int x, int y) => (long)x * 100003L + y;
            void Edge(int x0, int y0, int x1, int y1)
            {
                long a = Key(x0, y0);
                if (!next.TryGetValue(a, out List<long> list))
                    next[a] = list = new List<long>(1);
                list.Add(Key(x1, y1));
            }

            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    if (!At(i, j))
                        continue;
                    // In the grid (y down) an outer loop goes clockwise; seen with y up it is counter-clockwise.
                    if (!At(i, j - 1))
                        Edge(i + 1, j, i, j);
                    if (!At(i - 1, j))
                        Edge(i, j, i, j + 1);
                    if (!At(i, j + 1))
                        Edge(i, j + 1, i + 1, j + 1);
                    if (!At(i + 1, j))
                        Edge(i + 1, j + 1, i + 1, j);
                }
            }

            var loops = new List<List<Point2>>();
            while (next.Count > 0)
            {
                long start = 0;
                foreach (long k in next.Keys)
                {
                    start = k;
                    break;
                }

                var corners = new List<long>();
                long at = start, from = long.MinValue;
                for (int guard = 0; guard < 4 * (w + 1) * (h + 1) + 4; guard++)
                {
                    if (!next.TryGetValue(at, out List<long> outs) || outs.Count == 0)
                        break;
                    long to = Pick(outs, from, at);
                    outs.Remove(to);
                    if (outs.Count == 0)
                        next.Remove(at);
                    corners.Add(at);
                    from = at;
                    at = to;
                    if (at == start)
                        break;
                }

                if (corners.Count < 4)
                    continue;
                var loop = new List<Point2>(corners.Count);
                foreach (long c in corners)
                {
                    int x = (int)(c / 100003L), y = (int)(c % 100003L);
                    loop.Add(new Point2(left + x * cellW, top - y * cellH));
                }

                loops.Add(Simplify(loop, tolerance * Math.Min(cellW, cellH)));
            }

            return loops;
        }

        /// <summary>At a corner where two loops touch (two opaque cells meeting at a point), the turn that keeps the opaque cell on the left: the sharpest left turn, so the loops do not cross.</summary>
        private static long Pick(List<long> outs, long from, long at)
        {
            if (outs.Count == 1 || from == long.MinValue)
                return outs[0];
            long ax = at / 100003L, ay = at % 100003L, fx = from / 100003L, fy = from % 100003L;
            long dx = ax - fx, dy = ay - fy;
            long best = outs[0];
            int bestTurn = int.MaxValue;
            foreach (long o in outs)
            {
                long ox = o / 100003L - ax, oy = o % 100003L - ay;
                // In the grid (y down) a left turn as seen with y up is a cross product of the other sign.
                long cross = dx * oy - dy * ox;
                int turn = cross < 0 ? 0 : cross == 0 ? 1 : 2;
                if (turn < bestTurn)
                {
                    bestTurn = turn;
                    best = o;
                }
            }

            return best;
        }

        /// <summary>
        /// A closed loop with the points dropped that lie within tolerance of the line through the points kept on either side (Douglas-Peucker on each half, split at the farthest points). It starts at
        /// its leftmost point, a corner: begun next to a corner it would cut it.
        /// </summary>
        public static List<Point2> Simplify(List<Point2> loop, float tolerance)
        {
            if (loop.Count < 5)
                return loop;
            int first = 0;
            for (int i = 1; i < loop.Count; i++)
            {
                if (loop[i].X < loop[first].X || (loop[i].X == loop[first].X && loop[i].Y < loop[first].Y))
                    first = i;
            }

            loop = loop.GetRange(first, loop.Count - first).Concat(loop.GetRange(0, first)).ToList();
            int far = 0;
            float best = -1f;
            for (int i = 1; i < loop.Count; i++)
            {
                float dx = loop[i].X - loop[0].X, dy = loop[i].Y - loop[0].Y, d = dx * dx + dy * dy;
                if (d > best)
                {
                    best = d;
                    far = i;
                }
            }

            var keep = new bool[loop.Count];
            keep[0] = keep[far] = true;
            Keep(loop, 0, far, tolerance, keep);
            Keep(loop, far, loop.Count, tolerance, keep);
            var result = new List<Point2>();
            for (int i = 0; i < loop.Count; i++)
            {
                if (keep[i])
                    result.Add(loop[i]);
            }

            return result.Count >= 3 ? result : loop;
        }

        private static void Keep(List<Point2> loop, int from, int to, float tolerance, bool[] keep)
        {
            if (to - from < 2)
                return;
            Point2 a = loop[from], b = loop[to % loop.Count];
            float lx = b.X - a.X, ly = b.Y - a.Y, length = (float)Math.Sqrt(lx * lx + ly * ly);
            int far = -1;
            float best = tolerance;
            for (int i = from + 1; i < to; i++)
            {
                float px = loop[i].X - a.X, py = loop[i].Y - a.Y;
                float d = length > 1e-6f ? Math.Abs(px * ly - py * lx) / length : (float)Math.Sqrt(px * px + py * py);
                if (d > best)
                {
                    best = d;
                    far = i;
                }
            }

            if (far < 0)
                return;
            keep[far] = true;
            Keep(loop, from, far, tolerance, keep);
            Keep(loop, far, to, tolerance, keep);
        }

        /// <summary>The same sum as for any list, for a List: no interface call and no division for each point (the planner of a text light box asks it of every loop of every letter).</summary>
        public static float Area(List<Point2> loop)
        {
            double sum = 0.0;
            for (int i = 0, n = loop.Count; i < n; i++)
            {
                Point2 a = loop[i], b = loop[i + 1 == n ? 0 : i + 1];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }

            return (float)(sum * 0.5);
        }

        public static float Area(IList<Point2> loop)
        {
            double sum = 0.0;
            for (int i = 0; i < loop.Count; i++)
            {
                Point2 a = loop[i], b = loop[(i + 1) % loop.Count];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }

            return (float)(sum * 0.5);
        }

        private static bool Inside(IList<Point2> loop, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                if ((loop[i].Y > y) != (loop[j].Y > y) && x < (loop[j].X - loop[i].X) * (y - loop[i].Y) / (loop[j].Y - loop[i].Y) + loop[i].X)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>
        /// The triangles that fill the loops (outer loops counter-clockwise, holes clockwise): each hole with the smallest outer loop round it. The points are the loops' own (x, y), the triangles
        /// counter-clockwise seen with y up.
        /// </summary>
        public static void Triangulate(List<List<Point2>> loops, List<Point2> points, List<int> triangles)
        {
            var outers = new List<List<Point2>>();
            var holes = new List<List<IList<Vec3>>>();
            foreach (List<Point2> loop in loops)
            {
                if (Area(loop) > 0f)
                {
                    outers.Add(loop);
                    holes.Add(new List<IList<Vec3>>());
                }
            }

            foreach (List<Point2> loop in loops)
            {
                if (Area(loop) >= 0f)
                    continue;
                int owner = -1;
                float smallest = float.MaxValue;
                for (int k = 0; k < outers.Count; k++)
                {
                    float area = Area(outers[k]);
                    if (area < smallest && Inside(outers[k], loop[0].X * 0.5f + loop[1].X * 0.5f, loop[0].Y * 0.5f + loop[1].Y * 0.5f))
                    {
                        smallest = area;
                        owner = k;
                    }
                }

                if (owner >= 0)
                    holes[owner].Add(ToVec(loop));
            }

            for (int k = 0; k < outers.Count; k++)
            {
                var mesh = new MeshBuilder();
                Polygon.Fill(mesh, ToVec(outers[k]), holes[k]);
                int first = points.Count;
                foreach (Vec3 p in mesh.Positions)
                    points.Add(new Point2(p.X, p.Z));
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    int a = first + mesh.Indices[t], b = first + mesh.Indices[t + 1], c = first + mesh.Indices[t + 2];
                    Point2 pa = points[a], pb = points[b], pc = points[c];
                    bool ccw = (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X) > 0f;
                    triangles.Add(a);
                    triangles.Add(ccw ? b : c);
                    triangles.Add(ccw ? c : b);
                }
            }
        }

        private static List<Vec3> ToVec(IList<Point2> loop)
        {
            var list = new List<Vec3>(loop.Count);
            foreach (Point2 p in loop)
                list.Add(new Vec3(p.X, 0f, p.Y));
            return list;
        }
    }
}
