using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The filled areas of GB 5768.3-2025 drawn on any shape: the channelising island of 6.6 (导流线) on a polygon the player draws, the gore of an exit or an entry (线65, Fig. 94) between the two carriageways
    /// that part, and a hatched strip along a road. The lines inside follow the area's own axis: the V lines (V 型线, 45 wide, 1 m apart, each arm at 45 degrees to the way the traffic runs, the point of
    /// the V toward the traffic that comes, Fig. 94 a and b both) are placed along the axis and turned with it where it bends, so a gore on a curving ramp is filled as the figure shows and not with a fixed
    /// family of lines. Every piece is cut exactly to the area (MarkingFills.ClipConvex). Frame: X right, Z the way the traffic runs, Y 0, triangles up. Pure.
    /// </summary>
    public static class MarkingAreas
    {
        /// <summary>6.12.2 and Fig. 94: the edge lines of a gore are 20 wide, its V lines 45 wide and 100 apart.</summary>
        public const float GoreEdge = 0.2f, VWidth = 0.45f, VPitch = 1f;

        private static readonly float Root2 = (float)Math.Sqrt(2.0);

        /// <summary>The lengths along a line of points from its first (m, on the ground), one for each point.</summary>
        private static float[] Lengths(IList<Vec3> line)
        {
            var at = new float[line.Count];
            for (int i = 1; i < line.Count; i++)
                at[i] = at[i - 1] + Vec3.DistanceXZ(line[i - 1], line[i]);
            return at;
        }

        /// <summary>The point and the heading of a line at a distance along it; before its start and after its end the line goes on straight along its first and last piece.</summary>
        public static void PointAt(IList<Vec3> line, float[] at, float s, out Vec3 point, out Vec3 heading)
        {
            int n = line.Count;
            if (s <= 0f || n < 2)
            {
                heading = n < 2 ? new Vec3(0f, 0f, 1f) : (line[1] - line[0]).HeadingXZ();
                point = line[0] + heading * s;
                return;
            }

            if (s >= at[n - 1])
            {
                heading = (line[n - 1] - line[n - 2]).HeadingXZ();
                point = line[n - 1] + heading * (s - at[n - 1]);
                return;
            }

            int i = 1;
            while (i < n - 1 && at[i] < s)
                i++;
            float span = at[i] - at[i - 1];
            float f = span < 1e-6f ? 0f : (s - at[i - 1]) / span;
            point = Vec3.Lerp(line[i - 1], line[i], f);
            heading = (line[i] - line[i - 1]).HeadingXZ();
        }

        /// <summary>
        /// Bands placed along an axis (a line of points running the way the traffic runs) and cut to an area: at every pitch x sqrt 2 of the axis (so that bands at 45 degrees are pitch apart, centre to centre,
        /// measured square to them), counted from where the whole line starts (phase m before this piece of it), either a V (chevron: two arms going forward and out at 45 degrees to the axis's heading there,
        /// the point toward the traffic that comes, the corner mitred on the axis) or a diagonal band (one band at 45 degrees, leaning to the right going forward). Bands are placed before the axis's start and
        /// after its end as far as the area reaches. The points of the Vs that lie on the axis are put into apexes when it is given. Returns how many bands left paint.
        /// </summary>
        public static int AxisBands(MeshBuilder mesh, IList<Vec3> area, IList<Vec3> axis, float width, float pitch, bool chevron, float phase = 0f, List<Vec3> apexes = null) =>
            area == null || area.Count < 3 ? 0 : AxisBands(mesh, new AreaIndex(area), area, axis, width, pitch, chevron, phase, apexes);

        /// <summary>As AxisBands, over an area that is already indexed (AreaIndex.FromStrip for a strip or the inside of a gore).</summary>
        public static int AxisBands(MeshBuilder mesh, AreaIndex index, IList<Vec3> area, IList<Vec3> axis, float width, float pitch, bool chevron, float phase = 0f, List<Vec3> apexes = null)
        {
            if (index == null || !index.Ok || area == null || area.Count < 3 || axis == null || axis.Count < 2 || !(width > 0f) || !(pitch > 0.05f) || float.IsNaN(phase) || float.IsInfinity(phase))
                return 0;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vec3 p in area)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
            }

            float reach = (float)Math.Sqrt((maxX - minX) * (maxX - minX) + (maxZ - minZ) * (maxZ - minZ)) + 1f;
            float[] at = Lengths(axis);
            float total = at[axis.Count - 1];
            float step = pitch * Root2;
            // The bands at the whole line's distances (k + 1/2) step; this piece's axis starts at phase.
            int k0 = (int)Math.Floor((phase - reach) / step), k1 = (int)Math.Ceiling((phase + total + reach) / step);
            if (k1 - k0 > 20000)
                return 0;
            int drawn = 0;
            float h = width * 0.5f, apex = width * 0.5f * Root2;
            var piece = new List<Vec3>(4);
            for (int k = k0; k <= k1; k++)
            {
                float s = (k + 0.5f) * step - phase;
                PointAt(axis, at, s, out Vec3 a, out Vec3 t);
                Vec3 n = t.RightXZ();
                bool any = false;
                if (chevron)
                {
                    Vec3 outer = a - t * apex, inner = a + t * apex;
                    foreach (float side in new[] { 1f, -1f })
                    {
                        // The arm from its point outward, as far as its lines cross the area (an arm that crosses none of it ahead of the point is outside it).
                        Vec3 d = (t + n * side) * (1f / Root2);
                        if (!index.BandSpan(a, d, h, out float s0, out float s1) || s1 <= -apex)
                            continue;
                        float length = Math.Min(reach, s1 + apex);
                        piece.Clear();
                        piece.Add(outer);
                        piece.Add(outer + d * length);
                        piece.Add(inner + d * length);
                        piece.Add(inner);
                        any |= index.ClipWalk(mesh, piece, a - d * apex, d, length + apex * 2f, h + apex) > 0;
                    }
                }
                else
                {
                    Vec3 d = (t + n) * (1f / Root2), across = d.RightXZ() * h;
                    if (!index.BandSpan(a, d, h, out float s0, out float s1))
                        continue;
                    s0 = Math.Max(s0, -reach);
                    s1 = Math.Min(s1, reach);
                    piece.Clear();
                    piece.Add(a + d * s0 - across);
                    piece.Add(a + d * s1 - across);
                    piece.Add(a + d * s1 + across);
                    piece.Add(a + d * s0 + across);
                    any = index.ClipWalk(mesh, piece, a + d * s0, d, s1 - s0, h) > 0;
                }

                if (any)
                {
                    drawn++;
                    if (apexes != null && s >= -1e-4f && s <= total + 1e-4f)
                        apexes.Add(a);
                }
            }

            return drawn;
        }

        /// <summary>
        /// The axis of an area that has no sides of its own (an island the player drew): the middle of the widest stretch of it across each line z = constant, every step metres from its back to its front, smoothed
        /// over five points; the line runs +Z. A straight line through the middle of its box when there are not two such points.
        /// </summary>
        public static List<Vec3> CrossSectionAxis(IList<Vec3> polygon, float step = 0.5f)
        {
            var axis = new List<Vec3>();
            if (polygon == null || polygon.Count < 3 || !(step > 0.01f))
                return axis;
            float minZ = float.MaxValue, maxZ = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
            foreach (Vec3 p in polygon)
            {
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
            }

            var raw = new List<Vec3>();
            var xs = new List<float>();
            int n = polygon.Count;
            for (float z = minZ + step * 0.5f; z < maxZ && raw.Count < 4000; z += step)
            {
                xs.Clear();
                for (int i = 0; i < n; i++)
                {
                    Vec3 a = polygon[i], b = polygon[(i + 1) % n];
                    if ((a.Z <= z) == (b.Z <= z))
                        continue;
                    float f = (z - a.Z) / (b.Z - a.Z);
                    xs.Add(a.X + (b.X - a.X) * f);
                }

                xs.Sort();
                float best = -1f, mid = 0f;
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    if (xs[i + 1] - xs[i] > best)
                    {
                        best = xs[i + 1] - xs[i];
                        mid = (xs[i] + xs[i + 1]) * 0.5f;
                    }
                }

                if (best > 0f)
                    raw.Add(new Vec3(mid, 0f, z));
            }

            if (raw.Count < 2)
            {
                float cx = (minX + maxX) * 0.5f;
                axis.Add(new Vec3(cx, 0f, minZ));
                axis.Add(new Vec3(cx, 0f, Math.Max(maxZ, minZ + 0.1f)));
                return axis;
            }

            for (int i = 0; i < raw.Count; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int j = Math.Max(0, i - 2); j <= Math.Min(raw.Count - 1, i + 2); j++)
                {
                    sum += raw[j].X;
                    count++;
                }

                axis.Add(new Vec3(sum / count, 0f, raw[i].Z));
            }

            return axis;
        }

        /// <summary>
        /// The axis of an island that bends (an island along a curve): marched from its back (the middle of its lowest stretch across Z) forward, a step at a time: each step the stretch of the island across the
        /// way the axis goes is taken, square to that way, and its middle is the next point of the axis; the way is turned toward it, so the axis follows the island round a bend where a fixed set of
        /// cross-sections cannot. It stops where it leaves the island. Falls back to CrossSectionAxis when the island is too small to march in.
        /// </summary>
        public static List<Vec3> MarchingAxis(IList<Vec3> polygon, float step = 0.5f)
        {
            var axis = new List<Vec3>();
            if (polygon == null || polygon.Count < 3 || !(step > 0.05f))
                return axis;
            List<Vec3> sections = CrossSectionAxis(polygon, step * 0.5f);
            if (sections.Count < 4)
                return sections;
            float perimeter = 0f;
            for (int i = 0; i < polygon.Count; i++)
                perimeter += Vec3.DistanceXZ(polygon[i], polygon[(i + 1) % polygon.Count]);
            int n = polygon.Count;
            var xs = new List<float>();

            // The back: the middle of the stretch across Z, just above the lowest corner, that holds that corner (an island whose two ends are as low as each other starts at the first of them).
            int lowest = 0;
            for (int i = 1; i < n; i++)
            {
                if (polygon[i].Z < polygon[lowest].Z - 1e-5f)
                    lowest = i;
            }

            float z0 = polygon[lowest].Z + step * 0.5f, x0 = polygon[lowest].X;
            for (int i = 0; i < n; i++)
            {
                Vec3 a = polygon[i], b = polygon[(i + 1) % n];
                if ((a.Z <= z0) == (b.Z <= z0))
                    continue;
                xs.Add(a.X + (b.X - a.X) * (z0 - a.Z) / (b.Z - a.Z));
            }

            xs.Sort();
            float startX = float.NaN, nearest = float.MaxValue;
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                float off = x0 < xs[i] ? xs[i] - x0 : x0 > xs[i + 1] ? x0 - xs[i + 1] : 0f;
                if (off < nearest)
                {
                    nearest = off;
                    startX = (xs[i] + xs[i + 1]) * 0.5f;
                }
            }

            if (float.IsNaN(startX))
                return sections;
            Vec3 p = new Vec3(startX, 0f, z0);
            Vec3 d = new Vec3(0f, 0f, 1f);
            axis.Add(p);
            for (float walked = 0f; walked < perimeter * 0.6f && axis.Count < 4000; walked += step)
            {
                // The stretch of the outline across the way through the next point (the one that holds it, or is nearest it).
                Vec3 next = p + d * step;
                if (!Stretch(polygon, next, d, out Vec3 m, out Vec3 sideLo, out Vec3 sideHi, out float best) || best > step)
                    break;
                // 0.12.3: the way turns to the bisector of the two sides the stretch ends on, and the middle is taken again square to it: the points of the Vs then lie where their arms reach both sides
                // alike (0.12.2 turned the way four tenths toward the last step, and on a bend or a wedge that lagged to one side: the owner, "尖的连线位置应该是能智能识别中线位置").
                Vec3 bisector = Bisector(sideLo, sideHi, d);
                // A turn of more than 20 degrees in a step is the stretch ending on the island's end, not its sides: the axis ends there (the first step may turn as far as it needs, from +Z).
                if (axis.Count > 1 && bisector.LengthXZ > 0.5f && Vec3.Dot(bisector, d) < kMaxTurnCos)
                    break;
                if (bisector.LengthXZ > 0.5f)
                {
                    d = bisector;
                    if (Stretch(polygon, m, d, out Vec3 again, out _, out _, out float off) && off < step)
                        m = again;
                }

                if (Vec3.Dot(m - p, d) <= 1e-3f)
                    break;
                p = m;
                axis.Add(p);
            }

            return axis.Count >= 2 ? axis : sections;
        }

        /// <summary>
        /// The stretch of a polygon across the way d through a point (the line through it square to d): the middle of the stretch that holds the point (or is nearest it), the directions of the two edges
        /// the stretch ends on, and how far the point is outside it (0 inside). False when the line crosses no stretch.
        /// </summary>
        public static bool Stretch(IList<Vec3> polygon, Vec3 through, Vec3 d, out Vec3 middle, out Vec3 sideLo, out Vec3 sideHi, out float outside)
        {
            middle = through;
            sideLo = sideHi = d;
            outside = float.MaxValue;
            Vec3 across = d.RightXZ();
            int n = polygon.Count;
            var hits = new List<KeyValuePair<float, Vec3>>();
            for (int i = 0; i < n; i++)
            {
                Vec3 a = polygon[i], b = polygon[(i + 1) % n];
                float sa = Vec3.Dot(a - through, d), sb = Vec3.Dot(b - through, d);
                if ((sa <= 0f) == (sb <= 0f))
                    continue;
                float f = sa / (sa - sb);
                Vec3 q = Vec3.Lerp(a, b, f);
                hits.Add(new KeyValuePair<float, Vec3>(Vec3.Dot(q - through, across), (b - a).HeadingXZ()));
            }

            hits.Sort((x, y) => x.Key.CompareTo(y.Key));
            bool found = false;
            for (int i = 0; i + 1 < hits.Count; i += 2)
            {
                float lo = hits[i].Key, hi = hits[i + 1].Key;
                float off = lo > 0f ? lo : hi < 0f ? -hi : 0f;
                if (off < outside)
                {
                    outside = off;
                    middle = through + across * ((lo + hi) * 0.5f);
                    sideLo = hits[i].Value;
                    sideHi = hits[i + 1].Value;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>The bisector of two sides' directions, each turned to run the way d goes (unit; zero when they run opposite ways).</summary>
        public static Vec3 Bisector(Vec3 sideA, Vec3 sideB, Vec3 d)
        {
            Vec3 a = Vec3.Dot(sideA, d) < 0f ? sideA * -1f : sideA, b = Vec3.Dot(sideB, d) < 0f ? sideB * -1f : sideB;
            var sum = new Vec3(a.X + b.X, 0f, a.Z + b.Z);
            return sum.LengthXZ < 1e-4f ? new Vec3(0f, 0f, 0f) : sum.HeadingXZ();
        }

        /// <summary>
        /// The middle line of two sides that start at one place (a gore's nose) and part, from there to where one of them ends (0.12.3): marched a step at a time, each point the middle of the line across
        /// the two sides square to their bisector there, so that it is as far from each side as the other measured square to the way it runs (0.12.2 took the points at the same share of each side's length:
        /// on a gore whose sides differ in length or bend that lay to one side). Falls back to MidAxis when the sides cannot be marched.
        /// </summary>
        public static List<Vec3> MedialAxis(IList<Vec3> sideA, IList<Vec3> sideB, float step = 0.5f)
        {
            if (sideA == null || sideB == null || sideA.Count < 2 || sideB.Count < 2 || !(step > 0.05f))
                return MidAxis(sideA, sideB);
            Vec3 firstA = (sideA[1] - sideA[0]).HeadingXZ();
            Vec3 d = Bisector(firstA, (sideB[1] - sideB[0]).HeadingXZ(), firstA);
            if (d.LengthXZ < 0.5f)
                return MidAxis(sideA, sideB);
            Vec3 p = (sideA[0] + sideB[0]) * 0.5f;
            var axis = new List<Vec3> { p };
            for (int k = 0; k < 8000; k++)
            {
                Vec3 next = p + d * step;
                if (!Across(sideA, sideB, next, d, out Vec3 m, out Vec3 ta, out Vec3 tb))
                    break;
                Vec3 bisector = Bisector(ta, tb, d);
                if (bisector.LengthXZ > 0.5f)
                {
                    d = bisector;
                    if (Across(sideA, sideB, m, d, out Vec3 again, out _, out _))
                        m = again;
                }

                // Where a side bends at a corner of its points the middle of the line across is not quite as far from each: it is moved across by half the difference, twice.
                Vec3 sideways = d.RightXZ();
                for (int pass = 0; pass < 2; pass++)
                {
                    float da = Distance(sideA, m), db = Distance(sideB, m);
                    Vec3 towardB = Vec3.Dot(Nearest(sideB, m) - m, sideways) >= 0f ? sideways : sideways * -1f;
                    m = m + towardB * ((db - da) * 0.5f);
                }

                if (Vec3.Dot(m - p, d) <= 1e-3f)
                    break;
                p = m;
                axis.Add(p);
            }

            return axis.Count >= 2 ? axis : MidAxis(sideA, sideB);
        }

        private const float kMaxTurnCos = 0.9397f;

        /// <summary>The nearest point of a line of points to a place (on the ground; the height of the line there), and the distance to it.</summary>
        private static Vec3 Nearest(IList<Vec3> line, Vec3 p)
        {
            Vec3 best = line[0];
            float bestD = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 a = line[i], b = line[i + 1];
                float dx = b.X - a.X, dz = b.Z - a.Z, l2 = dx * dx + dz * dz;
                float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / l2));
                Vec3 q = Vec3.Lerp(a, b, t);
                float d = Vec3.DistanceXZ(p, q);
                if (d < bestD)
                {
                    bestD = d;
                    best = q;
                }
            }

            return best;
        }

        private static float Distance(IList<Vec3> line, Vec3 p) => Vec3.DistanceXZ(p, Nearest(line, p));

        /// <summary>Where the line through a point square to d crosses each of two lines of points (the crossing nearest the point on each): the middle of the two, and the two lines' directions there.</summary>
        private static bool Across(IList<Vec3> sideA, IList<Vec3> sideB, Vec3 through, Vec3 d, out Vec3 middle, out Vec3 ta, out Vec3 tb)
        {
            middle = through;
            bool a = Crossing(sideA, through, d, out Vec3 qa, out ta), b = Crossing(sideB, through, d, out Vec3 qb, out tb);
            if (!a || !b)
                return false;
            middle = (qa + qb) * 0.5f;
            return true;
        }

        /// <summary>Where the line through a point square to d crosses a line of points: the crossing nearest the point, and the line's direction there. False when it does not cross it.</summary>
        private static bool Crossing(IList<Vec3> line, Vec3 through, Vec3 d, out Vec3 at, out Vec3 direction)
        {
            at = through;
            direction = d;
            float best = float.MaxValue;
            bool found = false;
            Vec3 across = d.RightXZ();
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 a = line[i], b = line[i + 1];
                float sa = Vec3.Dot(a - through, d), sb = Vec3.Dot(b - through, d);
                if ((sa < 0f && sb < 0f) || (sa > 0f && sb > 0f))
                    continue;
                float f = Math.Abs(sa - sb) < 1e-9f ? 0f : sa / (sa - sb);
                Vec3 q = Vec3.Lerp(a, b, Math.Max(0f, Math.Min(1f, f)));
                float off = Math.Abs(Vec3.Dot(q - through, across));
                if (off < best)
                {
                    best = off;
                    at = q;
                    direction = (b - a).HeadingXZ();
                    found = true;
                }
            }

            return found;
        }

        /// <summary>A line of points at the given number of places spread evenly along it (by length on the ground), the ends kept.</summary>
        public static List<Vec3> Resample(IList<Vec3> line, int count)
        {
            var result = new List<Vec3>();
            if (line == null || line.Count == 0)
                return result;
            if (line.Count == 1 || count < 2)
            {
                result.Add(line[0]);
                return result;
            }

            float[] at = Lengths(line);
            float total = at[line.Count - 1];
            for (int i = 0; i < count; i++)
            {
                PointAt(line, at, total * i / (count - 1), out Vec3 p, out _);
                result.Add(p);
            }

            return result;
        }

        /// <summary>The middle line of two sides that start at one place (a gore's nose) and part: the mean of their points at the same share of each one's length, from the start to the far end.</summary>
        public static List<Vec3> MidAxis(IList<Vec3> sideA, IList<Vec3> sideB, int samples = 24)
        {
            var axis = new List<Vec3>();
            if (sideA == null || sideB == null || sideA.Count < 2 || sideB.Count < 2)
                return axis;
            List<Vec3> a = Resample(sideA, samples), b = Resample(sideB, samples);
            for (int i = 0; i < samples; i++)
                axis.Add((a[i] + b[i]) * 0.5f);
            return axis;
        }

        /// <summary>
        /// The island of 6.6 (导流线) on any simple polygon: the outline (as wide as the lines it joins: 15 or 20) inside the polygon, and inside that the fill: V lines (white, between traffic the same way; on the axis
        /// of the island, the points toward -Z, or +Z when flip), diagonal lines (yellow, between opposing traffic: 45 degrees, 1 m apart) or the lines between a motor and a cycle lane (30 wide, 2 m apart).
        /// </summary>
        public static MarkingShape Island(IList<Vec3> polygon, float outline, MarkingWarnings.ChannelFill fill, MarkingColour colour, float lineWidth = 0.45f, bool flip = false, List<Vec3> apexes = null)
        {
            var shape = new MarkingShape { Colour = colour };
            if (polygon == null || polygon.Count < 3 || !(outline > 0f) || !MarkingFills.IsSimple(polygon) || Math.Abs(Polygon.SignedArea2(polygon)) < 0.02f)
                return shape;
            MarkingFills.Outline(shape.Mesh, polygon, outline);
            // The fill stops at the inside of the outline; where the island is too narrow for that, at the island's own edge (the paint is one colour, the overlap does not show).
            List<Vec3> inner = MarkingFills.Inset(polygon, outline) ?? new List<Vec3>(polygon);
            float w = RoadMarkings.NearestOf(RoadMarkings.CrosswalkStripeWidths, lineWidth);
            switch (fill)
            {
                case MarkingWarnings.ChannelFill.CycleSide:
                    MarkingFills.Hatch(shape.Mesh, inner, 0.3f, 2f, 45f);
                    break;
                case MarkingWarnings.ChannelFill.Diagonal:
                    MarkingFills.Hatch(shape.Mesh, inner, w, 1f, 45f);
                    break;
                default:
                    {
                        List<Vec3> axis = MarchingAxis(inner);
                        if (flip)
                            axis.Reverse();
                        AxisBands(shape.Mesh, inner, axis, w, VPitch, true, 0f, apexes);
                        break;
                    }
            }

            return shape;
        }

        /// <summary>
        /// A gore (线65, Fig. 94) between two sides that start together at the nose and part: sideMain the through carriageway's edge on the gore's side and sideRamp the ramp's, both from the nose to the far end.
        /// The edge lines (20 wide) inside along both sides and across the far end, and the V lines inside them (45 wide, 1 m apart), the points toward the traffic that comes: for an exit the traffic runs from the
        /// nose to the far end, for an entry from the far end to the nose. A blunt nose (the two sides start apart) is closed by an edge line across it.
        /// </summary>
        public static MarkingShape Gore(IList<Vec3> sideMain, IList<Vec3> sideRamp, bool exit, List<Vec3> apexes = null)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (sideMain == null || sideRamp == null || sideMain.Count < 2 || sideRamp.Count < 2)
                return shape;
            List<Vec3> polygon = GorePolygon(sideMain, sideRamp);
            if (polygon.Count < 3 || Math.Abs(Polygon.SignedArea2(polygon)) < 0.05f || !MarkingFills.IsSimple(polygon))
                return shape;
            // Which way is into the gore from each side: the ramp side lies to one side of the main side.
            int inward = Side(sideMain, sideRamp);
            float h = GoreEdge * 0.5f;
            // The edge lines are cut to the gore: near the nose, where it is narrower than the two lines, they would reach over the other side into the lanes. The gore is indexed as the strip between its
            // two sides (both from the nose to the far end, resampled to as many points).
            AreaIndex area = AreaIndex.FromStrip(sideMain, sideRamp);
            if (!area.Ok)
                area = new AreaIndex(polygon);
            ClippedRibbon(shape.Mesh, MarkingFills.Offset(sideMain, inward * h), GoreEdge, area);
            ClippedRibbon(shape.Mesh, MarkingFills.Offset(sideRamp, -inward * h), GoreEdge, area);
            Vec3 farMain = sideMain[sideMain.Count - 1], farRamp = sideRamp[sideRamp.Count - 1];
            Vec3 back = (Mean(sideMain) + Mean(sideRamp)) * 0.5f;
            AcrossLine(shape.Mesh, farMain, farRamp, back, area);
            if (Vec3.DistanceXZ(sideMain[0], sideRamp[0]) > GoreEdge)
                AcrossLine(shape.Mesh, sideMain[0], sideRamp[0], back, area);

            List<Vec3> inner = GoreInner(sideMain, sideRamp, inward, out List<Vec3> innerMain, out List<Vec3> innerRamp);
            if (inner != null)
            {
                List<Vec3> axis = MedialAxis(sideMain, sideRamp);
                if (!exit)
                    axis.Reverse();
                AreaIndex innerIndex = AreaIndex.FromStrip(innerMain, innerRamp);
                AxisBands(shape.Mesh, innerIndex.Ok ? innerIndex : new AreaIndex(inner), inner, axis, VWidth, VPitch, true, 0f, apexes);
            }

            return shape;
        }

        /// <summary>The closed outline of a gore: along the main side from the nose to the far end and back along the ramp side.</summary>
        public static List<Vec3> GorePolygon(IList<Vec3> sideMain, IList<Vec3> sideRamp)
        {
            var polygon = new List<Vec3>(sideMain);
            for (int i = sideRamp.Count - 1; i >= 0; i--)
            {
                if (i == 0 && Vec3.DistanceXZ(sideRamp[0], polygon[0]) < 1e-4f)
                    break;
                polygon.Add(sideRamp[i]);
            }

            return polygon;
        }

        /// <summary>+1 when the ramp side lies to the right of the main side (looking from the nose along it), -1 when to the left.</summary>
        public static int Side(IList<Vec3> sideMain, IList<Vec3> sideRamp)
        {
            Vec3 farRamp = sideRamp[sideRamp.Count - 1];
            float best = float.MaxValue;
            int sign = 1;
            for (int i = 0; i + 1 < sideMain.Count; i++)
            {
                Vec3 a = sideMain[i], b = sideMain[i + 1];
                float c = Polygon.Cross(a, b, farRamp);
                float d = Vec3.DistanceXZ((a + b) * 0.5f, farRamp);
                if (d < best && Math.Abs(c) > 1e-6f)
                {
                    best = d;
                    sign = c < 0f ? 1 : -1;
                }
            }

            return sign;
        }

        private static Vec3 Mean(IList<Vec3> points)
        {
            Vec3 sum = Vec3.Zero;
            foreach (Vec3 p in points)
                sum = sum + p;
            return sum * (1f / Math.Max(1, points.Count));
        }

        /// <summary>An edge line 20 wide along the segment a-b, on the side of it that faces the given point (the inside of the gore), cut to the area.</summary>
        private static void AcrossLine(MeshBuilder mesh, Vec3 a, Vec3 b, Vec3 inside, AreaIndex area)
        {
            if (Vec3.DistanceXZ(a, b) < 1e-3f)
                return;
            Vec3 right = (b - a).HeadingXZ().RightXZ();
            float sign = Vec3.Dot(inside - a, right) >= 0f ? 1f : -1f;
            ClippedRibbon(mesh, new List<Vec3> { a + right * (sign * GoreEdge * 0.5f), b + right * (sign * GoreEdge * 0.5f) }, GoreEdge, area);
        }

        /// <summary>A ribbon along a line of points (as RoadMarkings.Ribbon, the corners on the mean of the two pieces' normals), every quad of it cut to an area's triangles.</summary>
        private static void ClippedRibbon(MeshBuilder mesh, IList<Vec3> line, float width, AreaIndex area)
        {
            if (line == null || line.Count < 2)
                return;
            float half = width * 0.5f;
            var left = new Vec3[line.Count];
            var right = new Vec3[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                Vec3 heading = i == 0 ? (line[1] - line[0]).HeadingXZ() : i == line.Count - 1 ? (line[i] - line[i - 1]).HeadingXZ() : ((line[i] - line[i - 1]).HeadingXZ() + (line[i + 1] - line[i]).HeadingXZ()).HeadingXZ();
                Vec3 r = heading.RightXZ() * half;
                left[i] = line[i] - r;
                right[i] = line[i] + r;
            }

            var quad = new List<Vec3>(4);
            for (int i = 0; i + 1 < line.Count; i++)
            {
                quad.Clear();
                quad.Add(left[i]);
                quad.Add(right[i]);
                quad.Add(right[i + 1]);
                quad.Add(left[i + 1]);
                area.Clip(mesh, quad);
            }
        }

        /// <summary>
        /// The part of a gore inside its edge lines: both sides moved inward by the edge line's width, from where they no longer cross (the narrow part at the nose is all edge line) to the far end, closed there
        /// one edge line in from it. Null when nothing of the gore is left inside the lines.
        /// </summary>
        private static List<Vec3> GoreInner(IList<Vec3> sideMain, IList<Vec3> sideRamp, int inward, out List<Vec3> innerMain, out List<Vec3> innerRamp)
        {
            innerMain = null;
            innerRamp = null;
            int n = 32;
            List<Vec3> a = MarkingFills.Offset(Resample(sideMain, n), inward * GoreEdge), b = MarkingFills.Offset(Resample(sideRamp, n), -inward * GoreEdge);
            int first = -1;
            for (int i = 0; i < n; i++)
            {
                Vec3 heading = i + 1 < n ? (a[i + 1] - a[i]).HeadingXZ() : (a[i] - a[i - 1]).HeadingXZ();
                float gap = Vec3.Dot(b[i] - a[i], heading.RightXZ()) * inward;
                if (gap > 0.02f)
                {
                    first = i;
                    break;
                }
            }

            if (first < 0 || first >= n - 2)
                return null;
            // The far end one edge line back from the ends of the sides.
            Vec3 farA = a[n - 1], farB = b[n - 1];
            Vec3 into = (a[n - 2] + b[n - 2]) * 0.5f - (farA + farB) * 0.5f;
            Vec3 back = into.HeadingXZ() * GoreEdge;
            var polygon = new List<Vec3>();
            for (int i = first; i < n - 1; i++)
                polygon.Add(a[i]);
            polygon.Add(farA + back);
            polygon.Add(farB + back);
            for (int i = n - 2; i >= first; i--)
                polygon.Add(b[i]);
            if (!MarkingFills.IsSimple(polygon) || Math.Abs(Polygon.SignedArea2(polygon)) < 0.02f)
                return null;
            // The same area as two rows of points, for the strip index: the main side's row then its far corner, the ramp side's row then its far corner.
            innerMain = new List<Vec3>();
            innerRamp = new List<Vec3>();
            for (int i = first; i < n - 1; i++)
            {
                innerMain.Add(a[i]);
                innerRamp.Add(b[i]);
            }

            innerMain.Add(farA + back);
            innerRamp.Add(farB + back);
            return polygon;
        }

        /// <summary>
        /// A strip along a road (6.6: a hatched median, a buffer between a lane and a cycle lane): from a line of points (the way the traffic runs) out to its right (rightSide) or left by width; its long sides
        /// edged, its ends edged only where the strip ends (a strip that is cut into pieces is open where the pieces join), and filled as an island is. phase is where along the whole strip this piece starts
        /// (m), so that the Vs and the diagonals of the pieces line up; the Vs point toward the start of the line (tipAtStart: the traffic runs from the start to the end) or its end, and then they are counted from
        /// the whole strip's end, which is total m from its start (0: this piece is the whole strip).
        /// </summary>
        public static MarkingShape Strip(IList<Vec3> line, float width, bool rightSide, MarkingWarnings.ChannelFill fill, MarkingColour colour, float outline = 0.15f, float lineWidth = 0.45f, bool tipAtStart = true,
            bool capStart = true, bool capEnd = true, float phase = 0f, float total = 0f)
        {
            var shape = new MarkingShape { Colour = colour };
            if (line == null || line.Count < 2 || !(width > 0.2f) || float.IsNaN(width) || !(outline > 0f))
                return shape;
            float sign = rightSide ? 1f : -1f;
            List<Vec3> near = new List<Vec3>(line), far = MarkingFills.Offset(line, sign * width);
            // The long sides, inside the strip.
            RoadMarkings.Ribbon(shape.Mesh, MarkingFills.Offset(line, sign * outline * 0.5f), outline);
            RoadMarkings.Ribbon(shape.Mesh, MarkingFills.Offset(line, sign * (width - outline * 0.5f)), outline);
            Vec3 middle = (Mean(near) + Mean(far)) * 0.5f;
            if (capStart)
                AcrossBand(shape.Mesh, near[0], far[0], middle, outline);
            if (capEnd)
                AcrossBand(shape.Mesh, near[near.Count - 1], far[far.Count - 1], middle, outline);

            // The fill: between the sides' inner edges, and in from the ends that are edged.
            List<Vec3> innerNear = MarkingFills.Offset(line, sign * outline), innerFar = MarkingFills.Offset(line, sign * (width - outline));
            if (capStart)
                TrimStart(innerNear, innerFar, outline);
            if (capEnd)
            {
                innerNear.Reverse();
                innerFar.Reverse();
                TrimStart(innerNear, innerFar, outline);
                innerNear.Reverse();
                innerFar.Reverse();
            }

            var area = new List<Vec3>(innerNear);
            for (int i = innerFar.Count - 1; i >= 0; i--)
                area.Add(innerFar[i]);
            if (!MarkingFills.IsSimple(area))
                return shape;
            AreaIndex strip = AreaIndex.FromStrip(innerNear, innerFar);
            float w = RoadMarkings.NearestOf(RoadMarkings.CrosswalkStripeWidths, lineWidth);
            List<Vec3> axis = MarkingFills.Offset(line, sign * width * 0.5f);
            List<Vec3> heights = new List<Vec3>(axis);
            int fillFrom = shape.Mesh.VertexCount;
            float length = Lengths(axis)[axis.Count - 1];
            if (fill == MarkingWarnings.ChannelFill.Chevron)
            {
                if (!tipAtStart)
                {
                    // The traffic runs from the end to the start: the axis the other way, and the Vs counted from the whole strip's end.
                    axis.Reverse();
                    float fromEnd = total > phase + length ? total - phase - length : 0f;
                    AxisBands(shape.Mesh, strip, area, axis, w, VPitch, true, fromEnd);
                }
                else
                {
                    AxisBands(shape.Mesh, strip, area, axis, w, VPitch, true, phase);
                }
            }
            else
            {
                bool cycle = fill == MarkingWarnings.ChannelFill.CycleSide;
                AxisBands(shape.Mesh, strip, area, axis, cycle ? 0.3f : w, cycle ? 2f : 1f, false, phase);
            }

            MarkingFills.FollowLine(shape.Mesh, fillFrom, heights);
            return shape;
        }

        /// <summary>An edge band across from a to b, of the width, on the side facing the given point.</summary>
        private static void AcrossBand(MeshBuilder mesh, Vec3 a, Vec3 b, Vec3 inside, float width)
        {
            if (Vec3.DistanceXZ(a, b) < 1e-3f)
                return;
            Vec3 right = (b - a).HeadingXZ().RightXZ();
            float sign = Vec3.Dot(inside - a, right) >= 0f ? 1f : -1f;
            RoadMarkings.Ribbon(mesh, new List<Vec3> { a + right * (sign * width * 0.5f), b + right * (sign * width * 0.5f) }, width);
        }

        /// <summary>Moves the first points of two side lines forward along them by a distance (the fill starts one edge line in from an edged end).</summary>
        private static void TrimStart(List<Vec3> a, List<Vec3> b, float distance)
        {
            if (a.Count < 2 || b.Count < 2)
                return;
            Vec3 ha = (a[1] - a[0]).HeadingXZ(), hb = (b[1] - b[0]).HeadingXZ();
            a[0] = a[0] + ha * Math.Min(distance, Vec3.DistanceXZ(a[0], a[1]) * 0.9f);
            b[0] = b[0] + hb * Math.Min(distance, Vec3.DistanceXZ(b[0], b[1]) * 0.9f);
        }
    }
}
