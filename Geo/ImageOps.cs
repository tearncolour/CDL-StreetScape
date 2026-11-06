using System;

namespace CDLC.TS.Geo
{
    /// <summary>Small operations on straight-alpha RGBA pictures (bottom row first), for the thumbnails. Pure.</summary>
    public static class ImageOps
    {
        public static byte[] Canvas(int width, int height) => new byte[width * height * 4];

        private static readonly float[] s_Linear = MakeLinearTable();

        private static float[] MakeLinearTable()
        {
            var table = new float[256];
            for (int i = 0; i < 256; i++)
            {
                double c = i / 255.0;
                table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
            }

            return table;
        }

        /// <summary>
        /// The average colour of a picture in linear light (what a shader adds up), over the pixels that are solid (alpha of 128 or more), taking about 160 samples along each side. White when
        /// nothing is solid. The night glow of a face is this colour, so that a green board glows green and a white one white instead of every board being the same flat white.
        /// </summary>
        public static void AverageLinear(byte[] rgba, int width, int height, out float r, out float g, out float b)
        {
            r = g = b = 1f;
            if (rgba == null || width <= 0 || height <= 0 || rgba.Length < width * height * 4)
                return;

            int step = Math.Max(1, Math.Min(width, height) / 160);
            double sr = 0, sg = 0, sb = 0;
            long n = 0;
            for (int y = step / 2; y < height; y += step)
            {
                for (int x = step / 2; x < width; x += step)
                {
                    int i = (y * width + x) * 4;
                    if (rgba[i + 3] < 128)
                        continue;
                    sr += s_Linear[rgba[i]];
                    sg += s_Linear[rgba[i + 1]];
                    sb += s_Linear[rgba[i + 2]];
                    n++;
                }
            }

            if (n == 0)
                return;
            r = (float)(sr / n);
            g = (float)(sg / n);
            b = (float)(sb / n);
        }

        /// <summary>Copies a picture into another with its lower left corner at (x, y); what falls outside is cut off.</summary>
        public static void Blit(byte[] target, int targetWidth, int targetHeight, byte[] source, int sourceWidth, int sourceHeight, int x, int y)
        {
            for (int row = 0; row < sourceHeight; row++)
            {
                int ty = y + row;
                if (ty < 0 || ty >= targetHeight)
                    continue;
                for (int column = 0; column < sourceWidth; column++)
                {
                    int tx = x + column;
                    if (tx < 0 || tx >= targetWidth)
                        continue;
                    Buffer.BlockCopy(source, (row * sourceWidth + column) * 4, target, (ty * targetWidth + tx) * 4, 4);
                }
            }
        }

        /// <summary>
        /// Paints a line of the given thickness (in pixels) round the shape of the picture: every pixel that is more than half opaque and has a pixel that is not
        /// within the thickness gets the colour. The picture's edge counts as outside.
        /// </summary>
        public static void Outline(byte[] rgba, int width, int height, int thickness, byte r, byte g, byte b)
        {
            var solid = new bool[width * height];
            for (int i = 0; i < solid.Length; i++)
                solid[i] = rgba[i * 4 + 3] >= 128;

            var edge = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!solid[y * width + x])
                        continue;
                    for (int dy = -thickness; dy <= thickness && !edge[y * width + x]; dy++)
                    {
                        for (int dx = -thickness; dx <= thickness; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            bool outside = nx < 0 || ny < 0 || nx >= width || ny >= height || !solid[ny * width + nx];
                            if (outside)
                            {
                                edge[y * width + x] = true;
                                break;
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < edge.Length; i++)
            {
                if (!edge[i])
                    continue;
                rgba[i * 4] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = 255;
            }
        }

        /// <summary>The picture shrunk by an integer factor, each pixel the average of a block weighted by alpha (so a transparent neighbour does not darken the colour).</summary>
        public static byte[] Downsample(byte[] rgba, int width, int height, int factor, out int outWidth, out int outHeight)
        {
            outWidth = width / factor;
            outHeight = height / factor;
            var result = new byte[outWidth * outHeight * 4];
            int block = factor * factor;
            for (int y = 0; y < outHeight; y++)
            {
                for (int x = 0; x < outWidth; x++)
                {
                    float alphaSum = 0f, rSum = 0f, gSum = 0f, bSum = 0f;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        for (int dx = 0; dx < factor; dx++)
                        {
                            int i = ((y * factor + dy) * width + x * factor + dx) * 4;
                            float a = rgba[i + 3] / 255f;
                            alphaSum += a;
                            rSum += rgba[i] * a;
                            gSum += rgba[i + 1] * a;
                            bSum += rgba[i + 2] * a;
                        }
                    }

                    int o = (y * outWidth + x) * 4;
                    if (alphaSum > 0f)
                    {
                        result[o] = (byte)Math.Min(255f, rSum / alphaSum + 0.5f);
                        result[o + 1] = (byte)Math.Min(255f, gSum / alphaSum + 0.5f);
                        result[o + 2] = (byte)Math.Min(255f, bSum / alphaSum + 0.5f);
                    }

                    result[o + 3] = (byte)Math.Min(255f, alphaSum / block * 255f + 0.5f);
                }
            }

            return result;
        }
    }
}
