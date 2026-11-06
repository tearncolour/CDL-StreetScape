using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static CDLC.TS.Geo.GuideKit;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The guide signs of ordinary roads (GB 5768.2-2022 chapter 8, 路1 to 路28: blue boards with the road diagram, stacked and lane boards, road numbers and names, place names, the
    /// facility signs, detours, the tunnel exit distance, the direction mark, the kilometre plate) and the expressway information and facility signs that are not exit boards (9.4 and
    /// 9.5, 路55 to 路77: green boards). Boards with text are laid out in the character height h of Table 3 (65, 55, 40, 30 cm for the four bands of design speed), with the gaps and margins
    /// of Table 6 and Fig. 3 and the arrangement of the figures and of the drawings of Appendix E (E.166 to E.192, E.222 to E.244); the facility squares have the sizes in cm the drawings give.
    /// An information that is typed as a road number (G105, S203, X008, G15, G1511, 皖S30) is drawn as its plate (8.1.5); a name that ends in 高速 or 快速路 on a blue board as a green plate
    /// with a white name (8.1.5).
    /// </summary>
    public static partial class RoadGuideSigns
    {
        private const string G = "guide";
        private const GuideColour Blue = GuideColour.Blue, Green = GuideColour.Green, White = GuideColour.White, Black = GuideColour.Black, Red = GuideColour.Red, Yellow = GuideColour.Yellow;

        // ------------------------------------------------------------------ sizes

        private static readonly string[] kBandIds = { "100", "80", "60", "30" };

        /// <summary>The character height h of Table 3 for a band (0 to 3).</summary>
        internal static float CharHeight(int band) => GuideTemplates.CharHeightOf(kBandIds[SignStyles.Band(band)]);

        // ------------------------------------------------------------------ boards

        /// <summary>A guide board with an inside of innerW x innerH: liner and border 0.1 h each, outer radius 0.4 h (Fig. 3); what is drawn as a figure must stay inside (the Safe region).</summary>
        internal static GuidePart Board(float h, float innerW, float innerH, GuideColour ground, GuideColour border = White)
        {
            GuidePart p = Part(h, innerW, innerH, ground);
            p.BorderColour = border;
            SetSafe(p);
            return p;
        }

        internal static void SetSafe(GuidePart p)
        {
            float e = p.Edge - 0.0005f;
            p.Safe = new[] { e, e, p.W - e, e, p.W - e, p.H - e, e, p.H - e };
        }

        internal static GuideBoard Single(GuidePart p, float h)
        {
            var b = new GuideBoard { H = h };
            b.Parts.Add(p);
            return b;
        }

        /// <summary>
        /// A board given in cm by the drawings (the facility squares of E.176 to E.188 and 9.5): w x h, liner and border each e (the "4, (3)" and "5, (4)" of the drawings, a twentieth of
        /// the height), corner radius 3 e, read off the drawings.
        /// </summary>
        internal static GuidePart Plain(float w, float h, float e, GuideColour ground, GuideColour border = White)
        {
            var p = new GuidePart { W = w, H = h, Ground = ground, BorderColour = border, Liner = e, Border = e, Radius = 3f * e };
            SetSafe(p);
            return p;
        }

        /// <summary>The size of a facility square: the drawings give two, the larger for 40 km/h and above, the one in brackets below.</summary>
        internal static float Pick(int band, float normal, float small) => SignStyles.Band(band) == 3 ? small : normal;

        // ------------------------------------------------------------------ a scene: things placed freely, then a board round them

        private sealed class Scene
        {
            public readonly List<GuideElement> Items = new List<GuideElement>();

            public void Add(GuideElement e)
            {
                if (e != null)
                    Items.Add(e);
            }

            public void Add(GuideBlock b, float x, float y)
            {
                if (b == null)
                    return;
                foreach (GuideElement e in b.Elements)
                    Items.Add(e.Moved(x, y));
            }

            public void Bounds(out float x0, out float y0, out float x1, out float y1)
            {
                x0 = y0 = float.MaxValue;
                x1 = y1 = float.MinValue;
                foreach (GuideElement e in Items)
                {
                    x0 = Math.Min(x0, e.X);
                    y0 = Math.Min(y0, e.Y);
                    x1 = Math.Max(x1, e.Right);
                    y1 = Math.Max(y1, e.Bottom);
                }

                if (Items.Count == 0)
                    x0 = y0 = x1 = y1 = 0f;
            }
        }

        /// <summary>
        /// The board round a scene: the inside is the scene's box with the margins (left, top, right, bottom), at least minW x minH (the scene centred in what is left over). keepOut: the side of a
        /// square in the inside's top left corner that must stay free (the direction mark), 0 for none; what would come into it is moved down.
        /// </summary>
        private static GuidePart Fit(Scene s, float h, GuideColour ground, float ml, float mt, float mr, float mb, float minW = 0f, float minH = 0f, float keepOut = 0f, GuideColour border = White)
        {
            s.Bounds(out float x0, out float y0, out float x1, out float y1);
            float cw = x1 - x0 + ml + mr, ch = y1 - y0 + mt + mb;
            float innerW = Math.Max(minW, cw);
            float extraX = (innerW - cw) * 0.5f;
            if (keepOut > 0f)
            {
                float need = 0f;
                foreach (GuideElement e in s.Items)
                {
                    float ex = e.X - x0 + ml + extraX, ey = e.Y - y0 + mt;
                    if (ex < keepOut + 0.1f * h && ey < keepOut + 0.1f * h)
                        need = Math.Max(need, keepOut + 0.15f * h - ey);
                }

                mt += need;
                ch += need;
            }

            float innerH = Math.Max(minH, ch);
            GuidePart p = Board(h, innerW, innerH, ground, border);
            float dx = p.Edge + ml + extraX - x0, dy = p.Edge + mt + (innerH - ch) * 0.5f - y0;
            foreach (GuideElement e in s.Items)
                p.Elements.Add(e.Moved(dx, dy));
            return p;
        }

        // ------------------------------------------------------------------ text

        internal static bool Wide(char c) => c >= 0x2E80;

        /// <summary>A line of text: Chinese characters in square cells h high with the gap between, letters and digits as ink h high (Table 4), the runs 0.15 h apart.</summary>
        internal static GuideBlock Line(string text, float h, GuideColour colour, float gap)
        {
            text = (text ?? "").Trim();
            var block = new GuideBlock(0f, h);
            float x = 0f;
            bool any = false;
            int i = 0;
            while (i < text.Length)
            {
                int j = i;
                bool wide = Wide(text[i]);
                while (j < text.Length && Wide(text[j]) == wide)
                    j++;
                string run = text.Substring(i, j - i);
                i = j;
                if (!wide)
                    run = run.Trim();
                if (run.Length == 0)
                    continue;
                if (any)
                    x += 0.15f * h;
                any = true;
                if (wide)
                {
                    block.Add(Cells(run, x, 0f, h, colour, gap));
                    x += CellWidth(run.Length, h, gap);
                }
                else
                {
                    float w = LatinWidth(run, h);
                    block.Add(Ink(run, x, 0f, w, h, colour));
                    x += w;
                }
            }

            block.W = x;
            if (!any)
                block.H = 0f;
            return Bilingual.Under(block, text, h, colour);
        }

        /// <summary>Blocks side by side, gap apart, centred on the tallest; empty ones are left out.</summary>
        private static GuideBlock Row(float gap, params GuideBlock[] blocks)
        {
            float w = 0f, hh = 0f;
            int n = 0;
            foreach (GuideBlock b in blocks)
            {
                if (b == null || b.W <= 0f)
                    continue;
                w += (n > 0 ? gap : 0f) + b.W;
                hh = Math.Max(hh, b.H);
                n++;
            }

            var row = new GuideBlock(w, hh);
            float x = 0f;
            n = 0;
            foreach (GuideBlock b in blocks)
            {
                if (b == null || b.W <= 0f)
                    continue;
                if (n++ > 0)
                    x += gap;
                row.Add(b, x, (hh - b.H) * 0.5f);
                x += b.W;
            }

            return row;
        }

        /// <summary>Blocks one under the other, gap apart, aligned (0 left, 0.5 centre, 1 right); empty ones are left out.</summary>
        private static GuideBlock Column(float gap, float align, params GuideBlock[] blocks)
        {
            float w = 0f, hh = 0f;
            int n = 0;
            foreach (GuideBlock b in blocks)
            {
                if (b == null || b.W <= 0f)
                    continue;
                hh += (n > 0 ? gap : 0f) + b.H;
                w = Math.Max(w, b.W);
                n++;
            }

            var col = new GuideBlock(w, hh);
            float y = 0f;
            n = 0;
            foreach (GuideBlock b in blocks)
            {
                if (b == null || b.W <= 0f)
                    continue;
                if (n++ > 0)
                    y += gap;
                col.Add(b, (w - b.W) * align, y);
                y += b.H;
            }

            return col;
        }

        // ------------------------------------------------------------------ information: a place, a road number, an unnumbered expressway

        private static readonly Regex kCode = new Regex(@"^([⺀-鿿]?)([GSXYZCgsxyzc])([0-9]{1,4})$");

        /// <summary>
        /// One information of a guide board, h high: a place or road name in characters (Line); a road number as its plate (8.1.5: G and three digits a national road, red; S and three
        /// digits a provincial road, yellow; X, Y, Z, C a county, township, special or village road, white; G or S with one, two or four digits a national or provincial expressway, the plate of
        /// Appendix C, the last two of four digits small; a Chinese character before the S is the province of the strip); on a blue board a name ending in 高速, 快速 or 快速路 is an unnumbered
        /// expressway or urban expressway: a green plate with the white name (8.1.5).
        /// </summary>
        internal static GuideBlock Info(string text, float h, GuideColour ground, GuideColour ink = White)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return new GuideBlock(0f, 0f);
            // A pictogram after the place (Fig. 207 b, the metro after 学府路): written as the place and the symbol's name in brackets, 学府路[地铁].
            Match tag = kTag.Match(text);
            if (tag.Success && SymbolGlyph(tag.Groups[2].Value) != null)
            {
                GuideBlock icon = SymbolPlate(SymbolGlyph(tag.Groups[2].Value), h, ground);
                string before = tag.Groups[1].Value.Trim();
                return before.Length == 0 ? icon : Row(SymbolGapK * h, Info(before, h, ground, ink), icon);
            }

            Match m = kCode.Match(text);
            if (m.Success)
                return CodePlate(m.Groups[1].Value, char.ToUpperInvariant(m.Groups[2].Value[0]), m.Groups[3].Value, h);
            if (ground == Blue && text.Length >= 3 && (text.EndsWith("高速") || text.EndsWith("快速路") || text.EndsWith("快速")))
                return NamePlate(text, h, Green, White);
            return Line(text, h, ink, NameGapK * h);
        }

        /// <summary>A place followed by the name of a pictogram in brackets (half or full width).</summary>
        private static readonly Regex kTag = new Regex(@"^(.*?)[\[［【]\s*([^\]］】]+?)\s*[\]］】]$");

        /// <summary>The gap between a place and the pictogram after it (Fig. 207 b: about 0.3 h).</summary>
        internal const float SymbolGapK = 0.3f;

        /// <summary>The names of the pictograms that may follow a place, and their glyphs (those of 路12 and 路13).</summary>
        internal static readonly string[] SymbolNames = { "地铁", "轨道交通", "机场", "飞机场", "停车场", "停车", "加油站", "加油", "充电站", "充电", "轮渡", "渡口" };

        internal static string SymbolGlyph(string name)
        {
            switch ((name ?? "").Trim())
            {
                case "地铁":
                case "轨道交通":
                    return Glyphs.R_Metro;
                case "机场":
                case "飞机场":
                    return Glyphs.R_Airplane;
                case "停车场":
                case "停车":
                    return Glyphs.R_P;
                case "加油站":
                case "加油":
                    return Glyphs.R_Fuel;
                case "充电站":
                case "充电":
                    return Glyphs.R_Charger;
                case "轮渡":
                case "渡口":
                    return Glyphs.R_Ferry;
                default:
                    return null;
            }
        }

        /// <summary>The pictogram after a place (Fig. 207 b): a white square h a side with rounded corners, the figure in the board's colour inside it, 0.1 h from its sides.</summary>
        private static GuideBlock SymbolPlate(string glyph, float h, GuideColour ground)
        {
            var block = new GuideBlock(h, h);
            block.Elements.Add(Rect(0f, 0f, h, h, White, 0.12f * h));
            block.Elements.Add(GuideKit.FitPicture(glyph, 0.1f * h, 0.1f * h, 0.8f * h, 0.8f * h, ground));
            return block;
        }

        /// <summary>The expressway plate on a guide board (table C.1): 100 cm high at 100 to 120 and 71 to 99 km/h, 70 cm below.</summary>
        internal static float ExpresswayPlateSize(float h) => h >= 0.55f - 1e-4f ? 1.00f : 0.70f;

        internal static GuideBlock CodePlate(string province, char letter, string digits, float h)
        {
            if ((letter == 'G' || letter == 'S') && digits.Length != 3)
            {
                string code = letter + (digits.Length == 4 ? digits.Substring(0, 2) : digits);
                string sub = digits.Length == 4 ? digits.Substring(2) : "";
                return RoadPlate(letter == 'G' ? RoadPlateKind.NationalExpressway : RoadPlateKind.ProvincialExpressway, code, sub, province.Length > 0 ? province : "省", ExpresswayPlateSize(h));
            }

            return OrdinaryPlate(letter + digits, letter, 1.2f * h);
        }

        /// <summary>
        /// The number plate of an ordinary road on a board (Fig. 210, 213 a, E.168, E.174: 1.2 h high, 2.6 h long for four characters): the code in letters 0.6 of the height, a quarter of the
        /// height at each end; G red with a white edge, S yellow and X, Y, Z, C white with a black line inside a narrow liner of the plate's colour.
        /// </summary>
        private static GuideBlock OrdinaryPlate(string code, char letter, float P)
        {
            float cap = 0.6f * P, pad = 0.25f * P;
            float w = LatinWidth(code, cap) + 2f * pad;
            var b = new GuideBlock(w, P);
            GuideColour fill = letter == 'G' ? Red : letter == 'S' ? Yellow : White;
            GuideColour ink = letter == 'G' ? White : Black;
            GuideElement body = Rect(0f, 0f, w, P, fill, 0.15f * P);
            if (letter == 'G')
            {
                body.StrokeWidth = 0.05f * P;
                body.Stroke = White;
                b.Add(body);
            }
            else
            {
                b.Add(body);
                float inset = 0.04f * P;
                GuideElement frame = Rect(inset, inset, w - 2f * inset, P - 2f * inset, fill, 0.15f * P - inset);
                frame.StrokeWidth = 0.05f * P;
                frame.Stroke = Black;
                b.Add(frame);
            }

            b.Add(Ink(code, pad, (P - cap) * 0.5f, w - 2f * pad, cap, ink, GuideAlign.Centre));
            return b;
        }

        /// <summary>A name on a plate of another colour (8.1.5: an unnumbered expressway in white on green): 1.4 h high, the name h high, 0.3 h at the ends, a white edge.</summary>
        internal static GuideBlock NamePlate(string text, float h, GuideColour fill, GuideColour ink)
        {
            GuideBlock line;
            using (Bilingual.Suspend())
                line = Line(text, h, ink, NameGapK * h);
            float P = 1.4f * h, w = line.W + 0.6f * h;
            var b = new GuideBlock(w, P);
            GuideElement body = Rect(0f, 0f, w, P, fill, 0.2f * h);
            body.StrokeWidth = 0.06f * h;
            body.Stroke = White;
            b.Add(body);
            b.Add(line, 0.3f * h, 0.2f * h);
            // An English line goes under the plate, on the board.
            return Bilingual.Under(b, text, h, White);
        }

        // ------------------------------------------------------------------ arrows (Fig. 204, E.166)

        private static float AngleOf(GuideArrow dir)
        {
            switch (dir)
            {
                case GuideArrow.UpRight: return -45f;
                case GuideArrow.Up: return -90f;
                case GuideArrow.UpLeft: return -135f;
                case GuideArrow.Left: return 180f;
                case GuideArrow.Down: return 90f;
                default: return 0f;
            }
        }

        /// <summary>
        /// The arrow of Fig. E.166 a to c pointing to the right, its middle at the origin: a stem 0.28 a thick and a head a wide and a long: the tip has 81 degrees (its sides reach the head's
        /// full width 0.58 a behind the tip), the head's sides then run straight back 0.42 a, and its back runs from there to the stem parallel to its front; length from the tail to the tip.
        /// </summary>
        private static float[] ArrowShape(float a, float length)
        {
            float t = 0.14f * a, x1 = length * 0.5f, x0 = -x1;
            float join = x1 - a + 0.58f * a * (1f - 2f * t / a);
            return new[]
            {
                x1, 0f, x1 - 0.58f * a, -0.5f * a, x1 - a, -0.5f * a, join, -t, x0, -t,
                x0, t, join, t, x1 - a, 0.5f * a, x1 - 0.58f * a, 0.5f * a
            };
        }

        private static float[] Turn(float[] p, float degrees, float cx, float cy, float scale = 1f)
        {
            double r = degrees * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            var q = new float[p.Length];
            for (int i = 0; i + 1 < p.Length; i += 2)
            {
                q[i] = cx + scale * (p[i] * c - p[i + 1] * s);
                q[i + 1] = cy + scale * (p[i] * s + p[i + 1] * c);
            }

            return q;
        }

        /// <summary>
        /// An arrow of E.166 in a box: a horizontal one as long as the box is wide and a head as high as the box, a vertical one the other way round, a diagonal one (45 degrees, 8.1.8) as large
        /// as the box's smaller side allows. A polygon of seven corners (the tests keep texts and arrows apart).
        /// </summary>
        internal static GuideElement ArrowIn(GuideArrow dir, float x, float y, float w, float hh, GuideColour colour)
        {
            float a, length;
            switch (dir)
            {
                case GuideArrow.Left:
                case GuideArrow.Right:
                    a = hh;
                    length = w;
                    break;
                case GuideArrow.Up:
                case GuideArrow.Down:
                    a = w;
                    length = hh;
                    break;
                default:
                    a = 1f;
                    length = 1.9f;
                    break;
            }

            float[] p = Turn(ArrowShape(a, length), AngleOf(dir), 0f, 0f);
            Box(p, out float bx0, out float by0, out float bx1, out float by1);
            float k = Math.Min(w / Math.Max(1e-6f, bx1 - bx0), hh / Math.Max(1e-6f, by1 - by0));
            if (dir == GuideArrow.Left || dir == GuideArrow.Right || dir == GuideArrow.Up || dir == GuideArrow.Down)
                k = Math.Min(1f, k);
            var q = new float[p.Length];
            float cx = (bx0 + bx1) * 0.5f, cy = (by0 + by1) * 0.5f;
            for (int i = 0; i + 1 < p.Length; i += 2)
            {
                q[i] = x + w * 0.5f + (p[i] - cx) * k;
                q[i + 1] = y + hh * 0.5f + (p[i + 1] - cy) * k;
            }

            return Poly(q, colour);
        }

        /// <summary>A block holding one arrow in a box w x hh.</summary>
        private static GuideBlock ArrowBox(GuideArrow dir, float w, float hh, GuideColour colour)
        {
            var b = new GuideBlock(w, hh);
            b.Add(ArrowIn(dir, 0f, 0f, w, hh, colour));
            return b;
        }

        private static void Box(float[] p, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue;
            x1 = y1 = float.MinValue;
            for (int i = 0; i + 1 < p.Length; i += 2)
            {
                x0 = Math.Min(x0, p[i]);
                x1 = Math.Max(x1, p[i]);
                y0 = Math.Min(y0, p[i + 1]);
                y1 = Math.Max(y1, p[i + 1]);
            }
        }

        // ------------------------------------------------------------------ figures drawn from contours (the road diagrams)

        /// <summary>A contour wound as a solid (counter-clockwise on the screen) or as a hole, so that the solids of a figure join and the holes cut (non-zero winding, as the painter fills).</summary>
        private static float[] Wound(float[] c, bool hole = false)
        {
            float a = 0f;
            int n = c.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += c[i * 2] * c[j * 2 + 1] - c[j * 2] * c[i * 2 + 1];
            }

            bool reverse = hole ? a < 0f : a > 0f;
            if (!reverse)
                return c;
            var r = new float[c.Length];
            for (int k = 0; k < n; k++)
            {
                r[k * 2] = c[(n - 1 - k) * 2];
                r[k * 2 + 1] = c[(n - 1 - k) * 2 + 1];
            }

            return r;
        }

        /// <summary>A figure of contours (solids and holes, already wound) as one element of the sign's figure: inside the board (the tests check it).</summary>
        private static GuideElement Figure(List<float[]> contours, GuideColour colour)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (float[] c in contours)
            {
                Box(c, out float a, out float b, out float cc, out float d);
                x0 = Math.Min(x0, a);
                y0 = Math.Min(y0, b);
                x1 = Math.Max(x1, cc);
                y1 = Math.Max(y1, d);
            }

            return new GuideElement { Kind = GuideElementKind.Path, Contours = contours, Colour = colour, X = x0, Y = y0, W = x1 - x0, H = y1 - y0, Art = true };
        }

        private static float[] RectContour(float x, float y, float w, float hh) => new[] { x, y, x, y + hh, x + w, y + hh, x + w, y };

        private static float[] CircleContour(float cx, float cy, float r, int n = 40)
        {
            var c = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                double a = -2.0 * Math.PI * i / n;
                c[i * 2] = cx + r * (float)Math.Cos(a);
                c[i * 2 + 1] = cy + r * (float)Math.Sin(a);
            }

            return c;
        }

        /// <summary>
        /// A road of a junction diagram (Fig. 205, E.167, E.168): from (x0, y0) along the unit direction (dx, dy), w wide; pointed: **the road keeps its width and ends in a right-angled point** (a
        /// gable: the two sides bevelled 45 degrees over half the width, the point on the road's axis), as every road end of E.167 a, b, c and E.168 is drawn (measured on the enlarged drawings:
        /// the point is as far forward as the road is half wide); otherwise square. Until 0.5.9 the end flared out by a quarter of the width before it pointed (a kite), which the standard's figures
        /// do not have (the owner pointed at it on 2026-10-19).
        /// </summary>
        private static float[] Arm(float x0, float y0, float dx, float dy, float length, float w, bool pointed)
        {
            float nx = -dy, ny = dx, hw = w * 0.5f;
            var pts = new List<float>();
            void P(float u, float v)
            {
                pts.Add(x0 + dx * u + nx * v);
                pts.Add(y0 + dy * u + ny * v);
            }

            if (!pointed)
            {
                P(0f, hw);
                P(length, hw);
                P(length, -hw);
                P(0f, -hw);
            }
            else
            {
                float b = Math.Max(0f, length - hw);
                P(0f, hw);
                P(b, hw);
                P(length, 0f);
                P(b, -hw);
                P(0f, -hw);
            }

            return Wound(pts.ToArray());
        }

        /// <summary>The rounded fill of the inner corner between two roads: the corner, and the directions u and v of the two road edges away from it.</summary>
        private static float[] Fillet(float cx, float cy, float ux, float uy, float vx, float vy, float r)
        {
            var pts = new List<float> { cx, cy };
            float ox = cx + r * (ux + vx), oy = cy + r * (uy + vy);
            const int n = 8;
            for (int k = 0; k <= n; k++)
            {
                double t = Math.PI * 0.5 * k / n;
                float c = (float)Math.Cos(t), s = (float)Math.Sin(t);
                pts.Add(ox - r * (vx * c + ux * s));
                pts.Add(oy - r * (vy * c + uy * s));
            }

            return Wound(pts.ToArray());
        }

        /// <summary>A convex polygon moved inward by d (each side parallel to itself): the inner edge of an outline.</summary>
        private static float[] Inset(float[] p, float d)
        {
            float[] c = Wound(p);
            int n = c.Length / 2;
            var lines = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ex = c[j * 2] - c[i * 2], ey = c[j * 2 + 1] - c[i * 2 + 1];
                float len = (float)Math.Sqrt(ex * ex + ey * ey);
                // Wound solid (negative area with y down): the inside is on the left of the direction of travel in a y-down frame, that is the normal (ey, -ex).
                float nx = ey / len, ny = -ex / len;
                lines[i * 3] = nx;
                lines[i * 3 + 1] = ny;
                lines[i * 3 + 2] = nx * c[i * 2] + ny * c[i * 2 + 1] + d;
            }

            var r = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                int k = (i + n - 1) % n;
                float a1 = lines[k * 3], b1 = lines[k * 3 + 1], c1 = lines[k * 3 + 2];
                float a2 = lines[i * 3], b2 = lines[i * 3 + 1], c2 = lines[i * 3 + 2];
                float det = a1 * b2 - a2 * b1;
                r[i * 2] = (c1 * b2 - c2 * b1) / det;
                r[i * 2 + 1] = (a1 * c2 - a2 * c1) / det;
            }

            return r;
        }

        /// <summary>A thick line along a polyline (the stems of the turning arrows): each vertex moved by half the width along the mean of its sides' normals.</summary>
        private static float[] Thick(List<float> line, float w)
        {
            int n = line.Count / 2;
            var left = new List<float>();
            var right = new List<float>();
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                float dx = line[b * 2] - line[a * 2], dy = line[b * 2 + 1] - line[a * 2 + 1];
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                float nx = -dy / len * w * 0.5f, ny = dx / len * w * 0.5f;
                left.Add(line[i * 2] + nx);
                left.Add(line[i * 2 + 1] + ny);
                right.Add(line[i * 2] - nx);
                right.Add(line[i * 2 + 1] - ny);
            }

            var pts = new List<float>(left);
            for (int i = n - 1; i >= 0; i--)
            {
                pts.Add(right[i * 2]);
                pts.Add(right[i * 2 + 1]);
            }

            return Wound(pts.ToArray());
        }

        /// <summary>The head of an E.166 arrow alone (the chevron, joined to a stem t thick), pointing along the angle from the tip (tx, ty).</summary>
        private static float[] Head(float tx, float ty, float degrees, float a, float t)
        {
            float join = -a + 0.58f * a * (1f - t / a);
            float[] p = { 0f, 0f, -0.58f * a, -0.5f * a, -a, -0.5f * a, join, -t * 0.5f, join - 0.05f * a, -t * 0.5f, join - 0.05f * a, t * 0.5f, join, t * 0.5f, -a, 0.5f * a, -0.58f * a, 0.5f * a };
            return Wound(Turn(p, degrees, tx, ty));
        }

        // ------------------------------------------------------------------ the direction mark in a board (8.6.4, Fig. 242, E.191)

        private static readonly GuideChoice[] kDirs =
        {
            new GuideChoice("", "不设", "None"),
            new GuideChoice("东", "东", "East"),
            new GuideChoice("南", "南", "South"),
            new GuideChoice("西", "西", "West"),
            new GuideChoice("北", "北", "North")
        };

        private static GuideField DirField(string def) => GuideField.Choice("dir", "方向标志（左上角）", "Direction mark (top left)", def, kDirs);

        private const float DirBoxK = 0.9f;

        /// <summary>The direction mark in the inside's top left corner (Fig. 202, 205 c: white, the character in the ground's colour, 0.65 h: 4.5.4 d asks for at least half of Table 3).</summary>
        private static void AddDirMark(GuidePart p, float h, string dir)
        {
            if (string.IsNullOrEmpty(dir))
                return;
            float e = p.Edge, s = DirBoxK * h, r = p.InnerRadius;
            var pts = new List<float> { e + s, e, e + s, e + s, e, e + s };
            const int n = 8;
            for (int k = 0; k <= n; k++)
            {
                double a = Math.PI + Math.PI * 0.5 * k / n;
                pts.Add(e + r + r * (float)Math.Cos(a));
                pts.Add(e + r + r * (float)Math.Sin(a));
            }

            p.Elements.Add(Poly(pts.ToArray(), White));
            float c = 0.65f * h;
            p.Elements.Add(Cells(dir, e + (s - c) * 0.5f, e + (s - c) * 0.5f, c, p.Ground, 0f));
        }

        // ------------------------------------------------------------------ fields

        /// <summary>A place on a route board: a name, a road number, or a name with a pictogram after it in brackets (学府路[地铁]); 10 characters at the most.</summary>
        internal static GuideField Place(string key, string zh, string en, string def) => GuideField.Text(key, zh, en, def, 10);

        private static readonly GuideChoice[] kUnits =
        {
            new GuideChoice("km", "km", "km"),
            new GuideChoice("m", "m", "m")
        };

        // ================================================================== 8.3 route guidance

        // ------------------------------------------------------------------ 路1 交叉路口预告（图形式） Fig. 202, 205; E.167

        private static readonly GuideChoice[] kJunctions =
        {
            new GuideChoice("x", "十字路口", "Crossroads"),
            new GuideChoice("t", "丁字路口（左、右）", "T junction (left and right)"),
            new GuideChoice("r", "直行与右转（├）", "Ahead and right (├)"),
            new GuideChoice("l", "直行与左转（┤）", "Ahead and left (┤)")
        };

        /// <summary>
        /// 路1 and 路5's form at a junction (Fig. 205, E.167): the junction drawn as roads 0.4 h wide with pointed ends, the informations straight ahead in a row above the upward road (two at most,
        /// 0.6 h apart), those to the left and to the right at the ends of the side roads (two at most, one under the other: 8.1.4 c, Fig. 202), 0.3 h from the points. With the name of the crossing
        /// road (Fig. 205 d, E.167 d) the junction is drawn with arrows and the name in a white hexagon on the crossing road, 0.6 h high (Fig. 203 note 4: 0.5 h to 0.7 h).
        /// </summary>
        private static GuideBoard Junction(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string form = s.Get("form");
            bool up = form != "t", left = form != "r", right = form != "l";
            string xroad = s.Get("xroad");
            bool named = xroad.Length > 0;
            var scene = new Scene();
            var roads = new List<float[]>();

            float tipUp, tipL, tipR, half;
            if (!named)
            {
                float w = 0.4f * h, hw = w * 0.5f;
                half = 0.3f * h;
                tipUp = -1.3f * h;
                tipL = left ? -1.5f * h : -hw;
                tipR = right ? 1.5f * h : hw;
                if (up) roads.Add(Arm(0f, 0f, 0f, -1f, -tipUp, w, true));
                if (left) roads.Add(Arm(0f, 0f, -1f, 0f, -tipL, w, true));
                if (right) roads.Add(Arm(0f, 0f, 1f, 0f, tipR, w, true));
                float r = 0.2f * h;
                if (up && left) roads.Add(Fillet(-hw, -hw, 0f, -1f, -1f, 0f, r));
                if (up && right) roads.Add(Fillet(hw, -hw, 0f, -1f, 1f, 0f, r));
                if (left) roads.Add(Fillet(-hw, hw, 0f, 1f, -1f, 0f, r));
                if (right) roads.Add(Fillet(hw, hw, 0f, 1f, 1f, 0f, r));
            }
            else
            {
                float lh = 0.6f * h, lgap = 0.1f * h;
                float tw = CellWidth(xroad.Length, lh, lgap);
                float hx = tw * 0.5f + 0.2f * h, hy = 0.5f * h, tip = hx + 0.4f * h;
                float[] hex = Wound(new[] { -tip, 0f, -hx, hy, hx, hy, tip, 0f, hx, -hy, -hx, -hy });
                roads.Add(hex);
                roads.Add(Wound(Inset(hex, 0.1f * h), true));
                half = hy;
                float a = 0.9f * h;
                tipUp = -hy - 2.2f * h;
                tipL = left ? -tip - 1.6f * h : -tip;
                tipR = right ? tip + 1.6f * h : tip;
                if (up) scene.Add(ArrowIn(GuideArrow.Up, -a * 0.5f, tipUp, a, -hy - tipUp + 0.05f * h, White));
                if (left) scene.Add(ArrowIn(GuideArrow.Left, tipL, -a * 0.5f, -tip - tipL + 0.05f * h, a, White));
                if (right) scene.Add(ArrowIn(GuideArrow.Right, tip - 0.05f * h, -a * 0.5f, tipR - tip + 0.05f * h, a, White));
                scene.Add(Cells(xroad, -tw * 0.5f, -lh * 0.5f, lh, White, lgap));
            }

            // The informations.
            GuideBlock ahead = up ? Row(0.6f * h, Info(s.Get("to1"), h, Blue), Info(s.Get("to2"), h, Blue)) : null;
            GuideBlock lcol = left ? Column(0.3f * h, 1f, Info(s.Get("tl1"), h, Blue), Info(s.Get("tl2"), h, Blue)) : null;
            GuideBlock rcol = right ? Column(0.3f * h, 0f, Info(s.Get("tr1"), h, Blue), Info(s.Get("tr2"), h, Blue)) : null;
            if (ahead != null && ahead.W > 0f)
            {
                // The names at the sides are taller with an English line under each, and reach up beside the names ahead: those stand over them.
                float aheadBottom = tipUp - 0.3f * h;
                if (Bilingual.Active)
                    aheadBottom = Math.Min(aheadBottom, -0.5f * Math.Max(lcol != null ? lcol.H : 0f, rcol != null ? rcol.H : 0f) - 0.2f * h);
                scene.Add(ahead, -ahead.W * 0.5f, aheadBottom - ahead.H);
            }

            if (lcol != null && lcol.W > 0f)
                scene.Add(lcol, tipL - 0.3f * h - lcol.W, -lcol.H * 0.5f);
            if (rcol != null && rcol.W > 0f)
                scene.Add(rcol, tipR + 0.3f * h, -rcol.H * 0.5f);

            // The road it comes from, down to the lowest information.
            float low = Math.Max(named ? half + 0.7f * h : 1.0f * h, Math.Max(lcol != null ? lcol.H * 0.5f : 0f, rcol != null ? rcol.H * 0.5f : 0f));
            if (named)
                roads.Add(Wound(RectContour(-0.125f * h, half - 0.05f * h, 0.25f * h, low - half + 0.05f * h)));
            else
                roads.Add(Arm(0f, 0f, 0f, 1f, low, 0.4f * h, false));
            scene.Items.Insert(0, Figure(roads, White));

            string dir = s.Get("dir");
            GuidePart p = Fit(scene, h, Blue, 0.6f * h, 0.4f * h, 0.6f * h, 0.4f * h, 0f, 0f, dir.Length > 0 ? DirBoxK * h : 0f);
            AddDirMark(p, h, dir);
            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路2 环岛（图形式） Fig. 206; E.168

        /// <summary>
        /// 路2 (Fig. 206, E.168): the roundabout as a white ring with a blue island and its roads 0.4 h wide; the informations of the roads up to the left and up to the right above them at the
        /// sides, those to the left and to the right under the side roads (8.3.2.2: not above them). A road is drawn when it has an information.
        /// </summary>
        private static GuideBoard Roundabout(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            float w = 0.4f * h, ro = 0.6f * h, ri = 0.3f * h;
            var scene = new Scene();
            var roads = new List<float[]> { Wound(CircleContour(0f, 0f, ro)), Wound(CircleContour(0f, 0f, ri), true) };
            GuideBlock nw = Column(0.3f * h, 1f, Info(s.Get("tnw1"), h, Blue), Info(s.Get("tnw2"), h, Blue));
            GuideBlock ne = Column(0.3f * h, 0f, Info(s.Get("tne1"), h, Blue), Info(s.Get("tne2"), h, Blue));
            GuideBlock wl = Column(0.3f * h, 1f, Info(s.Get("tw1"), h, Blue), Info(s.Get("tw2"), h, Blue));
            GuideBlock el = Column(0.3f * h, 0f, Info(s.Get("te1"), h, Blue), Info(s.Get("te2"), h, Blue));
            float start = ri + 0.1f * h;
            float dx = 0.5f, dy = -0.866f, lenD = 1.9f * h, lenS = 2.2f * h;
            // All the roads of the roundabout are drawn, whether they carry an information or not (Fig. 206).
            roads.Add(Arm(-dx * start, dy * start, -dx, dy, lenD - start, w, true));
            roads.Add(Arm(dx * start, dy * start, dx, dy, lenD - start, w, true));
            roads.Add(Arm(-start, 0f, -1f, 0f, lenS - start, w, true));
            roads.Add(Arm(start, 0f, 1f, 0f, lenS - start, w, true));
            float low = Math.Max(2.2f * h, 0.5f * h + Math.Max(wl.H, el.H) + 0.2f * h);
            roads.Add(Arm(0f, start, 0f, 1f, low - start, w, false));
            scene.Add(Figure(roads, White));

            float topY = dy * lenD;
            if (nw.W > 0f) scene.Add(nw, -dx * lenD - 0.5f * h - nw.W, Math.Min(topY, -0.5f * h - nw.H));
            if (ne.W > 0f) scene.Add(ne, dx * lenD + 0.5f * h, Math.Min(topY, -0.5f * h - ne.H));
            if (wl.W > 0f) scene.Add(wl, Math.Min(-0.6f * h - wl.W, -lenS + 0.6f * h), 0.5f * h);
            if (el.W > 0f) scene.Add(el, Math.Max(0.6f * h, lenS - 0.6f * h - el.W), 0.5f * h);

            string dir = s.Get("dir");
            GuidePart p = Fit(scene, h, Blue, 0.6f * h, 0.5f * h, 0.6f * h, 0.4f * h, 0f, 0f, dir.Length > 0 ? DirBoxK * h : 0f);
            AddDirMark(p, h, dir);
            return Single(p, h);
        }

        // ------------------------------------------------------------------ Fig. 203 c: a roundabout with the numbers of the crossing road on its roads

        /// <summary>
        /// The name of a tourist site on a blue guide board (8.1.5: a brown ground with white characters, Fig. 203 c shows it as a rounded box). No drawing gives its size: read off the figure,
        /// 1.6 h high, the characters h high 0.1 h apart, 0.3 h from the ends of the box.
        /// </summary>
        internal static GuideBlock TouristName(string text, float h)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return new GuideBlock(0f, 0f);
            GuideBlock line;
            using (Bilingual.Suspend())
                line = Line(text, h, White, 0.1f * h);
            float w = line.W + 0.6f * h, hh = 1.6f * h;
            var b = new GuideBlock(w, hh);
            b.Add(Rect(0f, 0f, w, hh, GuideColour.Brown, 0.25f * h));
            b.Add(line, 0.3f * h, (hh - line.H) * 0.5f);
            return Bilingual.Under(b, text, h, White);
        }

        /// <summary>
        /// What stands on the road of the crossing road (8.1.7 b: its number or its name): a number as the small plate of its class (the figure shows S209 on a plate 0.75 of the plates at the ends);
        /// a name in blue characters 0.6 h high on a small white box, so that it can be read over the white road.
        /// </summary>
        private static GuideBlock ShaftLabel(string text, float h)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return new GuideBlock(0f, 0f);
            if (kCode.IsMatch(text))
                return Info(text, 0.75f * h, Blue);
            GuideBlock line;
            using (Bilingual.Suspend())
                line = Line(text, 0.6f * h, Blue, 0.1f * h);
            float w = line.W + 0.4f * h, hh = 0.9f * h;
            var b = new GuideBlock(w, hh);
            b.Add(Rect(0f, 0f, w, hh, White, 0.15f * h));
            b.Add(line, 0.2f * h, (hh - line.H) * 0.5f);
            return b;
        }

        /// <summary>
        /// Fig. 203 c, the example of 8.1.7 b: the junction drawn as a roundabout (a ring with an island and its roads, as 路2), the number or name of the crossing road on the two side roads and the
        /// informations they lead to at their ends, the information straight ahead above the road going up (here a tourist site, brown), the road it comes from below. The figure is an example of
        /// the meaning of the informations and has no dimensioned drawing: the proportions are read off it (the ring 0.55 h in radius with an island of 0.2 h, the roads 0.3 h wide, the plates on
        /// the roads 0.05 h from the ring, the point 0.3 h beyond them, the informations 0.65 h from the points) and are estimates.
        /// </summary>
        private static GuideBoard RingJunction(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            float w = 0.3f * h, ro = 0.55f * h, ri = 0.2f * h, start = ri + 0.08f * h;
            var scene = new Scene();
            var roads = new List<float[]> { Wound(CircleContour(0f, 0f, ro)), Wound(CircleContour(0f, 0f, ri), true) };

            GuideBlock label = ShaftLabel(s.Get("xroad"), h);
            float tipSide = label.W > 0f ? ro + 0.05f * h + label.W + 0.3f * h : ro + 1.0f * h;
            float tipUp = ro + 1.15f * h;
            roads.Add(Arm(0f, -start, 0f, -1f, tipUp - start, w, true));
            roads.Add(Arm(-start, 0f, -1f, 0f, tipSide - start, w, true));
            roads.Add(Arm(start, 0f, 1f, 0f, tipSide - start, w, true));

            GuideBlock ahead1 = s.Flag("tour") ? TouristName(s.Get("to1"), h) : Info(s.Get("to1"), h, Blue);
            GuideBlock ahead = Row(0.6f * h, ahead1, Info(s.Get("to2"), h, Blue));
            GuideBlock lcol = Column(0.3f * h, 1f, Info(s.Get("tl1"), h, Blue), Info(s.Get("tl2"), h, Blue));
            GuideBlock rcol = Column(0.3f * h, 0f, Info(s.Get("tr1"), h, Blue), Info(s.Get("tr2"), h, Blue));
            float low = Math.Max(ro + 0.5f * h, 0.2f * h + 0.5f * Math.Max(lcol.H, rcol.H));
            roads.Add(Arm(0f, start, 0f, 1f, low - start, w, false));
            scene.Add(Figure(roads, White));

            if (label.W > 0f)
            {
                scene.Add(label, -(ro + 0.05f * h) - label.W, -label.H * 0.5f);
                scene.Add(label, ro + 0.05f * h, -label.H * 0.5f);
            }

            if (ahead.W > 0f)
                scene.Add(ahead, -ahead.W * 0.5f, -tipUp - 0.35f * h - ahead.H);
            if (lcol.W > 0f)
                scene.Add(lcol, -tipSide - 0.65f * h - lcol.W, -lcol.H * 0.5f);
            if (rcol.W > 0f)
                scene.Add(rcol, tipSide + 0.65f * h, -rcol.H * 0.5f);

            string dir = s.Get("dir");
            GuidePart p = Fit(scene, h, Blue, 0.6f * h, 0.4f * h, 0.6f * h, 0.4f * h, 0f, 0f, dir.Length > 0 ? DirBoxK * h : 0f);
            AddDirMark(p, h, dir);
            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路3 堆叠式 Fig. 207; E.169

        /// <summary>
        /// 路3 (Fig. 207, E.169): one row for each direction in the order ahead, left, right (8.1.7 a), the rows 1.8 h high (0.4 h, the characters, 0.4 h) and parted by white lines 0.1 h wide; the
        /// arrows of ahead and left before the text, the arrow of right after it; a horizontal arrow is 1.5 h long and h high, the arrow ahead 1.5 h high and h wide. Two informations of a
        /// direction stand in its row, 0.6 h apart (8.1.4 c). Rows without an information are left out.
        /// </summary>
        private static GuideBoard Stacked(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            var rows = new List<(GuideArrow arrow, GuideBlock info)>();
            void AddRow(GuideArrow a, string k1, string k2)
            {
                GuideBlock info = Row(0.6f * h, Info(s.Get(k1), h, Blue), Info(s.Get(k2), h, Blue));
                if (info.W > 0f)
                    rows.Add((a, info));
            }

            AddRow(GuideArrow.Up, "to1", "to2");
            AddRow(GuideArrow.Left, "tl1", "tl2");
            AddRow(GuideArrow.Right, "tr1", "tr2");
            if (rows.Count == 0)
                rows.Add((GuideArrow.Up, new GuideBlock(0f, 0f)));

            float innerW = 0f;
            var heights = new List<float>();
            foreach ((GuideArrow arrow, GuideBlock info) in rows)
            {
                bool vertical = arrow == GuideArrow.Up;
                float aw = vertical ? h : 1.5f * h;
                float side = vertical ? 0.5f : 0.6f;
                innerW = Math.Max(innerW, side * h + aw + (info.W > 0f ? 0.8f * h + info.W : 0f) + 0.6f * h);
                heights.Add(Math.Max(1.8f * h, info.H + 0.8f * h));
            }

            float line = 0.1f * h, innerH = line * (rows.Count - 1);
            foreach (float rh in heights)
                innerH += rh;
            GuidePart p = Board(h, innerW, innerH, Blue);
            float e = p.Edge, y = e;
            for (int i = 0; i < rows.Count; i++)
            {
                (GuideArrow arrow, GuideBlock info) = rows[i];
                float rh = heights[i];
                bool vertical = arrow == GuideArrow.Up, after = arrow == GuideArrow.Right;
                float aw = vertical ? h : 1.5f * h, ah = vertical ? 1.5f * h : h;
                float side = (vertical ? 0.5f : 0.6f) * h;
                float ax = after ? e + innerW - side - aw : e + side;
                p.Elements.Add(ArrowIn(arrow, ax, y + (rh - ah) * 0.5f, aw, ah, White));
                if (info.W > 0f)
                {
                    float from = after ? e + 0.6f * h : ax + aw + 0.8f * h;
                    float to = after ? ax - 0.8f * h : e + innerW - 0.6f * h;
                    p.Add(info, from + (to - from - info.W) * 0.5f, y + (rh - info.H) * 0.5f);
                }

                y += rh;
                if (i < rows.Count - 1)
                {
                    p.Elements.Add(Rect(e, y, innerW, line, White));
                    y += line;
                }
            }

            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路4 车道式 Fig. 208; E.170

        private static readonly GuideChoice[] kLaneArrows =
        {
            new GuideChoice("s", "↑ 直行", "↑ ahead"),
            new GuideChoice("l", "↰ 左转", "↰ left turn"),
            new GuideChoice("r", "↱ 右转", "↱ right turn"),
            new GuideChoice("sl", "直行和左转", "ahead and left"),
            new GuideChoice("sr", "直行和右转", "ahead and right")
        };

        /// <summary>
        /// The lane arrows of 路4 (E.170: 1.5 h high, the head h wide as E.166 c; the turning arrow bends right or left into a horizontal head). Drawn in a box of w x 1.5 h from (x, y). The
        /// width each kind needs.
        /// </summary>
        private static float LaneArrowWidth(string kind, float h) => kind == "s" ? h : kind == "l" || kind == "r" ? 1.5f * h : 1.9f * h;

        private static GuideElement LaneArrow(string kind, float x, float y, float h)
        {
            float B = 1.5f * h, a = 0.9f * h, t = 0.25f * h;
            float W = LaneArrowWidth(kind, h);
            bool mirror = kind == "l" || kind == "sl";
            var contours = new List<float[]>();
            // Drawn for the right turn, mirrored for the left one; x in the box from its left side.
            float sx = kind == "r" || kind == "l" ? 0.35f * h : 0.5f * h;
            if (kind == "s" || kind == "sl" || kind == "sr")
            {
                // The straight arrow: head h wide on top, the stem to the bottom.
                float ax = kind == "s" ? 0.5f * h : sx;
                float ha = kind == "s" ? h : a;
                contours.Add(Wound(RectContour(ax - t * 0.5f, ha * 0.55f, t, B - ha * 0.55f)));
                contours.Add(Head(ax, 0f, -90f, ha, t));
            }

            if (kind != "s")
            {
                // The turning arm: up from below (or out of the stem under the straight head), a bend, then the head to the right; a smaller head when it shares the stem.
                bool alone = kind == "r" || kind == "l";
                if (!alone)
                    a = 0.6f * h;
                float yh = alone ? 0.45f * h : 1.12f * h;
                float rr = alone ? 0.35f * h : 0.22f * h;
                float yStart = alone ? B : B - 0.05f * h;
                var c = new List<float> { sx, yStart, sx, yh + rr };
                for (int k = 1; k <= 8; k++)
                {
                    double ang = Math.PI + Math.PI * 0.5 * k / 8;
                    c.Add(sx + rr + rr * (float)Math.Cos(ang));
                    c.Add(yh + rr + rr * (float)Math.Sin(ang));
                }

                c.Add(W - 0.55f * a);
                c.Add(yh);
                contours.Add(Thick(c, t));
                contours.Add(Head(W, yh, 0f, a, t));
            }

            var placed = new List<float[]>();
            foreach (float[] c in contours)
            {
                var m = new float[c.Length];
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    m[i] = x + (mirror ? W - c[i] : c[i]);
                    m[i + 1] = y + c[i + 1];
                }

                placed.Add(Wound(m));
            }

            return Figure(placed, White);
        }

        /// <summary>The fields of a 路4 board: the number of lanes, then for each of the six lanes its information and its arrow. The defaults are given for the first lanes (the rest: empty, ahead).</summary>
        private static GuideField[] LaneFields(string count, params string[] defaults)
        {
            var fields = new List<GuideField> { GuideField.Choice("n", "车道数", "Lanes", count, kLaneCounts) };
            for (int i = 1; i <= MaxLanes; i++)
            {
                string place = defaults.Length > (i - 1) * 2 ? defaults[(i - 1) * 2] : "";
                string arrow = defaults.Length > (i - 1) * 2 + 1 ? defaults[(i - 1) * 2 + 1] : "s";
                fields.Add(Place("to" + i, "车道" + i + "信息（可空）", "Lane " + i + " (may be empty)", place));
                fields.Add(GuideField.Choice("a" + i, "车道" + i + "箭头", "Lane " + i + " arrow", arrow, kLaneArrows));
            }

            return fields.ToArray();
        }

        private static readonly GuideChoice[] kLaneCounts =
        {
            new GuideChoice("0", "按有信息的车道", "The lanes that have an information"),
            new GuideChoice("1", "1 条车道", "1 lane"), new GuideChoice("2", "2 条车道", "2 lanes"), new GuideChoice("3", "3 条车道", "3 lanes"),
            new GuideChoice("4", "4 条车道", "4 lanes"), new GuideChoice("5", "5 条车道", "5 lanes"), new GuideChoice("6", "6 条车道", "6 lanes")
        };

        /// <summary>The most lanes a 路4 board has.</summary>
        public const int MaxLanes = 6;

        /// <summary>
        /// The dashes of a dashed line from y0 to y1, as long as the gaps between them and as near to the length asked for as a whole number of them allows (0.12.78: the lines of 路4 ended in a dash
        /// of 0.1 to 0.2 h where the length was not a multiple of the period, next to dashes of 0.6 h).
        /// </summary>
        internal static IEnumerable<(float y, float length)> EvenDashes(float y0, float y1, float length)
        {
            float span = Math.Max(0f, y1 - y0);
            int count = Math.Max(1, (int)Math.Round((span / length + 1f) * 0.5f, MidpointRounding.AwayFromZero));
            float dash = span / (2 * count - 1);
            for (int i = 0; i < count; i++)
                yield return (y0 + 2f * i * dash, dash);
        }

        /// <summary>
        /// 路4 (Fig. 208, E.170; Fig. H.1 a): a column for each lane, its information (h) above its arrow (1.5 h), 0.3 h apart, 0.4 h from the top and the bottom; the lanes parted by a dashed white
        /// line 0.2 h wide, dashes and gaps 0.6 h. With "lanes" 0 (the form of 0.5.8 and before, and what an item saved then still says) the lanes are the ones that have an information, up to six; with a
        /// number (1 to 6) there are that many lanes, **each with its arrow whether it has an information or not**, and a board none of whose lanes has an information is the strip of arrows
        /// alone of Fig. H.1 a (the information row and the gap under it are left out).
        /// </summary>
        private static GuideBoard Lanes(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            int count = (int)Math.Max(0, Math.Min(MaxLanes, s.Number("n", 0)));
            var lanes = new List<(GuideBlock info, string arrow)>();
            for (int i = 1; i <= MaxLanes; i++)
            {
                GuideBlock info = Info(s.Get("to" + i), h, Blue);
                if (count == 0 ? info.W > 0f : i <= count)
                    lanes.Add((info, s.Get("a" + i)));
            }

            if (lanes.Count == 0)
                lanes.Add((new GuideBlock(0f, 0f), "s"));
            bool anyInfo = false;
            float infoH = h;
            foreach ((GuideBlock info, string arrow) in lanes)
            {
                anyInfo |= info.W > 0f;
                infoH = Math.Max(infoH, info.H);
            }

            if (!anyInfo)
                infoH = 0f;

            var widths = new List<float>();
            float innerW = 0f, divider = 0.2f * h;
            foreach ((GuideBlock info, string arrow) in lanes)
            {
                float cw = Math.Max(info.W, LaneArrowWidth(arrow, h)) + 0.8f * h;
                widths.Add(cw);
                innerW += cw;
            }

            innerW += divider * (lanes.Count - 1) + 0.4f * h;
            float innerH = 0.4f * h + infoH + (anyInfo ? 0.3f * h : 0f) + 1.5f * h + 0.4f * h;
            GuidePart p = Board(h, innerW, innerH, Blue);
            float e = p.Edge, x = e + 0.2f * h;
            for (int i = 0; i < lanes.Count; i++)
            {
                (GuideBlock info, string arrow) = lanes[i];
                float cw = widths[i];
                if (info.W > 0f)
                    p.Add(info, x + (cw - info.W) * 0.5f, e + 0.4f * h + (infoH - info.H) * 0.5f);
                float aw = LaneArrowWidth(arrow, h);
                p.Elements.Add(LaneArrow(arrow, x + (cw - aw) * 0.5f, e + 0.4f * h + infoH + (anyInfo ? 0.3f * h : 0f), h));
                x += cw;
                if (i < lanes.Count - 1)
                {
                    foreach ((float y, float length) in EvenDashes(e + 0.5f * h, e + innerH - 0.5f * h, 0.6f * h))
                        p.Elements.Add(Rect(x, y, divider, length, White));
                    x += divider;
                }
            }

            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路5 道路编号 Fig. 210; E.171

        /// <summary>
        /// 路5 (Fig. 210, E.171): the road number alone, letters and digits h high, 0.3 h from the edge, the board 2 h high: G red with a white border and white letters; S yellow with a black
        /// border and black letters; X (county), Y (township) and other letters white with a black border and black letters (8.3.3.4).
        /// </summary>
        private static GuideBoard RoadNumber(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string code = s.Get("code");
            char letter = code.Length > 0 ? char.ToUpperInvariant(code[0]) : 'G';
            GuideColour ground = letter == 'G' ? Red : letter == 'S' ? Yellow : White;
            GuideColour ink = letter == 'G' ? White : Black;
            GuideBlock line = Line(code.Length > 0 ? code : "G", h, ink, 0.2f * h);
            GuidePart p = Board(h, line.W + 0.6f * h, 1.6f * h, ground, letter == 'G' ? White : Black);
            p.Add(line, p.Edge + 0.3f * h, p.Edge + 0.3f * h);
            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路6 路名, 路9 地名, 路10 行政区划分界, 路11 道路管理分界 Fig. 211, 214 to 216; E.172, E.175

        /// <summary>A name alone (E.172, E.175): the characters h high and 0.2 h apart, 0.3 h from the edge, the board 2 h high.</summary>
        private static GuideBoard NameBoard(string text, float h)
        {
            GuideBlock line = Line(text, h, White, 0.2f * h);
            // 2 h high; more with an English line under the name.
            GuidePart p = Board(h, Math.Max(2.6f * h, line.W + 0.6f * h), line.H > h * 1.05f ? line.H + 0.6f * h : 1.6f * h, Blue);
            p.Add(line, p.Edge + 0.3f * h, p.Edge + 0.3f * h);
            return Single(p, h);
        }

        /// <summary>
        /// 路6 upright (8.3.4.3: in special cases the characters may stand one under the other, for one road name only, from the top down): E.172 turned, the characters h high 0.2 h apart, 0.3 h from the
        /// edge, the board 2 h wide. A letter or a digit stands in its own cell as a character does.
        /// </summary>
        private static GuideBoard UprightNameBoard(string text, float h)
        {
            text = (text ?? "").Trim();
            var cells = new List<GuideBlock>();
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                    continue;
                GuideBlock one;
                using (Bilingual.Suspend())
                    one = Line(c.ToString(), h, White, 0f);
                if (one.W > 0f)
                    cells.Add(one);
            }

            if (cells.Count == 0)
                cells.Add(new GuideBlock(h, h));
            GuideBlock column = Column(0.2f * h, 0.5f, cells.ToArray());
            GuidePart p = Board(h, 1.6f * h, Math.Max(2.6f * h, column.H + 0.6f * h), Blue);
            p.Add(column, p.Edge + (1.6f * h - column.W) * 0.5f, p.Edge + (p.H - 2f * p.Edge - column.H) * 0.5f);
            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路7 道路名称方向 Fig. 212; E.173

        /// <summary>How far across the sides of 路7 a's point go for every metre down (E.173 a, Fig. 212 a).</summary>
        internal const float PointSlope = 0.64f;

        private static readonly GuideChoice[] kNameDirs =
        {
            new GuideChoice("r", "指向右（尖头在右）", "Pointing right"),
            new GuideChoice("l", "指向左（尖头在左）", "Pointing left"),
            new GuideChoice("b", "两侧（两端三角）", "Both sides (triangles)"),
            new GuideChoice("u", "直行（箭头在左）", "Ahead (arrow at the left)")
        };

        /// <summary>
        /// 路7 (Fig. 212, E.173): a) the board pointed to the road's side (0.12.1; until then a rectangle with the chevron in it): 2.5 h high, the name 0.55 h from the inside's top and bottom and 0.3 h from
        /// its far side; 0.3 h after the name a white chevron 0.4 h wide, its sides parallel to those of the point, and from the chevron's start 1 h to the
        /// board's tip. The sides of the point go 0.64 h across for every h down (E.173 a: the point is 0.95 h from the corner region of 0.3 h to the tip; measured 0.65 on Fig. 212 a), the chevron's
        /// outer side 0.36 h inside the board's and its ends 0.1 h from the border (the liner, the border and the gaps of Fig. 212 a). b) the name between two small triangles 0.3 h wide and 0.8 h high, 0.2 h from it; c), d) the arrow ahead
        /// (h wide, 1.5 h high) 0.3 h from the edge and 0.6 h before the name or 0.3 h before a road number plate (1.2 h high).
        /// </summary>
        private static GuideBoard NameDirection(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string form = s.Get("form");
            string name = s.Get("name");
            GuidePart p;
            if (form == "u")
            {
                GuideBlock info = Info(name, h, Blue);
                bool plate = info.H > h * 1.05f;
                float gap = plate ? 0.3f * h : 0.6f * h;
                float innerH = Math.Max(1.8f * h, info.H + (plate ? 0.6f : 0.8f) * h);
                float innerW = 0.3f * h + h + (info.W > 0f ? gap + info.W : 0f) + 0.3f * h;
                p = Board(h, innerW, innerH, Blue);
                float e = p.Edge;
                p.Elements.Add(ArrowIn(GuideArrow.Up, e + 0.3f * h, e + (innerH - 1.5f * h) * 0.5f, h, 1.5f * h, White));
                if (info.W > 0f)
                    p.Add(info, e + 1.3f * h + gap, e + (innerH - info.H) * 0.5f);
                return Single(p, h);
            }

            GuideBlock line = Line(name, h, White, 0.2f * h);
            if (line.W <= 0f)
                line.W = 2f * h;
            if (form == "b")
            {
                float innerW = 0.3f * h + 0.3f * h + 0.2f * h + line.W + 0.2f * h + 0.3f * h + 0.3f * h;
                p = Board(h, innerW, 1.6f * h, Blue);
                float e = p.Edge, cy = e + 0.8f * h;
                float lx = e + 0.3f * h, rx = e + innerW - 0.3f * h;
                p.Elements.Add(Poly(new[] { lx, cy, lx + 0.3f * h, cy - 0.4f * h, lx + 0.3f * h, cy + 0.4f * h }, White));
                p.Elements.Add(Poly(new[] { rx, cy, rx - 0.3f * h, cy + 0.4f * h, rx - 0.3f * h, cy - 0.4f * h }, White));
                p.Add(line, e + 0.8f * h, e + 0.3f * h);
                return Single(p, h);
            }

            // a): the board pointed to the side of the road (E.173 a).
            bool toLeft = form == "l";
            float inner = 2.1f * h, edge = EdgeK * h;
            p = Board(h, 0.3f * h + line.W + 0.3f * h + 0.4f * h + 1.0f * h - edge, inner, Blue);
            float e2 = p.Edge, W = p.W, H = p.H;
            p.Shape = toLeft ? GuidePartShape.PointLeft : GuidePartShape.PointRight;
            p.Side = PointSlope * H * 0.5f;
            // The chevron, drawn for the right and mirrored for the left (x measured from the tip's side).
            float y0 = e2 + 0.1f * h, outerTop = W - 0.36f * h - PointSlope * (H * 0.5f - y0), outerMid = W - 0.36f * h;
            float[] chevron = { outerTop - 0.4f * h, y0, outerTop, y0, outerMid, H * 0.5f, outerTop, H - y0, outerTop - 0.4f * h, H - y0, outerMid - 0.4f * h, H * 0.5f };
            if (toLeft)
            {
                for (int i = 0; i < chevron.Length; i += 2)
                    chevron[i] = W - chevron[i];
            }

            p.Elements.Add(Poly(chevron, White));
            p.Add(line, toLeft ? W - e2 - 0.3f * h - line.W : e2 + 0.3f * h, e2 + 0.55f * h);
            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路8 地点距离 Fig. 213; E.174

        /// <summary>
        /// 路8 (Fig. 213, E.174), and on green the simple form of the expressway board 路40 (9.3.6; the two groups of Fig. 262 parted by a line are not drawn): up to three rows, near to far from the top (8.3.4.4), each an information at the left and its distance at the right (digits h high, the unit 0.5 h), the rows
        /// 0.4 h apart and 0.4 h from the edge; a road number plate (1.2 h) takes its row's height.
        /// </summary>
        private static GuideBoard PlaceDistance(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string unit = s.Get("unit");
            GuideColour ground = s.Is("ground", "green") ? Green : Blue;
            var rows = new List<(GuideBlock info, GuideBlock dist)>();
            for (int i = 1; i <= 3; i++)
            {
                GuideBlock info = Info(s.Get("to" + i), h, ground);
                string d = s.Get("d" + i);
                if (info.W <= 0f)
                    continue;
                rows.Add((info, d.Length > 0 ? Distance(d, unit, h, White) : null));
            }

            if (rows.Count == 0)
                rows.Add((new GuideBlock(2f * h, h), null));
            float infoW = 0f, distW = 0f, innerH = 0.4f * h;
            foreach ((GuideBlock info, GuideBlock dist) in rows)
            {
                infoW = Math.Max(infoW, info.W);
                if (dist != null)
                    distW = Math.Max(distW, dist.W);
                innerH += Math.Max(h, info.H) + 0.4f * h;
            }

            float innerW = 0.4f * h + infoW + (distW > 0f ? 0.6f * h + distW : 0f) + 0.4f * h;
            GuidePart p = Board(h, innerW, innerH, ground);
            float e = p.Edge, y = e + 0.4f * h;
            foreach ((GuideBlock info, GuideBlock dist) in rows)
            {
                float rh = Math.Max(h, info.H);
                p.Add(info, e + 0.4f * h, y + (rh - info.H) * 0.5f);
                if (dist != null)
                    p.Add(dist, e + innerW - 0.4f * h - dist.W, y + (rh - h) * 0.5f);
                y += rh + 0.4f * h;
            }

            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路27 方向标志 Fig. 241, 242; E.190, E.191

        /// <summary>
        /// 路27 (Fig. 241, 242, E.190, E.191): a square 2 h on a side with the character h high (0.3 h from the edge in the lines of E.190); set apart from a guide board it is blue with a white
        /// character, in a guide board white with a blue character and a blue border.
        /// </summary>
        private static GuideBoard DirectionSign(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            bool inBoard = s.Is("form", "in");
            string c = s.Get("dir");
            if (c.Length == 0)
                c = "北";
            float inner = 1.6f * h;
            GuidePart p = Board(h, inner, inner, inBoard ? White : Blue, inBoard ? Blue : White);
            float cell = h;
            p.Elements.Add(Cells(c, p.Edge + (inner - cell) * 0.5f, p.Edge + (inner - cell) * 0.5f, cell, inBoard ? Blue : White, 0f));
            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路25 此路不通 Fig. 239; E.188

        /// <summary>路25 (Fig. 239, E.188): 80 cm high and 68 wide (60 x 51 below 40 km/h), edges 4 (3) cm; the street a white bar 8 cm wide and 40 long ending at a red bar 30 x 8 cm.</summary>
        private static GuideBoard DeadEnd(int band)
        {
            float H = Pick(band, 0.80f, 0.60f), k = H / 0.80f;
            GuidePart p = Plain(0.68f * k, H, 0.04f * k, Blue);
            var c = new Canvas(p, 0f, 0f, p.W, p.H);
            float cx = p.W * 0.5f;
            c.Add(Rect(cx - 0.04f * k, 0.24f * k, 0.08f * k, 0.40f * k, White));
            c.Add(Rect(cx - 0.15f * k, 0.16f * k, 0.30f * k, 0.08f * k, Red));
            return Single(p, 0.4f * k);
        }

        // ================================================================== 8.4.3 and 8.5: the facility squares

        /// <summary>A canvas over the inside of a board's border (the frame most drawings of Appendix E put their figure in).</summary>
        private static Canvas Inside(GuidePart p) => new Canvas(p, p.Edge, p.Edge, p.W - 2f * p.Edge, p.H - 2f * p.Edge);

        /// <summary>A canvas over the whole board, in percent of its sides.</summary>
        private static Canvas Whole(GuidePart p) => new Canvas(p, 0f, 0f, p.W, p.H);

        /// <summary>A glyph drawn over (an area of) a canvas in the frame it was written in; the element's box is made the box of what it draws (the frame may reach over the border).</summary>
        private static void Draw(Canvas c, string glyph, GuideColour colour, float ux = 0f, float uy = 0f, float uw = 100f, float uh = 100f)
        {
            c.Pic(glyph, colour, ux, uy, uw, uh);
            GuideElement e = c.Part.Elements[c.Part.Elements.Count - 1];
            GuidePath.Bounds(e.Contours, out float x0, out float y0, out float x1, out float y1);
            e.X = x0;
            e.Y = y0;
            e.W = x1 - x0;
            e.H = y1 - y0;
        }

        /// <summary>A plain facility square (E.176 a, b, E.178, E.182, E.186): 80 cm (60 below 40 km/h), edges 4 (3) cm, the figure in the board's colour on blue.</summary>
        private static GuidePart Square80(int band, GuideColour ground = Blue, GuideColour border = White)
        {
            float s = Pick(band, 0.80f, 0.60f);
            return Plain(s, s, s / 20f, ground, border);
        }

        /// <summary>
        /// A facility square with a white panel (E.176 d to f, E.179): 100 cm (80), edges 5 (4) cm, a white square two thirds of the side in the middle with the figure in black; the canvas
        /// is the panel.
        /// </summary>
        private static Canvas Panel100(int band, out GuidePart p)
        {
            float s = Pick(band, 1.00f, 0.80f);
            p = Plain(s, s, s / 20f, Blue);
            float k = 0.667f * s, o = (s - k) * 0.5f;
            p.Elements.Add(Rect(o, o, k, k, White));
            return new Canvas(p, o, o, k, k);
        }

        private static GuideBoard Facility(GuidePart p) => Single(p, p.H * 0.5f);

        // ------------------------------------------------------------------ 路12 地点识别 Fig. 217, 218; E.176

        private static readonly GuideChoice[] kPlaces =
        {
            new GuideChoice("aid", "急救站", "First aid station"),
            new GuideChoice("air", "飞机场", "Airport"),
            new GuideChoice("wash", "洗车、加油（多个场所）", "Car wash and fuel (several places)"),
            new GuideChoice("fuel", "加油站", "Fuel station"),
            new GuideChoice("ev", "电动汽车充电站", "Charging station"),
            new GuideChoice("metro", "地铁", "Metro"),
            new GuideChoice("ferry", "汽车轮渡", "Car ferry")
        };

        /// <summary>
        /// 路12 (Fig. 217, 218 c, E.176): the first aid station (the red cross over the bandaged hand), the airport (the aeroplane, its nose to the side of the road to it: 8.4.3) and the car ferry on
        /// a blue square of 80 cm; several places on a board 120 x 80 cm (Fig. 217 c); the fuel station, the charging station and the metro in black on a white panel in a square of 100 cm.
        /// </summary>
        private static GuideBoard PlaceSymbol(GuideSpec s, int band)
        {
            string kind = s.Get("kind");
            GuidePart p;
            switch (kind)
            {
                case "fuel":
                case "ev":
                case "metro":
                {
                    Canvas c = Panel100(band, out p);
                    Draw(c, kind == "fuel" ? Glyphs.R_Fuel : kind == "ev" ? Glyphs.R_Charger : Glyphs.R_Metro, Black);
                    return Facility(p);
                }

                case "wash":
                {
                    float hh = Pick(band, 0.80f, 0.60f);
                    p = Plain(1.5f * hh, hh, hh / 20f, Blue);
                    Canvas c = Inside(p);
                    c.Fit(Glyphs.R_CarWash, White, 7.5f, 12.7f, 33.8f, 74.1f);
                    c.Fit(Glyphs.R_Fuel, White, 55.3f, 12.4f, 37f, 74.7f);
                    return Facility(p);
                }

                case "air":
                    p = Square80(band);
                    Draw(Inside(p), s.Is("side", "r") ? Glyphs.Mirror(Glyphs.R_Airplane) : Glyphs.R_Airplane, White);
                    return Facility(p);
                case "ferry":
                    p = Square80(band);
                    Inside(p).Fit(Glyphs.R_Ferry, White, 8f, 15f, 84f, 70f);
                    return Facility(p);
                default:
                {
                    p = Square80(band);
                    Canvas c = Inside(p);
                    c.Fit(Glyphs.R_FirstAidCross, Red, 35.6f, 11.8f, 32.5f, 29.8f);
                    Draw(c, Glyphs.R_FirstAidHand, White);
                    return Facility(p);
                }
            }
        }

        // ------------------------------------------------------------------ 路13 停车场 Fig. 219; E.177

        /// <summary>
        /// 路13 (Fig. 219, E.177): the open car park, a white serif P on a blue board 68 x 80 cm (51 x 60), edges 4 (3) cm; the indoor car park, the P under a roof on a board 80 x 100 cm (64 x 80),
        /// edges 5 (4) cm. Positions read off the drawings.
        /// </summary>
        private static GuideBoard Parking(GuideSpec s, int band)
        {
            if (s.Is("form", "in"))
            {
                float hh = Pick(band, 1.00f, 0.80f);
                GuidePart b = Plain(0.80f * hh, hh, hh / 20f, Blue);
                Canvas c = Whole(b);
                c.Fit(Glyphs.R_Roof, White, 16.1f, 17.8f, 67.9f, 22.7f);
                c.Fit(Glyphs.R_P, White, 32.6f, 43.9f, 41.1f, 39.7f);
                return Facility(b);
            }

            float H = Pick(band, 0.80f, 0.60f);
            GuidePart p = Plain(0.85f * H, H, H / 20f, Blue);
            Whole(p).Fit(Glyphs.R_P, White, 23.4f, 24.8f, 52.9f, 50.2f);
            return Facility(p);
        }

        // ------------------------------------------------------------------ 路14 错车道, 路15 港湾式紧急停车带, 路16, 路17, 路18, 路22

        /// <summary>路14 (Fig. 221, E.178): the passing place in white on a blue square of 80 cm (60).</summary>
        private static GuideBoard PassingPlace(int band)
        {
            GuidePart p = Square80(band);
            Draw(Whole(p), Glyphs.R_PassingBay, White);
            return Facility(p);
        }

        /// <summary>路15 (Fig. 222, E.179): the lay-by in black on the white panel of a blue square of 100 cm (80).</summary>
        private static GuideBoard Layby(int band)
        {
            Draw(Panel100(band, out GuidePart p), Glyphs.R_Layby, Black);
            return Facility(p);
        }

        /// <summary>
        /// 路16 and 路17 (Fig. 224, 225, E.180, E.181): squares of 40 cm with edges of 2 cm, as the drawings give them; the footbridge in white on blue, the underpass in blue on white with a blue
        /// border.
        /// </summary>
        private static GuideBoard Crossing(bool under)
        {
            GuidePart p = Plain(0.40f, 0.40f, 0.02f, under ? White : Blue, under ? Blue : White);
            Draw(Inside(p), under ? Glyphs.R_Underpass : Glyphs.R_Footbridge, under ? Blue : White);
            return Facility(p);
        }

        /// <summary>路18 (Fig. 227, E.182) and 路22 (Fig. 234, E.186): the wheelchair and the person running to the shelter, in white on a blue square of 80 cm (60).</summary>
        private static GuideBoard SymbolSquare(int band, string glyph)
        {
            GuidePart p = Square80(band);
            Draw(Inside(p), glyph, White);
            return Facility(p);
        }

        // ------------------------------------------------------------------ boards of symbols and text (路19 to 路21, 路23; E.183 to E.187)

        /// <summary>A glyph fitted (evenly scaled, centred) into a box in metres of the part's frame.</summary>
        private static void FitAt(GuidePart p, string glyph, GuideColour colour, float x, float y, float w, float hh) => new Canvas(p, x, y, w, hh).Fit(glyph, colour);

        private static readonly GuideChoice[] kOptArrows =
        {
            new GuideChoice("", "不设", "None"),
            new GuideChoice("b", "↗ 右前方", "↗ ahead right"),
            new GuideChoice("d", "↖ 左前方", "↖ ahead left"),
            new GuideChoice("c", "↑ 前方", "↑ ahead"),
            new GuideChoice("a", "→ 右", "→ right"),
            new GuideChoice("e", "← 左", "← left")
        };

        /// <summary>An arrow in a square box: the diagonal ones fill it (45 degrees), the straight ones are as long as the box and 0.7 of it wide.</summary>
        private static GuideElement ArrowSquare(GuideArrow dir, float x, float y, float s, GuideColour colour)
        {
            switch (dir)
            {
                case GuideArrow.Left:
                case GuideArrow.Right:
                    return ArrowIn(dir, x, y + 0.15f * s, s, 0.7f * s, colour);
                case GuideArrow.Up:
                case GuideArrow.Down:
                    return ArrowIn(dir, x + 0.15f * s, y, 0.7f * s, s, colour);
                default:
                    return ArrowIn(dir, x, y, s, s, colour);
            }
        }

        /// <summary>
        /// A white band along the bottom of a board's inside, bled over the border so that no line of the ground is left between them (the bleed of the framework grows a band upwards, so the
        /// element starts one border width lower and the grown band starts where the band does).
        /// </summary>
        private static void BottomBand(GuidePart p, float top, GuideColour colour)
        {
            float b = p.Border, e = p.Edge;
            GuideElement band = Rect(e, top + b, p.W - 2f * e, p.H - e - top - b, colour);
            band.RadiusBottom = p.InnerRadius;
            band.Bleed = b;
            p.Elements.Add(band);
        }

        /// <summary>A white band along the top of a board's inside, bled over the border.</summary>
        private static void TopBand(GuidePart p, float height, GuideColour colour)
        {
            float e = p.Edge;
            GuideElement band = Rect(e, e, p.W - 2f * e, height, colour);
            band.RadiusTop = p.InnerRadius;
            band.Bleed = p.Border;
            p.Elements.Add(band);
        }

        /// <summary>
        /// 路19 服务站, 路20 停车点, 路21 观景台 (Fig. 228 to 233, E.183 to E.185): a blue board about 3.4 h wide; in its upper part the symbols in two rows 1.5 h high (the toilets and drinking water,
        /// then the P and the arrow; the P with the toilets, then the arrow; the scenery, then the P and the arrow), 0.3 h apart; in a white band 1.6 h high at the bottom the name in blue characters h
        /// high. Without an arrow the P stands in the middle (Fig. 229, 233) and the stopping point loses its second row (Fig. 231).
        /// </summary>
        private static GuideBoard ServiceBoard(GuideSpec s, int band, string kind)
        {
            float h = CharHeight(band);
            string name = s.Get("name");
            GuideBlock text = Line(name, h, Blue, 0.15f * h);
            string a = s.Get("arrow");
            bool arrow = a.Length > 0;
            float innerW = Math.Max(3.4f * h, text.W + 0.6f * h);
            float row = 1.5f * h, gap = 0.3f * h, pad = 0.25f * h;
            bool second = kind != "stop" || arrow;
            float upper = pad + row + (second ? gap + row : 0f) + pad;
            float bandH = 1.6f * h;
            GuidePart p = Board(h, innerW, upper + bandH, Blue);
            float e = p.Edge, x0 = e + 0.3f * h, x1 = e + innerW - 0.3f * h, y1 = e + pad, y2 = y1 + row + gap;
            float arrowS = 1.15f * h;
            if (kind == "station")
            {
                FitAt(p, Glyphs.R_Toilet, White, x0, y1, 1.4f * h, row);
                FitAt(p, Glyphs.R_Drink, White, x1 - 0.75f * h, y1, 0.75f * h, row);
            }
            else if (kind == "stop")
            {
                FitAt(p, Glyphs.R_P, White, x0, y1, 0.95f * h, row);
                FitAt(p, Glyphs.R_Toilet, White, x1 - 1.4f * h, y1, 1.4f * h, row);
            }
            else
            {
                FitAt(p, Glyphs.R_Scenic, White, x0, y1, x1 - x0, row);
            }

            if (kind == "stop")
            {
                if (arrow)
                    p.Elements.Add(ArrowSquare(GuideTemplates.ArrowOf(a), e + (innerW - arrowS) * 0.5f, y2 + (row - arrowS) * 0.5f, arrowS, White));
            }
            else if (arrow)
            {
                FitAt(p, Glyphs.R_P, White, x0, y2, 0.8f * h, row);
                p.Elements.Add(ArrowSquare(GuideTemplates.ArrowOf(a), x1 - arrowS, y2 + (row - arrowS) * 0.5f, arrowS, White));
            }
            else
            {
                FitAt(p, Glyphs.R_P, White, e + (innerW - 0.8f * h) * 0.5f, y2, 0.8f * h, row);
            }

            BottomBand(p, e + upper, White);
            p.Add(text, e + (innerW - text.W) * 0.5f, e + upper + (bandH - h) * 0.5f);
            return Single(p, h);
        }

        /// <summary>
        /// 路23 超限检测站 (Fig. 236, E.187): 6 h wide; in the upper part the white panel (3.8 h x 3.1 h) with the scale and the lorry in black, parted from the lower part by a white line 0.1 h
        /// wide; below the name in characters h high, and under it the distance (500 m, set 500 m before the station) and the arrow, or the arrow alone at the entrance.
        /// </summary>
        private static GuideBoard WeighStation(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string name = s.Get("name");
            GuideBlock text = Line(name, h, White, 0.1f * h);
            string d = s.Get("dist"), a = s.Get("arrow");
            GuideBlock dist = d.Length > 0 ? Distance(d, s.Get("unit"), h, White) : null;
            float arrowS = 1.1f * h;
            float rowW = (dist != null ? dist.W : 0f) + (a.Length > 0 ? arrowS : 0f) + (dist != null && a.Length > 0 ? 1.0f * h : 0f);
            float innerW = Math.Max(5.6f * h, Math.Max(text.W, rowW) + 0.8f * h);
            float panelW = 3.8f * h, panelH = 3.1f * h;
            float upper = 0.35f * h + panelH + 0.35f * h;
            bool hasRow = rowW > 0f;
            float lower = 0.4f * h + h + (hasRow ? 0.35f * h + arrowS : 0f) + 0.4f * h;
            GuidePart p = Board(h, innerW, upper + 0.1f * h + lower, Blue);
            float e = p.Edge;
            float px = e + (innerW - panelW) * 0.5f, py = e + 0.35f * h;
            p.Elements.Add(Rect(px, py, panelW, panelH, White));
            FitAt(p, Glyphs.R_WeighStation, Black, px + 0.2f * h, py + 0.15f * h, panelW - 0.4f * h, panelH - 0.3f * h);
            p.Elements.Add(Rect(e, e + upper, innerW, 0.1f * h, White));
            float y = e + upper + 0.1f * h + 0.4f * h;
            p.Add(text, e + (innerW - text.W) * 0.5f, y);
            y += h + 0.35f * h;
            if (hasRow)
            {
                float x = e + (innerW - rowW) * 0.5f;
                if (dist != null)
                {
                    p.Add(dist, x, y + (arrowS - h) * 0.5f);
                    x += dist.W + 1.0f * h;
                }

                if (a.Length > 0)
                    p.Elements.Add(ArrowSquare(GuideTemplates.ArrowOf(a), x, y, arrowS, White));
            }

            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路24 绕行 Fig. 237, 238

        private static readonly GuideChoice[] kDetours =
        {
            new GuideChoice("a", "禁止左转：右转三次绕行（图 237 a）", "No left turn: three rights (Fig. 237 a)"),
            new GuideChoice("b", "禁止左转：直行后右转（图 237 b）", "No left turn: ahead, then round (Fig. 237 b)"),
            new GuideChoice("c", "限高：绕过受限路段（图 237 c）", "Height limit: detour (Fig. 237 c)")
        };

        /// <summary>
        /// 路24 (Fig. 237): the streets in white on blue, the route to take in black ending in an arrow, and the small prohibition sign at the place it avoids (8.6.1). The figure gives no
        /// dimensions; the board is drawn 6 h x 3.75 h with the streets at the places of the figure.
        /// </summary>
        private static GuideBoard Detour(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            float W = 6f * h, H = 3.75f * h, e = 0.01f, lw = e * 0.5f;
            var p = new GuidePart { W = W, H = H, Ground = Blue, BorderColour = Blue, Liner = lw, Border = lw, Radius = 0.3f * h };
            SetSafe(p);
            float iw = W - 2f * e, ih = H - 2f * e;
            float X(float u) => e + iw * u / 100f;
            float Y(float v) => e + ih * v / 100f;
            p.Elements.Add(Rect(X(0f), Y(10f), iw, ih * 0.16f, White));
            p.Elements.Add(Rect(X(0f), Y(62f), iw, ih * 0.16f, White));
            p.Elements.Add(Rect(X(20f), Y(0f), iw * 0.10f, ih, White));
            p.Elements.Add(Rect(X(66f), Y(0f), iw * 0.10f, ih, White));
            string form = s.Get("form");
            float[] route;
            float dirAngle;
            switch (form)
            {
                case "b":
                    route = new[] { 25f, 100f, 25f, 18f, 71f, 18f, 71f, 70f, 9f, 70f };
                    dirAngle = 180f;
                    break;
                case "c":
                    route = new[] { 25f, 100f, 25f, 70f, 71f, 70f, 71f, 18f, 25f, 18f, 25f, 6f };
                    dirAngle = -90f;
                    break;
                default:
                    route = new[] { 25f, 100f, 25f, 70f, 71f, 70f, 71f, 18f, 9f, 18f };
                    dirAngle = 180f;
                    break;
            }

            var line = new List<float>();
            for (int i = 0; i + 1 < route.Length; i += 2)
            {
                line.Add(X(route[i]));
                line.Add(Y(route[i + 1]));
            }

            float t = 0.22f * h, a = 0.6f * h;
            int n = line.Count;
            // The last leg stops where the head starts.
            float lx = line[n - 2], ly = line[n - 1];
            double rad = dirAngle * Math.PI / 180.0;
            line[n - 2] = lx - (float)Math.Cos(rad) * a * 0.6f;
            line[n - 1] = ly - (float)Math.Sin(rad) * a * 0.6f;
            var parts = new List<float[]> { Thick(SquareJoins(line), t), Head(lx, ly, dirAngle, a, t) };
            p.Elements.Add(Figure(parts, Black));

            // The small sign at the place avoided: no left turn (禁23) or the height limit (禁33), a disc 0.9 h across.
            float d = 0.75f * h;
            float cx = form == "b" ? X(12f) : X(25f), cy = form == "b" ? Y(88f) : Y(44f);
            p.Elements.Add(Oval(cx - d * 0.5f, cy - d * 0.5f, d, d, White));
            p.Elements.Add(Oval(cx - d * 0.46f, cy - d * 0.46f, d * 0.92f, d * 0.92f, Red));
            p.Elements.Add(Oval(cx - d * 0.34f, cy - d * 0.34f, d * 0.68f, d * 0.68f, White));
            if (form == "c")
            {
                p.Elements.Add(Ink("4m", cx - LatinWidth("4m", 0.28f * h) * 0.5f, cy - 0.14f * h, LatinWidth("4m", 0.28f * h), 0.28f * h, Black));
            }
            else
            {
                // The left turn arrow, crossed by the red bar.
                var turn = new List<float> { cx + 0.08f * h, cy + 0.25f * h, cx + 0.08f * h, cy - 0.05f * h, cx - 0.12f * h, cy - 0.05f * h };
                p.Elements.Add(Figure(new List<float[]> { Thick(turn, 0.09f * h), Head(cx - 0.25f * h, cy - 0.05f * h, 180f, 0.22f * h, 0.09f * h) }, Black));
                float r = d * 0.36f;
                p.Elements.Add(Bar(cx - r * 0.7f, cy - r * 0.7f, cx + r * 0.7f, cy + r * 0.7f, 0.07f * h, Red));
            }

            return Single(p, h);
        }

        /// <summary>A polyline with each corner cut into a short rounding of a few points, so that a thick line keeps its width round the corner.</summary>
        private static List<float> SquareJoins(List<float> line)
        {
            var r = new List<float>();
            int n = line.Count / 2;
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || i == n - 1)
                {
                    r.Add(line[i * 2]);
                    r.Add(line[i * 2 + 1]);
                    continue;
                }

                float px = line[i * 2 - 2], py = line[i * 2 - 1], x = line[i * 2], y = line[i * 2 + 1], nx = line[i * 2 + 2], ny = line[i * 2 + 3];
                float l1 = (float)Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)), l2 = (float)Math.Sqrt((nx - x) * (nx - x) + (ny - y) * (ny - y));
                float k = Math.Min(0.02f, Math.Min(l1, l2) * 0.3f);
                float ax = x - (x - px) / l1 * k, ay = y - (y - py) / l1 * k, bx = x + (nx - x) / l2 * k, by = y + (ny - y) / l2 * k;
                for (int j = 0; j <= 4; j++)
                {
                    float u = j / 4f, w = 1f - u;
                    r.Add(w * w * ax + 2f * w * u * x + u * u * bx);
                    r.Add(w * w * ay + 2f * w * u * y + u * u * by);
                }
            }

            return r;
        }

        // ------------------------------------------------------------------ 路26 隧道出口距离 Fig. 240; E.189

        private static readonly GuideChoice[] kTunnelBends =
        {
            new GuideChoice("s", "直线", "Straight"),
            new GuideChoice("l", "向左弯", "Bending left"),
            new GuideChoice("r", "向右弯", "Bending right")
        };

        /// <summary>
        /// 路26 (Fig. 240, E.189): a white board with a blue border; the tunnel in white on a blue panel at the side (in the ceiling's form, 1.6 h high: the panel 1.2 h square) or on top (on the
        /// wall, 2.8 h high), and the distance to the exit in blue digits 0.9 h high with "km". The bend of the road in the picture follows the tunnel's (8.6.3).
        /// </summary>
        private static GuideBoard TunnelExit(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string bend = s.Get("bend");
            string glyph = bend == "s" ? Glyphs.R_TunnelStraight : bend == "l" ? Glyphs.R_TunnelLeft : Glyphs.Mirror(Glyphs.R_TunnelLeft);
            string d = s.Get("dist");
            float dh = 0.9f * h;
            GuideBlock dist = Distance(d.Length > 0 ? d : "1", s.Get("unit"), dh, Blue);
            GuidePart p;
            if (s.Is("form", "v"))
            {
                float innerW = Math.Max(1.8f * h, dist.W + 0.4f * h);
                float panelH = 1.0f * h;
                p = Board(h, innerW, panelH + 0.25f * h + dh + 0.25f * h, White, Blue);
                float e = p.Edge;
                GuideElement panel = Rect(e, e, innerW, panelH, Blue);
                panel.RadiusTop = p.InnerRadius;
                panel.Bleed = p.Border;
                p.Elements.Add(panel);
                Draw(new Canvas(p, e, e, innerW, panelH), glyph, White);
                p.Add(dist, e + (innerW - dist.W) * 0.5f, e + panelH + 0.25f * h);
            }
            else
            {
                float panel = 1.2f * h;
                p = Board(h, panel + 0.2f * h + dist.W + 0.2f * h, panel, White, Blue);
                float e = p.Edge, r = p.InnerRadius;
                var pts = new List<float> { e + panel, e, e + panel, e + panel };
                for (int k = 0; k <= 6; k++)
                {
                    double ang = Math.PI * 0.5 + Math.PI * 0.5 * k / 6;
                    pts.Add(e + r + r * (float)Math.Cos(ang));
                    pts.Add(e + panel - r + r * (float)Math.Sin(ang));
                }

                for (int k = 0; k <= 6; k++)
                {
                    double ang = Math.PI + Math.PI * 0.5 * k / 6;
                    pts.Add(e + r + r * (float)Math.Cos(ang));
                    pts.Add(e + r + r * (float)Math.Sin(ang));
                }

                p.Elements.Add(Poly(pts.ToArray(), Blue));
                Draw(new Canvas(p, e, e, panel, panel), glyph, White);
                p.Add(dist, e + panel + 0.2f * h, e + (panel - dh) * 0.5f);
            }

            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路28 里程牌 Fig. 244; E.192

        /// <summary>
        /// 路28 (Fig. 244, E.192, in mm): 480 x 320, blue with a white border 20 mm wide inside a liner of 10, the lower corners cut 130 across and 110 up; the kilometre in white digits 120 high and
        /// under it the road's number plate 60 high. The board itself is drawn as a rectangle with the cut corners shown by the border (the framework has no outline of that shape).
        /// </summary>
        private static GuideBoard KilometrePlate(GuideSpec s, float W, float H, float cutX, float cutY, float liner, float border, float digits, float plateH, GuideColour ground, string km, GuideBlock plate)
        {
            var p = new GuidePart { W = W, H = H, Ground = ground, BorderColour = ground, Liner = 0.004f, Border = 0.004f, Radius = 0.02f };
            SetSafe(p);
            float e = p.Edge;
            float[] Outline(float d)
            {
                float x0 = liner + d, y0 = liner + d, x1 = W - liner - d, y1 = H - liner - d;
                float cx = cutX - d * 0.4f, cy = cutY - d * 0.4f;
                return new[] { x0, y0, x1, y0, x1, y1 - cy, x1 - cx, y1, x0 + cx, y1, x0, y1 - cy };
            }

            float[] outer = Outline(0f), inner = Outline(border);
            p.Elements.Add(Figure(new List<float[]> { Wound(outer), Wound(inner, true) }, White));
            float kw = LatinWidth(km.Length > 0 ? km : "0", digits);
            float maxW = W - 2f * (liner + border) - 0.02f;
            float dw = Math.Min(kw, maxW);
            p.Elements.Add(Ink(km.Length > 0 ? km : "0", (W - dw) * 0.5f, liner + border + 0.02f, dw, digits, White));
            if (plate != null && plate.W > 0f)
            {
                float k = Math.Min(1f, (W - 2f * cutX - 0.01f) / plate.W);
                var scaled = new GuideBlock(plate.W * k, plate.H * k);
                foreach (GuideElement el in plate.Elements)
                {
                    GuideElement m = el.Moved(0f, 0f);
                    m.X *= k; m.Y *= k; m.W *= k; m.H *= k; m.StrokeWidth *= k; m.RadiusTop *= k; m.RadiusBottom *= k; m.Gap *= k;
                    scaled.Add(m);
                }

                p.Add(scaled, (W - scaled.W) * 0.5f, H - liner - border - 0.02f - scaled.H);
            }

            return Single(p, digits);
        }

        // ================================================================== 9.4 expressway information (green)

        private static readonly GuideChoice[] kExpressways =
        {
            new GuideChoice("ge", "国家高速（编号）", "National expressway (numbered)"),
            new GuideChoice("se", "省级高速（编号）", "Provincial expressway (numbered)"),
            new GuideChoice("name", "无编号（白底绿字路名）", "Unnumbered (name, green on white)")
        };

        private static IEnumerable<GuideField> ExpresswayFields(string code, string name)
        {
            yield return GuideField.Choice("road", "道路", "Road", "ge", kExpressways);
            yield return GuideField.Text("code", "编号", "Number", code, 5, s => !s.Is("road", "name"));
            yield return GuideField.Text("sub", "环线序号（右下小字）", "Ring ID (small, low)", "", 2, s => !s.Is("road", "name"));
            yield return GuideField.Text("prov", "省简称", "Province", "皖", 2, s => s.Is("road", "se"));
            yield return GuideField.Text("rname", "路名", "Road name", name, 8, s => s.Is("road", "name"));
        }

        /// <summary>The expressway plate on top of a 9.4 board (Appendix C, table C.1: 100 cm high, 70 at 80 km/h and below).</summary>
        private static GuideBlock TopPlate(GuideSpec s, float h) =>
            RoadPlate(s.Is("road", "se") ? RoadPlateKind.ProvincialExpressway : RoadPlateKind.NationalExpressway, s.Get("code"), s.Get("sub"), s.Get("prov"), ExpresswayPlateSize(h));

        /// <summary>The expressway picture (E.222) in a box 2.7 h x 3 h at (x, y); ended: crossed by the red bar from the lower left to the upper right (E.224).</summary>
        private static void ExpresswayPicture(GuidePart p, float x, float y, float h, bool ended)
        {
            float w = 2.7f * h, hh = 3f * h;
            new Canvas(p, x, y, w, hh).Fit(Glyphs.R_Expressway, White);
            if (ended)
                p.Elements.Add(Bar(x + 0.12f * w, y + hh - 0.04f * hh, x + 0.92f * w, y + 0.04f * hh, 0.22f * h, Red));
        }

        /// <summary>
        /// 路55 to 路60 (Fig. 278 to 283, E.222 to E.227): the start, the end advance and the end of an expressway. A numbered road: the plate of Appendix C 0.4 h from the top, 0.4 h (0.8 h before
        /// a distance) over the picture 3 h high, 0.6 h from the bottom; the board 5.5 h wide, 6.7 h with the distance at the right of the picture. An unnumbered one: a white band at the top with the
        /// name in green characters h high (0.6 h, h, 0.5 h), the picture 0.4 h under it. The end advance gives the distance in digits h high (2 km, 1 km, 500 m: 9.4.2).
        /// </summary>
        private static GuideBoard ExpresswayEnds(GuideSpec s, int band, int kind)
        {
            float h = CharHeight(band);
            bool named = s.Is("road", "name");
            bool ended = kind > 0, advance = kind == 1;
            GuideBlock dist = advance ? Distance(s.Get("dist").Length > 0 ? s.Get("dist") : "2", s.Get("unit"), h, White) : null;
            float innerW = advance ? Math.Max(6.3f * h, 0.8f * h + 2.7f * h + 0.4f * h + dist.W + 0.6f * h) : 5.1f * h;
            GuidePart p;
            float e, picY;
            if (named)
            {
                string name = s.Get("rname");
                GuideBlock line = Line(name, h, Green, 0.3f * h);
                innerW = Math.Max(innerW, line.W + 0.8f * h);
                float bandH = 0.6f * h + h + 0.5f * h;
                p = Board(h, innerW, bandH + 0.4f * h + 3f * h + 0.6f * h, Green);
                e = p.Edge;
                TopBand(p, bandH, White);
                p.Add(line, e + (innerW - line.W) * 0.5f, e + 0.6f * h);
                picY = e + bandH + 0.4f * h;
            }
            else
            {
                GuideBlock plate = TopPlate(s, h);
                innerW = Math.Max(innerW, plate.W + 0.8f * h);
                float gap = advance ? 0.8f * h : 0.4f * h;
                p = Board(h, innerW, 0.4f * h + plate.H + gap + 3f * h + 0.6f * h, Green);
                e = p.Edge;
                p.Add(plate, e + (innerW - plate.W) * 0.5f, e + 0.4f * h);
                picY = e + 0.4f * h + plate.H + gap;
            }

            float picX = advance ? e + 0.8f * h : e + (innerW - 2.7f * h) * 0.5f;
            ExpresswayPicture(p, picX, picY, h, ended);
            if (dist != null)
                p.Add(dist, e + innerW - 0.6f * h - dist.W, picY + 3f * h - h);
            return Single(p, h);
        }

        /// <summary>
        /// 路61 (Fig. 284, E.228): 7.3 h wide and 6 h high; in the white upper part the waves (2.3 h high) and the frequency in green digits h high with its unit, in the green lower band (2 h) the
        /// words in white characters h high, 0.1 h apart.
        /// </summary>
        private static GuideBoard TrafficRadio(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string text = s.Get("text");
            GuideBlock line = Line(text.Length > 0 ? text : "道路交通信息", h, White, 0.1f * h);
            string f = s.Get("freq");
            string unit = s.Get("unit");
            float numberW = LatinWidth(f.Length > 0 ? f : "0", h), unitH = 0.5f * h, unitW = LatinWidth(unit, unitH);
            float innerW = Math.Max(Math.Max(6.9f * h, line.W + 0.8f * h), 1.1f * h + numberW + 0.15f * h + unitW + 0.6f * h);
            float upper = 0.4f * h + 2.3f * h + h + 0.3f * h;
            GuidePart p = Board(h, innerW, upper + 2f * h, Green);
            float e = p.Edge;
            TopBand(p, upper, White);
            new Canvas(p, e + 1.6f * h, e + 0.4f * h, 4.2f * h, 2.3f * h).Fit(Glyphs.R_Radio, Green);
            p.Elements.Add(Ink(f.Length > 0 ? f : "0", e + 1.1f * h, e + 2.7f * h, numberW, h, Green));
            p.Elements.Add(Ink(unit, e + 1.1f * h + numberW + 0.15f * h, e + 2.7f * h + h - unitH, unitW, unitH, Green, GuideAlign.Left, "k"));
            p.Add(line, e + (innerW - line.W) * 0.5f, e + upper + 0.5f * h);
            return Single(p, h);
        }

        /// <summary>
        /// 路62, 路63 (Fig. 285, 286, E.229, E.230): 70 x 48 cm, green with a white border 2 cm inside a liner of 2, the lower corners cut 19 across and 16 up; the kilometre in white digits 18 cm
        /// high, under it the road's number in white letters 8 cm high, or the name of an unnumbered road in characters 8 cm high.
        /// </summary>
        private static GuideBoard ExpresswayKilometre(GuideSpec s)
        {
            string code = s.Get("code");
            var below = new GuideBlock(0f, 0f);
            if (code.Length > 0)
            {
                bool latin = true;
                foreach (char c in code)
                    latin &= !Wide(c);
                below = Line(code, latin ? 0.07f : 0.08f, White, 0.008f);
            }

            return KilometrePlate(s, 0.70f, 0.48f, 0.19f, 0.16f, 0.02f, 0.02f, 0.18f, 0.08f, Green, s.Get("km"), below);
        }

        /// <summary>
        /// 路64 (Fig. 287, E.231): a green disc 10 or 15 cm across (9.4.5.2) with a white edge, the hundred's digit in white 5 (7.5) cm high over a white line, and the kilometre in white digits
        /// 1.8 (2.5) cm high under it.
        /// </summary>
        private static GuideBoard HundredMetre(GuideSpec s)
        {
            float d = s.Is("size", "15") ? 0.15f : 0.10f, k = d / 0.10f;
            var p = new GuidePart { W = d, H = d, Shape = GuidePartShape.Circle, Ground = White, Radius = d * 0.5f };
            float edge = 0.004f * k;
            p.Elements.Add(Oval(edge, edge, d - 2f * edge, d - 2f * edge, Green));
            var safe = new float[96];
            float r = d * 0.5f - edge - 0.0005f;
            for (int i = 0; i < 48; i++)
            {
                double a = 2.0 * Math.PI * i / 48;
                safe[i * 2] = d * 0.5f + r * (float)Math.Cos(a);
                safe[i * 2 + 1] = d * 0.5f + r * (float)Math.Sin(a);
            }

            p.Safe = safe;
            var c = new Canvas(p, 0f, 0f, d, d);
            string hm = s.Get("hm"), km = s.Get("km");
            c.Ink(hm.Length > 0 ? hm : "0", White, 30f, 11f, 40f, 50f);
            c.Rect(White, 14f, 65f, 72f, 3f);
            c.Ink(km.Length > 0 ? km : "0", White, 28f, 72f, 44f, 18f);
            return Single(p, 0.05f * k);
        }

        /// <summary>
        /// 路65 停车领卡 (Fig. 288, E.232): 6 h wide and 7 h high; the picture 4.7 h high over the width: the red booth on the left (1.55 h wide), the ticket coming out of its slot and the orange hand
        /// reaching for it on green; in the white lower band the words in green characters h high, 0.2 h apart.
        /// </summary>
        private static GuideBoard TicketBoard(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string text = s.Get("text");
            GuideBlock line = Line(text.Length > 0 ? text : "停车领卡", h, Green, 0.2f * h);
            float innerW = Math.Max(5.6f * h, line.W + 1.4f * h), pic = 4.7f * h;
            GuidePart p = Board(h, innerW, pic + 0.5f * h + h + 0.6f * h, Green);
            float e = p.Edge;
            GuideElement booth = Rect(e, e, 1.75f * h, pic, Red);
            booth.RadiusTop = p.InnerRadius;
            p.Elements.Add(booth);
            // The slot crosses the edge of the booth, the ticket comes out of it on the green, the hand reaches from the right edge (E.232, read off the drawing).
            new Canvas(p, e + 1.35f * h, e + 1.03f * h, 1.3f * h, 1.3f * h).Fit(Glyphs.R_Ticket, White);
            new Canvas(p, e + innerW - 2.87f * h, e + 2.46f * h, 2.87f * h, 1.47f * h).Fit(Glyphs.R_TicketHand, GuideColour.Orange);
            BottomBand(p, e + pic, White);
            p.Add(line, e + (innerW - line.W) * 0.5f, e + pic + 0.5f * h);
            return Single(p, h);
        }

        /// <summary>
        /// 路66 特殊天气建议速度 (Fig. 289, E.233): 4 h wide and 6 h high; the picture 3 h square 0.4 h from the top (the grey cloud, the white road edges, the dashed middle line and the white
        /// half-round distance marks at its side), and under it a yellow box 2.6 h x 1.2 h with rounded corners holding the speed in black digits 0.8 h high and "km/h".
        /// </summary>
        private static GuideBoard WeatherSpeed(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            GuidePart p = Board(h, 3.6f * h, 0.4f * h + 3f * h + 0.5f * h + 1.2f * h + 0.5f * h, Green);
            float e = p.Edge, px = e + 0.3f * h, py = e + 0.4f * h, ps = 3f * h;
            var c = new Canvas(p, px, py, ps, ps);
            c.Pic(Glyphs.R_FogRoad, White);
            int marks = s.Is("marks", "2") ? 2 : 1;
            // The half discs at the side of the road (E.233): one far ahead and, in Fig. 289 b, a second nearer.
            if (marks == 2)
                c.Fit(Glyphs.R_HalfDisc, White, 78f, 28f, 8f, 18f);
            c.Fit(Glyphs.R_HalfDisc, White, 80f, 60f, 10f, 20f);
            c.Pic(Glyphs.R_Cloud, GuideColour.Gray, 0f, 0f, 100f, 75f);
            float bx = e + 0.5f * h, by = py + ps + 0.5f * h, bw = 2.6f * h, bh = 1.2f * h;
            p.Elements.Add(Rect(bx, by, bw, bh, Yellow, 0.2f * h));
            string v = s.Get("limit");
            if (v.Length == 0)
                v = "50";
            float dh = 0.8f * h, uh = 0.35f * h;
            float vw = LatinWidth(v, dh), uw = LatinWidth("km/h", uh);
            float x = bx + (bw - vw - 0.15f * h - uw) * 0.5f;
            p.Elements.Add(Ink(v, x, by + (bh - dh) * 0.5f, vw, dh, Black));
            p.Elements.Add(Ink("km/h", x + vw + 0.15f * h, by + (bh + dh) * 0.5f - uh, uw, uh, Black, GuideAlign.Left, "k"));
            return Single(p, h);
        }

        /// <summary>
        /// 路67 紧急电话 (Fig. 291, E.234, in mm): 250 x 400, green, a white panel 160 x 180 in the middle with the black handset.
        /// </summary>
        private static GuideBoard EmergencyPhone()
        {
            GuidePart p = Plain(0.25f, 0.40f, 0.008f, Green);
            p.Radius = 0.03f;
            p.Elements.Add(Rect(0.045f, 0.11f, 0.16f, 0.18f, White));
            new Canvas(p, 0.06f, 0.125f, 0.13f, 0.15f).Fit(Glyphs.R_Phone, Black);
            return Facility(p);
        }

        /// <summary>
        /// 路68 电话位置指示 (Fig. 292, E.235, in mm): 750 x 250 (900 pointing both ways), green; the handset 120 wide, the distance in white digits 160 high and its unit; the end that points to the
        /// telephone is drawn as a white chevron inside a rectangular board (the board's own pointed outline cannot be drawn by the framework).
        /// </summary>
        private static GuideBoard PhoneDirection(GuideSpec s)
        {
            string form = s.Get("form");
            bool left = form != "r", right = form != "l";
            float H = 0.25f;
            string d0 = s.Get("dist");
            float distW0 = Distance(d0.Length > 0 ? d0 : "400", s.Get("unit"), 0.16f, White).W;
            float W = Math.Max(form == "b" ? 0.90f : 0.75f, 0.014f + (left ? 0.117f : 0.03f) + 0.12f + 0.03f + distW0 + (right ? 0.117f : 0.03f) + 0.014f);
            GuidePart p = Plain(W, H, 0.007f, Green);
            p.Radius = 0.02f;
            float e = p.Edge, top = e + 0.012f, bottom = H - e - 0.012f, mid = H * 0.5f, t = 0.03f, len = 0.075f;
            void Chevron(float x0, float sgn)
            {
                float tip = x0 + sgn * len;
                p.Elements.Add(Poly(new[] { x0 + sgn * len, top, x0 + sgn * (len - t * 1.3f), top, x0, mid, x0 + sgn * (len - t * 1.3f), bottom, x0 + sgn * len, bottom, x0 + sgn * t * 1.3f, mid }, White));
            }

            if (left) Chevron(e + 0.012f, 1f);
            if (right) Chevron(W - e - 0.012f, -1f);
            string d = s.Get("dist");
            GuideBlock dist = Distance(d.Length > 0 ? d : "400", s.Get("unit"), 0.16f, White);
            float phoneW = 0.12f, gap = 0.03f;
            float x0 = left ? e + 0.012f + len + 0.03f : e + 0.03f;
            float x1 = right ? W - e - 0.012f - len - 0.03f : W - e - 0.03f;
            float total = phoneW + gap + dist.W;
            float x = x0 + Math.Max(0f, (x1 - x0 - total) * 0.5f);
            bool phoneFirst = form != "r";
            if (phoneFirst)
            {
                new Canvas(p, x, mid - 0.08f, phoneW, 0.16f).Fit(Glyphs.Mirror(Glyphs.R_Phone), White);
                p.Add(dist, x + phoneW + gap, mid - 0.08f);
            }
            else
            {
                p.Add(dist, x, mid - 0.08f);
                new Canvas(p, x + dist.W + gap, mid - 0.08f, phoneW, 0.16f).Fit(Glyphs.R_Phone, White);
            }

            return Single(p, 0.16f);
        }

        /// <summary>
        /// 路69 救援电话 (Fig. 293, E.236): 4.3 h wide and 2.8 h high; 救援 in characters 0.5 h high 0.5 h from the top, the number in digits h high 0.3 h under them, the handset 1.2 h x 1.5 h at
        /// the right.
        /// </summary>
        private static GuideBoard RescuePhone(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string number = s.Get("number");
            if (number.Length == 0)
                number = "12122";
            float nw = LatinWidth(number, h);
            float innerW = Math.Max(3.9f * h, 0.4f * h + nw + 0.2f * h + 1.2f * h + 0.4f * h);
            GuidePart p = Board(h, innerW, 2.4f * h, Green);
            float e = p.Edge;
            p.Elements.Add(Cells("救援", e + 0.6f * h, e + 0.3f * h, 0.5f * h, White, 0.1f * h));
            p.Elements.Add(Ink(number, e + 0.4f * h, e + 1.1f * h, nw, h, White));
            new Canvas(p, e + innerW - 0.4f * h - 1.2f * h, e + 0.4f * h, 1.2f * h, 1.5f * h).Fit(Glyphs.R_PhoneTurned, White);
            return Single(p, h);
        }

        /// <summary>
        /// 路70 (Fig. 294, E.237): 8 h wide and 6.9 h high, yellow above (4.6 h: the panel 3 h high 0.8 h from the top with 电子收费 in black characters 0.5 h high, ETC in black letters h high
        /// under them and the toll booth at the right) and green below (1.9 h) with 收费站 in white characters h high and the distance in white digits at the right, or the words alone in the
        /// middle at the station. The board's liner is green and its border white.
        /// </summary>
        private static GuideBoard TollStation(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string name = s.Get("name"), d = s.Get("dist");
            GuideBlock line = Line(name.Length > 0 ? name : "收费站", h, White, 0.2f * h);
            GuideBlock dist = d.Length > 0 ? Distance(d, s.Get("unit"), h, White) : null;
            float innerW = Math.Max(7.6f * h, 0.6f * h + line.W + (dist != null ? 0.8f * h + dist.W : 0f) + 0.6f * h);
            float upper = 4.6f * h, lower = 1.9f * h;
            GuidePart p = Board(h, innerW, upper + lower, Green);
            float e = p.Edge;
            GuideElement yellow = Rect(e, e, innerW, upper, Yellow);
            yellow.RadiusTop = p.InnerRadius;
            p.Elements.Add(yellow);
            float px = e + (innerW - 6.4f * h) * 0.5f, py = e + 0.8f * h;
            p.Elements.Add(Cells("电子收费", px + 0.2f * h, py + 0.5f * h, 0.5f * h, Black, 0.2f * h));
            float etcW = LatinWidth("ETC", h);
            p.Elements.Add(Ink("ETC", px + 0.2f * h, py + 1.5f * h, etcW, h, Black));
            new Canvas(p, px + 3.4f * h, py, 3f * h, 3f * h).Fit(Glyphs.R_TollBooth, Black);
            float ly = e + upper + (lower - h) * 0.5f;
            if (dist != null)
            {
                p.Add(line, e + 0.6f * h, ly);
                p.Add(dist, e + innerW - 0.6f * h - dist.W, ly);
            }
            else
            {
                p.Add(line, e + (innerW - line.W) * 0.5f, ly);
            }

            return Single(p, h);
        }

        /// <summary>
        /// 路71 (Fig. 295, E.238): 4.5 h wide and 6.7 h high, green; the toll lanes (up to five: 9.5.4) with white lines between them at the top and the yellow arrows of the ETC lanes, the
        /// left edge of the road straight, the right edge narrowing from the plaza; 电子收费 in yellow characters 0.5 h high and ETC in yellow letters 1.3 h high at the bottom left.
        /// </summary>
        private static GuideBoard EtcLanes(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            int n = Math.Max(2, Math.Min(5, (int)s.Number("lanes", 4)));
            int m = Math.Max(1, Math.Min(n, (int)s.Number("etc", 3)));
            float lane = 0.75f * h;
            float innerW = Math.Max(4.1f * h, 0.3f * h + n * lane + 0.3f * h);
            GuidePart p = Board(h, innerW, 6.3f * h, Green);
            float e = p.Edge, x0 = e + 0.3f * h, top = e + 0.25f * h, w = 0.12f * h;
            float ty = e + 6.3f * h - 0.3f * h - 1.3f * h, bottom = ty - 0.15f * h;
            float xr = x0 + n * lane;
            // The left edge and the right edge (straight at first, then narrowing to the left).
            p.Elements.Add(Rect(x0 - w * 0.5f, top, w, bottom - top, White));
            float narrowTo = Math.Min(xr, x0 + 2.5f * h);
            var edge = narrowTo < xr - 0.01f
                ? new List<float> { xr, top, xr, top + 2.2f * h, narrowTo, bottom - 0.6f * h, narrowTo, bottom }
                : new List<float> { xr, top, xr, bottom };
            p.Elements.Add(Figure(new List<float[]> { Thick(edge, w) }, White));
            for (int i = 0; i < n; i++)
            {
                float cx = x0 + (i + 0.5f) * lane;
                if (i > 0)
                    p.Elements.Add(Rect(x0 + i * lane - w * 0.5f, top + 0.55f * h, w, 0.9f * h, White));
                if (i < m)
                {
                    // The first lane's arrow runs down the plaza, the others stop above it (Fig. 295).
                    float len = i == 0 ? bottom - top - 0.6f * h : 2.0f * h;
                    p.Elements.Add(ArrowIn(GuideArrow.Up, cx - 0.25f * h, top + 0.05f * h, 0.5f * h, len, Yellow));
                }
            }

            p.Elements.Add(Cells("电子收费", x0 + 0.2f * h, bottom - 0.5f * h, 0.5f * h, Yellow, 0.05f * h));
            p.Elements.Add(Ink("ETC", x0, ty, LatinWidth("ETC", 1.3f * h), 1.3f * h, Yellow));
            return Single(p, h);
        }

        /// <summary>
        /// 路72 to 路74 (Fig. 296, E.239 to E.241), fixed over the toll lanes: the ETC lane, yellow with a black border, 电子收费 in black characters 0.5 h high over ETC in black letters 1.1 h high
        /// (5 h x 2.5 h); the manual lane, yellow with a black border and the toll booth 1.5 h high (5 h x 2.5 h); the green lane, green with 绿色通道 in white characters h high (5.5 h x 2.2 h).
        /// </summary>
        private static GuideBoard TollLane(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string kind = s.Get("kind");
            GuidePart p;
            if (kind == "green")
            {
                GuideBlock line = Line("绿色通道", h, White, 0.2f * h);
                p = Board(h, Math.Max(5.1f * h, line.W + 0.8f * h), 1.8f * h, Green);
                p.Add(line, p.Edge + (p.W - 2f * p.Edge - line.W) * 0.5f, p.Edge + 0.4f * h);
                return Single(p, h);
            }

            p = Board(h, 4.6f * h, 2.1f * h, Yellow, Black);
            float e = p.Edge;
            if (kind == "manual")
            {
                new Canvas(p, e + 1.55f * h, e + 0.3f * h, 1.5f * h, 1.5f * h).Fit(Glyphs.R_TollBooth, Black);
            }
            else
            {
                float cw = CellWidth(4, 0.5f * h, 0.1f * h), etcW = LatinWidth("ETC", 1.1f * h);
                p.Elements.Add(Cells("电子收费", e + (4.6f * h - cw) * 0.5f, e + 0.15f * h, 0.5f * h, Black, 0.1f * h));
                p.Elements.Add(Ink("ETC", e + (4.6f * h - etcW) * 0.5f, e + 0.8f * h, etcW, 1.1f * h, Black));
            }

            return Single(p, h);
        }

        private static readonly GuideChoice[] kAreaForms =
        {
            new GuideChoice("adv", "预告（名称 + 距离）", "Advance (name and distance)"),
            new GuideChoice("dir", "基准点（名称 + 箭头）", "At the point (name and arrow)"),
            new GuideChoice("entry", "入口处（竖排）", "At the entrance (upright)")
        };

        /// <summary>
        /// 路75 服务区预告 (Fig. 298, E.242) and 路76 停车区预告 (Fig. 299, E.243): in a white upper part the symbols in green 1.7 h high (the P, the fuel pump, the spanner and the fork and knife, or
        /// the bed of a service area with lodging; the P and the cup of a parking area), in the green lower part the name in white characters h high with the distance or the arrow at the right; 7 h
        /// (6 h) x 5 h. At the entrance a green board with the name, 服务区 or 停车区 and the arrow under one another, 0.4 h apart.
        /// </summary>
        private static GuideBoard AreaBoard(GuideSpec s, int band, bool service)
        {
            float h = CharHeight(band);
            string name = s.Get("name"), form = s.Get("form");
            GuideBlock line = Line(name, h, White, 0.2f * h);
            GuideArrow arrow = GuideTemplates.ArrowOf(s.Get("arrow"));
            GuidePart p;
            float e;
            if (form == "entry")
            {
                GuideBlock kind = Line(service ? "服务区" : "停车区", h, White, 0.2f * h);
                float innerW = Math.Max(Math.Max(line.W, kind.W) + 0.8f * h, 3f * h);
                p = Board(h, innerW, 0.4f * h + h + 0.4f * h + h + 0.4f * h + h + 0.4f * h, Green);
                e = p.Edge;
                p.Add(line, e + (innerW - line.W) * 0.5f, e + 0.4f * h);
                p.Add(kind, e + (innerW - kind.W) * 0.5f, e + 1.8f * h);
                p.Elements.Add(ArrowSquare(arrow, e + (innerW - h) * 0.5f, e + 3.2f * h, h, White));
                return Single(p, h);
            }

            GuideBlock dist = form == "adv" ? Distance(s.Get("dist").Length > 0 ? s.Get("dist") : "2", s.Get("unit"), h, White) : null;
            float rightW = dist != null ? dist.W : h;
            float minW = service ? 6.6f * h : 5.6f * h;
            float iw = Math.Max(minW, 0.4f * h + line.W + 0.8f * h + rightW + 0.4f * h);
            float upper = 0.5f * h + 1.7f * h + 0.5f * h, lower = 0.5f * h + h + 0.5f * h;
            p = Board(h, iw, upper + lower, Green);
            e = p.Edge;
            TopBand(p, upper, White);
            float sy = e + 0.5f * h, sh = 1.7f * h;
            var symbols = new List<(string glyph, float w)> { (Glyphs.R_P, 1.25f * h) };
            if (service)
            {
                symbols.Add((Glyphs.R_Fuel, 1.1f * h));
                symbols.Add((Glyphs.R_Wrench, 0.55f * h));
                symbols.Add(s.Flag("bed") ? (Glyphs.R_Bed, 1.9f * h) : (Glyphs.R_ForkKnife, 1.4f * h));
            }
            else
            {
                symbols.Add((Glyphs.R_Cup, 2.2f * h));
            }

            float total = 0f;
            foreach ((string glyph, float w) in symbols)
                total += w;
            float gap = Math.Min(0.6f * h, (iw - 0.8f * h - total) / (symbols.Count - 1));
            float x = e + (iw - total - gap * (symbols.Count - 1)) * 0.5f;
            foreach ((string glyph, float w) in symbols)
            {
                new Canvas(p, x, sy, w, sh).Fit(glyph, Green);
                x += w + gap;
            }

            float ly = e + upper + 0.5f * h;
            p.Add(line, e + 0.4f * h, ly);
            if (dist != null)
                p.Add(dist, e + iw - 0.4f * h - dist.W, ly);
            else
                p.Elements.Add(ArrowSquare(arrow, e + iw - 0.4f * h - h, ly, h, White));
            return Single(p, h);
        }

        private static readonly GuideChoice[] kClimbForms =
        {
            new GuideChoice("a", "预告（大型车辆 爬坡车道）", "Advance (heavy vehicles, climbing lane)"),
            new GuideChoice("b", "起点（车道图形）", "At the start (lane picture)"),
            new GuideChoice("c", "途中（向下箭头）", "Along it (arrow down)"),
            new GuideChoice("d", "结束", "End")
        };

        /// <summary>
        /// 路77 爬坡车道 (Fig. 300, E.244): green; a) 大型车辆 in characters 0.6 h high over 爬坡车道 in characters h high (5.8 h x 3.5 h); b) the same at the right of the picture of the lane added
        /// at the right (2.5 h, 7.8 h x 3.5 h); c) 爬坡车道 over the arrow down (h); d) 爬坡车道 over 结束 (0.6 h) at the right.
        /// </summary>
        private static GuideBoard ClimbingLane(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            string form = s.Get("form");
            GuideBlock main = Line("爬坡车道", h, White, 0.4f * h);
            GuidePart p;
            float e;
            switch (form)
            {
                case "b":
                {
                    GuideBlock small = Line("大型车辆", 0.6f * h, White, 0.3f * h);
                    float innerW = 0.4f * h + 2.1f * h + 0.6f * h + main.W + 0.6f * h;
                    p = Board(h, innerW, 3.1f * h, Green);
                    e = p.Edge;
                    new Canvas(p, e + 0.4f * h, e + 0.3f * h, 2.1f * h, 2.5f * h).Fit(Glyphs.R_ClimbLane, White);
                    p.Add(small, e + innerW - 0.6f * h - small.W, e + 0.4f * h);
                    p.Add(main, e + innerW - 0.6f * h - main.W, e + 1.6f * h);
                    break;
                }

                case "c":
                    p = Board(h, main.W + 1.2f * h, 3.1f * h, Green);
                    e = p.Edge;
                    p.Add(main, e + 0.6f * h, e + 0.6f * h);
                    p.Elements.Add(ArrowIn(GuideArrow.Down, e + (main.W + 1.2f * h) * 0.5f - 0.5f * h, e + 1.9f * h, h, 0.8f * h, White));
                    break;
                case "d":
                {
                    GuideBlock end = Line("结束", 0.6f * h, White, 0.3f * h);
                    p = Board(h, main.W + 1.2f * h, 3.1f * h, Green);
                    e = p.Edge;
                    p.Add(main, e + 0.6f * h, e + 0.6f * h);
                    p.Add(end, e + 0.6f * h + main.W - end.W, e + 2.0f * h);
                    break;
                }

                default:
                {
                    GuideBlock small = Line("大型车辆", 0.6f * h, White, 0.3f * h);
                    p = Board(h, main.W + 1.2f * h, 3.1f * h, Green);
                    e = p.Edge;
                    p.Add(small, e + 0.6f * h, e + 0.4f * h);
                    p.Add(main, e + 0.6f * h, e + 1.6f * h);
                    break;
                }
            }

            return Single(p, h);
        }

        // ================================================================== the catalogue

        private static string[] W(params string[] words) => words;

        public static IEnumerable<GuideTemplate> All()
        {
            // 8.3 route guidance.
            yield return StandardSigns.Make("gb.guide8.01", "路1", "交叉路口预告（图形式）", "Intersection advance sign (diagram)", G, "jclkyg", W("交叉路口", "预告", "十字", "丁字", "图形", "junction"),
                Junction, new[]
                {
                    GuideField.Choice("form", "路口形式", "Junction", "x", kJunctions),
                    Place("to1", "直行信息一", "Ahead 1", "G2"), Place("to2", "直行信息二（可空）", "Ahead 2 (may be empty)", ""),
                    Place("tl1", "左转信息一", "Left 1", "南京路"), Place("tl2", "左转信息二（可空）", "Left 2 (may be empty)", ""),
                    Place("tr1", "右转信息一", "Right 1", "东北路"), Place("tr2", "右转信息二（可空）", "Right 2 (may be empty)", ""),
                    GuideField.Text("xroad", "交叉道路名", "Crossing road", "", 6),
                    DirField("北")
                });

            // 路1+ (0.12.78): the next one to three junctions of the road on one board, each at-grade or an interchange (RoadGuide.Multi.cs).
            yield return MultiJunctionTemplate();

            yield return StandardSigns.Make("gb.guide8.02", "路2", "环岛（图形式）", "Roundabout advance sign (diagram)", G, "hd", W("环岛", "预告", "交叉路口", "roundabout"),
                Roundabout, new[]
                {
                    Place("tnw1", "左前方信息一", "Ahead left 1", "白涧"), Place("tnw2", "左前方信息二（可空）", "Ahead left 2", ""),
                    Place("tne1", "右前方信息一", "Ahead right 1", "S304"), Place("tne2", "右前方信息二（可空）", "Ahead right 2", "三河"),
                    Place("tw1", "左侧信息一", "Left 1", "G102"), Place("tw2", "左侧信息二（可空）", "Left 2", "唐山"),
                    Place("te1", "右侧信息一", "Right 1", "双塔"), Place("te2", "右侧信息二（可空）", "Right 2", ""),
                    DirField("")
                });

            // Fig. 203 c (8.1.7 b, 8.1.5): the example of the meaning of the informations of a junction drawn as a roundabout; it has no code of its own.
            yield return StandardSigns.Make("gb.guide8.203c", "图203c", "环形交叉路口（箭杆路号，旅游景区）", "Roundabout junction with the crossing road's number, tourist site (Fig. 203 c)", G, "hxjckyg",
                W("环形", "环岛", "交叉路口", "箭杆", "路号", "旅游景区", "图203", "roundabout", "tourist"),
                RingJunction, new[]
                {
                    Place("to1", "直行信息一", "Ahead 1", "科技公园"), Place("to2", "直行信息二（可空）", "Ahead 2 (may be empty)", ""),
                    GuideField.Flag("tour", "直行信息一是景区名", "Ahead 1 is a tourist site", true),
                    Place("tl1", "左侧信息一", "Left 1", "G108"), Place("tl2", "左侧信息二（可空）", "Left 2 (may be empty)", ""),
                    Place("tr1", "右侧信息一", "Right 1", "G109"), Place("tr2", "右侧信息二（可空）", "Right 2 (may be empty)", ""),
                    GuideField.Text("xroad", "交叉道路编号或名称（箭杆上）", "Crossing road (on the roads)", "S209", 6),
                    DirField("北")
                });

            yield return StandardSigns.Make("gb.guide8.03", "路3", "交叉路口预告（堆叠式）", "Intersection advance sign (stacked)", G, "jclkyg", W("交叉路口", "预告", "堆叠", "stacked"),
                Stacked, new[]
                {
                    Place("to1", "直行信息一", "Ahead 1", "学府路"), Place("to2", "直行信息二（可空）", "Ahead 2", ""),
                    Place("tl1", "左转信息一", "Left 1", "公滨路"), Place("tl2", "左转信息二（可空）", "Left 2", ""),
                    Place("tr1", "右转信息一", "Right 1", "黄河路"), Place("tr2", "右转信息二（可空）", "Right 2", "")
                });

            yield return StandardSigns.Make("gb.guide8.04", "路4", "交叉路口预告（车道式）", "Intersection advance sign (lanes)", G, "jclkyg", W("交叉路口", "预告", "车道", "lanes"),
                Lanes, LaneFields("0", "东莞", "s", "深圳", "r", "", "r"));

            // Fig. H.1 a (the example of a city crossing): the lanes of the road as a strip of arrows, no names: left turn, two ahead, right turn.
            yield return StandardSigns.Make("gb.guide8.04b", "路4", "交叉路口预告（车道式，只有箭头）", "Intersection advance sign (lanes, arrows only; Fig. H.1 a)", G, "jclkyg", W("交叉路口", "预告", "车道", "箭头", "导向", "lanes", "arrows"),
                Lanes, LaneFields("4", "", "l", "", "s", "", "s", "", "r"));

            // The lane choice board of the cities (0.12.78): LanePick.cs.
            foreach (GuideTemplate lanePick in LanePickSigns.All())
                yield return lanePick;

            yield return StandardSigns.Make("gb.guide8.05", "路5", "道路编号", "Road number", G, "dlbh", W("道路编号", "国道", "省道", "县道", "乡道", "编号"),
                RoadNumber, new[] { GuideField.Text("code", "编号（G、S、X、Y）", "Number (G, S, X, Y)", "G105", 6) });

            yield return StandardSigns.Make("gb.guide8.06", "路6", "路名", "Road name", G, "lm", W("路名", "街道", "竖排", "road name"),
                (s, band) => s.Is("layout", "v") ? UprightNameBoard(s.Get("name"), CharHeight(band)) : NameBoard(s.Get("name"), CharHeight(band)),
                new[]
                {
                    GuideField.Text("name", "路名", "Name", "南京路", 8),
                    GuideField.Choice("layout", "排列", "Layout", "h", new[] { new GuideChoice("h", "横排（一般）", "Across (as a rule)"), new GuideChoice("v", "竖排（特殊情况，一个路名）", "Upright (special cases, one name)") })
                });

            yield return StandardSigns.Make("gb.guide8.07", "路7", "道路名称方向", "Road name and direction", G, "dlmcfx", W("路名", "方向", "道路名称"),
                NameDirection, new[]
                {
                    GuideField.Text("name", "路名或编号", "Name or number", "福宁路", 8),
                    GuideField.Choice("form", "形式", "Form", "r", kNameDirs)
                });

            yield return StandardSigns.Make("gb.guide8.08", "路8", "地点距离", "Place distance", G, "ddjl", W("地点", "距离", "里程", "distance"),
                PlaceDistance, new[]
                {
                    Place("to1", "地点一（最近）", "Place 1 (nearest)", "如皋"), GuideField.Digits("d1", "距离一", "Distance 1", "5", 4),
                    Place("to2", "地点二", "Place 2", "海安"), GuideField.Digits("d2", "距离二", "Distance 2", "40", 4),
                    Place("to3", "地点三（可空）", "Place 3 (may be empty)", "盐城"), GuideField.Digits("d3", "距离三", "Distance 3", "105", 4),
                    GuideField.Choice("unit", "单位", "Unit", "km", kUnits),
                    GuideField.Choice("ground", "道路", "Road", "blue", new[] { new GuideChoice("blue", "一般道路（蓝底，路8）", "Ordinary road (blue, 路8)"), new GuideChoice("green", "绿底（快速路请用路38）", "Green (use 路38 on expressways)") })
                });

            // 8.4 place guidance.
            yield return StandardSigns.Make("gb.guide8.09", "路9", "地名", "Place name", G, "dm", W("地名", "村", "镇", "place"),
                (s, band) => NameBoard(s.Get("name"), CharHeight(band)), new[] { GuideField.Text("name", "地名", "Name", "上清水", 8) });

            yield return StandardSigns.Make("gb.guide8.10", "路10", "行政区划分界", "Administrative boundary", G, "xzqhfj", W("分界", "界", "行政区划", "boundary"),
                (s, band) => NameBoard(s.Get("name"), CharHeight(band)), new[] { GuideField.Text("name", "文字（含“界”）", "Text (with 界)", "北京界", 8) });

            yield return StandardSigns.Make("gb.guide8.11", "路11", "道路管理分界", "Road maintenance boundary", G, "dlglfj", W("分界", "道班", "养护", "boundary"),
                (s, band) => NameBoard(s.Get("name"), Math.Max(0.15f, 0.5f * CharHeight(band))), new[] { GuideField.Text("name", "文字", "Text", "平谷道班", 8) });

            yield return StandardSigns.Make("gb.guide8.12", "路12", "地点识别", "Place identification", G, "ddsb", W("地点识别", "急救", "飞机场", "加油站", "充电站", "地铁"),
                PlaceSymbol, new[]
                {
                    GuideField.Choice("kind", "场所", "Place", "aid", kPlaces),
                    GuideField.Choice("side", "机头方向（出口方向）", "Nose to (side of the exit)", "l", new[] { new GuideChoice("l", "左上", "Upper left"), new GuideChoice("r", "右上", "Upper right") }, s => s.Is("kind", "air"))
                });

            // 8.5 road-side facilities.
            yield return StandardSigns.Make("gb.guide8.13", "路13", "停车场（区）", "Parking", G, "tcc", W("停车场", "停车", "P", "parking"),
                Parking, new[] { GuideField.Choice("form", "形式", "Form", "open", new[] { new GuideChoice("open", "露天停车场", "Open car park"), new GuideChoice("in", "室内停车场", "Indoor car park") }) });

            yield return StandardSigns.Make("gb.guide8.14", "路14", "错车道", "Passing place", G, "ccd", W("错车道", "让车", "passing"), PassingPlace);
            yield return StandardSigns.Make("gb.guide8.15", "路15", "港湾式紧急停车带", "Emergency lay-by", G, "gwsjjtcd", W("紧急停车带", "港湾", "停车带", "lay-by"), Layby);
            yield return StandardSigns.Make("gb.guide8.16", "路16", "人行天桥", "Footbridge", G, "rxtq", W("天桥", "人行", "footbridge"), band => Crossing(false));
            yield return StandardSigns.Make("gb.guide8.17", "路17", "人行地下通道", "Pedestrian underpass", G, "rxdxtd", W("地下通道", "人行", "underpass"), band => Crossing(true));
            yield return StandardSigns.Make("gb.guide8.18", "路18", "无障碍设施", "Accessible facility", G, "wzass", W("无障碍", "轮椅", "accessible"), band => SymbolSquare(band, Glyphs.R_Wheelchair));
            yield return StandardSigns.Make("gb.guide8.19", "路19", "服务站", "Service station", G, "fwz", W("服务站", "厕所", "饮水", "service"),
                (s, band) => ServiceBoard(s, band, "station"), new[]
                {
                    GuideField.Text("name", "名称", "Name", "服务站", 6),
                    GuideField.Choice("arrow", "箭头（预告时不设）", "Arrow (none on an advance sign)", "b", kOptArrows)
                });
            yield return StandardSigns.Make("gb.guide8.20", "路20", "停车点", "Rest point", G, "tcd", W("停车点", "厕所", "停车"),
                (s, band) => ServiceBoard(s, band, "stop"), new[]
                {
                    GuideField.Text("name", "名称", "Name", "停车点", 6),
                    GuideField.Choice("arrow", "箭头（预告时不设）", "Arrow (none on an advance sign)", "b", kOptArrows)
                });
            yield return StandardSigns.Make("gb.guide8.21", "路21", "观景台", "Scenic outlook", G, "gjt", W("观景台", "风景", "outlook"),
                (s, band) => ServiceBoard(s, band, "view"), new[]
                {
                    GuideField.Text("name", "名称", "Name", "观景台", 6),
                    GuideField.Choice("arrow", "箭头（预告时不设）", "Arrow (none on an advance sign)", "b", kOptArrows)
                });
            yield return StandardSigns.Make("gb.guide8.22", "路22", "应急避难设施（场所）", "Emergency shelter", G, "yjbnss", W("应急", "避难", "shelter"), band => SymbolSquare(band, Glyphs.R_Refuge));
            yield return StandardSigns.Make("gb.guide8.23", "路23", "超限检测站", "Weigh station", G, "cxjcz", W("超限", "检测站", "治超", "称重"),
                WeighStation, new[]
                {
                    GuideField.Text("name", "名称", "Name", "超限检测站", 6),
                    GuideField.Digits("dist", "距离（入口处为空）", "Distance (empty at the entrance)", "500", 4),
                    GuideField.Choice("unit", "单位", "Unit", "m", kUnits, s => s.Get("dist").Length > 0),
                    GuideField.Choice("arrow", "箭头", "Arrow", "b", kOptArrows)
                });

            // 8.6 other information.
            yield return StandardSigns.Make("gb.guide8.24", "路24", "绕行", "Detour", G, "rx", W("绕行", "禁止左转", "限高", "detour"),
                Detour, new[] { GuideField.Choice("form", "绕行方式", "Detour", "a", kDetours) });
            yield return StandardSigns.Make("gb.guide8.25", "路25", "此路不通", "No through road", G, "clbt", W("此路不通", "死胡同", "dead end"), DeadEnd);
            yield return StandardSigns.Make("gb.guide8.26", "路26", "隧道出口距离", "Tunnel exit distance", G, "sdckjl", W("隧道", "出口", "距离", "tunnel"),
                TunnelExit, new[]
                {
                    GuideField.Digits("dist", "距离", "Distance", "2", 4),
                    GuideField.Choice("unit", "单位", "Unit", "km", kUnits),
                    GuideField.Choice("bend", "隧道线形", "Tunnel's line", "l", kTunnelBends),
                    GuideField.Choice("form", "设置位置", "Placed", "h", new[] { new GuideChoice("h", "隧道顶部（横）", "Ceiling (wide)"), new GuideChoice("v", "侧壁（竖）", "Wall (tall)") })
                });

            yield return StandardSigns.Make("gb.guide8.27", "路27", "方向标志", "Direction mark", G, "fxbz", W("方向", "东", "南", "西", "北", "direction"),
                DirectionSign, new[]
                {
                    GuideField.Choice("dir", "方向", "Direction", "西", new[] { kDirs[1], kDirs[2], kDirs[3], kDirs[4] }),
                    GuideField.Choice("form", "设置", "Placed", "out", new[] { new GuideChoice("out", "版面外（蓝底白字）", "Apart (white on blue)"), new GuideChoice("in", "版面中（白底蓝字）", "In a board (blue on white)") })
                });

            yield return StandardSigns.Make("gb.guide8.28", "路28", "里程牌", "Kilometre plate", G, "lcp", W("里程", "里程牌", "公里", "kilometre"),
                (s, band) =>
                {
                    string code = s.Get("code");
                    Match m = kCode.Match(code);
                    GuideBlock plate = m.Success ? OrdinaryPlate(char.ToUpperInvariant(m.Groups[2].Value[0]) + m.Groups[3].Value, char.ToUpperInvariant(m.Groups[2].Value[0]), 0.06f) : null;
                    return KilometrePlate(s, 0.48f, 0.32f, 0.13f, 0.11f, 0.01f, 0.02f, 0.12f, 0.06f, Blue, s.Get("km"), plate);
                }, new[]
                {
                    GuideField.Digits("km", "里程（km）", "Kilometre", "220", 4),
                    GuideField.Text("code", "道路编号", "Road number", "G312", 5)
                });

            // 9.4 expressway information.
            yield return StandardSigns.Make("gb.guide8.55", "路55·56", "高速公路起点", "Expressway begin", G, "gsglqd", W("起点", "高速", "快速路", "start"),
                (s, band) => ExpresswayEnds(s, band, 0), new List<GuideField>(ExpresswayFields("G15", "水官高速")).ToArray());
            yield return StandardSigns.Make("gb.guide8.57", "路57·58", "终点预告", "Expressway end advance", G, "zdyg", W("终点", "预告", "高速", "end"),
                (s, band) => ExpresswayEnds(s, band, 1), new List<GuideField>(ExpresswayFields("G15", "水官高速"))
                {
                    GuideField.Digits("dist", "距离", "Distance", "2", 4),
                    GuideField.Choice("unit", "单位", "Unit", "km", kUnits)
                }.ToArray());
            yield return StandardSigns.Make("gb.guide8.59", "路59·60", "终点", "Expressway end", G, "zd", W("终点", "高速", "end"),
                (s, band) => ExpresswayEnds(s, band, 2), new List<GuideField>(ExpresswayFields("G15", "水官高速")).ToArray());
            yield return StandardSigns.Make("gb.guide8.61", "路61", "道路交通信息", "Traffic information", G, "dljtxx", W("交通信息", "广播", "频率", "radio"),
                TrafficRadio, new[]
                {
                    GuideField.Digits("freq", "频率", "Frequency", "1620", 5),
                    GuideField.Choice("unit", "单位", "Unit", "kHz", new[] { new GuideChoice("kHz", "kHz", "kHz"), new GuideChoice("MHz", "MHz", "MHz") }),
                    GuideField.Text("text", "文字", "Text", "道路交通信息", 7)
                });
            yield return StandardSigns.Make("gb.guide8.62", "路62·63", "里程牌（高速公路）", "Kilometre plate (expressway)", G, "lcp", W("里程", "里程牌", "高速", "kilometre"),
                (s, band) => ExpresswayKilometre(s), new[]
                {
                    GuideField.Digits("km", "里程（km）", "Kilometre", "2658", 4),
                    GuideField.Text("code", "编号或路名（无编号时）", "Number, or the name if none", "G15", 5)
                });
            yield return StandardSigns.Make("gb.guide8.64", "路64", "百米牌", "Hundred-metre plate", G, "bmp", W("百米", "百米牌", "里程"),
                (s, band) => HundredMetre(s), new[]
                {
                    GuideField.Digits("hm", "百米数", "Hundreds", "8", 1),
                    GuideField.Digits("km", "公里数", "Kilometre", "238", 4),
                    GuideField.Choice("size", "直径", "Diameter", "10", new[] { new GuideChoice("10", "10 cm", "10 cm"), new GuideChoice("15", "15 cm", "15 cm") })
                });
            yield return StandardSigns.Make("gb.guide8.65", "路65", "停车领卡", "Take a ticket", G, "tclk", W("领卡", "收费站", "入口", "ticket"),
                TicketBoard, new[] { GuideField.Text("text", "文字", "Text", "停车领卡", 6) });
            yield return StandardSigns.Make("gb.guide8.66", "路66", "特殊天气建议速度", "Advisory speed in bad weather", G, "tstqjysd", W("特殊天气", "建议速度", "雾", "车距"),
                WeatherSpeed, new[]
                {
                    GuideField.Digits("limit", "建议速度（km/h）", "Advisory speed (km/h)", "50", 3),
                    GuideField.Choice("marks", "车距确认线", "Distance marks", "1", new[] { new GuideChoice("1", "一个半圆（图 289 a）", "One half disc (Fig. 289 a)"), new GuideChoice("2", "两个半圆（图 289 b）", "Two half discs (Fig. 289 b)") })
                });

            // 9.5 expressway facilities.
            yield return StandardSigns.Make("gb.guide8.67", "路67", "紧急电话", "Emergency telephone", G, "jjdh", W("紧急电话", "电话", "phone"), band => EmergencyPhone());
            yield return StandardSigns.Make("gb.guide8.68", "路68", "电话位置指示", "Telephone direction", G, "dhwzzs", W("电话", "紧急电话", "距离", "phone"),
                (s, band) => PhoneDirection(s), new[]
                {
                    GuideField.Choice("form", "方向", "Direction", "l", new[] { new GuideChoice("l", "向左（图 292 a）", "Left (Fig. 292 a)"), new GuideChoice("r", "向右（图 292 b）", "Right (Fig. 292 b)"), new GuideChoice("b", "两侧（图 292 c）", "Both ways (Fig. 292 c)") }),
                    GuideField.Digits("dist", "距离", "Distance", "400", 4),
                    GuideField.Choice("unit", "单位", "Unit", "m", kUnits)
                });
            yield return StandardSigns.Make("gb.guide8.69", "路69", "救援电话", "Rescue telephone", G, "jydh", W("救援", "电话", "12122", "rescue"),
                RescuePhone, new[] { GuideField.Text("number", "电话号码", "Number", "12122", 8) });
            yield return StandardSigns.Make("gb.guide8.70", "路70", "收费站预告及收费站", "Toll station", G, "sfzyg", W("收费站", "ETC", "预告", "toll"),
                TollStation, new[]
                {
                    GuideField.Text("name", "文字", "Text", "收费站", 6),
                    GuideField.Digits("dist", "距离（收费站处为空）", "Distance (empty at the station)", "1", 4),
                    GuideField.Choice("unit", "单位", "Unit", "km", kUnits, s => s.Get("dist").Length > 0)
                });
            yield return StandardSigns.Make("gb.guide8.71", "路71", "ETC车道指引", "ETC lane guidance", G, "etccdzy", W("ETC", "电子收费", "车道", "收费站"),
                EtcLanes, new[]
                {
                    GuideField.Choice("lanes", "收费车道数", "Toll lanes", "4", new[] { new GuideChoice("2", "2", "2"), new GuideChoice("3", "3", "3"), new GuideChoice("4", "4", "4"), new GuideChoice("5", "5（及以上）", "5 (or more)") }),
                    GuideField.Choice("etc", "ETC 车道数（自左）", "ETC lanes (from the left)", "3", new[] { new GuideChoice("1", "1", "1"), new GuideChoice("2", "2", "2"), new GuideChoice("3", "3", "3"), new GuideChoice("4", "4", "4"), new GuideChoice("5", "5", "5") })
                });
            yield return StandardSigns.Make("gb.guide8.72", "路72·73·74", "收费车道", "Toll lane", G, "sfcd", W("ETC", "人工收费", "绿色通道", "收费车道"),
                TollLane, new[]
                {
                    GuideField.Choice("kind", "车道", "Lane", "etc", new[] { new GuideChoice("etc", "电子不停车收费（ETC）车道（路72）", "ETC lane (路72)"), new GuideChoice("manual", "人工收费车道（路73）", "Manual toll lane (路73)"), new GuideChoice("green", "绿色通道（路74）", "Green lane (路74)") })
                });
            yield return StandardSigns.Make("gb.guide8.75", "路75", "服务区预告", "Service area advance", G, "fwqyg", W("服务区", "加油", "餐饮", "预告", "service"),
                (s, band) => AreaBoard(s, band, true), new[]
                {
                    GuideField.Text("name", "服务区名称", "Name", "天目湖", 6),
                    GuideField.Choice("form", "形式", "Form", "adv", kAreaForms),
                    GuideField.Digits("dist", "距离", "Distance", "2", 4, s => s.Is("form", "adv")),
                    GuideField.Choice("unit", "单位", "Unit", "km", kUnits, s => s.Is("form", "adv")),
                    GuideField.Choice("arrow", "箭头", "Arrow", "b", GuideTemplates.kArrows, s => !s.Is("form", "adv")),
                    GuideField.Flag("bed", "提供住宿（床代替刀叉）", "Lodging (bed for knife and fork)", false, s => !s.Is("form", "entry"))
                });
            yield return StandardSigns.Make("gb.guide8.76", "路76", "停车区预告", "Rest area", G, "tcqyg", W("停车区", "预告", "parking"),
                (s, band) => AreaBoard(s, band, false), new[]
                {
                    GuideField.Text("name", "停车区名称", "Name", "浪网", 6),
                    GuideField.Choice("form", "形式", "Form", "adv", kAreaForms),
                    GuideField.Digits("dist", "距离", "Distance", "1", 4, s => s.Is("form", "adv")),
                    GuideField.Choice("unit", "单位", "Unit", "km", kUnits, s => s.Is("form", "adv")),
                    GuideField.Choice("arrow", "箭头", "Arrow", "b", GuideTemplates.kArrows, s => !s.Is("form", "adv"))
                });
            yield return StandardSigns.Make("gb.guide8.77", "路77", "爬坡车道", "Climbing lane", G, "ppcd", W("爬坡", "车道", "大型车辆", "climbing"),
                ClimbingLane, new[] { GuideField.Choice("form", "形式", "Form", "a", kClimbForms) });
        }
    }
}
