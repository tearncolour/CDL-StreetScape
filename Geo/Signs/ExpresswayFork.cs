using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;
using static CDLC.TS.Geo.Signs.RoadGuideSigns;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// Signs of the expressway guide chapter of GB 5768.2-2022: the exit advance and exit direction boards drawn with a figure of the lanes (9.3.8.3, Fig. 271, drawings E.216 to E.218):
    /// 路49 the advance board of a road fork (道路分岔预告), 路50 the advance board of an interchange with two exits (双出口枢纽式互通立体交叉的出口预告), 路51 the exit direction board of an
    /// interchange whose exit ramp has two lanes (出口匝道为2条车道枢纽式互通立体交叉的出口方向). Boards laid out in the character height h of Table 3; edge (liner and border) 0.2 h and corner
    /// radius 0.4 h as Fig. 3; the road numbers through RoadGuideSigns.Info (8.1.5: G15, S49, G1511 become the plates of Appendix C, table C.1: 100 cm high, 70 cm at 80 km/h and below).
    /// The figures of the standard all have the exit on the right; the "side" setting mirrors the arrangement for an exit on the left (not drawn by the standard).
    /// </summary>
    public static class ExpresswayForkSigns
    {
        private const string G = "guide";
        private const GuideColour Green = GuideColour.Green, White = GuideColour.White, Black = GuideColour.Black, Yellow = GuideColour.Yellow;

        private static readonly string[] kBandIds = { "100", "80", "60", "30" };

        /// <summary>The digit height D of Table 5 for the band (the exit number plate, E.207).</summary>
        private static float DigitHeight(int band) => GuideTemplates.BandOf(kBandIds[SignStyles.Band(band)]).D;

        // ================================================================== drawing the arrows

        /// <summary>A centre line drawn step by step: straight on, or round a circular arc. Headings in degrees on the screen (y down): 0 to the right, -90 up.</summary>
        private sealed class Pen
        {
            public readonly List<float> Points = new List<float>();
            public float X, Y, Heading;

            public Pen(float x, float y, float heading)
            {
                X = x;
                Y = y;
                Heading = heading;
                Points.Add(x);
                Points.Add(y);
            }

            private static float Cos(float deg) => (float)Math.Cos(deg * Math.PI / 180.0);

            private static float Sin(float deg) => (float)Math.Sin(deg * Math.PI / 180.0);

            public Pen Line(float length)
            {
                X += length * Cos(Heading);
                Y += length * Sin(Heading);
                Points.Add(X);
                Points.Add(Y);
                return this;
            }

            /// <summary>An arc of radius r that turns the heading by turn degrees: positive to the right (clockwise on the screen), negative to the left.</summary>
            public Pen Arc(float r, float turn)
            {
                float side = turn > 0f ? 90f : -90f;
                float cx = X + r * Cos(Heading + side), cy = Y + r * Sin(Heading + side);
                int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(turn) / 4f));
                float start = Heading;
                for (int k = 1; k <= n; k++)
                {
                    float hd = start + turn * k / n;
                    X = cx + r * Cos(hd - side);
                    Y = cy + r * Sin(hd - side);
                    Points.Add(X);
                    Points.Add(Y);
                }

                Heading = start + turn;
                return this;
            }
        }

        /// <summary>A contour wound as a solid (the winding RoadGuideSigns uses: solids join under the painter's non-zero rule).</summary>
        private static float[] Wound(float[] c)
        {
            float a = 0f;
            int n = c.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += c[i * 2] * c[j * 2 + 1] - c[j * 2] * c[i * 2 + 1];
            }

            if (a <= 0f)
                return c;
            var r = new float[c.Length];
            for (int k = 0; k < n; k++)
            {
                r[k * 2] = c[(n - 1 - k) * 2];
                r[k * 2 + 1] = c[(n - 1 - k) * 2 + 1];
            }

            return r;
        }

        /// <summary>A band of width w along a polyline (the shafts): each vertex moved by half the width along the normal of the line through its neighbours; square ends.</summary>
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

        /// <summary>
        /// The head of the arrow of E.166 (as RoadGuideSigns' ArrowShape): a wide and a long, the full width 0.58 a behind the tip, the sides straight back to a, the back parallel to the front
        /// down to the shaft (t wide), with a short stub into the shaft so that the two join. Pointing along the heading from the tip (tx, ty).
        /// </summary>
        private static float[] Head(float tx, float ty, float heading, float a, float t)
        {
            float j = -a + 0.58f * (a - t), stub = j - 0.06f * a;
            float[] p =
            {
                0f, 0f, -0.58f * a, -0.5f * a, -a, -0.5f * a, j, -t * 0.5f, stub, -t * 0.5f,
                stub, t * 0.5f, j, t * 0.5f, -a, 0.5f * a, -0.58f * a, 0.5f * a
            };
            double r = heading * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            var q = new float[p.Length];
            for (int i = 0; i < p.Length; i += 2)
            {
                q[i] = tx + p[i] * c - p[i + 1] * s;
                q[i + 1] = ty + p[i] * s + p[i + 1] * c;
            }

            return Wound(q);
        }

        /// <summary>Ends a pen's shaft with a head: the pen stands at the middle of the head's back; the shaft runs on into the head and the tip is a further along.</summary>
        private static void Finish(List<float[]> into, Pen pen, float a, float t)
        {
            float bx = pen.X, by = pen.Y;
            double r = pen.Heading * Math.PI / 180.0;
            float ux = (float)Math.Cos(r), uy = (float)Math.Sin(r);
            pen.Line(0.45f * a);
            into.Add(Thick(pen.Points, t));
            into.Add(Head(bx + ux * a, by + uy * a, pen.Heading, a, t));
        }

        // The figures in units of h, from the foot of their stem (up is negative y). E.216 and E.218 give their boxes in h; the strokes, radii and angles inside the boxes are measured off
        // the drawings rendered at 5250 px a page (estimated to about 0.05 h and 3 degrees).

        /// <summary>
        /// The bent arrow of E.216 (the left lanes' arrow) and of the yellow box (E.216, E.218), in a box 1.3 h x 1.5 h: a stem 0.26 h wide up from the foot 0.45 h, bent on a radius of 0.7 h
        /// through 67 degrees to the side (dir -1 left, +1 right), the head 0.76 h pointing 23 degrees above the horizontal.
        /// </summary>
        private static List<float[]> Bend(int dir)
        {
            var c = new List<float[]>();
            Finish(c, new Pen(0f, 0f, -90f).Line(0.45f).Arc(0.70f, dir * 67f).Line(0.10f), 0.76f, 0.26f);
            return c;
        }

        /// <summary>
        /// The forked arrow of E.216 (the shared lane of a fork), in a box 2 h x 2.6 h: a stem 0.27 h wide up the middle 0.9 h, two branches bent on 1.7 h through 45 degrees to each side, heads
        /// 0.68 h. The low board at the fork (Fig. 273 c) has the same arrow with a short stem (0.3 h): as wide, 2 h high.
        /// </summary>
        private static List<float[]> ForkY(float stem)
        {
            var c = new List<float[]> { Thick(new List<float> { 0f, 0f, 0f, -stem }, 0.27f) };
            foreach (int dir in new[] { -1, 1 })
                Finish(c, new Pen(0f, -stem + 0.1f, -90f).Arc(1.70f, dir * 45f), 0.68f, 0.27f);
            return c;
        }

        /// <summary>
        /// The arrow of the shared lane of E.218 (ahead, and from its foot a branch to the right), in a box 2.5 h x 2.7 h: the arrow ahead of E.166 (head 1 h, stem 0.3 h) 2.7 h long, the branch
        /// 0.27 h wide from the foot bent on 1.42 h through 68 degrees, 0.36 h straight, the head 0.78 h pointing 22 degrees above the horizontal, its tip 0.96 h under the top.
        /// </summary>
        private static List<float[]> AheadBend()
        {
            var c = new List<float[]>();
            Finish(c, new Pen(0f, 0f, -90f).Line(1.7f), 1.0f, 0.30f);
            Finish(c, new Pen(0.08f, 0f, -90f).Arc(1.42f, 68f).Line(0.36f), 0.78f, 0.27f);
            return c;
        }

        private static void Bounds(List<float[]> contours, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue;
            x1 = y1 = float.MinValue;
            foreach (float[] k in contours)
            {
                for (int i = 0; i + 1 < k.Length; i += 2)
                {
                    x0 = Math.Min(x0, k[i]);
                    x1 = Math.Max(x1, k[i]);
                    y0 = Math.Min(y0, k[i + 1]);
                    y1 = Math.Max(y1, k[i + 1]);
                }
            }
        }

        /// <summary>The width a figure takes when it is fitted (evenly) into a box w x hh.</summary>
        private static float FittedWidth(List<float[]> shape, float w, float hh)
        {
            Bounds(shape, out float x0, out float y0, out float x1, out float y1);
            float k = Math.Min(w / (x1 - x0), hh / (y1 - y0));
            return (x1 - x0) * k;
        }

        // ================================================================== placing things on a board, mirrored for an exit on the left

        /// <summary>
        /// The inside of a board in the frame of the standard's figures (the exit on the right); with mirror the whole is turned round left for right, block by block (a plate, a name, a
        /// distance keep their own reading order) and the arrows with it.
        /// </summary>
        private sealed class Sheet
        {
            private readonly GuidePart m_Part;
            private readonly bool m_Mirror;
            private readonly float m_W, m_E;

            public Sheet(GuidePart part, float innerW, bool mirror)
            {
                m_Part = part;
                m_W = innerW;
                m_E = part.Edge;
                m_Mirror = mirror;
            }

            private float X(float x, float w) => m_E + (m_Mirror ? m_W - x - w : x);

            public void Block(GuideBlock b, float x, float y)
            {
                if (b != null && b.W > 0f)
                    m_Part.Add(b, X(x, b.W), m_E + y);
            }

            public void Rect(float x, float y, float w, float hh, GuideColour colour) => m_Part.Elements.Add(GuideKit.Rect(X(x, w), m_E + y, w, hh, colour));

            /// <summary>The direction mark (E.216, E.218: a white square 1 h on a side; the character 0.8 h in the ground's green, as 路35's of ExpresswayNameSigns, estimated from Fig. 271).</summary>
            public void Mark(string dir, float x, float y, float h)
            {
                if (string.IsNullOrEmpty(dir))
                    return;
                Rect(x, y, h, h, White);
                m_Part.Elements.Add(Cells(dir.Substring(0, 1), X(x, h) + 0.1f * h, m_E + y + 0.1f * h, 0.8f * h, Green, 0f));
            }

            /// <summary>An arrow of E.166 in a box (RoadGuideSigns.ArrowIn), turned round with the board.</summary>
            public void Arrow(GuideArrow dir, float x, float y, float w, float hh, GuideColour colour)
            {
                if (m_Mirror)
                    dir = dir == GuideArrow.UpLeft ? GuideArrow.UpRight : dir == GuideArrow.UpRight ? GuideArrow.UpLeft : dir == GuideArrow.Left ? GuideArrow.Right : dir == GuideArrow.Right ? GuideArrow.Left : dir;
                m_Part.Elements.Add(ArrowIn(dir, X(x, w), m_E + y, w, hh, colour));
            }

            /// <summary>A figure of units (h, from its own origin) evenly scaled into the box w x hh at (x, y) and aligned in it (ax, ay: 0 left or top, 0.5 middle, 1 right or bottom).</summary>
            public void Fit(List<float[]> shape, float x, float y, float w, float hh, float ax, float ay, GuideColour colour)
            {
                Bounds(shape, out float x0, out float y0, out float x1, out float y1);
                float k = Math.Min(w / (x1 - x0), hh / (y1 - y0));
                float ox = x + (w - (x1 - x0) * k) * ax - x0 * k, oy = y + (hh - (y1 - y0) * k) * ay - y0 * k;
                Figure(shape, k, ox, oy, colour);
            }

            /// <summary>A figure of contours put at x = ox + k u, y = oy + k v of the inside, as one element of the sign's figure (Art: the tests keep it inside the board).</summary>
            public void Figure(List<float[]> shape, float k, float ox, float oy, GuideColour colour)
            {
                var contours = new List<float[]>();
                float bx0 = float.MaxValue, by0 = float.MaxValue, bx1 = float.MinValue, by1 = float.MinValue;
                foreach (float[] c in shape)
                {
                    var m = new float[c.Length];
                    for (int i = 0; i + 1 < c.Length; i += 2)
                    {
                        float x = ox + c[i] * k;
                        m[i] = m_E + (m_Mirror ? m_W - x : x);
                        m[i + 1] = m_E + oy + c[i + 1] * k;
                        bx0 = Math.Min(bx0, m[i]);
                        bx1 = Math.Max(bx1, m[i]);
                        by0 = Math.Min(by0, m[i + 1]);
                        by1 = Math.Max(by1, m[i + 1]);
                    }

                    contours.Add(Wound(m));
                }

                m_Part.Elements.Add(new GuideElement { Kind = GuideElementKind.Path, Contours = contours, Colour = colour, X = bx0, Y = by0, W = bx1 - bx0, H = by1 - by0, Art = true });
            }
        }

        /// <summary>One or two place names in a row, h high, 0.6 h apart (8.1.4 c, E.208 to E.213).</summary>
        private static GuideBlock Names(string a, string b, float h)
        {
            GuideBlock first = Line(a, h, White, NameGapK * h), second = Line(b, h, White, NameGapK * h);
            if (first.W <= 0f)
                return second;
            if (second.W <= 0f)
                return first;
            var row = new GuideBlock(first.W + 0.6f * h + second.W, h);
            row.Add(first, 0f, 0f);
            row.Add(second, first.W + 0.6f * h, 0f);
            return row;
        }

        /// <summary>The exit number plate (路41, E.207) on top of the board at the side of the exit (9.3.7), 左出口 for an exit on the left (Fig. 263 c).</summary>
        private static void AddExitPlate(GuideBoard board, GuideSpec s, int band, float h, bool left)
        {
            string number = s.Get("xnum");
            if (number.Length == 0)
                return;
            GuidePart plate = ExitNumberPlate(DigitHeight(band), number, s.Get("xsuf"), left, 0f, 0f);
            plate.X = left ? 0f : board.Main.W - plate.W;
            plate.Y = -plate.H - 0.05f * h;
            board.Parts.Add(plate);
        }

        // ================================================================== 路49 and 路51: two directions side by side and an arrow for each lane (E.216, E.218)

        /// <summary>What 路49 and 路51 do differently (E.216 against E.218).</summary>
        private sealed class ForkStyle
        {
            /// <summary>路49 is an advance board: the distance under the exit's names (E.216); without one it is the board at the base point (Fig. 273 c), lower.</summary>
            public bool Distance;

            /// <summary>The space between the exit's names and the exit lanes' arrows: 1.0 h (E.216), 0.7 h (E.218).</summary>
            public float GapRight;

            /// <summary>The exit's number plate centred over its names with the mark beside it (E.218, Fig. 271 c); otherwise plate and mark together centred (Fig. 271 a).</summary>
            public bool PlateCentred;
        }

        private static readonly ForkStyle kFork49 = new ForkStyle { Distance = true, GapRight = 1.0f, PlateCentred = false };
        private static readonly ForkStyle kFork51 = new ForkStyle { Distance = false, GapRight = 0.7f, PlateCentred = true };

        /// <summary>The space between the arrows of two lanes of the same kind (not drawn by the standard: estimated, as the lanes' arrows of 路4).</summary>
        private const float LanePitchGap = 0.8f;

        /// <summary>
        /// 路49 (E.216, Fig. 271 a) and 路51 (E.218, Fig. 271 c). The board has two halves parted by a white line 0.1 h wide from the inside's top down to the foot of the road numbers: the main
        /// line's direction on the left, the exit's on the right (9.3.8.4 a, b; mirrored for an exit on the left). In each half: the direction mark (a white square 1 h) and the road's number
        /// plate (100 (70) cm, 0.4 h from the top; the mark 0.2 h before it on the left, 0.3 h after it on the right, its foot 0.35 h above the plate's), the place names 0.4 h under the plate.
        /// Under them a row of arrows, one for each lane (Fig. 273 notes: the arrows over the lanes' centres): the lanes that go only the main line's way at the left (E.216: bent to the left,
        /// 1.3 h x 1.5 h, 0.4 h from the inside's edge; E.218: ahead, 1 h x 1.5 h), the shared lane under the parting line (E.216: forked, 2 h x 2.6 h from 0.1 h under the names' top; E.218:
        /// ahead with a branch to the right, 2.5 h x 2.7 h from the names' top), the lanes that go only to the exit at the right (bent to the right, 1.3 h x 1.5 h, on a yellow box 1.9 h x
        /// 1.85 h 0.2 h inside the edges: a through lane, 9.3.8.2). The arrows stand on a line 1.7 h under the names (E.216, E.218), 0.4 h above the inside's bottom; 路49's distance on it under
        /// the exit's names (digits h). Across: the names 1.2 h (E.218: 1 h) after the left lanes' arrows, 1.9 h (2 h) before the shared arrow, 1.6 h (0.7 h) after it, 1 h (0.7 h) before the
        /// yellow box; the distance 1.7 h after the shared arrow and 1.2 h before the box. With two characters a name these give 16.1 h x 5.84 h (路49) and 15.0 h x 5.84 h
        /// (路51) at 100 km/h; E.216 says 16.3 h and E.218 15.2 h, with their 125 cm plates drawn 2.07 h wide (1.92 h at h = 65 cm). E.216 puts the exit's plate and mark 0.4 h right of the
        /// middle of its names; here they are centred over the names as in the printed Fig. 271 a.
        /// </summary>
        private static GuideBoard ForkBoard(GuideSpec s, int band, ForkStyle st)
        {
            float h = CharHeight(band), m = 0.4f * h;
            bool mirror = s.Is("side", "l");

            // ---- the pieces
            GuideBlock mPlate = Info(s.Get("code"), h, Green), ePlate = Info(s.Get("code2"), h, Green);
            string mDir = s.Get("mdir"), eDir = s.Get("edir");
            GuideBlock mNames = Names(s.Get("tom1"), s.Get("tom2"), h), eNames = Names(s.Get("toe1"), s.Get("toe2"), h);
            GuideBlock dist = st.Distance && s.Get("dist").Length > 0 ? Distance(s.Get("dist"), s.Get("unit"), h, White) : null;
            bool compact = st.Distance && dist == null;
            float markS = h;

            // The groups of the top row: [mark] 0.2 h [plate] on the left, [plate] 0.3 h [mark] on the right.
            bool mMark = mDir.Length > 0, eMark = eDir.Length > 0;
            float mGroupW = mPlate.W + (mMark ? markS + (mPlate.W > 0f ? 0.2f * h : 0f) : 0f);
            float eGroupW = ePlate.W + (eMark ? markS + (ePlate.W > 0f ? 0.3f * h : 0f) : 0f);
            float T = Math.Max(Math.Max(mPlate.H, ePlate.H), mMark || eMark ? markS : 0f);

            // ---- the arrows
            string la = s.Get("la"), sa = s.Get("sa"), ra = s.Get("ra");
            int nl = Math.Max(0, Math.Min(3, (int)s.Number("nl", 1))), nr = Math.Max(0, Math.Min(2, (int)s.Number("nr", 1)));
            float awL = la == "a" ? h : 1.3f * h, gL = la == "a" ? 1.0f * h : 1.2f * h;
            float awR = 1.3f * h;
            bool yellow = ra == "yc" || ra == "yd";

            // ---- the rows
            float namesTop = T > 0f ? m + T + 0.4f * h : m;
            float bottomY = namesTop + h + (compact ? 1.0f : 1.7f) * h;
            float innerH = bottomY + m;

            // The shared lane's arrow: its box, and the space before and after it.
            List<float[]> shared = sa == "y" ? ForkY(compact ? 0.3f : 0.9f) : sa == "ar" ? AheadBend() : null;
            float shTop = sa == "y" && !compact ? namesTop + 0.1f * h : namesTop;
            float shH = bottomY - shTop;
            float shBoxW = sa == "y" ? 2.0f * h : 2.5f * h;
            float SW = shared != null ? FittedWidth(shared, shBoxW, shH) : 0f;
            float gCL = shared == null ? 0.8f * h : sa == "y" ? 1.9f * h : 2.0f * h;
            float gCR = shared == null ? 0.8f * h : sa == "y" ? 1.6f * h : 0.7f * h;
            float gDist = shared == null ? 0.8f * h : 1.7f * h;
            const float cG = 0.6f;

            // ---- across: the left half
            float leftRight = nl > 0 ? m + nl * awL + (nl - 1) * LanePitchGap * h : 0f;
            float cxL = nl > 0 ? leftRight + gL : m;
            if (mNames.W > 0f)
                cxL += mNames.W * 0.5f;
            if (mGroupW > 0f)
                cxL = Math.Max(cxL, m + mGroupW * 0.5f);

            float centre = m + SW * 0.5f;
            if (mNames.W > 0f)
                centre = Math.Max(centre, cxL + mNames.W * 0.5f + gCL + SW * 0.5f);
            if (mGroupW > 0f)
                centre = Math.Max(centre, cxL + mGroupW * 0.5f + cG * h);
            if (nl > 0)
                centre = Math.Max(centre, leftRight + 0.6f * h + SW * 0.5f);

            // ---- the right half
            float eTopW = st.PlateCentred && ePlate.W > 0f ? ePlate.W : eGroupW;
            float cxR = centre + SW * 0.5f + 0.6f * h;
            if (eNames.W > 0f)
                cxR = Math.Max(cxR, centre + SW * 0.5f + gCR + eNames.W * 0.5f);
            if (dist != null)
                cxR = Math.Max(cxR, centre + SW * 0.5f + gDist + dist.W * 0.5f);
            if (eTopW > 0f)
                cxR = Math.Max(cxR, centre + cG * h + eTopW * 0.5f);

            float rightBlockW = nr <= 0 ? 0f : yellow ? 0.4f * h + nr * awR + (nr - 1) * LanePitchGap * h + 0.2f * h : nr * awR + (nr - 1) * LanePitchGap * h + m;
            float afterNames = nr > 0 ? st.GapRight * h + rightBlockW : m;
            float afterDist = nr > 0 ? 1.2f * h + rightBlockW : m;
            float eTopRight = st.PlateCentred && ePlate.W > 0f ? cxR + ePlate.W * 0.5f + (eMark ? 0.3f * h + markS : 0f) : cxR + eGroupW * 0.5f;

            float innerW = Math.Max(centre + SW * 0.5f + m, cxR + m);
            if (nr > 0)
                innerW = Math.Max(innerW, centre + SW * 0.5f + 0.6f * h + rightBlockW);
            if (eNames.W > 0f)
                innerW = Math.Max(innerW, cxR + eNames.W * 0.5f + afterNames);
            if (dist != null)
                innerW = Math.Max(innerW, cxR + dist.W * 0.5f + afterDist);
            if (eTopW > 0f)
                innerW = Math.Max(innerW, eTopRight + m);

            // ---- the board
            GuidePart p = Board(h, innerW, innerH, Green);
            GuideBoard board = Single(p, h);
            var sh = new Sheet(p, innerW, mirror);

            // The top row: plates 0.4 h from the top; a mark's foot 0.35 h above its plate's (E.216: the mark 0.7 h from the top beside a plate of 1.65 h).
            float MarkY(GuideBlock plate) => plate.H > 1.05f * h ? Math.Max(m, m + plate.H - 0.35f * h - markS) : m;
            if (mGroupW > 0f)
            {
                float x = cxL - mGroupW * 0.5f;
                if (mMark)
                {
                    sh.Mark(mDir, x, MarkY(mPlate), h);
                    x += markS + 0.2f * h;
                }

                sh.Block(mPlate, x, m);
            }

            if (eGroupW > 0f)
            {
                float x = st.PlateCentred && ePlate.W > 0f ? cxR - ePlate.W * 0.5f : cxR - eGroupW * 0.5f;
                sh.Block(ePlate, x, m);
                if (eMark)
                    sh.Mark(eDir, ePlate.W > 0f ? x + ePlate.W + 0.3f * h : x, MarkY(ePlate), h);
            }

            // The parting line, from the inside's top to the foot of the top row (E.216, E.218), when both halves say something.
            bool leftSays = mGroupW > 0f || mNames.W > 0f, rightSays = eGroupW > 0f || eNames.W > 0f || dist != null;
            if (T > 0f && leftSays && rightSays)
                sh.Rect(centre - 0.05f * h, 0f, 0.1f * h, m + T, White);

            sh.Block(mNames, cxL - mNames.W * 0.5f, namesTop);
            sh.Block(eNames, cxR - eNames.W * 0.5f, namesTop);
            if (dist != null)
                sh.Block(dist, cxR - dist.W * 0.5f, bottomY - h);

            // The lanes' arrows, all standing on the line bottomY.
            float arrowTop = bottomY - 1.5f * h;
            for (int i = 0; i < nl; i++)
            {
                float x = m + i * (awL + LanePitchGap * h);
                if (la == "a")
                    sh.Arrow(GuideArrow.Up, x, arrowTop, awL, 1.5f * h, White);
                else if (la == "d")
                    sh.Arrow(GuideArrow.UpLeft, x, arrowTop, awL, 1.5f * h, White);
                else
                    sh.Fit(Bend(-1), x, arrowTop, awL, 1.5f * h, 1f, 1f, White);
            }

            if (shared != null)
                sh.Fit(shared, centre - SW * 0.5f, shTop, SW, shH, 0.5f, 1f, White);

            if (nr > 0)
            {
                float x0 = innerW - rightBlockW;
                GuideColour ink = yellow ? Black : White;
                if (yellow)
                {
                    // The yellow box: 1.85 h high, 0.2 h inside the right and the bottom of the inside (E.216, E.218); the arrows 0.4 h from its left.
                    sh.Rect(x0, bottomY + 0.2f * h - 1.85f * h, rightBlockW - 0.2f * h, 1.85f * h, Yellow);
                    x0 += 0.4f * h;
                }

                for (int i = 0; i < nr; i++)
                {
                    float x = x0 + i * (awR + LanePitchGap * h);
                    if (ra == "d" || ra == "yd")
                        sh.Arrow(GuideArrow.UpRight, x, arrowTop, awR, 1.5f * h, ink);
                    else
                        sh.Fit(Bend(1), x, arrowTop, awR, 1.5f * h, 0f, 1f, ink);
                }
            }

            AddExitPlate(board, s, band, h, mirror);
            return board;
        }

        // ================================================================== 路50: two exits, one after the other (E.217)

        /// <summary>
        /// 路50 (E.217, Fig. 271 b; 9.3.8.3: the main line's information is left out): at the left the figure 2.2 h wide, from 0.4 h under the inside's top to 0.6 h above its bottom: the arrow
        /// ahead of the main line (E.166, head 1 h, stem 0.33 h, 0.5 h from the figure's left) and a branch from it for each exit, bent to the right into a head of 0.68 h at the figure's right
        /// edge, 23 degrees above the horizontal, at the height of its exit's row (bent on a radius of h through 67 degrees, the shaft 0.25 h: measured off E.217). Then 0.3 h, the exits'
        /// number plates (100 (70) cm, the first 0.5 h from the top, 0.4 h apart), 0.3 h, their place names (h, centred on the plates' height), 0.4 h; the distance 0.4 h under the second
        /// plate (digits h), 0.4 h from the bottom. With 125 cm plates and two characters this is 8.1 h x 6.2 h at 100 km/h: E.217's width (6.2 h and the plate) and its
        /// height for 100 cm plates (E.217 says 6.4 h, its plates drawn 1.67 h high). E.217 starts the distance at the middle of the plates; here it is centred under plates and names (Fig. 271 b).
        /// </summary>
        private static GuideBoard DoubleExit(GuideSpec s, int band)
        {
            float h = CharHeight(band), m = 0.4f * h;
            bool mirror = s.Is("side", "l");
            var rows = new List<(GuideBlock plate, GuideBlock names)>();
            foreach ((string code, string a1, string a2) in new[] { ("code", "to1", "to1b"), ("code2", "to2", "to2b") })
            {
                GuideBlock plate = Info(s.Get(code), h, Green), names = Names(s.Get(a1), s.Get(a2), h);
                if (plate.W > 0f || names.W > 0f)
                    rows.Add((plate, names));
            }

            // A road typed by its name instead of a number is a line of characters in the plates' place: the names are then 0.6 h after it (as two names), not 0.3 h.
            float plateColW = 0f, namesColW = 0f, plateGap = 0.3f * h;
            foreach ((GuideBlock plate, GuideBlock names) in rows)
            {
                plateColW = Math.Max(plateColW, plate.W);
                namesColW = Math.Max(namesColW, names.W);
                if (plate.W > 0f && plate.H <= 1.05f * h)
                    plateGap = 0.6f * h;
            }

            // In units of h from the inside's top left: the figure from 0.4 to 2.6 across, the plates 0.3 after it, the names 0.3 after the widest plate.
            const float figL = 0.4f, figW = 2.2f, stemX = figL + 0.5f, tipX = figL + figW;
            float px = (tipX + 0.3f) * h;
            float nx = px + (plateColW > 0f ? plateColW + plateGap : 0f);
            float textRight = namesColW > 0f ? nx + namesColW : px + plateColW;

            // Down: the rows, then the distance.
            var tops = new List<float>();
            var heights = new List<float>();
            float y = 0.5f * h, rowsBottom = 0.5f * h;
            foreach ((GuideBlock plate, GuideBlock names) in rows)
            {
                float rh = Math.Max(plate.H, names.W > 0f ? h : 0f);
                tops.Add(y);
                heights.Add(rh);
                rowsBottom = y + rh;
                y += rh + 0.4f * h;
            }

            GuideBlock dist = s.Get("dist").Length > 0 ? Distance(s.Get("dist"), s.Get("unit"), h, White) : null;
            float distY = rowsBottom + 0.4f * h;
            float textBottom = dist != null ? distY + h + m : rowsBottom + 0.6f * h;

            // A branch: its head (0.68 h, 23 degrees up) has its tip at the figure's right edge; back from the head 'straight' on the slant, the arc of radius h through 67 degrees, which
            // leaves the stem 'rise' under the tip. The figure reaches 0.3 h under the lowest branch's start at the least, and stops 0.6 h above the inside's bottom.
            const float a = 0.68f, t = 0.25f, r = 1.0f, turn = 67f;
            float c23 = (float)Math.Cos(23.0 * Math.PI / 180.0), s23 = (float)Math.Sin(23.0 * Math.PI / 180.0);
            float arcDx = r * (1f - (float)Math.Cos(turn * Math.PI / 180.0)), arcDy = r * (float)Math.Sin(turn * Math.PI / 180.0);
            float straight = Math.Max(0f, (tipX - a * c23 - stemX - arcDx) / c23);
            float rise = a * s23 + arcDy + straight * s23;
            float figBottom = (figL + 2.6f) * h;
            for (int i = 0; i < rows.Count; i++)
                figBottom = Math.Max(figBottom, tops[i] + heights[i] * 0.5f + (rise + 0.3f) * h);
            float innerH = Math.Max(textBottom, figBottom + 0.6f * h);

            float innerW = Math.Max(textRight, tipX * h) + m;
            float distX = 0f;
            if (dist != null)
            {
                distX = Math.Max(px, (px + textRight) * 0.5f - dist.W * 0.5f);
                innerW = Math.Max(innerW, distX + dist.W + m);
            }

            GuidePart p = Board(h, innerW, innerH, Green);
            GuideBoard board = Single(p, h);
            var sh = new Sheet(p, innerW, mirror);

            for (int i = 0; i < rows.Count; i++)
            {
                (GuideBlock plate, GuideBlock names) = rows[i];
                sh.Block(plate, px, tops[i] + (heights[i] - plate.H) * 0.5f);
                sh.Block(names, nx, tops[i] + (heights[i] - names.H) * 0.5f);
            }

            if (dist != null)
                sh.Block(dist, distX, distY);

            // The figure, in units of h from the inside's top left.
            var fig = new List<float[]>();
            float bottom = innerH / h - 0.6f;
            Finish(fig, new Pen(stemX, bottom, -90f).Line(bottom - 0.4f - 1.0f), 1.0f, 0.33f);
            for (int i = 0; i < rows.Count; i++)
            {
                float tipY = (tops[i] + heights[i] * 0.5f) / h;
                var pen = new Pen(stemX, tipY + rise, -90f).Arc(r, turn);
                if (straight > 0f)
                    pen.Line(straight);
                Finish(fig, pen, a, t);
            }

            sh.Figure(fig, h, 0f, 0f, White);

            AddExitPlate(board, s, band, h, mirror);
            return board;
        }

        // ================================================================== the catalogue

        private static string[] W(params string[] words) => words;

        private static readonly GuideChoice[] kSides =
        {
            new GuideChoice("r", "右侧出口（如图 271）", "Exit on the right (Fig. 271)"),
            new GuideChoice("l", "左侧出口（左右对调）", "Exit on the left (mirrored)")
        };

        private static readonly GuideChoice[] kDirs =
        {
            new GuideChoice("", "不设", "None"),
            new GuideChoice("东", "东", "East"),
            new GuideChoice("南", "南", "South"),
            new GuideChoice("西", "西", "West"),
            new GuideChoice("北", "北", "North")
        };

        private static readonly GuideChoice[] kUnits =
        {
            new GuideChoice("km", "km", "km"),
            new GuideChoice("m", "m", "m")
        };

        private static readonly GuideChoice[] kLeftCount =
        {
            new GuideChoice("0", "无", "None"), new GuideChoice("1", "1 条", "1 lane"), new GuideChoice("2", "2 条", "2 lanes"), new GuideChoice("3", "3 条", "3 lanes")
        };

        private static readonly GuideChoice[] kRightCount =
        {
            new GuideChoice("0", "无", "None"), new GuideChoice("1", "1 条", "1 lane"), new GuideChoice("2", "2 条", "2 lanes")
        };

        private static readonly GuideChoice[] kLeftArrows =
        {
            new GuideChoice("c", "弯向左（图 271 a）", "Bending left (Fig. 271 a)"),
            new GuideChoice("a", "直行（图 271 c）", "Ahead (Fig. 271 c)"),
            new GuideChoice("d", "斜向左上 ↖", "Diagonal ↖")
        };

        private static readonly GuideChoice[] kSharedArrows =
        {
            new GuideChoice("y", "分叉（图 271 a）", "Forked (Fig. 271 a)"),
            new GuideChoice("ar", "直行并分出右弯（图 271 c）", "Ahead with a branch (Fig. 271 c)"),
            new GuideChoice("n", "无共用车道", "No shared lane")
        };

        private static readonly GuideChoice[] kRightArrows =
        {
            new GuideChoice("yc", "黄底黑色弯箭头（直出车道）", "Bending, black on yellow (through lane)"),
            new GuideChoice("c", "白色弯箭头", "Bending, white"),
            new GuideChoice("yd", "黄底黑色斜箭头 ↗（直出车道）", "Diagonal ↗, black on yellow"),
            new GuideChoice("d", "白色斜箭头 ↗", "Diagonal ↗, white")
        };

        /// <summary>The settings of 路49 and 路51: both directions, the lanes, the side of the exit and the exit number.</summary>
        private static GuideField[] ForkFields(bool advance, string mCode, string mDir, string mPlace, string eCode, string eDir, string ePlace, string leftArrow, string sharedArrow, string xnum)
        {
            var f = new List<GuideField>
            {
                GuideField.Text("code", "主线：道路编号（可空）", "Main line: road number (or none)", mCode, 6),
                GuideField.Choice("mdir", "主线方向：方位", "Main line: direction", mDir, kDirs),
                Place("tom1", "主线方向：地名", "Main line: place", mPlace),
                Place("tom2", "主线方向：地名二（可空）", "Main line: place 2 (optional)", ""),
                GuideField.Text("code2", "出口：道路编号（可空）", "Exit: road number (or none)", eCode, 6),
                GuideField.Choice("edir", "出口方向：方位", "Exit: direction", eDir, kDirs),
                Place("toe1", "出口方向：地名", "Exit: place", ePlace),
                Place("toe2", "出口方向：地名二（可空）", "Exit: place 2 (may be empty)", "")
            };
            if (advance)
            {
                f.Add(GuideField.Digits("dist", "距离（空为分流点）", "Distance (empty: at the fork)", "2", 4));
                f.Add(GuideField.Choice("unit", "单位", "Unit", "km", kUnits, sp => sp.Get("dist").Length > 0));
            }

            f.Add(GuideField.Choice("nl", "只去主线方向的车道", "Lanes for the main line only", "1", kLeftCount));
            f.Add(GuideField.Choice("la", "这些车道的箭头", "Their arrows", leftArrow, kLeftArrows, sp => !sp.Is("nl", "0")));
            f.Add(GuideField.Choice("sa", "共用车道（两个方向都可去）", "Shared lane (both ways)", sharedArrow, kSharedArrows));
            f.Add(GuideField.Choice("nr", "只去出口方向的车道", "Lanes for the exit only", "1", kRightCount));
            f.Add(GuideField.Choice("ra", "这些车道的箭头", "Their arrows", "yc", kRightArrows, sp => !sp.Is("nr", "0")));
            f.Add(GuideField.Choice("side", "出口位置", "Side of the exit", "r", kSides));
            f.Add(GuideField.Text("xnum", "出口编号（空为不设）", "Exit number (empty: none)", xnum, 4));
            f.Add(GuideField.Text("xsuf", "编号后缀（A、B、A-B）", "Suffix (A, B, A-B)", "", 3, sp => sp.Get("xnum").Length > 0));
            return f.ToArray();
        }

        public static IEnumerable<GuideTemplate> All()
        {
            // 9.3.8.3, Fig. 271 a, E.216: the fork of two roads, advance board (2 km, 1 km, 500 m); without a distance the same board at the fork (Fig. 273 c).
            yield return StandardSigns.Make("gb.guide9.49", "路49", "道路分岔预告", "Road fork advance (with lane arrows)", G, "dlfcyg", W("分岔", "分叉", "道路分岔", "预告", "车道", "fork"),
                (s, band) => ForkBoard(s, band, kFork49),
                ForkFields(true, "G72", "北", "柳州", "G80", "东", "玉林", "c", "y", "360"));

            // 9.3.8.3, Fig. 271 b, E.217, Fig. 272: the interchange with two exits, advance board (3 km, 2 km, 1 km), the main line's information left out.
            yield return StandardSigns.Make("gb.guide9.50", "路50", "双出口枢纽式互通立体交叉的出口预告", "Advance board of an interchange with two exits", G, "scksnshtltjcdckyg",
                W("双出口", "枢纽", "互通", "出口预告", "图形", "double exit"),
                DoubleExit, new[]
                {
                    GuideField.Text("code", "第一出口：道路编号", "First exit: road number", "G55", 6),
                    Place("to1", "第一出口：地名", "First exit: place", "永州"),
                    Place("to1b", "第一出口：地名二（可空）", "First exit: place 2 (optional)", ""),
                    GuideField.Text("code2", "第二出口：道路编号", "Second exit: road number", "G55", 6),
                    Place("to2", "第二出口：地名", "Second exit: place", "广州"),
                    Place("to2b", "第二出口：地名二（可空）", "Second exit: place 2 (optional)", ""),
                    GuideField.Digits("dist", "距离（空为不设）", "Distance (empty: none)", "3", 4),
                    GuideField.Choice("unit", "单位", "Unit", "km", kUnits, sp => sp.Get("dist").Length > 0),
                    GuideField.Choice("side", "出口位置", "Side of the exits", "r", kSides),
                    GuideField.Text("xnum", "出口编号（空为不设）", "Exit number (empty: none)", "133", 4),
                    GuideField.Text("xsuf", "编号后缀（A、B、A-B）", "Suffix (A, B, A-B)", "A-B", 3, sp => sp.Get("xnum").Length > 0)
                });

            // 9.3.8.3, 9.3.8.4 b, Fig. 271 c, E.218, Fig. 273 b: the exit ramp of two lanes, the second lane from the left shared, exit direction board at the base point.
            yield return StandardSigns.Make("gb.guide9.51", "路51", "出口匝道为2条车道枢纽式互通立体交叉的出口方向", "Exit direction of an interchange with a two-lane exit ramp", G, "ckzdwtcdsnshtltjcdckfx",
                W("出口方向", "两车道", "匝道", "共用车道", "枢纽", "lanes"),
                (s, band) => ForkBoard(s, band, kFork51),
                ForkFields(false, "G72", "北", "深圳", "G15", "东", "广州", "a", "ar", "280"));
        }
    }
}
