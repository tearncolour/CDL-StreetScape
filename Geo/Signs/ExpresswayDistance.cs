using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;
using static CDLC.TS.Geo.Signs.RoadGuideSigns;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// Signs of the expressway guide chapter of GB 5768.2-2022 (shared by the expressway and the urban expressway): 路38 to 路40, the place-distance boards (9.3.6, Fig. 260 to 262; the drawings
    /// E.204 to E.206). Green boards of rows, each an information at the left (a place in characters h high, a road number typed as G42 or 闽S10 drawn as its plate by Info, or a road name in a
    /// box of the colour 9.1.4 gives) and its distance at the right: the digits h high, right-aligned in a field 2 h wide (three digits), the unit 0.2 h after them, place and digits on one
    /// baseline; a plate's or a box's row is as high as the plate and the digits are centred on it. Rows 0.4 h apart, 0.4 h from the edge (E.204 to E.206). Near to far from the top, three
    /// lines at most (9.3.6.2).
    /// </summary>
    public static class ExpresswayDistanceSigns
    {
        private const string G = "guide";
        private const GuideColour Green = GuideColour.Green, White = GuideColour.White, Blue = GuideColour.Blue, Brown = GuideColour.Brown;

        /// <summary>E.204 to E.206: the rows of place and distance are 0.4 h apart.</summary>
        private const float RowGapK = 0.4f;

        /// <summary>E.204 to E.206: the digits' field is 2 h wide (it holds three digits: 112 km, 215 km in Fig. 262 a); a longer number widens it.</summary>
        private const float DigitsK = 2.0f;

        /// <summary>E.204 to E.206: 0.2 h between the digits' field and the unit's (1 h wide, 0.5 h high for km).</summary>
        private const float UnitGapK = 0.2f;

        /// <summary>E.205 a, E.206 a and b: the dividing line is 0.1 h thick.</summary>
        private const float LineK = 0.1f;

        // ------------------------------------------------------------------ rows

        private sealed class Row
        {
            public GuideBlock Info;
            public string Dist = "";

            /// <summary>Where the information starts (from the left margin) and the row's top and height (from the top of its group).</summary>
            public float X, Y, H;
        }

        private static readonly GuideChoice[] kKinds =
        {
            new GuideChoice("place", "地名（键入编号如 G42 时画编号牌）", "Place (a number such as G42: its plate)"),
            new GuideChoice("blue", "蓝底白字路名（无编号一般道路）", "Road name, white on blue (ordinary road)"),
            new GuideChoice("white", "白底绿字路名（无编号高速、快速路）", "Road name, green on white (expressway)"),
            new GuideChoice("brown", "棕底白字（旅游景区）", "White on brown (tourist area)")
        };

        /// <summary>
        /// An information of a row on the green board (9.1.4): a place in white characters, or a road number as its plate (Info, 8.1.5 and Appendix C); an unnumbered ordinary road's name
        /// white on a blue box, an unnumbered expressway's or urban expressway's green on a white box, a tourist area's white on a brown box.
        /// </summary>
        private static GuideBlock Information(string text, string kind, float h)
        {
            switch (kind)
            {
                case "blue": return NameBox(text, h, Blue, White, true);
                case "white": return NameBox(text, h, White, Green, false);
                case "brown": return NameBox(text, h, Brown, White, true);
                default: return PlaceInfo(text, h);
            }
        }

        /// <summary>
        /// A place, or a road number as its plate (Info). A name of four characters or more has them 0.1 h apart (E.205 b: 苏州新区; Table 6 asks for 0.1 h at least), shorter ones 0.2 h
        /// (E.204, E.206).
        /// </summary>
        private static GuideBlock PlaceInfo(string text, float h)
        {
            text = (text ?? "").Trim();
            int wide = 0;
            foreach (char c in text)
                wide += Wide(c) ? 1 : 0;
            if (wide >= 4 && wide == text.Length)
                return Line(text, h, White, 0.1f * h);
            return Info(text, h, Green);
        }

        /// <summary>
        /// The box of a road name (E.205 b, Fig. 261 b): as high as the expressway plate of table C.1 (100 cm, 70 at 80 km/h and less: the drawing's "100 (70)"), the characters h high and
        /// 0.2 h apart, 0.1 h from the box's ends (the drawing's 3.6 h for three characters), a thin white edge (0.06 h, read off the figure) and rounded corners (0.2 h, read off).
        /// </summary>
        private static GuideBlock NameBox(string text, float h, GuideColour fill, GuideColour ink, bool edge)
        {
            GuideBlock line;
            using (Bilingual.Suspend())
                line = Line(text, h, ink, NameGapK * h);
            if (line.W <= 0f)
                return new GuideBlock(0f, 0f);
            float P = ExpresswayPlateSize(h), pad = 0.1f * h;
            var box = new GuideBlock(line.W + 2f * pad, P);
            GuideElement body = Rect(0f, 0f, box.W, P, fill, 0.2f * h);
            if (edge)
            {
                body.StrokeWidth = 0.06f * h;
                body.Stroke = White;
            }

            box.Add(body);
            box.Add(line, pad, (P - h) * 0.5f);
            // An English line goes under the box, on the board.
            return Bilingual.Under(box, text, h, White);
        }

        /// <summary>The width of the digits' field: 2 h, or the widest number; 0 when no row has a distance.</summary>
        private static float DigitsWidth(IEnumerable<Row> rows, float h)
        {
            float w = 0f;
            bool any = false;
            foreach (Row r in rows)
            {
                if (r.Dist.Length == 0)
                    continue;
                any = true;
                w = Math.Max(w, LatinWidth(r.Dist, h));
            }

            return any ? Math.Max(DigitsK * h, w) : 0f;
        }

        private static float ColumnWidth(float digitsW, string unit, float h) => digitsW > 0f ? digitsW + UnitGapK * h + UnitWidth(unit, h) : 0f;

        /// <summary>A distance: the number h high right-aligned in its field, the unit after it on the same baseline ("km" 0.5 h high, "m" 0.4 h: the framework's UnitHeight, E.208).</summary>
        private static void PutDistance(GuidePart p, string value, string unit, float h, float colX, float digitsW, float baseline)
        {
            float numberW = LatinWidth(value, h);
            p.Elements.Add(Ink(value, colX + digitsW - numberW, baseline - h, numberW, h, White));
            float uh = UnitHeight(unit, h), uw = UnitWidth(unit, h);
            p.Elements.Add(Ink(unit, colX + digitsW + UnitGapK * h, baseline - uh, uw, uh, White, GuideAlign.Left, "k"));
        }

        /// <summary>The rows one under the other from y = 0, 0.4 h apart; returns their height.</summary>
        private static float StackRows(List<Row> rows, float h, float x)
        {
            float y = 0f;
            foreach (Row r in rows)
            {
                r.X = x;
                r.H = Math.Max(h, r.Info.H);
                r.Y = y;
                y += r.H + RowGapK * h;
            }

            return rows.Count > 0 ? y - RowGapK * h : 0f;
        }

        /// <summary>Puts the rows of a group whose top is at y0 (from the inside's top), the informations from the left margin, the distances in the column at colX (from the inside's left).</summary>
        private static void PutRows(GuidePart p, List<Row> rows, string unit, float h, float y0, float colX, float digitsW)
        {
            float e = p.Edge, m = MarginK * h;
            foreach (Row r in rows)
            {
                p.Add(r.Info, e + m + r.X, e + y0 + r.Y + (r.H - r.Info.H) * 0.5f);
                if (r.Dist.Length > 0)
                    PutDistance(p, r.Dist, unit, h, e + colX, digitsW, e + y0 + r.Y + (r.H - h) * 0.5f + h);
            }
        }

        /// <summary>
        /// A board of rows alone (路38, E.204; 路39 b, E.205 b): 0.4 h margins, the informations' column as wide as the widest, then at least gap (0.4 h in E.204 a, 0.3 h in E.205 b) to the
        /// digits' field, 0.2 h, the unit, 0.4 h. With no row at all the board keeps the size of one row of two characters.
        /// </summary>
        private static GuideBoard Stack(List<Row> rows, string unit, float h, float gap)
        {
            float m = MarginK * h;
            if (rows.Count == 0)
                rows.Add(new Row { Info = new GuideBlock(2f * h, h) });
            float rowsH = StackRows(rows, h, 0f);
            float infoW = 0f;
            foreach (Row r in rows)
                infoW = Math.Max(infoW, r.Info.W);
            float digitsW = DigitsWidth(rows, h), colW = ColumnWidth(digitsW, unit, h);
            float innerW = m + infoW + (colW > 0f ? gap + colW : 0f) + m;
            GuidePart p = Board(h, innerW, m + rowsH + m, Green);
            PutRows(p, rows, unit, h, m, innerW - m - colW, digitsW);
            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路38 地点距离 Fig. 260, E.204

        /// <summary>
        /// 路38 (9.3.6.3, Fig. 260, E.204): three lines, near to far from the top: the first the destination reached through the next interchange, the second and the third the nearest B layer
        /// and the A layer information on the route ahead. a) three places (E.204 a: 7 h by 5 h for two-character names and two-digit distances); b) the second line a road number, drawn as
        /// its plate 100 (70) cm high with the distance centred on it (E.204 b). A line left empty is left out.
        /// </summary>
        private static GuideBoard PlaceDistance(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            bool b = s.Is("form", "b");
            var rows = new List<Row>();
            for (int i = 1; i <= 3; i++)
            {
                bool number = b && i == 2;
                GuideBlock info = number ? Info(s.Get("code2"), h, Green) : Information(s.Get("to" + i), s.Get("k" + i), h);
                if (info.W <= 0f)
                    continue;
                rows.Add(new Row { Info = info, Dist = s.Get("d" + i) });
            }

            return Stack(rows, s.Get("unit"), h, 0.4f * h);
        }

        // ------------------------------------------------------------------ 路39 城市区域多个出口时地点距离 Fig. 261, E.205

        /// <summary>
        /// 路39 a (Fig. 261 a, E.205 a): the city's name over the number of its exits ("5个出口": the number in a cell of its own, then 个出口), each line 0.4 h from the edge and from the
        /// white line 0.1 h thick that parts them and joins the border; 5.8 h by 4.1 h for four characters. Set before the first exit advance board of the city's first interchange.
        /// </summary>
        private static GuideBoard ExitCount(GuideSpec s, int band)
        {
            float h = CharHeight(band), m = MarginK * h, t = LineK * h;
            GuideBlock city = Line(s.Get("city"), h, White, NameGapK * h);
            GuideBlock count = new GuideBlock(0f, 0f);
            string number = s.Get("exits");
            if (number.Length > 0)
            {
                float cell = Math.Max(h, LatinWidth(number, h));
                count = new GuideBlock(cell + NameGapK * h + CellWidth(3, h, NameGapK * h), h);
                count.Add(Ink(number, 0f, 0f, cell, h, White, GuideAlign.Centre));
                count.Add(Cells("个出口", cell + NameGapK * h, 0f, h, White, NameGapK * h));
            }

            float innerW = Math.Max(Math.Max(city.W, count.W), 2f * h) + 2f * m;
            float half = m + h + m;
            GuidePart p = Board(h, innerW, half + t + half, Green);
            float e = p.Edge;
            if (city.W > 0f)
                p.Add(city, e + (innerW - city.W) * 0.5f, e + m);

            // The line runs into the white border at both ends: the part of it bled over the border (drawn first, unclipped) is the whole line made longer by half the border at each end.
            float bleed = 0.5f * p.Border;
            GuideElement line = Rect(e, e + half + bleed, innerW, t - bleed, White);
            line.Bleed = bleed;
            p.Elements.Add(line);

            if (count.W > 0f)
                p.Add(count, e + (innerW - count.W) * 0.5f, e + half + t + m);
            return Single(p, h);
        }

        /// <summary>
        /// 路39 b (Fig. 261 b, E.205 b): up to three rows of place and distance after the board a); road names in boxes (blue for the city's roads), the last row a place; 0.3 h at least between
        /// the informations and the digits (E.205 b: 9 h wide with the four characters of 苏州新区 0.1 h apart; this layout gives 9.02 h, the unit being 1.02 h for 1 h).
        /// </summary>
        private static GuideBoard CityBoard(GuideSpec s, int band)
        {
            if (!s.Is("form", "b"))
                return ExitCount(s, band);
            float h = CharHeight(band);
            var rows = new List<Row>();
            for (int i = 1; i <= 3; i++)
            {
                GuideBlock info = Information(s.Get("to" + i), s.Get("k" + i), h);
                if (info.W <= 0f)
                    continue;
                rows.Add(new Row { Info = info, Dist = s.Get("d" + i) });
            }

            return Stack(rows, s.Get("unit"), h, 0.3f * h);
        }

        // ------------------------------------------------------------------ 路40 同时指引前方到达道路上的地点距离 Fig. 262, E.206

        /// <summary>A group of 路40: the plates of a road and the rows of its places.</summary>
        private sealed class Group
        {
            public readonly List<GuideBlock> Plates = new List<GuideBlock>();
            public readonly List<Row> Rows = new List<Row>();
            public bool Top;
            public float PlatesW, PlatesH, RowsH, H, RowsY, PlatesY;

            public bool Empty => Plates.Count == 0 && Rows.Count == 0;

            /// <summary>The space between two plates side by side: 0.4 h above the rows (E.206 a), 0.2 h at the left (not in the figures).</summary>
            public float PlateGap(float h) => (Top ? 0.4f : 0.2f) * h;

            public void MeasurePlates(float h)
            {
                PlatesW = 0f;
                PlatesH = 0f;
                for (int i = 0; i < Plates.Count; i++)
                {
                    PlatesW += (i > 0 ? PlateGap(h) : 0f) + Plates[i].W;
                    PlatesH = Math.Max(PlatesH, Plates[i].H);
                }
            }

            /// <summary>The rows from x (after the plates at the left), and the group's height: plates on top, 0.5 h, the rows (E.206 a); or the plates and the rows centred on each other (E.206).</summary>
            public void Layout(float h, float x)
            {
                RowsH = StackRows(Rows, h, Top ? 0f : x);
                if (Top)
                {
                    PlatesY = 0f;
                    RowsY = Plates.Count > 0 && Rows.Count > 0 ? PlatesH + 0.5f * h : Plates.Count > 0 ? PlatesH : 0f;
                    H = RowsY + RowsH;
                }
                else
                {
                    H = Math.Max(PlatesH, RowsH);
                    PlatesY = (H - PlatesH) * 0.5f;
                    RowsY = (H - RowsH) * 0.5f;
                }

                foreach (Row r in Rows)
                    r.Y += RowsY;
            }

            public float RowsRight()
            {
                float w = 0f;
                foreach (Row r in Rows)
                    w = Math.Max(w, r.X + r.Info.W);
                return w;
            }
        }

        /// <summary>
        /// 路40 (9.3.6.4, Fig. 262, E.206): the places on the current road above, those on the road reached ahead below, parted by a white line 0.1 h thick within the margins, 0.4 h from the
        /// rows on either side; each group gives its road number (9.3.6.4: 应给出道路编号). The current road's plates stand over its places (a, two plates 0.4 h apart for a shared section,
        /// then 0.5 h) or at their left (b); the road ahead's plate at the left of its places, centred on them, the places 0.2 h after it, then 0.3 h at least to the digits (E.206). Three lines in
        /// all at most (9.3.6.2): the current road's second place is offered only when the road ahead has one.
        /// </summary>
        private static GuideBoard Ahead(GuideSpec s, int band)
        {
            float h = CharHeight(band), m = MarginK * h, t = LineK * h, gap = 0.3f * h;
            string unit = s.Get("unit");

            var up = new Group { Top = s.Is("upos", "top") };
            foreach (string key in new[] { "uc1", "uc2" })
            {
                GuideBlock plate = Info(s.Get(key), h, Green);
                if (plate.W > 0f)
                    up.Plates.Add(plate);
            }

            var down = new Group();
            GuideBlock aheadPlate = Info(s.Get("dc"), h, Green);
            if (aheadPlate.W > 0f)
                down.Plates.Add(aheadPlate);

            bool upperTwo = s.Get("to4").Length == 0;
            for (int i = 1; i <= 4; i++)
            {
                if (i == 2 && !upperTwo)
                    continue;
                GuideBlock info = PlaceInfo(s.Get("to" + i), h);
                if (info.W <= 0f)
                    continue;
                (i <= 2 ? up : down).Rows.Add(new Row { Info = info, Dist = s.Get("d" + i) });
            }

            up.MeasurePlates(h);
            down.MeasurePlates(h);

            // The places after the plates at the left start at one x in both groups (E.206 b: the two plates are as wide, and so are the columns).
            float x = 0f;
            foreach (Group g in new[] { up, down })
            {
                if (!g.Top && g.Plates.Count > 0)
                    x = Math.Max(x, g.PlatesW + 0.2f * h);
            }

            up.Layout(h, x);
            down.Layout(h, x);

            var all = new List<Row>(up.Rows);
            all.AddRange(down.Rows);
            float digitsW = DigitsWidth(all, h), colW = ColumnWidth(digitsW, unit, h);
            float leftW = Math.Max(up.RowsRight(), down.RowsRight());
            if (!up.Top)
                leftW = Math.Max(leftW, up.PlatesW);
            leftW = Math.Max(Math.Max(leftW, down.PlatesW), 2f * h);
            float innerW = m + leftW + (colW > 0f ? gap + colW : 0f) + m;
            if (up.Top)
                innerW = Math.Max(innerW, up.PlatesW + 2f * m);

            bool both = !up.Empty && !down.Empty;
            float upY = m;
            float lineY = upY + up.H + RowGapK * h;
            float downY = both ? lineY + t + RowGapK * h : m;
            float innerH = up.Empty && down.Empty ? m + h + m : (down.Empty ? upY + up.H : downY + down.H) + m;

            GuidePart p = Board(h, innerW, innerH, Green);
            float e = p.Edge, colX = innerW - m - colW;

            if (!up.Empty)
            {
                float px = up.Top ? (innerW - up.PlatesW) * 0.5f : m;
                foreach (GuideBlock plate in up.Plates)
                {
                    p.Add(plate, e + px, e + upY + up.PlatesY + (up.PlatesH - plate.H) * 0.5f);
                    px += plate.W + up.PlateGap(h);
                }

                PutRows(p, up.Rows, unit, h, upY, colX, digitsW);
            }

            if (both)
                p.Elements.Add(Rect(e + m, e + lineY, innerW - 2f * m, t, White));

            if (!down.Empty)
            {
                float px = m;
                foreach (GuideBlock plate in down.Plates)
                {
                    p.Add(plate, e + px, e + downY + down.PlatesY + (down.PlatesH - plate.H) * 0.5f);
                    px += plate.W + down.PlateGap(h);
                }

                PutRows(p, down.Rows, unit, h, downY, colX, digitsW);
            }

            return Single(p, h);
        }

        // ------------------------------------------------------------------ the catalogue

        private static GuideField Dist(string key, string zh, string en, string def, Func<GuideSpec, bool> visible = null) => GuideField.Digits(key, zh, en, def, 4, visible);

        private static GuideField Kind(string key, string def, Func<GuideSpec, bool> visible = null) =>
            GuideField.Choice(key, "信息形式（9.1.4）", "Kind of information (9.1.4)", def, kKinds, visible);

        private static GuideField Unit(Func<GuideSpec, bool> visible = null) =>
            GuideField.Choice("unit", "距离单位", "Unit", "km", GuideTemplates.kUnits, visible);

        /// <summary>
        /// The settings of 路38: the same board in both entries, the form and the example's places and distances are what differ. Form a is Fig. 260 a (采育 14 km, 廊坊 37 km, 天津 95 km); form b
        /// is Fig. 260 b (璜塘 8 km, the road G42 17 km, 上海 25 km: the second line is a road number, drawn as its plate by Info).
        /// </summary>
        private static GuideField[] DistanceFields(bool b) => new[]
        {
            GuideField.Choice("form", "形式", "Form", b ? "b" : "a", new[]
            {
                new GuideChoice("a", "三行地点（图 260 a）", "Three places (Fig. 260 a)"),
                new GuideChoice("b", "第二行为道路编号（图 260 b）", "The second line a road number (Fig. 260 b)")
            }),
            Place("to1", "第一行：前方互通的目的地", "Line 1: via the next interchange", b ? "璜塘" : "采育"),
            Kind("k1", "place"),
            Dist("d1", "第一行距离", "Line 1 distance", b ? "8" : "14"),
            GuideField.Text("to2", "第二行：最近的 B 层信息", "Line 2: nearest B layer", "廊坊", 7, s => !s.Is("form", "b")),
            GuideField.Text("code2", "第二行：道路编号（如 G42）", "Line 2: road number (G42)", "G42", 6, s => s.Is("form", "b")),
            Kind("k2", "place", s => !s.Is("form", "b")),
            Dist("d2", "第二行距离", "Line 2 distance", b ? "17" : "37"),
            Place("to3", "第三行：A 层信息", "Line 3: A layer", b ? "上海" : "天津"),
            Kind("k3", "place"),
            Dist("d3", "第三行距离", "Line 3 distance", b ? "25" : "95"),
            Unit()
        };

        public static IEnumerable<GuideTemplate> All()
        {
            // 9.3.6, Fig. 260, E.204: listed twice so that form b has an entry of its own with the figure's example.
            yield return StandardSigns.Make("gb.guide9.38", "路38", "地点距离", "Place distance (expressway)", G, "ddjl", new[] { "地点距离", "距离", "高速", "确认", "里程", "distance" },
                PlaceDistance, DistanceFields(false));

            yield return StandardSigns.Make("gb.guide9.38b", "路38", "地点距离（第二行为道路编号）", "Place distance (the second line a road number)", G, "ddjl", new[] { "地点距离", "距离", "高速", "道路编号", "G42", "里程", "distance" },
                PlaceDistance, DistanceFields(true));

            yield return StandardSigns.Make("gb.guide9.39", "路39", "城市区域多个出口时地点距离", "Place distance in a city with several exits", G, "csqydgcksddjl",
                new[] { "城区", "出口数量", "个出口", "城市", "距离", "exits" },
                CityBoard, new[]
                {
                    GuideField.Choice("form", "形式", "Form", "a", new[]
                    {
                        new GuideChoice("a", "城市出口数量（图 261 a，设在最前）", "Number of exits (Fig. 261 a)"),
                        new GuideChoice("b", "地点距离（图 261 b，在 a 后）", "Places and distances (Fig. 261 b, after a)")
                    }),
                    GuideField.Text("city", "城市区域名称", "City area", "苏州城区", 6, s => !s.Is("form", "b")),
                    GuideField.Digits("exits", "出口数量", "Number of exits", "5", 2, s => !s.Is("form", "b")),
                    GuideField.Text("to1", "第一行：路名或地点", "Line 1: road or place", "人民路", 7, s => s.Is("form", "b")),
                    Kind("k1", "blue", s => s.Is("form", "b")),
                    Dist("d1", "第一行距离", "Line 1 distance", "7", s => s.Is("form", "b")),
                    GuideField.Text("to2", "第二行（可空）", "Line 2 (may be empty)", "东环路", 7, s => s.Is("form", "b")),
                    Kind("k2", "blue", s => s.Is("form", "b")),
                    Dist("d2", "第二行距离", "Line 2 distance", "17", s => s.Is("form", "b")),
                    GuideField.Text("to3", "第三行（可空）", "Line 3 (may be empty)", "苏州新区", 7, s => s.Is("form", "b")),
                    Kind("k3", "place", s => s.Is("form", "b")),
                    Dist("d3", "第三行距离", "Line 3 distance", "30", s => s.Is("form", "b")),
                    Unit(s => s.Is("form", "b"))
                });

            yield return StandardSigns.Make("gb.guide9.40", "路40", "同时指引前方到达道路上的地点距离", "Place distance with the road reached ahead", G, "tszyqfdddlsdddjl",
                new[] { "地点距离", "前方道路", "编号", "高速", "距离", "distance" },
                Ahead, new[]
                {
                    GuideField.Text("uc1", "当前道路编号", "Current road number", "闽S10", 6),
                    GuideField.Text("uc2", "当前道路编号二", "Second road number", "闽S52", 6),
                    GuideField.Choice("upos", "当前道路编号位置", "Current road number placed", "top", new[]
                    {
                        new GuideChoice("top", "地点上方（图 262 a）", "Above its places (Fig. 262 a)"),
                        new GuideChoice("left", "地点左侧（图 262 b）", "At the left of its places (Fig. 262 b)")
                    }),
                    Place("to1", "当前道路地点一", "Place on the current road 1", "洋后"),
                    Dist("d1", "距离", "Distance", "8"),
                    GuideField.Text("to2", "当前道路地点二", "Place on this road 2", "", 7, s => s.Get("to4").Length == 0),
                    Dist("d2", "距离", "Distance", "", s => s.Get("to4").Length == 0),
                    GuideField.Text("dc", "前方到达道路编号", "Number of the road reached ahead", "G3", 6),
                    Place("to3", "前方道路地点一", "Place on the road ahead 1", "衢州"),
                    Dist("d3", "距离", "Distance", "112"),
                    Place("to4", "前方道路地点二", "Place on the road ahead 2", "福州"),
                    Dist("d4", "距离", "Distance", "215"),
                    Unit()
                });
        }
    }
}
