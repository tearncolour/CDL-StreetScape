using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The owner's "精简文本" (2 Nov, twice): no standing explanation, no remark of the keys in a label, nothing about a numpad (he has none), short labels. What is shown is checked here as it is made:
    /// the language files, the key card and the hint line of every kind of the tool, and the label of every field of every template.
    /// </summary>
    internal static class TrimTests
    {
        /// <summary>Longest label of a field (CJK characters, Latin characters): a unit may stay in it, an explanation may not. A longer one is on the allow-list with the reason.</summary>
        private const int kLabelCjk = 16, kLabelLatin = 36;

        /// <summary>The longest line of the key card: what a key does in a few words, Shift as a short suffix.</summary>
        private const int kCardCjk = 22, kCardLatin = 50;

        private static readonly HashSet<string> s_LabelAllowList = new HashSet<string>(StringComparer.Ordinal);

        public static void RunAll()
        {
            Program.Run("trim: nothing displayed speaks of a numpad or of keys inside a label", NoKeyTalk);
            Program.Run("trim: a label of a field is short", ShortLabels);
            Program.Run("trim: the lines of the key card are short", ShortCard);
            Program.Run("trim: the notes of the support tab that were removed stay removed", RemovedKeys);
        }

        private static string FindUp(string relative)
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string path = Path.Combine(dir, relative);
                if (File.Exists(path))
                    return path;
                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        private static bool TalksKeys(string text) => text != null && (text.IndexOf("小键盘", StringComparison.Ordinal) >= 0 || text.IndexOf("numpad", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Every state of the tool the hint line and the card are made for: each kind, with the flags that change their text.</summary>
        private static IEnumerable<HintInfo> AllHints()
        {
            foreach (string kind in HintText.Kinds)
            {
                foreach (bool hung in new[] { false, true })
                {
                    foreach (float x in new[] { 0f, 1.2f })
                    {
                        yield return new HintInfo { Kind = kind, Hung = hung, X = x, Count = 2, Left = 2.3f, Need = 2.3f, Span = 14f, Max = 4, Name = "附着式", Host = "lamp" };
                        yield return new HintInfo { Kind = kind, Hung = hung, X = x, Count = 1, Turns = true, Tilt = true, Kerb = true, TwoSided = true, Back = true, Bad = true, Manual = true, Grew = true };
                    }
                }
            }
        }

        private static void NoKeyTalk()
        {
            int values = 0;
            foreach (string file in new[] { "lang/zh-HANS.json", "lang/en-US.json" })
            {
                string path = FindUp(file);
                Program.Check(path != null, file + " is found");
                if (path == null)
                    continue;
                foreach (string line in File.ReadAllLines(path))
                {
                    values++;
                    Program.Check(!TalksKeys(line), file + " does not speak of a numpad: " + line.Trim());
                }
            }

            Program.Check(values > 300, "the language files were read: " + values);

            int hints = 0;
            foreach (HintInfo info in AllHints())
            {
                foreach (bool zh in new[] { true, false })
                {
                    hints++;
                    string text = HintText.Of(info, zh);
                    Program.Check(!TalksKeys(text), info.Kind + ": the hint line has no numpad in it");
                    foreach (string[] key in HintText.Keys(info, zh))
                        Program.Check(!TalksKeys(key[0]) && !TalksKeys(key[1]), info.Kind + ": the key card has no numpad in it: " + key[0] + " " + key[1]);
                }
            }

            Program.Check(hints > 200, "the hints were checked: " + hints);

            int labels = 0;
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Template?.Fields == null)
                    continue;
                foreach (GuideField field in def.Template.Fields)
                {
                    labels++;
                    Program.Check(!TalksKeys(field.Zh) && !TalksKeys(field.En) && !(field.Zh ?? "").Contains("或按") && !(field.En ?? "").Contains("(or the "), def.Id + "/" + field.Key + ": the label does not tell which key to press: " + field.Zh);
                }
            }

            Program.Check(labels > 500, "the labels were checked: " + labels);
        }

        private static void ShortLabels()
        {
            var seen = new HashSet<GuideField>();
            int checkedFields = 0;
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Template?.Fields == null)
                    continue;
                foreach (GuideField field in def.Template.Fields)
                {
                    if (!seen.Add(field))
                        continue;
                    checkedFields++;
                    string id = def.Id + "/" + field.Key;
                    bool allowed = s_LabelAllowList.Contains(id);
                    int cjk = (field.Zh ?? "").Length;
                    Program.Check(allowed || cjk <= kLabelCjk, id + ": the Chinese label is " + cjk + " characters (at most " + kLabelCjk + "): " + field.Zh);
                    Program.Check(allowed || (field.En ?? "").Length <= kLabelLatin, id + ": the English label is " + (field.En ?? "").Length + " characters (at most " + kLabelLatin + "): " + field.En);
                }
            }

            Program.Check(checkedFields > 500, "the fields were checked: " + checkedFields);
        }

        private static void ShortCard()
        {
            int lines = 0;
            foreach (HintInfo info in AllHints())
            {
                foreach (bool zh in new[] { true, false })
                {
                    foreach (string[] key in HintText.Keys(info, zh))
                    {
                        lines++;
                        int limit = zh ? kCardCjk : kCardLatin;
                        Program.Check(key[1].Length <= limit, info.Kind + (zh ? " zh" : " en") + ": a line of the card is " + key[1].Length + " characters (at most " + limit + "): " + key[1]);
                    }
                }
            }

            Program.Check(lines > 500, "the card lines were checked: " + lines);
        }

        /// <summary>The paragraphs of the support tab that were deleted (a note when nothing is chosen, the lane count, the plain style's line) are not made again by a key in the language files.</summary>
        private static void RemovedKeys()
        {
            foreach (string file in new[] { "lang/zh-HANS.json", "lang/en-US.json" })
            {
                string path = FindUp(file);
                if (path == null)
                    continue;
                string all = File.ReadAllText(path);
                foreach (string key in new[] { "CDLC.TS.UI.NoChosen", "CDLC.TS.UI.LaneCount", "CDLC.TS.UI.ProfilePlain" })
                    Program.Check(!all.Contains("\"" + key + "\""), file + " has no " + key);
            }

            string script = FindUp(Path.Combine("UI", "src", "ts-ui.js"));
            Program.Check(script != null, "the panel script is found");
            if (script == null)
                return;
            string mjs = File.ReadAllText(script);
            foreach (string key in new[] { "UI.NoChosen", "UI.LaneCount", "UI.ProfilePlain" })
                Program.Check(!mjs.Contains("\"" + key + "\""), "the panel script does not use " + key);
        }
    }
}
