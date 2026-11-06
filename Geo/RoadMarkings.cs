using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The colours of a road marking (GB 5768.3-2025 4.3.1 has four: white, yellow, orange and red; the plain markings are white and yellow). Red is the ring of the speed-limit face painted on the road
    /// (5.14.3, Fig. 50) and Black the figures on it and the dark bands of the elevation marking (7.7); orange is for work zones (GB 5768.4) and is not here. The number is the material's number
    /// ("marking:N") and the index of the colour in the picture of a marking: do not renumber.
    /// </summary>
    public enum MarkingColour
    {
        White = 0,
        Yellow = 1,
        Red = 2,
        Black = 3
    }

    /// <summary>A word painted on the road (the "停" of a stop-and-give-way line): where its box is and how big. The renderer paints the word into the box; this is only the box.</summary>
    public sealed class MarkingText
    {
        public string Text = "";

        /// <summary>The centre of the box across the road (m, +X to the driver's right) and along it (m, +Z the way the traffic runs), and its size across and along.</summary>
        public float X, Z, Width, Length;

        /// <summary>The turn of the box about its centre (radians, as a mesh's yaw: local +Z, the way the word reads from near to far, goes to (sin yaw, cos yaw)); 0 reads along +Z.</summary>
        public float Yaw;

        /// <summary>
        /// The text is figures of a speed (线34, 5.14.2): they keep the width each has in the font (a 1 is narrower than a 0) and are squeezed together as the figures of Fig. 48 are; Width is then the width of
        /// one figure's cell (the 0's) and Length the height of the figures, and the figures are centred on X and Z as a row.
        /// </summary>
        public bool Digits;

        /// <summary>The character is set in the font's own proportions (0.12.22, the custom text): a letter or a figure as wide as its advance and as tall as the em, as a line of text is set.</summary>
        public bool Proportional;
    }

    /// <summary>One marking as flat geometry: the mesh (up-facing triangles at the height of the points it was made from), its colour and the boxes of its words.</summary>
    public sealed class MarkingShape
    {
        public MarkingColour Colour;
        public readonly MeshBuilder Mesh = new MeshBuilder();
        public readonly List<MarkingText> Texts = new List<MarkingText>();

        /// <summary>
        /// How far this paint lies above the rest of the marking's (m): paint that is drawn over other paint (the black figures on the white face of the speed-limit mark) is lifted, so that the two
        /// are not at the same height and do not flicker. 0 for paint that lies on the road. See MarkingShape.Layer.
        /// </summary>
        public float Lift;

        /// <summary>
        /// The paint is on an upright surface, not on the road: the shape's mesh is in the plane of x (across) and y (up) at z = 0 and faces -Z (the oncoming driver), and its layers are in front of one another
        /// (toward -Z). The picture of it is its front. The elevation marking (线72) is the one such.
        /// </summary>
        public bool Upright;

        /// <summary>
        /// Not paint but a solid part of a road barrier (0.12.13, Barriers): drawn in the solid's material, standing on the road at the marking's height (not lifted by ChunkBuilder.MarkingRaise and never fitted
        /// to the surface), casting shadows. Null for paint.
        /// </summary>
        public SolidColour? Solid;

        /// <summary>The baked detail of a solid's surface (0.12.32, DetailSurfaces); None: the kind its colour has (MarkingPlan.DetailOf).</summary>
        public DetailKind Detail;

        /// <summary>The height of one layer of paint over another (m): a few millimetres, under what the eye can see from a driving seat and more than the depth buffer tells apart at 100 m.</summary>
        public const float LayerStep = 0.006f;

        /// <summary>Puts the paint in a layer over the road: 0 on the road, 1 over that, and so on.</summary>
        public MarkingShape Layer(int layer)
        {
            Lift = Math.Max(0, layer) * LayerStep;
            return this;
        }

        /// <summary>The painted area of the mesh (m2), the words not counted.</summary>
        public float Area
        {
            get
            {
                double sum = 0.0;
                for (int i = 0; i + 2 < Mesh.Indices.Count; i += 3)
                {
                    Vec3 a = Mesh.Positions[Mesh.Indices[i]], b = Mesh.Positions[Mesh.Indices[i + 1]], c = Mesh.Positions[Mesh.Indices[i + 2]];
                    sum += MeshBuilder.Cross(b - a, c - a).Length * 0.5;
                }

                return (float)sum;
            }
        }
    }

    /// <summary>The line of a dashed marking: the length of a dash and of a gap (m), the width (m) and the colour.</summary>
    public struct DashSpec
    {
        public float On, Off, Width;
        public MarkingColour Colour;

        public DashSpec(float on, float off, float width, MarkingColour colour)
        {
            On = on;
            Off = off;
            Width = width;
            Colour = colour;
        }
    }

    /// <summary>
    /// The shapes of the road markings of GB 5768.3-2025 that are drawn as flat figures (design in scratch/design/DESIGN-markings-5768-3.md, the decoration only: nothing here touches a lane).
    /// Frame of a marking: Y up, +Z the way the traffic runs over it, +X to the driver's right; the triangles face up. The standard's own values are in the constants; what the standard was not read
    /// for is said where it is used. Pure, so it is checked offline.
    /// </summary>
    public static class RoadMarkings
    {
        // ---- stop line (线 52, 6.4.2): white solid line, 20, 30 or 40 cm wide, by road class, traffic and speed.
        public static readonly float[] StopLineWidths = { 0.2f, 0.3f, 0.4f };

        // ---- crosswalk (线 10, 5.8.2, Fig. 13): white parallel solid stripes 40 or 45 cm wide, gap 60 cm in general, adjusted to the width of the carriageway, at most 80 cm. The figure marks the
        // length of a stripe "≥200" (cm); the clause has no figure for it.
        public static readonly float[] CrosswalkStripeWidths = { 0.4f, 0.45f };
        public const float CrosswalkGap = 0.6f, CrosswalkMaxGap = 0.8f, CrosswalkMinLength = 2f;

        // ---- stop-and-give-way line (6.5.1.2): two parallel white solid lines 20 cm wide, 20 cm apart, and the word 停, 1 m wide and 2.5 m high.
        public const float GiveWayLineWidth = 0.2f, GiveWayLineGap = 0.2f, GiveWayTextWidth = 1f, GiveWayTextHeight = 2.5f;

        /// <summary>How far behind the near line the top of the word stands (m): Fig. 76 a) and b) print 200 to 250; 2 m is used. The word's strokes are 10 wide on its 100 x 250 grid (Fig. 76).</summary>
        public const float GiveWayTextGap = 2f, GiveWayTextGapMax = 2.5f;

        // ---- dashed lines of the lanes (5.1.2, 5.2.2): the centre line (线 1) is yellow 4 m on, 6 m off, 15 cm wide (10 cm on roads for non-motor traffic and the like); the line between lanes of one
        // direction (线 2, 线 3) is white 10 or 15 cm wide, 6 on 9 off or 3 on 9 off at 60 km/h and over, 2 on 4 off under 60 km/h.
        public static DashSpec CentreLine(bool narrow = false) => new DashSpec(4f, 6f, narrow ? 0.10f : 0.15f, MarkingColour.Yellow);
        public static DashSpec LaneLineFast(bool shortDash = false, bool narrow = false) => new DashSpec(shortDash ? 3f : 6f, 9f, narrow ? 0.10f : 0.15f, MarkingColour.White);
        public static DashSpec LaneLineSlow(bool narrow = false) => new DashSpec(2f, 4f, narrow ? 0.10f : 0.15f, MarkingColour.White);

        /// <summary>The value of the list that is nearest to the given one; the lower of two that are equally near; the first when the value is not a number.</summary>
        public static float NearestOf(float[] options, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return options[0];
            float best = options[0];
            for (int i = 1; i < options.Length; i++)
            {
                if (Math.Abs(options[i] - value) < Math.Abs(best - value) - 1e-6f)
                    best = options[i];
            }

            return best;
        }

        /// <summary>An up-facing rectangle in the plane at height y, from (x0, z0) to (x1, z1) in any order, as two triangles.</summary>
        public static void Rect(MeshBuilder mesh, float x0, float z0, float x1, float z1, float y = 0f)
        {
            float xa = Math.Min(x0, x1), xb = Math.Max(x0, x1), za = Math.Min(z0, z1), zb = Math.Max(z0, z1);
            var up = new Vec3(0f, 1f, 0f);
            int a = mesh.Vertex(new Vec3(xa, y, za), up, 0f, 0f);
            int b = mesh.Vertex(new Vec3(xa, y, zb), up, 0f, 1f);
            int c = mesh.Vertex(new Vec3(xb, y, zb), up, 1f, 1f);
            int d = mesh.Vertex(new Vec3(xb, y, za), up, 1f, 0f);
            mesh.QuadFacing(a, b, c, d, up);
        }

        /// <summary>The stop line: a white solid line across a span (m) centred on x = 0, its centre on z = 0; the width is snapped to 20, 30 or 40 cm.</summary>
        public static MarkingShape StopLine(float span, float width = 0.3f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(span > 0f) || float.IsInfinity(span))
                return shape;
            float w = NearestOf(StopLineWidths, width);
            Rect(shape.Mesh, -span * 0.5f, -w * 0.5f, span * 0.5f, w * 0.5f);
            return shape;
        }

        /// <summary>
        /// How many stripes a crosswalk of the given span has, and the gap between them: the most that keep the gap at 60 cm or more, and when that leaves a gap over 80 cm (the standard: "not over
        /// 80 cm"), one stripe more, whose gap is under 60 cm (the standard lets the gap be adjusted to the width of the carriageway and names no least; this is that adjustment). Two stripes at least.
        /// </summary>
        public static int CrosswalkStripes(float span, float stripeWidth, out float gap)
        {
            gap = 0f;
            if (!(span > 0f) || float.IsInfinity(span) || !(stripeWidth > 0f))
                return 0;
            int most = (int)Math.Floor((span + CrosswalkGap) / (stripeWidth + CrosswalkGap) + 1e-4f);
            if (most < 2)
            {
                // A span that holds fewer than two stripes with the least gap: two stripes at the ends (a gap below 60 cm only when there is no room).
                gap = Math.Max(0f, span - 2f * stripeWidth);
                return span >= 2f * stripeWidth ? 2 : 1;
            }

            int best = most;
            float bestGap = (span - most * stripeWidth) / (most - 1);
            if (bestGap > CrosswalkMaxGap)
            {
                // Too wide a gap: the limit is the standard's, so one more stripe, which narrows the gap below 60 cm.
                int more = most + 1;
                float moreGap = (span - more * stripeWidth) / (more - 1);
                if (moreGap >= 0f)
                {
                    best = more;
                    bestGap = moreGap;
                }
            }

            gap = bestGap;
            return best;
        }

        /// <summary>The crosswalk: white stripes along the traffic, across a span (m) centred on x = 0 and a length (m, 2 m at least) centred on z = 0; a stripe at each end.</summary>
        public static MarkingShape Crosswalk(float span, float stripeWidth = 0.4f, float length = 3f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            float w = NearestOf(CrosswalkStripeWidths, stripeWidth);
            int count = CrosswalkStripes(span, w, out float gap);
            if (count < 1 || float.IsNaN(length))
                return shape;
            float half = Math.Max(CrosswalkMinLength, length) * 0.5f;
            if (count == 1)
            {
                float one = Math.Min(w, span);
                Rect(shape.Mesh, -one * 0.5f, -half, one * 0.5f, half);
                return shape;
            }

            float x = -span * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Rect(shape.Mesh, x, -half, x + w, half);
                x += w + gap;
            }

            return shape;
        }

        /// <summary>
        /// The stop-and-give-way line: two white solid lines across a span (m) centred on x = 0, the pair centred on z = 0 (the traffic comes from -Z), and the box of the word 停 for each lane
        /// centre in laneCentres (none for a lane of non-motor traffic: the standard paints no word there). The word stands textGap behind the first line.
        /// </summary>
        public static MarkingShape GiveWay(float span, IList<float> laneCentres = null, float textGap = GiveWayTextGap)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(span > 0f) || float.IsInfinity(span))
                return shape;
            float outer = GiveWayLineWidth + GiveWayLineGap * 0.5f;
            float inner = GiveWayLineGap * 0.5f;
            Rect(shape.Mesh, -span * 0.5f, -outer, span * 0.5f, -inner);
            Rect(shape.Mesh, -span * 0.5f, inner, span * 0.5f, outer);
            if (laneCentres != null)
            {
                float gap = float.IsNaN(textGap) ? GiveWayTextGap : Math.Max(0f, textGap);
                foreach (float x in laneCentres)
                {
                    if (float.IsNaN(x) || float.IsInfinity(x))
                        continue;
                    shape.Texts.Add(new MarkingText { Text = "停", X = x, Z = -outer - gap - GiveWayTextHeight * 0.5f, Width = GiveWayTextWidth, Length = GiveWayTextHeight });
                }
            }

            return shape;
        }

        /// <summary>The length of a line of points in the ground plane, and the distance of each point from the first.</summary>
        public static float Lengths(IList<Vec3> points, List<float> at)
        {
            at.Clear();
            float total = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0)
                    total += Vec3.DistanceXZ(points[i - 1], points[i]);
                at.Add(total);
            }

            return total;
        }

        /// <summary>A ribbon of the given width along a line of points (its sides at half the width to each side of the line, the heading at a point the mean of the two segments' headings), as up-facing quads.</summary>
        public static void Ribbon(MeshBuilder mesh, IList<Vec3> points, float width)
        {
            if (points == null || points.Count < 2 || !(width > 0f))
                return;
            var up = new Vec3(0f, 1f, 0f);
            float half = width * 0.5f;
            int previousLeft = -1, previousRight = -1;
            for (int i = 0; i < points.Count; i++)
            {
                Vec3 heading;
                if (i == 0)
                    heading = (points[1] - points[0]).HeadingXZ();
                else if (i == points.Count - 1)
                    heading = (points[i] - points[i - 1]).HeadingXZ();
                else
                    heading = ((points[i] - points[i - 1]).HeadingXZ() + (points[i + 1] - points[i]).HeadingXZ()).HeadingXZ();
                Vec3 right = heading.RightXZ() * half;
                int left = mesh.Vertex(points[i] - right, up, 0f, i);
                int rightIndex = mesh.Vertex(points[i] + right, up, 1f, i);
                if (previousLeft >= 0)
                    mesh.QuadFacing(previousLeft, previousRight, rightIndex, left, up);
                previousLeft = left;
                previousRight = rightIndex;
            }
        }

        /// <summary>
        /// A dashed line along a centre line (points in the ground plane, the traffic running from the first to the last): the dashes of the spec from the phase (m into the pattern), each a ribbon
        /// that follows the line, so a dash on a bend bends. A dash shorter than 5 cm is dropped (the cut end of the line).
        /// </summary>
        public static MarkingShape Dashed(IList<Vec3> centre, DashSpec spec, float phase = 0f)
        {
            var shape = new MarkingShape { Colour = spec.Colour };
            if (!(spec.Width > 0f))
                return shape;
            foreach (List<Vec3> piece in DashPieces(centre, spec.On, spec.Off, phase))
                Ribbon(shape.Mesh, piece, spec.Width);
            return shape;
        }

        /// <summary>
        /// The dashes of a pattern (on, off: m) along a line, from the phase (m into the pattern), each as the points of the line it covers: its two ends and the points of the line between them, so a dash on a bend
        /// bends. A dash shorter than 5 cm is dropped (the cut end of the line); an off of 0 is one dash the whole length.
        /// </summary>
        public static List<List<Vec3>> DashPieces(IList<Vec3> centre, float on, float off, float phase = 0f)
        {
            var pieces = new List<List<Vec3>>();
            if (centre == null || centre.Count < 2 || !(on > 0f) || float.IsNaN(phase) || float.IsInfinity(phase))
                return pieces;
            var at = new List<float>();
            float total = Lengths(centre, at);
            if (!(total > 0f))
                return pieces;
            foreach (Dash dash in Marking.Dashes(total, on, Math.Max(0f, off), phase))
            {
                var points = new List<Vec3> { PointAt(centre, at, dash.Start) };
                for (int i = 0; i < centre.Count; i++)
                {
                    // A point of the line inside the dash; one on top of the one before it (a repeated point) makes a quad of no area and is left out.
                    if (at[i] > dash.Start + 1e-4f && at[i] < dash.End - 1e-4f && Vec3.DistanceXZ(centre[i], points[points.Count - 1]) > 1e-4f)
                        points.Add(centre[i]);
                }

                points.Add(PointAt(centre, at, dash.End));
                pieces.Add(points);
            }

            return pieces;
        }

        /// <summary>The point of a line of points at a distance along it (m from the first), clamped to the line.</summary>
        public static Vec3 PointAt(IList<Vec3> points, IList<float> at, float distance)
        {
            if (distance <= 0f)
                return points[0];
            for (int i = 1; i < points.Count; i++)
            {
                if (distance <= at[i])
                {
                    float span = at[i] - at[i - 1];
                    return span < 1e-6f ? points[i] : Vec3.Lerp(points[i - 1], points[i], (distance - at[i - 1]) / span);
                }
            }

            return points[points.Count - 1];
        }
    }
}
