using System;

namespace CDLC.TS.Geo
{
    /// <summary>How a letter's face is made: whole as the font draws it, only a band along its outline (hollow), or a neon tube along its outline.</summary>
    public enum TextEffect : byte
    {
        None = 0,
        Outline = 1,
        Neon = 2
    }

    /// <summary>
    /// How a text light box's letters are dressed beyond the font (0.12.76, the owner: "支持不同字体切换，支持部分艺术字"): immutable and part of a plan's key, so that a plan made for one dressing is never taken
    /// for another. A plain dressing with no fallback is what 0.12.75 made, and has the key 0: a plan of a plain text is the same plan as before. The effects are this mod's design, not a standard's.
    /// </summary>
    public sealed class TextArt : IEquatable<TextArt>
    {
        public static readonly TextArt Plain = new TextArt(false, 0f, 0f, TextEffect.None, 0f, 0f);

        /// <summary>A character the chosen font lacks is taken from the default font (so that no letter is a hole) instead of being left blank.</summary>
        public readonly bool Fallback;

        /// <summary>How much every stroke is made bolder (positive) or lighter (negative) on each side, in em; 0 for the font's own weight.</summary>
        public readonly float Weight;

        /// <summary>How far the top of a letter leans to the right of its bottom, per unit of height (the tangent of the angle: 0.2126 is 12 degrees); 0 for upright.</summary>
        public readonly float Slant;

        /// <summary>How the face is made, and the width of its band (outline) or the radius of its tube (neon), in em (at least what a stud can hold: TextStyles.MinBand).</summary>
        public readonly TextEffect Effect;

        public readonly float EffectWidth;

        /// <summary>The text is set on an arc of this many degrees (ArcTextLayout): positive with the letters outside the circle (an arch), negative inside (a smile); 0 for a straight line. The glyphs are the same, only their places differ.</summary>
        public readonly float Sweep;

        /// <summary>0 for the plain dressing; else a 64-bit key of every field.</summary>
        public readonly ulong Key;

        private TextArt(bool fallback, float weight, float slant, TextEffect effect, float effectWidth, float sweep)
        {
            Fallback = fallback;
            Weight = weight;
            Slant = slant;
            Effect = effect;
            EffectWidth = effect == TextEffect.None ? 0f : effectWidth;
            Sweep = sweep;
            if (!fallback && weight == 0f && slant == 0f && effect == TextEffect.None && sweep == 0f)
            {
                Key = 0UL;
                return;
            }

            var key = new Fingerprint();
            key.Add(fallback ? 1 : 0);
            key.Add(weight);
            key.Add(slant);
            key.Add((int)effect);
            key.Add(EffectWidth);
            key.Add(sweep);
            Key = key.Value | 1UL;
        }

        /// <summary>The numbers are rounded to ten thousandths, so that the same dressing is the same key whichever way it was worked out.</summary>
        public static TextArt Create(bool fallback = false, float weight = 0f, float slant = 0f, TextEffect effect = TextEffect.None, float effectWidth = 0f, float sweep = 0f)
        {
            weight = Quantised(weight);
            slant = Quantised(slant);
            effectWidth = Math.Max(0f, Quantised(effectWidth));
            sweep = Quantised(sweep);
            // What the layout can bend: the same arc whatever was asked beyond it, so that it is one key.
            if (sweep != 0f)
                sweep = Math.Sign(sweep) * Math.Max(ArcInfo.MinSweep, Math.Min(ArcInfo.MaxSweep, Math.Abs(sweep)));
            return !fallback && weight == 0f && slant == 0f && effect == TextEffect.None && sweep == 0f ? Plain : new TextArt(fallback, weight, slant, effect, effectWidth, sweep);
        }

        /// <summary>The same dressing set on an arc of this many degrees (see Sweep; 0 straightens it).</summary>
        public TextArt WithSweep(float sweep) => Create(Fallback, Weight, Slant, Effect, EffectWidth, sweep);

        private static float Quantised(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : (float)Math.Round(v * 10000.0) / 10000f;

        public bool IsPlain => Key == 0UL;

        /// <summary>Whether the glyph meshes are changed from the font's (bolder, lighter, leaning, hollow, neon): the glyphs are then kept under a key of their own.</summary>
        public bool ChangesGlyphs => Weight != 0f || Slant != 0f || Effect != TextEffect.None;

        /// <summary>The number that tells a glyph made for this dressing from the font's own in the glyph cache: 0 when the dressing leaves the glyph alone.</summary>
        public int GlyphVariant
        {
            get
            {
                if (!ChangesGlyphs)
                    return 0;
                unchecked
                {
                    int h = 17;
                    h = h * 31 + (int)Math.Round(Weight * 10000f);
                    h = h * 31 + (int)Math.Round(Slant * 10000f);
                    h = h * 31 + (int)Effect;
                    h = h * 31 + (int)Math.Round(EffectWidth * 10000f);
                    return h == 0 ? 1 : h;
                }
            }
        }

        public bool Equals(TextArt other) => other != null && Key == other.Key && Fallback == other.Fallback && Weight == other.Weight && Slant == other.Slant && Effect == other.Effect && EffectWidth == other.EffectWidth && Sweep == other.Sweep;

        public override bool Equals(object obj) => Equals(obj as TextArt);

        public override int GetHashCode() => unchecked((int)Key ^ (int)(Key >> 32));
    }
}
