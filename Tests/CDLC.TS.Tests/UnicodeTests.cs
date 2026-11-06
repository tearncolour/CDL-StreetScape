using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Unicode in the text light box (0.12.78; the owner, 2 Nov: "应该使用Unicode，支持Emoji"): a text is made of clusters, not UTF-16 units: a pair, an emoji sequence, a flag, a keycap are one cell; the spec keeps them
    /// whole through the sanitiser, the byte limit, the save and the layout's limits.
    /// </summary>
    internal static class UnicodeTests
    {
        public static void RunAll()
        {
            Program.Run("unicode: clusters of code points, emoji sequences, flags, keycaps, malformed text", Clusters);
            Program.Run("unicode: the keys of clusters", Keys);
            Program.Run("unicode: truncation and the last cluster", Truncation);
            Program.Run("unicode: which clusters are emoji and which are wide", Classes);
            Program.Run("unicode: the layout counts clusters", Layout);
            Program.Run("unicode: the text field keeps what it can draw and cuts at clusters", Field);
            Program.Run("unicode: the spec and the save carry emoji whole", RoundTrips);
        }

        // Pictographs written by their code points, so that no tool between the source and the compiler can change them.
        private static string U(params int[] scalars) => string.Concat(scalars.Select(char.ConvertFromUtf32));

        private static readonly string Grin = U(0x1F600), Family = U(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467), FamilyOfFour = U(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, 0x200D, 0x1F466);
        private static readonly string Thumb = U(0x1F44D, 0x1F3FD), RainbowFlag = U(0x1F3F3, 0xFE0F, 0x200D, 0x1F308), China = U(0x1F1E8, 0x1F1F3), Usa = U(0x1F1FA, 0x1F1F8);
        private static readonly string Keycap = U('1', 0xFE0F, 0x20E3), HashKeycap = U('#', 0xFE0F, 0x20E3), HeartText = U(0x2764), Heart = U(0x2764, 0xFE0F);
        private static readonly string England = U(0x1F3F4, 0xE0067, 0xE0062, 0xE0065, 0xE006E, 0xE0067, 0xE007F), Accent = "e" + U(0x301), ExtB = U(0x20000), ExtBWithSelector = U(0x20000, 0xE0100);

        private static void Clusters()
        {
            void Count(string text, int expect, string what) => Program.Check(TextClusters.Split(text).Count == expect && TextClusters.Count(text) == expect, what + ": " + TextClusters.Split(text).Count + " cluster(s) of " + expect);
            Count("", 0, "empty");
            Count(null, 0, "null");
            Count("abc", 3, "latin");
            Count("文字灯箱", 4, "han");
            Count(Grin, 1, "a pair is one");
            Count(Grin + Grin, 2, "two pairs");
            Count(Accent, 1, "a letter and its combining mark");
            Count(Heart, 1, "a symbol with the emoji selector");
            Count(HeartText, 1, "a symbol alone");
            Count(Thumb, 1, "a skin tone");
            Count(Family, 1, "a ZWJ family");
            Count(FamilyOfFour, 1, "a longer ZWJ family");
            Count(RainbowFlag, 1, "a ZWJ sequence with a selector");
            Count(China, 1, "a flag is two regional indicators");
            Count(China + Usa, 2, "two flags");
            Count(U(0x1F1E8, 0x1F1F3, 0x1F1FA), 2, "an odd regional indicator stands alone");
            Count(Keycap, 1, "a keycap");
            Count(HashKeycap, 1, "a keycap of #");
            Count(England, 1, "a tag sequence (a subdivision flag)");
            Count("a" + Grin + "b" + Family + "文", 5, "a mix");
            Count(ExtB, 1, "a Han character of the extension plane");
            Count(ExtBWithSelector, 1, "with an ideographic variation selector");
            Count("a\nb", 3, "a line break is a cluster");
            Count("a\r\nb", 4, "a carriage return and a line feed are two");
            Count("a" + U(0x200D) + "b", 2, "a joiner between letters ends up inside the first");
            Count(Grin + U(0x200D) + "a", 2, "a joiner before a letter does not join it");
            Count(U(0x200D), 1, "a joiner alone");

            // Malformed text: a lone half becomes U+FFFD and is a cluster of its own; nothing throws.
            string high = "\uD83D", low = "\uDE00";
            Count(high, 1, "a lone high half");
            Count(low + "a", 2, "a lone low half");
            Count(high + "a", 2, "a high half before a letter");
            Count("a" + low + high, 3, "a reversed pair is two lone halves");
            Program.Check(TextClusters.Scalars("a" + high + "b").SequenceEqual(new[] { (int)'a', 0xFFFD, (int)'b' }) && TextClusters.Scalars(Grin + low).SequenceEqual(new[] { 0x1F600, 0xFFFD }), "a lone half is U+FFFD among the code points");
            Count("a\n" + U(0x301), 3, "a line break does not take the mark after it");
            Count("a\r" + U(0xFE0F), 3, "nor does a carriage return");
            Program.Check(TextClusters.Split(high + "a")[0] == "�", "a lone half is replaced by U+FFFD: " + TextClusters.Split(high + "a")[0]);
            Program.Check(TextClusters.Split(Grin).Single() == Grin && TextClusters.Split(Family).Single() == Family, "a cluster is its own text, untouched");

            // The lengths add up to the text (nothing lost, nothing twice) and a long text is quick.
            string long10k = string.Concat(Enumerable.Repeat(Family + Grin + "a", 3000));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            List<string> many = TextClusters.Split(long10k);
            Program.Check(many.Count == 9000 && string.Concat(many) == long10k, "9,000 clusters of a long text, joined as they were");
            Program.Check(clock.ElapsedMilliseconds < 2000, "and fast: " + clock.ElapsedMilliseconds + " ms");
            for (int i = 0; i < 3000; i++)
            {
                int at = 0;
                string text = "x" + Family + "y";
                while (at < text.Length)
                    at += TextClusters.LengthAt(text, at);
                if (at != text.Length)
                {
                    Program.Check(false, "the cluster lengths do not add up");
                    break;
                }
            }

            // Every prefix of a text of every kind ends on a cluster edge: Truncate never leaves half a pair or half a sequence.
            string all = "a" + Grin + Family + Thumb + China + Keycap + England + Accent + "文";
            for (int n = 0; n <= TextClusters.Count(all); n++)
            {
                string cut = TextClusters.Truncate(all, n);
                bool paired = true;
                for (int i = 0; i < cut.Length; i++)
                {
                    if (char.IsHighSurrogate(cut[i]) && (i + 1 >= cut.Length || !char.IsLowSurrogate(cut[i + 1])))
                        paired = false;
                    if (char.IsLowSurrogate(cut[i]) && (i == 0 || !char.IsHighSurrogate(cut[i - 1])))
                        paired = false;
                    if (char.IsHighSurrogate(cut[i]))
                        i++;
                }

                Program.Check(paired && TextClusters.Count(cut) == n && all.StartsWith(cut, StringComparison.Ordinal), "a text cut to " + n + " cluster(s) is whole: " + TextClusters.Count(cut));
            }
        }

        private static void Keys()
        {
            TextClusters.ResetRegistry();
            Program.Check(TextClusters.KeyOf("a") == 'a' && TextClusters.KeyOf("文") == 0x6587 && TextClusters.KeyOf(Grin) == 0x1F600, "a code point is its own key, a pair too (every text without a sequence is as it was)");
            int family = TextClusters.KeyOf(Family), flag = TextClusters.KeyOf(China);
            Program.Check(family >= TextClusters.ClusterKeyBase && flag >= TextClusters.ClusterKeyBase && family != flag, "a sequence has a key of the registry, a different one each");
            Program.Check(TextClusters.KeyOf(Family) == family && TextClusters.KeyOf(U(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467)) == family, "the same sequence, the same key");
            Program.Check(TextClusters.TextOf(family) == Family && TextClusters.TextOf(0x1F600) == Grin && TextClusters.TextOf('a') == "a", "a key gives its text back");
            Program.Check(TextClusters.BaseOf(family) == 0x1F468 && TextClusters.BaseOf(flag) == 0x1F1E8 && TextClusters.BaseOf(0x6587) == 0x6587, "the first code point of a key");
            Program.Check(TextClusters.IsClusterKey(family) && !TextClusters.IsClusterKey(0x1F600) && !TextClusters.IsClusterKey(0x10FFFF), "which keys are clusters");
            Program.Check(TextClusters.TextOf(TextClusters.ClusterKeyBase + 5000) == "�" && TextClusters.TextOf(0xD800) == "�" && TextClusters.TextOf(-1) == "�", "an unknown key is U+FFFD, not an exception");
            Program.Check(TextClusters.KeyOf("") == 0 && TextClusters.KeyOf(null) == 0, "no text, no key");

            // Many threads asking for the same sequences get the same keys.
            TextClusters.ResetRegistry();
            var bags = new int[8][];
            var threads = new List<System.Threading.Thread>();
            for (int t = 0; t < 8; t++)
            {
                int index = t;
                var thread = new System.Threading.Thread(() =>
                {
                    bags[index] = new int[200];
                    for (int i = 0; i < 200; i++)
                        bags[index][i] = TextClusters.KeyOf(U(0x1F468, 0x200D, 0x1F300 + i % 50));
                });
                threads.Add(thread);
                thread.Start();
            }

            foreach (System.Threading.Thread thread in threads)
                thread.Join();
            bool same = true;
            for (int t = 1; t < 8; t++)
            {
                for (int i = 0; i < 200; i++)
                    same &= bags[t][i] == bags[0][i];
            }

            Program.Check(same && bags[0].Distinct().Count() == 50, "eight threads, one key for each sequence: " + bags[0].Distinct().Count() + " keys");

            // A full registry draws the first code point rather than growing without end.
            TextClusters.ResetRegistry();
            int last = 0;
            for (int i = 0; i < TextClusters.MaxRegistered + 10; i++)
                last = TextClusters.KeyOf(U(0x1F468, 0x200D, 0x20000 + i));
            Program.Check(last == 0x1F468, "past the registry's size a sequence is its first code point: " + last);
            TextClusters.ResetRegistry();
        }

        private static void Truncation()
        {
            Program.Check(TextClusters.Truncate(Family + "x", 1) == Family && TextClusters.Truncate(Family + "x", 2) == Family + "x" && TextClusters.Truncate(Family + "x", 0) == "", "a family is cut whole");
            Program.Check(TextClusters.Truncate("abc", 10) == "abc" && TextClusters.Truncate("", 3) == "" && TextClusters.Truncate(null, 3) == "", "nothing to cut");
            Program.Check(TextClusters.WithoutLast("a" + Family) == "a" && TextClusters.WithoutLast(Family) == "" && TextClusters.WithoutLast("") == "" && TextClusters.WithoutLast("ab") == "a", "the last cluster goes, whole");
            Program.Check(TextClusters.WithoutLast("a" + Grin) == "a" && TextClusters.WithoutLast(Thumb + Grin) == Thumb, "a pair goes whole");
        }

        private static void Classes()
        {
            Program.Check(TextClusters.IsEmoji(Grin) && TextClusters.IsEmoji(Family) && TextClusters.IsEmoji(China) && TextClusters.IsEmoji(Keycap) && TextClusters.IsEmoji(Heart) && TextClusters.IsEmoji(Thumb) && TextClusters.IsEmoji(England), "emoji");
            Program.Check(!TextClusters.IsEmoji("a") && !TextClusters.IsEmoji("1") && !TextClusters.IsEmoji("文") && !TextClusters.IsEmoji(HeartText) && !TextClusters.IsEmoji(ExtB) && !TextClusters.IsEmoji(Accent) && !TextClusters.IsEmoji("") && !TextClusters.IsEmoji(null), "not emoji: letters, digits, Han, a symbol without the selector");
            Program.Check(TextClusters.IsSequence(Family) && TextClusters.IsSequence(Grin) == false && TextClusters.IsSequence("a") == false && TextClusters.IsSequence(Heart) && !TextClusters.IsSequence(""), "a sequence is more than one code point");
            Program.Check(TextClusters.IsPictographic(0x1F600) && TextClusters.IsPictographic(0x2764) && TextClusters.IsPictographic(0x00A9) && !TextClusters.IsPictographic('a') && !TextClusters.IsPictographic('1') && !TextClusters.IsPictographic(0x6587), "pictographs");
            Program.Check(TextLayout.IsWide(0x6587) && TextLayout.IsWide(0x1F600) && TextLayout.IsWide(0x20000) && TextLayout.IsWide(0x3FFFF) && !TextLayout.IsWide('a') && !TextLayout.IsWide(0x2764) && !TextLayout.IsWide(0x40000), "wide: Han, emoji, the extension planes; not letters or a bare symbol");
            Program.Check(TextLayout.IsWide(TextClusters.KeyOf(Family)) && TextLayout.IsWide(TextClusters.KeyOf(Heart)) && TextLayout.IsWide(TextClusters.KeyOf(China)), "a sequence of emoji is wide");
            Program.Check(!TextLayout.IsWide(TextClusters.KeyOf(Accent)), "a letter with a mark is not");
            Program.Check(TextClusters.IsRegionalIndicator(0x1F1E6) && TextClusters.IsRegionalIndicator(0x1F1FF) && !TextClusters.IsRegionalIndicator(0x1F1E5) && !TextClusters.IsRegionalIndicator(0x1F200), "regional indicators");
            Program.Check(TextClusters.IsEmojiModifier(0x1F3FB) && TextClusters.IsEmojiModifier(0x1F3FF) && !TextClusters.IsEmojiModifier(0x1F3FA) && !TextClusters.IsEmojiModifier(0x1F400), "skin tones");
            Program.Check(TextClusters.IsVariationSelector(0xFE00) && TextClusters.IsVariationSelector(0xFE0F) && TextClusters.IsVariationSelector(0xE0100) && TextClusters.IsVariationSelector(0xE01EF) && !TextClusters.IsVariationSelector(0xFE10) && !TextClusters.IsVariationSelector(0xE01F0), "variation selectors");
        }

        private static TextFace Fake() => new TextFace(1000f, -120f, 880f, c => c == 'a' || c == 'b' ? 500f : TextLayout.IsWide(c) ? 1000f : -1f);

        private static void Layout()
        {
            TextClusters.ResetRegistry();
            TextStyle style = TextStyle.Of(1f);
            TextLayoutResult mixed = TextLayout.Layout("a" + Grin + "b" + Family, style, Fake());
            Program.Check(mixed.Glyphs.Count == 4 && mixed.Glyphs[1].Codepoint == 0x1F600 && mixed.Glyphs[3].Codepoint == TextClusters.KeyOf(Family) && !mixed.Truncated, "four cells: a pair is one, a family is one");
            Program.Near(mixed.Glyphs[1].Advance, 1f, 1e-5f, "an emoji is a full em wide");
            Program.Near(mixed.LineWidths[0], 0.5f + 1f + 0.5f + 1f, 1e-5f, "the line's width");

            // The limits count clusters: 12 to a line, 36 in all.
            string families12 = string.Concat(Enumerable.Repeat(Family, 12)), families13 = families12 + Family;
            TextLayoutResult line12 = TextLayout.Layout(families12, style, Fake()), line13 = TextLayout.Layout(families13, style, Fake());
            Program.Check(line12.Glyphs.Count == 12 && !line12.Truncated && line13.Glyphs.Count == 12 && line13.Truncated, "a line of 12 families is whole, a 13th is left out and says so");
            string three = families12 + "\n" + families12 + "\n" + families12;
            TextLayoutResult all36 = TextLayout.Layout(three, style, Fake());
            Program.Check(all36.Glyphs.Count == 36 && all36.Lines == 3 && !all36.Truncated, "three lines of 12 families: 36 cells");
            TextLayoutResult over = TextLayout.Layout(three + "\n" + Grin, style, Fake());
            Program.Check(over.Glyphs.Count == 36 && over.Truncated, "a fourth line is left out");

            // A cluster the face has no glyph for keeps its cell as a blank, once in the missing list, as the key.
            TextFace none = new TextFace(1000f, -120f, 880f, c => -1f);
            TextLayoutResult blank = TextLayout.Layout(Family + Family + "x", style, none);
            Program.Check(blank.Glyphs.All(g => g.Missing) && blank.Missing.Count == 2 && blank.Missing[0] == TextClusters.KeyOf(Family), "missing clusters are listed once each");
            Program.Near(blank.Glyphs[0].Advance, 1f, 1e-5f, "a missing emoji keeps a full em");
            Program.Near(blank.Glyphs[2].Advance, 0.5f, 1e-5f, "a missing letter keeps half an em");

            // The layout of text with no sequence in it is what it was: the code point is the key.
            TextLayoutResult plain = TextLayout.Layout("ab文", style, Fake());
            Program.Check(plain.Glyphs.Select(g => g.Codepoint).SequenceEqual(new[] { (int)'a', (int)'b', 0x6587 }), "plain text keeps code points as keys");
            TextLayoutResult crlf = TextLayout.Layout("a\r\nb", style, Fake());
            Program.Check(crlf.Lines == 2 && crlf.Glyphs.Count == 2, "a carriage return and a line feed are one break");
        }

        private static void Field()
        {
            GuideTemplate template = TextBoxes.Template;
            GuideSpec Parse(string text) => template.Parse("text=" + text.Replace(";", "\\;").Replace("=", "\\="));
            Program.Check(Parse(Grin + "a" + Family).Get("text") == Grin + "a" + Family, "the text box keeps emoji and a sequence");
            Program.Check(Parse("a\uD83Db").Get("text") == "ab" && Parse("\uDE00a").Get("text") == "a", "a lone half is dropped");
            Program.Check(Parse("a" + Grin).Get("text") == "a" + Grin && Parse("a\u0007b").Get("text") == "ab", "a control character is dropped, a pair is not");

            // The limit counts clusters: 40 families fit where 40 UTF-16 units held 5.
            string forty = string.Concat(Enumerable.Repeat(Grin, 40)), over = forty + Grin;
            Program.Check(TextClusters.Count(Parse(forty).Get("text")) <= 40 && TextClusters.Count(Parse(over).Get("text")) <= 40, "at most 40 clusters");
            string cut = Parse(over).Get("text");
            Program.Check(cut.Length % 2 == 0 && TextClusters.Split(cut).All(c => c == Grin), "cut at a cluster: no half of a pair");

            // The spec has a size in bytes: a text too long for it loses its last clusters, whole.
            string families = string.Concat(Enumerable.Repeat(FamilyOfFour, 36));
            GuideSpec big = Parse(families);
            string bigText = big.Get("text");
            Program.Check(Encoding.UTF8.GetByteCount(big.ToString()) <= GuideSpec.MaxBytes, "the spec fits its field: " + Encoding.UTF8.GetByteCount(big.ToString()) + " bytes");
            Program.Check(bigText.Length > 0 && TextClusters.Split(bigText).All(c => c == FamilyOfFour), "what is left is whole families: " + TextClusters.Count(bigText) + " of 36");
            Program.Check(!bigText.Contains("�"), "no replacement character from a cut pair");

            // The fields that are not the text of a board keep dropping the pairs (what draws them cannot).
            GuideField plain = GuideField.Text("name", "名", "Name", "", 20);
            Program.Check(plain.Clusters == false && GuideField.Text("t", "z", "e", "", 5).Unicode().Clusters, "only a field marked Unicode keeps them");
            GuideTemplate other = Plain("a" + Grin + "b");
            Program.Check(other.Parse("name=a" + Grin + "b").Get("name") == "ab", "an ordinary text field drops an emoji as before");

            // The editor's model tells the panel not to cut by UTF-16 units: max 0 for the text of a board.
            Program.Check(TextBoxes.Template.FieldOf("text").Clusters && StandBoxes.Template.FieldOf("text").Clusters, "the text box and the standing box take emoji");
        }

        private static GuideTemplate Plain(string value) => new GuideTemplate { Id = "plain.test", Zh = "测试", En = "Test", Fields = new[] { GuideField.Text("name", "名", "Name", "", 20) } };

        private static void RoundTrips()
        {
            string text = Grin + "a" + Family + ";=" + "\\" + China + Keycap + England + ExtB + "文";
            GuideSpec spec = TextBoxes.Template.Parse("text=" + text.Replace("\\", "\\\\").Replace(";", "\\;").Replace("=", "\\="));
            string written = spec.ToString();
            GuideSpec again = TextBoxes.Template.Parse(written);
            Program.Check(spec.Get("text") == text && again.Get("text") == text && again.ToString() == written, "separators and a backslash among emoji: written and read again");
            Program.Check(spec.Hash == again.Hash, "the same hash");

            // The save record (the game keeps the spec in a field of 509 bytes: the spec is cut at 480).
            var record = new SaveRecord { ModVersion = "0.12.78", City = "c", SaveId = "s", SaveName = "n", CityName = "m", Time = 1L };
            record.Items.Add(new StoredItem { TypeHash = 9, Position = new Vec3(1f, 2f, 3f), Spec = written, Anchor = -1 });
            var stream = new MemoryStream();
            record.Write(stream);
            stream.Position = 0;
            SaveRecord back = SaveRecord.Read(stream);
            Program.Check(back.Items.Count == 1 && back.Items[0].Spec == written, "the save record's text, byte for byte");
            // The save field is bytes (TsItem): the spec goes through UTF-8 and comes back the same, a pair and a sequence whole.
            byte[] bytes = Encoding.UTF8.GetBytes(written);
            Program.Check(bytes.Length <= GuideSpec.MaxBytes && Encoding.UTF8.GetString(bytes) == written && TextBoxes.Template.Parse(Encoding.UTF8.GetString(bytes)).Get("text") == text, "through the save field's bytes and back: " + bytes.Length + " bytes");

            // The planner's job key tells two emoji apart and the same emoji alike.
            var one = new TextPlanJob(Grin, 1, 0.5f, 0f, 1.15f, TextAlign.Centre, 0.002f);
            var two = new TextPlanJob(U(0x1F601), 1, 0.5f, 0f, 1.15f, TextAlign.Centre, 0.002f);
            var same = new TextPlanJob(Grin, 1, 0.5f, 0f, 1.15f, TextAlign.Centre, 0.002f);
            Program.Check(one.Key != two.Key && one.Key == same.Key && one.Equals(same) && !one.Equals(two), "plan jobs for different emoji differ");

            // The text of the hint for a character the font lacks.
            string shown = TextHints.Of(new TextWarning(TextWarningKind.MissingCharacter, codepoint: TextClusters.KeyOf(Family)), true);
            Program.Check(shown.Contains(Family), "the hint names the cluster, whole: " + shown);
            string simple = TextHints.Of(new TextWarning(TextWarningKind.MissingCharacter, codepoint: 0x1F600), false);
            Program.Check(simple.Contains(Grin), "and a single emoji: " + simple);
        }
    }
}
