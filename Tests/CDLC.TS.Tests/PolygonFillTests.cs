using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The faster triangulation of 0.12.0 (Polygon.Fill) and the indexed cutting of areas (AreaIndex) against what they replace: the same paint, checked by its area and by where it lies, on random outlines,
    /// glyphs with holes, and bands over areas of many shapes.
    /// </summary>
    internal static class PolygonFillTests
    {
        public static void RunAll()
        {
            Program.Run("fill: the triangles of random outlines cover each outline exactly", RandomOutlines);
            Program.Run("fill: glyphs with holes, as the old triangulation", Glyphs);
            Program.Run("fill: indexed cutting paints what cutting against every triangle paints", IndexedCutting);
        }

        private static float TriangleArea(MeshBuilder mesh, int from = 0)
        {
            double sum = 0;
            for (int i = from; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                sum += MeshBuilder.Cross(b - a, c - a).Y * 0.5;
            }

            return (float)sum;
        }

        private static bool AllUp(MeshBuilder mesh)
        {
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                if (!(MeshBuilder.Cross(b - a, c - a).Y > 0f))
                    return false;
            }

            return true;
        }

        private static bool Inside(IList<Vec3> polygon, float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if ((polygon[i].Z > z) != (polygon[j].Z > z) && x < (polygon[j].X - polygon[i].X) * (z - polygon[i].Z) / (polygon[j].Z - polygon[i].Z) + polygon[i].X)
                    inside = !inside;
            }

            return inside;
        }

        private static void RandomOutlines()
        {
            var rng = new Random(20261020);
            int bad = 0, outside = 0, worse = 0, trials = 0;
            for (int trial = 0; trial < 400; trial++)
            {
                // A star of 5 to 60 corners at random radii (simple: the corners go round the centre in order), and now and then a long thin strip of two offset lines (a run's area).
                var outline = new List<Vec3>();
                if (trial % 5 == 4)
                {
                    int n = 10 + rng.Next(50);
                    var line = new List<Vec3>();
                    for (int i = 0; i < n; i++)
                    {
                        double a = i * 0.04 * (rng.NextDouble() + 0.5);
                        line.Add(new Vec3((float)(60.0 * (1.0 - Math.Cos(a))), 0f, (float)(60.0 * Math.Sin(a))));
                    }

                    outline.AddRange(line);
                    List<Vec3> other = MarkingFills.Offset(line, 1.5f + (float)rng.NextDouble() * 2f);
                    for (int i = other.Count - 1; i >= 0; i--)
                        outline.Add(other[i]);
                    if (!MarkingFills.IsSimple(outline))
                        continue;
                }
                else
                {
                    int n = 5 + rng.Next(56);
                    for (int i = 0; i < n; i++)
                    {
                        double a = i * Math.PI * 2.0 / n, r = 1.0 + rng.NextDouble() * 9.0;
                        outline.Add(new Vec3((float)(r * Math.Cos(a)), 0f, (float)(r * Math.Sin(a))));
                    }
                }

                trials++;
                float area = Math.Abs(Polygon.SignedArea2(outline)) * 0.5f;
                var mesh = new MeshBuilder();
                Polygon.Fill(mesh, outline);
                float filled = TriangleArea(mesh);
                if (Math.Abs(filled - area) > area * 1e-3f + 1e-3f || !AllUp(mesh))
                    bad++;
                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                {
                    Vec3 c = (mesh.Positions[mesh.Indices[i]] + mesh.Positions[mesh.Indices[i + 1]] + mesh.Positions[mesh.Indices[i + 2]]) * (1f / 3f);
                    if (!Inside(outline, c.X, c.Z))
                        outside++;
                }

                var reference = new MeshBuilder();
                PolygonReference.Fill(reference, outline);
                if (Math.Abs(TriangleArea(reference) - area) < Math.Abs(filled - area) - area * 1e-4f)
                    worse++;
            }

            Program.Check(trials > 300, "enough outlines: " + trials);
            Program.Check(bad == 0, "every outline is covered exactly once, all triangles up: " + bad + " of " + trials + " are not");
            Program.Check(outside == 0, "no triangle lies outside its outline: " + outside);
            Program.Check(worse == 0, "never worse than the old triangulation: " + worse);
        }

        private static void Glyphs()
        {
            FontOutlines font = FontLetterTests.Font;
            Program.Check(font != null, "the font is there");
            if (font == null)
                return;
            int checkedGlyphs = 0, differ = 0;
            float worst = 0f;
            foreach (char ch in "0123456789-停公交车出租校铁路慢让行人非机动专用道左转弯待区右直掉头禁止限速学校快多乘员ABCabc")
            {
                if (!font.TryGlyph(ch, out GlyphOutline glyph) || glyph.IsEmpty)
                    continue;
                // Each contour as a polygon: the outer ones filled with their holes, as MarkingLetters does: compare the two triangulations' areas over every contour alone (no holes) and the glyph whole.
                foreach (float[] c in glyph.Contours)
                {
                    var loop = new List<Vec3>(c.Length / 2);
                    for (int i = 0; i + 1 < c.Length; i += 2)
                        loop.Add(new Vec3(c[i], 0f, c[i + 1]));
                    if (loop.Count < 3 || Math.Abs(Polygon.SignedArea2(loop)) < 1f)
                        continue;
                    var fast = new MeshBuilder();
                    var slow = new MeshBuilder();
                    Polygon.Fill(fast, loop);
                    PolygonReference.Fill(slow, loop);
                    float area = Math.Abs(Polygon.SignedArea2(loop)) * 0.5f;
                    float e = Math.Abs(TriangleArea(fast) - area) / area;
                    worst = Math.Max(worst, e);
                    if (e > 1e-3f && Math.Abs(TriangleArea(fast) - TriangleArea(slow)) > area * 1e-3f)
                        differ++;
                    checkedGlyphs++;
                }
            }

            Program.Check(checkedGlyphs > 100, "many contours of glyphs: " + checkedGlyphs);
            Program.Check(differ == 0, "each contour is covered as exactly as before: " + differ + " differ, worst error " + worst);
            // A glyph with holes (the 0 and the 8): the triangles cover the outer contour less its holes.
            foreach (char ch in "08路")
            {
                font.TryGlyph(ch, out GlyphOutline glyph);
                var loops = new List<List<Vec3>>();
                foreach (float[] c in glyph.Contours)
                {
                    var loop = new List<Vec3>();
                    for (int i = 0; i + 1 < c.Length; i += 2)
                        loop.Add(new Vec3(c[i], 0f, c[i + 1]));
                    loops.Add(loop);
                }

                loops.Sort((a, b) => Math.Abs(Polygon.SignedArea2(b)).CompareTo(Math.Abs(Polygon.SignedArea2(a))));
                List<Vec3> outer = loops[0];
                float expected = Math.Abs(Polygon.SignedArea2(outer)) * 0.5f;
                var holes = new List<IList<Vec3>>();
                for (int i = 1; i < loops.Count; i++)
                {
                    if (Math.Sign(Polygon.SignedArea2(loops[i])) != Math.Sign(Polygon.SignedArea2(outer)) && Inside(outer, loops[i][0].X, loops[i][0].Z))
                    {
                        holes.Add(loops[i]);
                        expected -= Math.Abs(Polygon.SignedArea2(loops[i])) * 0.5f;
                    }
                }

                var withHoles = new MeshBuilder();
                Polygon.Fill(withHoles, outer, holes);
                Program.Near(TriangleArea(withHoles), expected, expected * 2e-3f, ch + ": the outer contour less its holes");
                Program.Check(AllUp(withHoles), ch + ": all triangles up");
            }
        }

        private static void IndexedCutting()
        {
            var rng = new Random(7);
            int cases = 0, wrong = 0;
            for (int trial = 0; trial < 120; trial++)
            {
                var outline = new List<Vec3>();
                int n = 6 + rng.Next(40);
                for (int i = 0; i < n; i++)
                {
                    double a = i * Math.PI * 2.0 / n, r = 3.0 + rng.NextDouble() * 20.0;
                    outline.Add(new Vec3((float)(r * Math.Cos(a)), 0f, (float)(r * Math.Sin(a))));
                }

                var index = new AreaIndex(outline);
                var triangles = new MeshBuilder();
                Polygon.Fill(triangles, outline);
                // A band at a random angle through a random place, 45 cm wide.
                double angle = rng.NextDouble() * Math.PI;
                var d = new Vec3((float)Math.Sin(angle), 0f, (float)Math.Cos(angle));
                Vec3 across = d.RightXZ() * 0.225f;
                var o = new Vec3((float)(rng.NextDouble() * 30 - 15), 0f, (float)(rng.NextDouble() * 30 - 15));
                var band = new List<Vec3> { o - d * 60f - across, o + d * 60f - across, o + d * 60f + across, o - d * 60f + across };
                var slow = new MeshBuilder();
                MarkingFills.ClipConvex(slow, band, triangles);
                var fast = new MeshBuilder();
                if (index.BandSpan(o, d, 0.225f, out float s0, out float s1))
                {
                    var shortened = new List<Vec3> { o + d * s0 - across, o + d * s1 - across, o + d * s1 + across, o + d * s0 + across };
                    index.ClipWalk(fast, shortened, o + d * s0, d, s1 - s0, 0.225f);
                }

                cases++;
                if (Math.Abs(TriangleArea(fast) - TriangleArea(slow)) > 1e-3f)
                    wrong++;
            }

            Program.Check(cases == 120 && wrong == 0, "the shortened, indexed band paints the same area as the whole band cut against every triangle: " + wrong + " of " + cases + " differ");
            // The same for whole hatchings: the area painted is that of the old way (band by band against all triangles), on a long strip and on a star.
            var strip = new List<Vec3>();
            var line = new List<Vec3>();
            for (int i = 0; i <= 40; i++)
                line.Add(new Vec3((float)(80.0 * (1.0 - Math.Cos(i * 0.02))), 0f, (float)(80.0 * Math.Sin(i * 0.02))));
            strip.AddRange(line);
            List<Vec3> off = MarkingFills.Offset(line, 2f);
            for (int i = off.Count - 1; i >= 0; i--)
                strip.Add(off[i]);
            var hatched = new MeshBuilder();
            MarkingFills.Hatch(hatched, strip, 0.45f, 1f, 45f);
            float stripArea = Math.Abs(Polygon.SignedArea2(strip)) * 0.5f;
            Program.Check(TriangleArea(hatched) > stripArea * 0.4f && TriangleArea(hatched) < stripArea * 0.5f, "45 cm bands 1 m apart cover 45 % of a long bent strip: " + TriangleArea(hatched) / stripArea);
            foreach (Vec3 p in hatched.Positions)
            {
                if (!Inside(strip, p.X, p.Z) && DistanceToOutline(strip, p) > 2e-3f)
                {
                    Program.Check(false, "a point of the hatching is outside the strip: " + p);
                    break;
                }
            }

            // Indexed as a strip (quads between the two lines, as the strips along a road are), a band across it meets only the triangles near it.
            var index2 = AreaIndex.FromStrip(line, off);
            Program.Check(index2.Ok, "the strip is indexed");
            Program.Near(TriangleArea(index2.Triangles) * Math.Sign(TriangleArea(index2.Triangles)), stripArea, stripArea * 1e-3f, "its quads cover it exactly");
            var probe = new MeshBuilder();
            var d2 = new Vec3(0.7071f, 0f, 0.7071f);
            Vec3 at = (line[20] + off[20]) * 0.5f;
            index2.BandSpan(at, d2, 0.225f, out float a0, out float a1);
            var piece = new List<Vec3> { at + d2 * a0 - d2.RightXZ() * 0.225f, at + d2 * a1 - d2.RightXZ() * 0.225f, at + d2 * a1 + d2.RightXZ() * 0.225f, at + d2 * a0 + d2.RightXZ() * 0.225f };
            index2.ClipWalk(probe, piece, at + d2 * a0, d2, a1 - a0, 0.225f);
            var allTriangles = new MeshBuilder();
            Polygon.Fill(allTriangles, strip);
            Program.Check(index2.Tests < allTriangles.TriangleCount / 3, "a band across the strip is tested against a few of its triangles, not all: " + index2.Tests + " of " + allTriangles.TriangleCount);
            Program.Check(a1 - a0 < 8f, "and is shortened to where it crosses the strip (4 m wide): " + (a1 - a0));
        }

        private static float DistanceToOutline(IList<Vec3> polygon, Vec3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vec3 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                float t = len2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2));
                best = Math.Min(best, Vec3.DistanceXZ(p, new Vec3(a.X + dx * t, 0f, a.Z + dz * t)));
            }

            return best;
        }
    }
}
