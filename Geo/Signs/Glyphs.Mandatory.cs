using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The head of one of the mandatory signs' arrows, in widths of the arrow's stem (w): the chevron of Appendix E. From the tip back along the arrow: the outer corners are A to each side
    /// and B back, the wing's outer edge runs on C further back, the back edge comes in to the notch, Notch to each side and N back from the tip, where the stem joins.
    /// </summary>
    internal struct MandHead
    {
        public float A, B, C, N, Notch;

        public MandHead(float a, float b, float c, float n, float notch = 0.5f)
        {
            A = a;
            B = b;
            C = c;
            N = n;
            Notch = notch;
        }

        /// <summary>The round signs, the one-way, priority, U-turn and hard-shoulder boards and the arrows of the lane signs (E.50 to E.65, E.75 to E.98): A 1.56 w, B 1.665 w, C 1.475 w, N 2.015 w.</summary>
        public static readonly MandHead Round = new MandHead(1.56f, 1.665f, 1.475f, 2.015f);

        /// <summary>The lane direction boards (E.66 to E.73), a little narrower: A 1.42 w, B 1.47 w, C 1.42 w, N 1.94 w.</summary>
        public static readonly MandHead Lane = new MandHead(1.42f, 1.47f, 1.42f, 1.94f);

        /// <summary>The small arrow beside the P of 示40-2 and 示40-3 (E.92, E.93): a notched triangle, A 1.73 w, B 3.25 w, no wing, N 2.0 w.</summary>
        public static readonly MandHead Parking = new MandHead(1.73f, 3.25f, 0f, 2.0f);
    }

    /// <summary>
    /// A pictogram built from polygons only (circles, rings, sectors, capsules, arrow heads are cut into straight pieces), so that it can be moved, scaled and turned freely while it is drawn,
    /// and, at the end, written as it is or fitted into the 100 x 100 box. Solids and holes as with PicBuilder (non-zero winding: overlapping solids join, a hole cuts what is under it).
    /// </summary>
    internal sealed class MandPic
    {
        private readonly List<float[]> m_Contours = new List<float[]>();
        private readonly List<bool> m_Holes = new List<bool>();
        private float m_S = 1f, m_Ox, m_Oy, m_Cos = 1f, m_Sin;

        /// <summary>The frame the following shapes are written in: x' = ox + s (x cos a - y sin a), y' = oy + s (x sin a + y cos a); a in degrees, clockwise on the screen.</summary>
        public MandPic Frame(float scale = 1f, float ox = 0f, float oy = 0f, float degrees = 0f)
        {
            m_S = scale;
            m_Ox = ox;
            m_Oy = oy;
            double a = degrees * Math.PI / 180.0;
            m_Cos = (float)Math.Cos(a);
            m_Sin = (float)Math.Sin(a);
            return this;
        }

        public MandPic Poly(bool hole, params float[] p)
        {
            int n = p.Length / 2;
            if (n < 3)
                return this;
            var q = new List<float>(p.Length);
            float lx = float.NaN, ly = float.NaN;
            for (int i = 0; i < n; i++)
            {
                float x = p[i * 2], y = p[i * 2 + 1];
                float tx = m_Ox + m_S * (x * m_Cos - y * m_Sin), ty = m_Oy + m_S * (x * m_Sin + y * m_Cos);
                if (Math.Abs(tx - lx) < 1e-4f && Math.Abs(ty - ly) < 1e-4f)
                    continue;
                q.Add(tx);
                q.Add(ty);
                lx = tx;
                ly = ty;
            }

            if (q.Count >= 4 && Math.Abs(q[0] - q[q.Count - 2]) < 1e-4f && Math.Abs(q[1] - q[q.Count - 1]) < 1e-4f)
                q.RemoveRange(q.Count - 2, 2);
            if (q.Count < 6)
                return this;
            m_Contours.Add(q.ToArray());
            m_Holes.Add(hole);
            return this;
        }

        public MandPic Poly(params float[] p) => Poly(false, p);

        public MandPic Ellipse(float cx, float cy, float rx, float ry, bool hole = false, int steps = 40)
        {
            var p = new float[steps * 2];
            for (int i = 0; i < steps; i++)
            {
                double a = 2.0 * Math.PI * i / steps;
                p[i * 2] = cx + rx * (float)Math.Cos(a);
                p[i * 2 + 1] = cy + ry * (float)Math.Sin(a);
            }

            return Poly(hole, p);
        }

        public MandPic Circle(float cx, float cy, float r, bool hole = false) => Ellipse(cx, cy, r, r, hole);

        public MandPic Ring(float cx, float cy, float outer, float inner) => Circle(cx, cy, outer).Circle(cx, cy, inner, true);

        public MandPic Rect(float x, float y, float w, float h, bool hole = false) => Poly(hole, x, y, x + w, y, x + w, y + h, x, y + h);

        /// <summary>A rectangle whose four corners are rounded with r (rt, rb: the top and the bottom radius when they differ).</summary>
        public MandPic RoundRect(float x, float y, float w, float h, float r, bool hole = false, float rb = -1f)
        {
            float rt = Math.Min(r, Math.Min(w, h) * 0.5f);
            rb = rb < 0f ? rt : Math.Min(rb, Math.Min(w, h) * 0.5f);
            var p = new List<float>();
            void Corner(float ccx, float ccy, float rr, float a0)
            {
                if (rr < 1e-3f)
                {
                    p.Add(ccx);
                    p.Add(ccy);
                    return;
                }

                for (int i = 0; i <= 6; i++)
                {
                    double a = (a0 + 15.0 * i) * Math.PI / 180.0;
                    p.Add(ccx + rr * (float)Math.Cos(a));
                    p.Add(ccy + rr * (float)Math.Sin(a));
                }
            }

            Corner(x + w - rt, y + rt, rt, -90f);
            Corner(x + w - rb, y + h - rb, rb, 0f);
            Corner(x + rb, y + h - rb, rb, 90f);
            Corner(x + rt, y + rt, rt, 180f);
            return Poly(hole, p.ToArray());
        }

        /// <summary>A thick line with round ends.</summary>
        public MandPic Capsule(float x0, float y0, float x1, float y1, float width, bool hole = false)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float r = width * 0.5f;
            if (len < 1e-4f)
                return Circle(x0, y0, r, hole);
            double b = Math.Atan2(dx / len, -dy / len);
            var p = new List<float>();
            for (int i = 0; i <= 8; i++)
            {
                double a = b + Math.PI * i / 8;
                p.Add(x0 + r * (float)Math.Cos(a));
                p.Add(y0 + r * (float)Math.Sin(a));
            }

            for (int i = 0; i <= 8; i++)
            {
                double a = b + Math.PI + Math.PI * i / 8;
                p.Add(x1 + r * (float)Math.Cos(a));
                p.Add(y1 + r * (float)Math.Sin(a));
            }

            return Poly(hole, p.ToArray());
        }

        /// <summary>A thick line with square ends (a stem, a bar).</summary>
        public MandPic Bar(float x0, float y0, float x1, float y1, float width, bool hole = false)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f)
                return this;
            float nx = -dy / len * width * 0.5f, ny = dx / len * width * 0.5f;
            return Poly(hole, x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny);
        }

        public MandPic Stroke(float width, params float[] p)
        {
            for (int i = 0; i + 3 < p.Length; i += 2)
                Capsule(p[i], p[i + 1], p[i + 2], p[i + 3], width);
            return this;
        }

        /// <summary>A band along a circle: between the radii, from the angle a0 to a1 (degrees, from +x towards +y, that is clockwise on the screen).</summary>
        public MandPic Sector(float cx, float cy, float rIn, float rOut, float a0, float a1, bool hole = false)
        {
            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(a1 - a0) / 4f));
            var p = new List<float>();
            for (int i = 0; i <= steps; i++)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p.Add(cx + rOut * (float)Math.Cos(a));
                p.Add(cy + rOut * (float)Math.Sin(a));
            }

            for (int i = steps; i >= 0; i--)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p.Add(cx + rIn * (float)Math.Cos(a));
                p.Add(cy + rIn * (float)Math.Sin(a));
            }

            return Poly(hole, p.ToArray());
        }

        /// <summary>An arrow head with its tip at (tx, ty) pointing along (dx, dy), for a stem of width w.</summary>
        public MandPic Head(float tx, float ty, float dx, float dy, float w, MandHead h, bool hole = false)
        {
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            float fx = dx / len, fy = dy / len, nx = -fy, ny = fx;
            var p = new List<float>();
            void P(float a, float s)
            {
                p.Add(tx - fx * a * w + nx * s * w);
                p.Add(ty - fy * a * w + ny * s * w);
            }

            P(0f, 0f);
            P(h.B, h.A);
            if (h.C > 1e-3f)
                P(h.B + h.C, h.A);
            P(h.N, h.Notch);
            P(h.N, -h.Notch);
            if (h.C > 1e-3f)
                P(h.B + h.C, -h.A);
            P(h.B, -h.A);
            return Poly(hole, p.ToArray());
        }

        /// <summary>A straight arrow from its tail to its tip (stem w wide, square tail).</summary>
        public MandPic Arrow(float x0, float y0, float x1, float y1, float w, MandHead h)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float fx = dx / len, fy = dy / len;
            float back = (h.N - 0.3f) * w;
            Head(x1, y1, fx, fy, w, h);
            return Bar(x0, y0, x1 - fx * back, y1 - fy * back, w);
        }

        public void Bounds(out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue;
            x1 = y1 = float.MinValue;
            foreach (float[] c in m_Contours)
            {
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    x0 = Math.Min(x0, c[i]);
                    x1 = Math.Max(x1, c[i]);
                    y0 = Math.Min(y0, c[i + 1]);
                    y1 = Math.Max(y1, c[i + 1]);
                }
            }
        }

        /// <summary>The path as written (in the frame the shapes were given in).</summary>
        public string Path() => Emit(1f, 0f, 0f);

        /// <summary>The path scaled evenly and centred so that it fills the 100 x 100 box with the margin all round.</summary>
        public string Fitted(float margin = 5f)
        {
            Bounds(out float x0, out float y0, out float x1, out float y1);
            float s = (100f - 2f * margin) / Math.Max(1e-4f, Math.Max(x1 - x0, y1 - y0));
            return Emit(s, 50f - (x0 + x1) * 0.5f * s, 50f - (y0 + y1) * 0.5f * s);
        }

        private string Emit(float s, float ox, float oy)
        {
            var b = new PicBuilder();
            for (int i = 0; i < m_Contours.Count; i++)
            {
                float[] c = m_Contours[i];
                var p = new float[c.Length];
                for (int k = 0; k + 1 < c.Length; k += 2)
                {
                    p[k] = ox + c[k] * s;
                    p[k + 1] = oy + c[k + 1] * s;
                }

                b.Poly(m_Holes[i], p);
            }

            return b.ToString();
        }
    }

    /// <summary>
    /// The pictograms of the mandatory signs (指示标志, GB 5768.2-2022 chapter 6), redrawn from the dimensioned drawings of Appendix E (E.50 to E.99) and, where there is none (the figures
    /// of the parking variants, Fig. 127 to 133), from the printed figures. Names start with M_. Arrows of the round signs are written in the frame of the disc (0 to 100 is the diameter D:
    /// one grid square of Appendix E is 5 units), so that a sign puts them over the whole disc; the other figures fill their own box and are fitted by the sign into the area the drawing gives.
    /// </summary>
    public static partial class Glyphs
    {
        // ------------------------------------------------------------------ figures of the signs (paths in the frame of the board, cached)

        private static readonly object s_MandLock = new object();
        private static Dictionary<string, string> s_MandFigures;

        /// <summary>
        /// The figure of a mandatory sign by its key, as path data in the frame of its board: 0 to 100 across the board's width (the disc's diameter), the same scale downwards. A sign puts it
        /// over its whole board (Pic with the height 100 times the board's width over its height). The keys are listed in MandBuild.
        /// </summary>
        internal static string MandFigure(string key)
        {
            lock (s_MandLock)
            {
                if (s_MandFigures == null)
                    s_MandFigures = new Dictionary<string, string>();
                if (!s_MandFigures.TryGetValue(key, out string path))
                    s_MandFigures[key] = path = MandBuild(key);
                return path;
            }
        }

        /// <summary>The disc's arrows: stem 10 (a tenth of D), bends of radius 11.5 to 21.5 (E.51 to E.55: 2.3 and 4.3 squares of 5).</summary>
        private const float MandRW = 10f, MandRIn = 11.5f, MandROut = 21.5f;

        private static string MandBuild(string key)
        {
            var m = new MandPic();
            MandHead h = MandHead.Round;
            switch (key)
            {
                // 示1 (E.50): tip 1.74 squares from the top, stem to 18.2 squares, centred.
                case "ahead":
                    m.Arrow(50f, 91f, 50f, 8.7f, MandRW, h);
                    break;
                // 示2 (E.51): the stem on the right (centre 13.1 squares), the bend round (9.8, 10.1), the head's tip at 2.42 squares.
                case "turnL":
                    m.Bar(65.5f, 85.75f, 65.5f, 50f, MandRW).Sector(49f, 50.5f, MandRIn, MandROut, 270f, 360f).Arrow(49.5f, 34f, 12.1f, 34f, MandRW, h);
                    break;
                // 示4 (E.53): the straight arrow at 11.45 squares; the left branch leaves it round (8.15, 15.4).
                case "aheadL":
                    m.Arrow(57.25f, 89.4f, 57.25f, 9.3f, MandRW, h).Sector(40.75f, 77f, MandRIn, MandROut, 270f, 360f).Arrow(41.25f, 60.5f, 9.95f, 60.5f, MandRW, h);
                    break;
                // 示6 (E.55): the stem in the middle, two branches round (6.7, 12.1) and (13.3, 12.1).
                case "leftRight":
                    m.Bar(50f, 90.85f, 50f, 60f, MandRW)
                     .Sector(33.5f, 60.5f, MandRIn, MandROut, 270f, 360f).Arrow(34f, 44f, 9.15f, 44f, MandRW, h)
                     .Sector(66.5f, 60.5f, MandRIn, MandROut, 180f, 270f).Arrow(66f, 44f, 90.85f, 44f, MandRW, h);
                    break;
                // 示7 (E.56): the straight arrow turned 45 degrees down to the right, centred, as long as that of 示1.
                case "keepR":
                    m.Arrow(20.9f, 20.9f, 79.1f, 79.1f, MandRW, h);
                    break;
                // 示9 (E.58): three bands on a circle of 25.25 (5.05 squares), 6.25 wide, from 42 to 116 degrees and every 120 degrees, anticlockwise; heads 5.9 wide.
                case "roundabout":
                {
                    const float r = 24.85f, half = 3.45f, wh = 5.9f;
                    var head = new MandHead(1.56f, 1.665f, 1.475f, 2.04f, half / wh);
                    for (int k = 0; k < 3; k++)
                    {
                        float v0 = 42f + 120f * k, v1 = 116f + 120f * k;
                        m.Sector(50f, 50f, r - half, r + half, -v1 - 2f, -v0);
                        double a = v1 * Math.PI / 180.0;
                        float px = 50f + r * (float)Math.Cos(a), py = 50f - r * (float)Math.Sin(a);
                        float fx = -(float)Math.Sin(a), fy = -(float)Math.Cos(a);
                        float n = head.N * wh;
                        m.Head(px + fx * n, py + fy * n, fx, fy, wh, head);
                    }

                    break;
                }

                // 示41 (E.95, the square board): legs 10.7 wide at 37.55 and 73.65, the bend round (55.6, 35.2), the head down at the left.
                case "uturn":
                    m.Bar(73.65f, 88.4f, 73.65f, 34.7f, 10.7f).Sector(55.6f, 35.2f, 12.7f, 23.4f, 180f, 360f).Arrow(37.55f, 34.7f, 37.55f, 88.4f, 10.7f, h);
                    break;
                // 立体交叉直行和左转弯行驶 (photograph): the stem at 54, 8.6 wide, tip at 13.6; the loop (P) on its right, 24.5 x 25.5 with the stem as its left side and a hole 11.5 x 8.5; the left arrow at the loop's lower bar (y 61), tip at 23.
                case "xjL":
                    m.Arrow(54f, 86f, 54f, 13.6f, 8.6f, MandHeadInterchange)
                     .RoundRect(54f, 39.7f, 24.6f, 25.5f, 9f).RoundRect(58.6f, 48.2f, 11.6f, 8.5f, 3f, true)
                     .Arrow(54f, 61f, 23f, 61f, 8.6f, MandHeadInterchange);
                    break;
                // 立体交叉直行和右转弯行驶 (photograph): the stem at 40.5, a bar across to its left at y 49 (from 17), the branch leaving the stem at y 75 round (66.5, 75) to the arrow along y 49 (tip at 82).
                case "xjR":
                    m.Arrow(40.5f, 86f, 40.5f, 13.6f, 8.6f, MandHeadInterchange)
                     .Bar(17f, 49f, 40.5f, 49f, 7f)
                     .Sector(66.5f, 75f, 21.7f, 30.3f, 180f, 270f).Arrow(66.5f, 49f, 82f, 49f, 8.6f, MandHeadInterchange);
                    break;
                // 示15 (E.64, the square board): the white arrow ahead (10.5 wide) and the red one coming (7 wide), the same length.
                case "priorityAhead":
                    m.Arrow(65.55f, 76.6f, 65.55f, 23.1f, 10.5f, h);
                    break;
                case "priorityOncoming":
                    m.Arrow(29.45f, 23.1f, 29.45f, 76.2f, 7f, h);
                    break;
                // 示10 a (E.59 a, the 100 x 50 board, in cm): shaft 12.6 high at mid height, tail at 11.9, tip at 88.
                case "oneway":
                    m.Arrow(11.9f, 25f, 88f, 25f, 12.6f, h);
                    break;
                // 示10 b (E.59 b): the wide white arrow the characters stand in: shaft 26.1 high from 8.1, tip at 93.35.
                case "onewayText":
                    m.Arrow(8.1f, 25f, 93.35f, 25f, 26.1f, MandOneWayHead);
                    break;
                default:
                    return MandBuildMore(key, m);
            }

            return m.Path();
        }

        /// <summary>The head of the interchange signs' arrows (photograph): 0.85 of the round signs' head, which reaches 2.66 stem widths across as the photographed ones do and leaves a gap to the loop.</summary>
        internal static readonly MandHead MandHeadInterchange = new MandHead(1.33f, 1.42f, 1.25f, 1.71f);

        /// <summary>The head of the white arrow of 示10 b and 示11 b (E.59 b, E.60 b): A 0.75 w, B 0.80 w, C 0.79 w, N 1.35 w.</summary>
        internal static readonly MandHead MandOneWayHead = new MandHead(0.75f, 0.80f, 0.79f, 1.35f);

        private static string MandBuildMore(string key, MandPic m)
        {
            MandHead h = MandHead.Round;
            if (key.StartsWith("lane:"))
            {
                // A lane of the lane direction boards, in the frame of the 90 x 70 board (cm, then scaled to 0..100 across the 90).
                m.Frame(100f / 90f);
                MandLaneArrows(m, key.Substring(5));
                return m.Path();
            }

            switch (key)
            {
                // 示11 a (E.60 a, the 50 x 100 board, in cm; the frame is 0..100 across the 50 cm).
                case "oneway11":
                    m.Frame(2f).Arrow(25f, 88.15f, 25f, 11.85f, 12.65f, h);
                    break;
                case "oneway11Text":
                    m.Frame(2f).Arrow(25f, 91.8f, 25f, 7.0f, 26f, MandOneWayHead);
                    break;
                // The arrows of the lane signs (示25 to 示34, E.75 to E.84, in cm on the 100 cm board): down, 5.6 wide, from 49.9 to 75; the diagonal one from (60.6, 51.7) to its tip at (37.5, 74.8).
                case "laneDown":
                    m.Arrow(50f, 49.9f, 50f, 75f, 5.6f, h);
                    break;
                case "laneDownLeft":
                    m.Arrow(60.6f, 51.7f, 37.5f, 74.8f, 5.6f, h);
                    break;
                // 示40-2 and 示40-3 (E.92, E.93, the square board): the arrow beside the P, 5.45 wide, between 50.1 and 84.1.
                case "parkL":
                    m.Arrow(84.1f, 69.9f, 50.1f, 69.9f, 5.45f, MandHead.Parking);
                    break;
                case "parkR":
                    m.Arrow(50.1f, 69.9f, 84.1f, 69.9f, 5.45f, MandHead.Parking);
                    break;
                // 示42-2 (E.97): the arrow of the shoulder that ends: up from 85.6 to 47.5, a bend of 45 degrees to the left (radius 12), the head pointing up and left.
                case "shoulderMerge":
                    m.Bar(84.65f, 85.6f, 84.65f, 47f, 5f).Sector(72.65f, 47.5f, 9.5f, 14.5f, -45f, 0f).Arrow(81.35f, 39.2f, 70.88f, 28.75f, 5f, h);
                    break;
                // Pictograms in the frame of their round sign (units of the disc), as Appendix E places them.
                case "horn": MandHorn(m); break;
                case "headlamp": MandHeadlamp(m); break;
                case "carFront": MandCarFront(m); break;
                case "truck": MandTruck(m); break;
                case "truckRear": MandTruckRear(m); break;
                case "bicycle": MandBicycle(m); break;
                case "ebike": MandEBike(m); break;
                case "walker": MandWalker(m); break;
                // 示36-1 (E.86): the person at the left, the divider, the bicycle at the right; 示36-2 (E.87) the other way round; 示37 (E.88) one above the other.
                case "36-1":
                    MandPlace(m, c => MandWalker(c), 13f, 25.5f, 39.4f, 68.3f);
                    MandPlace(m, MandBicycle, 53.7f, 38f, 91f, 60.6f);
                    m.Rect(48.6f, 8.6f, 2.9f, 82.6f);
                    break;
                case "36-2":
                    MandPlace(m, MandBicycle, 9f, 38f, 46.3f, 60.6f);
                    MandPlace(m, c => MandWalker(c), 60.6f, 25.5f, 87f, 68.3f);
                    m.Rect(48.6f, 8.6f, 2.9f, 82.6f);
                    break;
                case "37":
                    MandPlace(m, c => MandWalker(c), 37.9f, 6.4f, 62.5f, 49.5f);
                    MandPlace(m, MandBicycle, 22.9f, 54.9f, 77.4f, 87.8f);
                    break;
                // 示38 (E.89): the bicycle a little larger and lower than in 示31, the person over it with a halo in the ground colour.
                case "pusherBike":
                    m.Frame(1.055f, -1.9f, 9.3f);
                    MandBicycle(m);
                    break;
                case "pusher": MandPusher(m); break;
                case "pusherHalo": MandPusher(m, 1.8f); break;
                // 示16 (E.65): the person over the zebra, with a halo in the colour of the triangle.
                case "zebra": MandZebra(m); break;
                case "crossingWalker":
                    MandPlace(m, c => MandWalker(c), 37f, 35f, 60.4f, 75.5f);
                    break;
                case "crossingHalo":
                    MandPlace(m, c => MandWalker(c), 37f, 35f, 60.4f, 75.5f, c => MandWalker(c, 2.8f));
                    break;
                default:
                    if (key.StartsWith("up:"))
                    {
                        // A straight arrow up at the x given, from 85.8 to 14.3, 5 wide (示42, E.96).
                        float x = float.Parse(key.Substring(3), System.Globalization.CultureInfo.InvariantCulture);
                        m.Arrow(x, 85.8f, x, 14.3f, 5f, h);
                        break;
                    }

                    return "";
            }

            return m.Path();
        }

        /// <summary>The lane direction combinations: r, l (示17, 示18), a (示19), ar, al (示20, 示21), u (示22), ul (示23), and two of our own for the special cases the standard leaves open: lr, alr.</summary>
        internal static readonly string[] MandLaneCombos = { "r", "l", "a", "ar", "al", "u", "ul", "lr", "alr" };

        /// <summary>
        /// The arrows of one lane of a lane direction board (E.66 to E.72, cm on the 90 x 70 board, stems 8 wide). The left-turning ones are the right-turning ones mirrored about the middle (45).
        /// </summary>
        private static void MandLaneArrows(MandPic m, string combo)
        {
            const float w = 8f;
            MandHead h = MandHead.Lane;
            bool mirror = combo == "l" || combo == "al";
            float X(float x) => mirror ? 90f - x : x;
            void Bar(float x0, float y0, float x1, float y1) => m.Bar(X(x0), y0, X(x1), y1, w);
            void Arrow(float x0, float y0, float x1, float y1) => m.Arrow(X(x0), y0, X(x1), y1, w, h);
            void Sector(float cx, float cy, float ri, float ro, float a0, float a1)
            {
                if (mirror)
                    m.Sector(90f - cx, cy, ri, ro, 180f - a1, 180f - a0);
                else
                    m.Sector(cx, cy, ri, ro, a0, a1);
            }

            switch (combo)
            {
                case "r":
                case "l":
                    // 示17 (E.66): stem 22.4..30.4 from 62.4, the bend round (39.7, 32.0) radii 9.3 and 17.3, the head's tip at 67.2.
                    Bar(26.4f, 62.4f, 26.4f, 31.5f);
                    Sector(39.7f, 32.0f, 9.3f, 17.3f, 180f, 270f);
                    Arrow(39.2f, 18.7f, 67.2f, 18.7f);
                    break;
                case "a":
                    // 示19 (E.68): the straight arrow in the middle, tip at 7.4.
                    Arrow(45f, 62.4f, 45f, 7.4f);
                    break;
                case "ar":
                case "al":
                    // 示20 (E.69): the straight arrow at 31.5 and the branch to the right round (45.5, 58.4), radii 10 and 18, tip at 69.8.
                    Arrow(31.5f, 62.5f, 31.5f, 7.6f);
                    Sector(45.5f, 58.4f, 10f, 18f, 180f, 270f);
                    Arrow(45f, 44.4f, 69.8f, 44.4f);
                    break;
                case "u":
                    // 示22 (E.71): up the right leg (55.3), over the top round (45, 21), radii 6.3 and 14.3, down the left leg to the head at 61.8.
                    Bar(55.3f, 61.8f, 55.3f, 20.5f);
                    Sector(45f, 21f, 6.3f, 14.3f, 180f, 360f);
                    Arrow(34.7f, 20.5f, 34.7f, 61.8f);
                    break;
                case "ul":
                    // 示23 (E.72): the U-turn further right (round (58.15, 29.05)) and the left turn along the top of its bend, tip at 17.6.
                    Bar(68.1f, 62f, 68.1f, 28.6f);
                    Sector(58.15f, 29.05f, 5.95f, 13.95f, 180f, 360f);
                    Arrow(48.2f, 28.6f, 48.2f, 62f);
                    Arrow(58.65f, 19.1f, 17.6f, 19.1f);
                    break;
                case "lr":
                    // Our own: left and right, as 示6 on the lane board.
                    Bar(45f, 62.4f, 45f, 40f);
                    Sector(31.7f, 40.5f, 9.3f, 17.3f, 270f, 360f);
                    Arrow(32.2f, 27.2f, 13.5f, 27.2f);
                    Sector(58.3f, 40.5f, 9.3f, 17.3f, 180f, 270f);
                    Arrow(57.8f, 27.2f, 76.5f, 27.2f);
                    break;
                case "alr":
                    // Our own: ahead, left and right.
                    Arrow(45f, 62.4f, 45f, 7.4f);
                    Sector(31.7f, 58f, 9.3f, 17.3f, 270f, 360f);
                    Arrow(32.2f, 44.7f, 13.5f, 44.7f);
                    Sector(58.3f, 58f, 9.3f, 17.3f, 180f, 270f);
                    Arrow(57.8f, 44.7f, 76.5f, 44.7f);
                    break;
            }
        }

        // ------------------------------------------------------------------ vehicles

        /// <summary>A circular arc as points (centre, radius, from a0 to a1 degrees, screen angles), appended to a list: for outlines that run round a wheel arch.</summary>
        private static void MandArc(List<float> p, float cx, float cy, float r, float a0, float a1, int steps = 10)
        {
            for (int i = 0; i <= steps; i++)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p.Add(cx + r * (float)Math.Cos(a));
                p.Add(cy + r * (float)Math.Sin(a));
            }
        }

        /// <summary>
        /// A car seen from the front (示24, E.74, in units of the disc): the cab with its curved roof, the wider body with the fenders, two short legs for the wheels; the windscreen, the two
        /// lamps and the two bars of the grille are holes. occupants: two people in the windscreen (示30, E.80).
        /// </summary>
        private static void MandCarFront(MandPic m, bool occupants = false)
        {
            m.Poly(29.2f, 32.9f, 33f, 31.3f, 40f, 29.8f, 50f, 28.95f, 60f, 29.8f, 67f, 31.3f, 70.9f, 32.9f, 72.6f, 44.5f, 76.4f, 48.1f, 75.3f, 63.5f,
                   24.8f, 63.5f, 23.8f, 48.1f, 27.4f, 44.5f);
            m.RoundRect(27.2f, 62f, 4.9f, 9.3f, 0f, false, 2.4f).RoundRect(67.9f, 62f, 4.7f, 9.3f, 0f, false, 2.3f);
            m.Poly(true, 31.3f, 35.3f, 40f, 34f, 50f, 33.5f, 60f, 34f, 68.8f, 35.3f, 69.7f, 44.3f, 60f, 42.9f, 50f, 42.5f, 40f, 42.9f, 30.4f, 44.3f);
            m.Circle(28.7f, 50.5f, 2.8f, true).Circle(71.4f, 50.5f, 2.8f, true);
            m.RoundRect(33.1f, 48.1f, 34f, 4f, 1.5f, true);
            m.Poly(true, 34.3f, 53.9f, 65.7f, 53.9f, 66.4f, 56f, 65.7f, 58.2f, 34.3f, 58.2f, 33.6f, 56f);
            if (occupants)
            {
                foreach (float x in new[] { 41.4f, 58.6f })
                {
                    m.Circle(x, 38.2f, 2.1f);
                    var s = new List<float>();
                    MandArc(s, x, 44f, 4.3f, 180f, 360f, 12);
                    m.Poly(s.ToArray());
                }
            }
        }

        /// <summary>A small passenger car from the side, facing left (示26, E.76, cm on the board): the outline runs round the two wheel arches; the windows are holes.</summary>
        private static void MandCarSide(MandPic m)
        {
            var p = new List<float> { 18.4f, 23.6f, 20.1f, 22.3f, 32.2f, 19.7f, 45.5f, 11.7f, 62.6f, 11.7f, 74.0f, 19.7f, 79.0f, 19.7f, 80.9f, 20.6f, 81.8f, 23.0f, 81.8f, 31.5f, 80.9f, 33.1f };
            MandArc(p, 68.2f, 32.7f, 7.5f, -3f, -177f);
            MandArc(p, 32.2f, 32.7f, 7.5f, -3f, -177f);
            p.AddRange(new[] { 19.5f, 33.1f, 18.4f, 31.8f, 18.2f, 25.5f });
            m.Poly(p.ToArray());
            m.Circle(32.2f, 32.7f, 5.2f).Circle(68.2f, 32.7f, 5.2f);
            m.Poly(true, 37.3f, 20.1f, 45.8f, 13.9f, 53f, 13.9f, 53f, 20.1f);
            m.Poly(true, 55.6f, 13.9f, 61.9f, 13.9f, 69.5f, 20.1f, 55.6f, 20.1f);
        }

        /// <summary>A bus from the side, facing left (示27, E.77, cm on the board): the front with its step, the rear overhang rising, the two wheel arches; the windscreen and four windows are holes; wheels with a hub.</summary>
        private static void MandBus(MandPic m)
        {
            var p = new List<float> { 17.6f, 22.4f, 84.0f, 22.4f, 85.3f, 23.7f, 85.3f, 38.5f, 71.9f, 41.6f };
            MandArc(p, 66.5f, 41.5f, 5.2f, 0f, -180f);
            MandArc(p, 30.8f, 41.5f, 5.2f, 0f, -180f);
            p.AddRange(new[] { 15.6f, 42.6f, 15.3f, 33.6f, 16.8f, 31.6f, 16.8f, 23.4f });
            m.Poly(p.ToArray());
            foreach (float x in new[] { 30.8f, 66.5f })
                m.Circle(x, 41.5f, 3.5f).Circle(x, 41.5f, 2.3f, true).Circle(x, 41.5f, 1.2f);
            m.RoundRect(19.9f, 26.6f, 3.8f, 7.3f, 0.6f, true, 1.9f);
            foreach (float x in new[] { 27.2f, 41.6f, 55.5f, 69.6f })
                m.RoundRect(x, 26.6f, 11.0f, 4.7f, 0.8f, true);
        }

        /// <summary>A tram from the side (示29, E.79, cm on the board): the pantograph (a rhombus on a short base), the body rounded below, five windows, two pairs of small wheels.</summary>
        private static void MandTram(MandPic m)
        {
            m.Poly(63.8f, 4.6f, 76.0f, 12.1f, 63.8f, 19.6f, 51.6f, 12.1f).Poly(true, 63.8f, 7.4f, 73.2f, 12.1f, 63.8f, 16.8f, 54.4f, 12.1f);
            m.Rect(57.6f, 17.3f, 12.2f, 2.0f);
            m.RoundRect(19.8f, 18.9f, 60.4f, 22.2f, 1.5f, false, 5f);
            foreach (float x in new[] { 21.4f, 33.2f, 45.2f, 57.1f, 69.1f })
                m.RoundRect(x, 21.9f, 9.6f, 10f, 0.8f, true);
            foreach (float x in new[] { 27.6f, 33.5f, 66.8f, 72.2f })
                m.Circle(x, 44.6f, 2.5f);
        }

        /// <summary>A lorry from the side, facing left (示43, E.99, units of the disc): the cab with its window, the bed, three loads above it, two wheels in their arches.</summary>
        private static void MandTruck(MandPic m)
        {
            var p = new List<float> { 28.6f, 31.5f, 37.6f, 31.5f, 38.7f, 32.6f, 39.0f, 47.4f, 80.6f, 47.4f, 80.6f, 59.6f };
            MandArc(p, 63.3f, 63.2f, 7.0f, -30f, -150f, 8);
            MandArc(p, 30.8f, 63.2f, 7.0f, -30f, -150f, 8);
            p.AddRange(new[] { 22.3f, 59.6f, 19.4f, 59.6f, 19.4f, 58.4f, 21.9f, 58.0f, 19.4f, 48.4f, 20.3f, 46.6f, 22.9f, 45.5f, 24.2f, 43.5f, 27.6f, 33.0f });
            m.Poly(p.ToArray());
            m.Poly(true, 30.4f, 34.2f, 35.4f, 34.2f, 36.0f, 34.8f, 36.3f, 43.0f, 27.4f, 43.0f, 29.6f, 34.8f);
            foreach (float x in new[] { 40.6f, 54.0f, 67.4f })
                m.Rect(x, 35.6f, 12.2f, 9.8f);
            foreach (float x in new[] { 30.8f, 63.3f })
                m.Ring(x, 63.2f, 5.3f, 3.2f);
        }

        /// <summary>A lorry from behind on its lane line (示39, E.90, units of the disc): the box, the bumper, two pairs of wheels standing on the line at the bottom.</summary>
        private static void MandTruckRear(MandPic m)
        {
            m.Rect(15.9f, 65.7f, 67.9f, 3.3f);
            m.RoundRect(48.8f, 25.9f, 30.6f, 27.7f, 2.5f, false, 0.4f);
            m.Rect(48.8f, 55.4f, 30.6f, 5.4f);
            foreach (float x in new[] { 49.6f, 54.4f, 70.1f, 74.9f })
                m.RoundRect(x, 60.4f, 3.1f, 5.6f, 0f, false, 1.4f);
        }

        public static string M_CarFront => MandFit(m => MandCarFront(m));
        public static string M_CarFrontHov => MandFit(m => MandCarFront(m, true));
        public static string M_CarSide => MandFit(MandCarSide);
        public static string M_Bus => MandFit(MandBus);
        public static string M_Tram => MandFit(MandTram);
        public static string M_Truck => MandFit(MandTruck);
        public static string M_TruckRear => MandFit(MandTruckRear);

        /// <summary>A figure built by the function, fitted into the 100 x 100 box (5 units of room all round).</summary>
        internal static string MandFit(Action<MandPic> build)
        {
            var m = new MandPic();
            build(m);
            return m.Fitted();
        }

        // ------------------------------------------------------------------ two-wheelers and people

        /// <summary>A bicycle facing left (示31, E.81, units of the disc): two wheels, the chainring with its pedals, the frame, the curved fork, the handlebar and the saddle; tubes 2.7 thick.</summary>
        private static void MandBicycle(MandPic m)
        {
            const float t = 2.7f;
            m.Ring(28.9f, 54.1f, 10.9f, 8.2f).Ring(71.0f, 54.1f, 10.9f, 8.2f).Ring(53.3f, 54.1f, 4.65f, 2.66f);
            m.Capsule(37.8f, 36.5f, 59.5f, 36.5f, t);
            m.Capsule(37.8f, 38f, 50.5f, 50.8f, t);
            m.Capsule(60.5f, 31.5f, 54.9f, 49.8f, t);
            m.Capsule(60f, 35.5f, 71f, 54.1f, t);
            m.Capsule(57.8f, 54.1f, 71f, 54.1f, t);
            m.Stroke(t, 36.4f, 30.9f, 38.4f, 38.8f, 37.1f, 44.8f, 33.1f, 50.8f, 29.4f, 53.6f);
            m.Capsule(34.5f, 27.8f, 42.8f, 27.0f, t).Capsule(36.4f, 27.6f, 36.4f, 31f, t);
            m.Poly(55f, 30f, 64.6f, 30f, 63.6f, 32.5f, 56.6f, 32.5f);
            m.Capsule(53f, 49.5f, 51.9f, 46.2f, 2.1f).Capsule(53.7f, 58.7f, 54.6f, 61f, 2.1f);
            m.Rect(49.9f, 45f, 4f, 2.2f).Rect(52.6f, 60.1f, 4f, 2.2f);
        }

        /// <summary>An electric bicycle facing left (示33, E.83, units of the disc): heavy wheels, the motor in the middle, the steering column with its bent bar, the lamp on the front wheel, the battery, the long saddle over the rear mudguard, the swing arm.</summary>
        private static void MandEBike(MandPic m)
        {
            m.Ring(27.1f, 52.4f, 12f, 8.4f).Ring(71.7f, 52.4f, 12f, 8.4f).Ring(50.4f, 53.1f, 4.9f, 2.7f);
            m.Capsule(27.1f, 52.4f, 38.6f, 27f, 3.4f);
            m.Stroke(3f, 38.6f, 27f, 40.6f, 25f, 44f, 25f).Rect(42.5f, 23.5f, 3f, 3f);
            m.RoundRect(22f, 37.6f, 5.2f, 2.8f, 1f);
            m.Capsule(35.6f, 36.5f, 47.6f, 50f, 3f);
            m.Poly(37.5f, 33.4f, 45.7f, 33.4f, 49.3f, 39.4f, 46.6f, 47.6f, 41f, 42.6f);
            m.Poly(57.4f, 33.2f, 79.6f, 33.2f, 79.6f, 38.4f, 72f, 38.4f, 68f, 38.9f, 64.6f, 40.3f, 62.5f, 41.5f, 61f, 41f, 57.4f, 35.2f);
            m.Capsule(55.2f, 52.4f, 72.9f, 52.4f, 3.6f);
            m.Capsule(53f, 49f, 55f, 46f, 2.2f).RoundRect(53.8f, 44.6f, 2.6f, 2.6f, 0.4f);
            m.Capsule(47.8f, 57.2f, 46f, 60f, 2.2f).RoundRect(44.7f, 59f, 2.6f, 2.6f, 0.4f);
        }

        /// <summary>A convex polygon pushed out by g (each edge moved outwards, neighbours intersected); the points in screen order either way round.</summary>
        private static float[] MandGrow(float[] p, float g)
        {
            int n = p.Length / 2;
            if (g <= 0f)
                return p;
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += p[i * 2] * p[j * 2 + 1] - p[j * 2] * p[i * 2 + 1];
            }

            float sign = area > 0f ? 1f : -1f;
            var q = new float[p.Length];
            for (int i = 0; i < n; i++)
            {
                int a = (i + n - 1) % n, b = (i + 1) % n;
                // Outward normals of the edges a->i and i->b.
                float e1x = p[i * 2] - p[a * 2], e1y = p[i * 2 + 1] - p[a * 2 + 1], e2x = p[b * 2] - p[i * 2], e2y = p[b * 2 + 1] - p[i * 2 + 1];
                float l1 = (float)Math.Sqrt(e1x * e1x + e1y * e1y), l2 = (float)Math.Sqrt(e2x * e2x + e2y * e2y);
                float n1x = sign * e1y / l1, n1y = -sign * e1x / l1, n2x = sign * e2y / l2, n2y = -sign * e2x / l2;
                float bx = n1x + n2x, by = n1y + n2y, bl = (float)Math.Sqrt(bx * bx + by * by);
                float k = g / Math.Max(0.3f, (bx * n1x + by * n1y) / Math.Max(1e-4f, bl)) / Math.Max(1e-4f, bl);
                q[i * 2] = p[i * 2] + bx * k;
                q[i * 2 + 1] = p[i * 2 + 1] + by * k;
            }

            return q;
        }

        /// <summary>A person walking to the left (示35, E.85, units of the disc), made of a head, a torso and limbs, every part g wider (the halo that keeps the figure clear of what is under it).</summary>
        private static void MandWalker(MandPic m, float g = 0f)
        {
            float d = 2f * g;
            m.Circle(50f, 20.8f, 6.7f + g);
            m.Poly(MandGrow(new[] { 43.4f, 33.7f, 46.2f, 30.8f, 50f, 29.7f, 53.8f, 30.8f, 57.2f, 33.7f, 57.9f, 44f, 57.9f, 60f, 42.9f, 57f, 42.9f, 44f }, g));
            m.Capsule(44.5f, 35.5f, 38.5f, 46f, 6f + d).Capsule(38.5f, 47.3f, 29.4f, 50.6f, 5.4f + d);
            m.Capsule(56.5f, 35.5f, 64.3f, 45.5f, 5.6f + d).Capsule(64.3f, 45.5f, 64.3f, 54.8f, 5f + d);
            m.Capsule(47f, 56.5f, 38.8f, 65f, 7.4f + d).Capsule(38.8f, 65f, 37f, 80.6f, 6.4f + d);
            m.Capsule(53.9f, 58f, 54f, 66.5f, 7.8f + d).Capsule(54f, 66.5f, 63f, 81.3f, 6.6f + d);
        }

        /// <summary>A person standing and pushing a bicycle (示38, E.89, units of the 40 cm disc): the person alone, g wider for the halo.</summary>
        private static void MandPusher(MandPic m, float g = 0f)
        {
            m.Circle(50.7f, 16.3f, 5.3f + g);
            m.RoundRect(46f - g, 23.4f - g, 9.9f + 2f * g, 39f + g, 4.95f + g, false, 0f);
            m.Poly(MandGrow(new[] { 46f, 60f, 51.6f, 60f, 51.6f, 75.5f, 50.7f, 77f, 46f, 79f }, g));
            m.Capsule(48f, 35.5f, 36.5f, 39.6f, 4f + 2f * g);
        }

        /// <summary>A person in a wheelchair facing left (示40 for the disabled, Fig. 127 a; drawn in the pixels of the printed figure).</summary>
        private static void MandWheelchair(MandPic m)
        {
            m.Circle(390f, 222f, 22f);
            m.Capsule(380f, 262f, 370f, 348f, 26f);
            m.Capsule(372f, 305f, 308f, 312f, 16f);
            m.Capsule(372f, 352f, 300f, 355f, 24f);
            m.Capsule(298f, 357f, 252f, 437f, 20f);
            m.Sector(395f, 378f, 60f, 72f, -70f, 160f);
        }

        public static string M_Bicycle => MandFit(MandBicycle);
        public static string M_EBike => MandFit(MandEBike);
        public static string M_Pedestrian => MandFit(m => MandWalker(m));
        public static string M_PushingPerson => MandFit(m => MandPusher(m));
        public static string M_Wheelchair => MandFit(MandWheelchair);

        // ------------------------------------------------------------------ the other figures

        /// <summary>The horn (示12, E.61, units of the disc): the bell at the left, the tube looping round to the right, back along the bottom, round a small bend and out through the loop to the mouthpiece.</summary>
        private static void MandHorn(MandPic m)
        {
            // The bell: its top edge falls from (15.6, 27.4) to the top tube at (42, 41.1), its bottom edge rises from (15.6, 63.5) to (44, 46.4) (Fig. 93 and E.61).
            var p = new List<float>();
            for (int i = 0; i <= 12; i++)
            {
                float x = 15.6f + 26.4f * i / 12f;
                p.Add(x);
                p.Add(41.1f - 13.7f * (float)Math.Pow((42f - x) / 26.4f, 2.2));
            }

            p.AddRange(new[] { 46f, 41.1f, 46f, 46.4f });
            for (int i = 12; i >= 0; i--)
            {
                float x = 15.6f + 28.4f * i / 12f;
                p.Add(x);
                p.Add(46.4f + 17.1f * (float)Math.Pow((44f - x) / 28.4f, 2.2));
            }

            m.Poly(p.ToArray());
            // The tube: along the top to the loop round (72, 53.9), back along the bottom, round the left end (39, 59.3) and out through the loop to the mouthpiece.
            m.Rect(42f, 41.1f, 30f, 5.3f).Sector(72f, 53.9f, 7.5f, 12.8f, -90f, 90f).Rect(39f, 61.4f, 33f, 5.3f);
            m.Sector(39f, 59.25f, 2.35f, 7.4f, 90f, 270f).Rect(39f, 51.85f, 46f, 5.05f);
            m.Poly(84f, 51.85f, 89f, 49f, 89f, 59.8f, 84f, 56.9f);
        }

        /// <summary>The headlamp (示13, E.62, units of the disc): the lamp, a lens bulging to the left and a dome to the right, and five beams rising a little to the right.</summary>
        private static void MandHeadlamp(MandPic m)
        {
            var p = new List<float>();
            MandArc(p, 91.7f, 48f, 44.8f, 210f, 150f, 12);
            for (int i = 0; i <= 18; i++)
            {
                double a = (90.0 - 180.0 * i / 18) * Math.PI / 180.0;
                p.Add(53f + 32.7f * (float)Math.Cos(a));
                p.Add(48f + 22.4f * (float)Math.Sin(a));
            }

            m.Poly(p.ToArray());
            for (int k = 0; k < 5; k++)
            {
                float y = 32.3f + 9.3f * k;
                m.Bar(21f, y + 3f, 43.9f, y - 3f, 4.8f);
            }
        }

        /// <summary>The slab-serif P of the parking signs (示40, E.91, units of the square board).</summary>
        private static void MandP(MandPic m)
        {
            var p = new List<float> { 26f, 19.9f, 61f, 19.9f };
            MandArc(p, 61f, 34.8f, 14.9f, -90f, 90f, 18);
            p.AddRange(new[] { 45.2f, 49.7f, 45.2f, 69.5f, 46.2f, 71.8f, 48.5f, 72.8f, 54.5f, 73f, 54.5f, 80.1f, 26.3f, 80.1f, 26.3f, 73f, 31.2f, 72.8f, 32.9f, 71.6f, 33.7f, 69.5f,
                               33.7f, 30f, 33.2f, 27.9f, 31.9f, 26.6f, 26f, 26.6f });
            m.Poly(p.ToArray());
            var h = new List<float> { 45.2f, 26.6f, 54.9f, 26.6f };
            MandArc(h, 54.9f, 34.5f, 7.9f, -90f, 90f, 12);
            h.AddRange(new[] { 45.2f, 42.4f });
            m.Poly(true, h.ToArray());
        }

        /// <summary>A taxi from the front (示40 for taxis, Fig. 129 a): the car of 示24 with the lamp on its roof.</summary>
        private static void MandTaxi(MandPic m)
        {
            MandCarFront(m);
            m.RoundRect(42.9f, 23.6f, 16.2f, 5.6f, 0.8f);
        }

        /// <summary>A charging post with its cable and plug (示40 for charging, Fig. 132 a; in the pixels of the printed figure): the screen and the lightning are holes.</summary>
        private static void MandCharger(MandPic m)
        {
            m.RoundRect(318f, 285f, 54f, 110f, 8f, false, 0f).Rect(310f, 393f, 70f, 9f);
            m.RoundRect(330f, 297f, 30f, 22f, 3f, true);
            m.Poly(true, 348f, 328f, 336f, 353f, 346f, 353f, 340f, 374f, 357f, 345f, 347f, 345f, 353f, 328f);
            m.Stroke(5f, 372f, 362f, 386f, 380f, 398f, 378f, 408f, 360f, 410f, 325f);
            m.RoundRect(402f, 302f, 16f, 24f, 3f).Rect(404f, 288f, 4f, 15f).Rect(412f, 288f, 4f, 15f);
        }

        /// <summary>The zebra under the person of 示16 (E.65, units of the square board): five slanting bars, the first one shorter where the triangle narrows.</summary>
        private static void MandZebra(MandPic m)
        {
            float[] x = { 17.5f, 29.8f, 42.1f, 54.4f, 66.7f };
            for (int k = 0; k < x.Length; k++)
            {
                float top = k == 0 ? 68f : 62.2f, dx = 9.3f * (top - 62.2f) / 17.3f;
                m.Poly(x[k] + dx, top, x[k] + dx + 5.2f, top, x[k] + 9.3f + 5.2f, 79.5f, x[k] + 9.3f, 79.5f);
            }
        }

        /// <summary>Draws a figure into the box (x0, y0)-(x1, y1) of m, scaled evenly and centred by the bounds of shape (turned by degrees first); draw, when given, is drawn in that same frame (a halo).</summary>
        private static void MandPlace(MandPic m, Action<MandPic> shape, float x0, float y0, float x1, float y1, Action<MandPic> draw = null, float degrees = 0f)
        {
            var t = new MandPic().Frame(1f, 0f, 0f, degrees);
            shape(t);
            t.Bounds(out float a0, out float b0, out float a1, out float b1);
            float s = Math.Min((x1 - x0) / (a1 - a0), (y1 - y0) / (b1 - b0));
            m.Frame(s, (x0 + x1) * 0.5f - (a0 + a1) * 0.5f * s, (y0 + y1) * 0.5f - (b0 + b1) * 0.5f * s, degrees);
            (draw ?? shape)(m);
            m.Frame();
        }

        public static string M_Horn => MandFit(MandHorn);
        public static string M_Headlamp => MandFit(MandHeadlamp);
        public static string M_ParkingP => MandFit(MandP);
        public static string M_Taxi => MandFit(MandTaxi);
        public static string M_Charger => MandFit(MandCharger);
        public static string M_CarFrontTilted => MandFit(m => MandPlace(m, c => MandCarFront(c), 0f, 0f, 100f, 100f, null, -7f));

        // The arrows, fitted, for the glyph sheet (the signs use the figures in the frame of their board, MandFigure).
        public static string M_ArrowAhead => MandFitPath("ahead");
        public static string M_ArrowTurnLeft => MandFitPath("turnL");
        public static string M_ArrowAheadLeft => MandFitPath("aheadL");
        public static string M_ArrowLeftRight => MandFitPath("leftRight");
        public static string M_ArrowKeepRight => MandFitPath("keepR");
        public static string M_ArrowRoundabout => MandFitPath("roundabout");
        public static string M_ArrowUTurn => MandFitPath("uturn");
        public static string M_ArrowUTurnLeft => MandFitPath("lane:ul");

        private static string MandFitPath(string key)
        {
            // Read the figure back as contours and fit them.
            var m = new MandPic();
            foreach (float[] c in GuidePath.Parse(MandFigure(key)))
                m.Poly(c);
            return m.Fitted();
        }
    }
}
