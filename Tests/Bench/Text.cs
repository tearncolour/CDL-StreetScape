using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CDLC.TS.Geo;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What a typed text costs as a lit board's glyphs (BENCH_TEXT=1; the design's section 9, step 1): text of 4, 12 and 36 Chinese characters (and a Latin string when the font has Latin letters) in the
    /// bundled traffic sign font, at letter heights of 0.3, 1 and 3 m and three flatnesses (0.2, 0.5 and 1 mm on a letter of 0.3 m, the same share of the letter at the others): the loops, points,
    /// triangles and vertices of the faces, backs and rims, the time to make them cold and with the cache, the fixed cut of 6 segments to a curve against the adaptive one, and the rear frame found. The font
    /// is the file named by BENCH_FONT, else Fonts/STJTBZ_GB18030.ttf found upward from the program's folder. No Unity.
    /// </summary>
    internal static class Text
    {
        private const string Chinese = "永远发光字体灯箱路牌停公交铁路出租车校快速多乘员人慢学口小回国日目田三十中华人民共和国交通标志专用字体";
        private const string Latin = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        public static void Run()
        {
            string file = Environment.GetEnvironmentVariable("BENCH_FONT");
            if (string.IsNullOrEmpty(file))
            {
                string dir = AppContext.BaseDirectory;
                for (int i = 0; i < 10 && dir != null && file == null; i++)
                {
                    string candidate = Path.Combine(dir, "Fonts", SignFonts.BundledCjk);
                    file = File.Exists(candidate) ? candidate : null;
                    dir = Path.GetDirectoryName(dir);
                }
            }

            if (file == null || !File.Exists(file))
            {
                Console.WriteLine("no font: set BENCH_FONT or run it below the repository");
                return;
            }

            var font = new FontOutlines(File.ReadAllBytes(file));
            Console.WriteLine($"font: {file} (ok {font.Ok}, {font.UnitsPerEm} units to the em, the em box {font.EmBottom} to {font.EmTop})");
            if (!font.Ok)
                return;
            bool latin = font.TryGlyph('A', out GlyphOutline a) && !a.IsEmpty && font.TryGlyph('0', out GlyphOutline z) && !z.IsEmpty;
            Console.WriteLine("Latin letters and figures in the font: " + (latin ? "yes" : "no (only the Chinese strings are measured)"));

            var cases = new List<(string Name, string Text)>
            {
                ("4 CJK", Chinese.Substring(0, 4)),
                ("12 CJK", Chinese.Substring(0, 12)),
                ("36 CJK, 3 lines", Chinese.Substring(0, 12) + "\n" + Chinese.Substring(12, 12) + "\n" + Chinese.Substring(24, 12))
            };
            if (latin)
                cases.Add(("12 Latin", Latin.Substring(0, 12)));

            // Warm up the JIT and the font's own caches so that the first row is not the slow one.
            Measure(font, cases[1].Text, 0.3f, 0.002f);

            Console.WriteLine();
            Console.WriteLine("flatness is a share of the em: 0.2, 0.5 and 1 mm on a letter of 0.3 m are 6.7e-4, 1.7e-3 and 3.3e-3. Times in ms (mean of 5 runs after the first, a laptop's .NET, not the game's Mono).");
            Console.WriteLine("text             height tol(mm)  loops points  tris(face+back+rim) verts   cold   warm | fixed-6: points tris maxdev(mm) | adaptive maxdev(mm) | islands rails studs bridged float fill  frame ms mass(kg)");
            foreach ((string name, string text) in cases)
            {
                foreach (float height in new[] { 0.3f, 1f, 3f })
                {
                    foreach (float tolMm in new[] { 0.2f, 0.5f, 1f })
                    {
                        float relative = tolMm / 300f;
                        Row row = Measure(font, text, height, relative);
                        Console.WriteLine($"{name,-16} {height,5:0.0} {relative * row.MetresPerEm * 1000f,7:0.00} {row.Loops,6} {row.Points,6} {row.Triangles,10} ({row.FaceTris}+{row.FaceTris}+{row.RimTris}) {row.Vertices,7} {row.ColdMs,6:0.00} {row.WarmMs,6:0.00} | {row.FixedPoints,9} {row.FixedTriangles,5} {row.FixedDeviation * 1000f,9:0.00} | {row.Deviation * 1000f,17:0.00} | {row.Islands,7} {row.RailPieces,5} {row.Studs,5} {row.Bridged,7} {row.Floating,5} {row.Fill,4:0.00} {row.FrameMs,8:0.00} {row.Mass,8:0.0}");
                    }
                }
            }

            // The same absolute flatness on the larger letters: what the points cost as a letter grows while the eye stays as close to it.
            Console.WriteLine();
            Console.WriteLine("the same 12 characters at an absolute flatness of 0.5 mm, whatever the letter's height:");
            foreach (float height in new[] { 0.3f, 1f, 3f })
            {
                Row row = Measure(font, cases[1].Text, height, 0.0005f / height);
                Console.WriteLine($"  height {height,3:0.0} m: {row.Loops} loops, {row.Points} points, {row.Triangles} triangles, {row.Vertices} vertices; fixed 6: {row.FixedPoints} points, deviation {row.FixedDeviation * 1000f:0.00} mm; adaptive deviation {row.Deviation * 1000f:0.00} mm; cold {row.ColdMs:0.00} ms");
            }
        }

        private sealed class Row
        {
            public int Loops, Points, Triangles, Vertices, FaceTris, RimTris, FixedPoints, FixedTriangles, Islands, RailPieces, Studs, Bridged, Floating;
            public double ColdMs, WarmMs, FrameMs;
            public float FixedDeviation, Deviation, Fill, Mass, MetresPerEm;
        }

        private static Row Measure(FontOutlines font, string text, float height, float relative)
        {
            var row = new Row();
            TextFace face = TextFace.Of(font);
            TextLayoutResult layout = TextLayout.Layout(text, TextStyle.Of(height), face);
            int bucket = TextGlyphs.Bucket(relative);
            var distinct = new List<int>();
            foreach (PlacedGlyph g in layout.Glyphs)
            {
                if (!g.Missing && !distinct.Contains(g.Codepoint))
                    distinct.Add(g.Codepoint);
            }

            // Cold: a new cache, every glyph made; warm: the same asked again. Five runs each, the first of the cold ones discarded.
            double cold = 0.0, warm = 0.0;
            GlyphCache cache = null;
            for (int run = 0; run < 6; run++)
            {
                cache = new GlyphCache(64L * 1024 * 1024);
                var clock = Stopwatch.StartNew();
                foreach (PlacedGlyph g in layout.Glyphs)
                {
                    if (!g.Missing)
                        cache.GetOrBuild(new GlyphKey(1, g.Codepoint, bucket), () => TextGlyphs.Build(font, g.Codepoint, bucket));
                }

                double c = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                foreach (PlacedGlyph g in layout.Glyphs)
                {
                    if (!g.Missing)
                        cache.GetOrBuild(new GlyphKey(1, g.Codepoint, bucket), () => TextGlyphs.Build(font, g.Codepoint, bucket));
                }

                double w = clock.Elapsed.TotalMilliseconds;
                if (run > 0)
                {
                    cold += c / 5.0;
                    warm += w / 5.0;
                }
            }

            row.ColdMs = cold;
            row.WarmMs = warm;

            // The text's total: every character as laid out (a repeated one counts each time).
            float maxDeviation = 0f, maxFixed = 0f;
            foreach (PlacedGlyph g in layout.Glyphs)
            {
                if (g.Missing)
                    continue;
                GlyphMesh mesh = cache.GetOrBuild(new GlyphKey(1, g.Codepoint, bucket), () => TextGlyphs.Build(font, g.Codepoint, bucket));
                if (mesh == null)
                    continue;
                row.Loops += mesh.Loops.Count;
                row.Points += mesh.LoopPoints;
                row.FaceTris += mesh.FaceTriangles;
                row.RimTris += mesh.RimTriangles;
                row.Triangles += mesh.TriangleCount;
                row.Vertices += mesh.Vertices;
            }

            foreach (int codepoint in distinct)
            {
                // The reference: the same glyph cut at the finest flatness (16 segments to a curve at most, so itself within 1/1000 of a unit of its own curves where they are gentle); the fixed cut and the adaptive one are measured against it, in ems.
                if (!font.TryGlyph(codepoint, 0.01, out GlyphOutline reference) || !font.TryGlyph(codepoint, out GlyphOutline fixedCut) || !font.TryGlyph(codepoint, relative * font.UnitsPerEm, out GlyphOutline adaptive))
                    continue;
                maxFixed = Math.Max(maxFixed, Deviation(reference, fixedCut) / font.UnitsPerEm);
                maxDeviation = Math.Max(maxDeviation, Deviation(reference, adaptive) / font.UnitsPerEm);
                int times = layout.Glyphs.FindAll(g => g.Codepoint == codepoint).Count;
                row.FixedPoints += Points(fixedCut) * times;
                GlyphMesh fixedMesh = TextGlyphs.FromOutline(fixedCut, font.UnitsPerEm);
                if (fixedMesh != null)
                    row.FixedTriangles += fixedMesh.TriangleCount * times;
            }

            row.MetresPerEm = layout.MetresPerEm;
            row.FixedDeviation = maxFixed * layout.MetresPerEm;
            row.Deviation = maxDeviation * layout.MetresPerEm;

            var frameClock = Stopwatch.StartNew();
            FramePlan plan = null;
            for (int i = 0; i < 5; i++)
                plan = LetterFrame.Plan(layout, c => cache.GetOrBuild(new GlyphKey(1, c, bucket), () => TextGlyphs.Build(font, c, bucket)), FrameStyle.For(height));
            row.FrameMs = frameClock.Elapsed.TotalMilliseconds / 5.0;
            row.Islands = plan.Islands.Count;
            row.RailPieces = plan.Rails.Count + plan.Ties.Count;
            row.Studs = plan.Studs.Count;
            row.Bridged = plan.BridgedIslands;
            row.Floating = plan.FloatingIslands;
            row.Fill = plan.FillFactor;
            row.Mass = plan.MassKg;
            return row;
        }

        private static int Points(GlyphOutline g)
        {
            int n = 0;
            foreach (float[] c in g.Contours)
                n += c.Length / 2;
            return n;
        }

        /// <summary>The largest distance of a point of the reference's contours from the same contour of the other cut (font units).</summary>
        private static float Deviation(GlyphOutline reference, GlyphOutline other)
        {
            float worst = 0f;
            for (int i = 0; i < reference.Contours.Count && i < other.Contours.Count; i++)
            {
                float[] r = reference.Contours[i], o = other.Contours[i];
                for (int p = 0; p + 1 < r.Length; p += 2)
                {
                    float best = float.MaxValue;
                    for (int q = 0; q + 1 < o.Length; q += 2)
                    {
                        int next = (q + 2) % o.Length;
                        float dx = o[next] - o[q], dy = o[next + 1] - o[q + 1], l2 = dx * dx + dy * dy;
                        float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((r[p] - o[q]) * dx + (r[p + 1] - o[q + 1]) * dy) / l2));
                        float ex = o[q] + dx * t - r[p], ey = o[q + 1] + dy * t - r[p + 1];
                        best = Math.Min(best, (float)Math.Sqrt(ex * ex + ey * ey));
                    }

                    worst = Math.Max(worst, best);
                }
            }

            return worst;
        }
    }
}
