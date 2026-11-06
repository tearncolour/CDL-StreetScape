using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// A text light box's text on an arc (0.12.76): the layout (every cell on its circle, the letters keeping their spacing, the box round them all, the limits and the odd inputs), the dressing that carries
    /// the sweep, the letters turned on a board (a flat one and a neon tube), the rear frame carried over from the straight line (studs in the letters, rails through the studs), the board built on a post,
    /// the template's fields and the editor's picture. How it looks in the game is not testable here.
    /// </summary>
    internal static class TextArcTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("arc: the layout on a circle", Circle);
            Program.Run("arc: a smile, lines, and the limits", Lines);
            Program.Run("arc: odd inputs", Odd);
            Program.Run("arc: a leaning letter has room in the box", Lean);
            Program.Run("arc: the dressing and the job carry the sweep", Dressing);
            Program.Run("arc: glyphs turned on a board", Turned);
            Program.Run("arc: the frame carried over", Frame);
            Program.Run("arc: built on a post, and the template", Built);
            Program.Run("arc: the editor's picture", Picture);
            Program.Run("arc: pictures for a look (when TS_STYLE_PREVIEW names a folder)", Pictures);
        }

        private static readonly TextFace Face = new TextFace(1000f, -200f, 800f, c => c == 'x' ? -1f : c == 'i' ? 0f : 600f);

        private static TextLayoutResult Straight(string text, float height = 0.5f, TextAlign align = TextAlign.Centre) =>
            TextLayout.Layout(text, new TextStyle { LetterHeight = height, LetterSpacing = 0f, LineSpacing = 1.15f, Align = align }, Face);

        private static Point2 Centre(TextLayoutResult layout, PlacedGlyph g) => ArcTextLayout.Turned(g.Angle, g.Advance * 0.5f, (layout.EmTopMetres + layout.EmBottomMetres) * 0.5f, g.X, g.Y);

        private static IEnumerable<Point2> Corners(TextLayoutResult layout, PlacedGlyph g)
        {
            for (int k = 0; k < 4; k++)
                yield return ArcTextLayout.Turned(g.Angle, (k & 1) != 0 ? g.Advance : 0f, (k & 2) != 0 ? layout.EmTopMetres : layout.EmBottomMetres, g.X, g.Y);
        }

        private static float Distance(Point2 a, Point2 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static void Circle()
        {
            TextLayoutResult line = Straight("ABCDEFGHIJ");
            TextLayoutResult arch = ArcTextLayout.Bend(line, 90f, 1);
            ArcInfo arc = arch.Arc;
            Program.Check(arc != null && ReferenceEquals(arc.Straight, line) && ReferenceEquals(arc.Bent, arch) && arc.Direction == 1, "the arc holds the straight layout and the bent one");
            Program.Near(line.Width, 3f, 1e-4f, "ten letters of 0.3 m: " + line.Width);
            Program.Near(arc.Radius, 3f / (float)(Math.PI / 2.0), 1e-4f, "the widest line's radius is its width over the sweep: " + arc.Radius);
            Program.Check(!arc.Limited && Math.Abs(arc.EffectiveSweep - Math.PI / 2.0) < 1e-5, "a bend that is allowed is not limited");
            Program.Check(arch.Glyphs.Count == 10 && arch.Lines == 1 && arch.LineWidths.SequenceEqual(line.LineWidths) && arch.Scale == line.Scale && arch.MetresPerEm == line.MetresPerEm && arch.LetterHeight == line.LetterHeight, "the glyphs and the figures of the line are kept");

            // Every cell's middle is the straight layout's middle bent, on the circle of the line; the origin and every point of the cell are carried rigidly.
            float middle = (line.EmTopMetres + line.EmBottomMetres) * 0.5f;
            Point2 axis = arc.Bend(0f, 0f);
            Point2 centre = new Point2(axis.X, axis.Y - arc.Radius);
            for (int g = 0; g < 10; g++)
            {
                PlacedGlyph from = line.Glyphs[g], to = arch.Glyphs[g];
                Point2 cell = Centre(arch, to), expected = arc.Bend(from.X + from.Advance * 0.5f, 0f);
                Program.Check(Distance(cell, expected) < 1e-4f, $"glyph {g}: the cell's middle is the bent middle: {cell.X},{cell.Y} against {expected.X},{expected.Y}");
                Program.Near(Distance(cell, centre), arc.Radius, 1e-4f, $"glyph {g} is on the circle");
                Program.Check(Distance(arc.Carry(g, new Point2(from.X, from.Y)), new Point2(to.X, to.Y)) < 1e-4f, $"glyph {g}: its origin is carried to where it is");
                Point2 carried = arc.Carry(g, new Point2(from.X + 0.1f, from.Y + 0.2f));
                Program.Near(Distance(carried, new Point2(to.X, to.Y)), (float)Math.Sqrt(0.1f * 0.1f + 0.2f * 0.2f), 1e-4f, $"glyph {g}: a point moves with it as a rigid body");
                Program.Check(to.Advance == from.Advance && to.Codepoint == from.Codepoint && to.Line == from.Line && to.Missing == from.Missing, $"glyph {g}: its character and cell are kept");
            }

            // The spacing along the circle is the advance, and each letter is turned by the angle it advances.
            for (int g = 1; g < 10; g++)
            {
                float step = arch.Glyphs[g - 1].Angle - arch.Glyphs[g].Angle;
                Program.Near(step, 0.3f / arc.Radius, 1e-5f, $"glyph {g} is turned by what it advances on the circle");
                Program.Near(Distance(Centre(arch, arch.Glyphs[g - 1]), Centre(arch, arch.Glyphs[g])), 2f * arc.Radius * (float)Math.Sin(0.3f / arc.Radius * 0.5f), 1e-4f, $"glyph {g}: the chord between the cells' middles");
            }

            // An arch: the ends drop, the text is mirrored about the middle, the middle letters stand upright.
            Program.Check(Centre(arch, arch.Glyphs[0]).Y < Centre(arch, arch.Glyphs[4]).Y - 0.1f && Centre(arch, arch.Glyphs[9]).Y < Centre(arch, arch.Glyphs[5]).Y - 0.1f, "the ends of an arch drop");
            for (int g = 0; g < 5; g++)
            {
                Point2 a = Centre(arch, arch.Glyphs[g]), b = Centre(arch, arch.Glyphs[9 - g]);
                Program.Check(Math.Abs(a.X + b.X) < 1e-4f && Math.Abs(a.Y - b.Y) < 1e-4f && Math.Abs(arch.Glyphs[g].Angle + arch.Glyphs[9 - g].Angle) < 1e-5f, $"glyphs {g} and {9 - g} are mirrored about the middle");
            }

            Program.Check(arch.Glyphs[0].Angle > 0f && arch.Glyphs[9].Angle < 0f, "the first letters lean to the left of the way up, the last to the right");

            // The box holds every cell, touches each side, and the text's middle is at its middle.
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (PlacedGlyph g in arch.Glyphs)
            {
                foreach (Point2 p in Corners(arch, g))
                {
                    minX = Math.Min(minX, p.X);
                    maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxY = Math.Max(maxY, p.Y);
                }
            }

            Program.Near(maxX - minX, arch.Width, 1e-4f, "the box is as wide as the cells reach");
            Program.Near(maxY - minY, arch.Height, 1e-4f, "and as high");
            Program.Check(Math.Abs(minX + maxX) < 1e-4f && Math.Abs(minY + maxY) < 1e-4f, "with the middle of the text at its middle: " + (minX + maxX) + ", " + (minY + maxY));
            Program.Check(arch.Width > line.Width * 0.6f && arch.Height > line.Height * 2f, "a bend makes the box higher and narrower than the line: " + arch.Width + " x " + arch.Height);

            // The bend does not change the straight layout, and a second bend of the result is the result.
            Program.Check(line.Arc == null && ReferenceEquals(ArcTextLayout.Bend(arch, 120f, 1), arch), "the straight layout is left as it was and a bent one is not bent again");
            TextLayoutResult again = ArcTextLayout.Bend(line, 90f, 1);
            Program.Check(again.Glyphs.Zip(arch.Glyphs, (a, b) => a.X == b.X && a.Y == b.Y && a.Angle == b.Angle).All(x => x) && again.Width == arch.Width, "bent again from the straight, the same");
            Program.Check(arch.Arc.Squeeze > 0.79f && arch.Arc.Squeeze < 1f, "the inner side's spacing: " + arch.Arc.Squeeze);
        }

        private static void Lines()
        {
            // A smile has its centre above: the ends rise, the cells are on the circle, the letters hang inside it.
            TextLayoutResult line = Straight("ABCDEFGHIJ");
            TextLayoutResult smile = ArcTextLayout.Bend(line, 90f, -1);
            ArcInfo arc = smile.Arc;
            Point2 axis = arc.Bend(0f, 0f);
            Point2 centre = new Point2(axis.X, axis.Y + arc.Radius);
            for (int g = 0; g < 10; g++)
            {
                PlacedGlyph from = line.Glyphs[g], to = smile.Glyphs[g];
                Point2 cell = Centre(smile, to);
                Program.Check(Distance(cell, arc.Bend(from.X + from.Advance * 0.5f, 0f)) < 1e-4f && Math.Abs(Distance(cell, centre) - arc.Radius) < 1e-4f, $"smile, glyph {g}: on the circle");
            }

            Program.Check(Centre(smile, smile.Glyphs[0]).Y > Centre(smile, smile.Glyphs[4]).Y + 0.1f && smile.Glyphs[0].Angle < 0f && smile.Glyphs[9].Angle > 0f, "the ends of a smile rise, and the letters lean the other way");
            Program.Near(smile.Width, ArcTextLayout.Bend(line, 90f, 1).Width, 0.1f, "a smile is about as wide as an arch of the same sweep: " + smile.Width);

            // Two lines are concentric a pitch apart: the first line's circle is the larger for an arch, the smaller for a smile; both the widest.
            TextLayoutResult two = Straight("ABCDEFGHIJ\nABCDEF");
            TextLayoutResult archTwo = ArcTextLayout.Bend(two, 90f, 1), smileTwo = ArcTextLayout.Bend(two, 90f, -1);
            float pitch = (two.Height - two.LetterHeight);
            foreach ((TextLayoutResult layout, int d) in new[] { (archTwo, 1), (smileTwo, -1) })
            {
                ArcInfo a = layout.Arc;
                Point2 c = a.Bend(0f, 0f);
                Point2 middle = new Point2(c.X, c.Y - d * a.RadiusAt(0f));
                // The circle's centre is the same for both lines; the radius of each line's cells.
                float r0 = Distance(Centre(layout, layout.Glyphs[0]), middle), r1 = Distance(Centre(layout, layout.Glyphs[10]), middle);
                Program.Check(!a.Limited && Math.Abs(r0 - a.Radius) < 1e-4f, $"d {d}: the widest line (the first) is the sweep's circle");
                Program.Near(r0 - r1, d * pitch, 1e-4f, $"d {d}: the second line's circle is a pitch {(d > 0 ? "inside" : "outside")}: {r0 - r1}");
                Program.Check(layout.Glyphs.Skip(10).All(g => Math.Abs(Distance(Centre(layout, g), middle) - r1) < 1e-4f), $"d {d}: the second line's letters are on its circle");
                // The shorter line is centred on the text's middle: its cells are mirrored about the circle's axis.
                Point2 first = Centre(layout, layout.Glyphs[10]), last = Centre(layout, layout.Glyphs[15]);
                Program.Check(Math.Abs((first.X - middle.X) + (last.X - middle.X)) < 1e-4f && Math.Abs(first.Y - last.Y) < 1e-4f, $"d {d}: the shorter line is centred");
            }

            // The widest line is the one that has the sweep, wherever it is.
            TextLayoutResult wideSecond = Straight("ABC\nABCDEFGHIJ");
            TextLayoutResult second = ArcTextLayout.Bend(wideSecond, 90f, 1);
            Program.Check(!second.Arc.Limited && Math.Abs(second.Arc.EffectiveSweep - Math.PI / 2.0) < 1e-5 && Math.Abs(second.Arc.Radius - 3f / (float)(Math.PI / 2.0)) < 1e-4f, "the widest line, the second, has the sweep");
            Point2 cs = second.Arc.Bend(0f, 0f);
            Program.Near(Distance(Centre(second, second.Glyphs[3]), new Point2(cs.X, cs.Y - second.Arc.RadiusAt(0f))), second.Arc.Radius, 1e-3f, "and its cells are on the sweep's circle");

            // Left and right alignment: the lines start (or end) where the straight lines do, about the text's middle.
            TextLayoutResult leftLine = Straight("ABCDEFGHIJ\nABC", 0.5f, TextAlign.Left);
            TextLayoutResult left = ArcTextLayout.Bend(leftLine, 90f, 1);
            float secondY = leftLine.Height * 0.5f - (leftLine.Height - leftLine.LetterHeight) - leftLine.LetterHeight * 0.5f;
            Program.Near(left.Glyphs[10].Angle, -(leftLine.Glyphs[10].X + leftLine.Glyphs[10].Advance * 0.5f) / left.Arc.RadiusAt(secondY), 1e-5f, "a left-aligned short line starts where the first does along its own circle");
            Program.Check(left.Glyphs[10].X < left.Glyphs[0].X + 0.5f && left.Glyphs[12].Angle > 0f, "and stays on the left");

            // Too much bend: limited at the inner side's crowding (ArcInfo.MinRadiusInHeights letter heights), and said so.
            TextLayoutResult small = Straight("ABCDE");
            TextLayoutResult limited = ArcTextLayout.Bend(small, 180f, 1);
            Program.Check(limited.Arc.Limited && Math.Abs(limited.Arc.Radius - ArcInfo.MinRadiusInHeights * small.LetterHeight) < 1e-4f, "a short text bent too far is bent to the least radius: " + limited.Arc.Radius);
            Program.Near(limited.Arc.EffectiveSweep, small.Width / (ArcInfo.MinRadiusInHeights * small.LetterHeight), 1e-5f, "its sweep is what the radius allows");
            Program.Near(limited.Arc.Squeeze, 0.8f, 1e-4f, "the inner side keeps four fifths of the spacing, no less");
            List<TextWarning> warnings = TextWarnings.Of(limited, null);
            Program.Check(warnings.Count == 1 && warnings[0].Kind == TextWarningKind.ArcLimited && warnings[0].Count == (int)Math.Round(limited.Arc.EffectiveSweep * 180.0 / Math.PI) && TextHints.Shown(warnings[0]), "the editor is told how far it bent: " + warnings.Count);
            Program.Check(TextHints.Of(warnings[0], true).Contains(warnings[0].Count + "°") && TextHints.Of(warnings[0], false).Contains(warnings[0].Count + "°"), "in both languages, with the number");
            Program.Check(TextWarnings.Of(ArcTextLayout.Bend(Straight("ABCDEFGHIJ"), 90f, 1), null).Count == 0, "a bend that is allowed says nothing");

            // Two lines limited by the line nearest the centre: the second line, a pitch inside an arch.
            TextLayoutResult tight = ArcTextLayout.Bend(Straight("ABCDEF\nABCDEF"), 120f, 1);
            Program.Near(tight.Arc.Radius - pitch, ArcInfo.MinRadiusInHeights * small.LetterHeight, 1e-4f, "the nearest line's circle has the least radius: " + tight.Arc.Radius);
            TextLayoutResult tightSmile = ArcTextLayout.Bend(Straight("ABCDEF\nABCDEF"), 120f, -1);
            Program.Near(tightSmile.Arc.Radius, ArcInfo.MinRadiusInHeights * small.LetterHeight, 1e-4f, "and for a smile, the first line is the nearest: " + tightSmile.Arc.Radius);

            // A blank first line has no letters and does not close the circle: the smile's nearest line is the second.
            TextLayoutResult blankFirst = ArcTextLayout.Bend(Straight("\nABCDEFGHIJ"), 120f, -1);
            Program.Check(blankFirst.Arc != null && !blankFirst.Arc.Limited && Math.Abs(blankFirst.Arc.Radius - 3f / (float)(Math.PI * 2.0 / 3.0)) < 1e-4f, "a line with no letter does not limit the bend: " + blankFirst.Arc?.Radius);

            // The sweep is held between 10 and 300 degrees.
            TextLayoutResult wide = Straight("ABCDEFGHIJKL");
            Program.Near(ArcTextLayout.Bend(wide, 3f, 1).Arc.EffectiveSweep, 10f * (float)(Math.PI / 180.0), 1e-5f, "less than 10 degrees is 10");
            Program.Near(ArcTextLayout.Bend(wide, 900f, 1).Arc.Sweep, 300f * (float)(Math.PI / 180.0), 1e-5f, "more than 300 degrees is 300");
        }

        private static void Odd()
        {
            TextLayoutResult line = Straight("ABCDEFGHIJ");
            Program.Check(ReferenceEquals(ArcTextLayout.Bend(line, 90f, 0), line) && ReferenceEquals(ArcTextLayout.Bend(null, 90f, 1), null), "no direction, no layout: nothing is bent");
            TextLayoutResult empty = Straight("");
            Program.Check(ReferenceEquals(ArcTextLayout.Bend(empty, 90f, 1), empty) && empty.Arc == null, "no letter, no arc");
            TextLayoutResult one = ArcTextLayout.Bend(Straight("A"), 90f, 1);
            Program.Check(one.Arc != null && one.Glyphs.Count == 1 && Math.Abs(one.Glyphs[0].Angle) < 1e-6f && one.Width > 0f && one.Height > 0f, "one letter is on the top of the circle, upright");
            TextLayoutResult nan = ArcTextLayout.Bend(line, float.NaN, 1);
            Program.Near(nan.Arc.EffectiveSweep, 10f * (float)(Math.PI / 180.0), 1e-5f, "a sweep that is not a number is the least");

            // A missing character and a blank keep their cells; a letter of no width is on the circle with no width.
            TextLayoutResult holes = Straight("AxB iA");
            TextLayoutResult bent = ArcTextLayout.Bend(holes, 90f, 1);
            Program.Check(bent.Glyphs.Count == holes.Glyphs.Count && bent.Missing.SequenceEqual(holes.Missing) && bent.Glyphs[1].Missing && !bent.Glyphs[0].Missing, "a character the font lacks keeps its cell");
            Program.Check(bent.Glyphs.All(g => !float.IsNaN(g.X) && !float.IsNaN(g.Y) && !float.IsNaN(g.Angle) && !float.IsInfinity(g.X)), "all finite");

            // Whatever is asked, the figures are finite, every cell is inside the box, and the box is the cells' own.
            var random = new Random(7);
            const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ xi";
            for (int n = 0; n < 300; n++)
            {
                int lines = 1 + random.Next(3);
                string text = string.Join("\n", Enumerable.Range(0, lines).Select(_ => new string(Enumerable.Range(0, 1 + random.Next(12)).Select(__ => Letters[random.Next(Letters.Length)]).ToArray())));
                float height = 0.15f + (float)random.NextDouble() * 2.5f, sweep = (float)random.NextDouble() * 400f - 20f;
                int direction = random.Next(2) == 0 ? 1 : -1;
                TextLayoutResult s = Straight(text, height, (TextAlign)random.Next(3));
                TextLayoutResult b = ArcTextLayout.Bend(s, sweep, direction);
                if (s.Glyphs.Count == 0)
                {
                    Program.Check(ReferenceEquals(b, s), "an empty text: " + text);
                    continue;
                }

                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                bool finite = true;
                foreach (PlacedGlyph g in b.Glyphs)
                {
                    foreach (Point2 p in Corners(b, g))
                    {
                        finite &= !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsInfinity(p.X) && !float.IsInfinity(p.Y);
                        minX = Math.Min(minX, p.X);
                        maxX = Math.Max(maxX, p.X);
                        minY = Math.Min(minY, p.Y);
                        maxY = Math.Max(maxY, p.Y);
                    }
                }

                ArcInfo arc = b.Arc;
                bool ok = finite && Math.Abs(minX + maxX) < 1e-3f && Math.Abs(minY + maxY) < 1e-3f && Math.Abs(maxX - minX - b.Width) < 1e-3f && Math.Abs(maxY - minY - b.Height) < 1e-3f
                    && arc.EffectiveSweep <= arc.Sweep + 1e-5f && arc.Squeeze >= 0.79f && arc.Radius >= ArcInfo.MinRadiusInHeights * height - 1e-3f;
                Program.Check(ok, $"random {n}: \"{text.Replace("\n", "|")}\" at {height:0.00} m, {sweep:0} degrees, direction {direction}");
            }
        }

        private static void Lean()
        {
            const float Slant = 0.2126f;
            TextLayoutResult line = Straight("ABCDEFGHIJ");
            TextLayoutResult upright = ArcTextLayout.Bend(line, 90f, 1), leaning = ArcTextLayout.Bend(line, 90f, 1, Slant);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (PlacedGlyph g in leaning.Glyphs)
            {
                for (int k = 0; k < 4; k++)
                {
                    float lean = ((k & 2) != 0 ? 0.5f : -0.5f) * Slant * line.LetterHeight;
                    Point2 p = ArcTextLayout.Turned(g.Angle, ((k & 1) != 0 ? g.Advance : 0f) + lean, (k & 2) != 0 ? line.EmTopMetres : line.EmBottomMetres, g.X, g.Y);
                    minX = Math.Min(minX, p.X);
                    maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxY = Math.Max(maxY, p.Y);
                }
            }

            Program.Check(Math.Abs(maxX - minX - leaning.Width) < 1e-4f && Math.Abs(maxY - minY - leaning.Height) < 1e-4f && Math.Abs(minX + maxX) < 1e-4f && Math.Abs(minY + maxY) < 1e-4f, "the box holds the leaning cells and is centred on them");
            Program.Check(leaning.Width >= upright.Width - 1e-5f && leaning.Height >= upright.Height - 1e-5f && leaning.Width + leaning.Height > upright.Width + upright.Height + 0.01f, "a leaning text needs a larger box on an arc: " + leaning.Width + " x " + leaning.Height + " against " + upright.Width + " x " + upright.Height);
            Program.Check(leaning.Glyphs.Zip(upright.Glyphs, (a, b) => Math.Abs(a.Angle - b.Angle) < 1e-6f && Math.Abs(a.Advance - b.Advance) < 1e-6f).All(x => x) && Math.Abs(leaning.Arc.Radius - upright.Arc.Radius) < 1e-6f, "and the circle and the angles are the same");
            TextLayoutResult byArt = ArcTextLayout.Layout("ABCDEFGHIJ", TextStyle.Of(0.5f), Face, TextArt.Create(slant: Slant, sweep: 90f));
            Program.Check(Math.Abs(byArt.Width - leaning.Width) < 1e-5f && Math.Abs(byArt.Height - leaning.Height) < 1e-5f, "the dressing's lean is the layout's");

            // On a board: an italic arc is as wide as its layout (not padded twice), a straight italic text is padded by the lean; the editor's picture has the board's shape in both.
            GuideTemplate t = TextBoxes.Template;
            SignInstance arc = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), t.Parse("style=italic;curve=arch;sweep=120").With("text", "斜体路牌").ToString());
            SignInstance flat = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), t.Parse("style=italic").With("text", "斜体路牌").ToString());
            Program.Near(arc.Board.Main.W, arc.Board.Text.Layout.Width, 1e-5f, "an italic arc's box is its layout's");
            Program.Near(flat.Board.Main.W, flat.Board.Text.Layout.Width + TextStyles.ItalicSlant * flat.Board.Text.Job.LetterHeight, 1e-5f, "an italic straight text's box is padded by the lean");
            foreach (SignInstance instance in new[] { arc, flat })
            {
                TextPlanResult plan = TextPlans.Get(instance.Board.Text.Job);
                byte[] rgba = TextFacePreview.Raster(instance.Board.Text, plan, new byte[] { 255, 232, 180 }, out int w, out int h);
                Program.Check(rgba != null && Math.Abs((float)w / h - instance.Board.Main.W / instance.Board.Main.H) < 0.03f, "the picture has the board's shape: " + w + " x " + h + " for " + instance.Board.Main.W + " x " + instance.Board.Main.H);
            }
        }

        private static TextPlanner Planner(GlyphCache cache) => new TextPlanner(id => id == TextFonts.DefaultId ? TextLayoutTests.BundledFont() : null, cache);

        private static TextPlanJob Job(string text, TextArt art, float height) =>
            new TextPlanJob(text, TextFonts.DefaultId, height, 0f, 1.15f, TextAlign.Centre, TextPlans.RelativeFlatness(height), art);

        private static void Dressing()
        {
            TextArt arch = TextArt.Create(sweep: 120f), smile = TextArt.Create(sweep: -120f);
            Program.Check(arch.Sweep == 120f && smile.Sweep == -120f && !arch.IsPlain && arch.Key != 0UL && arch.Key != smile.Key && !arch.Equals(smile) && arch.Equals(TextArt.Create(sweep: 120f)), "a sweep makes a dressing of its own, an arch and a smile are two");
            Program.Check(arch.Key != TextArt.Create(sweep: 121f).Key && !arch.Equals(TextArt.Create(sweep: 121f)) && arch.Key != TextArt.Create(true, 0f, 0f, TextEffect.None, 0f, 120f).Key, "and so is every degree, and the fallback");
            Program.Check(!arch.ChangesGlyphs && arch.GlyphVariant == 0, "the glyphs of an arc are the font's own: they are shared with the straight text");
            Program.Check(ReferenceEquals(TextArt.Create(), TextArt.Plain) && TextArt.Plain.IsPlain && TextArt.Plain.Key == 0UL && TextArt.Plain.Sweep == 0f && ReferenceEquals(arch.WithSweep(0f), TextArt.Plain), "the plain dressing is as it was, and a straightened arc is it");
            Program.Check(TextArt.Create(sweep: 5f).Sweep == ArcInfo.MinSweep && TextArt.Create(sweep: 5f).Key == TextArt.Create(sweep: 10f).Key && TextArt.Create(sweep: -999f).Sweep == -ArcInfo.MaxSweep && TextArt.Create(sweep: float.NaN).IsPlain, "the sweep is held to what the layout can bend, so that it is one key");
            TextArt bold = TextStyles.Art(TextStyles.Bold, false);
            Program.Check(bold.WithSweep(90f).Weight == bold.Weight && bold.WithSweep(90f).Sweep == 90f && TextStyles.Art(TextStyles.Neon, true, -90f).Effect == TextEffect.Neon && TextStyles.Art(TextStyles.Neon, true, -90f).Fallback && TextStyles.Art(TextStyles.Plain, false, 0f).IsPlain, "a style keeps itself on an arc");
            Program.Check(TextStyles.SignedSweep(TextStyles.Arch, 120f) == 120f && TextStyles.SignedSweep(TextStyles.Arch, -120f) == 120f && TextStyles.SignedSweep(TextStyles.Smile, 120f) == -120f && TextStyles.SignedSweep(TextStyles.Straight, 120f) == 0f && TextStyles.SignedSweep("what", 120f) == 0f, "the signed sweep of a curve");
            TextPlanJob straight = Job("文字灯箱", TextArt.Plain, 0.3f), curved = Job("文字灯箱", arch, 0.3f), other = Job("文字灯箱", TextArt.Create(sweep: 100f), 0.3f);
            Program.Check(straight.Key != curved.Key && curved.Key != other.Key && !curved.Equals(other) && curved.Equals(Job("文字灯箱", TextArt.Create(sweep: 120f), 0.3f)), "the job's key has the sweep in it");
            Program.Check(straight.Key == Job("文字灯箱", TextArt.Plain, 0.3f).Key && straight.Key == new TextPlanJob("文字灯箱", TextFonts.DefaultId, 0.3f, 0f, 1.15f, TextAlign.Centre, TextPlans.RelativeFlatness(0.3f)).Key, "a straight job is the job it was");
        }

        private static void Turned()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            int bucket = TextGlyphs.Bucket(TextPlans.RelativeFlatness(0.3f));
            GlyphMesh mesh = TextGlyphs.Build(font, '永', bucket);
            const float Mpe = 0.4f, X = 0.3f, Y = 0.2f, Back = 1.4f, Front = 1.5f, Angle = 0.7f;
            var plainFace = new MeshBuilder();
            var plainBack = new MeshBuilder();
            mesh.AddTo(plainFace, plainBack, Mpe, X, Y, Back, Front, true);
            var zeroFace = new MeshBuilder();
            var zeroBack = new MeshBuilder();
            mesh.AddTo(zeroFace, zeroBack, Mpe, X, Y, Back, Front, true, 0f);
            Program.Check(plainFace.Positions.SequenceEqual(zeroFace.Positions) && plainBack.Positions.SequenceEqual(zeroBack.Positions) && plainBack.Indices.SequenceEqual(zeroBack.Indices), "an angle of 0 is a letter as it was");

            var face = new MeshBuilder();
            var back = new MeshBuilder();
            mesh.AddTo(face, back, Mpe, X, Y, Back, Front, true, Angle);
            Program.Check(face.VertexCount == plainFace.VertexCount && back.VertexCount == plainBack.VertexCount && back.TriangleCount == plainBack.TriangleCount, "turned, the same vertices and triangles");
            float c = (float)Math.Cos(Angle), s = (float)Math.Sin(Angle);
            bool moved = true;
            for (int i = 0; i < mesh.Points.Length; i++)
            {
                Point2 p = mesh.Points[i];
                float px = (c * p.X - s * p.Y) * Mpe, py = (s * p.X + c * p.Y) * Mpe;
                moved &= Math.Abs(face.Positions[i].X + (X + px)) < 1e-5f && Math.Abs(face.Positions[i].Y - (Y + py)) < 1e-5f && Math.Abs(face.Positions[i].Z - Front) < 1e-6f && Math.Abs(back.Positions[i].Z - Back) < 1e-6f;
            }

            Program.Check(moved, "every point of the face is the letter's point turned about its origin, moved, and mirrored in x as a board's are");
            Program.Check(face.Normals.All(n => n.Z == 1f && n.X == 0f && n.Y == 0f) && face.Indices.Count == plainFace.Indices.Count, "and it faces the viewer");
            // The rim is built on the turned loops.
            List<List<Point2>> loops = mesh.Placed(Mpe, X, Y, Angle);
            Point2 first = mesh.Loops[0][0];
            Program.Check(Math.Abs(loops[0][0].X - (X + (c * first.X - s * first.Y) * Mpe)) < 1e-5f && Math.Abs(loops[0][0].Y - (Y + (s * first.X + c * first.Y) * Mpe)) < 1e-5f && mesh.Placed(Mpe, X, Y)[0][0].X == X + first.X * Mpe, "the placed loops are turned too");

            float RimReach(MeshBuilder m, bool x) => x ? m.Positions.Max(p => p.X) - m.Positions.Min(p => p.X) : m.Positions.Max(p => p.Y) - m.Positions.Min(p => p.Y);
            Program.Check(Math.Abs(RimReach(back, true) - RimReach(face, true)) < 1e-4f && Math.Abs(RimReach(back, false) - RimReach(face, false)) < 1e-4f, "the walls of the return stand on the turned loops: the back's box is the face's");

            // A tube turned: every vertex and normal is the unturned one turned about the glyph's origin; closed, facing out.
            GlyphMesh neon = GlyphStyles.Neon(TextGlyphs.Build(font, '国', bucket), TextStyles.NeonRadius, TextGlyphs.BucketFlatness(bucket), TextStyles.MinBand(0.3f, TextStyles.Art(TextStyles.Neon, false)));
            Program.Check(neon.Tube != null, "a glyph with a tube to turn");
            foreach (float turn in new[] { Angle, 2.5f, (float)Math.PI, -2f })
            {
                var flat = new MeshBuilder();
                neon.AddTo(flat, null, Mpe, 0f, 0f, 0f, Front, true);
                var turned = new MeshBuilder();
                neon.AddTo(turned, null, Mpe, 0f, 0f, 0f, Front, true, turn);
                float tc = (float)Math.Cos(turn), ts = (float)Math.Sin(turn);
                Program.Check(flat.VertexCount == turned.VertexCount && flat.Indices.SequenceEqual(turned.Indices), "a turned tube has the same triangles at " + turn);
                bool same = true;
                for (int i = 0; i < flat.VertexCount; i++)
                {
                    Vec3 a = flat.Positions[i], b = turned.Positions[i], na = flat.Normals[i], nb = turned.Normals[i];
                    // The vertex is at -x: the viewer's point is (-a.X, a.Y).
                    float vx = -a.X, vy = a.Y, nx = -na.X, ny = na.Y;
                    same &= Math.Abs(-b.X - (tc * vx - ts * vy)) < 1e-5f && Math.Abs(b.Y - (ts * vx + tc * vy)) < 1e-5f && Math.Abs(b.Z - a.Z) < 1e-6f
                        && Math.Abs(-nb.X - (tc * nx - ts * ny)) < 1e-5f && Math.Abs(nb.Y - (ts * nx + tc * ny)) < 1e-5f && Math.Abs(nb.Z - na.Z) < 1e-6f;
                }

                Program.Check(same, "every vertex and normal of the tube is the unturned one turned by " + turn);
                bool facing = true;
                for (int i = 0; i < turned.Indices.Count; i += 3)
                {
                    Vec3 geometric = MeshBuilder.Cross(turned.Positions[turned.Indices[i + 1]] - turned.Positions[turned.Indices[i]], turned.Positions[turned.Indices[i + 2]] - turned.Positions[turned.Indices[i]]);
                    Vec3 mean = turned.Normals[turned.Indices[i]] + turned.Normals[turned.Indices[i + 1]] + turned.Normals[turned.Indices[i + 2]];
                    facing &= Vec3.Dot(geometric, mean) > 0f;
                }

                Program.Check(facing, "and every triangle of the tube turned by " + turn + " faces the way its normals do");
            }
        }

        private static float DistanceToPolyline(Point2 p, IEnumerable<FrameSegment> segments)
        {
            float best = float.MaxValue;
            foreach (FrameSegment s in segments)
            {
                float dx = s.B.X - s.A.X, dy = s.B.Y - s.A.Y, l2 = dx * dx + dy * dy;
                float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - s.A.X) * dx + (p.Y - s.A.Y) * dy) / l2));
                best = Math.Min(best, Distance(p, new Point2(s.A.X + dx * t, s.A.Y + dy * t)));
            }

            return best;
        }

        private static void Frame()
        {
            // The real font: the frame planned on the straight line, carried to the arc.
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            TextPlanner planner = Planner(cache);
            const string Text = "永国大小招牌\n口人一目路";
            foreach (float height in new[] { 0.2f, 0.5f, 1.2f })
            {
                foreach (int sweep in new[] { 60, 120, -120, 200 })
                {
                    TextPlanResult line = planner.Plan(Job(Text, TextArt.Plain, height)), plan = planner.Plan(Job(Text, TextArt.Create(sweep: sweep), height));
                    string at = $"{height} m, {sweep} degrees";
                    FramePlan f = plan.Frame, s = line.Frame;
                    Program.Check(plan.Layout.Arc != null && plan.Meshes.Zip(line.Meshes, ReferenceEquals).All(x => x), $"{at}: the glyphs are the straight text's own");
                    Program.Check(f.Islands.Count == s.Islands.Count && f.Studs.Count == s.Studs.Count && f.Ties.Count == s.Ties.Count && f.Bridges.Count == s.Bridges.Count && f.BridgedIslands == s.BridgedIslands && f.EffectiveArea == s.EffectiveArea && f.Rails.Count >= s.Rails.Count, $"{at}: the same islands, studs, ties and rods");
                    Program.Check(f.Studs.All(st => f.Islands[st.Island].Contains(st.At.X, st.At.Y)), $"{at}: every stud is inside its letter, where the letter is drawn");

                    // The islands are the letters as drawn: the glyph's loops, turned and placed.
                    bool drawn = true;
                    foreach (FrameIsland island in f.Islands)
                    {
                        PlacedGlyph g = plan.Layout.Glyphs[island.Glyph];
                        List<List<Point2>> loops = plan.Meshes[island.Glyph].Placed(plan.Layout.MetresPerEm, g.X, g.Y, g.Angle);
                        foreach (FrameIsland.Piece piece in island.Pieces)
                            drawn &= loops.Any(l => l.Count == piece.Outer.Count && l.Zip(piece.Outer, (a, b) => Distance(a, b) < 1e-4f).All(x => x));
                    }

                    Program.Check(drawn, $"{at}: the islands are the loops of the letters as they are drawn");

                    // The rails: a chain for each rail, from the tie's end to the tie's end, through the studs they hold.
                    int starts = f.Rails.Count(r => !f.Rails.Any(o => Distance(o.B, r.A) < 1e-5f)), finishes = f.Rails.Count(r => !f.Rails.Any(o => Distance(o.A, r.B) < 1e-5f));
                    Program.Check(starts == 2 * s.RailHeights.Count && finishes == starts, $"{at}: a chain of pieces for each of the two rails of a line: {starts}, {finishes}");
                    bool through = true;
                    for (int i = 0; i < f.Studs.Count; i++)
                    {
                        FrameStud stud = f.Studs[i];
                        (_, float low, float high) = s.RailHeights.First(h => h.Line == stud.Line);
                        if (Math.Abs(s.Studs[i].At.Y - low) < 1e-4f || Math.Abs(s.Studs[i].At.Y - high) < 1e-4f)
                            through &= f.Rails.Any(r => Distance(r.A, stud.At) < 1e-5f || Distance(r.B, stud.At) < 1e-5f);
                    }

                    Program.Check(through, $"{at}: a rail passes through every stud it holds");
                    Program.Check(f.Ties.Select((t, i) => Distance(t.A, t.B) > 0.05f && Distance(t.A, plan.Layout.Arc.Bend(s.Ties[i].A.X, s.Ties[i].A.Y)) < 1e-6f && Distance(t.B, plan.Layout.Arc.Bend(s.Ties[i].B.X, s.Ties[i].B.Y)) < 1e-6f).All(x => x), $"{at}: a tie runs from the bent end of its straight one to the other");
                    Program.Check(f.Ties.All(t => f.Rails.Any(r => Distance(r.A, t.A) < 1e-5f || Distance(r.B, t.A) < 1e-5f) && f.Rails.Any(r => Distance(r.A, t.B) < 1e-5f || Distance(r.B, t.B) < 1e-5f)), $"{at}: the ties end on rails");
                    Program.Check(f.Rails.All(r => r.Length < 0.07f * (plan.Layout.Arc.Radius + 2f * height) + 0.05f && r.Length > 0f), $"{at}: a rail's pieces are cut short enough to follow the arc");
                    Program.Check(f.FrameLength > 0f && Math.Abs(f.FrameLength - (f.Rails.Sum(r => r.Length) + f.Ties.Sum(t => t.Length) + f.Bridges.Sum(b => b.Length))) < 1e-3f && f.MassKg > 0f, $"{at}: the length and the mass are worked out on the arc");
                }
            }

            // Rods from a stud to a rail: letters with three marks one over another, which two rails cannot all cross.
            TextLayoutResult straight = Straight("AAAAAAAAAA");
            TextLayoutResult bent = ArcTextLayout.Bend(straight, 90f, 1);
            GlyphMesh marks = TextGlyphs.FromLoops(new List<List<Point2>>
            {
                new List<Point2> { new Point2(0.1f, 0f), new Point2(0.3f, 0f), new Point2(0.3f, 0.1f), new Point2(0.1f, 0.1f) },
                new List<Point2> { new Point2(0.1f, 0.35f), new Point2(0.3f, 0.35f), new Point2(0.3f, 0.45f), new Point2(0.1f, 0.45f) },
                new List<Point2> { new Point2(0.1f, 0.7f), new Point2(0.3f, 0.7f), new Point2(0.3f, 0.8f), new Point2(0.1f, 0.8f) }
            }, 0.6f);
            FrameStyle style = FrameStyle.For(0.5f);
            FramePlan flat = LetterFrame.Plan(straight, c => marks, style);
            FramePlan arch = ArcTextLayout.BendFrame(flat, bent.Arc, style);
            Program.Check(flat.Bridges.Count > 0 && arch.Bridges.Count == flat.Bridges.Count && arch.BridgedIslands == flat.BridgedIslands, "marks that two rails cannot all cross are held by rods: " + flat.Bridges.Count);
            for (int i = 0; i < arch.Bridges.Count; i++)
            {
                FrameSegment rod = arch.Bridges[i];
                Program.Check(arch.Studs.Any(st => Distance(st.At, rod.A) < 1e-6f) && DistanceToPolyline(rod.B, arch.Rails) < style.RailSize * 0.5f, $"rod {i}: from a stud to a rail it ends on: " + DistanceToPolyline(rod.B, arch.Rails));
            }

            for (int i = 0; i < flat.Bridges.Count; i++)
            {
                int stud = Enumerable.Range(0, flat.Studs.Count).First(k => flat.Studs[k].At.X == flat.Bridges[i].A.X && flat.Studs[k].At.Y == flat.Bridges[i].A.Y);
                Program.Check(Distance(arch.Bridges[i].A, arch.Studs[stud].At) < 1e-6f && Distance(arch.Bridges[i].B, bent.Arc.Bend(flat.Bridges[i].B.X, flat.Bridges[i].B.Y)) < 1e-6f, $"rod {i} starts at its own stud and ends where the rail is bent to");
            }

            Program.Near(arch.MassKg, LetterFrame.EstimatedMass(arch, style), 1e-6f, "the mass is worked out from the bent frame");
            Program.Check(arch.Perimeter == flat.Perimeter && arch.EffectiveArea == flat.EffectiveArea && arch.RailHeights.SequenceEqual(flat.RailHeights), "what the bend does not change is kept");
            Program.Check(arch.LongestBridge > 0f && Math.Abs(arch.LongestBridge - arch.Bridges.Max(b => b.Length)) < 1e-6f && arch.LongBridges == arch.Bridges.Count(b => b.Length > style.LongRod), "the longest rod and the long ones are counted on the arc");
            Program.Check(arch.InkWidth > 0f && arch.InkHeight > 0f && arch.FillFactor > 0f && arch.FillFactor <= 1.001f, "the ink's box is the bent letters'");
            FramePlan none = ArcTextLayout.BendFrame(new FramePlan(), bent.Arc, style);
            Program.Check(none.Studs.Count == 0 && none.Rails.Count == 0 && none.MassKg == 0f && none.InkWidth == 0f, "no letters, no frame");
        }

        private static SignParts BuildOne(SignInstance instance, SupportDetail detail = SupportDetail.Full)
        {
            SupportDef support = SupportPlanner.Auto(instance.Def);
            return SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, support, 0f, detail, default);
        }

        private static void Built()
        {
            GuideTemplate t = TextBoxes.Template;
            Program.Check(t.FieldOf("curve").Choices.Select(c => c.Value).SequenceEqual(new[] { "straight", "arch", "smile" }) && t.FieldOf("curve").Default == "straight", "the curves listed, a straight line first");
            Program.Check(!t.FieldOf("sweep").Visible(t.Parse("")) && t.FieldOf("sweep").Visible(t.Parse("curve=arch")) && t.FieldOf("sweep").Visible(t.Parse("curve=smile")) && !t.FieldOf("sweep").Visible(t.Parse("curve=straight")), "the sweep is asked for only on a curve");
            Program.Check(TextBoxes.JobOf(t.Parse(""), out _).Art.IsPlain && TextBoxes.JobOf(t.Parse("curve=straight;sweep=200"), out _).Art.IsPlain, "a default text is the plain one it was, whatever the sweep field holds");
            Program.Check(TextBoxes.JobOf(t.Parse("curve=arch"), out _).Art.Sweep == 120f && TextBoxes.JobOf(t.Parse("curve=smile;sweep=90"), out _).Art.Sweep == -90f && TextBoxes.JobOf(t.Parse("curve=arch;sweep=5"), out _).Art.Sweep == ArcInfo.MinSweep && TextBoxes.JobOf(t.Parse("curve=arch;sweep=999"), out _).Art.Sweep == ArcInfo.MaxSweep, "the job's sweep: 120 by default, signed, held between 10 and 300");
            Program.Check(TextBoxes.JobOf(t.Parse("style=neon;curve=arch;sweep=100"), out _).Art.Effect == TextEffect.Neon && TextBoxes.JobOf(t.Parse("style=neon;curve=arch;sweep=100"), out _).Art.Sweep == 100f, "a style and a curve go together");

            // The board: the box is the arc's, the letters are inside it, the plan is the arc's.
            TextPlans.Clear();
            string spec = t.Parse("curve=arch;sweep=120").With("text", "文字灯箱招牌路牌").ToString();
            SignInstance instance = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), spec);
            TextBoard text = instance.Board.Text;
            GuidePart part = instance.Board.Main;
            Program.Check(text.Layout.Arc != null && Math.Abs(part.W - text.Layout.Width) < 1e-5f && Math.Abs(part.H - text.Layout.Height) < 1e-5f && part.H > text.Layout.LetterHeight * 1.5f, "the board's box is the arc's: " + part.W + " x " + part.H);
            SignParts parts = BuildOne(instance);
            MeshBuilder glow = parts.Solids[(int)text.Glow];
            TextPlanResult plan = TextPlans.Peek(text.Job);
            Program.Check(!parts.Provisional && plan != null && plan.Layout.Arc != null && glow != null && glow.VertexCount > 100, "built: the letters are the plan's");
            float minX = glow.Positions.Min(p => p.X), maxX = glow.Positions.Max(p => p.X), minY = glow.Positions.Min(p => p.Y), maxY = glow.Positions.Max(p => p.Y);
            Program.Check(maxX - minX <= part.W + 1e-3f && maxY - minY <= part.H + 1e-3f && maxX - minX > part.W * 0.8f && maxY - minY > part.H * 0.6f, $"the letters fill the box and no more: {maxX - minX} x {maxY - minY} in {part.W} x {part.H}");
            Program.Check(parts.Metal.VertexCount > 100, "and the frame is built");

            // The editor's notes for a bend that was limited, before any plan is there.
            TextPlans.Clear();
            string limitedSpec = t.Parse("curve=arch;sweep=300").With("text", "文字").ToString();
            List<string> notes = TextBoxes.Hints(limitedSpec, true), notesEn = TextBoxes.Hints(limitedSpec, false);
            Program.Check(notes.Count == 1 && notes[0].Contains("46°") && notesEn.Count == 1 && notesEn[0].Contains("46°"), "the note says how far it was bent, before the plan: " + string.Join(" | ", notes));
            TextPlans.Get(TextBoxes.JobOf(t.Parse(limitedSpec), out _));
            Program.Check(TextBoxes.Hints(limitedSpec, false).Count == 1, "and after it");
            TextPlans.Clear();
            SignInstance again = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), spec);
            parts = BuildOne(again);
            glow = parts.Solids[(int)again.Board.Text.Glow];

            // Far: a lit ring for each line, round the arc, inside the box.
            SignParts far = BuildOne(SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), spec), SupportDetail.Reduced);
            MeshBuilder bars = far.Solids[(int)text.Glow];
            Program.Check(bars != null && bars.VertexCount > 0 && bars.VertexCount < 1000, "the far tile has a lit ring of a few pieces: " + bars?.VertexCount);
            float barsW = bars.Positions.Max(p => p.X) - bars.Positions.Min(p => p.X), barsH = bars.Positions.Max(p => p.Y) - bars.Positions.Min(p => p.Y);
            Program.Check(barsW <= part.W + 1e-3f && barsH <= part.H + 1e-3f && barsH > text.Layout.LetterHeight, $"round the arc, not straight across: {barsW} x {barsH}");

            // Neon on an arc.
            SignInstance neon = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), t.Parse("style=neon;curve=smile;sweep=100").With("text", "口永目国人大").ToString());
            SignParts tubes = BuildOne(neon);
            Program.Check(!tubes.Provisional && tubes.Solids[(int)neon.Board.Text.Glow] != null && tubes.Solids[(int)neon.Board.Text.Glow].VertexCount > 200, "a neon smile is built");

            // The same text, straight and plain, builds as it did: nothing of the arc is in it.
            SignInstance plain = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), t.Parse("").With("text", "文字灯箱").ToString());
            Program.Check(plain.Board.Text.Layout.Arc == null && plain.Board.Text.Job.Art.IsPlain && TextPlans.Get(plain.Board.Text.Job).Layout.Arc == null, "a straight text has no arc");
        }

        /// <summary>The letters as the editor shows them with the rear frame drawn over them (studs red, rails grey, ties white, rods blue), written as PNG files when TS_STYLE_PREVIEW is a folder: for looking at.</summary>
        private static void Pictures()
        {
            string folder = Environment.GetEnvironmentVariable("TS_STYLE_PREVIEW");
            if (string.IsNullOrEmpty(folder))
                return;
            Directory.CreateDirectory(folder);
            SignDef def = SignCatalog.ById(TextBoxes.TemplateId);
            foreach ((string name, string spec, string text) in new[]
            {
                ("arc-1-arch120", "curve=arch;sweep=120", "永远发光路牌ABC"), ("arc-2-smile90", "curve=smile;sweep=90", "永远发光路牌ABC"), ("arc-3-two-lines", "curve=arch;sweep=150", "迎宾大道欢迎您\\nWELCOME HOME"),
                ("arc-4-neon", "style=neon;curve=arch;sweep=160", "夜市开放 OPEN"), ("arc-5-bolditalic", "style=bolditalic;curve=smile;sweep=120", "Hello World"), ("arc-6-outline", "style=outline;curve=arch;sweep=200", "CDL STREET"),
                ("arc-7-limited", "curve=arch;sweep=250", "永远"), ("arc-8-straight", "curve=straight", "永远发光路牌ABC")
            })
            {
                SignInstance instance = SignInstances.Of(def, TextBoxes.Template.Parse(spec).With("text", text).With("h", "0.5").ToString());
                TextBoard board = instance.Board.Text;
                TextPlanResult plan = TextPlans.Get(board.Job);
                byte[] rgba = TextFacePreview.Raster(board, plan, new byte[] { 255, 232, 180 }, out int w, out int h);
                Program.Check(rgba != null && w > 16 && h > 16, name + ": a picture is made");
                float boardW = Math.Max(TextBoxes.MinBox, board.Layout.Width), boardH = Math.Max(TextBoxes.MinBox, board.Layout.Height), scale = TextFacePreview.LongSide / Math.Max(boardW, boardH);
                Action<Point2, Point2, byte[]> line = (a, b, colour) => Draw(rgba, w, h, (a.X + boardW * 0.5f) * scale, (a.Y + boardH * 0.5f) * scale, (b.X + boardW * 0.5f) * scale, (b.Y + boardH * 0.5f) * scale, colour);
                foreach (FrameSegment s in plan.Frame.Rails)
                    line(s.A, s.B, new byte[] { 150, 150, 160 });
                foreach (FrameSegment s in plan.Frame.Ties)
                    line(s.A, s.B, new byte[] { 255, 255, 255 });
                foreach (FrameSegment s in plan.Frame.Bridges)
                    line(s.A, s.B, new byte[] { 80, 140, 255 });
                foreach (FrameStud stud in plan.Frame.Studs)
                    line(new Point2(stud.At.X - 0.012f, stud.At.Y), new Point2(stud.At.X + 0.012f, stud.At.Y), new byte[] { 255, 60, 60 });
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), Png.Encode(rgba, w, h));
            }
        }

        private static void Draw(byte[] rgba, int width, int height, float x0, float y0, float x1, float y1, byte[] colour)
        {
            int steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))) + 1;
            for (int i = 0; i <= steps; i++)
            {
                int x = (int)Math.Round(x0 + (x1 - x0) * i / steps), y = (int)Math.Round(y0 + (y1 - y0) * i / steps);
                if (x < 0 || y < 0 || x >= width || y >= height)
                    continue;
                for (int c = 0; c < 3; c++)
                    rgba[(y * width + x) * 4 + c] = colour[c];
            }
        }

        private static void Picture()
        {
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            TextPlanner planner = Planner(cache);
            TextPlanResult plan = planner.Plan(Job("永永永永永永永永", TextArt.Create(sweep: 120f), 0.3f));
            var board = new TextBoard { Job = plan.Job, Layout = plan.Layout };
            byte[] rgba = TextFacePreview.Raster(board, plan, new byte[] { 255, 220, 120 }, out int width, out int height);
            Program.Check(rgba != null && width > 16 && height > 16 && Math.Abs((float)width / height - plan.Layout.Width / plan.Layout.Height) < 0.05f, "the picture is the arc's box: " + width + " x " + height);
            bool Lit(int x, int y) => rgba[(y * width + x) * 4] > 100;
            // Rows are from the bottom: the arch has its middle letters at the top and its ends low on the sides, and nothing under its middle.
            int topLit = 0, bottomMiddle = 0, sideLow = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!Lit(x, y))
                        continue;
                    if (y >= height * 0.8f && Math.Abs(x - width * 0.5f) < width * 0.15f)
                        topLit++;
                    if (y < height * 0.15f && Math.Abs(x - width * 0.5f) < width * 0.2f)
                        bottomMiddle++;
                    if (y < height * 0.5f && (x < width * 0.2f || x > width * 0.8f))
                        sideLow++;
                }
            }

            // Every lit pixel is inside a letter's cell as it was turned: a picture of letters that were not turned has ink outside them at the ends.
            float boardW = Math.Max(TextBoxes.MinBox, plan.Layout.Width), boardH = Math.Max(TextBoxes.MinBox, plan.Layout.Height), scale = TextFacePreview.LongSide / Math.Max(boardW, boardH), slack = 1.5f / scale;
            int outside = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!Lit(x, y))
                        continue;
                    float bx = (x + 0.5f) / scale - boardW * 0.5f, by = (y + 0.5f) / scale - boardH * 0.5f;
                    bool inside = false;
                    foreach (PlacedGlyph g in plan.Layout.Glyphs)
                    {
                        float c = (float)Math.Cos(-g.Angle), s = (float)Math.Sin(-g.Angle), dx = bx - g.X, dy = by - g.Y, lx = c * dx - s * dy, ly = s * dx + c * dy;
                        inside |= lx >= -slack && lx <= g.Advance + slack && ly >= plan.Layout.EmBottomMetres - slack && ly <= plan.Layout.EmTopMetres + slack;
                    }

                    if (!inside)
                        outside++;
                }
            }

            Program.Check(outside == 0, "every lit pixel of the picture is in a letter's cell as it was turned: " + outside + " outside");
            Program.Check(topLit > 50 && bottomMiddle == 0 && sideLow > 50, $"an arch: lit at the top in the middle ({topLit}), dark under it ({bottomMiddle}), lit at the low ends ({sideLow})");
        }
    }
}
