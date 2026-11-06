using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The font outlines (FontOutlines) and the road lettering made from them (MarkingLetters). The bundled traffic sign font (Fonts/STJTBZ_GB18030.ttf, TrueType, 0.12.28) is the one the lettering
    /// is made of; the CFF reader is tested on Noto Sans SC (Tests/Fonts/NotoSansSC-Bold.ttf, SIL OFL), the font of the mod until 0.12.27 and a player's font may be such a one.
    /// </summary>
    internal static class FontLetterTests
    {
        /// <summary>A file of the repository (a path below its root), looked for upward from the tests' folder and in the working folder; null when it is not there.</summary>
        internal static string RepoFile(params string[] parts)
        {
            string tail = Path.Combine(parts);
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, tail);
                if (File.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            string cwd = Path.Combine(Directory.GetCurrentDirectory(), tail);
            return File.Exists(cwd) ? cwd : null;
        }

        /// <summary>The bundled font, read once for all the tests that need it; null when the file is not there.</summary>
        internal static FontOutlines Font { get; private set; }

        internal static string FontFile { get; private set; }

        /// <summary>The font the reading tests are on now (the bundled one, then the CFF one), and its file.</summary>
        private static FontOutlines s_Under;
        private static string s_UnderPath;

        public static void RunAll()
        {
            FontFile = RepoFile("Fonts", SignFonts.BundledCjk);
            string cff = RepoFile("Tests", "Fonts", "NotoSansSC-Bold.ttf");
            Program.Check(FontFile != null, "the bundled font is found next to the repository");
            Program.Check(cff != null, "the CFF test font is found");
            if (FontFile == null)
                return;
            Font = new FontOutlines(File.ReadAllBytes(FontFile));
            foreach ((string label, string path) in new[] { ("TrueType", FontFile), ("CFF", cff) })
            {
                if (path == null)
                    continue;
                s_UnderPath = path;
                s_Under = path == FontFile ? Font : new FontOutlines(File.ReadAllBytes(path));
                Program.Run($"font ({label}): the file is read, the cmap finds the characters", Reading);
                Program.Run($"font ({label}): outlines of digits and Chinese characters are sound", Outlines);
                Program.Run($"font ({label}): bad data gives no glyph and no exception", Damage);
            }

            Program.Run("font (TrueType): the outlines are the glyphs' (their boxes and holes)", TrueTypeShapes);
            Program.Run("lettering: characters are triangulated into boxes", Lettering);
        }

        private static void Reading()
        {
            Program.Check(s_Under.Ok, "the font is read");
            Program.Check(s_Under.UnitsPerEm == 1000, "1000 units to the em: " + s_Under.UnitsPerEm);
            foreach (char c in "0123456789-停公交铁路出租车校快速多乘员人慢学")
                Program.Check(s_Under.TryGlyph(c, out GlyphOutline g) && !g.IsEmpty, "the font has " + c);
            s_Under.TryGlyph(0x0000, out _);
            Program.Check(!s_Under.TryGlyph(0x10FFFF, out _), "a code beyond the font's range has no glyph");
            Program.Check(!s_Under.TryGlyph(-5, out _), "a negative code has no glyph");
        }

        private static float Shoelace(float[] c)
        {
            double sum = 0.0;
            int n = c.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int k = (i + 1) % n;
                sum += (double)c[i * 2] * c[k * 2 + 1] - (double)c[k * 2] * c[i * 2 + 1];
            }

            return (float)(sum * 0.5);
        }

        private static void Outlines()
        {
            // Every digit and the hyphen: inside the em, a plausible height, closed contours of at least three points, finite.
            foreach (char c in "0123456789-")
            {
                s_Under.TryGlyph(c, out GlyphOutline g);
                Program.Check(g.MinX >= -100f && g.MaxX <= 1100f && g.MinY >= -300f && g.MaxY <= 1000f, c + ": inside the em: " + g.MinX + ".." + g.MaxX + ", " + g.MinY + ".." + g.MaxY);
                foreach (float[] contour in g.Contours)
                {
                    Program.Check(contour.Length >= 6 && contour.Length % 2 == 0, c + ": a contour of three points at least");
                    foreach (float v in contour)
                        Program.Check(!float.IsNaN(v) && !float.IsInfinity(v), c + ": finite");
                }
            }

            // Figures have the heights of a figure: 0 to 9 are taller than 580 and shorter than 800 units (cap height 733 in Noto, 600 in the traffic sign font).
            foreach (char c in "0123456789")
            {
                s_Under.TryGlyph(c, out GlyphOutline g);
                float h = g.MaxY - g.MinY;
                Program.Check(h > 580f && h < 800f, c + " is about a cap high: " + h);
            }

            // 0 is a ring: two contours turning opposite ways (the outer and the hole); 8 has three; 1 has one.
            s_Under.TryGlyph('0', out GlyphOutline zero);
            Program.Check(zero.Contours.Count == 2 && Shoelace(zero.Contours[0]) * Shoelace(zero.Contours[1]) < 0f, "0: two contours, the hole turning the other way: " + zero.Contours.Count);
            s_Under.TryGlyph('8', out GlyphOutline eight);
            Program.Check(eight.Contours.Count == 3, "8: three contours: " + eight.Contours.Count);
            s_Under.TryGlyph('1', out GlyphOutline one);
            Program.Check(one.Contours.Count == 1, "1: one contour: " + one.Contours.Count);
            // The ring's hole is smaller than its outer.
            float a0 = Math.Abs(Shoelace(zero.Contours[0])), a1 = Math.Abs(Shoelace(zero.Contours[1]));
            Program.Check(Math.Min(a0, a1) < Math.Max(a0, a1) && Math.Min(a0, a1) > 0.1f * Math.Max(a0, a1), "the hole of 0 is a fraction of the outer: " + a0 + " / " + a1);

            // The hyphen is a bar: wider than tall.
            s_Under.TryGlyph('-', out GlyphOutline hyphen);
            Program.Check((hyphen.MaxX - hyphen.MinX) > 1.5f * (hyphen.MaxY - hyphen.MinY), "the hyphen is a bar (1.7 times as wide as tall in the traffic sign font, 2.6 in Noto)");

            // A Chinese character fills most of the em: 停 is wide and tall; 口 (a box) has an outer and a hole.
            s_Under.TryGlyph('停', out GlyphOutline ting);
            Program.Check(ting.MaxX - ting.MinX > 800f && ting.MaxY - ting.MinY > 800f, "停 fills the em: " + (ting.MaxX - ting.MinX) + " x " + (ting.MaxY - ting.MinY));
            Program.Check(ting.Contours.Count >= 3, "停 has several contours: " + ting.Contours.Count);
            s_Under.TryGlyph('口', out GlyphOutline kou);
            Program.Check(kou.Contours.Count == 2 && Shoelace(kou.Contours[0]) * Shoelace(kou.Contours[1]) < 0f, "口: an outer and a hole: " + kou.Contours.Count);
            s_Under.TryGlyph('一', out GlyphOutline yi);
            Program.Check(yi.Contours.Count == 1 && yi.MaxX - yi.MinX > 700f && yi.MaxY - yi.MinY < 150f, "一 is one flat bar: " + (yi.MaxX - yi.MinX) + " x " + (yi.MaxY - yi.MinY));
            Program.Check(Math.Abs(ting.Advance - 1000f) < 1f, "停 advances one em: " + ting.Advance);

            // The same glyph twice is the same object (cached).
            s_Under.TryGlyph('停', out GlyphOutline again);
            Program.Check(ReferenceEquals(ting, again), "cached");
            // Every character of the markings' words is there and fine.
            foreach (char c in "停让慢公交车快速多乘员人铁路出租校学校注意前方路面状况" )
            {
                if (!s_Under.TryGlyph(c, out GlyphOutline g))
                    continue;
                Program.Check(g.Contours.Count >= 1 && g.MaxX > g.MinX && g.MaxY > g.MinY, c + ": a real outline");
            }
        }

        private static void Damage()
        {
            Program.Check(!new FontOutlines(null).Ok && !new FontOutlines(new byte[0]).Ok && !new FontOutlines(new byte[100]).Ok, "no data, no font");
            var noise = new byte[2000];
            new Random(3).NextBytes(noise);
            Program.Check(!new FontOutlines(noise).Ok, "noise is not a font");
            // The real font cut short: the tables point outside the data.
            byte[] real = File.ReadAllBytes(s_UnderPath);
            var cut = new byte[100000];
            Array.Copy(real, cut, cut.Length);
            var broken = new FontOutlines(cut);
            Program.Check(!broken.Ok || !broken.TryGlyph('停', out _), "a font cut short is refused or has no glyph");
            // The real font with its charstrings damaged: no exception, whatever comes out.
            var damaged = (byte[])real.Clone();
            var rng = new Random(5);
            for (int i = 0; i < 2000; i++)
                damaged[300 + rng.Next(damaged.Length - 400)] = (byte)rng.Next(256);
            var font = new FontOutlines(damaged);
            int found = 0;
            foreach (char c in "0123456789停公交铁路")
            {
                try
                {
                    if (font.TryGlyph(c, out GlyphOutline g) && g != null)
                        found++;
                }
                catch (Exception ex)
                {
                    Program.Check(false, "a damaged font must not throw: " + ex.GetType().Name);
                }
            }

            Program.Check(found >= 0, "damage was survived");
        }

        private static float MeshArea(MeshBuilder mesh)
        {
            double sum = 0.0;
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                sum += MeshBuilder.Cross(b - a, c - a).Length * 0.5;
            }

            return (float)sum;
        }

        private static void TrueTypeShapes()
        {
            // The boxes GDI+ gives these glyphs of the traffic sign font (STJTBZ_GB18030 1.10, units of 1000), read when the reader was written: the reader's are the same within the cutting of the curves.
            var boxes = new Dictionary<char, (float x0, float y0, float x1, float y1)>
            {
                ['A'] = (23f, 0f, 630f, 600f), ['g'] = (66f, -228f, 462f, 457f), ['0'] = (60f, -6f, 563f, 606f)
            };
            foreach (KeyValuePair<char, (float x0, float y0, float x1, float y1)> pair in boxes)
            {
                Program.Check(Font.TryGlyph(pair.Key, out GlyphOutline g), pair.Key + " is there");
                Program.Check(Math.Abs(g.MinX - pair.Value.x0) < 4f && Math.Abs(g.MinY - pair.Value.y0) < 4f && Math.Abs(g.MaxX - pair.Value.x1) < 4f && Math.Abs(g.MaxY - pair.Value.y1) < 4f,
                    $"{pair.Key}: its box ({g.MinX:0},{g.MinY:0})-({g.MaxX:0},{g.MaxY:0})");
            }

            Program.Check(Font.TryGlyph('A', out GlyphOutline a) && a.Contours.Count == 2 && Math.Abs(a.Advance - 654f) < 1f, "A: an outer and its hole, advancing 654");
            Program.Check(Font.TryGlyph('g', out GlyphOutline gl) && gl.Contours.Count == 2, "g: a curved outer and its hole (the quadratic curves cut into segments)");
            // A quadratic curve is cut: an O has many more points than its few control points.
            Program.Check(Font.TryGlyph('O', out GlyphOutline o) && o.Contours.Count == 2 && o.Contours[0].Length / 2 > 20, "O: two round contours of many points");
            // A character that is a composite of others in some fonts reads whole (the TrueType reader follows components).
            int composites = 0;
            foreach (char c in "ÀÉÜçñ")
            {
                if (Font.TryGlyph(c, out GlyphOutline accented) && accented.Contours.Count >= 2)
                    composites++;
            }

            Program.Check(composites >= 3, "accented letters (composites in most fonts) have the letter and the accent: " + composites);
            Program.Check(new FontOutlines(File.ReadAllBytes(FontFile)).TryGlyph('停', out GlyphOutline ting) && ting.Contours.Count >= 3, "a second reading of the file is the same");
        }

        private static void Lettering()
        {
            MarkingLetters.Font = Font;
            // 停 in its box of 100 x 250: the triangles face up, lie inside the box, and have the area of the contours (outers less holes) scaled.
            var mesh = new MeshBuilder();
            Program.Check(MarkingLetters.AddCharacter(mesh, '停', 0f, 0f, 1f, 2.5f, false), "停 is drawn");
            Program.Check(mesh.TriangleCount > 20, "with many triangles: " + mesh.TriangleCount);
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                Program.Check(MeshBuilder.Cross(b - a, c - a).Y > 0f, "a triangle of 停 faces up");
            }

            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (Vec3 p in mesh.Positions)
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                z0 = Math.Min(z0, p.Z);
                z1 = Math.Max(z1, p.Z);
            }

            Program.Check(x0 >= -0.5f - 1e-3f && x1 <= 0.5f + 1e-3f && z0 >= -1.25f - 1e-3f && z1 <= 1.25f + 1e-3f, "inside its box: " + x0 + ".." + x1 + ", " + z0 + ".." + z1);
            Program.Check(x1 - x0 > 0.8f && z1 - z0 > 2.0f, "and fills most of it: " + (x1 - x0) + " x " + (z1 - z0));

            // The area is the contours' (outer less holes) scaled by the box: for 口, a ring.
            var ring = new MeshBuilder();
            MarkingLetters.AddCharacter(ring, '口', 0f, 0f, 1f, 1f, false);
            Font.TryGlyph('口', out GlyphOutline kou);
            float expected = (Math.Abs(Shoelace(kou.Contours[0])) - Math.Abs(Shoelace(kou.Contours[1]))) * (1f / 1000f) * (1f / 1000f);
            Program.Near(MeshArea(ring), Math.Abs(expected), Math.Abs(expected) * 0.01f, "口: area is the outer less the hole: " + MeshArea(ring) + " / " + Math.Abs(expected));
            // The hole is empty: the middle of the box has no triangle.
            bool covered = false;
            for (int i = 0; i + 2 < ring.Indices.Count; i += 3)
            {
                Vec3 a = ring.Positions[ring.Indices[i]], b = ring.Positions[ring.Indices[i + 1]], c = ring.Positions[ring.Indices[i + 2]];
                Vec3 centre = (a + b + c) * (1f / 3f);
                if (Math.Abs(centre.X) < 0.1f && Math.Abs(centre.Z - 0.0f) < 0.1f)
                    covered = true;
            }

            Program.Check(!covered, "the hole of 口 is open");

            // A figure fitted by its ink fills its cell exactly; a narrow 1 and a wide 0 in the same cell both fill it.
            foreach (char c in "01")
            {
                var m = new MeshBuilder();
                MarkingLetters.AddCharacter(m, c, 0f, 0f, 0.4f, 1f, true);
                float fx0 = float.MaxValue, fx1 = float.MinValue, fz0 = float.MaxValue, fz1 = float.MinValue;
                foreach (Vec3 p in m.Positions)
                {
                    fx0 = Math.Min(fx0, p.X);
                    fx1 = Math.Max(fx1, p.X);
                    fz0 = Math.Min(fz0, p.Z);
                    fz1 = Math.Max(fz1, p.Z);
                }

                Program.Near(fx1 - fx0, 0.4f, 1e-3f, c + " fills the cell's width");
                Program.Near(fz1 - fz0, 1f, 1e-3f, c + " fills the cell's height");
            }

            // A text box of figures: 17-18 across a box of 1.2 x 0.6: five cells, the hyphen a bar.
            var time = new MeshBuilder();
            int drawn = MarkingLetters.AddTexts(time, new List<MarkingText> { new MarkingText { Text = "17-18", X = 0f, Z = 0f, Width = 1.2f, Length = 0.6f } });
            Program.Check(drawn == 5 && time.TriangleCount > 10, "17-18: five characters drawn: " + drawn);
            // A box of one character.
            var word = new MeshBuilder();
            Program.Check(MarkingLetters.AddTexts(word, new List<MarkingText> { new MarkingText { Text = "公", X = 2f, Z = 5f, Width = 1f, Length = 2.5f }, new MarkingText { Text = "交", X = 2f, Z = 8f, Width = 1f, Length = 2.5f } }) == 2, "two characters in two boxes");
            Program.Check(word.Positions.TrueForAll(p => p.X >= 1.5f - 1e-3f && p.X <= 2.5f + 1e-3f && p.Z >= 3.75f - 1e-3f && p.Z <= 9.25f + 1e-3f), "inside their boxes");
            // A character the font does not have: nothing, and no throw. No font: nothing.
            Program.Check(!MarkingLetters.AddCharacter(new MeshBuilder(), '', 0f, 0f, 1f, 1f, false), "a character the font lacks is not drawn");
            Program.Check(MarkingLetters.AddTexts(new MeshBuilder(), null) == 0 && MarkingLetters.AddTexts(new MeshBuilder(), new List<MarkingText> { new MarkingText { Text = "", Width = 1f, Length = 1f } }) == 0, "no texts: nothing");
            Program.Check(!MarkingLetters.AddCharacter(new MeshBuilder(), '停', 0f, 0f, 0f, 1f, false) && !MarkingLetters.AddCharacter(new MeshBuilder(), '停', 0f, 0f, 1f, -1f, false), "a box with no size: nothing");
            MarkingLetters.Font = null;
            Program.Check(!MarkingLetters.AddCharacter(new MeshBuilder(), '停', 0f, 0f, 1f, 1f, false) && !MarkingLetters.Has('停'), "no font, no lettering");
            MarkingLetters.Font = Font;
        }
    }
}
