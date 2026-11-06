using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CDLC.TS.Geo
{
    /// <summary>What a template says about its English lines (Bilingual.AsBilingual): the spec fields that hold Chinese texts, and the layout it had before.</summary>
    public sealed class BilingualPlan
    {
        /// <summary>The keys of the spec fields whose Chinese text may have an English line under it (each gets a field "en_" + key).</summary>
        public string[] Keys;

        /// <summary>The English is set in capitals (the exit boards: GB 5768.2 4.5.1 e).</summary>
        public bool Capitals;

        /// <summary>Whether the form of the board the spec asks for has room for English at all (null: always): a form of a fixed size has not.</summary>
        public Func<GuideSpec, bool> Available;

        /// <summary>The template's own layout, which a board with the switch off is made by.</summary>
        public Func<GuideSpec, GuideBoard> Inner;
    }

    /// <summary>What the English lines of one board came to, for the editor's notes (GuideBoard.English).</summary>
    public sealed class BilingualReport
    {
        /// <summary>Lines drawn; texts with no English to draw; lines set smaller than the nominal height; lines wider than their Chinese (the board is made wider); lines left out as too long.</summary>
        public int Lines, Empty, Shrunk, Widened, Refused;

        /// <summary>The smallest height used, of the nominal.</summary>
        public float Least = 1f;

        /// <summary>The board's settings fill the whole of the field they are saved in (GuideSpec.MaxBytes): a text typed after this loses its end, so the editor says so (the junctions-ahead board holds the most).</summary>
        public bool SpecFull;
    }

    /// <summary>
    /// An English line under a Chinese text of a guide board (0.12.78; the owner: "支持中文下是否添加英文"). A template opts in with one call, AsBilingual(keys): it gets the switch "en" and, for each key, a field
    /// "en_" + key for the English; both are written into the spec only when they are not empty (GuideField.Sparse), so that every spec written before is the same string and every board with the switch
    /// off is the one the template laid out. With the switch on, the template's text builders (RoadGuide.Line, Auxiliary.Line, GuideKit.PlaceName) give a Chinese text that has English a block with the
    /// English line under it, and the template lays the block out as it lays out any block: the board grows with it (DESIGN-bilingual.md).
    /// </summary>
    public static partial class Bilingual
    {
        public const string FlagKey = "en";
        public const string TextPrefix = "en_";

        /// <summary>
        /// The capitals' height, of the Chinese h: Table 4 gives 1/3 h to 1/2 h for letters, 4.5.3 lets a height argued for go down to 80 % of the table's, which is not used here: a line is never set below
        /// 1/3 h. The line spacing is Table 6's 1/5 h (its range is 1/5 h to 1/3 h).
        /// </summary>
        public const float NominalK = 0.5f, LeastK = 1f / 3f, LineGapK = 0.2f;

        /// <summary>The most characters of an English line.</summary>
        public const int MaxLength = 32;

        /// <summary>A line wider than this much of its Chinese text, and of 6 h, is left out: it would make the board a different sign.</summary>
        private const float kMaxWiden = 2.5f, kMaxWidenH = 6f;

        private static readonly Regex kTag = new Regex(@"^(.*?)[\[［【]\s*[^\]］】]+?\s*[\]］】]$");

        // ------------------------------------------------------------------ opting in

        /// <summary>The template may carry an English line under the Chinese text of each of its keys, and under the nouns of Table A.1 wherever it writes them.</summary>
        public static GuideTemplate AsBilingual(this GuideTemplate template, params string[] keys) => Add(template, false, null, keys);

        /// <summary>As AsBilingual, with the English in capitals (the exit boards: "出口标志上的字母应大写").</summary>
        public static GuideTemplate AsBilingualCaps(this GuideTemplate template, params string[] keys) => Add(template, true, null, keys);

        /// <summary>
        /// The editor's label of an English field: the Chinese field's label up to its first note ("第一行：…", "车道1信息（可空）"), after "英文 · "; cut where the editor's labels end (16 characters in Chinese, 36 in English).
        /// </summary>
        private static string Label(string prefix, string source, int most)
        {
            int cut = source.IndexOfAny(new[] { '：', ':', '（', '(' });
            string name = (cut > 0 ? source.Substring(0, cut) : source).Trim();
            string label = prefix + name;
            return label.Length > most ? label.Substring(0, most) : label;
        }

        private static GuideTemplate Add(GuideTemplate template, bool capitals, Func<GuideSpec, bool> available, string[] keys)
        {
            if (template.Bilingual != null)
                throw new InvalidOperationException(template.Id + " is bilingual already");
            foreach (string key in keys)
            {
                if (template.FieldOf(key) == null)
                    throw new ArgumentException(template.Id + " has no field " + key);
            }

            // The fields go last: the editor's heading of their section then holds only them.
            var fields = new List<GuideField>(template.Fields)
            {
                GuideField.Flag(FlagKey, "英文行（汉字下方）", "English line under the Chinese", false, available).In("英文", "English").AsSparse()
            };
            foreach (string key in keys)
            {
                GuideField source = template.FieldOf(key);
                string chinese = key;
                Func<GuideSpec, bool> visible = source.Visible;
                fields.Add(GuideField.Text(TextPrefix + key, Label("英文 · ", source.Zh, 16), Label("English · ", source.En, 36), "", MaxLength,
                    s => s.Flag(FlagKey) && s.Get(chinese).Length > 0 && (visible == null || visible(s)) && (available == null || available(s))).AsSparse());
            }

            var plan = new BilingualPlan { Keys = keys, Capitals = capitals, Available = available, Inner = template.Layout };
            template.Fields = fields.ToArray();
            template.Bilingual = plan;
            template.Layout = spec => Lay(plan, spec);
            return template;
        }

        private static GuideBoard Lay(BilingualPlan plan, GuideSpec spec)
        {
            if (!spec.Flag(FlagKey) || (plan.Available != null && !plan.Available(spec)))
                return plan.Inner(spec);
            var scope = new Scope(plan, spec);
            Scope before = s_Current;
            s_Current = scope;
            try
            {
                GuideBoard board = plan.Inner(spec);
                board.English = scope.Report;
                return board;
            }
            finally
            {
                s_Current = before;
            }
        }

        // ------------------------------------------------------------------ the scope of one layout

        /// <summary>The English of the texts of the board being laid out on this thread (a layout is a pure function of its spec and may run on a worker).</summary>
        [ThreadStatic]
        private static Scope s_Current;

        private sealed class Scope
        {
            private readonly Dictionary<string, string> m_English = new Dictionary<string, string>();
            private readonly HashSet<string> m_Seen = new HashSet<string>();
            public readonly BilingualReport Report = new BilingualReport();
            public readonly bool Capitals;
            public bool Any;

            public Scope(BilingualPlan plan, GuideSpec spec)
            {
                Capitals = plan.Capitals;
                foreach (string key in plan.Keys)
                {
                    string chinese = spec.Get(key).Trim();
                    if (chinese.Length == 0)
                        continue;
                    string english = Normalise(spec.Get(TextPrefix + key));
                    if (english.Length == 0)
                        english = Normalise(TrafficTerms.English(chinese));
                    Any |= english.Length > 0;
                    Register(chinese, english);
                    // A place with a pictogram after it, 学府路[地铁], is built as the place and the pictogram: the text the builders see is the place's.
                    Match tag = kTag.Match(chinese);
                    if (tag.Success && tag.Groups[1].Value.Trim().Length > 0)
                        Register(tag.Groups[1].Value.Trim(), english);
                }
            }

            private string Normalise(string english)
            {
                english = (english ?? "").Trim();
                return Capitals ? english.ToUpperInvariant() : english;
            }

            private void Register(string chinese, string english)
            {
                if (!m_English.ContainsKey(chinese))
                    m_English[chinese] = english;
            }

            /// <summary>The English of a text the board writes: the one of its field, else the table's for a noun of Table A.1; "" for a text that has none; false for a text that is not one of these.</summary>
            public bool Lookup(string chinese, out string english)
            {
                chinese = (chinese ?? "").Trim();
                if (m_English.TryGetValue(chinese, out english))
                    return true;
                string noun = TrafficTerms.English(chinese);
                if (noun == null)
                    return false;
                english = Normalise(noun);
                Any = true;
                return true;
            }

            public bool First(string chinese) => m_Seen.Add(chinese);
        }

        // ------------------------------------------------------------------ the builders' hook

        /// <summary>
        /// For a builder that sets a Chinese text in a box of its own size (a plate, a name box): its text has no English line inside the box; the builder puts it under the box with Under, so that the box
        /// is as the standard draws it. Dispose to take the board's English up again.
        /// </summary>
        public static IDisposable Suspend() => new Suspension();

        /// <summary>A board of a bilingual template is being laid out with its English on: a layout that places blocks by the standard's distances may then make room for the taller ones.</summary>
        public static bool Active => s_Current != null;

        private sealed class Suspension : IDisposable
        {
            private readonly Scope m_Before = s_Current;

            public Suspension() => s_Current = null;

            public void Dispose() => s_Current = m_Before;
        }

        /// <summary>
        /// The block of a Chinese text with its English line under it, centred, when the board being laid out has English for the text; the block itself when it has not (and always when no board of
        /// a bilingual template is being laid out). The line is as high as capitals are (NominalK of h, down to LeastK when it would be wider than the Chinese), in the Chinese text's colour; a line
        /// still wider than the Chinese makes the block, and so the board, wider. A text with no English keeps the room of the line when another text of the board has one, so that the Chinese texts
        /// of a row stay level.
        /// </summary>
        public static GuideBlock Under(GuideBlock block, string text, float h, GuideColour colour)
        {
            Scope scope = s_Current;
            if (scope == null || block == null || block.W <= 0f || !scope.Lookup(text, out string english))
                return block;
            BilingualReport report = scope.Report;
            bool first = scope.First((text ?? "").Trim());
            float gap = LineGapK * h, cap = NominalK * h;
            if (english.Length == 0)
            {
                if (first)
                    report.Empty++;
                if (!scope.Any)
                    return block;
                var room = new GuideBlock(block.W, block.H + gap + cap);
                room.Add(block, 0f, 0f);
                return room;
            }

            float perCap = GuideKit.LatinWidth(english, 1f) * 1.04f;
            float width = perCap * cap;
            bool shrunk = false;
            if (width > block.W)
            {
                float fitted = Math.Max(LeastK * h, Math.Min(cap, block.W / perCap));
                shrunk = fitted < cap - 1e-6f;
                cap = fitted;
                width = perCap * cap;
            }

            if (width > Math.Max(block.W * kMaxWiden, kMaxWidenH * h))
            {
                if (first)
                    report.Refused++;
                return block;
            }

            if (first)
            {
                report.Lines++;
                if (shrunk)
                {
                    report.Shrunk++;
                    report.Least = Math.Min(report.Least, cap / (NominalK * h));
                }

                if (width > block.W + 1e-6f)
                    report.Widened++;
            }

            float w = Math.Max(block.W, width);
            var stacked = new GuideBlock(w, block.H + gap + cap);
            stacked.Add(block, (w - block.W) * 0.5f, 0f);
            stacked.Add(new GuideElement { Kind = GuideElementKind.Text, Mode = GuideTextMode.Line, Text = english, X = (w - width) * 0.5f, Y = block.H + gap, W = width, H = cap, Colour = colour, Align = GuideAlign.Centre });
            return stacked;
        }

        // ------------------------------------------------------------------ the editor's notes

        /// <summary>The short notes of the editor about the English lines of a board, in the language asked for (true: Chinese); none when all is as asked.</summary>
        public static IEnumerable<string> Notes(BilingualReport report, bool zh)
        {
            if (report == null)
                yield break;
            if (report.Empty > 0)
                yield return zh ? $"{report.Empty} 处未填英文，不显示" : $"{report.Empty} without English: not shown";
            if (report.Shrunk > 0)
                yield return zh ? $"{report.Shrunk} 行英文已缩小（最小 {report.Least * 100f:0}%）" : $"{report.Shrunk} English lines made smaller (down to {report.Least * 100f:0}%)";
            if (report.Widened > 0)
                yield return zh ? $"{report.Widened} 行英文比汉字宽，牌面已加宽" : $"{report.Widened} English lines wider than the Chinese: the board is wider";
            if (report.Refused > 0)
                yield return zh ? $"{report.Refused} 行英文太长，未显示" : $"{report.Refused} English lines too long: not shown";
            if (report.SpecFull)
                yield return zh ? "设置已满：再加字会被截去末尾" : "Settings full: more text is cut off";
        }
    }
}
