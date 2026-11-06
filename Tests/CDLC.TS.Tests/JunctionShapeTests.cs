using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The roads of the junction diagrams (路1, 路2, Fig. 203 c; Fig. 205, 206, E.167, E.168) measured against the standard's drawings: every road keeps its width to its end and ends in a right-angled
    /// point (a gable). 0.5.0 to 0.5.8 flared the end out by a quarter of the width before it pointed (a kite), which no figure of the standard has; no test looked at the shape of an end, so none
    /// noticed (the owner did, on 2026-10-19).
    /// </summary>
    internal static class JunctionShapeTests
    {
        public static void RunAll()
        {
            Program.Run("junction diagrams: the ends of the roads are gables of the road's own width", Gables);
            Program.Run("junction diagrams: the figure is inside the board and the road is 0.4 h wide", WidthAndFit);
            Program.Run("Fig. 209: road numbers and their arrows on one post, in the order of the figure", Fig209);
            Program.Run("路4: up to six lanes, an arrow for every lane, the strip of arrows alone (Fig. H.1 a)", LaneStrips);
        }

        private static GuideElement FigureOf(string id, string spec)
        {
            SignInstance instance = SignInstances.Of(SignCatalog.ChoiceById(id).Def, spec);
            Program.Check(instance.Board != null, id + " is a board");
            foreach (GuideElement e in instance.Board.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Path && e.Art && e.Contours != null)
                    return e;
            }

            return null;
        }

        /// <summary>The interior angles of a polygon (degrees) in the order of its vertices; the sign of the winding does not matter.</summary>
        private static List<double> Angles(float[] c)
        {
            int n = c.Length / 2;
            double area = 0.0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += c[i * 2] * (double)c[j * 2 + 1] - c[j * 2] * (double)c[i * 2 + 1];
            }

            double winding = area >= 0.0 ? 1.0 : -1.0;
            var angles = new List<double>();
            for (int i = 0; i < n; i++)
            {
                int p = (i + n - 1) % n, q = (i + 1) % n;
                double ax = c[i * 2] - c[p * 2], ay = c[i * 2 + 1] - c[p * 2 + 1];
                double bx = c[q * 2] - c[i * 2], by = c[q * 2 + 1] - c[i * 2 + 1];
                double turn = Math.Atan2(ax * by - ay * bx, ax * bx + ay * by) * winding;
                angles.Add(180.0 - turn * 180.0 / Math.PI);
            }

            return angles;
        }

        private static bool IsGable(float[] c)
        {
            if (c.Length != 10)
                return false;
            var a = Angles(c);
            int right = 0, shoulder = 0;
            foreach (double d in a)
            {
                right += Math.Abs(d - 90.0) < 1.0 ? 1 : 0;
                shoulder += Math.Abs(d - 135.0) < 1.0 ? 1 : 0;
            }

            return right == 3 && shoulder == 2;
        }

        private static float Dist(float[] c, int a, int b) => (float)Math.Sqrt((c[a * 2] - c[b * 2]) * (c[a * 2] - c[b * 2]) + (c[a * 2 + 1] - c[b * 2 + 1]) * (c[a * 2 + 1] - c[b * 2 + 1]));

        private static void Gables()
        {
            // The forms of 路1: how many roads end in a point (the road it comes from, at the bottom, is square).
            foreach ((string form, int ends) in new[] { ("x", 3), ("t", 2), ("r", 2), ("l", 2) })
            {
                GuideElement figure = FigureOf("gb.guide8.01", "form=" + form + ";to1=甲;tl1=乙;tr1=丙;xroad=;dir=;speed=60");
                Program.Check(figure != null, "路1 " + form + ": a figure");
                if (figure == null)
                    continue;
                int gables = 0, others = 0, kites = 0;
                foreach (float[] c in figure.Contours)
                {
                    if (IsGable(c))
                        gables++;
                    else if (c.Length == 14)
                        kites++;
                    else if (c.Length != 8 && c.Length < 20)
                        others++;
                }

                Program.Check(kites == 0, "路1 " + form + ": no road flares out before its end (a seven-sided contour): " + kites);
                Program.Check(gables == ends, "路1 " + form + ": " + ends + " roads end in a gable: " + gables);
            }

            // The gable itself, in every one of them: the apex is as far forward of the shoulders as the road is half wide (90 degrees), the sides are the road's width apart, and both bevels are equal.
            foreach (string id in new[] { "gb.guide8.01", "gb.guide8.02", "gb.guide8.203c" })
            {
                GuideElement figure = FigureOf(id, id == "gb.guide8.01" ? "form=x;speed=60" : "speed=60");
                Program.Check(figure != null, id + ": a figure");
                if (figure == null)
                    continue;
                int seen = 0;
                float width = -1f;
                foreach (float[] c in figure.Contours)
                {
                    if (!IsGable(c))
                        continue;
                    seen++;
                    // The apex is the vertex whose neighbours' edges are equal and whose angle is 90 degrees and that is not on the base: the shoulders are the two vertices of 135 degrees.
                    var a = Angles(c);
                    int apex = -1;
                    for (int i = 0; i < 5; i++)
                    {
                        int p = (i + 4) % 5, q = (i + 1) % 5;
                        if (Math.Abs(a[i] - 90.0) < 1.0 && Math.Abs(a[p] - 135.0) < 1.0 && Math.Abs(a[q] - 135.0) < 1.0)
                            apex = i;
                    }

                    Program.Check(apex >= 0, id + ": the point has a shoulder of 135 degrees on each side");
                    if (apex < 0)
                        continue;
                    int s1 = (apex + 4) % 5, s2 = (apex + 1) % 5;
                    float bevel1 = Dist(c, apex, s1), bevel2 = Dist(c, apex, s2), across = Dist(c, s1, s2);
                    Program.Near(bevel1, bevel2, 1e-3f, id + ": the two bevels are equal");
                    Program.Near(across, bevel1 * (float)Math.Sqrt(2.0), 1e-3f, id + ": the point is right-angled (the shoulders are a bevel times root 2 apart)");
                    if (width < 0f)
                        width = across;
                    Program.Near(across, width, 1e-3f, id + ": every road of the figure is as wide as the others");
                }

                Program.Check(seen >= 2, id + ": roads end in gables: " + seen);
            }

            // The roundabout has five roads in a point (four around, the road it comes from is square).
            GuideElement ring = FigureOf("gb.guide8.02", "speed=60");
            int ringGables = 0;
            foreach (float[] c in ring.Contours)
                ringGables += IsGable(c) ? 1 : 0;
            Program.Check(ringGables == 4, "路2: four roads end in a gable: " + ringGables);

            // The detector itself: a kite and a plain bar are not gables; a gable turned any way is one (the roundabout's roads lie at all angles).
            Program.Check(!IsGable(new float[] { 0, 1, 4, 1, 5, 0, 4, -1, 0, -1, -1, 0 }), "a six-sided pointed bar is not a gable");
            Program.Check(!IsGable(new float[] { 0, 1, 4, 1, 4, -1, 0, -1 }), "a bar is not a gable");
            for (int deg = 0; deg < 360; deg += 15)
            {
                double r = deg * Math.PI / 180.0, cs = Math.Cos(r), sn = Math.Sin(r);
                float[] bar = { 0f, 1f, 4f, 1f, 5f, 0f, 4f, -1f, 0f, -1f };
                var turned = new float[10];
                for (int i = 0; i < 5; i++)
                {
                    turned[i * 2] = (float)(bar[i * 2] * cs - bar[i * 2 + 1] * sn);
                    turned[i * 2 + 1] = (float)(bar[i * 2] * sn + bar[i * 2 + 1] * cs);
                }

                Program.Check(IsGable(turned), "a gable turned " + deg + " degrees is a gable");
            }
        }

        private static void WidthAndFit()
        {
            foreach (string form in new[] { "x", "t", "r", "l" })
            {
                SignInstance instance = SignInstances.Of(SignCatalog.ChoiceById("gb.guide8.01").Def, "form=" + form + ";to1=甲;tl1=乙;tr1=丙;speed=60");
                float h = instance.Board.H;
                GuideElement figure = null;
                foreach (GuideElement e in instance.Board.Main.Elements)
                {
                    if (e.Kind == GuideElementKind.Path && e.Art)
                        figure = e;
                }

                Program.Check(figure != null && h > 0f, "路1 " + form + ": a figure and a character height");
                if (figure == null)
                    continue;
                foreach (float[] c in figure.Contours)
                {
                    if (!IsGable(c))
                        continue;
                    var a = Angles(c);
                    for (int i = 0; i < 5; i++)
                    {
                        int p = (i + 4) % 5, q = (i + 1) % 5;
                        if (Math.Abs(a[i] - 90.0) < 1.0 && Math.Abs(a[p] - 135.0) < 1.0 && Math.Abs(a[q] - 135.0) < 1.0)
                            Program.Near(Dist(c, p, q), 0.4f * h, 1e-3f * h, "路1 " + form + ": a road is 0.4 h wide (E.167)");
                    }
                }

                // Inside the board (the figure's box and the part's).
                GuidePart part = instance.Board.Main;
                Program.Check(figure.X >= -1e-3f && figure.Y >= -1e-3f && figure.X + figure.W <= part.W + 1e-3f && figure.Y + figure.H <= part.H + 1e-3f, "路1 " + form + ": the figure is inside the board");
            }
        }

        /// <summary>The road number and the arrow plate under it, one pair over the other, on one post (Fig. 209 a and b): the boards are built in the figure's order whether the player ordered them or not.</summary>
        private static void Fig209()
        {
            uint post = SupportCatalog.ById(SupportCatalog.SinglePole).Hash;
            string Spec(string id, string key, string value) => SignCatalog.ChoiceById(id).Def.Template.Default().With(key, value).ToString();
            string g108 = Spec("gb.guide8.05", "code", "G108"), s209 = Spec("gb.guide8.05", "code", "S209"), left = Spec("gb.aux.06", "arrow", "c"), both = Spec("gb.aux.09", "form", "a");
            ItemRecord Member(string id, string spec, int slot, bool manual) =>
                new ItemRecord(SignCatalog.HashOf(id), new Vec3(0f, 0f, 0f), 0f, post, spec, 0f, 209u, slot, 0, 0f) { Manual = manual };

            foreach (bool manual in new[] { true, false })
            {
                // a): one number and one arrow.
                var a = new List<ItemRecord> { Member("gb.guide8.05", g108, 0, manual), Member("gb.aux.06", left, 1, manual) };
                a.Sort(ItemGroups.Compare);
                SignParts partsA = ChunkBuilder.PartsOfGroup(a);
                Program.Check(partsA != null && partsA.Faces.Count == 2, "Fig. 209 a built: two boards (manual " + manual + ")");

                // b): two numbers, each with its arrow.
                var b = new List<ItemRecord> { Member("gb.guide8.05", g108, 0, manual), Member("gb.aux.06", left, 1, manual), Member("gb.guide8.05", s209, 2, manual), Member("gb.aux.09", both, 3, manual) };
                b.Sort(ItemGroups.Compare);
                SignParts parts = ChunkBuilder.PartsOfGroup(b);
                Program.Check(parts != null && parts.Faces.Count == 4, "Fig. 209 b built: four boards (manual " + manual + ")");
                if (parts == null || parts.Faces.Count != 4)
                    continue;
                var boards = new List<KeyValuePair<float, FacePart>>();
                foreach (FacePart face in parts.Faces)
                {
                    face.Mesh.Bounds(out Vec3 lo, out Vec3 hi);
                    boards.Add(new KeyValuePair<float, FacePart>((lo.Y + hi.Y) * 0.5f, face));
                }

                boards.Sort((x, y) => y.Key.CompareTo(x.Key));
                string[] ids = { "gb.guide8.05", "gb.aux.06", "gb.guide8.05", "gb.aux.09" };
                for (int i = 0; i < 4; i++)
                    Program.Check(boards[i].Value.Instance.Def.Id == ids[i], "from the top, board " + (i + 1) + " is " + ids[i] + " (manual " + manual + "): " + boards[i].Value.Instance.Def.Id);
                Program.Check(boards[0].Value.Instance.Spec.Contains("G108") && boards[2].Value.Instance.Spec.Contains("S209"), "the road numbers are in the order given: G108, then S209 (manual " + manual + ")");
                for (int i = 1; i < 4; i++)
                    Program.Check(boards[i - 1].Key > boards[i].Key + 0.2f, "the boards are one over the other with room between (manual " + manual + ")");
            }
        }

        /// <summary>路4 with the lane count of 0.5.9: the default is what it was; a count gives that many lanes; with no information at all it is the strip of arrows of Fig. H.1 a.</summary>
        private static void LaneStrips()
        {
            SignChoice lanes = SignCatalog.ChoiceById("gb.guide8.04");
            SignChoice strip = SignCatalog.ChoiceById("gb.guide8.04b");
            Program.Check(lanes != null && strip != null && lanes.Group == "guide" && strip.Group == "guide" && lanes.Code == "路4" && strip.Code == "路4", "the two entries are 路4");
            int Arrows(SignInstance instance)
            {
                int n = 0;
                foreach (GuideElement e in instance.Board.Main.Elements)
                    n += e.Kind == GuideElementKind.Path && e.Art ? 1 : 0;
                return n;
            }

            int Dividers(SignInstance instance)
            {
                int n = 0;
                foreach (GuideElement e in instance.Board.Main.Elements)
                    n += e.Kind == GuideElementKind.Rect ? 1 : 0;
                return n;
            }

            // What an item saved before the count existed still draws: the lanes that have an information (two in the default), the same height as before.
            SignInstance old = SignInstances.Of(lanes.Def, "to1=东莞;a1=s;to2=深圳;a2=r;to3=;a3=r");
            SignInstance now = SignInstances.Of(lanes.Def, lanes.Spec);
            Program.Check(Arrows(old) == 2 && Arrows(now) == 2, "the default draws two lanes: " + Arrows(old) + " and " + Arrows(now));
            Program.Check(old.Board.Main.W == now.Board.Main.W && old.Board.Main.H == now.Board.Main.H, "and an item saved without the count is the same board");

            // A count makes that many lanes, with or without an information.
            for (int n = 1; n <= RoadGuideSigns.MaxLanes; n++)
            {
                SignInstance made = SignInstances.Of(lanes.Def, "n=" + n + ";to1=;a1=l;to2=;a2=s;to3=;a3=s;to4=;a4=r;to5=;a5=sl;to6=;a6=sr");
                Program.Check(Arrows(made) == n, n + " lanes with no information: " + Arrows(made) + " arrows");
                Program.Check(Dividers(made) >= (n > 1 ? n - 1 : 0), n + " lanes have dividers between them: " + Dividers(made));
            }

            // The strip of Fig. H.1 a: a left turn, two ahead, a right turn; no information row: the board is lower than the same lanes with names, and as high as arrow plus margins.
            SignInstance strip4 = SignInstances.Of(strip.Def, strip.Spec);
            SignInstance named4 = SignInstances.Of(lanes.Def, "n=4;to1=甲;a1=l;to2=乙;a2=s;to3=丙;a3=s;to4=丁;a4=r");
            Program.Check(Arrows(strip4) == 4, "the strip has four arrows: " + Arrows(strip4));
            Program.Check(strip4.Board.Main.H < named4.Board.Main.H - 0.8f * strip4.Board.H, "the strip is lower than the same lanes with names by an information row: " + strip4.Board.Main.H + " against " + named4.Board.Main.H);
            Program.Check(strip4.Board.Main.W > strip4.Board.Main.H * 2f, "and long and low: " + strip4.Board.Main.W + " x " + strip4.Board.Main.H);

            // More than six, and a count below the lanes that have an information: kept to the count.
            SignInstance many = SignInstances.Of(lanes.Def, "n=9;to1=甲;to2=乙;to3=丙;to4=丁;to5=戊;to6=己");
            Program.Check(Arrows(many) <= RoadGuideSigns.MaxLanes, "a count over six is kept to six: " + Arrows(many));
            SignInstance fewer = SignInstances.Of(lanes.Def, "n=2;to1=甲;to2=乙;to3=丙;to4=丁");
            Program.Check(Arrows(fewer) == 2, "a count of two with four informations shows two lanes: " + Arrows(fewer));

            // Every field of the sign has a name in both languages and the choices of the count are the seven.
            GuideField count = lanes.Def.Template.FieldOf("n");
            Program.Check(count != null && count.Choices != null && count.Choices.Length == 7, "the count has seven choices (the old rule, 1 to 6)");
        }
    }
}
