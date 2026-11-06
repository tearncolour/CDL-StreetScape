using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static CDLC.TS.Geo.GuideKit;
using static CDLC.TS.Geo.Signs.RoadGuideSigns;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// Signs of the expressway guide chapter of GB 5768.2-2022 (shared by the expressway and the urban expressway): 路29 to 路32, the entrance advance boards (9.3.1), set on the ordinary
    /// road before an entrance to an expressway or an urban expressway. 9.3.1.1: the first line is the number of the expressway entered (or the name of the urban expressway), the second
    /// the direction it leads to, A-layer places: one place followed by 方向 for one direction (路30), two places for two (路29). 9.3.1.3: 500 m before the reference point a board with
    /// the distance and the arrow ahead (figures a); at the reference point a board with the arrow of the direction to drive (figures b and c). 路31: an unnumbered road, its name in green
    /// on a white band at the top (Fig. 252). 9.3.1.4, 路32: two expressways that share the section entered, both numbers side by side; when the shared section is short, the places of
    /// each road in two columns parted by a vertical line (Fig. 253 d to f, the line parts the two directions).
    /// Laid out in Table 3's character height h by the drawings E.194 (路29), E.195 (路30), E.196 (路31) and E.197 (路32): a board 6 h wide (6.4 h with two columns), edge 0.2 h
    /// (Fig. 3); from the inside's top 0.4 h, the expressway plate of Appendix C 100 cm high (70 below 71 km/h, table C.1), 0.4 h, the places in characters h high, then a last zone
    /// 1.8 h high with 入口 in characters 0.7 h high.
    /// </summary>
    public static class ExpresswayEntrySigns
    {
        private const string G = "guide";
        private const GuideColour Green = GuideColour.Green, White = GuideColour.White;

        // ------------------------------------------------------------------ sizes, in h (E.194 to E.197)

        /// <summary>The inside of a 6 h board without its 0.2 h edge: the least width of the inside (E.194 to E.196; E.197 a to c).</summary>
        private const float MinInnerK = 5.6f;

        /// <summary>The least distance from what is written to the side of the inside (Table 6; E.194 a: 0.4 h beside the places).</summary>
        private const float MarginK = 0.4f;

        /// <summary>From the inside's top to the plate, and from the plate to the places (E.194 to E.197: 0.4 h, 100 (70), 0.4 h).</summary>
        private const float PlateTopK = 0.4f, PlateGapK = 0.4f;

        /// <summary>The gap between the characters of a place of a two-place line (E.194, E.196, E.197: 1 h, 0.1 h, 1 h) and of the one-place line with 方向 (E.195: four characters 0.2 h apart).</summary>
        private const float PairCharGapK = 0.1f, SingleCharGapK = 0.2f;

        /// <summary>Between the two places of a line (E.194 a: 0.6 h).</summary>
        private const float PlaceGapK = 0.6f;

        /// <summary>
        /// The last zone, from under the places to the inside's bottom: 0.5 h, 入口 0.7 h high (its two characters 0.1 h apart), 0.6 h (E.194 to E.196 and E.197 a to c; E.197 d to f give
        /// 0.6 h, 0.7 h, 0.6 h on the left and 0.4 h, 1 h, 0.4 h on the right of the same drawing, 1.9 h against 1.8 h: the 1.8 h of the other drawings is used).
        /// </summary>
        private const float ZoneK = 1.8f, EntryCharK = 0.7f, EntryTopK = 0.5f, EntryGapK = 0.1f;

        /// <summary>
        /// Figure a (E.194 a, E.195 a, E.196 a, E.197 a, d): 0.2 h, 入口, 0.2 h, the digits h high whose foot is 0.4 h over the inside's bottom, the unit, 0.3 h, the arrow ahead 1 h wide and 1.2 h
        /// high, 0.3 h under the zone's top, 0.2 h; the row is centred (E.197 d: the same row on a 6.4 h board has 0.4 h at its ends).
        /// </summary>
        private const float AheadMarginK = 0.2f, AheadDistGapK = 0.2f, AheadArrowGapK = 0.3f, AheadArrowW = 1f, AheadArrowH = 1.2f, AheadArrowTopK = 0.3f, DigitsTopK = 0.4f;

        /// <summary>Figures b and c (E.194 b, c and the like): 0.4 h, 入口, 0.8 h, the arrow 2.5 h long with a head 1 h high, 0.4 h under the zone's top, 0.4 h; centred (E.197 e, f: 0.6 h at the ends of a 6.4 h board).</summary>
        private const float SideMarginK = 0.4f, SideGapK = 0.8f, SideArrowL = 2.5f, SideArrowH = 1f, SideArrowTopK = 0.4f;

        /// <summary>
        /// The direction mark beside the plate (E.195: 0.2 h to the right of the plate, a square 1 h, centred on the plate's height; Fig. 251: white, the character in green, about 0.8 of the
        /// square: the character's size is estimated from the printed figure, the drawing does not give it).
        /// </summary>
        private const float MarkGapK = 0.2f, MarkK = 1f, MarkCharK = 0.8f;

        /// <summary>The two plates of 路32 side by side: 0.3 h apart for one place line (E.197 a: 100 (70), 0.3 h, 100 (70)), 0.4 h with two columns (E.197 d: 125 (85), 0.4 h, 125 (85)).</summary>
        private const float PlatesGapK = 0.3f, PlatesGapColumnsK = 0.4f;

        /// <summary>The white band of 路31 (E.196: from the liner 0.5 h, the name h high, 0.5 h; the border's 0.1 h is in the first: 0.4 h, h, 0.5 h from the inside's top), the name's characters 0.1 h apart.</summary>
        private const float BandK = 1.9f, BandNameTopK = 0.4f, BandCharGapK = 0.1f;

        /// <summary>Two columns (E.197 d: 0.4 h, the left place, 0.4 h, a white bar 0.2 h wide, 0.4 h, the right place, 0.4 h; the rows 0.4 h apart; the bar as high as the rows).</summary>
        private const float ColumnGapK = 0.4f, BarK = 0.2f, RowGapK = 0.4f;

        // ------------------------------------------------------------------ fields

        private static readonly GuideChoice[] kForms =
        {
            new GuideChoice("a", "a) 500 m 入口预告（距离、箭头）", "a) 500 m advance board (distance, arrow)"),
            new GuideChoice("b", "b) 基准点处，向左（行车方向指引）", "b) At the reference point, to the left"),
            new GuideChoice("c", "c) 基准点处，向右（行车方向指引）", "c) At the reference point, to the right")
        };

        private static readonly GuideChoice[] kDirs =
        {
            new GuideChoice("", "不设", "None"),
            new GuideChoice("东", "东", "East"),
            new GuideChoice("南", "南", "South"),
            new GuideChoice("西", "西", "West"),
            new GuideChoice("北", "北", "North")
        };

        private static readonly GuideChoice[] kGroups =
        {
            new GuideChoice("1", "一组地名（图 253 a～c）", "One line of places (Fig. 253 a to c)"),
            new GuideChoice("2", "两列地名，竖线隔开（图 253 d～f）", "Two columns split by a line (Fig. 253 d-f)")
        };

        private static bool Advance(GuideSpec s) => s.Is("form", "a");

        /// <summary>The form, the distance and its unit: the settings every entrance board has (9.3.1.3: the 500 m board and the board at the reference point).</summary>
        private static IEnumerable<GuideField> FormFields()
        {
            yield return GuideField.Choice("form", "版面（图 a、b、c）", "Board (figure a, b, c)", "a", kForms);
            yield return GuideField.Digits("dist", "距离（距基准点）", "Distance (to the reference point)", "500", 4, Advance);
            yield return GuideField.Choice("unit", "单位", "Unit", "m", GuideTemplates.kUnits, s => Advance(s) && s.Get("dist").Length > 0);
        }

        private static GuideField CodeField(string key, string zh, string en, string def) => GuideField.Text(key, zh, en, def, 6);

        private static GuideField PlaceIf(string key, string zh, string en, string def, Func<GuideSpec, bool> visible) => GuideField.Text(key, zh, en, def, 7, visible);

        // ------------------------------------------------------------------ blocks

        /// <summary>The same reading of a road number as RoadGuideSigns.Info (8.1.5: G15, G1511, 皖S30, G105...), to know whether a place is drawn as a plate or in characters.</summary>
        private static readonly Regex kCode = new Regex(@"^([⺀-鿿]?)([GSXYZCgsxyzc])([0-9]{1,4})$");

        private static bool IsCode(string text) => kCode.IsMatch((text ?? "").Trim());

        /// <summary>A place of the second line: a road number as its plate (Info, 8.1.5), otherwise the name in white characters h high, gap h apart.</summary>
        private static GuideBlock PlaceBlock(string text, float h, float gap)
        {
            text = (text ?? "").Trim();
            return IsCode(text) ? Info(text, h, Green) : Line(text, h, White, gap * h);
        }

        private static bool Empty(GuideBlock b) => b == null || b.W <= 0f;

        /// <summary>Blocks side by side, gap apart, centred on the tallest; empty ones are left out.</summary>
        private static GuideBlock Row(float gap, params GuideBlock[] blocks)
        {
            float w = 0f, hh = 0f;
            int n = 0;
            foreach (GuideBlock b in blocks)
            {
                if (Empty(b))
                    continue;
                w += (n++ > 0 ? gap : 0f) + b.W;
                hh = Math.Max(hh, b.H);
            }

            var row = new GuideBlock(w, n > 0 ? hh : 0f);
            float x = 0f;
            n = 0;
            foreach (GuideBlock b in blocks)
            {
                if (Empty(b))
                    continue;
                if (n++ > 0)
                    x += gap;
                row.Add(b, x, (hh - b.H) * 0.5f);
                x += b.W;
            }

            return row;
        }

        /// <summary>The first line: the plates (or what is typed, drawn by Info) side by side, and the direction mark 0.2 h to their right, centred on their height (E.195).</summary>
        private static GuideBlock PlateLine(float h, float gap, string dir, params string[] codes)
        {
            var plates = new List<GuideBlock>();
            foreach (string code in codes)
                plates.Add(Info(code, h, Green));
            GuideBlock line = Row(gap * h, plates.ToArray());
            if (string.IsNullOrEmpty(dir))
                return line;

            float s = MarkK * h, c = MarkCharK * h;
            var mark = new GuideBlock(s, s);
            mark.Add(Rect(0f, 0f, s, s, White));
            mark.Add(Cells(dir, (s - c) * 0.5f, (s - c) * 0.5f, c, Green, 0f));
            return Row(MarkGapK * h, line, mark);
        }

        /// <summary>The second line with one or two places: two, 0.6 h apart, their characters 0.1 h apart (E.194 a); one, with 方向 after it when asked (9.3.1.1), its characters 0.2 h apart (E.195 a).</summary>
        private static GuideBlock PlaceLine(float h, string first, string second, bool direction)
        {
            first = (first ?? "").Trim();
            second = (second ?? "").Trim();
            if (first.Length > 0 && second.Length > 0)
                return Row(PlaceGapK * h, PlaceBlock(first, h, PairCharGapK), PlaceBlock(second, h, PairCharGapK));
            string only = first.Length > 0 ? first : second;
            if (only.Length > 0 && direction && !IsCode(only))
                only += "方向";
            return PlaceBlock(only, h, SingleCharGapK);
        }

        /// <summary>
        /// The places of 路32 d to f (E.197 d): the places of each direction in a column, the first row the places on the first road, the second those on the second (Fig. 253 note 3); the
        /// columns 0.4 h from a white bar 0.2 h wide as high as the rows (note 2: the line parts the two directions). A place is centred in its column. With one column only, no bar.
        /// </summary>
        private static GuideBlock Columns(float h, string l1, string l2, string r1, string r2)
        {
            GuideBlock a1 = PlaceBlock(l1, h, PairCharGapK), a2 = PlaceBlock(l2, h, PairCharGapK);
            GuideBlock b1 = PlaceBlock(r1, h, PairCharGapK), b2 = PlaceBlock(r2, h, PairCharGapK);
            float leftW = Math.Max(a1.W, a2.W), rightW = Math.Max(b1.W, b2.W);
            var rows = new List<(GuideBlock left, GuideBlock right)>();
            if (!Empty(a1) || !Empty(b1))
                rows.Add((a1, b1));
            if (!Empty(a2) || !Empty(b2))
                rows.Add((a2, b2));
            if (rows.Count == 0)
                return new GuideBlock(0f, 0f);

            bool both = leftW > 0f && rightW > 0f;
            float rightX = both ? leftW + 2f * ColumnGapK * h + BarK * h : leftW;
            float w = rightX + rightW, hh = 0f;
            foreach ((GuideBlock left, GuideBlock right) in rows)
                hh += Math.Max(h, Math.Max(left.H, right.H));
            hh += RowGapK * h * (rows.Count - 1);

            var block = new GuideBlock(w, hh);
            float y = 0f;
            foreach ((GuideBlock left, GuideBlock right) in rows)
            {
                float rh = Math.Max(h, Math.Max(left.H, right.H));
                if (!Empty(left))
                    block.Add(left, (leftW - left.W) * 0.5f, y + (rh - left.H) * 0.5f);
                if (!Empty(right))
                    block.Add(right, rightX + (rightW - right.W) * 0.5f, y + (rh - right.H) * 0.5f);
                y += rh + RowGapK * h;
            }

            if (both)
                block.Add(Rect(leftW + ColumnGapK * h, 0f, BarK * h, hh, White));
            return block;
        }

        /// <summary>
        /// The last zone, 1.8 h high: figure a, 入口, the distance and the arrow ahead in a row (E.194 a); figure b, 入口 and the arrow to the left after it; figure c, the arrow to the right and
        /// 入口 after it (E.194 b, c: the arrow on the side of the text the figures put it). margin: the least space at its ends.
        /// </summary>
        private static GuideBlock LastRow(GuideSpec s, float h, out float margin)
        {
            float c = EntryCharK * h, entryW = CellWidth(2, c, EntryGapK * h), entryY = EntryTopK * h;
            GuideElement Entry(float x) => Cells("入口", x, entryY, c, White, EntryGapK * h);
            var row = new GuideBlock(0f, ZoneK * h);
            string form = s.Get("form");
            if (form == "b" || form == "c")
            {
                margin = SideMarginK * h;
                float arrowW = SideArrowL * h, arrowH = SideArrowH * h, arrowY = SideArrowTopK * h;
                if (form == "b")
                {
                    row.Add(Entry(0f));
                    row.Add(ArrowIn(GuideArrow.Left, entryW + SideGapK * h, arrowY, arrowW, arrowH, White));
                }
                else
                {
                    row.Add(ArrowIn(GuideArrow.Right, 0f, arrowY, arrowW, arrowH, White));
                    row.Add(Entry(arrowW + SideGapK * h));
                }

                row.W = entryW + SideGapK * h + arrowW;
                return row;
            }

            margin = AheadMarginK * h;
            row.Add(Entry(0f));
            float x = entryW;
            string d = s.Get("dist");
            if (d.Length > 0)
            {
                GuideBlock dist = Distance(d, s.Get("unit"), h, White);
                x += AheadDistGapK * h;
                row.Add(dist, x, DigitsTopK * h);
                x += dist.W;
            }

            x += AheadArrowGapK * h;
            row.Add(ArrowIn(GuideArrow.Up, x, AheadArrowTopK * h, AheadArrowW * h, AheadArrowH * h, White));
            row.W = x + AheadArrowW * h;
            return row;
        }

        // ------------------------------------------------------------------ the board

        /// <summary>
        /// An entrance advance board from its lines: the first line (plates and mark) or, for 路31, the white band with the name; the places; the last zone. The inside is at least 5.6 h wide
        /// (the 6 h board of the drawings) and as wide as the widest line with its margins; every line is centred across.
        /// </summary>
        private static GuideBoard EntryBoard(GuideSpec s, float h, GuideBlock top, string bandName, GuideBlock places)
        {
            bool band = bandName != null;
            GuideBlock name = band ? Line(bandName, h, Green, BandCharGapK * h) : null;
            GuideBlock last = LastRow(s, h, out float lastMargin);

            float innerW = MinInnerK * h;
            if (!Empty(top)) innerW = Math.Max(innerW, top.W + 2f * MarginK * h);
            if (!Empty(name)) innerW = Math.Max(innerW, name.W + 2f * MarginK * h);
            if (!Empty(places)) innerW = Math.Max(innerW, places.W + 2f * MarginK * h);
            innerW = Math.Max(innerW, last.W + 2f * lastMargin);

            // From the inside's top: the band, or the plate line between its gaps (or only the top margin when there is nothing to put there).
            float y = band ? BandK * h + PlateGapK * h : Empty(top) ? PlateTopK * h : PlateTopK * h + top.H + PlateGapK * h;
            float placesY = y;
            if (!Empty(places))
                y += places.H;
            float zoneY = y;
            float innerH = zoneY + ZoneK * h;

            GuidePart p = Board(h, innerW, innerH, Green);
            float e = p.Edge;
            if (band)
            {
                // The white band meets the white border in one colour (bled over it), as the exit boards' name band does.
                GuideElement strip = Rect(e, e, innerW, BandK * h, White);
                strip.RadiusTop = p.InnerRadius;
                strip.Bleed = p.Border;
                p.Elements.Add(strip);
                if (!Empty(name))
                    p.Add(name, e + (innerW - name.W) * 0.5f, e + BandNameTopK * h);
            }
            else if (!Empty(top))
            {
                p.Add(top, e + (innerW - top.W) * 0.5f, e + PlateTopK * h);
            }

            if (!Empty(places))
                p.Add(places, e + (innerW - places.W) * 0.5f, e + placesY);
            p.Add(last, e + (innerW - last.W) * 0.5f, e + zoneY);
            return Single(p, h);
        }

        // ------------------------------------------------------------------ 路29 to 路32

        /// <summary>路29 (Fig. 250, E.194): the expressway's number, two places, the last zone.</summary>
        private static GuideBoard TwoDirections(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            return EntryBoard(s, h, PlateLine(h, 0f, null, s.Get("code")), null, PlaceLine(h, s.Get("to1"), s.Get("to2"), false));
        }

        /// <summary>路30 (Fig. 251, E.195): the number with the direction mark at its right, one place with 方向.</summary>
        private static GuideBoard OneDirection(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            return EntryBoard(s, h, PlateLine(h, 0f, s.Get("dir"), s.Get("code")), null, PlaceLine(h, s.Get("to1"), "", s.Flag("fx")));
        }

        /// <summary>路31 (Fig. 252, E.196): the name of the unnumbered road in green on the white band, the places (one with 方向, or two) under it.</summary>
        private static GuideBoard Unnumbered(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            return EntryBoard(s, h, null, s.Get("rname"), PlaceLine(h, s.Get("to1"), s.Get("to2"), s.Flag("fx")));
        }

        /// <summary>路32 (Fig. 253, E.197): the two numbers side by side, then two places (a to c) or the two columns parted by the bar (d to f).</summary>
        private static GuideBoard Shared(GuideSpec s, int band)
        {
            float h = CharHeight(band);
            bool columns = s.Is("groups", "2");
            GuideBlock top = PlateLine(h, columns ? PlatesGapColumnsK : PlatesGapK, null, s.Get("code"), s.Get("code2"));
            GuideBlock places = columns ? Columns(h, s.Get("tl1"), s.Get("tl2"), s.Get("tr1"), s.Get("tr2")) : PlaceLine(h, s.Get("to1"), s.Get("to2"), false);
            return EntryBoard(s, h, top, null, places);
        }

        // ------------------------------------------------------------------ the catalogue

        private static string[] W(params string[] words) => words;

        private static GuideField[] Fields(IEnumerable<GuideField> first)
        {
            var list = new List<GuideField>(first);
            list.AddRange(FormFields());
            return list.ToArray();
        }

        public static IEnumerable<GuideTemplate> All()
        {
            yield return StandardSigns.Make("gb.guide9.29", "路29", "入口预告（进入后2个方向）", "Entrance advance sign (two directions)", G, "rkyg",
                W("入口", "预告", "入口预告", "高速", "两个方向", "entrance"), TwoDirections, Fields(new[]
                {
                    CodeField("code", "高速公路编号", "Expressway number", "G15"),
                    Place("to1", "方向一地名", "Place of one direction", "汕头"),
                    Place("to2", "方向二地名", "Place of the other direction", "深圳")
                }));

            yield return StandardSigns.Make("gb.guide9.30", "路30", "入口预告（进入后1个方向）", "Entrance advance sign (one direction)", G, "rkyg",
                W("入口", "预告", "入口预告", "高速", "方向", "entrance"), OneDirection, Fields(new[]
                {
                    CodeField("code", "高速公路编号", "Expressway number", "G25"),
                    GuideField.Choice("dir", "方向标志（编号右侧）", "Direction mark (right of number)", "南", kDirs),
                    Place("to1", "地名", "Place", "梅州"),
                    GuideField.Flag("fx", "地名后加“方向”", "方向 after the place", true)
                }));

            yield return StandardSigns.Make("gb.guide9.31", "路31", "无编号高速公路或城市快速路入口预告", "Entrance advance sign (unnumbered expressway or urban expressway)", G, "wbhrkyg",
                W("入口", "预告", "无编号", "快速路", "城市快速路", "entrance"), Unnumbered, Fields(new[]
                {
                    GuideField.Text("rname", "道路名称（白底绿字）", "Road name (green on white)", "九水路", 8),
                    Place("to1", "方向一地名", "Place of one direction", "李沧"),
                    Place("to2", "方向二地名（一个方向时为空）", "Other place (empty: one direction)", "崂山"),
                    GuideField.Flag("fx", "一个方向时地名后加“方向”", "方向 after a single place", true, s => s.Get("to1").Length == 0 || s.Get("to2").Length == 0)
                }));

            yield return StandardSigns.Make("gb.guide9.32", "路32", "两条高速公路路段重合的入口预告", "Entrance advance sign (two expressways sharing a section)", G, "chrkyg",
                W("入口", "预告", "重合", "共线", "高速", "entrance"), Shared, Fields(new[]
                {
                    CodeField("code", "编号一（G2、皖S30…）", "First number (G2, 皖S30...)", "G2"),
                    CodeField("code2", "编号二", "Second number", "G3"),
                    GuideField.Choice("groups", "地名排列", "Places", "1", kGroups),
                    PlaceIf("to1", "方向一地名", "Place of one direction", "天津", s => s.Is("groups", "1")),
                    PlaceIf("to2", "方向二地名", "Place of the other direction", "济南", s => s.Is("groups", "1")),
                    PlaceIf("tl1", "左列：编号一上的地名", "Left column: first road's place", "南通", s => s.Is("groups", "2")),
                    PlaceIf("tl2", "左列：编号二上的地名", "Left column: place on road 2", "苏州", s => s.Is("groups", "2")),
                    PlaceIf("tr1", "右列：编号一上的地名", "Right column: place on road 1", "信阳", s => s.Is("groups", "2")),
                    PlaceIf("tr2", "右列：编号二上的地名", "Right column: place on road 2", "武汉", s => s.Is("groups", "2"))
                }));
        }
    }
}
