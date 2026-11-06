using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The outline of the opaque pixels of a kept picture, at the picture's own resolution (0.12.74; DESIGN-wiring-plan.md step D): the loops ContourTrace makes, in the picture's own frame (x from 0 at
    /// the left to 1 at the right, y from 0 at the bottom to 1 at the top, as the loops of the cut are, y up), outer loops counter-clockwise and holes clockwise. Kept beside the picture as "TSM2" (the
    /// mask of 0.12.30 stays as "TSM1", so a mod that does not know this file reads the picture as before). Pure; the files are read and written by the mod.
    /// </summary>
    public sealed class PictureContour
    {
        public const int FormatVersion = 1;

        /// <summary>Header: "TSM2", the version, flags, the picture's width and height (pixels, two bytes each), the tolerance it was traced at (a float, pixels), the number of loops; then each loop: its number of points and the points as two floats each.</summary>
        private const int kHeader = 4 + 1 + 1 + 2 + 2 + 4 + 4;

        private const int kMaxLoops = 200000, kMaxPoints = 4000000;

        public int Width, Height;

        /// <summary>The alpha had graded values (an antialiased edge) when it was traced: the loops are on the sub-pixel crossing of the opaque threshold, not on the pixels' boundary.</summary>
        public bool Graded;

        /// <summary>The tolerance (pixels) the loops were dropped to.</summary>
        public float Tolerance = ContourTrace.DefaultTolerance;

        public List<List<Point2>> Loops = new List<List<Point2>>();

        public int Points
        {
            get
            {
                int n = 0;
                foreach (List<Point2> loop in Loops)
                    n += loop.Count;
                return n;
            }
        }

        /// <summary>
        /// The contour of a picture's alpha plane (rows from the top): the loops where the alpha crosses CustomMask.OpaqueAlpha, the rule the mask and the painter use (a pixel that is not wholly opaque is
        /// clear). Traced in pixels, so that the tolerance is the same along and across, then put in the picture's frame.
        /// </summary>
        public static PictureContour FromAlpha(byte[] alpha, int width, int height)
        {
            var contour = new PictureContour { Width = width, Height = height };
            if (alpha == null || width < 1 || height < 1 || alpha.Length < width * height)
                return contour;
            for (int i = 0; i < width * height && !contour.Graded; i++)
                contour.Graded = alpha[i] != 0 && alpha[i] != 255;
            List<List<Point2>> loops = ContourTrace.Trace(alpha, width, height, false, CustomMask.OpaqueAlpha, ContourTrace.DefaultTolerance, ContourTrace.DefaultMinArea, 0f, height, 1f, 1f);
            contour.Loops = Normalised(loops, width, height);
            return contour;
        }

        /// <summary>Loops in pixels (y up, 0 to the picture's height) put in the picture's frame, 0 to 1 each way.</summary>
        private static List<List<Point2>> Normalised(List<List<Point2>> pixels, int width, int height)
        {
            var result = new List<List<Point2>>(pixels.Count);
            foreach (List<Point2> loop in pixels)
            {
                var points = new List<Point2>(loop.Count);
                foreach (Point2 p in loop)
                    points.Add(new Point2(p.X / width, p.Y / height));
                result.Add(points);
            }

            return result;
        }

        /// <summary>The loops dropped to a coarser tolerance (pixels), as new lists; the loops themselves when it is not coarser than they are.</summary>
        public List<List<Point2>> Relaxed(float tolerance)
        {
            if (tolerance <= Tolerance + 1e-6f)
                return Loops;
            var pixels = new List<List<Point2>>(Loops.Count);
            foreach (List<Point2> loop in Loops)
            {
                var points = new List<Point2>(loop.Count);
                foreach (Point2 p in loop)
                    points.Add(new Point2(p.X * Width, p.Y * Height));
                pixels.Add(points);
            }

            return Normalised(ContourTrace.Relax(pixels, tolerance), Width, Height);
        }

        public byte[] Encode()
        {
            int points = Points;
            var data = new byte[kHeader + Loops.Count * 4 + points * 8];
            data[0] = (byte)'T';
            data[1] = (byte)'S';
            data[2] = (byte)'M';
            data[3] = (byte)'2';
            data[4] = FormatVersion;
            data[5] = (byte)(Graded ? 1 : 0);
            data[6] = (byte)(Width >> 8);
            data[7] = (byte)Width;
            data[8] = (byte)(Height >> 8);
            data[9] = (byte)Height;
            Put(data, 10, BitConverter.GetBytes(Tolerance));
            Put(data, 14, BitConverter.GetBytes(Loops.Count));
            int at = kHeader;
            foreach (List<Point2> loop in Loops)
            {
                Put(data, at, BitConverter.GetBytes(loop.Count));
                at += 4;
                foreach (Point2 p in loop)
                {
                    Put(data, at, BitConverter.GetBytes(p.X));
                    Put(data, at + 4, BitConverter.GetBytes(p.Y));
                    at += 8;
                }
            }

            return data;
        }

        private static void Put(byte[] data, int at, byte[] bytes)
        {
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, data, at, bytes.Length);
        }

        private static float Float(byte[] data, int at)
        {
            var bytes = new byte[4];
            Buffer.BlockCopy(data, at, bytes, 0, 4);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        private static int Int(byte[] data, int at)
        {
            var bytes = new byte[4];
            Buffer.BlockCopy(data, at, bytes, 0, 4);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        /// <summary>
        /// The contour of a file's bytes; null with the reason when they are not one this mod reads: another format (a TSM1 mask, an empty file), a version it does not know, a size that does not match what
        /// the header says, a point that is not a number or lies far outside the picture. Bytes after the last loop are ignored (a later version may add to the file).
        /// </summary>
        public static PictureContour Decode(byte[] data, out string why)
        {
            why = "";
            if (data == null || data.Length < kHeader || data[0] != 'T' || data[1] != 'S' || data[2] != 'M' || data[3] != '2')
            {
                why = data == null || data.Length == 0 ? "empty" : "not a TSM2 file";
                return null;
            }

            if (data[4] != FormatVersion)
            {
                why = "version " + data[4] + " is not known";
                return null;
            }

            int w = data[6] << 8 | data[7], h = data[8] << 8 | data[9];
            float tolerance = Float(data, 10);
            int count = Int(data, 14);
            if (w < 1 || h < 1 || count < 0 || count > kMaxLoops || float.IsNaN(tolerance) || tolerance < 0f || tolerance > 64f)
            {
                why = "the header is not valid";
                return null;
            }

            var contour = new PictureContour { Width = w, Height = h, Graded = (data[5] & 1) != 0, Tolerance = tolerance };
            int at = kHeader, total = 0;
            for (int k = 0; k < count; k++)
            {
                if (at + 4 > data.Length)
                {
                    why = "it ends inside a loop";
                    return null;
                }

                int n = Int(data, at);
                at += 4;
                total += Math.Max(0, n);
                if (n < 3 || total > kMaxPoints || (long)at + (long)n * 8 > data.Length)
                {
                    why = "a loop's size does not fit the file";
                    return null;
                }

                var loop = new List<Point2>(n);
                for (int i = 0; i < n; i++, at += 8)
                {
                    float x = Float(data, at), y = Float(data, at + 4);
                    if (float.IsNaN(x) || float.IsNaN(y) || x < -0.01f || x > 1.01f || y < -0.01f || y > 1.01f)
                    {
                        why = "a point is not in the picture";
                        return null;
                    }

                    loop.Add(new Point2(x, y));
                }

                contour.Loops.Add(loop);
            }

            return contour;
        }

        /// <summary>
        /// The contour of a picture, from the best source there is: its TSM2 file when it holds one this mod reads, else traced from the kept picture's alpha (alpha: null when the picture cannot be read).
        /// source says which, with the reason a file was not used; null when neither works. No thread rule here: the caller decides where it runs.
        /// </summary>
        public static PictureContour Load(string path, Func<(byte[] Alpha, int Width, int Height)> alpha, out string source)
        {
            source = "";
            string why = "no file";
            try
            {
                if (path != null && File.Exists(path))
                {
                    PictureContour contour = Decode(File.ReadAllBytes(path), out why);
                    if (contour != null)
                    {
                        source = "TSM2 file";
                        return contour;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                why = ex.Message;
            }

            (byte[] plane, int w, int h) = alpha != null ? alpha() : (null, 0, 0);
            if (plane == null || w < 1 || h < 1)
            {
                source = "none (" + why + ")";
                return null;
            }

            source = "traced from the kept picture (" + why + ")";
            return FromAlpha(plane, w, h);
        }
    }
}
