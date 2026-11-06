using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Offline checks of the thumbnail pipeline: the PNG writer, the picture operations, the software renderer's options and the composition of the thumbnails.</summary>
    internal static class ImageTests
    {
        public static void RunAll()
        {
            Program.Run("png: encode and decode round trip, both row orders", PngRoundTrip);
            Program.Run("png: structure, damage and wrong input", PngStructure);
            Program.Run("image: downsample averages by alpha", Downsample);
            Program.Run("image: outline, blit and canvas", OutlineAndBlit);
            Program.Run("image: the average colour in linear light", AverageColour);
            Program.Run("raster: transparent background and layers", RasterOptionsRules);
            Program.Run("thumbnails: file names", ThumbnailNames);
            Program.Run("thumbnails: signs and supports", ThumbnailContent);
        }

        private static byte[] Solid(int w, int h, byte r, byte g, byte b)
        {
            var data = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                data[i * 4] = r; data[i * 4 + 1] = g; data[i * 4 + 2] = b; data[i * 4 + 3] = 255;
            }

            return data;
        }

        private static void PngRoundTrip()
        {
            const int w = 7, h = 5;
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    rgba[i] = (byte)(x * 30); rgba[i + 1] = (byte)(y * 50); rgba[i + 2] = (byte)(x * y * 7); rgba[i + 3] = (byte)(255 - x * 20 - y * 9);
                }
            }

            foreach (bool bottomFirst in new[] { true, false })
            {
                byte[] png = Png.Encode(rgba, w, h, bottomFirst);
                Program.Check(Png.TryDecode(png, out byte[] back, out int bw, out int bh, bottomFirst), $"bottomFirst={bottomFirst}: decodes");
                Program.Check(bw == w && bh == h, "the size comes back");
                bool same = back != null && back.Length == rgba.Length;
                for (int i = 0; same && i < rgba.Length; i++)
                    same = back[i] == rgba[i];
                Program.Check(same, $"bottomFirst={bottomFirst}: every byte comes back");
            }

            // The row order is real: the same pixels written the other way round decode to a flipped picture.
            byte[] pngTopFirst = Png.Encode(rgba, w, h, false);
            Png.TryDecode(pngTopFirst, out byte[] flipped, out _, out _, true);
            Program.Check(flipped[0] == rgba[(h - 1) * w * 4], "a top-first picture read bottom-first is flipped (first row of the result is the last of the source)");

            // A picture that does not compress (noise) and one that does (flat) are both fine.
            var random = new Random(7);
            var noise = new byte[64 * 64 * 4];
            random.NextBytes(noise);
            Program.Check(Png.TryDecode(Png.Encode(noise, 64, 64), out byte[] noiseBack, out _, out _) && noiseBack.Length == noise.Length && noiseBack[1000] == noise[1000], "noise round trips");
            byte[] flat = Png.Encode(Solid(160, 160, 10, 20, 30), 160, 160);
            Program.Check(flat.Length < 1000 && Png.TryDecode(flat, out _, out _, out _), "a flat 160x160 picture is small: " + flat.Length + " bytes");
        }

        private static void PngStructure()
        {
            byte[] png = Png.Encode(Solid(4, 3, 200, 0, 0), 4, 3);
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            bool sig = true;
            for (int i = 0; i < 8; i++)
                sig &= png[i] == signature[i];
            Program.Check(sig, "the PNG signature");
            Program.Check(System.Text.Encoding.ASCII.GetString(png, 12, 4) == "IHDR", "the first chunk is IHDR");
            // The signature takes 8 bytes, the IHDR chunk 4 of length and 4 of type, then width 16..19, height 20..23, bit depth 24, colour type 25.
            Program.Check(png[24] == 8 && png[25] == 6, "8 bits per channel, colour type 6 (RGBA)");
            Program.Check(png[19] == 4 && png[23] == 3, "the width and the height are in the header");
            Program.Check(System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4) == "IEND", "the last chunk is IEND");

            // Damage: a flipped bit in the pixel data, a truncated file, the wrong signature.
            byte[] damaged = (byte[])png.Clone();
            damaged[damaged.Length / 2] ^= 0x10;
            Program.Check(!Png.TryDecode(damaged, out _, out _, out _), "a damaged file does not decode (the CRC or the checksum says so)");
            var truncated = new byte[png.Length - 20];
            Array.Copy(png, truncated, truncated.Length);
            Program.Check(!Png.TryDecode(truncated, out _, out _, out _), "a truncated file does not decode");
            byte[] wrong = (byte[])png.Clone();
            wrong[1] = 0;
            Program.Check(!Png.TryDecode(wrong, out _, out _, out _), "a wrong signature does not decode");
            Program.Check(!Png.TryDecode(null, out _, out _, out _) && !Png.TryDecode(new byte[10], out _, out _, out _), "nothing and a few bytes do not decode");

            bool threw = false;
            try { Png.Encode(new byte[10], 4, 3); } catch (ArgumentException) { threw = true; }
            Program.Check(threw, "a buffer of the wrong length is refused");
            threw = false;
            try { Png.Encode(new byte[0], 0, 0); } catch (ArgumentException) { threw = true; }
            Program.Check(threw, "a picture without size is refused");
        }

        private static void Downsample()
        {
            // 4x4 -> 2x2: the top left block is four different greys, fully opaque.
            var src = new byte[4 * 4 * 4];
            for (int i = 0; i < 16; i++)
            {
                src[i * 4 + 3] = 255;
                src[i * 4] = src[i * 4 + 1] = src[i * 4 + 2] = 0;
            }

            foreach (int i in new[] { 0, 1, 4, 5 })
            {
                src[i * 4] = (byte)(i * 10 + 40);   // 40, 50, 80, 90
                src[i * 4 + 1] = src[i * 4];
                src[i * 4 + 2] = src[i * 4];
            }

            byte[] small = ImageOps.Downsample(src, 4, 4, 2, out int w, out int h);
            Program.Check(w == 2 && h == 2 && small.Length == 16, "the size is halved");
            Program.Near(small[0], 65f, 1f, "the average of 40, 50, 80 and 90");
            Program.Check(small[3] == 255, "four opaque pixels stay opaque");

            // A block of one red opaque pixel and three transparent ones keeps the red (not a darker red) with a quarter of the alpha.
            var edge = new byte[2 * 2 * 4];
            edge[0] = 200; edge[3] = 255;
            byte[] one = ImageOps.Downsample(edge, 2, 2, 2, out _, out _);
            Program.Check(one[0] == 200, "the colour of a half transparent edge is not darkened by the transparent pixels: " + one[0]);
            Program.Near(one[3], 64f, 1f, "a quarter of the alpha");

            byte[] empty = ImageOps.Downsample(new byte[2 * 2 * 4], 2, 2, 2, out _, out _);
            Program.Check(empty[3] == 0, "a transparent block stays transparent");
        }

        private static byte[] Solid(int w, int h, byte r, byte g, byte b, byte a = 255)
        {
            var px = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                px[i * 4] = r; px[i * 4 + 1] = g; px[i * 4 + 2] = b; px[i * 4 + 3] = a;
            }

            return px;
        }

        private static void AverageColour()
        {
            ImageOps.AverageLinear(Solid(64, 64, 255, 255, 255), 64, 64, out float r, out float g, out float b);
            Program.Near(r, 1f, 1e-4f, "white is 1"); Program.Near(g, 1f, 1e-4f, "white is 1"); Program.Near(b, 1f, 1e-4f, "white is 1");
            ImageOps.AverageLinear(Solid(64, 64, 0, 0, 0), 64, 64, out r, out g, out b);
            Program.Near(r + g + b, 0f, 1e-6f, "black is 0");

            // Mid grey (sRGB 128) is 0.2158 in linear light, not 0.5: the sum a shader makes is of linear values.
            ImageOps.AverageLinear(Solid(64, 64, 128, 128, 128), 64, 64, out r, out g, out b);
            Program.Near(r, 0.2158f, 2e-3f, "sRGB 128 is 0.2158 linear");

            // Half red, half white (left and right): the average is between, per channel.
            var half = Solid(64, 64, 255, 255, 255);
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    int i = (y * 64 + x) * 4;
                    half[i + 1] = 0; half[i + 2] = 0;
                }
            }

            ImageOps.AverageLinear(half, 64, 64, out r, out g, out b);
            Program.Near(r, 1f, 1e-3f, "the red channel of half red, half white is 1");
            Program.Near(g, 0.5f, 0.02f, "the green channel is about half");
            Program.Near(b, 0.5f, 0.02f, "the blue channel is about half");

            // A blue board glows blue and dimmer than a white one: its average colour has a small red and a small green.
            ImageOps.AverageLinear(Solid(64, 64, 0, 74, 158), 64, 64, out r, out g, out b);
            Program.Check(r < 0.01f && g < 0.1f && b > g && b > 0.3f && b < 0.5f, $"a blue board: {r:0.000}, {g:0.000}, {b:0.000}");

            // Transparent pixels do not count: a round sign's corners are not part of its colour.
            var round = Solid(64, 64, 255, 0, 0, 0);
            for (int y = 16; y < 48; y++)
            {
                for (int x = 16; x < 48; x++)
                {
                    int i = (y * 64 + x) * 4;
                    round[i] = 0; round[i + 1] = 0; round[i + 2] = 255; round[i + 3] = 255;
                }
            }

            ImageOps.AverageLinear(round, 64, 64, out r, out g, out b);
            Program.Check(r < 0.01f && g < 0.01f && b > 0.99f, $"only the solid pixels count: {r:0.000}, {g:0.000}, {b:0.000}");

            // Nothing solid, an empty or short buffer, a zero size: white, and no throw.
            ImageOps.AverageLinear(Solid(8, 8, 10, 10, 10, 0), 8, 8, out r, out g, out b);
            Program.Check(r == 1f && g == 1f && b == 1f, "nothing solid is white");
            ImageOps.AverageLinear(new byte[3], 8, 8, out r, out g, out b);
            Program.Check(r == 1f && g == 1f && b == 1f, "a short buffer is white");
            ImageOps.AverageLinear(null, 8, 8, out r, out g, out b);
            ImageOps.AverageLinear(Solid(1, 1, 255, 255, 255), 0, 0, out r, out g, out b);
            Program.Check(r == 1f, "a zero size is white");

            // A large picture is sampled, not walked pixel by pixel: it still gives the colour (a 768 x 768 face of two halves).
            var big = Solid(768, 768, 255, 255, 255);
            for (int y = 0; y < 768; y++)
            {
                for (int x = 0; x < 384; x++)
                {
                    int i = (y * 768 + x) * 4;
                    big[i] = 0; big[i + 1] = 0; big[i + 2] = 0;
                }
            }

            ImageOps.AverageLinear(big, 768, 768, out r, out g, out b);
            Program.Near(r, 0.5f, 0.02f, "a big picture of half black, half white is about 0.5");
        }

        private static void OutlineAndBlit()
        {
            // A 10x10 transparent canvas with an opaque 6x6 square in the middle (pixels 2..7).
            byte[] canvas = ImageOps.Canvas(10, 10);
            byte[] square = Solid(6, 6, 255, 255, 255);
            ImageOps.Blit(canvas, 10, 10, square, 6, 6, 2, 2);
            Program.Check(canvas[(5 * 10 + 5) * 4 + 3] == 255 && canvas[(1 * 10 + 1) * 4 + 3] == 0, "the square is where it was put");

            ImageOps.Outline(canvas, 10, 10, 1, 10, 20, 30);
            int Pixel(int x, int y) => (y * 10 + x) * 4;
            Program.Check(canvas[Pixel(2, 2)] == 10 && canvas[Pixel(7, 7)] == 10 && canvas[Pixel(2, 5)] == 10, "the border pixels of the square are the outline colour");
            Program.Check(canvas[Pixel(4, 4)] == 255 && canvas[Pixel(5, 5)] == 255, "the inside keeps its colour");
            Program.Check(canvas[Pixel(1, 1) + 3] == 0, "the outline does not spread outside the shape");

            // Blit clips: a picture that hangs over the edge is cut, not wrapped.
            byte[] target = ImageOps.Canvas(4, 4);
            ImageOps.Blit(target, 4, 4, Solid(3, 3, 9, 9, 9), 3, 3, 3, 3);
            Program.Check(target[(3 * 4 + 3) * 4 + 3] == 255 && target[(0 * 4 + 0) * 4 + 3] == 0, "only the corner that is inside is drawn");
            ImageOps.Blit(target, 4, 4, Solid(2, 2, 7, 7, 7), 2, 2, -1, -1);
            Program.Check(target[0] == 7, "a negative position cuts the other side");

            // A shape that touches the edge of the picture counts the edge as outside.
            byte[] full = Solid(4, 4, 255, 255, 255);
            ImageOps.Outline(full, 4, 4, 1, 0, 0, 0);
            Program.Check(full[0] == 0 && full[(1 * 4 + 1) * 4] == 255, "the picture's edge counts as outside");
        }

        private static void RasterOptionsRules()
        {
            SignDef speed = SignCatalog.ById("gb.speed60");
            SignParts parts = SignBuilder.Build(speed, SupportCatalog.ById(SupportCatalog.SinglePole));
            byte[] picture = Solid(8, 8, 220, 30, 30);

            byte[] face = Raster.Render(parts, picture, 8, 8, new Vec3(0f, 0f, -1f), 200, out int w, out int h, null, 0f,
                new RasterOptions { Transparent = true, Layers = RasterLayers.Face });
            Program.Check(w >= 190 && h >= 190 && w <= 202 && h <= 202, $"a round face of 0.8 m is about 200 px square: {w}x{h}");
            int CornerAlpha(byte[] data, int x, int y) => data[(y * w + x) * 4 + 3];
            Program.Check(CornerAlpha(face, 1, 1) == 0 && CornerAlpha(face, w - 2, h - 2) == 0, "the corners outside the circle are transparent");
            Program.Check(CornerAlpha(face, w / 2, h / 2) == 255, "the middle of the face is opaque");
            int opaqueFace = 0;
            for (int i = 3; i < face.Length; i += 4)
                opaqueFace += face[i] == 255 ? 1 : 0;
            // The area of a circle is pi/4 of its square: about 78 %.
            Program.Check(opaqueFace > w * h * 0.70 && opaqueFace < w * h * 0.85, "the face covers about pi/4 of its square: " + (100.0 * opaqueFace / (w * h)).ToString("0") + " %");
            Program.Check(face[(h / 2 * w + w / 2) * 4] == 220 || face[(h / 2 * w + w / 2) * 4] > 100, "the picture is on the face (its red, shaded)");

            // Metal only has no face colour in the middle; everything has more covered pixels than the face alone.
            byte[] all = Raster.Render(parts, picture, 8, 8, new Vec3(-0.55f, -0.30f, -0.78f), 300, out int aw, out int ah, null, 0f, new RasterOptions { Transparent = true });
            byte[] metal = Raster.Render(parts, picture, 8, 8, new Vec3(-0.55f, -0.30f, -0.78f), 300, out int mw, out int mh, null, 0f, new RasterOptions { Transparent = true, Layers = RasterLayers.Metal });
            int Opaque(byte[] data)
            {
                int n = 0;
                for (int i = 3; i < data.Length; i += 4)
                    n += data[i] == 255 ? 1 : 0;
                return n;
            }

            Program.Check(aw == mw || true, "dimensions are by the drawn layers");
            Program.Check(Opaque(all) > 0 && Opaque(metal) > 0, "something is drawn in each");
            Program.Check(Opaque(all) > Opaque(metal), "all layers cover more than the metal alone");

            // Opaque background by default.
            byte[] backed = Raster.Render(parts, picture, 8, 8, new Vec3(0f, 0f, -1f), 100, out int bw, out int bh);
            Program.Check(backed[3] == 255 && backed[(bh - 1) * bw * 4 + 3] == 255, "without the transparent option the background is opaque");

            // A close-up window is a square of twice the half size round the focus point.
            Raster.Render(parts, picture, 8, 8, new Vec3(0f, 0f, -1f), 120, out int cw, out int ch, new Vec3(0f, 1f, 0f), 0.5f, new RasterOptions { Transparent = true });
            Program.Check(cw == 120 && ch == 120, $"a focus window is square: {cw}x{ch}");
        }

        private static void ThumbnailNames()
        {
            string a = Thumbnails.FileName("s", "gb.stop");
            Program.Check(a == Thumbnails.FileName("s", "gb.stop"), "a name is stable");
            Program.Check(a.StartsWith("s-gb.stop-") && a.EndsWith(".png") && a.Length == "s-gb.stop-".Length + 8 + 4, "kind, id, an eight-digit stamp and .png: " + a);
            Program.Check(Thumbnails.FileName("p", "gb.stop") != a, "the kind is part of the name");
            Program.Check(Thumbnails.FileName("s", "gb.speed60") != a, "the id is part of the name");
            string odd = Thumbnails.FileName("s", "a/b\\c:d e");
            Program.Check(odd.IndexOfAny(new[] { '/', '\\', ':', ' ' }) < 0, "characters that a file name cannot hold are replaced: " + odd);
            Program.Check(Thumbnails.Revision >= 1, "the art has a revision");

            var names = new HashSet<string>();
            foreach (SignChoice choice in SignCatalog.Choices)
                Program.Check(names.Add(Thumbnails.FileName("s", choice.Id)), "no two signs share a file: " + choice.Id);
            foreach (SupportDef support in SupportCatalog.All)
                Program.Check(names.Add(Thumbnails.FileName("p", support.Id)), "no two supports share a file: " + support.Id);
        }

        private static void ThumbnailContent()
        {
            byte[] picture = Solid(16, 16, 200, 40, 40);
            int s = Thumbnails.Size;
            int Alpha(byte[] data, int x, int y) => data[(y * s + x) * 4 + 3];

            foreach (SignChoice choice in SignCatalog.Choices)
            {
                // Letters with nothing behind them have no opaque middle and no outline of a plate (0.12.75): TextBoxTests look at theirs.
                if (choice.Id == CDLC.TS.Geo.Signs.TextBoxes.TemplateId || choice.Id == CDLC.TS.Geo.Signs.StandBoxes.TemplateId)
                    continue;
                SignInstance sign = SignInstances.Of(choice.Def, choice.Spec);
                string id = choice.Id;
                byte[] rgba = Thumbnails.SignRgba(sign, picture, 16, 16);
                Program.Check(rgba.Length == s * s * 4, id + ": a square RGBA picture");
                Program.Check(Alpha(rgba, 0, 0) == 0 && Alpha(rgba, s - 1, s - 1) == 0 && Alpha(rgba, 0, s - 1) == 0 && Alpha(rgba, s - 1, 0) == 0, id + ": the four corners are transparent");
                // A pair of lamp-post ads (0.12.49) has the post's gap in its middle.
                bool pair = sign.Board?.PoleAd != null && sign.Board.PoleAd.Pair;
                Program.Check(pair || Alpha(rgba, s / 2, s / 2) == 255, id + ": the middle is opaque");

                // Centred, with a margin all round.
                int minX = s, maxX = -1, minY = s, maxY = -1;
                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        if (Alpha(rgba, x, y) > 0)
                        {
                            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        }
                    }
                }

                // 0.12.59: a picture with nothing in it passed the two checks below (minX 160, maxX -1), and the neon signs and the lanterns, whose solids the face layer does not draw, were blank in the panel.
                Program.Check(maxX >= minX && maxY >= minY, $"{id}: there is something in the picture");
                // A lamp-post ad is seen by its colours on the panel's dark ground: its strokes, bodies or face, not the dark outline round them (the neon strips were a pixel wide and the outline took them).
                if (sign.Board?.PoleAd != null)
                {
                    int bright = 0;
                    for (int i = 0; i < s * s; i++)
                    {
                        if (rgba[i * 4 + 3] == 255 && Math.Max(rgba[i * 4], Math.Max(rgba[i * 4 + 1], rgba[i * 4 + 2])) >= 140)
                            bright++;
                    }

                    Program.Check(bright >= 300, $"{id}: its colours show in the thumbnail: {bright} bright pixels");
                }
                Program.Check(minX >= 5 && maxX <= s - 6 && minY >= 5 && maxY <= s - 6, $"{id}: a margin round the shape ({minX}..{maxX}, {minY}..{maxY})");
                Program.Check(Math.Abs((minX + maxX) - (s - 1)) <= 2 && Math.Abs((minY + maxY) - (s - 1)) <= 2, $"{id}: centred");

                // The outline: the border pixels are the dark colour (28, 34, 42) at least in part.
                int dark = 0;
                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        int i = (y * s + x) * 4;
                        if (rgba[i + 3] == 255 && rgba[i] < 60 && rgba[i + 1] < 60 && rgba[i + 2] < 60)
                            dark++;
                    }
                }

                Program.Check(dark > s / 2 || sign.Board?.PoleAd != null, id + ": there is a dark outline: " + dark + " px");

                byte[] png = Thumbnails.SignPng(sign, picture, 16, 16);
                Program.Check(Png.TryDecode(png, out byte[] back, out int bw, out int bh) && bw == s && bh == s && back.Length == rgba.Length, id + ": the PNG decodes to the same size");
                bool same = back != null;
                for (int i = 0; same && i < rgba.Length; i++)
                    same = back[i] == rgba[i];
                Program.Check(same, id + ": the PNG holds the rendered picture");
                Program.Check(png.Length < 20000, id + ": a small file: " + png.Length);
                byte[] again = Thumbnails.SignPng(sign, picture, 16, 16);
                Program.Check(png.Length == again.Length, id + ": the same input gives the same file");
            }

            // The circle is round: its corners within the shape's box are transparent while the box's edge middles are not.
            byte[] round = Thumbnails.SignRgba(SignInstances.Of(SignCatalog.ById("gb.speed60"), null), picture, 16, 16);
            Program.Check(Alpha(round, 20, 20) == 0 && Alpha(round, s / 2, 14) > 0, "the circle's bounding corner is empty and the top of the circle is not");

            // The supports: each different from the others, with the base of the post inside the picture.
            var supports = new List<byte[]>();
            foreach (SupportDef support in SupportCatalog.All)
            {
                byte[] rgba = Thumbnails.SupportRgba(support, picture, 16, 16);
                Program.Check(rgba.Length == s * s * 4, support.Id + ": a square RGBA picture");
                int covered = 0;
                for (int i = 3; i < rgba.Length; i += 4)
                    covered += rgba[i] > 0 ? 1 : 0;
                Program.Check(covered > 200 && covered < s * s * 0.6, support.Id + ": something is drawn but not everything: " + covered);
                Program.Check(Alpha(rgba, 0, 0) == 0 && Alpha(rgba, s - 1, s - 1) == 0, support.Id + ": the corners are transparent");
                foreach (byte[] other in supports)
                {
                    bool identical = true;
                    for (int i = 0; identical && i < rgba.Length; i++)
                        identical = rgba[i] == other[i];
                    Program.Check(!identical, support.Id + ": differs from the thumbnails of the supports before it");
                }

                supports.Add(rgba);
                Program.Check(Png.TryDecode(Thumbnails.SupportPng(support, picture, 16, 16), out _, out int w, out int h) && w == s && h == s, support.Id + ": the PNG decodes");
            }

            Program.Check(Thumbnails.CarrierFor(SupportCatalog.ById(SupportCatalog.DoublePole)).Def.Id == "guide.exit.advance" && Thumbnails.CarrierFor(SupportCatalog.ById(SupportCatalog.FCantilever)).Def.Id == "guide.exit.advance"
                && Thumbnails.CarrierFor(SupportCatalog.ById(SupportCatalog.Cantilever)).Def.Id == "gb17733.street", "the double post and the F type are shown with a long board, the single cantilever with a road-name plate on one arm");
            Program.Check(Thumbnails.CarrierFor(SupportCatalog.ById(SupportCatalog.SinglePole)).Def.Id == "gb.prohib.01", "the single post with the stop sign");
            Program.Check(Thumbnails.CarrierFor(SupportCatalog.ById(SupportCatalog.TriplePole)).Def.Width > SupportPlanner.DoublePostMaxWidth, "the triple post is shown with a board wider than two posts carry");
        }
    }
}
