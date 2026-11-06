using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The paint of every marking of the catalogue on flat ground as 0.12.82 built it (main d530646): at its default settings, on a flat grid of heights of 0.4 m, and, for the runs, along a straight and
    /// a curved path at one height: every part's colour, lift, flags and mesh buffers to the last bit. The golden file was made by the code of 0.12.82 in a worktree of its own; the paint taking the normals
    /// of the ground (0.12.83) must not move one bit of paint that lies flat.
    /// </summary>
    internal static class MarkingFlatPrint
    {
        private sealed class Hash
        {
            public ulong Value = 14695981039346656037UL;

            public void Add(int v)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Value ^= (byte)(v >> (8 * i));
                        Value *= 1099511628211UL;
                    }
                }
            }

            public void Add(float v) => Add(BitConverter.SingleToInt32Bits(v));

            public void Add(Vec3 v)
            {
                Add(v.X);
                Add(v.Y);
                Add(v.Z);
            }
        }

        public static ulong Of(MarkingPlan plan)
        {
            var h = new Hash();
            if (plan == null)
            {
                h.Add(-1);
                return h.Value;
            }

            h.Add(plan.Parts.Count);
            foreach (MarkingPart part in plan.Parts)
            {
                h.Add((int)part.Colour);
                h.Add(part.Lift);
                h.Add(part.Upright ? 1 : 0);
                h.Add(part.Solid.HasValue ? 1 + (int)part.Solid.Value : 0);
                h.Add((int)part.Detail);
                MeshBuilder mesh = part.Mesh;
                h.Add(mesh.VertexCount);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    h.Add(mesh.Positions[i]);
                    h.Add(mesh.Normals[i]);
                    h.Add(mesh.U[i]);
                    h.Add(mesh.V[i]);
                }

                h.Add(mesh.Indices.Count);
                foreach (int index in mesh.Indices)
                    h.Add(index);
            }

            return h.Value;
        }

        private static string Straight(float y)
        {
            var points = new List<Vec3>();
            for (float z = 0f; z <= 24f + 1e-3f; z += 2f)
                points.Add(new Vec3(0f, y, z));
            return RunPath.Encode(points, out _);
        }

        private static string Arc(float y)
        {
            var points = new List<Vec3>();
            for (int i = 0; i <= 16; i++)
            {
                double a = i * Math.PI / 32.0;
                points.Add(new Vec3((float)(30.0 * (1.0 - Math.Cos(a))), y, (float)(30.0 * Math.Sin(a))));
            }

            return RunPath.Encode(points, out _);
        }

        /// <summary>"id|variant|number" for every marking and every flat way of laying it.</summary>
        public static List<string> Lines()
        {
            var lines = new List<string>();
            if (!LightingPrint.Fonts())
                return lines;
            // The words of a marking are read from the fonts the first time one is drawn, and not again until this: a test that drew one before the fonts were found left none.
            MarkingLetters.Release();
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                string spec = t.Default().ToString();
                if (MarkingTemplates.IsRun(t))
                {
                    foreach (float y in new[] { 0f, 3.5f })
                    {
                        lines.Add(t.Id + "|straight " + y.ToString("0.0") + "|" + Of(MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec, Straight(y), 0f))).ToString("x16"));
                        lines.Add(t.Id + "|arc " + y.ToString("0.0") + "|" + Of(MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec, Arc(y), 0f))).ToString("x16"));
                    }

                    continue;
                }

                MarkingPlan plan = MarkingTemplates.PlanOf(t, spec);
                lines.Add(t.Id + "|default|" + Of(plan).ToString("x16"));
                if (plan == null || t.FieldOf(GroundGrid.Key) == null || plan.Parts.Count == 0)
                    continue;
                GroundGrid.Count(plan.MinX, plan.MaxX, plan.MinZ, plan.MaxZ, out int nx, out int nz);
                var grid = new GroundGrid { X0 = plan.MinX, X1 = plan.MaxX, Z0 = plan.MinZ, Z1 = plan.MaxZ, Nx = nx, Nz = nz, Heights = new float[nx * nz] };
                for (int k = 0; k < grid.Heights.Length; k++)
                    grid.Heights[k] = 0.4f;
                lines.Add(t.Id + "|grid 0.4|" + Of(MarkingTemplates.PlanOf(t, MarkingTemplates.WithGround(t, spec, grid))).ToString("x16"));
            }

            return lines;
        }

        public static int Write(string file)
        {
            List<string> lines = Lines();
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            Console.WriteLine(lines.Count + " plans written to " + file);
            return 0;
        }
    }
}
