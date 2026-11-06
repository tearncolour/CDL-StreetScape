using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The lane choice board of the cities (guide.lanepick, from the owner's photo of 请选择车道行驶) and the dashes of 路4. The figures are the lane direction board's (示17 to 示23, E.66) and are
    /// checked against its paths; the dashes of the form "as 示17" are E.66's own numbers; what 路4 kept and what the other boards of the catalogue draw is held to the digests of main 35cb654.
    /// </summary>
    internal static class LanePickTests
    {
        public static void RunAll()
        {
            Program.Run("lane pick: the entry, its settings and the nine figures", Entry);
            Program.Run("lane pick: each lane's figure is the lane direction board's, inside its lane", Figures);
            Program.Run("lane pick: dashed lines, lanes and the size of the field (the standard's E.66 in the form of 示17)", Field);
            Program.Run("lane pick: the strip of words", Strip);
            Program.Run("lane pick: the other ground, the line of the standard form and long words", Variants);
            Program.Run("路4: the dashes are even, the rest is as it was", Dashes);
            Program.Run("lane pick: no other board is changed, and none but the strip bleeds below", Neighbours);
        }

        private static GuideTemplate Template => GuideTemplates.ById(LanePickSigns.Id);

        private static GuideBoard Board(string extra, out GuideSpec spec)
        {
            GuideTemplate t = Template;
            spec = t.Parse("speed=60;" + extra);
            return t.Layout(spec);
        }

        private static float H(GuideSpec spec) => RoadGuideSigns.CharHeight(SignStyles.BandOf(spec));

        private static List<GuideElement> Of(GuidePart part, Func<GuideElement, bool> keep) => part.Elements.Where(keep).ToList();

        private static bool IsDash(GuideElement e) => e.Kind == GuideElementKind.Rect && e.Art;

        private static bool IsFigure(GuideElement e) => e.Kind == GuideElementKind.Path && e.Art;

        private static void Entry()
        {
            SignChoice choice = SignCatalog.ChoiceById(LanePickSigns.Id);
            Program.Check(choice != null && choice.Group == "guide" && choice.Code == LanePickSigns.Code && choice.Def.Template != null && choice.Def.Template.Standalone, "the board is in the guide group, marked 非国标");
            GuideTemplate t = Template;
            GuideField n = t.FieldOf("n");
            Program.Check(n != null && n.Kind == GuideFieldKind.Choice && n.Choices.Length == 6 && n.Default == "3", "one to six lanes, three by default");
            string[] combos = LanePickSigns.Combos.ToArray();
            Program.Check(combos.Length == 9 && combos.Distinct().Count() == 9, "nine arrows: " + string.Join(",", combos));
            string[] defaults = { "al", "a", "a", "a", "a", "ar" };
            for (int i = 1; i <= LanePickSigns.MaxLanes; i++)
            {
                GuideField lane = t.FieldOf("lane" + i);
                Program.Check(lane != null && lane.Choices.Select(c => c.Value).SequenceEqual(combos) && lane.Default == defaults[i - 1], "lane " + i + " offers the nine arrows");
                Program.Check(lane.Visible != null && lane.Visible(t.Default().With("n", i.ToString())) && (i == 1 || !lane.Visible(t.Default().With("n", (i - 1).ToString()))), "lane " + i + " is shown from " + i + " lanes");
            }

            // Every arrow is a figure of the lane direction board and none of them is empty or the same as another.
            var figures = new HashSet<string>();
            foreach (string c in combos)
            {
                string path = Glyphs.MandFigure("lane:" + c);
                Program.Check(!string.IsNullOrEmpty(path) && figures.Add(path), "the figure of " + c + " is the lane board's own and differs from the others");
            }

            GuideField words = t.FieldOf("words"), cap = t.FieldOf("cap");
            Program.Check(words != null && words.Kind == GuideFieldKind.Text && words.Default == "请选择车道行驶" && words.MaxLength == LanePickSigns.MaxWords, "the words are a text field with the photo's text");
            Program.Check(cap != null && cap.Kind == GuideFieldKind.Flag && cap.Default == "1" && words.Visible(t.Default()) && !words.Visible(t.Default().With("cap", "0")), "the strip can be switched off, and then its text is not asked for");
            Program.Check(t.FieldOf("strip").Default == "w" && t.FieldOf("ground").Default == "blue" && t.FieldOf("form").Default == "p", "white strip, blue ground, the photo's proportions by default");
        }

        private static void Figures()
        {
            foreach (string form in new[] { "p", "s" })
            {
                foreach (string combo in LanePickSigns.Combos)
                {
                    GuideBoard board = Board("n=1;lane1=" + combo + ";cap=0;form=" + form, out GuideSpec spec);
                    GuidePart part = board.Main;
                    List<GuideElement> figure = Of(part, IsFigure);
                    Program.Check(figure.Count == 1, form + " " + combo + ": one figure: " + figure.Count);
                    float u = LanePickSigns.Unit(H(spec)), tall = form == "s" ? 1f : LanePickSigns.PhotoTall;
                    GuidePath.Bounds(GuideKit.Contours(Glyphs.MandFigure("lane:" + combo)), out float bx0, out float by0, out float bx1, out float by1);
                    // The 100 x 100 frame of the figure is a box 90 cm wide and 90 cm high (tall: higher) put at the lane: its bounds are the path's, scaled.
                    float sx = 90f * u / 100f, sy = 90f * u * tall / 100f, x0 = part.Edge, y0 = part.Edge;
                    GuideElement e = figure[0];
                    Program.Near(e.X, x0 + bx0 * sx, 2e-4f, form + " " + combo + ": left edge as the lane board's");
                    Program.Near(e.Right, x0 + bx1 * sx, 2e-4f, form + " " + combo + ": right edge as the lane board's");
                    Program.Near(e.Y, y0 + by0 * sy, 2e-4f, form + " " + combo + ": top as the lane board's");
                    Program.Near(e.Bottom, y0 + by1 * sy, 2e-4f, form + " " + combo + ": foot as the lane board's");
                    // Between the lane's two dashed lines (their inner edges).
                    Program.Check(e.X >= x0 + 10f * u - 1e-4f && e.Right <= x0 + 80f * u + 1e-4f, form + " " + combo + ": inside its lane");
                }
            }

            // Each lane has the figure it was told, from the left, and the first lane's figure is where the first lane is.
            GuideBoard six = Board("n=6;lane1=u;lane2=l;lane3=al;lane4=a;lane5=ar;lane6=r;cap=0;form=s", out GuideSpec s6);
            List<GuideElement> row = Of(six.Main, IsFigure);
            Program.Check(row.Count == 6, "six lanes, six figures");
            string[] told = { "u", "l", "al", "a", "ar", "r" };
            float u6 = LanePickSigns.Unit(H(s6));
            for (int i = 0; i < 6; i++)
            {
                GuidePath.Bounds(GuideKit.Contours(Glyphs.MandFigure("lane:" + told[i])), out float bx0, out _, out float bx1, out _);
                Program.Near(row[i].X, six.Main.Edge + 75f * i * u6 + bx0 * 0.9f * u6, 2e-4f, "lane " + (i + 1) + ": " + told[i] + " at the left edge");
                Program.Near(row[i].Right, six.Main.Edge + 75f * i * u6 + bx1 * 0.9f * u6, 2e-4f, "lane " + (i + 1) + ": " + told[i] + " at the right edge");
            }
        }

        private static void Field()
        {
            // The form of 示17: dashes 5 cm wide and 12 cm long at 5, 29 and 53 cm from the top, at 5 + 75 i cm from the left (E.66), on a field 75 n + 15 by 70 cm.
            for (int n = 1; n <= LanePickSigns.MaxLanes; n++)
            {
                GuideBoard board = Board("n=" + n + ";cap=0;form=s", out GuideSpec spec);
                GuidePart part = board.Main;
                float h = H(spec), u = LanePickSigns.Unit(h), e0 = part.Edge;
                Program.Near(part.W, (75f * n + 15f) * u + 2f * e0, 2e-4f, n + " lanes: the field is 75 n + 15 cm wide");
                Program.Near(part.H, 70f * u + 2f * e0, 2e-4f, n + " lanes: 70 cm high");
                List<GuideElement> dashes = Of(part, IsDash);
                Program.Check(dashes.Count == 3 * (n + 1), n + " lanes: three dashes on each of the " + (n + 1) + " lines: " + dashes.Count);
                for (int line = 0; line <= n; line++)
                {
                    List<GuideElement> mine = dashes.Where(d => Math.Abs(d.X - (e0 + (5f + 75f * line) * u)) < 2e-4f).OrderBy(d => d.Y).ToList();
                    Program.Check(mine.Count == 3, n + " lanes: line " + line + " has three dashes: " + mine.Count);
                    for (int k = 0; k < mine.Count && k < 3; k++)
                    {
                        Program.Near(mine[k].Y, e0 + new[] { 5f, 29f, 53f }[k] * u, 2e-4f, n + " lanes, line " + line + ": dash " + k + " at 5, 29, 53 cm");
                        Program.Near(mine[k].W, 5f * u, 2e-4f, "5 cm wide");
                        Program.Near(mine[k].H, 12f * u, 2e-4f, "12 cm long");
                    }
                }
            }

            // The photo's proportions: the field is a quarter higher, the dashes on the same rule (equal dashes and gaps, 5 cm from the top and the foot), the lane as wide.
            GuideBoard p = Board("n=3;cap=0;form=p", out GuideSpec sp);
            float hp = H(sp), up = LanePickSigns.Unit(hp), fieldH = 70f * up * LanePickSigns.PhotoTall, ep = p.Main.Edge;
            Program.Near(p.Main.H, fieldH + 2f * ep, 2e-4f, "the photo's form is higher");
            Program.Near(p.Main.W, (75f * 3f + 15f) * up + 2f * ep, 2e-4f, "and as wide");
            List<GuideElement> line0 = Of(p.Main, IsDash).Where(d => Math.Abs(d.X - (ep + 5f * up)) < 2e-4f).OrderBy(d => d.Y).ToList();
            Program.Check(line0.Count >= 3, "a line of dashes: " + line0.Count);
            Program.Near(line0[0].Y, ep + 5f * up, 2e-4f, "the first dash 5 cm from the top");
            Program.Near(line0[line0.Count - 1].Bottom, ep + fieldH - 5f * up, 2e-4f, "the last 5 cm from the foot");
            for (int k = 1; k < line0.Count; k++)
            {
                Program.Near(line0[k].H, line0[0].H, 2e-4f, "dashes of one length");
                Program.Near(line0[k].Y - line0[k - 1].Bottom, line0[0].H, 2e-4f, "and gaps of the same");
            }
        }

        private static void Strip()
        {
            GuideBoard board = Board("n=3", out GuideSpec spec);
            GuidePart part = board.Main;
            float h = H(spec), u = LanePickSigns.Unit(h), e0 = part.Edge, fieldH = 70f * u * LanePickSigns.PhotoTall;
            Program.Near(part.W, (75f * 3f + 15f) * u + 2f * e0, 2e-4f, "seven characters fit under three lanes: the board is the field's width");
            Program.Near(part.H, fieldH + LanePickSigns.StripK * h + 2f * e0, 2e-4f, "and the strip is " + LanePickSigns.StripK + " h high");

            // The white strip: from the field's foot to the inside's foot, bled over the border on all four sides; the only one that does.
            List<GuideElement> bands = Of(part, e => e.Kind == GuideElementKind.Rect && e.Bleed > 0f);
            Program.Check(bands.Count == 1, "one band bleeds");
            GuideElement band = bands[0];
            Program.Check(band.Colour == GuideColour.White && band.BleedBottom && Math.Abs(band.Bleed - part.Border) < 1e-6f, "white, bled over the border on the sides, the top and below");
            Program.Near(band.Y - band.Bleed, e0 + fieldH, 2e-4f, "its top is the field's foot");
            Program.Near(band.Bottom, part.H - e0, 2e-4f, "and it ends at the foot of the inside (the contract of every element: inside the border)");
            Program.Near(band.X, e0, 2e-4f, "from edge");
            Program.Near(band.Right, part.W - e0, 2e-4f, "to edge");
            Program.Near(band.RadiusBottom, part.InnerRadius, 1e-6f, "its lower corners are the inside's");

            // The words: seven square cells of the character height, in the ground's colour, centred in the strip, spread (the photo: a gap of about 0.7 of a character).
            List<GuideElement> text = Of(part, e => e.Kind == GuideElementKind.Text);
            Program.Check(text.Count == 1 && text[0].Text == "请选择车道行驶" && text[0].Mode == GuideTextMode.Cell, "the words as one run of cells");
            GuideElement words = text[0];
            Program.Check(words.Colour == GuideColour.Blue, "blue on the white strip (the ground's colour)");
            Program.Near(words.H, h, 1e-5f, "a character is h high");
            Program.Near(words.W, 7f * h + 6f * words.Gap, 2e-4f, "cells as wide as high");
            Program.Check(words.Gap >= LanePickSigns.GapMinK * h - 1e-5f && words.Gap <= LanePickSigns.GapMaxK * h + 1e-5f, "the gap is " + words.Gap / h + " of a character");
            Program.Near(words.X + words.W * 0.5f, part.W * 0.5f, 2e-4f, "centred across");
            Program.Near(words.Y + words.H * 0.5f, e0 + fieldH + LanePickSigns.StripK * h * 0.5f, 2e-4f, "and in the strip's height");

            // A short text keeps the character height, with the widest gap; no strip without words or with the strip off.
            GuideBoard shortWords = Board("n=3;words=请慢行", out _);
            GuideElement few = Of(shortWords.Main, e => e.Kind == GuideElementKind.Text)[0];
            Program.Near(few.H, h, 1e-5f, "three characters: as high as seven");
            Program.Near(few.Gap, LanePickSigns.GapMaxK * h, 1e-5f, "with the widest gap");
            foreach (string off in new[] { "cap=0", "words=" })
            {
                GuideBoard none = Board("n=3;" + off, out _);
                Program.Check(!none.Main.Elements.Any(e => e.Kind == GuideElementKind.Text || e.Bleed > 0f), off + ": no strip, no text");
                Program.Near(none.Main.H, fieldH + 2f * e0, 2e-4f, off + ": the board is the field");
            }

            // A line of letters is ink text in the same colour inside the strip.
            GuideBoard latin = Board("n=3;words=Choose a lane", out _);
            List<GuideElement> ink = Of(latin.Main, e => e.Kind == GuideElementKind.Text);
            Program.Check(ink.Count > 0 && ink.All(e => e.Colour == GuideColour.Blue && e.Y >= e0 + fieldH - 1e-4f && e.Bottom <= part.H - e0 + 1e-4f), "letters in the strip, blue");
        }

        private static void Variants()
        {
            GuideBoard green = Board("n=3;ground=green", out _);
            Program.Check(green.Main.Ground == GuideColour.Green && Of(green.Main, e => e.Kind == GuideElementKind.Text).All(e => e.Colour == GuideColour.Green), "the green board: green ground, green words on the white strip");

            // The words on the ground: white ink under a white line, no band.
            GuideBoard onGround = Board("n=3;strip=g", out GuideSpec spec);
            float h = H(spec), u = LanePickSigns.Unit(h), e0 = onGround.Main.Edge, fieldH = 70f * u * LanePickSigns.PhotoTall;
            Program.Check(!onGround.Main.Elements.Any(e => e.Bleed > 0f), "no white band");
            List<GuideElement> rule = Of(onGround.Main, e => e.Kind == GuideElementKind.Rect && !e.Art);
            Program.Check(rule.Count == 1 && rule[0].Colour == GuideColour.White && Math.Abs(rule[0].H - 0.1f * h) < 1e-5f && Math.Abs(rule[0].Y - (e0 + fieldH)) < 2e-4f, "a white line of 0.1 h under the field");
            GuideElement words = Of(onGround.Main, e => e.Kind == GuideElementKind.Text)[0];
            Program.Check(words.Colour == GuideColour.White && words.Y >= e0 + fieldH + 0.1f * h, "white words under the line");
            Program.Near(onGround.Main.H, fieldH + 0.1f * h + LanePickSigns.StripK * h + 2f * e0, 2e-4f, "the board's height with the line and the strip");

            // Words longer than the lanes: the characters shrink to 0.7 h, then the board is widened round the field, which stays centred.
            GuideBoard narrow = Board("n=2;words=请选择车道行驶请选择车道", out _);
            GuideElement long1 = Of(narrow.Main, e => e.Kind == GuideElementKind.Text)[0];
            Program.Check(long1.H < h - 1e-4f && long1.H >= 0.7f * h - 1e-5f, "twelve characters under two lanes are smaller: " + long1.H / h + " h");
            Program.Check(narrow.Main.W >= long1.W + 0.8f * h + 2f * narrow.Main.Edge - 1e-4f, "the board is wide enough for them");
            float fieldW = (75f * 2f + 15f) * u, left = narrow.Main.Edge + (narrow.Main.W - 2f * narrow.Main.Edge - fieldW) * 0.5f;
            List<GuideElement> dashes = Of(narrow.Main, IsDash);
            Program.Near(dashes.Min(d => d.X), left + 5f * u, 2e-4f, "the field is centred in the wider board");
        }

        // ------------------------------------------------------------------ 路4

        /// <summary>The loop of main 35cb654: dashes of 0.6 h every 1.2 h from y0, the last cut short at y1.</summary>
        private static List<float> OldDashLengths(float y0, float y1, float h)
        {
            var lengths = new List<float>();
            for (float y = y0; y < y1 - 0.05f * h; y += 1.2f * h)
                lengths.Add(Math.Min(0.6f * h, y1 - y));
            return lengths;
        }

        private static void Dashes()
        {
            var specs = new List<(string id, string spec)>();
            foreach (string id in new[] { "gb.guide8.04", "gb.guide8.04b" })
            {
                foreach (string speed in new[] { "100", "80", "60", "30" })
                {
                    specs.Add((id, "speed=" + speed));
                    for (int n = 2; n <= 6; n++)
                    {
                        specs.Add((id, "speed=" + speed + ";n=" + n + ";a1=l;a2=s;a3=s;a4=r;a5=sl;a6=sr"));
                        specs.Add((id, "speed=" + speed + ";n=" + n + ";to1=甲路;to2=乙路;to3=丙;a1=l;a2=s;a3=sr"));
                    }
                }
            }

            int lines = 0;
            foreach ((string id, string text) in specs)
            {
                GuideTemplate t = GuideTemplates.ById(id);
                GuideSpec spec = t.Parse(text);
                GuidePart part = t.Layout(spec).Main;
                float h = H(spec), e0 = part.Edge;
                foreach (IGrouping<float, GuideElement> line in part.Elements.Where(e => e.Kind == GuideElementKind.Rect).GroupBy(e => (float)Math.Round(e.X, 4)))
                {
                    List<GuideElement> dashes = line.OrderBy(e => e.Y).ToList();
                    string what = id + " [" + text + "] line at " + line.Key;
                    lines++;
                    Program.Near(dashes[0].Y, e0 + 0.5f * h, 2e-4f, what + ": the first dash 0.5 h under the border");
                    Program.Near(dashes[dashes.Count - 1].Bottom, part.H - e0 - 0.5f * h, 2e-4f, what + ": the last 0.5 h over it");
                    for (int k = 0; k < dashes.Count; k++)
                    {
                        Program.Near(dashes[k].H, dashes[0].H, 2e-4f, what + ": dashes of one length");
                        Program.Near(dashes[k].W, 0.2f * h, 1e-5f, what + ": 0.2 h wide");
                        if (k > 0)
                            Program.Near(dashes[k].Y - dashes[k - 1].Bottom, dashes[0].H, 2e-4f, what + ": gaps as long as the dashes");
                    }

                    Program.Check(dashes[0].H >= 0.4f * h && dashes[0].H <= 0.9f * h, what + ": a dash of " + dashes[0].H / h + " h is near the 0.6 h of the drawing");
                }
            }

            Program.Check(lines > 100, "lines checked: " + lines);

            // What was wrong: the old loop ended each line in a stub where the length was not a multiple of 1.2 h: 0.2 h on the default board, 0.1 h on the strip.
            foreach ((string id, float expected) in new[] { ("gb.guide8.04", 0.2f), ("gb.guide8.04b", 0.1f) })
            {
                GuideTemplate t = GuideTemplates.ById(id);
                GuideSpec spec = t.Parse("speed=60");
                GuidePart part = t.Layout(spec).Main;
                float h = H(spec);
                List<float> old = OldDashLengths(0.5f * h, part.H - 2f * part.Edge - 0.5f * h, h);
                Program.Near(old[old.Count - 1], expected * h, 1e-4f, id + ": the old loop's last dash was " + expected + " h among dashes of 0.6 h");
            }

            // Everything else of the two boards is as it was on main (arrows, names, sizes): the digest of the elements that are not the dashes.
            var golden = new Dictionary<string, ulong>
            {
                { "gb.guide8.04|", 3845767959755547370UL },
                { "gb.guide8.04|n=3;a1=sl;a2=s;a3=sr", 12434704054115204858UL },
                { "gb.guide8.04|n=6;a1=l;a2=s;a3=s;a4=r;a5=sl;a6=sr", 16701153680506895227UL },
                { "gb.guide8.04|n=2;to1=甲路;to2=乙路;a1=s;a2=r;speed=60", 7170219062565325209UL },
                { "gb.guide8.04|n=1", 7440905761700833889UL },
                { "gb.guide8.04b|", 5281657883903550920UL },
                { "gb.guide8.04b|n=3;a1=sl;a2=s;a3=sr", 5021587473066568315UL },
                { "gb.guide8.04b|n=6;a1=l;a2=s;a3=s;a4=r;a5=sl;a6=sr", 15916430628569209255UL },
                { "gb.guide8.04b|n=1", 8785439432633327326UL }
            };
            foreach (KeyValuePair<string, ulong> pair in golden)
            {
                string[] parts = pair.Key.Split('|');
                GuideTemplate t = GuideTemplates.ById(parts[0]);
                ulong digest = Fnv(Digest(t.Layout(t.Parse(parts[1])), e => e.Kind != GuideElementKind.Rect));
                Program.Check(digest == pair.Value, pair.Key + ": arrows, names and size are as on main (" + digest + ")");
            }
        }

        // ------------------------------------------------------------------ the rest of the catalogue

        /// <summary>The boards that share 路4's helpers, the lane figures or the bottom bands, by their ids on main 35cb654: the 路 series, the lane direction board and the expressway boards with lane arrows.</summary>
        private static readonly string[] kFrozen =
        {
            "gb.guide8.01", "gb.guide8.02", "gb.guide8.203c", "gb.guide8.03", "gb.guide8.05", "gb.guide8.06", "gb.guide8.07", "gb.guide8.08", "gb.guide8.09", "gb.guide8.10", "gb.guide8.11", "gb.guide8.12",
            "gb.guide8.13", "gb.guide8.14", "gb.guide8.15", "gb.guide8.16", "gb.guide8.17", "gb.guide8.18", "gb.guide8.19", "gb.guide8.20", "gb.guide8.21", "gb.guide8.22", "gb.guide8.23", "gb.guide8.24",
            "gb.guide8.25", "gb.guide8.26", "gb.guide8.27", "gb.guide8.28", "gb.guide8.55", "gb.guide8.57", "gb.guide8.59", "gb.guide8.61", "gb.guide8.62", "gb.guide8.64", "gb.guide8.65", "gb.guide8.66",
            "gb.guide8.67", "gb.guide8.68", "gb.guide8.69", "gb.guide8.70", "gb.guide8.71", "gb.guide8.72", "gb.guide8.75", "gb.guide8.76", "gb.guide8.77", "gb.mand.17", "gb.guide9.47b", "gb.guide9.47c",
            "gb.guide9.49", "gb.guide9.51", "gb.guide9.268b"
        };

        private const ulong FrozenDigest = 9369016256527371926UL;

        private static void Neighbours()
        {
            ulong all = 14695981039346656037UL;
            int count = 0;
            foreach (string id in kFrozen)
            {
                GuideTemplate t = GuideTemplates.ById(id);
                Program.Check(t != null, id + " is in the catalogue");
                if (t == null)
                    continue;
                foreach (string band in new[] { "100", "80", "60", "30" })
                {
                    all = (all ^ Fnv(id + band + Digest(t.Layout(t.Default().With("speed", band))))) * 1099511628211UL;
                    count++;
                }
            }

            Console.WriteLine("  frozen digest " + all + " over " + count);
            Program.Check(all == FrozenDigest, "the boards next to 路4 draw what they drew on main (" + all + ")");

            // Only the strip of the lane choice board bleeds below the inside: every other default board of the catalogue leaves it.
            foreach (GuideTemplate t in GuideTemplates.All)
            {
                if (!t.Standalone)
                    continue;
                GuideBoard board = t.Layout(t.Default());
                bool below = board.Parts.Any(p => p.Elements.Any(e => e.BleedBottom));
                Program.Check(below == (t.Id == LanePickSigns.Id), t.Id + ": bleeds below only for the lane choice board");
            }
        }

        // ------------------------------------------------------------------ a digest of what a board draws

        private static string F(float v) => v.ToString("F5", CultureInfo.InvariantCulture);

        private static string Digest(GuideBoard board, Func<GuideElement, bool> keep = null)
        {
            var sb = new StringBuilder();
            foreach (GuidePart part in board.Parts)
            {
                sb.Append(F(part.X)).Append(',').Append(F(part.Y)).Append(',').Append(F(part.W)).Append(',').Append(F(part.H)).Append(',').Append((int)part.Ground).Append(',').Append((int)part.Shape).Append(';');
                foreach (GuideElement e in part.Elements)
                {
                    if (keep != null && !keep(e))
                        continue;
                    sb.Append((int)e.Kind).Append(',').Append((int)e.Colour).Append(',').Append(F(e.X)).Append(',').Append(F(e.Y)).Append(',').Append(F(e.W)).Append(',').Append(F(e.H)).Append(',')
                        .Append(F(e.RadiusTop)).Append(',').Append(F(e.RadiusBottom)).Append(',').Append(F(e.Bleed)).Append(',').Append(F(e.Gap)).Append(',').Append((int)e.Mode).Append(',').Append((int)e.Align).Append(',')
                        .Append(e.Text).Append(',').Append(e.Art ? 1 : 0).Append(',');
                    if (e.Contours != null)
                    {
                        double sum = 0.0;
                        foreach (float[] c in e.Contours)
                        {
                            foreach (float v in c)
                                sum += v;
                        }

                        sb.Append(sum.ToString("F4", CultureInfo.InvariantCulture));
                    }

                    if (e.Points != null)
                        sb.Append(e.Points.Sum().ToString("F4", CultureInfo.InvariantCulture));
                    sb.Append('|');
                }

                sb.Append('#');
            }

            return sb.ToString();
        }

        private static ulong Fnv(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in text)
                h = (h ^ c) * 1099511628211UL;
            return h;
        }
    }
}
