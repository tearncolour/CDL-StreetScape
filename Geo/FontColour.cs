using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>One layer of a colour glyph: the glyph whose outline it is, and its colour (RGBA, from CPAL; alpha 255 is opaque).</summary>
    public readonly struct ColourLayer
    {
        public readonly int Glyph;
        public readonly byte R, G, B, A;

        public ColourLayer(int glyph, byte r, byte g, byte b, byte a)
        {
            Glyph = glyph;
            R = r;
            G = g;
            B = b;
            A = a;
        }
    }

    /// <summary>
    /// What a colour emoji font gives beyond the outlines (0.12.78; the owner, 2 Nov: "支持Emoji"): the glyph a code point has in the cmap (the glyph id, whatever it draws), the ligatures of GSUB that make
    /// one glyph of an emoji sequence (a skin tone, a family, a profession: GSUB lookup type 4, also reached through the extension lookup type 7), and COLR version 0 layers with the colours of palette 0 of
    /// CPAL. COLR version 1 (a paint graph with gradients, transforms and composite modes: Segoe UI Emoji of Windows 11 has it) is NOT read: a font that has it still has the version 0 records for its
    /// base glyphs, and these are what is read. Every offset is checked: a damaged table gives nothing and never throws. Reading, on a font that is shared between threads, changes nothing: the tables are
    /// parsed once, on first use, under a lock.
    /// </summary>
    public sealed partial class FontOutlines
    {
        private int m_ColrAt, m_ColrLength, m_CpalAt, m_CpalLength, m_GsubAt, m_GsubLength;
        private bool m_ColourRead, m_LigaturesRead;
        private readonly object m_ColourLock = new object();
        private readonly Dictionary<int, ColourLayer[]> m_LayerCache = new Dictionary<int, ColourLayer[]>();
        private int m_ColrBaseCount, m_ColrBaseAt, m_ColrLayerAt, m_ColrLayerCount, m_PaletteAt, m_PaletteEntries;
        private List<Ligature> m_Ligatures;
        private Dictionary<int, List<Ligature>> m_LigaturesByFirst;

        /// <summary>The COLR table's version (0 or 1), or -1 when the font has none.</summary>
        public int ColrVersion => m_ColrAt > 0 && m_ColrLength >= 14 ? U16(m_ColrAt) : -1;

        public bool HasColourLayers => m_ColrAt > 0 && m_CpalAt > 0;

        /// <summary>How many glyphs the font has.</summary>
        public int GlyphCount => m_GlyphCount;

        private struct Ligature
        {
            public int First, Out;
            public int[] Rest;
        }

        private void NoteColourTable(string tag, int offset, int length)
        {
            if (tag == "COLR")
            {
                m_ColrAt = offset;
                m_ColrLength = length;
            }
            else if (tag == "CPAL")
            {
                m_CpalAt = offset;
                m_CpalLength = length;
            }
            else if (tag == "GSUB")
            {
                m_GsubAt = offset;
                m_GsubLength = length;
            }
        }

        /// <summary>The glyph id the cmap gives a code point; 0 when the font has none.</summary>
        public int GlyphIdOf(int codepoint)
        {
            if (!Ok)
                return 0;
            int gid = GlyphOf(codepoint);
            return gid > 0 && gid < m_GlyphCount ? gid : 0;
        }

        /// <summary>The outline of a glyph by its id (not through the cmap); false for an id the font does not have. Cut at the flatness (font units), 0 for the default cut. Not cached here.</summary>
        public bool TryGlyphById(int gid, double flatness, out GlyphOutline glyph)
        {
            glyph = null;
            if (!Ok || gid <= 0 || gid >= m_GlyphCount)
                return false;
            try
            {
                glyph = m_TrueType ? ReadTrueType(gid, flatness) : Read(gid);
            }
            catch (Exception)
            {
                glyph = null;
            }

            return glyph != null;
        }

        /// <summary>The advance of a glyph by its id in font units; -1 for an id the font does not have.</summary>
        public float AdvanceOfGlyph(int gid)
        {
            if (!Ok || gid <= 0 || gid >= m_GlyphCount)
                return -1f;
            if (!m_TrueType)
                return TryGlyphById(gid, 0.0, out GlyphOutline glyph) ? glyph.Advance : -1f;
            return AdvanceOf(gid);
        }

        // ------------------------------------------------------------------ ligatures

        /// <summary>
        /// The glyphs of a run of code points after the font's ligatures: each code point is its glyph, and wherever the font has a ligature for the glyphs that follow (a base and a skin tone, a person, a
        /// joiner and another person), they are one. Null when some code point has no glyph in the font. A zero-width joiner or a selector that no ligature took stays in the run as its own glyph (the caller
        /// leaves out what draws nothing).
        /// </summary>
        public List<int> Shape(IList<int> codepoints)
        {
            if (!Ok || codepoints == null || codepoints.Count == 0)
                return null;
            var run = new List<int>(codepoints.Count);
            foreach (int c in codepoints)
            {
                int gid = GlyphIdOf(c);
                if (gid == 0)
                {
                    // A selector or a joiner a font has no glyph for does not stop the run: it draws nothing.
                    if (c == TextClusters.Vs16 || c == TextClusters.Vs15 || c == TextClusters.Zwj || TextClusters.IsVariationSelector(c))
                        continue;
                    return null;
                }

                run.Add(gid);
            }

            if (!EnsureLigatures() || m_Ligatures.Count == 0)
                return run;
            // The lookups are tried in the font's order, repeatedly, as long as one changes the run (a family is made in steps).
            for (int pass = 0; pass < 8; pass++)
            {
                if (!Substitute(run))
                    break;
            }

            return run;
        }

        private bool Substitute(List<int> run)
        {
            bool changed = false;
            for (int i = 0; i < run.Count; i++)
            {
                if (!m_LigaturesByFirst.TryGetValue(run[i], out List<Ligature> candidates))
                    continue;
                foreach (Ligature lig in candidates)
                {
                    if (i + 1 + lig.Rest.Length > run.Count)
                        continue;
                    bool match = true;
                    for (int k = 0; k < lig.Rest.Length && match; k++)
                        match = run[i + 1 + k] == lig.Rest[k];
                    if (!match)
                        continue;
                    run.RemoveRange(i + 1, lig.Rest.Length);
                    run[i] = lig.Out;
                    changed = true;
                    break;
                }
            }

            return changed;
        }

        private bool EnsureLigatures()
        {
            lock (m_ColourLock)
            {
                if (m_LigaturesRead)
                    return m_Ligatures != null;
                m_LigaturesRead = true;
                var found = new List<Ligature>();
                try
                {
                    if (m_GsubAt > 0 && m_GsubLength > 10)
                        ReadGsubLigatures(found);
                }
                catch (Exception)
                {
                    found.Clear();
                }

                // Longest first: a family of four before a family of three.
                found.Sort((a, b) => b.Rest.Length.CompareTo(a.Rest.Length));
                m_Ligatures = found;
                m_LigaturesByFirst = new Dictionary<int, List<Ligature>>();
                foreach (Ligature lig in found)
                {
                    if (!m_LigaturesByFirst.TryGetValue(lig.First, out List<Ligature> list))
                        m_LigaturesByFirst[lig.First] = list = new List<Ligature>();
                    list.Add(lig);
                }

                return true;
            }
        }

        private bool InGsub(int at, int size) => at >= m_GsubAt && at + size <= m_GsubAt + m_GsubLength;

        private void ReadGsubLigatures(List<Ligature> into)
        {
            int lookupList = m_GsubAt + U16(m_GsubAt + 8);
            if (!InGsub(lookupList, 2))
                return;
            int lookups = U16(lookupList);
            for (int i = 0; i < lookups && into.Count < 200000; i++)
            {
                int lookup = lookupList + U16(lookupList + 2 + i * 2);
                if (!InGsub(lookup, 6))
                    continue;
                int type = U16(lookup), count = U16(lookup + 4);
                for (int s = 0; s < count; s++)
                {
                    int sub = lookup + U16(lookup + 6 + s * 2), subType = type;
                    if (!InGsub(sub, 8))
                        continue;
                    if (type == 7)
                    {
                        // An extension subtable names the real one: format, its type, a 32-bit offset from here.
                        subType = U16(sub + 2);
                        sub += (int)U32(sub + 4);
                        if (!InGsub(sub, 8))
                            continue;
                    }

                    if (subType == 4)
                        ReadLigatureSubtable(sub, into);
                }
            }
        }

        private void ReadLigatureSubtable(int sub, List<Ligature> into)
        {
            if (U16(sub) != 1)
                return;
            int coverageAt = sub + U16(sub + 2), sets = U16(sub + 4);
            List<int> covered = ReadCoverage(coverageAt);
            if (covered == null)
                return;
            for (int k = 0; k < sets && k < covered.Count; k++)
            {
                int set = sub + U16(sub + 6 + k * 2);
                if (!InGsub(set, 2))
                    continue;
                int ligs = U16(set);
                for (int j = 0; j < ligs; j++)
                {
                    int lig = set + U16(set + 2 + j * 2);
                    if (!InGsub(lig, 4))
                        continue;
                    int output = U16(lig), components = U16(lig + 2);
                    if (components < 2 || components > 16 || !InGsub(lig, 4 + (components - 1) * 2))
                        continue;
                    var rest = new int[components - 1];
                    for (int q = 0; q < rest.Length; q++)
                        rest[q] = U16(lig + 4 + q * 2);
                    into.Add(new Ligature { First = covered[k], Out = output, Rest = rest });
                }
            }
        }

        /// <summary>The glyph ids of a coverage table in the order of their coverage index; null when it does not fit the table.</summary>
        private List<int> ReadCoverage(int at)
        {
            if (!InGsub(at, 4))
                return null;
            int format = U16(at), count = U16(at + 2);
            var glyphs = new List<int>();
            if (format == 1)
            {
                if (!InGsub(at, 4 + count * 2))
                    return null;
                for (int i = 0; i < count; i++)
                    glyphs.Add(U16(at + 4 + i * 2));
                return glyphs;
            }

            if (format != 2 || !InGsub(at, 4 + count * 6))
                return null;
            for (int i = 0; i < count; i++)
            {
                int start = U16(at + 4 + i * 6), end = U16(at + 6 + i * 6), first = U16(at + 8 + i * 6);
                // The coverage index of glyph g is first + (g - start): the list is filled up to the index of the last.
                while (glyphs.Count < first)
                    glyphs.Add(0);
                for (int g = start; g <= end && glyphs.Count < 70000; g++)
                    glyphs.Add(g);
            }

            return glyphs;
        }

        // ------------------------------------------------------------------ colour layers

        /// <summary>
        /// The layers of a glyph by COLR version 0, bottom first, each with its colour from palette 0 of CPAL; null when the glyph has none (it is drawn as it is) or the font has no colour tables. A
        /// layer whose palette entry is 0xFFFF is the text's own colour: it is given alpha 0 and a colour of white, and the caller draws it in the face colour.
        /// </summary>
        public ColourLayer[] LayersOf(int gid)
        {
            if (!Ok || !HasColourLayers)
                return null;
            lock (m_ColourLock)
            {
                if (m_LayerCache.TryGetValue(gid, out ColourLayer[] kept))
                    return kept;
                ColourLayer[] made = null;
                try
                {
                    if (ReadColrHeader())
                        made = ReadLayers(gid);
                }
                catch (Exception)
                {
                    made = null;
                }

                m_LayerCache[gid] = made;
                return made;
            }
        }

        private bool ReadColrHeader()
        {
            if (m_ColourRead)
                return m_ColrBaseCount > 0;
            m_ColourRead = true;
            if (m_ColrLength < 14 || m_CpalLength < 12)
                return false;
            m_ColrBaseCount = U16(m_ColrAt + 2);
            m_ColrBaseAt = m_ColrAt + (int)U32(m_ColrAt + 4);
            m_ColrLayerAt = m_ColrAt + (int)U32(m_ColrAt + 8);
            m_ColrLayerCount = U16(m_ColrAt + 12);
            m_PaletteEntries = U16(m_CpalAt + 2);
            int firstColour = (int)U32(m_CpalAt + 8);
            int palettes = U16(m_CpalAt + 4);
            if (palettes < 1 || m_ColrBaseAt < m_ColrAt || m_ColrBaseAt + (long)m_ColrBaseCount * 6 > m_ColrAt + m_ColrLength || m_ColrLayerAt < m_ColrAt || m_ColrLayerAt + (long)m_ColrLayerCount * 4 > m_ColrAt + m_ColrLength)
            {
                m_ColrBaseCount = 0;
                return false;
            }

            m_PaletteAt = m_CpalAt + firstColour;
            if (m_PaletteAt < m_CpalAt || m_PaletteAt + (long)m_PaletteEntries * 4 > m_CpalAt + m_CpalLength)
            {
                m_ColrBaseCount = 0;
                return false;
            }

            return m_ColrBaseCount > 0;
        }

        private ColourLayer[] ReadLayers(int gid)
        {
            int lo = 0, hi = m_ColrBaseCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2, record = m_ColrBaseAt + mid * 6, id = U16(record);
                if (gid < id)
                    hi = mid - 1;
                else if (gid > id)
                    lo = mid + 1;
                else
                {
                    int first = U16(record + 2), count = U16(record + 4);
                    if (count < 1 || count > 256 || first + count > m_ColrLayerCount)
                        return null;
                    var layers = new ColourLayer[count];
                    for (int i = 0; i < count; i++)
                    {
                        int at = m_ColrLayerAt + (first + i) * 4, glyph = U16(at), palette = U16(at + 2);
                        if (palette == 0xFFFF)
                        {
                            layers[i] = new ColourLayer(glyph, 255, 255, 255, 0);
                            continue;
                        }

                        if (palette >= m_PaletteEntries)
                            return null;
                        int colour = m_PaletteAt + palette * 4;
                        layers[i] = new ColourLayer(glyph, m_Data[colour + 2], m_Data[colour + 1], m_Data[colour], m_Data[colour + 3]);
                    }

                    return layers;
                }
            }

            return null;
        }
    }
}
