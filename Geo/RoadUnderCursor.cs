using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The road the camera's ray points at when the game's own ray stopped at the ground above it (0.12.96; the owner, 5 Nov, a road in a cutting between retaining walls, whose logs say: the ray found
    /// the terrain at 408.0 m on every point of the road, the road's surface lay 8.3 to 9.5 m under that and the plane position of the terrain hit was 3 to 47 m off the road; the click was a free
    /// placement). The terrain is nearer to the camera than the road, so the game's ray reports it, and the road under it is open to the sky. The first answer of this class (0.12.95) compared the
    /// terrain hit with the road and refused a road 2.5 m or more under it: the very case. This one takes the camera's ray itself and puts it through each road's surface (the centre line's height there
    /// plus the surface's rise): the place where it meets that surface is the cursor's place on the road, and it counts when it is within the road's half width (and a margin) of the centre line and
    /// at least MinDepth under the ground the ray's own hit was on, so that a road on the ground, which the ray finds itself, is left to it. Of several roads the one the ray meets first. Pure: the
    /// engine side (PlacementTool.Cutting) reads the candidates and leaves out the roads that are covered (a tunnel under the hill is not under the cursor).
    /// </summary>
    public static class RoadUnderCursor
    {
        /// <summary>How far beyond a road's half width (m) the place may be: the edge of the cutting's footway.</summary>
        public const float Margin = 1f;

        /// <summary>How far under the ground hit of the game's ray (m) the road's surface lies at the place: less is a road on the ground, which the ray finds.</summary>
        public const float MinDepth = 1.5f;

        /// <summary>One road segment: its centre line (with the heights), the width of its cross-section, and how far its surface is above the centre line (the composition's surface height).</summary>
        public struct Candidate
        {
            public Bezier3 Curve;
            public float Width, Rise;
        }

        /// <summary>The answer: the index of the candidate, the parameter along its curve, the ray's place on the road's surface, the centre line's point there, and how far beyond the road's edge the place is (negative: inside it).</summary>
        public struct Found
        {
            public int Index;
            public float T;
            public Vec3 Hit, Centre;
            public float Outside;
        }

        /// <summary>
        /// The road the ray (from, to: a segment that goes down) meets inside a road's width, under groundY by MinDepth; false when there is none. The ray meets a road's surface where the ray is
        /// at the height of the surface above the centre line's point: of those points of the curve the one at which the ray is nearest to the centre line in the ground plane is the place.
        /// </summary>
        public static bool Pick(IReadOnlyList<Candidate> candidates, Vec3 from, Vec3 to, float groundY, out Found found, float margin = Margin, float minDepth = MinDepth)
        {
            found = default;
            float fall = from.Y - to.Y;
            if (candidates == null || !(fall > 1e-4f))
                return false;
            bool any = false;
            float bestOutside = float.MaxValue, bestAlong = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate c = candidates[i];
                if (float.IsNaN(c.Width) || c.Width < 0f || float.IsNaN(c.Rise))
                    continue;
                // A curve lies within its control points: a road whose lowest control point has its surface above the depth asked for has no place that is under it (most roads, those on the ground: not sampled).
                if (Math.Min(Math.Min(c.Curve.A.Y, c.Curve.B.Y), Math.Min(c.Curve.C.Y, c.Curve.D.Y)) + c.Rise > groundY - minDepth)
                    continue;
                float t = Nearest(c, from, to, fall, out float distance);
                if (float.IsNaN(t))
                    continue;
                Vec3 centre = c.Curve.Point(t);
                float along = (from.Y - (centre.Y + c.Rise)) / fall;
                Vec3 hit = from + (to - from) * along;
                float outside = distance - c.Width * 0.5f;
                if (outside > margin || groundY - hit.Y < minDepth)
                    continue;
                if (outside < bestOutside || (outside == bestOutside && along < bestAlong))
                {
                    any = true;
                    bestOutside = outside;
                    bestAlong = along;
                    found = new Found { Index = i, T = t, Hit = hit, Centre = centre, Outside = outside };
                }
            }

            return any;
        }

        // The parameter at which the ray, at the height of the road's surface there, is nearest the centre line in the plane (a scan, then a golden-section search round the best sample); NaN when the ray never
        // reaches the height of the surface along the curve (a camera under the road).
        private static float Nearest(Candidate c, Vec3 from, Vec3 to, float fall, out float distance)
        {
            const int samples = 64;
            int best = -1;
            distance = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float d = DistanceAt(c, (float)i / samples, from, to, fall);
                if (d < distance)
                {
                    distance = d;
                    best = i;
                }
            }

            if (best < 0)
                return float.NaN;
            float lo = Math.Max(0f, (best - 1f) / samples), hi = Math.Min(1f, (best + 1f) / samples);
            const float invPhi = 0.6180339887f;
            for (int i = 0; i < 24; i++)
            {
                float m1 = hi - (hi - lo) * invPhi, m2 = lo + (hi - lo) * invPhi;
                if (DistanceAt(c, m1, from, to, fall) < DistanceAt(c, m2, from, to, fall))
                    hi = m2;
                else
                    lo = m1;
            }

            float t = (lo + hi) * 0.5f;
            distance = DistanceAt(c, t, from, to, fall);
            return float.IsInfinity(distance) || float.IsNaN(distance) || distance == float.MaxValue ? float.NaN : t;
        }

        private static float DistanceAt(Candidate c, float t, Vec3 from, Vec3 to, float fall)
        {
            Vec3 centre = c.Curve.Point(t);
            float along = (from.Y - (centre.Y + c.Rise)) / fall;
            if (along < 0f || along > 1f)
                return float.MaxValue;
            return Vec3.DistanceXZ(from + (to - from) * along, centre);
        }
    }
}
