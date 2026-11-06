using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.Signs.AuxiliarySigns;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The BRT series (0.12.22; the owner: "加入BRT系列道路标识"): the catalogue's BRT tab lists the standard's BRT signs and markings (示28, 辅1 and 辅2 to go with it, 线59 and its words, the bays and
    /// stops with 快速公交; Series) and the boards Chinese cities put up along BRT corridors that the standard does not have: the way to a station, no other vehicles in the BRT lane, where the BRT
    /// lane starts and ends, and the enforcement notice. Those are this mod's design after the usual practice, marked 非国标 in the catalogue: blue boards (the colour of 指示 and of 示28) with white
    /// characters and the bus of 示28, the prohibition and the notice as white boards (告示, 11), characters by the road's speed band (Table 3) as the notices' are.
    /// </summary>
    public static class BrtSigns
    {
        public const string Group = "brt";

        /// <summary>What the series lists besides its own boards: the standard's signs and markings for a BRT lane, and the presets of the markings with 快速公交.</summary>
        public static readonly HashSet<string> Series = new HashSet<string>
        {
            "gb.mand.28", "gb.aux.01", "gb.aux.02", "mk.line.reserved", "brt.mk.text", "brt.mk.text.time", "brt.mk.busbay", "brt.mk.kerbstop"
        };

        /// <summary>The series an entry of the catalogue is also listed in ("" for none).</summary>
        public static string SeriesOf(string id, string group) => group == Group || (id != null && Series.Contains(id)) ? Group : "";

        /// <summary>The presets of the standard's markings with the words of a BRT lane (6.9.1: 快速公交, and the times of a lane reserved at times; 5.11.5: the bay or the stop of BRT buses).</summary>
        public static IEnumerable<GuidePreset> Presets()
        {
            yield return P("brt.mk.text", MarkingTemplates.WordId, "6.9.1", "路面文字：快速公交", "Road words: BRT", "ksgj", "text=快速公交", "BRT", "快速公交", "公交专用", "路面文字");
            yield return P("brt.mk.text.time", MarkingTemplates.WordId, "6.9.1（图87）", "路面文字：快速公交与专用时间", "Road words: BRT, with the times", "ksgjzysj", "text=快速公交;time=7-9,17-19",
                "BRT", "快速公交", "时段", "专用时间", "路面文字");
            yield return P("brt.mk.busbay", "mk.busbay", "线25", "BRT 港湾式停靠站标线", "BRT bus bay", "gwstkz", "word=快速公交", "BRT", "快速公交", "港湾", "停靠站");
            yield return P("brt.mk.kerbstop", "mk.kerbstop", "线26", "BRT 路边式停靠站标线", "BRT kerb-side stop", "lbstkz", "word=快速公交", "BRT", "快速公交", "路边", "停靠站");
        }

        private static GuidePreset P(string id, string template, string code, string zh, string en, string pinyin, string overrides, params string[] keywords) =>
            new GuidePreset { Id = id, Template = template, Code = code, Zh = zh, En = en, Pinyin = pinyin, Overrides = overrides, Keywords = keywords };

        private const string Code = "非国标";
        private static readonly GuideColour Blue = GuideColour.Blue, Green = GuideColour.Green, White = GuideColour.White, Black = GuideColour.Black, Red = GuideColour.Red;

        private static readonly GuideChoice[] kGrounds = { new GuideChoice("blue", "蓝底", "Blue"), new GuideChoice("green", "绿底", "Green") };

        private static readonly GuideChoice[] kArrows =
        {
            new GuideChoice("up", "直行 ↑", "Ahead ↑"), new GuideChoice("left", "左 ←", "Left ←"), new GuideChoice("right", "右 →", "Right →"),
            new GuideChoice("upleft", "左前 ↖", "Ahead left ↖"), new GuideChoice("upright", "右前 ↗", "Ahead right ↗"), new GuideChoice("none", "无", "None")
        };

        private static readonly GuideChoice[] kEnds = { new GuideChoice("start", "起点", "Start"), new GuideChoice("end", "终点", "End") };

        /// <summary>The characters of a BRT board (m): those of the auxiliary and notice signs of the band, 0.15 to 0.33 m.</summary>
        private static float Height(int band) => CharHeight(band);

        private static GuideArrow ArrowOf(string key)
        {
            switch (key)
            {
                case "left": return GuideArrow.Left;
                case "right": return GuideArrow.Right;
                case "upleft": return GuideArrow.UpLeft;
                case "upright": return GuideArrow.UpRight;
                default: return GuideArrow.Up;
            }
        }

        /// <summary>The bus of 示28 (white) and text beside it, centred on one line: the pictogram 1.6 h wide and 1.1 h high.</summary>
        private static GuideBlock BusAnd(string text, float h, float textH, GuideColour colour)
        {
            GuideBlock words = AuxiliarySigns.Line(text, textH, colour);
            float busW = 1.6f * h, busH = 1.1f * h, gap = 0.4f * h, rowH = Math.Max(busH, textH);
            var row = new GuideBlock(busW + (words.W > 0f ? gap + words.W : 0f), rowH);
            row.Add(GuideKit.FitPicture(Glyphs.M_Bus, 0f, (rowH - busH) * 0.5f, busW, busH, colour));
            if (words.W > 0f)
                row.Add(words, busW + gap, (rowH - textH) * 0.5f);
            return row;
        }

        /// <summary>The way to a BRT station: the bus and "BRT 快速公交", the station's name larger, the arrow and the distance.</summary>
        private static GuideBoard Station(GuideSpec s, int band)
        {
            float h = Height(band);
            GuideColour ground = s.Is("ground", "green") ? Green : Blue;
            GuideBlock head = BusAnd("BRT 快速公交", h, 0.8f * h, White);
            GuideBlock name = Text(s.Get("name"), 1.25f * h, White);
            GuideBlock way = null;
            bool arrow = !s.Is("arrow", "none");
            string dist = (s.Get("dist") ?? "").Trim();
            if (arrow || dist.Length > 0)
            {
                GuideBlock a = arrow ? GuideKit.ArrowBlock(ArrowOf(s.Get("arrow")), h, White) : null;
                GuideBlock d = dist.Length > 0 ? GuideKit.Distance(dist, s.Is("unit", "km") ? "km" : "m", h, White) : null;
                float gap = 0.4f * h, w = (a != null ? a.W : 0f) + (d != null ? d.W : 0f) + (a != null && d != null ? gap : 0f), rowH = Math.Max(a != null ? a.H : 0f, d != null ? d.H : 0f);
                way = new GuideBlock(w, rowH);
                if (a != null)
                    way.Add(a, 0f, (rowH - a.H) * 0.5f);
                if (d != null)
                    way.Add(d, w - d.W, (rowH - d.H) * 0.5f);
            }

            return AuxiliarySigns.Board(Rows(h, head, name, way), h, 0.5f * h, 0.45f * h, ground, White);
        }

        /// <summary>No other vehicles in the BRT lane: a white board with a red border, the lane in red, the rule and the hours in black.</summary>
        private static GuideBoard NoCars(GuideSpec s, int band)
        {
            float h = Height(band);
            GuideBoard board = AuxiliarySigns.Board(Rows(h, Text(s.Get("l1"), 1.1f * h, Red), Text(s.Get("l2"), h, Black), Text(s.Get("time"), 0.9f * h, Black)), h, 0.5f * h, 0.45f * h, White, Red);
            return board;
        }

        /// <summary>Where the BRT lane starts or ends: the bus and the lane's name on a blue board, 起点 or 终点 under them, the hours under that.</summary>
        private static GuideBoard LaneEnds(GuideSpec s, int band)
        {
            float h = Height(band);
            GuideBlock head = BusAnd(s.Get("l1"), h, h, White);
            GuideBlock which = Text(s.Is("kind", "end") ? "终点" : "起点", 1.25f * h, White);
            return AuxiliarySigns.Board(Rows(h, head, which, Text(s.Get("time"), 0.9f * h, White)), h, 0.5f * h, 0.45f * h, Blue, White);
        }

        /// <summary>The enforcement notice of a BRT lane: a white board with a black border, the lane in blue, what is forbidden in black, the penalty in red, the camera in black.</summary>
        private static GuideBoard Notice(GuideSpec s, int band)
        {
            float h = Height(band);
            return AuxiliarySigns.Board(Rows(h, Text(s.Get("l1"), 1.1f * h, Blue), Text(s.Get("l2"), h, Black), Text(s.Get("l3"), h, Red), Text(s.Get("l4"), 0.8f * h, Black)), h, 0.5f * h, 0.45f * h, White, Black);
        }

        public static IEnumerable<GuideTemplate> All()
        {
            yield return StandardSigns.Make("brt.station", Code, "BRT 站点指引（常见做法）", "Way to a BRT station (common practice)", Group, "brtzdzy", StandardSigns.Words("BRT", "快速公交", "站点", "车站", "指引", "bus rapid transit", "station"),
                Station, new[]
                {
                    GuideField.Text("name", "站名", "Station", "大学城站", 8),
                    GuideField.Choice("arrow", "箭头", "Arrow", "up", kArrows),
                    GuideField.Digits("dist", "距离（可空）", "Distance (may be empty)", "200", 4),
                    GuideField.Choice("unit", "单位", "Unit", "m", new[] { new GuideChoice("m", "m", "m"), new GuideChoice("km", "km", "km") }),
                    GuideField.Choice("ground", "底色", "Ground", "blue", kGrounds)
                });
            yield return StandardSigns.Make("brt.nocars", Code, "社会车辆禁止驶入 BRT 专用道（常见做法）", "No other vehicles in the BRT lane (common practice)", Group, "shcljzsr", StandardSigns.Words("BRT", "快速公交", "社会车辆", "禁止驶入", "专用道", "no entry"),
                NoCars, new[]
                {
                    GuideField.Text("l1", "第一行", "Line 1", "BRT专用道", 10),
                    GuideField.Text("l2", "第二行", "Line 2", "社会车辆禁止驶入", 10),
                    GuideField.Text("time", "时段（可空）", "Hours (may be empty)", "", 24)
                });
            yield return StandardSigns.Make("brt.lane.ends", Code, "BRT 专用道起点、终点（常见做法）", "BRT lane start / end (common practice)", Group, "brtzydqdzd", StandardSigns.Words("BRT", "快速公交", "专用道", "起点", "终点", "start", "end"),
                LaneEnds, new[]
                {
                    GuideField.Choice("kind", "起点或终点", "Start or end", "start", kEnds),
                    GuideField.Text("l1", "名称", "Name", "BRT专用道", 10),
                    GuideField.Text("time", "时段（可空）", "Hours (may be empty)", "", 24)
                });
            yield return StandardSigns.Make("brt.notice", Code, "BRT 专用道违法告示（常见做法）", "BRT lane enforcement notice (common practice)", Group, "brtzydwfgs", StandardSigns.Words("BRT", "快速公交", "违法", "告示", "罚款", "电子监控", "notice", "fine"),
                Notice, new[]
                {
                    GuideField.Text("l1", "第一行", "Line 1", "BRT专用道", 10),
                    GuideField.Text("l2", "第二行", "Line 2", "社会车辆违法驶入", 10),
                    GuideField.Text("l3", "处罚（红字）", "Penalty (red)", "罚款200元 记3分", 12),
                    GuideField.Text("l4", "第四行（可空）", "Line 4 (may be empty)", "电子监控", 10)
                });
        }
    }
}
