using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The small things that go with the markings, GB 5768.3-2025 chapter 8: raised pavement markers (突起路标, 8.1: reflective, the colour of the line they go with, 6 m to 15 m apart or closer) and delineators
    /// (轮廓标, 8.2: a post 70 cm high, 60 cm at the least, with a reflector on the face toward the driver; the spacing on a straight 50, 40 or 30 m by the class of road and on a curve by Table 7 from the radius).
    /// The standard gives the colour, the height and the spacing of these, but not their other sizes: a marker is made 10 cm square and 2.2 cm high, a delineator post 7 cm square with a reflector plate 15 x 9 cm;
    /// those are estimates. Both are runs along a road: they are placed at every spacing of the whole line (the line's start is the first place), and a piece of a line that starts phase metres along takes the
    /// places after that. Pure.
    /// </summary>
    public static class MarkingStuds
    {
        /// <summary>A marker's base side, its height, and the side of its top (m): an estimate, the standard gives none.</summary>
        public const float StudBase = 0.10f, StudHeight = 0.022f, StudTop = 0.06f;

        /// <summary>8.1.2: the markers 6 m to 15 m apart; the least the player may set is 2 m ("适当加密").</summary>
        public const float StudGapDefault = 10f, StudGapMin = 2f, StudGapMax = 15f;

        /// <summary>8.2.2 f): the height 70 cm, 60 cm at the least. The post's side and the reflector plate (width, height, thickness, and how far up its lower edge is, as shares of the post's height): estimates.</summary>
        public const float DelineatorHeight = 0.70f, DelineatorMinHeight = 0.60f, PostSide = 0.07f, PlateWidth = 0.15f, PlateHeight = 0.09f, PlateThickness = 0.012f;

        /// <summary>8.2.2 c): the spacing on a straight (m): 50 on a motorway (48 on a guard rail), 40 on a first-class road or an expressway, 30 on the others.</summary>
        public const float StraightHighway = 50f, StraightFirstClass = 40f, StraightOther = 30f;

        /// <summary>Table 7: the radius of the curve (m), the least of each row, and the spacing S (m) there: under 30 m 4; 30 to 89 8; 90 to 179 12; 180 to 274 16; 275 to 374 24; 375 to 999 32; 1000 to 1999 40; 2000 and over 48.</summary>
        public static readonly float[] CurveRadii = { 0f, 30f, 90f, 180f, 275f, 375f, 1000f, 2000f };

        public static readonly float[] CurveSpacings = { 4f, 8f, 12f, 16f, 24f, 32f, 40f, 48f };

        /// <summary>Table 7: the spacing S (m) on a curve of the given radius (m); a radius that is not a number or is not positive is the tightest row.</summary>
        public static float CurveSpacing(float radius)
        {
            if (float.IsNaN(radius) || radius <= 0f)
                return CurveSpacings[0];
            for (int i = CurveRadii.Length - 1; i >= 0; i--)
            {
                if (radius >= CurveRadii[i])
                    return CurveSpacings[i];
            }

            return CurveSpacings[0];
        }

        /// <summary>The spacing (m) of delineators along a line with this tightest radius (m): the curve's S, no more than the straight spacing of the road. A radius of 0 or less is "straight" (no curve).</summary>
        public static float DelineatorSpacing(float minRadius, float straight)
        {
            float s = Math.Max(1f, float.IsNaN(straight) ? StraightOther : straight);
            if (float.IsNaN(minRadius) || minRadius <= 0f)
                return s;
            return Math.Min(s, CurveSpacing(minRadius));
        }

        /// <summary>
        /// The tightest radius (m) of a line of points, from circles through three points each side of a place about 6 m along each way; float.MaxValue when it is straight (or too short to tell).
        /// </summary>
        public static float MinRadius(IList<Vec3> line)
        {
            float best = float.MaxValue;
            if (line == null || line.Count < 3)
                return best;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            const float reach = 6f;
            if (total < 2f * reach)
                return best;
            for (float s = reach; s <= total - reach + 1e-3f; s += 1f)
            {
                Vec3 a = RoadMarkings.PointAt(line, at, s - reach), b = RoadMarkings.PointAt(line, at, s), c = RoadMarkings.PointAt(line, at, s + reach);
                float r = CircleRadius(a, b, c);
                if (r < best)
                    best = r;
            }

            return best;
        }

        /// <summary>The radius of the circle through three points in the ground plane; float.MaxValue when they are in a line.</summary>
        public static float CircleRadius(Vec3 a, Vec3 b, Vec3 c)
        {
            double ab = Vec3.DistanceXZ(a, b), bc = Vec3.DistanceXZ(b, c), ca = Vec3.DistanceXZ(c, a);
            double cross = Math.Abs((b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X));
            if (cross < 1e-6)
                return float.MaxValue;
            return (float)(ab * bc * ca / (2.0 * cross));
        }

        /// <summary>The distances (m, from the start of the line, in order) of the places along a line of the given length where something every spacing goes: at 0, spacing, 2 spacing ... of the whole line; a piece that starts phase m along has those after phase.</summary>
        public static List<float> Stations(float length, float spacing, float phase)
        {
            var stations = new List<float>();
            if (!(length > 0f) || !(spacing > 0.05f) || float.IsInfinity(spacing))
                return stations;
            float first = 0f;
            if (!float.IsNaN(phase) && !float.IsInfinity(phase) && phase > 0f)
                first = (float)(Math.Floor(phase / spacing + 1e-6) + 1.0) * spacing - phase;
            for (float s = first; s <= length + 1e-4f && stations.Count < 2000; s += spacing)
                stations.Add(s);
            return stations;
        }

        // ---------------------------------------------------------------- the bodies

        private static readonly Vec3 Up = new Vec3(0f, 1f, 0f);

        /// <summary>A box standing on y0: its four sides and its top, the sides facing out, centred on (cx, cz) and turned by the heading (the way its depth runs).</summary>
        private static void Box(MeshBuilder mesh, float cx, float cz, Vec3 heading, float width, float depth, float y0, float height)
        {
            Vec3 forward = heading.HeadingXZ(), right = forward.RightXZ();
            float hw = width * 0.5f, hd = depth * 0.5f;
            Vec3 c = new Vec3(cx, 0f, cz);
            Vec3 p00 = c - right * hw - forward * hd, p10 = c + right * hw - forward * hd, p11 = c + right * hw + forward * hd, p01 = c - right * hw + forward * hd;
            Vec3[] bottom = { p00, p10, p11, p01 };
            Vec3[] outward = { forward * -1f, right, forward, right * -1f };
            for (int i = 0; i < 4; i++)
            {
                Vec3 a = bottom[i], b = bottom[(i + 1) % 4], n = outward[i];
                int v0 = mesh.Vertex(new Vec3(a.X, y0, a.Z), n, 0f, 0f), v1 = mesh.Vertex(new Vec3(b.X, y0, b.Z), n, 1f, 0f);
                int v2 = mesh.Vertex(new Vec3(b.X, y0 + height, b.Z), n, 1f, 1f), v3 = mesh.Vertex(new Vec3(a.X, y0 + height, a.Z), n, 0f, 1f);
                mesh.QuadFacing(v0, v1, v2, v3, n);
            }

            int t0 = mesh.Vertex(new Vec3(p00.X, y0 + height, p00.Z), Up, 0f, 0f), t1 = mesh.Vertex(new Vec3(p10.X, y0 + height, p10.Z), Up, 1f, 0f);
            int t2 = mesh.Vertex(new Vec3(p11.X, y0 + height, p11.Z), Up, 1f, 1f), t3 = mesh.Vertex(new Vec3(p01.X, y0 + height, p01.Z), Up, 0f, 1f);
            mesh.QuadFacing(t0, t1, t2, t3, Up);
        }

        /// <summary>A raised marker: a low block whose top is smaller than its base (the sides lean in), sides and top.</summary>
        public static void Stud(MeshBuilder mesh, float cx, float cz, Vec3 heading)
        {
            Vec3 forward = heading.HeadingXZ(), right = forward.RightXZ();
            float hb = StudBase * 0.5f, ht = StudTop * 0.5f;
            Vec3 c = new Vec3(cx, 0f, cz);
            Vec3[] low = { c - right * hb - forward * hb, c + right * hb - forward * hb, c + right * hb + forward * hb, c - right * hb + forward * hb };
            Vec3[] high = { c - right * ht - forward * ht, c + right * ht - forward * ht, c + right * ht + forward * ht, c - right * ht + forward * ht };
            Vec3[] out4 = { forward * -1f, right, forward, right * -1f };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vec3 n = MeshBuilder.Normalize(out4[i] * StudHeight + Up * (StudBase - StudTop) * 0.5f);
                int v0 = mesh.Vertex(low[i], n, 0f, 0f), v1 = mesh.Vertex(low[j], n, 1f, 0f);
                int v2 = mesh.Vertex(new Vec3(high[j].X, StudHeight, high[j].Z), n, 1f, 1f), v3 = mesh.Vertex(new Vec3(high[i].X, StudHeight, high[i].Z), n, 0f, 1f);
                mesh.QuadFacing(v0, v1, v2, v3, n);
            }

            int t0 = mesh.Vertex(new Vec3(high[0].X, StudHeight, high[0].Z), Up, 0f, 0f), t1 = mesh.Vertex(new Vec3(high[1].X, StudHeight, high[1].Z), Up, 1f, 0f);
            int t2 = mesh.Vertex(new Vec3(high[2].X, StudHeight, high[2].Z), Up, 1f, 1f), t3 = mesh.Vertex(new Vec3(high[3].X, StudHeight, high[3].Z), Up, 0f, 1f);
            mesh.QuadFacing(t0, t1, t2, t3, Up);
        }

        /// <summary>The heading of a line at a distance (the way the next metre of it runs), for a thing that stands on it.</summary>
        private static Vec3 HeadingAt(IList<Vec3> line, IList<float> at, float s)
        {
            int i = 1;
            while (i < line.Count - 1 && at[i] < s)
                i++;
            return (line[i] - line[i - 1]).HeadingXZ();
        }

        /// <summary>The raised markers along a line (8.1): one every gap metres of the whole line, in the colour of the line they go with.</summary>
        public static MarkingShape Studs(IList<Vec3> line, float gap, MarkingColour colour, float phase = 0f)
        {
            var shape = new MarkingShape { Colour = colour };
            if (line == null || line.Count < 2)
                return shape;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            float g = Math.Max(StudGapMin, Math.Min(StudGapMax, float.IsNaN(gap) ? StudGapDefault : gap));
            foreach (float s in Stations(total, g, phase))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                int before = shape.Mesh.VertexCount;
                Stud(shape.Mesh, p.X, p.Z, HeadingAt(line, at, s));
                // The marker stands on the road at the height of the line there.
                LiftFrom(shape.Mesh, before, p.Y);
            }

            return shape;
        }

        /// <summary>
        /// A delineator: a post of the given height, and the reflector on its face toward the driver (the face against the way the line runs). Returns the post in the white shape and the reflector in the coloured
        /// one, both made at height 0 for the caller to lift.
        /// </summary>
        public static void Delineator(MeshBuilder post, MeshBuilder reflector, float cx, float cz, Vec3 heading, float height)
        {
            float h = Math.Max(DelineatorMinHeight, Math.Min(1.2f, float.IsNaN(height) ? DelineatorHeight : height));
            Box(post, cx, cz, heading, PostSide, PostSide, 0f, h);
            // The reflector plate on the face that looks back along the line (toward the approaching driver), its lower edge at 0.74 of the height and its top at 0.87.
            Vec3 forward = heading.HeadingXZ();
            Vec3 at = new Vec3(cx, 0f, cz) - forward * (PostSide * 0.5f + PlateThickness * 0.5f);
            Box(reflector, at.X, at.Z, heading, PlateWidth, PlateThickness, h * 0.74f, PlateHeight);
        }

        /// <summary>The delineators along a line (8.2): one every spacing metres of the whole line, the posts white and the reflectors in the given colour (white, yellow, or red for an escape lane).</summary>
        public static List<MarkingShape> Delineators(IList<Vec3> line, float spacing, MarkingColour reflectorColour, float height, float phase = 0f)
        {
            var post = new MarkingShape { Colour = MarkingColour.White };
            var plate = new MarkingShape { Colour = reflectorColour };
            var shapes = new List<MarkingShape> { post, plate };
            if (line == null || line.Count < 2)
                return shapes;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                int postBefore = post.Mesh.VertexCount, plateBefore = plate.Mesh.VertexCount;
                Delineator(post.Mesh, plate.Mesh, p.X, p.Z, HeadingAt(line, at, s), height);
                LiftFrom(post.Mesh, postBefore, p.Y);
                LiftFrom(plate.Mesh, plateBefore, p.Y);
            }

            return shapes;
        }

        /// <summary>Raises the vertices from an index on by y: a body made at height 0 and put on the line at the line's height.</summary>
        private static void LiftFrom(MeshBuilder mesh, int from, float y)
        {
            for (int i = from; i < mesh.Positions.Count; i++)
            {
                Vec3 p = mesh.Positions[i];
                mesh.Positions[i] = new Vec3(p.X, p.Y + y, p.Z);
            }
        }
    }
}
