using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Texture coordinates for the triangles of a solid that were made without them (Solids.Convex, the prisms: all three corners at one place of the texture, which would draw the detail map as one
    /// texel tilted over the whole face): their corners are made again, mapped in metres onto the plane the face looks most along (a box map). Pure.
    /// </summary>
    public static class DetailUv
    {
        public static void Ensure(MeshBuilder mesh)
        {
            int triangles = mesh.Indices.Count / 3;
            for (int t = 0; t < triangles; t++)
            {
                int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
                float area = (mesh.U[ib] - mesh.U[ia]) * (mesh.V[ic] - mesh.V[ia]) - (mesh.U[ic] - mesh.U[ia]) * (mesh.V[ib] - mesh.V[ia]);
                if (Math.Abs(area) > 1e-10f)
                    continue;
                Vec3 a = mesh.Positions[ia], b = mesh.Positions[ib], c = mesh.Positions[ic];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                if (n.Length < 1e-10f)
                    continue;
                for (int k = 0; k < 3; k++)
                {
                    int old = mesh.Indices[t * 3 + k];
                    Vec3 p = mesh.Positions[old];
                    Map(p, n, out float u, out float v);
                    mesh.Indices[t * 3 + k] = mesh.Vertex(p, mesh.Normals[old], u, v);
                }
            }
        }

        /// <summary>The place on the plane the face looks most along: across (x or z) and up (y), or x and z for a face looking up or down.</summary>
        public static void Map(Vec3 p, Vec3 facing, out float u, out float v)
        {
            float ax = Math.Abs(facing.X), ay = Math.Abs(facing.Y), az = Math.Abs(facing.Z);
            if (ay >= ax && ay >= az)
            {
                u = p.X;
                v = p.Z;
            }
            else if (ax >= az)
            {
                u = p.Z;
                v = p.Y;
            }
            else
            {
                u = p.X;
                v = p.Y;
            }
        }
    }
}
