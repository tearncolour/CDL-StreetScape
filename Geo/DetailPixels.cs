using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The texels of a baked detail surface's maps as bytes, four to a texel (R, G, B, A), made without the engine so that they can be made on a worker (0.12.76): the main thread was making them in the
    /// frame that made the material (a loop of 260,000 texels with seven roundings each for a 512 square surface, 0.12.75's 40.7 ms for the first tree grate). What the textures hold is what the
    /// loops that were in TsMaterials held: the same roundings (to even, as Mathf.RoundToInt) and clamps.
    /// </summary>
    public static class DetailPixels
    {
        public static byte ToByte(float v)
        {
            int i = (int)Math.Round((double)(v * 255f));
            return (byte)(i < 0 ? 0 : i > 255 ? 255 : i);
        }

        /// <summary>
        /// The normal map and the mask map of a surface: x, y, z of each normal in R, G, B (alpha 1: read right where the shader takes the normal from R and G and where it takes it from A times R and G),
        /// and metallic, coat, 0 and smoothness of the game's mask map.
        /// </summary>
        public static void Maps(DetailSurface s, out byte[] normal, out byte[] mask)
        {
            int n = s.Width * s.Height;
            normal = new byte[n * 4];
            mask = new byte[n * 4];
            for (int k = 0; k < n; k++)
            {
                int o = k * 4;
                normal[o] = ToByte(s.Normals[k * 3] * 0.5f + 0.5f);
                normal[o + 1] = ToByte(s.Normals[k * 3 + 1] * 0.5f + 0.5f);
                normal[o + 2] = ToByte(s.Normals[k * 3 + 2] * 0.5f + 0.5f);
                normal[o + 3] = 255;
                mask[o] = ToByte(s.Metal[k]);
                mask[o + 1] = ToByte(s.Coat[k]);
                mask[o + 2] = 0;
                mask[o + 3] = ToByte(s.Smooth[k]);
            }
        }

        /// <summary>
        /// The colour map of the foliage surface in a solid's colour: the leaf green, and where a blossom is (Mix) the blossom's colour, the solid's own or for the mixed one the palette's by the blossom's hue; a solid that is no
        /// blossom colour has leaves only. Times the surface's shade; opaque.
        /// </summary>
        public static byte[] FoliageColour(DetailSurface s, SolidColour colour)
        {
            int n = s.Width * s.Height;
            var pixels = new byte[n * 4];
            bool bloom = WallFoliage.Is(colour), mixed = colour == SolidColour.BlossomMixed;
            (float sr, float sg, float sb) = WallFoliage.Blossom(colour);
            for (int k = 0; k < n; k++)
            {
                float mix = bloom ? s.Mix[k] : 0f, shade = s.Shade[k];
                (float fr, float fg, float fb) = mixed && mix > 0f ? WallFoliage.Palette(s.Hue[k]) : (sr, sg, sb);
                int o = k * 4;
                pixels[o] = ToByte((WallFoliage.LeafRed + (fr - WallFoliage.LeafRed) * mix) * shade);
                pixels[o + 1] = ToByte((WallFoliage.LeafGreen + (fg - WallFoliage.LeafGreen) * mix) * shade);
                pixels[o + 2] = ToByte((WallFoliage.LeafBlue + (fb - WallFoliage.LeafBlue) * mix) * shade);
                pixels[o + 3] = 255;
            }

            return pixels;
        }

        /// <summary>The colour map of a surface in a colour (its gamma-space red, green and blue, 0 to 1): the colour times the surface's shade; holes in its alpha, else opaque.</summary>
        public static byte[] Colour(DetailSurface s, float red, float green, float blue)
        {
            int n = s.Width * s.Height;
            var pixels = new byte[n * 4];
            for (int k = 0; k < n; k++)
            {
                float shade = s.Shade[k];
                int o = k * 4;
                pixels[o] = ToByte(red * shade);
                pixels[o + 1] = ToByte(green * shade);
                pixels[o + 2] = ToByte(blue * shade);
                pixels[o + 3] = s.Alpha != null ? ToByte(s.Alpha[k]) : (byte)255;
            }

            return pixels;
        }
    }
}
