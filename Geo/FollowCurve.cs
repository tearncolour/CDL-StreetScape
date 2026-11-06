using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The curve of one road edge as the follow needs it, made once per version of the curve and shared by every item of the edge (0.12.74 follow work): the arc length at 64 equal steps of t (the same table as
    /// ArcTable, so its lengths and its distance-to-t are the same numbers), the ground-plane points of those steps (the coarse search for the nearest point reads them instead of evaluating the curve 33 times),
    /// the nearest point by a table bracket and a safeguarded Newton step instead of a scan and 24 golden-section steps (about 81 evaluations of the curve), and one evaluation that gives the point and the
    /// heading together. Immutable after the constructor: any number of threads read one. Pure.
    /// </summary>
    public sealed class FollowCurve
    {
        public const int Steps = 64;

        private readonly float[] m_S = new float[Steps + 1];
        private readonly float[] m_X = new float[Steps + 1];
        private readonly float[] m_Z = new float[Steps + 1];

        public Bezier3 Curve { get; }

        /// <summary>The longest ground chord between two steps of the table (m): how far a point of the curve can be from the nearest step.</summary>
        public float MaxStep { get; }

        /// <summary>The length in the ground plane (m): ArcTable's number.</summary>
        public float Length => m_S[Steps];

        /// <summary>The curve has a shape to follow: a finite curve of at least RoadRelation.MinLength.</summary>
        public bool IsUsable { get; }

        /// <summary>How many times the curve was evaluated to make this (the table).</summary>
        public const int BuildEvaluations = Steps + 1;

        public FollowCurve(Bezier3 curve)
        {
            Curve = curve;
            float widest = 0f;
            Vec3 previous = curve.Point(0f);
            m_X[0] = previous.X;
            m_Z[0] = previous.Z;
            for (int i = 1; i <= Steps; i++)
            {
                Vec3 current = curve.Point((float)i / Steps);
                float step = Vec3.DistanceXZ(previous, current);
                m_S[i] = m_S[i - 1] + step;
                if (step > widest)
                    widest = step;
                m_X[i] = current.X;
                m_Z[i] = current.Z;
                previous = current;
            }

            MaxStep = widest;
            IsUsable = Length >= RoadRelation.MinLength && curve.A.IsFinite && curve.B.IsFinite && curve.C.IsFinite && curve.D.IsFinite;
        }

        /// <summary>The key of a curve: its 12 numbers hashed in order.</summary>
        public static ulong PrintOf(Bezier3 c)
        {
            var print = new Fingerprint();
            Add(ref print, c.A);
            Add(ref print, c.B);
            Add(ref print, c.C);
            Add(ref print, c.D);
            return print.Value;
        }

        private static void Add(ref Fingerprint print, Vec3 v)
        {
            print.Add(v.X);
            print.Add(v.Y);
            print.Add(v.Z);
        }

        /// <summary>Whether this is the table of that very curve (every number the same): a hit in a cache is checked with it, so that a collision of the hash is a miss and never a wrong table.</summary>
        public bool IsCurve(Bezier3 c) => Same(Curve.A, c.A) && Same(Curve.B, c.B) && Same(Curve.C, c.C) && Same(Curve.D, c.D);

        private static bool Same(Vec3 a, Vec3 b) => BitConverter.DoubleToInt64Bits(a.X) == BitConverter.DoubleToInt64Bits(b.X) && BitConverter.DoubleToInt64Bits(a.Y) == BitConverter.DoubleToInt64Bits(b.Y) && BitConverter.DoubleToInt64Bits(a.Z) == BitConverter.DoubleToInt64Bits(b.Z);

        /// <summary>An angle that is a sum or a difference of two angles in [-pi, pi] brought into that range: RoadRelation.Wrap without the remainder, which is the slow part of it.</summary>
        internal static float Fold(float angle)
        {
            const float twoPi = (float)(2.0 * Math.PI);
            if (angle > (float)Math.PI)
                angle -= twoPi;
            else if (angle < -(float)Math.PI)
                angle += twoPi;
            return angle;
        }

        /// <summary>The parameter at a distance s from the start, clamped: ArcTable.TAt.</summary>
        public float TAt(float s)
        {
            if (s <= 0f)
                return 0f;
            if (s >= Length)
                return 1f;
            int lo = 0, hi = Steps;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (m_S[mid] <= s)
                    lo = mid;
                else
                    hi = mid;
            }

            float span = m_S[hi] - m_S[lo];
            float fraction = span < 1e-9f ? 0f : (s - m_S[lo]) / span;
            return (lo + fraction) / Steps;
        }

        /// <summary>The distance from the start at parameter t, clamped: ArcTable.SAt.</summary>
        public float SAt(float t)
        {
            if (t <= 0f)
                return 0f;
            if (t >= 1f)
                return Length;
            float scaled = t * Steps;
            int i = (int)scaled;
            return m_S[i] + (m_S[i + 1] - m_S[i]) * (scaled - i);
        }

        /// <summary>The point and the ground-plane heading of the curve at t, from one evaluation (Point and Tangent with the chord for a collapsed end, as Bezier3 has them).</summary>
        public void At(float t, out Vec3 point, out Vec3 heading)
        {
            Bezier3 c = Curve;
            double u = 1.0 - t, uu = u * u, tt = t * (double)t;
            double w0 = uu * u, w1 = 3.0 * uu * t, w2 = 3.0 * u * tt, w3 = tt * t;
            point = new Vec3((float)(c.A.X * w0 + c.B.X * w1 + c.C.X * w2 + c.D.X * w3), (float)(c.A.Y * w0 + c.B.Y * w1 + c.C.Y * w2 + c.D.Y * w3), (float)(c.A.Z * w0 + c.B.Z * w1 + c.C.Z * w2 + c.D.Z * w3));
            double d0 = 3.0 * uu, d1 = 6.0 * u * t, d2 = 3.0 * tt;
            double hx = (c.B.X - c.A.X) * d0 + (c.C.X - c.B.X) * d1 + (c.D.X - c.C.X) * d2;
            double hz = (c.B.Z - c.A.Z) * d0 + (c.C.Z - c.B.Z) * d1 + (c.D.Z - c.C.Z) * d2;
            double length = Math.Sqrt(hx * hx + hz * hz);
            if (length < 1e-6)
            {
                // A cusp or a collapsed end: the chord, as Bezier3.Tangent does.
                hx = c.D.X - c.A.X;
                hz = c.D.Z - c.A.Z;
                length = Math.Sqrt(hx * hx + hz * hz);
            }

            heading = length < 1e-6 ? new Vec3(0f, 0f, 1f) : new Vec3((float)(hx / length), 0f, (float)(hz / length));
        }

        /// <summary>
        /// The parameter of the point of the curve nearest to p in the ground plane. The 65 points of the table are scanned (no evaluation of the curve) for the places where the distance is lowest; the true minimum of
        /// each of the two lowest is in the two steps round it, and is found there by Newton's step on the derivative of the squared distance, kept inside the bracket by bisection, from the vertex of the parabola
        /// through the table's three distances, or from hint (a parameter near the answer: the one found for the point before in a run of points) when that lies in the bracket. The second place is looked at only when
        /// its step is as near as a step's length could make it the nearer (a point near the centre of a bend has two). iterations is the number of steps taken (each is one evaluation of the curve and its first two derivatives).
        /// </summary>
        public float ClosestT(Vec3 p, float hint, out int iterations)
        {
            iterations = 0;
            Scan(p, out int best, out float bestD, out int other, out float otherD);
            if (best < 0)
                return 0f;
            float t = Refine(best, bestD, p, hint, ref iterations, out double d2);
            if (other >= 0 && otherD <= bestD + MaxStep * MaxStep * 4f)
            {
                float t2 = Refine(other, otherD, p, -1f, ref iterations, out double d2b);
                if (d2b < d2)
                    t = t2;
            }

            return t;
        }

        private float Refine(int cell, float cellD, Vec3 p, float hint, ref int iterations, out double d2)
        {
            double lo = cell == 0 ? 0.0 : (cell - 1.0) / Steps, hi = cell == Steps ? 1.0 : (cell + 1.0) / Steps;
            double t;
            if (hint >= lo && hint <= hi)
            {
                t = hint;
            }
            else
            {
                // The vertex of the parabola through the three distances round the step: within a few thousandths of a step of the minimum on a gentle curve.
                t = (double)cell / Steps;
                if (cell > 0 && cell < Steps)
                {
                    double a = Distance2(cell - 1, p), b = cellD, c = Distance2(cell + 1, p);
                    double denominator = a - 2.0 * b + c;
                    if (denominator > 1e-12)
                        t = Math.Max(lo, Math.Min(hi, t + 0.5 * (a - c) / denominator / Steps));
                }
            }

            // At an end of the curve the minimum is the end when the distance is still rising away from it.
            if (cell == 0 && Slope(0.0, p, out _, out d2) >= 0.0)
                return 0f;
            if (cell == Steps && Slope(1.0, p, out _, out d2) <= 0.0)
                return 1f;

            d2 = 0.0;
            for (int i = 0; i < 20; i++)
            {
                iterations++;
                double g = Slope(t, p, out double curvature, out d2);
                if (g > 0.0)
                    hi = t;
                else
                    lo = t;
                double next = curvature > 1e-12 ? t - g / curvature : double.NaN;
                if (!(next > lo && next < hi))
                    next = (lo + hi) * 0.5;
                bool done = Math.Abs(next - t) < 1e-8 || hi - lo < 1e-8;
                t = next;
                if (done)
                    break;
            }

            return (float)t;
        }

        /// <summary>RoadRelation.TryMeasure on this curve: the same relation (the same fields and rules), the nearest point found by ClosestT. hint is the parameter of the point before in a run, or negative; t is the parameter found.</summary>
        public bool TryMeasure(Vec3 position, float yaw, float hint, out RoadRelation relation, out float t)
        {
            relation = default;
            t = 0f;
            if (!IsUsable || !position.IsFinite || float.IsNaN(yaw) || float.IsInfinity(yaw))
                return false;
            t = ClosestT(position, hint, out _);
            At(t, out Vec3 on, out Vec3 heading);
            float dx = position.X - on.X, dz = position.Z - on.Z;
            relation = new RoadRelation
            {
                Fraction = SAt(t) / Length,
                Length = Length,
                Along = dx * heading.X + dz * heading.Z,
                Lateral = dx * heading.Z - dz * heading.X,
                Height = position.Y - on.Y,
                Yaw = RoadRelation.Wrap(yaw - RoadRelation.HeadingYaw(heading)),
                PlacedAt = position,
                PlacedYaw = yaw
            };
            return true;
        }

        /// <summary>The distance along this curve at which a relation puts its item (its share of the length, or the distance from the nearer end).</summary>
        public float DistanceOf(float fraction, float measuredLength, FollowMode mode)
        {
            if (mode == FollowMode.NearestEnd)
                return fraction < 0.5f ? fraction * measuredLength : Length - (measuredLength - fraction * measuredLength);
            return fraction * Length;
        }

        /// <summary>RoadRelation.TryApply on this curve.</summary>
        public bool TryApply(in RoadRelation relation, FollowMode mode, out Vec3 position, out float yaw)
        {
            position = default;
            yaw = 0f;
            if (!relation.IsValid || !IsUsable)
                return false;
            return Place(relation.Fraction, relation.Length, relation.Along, relation.Lateral, relation.Height, relation.Yaw, mode, out position, out yaw);
        }

        /// <summary>The place a relation given as numbers (a batch keeps them in arrays) has on this curve; false when it is not a finite place.</summary>
        internal bool Place(float fraction, float length, float along, float lateral, float height, float relativeYaw, FollowMode mode, out Vec3 position, out float yaw)
        {
            At(TAt(DistanceOf(fraction, length, mode)), out Vec3 on, out Vec3 heading);
            position = new Vec3(on.X + heading.X * along + heading.Z * lateral, on.Y + height, on.Z + heading.Z * along - heading.X * lateral);
            yaw = Fold(relativeYaw + RoadRelation.HeadingYaw(heading));
            return position.IsFinite;
        }

        /// <summary>The steps of the table where the distance to p is lowest: the lowest, and the lowest of those that are not beside it (another bend of the curve).</summary>
        private void Scan(Vec3 p, out int best, out float bestD, out int other, out float otherD)
        {
            best = other = -1;
            bestD = otherD = float.MaxValue;
            float before = float.MaxValue, here = float.MaxValue;
            for (int i = 0; i <= Steps + 1; i++)
            {
                float next = float.MaxValue;
                if (i <= Steps)
                {
                    float dx = m_X[i] - p.X, dz = m_Z[i] - p.Z;
                    next = dx * dx + dz * dz;
                }

                int at = i - 1;
                if (at >= 0 && here <= before && here <= next)
                {
                    if (here < bestD)
                    {
                        if (best >= 0 && at - best > 2 && bestD < otherD)
                        {
                            other = best;
                            otherD = bestD;
                        }

                        best = at;
                        bestD = here;
                    }
                    else if (at - best > 2 && here < otherD)
                    {
                        other = at;
                        otherD = here;
                    }
                }

                before = here;
                here = next;
            }
        }

        private double Distance2(int i, Vec3 p)
        {
            double dx = m_X[i] - p.X, dz = m_Z[i] - p.Z;
            return dx * dx + dz * dz;
        }

        /// <summary>Half the derivative of the squared ground distance from p to the curve at t, and half its second derivative.</summary>
        private double Slope(double t, Vec3 p, out double second, out double distance2)
        {
            Bezier3 c = Curve;
            double u = 1.0 - t, uu = u * u, tt = t * t;
            double x = c.A.X * uu * u + c.B.X * 3.0 * uu * t + c.C.X * 3.0 * u * tt + c.D.X * tt * t - p.X;
            double z = c.A.Z * uu * u + c.B.Z * 3.0 * uu * t + c.C.Z * 3.0 * u * tt + c.D.Z * tt * t - p.Z;
            double d1x = 3.0 * (uu * (c.B.X - c.A.X) + 2.0 * u * t * (c.C.X - c.B.X) + tt * (c.D.X - c.C.X));
            double d1z = 3.0 * (uu * (c.B.Z - c.A.Z) + 2.0 * u * t * (c.C.Z - c.B.Z) + tt * (c.D.Z - c.C.Z));
            double d2x = 6.0 * (u * (c.C.X - 2.0 * c.B.X + c.A.X) + t * (c.D.X - 2.0 * c.C.X + c.B.X));
            double d2z = 6.0 * (u * (c.C.Z - 2.0 * c.B.Z + c.A.Z) + t * (c.D.Z - 2.0 * c.C.Z + c.B.Z));
            second = d1x * d1x + d1z * d1z + x * d2x + z * d2z;
            distance2 = x * x + z * z;
            return x * d1x + z * d1z;
        }
    }
}
