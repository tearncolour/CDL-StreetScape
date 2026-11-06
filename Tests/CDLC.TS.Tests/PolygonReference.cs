// The ear clipping of 0.11.0, kept only to check the faster one of 0.12.0 against it (PolygonFillTests).
using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Triangulation of a flat polygon in the ground plane (X, Z) by ear clipping, for the figures of the road markings (arrows, crosses, the outlines of glyphs): a simple outline, in either order,
    /// optionally with holes, into up-facing triangles of a MeshBuilder. O(n^2) in the vertices, which is small here. Pure.
    /// </summary>
    internal static class PolygonReference
    {
        /// <summary>Twice the signed area in (X, Z): positive when the points run counter-clockwise with X to the right and Z up.</summary>
        public static float SignedArea2(IList<Vec3> points)
        {
            double sum = 0.0;
            for (int i = 0, n = points.Count; i < n; i++)
            {
                Vec3 a = points[i], b = points[(i + 1) % n];
                sum += (double)a.X * b.Z - (double)b.X * a.Z;
            }

            return (float)sum;
        }

        /// <summary>Appends the triangles of an outline (no holes) to the mesh, facing up; the outline's own Y is kept. Returns how many triangles were made.</summary>
        public static int Fill(MeshBuilder mesh, IList<Vec3> outline) => Fill(mesh, outline, null);

        /// <summary>Appends the triangles of an outline with holes (each hole a closed loop inside the outline, not touching it or one another) to the mesh, facing up.</summary>
        public static int Fill(MeshBuilder mesh, IList<Vec3> outline, IList<IList<Vec3>> holes)
        {
            if (outline == null || outline.Count < 3)
                return 0;
            List<Vec3> ring = Clean(outline);
            if (ring.Count < 3)
                return 0;
            if (SignedArea2(ring) < 0f)
                ring.Reverse();
            if (holes != null)
            {
                foreach (IList<Vec3> hole in holes)
                {
                    List<Vec3> h = Clean(hole);
                    if (h.Count < 3)
                        continue;
                    if (SignedArea2(h) > 0f)
                        h.Reverse();
                    ring = Bridge(ring, h);
                }
            }

            var up = new Vec3(0f, 1f, 0f);
            int first = mesh.VertexCount;
            foreach (Vec3 p in ring)
                mesh.Vertex(p, up, p.X, p.Z);
            var index = new List<int>(ring.Count);
            for (int i = 0; i < ring.Count; i++)
                index.Add(i);

            int made = 0;
            int guard = 0;
            while (index.Count > 3 && guard++ < 100000)
            {
                int n = index.Count;
                int ear = -1;
                for (int i = 0; i < n && ear < 0; i++)
                {
                    int ia = index[(i + n - 1) % n], ib = index[i], ic = index[(i + 1) % n];
                    if (Cross(ring[ia], ring[ib], ring[ic]) <= 1e-9f)
                        continue;
                    bool clear = true;
                    for (int k = 0; k < n && clear; k++)
                    {
                        int ip = index[k];
                        if (ip == ia || ip == ib || ip == ic)
                            continue;
                        // A point that is a copy of a corner (a bridge to a hole makes two) is not inside the ear.
                        if (Same(ring[ip], ring[ia]) || Same(ring[ip], ring[ib]) || Same(ring[ip], ring[ic]))
                            continue;
                        if (Inside(ring[ip], ring[ia], ring[ib], ring[ic]))
                            clear = false;
                    }

                    if (clear)
                        ear = i;
                }

                if (ear < 0)
                {
                    // No clean ear (collinear runs or a numerical knot): drop the vertex of the least turn so that the loop ends; the triangle it would make is thin.
                    int worst = 0;
                    float least = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        float c = Math.Abs(Cross(ring[index[(i + n - 1) % n]], ring[index[i]], ring[index[(i + 1) % n]]));
                        if (c < least)
                        {
                            least = c;
                            worst = i;
                        }
                    }

                    index.RemoveAt(worst);
                    continue;
                }

                int a = index[(ear + n - 1) % n], b = index[ear], c2 = index[(ear + 1) % n];
                mesh.TriFacing(first + a, first + b, first + c2, up);
                made++;
                index.RemoveAt(ear);
            }

            if (index.Count == 3 && Cross(ring[index[0]], ring[index[1]], ring[index[2]]) > 1e-9f)
            {
                mesh.TriFacing(first + index[0], first + index[1], first + index[2], up);
                made++;
            }

            return made;
        }

        /// <summary>The outline with repeated and non-finite points taken out, and without the closing copy of the first point.</summary>
        private static List<Vec3> Clean(IList<Vec3> points)
        {
            var list = new List<Vec3>(points.Count);
            foreach (Vec3 p in points)
            {
                if (!p.IsFinite)
                    continue;
                if (list.Count > 0 && Same(list[list.Count - 1], p))
                    continue;
                list.Add(p);
            }

            while (list.Count > 1 && Same(list[0], list[list.Count - 1]))
                list.RemoveAt(list.Count - 1);
            return list;
        }

        private static bool Same(Vec3 a, Vec3 b) => Math.Abs(a.X - b.X) < 1e-6f && Math.Abs(a.Z - b.Z) < 1e-6f;

        /// <summary>The cross product (b - a) x (c - a) in (X, Z): positive for a left turn when X is right and Z up.</summary>
        public static float Cross(Vec3 a, Vec3 b, Vec3 c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

        /// <summary>Whether p is inside or on the triangle a, b, c (counter-clockwise).</summary>
        private static bool Inside(Vec3 p, Vec3 a, Vec3 b, Vec3 c) => Cross(a, b, p) >= -1e-9f && Cross(b, c, p) >= -1e-9f && Cross(c, a, p) >= -1e-9f;

        /// <summary>
        /// Joins a hole (clockwise) into the outer ring (counter-clockwise) by the shortest bridge between a hole vertex and an outer vertex that crosses no edge of either: the ring then runs
        /// outer ... bridge-start, hole from bridge-end all the way round back to bridge-end, bridge-start ... outer.
        /// </summary>
        private static List<Vec3> Bridge(List<Vec3> outer, List<Vec3> hole)
        {
            int bestO = -1, bestH = -1;
            float best = float.MaxValue;
            for (int i = 0; i < outer.Count; i++)
            {
                for (int j = 0; j < hole.Count; j++)
                {
                    float d = (outer[i].X - hole[j].X) * (outer[i].X - hole[j].X) + (outer[i].Z - hole[j].Z) * (outer[i].Z - hole[j].Z);
                    if (d >= best)
                        continue;
                    if (CrossesAny(outer[i], hole[j], outer, i) || CrossesAny(outer[i], hole[j], hole, j))
                        continue;
                    best = d;
                    bestO = i;
                    bestH = j;
                }
            }

            if (bestO < 0)
            {
                // No clear bridge (a hole outside the outline, or touching it): the hole is left out rather than knotting the ring.
                return outer;
            }

            var ring = new List<Vec3>(outer.Count + hole.Count + 2);
            for (int i = 0; i <= bestO; i++)
                ring.Add(outer[i]);
            for (int j = 0; j <= hole.Count; j++)
                ring.Add(hole[(bestH + j) % hole.Count]);
            for (int i = bestO; i < outer.Count; i++)
                ring.Add(outer[i]);
            return ring;
        }

        /// <summary>Whether the segment p-q crosses any edge of the ring; the two edges that end at the vertex skip (they share p or q) are left out.</summary>
        private static bool CrossesAny(Vec3 p, Vec3 q, List<Vec3> ring, int skip)
        {
            for (int i = 0, n = ring.Count; i < n; i++)
            {
                int k = (i + 1) % n;
                if (i == skip || k == skip)
                    continue;
                if (Segments(p, q, ring[i], ring[k]))
                    return true;
            }

            return false;
        }

        /// <summary>Whether two segments properly cross (a shared end point is not a crossing).</summary>
        public static bool Segments(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 1e-9f && d2 < -1e-9f) || (d1 < -1e-9f && d2 > 1e-9f)) && ((d3 > 1e-9f && d4 < -1e-9f) || (d3 < -1e-9f && d4 > 1e-9f));
        }

        /// <summary>The points of an arc about a centre in the ground plane from one angle to another (radians, counter-clockwise positive with X right and Z up), the ends included.</summary>
        public static void Arc(List<Vec3> into, float cx, float cz, float radius, float fromAngle, float toAngle, int segments, float y = 0f)
        {
            segments = Math.Max(1, segments);
            for (int i = 0; i <= segments; i++)
            {
                double a = fromAngle + (toAngle - fromAngle) * i / segments;
                into.Add(new Vec3(cx + radius * (float)Math.Cos(a), y, cz + radius * (float)Math.Sin(a)));
            }
        }
    }
}
