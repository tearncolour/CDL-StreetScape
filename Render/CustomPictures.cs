using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Render
{
    /// <summary>
    /// The pictures of the player's own signs (0.12.29, CustomSigns) as the painter draws them: read from the folder they are kept in (Folder: a PNG named by its key) when a face first needs one, and
    /// the most recent kept for the faces that follow. A picture that is not there is drawn as nothing (the board's ground shows) and said once in the log. Also makes the kept copy of a picture that
    /// is imported. No Unity types: the preview program uses it too.
    /// </summary>
    internal static class CustomPictures
    {
        private const int kKept = 24;

        private static readonly object s_Lock = new object();
        private static readonly Dictionary<string, Bitmap> s_Kept = new Dictionary<string, Bitmap>();
        private static readonly LinkedList<string> s_Order = new LinkedList<string>();
        private static readonly HashSet<string> s_Missing = new HashSet<string>();

        /// <summary>What the kept pictures take in memory (0.12.104, the panel's figures): four bytes a pixel of each of the (at most 24) pictures held.</summary>
        internal static long KeptBytes()
        {
            lock (s_Lock)
            {
                long bytes = 0;
                foreach (Bitmap kept in s_Kept.Values)
                    bytes += (long)kept.Width * kept.Height * 4L;
                return bytes;
            }
        }

        /// <summary>The folder of the kept pictures (set by the mod, CustomStore, and the preview tools); null: no picture is drawn.</summary>
        internal static string Folder;

        /// <summary>Said in the log when a picture could not be read (the mod's log; the preview tools leave it null).</summary>
        internal static Action<string> Warn;

        /// <summary>Said in the log when a contour was traced at an import (the mod's log; the preview tools leave it null).</summary>
        internal static Action<string> Info;

        /// <summary>The contour the last Keep or Upgrade traced and its key, for the store to keep in memory (the picture's own antialiased edge is better than the one traced from the kept copy later); null when none was.</summary>
        internal static PictureContour LastContour;

        internal static string LastContourKey;

        internal static string PathOf(string key) => Folder != null && CustomSigns.IsKey(key) ? System.IO.Path.Combine(Folder, key + ".png") : null;

        /// <summary>The file of a picture's mask (0.12.30, CustomMask): empty for a picture with no clear pixel.</summary>
        internal static string MaskPathOf(string key) => Folder != null && CustomSigns.IsKey(key) ? System.IO.Path.Combine(Folder, key + ".mask") : null;

        /// <summary>The file of a picture's contour (0.12.74, PictureContour: TSM2): beside the mask, which stays for the mod of 0.12.72 and before; absent for a picture with no clear pixel.</summary>
        internal static string ContourPathOf(string key) => Folder != null && CustomSigns.IsKey(key) ? System.IO.Path.Combine(Folder, key + ".mask2") : null;

        /// <summary>
        /// The alpha plane of a kept picture (rows from the top) and its size: the contour of a picture that has no TSM2 is traced from it. Null when the picture cannot be read. What the import stores has its
        /// alpha whole or nothing, so this is the pixels' boundary and not the antialiased edge the import saw.
        /// </summary>
        internal static (byte[] Alpha, int Width, int Height) ReadAlpha(string key)
        {
            string path = PathOf(key);
            if (path == null)
                return (null, 0, 0);
            using (Bitmap picture = Read(path, out _))
            {
                if (picture == null)
                    return (null, 0, 0);
                int w = picture.Width, h = picture.Height;
                BitmapData data = picture.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var argb = new int[w * h];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, argb, 0, argb.Length);
                    var alpha = new byte[w * h];
                    for (int i = 0; i < argb.Length; i++)
                        alpha[i] = (byte)((uint)argb[i] >> 24);
                    return (alpha, w, h);
                }
                finally
                {
                    picture.UnlockBits(data);
                }
            }
        }

        /// <summary>
        /// Draws the picture of an Image element into its box (pixels): stretched to it, or for a cropped fill the middle of the picture in the box's proportions. An element that is Opaque is drawn with
        /// the picture's alpha left out (its board is cut where the picture is clear).
        /// </summary>
        internal static void Draw(Graphics g, GuideElement e, RectangleF box)
        {
            if (box.Width <= 0f || box.Height <= 0f)
                return;
            lock (s_Lock)
            {
                Bitmap picture = Get(e.Text);
                if (picture == null)
                    return;
                CustomSigns.SourceOf((float)picture.Height / picture.Width, box.Height / box.Width, e.Crop, out float u0, out float v0, out float u1, out float v1);
                var source = new RectangleF(u0 * picture.Width, v0 * picture.Height, (u1 - u0) * picture.Width, (v1 - v0) * picture.Height);

                InterpolationMode before = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // The edge pixels repeat outwards: without it the bicubic filter draws a faint border round the picture.
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    if (e.Opaque)
                        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0f, Matrix43 = 1f });
                    var corners = new[] { new PointF(box.Left, box.Top), new PointF(box.Right, box.Top), new PointF(box.Left, box.Bottom) };
                    g.DrawImage(picture, corners, source, GraphicsUnit.Pixel, attributes);
                }

                g.InterpolationMode = before;
            }
        }

        private static Bitmap Get(string key)
        {
            if (s_Kept.TryGetValue(key ?? "", out Bitmap kept))
            {
                s_Order.Remove(key);
                s_Order.AddFirst(key);
                return kept;
            }

            string path = PathOf(key);
            if (path == null || s_Missing.Contains(key))
                return null;
            Bitmap read = Read(path, out string why);
            if (read == null)
            {
                s_Missing.Add(key);
                Warn?.Invoke($"Custom signs: the picture {key} is not drawn: {why}");
                return null;
            }

            s_Kept[key] = read;
            s_Order.AddFirst(key);
            while (s_Order.Count > kKept)
            {
                string last = s_Order.Last.Value;
                s_Order.RemoveLast();
                s_Kept[last].Dispose();
                s_Kept.Remove(last);
            }

            return read;
        }

        /// <summary>A picture read whole into memory (a bitmap made from a file keeps the file open), as 32-bit ARGB; null with the reason when it cannot be.</summary>
        private static Bitmap Read(string path, out string why)
        {
            why = "";
            try
            {
                if (!File.Exists(path))
                {
                    why = "the file is not there";
                    return null;
                }

                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (Image image = Image.FromStream(stream, false, true))
                {
                    var copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(copy))
                        g.DrawImage(image, 0, 0, image.Width, image.Height);
                    return copy;
                }
            }
            catch (Exception ex)
            {
                why = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// The kept copy of a PNG file the player imports: decoded (any PNG GDI+ reads), made smaller when its longer side is over CustomSigns.MaxPicture, its alpha made whole or nothing (0.12.30: a pixel
        /// that is not wholly opaque is clear, the owner's "不允许半透明，半透明一律纯透明处理") with the colours of the clear pixels taken from their opaque neighbours (Bleed), and written as the key's
        /// file, with its mask beside it (empty when no pixel is clear). The width and height are the copy's. False with the reason when the bytes are not a picture.
        /// </summary>
        internal static bool Keep(byte[] data, string key, out int width, out int height, out string why)
        {
            width = height = 0;
            why = "";
            string path = PathOf(key);
            if (path == null)
            {
                why = "there is no folder for the pictures";
                return false;
            }

            try
            {
                using (var stream = new MemoryStream(data))
                using (Image image = Image.FromStream(stream, false, true))
                {
                    float scale = Math.Min(1f, (float)CustomSigns.MaxPicture / Math.Max(image.Width, image.Height));
                    width = Math.Max(1, (int)Math.Round(image.Width * scale));
                    height = Math.Max(1, (int)Math.Round(image.Height * scale));
                    using (var copy = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(copy))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(image, 0, 0, width, height);
                        }

                        Write(copy, key);
                    }
                }

                lock (s_Lock)
                {
                    s_Missing.Remove(key);
                    if (s_Kept.TryGetValue(key, out Bitmap old))
                    {
                        old.Dispose();
                        s_Kept.Remove(key);
                        s_Order.Remove(key);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                why = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// A picture kept by 0.12.29 (before the alpha was made whole or nothing, and with no mask): done again from the kept copy, as an import does it now. False with the reason when it cannot be.
        /// </summary>
        internal static bool Upgrade(string key, out string why)
        {
            why = "";
            try
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(PathOf(key))))
                using (Image image = Image.FromStream(stream, false, true))
                using (var copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(copy))
                        g.DrawImage(image, 0, 0, image.Width, image.Height);
                    Write(copy, key);
                }

                lock (s_Lock)
                {
                    if (s_Kept.TryGetValue(key, out Bitmap old))
                    {
                        old.Dispose();
                        s_Kept.Remove(key);
                        s_Order.Remove(key);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                why = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>The copy's alpha made whole or nothing and its clear pixels coloured from their neighbours, written as the key's picture, and its mask beside it (each a new file put in place).</summary>
        private static void Write(Bitmap copy, string key)
        {
            int w = copy.Width, h = copy.Height;
            var argb = new int[w * h];
            byte[] alpha = null;
            BitmapData data = copy.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, argb, 0, argb.Length);
                // The alpha as the picture has it, graded along an antialiased edge: Bleed makes it whole or nothing, and the contour is traced from this.
                for (int i = 0; i < argb.Length && alpha == null; i++)
                {
                    if ((uint)argb[i] >> 24 < CustomMask.OpaqueAlpha)
                    {
                        alpha = new byte[argb.Length];
                        for (int j = 0; j < argb.Length; j++)
                            alpha[j] = (byte)((uint)argb[j] >> 24);
                    }
                }

                Bleed(argb, w, h);
                System.Runtime.InteropServices.Marshal.Copy(argb, 0, data.Scan0, argb.Length);
            }
            finally
            {
                copy.UnlockBits(data);
            }

            var rgba = new byte[w * h * 4];
            for (int i = 0; i < argb.Length; i++)
                rgba[i * 4 + 3] = (byte)((uint)argb[i] >> 24);
            CustomMask mask = CustomMask.FromAlpha(rgba, w, h);
            Replace(PathOf(key), temporary => copy.Save(temporary, ImageFormat.Png));
            Replace(MaskPathOf(key), temporary => File.WriteAllBytes(temporary, mask != null ? mask.Encode() : new byte[0]));
            WriteContour(key, alpha, w, h);
        }

        /// <summary>The contour of a picture that has clear pixels beside its mask (TSM2; none for an opaque picture, and none while features.pictureCut is off). A failure leaves the job to the tracer at the next read.</summary>
        private static void WriteContour(string key, byte[] alpha, int w, int h)
        {
            string path = ContourPathOf(key);
            LastContour = null;
            LastContourKey = null;
            try
            {
                if (alpha == null || CustomSigns.PictureCutMode == FeatureState.Off)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    return;
                }

                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                PictureContour contour = PictureContour.FromAlpha(alpha, w, h);
                byte[] bytes = contour.Encode();
                LastContour = contour;
                LastContourKey = key;
                Replace(path, temporary => File.WriteAllBytes(temporary, bytes));
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Info?.Invoke($"Custom signs: the contour of {key} ({w} x {h} px): {contour.Loops.Count} loop(s), {contour.Points} points, {bytes.Length / 1024.0:0.0} KB, traced in {ms:0} ms{(contour.Graded ? "" : " (the alpha had no graded value: the pixels' boundary)")}.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Warn?.Invoke($"Custom signs: the contour of {key} could not be written: {ex.Message}");
            }
        }

        private static void Replace(string path, Action<string> write)
        {
            string temporary = path + ".tmp";
            write(temporary);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temporary, path);
        }

        /// <summary>How many pixels the colours of the opaque pixels reach into the clear ones (the rest take the opaque pixels' mean): enough for the mips of a face seen from afar.</summary>
        private const int kBleedPasses = 16;

        /// <summary>
        /// Every pixel wholly opaque or wholly clear (ARGB, alpha 255 or 0; opaque from CustomMask.OpaqueAlpha), and the clear ones coloured: those next to colour take the mean of their coloured neighbours, a ring at a time, the rest the
        /// mean of the opaque pixels. Drawn with its alpha left out (GuideElement.Opaque), the picture then has no dark edge where the board is cut.
        /// </summary>
        private static void Bleed(int[] argb, int w, int h)
        {
            var coloured = new bool[argb.Length];
            long sr = 0, sg = 0, sb = 0, n = 0;
            for (int i = 0; i < argb.Length; i++)
            {
                if ((uint)argb[i] >> 24 < CustomMask.OpaqueAlpha)
                    continue;
                coloured[i] = true;
                sr += (argb[i] >> 16) & 255;
                sg += (argb[i] >> 8) & 255;
                sb += argb[i] & 255;
                n++;
            }

            var opaque = (bool[])coloured.Clone();
            var ring = new List<int>();
            for (int pass = 0; pass < kBleedPasses; pass++)
            {
                ring.Clear();
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (coloured[i])
                            continue;
                        int r = 0, g = 0, b = 0, k = 0;
                        foreach (int j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
                        {
                            if (j < 0 || !coloured[j])
                                continue;
                            r += (argb[j] >> 16) & 255;
                            g += (argb[j] >> 8) & 255;
                            b += argb[j] & 255;
                            k++;
                        }

                        if (k == 0)
                            continue;
                        argb[i] = (r / k) << 16 | (g / k) << 8 | (b / k);
                        ring.Add(i);
                    }
                }

                if (ring.Count == 0)
                    break;
                foreach (int i in ring)
                    coloured[i] = true;
            }

            int mean = n > 0 ? (int)(sr / n) << 16 | (int)(sg / n) << 8 | (int)(sb / n) : 0;
            for (int i = 0; i < argb.Length; i++)
            {
                if (opaque[i])
                    argb[i] |= unchecked((int)0xFF000000);
                else
                    argb[i] = (coloured[i] ? argb[i] : mean) & 0x00FFFFFF;
            }
        }

        /// <summary>Lets the kept pictures go (the mod is unloaded).</summary>
        internal static void Clear()
        {
            lock (s_Lock)
            {
                foreach (Bitmap picture in s_Kept.Values)
                    picture.Dispose();
                s_Kept.Clear();
                s_Order.Clear();
                s_Missing.Clear();
            }
        }
    }
}
