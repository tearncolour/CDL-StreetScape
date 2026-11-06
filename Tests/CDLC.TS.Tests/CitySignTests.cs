using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The urban signs of 0.12.78 (citysigns): the blue chevron sign, the interchange signs and the check of 示41's arrow against the standard. The old signs next to them are pinned by fingerprints taken on
    /// main before the change (scratch/citysigns_baseline.txt): the refactor of 警46's layout and the appended templates must not move a stroke of any other sign.
    /// </summary>
    internal static class CitySignTests
    {
        public static void RunAll()
        {
            Program.Run("city signs: the signs that were there come out as they did", OldSignsUnchanged);
            Program.Run("city signs: the blue chevron sign is 警46's board and figure in white on blue", BlueChevron);
            Program.Run("city signs: the interchange signs have the loop, the bar and the branch where the photograph has them", Interchange);
            Program.Run("city signs: 示41's arrow is the figure of E.95", UTurn);
            Program.Run("city signs: the green distance plate is an auxiliary board and goes under the sign it explains", DistancePlate);
        }

        private static uint Fnv(string text)
        {
            uint h = 2166136261u;
            foreach (char c in text)
                h = (h ^ c) * 16777619u;
            return h;
        }

        /// <summary>The mandatory, warning and auxiliary templates of main 35cb654, by id: the signs this change must not move. A template added to those groups later is not in the list.</summary>
        private static readonly string[] s_Old = (
            "gb.mand.01 gb.mand.02 gb.mand.04 gb.mand.06 gb.mand.07 gb.mand.09 gb.mand.10 gb.mand.11 gb.mand.12 gb.mand.13 gb.mand.14 gb.mand.15 gb.mand.16 "
            + "gb.mand.17 gb.mand.24 gb.mand.25 gb.mand.26 gb.mand.27 gb.mand.28 gb.mand.29 gb.mand.30 gb.mand.31 gb.mand.32 gb.mand.33 gb.mand.34 gb.mand.35 "
            + "gb.mand.36 gb.mand.37 gb.mand.38 gb.mand.39 gb.mand.40 gb.mand.41 gb.mand.42 gb.mand.43 gb.warn.01 gb.warn.02 gb.warn.03 gb.warn.04 gb.warn.05 "
            + "gb.warn.06 gb.warn.07 gb.warn.08 gb.warn.09 gb.warn.10 gb.warn.11 gb.warn.12 gb.warn.13 gb.warn.14 gb.warn.15 gb.warn.16 gb.warn.17 gb.warn.18 "
            + "gb.warn.19 gb.warn.20 gb.warn.21 gb.warn.22 gb.warn.23 gb.warn.24 gb.warn.25 gb.warn.26 gb.warn.27 gb.warn.28 gb.warn.29 gb.warn.30 gb.warn.31 "
            + "gb.warn.32 gb.warn.33 gb.warn.34 gb.warn.35 gb.warn.36 gb.warn.37 gb.warn.38 gb.warn.39 gb.warn.40 gb.warn.41 gb.warn.42 gb.warn.43 gb.warn.44 "
            + "gb.warn.45 gb.warn.46 gb.warn.47 gb.aux.text gb.aux.01 gb.aux.02 gb.aux.03 gb.aux.04 gb.aux.05 gb.aux.06 gb.aux.07 gb.aux.08 gb.aux.09 gb.aux.11 "
            + "gb.aux.12 gb.aux.13 gb.aux.14 gb.aux.15 gb.aux.16 gb.aux.17 gb.aux.18 gb.aux.19 gb.aux.20").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Every state of the signs this change must not touch, as one string each; the baseline was read with the same code on main (scratch/citysigns_baseline.txt).</summary>
        internal static void Fingerprints(out string chevron, out string[] uturn, out string groups, out int count)
        {
            GuideTemplate warn = GuideTemplates.ById("gb.warn.46"), turn = GuideTemplates.ById("gb.mand.41");
            var all = new StringBuilder();
            foreach (string form in new[] { "1", "2", "3", "4" })
            foreach (string dir in new[] { "l", "r" })
            foreach (string n in new[] { "2", "3", "4" })
            foreach (string speed in new[] { "auto", "100", "80", "60", "30" })
            foreach (string small in new[] { "0", "1" })
                all.Append(BoardPrint.Of(warn.Layout(warn.Default().With("form", form).With("dir", dir).With("count", n).With("speed", speed).With("small", small))));
            chevron = Fnv(all.ToString()).ToString("x8");
            var list = new List<string>();
            foreach (string speed in new[] { "auto", "100", "80", "60", "30" })
                list.Add(BoardPrint.Hash(turn.Layout(turn.Default().With("speed", speed))));
            uturn = list.ToArray();

            var every = new StringBuilder();
            count = 0;
            foreach (string id in s_Old)
            {
                GuideTemplate t = GuideTemplates.ById(id);
                foreach (string speed in new[] { "auto", "100", "60" })
                    every.Append(t.Id).Append(speed).Append(BoardPrint.Of(t.Layout(t.FieldOf("speed") != null ? t.Default().With("speed", speed) : t.Default())));
                count++;
            }

            groups = Fnv(every.ToString()).ToString("x8");
        }

        private static void OldSignsUnchanged()
        {
            Fingerprints(out string chevron, out string[] uturn, out string groups, out int count);
            Program.Check(chevron == "ed5d24a7", "警46 in every form, side, count, band and size: " + chevron);
            Program.Check(string.Join(" ", uturn) == "7d79bde5 2dd1df38 16e678fa 7d79bde5 a8a3dcba", "示41 in every band: " + string.Join(" ", uturn));
            Program.Check(count == 101 && groups == "50adb47e", "every other mandatory, warning and auxiliary sign: " + count + " templates, " + groups);
        }

        // ------------------------------------------------------------------ the figures, read back from the board's contours

        /// <summary>Containment of a point (metres in the part's frame) by the non-zero winding rule, as GuidePainter fills a path: counter-clockwise solids join, a clockwise contour is a hole.</summary>
        private static bool Inside(GuideElement e, float x, float y)
        {
            int winding = 0;
            foreach (float[] c in e.Contours)
            {
                for (int i = 0, j = c.Length - 2; i < c.Length; j = i, i += 2)
                {
                    float xi = c[i], yi = c[i + 1], xj = c[j], yj = c[j + 1];
                    // The edge from j to i, with y down: a crossing of the ray to +x counts by the way the edge goes.
                    if ((yj <= y) != (yi <= y))
                    {
                        float at = xj + (y - yj) / (yi - yj) * (xi - xj);
                        if (at > x)
                            winding += yi > yj ? 1 : -1;
                    }
                }
            }

            return winding != 0;
        }

        /// <summary>The colour the board shows at a point given in units of its width (0 to 100 across, the same scale down): the last element that covers it, else the ground.</summary>
        private static GuideColour ColourAt(GuideBoard board, float ux, float uy)
        {
            GuidePart part = board.Main;
            float x = ux / 100f * part.W, y = uy / 100f * part.W;
            GuideColour colour = part.Ground;
            foreach (GuideElement e in part.Elements)
            {
                if (e.Kind == GuideElementKind.Path && Inside(e, x, y))
                    colour = e.Colour;
                else if (e.Kind == GuideElementKind.Oval)
                {
                    float rx = e.W * 0.5f, ry = e.H * 0.5f, dx = (x - e.X - rx) / rx, dy = (y - e.Y - ry) / ry;
                    if (dx * dx + dy * dy <= 1f)
                        colour = e.Colour;
                }
            }

            return colour;
        }

        private static GuideBoard Make(string id, string spec)
        {
            GuideTemplate t = GuideTemplates.ById(id);
            return t.Layout(t.Parse(spec));
        }

        private static void BlueChevron()
        {
            GuideTemplate blue = GuideTemplates.ById("gb.mand.curve"), yellow = GuideTemplates.ById("gb.warn.46");
            Program.Check(blue != null && blue.Group == StandardSigns.Mandatory && blue.Standalone, "the sign is in the list of the mandatory signs");
            Program.Check(blue.Linear && blue.FieldOf(SignLine.LineKey) != null && blue.FieldOf(SignLine.GapKey) != null, "it can be placed along a bend as 警46 can");
            Program.Check(blue.FieldOf("form").Choices.Length == 3 && blue.FieldOf("form").Choices[2].Value == "3", "the upright form 4 of a median's end is not offered: it has no blue version");
            foreach (string form in new[] { "1", "2", "3" })
            foreach (string dir in new[] { "l", "r" })
            foreach (string n in new[] { "2", "3", "4" })
            foreach (string speed in new[] { "100", "60" })
            foreach (string small in new[] { "0", "1" })
            {
                string what = $"form {form} {dir} x{n} at {speed}, small {small}";
                GuideBoard b = blue.Layout(blue.Default().With("form", form).With("dir", dir).With("count", n).With("speed", speed).With("small", small));
                GuideBoard y = yellow.Layout(yellow.Default().With("form", form).With("dir", dir).With("count", n).With("speed", speed).With("small", small));
                GuidePart pb = b.Main, py = y.Main;
                Program.Check(b.Parts.Count == 1 && pb.Ground == GuideColour.Blue && pb.BorderColour == GuideColour.White && pb.Border > 0f && pb.Liner > 0f, what + ": blue with the white line of Fig. 3");
                Program.Near(pb.W, py.W, 1e-5f, what + ": as wide as 警46's board");
                Program.Near(pb.H, py.H, 1e-5f, what + ": as high as 警46's board");
                int figures = 0;
                foreach (GuideElement e in pb.Elements)
                {
                    if (e.Kind == GuideElementKind.Path)
                    {
                        figures++;
                        Program.Check(e.Colour == GuideColour.White, what + ": a white chevron");
                    }
                }

                int expected = form == "1" ? 1 : form == "3" ? 4 : int.Parse(n);
                Program.Check(figures == expected, what + ": " + figures + " chevrons, expected " + expected);
            }

            // The photographed boards: 0.75 for one chevron and 1.4 for two (measured 0.756 and 1.41 on a 31 and a 58 pixel wide board).
            GuidePart one = blue.Layout(blue.Default().With("speed", "100")).Main, two = blue.Layout(blue.Default().With("form", "2").With("count", "2").With("speed", "100")).Main;
            Program.Near(one.W / one.H, 0.75f, 0.005f, "one chevron: 600 x 800 mm");
            Program.Near(two.W / two.H, 1.4f, 0.005f, "two chevrons: 1120 x 800 mm");
            // The chevron points where the setting says: the tip of the left one is its leftmost point and lies in the left half.
            GuideBoard left = Make("gb.mand.curve", "form=1;dir=l"), right = Make("gb.mand.curve", "form=1;dir=r");
            GuideElement fl = left.Main.Elements.Find(e => e.Kind == GuideElementKind.Path), fr = right.Main.Elements.Find(e => e.Kind == GuideElementKind.Path);
            GuidePath.Bounds(fl.Contours, out float lx0, out _, out float lx1, out _);
            GuidePath.Bounds(fr.Contours, out float rx0, out _, out float rx1, out _);
            float mid = left.Main.W * 0.5f;
            Program.Check(Inside(fl, lx0 + 0.02f, left.Main.H * 0.5f) && !Inside(fl, lx1 - 0.02f, left.Main.H * 0.5f) && Inside(fr, rx1 - 0.02f, right.Main.H * 0.5f) && !Inside(fr, rx0 + 0.02f, right.Main.H * 0.5f),
                "dir=l has its point on the left and dir=r on the right");
            Program.Check(Math.Abs((lx0 + lx1) * 0.5f - mid) < 1e-4f && Math.Abs((rx0 + rx1) * 0.5f - mid) < 1e-4f, "and each is centred on its board");
        }

        private static void Interchange()
        {
            GuideTemplate t = GuideTemplates.ById("gb.mand.xj");
            Program.Check(t != null && t.Group == StandardSigns.Mandatory && t.Standalone && t.FieldOf("dir") != null, "the sign is in the list of the mandatory signs with a turn setting");
            GuideBoard l = Make("gb.mand.xj", "dir=l"), r = Make("gb.mand.xj", "dir=r");
            Program.Check(l.Main.Shape == GuidePartShape.Circle && r.Main.Shape == GuidePartShape.Circle && Math.Abs(l.Main.W - r.Main.W) < 1e-6f, "round, one size");
            const GuideColour W = GuideColour.White, B = GuideColour.Blue;
            // 立体交叉直行和左转弯行驶 (units of the diameter, read off the 85 px disc of the photograph): the stem, the loop with a hole, the bar of its lower side, the arrow to the left.
            Program.Check(ColourAt(l, 54f, 30f) == W && ColourAt(l, 54f, 80f) == W, "left sign: the stem runs from the head to the foot");
            Program.Check(ColourAt(l, 75.5f, 52f) == W && ColourAt(l, 66f, 42.5f) == W && ColourAt(l, 66f, 62f) == W, "left sign: the loop's right side, top and lower bar");
            Program.Check(ColourAt(l, 64.4f, 52.5f) == B, "left sign: the loop is a ring, not a disc");
            Program.Check(ColourAt(l, 56f, 52f) == W && ColourAt(l, 56f, 61f) == W, "left sign: the stem is solid where the loop and the arrow cross it (overlapping solids join)");
            Program.Check(ColourAt(l, 40f, 61f) == W && ColourAt(l, 26f, 61f) == W, "left sign: the arrow to the left leaves the stem at the loop's lower bar");
            Program.Check(ColourAt(l, 40f, 40f) == B && ColourAt(l, 40f, 80f) == B && ColourAt(l, 85f, 52f) == B, "left sign: nothing else");
            // 立体交叉直行和右转弯行驶: the stem further left, a bar across to its left, the branch bending out of the stem's foot to the arrow along the bar's line.
            Program.Check(ColourAt(r, 40.5f, 30f) == W && ColourAt(r, 40.5f, 82f) == W, "right sign: the stem runs from the head to the foot");
            Program.Check(ColourAt(r, 25f, 49f) == W && ColourAt(r, 25f, 60f) == B && ColourAt(r, 25f, 38f) == B, "right sign: the short bar to the left of the stem, only that");
            Program.Check(ColourAt(r, 48.1f, 56.6f) == W && ColourAt(r, 56f, 62f) == B, "right sign: the branch is a bend (white on the curve, blue inside it)");
            Program.Check(ColourAt(r, 76f, 49f) == W && ColourAt(r, 76f, 30f) == B && ColourAt(r, 76f, 70f) == B, "right sign: the arrow along the bar's line");
            // The branch leaves the stem without a step: the stem's left edge just above and just below where the bend begins (y 75) is the same.
            float Edge(GuideBoard b, float y)
            {
                for (float x = 30f; x < 50f; x += 0.02f)
                {
                    if (ColourAt(b, x, y) == W)
                        return x;
                }

                return -1f;
            }

            Program.Check(Edge(r, 73f) > 0f && Math.Abs(Edge(r, 73f) - Edge(r, 77f)) < 0.2f, "right sign: no step where the bend leaves the stem: " + Edge(r, 73f) + " and " + Edge(r, 77f));
            Program.Check(BoardPrint.Hash(l) != BoardPrint.Hash(r), "the two are different boards (the right one is not the left one turned)");
            // Both figures stay inside the ring of the disc at every size.
            foreach (string speed in new[] { "100", "30" })
            foreach (string dir in new[] { "l", "r" })
            {
                GuideBoard b = Make("gb.mand.xj", "dir=" + dir + ";speed=" + speed);
                foreach (GuideElement e in b.Main.Elements)
                {
                    if (e.Art)
                        GuideTests.CheckInside(b.Main.Safe, e, "interchange " + dir + " at " + speed);
                }
            }
        }

        private static string TextOf(GuidePart part) => string.Concat(part.Elements.Select(e => e.Text));

        private static AssemblyBoard Placed(string id, string spec, int slot) =>
            new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById(id), spec), Slot = slot, Member = slot };

        /// <summary>The boards on one post as Assembly puts them: the placements in the order of the boards.</summary>
        private static List<Placement> Stack(params AssemblyBoard[] boards)
        {
            List<AssemblyBoard> ordered = Assembly.Order(boards);
            SignDef total = Assembly.TotalDef(ordered, false);
            SupportLayout layout = SupportLayouts.Of(total, SupportPlanner.Auto(total));
            return Assembly.Arrange(ordered, false, layout);
        }

        private static void DistancePlate()
        {
            GuideTemplate plate = GuideTemplates.ById("gb.aux.dist.green");
            Program.Check(plate != null && plate.Group == StandardSigns.Auxiliary && plate.Standalone && plate.Code == MandatorySigns.UrbanCode, "the plate is an auxiliary sign of the urban kind");
            float last = 0f;
            foreach (string band in new[] { "30", "60", "80", "100" })
            {
                GuideBoard board = plate.Layout(plate.Default().With("speed", band));
                GuidePart part = board.Main;
                string what = "plate at " + band;
                Program.Check(board.Parts.Count == 1 && part.Ground == GuideColour.Green && part.BorderColour == GuideColour.White, what + ": white on green, one board");
                string text = TextOf(part);
                Program.Check(text.Contains("50") && text.Contains("m") && !text.Contains("km"), what + ": 50 m: " + text);
                Program.Check(part.H > last, what + ": larger with the road's speed: " + part.H);
                last = part.H;
                foreach (GuideElement e in part.Elements)
                {
                    if (e.Art)
                        GuideTests.CheckInside(part.Safe, e, what);
                }
            }

            GuidePart km = plate.Layout(plate.Default().With("dist", "1.5").With("unit", "km")).Main;
            Program.Check(TextOf(km).Contains("1.5") && TextOf(km).Contains("km"), "the value and the unit are editable: " + TextOf(km));
            Program.Check(plate.Layout(plate.Default().With("dist", "")).Main.W > 0f, "an empty value still gives a board");

            // On a post, under the blue sign: the auxiliary rule of Assembly (it is a part of the board it explains) puts it right under, centred, the standard gap between.
            List<Placement> stack = Stack(Placed("gb.mand.curve", "form=2;count=2", 0), Placed("gb.aux.dist.green", "", 1));
            Program.Check(stack.Count == 2 && stack[0].Def.Id.Contains("gb.mand.curve") && stack[1].Def.Id.Contains("gb.aux.dist.green"), "the blue sign above and the plate under it: " + string.Join(", ", stack.ConvertAll(p => p.Def.Id)));
            if (stack.Count == 2)
            {
                Program.Near(stack[0].Bottom - stack[1].Top, Assembly.StackGap, 1e-4f, "the standard gap between them");
                Program.Near(stack[0].Cx, stack[1].Cx, 1e-5f, "on one middle line");
            }

            // The caption of 示41 in the photograph is what 辅助标志（文字） already is: added after 示41 it goes under it the same way (one added before any main board goes first: Assembly.Order).
            List<Placement> caption = Stack(Placed("gb.mand.41", "", 0), Placed("gb.aux.text", "text=允许掉头", 1));
            Program.Check(caption.Count == 2 && caption[0].Def.Id.Contains("gb.mand.41") && caption[1].Def.Id.Contains("gb.aux.text"), "示41 above and the caption under it: " + string.Join(", ", caption.ConvertAll(p => p.Def.Id)));
            Program.Check(TextOf(SignInstances.Of(SignCatalog.ById("gb.aux.text"), "text=允许掉头").Board.Main).Contains("允许掉头"), "the caption board says 允许掉头");
        }

        private static void UTurn()
        {
            // E.95 on the standard's own sheet (page 224 of GB 5768.2-2022, rendered 2000 px wide): the board is the rounded rectangle 614 to 1433 px across (100 units), the legs' outlines at
            // 879/966 and 1173/1261 px, the arc's top at 11.8 units and the arrow's tip at 88.4 (measured once; the code's figure is those numbers). Read back from the board's contours.
            GuideBoard b = Make("gb.mand.41", "");
            GuidePart part = b.Main;
            GuideElement fig = part.Elements.Find(e => e.Kind == GuideElementKind.Path);
            float unit = part.W / 100f;
            bool[] row = new bool[1001];
            for (int i = 0; i <= 1000; i++)
                row[i] = Inside(fig, i * 0.1f * unit, 45f * unit);
            var spans = new List<(float, float)>();
            for (int i = 0; i <= 1000; i++)
            {
                if (row[i] && (i == 0 || !row[i - 1]))
                    spans.Add((i * 0.1f, i * 0.1f));
                if (row[i])
                    spans[spans.Count - 1] = (spans[spans.Count - 1].Item1, i * 0.1f);
            }

            // 45 units down is below the arc (its centre is at 35.2) and above the arrow's barbs (they end at 54.8): the two legs only.
            Program.Check(spans.Count == 2, "two legs cross the row at 45: " + spans.Count);
            if (spans.Count == 2)
            {
                Program.Near(spans[0].Item1, 32.4f, 0.8f, "left leg's outer edge");
                Program.Near(spans[0].Item2, 43.0f, 0.8f, "left leg's inner edge");
                Program.Near(spans[1].Item1, 68.3f, 0.8f, "right leg's inner edge");
                Program.Near(spans[1].Item2, 79.0f, 0.8f, "right leg's outer edge");
            }

            GuidePath.Bounds(fig.Contours, out _, out float top, out _, out float bottom);
            Program.Near(top / unit, 11.8f, 0.8f, "the arc's top");
            Program.Near(bottom / unit, 88.4f, 0.8f, "the arrow's tip");
            // The photographed one (ref64) is a smaller figure: 34 units across and 56 high, legs 8.8 wide, 26.5 apart. It is not the standard's; the standard's is kept.
            float across = spans.Count == 2 ? spans[1].Item2 - spans[0].Item1 : 0f;
            Program.Check(across > 44f, "the figure is the standard's 46.6 units across, not the photograph's 34: " + across);
        }
    }
}
