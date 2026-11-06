using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The lane choice board of Chinese cities (0.12.78; the owner's photo of 请选择车道行驶, "路口车道指引牌优化"): the lanes of the road side by side on a blue field, a white dashed line between them and
    /// at both sides, an arrow figure in each lane and, under the field, a strip with the words. Not in the standard (marked 非国标): the field is the lane direction board of 示17 to 示23 (E.66 to E.73:
    /// 75 cm to a lane, 15 cm over, 70 cm high; dashes 5 x 12 cm at 5, 29 and 53 cm; the arrows of Glyphs.MandFigure "lane:..") at the scale that makes a lane 4 h wide (the photo: about 4.1 times the
    /// character height of the words), and the strip is the photo's: white, the words in the ground's colour, 1.7 times their height, spread over the width. What the photo could not give is estimated:
    /// the photo is 276 x 160 px, the arrows in it are slimmer than the standard's, and its proportions were read off it by eye (the lane pitch 289 px, the words 70 px).
    /// </summary>
    public static class LanePickSigns
    {
        public const string Id = "guide.lanepick", Code = "非国标";

        /// <summary>The most lanes the board has (the lane direction board's six).</summary>
        public const int MaxLanes = 6;

        /// <summary>The words the strip has by default, and the most it holds.</summary>
        public const string DefaultWords = "请选择车道行驶";
        public const int MaxWords = 14;

        private static readonly GuideChoice[] kCombos =
        {
            new GuideChoice("a", "直行", "Ahead"), new GuideChoice("l", "左转", "Left"), new GuideChoice("r", "右转", "Right"),
            new GuideChoice("al", "直行和左转", "Ahead and left"), new GuideChoice("ar", "直行和右转", "Ahead and right"), new GuideChoice("lr", "左转和右转", "Left and right"),
            new GuideChoice("alr", "直行、左转和右转", "Ahead, left and right"), new GuideChoice("u", "掉头", "U-turn"), new GuideChoice("ul", "掉头和左转", "U-turn and left")
        };

        /// <summary>The keys of the figures the board can draw (the choices of a lane).</summary>
        public static IEnumerable<string> Combos
        {
            get
            {
                foreach (GuideChoice c in kCombos)
                    yield return c.Value;
            }
        }

        private static readonly GuideChoice[] kCounts =
        {
            new GuideChoice("1", "1 条车道", "1 lane"), new GuideChoice("2", "2 条车道", "2 lanes"), new GuideChoice("3", "3 条车道", "3 lanes"),
            new GuideChoice("4", "4 条车道", "4 lanes"), new GuideChoice("5", "5 条车道", "5 lanes"), new GuideChoice("6", "6 条车道", "6 lanes")
        };

        private static readonly GuideChoice[] kGrounds = { new GuideChoice("blue", "蓝底", "Blue"), new GuideChoice("green", "绿底", "Green") };

        private static readonly GuideChoice[] kStrips = { new GuideChoice("w", "白条蓝字", "White strip"), new GuideChoice("g", "底色白字", "On the ground") };

        /// <summary>The scale of the lane direction board's drawing: metres to a centimetre of it, so that a lane (75 cm) is four character heights wide.</summary>
        public static float Unit(float h) => 4f * h / 75f;

        /// <summary>
        /// How much taller than the lane direction board's 70 cm the field is in the photo's proportions: in the photo the field is 1.19 lane pitches high where 示17 has 0.93, and the arrows run 0.9 of a
        /// pitch where 示19's run 0.73 (read off a 276 px photo: 342 / 288 px and 260 / 288 px, so 1.27 and 1.23). The figures are stretched by it, the dashes and the words are not.
        /// </summary>
        public const float PhotoTall = 1.25f;

        private static readonly GuideChoice[] kForms = { new GuideChoice("p", "照片比例（较高）", "As in the photo (taller)"), new GuideChoice("s", "标准比例（示17）", "As 示17 (the standard)") };

        /// <summary>The height of the strip's characters and of the strip, as a multiple of the character height h.</summary>
        public const float StripK = 1.7f;

        /// <summary>The widest the gap between the strip's characters grows (the photo: 0.7 of a character), and the least it keeps.</summary>
        public const float GapMaxK = 0.8f, GapMinK = 0.2f;

        public static int Lanes(GuideSpec s) => Math.Max(1, Math.Min(MaxLanes, (int)s.Number("n", 3)));

        /// <summary>The height of the characters of the words: h, smaller (to 0.7 h) when the lanes are too narrow for them.</summary>
        public static float WordHeight(string words, float fieldW, float h)
        {
            int n = words.Length;
            if (n <= 1 || !AllWide(words))
                return h;
            float need = CellWidth(n, h, GapMinK * h) + 0.8f * h;
            return need <= fieldW ? h : Math.Max(0.7f * h, h * fieldW / need);
        }

        private static bool AllWide(string text)
        {
            foreach (char c in text)
            {
                if (!RoadGuideSigns.Wide(c))
                    return false;
            }

            return true;
        }

        private static string WordsOf(GuideSpec s) => s.Flag("cap") ? (s.Get("words") ?? "").Trim() : "";

        private static GuideBoard Layout(GuideSpec s, int band)
        {
            float h = RoadGuideSigns.CharHeight(band), u = Unit(h);
            int n = Lanes(s);
            GuideColour ground = s.Is("ground", "green") ? GuideColour.Green : GuideColour.Blue;
            string words = WordsOf(s);
            bool strip = words.Length > 0, onGround = s.Is("strip", "g");
            GuideColour ink = onGround ? GuideColour.White : ground;
            float tall = s.Is("form", "s") ? 1f : PhotoTall;
            float fieldW = (75f * n + 15f) * u, fieldH = 70f * u * tall;

            // The words: Chinese characters in square cells spread over the field (as wide a gap as the photo's at most), anything else as a line of ink; the strip is taller than they are.
            float ch = strip ? WordHeight(words, fieldW, h) : 0f;
            GuideBlock line = null;
            float wordsW = 0f, gap = 0f;
            if (strip)
            {
                if (AllWide(words))
                {
                    int count = words.Length;
                    gap = count > 1 ? Math.Max(GapMinK * ch, Math.Min(GapMaxK * ch, (fieldW - 0.8f * h - count * ch) / (count - 1))) : 0f;
                    wordsW = CellWidth(count, ch, gap);
                }
                else
                {
                    line = RoadGuideSigns.Line(words, ch, ink, GapMinK * ch);
                    wordsW = line.W;
                }
            }

            float innerW = Math.Max(fieldW, strip ? wordsW + 0.8f * h : 0f);
            float stripH = strip ? StripK * ch : 0f;
            float separator = strip && onGround ? 0.1f * h : 0f;
            GuidePart p = RoadGuideSigns.Board(h, innerW, fieldH + stripH + separator, ground);
            float e = p.Edge, x0 = e + (innerW - fieldW) * 0.5f;

            // The dashed lines: at the sides and between the lanes, 5 cm wide, from 5 cm under the top to 5 cm over the bottom, dashes and gaps of 12 cm (E.66: 70 cm high gives the three at 5, 29 and 53).
            foreach ((float y, float length) in RoadGuideSigns.EvenDashes(5f * u, fieldH - 5f * u, 12f * u))
            {
                for (int i = 0; i <= n; i++)
                    Art(p, Rect(x0 + (5f + 75f * i) * u, e + y, 5f * u, length, GuideColour.White));
            }

            // The figure of each lane is the one of the lane direction board, in the frame of its 90 x 70 cm board put at the lane.
            for (int i = 0; i < n; i++)
            {
                GuideElement figure = Picture(Glyphs.MandFigure("lane:" + s.Get("lane" + (i + 1))), x0 + 75f * i * u, e, 90f * u, 90f * u * tall, GuideColour.White);
                GuidePath.Bounds(figure.Contours, out float bx0, out float by0, out float bx1, out float by1);
                figure.X = bx0;
                figure.Y = by0;
                figure.W = bx1 - bx0;
                figure.H = by1 - by0;
                Art(p, figure);
            }

            if (strip)
                AddWords(p, onGround, words, line, wordsW, gap, ch, ink, fieldH, stripH, separator);
            return RoadGuideSigns.Single(p, h);
        }

        private static void AddWords(GuidePart p, bool onGround, string words, GuideBlock line, float wordsW, float gap, float ch, GuideColour ink, float fieldH, float stripH, float separator)
        {
            float e = p.Edge, top = e + fieldH, innerW = p.W - 2f * e;
            if (onGround)
            {
                // A white line parts the words from the lanes; the words are white on the ground.
                p.Elements.Add(Rect(e, top, innerW, separator, GuideColour.White));
                top += separator;
            }
            else
            {
                // A white strip bled over the white border so that no hairline of the ground is left between them (as the bottom bands of 路19).
                // The bleed runs over the border below as well: the painter's bleed leaves the lower edge, and the inside's ground meets the strip there on one anti-aliased edge (a hairline across the strip).
                float b = p.Border;
                GuideElement band = Rect(e, top + b, innerW, p.H - e - top - b, GuideColour.White);
                band.RadiusBottom = p.InnerRadius;
                band.Bleed = b;
                band.BleedBottom = true;
                p.Elements.Add(band);
            }

            float y = top + (stripH - ch) * 0.5f, x = e + (innerW - wordsW) * 0.5f;
            if (line == null)
            {
                p.Elements.Add(Cells(words, x, y, ch, ink, gap));
                return;
            }

            p.Add(line, x, y);
        }

        private static GuideElement Art(GuidePart part, GuideElement element)
        {
            element.Art = true;
            part.Elements.Add(element);
            return element;
        }

        private static string[] W(params string[] words) => words;

        public static IEnumerable<GuideTemplate> All()
        {
            var fields = new List<GuideField> { GuideField.Choice("n", "车道数", "Lanes", "3", kCounts) };
            string[] defaults = { "al", "a", "a", "a", "a", "ar" };
            for (int i = 1; i <= MaxLanes; i++)
            {
                int lane = i;
                fields.Add(GuideField.Choice("lane" + i, "车道" + i + "（左起）", "Lane " + i + " (from the left)", defaults[i - 1], kCombos, s => Lanes(s) >= lane));
            }

            fields.Add(GuideField.Flag("cap", "文字条", "Words strip", true));
            fields.Add(GuideField.Text("words", "文字", "Text", DefaultWords, MaxWords, s => s.Flag("cap")));
            fields.Add(GuideField.Choice("strip", "文字样式", "Style", "w", kStrips, s => s.Flag("cap")));
            fields.Add(GuideField.Choice("form", "比例", "Proportions", "p", kForms));
            fields.Add(GuideField.Choice("ground", "底色", "Ground", "blue", kGrounds));
            yield return StandardSigns.Make(Id, Code, "车道选择牌（请选择车道行驶）", "Lane choice board (the lanes with arrows and a line of words)", "guide", "cdxzp",
                W("车道", "选择车道", "请选择车道行驶", "车道指引", "路口", "导向", "掉头", "lane choice", "lanes"), (spec, band) => Layout(spec, band), fields.ToArray());
        }
    }
}
