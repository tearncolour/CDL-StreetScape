using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using static CDLC.TS.Tests.ShapeFixture;

namespace CDLC.TS.Tests
{
    /// <summary>The contour file of a kept picture (PictureContour, TSM2, 0.12.74): the frame of the loops, the file's round trip, what a reader refuses, where a contour comes from when the file is not there, and the coarser tolerance.</summary>
    internal static class PictureContourTests
    {
        public static void RunAll()
        {
            Program.Run("picture contour: the loops are in the picture's own frame, y up, counter-clockwise", Frame);
            Program.Run("picture contour: the file round trips bit for bit and ignores what follows the last loop", RoundTrip);
            Program.Run("picture contour: a reader refuses what it does not know and what is damaged", Refuses);
            Program.Run("picture contour: a missing, TSM1-only, unknown or damaged file is traced from the kept picture", Sources);
            Program.Run("picture contour: a coarser tolerance has fewer points and stays within it", Relaxing);
        }

        internal static string TempFolder()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "tmp", "ts-contour-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static void Delete(string dir)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(dir, true);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        /// <summary>A file written in the temporary folder; a scanner that has the new file open for a moment is waited for (the preferences tests met it too).</summary>
        private static void WriteFile(string path, byte[] bytes) => Retry(() => File.WriteAllBytes(path, bytes));

        private static void DeleteFile(string path) => Retry(() =>
        {
            if (File.Exists(path))
                File.Delete(path);
        });

        private static void Retry(Action work)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    work();
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 40)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        private static byte[] Plane(int w, int h, Shape shape) => Alpha(shape, w, h);

        private static void Frame()
        {
            // A rectangle of 20 by 12 pixels at (4, 3) of a 32 by 24 picture: its corners in the frame 0 to 1 each way, y up (the picture's top is y = 1).
            byte[] box = Plane(32, 24, Rectangle(4, 3, 24, 15));
            PictureContour c = PictureContour.FromAlpha(box, 32, 24);
            Program.Check(c.Width == 32 && c.Height == 24 && !c.Graded, "the size is the picture's and the alpha was whole or nothing");
            Program.Check(c.Loops.Count == 1 && c.Loops[0].Count == 4, "one loop of four corners: " + c.Loops.Count);
            float area = CutOutline.Area(c.Loops[0]);
            Program.Near(area, 20f * 12f / (32f * 24f), 1e-5f, "its area is 20 x 12 of 32 x 24 pixels and its orientation counter-clockwise: " + area);
            float minX = 9f, maxX = -9f, minY = 9f, maxY = -9f;
            foreach (Point2 p in c.Loops[0])
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            Program.Near(minX, 4f / 32f, 1e-6f, "left edge at 4 pixels");
            Program.Near(maxX, 24f / 32f, 1e-6f, "right edge at 24 pixels");
            Program.Near(maxY, 1f - 3f / 24f, 1e-6f, "top edge: 3 pixels from the top, y up");
            Program.Near(minY, 1f - 15f / 24f, 1e-6f, "bottom edge: 15 pixels from the top, y up");

            // An antialiased shape is graded, and a hole is a clockwise loop inside the outer one.
            byte[] ring = Plane(64, 64, Ring(32, 32, 24, 6));
            PictureContour r = PictureContour.FromAlpha(ring, 64, 64);
            Program.Check(r.Graded, "an antialiased ring has graded alpha");
            int outers = 0, holes = 0;
            foreach (List<Point2> loop in r.Loops)
            {
                if (CutOutline.Area(loop) > 0f)
                    outers++;
                else
                    holes++;
            }

            Program.Check(outers == 1 && holes == 1, "a ring is an outer loop and a hole: " + outers + "/" + holes);
            PictureContour empty = PictureContour.FromAlpha(new byte[64], 8, 8);
            Program.Check(empty.Loops.Count == 0 && PictureContour.FromAlpha(null, 8, 8).Loops.Count == 0, "no opaque pixel or no plane: no loop");
        }

        private static bool Same(PictureContour a, PictureContour b)
        {
            if (a.Width != b.Width || a.Height != b.Height || a.Graded != b.Graded || a.Tolerance != b.Tolerance || a.Loops.Count != b.Loops.Count)
                return false;
            for (int i = 0; i < a.Loops.Count; i++)
            {
                if (a.Loops[i].Count != b.Loops[i].Count)
                    return false;
                for (int j = 0; j < a.Loops[i].Count; j++)
                {
                    if (a.Loops[i][j].X != b.Loops[i][j].X || a.Loops[i][j].Y != b.Loops[i][j].Y)
                        return false;
                }
            }

            return true;
        }

        private static PictureContour Sample() => PictureContour.FromAlpha(Plane(96, 64, Union(Ring(30, 32, 20, 5), Rectangle(60, 10, 90, 20), Star(70, 40, 14, 6, 5))), 96, 64);

        private static void RoundTrip()
        {
            PictureContour c = Sample();
            Program.Check(c.Loops.Count >= 3 && c.Graded, "the sample has several loops and graded alpha: " + c.Loops.Count);
            byte[] bytes = c.Encode();
            Program.Check(bytes.Length == 18 + c.Loops.Count * 4 + c.Points * 8 && bytes[0] == 'T' && bytes[1] == 'S' && bytes[2] == 'M' && bytes[3] == '2' && bytes[4] == PictureContour.FormatVersion, "the size and the magic: " + bytes.Length);
            PictureContour back = PictureContour.Decode(bytes, out string why);
            Program.Check(back != null && Same(c, back), "decoded it is the same bit for bit: " + why);
            var padded = new byte[bytes.Length + 40];
            Array.Copy(bytes, padded, bytes.Length);
            for (int i = bytes.Length; i < padded.Length; i++)
                padded[i] = (byte)(i * 7);
            PictureContour tail = PictureContour.Decode(padded, out why);
            Program.Check(tail != null && Same(c, tail), "bytes after the last loop are ignored (a later version may add to the file): " + why);
            PictureContour none = PictureContour.Decode(new PictureContour { Width = 5, Height = 7 }.Encode(), out why);
            Program.Check(none != null && none.Loops.Count == 0 && none.Width == 5 && none.Height == 7, "a picture with no loop is a file too: " + why);
            var coarse = new PictureContour { Width = 40, Height = 30, Tolerance = 1.25f, Graded = true };
            coarse.Loops.Add(new List<Point2> { new Point2(0.1f, 0.1f), new Point2(0.9f, 0.1f), new Point2(0.5f, 0.9f) });
            PictureContour t = PictureContour.Decode(coarse.Encode(), out why);
            Program.Check(t != null && t.Tolerance == 1.25f && t.Graded && Same(coarse, t), "the tolerance and the graded flag are kept");
        }

        private static byte[] Mutated(byte[] bytes, int at, byte value)
        {
            var copy = (byte[])bytes.Clone();
            copy[at] = value;
            return copy;
        }

        private static void Refuses()
        {
            byte[] good = Sample().Encode();
            string why;
            Program.Check(PictureContour.Decode(null, out why) == null && why == "empty", "no bytes: " + why);
            Program.Check(PictureContour.Decode(new byte[0], out why) == null && why == "empty", "an empty file (what an opaque picture's mask is): " + why);
            Program.Check(PictureContour.Decode(new CustomMask { Width = 8, Height = 8, Cells = new bool[64] }.Encode(), out why) == null && why == "not a TSM2 file", "a TSM1 mask is not a contour: " + why);
            var twoPoints = new PictureContour { Width = 16, Height = 16 };
            twoPoints.Loops.Add(new List<Point2> { new Point2(0.1f, 0.1f), new Point2(0.9f, 0.9f) });
            Program.Check(PictureContour.Decode(twoPoints.Encode(), out why) == null && why == "a loop's size does not fit the file", "a file whose only loop has two points: " + why);
            Program.Check(PictureContour.Decode(Mutated(good, 4, 2), out why) == null && why.Contains("version"), "a version this mod does not know: " + why);
            Program.Check(PictureContour.Decode(Mutated(good, 4, 0), out why) == null, "version 0 is not known either");
            var shortHeader = new byte[10];
            Array.Copy(good, shortHeader, 10);
            Program.Check(PictureContour.Decode(shortHeader, out why) == null, "a header cut short");
            Program.Check(PictureContour.Decode(Mutated(Mutated(good, 6, 0), 7, 0), out why) == null && why == "the header is not valid", "a picture of no width: " + why);
            var truncated = new byte[good.Length - 5];
            Array.Copy(good, truncated, truncated.Length);
            Program.Check(PictureContour.Decode(truncated, out why) == null, "a file that ends inside a loop: " + why);
            var many = (byte[])good.Clone();
            many[17] = 0x7F;
            Program.Check(PictureContour.Decode(many, out why) == null, "a count of loops that cannot be: " + why);
            var negative = (byte[])good.Clone();
            negative[17] = 0xFF;
            Program.Check(PictureContour.Decode(negative, out why) == null, "a negative count of loops: " + why);
            var hugeLoop = (byte[])good.Clone();
            hugeLoop[18 + 3] = 0x40;
            Program.Check(PictureContour.Decode(hugeLoop, out why) == null && why == "a loop's size does not fit the file", "a loop that claims more points than the file has: " + why);
            var tiny = (byte[])good.Clone();
            tiny[18] = 2;
            tiny[19] = tiny[20] = tiny[21] = 0;
            Program.Check(PictureContour.Decode(tiny, out why) == null, "a loop of two points is no loop: " + why);
            var nan = (byte[])good.Clone();
            byte[] quiet = BitConverter.GetBytes(float.NaN);
            Array.Copy(quiet, 0, nan, 22, 4);
            Program.Check(PictureContour.Decode(nan, out why) == null && why == "a point is not in the picture", "a point that is not a number: " + why);
            var outside = (byte[])good.Clone();
            Array.Copy(BitConverter.GetBytes(5f), 0, outside, 22, 4);
            Program.Check(PictureContour.Decode(outside, out why) == null, "a point far outside the picture: " + why);
            var badTolerance = (byte[])good.Clone();
            Array.Copy(BitConverter.GetBytes(-1f), 0, badTolerance, 10, 4);
            Program.Check(PictureContour.Decode(badTolerance, out why) == null, "a negative tolerance: " + why);
            Program.Check(PictureContour.Decode(good, out why) != null, "and the good file is read: " + why);
        }

        private static void Sources()
        {
            string dir = TempFolder();
            try
            {
                byte[] plane = Plane(64, 48, Union(Rectangle(8, 8, 40, 30), Disc(50, 20, 8)));
                int asked = 0;
                Func<(byte[], int, int)> provider = () =>
                {
                    asked++;
                    return (plane, 64, 48);
                };
                PictureContour traced = PictureContour.FromAlpha(plane, 64, 48);
                string path = Path.Combine(dir, "a.mask2");

                PictureContour missing = PictureContour.Load(path, provider, out string source);
                Program.Check(missing != null && asked == 1 && source.StartsWith("traced") && Same(traced, missing), "no file: traced from the kept picture: " + source);

                // A TSM1-only picture (what 0.12.72 and before keep): its .mask is another file; the contour's path has nothing.
                WriteFile(Path.Combine(dir, "a.mask"), new CustomMask { Width = 4, Height = 4, Cells = new bool[16] }.Encode());
                asked = 0;
                Program.Check(PictureContour.Load(path, provider, out source) != null && asked == 1 && source.StartsWith("traced"), "a TSM1-only picture is traced: " + source);

                // A file of the contour: read as it is, the picture not looked at.
                PictureContour stored = PictureContour.FromAlpha(Plane(64, 48, Rectangle(5, 5, 20, 20)), 64, 48);
                WriteFile(path, stored.Encode());
                asked = 0;
                PictureContour read = PictureContour.Load(path, provider, out source);
                Program.Check(read != null && asked == 0 && source == "TSM2 file" && Same(stored, read), "a TSM2 file is used and the picture is not read: " + source);

                // A TSM1 mask where a contour should be, a file of a later version, a damaged one: not used, said why, and the picture is traced.
                foreach ((string name, byte[] bytes) in new[]
                {
                    ("a TSM1 mask", new CustomMask { Width = 4, Height = 4, Cells = new bool[16] }.Encode()),
                    ("an unknown version", Mutated(stored.Encode(), 4, 9)),
                    ("a damaged file", new byte[] { (byte)'T', (byte)'S', (byte)'M', (byte)'2', 1, 0, 0, 64, 0 }),
                    ("an empty file", new byte[0])
                })
                {
                    WriteFile(path, bytes);
                    asked = 0;
                    PictureContour fallback = PictureContour.Load(path, provider, out source);
                    Program.Check(fallback != null && asked == 1 && source.StartsWith("traced") && Same(traced, fallback), name + " is traced from the kept picture: " + source);
                }

                // Neither: none, said why.
                DeleteFile(path);
                Program.Check(PictureContour.Load(path, () => (null, 0, 0), out source) == null && source.StartsWith("none"), "no file and no picture: none: " + source);
                Program.Check(PictureContour.Load(null, null, out source) == null && source.StartsWith("none"), "no path and no provider: none: " + source);
            }
            finally
            {
                Delete(dir);
            }
        }

        private static void Relaxing()
        {
            PictureContour c = Sample();
            Program.Check(ReferenceEquals(c.Relaxed(c.Tolerance), c.Loops) && ReferenceEquals(c.Relaxed(0.1f), c.Loops), "not coarser than the loops: the loops themselves");
            int before = c.Points;
            int previous = before;
            foreach (float tolerance in new[] { 0.75f, 1f, 2f })
            {
                List<List<Point2>> coarse = c.Relaxed(tolerance);
                int points = 0;
                foreach (List<Point2> loop in coarse)
                    points += loop.Count;
                Program.Check(points <= previous && points < before, $"tolerance {tolerance}: {points} points of {before}");
                previous = points;
                // Every point of the finer loops is within the tolerance of the coarse outline (in pixels).
                float worst = 0f;
                foreach (List<Point2> fine in c.Loops)
                {
                    foreach (Point2 p in fine)
                    {
                        float best = float.MaxValue;
                        foreach (List<Point2> loop in coarse)
                        {
                            for (int i = 0; i < loop.Count; i++)
                                best = Math.Min(best, Distance(p, loop[i], loop[(i + 1) % loop.Count], c.Width, c.Height));
                        }

                        worst = Math.Max(worst, best);
                    }
                }

                Program.Check(worst <= tolerance + 0.05f, $"tolerance {tolerance}: no point is farther than {worst:0.000} px from the coarse outline");
            }

            Program.Check(c.Relaxed(1f).Count <= c.Loops.Count, "no loop is made by relaxing");
        }

        /// <summary>The distance in pixels from a point to a segment, the points in the picture's frame.</summary>
        private static float Distance(Point2 p, Point2 a, Point2 b, int w, int h)
        {
            double px = p.X * w, py = p.Y * h, ax = a.X * w, ay = a.Y * h, bx = b.X * w, by = b.Y * h;
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            double t = l2 > 1e-12 ? Math.Max(0.0, Math.Min(1.0, ((px - ax) * dx + (py - ay) * dy) / l2)) : 0.0;
            double qx = ax + dx * t - px, qy = ay + dy * t - py;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }
    }
}
