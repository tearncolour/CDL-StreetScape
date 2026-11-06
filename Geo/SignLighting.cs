using System.Collections.Generic;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Geo
{
    /// <summary>How a board is lit at night (GB 5768.2-2022 4.2.4: 照明标志, by where the light source is).</summary>
    public enum LightingKind
    {
        /// <summary>Retroreflective sheeting only, which 4.8.14 makes the rule: the board is built as it was before.</summary>
        None,

        /// <summary>4.8.16 b: the source is inside the board's structure; the board is a light box.</summary>
        Internal,

        /// <summary>4.8.16 c: the source is outside the board, below and in front of it or at another suitable place, lighting its face.</summary>
        External
    }

    /// <summary>
    /// The optional lighting of a board (0.12.80; the owner: "标志照明"). A board has the setting "lt", written into the spec only when it is not "n" (GuideField.Sparse), so a spec written before is
    /// the same string and gives the same board. Which boards can have it and the numbers the standard gives are here; the geometry is SignBuilder.Lighting.
    /// </summary>
    public static class SignLighting
    {
        public const string Key = "lt";
        public const string ValueNone = "n", ValueInternal = "i", ValueExternal = "e";

        /// <summary>The reading distance at night the standard asks of both kinds (4.8.16 b 2 and c 2): 150 m or more.</summary>
        public const float ReadingDistance = 150f;

        /// <summary>External lighting (4.8.16 c 2): the greatest illuminance on the face over the least is below this.</summary>
        public const float MaxIlluminanceRatio = 4f;

        /// <summary>
        /// The groups of the catalogue that are boards of the road. Left out: the markings (not boards), the electric boxes, the LED and variable-message screens and the lamp-post ads, the text and standing
        /// light boxes (they have a light of their own or none), the BRT, toll and wayfinding series (this mod's, not the standard's boards).
        /// </summary>
        private static readonly HashSet<string> s_Groups = new HashSet<string>
        {
            StandardSigns.Prohibition, StandardSigns.Mandatory, StandardSigns.Warning, StandardSigns.Auxiliary, "guide", "tourist", "notice"
        };

        /// <summary>Whether a template's boards may be lit: a static board of the standard's groups (4.2.3 splits boards into static and variable-message ones; 4.8.17 is the lit LED kind).</summary>
        public static bool Applies(GuideTemplate template) => template != null && template.Group != null && s_Groups.Contains(template.Group);

        public static LightingKind Of(GuideSpec spec)
        {
            switch (spec.Get(Key))
            {
                case ValueInternal: return LightingKind.Internal;
                case ValueExternal: return LightingKind.External;
                default: return LightingKind.None;
            }
        }

        /// <summary>Gives every template the standard's boards may have the "lt" choice (once, as the catalogue is made, after the English line so that the editor's headings keep their order).</summary>
        public static void OptIn(IEnumerable<GuideTemplate> templates)
        {
            foreach (GuideTemplate template in templates)
            {
                if (!Applies(template) || template.FieldOf(Key) != null)
                    continue;
                var fields = new List<GuideField>(template.Fields)
                {
                    GuideField.Choice(Key, "照明", "Lighting", ValueNone, new[]
                    {
                        new GuideChoice(ValueNone, "无", "None"),
                        new GuideChoice(ValueInternal, "内部灯箱", "Light box"),
                        new GuideChoice(ValueExternal, "外部灯", "Lamps")
                    }).In("照明", "Lighting").AsSparse()
                };
                template.Fields = fields.ToArray();
            }
        }
    }
}
