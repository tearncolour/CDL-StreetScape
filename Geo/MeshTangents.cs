using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The tangents of a mesh from its texture coordinates (0.12.32, for the normal maps of the baked detail): for each vertex the direction u grows in, in the plane of its normal, and the sign of the
    /// bitangent (x, y, z, w with w = +1 or -1, as Unity has them). A vertex whose faces give none (no texture coordinates) gets any direction square to its normal, w +1. Pure.
    /// </summary>
    public static class MeshTangents
    {
        public static float[] Compute(MeshBuilder mesh)
        {
            int n = mesh.VertexCount;
            var tan = new Vec3[n];
            var bit = new Vec3[n];
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                Vec3 e1 = mesh.Positions[b] - mesh.Positions[a], e2 = mesh.Positions[c] - mesh.Positions[a];
                float du1 = mesh.U[b] - mesh.U[a], dv1 = mesh.V[b] - mesh.V[a], du2 = mesh.U[c] - mesh.U[a], dv2 = mesh.V[c] - mesh.V[a];
                float det = du1 * dv2 - du2 * dv1;
                if (Math.Abs(det) < 1e-12f)
                    continue;
                float r = 1f / det;
                Vec3 sdir = (e1 * dv2 - e2 * dv1) * r, tdir = (e2 * du1 - e1 * du2) * r;
                tan[a] = tan[a] + sdir;
                tan[b] = tan[b] + sdir;
                tan[c] = tan[c] + sdir;
                bit[a] = bit[a] + tdir;
                bit[b] = bit[b] + tdir;
                bit[c] = bit[c] + tdir;
            }

            var result = new float[n * 4];
            for (int i = 0; i < n; i++)
            {
                Vec3 normal = mesh.Normals[i];
                Vec3 t = tan[i] - normal * Vec3.Dot(normal, tan[i]);
                if (t.Length < 1e-8f)
                {
                    Vec3 helper = Math.Abs(normal.Y) < 0.9f ? new Vec3(0f, 1f, 0f) : new Vec3(1f, 0f, 0f);
                    t = MeshBuilder.Cross(helper, normal);
                }

                t = MeshBuilder.Normalize(t);
                float w = Vec3.Dot(MeshBuilder.Cross(normal, t), bit[i]) < 0f ? -1f : 1f;
                result[i * 4] = t.X;
                result[i * 4 + 1] = t.Y;
                result[i * 4 + 2] = t.Z;
                result[i * 4 + 3] = w;
            }

            return result;
        }
    }
}
