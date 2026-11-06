using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The roadside posts of 0.12.78 (the owner, 3 Nov, with two photos: "地面高速常见的路边立桩 ... 斜纹路口牌"): the 柱式轮廓标 with a 百米牌 on it (GB 5768.2-2022 Fig. 287, GB/T 24970-2020), the 公路里程碑
    /// and 百米桩 (Fig. 243, 245) and the red and white striped board (警46's form 5). The geometry, the numbering along a run (also when the run is cut in pieces), the faces the figures stand on and how
    /// they read, and that 警46's four forms came out as they did before. The game's own part (the materials, the look at night, the road under them) is not here.
    /// </summary>
    internal static class RoadPostTests
    {
        /// <summary>The digest of the 144 specs of 警46's forms 1 to 4 as the code of 0.12.77 laid them out (made before the change: OldFormsDigest on that code).</summary>
        private const ulong kOldFormsDigest = 4620398339909604978UL;

        public static void RunAll()
        {
            Program.Run("road posts: 警46 forms 1 to 4 are as before", OldForms);
            Program.Run("road posts: 警46's red and white stripes lean to the side to pass, in the photo's phase", Stripes);
            Program.Run("road posts: the numbering, the places and the plates", Numbering);
            Program.Run("road posts: the templates are in the catalogue, listed with short settings", Catalogue);
            Program.Run("road posts: the post of GB/T 24970: its section, its length, the slope of its top, the black mark and the reflector", PostGeometry);
            Program.Run("road posts: a plate every hundred metres, none where the hundred is 0, and a run cut in pieces goes on", Plates);
            Program.Run("road posts: the plate's figures stand on the face, upright and not mirrored", PlateFigures);
            Program.Run("road posts: the stones and the posts of Fig. 243 and 245", Stones);
            Program.Run("road posts: both faces of a stone read right, the colour is the road's class", StoneFaces);
            Program.Run("road posts: stones and posts in pieces of a run", StonePieces);
        }

        // ------------------------------------------------------------------ helpers

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        /// <summary>A straight line along +Z in chords of 100 m or less (a path is written in the item's frame, from its origin along +Z, a chord at most 1000 m).</summary>
        internal static List<Vec3> Straight(float length)
        {
            var line = new List<Vec3>();
            for (float z = 0f; z < length - 1f; z += 100f)
                line.Add(P(0f, z));
            line.Add(P(0f, length));
            return line;
        }

        internal static MarkingPlan PlanOf(string id, string settings, List<Vec3> line, float phase = 0f, float total = 0f)
        {
            GuideTemplate t = MarkingTemplates.ById(id);
            string path = RunPath.Encode(line, out _);
            GuideSpec spec = t.Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), path, phase, total));
        }

        /// <summary>The vertices of the plan's parts of a solid colour (and of a detail, when it is given), all of them in one list.</summary>
        internal static List<Vec3> Of(MarkingPlan plan, SolidColour colour, DetailKind? detail = null)
        {
            var all = new List<Vec3>();
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid == colour && (detail == null || part.Detail == detail.Value))
                    all.AddRange(part.Mesh.Positions);
            }

            return all;
        }

        private static MeshBuilder MeshOf(MarkingPlan plan, SolidColour colour)
        {
            var mesh = new MeshBuilder();
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid == colour)
                    mesh.Append(part.Mesh, Vec3.Zero, 0f);
            }

            return mesh;
        }

        /// <summary>The places along z (rounded to a decimetre) of a colour's vertices that stand within reach of a post's footprint: the clusters, as a sorted list.</summary>
        internal static List<float> Clusters(List<Vec3> vertices, float within)
        {
            var centres = new List<float>();
            float previous = float.NaN, sum = 0f;
            int n = 0;
            foreach (float z in vertices.Select(v => v.Z).OrderBy(z => z))
            {
                if (n > 0 && z - previous > within)
                {
                    centres.Add(sum / n);
                    sum = 0f;
                    n = 0;
                }

                sum += z;
                n++;
                previous = z;
            }

            if (n > 0)
                centres.Add(sum / n);
            return centres;
        }

        internal static float Max(IEnumerable<float> values) => values.Any() ? values.Max() : float.NaN;

        internal static float Min(IEnumerable<float> values) => values.Any() ? values.Min() : float.NaN;

        // ------------------------------------------------------------------ 警46

        /// <summary>A digest of everything a laid-out board draws: the parts and their elements.</summary>
        internal static ulong Digest(GuideBoard board)
        {
            var f = new Fingerprint();
            foreach (GuidePart p in board.Parts)
            {
                f.Add(p.X); f.Add(p.Y); f.Add(p.W); f.Add(p.H); f.Add(p.Side); f.Add((int)p.Shape); f.Add((int)p.Ground); f.Add((int)p.BorderColour);
                f.Add(p.Liner); f.Add(p.Border); f.Add(p.Radius);
                if (p.Safe != null)
                {
                    foreach (float v in p.Safe)
                        f.Add(v);
                }

                foreach (GuideElement e in p.Elements)
                {
                    f.Add((int)e.Kind); f.Add(e.X); f.Add(e.Y); f.Add(e.W); f.Add(e.H); f.Add((int)e.Colour);
                    f.Add(e.RadiusTop); f.Add(e.RadiusBottom); f.Add(e.StrokeWidth); f.Add((int)e.Stroke);
                    if (e.Points != null)
                    {
                        foreach (float v in e.Points)
                            f.Add(v);
                    }

                    if (e.Contours != null)
                    {
                        foreach (float[] c in e.Contours)
                        {
                            f.Add(c.Length);
                            foreach (float v in c)
                                f.Add(v);
                        }
                    }
                }
            }

            return f.Value;
        }

        /// <summary>The digest of every spec 警46 has in forms 1 to 4 (the form, the direction, the count, the pass side, the least size).</summary>
        internal static ulong OldFormsDigest()
        {
            GuideTemplate t = SignCatalog.ById("gb.warn.46").Template;
            var f = new Fingerprint();
            foreach (string form in new[] { "1", "2", "3", "4" })
            {
                foreach (string dir in new[] { "l", "r" })
                {
                    foreach (string count in new[] { "2", "3", "4" })
                    {
                        foreach (string pass in new[] { "both", "r", "l" })
                        {
                            foreach (string small in new[] { "0", "1" })
                            {
                                GuideSpec spec = t.Default().With("form", form).With("dir", dir).With("count", count).With("pass", pass).With("small", small);
                                ulong d = Digest(t.Layout(spec));
                                f.Add((int)(d & 0x7fffffff));
                                f.Add((int)(d >> 32));
                            }
                        }
                    }
                }
            }

            return f.Value;
        }

        private static void OldForms()
        {
            Program.Check(OldFormsDigest() == kOldFormsDigest, "the 144 specs of 警46 forms 1 to 4 lay out as they did before: " + OldFormsDigest());
            GuideTemplate t = SignCatalog.ById("gb.warn.46").Template;
            string plain = t.Default().ToString();
            Program.Check(t.Parse(plain).Is("form", "1") && !t.Parse(plain).ToString().Contains("form=5"), "a spec of an item placed before still means form 1");
            Program.Check(t.FieldOf("form").Choices.Length == 5 && t.FieldOf("form").Choices[4].Value == "5", "form 5 is appended after the four");
        }

        /// <summary>Whether (x, y) is inside the contours as the even-odd rule sees them (the path's fill).</summary>
        private static bool Inside(List<float[]> contours, float x, float y)
        {
            bool inside = false;
            foreach (float[] c in contours)
            {
                int n = c.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    float xi = c[i * 2], yi = c[i * 2 + 1], xj = c[j * 2], yj = c[j * 2 + 1];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                        inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>The figure of the board's stripes (or chevrons) as the contours of its paths, in the part's frame, and the colour they are in.</summary>
        private static List<float[]> Ink(GuideBoard board, out GuideColour colour)
        {
            colour = GuideColour.Black;
            var all = new List<float[]>();
            foreach (GuideElement e in board.Main.Elements)
            {
                if (e.Kind != GuideElementKind.Path || e.Contours == null)
                    continue;
                colour = e.Colour;
                all.AddRange(e.Contours);
            }

            return all;
        }

        private static void Stripes()
        {
            GuideTemplate t = SignCatalog.ById("gb.warn.46").Template;
            GuideSpec five = t.Default().With("form", "5");
            GuideBoard left = t.Layout(five.With("pass", "l")), right = t.Layout(five.With("pass", "r")), both = t.Layout(five.With("pass", "both"));
            Program.Check(left.Main.Ground == GuideColour.White && Math.Abs(left.Main.W - 0.6f) < 1e-4f && Math.Abs(left.Main.H - 1.2f) < 1e-4f, "a white board 600 x 1200 as 警46-4's, 1 to 2");
            List<float[]> inkL = Ink(left, out GuideColour colourL), inkR = Ink(right, out GuideColour colourR);
            Program.Check(colourL == GuideColour.Red && colourR == GuideColour.Red && Ink(both, out GuideColour colourB).Count > 0 && colourB == GuideColour.Red, "red stripes, red chevrons for both sides");

            // The photo (to pass on the left, stripes going up to the right): red at the top's right half, white at its left, a red corner at the bottom right, white at the bottom left.
            float w = left.Main.W, h = left.Main.H;
            Program.Check(Inside(inkL, 0.75f * w, 0.01f * h) && !Inside(inkL, 0.25f * w, 0.01f * h), "to pass on the left: the top edge is red on its right half, white on its left");
            Program.Check(Inside(inkL, 0.98f * w, 0.99f * h) && !Inside(inkL, 0.02f * w, 0.99f * h), "and the bottom right corner is red, the bottom left white");
            Program.Check(Inside(inkR, 0.25f * w, 0.01f * h) && !Inside(inkR, 0.75f * w, 0.01f * h), "to pass on the right it is the mirror: red on the top's left half");
            Program.Check(Inside(inkR, 0.02f * w, 0.99f * h) && !Inside(inkR, 0.98f * w, 0.99f * h), "and the bottom left corner is red");

            // 45 degrees: the middle of the board across a stripe is red for a run of 300 mm along the diagonal; the stripes are 300 wide along x, equal to the white ones between.
            int red = 0, total = 0;
            for (int i = 0; i < 40; i++)
            {
                float x = (i + 0.5f) / 40f * w;
                total++;
                red += Inside(inkL, x, 0.5f * h) ? 1 : 0;
            }

            Program.Check(red == 20, "along any level line half the board is red and half white: " + red + " of " + total);
            float above = 0f, below = 0f;
            for (int i = 0; i < 400; i++)
            {
                float x = (i + 0.5f) / 400f * w;
                above += Inside(inkL, x, 0.3f * h) ? 1f : 0f;
                below += Inside(inkL, x, 0.3f * h + 0.3f) ? 1f : 0f;
            }

            Program.Check(Math.Abs(above - 200f) < 3f && Math.Abs(below - 200f) < 3f, "and 300 mm further down too (the period is the width of the board): " + above + ", " + below);
            // Where it can be set.
            SignDef def = SignCatalog.ById("gb.warn.46");
            string on = t.Parse(t.Default().ToString()).With(SignLine.LineKey, "1").With("form", "5").ToString();
            Program.Check(!SignLine.On(def, on), "an upright board is not placed along a line, form 5 as 4");
            Program.Check(!t.FieldOf("pass").Visible(t.Parse(on).With("form", "1")) && t.FieldOf("pass").Visible(t.Parse(on)) && !t.FieldOf("small").Visible(t.Parse(on)), "the side to pass is a setting of form 5, the least size is not");
        }

        // ------------------------------------------------------------------ the numbering

        private static void Numbering()
        {
            PostMark m = RoadPosts.Hundreds(238, 0, 0);
            Program.Check(m.Stone && m.Km == 238 && m.Hundred == 0, "a run that starts at kilometre 238 hundred 0 starts with the stone");
            m = RoadPosts.Hundreds(238, 0, 3);
            Program.Check(!m.Stone && m.Km == 238 && m.Hundred == 3, "three places on: the post 3");
            m = RoadPosts.Hundreds(238, 8, 2);
            Program.Check(m.Stone && m.Km == 239 && m.Hundred == 0, "from hundred 8 the second place after is the stone of the next kilometre");
            m = RoadPosts.Hundreds(9999, 9, 1);
            Program.Check(m.Stone && m.Km == 10000, "no wrap at the top: 9999.9 and a hundred on is 10000");
            m = RoadPosts.Hundreds(5, 12, -4);
            Program.Check(!m.Stone && m.Km == 5 && m.Hundred == 9, "a hundred past 9 and a negative index are held: hundred 9");
            m = RoadPosts.Kilometres(238, 3);
            Program.Check(m.Stone && m.Km == 241, "stones only: a kilometre each");

            Program.Check(RoadPosts.IndexAt(0f, 50f) == 0 && RoadPosts.IndexAt(100f, 50f) == 2 && RoadPosts.IndexAt(149.9f, 50f) == 3 && RoadPosts.IndexAt(7f, 0f) == 0, "the place's index is the distance over the spacing, rounded");
            Program.Check(RoadPosts.HasPlate(0, 50f) && !RoadPosts.HasPlate(1, 50f) && RoadPosts.HasPlate(2, 50f) && RoadPosts.HasPlate(1, 100f) && RoadPosts.HasPlate(1, 250f), "at 50 m a plate on every second post, at 100 m and more on every one");
            Program.Check(RoadPosts.PlateIndex(4, 50f) == 2 && RoadPosts.PlateIndex(5, 100f) == 5, "and the plate's number follows the hundreds");
            Program.Check(RoadPosts.ColourOf(PostRoadClass.National) == SolidColour.Red && RoadPosts.ColourOf(PostRoadClass.Provincial) == SolidColour.Blue && RoadPosts.ColourOf(PostRoadClass.Local) == SolidColour.Black, "red, blue and black for national, provincial and local roads");
            // The numbers the code takes from the standard and its figures, written out: a change of them is a change of what the standard says (or of what was read off a figure).
            Program.Check(RoadPosts.PostLength == 1.250f && RoadPosts.TriangleBase == 0.100f && RoadPosts.TriangleHeight == 0.120f && RoadPosts.BandLength == 0.250f && RoadPosts.BarHeight == 0.180f && RoadPosts.BarWidth == 0.040f,
                "GB/T 24970-2020 as quoted: 1250 long, 100 base, 120 high, a mark 250 long, a reflector 180 x 40");
            Program.Check(RoadPosts.StoneDepth == 0.15f && RoadPosts.StoneChamfer == 0.05f && RoadPosts.CodeHeight == 0.05f && RoadPosts.HundredBuried == 0.30f, "Fig. 243: 15 deep, the top's corners cut 5, the road number 5 high; Fig. 245: the post 30 into the ground");
            Program.Check(RoadPosts.HundredSide == 0.12f && RoadPosts.HundredHeight == 0.25f && RoadPosts.HundredChamfer == 0.02f && RoadPosts.HundredDigit == 0.08f && RoadPosts.HundredDigitTop == 0.04f, "Fig. 245: 12 square, 25 high, the top cut 2, the figure 8 high and 4 under the top");
            Program.Check(RoadPosts.PostSpacing == 50f && RoadPosts.PlateSpacing == 100f && RoadPosts.PlateDiameter == 0.070f && RoadPosts.Slope == 0.55f && RoadPosts.BandCentre == 0.70f && RoadPosts.Buried == 0.25f, "the spacings, and the figure's and the estimates' numbers");
            RoadPosts.StoneSize(3, out float w3, out float h3, out float fb3, out float fh3, out float d3);
            RoadPosts.StoneSize(4, out float w4, out float h4, out float fb4, out float fh4, out float d4);
            Program.Check(d3 == 0.40f && d4 == 0.50f, "Fig. 243: 40 into the ground for three digits, 50 for four (the figure prints 50 (40))");
            Program.Check(w3 == 0.40f && h3 == 0.60f && fb3 == 0.25f && fh3 == 0.20f && w4 == 0.50f && h4 == 0.70f && fb4 == 0.30f && fh4 == 0.25f, "Fig. 243's two sizes: 40 x 60 and 50 x 70 centimetres");
        }

        private static void Catalogue()
        {
            foreach (string id in new[] { MarkingTemplates.DelineatorPostId, MarkingTemplates.MilePostId })
            {
                GuideTemplate t = MarkingTemplates.ById(id);
                Program.Check(t != null && t.Group == MarkingTemplates.BarrierGroup && t.IsMarking && t.Standalone && t.Id.StartsWith("bar.", StringComparison.Ordinal), id + " is a barrier of the panel");
                GuideSpec spec = t.Default();
                Program.Check(System.Text.Encoding.UTF8.GetByteCount(spec.ToString()) < 128, id + ": the settings are short: " + spec);
                Program.Check(spec.Is("where", "l"), id + ": by default on the driver's right as the line runs with the traffic (the traffic on the line's left)");
                Program.Check(t.FieldOf("off") != null && t.FieldOf(RunPath.Key) != null, id + ": a run with a setback");
                MarkingPlan plan = MarkingTemplates.PlanOf(t, spec.ToString());
                Program.Check(plan != null && plan.TriangleCount > 0, id + ": the picture of one has something to show");
            }

            Program.Check(MarkingTemplates.ById(MarkingTemplates.DelineatorPostId).Code == "图287" && MarkingTemplates.ById(MarkingTemplates.MilePostId).Code == "图243", "the figures are the codes");
            // Without a road under it a run shows a few posts close together (the picture, the cursor's first look).
            MarkingPlan stonePicture = MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.MilePostId), MarkingTemplates.ById(MarkingTemplates.MilePostId).Default().ToString());
            Program.Check(Clusters(Of(stonePicture, SolidColour.White), 1f).Count == 3, "the picture shows a stone and two posts: " + Clusters(Of(stonePicture, SolidColour.White), 1f).Count);
        }

        // ------------------------------------------------------------------ the 柱式轮廓标

        private static void PostGeometry()
        {
            // Two posts 50 m apart, no plates, the traffic on the line's left (where l, the default): the posts on the driver's right.
            MarkingPlan plan = PlanOf(MarkingTemplates.DelineatorPostId, "plate=0", Straight(60f));
            List<Vec3> white = Of(plan, SolidColour.White, DetailKind.Painted), first = white.Where(v => v.Z < 25f).ToList();
            Program.Check(first.Count > 0 && Clusters(white, 20f).Count == 2, "a post every 50 m: two on 60 m");
            Program.Near(Min(first.Select(v => v.Y)), -RoadPosts.Buried, 1e-4f, "it goes into the ground by 0.25 m");
            Program.Near(Max(first.Select(v => v.Y)), RoadPosts.PostLength - RoadPosts.Buried + RoadPosts.Slope * RoadPosts.TriangleBase * 0.5f, 2e-3f, "its tallest corner is 1.0 m and the slope of half the base over the standing length");
            float span = Max(first.Select(v => v.X)) - Min(first.Select(v => v.X)), depth = Max(first.Select(v => v.Z)) - Min(first.Select(v => v.Z));
            Program.Near(span, RoadPosts.TriangleBase, 1e-3f, "100 mm across the base (GB/T 24970-2020 5.1.1.3)");
            Program.Near(depth, RoadPosts.TriangleHeight, 1e-3f, "120 mm from the base to the apex");
            // The base faces the oncoming traffic (-Z: the line runs +Z): the apex is behind it.
            Program.Near(Min(first.Select(v => v.Z)), -RoadPosts.TriangleHeight / 3f, 1e-3f, "the base is at -Z, toward the traffic coming along the line");

            // The top slopes down to the carriageway: with the traffic on the line's left (-X) the top is lower at -X.
            float lowTop = Max(first.Where(v => v.X < -0.049f).Select(v => v.Y)), highTop = Max(first.Where(v => v.X > 0.049f).Select(v => v.Y));
            Program.Check(Math.Abs((highTop - lowTop) - RoadPosts.Slope * RoadPosts.TriangleBase) < 2e-3f, $"the top slopes to the carriageway on the left, {RoadPosts.Slope} across: {lowTop} against {highTop}");
            MarkingPlan other = PlanOf(MarkingTemplates.DelineatorPostId, "plate=0;where=r", Straight(60f));
            List<Vec3> otherFirst = Of(other, SolidColour.White, DetailKind.Painted).Where(v => v.Z < 25f).ToList();
            Program.Check(Max(otherFirst.Where(v => v.X < -0.049f).Select(v => v.Y)) > Max(otherFirst.Where(v => v.X > 0.049f).Select(v => v.Y)) + 0.05f, "traffic on the line's right: the top slopes the other way");

            // The black mark: 250 mm long all the way round, its edges parallel to the top.
            List<Vec3> black = Of(plan, SolidColour.Black).Where(v => v.Z < 25f).ToList();
            var pairs = black.GroupBy(v => ((int)Math.Round(v.X * 10000f), (int)Math.Round(v.Z * 10000f))).Where(g => g.Count() >= 2).ToList();
            Program.Check(pairs.Count >= 9, "the sleeve has a lower and an upper edge at each corner of the section: " + pairs.Count);
            Program.Check(pairs.All(g => Math.Abs((g.Max(v => v.Y) - g.Min(v => v.Y)) - RoadPosts.BandLength) < 1e-3f), "250 mm long at every corner (GB/T 24970-2020)");
            float bandMid = (Max(black.Select(v => v.Y)) + Min(black.Select(v => v.Y))) * 0.5f;
            Program.Near(bandMid, RoadPosts.BandCentre, 0.03f, "centred where the reflector of a delineator is, 0.70 m");
            var edgeLeft = pairs.OrderBy(g => g.Key.Item1).First();
            var edgeRight = pairs.OrderBy(g => g.Key.Item1).Last();
            float dTop = edgeRight.Max(v => v.Y) - edgeLeft.Max(v => v.Y), dBottom = edgeRight.Min(v => v.Y) - edgeLeft.Min(v => v.Y);
            Program.Check(Math.Abs(dTop - dBottom) < 1e-3f && Math.Abs(dTop) > 0.04f, $"the mark's edges are parallel to each other and slanted: {dTop}, {dBottom}");
            Program.Check(Math.Sign(dTop) == Math.Sign(highTop - lowTop), "and slant as the top does");

            // The reflector: 180 x 40 mm, retroreflective, on the face, in the middle of the mark; yellow on the other side of the road.
            List<Vec3> bar = Of(plan, SolidColour.White, DetailKind.Reflective).Where(v => v.Z < 25f).ToList();
            Program.Check(bar.Count > 0, "a reflective white bar");
            Program.Near(Max(bar.Select(v => v.X)) - Min(bar.Select(v => v.X)), RoadPosts.BarWidth, 1e-3f, "40 mm wide");
            Program.Near(Max(bar.Select(v => v.Y)) - Min(bar.Select(v => v.Y)), RoadPosts.BarHeight, 1e-3f, "180 mm high");
            Program.Near((Max(bar.Select(v => v.Y)) + Min(bar.Select(v => v.Y))) * 0.5f, RoadPosts.BandCentre, 1e-3f, "at the height of the mark's middle");
            Program.Check(Min(bar.Select(v => v.Z)) < -RoadPosts.TriangleHeight / 3f - 0.002f, "in front of the face, toward the traffic");
            MarkingPlan yellow = PlanOf(MarkingTemplates.DelineatorPostId, "plate=0;colour=y", Straight(60f));
            Program.Check(Of(yellow, SolidColour.Yellow, DetailKind.Reflective).Count > 0 && Of(yellow, SolidColour.White, DetailKind.Reflective).Count == 0, "the yellow reflector for the left side");
            // A line that bends has each post turned to the line there: after a right turn of 90 degrees the line runs along +X and the post at 50 m (20 m along the second chord) faces -X.
            MarkingPlan turned = PlanOf(MarkingTemplates.DelineatorPostId, "plate=0", new List<Vec3> { P(0f, 0f), P(0f, 30f), P(60f, 30f) });
            List<Vec3> turnedWhite = Of(turned, SolidColour.White, DetailKind.Painted).Where(v => v.X > 10f).ToList();
            Program.Near(Min(turnedWhite.Select(v => v.X)), 20f - RoadPosts.TriangleHeight / 3f, 1e-3f, "along +X the base is at -X: the post turns with the line");
            Program.Near(Max(turnedWhite.Select(v => v.Z)) - Min(turnedWhite.Select(v => v.Z)), RoadPosts.TriangleBase, 1e-3f, "and it is 100 across there too");
        }

        private static void Plates()
        {
            if (FontLetterTests.Font == null)
            {
                Program.Check(false, "the bundled font is needed for the plates");
                return;
            }

            MarkingLetters.Font = FontLetterTests.Font;
            // 450 m at 50 m: ten posts, a plate on every second one, from hundred 1.
            MarkingPlan plan = PlanOf(MarkingTemplates.DelineatorPostId, "", Straight(450f));
            List<float> discs = Clusters(Of(plan, SolidColour.Green), 10f), posts = Clusters(Of(plan, SolidColour.Black), 10f);
            Program.Check(posts.Count == 10, "ten posts on 450 m: " + posts.Count);
            Program.Check(discs.Count == 5 && discs.Select((z, i) => Math.Abs(z - 100f * i) < 0.1f).All(b => b), "five plates, at 0, 100 ... 400: " + string.Join(" ", discs.Select(z => z.ToString("0.#"))));
            // At 100 m a plate on each.
            MarkingPlan every = PlanOf(MarkingTemplates.DelineatorPostId, "gap=100", Straight(450f));
            Program.Check(Clusters(Of(every, SolidColour.Green), 10f).Count == 5 && Clusters(Of(every, SolidColour.Black), 10f).Count == 5, "100 m apart: five posts, a plate on each");
            Program.Check(Of(PlanOf(MarkingTemplates.DelineatorPostId, "plate=0", Straight(450f)), SolidColour.Green).Count == 0, "no plates when they are off");

            // The stone's place has no plate: from hundred 8, the third place is hundred 0.
            MarkingPlan wrap = PlanOf(MarkingTemplates.DelineatorPostId, "gap=100;hm=8", Straight(400f));
            List<float> wrapDiscs = Clusters(Of(wrap, SolidColour.Green), 10f);
            Program.Check(wrapDiscs.Count == 4 && wrapDiscs.All(z => Math.Abs(z - 200f) > 1f), "hundreds 8, 9, (0), 1, 2: no plate at 200 m, the post is there: " + string.Join(" ", wrapDiscs.Select(z => z.ToString("0.#"))));
            Program.Check(Clusters(Of(wrap, SolidColour.Black), 10f).Count == 5, "five posts");

            // A run cut in two pieces goes on in its numbers: the pieces together are the whole run (the places, and the figures of every plate).
            MarkingPlan whole = PlanOf(MarkingTemplates.DelineatorPostId, "", Straight(450f), 0f, 450f);
            MarkingPlan a = PlanOf(MarkingTemplates.DelineatorPostId, "", Straight(230f), 0f, 450f), b = PlanOf(MarkingTemplates.DelineatorPostId, "", Straight(220f), 230f, 450f);
            List<float> together = Clusters(Of(a, SolidColour.Green), 10f).Concat(Clusters(Of(b, SolidColour.Green), 10f).Select(z => z + 230f)).ToList(), all = Clusters(Of(whole, SolidColour.Green), 10f);
            Program.Check(together.Count == all.Count && together.Zip(all, (x, y) => Math.Abs(x - y) < 0.1f).All(t => t), "the plates of the pieces are at the places of the whole's: " + together.Count + " and " + all.Count);
            Program.Check(Of(a, SolidColour.Black).Count + Of(b, SolidColour.Black).Count == Of(whole, SolidColour.Black).Count, "and so are the posts");
            Program.Check(MeshOf(a, SolidColour.White).VertexCount + MeshOf(b, SolidColour.White).VertexCount == MeshOf(whole, SolidColour.White).VertexCount, "and the figures on them: the pieces' have the vertices of the whole's");
            // The figure follows the number: the plate at 100 m of a run from hundred 1 is the 2, as the first plate of a run from hundred 2 is.
            MarkingPlan fromOne = PlanOf(MarkingTemplates.DelineatorPostId, "hm=1", Straight(150f)), fromTwo = PlanOf(MarkingTemplates.DelineatorPostId, "hm=2", Straight(150f));
            List<(float x, float y)> two = HundredFigure(fromOne, 100f), twoAgain = HundredFigure(fromTwo, 0f), three = HundredFigure(fromTwo, 100f);
            Program.Check(two.Count >= 8 && two.Count == twoAgain.Count && two.Zip(twoAgain, (p, q) => Math.Abs(p.x - q.x) < 1e-5f && Math.Abs(p.y - q.y) < 1e-5f).All(t => t), "hundred 2 is the same figure wherever it stands: " + two.Count + " vertices");
            Program.Check(three.Count != two.Count || !two.Zip(three, (p, q) => Math.Abs(p.x - q.x) < 1e-5f && Math.Abs(p.y - q.y) < 1e-5f).All(t => t), "and hundred 3 is another");
            // A row that is too wide for its face is narrowed to it; one that fits is left as it is (a player's font with wide figures is held to the post).
            float wide = MarkingLetters.FigureRowWidth("88888888", 0.02f);
            Program.Check(!float.IsNaN(wide) && wide > 0.1f, "eight figures of a 0.02 cell are wider than 0.1 m: " + wide);
            float narrowed = Barriers.FigureCell("88888888", 0.02f, 0.1f);
            Program.Check(narrowed < 0.02f && Math.Abs(MarkingLetters.FigureRowWidth("88888888", narrowed) - 0.1f) < 1e-4f, "narrowed so that the row is 0.1 m: " + narrowed);
            Program.Check(Barriers.FigureCell("8", 0.02f, 0.1f) == 0.02f && Barriers.FigureCell("", 0.02f, 0.1f) == 0.02f, "a row that fits keeps its cell");
            // A kilometre of five digits (after 9999) stays on the disc.
            MarkingPlan five = PlanOf(MarkingTemplates.DelineatorPostId, "gap=100;km=9999;hm=5", Straight(700f));
            float dd = RoadPosts.PlateDiameter, yc5 = RoadPosts.PostLength - RoadPosts.Buried - RoadPosts.Slope * dd * 0.5f - 0.006f - dd * 0.5f;
            List<(float u, float y)> row = PlateFigure(five, yc5 - 0.2f * dd, yc5 - 0.42f * dd, true, 600f);
            Program.Check(row.Count > 20 && row.Max(p => Math.Abs(p.u)) <= 0.22f * dd + 1e-4f, "10001 stays inside the row of 44 % of the disc: " + row.Count + " vertices, to " + (row.Count == 0 ? 0f : row.Max(p => Math.Abs(p.u)) / dd));
            Program.Check(Clusters(Of(five, SolidColour.Green), 10f).Count == 7 && Clusters(Of(five, SolidColour.Green), 10f).All(z => Math.Abs(z - 500f) > 1f), "and the stone's place (hundred 0, 500 m on) has no plate");
            // Without its phase the second piece would start its grid again at its own start: its plates would be elsewhere.
            MarkingPlan restarted = PlanOf(MarkingTemplates.DelineatorPostId, "", Straight(220f), 0f, 450f);
            List<float> restartedDiscs = Clusters(Of(restarted, SolidColour.Green), 10f), told = Clusters(Of(b, SolidColour.Green), 10f);
            Program.Check(told.Count == 2 && Math.Abs(told[0] - 70f) < 0.1f && Math.Abs(told[1] - 170f) < 0.1f, "the piece that starts at 230 m has its plates 70 and 170 m along: " + string.Join(" ", told.Select(z => z.ToString("0.#"))));
            Program.Check(!(restartedDiscs.Count == 2 && Math.Abs(restartedDiscs[0] - 70f) < 0.1f), "a piece that is not told where it starts is not in step: " + string.Join(" ", restartedDiscs.Select(z => z.ToString("0.#"))));
        }

        /// <summary>The vertices of the hundred's figure of the plate on the post at z0 (on the face toward -Z), as (x, y relative to the post), in order.</summary>
        private static List<(float x, float y)> HundredFigure(MarkingPlan plan, float z0)
        {
            float d = RoadPosts.PlateDiameter, r = d * 0.5f, yc = RoadPosts.PostLength - RoadPosts.Buried - RoadPosts.Slope * r - 0.006f - r;
            return PlateFigure(plan, yc + 0.5f * d, yc - 0.13f * d, true, z0).OrderBy(p => p.u).ThenBy(p => p.y).Select(p => (p.u, p.y)).ToList();
        }

        /// <summary>The figure vertices of a plate in the viewer's frame (u to his right, y up), taken from the plane in front of the green disc.</summary>
        private static List<(float u, float y)> PlateFigure(MarkingPlan plan, float yAbove, float yBelow, bool facingMinusZ, float zCentre)
        {
            const float front = RoadPosts.TriangleHeight / 3f + 0.009f;
            var list = new List<(float u, float y)>();
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid != SolidColour.White)
                    continue;
                for (int i = 0; i < part.Mesh.VertexCount; i++)
                {
                    Vec3 v = part.Mesh.Positions[i];
                    float w = facingMinusZ ? zCentre - v.Z : v.Z - zCentre;
                    if (Math.Abs(w - front) < 1e-4f && v.Y > yBelow && v.Y < yAbove)
                        list.Add((facingMinusZ ? v.X : -v.X, v.Y));
                }
            }

            return list;
        }

        private static void PlateFigures()
        {
            if (FontLetterTests.Font == null)
            {
                Program.Check(false, "the bundled font is needed for the plates");
                return;
            }

            MarkingLetters.Font = FontLetterTests.Font;
            // One post with the plate 7 over the kilometre 238: the disc centre is at the height the builder gives it.
            MarkingPlan plan = PlanOf(MarkingTemplates.DelineatorPostId, "gap=100;hm=7;km=238", Straight(10f));
            float d = RoadPosts.PlateDiameter, r = d * 0.5f, yc = RoadPosts.PostLength - RoadPosts.Buried - RoadPosts.Slope * r - 0.006f - r;
            List<(float u, float y)> seven = PlateFigure(plan, yc + 0.5f * d, yc - 0.13f * d, true, 0f);
            Program.Check(seven.Count >= 6, "the hundred's figure is on the plane in front of the disc: " + seven.Count);
            Program.Near(seven.Max(p => p.y) - seven.Min(p => p.y), 0.5f * d, 3e-3f, "half the disc high (路64's 50 of 100)");
            Program.Check(seven.All(p => Math.Abs(p.u) < 0.25f * d), "inside the disc, in the middle of it across");
            float top = seven.Max(p => p.y), bottom = seven.Min(p => p.y), span = top - bottom;
            float meanTop = seven.Where(p => p.y > top - 0.25f * span).Average(p => p.u), meanBottom = seven.Where(p => p.y < bottom + 0.25f * span).Average(p => p.u);
            Program.Check(meanBottom < meanTop - 0.1f * d * 0.4f, $"a 7 is read as a 7 by the one who looks at the post: its foot is to the left of its bar ({meanBottom} against {meanTop}); mirrored it would not be");

            // Every triangle of the figures faces the traffic (-Z).
            int facing = 0, total = 0;
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid != SolidColour.White)
                    continue;
                MeshBuilder m = part.Mesh;
                for (int i = 0; i + 2 < m.Indices.Count; i += 3)
                {
                    Vec3 a = m.Positions[m.Indices[i]], b = m.Positions[m.Indices[i + 1]], c = m.Positions[m.Indices[i + 2]];
                    if (Math.Abs(a.Z - b.Z) > 1e-5f || Math.Abs(b.Z - c.Z) > 1e-5f || a.Y < yc - 0.2f * d || Math.Abs(-a.Z - (RoadPosts.TriangleHeight / 3f + 0.009f)) > 1e-4f)
                        continue;
                    total++;
                    facing += MeshBuilder.Cross(b - a, c - a).Z < 0f ? 1 : 0;
                }
            }

            Program.Check(total > 6 && facing == total, $"the figures' triangles all face the oncoming traffic: {facing} of {total}");
            // After a right turn the figure is on the -X face and reads right from there: the viewer's right is -Z.
            MarkingPlan bent = PlanOf(MarkingTemplates.DelineatorPostId, "gap=100;hm=6;km=238", new List<Vec3> { P(0f, 0f), P(0f, 60f), P(100f, 60f) });
            const float front = RoadPosts.TriangleHeight / 3f + 0.009f;
            var sevenBent = new List<(float u, float y)>();
            foreach (MarkingPart part in bent.Parts)
            {
                if (part.Solid != SolidColour.White)
                    continue;
                foreach (Vec3 v in part.Mesh.Positions)
                {
                    if (v.X > 10f && Math.Abs((40f - v.X) - front) < 1e-4f && v.Y > yc - 0.13f * d && v.Y < yc + 0.5f * d)
                        sevenBent.Add((-v.Z + 60f, v.Y));
                }
            }

            Program.Check(sevenBent.Count == seven.Count, "turned with the line: the same figure, " + sevenBent.Count + " and " + seven.Count);
            top = sevenBent.Max(p => p.y);
            bottom = sevenBent.Min(p => p.y);
            span = top - bottom;
            Program.Check(sevenBent.Where(p => p.y < bottom + 0.25f * span).Average(p => p.u) < sevenBent.Where(p => p.y > top - 0.25f * span).Average(p => p.u) - 0.1f * d * 0.4f, "and not mirrored there either");
        }

        // ------------------------------------------------------------------ the stones

        private static void StonePieces()
        {
            if (FontLetterTests.Font == null)
            {
                Program.Check(false, "the bundled font is needed for the stones");
                return;
            }

            MarkingLetters.Font = FontLetterTests.Font;
            // Stones and posts: a run of 1100 m in two pieces (450 m and 650 m) goes on in its numbers: the pieces have the vertices of the whole, which a restart would change (a stone where the post 5 stands).
            MarkingPlan whole = PlanOf(MarkingTemplates.MilePostId, "", Straight(1100f), 0f, 1100f);
            MarkingPlan a = PlanOf(MarkingTemplates.MilePostId, "", Straight(450f), 0f, 1100f), b = PlanOf(MarkingTemplates.MilePostId, "", Straight(650f), 450f, 1100f), restarted = PlanOf(MarkingTemplates.MilePostId, "", Straight(650f), 0f, 1100f);
            int wholeCount = Of(whole, SolidColour.White).Count + Of(whole, SolidColour.Red).Count;
            Program.Check(Of(a, SolidColour.White).Count + Of(a, SolidColour.Red).Count + Of(b, SolidColour.White).Count + Of(b, SolidColour.Red).Count == wholeCount, "the pieces have the vertices of the whole");
            Program.Check(Of(restarted, SolidColour.White).Count + Of(restarted, SolidColour.Red).Count != Of(b, SolidColour.White).Count + Of(b, SolidColour.Red).Count, "and a piece that restarted would not");
            // The first place of the second piece is 50 m on from the 450 of the first (the grid is 100 m): the place at 500 m, index 5, the post 5.
            Program.Check(Clusters(Of(b, SolidColour.White), 20f).Count == 7 && Math.Abs(Clusters(Of(b, SolidColour.White), 20f)[0] - 50f) < 0.2f, "the piece's first place is at 50 m: the grid goes on across the pieces");
        }

        private static void Stones()
        {
            if (FontLetterTests.Font == null)
            {
                Program.Check(false, "the bundled font is needed for the stones");
                return;
            }

            MarkingLetters.Font = FontLetterTests.Font;
            // One stone of three digits: 40 x 15 x 60 above the ground, 40 below (Fig. 243).
            MarkingPlan three = PlanOf(MarkingTemplates.MilePostId, "kind=km;km=238", Straight(10f));
            List<Vec3> white = Of(three, SolidColour.White);
            Program.Near(Max(white.Select(v => v.X)) - Min(white.Select(v => v.X)), 0.40f, 1e-4f, "40 cm wide for a kilometre of three digits");
            Program.Near(Max(white.Select(v => v.Z)) - Min(white.Select(v => v.Z)), RoadPosts.StoneDepth, 1e-4f, "15 deep");
            Program.Near(Max(white.Select(v => v.Y)), 0.60f, 1e-4f, "60 high");
            Program.Near(Min(white.Select(v => v.Y)), -0.40f, 1e-4f, "and 40 into the ground");
            MarkingPlan fourDigits = PlanOf(MarkingTemplates.MilePostId, "kind=km;km=1528", Straight(10f));
            Program.Near(Min(Of(fourDigits, SolidColour.White).Select(v => v.Y)), -0.50f, 1e-4f, "50 for a kilometre of four digits");
            // The corners at the top are cut 5 by 5.
            Program.Check(!white.Any(v => v.Y > 0.5999f && Math.Abs(v.X) > 0.15f + 1e-4f) && white.Any(v => Math.Abs(v.Y - 0.55f) < 1e-4f && Math.Abs(Math.Abs(v.X) - 0.20f) < 1e-4f), "the top is 30 wide, its corners cut 5 by 5");
            MarkingPlan four = PlanOf(MarkingTemplates.MilePostId, "kind=km;km=1528;class=s", Straight(10f));
            List<Vec3> whiteFour = Of(four, SolidColour.White);
            Program.Near(Max(whiteFour.Select(v => v.X)) - Min(whiteFour.Select(v => v.X)), 0.50f, 1e-4f, "50 cm wide for four digits");
            Program.Near(Max(whiteFour.Select(v => v.Y)), 0.70f, 1e-4f, "70 high");
            // The frame and the figures are in front of the faces; the kilometre's are between the frame's bars.
            List<Vec3> ink = Of(three, SolidColour.Red);
            Program.Check(ink.Count > 100, "the figures and the frame: " + ink.Count);
            Program.Check(ink.All(v => v.Y > 0.25f - 1e-4f && v.Y < 0.60f - RoadPosts.StoneChamfer + 3e-3f), "above the frame's bottom, under the top's cut: " + Min(ink.Select(v => v.Y)) + " to " + Max(ink.Select(v => v.Y)));
            float frameTop = Max(ink.Where(v => v.Y < 0.47f).Select(v => v.Y));
            Program.Near(frameTop, 0.45f, 1e-3f, "the frame is 20 high from 25 up (the figures in brackets of Fig. 243)");
            Program.Check(ink.Any(v => v.Y > 0.5f && v.Y < 0.56f), "the road's number is a line above the frame");
            MarkingPlan noCode = PlanOf(MarkingTemplates.MilePostId, "kind=km;km=238;code=", Straight(10f));
            Program.Check(Of(noCode, SolidColour.Red).Count < ink.Count && !Of(noCode, SolidColour.Red).Any(v => v.Y > 0.5f), "no road number: no line above the frame");

            // 百米桩: 12 square, 25 high, the top corners cut 2.
            MarkingPlan post = PlanOf(MarkingTemplates.MilePostId, "kind=hm;hm=7", Straight(10f));
            List<Vec3> postWhite = Of(post, SolidColour.White);
            Program.Near(Max(postWhite.Select(v => v.X)) - Min(postWhite.Select(v => v.X)), 0.12f, 1e-4f, "the post is 12 cm across");
            Program.Near(Max(postWhite.Select(v => v.Z)) - Min(postWhite.Select(v => v.Z)), 0.12f, 1e-4f, "12 deep");
            Program.Near(Max(postWhite.Select(v => v.Y)), 0.25f, 1e-4f, "25 high");
            List<Vec3> digit = Of(post, SolidColour.Red);
            Program.Near(Max(digit.Select(v => v.Y)), 0.25f - 0.04f, 3e-3f, "the figure's top is 4 under the top");
            Program.Near(Max(digit.Select(v => v.Y)) - Min(digit.Select(v => v.Y)), 0.08f, 3e-3f, "and it is 8 high");

            // All: a stone where the hundred is 0, the post elsewhere; 1100 m is places 0 to 11.
            MarkingPlan all = PlanOf(MarkingTemplates.MilePostId, "", Straight(1100f));
            List<Vec3> allWhite = Of(all, SolidColour.White);
            List<float> places = Clusters(allWhite, 20f);
            Program.Check(places.Count == 12, "a place every 100 m: twelve on 1100 m: " + places.Count);
            int stones = places.Count(z => Max(allWhite.Where(v => Math.Abs(v.Z - z) < 1f).Select(v => v.Y)) > 0.5f), posts = places.Count(z => Max(allWhite.Where(v => Math.Abs(v.Z - z) < 1f).Select(v => v.Y)) < 0.3f);
            Program.Check(stones == 2 && posts == 10, $"stones at 0 and 1000, ten posts: {stones}, {posts}");
            Program.Check(Max(allWhite.Where(v => Math.Abs(v.Z) < 1f).Select(v => v.Y)) > 0.5f && Max(allWhite.Where(v => Math.Abs(v.Z - 1000f) < 1f).Select(v => v.Y)) > 0.5f, "the stones are where the hundred is 0");
            // Posts only, from hundred 0, start at 1; the stone's place is left empty. Stones only: a kilometre apart.
            MarkingPlan hundreds = PlanOf(MarkingTemplates.MilePostId, "kind=hm", Straight(1100f));
            List<float> hundredPlaces = Clusters(Of(hundreds, SolidColour.White), 20f);
            Program.Check(hundredPlaces.Count == 11 && hundredPlaces.All(z => Math.Abs(z - 900f) > 1f && Math.Abs(z) < 1100.5f), "posts only: from hundred 1, none where the stone would be (900 m on): " + hundredPlaces.Count);
            MarkingPlan stonesOnly = PlanOf(MarkingTemplates.MilePostId, "kind=km", Straight(2100f));
            Program.Check(Clusters(Of(stonesOnly, SolidColour.White), 20f).Count == 3, "stones only: one a kilometre, three on 2100 m");
            MarkingPlan typed = PlanOf(MarkingTemplates.MilePostId, "kind=km;gap=500", Straight(2100f));
            Program.Check(Clusters(Of(typed, SolidColour.White), 20f).Count == 5, "a typed spacing: 500 m, five on 2100 m");
            MarkingPlan tooSmall = PlanOf(MarkingTemplates.MilePostId, "kind=km;gap=1", Straight(2100f));
            Program.Check(Clusters(Of(tooSmall, SolidColour.White), 20f).Count == 3, "a spacing under 2 m is not taken: the standard's 1000 m, three stones on 2100 m (at 1 m they would be one row): " + Clusters(Of(tooSmall, SolidColour.White), 20f).Count);
            MarkingPlan twoMetres = PlanOf(MarkingTemplates.MilePostId, "kind=km;gap=2", Straight(2100f));
            Program.Check(Clusters(Of(twoMetres, SolidColour.White), 20f).Count == 1, "and 2 m is the least that is");
            // The numbering of the stones: a kilometre each, so the third from 998 is 1000, which has four digits and the larger stone of Fig. 243.
            MarkingPlan run = PlanOf(MarkingTemplates.MilePostId, "kind=km;km=998", Straight(2100f));
            List<Vec3> runWhite = Of(run, SolidColour.White);
            Program.Near(Max(runWhite.Where(v => Math.Abs(v.Z) < 1f).Select(v => v.Y)), 0.60f, 1e-4f, "998 is on the 60 cm stone");
            Program.Near(Max(runWhite.Where(v => Math.Abs(v.Z - 1000f) < 1f).Select(v => v.Y)), 0.60f, 1e-4f, "999 too");
            Program.Near(Max(runWhite.Where(v => Math.Abs(v.Z - 2000f) < 1f).Select(v => v.Y)), 0.70f, 1e-4f, "and 1000, the third stone, on the 70 cm one");
        }

        private static void StoneFaces()
        {
            if (FontLetterTests.Font == null)
            {
                Program.Check(false, "the bundled font is needed for the stones");
                return;
            }

            MarkingLetters.Font = FontLetterTests.Font;
            MarkingPlan post = PlanOf(MarkingTemplates.MilePostId, "kind=hm;hm=7;class=x", Straight(10f));
            List<Vec3> ink = Of(post, SolidColour.Black);
            Program.Check(Of(post, SolidColour.Red).Count == 0 && ink.Count > 6, "a county road's figures are black");
            Program.Check(Of(PlanOf(MarkingTemplates.MilePostId, "kind=hm;hm=7;class=s", Straight(10f)), SolidColour.Blue).Count > 6, "a provincial road's blue");
            float half = RoadPosts.HundredSide * 0.5f;
            // Both faces carry the figure: on the face toward -Z (the viewer's right is +X) and on the face toward +Z (his right is -X); each reads as a 7.
            foreach (bool minus in new[] { true, false })
            {
                var face = new List<(float u, float y)>();
                foreach (Vec3 v in ink)
                {
                    if (minus && v.Z < -half + 1e-3f)
                        face.Add((v.X, v.Y));
                    else if (!minus && v.Z > half - 1e-3f)
                        face.Add((-v.X, v.Y));
                }

                Program.Check(face.Count >= 6, (minus ? "the -Z" : "the +Z") + " face has the figure: " + face.Count);
                float top = face.Max(p => p.y), bottom = face.Min(p => p.y), span = top - bottom;
                float meanTop = face.Where(p => p.y > top - 0.25f * span).Average(p => p.u), meanBottom = face.Where(p => p.y < bottom + 0.25f * span).Average(p => p.u);
                Program.Check(meanBottom < meanTop - 0.002f, (minus ? "-Z" : "+Z") + ": it reads as a 7 to the one who looks at that face: " + meanBottom + " against " + meanTop);
            }
        }
    }
}
