using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The tourist area signs (chapter 10: 旅1 to 旅17, Fig. 302 to 319) and the notice signs (chapter 11: Fig. 320 to 324) of GB 5768.2-2022.
    /// Notice signs: white boards with a black border and black text (11.2 to 11.4), characters half the height of Table 3 (4.5.8, at least 10 cm), the edges of Fig. 3; the standard gives
    /// them as examples (示例), so their texts are settings, with the printed text as the default. Tourist guide boards (旅1, 旅2): brown ground, white border, white text and figures,
    /// characters of the lower value of Table 3's range (4.5.7: 60, 50, 35, 25 cm), laid out by the drawings E.245 and E.246. Tourist symbol signs (旅3 to 旅17): brown squares of
    /// 60 x 60 cm, 45 x 45 cm on slow roads with little traffic (4.6.4: here the band under 40 km/h), with a white border and a white symbol.
    /// </summary>
    public static class NoticeSigns
    {
        private const string Notice = "notice";
        private const string Tourist = "tourist";

        private static float H(int band) => AuxiliarySigns.CharHeight(band);

        private static GuideElement Art(GuideElement e)
        {
            e.Art = true;
            return e;
        }

        // ------------------------------------------------------------------ 11.2.1 高速公路编号信息 (Fig. 320)

        /// <summary>
        /// Fig. 320: the expressway's number plate (Appendix C: a green plate under a red or yellow strip, 1.6 h high: the printed figure's proportion) and its name in black, on a white board;
        /// 0.3 h to the border, 0.3 h between the plate and the name (read off the printed figure: there is no drawing in Appendix E).
        /// </summary>
        private static GuideBoard ExpresswayInfo(GuideSpec s, int band)
        {
            float h = H(band);
            RoadPlateKind kind = s.Is("road", "ge") ? RoadPlateKind.NationalExpressway : RoadPlateKind.ProvincialExpressway;
            GuideBlock plate = GuideKit.RoadPlate(kind, s.Get("code"), s.Get("sub"), s.Get("prov"), 1.6f * h);
            GuideBlock name = AuxiliarySigns.Line(s.Get("name"), h, GuideColour.Black);
            float gap = name.W > 0f ? 0.3f * h : 0f;
            var row = new GuideBlock(plate.W + gap + name.W, plate.H);
            row.Add(plate, 0f, 0f);
            row.Add(name, plate.W + gap, (plate.H - h) * 0.5f);
            return AuxiliarySigns.Board(row, h, 0.3f * h, 0.3f * h);
        }

        // ------------------------------------------------------------------ 11.2.2 交通监控设备信息, 区间测速 (Fig. 321, 322; E.247)

        /// <summary>
        /// Fig. 321, 322 (E.247, 145 x 181 cm for characters of 25 cm, so in h): a white board, 0.4 h at the top, a plate 4 h x 4.64 h of the road's colour (blue on ordinary roads, green on
        /// expressways and urban expressways: 11.2.2) with a white line inside its edge and a white square 3 h with the camera in the plate's colour, 0.4 h, the lines of text, 0.4 h. The
        /// board is 5.4 h wide inside (the plate 0.7 h in from each side) or as wide as the text needs.
        /// </summary>
        private static GuideBoard CameraBoard(IList<string> lines, GuideColour colour, int band)
        {
            float h = H(band);
            GuideBlock text = AuxiliarySigns.Lines(lines, h, GuideColour.Black, 0.2f, 0.3f);
            float plateW = 4f * h, plateH = 4.64f * h;
            float innerW = Math.Max(5.4f * h, text.W + 0.8f * h);
            float innerH = 0.4f * h + plateH + 0.4f * h + text.H + 0.4f * h;
            GuideBoard board = AuxiliarySigns.Board(new GuideBlock(0f, 0f), h, 0f, 0f, GuideColour.White, GuideColour.Black, innerW, innerH);
            GuidePart part = board.Main;
            part.Elements.Clear();
            float e = part.Edge;
            float px = e + (innerW - plateW) * 0.5f, py = e + 0.4f * h;
            var canvas = new Canvas(part, 0f, 0f, part.W, part.H);
            canvas.Add(GuideKit.Rect(px, py, plateW, plateH, colour, 0.4f * h));
            canvas.Add(GuideKit.Rect(px + 0.12f * h, py + 0.12f * h, plateW - 0.24f * h, plateH - 0.24f * h, GuideColour.White, 0.3f * h));
            canvas.Add(GuideKit.Rect(px + 0.2f * h, py + 0.2f * h, plateW - 0.4f * h, plateH - 0.4f * h, colour, 0.22f * h));
            float sq = 3f * h, sx = px + (plateW - sq) * 0.5f, sy = py + (plateH - sq) * 0.5f;
            canvas.Add(GuideKit.Rect(sx, sy, sq, sq, GuideColour.White));
            canvas.Add(GuideKit.FitPicture(Glyphs.Mirror(Glyphs.N_Camera), sx + 0.3f * h, sy + 0.3f * h, sq - 0.6f * h, sq - 0.6f * h, colour));
            part.Add(text, e + (innerW - text.W) * 0.5f, py + plateH + 0.4f * h);
            return board;
        }

        private static GuideColour CameraColour(GuideSpec s) => s.Is("road", "green") ? GuideColour.Green : GuideColour.Blue;

        private static readonly GuideChoice[] kCameraRoads =
        {
            new GuideChoice("blue", "一般道路（蓝色图形）", "Ordinary road (blue)"),
            new GuideChoice("green", "高速公路、城市快速路（绿色图形）", "Expressway (green)")
        };

        private static IList<string> SectionLines(GuideSpec s)
        {
            switch (s.Get("form"))
            {
                case "end": return new[] { "区间测速", "终点" };
                case "len": return new[] { "前方", "区间测速", "长度" + s.Get("dist") + (s.Get("dist").Length > 0 ? s.Get("unit") : "") };
                default: return new[] { "区间测速", "起点" };
            }
        }

        // ------------------------------------------------------------------ 11.3 路外设施指引 (Fig. 323)

        private static readonly GuideChoice[] kFacilityArrows =
        {
            new GuideChoice("a", "→ 向右", "→ right"),
            new GuideChoice("e", "← 向左", "← left"),
            new GuideChoice("c", "↑ 前方", "↑ ahead"),
            new GuideChoice("b", "↗ 右前方", "↗ ahead right"),
            new GuideChoice("d", "↖ 左前方", "↖ ahead left"),
            new GuideChoice("none", "无箭头", "No arrow")
        };

        /// <summary>Fig. 323: up to three facilities (11.3: not more than 3), each a line of text with a guide arrow h square after it; the arrows in one column at the right; 0.3 h to the border, 0.4 h before the arrows.</summary>
        private static GuideBoard FacilityBoard(GuideSpec s, int band)
        {
            float h = H(band);
            var rows = new List<(GuideBlock text, string arrow)>();
            for (int i = 1; i <= 3; i++)
            {
                GuideBlock t = AuxiliarySigns.Line(s.Get("l" + i), h, GuideColour.Black);
                if (t.W > 0f)
                    rows.Add((t, s.Get("a" + i)));
            }

            if (rows.Count == 0)
                return AuxiliarySigns.Board(new GuideBlock(h, h), h, 0.3f * h, 0.3f * h);
            float textW = 0f;
            bool arrows = false;
            foreach ((GuideBlock t, string a) in rows)
            {
                textW = Math.Max(textW, t.W);
                arrows |= a != "none";
            }

            float lineGap = 0.3f * h;
            float w = textW + (arrows ? 0.4f * h + h : 0f);
            var block = new GuideBlock(w, rows.Count * h + (rows.Count - 1) * lineGap);
            float y = 0f;
            foreach ((GuideBlock t, string a) in rows)
            {
                block.Add(t, 0f, y);
                if (a != "none")
                    block.Add(GuideKit.ArrowBlock(GuideTemplates.ArrowOf(a), h, GuideColour.Black), w - h, y);
                y += h + lineGap;
            }

            return AuxiliarySigns.Board(block, h, 0.3f * h, 0.3f * h);
        }

        // ------------------------------------------------------------------ 11.4 行车安全提醒 (Fig. 324)

        /// <summary>
        /// Fig. 324 a: a red prohibition circle (ring a = D / 10 and bar b = 0.075 D: the proportions of Table 7) with a mobile phone (brown, a green screen) at the left, 3.2 h across, and two
        /// lines of text centred on each other, 0.6 h after it; 0.3 h to the border (read off the printed figure).
        /// </summary>
        private static GuideBoard PhoneBoard(GuideSpec s, int band)
        {
            float h = H(band);
            float d = 3.2f * h;
            GuideBlock text = AuxiliarySigns.Lines(new[] { s.Get("l1"), s.Get("l2") }, h, GuideColour.Black, 0.2f, 0.4f);
            var block = new GuideBlock(d + 0.6f * h + text.W, Math.Max(d, text.H));
            float cy = block.H * 0.5f;
            float a = 0.1f * d, b = 0.075f * d;
            block.Add(Art(GuideKit.Oval(0f, cy - d * 0.5f, d, d, GuideColour.Red)));
            block.Add(Art(GuideKit.Oval(a, cy - d * 0.5f + a, d - 2f * a, d - 2f * a, GuideColour.White)));
            float side = 0.62f * d, px = (d - side) * 0.5f, py = cy - side * 0.5f;
            block.Add(Art(GuideKit.Picture(Glyphs.N_Phone, px, py, side, side, GuideColour.Brown)));
            block.Add(Art(GuideKit.Rect(px + 0.36f * side, py + 0.35f * side, 0.28f * side, 0.18f * side, GuideColour.Green)));
            float reach = (d * 0.5f - a * 0.5f) / 1.41421356f;
            block.Add(Art(GuideKit.Bar(d * 0.5f - reach, cy - reach, d * 0.5f + reach, cy + reach, b, GuideColour.Red)));
            block.Add(text, d + 0.6f * h, cy - text.H * 0.5f);
            return AuxiliarySigns.Board(block, h, 0.3f * h, 0.3f * h);
        }

        /// <summary>Fig. 324 b (E.248, 6.5 h x 2 h): the black symbol 1 h across, 0.2 h, the characters 0.1 h apart, 0.3 h to the border.</summary>
        private static GuideBoard LitterBoard(GuideSpec s, int band)
        {
            float h = H(band);
            GuideBlock text = AuxiliarySigns.Line(s.Get("text"), h, GuideColour.Black, 0.1f);
            float gap = text.W > 0f ? 0.2f * h : 0f;
            var block = new GuideBlock(h + gap + text.W, h);
            block.Add(Art(GuideKit.Picture(Glyphs.N_NoLitter, 0f, 0f, h, h, GuideColour.Black)));
            block.Add(text, h + gap, 0f);
            return AuxiliarySigns.Board(block, h, 0.3f * h, 0.3f * h);
        }

        /// <summary>Fig. 324 c: a tall board, the seat belt in a box 4.8 h x 4 h, 0.6 h, the text; 0.4 h to the border (read off the printed figure).</summary>
        private static GuideBoard BeltBoard(GuideSpec s, int band)
        {
            float h = H(band);
            GuideBlock text = AuxiliarySigns.Line(s.Get("text"), h, GuideColour.Black, 0.3f);
            float picW = 4.8f * h, picH = 4f * h;
            float w = Math.Max(picW, text.W);
            var block = new GuideBlock(w, picH + (text.W > 0f ? 0.6f * h + h : 0f));
            block.Add(Art(GuideKit.FitPicture(Glyphs.N_SeatBelt, (w - picW) * 0.5f, 0f, picW, picH, GuideColour.Black)));
            block.Add(text, (w - text.W) * 0.5f, picH + 0.6f * h);
            return AuxiliarySigns.Board(block, h, 0.4f * h, 0.4f * h);
        }

        /// <summary>Fig. 324 d (E.249, 60 x 162 cm for characters of 30 cm): the characters one under the other 0.2 h apart, 0.3 h to the border at the sides and 0.2 h at the ends.</summary>
        private static GuideBoard VerticalBoard(GuideSpec s, int band)
        {
            float h = H(band);
            return AuxiliarySigns.Board(AuxiliarySigns.Column(s.Get("text"), h, GuideColour.Black), h, 0.3f * h, 0.2f * h);
        }

        /// <summary>A lane control signal of Fig. 324 f in a black square of side q: the green arrow down, the red cross, the white arrow down to the side.</summary>
        private static void LaneSignal(GuideBlock block, string kind, bool right, float x, float y, float q)
        {
            block.Add(Art(GuideKit.Rect(x, y, q, q, GuideColour.Black)));
            float[] P(params float[] u)
            {
                var p = new float[u.Length];
                for (int i = 0; i < u.Length; i += 2)
                {
                    p[i] = x + u[i] * q;
                    p[i + 1] = y + u[i + 1] * q;
                }

                return p;
            }

            switch (kind)
            {
                case "open":
                    block.Add(Art(GuideKit.Poly(P(0.33f, 0.08f, 0.67f, 0.08f, 0.67f, 0.48f, 0.92f, 0.48f, 0.5f, 0.92f, 0.08f, 0.48f, 0.33f, 0.48f), GuideColour.Green)));
                    break;
                case "closed":
                    block.Add(Art(GuideKit.Bar(x + 0.15f * q, y + 0.15f * q, x + 0.85f * q, y + 0.85f * q, 0.18f * q, GuideColour.Red)));
                    block.Add(Art(GuideKit.Bar(x + 0.85f * q, y + 0.15f * q, x + 0.15f * q, y + 0.85f * q, 0.18f * q, GuideColour.Red)));
                    break;
                default:
                {
                    // The arrow down to the right (down to the left when the lane changes to the left): a stem 0.2 q wide and a head, along the diagonal.
                    float[] u = { 0.122f, 0.278f, 0.278f, 0.122f, 0.598f, 0.442f, 0.732f, 0.308f, 0.86f, 0.86f, 0.308f, 0.732f, 0.442f, 0.598f };
                    if (!right)
                    {
                        for (int i = 0; i < u.Length; i += 2)
                            u[i] = 1f - u[i];
                    }

                    block.Add(Art(GuideKit.Poly(P(u), GuideColour.White)));
                    break;
                }
            }
        }

        /// <summary>Fig. 324 f: the title, then three rows of a lane control signal 1.2 h square and its meaning; rows 0.4 h apart, the text 0.5 h after the signal; 0.4 h to the border.</summary>
        private static GuideBoard LaneControlBoard(GuideSpec s, int band)
        {
            float h = H(band), q = 1.2f * h;
            GuideBlock title = AuxiliarySigns.Line(s.Get("title"), h, GuideColour.Black);
            string[] kinds = { "open", "closed", "change" };
            var rows = new List<GuideBlock>();
            for (int i = 0; i < 3; i++)
            {
                GuideBlock t = AuxiliarySigns.Line(s.Get("t" + (i + 1)), h, GuideColour.Black);
                var row = new GuideBlock(q + (t.W > 0f ? 0.5f * h + t.W : 0f), q);
                LaneSignal(row, kinds[i], !s.Is("side", "l"), 0f, 0f, q);
                row.Add(t, q + 0.5f * h, (q - h) * 0.5f);
                rows.Add(row);
            }

            GuideBlock body = AuxiliarySigns.Stack(rows, 0.4f * h, GuideAlign.Left, h);
            var all = new List<GuideBlock>();
            if (title.W > 0f)
                all.Add(title);
            all.Add(body);
            return AuxiliarySigns.Board(AuxiliarySigns.Stack(all, 0.4f * h, GuideAlign.Centre, h), h, 0.4f * h, 0.4f * h);
        }

        // ------------------------------------------------------------------ chapter 10: the tourist guide boards (旅1, 旅2)

        /// <summary>The character height of the tourist signs (4.5.7): the lower value of Table 3's range for the band (60, 50, 35, 25 cm).</summary>
        public static float TourHeight(int band) => new[] { 0.60f, 0.50f, 0.35f, 0.25f }[SignStyles.Band(band)];

        /// <summary>The space between the characters of a tourist area's name: 0.6 h for two (E.245), 0.4 h for three (E.246), 0.2 h for more.</summary>
        private static float NameGap(string name, float h) => (name ?? "").Length <= 2 ? 0.6f * h : name.Length == 3 ? 0.4f * h : 0.2f * h;

        private static GuideBlock TourName(string name, float h)
        {
            name = (name ?? "").Replace(" ", "");
            float gap = NameGap(name, h);
            foreach (char c in name)
            {
                // Letters or digits in the name (a typed 5A, a road number): as ink, by the auxiliary signs' line.
                if (c < 0x2E80)
                    return AuxiliarySigns.Line(name, h, GuideColour.White, gap / h);
            }

            var block = new GuideBlock(GuideKit.CellWidth(name.Length, h, gap), h);
            if (name.Length > 0)
                block.Add(GuideKit.Cells(name, 0f, 0f, h, GuideColour.White, gap));
            return block;
        }

        private static readonly GuideChoice[] kPictures =
        {
            new GuideChoice("hills", "山景（示例图形）", "Hills (example figure)"),
            new GuideChoice("temple", "塔与山（示例图形）", "Pagoda and mountain (example figure)"),
            new GuideChoice("none", "不设代表性图形", "No figure")
        };

        private static string PictureOf(string id) => id == "temple" ? Glyphs.N_Pagoda : id == "hills" ? Glyphs.N_Scenery : null;

        /// <summary>
        /// 旅1 旅游区距离 (Fig. 302, E.245, 6.9 h x 4.2 h): the name and the distance in one row (the name 0.6 h from the number, the unit km 0.5 h high), and under it, 0.4 h lower, the
        /// representative figure (10.1.2: may be given) in a band 1.8 h high; 0.4 h to the border at the sides and the top, 0.2 h at the bottom.
        /// </summary>
        private static GuideBoard TourDistance(GuideSpec s, int band)
        {
            float h = TourHeight(band);
            GuideBlock name = TourName(s.Get("name"), h);
            GuideBlock dist = AuxiliarySigns.Distance(s.Get("dist"), s.Get("unit"), h, GuideColour.White);
            float gap = name.W > 0f && dist.W > 0f ? 0.6f * h : 0f;
            var row = new GuideBlock(name.W + gap + dist.W, h);
            row.Add(name, 0f, 0f);
            row.Add(dist, name.W + gap, 0f);
            string picture = PictureOf(s.Get("pic"));
            float innerW = Math.Max(row.W, h) + 0.8f * h;
            float innerH = picture != null ? 0.4f * h + h + 0.4f * h + 1.8f * h + 0.2f * h : 0.4f * h + h + 0.4f * h;
            GuideBoard board = AuxiliarySigns.Board(new GuideBlock(0f, 0f), h, 0f, 0f, GuideColour.Brown, GuideColour.White, innerW, innerH);
            GuidePart part = board.Main;
            part.Elements.Clear();
            float e = part.Edge;
            part.Add(row, e + (innerW - row.W) * 0.5f, e + 0.4f * h);
            if (picture != null)
                new Canvas(part, e + 0.4f * h, e + 1.8f * h, innerW - 0.8f * h, 1.8f * h).Fit(picture, GuideColour.White);
            return board;
        }

        private static readonly GuideChoice[] kTourArrows =
        {
            new GuideChoice("a", "→ 向右", "→ right"),
            new GuideChoice("b", "↗ 右前方（高速公路出口）", "↗ ahead right (expressway exit)"),
            new GuideChoice("e", "← 向左", "← left"),
            new GuideChoice("d", "↖ 左前方", "↖ ahead left"),
            new GuideChoice("c", "↑ 前方", "↑ ahead")
        };

        /// <summary>The heavy arrow of the tourist boards (Fig. 303): a stem 0.3 h wide and a head of arms 0.36 h wide reaching 0.5 h to each side (E.246: in a box 2.5 h x 1 h; the printed arrow is heavier and a little larger).</summary>
        private static GuideElement TourArrow(float x0, float y0, float x1, float y1, float h) =>
            Art(GuideKit.Poly(AuxiliarySigns.LineArrow(x0, y0, x1, y1, 0.3f * h, 0.5f * h, 0.36f * h), GuideColour.White));

        /// <summary>
        /// 旅2 旅游区方向 (Fig. 303, E.246, 8.4 h x 4.7 h): the figure 3 h x 3.5 h at the left, 0.4 h, the name at the top right and under it the arrow, 2.5 h long and 1 h high (a diagonal one
        /// in a square of 2.1 h); 0.4 h to the border. Without a figure the name and the arrow are centred.
        /// </summary>
        private static GuideBoard TourDirection(GuideSpec s, int band)
        {
            float h = TourHeight(band);
            GuideBlock name = TourName(s.Get("name"), h);
            string picture = PictureOf(s.Get("pic"));
            string a = s.Get("arrow");
            bool diagonal = a == "b" || a == "d";
            bool vertical = a == "c";
            float arrowW = diagonal ? 2.1f * h : vertical ? h : 2.5f * h;
            float arrowH = diagonal || vertical ? 2.1f * h : h;
            float rightW = Math.Max(name.W, arrowW);
            float rightH = (name.W > 0f ? h + 0.4f * h : 0f) + arrowH;
            float picW = picture != null ? 3f * h : 0f, picH = 3.5f * h;
            float innerW = 0.4f * h + (picture != null ? picW + 0.4f * h : 0f) + rightW + 0.4f * h;
            float innerH = Math.Max(picture != null ? picH : 0f, rightH) + 0.8f * h;
            GuideBoard board = AuxiliarySigns.Board(new GuideBlock(0f, 0f), h, 0f, 0f, GuideColour.Brown, GuideColour.White, innerW, innerH);
            GuidePart part = board.Main;
            part.Elements.Clear();
            float e = part.Edge;
            if (picture != null)
                new Canvas(part, e + 0.4f * h, e + 0.4f * h, picW, innerH - 0.8f * h).Fit(picture, GuideColour.White);
            float rx = e + 0.4f * h + (picture != null ? picW + 0.4f * h : 0f);
            float top = e + 0.4f * h;
            float bottom = e + innerH - 0.4f * h;
            if (name.W > 0f)
                part.Add(name, rx + (rightW - name.W) * 0.5f, top);
            // The arrow stands at the bottom of the right-hand area (E.246: its box ends 0.7 h above the inner edge for the straight arrow; here the space under the name is shared evenly).
            float areaTop = name.W > 0f ? top + 1.4f * h : top;
            float ay = areaTop + (bottom - areaTop - arrowH) * 0.5f;
            float ax = rx + (rightW - arrowW) * 0.5f;
            var canvas = new Canvas(part, 0f, 0f, part.W, part.H);
            switch (a)
            {
                case "e": canvas.Add(TourArrow(ax + arrowW, ay + arrowH * 0.5f, ax, ay + arrowH * 0.5f, h)); break;
                case "c": canvas.Add(TourArrow(ax + arrowW * 0.5f, ay + arrowH, ax + arrowW * 0.5f, ay, h)); break;
                case "b": canvas.Add(TourArrow(ax + 0.35f * h, ay + arrowH - 0.35f * h, ax + arrowW - 0.35f * h, ay + 0.35f * h, h)); break;
                case "d": canvas.Add(TourArrow(ax + arrowW - 0.35f * h, ay + arrowH - 0.35f * h, ax + 0.35f * h, ay + 0.35f * h, h)); break;
                default: canvas.Add(TourArrow(ax, ay + arrowH * 0.5f, ax + arrowW, ay + arrowH * 0.5f, h)); break;
            }

            return board;
        }

        // ------------------------------------------------------------------ the tourist symbol signs (旅3 to 旅17)

        /// <summary>4.6.4: 60 x 60 cm, 45 x 45 cm on slow roads with little traffic (taken as the band under 40 km/h).</summary>
        public static float TourSymbolSide(int band) => SignStyles.Band(band) == 3 ? 0.45f : 0.60f;

        /// <summary>
        /// A tourist symbol sign (Fig. 304 to 318): a brown square, a brown liner 0.028 of the side, a white border 0.032 of the side and the corner radius 0.083 of the side (measured on the
        /// printed figures: 1.7 cm, 1.9 cm and 5 cm at 60 cm; 4.6.6 gives 4 cm for the radius of signs other than guide, notice and auxiliary boards), the white symbol in the inside.
        /// </summary>
        public static GuideBoard TourSymbol(int band, string glyph)
        {
            float a = TourSymbolSide(band);
            var part = new GuidePart { X = 0f, Y = 0f, W = a, H = a, Ground = GuideColour.Brown, BorderColour = GuideColour.White, Liner = 0.028f * a, Border = 0.032f * a, Radius = 0.083f * a };
            float e = part.Edge;
            part.Safe = new[] { e, e, a - e, e, a - e, a - e, e, a - e };
            new Canvas(part, e, e, a - 2f * e, a - 2f * e).Pic(glyph, GuideColour.White);
            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        private static readonly (string id, string code, string zh, string en, string pinyin, string[] words, Func<string> glyph, bool mirror)[] kSymbols =
        {
            ("gb.tour.03", "旅3", "信息服务", "Information", "xxfw", new[] { "信息", "问讯", "服务", "information" }, () => Glyphs.N_TourInfo, false),
            ("gb.tour.04", "旅4", "徒步", "Hiking", "tb", new[] { "徒步", "步行", "登山", "hiking" }, () => Glyphs.N_Hiker, true),
            ("gb.tour.05", "旅5", "索道", "Cable car", "sd", new[] { "索道", "缆车", "cable" }, () => Glyphs.N_CableCar, false),
            ("gb.tour.06", "旅6", "野营地", "Campsite", "yyd", new[] { "野营", "帐篷", "露营", "camping" }, () => Glyphs.N_Tent, false),
            ("gb.tour.07", "旅7", "营火", "Campfire", "yh", new[] { "营火", "篝火", "campfire" }, () => Glyphs.N_Campfire, false),
            ("gb.tour.08", "旅8", "旅居车营地", "Caravan site", "ljcyd", new[] { "旅居车", "房车", "营地", "caravan" }, () => Glyphs.N_Caravan, false),
            ("gb.tour.09", "旅9", "骑马", "Horse riding", "qm", new[] { "骑马", "马术", "riding" }, () => Glyphs.N_HorseRider, true),
            ("gb.tour.10", "旅10", "钓鱼", "Fishing", "dy", new[] { "钓鱼", "垂钓", "fishing" }, () => Glyphs.N_Fishing, true),
            ("gb.tour.11", "旅11", "高尔夫球", "Golf", "gefq", new[] { "高尔夫", "golf" }, () => Glyphs.N_Golf, false),
            ("gb.tour.12", "旅12", "潜水", "Diving", "qs", new[] { "潜水", "diving" }, () => Glyphs.N_Diving, false),
            ("gb.tour.13", "旅13", "游泳", "Swimming", "yy", new[] { "游泳", "泳池", "swimming" }, () => Glyphs.N_Swim, false),
            ("gb.tour.14", "旅14", "划船", "Boating", "hc", new[] { "划船", "游船", "boating" }, () => Glyphs.N_Boat, false),
            ("gb.tour.15", "旅15", "冬季游览区", "Winter recreation area", "djylq", new[] { "冬季", "雪花", "冰雪", "winter" }, () => Glyphs.N_Snowflake, false),
            ("gb.tour.16", "旅16", "滑雪", "Skiing", "hx", new[] { "滑雪", "雪场", "skiing" }, () => Glyphs.N_Ski, false),
            ("gb.tour.17", "旅17", "滑冰", "Skating", "hb", new[] { "滑冰", "溜冰", "skating" }, () => Glyphs.N_Skate, false)
        };

        private static string SymbolGlyph(string id)
        {
            foreach (var s in kSymbols)
            {
                if (s.id == id)
                    return s.mirror ? Glyphs.Mirror(s.glyph()) : s.glyph();
            }

            return null;
        }

        private static GuideChoice[] SymbolChoices()
        {
            var list = new List<GuideChoice>();
            foreach (var s in kSymbols)
                list.Add(new GuideChoice(s.id, s.code + " " + s.zh, s.en));
            list.Add(new GuideChoice("none", "不设", "None"));
            return list.ToArray();
        }

        /// <summary>
        /// Fig. 319 a: a tourist area's name, and under it up to three tourist symbols (small squares of 1.2 h, their white border inside the edge) and an arrow; read off the printed figure:
        /// 0.4 h to the border, 0.3 h between the rows, 0.2 h between the symbols, 0.5 h before the arrow (2 h long).
        /// </summary>
        private static GuideBoard TourCombination(GuideSpec s, int band)
        {
            float h = TourHeight(band), q = 1.2f * h;
            string name = (s.Get("name") ?? "").Replace(" ", "");
            float gap = name.Length <= 4 ? 0.5f * h : 0.2f * h;
            var nameBlock = new GuideBlock(GuideKit.CellWidth(name.Length, h, gap), h);
            if (name.Length > 0)
                nameBlock.Add(GuideKit.Cells(name, 0f, 0f, h, GuideColour.White, gap));
            var glyphs = new List<string>();
            foreach (string key in new[] { "s1", "s2", "s3" })
            {
                string g = SymbolGlyph(s.Get(key));
                if (g != null)
                    glyphs.Add(g);
            }

            string a = s.Get("arrow");
            bool hasArrow = a != "none";
            float symbolsW = glyphs.Count > 0 ? glyphs.Count * q + (glyphs.Count - 1) * 0.2f * h : 0f;
            float arrowW = hasArrow ? 2f * h : 0f;
            var row = new GuideBlock(symbolsW + (glyphs.Count > 0 && hasArrow ? 0.5f * h : 0f) + arrowW, q);
            float x = 0f;
            foreach (string g in glyphs)
            {
                GuideElement frame = GuideKit.Rect(x, 0f, q, q, GuideColour.Brown, 0.12f * q);
                frame.StrokeWidth = 0.06f * q;
                frame.Stroke = GuideColour.White;
                row.Add(Art(frame));
                row.Add(Art(GuideKit.Picture(g, x + 0.08f * q, 0.08f * q, 0.84f * q, 0.84f * q, GuideColour.White)));
                x += q + 0.2f * h;
            }

            if (hasArrow)
            {
                float ax = row.W - arrowW, cy = q * 0.5f;
                switch (a)
                {
                    case "e": row.Add(TourArrow(ax + arrowW, cy, ax, cy, h)); break;
                    default: row.Add(TourArrow(ax, cy, ax + arrowW, cy, h)); break;
                }
            }

            var parts = new List<GuideBlock>();
            if (nameBlock.W > 0f)
                parts.Add(nameBlock);
            if (row.W > 0f)
                parts.Add(row);
            GuideBlock all = AuxiliarySigns.Stack(parts, 0.3f * h, GuideAlign.Centre, h);
            return AuxiliarySigns.Board(all, h, 0.4f * h, 0.4f * h, GuideColour.Brown, GuideColour.White);
        }

        // ------------------------------------------------------------------ the catalogue

        public static IEnumerable<GuideTemplate> All()
        {
            // ---- chapter 10: tourist area signs
            yield return StandardSigns.Make("gb.tour.01", "旅1", "旅游区距离", "Tourist area distance", Tourist, "lyqjl", StandardSigns.Words("旅游", "景区", "距离", "tourist", "distance"),
                TourDistance,
                new[]
                {
                    GuideField.Text("name", "旅游区名称", "Tourist area", "灵山", 8),
                    GuideField.Digits("dist", "距离", "Distance", "16", 4),
                    GuideField.Choice("unit", "单位", "Unit", "km", new[] { new GuideChoice("km", "km", "km"), new GuideChoice("m", "m", "m") }, sp => sp.Get("dist").Length > 0),
                    GuideField.Choice("pic", "代表性图形", "Representative figure", "hills", kPictures)
                });

            yield return StandardSigns.Make("gb.tour.02", "旅2", "旅游区方向", "Tourist area direction", Tourist, "lyqfx", StandardSigns.Words("旅游", "景区", "方向", "tourist", "direction"),
                TourDirection,
                new[]
                {
                    GuideField.Text("name", "旅游区名称", "Tourist area", "云居寺", 8),
                    GuideField.Choice("arrow", "箭头", "Arrow", "a", kTourArrows),
                    GuideField.Choice("pic", "代表性图形", "Representative figure", "temple", kPictures)
                });

            foreach (var sym in kSymbols)
            {
                var item = sym;
                yield return StandardSigns.Make(item.id, item.code, item.zh, item.en, Tourist, item.pinyin, item.words,
                    band => TourSymbol(band, item.mirror ? Glyphs.Mirror(item.glyph()) : item.glyph()));
            }

            GuideChoice[] symbols = SymbolChoices();
            yield return StandardSigns.Make("gb.tour.18", "图319", "旅游符号与旅游区名称组合", "Tourist area name with symbols", Tourist, "lyfhylyqmczh", StandardSigns.Words("旅游", "组合", "景区", "符号", "tourist"),
                TourCombination,
                new[]
                {
                    GuideField.Text("name", "旅游区名称", "Tourist area", "森林公园", 8),
                    GuideField.Choice("s1", "符号一", "Symbol 1", "gb.tour.05", symbols),
                    GuideField.Choice("s2", "符号二", "Symbol 2", "gb.tour.06", symbols),
                    GuideField.Choice("s3", "符号三", "Symbol 3", "gb.tour.09", symbols),
                    GuideField.Choice("arrow", "箭头", "Arrow", "a", new[] { new GuideChoice("a", "→ 向右", "→ right"), new GuideChoice("e", "← 向左", "← left"), new GuideChoice("none", "无箭头", "No arrow") })
                });

            // ---- chapter 11: notice signs
            yield return StandardSigns.Make("gb.notice.01", "图320", "高速公路编号信息", "Expressway number information", Notice, "gsglbhxx", StandardSigns.Words("告示", "高速", "编号", "路名", "expressway"),
                ExpresswayInfo,
                new[]
                {
                    GuideField.Choice("road", "类别", "Class", "se", new[] { new GuideChoice("ge", "国家高速", "National expressway"), new GuideChoice("se", "省级高速", "Provincial expressway") }),
                    GuideField.Text("code", "编号", "Number", "S81", 5),
                    GuideField.Text("sub", "环线序号（右下小字）", "Ring ID (small, low)", "", 2),
                    GuideField.Text("prov", "省简称", "Province", "粤", 2, sp => sp.Is("road", "se")),
                    GuideField.Text("name", "高速公路名称", "Expressway name", "广州环城高速", 10)
                });

            yield return StandardSigns.Make("gb.notice.02", "图321", "交通监控设备信息", "Traffic enforcement camera", Notice, "jtjksbxx", StandardSigns.Words("告示", "监控", "抓拍", "测速", "camera"),
                (sp, band) => CameraBoard(new[] { sp.Get("text") }, CameraColour(sp), band),
                new[] { GuideField.Choice("road", "道路", "Road", "blue", kCameraRoads), GuideField.Text("text", "文字", "Text", "违法抓拍", 8) });

            yield return StandardSigns.Make("gb.notice.03", "图322", "区间测速信息", "Average speed check", Notice, "qjcsxx", StandardSigns.Words("告示", "区间", "测速", "speed", "camera"),
                (sp, band) => CameraBoard(SectionLines(sp), CameraColour(sp), band),
                new[]
                {
                    GuideField.Choice("form", "内容", "Content", "start", new[] { new GuideChoice("start", "a) 区间测速起点", "a) Start"), new GuideChoice("end", "b) 区间测速终点", "b) End"), new GuideChoice("len", "c) 前方区间测速长度", "c) Length ahead") }),
                    GuideField.Digits("dist", "长度", "Length", "500", 5, sp => sp.Is("form", "len")),
                    GuideField.Choice("unit", "单位", "Unit", "m", new[] { new GuideChoice("m", "m", "m"), new GuideChoice("km", "km", "km") }, sp => sp.Is("form", "len")),
                    GuideField.Choice("road", "道路", "Road", "green", kCameraRoads)
                });

            yield return StandardSigns.Make("gb.notice.04", "图323", "路外设施指引", "Off-road facilities", Notice, "lwsszy", StandardSigns.Words("告示", "设施", "指引", "facilities"),
                FacilityBoard,
                new[]
                {
                    GuideField.Text("l1", "第一条", "Item 1", "车辆管理所", 8), GuideField.Choice("a1", "箭头一", "Arrow 1", "a", kFacilityArrows),
                    GuideField.Text("l2", "第二条（可空）", "Item 2 (may be empty)", "税务服务厅", 8), GuideField.Choice("a2", "箭头二", "Arrow 2", "a", kFacilityArrows),
                    GuideField.Text("l3", "第三条（可空）", "Item 3 (may be empty)", "", 8), GuideField.Choice("a3", "箭头三", "Arrow 3", "a", kFacilityArrows)
                });

            yield return StandardSigns.Make("gb.notice.05", "图324a", "驾驶时禁用手持电话", "No hand-held phones while driving", Notice, "jssjyscdh", StandardSigns.Words("告示", "电话", "手机", "phone"),
                PhoneBoard,
                new[] { GuideField.Text("l1", "第一行", "Line 1", "驾驶时", 8), GuideField.Text("l2", "第二行", "Line 2", "禁用手持电话", 8) });

            yield return StandardSigns.Make("gb.notice.06", "图324b", "禁扔弃物", "No littering", Notice, "jrqw", StandardSigns.Words("告示", "扔", "垃圾", "litter"),
                LitterBoard, new[] { GuideField.Text("text", "文字", "Text", "禁扔弃物", 8) });

            yield return StandardSigns.Make("gb.notice.07", "图324c", "系安全带", "Fasten seat belts", Notice, "xaqd", StandardSigns.Words("告示", "安全带", "seat belt"),
                BeltBoard, new[] { GuideField.Text("text", "文字", "Text", "系安全带", 8) });

            yield return StandardSigns.Make("gb.notice.08", "图324d", "交替通行", "Merge in turn", Notice, "jttx", StandardSigns.Words("告示", "交替", "合流", "merge"),
                VerticalBoard, new[] { GuideField.Text("text", "文字（竖排）", "Text (vertical)", "交替通行", 6) });

            yield return StandardSigns.Make("gb.notice.09", "图324e", "严禁空挡下坡", "No coasting downhill in neutral", Notice, "yjkdxp", StandardSigns.Words("告示", "空挡", "下坡", "downhill"),
                (sp, band) => AuxiliarySigns.TextBoard(new[] { sp.Get("text") }, band, 0.3f, 0.3f), new[] { GuideField.Text("text", "文字", "Text", "严禁空挡下坡", 10) });

            yield return StandardSigns.Make("gb.notice.10", "图324f", "前方车道控制", "Lane control ahead", Notice, "qfcdkz", StandardSigns.Words("告示", "车道", "控制", "信号", "lane"),
                LaneControlBoard,
                new[]
                {
                    GuideField.Text("title", "标题", "Title", "前方车道控制", 8),
                    GuideField.Text("t1", "绿色箭头", "Green arrow", "车道开放", 6),
                    GuideField.Text("t2", "红色叉形", "Red cross", "车道关闭", 6),
                    GuideField.Text("t3", "白色斜箭头", "White diagonal arrow", "向右换道", 6),
                    GuideField.Choice("side", "换道方向", "Change to the", "r", new[] { new GuideChoice("r", "向右", "Right"), new GuideChoice("l", "向左", "Left") })
                });
        }
    }
}
