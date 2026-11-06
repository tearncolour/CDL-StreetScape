using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Glyph meshes made from another glyph mesh (0.12.76): moved, leaning, bolder or lighter. Each returns a new mesh through TextGlyphs.FromLoops (so it is triangulated and counted as any glyph is) and
    /// never changes the one it was given: the cache holds the original for every other use. Pure. The offset of the strokes cannot hold where strokes meet or come closer than the offset: it is
    /// checked, made smaller until it holds, and the glyph is left as the font has it when even the smallest does not.
    /// </summary>
    public static class GlyphStyles
    {
        /// <summary>The smallest offset (em) worth making: below it nothing can be seen, and the glyph is left as it is.</summary>
        public const float MinWeight = 0.0015f;

        /// <summary>How many times an offset that does not hold is halved before the glyph is left alone.</summary>
        public const int Halvings = 3;

        /// <summary>The glyph moved by (dx, dy) em; the same mesh when there is nothing to move.</summary>
        public static GlyphMesh Translate(GlyphMesh mesh, float dx, float dy)
        {
            if (mesh == null || (dx == 0f && dy == 0f))
                return mesh;
            var loops = new List<List<Point2>>(mesh.Loops.Count);
            foreach (List<Point2> loop in mesh.Loops)
            {
                var moved = new List<Point2>(loop.Count);
                foreach (Point2 p in loop)
                    moved.Add(new Point2(p.X + dx, p.Y + dy));
                loops.Add(moved);
            }

            return TextGlyphs.FromLoops(loops, mesh.Advance);
        }

        /// <summary>
        /// The glyph leaning: x moves by slant for every em of height above pivotY (the middle of the em box, so that a leaning line is as far out on the right as on the left of where it stood). An exact
        /// transform: the area, the orientation of every loop and the number of points do not change.
        /// </summary>
        public static GlyphMesh Shear(GlyphMesh mesh, float slant, float pivotY)
        {
            if (mesh == null || slant == 0f)
                return mesh;
            var loops = new List<List<Point2>>(mesh.Loops.Count);
            foreach (List<Point2> loop in mesh.Loops)
            {
                var leaning = new List<Point2>(loop.Count);
                foreach (Point2 p in loop)
                    leaning.Add(new Point2(p.X + (p.Y - pivotY) * slant, p.Y));
                loops.Add(leaning);
            }

            return TextGlyphs.FromLoops(loops, mesh.Advance);
        }

        /// <summary>
        /// The glyph with every stroke wider by amount on each side (negative: narrower), in em, by GlyphRaster (the strokes' outline moved by a distance, with round corners where it opens them). The
        /// shape that is left must be the shape that was: no hole closed (and none opened by a stroke that is gone), no stroke lost when it shrinks, no loops that cross, a sound mesh, more ink for a bolder glyph and
        /// less for a lighter one; strokes that nearly touched may join when it grows. When it is not the amount is halved, up to Halvings times, and the glyph is returned as it was (applied 0) when it never holds. flatness is the
        /// tolerance of the new outline in em. applied is the amount that was used.
        /// </summary>
        public static GlyphMesh Offset(GlyphMesh mesh, float amount, float flatness, out float applied)
        {
            applied = 0f;
            if (mesh == null || float.IsNaN(amount) || float.IsInfinity(amount))
                return mesh;
            CountLoops(mesh.Loops, out int outers, out int holes);
            float d = amount;
            for (int attempt = 0; attempt <= Halvings && Math.Abs(d) >= MinWeight; attempt++, d *= 0.5f)
            {
                List<List<Point2>> loops = GlyphRaster.Offset(mesh.Loops, d, flatness);
                if (loops.Count == 0)
                    continue;
                CountLoops(loops, out int newOuters, out int newHoles);
                // Strokes that were almost touching may join when the glyph grows, and an enclosed gap may then become a hole; but no hole may close, and no stroke may be lost when it shrinks.
                bool kept = newHoles >= holes && (d > 0f ? newOuters >= 1 && newOuters <= outers : newOuters >= outers);
                if (!kept || Crosses(loops))
                    continue;
                GlyphMesh made = TextGlyphs.FromLoops(loops, mesh.Advance);
                if (!made.IsSound(out _) || !(d > 0f ? made.Area > mesh.Area : made.Area < mesh.Area && made.Area > 0.05f * mesh.Area))
                    continue;
                applied = d;
                return made;
            }

            return mesh;
        }

        private static void CountLoops(List<List<Point2>> loops, out int outers, out int holes)
        {
            outers = holes = 0;
            foreach (List<Point2> loop in loops)
            {
                if (CutOutline.Area(loop) > 0f)
                    outers++;
                else
                    holes++;
            }
        }

        /// <summary>
        /// The glyph as a dressing makes it: bolder or lighter first, then leaning about pivotY, then the face made hollow or a neon tube (of the leaning, bolder letter). minBand (em) is the least width of a
        /// band or a tube that a stud can hold.
        /// </summary>
        public static GlyphMesh Apply(GlyphMesh mesh, TextArt art, float pivotY, float flatness, float minBand = 0f)
        {
            if (mesh == null || art == null || !art.ChangesGlyphs)
                return mesh;
            GlyphMesh made = art.Weight != 0f ? Offset(mesh, art.Weight, flatness, out _) : mesh;
            if (art.Slant != 0f)
                made = Shear(made, art.Slant, pivotY);
            switch (art.Effect)
            {
                case TextEffect.Outline: return Outline(made, art.EffectWidth, flatness, minBand);
                case TextEffect.Neon: return Neon(made, art.EffectWidth, flatness, minBand);
                default: return made;
            }
        }

        /// <summary>
        /// The glyph as a band along its outline, width wide (em; at least minBand): the glyph's own loops and, reversed, the loops of the glyph shrunk by the width (GlyphRaster), so that the band has the
        /// edge of the letter exactly and a hole where the shrunk letter is. A stroke narrower than twice the width has no hole: it stays whole. The same glyph when the band does not make a sound mesh.
        /// </summary>
        public static GlyphMesh Outline(GlyphMesh mesh, float width, float flatness, float minBand = 0f)
        {
            if (mesh == null)
                return null;
            float w = Math.Max(width, minBand);
            if (!(w > 0f))
                return mesh;
            List<List<Point2>> inner = GlyphRaster.Offset(mesh.Loops, -w, flatness);
            var loops = new List<List<Point2>>(mesh.Loops.Count + inner.Count);
            foreach (List<Point2> loop in mesh.Loops)
                loops.Add(new List<Point2>(loop));
            foreach (List<Point2> loop in inner)
            {
                var turned = new List<Point2>(loop);
                turned.Reverse();
                loops.Add(turned);
            }

            GlyphMesh band = TextGlyphs.FromLoops(loops, mesh.Advance);
            return band.IsSound(out _) ? band : mesh;
        }

        /// <summary>
        /// The glyph as a neon tube along its outline: a tube of radius (em; at least half of minBand) whose outside is the glyph's edge, so that its line is the outline moved inward by the radius, and, for
        /// the frame and the editor's picture, the band of the tube's footprint (Outline). A glyph whose strokes are all thinner than the tube has no line to lay a tube on: it is the flat band.
        /// </summary>
        public static GlyphMesh Neon(GlyphMesh mesh, float radius, float flatness, float minBand = 0f)
        {
            if (mesh == null)
                return null;
            float r = Math.Max(radius, minBand * 0.5f);
            if (!(r > 0f))
                return mesh;
            GlyphMesh band = Outline(mesh, 2f * r, flatness);
            List<List<Point2>> centre = GlyphRaster.Offset(mesh.Loops, -r, flatness * 1.5f);
            if (centre.Count == 0 || ReferenceEquals(band, mesh))
                return band;
            band.Tube = new NeonTube(centre, r);
            return band;
        }

        /// <summary>
        /// Whether any two edges of the loops cross (a proper crossing: touching at an end is not one, and edges that share a vertex are not compared), by a grid so that a glyph of a thousand points is not
        /// a million tests. An edge is tested against the others in each cell it is in, and each pair only in the first cell they share.
        /// </summary>
        public static bool Crosses(List<List<Point2>> loops)
        {
            int total = 0;
            foreach (List<Point2> loop in loops)
                total += loop.Count;
            if (total < 4)
                return false;
            var ax = new double[total];
            var ay = new double[total];
            var bx = new double[total];
            var by = new double[total];
            var loopOf = new int[total];
            var indexIn = new int[total];
            var sizeOf = new int[total];
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            int k = 0;
            for (int l = 0; l < loops.Count; l++)
            {
                List<Point2> loop = loops[l];
                for (int i = 0; i < loop.Count; i++, k++)
                {
                    Point2 p = loop[i], q = loop[(i + 1) % loop.Count];
                    ax[k] = p.X;
                    ay[k] = p.Y;
                    bx[k] = q.X;
                    by[k] = q.Y;
                    loopOf[k] = l;
                    indexIn[k] = i;
                    sizeOf[k] = loop.Count;
                    minX = Math.Min(minX, Math.Min(p.X, q.X));
                    maxX = Math.Max(maxX, Math.Max(p.X, q.X));
                    minY = Math.Min(minY, Math.Min(p.Y, q.Y));
                    maxY = Math.Max(maxY, Math.Max(p.Y, q.Y));
                }
            }

            int cells = Math.Max(1, Math.Min(64, (int)Math.Sqrt(total)));
            double width = Math.Max(1e-9, maxX - minX), height = Math.Max(1e-9, maxY - minY);
            var grid = new List<int>[cells * cells];
            var x0 = new int[total];
            var y0 = new int[total];
            for (int s = 0; s < total; s++)
            {
                x0[s] = Cell((Math.Min(ax[s], bx[s]) - minX) / width, cells);
                y0[s] = Cell((Math.Min(ay[s], by[s]) - minY) / height, cells);
                int x1 = Cell((Math.Max(ax[s], bx[s]) - minX) / width, cells), y1 = Cell((Math.Max(ay[s], by[s]) - minY) / height, cells);
                for (int cy = y0[s]; cy <= y1; cy++)
                {
                    for (int cx = x0[s]; cx <= x1; cx++)
                    {
                        int at = cy * cells + cx;
                        (grid[at] ?? (grid[at] = new List<int>())).Add(s);
                    }
                }
            }

            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    List<int> cell = grid[cy * cells + cx];
                    if (cell == null)
                        continue;
                    for (int i = 0; i < cell.Count; i++)
                    {
                        for (int j = i + 1; j < cell.Count; j++)
                        {
                            int s = cell[i], t = cell[j];
                            if (Math.Max(x0[s], x0[t]) != cx || Math.Max(y0[s], y0[t]) != cy)
                                continue;
                            if (loopOf[s] == loopOf[t])
                            {
                                int gap = Math.Abs(indexIn[s] - indexIn[t]);
                                if (gap == 1 || gap == sizeOf[s] - 1)
                                    continue;
                            }

                            if (Cross(ax[s], ay[s], bx[s], by[s], ax[t], ay[t], bx[t], by[t]))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        private static int Cell(double fraction, int cells) => Math.Max(0, Math.Min(cells - 1, (int)(fraction * cells)));

        private static bool Cross(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            const double Eps = 1e-12;
            double o1 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax), o2 = (bx - ax) * (dy - ay) - (by - ay) * (dx - ax);
            double o3 = (dx - cx) * (ay - cy) - (dy - cy) * (ax - cx), o4 = (dx - cx) * (by - cy) - (dy - cy) * (bx - cx);
            return ((o1 > Eps && o2 < -Eps) || (o1 < -Eps && o2 > Eps)) && ((o3 > Eps && o4 < -Eps) || (o3 < -Eps && o4 > Eps));
        }
    }
}
