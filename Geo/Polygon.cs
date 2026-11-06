using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Triangulation of a flat polygon in the ground plane (X, Z) by ear clipping, for the figures of the road markings (arrows, crosses, the outlines of glyphs): a simple outline, in either order,
    /// optionally with holes, into up-facing triangles of a MeshBuilder. O(n^2) in the vertices, which is small here. Pure.
    /// </summary>
    public static class Polygon
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
            // Ear clipping over a linked ring: only a reflex corner (or one in a line with its neighbours) can lie inside an ear, so only those are tested; the search goes on from where the last ear was
            // cut instead of starting again, which keeps a long strip or a glyph from costing the cube of its corners (0.12.0).
            int count = ring.Count;
            var prev = new int[count];
            var next = new int[count];
            var reflex = new bool[count];
            for (int i = 0; i < count; i++)
            {
                prev[i] = (i + count - 1) % count;
                next[i] = (i + 1) % count;
            }

            for (int i = 0; i < count; i++)
                reflex[i] = Cross(ring[prev[i]], ring[i], ring[next[i]]) <= 1e-9f;

            int made = 0;
            int remaining = count;
            int v = 0, misses = 0, guard = 0;
            while (remaining > 3 && guard++ < 4000000)
            {
                int ia = prev[v], ic = next[v];
                bool ear = !reflex[v];
                if (ear)
                {
                    for (int k = next[ic]; k != ia; k = next[k])
                    {
                        if (!reflex[k])
                            continue;
                        // A point that is a copy of a corner (a bridge to a hole makes two) is not inside the ear.
                        if (Same(ring[k], ring[ia]) || Same(ring[k], ring[v]) || Same(ring[k], ring[ic]))
                            continue;
                        if (Inside(ring[k], ring[ia], ring[v], ring[ic]))
                        {
                            ear = false;
                            break;
                        }
                    }
                }

                if (ear)
                {
                    mesh.TriFacing(first + ia, first + v, first + ic, up);
                    made++;
                    Unlink(v, prev, next);
                    remaining--;
                    reflex[ia] = Cross(ring[prev[ia]], ring[ia], ring[next[ia]]) <= 1e-9f;
                    reflex[ic] = Cross(ring[prev[ic]], ring[ic], ring[next[ic]]) <= 1e-9f;
                    // The corner before may be an ear now.
                    v = ia;
                    misses = 0;
                    continue;
                }

                v = ic;
                if (++misses < remaining)
                    continue;

                // No clean ear all round (collinear runs or a numerical knot): drop the corner of the least turn so that the loop ends; the triangle it would make is thin.
                int worst = v, k2 = v;
                float least = float.MaxValue;
                for (int c = 0; c < remaining; c++, k2 = next[k2])
                {
                    float turn = Math.Abs(Cross(ring[prev[k2]], ring[k2], ring[next[k2]]));
                    if (turn < least)
                    {
                        least = turn;
                        worst = k2;
                    }
                }

                int before = prev[worst], after = next[worst];
                Unlink(worst, prev, next);
                remaining--;
                reflex[before] = Cross(ring[prev[before]], ring[before], ring[next[before]]) <= 1e-9f;
                reflex[after] = Cross(ring[prev[after]], ring[after], ring[next[after]]) <= 1e-9f;
                v = before;
                misses = 0;
            }

            if (remaining == 3)
            {
                int x = v, y = next[v], z = next[next[v]];
                if (Cross(ring[x], ring[y], ring[z]) > 1e-9f)
                {
                    mesh.TriFacing(first + x, first + y, first + z, up);
                    made++;
                }
            }

            return made;
        }

        private static void Unlink(int v, int[] prev, int[] next)
        {
            next[prev[v]] = next[v];
            prev[next[v]] = prev[v];
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
