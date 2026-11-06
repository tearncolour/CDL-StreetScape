using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The normals of ground paint (0.12.83, SurfaceNormals): they follow the heights the paint was laid at, and paint that lies flat is as it was.</summary>
    internal static class SurfaceNormalsTests
    {
        public static void RunAll()
        {
            Program.Run("surface normals: paint on flat ground keeps its normals to the last bit", Flat);
            Program.Run("surface normals: paint on a plane takes the plane's normal", Plane);
            Program.Run("surface normals: a ribbon over a crest leans with it, continuously", Crest);
            Program.Run("surface normals: nothing but paint is changed, and nothing but the normals", OnlyPaint);
            Program.Run("surface normals: a quad wound the other way, a big triangle and a small one, the flat end of a slope", Weights);
            Program.Run("surface normals: a barrier's solid is not paint", NotPaint);
            Program.Run("surface normals: a line with no length, a vertical step and a point that is not a number", Degenerate);
            Program.Run("surface normals: a marking on a grade, in the plan", PlanGrade);
            Program.Run("surface normals: a marking on a grid of heights, in the plan", PlanGrid);
            Program.Run("surface normals: a run over a crest, in the plan, and the switch", PlanRun);
            Program.Run("surface normals: a bent marking takes them after the bend", PlanBent);
            Program.Run("surface normals: on a slope no paint of any marking is left straight up where it leans", EveryMarking);
            Program.Run("surface normals: a plan is kept for each state of the switch", Keys);
            Program.Run("surface normals: every marking on flat ground is as 0.12.82 built it", FlatGolden);
        }

        /// <summary>Only the comparison with 0.12.82, in a process of its own (the quick mode markflat).</summary>
        public static void GoldenOnly() => Program.Run("surface normals: every marking on flat ground is as 0.12.82 built it", FlatGolden);

        private static readonly Vec3 Up = new Vec3(0f, 1f, 0f);

        private static MeshBuilder Ribbon(Func<float, float> y, float length = 20f, float step = 1f, float width = 0.15f)
        {
            var points = new List<Vec3>();
            for (float z = 0f; z <= length + 1e-4f; z += step)
                points.Add(new Vec3(0f, y(z), z));
            var mesh = new MeshBuilder();
            RoadMarkings.Ribbon(mesh, points, width);
            return mesh;
        }

        private static MeshBuilder Copy(MeshBuilder m)
        {
            var c = new MeshBuilder();
            c.Positions.AddRange(m.Positions);
            c.Normals.AddRange(m.Normals);
            c.U.AddRange(m.U);
            c.V.AddRange(m.V);
            c.Indices.AddRange(m.Indices);
            return c;
        }

        private static bool Same(Vec3 a, Vec3 b) => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X) && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
            && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);

        private static void Flat()
        {
            foreach (float y in new[] { 0f, 0.4f, 3.5f, -2.25f })
            {
                MeshBuilder mesh = Ribbon(z => y);
                MeshBuilder before = Copy(mesh);
                SurfaceNormals.Fit(mesh);
                for (int i = 0; i < mesh.VertexCount; i++)
                    Program.Check(Same(mesh.Normals[i], before.Normals[i]) && Same(mesh.Normals[i], Up), "flat at " + y + ": vertex " + i + " is straight up");
            }

            // Heights that differ by a float's last bit (a grid of one height read at different places) are flat too.
            MeshBuilder noisy = Ribbon(z => 0.4f + (z % 2f == 0f ? 0f : 3e-8f));
            SurfaceNormals.Fit(noisy);
            foreach (Vec3 n in noisy.Normals)
                Program.Check(Same(n, Up), "last-bit noise is flat");
            // A slope of 0.2 % over the same length is not.
            MeshBuilder gentle = Ribbon(z => 0.002f * z);
            SurfaceNormals.Fit(gentle);
            Program.Check(gentle.Normals.TrueForAll(n => n.Z < -0.0019f), "a slope of 0.2 % leans every vertex");
        }

        private static void Plane()
        {
            foreach ((float a, float b) in new[] { (0.08f, -0.05f), (0.5f, 0.2f), (-0.3f, 0.7f) })
                PlaneOf(a, b);
        }

        private static void PlaneOf(float a, float b)
        {
            var mesh = new MeshBuilder();
            const int n = 5;
            for (int j = 0; j <= n; j++)
            {
                for (int i = 0; i <= n; i++)
                {
                    float x = i * 0.7f, z = j * 1.3f;
                    mesh.Vertex(new Vec3(x, a * x + b * z, z), Up, x, z);
                }
            }

            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int v00 = j * (n + 1) + i, v10 = v00 + 1, v01 = v00 + n + 1, v11 = v01 + 1;
                    mesh.QuadFacing(v00, v01, v11, v10, Up);
                }
            }

            SurfaceNormals.Fit(mesh);
            Vec3 want = MeshBuilder.Normalize(new Vec3(-a, 1f, -b));
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vec3 got = mesh.Normals[i];
                Program.Near(got.Length, 1f, 1e-5f, "vertex " + i + ": unit length");
                Program.Near(got.X, want.X, 2e-6f, "vertex " + i + ": x is the plane's");
                Program.Near(got.Y, want.Y, 2e-6f, "vertex " + i + ": y is the plane's");
                Program.Near(got.Z, want.Z, 2e-6f, "vertex " + i + ": z is the plane's");
            }
        }

        private static void Crest()
        {
            // Heights over a crest at z = 10: rising to it, falling after it.
            MeshBuilder mesh = Ribbon(z => -0.004f * (z - 10f) * (z - 10f), 20f, 0.5f);
            SurfaceNormals.Fit(mesh);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Program.Near(mesh.Normals[i].Length, 1f, 1e-5f, "unit length");
                Program.Check(mesh.Normals[i].Y > 0.95f, "faces up");
                Program.Near(mesh.Normals[i].X, 0f, 1e-6f, "a line has no sideways lean");
            }

            for (int i = 2; i + 2 < mesh.VertexCount; i += 2)
            {
                // The two vertices of a point across the line have the same normal; the next point's differs by a small step.
                Program.Near(Vec3.Dot(mesh.Normals[i], mesh.Normals[i + 2]), 1f, 5e-4f, "the normal changes by a small step from one point to the next");
            }

            int rising = mesh.Positions.FindIndex(p => Math.Abs(p.Z - 5f) < 1e-4f), falling = mesh.Positions.FindIndex(p => Math.Abs(p.Z - 15f) < 1e-4f), top = mesh.Positions.FindIndex(p => Math.Abs(p.Z - 10f) < 1e-4f);
            // The slope at z is 0.008 * (10 - z): 0.04 at z = 5 and -0.04 at z = 15. A quad's diagonal makes the two sides not quite mirror images (a few per cent of the lean).
            Program.Check(mesh.Normals[rising].Z < -0.02f, "before the crest it leans back (the ground rises toward +Z)");
            Program.Check(mesh.Normals[falling].Z > 0.02f, "after the crest it leans forward");
            Program.Near(mesh.Normals[rising].Z, -0.04f, 2e-3f, "as the slope there says");
            Program.Near(mesh.Normals[falling].Z, 0.04f, 2e-3f, "and as far the other way");
            Program.Near(mesh.Normals[top].Z, 0f, 2e-3f, "on the crest it is straight up");
            // Where it is steepest the normal is that of the slope there (0.004 * 2 * 10 = 0.08 at z = 0, the end: a one-sided slope).
            Vec3 start = mesh.Normals[0];
            Program.Near(start.Z / start.Y, -0.0788f, 0.003f, "the first point has the slope of the first segment (about 8 %)");
        }

        private static void OnlyPaint()
        {
            MeshBuilder mesh = Ribbon(z => 0.05f * z, 10f);
            // A stud's dome sits on the line: its normals are its own and are left as they are. Its base ring is made of the same vertices as paint would be.
            int first = mesh.VertexCount;
            var dome = new List<Vec3> { new Vec3(0.3f, 0.5f, 5f), new Vec3(0.4f, 0.5f, 5.1f), new Vec3(0.2f, 0.5f, 5.1f), new Vec3(0.3f, 0.55f, 5.05f) };
            Vec3[] own = { MeshBuilder.Normalize(new Vec3(0.2f, 1f, -0.1f)), MeshBuilder.Normalize(new Vec3(-0.3f, 1f, 0.2f)), Up, MeshBuilder.Normalize(new Vec3(0f, 1f, 0.4f)) };
            for (int i = 0; i < dome.Count; i++)
                mesh.Vertex(dome[i], own[i], 0f, 0f);
            mesh.TriFacing(first, first + 1, first + 3, Up);
            mesh.TriFacing(first + 1, first + 2, first + 3, Up);
            // A wall standing on the line: its triangle is steeper than paint can be, and its vertices are paint's too (up) in this test: they get nothing from it.
            int wall = mesh.VertexCount;
            mesh.Vertex(new Vec3(0.5f, 0f, 2f), Up, 0f, 0f);
            mesh.Vertex(new Vec3(0.5f, 0.3f, 2f), Up, 0f, 0f);
            mesh.Vertex(new Vec3(0.5f, 0f, 2.4f), Up, 0f, 0f);
            mesh.Tri(wall, wall + 1, wall + 2);
            MeshBuilder before = Copy(mesh);
            SurfaceNormals.Fit(mesh);
            for (int i = 0; i < before.VertexCount; i++)
            {
                Program.Check(Same(mesh.Positions[i], before.Positions[i]) && mesh.U[i] == before.U[i] && mesh.V[i] == before.V[i], "vertex " + i + ": the place and the texture coordinates are not touched");
                if (i >= first)
                    Program.Check(Same(mesh.Normals[i], before.Normals[i]), "vertex " + i + ": a dome's or a wall's normal is left as it was");
                else
                    Program.Check(mesh.Normals[i].Z < -0.04f, "vertex " + i + ": the paint leans with its slope");
            }

            Program.Check(mesh.Indices.Count == before.Indices.Count && mesh.Indices.TrueForAll(i => i < mesh.VertexCount), "the triangles are the same count");
            for (int k = 0; k < mesh.Indices.Count; k++)
                Program.Check(mesh.Indices[k] == before.Indices[k], "triangle corner " + k + " is as it was");
            for (int t = 0; t + 2 < first * 3 && t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 c = MeshBuilder.Cross(mesh.Positions[mesh.Indices[t + 1]] - mesh.Positions[mesh.Indices[t]], mesh.Positions[mesh.Indices[t + 2]] - mesh.Positions[mesh.Indices[t]]);
                if (mesh.Indices[t] < first)
                    Program.Check(c.Y > 0f, "the paint's triangle " + t / 3 + " still faces up");
            }
        }

        private static void Weights()
        {
            // A quad wound clockwise seen from below (as a mesh built facing down would be) is the same paint: it leans the same way.
            var back = new MeshBuilder();
            int a = back.Vertex(new Vec3(0f, 0f, 0f), Up), b = back.Vertex(new Vec3(1f, 0.05f, 0f), Up), c = back.Vertex(new Vec3(1f, 0.05f, 1f), Up), d = back.Vertex(new Vec3(0f, 0f, 1f), Up);
            back.QuadFacing(a, b, c, d, new Vec3(0f, -1f, 0f));
            SurfaceNormals.Fit(back);
            foreach (Vec3 n in back.Normals)
                Program.Check(n.Y > 0.99f && n.X < -0.04f, "wound the other way it still faces up and leans with its slope: " + n);

            // One vertex of a large triangle (flat, 10 m) and of a small one (1 m, rising 0.5 m): the normal is the large one's by its area, nearly up.
            var weighted = new MeshBuilder();
            int o = weighted.Vertex(new Vec3(0f, 0f, 0f), Up), big1 = weighted.Vertex(new Vec3(10f, 0f, 0f), Up), big2 = weighted.Vertex(new Vec3(0f, 0f, 10f), Up);
            int small1 = weighted.Vertex(new Vec3(-1f, 0.5f, 0f), Up), small2 = weighted.Vertex(new Vec3(0f, 0f, -1f), Up);
            weighted.TriFacing(o, big1, big2, Up);
            weighted.TriFacing(o, small2, small1, Up);
            SurfaceNormals.Fit(weighted);
            Vec3 fromBig = Up, fromSmall = MeshBuilder.Normalize(MeshBuilder.Cross(new Vec3(0f, 0f, -1f), new Vec3(-1f, 0.5f, 0f)));
            if (fromSmall.Y < 0f)
                fromSmall = fromSmall * -1f;
            Program.Check(weighted.Normals[o].Y > 0.999f && Vec3.Dot(weighted.Normals[o], fromBig) > Vec3.Dot(weighted.Normals[o], fromSmall), "weighted by area: the small steep triangle has little say: " + weighted.Normals[o]);

            // A vertex that is in a dome's triangle (the others have normals of their own) is not plain paint, and the triangle that shares it with other vertices is not either: their normals are left as they are.
            var shared = new MeshBuilder();
            int corner = shared.Vertex(new Vec3(0f, 0.2f, 0f), Up);
            int dome1 = shared.Vertex(new Vec3(0.1f, 0.2f, 0.1f), MeshBuilder.Normalize(new Vec3(-0.3f, 1f, 0f)));
            int dome2 = shared.Vertex(new Vec3(-0.1f, 0.25f, 0.1f), MeshBuilder.Normalize(new Vec3(0f, 1f, 0.3f)));
            int edge1 = shared.Vertex(new Vec3(1f, 0.1f, 0f), Up), edge2 = shared.Vertex(new Vec3(0f, 0.1f, 1f), Up);
            shared.TriFacing(corner, dome1, dome2, Up);
            shared.TriFacing(corner, edge1, edge2, Up);
            SurfaceNormals.Fit(shared);
            Program.Check(Same(shared.Normals[corner], Up) && Same(shared.Normals[edge1], Up) && Same(shared.Normals[edge2], Up), "a triangle with a corner that is in a dome's triangle is not paint: its corners keep their normals");

            // A ribbon that is flat for 10 m and then rises: the vertices well before the rise are exactly up, whatever the last bit of their heights says.
            MeshBuilder ramp = Ribbon(z => z < 10f ? (z % 2f == 0f ? 0.4f : 0.4f + 3e-8f) : 0.4f + 0.05f * (z - 10f), 20f, 1f);
            SurfaceNormals.Fit(ramp);
            for (int i = 0; i < ramp.VertexCount; i++)
            {
                float z = ramp.Positions[i].Z;
                if (z <= 8f)
                    Program.Check(Same(ramp.Normals[i], Up), "z " + z + ": the flat end stays straight up");
                else if (z >= 12f)
                    Program.Check(ramp.Normals[i].Z < -0.04f, "z " + z + ": the rise leans");
            }
        }

        private static void NotPaint()
        {
            // The same mesh as a barrier's solid and as paint, on a grade: Bake leaves the solid's normals as they are, with the switch on.
            var solid = new MarkingPlan();
            var shape = new MarkingShape { Colour = MarkingColour.White, Solid = SolidColour.PoleLightGrey };
            RoadMarkings.Rect(shape.Mesh, -0.2f, 0f, 0.2f, 8f);
            solid.Shapes.Add(shape);
            solid.Bake(0.1f, null, null, true);
            Program.Check(solid.Parts.Count == 1 && solid.Parts[0].Solid.HasValue, "one solid part");
            foreach (Vec3 n in solid.Parts[0].Mesh.Normals)
                Program.Check(Same(n, Up), "a solid keeps its own normals");

            var paint = new MarkingPlan();
            var flat = new MarkingShape { Colour = MarkingColour.White };
            RoadMarkings.Rect(flat.Mesh, -0.2f, 0f, 0.2f, 8f);
            paint.Shapes.Add(flat);
            paint.Bake(0.1f, null, null, true);
            Program.Check(paint.Parts[0].Mesh.Normals.TrueForAll(n => n.Z < -0.09f), "the same mesh as paint leans on the grade");
        }

        private static void Degenerate()
        {
            // A point twice (a segment of no length), a step straight up (a segment with no ground-plane length), and a point that is not a number.
            var points = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0.1f, 1f), new Vec3(0f, 0.1f, 1f), new Vec3(0f, 0.6f, 1f), new Vec3(0f, 0.6f, 2f), new Vec3(0f, 0.7f, 3f) };
            var mesh = new MeshBuilder();
            RoadMarkings.Ribbon(mesh, points, 0.15f);
            SurfaceNormals.Fit(mesh);
            foreach (Vec3 n in mesh.Normals)
                Program.Check(n.IsFinite && Math.Abs(n.Length - 1f) < 1e-4f && n.Y > 0f, "finite, unit, facing up: " + n);
            var bad = new MeshBuilder();
            int a = bad.Vertex(new Vec3(0f, 0f, 0f), Up), b = bad.Vertex(new Vec3(1f, float.NaN, 0f), Up), c = bad.Vertex(new Vec3(0f, 0.2f, 1f), Up), d = bad.Vertex(new Vec3(1f, 0.2f, 1f), Up);
            bad.Tri(a, b, c);
            bad.Tri(b, d, c);
            SurfaceNormals.Fit(bad);
            foreach (Vec3 n in bad.Normals)
                Program.Check(n.IsFinite, "a point that is not a number gives no normal that is not one");
            var empty = new MeshBuilder();
            SurfaceNormals.Fit(empty);
            var lone = new MeshBuilder();
            lone.Vertex(new Vec3(0f, 1f, 0f), Up);
            SurfaceNormals.Fit(lone);
            Program.Check(empty.VertexCount == 0 && Same(lone.Normals[0], Up), "no triangles, nothing to do");
        }

        private static SignDef Def(string id)
        {
            SignDef def = SignCatalog.ById(id);
            Program.Check(def != null, id + " is in the catalogue");
            return def;
        }

        private static MarkingPlan Of(SignDef def, string spec, bool normals)
        {
            bool was = SurfaceNormals.Enabled;
            SurfaceNormals.Enabled = normals;
            try
            {
                return MarkingTemplates.PlanOf(def, spec);
            }
            finally
            {
                SurfaceNormals.Enabled = was;
            }
        }

        private static void PlanGrade()
        {
            SignDef def = Def("mk.arrow.straight");
            string spec = MarkingTemplates.WithGrade(def.Template, def.Template.Default().ToString(), 0.1f);
            MarkingPlan on = Of(def, spec, true), off = Of(def, spec, false);
            Vec3 want = MeshBuilder.Normalize(new Vec3(0f, 1f, -0.1f));
            int tilted = 0;
            foreach (MarkingPart part in on.Parts)
            {
                foreach (Vec3 n in part.Mesh.Normals)
                {
                    Program.Near(n.Z, want.Z, 2e-5f, "an arrow on a grade of 10 % leans back by it");
                    Program.Near(n.X, 0f, 2e-5f, "and not sideways");
                    tilted++;
                }
            }

            Program.Check(tilted > 0, "the arrow has paint: " + tilted);
            foreach (MarkingPart part in off.Parts)
            {
                foreach (Vec3 n in part.Mesh.Normals)
                    Program.Check(Same(n, Up), "with the switch off the normals are straight up as they were");
            }

            // The shape is the same with the switch on or off.
            Program.Check(on.Parts.Count == off.Parts.Count && on.Parts[0].Mesh.VertexCount == off.Parts[0].Mesh.VertexCount, "the same paint");
            for (int i = 0; i < on.Parts[0].Mesh.VertexCount; i++)
                Program.Check(Same(on.Parts[0].Mesh.Positions[i], off.Parts[0].Mesh.Positions[i]), "the same places");
        }

        private static void PlanGrid()
        {
            SignDef def = Def("mk.arrow.straight");
            string spec = def.Template.Default().ToString();
            MarkingPlan flat = MarkingTemplates.PlanOf(def, spec);
            GroundGrid.Count(flat.MinX, flat.MaxX, flat.MinZ, flat.MaxZ, out int nx, out int nz);
            var grid = new GroundGrid { X0 = flat.MinX, X1 = flat.MaxX, Z0 = flat.MinZ, Z1 = flat.MaxZ, Nx = nx, Nz = nz, Heights = new float[nx * nz] };
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    GroundGrid.PointOf(grid.X0, grid.X1, grid.Z0, grid.Z1, nx, nz, i, j, out float x, out float z);
                    // A road that falls to its right by 5 % and rises along it by 2 %.
                    grid.Heights[j * nx + i] = -0.05f * x + 0.02f * z;
                }
            }

            MarkingPlan on = Of(def, MarkingTemplates.WithGround(def.Template, spec, grid), true);
            Vec3 want = MeshBuilder.Normalize(new Vec3(0.05f, 1f, -0.02f));
            int count = 0;
            foreach (MarkingPart part in on.Parts)
            {
                foreach (Vec3 n in part.Mesh.Normals)
                {
                    // The heights are kept in whole centimetres (GroundGrid), so the plane is the grid's to a centimetre over a metre or two.
                    Program.Near(n.X, want.X, 0.012f, "across: the road falls to the right, so the normal leans to the left");
                    Program.Near(n.Z, want.Z, 0.008f, "along: the road rises, so the normal leans back");
                    Program.Check(n.X > 0.03f && n.Z < -0.01f, "each sign and about the size of the slope");
                    count++;
                }
            }

            Program.Check(count > 0, "the arrow has paint");
        }

        private static void PlanRun()
        {
            SignDef def = Def("mk.line.centre");
            var points = new List<Vec3>();
            for (float z = 0f; z <= 24f + 1e-3f; z += 2f)
                points.Add(new Vec3(0f, -0.004f * (z - 12f) * (z - 12f), z));
            string spec = MarkingTemplates.WithRun(def.Template, def.Template.Default().ToString(), RunPath.Encode(points, out _), 0f);
            MarkingPlan on = Of(def, spec, true), off = Of(def, spec, false);
            Program.Check(!ReferenceEquals(on, off), "a plan for each state of the switch");
            float lean = 0f;
            foreach (MarkingPart part in on.Parts)
            {
                foreach (Vec3 n in part.Mesh.Normals)
                    lean = Math.Max(lean, Math.Abs(n.Z));
            }

            Program.Check(lean > 0.05f, "the line's paint leans on the slopes of the crest: " + lean);
            foreach (MarkingPart part in off.Parts)
            {
                foreach (Vec3 n in part.Mesh.Normals)
                    Program.Check(Same(n, Up), "with the switch off the line's normals are straight up");
            }
        }

        private static void PlanBent()
        {
            var arc = new List<Vec3>();
            for (float s = 0f; s <= 20f + 1e-4f; s += 0.5f)
            {
                double a = s / 30.0;
                arc.Add(new Vec3(30f - 30f * (float)Math.Cos(a), 0f, 30f * (float)Math.Sin(a)));
            }

            MarkingBend bend = MarkingBend.Decode(MarkingBend.Encode(arc, out _));
            Program.Check(bend != null, "the bend is made");
            var plan = new MarkingPlan();
            var shape = new MarkingShape { Colour = MarkingColour.White };
            RoadMarkings.Rect(shape.Mesh, -0.1f, 0f, 0.1f, 12f);
            plan.Shapes.Add(shape);
            plan.Bake(0.08f, null, bend, true);
            Program.Check(plan.Parts.Count == 1 && plan.Parts[0].Mesh.VertexCount > 4, "the paint is cut along the bend");
            // The heights are the straight line's, 8 % of the way along it, and the way along it is now the bend: the lean is 8 % in the direction the paint runs there, which turns with the bend.
            float widest = 0f;
            foreach (Vec3 n in plan.Parts[0].Mesh.Normals)
            {
                Program.Check(n.IsFinite && Math.Abs(n.Length - 1f) < 1e-4f, "unit length");
                Program.Near((float)Math.Sqrt(n.X * n.X + n.Z * n.Z), 0.0797f, 4e-3f, "leans back by the grade");
                widest = Math.Max(widest, Math.Abs(n.X));
            }

            Program.Check(widest > 0.01f, "the lean turns with the bend: some of it is sideways (it was taken after the bend): " + widest);

            // The positions are those of the bent paint: Fit comes after the bend.
            Vec3 end = plan.Parts[0].Mesh.Positions[plan.Parts[0].Mesh.VertexCount - 1];
            Program.Check(Math.Abs(end.X) > 1f, "the paint is bent away from the straight line: " + end);
            var without = new MarkingPlan();
            var shape2 = new MarkingShape { Colour = MarkingColour.White };
            RoadMarkings.Rect(shape2.Mesh, -0.1f, 0f, 0.1f, 12f);
            without.Shapes.Add(shape2);
            without.Bake(0.08f, null, bend, false);
            foreach (Vec3 n in without.Parts[0].Mesh.Normals)
                Program.Check(Same(n, Up), "baked without the normals: straight up");
        }

        /// <summary>
        /// Every marking of the catalogue on a grade of 8 % (a run on a path that rises by it): a vertex of paint that is still straight up must not be on a triangle that leans (a path that kept the
        /// flat normals); the paint of most of the catalogue does lean; paint that stands up (a barrier's solids, the elevation marking's sheet) is left to its own normals.
        /// </summary>
        private static void EveryMarking()
        {
            Program.Check(LightingPrint.Fonts(), "the fonts are found");
            MarkingLetters.Release();
            int leaning = 0, markings = 0;
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                string spec = t.Default().ToString();
                if (MarkingTemplates.IsRun(t))
                {
                    var points = new List<Vec3>();
                    for (float z = 0f; z <= 24f + 1e-3f; z += 2f)
                        points.Add(new Vec3(0f, 0.08f * z, z));
                    spec = MarkingTemplates.WithRun(t, spec, RunPath.Encode(points, out _), 0f);
                }
                else if (t.FieldOf("g") != null)
                {
                    spec = MarkingTemplates.WithGrade(t, spec, 0.08f);
                }

                MarkingPlan plan = Of(SignCatalog.ById(t.Id), spec, true);
                if (plan == null || plan.Parts.Count == 0)
                    continue;
                markings++;
                bool any = false;
                foreach (MarkingPart part in plan.Parts)
                {
                    if (part.Upright || part.Solid.HasValue)
                        continue;
                    MeshBuilder m = part.Mesh;
                    var onLean = new bool[m.VertexCount];
                    for (int k = 0; k + 2 < m.Indices.Count; k += 3)
                    {
                        int a = m.Indices[k], b = m.Indices[k + 1], c = m.Indices[k + 2];
                        Vec3 cross = MeshBuilder.Cross(m.Positions[b] - m.Positions[a], m.Positions[c] - m.Positions[a]);
                        float length = cross.Length;
                        if (length > 1e-12f && Math.Abs(cross.Y) / length < 0.9995f && Math.Abs(cross.Y) / length > 0.5f)
                            onLean[a] = onLean[b] = onLean[c] = true;
                    }

                    for (int i = 0; i < m.VertexCount; i++)
                    {
                        bool up = Same(m.Normals[i], Up);
                        Program.Check(!(up && onLean[i]), t.Id + ": vertex " + i + " is on a triangle that leans and is straight up");
                        if (!up)
                            any = true;
                    }
                }

                if (any)
                    leaning++;
            }

            Program.Check(markings >= 60 && leaning >= 55, "most markings lean on the grade: " + leaning + " of " + markings);
        }

        private static void Keys()
        {
            SignDef def = Def("mk.line.edge");
            string spec = def.Template.Default().ToString();
            MarkingPlan on1 = Of(def, spec, true), off1 = Of(def, spec, false), on2 = Of(def, spec, true), off2 = Of(def, spec, false);
            Program.Check(ReferenceEquals(on1, on2) && ReferenceEquals(off1, off2) && !ReferenceEquals(on1, off1), "each state has its plan, kept");
            bool was = SurfaceNormals.Enabled;
            try
            {
                SurfaceNormals.Enabled = false;
                Program.Check(MarkingTemplates.TryKept(def, spec, out MarkingPlan kept) && ReferenceEquals(kept, off1), "TryKept looks in the plans of the state the switch is in (off)");
                SurfaceNormals.Enabled = true;
                Program.Check(MarkingTemplates.TryKept(def, spec, out kept) && ReferenceEquals(kept, on1), "and on");
            }
            finally
            {
                SurfaceNormals.Enabled = was;
            }

            Program.Check(MarkingTemplates.PlanVersion(true, 0) != MarkingTemplates.PlanVersion(false, 0) && MarkingTemplates.PlanVersion(true, 3) != MarkingTemplates.PlanVersion(false, 0)
                && MarkingTemplates.PlanVersion(true, 1) != MarkingTemplates.PlanVersion(true, 2), "the version differs by the switch and by the level, whatever the level");
        }

        private static void FlatGolden()
        {
            string golden = FontLetterTests.RepoFile("Tests", "CDLC.TS.Tests", "Golden", "marking-flat-0.12.82.txt");
            Program.Check(golden != null, "the golden file is found");
            if (golden == null)
                return;
            string[] want = File.ReadAllLines(golden, new System.Text.UTF8Encoding(false));
            List<string> now = MarkingFlatPrint.Lines();
            Program.Check(now.Count == want.Length && now.Count == 419, "the same plans: " + now.Count + " against " + want.Length);
            int differ = 0;
            for (int i = 0; i < Math.Min(now.Count, want.Length); i++)
            {
                if (now[i] == want[i])
                    continue;
                differ++;
                if (differ <= 5)
                    Program.Check(false, "not as 0.12.82 built it: " + now[i]);
            }

            Program.Check(differ == 0, differ + " plan(s) of paint on flat ground differ from 0.12.82's");
        }
    }
}
