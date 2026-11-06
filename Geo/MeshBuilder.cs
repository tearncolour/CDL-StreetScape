using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Plain-array mesh data with no Unity types, so it can be built on any thread and checked offline. Conventions are Unity's:
    /// left-handed, Y up, a triangle is front-facing when its corners run clockwise seen from the front, so the geometric normal
    /// (b - a) x (c - a) points at the viewer. Local space of a sign: the pole stands on the origin, +Z is the way the sign faces.
    /// </summary>
    public sealed class MeshBuilder
    {
        public readonly List<Vec3> Positions = new List<Vec3>();
        public readonly List<Vec3> Normals = new List<Vec3>();
        public readonly List<float> U = new List<float>();
        public readonly List<float> V = new List<float>();
        public readonly List<int> Indices = new List<int>();

        public int VertexCount => Positions.Count;
        public int TriangleCount => Indices.Count / 3;

        public int Vertex(Vec3 position, Vec3 normal, float u = 0f, float v = 0f)
        {
            Positions.Add(position);
            Normals.Add(normal);
            U.Add(u);
            V.Add(v);
            return Positions.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        /// <summary>A triangle wound so that it faces the given direction, whatever order the corners came in.</summary>
        public void TriFacing(int a, int b, int c, Vec3 facing)
        {
            Vec3 n = Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
            if (Vec3.Dot(n, facing) >= 0f)
                Tri(a, b, c);
            else
                Tri(a, c, b);
        }

        public void QuadFacing(int a, int b, int c, int d, Vec3 facing)
        {
            TriFacing(a, b, c, facing);
            TriFacing(a, c, d, facing);
        }

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static Vec3 Normalize(Vec3 v)
        {
            float length = v.Length;
            return length < 1e-9f ? new Vec3(0f, 1f, 0f) : v * (1f / length);
        }

        /// <summary>Appends another builder moved by an offset and turned about Y by a yaw (local +Z goes to world (sin yaw, 0, cos yaw)).</summary>
        public void Append(MeshBuilder other, Vec3 offset, float yaw)
        {
            float s = (float)Math.Sin(yaw);
            float c = (float)Math.Cos(yaw);
            int first = Positions.Count;
            for (int i = 0; i < other.Positions.Count; i++)
            {
                Vec3 p = other.Positions[i];
                Vec3 n = other.Normals[i];
                Positions.Add(new Vec3(p.X * c + p.Z * s + offset.X, p.Y + offset.Y, -p.X * s + p.Z * c + offset.Z));
                Normals.Add(new Vec3(n.X * c + n.Z * s, n.Y, -n.X * s + n.Z * c));
                U.Add(other.U[i]);
                V.Add(other.V[i]);
            }

            for (int i = 0; i < other.Indices.Count; i++)
                Indices.Add(other.Indices[i] + first);
        }

        public void Bounds(out Vec3 min, out Vec3 max)
        {
            if (Positions.Count == 0)
            {
                min = Vec3.Zero;
                max = Vec3.Zero;
                return;
            }

            min = Positions[0];
            max = Positions[0];
            foreach (Vec3 p in Positions)
            {
                min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
        }
    }

    public readonly struct Point2
    {
        public readonly float X, Y;

        public Point2(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public static class Prims
    {
        /// <summary>An upright cylinder with smooth sides and an optional flat top; the bottom is not drawn (it stands on the ground).</summary>
        public static void Cylinder(MeshBuilder b, Vec3 baseCentre, float radius, float height, int segments, bool capTop)
        {
            segments = Math.Max(3, segments);
            var bottom = new int[segments + 1];
            var top = new int[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)(2.0 * Math.PI * i / segments);
                float x = (float)Math.Sin(a);
                float z = (float)Math.Cos(a);
                var n = new Vec3(x, 0f, z);
                var p = new Vec3(baseCentre.X + x * radius, baseCentre.Y, baseCentre.Z + z * radius);
                bottom[i] = b.Vertex(p, n, (float)i / segments, 0f);
                top[i] = b.Vertex(new Vec3(p.X, p.Y + height, p.Z), n, (float)i / segments, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                var outward = new Vec3((b.Normals[bottom[i]].X + b.Normals[bottom[i + 1]].X) * 0.5f, 0f, (b.Normals[bottom[i]].Z + b.Normals[bottom[i + 1]].Z) * 0.5f);
                b.QuadFacing(bottom[i], bottom[i + 1], top[i + 1], top[i], outward);
            }

            if (!capTop)
                return;

            var up = new Vec3(0f, 1f, 0f);
            var topCentre = new Vec3(baseCentre.X, baseCentre.Y + height, baseCentre.Z);
            int centre = b.Vertex(topCentre, up, 0.5f, 0.5f);
            int[] ring = new int[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = (float)(2.0 * Math.PI * i / segments);
                ring[i] = b.Vertex(new Vec3(topCentre.X + (float)Math.Sin(a) * radius, topCentre.Y, topCentre.Z + (float)Math.Cos(a) * radius), up, 0.5f, 0.5f);
            }

            for (int i = 0; i < segments; i++)
                b.TriFacing(centre, ring[i], ring[(i + 1) % segments], up);
        }

        /// <summary>A flat polygon in the plane z, as a fan from its middle, facing +Z or -Z. The texture covers the width x height box around the origin.</summary>
        public static void PolygonFace(MeshBuilder b, IList<Point2> outline, float z, bool front, float width, float height, float originY = 0f, float originX = 0f)
        {
            var normal = new Vec3(0f, 0f, front ? 1f : -1f);
            float cx = 0f, cy = 0f;
            foreach (Point2 p in outline)
            {
                cx += p.X;
                cy += p.Y;
            }

            cx /= outline.Count;
            cy /= outline.Count;
            // Seen from the front of a face that looks along +Z, a viewer looks along -Z, and in Unity's left-handed frame the right of that viewer is
            // -X (right = up x forward): the picture's U must run against X on a front face (0.1.1 had it the other way round and every picture
            // was mirrored). A back face is seen from the other side, so there U runs with X.
            float uCentre = 0.5f + cx / width;
            int centre = b.Vertex(new Vec3(cx + originX, cy + originY, z), normal, front ? 1f - uCentre : uCentre, 0.5f + cy / height);
            var ring = new int[outline.Count];
            for (int i = 0; i < outline.Count; i++)
            {
                Point2 p = outline[i];
                float u = 0.5f + p.X / width;
                ring[i] = b.Vertex(new Vec3(p.X + originX, p.Y + originY, z), normal, front ? 1f - u : u, 0.5f + p.Y / height);
            }

            for (int i = 0; i < outline.Count; i++)
                b.TriFacing(centre, ring[i], ring[(i + 1) % outline.Count], normal);
        }

        /// <summary>
        /// A flat polygon like PolygonFace, but for a face whose picture covers more than this polygon (a board with plates fixed to it share one picture). The outline is given as the
        /// viewer sees the face: x to the viewer's right, y up, round the middle of the polygon; a vertex is put at (cx - x) in the sign's frame (the viewer's right is -X, see PolygonFace)
        /// and cy + y. The picture's coordinates of a vertex come from the function, which is given the same x and y.
        /// </summary>
        public static void PolygonFaceMapped(MeshBuilder b, IList<Point2> outline, float z, bool front, float cx, float cy, Func<float, float, (float u, float v)> uv)
        {
            var normal = new Vec3(0f, 0f, front ? 1f : -1f);
            (float cu, float cv) = uv(0f, 0f);
            int centre = b.Vertex(new Vec3(cx, cy, z), normal, cu, cv);
            var ring = new int[outline.Count];
            for (int i = 0; i < outline.Count; i++)
            {
                Point2 p = outline[i];
                (float u, float v) = uv(p.X, p.Y);
                ring[i] = b.Vertex(new Vec3(cx - p.X, cy + p.Y, z), normal, u, v);
            }

            for (int i = 0; i < outline.Count; i++)
                b.TriFacing(centre, ring[i], ring[(i + 1) % outline.Count], normal);
        }

        /// <summary>
        /// A flat face of loops (0.12.30, a board cut to a picture: CutOutline, outer loops with their holes) in the plane z, facing +Z or -Z. The loops are as the viewer sees the face (x to the viewer's
        /// right, y up, round the part's middle); a vertex is put at (cx - x, cy + y), as PolygonFaceMapped does, and its picture's coordinates come from the function (null: none, for a back).
        /// </summary>
        public static void LoopsFace(MeshBuilder b, List<List<Point2>> loops, float z, bool front, float cx, float cy, Func<float, float, (float u, float v)> uv)
        {
            var normal = new Vec3(0f, 0f, front ? 1f : -1f);
            var points = new List<Point2>();
            var triangles = new List<int>();
            CutOutline.Triangulate(loops, points, triangles);
            int first = b.VertexCount;
            foreach (Point2 p in points)
            {
                (float u, float v) = uv != null ? uv(p.X, p.Y) : (0f, 0f);
                b.Vertex(new Vec3(cx - p.X, cy + p.Y, z), normal, u, v);
            }

            for (int t = 0; t + 2 < triangles.Count; t += 3)
                b.TriFacing(first + triangles[t], first + triangles[t + 1], first + triangles[t + 2], normal);
        }

        /// <summary>
        /// The edge of a face of loops (see LoopsFace): a strip round each loop between the back and the front plane, facing out of the board, which is on the left of each loop as the viewer sees it (so
        /// a hole's edge faces into the hole). smooth (0.12.49, the curved ornaments of the lamp-post boxes): where two edges turn by less than 40 degrees they share their vertices, the normal between
        /// theirs, so that a curve is shaded round and costs half the vertices; else each edge is flat.
        /// </summary>
        public static void LoopsRim(MeshBuilder b, List<List<Point2>> loops, float zBack, float zFront, float cx, float cy, bool smooth = false)
        {
            const float smoothCos = 0.766f;
            foreach (List<Point2> loop in loops)
            {
                var edges = new List<(Point2 P0, Point2 P1, Vec3 N)>(loop.Count);
                for (int i = 0; i < loop.Count; i++)
                {
                    Point2 p0 = loop[i], p1 = loop[(i + 1) % loop.Count];
                    float dx = p1.X - p0.X, dy = p1.Y - p0.Y, length = (float)Math.Sqrt(dx * dx + dy * dy);
                    // Out of the board is the loop's right as the viewer sees it, (dy, -dx); in the sign's frame x runs the other way.
                    if (length >= 1e-6f)
                        edges.Add((p0, p1, new Vec3(-dy / length, -dx / length, 0f)));
                }

                int m = edges.Count;
                // Each edge's vertices at its start and its end (back, front); a smooth corner's are its neighbour's.
                var starts = new (int Back, int Front)[m];
                var ends = new (int Back, int Front)[m];
                for (int i = 0; i < m; i++)
                {
                    (Point2 p0, Point2 _, Vec3 n) = edges[i];
                    Vec3 before = edges[(i + m - 1) % m].N;
                    if (smooth && Vec3.Dot(before, n) > smoothCos)
                    {
                        Vec3 mid = MeshBuilder.Normalize(before + n);
                        starts[i] = (b.Vertex(new Vec3(cx - p0.X, cy + p0.Y, zBack), mid), b.Vertex(new Vec3(cx - p0.X, cy + p0.Y, zFront), mid));
                        ends[(i + m - 1) % m] = starts[i];
                    }
                    else
                    {
                        starts[i] = (b.Vertex(new Vec3(cx - p0.X, cy + p0.Y, zBack), n), b.Vertex(new Vec3(cx - p0.X, cy + p0.Y, zFront), n));
                        ends[(i + m - 1) % m] = (b.Vertex(new Vec3(cx - p0.X, cy + p0.Y, zBack), before), b.Vertex(new Vec3(cx - p0.X, cy + p0.Y, zFront), before));
                    }
                }

                for (int i = 0; i < m; i++)
                    b.QuadFacing(starts[i].Back, ends[i].Back, ends[i].Front, starts[i].Front, edges[i].N);
            }
        }

        /// <summary>The edge of a board: a strip around the outline between the back plane and the front plane, facing out of the board.</summary>
        public static void Rim(MeshBuilder b, IList<Point2> outline, float zBack, float zFront, float originY = 0f, float originX = 0f)
        {
            float cx = 0f, cy = 0f;
            foreach (Point2 p in outline)
            {
                cx += p.X;
                cy += p.Y;
            }

            cx /= outline.Count;
            cy /= outline.Count;
            for (int i = 0; i < outline.Count; i++)
            {
                Point2 p0 = outline[i];
                Point2 p1 = outline[(i + 1) % outline.Count];
                float dx = p1.X - p0.X;
                float dy = p1.Y - p0.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-6f)
                    continue;

                var n = new Vec3(dy / length, -dx / length, 0f);
                float mx = (p0.X + p1.X) * 0.5f - cx;
                float my = (p0.Y + p1.Y) * 0.5f - cy;
                if (n.X * mx + n.Y * my < 0f)
                    n = n * -1f;

                int a = b.Vertex(new Vec3(p0.X + originX, p0.Y + originY, zBack), n);
                int c = b.Vertex(new Vec3(p1.X + originX, p1.Y + originY, zBack), n);
                int d = b.Vertex(new Vec3(p1.X + originX, p1.Y + originY, zFront), n);
                int e = b.Vertex(new Vec3(p0.X + originX, p0.Y + originY, zFront), n);
                b.QuadFacing(a, c, d, e, n);
            }
        }
    }
}
