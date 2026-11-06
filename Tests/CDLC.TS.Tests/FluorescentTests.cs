using System;
using System.IO;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The fluorescent yellow-green of GB 5768.2-2022 Table 1 and 2 (the owner's list of the standard's gaps, 3 Nov): its colour, and the five boards that may have it (警10, 警11, 警12, 示16's frame,
    /// the school bus 示40's frame and 辅14 学校). The numbers are the printed figures', measured on the rendered pages of the PDF, written out here and not read from the constants. A board
    /// without the setting is the board of 0.12.79: Golden() compares every entry of the panel with a table of hashes made on that build (Tests/CDLC.TS.Tests/fluorescent-golden.tsv, made with the "dump" argument on 0.12.79). What the
    /// colour looks like under the game's lights is not here.
    /// </summary>
    internal static class FluorescentTests
    {
        public static void RunAll()
        {
            Program.Run("fluorescent yellow-green: the colour, appended after every colour there was, in the printed yellow's light", Colour);
            Program.Run("fluorescent yellow-green: 警10, 警11 and 警12 take it as a ground, yellow when the setting is not there", Grounds);
            Program.Run("fluorescent yellow-green: the frame of 示16 is 10 cm wide round a sign that keeps its size (Fig. 97 b)", CrossingFrame);
            Program.Run("fluorescent yellow-green: the frame of the school bus 示40 and the ground of 辅14 学校 (Fig. 128 a, 159)", SchoolBoards);
            Program.Run("fluorescent yellow-green: the colour a face has before its picture is there is the sign's, not the frame's", Placeholder);
            Program.Run("fluorescent yellow-green: no board of the panel changed where the setting is not used (hashes of 0.12.79)", Golden);
        }

        private static float Linear(int v)
        {
            float c = v / 255f;
            return c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);
        }

        private static float Luminance(GuideColour colour)
        {
            GuideColours.Rgb(colour, out byte r, out byte g, out byte b);
            return 0.2126f * Linear(r) + 0.7152f * Linear(g) + 0.0722f * Linear(b);
        }

        private static GuideBoard Board(string id, string settings)
        {
            SignChoice choice = SignCatalog.ChoiceById(id);
            GuideTemplate t = choice.Def.Template;
            return t.Layout(t.Parse(settings));
        }

        private static string SpecOf(string id, string settings) => SignCatalog.ChoiceById(id).Def.Template.Parse(settings).ToString();

        private static void Colour()
        {
            // The enum: after the last colour there was (the 23rd of the lines), so no number that was saved moved.
            Program.Check((int)GuideColour.FluorescentYellowGreen == (int)GuideColour.Line23 + 1 && Enum.GetValues(typeof(GuideColour)).Cast<int>().Max() == (int)GuideColour.FluorescentYellowGreen, "appended last");
            Program.Check((int)GuideColour.FluorescentFireRed == 11 && (int)GuideColour.Line1 == 16, "the colours there were have their numbers");

            GuideColours.Rgb(GuideColour.FluorescentYellowGreen, out byte r, out byte g, out byte b);
            Program.Check(r == 176 && g == 218 && b == 63, "the colour: " + r + "," + g + "," + b);
            // The figures print the yellow-green (#9DC337) at 0.862 of the yellow's (#F8B64A) luminance; the panel's yellow is brighter than the print's, so the same relation is kept.
            float printedYellow = 0.2126f * Linear(248) + 0.7152f * Linear(182) + 0.0722f * Linear(74);
            float printedGreen = 0.2126f * Linear(157) + 0.7152f * Linear(195) + 0.0722f * Linear(55);
            Program.Near(printedGreen / printedYellow, 0.862f, 0.002f, "the printed relation of the two");
            Program.Near(Luminance(GuideColour.FluorescentYellowGreen) / Luminance(GuideColour.Yellow), printedGreen / printedYellow, 0.01f, "the same relation in the panel's colours");
            // Hue as printed: the green channel leads, red follows, blue is low: a yellow-green between the panel's yellow (hue 50) and its green (151).
            Program.Check(g > r && r > b && b < 80, "a yellow-green");
            // A colour the panel had is as it was.
            GuideColours.Rgb(GuideColour.Yellow, out byte yr, out byte yg, out byte yb);
            GuideColours.Rgb(GuideColour.FluorescentFireRed, out byte fr, out byte fg, out byte fb);
            Program.Check(yr == 255 && yg == 214 && yb == 0 && fr == 255 && fg == 80 && fb == 55, "the yellow and the fire red are as they were");
        }

        private static void Grounds()
        {
            foreach (string id in new[] { "gb.warn.10", "gb.warn.11", "gb.warn.12" })
            {
                GuideTemplate t = SignCatalog.ChoiceById(id).Def.Template;
                GuideField field = t.FieldOf("ground");
                Program.Check(field != null && field.Sparse && field.Default == "y" && field.Choices.Length == 2, id + ": a sparse choice of two, yellow by default");
                string plain = t.Default().ToString();
                Program.Check(!plain.Contains("ground"), id + ": the default spec has no key for it: " + plain);

                GuidePart yellow = Board(id, "").Parts[0], green = Board(id, "ground=g").Parts[0];
                Program.Check(yellow.Ground == GuideColour.Yellow && green.Ground == GuideColour.FluorescentYellowGreen, id + ": the ground");
                // Three nested regions as Table 8 has them: the black edge, then the field in the ground's colour (SignStyles.Warning), and the figure is black.
                Program.Check(green.Elements[0].Colour == GuideColour.Black && green.Elements[1].Colour == GuideColour.FluorescentYellowGreen, id + ": the black edge and the field of the green ground");
                Program.Check(!green.Elements.Any(e => e.Colour == GuideColour.Yellow), id + ": no yellow is left on the green ground");
                Program.Check(yellow.Elements.Count == green.Elements.Count, id + ": the same figure on both");
            }

            // 警10: the gap round the walker is cut in the ground's own colour, so it is invisible whichever the ground is (a yellow one on the green would be a halo).
            GuidePart walker = Board("gb.warn.10", "ground=g").Parts[0];
            Program.Check(walker.Elements.Count(e => e.Colour == GuideColour.FluorescentYellowGreen) == 2, "警10: the field and the walker's gap are the green");
            Program.Check(Board("gb.warn.10", "").Parts[0].Elements.Count(e => e.Colour == GuideColour.Yellow) == 2, "警10: the same two are yellow on the yellow ground");
            // 警13 and the others are not offered it: the standard has no green for them (Fig. 161 is yellow only).
            Program.Check(SignCatalog.ChoiceById("gb.warn.13").Def.Template.FieldOf("ground") == null, "警13 has no such setting");
        }

        private static void CrossingFrame()
        {
            GuideTemplate t = SignCatalog.ChoiceById("gb.mand.16").Def.Template;
            GuideField field = t.FieldOf("frame");
            Program.Check(field != null && field.Sparse && field.Kind == GuideFieldKind.Flag && field.Default == "0", "a sparse flag, off by default");
            Program.Check(!t.Default().ToString().Contains("frame"), "the default spec has no key for it: " + t.Default());

            GuidePart plain = Board("gb.mand.16", "").Parts[0], framed = Board("gb.mand.16", "frame=1").Parts[0];
            float sign = plain.W;
            // Fig. 97 b (measured on the page: the frame 43.5 px each side of a blue square of 369 px = 0.118 of its side; the text says 10 cm, 0.125 of an 80 cm sign): 10 cm each side.
            Program.Near(framed.W, sign + 0.20f, 1e-5f, "the frame adds 10 cm on every side");
            Program.Near(framed.H, sign + 0.20f, 1e-5f, "and above and below");
            Program.Check(framed.Ground == GuideColour.FluorescentYellowGreen, "the frame is the part's ground");
            // The sign inside is not made smaller (6.13: 并不应减小示16的尺寸): the blue element is the plain sign's inside.
            GuideElement blue = framed.Elements.First(e => e.Colour == GuideColour.Blue);
            Program.Near(blue.X, 0.10f + plain.Border, 1e-5f, "the blue starts at the frame plus the white liner");
            Program.Near(blue.W, sign - 2f * plain.Border, 1e-5f, "and is as wide as the plain sign's blue");
            GuideElement white = framed.Elements.First(e => e.Colour == GuideColour.White);
            Program.Near(white.W, sign, 1e-5f, "the white liner's outer size is the plain sign's");
            // The figure is where the plain sign's drawing puts it, in the sign's own frame: the triangle's top at 14.1 % of the sign's side from the sign's top (E.65).
            GuideElement triangle = framed.Elements.First(e => e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White);
            float top = triangle.Y - 0.10f;
            Program.Near(top / sign, 0.141f, 0.003f, "the triangle's apex stays at 14.1 % of the sign");
            Program.Check(framed.Safe != null && framed.Safe[0] > 0.10f, "the safe region is inside the frame");
            // The outer corner is round: the frame's width and the sign's own corner (Fig. 97 b's measures 0.13 of the sign's side; 0.14 here).
            Program.Near(framed.Radius, 0.04f + 0.10f, 1e-5f, "the outer corner");
        }

        private static void SchoolBoards()
        {
            GuideTemplate t = SignCatalog.ChoiceById("gb.mand.40").Def.Template;
            GuideField field = t.FieldOf("frame");
            Program.Check(field != null && field.Sparse && field.Visible != null, "示40: a sparse flag, shown only for a kind that has the frame");
            Program.Check(field.Visible(t.Parse("kind=school")) && !field.Visible(t.Parse("kind=taxi")) && !field.Visible(t.Parse("kind=p")), "示40: shown for the school bus only");
            Program.Check(!t.Default().ToString().Contains("frame"), "示40: the default spec has no key for it");

            GuidePart plain = Board("gb.mand.40", "kind=school").Parts[0], framed = Board("gb.mand.40", "kind=school;frame=1").Parts[0];
            Program.Near(framed.W, plain.W + 0.20f, 1e-5f, "Fig. 128 a: the frame adds 10 cm (6.31 d says 10 to 15; the least is drawn)");
            Program.Check(framed.Ground == GuideColour.FluorescentYellowGreen, "the school bus's frame is the yellow-green");
            // The characters 校车 and the bus are the plain sign's: the same number of elements, the framed one has the two backgrounds (white and blue) in place of the plain part's own.
            Program.Check(framed.Elements.Count == plain.Elements.Count + 2, "the same figure: " + framed.Elements.Count + " against " + plain.Elements.Count);
            // The frame of any other kind is ignored (the standard has it for the school bus only).
            GuidePart taxi = Board("gb.mand.40", "kind=taxi;frame=1").Parts[0];
            Program.Check(taxi.Ground == GuideColour.Blue && Math.Abs(taxi.W - Board("gb.mand.40", "kind=taxi").Parts[0].W) < 1e-6f, "a taxi's board keeps its blue, whatever the setting says");

            // 辅14 学校: white, or the fluorescent yellow-green, black text and border on both (Fig. 159, 128 b; Table 2).
            GuideTemplate aux = SignCatalog.ChoiceById("gb.aux.14").Def.Template;
            GuideField ground = aux.FieldOf("ground");
            Program.Check(ground != null && ground.Sparse && ground.Default == "w", "辅14: a sparse choice, white by default");
            Program.Check(!aux.Default().ToString().Contains("ground"), "辅14: the default spec has no key for it");
            GuidePart w = Board("gb.aux.14", "").Parts[0], g = Board("gb.aux.14", "ground=g").Parts[0];
            Program.Check(w.Ground == GuideColour.White && g.Ground == GuideColour.FluorescentYellowGreen, "辅14: the ground");
            Program.Check(g.BorderColour == w.BorderColour && Math.Abs(g.W - w.W) < 1e-6f && g.Elements.Count == w.Elements.Count && g.Elements.All(e => e.Colour != GuideColour.White), "辅14: the same board, the same border and text colour, no white on the green");
            // The other reason plates have no ground setting (Table 2 names 学校 and 校车 only).
            Program.Check(SignCatalog.ChoiceById("gb.aux.15").Def.Template.FieldOf("ground") == null && SignCatalog.ChoiceById("gb.aux.20").Def.Template.FieldOf("ground") == null, "辅15 to 辅19 and 辅20 do not have it");
        }

        private static void Placeholder()
        {
            Func<string, string, (byte r, byte g, byte b)> stand = (id, settings) =>
            {
                SignChoice choice = SignCatalog.ChoiceById(id);
                FaceDetail.Placeholder(SignInstances.Of(choice.Def, choice.Def.Template.Parse(settings).ToString()), out byte r, out byte g, out byte b);
                return (r, g, b);
            };
            GuideColours.Rgb(GuideColour.Blue, out byte br, out byte bg, out byte bb);
            GuideColours.Rgb(GuideColour.FluorescentYellowGreen, out byte gr, out byte gg, out byte gb);
            // A framed 示16 or school-bus 示40 is a blue sign in a frame: until its picture is there it is blue, as the plain one is, not the frame's green.
            Program.Check(stand("gb.mand.16", "frame=1") == (br, bg, bb) && stand("gb.mand.16", "") == (br, bg, bb), "示16, framed or not: blue");
            Program.Check(stand("gb.mand.40", "kind=school;frame=1") == (br, bg, bb), "示40 school bus, framed: blue");
            // A warning sign on the green ground IS green all over: its placeholder is the green, and the plain one the yellow.
            Program.Check(stand("gb.warn.10", "ground=g") == (gr, gg, gb), "警10 on the green ground: the green");
            GuideColours.Rgb(GuideColour.Yellow, out byte yr, out byte yg, out byte yb);
            Program.Check(stand("gb.warn.10", "") == (yr, yg, yb), "警10 on the yellow ground: the yellow");
            // 辅14 on the green ground: the green (the ground is the whole plate inside its black border).
            Program.Check(stand("gb.aux.14", "ground=g") == (gr, gg, gb), "辅14 on the green ground: the green");
        }

        private static void Golden()
        {
            // The hashes of every board of the panel as 0.12.79 made them (BoardDump, the "dump" argument): a board that is not changed by a setting of this change must be the same.
            string file = FontLetterTests.RepoFile("Tests", "CDLC.TS.Tests", "fluorescent-golden.tsv");
            if (file == null)
            {
                Program.Check(false, "the table of hashes (Tests/CDLC.TS.Tests/fluorescent-golden.tsv) is not found");
                return;
            }

            var golden = File.ReadAllLines(file).Select(l => l.Split('\t')).Where(p => p.Length == 3).ToDictionary(p => p[0], p => (spec: p[1], hash: p[2]));
            int compared = 0, notComparable = 0, redrawn = 0;
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (!golden.TryGetValue(choice.Id, out var was))
                    continue;
                // The text light boxes and the lamp-post neon boards are laid out with the font the tests before this one left set (TextFonts, MarkingLetters): their hash is not the same in a
                // full run as in the fresh process that made the table. Found by running this very comparison on the unchanged 0.12.79 code: the same nine entries differ there with the
                // same hashes. Nothing of this change touches them; they are left out of the comparison (and counted), not made to pass.
                if (choice.Id.StartsWith("custom.", StringComparison.Ordinal) || choice.Id.StartsWith("lamp.neon.", StringComparison.Ordinal))
                {
                    notComparable++;
                    continue;
                }

                string spec = choice.Spec ?? "";
                Program.Check(spec == was.spec, choice.Id + ": the settings of the entry are as they were: " + spec + " against " + was.spec);
                // The junctions-ahead board was redrawn on purpose after 0.12.79 (the owner: "太糙了"): its picture is no longer 0.12.79's, and nothing else is allowed to differ (checked on the merge:
                // the other 510 entries of the table have the same hashes). Its own tests pin the new drawing in literal numbers.
                if (choice.Id == "gb.guide8.01c")
                {
                    redrawn++;
                    continue;
                }

                if (choice.Def != null && choice.Def.Template != null && !choice.Def.IsMarking)
                {
                    string hash = BoardDump.Hash(choice.Def.Template.Layout(choice.Def.Template.Parse(spec)));
                    Program.Check(hash == was.hash, choice.Id + ": the board is as it was (" + hash + " against " + was.hash + ")");
                    compared++;
                }
            }

            Program.Check(compared >= 340 && notComparable <= 12 && redrawn == 1, "boards compared: " + compared + ", left out for the font: " + notComparable + ", redrawn on purpose: " + redrawn);
        }
    }
}
