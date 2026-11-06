using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The outline of a glyph moved outward or inward by a distance (0.12.76: bolder and lighter letters, the band of a hollow letter, the line of a neon one). The loops are not moved edge by edge (the
    /// corners of a Chinese character are made of dozens of short pieces, and an offset of them turns over and crosses itself wherever the pieces are shorter than the offset): the signed distance of the
    /// glyph is worked out on a grid of PixelsPerEm to the em, exactly (the distance to the nearest edge, its sign from the winding of the loops), and the line where it equals the offset is traced by marching
    /// squares (ContourTrace). Whatever the strokes do (meet, close a hole, vanish), the result is the outline of the shape that is left, with no crossing; corners that the offset opens are round (a
    /// disc of the offset's radius moved along the outline), corners that it closes are sharp. Pure.
    /// </summary>
    public static class GlyphRaster
    {
        /// <summary>The grid: one pixel is 1/256 of an em (a Chinese character is drawn on 256 by 256 of them), about a millimetre on a letter of 0.3 m.</summary>
        public const int PixelsPerEm = 256;

        /// <summary>The least a point may be off the traced line, in pixels: the marching squares of a smooth edge are within a tenth of a pixel, and this keeps the points down to a few for a curve.</summary>
        public const float MinTolerance = 0.3f;

        /// <summary>
        /// The loops of the shape that is the glyph grown by amount (em; negative: shrunk), outer loops counter-clockwise and holes clockwise as the glyph's. flatness is the tolerance of the traced line in
        /// em (the plan's, at least MinTolerance pixels). An empty list when nothing is left.
        /// </summary>
        public static List<List<Point2>> Offset(List<List<Point2>> loops, float amount, float flatness)
        {
            var empty = new List<List<Point2>>();
            if (loops == null || loops.Count == 0 || float.IsNaN(amount) || float.IsInfinity(amount) || float.IsNaN(flatness))
                return empty;
            int edges = 0;
            foreach (List<Point2> loop in loops)
                edges += loop.Count;
            if (edges < 3)
                return empty;
            var ax = new float[edges];
            var ay = new float[edges];
            var bx = new float[edges];
            var by = new float[edges];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int k = 0;
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++, k++)
                {
                    Point2 p = loop[i], q = loop[(i + 1) % loop.Count];
                    ax[k] = p.X;
                    ay[k] = p.Y;
                    bx[k] = q.X;
                    by[k] = q.Y;
                    minX = Math.Min(minX, p.X);
                    maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxY = Math.Max(maxY, p.Y);
                }
            }

            float h = 1f / PixelsPerEm, band = Math.Abs(amount) + 2f * h, pad = Math.Max(0f, amount) + 4f * h;
            float x0 = minX - pad, y0 = minY - pad;
            int width = (int)Math.Ceiling((maxX - minX + 2f * pad) / h) + 2, height = (int)Math.Ceiling((maxY - minY + 2f * pad) / h) + 2;
            if (width > 4 * PixelsPerEm || height > 4 * PixelsPerEm)
                return empty;

            bool[] inside = Winding(ax, ay, bx, by, x0, y0, h, width, height);
            var nearest = new float[width * height];
            for (int i = 0; i < nearest.Length; i++)
                nearest[i] = float.MaxValue;
            for (int s = 0; s < edges; s++)
            {
                int i0 = Math.Max(0, (int)Math.Floor((Math.Min(ax[s], bx[s]) - band - x0) / h)), i1 = Math.Min(width - 1, (int)Math.Ceiling((Math.Max(ax[s], bx[s]) + band - x0) / h));
                int j0 = Math.Max(0, (int)Math.Floor((Math.Min(ay[s], by[s]) - band - y0) / h)), j1 = Math.Min(height - 1, (int)Math.Ceiling((Math.Max(ay[s], by[s]) + band - y0) / h));
                float dx = bx[s] - ax[s], dy = by[s] - ay[s], length2 = dx * dx + dy * dy;
                for (int j = j0; j <= j1; j++)
                {
                    float py = y0 + j * h;
                    for (int i = i0; i <= i1; i++)
                    {
                        float px = x0 + i * h;
                        float t = length2 > 1e-14f ? Math.Max(0f, Math.Min(1f, ((px - ax[s]) * dx + (py - ay[s]) * dy) / length2)) : 0f;
                        float ex = px - (ax[s] + dx * t), ey = py - (ay[s] + dy * t), d2 = ex * ex + ey * ey;
                        if (d2 < nearest[j * width + i])
                            nearest[j * width + i] = d2;
                    }
                }
            }

            // The field, as a plane of bytes the marching squares take: 128 is on the line, a pixel (1/256 em) away on either side is a quarter of the range.
            var alpha = new byte[width * height];
            float scale = 127f / (2f * h);
            for (int j = 0; j < height; j++)
            {
                int row = height - 1 - j;
                for (int i = 0; i < width; i++)
                {
                    int at = j * width + i;
                    float value;
                    if (nearest[at] == float.MaxValue)
                    {
                        value = inside[at] ? 255f : 0f;
                    }
                    else
                    {
                        float signed = (inside[at] ? 1f : -1f) * (float)Math.Sqrt(nearest[at]);
                        value = 128f + (signed + amount) * scale;
                    }

                    alpha[row * width + i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value)));
                }
            }

            float tolerance = Math.Max(MinTolerance, Math.Max(0f, flatness) / h);
            // The node (i, j) is at (x0 + i h, y0 + j h): the image's first pixel centre is half a pixel in from its left, and its top row is the highest j.
            return ContourTrace.Trace(alpha, width, height, false, 128, tolerance, 3f, x0 - 0.5f * h, y0 + (height - 0.5f) * h, h, h);
        }

        /// <summary>Whether each node of the grid is inside the glyph: a row at a time, the winding of the loops at the nodes' height, the nodes between a crossing that makes it non-zero and the one that makes it zero again.</summary>
        private static bool[] Winding(float[] ax, float[] ay, float[] bx, float[] by, float x0, float y0, float h, int width, int height)
        {
            var inside = new bool[width * height];
            var crossings = new List<KeyValuePair<float, int>>();
            for (int j = 0; j < height; j++)
            {
                float y = y0 + j * h;
                crossings.Clear();
                for (int s = 0; s < ax.Length; s++)
                {
                    if ((ay[s] <= y) == (by[s] <= y))
                        continue;
                    float x = ax[s] + (y - ay[s]) * (bx[s] - ax[s]) / (by[s] - ay[s]);
                    crossings.Add(new KeyValuePair<float, int>(x, by[s] > ay[s] ? 1 : -1));
                }

                if (crossings.Count < 2)
                    continue;
                crossings.Sort((p, q) => p.Key.CompareTo(q.Key));
                int winding = 0;
                for (int c = 0; c + 1 < crossings.Count; c++)
                {
                    winding += crossings[c].Value;
                    if (winding == 0)
                        continue;
                    int from = Math.Max(0, (int)Math.Ceiling((crossings[c].Key - x0) / h)), to = Math.Min(width - 1, (int)Math.Ceiling((crossings[c + 1].Key - x0) / h) - 1);
                    for (int i = from; i <= to; i++)
                        inside[j * width + i] = true;
                }
            }

            return inside;
        }
    }
}
