using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The layout of a typed text (TextLayout) and the flatness-driven cut of a font's quadratic curves (FontOutlines.QuadraticSegments, TryGlyph with a flatness).</summary>
    internal static class TextLayoutTests
    {
        /// <summary>A face that needs no font: an em of 1000 from -200 to 800, 600 for every character, 800 for the Chinese ones (laid out as 1000), and nothing for the snowman.</summary>
        internal static readonly TextFace Fake = new TextFace(1000f, -200f, 800f, c => c == 0x2603 ? -1f : TextLayout.IsWide(c) ? 800f : c == ' ' ? 300f : 600f);

        private const float Eps = 1e-5f;

        /// <summary>The bundled font, read once; null (with a message) when the file is not found.</summary>
        private static FontOutlines s_Font;
        private static bool s_Tried;

        internal static FontOutlines BundledFont()
        {
            if (s_Tried)
                return s_Font;
            s_Tried = true;
            string file = FontLetterTests.RepoFile("Fonts", SignFonts.BundledCjk);
            if (file == null)
            {
                Program.Check(false, "the bundled font is found next to the repository (the text tests need it)");
                return null;
            }

            s_Font = new FontOutlines(File.ReadAllBytes(file));
            Program.Check(s_Font.Ok, "the bundled font is read");
            return s_Font.Ok ? s_Font : (s_Font = null);
        }

        public static void RunAll()
        {
            Program.Run("text: advances, positions and the box", Positions);
            Program.Run("text: alignment, spacing and the lines' pitch", AlignmentAndSpacing);
            Program.Run("text: lines, limits, Chinese characters, missing characters, surrogate pairs", LinesAndLimits);
            Program.Run("flatness: the segments of a quadratic and the bound they keep", Segments);
            Program.Run("flatness: the cut of a glyph (the fixed cut is unchanged, the adaptive one stays within the flatness)", GlyphCut);
        }

        private static TextStyle Style(float height, TextAlign align = TextAlign.Left, float spacing = 0f, float lines = 1.15f) =>
            new TextStyle { LetterHeight = height, LetterSpacing = spacing, LineSpacing = lines, Align = align };

        private static void Positions()
        {
            // 0.5 m letters on an em box of 1000 units: 0.0005 m a unit, an em of 0.5 m; two letters of 600 units are 0.3 m each.
            TextLayoutResult r = TextLayout.Layout("ab", Style(0.5f), Fake);
            Program.Near(r.Scale, 0.0005f, Eps, "metres a font unit");
            Program.Near(r.MetresPerEm, 0.5f, Eps, "metres an em");
            Program.Check(r.Lines == 1 && r.Glyphs.Count == 2 && !r.Truncated && r.Missing.Count == 0, "one line of two letters");
            Program.Near(r.Width, 0.6f, Eps, "the box is the advances' sum");
            Program.Near(r.Height, 0.5f, Eps, "one line is a letter high");
            Program.Near(r.Glyphs[0].X, -0.3f, Eps, "the box is centred on 0: the first letter starts at minus half the width");
            Program.Near(r.Glyphs[1].X, 0f, Eps, "the second starts an advance on");
            // The baseline: the top of the box is 0.25, the em box's top is 800 units (0.4 m) above the baseline.
            Program.Near(r.Glyphs[0].Y, -0.15f, Eps, "the baseline is below the middle by what the em box has above it less the half height");
            Program.Check(r.Glyphs[0].Codepoint == 'a' && r.Glyphs[1].Codepoint == 'b' && r.Glyphs[0].Line == 0, "the characters and the line");

            TextLayoutResult empty = TextLayout.Layout("", Style(0.5f), Fake);
            Program.Check(empty.Glyphs.Count == 0 && empty.Width == 0f && empty.Height == 0f && empty.Lines == 0, "no text, no box");
            Program.Check(TextLayout.Layout(null, Style(0.5f), Fake).Glyphs.Count == 0, "a null text is no text");
        }

        private static void AlignmentAndSpacing()
        {
            // Lines of four and two letters: 1.2 m and 0.6 m wide.
            TextLayoutResult left = TextLayout.Layout("abcd\nab", Style(0.5f, TextAlign.Left), Fake);
            TextLayoutResult centre = TextLayout.Layout("abcd\nab", Style(0.5f, TextAlign.Centre), Fake);
            TextLayoutResult right = TextLayout.Layout("abcd\nab", Style(0.5f, TextAlign.Right), Fake);
            Program.Near(left.Width, 1.2f, Eps, "the box is the widest line");
            Program.Near(left.LineWidths[1], 0.6f, Eps, "the short line's width");
            Program.Near(left.Glyphs[4].X, -0.6f, Eps, "left: the short line starts where the long one does");
            Program.Near(centre.Glyphs[4].X, -0.3f, Eps, "centre: half the difference in");
            Program.Near(right.Glyphs[4].X, 0f, Eps, "right: all the difference in");
            Program.Near(right.Glyphs[5].X + 0.3f, 0.6f, Eps, "right: the last letter ends at the box's right edge");

            // Spacing: a tenth of the letter height (0.05 m) between letters, not after the last.
            TextLayoutResult spaced = TextLayout.Layout("ab", Style(0.5f, TextAlign.Left, 0.1f), Fake);
            Program.Near(spaced.Width, 0.65f, Eps, "the gap is between letters only");
            Program.Near(spaced.Glyphs[1].X - spaced.Glyphs[0].X, 0.35f, Eps, "an advance and the gap");
            Program.Near(TextLayout.Layout("ab", Style(0.5f, TextAlign.Left, -1f), Fake).Width, 0.6f, Eps, "a negative spacing is none");

            // Lines: the pitch is 1.15 letter heights; the box is the pitch once and a letter.
            TextLayoutResult two = TextLayout.Layout("a\nb", Style(0.5f), Fake);
            Program.Near(two.Height, 0.575f + 0.5f, Eps, "two lines' box");
            Program.Near(two.Glyphs[0].Y - two.Glyphs[1].Y, 0.575f, Eps, "the pitch");
            Program.Near(two.Glyphs[0].Y, 0.5375f - 0.4f, Eps, "the first baseline: the box's top less the em box's ascent");
            Program.Near(TextLayout.Layout("a\nb", Style(0.5f, TextAlign.Left, 0f, 2f), Fake).Glyphs[0].Y - TextLayout.Layout("a\nb", Style(0.5f, TextAlign.Left, 0f, 2f), Fake).Glyphs[1].Y, 1f, Eps, "a line spacing of 2");
        }

        private static void LinesAndLimits()
        {
            Program.Check(TextLayout.Layout("a\nb\nc", Style(0.5f), Fake).Lines == 3, "three lines");
            Program.Check(!TextLayout.Layout("a\nb\nc\n", Style(0.5f), Fake).Truncated, "a trailing newline is not a fourth line");
            TextLayoutResult four = TextLayout.Layout("a\nb\nc\nd", Style(0.5f), Fake);
            Program.Check(four.Lines == 3 && four.Truncated && four.Glyphs.TrueForAll(g => g.Codepoint != 'd'), "a fourth line is left out and says so");
            Program.Check(TextLayout.Layout("a\r\nb", Style(0.5f), Fake).Lines == 2, "a carriage return is dropped");
            Program.Check(TextLayout.Layout("a\n\nb", Style(0.5f), Fake).Lines == 3, "an empty line between is kept");
            Program.Check(TextLayout.Layout("a\n\n", Style(0.5f), Fake).Lines == 1, "trailing empty lines go");

            TextLayoutResult wide = TextLayout.Layout(new string('x', 15), Style(0.5f), Fake);
            Program.Check(wide.Glyphs.Count == TextLayout.MaxPerLine && wide.Truncated, "a line is cut at " + TextLayout.MaxPerLine + " characters and says so");
            Program.Check(!TextLayout.Layout(new string('x', 12), Style(0.5f), Fake).Truncated, "twelve fit");

            // A Chinese character is an em wide whatever the font's advance (800 here); the others have the font's own.
            TextLayoutResult cjk = TextLayout.Layout("中a", Style(0.5f, TextAlign.Left), Fake);
            Program.Near(cjk.Width, 0.5f + 0.3f, Eps, "full width for 中, the font's 600 for a");
            Program.Check(TextLayout.IsWide(0x4E2D) && TextLayout.IsWide(0xFF21) && TextLayout.IsWide(0x3042) && !TextLayout.IsWide('A') && !TextLayout.IsWide(0xFF71), "which characters are wide");

            // The font has no glyph for the snowman: a blank of half an em, listed once however often it is typed.
            TextLayoutResult missing = TextLayout.Layout("a☃☃b", Style(0.5f, TextAlign.Left), Fake);
            Program.Check(missing.Missing.Count == 1 && missing.Missing[0] == 0x2603 && missing.Glyphs[1].Missing && missing.Glyphs[2].Missing && !missing.Glyphs[0].Missing, "missing characters are listed once and marked");
            Program.Near(missing.Width, 0.3f + 0.25f + 0.25f + 0.3f, Eps, "a missing character takes half an em");
            Program.Near(TextLayout.Layout("中", Style(0.5f), new TextFace(1000f, -200f, 800f, c => -1f)).Width, 0.5f, Eps, "a missing Chinese character takes an em");

            // U+20000 is two UTF-16 characters and one glyph.
            TextLayoutResult pair = TextLayout.Layout("𠀀a", Style(0.5f, TextAlign.Left), Fake);
            Program.Check(pair.Glyphs.Count == 2 && pair.Glyphs[0].Codepoint == 0x20000, "a surrogate pair is one character");
        }

        private static void Segments()
        {
            Program.Check(FontOutlines.QuadraticSegments(0.0, 1.0) == 1, "a straight curve is one segment");
            Program.Check(FontOutlines.QuadraticSegments(100.0, 1.0) == 5, "|d| 100, flatness 1: sqrt(100 / 4) = 5");
            Program.Check(FontOutlines.QuadraticSegments(101.0, 1.0) == 6, "a hair more rounds up");
            Program.Check(FontOutlines.QuadraticSegments(1e6, 0.01) == FontOutlines.MaxCurveSegments, "capped at " + FontOutlines.MaxCurveSegments);
            Program.Check(FontOutlines.QuadraticSegments(100.0, 0.0) == 1 && FontOutlines.QuadraticSegments(100.0, -1.0) == 1 && FontOutlines.QuadraticSegments(double.NaN, 1.0) == 1, "no flatness or no curve: one");

            // The bound: a curve cut at equal t into the segments asked for stays within the flatness of its polyline.
            var random = new Random(75);
            int tested = 0;
            double worst = 0.0;
            for (int i = 0; i < 400; i++)
            {
                double x0 = random.NextDouble() * 1000, y0 = random.NextDouble() * 1000, x1 = random.NextDouble() * 1000, y1 = random.NextDouble() * 1000, x2 = random.NextDouble() * 1000, y2 = random.NextDouble() * 1000;
                double flatness = 0.5 + random.NextDouble() * 20.0;
                double d = Math.Sqrt((x0 - 2 * x1 + x2) * (x0 - 2 * x1 + x2) + (y0 - 2 * y1 + y2) * (y0 - 2 * y1 + y2));
                int n = FontOutlines.QuadraticSegments(d, flatness);
                if (n >= FontOutlines.MaxCurveSegments)
                    continue;
                tested++;
                var poly = new double[(n + 1) * 2];
                for (int s = 0; s <= n; s++)
                {
                    double t = s / (double)n, u = 1 - t;
                    poly[s * 2] = u * u * x0 + 2 * u * t * x1 + t * t * x2;
                    poly[s * 2 + 1] = u * u * y0 + 2 * u * t * y1 + t * t * y2;
                }

                for (int k = 0; k <= 2000; k++)
                {
                    double t = k / 2000.0, u = 1 - t;
                    double px = u * u * x0 + 2 * u * t * x1 + t * t * x2, py = u * u * y0 + 2 * u * t * y1 + t * t * y2;
                    double best = double.MaxValue;
                    for (int s = 0; s < n; s++)
                        best = Math.Min(best, DistanceToSegment(px, py, poly[s * 2], poly[s * 2 + 1], poly[s * 2 + 2], poly[s * 2 + 3]));
                    worst = Math.Max(worst, best / flatness);
                }
            }

            Program.Check(tested >= 150, "enough curves were below the cap to test: " + tested);
            Program.Check(worst <= 1.0 + 1e-9, "the polyline stays within the flatness of the curve: worst share " + worst.ToString("0.000"));
            // The bound is not slack by a lot: one segment fewer than asked for breaks it for some curve.
            bool tight = false;
            for (int i = 0; i < 200 && !tight; i++)
            {
                double dd = 20.0 + random.NextDouble() * 400.0, flatness = 1.0;
                int n = FontOutlines.QuadraticSegments(dd, flatness);
                tight = n > 1 && dd / (4.0 * (n - 1) * (n - 1)) > flatness;
            }

            Program.Check(tight, "one segment fewer than asked for would break the bound");
        }

        private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-18 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((px - ax) * dx + (py - ay) * dy) / l2));
            double ex = ax + dx * t - px, ey = ay + dy * t - py;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        private static void GlyphCut()
        {
            FontOutlines font = BundledFont();
            if (font == null)
                return;
            string sample = "口小永国回路牌灯箱0123456789ABCDEFGabcdefg";
            int found = 0;
            foreach (char c in sample)
            {
                if (!font.TryGlyph(c, out GlyphOutline fixedCut))
                    continue;
                found++;
                // A flatness of 0 is the fixed cut: the same contours, point for point.
                Program.Check(font.TryGlyph(c, 0.0, out GlyphOutline zero) && SameContours(fixedCut, zero) && ReferenceEquals(fixedCut, zero), "flatness 0 is the fixed cut of " + c + ", the font's own cached one");
                // A coarse flatness (3.3 units: 1 mm on a letter of 0.3 m) has fewer points than the fixed 6 a curve, and stays within it of a fine one.
                bool cut = font.TryGlyph(c, 3.3, out GlyphOutline coarse) & font.TryGlyph(c, 0.05, out GlyphOutline fine);
                Program.Check(cut, "the adaptive cuts of " + c);
                if (!cut)
                    continue;
                Program.Check(Points(coarse) <= Points(fixedCut), $"the coarse cut of {c} has no more points than the fixed one ({Points(coarse)} against {Points(fixedCut)})");
                Program.Check(coarse.Contours.Count == fine.Contours.Count && coarse.Advance == fine.Advance, "the same contours and advance whatever the flatness: " + c);
                Program.Check(Deviation(fine, coarse) <= 3.3 + 1e-3, $"the coarse cut of {c} is within 3.3 units of the fine one: {Deviation(fine, coarse):0.00}");
                Program.Check(Points(fine) >= Points(coarse), "a finer cut has at least as many points: " + c);
            }

            Program.Check(found >= 30, "the font has the characters of the sample: " + found);
            Program.Check(!font.TryGlyph(0x2603, 1.0, out _), "no glyph for a character the font lacks, with a flatness too");
            Program.Check(font.TryAdvance('口', out float advance) && font.TryGlyph('口', out GlyphOutline box) && Math.Abs(advance - box.Advance) < 1e-3f && !font.TryAdvance(0x2603, out _), "TryAdvance is the glyph's advance, without reading the outline");
            // Across the sample the adaptive cut at a coarse flatness is smaller in all than the fixed one.
            int adaptive = 0, fixedPoints = 0;
            foreach (char c in sample)
            {
                if (font.TryGlyph(c, out GlyphOutline g) && font.TryGlyph(c, 3.3, out GlyphOutline a))
                {
                    fixedPoints += Points(g);
                    adaptive += Points(a);
                }
            }

            Program.Check(adaptive < fixedPoints, $"over the sample the adaptive cut at 3.3 units has fewer points than 6 a curve: {adaptive} against {fixedPoints}");
        }

        internal static int Points(GlyphOutline g)
        {
            int n = 0;
            foreach (float[] c in g.Contours)
                n += c.Length / 2;
            return n;
        }

        private static bool SameContours(GlyphOutline a, GlyphOutline b)
        {
            if (a.Contours.Count != b.Contours.Count || a.Advance != b.Advance)
                return false;
            for (int i = 0; i < a.Contours.Count; i++)
            {
                if (a.Contours[i].Length != b.Contours[i].Length)
                    return false;
                for (int k = 0; k < a.Contours[i].Length; k++)
                {
                    if (a.Contours[i][k] != b.Contours[i][k])
                        return false;
                }
            }

            return true;
        }

        /// <summary>The largest distance of a point of the fine cut from the same contour of the coarse one (font units).</summary>
        internal static double Deviation(GlyphOutline fine, GlyphOutline coarse)
        {
            double worst = 0.0;
            for (int i = 0; i < fine.Contours.Count; i++)
            {
                float[] f = fine.Contours[i], c = coarse.Contours[i];
                for (int p = 0; p + 1 < f.Length; p += 2)
                {
                    double best = double.MaxValue;
                    for (int q = 0; q + 1 < c.Length; q += 2)
                    {
                        int next = (q + 2) % c.Length;
                        best = Math.Min(best, DistanceToSegment(f[p], f[p + 1], c[q], c[q + 1], c[next], c[next + 1]));
                    }

                    worst = Math.Max(worst, best);
                }
            }

            return worst;
        }
    }
}
