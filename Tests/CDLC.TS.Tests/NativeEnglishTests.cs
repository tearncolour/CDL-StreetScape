using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// English is a language of the panel and not a translation laid over Chinese (0.12.80; the owner: "有没有原生英文支持"): with the game in English nothing the player reads has a Chinese character in it,
    /// except what the player or the sign itself says (a saved sign's own name, the Chinese on a board). Read here: the names of every sign, support and style, the lines of help and the keys' rows in every
    /// state, the English language file, the labels of the editor's fields, and the source's zh/en pairs (the English half may not hold a Chinese character or a Chinese name such as .Zh). The panel's
    /// own script is read by Tests/ui-language.mjs. The allow-list is one table: a string that must hold Chinese says why.
    /// </summary>
    internal static class NativeEnglishTests
    {
        /// <summary>CJK punctuation, the Han blocks and the full-width forms.</summary>
        private static readonly Regex s_Cjk = new Regex("[　-〿㐀-䶿一-鿿＀-￯]", RegexOptions.Compiled);

        /// <summary>The standard's own designation of a figure (禁7, 示17, 路8, 线5, 旅9): an identifier that the tile shows in both languages, so the English text that refers to a figure keeps it.</summary>
        private static readonly Regex s_Code = new Regex("[禁示警路线辅旅]\\d+(?:-\\d+)?", RegexOptions.Compiled);

        private const string Printed = "the label quotes the Chinese words the board prints (the player types them too)";

        /// <summary>Where: "choice:id", "support:id", "profile:id", "hint:kind", "key:kind:row", "lang:key", "field:template.key", "source:file:zh text". Why: what makes Chinese stay in the English text.</summary>
        internal static readonly (string Where, string Why)[] Allowed =
        {
            ("field:gb.prohib.41.reason", Printed),
            ("field:gb.mand.10.text", Printed),
            ("field:gb.mand.11.text", Printed),
            ("field:gb.mand.27.text", Printed),
            ("field:gb.guide8.10.name", Printed),
            ("field:gb.guide9.30.fx", Printed),
            ("field:gb.guide9.31.fx", Printed),
            ("field:gb.guide9.32.code", "the example is a plate prefix of a Chinese province"),
            ("field:gb17733.unit.name", Printed),
            ("field:mk.giveway.stop.word", Printed)
        };

        public static void RunAll()
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch (IOException) { }
            Program.Run("native English: the names of the signs, supports and styles", Names);
            Program.Run("native English: the lines of help in every state", Hints);
            Program.Run("native English: the keys' rows", Keys);
            Program.Run("native English: the language files", LangFiles);
            Program.Run("native English: the editor's labels and choices", Fields);
            Program.Run("native English: a code keeps the standard's designation and its words go into English", Codes);
            Program.Run("native English: the English half of the source's zh/en pairs", Source);
        }

        private static int s_Leaks;
        private static readonly HashSet<string> s_Seen = new HashSet<string>();
        private const int kPrinted = 40;

        private static void Begin()
        {
            s_Leaks = 0;
            s_Seen.Clear();
        }

        /// <summary>The English text has Chinese in it: failed with where and what (the first 40 of a section; all go to the file TS_TEXT_BUDGET_REPORT names when it is set, with the tag "cjk").</summary>
        private static void Leak(string where, string english)
        {
            if (string.IsNullOrEmpty(english) || !s_Cjk.IsMatch(s_Code.Replace(english, "")) || Allowed.Any(a => a.Where == where) || !s_Seen.Add(where + "\u0001" + english))
                return;
            string report = Environment.GetEnvironmentVariable("TS_TEXT_BUDGET_REPORT");
            if (!string.IsNullOrEmpty(report))
                File.AppendAllText(report, $"{where}\tcjk\t{english}\n", new UTF8Encoding(false));
            if (s_Leaks++ < kPrinted)
                Program.Check(false, $"Chinese in the English text: {where}: {english}");
            else if (s_Leaks == kPrinted + 1)
                Console.WriteLine("  (more leaks than printed: set TS_TEXT_BUDGET_REPORT=<file> to get them all)");
        }

        private static void Names()
        {
            Begin();
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                // A sign the player saved is named by the player.
                if (choice.Id.StartsWith(CustomSigns.ChoicePrefix, StringComparison.Ordinal))
                    continue;
                Program.Check(!string.IsNullOrWhiteSpace(choice.En), "the sign " + choice.Id + " has an English name");
                Leak("choice:" + choice.Id, choice.En);
                // The code is on the tile, in the tooltip and in the editor's title: for an English game the standard's designation or English words.
                Leak("code:" + choice.Id, choice.CodeEn);
            }

            foreach (SupportDef support in SupportCatalog.All)
            {
                Program.Check(!string.IsNullOrWhiteSpace(support.En), "the support " + support.Id + " has an English name");
                Leak("support:" + support.Id, support.En);
            }

            string root = TextBudgetTests.Root();
            Dictionary<string, string> en = root != null ? TextBudgetTests.Lang(root, "en-US.json") : new Dictionary<string, string>();
            Dictionary<string, string> zh = root != null ? TextBudgetTests.Lang(root, "zh-HANS.json") : new Dictionary<string, string>();
            foreach (StyleProfile profile in StyleProfiles.All)
            {
                Program.Check(!string.IsNullOrWhiteSpace(profile.En), "the style " + profile.Id + " has an English name");
                Leak("profile:" + profile.Id, profile.En);
                Program.Check(en.ContainsKey("CDLC.TS.UI.Profile." + profile.Id) && zh.ContainsKey("CDLC.TS.UI.Profile." + profile.Id), "the language files name the style " + profile.Id);
            }

            foreach (string family in SupportCatalog.All.Select(s => s.Family ?? "post").Distinct())
                Program.Check(en.ContainsKey("CDLC.TS.UI.Family." + family) && zh.ContainsKey("CDLC.TS.UI.Family." + family), "the language files name the support form " + family);

            Program.Check(s_Leaks == 0, "no sign, support or style has a Chinese English name: " + s_Leaks);
        }

        private static void Hints()
        {
            Begin();
            foreach (string kind in HintText.Kinds)
            {
                foreach ((HintInfo _, string _, string en, bool _) in TextBudgetTests.Lines(kind))
                    Leak("hint:" + kind, en);
            }

            Leak("hint:host-gone", HintText.HostGone(false));
            Program.Check(s_Leaks == 0, "no English line of help has Chinese in it, whatever name the tool gives it: " + s_Leaks);
        }

        private static void Keys()
        {
            Begin();
            foreach (string kind in HintText.Kinds)
            {
                for (int bits = 0; bits < 64; bits++)
                {
                    var info = new HintInfo { Kind = kind, Count = 3, Left = 1f, X = (bits & 1) != 0 ? 1f : 0f, Hung = (bits & 2) != 0, Turns = (bits & 4) != 0, Tilt = (bits & 8) != 0, Kerb = (bits & 16) != 0, Manual = (bits & 32) != 0, Name = "名" };
                    foreach (string[] row in HintText.Keys(info, false))
                    {
                        Leak("key:" + kind + ":" + row[0], row[0]);
                        Leak("key:" + kind + ":" + row[0], row[1]);
                    }
                }
            }

            Program.Check(s_Leaks == 0, "no English key row has Chinese in it: " + s_Leaks);
        }

        private static void LangFiles()
        {
            string root = TextBudgetTests.Root();
            if (root == null)
                return;
            Begin();
            Dictionary<string, string> en = TextBudgetTests.Lang(root, "en-US.json"), zh = TextBudgetTests.Lang(root, "zh-HANS.json");
            foreach (KeyValuePair<string, string> pair in en)
                Leak("lang:" + pair.Key, pair.Value);
            Program.Check(s_Leaks == 0, "the English language file has no Chinese in it: " + s_Leaks);

            // A Chinese value that is the English one word for word is a string nobody translated (a unit or a name has no letters of a sentence).
            int copies = 0;
            foreach (KeyValuePair<string, string> pair in zh)
            {
                if (en.TryGetValue(pair.Key, out string e) && e == pair.Value && !s_Cjk.IsMatch(e) && Regex.IsMatch(e, "[A-Za-z]{4,}") && !Allowed.Any(a => a.Where == "lang:" + pair.Key))
                {
                    copies++;
                    Program.Check(false, "the Chinese language file holds the English text of " + pair.Key + ": " + pair.Value);
                }
            }

            Program.Check(copies == 0, "every Chinese value is translated: " + copies);
        }

        private static void Fields()
        {
            Begin();
            var done = new HashSet<string>();
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                GuideTemplate template = choice.Def?.Template;
                if (template == null || !done.Add(template.Id))
                    continue;
                foreach (GuideField field in template.Fields)
                {
                    Leak("field:" + template.Id + "." + field.Key, field.En);
                    if (field.Choices == null)
                        continue;
                    foreach (GuideChoice c in field.Choices)
                        Leak("choice:" + template.Id + "." + field.Key + "." + c.Value, c.En);
                }
            }

            Program.Check(s_Leaks == 0, "no English label of a field or a choice has Chinese in it: " + s_Leaks);
        }

        private static void Codes()
        {
            Program.Check(CodeNames.En("禁7") == "禁7" && CodeNames.En("旅9") == "旅9" && CodeNames.En("禁7、禁8") == "禁7, 禁8", "the standard's designations stay: " + CodeNames.En("禁7、禁8"));
            Program.Check(CodeNames.En("图268") == "Fig. 268" && CodeNames.En("图B.1～B.3") == "Fig. B.1–B.3" && CodeNames.En("图324a") == "Fig. 324a", "a figure is a figure: " + CodeNames.En("图B.1～B.3"));
            Program.Check(CodeNames.En("线4（图5）") == "线4 (Fig. 5)" && CodeNames.En("6.9.1（图87）") == "6.9.1 (Fig. 87)", "a figure in a bracket: " + CodeNames.En("线4（图5）"));
            Program.Check(CodeNames.En("附录C") == "App. C" && CodeNames.En("杭州导则 B.2、B.3") == "Hangzhou guide B.2, B.3" && CodeNames.En("自定义（非国标）") == "Custom (Non-GB)", "a word, a source and a bracket: " + CodeNames.En("自定义（非国标）"));
            Program.Check(CodeNames.En("城市道路") == "City road" && CodeNames.En("城市") == "City", "the longer word is taken first: " + CodeNames.En("城市道路"));
            Program.Check(CodeNames.En("") == "" && CodeNames.En(null) == "", "no code, no text");
        }

        private static readonly Regex s_Pair =new Regex("zh\\s*\\?\\s*\\$?\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\\$?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly Regex s_Say = new Regex("Say\\(\\s*\\$?\"((?:[^\"\\\\]|\\\\.)*)\"\\s*,\\s*\\$?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly Regex s_ChineseName = new Regex("\\{[^}]*\\.Zh\\b[^}]*\\}", RegexOptions.Compiled);

        /// <summary>The folders whose zh/en pairs the player reads. They do not all compile into the runner (the panel's model and the tool need the game), so they are read as text.</summary>
        private static readonly string[] s_Folders = { "Systems", "Tool", "Geo" };

        private static void Source()
        {
            string root = TextBudgetTests.Root();
            if (root == null)
                return;
            Begin();
            foreach (string folder in s_Folders)
            {
                string dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir))
                    continue;
                foreach (string path in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(path, Encoding.UTF8);
                    string name = path.Substring(root.Length + 1).Replace('\\', '/');
                    foreach (Regex pattern in new[] { s_Pair, s_Say })
                    {
                        foreach (Match m in pattern.Matches(text))
                        {
                            string zh = m.Groups[1].Value, en = m.Groups[2].Value;
                            string where = "source:" + name + ":" + (zh.Length > 40 ? zh.Substring(0, 40) : zh);
                            Leak(where, en);
                            // The Chinese name of a sign or a support in the English half: a hole such as {choice.Zh}.
                            if (s_ChineseName.IsMatch(en) && !Allowed.Any(a => a.Where == where) && s_Seen.Add("name\u0001" + en))
                            {
                                s_Leaks++;
                                Program.Check(false, $"the English half names a sign in Chinese: {where}: {en}");
                            }
                        }
                    }
                }
            }

            Program.Check(s_Leaks == 0, "no English half of a source pair has Chinese in it: " + s_Leaks);
        }
    }
}
