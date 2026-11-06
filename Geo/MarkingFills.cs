using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Fills and outlines of areas for the road markings (GB 5768.3-2025): the hatching of channelising islands, of the space between double yellow lines, of the space beside a disabled bay
    /// (导流线、填充线), the band along the edge of an area (the outline of a grid, of a bay, of the diamond), and the sideways offset of a line. Frame: X right, Z the way the traffic runs, Y kept
    /// at 0; triangles face up. Pure.
    /// </summary>
    public static class MarkingFills
    {
        /// <summary>
        /// Hatching inside a simple polygon (either order): bands of the given width whose centre lines are pitch apart, at angleDeg from the +Z axis leaning toward +X as they go forward
        /// (45 is the "45 degrees to the direction of travel" of the standard; 135 leans the other way). Each band is cut exactly at the polygon's edges: the polygon is triangulated and the
        /// band is clipped against every triangle, so nothing is painted outside, whatever the shape. phase moves the family across (m). Returns how many bands left paint.
        /// </summary>
        public static int Hatch(MeshBuilder mesh, IList<Vec3> polygon, float width, float pitch, float angleDeg, float phase = 0f) =>
            polygon == null || polygon.Count < 3 ? 0 : Hatch(mesh, new AreaIndex(polygon), polygon, width, pitch, angleDeg, phase);

        /// <summary>As Hatch, over an area that is already indexed (AreaIndex.FromStrip for a strip between two lines).</summary>
        public static int Hatch(MeshBuilder mesh, AreaIndex index, IList<Vec3> polygon, float width, float pitch, float angleDeg, float phase = 0f)
        {
            if (index == null || !index.Ok || polygon == null || polygon.Count < 3 || !(width > 0f) || !(pitch > 0f) || float.IsNaN(angleDeg) || float.IsInfinity(angleDeg) || float.IsNaN(phase) || float.IsInfinity(phase))
                return 0;
            double a = angleDeg * Math.PI / 180.0;
            var d = new Vec3((float)Math.Sin(a), 0f, (float)Math.Cos(a));
            var n = new Vec3((float)Math.Cos(a), 0f, -(float)Math.Sin(a));
            float tMin = float.MaxValue, tMax = float.MinValue, sMin = float.MaxValue, sMax = float.MinValue;
            foreach (Vec3 p in polygon)
            {
                float t = Dot(p, n), s = Dot(p, d);
                tMin = Math.Min(tMin, t);
                tMax = Math.Max(tMax, t);
                sMin = Math.Min(sMin, s);
                sMax = Math.Max(sMax, s);
            }

            if (!(tMax > tMin) || !(sMax > sMin))
                return 0;
            int drawn = 0;
            float start = tMin + ((phase % pitch) + pitch) % pitch;
            float half = width * 0.5f;
            var band = new List<Vec3>(4);
            for (float t = start; t <= tMax; t += pitch)
            {
                // Where the band crosses the area along d; a band that crosses none of it is beside it (or wider than all of it: then it covers it, from end to end).
                Vec3 o = n * t;
                float s0, s1;
                if (!index.BandSpan(o, d, half, out s0, out s1))
                {
                    if (tMin < t - half || tMax > t + half)
                        continue;
                    s0 = sMin - 1f;
                    s1 = sMax + 1f;
                }

                band.Clear();
                band.Add(n * (t - half) + d * s0);
                band.Add(n * (t + half) + d * s0);
                band.Add(n * (t + half) + d * s1);
                band.Add(n * (t - half) + d * s1);
                if (index.ClipWalk(mesh, band, o + d * s0, d, s1 - s0, half) > 0)
                    drawn++;
            }

            return drawn;
        }

        /// <summary>
        /// Paints the part of a convex piece (a band, an arm of a V; either order) that lies inside an area given as its triangles (Polygon.Fill of the area): the piece is clipped against every triangle,
        /// so nothing is painted outside the area, whatever its shape. Returns how many pieces of it were painted.
        /// </summary>
        public static int ClipConvex(MeshBuilder mesh, IList<Vec3> convex, MeshBuilder areaTriangles) => ClipConvex(mesh, convex, areaTriangles, new List<Vec3>(8), new List<Vec3>(8));

        private static int ClipConvex(MeshBuilder mesh, IList<Vec3> convex, MeshBuilder areaTriangles, List<Vec3> piece, List<Vec3> scratch)
        {
            if (convex == null || convex.Count < 3 || areaTriangles == null)
                return 0;
            // The piece counter-clockwise, as the clipping keeps the left of each edge.
            bool reversed = Polygon.SignedArea2(convex) < 0f;
            var up = new Vec3(0f, 1f, 0f);
            int made = 0;
            for (int i = 0; i + 2 < areaTriangles.Indices.Count; i += 3)
            {
                Vec3 ta = areaTriangles.Positions[areaTriangles.Indices[i]], tb = areaTriangles.Positions[areaTriangles.Indices[i + 1]], tc = areaTriangles.Positions[areaTriangles.Indices[i + 2]];
                // A sliver of an area triangle (collinear corners of the area, made into a triangle by the triangulation) is left out: it covers nothing, and the lines of its edges are so nearly one line
                // that the clipping could keep a band along it far beyond the triangle.
                float cross = Polygon.Cross(ta, tb, tc);
                float longest = Math.Max(Vec3.DistanceXZ(ta, tb), Math.Max(Vec3.DistanceXZ(tb, tc), Vec3.DistanceXZ(tc, ta)));
                if (!(longest > 1e-6f) || Math.Abs(cross) / longest < 1e-4f)
                    continue;
                // The triangle counter-clockwise in (X, Z), so that "inside" is the left of each edge.
                if (cross < 0f)
                {
                    Vec3 swap = tb;
                    tb = tc;
                    tc = swap;
                }

                piece.Clear();
                if (reversed)
                {
                    for (int k = convex.Count - 1; k >= 0; k--)
                        piece.Add(convex[k]);
                }
                else
                {
                    piece.AddRange(convex);
                }

                Clip(piece, scratch, ta, tb);
                Clip(piece, scratch, tb, tc);
                Clip(piece, scratch, tc, ta);
                if (piece.Count < 3 || Math.Abs(Polygon.SignedArea2(piece)) < 1e-8f)
                    continue;
                // Whatever the numbers did, the piece is inside the triangle: one that is not is left out.
                float bx0 = Math.Min(ta.X, Math.Min(tb.X, tc.X)) - 1e-3f, bx1 = Math.Max(ta.X, Math.Max(tb.X, tc.X)) + 1e-3f, bz0 = Math.Min(ta.Z, Math.Min(tb.Z, tc.Z)) - 1e-3f, bz1 = Math.Max(ta.Z, Math.Max(tb.Z, tc.Z)) + 1e-3f;
                bool within = true;
                foreach (Vec3 p in piece)
                    within &= p.X >= bx0 && p.X <= bx1 && p.Z >= bz0 && p.Z <= bz1;
                if (!within)
                    continue;
                // The points that clipping put on top of one another are one point; a triangle of the fan with no area is left out (it would face neither up nor down).
                for (int k = piece.Count - 1; k > 0 && piece.Count > 2; k--)
                {
                    if (Vec3.DistanceXZ(piece[k], piece[k - 1]) < 1e-5f)
                        piece.RemoveAt(k);
                }

                if (piece.Count > 2 && Vec3.DistanceXZ(piece[0], piece[piece.Count - 1]) < 1e-5f)
                    piece.RemoveAt(piece.Count - 1);
                if (piece.Count < 3)
                    continue;
                int first = mesh.VertexCount;
                foreach (Vec3 p in piece)
                    mesh.Vertex(new Vec3(p.X, 0f, p.Z), up, p.X, p.Z);
                for (int k = 1; k + 1 < piece.Count; k++)
                {
                    if (Math.Abs(Polygon.Cross(piece[0], piece[k], piece[k + 1])) > 1e-9f)
                        mesh.TriFacing(first, first + k, first + k + 1, up);
                }

                made++;
            }

            return made;
        }

        /// <summary>
        /// A simple polygon moved inward by a distance (its edges parallel, the corners mitred, a mitre no longer than five times the distance), counter-clockwise; null when the result is not a simple polygon
        /// any more (the area is narrower than twice the distance somewhere, or a corner folds), so that the caller can fall back to the area itself.
        /// </summary>
        public static List<Vec3> Inset(IList<Vec3> polygon, float distance)
        {
            if (polygon == null || polygon.Count < 3 || float.IsNaN(distance))
                return null;
            var ring = new List<Vec3>(polygon);
            while (ring.Count > 1 && Vec3.DistanceXZ(ring[0], ring[ring.Count - 1]) < 1e-6f)
                ring.RemoveAt(ring.Count - 1);
            if (ring.Count < 3)
                return null;
            if (Polygon.SignedArea2(ring) < 0f)
                ring.Reverse();
            if (!(distance > 0f))
                return ring;
            int m = ring.Count;
            var inner = new List<Vec3>(m);
            for (int i = 0; i < m; i++)
            {
                Vec3 prev = ring[(i + m - 1) % m], cur = ring[i], next = ring[(i + 1) % m];
                Vec3 e0 = (cur - prev).HeadingXZ(), e1 = (next - cur).HeadingXZ();
                var n0 = new Vec3(-e0.Z, 0f, e0.X);
                var n1 = new Vec3(-e1.Z, 0f, e1.X);
                Vec3 bis = n0 + n1;
                float len = bis.LengthXZ;
                if (len < 1e-4f)
                {
                    inner.Add(cur + n1 * distance);
                    continue;
                }

                bis = bis * (1f / len);
                float cosHalf = Vec3.Dot(bis, n1);
                float mitre = cosHalf > 0.2f ? distance / cosHalf : distance * 5f;
                inner.Add(cur + bis * mitre);
            }

            if (Polygon.SignedArea2(inner) <= 0f || !IsSimple(inner))
                return null;
            return inner;
        }

        /// <summary>Whether a closed polygon is simple: no two of its edges that are not neighbours cross.</summary>
        public static bool IsSimple(IList<Vec3> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return false;
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Vec3 a = polygon[i], b = polygon[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i || (j + 1) % n == i || j == (i + 1) % n)
                        continue;
                    if (Polygon.Segments(a, b, polygon[j], polygon[(j + 1) % n]))
                        return false;
                }
            }

            return true;
        }

        /// <summary>Sutherland-Hodgman, for AreaIndex: keeps the part of the convex polygon on the left of (or on) the line a-b.</summary>
        internal static void ClipHalfPlane(List<Vec3> poly, List<Vec3> scratch, Vec3 a, Vec3 b) => Clip(poly, scratch, a, b);

        /// <summary>
        /// Writes a clipped piece as a fan of up-facing triangles, for AreaIndex: a piece that the numbers put outside its triangle's box is left out, points on top of one another are one, and a triangle of the fan
        /// with no area is not written. False when nothing was written.
        /// </summary>
        internal static bool EmitPiece(MeshBuilder mesh, List<Vec3> piece, float boxMinX, float boxMaxX, float boxMinZ, float boxMaxZ, Vec3 up)
        {
            foreach (Vec3 p in piece)
            {
                if (p.X < boxMinX - 1e-3f || p.X > boxMaxX + 1e-3f || p.Z < boxMinZ - 1e-3f || p.Z > boxMaxZ + 1e-3f)
                    return false;
            }

            for (int k = piece.Count - 1; k > 0 && piece.Count > 2; k--)
            {
                if (Vec3.DistanceXZ(piece[k], piece[k - 1]) < 1e-5f)
                    piece.RemoveAt(k);
            }

            if (piece.Count > 2 && Vec3.DistanceXZ(piece[0], piece[piece.Count - 1]) < 1e-5f)
                piece.RemoveAt(piece.Count - 1);
            if (piece.Count < 3)
                return false;
            int first = mesh.VertexCount;
            foreach (Vec3 p in piece)
                mesh.Vertex(new Vec3(p.X, 0f, p.Z), up, p.X, p.Z);
            for (int k = 1; k + 1 < piece.Count; k++)
            {
                if (Math.Abs(Polygon.Cross(piece[0], piece[k], piece[k + 1])) > 1e-9f)
                    mesh.TriFacing(first, first + k, first + k + 1, up);
            }

            return true;
        }

        /// <summary>Sutherland-Hodgman: keeps the part of the convex polygon on the left of (or on) the line a-b.</summary>
        private static void Clip(List<Vec3> poly, List<Vec3> scratch, Vec3 a, Vec3 b)
        {
            scratch.Clear();
            int m = poly.Count;
            for (int i = 0; i < m; i++)
            {
                Vec3 p = poly[i], q = poly[(i + 1) % m];
                float sp = Polygon.Cross(a, b, p), sq = Polygon.Cross(a, b, q);
                bool pin = sp >= 0f, qin = sq >= 0f;
                if (pin)
                    scratch.Add(p);
                if (pin != qin)
                {
                    float f = sp / (sp - sq);
                    scratch.Add(Vec3.Lerp(p, q, f));
                }
            }

            poly.Clear();
            poly.AddRange(scratch);
        }

        private static float Dot(Vec3 p, Vec3 axis) => p.X * axis.X + p.Z * axis.Z;

        /// <summary>
        /// The band along the inside of a closed polygon (either order; it is made counter-clockwise), of the given width, with mitred corners: the outline of an area as the standard draws it
        /// (the line is inside the figure's dimensions). Returns how many quads were drawn (one an edge).
        /// </summary>
        public static int Outline(MeshBuilder mesh, IList<Vec3> polygon, float width)
        {
            if (polygon == null || polygon.Count < 3 || !(width > 0f))
                return 0;
            var ring = new List<Vec3>(polygon);
            while (ring.Count > 1 && Vec3.DistanceXZ(ring[0], ring[ring.Count - 1]) < 1e-6f)
                ring.RemoveAt(ring.Count - 1);
            if (ring.Count < 3)
                return 0;
            if (Polygon.SignedArea2(ring) < 0f)
                ring.Reverse();
            int m = ring.Count;
            var inner = new Vec3[m];
            for (int i = 0; i < m; i++)
            {
                Vec3 prev = ring[(i + m - 1) % m], cur = ring[i], next = ring[(i + 1) % m];
                Vec3 e0 = (cur - prev).HeadingXZ(), e1 = (next - cur).HeadingXZ();
                // Inward of a counter-clockwise ring (X right, Z up) is the left of the edge: (-dz, dx).
                var n0 = new Vec3(-e0.Z, 0f, e0.X);
                var n1 = new Vec3(-e1.Z, 0f, e1.X);
                Vec3 bis = n0 + n1;
                float len = bis.LengthXZ;
                if (len < 1e-4f)
                {
                    inner[i] = cur + n1 * width;
                    continue;
                }

                bis = bis * (1f / len);
                float cosHalf = Vec3.Dot(bis, n1);
                float mitre = cosHalf > 0.2f ? width / cosHalf : width * 5f;
                inner[i] = cur + bis * mitre;
            }

            var up = new Vec3(0f, 1f, 0f);
            int made = 0;
            for (int i = 0; i < m; i++)
            {
                int k = (i + 1) % m;
                int a = mesh.Vertex(ring[i], up, 0f, 0f), b = mesh.Vertex(ring[k], up, 1f, 0f), c = mesh.Vertex(inner[k], up, 1f, 1f), d = mesh.Vertex(inner[i], up, 0f, 1f);
                mesh.QuadFacing(a, b, c, d, up);
                made++;
            }

            return made;
        }

        /// <summary>A filled polygon (either order), the same as Polygon.Fill, for the solid figures (a triangle, a half disc).</summary>
        public static int Solid(MeshBuilder mesh, IList<Vec3> polygon) => Polygon.Fill(mesh, polygon);

        /// <summary>The corners of an axis-aligned rectangle from (x0, z0) to (x1, z1), counter-clockwise.</summary>
        public static List<Vec3> Rectangle(float x0, float z0, float x1, float z1) => new List<Vec3> { new Vec3(x0, 0f, z0), new Vec3(x1, 0f, z0), new Vec3(x1, 0f, z1), new Vec3(x0, 0f, z1) };

        /// <summary>
        /// The vertices of a mesh from first on put at the height of a line beside them (0.12.83): each at the height the line has at its own nearest place, between its points. A fill is cut by an
        /// area in the plane (AreaIndex.ClipWalk emits flat pieces, at height 0), so that the bars of a strip along a road that climbs or falls stayed at the height of the strip's first point while its
        /// edge lines (Ribbon) followed the road: on a ramp they hung over the water and the grass beside it, or lay under the road. A line of one height changes nothing.
        /// </summary>
        public static void FollowLine(MeshBuilder mesh, int first, IList<Vec3> line)
        {
            if (mesh == null || line == null || line.Count < 2)
                return;
            for (int v = first; v < mesh.VertexCount; v++)
            {
                Vec3 p = mesh.Positions[v];
                float best = float.MaxValue, height = line[0].Y;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    Vec3 a = line[i], b = line[i + 1];
                    float dx = b.X - a.X, dz = b.Z - a.Z, length2 = dx * dx + dz * dz;
                    float t = length2 > 1e-12f ? Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length2)) : 0f;
                    float ex = a.X + dx * t - p.X, ez = a.Z + dz * t - p.Z, distance = ex * ex + ez * ez;
                    if (distance < best)
                    {
                        best = distance;
                        height = a.Y + (b.Y - a.Y) * t;
                    }
                }

                mesh.Positions[v] = new Vec3(p.X, height, p.Z);
            }
        }

        /// <summary>A line of points moved sideways by lateral (positive to the right of the way it runs: +X for a line running +Z, outward for a counter-clockwise arc), each point by the mean of its two segments' normals.</summary>
        public static List<Vec3> Offset(IList<Vec3> line, float lateral)
        {
            var moved = new List<Vec3>(line.Count);
            for (int i = 0; i < line.Count; i++)
            {
                Vec3 heading;
                if (line.Count < 2)
                    heading = new Vec3(0f, 0f, 1f);
                else if (i == 0)
                    heading = (line[1] - line[0]).HeadingXZ();
                else if (i == line.Count - 1)
                    heading = (line[i] - line[i - 1]).HeadingXZ();
                else
                    heading = ((line[i] - line[i - 1]).HeadingXZ() + (line[i + 1] - line[i]).HeadingXZ()).HeadingXZ();
                moved.Add(line[i] + heading.RightXZ() * lateral);
            }

            return moved;
        }
    }
}
