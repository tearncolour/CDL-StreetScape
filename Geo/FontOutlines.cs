using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>The outline of one glyph as closed polylines in font units (y up), curves already cut into short segments, and its advance.</summary>
    public sealed class GlyphOutline
    {
        /// <summary>Each contour is x, y, x, y ... (not repeated at the end).</summary>
        public readonly List<float[]> Contours = new List<float[]>();

        public float Advance;
        public float MinX, MinY, MaxX, MaxY;

        public bool IsEmpty => Contours.Count == 0;

        internal void Measure()
        {
            MinX = MinY = float.MaxValue;
            MaxX = MaxY = float.MinValue;
            foreach (float[] c in Contours)
            {
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    MinX = Math.Min(MinX, c[i]);
                    MaxX = Math.Max(MaxX, c[i]);
                    MinY = Math.Min(MinY, c[i + 1]);
                    MaxY = Math.Max(MaxY, c[i + 1]);
                }
            }

            if (MinX > MaxX)
                MinX = MinY = MaxX = MaxY = 0f;
        }
    }

    /// <summary>
    /// Glyph outlines read from an OpenType font: the cmap (formats 4 and 12), and the outlines either from a CFF table with its Type 2 charstrings (CID-keyed or not, with local and global subroutines,
    /// hints skipped, flex curves) or, since 0.12.28, from TrueType glyf and loca (the bundled traffic sign font is TrueType: quadratic curves, composite glyphs), cut into polylines. This is what the
    /// markings' words and figures are made of, so that no picture of a word is stored: 停, 公交, 铁路, the digits of a speed limit are the font's own outlines scaled into the box the standard gives. Pure;
    /// nothing is written, and a damaged font gives no glyph, not an exception.
    /// </summary>
    public sealed partial class FontOutlines
    {
        private readonly byte[] m_Data;
        private readonly int m_Face;
        private int m_CffOffset, m_CffLength;
        private int m_CharStringsAt;
        private int[] m_CharStringOffsets;
        private int m_GlyphCount;
        private int[] m_GlobalSubrs;
        private int m_GlobalBias;
        private FontDict[] m_Dicts;
        private byte[] m_FdOfGlyph;
        private readonly List<CmapRange> m_Ranges = new List<CmapRange>();

        /// <summary>TrueType outlines (0.12.28): the glyf table, where each glyph starts in it (loca, absolute, GlyphCount + 1 of them), and the advances (hmtx: the first HMetrics have their own, the rest the last).</summary>
        private bool m_TrueType;
        private int m_GlyfAt, m_GlyfLength;
        private int[] m_GlyphStarts;
        private int m_HmtxAt, m_HMetrics;
        private readonly Dictionary<int, GlyphOutline> m_Cache = new Dictionary<int, GlyphOutline>();

        /// <summary>The units of the font's em square (1000 for the CJK fonts).</summary>
        public int UnitsPerEm { get; private set; } = 1000;

        /// <summary>
        /// The box a Chinese character is designed on, in font units from the baseline (0.12.28): OS/2's typographic descender and ascender when they make an em (Noto Sans SC -120 to 880, the traffic
        /// sign font -200 to 800), else -0.12 to 0.88 of the em.
        /// </summary>
        public float EmBottom { get; private set; } = -120f;
        public float EmTop { get; private set; } = 880f;

        /// <summary>False when the font could not be read; then TryGlyph finds nothing.</summary>
        public bool Ok { get; private set; }

        private sealed class FontDict
        {
            public int[] LocalSubrs = new int[0];
            public int LocalBias = 107;
            public float DefaultWidth, NominalWidth;
        }

        private struct CmapRange
        {
            public int Start, End, Delta;       // format 12: glyph = Delta + (c - Start); format 4 with no idRangeOffset
            public int RangeOffsetAt;           // format 4: the address of idRangeOffset[i], or 0
            public bool Format4;
        }

        public FontOutlines(byte[] data) : this(data, 0)
        {
        }

        /// <summary>A font of a collection (.ttc: the table directory of face number face; a plain font has the one face 0). The faces of a collection share tables: their offsets are from the start of the file.</summary>
        public FontOutlines(byte[] data, int face)
        {
            m_Data = data;
            m_Face = face;
            try
            {
                Ok = data != null && data.Length > 12 && ReadTables();
            }
            catch (Exception)
            {
                Ok = false;
            }
        }

        /// <summary>The outline of the glyph for a character; false when the font has none for it (or is damaged). Cached.</summary>
        public bool TryGlyph(int codepoint, out GlyphOutline glyph)
        {
            glyph = null;
            if (!Ok)
                return false;
            int gid = GlyphOf(codepoint);
            if (gid <= 0 || gid >= m_GlyphCount)
                return false;
            lock (m_Cache)
            {
                if (m_Cache.TryGetValue(gid, out glyph))
                    return glyph != null;
            }

            GlyphOutline made = null;
            try
            {
                made = m_TrueType ? ReadTrueType(gid, 0.0) : Read(gid);
            }
            catch (Exception)
            {
                made = null;
            }

            lock (m_Cache)
                m_Cache[gid] = made;
            glyph = made;
            return made != null;
        }

        /// <summary>The most segments a quadratic curve is cut into by a flatness.</summary>
        public const int MaxCurveSegments = 16;

        /// <summary>
        /// How many segments keep a quadratic Bezier within flatness of the curve when it is cut at equal steps of t, from the curve's second difference |p0 - 2 p1 + p2| (the degree-2 case of Wang's
        /// formula: the distance between the curve and its chord at the same t is at most |p0 - 2 p1 + p2| / (4 n^2)). From 1 to MaxCurveSegments; 1 for a curve that is straight.
        /// </summary>
        public static int QuadraticSegments(double secondDifference, double flatness)
        {
            if (!(flatness > 0.0) || !(secondDifference > 0.0))
                return 1;
            return (int)Math.Max(1.0, Math.Min(MaxCurveSegments, Math.Ceiling(Math.Sqrt(secondDifference / (4.0 * flatness)))));
        }

        /// <summary>
        /// The outline of a character with the quadratic curves of a TrueType glyph cut by a flatness (font units; every curve gets QuadraticSegments of them, not the fixed 6 of TryGlyph). Not cached
        /// here: the caller keeps what it makes (GlyphCache). A flatness of 0 or less, and a CFF font (its curves are cubic), give TryGlyph's own outline.
        /// </summary>
        public bool TryGlyph(int codepoint, double flatness, out GlyphOutline glyph)
        {
            glyph = null;
            if (!(flatness > 0.0) || !m_TrueType)
                return TryGlyph(codepoint, out glyph);
            if (!Ok)
                return false;
            int gid = GlyphOf(codepoint);
            if (gid <= 0 || gid >= m_GlyphCount)
                return false;
            try
            {
                glyph = ReadTrueType(gid, flatness);
            }
            catch (Exception)
            {
                glyph = null;
            }

            return glyph != null;
        }

        /// <summary>The advance of a character in font units, without reading its outline; false when the font has no glyph for it.</summary>
        public bool TryAdvance(int codepoint, out float advance)
        {
            advance = 0f;
            if (!Ok)
                return false;
            if (!m_TrueType)
            {
                if (!TryGlyph(codepoint, out GlyphOutline glyph))
                    return false;
                advance = glyph.Advance;
                return true;
            }

            int gid = GlyphOf(codepoint);
            if (gid <= 0 || gid >= m_GlyphCount)
                return false;
            advance = AdvanceOf(gid);
            return true;
        }

        // ------------------------------------------------------------------ tables

        private int U8(int at) => m_Data[at];
        private int U16(int at) => (m_Data[at] << 8) | m_Data[at + 1];
        private int S16(int at) => (short)U16(at);
        private long U32(int at) => ((long)m_Data[at] << 24) | ((long)m_Data[at + 1] << 16) | ((long)m_Data[at + 2] << 8) | m_Data[at + 3];

        private bool ReadTables()
        {
            long version = U32(0);
            int directory = 0;
            // 'ttcf': a collection, the directory of a face is where its offset says.
            if (version == 0x74746366)
            {
                long faces = U32(8), at = 12 + 4L * m_Face;
                if (m_Face < 0 || m_Face >= faces || at + 4 > m_Data.Length)
                    return false;
                long start = U32((int)at);
                if (start < 12 || start + 12 > m_Data.Length)
                    return false;
                directory = (int)start;
                version = U32(directory);
            }
            else if (m_Face != 0)
            {
                return false;
            }

            // 'OTTO': CFF outlines; 1.0 or 'true': TrueType.
            m_TrueType = version == 0x00010000 || version == 0x74727565;
            if (version != 0x4F54544F && !m_TrueType)
                return false;
            int count = U16(directory + 4);
            if ((long)directory + 12 + 16L * count > m_Data.Length)
                return false;
            int cmapAt = 0, cmapLength = 0, headAt = 0, locaAt = 0, locaLength = 0, maxpAt = 0, hheaAt = 0, hmtxLength = 0, os2At = 0, os2Length = 0;
            for (int i = 0; i < count; i++)
            {
                int record = directory + 12 + i * 16;
                string tag = "" + (char)m_Data[record] + (char)m_Data[record + 1] + (char)m_Data[record + 2] + (char)m_Data[record + 3];
                int offset = (int)U32(record + 8), length = (int)U32(record + 12);
                if (offset < 0 || length < 0 || (long)offset + length > m_Data.Length)
                    return false;
                if (tag == "CFF ")
                {
                    m_CffOffset = offset;
                    m_CffLength = length;
                }
                else if (tag == "cmap")
                {
                    cmapAt = offset;
                    cmapLength = length;
                }
                else if (tag == "head")
                {
                    headAt = offset;
                }
                else if (tag == "glyf")
                {
                    m_GlyfAt = offset;
                    m_GlyfLength = length;
                }
                else if (tag == "loca")
                {
                    locaAt = offset;
                    locaLength = length;
                }
                else if (tag == "maxp")
                {
                    maxpAt = offset;
                }
                else if (tag == "hhea")
                {
                    hheaAt = offset;
                }
                else if (tag == "hmtx")
                {
                    m_HmtxAt = offset;
                    hmtxLength = length;
                }
                else if (tag == "OS/2")
                {
                    os2At = offset;
                    os2Length = length;
                }
                else if (tag == "COLR" || tag == "CPAL" || tag == "GSUB")
                {
                    NoteColourTable(tag, offset, length);
                }
            }

            if (cmapAt == 0 || headAt == 0)
                return false;
            UnitsPerEm = U16(headAt + 18);
            if (UnitsPerEm < 16 || UnitsPerEm > 16384)
                UnitsPerEm = 1000;
            EmBottom = -0.12f * UnitsPerEm;
            EmTop = 0.88f * UnitsPerEm;
            if (os2At > 0 && os2Length >= 74)
            {
                int ascender = S16(os2At + 68), descender = S16(os2At + 70);
                if (descender <= 0 && ascender > 0 && Math.Abs(ascender - descender - UnitsPerEm) <= UnitsPerEm / 50)
                {
                    EmBottom = descender;
                    EmTop = ascender;
                }
            }

            if (!ReadCmap(cmapAt, cmapLength))
                return false;
            if (!m_TrueType)
                return m_CffLength > 0 && ReadCff();
            return m_GlyfLength > 0 && locaAt > 0 && maxpAt > 0 && hheaAt > 0 && m_HmtxAt > 0 && ReadLoca(locaAt, locaLength, S16(headAt + 50), U16(maxpAt + 4), U16(hheaAt + 34), hmtxLength);
        }

        // ------------------------------------------------------------------ TrueType

        /// <summary>Where each glyph is in glyf (loca: 16-bit offsets halved, or 32-bit), and how many advances hmtx has; false when the tables do not fit together.</summary>
        private bool ReadLoca(int at, int length, int longOffsets, int glyphs, int hMetrics, int hmtxLength)
        {
            if (glyphs < 1 || length < (glyphs + 1) * (longOffsets != 0 ? 4 : 2) || hMetrics < 1 || hmtxLength < hMetrics * 4)
                return false;
            m_GlyphCount = glyphs;
            m_HMetrics = hMetrics;
            m_GlyphStarts = new int[glyphs + 1];
            for (int i = 0; i <= glyphs; i++)
            {
                long offset = longOffsets != 0 ? U32(at + i * 4) : U16(at + i * 2) * 2L;
                if (offset < 0 || offset > m_GlyfLength)
                    return false;
                m_GlyphStarts[i] = m_GlyfAt + (int)offset;
            }

            return true;
        }

        private float AdvanceOf(int gid) => U16(m_HmtxAt + Math.Min(gid, m_HMetrics - 1) * 4);

        /// <summary>A TrueType glyph: its contours (a simple glyph's, or its components', moved and scaled as the composite says) with the quadratic curves cut into segments, and its advance.</summary>
        private GlyphOutline ReadTrueType(int gid, double flatness)
        {
            var outline = new GlyphOutline { Advance = AdvanceOf(gid) };
            AddTrueType(outline, gid, 1f, 0f, 0f, 1f, 0f, 0f, 0, flatness);
            outline.Measure();
            return outline;
        }

        private void AddTrueType(GlyphOutline outline, int gid, float a, float b, float c, float d, float dx, float dy, int depth, double flatness)
        {
            if (gid < 0 || gid >= m_GlyphCount || depth > 8)
                return;
            int at = m_GlyphStarts[gid], end = m_GlyphStarts[gid + 1];
            // An empty glyph (a space) has no data.
            if (end - at < 10)
                return;
            int contours = S16(at);
            if (contours >= 0)
                AddSimple(outline, at, end, contours, a, b, c, d, dx, dy, flatness);
            else
                AddComposite(outline, at + 10, end, a, b, c, d, dx, dy, depth, flatness);
        }

        private void AddSimple(GlyphOutline outline, int at, int end, int contours, float a, float b, float c, float d, float dx, float dy, double flatness)
        {
            if (contours == 0)
                return;
            int endsAt = at + 10;
            var lastOfContour = new int[contours];
            for (int i = 0; i < contours; i++)
                lastOfContour[i] = U16(endsAt + i * 2);
            int points = lastOfContour[contours - 1] + 1;
            if (points <= 0 || points > 65535)
                return;
            int instructions = U16(endsAt + contours * 2);
            int p = endsAt + contours * 2 + 2 + instructions;

            // The flags, with their repeats.
            var flags = new byte[points];
            for (int i = 0; i < points;)
            {
                if (p >= end)
                    return;
                byte flag = m_Data[p++];
                flags[i++] = flag;
                if ((flag & 8) != 0)
                {
                    if (p >= end)
                        return;
                    int repeat = m_Data[p++];
                    for (int r = 0; r < repeat && i < points; r++)
                        flags[i++] = flag;
                }
            }

            // The x and then the y deltas: a byte with its sign in the flag, or the same as before, or a signed short.
            var xs = new int[points];
            var ys = new int[points];
            int x = 0, y = 0;
            for (int i = 0; i < points; i++)
            {
                byte f = flags[i];
                if ((f & 2) != 0)
                {
                    int v = m_Data[p++];
                    x += (f & 16) != 0 ? v : -v;
                }
                else if ((f & 16) == 0)
                {
                    x += S16(p);
                    p += 2;
                }

                xs[i] = x;
            }

            for (int i = 0; i < points; i++)
            {
                byte f = flags[i];
                if ((f & 4) != 0)
                {
                    int v = m_Data[p++];
                    y += (f & 32) != 0 ? v : -v;
                }
                else if ((f & 32) == 0)
                {
                    y += S16(p);
                    p += 2;
                }

                ys[i] = y;
            }

            if (p > end + 1)
                return;
            int first = 0;
            for (int k = 0; k < contours; k++)
            {
                int last = lastOfContour[k];
                if (last < first || last >= points)
                    return;
                AddContour(outline, xs, ys, flags, first, last, a, b, c, d, dx, dy, flatness);
                first = last + 1;
            }
        }

        /// <summary>
        /// One contour of on-curve and off-curve points as a closed polyline: between two off-curve points the on-curve one is their middle (TrueType's implied point), each quadratic curve cut into
        /// CurveSegments (or, with a flatness, as many as QuadraticSegments says of the curve as the composite's transform makes it); the points are put through that transform.
        /// </summary>
        private static void AddContour(GlyphOutline outline, int[] xs, int[] ys, byte[] flags, int first, int last, float a, float b, float c, float d, float dx, float dy, double flatness)
        {
            int n = last - first + 1;
            if (n < 2)
                return;
            bool On(int i) => (flags[first + (i % n + n) % n] & 1) != 0;
            double X(int i) => xs[first + (i % n + n) % n];
            double Y(int i) => ys[first + (i % n + n) % n];

            // Start at an on-curve point; a contour of off-curve points only starts at the middle of its first two.
            int start = -1;
            for (int i = 0; i < n && start < 0; i++)
            {
                if (On(i))
                    start = i;
            }

            double sx, sy;
            if (start >= 0)
            {
                sx = X(start);
                sy = Y(start);
            }
            else
            {
                start = 0;
                sx = (X(0) + X(1)) * 0.5;
                sy = (Y(0) + Y(1)) * 0.5;
            }

            var pts = new List<double>(n * 4) { sx, sy };
            double cx = sx, cy = sy;
            for (int step = 1; step <= n; step++)
            {
                int i = start + step;
                if (On(i))
                {
                    cx = X(i);
                    cy = Y(i);
                    pts.Add(cx);
                    pts.Add(cy);
                    continue;
                }

                // A control point: the curve ends at the next on-curve point, or at the middle of this and the next control point.
                double qx = X(i), qy = Y(i), ex, ey;
                if (On(i + 1))
                {
                    ex = X(i + 1);
                    ey = Y(i + 1);
                    step++;
                }
                else
                {
                    ex = (qx + X(i + 1)) * 0.5;
                    ey = (qy + Y(i + 1)) * 0.5;
                }

                int segments = CurveSegments;
                if (flatness > 0.0)
                {
                    double sx2 = cx - 2 * qx + ex, sy2 = cy - 2 * qy + ey;
                    double tx = a * sx2 + c * sy2, ty = b * sx2 + d * sy2;
                    segments = QuadraticSegments(Math.Sqrt(tx * tx + ty * ty), flatness);
                }

                for (int s = 1; s <= segments; s++)
                {
                    double t = s / (double)segments, u = 1 - t;
                    pts.Add(u * u * cx + 2 * u * t * qx + t * t * ex);
                    pts.Add(u * u * cy + 2 * u * t * qy + t * t * ey);
                }

                cx = ex;
                cy = ey;
            }

            var contour = new float[pts.Count];
            for (int i = 0; i + 1 < pts.Count; i += 2)
            {
                double px = pts[i], py = pts[i + 1];
                contour[i] = (float)(a * px + c * py + dx);
                contour[i + 1] = (float)(b * px + d * py + dy);
            }

            int len = contour.Length;
            if (len >= 4 && Math.Abs(contour[0] - contour[len - 2]) < 1e-3f && Math.Abs(contour[1] - contour[len - 1]) < 1e-3f)
                Array.Resize(ref contour, len - 2);
            if (contour.Length >= 6)
                outline.Contours.Add(contour);
        }

        /// <summary>A composite glyph: each component glyph moved by its offsets (point matching is not done: the components of a CJK font are placed by offsets) and scaled as its flags say.</summary>
        private void AddComposite(GlyphOutline outline, int p, int end, float a, float b, float c, float d, float dx, float dy, int depth, double flatness)
        {
            const int ArgsAreWords = 1, ArgsAreXY = 2, HaveScale = 8, MoreComponents = 32, HaveXYScale = 64, HaveTwoByTwo = 128;
            int flags;
            do
            {
                if (p + 4 > end)
                    return;
                flags = U16(p);
                int component = U16(p + 2);
                p += 4;
                float ox = 0f, oy = 0f;
                if ((flags & ArgsAreWords) != 0)
                {
                    if ((flags & ArgsAreXY) != 0)
                    {
                        ox = S16(p);
                        oy = S16(p + 2);
                    }

                    p += 4;
                }
                else
                {
                    if ((flags & ArgsAreXY) != 0)
                    {
                        ox = (sbyte)m_Data[p];
                        oy = (sbyte)m_Data[p + 1];
                    }

                    p += 2;
                }

                float ca = 1f, cb = 0f, cc = 0f, cd = 1f;
                if ((flags & HaveScale) != 0)
                {
                    ca = cd = F2Dot14(p);
                    p += 2;
                }
                else if ((flags & HaveXYScale) != 0)
                {
                    ca = F2Dot14(p);
                    cd = F2Dot14(p + 2);
                    p += 4;
                }
                else if ((flags & HaveTwoByTwo) != 0)
                {
                    ca = F2Dot14(p);
                    cb = F2Dot14(p + 2);
                    cc = F2Dot14(p + 4);
                    cd = F2Dot14(p + 6);
                    p += 8;
                }

                // The component's transform, then the parent's.
                AddTrueType(outline, component, a * ca + c * cb, b * ca + d * cb, a * cc + c * cd, b * cc + d * cd, a * ox + c * oy + dx, b * ox + d * oy + dy, depth + 1, flatness);
            }
            while ((flags & MoreComponents) != 0);
        }

        private float F2Dot14(int at) => S16(at) / 16384f;

        private bool ReadCmap(int at, int length)
        {
            int tables = U16(at + 2);
            int best = -1, bestScore = -1;
            for (int i = 0; i < tables; i++)
            {
                int record = at + 4 + i * 8;
                int platform = U16(record), encoding = U16(record + 2);
                int offset = (int)U32(record + 4);
                if (offset <= 0 || offset >= length)
                    continue;
                int format = U16(at + offset);
                // Prefer the full Unicode table (format 12), then the BMP one (format 4).
                int score = format == 12 && (platform == 3 && encoding == 10 || platform == 0) ? 3 : format == 4 && (platform == 3 && encoding == 1 || platform == 0) ? 2 : -1;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = at + offset;
                }
            }

            if (best < 0)
                return false;
            int fmt = U16(best);
            if (fmt == 12)
            {
                int groups = (int)U32(best + 12);
                if (groups < 0 || groups > 100000)
                    return false;
                for (int g = 0; g < groups; g++)
                {
                    int p = best + 16 + g * 12;
                    m_Ranges.Add(new CmapRange { Start = (int)U32(p), End = (int)U32(p + 4), Delta = (int)U32(p + 8) - (int)U32(p) });
                }
            }
            else
            {
                int segX2 = U16(best + 6);
                int segs = segX2 / 2;
                int endAt = best + 14, startAt = endAt + segX2 + 2, deltaAt = startAt + segX2, offsetAt = deltaAt + segX2;
                for (int s = 0; s < segs; s++)
                {
                    int rangeOffset = U16(offsetAt + s * 2);
                    m_Ranges.Add(new CmapRange
                    {
                        Start = U16(startAt + s * 2),
                        End = U16(endAt + s * 2),
                        Delta = S16(deltaAt + s * 2),
                        RangeOffsetAt = rangeOffset == 0 ? 0 : offsetAt + s * 2,
                        Format4 = true
                    });
                }
            }

            return m_Ranges.Count > 0;
        }

        private int GlyphOf(int codepoint)
        {
            int lo = 0, hi = m_Ranges.Count - 1;
            // The ranges are sorted by code; the last of format 4 is the 0xFFFF sentinel.
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                CmapRange r = m_Ranges[mid];
                if (codepoint < r.Start)
                    hi = mid - 1;
                else if (codepoint > r.End)
                    lo = mid + 1;
                else
                {
                    if (!r.Format4)
                        return r.Delta + codepoint;
                    if (r.RangeOffsetAt == 0)
                        return (codepoint + r.Delta) & 0xFFFF;
                    int at = r.RangeOffsetAt + U16(r.RangeOffsetAt) + (codepoint - r.Start) * 2;
                    if (at < 0 || at + 2 > m_Data.Length)
                        return 0;
                    int glyph = U16(at);
                    return glyph == 0 ? 0 : (glyph + r.Delta) & 0xFFFF;
                }
            }

            return 0;
        }

        // ------------------------------------------------------------------ CFF

        /// <summary>Reads an INDEX at an address (in the data): the offsets of its objects (count + 1 of them, absolute), and the address after it.</summary>
        private int[] ReadIndex(int at, out int end)
        {
            int count = U16(at);
            if (count == 0)
            {
                end = at + 2;
                return new int[0];
            }

            int offSize = U8(at + 2);
            if (offSize < 1 || offSize > 4)
                throw new FormatException("offSize");
            int basis = at + 3 + (count + 1) * offSize - 1;
            var offsets = new int[count + 1];
            for (int i = 0; i <= count; i++)
            {
                int v = 0;
                for (int k = 0; k < offSize; k++)
                    v = (v << 8) | U8(at + 3 + i * offSize + k);
                offsets[i] = basis + v;
            }

            end = offsets[count];
            return offsets;
        }

        private static int Bias(int count) => count < 1240 ? 107 : count < 33900 ? 1131 : 32768;

        private bool ReadCff()
        {
            int cff = m_CffOffset;
            int hdrSize = U8(cff + 2);
            int end;
            int[] names = ReadIndex(cff + hdrSize, out end);
            int[] topDicts = ReadIndex(end, out end);
            ReadIndex(end, out end);        // strings
            m_GlobalSubrs = ReadIndex(end, out end);
            m_GlobalBias = Bias(Math.Max(0, m_GlobalSubrs.Length - 1));
            if (topDicts.Length < 2)
                return false;
            Dictionary<int, List<double>> top = ReadDict(topDicts[0], topDicts[1]);
            if (!top.TryGetValue(17, out List<double> charStrings) || charStrings.Count < 1)
                return false;
            m_CharStringsAt = cff + (int)charStrings[0];
            m_CharStringOffsets = ReadIndex(m_CharStringsAt, out _);
            m_GlyphCount = m_CharStringOffsets.Length - 1;
            if (m_GlyphCount < 1)
                return false;

            bool cid = top.ContainsKey(1230);       // 12 30 ROS
            if (cid)
            {
                if (!top.TryGetValue(1236, out List<double> fdArray) || !top.TryGetValue(1237, out List<double> fdSelect))
                    return false;
                int[] fdOffsets = ReadIndex(cff + (int)fdArray[0], out _);
                m_Dicts = new FontDict[Math.Max(0, fdOffsets.Length - 1)];
                for (int i = 0; i < m_Dicts.Length; i++)
                    m_Dicts[i] = ReadPrivateOf(ReadDict(fdOffsets[i], fdOffsets[i + 1]));
                m_FdOfGlyph = ReadFdSelect(cff + (int)fdSelect[0], m_GlyphCount);
            }
            else
            {
                m_Dicts = new[] { ReadPrivateOf(top) };
                m_FdOfGlyph = null;
            }

            return m_Dicts.Length > 0;
        }

        private FontDict ReadPrivateOf(Dictionary<int, List<double>> dict)
        {
            var result = new FontDict();
            if (!dict.TryGetValue(18, out List<double> priv) || priv.Count < 2)
                return result;
            int size = (int)priv[0], at = m_CffOffset + (int)priv[1];
            Dictionary<int, List<double>> p = ReadDict(at, at + size);
            if (p.TryGetValue(20, out List<double> def) && def.Count > 0)
                result.DefaultWidth = (float)def[0];
            if (p.TryGetValue(21, out List<double> nominal) && nominal.Count > 0)
                result.NominalWidth = (float)nominal[0];
            if (p.TryGetValue(19, out List<double> subrs) && subrs.Count > 0)
            {
                result.LocalSubrs = ReadIndex(at + (int)subrs[0], out _);
                result.LocalBias = Bias(Math.Max(0, result.LocalSubrs.Length - 1));
            }

            return result;
        }

        private byte[] ReadFdSelect(int at, int glyphs)
        {
            var fd = new byte[glyphs];
            int format = U8(at);
            if (format == 0)
            {
                for (int g = 0; g < glyphs; g++)
                    fd[g] = (byte)U8(at + 1 + g);
            }
            else if (format == 3)
            {
                int ranges = U16(at + 1);
                for (int r = 0; r < ranges; r++)
                {
                    int first = U16(at + 3 + r * 3), dict = U8(at + 5 + r * 3), next = U16(at + 6 + r * 3);
                    for (int g = first; g < next && g < glyphs; g++)
                        fd[g] = (byte)dict;
                }
            }

            return fd;
        }

        /// <summary>Reads a DICT between two addresses: operator (escaped operators as 1200 + the second byte) to its operands.</summary>
        private Dictionary<int, List<double>> ReadDict(int from, int to)
        {
            var result = new Dictionary<int, List<double>>();
            var operands = new List<double>();
            int i = from;
            while (i < to)
            {
                int b0 = U8(i++);
                if (b0 <= 21)
                {
                    int op = b0;
                    if (b0 == 12)
                        op = 1200 + U8(i++);
                    result[op] = operands;
                    operands = new List<double>();
                }
                else if (b0 == 28)
                {
                    operands.Add(S16(i));
                    i += 2;
                }
                else if (b0 == 29)
                {
                    operands.Add((int)U32(i));
                    i += 4;
                }
                else if (b0 == 30)
                {
                    var text = new System.Text.StringBuilder();
                    bool done = false;
                    while (!done && i < to)
                    {
                        int b = U8(i++);
                        foreach (int nibble in new[] { b >> 4, b & 15 })
                        {
                            if (nibble <= 9) text.Append((char)('0' + nibble));
                            else if (nibble == 10) text.Append('.');
                            else if (nibble == 11) text.Append('E');
                            else if (nibble == 12) text.Append("E-");
                            else if (nibble == 14) text.Append('-');
                            else if (nibble == 15) { done = true; break; }
                        }
                    }

                    double.TryParse(text.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double real);
                    operands.Add(real);
                }
                else if (b0 >= 32 && b0 <= 246)
                {
                    operands.Add(b0 - 139);
                }
                else if (b0 >= 247 && b0 <= 250)
                {
                    operands.Add((b0 - 247) * 256 + U8(i++) + 108);
                }
                else if (b0 >= 251 && b0 <= 254)
                {
                    operands.Add(-(b0 - 251) * 256 - U8(i++) - 108);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------ Type 2 charstrings

        private sealed class Machine
        {
            public readonly List<double> Stack = new List<double>(48);
            public double X, Y;
            public int Stems;
            public bool WidthDone, Open, Ended;
            public float Width;
            public List<double> Points;                 // the contour being made: x, y, ...
            public GlyphOutline Outline;
            public FontDict Dict;
        }

        private GlyphOutline Read(int gid)
        {
            var machine = new Machine { Outline = new GlyphOutline(), Dict = m_Dicts[m_FdOfGlyph == null ? 0 : Math.Min(m_Dicts.Length - 1, m_FdOfGlyph[gid])] };
            machine.Width = machine.Dict.DefaultWidth;
            Run(machine, m_CharStringOffsets[gid], m_CharStringOffsets[gid + 1], 0);
            Close(machine);
            machine.Outline.Advance = machine.Width;
            machine.Outline.Measure();
            return machine.Outline;
        }

        private static void Close(Machine m)
        {
            if (m.Points != null && m.Points.Count >= 6)
            {
                var contour = new float[m.Points.Count];
                for (int i = 0; i < contour.Length; i++)
                    contour[i] = (float)m.Points[i];
                // A contour closed by repeating its first point loses the repeat.
                int n = contour.Length;
                if (n >= 4 && Math.Abs(contour[0] - contour[n - 2]) < 1e-3f && Math.Abs(contour[1] - contour[n - 1]) < 1e-3f)
                    Array.Resize(ref contour, n - 2);
                if (contour.Length >= 6)
                    m.Outline.Contours.Add(contour);
            }

            m.Points = null;
            m.Open = false;
        }

        private static void MoveTo(Machine m, double dx, double dy)
        {
            Close(m);
            m.X += dx;
            m.Y += dy;
            m.Points = new List<double> { m.X, m.Y };
            m.Open = true;
        }

        private static void LineTo(Machine m, double dx, double dy)
        {
            if (m.Points == null)
                m.Points = new List<double> { m.X, m.Y };
            m.X += dx;
            m.Y += dy;
            m.Points.Add(m.X);
            m.Points.Add(m.Y);
        }

        private const int CurveSegments = 6;

        private static void CurveTo(Machine m, double dx1, double dy1, double dx2, double dy2, double dx3, double dy3)
        {
            if (m.Points == null)
                m.Points = new List<double> { m.X, m.Y };
            double x0 = m.X, y0 = m.Y;
            double x1 = x0 + dx1, y1 = y0 + dy1, x2 = x1 + dx2, y2 = y1 + dy2, x3 = x2 + dx3, y3 = y2 + dy3;
            for (int i = 1; i <= CurveSegments; i++)
            {
                double t = i / (double)CurveSegments, u = 1 - t;
                m.Points.Add(u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3);
                m.Points.Add(u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3);
            }

            m.X = x3;
            m.Y = y3;
        }

        /// <summary>The width is the first number when the operator that first clears the stack has one more than it takes.</summary>
        private static void TakeWidth(Machine m, bool extra)
        {
            if (m.WidthDone)
                return;
            m.WidthDone = true;
            if (extra && m.Stack.Count > 0)
            {
                m.Width = (float)(m.Dict.NominalWidth + m.Stack[0]);
                m.Stack.RemoveAt(0);
            }
        }

        private void Run(Machine m, int from, int to, int depth)
        {
            if (depth > 10)
                return;
            int i = from;
            List<double> s = m.Stack;
            while (i < to && !m.Ended)
            {
                int b0 = U8(i++);
                if (b0 >= 32 || b0 == 28)
                {
                    if (b0 == 28) { s.Add(S16(i)); i += 2; }
                    else if (b0 <= 246) s.Add(b0 - 139);
                    else if (b0 <= 250) s.Add((b0 - 247) * 256 + U8(i++) + 108);
                    else if (b0 <= 254) s.Add(-(b0 - 251) * 256 - U8(i++) - 108);
                    else { s.Add((int)U32(i) / 65536.0); i += 4; }
                    if (s.Count > 513)
                        throw new FormatException("stack");
                    continue;
                }

                switch (b0)
                {
                    case 1: case 3: case 18: case 23:       // hstem vstem hstemhm vstemhm
                        TakeWidth(m, s.Count % 2 == 1);
                        m.Stems += s.Count / 2;
                        s.Clear();
                        break;
                    case 19: case 20:                         // hintmask cntrmask
                        TakeWidth(m, s.Count % 2 == 1);
                        m.Stems += s.Count / 2;
                        s.Clear();
                        i += (m.Stems + 7) / 8;
                        break;
                    case 21:                                  // rmoveto
                        TakeWidth(m, s.Count > 2);
                        if (s.Count >= 2) MoveTo(m, s[s.Count - 2], s[s.Count - 1]);
                        s.Clear();
                        break;
                    case 22:                                  // hmoveto
                        TakeWidth(m, s.Count > 1);
                        if (s.Count >= 1) MoveTo(m, s[s.Count - 1], 0);
                        s.Clear();
                        break;
                    case 4:                                   // vmoveto
                        TakeWidth(m, s.Count > 1);
                        if (s.Count >= 1) MoveTo(m, 0, s[s.Count - 1]);
                        s.Clear();
                        break;
                    case 5:                                   // rlineto
                        for (int k = 0; k + 1 < s.Count; k += 2) LineTo(m, s[k], s[k + 1]);
                        s.Clear();
                        break;
                    case 6: case 7:                           // hlineto vlineto
                        {
                            bool horizontal = b0 == 6;
                            for (int k = 0; k < s.Count; k++)
                            {
                                if (horizontal) LineTo(m, s[k], 0); else LineTo(m, 0, s[k]);
                                horizontal = !horizontal;
                            }

                            s.Clear();
                            break;
                        }
                    case 8:                                   // rrcurveto
                        for (int k = 0; k + 5 < s.Count; k += 6) CurveTo(m, s[k], s[k + 1], s[k + 2], s[k + 3], s[k + 4], s[k + 5]);
                        s.Clear();
                        break;
                    case 24:                                  // rcurveline
                        {
                            int k = 0;
                            for (; k + 7 < s.Count; k += 6) CurveTo(m, s[k], s[k + 1], s[k + 2], s[k + 3], s[k + 4], s[k + 5]);
                            if (k + 1 < s.Count) LineTo(m, s[k], s[k + 1]);
                            s.Clear();
                            break;
                        }
                    case 25:                                  // rlinecurve
                        {
                            int k = 0;
                            for (; k + 7 < s.Count; k += 2) LineTo(m, s[k], s[k + 1]);
                            if (k + 5 < s.Count) CurveTo(m, s[k], s[k + 1], s[k + 2], s[k + 3], s[k + 4], s[k + 5]);
                            s.Clear();
                            break;
                        }
                    case 26:                                  // vvcurveto
                        {
                            int k = 0;
                            double dx1 = 0;
                            if (s.Count % 4 == 1) dx1 = s[k++];
                            for (; k + 3 < s.Count; k += 4)
                            {
                                CurveTo(m, dx1, s[k], s[k + 1], s[k + 2], 0, s[k + 3]);
                                dx1 = 0;
                            }

                            s.Clear();
                            break;
                        }
                    case 27:                                  // hhcurveto
                        {
                            int k = 0;
                            double dy1 = 0;
                            if (s.Count % 4 == 1) dy1 = s[k++];
                            for (; k + 3 < s.Count; k += 4)
                            {
                                CurveTo(m, s[k], dy1, s[k + 1], s[k + 2], s[k + 3], 0);
                                dy1 = 0;
                            }

                            s.Clear();
                            break;
                        }
                    case 30: case 31:                         // vhcurveto hvcurveto
                        {
                            bool horizontalStart = b0 == 31;
                            int k = 0;
                            while (k + 3 < s.Count)
                            {
                                bool last = s.Count - k == 5;
                                double extra = last ? s[k + 4] : 0;
                                if (horizontalStart)
                                    CurveTo(m, s[k], 0, s[k + 1], s[k + 2], extra, s[k + 3]);
                                else
                                    CurveTo(m, 0, s[k], s[k + 1], s[k + 2], s[k + 3], extra);
                                horizontalStart = !horizontalStart;
                                k += 4;
                            }

                            s.Clear();
                            break;
                        }
                    case 10: case 29:                         // callsubr callgsubr
                        {
                            if (s.Count == 0)
                                throw new FormatException("subr");
                            int[] table = b0 == 10 ? m.Dict.LocalSubrs : m_GlobalSubrs;
                            int bias = b0 == 10 ? m.Dict.LocalBias : m_GlobalBias;
                            int number = (int)s[s.Count - 1] + bias;
                            s.RemoveAt(s.Count - 1);
                            if (number < 0 || number + 1 >= table.Length)
                                throw new FormatException("subr number");
                            Run(m, table[number], table[number + 1], depth + 1);
                            break;
                        }
                    case 11:                                  // return
                        return;
                    case 14:                                  // endchar
                        TakeWidth(m, s.Count == 1 || s.Count == 5);
                        Close(m);
                        s.Clear();
                        m.Ended = true;
                        return;
                    case 12:
                        {
                            int b1 = U8(i++);
                            switch (b1)
                            {
                                case 35:                      // flex
                                    if (s.Count >= 12)
                                    {
                                        CurveTo(m, s[0], s[1], s[2], s[3], s[4], s[5]);
                                        CurveTo(m, s[6], s[7], s[8], s[9], s[10], s[11]);
                                    }

                                    s.Clear();
                                    break;
                                case 34:                      // hflex
                                    if (s.Count >= 7)
                                    {
                                        CurveTo(m, s[0], 0, s[1], s[2], s[3], 0);
                                        CurveTo(m, s[4], 0, s[5], -s[2], s[6], 0);
                                    }

                                    s.Clear();
                                    break;
                                case 36:                      // hflex1
                                    if (s.Count >= 9)
                                    {
                                        CurveTo(m, s[0], s[1], s[2], s[3], s[4], 0);
                                        CurveTo(m, s[5], 0, s[6], s[7], s[8], -(s[1] + s[3] + s[7]));
                                    }

                                    s.Clear();
                                    break;
                                case 37:                      // flex1
                                    if (s.Count >= 11)
                                    {
                                        double dx = s[0] + s[2] + s[4] + s[6] + s[8], dy = s[1] + s[3] + s[5] + s[7] + s[9];
                                        CurveTo(m, s[0], s[1], s[2], s[3], s[4], s[5]);
                                        if (Math.Abs(dx) > Math.Abs(dy))
                                            CurveTo(m, s[6], s[7], s[8], s[9], s[10], -dy);
                                        else
                                            CurveTo(m, s[6], s[7], s[8], s[9], -dx, s[10]);
                                    }

                                    s.Clear();
                                    break;
                                default:
                                    s.Clear();
                                    break;
                            }

                            break;
                        }
                    default:
                        s.Clear();
                        break;
                }
            }
        }
    }
}
