using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>Random access to the bytes of a font file: the scan of the system fonts reads a few tables of each (a Chinese font is 10 to 20 MB) and never the whole file.</summary>
    public interface IFontBytes
    {
        long Length { get; }

        /// <summary>Reads up to count bytes from offset into buffer; how many were read (less at the end of the file, 0 beyond it).</summary>
        int Read(long offset, byte[] buffer, int count);
    }

    public sealed class FontBytes : IFontBytes
    {
        private readonly byte[] m_Data;

        public FontBytes(byte[] data)
        {
            m_Data = data ?? new byte[0];
        }

        public long Length => m_Data.Length;

        public int Read(long offset, byte[] buffer, int count)
        {
            if (offset < 0 || offset >= m_Data.Length)
                return 0;
            int n = (int)Math.Min(count, m_Data.Length - offset);
            Array.Copy(m_Data, offset, buffer, 0, n);
            return n;
        }
    }

    /// <summary>A font file on disk, opened for reading beside whoever else has it (a font the system holds open is not exclusive).</summary>
    public sealed class FontFile : IFontBytes, IDisposable
    {
        private readonly FileStream m_Stream;

        public FontFile(string path)
        {
            m_Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
        }

        public long Length => m_Stream.Length;

        public int Read(long offset, byte[] buffer, int count)
        {
            if (offset < 0 || offset >= m_Stream.Length)
                return 0;
            m_Stream.Seek(offset, SeekOrigin.Begin);
            int total = 0;
            while (total < count)
            {
                int n = m_Stream.Read(buffer, total, count - total);
                if (n <= 0)
                    break;
                total += n;
            }

            return total;
        }

        public void Dispose() => m_Stream.Dispose();
    }

    /// <summary>What one face of a font file is: its names, whether this reader can draw it, and what it covers.</summary>
    public sealed class FontFaceInfo
    {
        public int Face;

        /// <summary>The family in English and, when the font names it, in Chinese ("" when the font has none); the style ("Regular", "Bold Italic").</summary>
        public string FamilyEn = "", FamilyZh = "", StyleEn = "";

        /// <summary>Outlines this reader takes (glyf and its tables, or CFF) and a cmap it can look up.</summary>
        public bool Supported;

        public int WeightClass = 400;
        public bool Italic;

        /// <summary>How many of the sample characters the cmap has (FontInfo.CjkSample and LatinSample).</summary>
        public int CjkCovered, LatinCovered;

        public bool CoversCjk => CjkCovered >= FontInfo.CjkSample.Length - 2;

        public bool CoversLatin => LatinCovered >= FontInfo.LatinSample.Length - 1;

        /// <summary>The face a family is listed by: regular weight, upright.</summary>
        public bool IsRegular => WeightClass >= 350 && WeightClass <= 450 && !Italic;

        /// <summary>The name a face is listed by: its family (the Chinese family in the Chinese list), and nothing for a face that names none.</summary>
        public string DisplayEn => FamilyEn.Length > 0 ? FamilyEn : FamilyZh;

        public string DisplayZh => FamilyZh.Length > 0 ? FamilyZh : FamilyEn;
    }

    /// <summary>
    /// Reads what a font file says about itself without reading all of it (0.12.76, for the font list of the text light box): the faces of a collection, their family names (the Chinese one too), whether
    /// the outlines are ones FontOutlines draws, and whether the cmap has the sample characters of Chinese and of Latin. Pure but for the bytes it is given; a damaged file gives no faces, never an exception.
    /// </summary>
    public static class FontInfo
    {
        /// <summary>Twenty of the commonest Chinese characters (a font that has all but two of them is taken for a Chinese font), and the letters and figures a Latin text is made of.</summary>
        public const string CjkSample = "的一是不了人我在有他这中大来上国个到说们";

        public const string LatinSample = "AZaz09";

        private const int MaxTable = 8 * 1024 * 1024, MaxFaces = 64;

        public static List<FontFaceInfo> Probe(IFontBytes bytes)
        {
            var faces = new List<FontFaceInfo>();
            if (bytes == null || bytes.Length < 12)
                return faces;
            try
            {
                byte[] head = Block(bytes, 0, 12);
                if (head == null)
                    return faces;
                long tag = U32(head, 0);
                if (tag == 0x74746366)
                {
                    int count = (int)Math.Min(MaxFaces, U32(head, 8));
                    byte[] offsets = Block(bytes, 12, count * 4);
                    if (offsets == null)
                        return faces;
                    for (int i = 0; i < count; i++)
                        faces.Add(Face(bytes, U32(offsets, i * 4), i));
                }
                else
                {
                    faces.Add(Face(bytes, 0, 0));
                }
            }
            catch (Exception)
            {
                // A damaged file has what could be read before.
            }

            return faces;
        }

        /// <summary>The faces of a file on disk; none for a file that cannot be opened.</summary>
        public static List<FontFaceInfo> Probe(string path)
        {
            try
            {
                using (var file = new FontFile(path))
                    return Probe(file);
            }
            catch (Exception)
            {
                return new List<FontFaceInfo>();
            }
        }

        private struct Table
        {
            public long Offset, Length;
        }

        private static FontFaceInfo Face(IFontBytes bytes, long directory, int index)
        {
            var info = new FontFaceInfo { Face = index };
            byte[] head = Block(bytes, directory, 12);
            if (head == null)
                return info;
            long version = U32(head, 0);
            if (version != 0x00010000 && version != 0x74727565 && version != 0x4F54544F)
                return info;
            int tables = U16(head, 4);
            byte[] records = Block(bytes, directory + 12, tables * 16);
            if (records == null)
                return info;
            var found = new Dictionary<string, Table>();
            for (int i = 0; i < tables; i++)
            {
                string name = "" + (char)records[i * 16] + (char)records[i * 16 + 1] + (char)records[i * 16 + 2] + (char)records[i * 16 + 3];
                long offset = U32(records, i * 16 + 8), length = U32(records, i * 16 + 12);
                if (offset + length > bytes.Length)
                    continue;
                found[name] = new Table { Offset = offset, Length = length };
            }

            bool outlines = version == 0x4F54544F ? found.ContainsKey("CFF ") : found.ContainsKey("glyf") && found.ContainsKey("loca") && found.ContainsKey("maxp") && found.ContainsKey("hhea") && found.ContainsKey("hmtx");
            info.Supported = outlines && found.ContainsKey("head") && found.ContainsKey("cmap");
            if (found.TryGetValue("OS/2", out Table os2) && os2.Length >= 64)
            {
                byte[] block = Block(bytes, os2.Offset, 64);
                if (block != null)
                {
                    info.WeightClass = U16(block, 4);
                    info.Italic = (U16(block, 62) & 1) != 0;
                }
            }
            else if (found.TryGetValue("head", out Table headTable) && headTable.Length >= 46)
            {
                byte[] block = Block(bytes, headTable.Offset + 44, 2);
                if (block != null)
                {
                    int style = U16(block, 0);
                    info.WeightClass = (style & 1) != 0 ? 700 : 400;
                    info.Italic = (style & 2) != 0;
                }
            }

            if (found.TryGetValue("name", out Table nameTable))
                ReadNames(Block(bytes, nameTable.Offset, (int)Math.Min(nameTable.Length, MaxTable)), info);
            if (info.Supported && found.TryGetValue("cmap", out Table cmap))
            {
                byte[] block = Block(bytes, cmap.Offset, (int)Math.Min(cmap.Length, MaxTable));
                var lookup = block != null ? Cmap.Of(block) : null;
                if (lookup == null)
                {
                    info.Supported = false;
                }
                else
                {
                    foreach (char c in CjkSample)
                        info.CjkCovered += lookup.Has(c) ? 1 : 0;
                    foreach (char c in LatinSample)
                        info.LatinCovered += lookup.Has(c) ? 1 : 0;
                }
            }

            return info;
        }

        private static byte[] Block(IFontBytes bytes, long offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > bytes.Length)
                return null;
            var block = new byte[length];
            int read = bytes.Read(offset, block, length);
            return read == length ? block : null;
        }

        private static int U16(byte[] b, int at) => (b[at] << 8) | b[at + 1];

        private static long U32(byte[] b, int at) => ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];

        // ------------------------------------------------------------------ names

        /// <summary>
        /// The family and the style from the name table: the typographic ones (names 16 and 17) before the plain ones (1 and 2); English from the Windows record for en-US (or a Mac one), Chinese from
        /// the Windows records for zh-CN, zh-TW or zh-HK. Strings are UTF-16BE (Windows, Unicode) or Latin-1 (Mac Roman, which is the same for the letters a family name has).
        /// </summary>
        private static void ReadNames(byte[] name, FontFaceInfo info)
        {
            if (name == null || name.Length < 6)
                return;
            int count = U16(name, 2), stringsAt = U16(name, 4);
            string family16En = null, family1En = null, family16Zh = null, family1Zh = null, style17 = null, style2 = null;
            for (int i = 0; i < count && 6 + (i + 1) * 12 <= name.Length; i++)
            {
                int at = 6 + i * 12;
                int platform = U16(name, at), language = U16(name, at + 4), id = U16(name, at + 6), length = U16(name, at + 8), offset = U16(name, at + 10);
                if (id != 1 && id != 2 && id != 16 && id != 17)
                    continue;
                bool english = (platform == 3 && language == 0x0409) || (platform == 1 && language == 0) || (platform == 0 && language == 0);
                bool chinese = platform == 3 && (language == 0x0804 || language == 0x0404 || language == 0x0C04 || language == 0x1004);
                if (!english && !chinese)
                    continue;
                string text = Decode(name, stringsAt + offset, length, platform);
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                if (chinese)
                {
                    if (id == 16 && family16Zh == null)
                        family16Zh = text;
                    else if (id == 1 && family1Zh == null)
                        family1Zh = text;
                    continue;
                }

                if (id == 16 && family16En == null)
                    family16En = text;
                else if (id == 1 && family1En == null)
                    family1En = text;
                else if (id == 17 && style17 == null)
                    style17 = text;
                else if (id == 2 && style2 == null)
                    style2 = text;
            }

            info.FamilyEn = (family16En ?? family1En ?? "").Trim();
            info.FamilyZh = (family16Zh ?? family1Zh ?? "").Trim();
            info.StyleEn = (style17 ?? style2 ?? "").Trim();
            if (string.Equals(info.FamilyZh, info.FamilyEn, StringComparison.Ordinal))
                info.FamilyZh = "";
        }

        private static string Decode(byte[] name, int at, int length, int platform)
        {
            if (at < 0 || length < 0 || at + length > name.Length)
                return null;
            if (platform == 3 || platform == 0)
                return length % 2 == 0 ? Encoding.BigEndianUnicode.GetString(name, at, length) : null;
            // Mac Roman: the letters a family name is made of are the same as Latin-1's.
            var chars = new char[length];
            for (int i = 0; i < length; i++)
                chars[i] = (char)name[at + i];
            return new string(chars);
        }

        // ------------------------------------------------------------------ cmap

        /// <summary>The lookup of one cmap subtable: format 12 (Unicode full) or 4 (BMP), the best one the table has; a character has a glyph when the lookup finds one that is not 0.</summary>
        private sealed class Cmap
        {
            private readonly byte[] m_Table;
            private readonly int m_At;
            private readonly bool m_Format12;

            private Cmap(byte[] table, int at, bool format12)
            {
                m_Table = table;
                m_At = at;
                m_Format12 = format12;
            }

            public static Cmap Of(byte[] table)
            {
                if (table.Length < 4)
                    return null;
                int count = U16(table, 2), best = -1, bestFormat = 0;
                for (int i = 0; i < count && 4 + (i + 1) * 8 <= table.Length; i++)
                {
                    int platform = U16(table, 4 + i * 8), encoding = U16(table, 6 + i * 8);
                    long offset = U32(table, 8 + i * 8);
                    if (offset + 4 > table.Length || !(platform == 0 || (platform == 3 && (encoding == 1 || encoding == 10))))
                        continue;
                    int format = U16(table, (int)offset);
                    if ((format == 12 && offset + 16 <= table.Length) || (format == 4 && offset + 14 <= table.Length))
                    {
                        if (best < 0 || (format == 12 && bestFormat != 12))
                        {
                            best = (int)offset;
                            bestFormat = format;
                        }
                    }
                }

                return best < 0 ? null : new Cmap(table, best, bestFormat == 12);
            }

            public bool Has(int c) => (m_Format12 ? Format12(c) : Format4(c)) > 0;

            private int Format12(int c)
            {
                long groups = U32(m_Table, m_At + 12);
                long first = m_At + 16;
                if (first + groups * 12 > m_Table.Length)
                    groups = Math.Max(0, (m_Table.Length - first) / 12);
                long lo = 0, hi = groups - 1;
                while (lo <= hi)
                {
                    long mid = (lo + hi) / 2;
                    int at = (int)(first + mid * 12);
                    long start = U32(m_Table, at), end = U32(m_Table, at + 4);
                    if (c < start)
                        hi = mid - 1;
                    else if (c > end)
                        lo = mid + 1;
                    else
                        return (int)(U32(m_Table, at + 8) + (c - start));
                }

                return 0;
            }

            private int Format4(int c)
            {
                if (c > 0xFFFF)
                    return 0;
                int segX2 = U16(m_Table, m_At + 6), segments = segX2 / 2;
                int ends = m_At + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
                if (ranges + segX2 > m_Table.Length)
                    return 0;
                for (int s = 0; s < segments; s++)
                {
                    if (c > U16(m_Table, ends + s * 2))
                        continue;
                    int start = U16(m_Table, starts + s * 2);
                    if (c < start)
                        return 0;
                    int delta = U16(m_Table, deltas + s * 2), range = U16(m_Table, ranges + s * 2);
                    if (range == 0)
                        return (c + delta) & 0xFFFF;
                    int at = ranges + s * 2 + range + (c - start) * 2;
                    if (at + 2 > m_Table.Length)
                        return 0;
                    int glyph = U16(m_Table, at);
                    return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
                }

                return 0;
            }
        }
    }
}
