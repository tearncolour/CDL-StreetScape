using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The road-surface figures of GB 5768.3-2025 5.15 (线35 to 线40): the non-motor-vehicle mark (Fig. 51), the arrow that shows the way it goes (Fig. 52), the shared-road mark (Fig. 54), the electric-bicycle
    /// mark (Fig. 56), the pedestrian mark (Fig. 57) and the mark of a disabled person's bay (Fig. 59), all white (5.15.1).
    ///
    /// What is exact and what is read from the figure. The standard draws each figure on a grid and gives no outline in numbers, so the shapes here are built from strokes (a tube of a width with round ends), rings and
    /// a few polygons whose centre lines, radii and widths were read from the figures' grids (a cell of the grid is the unit of each figure's own measures below) and drawn in the order the figure shows. They
    /// are procedural look-alikes of the pictograms, not traced outlines; the overall proportions (width to height), the wheels, the frame and the posture follow the figures. The arrow of Fig. 52 is the exception:
    /// its measures are in the figure (150 long, a shaft 8 wide, a head 60 long and 24 wide) and are exact, and it is not scaled with the figure it goes with. A figure is as wide as the player sets it
    /// (5.15.1: half the width of the lane or the bay, to a multiple of 10 cm). Each shape is centred on its own middle: +Z is the way the traffic runs over it, +X the driver's right, and the figures are
    /// drawn as they are in the standard (the bicycle faces left). Pure.
    /// </summary>
    public static class MarkingPictograms
    {
        /// <summary>The arrow of Fig. 52 (m): the whole length, the shaft's width, the head's length and its width (the head reaches 8 cm out on each side of the shaft).</summary>
        public const float ArrowLength = 1.5f, ArrowShaft = 0.08f, ArrowHead = 0.6f, ArrowHeadWidth = 0.24f;

        /// <summary>The distance between a figure and the arrow after it, and between the figures of a row (5.15.2, 5.15.5: 1 m).</summary>
        public const float Gap = 1f;

        /// <summary>The figures' own grids (cells): the width and the height each was read on. A figure's width in metres divided by its width in cells is the size of a cell.</summary>
        public const float BicycleCells = 25f, BicycleRows = 15.6f, EBikeCells = 20f, EBikeRows = 12.3f, PedestrianCells = 12f, PedestrianRows = 18f, WheelchairCells = 17f, WheelchairRows = 18.7f;

        // ---------------------------------------------------------------- drawing primitives (in the figure's own units; X right, Z up the figure)

        private static readonly Vec3 Up = new Vec3(0f, 1f, 0f);

        /// <summary>A disc of the given radius, a fan of up-facing triangles.</summary>
        internal static void Disc(MeshBuilder mesh, float cx, float cz, float radius, int segments = 24)
        {
            if (!(radius > 0f))
                return;
            int centre = mesh.Vertex(new Vec3(cx, 0f, cz), Up, 0.5f, 0.5f);
            int first = -1, previous = -1;
            for (int i = 0; i < segments; i++)
            {
                double a = 2.0 * Math.PI * i / segments;
                int v = mesh.Vertex(new Vec3(cx + radius * (float)Math.Cos(a), 0f, cz + radius * (float)Math.Sin(a)), Up, 0f, 0f);
                if (first < 0)
                    first = v;
                if (previous >= 0)
                    mesh.TriFacing(centre, previous, v, Up);
                previous = v;
            }

            mesh.TriFacing(centre, previous, first, Up);
        }

        /// <summary>A straight tube from one point to another: a rectangle of the width, with a half disc at each end when round.</summary>
        internal static void Stroke(MeshBuilder mesh, float x0, float z0, float x1, float z1, float width, bool round = true)
        {
            float dx = x1 - x0, dz = z1 - z0, length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length > 1e-6f)
            {
                float nx = -dz / length * width * 0.5f, nz = dx / length * width * 0.5f;
                int a = mesh.Vertex(new Vec3(x0 + nx, 0f, z0 + nz), Up, 0f, 0f);
                int b = mesh.Vertex(new Vec3(x1 + nx, 0f, z1 + nz), Up, 0f, 1f);
                int c = mesh.Vertex(new Vec3(x1 - nx, 0f, z1 - nz), Up, 1f, 1f);
                int d = mesh.Vertex(new Vec3(x0 - nx, 0f, z0 - nz), Up, 1f, 0f);
                mesh.QuadFacing(a, b, c, d, Up);
            }

            if (round)
            {
                Disc(mesh, x0, z0, width * 0.5f, 12);
                Disc(mesh, x1, z1, width * 0.5f, 12);
            }
        }

        /// <summary>A tube through several points (x, z pairs): its pieces end in discs, so the bends are round.</summary>
        internal static void Chain(MeshBuilder mesh, float width, params float[] points)
        {
            for (int i = 0; i + 3 < points.Length; i += 2)
                Stroke(mesh, points[i], points[i + 1], points[i + 2], points[i + 3], width, false);
            for (int i = 0; i + 1 < points.Length; i += 2)
                Disc(mesh, points[i], points[i + 1], width * 0.5f, 12);
        }

        /// <summary>The part of a ring between two angles (radians, counter-clockwise from +X): a wheel, or the arc of a wheelchair's wheel.</summary>
        internal static void Sector(MeshBuilder mesh, float cx, float cz, float outer, float inner, float from, float to, int segments)
        {
            if (!(outer > inner) || inner < 0f)
                return;
            segments = Math.Max(2, segments);
            int previousOuter = -1, previousInner = -1;
            for (int i = 0; i <= segments; i++)
            {
                double a = from + (to - from) * i / segments;
                float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                int o = mesh.Vertex(new Vec3(cx + outer * c, 0f, cz + outer * s), Up, 0f, 0f);
                int n = mesh.Vertex(new Vec3(cx + inner * c, 0f, cz + inner * s), Up, 1f, 0f);
                if (previousOuter >= 0)
                    mesh.QuadFacing(previousOuter, o, n, previousInner, Up);
                previousOuter = o;
                previousInner = n;
            }
        }

        /// <summary>A ring: a wheel's tyre.</summary>
        internal static void Ring(MeshBuilder mesh, float cx, float cz, float outer, float inner, int segments = 40) => Sector(mesh, cx, cz, outer, inner, 0f, (float)(2.0 * Math.PI), segments);

        private static void Fill(MeshBuilder mesh, params float[] points)
        {
            var outline = new List<Vec3>();
            for (int i = 0; i + 1 < points.Length; i += 2)
                outline.Add(new Vec3(points[i], 0f, points[i + 1]));
            Polygon.Fill(mesh, outline);
        }

        /// <summary>Adds a mesh made in a figure's units into a shape, scaled to metres and moved.</summary>
        private static void Place(MarkingShape dst, MeshBuilder src, float k, float dx, float dz)
        {
            MeshBuilder mesh = dst.Mesh;
            int first = mesh.VertexCount;
            for (int i = 0; i < src.Positions.Count; i++)
            {
                Vec3 p = src.Positions[i];
                mesh.Vertex(new Vec3(p.X * k + dx, 0f, p.Z * k + dz), Up, src.U[i], src.V[i]);
            }

            for (int i = 0; i + 2 < src.Indices.Count; i += 3)
                mesh.Tri(first + src.Indices[i], first + src.Indices[i + 1], first + src.Indices[i + 2]);
        }

        /// <summary>Moves a shape so that the middle of its box is the origin.</summary>
        private static MarkingShape Centred(MarkingShape shape)
        {
            if (shape.Mesh.VertexCount == 0)
                return shape;
            shape.Mesh.Bounds(out Vec3 min, out Vec3 max);
            float cx = (min.X + max.X) * 0.5f, cz = (min.Z + max.Z) * 0.5f;
            for (int i = 0; i < shape.Mesh.Positions.Count; i++)
            {
                Vec3 p = shape.Mesh.Positions[i];
                shape.Mesh.Positions[i] = new Vec3(p.X - cx, p.Y, p.Z - cz);
            }

            return shape;
        }

        private static float Clamp(float v, float lo, float hi) => float.IsNaN(v) ? lo : Math.Max(lo, Math.Min(hi, v));

        // ---------------------------------------------------------------- the figures, in their own units (cells of each figure's grid)

        /// <summary>The non-motor-vehicle mark (Fig. 51): two wheels, the frame, the fork and handlebar, the saddle and the crank. 25 cells wide, 15.6 high; the bicycle faces left.</summary>
        internal static MeshBuilder BicycleUnits()
        {
            var m = new MeshBuilder();
            // The wheels: rings 4.0 cells out and 1.25 thick.
            Ring(m, 4.8f, 4.6f, 4.0f, 2.75f);
            Ring(m, 20.3f, 4.6f, 4.0f, 2.75f);
            // The crank: a ring, and the two cranks with their pedals.
            Ring(m, 14.0f, 4.7f, 1.45f, 0.9f, 20);
            Stroke(m, 14.0f, 4.7f, 13.4f, 7.0f, 0.8f);
            Stroke(m, 14.0f, 4.7f, 14.6f, 2.4f, 0.8f);
            // The frame: down tube, seat tube, top tube, chain stay and seat stay.
            Stroke(m, 14.0f, 4.7f, 7.6f, 10.2f, 1.2f);
            Stroke(m, 14.0f, 4.7f, 15.8f, 10.2f, 1.2f);
            Stroke(m, 8.0f, 10.4f, 15.8f, 10.4f, 1.0f);
            Stroke(m, 14.0f, 4.7f, 20.3f, 4.6f, 1.0f);
            Stroke(m, 20.3f, 4.6f, 16.2f, 10.2f, 1.0f);
            // The fork and the handlebar.
            Stroke(m, 4.8f, 4.6f, 7.6f, 11.0f, 1.0f);
            Stroke(m, 7.6f, 10.2f, 8.2f, 13.0f, 1.0f);
            Stroke(m, 6.9f, 13.4f, 10.0f, 13.4f, 1.2f);
            // The saddle and its post.
            Stroke(m, 16.0f, 10.2f, 16.3f, 12.3f, 1.0f);
            Stroke(m, 15.0f, 12.9f, 18.2f, 12.9f, 1.3f);
            return m;
        }

        /// <summary>The electric-bicycle mark (Fig. 56): two wheels, the front fork and handlebar, the body, the long saddle and the swing arm. 20 cells wide, 12.3 high; it faces left.</summary>
        internal static MeshBuilder ElectricBicycleUnits()
        {
            var m = new MeshBuilder();
            Ring(m, 4.0f, 4.2f, 3.4f, 2.4f);
            Ring(m, 16.1f, 3.8f, 3.35f, 2.35f);
            Ring(m, 10.3f, 3.8f, 1.0f, 0.6f, 20);
            // The fork from the front hub to the handlebar and the handlebar's grips.
            Stroke(m, 4.0f, 4.2f, 7.2f, 11.2f, 1.0f);
            Stroke(m, 6.4f, 11.6f, 9.0f, 11.6f, 1.0f);
            // The body under the seat.
            Fill(m, 7.3f, 9.4f, 10.0f, 9.2f, 10.7f, 8.0f, 9.6f, 6.2f, 7.8f, 6.2f, 6.9f, 7.6f);
            // The swing arm from the middle of the bike to the back wheel, and the long saddle over it.
            Stroke(m, 10.3f, 3.8f, 16.1f, 3.8f, 1.0f);
            Stroke(m, 13.2f, 8.65f, 18.2f, 8.65f, 1.4f);
            Stroke(m, 15.5f, 8.2f, 16.1f, 5.4f, 0.8f);
            return m;
        }

        /// <summary>The pedestrian mark (Fig. 57): a head, a body, two arms swinging, two legs in step. 12 cells wide, 18 high; the figure walks to the left.</summary>
        internal static MeshBuilder PedestrianUnits()
        {
            var m = new MeshBuilder();
            Disc(m, 6.85f, 15.75f, 1.65f, 28);
            Fill(m, 5.0f, 13.4f, 8.7f, 13.4f, 8.4f, 6.5f, 5.4f, 6.5f);
            Chain(m, 1.5f, 5.4f, 12.8f, 3.4f, 10.3f, 1.5f, 9.3f);
            Chain(m, 1.5f, 8.4f, 12.9f, 10.2f, 11.0f, 10.3f, 8.2f);
            Chain(m, 2.0f, 5.9f, 6.8f, 3.9f, 4.3f, 3.3f, 1.7f);
            Chain(m, 2.0f, 7.1f, 6.6f, 9.4f, 3.0f, 10.2f, 1.5f);
            return m;
        }

        /// <summary>The mark of a disabled person's bay (Fig. 59): a head, a seated body, an arm, the legs, and the wheel as an arc. 17 cells wide, 18.7 high; it faces left.</summary>
        internal static MeshBuilder WheelchairUnits()
        {
            var m = new MeshBuilder();
            Disc(m, 10.9f, 16.3f, 1.4f, 24);
            Stroke(m, 10.7f, 14.2f, 10.5f, 7.0f, 2.6f);
            Chain(m, 1.3f, 10.3f, 12.6f, 10.0f, 10.5f, 5.8f, 10.5f);
            Chain(m, 1.9f, 10.5f, 6.9f, 3.9f, 6.9f, 1.7f, 2.2f);
            // The wheel: an arc of 240 degrees round the chair, open at the front; the circle is centred at (10.7, 6.2) and 5.2 cells out, 1.0 thick.
            Sector(m, 10.7f, 6.2f, 5.2f, 4.2f, (float)(-169.0 * Math.PI / 180.0), (float)(70.0 * Math.PI / 180.0), 40);
            return m;
        }

        // ---------------------------------------------------------------- the shapes

        /// <summary>The arrow of Fig. 52 that shows the way a bicycle or a pedestrian goes, its tail at z0 and the middle of its shaft at x: exact measures, not scaled.</summary>
        public static void TravelArrow(MeshBuilder mesh, float x, float z0)
        {
            float shaftTop = z0 + ArrowLength - ArrowHead;
            RoadMarkings.Rect(mesh, x - ArrowShaft * 0.5f, z0, x + ArrowShaft * 0.5f, shaftTop);
            int a = mesh.Vertex(new Vec3(x - ArrowHeadWidth * 0.5f, 0f, shaftTop), Up, 0f, 0f);
            int b = mesh.Vertex(new Vec3(x + ArrowHeadWidth * 0.5f, 0f, shaftTop), Up, 1f, 0f);
            int c = mesh.Vertex(new Vec3(x, 0f, z0 + ArrowLength), Up, 0.5f, 1f);
            mesh.TriFacing(a, b, c, Up);
        }

        /// <summary>The arrow alone (线36), centred.</summary>
        public static MarkingShape Arrow()
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            TravelArrow(shape.Mesh, 0f, 0f);
            return Centred(shape);
        }

        /// <summary>The widest a figure is made (m): a lane of 3.5 m halved, with room.</summary>
        public const float MaxWidth = 3.0f, MinWidth = 0.4f;

        /// <summary>The non-motor-vehicle mark of the given width (m; 5.15.1: half a lane, to a multiple of 10 cm), with the arrow of 5.15.2 a metre in front of it when asked (线35, 线36).</summary>
        public static MarkingShape Bicycle(float width, bool arrow)
        {
            float k = Clamp(width, MinWidth, MaxWidth) / BicycleCells;
            var shape = new MarkingShape { Colour = MarkingColour.White };
            Place(shape, BicycleUnits(), k, 0f, 0f);
            if (arrow)
                TravelArrow(shape.Mesh, BicycleCells * k * 0.5f, BicycleRows * k + Gap);
            return Centred(shape);
        }

        /// <summary>The electric-bicycle mark of the given width (m), with the arrow of 5.15.4 a metre in front of it when asked (线38).</summary>
        public static MarkingShape ElectricBicycle(float width, bool arrow)
        {
            float k = Clamp(width, MinWidth, MaxWidth) / EBikeCells;
            var shape = new MarkingShape { Colour = MarkingColour.White };
            Place(shape, ElectricBicycleUnits(), k, 0f, 0f);
            if (arrow)
                TravelArrow(shape.Mesh, EBikeCells * k * 0.5f, EBikeRows * k + Gap);
            return Centred(shape);
        }

        /// <summary>
        /// The pedestrian mark of the given height (m), and with it, in the order of 5.15.5 along the way the traffic goes and a metre apart, the non-motor-vehicle mark before it (as wide as the figure
        /// is high, as in Fig. 58) and the arrow after it (线39).
        /// </summary>
        public static MarkingShape Pedestrian(float height, bool bicycle, bool arrow)
        {
            float k = Clamp(height, 0.5f, 3.0f) / PedestrianRows;
            var shape = new MarkingShape { Colour = MarkingColour.White };
            float z = 0f, centre = PedestrianCells * k * 0.5f;
            if (bicycle)
            {
                float kb = PedestrianRows * k / BicycleCells;
                Place(shape, BicycleUnits(), kb, centre - BicycleCells * kb * 0.5f, 0f);
                z = BicycleRows * kb + Gap;
            }

            Place(shape, PedestrianUnits(), k, 0f, z);
            z += PedestrianRows * k;
            if (arrow)
                TravelArrow(shape.Mesh, centre, z + Gap);
            return Centred(shape);
        }

        /// <summary>The mark of a disabled person's bay of the given width (m).</summary>
        public static MarkingShape Wheelchair(float width)
        {
            float k = Clamp(width, MinWidth, MaxWidth) / WheelchairCells;
            var shape = new MarkingShape { Colour = MarkingColour.White };
            Place(shape, WheelchairUnits(), k, 0f, 0f);
            return Centred(shape);
        }

        /// <summary>
        /// The shared-road mark (Fig. 54, 线37): two bands of chevrons pointing the way the traffic runs, over the non-motor-vehicle mark. The figure is as wide as the chevrons (5.15.3: as wide as the shared
        /// surface and not under 0.5 m). The bands are 45 degrees to the way, 2.4 cells thick (up and down the figure) and 2.4 apart, the bicycle 19 cells wide under them (the figure's grid).
        /// </summary>
        public static MarkingShape Shared(float width)
        {
            const float half = 9.2f, thick = 2.4f, gap = 2.4f, bike = 19.1f;
            float k = Clamp(width, MinWidth, MaxWidth) / (2f * half);
            var shape = new MarkingShape { Colour = MarkingColour.White };
            float kb = bike / BicycleCells;
            Place(shape, BicycleUnits(), k * kb, half * k - BicycleCells * kb * k * 0.5f, 0f);
            float bikeTop = BicycleRows * kb;
            // The lowest end of the chevrons is just over the bicycle's top.
            float apex = bikeTop + 0.3f + 2f * thick + gap + half;
            var bands = new MeshBuilder();
            for (int i = 0; i < 2; i++)
            {
                float a = apex - i * (thick + gap);
                Fill(bands, 0f, a, half, a - half, half, a - half - thick, 0f, a - thick, -half, a - half - thick, -half, a - half);
            }

            Place(shape, bands, k, half * k, 0f);
            return Centred(shape);
        }
    }
}
