using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The pictures of the faces as BC1 (DXT1) blocks with their mips (0.12.27): half a byte a pixel where RGBA32 takes four, so a face takes an eighth of the graphics memory it did, and the main
    /// thread only hands the bytes over (LoadRawTextureData) instead of making the mips. The faces are opaque (the painter writes alpha 255), so the four-colour mode is all that is needed. The mips
    /// are box-filtered in linear light (an sRGB picture averaged as stored darkens thin white strokes on a dark ground as it gets smaller). Rows are in the order of the picture (the bottom row first,
    /// as a Unity texture's); a block covers four rows of it. The pixels are worked on as one uint each (R in the low byte): Mono checks every index of an array, a quarter as many reads is most of the
    /// speed. Pure, so that it is tested offline; safe from any thread.
    /// </summary>
    public static class BlockCompress
    {
        /// <summary>The bytes of one level of BC1 (8 a block of 4 x 4, a level smaller than a block is one block).</summary>
        public static int Bc1LevelSize(int w, int h) => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8;

        /// <summary>The number of levels of a full mip chain down to 1 x 1, as a Unity texture with mips has.</summary>
        public static int LevelCount(int w, int h)
        {
            int n = 1, m = Math.Max(w, h);
            while (m > 1)
            {
                m >>= 1;
                n++;
            }

            return n;
        }

        /// <summary>The bytes of a whole chain of BC1 levels.</summary>
        public static int Bc1ChainSize(int w, int h) => Bc1LevelOffset(w, h, LevelCount(w, h));

        /// <summary>The offset of a level in a chain made by Bc1Chain.</summary>
        public static int Bc1LevelOffset(int w, int h, int level)
        {
            int offset = 0;
            for (int i = 0; i < level; i++)
                offset += Bc1LevelSize(Math.Max(1, w >> i), Math.Max(1, h >> i));
            return offset;
        }

        /// <summary>Whether a picture can be made into BC1: its first level must be whole blocks (a GPU takes BC only in multiples of four).</summary>
        public static bool CanEncode(int w, int h) => w >= 4 && h >= 4 && w % 4 == 0 && h % 4 == 0;

        // ------------------------------------------------------------------ the mips

        /// <summary>sRGB to linear light in 16 bits, and back from linear (16 bits) to sRGB.</summary>
        private static readonly int[] s_ToLinear = BuildToLinear();
        private static readonly byte[] s_ToSrgb = BuildToSrgb();

        private static int[] BuildToLinear()
        {
            var t = new int[256];
            for (int i = 0; i < 256; i++)
            {
                double c = i / 255.0;
                t[i] = (int)Math.Round((c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4)) * 65535.0);
            }

            return t;
        }

        private static byte[] BuildToSrgb()
        {
            var t = new byte[65536];
            for (int i = 0; i < t.Length; i++)
            {
                double l = i / 65535.0;
                double c = l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055;
                t[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(c * 255.0)));
            }

            return t;
        }

        /// <summary>The pixels of a picture of RGBA bytes as one uint each (R in the low byte).</summary>
        public static uint[] Pack(byte[] rgba)
        {
            var pixels = new uint[rgba.Length / 4];
            Buffer.BlockCopy(rgba, 0, pixels, 0, pixels.Length * 4);
            if (!BitConverter.IsLittleEndian)
            {
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = (uint)(rgba[i * 4] | (rgba[i * 4 + 1] << 8) | (rgba[i * 4 + 2] << 16) | (rgba[i * 4 + 3] << 24));
            }

            return pixels;
        }

        /// <summary>
        /// The next level of a picture: half the size each way (at least 1), each pixel the average in linear light of the pixels of the level above that it covers (2 x 2, or 3 at an odd edge). Alpha is
        /// averaged as it is.
        /// </summary>
        public static uint[] HalfLevel(uint[] src, int w, int h, out int nw, out int nh)
        {
            nw = Math.Max(1, w / 2);
            nh = Math.Max(1, h / 2);
            var dst = new uint[nw * nh];
            int[] lin = s_ToLinear;
            byte[] srgb = s_ToSrgb;
            if (w == nw * 2 && h == nh * 2)
            {
                // The usual case, 2 x 2 to one, written out.
                for (int y = 0; y < nh; y++)
                {
                    int top = y * 2 * w, bottom = top + w, o = y * nw;
                    for (int x = 0; x < nw; x++, top += 2, bottom += 2)
                    {
                        uint p0 = src[top], p1 = src[top + 1], p2 = src[bottom], p3 = src[bottom + 1];
                        if (p0 == p1 && p0 == p2 && p0 == p3)
                        {
                            dst[o + x] = p0;
                            continue;
                        }

                        int r = lin[p0 & 0xFF] + lin[p1 & 0xFF] + lin[p2 & 0xFF] + lin[p3 & 0xFF];
                        int g = lin[(p0 >> 8) & 0xFF] + lin[(p1 >> 8) & 0xFF] + lin[(p2 >> 8) & 0xFF] + lin[(p3 >> 8) & 0xFF];
                        int b = lin[(p0 >> 16) & 0xFF] + lin[(p1 >> 16) & 0xFF] + lin[(p2 >> 16) & 0xFF] + lin[(p3 >> 16) & 0xFF];
                        uint a = ((p0 >> 24) + (p1 >> 24) + (p2 >> 24) + (p3 >> 24) + 2) >> 2;
                        dst[o + x] = srgb[(r + 2) >> 2] | ((uint)srgb[(g + 2) >> 2] << 8) | ((uint)srgb[(b + 2) >> 2] << 16) | (a << 24);
                    }
                }

                return dst;
            }

            for (int y = 0; y < nh; y++)
            {
                int y0 = y * h / nh, y1 = Math.Max(y0 + 1, (y + 1) * h / nh);
                for (int x = 0; x < nw; x++)
                {
                    int x0 = x * w / nw, x1 = Math.Max(x0 + 1, (x + 1) * w / nw);
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int row = sy * w;
                        for (int sx = x0; sx < x1; sx++)
                        {
                            uint p = src[row + sx];
                            r += lin[p & 0xFF];
                            g += lin[(p >> 8) & 0xFF];
                            b += lin[(p >> 16) & 0xFF];
                            a += (int)(p >> 24);
                            n++;
                        }
                    }

                    int half = n >> 1;
                    dst[y * nw + x] = srgb[(r + half) / n] | ((uint)srgb[(g + half) / n] << 8) | ((uint)srgb[(b + half) / n] << 16) | ((uint)((a + half) / n) << 24);
                }
            }

            return dst;
        }

        /// <summary>
        /// The whole chain of a picture (RGBA bytes, the bottom row first, its size whole blocks: CanEncode) as BC1: the first level from the picture, each next one from the level above (HalfLevel), one after
        /// the other, as LoadRawTextureData takes them for a texture with a full mip chain.
        /// </summary>
        public static byte[] Bc1Chain(byte[] rgba, int w, int h)
        {
            if (!CanEncode(w, h))
                throw new ArgumentException($"a picture of {w} x {h} is not whole blocks of 4 x 4");
            if (rgba == null || rgba.Length != w * h * 4)
                throw new ArgumentException("the picture is not w x h x 4 bytes");
            var output = new byte[Bc1ChainSize(w, h)];
            int offset = 0, levels = LevelCount(w, h);
            uint[] level = Pack(rgba);
            int lw = w, lh = h;
            var scratch = new Scratch();
            for (int i = 0; i < levels; i++)
            {
                offset += EncodeLevel(level, lw, lh, output, offset, scratch);
                if (i + 1 < levels)
                    level = HalfLevel(level, lw, lh, out lw, out lh);
            }

            return output;
        }

        // ------------------------------------------------------------------ one level

        /// <summary>What one encoding keeps between blocks (no allocation per block, nothing shared between threads), and the last block of one colour, which the next one often is.</summary>
        private sealed class Scratch
        {
            public readonly uint[] Block = new uint[16];
            public readonly int[] R = new int[16], G = new int[16], B = new int[16];
            public readonly int[] Pal = new int[12];
            public uint SameColour = 0xFFFFFFFFu;
            public bool HasSame;
            public int SameC0, SameC1;
            public uint SameIndices;
        }

        /// <summary>One level of BC1 into output at offset; a level whose size is not whole blocks repeats its last row and column into the blocks it needs. Returns the bytes written.</summary>
        private static int EncodeLevel(uint[] px, int w, int h, byte[] output, int offset, Scratch s)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            uint[] block = s.Block;
            int at = offset;
            for (int by = 0; by < bh; by++)
            {
                bool inside = by * 4 + 3 < h;
                for (int bx = 0; bx < bw; bx++)
                {
                    if (inside && bx * 4 + 3 < w)
                    {
                        // Most blocks of a sign are one colour: that is told from the picture before anything is copied.
                        int p = by * 4 * w + bx * 4;
                        uint f = px[p] & 0x00FFFFFFu;
                        bool same = true;
                        for (int j = 0, q = p; j < 4 && same; j++, q += w)
                            same = (px[q] & 0x00FFFFFFu) == f && (px[q + 1] & 0x00FFFFFFu) == f && (px[q + 2] & 0x00FFFFFFu) == f && (px[q + 3] & 0x00FFFFFFu) == f;
                        if (same)
                        {
                            OneColour(s, f, output, at);
                            at += 8;
                            continue;
                        }

                        for (int j = 0; j < 4; j++, p += w)
                        {
                            block[j * 4] = px[p];
                            block[j * 4 + 1] = px[p + 1];
                            block[j * 4 + 2] = px[p + 2];
                            block[j * 4 + 3] = px[p + 3];
                        }
                    }
                    else
                    {
                        for (int j = 0; j < 4; j++)
                        {
                            int row = Math.Min(h - 1, by * 4 + j) * w;
                            for (int i = 0; i < 4; i++)
                                block[j * 4 + i] = px[row + Math.Min(w - 1, bx * 4 + i)];
                        }
                    }

                    EncodeBlock(s, output, at);
                    at += 8;
                }
            }

            return at - offset;
        }

        // The endpoints of a block of one colour (stb_dxt's way): for a value v of a channel, the two quantized values whose two-thirds mix (index 2) is nearest to v.
        private static readonly byte[] s_Match5 = BuildMatch(5), s_Match6 = BuildMatch(6);

        private static int Expand5(int c) => (c << 3) | (c >> 2);

        private static int Expand6(int c) => (c << 2) | (c >> 4);

        private static byte[] BuildMatch(int bits)
        {
            int size = 1 << bits;
            var table = new byte[256 * 2];
            for (int v = 0; v < 256; v++)
            {
                int bestErr = int.MaxValue;
                for (int a = 0; a < size; a++)
                {
                    int ea = bits == 5 ? Expand5(a) : Expand6(a);
                    for (int c = 0; c < size; c++)
                    {
                        int ec = bits == 5 ? Expand5(c) : Expand6(c);
                        int mix = (2 * ea + ec) / 3;
                        // A little penalty for endpoints far apart: GPUs round the mix differently, and the nearer the endpoints the less that shows.
                        int err = Math.Abs(mix - v) * 100 + Math.Abs(ea - ec);
                        if (err < bestErr)
                        {
                            bestErr = err;
                            table[v * 2] = (byte)a;
                            table[v * 2 + 1] = (byte)c;
                        }
                    }
                }
            }

            return table;
        }

        private static int Pack565(int r5, int g6, int b5) => (r5 << 11) | (g6 << 5) | b5;

        private static int Quantize(int r, int g, int b) => Pack565((r * 31 + 127) / 255, (g * 63 + 127) / 255, (b * 31 + 127) / 255);

        private static void Write(byte[] output, int at, int c0, int c1, uint indices)
        {
            output[at] = (byte)c0;
            output[at + 1] = (byte)(c0 >> 8);
            output[at + 2] = (byte)c1;
            output[at + 3] = (byte)(c1 >> 8);
            output[at + 4] = (byte)indices;
            output[at + 5] = (byte)(indices >> 8);
            output[at + 6] = (byte)(indices >> 16);
            output[at + 7] = (byte)(indices >> 24);
        }

        /// <summary>A block of one colour (RGB, R in the low byte), exactly by the match tables: the endpoints whose two-thirds mix is the colour, all indices that mix.</summary>
        private static void OneColour(Scratch s, uint colour, byte[] output, int at)
        {
            if (!s.HasSame || colour != s.SameColour)
            {
                int vr = (int)(colour & 0xFF), vg = (int)((colour >> 8) & 0xFF), vb = (int)((colour >> 16) & 0xFF);
                int c0 = Pack565(s_Match5[vr * 2], s_Match6[vg * 2], s_Match5[vb * 2]), c1 = Pack565(s_Match5[vr * 2 + 1], s_Match6[vg * 2 + 1], s_Match5[vb * 2 + 1]);
                // Index 2 is two thirds of the first endpoint; with the endpoints the other way round it is index 3. One endpoint for both: index 0.
                s.SameC0 = Math.Max(c0, c1);
                s.SameC1 = Math.Min(c0, c1);
                s.SameIndices = c0 == c1 ? 0u : c0 > c1 ? 0xAAAAAAAAu : 0xFFFFFFFFu;
                s.SameColour = colour;
                s.HasSame = true;
            }

            Write(output, at, s.SameC0, s.SameC1, s.SameIndices);
        }

        /// <summary>
        /// A block of 16 pixels as BC1: one colour exactly (OneColour); else the endpoints at the ends of the colours' principal axis, refined once by least squares from the indices when that leaves less
        /// error, each colour the nearest of the four of the palette.
        /// </summary>
        private static void EncodeBlock(Scratch s, byte[] output, int at)
        {
            uint[] block = s.Block;
            uint first = block[0] & 0x00FFFFFFu;
            bool same = true;
            for (int k = 1; k < 16 && same; k++)
                same = (block[k] & 0x00FFFFFFu) == first;
            if (same)
            {
                OneColour(s, first, output, at);
                return;
            }

            int[] R = s.R, G = s.G, B = s.B;
            int sr = 0, sg = 0, sb = 0;
            for (int k = 0; k < 16; k++)
            {
                uint p = block[k];
                int r = (int)(p & 0xFF), g = (int)((p >> 8) & 0xFF), b = (int)((p >> 16) & 0xFF);
                R[k] = r;
                G[k] = g;
                B[k] = b;
                sr += r;
                sg += g;
                sb += b;
            }

            // The covariance of the colours, and the principal axis by a few steps of the power method from the channel that varies most.
            float mr = sr / 16f, mg = sg / 16f, mb = sb / 16f;
            float crr = 0f, cgg = 0f, cbb = 0f, crg = 0f, crb = 0f, cgb = 0f;
            for (int k = 0; k < 16; k++)
            {
                float r = R[k] - mr, g = G[k] - mg, b = B[k] - mb;
                crr += r * r;
                cgg += g * g;
                cbb += b * b;
                crg += r * g;
                crb += r * b;
                cgb += g * b;
            }

            float vx, vy, vz;
            if (crr >= cgg && crr >= cbb) { vx = 1f; vy = crg / crr; vz = crb / crr; }
            else if (cgg >= cbb) { vx = crg / cgg; vy = 1f; vz = cgb / cgg; }
            else { vx = crb / cbb; vy = cgb / cbb; vz = 1f; }
            for (int it = 0; it < 3; it++)
            {
                float nx = crr * vx + crg * vy + crb * vz, ny = crg * vx + cgg * vy + cgb * vz, nz = crb * vx + cgb * vy + cbb * vz;
                float m = Math.Max(Math.Abs(nx), Math.Max(Math.Abs(ny), Math.Abs(nz)));
                if (m < 1e-6f)
                    break;
                vx = nx / m;
                vy = ny / m;
                vz = nz / m;
            }

            // The ends: the colours that lie furthest along the axis each way.
            float lo = float.MaxValue, hi = float.MinValue;
            int ilo = 0, ihi = 0;
            for (int k = 0; k < 16; k++)
            {
                float d = R[k] * vx + G[k] * vy + B[k] * vz;
                if (d < lo) { lo = d; ilo = k; }
                if (d > hi) { hi = d; ihi = k; }
            }

            int a = Quantize(R[ihi], G[ihi], B[ihi]), b0 = Quantize(R[ilo], G[ilo], B[ilo]);
            uint indices = Indices(s, a, b0, out int error);
            if (error > 0 && Refine(s, indices, out int ra, out int rb))
            {
                uint refined = Indices(s, ra, rb, out int refinedError);
                if (refinedError < error)
                {
                    a = ra;
                    b0 = rb;
                    indices = refined;
                }
            }

            Write(output, at, Math.Max(a, b0), Math.Min(a, b0), indices);
        }

        /// <summary>
        /// The index of each pixel for the endpoints in four-colour order (c0 the larger), the nearest of the four colours of the palette, and the squared error of the block. Endpoints that are one colour
        /// give every index 0 (the block is then read in three-colour mode, whose index 3 would be black).
        /// </summary>
        private static uint Indices(Scratch s, int a, int b, out int error)
        {
            int c0 = Math.Max(a, b), c1 = Math.Min(a, b);
            int[] R = s.R, G = s.G, B = s.B, pal = s.Pal;
            int r0 = Expand5((c0 >> 11) & 31), g0 = Expand6((c0 >> 5) & 63), b0 = Expand5(c0 & 31);
            int r1 = Expand5((c1 >> 11) & 31), g1 = Expand6((c1 >> 5) & 63), b1 = Expand5(c1 & 31);
            error = 0;
            if (c0 == c1)
            {
                for (int k = 0; k < 16; k++)
                {
                    int dr = R[k] - r0, dg = G[k] - g0, db = B[k] - b0;
                    error += dr * dr + dg * dg + db * db;
                }

                return 0u;
            }

            pal[0] = r0; pal[1] = g0; pal[2] = b0;
            pal[3] = r1; pal[4] = g1; pal[5] = b1;
            pal[6] = (2 * r0 + r1) / 3; pal[7] = (2 * g0 + g1) / 3; pal[8] = (2 * b0 + b1) / 3;
            pal[9] = (r0 + 2 * r1) / 3; pal[10] = (g0 + 2 * g1) / 3; pal[11] = (b0 + 2 * b1) / 3;

            // Along the line from c1 to c0: a pixel's place on it picks the palette entry (1, 3, 2, 0 from c1 on); the entry's distance is the error.
            int dx = r0 - r1, dy = g0 - g1, dz = b0 - b1;
            int len = dx * dx + dy * dy + dz * dz;
            uint indices = 0;
            for (int k = 0; k < 16; k++)
            {
                int t = (R[k] - r1) * dx + (G[k] - g1) * dy + (B[k] - b1) * dz;
                // t / len in thirds: below 1/6 the end c1, below 1/2 a third, below 5/6 two thirds, else c0.
                int six = 6 * t;
                int e = six < len ? 1 : six < 3 * len ? 3 : six < 5 * len ? 2 : 0;
                int q = e * 3;
                int er = R[k] - pal[q], eg = G[k] - pal[q + 1], eb = B[k] - pal[q + 2];
                error += er * er + eg * eg + eb * eb;
                indices |= (uint)e << (k * 2);
            }

            return indices;
        }

        /// <summary>The two endpoints that fit the pixels best for the indices (index 0 the first, 1 the second), by least squares over the weights 1, 0, 2/3, 1/3 of index 0 to 3.</summary>
        private static bool Refine(Scratch s, uint indices, out int ra, out int rb)
        {
            ra = rb = 0;
            int[] R = s.R, G = s.G, B = s.B;
            float aa = 0f, bb = 0f, ab = 0f, ar = 0f, ag = 0f, abl = 0f, br = 0f, bg = 0f, bbl = 0f;
            for (int k = 0; k < 16; k++)
            {
                int idx = (int)((indices >> (k * 2)) & 3);
                float wa = idx == 0 ? 1f : idx == 1 ? 0f : idx == 2 ? 2f / 3f : 1f / 3f, wb = 1f - wa;
                aa += wa * wa;
                bb += wb * wb;
                ab += wa * wb;
                ar += wa * R[k]; ag += wa * G[k]; abl += wa * B[k];
                br += wb * R[k]; bg += wb * G[k]; bbl += wb * B[k];
            }

            float det = aa * bb - ab * ab;
            if (Math.Abs(det) < 1e-6f)
                return false;
            float inv = 1f / det;
            // Indices orders the two again and finds the indices anew, so which comes out larger does not matter.
            ra = Quantize(Clamp((ar * bb - br * ab) * inv), Clamp((ag * bb - bg * ab) * inv), Clamp((abl * bb - bbl * ab) * inv));
            rb = Quantize(Clamp((br * aa - ar * ab) * inv), Clamp((bg * aa - ag * ab) * inv), Clamp((bbl * aa - abl * ab) * inv));
            return true;
        }

        private static int Clamp(float v) => v <= 0f ? 0 : v >= 255f ? 255 : (int)(v + 0.5f);

        // ------------------------------------------------------------------ reading back (the tests, and the check of the first pictures in the game)

        /// <summary>One level of BC1 (at offset) as RGBA, the rows in the order of the blocks (the bottom row first for a texture's data); alpha 255, or 0 where a three-colour block says transparent.</summary>
        public static byte[] DecodeBc1(byte[] data, int offset, int w, int h)
        {
            var rgba = new byte[w * h * 4];
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var pal = new int[16];
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int at = offset + (by * bw + bx) * 8;
                    int c0 = data[at] | (data[at + 1] << 8), c1 = data[at + 2] | (data[at + 3] << 8);
                    uint idx = (uint)(data[at + 4] | (data[at + 5] << 8) | (data[at + 6] << 16) | (data[at + 7] << 24));
                    int r0 = Expand5((c0 >> 11) & 31), g0 = Expand6((c0 >> 5) & 63), b0 = Expand5(c0 & 31);
                    int r1 = Expand5((c1 >> 11) & 31), g1 = Expand6((c1 >> 5) & 63), b1 = Expand5(c1 & 31);
                    pal[0] = r0; pal[1] = g0; pal[2] = b0; pal[3] = 255;
                    pal[4] = r1; pal[5] = g1; pal[6] = b1; pal[7] = 255;
                    if (c0 > c1)
                    {
                        pal[8] = (2 * r0 + r1) / 3; pal[9] = (2 * g0 + g1) / 3; pal[10] = (2 * b0 + b1) / 3; pal[11] = 255;
                        pal[12] = (r0 + 2 * r1) / 3; pal[13] = (g0 + 2 * g1) / 3; pal[14] = (b0 + 2 * b1) / 3; pal[15] = 255;
                    }
                    else
                    {
                        pal[8] = (r0 + r1) / 2; pal[9] = (g0 + g1) / 2; pal[10] = (b0 + b1) / 2; pal[11] = 255;
                        pal[12] = 0; pal[13] = 0; pal[14] = 0; pal[15] = 0;
                    }

                    for (int j = 0; j < 4; j++)
                    {
                        int y = by * 4 + j;
                        if (y >= h)
                            break;
                        for (int i = 0; i < 4; i++)
                        {
                            int x = bx * 4 + i;
                            if (x >= w)
                                break;
                            int e = (int)((idx >> ((j * 4 + i) * 2)) & 3) * 4, p = (y * w + x) * 4;
                            rgba[p] = (byte)pal[e];
                            rgba[p + 1] = (byte)pal[e + 1];
                            rgba[p + 2] = (byte)pal[e + 2];
                            rgba[p + 3] = (byte)pal[e + 3];
                        }
                    }
                }
            }

            return rgba;
        }
    }
}
