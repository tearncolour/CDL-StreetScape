using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The yellow and black bands of a height-limit frame's tubes (0.12.78; the owner's photo of a tunnel entrance's frame: bands of 0.2 to 0.25 m along every tube): a tube is cut into equal bands of about
    /// Pitch, an odd number of them, so that its two ends are yellow whatever its length. Pure.
    /// </summary>
    public static class LimitBands
    {
        /// <summary>The length of a band (m): this mod's estimate from the photo.</summary>
        public const float Pitch = 0.25f;

        /// <summary>How many bands a tube of this length has: the odd number nearest to length / Pitch, at least one.</summary>
        public static int CountFor(float length)
        {
            float ideal = length / Pitch;
            int below = (int)Math.Floor(ideal);
            if (below % 2 == 0)
                below--;
            below = Math.Max(1, below);
            int above = below + 2;
            return ideal - below <= above - ideal ? below : above;
        }

        /// <summary>The bands of a tube of this length, from its start: yellow first, then black, alternating; the last is yellow.</summary>
        public static List<(float From, float To, SolidColour Colour)> Of(float length)
        {
            int count = CountFor(length);
            var bands = new List<(float From, float To, SolidColour Colour)>(count);
            for (int i = 0; i < count; i++)
                bands.Add((length * i / count, length * (i + 1) / count, i % 2 == 0 ? SolidColour.Yellow : SolidColour.Black));
            return bands;
        }
    }
}
