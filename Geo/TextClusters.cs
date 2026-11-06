using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A typed text as the cells a lit board is made of (0.12.78, DESIGN-text-lightbox.md: the owner, 2 Nov: "应该使用Unicode，支持Emoji"). A cell is a cluster of code points that is one thing to read: a surrogate
    /// pair is one character, a base with a variation selector (U+FE0F), a skin tone (U+1F3FB to U+1F3FF), a keycap (U+20E3) or a combining mark is one, a ZWJ sequence (a family, a profession) is one, two
    /// regional indicators (a flag) are one, a tag sequence (a subdivision flag) is one. .NET Framework's StringInfo joins surrogate pairs and combining marks and nothing of emoji, so the rules of UAX #29
    /// that matter for emoji are written here. Pure.
    /// <para>
    /// A cluster is named by an int, its key: the code point itself when it is one (so that every text that has no sequence in it is laid out, cached and drawn as before), else a number from the registry
    /// (KeyOf), which is at least ClusterKeyBase. The keys of the registry are for one run of the game only: they are never saved (a spec keeps the text).
    /// </para>
    /// </summary>
    public static class TextClusters
    {
        /// <summary>The first key of a cluster of several code points: above every code point (0x10FFFF) and positive.</summary>
        public const int ClusterKeyBase = 0x40000000;

        /// <summary>How many different sequences the registry keeps: a text holds at most 36 cells, so this is far more than a city uses; past it a sequence is drawn as its first code point.</summary>
        public const int MaxRegistered = 65536;

        private static readonly Dictionary<string, int> s_Keys = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly List<string> s_Texts = new List<string>();
        private static readonly object s_Lock = new object();

        // ------------------------------------------------------------------ code points

        /// <summary>The code point at an index of a string: a surrogate pair is one (length 2), a lone half is U+FFFD (length 1).</summary>
        public static int ScalarAt(string text, int index, out int length)
        {
            char c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                length = 2;
                return char.ConvertToUtf32(c, text[index + 1]);
            }

            length = 1;
            return char.IsSurrogate(c) ? 0xFFFD : c;
        }

        /// <summary>The code points of a string, a lone surrogate half as U+FFFD.</summary>
        public static List<int> Scalars(string text)
        {
            var list = new List<int>(text.Length);
            for (int i = 0; i < text.Length;)
            {
                list.Add(ScalarAt(text, i, out int length));
                i += length;
            }

            return list;
        }

        public static string FromScalar(int scalar) => scalar >= 0 && scalar < 0x110000 && !(scalar >= 0xD800 && scalar <= 0xDFFF) ? char.ConvertFromUtf32(scalar) : "�";

        // ------------------------------------------------------------------ classes of code points

        public static bool IsRegionalIndicator(int s) => s >= 0x1F1E6 && s <= 0x1F1FF;

        public static bool IsVariationSelector(int s) => (s >= 0xFE00 && s <= 0xFE0F) || (s >= 0xE0100 && s <= 0xE01EF);

        public static bool IsEmojiModifier(int s) => s >= 0x1F3FB && s <= 0x1F3FF;

        public static bool IsTag(int s) => s >= 0xE0020 && s <= 0xE007F;

        public const int Zwj = 0x200D, Keycap = 0x20E3, Vs15 = 0xFE0E, Vs16 = 0xFE0F;

        /// <summary>Whether a code point is a combining mark or another that joins the one before it without taking a cell of its own.</summary>
        private static bool IsExtend(int s)
        {
            if (s == Zwj || s == Keycap || IsVariationSelector(s) || IsEmojiModifier(s) || IsTag(s))
                return true;
            if (s < 0x300 || s > 0xE01EF)
                return false;
            UnicodeCategory category = s < 0x10000 ? CharUnicodeInfo.GetUnicodeCategory((char)s) : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(s), 0);
            return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark || category == UnicodeCategory.SpacingCombiningMark;
        }

        /// <summary>
        /// Whether a code point is a pictograph an emoji sequence is made of (Extended_Pictographic in UAX #29, as ranges): the emoji blocks, the symbols and dingbats that have an emoji form, the arrows
        /// and the technical symbols that do. Digits and # * (keycap bases) are not.
        /// </summary>
        public static bool IsPictographic(int s) =>
            (s >= 0x1F000 && s <= 0x1FAFF) || (s >= 0x2600 && s <= 0x27BF) || (s >= 0x2300 && s <= 0x23FF) || (s >= 0x2190 && s <= 0x21FF) || (s >= 0x2B00 && s <= 0x2BFF) || (s >= 0x2900 && s <= 0x297F)
            || s == 0x00A9 || s == 0x00AE || s == 0x203C || s == 0x2049 || s == 0x2122 || s == 0x2139 || s == 0x3030 || s == 0x303D || s == 0x3297 || s == 0x3299 || s == 0x24C2 || (s >= 0x25A0 && s <= 0x25FF);

        /// <summary>Whether a code point is set full width on a board: the CJK blocks, kana, hangul, the full-width forms, the extension planes of the Han characters, the emoji blocks.</summary>
        public static bool IsWideScalar(int s) =>
            (s >= 0x2E80 && s <= 0x9FFF) || (s >= 0xAC00 && s <= 0xD7AF) || (s >= 0xF900 && s <= 0xFAFF) || (s >= 0xFE30 && s <= 0xFE4F) || (s >= 0xFF01 && s <= 0xFF60) || (s >= 0xFFE0 && s <= 0xFFE6)
            || (s >= 0x20000 && s <= 0x3FFFF) || (s >= 0x1F000 && s <= 0x1FAFF);

        // ------------------------------------------------------------------ clusters

        /// <summary>The length (UTF-16 units) of the cluster that starts at an index; at least 1. A line break is a cluster of its own.</summary>
        public static int LengthAt(string text, int index)
        {
            int first = ScalarAt(text, index, out int length);
            int end = index + length;
            if (first == '\n' || first == '\r')
                return length;
            bool pictographBase = IsPictographic(first);
            if (IsRegionalIndicator(first) && end < text.Length)
            {
                int next = ScalarAt(text, end, out int nextLength);
                if (IsRegionalIndicator(next))
                    end += nextLength;
            }

            while (end < text.Length)
            {
                int next = ScalarAt(text, end, out int nextLength);
                if (next == Zwj)
                {
                    // ZWJ joins the next pictograph to a cluster that began with one (UAX #29 GB11); else it only ends up inside (a joiner in a script).
                    end += nextLength;
                    if (pictographBase && end < text.Length)
                    {
                        int joined = ScalarAt(text, end, out int joinedLength);
                        if (IsPictographic(joined))
                            end += joinedLength;
                    }

                    continue;
                }

                if (!IsExtend(next))
                    break;
                end += nextLength;
            }

            return Math.Max(1, end - index);
        }

        /// <summary>The clusters of a text in order (a line break is one cluster; a lone surrogate half is replaced by U+FFFD).</summary>
        public static List<string> Split(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text))
                return list;
            for (int i = 0; i < text.Length;)
            {
                int length = LengthAt(text, i);
                list.Add(Clean(text.Substring(i, length)));
                i += length;
            }

            return list;
        }

        /// <summary>How many clusters a text has (line breaks included).</summary>
        public static int Count(string text)
        {
            int count = 0;
            if (string.IsNullOrEmpty(text))
                return 0;
            for (int i = 0; i < text.Length; i += LengthAt(text, i))
                count++;
            return count;
        }

        /// <summary>The text cut to at most this many clusters, at a boundary of a cluster (never inside a pair or a sequence).</summary>
        public static string Truncate(string text, int maxClusters)
        {
            if (string.IsNullOrEmpty(text) || maxClusters <= 0)
                return "";
            int i = 0, count = 0;
            while (i < text.Length && count < maxClusters)
            {
                i += LengthAt(text, i);
                count++;
            }

            return i >= text.Length ? text : text.Substring(0, i);
        }

        /// <summary>The text without its last cluster.</summary>
        public static string WithoutLast(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            int i = 0, lastStart = 0;
            while (i < text.Length)
            {
                lastStart = i;
                i += LengthAt(text, i);
            }

            return text.Substring(0, lastStart);
        }

        /// <summary>The cluster with each lone surrogate half replaced by U+FFFD (the same string when it has none).</summary>
        private static string Clean(string cluster)
        {
            bool lone = false;
            for (int i = 0; i < cluster.Length && !lone; i++)
            {
                if (char.IsSurrogate(cluster[i]))
                {
                    ScalarAt(cluster, i, out int length);
                    lone = length == 1;
                    i += length - 1;
                }
            }

            if (!lone)
                return cluster;
            var sb = new System.Text.StringBuilder(cluster.Length);
            foreach (int s in Scalars(cluster))
                sb.Append(FromScalar(s));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ what a cluster is

        /// <summary>Whether a cluster is drawn as an emoji: a pictograph in an emoji block, a symbol with the emoji selector, a sequence, a flag, a keycap.</summary>
        public static bool IsEmoji(string cluster)
        {
            if (string.IsNullOrEmpty(cluster))
                return false;
            for (int i = 0; i < cluster.Length;)
            {
                int s = ScalarAt(cluster, i, out int length);
                i += length;
                if (s == Vs16 || s == Keycap || s == Zwj || IsRegionalIndicator(s) || IsEmojiModifier(s) || (s >= 0x1F000 && s <= 0x1FAFF))
                    return true;
            }

            // A bare symbol of the old blocks (a star, a sun) is text unless the selector asked for the picture.
            return false;
        }

        /// <summary>Whether a cluster is more than one code point.</summary>
        public static bool IsSequence(string cluster)
        {
            if (string.IsNullOrEmpty(cluster))
                return false;
            ScalarAt(cluster, 0, out int length);
            return length < cluster.Length;
        }

        // ------------------------------------------------------------------ keys

        /// <summary>The key of a cluster: the code point when it is one, else a number of the registry (a registry that is full gives the first code point).</summary>
        public static int KeyOf(string cluster)
        {
            if (string.IsNullOrEmpty(cluster))
                return 0;
            int first = ScalarAt(cluster, 0, out int length);
            if (length >= cluster.Length)
                return first;
            lock (s_Lock)
            {
                if (s_Keys.TryGetValue(cluster, out int key))
                    return key;
                if (s_Texts.Count >= MaxRegistered)
                    return first;
                key = ClusterKeyBase + s_Texts.Count;
                s_Texts.Add(cluster);
                s_Keys[cluster] = key;
                return key;
            }
        }

        public static bool IsClusterKey(int key) => key >= ClusterKeyBase;

        /// <summary>The text of a key: the registry's cluster, or the code point.</summary>
        public static string TextOf(int key)
        {
            if (key >= ClusterKeyBase)
            {
                lock (s_Lock)
                {
                    int at = key - ClusterKeyBase;
                    return at < s_Texts.Count ? s_Texts[at] : "�";
                }
            }

            return FromScalar(key);
        }

        /// <summary>The first code point of a key's text (the key itself when it is a code point).</summary>
        public static int BaseOf(int key)
        {
            if (key < ClusterKeyBase)
                return key;
            string text = TextOf(key);
            return ScalarAt(text, 0, out _);
        }

        /// <summary>Whether a key is set full width: a code point of IsWideScalar, or a cluster that is drawn as an emoji.</summary>
        public static bool IsWideKey(int key) => key >= ClusterKeyBase ? IsEmoji(TextOf(key)) || IsWideScalar(BaseOf(key)) : IsWideScalar(key);

        /// <summary>For the tests: forgets the registry.</summary>
        internal static void ResetRegistry()
        {
            lock (s_Lock)
            {
                s_Keys.Clear();
                s_Texts.Clear();
            }
        }
    }
}
