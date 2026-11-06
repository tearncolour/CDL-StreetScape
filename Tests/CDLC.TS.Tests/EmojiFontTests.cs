using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The emoji fonts' data (0.12.78): the cmap of astral code points, the ligatures of GSUB that make one glyph of a sequence, the COLR version 0 layers with CPAL's colours. Synthetic fonts built in the test
    /// check each table's reading against the format; the real fonts of the machine (Segoe UI Emoji and Symbol) are read where they are, and the test says plainly when they are not (it passes then: a font that
    /// is not installed is not a fault).
    /// </summary>
    internal static class EmojiFontTests
    {
        public static void RunAll()
        {
            Program.Run("emoji fonts: a synthetic font's cmap 12, ligatures, COLR and CPAL", Synthetic);
            Program.Run("emoji fonts: a coverage given as ranges", RangeCoverage);
            Program.Run("emoji fonts: damaged tables give nothing and never throw", Damaged);
            Program.Run("emoji mesh: layers, a flag's two letters, the first of a family, the chain of fonts", MeshRules);
            Program.Run("emoji colours: the nearest of the palette", NearestColours);
            Program.Run("emoji board geometry: the layers on a letter", LayersOnLetter);
            Program.Run("emoji fonts: the fonts of this machine", RealFonts);
        }

        // ------------------------------------------------------------------ a font built here

        private sealed class Table
        {
            public string Tag;
            public byte[] Data;
        }

        private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }

        private static void U32(List<byte> b, long v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

        private static void Put16(byte[] b, int at, int v) { b[at] = (byte)(v >> 8); b[at + 1] = (byte)v; }

        /// <summary>One square glyph per code point of the list (glyph ids 1..n), a TrueType font with the tables a reader needs, and the tables the test adds.</summary>
        private static byte[] Font(int[] codepoints, params Table[] extra) => FontWithAdvances(null, codepoints, extra);

        /// <summary>The same with the advance of each glyph (by glyph id, 1000 where the list is short or null).</summary>
        private static byte[] FontWithAdvances(int[] advances, int[] codepoints, params Table[] extra)
        {
            int glyphs = codepoints.Length + 1;
            // glyf: glyph 0 empty, the others a square of 100 units (4 points on curve, one contour).
            var glyf = new List<byte>();
            var loca = new List<int>();
            for (int g = 0; g < glyphs; g++)
            {
                loca.Add(glyf.Count);
                if (g == 0)
                    continue;
                U16(glyf, 1); U16(glyf, 0); U16(glyf, 0); U16(glyf, 100); U16(glyf, 100);
                U16(glyf, 3);
                U16(glyf, 0);
                glyf.Add(1); glyf.Add(1); glyf.Add(1); glyf.Add(1);
                // Delta coordinates of the square (0,0) (100,0) (100,100) (0,100).
                int[] dx = { 0, 100, 0, -100 }, dy = { 0, 0, 100, 0 };
                foreach (int d in dx) U16(glyf, d);
                foreach (int d in dy) U16(glyf, d);
                while (glyf.Count % 4 != 0)
                    glyf.Add(0);
            }

            loca.Add(glyf.Count);
            var locaBytes = new List<byte>();
            foreach (int o in loca)
                U32(locaBytes, o);
            var head = new byte[54];
            Put16(head, 18, 1000);
            Put16(head, 50, 1);
            var maxp = new byte[32];
            Put16(maxp, 4, glyphs);
            var hhea = new byte[36];
            Put16(hhea, 34, glyphs);
            var hmtx = new List<byte>();
            for (int g = 0; g < glyphs; g++)
            {
                U16(hmtx, advances != null && g < advances.Length ? advances[g] : 1000);
                U16(hmtx, 0);
            }

            // cmap format 12: one group per code point.
            var cmap = new List<byte>();
            U16(cmap, 0); U16(cmap, 1); U16(cmap, 3); U16(cmap, 10); U32(cmap, 12);
            U16(cmap, 12); U16(cmap, 0); U32(cmap, 16 + codepoints.Length * 12); U32(cmap, 0); U32(cmap, codepoints.Length);
            // The groups are sorted by code point, as a cmap's are (the glyph ids follow the order the caller gave).
            foreach (int i in Enumerable.Range(0, codepoints.Length).OrderBy(k => codepoints[k]))
            {
                U32(cmap, codepoints[i]); U32(cmap, codepoints[i]); U32(cmap, i + 1);
            }

            var tables = new List<Table>
            {
                new Table { Tag = "cmap", Data = cmap.ToArray() }, new Table { Tag = "glyf", Data = glyf.ToArray() }, new Table { Tag = "head", Data = head }, new Table { Tag = "hhea", Data = hhea },
                new Table { Tag = "hmtx", Data = hmtx.ToArray() }, new Table { Tag = "loca", Data = locaBytes.ToArray() }, new Table { Tag = "maxp", Data = maxp }
            };
            tables.AddRange(extra);
            tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));
            var file = new List<byte>();
            U32(file, 0x00010000); U16(file, tables.Count); U16(file, 0); U16(file, 0); U16(file, 0);
            int offset = 12 + tables.Count * 16;
            foreach (Table t in tables)
            {
                foreach (char c in t.Tag) file.Add((byte)c);
                U32(file, 0); U32(file, offset); U32(file, t.Data.Length);
                offset += (t.Data.Length + 3) & ~3;
            }

            foreach (Table t in tables)
            {
                file.AddRange(t.Data);
                while (file.Count % 4 != 0)
                    file.Add(0);
            }

            return file.ToArray();
        }

        /// <summary>GSUB with one type 4 lookup: ligatures (first glyph, rest glyphs, output glyph).</summary>
        private struct Lig
        {
            public int First, Out;
            public int[] Rest;

            public Lig(int first, int[] rest, int output)
            {
                First = first;
                Rest = rest;
                Out = output;
            }
        }

        private static Table Gsub(bool extension, params Lig[] ligatures) => GsubTable(extension, false, ligatures);

        private static Table GsubRanges(params Lig[] ligatures) => GsubTable(false, true, ligatures);

        private static Table GsubTable(bool extension, bool ranges, Lig[] ligatures)
        {
            var firsts = ligatures.Select(l => l.First).Distinct().OrderBy(x => x).ToList();
            // Subtable: format 1, coverage offset, set count, set offsets; then the coverage; then the sets, each with its ligatures.
            var sub = new List<byte>();
            int header = 6 + firsts.Count * 2;
            var coverage = new List<byte>();
            if (!ranges)
            {
                U16(coverage, 1); U16(coverage, firsts.Count);
                foreach (int f in firsts) U16(coverage, f);
            }
            else
            {
                // Format 2: runs of consecutive glyphs, each with the coverage index of its first.
                var spans = new List<(int Start, int End, int Index)>();
                for (int i = 0; i < firsts.Count; i++)
                {
                    if (spans.Count > 0 && spans[spans.Count - 1].End == firsts[i] - 1)
                        spans[spans.Count - 1] = (spans[spans.Count - 1].Start, firsts[i], spans[spans.Count - 1].Index);
                    else
                        spans.Add((firsts[i], firsts[i], i));
                }

                U16(coverage, 2); U16(coverage, spans.Count);
                foreach (var s in spans) { U16(coverage, s.Start); U16(coverage, s.End); U16(coverage, s.Index); }
            }

            var sets = new List<List<byte>>();
            foreach (int f in firsts)
            {
                var ligs = ligatures.Where(l => l.First == f).ToList();
                var set = new List<byte>();
                U16(set, ligs.Count);
                int at = 2 + ligs.Count * 2;
                var bodies = new List<List<byte>>();
                foreach (var l in ligs)
                {
                    var body = new List<byte>();
                    U16(body, l.Out); U16(body, l.Rest.Length + 1);
                    foreach (int r in l.Rest) U16(body, r);
                    bodies.Add(body);
                }

                foreach (var body in bodies)
                {
                    U16(set, at);
                    at += body.Count;
                }

                foreach (var body in bodies)
                    set.AddRange(body);
                sets.Add(set);
            }

            U16(sub, 1); U16(sub, header); U16(sub, firsts.Count);
            int pos = header + coverage.Count;
            foreach (var set in sets)
            {
                U16(sub, pos);
                pos += set.Count;
            }

            sub.AddRange(coverage);
            foreach (var set in sets) sub.AddRange(set);

            var lookup = new List<byte>();
            if (!extension)
            {
                U16(lookup, 4); U16(lookup, 0); U16(lookup, 1); U16(lookup, 8);
                lookup.AddRange(sub);
            }
            else
            {
                // Type 7: the subtable is an extension (format 1, the real type, a 32-bit offset to the real subtable).
                U16(lookup, 7); U16(lookup, 0); U16(lookup, 1); U16(lookup, 8);
                U16(lookup, 1); U16(lookup, 4); U32(lookup, 8);
                lookup.AddRange(sub);
            }

            var list = new List<byte>();
            U16(list, 1); U16(list, 4);
            list.AddRange(lookup);
            // The header: version, script list, feature list and lookup list offsets; the first two lists are empty (a count of 0 each), so the lookup list follows them at 10 + 4.
            var gsub = new List<byte>();
            U32(gsub, 0x00010000); U16(gsub, 10); U16(gsub, 12); U16(gsub, 14);
            U16(gsub, 0); U16(gsub, 0);
            gsub.AddRange(list);
            var all = gsub;
            return new Table { Tag = "GSUB", Data = all.ToArray() };
        }

        /// <summary>COLR v0 for glyph 1: layers (glyph, palette index); CPAL with the colours (BGRA) of palette 0.</summary>
        private static Table[] Colour(int baseGlyph, (int Glyph, int Palette)[] layers, (byte R, byte G, byte B, byte A)[] colours, int colrVersion = 0)
        {
            var colr = new List<byte>();
            U16(colr, colrVersion); U16(colr, 1); U32(colr, 14); U32(colr, 20); U16(colr, layers.Length);
            U16(colr, baseGlyph); U16(colr, 0); U16(colr, layers.Length);
            foreach (var l in layers) { U16(colr, l.Glyph); U16(colr, l.Palette); }
            var cpal = new List<byte>();
            U16(cpal, 0); U16(cpal, colours.Length); U16(cpal, 1); U16(cpal, colours.Length); U32(cpal, 14); U16(cpal, 0);
            foreach (var c in colours) { cpal.Add(c.B); cpal.Add(c.G); cpal.Add(c.R); cpal.Add(c.A); }
            return new[] { new Table { Tag = "COLR", Data = colr.ToArray() }, new Table { Tag = "CPAL", Data = cpal.ToArray() } };
        }

        private static void Synthetic()
        {
            // Code points: a, the man, the woman, a zero-width joiner, a skin tone, a thumbs up, a heart. Glyph ids follow the order from 1.
            int[] cps = { 'a', 0x1F468, 0x1F469, 0x200D, 0x1F3FD, 0x1F44D, 0x2764 };
            var font = new FontOutlines(Font(cps, Gsub(false, new Lig(6, new[] { 5 }, 1), new Lig(2, new[] { 4, 3 }, 2))), 0);
            // (6 5) is thumbs-up + skin tone: ligature into glyph 1 (any id: the test checks the substitution).
            Program.Check(font.Ok && font.GlyphCount == 8, "the synthetic font reads: " + font.Ok + " " + font.GlyphCount);
            Program.Check(font.GlyphIdOf(0x1F468) == 2 && font.GlyphIdOf('a') == 1 && font.GlyphIdOf(0x2764) == 7 && font.GlyphIdOf(0x1F600) == 0, "the cmap of astral code points (format 12)");
            Program.Check(font.TryGlyphById(2, 0.0, out GlyphOutline outline) && !outline.IsEmpty && outline.MaxX - outline.MinX == 100f, "the outline of a glyph by its id");
            Program.Check(!font.TryGlyphById(0, 0.0, out _) && !font.TryGlyphById(99, 0.0, out _) && !font.TryGlyphById(-1, 0.0, out _), "no outline for the notdef, an id beyond the font, a negative id");
            Program.Check(font.AdvanceOfGlyph(2) == 1000f && font.AdvanceOfGlyph(99) < 0f, "the advance by id");

            List<int> thumb = font.Shape(new[] { 0x1F44D, 0x1F3FD });
            Program.Check(thumb != null && thumb.Count == 1 && thumb[0] == 1, "a ligature: thumbs up and a skin tone are one glyph: " + (thumb == null ? "null" : string.Join(",", thumb)));
            // The ligature (2: 4 3) is the man, a joiner and the woman: glyph 2 again (a ligature of three glyphs).
            List<int> chain = font.Shape(new[] { 0x1F468, 0x200D, 0x1F469 });
            Program.Check(chain != null && chain.Count == 1 && chain[0] == 2, "a ligature of three");
            List<int> none = font.Shape(new[] { 0x1F468, 0x1F44D });
            Program.Check(none != null && none.SequenceEqual(new[] { 2, 6 }), "no ligature: the glyphs stay");
            Program.Check(font.Shape(new[] { 0x1F600 }) == null, "a code point with no glyph: null");
            List<int> selector = font.Shape(new[] { 0x2764, 0xFE0F });
            Program.Check(selector != null && selector.SequenceEqual(new[] { 7 }), "a variation selector the font has no glyph for is left out: " + (selector == null ? "null" : string.Join(",", selector)));
            Program.Check(font.Shape(new int[0]) == null && font.Shape(null) == null, "nothing to shape");

            // The same ligatures reached through an extension lookup (type 7).
            var ext = new FontOutlines(Font(cps, Gsub(true, new Lig(6, new[] { 5 }, 1))), 0);
            List<int> viaExt = ext.Shape(new[] { 0x1F44D, 0x1F3FD });
            Program.Check(viaExt != null && viaExt.Count == 1 && viaExt[0] == 1, "ligatures through the extension lookup");

            // COLR v0 and CPAL.
            var tables = Colour(2, new[] { (3, 0), (4, 1), (5, 0xFFFF) }, new[] { ((byte)250, (byte)10, (byte)20, (byte)255), ((byte)1, (byte)2, (byte)3, (byte)128) });
            var colour = new FontOutlines(Font(cps, tables), 0);
            Program.Check(colour.HasColourLayers && colour.ColrVersion == 0, "colour tables found");
            ColourLayer[] layers = colour.LayersOf(2);
            Program.Check(layers != null && layers.Length == 3, "three layers");
            Program.Check(layers[0].Glyph == 3 && layers[0].R == 250 && layers[0].G == 10 && layers[0].B == 20 && layers[0].A == 255, "the first layer's glyph and colour (CPAL stores BGRA)");
            Program.Check(layers[1].Glyph == 4 && layers[1].R == 1 && layers[1].G == 2 && layers[1].B == 3 && layers[1].A == 128, "the second's, with its alpha");
            Program.Check(layers[2].Glyph == 5 && layers[2].A == 0, "palette entry 0xFFFF is the text colour: alpha 0");
            Program.Check(colour.LayersOf(1) == null && colour.LayersOf(99) == null, "a glyph with no layers: null");
            Program.Check(new FontOutlines(Font(cps), 0).LayersOf(2) == null && !new FontOutlines(Font(cps), 0).HasColourLayers, "a font with no colour tables: null");
            var v1 = new FontOutlines(Font(cps, Colour(2, new[] { (3, 0) }, new[] { ((byte)1, (byte)2, (byte)3, (byte)255) }, 1)), 0);
            Program.Check(v1.ColrVersion == 1 && v1.LayersOf(2) != null && v1.LayersOf(2).Length == 1, "a version 1 table still gives its base glyph records");

            // The last palette entry reads; one index past it does not.
            var palette = new[] { ((byte)1, (byte)2, (byte)3, (byte)255), ((byte)4, (byte)5, (byte)6, (byte)255) };
            ColourLayer[] lastEntry = new FontOutlines(Font(cps, Colour(2, new[] { (3, 1) }, palette)), 0).LayersOf(2);
            Program.Check(lastEntry != null && lastEntry[0].R == 4 && new FontOutlines(Font(cps, Colour(2, new[] { (3, 2) }, palette)), 0).LayersOf(2) == null, "a palette index past the palette: no layers");

            // A ligature of one component is no ligature; ligatures made in steps (a man and a joiner are glyph 7, glyph 7 and a woman are glyph 1) come out whole.
            var single = new FontOutlines(Font(cps, Gsub(false, new Lig(6, new int[0], 1))), 0);
            Program.Check(single.Shape(new[] { 0x1F44D }).SequenceEqual(new[] { 6 }), "a ligature of one component is left alone");
            var steps = new FontOutlines(Font(cps, Gsub(false, new Lig(2, new[] { 4 }, 7), new Lig(7, new[] { 3 }, 1))), 0);
            Program.Check(steps.Shape(new[] { 0x1F468, 0x200D, 0x1F469 }).SequenceEqual(new[] { 1 }), "a family made in two steps");

            // A cmap that points at a glyph the font does not have (its last id is 7): no glyph, so the code point is missing.
            byte[] beyond = Font(cps);
            byte[] group = { 0x00, 0x01, 0xF4, 0x68, 0x00, 0x01, 0xF4, 0x68, 0x00, 0x00, 0x00, 0x02 };
            int at = -1;
            for (int i = 0; i + group.Length <= beyond.Length && at < 0; i++)
            {
                bool same = true;
                for (int k = 0; k < group.Length && same; k++)
                    same = beyond[i + k] == group[k];
                if (same)
                    at = i;
            }

            Program.Check(at > 0, "the man's group is in the cmap");
            beyond[at + 11] = 8;
            var broken = new FontOutlines(beyond, 0);
            Program.Check(broken.Ok && broken.GlyphIdOf(0x1F468) == 0 && broken.Shape(new[] { 0x1F468 }) == null, "a cmap past the glyphs gives none");
        }

        private static void RangeCoverage()
        {
            // The man, the woman, a joiner, a tone, a thumb: glyphs 2, 3, 4, 5, 6 (a is 1). The firsts 2, 5 and 6 are covered as the ranges (2) and (5-6).
            int[] cps = { 'a', 0x1F468, 0x1F469, 0x200D, 0x1F3FD, 0x1F44D };
            var font = new FontOutlines(Font(cps, GsubRanges(new Lig(2, new[] { 4, 3 }, 1), new Lig(5, new[] { 2 }, 3), new Lig(6, new[] { 5 }, 4))), 0);
            Program.Check(font.Shape(new[] { 0x1F468, 0x200D, 0x1F469 }).SequenceEqual(new[] { 1 }), "the first range: man, joiner, woman");
            Program.Check(font.Shape(new[] { 0x1F3FD, 0x1F468 }).SequenceEqual(new[] { 3 }), "the second range's first glyph");
            Program.Check(font.Shape(new[] { 0x1F44D, 0x1F3FD }).SequenceEqual(new[] { 4 }), "the second range's last glyph");
            Program.Check(font.Shape(new[] { 0x1F469, 0x1F44D }).SequenceEqual(new[] { 3, 6 }), "a glyph the ranges do not cover is left alone");
        }

        private static void Damaged()
        {
            int[] cps = { 'a', 0x1F468, 0x1F469, 0x200D, 0x1F3FD };
            byte[] good = Font(cps, Colour(2, new[] { (3, 0), (4, 1) }, new[] { ((byte)1, (byte)2, (byte)3, (byte)255), ((byte)4, (byte)5, (byte)6, (byte)255) }).Concat(new[] { Gsub(false, new Lig(2, new[] { 5 }, 1)) }).ToArray());
            var font = new FontOutlines(good, 0);
            Program.Check(font.Ok && font.LayersOf(2) != null && font.Shape(new[] { 0x1F468, 0x1F3FD }) != null, "the undamaged font reads");

            // Every truncation and a sample of byte flips: nothing throws.
            var random = new Random(12);
            int made = 0, readable = 0;
            for (int cut = 0; cut < good.Length; cut += 7)
            {
                try
                {
                    var f = new FontOutlines(good.Take(cut).ToArray(), 0);
                    made++;
                    if (f.Ok)
                    {
                        readable++;
                        f.LayersOf(2);
                        f.Shape(new[] { 0x1F468, 0x1F3FD });
                        f.Shape(new[] { 0x1F469, 0x200D });
                    }
                }
                catch (Exception ex)
                {
                    Program.Check(false, "a font cut at " + cut + " threw " + ex.GetType().Name);
                    return;
                }
            }

            for (int i = 0; i < 400; i++)
            {
                byte[] bent = (byte[])good.Clone();
                for (int k = 0; k < 6; k++)
                    bent[random.Next(bent.Length)] = (byte)random.Next(256);
                try
                {
                    var f = new FontOutlines(bent, 0);
                    if (f.Ok)
                    {
                        f.LayersOf(2);
                        f.LayersOf(random.Next(0, 70000));
                        f.Shape(new[] { 0x1F468, 0x1F3FD });
                        f.TryGlyphById(random.Next(-5, 70), 0.0, out _);
                    }
                }
                catch (Exception ex)
                {
                    Program.Check(false, "a damaged font threw " + ex.GetType().Name + ": " + ex.Message);
                    return;
                }
            }

            Program.Check(made > 10, "truncations tried: " + made + ", of which " + readable + " still read");
        }

        // ------------------------------------------------------------------ emoji made from a font built here

        private static string Cluster(params int[] scalars) => string.Concat(scalars.Select(char.ConvertFromUtf32));

        private static void MeshRules()
        {
            // a = 1, the man = 2, the woman = 3, a joiner = 4, a tone = 5, regional indicators C and N = 6 and 7; every glyph a square of 0.1 em with an advance of 1 em.
            int[] cps = { 'a', 0x1F468, 0x1F469, 0x200D, 0x1F3FD, 0x1F1E8, 0x1F1F3 };
            int bucket = TextGlyphs.Bucket(0.002f);
            var colour = new FontOutlines(Font(cps, Colour(2, new[] { (3, 0), (4, 1), (5, 0xFFFF) }, new[] { ((byte)250, (byte)10, (byte)20, (byte)255), ((byte)1, (byte)2, (byte)3, (byte)128) })), 0);
            var plain = new FontOutlines(Font(cps), 0);

            GlyphMesh letter = EmojiMesh.Build(plain, "a", bucket);
            Program.Check(letter != null && letter.Emoji == null && letter.Area > 0f, "a glyph with no layers is its outline");
            GlyphMesh man = EmojiMesh.Build(colour, Cluster(0x1F468), bucket);
            Program.Check(man != null && man.Emoji != null && man.Emoji.Layers.Count == 3, "a glyph with layers has them");
            EmojiLayer[] layers = man.Emoji.Layers.ToArray();
            Program.Check(layers[0].R == 250 && layers[0].G == 10 && layers[0].B == 20 && layers[0].Colour == EmojiColours.Nearest(250, 10, 20), "the first layer is its colour's nearest in the palette");
            Program.Check(layers[1].R == 1 && layers[1].G == 2 && layers[1].B == 3 && layers[1].Colour == EmojiColours.Nearest(1, 2, 3), "the second");
            Program.Check(layers[2].Colour == SolidColour.GlowWarm && EmojiColours.Nearest(255, 255, 255) != SolidColour.GlowWarm, "a layer in the text's colour is lit in the board's own colour, not the palette's white");
            Program.Near(man.Area, EmojiMesh.Build(plain, Cluster(0x1F468), bucket).Area, man.Area * 0.01f, "the silhouette is the union of the layers (traced from a raster: within a hundredth)");
            Program.Check(EmojiMesh.Build(colour, Cluster(0x1F468, 0xFE0F), bucket).Emoji.Layers.Count == 3, "a selector the font has no glyph for draws nothing");

            // A family: three glyphs, drawn as the first (COLR version 1 puts them in one em box, which is not read).
            GlyphMesh family = EmojiMesh.Build(colour, Cluster(0x1F468, 0x200D, 0x1F469), bucket);
            Program.Check(family != null && family.Emoji != null && family.Emoji.Layers.Count == 3, "a family is its first glyph: " + (family?.Emoji == null ? "no layers" : family.Emoji.Layers.Count + " layers"));
            Program.Near(family.Area, man.Area, 1e-6f, "of the first glyph's size");

            // A flag: its two letters side by side, each half of the cell wide, and no more than two whatever the run.
            GlyphMesh flag = EmojiMesh.Build(colour, Cluster(0x1F1E8, 0x1F1F3), bucket);
            Program.Check(flag != null && flag.Emoji != null && flag.Emoji.Layers.Count == 2 && flag.Loops.Count == 2, "a flag is two letters");
            Program.Check(flag.Emoji.Layers[0].Mesh.Points.Max(p => p.X) <= 0.06f && flag.Emoji.Layers[1].Mesh.Points.Min(p => p.X) >= 0.49f && flag.Emoji.Layers[1].Mesh.Points.Max(p => p.X) <= 0.56f, "the left one in the left half, the right one in the right half");
            Program.Near(flag.Area, letter.Area / 2f, letter.Area * 0.01f, "each letter shrunk to the half: a quarter of the area, two of them");
            // A letter narrower than its slot (0.3 em of the 0.5) is in the slot's middle.
            var uneven = new FontOutlines(FontWithAdvances(new[] { 1000, 1000, 1000, 1000, 1000, 1000, 300, 1000 }, cps), 0);
            GlyphMesh narrow = EmojiMesh.Build(uneven, Cluster(0x1F1E8, 0x1F1F3), bucket);
            Program.Near(narrow.Emoji.Layers[0].Mesh.Points.Min(p => p.X), 0.1f, 1e-4f, "the narrow letter is moved by half of what the slot has over");
            Program.Near(narrow.Emoji.Layers[1].Mesh.Points.Min(p => p.X), 0.5f, 1e-4f, "the one that fills its slot is at its start");
            GlyphMesh three = EmojiMesh.Build(colour, Cluster(0x1F1E8, 0x1F1F3, 0x1F1E8), bucket);
            Program.Check(three != null && three.Emoji.Layers.Count == 2, "at most two letters side by side");

            // Nothing: no glyph, no cluster, no font.
            Program.Check(EmojiMesh.Build(colour, Cluster(0x1F600), bucket) == null && EmojiMesh.Build(colour, "", bucket) == null && EmojiMesh.Build(null, "a", bucket) == null, "a cluster the font cannot draw is null");

            // Too many layers: the biggest kept, at most MaxLayers.
            var many = new FontOutlines(Font(cps, Colour(2, Enumerable.Repeat((3, 0), 30).ToArray(), new[] { ((byte)250, (byte)10, (byte)20, (byte)255) })), 0);
            Program.Check(EmojiMesh.Build(many, Cluster(0x1F468), bucket).Emoji.Layers.Count == EmojiPlan.MaxLayers, "thirty layers are cut to " + EmojiPlan.MaxLayers);

            // The chain: the first font that draws the cluster, passing over one that lacks the code point.
            var small = new FontOutlines(Font(new[] { (int)'a' }), 0);
            Program.Check(EmojiMesh.FromChain(new[] { colour, plain }, Cluster(0x1F468), bucket).Emoji != null, "the colour font first: its layers");
            Program.Check(EmojiMesh.FromChain(new[] { plain, colour }, Cluster(0x1F468), bucket).Emoji == null, "the plain font first: its outline");
            GlyphMesh passed = EmojiMesh.FromChain(new[] { small, colour }, Cluster(0x1F468), bucket);
            Program.Check(passed != null && passed.Emoji != null, "a font without the code point is passed over");
            Program.Check(EmojiMesh.FromChain(new[] { small }, Cluster(0x1F468), bucket) == null && EmojiMesh.FromChain(new FontOutlines[0], "a", bucket) == null && EmojiMesh.FromChain(null, "a", bucket) == null, "no font draws it: null");
        }

        private static void NearestColours()
        {
            foreach (SolidColour c in EmojiColours.Palette)
            {
                byte[] rgb = CDLC.TS.Geo.Raster.SolidRgb(c);
                Program.Check(EmojiColours.Nearest(rgb[0], rgb[1], rgb[2]) == c, c + " is its own nearest");
            }

            Program.Check(EmojiColours.Nearest(250, 250, 250) == SolidColour.GlowWhite, "white is the white glow");
            Program.Check(EmojiColours.Nearest(12, 12, 12) == SolidColour.Black, "near black is black");
            Program.Check(EmojiColours.Nearest(255, 20, 20) == SolidColour.GlowRed, "a red is the red glow");
            // The weights: the red-and-blue weights follow how red the pair is, green counts double.
            Program.Near((float)EmojiColours.Distance(100, 0, 0, 0, 0, 0), 21953.125f, 0.01f, "red alone");
            Program.Near((float)EmojiColours.Distance(0, 10, 0, 0, 0, 0), 400f, 1e-3f, "green alone");
            Program.Near((float)EmojiColours.Distance(0, 0, 10, 0, 0, 0), 299.609375f, 1e-3f, "blue alone");
            Program.Check(EmojiColours.Distance(40, 90, 200, 40, 90, 200) == 0.0 && EmojiColours.Distance(10, 20, 30, 200, 100, 50) == EmojiColours.Distance(200, 100, 50, 10, 20, 30), "nothing from itself; the same either way round");
        }

        private static void LayersOnLetter()
        {
            int[] cps = { 'a', 0x1F468, 0x1F469 };
            int bucket = TextGlyphs.Bucket(0.002f);
            var colour = new FontOutlines(Font(cps, Colour(2, new[] { (3, 0), (3, 1), (3, 0) }, new[] { ((byte)250, (byte)10, (byte)20, (byte)255), ((byte)1, (byte)2, (byte)3, (byte)255) })), 0);
            var solids = new Dictionary<SolidColour, MeshBuilder>();
            Func<SolidColour, MeshBuilder> solid = c => solids.TryGetValue(c, out MeshBuilder m) ? m : solids[c] = new MeshBuilder();

            GlyphMesh man = EmojiMesh.Build(colour, Cluster(0x1F468), bucket);
            var back = new MeshBuilder();
            const float mpe = 0.5f, x = 0.3f, y = -0.2f, angle = 0.7f, zBack = 0f, zFront = 0.06f;
            man.AddEmojiTo(solid, back, mpe, x, y, zBack, zFront, true, angle);
            int count = man.Emoji.Layers.Count;
            Program.Check(count == 3, "three layers");
            float step = EmojiPlan.LayerStep;
            // The layers in their planes: the last in the face's plane, each one before it a step behind, placed as a letter is.
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            for (int i = 0; i < count; i++)
            {
                EmojiLayer layer = man.Emoji.Layers[i];
                Point2 p = layer.Mesh.Points[0];
                var expected = new Vec3(-(x + (cos * p.X - sin * p.Y) * mpe), y + (sin * p.X + cos * p.Y) * mpe, zFront - (count - 1 - i) * step);
                Program.Check(solids[layer.Colour].Positions.Any(q => Math.Abs(q.X - expected.X) < 1e-5f && Math.Abs(q.Y - expected.Y) < 1e-5f && Math.Abs(q.Z - expected.Z) < 1e-6f), "layer " + i + ": its first point where a letter's would be, in its plane");
            }

            int faceVertices = solids.Values.Sum(m => m.VertexCount), faceIndices = solids.Values.Sum(m => m.Indices.Count);
            Program.Check(faceVertices == man.Emoji.Layers.Sum(l => l.Mesh.Points.Length) && faceIndices == man.Emoji.Layers.Sum(l => l.Mesh.Triangles.Length), "every point and every triangle of every layer: " + faceVertices + " vertices, " + faceIndices + " indices");
            Program.Check(solids.Values.All(m => m.Normals.All(n => n.X == 0f && n.Y == 0f && n.Z == 1f)), "the faces look at the viewer");
            Program.Check(solids.Count == 2 && solids.ContainsKey(man.Emoji.Layers[0].Colour) && solids.ContainsKey(man.Emoji.Layers[1].Colour), "each layer in the solid of its colour");

            // What it counts: the layers' faces instead of the silhouette's, and the layers in what a return may come to.
            int layerVertices = man.Emoji.Layers.Sum(l => l.Mesh.Points.Length), layerTriangles = man.Emoji.Layers.Sum(l => l.Mesh.Triangles.Length / 3);
            Program.Check(man.Emoji.FaceVertices == layerVertices && man.Emoji.FaceTriangles == layerTriangles, "the plan counts its layers");
            Program.Check(man.Vertices == man.FaceVertices + man.RimVertices + layerVertices && man.TriangleCount == man.FaceTriangles + man.RimTriangles + layerTriangles, "the glyph counts the layers as its face");
            Program.Check(man.ReturnVertices == man.FaceVertices + man.RimVertices + layerVertices, "and in what its return may come to");
            Program.Check(man.Emoji.Bytes >= man.Emoji.Layers.Sum(l => l.Mesh.Bytes) && man.Bytes >= man.Emoji.Bytes + 160, "and in its bytes");
            GlyphMesh letterOnly = EmojiMesh.Build(colour, "a", bucket);
            Program.Check(letterOnly.Vertices == 2 * letterOnly.FaceVertices + letterOnly.RimVertices && letterOnly.ReturnVertices == letterOnly.FaceVertices + letterOnly.RimVertices, "a plain glyph counts as it did");

            // The back and the rim are those of any letter: one layer that is the whole glyph builds what AddTo builds, whether the rim smooths or not, and a second glyph on the same builders goes after the first.
            var ring = new List<Point2>();
            for (int i = 0; i < 24; i++)
                ring.Add(new Point2((float)(0.3 * Math.Cos(i * Math.PI / 12)), (float)(0.3 * Math.Sin(i * Math.PI / 12))));
            GlyphMesh round = TextGlyphs.FromLoops(new List<List<Point2>> { ring }, 1f);
            round.Emoji = new EmojiPlan();
            round.Emoji.Layers.Add(new EmojiLayer { Mesh = round, Colour = SolidColour.GlowWarm });
            foreach (bool smooth in new[] { true, false })
            {
                var emojiFace = new MeshBuilder();
                var emojiBack = new MeshBuilder();
                var plainFace = new MeshBuilder();
                var plainBack = new MeshBuilder();
                foreach (float at in new[] { 0f, 1.2f })
                {
                    round.AddEmojiTo(c => emojiFace, emojiBack, mpe, x + at, y, zBack, zFront, smooth, angle);
                    round.AddTo(plainFace, plainBack, mpe, x + at, y, zBack, zFront, smooth, angle);
                }

                Program.Check(emojiFace.Positions.SequenceEqual(plainFace.Positions) && emojiFace.Indices.SequenceEqual(plainFace.Indices) && emojiFace.Normals.SequenceEqual(plainFace.Normals), "one layer that is the glyph: the same face as a letter's (smooth rim " + smooth + ")");
                Program.Check(emojiBack.Positions.Count > 0 && emojiBack.Positions.SequenceEqual(plainBack.Positions) && emojiBack.Indices.SequenceEqual(plainBack.Indices) && emojiBack.Normals.SequenceEqual(plainBack.Normals), "and the same back and rim (smooth rim " + smooth + ")");
            }

            // Many layers on a shallow letter stay inside a third of it, each in a plane of its own.
            var layered = new FontOutlines(Font(cps, Colour(2, Enumerable.Repeat((3, 0), 24).ToArray(), new[] { ((byte)250, (byte)10, (byte)20, (byte)255) })), 0);
            GlyphMesh deep = EmojiMesh.Build(layered, Cluster(0x1F468), bucket);
            var shallow = new MeshBuilder();
            deep.AddEmojiTo(c => shallow, new MeshBuilder(), mpe, 0f, 0f, 0f, 0.03f, true, 0f);
            float[] planes = shallow.Positions.Select(p => (float)Math.Round(p.Z, 6)).Distinct().ToArray();
            Program.Check(deep.Emoji.Layers.Count == 24 && planes.Length == 24, "twenty-four layers in twenty-four planes: " + planes.Length);
            Program.Check(planes.Min() >= 0.02f - 1e-6f && planes.Max() <= 0.03f + 1e-6f, "inside the front third of the letter: " + planes.Min() + " to " + planes.Max());
        }

        // ------------------------------------------------------------------ the fonts of this machine

        internal static string SystemFont(string name)
        {
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "c:/Windows/Fonts" })
            {
                try
                {
                    if (!string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, name)))
                        return Path.Combine(folder, name);
                }
                catch (Exception)
                {
                    // A folder that cannot be asked is not there.
                }
            }

            return null;
        }

        private static void RealFonts()
        {
            string path = SystemFont("seguiemj.ttf");
            if (path == null)
            {
                Console.WriteLine("  (Segoe UI Emoji is not on this machine: its tests are skipped)");
                return;
            }

            var font = new FontOutlines(File.ReadAllBytes(path), 0);
            Program.Check(font.Ok && font.GlyphCount > 60000, "Segoe UI Emoji reads: " + font.GlyphCount + " glyphs");
            Program.Check(font.HasColourLayers && font.ColrVersion >= 0, "it has COLR (version " + font.ColrVersion + ") and CPAL");
            int grin = font.GlyphIdOf(0x1F600);
            Program.Check(grin > 0, "the grinning face has a glyph");
            ColourLayer[] layers = font.LayersOf(grin);
            Program.Check(layers != null && layers.Length >= 3 && layers.All(l => l.A > 0), "its colour layers (version 0 records): " + (layers == null ? 0 : layers.Length));
            Program.Check(layers != null && layers.All(l => font.TryGlyphById(l.Glyph, 0.0, out GlyphOutline o) && !o.IsEmpty), "each layer's glyph has an outline");
            List<int> family = font.Shape(new[] { 0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467 });
            Program.Check(family != null && family.Count < 5, "a family is substituted by the font's ligatures: " + (family == null ? "null" : family.Count + " glyph(s)"));
            List<int> thumb = font.Shape(new[] { 0x1F44D, 0x1F3FD });
            Program.Check(thumb != null && thumb.Count == 1, "a thumbs up with a skin tone is one glyph");
            List<int> technologist = font.Shape(new[] { 0x1F469, 0x1F3FD, 0x200D, 0x1F4BB });
            Program.Check(technologist != null && technologist.Count == 1, "a woman technologist with a skin tone is one glyph");
            Program.Check(font.GlyphIdOf(0x20000) == 0, "no Han characters of the extension plane in an emoji font");

            string symbol = SystemFont("seguisym.ttf");
            if (symbol == null)
                return;
            var sym = new FontOutlines(File.ReadAllBytes(symbol), 0);
            Program.Check(sym.Ok && sym.GlyphIdOf(0x1F600) > 0 && sym.GlyphIdOf(0x2764) > 0 && sym.GlyphIdOf(0x1F525) > 0, "Segoe UI Symbol has the plain outlines of the commonest emoji");
            Program.Check(!sym.HasColourLayers && sym.TryGlyph(0x1F600, out GlyphOutline face) && !face.IsEmpty, "and they are monochrome");
        }
    }
}
