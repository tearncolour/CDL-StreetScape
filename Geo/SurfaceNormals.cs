using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The normals of ground paint once its heights were laid (0.12.83; the owner: "我们的地面标线能不能继承当前覆盖道路的法线"). Paint is made flat with a normal of straight up, and then given the heights of the
    /// ground (a grade, a grid of heights under a thing put down with one click, the heights of a run's path): its triangles lean with the ground and its normals did not, so on a slope or over a crest it was
    /// lit as if flat. A vertex that is plain paint (its normal straight up and every triangle it is in made of such vertices) takes the sum of the normals of its up-facing triangles, each by its area;
    /// a vertex of anything else (a stud's dome, a post, a box's side) keeps what it has. Paint on flat ground (a mesh whose heights differ by FlatRange or less, and a vertex whose sideways part is under FlatSlope) stays exactly as it was. Pure.
    /// </summary>
    public static class SurfaceNormals
    {
        /// <summary>
        /// Whether paint takes the normals of the ground it lies on (the experiment's markingNormals; on). It is part of a plan's cache key (MarkingTemplates), and is read once for each plan.
        /// </summary>
        public static volatile bool Enabled = true;

        /// <summary>A triangle that leans more than 60 degrees from the flat (its normal's up part under this share of its length) is a wall of something, not paint, and gives no normal.</summary>
        public const float MinUp = 0.5f;

        /// <summary>
        /// A sideways part of the sum under this share of its up part (a slope of 0.01 %, 0.006 degrees) is no slope: heights that are the same to a float's last bit (a grid of one height read at different
        /// places) leave the normal straight up, as paint on flat ground always had it.
        /// </summary>
        public const float FlatSlope = 1e-4f;

        /// <summary>A mesh whose heights differ by no more than this (m; 10 micrometres: a float's last bit at the height of a road) lies flat, and its normals are left as they are.</summary>
        public const float FlatRange = 1e-5f;

        private static bool IsUp(Vec3 n) => n.X == 0f && n.Y == 1f && n.Z == 0f;

        /// <summary>Gives the plain paint vertices of a mesh the normal of the surface they lie on (see the class); the mesh's positions, texture coordinates and triangles are not touched.</summary>
        public static void Fit(MeshBuilder mesh)
        {
            int count = mesh.VertexCount;
            if (count == 0 || mesh.Indices.Count < 3)
                return;
            float low = float.MaxValue, high = float.MinValue;
            foreach (Vec3 p in mesh.Positions)
            {
                low = Math.Min(low, p.Y);
                high = Math.Max(high, p.Y);
            }

            if (!(high - low > FlatRange))
                return;
            var other = new bool[count];
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                if (IsUp(mesh.Normals[a]) && IsUp(mesh.Normals[b]) && IsUp(mesh.Normals[c]))
                    continue;
                other[a] = true;
                other[b] = true;
                other[c] = true;
            }

            var sum = new Vec3[count];
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                if (other[a] || other[b] || other[c])
                    continue;
                Vec3 cross = MeshBuilder.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                // Paint faces up whichever way a quad was wound; the length of the cross product is twice the triangle's area.
                if (cross.Y < 0f)
                    cross = cross * -1f;
                float length = cross.Length;
                if (!(length > 1e-12f) || cross.Y < MinUp * length)
                    continue;
                sum[a] = sum[a] + cross;
                sum[b] = sum[b] + cross;
                sum[c] = sum[c] + cross;
            }

            for (int v = 0; v < count; v++)
            {
                if (sum[v].X * sum[v].X + sum[v].Z * sum[v].Z > FlatSlope * FlatSlope * sum[v].Y * sum[v].Y)
                    mesh.Normals[v] = MeshBuilder.Normalize(sum[v]);
            }
        }
    }
}
