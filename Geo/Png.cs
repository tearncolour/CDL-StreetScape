using System;
using System.IO;
using System.IO.Compression;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A minimal PNG writer for the thumbnails: 8-bit RGBA, no interlace, filter 0 on every row, one IDAT. The game's UI loads the file. Pure (System.IO.Compression
    /// only), so it is checked offline against a decoder written for the test. Pictures here are bottom row first (a Unity texture's order); PNG wants the top row first.
    /// </summary>
    public static class Png
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static uint[] s_Crc;

        public static byte[] Encode(byte[] rgba, int width, int height, bool bottomFirst = true)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("the picture has no size");
            if (rgba == null || rgba.Length != width * height * 4)
                throw new ArgumentException($"{(rgba == null ? 0 : rgba.Length)} bytes for a {width}x{height} picture");

            // The rows with their filter bytes.
            int stride = width * 4;
            var raw = new byte[(stride + 1) * height];
            for (int row = 0; row < height; row++)
            {
                int source = bottomFirst ? height - 1 - row : row;
                raw[row * (stride + 1)] = 0;
                Buffer.BlockCopy(rgba, source * stride, raw, row * (stride + 1) + 1, stride);
            }

            byte[] compressed;
            using (var memory = new MemoryStream())
            {
                memory.WriteByte(0x78);
                memory.WriteByte(0x9C);
                using (var deflate = new DeflateStream(memory, CompressionLevel.Optimal, true))
                    deflate.Write(raw, 0, raw.Length);
                uint adler = Adler32(raw);
                memory.WriteByte((byte)(adler >> 24));
                memory.WriteByte((byte)(adler >> 16));
                memory.WriteByte((byte)(adler >> 8));
                memory.WriteByte((byte)adler);
                compressed = memory.ToArray();
            }

            using (var png = new MemoryStream())
            {
                png.Write(Signature, 0, Signature.Length);
                var header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 8;      // bit depth
                header[9] = 6;      // colour type: RGBA
                header[10] = 0;     // compression
                header[11] = 0;     // filter
                header[12] = 0;     // no interlace
                Chunk(png, "IHDR", header);
                Chunk(png, "IDAT", compressed);
                Chunk(png, "IEND", new byte[0]);
                return png.ToArray();
            }
        }

        /// <summary>The size of the picture a PNG file starts with (its signature and IHDR, the first 24 bytes), read without decoding it; false when the bytes are not that.</summary>
        public static bool TrySize(byte[] head, out int width, out int height)
        {
            width = height = 0;
            if (head == null || head.Length < 24)
                return false;
            for (int i = 0; i < 8; i++)
            {
                if (head[i] != Signature[i])
                    return false;
            }

            if (head[12] != 'I' || head[13] != 'H' || head[14] != 'D' || head[15] != 'R')
                return false;
            uint w = ReadBigEndian(head, 16), h = ReadBigEndian(head, 20);
            if (w == 0 || h == 0 || w > int.MaxValue || h > int.MaxValue)
                return false;
            width = (int)w;
            height = (int)h;
            return true;
        }

        /// <summary>Decodes a PNG made by Encode (8-bit RGBA, filter 0 only); for the tests. Returns false for anything else or a damaged file.</summary>
        public static bool TryDecode(byte[] png, out byte[] rgba, out int width, out int height, bool bottomFirst = true)
        {
            rgba = null;
            width = height = 0;
            try
            {
                if (png == null || png.Length < 8 + 12 * 3)
                    return false;
                for (int i = 0; i < 8; i++)
                {
                    if (png[i] != Signature[i])
                        return false;
                }

                int position = 8;
                var idat = new MemoryStream();
                bool sawEnd = false;
                while (position + 12 <= png.Length && !sawEnd)
                {
                    int length = (int)ReadBigEndian(png, position);
                    string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
                    if (position + 12 + length > png.Length)
                        return false;
                    uint stored = ReadBigEndian(png, position + 8 + length);
                    var body = new byte[length];
                    Buffer.BlockCopy(png, position + 8, body, 0, length);
                    if (Crc(type, body) != stored)
                        return false;

                    if (type == "IHDR")
                    {
                        width = (int)ReadBigEndian(body, 0);
                        height = (int)ReadBigEndian(body, 4);
                        if (body[8] != 8 || body[9] != 6 || body[10] != 0 || body[11] != 0 || body[12] != 0)
                            return false;
                    }
                    else if (type == "IDAT")
                    {
                        idat.Write(body, 0, body.Length);
                    }
                    else if (type == "IEND")
                    {
                        sawEnd = true;
                    }

                    position += 12 + length;
                }

                if (!sawEnd || width <= 0 || height <= 0)
                    return false;

                byte[] z = idat.ToArray();
                if (z.Length < 6 || z[0] != 0x78)
                    return false;

                var inflated = new MemoryStream();
                using (var source = new MemoryStream(z, 2, z.Length - 6))
                using (var deflate = new DeflateStream(source, CompressionMode.Decompress))
                    deflate.CopyTo(inflated);
                byte[] raw = inflated.ToArray();
                if (ReadBigEndian(z, z.Length - 4) != Adler32(raw))
                    return false;

                int stride = width * 4;
                if (raw.Length != (stride + 1) * height)
                    return false;
                rgba = new byte[stride * height];
                for (int row = 0; row < height; row++)
                {
                    if (raw[row * (stride + 1)] != 0)
                        return false;
                    int target = bottomFirst ? height - 1 - row : row;
                    Buffer.BlockCopy(raw, row * (stride + 1) + 1, rgba, target * stride, stride);
                }

                return true;
            }
            catch (Exception)
            {
                rgba = null;
                return false;
            }
        }

        private static void Chunk(Stream stream, string type, byte[] body)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)body.Length);
            stream.Write(length, 0, 4);
            byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
            stream.Write(name, 0, 4);
            stream.Write(body, 0, body.Length);
            var crc = new byte[4];
            WriteBigEndian(crc, 0, Crc(type, body));
            stream.Write(crc, 0, 4);
        }

        private static uint Crc(string type, byte[] body)
        {
            if (s_Crc == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }

                s_Crc = table;
            }

            uint crc = 0xFFFFFFFFu;
            foreach (char ch in type)
                crc = s_Crc[(crc ^ (byte)ch) & 0xFF] ^ (crc >> 8);
            foreach (byte b in body)
                crc = s_Crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte value in data)
            {
                a = (a + value) % 65521u;
                b = (b + a) % 65521u;
            }

            return (b << 16) | a;
        }

        private static void WriteBigEndian(byte[] target, int at, uint value)
        {
            target[at] = (byte)(value >> 24);
            target[at + 1] = (byte)(value >> 16);
            target[at + 2] = (byte)(value >> 8);
            target[at + 3] = (byte)value;
        }

        private static uint ReadBigEndian(byte[] source, int at) =>
            ((uint)source[at] << 24) | ((uint)source[at + 1] << 16) | ((uint)source[at + 2] << 8) | source[at + 3];
    }
}
