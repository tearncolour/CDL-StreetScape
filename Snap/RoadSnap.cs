using System;
using System.Collections.Generic;

namespace CDLC.TS.Snap
{
    public enum SnapSide
    {
        Auto,
        Left,
        Right
    }

    public struct EdgeSnapOptions
    {
        /// <summary>The distance from the road's edge (m): positive outwards (a pole on the verge), negative inwards (a line on the carriageway).</summary>
        public float Margin;

        /// <summary>Which side of the edge's direction; Auto picks the side the cursor is on.</summary>
        public SnapSide Side;

        /// <summary>Traffic keeps to the right (China, as on the game's own roads). A sign faces the traffic that approaches it.</summary>
        public bool RightHandTraffic;

        /// <summary>Added to the height of the centre line (m).</summary>
        public float HeightOffset;

        public static EdgeSnapOptions Default => new EdgeSnapOptions { Margin = 0.5f, Side = SnapSide.Auto, RightHandTraffic = true, HeightOffset = 0f };
    }

    public struct EdgeSnapResult
    {
        /// <summary>The parameter on the centre curve nearest to the cursor.</summary>
        public float T;

        /// <summary>The distance from the start of the curve (m).</summary>
        public float Distance;

        /// <summary>+1 for the right of the edge's direction, -1 for the left.</summary>
        public int Side;

        /// <summary>The signed distance from the centre line (m), positive to the right.</summary>
        public float Lateral;

        public Vec3 Position;

        /// <summary>The ground-plane direction of the edge at T.</summary>
        public Vec3 Heading;

        /// <summary>The direction the front of the item looks at, for an item meant to be seen by approaching traffic.</summary>
        public Vec3 Facing;

        /// <summary>The angle of Facing around Y (radians, yaw 0 faces +Z).</summary>
        public float Yaw;

        /// <summary>The distance from the cursor to the snapped position in the ground plane (m).</summary>
        public float CursorDistance;

        /// <summary>The cursor is too far from the road for the snap to mean anything.</summary>
        public bool TooFar;
    }

    public static class EdgeSnap
    {
        /// <summary>
        /// Snaps a point to the side of a road edge. The edge is its centre curve and the width of its composition; the item stands at the
        /// edge of the road plus the margin, on the side of the cursor, facing the traffic that comes towards it.
        /// </summary>
        public static EdgeSnapResult Solve(ArcTable centre, float roadWidth, Vec3 cursor, EdgeSnapOptions options, float reach = 40f)
        {
            Bezier3 curve = centre.Curve;
            float t = curve.ClosestT(cursor);
            Vec3 onCentre = curve.Point(t);
            Vec3 heading = curve.Tangent(t).HeadingXZ();
            Vec3 right = heading.RightXZ();

            int side;
            if (options.Side == SnapSide.Left)
                side = -1;
            else if (options.Side == SnapSide.Right)
                side = 1;
            else
                side = Vec3.Dot(cursor - onCentre, right) >= 0f ? 1 : -1;

            float lateral = side * (Math.Max(0f, roadWidth) * 0.5f + options.Margin);
            Vec3 position = onCentre + right * lateral;
            position.Y += options.HeightOffset;

            // The traffic on the right of the edge's direction travels along it (right-hand traffic), so it looks back at an item beside it.
            bool trafficTravelsForward = (side > 0) == options.RightHandTraffic;
            Vec3 facing = trafficTravelsForward ? heading * -1f : heading;

            return new EdgeSnapResult
            {
                T = t,
                Distance = centre.SAt(t),
                Side = side,
                Lateral = lateral,
                Position = position,
                Heading = heading,
                Facing = facing,
                Yaw = YawOf(facing),
                CursorDistance = Vec3.DistanceXZ(position, cursor),
                TooFar = Vec3.DistanceXZ(onCentre, cursor) > Math.Max(0f, roadWidth) * 0.5f + reach
            };
        }

        public static float YawOf(Vec3 direction) => (float)Math.Atan2(direction.X, direction.Z);
    }

    /// <summary>A stop line, a give-way line or a crossing at the end of an approach.</summary>
    public struct ApproachPose
    {
        public Vec3 Position;

        /// <summary>The direction of travel of the approaching traffic.</summary>
        public Vec3 Heading;

        public float Yaw;
        public float DistanceFromEnd;
    }

    public static class Approach
    {
        /// <summary>
        /// The pose a distance before the end of an approach curve, at a lateral offset from its centre line (positive right). The curve runs
        /// in the direction of travel and ends at the junction; the setback is measured along it from that end (m).
        /// </summary>
        public static ApproachPose BeforeEnd(ArcTable approach, float setback, float lateral)
        {
            float s = Math.Max(0f, approach.Length - Math.Max(0f, setback));
            float t = approach.TAt(s);
            Vec3 heading = approach.Curve.Tangent(t).HeadingXZ();
            Vec3 position = approach.Curve.Point(t) + heading.RightXZ() * lateral;
            return new ApproachPose { Position = position, Heading = heading, Yaw = EdgeSnap.YawOf(heading), DistanceFromEnd = approach.Length - s };
        }
    }

    /// <summary>A dash of a marking: the interval of the line it covers (m from the start).</summary>
    public struct Dash
    {
        public float Start, End;

        public Dash(float start, float end)
        {
            Start = start;
            End = end;
        }

        public float Length => End - Start;
    }

    public static class Marking
    {
        /// <summary>
        /// The dashes of a broken line of the given length: on, off, on, off... starting at the phase (m into the pattern). A dash cut by the end of
        /// the line is shortened; one shorter than minDash is dropped. A solid line is the pattern with no gap.
        /// </summary>
        public static List<Dash> Dashes(float length, float on, float off, float phase = 0f, float minDash = 0.05f)
        {
            var result = new List<Dash>();
            if (length <= 0f || on <= 0f)
                return result;
            if (off <= 0f)
            {
                result.Add(new Dash(0f, length));
                return result;
            }

            float period = on + off;
            float position = -((phase % period) + period) % period;
            while (position < length)
            {
                float start = Math.Max(0f, position);
                float end = Math.Min(length, position + on);
                if (end - start >= minDash)
                    result.Add(new Dash(start, end));
                position += period;
            }

            return result;
        }

        /// <summary>
        /// The points of a line along a curve between two distances (m) at a lateral offset from the centre (positive right), no further apart than
        /// maxStep. The line follows the curve, so a marking on a bend bends.
        /// </summary>
        public static List<Vec3> Offset(ArcTable curve, float from, float to, float lateral, float maxStep = 1.5f)
        {
            var points = new List<Vec3>();
            from = Math.Max(0f, from);
            to = Math.Min(curve.Length, to);
            if (to <= from)
                return points;

            int segments = Math.Max(1, (int)Math.Ceiling((to - from) / Math.Max(0.05f, maxStep)));
            for (int i = 0; i <= segments; i++)
            {
                float s = from + (to - from) * i / segments;
                float t = curve.TAt(s);
                Vec3 heading = curve.Curve.Tangent(t).HeadingXZ();
                points.Add(curve.Curve.Point(t) + heading.RightXZ() * lateral);
            }

            return points;
        }
    }
}
