using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// 路1+ 连续路口预告（图形式）, the junctions of the road ahead on one board (0.12.78, drawn again in 0.12.80): the road the driver is on is a stem going up, the next one to three junctions are
    /// drawn on it from the nearest at the bottom, each as an at-grade junction (a bar with the right-angled ends of Fig. 205) or a grade-separated one (the bar broken where the stem passes over
    /// it, with the ramps of a diamond or the loop of a half cloverleaf), the names of the roads at the ends of the bars, the name of an interchange beside it, and what the road leads to at the
    /// top with the plate of the road number or expressway beside that. GB 5768.2 draws one junction only (路1, Fig. 205): the road widths, the points and the rounded corners are its, the
    /// rest (the finer road, the heights of the names, the ramps) was read off a photograph of a Guangzhou board and is an estimate. The English lines are typed, as the place-name signs' pinyin is.
    /// </summary>
    public static partial class RoadGuideSigns
    {
        public const string MultiId = "gb.guide8.01c";

        /// <summary>The most junctions the board shows.</summary>
        public const int MultiMax = 3;

        /// <summary>How many characters a name of the board holds, and how many letters an English line (the spec's 480 bytes decide when the whole board is full).</summary>
        public const int MultiNameMax = 10, MultiEnglishMax = 26;

        /// <summary>The least width of the board over its height: a landscape board; the content may make it wider.</summary>
        private const float MultiAspect = 1.35f;

        internal static readonly GuideChoice[] kMultiCounts =
        {
            new GuideChoice("1", "1 个", "1"),
            new GuideChoice("2", "2 个", "2"),
            new GuideChoice("3", "3 个", "3")
        };

        internal static readonly GuideChoice[] kMultiKinds =
        {
            new GuideChoice("p", "平面路口", "At-grade junction"),
            new GuideChoice("l", "互通式立交（环形匝道）", "Interchange (loop ramp)"),
            new GuideChoice("d", "互通式立交（菱形匝道）", "Interchange (diamond ramps)")
        };

        internal static readonly GuideChoice[] kMultiSides =
        {
            new GuideChoice("r", "右侧有岔路", "A road on the right"),
            new GuideChoice("l", "左侧有岔路", "A road on the left"),
            new GuideChoice("b", "左右都有", "Roads on both sides")
        };

        /// <summary>The key of a junction's setting: its number counts from 1, the nearest.</summary>
        public static string MultiKey(string name, int junction) => name + junction;

        private static Func<GuideSpec, bool> MultiShown(int i, Func<GuideSpec, bool> more = null) => s => s.Number("n", MultiMax) >= i && (more == null || more(s));

        internal static bool MultiLeft(GuideSpec s, int i) => !s.Is(MultiKey("b", i), "r");

        internal static bool MultiRight(GuideSpec s, int i) => !s.Is(MultiKey("b", i), "l");

        internal static bool MultiInterchange(GuideSpec s, int i) => !s.Is(MultiKey("k", i), "p");

        private static IEnumerable<GuideField> MultiJunctionFields(int i, string kind, string left, string leftEn, string right, string rightEn, string name, string nameEn, string side)
        {
            string n = i == 1 ? "（最近）" : "";
            yield return GuideField.Choice(MultiKey("k", i), "形式", "Kind", kind, kMultiKinds, MultiShown(i)).In($"第 {i} 个路口{n}", i == 1 ? "Junction 1 (nearest)" : "Junction " + i);
            yield return GuideField.Choice(MultiKey("b", i), "岔路", "Roads", side, kMultiSides, MultiShown(i));
            yield return GuideField.Text(MultiKey("lz", i), "左侧路名", "Left name", left, MultiNameMax, MultiShown(i, s => MultiLeft(s, i)));
            yield return GuideField.Text(MultiKey("le", i), "左侧英文", "Left English", leftEn, MultiEnglishMax, MultiShown(i, s => MultiLeft(s, i)));
            yield return GuideField.Text(MultiKey("rz", i), "右侧路名", "Right name", right, MultiNameMax, MultiShown(i, s => MultiRight(s, i)));
            yield return GuideField.Text(MultiKey("re", i), "右侧英文", "Right English", rightEn, MultiEnglishMax, MultiShown(i, s => MultiRight(s, i)));
            yield return GuideField.Text(MultiKey("xz", i), "立交名称", "Interchange name", name, MultiNameMax, MultiShown(i, s => MultiInterchange(s, i)));
            yield return GuideField.Text(MultiKey("xe", i), "立交英文", "Interchange English", nameEn, MultiEnglishMax, MultiShown(i, s => MultiInterchange(s, i)));
        }

        /// <summary>The settings of 路1+; the defaults are the photograph's content (the owner's example).</summary>
        internal static GuideField[] MultiFields()
        {
            var fields = new List<GuideField>
            {
                GuideField.Text("road", "本路路名", "This road", "科韵路", MultiNameMax),
                GuideField.Choice("n", "前方路口数", "Junctions shown", "3", kMultiCounts)
            };
            fields.AddRange(MultiJunctionFields(1, "p", "", "", "花城大道", "Huacheng Ave.", "", "", "r"));
            fields.AddRange(MultiJunctionFields(2, "p", "黄埔大道西", "Huangpu Ave.W.", "黄埔大道东", "Huangpu Ave.E.", "", "", "b"));
            fields.AddRange(MultiJunctionFields(3, "l", "", "", "", "", "中山一立交", "Zhongshan 1st Interchange", "r"));
            fields.Add(GuideField.Text("dz", "前方道路", "Leads to", "中山大道", MultiNameMax).In("前方", "Ahead"));
            fields.Add(GuideField.Text("de", "前方英文", "Leads to, English", "Zhongshan Ave.", MultiEnglishMax));
            fields.Add(GuideField.Text("cz", "路牌（道路编号或快速路）", "Plate (road number or expressway)", "广园快速", 8));
            fields.Add(GuideField.Text("ce", "路牌英文", "Plate, English", "Guangyuan Expwy.", 24, s => s.Get("cz").Length > 0 && !kCode.IsMatch(s.Get("cz"))));
            fields.Add(DirField("北"));
            return fields.ToArray();
        }

        // ------------------------------------------------------------------ names

        /// <summary>The height of the names at the ends of the roads over the character height, the least they are shrunk to, and the widest a name may be (in character heights) before it is wrapped.</summary>
        private const float MultiNameK = 0.75f, MultiNameFloorK = 0.6f, MultiNameWidthK = 4.4f, MultiLatinWidthK = 6f;

        private static readonly string[] kMultiSuffixes = { "立交", "大道", "公路", "大街", "高速", "快速", "路", "街", "道", "桥", "巷" };

        /// <summary>Where a long name of characters is broken into two lines: the index where the second begins, a generic word of a road (路, 大道, 立交...) beginning the second when one is near the middle; 0 for a name not to be broken.</summary>
        internal static int MultiBreak(string text)
        {
            text = (text ?? "").Trim();
            int n = text.Length;
            if (n < 5 || text.IndexOf('[') >= 0 || text.IndexOf('［') >= 0 || text.IndexOf('【') >= 0)
                return 0;
            foreach (char c in text)
            {
                if (!Wide(c))
                    return 0;
            }

            int mid = n / 2, best = mid;
            for (int d = 0; d <= 1; d++)
            {
                foreach (int p in new[] { mid + d, mid - d })
                {
                    if (p < 2 || p > n - 1)
                        continue;
                    foreach (string tail in kMultiSuffixes)
                    {
                        if (string.CompareOrdinal(text, p, tail, 0, tail.Length) == 0 && p + tail.Length == n)
                            return p;
                    }
                }
            }

            return best;
        }

        private static GuideBlock Latin(string text, float height, GuideAlign align, GuideColour colour = GuideColour.White)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return null;
            float w = LatinWidth(text, height);
            var block = new GuideBlock(w, height);
            block.Add(Ink(text, 0f, 0f, w, height, colour, align, "Hg"));
            return block;
        }

        private static GuideAlign AlignOf(float align) => align >= 1f ? GuideAlign.Right : align <= 0f ? GuideAlign.Left : GuideAlign.Centre;

        /// <summary>An English line of at most maxW wide: shrunk to 0.3 h at the least, then broken at the space nearest the middle into two lines.</summary>
        private static GuideBlock MultiLatin(string text, float height, float h, float align, float maxW)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return null;
            float floor = 0.3f * h;
            float ph = height;
            while (ph > floor && LatinWidth(text, ph) > maxW)
                ph = Math.Max(floor, ph * 0.92f);
            if (LatinWidth(text, ph) <= maxW)
                return Latin(text, ph, AlignOf(align));
            int mid = text.Length / 2, at = -1;
            for (int d = 0; d < text.Length && at < 0; d++)
            {
                if (mid - d > 0 && text[mid - d] == ' ')
                    at = mid - d;
                else if (mid + d < text.Length - 1 && text[mid + d] == ' ')
                    at = mid + d;
            }

            if (at < 0)
                return Latin(text, ph, AlignOf(align));
            return Column(0.1f * ph, align, Latin(text.Substring(0, at), ph, AlignOf(align)), Latin(text.Substring(at + 1), ph, AlignOf(align)));
        }

        /// <summary>
        /// A name at the end of a road, never cut: the name at 0.75 h; when it is wider than the room it is shrunk to 0.6 h, and a name that is still too wide is broken into two lines at a word of
        /// a road (the two at 0.75 h, or at 0.6 h when that is too wide); its English line under it, half as high, shrunk and broken the same way.
        /// </summary>
        internal static GuideBlock MultiName(string zh, string en, float h, float align)
        {
            zh = (zh ?? "").Trim();
            float maxW = MultiNameWidthK * h;
            GuideBlock name = Info(zh, MultiNameK * h, Blue);
            float used = MultiNameK;
            if (name.W > maxW)
            {
                name = Info(zh, MultiNameFloorK * h, Blue);
                used = MultiNameFloorK;
            }

            int cut = name.W > maxW ? MultiBreak(zh) : 0;
            if (cut > 0)
            {
                foreach (float k in new[] { MultiNameK, MultiNameFloorK })
                {
                    GuideBlock a = Info(zh.Substring(0, cut), k * h, Blue), b = Info(zh.Substring(cut), k * h, Blue);
                    name = Column(0.12f * k * h, align, a, b);
                    used = k;
                    if (name.W <= maxW)
                        break;
                }
            }

            return Column(0.12f * h, align, name, MultiLatin(en, 0.5f * used * h, h, align, Math.Max(maxW, MultiLatinWidthK * h)));
        }

        /// <summary>The plate beside the destination: a road number as its plate, any other name white on a green plate with a white edge and its English line under it.</summary>
        private static GuideBlock MultiPlate(string zh, string en, float h)
        {
            zh = (zh ?? "").Trim();
            en = (en ?? "").Trim();
            if (zh.Length == 0)
                return null;
            if (kCode.IsMatch(zh))
                return Info(zh, 0.8f * h, Blue);
            float ch = 0.6f * h;
            GuideBlock line = Line(zh, ch, White, NameGapK * ch);
            GuideBlock latin = MultiLatin(en, 0.3f * h, h, 0.5f, Math.Max(line.W, 4.6f * h));
            float padX = 0.3f * ch, padY = 0.25f * ch;
            float inner = Math.Max(line.W, latin != null ? latin.W : 0f);
            float w = inner + 2f * padX, hh = 2f * padY + line.H + (latin != null ? 0.1f * ch + latin.H : 0f);
            var plate = new GuideBlock(w, hh);
            GuideElement body = Rect(0f, 0f, w, hh, Green, 0.2f * ch);
            body.StrokeWidth = 0.06f * ch;
            body.Stroke = White;
            plate.Add(body);
            plate.Add(line, (w - line.W) * 0.5f, padY);
            if (latin != null)
                plate.Add(latin, (w - latin.W) * 0.5f, padY + line.H + 0.1f * ch);
            return plate;
        }

        // ------------------------------------------------------------------ the junctions

        /// <summary>One junction as the layout sees it: what stands at its ends and how far it reaches up and down from the bar.</summary>
        private sealed class MultiStop
        {
            public char Kind;
            public bool Left, Right;
            public GuideBlock LeftBlock, RightBlock, NameBlock;

            /// <summary>The interchange's name stands beside the stem on the side that has no road (true), else it stands under the name of the road on the left.</summary>
            public bool NameBesideStem;
            public float Arm, Up, Down;
        }

        private static MultiStop MultiStopOf(GuideSpec s, int i, float h, float w)
        {
            var stop = new MultiStop { Kind = s.Get(MultiKey("k", i))[0], Left = MultiLeft(s, i), Right = MultiRight(s, i) };
            stop.Arm = MultiArmOf(stop.Kind) * h;
            if (stop.Left)
                stop.LeftBlock = MultiName(s.Get(MultiKey("lz", i)), s.Get(MultiKey("le", i)), h, 1f);
            if (stop.Right)
                stop.RightBlock = MultiName(s.Get(MultiKey("rz", i)), s.Get(MultiKey("re", i)), h, 0f);
            if (stop.Kind != 'p')
            {
                GuideBlock name = MultiName(s.Get(MultiKey("xz", i)), s.Get(MultiKey("xe", i)), h, stop.Left && !stop.Right ? 0f : 1f);
                if (name.W > 0f)
                {
                    stop.NameBesideStem = !(stop.Left && stop.Right);
                    if (stop.NameBesideStem)
                        stop.NameBlock = name;
                    else
                        stop.LeftBlock = Column(0.25f * MultiNameK * h, 1f, stop.LeftBlock, name);
                }
            }

            MultiExtent(stop.Kind, w, h, out float up, out float down);
            float half = Math.Max(stop.LeftBlock != null ? stop.LeftBlock.H : 0f, stop.RightBlock != null ? stop.RightBlock.H : 0f) * 0.5f;
            stop.Up = Math.Max(up, half);
            stop.Down = Math.Max(down, half);
            if (stop.NameBlock != null)
            {
                // The name is level with the loop's centre (a diamond's: the bar): it reaches half its height above and below that.
                float centre = stop.Kind == 'l' ? MultiLoopRadius * h : 0f;
                stop.Up = Math.Max(stop.Up, centre + stop.NameBlock.H * 0.5f);
                stop.Down = Math.Max(stop.Down, stop.NameBlock.H * 0.5f - centre);
            }

            return stop;
        }

        /// <summary>
        /// 路1+ : the road the driver is on, up from the foot of the board, with the junctions ahead along it, the nearest at the bottom; what it leads to above the last. Every road is one width,
        /// a grade-separated junction's bar is broken under the stem, the names are 0.75 h, the board follows its content (at least 1.35 times as wide as it is high) and keeps the direction
        /// mark's corner free.
        /// </summary>
        private static GuideBoard MultiJunction(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            float w = MultiRoadK * h, hw = w * 0.5f, gap = 0.4f * h, fillet = 0.2f * h, pass = MultiPassGap * h;
            int count = Math.Max(1, Math.Min(MultiMax, (int)s.Number("n", MultiMax)));
            var stops = new List<MultiStop>();
            for (int i = 1; i <= count; i++)
                stops.Add(MultiStopOf(s, i, h, w));

            // From the nearest up: the bars' heights, then the foot below the first and the tip above the last.
            var bars = new float[count];
            for (int i = 1; i < count; i++)
                bars[i] = bars[i - 1] - (stops[i - 1].Up + gap + stops[i].Down);
            float foot = stops[0].Down + 0.55f * h;
            float tip = bars[count - 1] - stops[count - 1].Up - 0.7f * h;

            var scene = new Scene();
            var roads = new List<float[]> { MultiStem(foot, tip, w) };
            for (int i = 0; i < count; i++)
            {
                MultiStop stop = stops[i];
                float y = bars[i];
                bool separated = stop.Kind != 'p';
                foreach (int side in new[] { -1, 1 })
                {
                    bool has = side < 0 ? stop.Left : stop.Right;
                    if (!has)
                        continue;
                    MultiBar(roads, side, y, stop.Arm, w, fillet, separated, pass);
                    if (stop.Kind == 'd')
                        MultiDiamond(roads, side, y, w, h);
                    else if (stop.Kind == 'l')
                        MultiLoop(roads, side, y, w, h);
                }

                float tipGap = 0.3f * h;
                if (stop.Left && stop.LeftBlock != null && stop.LeftBlock.W > 0f)
                    scene.Add(stop.LeftBlock, -stop.Arm - tipGap - stop.LeftBlock.W, y - stop.LeftBlock.H * 0.5f);
                if (stop.Right && stop.RightBlock != null && stop.RightBlock.W > 0f)
                    scene.Add(stop.RightBlock, stop.Arm + tipGap, y - stop.RightBlock.H * 0.5f);
                if (stop.NameBlock != null)
                {
                    // Beside the stem on the side that has no road, level with the loop's centre (a diamond's: with the bar), a clear gap from the stem and from the ring.
                    float beside = hw + 0.5f * h, centre = stop.Kind == 'l' ? y - MultiLoopRadius * h : y;
                    if (stop.Left)
                        scene.Add(stop.NameBlock, beside + (stop.Kind == 'l' ? 2f * MultiLoopRadius * h : 0f), centre - stop.NameBlock.H * 0.5f);
                    else
                        scene.Add(stop.NameBlock, -beside - stop.NameBlock.W, centre - stop.NameBlock.H * 0.5f);
                }
            }

            scene.Items.Insert(0, Figure(roads, White));

            // Above the stem: what it leads to, centred on it, and the plate to the right of that.
            GuideBlock lead = Column(0.12f * h, 0.5f, Info(s.Get("dz"), h, Blue), MultiLatin(s.Get("de"), 0.5f * h, h, 0.5f, 6f * h));
            GuideBlock plate = MultiPlate(s.Get("cz"), s.Get("ce"), h);
            float top = tip - 0.3f * h, rowH = Math.Max(lead.H, plate != null ? plate.H : 0f);
            if (lead.W > 0f)
                scene.Add(lead, -lead.W * 0.5f, top - rowH + (rowH - lead.H) * 0.5f);
            if (plate != null)
                scene.Add(plate, lead.W > 0f ? lead.W * 0.5f + 0.8f * h : -plate.W * 0.5f, top - rowH + (rowH - plate.H) * 0.5f);

            // Below it: the road the driver is on.
            GuideBlock own = Info(s.Get("road"), 0.7f * h, Blue);
            if (own.W > 0f)
                scene.Add(own, -own.W * 0.5f, foot + 0.25f * h);

            string dir = s.Get("dir");
            float ml = 0.7f * h, mt = 0.5f * h;
            scene.Bounds(out float x0, out float y0, out float x1, out float y1);
            float minW = MultiAspect * (y1 - y0 + 2f * mt + (dir.Length > 0 ? 0.3f * h : 0f));
            GuidePart p = Fit(scene, h, Blue, ml, mt, ml, mt, minW, 0f, dir.Length > 0 ? DirBoxK * h : 0f);
            AddDirMark(p, h, dir);
            GuideBoard board = Single(p, h);
            // The settings fill their field: a text typed now loses its end (GuideSpec.Build), which the editor says in one line (Bilingual.Notes shows the report).
            if (s.Full)
                board.English = new BilingualReport { SpecFull = true };
            return board;
        }

        internal static GuideTemplate MultiJunctionTemplate() => StandardSigns.Make(MultiId, "路1+", "连续路口预告（图形式）", "Junctions ahead (diagram)", G, "lxlkyg",
            W("连续路口", "多个路口", "预告", "立交", "互通", "平面路口", "路口", "junctions", "interchange", "sequence"), MultiJunction, MultiFields());
    }
}
