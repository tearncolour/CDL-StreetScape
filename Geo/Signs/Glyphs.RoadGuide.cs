using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The pictograms of the guide signs of chapter 8 (ordinary roads) and of the expressway information and facility signs of 9.4 and 9.5 of GB 5768.2-2022, redrawn from the printed
    /// figures and the drawings of Appendix E (E.176 to E.192, E.222 to E.244). Prefix R_. One colour each; a sign puts the colour on. Where a glyph is written in the frame of its drawing
    /// (the inside of the board's border, or the white panel) the comment says so, and the sign puts it over that frame; the others fill their own box and are fitted into their place.
    /// </summary>
    public static partial class Glyphs
    {
        // ------------------------------------------------------------------ helpers (private: only public strings are glyphs)

        /// <summary>Points of an elliptic arc, angles in degrees on the screen (0 to the right, 90 down).</summary>
        private static float[] RArc(float cx, float cy, float rx, float ry, float a0, float a1, int steps = 12)
        {
            var p = new float[(steps + 1) * 2];
            for (int i = 0; i <= steps; i++)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p[i * 2] = cx + rx * (float)Math.Cos(a);
                p[i * 2 + 1] = cy + ry * (float)Math.Sin(a);
            }

            return p;
        }

        private static float[] RCat(params float[][] parts)
        {
            var all = new List<float>();
            foreach (float[] p in parts)
                all.AddRange(p);
            return all.ToArray();
        }

        /// <summary>A band along a circle between two radii, from angle a0 to a1 (a ring segment), as a polygon.</summary>
        private static float[] RBand(float cx, float cy, float r0, float r1, float a0, float a1, int steps = 16)
        {
            float[] outer = RArc(cx, cy, r1, r1, a0, a1, steps);
            float[] inner = RArc(cx, cy, r0, r0, a1, a0, steps);
            return RCat(outer, inner);
        }

        /// <summary>A thick polyline with square ends and mitred joins (for strokes the drawings end square).</summary>
        private static float[] RLine(float w, params float[] p)
        {
            int n = p.Length / 2;
            var left = new List<float>();
            var right = new List<float>();
            for (int i = 0; i < n; i++)
            {
                float nx = 0f, ny = 0f;
                int count = 0;
                if (i > 0)
                {
                    float dx = p[i * 2] - p[i * 2 - 2], dy = p[i * 2 + 1] - p[i * 2 - 1], l = (float)Math.Sqrt(dx * dx + dy * dy);
                    nx += -dy / l;
                    ny += dx / l;
                    count++;
                }

                if (i < n - 1)
                {
                    float dx = p[i * 2 + 2] - p[i * 2], dy = p[i * 2 + 3] - p[i * 2 + 1], l = (float)Math.Sqrt(dx * dx + dy * dy);
                    nx += -dy / l;
                    ny += dx / l;
                    count++;
                }

                float nl = (float)Math.Sqrt(nx * nx + ny * ny);
                nx /= nl;
                ny /= nl;
                // The mitre: the offset grows as the sides turn.
                float k = w * 0.5f;
                if (count == 2)
                {
                    float dx = p[i * 2 + 2] - p[i * 2], dy = p[i * 2 + 3] - p[i * 2 + 1], l = (float)Math.Sqrt(dx * dx + dy * dy);
                    float cos = (nx * (-dy / l) + ny * (dx / l));
                    k /= Math.Max(0.3f, cos);
                }

                left.Add(p[i * 2] + nx * k);
                left.Add(p[i * 2 + 1] + ny * k);
                right.Add(p[i * 2] - nx * k);
                right.Add(p[i * 2 + 1] - ny * k);
            }

            var all = new List<float>(left);
            for (int i = n - 1; i >= 0; i--)
            {
                all.Add(right[i * 2]);
                all.Add(right[i * 2 + 1]);
            }

            return all.ToArray();
        }

        /// <summary>A path moved and scaled evenly so that its bounds fill the box 5..95 along their longer side, centred (for figures read off a drawing in its own units).</summary>
        private static string RFit(string path)
        {
            List<float[]> contours = GuidePath.Parse(path);
            GuidePath.Bounds(contours, out float x0, out float y0, out float x1, out float y1);
            float s = 90f / Math.Max(x1 - x0, y1 - y0);
            float dx = 50f - (x0 + x1) * 0.5f * s, dy = 50f - (y0 + y1) * 0.5f * s;
            var sb = new StringBuilder();
            foreach (float[] c in GuidePath.Map(contours, s, s, dx, dy))
            {
                for (int i = 0; i + 1 < c.Length; i += 2)
                    sb.Append(i == 0 ? 'M' : 'L').Append(c[i].ToString("0.##", CultureInfo.InvariantCulture)).Append(' ').Append(c[i + 1].ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append("Z ");
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ 路12 地点识别 (Fig. 217, 218; E.176)

        /// <summary>The red cross of the first aid station (E.176 a), its arms a third of its size.</summary>
        public static readonly string R_FirstAidCross = new PicBuilder().Rect(33f, 5f, 34f, 90f).Rect(5f, 33f, 90f, 34f).ToString();

        /// <summary>The bandaged hand of the first aid station (E.176 a), in the frame of the board's inside: four fingers and the thumb to the left, the bandage with five blue stripes.</summary>
        public static readonly string R_FirstAidHand = new PicBuilder()
            .Poly(false, 38.7f, 59.1f, 55.5f, 57.4f, 59.5f, 48.4f, 67.4f, 55.7f, 81.5f, 56.4f, 84.2f, 57.1f, 84.2f, 79.2f, 68.9f, 79.2f, 61.7f, 86f, 40.9f, 87.1f, 38.7f, 83.2f)
            .Poly(false, 30.4f, 49.8f, 32.6f, 48.4f, 57.5f, 48.4f, 54.5f, 54.8f, 32.6f, 54.8f)
            .Poly(false, 20.8f, 59.1f, 39f, 59.1f, 39f, 63.4f, 20.8f, 63.4f)
            .Poly(false, 15.8f, 70f, 17.9f, 67.7f, 39f, 67.7f, 39f, 72f, 17.9f, 72f)
            .Poly(false, 17.9f, 77.9f, 20f, 75.6f, 39f, 75.6f, 39f, 79.9f, 20f, 79.9f)
            .Poly(false, 26.5f, 85f, 28.2f, 83.5f, 39f, 83.5f, 39f, 86.8f, 28.2f, 86.8f)
            .Poly(true, RLine(3.2f, 42f, 62f, 56f, 84f))
            .Poly(true, RLine(3.2f, 43f, 83.5f, 48.5f, 74.5f))
            .Poly(true, RLine(3.2f, 56f, 63f, 60.5f, 54.5f))
            .Poly(true, RLine(3.2f, 68.6f, 59f, 68.6f, 77f))
            .Poly(true, RLine(3.2f, 77.5f, 59f, 77.5f, 77f))
            .ToString();

        /// <summary>The aeroplane of the airport (E.176 b), in the frame of the board's inside, its nose to the upper left (the sign mirrors it to the side of the exit).</summary>
        public static readonly string R_Airplane = new PicBuilder()
            .Poly(false, RCat(
                RArc(19f, 18f, 5.5f, 5.5f, 135f, 290f, 8),
                new[] { 35.2f, 27.3f, 81.8f, 20.1f, 87.5f, 26.1f, 48.8f, 41.9f, 67.7f, 62.4f, 81.5f, 60f, 85.4f, 63.4f, 72.5f, 71.7f, 63.8f, 84.6f, 60.5f, 81.5f, 62.8f, 67.1f,
                    42.3f, 48.1f, 26.5f, 87.1f, 20.8f, 81.3f, 27.5f, 34.7f }))
            .ToString();

        /// <summary>The car wash (E.176 c, left): a car seen from the front under a shower head and its spray.</summary>
        public static readonly string R_CarWash = RFit(BuildCarWash());

        private static string BuildCarWash()
        {
            var b = new PicBuilder()
                .Poly(false, RCat(RArc(552f, 640f, 92f, 118f, 228f, 312f, 10), new[] { 648f, 580f, 662f, 598f, 662f, 668f, 441f, 668f, 441f, 598f, 455f, 580f }))
                .Poly(true, 480f, 548f, 624f, 548f, 632f, 576f, 472f, 576f)
                .Circle(462f, 607f, 10f, true).Circle(642f, 607f, 10f, true)
                .RoundRect(488f, 598f, 128f, 13f, 6f, true).RoundRect(492f, 624f, 120f, 12f, 6f, true)
                .RoundRect(458f, 660f, 26f, 38f, 8f).RoundRect(620f, 660f, 26f, 38f, 8f)
                .Poly(false, RCat(RArc(555f, 420f, 28f, 16f, 180f, 360f, 8)))
                .Rect(551f, 396f, 8f, 12f);
            // The spray: rows of drops widening downwards.
            for (int row = 0; row < 6; row++)
            {
                int n = 5 + row % 2;
                float y = 440f + row * 14f, half = 26f + row * 12f;
                for (int k = 0; k < n; k++)
                    b.Circle(555f - half + 2f * half * k / (n - 1), y, 5f);
            }

            return b.ToString();
        }

        /// <summary>The fuel pump (E.176 c, d; 9.5 E.242), in the frame of the white panel of E.176 d: a column with its window, the base, the nozzle and the hose hanging in a loop.</summary>
        public static readonly string R_Fuel = new PicBuilder()
            .Rect(20f, 83f, 60f, 5f)
            .Poly(false, 35.5f, 12.4f, 64.2f, 12.4f, 64.2f, 18f, 74.5f, 27f, 74.5f, 30f, 64.2f, 30f, 64.2f, 83.5f, 35.5f, 83.5f)
            .RoundRect(39.4f, 16f, 21.8f, 17.6f, 2f, true)
            .Poly(false, RLine(3.5f, RCat(new[] { 72.8f, 28f, 72.8f, 60f }, RArc(72.3f, 72.5f, 6.2f, 6.2f, -30f, 180f, 10), new[] { 66.1f, 46f })))
            .Rect(63.5f, 25f, 5f, 12f)
            .ToString();

        /// <summary>The charging station (E.176 e), in the frame of the white panel: the charger with its window and a lightning bolt, the cable and the plug.</summary>
        public static readonly string R_Charger = new PicBuilder()
            .Poly(false, RCat(RArc(25.5f, 17.5f, 4.7f, 4.7f, 180f, 270f, 4), RArc(45.5f, 17.5f, 4.7f, 4.7f, 270f, 360f, 4), new[] { 50.2f, 82.8f, 53.2f, 82.8f, 53.2f, 87.6f, 17.8f, 87.6f, 17.8f, 82.8f, 20.8f, 82.8f }))
            .RoundRect(24.8f, 16.3f, 21.4f, 17.5f, 1.5f, true)
            .Poly(true, 38f, 40f, 28f, 58.5f, 34.5f, 56f, 32f, 72f, 43.5f, 52f, 37f, 54.5f)
            .Poly(false, RLine(3.5f, RCat(new[] { 49f, 62.5f }, RArc(56.5f, 66.5f, 4f, 4f, -90f, 0f, 4), RArc(66.5f, 78f, 6f, 6f, 180f, 0f, 10), new[] { 72.5f, 42f })))
            .Poly(false, 65.6f, 24.8f, 82.8f, 24.8f, 82.8f, 34f, 76.5f, 40f, 76.5f, 44f, 69f, 44f, 69f, 40f, 65.6f, 34f)
            .Rect(67.5f, 15f, 3.5f, 10f).Rect(77.4f, 15f, 3.5f, 10f)
            .ToString();

        /// <summary>The metro (E.176 f), in the frame of the white panel: a train seen from the front under an arch, on its rails.</summary>
        public static readonly string R_Metro = new PicBuilder()
            .Poly(false, RCat(RArc(50.15f, 46.4f, 34.85f, 35f, 180f, 360f, 16), new[] { 85f, 84f, 79f, 84f }, RArc(50.15f, 46.4f, 28.85f, 29f, 360f, 180f, 16), new[] { 21.3f, 84f, 15.3f, 84f }))
            .Poly(false, RCat(RArc(39f, 35f, 7f, 7f, 180f, 270f, 5), RArc(62f, 35f, 7f, 7f, 270f, 360f, 5), new[] { 69f, 72f, 32.1f, 72f }))
            .RoundRect(36f, 36f, 29f, 13.5f, 2f, true)
            .RoundRect(44.4f, 29.6f, 11.1f, 4.5f, 1.5f, true)
            .Circle(39.6f, 64.5f, 3.6f, true).Circle(59.8f, 64.5f, 3.6f, true)
            .Rect(37f, 75f, 26f, 3.5f)
            .Poly(false, RLine(4f, 38f, 74f, 29f, 88f))
            .Poly(false, RLine(4f, 62f, 74f, 71f, 88f))
            .ToString();

        /// <summary>The car ferry (Fig. 218 c): a car on a boat on two waves.</summary>
        public static readonly string R_Ferry = new PicBuilder()
            .Poly(false, 10f, 48f, 90f, 48f, 82f, 64f, 18f, 64f)
            .Poly(false, 22f, 46f, 26f, 34f, 36f, 26f, 64f, 26f, 72f, 34f, 82f, 38f, 82f, 46f)
            .Poly(true, 31f, 36f, 38f, 30f, 48f, 30f, 48f, 36f)
            .Poly(true, 52f, 30f, 62f, 30f, 67f, 36f, 52f, 36f)
            .Circle(34f, 47f, 5f).Circle(70f, 47f, 5f)
            .Poly(false, RLine(4f, 8f, 75f, 20f, 70f, 32f, 75f, 44f, 70f, 56f, 75f, 68f, 70f, 80f, 75f, 92f, 70f))
            .Poly(false, RLine(4f, 14f, 86f, 26f, 81f, 38f, 86f, 50f, 81f, 62f, 86f, 74f, 81f, 86f, 86f))
            .ToString();

        // ------------------------------------------------------------------ 路13 停车场 (Fig. 219; E.177)

        /// <summary>The serif P of the parking signs (E.177 a), filling its box: the stem with its serifs, the bowl and its counter.</summary>
        public static readonly string R_P = new PicBuilder()
            .Poly(false, RCat(new[] { 9.8f, 5f, 68.9f, 5f }, RArc(68.9f, 27.4f, 22.4f, 22.4f, -90f, 90f, 14),
                new[] { 38.6f, 49.8f, 38.6f, 81f, 41f, 84.7f, 51.8f, 84.7f, 51.8f, 95f, 9.8f, 95f, 9.8f, 84.7f, 18.4f, 84.7f, 20.8f, 81f, 20.8f, 19f, 18.4f, 15.3f, 9.8f, 15.3f }))
            .Poly(true, RCat(new[] { 38.6f, 15.3f, 59.9f, 15.3f }, RArc(59.9f, 27.2f, 11.9f, 11.9f, -90f, 90f, 10), new[] { 38.6f, 39.1f }))
            .ToString();

        /// <summary>The roof over the P of the indoor car park (E.177 b), a chevron.</summary>
        public static readonly string R_Roof = RFit(new PicBuilder().Poly(false, 582f, 1165f, 717f, 1052f, 853f, 1165f, 806f, 1165f, 717f, 1092f, 628f, 1165f).ToString());

        // ------------------------------------------------------------------ 路14 错车道, 路15 港湾式紧急停车带 (Fig. 221, 222; E.178, E.179)

        /// <summary>The passing place (E.178), in the frame of the whole board (a percentage of its side): the straight edge and the edge that widens.</summary>
        public static readonly string R_PassingBay = new PicBuilder()
            .Rect(33.9f, 19.8f, 3.9f, 60.4f)
            .Poly(false, RLine(3.6f, 52.3f, 19.8f, 52.3f, 28.5f, 64.3f, 41f, 64.3f, 59.2f, 52.3f, 71.5f, 52.3f, 80.2f))
            .ToString();

        /// <summary>The lay-by (E.179), in the frame of the white panel: the road with its dashed edge (six blue squares) and the bay bulging out to the right.</summary>
        public static readonly string R_Layby = BuildLayby();

        private static string BuildLayby()
        {
            var b = new PicBuilder().Poly(false, 31.1f, 12.1f, 48.3f, 12.1f, 48.3f, 20.5f, 52.5f, 27f, 57.5f, 33f, 60.8f, 37.5f, 62.2f, 41.7f, 62.2f, 58.9f, 60.8f, 63f, 57.5f, 67.5f, 52.5f, 73.5f,
                48.3f, 78.5f, 48.3f, 87.6f, 31.1f, 87.6f);
            for (int k = 0; k < 6; k++)
                b.Rect(36.9f, 23.3f + 10.1f * k, 3.6f, 3.6f, true);
            return b.ToString();
        }

        // ------------------------------------------------------------------ 路16 人行天桥, 路17 人行地下通道 (Fig. 224, 225; E.180, E.181)

        /// <summary>The footbridge (E.180), in the frame of the board's inside: a person climbing the stairs, the railing of the deck, and the ramp under them.</summary>
        public static readonly string R_Footbridge = new PicBuilder()
            .Circle(18.9f, 14.9f, 5f)
            .Capsule(18.9f, 26.5f, 18.9f, 41f, 10.6f)
            .Capsule(22f, 30f, 31f, 37f, 4.5f)
            .Capsule(23f, 44f, 32f, 49f, 5f).Capsule(32f, 49f, 31.2f, 64.5f, 5f)
            .Capsule(18.9f, 44f, 18.9f, 73.5f, 5f)
            .Poly(false, 0f, 84.3f, 12.6f, 84.3f, 12.6f, 75.5f, 24.7f, 75.5f, 24.7f, 66.6f, 36.9f, 66.6f, 36.9f, 58.3f, 49.2f, 58.3f, 49.2f, 50f, 61.9f, 50f, 61.9f, 36.1f, 100f, 36.1f,
                100f, 54.5f, 69.2f, 54.5f, 0f, 94.4f)
            .Poly(false, 0f, 97.9f, 71.2f, 58f, 100f, 58f, 100f, 62f, 73.5f, 62f, 7.6f, 100f, 0f, 100f)
            .Rect(67.7f, 12.1f, 4.5f, 24.5f).Rect(74.7f, 12.1f, 4.5f, 24.5f).Rect(81.8f, 12.1f, 4.5f, 24.5f).Rect(89.1f, 12.1f, 4.5f, 24.5f).Rect(96.6f, 12.1f, 3.4f, 24.5f)
            .Rect(64.3f, 8.3f, 35.7f, 3f).Rect(64.3f, 8.3f, 3f, 28f)
            .Circle(65.8f, 6f, 2.8f)
            .ToString();

        /// <summary>The pedestrian underpass (E.181), in the frame of the board's inside: the ceiling's corner, a person going down the stairs, and the ramp.</summary>
        public static readonly string R_Underpass = new PicBuilder()
            .Poly(false, 0f, 0f, 62.5f, 0f, 0f, 30.8f)
            .Circle(53.8f, 24.3f, 4.8f)
            .Capsule(55f, 31.3f, 55f, 47.5f, 9.5f)
            .Capsule(52.5f, 35f, 44f, 48f, 4.5f)
            .Capsule(52f, 50f, 52f, 78.5f, 5f)
            .Capsule(56.5f, 52.5f, 63.8f, 65f, 5f)
            .Poly(false, 13f, 100f, 13f, 93.8f, 26.8f, 93.8f, 26.8f, 87f, 41.8f, 87f, 41.8f, 80f, 56.8f, 80f, 56.8f, 73.3f, 72f, 73.3f, 72f, 66.3f, 86.8f, 66.3f, 86.8f, 60f, 100f, 60f, 100f, 68f,
                32f, 100f)
            .Poly(false, 100f, 72f, 100f, 100f, 39f, 100f)
            .ToString();

        // ------------------------------------------------------------------ 路18 无障碍设施 (Fig. 227; E.182)

        /// <summary>The wheelchair (E.182), in the frame of the board's inside: the person seated, facing left, and the wheel open at the top.</summary>
        public static readonly string R_Wheelchair = new PicBuilder()
            .Circle(59.6f, 17.6f, 6.2f)
            .Capsule(61f, 29f, 58.5f, 58f, 8f)
            .Capsule(58.5f, 59f, 30f, 57f, 7.5f)
            .Capsule(30f, 57f, 21.5f, 84f, 7.5f)
            .Capsule(58f, 40f, 38f, 43f, 6f)
            .Poly(false, RBand(58.5f, 64.7f, 18.6f, 23.6f, -75f, 185f, 24))
            .ToString();

        // ------------------------------------------------------------------ 路19 to 路21 (Fig. 228 to 233; E.183 to E.185)

        /// <summary>The toilets (E.183, E.184): a man and a woman with a line between them.</summary>
        public static readonly string R_Toilet = new PicBuilder()
            .Circle(24f, 12f, 6.5f)
            .RoundRect(8f, 21f, 32f, 12f, 6f).Rect(14f, 27f, 20f, 28f)
            .Capsule(11f, 27f, 11f, 55f, 6f).Capsule(37f, 27f, 37f, 55f, 6f)
            .Rect(15f, 54f, 7.5f, 41f).Rect(25.5f, 54f, 7.5f, 41f)
            .Rect(48.5f, 5f, 3f, 90f)
            .Circle(76.5f, 12f, 6.5f)
            .Poly(false, 69f, 21f, 84f, 21f, 95f, 68f, 58f, 68f)
            .Rect(70f, 66f, 5.5f, 29f).Rect(78f, 66f, 5.5f, 29f)
            .ToString();

        /// <summary>The drinking water (E.183): a cup with water in it and steam over it.</summary>
        public static readonly string R_Drink = new PicBuilder()
            .Poly(false, 20f, 42f, 80f, 42f, 72f, 95f, 28f, 95f)
            .Poly(true, 27f, 47f, 73f, 47f, 66.5f, 89f, 33.5f, 89f)
            .Poly(false, RLine(4f, 28f, 57f, 36f, 54f, 44f, 58f, 52f, 54f, 60f, 58f, 68f, 54f, 72f, 56f))
            .Poly(false, RLine(4f, 30f, 66f, 38f, 63f, 46f, 67f, 54f, 63f, 62f, 67f, 70f, 63f))
            .Poly(false, RLine(4.5f, 36f, 36f, 32f, 28f, 37f, 20f, 33f, 12f, 37f, 5f))
            .Poly(false, RLine(4.5f, 50f, 36f, 46f, 28f, 51f, 20f, 47f, 12f, 51f, 5f))
            .Poly(false, RLine(4.5f, 64f, 36f, 60f, 28f, 65f, 20f, 61f, 12f, 65f, 5f))
            .ToString();

        /// <summary>The scenic outlook (E.185): two overlapping discs, in the left a fir tree and a cliff, in the right three waves, cut out of them.</summary>
        public static readonly string R_Scenic = BuildScenic();

        private static string BuildScenic()
        {
            var b = new PicBuilder().Poly(false, RCat(RArc(28.2f, 50f, 23.6f, 23.6f, 23.7f, 336.3f, 30), RArc(71.4f, 50f, 23.6f, 23.6f, 203.7f, 516.3f, 30)));
            // The fir tree in three tiers and its trunk.
            b.Poly(true, 21.2f, 28.8f, 25.6f, 38f, 23.6f, 38f, 28f, 47f, 25.6f, 47f, 30.6f, 58f, 22.9f, 58f, 22.9f, 64f, 19.5f, 64f, 19.5f, 58f, 11.8f, 58f, 16.8f, 47f, 14.4f, 47f, 18.8f, 38f, 16.8f, 38f);
            b.Poly(true, 32f, 41f, 40f, 39.5f, 43f, 44f, 44.5f, 50f, 46.5f, 56f, 48f, 62f, 32f, 64.5f);
            for (int k = 0; k < 3; k++)
            {
                float y = 52f + 5.5f * k;
                var wave = new List<float>();
                for (int i = 0; i <= 12; i++)
                {
                    float x = 59f + 27f * i / 12f;
                    wave.Add(x);
                    wave.Add(y + 1.6f * (float)Math.Sin(i * Math.PI / 3.0));
                }

                b.Poly(true, RLine(3.2f, wave.ToArray()));
            }

            return b.ToString();
        }

        // ------------------------------------------------------------------ 路22 应急避难设施, 路23 超限检测站 (Fig. 234, 236; E.186, E.187)

        /// <summary>The emergency shelter (E.186), in the frame of the board's inside: a person running to the left and the corner triangle at the lower right.</summary>
        public static readonly string R_Refuge = new PicBuilder()
            .Circle(38.1f, 14.3f, 7f)
            .Capsule(46.6f, 25f, 55f, 49f, 11f)
            .Stroke(6f, 52f, 23.5f, 60.7f, 23.5f, 69f, 38f)
            .Stroke(6f, 45f, 26f, 32f, 39.5f, 14f, 39.5f)
            .Capsule(51f, 48f, 28f, 88f, 8f)
            .Stroke(8f, 56f, 50f, 55.5f, 72f, 82f, 72f)
            .Poly(false, 100f, 77.5f, 100f, 100f, 77.7f, 100f)
            .ToString();

        /// <summary>The overload checking station (E.187): the weighing scale (a dial on a post) and a loaded lorry on the weighing platform.</summary>
        public static readonly string R_WeighStation = new PicBuilder()
            .Ring(16.5f, 24f, 10f, 6.5f)
            .Rect(15f, 34f, 3.6f, 50f)
            .Poly(false, 9.3f, 76.3f, 19.4f, 76.3f, 19.4f, 84.2f, 7.9f, 84.2f)
            .Rect(21.2f, 77f, 70.2f, 6.5f)
            .Poly(false, 23.7f, 51.5f, 26.6f, 39.6f, 32f, 34.6f, 45.3f, 34.6f, 45.3f, 62f, 23f, 62f)
            .Poly(true, 32.4f, 37.6f, 43.2f, 37.6f, 43.2f, 46.1f, 30.2f, 46.1f)
            .Rect(45.3f, 51.1f, 46.1f, 10.9f)
            .RoundRect(47.5f, 24.8f, 41.4f, 24.5f, 3f)
            .Rect(50f, 27f, 11f, 9f, true).Rect(64f, 27f, 11f, 9f, true).Rect(78f, 27f, 8.5f, 9f, true)
            .Rect(50f, 39f, 11f, 8f, true).Rect(64f, 39f, 11f, 8f, true).Rect(78f, 39f, 8.5f, 8f, true)
            .Circle(34.9f, 68.4f, 7.2f).Circle(34.9f, 68.4f, 3.2f, true)
            .Circle(70.9f, 68.4f, 7.2f).Circle(70.9f, 68.4f, 3.2f, true)
            .ToString();

        // ------------------------------------------------------------------ 路26 隧道出口距离 (Fig. 240; E.189)

        /// <summary>The tunnel straight ahead (Fig. 240 b): the white arch of the portal, the light of the exit far away, the road's white sides narrowing to it and the dashed middle line.</summary>
        public static readonly string R_TunnelStraight = BuildTunnel(0f);

        /// <summary>The tunnel bending to the left (Fig. 240 a): the exit to the left and the road bending to it (mirrored for a bend to the right).</summary>
        public static readonly string R_TunnelLeft = BuildTunnel(-11f);

        private static string BuildTunnel(float shift)
        {
            var b = new PicBuilder();
            // The arch: the ring between two half ellipses standing on the bottom.
            b.Poly(false, RCat(RArc(50f, 97f, 45f, 87f, 180f, 360f, 24), RArc(50f, 97f, 36f, 74f, 360f, 180f, 24)));
            // The exit: a small arch.
            float ox = 50f + shift;
            b.Poly(false, RCat(RArc(ox, 56f, 8f, 8f, 180f, 360f, 10), new[] { ox + 8f, 64f, ox - 8f, 64f }));
            // The road's sides from the exit to the bottom: the light part, with the road cut out of it and the middle line on the road.
            var sides = new List<float>();
            var road = new List<float>();
            var lineL = new List<float>();
            const int n = 10;
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                // The middle of the road: from the exit (t = 0) to the bottom's middle (t = 1), bending when shifted.
                float cx = (1 - t) * (1 - t) * ox + 2f * (1 - t) * t * 50f + t * t * 50f;
                float y = 64f + 33f * t;
                float hs = 8f + 28f * t, hr = 3.5f + 18f * t;
                sides.Add(cx - hs);
                sides.Add(y);
                road.Add(cx - hr);
                road.Add(y);
                lineL.Add(cx);
                lineL.Add(y);
            }

            float[] s = sides.ToArray(), r = road.ToArray();
            var sidePoly = new List<float>(s);
            var roadPoly = new List<float>(r);
            for (int i = n; i >= 0; i--)
            {
                float cx = lineL[i * 2], y = lineL[i * 2 + 1];
                float t = (float)i / n;
                float hs = 8f + 28f * t, hr = 3.5f + 18f * t;
                sidePoly.Add(cx + hs);
                sidePoly.Add(y);
                roadPoly.Add(cx + hr);
                roadPoly.Add(y);
            }

            b.Poly(false, sidePoly.ToArray());
            b.Poly(true, roadPoly.ToArray());
            // Dashes on the middle line, wider as they come nearer.
            float[][] dashes = { new[] { 0.2f, 0.32f, 3f }, new[] { 0.45f, 0.6f, 3.5f }, new[] { 0.75f, 0.95f, 4.2f } };
            foreach (float[] d in dashes)
            {
                int i0 = (int)Math.Round(d[0] * n), i1 = (int)Math.Round(d[1] * n);
                float x0 = lineL[i0 * 2], y0 = lineL[i0 * 2 + 1], x1 = lineL[i1 * 2], y1 = lineL[i1 * 2 + 1];
                b.Poly(false, RLine(d[2], x0, y0, x1, y1));
            }

            return b.ToString();
        }

        // ------------------------------------------------------------------ 9.4 起点、终点 (Fig. 278 to 283; E.222 to E.227)

        /// <summary>The expressway (E.222): the two carriageways going away under an overpass.</summary>
        public static readonly string R_Expressway = new PicBuilder()
            .Poly(false, 43.1f, 5f, 47.1f, 5f, 47.1f, 34f, 37.9f, 34f)
            .Poly(false, 52.9f, 5f, 56.9f, 5f, 62.6f, 34f, 52.9f, 34f)
            .Rect(15.4f, 37.2f, 69.1f, 4f)
            .Rect(23.1f, 41f, 3.7f, 3.9f).Rect(73.8f, 41f, 3.7f, 3.9f)
            .Poly(false, 35.4f, 46f, 47.1f, 46f, 47.1f, 95f, 26.2f, 95f)
            .Poly(false, 52.9f, 46f, 64.6f, 46f, 73.8f, 95f, 52.9f, 95f)
            .ToString();

        // ------------------------------------------------------------------ 9.4.4 to 9.4.7 (Fig. 284 to 289; E.228 to E.233)

        /// <summary>The broadcast waves of the traffic radio (E.228): a small square and five arcs growing to the right.</summary>
        public static readonly string R_Radio = BuildRadio();

        private static string BuildRadio()
        {
            var b = new PicBuilder().Rect(5f, 41f, 8f, 18f);
            float[] xs = { 24f, 39f, 54f, 70f, 87f };
            float[] hs = { 11f, 18f, 26f, 34f, 44f };
            for (int i = 0; i < 5; i++)
            {
                float r = hs[i] * 2.2f;
                float a = (float)(Math.Asin(hs[i] / r) * 180.0 / Math.PI);
                b.Poly(false, RBand(xs[i] - r, 50f, r - 2.4f, r + 2.4f, -a, a, 12));
            }

            return b.ToString();
        }

        /// <summary>The ticket of the toll entry (E.232): the slot of the ticket machine and the ticket coming out of it.</summary>
        public static readonly string R_Ticket = RFit(new PicBuilder().Rect(591f, 410f, 64f, 18f).Poly(false, 633f, 447f, 708f, 470f, 708f, 525f, 633f, 503f).ToString());

        /// <summary>The hand reaching for the ticket (E.232): an arm from the right with the palm up, the thumb raised.</summary>
        public static readonly string R_TicketHand = RFit(new PicBuilder()
            .Poly(false, 975f, 612f, 900f, 598f, 850f, 588f, 815f, 590f, 790f, 600f, 782f, 614f, 800f, 628f, 850f, 641f, 975f, 668f)
            .Capsule(792f, 597f, 726f, 573f, 19f)
            .Capsule(796f, 609f, 736f, 592f, 18f)
            .Capsule(822f, 590f, 806f, 545f, 19f)
            .ToString());

        /// <summary>The cloud of the bad weather sign (E.233), in the frame of its picture: a band along the top with a scalloped lower edge.</summary>
        public static readonly string R_Cloud = new PicBuilder()
            .Rect(0f, 0f, 100f, 14f)
            .Circle(13.3f, 17.6f, 12.5f).Circle(39.9f, 19.5f, 13f).Circle(66.5f, 16.8f, 12f).Circle(87.5f, 13f, 12.5f)
            .ToString();

        /// <summary>The road in fog (E.233), in the frame of its picture: the two white edges coming nearer and the dashed middle line.</summary>
        public static readonly string R_FogRoad = new PicBuilder()
            .Poly(false, 24.7f, 21.8f, 29.7f, 21.8f, 19f, 95.4f, 7.6f, 95.4f)
            .Poly(false, 66.5f, 21.8f, 72.2f, 21.8f, 93.9f, 95.4f, 81.7f, 95.4f)
            .Poly(false, 47.5f, 27.5f, 51.5f, 27.5f, 52.1f, 32.4f, 46.9f, 32.4f)
            .Poly(false, 47.2f, 45.4f, 53.2f, 45.4f, 54f, 54.2f, 46.4f, 54.2f)
            .Poly(false, 46.4f, 75.2f, 54.7f, 75.2f, 55.9f, 96.2f, 45.2f, 96.2f)
            .ToString();

        /// <summary>A white half disc (the half-round distance marks beside the road, E.233), the flat side on the left.</summary>
        public static readonly string R_HalfDisc = new PicBuilder().Poly(false, RArc(27.5f, 50f, 45f, 45f, -90f, 90f, 16)).ToString();

        // ------------------------------------------------------------------ 9.5 沿线设施 (Fig. 291 to 299; E.234 to E.243)

        /// <summary>The telephone handset (Fig. 291, E.234): a curved grip with the ear and mouth pieces turned inwards, the ear piece at the upper left.</summary>
        public static readonly string R_Phone = new PicBuilder()
            .Poly(false, RBand(80f, 20f, 52f, 68f, 100f, 166f, 18))
            .Poly(false, RLine(24f, 80f - 70f * 0.970f, 20f + 70f * 0.242f, 80f - 38f * 0.970f, 20f + 38f * 0.242f))
            .Poly(false, RLine(24f, 80f - 70f * 0.174f, 20f + 70f * 0.985f, 80f - 38f * 0.174f, 20f + 38f * 0.985f))
            .ToString();

        /// <summary>The handset turned half round (Fig. 293: the ear piece at the top left, bulging to the upper right).</summary>
        public static string R_PhoneTurned => Turned(R_Phone);

        /// <summary>A glyph turned half round about the middle of its box (x' = 100 - x, y' = 100 - y; the winding is kept).</summary>
        private static string Turned(string path)
        {
            var sb = new StringBuilder();
            foreach (float[] c in GuidePath.Map(GuideKit.Contours(path), -1f, -1f, 100f, 100f))
            {
                for (int i = 0; i + 1 < c.Length; i += 2)
                    sb.Append(i == 0 ? 'M' : 'L').Append(c[i].ToString("0.##", CultureInfo.InvariantCulture)).Append(' ').Append(c[i + 1].ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append("Z ");
            }

            return sb.ToString();
        }

        /// <summary>The toll booth (E.237, E.240): the frame of the booth, the collector in it reaching out, and the ticket.</summary>
        public static readonly string R_TollBooth = new PicBuilder()
            .Rect(5f, 5f, 8f, 90f).Rect(5f, 5f, 75f, 8f).Rect(72f, 5f, 8f, 26f).Rect(5f, 87f, 52f, 8f)
            .Circle(40f, 35f, 10f)
            .Poly(false, RCat(RArc(32f, 58f, 8f, 8f, 180f, 270f, 5), RArc(46f, 58f, 8f, 8f, 270f, 360f, 5), new[] { 54f, 87f, 24f, 87f }))
            .Stroke(8f, 48f, 60f, 64f, 70f, 72f, 70f)
            .Rect(81f, 64f, 14f, 9f)
            .ToString();

        /// <summary>The spanner of the repair service (E.242): an open jaw on top of a handle.</summary>
        public static readonly string R_Wrench = new PicBuilder()
            .Poly(false, RBand(50f, 24f, 9f, 19f, -60f, 240f, 20))
            .Poly(false, 43f, 38f, 57f, 38f, 55f, 90f, 45f, 90f)
            .Circle(50f, 90f, 5f)
            .ToString();

        /// <summary>The fork and knife of the restaurant (E.242), crossed.</summary>
        public static readonly string R_ForkKnife = new PicBuilder()
            .Capsule(12f, 90f, 66f, 30f, 9f)
            .Poly(false, 60f, 36f, 86f, 6f, 93f, 11f, 72f, 44f)
            .Capsule(88f, 90f, 40f, 40f, 9f)
            .Poly(false, 26f, 40f, 46f, 20f, 52f, 26f, 32f, 46f)
            .Capsule(30f, 36f, 12f, 18f, 4.5f).Capsule(36f, 30f, 18f, 12f, 4.5f).Capsule(42f, 24f, 24f, 6f, 4.5f)
            .ToString();

        /// <summary>The bed of the lodging (E.242): the head and the foot of the bed, the mattress, a person's head on the pillow.</summary>
        public static readonly string R_Bed = new PicBuilder()
            .Rect(5f, 28f, 8f, 62f).Rect(87f, 52f, 8f, 38f)
            .Rect(5f, 70f, 90f, 9f)
            .Rect(13f, 58f, 74f, 12f)
            .Circle(25f, 49f, 7.5f)
            .Poly(false, 34f, 58f, 38f, 48f, 87f, 48f, 87f, 58f)
            .ToString();

        /// <summary>The coffee cup of the rest area (E.243): the cup, its handle and the saucer.</summary>
        public static readonly string R_Cup = new PicBuilder()
            .Poly(false, RCat(new[] { 8f, 28f, 74f, 28f }, RArc(41f, 28f, 33f, 34f, 0f, 180f, 18)))
            .Poly(false, RBand(78f, 40f, 6f, 13f, -110f, 120f, 14))
            .Capsule(10f, 76f, 82f, 76f, 7f)
            .ToString();

        /// <summary>The climbing lane (Fig. 300 b, E.244 b): the through lane going on, the lane added on its right bending out and up to the arrow head.</summary>
        public static readonly string R_ClimbLane = new PicBuilder()
            .Poly(false, RLine(11f, 14f, 95f, 14f, 56f, 48f, 32f))
            .Poly(false, RLine(11f, 48f, 95f, 48f, 70f, 74f, 50f, 74f, 30f))
            .Poly(false, 74f, 5f, 95f, 33f, 53f, 33f)
            .ToString();
    }
}
