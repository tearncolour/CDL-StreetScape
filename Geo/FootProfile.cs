using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The heights of a barrier's foot where no road surface holds them (0.12.84; the owner, beside a railway: the top and the bottom of a noise wall were wavy and its base had white slivers): a
    /// point with no surface under it takes the terrain's height when that is within SurfaceHeights.MaxGroundStep of the line's own, else the line's own, so beside a track on an embankment a wall
    /// 5.5 m from the centre line stood on the terrain at one point and on the track's height at the next, up to 3 m apart, and the terrain's own grid (3.5 m cells, bilinear) adds a zigzag.
    /// Between free points (those with no surface under them) two limits are made, and a point on a surface is never moved: a ripple (a point NoiseMax or less from the straight line between its
    /// neighbours, more than Wiggle) is brought to Wiggle from it, and the grade between two neighbours is at most MaxGrade, kept by lowering the higher one (a plinth sinks into the ground at a step
    /// and never floats over it; a dip is widened into a valley at the grade, not filled). Where the line leaves a surface for the terrain the step stays. A line whose free points are already within
    /// both is returned as it was, bit for bit. Pure.
    /// </summary>
    public static class FootProfile
    {
        /// <summary>The steepest grade (rise over run) between two free neighbours: 0.5, a slope of 27 degrees; an embankment of 1:1.5 is 0.67 and is followed only as far as this.</summary>
        public const float MaxGrade = 0.5f;

        /// <summary>How far (m) a free point may be from the straight line between its two neighbours: the terrain's cells give a zigzag of this size at most.</summary>
        public const float Wiggle = 0.1f;

        /// <summary>A free point farther than this (m) from the straight line between its neighbours is a step or a corner of the ground, not a ripple, and is left to the grade limit.</summary>
        public const float NoiseMax = 0.5f;

        /// <summary>How many rounds of the ripple limit are made: each hands the excess of a point to its neighbours, so a zigzag dies out.</summary>
        public const int Rounds = 8;

        /// <summary>The line's heights with the free points (held false) limited, in place. Returns whether any point moved. held: a point on a surface (or filled from the surfaces each side), which stays.</summary>
        public static bool Smooth(List<Vec3> line, IList<bool> held, int rounds = Rounds)
        {
            int n = line == null ? 0 : line.Count;
            if (n < 2 || held == null || held.Count != n)
                return false;
            bool anyFree = false;
            for (int i = 0; i < n && !anyFree; i++)
                anyFree = !held[i];
            if (!anyFree)
                return false;
            var along = new float[n];
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                along[i] = i == 0 ? 0f : along[i - 1] + Vec3.DistanceXZ(line[i - 1], line[i]);
                y[i] = line[i].Y;
            }

            bool moved = false;
            for (int round = 0; round < rounds; round++)
            {
                bool changed = false;
                for (int i = 1; i + 1 < n; i++)
                {
                    float run = along[i + 1] - along[i - 1];
                    if (held[i - 1] || held[i] || held[i + 1] || run < 1e-4f)
                        continue;
                    float straight = y[i - 1] + (y[i + 1] - y[i - 1]) * (along[i] - along[i - 1]) / run;
                    float off = y[i] - straight;
                    if (Math.Abs(off) <= Wiggle || Math.Abs(off) > NoiseMax)
                        continue;
                    y[i] = straight + (off > 0f ? Wiggle : -Wiggle);
                    changed = true;
                }

                moved |= changed;
            }

            for (int i = 1; i < n; i++)
                moved |= Lower(y, along, held, i, i - 1);
            for (int i = n - 2; i >= 0; i--)
                moved |= Lower(y, along, held, i, i + 1);
            if (!moved)
                return false;
            for (int i = 0; i < n; i++)
                line[i] = new Vec3(line[i].X, y[i], line[i].Z);

            return true;
        }

        /// <summary>Point i of two free neighbours goes no higher than MaxGrade over their distance above the other.</summary>
        private static bool Lower(float[] y, float[] along, IList<bool> held, int i, int from)
        {
            if (held[i] || held[from])
                return false;
            float top = y[from] + MaxGrade * Math.Abs(along[i] - along[from]);
            if (y[i] <= top)
                return false;
            y[i] = top;
            return true;
        }
    }
}
