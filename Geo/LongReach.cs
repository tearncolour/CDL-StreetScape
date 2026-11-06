using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The items that are seen from far (0.12.73; the owner: "全封闭隔音墙离远了就消失了"): the noise walls and enclosures, hundreds of metres long and 3 to 9 m high, which are plain to see at kilometres,
    /// where a sign is a speck at 700 m. They are built beyond the far limit (TileTier.Horizon) and have the coarse shapes of the barrier levels (BarrierLod). By the item's type, not by its plan: the
    /// store asks it for every item it takes, on the main thread, and a plan costs a build. Pure.
    /// </summary>
    public static class LongReach
    {
        private static readonly HashSet<uint> s_Hashes = Hashes();

        private static HashSet<uint> Hashes()
        {
            var hashes = new HashSet<uint>();
            foreach (string id in MarkingTemplates.LongReachIds)
                hashes.Add(SignCatalog.HashOf(id));
            return hashes;
        }

        public static bool Is(uint typeHash) => s_Hashes.Contains(typeHash);
    }

    /// <summary>
    /// How much of a barrier is built (0.12.73): the levels of its geometry by the tile's tier (ChunkDetail.Barrier). Level 0 is what the barrier always was and is made as it was; the others leave out
    /// what cannot be seen from the tier's distances and keep its footprint and its height: 1 (Mid, 70 to 250 m) no bolts, rubber, glass frames, cap strips or purlins, simple posts, ribs of fewer
    /// lengths; 2 (Far, 250 to 700 m, and Horizon) no posts, ribs or louvre sheets either, the panels of a wall as long strips, the arch of a roof in a few lengths.
    /// </summary>
    public static class BarrierLod
    {
        public const int Full = 0, Reduced = 1, Coarse = 2;

        /// <summary>The level of a tier's barriers.</summary>
        public static int Of(TileTier tier) => tier == TileTier.Near || tier == TileTier.None ? Full : tier == TileTier.Mid ? Reduced : Coarse;

        /// <summary>How far (m) the thinned line of level 2 may be from the line: 5 cm, 1 px at 100 m on a screen 1080 px high, and the wall is seen from 250 m.</summary>
        public const float Tolerance = 0.05f;

        /// <summary>
        /// The line with the points left out that the others stand in for within tolerance (m, in space: the heights are the ground's), by Douglas and Peucker; a wall drawn from it has a ring of vertices at
        /// each of the points it keeps, so a straight stretch has two. The first and the last three points are kept: the ways the wall goes where it meets the item before it and the one after are the
        /// tangents of the circles through them (Barriers.JoinAheads), and the pieces of a run must still meet. A line of fewer than seven points is as it was.
        /// </summary>
        public static List<Vec3> Simplify(IList<Vec3> line, float tolerance)
        {
            int n = line.Count;
            var result = new List<Vec3>(n);
            if (n < 7)
            {
                result.AddRange(line);
                return result;
            }

            var keep = new bool[n];
            keep[0] = keep[1] = keep[2] = keep[n - 3] = keep[n - 2] = keep[n - 1] = true;
            var stack = new Stack<(int From, int To)>();
            stack.Push((2, n - 3));
            while (stack.Count > 0)
            {
                (int from, int to) = stack.Pop();
                int far = -1;
                float worst = tolerance;
                for (int i = from + 1; i < to; i++)
                {
                    float d = DistanceToSegment(line[i], line[from], line[to]);
                    if (d > worst)
                    {
                        worst = d;
                        far = i;
                    }
                }

                if (far < 0)
                    continue;
                keep[far] = true;
                stack.Push((from, far));
                stack.Push((far, to));
            }

            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                    result.Add(line[i]);
            }

            return result;
        }

        /// <summary>The distance in space from a point to a segment.</summary>
        public static float DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = b - a;
            float length2 = Vec3.Dot(ab, ab);
            float t = length2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, Vec3.Dot(p - a, ab) / length2));
            return (p - (a + ab * t)).Length;
        }
    }
}
