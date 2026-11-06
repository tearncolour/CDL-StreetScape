using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The lamp-post light boxes' designs (0.12.49, LampAds.Box; the owner: "制作更多样式，包括灯箱样式"; the photos and sizes in scratch/research-lampads/REPORT.md 9): what is round a box and what holds it
    /// (PoleAd.Plates, Rods, Balls, Arms, Hoops), its ornaments drawn as distance fields (negative inside, metres to the edge) and traced into loops, its frames as unions of rectangles traced
    /// exactly. Every loop is round a middle with y up, outer ones anticlockwise and holes clockwise, as Prims.LoopsFace takes them. Pure.
    /// </summary>
    public static class LampBoxes
    {
        // ------------------------------------------------------------------ fields

        /// <summary>A disc of radius r round (cx, cy).</summary>
        public static float Circle(float x, float y, float cx, float cy, float r) => (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        /// <summary>A rectangle of half sizes hx, hy round (cx, cy).</summary>
        public static float Box(float x, float y, float cx, float cy, float hx, float hy)
        {
            float dx = Math.Abs(x - cx) - hx, dy = Math.Abs(y - cy) - hy;
            float ox = Math.Max(dx, 0f), oy = Math.Max(dy, 0f);
            return (float)Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(dx, dy), 0f);
        }

        /// <summary>A stroke r wide each side of the segment from a to b, round at its ends.</summary>
        public static float Segment(float x, float y, float ax, float ay, float bx, float by, float r)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, (px * dx + py * dy) / l2));
            float ex = px - dx * t, ey = py - dy * t;
            return (float)Math.Sqrt(ex * ex + ey * ey) - r;
        }

        /// <summary>The signed distance to a polygon (any simple one): the distance to its nearest edge, negative inside (even-odd).</summary>
        public static float Polygon(float x, float y, IList<Point2> polygon)
        {
            float best = float.MaxValue;
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Point2 a = polygon[i], b = polygon[j];
                best = Math.Min(best, Segment(x, y, a.X, a.Y, b.X, b.Y, 0f));
                if ((a.Y > y) != (b.Y > y) && x < a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y))
                    inside = !inside;
            }

            return inside ? -best : best;
        }

        /// <summary>
        /// The loops where a field is negative (marching squares on cells of the size given, the crossings put where the field is zero along each side), with the points dropped that are within
        /// half a cell of the line through their neighbours. The field must be positive along the edge of the box (x0, y0) to (x1, y1).
        /// </summary>
        public static List<List<Point2>> Contour(Func<float, float, float> field, float x0, float y0, float x1, float y1, float cell)
        {
            int nx = Math.Max(2, (int)Math.Ceiling((x1 - x0) / cell)), ny = Math.Max(2, (int)Math.Ceiling((y1 - y0) / cell)), row = nx + 1;
            var f = new float[row * (ny + 1)];
            for (int j = 0; j <= ny; j++)
            {
                for (int i = 0; i <= nx; i++)
                {
                    // Not zero on a node: a crossing there would be put on the node twice, once for each side that meets it.
                    float v = field(x0 + i * cell, y0 + j * cell);
                    f[j * row + i] = v == 0f ? 1e-7f : v;
                }
            }

            // A crossing's key: the side of a cell it is on, horizontal (from node (i, j) to (i + 1, j)) or vertical (from (i, j) to (i, j + 1)).
            int H(int i, int j) => 2 * (j * row + i);
            int V(int i, int j) => 2 * (j * row + i) + 1;
            Point2 At(int key)
            {
                int node = key >> 1, i = node % row, j = node / row;
                int other = (key & 1) == 0 ? node + 1 : node + row;
                float fa = f[node], fb = f[other], t = fa / (fa - fb);
                return (key & 1) == 0 ? new Point2(x0 + (i + t) * cell, y0 + j * cell) : new Point2(x0 + i * cell, y0 + (j + t) * cell);
            }

            // Each cell joins the crossing where its edge, walked anticlockwise, leaves the inside (B) to one where it comes back in (A), so that the inside is on the left of every loop.
            var next = new Dictionary<int, int>();
            var sides = new int[4];
            var corners = new bool[4];
            var crossing = new List<(int Key, bool In)>(4);
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    float c0 = f[j * row + i], c1 = f[j * row + i + 1], c2 = f[(j + 1) * row + i + 1], c3 = f[(j + 1) * row + i];
                    corners[0] = c0 < 0f;
                    corners[1] = c1 < 0f;
                    corners[2] = c2 < 0f;
                    corners[3] = c3 < 0f;
                    if (corners[0] == corners[1] && corners[1] == corners[2] && corners[2] == corners[3])
                        continue;
                    sides[0] = H(i, j);
                    sides[1] = V(i + 1, j);
                    sides[2] = H(i, j + 1);
                    sides[3] = V(i, j);
                    crossing.Clear();
                    for (int k = 0; k < 4; k++)
                    {
                        if (corners[k] != corners[(k + 1) & 3])
                            crossing.Add((sides[k], corners[(k + 1) & 3]));
                    }

                    // A saddle (two inside corners across from each other): the insides join through the middle when it is inside.
                    bool joined = crossing.Count == 4 && c0 + c1 + c2 + c3 < 0f;
                    for (int k = 0; k < crossing.Count; k++)
                    {
                        if (crossing[k].In)
                            continue;
                        int to = joined ? (k + 1) % crossing.Count : (k + crossing.Count - 1) % crossing.Count;
                        next[crossing[k].Key] = crossing[to].Key;
                    }
                }
            }

            var loops = new List<List<Point2>>();
            var done = new HashSet<int>();
            foreach (int start in next.Keys)
            {
                if (done.Contains(start))
                    continue;
                var loop = new List<Point2>();
                int at = start;
                while (done.Add(at))
                {
                    loop.Add(At(at));
                    if (!next.TryGetValue(at, out at))
                        break;
                }

                if (loop.Count >= 3 && Math.Abs(CutOutline.Area(loop)) > cell * cell)
                    loops.Add(CutOutline.Simplify(loop, 0.5f * cell));
            }

            return loops;
        }

        /// <summary>
        /// The loops round a union of rectangles (x0, y0, x1, y1 each), exactly: the cells between all their edges are filled where a rectangle covers them and traced, only the points where an edge
        /// turns kept.
        /// </summary>
        public static List<List<Point2>> Rects(IList<(float X0, float Y0, float X1, float Y1)> rects)
        {
            var xs = new SortedSet<float>();
            var ys = new SortedSet<float>();
            foreach ((float x0, float y0, float x1, float y1) in rects)
            {
                xs.Add(x0);
                xs.Add(x1);
                ys.Add(y0);
                ys.Add(y1);
            }

            var x = new List<float>(xs);
            var y = new List<float>(ys);
            y.Reverse();
            bool Filled(int i, int j)
            {
                float cx = (x[i] + x[i + 1]) * 0.5f, cy = (y[j] + y[j + 1]) * 0.5f;
                foreach ((float x0, float y0, float x1, float y1) in rects)
                {
                    if (cx > x0 && cx < x1 && cy > y0 && cy < y1)
                        return true;
                }

                return false;
            }

            // Traced on the grid of the cells' indices (one unit each, rows down from the top), then each corner put where its edges are.
            var loops = new List<List<Point2>>();
            foreach (List<Point2> loop in CutOutline.Trace(Filled, x.Count - 1, y.Count - 1, 1f, 1f, 0f, 0f, 0f))
            {
                var real = new List<Point2>(loop.Count);
                foreach (Point2 p in loop)
                    real.Add(new Point2(x[(int)Math.Round(p.X)], y[(int)Math.Round(-p.Y)]));
                loops.Add(real);
            }

            return loops;
        }

        /// <summary>The loops mirrored left to right (the second part of a pair, its side towards the post on the other side): each turned the other way round so that it keeps its inside on the left.</summary>
        public static List<List<Point2>> Mirrored(List<List<Point2>> loops)
        {
            var mirrored = new List<List<Point2>>(loops.Count);
            foreach (List<Point2> loop in loops)
            {
                var m = new List<Point2>(loop.Count);
                for (int i = loop.Count - 1; i >= 0; i--)
                    m.Add(new Point2(-loop[i].X, loop[i].Y));
                mirrored.Add(m);
            }

            return mirrored;
        }

        /// <summary>The box round the loops.</summary>
        public static void Bounds(IEnumerable<List<Point2>> loops, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue;
            x1 = y1 = float.MinValue;
            foreach (List<Point2> loop in loops)
            {
                foreach (Point2 p in loop)
                {
                    x0 = Math.Min(x0, p.X);
                    y0 = Math.Min(y0, p.Y);
                    x1 = Math.Max(x1, p.X);
                    y1 = Math.Max(y1, p.Y);
                }
            }
        }

        // ------------------------------------------------------------------ ornaments (fields of x and y)

        /// <summary>The union of fields: the least of them.</summary>
        public static Func<float, float, float> Union(params Func<float, float, float>[] fields) => (x, y) =>
        {
            float d = float.MaxValue;
            foreach (Func<float, float, float> f in fields)
                d = Math.Min(d, f(x, y));
            return d;
        };

        /// <summary>A field with the holes of another cut out of it.</summary>
        public static Func<float, float, float> Minus(Func<float, float, float> field, Func<float, float, float> holes) => (x, y) => Math.Max(field(x, y), -holes(x, y));

        /// <summary>A field the same on both sides of x = 0 (what is drawn for x &gt;= 0 is mirrored).</summary>
        public static Func<float, float, float> Both(Func<float, float, float> half) => (x, y) => half(Math.Abs(x), y);

        /// <summary>
        /// A scroll: a stroke r wide each side from the angle a0 round (cx, cy) through sweep (radians, anticlockwise when positive), its radius going from radius down to shrink of it, that ends in a ball.
        /// </summary>
        public static Func<float, float, float> Spiral(float cx, float cy, float radius, double a0, double sweep, float shrink, float r, float ball)
        {
            const int n = 20;
            var px = new float[n + 1];
            var py = new float[n + 1];
            for (int k = 0; k <= n; k++)
            {
                double f = k / (double)n, a = a0 + sweep * f, rr = radius * (1.0 - (1.0 - shrink) * f);
                px[k] = cx + (float)(rr * Math.Cos(a));
                py[k] = cy + (float)(rr * Math.Sin(a));
            }

            float half = radius + Math.Max(ball, r);
            return (x, y) =>
            {
                // Far from its box the distance to the box will do: a field is only looked at for its sign and near its edge.
                float outside = Box(x, y, cx, cy, half, half);
                if (outside > 0.02f)
                    return outside;
                float d = Circle(x, y, px[n], py[n], ball);
                for (int k = 0; k < n; k++)
                    d = Math.Min(d, Segment(x, y, px[k], py[k], px[k + 1], py[k + 1], r));
                return d;
            };
        }

        /// <summary>
        /// A crest of cloud scrolls (云头) w wide and h high on a band along y = 0 that reaches sink below it (into what it stands on): a 如意 head in the middle (two scrolls that meet over a stem) and
        /// on each side scrolls sitting on the band, curling in and out by turns, a little smaller towards the ends; after the LED crests on the boxes of L1, L2 and dl-box-q2-t15 (REPORT.md 9), its
        /// strokes 13 % of its height wide. Symmetric.
        /// </summary>
        public static Func<float, float, float> Crest(float w, float h, float sink)
        {
            float t = 0.065f * h, ball = 1.3f * t, band = 0.09f * h;
            var parts = new List<Func<float, float, float>> { (x, y) => Box(x, y, 0f, (band - sink) * 0.5f, 0.5f * w - 0.03f * h, (band + sink) * 0.5f) };
            float hc = 0.15f * h, hr = 0.30f * h, hy = h - hr - t;
            double a = 235.0 * Math.PI / 180.0;
            parts.Add(Spiral(hc, hy, hr, a, 310.0 * Math.PI / 180.0, 0.45f, t, ball));
            float sx = hc + hr * (float)Math.Cos(a), sy = hy + hr * (float)Math.Sin(a);
            parts.Add((x, y) => Segment(x, y, sx, sy, 0f, band * 0.5f, t));
            float start = hc + hr + t, end = 0.5f * w;
            int n = Math.Max(1, (int)Math.Round((end - start) / (0.55f * h)));
            float step = (end - start) / n;
            for (int k = 0; k < n; k++)
            {
                float r = Math.Min(0.25f * h, 0.5f * step) * (1f - 0.15f * k / Math.Max(1, n - 1));
                float cx = start + step * (k + 0.5f), cy = band + r + 0.02f * h;
                // The last one (at the end) curls in, towards the middle.
                bool inward = (n - 1 - k) % 2 == 0;
                double a0 = (inward ? 255.0 : 285.0) * Math.PI / 180.0;
                parts.Add(Spiral(cx, cy, r, a0, (inward ? 300.0 : -300.0) * Math.PI / 180.0, 0.45f, t, ball));
                float bx = cx + r * (float)Math.Cos(a0), by = cy + r * (float)Math.Sin(a0);
                parts.Add((x, y) => Segment(x, y, bx, by, bx, band * 0.5f, t));
            }

            return Both(Union(parts.ToArray()));
        }

        /// <summary>
        /// A Chinese knot (盘长结) of half-diagonal d round (0, 0): a diamond woven of strands (each way its two sides and inner ones between: two make the usual nine openings, one the four of a bold
        /// knot), two ear loops on each side and a loop on top; with a tassel, under it a cord, a bead, a collar and a fringe of threads (slits) to 2.85 d below the middle. stroke: the strands' half
        /// width over d.
        /// </summary>
        public static Func<float, float, float> Knot(float d, int inner, float stroke, bool tassel)
        {
            float t = stroke * d, s2 = (float)Math.Sqrt(0.5), a = d * s2;
            var parts = new List<Func<float, float, float>>();
            for (int k = 0; k <= inner + 1; k++)
            {
                // The lines p = c and q = c of the weave (p and q along the diagonals) from one side of the diamond to the other.
                float p = a * (-1f + 2f * k / (inner + 1));
                float ax = (p + a) * s2, ay = (p - a) * s2, bx = (p - a) * s2, by = (p + a) * s2;
                float cx = (-a - p) * s2, cy = (p - a) * s2, ex = (a - p) * s2, ey = (a + p) * s2;
                parts.Add((x, y) => Math.Min(Segment(x, y, ax, ay, bx, by, t), Segment(x, y, cx, cy, ex, ey, t)));
            }

            float ear = 0.25f * d;
            parts.Add(Both((x, y) => Math.Min(Math.Abs(Circle(x, y, 1.13f * d, 0.27f * d, ear)) - t, Math.Abs(Circle(x, y, 1.13f * d, -0.27f * d, ear)) - t)));
            parts.Add((x, y) => Math.Abs(Circle(x, y, 0f, 1.22f * d, 0.22f * d)) - t);
            if (tassel)
            {
                parts.Add((x, y) => Segment(x, y, 0f, -d, 0f, -1.75f * d, 1.1f * t));
                parts.Add((x, y) => Circle(x, y, 0f, -1.38f * d, 0.13f * d));
                parts.Add((x, y) => Box(x, y, 0f, -1.70f * d, 0.10f * d, 0.07f * d));
                var fringe = new List<Point2> { new Point2(-0.12f * d, -1.76f * d), new Point2(0.12f * d, -1.76f * d), new Point2(0.34f * d, -2.85f * d), new Point2(-0.34f * d, -2.85f * d) };
                parts.Add(Minus((x, y) => Polygon(x, y, fringe),
                    (x, y) => Math.Min(Box(x, y, 0f, -2.38f * d, 0.022f * d, 0.36f * d), Box(Math.Abs(x), y, 0.15f * d, -2.38f * d, 0.022f * d, 0.36f * d))));
            }

            return Union(parts.ToArray());
        }

        /// <summary>
        /// What hangs under a box with crests (如意云头): the crest upside down, ch high and 0.9 of the width, and from its head down a stem to a bold knot with its tassel; the knot's half-diagonal
        /// what is left of hp over 4.05, at most 0.3 of the width (the knot is 3 half-diagonals wide). height: how far below y = 0 it reaches. The band reaches sink above y = 0 (into the box).
        /// </summary>
        public static Func<float, float, float> Pendant(float w, float ch, float hp, float sink, out float height)
        {
            Func<float, float, float> crest = Crest(0.9f * w, ch, sink);
            float d = Math.Min((hp - ch) / 4.05f, 0.3f * w), ky = -ch - 1.2f * d, stem = 0.06f * ch;
            height = ch + 4.05f * d;
            Func<float, float, float> knot = Knot(d, 1, 0.12f, true);
            return Union((x, y) => crest(x, -y), (x, y) => Segment(x, y, 0f, -0.5f * ch, 0f, ky + 1.3f * d, stem), (x, y) => knot(x, y - ky));
        }

        // ------------------------------------------------------------------ the fan

        /// <summary>
        /// The fan (扇形灯箱) as measured on the supplier's drawing dl-box-q7-t2 (REPORT.md 9), over its width w: the top an arc through the outer corners (y = 0, x = ±w/2) rising 0.257 w in the middle,
        /// straight sides down to the lower corners (±0.24 w, -0.434 w), the bottom an arc through them bulging 0.143 w up. inset: every edge moved that far in (the lit face inside the frame).
        /// Anticlockwise, y up.
        /// </summary>
        public static List<Point2> Fan(float w, float inset)
        {
            FanArcs(w, out float topY, out float topR, out float bottomY, out float bottomR);
            float tr = topR - inset, br = bottomR + inset;
            // The right side, from the outer corner down to the lower one, moved in along its normal towards the middle.
            float lx = 0.24f * w, ly = -0.434f * w, dx = lx - 0.5f * w, dy = ly, length = (float)Math.Sqrt(dx * dx + dy * dy);
            dx /= length;
            dy /= length;
            float px = 0.5f * w + dy * inset, py = -dx * inset;
            Point2 Hit(float cy, float r, float near)
            {
                float ox = px, oy = py - cy, b = ox * dx + oy * dy, c = ox * ox + oy * oy - r * r, root = (float)Math.Sqrt(Math.Max(0f, b * b - c));
                float t0 = -b - root, t1 = -b + root, t = Math.Abs(t0 - near) < Math.Abs(t1 - near) ? t0 : t1;
                return new Point2(px + dx * t, py + dy * t);
            }

            Point2 top = Hit(topY, tr, 0f), low = Hit(bottomY, br, length);
            double at = Math.Atan2(top.Y - topY, top.X), ab = Math.Atan2(low.Y - bottomY, low.X);
            var points = new List<Point2>();
            const int topSteps = 24, bottomSteps = 16;
            for (int k = 0; k <= topSteps; k++)
            {
                double a = at + (Math.PI - 2.0 * at) * k / topSteps;
                points.Add(new Point2((float)(tr * Math.Cos(a)), topY + (float)(tr * Math.Sin(a))));
            }

            for (int k = 0; k <= bottomSteps; k++)
            {
                double a = Math.PI - ab - (Math.PI - 2.0 * ab) * k / bottomSteps;
                points.Add(new Point2((float)(br * Math.Cos(a)), bottomY + (float)(br * Math.Sin(a))));
            }

            return points;
        }

        /// <summary>The centres (on x = 0) and the radii of the fan's top and bottom arcs (Fan).</summary>
        public static void FanArcs(float w, out float topY, out float topR, out float bottomY, out float bottomR)
        {
            float st = 0.257f * w, lx = 0.24f * w, ly = -0.434f * w, sb = 0.143f * w;
            topR = (0.25f * w * w + st * st) / (2f * st);
            topY = st - topR;
            bottomR = (lx * lx + sb * sb) / (2f * sb);
            bottomY = ly + sb - bottomR;
        }

        /// <summary>
        /// The fan's frame (dl-box-q7-t2, q7-t3): a band round the box (band wide, its inner edge into past the box's edge) of a 1.8 cm strip along its outside and 1.4 cm struts zigzagging from it to
        /// the box; a crest of scrolls crestH high along the top arc; and from the bottom arc's middle a cord, a spindle bead and a knot with its tassel, the knot's middle at knotY, its half-diagonal
        /// knot. One laser-cut piece.
        /// </summary>
        public static Func<float, float, float> FanFrame(float w, float band, float into, float crestH, float knotY, float knot)
        {
            List<Point2> outer = Fan(w, 0f), face = Fan(w, band);
            const float strip = 0.018f, strut = 0.007f;
            // About 34 openings round a fan, as on the drawing.
            List<Point2> o = Resample(outer, Math.Max(12, (int)Math.Round(Perimeter(outer) / (0.17f * w))));
            List<Point2> i = Resample(face, 2 * o.Count);
            var struts = new List<(Point2, Point2)>();
            for (int k = 0; k < o.Count; k++)
            {
                struts.Add((o[k], i[2 * k + 1]));
                struts.Add((i[2 * k + 1], o[(k + 1) % o.Count]));
            }

            float Struts(float x, float y)
            {
                float d = float.MaxValue;
                foreach ((Point2 a, Point2 b) in struts)
                    d = Math.Min(d, Segment(x, y, a.X, a.Y, b.X, b.Y, strut));
                return d;
            }

            Func<float, float, float> lattice = (x, y) =>
            {
                float dOut = Polygon(x, y, outer), dFace = Polygon(x, y, face);
                float frame = Math.Max(dOut, -(dFace + into));
                if (frame > 0f)
                    return frame;
                float opening = Math.Max(Math.Max(dOut + strip, -dFace), -Struts(x, y));
                return Math.Max(frame, -opening);
            };
            FanArcs(w, out float topY, out float topR, out float bottomY, out float bottomR);
            // The crest drawn along the top arc: its x the length along the arc, its y the height over it.
            Func<float, float, float> crest = Crest(0.8f * w, crestH, strip + 0.01f);
            Func<float, float, float> bent = (x, y) =>
            {
                float dy = y - topY;
                return crest(topR * (float)Math.Atan2(x, dy), (float)Math.Sqrt(x * x + dy * dy) - topR);
            };
            float bottom = bottomY + bottomR, beadY = bottom - 0.05f * w, beadA = 0.022f * w, beadB = 0.045f * w;
            Func<float, float, float> knotField = Knot(knot, 2, 0.075f, true);
            return Union(lattice, bent,
                (x, y) => Segment(x, y, 0f, bottom + into, 0f, knotY + 1.2f * knot, 0.006f),
                (x, y) => ((float)Math.Sqrt(x * x / (beadA * beadA) + (y - beadY) * (y - beadY) / (beadB * beadB)) - 1f) * beadA,
                (x, y) => knotField(x, y - knotY));
        }

        private static float Perimeter(List<Point2> loop)
        {
            float length = 0f;
            for (int k = 0; k < loop.Count; k++)
                length += Distance(loop[k], loop[(k + 1) % loop.Count]);
            return length;
        }

        private static float Distance(Point2 a, Point2 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>n points evenly along a closed loop, from its first point.</summary>
        private static List<Point2> Resample(List<Point2> loop, int n)
        {
            float total = Perimeter(loop);
            var points = new List<Point2>(n);
            int edge = 0;
            float edgeStart = 0f;
            for (int k = 0; k < n; k++)
            {
                float at = total * k / n;
                while (edgeStart + Distance(loop[edge], loop[(edge + 1) % loop.Count]) < at && edge < loop.Count - 1)
                {
                    edgeStart += Distance(loop[edge], loop[(edge + 1) % loop.Count]);
                    edge++;
                }

                Point2 a = loop[edge], b = loop[(edge + 1) % loop.Count];
                float length = Distance(a, b), f = length > 1e-9f ? (at - edgeStart) / length : 0f;
                points.Add(new Point2(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f));
            }

            return points;
        }

        // ------------------------------------------------------------------ the designs (what is round a box and what holds it, in the first part's frame: PoleAd)

        /// <summary>
        /// A plate traced from a field that is negative only within the box (x0, y0) to (x1, y1) and a little round it (a scroll's stroke at the end of a crest stands half its width out), in cells of
        /// about a 180th of its size (3 to 6 mm).
        /// </summary>
        internal static void Plate(PoleAd ad, Func<float, float, float> field, float x0, float y0, float x1, float y1, float z, float half, SolidColour colour, SolidColour lit)
        {
            float cell = Math.Max(0.003f, Math.Min(0.006f, Math.Max(x1 - x0, y1 - y0) / 180f)), pad = 2f * cell + 0.02f;
            List<List<Point2>> loops = Contour(field, x0 - pad, y0 - pad, x1 + pad, y1 + pad, cell);
            if (loops.Count > 0)
                ad.Plates.Add(new PoleAdPlate { Loops = loops, Z = z, Half = half, Colour = colour, LitColour = lit });
        }

        /// <summary>
        /// An arm from the post to the outline's side at the height y, its bracket plate along the edge there: 5 cm each way, less on a curved edge, so that its ends are within 1.5 mm of the edge (the
        /// plate sinks 2 mm into the box).
        /// </summary>
        internal static PoleAdArm ArmTo(List<Point2> outline, float y, float half)
        {
            float reach = EdgeX(outline, y), slope = (EdgeX(outline, y + 0.02f) - EdgeX(outline, y - 0.02f)) / 0.04f;
            slope = float.IsNaN(slope) ? 0f : slope;
            float along = 0.05f, off = Math.Max(Math.Abs(EdgeX(outline, y + along) - reach - slope * along), Math.Abs(EdgeX(outline, y - along) - reach + slope * along));
            if (off > 0.0015f)
                along = Math.Max(0.025f, along * (float)Math.Sqrt(0.0015f / off));
            return new PoleAdArm { Y = y, Reach = reach, Slope = slope, Along = along, Half = half };
        }

        /// <summary>How thick an ornament on a box may be: its own thickness, but inside the box's faces (half a centimetre in from each).</summary>
        internal static float Within(float depth, float half) => Math.Min(half, depth * 0.5f - 0.005f);

        /// <summary>
        /// 如意云头 (L1 = dl-44-t1, L2, dl-box-q2-t15; measured on L1 over the box's height h): a crest of scrolls on the box, 0.85 of its width and 0.17 of h high (at most 0.4 of its own width), and
        /// a pendant under it 0.8 of the width and 0.5 of h (its crest 0.12 of h), both 5 cm thick (3D LED acrylic in the photos, about half the box's depth), reaching into the box. Round the post
        /// only the crest, in front of the post and behind it, 2.5 cm each and half a centimetre inside the faces.
        /// </summary>
        internal static void Ruyi(PoleAd ad, List<Point2> outline, float w, float h, float depth, SolidColour colour, SolidColour lit)
        {
            float top = EdgeY(outline, 0f, true), bottom = EdgeY(outline, 0f, false);
            float cw = Fitting(outline, 0.85f * w, h, true), ch = Math.Min(0.17f * h, 0.4f * cw), sink = Drop(outline, 0.5f * cw, true) + 0.012f;
            Func<float, float, float> crest = Crest(cw, ch, sink);
            if (ad.Wrap)
            {
                const float shell = 0.0125f;
                foreach (float z in new[] { depth * 0.5f - 0.005f - shell, shell + 0.005f - depth * 0.5f })
                    Plate(ad, (x, y) => crest(x, y - top), -0.5f * cw, top - sink, 0.5f * cw, top + ch, z, shell, colour, lit);
            }
            else
            {
                Plate(ad, (x, y) => crest(x, y - top), -0.5f * cw, top - sink, 0.5f * cw, top + ch, 0f, Within(depth, 0.025f), colour, lit);
                float pw = Fitting(outline, 0.8f * w, h, false), hp = 0.5f * h, pSink = Drop(outline, 0.45f * pw, false) + 0.012f;
                Func<float, float, float> pendant = Pendant(pw, Math.Min(0.12f * h, 0.3f * pw), hp, pSink, out float reach);
                Plate(ad, (x, y) => pendant(x, y - bottom), -0.5f * pw, bottom - reach, 0.5f * pw, bottom + pSink, 0f, Within(depth, 0.025f), colour, lit);
                ad.Below = -h * 0.5f - bottom + reach;
            }

            ad.Above = top - h * 0.5f + ch;
        }

        /// <summary>
        /// How wide an ornament on the top (or under the bottom) of an outline may be, at most widest: as wide as the edge stays within 3 % of the height of its highest (lowest) point, so that its band
        /// is not seen over the shoulders of an oval or an arch (on an oval about a third of the width).
        /// </summary>
        private static float Fitting(List<Point2> outline, float widest, float h, bool top)
        {
            float w = widest;
            while (w > 0.25f * widest && Drop(outline, 0.5f * w, top) > 0.03f * h)
                w *= 0.95f;
            return w;
        }

        /// <summary>
        /// 中国结 under the box (dl-box-q4-t2, q4-t6, t11, t16, t19): a tab under its middle (0.24 of its width, 0.05 high) and from it a knot whose ears span 0.55 of the width, with its tassel (the knot
        /// 0.83 of the width high in all), 4 cm thick (blow-moulded LED knots), glowing at night; the box's edge in the knot's colour (as the photos' red rims).
        /// </summary>
        internal static void KnotUnder(PoleAd ad, List<Point2> outline, float w, float h, float depth, SolidColour colour, SolidColour lit)
        {
            float bottom = EdgeY(outline, 0f, false);
            float d = 0.19f * w, tab = 0.05f * w, tabHalf = 0.12f * w, sink = Drop(outline, tabHalf, false) + 0.012f, ky = bottom - tab - 1.5f * d;
            Func<float, float, float> knot = Knot(d, 2, 0.11f, true);
            Plate(ad, Union((x, y) => Box(x, y, 0f, bottom + (sink - tab) * 0.5f, tabHalf, (sink + tab) * 0.5f), (x, y) => knot(x, y - ky)),
                -1.6f * d, ky - 2.9f * d, 1.6f * d, bottom + sink, 0f, Within(depth, 0.02f), colour, lit);
            ad.Below = -h * 0.5f - bottom + tab + 1.5f * d + 2.85f * d;
            ad.Edge = colour;
        }

        /// <summary>
        /// The lattice of a round box (窗格, after the drawing dl-box-q1-t23 and L4; measured on the drawing over the box's diameter d, 310 px): a bar 0.139 d over the box and one under it from 0.22 d
        /// past its middle to the post (a hoop round the post at each), an upright bar 0.081 d from its side towards the post, short stubs with a tab holding the box at its top and its bottom, and in
        /// the corners towards the post a 回纹 knot of strips 0.016 d wide (the drawing's path). Bars 0.055 d (2.5 to 4.5 cm), one piece of painted steel 2.5 cm thick; gap: from the upright bar to the
        /// post.
        /// </summary>
        internal static void Lattice(PoleAd ad, float d, float depth, float gap, SolidColour colour)
        {
            float r = 0.5f * d, gv = 0.139f * d, gi = 0.081f * d, bar = Math.Max(0.025f, Math.Min(0.045f, 0.055f * d)), outer = -0.22f * d, inner = r + gi, post = inner + bar + gap, top = r + gv;
            var rects = new List<(float, float, float, float)>
            {
                (outer, top, post, top + bar), (outer, -top - bar, post, -top), (inner, -top, inner + bar, top),
                (-0.012f, r - 0.012f, 0.012f, top), (-0.035f, r - 0.012f, 0.035f, r + 0.022f), (-0.012f, -top, 0.012f, 0.012f - r), (-0.035f, -r - 0.022f, 0.035f, 0.012f - r)
            };
            // The 回纹 knot of the drawing (its pixels): u from the upright bar towards the box, v down from the top bar.
            float[] path = { 117, 0, 117, 25, 82, 25, 82, 9, 46, 9, 46, 62, 29, 62, 29, 9, 9, 9, 9, 27, 64, 27, 64, 44, 9, 44, 9, 79, 29, 79, 29, 117, 0, 117 };
            float s = d / 310f, half = 0.5f * Math.Max(0.01f, 0.016f * d);
            foreach (float flip in new[] { 1f, -1f })
            {
                for (int k = 0; k + 3 < path.Length; k += 2)
                {
                    float x0 = inner - path[k] * s, y0 = flip * (top - path[k + 1] * s), x1 = inner - path[k + 2] * s, y1 = flip * (top - path[k + 3] * s);
                    rects.Add((Math.Min(x0, x1) - half, Math.Min(y0, y1) - half, Math.Max(x0, x1) + half, Math.Max(y0, y1) + half));
                }
            }

            ad.Plates.Add(new PoleAdPlate { Loops = Rects(rects), Half = Within(depth, 0.0125f), Colour = colour, LitColour = colour });
            ad.Hoops.Add(top + 0.5f * bar);
            ad.Hoops.Add(-top - 0.5f * bar);
            ad.Edge = colour;
            ad.Gap = gi + bar + gap;
            // The hoops round the post reach past the bars.
            ad.Above = ad.Below = gv + Math.Max(bar, 0.5f * bar + 0.5f * LampAds.HoopHeight);
        }

        /// <summary>
        /// The frets of a framed box (回纹, L7 = dl-46-t13; measured over the box's width w): from the middle of its top a square tube of 2.5 cm rises 0.11 w, runs towards the post to 3.5 cm short of
        /// it and turns down into a hook, and the same under the box; the box held by two arms at 0.28 of its height from its middle (as in the photo).
        /// </summary>
        internal static void Frets(PoleAd ad, List<Point2> outline, float w, float h, float depth, SolidColour colour)
        {
            const float tube = 0.0125f;
            // The frets need room beside the box: 8 cm at least to the post.
            ad.Gap = Math.Max(ad.Gap, 0.08f);
            float top = 0.5f * h, inner = 0.5f * w, rise = 0.11f * w, run = inner + ad.Gap - 0.035f, hook = Math.Max(0.02f, Math.Min(0.12f * w, run - inner - 0.03f));
            float[] path = { 0f, -0.012f, 0f, rise, run, rise, run, rise - 1.8f * hook, run - hook, rise - 1.8f * hook, run - hook, rise - 0.8f * hook, run - 0.45f * hook, rise - 0.8f * hook };
            var rects = new List<(float, float, float, float)>();
            foreach (float flip in new[] { 1f, -1f })
            {
                for (int k = 0; k + 3 < path.Length; k += 2)
                {
                    float x0 = path[k], y0 = flip * (top + path[k + 1]), x1 = path[k + 2], y1 = flip * (top + path[k + 3]);
                    rects.Add((Math.Min(x0, x1) - tube, Math.Min(y0, y1) - tube, Math.Max(x0, x1) + tube, Math.Max(y0, y1) + tube));
                }
            }

            ad.Plates.Add(new PoleAdPlate { Loops = Rects(rects), Half = Within(depth, tube), Colour = colour, LitColour = colour });
            foreach (float y in new[] { 0.28f * h, -0.28f * h })
                ad.Arms.Add(ArmTo(outline, y, depth * 0.5f + 0.004f));
            ad.Above = ad.Below = rise + tube;
        }

        /// <summary>
        /// The fan's lit face, inside a frame 0.08 of the fan's width wide (dl-box-q7-t2), round its middle; its size and how far its middle is above the outer corners; and the rectangle the print
        /// goes in (half its width, its lowest and highest y, round the middle): 0.58 of the fan wide, between the bottom arc and the top one 2 % of the fan in from them.
        /// </summary>
        internal static List<Point2> FanFace(float wf, out float faceW, out float faceH, out float middleY, out float printHalf, out float printLow, out float printHigh)
        {
            List<Point2> face = Fan(wf, 0.08f * wf);
            Bounds(new[] { face }, out float x0, out float y0, out float x1, out float y1);
            faceW = x1 - x0;
            faceH = y1 - y0;
            middleY = (y0 + y1) * 0.5f;
            var moved = new List<Point2>(face.Count);
            foreach (Point2 p in face)
                moved.Add(new Point2(p.X, p.Y - middleY));
            FanArcs(wf, out float topY, out float topR, out float bottomY, out float bottomR);
            float m = 0.02f * wf, band = 0.08f * wf;
            printHalf = 0.29f * wf;
            printHigh = topY + (float)Math.Sqrt((topR - band) * (topR - band) - printHalf * printHalf) - m - middleY;
            printLow = bottomY + bottomR + band + m - middleY;
            return moved;
        }

        /// <summary>
        /// What is round a fan and holds it (dl-box-q7-t2, its numbers over the fan's width wf from the outer corners): the frame (FanFrame: the crest 0.17 wf high, the knot's middle 0.62 wf below
        /// the corners, its half-diagonal 0.15 wf) of trim's colour; an upright on the fan's middle line from the crest's head to a ball 0.69 wf up; a crook arm from the upright at 0.55 wf to the post,
        /// bent down beside it into a hoop at 0.223 wf; and a straight arm at 0.263 wf below the corners to the frame's side. The frame 2.4 cm thick, the upright 40 mm and the crook 36 mm (inside the upright where it starts), the ball 0.11 wf.
        /// The post's surface is ad.Gap beside the face.
        /// </summary>
        internal static void FanDressing(PoleAd ad, float wf, float faceW, float faceH, float middleY, float depth, SolidColour trim, SolidColour hardware)
        {
            float into = 0.012f, crestH = 0.17f * wf, knot = 0.15f * wf, knotY = -0.62f * wf, half = Within(depth, 0.012f);
            Func<float, float, float> frame = FanFrame(wf, 0.08f * wf, into, crestH, knotY, knot);
            float lowest = knotY - 2.85f * knot - middleY;
            Plate(ad, (x, y) => frame(x, y + middleY), -0.5f * wf, lowest - 0.01f, 0.5f * wf, 0.257f * wf + crestH + 0.02f - middleY, 0f, half, trim, trim);
            float ballY = 0.69f * wf - middleY, ballR = Math.Max(0.035f, 0.057f * wf);
            // The upright stands on the crest's head: it ends a centimetre into the first stroke it meets going down the middle.
            float foot = 0.257f * wf + crestH + 0.01f;
            while (foot > 0f && frame(0f, foot) > 0f)
                foot -= 0.002f;
            ad.Rods.Add(new PoleAdRod { Path = new List<Point2> { new Point2(0f, ballY), new Point2(0f, foot - 0.01f - middleY) }, Radius = 0.02f, Colour = hardware, CapEnd = true });
            ad.Balls.Add((new Point2(0f, ballY), ballR, hardware));
            const float rho = 0.018f;
            float vx = 0.5f * faceW + ad.Gap - rho - 0.002f, bend = 0.14f * wf, ky = 0.55f * wf - middleY, hy = 0.223f * wf - middleY;
            var crook = new List<Point2> { new Point2(0f, ky) };
            for (int k = 0; k <= 8; k++)
            {
                double a = Math.PI * 0.5 * (1.0 - k / 8.0);
                crook.Add(new Point2(vx - bend + bend * (float)Math.Cos(a), ky - bend + bend * (float)Math.Sin(a)));
            }

            crook.Add(new Point2(vx, hy - 0.5f * LampAds.HoopHeight));
            ad.Rods.Add(new PoleAdRod { Path = crook, Radius = rho, Colour = hardware, CapEnd = true });
            ad.Hoops.Add(hy);
            var outer = new List<Point2>();
            foreach (Point2 p in Fan(wf, 0f))
                outer.Add(new Point2(p.X, p.Y - middleY));
            ad.Arms.Add(ArmTo(outer, -0.263f * wf - middleY, half + 0.004f));
            ad.Edge = trim;
            ad.Above = ballY + ballR - 0.5f * faceH;
            ad.Below = -0.5f * faceH - lowest;
        }

        // ------------------------------------------------------------------ where an outline is

        /// <summary>The highest (top) or the lowest point of an outline's edge over x (where a crest sits on it or a pendant hangs from it); NaN when the outline does not reach x.</summary>
        public static float EdgeY(IList<Point2> outline, float x, bool top)
        {
            float best = float.NaN;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                Point2 a = outline[i], b = outline[j];
                if ((a.X > x) == (b.X > x) && a.X != x)
                    continue;
                float y = Math.Abs(b.X - a.X) < 1e-9f ? Math.Max(a.Y, b.Y) : a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
                if (float.IsNaN(best) || (top ? y > best : y < best))
                    best = y;
            }

            return best;
        }

        /// <summary>How far right an outline's edge reaches at the height y (where an arm from a post on its right meets it); NaN when it does not reach y.</summary>
        public static float EdgeX(IList<Point2> outline, float y)
        {
            float best = float.NaN;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                Point2 a = outline[i], b = outline[j];
                if ((a.Y > y) == (b.Y > y) && a.Y != y)
                    continue;
                float x = Math.Abs(b.Y - a.Y) < 1e-9f ? Math.Max(a.X, b.X) : a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
                if (float.IsNaN(best) || x > best)
                    best = x;
            }

            return best;
        }

        /// <summary>How far the top edge of an outline drops below its highest point within x of the middle (how far into the box a crest on it must reach to meet it everywhere).</summary>
        public static float Drop(IList<Point2> outline, float halfWidth, bool top)
        {
            float edge = EdgeY(outline, 0f, top), worst = 0f;
            for (int k = 0; k <= 16; k++)
            {
                float y = EdgeY(outline, halfWidth * (k / 8f - 1f), top);
                if (!float.IsNaN(y))
                    worst = Math.Max(worst, top ? edge - y : y - edge);
            }

            return worst;
        }
    }
}
