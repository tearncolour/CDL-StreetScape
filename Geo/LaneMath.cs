using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Where the lanes of a road are across a gantry or an arm, worked out from the lanes' curves. Pure, so that it is checked offline: the game's reader (RoadReader) only turns the lanes' curves
    /// into Bezier3 and hands them to this. The beam is a straight line along the sign's X axis at the sign's position; a lane is where its curve crosses the vertical plane through that line (the
    /// plane the beam hangs in), which is what a driver passes under, wherever the lane's curve is nearest to the sign's foot.
    /// </summary>
    public static class LaneMath
    {
        /// <summary>The distance within which two lanes are one (m): lanes that overlap in the cross-section (a bus lane drawn over a lane) count once.</summary>
        public const float SameLane = 0.5f;

        /// <summary>
        /// The parameter of the first place where the curve crosses the plane through origin with the normal look (in the ground plane: y is ignored), or -1 when it does not cross. The curve is
        /// sampled at 24 points; the crossing is found in the first interval whose ends are on different sides (or on the plane) and refined by halving.
        /// </summary>
        public static float CrossingT(Bezier3 curve, Vec3 origin, Vec3 look)
        {
            const int samples = 24;
            float previous = Side(curve.Point(0f), origin, look);
            for (int i = 1; i <= samples; i++)
            {
                float t1 = i / (float)samples;
                float current = Side(curve.Point(t1), origin, look);
                if (previous == 0f)
                    return (i - 1) / (float)samples;
                if (previous * current <= 0f)
                {
                    float lo = (i - 1) / (float)samples, hi = t1, sideLo = previous;
                    for (int k = 0; k < 24; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        float sideMid = Side(curve.Point(mid), origin, look);
                        if (sideMid == 0f)
                            return mid;
                        if (sideLo * sideMid < 0f)
                            hi = mid;
                        else
                        {
                            lo = mid;
                            sideLo = sideMid;
                        }
                    }

                    return (lo + hi) * 0.5f;
                }

                previous = current;
            }

            return -1f;
        }

        private static float Side(Vec3 point, Vec3 origin, Vec3 look) => (point.X - origin.X) * look.X + (point.Z - origin.Z) * look.Z;

        /// <summary>
        /// The sign-frame X (m, +X toward the driver's left) of a lane at the plane of the beam: where the curve crosses it, else (the lane does not reach the plane: the edge ends before it) where
        /// it is nearest to origin. look is the way the sign faces in the ground plane (sin yaw, cos yaw); the X axis is (cos yaw, -sin yaw).
        /// </summary>
        public static float LaneX(Bezier3 curve, Vec3 origin, float yaw, out float t, out bool crossed)
        {
            float sin = (float)Math.Sin(yaw), cos = (float)Math.Cos(yaw);
            var look = new Vec3(sin, 0f, cos);
            t = CrossingT(curve, origin, look);
            crossed = t >= 0f;
            if (!crossed)
                t = curve.ClosestT(origin);
            Vec3 at = curve.Point(t);
            return (at.X - origin.X) * cos - (at.Z - origin.Z) * sin;
        }

        /// <summary>Whether a lane faces the sign: its direction of travel at t is against the way the sign looks (the traffic comes toward the sign's face).</summary>
        public static bool FacesSign(Bezier3 curve, float t, float yaw)
        {
            Vec3 heading = curve.Tangent(t);
            return heading.X * (float)Math.Sin(yaw) + heading.Z * (float)Math.Cos(yaw) < 0f;
        }

        /// <summary>
        /// Where a lane is across its edge and which way it runs, by the lane's middle: the signed distance (m) from the edge's centre line to the lane's middle point, positive to the right of the
        /// edge's direction (looking along it, ground plane), and whether the lane runs the way the edge does. The edge's tangent at the point of the edge nearest the lane's middle decides the
        /// direction (the lane's tangent there against it), which holds for a lane that starts part way along the edge, for a short edge on a wide road and for a bend; 0.3.x and 0.4.0 told the
        /// direction from which end of the edge the lane's start was nearer to, which is right only when every lane starts at an end.
        /// </summary>
        public static void Across(Bezier3 edge, Bezier3 lane, out float lateral, out bool forward)
        {
            Vec3 middle = lane.Point(0.5f);
            float t = edge.ClosestT(middle);
            Vec3 on = edge.Point(t);
            Vec3 along = edge.Tangent(t);
            float length = along.LengthXZ;
            float ax = length > 1e-6f ? along.X / length : 0f, az = length > 1e-6f ? along.Z / length : 1f;
            lateral = (middle.X - on.X) * az - (middle.Z - on.Z) * ax;
            Vec3 heading = lane.Tangent(0.5f);
            forward = heading.X * ax + heading.Z * az >= 0f;
        }

        /// <summary>
        /// Where a lane of a road's cross-section is across its segment (0.12.7): a lane x m from the middle of a cross-section width m wide is x / width + 1/2 of the way from the segment's left side to
        /// its right (as the game lays its lanes out: LaneSystem.CreateEdgeLane, which takes 1 m for a narrower width); left and right are the sides at one place along, the lateral is measured right of
        /// the edge's curve there.
        /// </summary>
        public static float CrossSectionLateral(Bezier3 edge, Vec3 left, Vec3 right, float x, float width)
        {
            Vec3 at = Vec3.Lerp(left, right, x / Math.Max(1f, width) + 0.5f);
            float t = edge.ClosestT(at);
            Vec3 on = edge.Point(t), along = edge.Tangent(t);
            float length = along.LengthXZ;
            float ax = length > 1e-6f ? along.X / length : 0f, az = length > 1e-6f ? along.Z / length : 1f;
            return (at.X - on.X) * az - (at.Z - on.Z) * ax;
        }

        /// <summary>
        /// The part of a segment that both its sides have (0.12.7), m along its curve: the sides start at startLeft and startRight and end at endLeft and endRight (each projected on the curve, in either
        /// order). False when it is under minLength.
        /// </summary>
        public static bool CommonSpan(float startLeft, float startRight, float endLeft, float endRight, float minLength, out float s0, out float s1)
        {
            s0 = Math.Max(Math.Min(startLeft, endLeft), Math.Min(startRight, endRight));
            s1 = Math.Min(Math.Max(startLeft, endLeft), Math.Max(startRight, endRight));
            return !float.IsNaN(s0) && !float.IsNaN(s1) && s1 - s0 >= minLength;
        }

        /// <summary>
        /// How many lanes there are, each way: the lanes' lateral places are merged when they are within SameLane of one another and run the same way (a bus lane drawn over a lane is one lane to a
        /// driver). Both lists are the distinct places, from the left of the edge's direction to the right.
        /// </summary>
        public static void CountDistinct(IReadOnlyList<float> laterals, IReadOnlyList<bool> forwards, out List<float> forward, out List<float> backward)
        {
            forward = new List<float>();
            backward = new List<float>();
            for (int i = 0; i < laterals.Count; i++)
            {
                List<float> list = forwards[i] ? forward : backward;
                bool known = false;
                foreach (float other in list)
                    known |= Math.Abs(other - laterals[i]) < SameLane;
                if (!known)
                    list.Add(laterals[i]);
            }

            forward.Sort((a, b) => a.CompareTo(b));
            backward.Sort((a, b) => a.CompareTo(b));
        }

        /// <summary>
        /// The lanes as the editor offers them: lanes within SameLane of one another count once (the first of them), those whose X is less than minX (beside or behind the first post) are left
        /// out, and the rest are sorted from the driver's left to the right (the largest X first).
        /// </summary>
        public static List<float> Arrange(IEnumerable<float> xs, float minX, float maxX = float.PositiveInfinity)
        {
            var result = new List<float>();
            foreach (float x in xs)
            {
                if (x < minX || x > maxX)
                    continue;
                bool known = false;
                foreach (float other in result)
                    known |= Math.Abs(other - x) < SameLane;
                if (!known)
                    result.Add(x);
            }

            result.Sort((a, b) => b.CompareTo(a));
            return result;
        }

        /// <summary>
        /// One car lane that faces a sign, as the blockage rule needs it: where it is across the beam (the sign-frame X, +X toward the driver's left), how far along the lane the beam is (0 to 1), the
        /// lane's length (m), and the game's blockage bytes (CarLane.m_BlockageStart/End: 0 to 255 along the lane in its own direction; a lane with none has start 255 and end 0).
        /// </summary>
        public struct LaneSample
        {
            public float X, T, Length;
            public byte BlockStart, BlockEnd;
        }

        /// <summary>
        /// How far ahead of the beam a lane is blocked (m along the lane), or -1: the blockage's bounds are the bytes over 255, a blockage that ends before the beam (the traffic has passed it) is not
        /// ahead, one that covers the beam is at 0, one that starts after it is (start - t) x length ahead. No blockage is start above end (the game's value for none is 255 and 0).
        /// </summary>
        public static float BlockedAhead(LaneSample lane)
        {
            if (lane.BlockStart > lane.BlockEnd)
                return -1f;
            float start = lane.BlockStart / 255f, end = lane.BlockEnd / 255f;
            if (end < lane.T)
                return -1f;
            if (start <= lane.T)
                return 0f;
            return (start - lane.T) * Math.Max(0f, lane.Length);
        }

        /// <summary>
        /// The lanes as a lane screen counts them, from the driver's left: the same lanes and the same order as Arrange (lanes within SameLane of one another are one, the first of them stands for the
        /// group; those with X under minX are left out), and for each lane how far ahead it is blocked (BlockedAhead, the nearest of the lanes that were merged) or -1.
        /// </summary>
        public static List<float> BlockedByLane(IEnumerable<LaneSample> lanes, float minX)
        {
            var groups = new List<KeyValuePair<float, float>>();
            foreach (LaneSample lane in lanes)
            {
                if (lane.X < minX)
                    continue;
                float blocked = BlockedAhead(lane);
                int found = -1;
                for (int i = 0; i < groups.Count && found < 0; i++)
                {
                    if (Math.Abs(groups[i].Key - lane.X) < SameLane)
                        found = i;
                }

                if (found < 0)
                {
                    groups.Add(new KeyValuePair<float, float>(lane.X, blocked));
                }
                else if (blocked >= 0f && (groups[found].Value < 0f || blocked < groups[found].Value))
                {
                    groups[found] = new KeyValuePair<float, float>(groups[found].Key, blocked);
                }
            }

            groups.Sort((a, b) => b.Key.CompareTo(a.Key));
            var result = new List<float>(groups.Count);
            foreach (KeyValuePair<float, float> group in groups)
                result.Add(group.Value);
            return result;
        }
    }
}
