using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The face a text is laid out in (0.12.76): the chosen font's, and, when the text asks (TextArt.Fallback) and the chosen font is not the default, the default font's for each character the chosen one has
    /// no glyph for, so that a text in Chinese in a Latin-only font is not full of blanks. The layout, the plan and the board's box all ask here, so that they lay the text out alike. Pure.
    /// </summary>
    public static class TextFaces
    {
        /// <summary>The fonts of a job: the chosen one (null when it cannot be read) and the one that fills in for it (null when none is wanted or there is none).</summary>
        public static void Fonts(TextPlanJob job, Func<int, FontOutlines> fonts, out FontOutlines primary, out FontOutlines fallback)
        {
            primary = fonts(job.FontId);
            fallback = null;
            if (primary != null && job.Art.Fallback && job.FontId != TextFonts.DefaultId)
            {
                fallback = fonts(TextFonts.DefaultId);
                if (fallback != null && (ReferenceEquals(fallback, primary) || !fallback.Ok))
                    fallback = null;
            }
        }

        /// <summary>
        /// The machine's symbol and emoji fonts that the chain ends with (0.12.78: the owner, "支持Emoji"): Segoe UI Emoji and Symbol where they are. Set once at the load, by the preferences (none: no
        /// emoji font, a cluster that the text's fonts lack is a missing character as it was); read from any thread.
        /// </summary>
        private static volatile FontOutlines[] s_Emoji = new FontOutlines[0];

        public static void UseEmojiFonts(IEnumerable<FontOutlines> fonts)
        {
            var list = new List<FontOutlines>();
            foreach (FontOutlines font in fonts ?? new FontOutlines[0])
            {
                if (font != null && font.Ok && !list.Contains(font))
                    list.Add(font);
            }

            s_Emoji = list.ToArray();
        }

        /// <summary>The emoji fonts in use, in the order they are tried.</summary>
        public static IReadOnlyList<FontOutlines> EmojiFonts => s_Emoji;

        public static TextFace Of(FontOutlines primary, FontOutlines fallback)
        {
            TextFace face = fallback == null ? TextFace.Of(primary) : Compose(TextFace.Of(primary), TextFace.Of(fallback));
            FontOutlines[] emoji = s_Emoji;
            for (int i = 0; i < emoji.Length; i++)
            {
                if (!ReferenceEquals(emoji[i], primary) && !ReferenceEquals(emoji[i], fallback))
                    face = Compose(face, TextFace.Of(emoji[i]));
            }

            return face;
        }

        /// <summary>The primary's face with the fallback's advance (scaled to the primary's em) for each character the primary has none for; none where neither has.</summary>
        public static TextFace Compose(TextFace primary, TextFace fallback)
        {
            float ratio = primary.UnitsPerEm / fallback.UnitsPerEm;
            return new TextFace(primary.UnitsPerEm, primary.EmBottom, primary.EmTop, c =>
            {
                float own = primary.Advance(c);
                if (own >= 0f)
                    return own;
                float other = fallback.Advance(c);
                return other >= 0f ? other * ratio : -1f;
            });
        }

        /// <summary>Whether the glyph of a character is the fallback's: the chosen font has none and the fallback has.</summary>
        public static bool FromFallback(FontOutlines primary, FontOutlines fallback, int codepoint) =>
            fallback != null && !primary.TryAdvance(codepoint, out _) && fallback.TryAdvance(codepoint, out _);

        /// <summary>
        /// How far (in em) a glyph of the fallback is moved up to sit in the chosen font's em box: the two fonts put the baseline at their own places in the box a Chinese character is designed on, and a
        /// glyph drawn for the other box would stand a little high or low among the chosen font's.
        /// </summary>
        public static float Lift(FontOutlines primary, FontOutlines fallback) =>
            primary.EmTop / primary.UnitsPerEm - fallback.EmTop / fallback.UnitsPerEm;
    }
}
