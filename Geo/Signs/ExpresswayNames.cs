using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;
using static CDLC.TS.Geo.Signs.RoadGuideSigns;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// Signs of the expressway guide chapter of GB 5768.2-2022 (shared by the expressway and the urban expressway): 路33 to 路37 (9.3.2, 9.3.4, 9.3.5). The place and direction boards at a
    /// fork (路33, 路34 with the expressway's number, 路35 with the number and the geographic direction; Fig. 254, 255, E.198 to E.200), each for the left or the right branch; the naming
    /// number (路36, Fig. 257, 258, E.202), which is the naming plate of Appendix C set after an entrance (C.1.1.2.2) and is drawn by that layout, with the direction plate above it
    /// (Fig. 258), and the place direction plate that hangs under it on the same post (Fig. 258 b); the road name of an urban expressway (路37, Fig. 259, E.203). Boards with text are
    /// laid out in the character height h of Table 3; edge (liner and border) 0.2 h and corner radius 0.4 h as Fig. 3.
    /// </summary>
    public static class ExpresswayNameSigns
    {
        private const string G = "guide";
        private const GuideColour Green = GuideColour.Green, White = GuideColour.White, Black = GuideColour.Black;

        // ------------------------------------------------------------------ 路33, 路34, 路35 地点、方向标志 Fig. 254, 255; E.198, E.199, E.200

        private static readonly GuideChoice[] kSides =
        {
            new GuideChoice("l", "左侧方向（↖，箭头在左）", "Left branch (↖, arrow at the left)"),
            new GuideChoice("r", "右侧方向（↗，箭头在右）", "Right branch (↗, arrow at the right)")
        };

        private static readonly GuideChoice[] kDirs =
        {
            new GuideChoice("北", "北", "North"),
            new GuideChoice("南", "南", "South"),
            new GuideChoice("东", "东", "East"),
            new GuideChoice("西", "西", "West")
        };

        /// <summary>The 45 degree arrow's box (E.198 to E.200): 1.1 h wide and 1.2 h high (1.25 h in E.199 b), 0.4 h from the inside's edge and 0.5 h from the text.</summary>
        private const float ArrowW = 1.1f, ArrowH = 1.2f, ArrowGap = 0.5f;

        /// <summary>The margin of Table 6 and the drawings: 0.4 h from the inside's edge.</summary>
        private const float Margin = 0.4f;

        /// <summary>The direction mark of 路35 (E.200): a white square 1 h on a side, 0.4 h beside the plate and level with its top. The character in it is 0.8 h, in the green of the ground (read off Fig. 254 c, estimated).</summary>
        private const float MarkK = 1.0f, MarkCharK = 0.8f, MarkGap = 0.4f;

        /// <summary>
        /// 路33 to 路35 (Fig. 254, E.198 to E.200), the board of one branch: the place name h high (0.2 h apart) and the 45 degree arrow of E.166 in its box 1.1 h x 1.2 h, 0.5 h from the name, on
        /// the side the branch goes (8.1.7: ↖ at the left of the text, ↗ at its right). 路33 (E.198): the board 2.5 h high, the name and the arrow in the middle of its height.
        /// 路34 (E.199): the expressway's number plate of Appendix C (100 cm high, 70 at 80 km/h and below, table C.1) 0.4 h from the top and 0.5 h above the name, centred over it as in
        /// Fig. 254 b; the name 0.4 h from the bottom; the arrow's box 0.1 h above the name's top. 路35 (E.200): also the direction mark 0.4 h beside the plate on the arrow's side, level
        /// with the plate's top; the arrow's box lower, its bottom 0.4 h above the name's bottom. A number emptied leaves 路33 (路35: the mark over the name). With two characters the
        /// board is 5 h wide as in the drawings; a longer name or a wider plate (four digits) widens it.
        /// </summary>
        private static GuideBoard Fork(GuideSpec s, int band, bool numbered, bool marked)
        {
            float h = CharHeight(band);
            bool right = s.Is("side", "r");
            GuideBlock name = Line(s.Get("to"), h, White, NameGapK * h);
            if (name.W <= 0f)
                name = new GuideBlock(CellWidth(2, h, NameGapK * h), h);
            GuideBlock plate = numbered ? Info(s.Get("code"), h, Green) : null;
            if (plate != null && plate.W <= 0f)
                plate = null;
            string mark = marked ? s.Get("dir") : "";
            float markS = MarkK * h;

            float topH = plate != null ? plate.H : mark.Length > 0 ? markS : 0f;
            float content = Math.Max(name.W, plate != null ? plate.W : 0f);
            float aw = ArrowW * h, ah = ArrowH * h;
            float innerW = Margin * h + aw + ArrowGap * h + content + Margin * h;
            // E.198: 2.5 h high, the inside 2.1 h; with a plate (E.199, E.200) 0.4 h, the plate, 0.5 h, the name, 0.4 h.
            float innerH = topH > 0f ? Margin * h + topH + 0.5f * h + h + Margin * h : 2.1f * h;
            GuidePart p = Board(h, innerW, innerH, Green);
            float e = p.Edge;

            float cx = right ? e + Margin * h : e + Margin * h + aw + ArrowGap * h;
            float ax = right ? e + innerW - Margin * h - aw : e + Margin * h;
            float nameY = topH > 0f ? e + Margin * h + topH + 0.5f * h : e + (innerH - h) * 0.5f;
            p.Add(name, cx + (content - name.W) * 0.5f, nameY);

            float ay;
            if (topH <= 0f)
                ay = e + (innerH - ah) * 0.5f;
            else if (mark.Length > 0)
                // Never higher than 0.1 h under the mark (the two share the arrow's side when a name is typed for the number: a line h high instead of a plate).
                ay = Math.Max(nameY + h - Margin * h - ah, e + Margin * h + markS + 0.1f * h);
            else
                ay = nameY - 0.1f * h - ah;
            p.Elements.Add(ArrowIn(right ? GuideArrow.UpRight : GuideArrow.UpLeft, ax, ay, aw, ah, White));

            float topY = e + Margin * h;
            if (plate != null)
            {
                float px = cx + (content - plate.W) * 0.5f;
                p.Add(plate, px, topY + (topH - plate.H) * 0.5f);
                if (mark.Length > 0)
                    AddMark(p, mark, right ? px + plate.W + MarkGap * h : px - MarkGap * h - markS, topY, h);
            }
            else if (mark.Length > 0)
            {
                AddMark(p, mark, cx + (content - markS) * 0.5f, topY, h);
            }

            return Single(p, h);
        }

        /// <summary>The direction mark of 路35: a white square with the character in the ground's green (Fig. 254 c).</summary>
        private static void AddMark(GuidePart p, string mark, float x, float y, float h)
        {
            float side = MarkK * h, c = MarkCharK * h;
            p.Elements.Add(Rect(x, y, side, side, White));
            p.Elements.Add(Cells(mark.Substring(0, 1), x + (side - c) * 0.5f, y + (side - c) * 0.5f, c, Green, 0f));
        }

        // ------------------------------------------------------------------ 路36 命名编号标志 Fig. 257, 258; E.202; Appendix C.1.1.2.2

        private static readonly GuideChoice[] kRoads =
        {
            new GuideChoice("ge", "国家高速（红底“国家高速”）", "National expressway (red strip)"),
            new GuideChoice("se", "省级高速（黄底“X高速”）", "Provincial expressway (yellow strip)")
        };

        private static readonly GuideChoice[] kOptDirs =
        {
            new GuideChoice("", "不设", "None"),
            kDirs[0], kDirs[1], kDirs[2], kDirs[3]
        };

        /// <summary>The space between the direction plate and the naming plate on the post in Fig. 258: 11 to 13 cm (read off the figure).</summary>
        private const float PlateGap = 0.12f;

        /// <summary>
        /// The naming plate after an entrance (C.1.1.2.2: 125 x 120 cm, 170 x 120 for a number of four digits; 国家高速 or X高速 10 cm, the number 45, the short name 20, 10 to 20 for more
        /// than four characters): the layout of Appendix C (GuideTemplates' "guide.road.plate", use "entry", as the presets gb.plate.entry) with this sign's fields.
        /// </summary>
        private static GuideBoard NamingPlate(GuideSpec s)
        {
            GuideTemplate plates = GuideTemplates.ById("guide.road.plate") ?? throw new InvalidOperationException("the plates of Appendix C are missing");
            GuideSpec ps = plates.Default()
                .With("road", s.Get("road"))
                .With("code", s.Get("code"))
                .With("sub", s.Get("sub"))
                .With("prov", s.Get("prov"))
                .With("rname", s.Get("rname"))
                .With("use", "entry");
            return plates.Layout(ps);
        }

        /// <summary>
        /// The geographic direction plate above the naming plate (Fig. 258 a to c): green with a white border, the character in white. No drawing gives it; read off Fig. 258: 80 x 64 cm,
        /// the character 32 cm; liner and border 3 cm each as E.202 gives for the naming plate.
        /// </summary>
        private static GuidePart DirectionPlate(string dir)
        {
            const float w = 0.80f, hh = 0.64f, c = 0.32f;
            var p = new GuidePart { W = w, H = hh, Ground = Green, BorderColour = White, Liner = 0.03f, Border = 0.03f, Radius = 0.06f };
            SetSafe(p);
            p.Elements.Add(Cells(dir.Substring(0, 1), (w - c) * 0.5f, (hh - c) * 0.5f, c, White, 0f));
            return p;
        }

        /// <summary>
        /// 路36 (9.3.4, Fig. 257, 258): the naming plate, with the direction plate centred over it (Fig. 258 a; also the top of b and c) as a plate fixed on top of the board. The rest of
        /// Fig. 258 is several boards on one post: the place direction plate (gb.guide9.36b) under it (b), a second naming plate under it on a section two expressways share (c).
        /// </summary>
        private static GuideBoard Naming(GuideSpec s)
        {
            GuideBoard board = NamingPlate(s);
            string dir = s.Get("dir");
            if (dir.Length > 0)
            {
                GuidePart mark = DirectionPlate(dir);
                mark.X = (board.Main.W - mark.W) * 0.5f;
                mark.Y = -PlateGap - mark.H;
                board.Parts.Add(mark);
            }

            return board;
        }

        /// <summary>
        /// The place direction plate under the naming plate (Fig. 258 b, 西安方向): white with a black border and black characters 20 cm high (the short name's height of C.1.1.2.2), 4 cm
        /// apart. No drawing gives it; read off the figure: 37 cm high, 107 cm wide for four characters (the text 5 cm inside the border), a black border 2 cm in a white liner of 1 cm.
        /// </summary>
        private static GuideBoard TowardPlate(GuideSpec s)
        {
            const float hh = 0.37f, ch = 0.20f, liner = 0.01f, border = 0.02f, pad = 0.05f;
            GuideBlock line = Line(s.Get("toward"), ch, Black, NameGapK * ch);
            float edge = liner + border;
            float w = Math.Max(CellWidth(2, ch, NameGapK * ch), line.W) + 2f * (edge + pad);
            var p = new GuidePart { W = w, H = hh, Ground = White, BorderColour = Black, Liner = liner, Border = border, Radius = 0.05f };
            SetSafe(p);
            p.Add(line, (w - line.W) * 0.5f, (hh - line.H) * 0.5f);
            return Single(p, ch);
        }

        // ------------------------------------------------------------------ 路37 路名标志 Fig. 259; E.203

        /// <summary>路37 (Fig. 259, E.203): the name of the urban expressway in characters h high, 0.2 h apart, 0.4 h from the inside's edges: 4.6 h x 2.2 h for three characters.</summary>
        private static GuideBoard RoadName(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            GuideBlock line = Line(s.Get("name"), h, White, NameGapK * h);
            float innerW = Math.Max(line.W, h) + 2f * Margin * h;
            GuidePart p = Board(h, innerW, h + 2f * Margin * h, Green);
            p.Add(line, p.Edge + (innerW - line.W) * 0.5f, p.Edge + Margin * h);
            return Single(p, h);
        }

        // ================================================================== the catalogue

        private static string[] W(params string[] words) => words;

        private static GuideField Code() => GuideField.Text("code", "高速公路编号", "Expressway number", "G3", 6);

        public static IEnumerable<GuideTemplate> All()
        {
            // 9.3.2: at the fork of the ramp, one board for each branch (Fig. 254), also over the road (Fig. 255).
            yield return StandardSigns.Make("gb.guide9.33", "路33", "地点、方向", "Place and direction (fork)", G, "ddfx", W("地点", "方向", "分岔", "匝道", "fork"),
                (s, band) => Fork(s, band, false, false), new[]
                {
                    Place("to", "地点", "Place", "合肥"),
                    GuideField.Choice("side", "行驶方向", "Branch", "l", kSides)
                });

            yield return StandardSigns.Make("gb.guide9.34", "路34", "带编号信息的地点、方向", "Place and direction with the number (fork)", G, "dbhxxdddfx", W("地点", "方向", "分岔", "编号", "高速", "fork"),
                (s, band) => Fork(s, band, true, false), new[]
                {
                    Code(),
                    Place("to", "地点", "Place", "天津"),
                    GuideField.Choice("side", "行驶方向", "Branch", "l", kSides)
                });

            yield return StandardSigns.Make("gb.guide9.35", "路35", "带编号、方向信息的地点、方向", "Place and direction with the number and the direction (fork)", G, "dbhfxxxdddfx", W("地点", "方向", "分岔", "编号", "东南西北", "fork"),
                (s, band) => Fork(s, band, true, true), new[]
                {
                    Code(),
                    GuideField.Choice("dir", "地理方位", "Direction", "北", kDirs),
                    Place("to", "地点", "Place", "天津"),
                    GuideField.Choice("side", "行驶方向", "Branch", "l", kSides)
                });

            // 9.3.4: the naming number, and the place direction plate of Fig. 258 b that hangs under it.
            yield return StandardSigns.Make("gb.guide9.36", "路36", "命名编号", "Expressway naming number", G, "mmbh", W("命名编号", "高速", "国家高速", "编号", "京沪"),
                (s, band) => Naming(s), new[]
                {
                    GuideField.Choice("road", "类别", "Class", "ge", kRoads),
                    GuideField.Text("code", "编号", "Number", "G2", 5),
                    GuideField.Text("sub", "环线序号（右下小字）", "Ring ID (small, low)", "", 2),
                    GuideField.Text("prov", "省简称", "Province", "皖", 2, sp => sp.Is("road", "se")),
                    GuideField.Text("rname", "中文简称", "Short name", "京沪高速", 8),
                    GuideField.Choice("dir", "地理方位（上方方位牌）", "Direction (plate above)", "", kOptDirs)
                });

            yield return StandardSigns.Make("gb.guide9.36b", "路36", "命名编号下方的地点方向", "Place direction under the naming number", G, "mmbhxfdddfx", W("命名编号", "方向", "地点方向", "白牌"),
                (s, band) => TowardPlate(s), new[] { GuideField.Text("toward", "地点方向", "Place direction", "西安方向", 7) });

            // 9.3.5: the name of an urban expressway.
            yield return StandardSigns.Make("gb.guide9.37", "路37", "路名（城市快速路）", "Road name (urban expressway)", G, "lm", W("路名", "快速路", "环路", "urban expressway"),
                RoadName, new[] { GuideField.Text("name", "路名", "Name", "北四环", 8) });
        }
    }
}
