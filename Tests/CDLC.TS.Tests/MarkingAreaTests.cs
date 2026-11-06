using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The area markings drawn on any shape (MarkingAreas, MarkingFills.ClipConvex and Inset): an island the player draws, a gore between two carriageways that part (GoreLayout), a strip along a road and the
    /// thick lines of 线65; the outline text (MarkingPolygon) and the templates that use them.
    /// </summary>
    internal static class MarkingAreaTests
    {
        public static void RunAll()
        {
            Program.Run("areas: a convex piece is cut exactly to an area", Clip);
            Program.Run("areas: an outline moved inward, and when it cannot be", Inset);
            Program.Run("areas: V lines along a straight axis, their spacing and their point", StraightVs);
            Program.Run("areas: V lines and diagonals follow a bending axis, and pieces join", BentVs);
            Program.Run("areas: an island of any shape, its axis, its paint inside it", Islands);
            Program.Run("areas: a gore between curving sides, the Vs to the traffic", Gores);
            Program.Run("areas: the gore at a fork: the nose, the far end, what is not a fork", Forks);
            Program.Run("areas: the lead line before the nose of an exit, after the nose of an entry", Leads);
            Program.Run("areas: the outline as text", Outlines);
            Program.Run("areas: the thick lines of 线65 and the strip along a road", ThickAndStrip);
            Program.Run("areas: the templates of the island, the gore, the thick line and the strip", Templates);
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static float Area(MeshBuilder mesh)
        {
            double sum = 0.0;
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                sum += MeshBuilder.Cross(b - a, c - a).Length * 0.5;
            }

            return (float)sum;
        }

        private static void FacesUp(MeshBuilder mesh, string what)
        {
            int bad = 0;
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                if (!(MeshBuilder.Cross(b - a, c - a).Y > 0f))
                    bad++;
            }

            Program.Check(bad == 0, what + ": every triangle faces up (" + bad + " do not)");
        }

        /// <summary>Whether a point is inside or within tol of a simple polygon (even-odd, then the distance to its edges).</summary>
        private static bool InsidePolygon(IList<Vec3> polygon, float x, float z, float tol)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if ((polygon[i].Z > z) != (polygon[j].Z > z) && x < (polygon[j].X - polygon[i].X) * (z - polygon[i].Z) / (polygon[j].Z - polygon[i].Z) + polygon[i].X)
                    inside = !inside;
            }

            if (inside)
                return true;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vec3 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                float t = len2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((x - a.X) * dx + (z - a.Z) * dz) / len2));
                float px = a.X + dx * t - x, pz = a.Z + dz * t - z;
                if (px * px + pz * pz <= tol * tol)
                    return true;
            }

            return false;
        }

        private static void AllInside(MeshBuilder mesh, IList<Vec3> polygon, string what, float tol = 2e-3f)
        {
            int outside = 0;
            foreach (Vec3 p in mesh.Positions)
            {
                if (!InsidePolygon(polygon, p.X, p.Z, tol))
                    outside++;
            }

            Program.Check(outside == 0, what + ": no paint outside the area (" + outside + " points out)");
        }

        /// <summary>Whether a point of the ground is under a triangle of a mesh.</summary>
        private static bool Covered(MeshBuilder mesh, float x, float z)
        {
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                float d1 = (x - b.X) * (a.Z - b.Z) - (a.X - b.X) * (z - b.Z), d2 = (x - c.X) * (b.Z - c.Z) - (b.X - c.X) * (z - c.Z), d3 = (x - a.X) * (c.Z - a.Z) - (c.X - a.X) * (z - a.Z);
                bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(neg && pos))
                    return true;
            }

            return false;
        }

        private static MeshBuilder Triangles(IList<Vec3> polygon)
        {
            var mesh = new MeshBuilder();
            Polygon.Fill(mesh, polygon);
            return mesh;
        }

        // ---------------------------------------------------------------- clipping and inset

        private static void Clip()
        {
            // A square of 4 x 4 over a triangle (0,0), (4,0), (0,4): the overlap is the triangle's half of the square, 8 m2.
            var triangle = new List<Vec3> { P(0f, 0f), P(4f, 0f), P(0f, 4f) };
            var mesh = new MeshBuilder();
            int pieces = MarkingFills.ClipConvex(mesh, new List<Vec3> { P(-1f, -1f), P(3f, -1f), P(3f, 3f), P(-1f, 3f) }, Triangles(triangle));
            Program.Check(pieces > 0, "something is painted");
            // The square -1..3 each way over the triangle x, z >= 0, x + z <= 4: the square 0..3 less its corner past x + z = 4 (a triangle of legs 2): 9 - 2.
            Program.Near(Area(mesh), 9f - 2f, 1e-3f, "the area is the overlap");
            AllInside(mesh, triangle, "clip");
            FacesUp(mesh, "clip");
            // The other way round (clockwise) is the same.
            var other = new MeshBuilder();
            MarkingFills.ClipConvex(other, new List<Vec3> { P(-1f, -1f), P(-1f, 3f), P(3f, 3f), P(3f, -1f) }, Triangles(triangle));
            Program.Near(Area(other), Area(mesh), 1e-4f, "a clockwise piece is cut the same");
            // A piece beside the area paints nothing; no piece, no area: nothing.
            var none = new MeshBuilder();
            Program.Check(MarkingFills.ClipConvex(none, new List<Vec3> { P(10f, 10f), P(11f, 10f), P(11f, 11f) }, Triangles(triangle)) == 0 && none.VertexCount == 0, "a piece outside paints nothing");
            Program.Check(MarkingFills.ClipConvex(none, null, Triangles(triangle)) == 0 && MarkingFills.ClipConvex(none, new List<Vec3> { P(0f, 0f), P(1f, 1f) }, Triangles(triangle)) == 0 && MarkingFills.ClipConvex(none, triangle, null) == 0, "no piece or no area: nothing");
            // Over a concave area (an L), nothing falls in its notch.
            var l = new List<Vec3> { P(0f, 0f), P(4f, 0f), P(4f, 1f), P(1f, 1f), P(1f, 4f), P(0f, 4f) };
            var lMesh = new MeshBuilder();
            MarkingFills.ClipConvex(lMesh, new List<Vec3> { P(-1f, -1f), P(5f, -1f), P(5f, 5f), P(-1f, 5f) }, Triangles(l));
            Program.Near(Area(lMesh), 7f, 1e-3f, "a piece over a whole L is the L: 7 m2");
            Program.Check(!Covered(lMesh, 2.5f, 2.5f), "and the notch of the L is not painted");
        }

        private static void Inset()
        {
            List<Vec3> square = MarkingFills.Inset(new List<Vec3> { P(0f, 0f), P(10f, 0f), P(10f, 10f), P(0f, 10f) }, 1f);
            Program.Check(square != null && square.Count == 4, "a square moved in by 1 m");
            Program.Near(Polygon.SignedArea2(square) * 0.5f, 64f, 1e-3f, "is 8 x 8, counter-clockwise");
            List<Vec3> cw = MarkingFills.Inset(new List<Vec3> { P(0f, 0f), P(0f, 10f), P(10f, 10f), P(10f, 0f) }, 1f);
            Program.Near(Polygon.SignedArea2(cw) * 0.5f, 64f, 1e-3f, "a clockwise square the same");
            Program.Check(MarkingFills.Inset(new List<Vec3> { P(0f, 0f), P(10f, 0f), P(10f, 1f), P(0f, 1f) }, 0.6f) == null, "a strip 1 m wide moved in by 0.6 m is nothing (null)");
            Program.Check(MarkingFills.Inset(null, 1f) == null && MarkingFills.Inset(new List<Vec3> { P(0f, 0f), P(1f, 0f) }, 1f) == null, "no outline, no inset");
            List<Vec3> l = MarkingFills.Inset(new List<Vec3> { P(0f, 0f), P(6f, 0f), P(6f, 2f), P(2f, 2f), P(2f, 6f), P(0f, 6f) }, 0.5f);
            Program.Check(l != null && MarkingFills.IsSimple(l), "an L moved in by 0.5 m is still a simple outline");
            Program.Near(Polygon.SignedArea2(l) * 0.5f, 5f * 1f + 1f * 4f, 1e-3f, "of the right area: an L of arms 1 m wide, 5 and 5 m long, sharing a square: 9 m2");
            Program.Check(MarkingFills.IsSimple(new List<Vec3> { P(0f, 0f), P(1f, 0f), P(1f, 1f) }) && !MarkingFills.IsSimple(new List<Vec3> { P(0f, 0f), P(1f, 1f), P(1f, 0f), P(0f, 1f) }), "a triangle is simple, a bow tie is not");
        }

        // ---------------------------------------------------------------- the Vs

        private static readonly float Root2 = (float)Math.Sqrt(2.0);

        private static void StraightVs()
        {
            var area = new List<Vec3> { P(-5f, 0f), P(5f, 0f), P(5f, 20f), P(-5f, 20f) };
            var axis = new List<Vec3> { P(0f, 0f), P(0f, 20f) };
            var mesh = new MeshBuilder();
            var apexes = new List<Vec3>();
            int drawn = MarkingAreas.AxisBands(mesh, area, axis, 0.45f, 1f, true, 0f, apexes);
            Program.Check(drawn > 14, "V lines are drawn: " + drawn);
            AllInside(mesh, area, "Vs");
            FacesUp(mesh, "Vs");
            Program.Check(apexes.Count == 14, "20 m of axis has a V every 1.414 m (1 m apart square to the arms): " + apexes.Count);
            for (int i = 1; i < apexes.Count; i++)
                Program.Near(apexes[i].Z - apexes[i - 1].Z, Root2, 1e-3f, "the points are pitch x sqrt 2 apart along the axis");
            Program.Near(apexes[0].Z, Root2 * 0.5f, 1e-3f, "the first is half a step from the start");
            foreach (Vec3 a in apexes)
            {
                Program.Near(a.X, 0f, 1e-5f, "every point on the axis");
                // The arms go forward and out at 45 degrees: 1 m out each way, 1 m forward is on an arm; 1 m back is not (the V points back at the traffic that comes).
                if (a.Z > 2f && a.Z < 17f)
                {
                    Program.Check(Covered(mesh, a.X + 0.7f, a.Z + 0.7f) && Covered(mesh, a.X - 0.7f, a.Z + 0.7f), "forward and out is an arm");
                    Program.Check(Covered(mesh, a.X, a.Z), "the point is paint");
                    Program.Check(!Covered(mesh, a.X, a.Z - 0.45f), "and just behind the point (outside the V's corner) is road");
                }
            }

            // Width: on a line across an arm (square to it) the paint is 45 cm.
            Vec3 mid = apexes[6];
            var dir = new Vec3(1f, 0f, 1f) * (1f / Root2);
            var across = new Vec3(1f, 0f, -1f) * (1f / Root2);
            Vec3 onArm = mid + dir * 2f;
            float run = 0f;
            for (float t = -0.5f; t <= 0.5f; t += 0.001f)
            {
                Vec3 q = onArm + across * t;
                if (Covered(mesh, q.X, q.Z))
                    run += 0.001f;
            }

            Program.Near(run, 0.45f, 0.01f, "an arm is 45 cm wide square to it: " + run);
            // Square to the arms the next V's arm is 1 m on, centre to centre: measured from the paint, the start of this arm's band to the start of the next one's.
            float bandStart = float.NaN, nextStart = float.NaN;
            for (float t = -0.4f; t <= 1.4f; t += 0.001f)
            {
                Vec3 q = onArm - across * t;
                bool inside = Covered(mesh, q.X, q.Z);
                if (inside && float.IsNaN(bandStart))
                    bandStart = t;
                else if (inside && !float.IsNaN(bandStart) && t - bandStart > 0.6f && float.IsNaN(nextStart))
                    nextStart = t;
            }

            Program.Near(nextStart - bandStart, 1f, 0.01f, "the Vs are 1 m apart square to their arms: " + (nextStart - bandStart));

            // Diagonals: one band through each place, at 45 degrees leaning right going forward.
            var diag = new MeshBuilder();
            var places = new List<Vec3>();
            MarkingAreas.AxisBands(diag, area, axis, 0.45f, 1f, false, 0f, places);
            AllInside(diag, area, "diagonals");
            foreach (Vec3 a in places)
            {
                if (a.Z > 4f && a.Z < 16f)
                {
                    Program.Check(Covered(diag, a.X + 2f, a.Z + 2f) && Covered(diag, a.X - 2f, a.Z - 2f), "a diagonal runs forward-right through its place");
                    // Half a metre square to the band (between it and the next one, 1 m on) is road.
                    Program.Check(!Covered(diag, a.X - 0.35f, a.Z + 0.35f) && !Covered(diag, a.X + 0.35f, a.Z - 0.35f), "and not forward-left: square to it, half way to the next one, is road");
                }
            }

            // Nothing for an axis or an area that is not one.
            Program.Check(MarkingAreas.AxisBands(new MeshBuilder(), area, new List<Vec3> { P(0f, 0f) }, 0.45f, 1f, true) == 0 && MarkingAreas.AxisBands(new MeshBuilder(), null, axis, 0.45f, 1f, true) == 0
                && MarkingAreas.AxisBands(new MeshBuilder(), area, axis, 0.45f, 0f, true) == 0 && MarkingAreas.AxisBands(new MeshBuilder(), area, axis, 0.45f, 1f, true, float.NaN) == 0, "no axis, no area, no pitch or a phase that is not a number: nothing");
        }

        private static void BentVs()
        {
            // A strip 4 m wide along a quarter circle of radius 30 m (centre (30, 0)), the axis its middle line, running from (0, 0) north and bending east.
            var axis = new List<Vec3>();
            var inner = new List<Vec3>();
            var outer = new List<Vec3>();
            for (int i = 0; i <= 40; i++)
            {
                double a = Math.PI - i * (Math.PI / 2.0) / 40.0;
                axis.Add(P((float)(30.0 + 30.0 * Math.Cos(a)), (float)(30.0 * Math.Sin(a))));
                inner.Add(P((float)(30.0 + 28.0 * Math.Cos(a)), (float)(28.0 * Math.Sin(a))));
                outer.Add(P((float)(30.0 + 32.0 * Math.Cos(a)), (float)(32.0 * Math.Sin(a))));
            }

            var area = new List<Vec3>(outer);
            for (int i = inner.Count - 1; i >= 0; i--)
                area.Add(inner[i]);
            var mesh = new MeshBuilder();
            var apexes = new List<Vec3>();
            MarkingAreas.AxisBands(mesh, area, axis, 0.45f, 1f, true, 0f, apexes);
            AllInside(mesh, area, "bent Vs");
            FacesUp(mesh, "bent Vs");
            Program.Check(apexes.Count > 25, "Vs all along the bend: " + apexes.Count);
            int right = 0, total = 0;
            foreach (Vec3 a in apexes)
            {
                // The heading of the bend at the point: square to the radius, going clockwise.
                Vec3 radial = (a - P(30f, 0f)).HeadingXZ();
                var t = new Vec3(radial.Z, 0f, -radial.X);
                Vec3 n = t.RightXZ();
                Vec3 arm1 = a + (t + n) * (0.6f / Root2), arm2 = a + (t - n) * (0.6f / Root2), back1 = a + (n - t) * (0.6f / Root2), back2 = a - (t + n) * (0.6f / Root2);
                total++;
                if (Covered(mesh, arm1.X, arm1.Z) && Covered(mesh, arm2.X, arm2.Z) && !(Covered(mesh, back1.X, back1.Z) && Covered(mesh, back2.X, back2.Z)))
                    right++;
            }

            Program.Check(right >= total - 1, "every V is turned with the bend: its arms 45 degrees forward and out of the bend's heading there: " + right + " of " + total);

            // Pieces join: two halves of a straight strip, the second given the whole line's distance where it starts, make the Vs of the whole.
            var rect = new List<Vec3> { P(-2f, 0f), P(2f, 0f), P(2f, 40f), P(-2f, 40f) };
            var whole = new List<Vec3>();
            MarkingAreas.AxisBands(new MeshBuilder(), rect, new List<Vec3> { P(0f, 0f), P(0f, 40f) }, 0.45f, 1f, true, 0f, whole);
            var halves = new List<Vec3>();
            MarkingAreas.AxisBands(new MeshBuilder(), new List<Vec3> { P(-2f, 0f), P(2f, 0f), P(2f, 20f), P(-2f, 20f) }, new List<Vec3> { P(0f, 0f), P(0f, 20f) }, 0.45f, 1f, true, 0f, halves);
            var second = new List<Vec3>();
            // The second piece in its own frame (starting at 0) with the phase 20 m.
            MarkingAreas.AxisBands(new MeshBuilder(), new List<Vec3> { P(-2f, 0f), P(2f, 0f), P(2f, 20f), P(-2f, 20f) }, new List<Vec3> { P(0f, 0f), P(0f, 20f) }, 0.45f, 1f, true, 20f, second);
            foreach (Vec3 a in second)
                halves.Add(new Vec3(a.X, 0f, a.Z + 20f));
            Program.Check(halves.Count == whole.Count, "two pieces have the Vs of the whole: " + halves.Count + " / " + whole.Count);
            float worst = 0f;
            for (int i = 0; i < Math.Min(halves.Count, whole.Count); i++)
                worst = Math.Max(worst, Math.Abs(halves[i].Z - whole[i].Z));
            Program.Check(worst < 1e-3f, "at the same places: " + worst);
        }

        // ---------------------------------------------------------------- islands

        private static void Islands()
        {
            // A triangle pointing at the traffic (its point at z = 0): the axis runs along its middle.
            var triangle = new List<Vec3> { P(0f, 0f), P(1.5f, 20f), P(-1.5f, 20f) };
            List<Vec3> axis = MarkingAreas.CrossSectionAxis(triangle);
            Program.Check(axis.Count > 30, "the axis has a point every half metre: " + axis.Count);
            foreach (Vec3 p in axis)
                Program.Near(p.X, 0f, 1e-3f, "on the triangle's middle");
            for (int i = 1; i < axis.Count; i++)
                Program.Check(axis[i].Z > axis[i - 1].Z, "running +Z");
            // A parallelogram leaning right: its middle moves right as it goes.
            List<Vec3> lean = MarkingAreas.CrossSectionAxis(new List<Vec3> { P(0f, 0f), P(2f, 0f), P(12f, 20f), P(10f, 20f) });
            Vec3 m = lean[lean.Count / 2];
            Program.Near(m.X, 1f + m.Z * 0.5f, 0.05f, "a leaning island's axis is its middle: " + m);
            Program.Check(MarkingAreas.CrossSectionAxis(null).Count == 0, "no outline, no axis");
            // The marched axis follows an island round a bend: a band 3 m wide along a half circle of radius 20 m (from (0, 0) north, round to (40, 0) going south) has its axis on the radius 20.
            var band = new List<Vec3>();
            for (int i = 0; i <= 24; i++)
            {
                double a = Math.PI - i * Math.PI / 24.0;
                band.Add(P((float)(20.0 + 21.5 * Math.Cos(a)), (float)(21.5 * Math.Sin(a))));
            }

            for (int i = 24; i >= 0; i--)
            {
                double a = Math.PI - i * Math.PI / 24.0;
                band.Add(P((float)(20.0 + 18.5 * Math.Cos(a)), (float)(18.5 * Math.Sin(a))));
            }

            List<Vec3> marched = MarkingAreas.MarchingAxis(band);
            float marchedLength = 0f;
            for (int i = 1; i < marched.Count; i++)
                marchedLength += Vec3.DistanceXZ(marched[i - 1], marched[i]);
            Program.Check(marchedLength > (float)Math.PI * 20f * 0.85f, "it goes round the bend: " + marchedLength + " of " + (float)Math.PI * 20f);
            float off = 0f;
            foreach (Vec3 q in marched)
                off = Math.Max(off, Math.Abs(Vec3.DistanceXZ(q, P(20f, 0f)) - 20f));
            Program.Check(off < 0.1f, "on the band's middle line: " + off);
            // A straight triangle's marched axis is its middle too.
            foreach (Vec3 q in MarkingAreas.MarchingAxis(triangle))
                Program.Near(q.X, 0f, 0.05f, "a triangle's marched axis is on its middle");

            // A crescent (a bent island) of 12 corners: everything inside it, up, an outline and Vs.
            var crescent = new List<Vec3>();
            for (int i = 0; i <= 6; i++)
            {
                double a = i * Math.PI / 12.0;
                crescent.Add(P((float)(20.0 * (1.0 - Math.Cos(a))), (float)(20.0 * Math.Sin(a))));
            }

            for (int i = 6; i >= 1; i--)
            {
                double a = i * Math.PI / 12.0;
                crescent.Add(P((float)(20.0 - 16.0 * Math.Cos(a)), (float)(16.0 * Math.Sin(a))));
            }

            Program.Check(MarkingFills.IsSimple(crescent), "(the crescent is a simple outline)");
            var apexes = new List<Vec3>();
            MarkingShape island = MarkingAreas.Island(crescent, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.45f, false, apexes);
            Program.Check(island.Mesh.TriangleCount > 20 && apexes.Count > 3, "a bent island is painted with Vs: " + apexes.Count);
            AllInside(island.Mesh, crescent, "crescent");
            FacesUp(island.Mesh, "crescent");
            // The outline is painted all round: just inside each corner's edge is paint.
            int edged = 0;
            for (int i = 0; i < crescent.Count; i++)
            {
                Vec3 a = crescent[i], b = crescent[(i + 1) % crescent.Count];
                Vec3 middle = (a + b) * 0.5f;
                Vec3 inward = (b - a).HeadingXZ();
                inward = Polygon.SignedArea2(crescent) > 0f ? new Vec3(-inward.Z, 0f, inward.X) : new Vec3(inward.Z, 0f, -inward.X);
                Vec3 q = middle + inward * 0.07f;
                if (Covered(island.Mesh, q.X, q.Z))
                    edged++;
            }

            Program.Check(edged == crescent.Count, "every edge has its outline: " + edged + " of " + crescent.Count);
            // flip turns the Vs: their points toward +Z.
            var flipped = new List<Vec3>();
            MarkingShape back = MarkingAreas.Island(triangle, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.45f, true, flipped);
            var forward = new List<Vec3>();
            MarkingShape front = MarkingAreas.Island(triangle, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.45f, false, forward);
            Vec3 f0 = forward[forward.Count / 2], b0 = flipped[flipped.Count / 2];
            Program.Check(Covered(front.Mesh, f0.X + 0.3f, f0.Z + 0.3f) && !Covered(front.Mesh, f0.X, f0.Z - 0.4f), "unflipped: the Vs point back (-Z), to the traffic");
            Program.Check(Covered(back.Mesh, b0.X + 0.3f, b0.Z - 0.3f) && !Covered(back.Mesh, b0.X, b0.Z + 0.4f), "flipped: they point forward (+Z)");
            // The other fills, inside and up; nothing for an outline that crosses itself or has no area.
            foreach (MarkingWarnings.ChannelFill fill in new[] { MarkingWarnings.ChannelFill.Diagonal, MarkingWarnings.ChannelFill.CycleSide })
            {
                MarkingShape s = MarkingAreas.Island(crescent, 0.2f, fill, MarkingColour.Yellow);
                AllInside(s.Mesh, crescent, "crescent " + fill);
                FacesUp(s.Mesh, "crescent " + fill);
                Program.Check(s.Colour == MarkingColour.Yellow && s.Mesh.TriangleCount > 10, fill + ": painted in the colour given");
            }

            Program.Check(MarkingAreas.Island(new List<Vec3> { P(0f, 0f), P(4f, 4f), P(4f, 0f), P(0f, 4f) }, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White).Mesh.VertexCount == 0, "a bow tie is not an island");
            Program.Check(MarkingAreas.Island(new List<Vec3> { P(0f, 0f), P(4f, 0f), P(8f, 0.001f) }, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White).Mesh.VertexCount == 0, "nor three points in a line");
        }

        // ---------------------------------------------------------------- gores

        private static void Gores()
        {
            // An exit: the main side straight north, the ramp side curving away to the right (x = z * z / 200 from the nose at the origin), 40 m long.
            var main = new List<Vec3>();
            var ramp = new List<Vec3>();
            for (int i = 0; i <= 10; i++)
            {
                float z = i * 4f;
                main.Add(P(0f, z));
                ramp.Add(P(z * z / 200f, z));
            }

            foreach (bool exit in new[] { true, false })
            {
                var m = exit ? main : main.ConvertAll(p => P(p.X, -p.Z));
                var r = exit ? ramp : ramp.ConvertAll(p => P(p.X, -p.Z));
                var apexes = new List<Vec3>();
                MarkingShape gore = MarkingAreas.Gore(m, r, exit, apexes);
                string what = exit ? "curved exit" : "curved entry";
                List<Vec3> polygon = MarkingAreas.GorePolygon(m, r);
                AllInside(gore.Mesh, polygon, what);
                FacesUp(gore.Mesh, what);
                Program.Check(apexes.Count > 5, what + ": Vs: " + apexes.Count);
                int pointing = 0;
                foreach (Vec3 a in apexes)
                {
                    // The traffic runs +Z in both: a V's point is its back.
                    bool ahead = Covered(gore.Mesh, a.X + 0.3f, a.Z + 0.3f) && Covered(gore.Mesh, a.X - 0.3f, a.Z + 0.3f);
                    bool behind = Covered(gore.Mesh, a.X, a.Z - 0.45f);
                    if (ahead && !behind)
                        pointing++;
                }

                Program.Check(pointing >= apexes.Count - 3, what + ": the Vs point to the traffic: " + pointing + " of " + apexes.Count);
                // The edge lines: just inside each side near the far end, and across the far end, is paint; the middle of the gore between two Vs is not (it is not a solid fill).
                float zf = exit ? 39f : -39f;
                Program.Check(Covered(gore.Mesh, 0.1f, zf * 0.75f) && Covered(gore.Mesh, (zf * zf * 0.5625f) / 200f - 0.1f, zf * 0.75f), what + ": both sides are edged");
                Program.Check(Covered(gore.Mesh, 3.9f, exit ? 39.95f : -39.95f), what + ": and the far end");
                Program.Check(Area(gore.Mesh) < 0.7f * Math.Abs(Polygon.SignedArea2(polygon)) * 0.5f, what + ": not a solid fill");
            }

            // In a wide gore (8 m at the far end, straight sides) an arm of a V is 45 cm wide square to it (Fig. 94), and the edge lines 20.
            var wide = new List<Vec3>();
            MarkingShape wideGore = MarkingAreas.Gore(new List<Vec3> { P(0f, 0f), P(0f, 40f) }, new List<Vec3> { P(0f, 0f), P(8f, 40f) }, true, wide);
            Vec3 apex = wide[wide.Count - 4];
            var armDir = new Vec3(1f, 0f, 1f) * (1f / Root2);
            var square = new Vec3(1f, 0f, -1f) * (1f / Root2);
            Vec3 onArm = apex + armDir * 0.8f;
            float across = 0f;
            for (float t = -0.4f; t <= 0.4f; t += 0.001f)
            {
                Vec3 q = onArm + square * t;
                if (Covered(wideGore.Mesh, q.X, q.Z))
                    across += 0.001f;
            }

            Program.Near(across, 0.45f, 0.01f, "a V's arm in a gore is 45 cm wide: " + across);
            // The run of paint from the through side inward, at several places along it; where no V touches the line it is the line alone.
            float edge = float.MaxValue;
            for (float z = 18f; z < 22f; z += 0.25f)
            {
                float run = 0f;
                for (float x = 0f; x < 0.6f; x += 0.001f)
                {
                    if (!Covered(wideGore.Mesh, x + 0.0005f, z))
                        break;
                    run += 0.001f;
                }

                edge = Math.Min(edge, run);
            }

            Program.Near(edge, 0.2f, 0.005f, "and the edge line along the through side 20 cm: " + edge);

            // A blunt nose: the sides start 1 m apart: an edge line closes the nose.
            MarkingShape blunt = MarkingAreas.Gore(new List<Vec3> { P(0f, 0f), P(0f, 30f) }, new List<Vec3> { P(1f, 0f), P(4f, 30f) }, true);
            Program.Check(Covered(blunt.Mesh, 0.5f, 0.1f), "a blunt nose is closed by an edge line");
            Program.Check(MarkingAreas.Gore(null, ramp, true).Mesh.VertexCount == 0 && MarkingAreas.Gore(main, new List<Vec3> { P(0f, 0f) }, true).Mesh.VertexCount == 0, "no sides, no gore");
            Program.Check(MarkingAreas.Side(main, ramp) == 1 && MarkingAreas.Side(main, ramp.ConvertAll(p => P(-p.X, p.Z))) == -1, "the ramp's side: right, and left");
        }

        // ---------------------------------------------------------------- forks

        private static List<Vec3> Line(Func<float, Vec3> f, float from, float to, float step)
        {
            var line = new List<Vec3>();
            for (float s = from; s < to; s += step)
                line.Add(f(s));
            line.Add(f(to));
            return line;
        }

        private static void Forks()
        {
            // The through edge straight north from the junction; the ramp's edge starts overlapping it (half a metre to its left, as edges given from the junction's middle do) and curves off to the right.
            List<Vec3> main = Line(z => P(0f, z), 0f, 150f, 2f);
            List<Vec3> ramp = Line(z => P(-0.5f + z * z / 600f, z), 0f, 150f, 2f);
            GoreSolution g = GoreLayout.Solve(main, ramp, 1, 4f, 80f);
            Program.Check(g != null, "a fork is found");
            float noseZ = (float)Math.Sqrt(300.0);
            Program.Near(g.Nose.Z, noseZ, 0.05f, "the nose where the edges cross, between the samples: z = 17.32: " + g.Nose);
            Program.Near(g.Nose.X, 0f, 0.05f, "on the through edge");
            // The gore's width is measured square to the through edge from the ramp's nearest point: 4 m a little past z = 52, where the ramp is 4 m off along x.
            float farZ = (float)Math.Sqrt(600.0 * 4.5);
            Program.Check(g.Main[g.Main.Count - 1].Z > farZ - 0.1f && g.Main[g.Main.Count - 1].Z < farZ + 1.2f, "the far end where they are 4 m apart: z = 52 to 53: " + g.Main[g.Main.Count - 1]);
            Program.Near(g.FarWidth, 4f, 0.1f, "4 m wide there");
            Program.Check(g.Length > farZ - noseZ - 0.3f && g.Length < farZ - noseZ + 1.3f, "the gore's length: " + g.Length);
            Program.Check(!g.Blunt && Vec3.DistanceXZ(g.Main[0], g.Ramp[0]) < 1e-4f, "the sides start at one point");
            Program.Check(g.Main.Count <= GoreLayout.MaxSidePoints && g.Ramp.Count <= GoreLayout.MaxSidePoints && g.Main.Count >= 2, "at most " + GoreLayout.MaxSidePoints + " points a side");
            foreach (Vec3 p in g.Ramp)
                Program.Near(p.X, -0.5f + p.Z * p.Z / 600f, 0.05f, "the ramp side is on the ramp's edge");
            // The longest it may be.
            GoreSolution shortOne = GoreLayout.Solve(main, ramp, 1, 4f, 10f);
            Program.Near(shortOne.Length, 10f, 0.3f, "no longer than asked: " + shortOne.Length);
            Program.Check(shortOne.FarWidth < 4f, "and so narrower at its end");

            // The edges start apart (the game's pieces start at the junction's edge): carried back along their first pieces, they meet before their starts.
            List<Vec3> apartMain = Line(z => P(0f, z), 10f, 120f, 2f);
            List<Vec3> apartRamp = Line(z => P(0.1f * (z - 5f), z), 10f, 120f, 2f);
            GoreSolution apart = GoreLayout.Solve(apartMain, apartRamp, 1, 3f, 80f);
            Program.Check(apart != null && !apart.Blunt, "edges that start apart are carried back to where they meet");
            Program.Near(apart.Nose.Z, 5f, 0.4f, "at z = 5, before the pieces start: " + apart?.Nose);
            Program.Check(apart.NoseAt < 0f, "the nose is before the main side's start: " + apart.NoseAt);

            // Edges that never meet: side by side 1 m apart for 10 m, then parting (carried back, they stay 1 m apart): a blunt nose 1 m wide.
            List<Vec3> parallelStart = Line(z => P(1f + Math.Max(0f, z - 10f) * 0.15f, z), 0f, 100f, 1f);
            GoreSolution bluntFork = GoreLayout.Solve(main, parallelStart, 1, 3f, 80f);
            Program.Check(bluntFork != null && bluntFork.Blunt, "edges that never meet make a blunt nose");
            Program.Near(bluntFork.NoseGap, 1f, 0.05f, "as wide as they are apart: " + bluntFork.NoseGap);
            Program.Check(Vec3.DistanceXZ(bluntFork.Main[0], bluntFork.Ramp[0]) > 0.9f, "and the sides start apart");
            // Edges that come within Touch of each other are taken to meet: a sharp nose.
            List<Vec3> touching = Line(z => P(0.03f + Math.Max(0f, z - 10f) * 0.15f, z), 0f, 100f, 1f);
            GoreSolution sharp = GoreLayout.Solve(main, touching, 1, 3f, 80f);
            Program.Check(sharp != null && !sharp.Blunt && Vec3.DistanceXZ(sharp.Main[0], sharp.Ramp[0]) < 1e-4f, "edges 3 cm apart meet: a sharp nose");

            // A ramp on the left.
            GoreSolution left = GoreLayout.Solve(main, ramp.ConvertAll(p => P(-p.X, p.Z)), -1, 4f, 80f);
            Program.Check(left != null && Math.Abs(left.Nose.Z - noseZ) < 0.4f, "a ramp on the left the same way");
            // What is not a fork: the ramp on the other side than said, parallel edges, edges at too wide an angle, edges that do not part enough.
            Program.Check(GoreLayout.Solve(main, ramp, -1, 4f, 80f) == null, "the ramp is not on the side given: none");
            Program.Check(GoreLayout.Solve(main, Line(z => P(2f, z), 0f, 150f, 2f), 1, 4f, 80f) == null, "parallel edges: none");
            Program.Check(GoreLayout.Solve(main, Line(z => P(z * 1.7f, z), 0f, 80f, 2f), 1, 4f, 80f) == null, "edges at 60 degrees: a junction, not a gore");
            Program.Check(GoreLayout.Solve(main, Line(z => P(-0.5f + z * 0.004f, z), 0f, 150f, 2f), 1, 4f, 80f) == null, "edges that part by 10 cm in 150 m: none");
            Program.Check(GoreLayout.Solve(main, ramp, 1, 4f, 2f) == null, "a gore of 2 m, too narrow at its end (0.6 m at least): none");
            Program.Check(GoreLayout.Solve(null, ramp, 1, 4f, 80f) == null && GoreLayout.Solve(main, ramp, 1, 0f, 80f) == null && GoreLayout.Solve(main, ramp, 1, 4f, 0f) == null, "no edges, no width or no length: none");
            // The sides it found make a gore that is drawn.
            MarkingShape drawn = MarkingAreas.Gore(g.Main, g.Ramp, true);
            Program.Check(drawn.Mesh.TriangleCount > 30, "the gore of the fork is drawn: " + drawn.Mesh.TriangleCount);
            AllInside(drawn.Mesh, MarkingAreas.GorePolygon(g.Main, g.Ramp), "the fork's gore", 0.01f);
        }

        private static void Leads()
        {
            // An exit: the approach's centre line runs north 5 m to the left of where the nose will be, ending at the junction (z = 0); the nose is 17 m past it on the through edge.
            List<Vec3> approach = Line(z => P(-5f, z), -150f, 0f, 5f);
            var nose = P(0f, 17f);
            List<Vec3> lead = GoreLayout.LeadBefore(approach, nose, 60f);
            Program.Check(lead.Count >= 3, "a lead line: " + lead.Count);
            Program.Near(Vec3.DistanceXZ(lead[lead.Count - 1], nose), 0f, 1e-4f, "it ends at the nose");
            float length = 0f;
            for (int i = 1; i < lead.Count; i++)
                length += Vec3.DistanceXZ(lead[i - 1], lead[i]);
            Program.Near(length, 60f, 2.1f, "it is about as long as asked: " + length);
            foreach (Vec3 p in lead)
                Program.Near(p.X, 0f, 1e-3f, "it lies where the nose's line is on the approach, 5 m right of its centre");
            for (int i = 1; i < lead.Count; i++)
                Program.Check(lead[i].Z > lead[i - 1].Z, "it runs the way the traffic does");
            // An entry: from the nose on, along the road that leaves the junction.
            List<Vec3> departure = Line(z => P(-5f, z), 0f, 150f, 5f);
            var entryNose = P(0f, -17f);
            List<Vec3> after = GoreLayout.LeadAfter(departure, entryNose, 40f);
            Program.Near(Vec3.DistanceXZ(after[0], entryNose), 0f, 1e-4f, "an entry's lead starts at the nose");
            Program.Check(after[after.Count - 1].Z > 18f && after[after.Count - 1].Z < 25f, "and goes on for about the length: " + after[after.Count - 1]);
            for (int i = 1; i < after.Count; i++)
                Program.Check(after[i].Z > after[i - 1].Z, "the way the traffic runs");
            // A nose before the approach's end: the lead ends at the nose's place on it.
            List<Vec3> early = GoreLayout.LeadBefore(approach, P(0f, -30f), 20f);
            Program.Near(early[early.Count - 1].Z, -30f, 1e-3f, "a nose before the junction: the lead ends there");
            Program.Check(early[0].Z < -48f && early[0].Z > -52f, "and starts 20 m before it: " + early[0]);
            Program.Check(GoreLayout.LeadBefore(null, nose, 60f).Count == 0 && GoreLayout.LeadBefore(approach, nose, 0f).Count == 0, "no approach or no length: no lead");
            // A short lead to a nose far past the approach's end is as short as asked, straight before the nose.
            List<Vec3> shortLead = GoreLayout.LeadBefore(approach, nose, 9f);
            float shortLength = 0f;
            for (int i = 1; i < shortLead.Count; i++)
                shortLength += Vec3.DistanceXZ(shortLead[i - 1], shortLead[i]);
            Program.Near(shortLength, 9f, 0.05f, "a lead of 9 m to a nose 17 m past the junction is 9 m: " + shortLength);
            Program.Near(shortLead[0].Z, 8f, 0.05f, "from 9 m before the nose");
        }

        // ---------------------------------------------------------------- the outline as text

        private static void Outlines()
        {
            var world = new List<Vec3> { new Vec3(100f, 5f, 200f), new Vec3(110f, 5f, 200f), new Vec3(110f, 6f, 220f), new Vec3(100f, 6f, 215f) };
            float yaw = 0.6f;
            List<Vec3> local = MarkingPolygon.ToLocal(world, world[0], yaw);
            Program.Near(local[0].X, 0f, 1e-5f, "the first corner is the origin");
            List<Vec3> back = MarkingPolygon.ToWorld(local, world[0], yaw);
            float worst = 0f;
            for (int i = 0; i < world.Count; i++)
                worst = Math.Max(worst, (back[i] - world[i]).Length);
            Program.Check(worst < 1e-3f, "to the item's frame and back: " + worst);
            // The item's +Z is (sin yaw, cos yaw): a point 1 m along it is (0, 1) in the frame.
            List<Vec3> ahead = MarkingPolygon.ToLocal(new List<Vec3> { new Vec3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw)) }, Vec3.Zero, yaw);
            Program.Check(Math.Abs(ahead[0].X) < 1e-5f && Math.Abs(ahead[0].Z - 1f) < 1e-5f, "the frame's +Z is the yaw's way");

            string text = MarkingPolygon.Encode(local);
            List<Vec3> read = MarkingPolygon.Decode(text);
            Program.Check(read.Count == 4, "four corners read back");
            for (int i = 0; i < 4; i++)
                Program.Check(Math.Abs(read[i].X - local[i].X) <= 0.006f && Math.Abs(read[i].Z - local[i].Z) <= 0.006f, "to the centimetre");
            Program.Check(text.Length < 60 && text.IndexOf(';') < 0 && text.IndexOf('=') < 0, "short, and nothing in it that the settings' text uses: " + text);
            // The form with differences (0.12.0) and the old one (every corner as it is) read the same.
            string old = MarkingPolygon.EncodeAbsolute(local);
            Program.Check(text[0] == MarkingPolygon.DeltaMark && old[0] != MarkingPolygon.DeltaMark, "the new form is marked, the old is not: " + text + " / " + old);
            List<Vec3> readOld = MarkingPolygon.Decode(old);
            bool same = readOld.Count == read.Count;
            for (int i = 0; same && i < read.Count; i++)
                same = readOld[i].Equals(read[i]);
            Program.Check(same, "the old form reads as the new one");
            Program.Check(MarkingPolygon.Decode("d0:0|100:x|0:100").Count == 0 && MarkingPolygon.Decode("d").Count == 0 && MarkingPolygon.Decode("d0:0|100:0").Count == 0, "the new form: junk, nothing, two corners: none");
            Program.Check(MarkingPolygon.Decode("d0:0|400:400|0:-400|-400:400").Count == 0, "the new form: an outline that crosses itself: none");
            Program.Check(MarkingPolygon.Decode("d150000:0|60000:0|0:100").Count == 0 && MarkingPolygon.Decode("d100000:0|50000:0|0:100").Count == 3, "a corner more than 2 km out by the sum of the differences: none; within: read");
            // Rounding: the differences are of the rounded corners, so 40 corners of 0.004 m steps do not drift.
            var creep = new List<Vec3>();
            for (int i = 0; i < 39; i++)
                creep.Add(P(i * 1.004f, (float)Math.Sin(i * 0.3) * 0.004f));
            creep.Add(P(20f, 30f));
            string creepText = MarkingPolygon.Encode(creep);
            List<Vec3> creepRead = MarkingPolygon.Decode(creepText);
            float drift = 0f;
            for (int i = 0; i < creepRead.Count; i++)
                drift = Math.Max(drift, Math.Max(Math.Abs(creepRead[i].X - creep[i].X), Math.Abs(creepRead[i].Z - creep[i].Z)));
            Program.Check(creepRead.Count == 40 && drift <= 0.0051f, "40 corners read back each to the centimetre, no error adding up: " + drift);
            Program.Check(MarkingPolygon.Decode("").Count == 0 && MarkingPolygon.Decode(null).Count == 0 && MarkingPolygon.Decode("0:0|100:0").Count == 0 && MarkingPolygon.Decode("0:0|100:x|0:100").Count == 0, "nothing, two corners, or junk: none");
            Program.Check(MarkingPolygon.Decode("0:0|400:400|400:0|0:400").Count == 0, "an outline that crosses itself: none");
            Program.Check(MarkingPolygon.Decode("0:0|1000:0|1000:1000|500:-300|0:1000").Count == 0, "nor one that crosses itself and still has an area");
            Program.Check(MarkingPolygon.Decode("0:0|100:0|200:1").Count == 0, "three points nearly in a line (no area): none");
            Program.Check(MarkingPolygon.Encode(new List<Vec3> { P(0f, 0f), P(1f, 0f) }) == "" && MarkingPolygon.Encode(new List<Vec3> { P(0f, 0f), P(float.NaN, 0f), P(0f, 1f) }) == "", "no text for two corners or a corner that is not a number");
            var many = new List<Vec3>();
            for (int i = 0; i < 48; i++)
                many.Add(P((float)(30.0 * Math.Cos(i * Math.PI / 24.0)), (float)(30.0 * Math.Sin(i * Math.PI / 24.0))));
            Program.Check(MarkingPolygon.Encode(many) == "", "more than " + MarkingPolygon.MaxPoints + " corners: no text");
            string fitted = MarkingPolygon.EncodeFitting(many);
            Program.Check(fitted.Length > 0 && fitted.Length <= MarkingPolygon.MaxChars && MarkingPolygon.Decode(fitted).Count == MarkingPolygon.MaxPoints, "a circle of 48 corners is fitted to the most corners: " + MarkingPolygon.Decode(fitted).Count + " corners, " + fitted.Length + " characters");
            Program.Check(MarkingPolygon.Encode(many.GetRange(0, 40)).Length > 0, "40 corners are written as they are");
            string tight = MarkingPolygon.EncodeFitting(many, 80);
            Program.Check(tight.Length > 0 && tight.Length <= 80 && MarkingPolygon.Decode(tight).Count >= 3, "and into 80 characters: " + MarkingPolygon.Decode(tight).Count + " corners");
            List<Vec3> simple = MarkingPolygon.Simplify(many, 8);
            Program.Check(simple.Count == 8 && MarkingFills.IsSimple(simple), "simplified to 8 corners, still simple");
            Program.Check(Math.Abs(Polygon.SignedArea2(simple)) * 0.5f > 0.8f * (float)(Math.PI * 900.0) * 0.7f, "and keeping most of the area");
            // Stars of 30 corners at random radii, simplified to 6: every one stays a simple outline (a corner whose taking away would make it cross itself is not taken).
            var random = new Random(12345);
            int crossed = 0;
            for (int trial = 0; trial < 300; trial++)
            {
                var star = new List<Vec3>();
                for (int i = 0; i < 30; i++)
                {
                    double a = i * Math.PI * 2.0 / 30.0, r = 1.0 + random.NextDouble() * 9.0;
                    star.Add(P((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a))));
                }

                if (!MarkingFills.IsSimple(MarkingPolygon.Simplify(star, 6)))
                    crossed++;
            }

            Program.Check(crossed == 0, "simplified stars stay simple: " + crossed + " of 300 cross themselves");
            List<Vec3> kept = MarkingPolygon.Simplify(many, 5, new HashSet<int> { 3, 17 });
            Program.Check(kept.Contains(many[3]) && kept.Contains(many[17]), "the corners asked for are kept");
        }

        // ---------------------------------------------------------------- thick lines and strips

        private static void ThickAndStrip()
        {
            List<Vec3> straight = Line(z => P(0f, z), 0f, 30f, 2f);
            MarkingShape dashed = MarkingLines.ThickLine(straight);
            Program.Near(dashed.Area, 5f * 3f * 0.45f, 1e-3f, "30 m of 3-3: five dashes of 3 m, 45 wide");
            Program.Check(Covered(dashed.Mesh, 0f, 1.5f) && !Covered(dashed.Mesh, 0f, 4.5f) && Covered(dashed.Mesh, 0.2f, 1.5f) && !Covered(dashed.Mesh, 0.24f, 1.5f), "a dash, a gap; 45 wide");
            MarkingShape solidEnd = MarkingLines.ThickLine(straight, 0f, 28f, 31f);
            Program.Check(Covered(solidEnd.Mesh, 0f, 28.5f) && Covered(solidEnd.Mesh, 0f, 29.9f), "the solid at the end is paint all along");
            Program.Check(!Covered(solidEnd.Mesh, 0f, 27.5f) && Covered(solidEnd.Mesh, 0f, 25.5f), "the dashes before it stay");
            // A solid that starts inside a dash: the part of the dash before it stays.
            MarkingShape split = MarkingLines.ThickLine(straight, 0f, 25f, 31f);
            Program.Check(Covered(split.Mesh, 0f, 24.5f) && Covered(split.Mesh, 0f, 25.5f) && Covered(split.Mesh, 0f, 28.5f) && !Covered(split.Mesh, 0f, 23.5f), "a solid from 25 m: the dash of 24 to 27 is paint up to it, then the solid");
            MarkingShape solidStart = MarkingLines.ThickLine(straight, 0f, -1f, 2f);
            Program.Check(Covered(solidStart.Mesh, 0f, 1.0f) && Covered(solidStart.Mesh, 0f, 1.95f) && !Covered(solidStart.Mesh, 0f, 4.5f), "a solid at the start");
            // A piece that starts 30 m into the line, with the solid at 58 to 61 of the whole: its last 2 m.
            MarkingShape piece = MarkingLines.ThickLine(straight, 30f, 58f, 61f);
            Program.Check(Covered(piece.Mesh, 0f, 28.5f) && Covered(piece.Mesh, 0f, 29.5f) && Covered(piece.Mesh, 0f, 1.5f), "the second piece carries the pattern on (a dash at 30 to 33) and has the solid at its end");
            Program.Check(MarkingLines.ThickLine(null).Mesh.VertexCount == 0 && MarkingLines.ThickLine(straight, float.NaN).Mesh.VertexCount == 0, "no line, or a phase that is not a number: nothing");

            // A strip 2 m wide to the right of a 20 m line: the long sides, the ends, and the fill inside.
            List<Vec3> line = Line(z => P(0f, z), 0f, 20f, 2f);
            var rect = new List<Vec3> { P(0f, 0f), P(2f, 0f), P(2f, 20f), P(0f, 20f) };
            MarkingShape strip = MarkingAreas.Strip(line, 2f, true, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow);
            AllInside(strip.Mesh, rect, "strip", 0.01f);
            FacesUp(strip.Mesh, "strip");
            Program.Check(strip.Colour == MarkingColour.Yellow, "yellow");
            Program.Check(Covered(strip.Mesh, 0.07f, 10f) && Covered(strip.Mesh, 1.93f, 10f), "both long sides are edged");
            Program.Check(Covered(strip.Mesh, 1f, 0.07f) && Covered(strip.Mesh, 1f, 19.93f), "and both ends");
            MarkingShape open = MarkingAreas.Strip(line, 2f, true, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow, 0.15f, 0.45f, true, false, false);
            Program.Check(!Covered(open.Mesh, 1f, 0.07f) || !Covered(open.Mesh, 0.6f, 0.07f) || !Covered(open.Mesh, 1.4f, 0.07f), "a piece in the middle of a strip has no end lines");
            MarkingShape leftStrip = MarkingAreas.Strip(line, 2f, false, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow);
            AllInside(leftStrip.Mesh, new List<Vec3> { P(-2f, 0f), P(0f, 0f), P(0f, 20f), P(-2f, 20f) }, "a strip on the left", 0.01f);
            // Vs in a strip point to its start, or its end.
            MarkingShape vs = MarkingAreas.Strip(line, 3f, true, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.15f, 0.45f, true);
            MarkingShape reversed = MarkingAreas.Strip(line, 3f, true, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.15f, 0.45f, false);
            int toStart = 0, toEnd = 0;
            for (float z = 3f; z < 17f; z += 0.05f)
            {
                // On the middle line of the strip a V's point is paint with road just behind it (toward its point's side).
                if (Covered(vs.Mesh, 1.5f, z) && !Covered(vs.Mesh, 1.5f, z - 0.4f) && Covered(vs.Mesh, 1.9f, z + 0.4f))
                    toStart++;
                if (Covered(reversed.Mesh, 1.5f, z) && !Covered(reversed.Mesh, 1.5f, z + 0.4f) && Covered(reversed.Mesh, 1.9f, z - 0.4f))
                    toEnd++;
            }

            Program.Check(toStart > 5 && toEnd > 5, "the Vs point to the start, or to the end when asked: " + toStart + " / " + toEnd);
            // Two pieces of a strip, joined, paint as the whole does (the fill lines up at the join).
            List<Vec3> whole = Line(z => P(0f, z), 0f, 40f, 2f);
            MarkingShape all = MarkingAreas.Strip(whole, 2f, true, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow);
            MarkingShape first = MarkingAreas.Strip(Line(z => P(0f, z), 0f, 20f, 2f), 2f, true, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow, 0.15f, 0.45f, true, true, false, 0f, 40f);
            MarkingShape second = MarkingAreas.Strip(Line(z => P(0f, z), 0f, 20f, 2f), 2f, true, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow, 0.15f, 0.45f, true, false, true, 20f, 40f);
            int differ = 0, samples = 0;
            for (float z = 0.5f; z < 39.5f; z += 0.13f)
            {
                for (float x = 0.3f; x < 1.7f; x += 0.29f)
                {
                    samples++;
                    bool w = Covered(all.Mesh, x, z), p = z < 20f ? Covered(first.Mesh, x, z) : Covered(second.Mesh, x, z - 20f);
                    if (w != p && Math.Abs(z - 20f) > 0.02f)
                        differ++;
                }
            }

            Program.Check(differ <= samples / 200, "two pieces paint as one strip: " + differ + " of " + samples + " samples differ");
            // The same with Vs pointing to the strip's end: they are counted from the whole strip's end, so the two pieces still paint as the whole.
            MarkingShape allE = MarkingAreas.Strip(whole, 2f, true, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.15f, 0.45f, false);
            MarkingShape firstE = MarkingAreas.Strip(Line(z => P(0f, z), 0f, 20f, 2f), 2f, true, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.15f, 0.45f, false, true, false, 0f, 40f);
            MarkingShape secondE = MarkingAreas.Strip(Line(z => P(0f, z), 0f, 20f, 2f), 2f, true, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.15f, 0.45f, false, false, true, 20f, 40f);
            int differE = 0;
            for (float z = 0.5f; z < 39.5f; z += 0.13f)
            {
                for (float x = 0.3f; x < 1.7f; x += 0.29f)
                {
                    bool w = Covered(allE.Mesh, x, z), p = z < 20f ? Covered(firstE.Mesh, x, z) : Covered(secondE.Mesh, x, z - 20f);
                    if (w != p && Math.Abs(z - 20f) > 0.02f)
                        differE++;
                }
            }

            Program.Check(differE <= samples / 200, "and with the Vs to the end: " + differE + " of " + samples + " samples differ");
            Program.Check(MarkingAreas.Strip(null, 2f, true, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow).Mesh.VertexCount == 0 && MarkingAreas.Strip(line, 0.1f, true, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow).Mesh.VertexCount == 0, "no line, or too narrow: nothing");
        }

        // ---------------------------------------------------------------- templates

        private static string PolygonSpec(GuideTemplate t, IList<Vec3> local, int split = -1, bool blunt = false) => MarkingTemplates.WithPolygon(t, t.Default().ToString(), MarkingPolygon.Encode(local), split, blunt);

        private static void Templates()
        {
            // The island: drawn by corners, or the default triangle.
            GuideTemplate island = MarkingTemplates.ById("mk.channel");
            var l = new List<Vec3> { P(0f, 0f), P(6f, 0f), P(6f, 2f), P(2f, 2f), P(2f, 8f), P(0f, 8f) };
            MarkingPlan drawn = MarkingTemplates.PlanOf(island, PolygonSpec(island, l));
            Program.Near(drawn.MinX, 0f, 0.01f, "an island drawn as an L is painted where the L is");
            Program.Near(drawn.MaxX, 6f, 0.01f, "");
            Program.Near(drawn.MaxZ, 8f, 0.01f, "");
            foreach (MarkingPart part in drawn.Parts)
                AllInside(part.Mesh, l, "the L island", 0.01f);
            MarkingPlan triangle = MarkingTemplates.PlanOf(island, island.Default().ToString());
            Program.Near(triangle.MaxZ - triangle.MinZ, 20f, 0.01f, "without corners, the triangle of the length");
            GuideSpec withL = island.Parse(PolygonSpec(island, l));
            Program.Check(!island.FieldOf("len").Visible(withL) && !island.FieldOf("width").Visible(withL) && island.FieldOf("len").Visible(island.Default()), "with corners the length and the width are not shown");
            Program.Check(!island.FieldOf(MarkingPolygon.Key).Visible(withL), "the outline is never shown");
            Program.Check(island.MarkingNote(withL, true).Contains("6") && island.MarkingNote(island.Default(), true).Contains("单击"), "the editor says how it was drawn, or how to draw it");
            // A damaged outline is the triangle.
            MarkingPlan damaged = MarkingTemplates.PlanOf(island, island.Parse(island.Default().ToString()).With(MarkingPolygon.Key, "0:0|zz|5:5").ToString());
            Program.Near(damaged.MaxZ - damaged.MinZ, 20f, 0.01f, "a damaged outline is drawn as the triangle");

            // The gore of a fork: its outline and split; the straight figure without.
            GuideTemplate gore = MarkingTemplates.ById("mk.gore");
            var main = new List<Vec3> { P(0f, 0f), P(0f, 10f), P(0f, 20f), P(0f, 30f) };
            var ramp = new List<Vec3> { P(0f, 0f), P(0.6f, 10f), P(1.8f, 20f), P(4f, 30f) };
            List<Vec3> polygon = MarkingAreas.GorePolygon(main, ramp);
            string spec = PolygonSpec(gore, polygon, main.Count - 1);
            Program.Check(MarkingTemplates.GoreSides(gore.Parse(spec), out List<Vec3> m2, out List<Vec3> r2) && m2.Count == 4 && r2.Count == 4, "the sides are read back from the outline and the split");
            Program.Check(Vec3.DistanceXZ(r2[0], m2[0]) < 1e-3f && Vec3.DistanceXZ(r2[3], P(4f, 30f)) < 0.01f, "the ramp side from the nose to its far end");
            MarkingPlan fork = MarkingTemplates.PlanOf(gore, spec);
            foreach (MarkingPart part in fork.Parts)
                AllInside(part.Mesh, polygon, "the gore of a fork", 0.01f);
            Program.Near(fork.MinZ, 0f, 0.01f, "the gore of a fork has no lead of its own (that is the thick line's)");
            MarkingPlan straight = MarkingTemplates.PlanOf(gore, gore.Default().ToString());
            Program.Near(straight.MinZ, -60f, 0.01f, "the straight figure has its lead");
            // A blunt gore: the ramp side starts apart.
            var bluntRamp = new List<Vec3> { P(1f, 0f), P(1.6f, 10f), P(2.8f, 20f), P(5f, 30f) };
            var bluntPolygon = new List<Vec3>(main);
            for (int i = bluntRamp.Count - 1; i >= 0; i--)
                bluntPolygon.Add(bluntRamp[i]);
            Program.Check(MarkingTemplates.GoreSides(gore.Parse(PolygonSpec(gore, bluntPolygon, 3, true)), out _, out List<Vec3> br) && br.Count == 4 && Vec3.DistanceXZ(br[0], P(1f, 0f)) < 0.01f, "a blunt nose's ramp side starts where it does");
            Program.Check(!MarkingTemplates.GoreSides(gore.Parse(PolygonSpec(gore, polygon, 0)), out _, out _) && !MarkingTemplates.GoreSides(gore.Parse(PolygonSpec(gore, polygon, 99)), out _, out _), "a split that is not in the outline: no sides (the straight figure is drawn)");
            Program.Near(MarkingTemplates.PlanOf(gore, PolygonSpec(gore, polygon, 99)).MinZ, -60f, 0.01f, "");
            // The straight figure's ramp on the left.
            MarkingPlan leftRamp = MarkingTemplates.PlanOf(gore, gore.Parse(gore.Default().ToString()).With("side", "l").ToString());
            Program.Check(leftRamp.MinX < -3.9f && leftRamp.MaxX < 0.01f, "a ramp on the left: the gore to the left: " + leftRamp.MinX + ".." + leftRamp.MaxX);

            // The thick line: the solid where the line ends (an exit's), on the last piece only.
            GuideTemplate thick = MarkingTemplates.ById("mk.line.thick");
            Program.Check(MarkingTemplates.IsRun(thick) && thick.FieldOf(MarkingTemplates.TotalKey) != null, "the thick line is a run, and knows the whole line's length");
            List<Vec3> piece = Line(z => P(0f, z), 0f, 30f, 2f);
            string path = RunPath.Encode(piece, out _);
            string exitSpec = thick.Default().ToString();
            MarkingPlan alone = MarkingTemplates.PlanOf(thick, MarkingTemplates.WithRun(thick, exitSpec, path, 0f, 30f));
            MarkingPlan firstOfTwo = MarkingTemplates.PlanOf(thick, MarkingTemplates.WithRun(thick, exitSpec, path, 0f, 60f));
            MarkingPlan lastOfTwo = MarkingTemplates.PlanOf(thick, MarkingTemplates.WithRun(thick, exitSpec, path, 30f, 60f));
            Program.Check(CoveredPlan(alone, 0f, 29f) && CoveredPlan(alone, 0f, 28.2f), "a line alone ends in the solid");
            Program.Check(!CoveredPlan(alone, 0f, 27.5f), "2 m of it: before that the gap of the dashes (27 to 30) is road");
            Program.Check(!CoveredPlan(firstOfTwo, 0f, 29f) || !CoveredPlan(firstOfTwo, 0f, 28.2f), "the first of two pieces does not");
            Program.Check(CoveredPlan(lastOfTwo, 0f, 29f) && CoveredPlan(lastOfTwo, 0f, 28.2f), "the last does");
            MarkingPlan entryLine = MarkingTemplates.PlanOf(thick, MarkingTemplates.WithRun(thick, thick.Parse(exitSpec).With("solid", "start").ToString(), path, 0f, 60f));
            Program.Check(CoveredPlan(entryLine, 0f, 0.5f) && CoveredPlan(entryLine, 0f, 1.8f) && !CoveredPlan(entryLine, 0f, 4.5f), "an entry's line starts with the solid");

            // The strip: the first piece is edged at its start, the last at its end.
            GuideTemplate strip = MarkingTemplates.ById("mk.strip");
            string stripSpec = strip.Default().ToString();
            MarkingPlan s1 = MarkingTemplates.PlanOf(strip, MarkingTemplates.WithRun(strip, stripSpec, path, 0f, 60f)), s2 = MarkingTemplates.PlanOf(strip, MarkingTemplates.WithRun(strip, stripSpec, path, 30f, 60f));
            Program.Check(CoveredPlan(s1, 0.75f, 0.07f) && CoveredPlan(s1, 0.45f, 0.07f), "the first piece of a strip is edged at its start");
            Program.Check(!(CoveredPlan(s1, 0.75f, 29.93f) && CoveredPlan(s1, 0.45f, 29.93f) && CoveredPlan(s1, 1.05f, 29.93f)), "and open at the join");
            Program.Check(CoveredPlan(s2, 0.75f, 29.93f) && CoveredPlan(s2, 0.45f, 29.93f), "the last piece is edged at its end");
            Program.Check(!(CoveredPlan(s2, 0.75f, 0.07f) && CoveredPlan(s2, 0.45f, 0.07f) && CoveredPlan(s2, 1.05f, 0.07f)), "and open where it joins the first");

            // The settings fit their field: an outline of 24 corners each a kilometre off, the longest path of a run.
            var far = new List<Vec3>();
            for (int i = 0; i < MarkingPolygon.MaxPoints; i++)
                far.Add(P((float)(999.0 * Math.Cos(i * Math.PI * 2 / MarkingPolygon.MaxPoints)) - 0.01f * i, (float)(-999.0 * Math.Sin(i * Math.PI * 2 / MarkingPolygon.MaxPoints))));
            string farText = MarkingPolygon.EncodeFitting(far);
            foreach (GuideTemplate t in new[] { island, gore })
            {
                string full = MarkingTemplates.WithPolygon(t, t.Default().ToString(), farText, t == gore ? 11 : -1, true);
                Program.Check(System.Text.Encoding.UTF8.GetByteCount(full) <= GuideSpec.MaxBytes, t.Id + ": the settings with the longest outline fit: " + System.Text.Encoding.UTF8.GetByteCount(full));
                Program.Check(t.Parse(full).Get(MarkingPolygon.Key) == farText, t.Id + ": and the outline is kept whole");
            }

            var longest = new List<Vec3> { P(0f, 0f) };
            for (int i = 1; i <= RunPath.MaxPieces; i++)
                longest.Add(P(longest[i - 1].X + (i % 2 == 0 ? 0.9f : -0.9f), longest[i - 1].Z + 3.1f));
            foreach (List<Vec3> run in RunPath.Split(longest, MarkingRuns.MaxItemLength))
            {
                string runPath = RunPath.Encode(run, out _);
                foreach (GuideTemplate t in MarkingTemplates.All)
                {
                    if (!MarkingTemplates.IsRun(t))
                        continue;
                    string full = MarkingTemplates.WithRun(t, t.Default().ToString(), runPath, 9999.99f, 9999.99f);
                    Program.Check(System.Text.Encoding.UTF8.GetByteCount(full) <= GuideSpec.MaxBytes && t.Parse(full).Get(RunPath.Key) == runPath, t.Id + ": a run's longest settings fit, the path whole: " + System.Text.Encoding.UTF8.GetByteCount(full));
                }
            }
        }

        private static bool CoveredPlan(MarkingPlan plan, float x, float z)
        {
            foreach (MarkingPart part in plan.Parts)
            {
                if (Covered(part.Mesh, x, z))
                    return true;
            }

            return false;
        }
    }
}
