using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;
using static CDLC.TS.Tests.ShapeFixture;

namespace CDLC.TS.Tests
{
    /// <summary>The board of a picture cut from its contour (PictureCut, the cut in CustomSigns, the shadow and its counters; 0.12.74): where the contour lands on the board, the cuts to the board's box and outline, the budget, the mesh the loops make, and the three modes.</summary>
    internal static class PictureCutTests
    {
        public static void RunAll()
        {
            Program.Run("picture cut: a rectangle picture is its rectangle, at its place on the board", Rectangles);
            Program.Run("picture cut: a cropped fill shows the middle of the picture, cut to the board", Cropped);
            Program.Run("picture cut: a round board cuts the picture to its outline", RoundBoard);
            Program.Run("picture cut: a 2 px stroke survives where the cell cut loses it, and the edge is within a pixel", Strokes);
            Program.Run("picture cut: the budget drops the tolerance from 0.5 toward 1 px and refuses a board that stays over", Budget);
            Program.Run("picture cut: the board's mesh (face, back, edge) is closed and its picture coordinates stay in range", Meshes);
            Program.Run("picture cut: the area two sets of loops differ by", Comparing);
            Program.Run("picture cut: Off, Shadow and On cut the board as each should", Modes);
            Program.Run("picture cut: the counters stop at the limit and keep the worst", Counters);
            Program.Run("picture cut: the preference is shadow unless the file says otherwise", Preference);
        }

        private static void Preference()
        {
            Program.Check(new Preferences().PictureCut == FeatureState.Shadow, "no file: shadow");
            foreach (FeatureState state in new[] { FeatureState.Off, FeatureState.Shadow, FeatureState.Log, FeatureState.On })
                Program.Check(Preferences.Parse("{\"features\":{\"pictureCut\":\"" + Preferences.StateName(state) + "\"}}", out _).PictureCut == state, "read as " + state);
            Program.Check(Preferences.Parse("{\"features\":{\"pictureCut\":\"maybe\"}}", out _).PictureCut == FeatureState.Shadow && Preferences.Parse("{\"features\":{\"pictureCut\":7}}", out _).PictureCut == FeatureState.Shadow, "a state that is not one: shadow");
            Program.Check(new Preferences().ToText().Contains("\"pictureCut\": \"shadow\""), "the file made with the defaults lists it");
        }

        private const string Key = "00000000000000ab";

        private static GuidePart Part(float w, float h, float radius = 0f) => new GuidePart { Shape = GuidePartShape.RoundRect, W = w, H = h, Radius = radius };

        private static GuideElement Box(float w, float h, bool crop = false, float x = 0f, float y = 0f) => new GuideElement { Kind = GuideElementKind.Image, X = x, Y = y, W = w, H = h, Text = Key, Crop = crop };

        private static PictureContour Contour(Shape shape, int w, int h) => PictureContour.FromAlpha(Alpha(shape, w, h), w, h);

        // The text-like picture takes seconds to sample: once for all the tests.
        private static byte[] s_TextAlpha;
        private static PictureContour s_TextContour;

        private static byte[] TextAlpha() => s_TextAlpha ?? (s_TextAlpha = Alpha(TextShape(1024, 640), 1024, 640));

        private static PictureContour TextContour() => s_TextContour ?? (s_TextContour = PictureContour.FromAlpha(TextAlpha(), 1024, 640));

        private static float Area(List<List<Point2>> loops)
        {
            float sum = 0f;
            foreach (List<Point2> loop in loops)
                sum += CutOutline.Area(loop);
            return sum;
        }

        private static void Bounds(List<List<Point2>> loops, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue;
            x1 = y1 = float.MinValue;
            foreach (List<Point2> loop in loops)
            {
                foreach (Point2 p in loop)
                {
                    x0 = Math.Min(x0, p.X);
                    x1 = Math.Max(x1, p.X);
                    y0 = Math.Min(y0, p.Y);
                    y1 = Math.Max(y1, p.Y);
                }
            }
        }

        private static PictureCutResult Make(PictureContour c, GuidePart part, GuideElement image, float aspect, int budget = PictureCut.TriangleBudget) =>
            PictureCut.Make(c, GuideKit.PartOutline(part), part.W, part.H, image.X, image.Y, image.W, image.H, image.Crop, aspect, budget);

        private static void Rectangles()
        {
            // A 64 x 48 picture, opaque from (8, 6) to (56, 42): on a board of 1 m by 0.75 m that shows it whole.
            PictureContour c = Contour(Rectangle(8, 6, 56, 42), 64, 48);
            GuidePart part = Part(1f, 0.75f);
            PictureCutResult r = Make(c, part, Box(1f, 0.75f), 0.75f);
            Program.Check(r.Loops != null && r.Loops.Count == 1 && r.Loops[0].Count == 4 && r.Reason == "", "one loop of four corners: " + (r.Loops == null ? r.Reason : r.Loops.Count.ToString()));
            Program.Near(Area(r.Loops), 0.75f * (48f * 36f) / (64f * 48f), 1e-5f, "the area is 48 x 36 of 64 x 48 of the board");
            Program.Check(r.Vertices == 4 && r.Triangles == 2 * 2 + 2 * 4 && r.Tolerance == 0.5f, $"four vertices, twelve triangles (the face, the back, the edge), at 0.5 px: {r.Vertices} / {r.Triangles} / {r.Tolerance}");
            Bounds(r.Loops, out float x0, out float y0, out float x1, out float y1);
            Program.Near(x0, -0.5f + 8f / 64f, 1e-5f, "left edge");
            Program.Near(x1, -0.5f + 56f / 64f, 1e-5f, "right edge");
            Program.Near(y1, 0.375f - 6f / 48f * 0.75f, 1e-5f, "top edge, y up from the middle");
            Program.Near(y0, 0.375f - 42f / 48f * 0.75f, 1e-5f, "bottom edge");

            // The picture on a part of its own box (margins): a quarter size, in the lower right corner of the part.
            PictureCutResult small = Make(c, part, Box(0.5f, 0.375f, false, 0.5f, 0.375f), 0.75f);
            Bounds(small.Loops, out x0, out y0, out x1, out y1);
            Program.Check(small.Loops != null && x0 >= 0f - 1e-5f && x1 <= 0.5f + 1e-5f && y0 >= -0.375f - 1e-5f && y1 <= 0f + 1e-5f, "a picture in a box of the part's lower right: the loop is in that box");
            Program.Near(Area(small.Loops), Area(r.Loops) * 0.25f, 1e-5f, "a quarter of the area");

            // No contour, no outline, an empty picture, a picture with no opaque pixel on the board.
            Program.Check(PictureCut.Make(null, GuideKit.PartOutline(part), 1f, 0.75f, 0f, 0f, 1f, 0.75f, false, 0.75f).Loops == null, "no contour: no loops, and the reason");
            PictureCutResult none = Make(new PictureContour { Width = 8, Height = 8 }, part, Box(1f, 0.75f), 0.75f);
            Program.Check(none.Loops != null && none.Loops.Count == 0, "a contour of no loop: an empty board, as the cell cut finds no opaque cell");
        }

        private static void Cropped()
        {
            // 100 x 50 picture, opaque from (10, 5) to (90, 45); on a square board filled by cropping it shows u 0.25 to 0.75 and all its height.
            PictureContour c = Contour(Rectangle(10, 5, 90, 45), 100, 50);
            GuidePart part = Part(1f, 1f);
            PictureCutResult r = Make(c, part, Box(1f, 1f, true), 0.5f);
            Program.Check(r.Loops != null && r.Loops.Count == 1 && r.Loops[0].Count == 4, "the cropped picture's loop is cut to the board: " + (r.Loops == null ? r.Reason : r.Loops[0].Count.ToString()));
            Bounds(r.Loops, out float x0, out float y0, out float x1, out float y1);
            Program.Check(Math.Abs(x0 + 0.5f) < 1e-5f && Math.Abs(x1 - 0.5f) < 1e-5f, "it spans the board across");
            Program.Near(y1, 0.5f - 0.1f, 1e-5f, "its top is 5 of 50 pixels from the board's top");
            Program.Near(y0, -0.5f + 0.1f, 1e-5f, "and its bottom 5 pixels from the bottom");
            // The same board filled whole (not cropped) is the picture squashed to it: the rectangle of 0.8 by 0.8 of the board.
            PictureCutResult whole = Make(c, part, Box(1f, 1f), 0.5f);
            Program.Near(Area(whole.Loops), 0.8f * 0.8f, 1e-4f, "stretched, the picture's rectangle is 80 % by 80 %");
            // A picture larger than its board (a box of 2 m over a 1 m part): cut to the part's box too.
            PictureCutResult over = Make(Contour(Rectangle(0, 0, 100, 50), 100, 50), part, Box(2f, 1f, false, -0.5f, 0f), 0.5f);
            Bounds(over.Loops, out x0, out y0, out x1, out y1);
            Program.Check(over.Loops != null && x0 >= -0.5f - 1e-5f && x1 <= 0.5f + 1e-5f && y0 >= -0.5f - 1e-5f && y1 <= 0.5f + 1e-5f, "beyond the part's outline there is no board");
            // A cropped picture in a box smaller than the part (size 50 %): what is cropped away does not appear round the box.
            PictureContour full = Contour(Rectangle(0, 0, 100, 50), 100, 50);
            PictureCutResult boxed = Make(full, part, Box(0.5f, 0.5f, true, 0.25f, 0.25f), 0.5f);
            Bounds(boxed.Loops, out x0, out y0, out x1, out y1);
            Program.Check(boxed.Loops != null && Math.Abs(x0 + 0.25f) < 1e-5f && Math.Abs(x1 - 0.25f) < 1e-5f && Math.Abs(y0 + 0.25f) < 1e-5f && Math.Abs(y1 - 0.25f) < 1e-5f, "the cut is the box the picture is drawn in: " + x0 + " " + x1 + " " + y0 + " " + y1);
            // A box of the picture that is only partly on the part (shifted right by half): the loop stops at the part's edge on the right.
            PictureCutResult shifted = Make(c, part, Box(1f, 1f, false, 0.5f, 0f), 0.5f);
            Bounds(shifted.Loops, out x0, out y0, out x1, out y1);
            Program.Check(shifted.Loops != null && x1 <= 0.5f + 1e-5f && x0 >= 0.5f - 0.5f + 0.1f - 1e-4f, "a picture half off the board is cut at the board's edge");
        }

        private static void RoundBoard()
        {
            // A picture opaque all over, on a round part of 1 m: the board is the part's 64-gon cut to the picture's rectangle, and every loop point is in the polygon.
            PictureContour c = Contour(Rectangle(0, 0, 100, 100), 100, 100);
            GuidePart part = new GuidePart { Shape = GuidePartShape.Circle, W = 1f, H = 1f, Radius = 0.5f };
            PictureCutResult r = Make(c, part, Box(1f, 1f), 1f);
            Program.Check(r.Loops != null && r.Loops.Count == 1, "a loop: " + (r.Loops == null ? r.Reason : r.Loops.Count.ToString()));
            List<Point2> circle = GuideKit.PartOutline(part);
            bool inside = true;
            foreach (Point2 p in r.Loops[0])
            {
                for (int i = 0; i < circle.Count; i++)
                {
                    Point2 a = circle[i], b = circle[(i + 1) % circle.Count];
                    if ((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X) < -1e-5f)
                        inside = false;
                }
            }

            Program.Check(inside, "every point is inside the part's outline");
            Program.Check(r.Loops[0].Count == 64, "it has the outline's corners: " + r.Loops[0].Count);
            float disc = Area(new List<List<Point2>> { circle });
            Program.Near(Area(r.Loops), disc, 1e-4f, "and all of its area");
            // The convex shape a lamp-post box passes instead of the part's own outline is used the same way.
            var diamond = new List<Point2> { new Point2(0f, -0.5f), new Point2(0.5f, 0f), new Point2(0f, 0.5f), new Point2(-0.5f, 0f) };
            PictureCutResult cut = PictureCut.Make(c, diamond, 1f, 1f, 0f, 0f, 1f, 1f, false, 1f);
            Program.Near(Area(cut.Loops), 0.5f, 1e-4f, "a diamond shape: half the board");
            // An outline given clockwise is turned round.
            var clockwise = new List<Point2>(diamond);
            clockwise.Reverse();
            Program.Near(Area(PictureCut.Make(c, clockwise, 1f, 1f, 0f, 0f, 1f, 1f, false, 1f).Loops), Area(cut.Loops), 1e-5f, "a clockwise outline gives the same board");
        }

        private static List<List<Point2>> CellCutOf(byte[] alpha, int w, int h, float pw, float ph)
        {
            CustomMask mask = CustomMask.FromAlpha(Rgba(Whole(alpha)), w, h);
            GuidePart part = Part(pw, ph);
            return CustomSigns.CellCut(part, Box(pw, ph), mask, (float)h / w);
        }

        private static void Strokes()
        {
            // A picture of 1024 x 640 pixels with an L of two pixels' stroke: a cell of the 192-cell mask is over five pixels, and none is wholly the stroke.
            int w = 1024, h = 640;
            Shape stroke = Union(Rectangle(200, 100, 202, 540), Rectangle(200, 538, 760, 540));
            byte[] alpha = Alpha(stroke, w, h);
            Program.Check(CellCutOf(alpha, w, h, 1f, 0.625f) == null, "the 192-cell mask has no opaque cell in a 2 px stroke: the board is not cut at all");
            PictureContour c = PictureContour.FromAlpha(alpha, w, h);
            PictureCutResult r = Make(c, Part(1f, 0.625f), Box(1f, 0.625f), 0.625f);
            Program.Check(r.Loops != null && r.Loops.Count == 1, "the contour keeps it: one loop");
            float exact = (2f * 440f + 558f * 2f) / (w * h) * 0.625f;
            Program.Near(Area(r.Loops) / exact, 1f, 0.02f, $"with the stroke's area to 2 %: {Area(r.Loops) / exact:0.000}");

            // A disc: the contour is within a pixel of the circle; the cell cut is eroded by up to a cell.
            Shape disc = Disc(w / 2.0, h / 2.0, 200.0);
            byte[] dalpha = Alpha(disc, w, h);
            PictureCutResult round = Make(PictureContour.FromAlpha(dalpha, w, h), Part(1f, 0.625f), Box(1f, 0.625f), 0.625f);
            float discArea = (float)(Math.PI * 200.0 * 200.0) / (w * h) * 0.625f;
            Program.Near(Area(round.Loops) / discArea, 1f, 0.015f, $"a disc's area to 1.5 % (the edge is where the alpha crosses 250 of 255): {Area(round.Loops) / discArea:0.0000}");
            List<List<Point2>> cells = CellCutOf(dalpha, w, h, 1f, 0.625f);
            PictureCut.Compare(cells, round.Loops, 400, out double areaCells, out double areaContour, out double cellsOnly, out double contourOnly);
            Program.Check(contourOnly > 0.0 && areaContour > areaCells * 0.9, $"the cell cut lost {contourOnly / areaContour * 100.0:0.00} % of the contour's area and showed {cellsOnly / areaCells * 100.0:0.00} % beyond it");
        }

        private static void Budget()
        {
            // A text-like picture of 72 islands, each an E, a ring or a cross.
            PictureContour text = TextContour();
            GuidePart part = Part(1f, 0.625f);
            GuideElement image = Box(1f, 0.625f);
            PictureCutResult fine = Make(text, part, image, 0.625f, int.MaxValue);
            PictureCutResult exact = Make(text, part, image, 0.625f, fine.Triangles);
            Program.Check(exact.Loops != null && exact.Tolerance == 0.5f && exact.Triangles == fine.Triangles, "a budget of exactly the cost keeps 0.5 px");
            Program.Check(fine.Loops != null && fine.Loops.Count >= 72 && fine.Tolerance == 0.5f, $"unlimited, 0.5 px: {fine.Loops?.Count} loops, {fine.Vertices} vertices, {fine.Triangles} triangles");
            Program.Check(fine.Triangles == CostOf(fine.Loops), "the cost reported is the cost of the mesh: " + fine.Triangles + " / " + CostOf(fine.Loops));

            PictureCutResult relaxed = Make(text, part, image, 0.625f, fine.Triangles - 1);
            Program.Check(relaxed.Loops != null && relaxed.Tolerance > 0.5f && relaxed.Triangles < fine.Triangles && relaxed.Triangles <= fine.Triangles - 1, $"a budget a triangle short: relaxed to {relaxed.Tolerance} px, {relaxed.Triangles} triangles");
            PictureCutResult coarse = Make(text, part, image, 0.625f, relaxed.Triangles - 1);
            Program.Check(coarse.Loops == null ? coarse.Reason.StartsWith("over the budget") : coarse.Tolerance > relaxed.Tolerance, "and one short of that goes further or is refused: " + (coarse.Loops == null ? coarse.Reason : coarse.Tolerance.ToString()));
            // Many separate islands cost less than four triangles a vertex: the early skip is a lower bound and does not relax a board that fits.
            Shape squares = (x, y) => (int)(x / 6.0) % 2 == 0 && (int)(y / 6.0) % 2 == 0 && x < 300 && y < 300 && x % 6.0 >= 1.0 && x % 6.0 < 5.0 && y % 6.0 >= 1.0 && y % 6.0 < 5.0;
            PictureContour islands = Contour(squares, 320, 320);
            PictureCutResult lots = Make(islands, Part(1f, 1f), Box(1f, 1f), 1f, int.MaxValue);
            PictureCutResult lotsFit = Make(islands, Part(1f, 1f), Box(1f, 1f), 1f, lots.Triangles);
            Program.Check(lots.Loops != null && lots.Loops.Count > 300 && lotsFit.Loops != null && lotsFit.Tolerance == 0.5f && lotsFit.Triangles == lots.Triangles, $"{lots.Loops?.Count} islands, a budget of their cost: kept at 0.5 px ({lotsFit.Tolerance})");
            Program.Check(lots.Triangles < 4 * lots.Vertices - 4 * lots.Loops.Count + 1 && lots.Triangles >= 4 * lots.Vertices - 4 * lots.Loops.Count, "the cost is the lower bound for loops without holes: " + lots.Triangles + " / " + (4 * lots.Vertices - 4 * lots.Loops.Count));

            // Loops the triangles cannot fill (a hole that is in no outer loop): refused with the reason, not built.
            var orphan = new PictureContour { Width = 100, Height = 100 };
            orphan.Loops.Add(new List<Point2> { new Point2(0.1f, 0.1f), new Point2(0.4f, 0.1f), new Point2(0.4f, 0.4f), new Point2(0.1f, 0.4f) });
            orphan.Loops.Add(new List<Point2> { new Point2(0.6f, 0.6f), new Point2(0.6f, 0.8f), new Point2(0.8f, 0.8f), new Point2(0.8f, 0.6f) });
            PictureCutResult unfilled = Make(orphan, Part(1f, 1f), Box(1f, 1f), 1f);
            Program.Check(unfilled.Loops == null && unfilled.Reason == "the triangles do not fill the loops", "a hole in no outer loop: " + unfilled.Reason);
            PictureCutResult refused = Make(text, part, image, 0.625f, 10);
            Program.Check(refused.Loops == null && refused.Reason.StartsWith("over the budget") && refused.Tolerance == 1f && refused.Triangles > 10, "a budget no tolerance meets: refused at 1 px with the reason: " + refused.Reason);
            // The default budget.
            PictureCutResult usual = Make(text, part, image, 0.625f);
            Program.Check(usual.Loops == null ? usual.Reason.StartsWith("over the budget") : usual.Triangles <= PictureCut.TriangleBudget, $"the default budget of {PictureCut.TriangleBudget}: " + (usual.Loops == null ? usual.Reason : usual.Triangles + " triangles at " + usual.Tolerance + " px"));

            // A noisy blob (a photograph's silhouette): many corners.
            PictureContour blob = Contour(Blob(512, 512, 200, 60), 512, 512);
            PictureCutResult photo = Make(blob, Part(1f, 1f), Box(1f, 1f), 1f, int.MaxValue);
            PictureCutResult photoLimited = Make(blob, Part(1f, 1f), Box(1f, 1f), 1f);
            Program.Check(photo.Loops != null && (photoLimited.Loops == null || photoLimited.Triangles <= PictureCut.TriangleBudget), $"a blob: {photo.Triangles} triangles unlimited; with the budget: " + (photoLimited.Loops == null ? photoLimited.Reason : photoLimited.Triangles + " at " + photoLimited.Tolerance + " px"));
        }

        private static int CostOf(List<List<Point2>> loops)
        {
            PictureCutShadow.Counts(loops, out _, out int triangles);
            return triangles;
        }

        /// <summary>A line of letters: the bench's text-like picture.</summary>
        internal static Shape TextShape(int w, int h)
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

        /// <summary>A silhouette with a rough edge: a disc whose radius wanders with the angle, with a hole.</summary>
        internal static Shape Blob(int w, int h, double radius, int waves)
        {
            var rng = new System.Random(7);
            var phase = new double[6];
            var amp = new double[6];
            for (int i = 0; i < 6; i++)
            {
                phase[i] = rng.NextDouble() * 6.28;
                amp[i] = 0.02 + 0.03 * rng.NextDouble();
            }

            return (x, y) =>
            {
                double dx = x - w / 2.0, dy = y - h / 2.0, a = Math.Atan2(dy, dx), r = Math.Sqrt(dx * dx + dy * dy);
                double edge = radius;
                for (int i = 0; i < 6; i++)
                    edge += radius * amp[i] * Math.Sin(a * (waves / 6 + i * 3 + 1) + phase[i]);
                return r <= edge && r > radius * 0.25;
            };
        }

        /// <summary>The mesh of a board as the builder makes it: the face, the back and the edge, in one list.</summary>
        private static MeshBuilder MeshOf(List<List<Point2>> loops, float w, float h, out Func<float, float, (float u, float v)> uv)
        {
            uv = (px, py) => ((w * 0.5f + px) / w, 1f - (h * 0.5f - py) / h);
            var mesh = new MeshBuilder();
            Prims.LoopsFace(mesh, loops, 0.02f, true, 0f, 0f, uv);
            Prims.LoopsFace(mesh, loops, 0f, false, 0f, 0f, null);
            Prims.LoopsRim(mesh, loops, 0f, 0.02f, 0f, 0f);
            return mesh;
        }

        /// <summary>Whether every edge of the mesh, welded by position, is the edge of exactly two triangles; the problem when it is not.</summary>
        private static string Closed(MeshBuilder mesh)
        {
            var ids = new Dictionary<(long, long, long), int>();
            int Weld(Vec3 p)
            {
                var key = ((long)Math.Round(p.X * 1e5), (long)Math.Round(p.Y * 1e5), (long)Math.Round(p.Z * 1e5));
                if (!ids.TryGetValue(key, out int id))
                    ids[key] = id = ids.Count;
                return id;
            }

            var edges = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = Weld(mesh.Positions[mesh.Indices[t]]), b = Weld(mesh.Positions[mesh.Indices[t + 1]]), c = Weld(mesh.Positions[mesh.Indices[t + 2]]);
                if (a == b || b == c || a == c)
                    return "a triangle with two corners in one place";
                foreach ((int p, int q) in new[] { (a, b), (b, c), (c, a) })
                {
                    var edge = p < q ? (p, q) : (q, p);
                    edges[edge] = (edges.TryGetValue(edge, out int n) ? n : 0) + 1;
                }
            }

            int open = 0, many = 0;
            foreach (int count in edges.Values)
            {
                if (count == 1)
                    open++;
                else if (count > 2)
                    many++;
            }

            return open == 0 && many == 0 ? "" : $"{open} open edge(s), {many} edge(s) of more than two triangles";
        }

        private static void Meshes()
        {
            var pictures = new List<(string Name, Shape Shape, int W, int H)>
            {
                ("text", null, 1024, 640),
                ("ring", Ring(256, 256, 200, 10), 512, 512),
                ("thin stroke", Union(Rectangle(40, 20, 42, 200), Rectangle(40, 198, 160, 200)), 256, 256),
                ("blob with a hole", Blob(512, 512, 200, 60), 512, 512),
                ("star", Star(128, 128, 110, 40, 7), 256, 256),
                ("hole", Minus(Rectangle(10, 10, 118, 118), Disc(64, 64, 30)), 128, 128)
            };
            foreach ((string name, Shape shape, int w, int h) in pictures)
            {
                float pw = 1f, ph = (float)h / w;
                PictureContour c = shape == null ? TextContour() : Contour(shape, w, h);
                PictureCutResult r = Make(c, Part(pw, ph), Box(pw, ph), ph);
                if (r.Loops == null)
                {
                    Program.Check(r.Reason.StartsWith("over the budget"), name + ": no board and not for the budget: " + r.Reason);
                    continue;
                }

                MeshBuilder mesh = MeshOf(r.Loops, pw, ph, out _);
                string problem = Closed(mesh);
                Program.Check(problem == "", $"{name}: the board's mesh is closed ({r.Loops.Count} loops, {r.Vertices} vertices, {mesh.TriangleCount} triangles, tolerance {r.Tolerance}): {problem}");
                Program.Check(mesh.TriangleCount == r.Triangles, $"{name}: the cost counted is the triangles built: {mesh.TriangleCount} / {r.Triangles}");
            }

            // The picture coordinates of the face come from where a vertex is on the board, as before: every one is in 0 to 1 and is the function of its place.
            PictureCutResult cut = Make(TextContour(), Part(1f, 0.625f), Box(1f, 0.625f), 0.625f);
            Program.Check(cut.Loops != null, "the text board is cut: " + cut.Reason);
            var face = new MeshBuilder();
            Func<float, float, (float u, float v)> uv = (px, py) => ((1f * 0.5f + px) / 1f, 1f - (0.625f * 0.5f - py) / 0.625f);
            Prims.LoopsFace(face, cut.Loops, 0.02f, true, 0f, 0f, uv);
            bool range = true, place = true;
            for (int i = 0; i < face.VertexCount; i++)
            {
                float u = face.U[i], v = face.V[i];
                range &= u >= -1e-4f && u <= 1f + 1e-4f && v >= -1e-4f && v <= 1f + 1e-4f;
                // The vertex is at (-x, y) of the loop's point: the face is mirrored as the builder has it.
                place &= Math.Abs(u - (0.5f - face.Positions[i].X)) < 1e-5f && Math.Abs(v - (1f - (0.3125f - face.Positions[i].Y) / 0.625f)) < 1e-5f;
            }

            Program.Check(range && place, "the face's picture coordinates are in range and follow the vertex's place");
        }

        private static void Comparing()
        {
            List<Point2> a = new List<Point2> { new Point2(0f, 0f), new Point2(4f, 0f), new Point2(4f, 3f), new Point2(0f, 3f) };
            List<List<Point2>> one = new List<List<Point2>> { a };
            PictureCut.Compare(one, one, 100, out double ao, out double an, out double oo, out double no);
            Program.Check(Math.Abs(ao - 12.0) < 1e-6 && Math.Abs(an - 12.0) < 1e-6 && oo < 1e-9 && no < 1e-9, $"the same set: areas 12, no difference: {ao} {an} {oo} {no}");
            var shifted = new List<List<Point2>> { new List<Point2> { new Point2(1f, 0f), new Point2(5f, 0f), new Point2(5f, 3f), new Point2(1f, 3f) } };
            PictureCut.Compare(one, shifted, 30, out ao, out an, out oo, out no);
            Program.Check(Math.Abs(oo - 3.0) < 1e-6 && Math.Abs(no - 3.0) < 1e-6 && Math.Abs(ao - 12.0) < 1e-6, $"shifted by one: 3 only in each: {oo} {no}");
            var inner = new List<List<Point2>> { new List<Point2> { new Point2(1f, 1f), new Point2(3f, 1f), new Point2(3f, 2f), new Point2(1f, 2f) } };
            PictureCut.Compare(one, inner, 30, out ao, out an, out oo, out no);
            Program.Check(Math.Abs(an - 2.0) < 1e-6 && Math.Abs(oo - 10.0) < 1e-6 && no < 1e-9, $"a smaller one inside: only the old has 10: {oo} {no}");
            var holed = new List<List<Point2>> { a, new List<Point2> { new Point2(1f, 1f), new Point2(1f, 2f), new Point2(3f, 2f), new Point2(3f, 1f) } };
            PictureCut.Compare(holed, one, 30, out ao, out an, out oo, out no);
            Program.Check(Math.Abs(ao - 10.0) < 1e-6 && Math.Abs(an - 12.0) < 1e-6 && no > 1.999 && oo < 1e-9, $"a hole: the new fills it: {ao} {an} {oo} {no}");
            PictureCut.Compare(null, one, 100, out ao, out an, out oo, out no);
            Program.Check(ao == 0.0 && Math.Abs(an - 12.0) < 1e-6 && Math.Abs(no - 12.0) < 1e-6, "no old set: all of the new one is new");
            PictureCut.Compare(new List<List<Point2>>(), new List<List<Point2>>(), 100, out ao, out an, out oo, out no);
            Program.Check(ao == 0.0 && an == 0.0, "two empty sets: nothing, no division by nothing");
            // A disc against a square of its own area, to the resolution of the rows.
            var disc = new List<Point2>();
            for (int i = 0; i < 360; i++)
                disc.Add(new Point2((float)Math.Cos(i * Math.PI / 180.0), (float)Math.Sin(i * Math.PI / 180.0)));
            PictureCut.Compare(new List<List<Point2>> { disc }, new List<List<Point2>> { disc }, 400, out ao, out an, out oo, out no);
            Program.Check(Math.Abs(ao - Math.PI) < 0.005 && oo < 1e-9, $"a disc's area to the rows' resolution: {ao}");
        }

        private static List<List<Point2>> Clone(List<List<Point2>> loops)
        {
            var copy = new List<List<Point2>>();
            foreach (List<Point2> loop in loops)
                copy.Add(new List<Point2>(loop));
            return copy;
        }

        private static bool SameLoops(List<List<Point2>> a, List<List<Point2>> b)
        {
            if (a == null || b == null)
                return a == b;
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Count != b[i].Count)
                    return false;
                for (int j = 0; j < a[i].Count; j++)
                {
                    if (a[i][j].X != b[i][j].X || a[i][j].Y != b[i][j].Y)
                        return false;
                }
            }

            return true;
        }

        private static void Modes()
        {
            Func<string, PictureContour> savedContours = CustomSigns.Contours;
            FeatureState savedMode = CustomSigns.PictureCutMode;
            Action<string> savedLog = PictureCutShadow.Log, savedUseLog = PictureCutUse.Log;
            var lines = new List<string>();
            try
            {
                PictureCutShadow.Log = line => { lock (lines) lines.Add(line); };
                PictureCutUse.Log = line => { lock (lines) lines.Add(line); };
                int w = 512, h = 320;
                byte[] alpha = Alpha(Union(Ring(256, 160, 120, 14), Rectangle(40, 40, 60, 280), Disc(430, 100, 40)), w, h);
                CustomMask mask = CustomMask.FromAlpha(Rgba(Whole(alpha)), w, h);
                PictureContour contour = PictureContour.FromAlpha(alpha, w, h);
                GuidePart part = Part(1f, 0.625f);
                GuideElement image = Box(1f, 0.625f);
                List<List<Point2>> cell = CustomSigns.CellCut(part, image, mask, 0.625f);
                PictureCutResult made = Make(contour, part, image, 0.625f);
                Program.Check(cell != null && made.Loops != null && !SameLoops(cell, made.Loops), "the two cuts differ on this picture");
                bool ready = true;
                CustomSigns.Contours = key => ready ? contour : null;

                // Off: today's cut, the contour not asked for.
                int asked = 0;
                CustomSigns.Contours = key =>
                {
                    asked++;
                    return ready ? contour : null;
                };
                CustomSigns.PictureCutMode = FeatureState.Off;
                Program.Check(SameLoops(CustomSigns.Cut(part, image, mask, 0.625f), cell) && asked == 0, "off: the cell cut, and the contour is not asked for");
                CustomSigns.Contours = null;
                CustomSigns.PictureCutMode = FeatureState.On;
                Program.Check(SameLoops(CustomSigns.Cut(part, image, mask, 0.625f), cell), "no contour source: the cell cut in any mode");
                CustomSigns.Contours = key =>
                {
                    asked++;
                    return ready ? contour : null;
                };

                // Shadow: the cell cut is the board's, the contour's is made on another thread and compared.
                PictureCutShadow.Reset();
                CustomSigns.PictureCutMode = FeatureState.Shadow;
                List<List<Point2>> shadowed = CustomSigns.Cut(part, image, mask, 0.625f);
                Program.Check(SameLoops(shadowed, cell), "shadow: the board is the cell cut");
                PictureCutShadow.Drain();
                CutShadowStats stats = PictureCutShadow.Stats;
                Program.Check(stats.Count == 1 && stats.Fallbacks == 0 && stats.NewVertices > 0 && stats.OldVertices > 0 && stats.LostSum > 0.0, $"shadow: one board compared with both cuts' counts: {stats.Count} / {stats.OldVertices} / {stats.NewVertices} / lost {stats.LostSum:0.0000}");
                lock (lines)
                    Program.Check(lines.Exists(l => l.StartsWith("Picture cut shadow: " + Key)), "shadow: the first boards are written in full");
                CustomSigns.PictureCutMode = FeatureState.Log;
                Program.Check(SameLoops(CustomSigns.Cut(part, image, mask, 0.625f), cell), "log reads as shadow");
                PictureCutShadow.Drain();
                Program.Check(PictureCutShadow.Stats.Count == 2, "and is compared too");
                ready = false;
                PictureCutShadow.Reset();
                CustomSigns.PictureCutMode = FeatureState.Shadow;
                Program.Check(SameLoops(CustomSigns.Cut(part, image, mask, 0.625f), cell), "shadow with no contour yet: the cell cut");
                PictureCutShadow.Drain();
                Program.Check(PictureCutShadow.Stats.Count == 0 && PictureCutShadow.Stats.NotReady == 1, "and it is counted as not ready");
                ready = true;

                // The comparison itself on this board: what the shadow reports is what the functions say.
                var job = new PictureCutShadow.Job { Key = Key, Contour = contour, Outline = GuideKit.PartOutline(part), PartW = 1f, PartH = 0.625f, ImageW = 1f, ImageH = 0.625f, PictureAspect = 0.625f, Old = Clone(cell), OldMs = 0.5 };
                CutComparison c = PictureCutShadow.Compare(job);
                PictureCut.Compare(cell, made.Loops, 400, out double ao, out double an, out double oo, out double no);
                Program.Check(c.Reason == "" && Math.Abs(c.AreaOld - ao) < 1e-12 && Math.Abs(c.AreaNew - an) < 1e-12 && Math.Abs(c.OldOnly - oo) < 1e-12 && Math.Abs(c.NewOnly - no) < 1e-12 && c.NewTriangles == made.Triangles && c.OldMs == 0.5, "the comparison is the two cuts' counts and areas");
                Program.Check(c.Lost == c.NewOnly / c.AreaNew && c.Excess == c.OldOnly / c.AreaOld && c.Lost > 0.0, "the share lost is what only the contour has over the contour's area");
                job.Contour = new PictureContour { Width = 0, Height = 0 };
                Program.Check(PictureCutShadow.Compare(job).Reason.Length > 0, "a contour that cannot be cut is a reason, not a throw");

                // On: the contour's board; with none, the cell cut, counted.
                PictureCutUse.Reset();
                CustomSigns.PictureCutMode = FeatureState.On;
                Program.Check(SameLoops(CustomSigns.Cut(part, image, mask, 0.625f), made.Loops), "on: the board is the contour's");
                Program.Check(PictureCutUse.Used == 1 && PictureCutUse.Fallbacks == 0, "and counted");
                ready = false;
                Program.Check(SameLoops(CustomSigns.Cut(part, image, mask, 0.625f), cell) && PictureCutUse.NotReady == 1, "on with no contour yet: the cell cut, counted");
                ready = true;
                CustomSigns.Contours = key => new PictureContour { Width = 8, Height = 8 };
                Program.Check(CustomSigns.Cut(part, image, mask, 0.625f) == null, "on with a contour that has nothing opaque on the board: no cut, the whole outline, as the cell cut has none");
                PictureContour textContour = TextContour();
                CustomSigns.Contours = key => textContour;
                PictureCutUse.Reset();
                GuideElement textImage = Box(1f, 0.625f);
                List<List<Point2>> fallback = CustomSigns.Cut(part, textImage, CustomMask.FromAlpha(Rgba(Whole(TextAlpha())), 1024, 640), 0.625f);
                Program.Check(PictureCutUse.Used + PictureCutUse.Fallbacks == 1 && fallback != null, "a text board is cut one way or the other and counted: used " + PictureCutUse.Used + ", fallbacks " + PictureCutUse.Fallbacks);
                // An image with a text that is not a key is never looked up.
                asked = 0;
                CustomSigns.Contours = key =>
                {
                    asked++;
                    return contour;
                };
                GuideElement odd = Box(1f, 0.625f);
                odd.Text = "not-a-key";
                CustomSigns.Cut(part, odd, mask, 0.625f);
                Program.Check(asked == 0, "a text that is not a picture's key asks for nothing");
            }
            finally
            {
                PictureCutShadow.Drain();
                CustomSigns.Contours = savedContours;
                CustomSigns.PictureCutMode = savedMode;
                PictureCutShadow.Log = savedLog;
                PictureCutUse.Log = savedUseLog;
                PictureCutShadow.Reset();
                PictureCutUse.Reset();
            }
        }

        private static CutComparison Comparison(double lost, string reason = "", float tolerance = 0.5f) => new CutComparison
        {
            Key = Key, OldVertices = 100, OldTriangles = 300, NewVertices = 400, NewTriangles = 1200, OldMs = 0.2, NewMs = 1.5, AreaOld = 1.0, AreaNew = 1.0, OldOnly = 0.01, NewOnly = lost, Tolerance = tolerance, Reason = reason
        };

        private static void Counters()
        {
            var stats = new CutShadowStats();
            var lines = new List<string>();
            for (int i = 0; i < 2300; i++)
                lines.AddRange(stats.Add(Comparison((i % 100) / 1000.0)));
            Program.Check(stats.Count == CutShadowStats.Limit && stats.Full, "the count stops at the limit: " + stats.Count);
            Program.Check(lines.FindAll(l => l.StartsWith("Picture cut shadow: " + Key)).Count == CutShadowStats.Detailed, "the first " + CutShadowStats.Detailed + " are in full");
            Program.Check(lines.FindAll(l => l.Contains("board(s) compared")).Count == 4, "four summaries: at 50, 250, 1000 and the limit");
            Program.Check(stats.WorstList.Count == CutShadowStats.Worst, "the worst " + CutShadowStats.Worst + " are kept");
            bool sorted = true;
            for (int i = 1; i < stats.WorstList.Count; i++)
                sorted &= stats.WorstList[i - 1].Lost >= stats.WorstList[i].Lost;
            Program.Check(sorted && stats.WorstList[0].Lost == 0.099, "worst first: " + stats.WorstList[0].Lost);
            Program.Check(lines[lines.Count - 1].Contains("the worst 20 by what the cell cut loses") && lines[lines.Count - 1].Contains("(the last, 2000 compared)"), "the last summary lists them");
            Program.Check(stats.Add(Comparison(0.5)).Count == 0 && stats.Count == CutShadowStats.Limit, "nothing is written or counted after the limit");

            var mixed = new CutShadowStats();
            mixed.Add(Comparison(0.02));
            mixed.Add(Comparison(0.0, "the loops did not pair up along the clip"));
            mixed.Add(Comparison(0.03, "", 0.75f));
            mixed.Add(Comparison(0.0, "over the budget at 1.0 px: 9000 triangles"));
            Program.Check(mixed.Count == 4 && mixed.Fallbacks == 2 && mixed.Relaxed == 1 && mixed.OverBudget == 1 && mixed.WorstList.Count == 2, $"a board with no contour result is a fallback and not ranked: {mixed.Fallbacks} / {mixed.WorstList.Count}");
            string summary = mixed.Summary(false);
            Program.Check(summary.Contains("4 board(s) compared, 2 with the contour, 2 where it was not used") && summary.Contains("over the budget x1") && summary.Contains("the loops did not pair up along the clip x1"), "the summary says how many and why: " + summary);
            Program.Check(summary.Contains("mean per board") && summary.Contains("1 relaxed above 0.5 px"), "and the means");

            // The use counters: the first lines in full, then 50, 250 and 1,000.
            var said = new List<string>();
            PictureCutUse.Log = said.Add;
            PictureCutUse.Reset();
            try
            {
                var ok = new PictureCutResult { Loops = new List<List<Point2>> { new List<Point2>() }, Vertices = 10, Triangles = 40, Tolerance = 0.5f, Ms = 0.4 };
                var coarse = new PictureCutResult { Loops = new List<List<Point2>> { new List<Point2>() }, Vertices = 10, Triangles = 40, Tolerance = 0.75f, Ms = 0.4 };
                var bad = new PictureCutResult { Reason = "over the budget at 1.0 px: 9000 triangles", Tolerance = 1f };
                for (int i = 0; i < 1100; i++)
                    PictureCutUse.Note(Key, i % 10 == 9 ? bad : i % 10 == 4 ? coarse : ok);
                Program.Check(PictureCutUse.Used == 990 && PictureCutUse.Fallbacks == 110, "used and fallbacks are counted: " + PictureCutUse.Used + "/" + PictureCutUse.Fallbacks);
                Program.Check(said[said.Count - 1].Contains("(100 relaxed above 0.5 px"), "the relaxed boards are counted in the summary: " + said[said.Count - 1]);
                Program.Check(said.Count == CutShadowStats.Detailed + 3, "eight in full and three summaries: " + said.Count);
                Program.Check(said[said.Count - 1].Contains("1000 board(s)") && said[said.Count - 1].Contains("over the budget x"), "the summaries say how many and why");
            }
            finally
            {
                PictureCutUse.Log = null;
                PictureCutUse.Reset();
            }
        }
    }
}
