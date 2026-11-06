using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A polyline with fewer points (0.12.73, pure): Ramer-Douglas-Peucker over a line of the ground and the height, for the lines the tool lays (a chain's centre line is sampled every metre, a
    /// marking's line every two, a wall's path has at most 32 chords an item). A point is dropped when it is within tolXz of the chord that replaces it in the plan, and within tolY of it in height, so
    /// a long straight or even stretch keeps its two ends and a bend or a change of grade keeps what it needs; maxChord keeps a point every so many metres where the line is straight. Iterative, so
    /// the number of points cannot overflow the stack; the plain algorithm is O(n^2) in the worst case (Hershberger and Snoeyink, UBC TR-92-07, make it O(n log n) with path hulls), and a line of
    /// 32 or 3000 points does not need that.
    /// </summary>
    public static class LineSimplify
    {
        /// <summary>
        /// The points kept, in order, the first and the last always among them. A line of fewer than three points, with a point that is not finite, or with a tolerance that is not above zero, is returned
        /// as it is. maxChord (m, 3-D) is the longest chord kept where the line has points to split it at; at least 0.1 m.
        /// </summary>
        public static List<Vec3> Simplify(IList<Vec3> line, float tolXz, float tolY, float maxChord)
        {
            var kept = new List<Vec3>(line.Count);
            if (line.Count < 3 || !(tolXz > 0f) || !(tolY > 0f) || float.IsNaN(maxChord) || !AllFinite(line))
            {
                kept.AddRange(line);
                return kept;
            }

            maxChord = Math.Max(0.1f, maxChord);
            var keep = new bool[line.Count];
            keep[0] = keep[line.Count - 1] = true;
            var ranges = new Stack<(int First, int Last)>();
            ranges.Push((0, line.Count - 1));
            while (ranges.Count > 0)
            {
                (int first, int last) = ranges.Pop();
                if (last - first < 2)
                    continue;
                int at = Worst(line, first, last, tolXz, tolY, out bool outside);
                if (!outside && (line[last] - line[first]).Length <= maxChord)
                    continue;
                // Over the tolerance: split at the worst point. Only too long: split in the middle of the points.
                if (!outside)
                    at = (first + last) / 2;
                keep[at] = true;
                ranges.Push((first, at));
                ranges.Push((at, last));
            }

            for (int i = 0; i < line.Count; i++)
            {
                if (keep[i])
                    kept.Add(line[i]);
            }

            return kept;
        }

        private static bool AllFinite(IList<Vec3> line)
        {
            for (int i = 0; i < line.Count; i++)
            {
                if (!line[i].IsFinite)
                    return false;
            }

            return true;
        }

        /// <summary>The point between first and last that is farthest from their chord, by how many times its tolerance it is off (in the plan or in height, whichever is more); outside says it is over 1.</summary>
        private static int Worst(IList<Vec3> line, int first, int last, float tolXz, float tolY, out bool outside)
        {
            Vec3 a = line[first], b = line[last];
            float dx = b.X - a.X, dz = b.Z - a.Z, length2 = dx * dx + dz * dz;
            int worst = first + 1;
            float most = -1f;
            for (int i = first + 1; i < last; i++)
            {
                Vec3 p = line[i];
                // Where along the chord p is (clamped), the plan's distance to that place and the height the chord has there.
                float u = length2 > 1e-12f ? Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length2)) : 0f;
                float ex = a.X + u * dx - p.X, ez = a.Z + u * dz - p.Z;
                float off = (float)Math.Sqrt(ex * ex + ez * ez) / tolXz, high = Math.Abs(p.Y - (a.Y + u * (b.Y - a.Y))) / tolY;
                float excess = Math.Max(off, high);
                if (excess > most)
                {
                    most = excess;
                    worst = i;
                }
            }

            outside = most > 1f;
            return worst;
        }
    }
}
