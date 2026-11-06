using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The styles a text light box's letters can be dressed in (0.12.76: the owner, "支持部分艺术字"): one choice of the template, mapped here to the dressing the planner makes (TextArt). The effects are this
    /// mod's design: a stroke made bolder or lighter by an offset of the glyph's outline, a lean by a shear about the middle of the em box, and the return of the letters lit in a second colour.
    /// </summary>
    public static class TextStyles
    {
        public const string Plain = "plain", Light = "light", Bold = "bold", Italic = "italic", BoldItalic = "bolditalic", TwoTone = "twotone", Outline = "outline", Neon = "neon";

        /// <summary>How much a stroke grows (or shrinks) on each side in em (a Chinese stroke of the traffic sign font is 0.08 to 0.1 em wide), and the lean (the tangent of 12 degrees).</summary>
        public const float BoldWeight = 0.022f, LightWeight = -0.012f, ItalicSlant = 0.2126f;

        /// <summary>The band of a hollow letter and the radius of a neon tube, in em (a stroke of the traffic sign font is 0.08 to 0.1 em wide): at least what a stud can hold, MinBand.</summary>
        public const float OutlineWidth = 0.03f, NeonRadius = 0.02f;

        /// <summary>The curves a text can be set on (the sweep is its own field): a straight line, an arch (the letters on the outside of a circle, the ends dropping), a smile (inside it, the ends rising).</summary>
        public const string Straight = "straight", Arch = "arch", Smile = "smile";

        public static GuideChoice[] CurveChoices() => new[]
        {
            new GuideChoice(Straight, "直线", "Straight"), new GuideChoice(Arch, "上拱", "Arch"), new GuideChoice(Smile, "下弯", "Smile")
        };

        /// <summary>The signed sweep (degrees) of a curve choice and the sweep typed: positive for an arch, negative for a smile, 0 for a straight line or an unknown name.</summary>
        public static float SignedSweep(string curve, float degrees) => curve == Arch ? Math.Abs(degrees) : curve == Smile ? -Math.Abs(degrees) : 0f;

        public static GuideChoice[] Choices() => new[]
        {
            new GuideChoice(Plain, "标准", "Plain"), new GuideChoice(Bold, "粗体", "Bold"), new GuideChoice(Light, "细体", "Light"), new GuideChoice(Italic, "斜体", "Italic"),
            new GuideChoice(BoldItalic, "粗斜体", "Bold italic"), new GuideChoice(Outline, "空心", "Hollow"), new GuideChoice(Neon, "霓虹", "Neon"), new GuideChoice(TwoTone, "双色", "Two-tone")
        };

        /// <summary>The dressing of a style (an unknown name is plain).</summary>
        public static TextArt Art(string style, bool fallback)
        {
            switch (style)
            {
                case Bold: return TextArt.Create(fallback, BoldWeight);
                case Light: return TextArt.Create(fallback, LightWeight);
                case Italic: return TextArt.Create(fallback, 0f, ItalicSlant);
                case BoldItalic: return TextArt.Create(fallback, BoldWeight, ItalicSlant);
                case Outline: return TextArt.Create(fallback, 0f, 0f, TextEffect.Outline, OutlineWidth);
                case Neon: return TextArt.Create(fallback, 0f, 0f, TextEffect.Neon, NeonRadius);
                default: return TextArt.Create(fallback);
            }
        }

        /// <summary>The dressing of a style set on an arc (a signed sweep in degrees: see TextArt.Sweep).</summary>
        public static TextArt Art(string style, bool fallback, float sweep) => Art(style, fallback).WithSweep(sweep);

        /// <summary>The return of the letters is lit too, in a colour of its own.</summary>
        public static bool IsTwoTone(string style) => style == TwoTone;

        /// <summary>The face is a tube: the letters have no return and no depth of their own.</summary>
        public static bool IsNeon(string style) => style == Neon;

        /// <summary>
        /// The frame's sizes for letters of a height in a dressing: a hollow letter or a neon tube is held by thinner studs (clips: half the radius of a solid letter's), so that a band or a tube of a
        /// few millimetres can hold one.
        /// </summary>
        public static FrameStyle FrameFor(float letterHeight, TextArt art)
        {
            FrameStyle style = FrameStyle.For(letterHeight);
            if (art != null && art.Effect != TextEffect.None)
                style.StudRadius *= 0.5f;
            return style;
        }

        /// <summary>The least width (em) of a band or of a tube that a stud of the frame can hold: four of its radii (the least span the frame planner puts a stud in), and a tenth more.</summary>
        public static float MinBand(float letterHeight, TextArt art) => 4.4f * FrameFor(letterHeight, art).StudRadius / Math.Max(0.01f, letterHeight);

        /// <summary>The radius of a neon tube in em: the style's, or what a stud can hold if that is more.</summary>
        public static float TubeRadius(float letterHeight, TextArt art) => art == null || art.Effect != TextEffect.Neon ? 0f : Math.Max(art.EffectWidth, MinBand(letterHeight, art) * 0.5f);
    }
}
