using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The left-turn waiting area (左弯待转区, GB 5768.3 5.5, Fig. 10) laid along the junction's left-turn lanes (mk.waiting, 0.12.0). The axis is the path of the leftmost left-turn lane through the junction (the
    /// curve of the game's junction lane, read when it is placed and kept in the settings); the other waiting lanes lie to its right, a lane's width apart, concentric with it. The area starts start metres
    /// along the axis (past the crosswalk) and is length metres long. What the standard says and how it is drawn:
    /// - two parallel dashed lines to each waiting lane, 15 cm wide, 50 cm on and 50 off (neighbouring lanes share the line between them); the one that continues the left edge line is yellow when the
    ///   leftmost waiting lane is the leftmost lane of its direction, else white; the others white;
    /// - a stop line across the front, across all the waiting lanes (20 cm; 30 or 40 may be chosen); its far edge is where the area ends;
    /// - a white left-turn guide arrow 3 m long in each lane: one with its tail 50 cm after the start (as Fig. 10 has it) and, when the area is 9 m or longer, another with its tip 1 m before the stop line
    ///   (Fig. 10: 100); an area too short for an arrow (under 4.5 m) has none.
    /// How long the area is when it is placed is AutoLength: it runs into the junction until it would come within half a metre of a lane of the traffic coming the other way (the standard: it must not
    /// get in the way of other movements), then stops a metre short, and is never longer than 30 m. Pure.
    /// </summary>
    public static class WaitingArea
    {
        public const float LineWidth = 0.15f, Dash = 0.5f, ArrowLength = 3f, ArrowTail = 0.5f, ArrowBeforeStop = 1f, SecondArrowFrom = 9f;

        /// <summary>The longest area that is made by itself (m), how far it keeps from the other traffic (m) and how much it is then shortened (m).</summary>
        public const float MaxLength = 30f, Clearance = 0.5f, Back = 1f;

        /// <summary>The length of a polyline on the ground.</summary>
        public static float Length(IList<Vec3> line)
        {
            float total = 0f;
            if (line == null)
                return 0f;
            for (int i = 1; i < line.Count; i++)
                total += Vec3.DistanceXZ(line[i - 1], line[i]);
            return total;
        }

        /// <summary>The point at a distance along a polyline (clamped to its ends), and the direction of the polyline there (on the ground, unit).</summary>
        public static Vec3 At(IList<Vec3> line, float s, out Vec3 direction)
        {
            direction = new Vec3(0f, 0f, 1f);
            if (line == null || line.Count == 0)
                return Vec3.Zero;
            if (line.Count == 1)
                return line[0];
            float walked = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                float d = Vec3.DistanceXZ(line[i - 1], line[i]);
                if (d < 1e-6f)
                    continue;
                direction = (line[i] - line[i - 1]).HeadingXZ();
                if (walked + d >= s || i == line.Count - 1)
                {
                    float t = Math.Max(0f, Math.Min(1f, (s - walked) / d));
                    return Vec3.Lerp(line[i - 1], line[i], t);
                }

                walked += d;
            }

            return line[line.Count - 1];
        }

        /// <summary>The part of a polyline from s0 to s1 (m along it, clamped), with a point at every step metres at the most (so that a line offset from it follows its curve).</summary>
        public static List<Vec3> Cut(IList<Vec3> line, float s0, float s1, float step = 0.5f)
        {
            var part = new List<Vec3>();
            float total = Length(line);
            if (line == null || line.Count < 2 || !(total > 0f))
                return part;
            s0 = Math.Max(0f, Math.Min(total, s0));
            s1 = Math.Max(s0, Math.Min(total, s1));
            if (s1 - s0 < 1e-3f)
                return part;
            int pieces = Math.Max(1, (int)Math.Ceiling((s1 - s0) / Math.Max(0.05f, step)));
            for (int i = 0; i <= pieces; i++)
                part.Add(At(line, s0 + (s1 - s0) * i / pieces, out _));
            return part;
        }

        /// <summary>The paint of the area: the lines, the stop line and the arrows, in the axis' frame.</summary>
        public static List<MarkingShape> Shapes(IList<Vec3> axis, int lanes, float laneWidth, float start, float length, bool leftYellow, float stopWidth, bool arrows)
        {
            var shapes = new List<MarkingShape>();
            float total = Length(axis);
            if (axis == null || axis.Count < 2 || !(total > 1f) || lanes < 1 || !(laneWidth > 0f) || float.IsNaN(start) || float.IsNaN(length))
                return shapes;
            lanes = Math.Min(lanes, 6);
            float s0 = Math.Max(0f, Math.Min(total - 1f, start));
            float s1 = Math.Max(s0 + 1f, Math.Min(total, s0 + length));
            List<Vec3> middle = Cut(axis, s0, s1);
            if (middle.Count < 2)
                return shapes;

            // The lines: lane k's left one at (k - 1/2) widths right of the axis (the axis is the middle of the leftmost lane).
            for (int k = 0; k <= lanes; k++)
            {
                MarkingColour colour = k == 0 && leftYellow ? MarkingColour.Yellow : MarkingColour.White;
                shapes.Add(MarkingLines.Line(MarkingFills.Offset(middle, (k - 0.5f) * laneWidth), LineSide.Dashed(LineWidth, colour, Dash, Dash)));
            }

            // The stop line across the front: from the outer side of the left line to that of the right one, its far edge at the end of the area.
            float w = RoadMarkings.NearestOf(RoadMarkings.StopLineWidths, stopWidth);
            Vec3 end = middle[middle.Count - 1];
            Vec3 along = (end - middle[middle.Count - 2]).HeadingXZ();
            Vec3 right = along.RightXZ();
            float from = -0.5f * laneWidth - LineWidth * 0.5f, to = (lanes - 0.5f) * laneWidth + LineWidth * 0.5f;
            var stop = new MarkingShape { Colour = MarkingColour.White };
            var up = new Vec3(0f, 1f, 0f);
            Vec3 a = end + right * from - along * w, b = end + right * from, c = end + right * to, d = end + right * to - along * w;
            stop.Mesh.QuadFacing(stop.Mesh.Vertex(a, up), stop.Mesh.Vertex(b, up), stop.Mesh.Vertex(c, up), stop.Mesh.Vertex(d, up), up);
            shapes.Add(stop);

            // The arrows, in the middle of each lane: one 50 cm after the start, and in an area of 9 m or more one whose tip is 1 m before the stop line.
            float areaLength = s1 - s0;
            if (arrows && areaLength >= ArrowTail + ArrowLength + ArrowBeforeStop)
            {
                var arrowShape = new MarkingShape { Colour = MarkingColour.White };
                MarkingShape arrow = RoadArrows.Shape(ArrowKind.Left, ArrowSize.Line30);
                for (int k = 0; k < lanes; k++)
                {
                    List<Vec3> centre = MarkingFills.Offset(middle, k * laneWidth);
                    float laneLength = Length(centre);
                    var tails = new List<float> { ArrowTail * laneLength / areaLength };
                    if (areaLength >= SecondArrowFrom)
                        tails.Add(laneLength - (ArrowBeforeStop + w) * laneLength / areaLength - ArrowLength);
                    foreach (float tail in tails)
                    {
                        // The arrow is a rigid figure: it is laid along the chord from its tail to its tip.
                        Vec3 p0 = At(centre, tail, out _), p1 = At(centre, tail + ArrowLength, out _);
                        Vec3 chord = (p1 - p0).HeadingXZ();
                        arrowShape.Mesh.Append(arrow.Mesh, p0, (float)Math.Atan2(chord.X, chord.Z));
                    }
                }

                shapes.Add(arrowShape);
            }

            return shapes;
        }

        /// <summary>The distance on the ground between two segments (0 when they cross).</summary>
        public static float SegmentDistance(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            if (Polygon.Segments(a, b, c, d))
                return 0f;
            return Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)), Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
        }

        private static float PointSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
            float t = len2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2));
            float ex = a.X + dx * t - p.X, ez = a.Z + dz * t - p.Z;
            return (float)Math.Sqrt(ex * ex + ez * ez);
        }

        /// <summary>
        /// How long the area is to be (m): from start along the axis, half a metre at a time, the cross section of the area (from half a lane left of the axis to its right side) is checked against the
        /// lanes of the other traffic (each a polyline with its half width); the area ends a metre before the first place where it comes within Clearance of one, and is never longer than MaxLength or
        /// than the axis leaves (3 m short of its end, the exit's crosswalk). At least 0 (no room).
        /// </summary>
        public static float AutoLength(IList<Vec3> axis, int lanes, float laneWidth, float start, IList<IList<Vec3>> others, IList<float> halfWidths, float maxLength = MaxLength)
        {
            float total = Length(axis);
            if (axis == null || axis.Count < 2 || lanes < 1 || !(laneWidth > 0f))
                return 0f;
            float limit = Math.Max(0f, Math.Min(maxLength, total - 3f - start));
            if (others == null || others.Count == 0)
                return limit;
            for (float t = 0f; t <= limit + 1e-4f; t += 0.5f)
            {
                Vec3 p = At(axis, start + t, out Vec3 direction);
                Vec3 right = direction.RightXZ();
                Vec3 left = p + right * (-0.5f * laneWidth), outer = p + right * ((lanes - 0.5f) * laneWidth);
                for (int o = 0; o < others.Count; o++)
                {
                    IList<Vec3> lane = others[o];
                    float half = halfWidths != null && o < halfWidths.Count ? halfWidths[o] : 1.75f;
                    if (lane == null)
                        continue;
                    for (int i = 1; i < lane.Count; i++)
                    {
                        if (SegmentDistance(left, outer, lane[i - 1], lane[i]) - half < Clearance)
                            return Math.Max(0f, t - Back);
                    }
                }
            }

            return limit;
        }

        /// <summary>The axis of the area when it has none (the thumbnail, a draft): a left turn of 12 m radius over a quarter circle, from the origin heading +Z.</summary>
        public static List<Vec3> DefaultAxis()
        {
            var axis = new List<Vec3>();
            const float radius = 12f;
            for (int i = 0; i <= 16; i++)
            {
                double a = i * Math.PI / 32.0;
                axis.Add(new Vec3((float)(-radius * (1.0 - Math.Cos(a))), 0f, (float)(radius * Math.Sin(a))));
            }

            return axis;
        }
    }
}
