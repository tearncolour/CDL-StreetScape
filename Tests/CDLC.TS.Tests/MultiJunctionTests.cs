using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The sequential junctions board (gb.guide8.01c, 0.12.78): its settings and their defaults (the owner's photograph), what is shown for how many junctions and of which kind, the order up the stem,
    /// the sides, the plate, and that nothing in any combination overlaps the roads or each other or leaves the board.
    /// </summary>
    internal static class MultiJunctionTests
    {
        public static void RunAll()
        {
            Program.Run("multi junction: the template, its defaults and the visible settings", Template);
            Program.Run("multi junction: the order up the stem and the sides", Order);
            Program.Run("multi junction: how many junctions are shown", Counts);
            Program.Run("multi junction: at-grade and interchange are drawn differently", Kinds);
            Program.Run("multi junction: the plate, the English lines and the direction mark", Pieces);
            Program.Run("multi junction: every combination keeps its pieces apart and on the board", Combinations);
            Program.Run("multi junction: long texts and every speed band", LongAndBands);
            Program.Run("multi junction: instances and meshes", Instances);
            Program.Run("multi junction: names are never cut silently", Names);
            Program.Run("multi junction: the drawing in literal numbers", Literal);
        }

        private static GuideTemplate T => GuideTemplates.ById(RoadGuideSigns.MultiId);

        private static GuideSpec Spec(params string[] keyValues)
        {
            GuideSpec s = T.Default();
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
                s = s.With(keyValues[i], keyValues[i + 1]);
            return s;
        }

        private static GuideBoard Lay(params string[] keyValues) => T.Layout(Spec(keyValues));

        private static GuideElement TextOf(GuideBoard board, string text)
        {
            foreach (GuideElement e in board.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Text && e.Text == text)
                    return e;
            }

            return null;
        }

        private static GuideElement Figure(GuideBoard board)
        {
            foreach (GuideElement e in board.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Path)
                    return e;
            }

            return null;
        }

        private static float Area(float[] c)
        {
            float a = 0f;
            int n = c.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += c[i * 2] * c[j * 2 + 1] - c[j * 2] * c[i * 2 + 1];
            }

            return a * 0.5f;
        }

        /// <summary>The stem is the figure's first contour; the others are its bars, fillets and ramps. A hole is wound against the stem.</summary>
        private static List<float[]> Holes(GuideElement figure)
        {
            var holes = new List<float[]>();
            float stem = Area(figure.Contours[0]);
            for (int i = 1; i < figure.Contours.Count; i++)
            {
                if (Area(figure.Contours[i]) * stem < 0f)
                    holes.Add(figure.Contours[i]);
            }

            return holes;
        }

        private static void Box(float[] c, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue;
            x1 = y1 = float.MinValue;
            for (int i = 0; i + 1 < c.Length; i += 2)
            {
                x0 = Math.Min(x0, c[i]);
                x1 = Math.Max(x1, c[i]);
                y0 = Math.Min(y0, c[i + 1]);
                y1 = Math.Max(y1, c[i + 1]);
            }
        }

        private static float StemX(GuideBoard board)
        {
            Box(Figure(board).Contours[0], out float x0, out _, out float x1, out _);
            return (x0 + x1) * 0.5f;
        }

        private static float CentreY(GuideElement e) => e.Y + e.H * 0.5f;

        // ------------------------------------------------------------------ the template

        private static void Template()
        {
            GuideTemplate t = T;
            Program.Check(t != null && t.Group == "guide" && t.Code == "路1+" && t.Standalone, "the board is in the catalogue as a road guide board");
            SignChoice choice = SignCatalog.ChoiceById(RoadGuideSigns.MultiId);
            Program.Check(choice != null && Array.IndexOf(choice.Keywords, "立交") >= 0 && Array.IndexOf(choice.Keywords, "平面路口") >= 0, "and is found by 立交 and 平面路口");
            int same = 0;
            foreach (GuideTemplate other in GuideTemplates.All)
                same += other.Id == RoadGuideSigns.MultiId ? 1 : 0;
            Program.Check(same == 1, "once");

            GuideSpec d = t.Default();
            Program.Check(d.Get("road") == "科韵路" && d.Get("n") == "3", "the defaults are the owner's photograph: 科韵路, three junctions");
            Program.Check(d.Get("k1") == "p" && d.Get("b1") == "r" && d.Get("rz1") == "花城大道" && d.Get("re1") == "Huacheng Ave.", "the first is at-grade with 花城大道 on the right");
            Program.Check(d.Get("k2") == "p" && d.Get("b2") == "b" && d.Get("lz2") == "黄埔大道西" && d.Get("rz2") == "黄埔大道东", "the second is at-grade with 黄埔大道西 and 黄埔大道东");
            Program.Check(d.Get("k3") == "l" && d.Get("xz3") == "中山一立交" && d.Get("xe3") == "Zhongshan 1st Interchange", "the third is the interchange 中山一立交 with a loop ramp");
            Program.Check(d.Get("dz") == "中山大道" && d.Get("cz") == "广园快速" && d.Get("dir") == "北", "it leads to 中山大道, the plate 广园快速, the north mark");
            Program.Check(Encoding.UTF8.GetByteCount(d.ToString()) <= GuideSpec.MaxBytes - 100, "the defaults leave room in the saved field: " + Encoding.UTF8.GetByteCount(d.ToString()));
            Program.Check(t.Parse(d.ToString()).ToString() == d.ToString(), "a written spec reads back the same");
            Program.Check(d.With("k1", "x").Get("k1") == "p" && d.With("n", "9").Get("n") == "3" && d.With("b2", "z").Get("b2") == "b", "a kind, a count or a side that is not offered takes the default");

            // The fields that matter for the spec as it is.
            bool Shown(GuideSpec s, string key) => t.FieldOf(key).Visible == null || t.FieldOf(key).Visible(s);
            GuideSpec one = d.With("n", "1");
            Program.Check(Shown(d, "k3") && Shown(d, "xz3") && !Shown(one, "k3") && !Shown(one, "rz2") && Shown(one, "rz1") && !Shown(d.With("n", "2"), "xz3"), "a junction's settings are shown when it is");
            Program.Check(!Shown(d, "lz1") && Shown(d, "rz1") && Shown(d.With("b1", "l"), "lz1") && !Shown(d.With("b1", "l"), "rz1") && Shown(d.With("b1", "b"), "lz1") && Shown(d.With("b1", "b"), "rz1"), "a side's names are shown when it has a road");
            Program.Check(!Shown(d, "xz1") && Shown(d.With("k1", "d"), "xz1") && Shown(d.With("k1", "l"), "xe1"), "the name of an interchange is shown for an interchange only");
            Program.Check(Shown(d, "ce") && !Shown(d.With("cz", "G15"), "ce") && !Shown(d.With("cz", ""), "ce"), "the plate's English line is for a name, not for a number");
            foreach (GuideField f in t.Fields)
                Program.Check(f.Key == "speed" || f.Key == SignLighting.Key || f.Key == "dir" || f.Key == "road" || f.Key == "n" || f.Key == "dz" || f.Key == "de" || f.Key == "cz" || f.Key == "ce" || ((f.Key.Length == 2 || f.Key.Length == 3) && "kblrx".IndexOf(f.Key[0]) >= 0 && char.IsDigit(f.Key[f.Key.Length - 1])), "a known key: " + f.Key);
        }

        // ------------------------------------------------------------------ order and sides

        private static void Order()
        {
            GuideBoard b = Lay();
            GuideElement dest = TextOf(b, "中山大道"), inter = TextOf(b, "中山一立交"), w2 = TextOf(b, "黄埔大道西"), e2 = TextOf(b, "黄埔大道东"), e1 = TextOf(b, "花城大道"), own = TextOf(b, "科韵路");
            Program.Check(dest != null && inter != null && w2 != null && e2 != null && e1 != null && own != null, "all the names of the photograph are on the board");
            if (dest == null || inter == null || w2 == null || e2 == null || e1 == null || own == null)
                return;
            Program.Check(dest.Y < inter.Y && inter.Y < w2.Y && w2.Y < e1.Y && e1.Y < own.Y, "up the stem: the road it leads to, the third junction, the second, the first, the road the driver is on");
            Program.Near(CentreY(w2), CentreY(e2), 1e-4f, "the left and the right of one junction are on one bar");
            Program.Check(e1.Y > e2.Y, "the nearest junction is under the second");

            float stem = StemX(b);
            Program.Check(w2.Right < stem && inter.Right < stem && e2.X > stem && e1.X > stem, "the left names are left of the stem and the right names right of it");
            GuideElement w2e = TextOf(b, "Huangpu Ave.W."), e2e = TextOf(b, "Huangpu Ave.E."), ie = TextOf(b, "Zhongshan 1st Interchange");
            Program.Check(w2e != null && e2e != null && ie != null && Math.Abs(w2e.Right - w2.Right) < 1e-4f && Math.Abs(ie.Right - inter.Right) < 1e-4f && Math.Abs(e2e.X - e2.X) < 1e-4f && w2e.Y > w2.Y && e2e.Y > e2.Y,
                "a name's English line is under it, the left ones end where their names end and the right ones start where theirs start");
            Program.Check(own.X < stem && own.Right > stem, "the road the driver is on is under the stem");
            Program.Check(dest.X < stem && dest.Right > stem - 0.01f, "the road it leads to is over the stem");

            // The nearer a junction, the nearer the foot: the bars of the figure, found from the figure's contours, are in the order of the settings.
            GuideElement fig = Figure(b);
            var barYs = new List<float>();
            for (int i = 1; i < fig.Contours.Count; i++)
            {
                Box(fig.Contours[i], out float x0, out float y0, out float x1, out float y1);
                if (y1 - y0 < 0.5f * (x1 - x0) && x1 - x0 > 0.3f)
                    barYs.Add((y0 + y1) * 0.5f);
            }

            Program.Check(barYs.Count >= 4, "the bars of the junctions are in the figure: " + barYs.Count);

            // Sides: a road on one side only draws the figure on that side only.
            GuideBoard right = Lay("n", "2", "b1", "r", "b2", "r"), left = Lay("n", "2", "b1", "l", "b2", "l");
            float rStem = StemX(right), lStem = StemX(left);
            Box(Figure(right).Contours[0], out float sx0, out _, out float sx1, out _);
            Program.Near(Figure(right).X, sx0, 1e-4f, "roads on the right only: the figure starts at the stem's left edge");
            Box(Figure(left).Contours[0], out _, out _, out float lx1, out _);
            Program.Near(Figure(left).Right, lx1, 1e-4f, "roads on the left only: the figure ends at the stem's right edge");
            Program.Check(rStem > 0f && lStem > 0f, "the figure is on the board");
            Program.Check(TextOf(right, "黄埔大道西") == null && TextOf(right, "黄埔大道东") != null && TextOf(left, "黄埔大道东") == null && TextOf(left, "黄埔大道西") != null, "and so are the names");

            // A road with no name is still drawn.
            GuideBoard unnamed = Lay("n", "2", "lz2", "", "le2", "");
            Program.Check(TextOf(unnamed, "黄埔大道西") == null && Math.Abs(Figure(unnamed).W - Figure(Lay("n", "2")).W) < 1e-4f && Math.Abs(Figure(unnamed).H - Figure(Lay("n", "2")).H) < 1e-4f, "a road with no name is drawn with no name");
        }

        private static void Counts()
        {
            GuideBoard n1 = Lay("n", "1"), n2 = Lay("n", "2"), n3 = Lay("n", "3");
            Program.Check(TextOf(n1, "花城大道") != null && TextOf(n1, "黄埔大道西") == null && TextOf(n1, "中山一立交") == null, "one junction: the nearest only");
            Program.Check(TextOf(n2, "黄埔大道西") != null && TextOf(n2, "中山一立交") == null, "two junctions: not the interchange");
            Program.Check(TextOf(n3, "中山一立交") != null, "three junctions: the interchange");
            Program.Check(TextOf(n1, "中山大道") != null && TextOf(n1, "科韵路") != null, "the road it leads to and the road the driver is on are always there");
            Program.Check(n1.Main.H < n2.Main.H && n2.Main.H < n3.Main.H, "each junction makes the board taller: " + n1.Main.H + ", " + n2.Main.H + ", " + n3.Main.H);
            Program.Check(Figure(n1).Contours.Count < Figure(n2).Contours.Count && Figure(n2).Contours.Count < Figure(n3).Contours.Count, "and the figure larger");
        }

        // ------------------------------------------------------------------ the kinds

        private static void Kinds()
        {
            // Every board here is laid out in band 0 (the 100 km/h character height) so that one h serves them all: the default speed is automatic, which has another h.
            float h = RoadGuideSigns.CharHeight(0), road = RoadGuideSigns.MultiRoadK * h;
            GuideElement atGrade = Figure(Lay("k3", "p", "speed", "100")), loop = Figure(Lay("k3", "l", "speed", "100")), diamond = Figure(Lay("k3", "d", "speed", "100"));

            // One road width for the whole board: the stem is that wide, and so is every bar (the shorter side of a bar's box).
            Box(atGrade.Contours[0], out float sx0, out _, out float sx1, out _);
            Program.Near(sx1 - sx0, road, 0.02f * road, "the stem is one road width: " + (sx1 - sx0) / h + " h");
            foreach (GuideElement fig in new[] { atGrade, loop, diamond })
            {
                for (int i = 1; i < fig.Contours.Count; i++)
                {
                    Box(fig.Contours[i], out float x0, out float y0, out float x1, out float y1);
                    if (x1 - x0 > 1.2f * h && y1 - y0 < 1.5f * road)
                        Program.Near(y1 - y0, road, 0.03f * road, "a bar is one road width: " + (y1 - y0) / h + " h");
                }
            }

            // A grade-separated junction's bar is broken where the stem passes over it: a gap of MultiPassGap between the stem and the bar; an at-grade one springs from the stem.
            float Gap(GuideElement fig, bool right)
            {
                Box(fig.Contours[0], out float gx0, out _, out float gx1, out _);
                float stemEdge = right ? gx1 : gx0, best = float.MaxValue;
                for (int i = 1; i < fig.Contours.Count; i++)
                {
                    Box(fig.Contours[i], out float x0, out float y0, out float x1, out float y1);
                    if (y1 - y0 > 1.5f * road || x1 - x0 < 1.2f * h)
                        continue;
                    best = Math.Min(best, right ? x0 - stemEdge : stemEdge - x1);
                }

                return best;
            }

            GuideBoard separated = Lay("n", "1", "k1", "d", "b1", "r", "speed", "100"), grade = Lay("n", "1", "k1", "p", "b1", "r", "speed", "100");
            Program.Near(Gap(Figure(separated), true), RoadGuideSigns.MultiPassGap * h, 0.01f * h, "a grade-separated bar is exactly the pass gap away from the stem: " + Gap(Figure(separated), true) / h + " h");
            Program.Check(Gap(Figure(grade), true) <= 0.001f * h, "an at-grade bar starts on the stem, not a gap away: " + Gap(Figure(grade), true) / h + " h");

            // A loop is a P: a three-quarter ring above the bar, on the road's side, its leftmost point on the stem's axis (so the ring is the stem's bulge).
            GuideBoard onRight = Lay("k3", "l", "b3", "r", "speed", "100"), onLeft = Lay("k3", "l", "b3", "l", "speed", "100");
            float stem = StemX(onRight);
            float[] ring = RingOf(Figure(onRight), h);
            Program.Check(ring != null, "a loop ramp is a contour of the figure: it is a ring of a quarter-circle's reach");
            if (ring != null)
            {
                Box(ring, out float rx0, out float ry0, out float rx1, out float ry1);
                float rc = RoadGuideSigns.MultiLoopRadius * h;
                Program.Near(ry1 - ry0, 2f * rc + road, 0.03f * h, "the ring is a diameter and a road high: " + (ry1 - ry0) / h + " h");
                Program.Near(rx1 - rx0, 2f * rc + road, 0.03f * h, "and as wide (it is round): " + (rx1 - rx0) / h + " h");
                Program.Check(Math.Abs(rx0 - (stem - road * 0.5f)) < 0.03f * h, "its near side is the stem's near edge, the ring is the stem's bulge: " + (rx0 - (stem - road * 0.5f)) / h + " h");
                Program.Check((rx0 + rx1) * 0.5f > stem + 0.01f, "a road on the right: the ring is on the right of the stem");
            }

            Program.Check(Figure(onLeft) != null && RingOf(Figure(onLeft), h) != null, "a road on the left has its ring");
            float[] leftRing = RingOf(Figure(onLeft), h);
            if (leftRing != null)
            {
                Box(leftRing, out float lx0, out _, out float lx1, out _);
                Program.Check((lx0 + lx1) * 0.5f < StemX(onLeft) - 0.01f, "a road on the left: the ring is on the left");
            }

            GuideBoard loopBoth = Lay("k3", "l", "b3", "b", "speed", "100");
            Program.Check(CountRings(Figure(loopBoth), h) == 2 && CountRings(Figure(onRight), h) == 1, "with roads on both sides a ring on each: " + CountRings(Figure(loopBoth), h));

            // A diamond's ramps reach 1.7 h above and below the bar (the figure is that tall around it); an at-grade junction's bar and fillets stay within 0.6 h of its axis.
            float Reach(GuideElement fig)
            {
                float reach = 0f;
                for (int i = 1; i < fig.Contours.Count; i++)
                {
                    Box(fig.Contours[i], out _, out float y0, out _, out float y1);
                    reach = Math.Max(reach, y1 - y0);
                }

                return reach;
            }

            Program.Check(Reach(diamond) > 1.6f * h && Reach(atGrade) < 0.5f * h, "a diamond's ramps are as tall as " + Reach(diamond) / h + " h, an at-grade junction's pieces " + Reach(atGrade) / h);
            Program.Check(Figure(Lay("k3", "d", "b3", "b", "speed", "100")).Contours.Count > diamond.Contours.Count, "a diamond with roads on both sides has the ramps of both");

            // An interchange has its name; an at-grade junction does not show one, whatever is typed.
            Program.Check(TextOf(Lay("k3", "d"), "中山一立交") != null && TextOf(Lay("k3", "p"), "中山一立交") == null, "the interchange's name is on an interchange only");
            Program.Check(TextOf(Lay("k3", "l"), "Zhongshan 1st Interchange") != null && TextOf(Lay("k3", "l", "xe3", ""), "Zhongshan 1st Interchange") == null && TextOf(Lay("k3", "l", "xe3", ""), "中山一立交") != null, "and its English line");

            // The name of an interchange stands beside the stem on the side that has no road (the ring is on the other side); with roads on both sides it stands under the road's name on the left.
            GuideBoard besideStem = Lay("k3", "l", "b3", "r");
            GuideElement name = TextOf(besideStem, "中山一立交");
            Program.Check(name != null && name.Right < StemX(besideStem), "an interchange with a road on the right: its name is left of the stem");
            GuideBoard stacked = Lay("n", "1", "k1", "d", "b1", "b", "lz1", "北环路", "le1", "", "xz1", "东站立交", "xe1", "");
            GuideElement above = TextOf(stacked, "北环路"), below = TextOf(stacked, "东站立交");
            Program.Check(above != null && below != null && above.Bottom <= below.Y + 1e-4f && Math.Abs(above.Right - below.Right) < 1e-4f, "with roads on both sides: the road's name over the interchange's, right-aligned");

            // The spec of one kind is not the spec of another.
            Program.Check(SignInstances.Of(SignCatalog.ById(RoadGuideSigns.MultiId), Spec("k3", "l").ToString()).Key != SignInstances.Of(SignCatalog.ById(RoadGuideSigns.MultiId), Spec("k3", "p").ToString()).Key, "another kind, another sign");
        }

        /// <summary>The loop's contour: the one whose box is as wide as it is high and as large as a ring (a bar is a long thin box, a ramp or fillet small).</summary>
        private static float[] RingOf(GuideElement figure, float h)
        {
            float rc = RoadGuideSigns.MultiLoopRadius * h;
            foreach (float[] c in figure.Contours)
            {
                Box(c, out float x0, out float y0, out float x1, out float y1);
                if (x1 - x0 > 1.5f * rc && y1 - y0 > 1.5f * rc && Math.Abs((x1 - x0) - (y1 - y0)) < 0.1f * rc)
                    return c;
            }

            return null;
        }

        private static int CountRings(GuideElement figure, float h)
        {
            float rc = RoadGuideSigns.MultiLoopRadius * h;
            int n = 0;
            foreach (float[] c in figure.Contours)
            {
                Box(c, out float x0, out float y0, out float x1, out float y1);
                if (x1 - x0 > 1.5f * rc && y1 - y0 > 1.5f * rc && Math.Abs((x1 - x0) - (y1 - y0)) < 0.1f * rc)
                    n++;
            }

            return n;
        }

        // ------------------------------------------------------------------ the plate, the English lines, the mark

        private static void Pieces()
        {
            bool Green(GuideBoard b)
            {
                foreach (GuideElement e in b.Main.Elements)
                {
                    if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Green)
                        return true;
                }

                return false;
            }

            GuideBoard chip = Lay();
            Program.Check(Green(chip) && TextOf(chip, "广园快速") != null && TextOf(chip, "Guangyuan Expwy.") != null, "a name on the plate is white on green with its English line");
            GuideElement plate = null, lead = TextOf(chip, "中山大道");
            foreach (GuideElement e in chip.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Green)
                    plate = e;
            }

            Program.Check(plate != null && plate.X > lead.Right && TextOf(chip, "广园快速").X > plate.X && TextOf(chip, "广园快速").Right < plate.Right, "the plate is right of the road it leads to, the name on it");
            Program.Near(CentreY(lead) - (plate.Y + plate.H * 0.5f), 0f, 0.35f * plate.H, "and level with it");

            GuideBoard number = Lay("cz", "G15");
            Program.Check(TextOf(number, "G15") != null && TextOf(number, "Guangyuan Expwy.") == null && TextOf(number, "广园快速") == null, "a road number is its own plate, with no English line");
            GuideBoard none = Lay("cz", "");
            Program.Check(!Green(none) && TextOf(none, "Guangyuan Expwy.") == null, "no plate when none is typed");
            Program.Check(TextOf(Lay("cz", "广园快速", "ce", ""), "Guangyuan Expwy.") == null && Green(Lay("cz", "广园快速", "ce", "")), "a plate with no English line");

            Program.Check(TextOf(Lay("de", ""), "Zhongshan Ave.") == null && TextOf(Lay(), "Zhongshan Ave.") != null, "the English line of the road it leads to");
            Program.Check(TextOf(Lay("dz", "", "de", "", "cz", ""), "中山大道") == null && Lay("dz", "", "de", "", "cz", "").Main.H > 0f, "a board with nothing over the stem");

            // The mark is in the corner and the figure stays out of it.
            GuideBoard marked = Lay(), plain = Lay("dir", "");
            Program.Check(TextOf(marked, "北") != null && TextOf(plain, "北") == null, "the direction mark is set by dir");
            foreach (string dir in new[] { "东", "南", "西", "北" })
                Program.Check(TextOf(Lay("dir", dir), dir) != null, "the mark " + dir);
        }

        // ------------------------------------------------------------------ pieces apart

        private static bool Overlap(GuideElement a, GuideElement b, float eps) => a.X < b.Right - eps && b.X < a.Right - eps && a.Y < b.Bottom - eps && b.Y < a.Bottom - eps;

        private static bool InPolygon(float[] c, float x, float y)
        {
            bool inside = false;
            int n = c.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = c[i * 2], yi = c[i * 2 + 1], xj = c[j * 2], yj = c[j * 2 + 1];
                if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>Whether a contour touches the inside of a box: a corner of the box in it, or a point along its sides in the box.</summary>
        private static bool Touches(float[] c, GuideElement box, float eps)
        {
            float x0 = box.X + eps, y0 = box.Y + eps, x1 = box.Right - eps, y1 = box.Bottom - eps;
            if (x1 <= x0 || y1 <= y0)
                return false;
            if (InPolygon(c, x0, y0) || InPolygon(c, x1, y0) || InPolygon(c, x0, y1) || InPolygon(c, x1, y1) || InPolygon(c, (x0 + x1) * 0.5f, (y0 + y1) * 0.5f))
                return true;
            int n = c.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                for (int k = 0; k <= 8; k++)
                {
                    float x = c[i * 2] + (c[j * 2] - c[i * 2]) * k / 8f, y = c[i * 2 + 1] + (c[j * 2 + 1] - c[i * 2 + 1]) * k / 8f;
                    if (x > x0 && x < x1 && y > y0 && y < y1)
                        return true;
                }
            }

            return false;
        }

        private static void Apart(GuideBoard board, string what)
        {
            GuidePart part = board.Main;
            float h = part.H * 0.0f + RoadGuideSigns.CharHeight(0);
            float eps = 0.01f * h;
            float inL = part.Edge - 1e-4f, inR = part.W - part.Edge + 1e-4f, inT = part.Edge - 1e-4f, inB = part.H - part.Edge + 1e-4f;
            var texts = new List<GuideElement>();
            GuideElement fig = null;
            foreach (GuideElement e in part.Elements)
            {
                Program.Check(e.X >= inL && e.Right <= inR && e.Y >= inT && e.Bottom <= inB, $"{what}: a {e.Kind} '{e.Text}' is on the board");
                if (e.Kind == GuideElementKind.Text && !string.IsNullOrEmpty(e.Text))
                    texts.Add(e);
                else if (e.Kind == GuideElementKind.Path)
                    fig = e;
            }

            for (int i = 0; i < texts.Count; i++)
            {
                for (int j = i + 1; j < texts.Count; j++)
                    Program.Check(!Overlap(texts[i], texts[j], eps), $"{what}: '{texts[i].Text}' and '{texts[j].Text}' do not overlap");
                if (fig != null)
                {
                    foreach (float[] contour in fig.Contours)
                        Program.Check(!Touches(contour, texts[i], eps), $"{what}: '{texts[i].Text}' does not touch the roads");
                }
            }

            if (fig != null && part.Safe != null)
                GuideTests.CheckInside(part.Safe, fig, what + ": the figure");
        }

        private static void Combinations()
        {
            int boards = 0;
            string[] kinds = { "p", "l", "d" }, sides = { "r", "l", "b" };
            foreach (int count in new[] { 1, 2, 3 })
            {
                int combos = (int)Math.Pow(9, count);
                for (int c = 0; c < combos; c++)
                {
                    var kv = new List<string> { "n", count.ToString() };
                    int rest = c;
                    for (int i = 1; i <= count; i++)
                    {
                        kv.Add("k" + i);
                        kv.Add(kinds[rest % 3]);
                        rest /= 3;
                        kv.Add("b" + i);
                        kv.Add(sides[rest % 3]);
                        rest /= 3;
                        // The first junction has no road names in the defaults on its left, the interchange none on the right: give every one a name and an interchange its own.
                        kv.Add("lz" + i);
                        kv.Add("北环路");
                        kv.Add("le" + i);
                        kv.Add("North Ring Rd.");
                        kv.Add("rz" + i);
                        kv.Add("南环路");
                        kv.Add("re" + i);
                        kv.Add("South Ring Rd.");
                        kv.Add("xz" + i);
                        kv.Add("东站立交");
                        kv.Add("xe" + i);
                        kv.Add("East Station Interchange");
                    }

                    GuideBoard board = Lay(kv.ToArray());
                    boards++;
                    Apart(board, "n=" + count + " #" + c);
                    float ratio = board.Main.W / board.Main.H;
                    Program.Check(ratio >= 1.3f && ratio < 4f, $"n={count} #{c}: the board is {ratio:0.00} times as wide as high");
                }
            }

            Program.Check(boards == 9 + 81 + 729, "every combination was laid out: " + boards);
        }

        private static void LongAndBands()
        {
            var random = new Random(78);
            string[] zh = { "", "路", "学府路", "中山南二路", "一二三四五六七", "AB12" }, en = { "", "Rd.", "Zhongshan 1st Interchange", "WWWWWWWWWWWWWWWWWWWWWWWW" };
            string[] kinds = { "p", "l", "d" }, sides = { "r", "l", "b" };
            for (int round = 0; round < 300; round++)
            {
                var kv = new List<string> { "n", (1 + random.Next(3)).ToString(), "dz", zh[random.Next(zh.Length)], "de", en[random.Next(en.Length)], "cz", new[] { "", "G15", "广园快速", "S209", "三环快速路" }[random.Next(5)], "dir", new[] { "", "北" }[random.Next(2)], "speed", new[] { "100", "80", "60", "30" }[random.Next(4)] };
                for (int i = 1; i <= 3; i++)
                {
                    kv.Add("k" + i); kv.Add(kinds[random.Next(3)]);
                    kv.Add("b" + i); kv.Add(sides[random.Next(3)]);
                    foreach (string key in new[] { "lz", "rz", "xz" }) { kv.Add(key + i); kv.Add(zh[random.Next(zh.Length)]); }
                    foreach (string key in new[] { "le", "re", "xe" }) { kv.Add(key + i); kv.Add(en[random.Next(en.Length)]); }
                }

                string what = "random " + round + " [" + string.Join(",", kv) + "]";
                GuideBoard board = Lay(kv.ToArray());
                Program.Check(board.Main.W > 0f && board.Main.H > 0f && !float.IsNaN(board.Main.W + board.Main.H), what + ": a board with a size");
                // The whole sign is in the character height of its band: Apart() reads band 0's h for its margin only.
                Apart(board, what);
            }

            // Everything is in the character height: the board of a band is the board of any other scaled by their heights.
            GuideBoard big = Lay("speed", "100"), small = Lay("speed", "30");
            float k = RoadGuideSigns.CharHeight(0) / RoadGuideSigns.CharHeight(3);
            Program.Near(big.Main.W / small.Main.W, k, 0.002f * k, "the board is in the character height: " + big.Main.W / small.Main.W);
            Program.Near(big.Main.H / small.Main.H, k, 0.002f * k, "in both directions");
        }


        // ------------------------------------------------------------------ names

        private static GuideSpec Full3(int names, int latin)
        {
            GuideSpec s = Spec("n", "3");
            for (int i = 1; i <= 3; i++)
            {
                s = s.With("k" + i, i == 3 ? "d" : "p").With("b" + i, "b");
                foreach (string k in new[] { "lz", "rz", "xz" })
                    s = s.With(k + i, new string('路', names));
                foreach (string k in new[] { "le", "re", "xe" })
                    s = s.With(k + i, new string('a', latin));
            }

            return s;
        }

        private static void Names()
        {
            // The limits of the board: ten characters of a name, twenty-six letters of an English line (the spec of a full board does not hold them all: see below).
            GuideSpec max = Spec("lz1", new string('路', 30), "le1", new string('a', 60), "road", new string('路', 30));
            Program.Check(max.Get("lz1").Length == RoadGuideSigns.MultiNameMax && max.Get("le1").Length == RoadGuideSigns.MultiEnglishMax && max.Get("road").Length == RoadGuideSigns.MultiNameMax, "a field holds its limit: " + max.Get("lz1").Length + ", " + max.Get("le1").Length);

            // A board of ordinary names (six characters, English of eight letters) fits the saved field whole: nothing is cut and the editor has no line about it.
            GuideSpec ordinary = Full3(6, 8);
            Program.Check(!ordinary.Trimmed && !ordinary.Full, "a board of six-character names and English of 8 letters fits: " + System.Text.Encoding.UTF8.GetByteCount(ordinary.ToString()) + " bytes");
            for (int i = 1; i <= 3; i++)
                Program.Check(ordinary.Get("lz" + i).Length == 6 && ordinary.Get("xz" + i).Length == 6 && ordinary.Get("le" + i).Length == 8, "and keeps every name of junction " + i);
            GuideBoard calm = T.Layout(ordinary);
            Program.Check(calm.English == null || !calm.English.SpecFull, "no line about full settings");

            // A board that does not fit is trimmed by the spec (the end of the longest text goes): that is known to the spec, and the editor says so in one line, in both languages.
            GuideSpec crowded = Full3(10, 26);
            Program.Check(crowded.Trimmed && crowded.Full, "a board of ten-character names and English of 26 letters does not fit and says so");
            GuideBoard warned = T.Layout(crowded);
            Program.Check(warned.English != null && warned.English.SpecFull, "the board carries the fact for the editor");
            var zh = new List<string>(Bilingual.Notes(warned.English, true));
            var en = new List<string>(Bilingual.Notes(warned.English, false));
            Program.Check(zh.Count == 1 && zh[0].Contains("设置已满") && en.Count == 1 && en[0].Contains("Settings full"), "the editor's line, in both languages: " + (zh.Count > 0 ? zh[0] : "none"));

            // A name wider than the room is shrunk, then wrapped in two lines: the two lines are the name, in order.
            string name = "中山南二路东延伸线";
            int cut = RoadGuideSigns.MultiBreak(name);
            Program.Check(cut > 1 && cut < name.Length - 1, "a long name is broken inside itself: " + cut);
            GuideBoard longName = Lay("n", "1", "b1", "b", "lz1", name, "le1", "", "rz1", "黑核桃街", "speed", "100");
            GuideElement first = TextOf(longName, name.Substring(0, cut)), second = TextOf(longName, name.Substring(cut));
            Program.Check(first != null && second != null && first.Bottom <= second.Y + 1e-4f && Math.Abs(first.Right - second.Right) < 1e-4f, "drawn whole in two lines, the first over the second, right-aligned (it is on the left)");

            // Where the break falls: at the word of a road when one stands near the middle, else in the middle; a short name or a mixed one is not broken.
            Program.Check(RoadGuideSigns.MultiBreak("肉桂公路立交桥") == 4 || RoadGuideSigns.MultiBreak("肉桂公路立交桥") == 3 || RoadGuideSigns.MultiBreak("肉桂公路立交桥") == 5, "a seven-character name is broken near the middle: " + RoadGuideSigns.MultiBreak("肉桂公路立交桥"));
            Program.Check(RoadGuideSigns.MultiBreak("学府路") == 0 && RoadGuideSigns.MultiBreak("AB12路") == 0 && RoadGuideSigns.MultiBreak("") == 0, "a short name and a mixed one are not broken");
        }

        // ------------------------------------------------------------------ the drawing in literal numbers

        /// <summary>
        /// The contract of the drawing with the numbers written out, not read from the constants they test (the first mutation round changed the road width, the pass gap and the loop's radius and no
        /// test noticed, because each compared the figure with the constant). A road is 0.36 h, the pass gap 0.14 h, the loop's centre line 0.62 h in radius; the boards are in the 100 km/h band (h = 0.65 m).
        /// </summary>
        private static void Literal()
        {
            Program.Near(RoadGuideSigns.CharHeight(0), 0.65f, 1e-4f, "the character height of the 100 km/h band is 0.65 m");
            GuideBoard grade = Lay("n", "1", "k1", "p", "b1", "r", "speed", "100"), sep = Lay("n", "1", "k1", "d", "b1", "r", "speed", "100"), loop = Lay("n", "1", "k1", "l", "b1", "r", "speed", "100");

            // The stem and every bar are 0.36 h wide: 0.234 m.
            Box(Figure(grade).Contours[0], out float s0, out _, out float s1, out _);
            Program.Near(s1 - s0, 0.234f, 0.002f, "the stem is 0.234 m wide: " + (s1 - s0));
            Box(Figure(grade).Contours[1], out _, out float b0, out _, out float b1);
            Program.Near(b1 - b0, 0.234f, 0.002f, "and the bar: " + (b1 - b0));

            // The pass gap: a separated junction's bar starts 0.14 h = 0.091 m from the stem's edge.
            Box(Figure(sep).Contours[0], out _, out _, out float e1, out _);
            Box(Figure(sep).Contours[1], out float g0, out _, out _, out _);
            Program.Near(g0 - e1, 0.091f, 0.002f, "the bar of a grade-separated junction starts 0.091 m from the stem: " + (g0 - e1));

            // The ends of the roads are the right-angled points of Fig. 205: five points (a gable), not four (square).
            Program.Check(Figure(grade).Contours[1].Length / 2 == 5, "an at-grade bar ends in a point: " + Figure(grade).Contours[1].Length / 2 + " points");
            Program.Check(Figure(sep).Contours[1].Length / 2 == 5, "and so does a separated one: " + Figure(sep).Contours[1].Length / 2);
            Program.Check(Figure(grade).Contours[0].Length / 2 == 5, "the stem ends in a point");

            // The inner corners of an at-grade junction are rounded (Fig. 205): two small contours of nine points or more, under a fifth of h across.
            int fillets = 0;
            for (int i = 2; i < Figure(grade).Contours.Count; i++)
            {
                Box(Figure(grade).Contours[i], out float x0, out float y0, out float x1, out float y1);
                if (x1 - x0 < 0.2f * 0.65f && y1 - y0 < 0.2f * 0.65f && Figure(grade).Contours[i].Length / 2 >= 9)
                    fillets++;
            }

            Program.Check(fillets == 2, "an at-grade junction has two rounded inner corners: " + fillets);

            // The loop: a ring on a centre line of 0.62 h = 0.403 m, a road wide: 2 x 0.403 + 0.234 = 1.040 m both ways.
            float[] ring = null;
            foreach (float[] c in Figure(loop).Contours)
            {
                Box(c, out float x0, out float y0, out float x1, out float y1);
                if (x1 - x0 > 0.8f && y1 - y0 > 0.8f)
                    ring = c;
            }

            Program.Check(ring != null, "the loop is a ring of the figure");
            if (ring == null)
                return;
            Box(ring, out float rx0, out float ry0, out float rx1, out float ry1);
            Program.Near(rx1 - rx0, 1.040f, 0.01f, "the ring is 1.04 m across: " + (rx1 - rx0));
            Program.Near(ry1 - ry0, 1.040f, 0.01f, "and 1.04 m high: " + (ry1 - ry0));

            // It starts on the bar's axis and ends on the stem's axis: its lowest edge is the bar's lower edge, its near side is on the stem's axis less half a road.
            Box(Figure(loop).Contours[0], out float lx0, out _, out float lx1, out _);
            float stemAxis = (lx0 + lx1) * 0.5f;
            Box(Figure(loop).Contours[1], out _, out float barTop, out _, out float barBottom);
            Program.Near(ry1, barBottom, 0.01f, "the ring's lowest edge is the bar's lower edge: " + (ry1 - barBottom));
            Program.Near(rx0 + 0.117f, stemAxis, 0.01f, "its near side is the stem's near edge: " + (rx0 + 0.117f - stemAxis));

            // The direction: it leaves the bar going away from the stem and comes round over the top: the ring's topmost point is a radius right of the stem's axis.
            float topX = float.NaN, top = float.MaxValue;
            for (int i = 0; i + 1 < ring.Length; i += 2)
            {
                if (ring[i + 1] < top)
                {
                    top = ring[i + 1];
                    topX = ring[i];
                }
            }

            Program.Near(topX - stemAxis, 0.403f, 0.05f, "the ring's top is a radius right of the stem's axis: " + (topX - stemAxis));

            // The ends of the thick line: the contour has the line's left offsets first (n points, the line's order) and the right offsets after (n points, back). So the middle of the first and the last of
            // the n left points and their right partners is the line's start and end. The line starts on the bar's axis (the ring's lowest point, y = the bar's centre) and ends on the stem's axis.
            int n = ring.Length / 4;
            float startX = (ring[0] + ring[(2 * n) * 2 - 2]) * 0.5f, startY = (ring[1] + ring[(2 * n) * 2 - 1]) * 0.5f;
            float endX = (ring[(n - 1) * 2] + ring[(n) * 2]) * 0.5f, endY = (ring[(n - 1) * 2 + 1] + ring[(n) * 2 + 1]) * 0.5f;
            float barAxisY = (barTop + barBottom) * 0.5f;
            Program.Near(startY, barAxisY, 0.01f, "the ring's line starts on the bar's axis: " + (startY - barAxisY));
            Program.Check(startX > stemAxis + 0.2f, "and away from the stem, on its right: " + (startX - stemAxis));
            Program.Near(endX, stemAxis, 0.01f, "it ends on the stem's axis: " + (endX - stemAxis));
            Program.Check(endY < barAxisY - 0.3f, "and well above the bar: " + (barAxisY - endY));

            // The interchange's name is level with the loop's centre, 0.403 m above the bar's axis (the road's name is level with the bar), on a board with the road on the right only.
            GuideBoard named = Lay("n", "1", "k1", "l", "b1", "r", "xz1", "东站立交", "xe1", "", "rz1", "北环路", "re1", "", "speed", "100");
            GuideElement name = TextOf(named, "东站立交"), road = TextOf(named, "北环路");
            Program.Check(name != null && road != null, "the interchange's name and the road's are on the board");
            if (name != null && road != null)
                Program.Near(CentreY(road) - CentreY(name), 0.403f, 0.03f, "the interchange's name is level with the loop's centre, 0.403 m above the bar: " + (CentreY(road) - CentreY(name)));
        }

        // ------------------------------------------------------------------ instances

        private static void Instances()
        {
            SignDef def = SignCatalog.ById(RoadGuideSigns.MultiId);
            foreach (string speed in new[] { "100", "80", "60", "30" })
            {
                SignInstance i = SignInstances.Of(def, Spec("speed", speed).ToString());
                // A board too large for the texture cap keeps the least pixels to the metre, as every large board does (the bands of a motorway: 11 m wide).
                bool capped = i.TexW <= SignInstance.MaxTexture + 4 && i.TexH <= SignInstance.MaxTexture + 4;
                Program.Check(i.IsBoard && i.TexW >= 16 && (capped || Math.Abs(i.PixelsPerMetre - SignInstance.MinPixelsPerMetre) < 1e-3f) && i.TexW % 4 == 0 && i.TexH % 4 == 0, "band " + speed + ": the picture " + i.TexW + "x" + i.TexH + " at " + i.PixelsPerMetre + " px/m");
                Program.Check(speed == "100" || speed == "80" || capped, "a city street's bands (40 to 70 and below) fit the texture cap: " + i.TexW + "x" + i.TexH);
                Program.Check(Math.Abs(i.Def.Width - i.Board.Main.W) < 1e-6f, "the instance has the board's size");
            }

            SignInstance instance = SignInstances.Of(def, Spec().ToString());
            foreach (SupportDef support in SupportCatalog.All)
            {
                SignParts parts = SignBuilder.Build(instance, support);
                Program.Check(parts.VertexCount > 0 && parts.Picks.Count >= 1, support.Id + ": the board can be built and pointed at");
            }
        }
    }
}
