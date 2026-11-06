using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.FollowReference
{
    /// <summary>
    /// A frozen copy of the road-follow code as it was at ca86e49 (RoadRelation, PathFollow, ArcTable and Bezier3.ClosestT), kept in the test project so that the faster code is compared with what it replaced
    /// whatever happens to Snap/Geometry.cs and Geo/RoadRelation.cs later. Test and bench only. Evals counts the curve evaluations (Point or Tangent) the copy makes; it is a plain static for one thread.
    /// </summary>
    internal static class Ref
    {
        public static long Evals;

        public static Vec3 Point(Bezier3 c, float t)
        {
            Evals++;
            return c.Point(t);
        }

        public static Vec3 Tangent(Bezier3 c, float t)
        {
            Evals++;
            return c.Tangent(t);
        }

        public static float ClosestT(Bezier3 c, Vec3 p, int samples = 32)
        {
            if (samples < 2)
                samples = 2;
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float distance = Vec3.DistanceXZ(Point(c, (float)i / samples), p);
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
                if (Vec3.DistanceXZ(Point(c, m1), p) < Vec3.DistanceXZ(Point(c, m2), p))
                    hi = m2;
                else
                    lo = m1;
            }

            return (lo + hi) * 0.5f;
        }
    }

    internal sealed class RefArc
    {
        private readonly float[] m_Distance;
        private const int Steps = 64;

        public Bezier3 Curve { get; }

        public float Length => m_Distance[Steps];

        public RefArc(Bezier3 curve)
        {
            Curve = curve;
            m_Distance = new float[Steps + 1];
            Vec3 previous = Ref.Point(curve, 0f);
            for (int i = 1; i <= Steps; i++)
            {
                Vec3 current = Ref.Point(curve, (float)i / Steps);
                m_Distance[i] = m_Distance[i - 1] + Vec3.DistanceXZ(previous, current);
                previous = current;
            }
        }

        public float TAt(float s)
        {
            if (s <= 0f)
                return 0f;
            if (s >= Length)
                return 1f;
            int lo = 0, hi = Steps;
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
            return (lo + fraction) / Steps;
        }

        public float SAt(float t)
        {
            if (t <= 0f)
                return 0f;
            if (t >= 1f)
                return Length;
            float scaled = t * Steps;
            int i = (int)scaled;
            return m_Distance[i] + (m_Distance[i + 1] - m_Distance[i]) * (scaled - i);
        }
    }

    /// <summary>The relation and its follow as they were: the same fields, the same rules.</summary>
    internal struct RefRelation
    {
        public float Fraction, Length, Along, Lateral, Height, Yaw;
        public Vec3 PlacedAt;
        public float PlacedYaw;

        public bool IsValid => Length >= RoadRelation.MinLength;

        public struct Pair
        {
            public RefArc Old, Now;
        }

        private static bool Usable(RefArc arc) => arc.Length >= RoadRelation.MinLength && arc.Curve.A.IsFinite && arc.Curve.B.IsFinite && arc.Curve.C.IsFinite && arc.Curve.D.IsFinite;

        private static float HeadingYaw(Vec3 heading) => (float)Math.Atan2(heading.X, heading.Z);

        public static bool TryMeasure(RefArc arc, Vec3 position, float yaw, out RefRelation relation)
        {
            relation = default;
            if (arc == null || !position.IsFinite || float.IsNaN(yaw) || float.IsInfinity(yaw) || !Usable(arc))
                return false;
            Bezier3 curve = arc.Curve;
            float t = Ref.ClosestT(curve, position);
            Vec3 on = Ref.Point(curve, t), heading = Ref.Tangent(curve, t).HeadingXZ(), right = heading.RightXZ();
            Vec3 d = position - on;
            relation = new RefRelation
            {
                Fraction = arc.SAt(t) / arc.Length,
                Length = arc.Length,
                Along = Vec3.Dot(new Vec3(d.X, 0f, d.Z), heading),
                Lateral = Vec3.Dot(new Vec3(d.X, 0f, d.Z), right),
                Height = d.Y,
                Yaw = RoadRelation.Wrap(yaw - HeadingYaw(heading)),
                PlacedAt = position,
                PlacedYaw = yaw
            };
            return true;
        }

        public bool TryApply(RefArc arc, FollowMode mode, out Vec3 position, out float yaw)
        {
            position = default;
            yaw = 0f;
            if (!IsValid || arc == null || !Usable(arc))
                return false;
            float s = Fraction * Length;
            if (mode == FollowMode.NearestEnd)
                s = Fraction < 0.5f ? s : arc.Length - (Length - s);
            else
                s = Fraction * arc.Length;
            float t = arc.TAt(s);
            Bezier3 curve = arc.Curve;
            Vec3 on = Ref.Point(curve, t), heading = Ref.Tangent(curve, t).HeadingXZ(), right = heading.RightXZ();
            position = on + heading * Along + right * Lateral + new Vec3(0f, Height, 0f);
            yaw = RoadRelation.Wrap(Yaw + HeadingYaw(heading));
            return position.IsFinite;
        }

        public FollowOutcome Follow(RefArc arc, FollowMode mode, Vec3 position, float yaw, out Vec3 newPosition, out float newYaw)
        {
            newPosition = position;
            newYaw = yaw;
            if (!IsValid)
                return FollowOutcome.Detached;
            if ((position - PlacedAt).Length > RoadRelation.PlacedTolerance || Math.Abs(RoadRelation.Wrap(yaw - PlacedYaw)) > 1e-3f)
                return FollowOutcome.Detached;
            if (!TryApply(arc, mode, out Vec3 at, out float turned))
                return FollowOutcome.Unmeasurable;
            if ((at - position).Length <= RoadRelation.WriteTolerance && Math.Abs(RoadRelation.Wrap(turned - yaw)) <= 1e-4f)
                return FollowOutcome.Unchanged;
            newPosition = at;
            newYaw = turned;
            return FollowOutcome.Moved;
        }

        public static bool TryMapPoints(IList<Pair> roads, FollowMode mode, IList<Vec3> points, List<Vec3> mapped)
        {
            mapped.Clear();
            var distances = new float[roads.Count];
            var heights = new float[roads.Count];
            var candidates = new int[roads.Count];
            foreach (Vec3 point in points)
            {
                // The road choice is the plane distance of 0.12.74 and, of roads that lie over one another, the one at the point's level (RoadLevels, 0.12.86: the one change to this reference).
                int found = 0;
                for (int i = 0; i < roads.Count; i++)
                {
                    if (roads[i].Old == null || !Usable(roads[i].Old) || !point.IsFinite)
                        continue;
                    Bezier3 curve = roads[i].Old.Curve;
                    Vec3 on = Ref.Point(curve, Ref.ClosestT(curve, point, 8));
                    candidates[found] = i;
                    distances[found] = Vec3.DistanceXZ(on, point);
                    heights[found] = on.Y;
                    found++;
                }

                int best = found == 0 ? -1 : candidates[RoadLevels.Pick(distances, heights, found, point.Y)];

                if (best < 0 || !TryMeasure(roads[best].Old, point, 0f, out RefRelation relation) || !relation.TryApply(roads[best].Now, mode, out Vec3 at, out _))
                {
                    mapped.Clear();
                    return false;
                }

                mapped.Add(at);
            }

            return true;
        }
    }

    internal static class RefPathFollow
    {
        public static bool TryRemap(string path, Vec3 position, float yaw, IList<RefRelation.Pair> roads, FollowMode mode, out string newPath, out Vec3 newPosition, out float newYaw)
        {
            newPath = "";
            newPosition = position;
            newYaw = yaw;
            List<Vec3> local = RunPath.Decode(path);
            if (local.Count < 2)
                return false;
            var mapped = new List<Vec3>(local.Count);
            if (!RefRelation.TryMapPoints(roads, mode, MarkingPolygon.ToWorld(local, position, yaw), mapped))
                return false;
            string text = RunPath.Encode(mapped, out float turned);
            if (text.Length == 0)
                return false;
            newPath = text;
            newPosition = mapped[0];
            newYaw = turned;
            return true;
        }
    }
}
