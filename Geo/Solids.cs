using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>Solid shapes for the supports, built into a MeshBuilder. Pure. Every face is oriented away from the inside, whatever order its corners are given in.</summary>
    public static class Solids
    {
        /// <summary>A convex solid with flat faces (each its own vertices and normal). A face is a list of corner indices of a convex polygon.</summary>
        public static void Convex(MeshBuilder b, IList<Vec3> verts, int[][] faces)
        {
            Vec3 centroid = Vec3.Zero;
            foreach (Vec3 v in verts)
                centroid += v;
            centroid = centroid * (1f / verts.Count);

            foreach (int[] face in faces)
            {
                Vec3 faceCentre = Vec3.Zero;
                foreach (int index in face)
                    faceCentre += verts[index];
                faceCentre = faceCentre * (1f / face.Length);

                Vec3 n = MeshBuilder.Normalize(MeshBuilder.Cross(verts[face[1]] - verts[face[0]], verts[face[2]] - verts[face[0]]));
                if (Vec3.Dot(n, faceCentre - centroid) < 0f)
                    n = n * -1f;

                var ids = new int[face.Length];
                for (int k = 0; k < face.Length; k++)
                    ids[k] = b.Vertex(verts[face[k]], n);
                for (int k = 1; k + 1 < face.Length; k++)
                    b.TriFacing(ids[0], ids[k], ids[k + 1], n);
            }
        }

        private static readonly int[][] s_BoxFaces =
        {
            new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 },    // -X, +X
            new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 },    // -Y, +Y
            new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }     // -Z, +Z
        };

        /// <summary>An axis-aligned box around a centre with the given half extents.</summary>
        public static void Box(MeshBuilder b, Vec3 centre, Vec3 half)
        {
            var v = new Vec3[8];
            for (int i = 0; i < 8; i++)
                v[i] = new Vec3(centre.X + ((i & 1) != 0 ? half.X : -half.X), centre.Y + ((i & 2) != 0 ? half.Y : -half.Y), centre.Z + ((i & 4) != 0 ? half.Z : -half.Z));
            Convex(b, v, s_BoxFaces);
        }

        /// <summary>A flat plate: the convex polygon (its corners in order) pushed half the offset either way (a rib, a piece of a hoop's strap).</summary>
        public static void Slab(MeshBuilder b, IList<Vec3> outline, Vec3 offset)
        {
            int n = outline.Count;
            var verts = new Vec3[2 * n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = outline[i] - offset;
                verts[n + i] = outline[i] + offset;
            }

            var faces = new int[n + 2][];
            faces[0] = new int[n];
            faces[1] = new int[n];
            for (int i = 0; i < n; i++)
            {
                faces[0][i] = i;
                faces[1][i] = n + i;
                faces[2 + i] = new[] { i, (i + 1) % n, n + (i + 1) % n, n + i };
            }

            Convex(b, verts, faces);
        }

        private static readonly int[][] s_PrismFaces =
        {
            new[] { 0, 1, 2 }, new[] { 3, 4, 5 },
            new[] { 0, 1, 4, 3 }, new[] { 1, 2, 5, 4 }, new[] { 2, 0, 3, 5 }
        };

        /// <summary>A triangular plate: the triangle p0 p1 p2 pushed half the offset either way (a gusset between a post and its flange).</summary>
        public static void Prism(MeshBuilder b, Vec3 p0, Vec3 p1, Vec3 p2, Vec3 offset)
        {
            Convex(b, new[] { p0 - offset, p1 - offset, p2 - offset, p0 + offset, p1 + offset, p2 + offset }, s_PrismFaces);
        }

        /// <summary>
        /// A tube (or a tapered one) from a to c with radius ra at a and rc at c, smooth-shaded, with flat ends when asked for. Used for posts, arms,
        /// bands, bolts and (with six sides) nuts.
        /// </summary>
        /// <summary>
        /// The most sides a tube is given on this thread; 0 (what a thread starts with) is no limit. The mesh builder sets it while it builds a support in reduced detail (SignBuilder.BuildAssembly):
        /// the tubes of a tile that is far away are hexagons, which at that distance are as round as any.
        /// </summary>
        [ThreadStatic]
        public static int MaxSegments;

        public static void Tube(MeshBuilder b, Vec3 a, Vec3 c, float ra, float rc, int segments, bool capA, bool capC)
        {
            if (MaxSegments > 0 && segments > MaxSegments)
                segments = MaxSegments;
            segments = Math.Max(3, segments);
            Vec3 axis = c - a;
            float length = axis.Length;
            if (length < 1e-6f)
                return;

            Vec3 d = axis * (1f / length);
            Vec3 helper = Math.Abs(d.Y) < 0.9f ? new Vec3(0f, 1f, 0f) : new Vec3(1f, 0f, 0f);
            Vec3 u = MeshBuilder.Normalize(MeshBuilder.Cross(helper, d));
            Vec3 v = MeshBuilder.Cross(d, u);
            float slope = (ra - rc) / length;

            var ringA = new int[segments + 1];
            var ringC = new int[segments + 1];
            var radials = new Vec3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(2.0 * Math.PI * i / segments);
                Vec3 radial = u * (float)Math.Cos(angle) + v * (float)Math.Sin(angle);
                radials[i] = radial;
                Vec3 n = MeshBuilder.Normalize(radial + d * slope);
                // Texture coordinates in metres: round the tube at its mean radius, and along it.
                float round = (float)(2.0 * Math.PI * i / segments) * (ra + rc) * 0.5f;
                ringA[i] = b.Vertex(a + radial * ra, n, round, 0f);
                ringC[i] = b.Vertex(c + radial * rc, n, round, length);
            }

            for (int i = 0; i < segments; i++)
                b.QuadFacing(ringA[i], ringA[i + 1], ringC[i + 1], ringC[i], radials[i] + radials[i + 1]);

            if (capA)
                Cap(b, a, d * -1f, u, v, ra, segments);
            if (capC)
                Cap(b, c, d, u, v, rc, segments);
        }

        /// <summary>
        /// A tapered prism from a to c (0.12.57: 八角锥杆's octagonal posts and arms): a regular polygon of sides corners on the radius ra at a and rc at c, a flat (not a corner) toward Tube's first
        /// direction (+Z for an upright one: the front; -Z for one along X, whose flats then face up and down), flat-shaded, with flat ends when asked for. Its sides are not cut in reduced detail:
        /// the facets are what it looks like.
        /// </summary>
        public static void Faceted(MeshBuilder b, Vec3 a, Vec3 c, float ra, float rc, int sides, bool capA, bool capC)
        {
            Vec3 axis = c - a;
            float length = axis.Length;
            if (length < 1e-6f)
                return;
            Vec3 d = axis * (1f / length);
            Vec3 helper = Math.Abs(d.Y) < 0.9f ? new Vec3(0f, 1f, 0f) : new Vec3(1f, 0f, 0f);
            Vec3 u = MeshBuilder.Normalize(MeshBuilder.Cross(helper, d));
            Vec3 v = MeshBuilder.Cross(d, u);
            var verts = new Vec3[2 * sides];
            for (int i = 0; i < sides; i++)
            {
                double angle = 2.0 * Math.PI * (i + 0.5) / sides;
                Vec3 radial = u * (float)Math.Cos(angle) + v * (float)Math.Sin(angle);
                verts[i] = a + radial * ra;
                verts[sides + i] = c + radial * rc;
            }

            var faces = new List<int[]>();
            for (int i = 0; i < sides; i++)
                faces.Add(new[] { i, (i + 1) % sides, sides + (i + 1) % sides, sides + i });
            if (capA)
                faces.Add(Range(0, sides));
            if (capC)
                faces.Add(Range(sides, sides));
            Convex(b, verts, faces.ToArray());
        }

        private static int[] Range(int start, int count)
        {
            var ids = new int[count];
            for (int i = 0; i < count; i++)
                ids[i] = start + i;
            return ids;
        }

        /// <summary>
        /// A hollow collar (a band) from a to c: the outer wall of radius ro, the inner wall of radius ri facing in, and the two flat rings between them. Nothing is inside ri, which is where a
        /// post that is not ours goes (an attached support): a solid collar would draw a disc where the game draws its own post, and hide how thick that post really is.
        /// </summary>
        public static void Band(MeshBuilder b, Vec3 a, Vec3 c, float ri, float ro, int segments)
        {
            segments = Math.Max(3, segments);
            Vec3 axis = c - a;
            float length = axis.Length;
            if (length < 1e-6f || ri >= ro)
                return;

            Vec3 d = axis * (1f / length);
            Vec3 helper = Math.Abs(d.Y) < 0.9f ? new Vec3(0f, 1f, 0f) : new Vec3(1f, 0f, 0f);
            Vec3 u = MeshBuilder.Normalize(MeshBuilder.Cross(helper, d));
            Vec3 v = MeshBuilder.Cross(d, u);

            // The outer wall with the same sides as the rest (not through Tube, whose sides MaxSegments cuts in reduced detail: its flats would leave the rings' edges standing out of it, 0.12.49).
            var outerA = new int[segments + 1];
            var outerC = new int[segments + 1];
            var innerA = new int[segments + 1];
            var innerC = new int[segments + 1];
            var ringOuterA = new int[segments + 1];
            var ringInnerA = new int[segments + 1];
            var ringOuterC = new int[segments + 1];
            var ringInnerC = new int[segments + 1];
            var radials = new Vec3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(2.0 * Math.PI * i / segments);
                Vec3 radial = u * (float)Math.Cos(angle) + v * (float)Math.Sin(angle);
                radials[i] = radial;
                float k = (float)i / segments;
                // In metres round and along it, as a tube's.
                outerA[i] = b.Vertex(a + radial * ro, radial, angle * ro, 0f);
                outerC[i] = b.Vertex(c + radial * ro, radial, angle * ro, length);
                innerA[i] = b.Vertex(a + radial * ri, radial * -1f, k, 0f);
                innerC[i] = b.Vertex(c + radial * ri, radial * -1f, k, 1f);
                ringOuterA[i] = b.Vertex(a + radial * ro, d * -1f, k, 0f);
                ringInnerA[i] = b.Vertex(a + radial * ri, d * -1f, k, 1f);
                ringOuterC[i] = b.Vertex(c + radial * ro, d, k, 0f);
                ringInnerC[i] = b.Vertex(c + radial * ri, d, k, 1f);
            }

            for (int i = 0; i < segments; i++)
            {
                b.QuadFacing(outerA[i], outerA[i + 1], outerC[i + 1], outerC[i], radials[i] + radials[i + 1]);
                b.QuadFacing(innerA[i], innerA[i + 1], innerC[i + 1], innerC[i], (radials[i] + radials[i + 1]) * -1f);
                b.QuadFacing(ringOuterA[i], ringOuterA[i + 1], ringInnerA[i + 1], ringInnerA[i], d * -1f);
                b.QuadFacing(ringOuterC[i], ringOuterC[i + 1], ringInnerC[i + 1], ringInnerC[i], d);
            }
        }

        private static void Cap(MeshBuilder b, Vec3 centre, Vec3 normal, Vec3 u, Vec3 v, float radius, int segments)
        {
            int middle = b.Vertex(centre, normal, 0.5f, 0.5f);
            var ring = new int[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)(2.0 * Math.PI * i / segments);
                ring[i] = b.Vertex(centre + (u * (float)Math.Cos(angle) + v * (float)Math.Sin(angle)) * radius, normal, 0.5f, 0.5f);
            }

            for (int i = 0; i < segments; i++)
                b.TriFacing(middle, ring[i], ring[(i + 1) % segments], normal);
        }
    }
}
