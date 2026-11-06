using System;
using System.Collections.Generic;
using System.Text;

namespace CDLC.TS.Geo
{
    public enum GuideFieldKind
    {
        /// <summary>Free text (a place name, a number); at most MaxLength characters.</summary>
        Text,

        /// <summary>One of a fixed list.</summary>
        Choice,

        /// <summary>On or off ("1" or "0").</summary>
        Flag
    }

    public sealed class GuideChoice
    {
        public readonly string Value, Zh, En;

        public GuideChoice(string value, string zh, string en)
        {
            Value = value;
            Zh = zh;
            En = en;
        }
    }

    /// <summary>One thing about a guide board that the player can change after placing it. The editor of the panel is built from these, so a new template needs no new UI.</summary>
    public sealed class GuideField
    {
        public string Key, Zh, En;
        public GuideFieldKind Kind;
        public string Default = "";

        /// <summary>Text: the most characters (a Chinese character counts as one).</summary>
        public int MaxLength = 8;

        public GuideChoice[] Choices;

        /// <summary>Choice: values that are not in the list but are accepted all the same (a font file the system has but the list has not read yet: the list is made on a worker). Null: only the list.</summary>
        public Func<string, bool> Accepts;

        /// <summary>Text: only digits and the decimal point are kept (a limit, a height, a mass): whatever else is typed is dropped.</summary>
        public bool DigitsOnly;

        /// <summary>A digits field that may start with a minus (a signed angle: the tilt's negative side is the depression angle); the minus is kept only as the first character.</summary>
        public bool Negative;

        /// <summary>Whether the field matters for the spec as it is now (a road number has no meaning for a road that has none). Null: always.</summary>
        public Func<GuideSpec, bool> Visible;

        /// <summary>0.12.47: the heading of the editor's section this field starts (null: it goes on in the one before), in Chinese and English.</summary>
        public string SectionZh, SectionEn;

        /// <summary>A setting of how the sign is placed, not of the sign (the line settings of SignLine): the editor of a sign that is already placed does not show it.</summary>
        public bool DraftOnly;

        /// <summary>A text that wants the whole width of the editor under its label, whatever its label is (a line of text of 36 characters).</summary>
        public bool Wide;

        /// <summary>
        /// Written into the spec only when it is not the default (a field a later build adds that an older save never had: the spec of every sign that does not use it stays as it was written).
        /// Reading is as for any field: a missing key is the default.
        /// </summary>
        public bool Sparse;

        /// <summary>Text: the characters beyond the BMP are kept (an emoji, a rare Han character) and MaxLength counts clusters (TextClusters), not UTF-16 units: the text of a lit board. The others drop a surrogate, as before: what draws them cannot.</summary>
        public bool Clusters;

        public GuideField Unicode()
        {
            Clusters = true;
            return this;
        }

        public GuideField Full()
        {
            Wide = true;
            return this;
        }

        public GuideField AsSparse()
        {
            Sparse = true;
            return this;
        }

        public GuideField Signed()
        {
            Negative = true;
            return this;
        }

        /// <summary>A number the editor sets with a slider as well as by typing (0.12.76): from SliderMin to SliderMax in steps of SliderStep; the field stays a Digits text, so everything that reads it (the spec, the batch interface) is as before.</summary>
        public double SliderMin, SliderMax, SliderStep;

        public bool HasSlider => SliderStep > 0.0 && SliderMax > SliderMin;

        public GuideField Slider(double min, double max, double step)
        {
            SliderMin = min;
            SliderMax = max;
            SliderStep = step;
            return this;
        }

        /// <summary>The field starts a section of the editor with this heading.</summary>
        public GuideField In(string zh, string en)
        {
            SectionZh = zh;
            SectionEn = en;
            return this;
        }

        public static GuideField Text(string key, string zh, string en, string def, int max, Func<GuideSpec, bool> visible = null) =>
            new GuideField { Key = key, Zh = zh, En = en, Kind = GuideFieldKind.Text, Default = def, MaxLength = max, Visible = visible };

        /// <summary>A number typed as text: digits and one decimal point, at most max characters.</summary>
        public static GuideField Digits(string key, string zh, string en, string def, int max, Func<GuideSpec, bool> visible = null) =>
            new GuideField { Key = key, Zh = zh, En = en, Kind = GuideFieldKind.Text, Default = def, MaxLength = max, Visible = visible, DigitsOnly = true };

        public static GuideField Choice(string key, string zh, string en, string def, GuideChoice[] choices, Func<GuideSpec, bool> visible = null) =>
            new GuideField { Key = key, Zh = zh, En = en, Kind = GuideFieldKind.Choice, Default = def, Choices = choices, Visible = visible };

        public static GuideField Flag(string key, string zh, string en, bool def, Func<GuideSpec, bool> visible = null) =>
            new GuideField { Key = key, Zh = zh, En = en, Kind = GuideFieldKind.Flag, Default = def ? "1" : "0", Visible = visible };

        /// <summary>The value this field accepts for a given input: a choice must be one of the list, a flag 0 or 1, a text is cleaned and cut.</summary>
        public string Sanitize(string value)
        {
            value = value ?? "";
            switch (Kind)
            {
                case GuideFieldKind.Choice:
                    foreach (GuideChoice choice in Choices)
                    {
                        if (choice.Value == value)
                            return value;
                    }

                    return Accepts != null && Accepts(value) ? value : Default;
                case GuideFieldKind.Flag:
                    return value == "1" ? "1" : value == "0" ? "0" : Default;
                default:
                    var clean = new StringBuilder();
                    for (int i = 0; i < value.Length; i++)
                    {
                        char c = value[i];
                        // No control characters and no lone surrogate halves (a pair is kept by a field of clusters); the separators are escaped when the spec is written.
                        if (c < 0x20 || c == 0x7F)
                            continue;
                        if (char.IsSurrogate(c))
                        {
                            if (Clusters && char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                            {
                                clean.Append(c).Append(value[i + 1]);
                                i++;
                            }

                            continue;
                        }

                        if (DigitsOnly && !((c >= '0' && c <= '9') || (c == '.' && clean.ToString().IndexOf('.') < 0) || (c == '-' && Negative && clean.Length == 0)))
                            continue;
                        clean.Append(c);
                    }

                    string text = clean.ToString().Trim();
                    if (Clusters)
                        return TextClusters.Truncate(text, MaxLength);
                    if (text.Length > MaxLength)
                        text = text.Substring(0, MaxLength);
                    return text;
            }
        }
    }

    /// <summary>
    /// The settings of one guide board: a value for every field of its template. Written as "key=value;key=value" in the template's field order, with \\, \; and \= escaped,
    /// so it is one short string that is saved with the item (TsItem), compared, and put into a cache key. A spec always has every field (a field a later version adds takes
    /// its default), and a value the template does not accept is replaced by the field's default: whatever is read from a save or typed into the panel is made valid here.
    /// </summary>
    public sealed class GuideSpec
    {
        /// <summary>The most bytes (UTF-8) the written spec may have: the size of the field it is saved in is 509.</summary>
        public const int MaxBytes = 480;

        private readonly GuideTemplate m_Template;
        private readonly Dictionary<string, string> m_Values;
        private string m_Text;

        private GuideSpec(GuideTemplate template, Dictionary<string, string> values)
        {
            m_Template = template;
            m_Values = values;
        }

        public GuideTemplate Template => m_Template;

        public string Get(string key) => m_Values.TryGetValue(key, out string value) ? value : "";

        public bool Is(string key, string value) => Get(key) == value;

        public bool Flag(string key) => Get(key) == "1";

        /// <summary>The value as a number; the fallback when it is not one.</summary>
        public double Number(string key, double fallback) =>
            double.TryParse(Get(key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;

        public GuideSpec With(string key, string value)
        {
            var values = new Dictionary<string, string>(m_Values);
            GuideField field = m_Template.FieldOf(key);
            if (field != null)
                values[key] = field.Sanitize(value);
            return Build(m_Template, values);
        }

        public static GuideSpec Defaults(GuideTemplate template)
        {
            var values = new Dictionary<string, string>();
            foreach (GuideField field in template.Fields)
                values[field.Key] = field.Default;
            return Build(template, values);
        }

        /// <summary>Reads a written spec for a template. Unknown keys are dropped, missing and invalid values take the default.</summary>
        public static GuideSpec Parse(GuideTemplate template, string text)
        {
            var read = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(text))
            {
                foreach (string pair in SplitUnescaped(text, ';'))
                {
                    List<string> kv = SplitUnescaped(pair, '=', 2);
                    if (kv.Count == 2)
                        read[Unescape(kv[0])] = Unescape(kv[1]);
                }
            }

            var values = new Dictionary<string, string>();
            foreach (GuideField field in template.Fields)
                values[field.Key] = read.TryGetValue(field.Key, out string value) ? field.Sanitize(value) : field.Default;
            return Build(template, values);
        }

        private static GuideSpec Build(GuideTemplate template, Dictionary<string, string> values)
        {
            var spec = new GuideSpec(template, values);
            bool trimmed = false;
            // A spec that would not fit its field (it cannot, with the limits of the fields in the catalogue, but a template may be added with wide limits) loses the end of its longest text.
            while (Encoding.UTF8.GetByteCount(spec.ToString()) > MaxBytes)
            {
                string longestKey = null;
                int longest = 0;
                bool clusters = false;
                foreach (GuideField field in template.Fields)
                {
                    if (field.Kind == GuideFieldKind.Text && values[field.Key].Length > longest)
                    {
                        longest = values[field.Key].Length;
                        longestKey = field.Key;
                        clusters = field.Clusters;
                    }
                }

                if (longestKey == null)
                    break;
                // A text of clusters loses its last cluster, never half of a pair or of a sequence.
                values[longestKey] = clusters ? TextClusters.WithoutLast(values[longestKey]) : values[longestKey].Substring(0, longest - 1);
                spec.m_Text = null;
                trimmed = true;
            }

            spec.Trimmed = trimmed;
            return spec;
        }

        public override string ToString()
        {
            if (m_Text != null)
                return m_Text;
            var sb = new StringBuilder();
            foreach (GuideField field in m_Template.Fields)
            {
                if (field.Sparse && m_Values[field.Key] == field.Default)
                    continue;
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(Escape(field.Key)).Append('=').Append(Escape(m_Values[field.Key]));
            }

            return m_Text = sb.ToString();
        }

        /// <summary>Whether Build had to cut the end off a text because the written settings would not fit the saved field (MaxBytes): what the player typed is not all there.</summary>
        public bool Trimmed { get; private set; }

        /// <summary>Whether the written settings fill the saved field to within a character or two: a text typed now is cut.</summary>
        public bool Full => Trimmed || Encoding.UTF8.GetByteCount(ToString()) > MaxBytes - 3;

        public uint Hash => SignCatalog.HashOf(ToString());

        // ------------------------------------------------------------------ escaping

        private static string Escape(string text)
        {
            if (text.IndexOfAny(new[] { '\\', ';', '=' }) < 0)
                return text;
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (c == '\\' || c == ';' || c == '=')
                    sb.Append('\\');
                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0)
                return text;
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                    i++;
                sb.Append(text[i]);
            }

            return sb.ToString();
        }

        /// <summary>Splits at a separator that is not escaped; with a limit, the last piece keeps the rest.</summary>
        private static List<string> SplitUnescaped(string text, char separator, int limit = int.MaxValue)
        {
            var pieces = new List<string>();
            var current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    current.Append(c).Append(text[++i]);
                    continue;
                }

                if (c == separator && pieces.Count < limit - 1)
                {
                    pieces.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            pieces.Add(current.ToString());
            return pieces;
        }
    }

    /// <summary>A kind of guide board: its fields, and the function that lays the board out from a spec.</summary>
    public sealed class GuideTemplate
    {
        public string Id;

        /// <summary>The sign's number in GB 5768.2 (路42), or the words of the part of the standard it follows.</summary>
        public string Code;

        public string Zh, En;
        public GuideField[] Fields;
        public Func<GuideSpec, GuideBoard> Layout;

        /// <summary>The template can set an English line under its Chinese texts (Bilingual.cs, AsBilingual); null: it cannot.</summary>
        public BilingualPlan Bilingual;

        /// <summary>The kind of sign for the panel's filter: "prohibition", "warning", "mandatory", "guide" (default) or "auxiliary".</summary>
        public string Group = "guide";

        /// <summary>For the search: words, and the pinyin initials of the Chinese name.</summary>
        public string[] Keywords = { "指路", "guide" };

        public string Pinyin = "";

        /// <summary>True when the template is listed in the panel as it is (with its defaults), not only through presets: the standard's fixed signs.</summary>
        public bool Standalone;

        /// <summary>The sign may be placed along a line (SignLine; StandardSigns.AsLinear adds the two settings): a template opts in, the tool needs nothing of its own.</summary>
        public bool Linear;

        /// <summary>The height of the board's lower edge (m) when it is not the roadside clearance (0.12.57: the metro board's 2.5 m, the pedestrian strip's 1.9 m); 0 takes the roadside's.</summary>
        public float Clearance;

        /// <summary>
        /// A template whose picture follows the game while it is on the map (the traffic-state screen): the renderer gives each placed item of it an "id" and a "rev" in its settings, the
        /// revision of what the item shows now, so that a change of what it shows is a change of the item's settings to everything that compares them. Not saved.
        /// </summary>
        public bool Live;

        /// <summary>
        /// Road markings (GB 5768.3): a template with this is a marking, not a board. It makes the shapes of the marking from the settings (MarkingTemplates); the layout above is only a stand-in that
        /// keeps the catalogue's code that walks every template working, and nothing draws it.
        /// </summary>
        public Func<GuideSpec, MarkingPlan> Marking;

        public bool IsMarking => Marking != null;

        /// <summary>A line for the editor of a marking about what its settings come to (a length worked out from a formula, a spacing from a table), in the language asked for (true: Chinese); null when there is none.</summary>
        public Func<GuideSpec, bool, string> MarkingNote;

        /// <summary>The Chinese names the template was known by in earlier builds (saved items carry the hash of the id): the id of an older sign that this template replaces.</summary>
        public string[] LegacyIds = new string[0];

        public uint Hash => SignCatalog.HashOf(Id);

        public GuideField FieldOf(string key)
        {
            foreach (GuideField field in Fields)
            {
                if (field.Key == key)
                    return field;
            }

            return null;
        }

        public GuideSpec Default() => GuideSpec.Defaults(this);

        public GuideSpec Parse(string text) => GuideSpec.Parse(this, text);
    }
}
