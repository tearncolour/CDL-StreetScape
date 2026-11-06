using System;
using System.Collections.Generic;
using static CDLC.TS.Geo.GuideKit;
using static CDLC.TS.Geo.Signs.RoadGuideSigns;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// Signs of the expressway guide chapter of GB 5768.2-2022: the exit direction board of an interchange whose exit ramp has two lanes leading to two destinations, 路47 b and c (9.3.8.1,
    /// Fig. 267 b and c, drawing E.213 b and c). 9.3.8.1: "枢纽互通式立体交叉出口匝道为单车道时，出口方向标志见图 267a)；出口匝道为 2 条车道，驾驶人沿着不同行车道方向通往不同的目的地时，出口方向
    /// 标志见图 267b)、图 267c)". Form a (one road plate, one arrow) is the catalogue's gb.exit.hub.dir (template guide.exit.direction of GuideTemplates). The board is laid out in the
    /// character height h of Table 3; edge (liner and border) 0.2 h and corner radius 0.4 h as Fig. 3; the road numbers through RoadGuideSigns.Info (8.1.5: G78, S49, G1511 become the
    /// plates of Appendix C, table C.1: 100 cm high, 70 cm at 80 km/h and below; E.213 b gives them as "125 (85)" x "100 (70)").
    /// Copied from ExpresswayForkSigns (private there): DigitHeight, Names, AddExitPlate and the direction mark (a white square 1 h with the character 0.8 h in green).
    /// </summary>
    public static class ExpresswayHubSigns
    {
        private const string G = "guide";
        private const GuideColour Green = GuideColour.Green, White = GuideColour.White;

        private static readonly string[] kBandIds = { "100", "80", "60", "30" };

        /// <summary>The digit height D of Table 5 for the band (the exit number plate, E.207). Copied from ExpresswayForkSigns.</summary>
        private static float DigitHeight(int band) => GuideTemplates.BandOf(kBandIds[SignStyles.Band(band)]).D;

        /// <summary>One or two place names in a row, h high, 0.6 h apart (8.1.4 c, E.208 to E.213). Copied from ExpresswayForkSigns.</summary>
        private static GuideBlock Names(string a, string b, float h)
        {
            GuideBlock first = Line(a, h, White, NameGapK * h), second = Line(b, h, White, NameGapK * h);
            if (first.W <= 0f)
                return second;
            if (second.W <= 0f)
                return first;
            var row = new GuideBlock(first.W + 0.6f * h + second.W, h);
            row.Add(first, 0f, 0f);
            row.Add(second, first.W + 0.6f * h, 0f);
            return row;
        }

        /// <summary>The exit number plate (路41, E.207) on top of the board at the side of the exit (9.3.7), 左出口 for an exit on the left (Fig. 263 c). Copied from ExpresswayForkSigns.</summary>
        private static void AddExitPlate(GuideBoard board, GuideSpec s, int band, float h, bool left)
        {
            string number = s.Get("xnum");
            if (number.Length == 0)
                return;
            GuidePart plate = ExitNumberPlate(DigitHeight(band), number, s.Get("xsuf"), left, 0f, 0f);
            plate.X = left ? 0f : board.Main.W - plate.W;
            plate.Y = -plate.H - 0.05f * h;
            board.Parts.Add(plate);
        }

        /// <summary>The direction mark (E.213 b, c: a white square 1 h on a side; the character 0.8 h in the ground's green, as 路35 and 路49 to 路51). x, y: the square's top left in the part.</summary>
        private static void Mark(GuidePart p, string dir, float x, float y, float h)
        {
            p.Elements.Add(Rect(x, y, h, h, White));
            p.Elements.Add(Cells(dir.Substring(0, 1), x + 0.1f * h, y + 0.1f * h, 0.8f * h, Green, 0f));
        }

        /// <summary>
        /// The 45 degree arrow of E.166 b (8.1.8 b and d) in a square box: a head a wide and a long (the tip 81 degrees, the full width 0.58 a behind the tip, the sides then parallel to the
        /// axis, the back parallel to the front), a stem 0.28 a; E.166 b gives its box as 1.2 a to 2 a. E.213 draws it in its 1 h box with the head 0.77 h and the whole 1.22 h long (1.6 a,
        /// measured on the drawing rendered at 5000 px a page): that length here, the arrow as large as the box allows (RoadGuideSigns.ArrowIn draws the diagonal arrows 1.9 a long, which
        /// makes the head 0.69 h in the same box, visibly thinner than Fig. 267's). The outline as RoadGuideSigns.ArrowShape (private there), a polygon of nine corners.
        /// </summary>
        private static GuideElement Diagonal(bool leftward, float x, float y, float size, GuideColour colour)
        {
            const float a = 1f, length = 1.6f, t = 0.14f;
            float x1 = length * 0.5f, x0 = -x1, join = x1 - a + 0.58f * (a - 2f * t);
            float[] p =
            {
                x1, 0f, x1 - 0.58f * a, -0.5f * a, x1 - a, -0.5f * a, join, -t, x0, -t,
                x0, t, join, t, x1 - a, 0.5f * a, x1 - 0.58f * a, 0.5f * a
            };

            // Turned on the screen (y down): -45 degrees points up and to the right, -135 up and to the left.
            double r = (leftward ? -135.0 : -45.0) * Math.PI / 180.0;
            float c = (float)Math.Cos(r), sn = (float)Math.Sin(r);
            var q = new float[p.Length];
            float bx0 = float.MaxValue, by0 = float.MaxValue, bx1 = float.MinValue, by1 = float.MinValue;
            for (int i = 0; i < p.Length; i += 2)
            {
                q[i] = p[i] * c - p[i + 1] * sn;
                q[i + 1] = p[i] * sn + p[i + 1] * c;
                bx0 = Math.Min(bx0, q[i]);
                bx1 = Math.Max(bx1, q[i]);
                by0 = Math.Min(by0, q[i + 1]);
                by1 = Math.Max(by1, q[i + 1]);
            }

            float k = size / Math.Max(bx1 - bx0, by1 - by0);
            float cx = (bx0 + bx1) * 0.5f, cy = (by0 + by1) * 0.5f;
            for (int i = 0; i < q.Length; i += 2)
            {
                q[i] = x + size * 0.5f + (q[i] - cx) * k;
                q[i + 1] = y + size * 0.5f + (q[i + 1] - cy) * k;
            }

            return Poly(q, colour);
        }

        // ================================================================== 路47 b and c (E.213 b, c)

        /// <summary>What one of the two groups says: its road (a plate, or a name in characters), its direction mark, its place names.</summary>
        private sealed class Group
        {
            public readonly GuideBlock Plate, Names;
            public readonly string Dir;

            /// <summary>The top row's width: [mark] 0.2 h [plate] (the mark on the board's outer side of the plate).</summary>
            public readonly float TopW;

            public bool HasMark => Dir.Length > 0;
            public bool Says => TopW > 0f || Names.W > 0f;

            public Group(GuideSpec s, string code, string dir, string to, string tob, float h)
            {
                Plate = Info(s.Get(code), h, Green);
                Dir = s.Get(dir);
                Names = ExpresswayHubSigns.Names(s.Get(to), s.Get(tob), h);
                TopW = Plate.W + (HasMark ? h + (Plate.W > 0f ? 0.2f * h : 0f) : 0f);
            }
        }

        /// <summary>The least and the greatest lane width a player may type for form c (m); the arrows are put that far apart.</summary>
        private const float MinLane = 2.5f, MaxLane = 6.0f;

        /// <summary>
        /// 路47 b (E.213 b, Fig. 267 b) and c (E.213 c, Fig. 267 c), in h from the inside's top left (inside the 0.2 h edge). Two groups side by side, parted by a white bar 0.1 h wide in
        /// the middle from the plates' top (0.4 h under the inside's top) down to the foot of the place names. Each group: the road's plate 0.25 h from the bar's middle (the two plates
        /// 0.5 h apart, 0.4 h under the inside's top) and the direction mark (a white square 1 h, 0.7 h under the inside's top, 0.2 h from the plate) on the outer side, at least 0.4 h
        /// from the inside's edge; the place names 0.4 h under the plates, 1 h from the inside's edge and 0.8 h from the bar's middle (1.6 h between the two groups), centred in that
        /// space when there is more of it. Under the names, 0.5 h, the arrow (1 h, E.213 b: one, under the bar; E.213 c: one for each lane, 1.4 h from the inside's edges, "the arrows
        /// over the lanes' centres, 8.1.8" by the note; with a lane width set, the two arrows that far apart about the bar, which is then over the lane line), and 0.4 h to the bottom.
        /// With the standard's example (two characters a name, 125 cm plates) this is 8 h x (3.7 h + 100 cm) inside, as E.213 b and c (the drawing draws its 125 cm plates 2.07 h wide,
        /// so that its top chain 0.4 h + 1 h + 0.2 h + 125 + 0.5 h + 125 + 0.2 h + 1 h + 0.4 h adds up to the 8 h of the names' chain; with the real 1.92 h the plates keep their
        /// dimensioned 0.5 h at the bar and the marks get the rest, 0.63 h from the edge). The exit side (xside) puts the exit number plate on the upper right or the upper left with
        /// 左出口 (9.3.7, as the exit boards of GuideTemplates) and turns the arrows to ↗ or ↖ (8.1.8 b and d); the groups stay in the order of the lanes.
        /// </summary>
        private static GuideBoard HubBoard(GuideSpec s, int band)
        {
            float h = CharHeight(band), m = 0.4f * h;
            bool left = s.Is("xside", "l");
            bool lanes = s.Is("form", "c");

            var g1 = new Group(s, "code1", "dir1", "to1", "to1b", h);
            var g2 = new Group(s, "code2", "dir2", "to2", "to2b", h);
            bool two = g1.Says && g2.Says;

            // Form c with a lane width: the arrows' centres that far apart (8.1.8: over the lanes' centres), kept between the bounds.
            float lane = 0f;
            if (lanes && float.TryParse(s.Get("lw"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float typed) && typed > 0f)
                lane = Math.Max(Math.Max(MinLane, 1.4f * h), Math.Min(MaxLane, typed));

            // ---- down: the top row (plates and marks), the names, the arrows
            float T = 0f;
            foreach (Group g in new[] { g1, g2 })
            {
                if (g.Plate.W > 0f) T = Math.Max(T, g.Plate.H);
                if (g.HasMark) T = Math.Max(T, h);
            }

            bool anyNames = g1.Names.W > 0f || g2.Names.W > 0f;
            float y = m;
            float topY = m;
            if (T > 0f)
                y += T + 0.4f * h;
            float namesY = y;
            if (anyNames)
                y += h + 0.5f * h;
            else if (T > 0f)
                y += 0.1f * h;
            float arrowY = y;
            float innerH = arrowY + h + m;
            // A mark 0.7 h under the inside's top (E.213 b, c: 0.3 h under the top of a 100 cm plate), not lower than the row allows.
            float markY = topY + Math.Max(0f, Math.Min(0.3f * h, T - h));

            GuidePart exitPlate = s.Get("xnum").Length > 0 ? ExitNumberPlate(DigitHeight(band), s.Get("xnum"), s.Get("xsuf"), left, 0f, 0f) : null;
            float edge = EdgeK * h;
            float minW = exitPlate != null ? exitPlate.W - 2f * edge : 0f;

            GuidePart p;
            if (two)
            {
                // ---- across: each half from the inside's edge to the bar's middle
                float HalfW(Group g)
                {
                    float w = 0.9f * h;
                    if (g.TopW > 0f) w = Math.Max(w, m + g.TopW + 0.25f * h);
                    if (g.Names.W > 0f) w = Math.Max(w, h + g.Names.W + 0.8f * h);
                    if (lanes)
                        w = Math.Max(w, lane > 0f ? lane * 0.5f + 0.5f * h + m : 1.4f * h + h + 0.4f * h);
                    return w;
                }

                float wl = HalfW(g1), wr = HalfW(g2);
                if (wl + wr < minW)
                {
                    float extra = (minW - wl - wr) * 0.5f;
                    wl += extra;
                    wr += extra;
                }

                float innerW = wl + wr, bar = wl;
                p = Board(h, innerW, innerH, Green);
                float e = p.Edge;

                // The names: 1 h from the inside's edge, 0.8 h from the bar's middle, centred in what is between.
                float NamesCentre(bool onLeft) => onLeft ? (h + bar - 0.8f * h) * 0.5f : (bar + 0.8f * h + innerW - h) * 0.5f;

                // The top row: the plates hug the bar; the marks outside them.
                void Top(Group g, bool onLeft)
                {
                    if (g.TopW <= 0f)
                        return;
                    if (g.Plate.W > 0f)
                    {
                        float px = onLeft ? bar - 0.25f * h - g.Plate.W : bar + 0.25f * h;
                        p.Add(g.Plate, e + px, e + topY + (T - g.Plate.H) * 0.5f);
                        if (g.HasMark)
                            Mark(p, g.Dir, e + (onLeft ? px - 0.2f * h - h : px + g.Plate.W + 0.2f * h), e + markY, h);
                    }
                    else
                    {
                        // A mark without a plate stands over the group's names (or at the bar when there are none).
                        float centre = g.Names.W > 0f ? NamesCentre(onLeft) : onLeft ? bar - 0.25f * h - 0.5f * h : bar + 0.25f * h + 0.5f * h;
                        Mark(p, g.Dir, e + centre - 0.5f * h, e + markY, h);
                    }
                }

                Top(g1, true);
                Top(g2, false);
                if (g1.Names.W > 0f)
                    p.Add(g1.Names, e + NamesCentre(true) - g1.Names.W * 0.5f, e + namesY);
                if (g2.Names.W > 0f)
                    p.Add(g2.Names, e + NamesCentre(false) - g2.Names.W * 0.5f, e + namesY);

                // The bar: 0.1 h wide, from the plates' top to the foot of the names (of the top row when there are no names).
                float barBottom = anyNames ? namesY + h : topY + T;
                if (barBottom > topY)
                    p.Elements.Add(Rect(e + bar - 0.05f * h, e + topY, 0.1f * h, barBottom - topY, White));

                // The arrows.
                if (!lanes)
                    p.Elements.Add(Diagonal(left, e + bar - 0.5f * h, e + arrowY, h, White));
                else if (lane > 0f)
                {
                    p.Elements.Add(Diagonal(left, e + bar - lane * 0.5f - 0.5f * h, e + arrowY, h, White));
                    p.Elements.Add(Diagonal(left, e + bar + lane * 0.5f - 0.5f * h, e + arrowY, h, White));
                }
                else
                {
                    p.Elements.Add(Diagonal(left, e + 1.4f * h, e + arrowY, h, White));
                    p.Elements.Add(Diagonal(left, e + innerW - 1.4f * h - h, e + arrowY, h, White));
                }
            }
            else
            {
                // Only one group says something (the other is emptied): one column, centred, with the margins of Table 6 (0.4 h), as form a; no bar.
                Group g = g1.Says ? g1 : g2;
                bool markLeft = g == g1;
                float innerW = Math.Max(minW, h + 2f * m);
                if (g.TopW > 0f) innerW = Math.Max(innerW, g.TopW + 2f * m);
                if (g.Names.W > 0f) innerW = Math.Max(innerW, g.Names.W + 2f * m);
                if (lanes)
                    innerW = Math.Max(innerW, lane > 0f ? lane + h + 2f * m : 2f * (1.4f * h + h) + 1.2f * h);

                p = Board(h, innerW, innerH, Green);
                float e = p.Edge;
                if (g.TopW > 0f)
                {
                    float x = (innerW - g.TopW) * 0.5f;
                    float px = markLeft && g.HasMark ? x + h + 0.2f * h : x;
                    if (g.Plate.W > 0f)
                        p.Add(g.Plate, e + px, e + topY + (T - g.Plate.H) * 0.5f);
                    if (g.HasMark)
                        Mark(p, g.Dir, e + (g.Plate.W <= 0f || markLeft ? x : px + g.Plate.W + 0.2f * h), e + markY, h);
                }

                if (g.Names.W > 0f)
                    p.Add(g.Names, e + (innerW - g.Names.W) * 0.5f, e + namesY);

                if (!lanes)
                    p.Elements.Add(Diagonal(left, e + (innerW - h) * 0.5f, e + arrowY, h, White));
                else if (lane > 0f)
                {
                    p.Elements.Add(Diagonal(left, e + innerW * 0.5f - lane * 0.5f - 0.5f * h, e + arrowY, h, White));
                    p.Elements.Add(Diagonal(left, e + innerW * 0.5f + lane * 0.5f - 0.5f * h, e + arrowY, h, White));
                }
                else
                {
                    p.Elements.Add(Diagonal(left, e + 1.4f * h, e + arrowY, h, White));
                    p.Elements.Add(Diagonal(left, e + innerW - 1.4f * h - h, e + arrowY, h, White));
                }
            }

            GuideBoard board = Single(p, h);
            AddExitPlate(board, s, band, h, left);
            return board;
        }

        // ================================================================== Fig. 268 (E.214): the main line's direction information

        /// <summary>
        /// The board that gives the main line's direction at an interchange's exit (9.3.8.1, Fig. 268, drawing E.214; Fig. 269 puts it beside the exit boards): the road's plate with its direction
        /// mark (the plate's width, 0.3 h, a square 1 h, 0.3 h under the plate's top; as E.215 a), the place names 0.4 h under the plate, 0.5 h under them the arrow. a (E.214 a): one arrow
        /// up, a box 1 h wide and 1.2 h high, 0.2 h to the foot, the names 1.2 h from the inside's edges (with three characters 5.8 h inside, 6.2 h outside as the drawing). b (E.214 b,
        /// "an arrow over each lane's centre", 8.1.8): an arrow down in a box 1 h square for each lane, 0.4 h to the foot, the outer ones 0.4 h from the inside's edges and the others evenly
        /// between (centres 6.3 h apart in the drawing's three lanes, 14.4 h inside; 6.2 h in E.215 b), the plate and the names centred. A lane width set puts the arrows that far apart.
        /// </summary>
        private static GuideBoard MainLineBoard(GuideSpec s, int band)
        {
            float h = CharHeight(band), m = 0.4f * h;
            bool lanes = s.Is("form", "b");
            int n = lanes ? Math.Max(2, Math.Min(5, (int)s.Number("lanes", 3))) : 1;
            float lane = 0f;
            if (lanes && float.TryParse(s.Get("lw"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float typed) && typed > 0f)
                lane = Math.Max(Math.Max(MinLane, 1.4f * h), Math.Min(MaxLane, typed));

            GuideBlock plate = Info(s.Get("code"), h, Green);
            string dir = s.Get("dir");
            bool mark = dir.Length > 0;
            GuideBlock names = Names(s.Get("to"), s.Get("tob"), h);

            float gap = plate.W > 0f ? 0.3f * h : 0f;
            float groupW = plate.W + (mark ? gap + h : 0f);
            float markDown = plate.W > 0f ? 0.3f * h : 0f;
            float T = Math.Max(plate.W > 0f ? plate.H : 0f, mark ? markDown + h : 0f);

            float y = m;
            float plateY = y;
            if (T > 0f)
                y += T + 0.4f * h;
            float namesY = y;
            if (names.W > 0f)
                y += h + 0.5f * h;
            else if (T > 0f)
                y += 0.1f * h;
            float arrowY = y;
            float arrowH = lanes ? h : 1.2f * h;
            float innerH = arrowY + arrowH + (lanes ? 0.4f : 0.2f) * h;

            float innerW = Math.Max(Math.Max(groupW, names.W) + 2f * 1.2f * h, h + 2f * m);
            if (lanes)
                innerW = Math.Max(innerW, lane > 0f ? (n - 1) * lane + h + 2f * 0.4f * h : 2f * 0.4f * h + h + (n - 1) * 6.3f * h);

            GuidePart p = Board(h, innerW, innerH, Green);
            float e = p.Edge;
            if (T > 0f)
            {
                float gx = (innerW - groupW) * 0.5f;
                if (plate.W > 0f)
                    p.Add(plate, e + gx, e + plateY + (T - plate.H) * 0.5f);
                if (mark)
                    Mark(p, dir, e + gx + plate.W + gap, e + plateY + markDown, h);
            }

            if (names.W > 0f)
                p.Add(names, e + (innerW - names.W) * 0.5f, e + namesY);

            if (!lanes)
            {
                p.Elements.Add(ArrowIn(GuideArrow.Up, e + (innerW - h) * 0.5f, e + arrowY, h, 1.2f * h, White));
            }
            else
            {
                float x0 = 0.4f * h, step = (innerW - 2f * 0.4f * h - h) / (n - 1);
                if (lane > 0f)
                {
                    step = lane;
                    x0 = (innerW - (n - 1) * lane - h) * 0.5f;
                }

                for (int i = 0; i < n; i++)
                    p.Elements.Add(ArrowIn(GuideArrow.Down, e + x0 + i * step, e + arrowY, h, h, White));
            }

            return Single(p, h);
        }

        // ================================================================== the catalogue

        private static string[] W(params string[] words) => words;

        private static readonly GuideChoice[] kForms =
        {
            new GuideChoice("b", "一个箭头居中（图 267 b）", "One arrow, middle (Fig. 267 b)"),
            new GuideChoice("c", "箭头对着各车道中心（图 267 c）", "Arrow over each lane (Fig. 267 c)")
        };

        private static readonly GuideChoice[] kSides =
        {
            new GuideChoice("r", "右侧出口（↗，编号牌在右上角）", "Right exit (↗, number plate upper right)"),
            new GuideChoice("l", "左侧出口（↖，左出口编号牌在左上角）", "Left exit (↖, left exit plate upper left)")
        };

        private static readonly GuideChoice[] kDirs =
        {
            new GuideChoice("", "不设", "None"),
            new GuideChoice("东", "东", "East"),
            new GuideChoice("南", "南", "South"),
            new GuideChoice("西", "西", "West"),
            new GuideChoice("北", "北", "North")
        };

        private static readonly GuideChoice[] kMainForms =
        {
            new GuideChoice("a", "箭头向上，一个（图 268 a）", "One arrow up (Fig. 268 a)"),
            new GuideChoice("b", "箭头向下，对着各车道（图 268 b）", "An arrow down over each lane (Fig. 268 b)")
        };

        private static readonly GuideChoice[] kLaneCounts =
        {
            new GuideChoice("2", "2 条车道", "2 lanes"), new GuideChoice("3", "3 条车道", "3 lanes"), new GuideChoice("4", "4 条车道", "4 lanes"), new GuideChoice("5", "5 条车道", "5 lanes")
        };

        /// <summary>The settings of the main line's direction board (Fig. 268): the form (the arrows) only has another default in the two entries.</summary>
        private static GuideField[] MainFields(string form) => new[]
        {
            GuideField.Text("code", "主线：道路编号（空为不设）", "Main line: road number (or none)", "G25", 6),
            GuideField.Choice("dir", "主线：方位", "Main line: direction", "南", kDirs),
            Place("to", "主线：地名", "Main line: place", "连云港"),
            Place("tob", "主线：地名二（可空）", "Main line: place 2 (optional)", ""),
            GuideField.Choice("form", "箭头", "Arrows", form, kMainForms),
            GuideField.Choice("lanes", "车道数（向下箭头的个数）", "Lanes (number of arrows)", "3", kLaneCounts, sp => sp.Is("form", "b")),
            GuideField.Digits("lw", "车道宽度（m）", "Lane width (m)", "", 4, sp => sp.Is("form", "b"))
        };

        /// <summary>The settings of 路47 b and c: the same board, the form (the arrows) only has another default.</summary>
        private static GuideField[] HubFields(string form) => new[]
        {
            GuideField.Text("code1", "左侧车道：道路编号", "Left lane: road number", "G78", 6),
            GuideField.Choice("dir1", "左侧车道方向：方位", "Left lane's route: direction", "东", kDirs),
            Place("to1", "左侧车道方向：地名", "Left lane's route: place", "汕头"),
            Place("to1b", "左侧车道：地名二", "Left lane: place 2", ""),
            GuideField.Text("code2", "右侧车道：道路编号", "Right lane: road number", "G78", 6),
            GuideField.Choice("dir2", "右侧车道方向：方位", "Right lane's route: direction", "西", kDirs),
            Place("to2", "右侧车道方向：地名", "Right lane's route: place", "贺州"),
            Place("to2b", "右侧车道：地名二", "Right lane: place 2", ""),
            GuideField.Choice("form", "箭头", "Arrows", form, kForms),
            GuideField.Digits("lw", "车道宽度（m）", "Lane width (m)", "", 4, sp => sp.Is("form", "c")),
            GuideField.Choice("xside", "出口位置", "Side of the exit", "r", kSides),
            GuideField.Text("xnum", "出口编号（空为不设）", "Exit number (empty: none)", "133", 4),
            GuideField.Text("xsuf", "编号后缀（A、B、A-B）", "Suffix (A, B, A-B)", "", 3, sp => sp.Get("xnum").Length > 0)
        };

        public static IEnumerable<GuideTemplate> All()
        {
            // 9.3.8.1, Fig. 267 b and c, E.213 b and c: the exit ramp of two lanes, each lane to its own destination; the exit direction board at the base point. One template with a choice of
            // the arrows (b: one under the bar, c: one over each lane), listed twice so that a player finds form c by its own entry (the entry only has another default for the arrows).
            yield return StandardSigns.Make("gb.guide9.47b", "路47", "枢纽互通出口方向（出口匝道两车道，两个方向并列）", "Interchange exit direction, two-lane exit ramp (two routes side by side)", G,
                "snhtckfx", W("枢纽", "互通", "出口方向", "两车道", "两个方向", "并列"),
                HubBoard, HubFields("b"));

            yield return StandardSigns.Make("gb.guide9.47c", "路47", "枢纽互通出口方向（出口匝道两车道，箭头对着车道）", "Interchange exit direction, two-lane exit ramp (an arrow over each lane)", G,
                "snhtckfx", W("枢纽", "互通", "出口方向", "两车道", "车道", "箭头", "每车道"),
                HubBoard, HubFields("c"));

            // 9.3.8.1, Fig. 268 a and b, E.214: the main line's direction information at the exit of an interchange, an arrow up or an arrow down over each lane.
            yield return StandardSigns.Make("gb.guide9.268a", "图268", "枢纽互通主线方向信息指引（箭头向上）", "Interchange main line direction (arrow up)", G,
                "snhtzxfxxxzy", W("枢纽", "互通", "主线", "方向", "信息指引", "箭头向上", "main line"),
                MainLineBoard, MainFields("a"));

            yield return StandardSigns.Make("gb.guide9.268b", "图268", "枢纽互通主线方向信息指引（箭头向下对着车道）", "Interchange main line direction (arrows down over the lanes)", G,
                "snhtzxfxxxzy", W("枢纽", "互通", "主线", "方向", "信息指引", "箭头向下", "车道", "main line"),
                MainLineBoard, MainFields("b"));
        }
    }
}
