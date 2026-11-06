using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// An area (a simple polygon, either order) made ready for cutting many pieces of paint to it: its triangles, a grid of which triangles reach into which cell, and its edges for finding where a line
    /// crosses it. A band of hatching or an arm of a V is first shortened to where its lines cross the area (Span), then cut only against the triangles of the cells it passes over (ClipWalk); the paint is the
    /// same as cutting it against every triangle (MarkingFills.ClipConvex), at a cost that grows with the size of the piece and not with the size of the area. Pure; not shared between threads.
    /// </summary>
    public sealed class AreaIndex
    {
        public readonly MeshBuilder Triangles = new MeshBuilder();
        private readonly List<Vec3> m_Polygon;
        private readonly float m_MinX, m_MinZ, m_Cell;
        private readonly int m_Cols, m_Rows;
        private readonly List<int>[] m_Cells;
        private readonly Vec3[] m_A, m_B, m_C;
        private readonly bool[] m_Usable;
        private readonly float[] m_BoxMinX, m_BoxMaxX, m_BoxMinZ, m_BoxMaxZ;
        private readonly int[] m_Stamp;
        private int m_Generation;
        private readonly List<int> m_Candidates = new List<int>();
        private readonly List<Vec3> m_Piece = new List<Vec3>(8), m_Scratch = new List<Vec3>(8);

        /// <summary>How many triangle tests the last cuts made (for the benchmark and the tests).</summary>
        public long Tests { get; private set; }

        public bool Ok => m_Cells != null;

        public float MinX => m_MinX;

        public float MinZ => m_MinZ;

        public AreaIndex(IList<Vec3> polygon) : this(polygon, null)
        {
        }

        /// <summary>
        /// The area between two lines that run side by side the same way (a strip along a road, the inside of a gore): its outline is the near line and the far line back, and its triangles are the quads between
        /// the points of the same number (the lines are resampled to as many points each), so each is small and local; ear clipping a long strip makes long slivers that a band crosses many of.
        /// </summary>
        public static AreaIndex FromStrip(IList<Vec3> near, IList<Vec3> far)
        {
            if (near == null || far == null || near.Count < 2 || far.Count < 2)
                return new AreaIndex(null, null);
            int n = Math.Max(near.Count, far.Count);
            List<Vec3> a = near.Count == n ? new List<Vec3>(near) : MarkingAreas.Resample(near, n), b = far.Count == n ? new List<Vec3>(far) : MarkingAreas.Resample(far, n);
            var outline = new List<Vec3>(a);
            for (int i = b.Count - 1; i >= 0; i--)
                outline.Add(b[i]);
            var triangles = new MeshBuilder();
            var up = new Vec3(0f, 1f, 0f);
            for (int i = 0; i + 1 < n; i++)
            {
                int i0 = triangles.Vertex(a[i], up, 0f, 0f), i1 = triangles.Vertex(a[i + 1], up, 0f, 0f), j0 = triangles.Vertex(b[i], up, 0f, 0f), j1 = triangles.Vertex(b[i + 1], up, 0f, 0f);
                triangles.Tri(i0, i1, j1);
                triangles.Tri(i0, j1, j0);
            }

            return new AreaIndex(outline, triangles);
        }

        /// <summary>The area with triangles of its own (they must cover it exactly; any winding); null makes them by ear clipping.</summary>
        public AreaIndex(IList<Vec3> polygon, MeshBuilder triangles)
        {
            m_Polygon = polygon == null ? new List<Vec3>() : new List<Vec3>(polygon);
            if (m_Polygon.Count < 3)
                return;
            if (triangles != null)
            {
                Triangles.Append(triangles, Vec3.Zero, 0f);
                if (Triangles.Indices.Count < 3)
                    return;
            }
            else if (Polygon.Fill(Triangles, m_Polygon) == 0)
            {
                return;
            }
            int count = Triangles.Indices.Count / 3;
            m_A = new Vec3[count];
            m_B = new Vec3[count];
            m_C = new Vec3[count];
            m_Usable = new bool[count];
            m_BoxMinX = new float[count];
            m_BoxMaxX = new float[count];
            m_BoxMinZ = new float[count];
            m_BoxMaxZ = new float[count];
            m_Stamp = new int[count];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int t = 0; t < count; t++)
            {
                Vec3 a = Triangles.Positions[Triangles.Indices[t * 3]], b = Triangles.Positions[Triangles.Indices[t * 3 + 1]], c = Triangles.Positions[Triangles.Indices[t * 3 + 2]];
                // Counter-clockwise in (X, Z), so that "inside" is the left of each edge; a sliver (corners in a line) covers nothing and is left out.
                float cross = Polygon.Cross(a, b, c);
                float longest = Math.Max(Vec3.DistanceXZ(a, b), Math.Max(Vec3.DistanceXZ(b, c), Vec3.DistanceXZ(c, a)));
                m_Usable[t] = longest > 1e-6f && Math.Abs(cross) / longest >= 1e-4f;
                if (cross < 0f)
                {
                    Vec3 swap = b;
                    b = c;
                    c = swap;
                }

                m_A[t] = a;
                m_B[t] = b;
                m_C[t] = c;
                m_BoxMinX[t] = Math.Min(a.X, Math.Min(b.X, c.X));
                m_BoxMaxX[t] = Math.Max(a.X, Math.Max(b.X, c.X));
                m_BoxMinZ[t] = Math.Min(a.Z, Math.Min(b.Z, c.Z));
                m_BoxMaxZ[t] = Math.Max(a.Z, Math.Max(b.Z, c.Z));
                minX = Math.Min(minX, m_BoxMinX[t]);
                maxX = Math.Max(maxX, m_BoxMaxX[t]);
                minZ = Math.Min(minZ, m_BoxMinZ[t]);
                maxZ = Math.Max(maxZ, m_BoxMaxZ[t]);
            }

            // Cells of about the size of a triangle, between half a metre and 8 m, at most 4096 of them.
            float w = Math.Max(0.01f, maxX - minX), h = Math.Max(0.01f, maxZ - minZ);
            float cell = Math.Max(0.5f, Math.Min(8f, (float)Math.Sqrt(w * h / Math.Max(1, count)) * 1.5f));
            while ((Math.Ceiling(w / cell) + 1) * (Math.Ceiling(h / cell) + 1) > 4096)
                cell *= 1.5f;
            m_Cell = cell;
            m_MinX = minX;
            m_MinZ = minZ;
            m_Cols = (int)Math.Ceiling(w / cell) + 1;
            m_Rows = (int)Math.Ceiling(h / cell) + 1;
            m_Cells = new List<int>[m_Cols * m_Rows];
            for (int t = 0; t < count; t++)
            {
                if (!m_Usable[t])
                    continue;
                int x0 = ColOf(m_BoxMinX[t]), x1 = ColOf(m_BoxMaxX[t]), z0 = RowOf(m_BoxMinZ[t]), z1 = RowOf(m_BoxMaxZ[t]);
                for (int x = x0; x <= x1; x++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        int i = z * m_Cols + x;
                        if (m_Cells[i] == null)
                            m_Cells[i] = new List<int>(4);
                        m_Cells[i].Add(t);
                    }
                }
            }
        }

        private int ColOf(float x) => Math.Max(0, Math.Min(m_Cols - 1, (int)Math.Floor((x - m_MinX) / m_Cell)));

        private int RowOf(float z) => Math.Max(0, Math.Min(m_Rows - 1, (int)Math.Floor((z - m_MinZ) / m_Cell)));

        private void NewStamp()
        {
            m_Generation++;
            if (m_Generation == int.MaxValue)
            {
                Array.Clear(m_Stamp, 0, m_Stamp.Length);
                m_Generation = 1;
            }

            m_Candidates.Clear();
        }

        private void AddCell(int x, int z)
        {
            if (x < 0 || z < 0 || x >= m_Cols || z >= m_Rows)
                return;
            List<int> list = m_Cells[z * m_Cols + x];
            if (list == null)
                return;
            foreach (int t in list)
            {
                if (m_Stamp[t] != m_Generation)
                {
                    m_Stamp[t] = m_Generation;
                    m_Candidates.Add(t);
                }
            }
        }

        /// <summary>
        /// Where a line through o in the direction d (unit, ground plane) is inside the area, as the least and the greatest distance along it at which it crosses an edge of the area; false when it
        /// crosses none.
        /// </summary>
        public bool Span(Vec3 o, Vec3 d, out float t0, out float t1)
        {
            t0 = float.MaxValue;
            t1 = float.MinValue;
            int n = m_Polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Vec3 a = m_Polygon[i], b = m_Polygon[(i + 1) % n];
                float ex = b.X - a.X, ez = b.Z - a.Z;
                float den = d.X * ez - d.Z * ex;
                if (Math.Abs(den) < 1e-9f)
                    continue;
                float ax = a.X - o.X, az = a.Z - o.Z;
                // o + d t = a + e u: t = (ax ez - az ex) / den, u = (ax dz - az dx) / den.
                float t = (ax * ez - az * ex) / den, u = (ax * d.Z - az * d.X) / den;
                if (u < -1e-6f || u > 1f + 1e-6f)
                    continue;
                t0 = Math.Min(t0, t);
                t1 = Math.Max(t1, t);
            }

            return t0 <= t1;
        }

        /// <summary>
        /// The extent along d of a band of the given half width whose centre line goes through o: the union of where its centre and its two sides cross the area (with the width added at both ends); false when
        /// none of the three crosses it (the band misses the area, or is wider than it: then whether it covers it is decided by the caller).
        /// </summary>
        public bool BandSpan(Vec3 o, Vec3 d, float halfWidth, out float t0, out float t1)
        {
            Vec3 across = d.RightXZ();
            bool any = false;
            t0 = float.MaxValue;
            t1 = float.MinValue;
            foreach (float k in new[] { -halfWidth, 0f, halfWidth })
            {
                if (Span(o + across * k, d, out float a, out float b))
                {
                    any = true;
                    t0 = Math.Min(t0, a);
                    t1 = Math.Max(t1, b);
                }
            }

            if (any)
            {
                t0 -= halfWidth * 2f + 0.01f;
                t1 += halfWidth * 2f + 0.01f;
            }

            return any;
        }

        /// <summary>Paints the part of a convex piece inside the area, testing the triangles of the cells its box covers. Returns how many triangles it was painted in.</summary>
        public int Clip(MeshBuilder mesh, IList<Vec3> convex)
        {
            if (!Ok || convex == null || convex.Count < 3)
                return 0;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vec3 p in convex)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
            }

            NewStamp();
            int x0 = ColOf(minX), x1 = ColOf(maxX), z0 = RowOf(minZ), z1 = RowOf(maxZ);
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                    AddCell(x, z);
            }

            return ClipCandidates(mesh, convex, minX, maxX, minZ, maxZ);
        }

        /// <summary>
        /// Paints the part of a convex piece inside the area when the piece is a band: it runs from a along d for length metres and is within halfWidth of that line. The candidates are the triangles of the
        /// cells along the line (the piece's box would hold a great many for a long band at 45 degrees).
        /// </summary>
        public int ClipWalk(MeshBuilder mesh, IList<Vec3> convex, Vec3 a, Vec3 d, float length, float halfWidth)
        {
            if (!Ok || convex == null || convex.Count < 3 || !(length >= 0f))
                return 0;
            NewStamp();
            int reach = (int)Math.Ceiling((halfWidth + 0.01f) / m_Cell);
            float step = m_Cell * 0.5f;
            int steps = (int)Math.Ceiling(length / step);
            if (steps > 200000)
                return Clip(mesh, convex);
            int lastX = int.MinValue, lastZ = int.MinValue;
            for (int i = 0; i <= steps; i++)
            {
                Vec3 p = a + d * Math.Min(length, i * step);
                int cx = (int)Math.Floor((p.X - m_MinX) / m_Cell), cz = (int)Math.Floor((p.Z - m_MinZ) / m_Cell);
                if (cx == lastX && cz == lastZ)
                    continue;
                lastX = cx;
                lastZ = cz;
                for (int x = cx - reach; x <= cx + reach; x++)
                {
                    for (int z = cz - reach; z <= cz + reach; z++)
                        AddCell(x, z);
                }
            }

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vec3 p in convex)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
            }

            return ClipCandidates(mesh, convex, minX, maxX, minZ, maxZ);
        }

        private int ClipCandidates(MeshBuilder mesh, IList<Vec3> convex, float minX, float maxX, float minZ, float maxZ)
        {
            bool reversed = Polygon.SignedArea2(convex) < 0f;
            var up = new Vec3(0f, 1f, 0f);
            int made = 0;
            foreach (int t in m_Candidates)
            {
                if (m_BoxMaxX[t] < minX || m_BoxMinX[t] > maxX || m_BoxMaxZ[t] < minZ || m_BoxMinZ[t] > maxZ)
                    continue;
                Tests++;
                m_Piece.Clear();
                if (reversed)
                {
                    for (int k = convex.Count - 1; k >= 0; k--)
                        m_Piece.Add(convex[k]);
                }
                else
                {
                    m_Piece.AddRange(convex);
                }

                Vec3 ta = m_A[t], tb = m_B[t], tc = m_C[t];
                MarkingFills.ClipHalfPlane(m_Piece, m_Scratch, ta, tb);
                MarkingFills.ClipHalfPlane(m_Piece, m_Scratch, tb, tc);
                MarkingFills.ClipHalfPlane(m_Piece, m_Scratch, tc, ta);
                if (m_Piece.Count < 3 || Math.Abs(Polygon.SignedArea2(m_Piece)) < 1e-8f)
                    continue;
                if (MarkingFills.EmitPiece(mesh, m_Piece, m_BoxMinX[t], m_BoxMaxX[t], m_BoxMinZ[t], m_BoxMaxZ[t], up))
                    made++;
            }

            return made;
        }
    }
}
