using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Tests;
using static CDLC.TS.Tests.ShapeFixture;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// The picture cut as it is wired (BENCH_PICTURECUT=1; 0.12.74, DESIGN-wiring-plan.md step D): for pictures of known shape, what the import costs (the contour traced from the graded alpha and written as TSM2), what a board costs
    /// (PictureCut.Make with the default budget of 6,000 triangles) against the cell cut of 0.12.72 (the vertices and triangles it builds, and what each of the two covers that the other does not), and what the shadow's comparison costs.
    /// Milliseconds are the best of five on .NET Framework 4.8 or the runtime of the project, not the game's Mono.
    /// </summary>
    internal static class PictureCutBench
    {
        public static void Run()
        {
            Console.WriteLine("import: contour traced from the picture's graded alpha (ms), TSM2 size, loops, points");
            Console.WriteLine("board:  PictureCut.Make on a board of 1 m, the default budget 6,000; cost = triangles as built (face + back + edge)");
            Console.WriteLine("cell cut: the 0.12.72 cut (192-cell mask, 160 cells on the board); lost = share of the contour's area the cell cut does not cover, over = share of the cell cut's own area that is not on the contour");
            Console.WriteLine();
            Console.WriteLine("picture            px           import ms  KB   loops points | board: loops  v   tri  tol   ms  reason | cell cut: loops  v   tri   ms | lost%   over% | shadow ms");
            foreach ((string name, Shape shape, int w, int h) in Pictures())
            {
                byte[] alpha = Alpha(shape, w, h);
                double importMs = Best(5, () => PictureContour.FromAlpha(alpha, w, h));
                PictureContour contour = PictureContour.FromAlpha(alpha, w, h);
                byte[] file = contour.Encode();
                float ph = (float)h / w;
                var part = new GuidePart { Shape = GuidePartShape.RoundRect, W = 1f, H = ph, Radius = 0f };
                var image = new GuideElement { Kind = GuideElementKind.Image, X = 0f, Y = 0f, W = 1f, H = ph, Text = "00000000000000ab" };
                PictureCutResult made = null;
                double boardMs = Best(5, () => made = PictureCut.Make(contour, GuideKit.PartOutline(part), 1f, ph, 0f, 0f, 1f, ph, false, ph));
                CustomMask mask = CustomMask.FromAlpha(Rgba(Whole(alpha)), w, h);
                List<List<Point2>> cell = null;
                double cellMs = Best(5, () => cell = CustomSigns.CellCut(part, image, mask, ph));
                PictureCutShadow.Counts(cell, out int cellV, out int cellT);
                double lost = 0.0, over = 0.0, compareMs = 0.0;
                if (made.Loops != null)
                {
                    PictureCut.Compare(cell ?? new List<List<Point2>>(), made.Loops, 400, out double ao, out double an, out double oo, out double no);
                    lost = an > 0.0 ? no / an * 100.0 : 0.0;
                    over = ao > 0.0 ? oo / ao * 100.0 : 0.0;
                    var job = new PictureCutShadow.Job { Key = "bench", Contour = contour, Outline = GuideKit.PartOutline(part), PartW = 1f, PartH = ph, ImageW = 1f, ImageH = ph, PictureAspect = ph, Old = cell, OldMs = cellMs };
                    compareMs = Best(3, () => PictureCutShadow.Compare(job));
                }

                Console.WriteLine($"{name,-18} {w,4}x{h,-4} {importMs,9:0.0} {file.Length / 1024.0,5:0.0} {contour.Loops.Count,6} {contour.Points,6} | {(made.Loops == null ? "-" : made.Loops.Count.ToString()),5} {made.Vertices,5} {made.Triangles,6} {made.Tolerance,4:0.00} {boardMs,6:0.0}  {(made.Reason.Length > 0 ? made.Reason : "-")} | "
                    + $"{(cell == null ? 0 : cell.Count),5} {cellV,5} {cellT,6} {cellMs,6:0.0} | {lost,6:0.00} {over,6:0.00} | {compareMs,7:0.0}");
            }

            Console.WriteLine();
            Console.WriteLine("a cropped fill (cover) of a 1024 x 640 text picture on a square board, and a round board: clipping to the box and the outline");
            PictureContour textContour = PictureContour.FromAlpha(Alpha(Text(), 1024, 640), 1024, 640);
            PictureCutResult cover = null, round = null;
            double coverMs = Best(5, () => cover = PictureCut.Make(textContour, GuideKit.PartOutline(new GuidePart { W = 1f, H = 1f }), 1f, 1f, 0f, 0f, 1f, 1f, true, 0.625f));
            var circle = new GuidePart { Shape = GuidePartShape.Circle, W = 1f, H = 1f, Radius = 0.5f };
            double roundMs = Best(5, () => round = PictureCut.Make(textContour, GuideKit.PartOutline(circle), 1f, 1f, 0f, 0f, 1f, 1f, true, 0.625f));
            Console.WriteLine($"cover on a square: {cover.Loops?.Count} loops, {cover.Vertices} v, {cover.Triangles} tri at {cover.Tolerance:0.00} px, {coverMs:0.0} ms {cover.Reason}");
            Console.WriteLine($"cover on a circle: {round.Loops?.Count} loops, {round.Vertices} v, {round.Triangles} tri at {round.Tolerance:0.00} px, {roundMs:0.0} ms {round.Reason}");
        }

        private static double Best(int runs, Action work)
        {
            double best = double.MaxValue;
            for (int i = -1; i < runs; i++)
            {
                long start = Stopwatch.GetTimestamp();
                work();
                if (i >= 0)
                    best = Math.Min(best, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            }

            return best;
        }

        private static IEnumerable<(string, Shape, int, int)> Pictures()
        {
            foreach ((int w, int h) in new[] { (1024, 640), (256, 256) })
            {
                double m = Math.Min(w, h);
                yield return ("rectangle", Rectangle(0.1 * w + 0.3, 0.15 * h + 0.3, 0.9 * w - 0.2, 0.85 * h - 0.4), w, h);
                yield return ("circle", Disc(w / 2.0, h / 2.0, 0.4 * m), w, h);
                yield return ("star", Star(w / 2.0, h / 2.0 + 0.02 * h, 0.45 * m, 0.18 * m, 5), w, h);
                yield return ("ring (3 px)", Ring(w / 2.0, h / 2.0, 0.4 * m, 3), w, h);
                double lx = Math.Round(0.2 * w), ly0 = Math.Round(0.15 * h), ly1 = Math.Round(0.85 * h);
                yield return ("L (2 px)", Union(Rectangle(lx, ly0, lx + 2, ly1), Rectangle(lx, ly1 - 2, Math.Round(0.75 * w), ly1)), w, h);
            }

            yield return ("text", Text(), 1024, 640);
            yield return ("blob (photo)", Blob(1024, 640), 1024, 640);
        }

        private static Shape Text()
        {
            var parts = new List<Shape>();
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 12; col++)
                {
                    double x = 30 + col * 80.3, y = 30 + row * 100.4;
                    switch ((row + col) % 3)
                    {
                        case 0:
                            parts.Add(Union(Rectangle(x, y, x + 10, y + 56), Rectangle(x, y, x + 40, y + 10), Rectangle(x, y + 23, x + 32, y + 33), Rectangle(x, y + 46, x + 40, y + 56)));
                            break;
                        case 1:
                            parts.Add(Ring(x + 22, y + 28, 24, 7));
                            break;
                        default:
                            parts.Add(Union(Rectangle(x, y + 20, x + 44, y + 36), Rectangle(x + 14, y, x + 22, y + 56)));
                            break;
                    }
                }
            }

            return Union(parts.ToArray());
        }

        /// <summary>A silhouette with a rough edge and a hole: a disc whose radius wanders with the angle.</summary>
        private static Shape Blob(int w, int h)
        {
            var rng = new Random(7);
            var phase = new double[6];
            var amp = new double[6];
            for (int i = 0; i < 6; i++)
            {
                phase[i] = rng.NextDouble() * 6.28;
                amp[i] = 0.02 + 0.03 * rng.NextDouble();
            }

            double radius = 0.4 * Math.Min(w, h);
            return (x, y) =>
            {
                double dx = x - w / 2.0, dy = y - h / 2.0, a = Math.Atan2(dy, dx), r = Math.Sqrt(dx * dx + dy * dy);
                double edge = radius;
                for (int i = 0; i < 6; i++)
                    edge += radius * amp[i] * Math.Sin(a * (10 + i * 3 + 1) + phase[i]);
                return r <= edge && r > radius * 0.25;
            };
        }
    }
}
