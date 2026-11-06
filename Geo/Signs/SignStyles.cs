using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The shapes of the standard's fixed signs (prohibition, mandatory, warning, auxiliary), each built from the sizes of GB 5768.2-2022 Table 7 and Table 8 (and Fig. 1, Fig. 2 and Fig. 3) for
    /// the four bands of design speed (100 to 120, 71 to 99, 40 to 70 and under 40 km/h, index 0 to 3). A style returns the laid-out board; the figure on it is written by the caller on a Canvas
    /// (units 0 to 100 inside the area the standard gives to the figure). All sizes are in metres. Sizes are read from the tables; the proportions of the figures inside them (how much of the
    /// circle the picture takes, the weight of strokes) are read off the printed drawings and are not given by the standard.
    /// </summary>
    public static class SignStyles
    {
        /// <summary>The band a sign is laid out for when the road is not known (a street of 40 to 70 km/h).</summary>
        public const int DefaultBand = 2;

        private const float Sqrt3Over2 = 0.8660254f;

        // Table 7, round signs (D, red ring a, red bar b, liner c) and the octagon (D, white edge d); Table 8, warning triangles (A, black edge B, corner radius R, liner c).
        private static readonly float[] CircleD = { 1.20f, 1.00f, 0.80f, 0.60f };
        private static readonly float[] RingA = { 0.12f, 0.10f, 0.08f, 0.06f };
        private static readonly float[] BarB = { 0.09f, 0.075f, 0.06f, 0.045f };
        private static readonly float[] LinerC = { 0.010f, 0.008f, 0.006f, 0.004f };
        private static readonly float[] OctagonD = { 0.80f, 0.80f, 0.80f, 0.60f };
        private static readonly float[] OctagonEdge = { 0.03f, 0.03f, 0.03f, 0.02f };
        private static readonly float[] YieldA = { 0.90f, 0.90f, 0.90f, 0.70f };
        private static readonly float[] YieldEdge = { 0.09f, 0.09f, 0.09f, 0.07f };
        private static readonly float[] YieldR = { 0.04f, 0.04f, 0.04f, 0.03f };
        private static readonly float[] WarnA = { 1.30f, 1.10f, 0.90f, 0.70f };
        private static readonly float[] WarnB = { 0.09f, 0.08f, 0.065f, 0.05f };
        private static readonly float[] WarnR = { 0.06f, 0.05f, 0.04f, 0.03f };
        private static readonly float[] SquareA = { 1.20f, 1.00f, 0.80f, 0.60f };

        /// <summary>Table 7, rectangular signs: the long side B and the short side C of the rectangle (B x C: 120 x 96, 100 x 80, 80 x 64, 60 x 48).</summary>
        private static readonly float[] RectLong = { 1.20f, 1.00f, 0.80f, 0.60f };
        private static readonly float[] RectShort = { 0.96f, 0.80f, 0.64f, 0.48f };

        public static int Band(int band) => Math.Max(0, Math.Min(3, band));

        /// <summary>The band index (0 to 3) of a spec's speed setting; "auto" and anything else are the default band.</summary>
        public static int BandOf(GuideSpec spec)
        {
            switch (spec.Get("speed"))
            {
                case "100": return 0;
                case "80": return 1;
                case "60": return 2;
                case "30": return 3;
                default: return DefaultBand;
            }
        }

        public static float CircleDiameter(int band) => CircleD[Band(band)];

        public static float SquareSide(int band) => SquareA[Band(band)];

        public static float RectLongSide(int band) => RectLong[Band(band)];

        public static float RectShortSide(int band) => RectShort[Band(band)];

        public static float WarningSide(int band) => WarnA[Band(band)];

        // ------------------------------------------------------------------ safe regions

        /// <summary>A circle as x, y pairs in the frame of a part whose box is w x h, 48 corners, a little inside the radius so that the polygon of the test is inside the true circle.</summary>
        private static float[] CircleSafe(float w, float h, float radius)
        {
            var points = new float[96];
            float r = radius * 0.998f;
            for (int i = 0; i < 48; i++)
            {
                double a = 2.0 * Math.PI * i / 48;
                points[i * 2] = w * 0.5f + r * (float)Math.Cos(a);
                points[i * 2 + 1] = h * 0.5f + r * (float)Math.Sin(a);
            }

            return points;
        }

        // ------------------------------------------------------------------ parts

        private static GuideBoard BoardOf(GuidePart part)
        {
            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        private static GuideElement PolyFromOutline(IList<Point2> outline, float w, float h, GuideColour colour) =>
            GuideKit.Poly(GuideKit.ToPartFrame(outline, w, h), colour);

        // ------------------------------------------------------------------ prohibition: round with a red ring (Fig. 1 c)

        /// <summary>
        /// A round prohibition sign: a white board of diameter D, a red ring of width a at the liner's distance c from the edge, a white disc, the figure, and (bar) the red bar from top left to
        /// bottom right at 45 degrees, b wide, that crosses it out. artScale: the side of the canvas as a fraction of D (0.62: what the printed figures take).
        /// </summary>
        public static GuideBoard Prohibition(int band, Action<Canvas> art, bool bar = true, float artScale = 0.62f)
        {
            band = Band(band);
            float d = CircleD[band], a = RingA[band], b = BarB[band], c = LinerC[band];
            var part = new GuidePart { X = 0f, Y = 0f, W = d, H = d, Shape = GuidePartShape.Circle, Ground = GuideColour.White, Radius = d * 0.5f };
            part.Elements.Add(GuideKit.Oval(c, c, d - 2f * c, d - 2f * c, GuideColour.Red));
            part.Elements.Add(GuideKit.Oval(c + a, c + a, d - 2f * (c + a), d - 2f * (c + a), GuideColour.White));
            part.Safe = CircleSafe(d, d, d * 0.5f - c - a);
            float side = d * artScale;
            art?.Invoke(new Canvas(part, (d - side) * 0.5f, (d - side) * 0.5f, side, side));
            if (bar)
            {
                // The bar ends in the middle of the ring: its square ends are then inside the ring's outer edge.
                float reach = (d * 0.5f - c - a * 0.5f) / (float)Math.Sqrt(2.0);
                part.Elements.Add(GuideKit.Bar(d * 0.5f - reach, d * 0.5f - reach, d * 0.5f + reach, d * 0.5f + reach, b, GuideColour.Red));
            }

            return BoardOf(part);
        }

        // ------------------------------------------------------------------ mandatory: a blue disc (Fig. 1 c without the ring)

        /// <summary>A round mandatory sign: a white liner c wide round a blue disc, the figure in white.</summary>
        public static GuideBoard MandatoryRound(int band, Action<Canvas> art, float artScale = 0.64f, GuideColour ground = GuideColour.Blue)
        {
            band = Band(band);
            float d = CircleD[band], c = LinerC[band];
            var part = new GuidePart { X = 0f, Y = 0f, W = d, H = d, Shape = GuidePartShape.Circle, Ground = GuideColour.White, Radius = d * 0.5f };
            part.Elements.Add(GuideKit.Oval(c, c, d - 2f * c, d - 2f * c, ground));
            part.Safe = CircleSafe(d, d, d * 0.5f - c);
            float side = d * artScale;
            art?.Invoke(new Canvas(part, (d - side) * 0.5f, (d - side) * 0.5f, side, side));
            return BoardOf(part);
        }

        // ------------------------------------------------------------------ rectangles (Fig. 1 d, f; Fig. 3)

        /// <summary>
        /// A rectangular sign, w x h, with rounded corners: the ground colour, then a border of the border colour, then the ground again (Fig. 3's scheme, with c for the liner and the border).
        /// The canvas is the rectangle inside the border, reduced by margin (m). cornerRadius: the standard's 4 cm for signs that are not guide, notice or auxiliary boards (4.6.6).
        /// </summary>
        public static GuideBoard Rectangle(int band, float w, float h, GuideColour ground, GuideColour border, Action<Canvas> art, float margin = 0f, float cornerRadius = 0.04f, float borderWidth = -1f)
        {
            band = Band(band);
            float c = LinerC[band];
            var part = new GuidePart { X = 0f, Y = 0f, W = w, H = h, Shape = GuidePartShape.RoundRect, Ground = ground, BorderColour = border, Liner = c, Border = borderWidth < 0f ? c : borderWidth, Radius = cornerRadius };
            float edge = part.Edge + margin;
            part.Safe = new[] { edge, edge, w - edge, edge, w - edge, h - edge, edge, h - edge };
            art?.Invoke(new Canvas(part, edge, edge, w - 2f * edge, h - 2f * edge));
            return BoardOf(part);
        }

        /// <summary>A square sign of the band's side (Table 7: 120, 100, 80, 60 cm).</summary>
        public static GuideBoard Square(int band, GuideColour ground, GuideColour border, Action<Canvas> art, float margin = 0f) =>
            Rectangle(band, SquareA[Band(band)], SquareA[Band(band)], ground, border, art, margin);

        /// <summary>A rectangular sign of the band's B x C (Table 7: 120 x 96, 100 x 80, 80 x 64, 60 x 48 cm); tall: the long side vertical.</summary>
        public static GuideBoard RectBoard(int band, bool tall, GuideColour ground, GuideColour border, Action<Canvas> art, float margin = 0f) =>
            tall ? Rectangle(band, RectShort[Band(band)], RectLong[Band(band)], ground, border, art, margin)
                 : Rectangle(band, RectLong[Band(band)], RectShort[Band(band)], ground, border, art, margin);

        // ------------------------------------------------------------------ warning (Fig. 2, Table 8)

        /// <summary>
        /// A warning sign: an equilateral triangle, apex up, of side A: a black edge of width B with corner radius R, a yellow field, the figure in black. The canvas is a square of artScale times the
        /// inner triangle's side, centred a little below the centre of gravity (the printed figures stand there).
        /// </summary>
        public static GuideBoard Warning(int band, Action<Canvas> art, float artScale = 0.52f, GuideColour field = GuideColour.Yellow, GuideColour edge = GuideColour.Black)
        {
            band = Band(band);
            float a = WarnA[band], b = WarnB[band], c = LinerC[band], r = WarnR[band] + c;
            float hNominal = a * Sqrt3Over2;
            GuideKit.TriangleBounds(a, r, true, out _, out float cy, out float w, out float h);
            var part = new GuidePart { X = 0f, Y = 0f, W = w, H = h, Side = a, Shape = GuidePartShape.Triangle, Ground = field, Radius = r };
            part.Elements.Add(PolyFromOutline(GuideKit.TriangleOutlineIn(a, r, true, c), w, h, edge));
            part.Elements.Add(PolyFromOutline(GuideKit.TriangleOutlineIn(a, r, true, c + b), w, h, field));
            part.Safe = GuideKit.ToPartFrame(GuideKit.TriangleOutlineIn(a, r, true, c + b + 0.002f), w, h);
            float inRadius = a / (2f * (float)Math.Sqrt(3.0));
            float innerSide = a * (inRadius - c - b) / inRadius;
            float side = innerSide * artScale;
            // The square stands on the base of the white field, a twentieth of the side above it (the printed figures stand low in the triangle: the wide part is at the bottom). Its top
            // corners are in the narrow part and outside the triangle, which is fine: a figure does not use them, and the tests check what a figure really draws.
            float baseY = h - c - b - 0.05f * innerSide;
            art?.Invoke(new Canvas(part, w * 0.5f - side * 0.5f, baseY - side, side, side));
            return BoardOf(part);
        }

        // ------------------------------------------------------------------ the two special ones: stop and yield

        /// <summary>禁1 停车让行 (Fig. 10): a red octagon of D across with a white edge d, and 停 in white.</summary>
        public static GuideBoard Stop(int band)
        {
            band = Band(band);
            float d = OctagonD[band], e = OctagonEdge[band];
            var part = new GuidePart { X = 0f, Y = 0f, W = d, H = d, Shape = GuidePartShape.Octagon, Ground = GuideColour.White };
            part.Elements.Add(PolyFromOutline(GuideKit.OctagonOutline(d - 2f * e), d, d, GuideColour.Red));
            part.Safe = GuideKit.ToPartFrame(GuideKit.OctagonOutline(d - 2f * e - 0.004f), d, d);
            float ch = d * 0.60f;
            new Canvas(part, (d - ch) * 0.5f, (d - ch) * 0.5f, ch, ch).Cells("停", GuideColour.White, 0f, 0f, 100f);
            return BoardOf(part);
        }

        /// <summary>
        /// 禁2 减速让行 (Fig. 12): a triangle with the apex down, red edge a (Table 7: 9 cm for a side of 90), white field, liner c, and 让 in black. The character is sized and placed from the white
        /// triangle: its inradius r is the distance from the top edge to the middle of the field; the cell is 1.3 r (a quarter of the side) and its middle 0.92 r below the top edge, which leaves the
        /// top corners of the cell clear of the red edge by a tenth of the cell at least and the bottom corners clear of the slanting edges (the tests measure both).
        /// </summary>
        public static GuideBoard Yield(int band)
        {
            band = Band(band);
            float a = YieldA[band], e = YieldEdge[band], r = YieldR[band], c = LinerC[band];
            GuideKit.TriangleBounds(a, r, false, out _, out _, out float w, out float h);
            var part = new GuidePart { X = 0f, Y = 0f, W = w, H = h, Side = a, Shape = GuidePartShape.TriangleDown, Ground = GuideColour.White, Radius = r };
            part.Elements.Add(PolyFromOutline(GuideKit.TriangleOutlineIn(a, r, false, c), w, h, GuideColour.Red));
            part.Elements.Add(PolyFromOutline(GuideKit.TriangleOutlineIn(a, r, false, c + e), w, h, GuideColour.White));
            part.Safe = GuideKit.ToPartFrame(GuideKit.TriangleOutlineIn(a, r, false, c + e + 0.002f), w, h);
            float inset = c + e;
            float inRadius = a / (2f * (float)Math.Sqrt(3.0)) - inset;
            float cell = 1.30f * inRadius;
            float middle = inset + 0.92f * inRadius;
            new Canvas(part, (w - cell) * 0.5f, middle - cell * 0.5f, cell, cell).Cells("让", GuideColour.Black, 0f, 0f, 100f);
            return BoardOf(part);
        }
    }
}
