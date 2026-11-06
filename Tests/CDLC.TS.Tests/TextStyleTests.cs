using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The artistic styles of the text light box (0.12.76): the lean (an exact shear), bolder and lighter strokes (an offset of the outline, checked against areas worked out by hand and for what it cannot
    /// do), the crossing test it relies on, the glyphs of the traffic sign font dressed, the plan that holds them, and the template. The look in the game is not testable here.
    /// </summary>
    internal static class TextStyleTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("styles: edges that cross", Crossing);
            Program.Run("styles: the lean is an exact shear", Lean);
            Program.Run("styles: the offset of strokes, against areas worked out by hand", OffsetMaths);
            Program.Run("styles: an offset that does not hold is made smaller, and then left", OffsetLimits);
            Program.Run("styles: bolder and lighter glyphs of the traffic sign font", RealGlyphs);
            Program.Run("styles: planned, kept apart and held by a frame", Planned);
            Program.Run("styles: the template and the board", TemplateStyles);
            Program.Run("styles: pictures for a look (when TS_STYLE_PREVIEW names a folder)", Pictures);
        }

        private static List<Point2> Rect(float x0, float y0, float x1, float y1, bool hole = false)
        {
            var loop = new List<Point2> { new Point2(x0, y0), new Point2(x1, y0), new Point2(x1, y1), new Point2(x0, y1) };
            if (hole)
                loop.Reverse();
            return loop;
        }

        private static List<Point2> Circle(float cx, float cy, float r, int n)
        {
            var loop = new List<Point2>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                loop.Add(new Point2(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a))));
            }

            return loop;
        }

        private static GlyphMesh Mesh(params List<Point2>[] loops) => TextGlyphs.FromLoops(loops.ToList(), 1f);

        private static float Area(GlyphMesh mesh) => mesh.Area;

        private static void Crossing()
        {
            List<List<Point2>> L(params List<Point2>[] loops) => loops.ToList();
            Program.Check(GlyphStyles.Crosses(L(Rect(0, 0, 2, 2), Rect(1, 1, 3, 3))), "two squares that overlap cross");
            Program.Check(!GlyphStyles.Crosses(L(Rect(0, 0, 1, 1), Rect(2, 2, 3, 3))), "two squares apart do not");
            Program.Check(!GlyphStyles.Crosses(L(Rect(0, 0, 4, 4), Rect(1, 1, 2, 2, true))), "a hole inside its outer one does not");
            Program.Check(!GlyphStyles.Crosses(L(Rect(0, 0, 1, 1), Rect(1, 0, 2, 1))), "two squares that share a side touch and do not cross");
            Program.Check(!GlyphStyles.Crosses(L(Rect(0, 0, 1, 1), Rect(1, 1, 2, 2))), "two squares that meet at a corner do not");
            Program.Check(!GlyphStyles.Crosses(L(Rect(0, 0, 2, 2), new List<Point2> { new Point2(1, 0), new Point2(1.5f, -1), new Point2(0.5f, -1) })), "an end that lies on another edge is a touch, not a crossing");
            Program.Check(GlyphStyles.Crosses(L(new List<Point2> { new Point2(0, 0), new Point2(1, 1), new Point2(1, 0), new Point2(0, 1) })), "a bow tie crosses itself");
            Program.Check(!GlyphStyles.Crosses(L(Rect(0, 0, 1, 1))) && !GlyphStyles.Crosses(L()) && !GlyphStyles.Crosses(L(new List<Point2> { new Point2(0, 0), new Point2(1, 0), new Point2(0, 1) })), "one simple loop, none, a triangle: no crossing");

            // The grid finds what the pairs would: random loops, checked against every pair of edges.
            var random = new Random(99);
            int crossing = 0, simple = 0;
            for (int round = 0; round < 400; round++)
            {
                var loops = new List<List<Point2>>();
                int count = 1 + random.Next(3);
                for (int l = 0; l < count; l++)
                {
                    int n = 3 + random.Next(40);
                    float cx = (float)random.NextDouble() * 2f, cy = (float)random.NextDouble() * 2f, r = 0.2f + (float)random.NextDouble() * 1.5f;
                    var loop = new List<Point2>();
                    var angles = Enumerable.Range(0, n).Select(_ => random.NextDouble() * 2.0 * Math.PI).ToList();
                    if (random.Next(2) == 0)
                        angles.Sort();
                    foreach (double a in angles)
                        loop.Add(new Point2(cx + (float)(r * (0.6 + 0.4 * random.NextDouble()) * Math.Cos(a)), cy + (float)(r * (0.6 + 0.4 * random.NextDouble()) * Math.Sin(a))));
                    loops.Add(loop);
                }

                bool expected = Brute(loops), got = GlyphStyles.Crosses(loops);
                if (expected)
                    crossing++;
                else
                    simple++;
                Program.Check(expected == got, "round " + round + ": the grid and the pairs agree (" + expected + ", " + got + ")");
            }

            Program.Check(crossing > 50 && simple > 20, "both kinds of case were tried: " + crossing + " crossing, " + simple + " not");
        }

        /// <summary>Every pair of edges, by their parameters along each: a crossing strictly inside both.</summary>
        private static bool Brute(List<List<Point2>> loops)
        {
            var edges = new List<(Point2 A, Point2 B, int Loop, int Index, int Size)>();
            for (int l = 0; l < loops.Count; l++)
            {
                for (int i = 0; i < loops[l].Count; i++)
                    edges.Add((loops[l][i], loops[l][(i + 1) % loops[l].Count], l, i, loops[l].Count));
            }

            for (int s = 0; s < edges.Count; s++)
            {
                for (int t = s + 1; t < edges.Count; t++)
                {
                    var a = edges[s];
                    var b = edges[t];
                    if (a.Loop == b.Loop && (Math.Abs(a.Index - b.Index) == 1 || Math.Abs(a.Index - b.Index) == a.Size - 1))
                        continue;
                    double dx1 = a.B.X - a.A.X, dy1 = a.B.Y - a.A.Y, dx2 = b.B.X - b.A.X, dy2 = b.B.Y - b.A.Y;
                    double denominator = dx1 * dy2 - dy1 * dx2;
                    if (Math.Abs(denominator) < 1e-15)
                        continue;
                    double ox = b.A.X - a.A.X, oy = b.A.Y - a.A.Y;
                    double u = (ox * dy2 - oy * dx2) / denominator, v = (ox * dy1 - oy * dx1) / denominator;
                    if (u > 1e-9 && u < 1 - 1e-9 && v > 1e-9 && v < 1 - 1e-9)
                        return true;
                }
            }

            return false;
        }

        private static void Lean()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            int bucket = TextGlyphs.Bucket(TextPlans.RelativeFlatness(0.3f));
            const float Slant = 0.2126f, Pivot = 0.3f;
            int tried = 0;
            foreach (char c in "口小永国一人大0ABO的")
            {
                GlyphMesh mesh = TextGlyphs.Build(font, c, bucket);
                if (mesh == null)
                    continue;
                GlyphMesh leaning = GlyphStyles.Shear(mesh, Slant, Pivot);
                bool exact = leaning.Loops.Count == mesh.Loops.Count;
                for (int l = 0; l < mesh.Loops.Count && exact; l++)
                {
                    exact &= leaning.Loops[l].Count == mesh.Loops[l].Count;
                    for (int i = 0; i < mesh.Loops[l].Count && exact; i++)
                    {
                        Point2 p = mesh.Loops[l][i], q = leaning.Loops[l][i];
                        exact &= Math.Abs(q.Y - p.Y) < 1e-7f && Math.Abs(q.X - (p.X + (p.Y - Pivot) * Slant)) < 2e-6f;
                    }
                }

                Program.Check(exact, c + ": every point moves by the lean of its height above the pivot, and only in x");
                Program.Near(leaning.Area, mesh.Area, mesh.Area * 1e-4f, c + ": a shear keeps the area");
                Program.Check(leaning.Loops.Select(l => Math.Sign(CutOutline.Area(l))).SequenceEqual(mesh.Loops.Select(l => Math.Sign(CutOutline.Area(l)))), c + ": and the way each loop runs");
                Program.Check(leaning.IsSound(out string why), c + ": a sound mesh: " + why);
                Program.Check(leaning.MaxY == mesh.MaxY && leaning.MinY == mesh.MinY && leaning.Advance == mesh.Advance, c + ": the height and the advance stay");
                GlyphMesh back = GlyphStyles.Shear(leaning, -Slant, Pivot);
                bool same = true;
                for (int l = 0; l < mesh.Loops.Count; l++)
                {
                    for (int i = 0; i < mesh.Loops[l].Count; i++)
                        same &= Math.Abs(back.Loops[l][i].X - mesh.Loops[l][i].X) < 1e-5f;
                }

                Program.Check(same, c + ": leaning back is where it began");
                tried++;
            }

            Program.Check(tried >= 8, "glyphs were tried: " + tried);
            GlyphMesh one = TextGlyphs.Build(font, '口', bucket);
            Program.Check(ReferenceEquals(GlyphStyles.Shear(one, 0f, Pivot), one) && GlyphStyles.Shear(null, 0.2f, Pivot) == null, "no lean, no mesh: the same");
            // A line at the pivot does not move; above it leans right, below it left.
            GlyphMesh bar = Mesh(Rect(0f, Pivot - 0.01f, 1f, Pivot + 0.01f));
            GlyphMesh lean = GlyphStyles.Shear(bar, 0.5f, Pivot);
            Program.Check(Math.Abs(lean.Loops[0][0].X - (0f - 0.005f)) < 1e-6f && Math.Abs(lean.Loops[0][2].X - (1f + 0.005f)) < 1e-6f, "the top of the bar goes right and its bottom left by the same");
        }

        /// <summary>The flatness the tests ask the traced line for: what a letter of 0.3 m is cut with.</summary>
        private const float Flat = 0.0017f;

        private static GlyphMesh Grow(GlyphMesh mesh, float amount, out float applied) => GlyphStyles.Offset(mesh, amount, Flat, out applied);

        private static void OffsetMaths()
        {
            // A square grown by d is the square, its four sides moved out, and a quarter of a disc at each corner: 1 + 4 d + pi d squared. Shrunk it is (1 - 2 d) squared, the corners staying sharp.
            GlyphMesh square = Mesh(Rect(0, 0, 1, 1));
            GlyphMesh grown = Grow(square, 0.1f, out float applied);
            Program.Near(applied, 0.1f, 1e-6f, "the whole offset was made");
            Program.Near(grown.Area, 1f + 0.4f + (float)Math.PI * 0.01f, 0.006f, "a square grown by 0.1, its corners round: " + grown.Area);
            Program.Check(grown.Loops.Count == 1 && CutOutline.Area(grown.Loops[0]) > 0f && grown.IsSound(out _) && !GlyphStyles.Crosses(grown.Loops), "one outer loop, counter-clockwise, sound");
            GlyphMesh thin = Grow(square, -0.1f, out applied);
            Program.Near(thin.Area, 0.64f, 0.004f, "and shrunk by 0.1: " + thin.Area);
            Program.Near(applied, -0.1f, 1e-6f, "(the whole offset)");
            Program.Check(Math.Abs(grown.MinX + 0.1f) < 0.004f && Math.Abs(grown.MaxX - 1.1f) < 0.004f && Math.Abs(grown.MinY + 0.1f) < 0.004f && Math.Abs(grown.MaxY - 1.1f) < 0.004f, "its box is 0.1 out on every side: " + grown.MinX + ", " + grown.MaxX);
            Program.Check(Math.Abs(thin.MinX - 0.1f) < 0.004f && Math.Abs(thin.MaxX - 0.9f) < 0.004f && Math.Abs(thin.MinY - 0.1f) < 0.004f && Math.Abs(thin.MaxY - 0.9f) < 0.004f, "and 0.1 in");

            // A ring: the outer loop grows and the hole, which is clockwise, shrinks: the filled area grows on both its sides.
            GlyphMesh ring = Mesh(Rect(0, 0, 1, 1), Rect(0.3f, 0.3f, 0.7f, 0.7f, true));
            Program.Near(ring.Area, 1f - 0.16f, 1e-5f, "the ring before: " + ring.Area);
            GlyphMesh bold = Grow(ring, 0.05f, out applied);
            Program.Near(bold.Area, 1f + 0.2f + (float)Math.PI * 0.0025f - 0.09f, 0.004f, "bolder, the hole is 0.3 wide: " + bold.Area);
            Program.Check(bold.Loops.Count == 2 && bold.Loops.Count(l => CutOutline.Area(l) < 0f) == 1 && bold.Loops.Count(l => CutOutline.Area(l) > 0f) == 1, "and is still a ring: an outer loop and a hole");
            GlyphMesh light = Grow(ring, -0.05f, out applied);
            Program.Near(light.Area, 0.81f - (0.25f - (4f - (float)Math.PI) * 0.0025f), 0.004f, "lighter, the hole is 0.5 wide with round corners: " + light.Area);

            // An L grown: perimeter 8, five convex corners (a quarter disc each) and one reflex (a square lost twice over).
            GlyphMesh ell = Mesh(new List<Point2> { new Point2(0, 0), new Point2(2, 0), new Point2(2, 1), new Point2(1, 1), new Point2(1, 2), new Point2(0, 2) });
            Program.Near(GlyphStyles.Offset(ell, 0.1f, Flat, out _).Area, 3f + 0.8f + (5f * (float)Math.PI / 4f - 1f) * 0.01f, 0.012f, "the L grown");

            // A polygon of many sides grows to a disc.
            const int N = 96;
            GlyphMesh circle = Mesh(Circle(0.5f, 0.5f, 0.4f, N));
            Program.Near(Grow(circle, 0.03f, out _).Area, (float)(Math.PI * 0.43 * 0.43), 0.006f, "a 96-gon grown by 0.03 is the bigger disc");
            Program.Near(Grow(circle, -0.03f, out _).Area, (float)(Math.PI * 0.37 * 0.37), 0.006f, "and shrunk, the smaller");

            // Where the offset opens a corner the line is round: the point of the grown square's outline nearest to the old corner is a radius from it.
            Point2 nearest = grown.Loops[0].OrderBy(p => (p.X - 1f) * (p.X - 1f) + (p.Y - 1f) * (p.Y - 1f)).First();
            float corner = (float)Math.Sqrt((nearest.X - 1f) * (nearest.X - 1f) + (nearest.Y - 1f) * (nearest.Y - 1f));
            Program.Check(Math.Abs(corner - 0.1f) < 0.006f, "a grown corner is on the circle of the offset's radius round the old one: " + corner);

            // The way up is the way up: a T grown reaches round its bar at the bottom and not at the top of its stem.
            GlyphMesh tee = Mesh(new List<Point2> { new Point2(0, 0), new Point2(3, 0), new Point2(3, 1), new Point2(2, 1), new Point2(2, 3), new Point2(1, 3), new Point2(1, 1), new Point2(0, 1) });
            GlyphMesh teeGrown = Grow(tee, 0.05f, out _);
            Program.Check(TextGlyphs.Inside(teeGrown.Loops[0], 0.5f, 0.5f) && TextGlyphs.Inside(teeGrown.Loops[0], 1.5f, 3.03f) && !TextGlyphs.Inside(teeGrown.Loops[0], 0.2f, 2.5f) && !TextGlyphs.Inside(teeGrown.Loops[0], 1.5f, 3.2f), "a T keeps its bar at the foot and its stem upward");
            Program.Check(Math.Abs(teeGrown.MaxY - 3.05f) < 0.005f && Math.Abs(teeGrown.MinY + 0.05f) < 0.005f && Math.Abs(teeGrown.MaxX - 3.05f) < 0.005f, "and its box is 0.05 out");

            // The input is never changed.
            Program.Check(square.Loops[0][0].X == 0f && square.Loops[0][2].X == 1f && square.Area == 1f && ring.Loops.Count == 2, "the glyph given is the glyph it was");
        }

        private static void OffsetLimits()
        {
            // A strip 0.1 wide made lighter by 0.06 on each side would be gone; by 0.03 it is a strip 0.04 wide.
            GlyphMesh strip = Mesh(Rect(0, 0, 0.1f, 1f));
            GlyphMesh lighter = Grow(strip, -0.06f, out float applied);
            Program.Near(applied, -0.03f, 1e-6f, "a stroke narrower than the offset: the offset is halved once: " + applied);
            Program.Near(lighter.Area, 0.04f * 0.94f, 0.003f, "and the strip is what that leaves: " + lighter.Area);

            // Two bars 0.04 apart made bolder by 0.03 on each side run into one: strokes that nearly touched may join.
            GlyphMesh bars = Mesh(Rect(0, 0, 0.1f, 1f), Rect(0.14f, 0, 0.24f, 1f));
            GlyphMesh bolder = Grow(bars, 0.03f, out applied);
            Program.Check(applied == 0.03f && bolder.Loops.Count == 1 && bolder.Loops.Count(l => CutOutline.Area(l) < 0f) == 0, "strokes 0.04 apart join when each grows by 0.03: " + bolder.Loops.Count + " loop(s)");

            // A stroke beside a fat one that the offset would take away: the offset is made smaller, so that it stays.
            GlyphMesh fatAndThin = Mesh(Rect(0, 0, 0.1f, 1f), Rect(0.5f, 0, 0.53f, 1f));
            GlyphMesh stayed = Grow(fatAndThin, -0.02f, out applied);
            Program.Check(applied == -0.01f && stayed.Loops.Count == 2, "a stroke 0.03 wide made lighter by 0.02 each side would be gone: the offset is halved and both stay: " + applied + ", " + stayed.Loops.Count);

            // A U whose opening the growth closes: a hole appears (the gap is enclosed); a hole that was there never closes.
            GlyphMesh cup = Mesh(new List<Point2> { new Point2(0, 0), new Point2(0.5f, 0), new Point2(0.5f, 0.5f), new Point2(0.46f, 0.5f), new Point2(0.46f, 0.1f), new Point2(0.04f, 0.1f), new Point2(0.04f, 0.5f), new Point2(0f, 0.5f) });
            GlyphMesh closed = Grow(cup, 0.03f, out applied);
            Program.Check(applied == 0.03f && closed.Loops.Count(l => CutOutline.Area(l) < 0f) >= 0 && closed.IsSound(out _), "a cup made fatter is still a sound shape: " + closed.Loops.Count + " loop(s)");

            // A request six times too big is halved three times, and no more.
            GlyphMesh wide = Mesh(Rect(0, 0, 0.1f, 1f));
            GlyphMesh third = Grow(wide, -0.3f, out applied);
            Program.Check(applied == -0.0375f && third.Loops.Count == 1, "a request that is eight times too big takes three halvings: " + applied);
            Program.Check(ReferenceEquals(Grow(wide, -0.6f, out applied), wide) && applied == 0f, "and one that is sixteen times too big takes none that holds");

            // A stroke so thin that no offset holds is left as it was.
            GlyphMesh hair = Mesh(Rect(0, 0, 0.01f, 1f));
            GlyphMesh same = Grow(hair, -0.2f, out applied);
            Program.Check(ReferenceEquals(same, hair) && applied == 0f, "a hair line cannot be made lighter: the glyph is returned as it was");

            // A hole that the offset would close.
            GlyphMesh ring = Mesh(Rect(0, 0, 1, 1), Rect(0.45f, 0.45f, 0.55f, 0.55f, true));
            GlyphMesh closing = Grow(ring, 0.08f, out applied);
            Program.Check(applied > 0f && applied < 0.08f && closing.Loops.Count == 2, "a hole too small for the offset stays open at a smaller one: " + applied);

            // Two islands that the offset joins.
            GlyphMesh dots = Mesh(Rect(0, 0, 0.1f, 0.1f), Rect(0.15f, 0, 0.25f, 0.1f));
            Program.Check(Grow(dots, 0.04f, out applied).Loops.Count == 1 && applied == 0.04f && Grow(dots, 0.02f, out applied).Loops.Count == 2 && applied == 0.02f, "two dots 0.05 apart join at 0.04 each side and stay two at 0.02: " + applied);

            // Nothing to do, and numbers that are not numbers.
            Program.Check(ReferenceEquals(Grow(strip, 0f, out applied), strip) && applied == 0f, "no offset, the same mesh");
            Program.Check(ReferenceEquals(Grow(strip, 0.001f, out applied), strip) && applied == 0f, "less than a hundredth of an em is not made");
            Program.Check(ReferenceEquals(Grow(strip, float.NaN, out _), strip) && ReferenceEquals(Grow(strip, float.PositiveInfinity, out _), strip) && Grow(null, 0.1f, out _) == null, "NaN, infinity and no mesh give what they were given");
            Program.Check(GlyphRaster.Offset(null, 0.1f, Flat).Count == 0 && GlyphRaster.Offset(new List<List<Point2>>(), 0.1f, Flat).Count == 0 && GlyphRaster.Offset(new List<List<Point2>> { Rect(0, 0, 1, 1) }, float.NaN, Flat).Count == 0, "no loops, no line");

            // Apply: bolder, then leaning; the dressing that changes nothing leaves the glyph.
            GlyphMesh both = GlyphStyles.Apply(Mesh(Rect(0, 0, 1, 1)), TextArt.Create(false, 0.1f, 0.5f), 0.5f, Flat);
            Program.Near(both.Area, 1f + 0.4f + (float)Math.PI * 0.01f, 0.006f, "bold and leaning: the area of the bold one, sheared");
            GlyphMesh plain = Mesh(Rect(0, 0, 1, 1));
            Program.Check(ReferenceEquals(GlyphStyles.Apply(plain, TextArt.Plain, 0.5f, Flat), plain) && ReferenceEquals(GlyphStyles.Apply(plain, TextArt.Create(true), 0.5f, Flat), plain) && GlyphStyles.Apply(null, TextArt.Create(false, 0.1f), 0.5f, Flat) == null, "a dressing with no change leaves the glyph");
        }

        private static void RealGlyphs()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            int bucket = TextGlyphs.Bucket(TextPlans.RelativeFlatness(0.3f));
            var clock = Stopwatch.StartNew();
            int whole = 0, reduced = 0, left = 0, total = 0;
            for (int c = 0x4E00; c < 0x4E00 + 500; c++)
            {
                GlyphMesh mesh = TextGlyphs.Build(font, c, bucket);
                if (mesh == null)
                    continue;
                total++;
                foreach (float weight in new[] { TextStyles.BoldWeight, TextStyles.LightWeight })
                {
                    GlyphMesh made = GlyphStyles.Offset(mesh, weight, Flat, out float applied);
                    if (applied == weight)
                        whole++;
                    else if (applied != 0f)
                        reduced++;
                    else
                        left++;
                    if (applied == 0f)
                        continue;
                    int outers = mesh.Loops.Count(l => CutOutline.Area(l) > 0f), holes = mesh.Loops.Count - outers, newOuters = made.Loops.Count(l => CutOutline.Area(l) > 0f), newHoles = made.Loops.Count - newOuters;
                    bool good = made.IsSound(out string why) && !GlyphStyles.Crosses(made.Loops) && (weight > 0f ? made.Area > mesh.Area : made.Area < mesh.Area) && newHoles >= holes && (weight > 0f ? newOuters >= 1 && newOuters <= outers : newOuters >= outers);
                    if (!good)
                        Program.Check(false, "U+" + c.ToString("X") + " at " + weight + " (applied " + applied + "): " + why);
                }
            }

            Console.WriteLine($"  bolder and lighter, {total} characters, {total * 2} offsets in {clock.ElapsedMilliseconds} ms: {whole} whole, {reduced} halved, {left} left as the font has them");
            Program.Check(total > 400 && whole > total * 2 * 8 / 10, "most strokes take the whole offset: " + whole + " of " + total * 2);
            Program.Check(whole + reduced + left == total * 2, "every offset ended one of three ways");

            foreach (char c in "口小永国一人大0ABO的目")
            {
                GlyphMesh mesh = TextGlyphs.Build(font, c, bucket);
                if (mesh == null)
                    continue;
                GlyphMesh bold = GlyphStyles.Offset(mesh, TextStyles.BoldWeight, Flat, out float a1), light = GlyphStyles.Offset(mesh, TextStyles.LightWeight, Flat, out float a2);
                Program.Check(a1 > 0f && bold.Area > mesh.Area * 1.02f, c + ": bolder: " + a1 + ", area " + mesh.Area + " to " + bold.Area);
                Program.Check(a2 < 0f && light.Area < mesh.Area * 0.98f && light.Area > 0.3f * mesh.Area, c + ": lighter: " + a2 + ", area " + mesh.Area + " to " + light.Area);
                int holes = mesh.Loops.Count(l => CutOutline.Area(l) < 0f);
                Program.Check(bold.Loops.Count(l => CutOutline.Area(l) < 0f) >= holes && light.Loops.Count(l => CutOutline.Area(l) < 0f) >= holes, c + ": no hole is lost (" + holes + ")");
            }

            // 口 is a ring: a hole that stays a hole.
            GlyphMesh kou = TextGlyphs.Build(font, '口', bucket);
            Program.Check(kou.Loops.Count == 2 && GlyphStyles.Offset(kou, TextStyles.BoldWeight, Flat, out _).Loops.Count == 2, "口 is a stroke ring and stays one");
        }

        private static TextPlanner Planner(GlyphCache cache) => new TextPlanner(id => id == TextFonts.DefaultId ? TextLayoutTests.BundledFont() : null, cache);

        private static TextPlanJob Job(string text, TextArt art, float height = 0.3f) =>
            new TextPlanJob(text, TextFonts.DefaultId, height, 0f, 1.15f, TextAlign.Centre, TextPlans.RelativeFlatness(height), art);

        private static void Planned()
        {
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            TextPlanner planner = Planner(cache);
            TextArt bold = TextStyles.Art(TextStyles.Bold, false), light = TextStyles.Art(TextStyles.Light, false), italic = TextStyles.Art(TextStyles.Italic, false), both = TextStyles.Art(TextStyles.BoldItalic, false);
            Program.Check(bold.Weight == TextStyles.BoldWeight && bold.Slant == 0f && light.Weight == TextStyles.LightWeight && italic.Slant == TextStyles.ItalicSlant && italic.Weight == 0f && both.Weight == TextStyles.BoldWeight && both.Slant == TextStyles.ItalicSlant, "the styles' numbers");
            Program.Check(new[] { bold, light, italic, both, TextArt.Plain }.Select(a => a.Key).Distinct().Count() == 5 && new[] { bold, light, italic, both }.Select(a => a.GlyphVariant).Distinct().Count() == 4 && TextArt.Plain.GlyphVariant == 0 && TextArt.Create(true).GlyphVariant == 0, "each dressing has its key and its glyph variant, and a plain one has none");
            Program.Check(TextArt.Create(false, 0.02224f).Equals(TextArt.Create(false, 0.0222f)) && TextArt.Create(false, 0.02224f).Weight == 0.0222f && !TextArt.Create(false, 0.0223f).Equals(TextArt.Create(false, 0.0222f)) && TextArt.Create(false, 0.0222f).Key == TextArt.Create(false, 0.02224f).Key, "the numbers of a dressing are kept to ten thousandths");
            Program.Check(TextArt.Create(false, float.NaN).IsPlain && TextArt.Create(false, 0f, float.PositiveInfinity).IsPlain && TextArt.Create(false, 0.0000499f).IsPlain, "a number that is not one, or less than the least, is no dressing");
            Program.Check(TextArt.Create(false, 0.02f).ChangesGlyphs && TextArt.Create(false, 0f, 0.2f).ChangesGlyphs && !TextArt.Create(true).ChangesGlyphs && !TextArt.Plain.ChangesGlyphs, "weight alone and slant alone change the glyphs; a fallback alone does not");
            Program.Check(TextStyles.Art("nothing", false).IsPlain && TextStyles.Art(TextStyles.TwoTone, false).IsPlain && TextStyles.Art(TextStyles.Plain, true).Fallback, "an unknown style, and the two-tone one, leave the glyphs alone");

            const string Text = "永国大小\n口人一";
            TextPlanResult plain = planner.Plan(Job(Text, TextArt.Plain));
            TextPlanResult heavy = planner.Plan(Job(Text, bold)), thin = planner.Plan(Job(Text, light)), lean = planner.Plan(Job(Text, italic)), heavyLean = planner.Plan(Job(Text, both));
            Program.Check(plain.Meshes.Length == heavy.Meshes.Length && plain.Meshes.All(m => m != null), "the same characters in every plan");
            Program.Check(plain.Layout.Glyphs.Zip(heavy.Layout.Glyphs, (a, b) => a.X == b.X && a.Y == b.Y).All(x => x) && plain.Layout.Width == lean.Layout.Width, "the layout does not change with the dressing");
            for (int i = 0; i < plain.Meshes.Length; i++)
            {
                Program.Check(heavy.Meshes[i].Area > plain.Meshes[i].Area && thin.Meshes[i].Area < plain.Meshes[i].Area, "glyph " + i + ": bolder is more ink, lighter is less");
                Program.Near(lean.Meshes[i].Area, plain.Meshes[i].Area, plain.Meshes[i].Area * 1e-4f, "glyph " + i + ": a lean keeps the ink");
                Program.Check(!ReferenceEquals(lean.Meshes[i], plain.Meshes[i]) && !ReferenceEquals(heavy.Meshes[i], plain.Meshes[i]) && !ReferenceEquals(heavyLean.Meshes[i], heavy.Meshes[i]), "glyph " + i + ": each is a mesh of its own");
            }

            Program.Check(heavy.Frame.EffectiveArea > plain.Frame.EffectiveArea && thin.Frame.EffectiveArea < plain.Frame.EffectiveArea, "the frame counts the ink of the dressed letters: " + thin.Frame.EffectiveArea + " < " + plain.Frame.EffectiveArea + " < " + heavy.Frame.EffectiveArea);
            foreach (TextPlanResult plan in new[] { plain, heavy, thin, lean, heavyLean })
            {
                bool held = plan.Frame.Studs.Count >= 8 && plan.Frame.Studs.All(s => plan.Frame.Islands[s.Island].Contains(s.At.X, s.At.Y));
                Program.Check(held, "every stud is in its letter's ink (" + plan.Frame.Studs.Count + " studs)");
                Program.Check(plan.Frame.FloatingIslands == 0, "no letter is left floating");
            }

            // The lean in a plan is about the middle of the font's em box.
            FontOutlines font = TextLayoutTests.BundledFont();
            float pivot = (font.EmTop + font.EmBottom) * 0.5f / font.UnitsPerEm;
            int bucket = TextGlyphs.Bucket(TextPlans.RelativeFlatness(0.3f));
            GlyphMesh seed = TextGlyphs.Build(font, '永', bucket);
            GlyphMesh leaned = lean.Meshes[0];
            Program.Check(leaned.Loops[0].Count == seed.Loops[0].Count && Math.Abs(leaned.Loops[0][0].X - (seed.Loops[0][0].X + (seed.Loops[0][0].Y - pivot) * TextStyles.ItalicSlant)) < 1e-5f && Math.Abs(leaned.Loops[0][3].X - (seed.Loops[0][3].X + (seed.Loops[0][3].Y - pivot) * TextStyles.ItalicSlant)) < 1e-5f, "a leaning glyph in a plan leans about the middle of the em box: " + pivot);

            // The glyphs are kept: a second plan finds the same meshes, and the plain ones are still the font's.
            TextPlanResult again = planner.Plan(Job(Text, bold));
            Program.Check(again.Meshes.Zip(heavy.Meshes, ReferenceEquals).All(x => x), "planned again, the same cached glyphs");
            TextPlanResult plainAgain = planner.Plan(Job(Text, TextArt.Plain));
            Program.Check(plainAgain.Meshes.Zip(plain.Meshes, ReferenceEquals).All(x => x), "and the plain ones were not touched");

            // Dressed with a fallback: the glyph is lifted first, then dressed; each stage has its key.
            TextPlanJob a = Job(Text, bold), b = Job(Text, TextArt.Create(false, TextStyles.BoldWeight)), c = Job(Text, TextArt.Create(true, TextStyles.BoldWeight));
            Program.Check(a.Equals(b) && a.Key == b.Key && !a.Equals(c) && a.Key != c.Key, "the same dressing is the same job, whichever way it was written");

            // Eight threads at once, one cache: the same glyphs in every plan.
            var shared = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            TextPlanner common = Planner(shared);
            var results = new TextPlanResult[8];
            var start = new Barrier(8);
            var threads = new List<Thread>();
            for (int t = 0; t < 8; t++)
            {
                int me = t;
                var thread = new Thread(() =>
                {
                    start.SignalAndWait();
                    results[me] = common.Plan(Job(Text, both));
                });
                threads.Add(thread);
                thread.Start();
            }

            foreach (Thread thread in threads)
                thread.Join();
            Program.Check(results.All(r => r != null && r.Meshes.Zip(results[0].Meshes, ReferenceEquals).All(x => x)), "eight threads planning one dressed text share its glyphs");
            Program.Check(results.All(r => r.Frame.Studs.Count == results[0].Frame.Studs.Count && Math.Abs(r.Frame.EffectiveArea - results[0].Frame.EffectiveArea) < 1e-6f), "and the same frame");
        }

        private static void TemplateStyles()
        {
            GuideTemplate t = TextBoxes.Template;
            GuideField style = t.FieldOf("style"), second = t.FieldOf("glow2"), ret = t.FieldOf("ret");
            Program.Check(style != null && style.Kind == GuideFieldKind.Choice && style.Default == TextStyles.Plain && style.Choices.Select(c => c.Value).SequenceEqual(new[] { "plain", "bold", "light", "italic", "bolditalic", "outline", "neon", "twotone" }), "the style row and its choices");
            Program.Check(style.Choices.All(c => c.Zh.Length > 0 && c.Zh.Length <= 4 && c.En.Length <= 12), "short names");
            Program.Check(second != null && !second.Visible(t.Parse("style=bold")) && second.Visible(t.Parse("style=twotone")), "the side glow colour is for the two-tone style only");
            Program.Check(ret.Visible != null && ret.Visible(t.Parse("style=bold")) && !ret.Visible(t.Parse("style=twotone")), "and the return's own colour is not asked then");
            Program.Check(t.Parse("style=sparkle").Get("style") == TextStyles.Plain, "a style that is not one is plain");

            // The job and the board.
            TextArt art(string spec) => TextBoxes.JobOf(t.Parse(spec), out _).Art;
            Program.Check(art("style=plain;font=bundled").IsPlain && art("style=bold;font=bundled").Weight == TextStyles.BoldWeight && art("style=italic").Slant == TextStyles.ItalicSlant && art("style=bolditalic").Weight > 0f, "the job carries the dressing");
            SignDef def = SignCatalog.ById(TextBoxes.TemplateId);
            SignInstance plain = SignInstances.Of(def, t.Parse("text=永远发光").ToString()), lean = SignInstances.Of(def, t.Parse("text=永远发光;style=italic").ToString());
            float height = plain.Board.Text.Job.LetterHeight;
            Program.Near(lean.Board.Main.W, plain.Board.Main.W + TextStyles.ItalicSlant * height, 1e-4f, "a leaning text has room for the lean: the box is wider by the lean of one height");
            Program.Near(SignInstances.Of(def, t.Parse("text=永远发光;style=bold").ToString()).Board.Main.W, plain.Board.Main.W, 1e-5f, "a bold one needs no more");

            // Two-tone: the return is lit in the second colour; the others have the return's own.
            SignInstance two = SignInstances.Of(def, t.Parse("text=永;style=twotone;glow=blue;glow2=red;ret=white").ToString());
            Program.Check(two.Board.Text.Glow == SolidColour.GlowBlue && two.Board.Text.Return == SolidColour.GlowRed, "two-tone: a blue face and a red lit return");
            SignInstance normal = SignInstances.Of(def, t.Parse("text=永;style=bold;glow=blue;glow2=red;ret=white").ToString());
            Program.Check(normal.Board.Text.Return == SolidColour.White, "any other style: the return's colour as it was set");
            SignParts parts = BuildOnPost(two);
            Program.Check(parts.Solids[(int)SolidColour.GlowBlue] != null && parts.Solids[(int)SolidColour.GlowRed] != null && parts.Solids[(int)SolidColour.GlowRed].VertexCount > parts.Solids[(int)SolidColour.GlowBlue].VertexCount, "built, the glow face and the lit return are two meshes of their own");

            // Bold and leaning letters are built and closed like any other.
            foreach (string spec in new[] { "style=bold", "style=light", "style=italic", "style=bolditalic" })
            {
                SignInstance dressed = SignInstances.Of(def, t.Parse("text=文字灯箱;" + spec).ToString());
                SignParts built = BuildOnPost(dressed);
                MeshBuilder glow = built.Solids[(int)dressed.Board.Text.Glow], back = built.Solids[(int)dressed.Board.Text.Return];
                Program.Check(!built.Provisional && glow != null && glow.VertexCount > 100 && back != null && back.VertexCount > glow.VertexCount, spec + ": built with a face and a return");
                Program.Check(glow.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)) && back.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)), spec + ": every coordinate is a number");
            }
        }

        /// <summary>The editor's picture of each style, written as PNG files when TS_STYLE_PREVIEW is a folder: for looking at; the check is that a picture is made and has the glow in it.</summary>
        private static void Pictures()
        {
            string folder = Environment.GetEnvironmentVariable("TS_STYLE_PREVIEW");
            if (string.IsNullOrEmpty(folder))
                return;
            Directory.CreateDirectory(folder);
            SignDef def = SignCatalog.ById(TextBoxes.TemplateId);
            foreach ((string name, string extra) in StyleSpecs)
            {
                SignInstance instance = SignInstances.Of(def, TextBoxes.Template.Parse(extra).With("text", "永远发光ABC\\n粗细斜体 123").With("h", "0.6").ToString());
                TextPlanResult plan = TextPlans.Get(instance.Board.Text.Job);
                byte[] rgba = TextFacePreview.Raster(instance.Board.Text, plan, new byte[] { 255, 232, 180 }, out int w, out int h);
                Program.Check(rgba != null && w > 16 && h > 16, name + ": a picture is made");
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), Png.Encode(rgba, w, h));
            }
        }

        private static readonly (string Name, string Spec)[] StyleSpecs =
        {
            ("1-plain", "style=plain"), ("2-bold", "style=bold"), ("3-light", "style=light"), ("4-italic", "style=italic"), ("5-bolditalic", "style=bolditalic"), ("6-outline", "style=outline"), ("7-neon", "style=neon")
        };

        private static SignParts BuildOnPost(SignInstance instance)
        {
            SupportDef support = SupportPlanner.Auto(instance.Def);
            var boards = new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } };
            return SignBuilder.BuildAssembly(boards, support, 0f, SupportDetail.Full, default);
        }
    }
}
