using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The auxiliary (辅助) signs of GB 5768.2-2022 chapter 12 (辅1 to 辅20, Fig. 325 to 345): a white board with a black border and black text or figures, fixed under another sign to say
    /// when, to whom, how far, in which direction or why it applies. Characters and digits are half the height of Table 3 (4.5.8: 50 % of Table 3, at least 10 cm); the liner, the border
    /// and the corner radius follow Fig. 3 for that height. The distances from the text to the border are those of the drawings of Appendix E (E.250: 0.4 h; E.251: 0.3 h; E.252: 0.2 h
    /// across and 0.3 h up and down), 0.4 h (Table 6) where there is no drawing. Text, times, distances and directions are settings: the standard says the printed ones are examples.
    /// The helpers here (Line, Lines, Board, the arrows) are used by the notice and tourist signs too.
    /// </summary>
    public static class AuxiliarySigns
    {
        private const string G = StandardSigns.Auxiliary;

        /// <summary>The space between Chinese characters: 0.2 h (Fig. 3, E.250, E.252, and the printed Fig. 337; E.251 writes 0.5 h between 长 and 度, which its own printed figure does not show).</summary>
        public const float GapK = 0.2f;

        /// <summary>The space between lines: 1/3 h, the upper value of Table 6 (the printed figures 325 b and 335 show about 0.4 h).</summary>
        public const float LineGapK = 1f / 3f;

        /// <summary>The character height of an auxiliary sign for a band (m): 50 % of Table 3's value for the band, 10 cm at the least.</summary>
        public static float CharHeight(int band) => Math.Max(0.10f, 0.5f * GuideTemplates.CharHeightOf(new[] { "100", "80", "60", "30" }[SignStyles.Band(band)]));

        private static bool IsWide(char c) => c >= 0x2E80;

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        /// <summary>
        /// A line of text as a block, h high: Chinese characters in square cells (gapK h apart), digits and letters as ink as high as the cell (Table 4: digits h), and the unit after a number
        /// (m, km) small on the digits' baseline as GuideKit.Distance writes it (Fig. 331 to 337). Runs follow each other 0.15 h apart. An empty line is an empty block.
        /// </summary>
        public static GuideBlock Line(string text, float h, GuideColour colour, float gapK = GapK)
        {
            var block = new GuideBlock(0f, h);
            float x = 0f;
            text = (text ?? "").Trim();
            int i = 0;
            bool first = true;
            while (i < text.Length)
            {
                int j = i;
                bool wide = IsWide(text[i]);
                while (j < text.Length && IsWide(text[j]) == wide)
                    j++;
                string run = text.Substring(i, j - i);
                i = j;
                if (wide)
                {
                    run = run.Replace(" ", "");
                    if (run.Length == 0)
                        continue;
                    if (!first) x += 0.15f * h;
                    float gap = gapK * h;
                    block.Elements.Add(GuideKit.Cells(run, x, 0f, h, colour, gap));
                    x += GuideKit.CellWidth(run.Length, h, gap);
                    first = false;
                    continue;
                }

                run = run.Trim();
                if (run.Length == 0)
                    continue;
                // A unit at the end of the run, after a digit: written small.
                string unit = null;
                if (run.Length >= 3 && run.EndsWith("km") && IsDigit(run[run.Length - 3]))
                    unit = "km";
                else if (run.Length >= 2 && run.EndsWith("m") && !run.EndsWith("km") && IsDigit(run[run.Length - 2]))
                    unit = "m";
                string number = unit == null ? run : run.Substring(0, run.Length - unit.Length).TrimEnd();
                if (!first) x += 0.15f * h;
                float w = GuideKit.LatinWidth(number, h);
                block.Elements.Add(GuideKit.Ink(number, x, 0f, w, h, colour, GuideAlign.Left));
                x += w;
                if (unit != null)
                {
                    float uh = GuideKit.UnitHeight(unit, h), uw = GuideKit.UnitWidth(unit, h);
                    x += 0.12f * h;
                    block.Elements.Add(GuideKit.Ink(unit, x, h - uh, uw, uh, colour, GuideAlign.Left, "k"));
                    x += uw;
                }

                first = false;
            }

            block.W = x;
            return Bilingual.Under(block, text, h, colour);
        }

        /// <summary>Lines one above the other, lineGapK h apart, each centred (or aligned) in the widest; empty lines are left out. No line at all: an empty block h square.</summary>
        public static GuideBlock Lines(IList<string> lines, float h, GuideColour colour, float gapK = GapK, float lineGapK = LineGapK, GuideAlign align = GuideAlign.Centre)
        {
            var blocks = new List<GuideBlock>();
            foreach (string line in lines)
            {
                GuideBlock b = Line(line, h, colour, gapK);
                if (b.W > 0f)
                    blocks.Add(b);
            }

            return Stack(blocks, lineGapK * h, align, h);
        }

        /// <summary>Blocks one under another, centred, 0.4 h apart; none that is null or empty (the boards of the BRT and the toll plaza series, 0.12.22 and 0.12.26).</summary>
        public static GuideBlock Rows(float h, params GuideBlock[] rows)
        {
            var list = new List<GuideBlock>();
            foreach (GuideBlock r in rows)
            {
                if (r != null && r.W > 0f)
                    list.Add(r);
            }

            return Stack(list, 0.4f * h, GuideAlign.Centre, h);
        }

        /// <summary>A line of text as a block, or null when there is none.</summary>
        public static GuideBlock Text(string text, float h, GuideColour colour) => string.IsNullOrWhiteSpace(text) ? null : Line(text.Trim(), h, colour);

        /// <summary>Blocks one above the other with a gap, aligned in the widest. emptySize: the size of the empty block when there is none.</summary>
        public static GuideBlock Stack(IList<GuideBlock> blocks, float gap, GuideAlign align, float emptySize)
        {
            if (blocks.Count == 0)
                return new GuideBlock(emptySize, emptySize);
            float w = 0f, hTotal = 0f;
            foreach (GuideBlock b in blocks)
            {
                w = Math.Max(w, b.W);
                hTotal += b.H;
            }

            hTotal += gap * (blocks.Count - 1);
            var stack = new GuideBlock(w, hTotal);
            float y = 0f;
            foreach (GuideBlock b in blocks)
            {
                float x = align == GuideAlign.Left ? 0f : align == GuideAlign.Right ? w - b.W : (w - b.W) * 0.5f;
                stack.Add(b, x, y);
                y += b.H + gap;
            }

            return stack;
        }

        /// <summary>Characters one under the other (a vertical board: Fig. 324 d), gapK h apart.</summary>
        public static GuideBlock Column(string text, float h, GuideColour colour, float gapK = GapK)
        {
            var blocks = new List<GuideBlock>();
            foreach (char c in (text ?? "").Replace(" ", ""))
                blocks.Add(Line(c.ToString(), h, colour));
            return Stack(blocks, gapK * h, GuideAlign.Centre, h);
        }

        /// <summary>
        /// A board of Fig. 3 for characters h high round a block: the liner and the border 0.1 h each, the outer radius 0.4 h, the block mx from the border at the sides and my above and below,
        /// centred in a board at least minInnerW wide inside. The safe region (what a figure must stay inside) is the inside of the border.
        /// </summary>
        public static GuideBoard Board(GuideBlock content, float h, float mx, float my, GuideColour ground = GuideColour.White, GuideColour border = GuideColour.Black, float minInnerW = 0f, float minInnerH = 0f)
        {
            float innerW = Math.Max(content.W + 2f * mx, minInnerW), innerH = Math.Max(content.H + 2f * my, minInnerH);
            GuidePart part = GuideKit.Part(h, innerW, innerH, ground);
            part.BorderColour = border;
            float e = part.Edge;
            part.Safe = new[] { e, e, part.W - e, e, part.W - e, part.H - e, e, part.H - e };
            part.Add(content, e + (innerW - content.W) * 0.5f, e + (innerH - content.H) * 0.5f);
            var board = new GuideBoard { H = h };
            board.Parts.Add(part);
            return board;
        }

        // ------------------------------------------------------------------ the arrows of the auxiliary signs (Fig. 330 to 334)

        /// <summary>
        /// The arrow of the auxiliary signs (Fig. 330 to 334): a straight stem t wide and an open head of two arms w wide at 45 degrees reaching s to each side, from the tail (x0, y0) to the
        /// tip (x1, y1); both: a head at each end (Fig. 333). Read off the printed figures in the digit height h: t 0.16 h, s 0.4 h, w 0.2 h for the long arrows.
        /// </summary>
        public static float[] LineArrow(float x0, float y0, float x1, float y1, float t, float s, float w, bool both = false)
        {
            float dx = x1 - x0, dy = y1 - y0, L = (float)Math.Sqrt(dx * dx + dy * dy);
            if (L < 1e-5f)
                return new float[0];
            dx /= L;
            dy /= L;
            float nx = -dy, ny = dx;
            const float r2 = 1.41421356f;
            float k = w / r2;
            var local = new List<float>
            {
                L, 0f,
                L - s, -s,
                L - s - k, -s + k,
                L - w * r2 - t * 0.5f, -t * 0.5f
            };
            if (both)
                local.AddRange(new[] { w * r2 + t * 0.5f, -t * 0.5f, s + k, -s + k, s, -s, 0f, 0f, s, s, s + k, s - k, w * r2 + t * 0.5f, t * 0.5f });
            else
                local.AddRange(new[] { 0f, -t * 0.5f, 0f, t * 0.5f });
            local.AddRange(new[] { L - w * r2 - t * 0.5f, t * 0.5f, L - s - k, s - k, L - s, s });
            var points = new float[local.Count];
            for (int i = 0; i < local.Count; i += 2)
            {
                points[i] = x0 + dx * local[i] + nx * local[i + 1];
                points[i + 1] = y0 + dy * local[i] + ny * local[i + 1];
            }

            return points;
        }

        /// <summary>A long arrow (h the digit height) as a figure element of a block.</summary>
        private static GuideElement Arrow(float x0, float y0, float x1, float y1, float h, GuideColour colour, bool both = false, float scale = 1f)
        {
            GuideElement e = GuideKit.Poly(LineArrow(x0, y0, x1, y1, 0.16f * h * scale, 0.4f * h * scale, 0.2f * h * scale, both), colour);
            e.Art = true;
            return e;
        }

        /// <summary>A distance (digits h high, the unit small) as a block; an empty number gives an empty block.</summary>
        public static GuideBlock Distance(string value, string unit, float h, GuideColour colour)
        {
            if (string.IsNullOrEmpty(value))
                return new GuideBlock(0f, h);
            return GuideKit.Distance(value, unit, h, colour);
        }

        /// <summary>辅8, 辅10 (Fig. 332, 334): the distance over a long arrow; the number stands over the arrow's tail, 0.3 h in from its end; the arrow is 1.6 h longer than the number, 3 h at least.</summary>
        public static GuideBlock DistanceOverArrow(string value, string unit, bool left, float h, GuideColour colour)
        {
            GuideBlock number = Distance(value, unit, h, colour);
            float s = 0.4f * h;
            float L = Math.Max(number.W + 1.6f * h, 3.0f * h);
            float arrowY = h + 0.15f * h + s;
            var block = new GuideBlock(L, arrowY + s);
            block.Add(number, left ? L - 0.3f * h - number.W : 0.3f * h, 0f);
            block.Add(left ? Arrow(L, arrowY, 0f, arrowY, h, colour) : Arrow(0f, arrowY, L, arrowY, h, colour));
            return block;
        }

        /// <summary>辅7 (Fig. 331): the distance and an arrow up as high as the digits, after it.</summary>
        public static GuideBlock DistanceAhead(string value, string unit, float h, GuideColour colour)
        {
            GuideBlock number = Distance(value, unit, h, colour);
            float s = 0.32f * h;
            float x = number.W > 0f ? number.W + 0.25f * h : 0f;
            var block = new GuideBlock(x + 2f * s, h);
            block.Add(number, 0f, 0f);
            GuideElement e = GuideKit.Poly(LineArrow(x + s, h, x + s, 0f, 0.16f * h, s, 0.18f * h), colour);
            e.Art = true;
            block.Add(e);
            return block;
        }

        /// <summary>辅9 b (Fig. 333 b): the distance to the left and to the right of a dividing line, over an arrow with a head at each end.</summary>
        public static GuideBlock DistancesBothWays(string left, string right, string unit, float h, GuideColour colour)
        {
            GuideBlock a = Distance(left, unit, h, colour), b = Distance(right, unit, h, colour);
            float s = 0.4f * h;
            float half = Math.Max(Math.Max(a.W, b.W) + 1.5f * h, 2.0f * h);
            float L = 2f * half;
            float arrowY = h + 0.15f * h + s;
            var block = new GuideBlock(L, arrowY + s);
            block.Add(a, half - 0.3f * h - a.W, 0f);
            block.Add(b, half + 0.3f * h, 0f);
            block.Add(Arrow(0f, arrowY, L, arrowY, h, colour, true));
            GuideElement divider = GuideKit.Rect(half - 0.05f * h, 0f, 0.1f * h, arrowY + s, colour);
            divider.Art = true;
            block.Add(divider);
            return block;
        }

        // ------------------------------------------------------------------ the boards

        /// <summary>A board of text lines with the margins of a drawing (mx, my in h).</summary>
        public static GuideBoard TextBoard(IList<string> lines, int band, float mxK = 0.4f, float myK = 0.4f, float gapK = GapK, GuideColour ground = GuideColour.White)
        {
            float h = CharHeight(band);
            return Board(Lines(lines, h, GuideColour.Black, gapK), h, mxK * h, myK * h, ground);
        }

        /// <summary>The direction boards (辅6, 辅9 a: Fig. 330, 333 a): 4 h x 2 h (read off the printed figures, which have no drawing in Appendix E), the arrow in the middle.</summary>
        private static GuideBoard DirectionBoard(string arrow, int band)
        {
            float h = CharHeight(band);
            GuideBoard board = Board(new GuideBlock(0f, 0f), h, 0f, 0f, GuideColour.White, GuideColour.Black, 3.6f * h, 1.6f * h);
            GuidePart part = board.Main;
            float cx = part.W * 0.5f, cy = part.H * 0.5f;
            var canvas = new Canvas(part, 0f, 0f, part.W, part.H);
            switch (arrow)
            {
                case "b": canvas.Add(Arrow(cx - 1.2f * h, cy, cx + 1.2f * h, cy, h, GuideColour.Black)); break;
                case "c": canvas.Add(Arrow(cx + 1.2f * h, cy, cx - 1.2f * h, cy, h, GuideColour.Black)); break;
                case "d": canvas.Add(Arrow(cx + 0.5f * h, cy + 0.5f * h, cx - 0.5f * h, cy - 0.5f * h, h, GuideColour.Black, false, 0.8f)); break;
                case "e": canvas.Add(Arrow(cx - 0.5f * h, cy + 0.5f * h, cx + 0.5f * h, cy - 0.5f * h, h, GuideColour.Black, false, 0.8f)); break;
                case "f": canvas.Add(GuideKit.FitPicture(Glyphs.Mirror(Glyphs.N_TurnLeft), cx - 0.75f * h, cy - 0.65f * h, 1.5f * h, 1.3f * h, GuideColour.Black)); break;
                case "g": canvas.Add(GuideKit.FitPicture(Glyphs.N_TurnLeft, cx - 0.75f * h, cy - 0.65f * h, 1.5f * h, 1.3f * h, GuideColour.Black)); break;
                case "both": canvas.Add(Arrow(cx - 1.1f * h, cy, cx + 1.1f * h, cy, h, GuideColour.Black, true)); break;
                default: canvas.Add(Arrow(cx, cy + 0.6f * h, cx, cy - 0.6f * h, h, GuideColour.Black, false, 0.8f)); break;
            }

            return board;
        }

        /// <summary>A board with pictures in boxes (in h, from the inside's top left corner) on an inside of innerW x innerH (h).</summary>
        private static GuideBoard PictureBoard(int band, float innerW, float innerH, params (string glyph, float x, float y, float w, float hh)[] pictures)
        {
            float h = CharHeight(band);
            GuideBoard board = Board(new GuideBlock(0f, 0f), h, 0f, 0f, GuideColour.White, GuideColour.Black, innerW * h, innerH * h);
            GuidePart part = board.Main;
            var canvas = new Canvas(part, part.Edge, part.Edge, innerW * h, innerH * h);
            foreach ((string glyph, float x, float y, float w, float hh) in pictures)
                canvas.Fit(glyph, GuideColour.Black, x / innerW * 100f, y / innerH * 100f, w / innerW * 100f, hh / innerH * 100f);
            return board;
        }

        private static readonly GuideChoice[] kUnits =
        {
            new GuideChoice("m", "m", "m"),
            new GuideChoice("km", "km", "km")
        };

        private static readonly GuideChoice[] kLeftRight =
        {
            new GuideChoice("l", "向左（箭头向左）", "To the left"),
            new GuideChoice("r", "向右（箭头向右）", "To the right")
        };

        private static readonly GuideChoice[] kDirections =
        {
            new GuideChoice("a", "a) ↑ 向前", "a) ↑ ahead"),
            new GuideChoice("b", "b) → 向右", "b) → to the right"),
            new GuideChoice("c", "c) ← 向左", "c) ← to the left"),
            new GuideChoice("d", "d) ↖ 左前方", "d) ↖ ahead left"),
            new GuideChoice("e", "e) ↗ 右前方", "e) ↗ ahead right"),
            new GuideChoice("f", "f) ↱ 向右转", "f) ↱ turn right"),
            new GuideChoice("g", "g) ↰ 向左转", "g) ↰ turn left")
        };

        private static readonly GuideChoice[] kVehicles =
        {
            new GuideChoice("truck", "货车", "Lorry"),
            new GuideChoice("car", "机动车", "Motor vehicle"),
            new GuideChoice("tractor", "拖拉机", "Tractor"),
            new GuideChoice("none", "不设", "None")
        };

        private static readonly GuideChoice[] kComboDirections =
        {
            new GuideChoice("l", "← 向左", "← to the left"),
            new GuideChoice("r", "→ 向右", "→ to the right"),
            new GuideChoice("a", "↑ 向前", "↑ ahead")
        };

        private static string VehicleGlyph(string v) => v == "car" ? Glyphs.N_CarFront : v == "tractor" ? Glyphs.N_Tractor : Glyphs.N_Truck;

        /// <summary>
        /// 辅20 (Fig. 345): up to three auxiliary signs in one board, one under the other, divided by black lines as wide as the border: a vehicle, a distance with its direction, a time.
        /// A section left empty is left out. Each keeps 0.3 h above and below and 0.4 h at the sides.
        /// </summary>
        private static GuideBoard Combined(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            var sections = new List<GuideBlock>();
            if (!s.Is("veh", "none"))
            {
                // The picture 1.2 h high (Fig. 345: the lorry is about as high as the digits and a fifth more).
                var pic = new GuideBlock(2.0f * h, 1.2f * h);
                GuideElement e = GuideKit.FitPicture(VehicleGlyph(s.Get("veh")), 0f, 0f, 2.0f * h, 1.2f * h, GuideColour.Black);
                e.Art = true;
                pic.Add(e);
                sections.Add(pic);
            }

            if (s.Get("dist").Length > 0)
                sections.Add(s.Is("dir", "a") ? DistanceAhead(s.Get("dist"), s.Get("unit"), h, GuideColour.Black) : DistanceOverArrow(s.Get("dist"), s.Get("unit"), s.Is("dir", "l"), h, GuideColour.Black));
            GuideBlock time = Line(s.Get("time"), h, GuideColour.Black);
            if (time.W > 0f)
                sections.Add(time);
            if (sections.Count == 0)
                sections.Add(new GuideBlock(h, h));

            float mx = 0.4f * h, my = 0.3f * h, line = 0.1f * h;
            float contentW = 0f, innerH = 0f;
            foreach (GuideBlock b in sections)
            {
                contentW = Math.Max(contentW, b.W);
                innerH += b.H + 2f * my;
            }

            innerH += line * (sections.Count - 1);
            float innerW = contentW + 2f * mx;
            var all = new GuideBlock(innerW, innerH);
            float y = 0f;
            for (int i = 0; i < sections.Count; i++)
            {
                if (i > 0)
                {
                    GuideElement rule = GuideKit.Rect(0f, y, innerW, line, GuideColour.Black);
                    // The dividing line runs from border to border, like the border itself: not a figure inside it.
                    all.Add(rule);
                    y += line;
                }

                all.Add(sections[i], (innerW - sections[i].W) * 0.5f, y + my);
                y += sections[i].H + 2f * my;
            }

            return Board(all, h, 0f, 0f);
        }

        /// <summary>
        /// The green distance plate (城市, from a photograph: white "50 m" and "100 m" on green boards under the blue curve signs; the guide boards of GB 5768.2-2009's 车距确认 looked so and the 2022
        /// edition deleted them): the digits of the auxiliary signs (CharHeight) in white, the liner and the border of a guide board (Fig. 3), 0.4 h at the sides and 0.3 h above and below. An auxiliary
        /// board: it goes under the board it explains, as the others do (Assembly).
        /// </summary>
        private static GuideBoard GreenDistance(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            return Board(Distance(s.Get("dist"), s.Get("unit"), h, GuideColour.White), h, 0.4f * h, 0.3f * h, GuideColour.Green, GuideColour.White, 2f * h);
        }

        public static IEnumerable<GuideTemplate> All()
        {
            // The general text board (Fig. 21, 25 and the like): "6:00-20:00", "中型客车", "10t以上", ...
            yield return StandardSigns.Make("gb.aux.text", "辅助", "辅助标志（文字）", "Auxiliary sign (text)", G, "fzbz", StandardSigns.Words("辅助", "文字", "说明", "auxiliary", "text"),
                (spec, band) => TextBoard(new[] { spec.Get("text") }, band),
                new[] { GuideField.Text("text", "文字", "Text", "6:00-20:00", 14) });

            // 辅1 时间范围 (Fig. 325): one or two lines of times, 24-hour clock.
            yield return StandardSigns.Make("gb.aux.01", "辅1", "时间范围", "Time period", G, "sjfw", StandardSigns.Words("时间", "时段", "time", "hours"),
                (spec, band) => TextBoard(new[] { spec.Get("t1"), spec.Get("t2") }, band),
                new[] { GuideField.Text("t1", "时间", "Time", "6:00-20:00", 11), GuideField.Text("t2", "第二时段（可空）", "Second period (may be empty)", "", 11) });

            // 辅2 公交车除外 (Fig. 326, E.250: 7 h x 2.2 h for five characters: 0.4 h to the border).
            yield return StandardSigns.Make("gb.aux.02", "辅2", "公交车除外", "Except buses", G, "gjccw", StandardSigns.Words("公交", "除外", "车辆", "except", "bus"),
                (spec, band) => TextBoard(new[] { spec.Get("text") }, band),
                new[] { GuideField.Text("text", "文字", "Text", "公交车除外", 8) });

            // 辅3 机动车 (Fig. 327), 辅4 货车 (Fig. 328), 辅5 货车、拖拉机 (Fig. 329): the boards read off the printed figures (no drawing in Appendix E).
            yield return StandardSigns.Make("gb.aux.03", "辅3", "机动车", "Motor vehicles", G, "jdc", StandardSigns.Words("机动车", "汽车", "车型", "motor", "car"),
                band => PictureBoard(band, 3.5f, 2.0f, (Glyphs.N_CarFront, 0.75f, 0.3f, 2.0f, 1.4f)));
            yield return StandardSigns.Make("gb.aux.04", "辅4", "货车", "Lorries", G, "hc", StandardSigns.Words("货车", "卡车", "车型", "lorry", "truck"),
                band => PictureBoard(band, 3.5f, 2.0f, (Glyphs.N_Truck, 0.55f, 0.3f, 2.4f, 1.4f)));
            yield return StandardSigns.Make("gb.aux.05", "辅5", "货车、拖拉机", "Lorries and tractors", G, "hctlj", StandardSigns.Words("货车", "拖拉机", "车型", "tractor", "truck"),
                band => PictureBoard(band, 3.2f, 3.6f, (Glyphs.N_Truck, 0.5f, 0.3f, 2.2f, 1.3f), (Glyphs.N_Tractor, 0.5f, 1.9f, 2.2f, 1.4f)));

            // 辅6 行驶方向 (Fig. 330 a to g): one sign, the arrow is a setting.
            yield return StandardSigns.Make("gb.aux.06", "辅6", "行驶方向", "Direction of travel", G, "xsfx", StandardSigns.Words("方向", "箭头", "行驶", "direction", "arrow"),
                (spec, band) => DirectionBoard(spec.Get("arrow"), band),
                new[] { GuideField.Choice("arrow", "箭头", "Arrow", "a", kDirections) });

            // 辅7 向前 200 m (Fig. 331).
            yield return StandardSigns.Make("gb.aux.07", "辅7", "向前200m", "Ahead 200 m", G, "xq", StandardSigns.Words("向前", "距离", "前方", "ahead", "distance"),
                (spec, band) => Board(DistanceAhead(spec.Get("dist"), spec.Get("unit"), CharHeight(band), GuideColour.Black), CharHeight(band), 0.4f * CharHeight(band), 0.4f * CharHeight(band)),
                new[] { GuideField.Digits("dist", "距离", "Distance", "200", 4), GuideField.Choice("unit", "单位", "Unit", "m", kUnits) });

            // 辅8 向左 100 m (Fig. 332) and 辅10 向右 100 m (Fig. 334): one sign, the side is a setting.
            yield return StandardSigns.Make("gb.aux.08", "辅8·辅10", "向左、向右的距离", "Distance to the left or right", G, "xzxydjl", StandardSigns.Words("向左", "向右", "距离", "left", "right"),
                (spec, band) => Board(DistanceOverArrow(spec.Get("dist"), spec.Get("unit"), !spec.Is("side", "r"), CharHeight(band), GuideColour.Black), CharHeight(band), 0.4f * CharHeight(band), 0.4f * CharHeight(band)),
                new[] { GuideField.Choice("side", "方向", "Side", "l", kLeftRight), GuideField.Digits("dist", "距离", "Distance", "100", 4), GuideField.Choice("unit", "单位", "Unit", "m", kUnits) });

            // 辅9 向左、向右 (Fig. 333): a) the arrow both ways; b) the distances to each side.
            yield return StandardSigns.Make("gb.aux.09", "辅9", "向左、向右", "To the left and to the right", G, "xzxy", StandardSigns.Words("向左", "向右", "双向", "both", "ways"),
                (spec, band) => spec.Is("form", "b")
                    ? Board(DistancesBothWays(spec.Get("left"), spec.Get("right"), spec.Get("unit"), CharHeight(band), GuideColour.Black), CharHeight(band), 0.4f * CharHeight(band), 0.4f * CharHeight(band))
                    : DirectionBoard("both", band),
                new[]
                {
                    GuideField.Choice("form", "形式", "Form", "a", new[] { new GuideChoice("a", "a) 双向箭头", "a) Arrow both ways"), new GuideChoice("b", "b) 两侧距离", "b) Distances to each side") }),
                    GuideField.Digits("left", "左侧距离", "Distance to the left", "50", 4, s => s.Is("form", "b")),
                    GuideField.Digits("right", "右侧距离", "Distance to the right", "50", 4, s => s.Is("form", "b")),
                    GuideField.Choice("unit", "单位", "Unit", "m", kUnits, s => s.Is("form", "b"))
                });

            // 辅11 某区域内 (Fig. 335): two lines.
            yield return StandardSigns.Make("gb.aux.11", "辅11", "某区域内", "Within an area", G, "mqyn", StandardSigns.Words("区域", "范围", "区域内", "area", "zone"),
                (spec, band) => TextBoard(new[] { spec.Get("l1"), spec.Get("l2") }, band),
                new[] { GuideField.Text("l1", "第一行", "Line 1", "二环路", 8), GuideField.Text("l2", "第二行", "Line 2", "区域内", 8) });

            // 辅12 距离某地 200 m (Fig. 336).
            yield return StandardSigns.Make("gb.aux.12", "辅12", "距离某地200m", "Distance 200 m", G, "jlmd", StandardSigns.Words("距离", "某地", "前方", "distance"),
                (spec, band) => Board(Distance(spec.Get("dist"), spec.Get("unit"), CharHeight(band), GuideColour.Black), CharHeight(band), 0.4f * CharHeight(band), 0.4f * CharHeight(band), GuideColour.White, GuideColour.Black, CharHeight(band)),
                new[] { GuideField.Digits("dist", "距离", "Distance", "200", 4), GuideField.Choice("unit", "单位", "Unit", "m", kUnits) });

            // 辅13 长度 (Fig. 337, E.251: 0.3 h to the border).
            yield return StandardSigns.Make("gb.aux.13", "辅13", "长度", "Length", G, "cd", StandardSigns.Words("长度", "路段", "length"),
                (spec, band) => TextBoard(new[] { "长度" + spec.Get("dist") + (spec.Get("dist").Length > 0 ? spec.Get("unit") : "") }, band, 0.3f, 0.3f),
                new[] { GuideField.Digits("dist", "长度", "Length", "300", 4), GuideField.Choice("unit", "单位", "Unit", "m", kUnits) });

            // 辅14 to 辅19: the reason for the sign (Fig. 339 to 344, E.252: 60 x 40 cm for two characters of 20 cm: 0.2 h at the sides, 0.3 h above and below).
            foreach ((string id, string code, string zh, string en, string py, string[] words) in new[]
            {
                ("gb.aux.14", "辅14", "学校", "School", "xx", new[] { "学校", "理由", "school" }),
                ("gb.aux.15", "辅15", "海关", "Customs", "hg", new[] { "海关", "理由", "customs" }),
                ("gb.aux.16", "辅16", "事故", "Accident", "sg", new[] { "事故", "理由", "accident" }),
                ("gb.aux.17", "辅17", "塌方", "Landslide", "tf", new[] { "塌方", "理由", "landslide" }),
                ("gb.aux.18", "辅18", "教练车行驶路线", "Driving school route", "jlcxslx", new[] { "教练", "路线", "驾校", "learner" }),
                ("gb.aux.19", "辅19", "驾驶考试路线", "Driving test route", "jskslx", new[] { "考试", "路线", "驾考", "test" })
            })
            {
                string text = code == "辅18" ? "教练路线" : code == "辅19" ? "考试路线" : zh;
                // Table 2: the school and school-bus plates (学校, 校车) are white or fluorescent yellow-green (Fig. 159, 128 b and c: black text and border on it); white unless the setting says so.
                bool school = code == "辅14";
                yield return StandardSigns.Make(id, code, zh, en, G, py, words,
                    (spec, band) => school && spec.Is("ground", "g") ? TextBoard(new[] { spec.Get("text") }, band, 0.2f, 0.3f, GapK, GuideColour.FluorescentYellowGreen) : TextBoard(new[] { spec.Get("text") }, band, 0.2f, 0.3f),
                    school ? new[] { GuideField.Text("text", "文字", "Text", text, 8), GuideField.Choice("ground", "底色", "Ground", "w", new[] { new GuideChoice("w", "白色", "White"), new GuideChoice("g", "荧光黄绿", "Fluorescent yellow-green") }).AsSparse() }
                           : new[] { GuideField.Text("text", "文字", "Text", text, 8) });
            }

            // 辅20 组合辅助标志 (Fig. 345).
            yield return StandardSigns.Make("gb.aux.20", "辅20", "组合辅助标志", "Combined auxiliary sign", G, "zhfzbz", StandardSigns.Words("组合", "辅助", "车型", "时间", "combined"),
                Combined,
                new[]
                {
                    GuideField.Choice("veh", "车辆种类", "Vehicle", "truck", kVehicles),
                    GuideField.Digits("dist", "距离（空为不设）", "Distance (empty: none)", "100", 4),
                    GuideField.Choice("unit", "单位", "Unit", "m", kUnits, s => s.Get("dist").Length > 0),
                    GuideField.Choice("dir", "方向", "Direction", "l", kComboDirections, s => s.Get("dist").Length > 0),
                    GuideField.Text("time", "时间（空为不设）", "Time (empty: none)", "7:30-18:30", 11)
                });

            // 距离牌（绿底白字）: after the standard's own, so that no id or setting of theirs moves.
            yield return StandardSigns.Make("gb.aux.dist.green", MandatorySigns.UrbanCode, "距离牌（绿底白字）", "Distance plate (white on green)", G, "jlp", StandardSigns.Words("距离", "距离牌", "绿底", "弯道", "车距", "distance", "plate"),
                GreenDistance,
                new[] { GuideField.Digits("dist", "距离", "Distance", "50", 4), GuideField.Choice("unit", "单位", "Unit", "m", kUnits) });
        }
    }
}
