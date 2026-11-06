using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The direction boards for people on foot (0.12.57; the owner's "行人和地铁导向牌"), a group of their own after the place names, beside the street-name plates they hang under:
    /// the metro board outside a station (way.metro, Shanghai DG/TJ draft 城市轨道交通导向标识系统设计规范 9.1.1, figs. 15 and 20: 1640 × 420 mm, blue with a white edge, the lower edge at
    /// least 2.5 m up, an arrow, the GB/T 10001.3 metro sign on white, the line's number on its colour, 号线, Line N and the distance in 50 m steps to 200 m) and the pedestrian guide strip
    /// (way.ped, Hangzhou 城市道路杆件及标识整合设计导则 2015 figs. B.2 and B.3: two plates of 700 × 180 mm side by side with the post between them, a double chevron at the outer end,
    /// the distance, the name with its pinyin under it and a pictogram at the post, the strip's lower edge at 1.9 m). The Shanghai metro logo of the draft's figs. 1 and 2 is not drawn (a
    /// registered mark; its construction drawing is not traced here); the line colours are the draft's fig. A.1's, read off its swatches. Pure.
    /// </summary>
    public static class WayfindingSigns
    {
        public const string Group = "wayfinding";

        public const string MetroId = "way.metro", PedId = "way.ped";

        private const GuideColour Blue = GuideColour.Blue, Green = GuideColour.Green, White = GuideColour.White;

        /// <summary>The metro board (draft 9.1.1.4, fig. 20; mm read off it at 2.076 mm to a pixel, ±10 mm) in metres: its size, the lower edge's height, the gaps and the edge's width.</summary>
        public const float MetroW = 1.640f, MetroH = 0.420f, MetroClearance = 2.5f, MetroEdge = 0.011f, MetroMargin = 0.050f, MetroGap = 0.029f;

        /// <summary>The metro board's pieces (fig. 20): the arrow's width and height, the metro sign's square, the line's box, the characters of 号线, the English line, the distance's box.</summary>
        public const float ArrowW = 0.154f, ArrowH = 0.332f, SignSide = 0.328f, NumberW = 0.195f, NumberH = 0.178f, CharH = 0.104f, EnglishH = 0.070f, DistW = 0.257f, DistH = 0.077f;

        /// <summary>The pedestrian strip (Hangzhou figs. B.2, B.3): each plate 700 × 180 mm, the strip's lower edge at 1.9 m.</summary>
        public const float PlateW = 0.700f, PlateH = 0.180f, PedClearance = 1.9f;

        private static readonly GuideChoice[] kSide = { new GuideChoice("left", "往左", "To the left"), new GuideChoice("right", "往右", "To the right") };

        private static readonly GuideChoice[] kWay =
        {
            new GuideChoice("left", "往左 «", "Left «"), new GuideChoice("right", "往右 »", "Right »"),
            new GuideChoice("up", "往前 ⌃", "Ahead ⌃"), new GuideChoice("down", "往后 ⌄", "Back ⌄")
        };

        private static readonly GuideChoice[] kSymbol =
        {
            new GuideChoice("", "无", "None"), new GuideChoice("metro", "地铁", "Metro"), new GuideChoice("toilet", "公厕", "Toilets"), new GuideChoice("access", "无障碍", "Accessible"),
            new GuideChoice("bridge", "人行天桥", "Footbridge"), new GuideChoice("subway", "人行地道", "Underpass"), new GuideChoice("park", "停车场", "Car park"),
            new GuideChoice("air", "机场", "Airport"), new GuideChoice("fuel", "加油站", "Fuel station"), new GuideChoice("ev", "充电站", "Charging station"), new GuideChoice("ferry", "轮渡", "Ferry")
        };

        private static string[] W(params string[] words) => words;

        private static string GlyphOf(string key)
        {
            switch (key)
            {
                case "metro": return Glyphs.R_Metro;
                case "toilet": return Glyphs.R_Toilet;
                case "access": return Glyphs.R_Wheelchair;
                case "bridge": return Glyphs.R_Footbridge;
                case "subway": return Glyphs.R_Underpass;
                case "park": return Glyphs.R_P;
                case "air": return Glyphs.R_Airplane;
                case "fuel": return Glyphs.R_Fuel;
                case "ev": return Glyphs.R_Charger;
                case "ferry": return Glyphs.R_Ferry;
                default: return null;
            }
        }

        private static GuideTemplate Make(string id, string code, string zh, string en, string pinyin, string[] keywords, Func<GuideSpec, GuideBoard> layout, GuideField[] fields, float clearance) => new GuideTemplate
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
            Layout = layout,
            Clearance = clearance
        };

        public static IEnumerable<GuideTemplate> All()
        {
            yield return Make(MetroId, "上海征求意见稿 9.1.1", "地铁站外导向标识", "Metro board outside a station", "dtzwdxbs", W("地铁", "轨道交通", "站外", "导向", "指引", "号线", "metro", "subway", "station"),
                s => Metro(s.Get("side") != "right", LineOf(s.Get("line")), DistanceOf(s.Get("dist"))),
                new[]
                {
                    GuideField.Choice("side", "箭头方向", "Arrow", "left", kSide), GuideField.Digits("line", "线路（1～23）", "Line (1-23)", "12", 2),
                    GuideField.Digits("dist", "距离（m）", "Distance (m)", "50", 3)
                }, MetroClearance);
            yield return Make(PedId, "杭州导则 B.2、B.3", "步行者导向牌（双向）", "Pedestrian guide strip (two ways)", "bxzdxp", W("步行者", "行人", "导向牌", "指引", "导向", "地铁", "公厕", "景区", "pedestrian", "guide"),
                s => Strip(Half(s, 1), Half(s, 2)),
                new[]
                {
                    GuideField.Text("n1", "左侧：汉字名称", "Left: name", "地铁", 6), GuideField.Text("p1", "左侧：拼音", "Left: pinyin", "DITIE", 20), GuideField.Digits("d1", "左侧：距离（m，0 不标）", "Left: distance (m, 0 for none)", "180", 4),
                    GuideField.Choice("w1", "左侧：箭头", "Left: arrow", "left", kWay), GuideField.Choice("s1", "左侧：图形符号", "Left: pictogram", "metro", kSymbol),
                    GuideField.Text("n2", "右侧：汉字名称", "Right: name", "公厕", 6), GuideField.Text("p2", "右侧：拼音", "Right: pinyin", "GONGCE", 20), GuideField.Digits("d2", "右侧：距离（m，0 不标）", "Right: distance (m, 0 for none)", "340", 4),
                    GuideField.Choice("w2", "右侧：箭头", "Right: arrow", "right", kWay), GuideField.Choice("s2", "右侧：图形符号", "Right: pictogram", "toilet", kSymbol)
                }, PedClearance);
        }

        // ------------------------------------------------------------------ the metro board

        /// <summary>The colours of the lines 1 to 23 of Shanghai (the draft's fig. A.1, read off its swatches; the standard gives them in CMYK).</summary>
        private static readonly byte[][] kLines =
        {
            new byte[] { 220, 30, 37 }, new byte[] { 121, 186, 67 }, new byte[] { 245, 211, 13 }, new byte[] { 61, 36, 114 }, new byte[] { 139, 66, 146 }, new byte[] { 223, 23, 100 },
            new byte[] { 232, 110, 36 }, new byte[] { 6, 139, 204 }, new byte[] { 129, 196, 233 }, new byte[] { 192, 168, 205 }, new byte[] { 123, 17, 24 }, new byte[] { 0, 116, 90 },
            new byte[] { 228, 145, 183 }, new byte[] { 92, 90, 38 }, new byte[] { 185, 162, 124 }, new byte[] { 142, 204, 186 }, new byte[] { 180, 116, 106 }, new byte[] { 189, 146, 66 },
            new byte[] { 242, 165, 114 }, new byte[] { 0, 153, 94 }, new byte[] { 242, 171, 31 }, new byte[] { 84, 40, 105 }, new byte[] { 170, 206, 114 }
        };

        public static int LineOf(string text)
        {
            int n = 12;
            if (!string.IsNullOrEmpty(text) && int.TryParse(text, out int parsed))
                n = parsed;
            return Math.Max(1, Math.Min(kLines.Length, n));
        }

        public static int DistanceOf(string text)
        {
            int m = 50;
            if (!string.IsNullOrEmpty(text) && int.TryParse(text, out int parsed))
                m = parsed;
            return Math.Max(50, Math.Min(200, (int)Math.Round(m / 50.0) * 50));
        }

        /// <summary>The colour of a line as the guide colours have it (GuideColour.Line1 and on).</summary>
        public static GuideColour LineColour(int line) => (GuideColour)((int)GuideColour.Line1 + line - 1);

        public static byte[] LineRgb(int line) => kLines[line - 1];

        /// <summary>The line's figures in white or in black on its colour, by how light the colour is (fig. 20: white on line 12's green, black on line 13's pink): a lightness of 160 of 255 or more takes black.</summary>
        public static bool BlackOn(int line)
        {
            byte[] c = kLines[line - 1];
            return 0.299f * c[0] + 0.587f * c[1] + 0.114f * c[2] >= 160f;
        }

        /// <summary>
        /// The metro board (draft figs. 15 and 20): pointing left, from the left the arrow, the metro sign and the block of the line; pointing right, the block, the metro sign, the arrow, flush
        /// to the right. The block is the line's number on its colour and 号线 on the upper row, Line N and the distance in a white box on the lower. The pieces' sizes are fig. 20's.
        /// </summary>
        private static GuideBoard Metro(bool left, int line, int distance)
        {
            GuidePart p = RoadGuideSigns.Plain(MetroW, MetroH, MetroEdge, Blue);
            float top = (MetroH - ArrowH) * 0.5f, signTop = (MetroH - SignSide) * 0.5f;
            string english = "Line " + line, miles = distance.ToString();
            float upper = NumberW + MetroGap + 2f * CharH + 0.008f;
            GuideBlock dist = Distance(miles, "m", DistH * 0.62f, Blue);
            GuideBlock words = RoadGuideSigns.Line(english, EnglishH, White, 0f);
            float lower = words.W + MetroGap + DistW;
            float block = Math.Max(upper, lower), total = ArrowW + MetroGap * 0.9f + SignSide + MetroGap + block;
            float x = left ? MetroMargin : MetroW - MetroMargin - total;
            float arrowX, signX, blockX;
            if (left)
            {
                arrowX = x;
                signX = x + ArrowW + MetroGap * 0.9f;
                blockX = signX + SignSide + MetroGap;
            }
            else
            {
                blockX = x;
                signX = blockX + block + MetroGap;
                arrowX = signX + SignSide + MetroGap * 0.9f;
            }

            p.Elements.Add(Poly(Chevron(left, arrowX, top, ArrowW, ArrowH, 0.4f * ArrowW), White));
            p.Elements.Add(Rect(signX, signTop, SignSide, SignSide, White, 0.03f * SignSide));
            p.Elements.Add(FitPicture(Glyphs.R_Metro, signX + 0.06f * SignSide, signTop + 0.06f * SignSide, 0.88f * SignSide, 0.88f * SignSide, Blue));
            float rowTop = 0.046f;
            GuideColour box = LineColour(line);
            p.Elements.Add(Rect(blockX, rowTop, NumberW, NumberH, box, 0.01f));
            GuideBlock number = RoadGuideSigns.Line(line.ToString(), 0.125f, BlackOn(line) ? GuideColour.Black : White, 0f);
            p.Add(number, blockX + (NumberW - number.W) * 0.5f, rowTop + (NumberH - number.H) * 0.5f);
            p.Elements.Add(Cells("号线", blockX + NumberW + MetroGap, rowTop + (NumberH - CharH) * 0.5f, CharH, White, 0.008f));
            float lowTop = 0.297f;
            p.Add(words, blockX, lowTop + (DistH - words.H) * 0.5f);
            float boxX = blockX + words.W + MetroGap;
            p.Elements.Add(Rect(boxX, lowTop, DistW, DistH, White, 0.012f));
            p.Add(dist, boxX + (DistW - dist.W) * 0.5f, lowTop + (DistH - dist.H) * 0.5f);
            return Single(p, CharH);
        }

        // ------------------------------------------------------------------ the pedestrian strip

        /// <summary>A chevron's outline in the box (x, y, w, h) pointing to the left or to the right: two slanted bars t thick across.</summary>
        private static float[] Chevron(bool left, float x, float y, float w, float h, float t)
        {
            if (left)
                return new[] { x + w - t, y, x + w, y, x + t, y + h * 0.5f, x + w, y + h, x + w - t, y + h, x, y + h * 0.5f };
            return new[] { x + t, y, x, y, x + w - t, y + h * 0.5f, x, y + h, x + t, y + h, x + w, y + h * 0.5f };
        }

        /// <summary>A double chevron in the square s at (x, y), pointing as asked: two chevrons 0.45 s apart along the way (fig. B.2).</summary>
        private static List<GuideElement> Double(string way, float x, float y, float s, GuideColour colour)
        {
            // Drawn pointing left in the unit square (u along the way, v across), then turned: right is u turned about the middle, up takes (v, u), down (v, 1 - u).
            float[] one = { 0.35f, 0f, 0.55f, 0f, 0.20f, 0.5f, 0.55f, 1f, 0.35f, 1f, 0f, 0.5f };
            var result = new List<GuideElement>();
            foreach (float shift in new[] { 0f, 0.45f })
            {
                var points = new float[one.Length];
                for (int i = 0; i < one.Length; i += 2)
                {
                    float u = one[i] + shift, v = one[i + 1];
                    switch (way)
                    {
                        case "right": points[i] = x + (1f - u) * s; points[i + 1] = y + v * s; break;
                        case "up": points[i] = x + v * s; points[i + 1] = y + u * s; break;
                        case "down": points[i] = x + v * s; points[i + 1] = y + (1f - u) * s; break;
                        default: points[i] = x + u * s; points[i + 1] = y + v * s; break;
                    }
                }

                result.Add(Poly(points, colour));
            }

            return result;
        }

        /// <summary>What one plate of the strip says: its name and pinyin, the distance in metres (0: none), the arrow's way and the pictogram.</summary>
        private struct HalfPlate
        {
            public string Name, Pinyin, Way, Symbol;
            public int Metres;
        }

        private static HalfPlate Half(GuideSpec s, int i)
        {
            int.TryParse(s.Get("d" + i), out int metres);
            return new HalfPlate { Name = (s.Get("n" + i) ?? "").Trim(), Pinyin = s.Get("p" + i), Way = s.Get("w" + i), Symbol = s.Get("s" + i), Metres = Math.Max(0, Math.Min(9999, metres)) };
        }

        /// <summary>
        /// The two plates side by side (fig. B.3), the post between them: the left plate from its outer end the arrow, the distance, the name with the pinyin under it, the pictogram at the post; the
        /// right plate the same the other way round. Each is 700 × 180 mm; the name's characters 0.5 of the plate's height, the pinyin 0.16, the distance 0.22, the pictogram a white square of 0.72.
        /// </summary>
        private static GuideBoard Strip(HalfPlate left, HalfPlate right)
        {
            GuidePart p = PlaceNameSigns.Board(2f * PlateW, PlateH, Green);
            Plate(p, left, false);
            Plate(p, right, true);
            return Single(p, 0.5f * PlateH);
        }

        private static void Plate(GuidePart p, HalfPlate half, bool rightHand)
        {
            float x0 = rightHand ? PlateW : 0f, h = PlateH, margin = 0.025f, arrow = 0.6f * h, picture = 0.72f * h, gap = 0.02f;
            // From the outer end inward: the arrow, the distance, the name; at the post the pictogram.
            float outer = rightHand ? x0 + PlateW - margin : x0 + margin, inner = rightHand ? x0 + margin : x0 + PlateW - margin;
            if (half.Way == "left" || half.Way == "right" || half.Way == "up" || half.Way == "down")
            {
                float ax = rightHand ? outer - arrow : outer;
                foreach (GuideElement e in Double(half.Way, ax, (h - arrow) * 0.5f, arrow, White))
                    p.Elements.Add(e);
            }

            float cursorOuter = rightHand ? outer - arrow - gap : outer + arrow + gap;
            string glyph = GlyphOf(half.Symbol);
            float pictureX = rightHand ? inner + 0.0f : inner - picture;
            float region0 = rightHand ? inner + (glyph != null ? picture + gap : 0f) : cursorOuter, region1 = rightHand ? cursorOuter : inner - (glyph != null ? picture + gap : 0f);
            if (glyph != null)
            {
                float py = (h - picture) * 0.5f;
                p.Elements.Add(Rect(pictureX, py, picture, picture, White, 0.1f * picture));
                p.Elements.Add(FitPicture(glyph, pictureX + 0.1f * picture, py + 0.1f * picture, 0.8f * picture, 0.8f * picture, Green));
            }

            // The distance beside the arrow, the name between it and the pictogram.
            float from = Math.Min(region0, region1), to = Math.Max(region0, region1);
            if (half.Metres > 0)
            {
                GuideBlock distance = Distance(half.Metres.ToString(), "m", 0.22f * h, White);
                float dx = rightHand ? to - distance.W : from;
                p.Add(distance, dx, 0.18f * h + (0.5f * h - distance.H) * 0.5f);
                if (rightHand)
                    to -= distance.W + gap;
                else
                    from += distance.W + gap;
            }

            float ch = 0.5f * h;
            if (half.Name.Length > 0 && to > from)
            {
                GuideElement name = PlaceNameSigns.Spread(half.Name, from, to, 0.07f * h, ch, White);
                if (name != null)
                    p.Elements.Add(name);
            }

            if (!string.IsNullOrWhiteSpace(half.Pinyin) && to > from)
            {
                foreach (GuideElement e in PlaceNameSigns.Letters(half.Pinyin, from, to, 0.66f * h, 0.16f * h, White))
                    p.Elements.Add(e);
            }
        }

        private static GuideBoard Single(GuidePart p, float h)
        {
            var board = new GuideBoard { H = h };
            board.Parts.Add(p);
            return board;
        }
    }
}
