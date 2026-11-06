using System;

namespace CDLC.TS.Snap
{
    // Pure geometry of the snapping kernel: no Unity and no game types, so it is compiled into the offline test runner as it is.
    // Conventions are the game's (Unity): left-handed, Y up, forward is +Z, the right of a heading (x, z) is (z, -x), and a yaw is the
    // angle around Y with yaw 0 facing +Z (yaw = atan2(x, z)).

    public struct Vec3
    {
        public float X, Y, Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => a * s;

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>The length in the ground plane; roads are snapped by their plan view.</summary>
        public float LengthXZ => (float)Math.Sqrt(X * X + Z * Z);

        /// <summary>The ground-plane direction of this vector with Y zeroed; (0, 0, 1) when it has no ground-plane length.</summary>
        public Vec3 HeadingXZ()
        {
            float length = LengthXZ;
            return length < 1e-6f ? new Vec3(0f, 0f, 1f) : new Vec3(X / length, 0f, Z / length);
        }

        /// <summary>The right of a ground-plane heading.</summary>
        public Vec3 RightXZ()
        {
            Vec3 h = HeadingXZ();
            return new Vec3(h.Z, 0f, -h.X);
        }

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        public static float DistanceXZ(Vec3 a, Vec3 b) => (a - b).LengthXZ;

        public bool IsFinite => !(float.IsNaN(X) || float.IsInfinity(X) || float.IsNaN(Y) || float.IsInfinity(Y) || float.IsNaN(Z) || float.IsInfinity(Z));

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>A cubic Bezier curve, as the game stores the centre line of a road edge.</summary>
    public readonly struct Bezier3
    {
        public readonly Vec3 A, B, C, D;

        public Bezier3(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }

        /// <summary>A straight line, as a cubic with its control points a third of the way along.</summary>
        public static Bezier3 Line(Vec3 a, Vec3 d) => new Bezier3(a, Vec3.Lerp(a, d, 1f / 3f), Vec3.Lerp(a, d, 2f / 3f), d);

        public Vec3 Point(float t)
        {
            float u = 1f - t;
            return A * (u * u * u) + B * (3f * u * u * t) + C * (3f * u * t * t) + D * (t * t * t);
        }

        public Vec3 Tangent(float t)
        {
            float u = 1f - t;
            Vec3 v = (B - A) * (3f * u * u) + (C - B) * (6f * u * t) + (D - C) * (3f * t * t);
            if (v.LengthXZ >= 1e-6f)
                return v;

            // A cusp or a collapsed end: fall back to the chord so that the heading is never undefined.
            return D - A;
        }

        /// <summary>
        /// The parameter of the point nearest to p in the ground plane: a coarse scan, then a golden-section refinement around the best
        /// sample. Exact for a road edge, which is a gentle non-self-intersecting curve.
        /// </summary>
        public float ClosestT(Vec3 p, int samples = 32)
        {
            if (samples < 2)
                samples = 2;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float distance = Vec3.DistanceXZ(Point((float)i / samples), p);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            float lo = Math.Max(0f, (best - 1f) / samples);
            float hi = Math.Min(1f, (best + 1f) / samples);
            const float invPhi = 0.6180339887f;
            for (int i = 0; i < 24; i++)
            {
                float m1 = hi - (hi - lo) * invPhi;
                float m2 = lo + (hi - lo) * invPhi;
                if (Vec3.DistanceXZ(Point(m1), p) < Vec3.DistanceXZ(Point(m2), p))
                    hi = m2;
                else
                    lo = m1;
            }

            return (lo + hi) * 0.5f;
        }
    }

    /// <summary>The arc length of a curve as a table, to move along it by metres instead of by the curve parameter.</summary>
    public sealed class ArcTable
    {
        private readonly float[] m_Distance;
        private readonly int m_Steps;

        public Bezier3 Curve { get; }

        /// <summary>The length of the curve in the ground plane (m).</summary>
        public float Length => m_Distance[m_Steps];

        public ArcTable(Bezier3 curve, int steps = 64)
        {
            Curve = curve;
            m_Steps = Math.Max(4, steps);
            m_Distance = new float[m_Steps + 1];
            Vec3 previous = curve.Point(0f);
            for (int i = 1; i <= m_Steps; i++)
            {
                Vec3 current = curve.Point((float)i / m_Steps);
                m_Distance[i] = m_Distance[i - 1] + Vec3.DistanceXZ(previous, current);
                previous = current;
            }
        }

        /// <summary>The parameter t at a distance s from the start, clamped to the curve.</summary>
        public float TAt(float s)
        {
            if (s <= 0f)
                return 0f;
            if (s >= Length)
                return 1f;

            int lo = 0;
            int hi = m_Steps;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (m_Distance[mid] <= s)
                    lo = mid;
                else
                    hi = mid;
            }

            float span = m_Distance[hi] - m_Distance[lo];
            float fraction = span < 1e-9f ? 0f : (s - m_Distance[lo]) / span;
            return (lo + fraction) / m_Steps;
        }

        /// <summary>The distance from the start at parameter t.</summary>
        public float SAt(float t)
        {
            if (t <= 0f)
                return 0f;
            if (t >= 1f)
                return Length;

            float scaled = t * m_Steps;
            int i = (int)scaled;
            return m_Distance[i] + (m_Distance[i + 1] - m_Distance[i]) * (scaled - i);
        }
    }
}
