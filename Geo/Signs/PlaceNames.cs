using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The place-name signs of GB 17733-2008 (地名 标志), group "placename": the street and lane signs, the signs of areas, residential quarters and villages, the door, building, unit and floor plates of
    /// 4.2.1, the signs of administrative areas, special zones, facilities, memorial and tourist places and of the natural features of 4.1 and 4.2, and the stone (碑碣式) signs; with a pictogram (5.3.2),
    /// the direction of the street (5.3.3) and the postal code of a door (5.3.4). The layouts are those of 5.3.1 and Appendix B (the Chinese name in the upper three fifths, its pinyin in the lower two
    /// fifths; measured on Figs. B.11 to B.14: the characters 0.44 of the board's height, in the middle of the upper three fifths and spread over its width; the pinyin in capitals 0.17 of the height, its
    /// top at 0.66 of it, letters a quarter of their height apart and words 1.2 of it); the colours are Table 1's (blue, green, brown, white, the base plate's colour for an etched metal sign and for a
    /// stone); the sizes are Table 2's (the writing plane, mm; a size that is a range is set in it). The pinyin is typed (5.2.1.3: the spelling rules of Chinese place names): this mod does not convert
    /// characters. The text is in a sans-serif bold (5.2.1.5: 黑体; Appendix A's letters and figures are condensed capitals, and the mod's font stands in for them). Pure.
    /// </summary>
    public static class PlaceNameSigns
    {
        public const string Group = StandardSigns.PlaceName;

        private const GuideColour Blue = GuideColour.Blue, Green = GuideColour.Green, Brown = GuideColour.Brown, White = GuideColour.White, Red = GuideColour.Red, Black = GuideColour.Black, Plate = GuideColour.Gray;

        /// <summary>The Chinese characters' height and where their middle is, of the board's height; the pinyin's height and its top (Figs. B.11 to B.14).</summary>
        public const float CharK = 0.44f, CharMidK = 0.30f, PinyinK = 0.17f, PinyinTopK = 0.66f;

        /// <summary>The margin at the ends of a line (of the board's height), the space between letters and between words (of their height).</summary>
        public const float MarginK = 0.22f, TrackK = 0.25f, WordGapK = 1.2f;

        /// <summary>A stone (碑碣式, 5.1.2) stands on the ground: its writing plane starts this high (m), over a low plinth; the other signs are 2 m up at least (5.1.3), as the roadside boards (2.3 m).</summary>
        public const float StoneClearance = 0.3f;

        public static bool IsStone(string id) => id != null && id.StartsWith("gb17733.stone", StringComparison.Ordinal);

        private static string[] W(params string[] words) => words;

        private static GuideTemplate Make(string id, string code, string zh, string en, string pinyin, string[] keywords, Func<GuideSpec, GuideBoard> layout, GuideField[] fields) => new GuideTemplate
        {
            Id = id,
            Code = code,
            Zh = zh,
            En = en,
            Group = Group,
            Pinyin = pinyin,
            Keywords = keywords,
            Standalone = true,
            Fields = fields,
            Layout = layout
        };

        private static GuideField Name(string def, int max = 12) => GuideField.Text("name", "汉字名称", "Chinese name", def, max);

        private static GuideField Spelling(string def) => GuideField.Text("py", "汉语拼音（大写）", "Pinyin (capitals)", def, 32);

        private static GuideField Size(string key, string zh, string en, string def, int digits) => GuideField.Digits(key, zh, en, def, digits);

        private static readonly GuideChoice[] kRun =
        {
            new GuideChoice("ew", "东西走向（蓝底）", "East-west (blue)"),
            new GuideChoice("ns", "南北走向（绿底）", "North-south (green)")
        };

        private static readonly GuideChoice[] kEnds =
        {
            new GuideChoice("", "不标", "None"),
            new GuideChoice("ew", "东 … 西", "East ... West"),
            new GuideChoice("we", "西 … 东", "West ... East"),
            new GuideChoice("sn", "南 … 北", "South ... North"),
            new GuideChoice("ns", "北 … 南", "North ... South")
        };

        private static readonly GuideChoice[] kFinish =
        {
            new GuideChoice("blue", "蓝底白字", "White on blue"),
            new GuideChoice("metal", "金属腐蚀（基板色，黑字）", "Etched metal (black)"),
            new GuideChoice("metalred", "金属腐蚀（基板色，红字）", "Etched metal (red)")
        };

        private static readonly GuideChoice[] kSymbols =
        {
            new GuideChoice("", "无", "None"),
            new GuideChoice("地铁", "地铁", "Metro"),
            new GuideChoice("机场", "飞机场", "Airport"),
            new GuideChoice("轮渡", "渡口、港口", "Ferry, port"),
            new GuideChoice("停车", "停车场", "Car park"),
            new GuideChoice("加油", "加油站", "Fuel station"),
            new GuideChoice("充电", "充电站", "Charging station")
        };

        private static readonly GuideChoice[] kDoorSizes =
        {
            new GuideChoice("l", "大（570×370 mm）", "Large (570 x 370 mm)"),
            new GuideChoice("m", "中（270×170 mm）", "Middle (270 x 170 mm)"),
            new GuideChoice("s", "小（150×90 mm）", "Small (150 x 90 mm)")
        };

        private static readonly GuideChoice[] kInk =
        {
            new GuideChoice("k", "黑字", "Black"),
            new GuideChoice("r", "红字", "Red")
        };

        /// <summary>A number of the settings in mm, held to the range of Table 2, in metres.</summary>
        private static float Mm(GuideSpec s, string key, float def, float min, float max)
        {
            double v = s.Number(key, def);
            if (double.IsNaN(v) || double.IsInfinity(v))
                v = def;
            return (float)Math.Max(min, Math.Min(max, v)) / 1000f;
        }

        /// <summary>A board of one part w x h (m) in a colour, its corners a little rounded (5.1.3), no liner and no border (the signs of Appendix B have none).</summary>
        internal static GuidePart Board(float w, float h, GuideColour ground) => new GuidePart { X = 0f, Y = 0f, W = w, H = h, Ground = ground, Liner = 0f, Border = 0f, Radius = Math.Min(w, h) * 0.03f };

        private static GuideBoard Single(GuidePart p, float h)
        {
            var board = new GuideBoard { H = h };
            board.Parts.Add(p);
            return board;
        }

        // ------------------------------------------------------------------ the pieces

        /// <summary>Chinese characters in square cells ch high, spread over the width from x0 to x1 (equal gaps, the first at x0 and the last at x1; one character in the middle), at y; made smaller when they do not fit with a gap of 0.1 of them.</summary>
        internal static GuideElement Spread(string text, float x0, float x1, float y, float ch, GuideColour colour)
        {
            text = (text ?? "").Trim();
            int n = text.Length;
            float avail = Math.Max(0.01f, x1 - x0);
            if (n == 0)
                return null;
            if (n * ch + (n - 1) * 0.1f * ch > avail)
            {
                float smaller = avail / (n + (n - 1) * 0.1f);
                y += (ch - smaller) * 0.5f;
                ch = smaller;
            }

            if (n == 1)
                return Cells(text, x0 + (avail - ch) * 0.5f, y, ch, colour, 0f);
            float gap = (avail - n * ch) / (n - 1);
            // Spread to the whole width only while the gap is under a character (Figs. B.11, B.13: three and four characters); more room and the run is centred with a gap of a character.
            gap = Math.Min(gap, ch);
            float w = n * ch + (n - 1) * gap;
            return Cells(text, x0 + (avail - w) * 0.5f, y, ch, colour, gap);
        }

        /// <summary>
        /// The pinyin as capitals cap high, letter by letter (a quarter of their height apart, the words 1.2 of it), centred between x0 and x1 at y; squeezed to fit. One element for each letter, so that the
        /// spacing of Appendix B is kept.
        /// </summary>
        internal static List<GuideElement> Letters(string text, float x0, float x1, float y, float cap, GuideColour colour)
        {
            var elements = new List<GuideElement>();
            string upper = (text ?? "").Trim().ToUpperInvariant();
            if (upper.Length == 0)
                return elements;
            string[] words = upper.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float Width(float k)
            {
                float w = 0f;
                for (int i = 0; i < words.Length; i++)
                {
                    if (i > 0)
                        w += WordGapK * cap * k;
                    for (int j = 0; j < words[i].Length; j++)
                        w += (RoadGuideSigns.Wide(words[i][j]) ? cap : LatinWidth(words[i][j].ToString(), cap)) * k + (j > 0 ? TrackK * cap * k : 0f);
                }

                return w;
            }

            // Too long for the line: the letters are made smaller (not narrower: a letter is never drawn squeezed), and stay on the middle of the line.
            float avail = Math.Max(0.01f, x1 - x0);
            float scale = Math.Min(1f, avail / Math.Max(1e-6f, Width(1f)));
            float c = cap * scale, top = y + (cap - c) * 0.5f;
            float x = x0 + (avail - Width(scale)) * 0.5f;
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0)
                    x += WordGapK * c;
                for (int j = 0; j < words[i].Length; j++)
                {
                    if (j > 0)
                        x += TrackK * c;
                    string letter = words[i][j].ToString();
                    // A Chinese character typed into the pinyin stands in a square cell as high as the letters.
                    bool wide = RoadGuideSigns.Wide(words[i][j]);
                    float lw = wide ? c : LatinWidth(letter, c);
                    elements.Add(wide ? Cells(letter, x, top, c, colour, 0f) : Ink(letter, x, top, lw, c, colour, GuideAlign.Centre));
                    x += lw;
                }
            }

            return elements;
        }

        /// <summary>
        /// A number or a short text (figures, letters, characters as they come: RoadGuideSigns.Line) h high, centred in the box x, y, w, h; made smaller (and kept in the middle of the box) when it is wider
        /// than the box.
        /// </summary>
        internal static void Fitted(GuidePart p, string text, float x, float y, float w, float h, GuideColour colour)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return;
            GuideBlock line = RoadGuideSigns.Line(text, h, colour, 0.1f * h);
            if (line.W > w && line.W > 0f)
            {
                float k = w / line.W;
                line = RoadGuideSigns.Line(text, h * k, colour, 0.1f * h * k);
            }

            p.Add(line, x + (w - line.W) * 0.5f, y + (h - line.H) * 0.5f);
        }

        private static void Add(GuidePart p, GuideElement e)
        {
            if (e != null)
                p.Elements.Add(e);
        }

        private static void Add(GuidePart p, List<GuideElement> list)
        {
            foreach (GuideElement e in list)
                p.Elements.Add(e);
        }

        /// <summary>
        /// The name sign of 5.3.1.1 (Figs. B.1 to B.15): the name in the upper three fifths, the pinyin in the lower two, on a board w x h; with direction words (5.3.3, Fig. B.23) at each end of the pinyin,
        /// as high as it; with a pictogram (5.3.2, Fig. B.22) in the left two fifths, the text in the right three.
        /// </summary>
        internal static GuideBoard NameSign(string name, string pinyin, float w, float h, GuideColour ground, GuideColour ink, string ends = "", string symbol = "")
        {
            GuidePart p = Board(w, h, ground);
            float margin = MarginK * h, ch = CharK * h, cap = PinyinK * h;
            float x0 = margin, x1 = w - margin;
            string glyph = RoadGuideSigns.SymbolGlyph(symbol);
            if (glyph != null)
            {
                // The pictogram: a white square in the left two fifths (of the width, Fig. B.22), as high as the board less a margin, the figure in the board's colour.
                float side = Math.Min(w * 0.4f - margin, h - 2f * 0.12f * h);
                float sx = margin * 0.6f, sy = (h - side) * 0.5f;
                p.Elements.Add(Rect(sx, sy, side, side, White, 0.08f * side));
                p.Elements.Add(FitPicture(glyph, sx + 0.08f * side, sy + 0.08f * side, 0.84f * side, 0.84f * side, ground));
                x0 = w * 0.4f + margin * 0.3f;
            }

            Add(p, Spread(name, x0, x1, CharMidK * h - ch * 0.5f, ch, ink));
            float py0 = x0, py1 = x1;
            if (!string.IsNullOrEmpty(ends) && ends.Length == 2)
            {
                // The direction words at the two ends of the pinyin's line, as high as the pinyin, a margin from the edge.
                string a = Word(ends[0]), b = Word(ends[1]);
                Add(p, Cells(a, x0, PinyinTopK * h, cap, ink, 0f));
                Add(p, Cells(b, x1 - cap, PinyinTopK * h, cap, ink, 0f));
                py0 = x0 + cap * 2f;
                py1 = x1 - cap * 2f;
            }

            Add(p, Letters(pinyin, py0, py1, PinyinTopK * h, cap, ink));
            return Single(p, ch);
        }

        private static string Word(char c) => c == 'e' ? "东" : c == 'w' ? "西" : c == 'n' ? "北" : c == 's' ? "南" : "";

        /// <summary>The building plate (5.3.1.2, Fig. B.16): the left three fifths blue with the name (two rows when it is over three characters) and its pinyin, the right two fifths white with the number in red, 350 mm high at least.</summary>
        internal static GuideBoard BuildingSign(string name, string pinyin, string number, float w, float h, bool metal, GuideColour metalInk)
        {
            GuidePart p = Board(w, h, metal ? Plate : Blue);
            GuideColour ink = metal ? metalInk : White, numberInk = metal ? metalInk : Red;
            float left = w * 0.6f;
            if (!metal)
                p.Elements.Add(Rect(left, 0f, w - left, h, White));
            name = (name ?? "").Trim();
            float margin = 0.08f * h;
            if (name.Length > 3)
            {
                int half = (name.Length + 1) / 2;
                float rowH = 0.6f * h / 2f, ch = 0.8f * rowH;
                Add(p, Spread(name.Substring(0, half), margin, left - margin, (rowH - ch) * 0.5f, ch, ink));
                Add(p, Spread(name.Substring(half), margin, left - margin, rowH + (rowH - ch) * 0.5f, ch, ink));
            }
            else
            {
                float ch = 0.4f * h;
                Add(p, Spread(name, margin, left - margin, 0.3f * h - ch * 0.5f, ch, ink));
            }

            float cap = 0.12f * h;
            Add(p, Letters(pinyin, margin, left - margin, 0.6f * h + (0.4f * h - cap) * 0.5f, cap, ink));
            float digits = Math.Max(0.35f, 0.7f * h);
            digits = Math.Min(digits, 0.9f * h);
            Fitted(p, number, left + 0.06f * w, (h - digits) * 0.5f, w - left - 0.12f * w, digits, numberInk);
            return Single(p, digits);
        }

        /// <summary>The door plate (5.3.1.3, Fig. B.17): the street's name in the upper two fifths, the number in the lower three; with the postal code (5.3.4, Fig. B.24) the name in the top fifth, the number in the middle three, "邮政编码" and the code in the last fifth.</summary>
        internal static GuideBoard DoorSign(string street, string number, string postcode, float w, float h, GuideColour ground, GuideColour ink)
        {
            GuidePart p = Board(w, h, ground);
            bool code = !string.IsNullOrWhiteSpace(postcode);
            float nameBand = code ? 0.2f * h : 0.4f * h, numberTop = nameBand, numberBand = 0.6f * h;
            float ch = (code ? 0.6f : 0.55f) * nameBand;
            Add(p, Spread(street, 0.15f * w, 0.85f * w, (nameBand - ch) * 0.5f + (code ? 0.03f * h : 0.04f * h), ch, ink));
            float digits = 0.78f * numberBand;
            Fitted(p, number, 0.1f * w, numberTop + (numberBand - digits) * 0.5f, 0.8f * w, digits, ink);
            if (code)
            {
                float small = 0.42f * 0.2f * h, y = 0.8f * h + (0.2f * h - small) * 0.5f - 0.02f * h;
                float textW = CellWidth(4, small, 0.15f * small), digitsW = Math.Min(0.9f * w - textW - small, RoadGuideSigns.Line(postcode.Trim(), small, ink, 0.1f * small).W);
                float total = textW + small + digitsW, x = (w - total) * 0.5f;
                Add(p, Cells("邮政编码", x, y, small, ink, 0.15f * small));
                Fitted(p, postcode, x + textW + small, y, Math.Max(0.01f, digitsW), small, ink);
            }

            return Single(p, ch);
        }

        /// <summary>The unit plate (5.3.1.4, Fig. B.18): the unit's name in the middle three fifths of the width.</summary>
        internal static GuideBoard UnitSign(string name, float w, float h, GuideColour ground, GuideColour ink)
        {
            GuidePart p = Board(w, h, ground);
            float ch = 0.5f * h;
            Add(p, Spread(name, 0.2f * w, 0.8f * w, (h - ch) * 0.5f, ch, ink));
            return Single(p, ch);
        }

        /// <summary>The floor plate (5.3.1.5, Fig. B.19): a disc, the number of the floor ("1", "B1") in the middle three fifths of its diameter.</summary>
        internal static GuideBoard FloorSign(string floor, float d, GuideColour ground, GuideColour ink)
        {
            var p = new GuidePart { X = 0f, Y = 0f, W = d, H = d, Ground = ground, Shape = GuidePartShape.Circle, Radius = d * 0.5f };
            float cap = 0.55f * d;
            Fitted(p, floor, 0.2f * d, (d - cap) * 0.5f, 0.6f * d, cap, ink);
            return Single(p, cap);
        }

        /// <summary>The upright stone (5.3.1.6, Fig. B.20): the name one character under the other in the upper four fifths, the pinyin across in the lowest fifth.</summary>
        internal static GuideBoard UprightStone(string name, string pinyin, float w, float h, GuideColour ink)
        {
            GuidePart p = Board(w, h, Plate);
            name = (name ?? "").Trim();
            int n = Math.Max(1, name.Length);
            float band = 0.8f * h, ch = Math.Min(0.62f * w, band / (n + (n - 1) * 0.15f + 0.4f));
            float gap = n > 1 ? Math.Min(0.3f * ch, (band - 0.2f * ch - n * ch) / (n - 1)) : 0f;
            // A little lower than the middle of the four fifths (Fig. B.20), never out of them.
            float total = n * ch + (n - 1) * gap, slack = Math.Max(0f, band - total), y = slack * 0.5f + Math.Min(0.03f * h, slack * 0.4f);
            for (int i = 0; i < name.Length; i++)
                Add(p, Cells(name[i].ToString(), (w - ch) * 0.5f, y + i * (ch + gap), ch, ink, 0f));
            float cap = Math.Min(0.06f * h, 0.12f * w);
            Add(p, Letters(pinyin, 0.06f * w, 0.94f * w, 0.8f * h + (0.2f * h - cap) * 0.5f, cap, ink));
            return Single(p, ch);
        }

        // ------------------------------------------------------------------ the catalogue

        public static IEnumerable<GuideTemplate> All()
        {
            // 4.2.1 街、巷 (Table 1: blue for a street that runs east-west, green for one that runs north-south; Table 2: a street 1200 to 1700 x 300 to 700 mm, a lane 460 x 160).
            yield return Make("gb17733.street", "图B.11", "街地名标志（路名牌）", "Street name sign", "jdmbz", W("街", "路名牌", "街牌", "路牌", "地名", "17733", "street"),
                s => NameSign(s.Get("name"), s.Get("py"), Mm(s, "len", 1500, 1200, 1700), Mm(s, "wid", 600, 300, 700), s.Is("run", "ns") ? Green : Blue, White, s.Get("ends")),
                new[]
                {
                    Name("中山街"), Spelling("ZHONGSHAN JIE"), GuideField.Choice("run", "走向（底色）", "Runs (colour)", "ew", kRun), GuideField.Choice("ends", "两端方向（5.3.3）", "Directions at the ends (5.3.3)", "", kEnds),
                    Size("len", "长（mm，1200～1700）", "Length (mm, 1200-1700)", "1500", 4), Size("wid", "宽（mm，300～700）", "Height (mm, 300-700)", "600", 3)
                });
            yield return Make("gb17733.lane", "图B.12", "巷地名标志", "Lane name sign", "xdmbz", W("巷", "胡同", "巷牌", "地名", "17733", "lane", "alley"),
                s => NameSign(s.Get("name"), s.Get("py"), 0.46f, 0.16f, s.Is("run", "ew") ? Blue : Green, White, s.Get("ends")),
                new[] { Name("柳荫巷"), Spelling("LIUYIN XIANG"), GuideField.Choice("run", "走向（底色）", "Runs (colour)", "ns", kRun), GuideField.Choice("ends", "两端方向（5.3.3）", "Directions at the ends (5.3.3)", "", kEnds) });

            // 区片、小区、村 (Table 1: blue, or the base plate of an etched metal sign with black or red; Table 2: 800 to 1700 x 300 to 700 mm).
            foreach ((string id, string code, string zh, string en, string py, string name, string spell, string[] words) in new[]
            {
                ("gb17733.area", "图B.13", "区片地名标志", "District name sign", "qpdmbz", "上清区片", "SHANGQING QUPIAN", W("区片", "地名", "17733", "district")),
                ("gb17733.estate", "图B.14", "小区地名标志", "Residential estate sign", "xqdmbz", "幸福小区", "XINGFU XIAOQU", W("小区", "社区", "地名", "17733", "estate", "residential")),
                ("gb17733.village", "图B.15", "村地名标志", "Village name sign", "cdmbz", "王家庄", "WANGJIAZHUANG", W("村", "庄", "地名", "17733", "village"))
            })
            {
                yield return Make(id, code, zh, en, py, words,
                    s => NameSign(s.Get("name"), s.Get("py"), Mm(s, "len", 1500, 800, 1700), Mm(s, "wid", 600, 300, 700), s.Is("finish", "blue") ? Blue : Plate, s.Is("finish", "blue") ? White : s.Is("finish", "metalred") ? Red : Black),
                    new[]
                    {
                        Name(name), Spelling(spell), GuideField.Choice("finish", "颜色", "Colours", "blue", kFinish),
                        Size("len", "长（mm，800～1700）", "Length (mm, 800-1700)", "1500", 4), Size("wid", "宽（mm，300～700）", "Height (mm, 300-700)", "600", 3)
                    });
            }

            // 门 (5.3.1.3, 5.3.4; Table 2: large 570 x 370, middle 270 x 170, small 150 x 90).
            yield return Make("gb17733.door", "图B.17", "门地名标志（门牌）", "Door number plate", "mdmbz", W("门牌", "门牌号", "地址", "地名", "邮政编码", "17733", "door", "house number"),
                s =>
                {
                    float w = s.Is("size", "s") ? 0.15f : s.Is("size", "m") ? 0.27f : 0.57f, h = s.Is("size", "s") ? 0.09f : s.Is("size", "m") ? 0.17f : 0.37f;
                    bool metal = !s.Is("finish", "blue");
                    return DoorSign(s.Get("street"), s.Get("no"), s.Get("post"), w, h, metal ? Plate : Blue, metal ? (s.Is("finish", "metalred") ? Red : Black) : White);
                },
                new[]
                {
                    GuideField.Text("street", "街区、村名称", "Street or village", "幸福大街", 10), GuideField.Text("no", "门号", "Number", "5", 6),
                    GuideField.Text("post", "邮政编码（可空，5.3.4）", "Postal code (may be empty, 5.3.4)", "", 6),
                    GuideField.Choice("size", "尺寸", "Size", "l", kDoorSizes), GuideField.Choice("finish", "颜色", "Colours", "blue", kFinish)
                });

            // 楼 (5.3.1.2; Table 2: 900 x 500).
            yield return Make("gb17733.building", "图B.16", "楼地名标志（楼牌）", "Building number plate", "ldmbz", W("楼牌", "楼号", "栋", "地名", "17733", "building"),
                s => BuildingSign(s.Get("name"), s.Get("py"), s.Get("no"), 0.9f, 0.5f, !s.Is("finish", "blue"), s.Is("finish", "metalred") ? Red : Black),
                new[] { Name("清风小区", 8), Spelling("QINGFENG XIAOQU"), GuideField.Text("no", "楼号", "Number", "8", 4), GuideField.Choice("finish", "颜色", "Colours", "blue", kFinish) });

            // 楼单元 (5.3.1.4; Table 2: 300 to 400 x 150 to 200), 楼层 (5.3.1.5; a disc of 200 to 400).
            yield return Make("gb17733.unit", "图B.18", "楼单元地名标志", "Building unit plate", "ldydmbz", W("单元", "楼单元", "地名", "17733", "unit", "entrance"),
                s => UnitSign(s.Get("name"), Mm(s, "len", 350, 300, 400), Mm(s, "wid", 175, 150, 200), s.Is("finish", "blue") ? Blue : Plate, s.Is("finish", "blue") ? White : s.Is("finish", "metalred") ? Red : Black),
                new[]
                {
                    GuideField.Text("name", "单元（“一单元”起）", "Unit (from 一单元)", "一单元", 5), GuideField.Choice("finish", "颜色", "Colours", "blue", kFinish),
                    Size("len", "长（mm，300～400）", "Length (mm, 300-400)", "350", 3), Size("wid", "宽（mm，150～200）", "Height (mm, 150-200)", "175", 3)
                });
            yield return Make("gb17733.floor", "图B.19", "楼层地名标志", "Floor plate", "lcdmbz", W("楼层", "层", "地下", "B1", "地名", "17733", "floor", "level"),
                s => FloorSign(s.Get("floor"), Mm(s, "dia", 300, 200, 400), s.Is("finish", "blue") ? Blue : Plate, s.Is("finish", "blue") ? White : s.Is("finish", "metalred") ? Red : Black),
                new[]
                {
                    GuideField.Text("floor", "楼层（1、2…，B1、B2…）", "Floor (1, 2 ..., B1, B2 ...)", "B1", 4), GuideField.Choice("finish", "颜色", "Colours", "blue", kFinish),
                    Size("dia", "直径（mm，200～400）", "Diameter (mm, 200-400)", "300", 3)
                });

            // 4.2.2 行政区域 (blue; 2000 to 7000 x 900 to 2500), 4.2.3 专业区 (green; 1500 to 3000 x 600 to 1200), 4.2.4 设施 (blue; 1500 to 3000 x 800 to 1500), 4.2.5 纪念地和旅游地 (brown; the same),
            // 4.1 海域、水系、地形 (green; 1500 to 5700 x 600 to 1800). A pictogram (5.2.2.1) for special zones, facilities and memorial or tourist places.
            foreach ((string id, string code, string zh, string en, string py, string name, string spell, GuideColour ground, float len, float lenMin, float lenMax, float wid, float widMin, float widMax, bool symbol, string[] words) in new[]
            {
                ("gb17733.admin", "图B.4～B.7", "行政区域地名标志", "Administrative area sign", "xzqydmbz", "平山县", "PINGSHAN XIAN", Blue, 3000f, 2000f, 7000f, 1200f, 900f, 2500f, false, W("省", "市", "县", "区", "乡", "镇", "行政区域", "地名", "17733", "county", "town")),
                ("gb17733.zone", "图B.8", "专业区地名标志", "Special zone sign", "zyqdmbz", "大港开发区", "DAGANG KAIFAQU", Green, 2500f, 1500f, 3000f, 1000f, 600f, 1200f, true, W("开发区", "工业区", "专业区", "地名", "17733", "zone", "industrial")),
                ("gb17733.facility", "图B.9", "设施地名标志", "Facility name sign", "ssdmbz", "金门大桥", "JINMEN DAQIAO", Blue, 2400f, 1500f, 3000f, 1000f, 800f, 1500f, true, W("桥", "车站", "地铁站", "隧道", "环岛", "设施", "地名", "17733", "bridge", "station")),
                ("gb17733.memorial", "图B.10", "纪念地和旅游地地名标志", "Memorial and tourist place sign", "jndhlyddmbz", "五指岩景区", "WUZHIYAN JINGQU", Brown, 2400f, 1500f, 3000f, 1000f, 800f, 1500f, true, W("景区", "公园", "纪念馆", "寺", "旅游地", "地名", "17733", "scenic", "park")),
                ("gb17733.nature", "图B.1～B.3", "海域、水系、地形地名标志", "Sea, water and landform sign", "hysxdxdmbz", "微山湖", "WEISHAN HU", Green, 2500f, 1500f, 5700f, 1000f, 600f, 1800f, false, W("湖", "河", "山", "岛", "海", "地名", "17733", "lake", "river", "mountain"))
            })
            {
                var fields = new List<GuideField>
                {
                    Name(name), Spelling(spell),
                    Size("len", $"长（mm，{lenMin:0}～{lenMax:0}）", $"Length (mm, {lenMin:0}-{lenMax:0})", len.ToString("0"), 4), Size("wid", $"宽（mm，{widMin:0}～{widMax:0}）", $"Height (mm, {widMin:0}-{widMax:0})", wid.ToString("0"), 4)
                };
                if (symbol)
                    fields.Add(GuideField.Choice("sym", "图形符号（5.3.2）", "Pictogram (5.3.2)", "", kSymbols));
                GuideColour g = ground;
                float l0 = len, l1 = lenMin, l2 = lenMax, w0 = wid, w1 = widMin, w2 = widMax;
                yield return Make(id, code, zh, en, py, words, s => NameSign(s.Get("name"), s.Get("py"), Mm(s, "len", l0, l1, l2), Mm(s, "wid", w0, w1, w2), g, White, "", s.Get("sym")), fields.ToArray());
            }

            // 碑碣式 (5.3.1.6, 5.4: the base plate's colour, black or red characters; 5.5.2: upright 300 to 1000 wide x 600 to 2000 high, across 1000 to 2000 x 600 to 1200).
            yield return Make("gb17733.stone.v", "图B.20", "碑碣式地名标志（竖置）", "Name stone (upright)", "bjsdmbz", W("碑", "碑碣", "石碑", "竖", "地名", "17733", "stone", "monument"),
                s => UprightStone(s.Get("name"), s.Get("py"), Mm(s, "len", 600, 300, 1000), Mm(s, "wid", 1400, 600, 2000), s.Is("ink", "r") ? Red : Black),
                new[]
                {
                    Name("大屿岛", 6), Spelling("DAYU DAO"), GuideField.Choice("ink", "字色", "Characters", "k", kInk),
                    Size("len", "宽（mm，300～1000）", "Width (mm, 300-1000)", "600", 4), Size("wid", "高（mm，600～2000）", "Height (mm, 600-2000)", "1400", 4)
                });
            yield return Make("gb17733.stone.h", "图B.21", "碑碣式地名标志（横置）", "Name stone (across)", "bjsdmbz", W("碑", "碑碣", "石碑", "横", "地名", "17733", "stone", "monument"),
                s => NameSign(s.Get("name"), s.Get("py"), Mm(s, "len", 1600, 1000, 2000), Mm(s, "wid", 1000, 600, 1200), Plate, s.Is("ink", "r") ? Red : Black),
                new[]
                {
                    Name("薛家屯", 8), Spelling("XUEJIATUN"), GuideField.Choice("ink", "字色", "Characters", "k", kInk),
                    Size("len", "长（mm，1000～2000）", "Length (mm, 1000-2000)", "1600", 4), Size("wid", "高（mm，600～1200）", "Height (mm, 600-1200)", "1000", 4)
                });
        }
    }
}
