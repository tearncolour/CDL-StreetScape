using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The colours of the mod's foliage surface (DetailKind.Foliage, the face of a vertical greening wall): the leaves' own green and the blossoms' colour, which is the solid's (SolidColour.Blossom*: the colour
    /// map is the leaf green with blossoms of that colour on it; BlossomMixed has red, pink, yellow and white ones by a hash of each blossom). Gamma-space 0 to 1, estimates of the usual flowering climbers and
    /// bedding plants (a rose, a pelargonium, a daisy), not a standard's. Pure.
    /// </summary>
    public static class WallFoliage
    {
        public const float LeafRed = 0.17f, LeafGreen = 0.40f, LeafBlue = 0.15f;

        /// <summary>The share of the face that the blossoms cover, measured on the surface by the tests (0.296 of a tile).</summary>
        public const float Coverage = 0.30f;

        /// <summary>
        /// How much of a picture's face is the blossoms' colour (the catalogue's thumbnail, the editor's preview): more than Coverage, on purpose, because at the true share the five colours are all a dull olive at 128 px and the
        /// wall does not read as flowers. A picture is a flat colour per body, not the game's surface.
        /// </summary>
        public const float PictureWeight = 0.60f;

        public static bool Is(SolidColour colour) => colour >= SolidColour.BlossomRed && colour <= SolidColour.BlossomMixed;

        /// <summary>A blossom's colour for a solid: its own; for the mixed one the mean of the palette by its shares (the plain solid of the far level's band, the pictures).</summary>
        public static (float R, float G, float B) Blossom(SolidColour colour)
        {
            switch (colour)
            {
                case SolidColour.BlossomRed: return (0.78f, 0.13f, 0.18f);
                case SolidColour.BlossomYellow: return (0.96f, 0.80f, 0.15f);
                case SolidColour.BlossomWhite: return (0.95f, 0.94f, 0.90f);
                case SolidColour.BlossomMixed: return (0.91f, 0.54f, 0.49f);
                default: return (0.94f, 0.38f, 0.62f);
            }
        }

        /// <summary>The mixed blossoms' colour for a blossom's hash (0 to 1): red a quarter, pink 30 %, yellow a fifth, white a quarter.</summary>
        public static (float R, float G, float B) Palette(float hue) =>
            hue < 0.25f ? Blossom(SolidColour.BlossomRed) : hue < 0.55f ? Blossom(SolidColour.BlossomPink) : hue < 0.75f ? Blossom(SolidColour.BlossomYellow) : Blossom(SolidColour.BlossomWhite);

        /// <summary>The colour of a solid's face in the pictures: the leaves with the blossoms over PictureWeight of it.</summary>
        public static byte[] PictureRgb(SolidColour colour)
        {
            (float r, float g, float b) = Blossom(colour);
            byte Mix(float leaf, float blossom) => DetailPixels.ToByte(leaf * (1f - PictureWeight) + blossom * PictureWeight);
            return new[] { Mix(LeafRed, r), Mix(LeafGreen, g), Mix(LeafBlue, b) };
        }

        /// <summary>The solid of a wall's flower colour choice ("r", "p", "y", "w", "m"; "l" or anything else is the leaves only); None for the plain green wall.</summary>
        public static SolidColour? OfChoice(string choice)
        {
            switch (choice)
            {
                case "r": return SolidColour.BlossomRed;
                case "p": return SolidColour.BlossomPink;
                case "y": return SolidColour.BlossomYellow;
                case "w": return SolidColour.BlossomWhite;
                case "m": return SolidColour.BlossomMixed;
                default: return null;
            }
        }
    }
}
