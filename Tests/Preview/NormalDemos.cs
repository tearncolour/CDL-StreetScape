using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// A picture of what the paint's normals do (0.12.83, SurfaceNormals), from a made-up road (no game): a road over a crest (grades of up to 24 %) with a camber of 6 %, seen from above with the traffic running to the right, lit by
    /// a low sun from behind it (Lambert: no shadows, no glossiness). Top: the road alone. Middle: a dashed line and two arrows over it with the normals they had before (straight up). Bottom: the same
    /// with the normals of the ground they lie on. Where the paint's brightness differs from the road's beside it the paint is lit as another slope than it lies on. Usage: Preview.exe root out --marking-normals.
    /// Geometry only; not the game's lighting.
    /// </summary>
    internal static class NormalDemos
    {
        private const float Length = 40f, Half = 4.5f, Scale = 22f;

        private static float Height(float x, float z) => 0.5f - 0.006f * (z - 20f) * (z - 20f) - 0.06f * x;

        private static Vec3 Normal(float x, float z) => MeshBuilder.Normalize(new Vec3(0.06f, 1f, 0.012f * (z - 20f)));

        private static readonly Vec3 Sun = MeshBuilder.Normalize(new Vec3(0f, 0.34f, -0.94f));

        private static float Light(Vec3 n) => 0.18f + 0.82f * Math.Max(0f, Vec3.Dot(MeshBuilder.Normalize(n), Sun));

        private sealed class Canvas
        {
            public readonly int W, H;
            public readonly byte[] Rgba;
            public readonly float[] Depth;

            public Canvas(int w, int h)
            {
                W = w;
                H = h;
                Rgba = new byte[w * h * 4];
                Depth = new float[w * h];
                for (int i = 0; i < Depth.Length; i++)
                    Depth[i] = float.MinValue;
                for (int i = 0; i < w * h; i++)
                    Rgba[i * 4 + 3] = 255;
            }

            private (float X, float Y) At(Vec3 p) => ((p.Z) * Scale, (p.X + Half) * Scale);

            public void Triangle(Vec3 a, Vec3 b, Vec3 c, Vec3 na, Vec3 nb, Vec3 nc, float r, float g, float bl)
            {
                var (ax, ay) = At(a);
                var (bx, by) = At(b);
                var (cx, cy) = At(c);
                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), y1 = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                float det = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
                if (Math.Abs(det) < 1e-9f)
                    return;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float wa = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / det, wb = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / det, wc = 1f - wa - wb;
                        if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f)
                            continue;
                        float depth = a.Y * wa + b.Y * wb + c.Y * wc;
                        int i = y * W + x;
                        if (depth < Depth[i])
                            continue;
                        Depth[i] = depth;
                        Vec3 n = na * wa + nb * wb + nc * wc;
                        float l = Light(n);
                        Rgba[i * 4] = (byte)Math.Max(0, Math.Min(255, (int)(255f * r * l)));
                        Rgba[i * 4 + 1] = (byte)Math.Max(0, Math.Min(255, (int)(255f * g * l)));
                        Rgba[i * 4 + 2] = (byte)Math.Max(0, Math.Min(255, (int)(255f * bl * l)));
                    }
                }
            }
        }

        private static void Ground(Canvas canvas)
        {
            for (float z = 0f; z < Length; z += 0.5f)
            {
                for (float x = -Half; x < Half; x += 0.5f)
                {
                    Vec3 p00 = new Vec3(x, Height(x, z), z), p10 = new Vec3(x + 0.5f, Height(x + 0.5f, z), z), p01 = new Vec3(x, Height(x, z + 0.5f), z + 0.5f), p11 = new Vec3(x + 0.5f, Height(x + 0.5f, z + 0.5f), z + 0.5f);
                    Vec3 n00 = Normal(x, z), n10 = Normal(x + 0.5f, z), n01 = Normal(x, z + 0.5f), n11 = Normal(x + 0.5f, z + 0.5f);
                    canvas.Triangle(p00, p01, p11, n00, n01, n11, 0.3f, 0.3f, 0.32f);
                    canvas.Triangle(p00, p11, p10, n00, n11, n10, 0.3f, 0.3f, 0.32f);
                }
            }
        }

        private static void Paint(Canvas canvas, bool normals)
        {
            bool was = SurfaceNormals.Enabled;
            SurfaceNormals.Enabled = normals;
            try
            {
                // A dashed centre line along the crest and the camber: the points of its path are on the ground.
                SignDef line = SignCatalog.ById("mk.line.centre");
                var points = new List<Vec3>();
                for (float z = 0f; z <= Length + 1e-3f; z += 2f)
                    points.Add(new Vec3(0f, Height(0f, z), z));
                string spec = MarkingTemplates.WithRun(line.Template, line.Template.Default().ToString(), RunPath.Encode(points, out _), 0f);
                Draw(canvas, MarkingTemplates.PlanOf(line, spec), Vec3.Zero, 0f, 0.03f);

                // Two arrows, each on the heights of the ground under it (the grid of a thing put down with one click).
                SignDef arrow = SignCatalog.ById("mk.arrow.straight");
                foreach (float z0 in new[] { 5f, 28f })
                {
                    Vec3 at = new Vec3(-2.2f, Height(-2.2f, z0), z0);
                    MarkingPlan flat = MarkingTemplates.PlanOf(arrow, arrow.Template.Default().ToString());
                    GroundGrid.Count(flat.MinX, flat.MaxX, flat.MinZ, flat.MaxZ, out int nx, out int nz);
                    var grid = new GroundGrid { X0 = flat.MinX, X1 = flat.MaxX, Z0 = flat.MinZ, Z1 = flat.MaxZ, Nx = nx, Nz = nz, Heights = new float[nx * nz] };
                    for (int j = 0; j < nz; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            GroundGrid.PointOf(grid.X0, grid.X1, grid.Z0, grid.Z1, nx, nz, i, j, out float x, out float z);
                            grid.Heights[j * nx + i] = Height(at.X + x, at.Z + z) - at.Y;
                        }
                    }

                    Draw(canvas, MarkingTemplates.PlanOf(arrow, MarkingTemplates.WithGround(arrow.Template, arrow.Template.Default().ToString(), grid)), at, 0f, 0.03f);
                }
            }
            finally
            {
                SurfaceNormals.Enabled = was;
            }
        }

        private static void Draw(Canvas canvas, MarkingPlan plan, Vec3 at, float yaw, float lift)
        {
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid.HasValue || part.Upright)
                    continue;
                var mesh = new MeshBuilder();
                mesh.Append(part.Mesh, at + new Vec3(0f, lift, 0f), yaw);
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                    bool yellow = part.Colour == MarkingColour.Yellow;
                    canvas.Triangle(mesh.Positions[a], mesh.Positions[b], mesh.Positions[c], mesh.Normals[a], mesh.Normals[b], mesh.Normals[c], 1f, yellow ? 0.8f : 1f, yellow ? 0.1f : 0.96f);
                }
            }
        }

        public static int Run(string output)
        {
            Directory.CreateDirectory(output);
            int w = (int)(Length * Scale), h = (int)(2f * Half * Scale);
            Canvas road = new Canvas(w, h), before = new Canvas(w, h), after = new Canvas(w, h);
            Ground(road);
            Ground(before);
            Paint(before, false);
            Ground(after);
            Paint(after, true);
            int gap = 6, total = h * 3 + gap * 2;
            var sheet = new byte[w * total * 4];
            for (int i = 0; i < sheet.Length; i += 4)
            {
                sheet[i] = sheet[i + 1] = sheet[i + 2] = 20;
                sheet[i + 3] = 255;
            }

            Canvas[] panels = { road, before, after };
            for (int p = 0; p < 3; p++)
                Array.Copy(panels[p].Rgba, 0, sheet, (p * (h + gap)) * w * 4, w * h * 4);
            File.WriteAllBytes(Path.Combine(output, "marking-normals.png"), Png.Encode(sheet, w, total, false));
            Console.WriteLine("marking-normals.png written: the road, the paint with straight-up normals, the paint with the ground's");
            return 0;
        }
    }
}
