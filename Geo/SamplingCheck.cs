using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>How far (m) the sampled polylines of a part are from the true curves, at 64 places along each: the adaptive one, the fixed 16-step one, and the two from each other.</summary>
    public struct SamplingMeasure
    {
        public float AdaptivePlan, AdaptiveHeight, FixedPlan, FixedHeight, BetweenPlan, BetweenHeight;

        /// <summary>The plan area (m2) of the part's bands from a dense sampling of the curves (64 steps): what the bands' areas are held to.</summary>
        public double ReferenceArea;
    }

    /// <summary>The corner points of the bands of two samplings of a part: how many were compared, how many are further apart than the limit, and whether the two made another number of bands.</summary>
    public struct CornerResult
    {
        public int Checked, Bad;
        public bool CountDiffers;
    }

    /// <summary>
    /// What the shadow comparison measures of a part made by both samplers (0.12.75, pure; DESIGN-wiring-plan.md B): the deviation of each sampled polyline from the curve it samples, which is the
    /// number the tolerances are about (the fixed one is not the truth: where it is 40 mm off a curve, an adaptive polyline within 10 mm of the curve is 30 mm or more from it, rightly); the corner
    /// points where the parts of an edge meet (the cross-section lines); the plan area of the bands against a dense reference.
    /// </summary>
    public static class SamplingCheck
    {
        public const int Places = 64, AreaSteps = 64;

        public static SamplingMeasure Measure(SamplingProbe probe, int places = Places)
        {
            var m = new SamplingMeasure();
            if (probe == null || places < 2)
                return m;
            foreach (SamplingContext context in probe.Contexts)
            {
                MeasureCurve(context.Left, context.At, places, ref m);
                MeasureCurve(context.Right, context.At, places, ref m);
            }

            m.ReferenceArea = ReferenceArea(probe);
            return m;
        }

        private static void MeasureCurve(in Bezier3 curve, IList<float> adaptive, int places, ref SamplingMeasure m)
        {
            IList<float> fixedAt = PartSampling.FixedAt;
            Vec3[] adaptivePoints = PointsAt(curve, adaptive), fixedPoints = PointsAt(curve, fixedAt);
            int ka = 0, kf = 0;
            for (int i = 0; i < places; i++)
            {
                float u = i / (float)(places - 1);
                Vec3 truth = curve.Point(u), a = Chord(adaptivePoints, adaptive, u, ref ka), f = Chord(fixedPoints, fixedAt, u, ref kf);
                m.AdaptivePlan = Math.Max(m.AdaptivePlan, Vec3.DistanceXZ(a, truth));
                m.AdaptiveHeight = Math.Max(m.AdaptiveHeight, Math.Abs(a.Y - truth.Y));
                m.FixedPlan = Math.Max(m.FixedPlan, Vec3.DistanceXZ(f, truth));
                m.FixedHeight = Math.Max(m.FixedHeight, Math.Abs(f.Y - truth.Y));
                m.BetweenPlan = Math.Max(m.BetweenPlan, Vec3.DistanceXZ(a, f));
                m.BetweenHeight = Math.Max(m.BetweenHeight, Math.Abs(a.Y - f.Y));
            }
        }

        private static Vec3[] PointsAt(in Bezier3 curve, IList<float> at)
        {
            var points = new Vec3[at.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = curve.Point(at[i]);
            return points;
        }

        /// <summary>The point of the polyline through the curve's points (at the places `at`) that lies at the parameter u (u rises from call to call; k is the piece it was in).</summary>
        private static Vec3 Chord(Vec3[] points, IList<float> at, float u, ref int k)
        {
            int last = at.Count - 2;
            while (k < last && at[k + 1] < u)
                k++;
            float span = at[k + 1] - at[k];
            return Vec3.Lerp(points[k], points[k + 1], span > 0f ? Math.Max(0f, Math.Min(1f, (u - at[k]) / span)) : 0f);
        }

        /// <summary>
        /// The corners of each band (the first and the last place, left and right) of two samplings, band by band: both lay one band for each stretch of each curve pair in the same order. Both have the
        /// places 0 and 1, so the corners are the same points; a corner further than limit (m) apart is one that the other sampler would move.
        /// </summary>
        public static CornerResult Corners(IList<SurfaceStrip> a, IList<SurfaceStrip> b, float limit)
        {
            var result = new CornerResult { CountDiffers = a.Count != b.Count };
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                SurfaceStrip x = a[i], y = b[i];
                if (x.Left.Count == 0 || y.Left.Count == 0 || x.Right.Count == 0 || y.Right.Count == 0)
                    continue;
                Count(ref result, x.Left[0], y.Left[0], limit);
                Count(ref result, x.Right[0], y.Right[0], limit);
                Count(ref result, x.Left[x.Left.Count - 1], y.Left[y.Left.Count - 1], limit);
                Count(ref result, x.Right[x.Right.Count - 1], y.Right[y.Right.Count - 1], limit);
            }

            return result;
        }

        private static void Count(ref CornerResult result, Vec3 a, Vec3 b, float limit)
        {
            result.Checked++;
            if (!((a - b).Length <= limit))
                result.Bad++;
        }

        /// <summary>The number of pieces (the steps between two places) of the bands.</summary>
        public static int Pieces(IList<SurfaceStrip> strips)
        {
            int pieces = 0;
            foreach (SurfaceStrip strip in strips)
                pieces += Math.Max(0, strip.Left.Count - 1);
            return pieces;
        }

        /// <summary>The plan area (m2) of the bands: each is the polygon of its left line and its right line.</summary>
        public static double PlanArea(IList<SurfaceStrip> strips)
        {
            double total = 0.0;
            foreach (SurfaceStrip strip in strips)
            {
                double area = Shoelace(strip.Left, strip.Right);
                if (!double.IsNaN(area) && !double.IsInfinity(area))
                    total += area;
            }

            return total;
        }

        /// <summary>The area (m2, in the plan) of the polygon that runs along the line `left` from its first point to its last and back along `right`.</summary>
        private static double Shoelace(IList<Vec3> left, IList<Vec3> right)
        {
            int n = left.Count;
            if (n < 2 || right.Count != n)
                return 0.0;
            double sum = 0.0;
            for (int i = 0; i + 1 < n; i++)
                sum += Cross(left[i], left[i + 1]);
            sum += Cross(left[n - 1], right[n - 1]);
            for (int i = n - 1; i > 0; i--)
                sum += Cross(right[i], right[i - 1]);
            sum += Cross(right[0], left[0]);
            return Math.Abs(sum) * 0.5;
        }

        private static double Cross(Vec3 p, Vec3 q) => (double)p.X * q.Z - (double)q.X * p.Z;

        /// <summary>The plan area (m2) of the bands of a probe's curve pairs and stretches, from AreaSteps equal steps of each curve.</summary>
        public static double ReferenceArea(SamplingProbe probe)
        {
            double total = 0.0;
            int n = AreaSteps;
            var l = new Vec3[n + 1];
            var r = new Vec3[n + 1];
            var a = new Vec3[n + 1];
            var b = new Vec3[n + 1];
            foreach (SamplingContext context in probe.Contexts)
            {
                for (int i = 0; i <= n; i++)
                {
                    l[i] = context.Left.Point(i / (float)n);
                    r[i] = context.Right.Point(i / (float)n);
                }

                foreach (PaintableStretch stretch in context.Across)
                {
                    for (int i = 0; i <= n; i++)
                    {
                        a[i] = Vec3.Lerp(l[i], r[i], stretch.F0);
                        b[i] = Vec3.Lerp(l[i], r[i], stretch.F1);
                    }

                    double area = Shoelace(a, b);
                    if (!double.IsNaN(area) && !double.IsInfinity(area))
                        total += area;
                }
            }

            return total;
        }
    }
}
