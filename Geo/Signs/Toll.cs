using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.Signs.AuxiliarySigns;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The toll plaza series (0.12.26; the owner: "加入高速收费站路标等等"): the catalogue's 收费站 tab lists the standard's toll signs and markings (路65 take a ticket, 路70 the toll station, 路71 the ETC
    /// lanes, 路72 to 路74 the toll lanes, 线69 and 线70, the lane-control screens, the height limit; Series) with the boards a plaza carries that the standard does not have, after the usual
    /// practice and marked 非国标: the station's name on its canopy, the board over each lane, the lane's signal (a red cross over a green arrow), and the notices (slow down, keep your distance, weigh
    /// in, how to pay); and the cones, drums, bollards and the plaza's own things (TollProps) from the barriers. The boards are drawn with the characters of the road's speed band (Table 3) as the
    /// notices' are; the colours are the usual ones (green and blue grounds with white characters, yellow with black), the sizes this mod's.
    /// </summary>
    public static class TollSigns
    {
        public const string Group = "toll";

        public const string StationId = "toll.station", LaneId = "toll.lane", SignalId = "toll.signal", NoticeId = "toll.notice";

        /// <summary>
        /// What belongs to a toll plaza and to nothing else (0.12.107; the owner, with a picture of the barrier tab: "你完全属于收费站的东西就别进到栏杆分组里了"): the lifting-arm gate, the booth, the island and the
        /// rubber hump. They are listed in the toll tab only, not in the barrier tab (their kind stays the barriers', which the placing reads); the cones, drums, water barriers and bollards are barriers
        /// anywhere, and stay in both.
        /// </summary>
        public static readonly HashSet<string> Only = new HashSet<string> { MarkingTemplates.GateId, MarkingTemplates.BoothId, MarkingTemplates.TollIslandId, MarkingTemplates.HumpId };

        /// <summary>What the series lists besides its own boards: the standard's signs and markings of a toll plaza, and the barriers and props that go with one.</summary>
        public static readonly HashSet<string> Series = new HashSet<string>(Only)
        {
            "gb.guide8.65", "gb.guide8.70", "gb.guide8.71", "gb.guide8.72", "gb.prohib.36", "gb.vms.lane", "gb.vms.lane.closed", "mk.toll.plaza", "mk.toll.island",
            MarkingTemplates.ConeId, MarkingTemplates.CrashDrumId, MarkingTemplates.WaterBarrierId, MarkingTemplates.BollardId
        };

        /// <summary>The series an entry of the catalogue is also listed in ("" for none).</summary>
        public static string SeriesOf(string id, string group) => group == Group || (id != null && Series.Contains(id)) ? Group : "";

        private const string Code = "非国标";
        private static readonly GuideColour Blue = GuideColour.Blue, Green = GuideColour.Green, White = GuideColour.White, Black = GuideColour.Black, Red = GuideColour.Red, Yellow = GuideColour.Yellow, Gray = GuideColour.Gray;

        private static readonly GuideChoice[] kNoticeGrounds = { new GuideChoice("yellow", "黄底黑字", "Yellow, black text"), new GuideChoice("green", "绿底白字", "Green, white text"), new GuideChoice("blue", "蓝底白字", "Blue, white text"), new GuideChoice("white", "白底黑字", "White, black text") };

        private static readonly GuideChoice[] kLaneKinds =
        {
            new GuideChoice("etc", "ETC 专用", "ETC only"), new GuideChoice("mixed", "ETC 与人工混合", "ETC and manual"), new GuideChoice("manual", "人工收费", "Manual"),
            new GuideChoice("truck", "货车专用", "Trucks"), new GuideChoice("green", "绿色通道", "Green channel")
        };

        private static readonly GuideChoice[] kSignalStates = { new GuideChoice("open", "绿色箭头亮（可通行）", "Green arrow lit (open)"), new GuideChoice("closed", "红色叉亮（禁止通行）", "Red cross lit (closed)") };

        /// <summary>The characters of a toll board (m): those of the auxiliary and notice signs of the band, 0.15 to 0.33 m.</summary>
        private static float Height(int band) => CharHeight(band);

        /// <summary>The arrow of the signal: a stem and a head, in a box 100 wide and 100 high (the pictures' units).</summary>
        private static readonly string ArrowDown = new PicBuilder().Rect(38f, 4f, 24f, 50f).Poly(false, 12f, 50f, 88f, 50f, 50f, 96f).ToString();

        /// <summary>The cross of the signal: two bars at 45 degrees.</summary>
        private static readonly string Cross = new PicBuilder().Capsule(14f, 14f, 86f, 86f, 20f).Capsule(86f, 14f, 14f, 86f, 20f).ToString();

        /// <summary>The words and the colours of a kind of lane: two lines, the ground, the first line's colour and the second's.</summary>
        private static void LaneOf(string kind, out string l1, out string l2, out GuideColour ground, out GuideColour c1, out GuideColour c2)
        {
            switch (kind)
            {
                case "mixed":
                    l1 = "ETC/人工"; l2 = "混合"; ground = Blue; c1 = Yellow; c2 = White;
                    break;
                case "manual":
                    l1 = "人工"; l2 = "收费"; ground = Green; c1 = White; c2 = White;
                    break;
                case "truck":
                    l1 = "货车"; l2 = "专用"; ground = Yellow; c1 = Black; c2 = Black;
                    break;
                case "green":
                    l1 = "绿色通道"; l2 = ""; ground = Green; c1 = White; c2 = White;
                    break;
                default:
                    l1 = "ETC"; l2 = "专用"; ground = Blue; c1 = Yellow; c2 = White;
                    break;
            }
        }

        /// <summary>The station's name on its canopy: ETC in yellow beside the name in large characters, the name in English under it, white on green or blue, with a white border.</summary>
        private static GuideBoard Station(GuideSpec s, int band)
        {
            float h = Height(band);
            GuideBlock name = Text(s.Get("name"), 2.4f * h, White);
            GuideBlock row = name;
            if (s.Flag("etc") && name != null)
            {
                GuideBlock etc = Line("ETC", 1.1f * h, Yellow);
                float gap = 0.8f * h, rowH = Math.Max(name.H, etc.H);
                row = new GuideBlock(etc.W + gap + name.W, rowH);
                row.Add(etc, 0f, (rowH - etc.H) * 0.5f);
                row.Add(name, etc.W + gap, (rowH - name.H) * 0.5f);
            }

            GuideColour ground = s.Is("ground", "blue") ? Blue : Green;
            return Board(Rows(h, row, Text(s.Get("en"), 0.9f * h, White)), h, 1.2f * h, 0.8f * h, ground, White);
        }

        /// <summary>The board over a toll lane: the lane's number in large digits, a bar, and the kind of lane in two lines, on the kind's ground.</summary>
        private static GuideBoard Lane(GuideSpec s, int band)
        {
            float h = Height(band);
            LaneOf(s.Get("kind"), out string l1, out string l2, out GuideColour ground, out GuideColour c1, out GuideColour c2);
            string own1 = (s.Get("l1") ?? "").Trim(), own2 = (s.Get("l2") ?? "").Trim();
            if (own1.Length > 0 || own2.Length > 0)
            {
                l1 = own1;
                l2 = own2;
            }

            GuideColour ink = ground == Yellow ? Black : White;
            GuideBlock number = Text(s.Get("no"), 2.2f * h, ink);
            GuideBlock words = l1.Length + l2.Length > 0 ? Rows(h, Text(l1, 1.3f * h, c1), Text(l2, 1.0f * h, c2)) : null;
            GuideBlock row = number ?? words ?? Line("ETC", h, ink);
            if (number != null && words != null)
            {
                // The number and the words with a bar between them.
                float gap = 0.9f * h, bar = 0.1f * h, rowH = Math.Max(number.H, words.H);
                row = new GuideBlock(number.W + 2f * gap + bar + words.W, rowH);
                row.Add(number, 0f, (rowH - number.H) * 0.5f);
                row.Elements.Add(GuideKit.Rect(number.W + gap, 0.1f * rowH, bar, 0.8f * rowH, ink));
                row.Add(words, number.W + 2f * gap + bar, (rowH - words.H) * 0.5f);
            }

            return Board(row, h, 0.8f * h, 0.5f * h, ground, ground == Yellow ? Black : White);
        }

        /// <summary>The lane's signal: a black box with two lamps, the red cross above the green arrow; the one that is lit in colour, the other dark grey.</summary>
        private static GuideBoard Signal(GuideSpec s, int band)
        {
            float h = Height(band);
            bool open = !s.Is("state", "closed");
            float lamp = 1.6f * h, gap = 0.3f * h;
            var box = new GuideBlock(lamp, 2f * lamp + gap);
            // The cross over the arrow, as the signals over the lanes of a plaza hang.
            box.Elements.Add(GuideKit.Rect(0f, 0f, lamp, lamp, open ? Gray : Red, 0.5f * lamp));
            box.Elements.Add(GuideKit.FitPicture(Cross, 0.22f * lamp, 0.22f * lamp, 0.56f * lamp, 0.56f * lamp, open ? Black : White));
            box.Elements.Add(GuideKit.Rect(0f, lamp + gap, lamp, lamp, open ? Green : Gray, 0.5f * lamp));
            box.Elements.Add(GuideKit.FitPicture(ArrowDown, 0.22f * lamp, lamp + gap + 0.2f * lamp, 0.56f * lamp, 0.6f * lamp, open ? White : Black));
            return Board(box, h, 0.4f * h, 0.4f * h, Black, Gray);
        }

        /// <summary>A notice of the plaza: up to three lines on a ground of the series' colours, the characters the colour that reads on it, the border white or black.</summary>
        private static GuideBoard Notice(GuideSpec s, int band)
        {
            float h = Height(band);
            string ground = s.Get("ground");
            GuideColour back = ground == "green" ? Green : ground == "blue" ? Blue : ground == "white" ? White : Yellow;
            GuideColour ink = back == Green || back == Blue ? White : Black;
            return Board(Rows(h, Text(s.Get("l1"), 1.2f * h, ink), Text(s.Get("l2"), h, ink), Text(s.Get("l3"), 0.9f * h, ink)), h, 0.6f * h, 0.5f * h, back, back == Green || back == Blue ? White : Black);
        }

        /// <summary>The presets of the notice: the lines the plazas put up most.</summary>
        public static IEnumerable<GuidePreset> Presets()
        {
            yield return P("toll.p.ahead", "前方收费站 请减速慢行", "Toll station ahead, slow down", "qfsfzqjs", "l1=前方收费站;l2=请减速慢行;l3=;ground=yellow", "前方", "收费站", "减速", "慢行");
            yield return P("toll.p.dist", "前方收费站 保持车距", "Toll station ahead, keep your distance", "qfsfzbcj", "l1=前方收费站;l2=请保持车距;l3=按指示车道通行;ground=green", "前方", "收费站", "车距", "车道");
            yield return P("toll.p.etc", "ETC 车道 限速", "ETC lanes, speed limit", "etccdxs", "l1=ETC车道;l2=限速20km/h;l3=请勿停车 请勿跟车;ground=blue", "ETC", "限速", "勿停车", "跟车");
            yield return P("toll.p.pay", "请按车道指示缴费", "Pay as the lanes say", "qazdzjjf", "l1=请按指示;l2=缴费通行;l3=;ground=green", "缴费", "指示", "通行", "车道");
            yield return P("toll.p.weigh", "入口称重检测", "Weighing at the entry", "rkczjc", "l1=入口称重检测;l2=超限超载车辆;l3=禁止驶入;ground=yellow", "称重", "检测", "超限", "超载");
            yield return P("toll.p.truck", "货车请走货车通道", "Trucks to the truck lanes", "hcqzhctd", "l1=货车;l2=请走货车通道;l3=;ground=blue", "货车", "通道", "专用");
        }

        private static GuidePreset P(string id, string zh, string en, string pinyin, string overrides, params string[] keywords) =>
            new GuidePreset { Id = id, Template = NoticeId, Code = Code, Zh = zh, En = en, Pinyin = pinyin, Overrides = overrides, Keywords = keywords };

        public static IEnumerable<GuideTemplate> All()
        {
            yield return StandardSigns.Make(StationId, Code, "收费站名牌（雨棚、常见做法）", "Toll station's name (canopy, common practice)", Group, "sfzmp", StandardSigns.Words("收费站", "名牌", "雨棚", "ETC", "toll station", "canopy", "name"),
                Station, new[]
                {
                    GuideField.Text("name", "站名", "Name", "示范收费站", 10),
                    GuideField.Text("en", "英文（可空）", "English (may be empty)", "", 24),
                    GuideField.Flag("etc", "带 ETC 字样", "With ETC", true),
                    GuideField.Choice("ground", "底色", "Ground", "green", new[] { new GuideChoice("green", "绿底", "Green"), new GuideChoice("blue", "蓝底", "Blue") })
                });
            yield return StandardSigns.Make(LaneId, Code, "收费车道牌（车道号和类型）", "Toll lane board (number and kind)", Group, "sfcdp", StandardSigns.Words("收费", "车道", "ETC", "人工", "混合", "货车", "绿色通道", "车道号", "toll lane"),
                Lane, new[]
                {
                    GuideField.Choice("kind", "类型", "Kind", "etc", kLaneKinds),
                    GuideField.Digits("no", "车道号（可空）", "Lane number (may be empty)", "1", 2),
                    GuideField.Text("l1", "第一行（空：按类型）", "Line 1 (empty: by the kind)", "", 8),
                    GuideField.Text("l2", "第二行", "Line 2", "", 8)
                });
            yield return StandardSigns.Make(SignalId, Code, "收费车道通行信号灯（红叉绿箭）", "Toll lane signal (red cross, green arrow)", Group, "sfcdtxxhd", StandardSigns.Words("通行", "信号灯", "红叉", "绿箭", "车道", "signal", "lane"),
                Signal, new[] { GuideField.Choice("state", "亮的灯", "The lit lamp", "open", kSignalStates) });
            yield return StandardSigns.Make(NoticeId, Code, "收费站提示牌（常见做法）", "Toll plaza notice (common practice)", Group, "sfztsp", StandardSigns.Words("收费站", "提示", "减速", "称重", "车距", "ETC", "notice", "toll"),
                Notice, new[]
                {
                    GuideField.Text("l1", "第一行", "Line 1", "前方收费站", 12),
                    GuideField.Text("l2", "第二行", "Line 2", "请减速慢行", 12),
                    GuideField.Text("l3", "第三行（可空）", "Line 3 (may be empty)", "", 14),
                    GuideField.Choice("ground", "底色", "Ground", "yellow", kNoticeGrounds)
                });
        }
    }
}
