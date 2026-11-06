using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The foliage surface of a vertical greening wall's face (DetailKind.Foliage, 0.12.78). No prefab of the game is thin enough to cover a face 0.3 to 0.6 m thick (the thinnest greenery of the dump is a flower
    /// pot 0.52 m across and a hedge piece 1.13 m deep), so the face between the troughs is this: two layers of overlapping leaves (cells of 5 and 7 cm, each a lump with its own height) and blossoms (cells of 10 cm,
    /// one in a cell where a hash is under a density that follows a clumping noise; five-lobed domes 7.6 to 10 cm across with a dimple in the middle, the highest of the overlapping ones on top), baked into the normal map, the leaves' and blossoms' shade and
    /// the colour map's blossom share (DetailSurface.Mix). The tile is 1 m up the face (u) by 2 m along it (v), 256 by 512 texels, and repeats without a seam (the noise and the cells wrap). All the numbers are
    /// this mod's estimates of a flowering climber or a bed of bedding plants; the seed is fixed, so the pixels are the same in every game. Pure.
    /// </summary>
    public static partial class DetailSurfaces
    {
        public const float FoliageUp = 1f, FoliageAlong = 2f;

        private const int kFoliageWidth = 256, kFoliageHeight = 512;

        private struct FoliageTexel
        {
            public float Height, Shade, Smooth, Mix, Hue;
        }

        /// <summary>
        /// The foliage at any point (u up the face, v along it, metres; the tile repeats, so a point and the same point a tile on are the same): its height (m), how much of it is a blossom (0 to 1) and the blossom's hue (0 to 1).
        /// What the baked texel at that point holds; the tests of the seam compare a point with its copy a tile away.
        /// </summary>
        public static (float Height, float Mix, float Hue) FoliageSample(float u, float v)
        {
            FoliageTexel texel = FoliageAt(new Tiling(FoliageUp, FoliageAlong), u, v, FoliageUp / kFoliageWidth);
            return (texel.Height, texel.Mix, texel.Hue);
        }

        private static DetailSurface FoliageSurface(DetailKind kind, Tiling t)
        {
            float du = t.SizeU / kFoliageWidth, dv = t.SizeV / kFoliageHeight;
            var texels = new FoliageTexel[kFoliageWidth * kFoliageHeight];
            for (int j = 0; j < kFoliageHeight; j++)
            {
                for (int i = 0; i < kFoliageWidth; i++)
                    texels[j * kFoliageWidth + i] = FoliageAt(t, (i + 0.5f) * du, (j + 0.5f) * dv, du);
            }

            int At(float u, float v) => (int)(v / dv) * kFoliageWidth + (int)(u / du);
            DetailSurface s = Bake(kind, kFoliageWidth, kFoliageHeight, t, (u, v) => texels[At(u, v)].Height, (u, v) => (0f, 0f, texels[At(u, v)].Smooth), 0.85f, (u, v) => texels[At(u, v)].Shade);
            s.Mix = new float[texels.Length];
            s.Hue = new float[texels.Length];
            for (int k = 0; k < texels.Length; k++)
            {
                s.Mix[k] = texels[k].Mix;
                s.Hue[k] = texels[k].Hue;
            }

            return s;
        }

        private static float FoliageHash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / (float)uint.MaxValue;
            }
        }

        private static int FoliageMod(int a, int n) => (a % n + n) % n;

        /// <summary>The cells of the blossoms (m): a blossom centre is jittered a quarter of a cell either way of the middle, so two are at least 5 cm apart and a blossom is never cut by a neighbour's cell.</summary>
        private const float kBlossomCell = 0.10f;

        /// <summary>One texel of the foliage at (u, v) metres of the tile, texel metres wide (the blossoms' edge is soft by half of one).</summary>
        private static FoliageTexel FoliageAt(Tiling t, float u, float v, float texel)
        {
            (float d1, float value1, _, _, _) = t.CellFacet(u, v, 0.05f, 201);
            float r1 = d1 / 0.034f, leaf = 0.006f * value1 + (r1 < 1f ? 0.004f * (float)Math.Sqrt(1f - r1 * r1) : 0f);
            (float d2, float value2, _, _, _) = t.CellFacet(u, v, 0.07f, 211);
            float r2 = d2 / 0.05f, back = 0.0045f * value2 + (r2 < 1f ? 0.003f * (float)Math.Sqrt(1f - r2 * r2) : 0f) - 0.002f;
            float height = Math.Max(leaf, back);
            float leafShade = Math.Max(0.55f, Math.Min(1f, 0.64f + 0.36f * height / 0.010f + 0.06f * t.Fbm(u, v, 12f, 2, 205)));
            var texelOut = new FoliageTexel { Height = height, Shade = leafShade, Smooth = 0.32f };

            // The blossoms: of the nine cells round the texel, those that have one (a hash under a density that clumps), the highest dome that covers the texel.
            int nu = Math.Max(1, (int)Math.Round(t.SizeU / kBlossomCell)), nv = Math.Max(1, (int)Math.Round(t.SizeV / kBlossomCell));
            float su = t.SizeU / nu, sv = t.SizeV / nv;
            int cx = (int)Math.Floor(u / su), cy = (int)Math.Floor(v / sv);
            float bestDome = float.MinValue, bestMix = 0f, bestHue = 0f, bestX = 0f;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy, wx = FoliageMod(x, nu), wy = FoliageMod(y, nv);
                    float px = (x + 0.5f + (FoliageHash(wx, wy, 222) - 0.5f) * 0.5f) * su, py = (y + 0.5f + (FoliageHash(wx, wy, 223) - 0.5f) * 0.5f) * sv;
                    float clump = 0.5f + 0.5f * t.Fbm(px, py, 2.5f, 3, 203);
                    if (!(FoliageHash(wx, wy, 221) < 0.45f + 0.55f * clump))
                        continue;
                    float other = FoliageHash(wx, wy, 224), du = u - px, dv = v - py, d = (float)Math.Sqrt(du * du + dv * dv);
                    float radius = (0.038f + 0.012f * other) * (0.84f + 0.16f * (float)Math.Cos(5f * (float)Math.Atan2(dv, du) + other * 6.2831853f));
                    if (d >= radius + texel)
                        continue;
                    float x1 = d / radius, dome = 0.004f + 0.007f * Math.Max(0f, 1f - x1 * x1) - (d < 0.005f ? 0.002f : 0f);
                    if (dome > bestDome)
                    {
                        bestDome = dome;
                        bestMix = Math.Max(0f, Math.Min(1f, (radius - d) / texel + 0.5f));
                        bestHue = Math.Min(0.999f, FoliageHash(wx, wy, 225));
                        bestX = x1;
                    }
                }
            }

            if (bestMix <= 0f)
                return texelOut;
            texelOut.Height = bestMix > 0.5f ? Math.Max(height, bestDome) : height;
            texelOut.Mix = bestMix;
            texelOut.Hue = bestHue;
            texelOut.Shade = leafShade + (0.94f + 0.06f * (1f - Math.Min(1f, bestX)) - leafShade) * bestMix;
            texelOut.Smooth = 0.32f - 0.07f * bestMix;
            return texelOut;
        }
    }
}
