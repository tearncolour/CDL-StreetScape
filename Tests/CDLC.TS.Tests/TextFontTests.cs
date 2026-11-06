using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The fonts of the text light box (0.12.76): the faces of a collection, what a font file says about itself (names, outlines, coverage), the list of the system's fonts made from it, a font dropped into
    /// a folder found without a restart, and the characters a font lacks filled from the default font. Tests that need a font of this machine (Arial, the Chinese ones) say so and are left out where it has none.
    /// </summary>
    internal static class TextFontTests
    {
        public static void RunAll()
        {
            FontOutlines bundled = TextLayoutTests.BundledFont();
            if (bundled == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, bundled);
            Program.Run("fonts: the faces of a collection", Collection);
            Program.Run("fonts: what a font file says about itself", Info);
            Program.Run("fonts: the cmap's formats 4 and 12, to the last character of a range", Cmaps);
            Program.Run("fonts: the OS/2 table's weight and italic bit, the glyph key's variant", Os2AndKeys);
            Program.Run("fonts: a damaged file gives what could be read, and never throws", Damaged);
            Program.Run("fonts: the system's fonts of this machine", MachineFonts);
            Program.Run("fonts: the list has one font for each family", Curated);
            Program.Run("fonts: system fonts are choices, and a folder is looked at again without a restart", Choices);
            Program.Run("fonts: a character the font lacks is filled from the default font", Fallback);
            Program.Run("fonts: the font row of the template", Row);
        }

        internal static byte[] BundledBytes() => File.ReadAllBytes(FontLetterTests.RepoFile("Fonts", SignFonts.BundledCjk));

        /// <summary>A collection of faces copies of one font: the header, and the font moved behind it with its tables' offsets moved the same.</summary>
        internal static byte[] AsCollection(byte[] font, int faces)
        {
            int header = 12 + 4 * faces;
            var data = new byte[header + font.Length];
            data[0] = (byte)'t';
            data[1] = (byte)'t';
            data[2] = (byte)'c';
            data[3] = (byte)'f';
            data[5] = 1;
            data[11] = (byte)faces;
            for (int i = 0; i < faces; i++)
                Put32(data, 12 + 4 * i, header);
            Array.Copy(font, 0, data, header, font.Length);
            int tables = (font[4] << 8) | font[5];
            for (int i = 0; i < tables; i++)
            {
                int at = header + 12 + i * 16 + 8;
                Put32(data, at, Get32(data, at) + header);
            }

            return data;
        }

        private static void Put32(byte[] b, int at, long v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        private static long Get32(byte[] b, int at) => ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];

        private static string OsFont(string name)
        {
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                string path = Path.Combine(folder, name);
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Collection()
        {
            byte[] ttf = BundledBytes();
            byte[] collection = AsCollection(ttf, 2);
            var plain = new FontOutlines(ttf);
            var face0 = new FontOutlines(collection, 0);
            var face1 = new FontOutlines(collection, 1);
            Program.Check(plain.Ok && face0.Ok && face1.Ok, "both faces of a collection are read");
            Program.Check(new FontOutlines(collection).Ok, "a collection read as a plain font is its first face");
            Program.Check(!new FontOutlines(collection, 2).Ok && !new FontOutlines(collection, -1).Ok, "a face the collection does not have is not a font");
            Program.Check(new FontOutlines(ttf, 0).Ok && !new FontOutlines(ttf, 1).Ok, "a plain font has the one face 0");
            bool same = plain.TryAdvance('永', out float a) && face1.TryAdvance('永', out float b) && a == b && plain.UnitsPerEm == face1.UnitsPerEm;
            plain.TryGlyph('永', out GlyphOutline o0);
            face1.TryGlyph('永', out GlyphOutline o1);
            same &= o0 != null && o1 != null && o0.Contours.Count == o1.Contours.Count && o0.Contours[0].Length == o1.Contours[0].Length;
            Program.Check(same, "a face of a collection draws the glyphs of the font it holds");

            // A header that points outside the file, or counts what is not there, is not a font and not an exception.
            foreach (int bad in new[] { 0, 5, 11, 12, 15, 99999999 })
            {
                byte[] broken = (byte[])collection.Clone();
                Put32(broken, 12, bad);
                Program.Check(!new FontOutlines(broken, 0).Ok, "a face directory at " + bad + " is no font");
            }

            byte[] many = (byte[])collection.Clone();
            Put32(many, 8, 1000000);
            Program.Check(!new FontOutlines(many, 999).Ok && new FontOutlines(many, 1).Ok, "a count that is more than the header holds does not make a face of what is not there");
        }

        private static void Info()
        {
            byte[] ttf = BundledBytes();
            List<FontFaceInfo> faces = FontInfo.Probe(new FontBytes(ttf));
            Program.Check(faces.Count == 1 && faces[0].Face == 0, "one face in a plain font");
            FontFaceInfo f = faces[0];
            Console.WriteLine($"  bundled font says: family '{f.FamilyEn}' / '{f.FamilyZh}', style '{f.StyleEn}', weight {f.WeightClass}, Chinese {f.CjkCovered}/{FontInfo.CjkSample.Length}, Latin {f.LatinCovered}/{FontInfo.LatinSample.Length}");
            Program.Check(f.Supported && f.CoversCjk, "the traffic sign font is drawn by this reader and has Chinese: " + f.CjkCovered + " of " + FontInfo.CjkSample.Length);
            Program.Check(f.CjkCovered <= FontInfo.CjkSample.Length && f.LatinCovered <= FontInfo.LatinSample.Length, "the counts are of the samples");
            Program.Check(f.DisplayEn.Length > 0 && f.DisplayZh.Length > 0, "it has a name to list it by");

            List<FontFaceInfo> two = FontInfo.Probe(new FontBytes(AsCollection(ttf, 2)));
            Program.Check(two.Count == 2 && two[0].Face == 0 && two[1].Face == 1 && two.All(x => x.Supported && x.CjkCovered == f.CjkCovered && x.FamilyEn == f.FamilyEn), "a collection's faces are each read: " + two.Count);
            Program.Check(FontInfo.Probe((IFontBytes)null).Count == 0 && FontInfo.Probe(new FontBytes(new byte[0])).Count == 0 && FontInfo.Probe(new FontBytes(new byte[] { 1, 2, 3 })).Count == 0, "nothing is no face");

            // The same answers from a file on disk, read in pieces.
            string dir = Path.Combine(AppContext.BaseDirectory, "tmp", "ts-fontinfo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "a.ttf");
                File.WriteAllBytes(path, ttf);
                List<FontFaceInfo> onDisk = FontInfo.Probe(path);
                Program.Check(onDisk.Count == 1 && onDisk[0].FamilyEn == f.FamilyEn && onDisk[0].CjkCovered == f.CjkCovered && onDisk[0].Supported, "a file on disk says the same");
                Program.Check(FontInfo.Probe(Path.Combine(dir, "missing.ttf")).Count == 0, "a file that is not there has no face");
                File.WriteAllText(Path.Combine(dir, "note.ttf"), "not a font at all");
                Program.Check(FontInfo.Probe(Path.Combine(dir, "note.ttf")).Count == 0 || !FontInfo.Probe(Path.Combine(dir, "note.ttf"))[0].Supported, "a file that is not a font is not listed as one");
            }
            finally
            {
                Directory.Delete(dir, true);
            }

            // Which of the names is chosen is told from a font whose name table the test writes: the typographic family before the plain one, English and Chinese kept apart.
            FontFaceInfo named = FontInfo.Probe(new FontBytes(WithNames(ttf, ("Plain Family", "Plain Sub", "Typo Family", "Typo Sub", "字族")))).FirstOrDefault();
            Program.Check(named != null && named.FamilyEn == "Typo Family" && named.StyleEn == "Typo Sub" && named.FamilyZh == "字族", "typographic names first, the Chinese family apart: " + named?.FamilyEn + "/" + named?.StyleEn + "/" + named?.FamilyZh);
            FontFaceInfo plainNames = FontInfo.Probe(new FontBytes(WithNames(ttf, ("Plain Family", "Plain Sub", null, null, null)))).FirstOrDefault();
            Program.Check(plainNames != null && plainNames.FamilyEn == "Plain Family" && plainNames.StyleEn == "Plain Sub" && plainNames.FamilyZh == "", "the plain names when there are no typographic ones");
        }

        /// <summary>A font with its name table made over: Windows records, English (1409) and a Chinese family (0804); null names are left out.</summary>
        private static byte[] WithNames(byte[] ttf, (string Family, string Style, string TypoFamily, string TypoStyle, string Chinese) names)
        {
            var records = new List<(int Platform, int Language, int Id, byte[] Text)>();
            void Add(int platform, int language, int id, string text)
            {
                if (text != null)
                    records.Add((platform, language, id, System.Text.Encoding.BigEndianUnicode.GetBytes(text)));
            }

            Add(3, 0x0409, 1, names.Family);
            Add(3, 0x0409, 2, names.Style);
            Add(3, 0x0409, 16, names.TypoFamily);
            Add(3, 0x0409, 17, names.TypoStyle);
            Add(3, 0x0804, 1, names.Chinese);
            int strings = 6 + records.Count * 12;
            var table = new List<byte> { 0, 0, (byte)(records.Count >> 8), (byte)records.Count, (byte)(strings >> 8), (byte)strings };
            int offset = 0;
            foreach (var r in records)
            {
                foreach (int v in new[] { r.Platform, 1, r.Language, r.Id, r.Text.Length, offset })
                {
                    table.Add((byte)(v >> 8));
                    table.Add((byte)v);
                }

                offset += r.Text.Length;
            }

            foreach (var r in records)
                table.AddRange(r.Text);
            while (table.Count % 4 != 0)
                table.Add(0);
            return WithTable(ttf, "name", table.ToArray());
        }

        /// <summary>A font with one table made over: the new one goes at the end of the file and the directory record of the tag is pointed at it.</summary>
        private static byte[] WithTable(byte[] ttf, string tag, byte[] table)
        {
            byte[] result = new byte[ttf.Length + table.Length];
            Array.Copy(ttf, result, ttf.Length);
            table.CopyTo(result, ttf.Length);
            int tables = (ttf[4] << 8) | ttf[5];
            for (int i = 0; i < tables; i++)
            {
                int at = 12 + i * 16;
                if (ttf[at] == tag[0] && ttf[at + 1] == tag[1] && ttf[at + 2] == tag[2] && ttf[at + 3] == tag[3])
                {
                    Put32(result, at + 8, ttf.Length);
                    Put32(result, at + 12, table.Length);
                }
            }

            return result;
        }

        private static void Put16(List<byte> b, int v)
        {
            b.Add((byte)(v >> 8));
            b.Add((byte)v);
        }

        private static void Put32(List<byte> b, long v)
        {
            Put16(b, (int)(v >> 16) & 0xFFFF);
            Put16(b, (int)v & 0xFFFF);
        }

        /// <summary>A cmap of one format 12 subtable (Windows, Unicode full): the groups of characters, each with the glyphs 1, 2 ... from its start.</summary>
        private static byte[] Cmap12(params (int Start, int End)[] groups)
        {
            var t = new List<byte>();
            Put16(t, 0);
            Put16(t, 1);
            Put16(t, 3);
            Put16(t, 10);
            Put32(t, 12);
            Put16(t, 12);
            Put16(t, 0);
            Put32(t, 16 + groups.Length * 12);
            Put32(t, 0);
            Put32(t, groups.Length);
            int glyph = 1;
            foreach ((int start, int end) in groups)
            {
                Put32(t, start);
                Put32(t, end);
                Put32(t, glyph);
                glyph += end - start + 1;
            }

            return t.ToArray();
        }

        /// <summary>A cmap of one format 4 subtable (Windows, Unicode BMP): segments that go by a delta, and the segment of rangeStart to rangeEnd by the glyph id array (the glyph of its first character is 0: not there).</summary>
        private static byte[] Cmap4((int Start, int End)[] byDelta, (int Start, int End) byArray, int zeroAt = -1)
        {
            var segments = new List<(int Start, int End, bool Array)>();
            foreach ((int start, int end) in byDelta)
                segments.Add((start, end, false));
            segments.Add((byArray.Start, byArray.End, true));
            segments.Sort((a, b) => a.Start.CompareTo(b.Start));
            segments.Add((0xFFFF, 0xFFFF, false));
            int count = segments.Count;
            var t = new List<byte>();
            Put16(t, 0);
            Put16(t, 1);
            Put16(t, 3);
            Put16(t, 1);
            Put32(t, 12);
            int arrayLength = byArray.End - byArray.Start + 1;
            int length = 16 + count * 8 + arrayLength * 2;
            Put16(t, 4);
            Put16(t, length);
            Put16(t, 0);
            Put16(t, count * 2);
            Put16(t, 0);
            Put16(t, 0);
            Put16(t, 0);
            foreach (var s in segments)
                Put16(t, s.End);
            Put16(t, 0);
            foreach (var s in segments)
                Put16(t, s.Start);
            foreach (var s in segments)
                Put16(t, s.Array || s.Start == 0xFFFF ? 0 : (s.Start == zeroAt ? -s.Start : 1 - s.Start) & 0xFFFF);
            for (int i = 0; i < count; i++)
                Put16(t, segments[i].Array ? 2 * (count - i) : 0);
            // The glyph ids of the array's segment: none for its first character, glyph 7 for the others.
            for (int i = 0; i < arrayLength; i++)
                Put16(t, i == 0 ? 0 : 7);
            return t.ToArray();
        }

        private static (int Latin, int Cjk) Covered(byte[] ttf, byte[] cmap)
        {
            FontFaceInfo f = FontInfo.Probe(new FontBytes(WithTable(ttf, "cmap", cmap))).FirstOrDefault();
            return f != null && f.Supported ? (f.LatinCovered, f.CjkCovered) : (-1, -1);
        }

        private static void Cmaps()
        {
            byte[] ttf = BundledBytes();
            string cjk = FontInfo.CjkSample;
            var singles = cjk.Select(c => ((int)c, (int)c)).ToArray();
            // Format 12: the range of the Latin sample's characters, each end of it a character of the sample.
            var group12 = new List<(int, int)> { (0x30, 0x39), (0x41, 0x5A), (0x61, 0x7A) };
            group12.AddRange(singles.OrderBy(s => s.Item1));
            (int latin, int chinese) = Covered(ttf, Cmap12(group12.ToArray()));
            Program.Check(latin == 6 && chinese == 20, $"format 12: every sample character, to the last of each range: {latin} and {chinese}");
            foreach (var shave in new[] { (0x30, 0x39, 0x31, 0x39), (0x30, 0x39, 0x30, 0x38), (0x41, 0x5A, 0x42, 0x5A), (0x41, 0x5A, 0x41, 0x59), (0x61, 0x7A, 0x62, 0x7A), (0x61, 0x7A, 0x61, 0x79) })
            {
                var shaved = new List<(int, int)>(group12);
                shaved[shaved.IndexOf((shave.Item1, shave.Item2))] = (shave.Item3, shave.Item4);
                (int l, int c) = Covered(ttf, Cmap12(shaved.OrderBy(g => g.Item1).ToArray()));
                Program.Check(l == 5 && c == 20, $"format 12: a range that stops short of its sample character leaves it out ({shave.Item3:X}-{shave.Item4:X}): {l}");
            }

            // A character of Chinese just beyond a range is not in it; the characters between ranges are not either.
            var gaps = new List<(int, int)> { (0x30, 0x39), (0x41, 0x5A), (0x61, 0x7A), (0x7B, 0x7F) };
            gaps.AddRange(singles.Skip(2).OrderBy(s => s.Item1));
            (int lg, int cg) = Covered(ttf, Cmap12(gaps.OrderBy(g => g.Item1).ToArray()));
            Program.Check(lg == 6 && cg == 18, $"format 12: the two characters not in a group are not covered: {cg}");
            Program.Check(Covered(ttf, Cmap12()) == (0, 0), "format 12 with no group covers nothing");

            // Format 4: segments by delta, and by the glyph id array whose first entry is 0.
            var delta = new List<(int, int)> { (0x30, 0x39), (0x41, 0x5A) };
            delta.AddRange(singles.Select(s => s).OrderBy(s => s.Item1));
            (int l4, int c4) = Covered(ttf, Cmap4(delta.ToArray(), (0x61, 0x7A)));
            Program.Check(l4 == 5 && c4 == 20, $"format 4: the segment by the glyph array has no glyph for its first character ('a'), the rest are there: {l4} and {c4}");
            var delta2 = new List<(int, int)> { (0x30, 0x39), (0x41, 0x5A), (0x61, 0x7A) };
            (int l5, int c5) = Covered(ttf, Cmap4(delta2.ToArray(), (0x4E00, 0x9FFF)));
            Program.Check(l5 == 6 && c5 == 19 && FontInfo.Probe(new FontBytes(WithTable(ttf, "cmap", Cmap4(delta2.ToArray(), (0x4E00, 0x9FFF))))).First().CoversCjk, $"format 4: the characters of a range by the array, but its first: {c5}");
            foreach (var shave in new[] { (0x30, 0x39, 0x31, 0x39), (0x30, 0x39, 0x30, 0x38), (0x41, 0x5A, 0x42, 0x5A), (0x41, 0x5A, 0x41, 0x59) })
            {
                var shaved = new List<(int, int)>(delta);
                shaved[shaved.IndexOf((shave.Item1, shave.Item2))] = (shave.Item3, shave.Item4);
                (int l, int c) = Covered(ttf, Cmap4(shaved.OrderBy(g => g.Item1).ToArray(), (0x61, 0x7A)));
                Program.Check(l == 4 && c == 20, $"format 4: a segment that stops short of its sample character leaves it out ({shave.Item3:X}-{shave.Item4:X}): {l}");
            }

            // A segment whose delta makes the glyph of its first character 0: that character is not there, the rest are.
            (int lz, int cz) = Covered(ttf, Cmap4(delta.ToArray(), (0x61, 0x7A), 0x30));
            Program.Check(lz == 4 && cz == 20, $"format 4: a delta that gives glyph 0 is no glyph ('0'; and 'a' by the array): {lz}");

            // No table this reader knows is a font it cannot look up: not listed as one that draws.
            var other = new List<byte>();
            Put16(other, 0);
            Put16(other, 1);
            Put16(other, 1);
            Put16(other, 0);
            Put32(other, 12);
            Put16(other, 0);
            Put16(other, 262);
            FontFaceInfo none = FontInfo.Probe(new FontBytes(WithTable(ttf, "cmap", other.ToArray()))).First();
            Program.Check(!none.Supported, "a cmap with no subtable of format 4 or 12 is not a font this reader lists");
        }

        private static int CountIn(string sample, int from, int to) => sample.Count(c => c >= from && c <= to);

        private static void Damaged()
        {
            byte[] ttf = BundledBytes();
            byte[] collection = AsCollection(ttf, 3);
            var random = new Random(12345);
            int tried = 0;
            foreach (byte[] source in new[] { ttf, collection })
            {
                foreach (int length in new[] { 0, 1, 3, 4, 11, 12, 13, 27, 28, 100, 400, 3000, 50000 })
                {
                    byte[] cut = new byte[Math.Min(length, source.Length)];
                    Array.Copy(source, cut, cut.Length);
                    Probe(cut);
                    tried++;
                }

                for (int i = 0; i < 60; i++)
                {
                    byte[] flipped = (byte[])source.Clone();
                    int flips = 1 + random.Next(8);
                    for (int k = 0; k < flips; k++)
                        flipped[random.Next(Math.Min(flipped.Length, 2000))] = (byte)random.Next(256);
                    Probe(flipped);
                    tried++;
                }
            }

            Program.Check(tried > 100, "damaged files were tried: " + tried);
        }

        private static void Probe(byte[] data)
        {
            try
            {
                FontInfo.Probe(new FontBytes(data));
                new FontOutlines(data, 0);
                new FontOutlines(data, 2);
            }
            catch (Exception ex)
            {
                Program.Check(false, "a damaged font threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void MachineFonts()
        {
            string arial = OsFont("arial.ttf"), yahei = OsFont("msyh.ttc"), simsun = OsFont("simsun.ttc");
            if (arial == null && yahei == null && simsun == null)
            {
                Console.WriteLine("  (this machine has none of Arial, Microsoft YaHei or SimSun: the system fonts' names were not checked)");
                return;
            }

            if (arial != null)
            {
                FontFaceInfo f = FontInfo.Probe(arial).First();
                Program.Check(f.Supported && f.FamilyEn == "Arial" && f.CoversLatin && !f.CoversCjk && f.IsRegular, $"Arial is Latin and regular: {f.FamilyEn}, {f.StyleEn}, Chinese {f.CjkCovered}");
            }

            foreach (string file in new[] { yahei, simsun })
            {
                if (file == null)
                    continue;
                List<FontFaceInfo> faces = FontInfo.Probe(file);
                Program.Check(faces.Count >= 1 && faces.All(x => x.Face >= 0 && x.Face < faces.Count), Path.GetFileName(file) + " has its faces: " + faces.Count);
                FontFaceInfo regular = faces.FirstOrDefault(x => x.IsRegular && x.Supported) ?? faces[0];
                Program.Check(regular.Supported && regular.CoversCjk && regular.CoversLatin, Path.GetFileName(file) + " is Chinese and Latin: " + regular.FamilyEn + " / " + regular.FamilyZh);
                Program.Check(regular.FamilyZh.Length > 0 && regular.FamilyEn.Length > 0, "and has its names in both languages: " + regular.FamilyEn + " / " + regular.FamilyZh);
                // The collection's faces are fonts of their own: the first and the last are read and draw a Chinese character.
                byte[] bytes = File.ReadAllBytes(file);
                foreach (FontFaceInfo face in new[] { faces[0], faces[faces.Count - 1] })
                {
                    var font = new FontOutlines(bytes, face.Face);
                    Program.Check(font.Ok && font.TryGlyph('永', out GlyphOutline outline) && !outline.IsEmpty, Path.GetFileName(file) + " face " + face.Face + " draws a Chinese character");
                }
            }
        }

        private static FontFaceInfo Face(string family, bool cjk, bool latin = true, int weight = 400, bool italic = false, int face = 0, bool supported = true, string zh = "", string style = "Regular") =>
            new FontFaceInfo { FamilyEn = family, FamilyZh = zh, StyleEn = style, Supported = supported, WeightClass = weight, Italic = italic, Face = face, CjkCovered = cjk ? FontInfo.CjkSample.Length : 0, LatinCovered = latin ? FontInfo.LatinSample.Length : 0 };

        private static void Curated()
        {
            var faces = new List<SystemFace>
            {
                new SystemFace("alphab.ttf", Face("Alpha", false, weight: 700, style: "Bold")),
                new SystemFace("alphai.ttf", Face("Alpha", false, italic: true, style: "Italic")),
                new SystemFace("alpha.ttf", Face("Alpha", false)),
                new SystemFace("beta.ttc", Face("Beta", true, weight: 700, face: 0, zh: "贝塔", style: "Bold")),
                new SystemFace("beta.ttc", Face("Beta", true, face: 1, zh: "贝塔")),
                new SystemFace("aardvark.otf", Face("Aardvark", false)),
                new SystemFace("sym.ttf", Face("Symbols", false, latin: false)),
                new SystemFace("broken.ttf", Face("Broken", true, supported: false)),
                new SystemFace("vertical.ttf", Face("@Vertical", true)),
                new SystemFace("noname.ttf", Face("", true))
            };
            List<GuideChoice> list = TextFonts.CuratedChoices(faces, 100);
            Program.Check(list.Select(c => c.Value).SequenceEqual(new[] { "sys:beta.ttc#1", "sys:aardvark.otf", "sys:alpha.ttf" }), "the regular face of each family, those with Chinese first: " + string.Join(", ", list.Select(c => c.Value)));
            Program.Check(list[0].Zh == "贝塔" && list[0].En == "Beta" && list[1].Zh == "Aardvark（无中文）" && list[1].En == "Aardvark (no CJK)", "names in both languages, and a font without Chinese says so: " + list[0].Zh + " | " + list[1].Zh + " | " + list[1].En);
            Program.Check(TextFonts.CuratedChoices(faces, 2).Count == 2 && TextFonts.CuratedChoices(faces, 0).Count == 0 && TextFonts.CuratedChoices(null, 5).Count == 0, "the most the list holds");

            // The same list whatever order the files were read in.
            var random = new Random(7);
            string expected = string.Join("|", list.Select(c => c.Value));
            for (int round = 0; round < 40; round++)
            {
                List<SystemFace> shuffled = faces.OrderBy(_ => random.Next()).ToList();
                Program.Check(string.Join("|", TextFonts.CuratedChoices(shuffled, 100).Select(c => c.Value)) == expected, "the order the files were read in does not matter (round " + round + ")");
            }

            // The samples: a face with all but two of the Chinese sample and all but one of the Latin one is a Chinese and a Latin font; with one fewer of either it is not.
            FontFaceInfo edge = Face("Edge", false);
            edge.CjkCovered = FontInfo.CjkSample.Length - 2;
            edge.LatinCovered = FontInfo.LatinSample.Length - 1;
            Program.Check(edge.CoversCjk && edge.CoversLatin, "all but two Chinese and all but one Latin characters still cover");
            edge.CjkCovered--;
            edge.LatinCovered--;
            Program.Check(!edge.CoversCjk && !edge.CoversLatin, "one fewer of each does not");
            FontFaceInfo weights = Face("Weights", false);
            foreach ((int weight, bool italic, bool regular) in new[] { (349, false, false), (350, false, true), (400, false, true), (450, false, true), (451, false, false), (400, true, false) })
            {
                weights.WeightClass = weight;
                weights.Italic = italic;
                Program.Check(weights.IsRegular == regular, "weight " + weight + (italic ? " italic" : "") + " is " + (regular ? "" : "not ") + "regular");
            }

            // A face with only an italic or only a bold takes the family when nothing is regular; the nearer to regular weight wins.
            List<GuideChoice> only = TextFonts.CuratedChoices(new[]
            {
                new SystemFace("g900.ttf", Face("Gamma", true, weight: 900)), new SystemFace("g600.ttf", Face("Gamma", true, weight: 600)), new SystemFace("gi.ttf", Face("Gamma", true, italic: true, weight: 400))
            }, 10);
            Program.Check(only.Count == 1 && only[0].Value == "sys:g600.ttf", "with no regular face the upright one nearest to regular stands for the family: " + only[0].Value);
        }

        /// <summary>The OS/2 table of a font with its weight class and its selection flags made over.</summary>
        private static byte[] WithOs2(byte[] ttf, int weight, int selection)
        {
            int tables = (ttf[4] << 8) | ttf[5];
            for (int i = 0; i < tables; i++)
            {
                int at = 12 + i * 16;
                if (ttf[at] == 'O' && ttf[at + 1] == 'S' && ttf[at + 2] == '/' && ttf[at + 3] == '2')
                {
                    int offset = (int)Get32(ttf, at + 8), length = (int)Get32(ttf, at + 12);
                    byte[] table = new byte[length];
                    Array.Copy(ttf, offset, table, 0, length);
                    table[4] = (byte)(weight >> 8);
                    table[5] = (byte)weight;
                    table[62] = (byte)(selection >> 8);
                    table[63] = (byte)selection;
                    return WithTable(ttf, "OS/2", table);
                }
            }

            return ttf;
        }

        private static void Os2AndKeys()
        {
            byte[] ttf = BundledBytes();
            foreach ((int weight, int selection, bool italic, bool regular) in new[] { (400, 0, false, true), (400, 1, true, false), (400, 2, false, true), (400, 0x21, true, false), (700, 0, false, false), (300, 0, false, false), (400, 0x40, false, true) })
            {
                FontFaceInfo f = FontInfo.Probe(new FontBytes(WithOs2(ttf, weight, selection))).First();
                Program.Check(f.WeightClass == weight && f.Italic == italic && f.IsRegular == regular, $"OS/2 weight {weight}, selection {selection:X}: weight {f.WeightClass}, italic {f.Italic}, regular {f.IsRegular}");
            }

            Program.Check(new GlyphKey(1, 2, 3).Equals(new GlyphKey(1, 2, 3, 0)) && !new GlyphKey(1, 2, 3, 0).Equals(new GlyphKey(1, 2, 3, 5)) && !new GlyphKey(1, 2, 3, 5).Equals(new GlyphKey(1, 2, 3, 6)) && new GlyphKey(1, 2, 3, 5).Equals(new GlyphKey(1, 2, 3, 5)), "a glyph key tells the glyph of a variant from the font's own");
            Program.Check(new GlyphKey(1, 2, 3, 5).Equals((object)new GlyphKey(1, 2, 3, 5)) && !new GlyphKey(1, 2, 3, 5).Equals((object)new GlyphKey(2, 2, 3, 5)) && !new GlyphKey(1, 2, 3, 5).Equals((object)"x"), "and by object");
        }

        private static void Choices()
        {
            byte[] ttf = BundledBytes();
            string before = TextFonts.Folder;
            string root = Path.Combine(AppContext.BaseDirectory, "tmp", "ts-sysfonts-" + Guid.NewGuid().ToString("N"));
            string system = Path.Combine(root, "system"), player = Path.Combine(root, "player");
            Directory.CreateDirectory(system);
            Directory.CreateDirectory(player);
            TextFonts.RescanAndWait();
            int changes = 0, listVersion = TextFonts.ListVersion;
            Action hook = () => changes++;
            Program.Check(TextBoxes.Template != null, "the template is made (it hears of the list's changes)");
            TextFonts.Changed += hook;
            try
            {
                File.WriteAllBytes(Path.Combine(system, "zz.ttf"), ttf);
                File.WriteAllBytes(Path.Combine(system, "multi.ttc"), AsCollection(ttf, 2));
                File.WriteAllText(Path.Combine(system, "junk.ttf"), "x");
                File.WriteAllText(Path.Combine(system, "readme.txt"), "x");
                TextFonts.Begin(player);
                TextFonts.UseSystem(new[] { system, Path.Combine(root, "missing") }, false);
                // Known by name at once, before anything was read.
                Program.Check(TextFonts.IsKnownValue("sys:zz.ttf") && TextFonts.IsKnownValue("sys:multi.ttc#1") && TextFonts.IsKnownValue("sys:ZZ.TTF"), "a system font is known by its file name before the list is made");
                foreach (string bad in new[] { "", null, "zz.ttf", "sys:", "sys:missing.ttf", "sys:../zz.ttf", "sys:..\\zz.ttf", "sys:readme.txt", "sys:multi.ttc#64", "sys:multi.ttc#-1", "sys:multi.ttc#x", "sys:zz.ttf#" })
                    Program.Check(!TextFonts.IsKnownValue(bad), "'" + bad + "' is no system font");
                GuideSpec kept = TextBoxes.Template.Parse("text=字;font=sys:zz.ttf");
                Program.Check(kept.Get("font") == "sys:zz.ttf", "the settings keep a system font that the list has not read yet");
                Program.Check(TextBoxes.Template.Parse("font=sys:nothing.ttf").Get("font") == TextFonts.DefaultChoice && TextBoxes.Template.Parse("font=nothing.ttf").Get("font") == TextFonts.DefaultChoice, "and send any other name to the default");

                TextFonts.RescanAndWait();
                string[] values = TextFonts.Choices().Select(c => c.Value).ToArray();
                Program.Check(values[0] == TextFonts.DefaultChoice && values.Contains("sys:multi.ttc") && !values.Contains("sys:zz.ttf") && !values.Contains("sys:junk.ttf"), "the list has one of the two fonts of the same family, not the junk: " + string.Join(",", values));
                Program.Check(TextFonts.ListVersion > listVersion, "the list's change was counted: " + listVersion + " to " + TextFonts.ListVersion);
                // A font of a system file is read by its value; a face of a collection by its number.
                FontOutlines whole = TextFonts.Get(TextFonts.IdOf("sys:zz.ttf")), second = TextFonts.Get(TextFonts.IdOf("sys:multi.ttc#1"));
                Program.Check(whole != null && whole.Ok && second != null && second.Ok && !ReferenceEquals(whole, second), "a system font and the second face of a collection are read");
                Program.Check(TextFonts.Get(TextFonts.IdOf("sys:multi.ttc#9")) == null, "a face the collection does not have is no font");

                int version = TextFonts.ListVersion, tellings = changes;
                TextFonts.RescanAndWait();
                Program.Check(TextFonts.ListVersion == version && changes == tellings, "a scan that finds nothing new tells nothing");

                // A file dropped into the player's folder appears with no restart; one taken away goes.
                Program.Check(!TextFonts.Files.Contains("mine.ttf"), "not there yet");
                tellings = changes;
                File.WriteAllBytes(Path.Combine(player, "mine.ttf"), ttf);
                File.WriteAllBytes(Path.Combine(player, "collection.ttc"), AsCollection(ttf, 2));
                TextFonts.RescanAndWait();
                Program.Check(TextFonts.Files.Contains("mine.ttf") && TextFonts.Files.Contains("collection.ttc") && TextFonts.Choices().Any(c => c.Value == "mine.ttf"), "a font dropped into the folder is a choice at the next scan");
                Program.Check(TextFonts.ListVersion > version && changes > tellings, "and the change is told: version " + version + " to " + TextFonts.ListVersion + ", told " + tellings + " to " + changes);
                Program.Check(TextFonts.Get(TextFonts.IdOf("mine.ttf")) != null && TextFonts.Get(TextFonts.IdOf("collection.ttc")) != null, "and is read");
                File.Delete(Path.Combine(player, "mine.ttf"));
                TextFonts.RescanAndWait();
                Program.Check(!TextFonts.Files.Contains("mine.ttf"), "a file taken away is gone from the list");

                // A file of the system folder that appears later is known at the next scan, and one that changed is read again.
                File.WriteAllBytes(Path.Combine(system, "later.ttf"), ttf);
                Program.Check(!TextFonts.IsKnownValue("sys:later.ttf"), "a file made after the listing is not known until the next one");
                TextFonts.RescanAndWait();
                Program.Check(TextFonts.IsKnownValue("sys:later.ttf"), "and is known after it");

                // The same call from many threads at once makes one scan at a time and ends with a list.
                var threads = new List<System.Threading.Thread>();
                for (int i = 0; i < 6; i++)
                {
                    var t = new System.Threading.Thread(TextFonts.RescanAndWait);
                    threads.Add(t);
                    t.Start();
                }

                foreach (System.Threading.Thread t in threads)
                    t.Join();
                Program.Check(TextFonts.Choices().Any(c => c.Value == "sys:later.ttf") && TextFonts.Choices().Count(c => c.Value.StartsWith("sys:")) == 1, "scans started together end with the list made: one family, its first file");
            }
            finally
            {
                TextFonts.Changed -= hook;
                TextFonts.UseSystem(new string[0], false);
                TextFonts.Begin(before);
                TextFonts.RescanAndWait();
                TextBoxes.RefreshFonts();
                TextFonts.Release();
                TextFonts.Use(TextFonts.DefaultId, TextLayoutTests.BundledFont());
                Directory.Delete(root, true);
            }

            Program.Check(!TextFonts.Choices().Any(c => c.Value.StartsWith("sys:")), "the system's fonts are let go with their folder");
        }

        private static void Fallback()
        {
            // The advance of a character the chosen face lacks is the fallback's, in the chosen face's units; where neither has it, none.
            var primary = new TextFace(1000f, -120f, 880f, c => c == 'A' ? 600f : -1f);
            var filler = new TextFace(2000f, -400f, 1600f, c => c == 'B' ? 1000f : c == 'A' ? 1200f : -1f);
            TextFace composed = TextFaces.Compose(primary, filler);
            Program.Check(composed.UnitsPerEm == 1000f && composed.EmTop == 880f && composed.EmBottom == -120f, "the box and the units are the chosen face's");
            Program.Check(composed.Advance('A') == 600f && composed.Advance('B') == 500f && composed.Advance('Z') == -1f, "its own advance first, the fallback's scaled to its units, none where neither has it");
            var zero = new TextFace(1000f, -120f, 880f, c => c == 'M' ? 0f : -1f);
            Program.Check(TextFaces.Compose(zero, filler).Advance('M') == 0f && TextFaces.Compose(zero, filler).Advance('B') == 500f, "an advance of 0 (a mark) is the face's own, not a gap to fill");

            const int Id = 4242;
            TextPlanJob plain = new TextPlanJob("A永", Id, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f);
            TextPlanJob plainAgain = new TextPlanJob("A永", Id, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f, TextArt.Plain);
            TextPlanJob filled = new TextPlanJob("A永", Id, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f, TextArt.Create(fallback: true));
            Program.Check(plain.Equals(plainAgain) && plain.Key == plainAgain.Key && plain.Art.IsPlain && plain.Art.Key == 0UL, "a job with no art is the job of 0.12.75, with the same key");
            Program.Check(!plain.Equals(filled) && plain.Key != filled.Key && filled.GetHashCode() != plain.GetHashCode() && TextArt.Create(fallback: true).Equals(TextArt.Create(fallback: true)) && TextArt.Create() == TextArt.Plain, "a job that fills in is another job");

            string arialPath = OsFont("arial.ttf");
            if (arialPath == null)
            {
                Console.WriteLine("  (this machine has no Arial: the fallback was checked on faces that are made up, not on a font)");
                return;
            }

            FontOutlines arial = new FontOutlines(File.ReadAllBytes(arialPath)), bundled = TextLayoutTests.BundledFont();
            TextFonts.Use(Id, arial);
            try
            {
                var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
                var planner = new TextPlanner(id => id == Id ? arial : id == TextFonts.DefaultId ? bundled : null, cache);
                TextPlanResult blank = planner.Plan(plain), full = planner.Plan(filled);
                Program.Check(blank.Meshes[0] != null && blank.Meshes[1] == null && blank.Layout.Glyphs[1].Missing && blank.Layout.Missing.Contains('永'), "without the switch the character Arial lacks is a blank and is listed");
                Program.Check(blank.Warnings.Any(w => w.Kind == TextWarningKind.MissingCharacter), "and the editor says so");
                Program.Check(full.Meshes[0] != null && full.Meshes[1] != null && !full.Layout.Glyphs[1].Missing && full.Layout.Missing.Count == 0, "with it the character is drawn");
                Program.Check(!full.Warnings.Any(w => w.Kind == TextWarningKind.MissingCharacter), "and nothing is missing");
                Program.Check(ReferenceEquals(blank.Meshes[0], full.Meshes[0]), "a character the font has is the same cached glyph either way");
                Program.Check(Math.Abs(full.Layout.Glyphs[1].X - blank.Layout.Glyphs[1].X) < 1e-4f, "the blank keeps the place the glyph then takes");

                int bucket = TextGlyphs.Bucket(filled.RelativeFlatness);
                GlyphMesh basic = cache.GetOrBuild(bundled, TextFonts.DefaultId, '永', bucket);
                float lift = TextFaces.Lift(arial, bundled);
                Console.WriteLine($"  the default font's box against Arial's: lifted by {lift:0.0000} em");
                Program.Check(Math.Abs(full.Meshes[1].MinY - (basic.MinY + lift)) < 1e-5f && Math.Abs(full.Meshes[1].MaxY - (basic.MaxY + lift)) < 1e-5f && Math.Abs(full.Meshes[1].MaxX - basic.MaxX) < 1e-6f, "the filled glyph is the default font's, moved into Arial's box by the difference of the boxes");
                // Where the glyph stands in its em box does not change: the same gap from the top of the box as in the font it came from.
                float gapThere = bundled.EmTop / bundled.UnitsPerEm - basic.MaxY, gapHere = arial.EmTop / arial.UnitsPerEm - full.Meshes[1].MaxY;
                float lowThere = basic.MinY - bundled.EmBottom / bundled.UnitsPerEm, lowHere = full.Meshes[1].MinY - arial.EmBottom / arial.UnitsPerEm;
                Program.Check(Math.Abs(gapThere - gapHere) < 1e-4f && Math.Abs(lowThere - lowHere) < 1e-4f, "so it stands in Arial's box where it stood in its own: " + gapThere + " and " + gapHere);
                Program.Check(full.Meshes[1].IsSound(out string why), "and is a sound mesh: " + why);
                Program.Check(full.Frame != null && full.Frame.Islands.Count > blank.Frame.Islands.Count, "the frame holds the filled character too");

                // The same text in the default font needs no filling and is not a different plan.
                TextPlanJob own = new TextPlanJob("A永", TextFonts.DefaultId, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f, TextArt.Create(fallback: true));
                TextPlanResult same = planner.Plan(own), none = planner.Plan(new TextPlanJob("A永", TextFonts.DefaultId, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f));
                Program.Check(same.Meshes.Length == none.Meshes.Length && same.Meshes.Zip(none.Meshes, ReferenceEquals).All(x => x), "the default font fills in nothing: the same glyphs");

                // A character neither has stays a blank, and the two meshes of a character are kept apart in the cache.
                TextPlanResult absent = planner.Plan(new TextPlanJob("A☃永", Id, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f, TextArt.Create(fallback: true)));
                Program.Check(absent.Meshes[1] == null && absent.Layout.Glyphs[1].Missing && absent.Layout.Missing.Contains('☃') && absent.Meshes[2] != null, "a character that neither font has is left blank and listed");
            }
            finally
            {
                TextFonts.Use(TextFonts.DefaultId, TextLayoutTests.BundledFont());
                TextFonts.Release();
                TextFonts.Use(TextFonts.DefaultId, TextLayoutTests.BundledFont());
            }
        }

        private static void Row()
        {
            GuideTemplate t = TextBoxes.Template;
            GuideField font = t.FieldOf("font"), fill = t.FieldOf("fb");
            Program.Check(font != null && font.Visible == null && font.Accepts != null, "the font row is always shown");
            Program.Check(fill != null && fill.Kind == GuideFieldKind.Flag && fill.Default == "1", "the switch for the gaps is a flag, on");
            Program.Check(!fill.Visible(t.Parse("font=bundled")) && t.Parse("font=bundled").Get("fb") == "1", "it is shown only for a font that is not the default");
            string before = TextFonts.Folder;
            string dir = Path.Combine(AppContext.BaseDirectory, "tmp", "ts-fontrow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "mine.ttf"), BundledBytes());
                TextFonts.Begin(dir);
                TextBoxes.RefreshFonts();
                GuideSpec chosen = t.Parse("text=字;font=mine.ttf;fb=1");
                Program.Check(fill.Visible(chosen), "and is shown when there is one");
                Program.Check(JobArt(chosen).Fallback && !JobArt(t.Parse("text=字;font=mine.ttf;fb=0")).Fallback && JobArt(t.Parse("text=字;font=bundled;fb=1")).IsPlain, "the job's art fills in only for another font whose switch is on, and is plain for the default font");
                Program.Check(JobArt(t.Parse("text=字;font=gone.ttf;fb=1")).IsPlain, "a font that is gone is the default font: nothing to fill");
            }
            finally
            {
                TextFonts.Begin(before);
                TextBoxes.RefreshFonts();
                TextFonts.Release();
                TextFonts.Use(TextFonts.DefaultId, TextLayoutTests.BundledFont());
                Directory.Delete(dir, true);
            }
        }

        private static TextArt JobArt(GuideSpec spec) => TextBoxes.JobOf(spec, out _).Art;
    }
}
