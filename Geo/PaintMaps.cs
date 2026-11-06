using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The maps of the road paint's baked surface (DetailKind.RoadPaint) with the grain and the smoothness the marking experiment asks for (0.12.83), as bytes four to a texel, made without the engine as
    /// DetailPixels makes the baked ones. Scale 1 and a mean smoothness of the baked one are the baked maps, byte for byte (the same arithmetic). Pure.
    /// </summary>
    public static class PaintMaps
    {
        /// <summary>
        /// The normal map with the slope of the grain times scale: a texel's normal (x, y, z) becomes (x k / z, y k / z, 1) made a unit again, which is the map of a relief k times as high. Scale 1 is the baked
        /// map itself (to the byte), 0 a flat one.
        /// </summary>
        public static byte[] Grain(DetailSurface s, float scale)
        {
            int n = s.Width * s.Height;
            var pixels = new byte[n * 4];
            for (int k = 0; k < n; k++)
            {
                float nz = Math.Max(s.Normals[k * 3 + 2], 1e-3f);
                float x = s.Normals[k * 3] / nz * scale, y = s.Normals[k * 3 + 1] / nz * scale;
                float length = (float)Math.Sqrt(x * x + y * y + 1f);
                int o = k * 4;
                pixels[o] = DetailPixels.ToByte(x / length * 0.5f + 0.5f);
                pixels[o + 1] = DetailPixels.ToByte(y / length * 0.5f + 0.5f);
                pixels[o + 2] = DetailPixels.ToByte(1f / length * 0.5f + 0.5f);
                pixels[o + 3] = 255;
            }

            return pixels;
        }

        /// <summary>The mean angle (degrees) the grain tips the paint's normal from straight up: how faint the grain is.</summary>
        public static float MeanTilt(DetailSurface s)
        {
            int n = s.Width * s.Height;
            double sum = 0.0;
            for (int k = 0; k < n; k++)
                sum += Math.Atan2(Math.Sqrt(s.Normals[k * 3] * s.Normals[k * 3] + s.Normals[k * 3 + 1] * s.Normals[k * 3 + 1]), s.Normals[k * 3 + 2]);
            return n == 0 ? 0f : (float)(sum / n * 180.0 / Math.PI);
        }

        /// <summary>The mean of the surface's smoothness (0 to 1).</summary>
        public static float MeanSmooth(DetailSurface s)
        {
            double sum = 0.0;
            foreach (float v in s.Smooth)
                sum += v;
            return s.Smooth.Length == 0 ? 0f : (float)(sum / s.Smooth.Length);
        }

        /// <summary>The mask map with every texel's smoothness moved by the same amount so that the mean is the one asked for (the variation across the paint stays; a byte holds 0 to 1); the other channels as baked.</summary>
        public static byte[] Smoothness(DetailSurface s, float mean)
        {
            DetailPixels.Maps(s, out _, out byte[] mask);
            float shift = mean - MeanSmooth(s);
            for (int k = 0; k < s.Smooth.Length; k++)
                mask[k * 4 + 3] = DetailPixels.ToByte(s.Smooth[k] + shift);
            return mask;
        }
    }
}
