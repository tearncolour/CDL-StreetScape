using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;

namespace CDLC.TS.Geo
{
    /// <summary>A guide board that is offered in the panel: a template with some of its fields set. Choosing it makes a draft of its spec, which can then be edited.</summary>
    public sealed class GuidePreset
    {
        public string Id, Template, Code, Zh, En, Pinyin;
        public string[] Keywords;

        /// <summary>The fields it sets, "key=value;key=value"; the rest are the template's defaults.</summary>
        public string Overrides;
    }

    /// <summary>
    /// The guide boards of GB 5768.2-2022 as templates: every board is laid out from its character height h by the rules and figures of the standard (Table 3 to 6, Fig. 3, the
    /// drawings of Appendix E, Appendix C), from fields the player can edit. What is covered: exit number (路41), exit advance and exit direction on ordinary roads and at
    /// interchanges of expressways (路42 to 47, with a numbered road, an unnumbered one or an expressway), the through lane's strip (路48), the 300/200/100 m boards (路52), the exit
    /// sign (路53), the next exit (路54), and the expressway plates of Appendix C. What is not: the forked boards (路49 to 51) with their lane graphics, two roads side by side
    /// (路47 b), the boards of the ordinary road guide chapter (8), and the notices.
    /// </summary>
    public static class GuideTemplates
    {
        // ------------------------------------------------------------------ the speed bands

        /// <summary>The character height h of Table 3 and the digit height D of Table 5 for a band of design speed (mid values of the ranges the tables give).</summary>
        internal sealed class Band
        {
            /// <summary>The height of the Chinese characters and of the digits (Table 3 and Table 5), in metres.</summary>
            public float H, D;
        }

        internal static Band BandOf(string id)
        {
            switch (id)
            {
                case "80": return new Band { H = 0.55f, D = 0.375f };
                case "60": return new Band { H = 0.40f, D = 0.275f };
                case "30": return new Band { H = 0.30f, D = 0.175f };
                // "100", and "auto" (the board is laid out before the road is known: the tool puts a real band in when it places the board).
                default: return new Band { H = 0.65f, D = 0.45f };
            }
        }

        internal static readonly GuideChoice[] kSpeeds =
        {
            new GuideChoice("100", "100–120 km/h（65 cm）", "100–120 km/h (char height 65 cm)"),
            new GuideChoice("80", "71–99 km/h（55 cm）", "71–99 km/h (char height 55 cm)"),
            new GuideChoice("60", "40–70 km/h（40 cm）", "40–70 km/h (char height 40 cm)"),
            new GuideChoice("30", "<40 km/h（30 cm）", "<40 km/h (char height 30 cm)")
        };

        private static readonly GuideChoice kAutoSpeed = new GuideChoice("auto", "自动（高速按限速，城市 40–70 档）", "Auto (city street: up to 40–70)");

        /// <summary>The speed band of a board by the road's speed limit (GB 5768.2 Table 3: 100 to 120, 71 to 99, 40 to 70, under 40 km/h): the value of the "speed" field.</summary>
        public static string BandForSpeed(float kmh) => kmh >= 99.5f ? "100" : kmh >= 70.5f ? "80" : kmh >= 39.5f ? "60" : "30";

        /// <summary>
        /// The band of a road (the "speed" setting that "auto" becomes): a road that runs by the game's highway rules (the motorway and expressway prefabs) takes the band of its speed limit as Table 3
        /// has it; **any other road is a city street and never goes above the 40 to 70 km/h band (character height 40 cm)**, whatever its limit says. The standard chooses the band by the road's
        /// *design* speed (4.5.2), which the game does not keep; the game's limit on a city street (80 or 100 km/h on a wide avenue) is a game rule and not a design speed, and read as one it put
        /// the characters of a street sign at 55 and 65 cm, the size of a motorway's (the owner saw this on 2026-10-19). That the design speed of a city street is at most 60 km/h (the
        /// expressways, which are highways in the game, are the exception) is from the city road design code CJJ 37, **recalled and not checked against its text**: provisional.
        /// </summary>
        public static string BandForRoad(float kmh, bool highwayRules)
        {
            string band = BandForSpeed(kmh);
            return !highwayRules && (band == "100" || band == "80") ? "60" : band;
        }

        /// <summary>
        /// The draft with its "speed" setting made concrete: "auto" becomes the band of the road's speed limit (BandForRoad: a city street is kept to the 40 to 70 km/h band; highwayRules false),
        /// or 40 to 70 km/h when there is no road or it has none. highwayRules defaults to true, which is what every caller before 0.6.0 got: the band of the limit alone.
        /// </summary>
        public static string ResolveSpeed(SignDef def, string draft, float kmh, bool known, bool highwayRules = true)
        {
            GuideTemplate template = def.Template;
            if (template == null || template.FieldOf("speed") == null)
                return draft;
            GuideSpec spec = template.Parse(draft);
            if (!spec.Is("speed", "auto"))
                return draft;
            return spec.With("speed", known ? BandForRoad(kmh, highwayRules) : "60").ToString();
        }

        /// <summary>The character height h (m) of a band (Table 3, a value inside each range).</summary>
        public static float CharHeightOf(string band) => BandOf(band).H;

        public static GuideField SpeedField()
        {
            var choices = new List<GuideChoice> { kAutoSpeed };
            choices.AddRange(kSpeeds);
            return GuideField.Choice("speed", "设计速度（字高档）", "Design speed (character height)", "auto", choices.ToArray());
        }

        // ------------------------------------------------------------------ road lines

        internal static readonly GuideChoice[] kRoads =
        {
            new GuideChoice("g", "国道 G（红底）", "National road G (red)"),
            new GuideChoice("s", "省道 S（黄底）", "Provincial road S (yellow)"),
            new GuideChoice("ge", "国家高速（红条）", "National expressway (red strip)"),
            new GuideChoice("se", "省级高速（黄条）", "Provincial expressway (yellow strip)"),
            new GuideChoice("name", "无编号道路（白底路名）", "Unnumbered road (white name band)"),
            new GuideChoice("none", "不设", "None")
        };

        internal static bool HasCode(GuideSpec s) => s.Get("road") != "name" && s.Get("road") != "none";

        internal static IEnumerable<GuideField> RoadFields()
        {
            yield return GuideField.Choice("road", "道路", "Road", "g", kRoads);
            yield return GuideField.Text("code", "编号", "Number", "G204", 5, HasCode);
            yield return GuideField.Text("sub", "环线序号（右下小字）", "Ring ID (small, low)", "", 2, s => s.Is("road", "ge") || s.Is("road", "se"));
            yield return GuideField.Text("prov", "省简称", "Province", "沪", 2, s => s.Is("road", "se"));
            yield return GuideField.Text("rname", "路名", "Road name", "前进路", 8, s => s.Is("road", "name"));
        }

        internal static RoadPlateKind PlateKind(string road)
        {
            switch (road)
            {
                case "s": return RoadPlateKind.ProvincialRoad;
                case "ge": return RoadPlateKind.NationalExpressway;
                case "se": return RoadPlateKind.ProvincialExpressway;
                default: return RoadPlateKind.NationalRoad;
            }
        }

        internal static readonly GuideChoice[] kArrows =
        {
            new GuideChoice("b", "↗ 右侧出口 / 斜向右", "↗ right exit / diagonal right"),
            new GuideChoice("d", "↖ 左侧出口 / 斜向左", "↖ left exit / diagonal left"),
            new GuideChoice("c", "↑ 前进", "↑ ahead"),
            new GuideChoice("a", "→ 向右", "→ right"),
            new GuideChoice("e", "← 向左", "← left"),
            new GuideChoice("f", "↓ 当前车道（路上方）", "↓ this lane (overhead)")
        };

        internal static GuideArrow ArrowOf(string id)
        {
            switch (id)
            {
                case "d": return GuideArrow.UpLeft;
                case "c": return GuideArrow.Up;
                case "a": return GuideArrow.Right;
                case "e": return GuideArrow.Left;
                case "f": return GuideArrow.Down;
                default: return GuideArrow.UpRight;
            }
        }

        private static readonly GuideChoice[] kLaneArrows =
        {
            new GuideChoice("1", "一个箭头居中（图 270 a、b）", "One arrow, middle (Fig. 270 a, b)"),
            new GuideChoice("2", "两个箭头（图 270 c）", "Two arrows (Fig. 270 c)")
        };

        private static readonly GuideChoice[] kMarkDirs =
        {
            new GuideChoice("", "不设", "None"),
            new GuideChoice("东", "东", "East"),
            new GuideChoice("南", "南", "South"),
            new GuideChoice("西", "西", "West"),
            new GuideChoice("北", "北", "North")
        };

        internal static readonly GuideChoice[] kSides =
        {
            new GuideChoice("r", "右侧出口", "Right exit"),
            new GuideChoice("l", "左侧出口（左出口）", "Left exit")
        };

        internal static readonly GuideChoice[] kUnits =
        {
            new GuideChoice("km", "km", "km"),
            new GuideChoice("m", "m", "m")
        };

        private static IEnumerable<GuideField> ExitNumberFields(string number, string side)
        {
            yield return GuideField.Text("xnum", "出口编号（空为不设）", "Exit number (empty: none)", number, 4);
            yield return GuideField.Text("xsuf", "编号后缀（A、B、A-B）", "Suffix (A, B, A-B)", "", 3, s => s.Get("xnum").Length > 0);
            yield return GuideField.Choice("xside", "编号牌位置", "Number plate side", side, kSides, s => s.Get("xnum").Length > 0);
        }

        // ------------------------------------------------------------------ the exit advance and direction boards (路42 to 48)

        /// <summary>
        /// 路42 to 47 (Fig. E.208 to E.213, in h): the board is 5.6 h high for two places: edge 0.2 h, margin 0.4 h, the road line 1.5 h (a plate) or 1.9 h (the white name band that fills the
        /// width), 0.4 h, the place names 1 h, 0.5 h, the distance and the arrow 1 h, margin, edge. The names are 0.6 h apart, the distance starts at the margin and the arrow stands 0.6 h
        /// in from the right. With two names of two characters the board is 6.2 h wide, as in the figures. The exit number plate sits on top, at the side of the exit. A through lane
        /// (路48, Fig. 270, E.215 a) replaces the last row by a yellow strip with a black arrow: 0.3 h under the names, 1.7 h high (0.3 h, the arrow 1 h, 0.4 h). An expressway plate (E.212,
        /// E.213 a, E.215 a: "100 (70)" x "125 (85)" or "170 (115)") has the size of table C.1 and not 1.5 h, which makes the board 5.64 h high at 100 km/h.
        /// </summary>
        private static GuideBoard ExitBoard(GuideSpec s, bool advance)
        {
            Band band = BandOf(s.Get("speed"));
            float h = band.H;
            float m = MarginK * h, edge = EdgeK * h;
            string road = s.Get("road");
            bool lane = !advance && s.Flag("lane");
            bool twoArrows = lane && s.Is("lanes", "2");
            string dir = advance ? "" : s.Get("dir");

            GuideBlock plate = null;
            string bandName = null;
            if (road == "name")
                bandName = s.Get("rname");
            else if (road != "none")
            {
                // The plates of national and provincial roads are 1.5 h high (E.208 to E.211); the expressway plates of Appendix C have the sizes of table C.1, which E.212 and E.213 a give as
                // 100 (70) x 170 (115) cm, so that the board is 5.64 h high at 100 km/h (it was 5.6 h with a plate of 1.5 h whatever the band).
                RoadPlateKind kind = PlateKind(road);
                bool expressway = kind == RoadPlateKind.NationalExpressway || kind == RoadPlateKind.ProvincialExpressway;
                plate = RoadPlate(kind, s.Get("code"), s.Get("sub"), s.Get("prov"), expressway ? Signs.RoadGuideSigns.ExpresswayPlateSize(h) : 1.5f * h);
            }

            GuideBlock n1 = PlaceName(s.Get("to1"), h, GuideColour.White);
            GuideBlock n2 = PlaceName(s.Get("to2"), h, GuideColour.White);
            bool two = n1.W > 0f && n2.W > 0f;
            bool hasNames = n1.W > 0f || n2.W > 0f;
            float namesW = two ? n1.W + 0.6f * h + n2.W : Math.Max(n1.W, n2.W);

            GuideArrow arrow = ArrowOf(s.Get("arrow"));
            GuideBlock distance = advance && s.Get("dist").Length > 0 ? Distance(s.Get("dist"), s.Get("unit"), h, GuideColour.White) : null;
            GuidePart numberPlate = s.Get("xnum").Length > 0 ? ExitNumberPlate(band.D, s.Get("xnum"), s.Get("xsuf"), s.Is("xside", "l"), 0f, 0f) : null;

            // The direction mark (a white square h, the character 0.8 h in the ground's green) 0.3 h to the right of the plate, 0.3 h under its top (E.215 a: 0.7 h under the inside's top): the
            // plate and the mark are centred as one group.
            bool mark = plate != null && dir.Length > 0;
            float groupW = plate != null ? plate.W + (mark ? 0.3f * h + h : 0f) : 0f;

            // The inside's width: the widest row and its margins; the exit number plate is not wider than the board it sits on.
            float innerW = 3.0f * h;
            if (numberPlate != null) innerW = Math.Max(innerW, numberPlate.W - 2f * edge);
            if (plate != null) innerW = Math.Max(innerW, groupW + 2f * m);
            // A through lane's board (E.215 a, Fig. 270): 6.2 h wide outside, 5.8 h inside, whatever the names; with an arrow over each of two lanes (E.215 b) 8.4 h outside, 8 h inside.
            if (lane) innerW = Math.Max(innerW, (twoArrows ? 8.0f : 5.8f) * h);
            if (bandName != null) innerW = Math.Max(innerW, CellWidth(bandName.Length, h, NameGapK * h) + 2f * m);
            if (hasNames) innerW = Math.Max(innerW, namesW + 2f * m);
            if (distance != null) innerW = Math.Max(innerW, m + distance.W + 0.8f * h + h + 0.6f * h);
            innerW = Math.Max(innerW, h + 2f * m);

            // From the inside's top: the road line, then the names, then the last row.
            float y = 0f;
            float plateY = 0f, bandH = 0f;
            if (plate != null)
            {
                plateY = m;
                y = m + plate.H + 0.4f * h;
            }
            else if (bandName != null)
            {
                bandH = 1.9f * h;
                y = bandH + 0.4f * h;
            }
            else
            {
                y = m;
            }

            float namesY = y;
            // 路48 (E.215 a): the yellow strip starts 0.3 h under the names and is 1.7 h high (0.3 h, the arrow 1 h, 0.4 h); the other boards have 0.5 h and a last row of 1 h.
            // The names are h high, more with an English line under them.
            float namesH = Math.Max(h, Math.Max(n1.H, n2.H));
            if (hasNames)
                y += namesH + (lane ? 0.3f : 0.5f) * h;
            float lastY = y;
            float innerH = lane ? lastY + 1.7f * h : lastY + h + m;

            GuidePart main = Part(h, innerW, innerH, GuideColour.Green);
            var board = new GuideBoard { H = h };
            board.Parts.Add(main);

            if (plate != null)
            {
                float gx = (innerW - groupW) * 0.5f;
                main.Add(plate, edge + gx, edge + plateY);
                if (mark)
                {
                    float mx = edge + gx + plate.W + 0.3f * h, my = edge + plateY + 0.3f * h;
                    main.Elements.Add(Rect(mx, my, h, h, GuideColour.White));
                    main.Elements.Add(Cells(dir.Substring(0, 1), mx + 0.1f * h, my + 0.1f * h, 0.8f * h, GuideColour.Green, 0f));
                }
            }

            if (bandName != null)
            {
                // The white band runs into the white border (bled over it), so that no hairline of ground colour is left between the two at the corners.
                GuideElement strip = Rect(edge, edge, innerW, bandH, GuideColour.White);
                strip.RadiusTop = main.InnerRadius;
                strip.Bleed = main.Border;
                main.Elements.Add(strip);
                float textW = CellWidth(bandName.Length, h, NameGapK * h);
                if (bandName.Length > 0)
                    main.Elements.Add(Cells(bandName, edge + (innerW - textW) * 0.5f, edge + (bandH - h) * 0.5f, h, GuideColour.Green, NameGapK * h));
            }

            if (hasNames)
            {
                float x0 = (innerW - namesW) * 0.5f;
                if (two)
                {
                    main.Add(n1, edge + x0, edge + namesY);
                    main.Add(n2, edge + x0 + n1.W + 0.6f * h, edge + namesY);
                }
                else
                {
                    GuideBlock only = n1.W > 0f ? n1 : n2;
                    main.Add(only, edge + (innerW - only.W) * 0.5f, edge + namesY);
                }
            }

            if (lane)
            {
                GuideElement strip = Rect(edge, edge + lastY, innerW, 1.7f * h, GuideColour.Yellow);
                strip.RadiusBottom = main.InnerRadius;
                main.Elements.Add(strip);
                if (twoArrows)
                {
                    // E.215 b: an arrow over the centre of each of two lanes, 0.4 h from the inside's edges (8.1.8).
                    main.Add(ArrowBlock(arrow, h, GuideColour.Black), edge + 0.4f * h, edge + lastY + 0.3f * h);
                    main.Add(ArrowBlock(arrow, h, GuideColour.Black), edge + innerW - 0.4f * h - h, edge + lastY + 0.3f * h);
                }
                else
                {
                    main.Add(ArrowBlock(arrow, h, GuideColour.Black), edge + (innerW - h) * 0.5f, edge + lastY + 0.3f * h);
                }
            }
            else if (distance != null)
            {
                main.Add(distance, edge + m, edge + lastY);
                main.Add(ArrowBlock(arrow, h, GuideColour.White), edge + innerW - 0.6f * h - h, edge + lastY);
            }
            else
            {
                main.Add(ArrowBlock(arrow, h, GuideColour.White), edge + (innerW - h) * 0.5f, edge + lastY);
            }

            AddExitNumberPlate(board, s, band.D, h);
            return board;
        }

        /// <summary>The exit number plate on top of a board, at the side of the exit (9.3.7: on the upper left or the upper right of the board according to the side of the exit).</summary>
        private static void AddExitNumberPlate(GuideBoard board, GuideSpec s, float D, float h)
        {
            string number = s.Get("xnum");
            if (number.Length == 0)
                return;
            bool left = s.Is("xside", "l");
            GuidePart plate = ExitNumberPlate(D, number, s.Get("xsuf"), left, 0f, 0f);
            GuidePart main = board.Main;
            plate.X = left ? 0f : main.W - plate.W;
            plate.Y = -plate.H - 0.05f * h;
            board.Parts.Add(plate);
        }

        private static GuideTemplate MakeExit(bool advance)
        {
            var fields = new List<GuideField>(RoadFields());
            fields.Add(GuideField.Text("to1", "地名一", "Place 1", "如皋", 6));
            fields.Add(GuideField.Text("to2", "地名二（可空）", "Place 2 (may be empty)", "海安", 6));
            if (advance)
            {
                fields.Add(GuideField.Text("dist", "距离", "Distance", "500", 4));
                fields.Add(GuideField.Choice("unit", "单位", "Unit", "m", kUnits, s => s.Get("dist").Length > 0));
            }
            else
            {
                fields.Add(GuideField.Flag("lane", "直出车道（黄底黑箭头）", "Through lane (yellow strip)", false));
                fields.Add(GuideField.Choice("lanes", "箭头数", "Arrows", "1", kLaneArrows, s => s.Flag("lane")));
                fields.Add(GuideField.Choice("dir", "方位格（路牌右侧）", "Direction mark (right of plate)", "", kMarkDirs, s => s.Get("road") != "name" && s.Get("road") != "none"));
            }

            fields.Add(GuideField.Choice("arrow", "箭头", "Arrow", "b", kArrows));
            fields.AddRange(ExitNumberFields("145", "r"));
            fields.Add(SpeedField());
            return new GuideTemplate
            {
                Id = advance ? "guide.exit.advance" : "guide.exit.direction",
                Code = advance ? "路42·44·46" : "路43·45·47·48",
                Zh = advance ? "出口预告标志" : "出口方向标志",
                En = advance ? "Exit advance board" : "Exit direction board",
                Fields = fields.ToArray(),
                Layout = s => ExitBoard(s, advance)
            };
        }

        // ------------------------------------------------------------------ the exit sign (路53)

        /// <summary>
        /// 路53 (Fig. E.220, in h): a vertical board 5.5 h high (6.5 h with 左 above 出口), 出口 in characters h, a white oval 1.5 h high with the number in digits h, and the 45 degree arrow
        /// 1 h, rows 0.4 h apart. About 4 h wide for three digits.
        /// </summary>
        private static GuideBoard ExitSignBoard(GuideSpec s)
        {
            Band band = BandOf(s.Get("speed"));
            float h = band.H;
            float m = MarginK * h, edge = EdgeK * h;
            bool left = s.Is("xside", "l");
            string number = s.Get("xnum"), suffix = s.Get("xsuf");
            // The oval is 1.5 h high and the digits are 1/1.6 of that (the proportion of Fig. E.207), 0.9375 h.
            float D = 1.5f * h / 1.6f;
            float suffixCap = D * 2f / 3f;
            float digitsW = LatinWidth(number, D) + (suffix.Length > 0 ? 0.05f * D + LatinWidth(suffix, suffixCap) : 0f);
            float ovalW = Math.Max(digitsW + 0.7f * D, 2.0f * h);
            GuideBlock chars = PlaceName("出口", h, GuideColour.White);
            float charsW = chars.W;
            float innerW = Math.Max(Math.Max(ovalW, charsW) + 2f * m, 3.6f * h);   // E.220: 4 h wide in all its examples

            float y = m;
            float leftRow = left ? h : 0f;
            float charsY = y + leftRow;
            float ovalY = charsY + chars.H + 0.4f * h;
            float arrowY = ovalY + 1.5f * h + 0.4f * h;
            float innerH = arrowY + h + m;

            GuidePart main = Part(h, innerW, innerH, GuideColour.Green);
            var board = new GuideBoard { H = h };
            board.Parts.Add(main);

            float charsX = edge + (innerW - charsW) * 0.5f;
            if (left)
                main.Elements.Add(Cells("左", charsX, edge + y, h, GuideColour.White, 0f));
            main.Add(chars, charsX, edge + charsY);

            float ox = edge + (innerW - ovalW) * 0.5f, oy = edge + ovalY;
            main.Elements.Add(Oval(ox, oy, ovalW, 1.5f * h, GuideColour.White));
            float dx = ox + (ovalW - digitsW) * 0.5f;
            float baseline = oy + (1.5f * h + D) * 0.5f;
            float numberW = LatinWidth(number, D);
            if (number.Length > 0)
                main.Elements.Add(Ink(number, dx, baseline - D, numberW, D, GuideColour.Green));
            if (suffix.Length > 0)
                main.Elements.Add(Ink(suffix, dx + numberW + 0.05f * D, baseline - suffixCap, LatinWidth(suffix, suffixCap), suffixCap, GuideColour.Green));

            main.Add(ArrowBlock(left ? GuideArrow.UpLeft : GuideArrow.UpRight, h, GuideColour.White), edge + (innerW - h) * 0.5f, edge + arrowY);
            return board;
        }

        // ------------------------------------------------------------------ the exit number plate on its own (路41)

        private static GuideBoard ExitNumberBoard(GuideSpec s)
        {
            Band band = BandOf(s.Get("speed"));
            GuidePart plate = ExitNumberPlate(band.D, s.Get("xnum"), s.Get("xsuf"), s.Is("xside", "l"), 0f, 0f);
            var board = new GuideBoard { H = plate.Liner * 10f };
            board.Parts.Add(plate);
            return board;
        }

        // ------------------------------------------------------------------ the next exit (路54)

        /// <summary>
        /// 路54 (Fig. 276, E.221): 下一出口 and the distance in km, one line (2.2 h high) or two (3.5 h high). The characters may be smaller than the digits, which keep their height (9.3.11):
        /// 0.7 h for the characters, h for the digits.
        /// </summary>
        private static GuideBoard NextExitBoard(GuideSpec s)
        {
            Band band = BandOf(s.Get("speed"));
            float h = band.H;
            float m = MarginK * h, edge = EdgeK * h;
            float hc = 0.7f * h;
            bool vertical = s.Is("form", "v");
            GuideBlock distance = Distance(s.Get("dist"), s.Get("unit"), h, GuideColour.White);
            float charsW = CellWidth(4, hc, 0.1f * h);

            float innerW, innerH;
            if (vertical)
            {
                innerW = Math.Max(charsW, distance.W) + 2f * m;
                innerH = m + hc + 0.6f * h + h + m;
            }
            else
            {
                innerW = m + charsW + 0.3f * h + distance.W + m;
                innerH = m + h + m;
            }

            GuidePart main = Part(h, innerW, innerH, GuideColour.Green);
            var board = new GuideBoard { H = h };
            board.Parts.Add(main);
            if (vertical)
            {
                main.Elements.Add(Cells("下一出口", edge + (innerW - charsW) * 0.5f, edge + m, hc, GuideColour.White, 0.1f * h));
                main.Add(distance, edge + (innerW - distance.W) * 0.5f, edge + m + hc + 0.6f * h);
            }
            else
            {
                // The characters sit on the digits' baseline.
                main.Elements.Add(Cells("下一出口", edge + m, edge + m + h - hc, hc, GuideColour.White, 0.1f * h));
                main.Add(distance, edge + m + charsW + 0.3f * h, edge + m);
            }

            return board;
        }

        // ------------------------------------------------------------------ the 300, 200 and 100 m boards (路52)

        /// <summary>
        /// 路52 (Fig. E.219 and 274): a green board 0.60 x 1.42 m, the number white and 0.15 m high with "m" under it, and 3, 2 or 1 white diagonal stripes in the lower part, sloping
        /// down to the right for a board on the left side of the road and up to the right for one on the right (Fig. 274 a and b).
        /// </summary>
        private static GuideBoard CountdownBoard(GuideSpec s)
        {
            const float W = 0.60f, H = 1.42f;
            string dist = s.Get("dist");
            int stripes = dist == "300" ? 3 : dist == "200" ? 2 : 1;
            bool leftSide = s.Is("side", "l");

            const float liner = 0.02f, border = 0.02f;
            var main = new GuidePart { W = W, H = H, Ground = GuideColour.Green, Liner = liner, Border = border, Radius = 0.08f };
            var board = new GuideBoard { H = 0.15f };
            board.Parts.Add(main);

            float edge = liner + border;
            float innerW = W - 2f * edge, innerH = H - 2f * edge;
            float cap = 0.15f;
            float numberW = LatinWidth(dist, cap);
            main.Elements.Add(Ink(dist, edge + (innerW - numberW) * 0.5f, edge + 0.10f, numberW, cap, GuideColour.White));
            float mH = 0.105f;
            main.Elements.Add(Ink("m", edge + (innerW - 0.13f) * 0.5f, edge + 0.10f + cap + 0.06f, 0.13f, mH, GuideColour.White, GuideAlign.Centre, "k"));

            // The stripes: bands of equal height at the middle of the board, a gap of the same height between them; the slope is 0.55.
            float regionTop = 0.50f, regionH = innerH - regionTop - 0.06f;
            float t = stripes == 3 ? regionH / 6f : stripes == 2 ? regionH / 4f : regionH / 3f;
            float slope = leftSide ? 0.55f : -0.55f;
            float midX = innerW * 0.5f;
            for (int i = 0; i < stripes; i++)
            {
                float a = regionTop + t * (1 + 2 * i);
                float b = a + t;
                // Heights at the left edge of the inside, so that the band passes through its middle height at the middle of the board.
                float shift = slope * midX;
                float[] band =
                {
                    0f, a - shift,
                    innerW, a - shift + slope * innerW,
                    innerW, b - shift + slope * innerW,
                    0f, b - shift
                };
                float[] clipped = ClipToBox(band, 0f, regionTop, innerW, regionTop + regionH);
                if (clipped.Length >= 6)
                {
                    var shifted = new float[clipped.Length];
                    for (int k = 0; k < clipped.Length; k += 2)
                    {
                        shifted[k] = clipped[k] + edge;
                        shifted[k + 1] = clipped[k + 1] + edge;
                    }

                    main.Elements.Add(Poly(shifted, GuideColour.White));
                }
            }

            return board;
        }

        // ------------------------------------------------------------------ the expressway plates (Appendix C)

        private static readonly GuideChoice[] kUses =
        {
            new GuideChoice("guide", "指路牌上的路线信息（表 C.1）", "Route info on a guide board (Table C.1)"),
            new GuideChoice("entry", "入口后命名编号牌（C.1.1.2.2）", "Naming plate after an entrance (C.1.1.2.2)"),
            new GuideChoice("start", "起点的命名编号标志（C.1.1.2.1）", "Naming plate at the start (C.1.1.2.1)")
        };

        // E.202: the run of the four characters of the name (4 x 20 + 3 x 5 cm) a longer name is fitted into, the gap of its characters (5 of 20), and the number's box and the gaps round it.
        private const float kEntryNameRun = 0.95f, kEntryNameGapK = 0.25f;
        private const float kEntryNumberBox = 0.45f, kEntryGapAbove = 0.08f, kEntryGapBetween = 0.07f, kEntryNameRow = 0.20f;

        /// <summary>
        /// Appendix C: the national expressway number plates, and the provincial ones (C.2: the same, the strip yellow with black X高速). Sizes in cm from the text: at the start 330 x 300 with
        /// 国家高速 50, number 70, name 60; after an interchange 125 x 120 (170 x 120 for four digits) with 10, 45 and 20 (the name 10 to 20 when it has more than four characters); on a
        /// guide board table C.1: 100 x 100, 125 x 100, 170 x 100 (70 x 70, 85 x 70, 115 x 70 at 80 km/h or less).
        /// </summary>
        private static GuideBoard ExpresswayPlateBoard(GuideSpec s)
        {
            string use = s.Get("use");
            bool national = s.Is("road", "ge");
            string code = s.Get("code"), sub = s.Get("sub"), name = s.Get("rname");
            int digits = 0;
            foreach (char c in code + sub)
            {
                if (c >= '0' && c <= '9')
                    digits++;
            }

            float W, H, liner, border, radius, stripText, number, nameH;
            float stripH;
            bool entry = use == "entry";
            if (use == "start")
            {
                W = 3.30f; H = 3.00f; stripText = 0.50f; number = 0.70f; nameH = 0.60f;
                liner = 0.06f; border = 0.06f; radius = 0.24f; stripH = 1.15f * stripText;
            }
            else if (entry)
            {
                // E.202 (Fig. 257): the edge is the liner 3 and the border 3; the strip 20 high (5 + 10 + 5) with characters of 10 cm 10 cm apart; the number's box 45 high, 8 below the
                // strip; the name's row 20 high, 7 below it, with characters of 20 cm 5 cm apart (the row keeps its height for the shorter characters of a long name); 8 above the edge.
                // The outer radius is not dimensioned: 11 cm is measured on the drawing (three concentric arcs of 11, 8 and 5 cm).
                W = digits >= 4 ? 1.70f : 1.25f; H = 1.20f; stripText = 0.10f; number = 0.45f;
                nameH = name.Length > 4 ? Math.Max(0.10f, Math.Min(0.20f, kEntryNameRun / ((1f + kEntryNameGapK) * name.Length - kEntryNameGapK))) : 0.20f;
                liner = 0.03f; border = 0.03f; radius = 0.11f; stripH = 0.20f;
            }
            else
            {
                bool fast = s.Get("speed") != "60";
                float P = fast ? 1.00f : 0.70f;
                stripText = fast ? 0.10f : 0.07f;
                number = 0.45f * P;   // E.201 b: 31.5 cm on the 70 cm plate, the figure of a scaled by 0.7
                float k = digits <= 1 ? 1.00f : digits == 2 ? 1.25f : digits == 3 ? 1.45f : 1.70f;
                W = (fast ? k : (digits <= 1 ? 0.70f : digits == 2 ? 0.85f : digits == 3 ? 1.0f : 1.15f)) * (fast ? P : 1f);
                // The table's width is for the usual codes; a longer one widens the plate.
                float contentW = LatinWidth(code, number) + (sub.Length > 0 ? 0.03f * number + LatinWidth(sub, number * 0.55f) : 0f);
                W = Math.Max(W, contentW + 0.22f * P);
                // E.201: one edge of 3 (no outer liner: the plate is put on a board), the strip 20 high, the number's box 14 under it; 70 cm high is the same scaled by 0.7. The 11 cm radius is
                // measured on the drawing.
                H = P; nameH = 0f; liner = 0f; border = 0.03f * P; radius = 0.11f * P; stripH = 2f * stripText;
            }

            float edge = liner + border;
            var main = new GuidePart { W = W, H = H, Ground = GuideColour.Green, Liner = liner, Border = border, Radius = radius };
            var board = new GuideBoard { H = number };
            board.Parts.Add(main);

            float innerW = W - 2f * edge;
            GuideElement strip = Rect(edge, edge, innerW, stripH, national ? GuideColour.Red : GuideColour.Yellow);
            strip.RadiusTop = main.InnerRadius;
            main.Elements.Add(strip);

            string stripString = national ? "国家高速" : s.Get("prov") + "高速";
            int chars = stripString.Length;
            float span = innerW * 0.78f;
            float gap = chars > 1 ? Math.Max(0.05f * stripText, (span - chars * stripText) / (chars - 1)) : 0f;
            if (use != "start" && chars > 1)
                gap = stripText;   // E.201 and E.202: the characters are 10 cm high and 10 cm apart
            float textW = CellWidth(chars, stripText, gap);
            main.Elements.Add(Cells(stripString, edge + (innerW - textW) * 0.5f, edge + (stripH - stripText) * 0.5f, stripText, national ? GuideColour.White : GuideColour.Black, gap));

            // Below the strip: the number, and the name when there is one, spaced evenly. A plate of a fixed size (entry, start) makes the number smaller when what is typed is too wide for it.
            float subCap = number * 0.55f;
            float codeW = LatinWidth(code, number), subW = LatinWidth(sub, subCap);
            float needed = codeW + (subW > 0f ? 0.03f * number + subW : 0f);
            float available = entry ? innerW - 0.06f : innerW * 0.92f;   // E.202: the number's box is 107 of the 113 cm inside the edge
            if (needed > available)
            {
                float shrink = available / needed;
                number *= shrink;
                subCap = number * 0.55f;
                codeW = LatinWidth(code, number);
                subW = LatinWidth(sub, subCap);
            }

            float areaTop = edge + stripH, areaBottom = H - edge;
            bool hasName = name.Length > 0 && nameH > 0f;
            float free = areaBottom - areaTop - number - (hasName ? nameH : 0f);
            int gaps = hasName ? 3 : 2;
            float g = free / gaps;
            float numberY = areaTop + g;
            float nameY = numberY + number + g;
            if (entry && hasName)
            {
                // The figure's fixed gaps, not an even spread: the number (a smaller one when what is typed is wide) is centred in its box and the name in its row.
                float boxTop = areaTop + kEntryGapAbove;
                numberY = boxTop + (kEntryNumberBox - number) * 0.5f;
                nameY = boxTop + kEntryNumberBox + kEntryGapBetween + (kEntryNameRow - nameH) * 0.5f;
            }
            else if (use == "guide")
                numberY = areaTop + 0.14f * H;   // E.201: 14 under the strip (15 above the edge), 45 high at 100 cm
            float total = codeW + (subW > 0f ? 0.03f * number + subW : 0f);
            float nx = edge + (innerW - total) * 0.5f;
            main.Elements.Add(Ink(code, nx, numberY, codeW, number, GuideColour.White));
            if (subW > 0f)
                main.Elements.Add(Ink(sub, nx + codeW + 0.03f * number, numberY + number - subCap, subW, subCap, GuideColour.White));

            if (hasName)
            {
                float ngap = Math.Max(0.1f * nameH, Math.Min((entry ? kEntryNameGapK : NameGapK) * nameH, (innerW - name.Length * nameH) / Math.Max(1, name.Length - 1)));
                float nameW = CellWidth(name.Length, nameH, ngap);
                main.Elements.Add(Cells(name, edge + (innerW - nameW) * 0.5f, nameY, nameH, GuideColour.White, ngap));
            }

            return board;
        }

        // ------------------------------------------------------------------ the catalogue

        private static GuideTemplate MakeRoadPlate() => new GuideTemplate
        {
            Id = "guide.road.plate",
            Code = "附录C",
            Zh = "高速公路命名编号标志",
            En = "Expressway number plate",
            Fields = new[]
            {
                GuideField.Choice("road", "类别", "Class", "ge", new[] { new GuideChoice("ge", "国家高速", "National expressway"), new GuideChoice("se", "省级高速", "Provincial expressway") }),
                GuideField.Text("code", "编号", "Number", "G15", 5),
                GuideField.Text("sub", "环线序号（右下小字）", "Ring ID (small, low)", "", 2),
                GuideField.Text("prov", "省简称", "Province", "沪", 2, s => s.Is("road", "se")),
                GuideField.Text("rname", "路名（可空）", "Road name (may be empty)", "", 8, s => !s.Is("use", "guide")),
                GuideField.Choice("use", "用途", "Use", "guide", kUses),
                GuideField.Choice("speed", "设计速度", "Design speed", "100", new[] { kSpeeds[0], new GuideChoice("60", "80 km/h 及以下", "80 km/h or less") }, s => s.Is("use", "guide"))
            },
            Layout = ExpresswayPlateBoard
        };

        private static readonly GuideTemplate s_ExitAdvance = MakeExit(true);
        private static readonly GuideTemplate s_ExitDirection = MakeExit(false);
        private static readonly GuideTemplate s_ExitSign = new GuideTemplate
        {
            Id = "guide.exit.sign",
            Code = "路53",
            Zh = "出口标志",
            En = "Exit sign",
            Fields = new List<GuideField>(ExitNumberFields("321", "r")) { SpeedField() }.ToArray(),
            Layout = ExitSignBoard
        };

        private static readonly GuideTemplate s_ExitNumber = new GuideTemplate
        {
            Id = "guide.exit.number",
            Code = "路41",
            Zh = "出口编号标志",
            En = "Exit number plate",
            Fields = new List<GuideField>(ExitNumberFields("119", "r")) { SpeedField() }.ToArray(),
            Layout = ExitNumberBoard
        };

        private static readonly GuideTemplate s_ExitNext = new GuideTemplate
        {
            Id = "guide.exit.next",
            Code = "路54",
            Zh = "下一出口预告标志",
            En = "Next exit board",
            Fields = new[]
            {
                GuideField.Choice("form", "形式", "Form", "h", new[] { new GuideChoice("h", "横排（一行）", "One line"), new GuideChoice("v", "竖排（两行）", "Two lines") }),
                GuideField.Text("dist", "距离", "Distance", "15", 4),
                GuideField.Choice("unit", "单位", "Unit", "km", kUnits),
                SpeedField()
            },
            Layout = NextExitBoard
        };

        private static readonly GuideTemplate s_Countdown = new GuideTemplate
        {
            Id = "guide.countdown",
            Code = "路52",
            Zh = "出口预告倒计数标志",
            En = "Exit countdown board",
            Fields = new[]
            {
                GuideField.Choice("dist", "距离", "Distance", "300", new[] { new GuideChoice("300", "300 m", "300 m"), new GuideChoice("200", "200 m", "200 m"), new GuideChoice("100", "100 m", "100 m") }),
                GuideField.Choice("side", "设置位置", "Side of the road", "r", new[] { new GuideChoice("l", "设置在左侧（斜条向右下）", "On the left (stripes slope down right)"), new GuideChoice("r", "设置在右侧（斜条向右上）", "On the right (stripes slope up right)") })
            },
            Layout = CountdownBoard
        };

        private static readonly GuideTemplate s_RoadPlate = MakeRoadPlate();

        private static readonly GuideTemplate[] s_All = BuildAll();

        private static GuideTemplate[] BuildAll()
        {
            // The guide boards first, then the fixed signs of the standard in the order of its chapters: prohibition (5), mandatory (6), warning (7), auxiliary (12).
            var all = new List<GuideTemplate> { s_ExitAdvance, s_ExitDirection, s_ExitSign, s_ExitNumber, s_ExitNext, s_Countdown, s_RoadPlate };
            var fixedSigns = new List<GuideTemplate>();
            fixedSigns.AddRange(Signs.ProhibitionSigns.All());
            fixedSigns.AddRange(Signs.MandatorySigns.All());
            fixedSigns.AddRange(Signs.WarningSigns.All());
            fixedSigns.AddRange(Signs.AuxiliarySigns.All());
            fixedSigns.AddRange(Signs.NoticeSigns.All());
            fixedSigns.AddRange(Signs.RoadGuideSigns.All());
            fixedSigns.AddRange(Signs.ExpresswayEntrySigns.All());
            fixedSigns.AddRange(Signs.ExpresswayNameSigns.All());
            fixedSigns.AddRange(Signs.ExpresswayDistanceSigns.All());
            fixedSigns.AddRange(Signs.ExpresswayForkSigns.All());
            fixedSigns.AddRange(Signs.ExpresswayHubSigns.All());
            fixedSigns.AddRange(Signs.VariableSigns.All());
            fixedSigns.AddRange(Signs.BrtSigns.All());
            fixedSigns.AddRange(Signs.TollSigns.All());
            fixedSigns.AddRange(Signs.Cabinets.All());
            fixedSigns.AddRange(Signs.LampAds.All());
            fixedSigns.AddRange(Signs.OtherSigns.All());
            // The place-name signs of GB 17733-2008 (group "placename"): street, lane, door and building plates, the names of areas, facilities and features.
            fixedSigns.AddRange(Signs.PlaceNameSigns.All());
            fixedSigns.AddRange(Signs.WayfindingSigns.All());
            // The player's own signs from pictures (0.12.29): one template; what the player saved are its entries in the catalogue (SignCatalog).
            fixedSigns.AddRange(Signs.CustomSigns.All());
            // The text light box (0.12.75): channel letters made from typed text, in the same tab; listed unless features.textBox is off.
            fixedSigns.AddRange(Signs.TextBoxes.All());
            // The standing light box (0.12.76): a lit box on posts on the ground, tilted back, with its triangular frames; listed unless features.standBox is off.
            fixedSigns.AddRange(Signs.StandBoxes.All());
            // The speed limits by vehicle type: the green board of a gantry with the max and min round signs, in the guide tab.
            fixedSigns.AddRange(Signs.TypeSpeedSigns.All());
            // The road markings of GB 5768.3 (group "marking"): templates like the signs, made into shapes on the road and not into boards.
            fixedSigns.AddRange(MarkingTemplates.All);
            all.InsertRange(0, fixedSigns);
            Bilingual.OptIn(all);
            SignLighting.OptIn(all);
            return all.ToArray();
        }

        public static IReadOnlyList<GuideTemplate> All => s_All;

        public static GuideTemplate ById(string id)
        {
            foreach (GuideTemplate t in s_All)
            {
                if (t.Id == id)
                    return t;
            }

            return null;
        }

        public static GuideTemplate ByHash(uint hash)
        {
            foreach (GuideTemplate t in s_All)
            {
                if (t.Hash == hash)
                    return t;
            }

            return null;
        }

        /// <summary>The boards the panel offers: the figures of the standard, as templates with their fields set; the BRT series' markings with its words (0.12.22) and the kinds of electric box (0.12.23).</summary>
        public static readonly GuidePreset[] Presets = WithSeries(new[]
        {
            P("gb.exit.adv.2km", "guide.exit.advance", "路42", "2 km 出口预告（有编号道路）", "Exit advance 2 km (numbered road)", "ckyg", "road=g;code=G204;to1=如皋;to2=海安;dist=2;unit=km;arrow=b;xnum=145", "出口", "预告", "2km", "国道"),
            P("gb.exit.adv.1km", "guide.exit.advance", "路42", "1 km 出口预告（有编号道路）", "Exit advance 1 km (numbered road)", "ckyg", "road=g;code=G204;to1=如皋;to2=海安;dist=1;unit=km;arrow=b;xnum=145", "出口", "预告", "1km"),
            P("gb.exit.adv.500", "guide.exit.advance", "路42", "500 m 出口预告（有编号道路）", "Exit advance 500 m (numbered road)", "ckyg", "road=g;code=G204;to1=如皋;to2=海安;dist=500;unit=m;arrow=b;xnum=145", "出口", "预告", "500"),
            P("gb.exit.dir", "guide.exit.direction", "路43", "出口方向（有编号道路）", "Exit direction (numbered road)", "ckfx", "road=g;code=G204;to1=如皋;to2=海安;arrow=b;xnum=145", "出口", "方向"),
            P("gb.exit.adv.name", "guide.exit.advance", "路44", "2 km 出口预告（无编号·左出口）", "Exit advance 2 km (unnumbered, left exit)", "ckyg", "road=name;rname=前进路;to1=广州;to2=番禺;dist=2;unit=km;arrow=d;xnum=32;xside=l", "出口", "预告", "无编号", "左出口"),
            P("gb.exit.dir.name", "guide.exit.direction", "路45", "出口方向（无编号·左出口）", "Exit direction (unnumbered, left exit)", "ckfx", "road=name;rname=前进路;to1=广州;to2=番禺;arrow=d;xnum=32;xside=l", "出口", "方向", "无编号", "左出口"),
            P("gb.exit.hub.3km", "guide.exit.advance", "路46", "3 km 枢纽互通出口预告", "Interchange exit advance 3 km", "ckyg", "road=ge;code=G15;sub=11;to1=日照;to2=济宁;dist=3;unit=km;arrow=b;xnum=50", "出口", "预告", "枢纽", "互通", "高速"),
            P("gb.exit.hub.500", "guide.exit.advance", "路46", "500 m 枢纽互通出口预告", "Interchange exit advance 500 m", "ckyg", "road=ge;code=G15;sub=11;to1=日照;to2=济宁;dist=500;unit=m;arrow=b;xnum=50", "出口", "预告", "枢纽", "互通", "高速"),
            P("gb.exit.hub.dir", "guide.exit.direction", "路47", "枢纽互通出口方向", "Interchange exit direction", "ckfx", "road=ge;code=G15;sub=11;to1=日照;to2=济宁;arrow=b;xnum=50", "出口", "方向", "枢纽", "互通", "高速"),
            P("gb.exit.through", "guide.exit.direction", "路48", "直出车道出口方向", "Through lane exit direction", "zccd", "road=ge;code=G55;dir=南;to1=广州;to2=;arrow=b;lane=1;xnum=133;xsuf=A", "出口", "直出", "车道", "黄底"),
            P("gb.exit.through.down", "guide.exit.direction", "路48", "直出车道出口方向（箭头向下，路上方）", "Through lane exit direction (arrow down, overhead)", "zccd", "road=ge;code=G55;dir=北;to1=永州;to2=;arrow=f;lane=1;xnum=133;xsuf=B", "出口", "直出", "车道", "黄底", "向下"),
            P("gb.exit.through.2", "guide.exit.direction", "路48", "直出车道出口方向（两个箭头）", "Through lane exit direction (two arrows)", "zccd", "road=se;code=S51;prov=粤;to1=阳江;to2=;arrow=b;lane=1;lanes=2;xnum=235", "出口", "直出", "车道", "黄底", "两个箭头"),
            P("gb.exit.sign", "guide.exit.sign", "路53", "出口标志（右）", "Exit sign (right)", "ckbz", "xnum=321;xside=r", "出口", "标志"),
            P("gb.exit.sign.left", "guide.exit.sign", "路53", "出口标志（左出口）", "Exit sign (left)", "ckbz", "xnum=101;xside=l", "出口", "标志", "左出口"),
            P("gb.exit.next.h", "guide.exit.next", "路54", "下一出口预告（一行）", "Next exit (one line)", "xykyg", "form=h;dist=15;unit=km", "下一出口", "预告"),
            P("gb.exit.next.v", "guide.exit.next", "路54", "下一出口预告（两行）", "Next exit (two lines)", "xykyg", "form=v;dist=15;unit=km", "下一出口", "预告"),
            P("gb.exit.num", "guide.exit.number", "路41", "出口编号 119", "Exit number 119", "ckbh", "xnum=119", "出口", "编号"),
            P("gb.exit.num.ab", "guide.exit.number", "路41", "出口编号 180A-B", "Exit number 180A-B", "ckbh", "xnum=180;xsuf=A-B", "出口", "编号"),
            P("gb.exit.num.left", "guide.exit.number", "路41", "左出口编号 272", "Left exit number 272", "ckbh", "xnum=272;xside=l", "出口", "编号", "左出口"),
            P("gb.count.300", "guide.countdown", "路52", "300 m 出口预告", "Exit 300 m", "djs", "dist=300;side=r", "出口", "倒计数", "300"),
            P("gb.count.200", "guide.countdown", "路52", "200 m 出口预告", "Exit 200 m", "djs", "dist=200;side=r", "出口", "倒计数", "200"),
            P("gb.count.100", "guide.countdown", "路52", "100 m 出口预告", "Exit 100 m", "djs", "dist=100;side=r", "出口", "倒计数", "100"),
            P("gb.plate.ge", "guide.road.plate", "附录C", "国家高速编号牌（指路用）", "National expressway plate (route info)", "gjgs", "road=ge;code=G15;use=guide", "国家高速", "编号", "G15"),
            P("gb.plate.ge4", "guide.road.plate", "附录C", "国家高速环线编号牌 G4001", "National expressway ring plate G4001", "gjgs", "road=ge;code=G40;sub=01;use=guide", "国家高速", "环线", "G4001"),
            P("gb.plate.start", "guide.road.plate", "附录C", "国家高速起点命名编号牌", "National expressway naming plate (start)", "gjgs", "road=ge;code=G2;rname=京沪高速;use=start", "国家高速", "起点", "京沪"),
            P("gb.plate.entry", "guide.road.plate", "附录C", "国家高速入口后命名编号牌", "National expressway naming plate (after entrance)", "gjgs", "road=ge;code=G3;rname=京台高速;use=entry", "国家高速", "入口", "京台"),
            P("gb.plate.se", "guide.road.plate", "附录C", "省级高速编号牌（指路用）", "Provincial expressway plate (route info)", "sjgs", "road=se;code=S1;prov=沪;use=guide", "省级高速", "省高速", "编号")
        });

        private static GuidePreset[] WithSeries(GuidePreset[] standard)
        {
            var all = new List<GuidePreset>(standard);
            all.AddRange(Signs.BrtSigns.Presets());
            all.AddRange(Signs.TollSigns.Presets());
            all.AddRange(Signs.Cabinets.Presets());
            all.AddRange(Signs.LampAds.Presets());
            return all.ToArray();
        }

        private static GuidePreset P(string id, string template, string code, string zh, string en, string pinyin, string overrides, params string[] keywords) =>
            new GuidePreset { Id = id, Template = template, Code = code, Zh = zh, En = en, Pinyin = pinyin, Overrides = overrides, Keywords = keywords };

        public static GuidePreset PresetById(string id)
        {
            foreach (GuidePreset p in Presets)
            {
                if (p.Id == id)
                    return p;
            }

            return null;
        }

        /// <summary>The spec of a preset: the template's defaults with the preset's fields set.</summary>
        public static GuideSpec SpecOf(GuidePreset preset)
        {
            GuideTemplate template = ById(preset.Template);
            GuideSpec spec = template.Default();
            foreach (string pair in preset.Overrides.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0)
                    spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return spec;
        }
    }
}
