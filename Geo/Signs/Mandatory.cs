using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The mandatory (指示) signs of GB 5768.2-2022 chapter 6 (示1 to 示43): blue ground, white figures; round (Table 7: D 120, 100, 80, 60 cm) or rectangular (Table 7's rows for one-way, lane
    /// direction, the squares and the B x C rectangles of the lane signs). The figures are redrawn from the dimensioned drawings of Appendix E (E.50 to E.99) and placed where those drawings
    /// put them: the pictograms are in Glyphs.Mandatory.cs (M_...), the figures of a sign are in the frame of its board (Glyphs.MandFigure).
    /// </summary>
    public static class MandatorySigns
    {
        private const string G = StandardSigns.Mandatory;

        /// <summary>The code of the urban signs of 0.12.78: boards drawn from photographs that the 2022 standard has no number for.</summary>
        public const string UrbanCode = "城市";

        /// <summary>Table 7: the liner c (1, 0.8, 0.6, 0.4 cm) and the ring a of the round signs (12, 10, 8, 6 cm) by band.</summary>
        private static readonly float[] LinerC = { 0.010f, 0.008f, 0.006f, 0.004f };

        private static readonly float[] RingA = { 0.12f, 0.10f, 0.08f, 0.06f };

        private static float Liner(int band) => LinerC[SignStyles.Band(band)];

        /// <summary>4.6.6: the corner radius of the rectangular signs that are not guide, notice or auxiliary boards.</summary>
        private const float Corner = 0.04f;

        // ------------------------------------------------------------------ styles

        /// <summary>
        /// A rectangular mandatory sign (Fig. 1 d and f): a blue board w x h with the white liner c at its very edge and corners of 4 cm. (SignStyles.Rectangle draws the guide boards' edge of
        /// Fig. 3, a blue liner outside a white border, which the mandatory signs do not have.) The canvas is the whole board; the safe region the inside of the liner.
        /// </summary>
        internal static GuideBoard Board(float w, float h, float liner, Action<Canvas> art)
        {
            var part = new GuidePart
            {
                X = 0f, Y = 0f, W = w, H = h, Shape = GuidePartShape.RoundRect, Ground = GuideColour.Blue, BorderColour = GuideColour.White, Liner = 0f, Border = liner, Radius = Corner
            };
            part.Safe = new[] { liner, liner, w - liner, liner, w - liner, h - liner, liner, h - liner };
            art?.Invoke(new Canvas(part, 0f, 0f, w, h));
            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        /// <summary>The fluorescent yellow-green frame that 示16 (6.13: 10 cm wide) and the school bus 示40 (6.31 d: 10 to 15 cm; 10 is drawn) may have, added round the sign without making it smaller.</summary>
        private const float FrameWidth = 0.10f;

        /// <summary>
        /// A mandatory square with a frame of fluorescent yellow-green round it (Fig. 97 b, 128 a): the blue square of side a with its white liner, kept at its size, in a frame of the given width. One part:
        /// the frame is the part's ground and its outer corner (the frame's width plus the sign's own corner: the figure's measures 0.13 of its side, this 0.14), the sign is two elements over it, and the
        /// canvas of the figure is the blue square, so every figure of the sign stays where its drawing puts it.
        /// </summary>
        private static GuideBoard Framed(float a, float liner, float frame, Action<Canvas> art)
        {
            float w = a + 2f * frame;
            var part = new GuidePart
            {
                X = 0f, Y = 0f, W = w, H = w, Shape = GuidePartShape.RoundRect, Ground = GuideColour.FluorescentYellowGreen, BorderColour = GuideColour.FluorescentYellowGreen, Liner = 0f, Border = 0f,
                Radius = Corner + frame
            };
            part.Safe = new[] { frame + liner, frame + liner, frame + a - liner, frame + liner, frame + a - liner, frame + a - liner, frame + liner, frame + a - liner };
            part.Elements.Add(GuideKit.Rect(frame, frame, a, a, GuideColour.White, Corner));
            part.Elements.Add(GuideKit.Rect(frame + liner, frame + liner, a - 2f * liner, a - 2f * liner, GuideColour.Blue, Math.Max(0f, Corner - liner)));
            art?.Invoke(new Canvas(part, frame, frame, a, a));
            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        private static GuideField FrameField(Func<GuideSpec, bool> visible = null) =>
            GuideField.Flag("frame", "荧光边框", "Fluorescent frame", false, visible).AsSparse();

        /// <summary>A round sign of any diameter: the white liner c round a disc of the ground colour; the canvas is the whole disc (示38 is 40 cm whatever the road).</summary>
        private static GuideBoard Round(float d, float c, Action<Canvas> art, GuideColour ground = GuideColour.Blue)
        {
            var part = new GuidePart { X = 0f, Y = 0f, W = d, H = d, Shape = GuidePartShape.Circle, Ground = GuideColour.White, Radius = d * 0.5f };
            part.Elements.Add(GuideKit.Oval(c, c, d - 2f * c, d - 2f * c, ground));
            part.Safe = CircleSafe(d, d * 0.5f - c);
            art?.Invoke(new Canvas(part, 0f, 0f, d, d));
            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        /// <summary>示43 (Table 7 note b): the round sign of the prohibition signs with a green ring instead of the red one, on white, the figure in black; no bar.</summary>
        private static GuideBoard GreenRing(int band, Action<Canvas> art)
        {
            band = SignStyles.Band(band);
            float d = SignStyles.CircleDiameter(band), a = RingA[band], c = LinerC[band];
            var part = new GuidePart { X = 0f, Y = 0f, W = d, H = d, Shape = GuidePartShape.Circle, Ground = GuideColour.White, Radius = d * 0.5f };
            part.Elements.Add(GuideKit.Oval(c, c, d - 2f * c, d - 2f * c, GuideColour.Green));
            part.Elements.Add(GuideKit.Oval(c + a, c + a, d - 2f * (c + a), d - 2f * (c + a), GuideColour.White));
            part.Safe = CircleSafe(d, d * 0.5f - c - a);
            art?.Invoke(new Canvas(part, 0f, 0f, d, d));
            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        private static float[] CircleSafe(float d, float radius)
        {
            var points = new float[96];
            float r = radius * 0.998f;
            for (int i = 0; i < 48; i++)
            {
                double a = 2.0 * Math.PI * i / 48;
                points[i * 2] = d * 0.5f + r * (float)Math.Cos(a);
                points[i * 2 + 1] = d * 0.5f + r * (float)Math.Sin(a);
            }

            return points;
        }

        // ------------------------------------------------------------------ drawing on a canvas

        /// <summary>
        /// A path put into the area (units of the canvas) exactly as Pic does, but with the element's box set to what is really drawn (Pic gives it the area, which for a figure written in the
        /// frame of the whole board would reach over the board's edge).
        /// </summary>
        private static void PicExact(Canvas c, string path, GuideColour colour, float ux, float uy, float uw, float uh)
        {
            if (string.IsNullOrEmpty(path))
                return;
            GuideElement e = GuideKit.Picture(path, c.X + c.Mx(ux), c.Y + c.My(uy), c.Mx(uw), c.My(uh), colour);
            GuidePath.Bounds(e.Contours, out float x0, out float y0, out float x1, out float y1);
            e.X = x0;
            e.Y = y0;
            e.W = x1 - x0;
            e.H = y1 - y0;
            c.Add(e);
        }

        /// <summary>A figure written in the frame of the board (0 to 100 across its width, the same scale downwards) over the whole canvas.</summary>
        private static void Fig(Canvas c, string key, GuideColour colour, bool mirror = false)
        {
            string path = Glyphs.MandFigure(key);
            PicExact(c, mirror ? Glyphs.Mirror(path) : path, colour, 0f, 0f, 100f, 100f * c.W / c.H);
        }

        /// <summary>A pictogram fitted into a box given in cm of a reference board refW x refH (the drawing of Appendix E); the sign's board may be any multiple of it.</summary>
        private static void FitRef(Canvas c, string glyph, GuideColour colour, float x0, float y0, float x1, float y1, float refW, float refH) =>
            c.Fit(glyph, colour, x0 / refW * 100f, y0 / refH * 100f, (x1 - x0) / refW * 100f, (y1 - y0) / refH * 100f);

        /// <summary>Chinese characters in square cells of side h (m) with gap (m), the run centred on cx (units of the canvas width), its top at y (m from the canvas top).</summary>
        private static void CellsAt(Canvas c, string text, GuideColour colour, float cx, float y, float h, float gap)
        {
            text = text ?? "";
            if (text.Length == 0)
                return;
            float w = GuideKit.CellWidth(text.Length, h, gap);
            c.Cells(text, colour, cx - w * 0.5f / c.W * 100f, y / c.H * 100f, h / c.H * 100f, gap / c.H * 100f);
        }

        private static GuideChoice C(string value, string zh, string en) => new GuideChoice(value, zh, en);

        // ------------------------------------------------------------------ the round signs with arrows (示1 to 示9)

        private static readonly GuideChoice[] kLeftRight = { C("l", "向左", "Left"), C("r", "向右", "Right") };

        private static GuideBoard Disc(int band, string key, bool mirror = false) =>
            SignStyles.MandatoryRound(band, c => Fig(c, key, GuideColour.White, mirror), 1f);

        // ------------------------------------------------------------------ the lane direction boards (示17 to 示23, Fig. 99 to 106, E.66 to E.73)

        /// <summary>Table 7: the lane direction board is 90 x 70 cm whatever the road; liner 0.6 cm (note e). A board of n lanes side by side (Fig. 106, E.73) shares the dashed lines: 75 n + 15 cm.</summary>
        private const float LaneH = 0.70f, LanePitch = 0.75f, LaneLiner = 0.006f;

        private static readonly GuideChoice[] kLaneCombos =
        {
            C("r", "右转（示17）", "Right turn (示17)"), C("l", "左转（示18）", "Left turn (示18)"), C("a", "直行（示19）", "Ahead (示19)"),
            C("ar", "直行和右转（示20）", "Ahead and right (示20)"), C("al", "直行和左转（示21）", "Ahead and left (示21)"), C("u", "掉头（示22）", "U-turn (示22)"),
            C("ul", "掉头和左转（示23）", "U-turn and left (示23)"), C("lr", "左转和右转（自定）", "Left and right (custom)"), C("alr", "直行、左转和右转（自定）", "Ahead, left and right (custom)")
        };

        private static readonly GuideChoice[] kLaneCount =
        {
            C("1", "1 条车道", "1 lane"), C("2", "2 条车道", "2 lanes"), C("3", "3 条车道", "3 lanes"), C("4", "4 条车道", "4 lanes"), C("5", "5 条车道", "5 lanes"), C("6", "6 条车道", "6 lanes")
        };

        private static int Lanes(GuideSpec s) => Math.Max(1, Math.Min(6, (int)s.Number("lanes", 1)));

        private static GuideBoard LaneBoard(GuideSpec s)
        {
            int n = Lanes(s);
            float w = LanePitch * n + 0.15f;
            return Board(w, LaneH, LaneLiner, c =>
            {
                float k = 100f / (w * 100f);
                // The dashed lines: 5 cm wide, 12 cm long at 5, 29 and 53 cm from the top, at 5 + 75 i cm from the left (E.66).
                for (int i = 0; i <= n; i++)
                {
                    foreach (float y in new[] { 5f, 29f, 53f })
                        c.Rect(GuideColour.White, (5f + 75f * i) * k, y / 70f * 100f, 5f * k, 12f / 70f * 100f);
                }

                for (int i = 0; i < n; i++)
                {
                    string combo = s.Get("lane" + (i + 1));
                    // The figure of one lane is written in the frame of the 90 cm board (0..100 across 90 cm): put it at 75 i cm.
                    PicExact(c, Glyphs.MandFigure("lane:" + combo), GuideColour.White, 75f * i * k, 0f, 90f * k, 100f * 90f / 70f);
                }
            });
        }

        // ------------------------------------------------------------------ the lane signs (示25 to 示34, Fig. 108 to 117, E.75 to E.84)

        /// <summary>Table 7, rectangle B x C with its note d: 120 x 96, 100 x 80, 80 x 64, 60 x 48 cm with an arrow; 120 x 60, 100 x 50, 80 x 40, 60 x 30 cm without.</summary>
        private static readonly float[] LaneSignW = { 1.20f, 1.00f, 0.80f, 0.60f }, LaneSignH = { 0.96f, 0.80f, 0.64f, 0.48f }, LaneSignHNoArrow = { 0.60f, 0.50f, 0.40f, 0.30f };

        private static readonly GuideChoice[] kLaneArrows =
        {
            C("down", "向下（设在车道正上方）", "Down (over the lane)"), C("dl", "斜向左下（指向左侧车道）", "Down to the left"), C("dr", "斜向右下（指向右侧车道）", "Down to the right"),
            C("none", "省略（设在车道正上方）", "None (over the lane)")
        };

        /// <summary>
        /// A lane sign: a blue rectangle with the dashed lane lines at both sides, the figure (drawn by figure in cm of the 100 x 80 cm board of E.75; y0 to y1 is the height it takes), and the
        /// arrow below it (E.75 a, c). Without the arrow the board is the shorter one and the figure is centred in its height (E.75 b, E.76 b: it keeps its size and its place across).
        /// </summary>
        private static GuideBoard LaneSign(int band, string arrow, float y0, float y1, Action<Canvas, float> figure)
        {
            band = SignStyles.Band(band);
            bool none = arrow == "none";
            bool noArrow = none || arrow == "hov";
            float w = LaneSignW[band], h = none ? LaneSignHNoArrow[band] : LaneSignH[band];
            // The drawing's board is 100 cm wide; with the arrow it is 80 of them high, without it 50.
            float refH = none ? 50f : 80f;
            return Board(w, h, Liner(band), c =>
            {
                // The dashes: 5 cm wide at 5 and 90 cm; with the arrow four 12 cm long every 19.33 cm from 5 (E.75 a), without it three 10 cm long every 16 cm from 4 (E.76 b).
                float[] ys = none ? new[] { 4f, 20f, 36f } : new[] { 5f, 24.33f, 43.67f, 63f };
                float len = none ? 10f : 12f;
                foreach (float x in new[] { 5f, 90f })
                {
                    foreach (float y in ys)
                        c.Rect(GuideColour.White, x, y / refH * 100f, 5f, len / refH * 100f);
                }

                figure(c, none ? 25f - (y0 + y1) * 0.5f : 0f);
                if (!noArrow)
                {
                    string key = arrow == "down" ? "laneDown" : "laneDownLeft";
                    PicExact(c, arrow == "dr" ? Glyphs.Mirror(Glyphs.MandFigure(key)) : Glyphs.MandFigure(key), GuideColour.White, 0f, 0f, 100f, 125f);
                }
            });
        }

        /// <summary>The height of the board in cm of the drawing (100 cm wide): 80 with the arrow, 50 without.</summary>
        private static float RefH(Canvas c) => 100f * c.H / c.W;

        /// <summary>A pictogram fitted into a box given in cm of the 100 x 80 board of E.75, moved down by dy cm.</summary>
        private static void LaneFit(Canvas c, string glyph, float x0, float y0, float x1, float y1, float dy) =>
            FitRef(c, glyph, GuideColour.White, x0, y0 + dy, x1, y1 + dy, 100f, RefH(c));

        /// <summary>
        /// The characters above the figure of a lane sign: cells h cm of the drawing high with the gap, from y. On the 60 x 48 cm board (and its short form) the note of E.77 and E.78 asks
        /// for characters 10 cm high, larger than the drawing's: they are made so, kept in proportion with their gap, and moved up to 3 cm of the drawing from the top.
        /// </summary>
        private static void LaneText(Canvas c, string text, float y, float h, float gap, float dy)
        {
            // The canvas is the board: metres per cm of the drawing are its width over 100.
            float cm = c.W / 100f;
            if (h * cm < 0.10f - 1e-4f && c.W < 0.61f)
            {
                float k = 0.10f / (h * cm);
                h *= k;
                gap *= k;
                y = 3f;
                // Between the dashed lines (their inner edges are 10 and 90 cm of the drawing) with 4 cm of room at each side: the gap comes down to a tenth of the cell first.
                int n = (text ?? "").Length;
                if (n > 1 && n * h + (n - 1) * gap > 72f)
                    gap = Math.Max(0.1f * h, (72f - n * h) / (n - 1));
                if (n > 0 && n * h + (n - 1) * gap > 72f)
                {
                    float s = 72f / (n * h + (n - 1) * gap);
                    h *= s;
                    gap *= s;
                }
            }

            CellsAt(c, text, GuideColour.White, 50f, (y + dy) * cm, h * cm, gap * cm);
        }

        // ------------------------------------------------------------------ the catalogue

        public static IEnumerable<GuideTemplate> All()
        {
            // 示1 直行 (6.2, Fig. 77, E.50).
            yield return StandardSigns.Make("gb.mand.01", "示1", "直行", "Ahead only", G, "zx", StandardSigns.Words("直行", "前进", "ahead", "straight"),
                band => Disc(band, "ahead"));

            // 示2, 示3 向左（或向右）转弯 (6.3, Fig. 79, 80, E.51, E.52): one sign, the direction a setting; 示3 is 示2 mirrored.
            yield return StandardSigns.Make("gb.mand.02", "示2/示3", "向左（或向右）转弯", "Turn left / turn right", G, "xzzw", StandardSigns.Words("左转", "右转", "转弯", "turn left", "turn right"),
                (spec, band) => Disc(band, "turnL", spec.Is("dir", "r")),
                new[] { GuideField.Choice("dir", "方向", "Direction", "l", kLeftRight) });

            // 示4, 示5 直行和向左（或向右）转弯 (6.4, Fig. 82, 83, E.53, E.54).
            yield return StandardSigns.Make("gb.mand.04", "示4/示5", "直行和向左（或向右）转弯", "Ahead or turn left / right", G, "zxhxzzw", StandardSigns.Words("直行", "左转", "右转", "ahead", "turn"),
                (spec, band) => Disc(band, "aheadL", spec.Is("dir", "r")),
                new[] { GuideField.Choice("dir", "转弯方向", "Turn", "l", kLeftRight) });

            // 示6 向左和向右转弯 (6.5, Fig. 84, E.55).
            yield return StandardSigns.Make("gb.mand.06", "示6", "向左和向右转弯", "Turn left or right", G, "xzhxyzw", StandardSigns.Words("左转", "右转", "转弯", "left or right"),
                band => Disc(band, "leftRight"));

            // 示7, 示8 分隔带右侧（或左侧）行驶 (6.6, Fig. 85, 86, E.56, E.57): the arrow down to the right, or mirrored down to the left.
            yield return StandardSigns.Make("gb.mand.07", "示7/示8", "分隔带右侧（或左侧）行驶", "Keep right / keep left", G, "fgdycxs", StandardSigns.Words("靠右", "靠左", "分隔带", "keep right", "keep left"),
                (spec, band) => Disc(band, "keepR", spec.Is("side", "l")),
                new[] { GuideField.Choice("side", "行驶侧", "Side", "r", new[] { C("r", "右侧（示7）", "Right (示7)"), C("l", "左侧（示8）", "Left (示8)") }) });

            // 示9 环岛行驶 (6.7, Fig. 88, E.58).
            yield return StandardSigns.Make("gb.mand.09", "示9", "环岛行驶", "Roundabout", G, "hdxs", StandardSigns.Words("环岛", "环形", "roundabout"),
                band => Disc(band, "roundabout"));

            // 示10 单行路（向左或向右）(6.8, Fig. 90, E.59): Table 7, 100 x 50, 80 x 40, 60 x 30 cm (none is given for 100 to 120 km/h: the largest is used); the arrow alone (a) or with 单行路 (b, c).
            yield return StandardSigns.Make("gb.mand.10", "示10", "单行路（向左或向右）", "One way (left or right)", G, "dxl", StandardSigns.Words("单行", "单行路", "one way"),
                (spec, band) => OneWay(band, spec.Is("dir", "l"), spec.Flag("text")),
                new[] { GuideField.Choice("dir", "方向", "Direction", "r", kLeftRight), GuideField.Flag("text", "加“单行路”字样", "With 单行路", false) });

            // 示11 单行路（直行）(6.8, Fig. 91, E.60): the same board standing.
            yield return StandardSigns.Make("gb.mand.11", "示11", "单行路（直行）", "One way (ahead)", G, "dxlzx", StandardSigns.Words("单行", "单行路", "直行", "one way"),
                (spec, band) => OneWayAhead(band, spec.Flag("text")),
                new[] { GuideField.Flag("text", "加“单行路”字样", "With 单行路", false) });

            // 示12 鸣喇叭 (6.9, Fig. 93, E.61).
            yield return StandardSigns.Make("gb.mand.12", "示12", "鸣喇叭", "Sound horn", G, "mlb", StandardSigns.Words("鸣笛", "喇叭", "horn"),
                band => Disc(band, "horn"));

            // 示13 开车灯 (6.10, Fig. 94, E.62).
            yield return StandardSigns.Make("gb.mand.13", "示13", "开车灯", "Switch on headlights", G, "kcd", StandardSigns.Words("车灯", "开灯", "隧道", "headlights"),
                band => Disc(band, "headlamp"));

            // 示14 最低限速 (6.11, Fig. 95, E.63): the number is a setting (60 in the figure).
            yield return StandardSigns.Make("gb.mand.14", "示14", "最低限速", "Minimum speed", G, "zdxs", StandardSigns.Words("最低", "限速", "低速", "minimum speed"),
                (spec, band) => MinimumSpeed(band, spec.Get("limit")),
                new[] { GuideField.Digits("limit", "最低限速值（km/h）", "Minimum (km/h)", "60", 3) });

            // 示15 会车先行 (6.12, Fig. 96, E.64): a square (Table 7: 120, 100, 80, 60 cm); the white arrow ahead, the red one coming.
            yield return StandardSigns.Make("gb.mand.15", "示15", "会车先行", "Priority over oncoming traffic", G, "hcxx", StandardSigns.Words("会车", "先行", "优先", "priority"),
                band => Square(band, c =>
                {
                    Fig(c, "priorityOncoming", GuideColour.Red);
                    Fig(c, "priorityAhead", GuideColour.White);
                }));

            // 示16 人行横道 (6.13, Fig. 97 a, E.65): a square; a white triangle, the person and the zebra in black. The 10 cm frame of fluorescent yellow-green of Fig. 97 b is a setting ("可": may):
            // off by default, and then the board is the one of 0.12.79.
            yield return StandardSigns.Make("gb.mand.16", "示16", "人行横道", "Pedestrian crossing", G, "rxhd", StandardSigns.Words("人行横道", "斑马线", "行人", "荧光", "crossing"),
                (spec, band) => spec.Flag("frame") ? Framed(SignStyles.SquareSide(band), Liner(band), FrameWidth, Crossing) : Square(band, Crossing), new[] { FrameField() });

            // 示17 to 示23 车道行驶方向 (6.14, Fig. 99 to 106, E.66 to E.73): one board of one to six lanes; the arrows of every lane are a setting.
            {
                var fields = new List<GuideField> { GuideField.Choice("lanes", "车道数（并设）", "Lanes side by side", "1", kLaneCount) };
                string[] defaults = { "r", "a", "a", "a", "a", "l" };
                for (int i = 1; i <= 6; i++)
                {
                    int lane = i;
                    fields.Add(GuideField.Choice("lane" + i, "第 " + i + " 车道（左起）", "Lane " + i + " (from the left)", lane == 1 ? "r" : defaults[lane - 1], kLaneCombos, s => Lanes(s) >= lane));
                }

                yield return StandardSigns.Make("gb.mand.17", "示17~示23", "车道行驶方向", "Lane direction", G, "cdxsfx",
                    StandardSigns.Words("车道", "导向", "方向", "转弯车道", "掉头", "lane"),
                    (spec, band) => LaneBoard(spec), fields.ToArray());
            }

            // 示24 机动车行驶 (6.15, Fig. 107, E.74).
            yield return StandardSigns.Make("gb.mand.24", "示24", "机动车行驶", "Motor vehicles only", G, "jdcxs", StandardSigns.Words("机动车", "汽车", "motor vehicles"),
                band => Disc(band, "carFront"));

            // 示25 to 示34: the lane signs; the arrow (down, down to the left or right, none) is a setting.
            GuideField arrowField = GuideField.Choice("arrow", "箭头", "Arrow", "down", kLaneArrows);

            // 示25 机动车车道 (6.16, Fig. 108, E.75).
            yield return StandardSigns.Make("gb.mand.25", "示25", "机动车车道", "Motor vehicle lane", G, "jdccd", StandardSigns.Words("机动车道", "车道", "motor vehicle lane"),
                (spec, band) => LaneSign(band, spec.Get("arrow"), 4.8f, 47.1f, (c, dy) => LaneFit(c, Glyphs.M_CarFront, 23.8f, 4.8f, 76.2f, 47.1f, dy)), new[] { arrowField });

            // 示26 小型客车车道 (6.17, Fig. 109, E.76).
            yield return StandardSigns.Make("gb.mand.26", "示26", "小型客车车道", "Car lane", G, "xxkccd", StandardSigns.Words("小客车", "小型客车", "车道", "car lane"),
                (spec, band) => LaneSign(band, spec.Get("arrow"), 12.2f, 38.8f, (c, dy) => LaneFit(c, Glyphs.M_CarSide, 18.3f, 12.2f, 81.9f, 38.8f, dy)), new[] { arrowField });

            // 示27 公交专用车道 (6.18, Fig. 110, E.77): 公交 above the bus (a, b: buses only) or the bus alone, moved up (c, d: buses and commuter coaches).
            yield return StandardSigns.Make("gb.mand.27", "示27", "公交专用车道", "Bus lane", G, "gjzycd", StandardSigns.Words("公交", "公交车道", "专用道", "bus lane"),
                (spec, band) => LaneSign(band, spec.Get("arrow"), spec.Flag("text") ? 7.6f : 14.5f, spec.Flag("text") ? 45.4f : 37f, (c, dy) =>
                {
                    if (spec.Flag("text"))
                    {
                        LaneText(c, "公交", 7.6f, 10f, 5.6f, dy);
                        LaneFit(c, Glyphs.M_Bus, 15.1f, 22.3f, 85.2f, 45.4f, dy);
                    }
                    else
                    {
                        LaneFit(c, Glyphs.M_Bus, 14.8f, 14.5f, 85.1f, 37f, dy);
                    }
                }),
                new[] { arrowField, GuideField.Flag("text", "加“公交”字样（仅公交车）", "With 公交 (buses only)", true) });

            // 示28 快速公交系统（BRT）专用车道 (6.19, Fig. 111, E.78).
            yield return StandardSigns.Make("gb.mand.28", "示28", "快速公交系统（BRT）专用车道", "BRT lane", G, "ksgjxtzycd", StandardSigns.Words("快速公交", "BRT", "专用道", "bus rapid transit"),
                (spec, band) => LaneSign(band, spec.Get("arrow"), 7.4f, 45.1f, (c, dy) =>
                {
                    LaneText(c, "快速公交", 7.4f, 10f, 2.8f, dy);
                    LaneFit(c, Glyphs.M_Bus, 14.6f, 22.5f, 85.5f, 45.1f, dy);
                }), new[] { arrowField });

            // 示29 有轨电车专用车道 (6.20, Fig. 112, E.79).
            yield return StandardSigns.Make("gb.mand.29", "示29", "有轨电车专用车道", "Tram lane", G, "ygdczycd", StandardSigns.Words("有轨电车", "电车", "专用道", "tram"),
                (spec, band) => LaneSign(band, spec.Get("arrow"), 4.8f, 46.9f, (c, dy) => LaneFit(c, Glyphs.M_Tram, 19.9f, 4.8f, 80.5f, 46.9f, dy)), new[] { arrowField });

            // 示30 多乘员车辆（HOV）专用车道 (6.21, Fig. 113, E.80): the board without an arrow but of the full height; the number of people is a setting ("3+" in the figure).
            yield return StandardSigns.Make("gb.mand.30", "示30", "多乘员车辆（HOV）专用车道", "HOV lane", G, "dcyclzycd", StandardSigns.Words("多乘员", "HOV", "合乘", "专用道", "HOV lane"),
                (spec, band) => Hov(band, spec.Get("people")),
                new[] { GuideField.Digits("people", "最少乘员数", "Least occupants", "3", 1) });

            // 示31 非机动车行驶 (6.22, Fig. 114, E.81).
            yield return StandardSigns.Make("gb.mand.31", "示31", "非机动车行驶", "Non-motor vehicles only", G, "fjdcxs", StandardSigns.Words("非机动车", "自行车", "bicycles"),
                band => Disc(band, "bicycle"));

            // 示32 非机动车车道 (6.23, Fig. 115, E.82).
            yield return StandardSigns.Make("gb.mand.32", "示32", "非机动车车道", "Non-motor vehicle lane", G, "fjdccd", StandardSigns.Words("非机动车道", "自行车道", "bicycle lane"),
                (spec, band) => LaneSign(band, spec.Get("arrow"), 6.4f, 42.2f, (c, dy) => LaneFit(c, Glyphs.M_Bicycle, 18.3f, 6.4f, 81.6f, 42.2f, dy)), new[] { arrowField });

            // 示33 电动自行车行驶 (6.24, Fig. 116, E.83).
            yield return StandardSigns.Make("gb.mand.33", "示33", "电动自行车行驶", "Electric bicycles only", G, "ddzxcxs", StandardSigns.Words("电动自行车", "电动车", "e-bike"),
                band => Disc(band, "ebike"));

            // 示34 电动自行车车道 (6.25, Fig. 117, E.84).
            yield return StandardSigns.Make("gb.mand.34", "示34", "电动自行车车道", "Electric bicycle lane", G, "ddzxccd", StandardSigns.Words("电动自行车道", "电动车道", "e-bike lane"),
                (spec, band) => LaneSign(band, spec.Get("arrow"), 5.2f, 46.1f, (c, dy) => LaneFit(c, Glyphs.M_EBike, 15.9f, 5.2f, 83.9f, 46.1f, dy)), new[] { arrowField });

            // 示35 行人 (6.26, Fig. 118, E.85).
            yield return StandardSigns.Make("gb.mand.35", "示35", "行人", "Pedestrians only", G, "xr", StandardSigns.Words("行人", "步行", "步行街", "pedestrians"),
                band => Disc(band, "walker"));

            // 示36 非机动车与行人分开空间通行 (6.27, Fig. 119 a, b, E.86, E.87): which side the pedestrians are on is a setting (36-1: left, 36-2: right).
            yield return StandardSigns.Make("gb.mand.36", "示36", "非机动车与行人分开空间通行", "Segregated cycle and pedestrian path", G, "fjdcyxrfkkjtx",
                StandardSigns.Words("非机动车", "行人", "分开", "分隔", "segregated"),
                (spec, band) => Disc(band, spec.Is("side", "r") ? "36-2" : "36-1"),
                new[] { GuideField.Choice("side", "行人一侧", "Pedestrians on", "l", new[] { C("l", "左侧（示36-1）", "The left (示36-1)"), C("r", "右侧（示36-2）", "The right (示36-2)") }) });

            // 示37 非机动车与行人共享空间通行 (6.27, Fig. 119 c, E.88).
            yield return StandardSigns.Make("gb.mand.37", "示37", "非机动车与行人共享空间通行", "Shared cycle and pedestrian path", G, "fjdcyxrgxkjtx", StandardSigns.Words("非机动车", "行人", "共享", "shared"),
                band => Disc(band, "37"));

            // 示38 非机动车推行 (6.29, Fig. 121, E.89): 40 cm across whatever the road (6.29); the liner is Table 7's least (0.4 cm).
            yield return StandardSigns.Make("gb.mand.38", "示38", "非机动车推行", "Push bicycles", G, "fjdctx", StandardSigns.Words("推行", "下车推行", "自行车", "push"),
                band => Round(0.40f, 0.004f, c =>
                {
                    Fig(c, "pusherBike", GuideColour.White);
                    Fig(c, "pusherHalo", GuideColour.Blue);
                    Fig(c, "pusher", GuideColour.White);
                }));

            // 示39 靠右侧车道行驶 (6.30, Fig. 122, E.90).
            yield return StandardSigns.Make("gb.mand.39", "示39", "靠右侧车道行驶", "Keep to the right-hand lane", G, "kycdxs", StandardSigns.Words("靠右", "右侧车道", "货车", "keep right lane"),
                band => Disc(band, "truckRear"));

            // 示40 停车位 (6.31, Fig. 124 and 127 to 133, E.91 to E.94): the variant is a setting.
            yield return StandardSigns.Make("gb.mand.40", "示40", "停车位", "Parking", G, "tcw", StandardSigns.Words("停车", "停车场", "P", "parking"),
                (spec, band) => Parking(band, spec.Get("kind"), spec.Flag("frame")),
                new[] { GuideField.Choice("kind", "种类", "Kind", "p", kParkingKinds), FrameField(s => s.Is("kind", "school")) });

            // 示41 允许掉头 (6.32, Fig. 134, E.95): a square.
            yield return StandardSigns.Make("gb.mand.41", "示41", "允许掉头", "U-turn permitted", G, "yxdt", StandardSigns.Words("掉头", "调头", "U-turn"),
                band => Square(band, c => Fig(c, "uturn", GuideColour.White)));

            // 示42 硬路肩允许行驶 (6.33, Fig. 136, E.96 to E.98): a square; the stage (begins, ends soon, ends) is a setting.
            yield return StandardSigns.Make("gb.mand.42", "示42", "硬路肩允许行驶", "Hard shoulder running", G, "hljyxxs", StandardSigns.Words("硬路肩", "路肩", "应急车道", "hard shoulder"),
                (spec, band) => Square(band, c => Shoulder(c, spec.Get("stage"), Liner(band) / SignStyles.SquareSide(band))),
                new[] { GuideField.Choice("stage", "形式", "Stage", "1", new[] { C("1", "路段开始（示42-1）", "Begins (示42-1)"), C("2", "即将结束（示42-2）", "Ends soon (示42-2)"), C("3", "路段结束（示42-3）", "Ends (示42-3)") }) });

            // 示43 货车通行 (6.34, Fig. 137, E.99): round, white ground, green ring (Table 7 note b), black figure.
            yield return StandardSigns.Make("gb.mand.43", "示43", "货车通行", "Lorries permitted", G, "hctx", StandardSigns.Words("货车", "卡车", "货车通行", "lorry", "truck"),
                band => GreenRing(band, c => Fig(c, "truck", GuideColour.Black)));

            // 城市弯道指示牌 (blue, from a photograph): 警46's board and figure in white on blue; no number in GB 5768.2-2022, whose 警46 is yellow. Appended after 示43: the ids and specs of the others do not move.
            yield return StandardSigns.Make("gb.mand.curve", UrbanCode, "城市弯道指示牌（蓝底）", "Urban curve sign (blue)", G, "csjdzsp", StandardSigns.Words("弯道", "诱导", "线形诱导", "人字", "蓝色", "指示", "chevron", "curve"),
                WarningSigns.BlueChevron,
                new[]
                {
                    GuideField.Choice("form", "形式", "Form", "1", new[] { C("1", "单个（弯道外侧）", "Single chevron"), C("2", "多个同向", "Chevrons in a row"), C("3", "双向（T形交叉口）", "Both ways (T junction)") }),
                    GuideField.Choice("dir", "箭头指向", "Points", "l", kLeftRight, s => s.Is("form", "1") || s.Is("form", "2")),
                    GuideField.Choice("count", "箭头个数", "Chevrons", "3", new[] { C("2", "2 个", "2"), C("3", "3 个", "3"), C("4", "4 个", "4") }, s => s.Is("form", "2")),
                    GuideField.Flag("small", "最小尺寸", "Least size", false)
                }).AsLinear();

            // 立体交叉直行和左（右）转弯行驶 (a photograph). The foreword of GB 5768.2-2022 lists 立体交叉行驶路线标志 (6.7 of the 2009 edition) among the signs it deleted; this is that kind of sign, but the 2009 figure was
            // not at hand, so the drawing is from the photograph (an 85 px disc) with the arrows of this chapter, and carries no number.
            yield return StandardSigns.Make("gb.mand.xj", UrbanCode, "立体交叉直行和左（右）转弯行驶", "Interchange: ahead and turn left / right", G, "ltjczxhzywxs", StandardSigns.Words("立交", "立体交叉", "互通", "直行", "左转", "右转", "interchange", "ramp"),
                (spec, band) => Disc(band, spec.Is("dir", "r") ? "xjR" : "xjL"),
                new[] { GuideField.Choice("dir", "转弯方向", "Turn", "l", kLeftRight) });
        }

        // ------------------------------------------------------------------ layouts of single signs

        /// <summary>A square sign of the band's side (Table 7: 120, 100, 80, 60 cm).</summary>
        private static GuideBoard Square(int band, Action<Canvas> art)
        {
            float a = SignStyles.SquareSide(band);
            return Board(a, a, Liner(band), art);
        }

        /// <summary>Table 7, one-way signs: 100 x 50 (71 to 99 km/h), 80 x 40 (40 to 70), 60 x 30 (under 40); the 100 to 120 band has no value and takes the largest.</summary>
        private static readonly float[] OneWayB = { 1.00f, 1.00f, 0.80f, 0.60f };

        /// <summary>示10 (E.59): the arrow alone, or the wide white arrow with 单行路 in black in cells of 20 cm on the 100 cm board, 4.5 cm apart, centred on 46 cm (54 for the left one).</summary>
        private static GuideBoard OneWay(int band, bool left, bool text)
        {
            float b = OneWayB[SignStyles.Band(band)];
            return Board(b, b * 0.5f, Liner(band), c =>
            {
                Fig(c, text ? "onewayText" : "oneway", GuideColour.White, left);
                if (text)
                {
                    float cm = b / 100f;
                    CellsAt(c, "单行路", GuideColour.Black, left ? 54f : 46f, 15f * cm, 20f * cm, 4.5f * cm);
                }
            });
        }

        /// <summary>示11 (E.60): the board standing, 50 x 100 cm at 71 to 99 km/h; the characters one above the other, 20 cm cells 4.6 cm apart, from 18.6 cm.</summary>
        private static GuideBoard OneWayAhead(int band, bool text)
        {
            float b = OneWayB[SignStyles.Band(band)];
            return Board(b * 0.5f, b, Liner(band), c =>
            {
                Fig(c, text ? "oneway11Text" : "oneway11", GuideColour.White);
                if (text)
                {
                    float cm = b / 100f;
                    for (int i = 0; i < 3; i++)
                        CellsAt(c, "单行路".Substring(i, 1), GuideColour.Black, 50f, (18.6f + 24.6f * i) * cm, 20f * cm, 0f);
                }
            });
        }

        /// <summary>
        /// 示14 (E.63): the number in white on the blue disc, its ink 40.8 % of D high round the middle; below a chord 81.6 % of D from the top the disc is white (a part of the ground, not of
        /// the figure). Three digits are made lower so that they fit 74 % of D across.
        /// </summary>
        private static GuideBoard MinimumSpeed(int band, string limit)
        {
            band = SignStyles.Band(band);
            float d = SignStyles.CircleDiameter(band), c = Liner(band);
            GuideBoard board = SignStyles.MandatoryRound(band, cv =>
            {
                string text = string.IsNullOrEmpty(limit) ? "" : limit;
                if (text.Length == 0)
                    return;
                float perH = GuideKit.LatinWidth(text, 1f);
                float h = Math.Min(40.8f, 74f / Math.Max(0.01f, perH));
                cv.Ink(text, GuideColour.White, 13f, 50f - h * 0.5f, 74f, h);
            }, 1f);
            // The white segment: the disc below the chord, up to the middle of the liner.
            float r = d * 0.5f - c * 0.5f, y = d * 0.816f;
            var points = new List<float>();
            double half = Math.Acos((y - d * 0.5f) / r);
            for (int i = 0; i <= 32; i++)
            {
                double a = Math.PI * 0.5 - half + 2.0 * half * i / 32;
                points.Add(d * 0.5f + r * (float)Math.Cos(a));
                points.Add(d * 0.5f + r * (float)Math.Sin(a));
            }

            board.Main.Elements.Add(GuideKit.Poly(points.ToArray(), GuideColour.White));
            return board;
        }

        /// <summary>示16 (E.65): the white triangle (apex 14.1 %, base 83.4 % from the top, 10.3 % to 90.5 % across), the zebra and the person in black, the person with a white halo.</summary>
        private static void Crossing(Canvas c)
        {
            c.Poly(GuideColour.White, 50.4f, 14.1f, 90.5f, 83.4f, 10.3f, 83.4f);
            Fig(c, "zebra", GuideColour.Black);
            Fig(c, "crossingHalo", GuideColour.White);
            Fig(c, "crossingWalker", GuideColour.Black);
        }

        /// <summary>示30 (E.80): the lane board of the full height without an arrow; the number and a plus in the upper right (ink 24.5 cm high from 4.9 cm, ending at 86.4 cm), the car with two people below.</summary>
        private static GuideBoard Hov(int band, string people) => LaneSign(band, "hov", 4.9f, 76.5f, (c, dy) =>
        {
            string text = string.IsNullOrEmpty(people) ? "" : people + "+";
            if (text.Length > 0)
                c.Ink(text, GuideColour.White, 40f, 4.9f / 80f * 100f, 46.4f, 24.5f / 80f * 100f, GuideAlign.Right);
            LaneFit(c, Glyphs.M_CarFrontHov, 24.3f, 33.7f, 75.7f, 76.5f, 0f);
        });

        // ------------------------------------------------------------------ parking (示40)

        private static readonly GuideChoice[] kParkingKinds =
        {
            C("p", "停车位（示40-1）", "Parking (示40-1)"), C("pl", "向左停车（示40-2）", "Parking to the left (示40-2)"), C("pr", "向右停车（示40-3）", "Parking to the right (示40-3)"),
            C("kerb", "占用部分人行道停车（示40-4）", "Half on the pavement (示40-4)"), C("disabled", "残疾人专用（图127 a）", "Disabled (Fig. 127 a)"),
            C("school", "校车专用（图128 a）", "School bus (Fig. 128 a)"), C("taxi", "出租车专用（图129 a）", "Taxi (Fig. 129 a)"), C("cycle", "非机动车专用（图130 a）", "Bicycles (Fig. 130 a)"),
            C("bus", "公交车专用（图131 a）", "Bus (Fig. 131 a)"), C("charge", "充电停车位（图132 a）", "Charging (Fig. 132 a)"), C("own", "专属停车位（图133 a）", "Reserved (Fig. 133 a)")
        };

        /// <summary>
        /// 示40: the square of Table 7 note c (80 cm at 70 km/h or less, 60 cm under 40; 80 cm is used above 70, where the table gives none), 30 cm for bicycles (6.31.3 f); 示40-4 is 40 x 60 cm
        /// (20 x 30 under 40 km/h) with the liner 0.4 cm (note e). The positions are those of E.91 to E.94 and of the printed figures 127 to 133, in % of the board.
        /// </summary>
        private static GuideBoard Parking(int band, string kind, bool frame = false)
        {
            band = SignStyles.Band(band);
            if (kind == "kerb")
            {
                float kw = band == 3 ? 0.20f : 0.40f;
                return Board(kw, kw * 1.5f, 0.004f, c =>
                {
                    c.Fit(Glyphs.M_ParkingP, GuideColour.White, 31.5f, 9.7f, 41.1f, 33.2f);
                    c.Fit(Glyphs.M_CarFrontTilted, GuideColour.White, 18.8f, 56f, 57.5f, 31f);
                    c.Rect(GuideColour.White, 13.3f, 89f, 38.9f, 3.3f);
                    c.Rect(GuideColour.White, 52f, 85.3f, 35.8f, 7f);
                });
            }

            float a = kind == "cycle" ? 0.30f : band == 3 ? 0.60f : 0.80f;
            float liner = kind == "cycle" || band == 3 ? 0.004f : 0.006f;
            Action<Canvas> art = c =>
            {
                if (kind == "p")
                {
                    c.Fit(Glyphs.M_ParkingP, GuideColour.White, 25.5f, 19.6f, 50.4f, 60.6f);
                    return;
                }

                c.Fit(Glyphs.M_ParkingP, GuideColour.White, 15.4f, 19.9f, 50f, 60.2f);
                switch (kind)
                {
                    case "pl": Fig(c, "parkL", GuideColour.White); break;
                    case "pr": Fig(c, "parkR", GuideColour.White); break;
                    case "disabled": c.Fit(Glyphs.M_Wheelchair, GuideColour.White, 47.6f, 40.7f, 44f, 48.8f); break;
                    case "taxi": c.Fit(Glyphs.M_Taxi, GuideColour.White, 54f, 55.2f, 29.3f, 20.6f); break;
                    case "cycle": c.Fit(Glyphs.M_Bicycle, GuideColour.White, 49.6f, 59.1f, 33.7f, 19.9f); break;
                    case "charge": c.Fit(Glyphs.M_Charger, GuideColour.White, 62.5f, 56.2f, 20.8f, 23.6f); break;
                    case "school":
                        CellsAt(c, "校车", GuideColour.White, 70.5f, 0.561f * a, 0.096f * a, 0.096f * a * 0.45f);
                        c.Fit(Glyphs.M_Bus, GuideColour.White, 51.3f, 68.7f, 36.7f, 11.5f);
                        break;
                    case "bus":
                        CellsAt(c, "公交", GuideColour.White, 69f, 0.562f * a, 0.093f * a, 0.093f * a * 0.5f);
                        c.Fit(Glyphs.M_Bus, GuideColour.White, 50.6f, 66.5f, 36.7f, 12.5f);
                        break;
                    case "own":
                        CellsAt(c, "专属", GuideColour.White, 69.5f, 0.625f * a, 0.169f * a, 0.079f * a);
                        break;
                }
            };
            return frame && kind == "school" ? Framed(a, liner, FrameWidth, art) : Board(a, a, liner, art);
        }

        // ------------------------------------------------------------------ the hard shoulder (示42)

        /// <summary>
        /// 示42 (E.96 to E.98, units of the square board): two lanes' arrows at 20.6 and 44.4, the solid edge line from 9.2 to 91 at 60.2 to 63.1, the shoulder's arrow at 78.9; 42-2 bends it to
        /// the left, 42-3 crosses it with a red band 4 wide from (65, 100) to (96.2, 0), cut to the inside of the liner.
        /// </summary>
        private static void Shoulder(Canvas c, string stage, float linerUnits)
        {
            Fig(c, "up:20.6", GuideColour.White);
            Fig(c, "up:44.4", GuideColour.White);
            c.Rect(GuideColour.White, 60.2f, 9.2f, 2.9f, 81.8f);
            if (stage == "2")
            {
                Fig(c, "shoulderMerge", GuideColour.White);
                return;
            }

            Fig(c, "up:78.9", GuideColour.White);
            if (stage == "3")
            {
                float e = linerUnits * 100f + 0.05f;
                float[] band = GuideKit.ClipToBox(new[] { 62.9f, 100f, 67.1f, 100f, 97.9f, 0f, 94.5f, 0f }, e, e, 100f - e, 100f - e);
                c.Poly(GuideColour.Red, band);
            }
        }
    }
}
