using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The warning (警告) signs of GB 5768.2-2022 chapter 7 (警1 to 警47): a yellow triangle with a black edge and a black figure (Table 8), and the few that are not triangles (警31, 警32,
    /// 警38, 警42, 警43, 警46, 警47). The figures are redrawn from the printed ones (Fig. 139 to 201) and the drawings of Appendix E (Fig. E.100 to E.165).
    ///
    /// The figures are written in "field units": the yellow field of the triangle as a square canvas, x from 0 at the left corner of the field's base to 100 at the right one, y 100 on the
    /// base; the apex of the field is then at (50, 13.4). The printed figures were measured in that frame (a grid laid over Fig. 139 to 198 at 3000 px a page), so a figure keeps its place
    /// and its size at every band of Table 8. Black masses that fill the triangle (the slope of 警5, the cliff of 警18 and 警21, the bank of 警22...) keep a yellow line of 2.5 units from the
    /// edge, as printed.
    /// </summary>
    public static class WarningSigns
    {
        private const string G = StandardSigns.Warning;
        private const GuideColour K = GuideColour.Black;

        // ------------------------------------------------------------------ the frame of the figures

        /// <summary>The field follows the border polygon in SignStyles.Warning; field units keep the pictogram inside the painted interior.</summary>
        internal static Canvas Field(Canvas c)
        {
            GuideElement f = c.Part.Elements[1];
            return new Canvas(c.Part, f.X, f.Bottom - f.W, f.W, f.W);
        }

        /// <summary>The apex of the field (field units): x 50, y 100 minus the height of an equilateral triangle of side 100.</summary>
        private const float ApexY = 13.3975f;

        /// <summary>The inradius of the field (field units).</summary>
        private const float InR = 28.8675f;

        /// <summary>The yellow line between the edge and a black mass that fills the triangle (field units, read off Fig. 149, 166, 169 and 170).</summary>
        private const float Gap = 2.5f;

        /// <summary>The field's triangle drawn g units inside its sides (the outline of a mass that fills the triangle).</summary>
        private static float[] Inset(float g)
        {
            float k = (InR - g) / InR, cx = 50f, cy = 100f - InR;
            return new[] { cx, cy + (ApexY - cy) * k, cx - 50f * k, cy + InR * k, cx + 50f * k, cy + InR * k };
        }

        /// <summary>A polygon clipped by a convex one (Sutherland-Hodgman); both as x, y pairs in any order of turning.</summary>
        private static float[] ClipConvex(float[] subject, float[] clip)
        {
            int n = clip.Length / 2;
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += clip[i * 2] * clip[j * 2 + 1] - clip[j * 2] * clip[i * 2 + 1];
            }

            float sign = area > 0f ? 1f : -1f;
            var points = new List<float>(subject);
            for (int e = 0; e < n && points.Count >= 6; e++)
            {
                float ax = clip[e * 2], ay = clip[e * 2 + 1], bx = clip[(e + 1) % n * 2], by = clip[(e + 1) % n * 2 + 1];
                float Side(float x, float y) => sign * ((bx - ax) * (y - ay) - (by - ay) * (x - ax));
                var next = new List<float>();
                int m = points.Count / 2;
                for (int i = 0; i < m; i++)
                {
                    float px = points[i * 2], py = points[i * 2 + 1], qx = points[(i + 1) % m * 2], qy = points[(i + 1) % m * 2 + 1];
                    float sp = Side(px, py), sq = Side(qx, qy);
                    if (sp >= 0f)
                    {
                        next.Add(px);
                        next.Add(py);
                    }

                    if ((sp >= 0f) != (sq >= 0f))
                    {
                        float t = sp / (sp - sq);
                        next.Add(px + (qx - px) * t);
                        next.Add(py + (qy - py) * t);
                    }
                }

                points = next;
            }

            return points.ToArray();
        }

        /// <summary>A black mass that fills the triangle: the polygon cut to the field less the yellow line.</summary>
        private static PicBuilder Mass(this PicBuilder b, params float[] polygon) => b.Poly(ClipConvex(polygon, Inset(Gap)));

        /// <summary>
        /// A path drawn on the field (field units), mirrored left to right when asked. The element's box is what the path draws (the field's square reaches above the triangle, and an
        /// element's box must stay in the part).
        /// </summary>
        private static void Draw(Canvas f, string path, bool mirror = false, GuideColour colour = K)
        {
            GuideElement e = GuideKit.Picture(mirror ? Glyphs.Mirror(path) : path, f.X, f.Y, f.W, f.H, colour);
            GuidePath.Bounds(e.Contours, out float x0, out float y0, out float x1, out float y1);
            e.X = x0;
            e.Y = y0;
            e.W = x1 - x0;
            e.H = y1 - y0;
            f.Add(e);
        }

        /// <summary>A glyph put into an area of the field, mirrored left to right when asked (the area stays where it is: the figure turns round in it).</summary>
        private static void Put(Canvas f, string glyph, float x, float y, float w, float h, bool mirror = false, GuideColour colour = K)
        {
            if (mirror)
                f.Pic(Glyphs.Mirror(glyph), colour, 100f - x - w, y, w, h);
            else
                f.Pic(glyph, colour, x, y, w, h);
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>A band through points: square ends, round outer corners where it turns (the roads of 警2 to 警4).</summary>
        internal static PicBuilder Band(this PicBuilder b, float width, params float[] p)
        {
            float r = width * 0.5f;
            for (int i = 0; i + 3 < p.Length; i += 2)
            {
                float dx = p[i + 2] - p[i], dy = p[i + 3] - p[i + 1], len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f)
                    continue;
                float nx = -dy / len * r, ny = dx / len * r;
                b.Poly(p[i] + nx, p[i + 1] + ny, p[i + 2] + nx, p[i + 3] + ny, p[i + 2] - nx, p[i + 3] - ny, p[i] - nx, p[i + 1] - ny);
                if (i > 0)
                    b.Circle(p[i], p[i + 1], r);
            }

            return b;
        }

        /// <summary>An arrow head: its tip, the direction it points, its length to the barbs, its half width, and how far the middle of its back is drawn in towards the tip (swept barbs).</summary>
        internal static PicBuilder Head(this PicBuilder b, float tx, float ty, float dx, float dy, float length, float half, float notch = 0f)
        {
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            dx /= len;
            dy /= len;
            float bx = tx - dx * length, by = ty - dy * length, nx = -dy * half, ny = dx * half;
            return b.Poly(tx, ty, bx + nx, by + ny, tx - dx * (length - notch), ty - dy * (length - notch), bx - nx, by - ny);
        }

        /// <summary>A band along a circular arc (centre, radius of its middle, width), from angle a0 to a1 in degrees, counter-clockwise as seen (0 to the right, 90 up).</summary>
        internal static PicBuilder Arc(this PicBuilder b, float cx, float cy, float r, float width, float a0, float a1, int steps = 16)
        {
            var p = new List<float>();
            float ro = r + width * 0.5f, ri = r - width * 0.5f;
            for (int i = 0; i <= steps; i++)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p.Add(cx + ro * (float)Math.Cos(a));
                p.Add(cy - ro * (float)Math.Sin(a));
            }

            for (int i = steps; i >= 0; i--)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p.Add(cx + ri * (float)Math.Cos(a));
                p.Add(cy - ri * (float)Math.Sin(a));
            }

            return b.Poly(p.ToArray());
        }

        /// <summary>A straight bar with square ends from one point to another.</summary>
        internal static PicBuilder Bar(this PicBuilder b, float x0, float y0, float x1, float y1, float width) => b.Band(width, x0, y0, x1, y1);

        /// <summary>A line through points with sharp (mitred) corners and square ends, as one polygon (the edges of the roads of 警7 and 警8).</summary>
        internal static PicBuilder Miter(this PicBuilder b, float width, params float[] p)
        {
            int n = p.Length / 2;
            float r = width * 0.5f;
            var left = new List<float>();
            var right = new List<float>();
            for (int i = 0; i < n; i++)
            {
                // The unit normals of the segments before and after the point; at an end, the one segment's.
                float nx = 0f, ny = 0f;
                for (int s = i - 1; s <= i; s++)
                {
                    if (s < 0 || s >= n - 1)
                        continue;
                    float dx = p[s * 2 + 2] - p[s * 2], dy = p[s * 2 + 3] - p[s * 2 + 1], len = (float)Math.Sqrt(dx * dx + dy * dy);
                    nx += -dy / len;
                    ny += dx / len;
                }

                float m = (float)Math.Sqrt(nx * nx + ny * ny);
                nx /= m;
                ny /= m;
                // The mitre: as far out as makes the offset edges meet (r over the cosine of half the turn).
                float cos = 1f;
                if (i > 0 && i < n - 1)
                {
                    float dx = p[i * 2] - p[i * 2 - 2], dy = p[i * 2 + 1] - p[i * 2 - 1], len = (float)Math.Sqrt(dx * dx + dy * dy);
                    cos = Math.Max(0.3f, nx * (-dy / len) + ny * (dx / len));
                }

                left.Add(p[i * 2] + nx * r / cos);
                left.Add(p[i * 2 + 1] + ny * r / cos);
                right.Add(p[i * 2] - nx * r / cos);
                right.Add(p[i * 2 + 1] - ny * r / cos);
            }

            for (int i = n - 1; i >= 0; i--)
            {
                left.Add(right[i * 2]);
                left.Add(right[i * 2 + 1]);
            }

            return b.Poly(left.ToArray());
        }

        /// <summary>An arrow from a tail to a tip: a shaft of a width and a head (length to the barbs, half width, swept barbs by the notch).</summary>
        internal static PicBuilder Arrow(this PicBuilder b, float x0, float y0, float x1, float y1, float width, float length, float half, float notch = 0f)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float back = length - notch + 0.5f;
            return b.Band(width, x0, y0, x1 - dx / len * back, y1 - dy / len * back).Head(x1, y1, dx, dy, length, half, notch);
        }

        /// <summary>
        /// An arrow whose head is an open chevron of two legs at 45 degrees (警9, 警39): the outer edges run back from the tip by length, then the legs are cut square to the shaft for cut, and
        /// their inner edges run back at 45 degrees to the shaft.
        /// </summary>
        internal static PicBuilder Arrow45(this PicBuilder b, float x0, float y0, float x1, float y1, float width, float length, float cut)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            dx /= len;
            dy /= len;
            float nx = -dy, ny = dx, h = width * 0.5f;
            float[] p = new float[18];
            int k = 0;
            void Add(float along, float side)
            {
                p[k++] = x1 - dx * along + nx * side;
                p[k++] = y1 - dy * along + ny * side;
            }

            float inner = cut + h;
            Add(0f, 0f);
            Add(length, length);
            Add(length + cut, length);
            Add(inner, h);
            Add(len, h);
            Add(len, -h);
            Add(inner, -h);
            Add(length + cut, -length);
            Add(length, -length);
            return b.Poly(p);
        }

        // ------------------------------------------------------------------ templates

        private static GuideTemplate Tri(string id, string code, string zh, string en, string pinyin, string[] keywords, Action<GuideSpec, Canvas> art, GuideField[] extra = null,
            GuideColour field = GuideColour.Yellow, GuideColour edge = GuideColour.Black) =>
            StandardSigns.Make(id, code, zh, en, G, pinyin, keywords, (spec, band) => SignStyles.Warning(band, c => art(spec, Field(c)), 0.52f, field, edge), extra);

        private static GuideTemplate Tri(string id, string code, string zh, string en, string pinyin, string[] keywords, Action<Canvas> art, params string[] legacyIds) =>
            StandardSigns.Make(id, code, zh, en, G, pinyin, keywords, (spec, band) => SignStyles.Warning(band, c => art(Field(c))), null, legacyIds);

        /// <summary>
        /// The warning signs about people (警10, 警11, 警12: GB 5768.2 7.11 to 7.13, Table 2) have a yellow or a fluorescent yellow-green ground; yellow when the setting is not there, so a board placed
        /// before it was is the same string and the same board. art is given the ground, for what it draws in the ground's own colour.
        /// </summary>
        private static GuideTemplate TriPeople(string id, string code, string zh, string en, string pinyin, string[] keywords, Action<Canvas, GuideColour> art) =>
            StandardSigns.Make(id, code, zh, en, G, pinyin, keywords, (spec, band) =>
            {
                GuideColour ground = GroundOf(spec);
                return SignStyles.Warning(band, c => art(Field(c), ground), 0.52f, ground, GuideColour.Black);
            }, new[] { GroundField() });

        private static GuideField GroundField() =>
            GuideField.Choice("ground", "底色", "Ground", "y", new[] { C("y", "黄色", "Yellow"), C("g", "荧光黄绿", "Fluorescent yellow-green") }).AsSparse();

        private static GuideColour GroundOf(GuideSpec spec) => spec.Is("ground", "g") ? GuideColour.FluorescentYellowGreen : GuideColour.Yellow;

        private static GuideChoice C(string value, string zh, string en) => new GuideChoice(value, zh, en);

        private static GuideField LeftRight(string zhLeft = "向左", string zhRight = "向右", string enLeft = "Left", string enRight = "Right", string def = "l") =>
            GuideField.Choice("dir", "方向", "Direction", def, new[] { C("l", zhLeft, enLeft), C("r", zhRight, enRight) });

        private static bool Right(GuideSpec s) => s.Is("dir", "r");

        // ------------------------------------------------------------------ 警1 交叉路口

        private static readonly GuideChoice[] kCrossings =
        {
            C("1", "警1-1 十字交叉", "1-1 Crossroads"),
            C("2", "警1-2 错位交叉", "1-2 Staggered junction"),
            C("3", "警1-3 左侧斜交（向后）", "1-3 Side road at an angle, left, behind"),
            C("4", "警1-4 右侧斜交（向后）", "1-4 Side road at an angle, right, behind"),
            C("5", "警1-5 左侧斜交（向前）", "1-5 Side road at an angle, left, ahead"),
            C("6", "警1-6 右侧斜交（向前）", "1-6 Side road at an angle, right, ahead"),
            C("7", "警1-7 Y形交叉", "1-7 Y junction"),
            C("8", "警1-8 T形交叉", "1-8 T junction"),
            C("9", "警1-9 左侧T形交叉", "1-9 Side road on the left"),
            C("10", "警1-10 右侧T形交叉", "1-10 Side road on the right"),
            C("11", "警1-11 环形交叉", "1-11 Roundabout")
        };

        /// <summary>
        /// 警1 (Fig. 139, E.100 to E.110): the road the driver is on and the crossing roads as bars 10 units wide. narrow: the crossing road is narrower than the driver's (Fig. 140 a to c:
        /// the driver's road 11, the other 7).
        /// </summary>
        private static void Crossing(GuideSpec s, Canvas f)
        {
            string form = s.Get("form");
            bool narrow = s.Flag("narrow");
            float main = narrow ? 11f : 10f, side = narrow ? 7f : 10f;
            var b = new PicBuilder();
            switch (form)
            {
                case "2":
                    b.Rect(45f - main * 0.5f, 41f, main, 54f).Rect(50f, 59f - side * 0.5f, 16f, side).Rect(24f, 78f - side * 0.5f, 16f, side);
                    break;
                case "3":
                case "4":
                    b.Rect(51f - main * 0.5f, 45f, main, 50f).Bar(27f, 85.5f, 51f, 66f, side);
                    break;
                case "5":
                case "6":
                    b.Rect(51f - main * 0.5f, 45f, main, 50f).Bar(32f, 57f, 51f, 74f, side);
                    break;
                case "7":
                    b.Rect(50f - main * 0.5f, 70f, main, 25f).Bar(33f, 54f, 50f, 71f, main).Bar(67f, 54f, 50f, 71f, main);
                    break;
                case "8":
                    b.Rect(50f - main * 0.5f, 61.5f, main, 33.5f).Rect(30f, 61.5f - side * 0.5f, 40f, side);
                    break;
                case "9":
                case "10":
                    b.Rect(55f - main * 0.5f, 45f, main, 50f).Rect(26f, 70f - side * 0.5f, 29f, side);
                    break;
                case "11":
                    // Three arrows round a ring, counter-clockwise (the traffic of a roundabout goes round it counter-clockwise). One arrow measured on Fig. 139 k) as polar points (angle in
                    // degrees counter-clockwise from the right, radius) round the middle of the ring; the other two are it turned by 120 and 240 degrees.
                    for (int k = 0; k < 3; k++)
                        b.Poly(Polar(50f, 72.75f, 120f * k, RoundaboutArrow()));
                    break;
                default:
                    b.Rect(50f - main * 0.5f, 45f, main, 50f).Rect(25f, 70f - side * 0.5f, 50f, side);
                    break;
            }

            Draw(f, b.ToString(), form == "4" || form == "6" || form == "10");
        }

        /// <summary>One arrow of 警1-11: the outer edge of the ring from its tail to the head, the head (swept barbs), and the inner edge back (polar: angle, radius).</summary>
        private static float[] RoundaboutArrow()
        {
            var p = new List<float>();
            for (int i = 0; i <= 12; i++)
            {
                p.Add(39.2f + (115.8f - 39.2f) * i / 12f);
                p.Add(20.4f);
            }

            p.AddRange(new[] { 104.9f, 25.6f, 119.6f, 25.5f, 141.8f, 20.5f, 126.2f, 11.3f, 94.3f, 12.1f, 113f, 16.3f });
            for (int i = 12; i >= 0; i--)
            {
                p.Add(38.6f + (113f - 38.6f) * i / 12f);
                p.Add(16.1f);
            }

            return p.ToArray();
        }

        /// <summary>Polar points (angle in degrees counter-clockwise from the right as seen, radius) turned by a further angle, as x, y pairs round a middle.</summary>
        private static float[] Polar(float cx, float cy, float turn, float[] polar)
        {
            var p = new float[polar.Length];
            for (int i = 0; i + 1 < polar.Length; i += 2)
            {
                double a = (polar[i] + turn) * Math.PI / 180.0;
                p[i] = cx + polar[i + 1] * (float)Math.Cos(a);
                p[i + 1] = cy - polar[i + 1] * (float)Math.Sin(a);
            }

            return p;
        }

        // ------------------------------------------------------------------ 警2 to 警4: bends

        /// <summary>警2 急弯路 (Fig. 141, E.111): a road that turns sharply, with an arrow; drawn for a bend to the left.</summary>
        private static readonly string kSharpBend = new PicBuilder()
            .Band(8.1f, 41.2f, 92.75f, 69.8f, 66.6f, 47.25f, 58.15f)
            .Poly(32.7f, 52.4f, 47.2f, 46.2f, 57.8f, 50.05f, 48.3f, 54.2f, 46.2f, 62.1f, 50.1f, 70.4f, 39.8f, 66.7f)
            .ToString();

        /// <summary>警3 反向弯路 (Fig. 143, E.113): two bends the other way round; drawn as 警3-1.</summary>
        private static readonly string kReverseBend = new PicBuilder().Band(10f, 49.3f, 47.2f, 36.5f, 67f, 69f, 67f, 49f, 91f).ToString();

        /// <summary>警4 连续弯路 (Fig. 146, E.115): a road of several bends; drawn as 警4-1.</summary>
        private static readonly string kWindingRoad = new PicBuilder().Band(10f, 61.2f, 52.6f, 41.5f, 62.3f, 58.5f, 67.8f, 41.5f, 73.4f, 58.5f, 79f, 38.5f, 88.9f).ToString();

        // ------------------------------------------------------------------ 警5 to 警9

        /// <summary>
        /// 警5 陡坡 (Fig. 149, 150, E.117, E.118): a black slope filling the bottom of the triangle and a yellow arrow along it. Drawn for 警5-1 (up, the slope high on the left); 警5-2 is the
        /// slope mirrored and the arrow pointing down it. The grade (Fig. 150, "8%") is a setting; empty: none, as in Fig. 149. The printed figure writes it along the slope; here it is
        /// written level above the slope (the canvas has no turned text).
        /// </summary>
        private static void Slope(GuideSpec s, Canvas f)
        {
            bool down = s.Is("dir", "down");
            Draw(f, new PicBuilder().Mass(6.3f, 46.24f, 110f, 105.7f, 110f, 110f, -10f, 110f, -10f, 46.24f).ToString(), down);
            string arrow = down ? new PicBuilder().Arrow(65.1f, 72.1f, 32.5f, 91f, 3.6f, 9.5f, 6.7f, 2.7f).ToString() : new PicBuilder().Arrow(67.5f, 91f, 34.9f, 72.1f, 3.6f, 9.5f, 6.7f, 2.7f).ToString();
            Draw(f, arrow, false, GuideColour.Yellow);
            string grade = s.Get("grade");
            if (!string.IsNullOrEmpty(grade))
                f.Ink(grade + "%", K, down ? 26f : 46f, 53f, 28f, 12f);
        }

        /// <summary>警6 连续下坡 (Fig. 151, E.119): two slopes one after the other going down to the left, an arrow down each.</summary>
        private static readonly string kDownhills = new PicBuilder()
            .Poly(17.9f, 95.8f, 71.3f, 95.8f, 71.3f, 54.3f, 54.1f, 75.8f, 37.8f, 75.8f)
            .Arrow(64.1f, 56f, 50.4f, 72.5f, 2.4f, 6f, 4.6f, 3f)
            .Arrow(32.7f, 73.7f, 17.9f, 88.9f, 2.4f, 6f, 4.6f, 3f)
            .ToString();

        /// <summary>警7 窄路 (Fig. 152, E.120 to E.122): the edges of the road coming in. both: 警7-1; else the side that narrows (警7-2 right, 警7-3 left).</summary>
        private static void Narrows(GuideSpec s, Canvas f)
        {
            const float w = 4.7f;
            var b = new PicBuilder();
            switch (s.Get("side"))
            {
                case "both":
                    b.Miter(w, 42.6f, 41.5f, 42.6f, 61f, 36.1f, 78f, 36.1f, 94.2f).Miter(w, 57.4f, 41.5f, 57.4f, 61f, 63.9f, 78f, 63.9f, 94.2f);
                    break;
                default:
                    b.Miter(w, 41.6f, 41.6f, 41.6f, 94.6f).Miter(w, 56.3f, 41.6f, 56.3f, 61f, 62.75f, 78f, 62.75f, 94.6f);
                    break;
            }

            Draw(f, b.ToString(), s.Is("side", "l"));
        }

        /// <summary>警8 窄桥 (Fig. 155, E.123): the parapets of a bridge narrower than the road.</summary>
        private static readonly string kNarrowBridge = new PicBuilder()
            .Miter(4.2f, 38f, 45.6f, 38f, 53.7f, 43f, 58.4f, 43f, 81.6f, 38f, 86.3f, 38f, 95f)
            .Miter(4.2f, 62f, 45.6f, 62f, 53.7f, 57f, 58.4f, 57f, 81.6f, 62f, 86.3f, 62f, 95f)
            .ToString();

        /// <summary>警9 双向交通 (Fig. 156, E.124): an arrow down on the left, an arrow up on the right (traffic keeps right).</summary>
        private static readonly string kTwoWay = new PicBuilder()
            .Arrow45(38.6f, 52.1f, 38.6f, 89.9f, 5f, 8f, 5.2f)
            .Arrow45(60.6f, 89.4f, 60.6f, 51.6f, 5f, 8f, 5.2f)
            .ToString();

        // ------------------------------------------------------------------ 警10 to 警17

        /// <summary>警10 注意行人 (Fig. 157, E.125): the stripes of a zebra crossing, slanting, and a person walking over them with a yellow line round the legs where they cross the stripes.</summary>
        /// <summary>The gap round the walker is cut in the ground's own colour: a yellow one on the green ground would be a halo.</summary>
        private static void Pedestrians(Canvas f, GuideColour ground)
        {
            var stripes = new PicBuilder();
            for (int k = 0; k < 5; k++)
                stripes.Mass(7.5f + 16f * k, 74f, 14f + 16f * k, 74f, 27f + 16f * k, 96.6f, 20.5f + 16f * k, 96.6f);
            Draw(f, stripes.ToString());
            Draw(f, WarningArt.Walker(1.5f), false, ground);
            Draw(f, WarningArt.Walker(0f));
        }

        /// <summary>警17 注意信号灯 (Fig. 165, E.132): the housing in black, the top light red, the middle one the yellow of the field, the bottom one green.</summary>
        private static void Signals(Canvas f)
        {
            Draw(f, WarningArt.SignalHousing);
            f.Oval(GuideColour.Red, 49.75f - 7.3f, 47.1f - 7.3f, 14.6f, 14.6f);
            f.Oval(GuideColour.Green, 49.75f - 7.3f, 89.7f - 7.3f, 14.6f, 14.6f);
        }

        // ------------------------------------------------------------------ 警18 to 警27

        /// <summary>Points on a circle (centre, radius) from angle a0 to a1 in degrees, counter-clockwise as seen (0 to the right, 90 up), added to a list of x, y pairs.</summary>
        private static List<float> ArcTo(this List<float> p, float cx, float cy, float r, float a0, float a1, int steps = 12)
        {
            for (int i = 0; i <= steps; i++)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p.Add(cx + r * (float)Math.Cos(a));
                p.Add(cy - r * (float)Math.Sin(a));
            }

            return p;
        }

        private static List<float> To(this List<float> p, params float[] points)
        {
            p.AddRange(points);
            return p;
        }

        /// <summary>警18 注意落石 (Fig. 166, E.133): the rock face on the left, its foot along the bottom, four stones falling; 警18-2 is it mirrored.</summary>
        private static readonly string kRockfall = new PicBuilder()
            .Mass(44.3f, 25f, 43.8f, 32.6f, 44.1f, 40.2f, 42.3f, 50.2f, 42.9f, 59f, 43.8f, 63.7f, 45.2f, 72f, 46.4f, 80.1f, 49f, 84.8f, 50.5f, 88.9f, 49.9f, 94.3f, 100f, 94.3f, 100f, 110f,
                -10f, 110f, -10f, 25f)
            .Poly(46.8f, 37f, 48.4f, 36.8f, 48.9f, 39.6f, 47.3f, 40.4f)
            .Poly(53.3f, 47.4f, 55.8f, 46.8f, 57.5f, 49.4f, 57.4f, 52.6f, 55.4f, 52.8f, 53.2f, 50.5f)
            .Poly(61.6f, 57.5f, 63.1f, 57.6f, 64f, 60.4f, 62.8f, 61.9f, 61f, 61f)
            .Poly(61.2f, 72.4f, 63.6f, 72.4f, 64.2f, 75.6f, 63f, 78.6f, 61.3f, 78.3f, 60.4f, 75.9f)
            .ToString();

        /// <summary>警20 易滑 (Fig. 168, E.136): a car skidding and the two wavy tracks it leaves.</summary>
        private static readonly string kSlippery = new PicBuilder()
            .Path(WarningArt.CarSkidding)
            .Poly(40.3f, 96.3f, 26.9f, 78.75f, 27.4f, 77.5f, 28.5f, 76.9f, 49.1f, 68.75f, 42.2f, 62.2f, 51.25f, 68.4f, 51.6f, 70f, 50.3f, 71f, 34.1f, 79.4f, 33.4f, 80f, 50.3f, 96.3f)
            .Poly(58.1f, 96.3f, 44.1f, 79.4f, 44.7f, 78.1f, 61f, 71.25f, 54.7f, 66.25f, 62.8f, 70.9f, 63.75f, 72.2f, 62.2f, 73.4f, 51.25f, 79.7f, 50.6f, 80.6f, 68.1f, 96.3f)
            .ToString();

        /// <summary>警21 傍山险路 (Fig. 169, E.137): the mountain on the left with its cliff, a ledge where the road runs, and the ground along the bottom; 警21-2 mirrored.</summary>
        private static readonly string kMountainRoad = new PicBuilder()
            .Mass(40.3f, 34.1f, 42.3f, 33.2f, 45.5f, 36.1f, 44.7f, 39.1f, 43.1f, 48.4f, 44.1f, 60.2f, 44.3f, 68.4f, 52f, 68.4f, 52f, 65.4f, 53.4f, 65.4f, 53.4f, 68.4f, 54.3f, 68.4f,
                54.4f, 76.6f, 56.4f, 83.6f, 58.1f, 86.5f, 59.9f, 87.7f, 61.7f, 92.4f, 64.6f, 95.1f, 71f, 94.8f, 82.8f, 94.2f, 100f, 94.8f, 100f, 110f, -10f, 110f, -10f, 34.1f)
            .ToString();

        /// <summary>A wave as a stroke: a sine of the period and amplitude round the line v = c, from u0 to u1, its crests at u = crest + k period.</summary>
        private static PicBuilder Wave(this PicBuilder b, float u0, float u1, float c, float amplitude, float period, float crest, float width)
        {
            var p = new List<float>();
            int n = (int)Math.Ceiling((u1 - u0) / 1.2f);
            for (int i = 0; i <= n; i++)
            {
                float u = u0 + (u1 - u0) * i / n;
                p.Add(u);
                p.Add(c - amplitude * (float)Math.Cos(2.0 * Math.PI * (u - crest) / period));
            }

            return b.Stroke(width, p.ToArray());
        }

        /// <summary>警22 堤坝路 (Fig. 170, E.139): the bank on the left, the water on the right as two waves, a car going over the edge; 警22-2 mirrored.</summary>
        private static readonly string kEmbankment = new PicBuilder()
            .Mass(-10f, 71.7f, 50.8f, 71.7f, 50.8f, 110f, -10f, 110f)
            .Wave(51.5f, 88.6f, 84.3f, 1.8f, 17.4f, 62.7f, 2.2f)
            .Wave(51.5f, 91.5f, 92.5f, 1.9f, 17.4f, 63f, 2.2f)
            .Path(WarningArt.CarFalling)
            .ToString();

        /// <summary>警23 村庄 (Fig. 171, E.141): a house with a window of four panes, a fir tree, the ground.</summary>
        private static void Village(Canvas f)
        {
            Draw(f, new PicBuilder().Mass(-10f, 48f, 32.1f, 48f, 48.1f, 61f, 43.5f, 61f, 43.5f, 91.8f, 100f, 91.8f, 100f, 110f, -10f, 110f).Path(WarningArt.FirTree).ToString());
            Draw(f, new PicBuilder().Rect(22.1f, 77.5f, 4.2f, 5.5f).Rect(27.9f, 77.5f, 4.2f, 5.5f).Rect(22.1f, 84f, 4.2f, 7.8f).Rect(27.9f, 84f, 4.2f, 7.8f).ToString(), false, GuideColour.Yellow);
        }

        /// <summary>警24 隧道 (Fig. 172, E.142): the portal (an arch band), the tunnel inside, its lit opening and the two edges of the road going in.</summary>
        private static void Tunnel(Canvas f)
        {
            var portal = new List<float>().ArcTo(49.7f, 66.4f, 22.1f, 180f, 0f, 24).To(71.8f, 96.6f, 66.3f, 96.6f).ArcTo(49.7f, 66.4f, 16.6f, 0f, 180f, 24).To(33.1f, 96.6f, 27.6f, 96.6f);
            var inside = new List<float>().ArcTo(49.7f, 66.4f, 15.2f, 180f, 0f, 24).To(64.9f, 96.6f, 34.5f, 96.6f);
            Draw(f, new PicBuilder().Poly(portal.ToArray()).Poly(inside.ToArray()).ToString());
            var opening = new List<float>().ArcTo(49.65f, 66.4f, 4.95f, 180f, 0f, 12).To(54.6f, 78f, 44.7f, 78f);
            Draw(f, new PicBuilder().Poly(opening.ToArray()).Poly(44.7f, 78f, 45.9f, 78f, 35.8f, 96.6f, 34.6f, 96.6f).Poly(53.4f, 78f, 54.6f, 78f, 64.8f, 96.6f, 63.6f, 96.6f).ToString(), false,
                GuideColour.Yellow);
        }

        /// <summary>警25 驼峰桥 (Fig. 173, E.143): the road rising over an arched bridge.</summary>
        private static readonly string kHumpBridge = new PicBuilder()
            .Poly(new List<float>().To(14.3f, 95.1f, 14.3f, 83.2f, 28.3f, 83.2f).ArcTo(50.15f, 83.25f, 14.5f, 138f, 42f, 16).To(72.3f, 83.2f, 85.9f, 83.2f, 85.9f, 95.1f, 58.3f, 95.1f)
                .ArcTo(50.15f, 83.25f, 8.15f, 0f, 180f, 16).To(42f, 95.1f).ToArray())
            .ToString();

        /// <summary>警26 路面不平 (Fig. 174, E.144): a block of road with two bumps and a dip between them.</summary>
        private static readonly string kUnevenRoad = new PicBuilder()
            .Poly(new List<float>().To(15.65f, 95.1f, 15.65f, 80.5f).ArcTo(32.2f, 85.7f, 13.1f, 157.7f, 40.3f, 14).ArcTo(51f, 69.74f, 11.56f, 220.3f, 319.7f, 10)
                .ArcTo(69.8f, 85.7f, 13.1f, 139.7f, 22.3f, 14).To(86.5f, 80.5f, 86.5f, 95.1f).ToArray())
            .ToString();

        /// <summary>警27 减速丘 (Fig. 175, E.145): a bar of road with one hump on it.</summary>
        private static readonly string kSpeedHump = new PicBuilder()
            .Poly(new List<float>().To(15.1f, 91.05f, 15.1f, 83.5f, 30.8f, 83.5f).ArcTo(50.7f, 98.8f, 25.1f, 142.5f, 37.5f, 18).To(70.6f, 83.5f, 86.8f, 83.5f, 86.8f, 91.05f).ToArray())
            .ToString();

        // ------------------------------------------------------------------ 警28 to 警36

        /// <summary>警28 过水路面 (Fig. 177, E.146): a car seen from the front driving through water on the road: the road as a block, the splashes either side.</summary>
        private static readonly string kFord = new PicBuilder()
            .Path(WarningArt.CarFront)
            .Poly(13.85f, 85f, 86.9f, 85f, 92.6f, 95f, 8.1f, 95f)
            .Stroke(1.6f, 23.1f, 79.9f, 23.6f, 76.8f, 26.2f, 75.3f, 29.3f, 75.9f, 32f, 78.6f)
            .Stroke(1.5f, 18.3f, 83f, 24f, 81.2f, 28f, 80.8f, 33.3f, 81.7f, 37.7f, 83.5f, 40f, 84.6f)
            .Stroke(1.4f, 43.5f, 82.8f, 47f, 83.6f, 51f, 83.8f, 55f, 83.3f, 58.1f, 83.2f)
            .Stroke(1.6f, 63f, 84.4f, 68.7f, 80.4f, 74.9f, 78.1f, 80.2f, 78.1f, 82.6f, 80.2f)
            .Stroke(1.4f, 73.1f, 82.1f, 78.5f, 81.7f, 82.9f, 83f, 85.1f, 84.3f)
            .ToString();

        /// <summary>警29 有人看守铁路道口 (Fig. 178, E.147): a gate of five pointed posts between two rails.</summary>
        private static readonly string kGuardedCrossing = Fence();

        private static string Fence()
        {
            var b = new PicBuilder().Rect(25.9f, 59.9f, 48.3f, 2.3f).Rect(25.9f, 88.8f, 48.3f, 3f);
            foreach (float x in new[] { 29f, 39.5f, 50f, 60.6f, 71f })
                b.Rect(x - 1.55f, 59.9f, 3.1f, 34.9f).Poly(x - 1.8f, 60.2f, x, 56.7f, x + 1.8f, 60.2f);
            return b.ToString();
        }

        /// <summary>
        /// 警31 叉形符号 (Fig. 180): the cross buck: two red-edged white bars crossed, and for several tracks a chevron of the same bars 3 cm below. Sizes of Fig. 180: 90 cm, 120 cm in
        /// brackets (taken for 100 to 120 km/h). In centimetres for the 90 cm size: the bars are 14 wide (a red edge of 4, white 6 in the middle), their axes run from (0, 5.7) to (90, 35.1)
        /// and back (18.1 degrees), their ends are points 6 deep that reach 3.2 beyond. The standard's board is the shape of the cross itself, which the framework cannot cut: it stands
        /// on a white rectangle round the figure.
        /// </summary>
        private static GuideBoard CrossBuck(GuideSpec s, int band)
        {
            float k = band == 0 ? 120f / 90f : 1f;
            bool many = s.Flag("tracks");
            float w = 98.9f * k, h = (many ? 61.7f : 43.8f) * k;
            return SignStyles.Rectangle(band, w / 100f, h / 100f, GuideColour.White, GuideColour.White, c =>
            {
                float ox = c.X * 100f, oy = c.Y * 100f, sx = 100f / (w - 2f * ox), sy = 100f / (h - 2f * oy);
                const float cos = 0.9505f, sin = 0.3107f, reach = 48.04f / cos;
                void Poly(GuideColour colour, List<float> cm)
                {
                    var p = new float[cm.Count];
                    for (int i = 0; i + 1 < cm.Count; i += 2)
                    {
                        // The figure's left point is at x = -3.04 and its top at y = -0.09: moved into the board with 1.4 cm of white round it.
                        p[i] = ((cm[i] + 4.44f) * k - ox) * sx;
                        p[i + 1] = ((cm[i + 1] + 1.49f) * k - oy) * sy;
                    }

                    c.Poly(colour, p);
                }

                // One arm from the crossing point (45, yc) out to its point, on one side (dir -1 left, 1 right) and down (down 1) or up (down -1): the corner on the far side of the axis
                // first, the point, the corner on the near side.
                void Arm(List<float> p, float yc, float dir, float down, float half, float inset, float depth, bool farFirst)
                {
                    float lx = dir * cos, ly = down * sin, nx = -ly, ny = lx;
                    if (ny * down < 0f)
                    {
                        nx = -nx;
                        ny = -ny;
                    }

                    float back = reach - inset - depth, tipAt = reach - inset;
                    float bx = 45f + lx * back, by = yc + ly * back;
                    float s1 = farFirst ? 1f : -1f;
                    p.To(bx + nx * half * s1, by + ny * half * s1, 45f + lx * tipAt, yc + ly * tipAt, bx - nx * half * s1, by - ny * half * s1);
                }

                foreach (GuideColour colour in new[] { GuideColour.Red, GuideColour.White })
                {
                    bool red = colour == GuideColour.Red;
                    float half = red ? 7f : 3f, inset = red ? 0f : 5.27f, depth = red ? 6f : 2.57f, notch = half / cos;
                    // The cross: two bars, each the hexagon of its two arms.
                    foreach (float slope in new[] { 1f, -1f })
                    {
                        var bar = new List<float>();
                        Arm(bar, 20.4f, 1f, slope, half, inset, depth, true);
                        Arm(bar, 20.4f, -1f, -slope, half, inset, depth, true);
                        Poly(colour, bar);
                    }

                    if (!many)
                        continue;
                    // The chevron: the lower arms of a cross 17.88 lower (3 cm clear of the cross), its apex and its notch on the middle.
                    float yc = 20.4f + 17.88f;
                    var chevron = new List<float>().To(45f, yc - notch);
                    Arm(chevron, yc, -1f, 1f, half, inset, depth, false);
                    chevron.To(45f, yc + notch);
                    Arm(chevron, yc, 1f, 1f, half, inset, depth, true);
                    Poly(colour, chevron);
                }
            }, 0f, 0f, 0f);
        }

        /// <summary>警32 斜杠符号 (Fig. 181): a white board 45 x 78 cm with one, two or three red bars rising to the right, 12 cm thick, rising 18 cm across the board.</summary>
        private static GuideBoard SlashBoard(GuideSpec s, int band)
        {
            int n = s.Is("bars", "3") ? 3 : s.Is("bars", "2") ? 2 : 1;
            // The tops of the bars at the right edge (Fig. 181 a to c): 24; 15 and 33; 6, 24 and 42.
            float[] tops = n == 1 ? new[] { 24f } : n == 2 ? new[] { 15f, 33f } : new[] { 6f, 24f, 42f };
            return SignStyles.Rectangle(band, 0.45f, 0.78f, GuideColour.White, GuideColour.White, c =>
            {
                float ox = c.X * 100f, oy = c.Y * 100f, sx = 100f / (45f - 2f * ox), sy = 100f / (78f - 2f * oy);
                foreach (float top in tops)
                {
                    float[] cm = { 0f, top + 18f, 45f, top, 45f, top + 12f, 0f, top + 30f };
                    var p = new float[8];
                    for (int i = 0; i < 8; i += 2)
                    {
                        p[i] = Math.Max(0.1f, Math.Min(99.9f, (cm[i] - ox) * sx));
                        p[i + 1] = (cm[i + 1] - oy) * sy;
                    }

                    c.Poly(GuideColour.Red, p);
                }
            }, 0f, 0f, 0f);
        }

        /// <summary>警33 事故易发路段 (Fig. 183, E.149): a car run into a tractor, the burst between them, an exclamation mark above.</summary>
        private static void Accident(Canvas f)
        {
            Draw(f, WarningArt.Accident);
            Draw(f, WarningArt.AccidentGaps, false, GuideColour.Yellow);
            Draw(f, WarningArt.AccidentWheels);
            Draw(f, WarningArt.AccidentWhite, false, GuideColour.White);
        }

        /// <summary>The obstacle of 警34 (Fig. 184): a narrow shield, square at the top and round at the bottom, its middle at u, its top at v.</summary>
        private static PicBuilder Obstacle(this PicBuilder b, float u, float v)
        {
            float[] dy = { 0f, 6.1f, 12.8f, 17.1f, 20.1f, 22.1f };
            float[] half = { 5.6f, 5.3f, 4.5f, 3.7f, 2.6f, 1.8f };
            var p = new List<float>();
            for (int i = 0; i < dy.Length; i++)
                p.To(u + half[i], v + dy[i]);
            p.ArcTo(u, v + 22.1f, 1.8f, 0f, -180f, 8);
            for (int i = dy.Length - 1; i >= 0; i--)
                p.To(u - half[i], v + dy[i]);
            return b.Poly(p.ToArray());
        }

        /// <summary>警34 注意障碍物 (Fig. 184, E.150 to E.152): an obstacle and the way round it: both sides (警34-1), on the left (警34-2) or on the right (警34-3).</summary>
        private static void Obstacle(GuideSpec s, Canvas f)
        {
            var b = new PicBuilder();
            if (s.Is("side", "both"))
            {
                b.Obstacle(50.4f, 33.9f).Rect(45.4f, 79.5f, 10.1f, 11.9f);
                foreach (float x in new[] { 36f, 64.8f })
                    b.Stroke(4.8f, 50.4f, 77.2f, x, 77.2f, x, 64f).Arrow45(x, 66f, x, 54.6f, 4.8f, 7.5f, 6f);
                Draw(f, b.ToString());
                return;
            }

            b.Obstacle(50.5f, 35.8f).Band(5f, 48f, 93.4f, 48f, 78.8f, 36.2f, 78.8f, 36.2f, 66f).Arrow45(36.2f, 68f, 36.2f, 56.6f, 5f, 7.5f, 6.5f);
            Draw(f, b.ToString(), s.Is("side", "r"));
        }

        /// <summary>警35 注意危险 (Fig. 185, E.153): an exclamation mark, a wedge round at the top, and the dot.</summary>
        private static readonly string kDanger = new PicBuilder()
            .Poly(new List<float>().ArcTo(50.2f, 46.35f, 6.85f, 195f, -15f, 16).To(50.2f, 81.9f).ToArray())
            .Circle(50.2f, 89.5f, 4.95f)
            .ToString();

        /// <summary>警36 施工 (Fig. 186, E.154): a man digging at a heap on the ground, black on orange.</summary>
        private static void Roadworks(Canvas f)
        {
            Draw(f, new PicBuilder()
                .Mass(-10f, 91f, 49f, 91f, 50.3f, 89.2f, 52.4f, 87.6f, 54.26f, 83.9f, 56.9f, 83f, 62.93f, 83.15f, 63.7f, 76.05f, 63.2f, 71.3f, 65.56f, 68.4f, 68.7f, 67.1f, 70.55f, 64f, 73.2f, 60.8f, 96f, 96f, 110f, 110f, -10f, 110f)
                .Path(WarningArt.Worker).ToString());
        }

        // ------------------------------------------------------------------ 警37 to 警41, 警44, 警45

        /// <summary>警37 交通事故管理 (Fig. 187, E.155): 事故 in two cells at the bottom of the field (Fig. 187: cells of 31 units from 63 down; here 29.6, the largest whose corners clear the edge).</summary>
        private static void AccidentManagement(Canvas f) => f.Cells("事故", K, 20.4f, 65.6f, 29.6f, 0f, GuideAlign.Left);

        /// <summary>警39 注意潮汐车道 (Fig. 190, E.157): an arrow with a head at both ends between two double dashed lines.</summary>
        private static string TidalLane()
        {
            var b = new PicBuilder().Arrow45(50.35f, 76f, 50.35f, 51.4f, 3.9f, 8.6f, 7.9f).Arrow45(50.35f, 73f, 50.35f, 97.8f, 3.9f, 8.6f, 7.9f);
            foreach (float u in new[] { 26.8f, 31.5f, 67.5f, 72.5f })
            {
                foreach (float v in new[] { 58.3f, 73.5f, 88.6f })
                    b.Rect(u, v, 1.75f, Math.Min(10f, 97.9f - v));
            }

            return b.ToString();
        }

        private static readonly string kTidalLane = TidalLane();

        /// <summary>警40 注意保持车距 (Fig. 191, E.158): a car ahead seen from behind, the lane's dashed edges in perspective, two chevrons on the road.</summary>
        private static string KeepDistance()
        {
            var b = new PicBuilder()
                .RoundRect(43.3f, 44.6f, 14.2f, 7f, 3f).RoundRect(42.3f, 50f, 16.2f, 8.1f, 1.5f)
                .Rect(44.7f, 47.5f, 11.4f, 2.9f, true)
                .RoundRect(42.8f, 57f, 2.2f, 3.6f, 1f).RoundRect(55.7f, 57f, 2.2f, 3.6f, 1f)
                .Poly(50.3f, 64.3f, 67f, 74f, 60.5f, 74f, 50.3f, 67.2f, 40.1f, 74f, 33.7f, 74f)
                .Poly(50.3f, 78.4f, 84.3f, 97.6f, 71.3f, 97.6f, 50.3f, 84.1f, 29.3f, 97.6f, 16.3f, 97.6f);
            float[] dashes = { 0f, 0.1f, 0.2f, 0.33f, 0.43f, 0.57f, 0.65f, 0.75f, 0.82f, 0.88f, 0.93f, 1f };
            foreach (float side in new[] { -1f, 1f })
            {
                float x0 = 50.3f + side * 46f, y0 = 97f, x1 = 50.3f + side * 10.8f, y1 = 57.6f;
                for (int i = 0; i + 1 < dashes.Length; i += 2)
                    b.Bar(x0 + (x1 - x0) * dashes[i], y0 + (y1 - y0) * dashes[i], x0 + (x1 - x0) * dashes[i + 1], y0 + (y1 - y0) * dashes[i + 1], 2.2f);
            }

            return b.ToString();
        }

        private static readonly string kKeepDistance = KeepDistance();

        /// <summary>警41 注意合流 (Fig. 192, E.159, E.160): the road ahead, an arrow, and a road joining it from the left (警41-1) or from the right (警41-2, mirrored).</summary>
        private static readonly string kMerge = new PicBuilder()
            .Arrow45(50.1f, 95.8f, 50.1f, 35.4f, 6.6f, 10.8f, 9.9f)
            .Band(3.6f, 29.6f, 84.5f, 41.5f, 77f, 45f, 73f, 47f, 68f)
            .ToString();

        private static readonly GuideChoice[] kWeathers =
        {
            C("ice", "注意路面结冰（警44-1）", "Ice (44-1)"),
            C("rain", "注意雨（雪）天（警44-2）", "Rain or snow (44-2)"),
            C("fog", "注意雾天（警44-3）", "Fog (44-3)"),
            C("storm", "注意不利气象条件（警44-4）", "Adverse weather (44-4)")
        };

        /// <summary>警44 (Fig. 197): the weather warnings of variable message signs, yellow on black with a yellow edge.</summary>
        private static void Weather(GuideSpec s, Canvas f)
        {
            const GuideColour Y = GuideColour.Yellow;
            switch (s.Get("kind"))
            {
                case "rain":
                    Draw(f, WarningArt.Cloud, false, Y);
                    Draw(f, WarningArt.Rain, false, Y);
                    break;
                case "fog":
                    // 雾 as in Fig. 197 c: its ink 39 units from 56 down; the cell of 41 units.
                    f.Cells("雾", Y, 29.4f, 54.4f, 41f, 0f, GuideAlign.Left);
                    break;
                case "storm":
                    Draw(f, WarningArt.Cloud, false, Y);
                    Draw(f, WarningArt.Lightning, false, Y);
                    break;
                default:
                    Draw(f, WarningArt.Snowflake, false, Y);
                    break;
            }
        }

        /// <summary>警45 注意前方车辆排队 (Fig. 198): three cars seen from behind, one behind the other, each with a black line round it where it stands in front of the one behind.</summary>
        private static void Queue(Canvas f)
        {
            const GuideColour Y = GuideColour.Yellow;
            Draw(f, WarningArt.QueueCar(-22.2f, -12.6f, 0f), false, Y);
            Draw(f, WarningArt.QueueCar(-11.4f, -6.5f, 1.2f), false, K);
            Draw(f, WarningArt.QueueCar(-11.4f, -6.5f, 0f), false, Y);
            Draw(f, WarningArt.QueueCar(0f, 0f, 1.2f), false, K);
            Draw(f, WarningArt.QueueCar(0f, 0f, 0f), false, Y);
        }

        // ------------------------------------------------------------------ the boards that are not triangles: 警38, 警42, 警43, 警46, 警47

        /// <summary>Table 7, the liner c of a sign (1.0, 0.8, 0.6, 0.4 cm by band); SignStyles keeps its copy private.</summary>
        private static readonly float[] kLiner = { 0.010f, 0.008f, 0.006f, 0.004f };

        /// <summary>The values of the "speed" setting for the bands 0 to 3.</summary>
        private static readonly string[] kBandIds = { "100", "80", "60", "30" };

        /// <summary>
        /// A rectangular board of w x h centimetres with rounded corners: a liner of the ground colour, a border, the ground again (the scheme of Fig. 3, with the widths given, in cm).
        /// The art gets the canvas (the inside of the border) and two functions that turn board centimetres (from the board's top left corner) into the canvas's units. The safe region is
        /// the inside of the border.
        /// </summary>
        private static GuideBoard Board(float w, float h, GuideColour ground, GuideColour border, float liner, float borderWidth, float radius, Action<Canvas, Func<float, float>, Func<float, float>> art)
        {
            var part = new GuidePart { X = 0f, Y = 0f, W = w / 100f, H = h / 100f, Shape = GuidePartShape.RoundRect, Ground = ground, BorderColour = border, Liner = liner / 100f, Border = borderWidth / 100f, Radius = radius / 100f };
            float edge = part.Edge;
            part.Safe = new[] { edge, edge, part.W - edge, edge, part.W - edge, part.H - edge, edge, part.H - edge };
            var c = new Canvas(part, edge, edge, part.W - 2f * edge, part.H - 2f * edge);
            art?.Invoke(c, cm => (cm / 100f - c.X) / c.W * 100f, cm => (cm / 100f - c.Y) / c.H * 100f);
            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        /// <summary>A path written in board centimetres turned into the canvas's units, every point kept a hair inside the canvas (a point on the edge of the safe region is outside it).</summary>
        private static string ToUnits(string cmPath, Func<float, float> u, Func<float, float> v)
        {
            var sb = new System.Text.StringBuilder();
            foreach (float[] contour in GuidePath.Parse(cmPath))
            {
                for (int i = 0; i + 1 < contour.Length; i += 2)
                {
                    float x = Math.Max(0.05f, Math.Min(99.95f, u(contour[i]))), y = Math.Max(0.05f, Math.Min(99.95f, v(contour[i + 1])));
                    sb.Append(i == 0 ? 'M' : 'L').Append(x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                        .Append(y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }

                sb.Append("Z ");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 警38 建议速度 (Fig. 188, E.156, Table 15): a yellow rectangle B x C (130 x 60, 110 x 50, 90 x 40, 70 x 30 cm by band) with a black border, the advised speed in digits of Table 15's
        /// height d (30, 25, 20, 15 cm) and "km/h" half as high (Table 15), sitting on one line. E.156: two digits 1.4 d wide (0.7 d a digit), 0.3 d, "km/h" 1.5 d wide; corner radius 0.4 d.
        /// The liner is Table 7's c, the border 0.1 d (Fig. 3's rule for boards with text; E.156 does not give it). The group is centred on the board.
        /// </summary>
        private static GuideBoard AdvisorySpeed(GuideSpec s, int band)
        {
            band = SignStyles.Band(band);
            float bw = new[] { 130f, 110f, 90f, 70f }[band], bh = new[] { 60f, 50f, 40f, 30f }[band], d = new[] { 30f, 25f, 20f, 15f }[band];
            string number = s.Get("advice");
            if (string.IsNullOrEmpty(number))
                number = "30";
            float numberW = 0.7f * d * Math.Max(2, number.Length), unitW = 1.5f * d, total = numberW + 0.3f * d + unitW;
            float x0 = (bw - total) * 0.5f, top = (bh - d) * 0.5f, ux = x0 + numberW + 0.3f * d;
            return Board(bw, bh, GuideColour.Yellow, GuideColour.Black, kLiner[band] * 100f, 0.1f * d, 0.4f * d, (c, u, v) =>
            {
                c.Ink(number, K, u(x0), v(top), u(x0 + numberW) - u(x0), v(top + d) - v(top), GuideAlign.Right);
                c.Ink("km/h", K, u(ux), v(top + 0.5f * d), u(ux + unitW) - u(ux), v(top + d) - v(top + 0.5f * d), GuideAlign.Left);
            });
        }

        /// <summary>
        /// 警42 注意车道数变少 (Fig. 193, E.161, E.162): the lanes as arrows 40 cm apart, the lane that ends bending into its neighbour with its dashed line, and the ground beyond the road
        /// (black, at the top on the side that ends). In cm, E.161 and E.162: 150 x 183 (two lanes) and 190 x 183 (three) under 80 km/h, 1.4 times that at 80 km/h and over (taken for the
        /// two fastest bands); liner 2.5, border 2.5, corner radius 10. The arrows: shafts 8.2 wide from 20 cm to 163 cm, heads 25 wide. The edge line of the ending lane leaves the last
        /// shaft 57.5 cm down at 47 degrees and runs down 40 cm right of it; the ground's edge leaves the top 30 cm right of the last shaft. The side is a setting: the standard prints the
        /// right lane ending; the left one is the figure mirrored.
        /// </summary>
        private static GuideBoard LanesReduce(GuideSpec s, int band)
        {
            int n = s.Is("lanes", "3") ? 3 : 2;
            float k = SignStyles.Band(band) <= 1 ? 1.4f : 1f, w = n == 3 ? 190f : 150f, h = 183f;
            return Board(w * k, h * k, GuideColour.Yellow, GuideColour.Black, 2.5f * k, 2.5f * k, 10f * k, (c, u0, v0) =>
            {
                const float dx = 0.676f, dy = 0.737f;
                float last = 35f + 40f * (n - 1);
                var p = new PicBuilder();
                for (int i = 0; i < n; i++)
                    p.Arrow45(35f + 40f * i, 163f, 35f + 40f * i, 20f, 8.2f, 12.5f, 11.4f);

                // The edge line of the ending lane: down and right from the last shaft, a curve, then straight down 40 cm right of the shaft.
                float ix = last + 40.3f, iy = 58.6f + 40.3f / dx * dy;
                var line = new List<float> { last, 58.6f };
                for (int i = 0; i <= 10; i++)
                {
                    float t = i / 10f, ax = ix - 16f * dx, ay = iy - 16f * dy, bx = ix, by = iy + 14f;
                    line.Add((1 - t) * (1 - t) * ax + 2 * t * (1 - t) * ix + t * t * bx);
                    line.Add((1 - t) * (1 - t) * ay + 2 * t * (1 - t) * iy + t * t * by);
                }

                line.Add(ix);
                line.Add(159f);
                p.Stroke(8.2f, line.ToArray()).Rect(ix - 4.1f, 158f, 8.2f, 5f);
                foreach (float y in new[] { 101.8f, 125.7f, 149.4f })
                    p.Rect(last + 20f - 1.7f, y, 3.4f, 13f);

                // The ground beyond the road: from 30 cm right of the last shaft at the top, down to 57.5, then parallel to the edge line into the board's right edge.
                float ex = w - 5f, zx = last + 30f, jy = 57.5f + (ex - zx) / dx * dy;
                var zone = new List<float> { w, 0f, zx, 0f, zx, 57.5f };
                for (int i = 0; i <= 10; i++)
                {
                    float t = i / 10f, ax = ex - 16f * dx, ay = jy - 16f * dy, bx = ex, by = jy + 15f;
                    zone.Add((1 - t) * (1 - t) * ax + 2 * t * (1 - t) * ex + t * t * bx);
                    zone.Add((1 - t) * (1 - t) * ay + 2 * t * (1 - t) * jy + t * t * by);
                }

                zone.Add(w);
                zone.Add(jy + 15f);
                p.Poly(zone.ToArray());
                Draw(c, ToUnits(p.ToString(), x => u0(x * k), y => v0(y * k)), s.Is("side", "l"));
            });
        }

        /// <summary>
        /// 警43 避险车道 (Fig. 195, E.163, E.164): the main road bending away to the left, the escape lane going straight on to its gravel bed (red and black), 避险车道 and an arrow. In the
        /// character height h of Table 3 (4.5.2): 43-1 upright 5.8 h x 8.8 h, 43-2 across 9.4 h x 5.9 h; the figure in a box 3 h x 4.6 h; the characters 1 h with 0.2 h between; the arrow in
        /// a square of 1 h; edge 0.2 h (Fig. 3), corner radius 0.4 h. The figure was measured on Fig. 195 a: the main road an arc band 0.8 h wide, the escape lane 0.35 h wide under the bed.
        /// side: the standard prints the lane on the right (arrow up right); on the left the figure is mirrored and the arrow points up left.
        /// </summary>
        private static GuideBoard EscapeLane(GuideSpec s, int band)
        {
            bool across = s.Is("form", "h"), left = s.Is("side", "l");
            float h = 100f * GuideTemplates.CharHeightOf(kBandIds[SignStyles.Band(band)]);
            float bw = (across ? 9.4f : 5.8f) * h, bh = (across ? 5.9f : 8.8f) * h;
            // Where the figure box, the characters and the arrow are (in h from the board's top left corner), for the form.
            float fx = across ? 0.6f : 1.4f, fy = 0.6f, tx = across ? 4.2f : 0.6f, ty = across ? 2.8f : 5.7f, ax = across ? 7.8f : 4.2f, ay = across ? 4.3f : 7.2f;
            if (left)
            {
                tx = (across ? 9.4f : 5.8f) - tx - 4.6f;
                ax = (across ? 9.4f : 5.8f) - ax - 1f;
            }

            return Board(bw, bh, GuideColour.Yellow, GuideColour.Black, 0.1f * h, 0.1f * h, 0.4f * h, (c, u, v) =>
            {
                // The figure in box units (h, from the box's top left), drawn for the lane on the right; for the left the whole canvas is mirrored (the board is symmetric).
                float X(float bx) => (fx + bx) * h;
                float Y(float by) => (fy + by) * h;
                var road = new PicBuilder();
                var arc = new List<float>().ArcTo(-2.07f, 4.09f, 4.75f, 0f, 57f, 20).ArcTo(-2.07f, 4.09f, 3.92f, 57f, 0f, 20);
                road.Poly(arc.ToArray()).Rect(1.85f, 4.05f, 0.83f, 0.58f).Rect(2.33f, 1.45f, 0.35f, 3.18f).Rect(2.1f, 0f, 0.9f, 1.5f);
                var red = new PicBuilder();
                foreach (float cx in new[] { 0.12f, 0.57f })
                {
                    foreach (float ry in new[] { 0.03f, 0.42f, 0.8f, 1.18f })
                        red.Rect(2.1f + cx, ry, 0.2f, 0.27f);
                }

                foreach (float ry in new[] { 0.22f, 0.61f, 1f })
                    red.Rect(2.1f + 0.34f, ry, 0.2f, 0.27f);
                Draw(c, ToUnits(road.ToString(), x => u(X(x)), y => v(Y(y))), left);
                Draw(c, ToUnits(red.ToString(), x => u(X(x)), y => v(Y(y))), left, GuideColour.Red);
                c.Cells("避险车道", K, u(tx * h), v(ty * h), v((ty + 1f) * h) - v(ty * h), v((ty + 0.2f) * h) - v(ty * h));
                c.Arrow(left ? GuideArrow.UpLeft : GuideArrow.UpRight, K, u(ax * h), v(ay * h), u((ax + 1f) * h) - u(ax * h));
            });
        }

        /// <summary>Table 16: A, B, C, D, E of a 线形诱导标 (mm): at 80 km/h and over, under 80, and the least.</summary>
        private static readonly float[][] kDelineator = { new[] { 600f, 800f, 260f, 360f, 40f }, new[] { 400f, 600f, 170f, 270f, 30f }, new[] { 220f, 400f, 90f, 180f, 20f } };

        private static readonly GuideChoice[] kDelineatorForms =
        {
            C("1", "警46-1 单个（弯道外侧）", "46-1 Single chevron (outside of a bend)"),
            C("2", "警46-2 多个同向（环岛等）", "46-2 Chevrons in a row (roundabouts)"),
            C("3", "警46-3 双向（T形交叉口）", "46-3 Both ways (T junction)"),
            C("4", "警46-4 竖向（分隔设施端部）", "46-4 Upright (end of a median)"),
            C("5", "红白斜纹竖牌（路口、障碍物）", "Red and white stripes (junction, obstacle)")
        };

        /// <summary>A chevron pointing left in mm: the tip E from the left, the arms to the top and bottom E from the edges, C across (Fig. 199 a); x0 is where its box starts.</summary>
        private static float[] ChevronPoints(float x0, float cc, float d, float e, bool right)
        {
            float top = e, mid = e + d, bottom = e + 2f * d;
            float[] p = { x0, mid, x0 + cc, top, x0 + 2f * cc, top, x0 + cc, mid, x0 + 2f * cc, bottom, x0 + cc, bottom };
            if (right)
            {
                for (int i = 0; i < p.Length; i += 2)
                    p[i] = 2f * x0 + 2f * cc - p[i];
            }

            return p;
        }

        /// <summary>
        /// The board and the chevrons of 警46's forms 1 to 3 (and of the blue chevron sign, MandatorySigns, which is the same figure on a blue board), in mm from the board's top left: its width and height, and
        /// the polygon of each chevron. The size is Table 16's for 80 km/h and over, under 80, or the least ("small").
        /// </summary>
        internal static float[][] ChevronRow(GuideSpec s, int band, out float w, out float h)
        {
            string form = s.Get("form");
            bool right = s.Is("dir", "r");
            float[] t = s.Flag("small") ? kDelineator[2] : SignStyles.Band(band) <= 1 ? kDelineator[0] : kDelineator[1];
            float a = t[0], bb = t[1], cc = t[2], d = t[3], e = t[4];
            int count = s.Is("count", "4") ? 4 : s.Is("count", "3") ? 3 : 2;
            float gap = form == "3" ? 0.2f * cc : 0f;
            int n = form == "1" ? 1 : form == "3" ? 4 : count;
            w = 2f * e + n * 2f * cc + gap;
            h = form == "1" ? bb : 2f * e + 2f * d;
            if (form == "1")
                w = a;
            var row = new float[n][];
            for (int i = 0; i < n; i++)
            {
                bool pointsRight = form == "3" ? i >= 2 : right;
                row[i] = ChevronPoints(e + 2f * cc * i + (form == "3" && i >= 2 ? gap : 0f), cc, d, (h - 2f * d) * 0.5f, pointsRight);
            }

            return row;
        }

        /// <summary>
        /// The blue chevron sign (城市弯道指示牌): 警46's forms 1 to 3, white chevrons on a blue ground with the white line of Fig. 3 inside the edge. The photographed boards have the proportions of
        /// Table 16's boards (0.75 for one chevron, 1.4 for two) and the standard of 2022 gives no blue one, so the board's size and the figure are 警46's and only the colours are the photo's.
        /// </summary>
        internal static GuideBoard BlueChevron(GuideSpec s, int band)
        {
            float[][] row = ChevronRow(s, band, out float w, out float h);
            float c = new[] { 1.0f, 0.8f, 0.6f, 0.4f }[SignStyles.Band(band)];
            return Board(w / 10f, h / 10f, GuideColour.Blue, GuideColour.White, c, c, 4f, (cv, u, v) =>
            {
                foreach (float[] chevron in row)
                {
                    var p = new PicBuilder();
                    p.Poly(chevron);
                    Draw(cv, ToUnits(p.ToString(), x => u(x / 10f), y => v(y / 10f)), false, GuideColour.White);
                }
            });
        }

        /// <summary>
        /// 警46 线形诱导标 (Fig. 199, Table 16, Table 18): yellow, black figures, no border, corners rounded 4 cm (4.6.6). 46-1: one chevron on A x B. 46-2 and 46-3: two to four chevrons
        /// side by side on one board, each C, D, E of Table 16 (the standard gives no size for these: the board is drawn round them as printed, 2 E plus 2 C a chevron, 46-3 with a gap
        /// of a fifth of C between its two halves). 46-4: the upright board F x G = 600 x 1200 of Table 18 with two chevrons pointing up (both sides) or stripes 300 wide at 45 degrees
        /// (one side). The size is Table 16's for 80 km/h and over, under 80, or the least ("small"). Form 5 (0.12.78; the owner's photo of a board of red and white stripes at 45 degrees, about 1 to 2.1 in the photo, 1 to 2 as 4's board is): the
        /// same board and stripes in red on white, the stripes the other half of the period (the photo has the red band starting in the top corner on the side to pass: 4's black starts at the other
        /// corner); the standard has no such board among 警46's, nothing here says it is one.
        /// </summary>
        private static GuideBoard Delineator(GuideSpec s, int band)
        {
            string form = s.Get("form");
            if (form == "4" || form == "5")
            {
                bool redWhite = form == "5";
                GuideColour ground = redWhite ? GuideColour.White : GuideColour.Yellow;
                return Board(60f, 120f, ground, ground, 0f, 0f, 4f, (c, u, v) =>
                {
                    var p = new PicBuilder();
                    if (s.Is("pass", "both"))
                    {
                        p.Poly(0f, 300f, 300f, 0f, 600f, 300f, 600f, 600f, 300f, 300f, 0f, 600f).Poly(0f, 900f, 300f, 600f, 600f, 900f, 600f, 1200f, 300f, 900f, 0f, 1200f);
                    }
                    else
                    {
                        // Stripes going down to the right, towards the side to pass (the second of Fig. 199 d): black where y - x (mm) is in [-600, -300], [0, 300], [600, 900]; form 5's red where it is in [-300, 0], [300, 600], [900, 1200].
                        foreach (float c0 in redWhite ? new[] { -300f, 300f, 900f } : new[] { -600f, 0f, 600f })
                            p.Poly(GuideKit.ClipToBox(new[] { -600f, -600f + c0, 1800f, 1800f + c0, 1800f, 2100f + c0, -600f, -300f + c0 }, 0f, 0f, 600f, 1200f));
                    }

                    Draw(c, ToUnits(p.ToString(), x => u(x / 10f), y => v(y / 10f)), s.Is("pass", "l"), redWhite ? GuideColour.Red : K);
                });
            }

            float[][] row = ChevronRow(s, band, out float w, out float h);
            return Board(w / 10f, h / 10f, GuideColour.Yellow, GuideColour.Yellow, 0f, 0f, 4f, (c, u, v) =>
            {
                var p = new PicBuilder();
                foreach (float[] chevron in row)
                    p.Poly(chevron);
                Draw(c, ToUnits(p.ToString(), x => u(x / 10f), y => v(y / 10f)));
            });
        }

        /// <summary>
        /// 警47 注意积水 (Fig. 201): a gauge 225 x 2150 mm, yellow, round at the top, its zero at the lowest point of the road: black marks on the right edge every 0.1 m (40 x 40 mm), larger
        /// at the half metres (60 x 55) and the metre (110 x 55), the depths 0.5, 1, 1.5 with the fraction small, and 米 at the top (measured on Fig. 201). The framework rounds all four
        /// corners alike, so the bottom is round as well (the printed one is square). It is 225 mm wide as the standard says (the thumbnail's outline used to fall between pixels at that
        /// width; Thumbnails now puts the first solid pixel on an even one). The radius is a hair under half the width (half exactly leaves a top edge of no length, whose triangles are
        /// degenerate).
        /// </summary>
        private static GuideBoard WaterGauge(GuideSpec s, int band)
        {
            const float w = 22.5f, h = 215f, r = 11.2f;
            return Board(w, h, GuideColour.Yellow, GuideColour.Yellow, 0f, 0f, r, (c, u, v) =>
            {
                var p = new PicBuilder();
                for (int k = 1; k <= 18; k++)
                {
                    float y = h - 10f * k;
                    float mw = k == 10 ? 11f : k % 5 == 0 ? 6f : 4f, mh = k % 5 == 0 ? 5.5f : 4f;
                    // The lowest mark is inside the round bottom corner: it keeps 0.6 cm from the right edge.
                    float right = k == 1 ? w - 0.6f : w - 0.02f;
                    p.Rect(right - mw, y - mh * 0.5f, mw, mh);
                }

                Draw(c, ToUnits(p.ToString(), u, v));
                // The depths: the whole metres 10.9 cm high, their right side at 8 cm, sitting on the bottom of their mark; the half ".5" 4 cm high after them.
                foreach (int k in new[] { 5, 10, 15 })
                {
                    float bottom = h - 10f * k + 2.75f;
                    c.Ink((k / 10).ToString(), K, u(1.5f), v(bottom - 10.9f), u(8f) - u(1.5f), v(bottom) - v(bottom - 10.9f), GuideAlign.Right);
                    if (k % 10 != 0)
                        c.Ink(".5", K, u(8.2f), v(bottom - 4f), u(12.8f) - u(8.2f), v(bottom) - v(bottom - 4f), GuideAlign.Left);
                }

                c.Cells("米", K, u(2.75f), v(11.5f), v(28.5f) - v(11.5f));
            });
        }

        // ------------------------------------------------------------------ the list

        public static IEnumerable<GuideTemplate> All()
        {
            // 警1 交叉路口 (7.2, Fig. 139 and 140): the eleven forms are one sign.
            yield return Tri("gb.warn.01", "警1", "交叉路口", "Intersection", "jclk", StandardSigns.Words("交叉", "路口", "十字", "T形", "Y形", "环岛", "crossroads"), Crossing,
                new[]
                {
                    GuideField.Choice("form", "形式", "Form", "1", kCrossings),
                    GuideField.Flag("narrow", "被交路较窄", "Crossing road narrower", false, s => !s.Is("form", "7") && !s.Is("form", "11"))
                });

            // 警2 急弯路 (7.3, Fig. 141): 警2-1 to the left, 警2-2 to the right.
            yield return Tri("gb.warn.02", "警2", "急弯路", "Sharp bend", "jwl", StandardSigns.Words("急弯", "弯道", "转弯", "bend", "curve"),
                (s, f) => Draw(f, kSharpBend, Right(s)), new[] { LeftRight("向左急弯（警2-1）", "向右急弯（警2-2）", "To the left (2-1)", "To the right (2-2)") });

            // 警3 反向弯路 (7.4, Fig. 143).
            yield return Tri("gb.warn.03", "警3", "反向弯路", "Reverse bend", "fxwl", StandardSigns.Words("反向", "弯路", "S弯", "reverse", "bend"),
                (s, f) => Draw(f, kReverseBend, Right(s)), new[] { LeftRight("先向左（警3-1）", "先向右（警3-2）", "Left first (3-1)", "Right first (3-2)") });

            // 警4 连续弯路 (7.5, Fig. 146).
            yield return Tri("gb.warn.04", "警4", "连续弯路", "Winding road", "lxwl", StandardSigns.Words("连续", "弯路", "winding", "bends"),
                (s, f) => Draw(f, kWindingRoad, Right(s)), new[] { LeftRight("警4-1", "警4-2", "4-1", "4-2") });

            // 警5 陡坡 (7.6, Fig. 149, 150).
            yield return Tri("gb.warn.05", "警5", "陡坡", "Steep hill", "dp", StandardSigns.Words("陡坡", "上坡", "下坡", "坡度", "steep", "hill"), Slope,
                new[]
                {
                    GuideField.Choice("dir", "方向", "Direction", "up", new[] { C("up", "上陡坡（警5-1）", "Steep ascent (5-1)"), C("down", "下陡坡（警5-2）", "Steep descent (5-2)") }),
                    GuideField.Digits("grade", "坡度值（%，可空）", "Grade (%, may be empty)", "", 2)
                });

            // 警6 连续下坡 (7.7, Fig. 151).
            yield return Tri("gb.warn.06", "警6", "连续下坡", "Successive downhill slopes", "lxxp", StandardSigns.Words("连续", "下坡", "长下坡", "downhill"), f => Draw(f, kDownhills));

            // 警7 窄路 (7.8, Fig. 152).
            yield return Tri("gb.warn.07", "警7", "窄路", "Road narrows", "zl", StandardSigns.Words("窄路", "变窄", "narrow"), Narrows,
                new[] { GuideField.Choice("side", "变窄的一侧", "Side that narrows", "both", new[] { C("both", "两侧变窄（警7-1）", "Both sides (7-1)"), C("r", "右侧变窄（警7-2）", "Right side (7-2)"), C("l", "左侧变窄（警7-3）", "Left side (7-3)") }) });

            // 警8 窄桥 (7.9, Fig. 155).
            yield return Tri("gb.warn.08", "警8", "窄桥", "Narrow bridge", "zq", StandardSigns.Words("窄桥", "桥", "narrow", "bridge"), f => Draw(f, kNarrowBridge));

            // 警9 双向交通 (7.10, Fig. 156).
            yield return Tri("gb.warn.09", "警9", "双向交通", "Two-way traffic", "sxjt", StandardSigns.Words("双向", "会车", "two-way"), f => Draw(f, kTwoWay));

            // 警10 注意行人 (7.11, Fig. 157): the ground "可" be fluorescent yellow-green (Table 2: yellow (fluorescent yellow-green)); yellow by default.
            yield return TriPeople("gb.warn.10", "警10", "注意行人", "Pedestrians", "zyxr", StandardSigns.Words("行人", "人行横道", "斑马线", "pedestrian", "crossing"), Pedestrians);

            // 警11 注意儿童 (7.12, Fig. 158): the ground "宜" be fluorescent yellow-green (the standard's stronger word than 警10's and 警12's "可"); yellow by default, as it was.
            yield return TriPeople("gb.warn.11", "警11", "注意儿童", "Children", "zyet", StandardSigns.Words("儿童", "学校", "小学", "children", "school"), (f, ground) => Draw(f, WarningArt.Children));

            // 警12 注意残疾人 (7.13, Fig. 160): "可" be fluorescent yellow-green; yellow by default.
            yield return TriPeople("gb.warn.12", "警12", "注意残疾人", "Disabled persons", "zycjr", StandardSigns.Words("残疾人", "轮椅", "无障碍", "disabled", "wheelchair"), (f, ground) => Draw(f, WarningArt.Wheelchair));

            // 警13 注意非机动车 (7.14, Fig. 161).
            yield return Tri("gb.warn.13", "警13", "注意非机动车", "Non-motor vehicles", "zyfjdc", StandardSigns.Words("非机动车", "自行车", "单车", "bicycle", "cyclists"), f => Draw(f, WarningArt.Bicycle));

            // 警14 注意电动自行车 (7.15, Fig. 162).
            yield return Tri("gb.warn.14", "警14", "注意电动自行车", "Electric bicycles", "zyddzxc", StandardSigns.Words("电动自行车", "电动车", "电瓶车", "e-bike"), f => Draw(f, WarningArt.EBike));

            // 警15 注意牲畜 (7.16, Fig. 163): the standard lets the animal be the one most often met; the printed one, a cow, is drawn.
            yield return Tri("gb.warn.15", "警15", "注意牲畜", "Cattle", "zysc", StandardSigns.Words("牲畜", "牛", "放牧", "cattle", "livestock"), f => Draw(f, WarningArt.Cow));

            // 警16 注意野生动物 (7.17, Fig. 164): likewise; the printed deer.
            yield return Tri("gb.warn.16", "警16", "注意野生动物", "Wild animals", "zyysdw", StandardSigns.Words("野生动物", "鹿", "动物", "wild", "animals", "deer"), f => Draw(f, WarningArt.Deer));

            // 警17 注意信号灯 (7.18, Fig. 165).
            yield return Tri("gb.warn.17", "警17", "注意信号灯", "Traffic signals", "zyxhd", StandardSigns.Words("信号灯", "红绿灯", "traffic", "lights", "signals"), Signals);

            // 警18 注意落石 (7.19, Fig. 166): the side the rocks fall from.
            yield return Tri("gb.warn.18", "警18", "注意落石", "Falling rocks", "zyls", StandardSigns.Words("落石", "塌方", "山体", "falling", "rocks"),
                (s, f) => Draw(f, kRockfall, Right(s)), new[] { LeftRight("左侧落石（警18-1）", "右侧落石（警18-2）", "From the left (18-1)", "From the right (18-2)") });

            // 警19 注意横风 (7.20, Fig. 167).
            yield return Tri("gb.warn.19", "警19", "注意横风", "Crosswind", "zyhf", StandardSigns.Words("横风", "侧风", "大风", "crosswind", "wind"), f =>
            {
                Draw(f, WarningArt.Windsock);
                Draw(f, WarningArt.SockLight, false, GuideColour.Yellow);
            });

            // 警20 易滑 (7.21, Fig. 168).
            yield return Tri("gb.warn.20", "警20", "易滑", "Slippery road", "yh", StandardSigns.Words("易滑", "路滑", "slippery"), f => Draw(f, kSlippery));

            // 警21 傍山险路 (7.22, Fig. 169): the side the mountain is on.
            yield return Tri("gb.warn.21", "警21", "傍山险路", "Mountain road", "bsxl", StandardSigns.Words("傍山", "险路", "悬崖", "山路", "mountain", "cliff"),
                (s, f) => Draw(f, kMountainRoad, Right(s)), new[] { LeftRight("左侧傍山险路（警21-1）", "右侧傍山险路（警21-2）", "Mountain on the left (21-1)", "Mountain on the right (21-2)") });

            // 警22 堤坝路 (7.23, Fig. 170): the side the water is on.
            yield return Tri("gb.warn.22", "警22", "堤坝路", "Embankment", "dbl", StandardSigns.Words("堤坝", "水库", "河堤", "embankment", "water"),
                (s, f) => Draw(f, kEmbankment, s.Is("water", "l")),
                new[] { GuideField.Choice("water", "水在哪侧", "Water on the", "r", new[] { C("r", "水在右侧（警22-1）", "Right (22-1)"), C("l", "水在左侧（警22-2）", "Left (22-2)") }) });

            // 警23 村庄 (7.24, Fig. 171).
            yield return Tri("gb.warn.23", "警23", "村庄", "Village", "cz", StandardSigns.Words("村庄", "集镇", "村", "village"), Village);

            // 警24 隧道 (7.25, Fig. 172).
            yield return Tri("gb.warn.24", "警24", "隧道", "Tunnel", "sd", StandardSigns.Words("隧道", "tunnel"), Tunnel);

            // 警25 驼峰桥 (7.26, Fig. 173).
            yield return Tri("gb.warn.25", "警25", "驼峰桥", "Hump bridge", "tfq", StandardSigns.Words("驼峰桥", "拱桥", "hump", "bridge"), f => Draw(f, kHumpBridge));

            // 警26 路面不平 (7.27, Fig. 174).
            yield return Tri("gb.warn.26", "警26", "路面不平", "Uneven road", "lmbp", StandardSigns.Words("路面不平", "颠簸", "坑洼", "uneven", "bumpy"), f => Draw(f, kUnevenRoad));

            // 警27 减速丘 (7.28, Fig. 175).
            yield return Tri("gb.warn.27", "警27", "减速丘", "Speed hump", "jsq", StandardSigns.Words("减速丘", "减速带", "speed", "hump", "bump"), f => Draw(f, kSpeedHump));

            // 警28 过水路面（或漫水桥） (7.29, Fig. 177).
            yield return Tri("gb.warn.28", "警28", "过水路面（或漫水桥）", "Ford", "gslm", StandardSigns.Words("过水路面", "漫水桥", "涉水", "ford", "water"), f => Draw(f, kFord));

            // 警29 有人看守铁路道口 (7.30, Fig. 178).
            yield return Tri("gb.warn.29", "警29", "有人看守铁路道口", "Level crossing with barriers", "yrkstldk", StandardSigns.Words("铁路道口", "有人看守", "栏杆", "level crossing", "railway"),
                f => Draw(f, kGuardedCrossing));

            // 警30 无人看守铁路道口 (7.30, Fig. 179).
            yield return Tri("gb.warn.30", "警30", "无人看守铁路道口", "Level crossing without barriers", "wrkstldk", StandardSigns.Words("铁路道口", "无人看守", "火车", "level crossing", "train"),
                f => Draw(f, WarningArt.Locomotive));

            // 警31 叉形符号 (7.30, Fig. 180): over a level crossing sign where several tracks cross the road (the second chevron).
            yield return StandardSigns.Make("gb.warn.31", "警31", "叉形符号", "Cross buck", G, "cxfh", StandardSigns.Words("叉形符号", "铁路", "道口", "cross buck", "railway"), CrossBuck,
                new[] { GuideField.Flag("tracks", "多股铁路（加下方人字）", "Several tracks (second chevron)", true) });

            // 警32 斜杠符号 (7.30, Fig. 181): under the 无人看守铁路道口 sign, 150, 100 and 50 m before the crossing (three, two, one bars).
            yield return StandardSigns.Make("gb.warn.32", "警32", "斜杠符号", "Countdown marker (level crossing)", G, "xgfh", StandardSigns.Words("斜杠符号", "铁路", "道口", "距离", "countdown"),
                SlashBoard,
                new[] { GuideField.Choice("bars", "斜杠数", "Bars", "1", new[] { C("1", "一道（距道口 50 m）", "One (50 m)"), C("2", "二道（100 m）", "Two (100 m)"), C("3", "三道（150 m）", "Three (150 m)") }) });

            // 警33 事故易发路段 (7.31, Fig. 183).
            yield return Tri("gb.warn.33", "警33", "事故易发路段", "Accident blackspot", "sgyfld", StandardSigns.Words("事故", "易发", "事故多发", "accident", "blackspot"), Accident);

            // 警34 注意障碍物 (7.32, Fig. 184).
            yield return Tri("gb.warn.34", "警34", "注意障碍物", "Obstacle", "zyzaw", StandardSigns.Words("障碍物", "绕行", "obstacle", "detour"), Obstacle,
                new[] { GuideField.Choice("side", "绕行", "Pass", "both", new[] { C("both", "左右绕行（警34-1）", "Either side (34-1)"), C("l", "左侧绕行（警34-2）", "On the left (34-2)"), C("r", "右侧绕行（警34-3）", "On the right (34-3)") }) });

            // 警35 注意危险 (7.33, Fig. 185).
            yield return Tri("gb.warn.35", "警35", "注意危险", "Danger", "zywx", StandardSigns.Words("危险", "感叹号", "danger"), f => Draw(f, kDanger));

            // 警36 施工 (7.34, Fig. 186): orange ground (the fluorescent orange the standard also allows is not among the colours).
            yield return Tri("gb.warn.36", "警36", "施工", "Road works", "sg", StandardSigns.Words("施工", "道路施工", "roadworks", "construction"), (s, f) => Roadworks(f), null, GuideColour.Orange);

            // 警37 交通事故管理: amendment 1 (effective 2026-01-01), 7.35 and Fig. 187, fluorescent fire red.
            yield return Tri("gb.warn.37", "警37", "交通事故管理", "Traffic accident management", "jtsggl", StandardSigns.Words("事故", "事故管理", "交通事故", "accident"),
                (s, f) => AccidentManagement(f), null, GuideColour.FluorescentFireRed);

            // 警38 建议速度 (7.36, Fig. 188, Table 15): the advised speed is a setting.
            yield return StandardSigns.Make("gb.warn.38", "警38", "建议速度", "Advisory speed", G, "jysd", StandardSigns.Words("建议速度", "速度", "弯道", "匝道", "advisory", "speed"), AdvisorySpeed,
                new[] { GuideField.Digits("advice", "建议速度（km/h）", "Advised speed (km/h)", "30", 3) });

            // 警39 注意潮汐车道 (7.37, Fig. 190).
            yield return Tri("gb.warn.39", "警39", "注意潮汐车道", "Tidal lane", "zycxcd", StandardSigns.Words("潮汐车道", "可变车道", "tidal", "reversible lane"), f => Draw(f, kTidalLane));

            // 警40 注意保持车距 (7.38, Fig. 191).
            yield return Tri("gb.warn.40", "警40", "注意保持车距", "Keep your distance", "zybccj", StandardSigns.Words("车距", "保持车距", "distance", "headway"), f => Draw(f, kKeepDistance));

            // 警41 注意合流 (7.39, Fig. 192): the side the other road joins from.
            yield return Tri("gb.warn.41", "警41", "注意合流", "Merging traffic", "zyhl", StandardSigns.Words("合流", "汇合", "汇入", "merge"),
                (s, f) => Draw(f, kMerge, Right(s)), new[] { LeftRight("从左侧汇入（警41-1）", "从右侧汇入（警41-2）", "From the left (41-1)", "From the right (41-2)") });

            // 警42 注意车道数变少 (7.40, Fig. 193).
            yield return StandardSigns.Make("gb.warn.42", "警42", "注意车道数变少", "Lanes reduce", G, "zycdsbs", StandardSigns.Words("车道数变少", "车道减少", "并道", "lane", "ends"), LanesReduce,
                new[]
                {
                    GuideField.Choice("lanes", "车道数", "Lanes", "2", new[] { C("2", "两车道（警42-1）", "Two (42-1)"), C("3", "三车道（警42-2）", "Three (42-2)") }),
                    GuideField.Choice("side", "减少的车道", "Lane that ends", "r", new[] { C("r", "右侧车道", "Right lane"), C("l", "左侧车道", "Left lane") })
                });

            // 警43 避险车道 (7.41, Fig. 195).
            yield return StandardSigns.Make("gb.warn.43", "警43", "避险车道", "Escape lane", G, "bxcd", StandardSigns.Words("避险车道", "避险", "紧急", "escape", "runaway"), EscapeLane,
                new[]
                {
                    GuideField.Choice("form", "版面", "Layout", "v", new[] { C("v", "竖版（警43-1）", "Upright (43-1)"), C("h", "横版（警43-2）", "Across (43-2)") }),
                    GuideField.Choice("side", "避险车道在", "Escape lane on the", "r", new[] { C("r", "右侧", "Right"), C("l", "左侧", "Left") })
                });

            // 警44 注意路面结冰、注意雨（雪）天、注意雾天、注意不利气象条件 (7.42, Fig. 197): for variable message signs; one sign with the four figures.
            yield return Tri("gb.warn.44", "警44", "注意不利气象条件", "Adverse weather", "zybliqxtj", StandardSigns.Words("结冰", "雨雪", "雾", "天气", "ice", "fog", "weather"), Weather,
                new[] { GuideField.Choice("kind", "天气", "Weather", "ice", kWeathers) }, GuideColour.Black, GuideColour.Yellow);

            // 警45 注意前方车辆排队 (7.43, Fig. 198): for variable message signs.
            yield return Tri("gb.warn.45", "警45", "注意前方车辆排队", "Queues ahead", "zyqfclpd", StandardSigns.Words("排队", "拥堵", "车辆排队", "queue", "congestion"), (s, f) => Queue(f), null,
                GuideColour.Black, GuideColour.Yellow);

            // 警46 线形诱导标 (7.44, Fig. 199, Table 16 and 18).
            yield return StandardSigns.Make("gb.warn.46", "警46", "线形诱导标", "Chevron alignment sign", G, "xxydb", StandardSigns.Words("线形诱导标", "诱导标", "弯道", "环岛", "红白", "斜纹", "路口", "障碍物", "chevron", "delineator", "stripes"),
                Delineator,
                new[]
                {
                    GuideField.Choice("form", "形式", "Form", "1", kDelineatorForms),
                    GuideField.Choice("dir", "箭头指向", "Points", "l", new[] { C("l", "向左", "Left"), C("r", "向右", "Right") }, s => s.Is("form", "1") || s.Is("form", "2")),
                    GuideField.Choice("count", "箭头个数", "Chevrons", "3", new[] { C("2", "2 个", "2"), C("3", "3 个", "3"), C("4", "4 个", "4") }, s => s.Is("form", "2")),
                    GuideField.Choice("pass", "通行方向", "Pass on", "both", new[] { C("both", "两侧通行（人字）", "Both sides (chevrons)"), C("r", "右侧通行（斜纹）", "The right (stripes)"), C("l", "左侧通行（斜纹）", "The left (stripes)") },
                        s => s.Is("form", "4") || s.Is("form", "5")),
                    GuideField.Flag("small", "最小尺寸（表 16）", "Least size (Table 16)", false, s => !s.Is("form", "4") && !s.Is("form", "5"))
                }).AsLinear(s => !s.Is("form", "4") && !s.Is("form", "5"));

            // 警47 注意积水 (7.45, Fig. 201): one size.
            yield return StandardSigns.Make("gb.warn.47", "警47", "注意积水", "Water depth gauge", G, "zyjs", StandardSigns.Words("积水", "水深", "下穿", "水位", "flood", "water depth"), WaterGauge, null);
        }
    }
}
