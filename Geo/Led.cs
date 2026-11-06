using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The colours an LED variable-message sign lights (GB 5768.2 Table 10: red, yellow, green, white and, for the border of a mandatory sign and the fixed part of a lane or traffic sign, blue).</summary>
    public enum LedColour : byte
    {
        Off = 0,
        Red = 1,
        Amber = 2,
        Green = 3,
        White = 4,
        Blue = 5
    }

    /// <summary>
    /// The dots of an LED screen: DotsX columns and DotsY rows, row 0 the top and column 0 the left of the picture (what a driver sees), each dot off or one of the colours.
    /// Pure data; made from a painted picture by Led.Quantize and turned into a picture (Led.Raster) or into small quads (Led.AddDots).
    /// </summary>
    public sealed class LedGrid
    {
        public int DotsX, DotsY;
        public LedColour[] Dots;

        /// <summary>The size of the picture it was made from, 0 when it was not made from one.</summary>
        public int PictureW, PictureH;

        public int Lit
        {
            get
            {
                int n = 0;
                foreach (LedColour c in Dots)
                    n += c != LedColour.Off ? 1 : 0;
                return n;
            }
        }

        public LedColour At(int x, int y) => x < 0 || y < 0 || x >= DotsX || y >= DotsY ? LedColour.Off : Dots[y * DotsX + x];

        public int Count(LedColour colour)
        {
            int n = 0;
            foreach (LedColour c in Dots)
                n += c == colour ? 1 : 0;
            return n;
        }

        public LedGrid(int dotsX, int dotsY)
        {
            DotsX = Math.Max(1, dotsX);
            DotsY = Math.Max(1, dotsY);
            Dots = new LedColour[DotsX * DotsY];
        }
    }

    /// <summary>
    /// An LED variable-message sign (4.11) as dots. The screen's picture is painted like any board (black ground, the figure in its colours) and then read dot by dot: a dot is lit when most of
    /// its cell is bright, and takes the colour that cell mostly is. A lit dot is a small square facing the driver in front of the screen; the picture of the screen itself shows the same dots
    /// (Raster), so that a screen seen from far away, without the quads, looks the same. Pure, so that it is checked offline; the game's painter is not needed for any of it.
    /// </summary>
    public static class Led
    {
        /// <summary>
        /// What the dots of a screen are made from, as text: the size of the sign, the box of its picture, the screen's pitch, dot limit and samples, and every part and element of its laid-out board (the
        /// painter reads nothing else: GuidePainter.Draw takes the board and the box). Two instances whose settings differ only in what does not change the board (the speed band of the road, which a
        /// screen's layout does not use) have the same key, so the dots are painted once for both (0.12.1: the tool met a new band at every road of another speed and painted them again, 8 to 41 ms on
        /// the main thread). Null for an instance that is not a screen.
        /// </summary>
        public static string PictureKey(SignInstance instance)
        {
            if (instance == null || !instance.IsLed || instance.Def == null)
                return null;
            var sb = new System.Text.StringBuilder(256);
            void F(float v) => sb.Append(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            void S(string v) => sb.Append(v == null ? -1 : v.Length).Append(':').Append(v ?? "").Append(',');
            GuideBoard board = instance.Board;
            F(instance.Def.Width);
            F(instance.Def.Height);
            F(instance.BoundsX0);
            F(instance.BoundsY0);
            F(instance.BoundsX1);
            F(instance.BoundsY1);
            F(board.H);
            F(board.LedPitch);
            sb.Append(board.LedMaxDots).Append(',').Append(board.LedSamples).Append(';');
            foreach (GuidePart p in board.Parts)
            {
                sb.Append('P');
                F(p.X);
                F(p.Y);
                F(p.W);
                F(p.H);
                F(p.Side);
                F(p.Liner);
                F(p.Border);
                F(p.Radius);
                sb.Append((int)p.Shape).Append(',').Append((int)p.Ground).Append(',').Append((int)p.BorderColour).Append(',');
                if (p.Safe != null)
                {
                    foreach (float v in p.Safe)
                        F(v);
                }

                foreach (GuideElement e in p.Elements)
                {
                    sb.Append('E').Append((int)e.Kind).Append(',');
                    F(e.X);
                    F(e.Y);
                    F(e.W);
                    F(e.H);
                    F(e.RadiusTop);
                    F(e.RadiusBottom);
                    F(e.StrokeWidth);
                    F(e.Bleed);
                    F(e.Gap);
                    sb.Append((int)e.Colour).Append(',').Append((int)e.Stroke).Append(',').Append(e.Art ? 1 : 0).Append(',').Append((int)e.Mode).Append(',').Append((int)e.Align).Append(',').Append((int)e.Arrow).Append(',');
                    S(e.Text);
                    S(e.InkRef);
                    if (e.Points != null)
                    {
                        sb.Append("pt");
                        foreach (float v in e.Points)
                            F(v);
                    }

                    if (e.Contours != null)
                    {
                        foreach (float[] contour in e.Contours)
                        {
                            sb.Append("ct");
                            foreach (float v in contour)
                                F(v);
                        }
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// How many dots a character of a text screen is high: 32 (it was 16 up to 0.5.2, and the owner found the screens too coarse). A character of 40 cm is then 12.5 mm to the dot, the pitch of a
        /// fine outdoor panel (P10 panels of the common kind are coarser, and nothing in the standard asks for either).
        /// </summary>
        public const int DotsPerCharacter = 32;

        /// <summary>The pitch of the dots (m) when nothing says otherwise: 12.5 mm.</summary>
        public const float DefaultPitch = 0.0125f;

        /// <summary>
        /// The most dots a screen has: it bounds the vertices (4 for each lit dot; a text screen has between a seventh and two fifths of its dots lit (measured on the default and the fullest screens: 13 to 38 per cent), so 60000 dots are at the most about 100000 vertices) and the time of
        /// quantizing; a screen that would have more is given a coarser pitch.
        /// </summary>
        public const int MaxDots = 60000;

        /// <summary>How many pixels of the painted picture a dot looks at, in each direction, at the least (the picture it is read from is made large enough for that), and the longest side of that picture.</summary>
        public const int SamplesPerDot = 6, MaxSampleSide = 3072;

        /// <summary>How many pixels of the picture a dot has in the screen's face (what the screen looks like from afar), in each direction, and the longest side of that face.</summary>
        public const int FacePixelsPerDot = 4, MaxFaceSide = 1024;

        /// <summary>The size of the picture a grid of these dots is read from: SamplesPerDot to a dot each way, the longest side kept to MaxSampleSide (the shape is kept).</summary>
        public static void SampleSize(int dotsX, int dotsY, out int w, out int h, int samplesPerDot = 0)
        {
            int n = samplesPerDot > 0 ? samplesPerDot : SamplesPerDot;
            Fit(dotsX * n, dotsY * n, MaxSampleSide, out w, out h);
        }

        /// <summary>
        /// The size of the picture of the screen's face: **a whole number of pixels to a dot** each way, FacePixelsPerDot at the most and as many fewer as keep the long side to MaxFaceSide (a
        /// whole number, so that every dot has the same pixels: with 2.13 or 3.55 pixels to a dot the rounding of the cells left a dark line between some rows and not others, which showed on
        /// the dense traffic-state screen as a moire over the whole map). More dots than MaxFaceSide on the long side are fitted, the shape kept (none of the screens does).
        /// </summary>
        public static void FaceSize(int dotsX, int dotsY, out int w, out int h)
        {
            int longest = Math.Max(1, Math.Max(dotsX, dotsY));
            int perDot = Math.Min(FacePixelsPerDot, MaxFaceSide / longest);
            if (perDot < 1)
            {
                Fit(dotsX, dotsY, MaxFaceSide, out w, out h);
                return;
            }

            w = Math.Max(4, dotsX * perDot);
            h = Math.Max(4, dotsY * perDot);
        }

        private static void Fit(int w0, int h0, int longest, out int w, out int h)
        {
            double scale = Math.Min(1.0, longest / (double)Math.Max(1, Math.Max(w0, h0)));
            w = Math.Max(4, (int)Math.Round(w0 * scale));
            h = Math.Max(4, (int)Math.Round(h0 * scale));
        }

        /// <summary>How far in front of the screen's face the dots stand (m): enough not to fight the face's picture at 100 m, little enough to look like a part of the screen.</summary>
        public const float Lift = 0.003f;

        /// <summary>The lit part of a dot's cell (the rest is the gap between dots): what an unlit dot of the face's picture is drawn as, and what a lit dot was until 0.12.23.</summary>
        public const float Fill = 0.78f;

        /// <summary>
        /// How bright a lit cell is against a dot (0.12.24; the owner: "可变信息牌系列字体处理参考最新的Assets/BusDriver的处理，消除摩尔纹"). A lit dot is drawn as its whole cell, so that no gap is left between
        /// two to beat with the pixel grid (as BusDriver 0.13.3 does: a gap narrower than a pixel is drawn in some columns and missed in others), and its light is a dot's share of the cell, so that a
        /// screen is as bright as it was. BusDriver draws its dots one by one again from 5 pixels a pitch; this mod's pitches (9 to 19 mm) are that large only within a few metres of the camera, so the
        /// cells are drawn always.
        /// </summary>
        public const float CellLight = Fill * Fill;

        /// <summary>The frame round an LED screen's face: how wide it is across the face and how far it stands in front of it (m). The screen sits in it, recessed by the reach.</summary>
        public const float BezelWidth = 0.04f, BezelReach = 0.035f;

        /// <summary>
        /// How deep the cabinet of an LED screen is, front to back (m): a screen is a box with the display in its front, not a plate. The standard gives no depth (4.11 says nothing of the
        /// build); the figures are the usual ones of road variable-message signs, 20 to 45 cm: 0.12 m and 0.08 of the shorter side of the screen, so 22 cm for a 1.2 m lane signal and
        /// 36 cm for a 4 x 3 m traffic-state screen. PROVISIONAL (the owner asked for thickness on 2026-10-19 and has not yet seen this one).
        /// </summary>
        public static float CabinetDepth(float width, float height) => Math.Max(0.20f, Math.Min(0.45f, 0.12f + 0.08f * Math.Min(width, height)));

        /// <summary>How many dots a screen of this size has at the pitch: at least 4 each way, and at most MaxDots in all (the pitch is made coarser to keep to it).</summary>
        public static void Dimensions(float width, float height, float pitch, out int dotsX, out int dotsY, int maxDots = MaxDots)
        {
            pitch = pitch > 1e-4f ? pitch : DefaultPitch;
            dotsX = Math.Max(4, (int)Math.Round(width / pitch));
            dotsY = Math.Max(4, (int)Math.Round(height / pitch));
            if (dotsX * dotsY > maxDots)
            {
                double scale = Math.Sqrt(maxDots / (double)(dotsX * dotsY));
                dotsX = Math.Max(4, (int)Math.Floor(dotsX * scale));
                dotsY = Math.Max(4, (int)Math.Floor(dotsY * scale));
            }
        }

        /// <summary>The dots of a screen of this board at its size: its own pitch, and its own limit when it has one (LedMaxDots) else MaxDots. The one place that decides it; the renderer and the tests both ask here.</summary>
        public static void Dimensions(GuideBoard board, float width, float height, out int dotsX, out int dotsY) =>
            Dimensions(width, height, board != null ? board.LedPitch : 0f, out dotsX, out dotsY, board != null && board.LedMaxDots > 0 ? board.LedMaxDots : MaxDots);

        /// <summary>The colour a pixel (0 to 1) is lit as: Off for the dark; white for the pale; else by the hue: red (and the purples), amber (orange, yellow, brown), green (to cyan), blue.</summary>
        public static LedColour Classify(float r, float g, float b)
        {
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max < 0.30f)
                return LedColour.Off;
            float chroma = max - min;
            if (chroma / max < 0.30f)
                return LedColour.White;

            float hue;
            if (max == r)
                hue = 60f * (((g - b) / chroma) % 6f);
            else if (max == g)
                hue = 60f * (2f + (b - r) / chroma);
            else
                hue = 60f * (4f + (r - g) / chroma);
            if (hue < 0f)
                hue += 360f;

            if (hue < 15f || hue >= 270f)
                return LedColour.Red;
            if (hue < 75f)
                return LedColour.Amber;
            if (hue < 200f)
                return LedColour.Green;
            return LedColour.Blue;
        }

        /// <summary>
        /// The dots of a picture: RGBA bytes, the first row at the bottom (the order the painter writes and a Unity texture reads). Each dot looks at its cell of pixels: a pixel counts when it is
        /// opaque (alpha 128 or more) and bright (the largest channel at 0.30 or more); the dot is lit when at least half the cell counts, in the colour of the average of those pixels.
        /// </summary>
        public static LedGrid Quantize(byte[] rgba, int w, int h, int dotsX, int dotsY)
        {
            var grid = new LedGrid(dotsX, dotsY) { PictureW = w, PictureH = h };
            if (rgba == null || w <= 0 || h <= 0 || rgba.Length < w * h * 4)
                return grid;

            for (int j = 0; j < grid.DotsY; j++)
            {
                int yTop0 = (int)((long)j * h / grid.DotsY);
                int yTop1 = Math.Max(yTop0 + 1, (int)((long)(j + 1) * h / grid.DotsY));
                for (int i = 0; i < grid.DotsX; i++)
                {
                    int x0 = (int)((long)i * w / grid.DotsX);
                    int x1 = Math.Max(x0 + 1, (int)((long)(i + 1) * w / grid.DotsX));
                    int total = 0, lit = 0;
                    float sr = 0f, sg = 0f, sb = 0f;
                    for (int yTop = yTop0; yTop < yTop1 && yTop < h; yTop++)
                    {
                        int row = (h - 1 - yTop) * w;
                        for (int x = x0; x < x1 && x < w; x++)
                        {
                            total++;
                            int p = (row + x) * 4;
                            if (rgba[p + 3] < 128)
                                continue;
                            float r = rgba[p] / 255f, g = rgba[p + 1] / 255f, b = rgba[p + 2] / 255f;
                            if (Math.Max(r, Math.Max(g, b)) < 0.30f)
                                continue;
                            lit++;
                            sr += r;
                            sg += g;
                            sb += b;
                        }
                    }

                    if (total > 0 && lit * 2 >= total && lit > 0)
                        grid.Dots[j * grid.DotsX + i] = Classify(sr / lit, sg / lit, sb / lit);
                }
            }

            return grid;
        }

        /// <summary>The colour a lit dot shows (0 to 255, as the picture of the screen and its glow are made from): bright, a little paler than the paint's colours.</summary>
        public static void Rgb(LedColour colour, out byte r, out byte g, out byte b)
        {
            switch (colour)
            {
                case LedColour.Red: r = 255; g = 36; b = 30; break;
                case LedColour.Amber: r = 255; g = 176; b = 0; break;
                case LedColour.Green: r = 24; g = 255; b = 84; break;
                case LedColour.White: r = 255; g = 255; b = 244; break;
                case LedColour.Blue: r = 56; g = 116; b = 255; break;
                default: r = 0; g = 0; b = 0; break;
            }
        }

        /// <summary>
        /// The screen as a picture of w x h pixels (RGBA, the first row at the bottom): black, with a faint square the Fill part of its cell for every unlit dot and the whole cell in its colour for
        /// every lit dot (CellLight). This is the face of the screen; the lit cells are drawn again in front of it, glowing, near by.
        /// </summary>
        public static byte[] Raster(LedGrid grid, int w, int h)
        {
            var rgba = new byte[Math.Max(0, w * h * 4)];
            for (int p = 3; p < rgba.Length; p += 4)
                rgba[p] = 255;
            if (grid == null || w <= 0 || h <= 0)
                return rgba;

            for (int j = 0; j < grid.DotsY; j++)
            {
                for (int i = 0; i < grid.DotsX; i++)
                {
                    LedColour colour = grid.Dots[j * grid.DotsX + i];
                    Rgb(colour, out byte r, out byte g, out byte b);
                    if (colour == LedColour.Off)
                    {
                        r = g = b = 14;
                    }

                    // The cell in pixels, from the top; an unlit dot is its middle part, a lit one all of it.
                    double cx0 = (double)i * w / grid.DotsX, cx1 = (double)(i + 1) * w / grid.DotsX;
                    double cy0 = (double)j * h / grid.DotsY, cy1 = (double)(j + 1) * h / grid.DotsY;
                    double part = colour == LedColour.Off ? Fill : 1.0;
                    double mx = (cx0 + cx1) * 0.5, my = (cy0 + cy1) * 0.5, hx = (cx1 - cx0) * 0.5 * part, hy = (cy1 - cy0) * 0.5 * part;
                    int px0 = (int)Math.Round(mx - hx), px1 = Math.Max(px0 + 1, (int)Math.Round(mx + hx));
                    int py0 = (int)Math.Round(my - hy), py1 = Math.Max(py0 + 1, (int)Math.Round(my + hy));
                    for (int yTop = Math.Max(0, py0); yTop < py1 && yTop < h; yTop++)
                    {
                        int row = (h - 1 - yTop) * w;
                        for (int x = Math.Max(0, px0); x < px1 && x < w; x++)
                        {
                            int p = (row + x) * 4;
                            rgba[p] = r;
                            rgba[p + 1] = g;
                            rgba[p + 2] = b;
                        }
                    }
                }
            }

            return rgba;
        }

        /// <summary>
        /// The lit dots as their whole cells (CellLight) in front of a screen whose middle is (cx, cy) in the sign's frame and whose size is width x height, at the plane z: one mesh for each colour,
        /// made when it is needed (perColour is indexed by LedColour), the cells of one colour side by side in a row one quad. The picture's left is +X in the sign's frame (the viewer's left), so
        /// column 0 is the largest X. Returns the number of lit dots.
        /// </summary>
        public static int AddDots(MeshBuilder[] perColour, LedGrid grid, float cx, float cy, float width, float height, float z)
        {
            if (perColour == null || grid == null || grid.DotsX <= 0 || grid.DotsY <= 0)
                return 0;

            float pitchX = width / grid.DotsX, pitchY = height / grid.DotsY;
            float left = cx + width * 0.5f, top = cy + height * 0.5f;
            var front = new Vec3(0f, 0f, 1f);
            int added = 0;
            for (int j = 0; j < grid.DotsY; j++)
            {
                int i = 0;
                while (i < grid.DotsX)
                {
                    LedColour colour = grid.Dots[j * grid.DotsX + i];
                    int end = i + 1;
                    if (colour != LedColour.Off)
                    {
                        while (end < grid.DotsX && grid.Dots[j * grid.DotsX + end] == colour)
                            end++;
                        MeshBuilder mesh = perColour[(int)colour] ?? (perColour[(int)colour] = new MeshBuilder());
                        // Cell edges from the screen's edge by whole pitches: two runs that meet share their edge exactly.
                        float x0 = left - end * pitchX, x1 = left - i * pitchX, y1 = top - j * pitchY, y0 = top - (j + 1) * pitchY;
                        int a = mesh.Vertex(new Vec3(x0, y0, z), front);
                        int b = mesh.Vertex(new Vec3(x1, y0, z), front);
                        int c = mesh.Vertex(new Vec3(x1, y1, z), front);
                        int d = mesh.Vertex(new Vec3(x0, y1, z), front);
                        mesh.QuadFacing(a, b, c, d, front);
                        added += end - i;
                    }

                    i = end;
                }
            }

            return added;
        }
    }
}
