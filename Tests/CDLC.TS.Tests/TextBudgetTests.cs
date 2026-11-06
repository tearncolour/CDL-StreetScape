using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The length budget of everything the player reads (0.12.80; the owner asked three times for shorter texts: "精简文本", then "现在的文本还是过长，包括各种提示文本"): the lines of help for every kind
    /// and every flag the tool can set, the keys' labels, the language files, the labels of the editor's fields and choices, and the zh/en pairs of the panel's own model (read from the source, for that
    /// code does not compile into the test runner). A line over its budget fails with what it is, so that a text added later is shortened when it is added, not by the next complaint. The
    /// allow-list is one table: a string that must be long says why. Characters, not bytes (a Chinese character is one).
    /// </summary>
    internal static class TextBudgetTests
    {
        /// <summary>A standing line (the help line while a kind holds, a caption), a state line (a refusal or a reading that shows only when it applies), a tooltip, the saved-data lines.</summary>
        internal static readonly (int Zh, int En) Standing = (20, 48), State = (28, 64), Tip = (40, 90), SaveData = (30, 70), Label = (16, 34), Choice = (20, 44), KeyRow = (10, 22), KeyName = (16, 26);

        /// <summary>
        /// Where: "hint:kind", "key:kind", "lang:key", "field:template.key", "choice:template.key.value", "source:file:zh text". Why: what makes the string long. Empty by intent: a text over its budget is cut.
        /// </summary>
        internal static readonly (string Where, string Why)[] Allowed =
        {
        };

        public static void RunAll()
        {
            // The offenders are Chinese text: the console must say it in UTF-8 or the list cannot be read.
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch (IOException) { }
            Program.Run("text budget: the lines of help of every kind and flag", Hints);
            Program.Run("text budget: the keys' rows", KeyRows);
            Program.Run("text budget: the language files", LangFiles);
            Program.Run("text budget: the labels of the editor's fields and choices", FieldLabels);
            Program.Run("text budget: the zh/en pairs of the panel's own model", ModelSource);
            Program.Run("text budget: zh and en carry the same numbers", Parity);
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsAllowed(string where) => Allowed.Any(a => a.Where == where);

        private static int Len(string s) => new StringInfo(s ?? "").LengthInTextElements;

        private static string Short(string s) => s.Length > 70 ? s.Substring(0, 70) + "..." : s;

        private static int s_Offenders;
        private static readonly HashSet<string> s_Seen = new HashSet<string>();

        private static void Begin()
        {
            s_Offenders = 0;
            s_Seen.Clear();
        }

        /// <summary>How many of a section's offenders are printed (all of them are counted, and written to the file TS_TEXT_BUDGET_REPORT names when it is set).</summary>
        private const int kPrinted = 40;

        private static readonly HashSet<string> s_Dumped = new HashSet<string>();

        /// <summary>When the environment names a file in TS_TEXT_DUMP: every text the test measures, in budget or not (where, language, length, text), for a before/after comparison of two commits.</summary>
        private static void Dump(string where, bool zh, string text)
        {
            string file = Environment.GetEnvironmentVariable("TS_TEXT_DUMP");
            if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(text) && s_Dumped.Add(where + "\u0001" + zh + "\u0001" + text))
                File.AppendAllText(file, $"{where}\t{(zh ? "zh" : "en")}\t{Len(text)}\t{text}\n", new UTF8Encoding(false));
        }

        private static void Over(string where, bool zh, string text, int budget)
        {
            Dump(where, zh, text);
            if (IsAllowed(where) || Len(text) <= budget || !s_Seen.Add((zh ? "zh\u0001" : "en\u0001") + text))
                return;
            string line = $"{where}\t{(zh ? "zh" : "en")}\t{Len(text)}\t{budget}\t{text}";
            string report = Environment.GetEnvironmentVariable("TS_TEXT_BUDGET_REPORT");
            if (!string.IsNullOrEmpty(report))
                File.AppendAllText(report, line + "\n", new UTF8Encoding(false));
            if (s_Offenders++ < kPrinted)
                Program.Check(false, $"text over its budget: {where} {(zh ? "zh" : "en")} {Len(text)} > {budget}: {Short(text)}");
            else if (s_Offenders == kPrinted + 1)
                Console.WriteLine("  (more offenders than printed: set TS_TEXT_BUDGET_REPORT=<file> to get them all)");
        }

        /// <summary>The root of the repository (the folder with lang/en-US.json), found from where the runner stands.</summary>
        internal static string Root()
        {
            for (string dir = AppContext.BaseDirectory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                if (File.Exists(Path.Combine(dir, "lang", "en-US.json")))
                    return dir;
            }

            return null;
        }

        internal static Dictionary<string, string> Lang(string root, string file)
        {
            string text = File.ReadAllText(Path.Combine(root, "lang", file), Encoding.UTF8);
            Program.Check(MiniJson.TryParse(text, out JsonValue json, out string error), file + " parses: " + error);
            var map = new Dictionary<string, string>();
            if (json != null)
            {
                foreach (KeyValuePair<string, JsonValue> member in json.Members)
                    map[member.Key] = member.Value.Text;
            }

            return map;
        }

        // ------------------------------------------------------------------ the lines of help

        /// <summary>Kinds whose line is the same whatever is going on: a standing line. The others show a refusal or a reading: a state line.</summary>
        private static readonly HashSet<string> s_Standing = new HashSet<string>
        {
            "choose", "road", "place", "delete", "edit", "move", "cut-start", "mark-stamp", "mark-run-start", "mark-bend", "mark-bend-start", "line-start", "pick", "pick-mode", "mark-gore-free", "arm", "host", "wall", "roof"
        };

        /// <summary>
        /// Every state a kind can be in: the flags together (so the longest line of each is found), the names the kinds read, the counts and the numbers at the width they are in the game. Distinct
        /// lines only are returned, with the state that made each.
        /// </summary>
        internal static List<(HintInfo Info, string Zh, string En, bool Plain)> Lines(string kind)
        {
            if (s_LineCache.TryGetValue(kind, out List<(HintInfo Info, string Zh, string En, bool Plain)> cached))
                return cached;
            var found = new List<(HintInfo Info, string Zh, string En, bool Plain)>();
            var seen = new HashSet<string>();
            string[] names = NamesOf(kind);
            for (int bits = 0; bits < 256; bits++)
            {
                foreach (string name in names)
                {
                    foreach (int count in new[] { 0, 1, 3 })
                    {
                        foreach (float x in new[] { 0f, 1.25f })
                        foreach (int mid in new[] { 0, 1, 2, 3 })
                        {
                            var info = new HintInfo
                            {
                                Kind = kind, Count = count, Left = 12.5f, Need = 3.5f, X = x, Max = (bits & 128) != 0 ? 40 : 0, Span = 14.5f, Grew = (bits & 1) != 0, Manual = (bits & 2) != 0, Hung = (bits & 4) != 0,
                                Bad = (bits & 8) != 0, Name = name, NameEn = name.Length > 0 && kind == "pick" ? "No entry" : "", Host = (bits & 16) != 0 ? "lamp" : "", Turns = (bits & 32) != 0, Kerb = false, Tilt = (bits & 64) != 0, MidOn = mid > 0, MidKey = mid >= 2, MidHere = mid == 3, TwoSided = (bits & 4) != 0, Back = (bits & 1) != 0
                            };
                            string zh = HintText.Of(info, true), en = HintText.Of(info, false);
                            if (seen.Add(zh + "\u0001" + en))
                                found.Add((info, zh, en, !info.Bad && mid != 1 && mid != 2));
                        }
                    }
                }
            }

            return s_LineCache[kind] = found;
        }

        private static readonly Dictionary<string, List<(HintInfo Info, string Zh, string En, bool Plain)>> s_LineCache = new Dictionary<string, List<(HintInfo Info, string Zh, string En, bool Plain)>>();

        /// <summary>The names a kind is given: the support's name (the longest there is) for a wall or a roof, the words the others read; none for a kind that reads none.</summary>
        private static string[] NamesOf(string kind)
        {
            switch (kind)
            {
                case "pick":
                    return new[] { "", "禁止驶入" };
                case "wall":
                case "roof":
                    return new[] { "", SupportCatalog.Listed.Where(s => SupportKinds.OnWall(s.Kind)).OrderByDescending(s => s.Zh.Length).First().Zh };
                case "mark-waiting":
                    return new[] { "", "road", "room", "none" };
                case "bar-auto":
                    return new[] { "", "road", "Highway", "National", "City" };
                case "mark-auto":
                case "mark-auto-marked":
                    return new[] { "", "road", "e" };
                case "mark-clear":
                    return new[] { "", "n", "r", "e" };
                case "mark-poly":
                    return new[] { "", "clear", "grid" };
                default:
                    return new[] { "" };
            }
        }

        private static void Hints()
        {
            Begin();
            foreach (string kind in HintText.Kinds)
            {
                (int Zh, int En) budget = s_Standing.Contains(kind) ? Standing : State;
                // The longest line of each kind in each language is what the player can meet; plain is the kind without a refusal or a rule (a standing line stays under the standing budget, a refusal under the state's).
                string worstZh = "", worstEn = "", worstZhPlain = "", worstEnPlain = "";
                foreach ((HintInfo info, string zh, string en, bool plain) in Lines(kind))
                {
                    if (Len(zh) > Len(worstZh)) worstZh = zh;
                    if (Len(en) > Len(worstEn)) worstEn = en;
                    if (plain && Len(zh) > Len(worstZhPlain)) worstZhPlain = zh;
                    if (plain && Len(en) > Len(worstEnPlain)) worstEnPlain = en;
                }

                Over("hint:" + kind, true, worstZhPlain, budget.Zh);
                Over("hint:" + kind, false, worstEnPlain, budget.En);
                // With a refusal the line may use the state budget, whatever the kind.
                Over("hint:" + kind + "+state", true, worstZh, State.Zh + 18);
                Over("hint:" + kind + "+state", false, worstEn, State.En + 30);
            }

            Over("hint:host-gone", true, HintText.HostGone(true), State.Zh);
            Over("hint:host-gone", false, HintText.HostGone(false), State.En);

            Program.Check(s_Offenders == 0, "the lines of help are within their budgets: " + s_Offenders + " over");
        }

        private static void KeyRows()
        {
            Begin();
            foreach (string kind in HintText.Kinds)
            {
                for (int bits = 0; bits < 64; bits++)
                {
                    var info = new HintInfo { Kind = kind, Count = 3, Left = 1f, X = (bits & 1) != 0 ? 1f : 0f, Hung = (bits & 2) != 0, Turns = (bits & 4) != 0, Tilt = (bits & 8) != 0, Kerb = (bits & 16) != 0, Manual = (bits & 32) != 0 };
                    foreach (bool zh in new[] { true, false })
                    {
                        foreach (string[] row in HintText.Keys(info, zh))
                        {
                            Over("key:" + kind + ":" + row[0], zh, row[1], zh ? KeyRow.Zh : KeyRow.En);
                            Over("keyname:" + kind + ":" + row[0], zh, row[0], zh ? KeyName.Zh : KeyName.En);
                        }
                    }
                }
            }

            Program.Check(s_Offenders == 0, "the keys' rows are within their budget: " + s_Offenders + " over");
        }

        // ------------------------------------------------------------------ the language files

        private static (int Zh, int En) LangBudget(string key)
        {
            if (key.StartsWith("Assets.", StringComparison.Ordinal) || key.StartsWith("SubServices.", StringComparison.Ordinal))
                return Tip;
            if (key.Contains("SaveData"))
                return SaveData;
            if (key.EndsWith("Tip", StringComparison.Ordinal) || key.EndsWith("Tooltip", StringComparison.Ordinal) || key.EndsWith("Ask", StringComparison.Ordinal))
                return Tip;
            return Standing;
        }

        private static void LangFiles()
        {
            string root = Root();
            Program.Check(root != null, "the repository's lang folder is found from the runner");
            if (root == null)
                return;
            Begin();
            Dictionary<string, string> zh = Lang(root, "zh-HANS.json"), en = Lang(root, "en-US.json");
            foreach (string key in en.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                (int Zh, int En) budget = LangBudget(key);
                if (zh.TryGetValue(key, out string z))
                    Over("lang:" + key, true, z, budget.Zh);
                Over("lang:" + key, false, en[key], budget.En);
            }

            Program.Check(s_Offenders == 0, "the language files are within their budgets: " + s_Offenders + " over");
        }

        // ------------------------------------------------------------------ the editor's labels

        private static void FieldLabels()
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
                    string where = "field:" + template.Id + "." + field.Key;
                    Over(where, true, field.Zh, Label.Zh);
                    Over(where, false, field.En, Label.En);
                    if (field.Choices == null)
                        continue;
                    foreach (GuideChoice c in field.Choices)
                    {
                        string cw = "choice:" + template.Id + "." + field.Key + "." + c.Value;
                        Over(cw, true, c.Zh, Choice.Zh);
                        Over(cw, false, c.En, Choice.En);
                    }
                }
            }

            Program.Check(s_Offenders == 0, "the labels of the editor's fields and choices are within their budgets: " + s_Offenders + " over");
        }

        // ------------------------------------------------------------------ the panel's own model (source)

        private static readonly Regex s_Pair = new Regex("zh\\s*\\?\\s*\\$?\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\\$?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly Regex s_Say = new Regex("Say\\(\\s*\\$?\"((?:[^\"\\\\]|\\\\.)*)\"\\s*,\\s*\\$?\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);
        private static readonly Regex s_Hole = new Regex("\\{[^}]*\\}", RegexOptions.Compiled);

        /// <summary>The folders whose zh/en pairs the player reads: the panel's model and messages (they need the game, so they do not compile into the runner), the tool, and the notes of the markings and the text boards. Read as text.</summary>
        private static readonly string[] s_ModelFolders = { "Systems", "Tool", "Geo" };

        private static void ModelSource()
        {
            string root = Root();
            if (root == null)
                return;
            Begin();
            foreach (string path in s_ModelFolders.Select(f => Path.Combine(root, f)).Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)).OrderBy(p => p, StringComparer.Ordinal))
            {
                string file = path.Substring(root.Length + 1).Replace('\\', '/');
                string text = File.ReadAllText(path, Encoding.UTF8);
                foreach (Regex pattern in new[] { s_Pair, s_Say })
                {
                    foreach (Match m in pattern.Matches(text))
                    {
                        string zh = s_Hole.Replace(m.Groups[1].Value, "N"), en = s_Hole.Replace(m.Groups[2].Value, "N");
                        // The summaries of the editor's info line ("牌面 N × N m") are readings of a few words: the state budget is the line's.
                        string where = "source:" + file + ":" + Short(zh);
                        Over(where, true, zh, State.Zh);
                        Over(where, false, en, State.En);
                    }
                }
            }

            Program.Check(s_Offenders == 0, "the panel's own zh/en pairs are within their budgets: " + s_Offenders + " over");
        }

        // ------------------------------------------------------------------ the same numbers in both languages

        private static readonly Regex s_Number = new Regex("\\d+(?:\\.\\d+)?", RegexOptions.Compiled);
        private static readonly Regex s_Placeholder = new Regex("\\{\\d+\\}", RegexOptions.Compiled);

        private static string Numbers(string s) => string.Join(",", s_Number.Matches(s).Cast<Match>().Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal));

        private static void Parity()
        {
            string root = Root();
            if (root != null)
            {
                Dictionary<string, string> zh = Lang(root, "zh-HANS.json"), en = Lang(root, "en-US.json");
                foreach (string key in en.Keys)
                {
                    if (!zh.TryGetValue(key, out string z))
                    {
                        Program.Check(false, "lang key in en-US only: " + key);
                        continue;
                    }

                    string a = string.Join("", s_Placeholder.Matches(z).Cast<Match>().Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal)),
                        b = string.Join("", s_Placeholder.Matches(en[key]).Cast<Match>().Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal));
                    Program.Check(a == b, $"lang {key}: the placeholders are {a} in zh and {b} in en");
                }

                foreach (string key in zh.Keys.Where(k => !en.ContainsKey(k)))
                    Program.Check(false, "lang key in zh-HANS only: " + key);
            }

            // The lines of help: the numbers a state puts into a line are the same in both languages (a constant that only one language names is not a number of the state).
            int mismatches = 0;
            foreach (string kind in HintText.Kinds)
            {
                foreach ((HintInfo info, string zh, string en, bool plain) in Lines(kind))
                {
                    string a = Numbers(zh), b = Numbers(en);
                    if (a != b && mismatches++ < 40)
                        Program.Check(false, $"hint '{kind}': the numbers differ: zh [{a}] en [{b}]: {Short(zh)}");
                }
            }

            Program.Check(mismatches == 0, "the lines of help carry the same numbers in zh and en: " + mismatches + " differ");
        }
    }
}
