using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The points of a barrier's line along a curve, closer where the curve bends (0.12.84; the owner: noise walls "still do not fit the curve" on some ramps): a point every MaxStep (as
    /// MarkingRuns.Line has always made them, 2 m) leaves a sag of c^2 / 8R between two of them, 3.3 cm at a radius of 15 m and 5 cm at 10 m, and a wall's bays and the path a run is saved with
    /// (RunPath.Encode: 32 pieces of at most 3.5 m) are straight between points. Here an interval whose curve leaves its chord by more than Tolerance is cut in two, down to MinStep, so the line is
    /// within Tolerance of the curve whatever the radius; where it does not, the points are exactly those of Line (a straight road and every bend of 17 m radius or more are not changed). The one entry
    /// for a placement and for a line made again after its road moved (the follow maps the old points onto the new curve; Sample takes the new curve's point function). Pure.
    /// </summary>
    public static class BarrierPath
    {
        /// <summary>How far (m) the curve may leave a chord of the line: 3 cm, the sag of a 2 m chord on a radius of 17 m.</summary>
        public const float Tolerance = 0.03f;

        /// <summary>The longest interval between two points (m): MarkingRuns.Step, the spacing every run has had.</summary>
        public const float MaxStep = MarkingRuns.Step;

        /// <summary>The shortest interval cut again (m): a bend tighter than this has no wall.</summary>
        public const float MinStep = 0.25f;

        /// <summary>The deepest cut of one interval (each cut halves it; 2 m to 0.25 m is three).</summary>
        private const int MaxDepth = 3;

        /// <summary>Whether a line of these points is one path (RunPath.Encode writes at most RunPath.MaxPieces pieces and drops the rest): a caller that makes a path from Refine's points keeps the plain map when not.</summary>
        public static bool FitsPath(IList<Vec3> points) => points != null && points.Count >= 2 && points.Count - 1 <= RunPath.MaxPieces;

        /// <summary>The sag (m) of a chord of the length c on a circle of the radius r: how far the middle of the arc is from the middle of the chord.</summary>
        public static float Sag(float chord, float radius)
        {
            if (!(radius > 0f) || !(chord > 0f))
                return 0f;
            float half = chord * 0.5f;
            return half >= radius ? radius : radius - (float)Math.Sqrt(radius * radius - half * half);
        }

        /// <summary>The longest chord (m) a circle of the radius r leaves within tolerance of its arc.</summary>
        public static float ChordFor(float radius, float tolerance) => !(radius > tolerance) ? 0f : 2f * (float)Math.Sqrt(radius * radius - (radius - tolerance) * (radius - tolerance));

        /// <summary>
        /// The points of the curve pointAt gives (a point at a distance along the chain) from s0 to s1 (backwards when s1 is smaller): at the spacing of MarkingRuns.Line (the last interval joined to the
        /// one before it when shorter than half a metre, the end itself last), each interval cut where the curve at its quarter, middle and three-quarter point is farther than tolerance from its chord.
        /// Empty for a length under MarkingRuns.MinLength or over MarkingRuns.MaxLength, or a function that does not give finite points. refineLast false: the interval at the end is not cut (a line whose end
        /// follows the cursor keeps the points before the end at the same places however far the end is: what the tool's cache of the surface under them relies on).
        /// </summary>
        public static List<Vec3> Sample(Func<float, Vec3> pointAt, float s0, float s1, float tolerance = Tolerance, float maxStep = MaxStep, float minStep = MinStep, bool refineLast = true)
        {
            var result = new List<Vec3>();
            if (pointAt == null || float.IsNaN(s0) || float.IsNaN(s1) || float.IsInfinity(s0) || float.IsInfinity(s1) || !(maxStep > 0f))
                return result;
            float length = Math.Abs(s1 - s0);
            if (length < MarkingRuns.MinLength || length > MarkingRuns.MaxLength)
                return result;
            float sign = s1 >= s0 ? 1f : -1f;
            int whole = (int)Math.Floor(length / maxStep);
            var seeds = new List<float>();
            for (int i = 0; i <= whole; i++)
            {
                float d = i * maxStep;
                if (length - d < 0.5f && i > 0)
                    break;
                seeds.Add(d);
            }

            seeds.Add(length);
            Vec3 previous = pointAt(s0);
            result.Add(previous);
            for (int i = 1; i < seeds.Count; i++)
            {
                Vec3 next = pointAt(i == seeds.Count - 1 ? s1 : s0 + sign * seeds[i]);
                if (refineLast || i < seeds.Count - 1)
                    Cut(pointAt, s0, sign, seeds[i - 1], seeds[i], previous, next, tolerance, minStep, 0, result);
                result.Add(next);
                previous = next;
            }

            foreach (Vec3 p in result)
            {
                if (!p.IsFinite)
                    return new List<Vec3>();
            }

            return result;
        }

        /// <summary>
        /// A line already made (the points of a saved path, put in the world) carried over to a new curve by map (the follow of a road that moved: RoadRelation.TryMapPoints for one point), with the
        /// chords the map bends cut again until the new line is within the tolerance of what the map makes of the old one's chords: the same quality as a new placement on the new road. The first and last
        /// points and every point of the old line are kept (mapped); the caller makes sure the result still fits a path (RunPath.MaxPieces pieces) before it writes it, else keeps the plain map.
        /// </summary>
        public static List<Vec3> Refine(IList<Vec3> points, Func<Vec3, Vec3> map, float tolerance = Tolerance, float minStep = MinStep)
        {
            var result = new List<Vec3>();
            if (points == null || points.Count == 0 || map == null)
                return result;
            Vec3 previous = map(points[0]);
            result.Add(previous);
            for (int i = 1; i < points.Count; i++)
            {
                Vec3 from = points[i - 1], to = points[i];
                float length = Vec3.DistanceXZ(from, to);
                Vec3 next = map(to);
                if (length > 2f * minStep)
                    Cut(s => map(Vec3.Lerp(from, to, s / length)), 0f, 1f, 0f, length, previous, next, tolerance, minStep, 0, result);
                result.Add(next);
                previous = next;
            }

            return result;
        }

        /// <summary>Adds the points strictly between a and b (distances from the start) where the curve leaves the chord from pa to pb by more than the tolerance, each half again.</summary>
        private static void Cut(Func<float, Vec3> pointAt, float s0, float sign, float a, float b, Vec3 pa, Vec3 pb, float tolerance, float minStep, int depth, List<Vec3> into)
        {
            if (depth >= MaxDepth || (b - a) * 0.5f < minStep || Deviation(pointAt, s0, sign, a, b, pa, pb) <= tolerance)
                return;
            float m = (a + b) * 0.5f;
            Vec3 pm = pointAt(s0 + sign * m);
            Cut(pointAt, s0, sign, a, m, pa, pm, tolerance, minStep, depth + 1, into);
            into.Add(pm);
            Cut(pointAt, s0, sign, m, b, pm, pb, tolerance, minStep, depth + 1, into);
        }

        /// <summary>The largest distance in the ground plane of the curve's points at a quarter, a half and three quarters of the interval from the chord between its ends.</summary>
        private static float Deviation(Func<float, Vec3> pointAt, float s0, float sign, float a, float b, Vec3 pa, Vec3 pb)
        {
            float dx = pb.X - pa.X, dz = pb.Z - pa.Z, l2 = dx * dx + dz * dz, worst = 0f;
            for (int k = 1; k <= 3; k++)
            {
                Vec3 p = pointAt(s0 + sign * (a + (b - a) * k * 0.25f));
                float t = l2 < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - pa.X) * dx + (p.Z - pa.Z) * dz) / l2));
                float fx = pa.X + dx * t - p.X, fz = pa.Z + dz * t - p.Z;
                worst = Math.Max(worst, (float)Math.Sqrt(fx * fx + fz * fz));
            }

            return worst;
        }
    }
}
