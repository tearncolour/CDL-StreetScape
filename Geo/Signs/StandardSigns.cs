using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// How a fixed sign of the standard becomes a template of the panel: its names and its layout. Every one has the setting "speed" (the band of Table 7 and 8; automatic by default, so a sign
    /// is made for the road it stands at), and may have more (the number on a speed limit). The layout is a function of the band (0 to 3) and the spec; it returns the laid-out board.
    /// </summary>
    public static class StandardSigns
    {
        public const string Prohibition = "prohibition";
        public const string Mandatory = "mandatory";
        public const string Warning = "warning";
        public const string Auxiliary = "auxiliary";

        /// <summary>The place-name signs of GB 17733-2008 (PlaceNameSigns).</summary>
        public const string PlaceName = "placename";

        /// <summary>A fixed sign with no setting but its size.</summary>
        public static GuideTemplate Make(string id, string code, string zh, string en, string group, string pinyin, string[] keywords, Func<int, GuideBoard> layout, params string[] legacyIds) =>
            Make(id, code, zh, en, group, pinyin, keywords, (spec, band) => layout(band), null, legacyIds);

        /// <summary>A sign with settings of its own (extra fields) besides its size.</summary>
        public static GuideTemplate Make(string id, string code, string zh, string en, string group, string pinyin, string[] keywords, Func<GuideSpec, int, GuideBoard> layout, GuideField[] extra, params string[] legacyIds)
        {
            var fields = new List<GuideField>();
            if (extra != null)
                fields.AddRange(extra);
            fields.Add(GuideTemplates.SpeedField());
            return new GuideTemplate
            {
                Id = id,
                Code = code,
                Zh = zh,
                En = en,
                Group = group,
                Pinyin = pinyin ?? "",
                Keywords = keywords ?? new string[0],
                Standalone = true,
                Fields = fields.ToArray(),
                Layout = spec => layout(spec, SignStyles.BandOf(spec)),
                LegacyIds = legacyIds ?? new string[0]
            };
        }

        /// <summary>
        /// The sign may be placed along a line (SignLine): it takes the settings "line" (off by default) and "gap" (the spacing, empty is automatic) before its speed setting, shown only where allowed says
        /// the form of the sign makes a line (null: always).
        /// </summary>
        public static GuideTemplate AsLinear(this GuideTemplate template, Func<GuideSpec, bool> allowed = null)
        {
            var fields = new List<GuideField>(template.Fields);
            int speed = fields.FindIndex(field => field.Key == "speed");
            int at = speed < 0 ? fields.Count : speed;
            GuideField line = GuideField.Flag(SignLine.LineKey, "线性摆放", "Place along a line", false, allowed);
            GuideField gap = GuideField.Digits(SignLine.GapKey, "间距（m，空为自动）", "Spacing (m, empty: automatic)", "", 5, spec => (allowed == null || allowed(spec)) && spec.Flag(SignLine.LineKey));
            line.DraftOnly = true;
            gap.DraftOnly = true;
            fields.Insert(at, line);
            fields.Insert(at + 1, gap);
            template.Fields = fields.ToArray();
            template.Linear = true;
            return template;
        }

        /// <summary>A list of keywords from the words of a name (the search looks at them as well as at the name).</summary>
        public static string[] Words(params string[] words) => words;
    }
}
