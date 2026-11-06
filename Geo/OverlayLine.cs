using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The lines the tool draws over the world while a marking is being placed (0.12.3): a line of a long stretch is a point every one or two metres, and every piece of it is one call of the game's
    /// overlay at every frame (a stretch of 3 km with ten lines was some 15000 calls a frame). What is drawn is the line with the points taken out that it does not need: those within a tolerance of the
    /// straight line between the points kept either side (Douglas-Peucker, in three dimensions). Worked out once when the line changes, not at every frame. Pure.
    /// </summary>
    public static class OverlayLine
    {
        /// <summary>How far the line drawn may be from the line (m): far less than the overlay's width.</summary>
        public const float Tolerance = 0.05f;

        /// <summary>The points of a line that keep it within tolerance (m) of where it is: the first and the last, and those it bends at.</summary>
        public static List<Vec3> Simplify(IList<Vec3> line, float tolerance = Tolerance)
        {
            var kept = new List<Vec3>();
            if (line == null || line.Count == 0)
                return kept;
            int n = line.Count;
            if (n <= 2 || !(tolerance > 0f))
            {
                kept.AddRange(line);
                return kept;
            }

            // Plain arrays: an interface's indexer and the vector's operators cost several times the arithmetic on Unity's Mono.
            var xs = new float[n];
            var ys = new float[n];
            var zs = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vec3 p = line[i];
                xs[i] = p.X;
                ys[i] = p.Y;
                zs[i] = p.Z;
            }

            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var stack = new Stack<long>();
            stack.Push(n - 1);
            float t2 = tolerance * tolerance;
            while (stack.Count > 0)
            {
                long pair = stack.Pop();
                int a = (int)(pair >> 32), b = (int)(pair & 0xffffffffL);
                if (b - a < 2)
                    continue;
                float ax = xs[a], ay = ys[a], az = zs[a];
                float ex = xs[b] - ax, ey = ys[b] - ay, ez = zs[b] - az;
                float l2 = ex * ex + ey * ey + ez * ez;
                float inverse = l2 < 1e-9f ? 0f : 1f / l2;
                int worst = -1;
                float worstD = t2;
                for (int i = a + 1; i < b; i++)
                {
                    float px = xs[i] - ax, py = ys[i] - ay, pz = zs[i] - az;
                    float t = (px * ex + py * ey + pz * ez) * inverse;
                    t = t < 0f ? 0f : t > 1f ? 1f : t;
                    float dx = px - ex * t, dy = py - ey * t, dz = pz - ez * t;
                    float d = dx * dx + dy * dy + dz * dz;
                    if (d > worstD)
                    {
                        worstD = d;
                        worst = i;
                    }
                }

                if (worst < 0)
                    continue;
                keep[worst] = true;
                stack.Push(((long)a << 32) | (uint)worst);
                stack.Push(((long)worst << 32) | (uint)b);
            }

            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                    kept.Add(line[i]);
            }

            return kept;
        }
    }
}
