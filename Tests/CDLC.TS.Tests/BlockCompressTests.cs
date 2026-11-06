using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The faces as BC1 with their mips (0.12.27, BlockCompress; the owner: "继续优化加载性能和资源占用"): sizes, exact single colours, the order of the rows, the mips in linear light, quality.</summary>
    internal static class BlockCompressTests
    {
        public static void RunAll()
        {
            Program.Run("bc1: the sizes of the levels and the chain", Sizes);
            Program.Run("bc1: one colour is kept as it is", OneColour);
            Program.Run("bc1: the rows keep their order", Order);
            Program.Run("bc1: the mips are averaged in linear light", Mips);
            Program.Run("bc1: edges, text and three colours", Quality);
            Program.Run("bc1: every board of the catalogue is whole blocks", Catalogue);
        }

        private static byte[] Picture(int w, int h, Func<int, int, (byte r, byte g, byte b)> colour)
        {
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    (byte r, byte g, byte b) = colour(x, y);
                    int p = (y * w + x) * 4;
                    rgba[p] = r;
                    rgba[p + 1] = g;
                    rgba[p + 2] = b;
                    rgba[p + 3] = 255;
                }
            }

            return rgba;
        }

        private static int MaxError(byte[] a, byte[] b, int pixels)
        {
            int max = 0;
            for (int i = 0; i < pixels; i++)
            {
                for (int c = 0; c < 3; c++)
                    max = Math.Max(max, Math.Abs(a[i * 4 + c] - b[i * 4 + c]));
            }

            return max;
        }

        private static double Psnr(byte[] a, byte[] b, int pixels)
        {
            double se = 0;
            for (int i = 0; i < pixels; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    double d = a[i * 4 + c] - b[i * 4 + c];
                    se += d * d;
                }
            }

            double mse = se / (pixels * 3.0);
            return mse <= 0 ? 99.0 : 10.0 * Math.Log10(255.0 * 255.0 / mse);
        }

        private static void Sizes()
        {
            Program.Check(BlockCompress.LevelCount(768, 384) == 10 && BlockCompress.LevelCount(676, 768) == 10 && BlockCompress.LevelCount(4, 4) == 3 && BlockCompress.LevelCount(1, 1) == 1, "levels down to 1 x 1, as Unity makes them");
            Program.Check(BlockCompress.Bc1LevelSize(768, 384) == 192 * 96 * 8 && BlockCompress.Bc1LevelSize(2, 1) == 8 && BlockCompress.Bc1LevelSize(6, 6) == 4 * 8, "8 bytes a block, a part of a block a whole one");
            int chain = BlockCompress.Bc1ChainSize(768, 384);
            Program.Check(chain > BlockCompress.Bc1LevelSize(768, 384) && chain < BlockCompress.Bc1LevelSize(768, 384) * 4 / 3 + 200, "the chain is a third more than the first level: " + chain);
            Program.Check(chain * 8 < 768 * 384 * 4 * 4 / 3 + 1600, "an eighth of RGBA32 with its mips: " + chain + " for " + 768 * 384 * 4 * 4 / 3);
            Program.Check(BlockCompress.Bc1LevelOffset(768, 384, 0) == 0 && BlockCompress.Bc1LevelOffset(768, 384, 1) == BlockCompress.Bc1LevelSize(768, 384) && BlockCompress.Bc1LevelOffset(768, 384, 10) == chain, "where each level starts");
            Program.Check(BlockCompress.CanEncode(768, 384) && BlockCompress.CanEncode(4, 4) && !BlockCompress.CanEncode(766, 384) && !BlockCompress.CanEncode(2, 2), "whole blocks only");
            Program.Check(BlockCompress.Bc1Chain(Picture(8, 4, (x, y) => (10, 20, 30)), 8, 4).Length == BlockCompress.Bc1ChainSize(8, 4), "the chain is as long as said");
            bool threw = false;
            try { BlockCompress.Bc1Chain(new byte[6 * 4 * 4], 6, 4); } catch (ArgumentException) { threw = true; }
            Program.Check(threw, "a picture that is not whole blocks is refused");
        }

        private static void OneColour()
        {
            // Every value of each channel alone, and greys: the match tables give the colour within the rounding of a GPU's thirds.
            int worst = 0;
            for (int v = 0; v < 256; v++)
            {
                foreach ((byte r, byte g, byte b) c in new[] { ((byte)v, (byte)0, (byte)0), ((byte)0, (byte)v, (byte)0), ((byte)0, (byte)0, (byte)v), ((byte)v, (byte)v, (byte)v) })
                {
                    byte[] block = Picture(4, 4, (x, y) => c);
                    byte[] back = BlockCompress.DecodeBc1(BlockCompress.Bc1Chain(block, 4, 4), 0, 4, 4);
                    worst = Math.Max(worst, MaxError(block, back, 16));
                }
            }

            Program.Check(worst <= 1, "a block of one colour comes back within 1: " + worst);
            // The colours of the standard's boards (GB 5768.2 Appendix A as this mod paints them) come back exactly or within 1.
            foreach ((byte r, byte g, byte b) c in new[] { ((byte)0, (byte)122, (byte)61), ((byte)0, (byte)74, (byte)153), ((byte)214, (byte)28, (byte)36), ((byte)255, (byte)204, (byte)0), ((byte)250, (byte)250, (byte)250), ((byte)18, (byte)18, (byte)18) })
            {
                byte[] block = Picture(8, 8, (x, y) => c);
                Program.Check(MaxError(block, BlockCompress.DecodeBc1(BlockCompress.Bc1Chain(block, 8, 8), 0, 8, 8), 64) <= 1, $"the ground ({c.r}, {c.g}, {c.b}) keeps its colour");
            }

            // Every block of one colour is in four-colour mode or has index 0: nothing reads as the transparent black of the three-colour mode.
            byte[] grey = BlockCompress.Bc1Chain(Picture(4, 4, (x, y) => (128, 128, 128)), 4, 4);
            Program.Check(BlockCompress.DecodeBc1(grey, 0, 4, 4)[3] == 255, "opaque");
        }

        private static void Order()
        {
            // Four quadrants of their own colours: each comes back where it was, the rows as they are in the picture (the bottom first).
            const int w = 16, h = 8;
            (byte, byte, byte) Quadrant(int x, int y) => x < 8 ? (y < 4 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)255, (byte)0)) : (y < 4 ? ((byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255));
            byte[] picture = Picture(w, h, Quadrant);
            byte[] back = BlockCompress.DecodeBc1(BlockCompress.Bc1Chain(picture, w, h), 0, w, h);
            Program.Check(MaxError(picture, back, w * h) <= 1, "the quadrants stay where they are");
            Program.Check(back[0] > 250 && back[1] < 5 && back[((h - 1) * w) * 4 + 1] > 250 && back[(w - 1) * 4 + 2] > 250, "red at the first row's start, green at the last row's, blue at the first row's end");
        }

        private static void Mips()
        {
            // A checker of black and white: the next level is the grey that is half the light (188), not half the code (128), which would darken the strokes of a board seen from afar.
            uint[] checker = BlockCompress.Pack(Picture(4, 4, (x, y) => ((x + y) % 2 == 0) ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255)));
            uint[] half = BlockCompress.HalfLevel(checker, 4, 4, out int hw, out int hh);
            Program.Check(hw == 2 && hh == 2 && half.Length == 4, "half the size");
            int g = (int)(half[0] & 0xFF);
            Program.Check(Math.Abs(g - 188) <= 1, "half the light is 188: " + g);
            Program.Check((half[0] >> 24) == 255, "and opaque");

            // One colour stays that colour all the way down; an odd level averages 3 into one at its edge.
            uint[] flat = BlockCompress.Pack(Picture(6, 2, (x, y) => (0, 122, 61)));
            uint[] down = BlockCompress.HalfLevel(flat, 6, 2, out int dw, out int dh);
            Program.Check(dw == 3 && dh == 1 && Array.TrueForAll(down, p => p == flat[0]), "one colour stays as it is");
            uint[] odd = BlockCompress.HalfLevel(BlockCompress.Pack(Picture(3, 1, (x, y) => x == 2 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0))), 3, 1, out int ow, out int oh);
            Program.Check(ow == 1 && oh == 1 && Math.Abs((int)(odd[0] & 0xFF) - 156) <= 2, "three pixels into one at an odd edge (a third of the light): " + (odd[0] & 0xFF));

            // The chain: every level as BlockCompress reads it is near the level made from the one above.
            const int w = 64, h = 32;
            byte[] picture = Picture(w, h, (x, y) => ((x / 5 + y / 3) % 2 == 0) ? ((byte)0, (byte)74, (byte)153) : ((byte)250, (byte)250, (byte)250));
            byte[] chain = BlockCompress.Bc1Chain(picture, w, h);
            uint[] level = BlockCompress.Pack(picture);
            int lw = w, lh = h;
            for (int i = 1; i < BlockCompress.LevelCount(w, h); i++)
            {
                level = BlockCompress.HalfLevel(level, lw, lh, out lw, out lh);
                var want = new byte[lw * lh * 4];
                Buffer.BlockCopy(level, 0, want, 0, want.Length);
                byte[] got = BlockCompress.DecodeBc1(chain, BlockCompress.Bc1LevelOffset(w, h, i), lw, lh);
                Program.Check(Psnr(want, got, lw * lh) > 28.0, $"level {i} ({lw} x {lh}): {Psnr(want, got, lw * lh):0.0} dB");
            }
        }

        private static void Quality()
        {
            // A white disc with a soft edge on blue (a stroke of a character, its anti-aliasing): two colours and their mixes, which BC1 keeps well.
            const int w = 64, h = 64;
            byte[] disc = Picture(w, h, (x, y) =>
            {
                double d = Math.Sqrt((x - 31.5) * (x - 31.5) + (y - 31.5) * (y - 31.5)) - 20.0;
                double t = Math.Max(0.0, Math.Min(1.0, 0.5 - d));
                return ((byte)(0 + t * 250), (byte)(74 + t * 176), (byte)(153 + t * 97));
            });
            byte[] back = BlockCompress.DecodeBc1(BlockCompress.Bc1Chain(disc, w, h), 0, w, h);
            double psnr = Psnr(disc, back, w * h);
            Program.Check(psnr > 38.0, "a soft edge between two colours (39.5 dB when it was written): " + psnr.ToString("0.0") + " dB");

            // Three colours meeting in a block (a red ring, white, black): the worst case of BC1, still near.
            byte[] three = Picture(w, h, (x, y) => x < 20 ? ((byte)214, (byte)28, (byte)36) : x < 40 ? ((byte)250, (byte)250, (byte)250) : ((byte)18, (byte)18, (byte)18));
            byte[] threeBack = BlockCompress.DecodeBc1(BlockCompress.Bc1Chain(three, w, h), 0, w, h);
            Program.Check(Psnr(three, threeBack, w * h) > 25.0, "three colours: " + Psnr(three, threeBack, w * h).ToString("0.0") + " dB");
            // Where the boundary falls on the edge of a block, each block has two colours at most and keeps them.
            Program.Check(MaxError(three, threeBack, w * h) < 140, "and no pixel is far from its colour: " + MaxError(three, threeBack, w * h));
        }

        private static void Catalogue()
        {
            var bad = new List<string>();
            foreach (SignChoice c in SignCatalog.Choices)
            {
                if (c.Def.IsMarking)
                    continue;
                SignInstance instance = SignInstances.Of(c.Def, c.Spec);
                if (!BlockCompress.CanEncode(instance.TexW, instance.TexH))
                    bad.Add($"{c.Id} {instance.TexW}x{instance.TexH}");
                foreach (int detail in new[] { FaceDetail.Half, FaceDetail.Small })
                {
                    SignInstance sized = FaceDetail.Sized(instance, FaceDetail.MaxSide(detail));
                    if (!BlockCompress.CanEncode(sized.TexW, sized.TexH))
                        bad.Add($"{c.Id} at detail {detail} {sized.TexW}x{sized.TexH}");
                }
            }

            Program.Check(bad.Count == 0, "every face's picture is whole blocks at every detail: " + string.Join(", ", bad));
        }
    }
}
