using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using CDLC.TS.Geo;

namespace CDLC.TS.Render
{
    /// <summary>
    /// The two families a face is painted with (0.12.28, SignFonts): the Chinese characters' and the letters and figures', each with the style it is drawn in. A font file is drawn as it is (Regular:
    /// GDI+ says Bold is there for any face and makes it by thickening the strokes, which would change the traffic sign font's own); an installed family falls back on its Bold.
    /// </summary>
    internal sealed class PaintFonts
    {
        public FontFamily Cjk, Latin;
        public FontStyle CjkStyle, LatinStyle;

        /// <summary>The family a run of text is drawn in: the Chinese one when it has a Chinese character (the letters' font may have none), else the letters'.</summary>
        public FontFamily For(string text, out FontStyle style)
        {
            foreach (char c in text ?? "")
            {
                if (c >= 0x2E80)
                {
                    style = CjkStyle;
                    return Cjk;
                }
            }

            style = LatinStyle;
            return Latin;
        }
    }

    /// <summary>
    /// Paints the picture of a sign face with GDI+ (System.Drawing, which the game ships) and returns it as raw RGBA bytes, the first row at the bottom, which is the order of a Unity texture. The text
    /// is set in the fonts SignFonts names (since 0.12.28 the traffic sign font the mod carries, or the player's), loaded privately from their files so that they do not depend on what the player has
    /// installed; when a file is not there or fails, an installed Chinese font is used and the report says so. No Unity types here: the same file is compiled into a small preview program that
    /// writes PNGs.
    /// </summary>
    public static class LegendPainter
    {
        private static readonly string[] kInstalled = { "Microsoft YaHei", "Microsoft YaHei UI", "DengXian", "SimHei", "Noto Sans SC", "Noto Sans CJK SC", "PingFang SC" };
        private static readonly object s_Lock = new object();
        private static PrivateFontCollection s_CjkFiles, s_LatinFiles;
        private static PaintFonts s_Fonts;
        private static int s_FontsVersion = -1;

        /// <summary>Where the fonts of the last paint came from: the files, an installed family, or the generic fallback.</summary>
        public static string FontSource { get; private set; } = "not used yet";

        private static readonly Color kRed = Color.FromArgb(214, 28, 36);
        private static readonly Color kGreen = Color.FromArgb(0, 118, 84);
        private static readonly Color kWhite = Color.FromArgb(250, 250, 250);
        private static readonly Color kBlack = Color.FromArgb(18, 18, 18);

        /// <summary>The family of a font file, or null when it cannot be read (the reason goes into why).</summary>
        private static FontFamily FromFile(string file, ref PrivateFontCollection collection, ref string why)
        {
            if (file == null)
                return null;
            try
            {
                if (!File.Exists(file))
                {
                    why += (why.Length > 0 ? "; " : "") + Path.GetFileName(file) + " is not there";
                    return null;
                }

                collection?.Dispose();
                collection = new PrivateFontCollection();
                collection.AddFontFile(file);
                if (collection.Families.Length > 0)
                    return collection.Families[0];
                why += (why.Length > 0 ? "; " : "") + Path.GetFileName(file) + " has no family";
            }
            catch (Exception ex)
            {
                why += (why.Length > 0 ? "; " : "") + Path.GetFileName(file) + " failed (" + ex.GetType().Name + ": " + ex.Message + ")";
            }

            return null;
        }

        private static FontFamily Installed(out string name)
        {
            foreach (string candidate in kInstalled)
            {
                try
                {
                    name = candidate;
                    return new FontFamily(candidate);
                }
                catch (ArgumentException)
                {
                }
            }

            name = "generic sans serif";
            return FontFamily.GenericSansSerif;
        }

        /// <summary>
        /// The style a family is painted in: the wanted one when the family has it, else the first it has. A file is wanted Regular (GDI+ thickens a face asked for as Bold once more: 0.12.27 did
        /// that to Noto Sans SC Bold), an installed family Bold; a file whose only face is named Bold has no Regular and is painted as its Bold.
        /// </summary>
        private static FontStyle StyleOf(FontFamily family, FontStyle wanted)
        {
            foreach (FontStyle style in new[] { wanted, FontStyle.Regular, FontStyle.Bold, FontStyle.Italic })
            {
                if (family.IsStyleAvailable(style))
                    return style;
            }

            return wanted;
        }

        /// <summary>The fonts SignFonts names, read again when it names others (its Version). Under s_Lock.</summary>
        private static PaintFonts Current()
        {
            if (s_Fonts != null && s_FontsVersion == SignFonts.Version)
                return s_Fonts;
            string why = "";
            var fonts = new PaintFonts();
            fonts.Cjk = FromFile(SignFonts.Cjk, ref s_CjkFiles, ref why);
            string cjkSource;
            if (fonts.Cjk != null)
            {
                fonts.CjkStyle = StyleOf(fonts.Cjk, FontStyle.Regular);
                cjkSource = "file " + Path.GetFileName(SignFonts.Cjk) + " as '" + fonts.Cjk.Name + "'";
            }
            else
            {
                fonts.Cjk = Installed(out string name);
                fonts.CjkStyle = StyleOf(fonts.Cjk, FontStyle.Bold);
                cjkSource = "installed '" + name + "'";
            }

            fonts.Latin = FromFile(SignFonts.Latin, ref s_LatinFiles, ref why);
            string latinSource;
            if (fonts.Latin != null)
            {
                fonts.LatinStyle = StyleOf(fonts.Latin, FontStyle.Regular);
                latinSource = "file " + Path.GetFileName(SignFonts.Latin) + " as '" + fonts.Latin.Name + "'";
            }
            else
            {
                fonts.Latin = fonts.Cjk;
                fonts.LatinStyle = fonts.CjkStyle;
                latinSource = "the Chinese font's";
            }

            FontSource = $"Chinese {cjkSource}, letters and figures {latinSource}" + (why.Length > 0 ? " (" + why + ")" : "");
            s_Fonts = fonts;
            s_FontsVersion = SignFonts.Version;
            return fonts;
        }

        /// <summary>The fonts the signs are painted in; for tools that draw outside a sign.</summary>
        internal static PaintFonts Fonts()
        {
            lock (s_Lock)
                return Current();
        }

        /// <summary>Paints the face of a sign and returns RGBA32 bytes, the bottom row first.</summary>
        public static byte[] Paint(SignDef def, out int width, out int height)
        {
            width = def.TextureWidth;
            height = def.TextureHeight;
            lock (s_Lock)
            {
                PaintFonts fonts = Current();
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    switch (def.Id)
                    {
                        case "gb.speed60": PaintSpeed(g, fonts, width, height, "60"); break;
                        case "gb.stop": PaintStop(g, fonts, width, height); break;
                        default: g.Clear(Color.FromArgb(255, 0, 255)); break;
                    }

                    return ToRaw(bitmap);
                }
            }
        }

        /// <summary>Paints the picture of a sign as it is drawn: a classic sign by its painter, a guide board from its laid out parts. RGBA32, the bottom row first.</summary>
        public static byte[] Paint(SignInstance instance, out int width, out int height)
        {
            if (instance.Board == null)
                return Paint(instance.Def, out width, out height);

            width = instance.TexW;
            height = instance.TexH;
            lock (s_Lock)
            {
                PaintFonts fonts = Current();
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    GuidePainter.Draw(g, fonts, instance, width, height);
                    return ToRaw(bitmap);
                }
            }
        }

        /// <summary>
        /// The picture of a board with its cut parts made clear (0.12.30, GuidePart.Cut: a player's picture with clear pixels), for the editor, where the board is seen as it will be. The bytes are the
        /// painter's (RGBA32, the bottom row first) and are changed in place; a board with no cut part is left as it is.
        /// </summary>
        public static void CutAway(SignInstance instance, byte[] raw, int width, int height)
        {
            if (instance.Board == null || !instance.Board.Parts.Exists(p => p.Cut != null))
                return;
            float sx = width / (instance.BoundsX1 - instance.BoundsX0), sy = height / (instance.BoundsY1 - instance.BoundsY0);
            using (var mask = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.Transparent);
                    foreach (GuidePart part in instance.Board.Parts)
                    {
                        float mx = part.X + part.W * 0.5f - instance.BoundsX0, my = part.Y + part.H * 0.5f - instance.BoundsY0;
                        using (var path = new GraphicsPath(FillMode.Alternate))
                        {
                            if (part.Cut == null)
                            {
                                path.AddRectangle(new RectangleF((part.X - instance.BoundsX0) * sx, (part.Y - instance.BoundsY0) * sy, part.W * sx, part.H * sy));
                            }
                            else
                            {
                                foreach (List<Point2> loop in part.Cut)
                                {
                                    var points = new PointF[loop.Count];
                                    for (int i = 0; i < loop.Count; i++)
                                        points[i] = new PointF((mx + loop[i].X) * sx, (my - loop[i].Y) * sy);
                                    if (points.Length >= 3)
                                        path.AddPolygon(points);
                                }
                            }

                            g.FillPath(Brushes.White, path);
                        }
                    }
                }

                BitmapData data = mask.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[width];
                    for (int y = 0; y < height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, width);
                        int target = (height - 1 - y) * width * 4;
                        for (int x = 0; x < width; x++)
                            raw[target + x * 4 + 3] = (byte)((uint)row[x] >> 24 >= 128 ? 255 : 0);
                    }
                }
                finally
                {
                    mask.UnlockBits(data);
                }
            }
        }

        private static void PaintSpeed(Graphics g, PaintFonts fonts, int w, int h, string number)
        {
            // 禁39: a white disc inside a red ring of a tenth of the diameter; the number is black.
            g.Clear(kRed);
            float ring = w * 0.10f;
            using (var white = new SolidBrush(kWhite))
                g.FillEllipse(white, ring, ring, w - 2 * ring, h - 2 * ring);
            Text(g, fonts, number, w * 0.46f, kBlack, w * 0.5f, h * 0.5f);
        }

        private static void PaintStop(Graphics g, PaintFonts fonts, int w, int h)
        {
            // 禁1: red face, a white edge line inside the octagon, white 停 above and STOP below.
            g.Clear(kRed);
            float r = w * 0.5f / (float)Math.Cos(Math.PI / 8) * 0.92f;
            var corners = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double a = Math.PI / 8 + Math.PI / 4 * i;
                corners[i] = new PointF(w * 0.5f + (float)Math.Cos(a) * r, h * 0.5f - (float)Math.Sin(a) * r);
            }

            using (var pen = new Pen(kWhite, w * 0.028f) { LineJoin = LineJoin.Round })
                g.DrawPolygon(pen, corners);
            Text(g, fonts, "停", w * 0.50f, kWhite, w * 0.5f, h * 0.42f);
            Text(g, fonts, "STOP", w * 0.15f, kWhite, w * 0.5f, h * 0.76f);
        }

        private static void Text(Graphics g, PaintFonts fonts, string text, float pixelSize, Color colour, float cx, float cy)
        {
            FontFamily family = fonts.For(text, out FontStyle style);
            using (var font = new Font(family, pixelSize, style, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(colour))
            using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                format.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.NoWrap;
                g.DrawString(text, font, brush, new PointF(cx, cy), format);
            }
        }

        private static byte[] ToRaw(Bitmap bitmap)
        {
            int w = bitmap.Width, h = bitmap.Height;
            var pixels = new int[w * h];
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                // Bitmap rows run top to bottom; a Unity texture starts at the bottom.
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, (h - 1 - y) * w, w);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            // A pixel as an int is 0xAARRGGBB (GDI+ memory order B, G, R, A); the texture wants R, G, B, A in memory, 0xAABBGGRR, opaque. A pixel at a time as one int: Mono checks every index of an
            // array, and four bytes a pixel were most of the time of the copy (0.12.27).
            for (int i = 0; i < pixels.Length; i++)
            {
                int v = pixels[i];
                pixels[i] = (v & 0x0000FF00) | ((v >> 16) & 0xFF) | ((v & 0xFF) << 16) | unchecked((int)0xFF000000);
            }

            var raw = new byte[w * h * 4];
            Buffer.BlockCopy(pixels, 0, raw, 0, raw.Length);
            return raw;
        }
    }
}
