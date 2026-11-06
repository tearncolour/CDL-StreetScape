using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;
using static CDLC.TS.Geo.Signs.RoadGuideSigns;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The variable-message signs of GB 5768.2-2022 (4.11): a screen that shows what the traffic, the road or the weather calls for (speed control, lane control, the state of the road, the
    /// weather) and is off when nothing needs to be shown (4.11.1). Drawn as LED screens (4.11.3): a black board with the figure in the colours of Table 10, read dot by dot (Led) and made
    /// of lit dots in the game. The standard gives no size, pitch or character height for them (4.11.4: the font, the height and the spacing by the principle of being clear, easy to read
    /// and safe), so the sizes here are this mod's: characters 30 to 60 cm, 32 dots high; a lane signal 1.2 m square; and the colours are Table 10's.
    /// The content is what the player sets, or (the text screen, src = auto, 0.8.0) what the road ahead says (VmsAuto: accidents and congestion, read from the game). The weather is not followed yet:
    /// no real value of it has been read.
    /// </summary>
    public static class VariableSigns
    {
        private const string G = "variable";
        private static readonly GuideColour Black = GuideColour.Black, Green = GuideColour.Green, Red = GuideColour.Red, Yellow = GuideColour.Yellow, White = GuideColour.White;

        /// <summary>The screen: a black board (no edge, a small radius) with the pitch of its dots; its part is given back.</summary>
        private static GuideBoard Screen(float width, float height, float pitch, float characterHeight, out GuidePart part, int maxDots = 0, int samples = 0)
        {
            part = new GuidePart { W = width, H = height, Ground = Black, Liner = 0f, Border = 0f, Radius = 0.04f };
            var board = new GuideBoard { H = characterHeight, LedPitch = pitch, LedMaxDots = maxDots, LedSamples = samples };
            board.Parts.Add(part);
            return board;
        }

        // ------------------------------------------------------------------ the text screen

        private static readonly GuideChoice[] kClasses =
        {
            new GuideChoice("warn", "警告信息（黄）", "Warning (yellow)"),
            new GuideChoice("info", "一般信息（绿）", "General (green)"),
            new GuideChoice("red", "禁令信息（红）", "Prohibition (red)"),
            new GuideChoice("white", "指示信息（白）", "Instruction (white)")
        };

        /// <summary>
        /// Whether a field of the player's own content is asked for: always without the extension that reads the game (0.12.29, LiveContent: the screen shows what was set), else while the screen's
        /// content is not automatic.
        /// </summary>
        private static bool Manual(GuideSpec s) => !LiveContent.On || !s.Is("src", "auto");

        private static readonly GuideChoice[] kSources =
        {
            new GuideChoice("manual", "手动（下面的文字）", "Manual (the lines)"),
            new GuideChoice("auto", "自动（前方事故、拥堵）", "Auto (accidents, jams ahead)")
        };

        private static readonly GuideChoice[] kSpeedSources =
        {
            new GuideChoice("manual", "手动（下面的限速值）", "Manual (the value)"),
            new GuideChoice("auto", "自动（路段限速，前方有事故时降低）", "Auto (road limit, lower ahead)")
        };

        private static readonly GuideChoice[] kLaneSources =
        {
            new GuideChoice("manual", "手动（下面的车道状态）", "Manual (the state)"),
            new GuideChoice("auto", "自动（车道被堵时关闭，旁道并道）", "Auto (closed if blocked)")
        };

        private static readonly GuideChoice[] kHeights =
        {
            new GuideChoice("30", "30 cm", "30 cm"), new GuideChoice("40", "40 cm", "40 cm"), new GuideChoice("50", "50 cm", "50 cm"), new GuideChoice("60", "60 cm", "60 cm")
        };

        /// <summary>The colour of the characters by the kind of information (Table 10: road general information green, road warning information yellow, prohibition red, instruction white).</summary>
        public static GuideColour ClassColour(string cls)
        {
            switch (cls)
            {
                case "info": return Green;
                case "red": return Red;
                case "white": return White;
                default: return Yellow;
            }
        }

        /// <summary>The longest text screen (m) a length may ask for: a screen across three lanes.</summary>
        public const float MaxScreenLength = 12f;

        /// <summary>
        /// The text screen: up to four lines of characters h high (Chinese in cells, letters and digits as ink), 0.15 h apart, the lines 0.35 h apart, centred, 0.5 h from the edges of the
        /// screen; at least four characters wide and two lines high, so that an empty screen (off, 4.11.1) is a screen and not a point. The dots are h/32 apart: a character is 32 dots high.
        /// Its length (0.12.22; the owner: "可变信息标志灯牌应该允许修改长度") is the player's when set, so that it stays one size whatever it shows (an automatic screen's text changes); a text longer than that
        /// makes it longer.
        /// </summary>
        private static GuideBoard TextScreen(GuideSpec s, int band)
        {
            float h = (float)s.Number("h", 40) / 100f;
            GuideColour colour = ClassColour(s.Get("cls"));
            var lines = new List<GuideBlock>();
            foreach (string key in new[] { "l1", "l2", "l3", "l4" })
            {
                string text = s.Get(key).Trim();
                if (text.Length > 0)
                {
                    GuideBlock line = Line(text, h, colour, 0.15f * h);
                    if (line.W > 0f)
                        lines.Add(line);
                }
            }

            float margin = 0.5f * h, gap = 0.35f * h;
            float widest = 4f * h;
            foreach (GuideBlock line in lines)
                widest = Math.Max(widest, line.W);
            int rows = Math.Max(2, lines.Count);
            float asked = (float)Math.Max(0.0, Math.Min(MaxScreenLength, s.Number("len", 0.0)));
            float width = Math.Max(widest + 2f * margin, asked), height = rows * h + (rows - 1) * gap + 2f * margin;
            GuideBoard board = Screen(width, height, h / Led.DotsPerCharacter, h, out GuidePart part);
            float y = margin + (rows - lines.Count) * (h + gap) * 0.5f;
            foreach (GuideBlock line in lines)
            {
                part.Add(line, (width - line.W) * 0.5f, y);
                y += h + gap;
            }

            return board;
        }

        // ------------------------------------------------------------------ the lane-control screen

        private static readonly GuideChoice[] kLaneStates =
        {
            new GuideChoice("pass", "绿色向下箭头（可通行）", "Green arrow down (open)"),
            new GuideChoice("closed", "红色叉（车道关闭）", "Red cross (closed)"),
            new GuideChoice("left", "黄色斜箭头（向左并道）", "Yellow arrow (merge left)"),
            new GuideChoice("right", "黄色斜箭头（向右并道）", "Yellow arrow (merge right)"),
            new GuideChoice("off", "关闭（不显示）", "Off (nothing shown)")
        };

        /// <summary>A lane signal's side (m): square, so that it can hang over one lane (3 to 3.75 m wide) with room to spare.</summary>
        public const float LaneSide = 1.2f;

        /// <summary>A bar of thickness t from (x0, y0) to (x1, y1) as a polygon (the strokes of the cross).</summary>
        private static float[] Bar(float x0, float y0, float x1, float y1, float t)
        {
            float dx = x1 - x0, dy = y1 - y0, length = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / length * t * 0.5f, ny = dx / length * t * 0.5f;
            return new[] { x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny };
        }

        /// <summary>The same polygon turned half a turn about the middle of a square of the given side at (x, y): an arrow up and to the right becomes one down and to the left.</summary>
        private static float[] HalfTurn(float[] points, float x, float y, float side)
        {
            var turned = new float[points.Length];
            for (int i = 0; i + 1 < points.Length; i += 2)
            {
                turned[i] = 2f * (x + side * 0.5f) - points[i];
                turned[i + 1] = 2f * (y + side * 0.5f) - points[i + 1];
            }

            return turned;
        }

        /// <summary>
        /// The lane-control screen (Table 10 lane and traffic signs; the figures by the usual practice, the standard shows none): a green arrow down over a lane that is open, a red cross over
        /// one that is closed, a yellow arrow down and to one side where the traffic has to change lanes; nothing when it is off.
        /// </summary>
        private static GuideBoard LaneScreen(GuideSpec s, int band)
        {
            GuideBoard board = Screen(LaneSide, LaneSide, Led.DefaultPitch, 0.5f, out GuidePart part);
            float m = 0.18f, box = LaneSide - 2f * m;
            switch (s.Get("state"))
            {
                case "closed":
                    part.Elements.Add(Poly(Bar(m, m, m + box, m + box, 0.16f), Red));
                    part.Elements.Add(Poly(Bar(m + box, m, m, m + box, 0.16f), Red));
                    break;
                case "left":
                    part.Elements.Add(Poly(HalfTurn(ArrowPolygon(GuideArrow.UpRight, m, m, box), m, m, box), Yellow));
                    break;
                case "right":
                    part.Elements.Add(Poly(HalfTurn(ArrowPolygon(GuideArrow.UpLeft, m, m, box), m, m, box), Yellow));
                    break;
                case "off":
                    break;
                default:
                    part.Elements.Add(Poly(ArrowPolygon(GuideArrow.Down, m, m, box), Green));
                    break;
            }

            return board;
        }

        // ------------------------------------------------------------------ the speed screen

        /// <summary>
        /// The speed-limit screen (Table 10, prohibition signs: black ground, red border, yellow figures): a red ring 0.14 m wide on a 1.2 m screen and the limit in yellow digits in it; an
        /// empty value is the ring alone.
        /// </summary>
        private static GuideBoard SpeedScreen(GuideSpec s, int band)
        {
            GuideBoard board = Screen(LaneSide, LaneSide, Led.DefaultPitch, 0.4f, out GuidePart part);
            const float edge = 0.05f, ring = 0.14f;
            part.Elements.Add(Oval(edge, edge, LaneSide - 2f * edge, LaneSide - 2f * edge, Red));
            part.Elements.Add(Oval(edge + ring, edge + ring, LaneSide - 2f * (edge + ring), LaneSide - 2f * (edge + ring), Black));
            string value = s.Get("value").Trim();
            if (value.Length > 0)
            {
                float digit = value.Length >= 3 ? 0.32f : 0.42f;
                float width = LatinWidth(value, digit);
                part.Elements.Add(Ink(value, (LaneSide - width) * 0.5f, (LaneSide - digit) * 0.5f, width, digit, Yellow, GuideAlign.Centre));
            }

            return board;
        }

        // ------------------------------------------------------------------ the traffic-state screen

        private static readonly GuideChoice[] kTrafficSizes =
        {
            new GuideChoice("s", "小：3.2 m x 2.4 m", "Small: 3.2 m x 2.4 m"),
            new GuideChoice("m", "中：4.0 m x 3.0 m", "Medium: 4.0 m x 3.0 m"),
            new GuideChoice("l", "大：5.0 m x 3.75 m", "Large: 5.0 m x 3.75 m")
        };

        private static readonly GuideChoice[] kJunctions =
        {
            new GuideChoice("2", "前方 2 个路口", "2 junctions ahead"), new GuideChoice("3", "前方 3 个路口", "3 junctions ahead"),
            new GuideChoice("4", "前方 4 个路口", "4 junctions ahead"), new GuideChoice("5", "前方 5 个路口", "5 junctions ahead")
        };

        private static readonly GuideChoice[] kRanges =
        {
            new GuideChoice("500", "500 m", "500 m"), new GuideChoice("800", "800 m", "800 m"), new GuideChoice("1200", "1200 m", "1200 m"), new GuideChoice("2000", "2000 m", "2000 m")
        };

        /// <summary>
        /// The dots across the traffic-state screen, whatever its size (the pitch follows the size: 6.7 mm on the small screen, 8.3 on the medium, 10.4 on the large) and so 30 dots to a
        /// character on all three: a map with the names of roads has to be read from far, and a screen with 16 dots to the character (0.5.4, at the text screens' pitch) was blurred. Few of its
        /// dots are lit (about one in ten), and only those cost vertices.
        /// </summary>
        public const int TrafficDotsAcross = 480;

        /// <summary>The samples of the painted picture to a dot of the traffic-state screen (the text screens have Led.SamplesPerDot, six).</summary>
        public const int TrafficSamplesPerDot = 3;

        /// <summary>The most dots the traffic-state screen has (480 x 360 and a margin for the rounding).</summary>
        public const int TrafficMaxDots = 480 * 360 + 1000;

        /// <summary>The size of the traffic-state screen (m).</summary>
        public static void TrafficSize(string size, out float width, out float height)
        {
            switch (size)
            {
                case "s": width = 3.2f; height = 2.4f; break;
                case "l": width = 5.0f; height = 3.75f; break;
                default: width = 4.0f; height = 3.0f; break;
            }
        }

        /// <summary>
        /// The traffic-state screen (no figure of the standard: 4.11.1 names speed, lane, road and weather information and Table 10 the colours of a traffic-state screen, blue or green ground and red,
        /// yellow, green figures; the ones over Chinese city roads show the roads ahead as a small map): a black screen with the roads that traffic from the sign can take, each line green,
        /// yellow or red by how congested it is that way, their names in white, the sign's red mark at the foot, the way the traffic goes at the top right. What it shows comes from the
        /// network the game's reader published for this item ("id"); a screen that has not been read yet, or has no item (the panel's picture, the preview), shows a made-up network.
        /// </summary>
        private static GuideBoard TrafficScreen(GuideSpec s, int band)
        {
            TrafficSize(s.Get("size"), out float width, out float height);
            float ch = 0.085f * height;
            // Three samples to a dot, not six: the picture the dots are read from is 1440 x 1080 and not 2880 x 2160 (6 MB and not 25 MB to paint and read each time the screen changes, 0.6.0).
            GuideBoard board = Screen(width, height, width / TrafficDotsAcross, ch, out GuidePart part, TrafficMaxDots, TrafficSamplesPerDot);
            TmNetwork network;
            string id = s.Get("id");
            if (id.Length == 0)
            {
                network = TrafficMap.Demo();
            }
            else
            {
                // The network of this revision: the published one, or the staged one that is being made ready for it.
                int.TryParse(s.Get("rev"), out int revision);
                network = int.TryParse(id, out int number) ? TrafficMap.Get(number, revision) : null;
            }
            part.Elements.AddRange(TrafficMap.Layout(network ?? new TmNetwork(), new TmOptions { Width = width, Height = height, CharHeight = ch, Names = s.Flag("names"), Direction = s.Flag("dir") }));
            return board;
        }

        // ------------------------------------------------------------------ the catalogue

        private static string[] W(params string[] words) => words;

        public static IEnumerable<GuideTemplate> All()
        {
            // 4.11: a screen with up to four lines of text; empty, it is off.
            yield return StandardSigns.Make("gb.vms.text", "4.11", "可变信息标志（文字）", "Variable-message sign (text)", G, "kbxxbzwz", W("可变信息", "可变", "情报板", "文字", "LED", "显示屏", "variable", "message", "VMS"),
                TextScreen, new[]
                {
                    // The lines and the colour are the player's when the screen is manual; an automatic screen says what the road ahead says, so they are not asked for (the editor's note shows it).
                    GuideField.Text("l1", "第一行", "Line 1", "前方事故", 10, Manual),
                    GuideField.Text("l2", "第二行", "Line 2", "减速慢行", 10, Manual),
                    GuideField.Text("l3", "第三行（可空）", "Line 3 (may be empty)", "", 10, Manual),
                    GuideField.Text("l4", "第四行（可空）", "Line 4 (may be empty)", "", 10, Manual),
                    GuideField.Choice("cls", "信息类别（颜色）", "Kind (colour)", "warn", kClasses, Manual),
                    GuideField.Choice("h", "字高", "Character height", "40", kHeights),
                    GuideField.Digits("len", "屏幕长度（m，0 为按文字）", "Screen length (m; 0: as the text)", "0", 4),
                    // 0.8.0: the lines follow the game's accidents and congestion on the road ahead (VmsAuto); the lines above are the player's own and show when the screen is manual. An older
                    // item has no such field and is manual.
                    GuideField.Choice("src", "内容来源", "Content", "manual", kSources, sp => LiveContent.On)
                });

            // The lane-control screens, one for each lane: open, closed, merge; listed with the state of each.
            yield return StandardSigns.Make("gb.vms.lane", "4.11", "可变信息标志（车道控制）", "Variable-message sign (lane control)", G, "kbxxbzcdkz", W("车道控制", "车道", "可变", "可变导向车道", "箭头", "叉", "LED", "lane", "variable"),
                LaneScreen, LaneFields("pass"));
            yield return StandardSigns.Make("gb.vms.lane.closed", "4.11", "可变信息标志（车道关闭）", "Variable-message sign (lane closed)", G, "kbxxbzcdgb", W("车道关闭", "车道", "叉", "可变", "LED", "closed", "variable"),
                LaneScreen, LaneFields("closed"));

            // The traffic-state screen: the congestion of the roads ahead, read from the game.
            GuideTemplate traffic = StandardSigns.Make(TrafficMap.TemplateId, "4.11", "可变信息标志（路网态势）", "Variable-message sign (traffic state ahead)", G, "kbxxbzlwts", W("路网态势", "路况", "拥堵", "交通状态", "诱导屏", "可变", "LED", "路口", "traffic", "congestion", "variable"),
                TrafficScreen, new[]
                {
                    GuideField.Choice("size", "屏幕大小", "Screen size", "m", kTrafficSizes),
                    GuideField.Choice("jn", "前方路口数", "Junctions ahead", "3", kJunctions),
                    GuideField.Choice("range", "最远距离", "Farthest", "800", kRanges),
                    GuideField.Flag("names", "显示路名", "Show road names", true),
                    GuideField.Flag("dir", "显示行驶方向", "Show the direction of travel", true),
                    GuideField.Digits("slow", "缓行：车速低于 %", "Slow: speed below %", "70", 3),
                    GuideField.Digits("jam", "拥堵：车速低于自由流的 %", "Jam: below this % of free speed", "40", 3),
                    // Set by the renderer for each placed item, not by the player (never shown): the item and the revision of what it shows.
                    GuideField.Text("id", "（内部）实时牌号", "(internal) live item", "", 9, sp => false),
                    GuideField.Text("rev", "（内部）实时版本", "(internal) live revision", "", 9, sp => false)
                });
            traffic.Live = true;
            yield return traffic;

            // The speed limit as a screen.
            yield return StandardSigns.Make("gb.vms.speed", "4.11", "可变信息标志（限速）", "Variable-message sign (speed limit)", G, "kbxxbzxs", W("限速", "速度控制", "可变限速", "可变", "LED", "speed", "variable"),
                SpeedScreen, new[]
                {
                    GuideField.Digits("value", "限速值（km/h，空为只有红圈）", "Limit (km/h; empty: the ring only)", "60", 3, Manual),
                    // 0.9.0: the limit follows the road the sign stands on (VmsAuto.SpeedFor). An older item has no such field and is manual.
                    GuideField.Choice("src", "内容来源", "Content", "manual", kSpeedSources, sp => LiveContent.On)
                });
        }

        private static GuideField[] LaneFields(string state) => new[]
        {
            GuideField.Choice("state", "车道状态", "Lane state", state, kLaneStates, Manual),
            // 0.9.0: the state follows what blocks the lane the board is over (VmsAuto.LaneStateFor); the board has to be assigned to a lane. An older item has no such field and is manual.
            GuideField.Choice("src", "内容来源", "Content", "manual", kLaneSources, sp => LiveContent.On)
        };
    }
}
