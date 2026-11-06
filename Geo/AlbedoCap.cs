using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A ceiling for how bright a near-white surface of this mod is in daylight (0.12.92; the owner, 5 Nov: "白色材质白天会过曝", with a noise wall of white louvred panels). The same colour number is
    /// not the same light on this mod's three routes: HDRP/Lit and the plain material on the game's shader take it as a Color property, which Unity turns from gamma to linear light (a white of 0.86 is
    /// 0.72 as light), while the baked details bake the colour's gamma form into their colour map, so their light is the number itself (0.86, and 0.80 for the light grey of a louvre panel: 1.2 and
    /// 1.3 times the plain route's). A surface of 0.86 in the sun is nearly the white of the screen. Pure.
    /// </summary>
    public static class AlbedoCap
    {
        /// <summary>The ceiling a near-neutral colour is held to when nothing says otherwise: the light of the white the plain route has given since the first version (0.86 as a Color property).</summary>
        public const float Default = 0.72f;

        /// <summary>The smallest and the largest ceiling the experiment file may name.</summary>
        public const float Lowest = 0.2f, Highest = 1f;

        /// <summary>How far the dimmest channel may be below the brightest for a colour to count as neutral (near white or grey): a yellow or an orange is a colour, not a white, and is left alone.</summary>
        public const float Neutral = 0.85f;

        /// <summary>One channel of a Color property as light: Unity's gamma to linear conversion (the sRGB curve).</summary>
        public static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);

        /// <summary>
        /// The factor for the colour of a surface whose light (linear) is r, g, b: 1 for a colour that is not neutral or is not brighter than the ceiling, else the ceiling over its brightest
        /// channel, so that the brightest channel comes to the ceiling and the colour keeps its tint. A ceiling that is not a number above zero holds nothing.
        /// </summary>
        public static float Factor(float r, float g, float b, float ceiling)
        {
            if (!(ceiling > 0f) || float.IsNaN(r) || float.IsNaN(g) || float.IsNaN(b))
                return 1f;
            float peak = Math.Max(r, Math.Max(g, b)), low = Math.Min(r, Math.Min(g, b));
            if (!(peak > ceiling) || low < Neutral * peak)
                return 1f;
            return ceiling / peak;
        }
    }
}
