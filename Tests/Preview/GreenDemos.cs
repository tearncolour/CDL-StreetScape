using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// Pictures of the road greening (0.12.49, Barriers.Greening, Barriers.Viaduct) from made-up lines (no game): each template's bodies as the raster draws a barrier, the grates' and gratings' holes cut
    /// out for real (each solid texel of the top a little quad), and where the game's plants go a magenta marker (preview only: a post 0.6 m high and a ring as wide as the room asked for). Usage:
    /// Preview.exe root out --green-demos; writes out/green/&lt;name&gt;.&lt;view&gt;.png. Geometry only; not the game, and without the game's plants.
    /// </summary>
    internal static class GreenDemos
    {
        private static readonly Vec3 Iso = new Vec3(-0.75f, -0.42f, 0.52f), Top = new Vec3(0.001f, -1f, 0.02f), Side = new Vec3(-1f, -0.12f, 0.05f), Outside = new Vec3(0.85f, -0.25f, 0.45f),
            Below = new Vec3(0.55f, 0.55f, 0.6f), Along = new Vec3(-0.25f, -0.35f, 0.9f);

        private static List<Vec3> Straight(float length, float grade = 0f) => new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, length * grade, length) };

        /// <summary>An arc from the origin heading +Z of radius r to the right (+X) or the left, length long, rising by grade.</summary>
        private static List<Vec3> Arc(float r, float length, bool right, float grade = 0f)
        {
            var points = new List<Vec3>();
            for (float s = 0f; s <= length + 1e-4f; s += 2f)
            {
                double a = s / r;
                float x = r - r * (float)Math.Cos(a);
                points.Add(new Vec3(right ? x : -x, s * grade, r * (float)Math.Sin(a)));
            }

            return points;
        }

        private static MarkingPlan PlanOf(string id, string settings, List<Vec3> line)
        {
            GuideTemplate t = MarkingTemplates.ById(id);
            GuideSpec spec = t.Default();
            foreach (string pair in settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                spec = spec.With(pair.Substring(0, pair.IndexOf('=')), pair.Substring(pair.IndexOf('=') + 1));
            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), RunPath.Encode(line, out _), 0f));
        }

        public static int Run(string output)
        {
            Directory.CreateDirectory(output);
            var scenes = new List<(string Name, string Id, string Settings, List<Vec3> Line, Vec3[] Views)>
            {
                ("pit-iron", MarkingTemplates.TreePitId, "where=m", Straight(12f), new[] { Iso, Top }),
                ("pit-iron-close", MarkingTemplates.TreePitId, "where=m", Straight(1f), new[] { Iso, Top }),
                ("pit-resin-guard", MarkingTemplates.TreePitId, "where=m;cover=resin;guard=1", Straight(1f), new[] { Iso, Side }),
                ("pit-frp", MarkingTemplates.TreePitId, "where=m;cover=frp;size=150", Straight(1f), new[] { Iso, Top }),
                ("pit-raised", MarkingTemplates.TreePitId, "where=m;kerb=15;guard=1", Straight(1f), new[] { Iso }),
                ("pit-slope", MarkingTemplates.TreePitId, "where=m;guard=1", Straight(13f, 0.05f), new[] { Side, Iso }),
                ("strip", MarkingTemplates.TreePitId, "where=m;size=s200", Straight(20f), new[] { Iso, Top }),
                ("belt-hedge", MarkingTemplates.GreenBeltId, "width=200", Straight(20f), new[] { Iso, Top }),
                ("belt-hedge-trees", MarkingTemplates.GreenBeltId, "width=250;tree=plane;treegap=8", Straight(24f), new[] { Iso, Top }),
                ("belt-balls", MarkingTemplates.GreenBeltId, "width=300;under=shrubs;tree=linden;treegap=6", Straight(18f), new[] { Top }),
                ("belt-flowers-low", MarkingTemplates.GreenBeltId, "width=200;under=flowers;gap=2;edge=low", Straight(12f), new[] { Iso }),
                ("belt-curve", MarkingTemplates.GreenBeltId, "width=150", Arc(20f, 24f, true), new[] { Top, Iso }),
                ("belt-end", MarkingTemplates.GreenBeltId, "width=200;edge=k20", Straight(6f), new[] { Along }),
                ("viaduct-hung", MarkingTemplates.ViaductPlanterId, "", Straight(6f), new[] { Outside, Below, Along, Side }),
                ("viaduct-saddle", MarkingTemplates.ViaductPlanterId, "mount=saddle", Straight(5f), new[] { Outside, Along, Iso }),
                ("viaduct-top", MarkingTemplates.ViaductPlanterId, "mount=top;box=green;length=150", Straight(5f), new[] { Outside, Along }),
                ("viaduct-trough", MarkingTemplates.ViaductPlanterId, "mount=trough;plants=mixed", Straight(6f), new[] { Outside, Along, Below }),
                ("viaduct-median", MarkingTemplates.ViaductPlanterId, "mount=saddle;where=m;box=dark", Straight(5f), new[] { Along, Iso }),
                ("viaduct-curve", MarkingTemplates.ViaductPlanterId, "", Arc(30f, 16f, false, 0.04f), new[] { Outside, Top }),
                ("viaduct-curve-in", MarkingTemplates.ViaductPlanterId, "length=200", Arc(20f, 12f, false), new[] { Top, Outside }),
                ("viaduct-saddle-curve", MarkingTemplates.ViaductPlanterId, "mount=saddle;length=200", Arc(20f, 12f, true), new[] { Top, Along }),
                ("pit-curve", MarkingTemplates.TreePitId, "where=r;guard=1", Arc(25f, 20f, false, 0.03f), new[] { Iso, Top }),
                ("bowl-stone", MarkingTemplates.FlowerBowlId, "where=m", Straight(1f), new[] { Iso, Side }),
                ("bowl-round", MarkingTemplates.FlowerBowlId, "where=m;style=round;colour=white;size=120", Straight(1f), new[] { Iso })
            };
            foreach (var (name, id, settings, line, views) in scenes)
            {
                MarkingPlan plan = PlanOf(id, settings, line);
                var bodies = new List<(MeshBuilder Mesh, byte[] Rgb)>();
                foreach (MarkingPart part in plan.Parts)
                {
                    MeshBuilder mesh = DetailSurfaces.HasHoles(part.Detail) ? CutOut(part) : part.Mesh;
                    bodies.Add((mesh, Raster.SolidRgb(part.Solid ?? SolidColour.White)));
                }

                var markers = new MeshBuilder();
                foreach (PropAnchor prop in plan.BakedProps)
                {
                    Solids.Tube(markers, prop.Local, prop.Local + new Vec3(0f, 0.6f, 0f), 0.015f, 0.015f, 6, false, true);
                    if (prop.Kind != GreenPlants.Hedge)
                    {
                        Solids.Band(markers, prop.Local + new Vec3(0f, 0.002f, 0f), prop.Local + new Vec3(0f, 0.012f, 0f), Math.Max(0.01f, prop.Room * 0.5f - 0.015f), prop.Room * 0.5f, 24);
                        continue;
                    }

                    // A hedge piece's footprint (GreenPlants.HedgeLength x HedgeWidth along its heading), at the top of the soil it is sunk into.
                    var ahead = new Vec3((float)Math.Sin(prop.Yaw), 0f, (float)Math.Cos(prop.Yaw));
                    Vec3 right = ahead.RightXZ(), lift = new Vec3(0f, 0.09f, 0f);
                    var corners = new List<Vec3>();
                    foreach ((float x, float z) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                        corners.Add(prop.Local + lift + right * (x * GreenPlants.HedgeWidth * 0.5f) + ahead * (z * GreenPlants.HedgeLength * 0.5f));
                    for (int c = 0; c < 4; c++)
                        Solids.Tube(markers, corners[c], corners[(c + 1) % 4], 0.012f, 0.012f, 4, false, false);
                }

                bodies.Add((markers, new byte[] { 230, 40, 200 }));
                for (int v = 0; v < views.Length; v++)
                {
                    byte[] bottomFirst = Raster.RenderBodies(bodies, views[v], 900, out int w, out int h);
                    var rows = new byte[bottomFirst.Length];
                    for (int y = 0; y < h; y++)
                        Array.Copy(bottomFirst, (h - 1 - y) * w * 4, rows, y * w * 4, w * 4);
                    File.WriteAllBytes(Path.Combine(output, $"{name}.{v}.png"), Png.Encode(rows, w, h, false));
                }

                int vertices = 0;
                foreach (MarkingPart part in plan.Parts)
                    vertices += part.Mesh.VertexCount;
                Console.WriteLine($"{name}: {plan.Parts.Count} parts, {vertices} vertices, {plan.TriangleCount} triangles, {plan.BakedProps.Count} plants ({string.Join(", ", Kinds(plan))})");
            }

            return 0;
        }

        private static IEnumerable<string> Kinds(MarkingPlan plan)
        {
            var counts = new SortedDictionary<string, int>();
            foreach (PropAnchor p in plan.BakedProps)
                counts[p.Kind] = counts.TryGetValue(p.Kind, out int n) ? n + 1 : 1;
            foreach (KeyValuePair<string, int> pair in counts)
                yield return pair.Value + " " + pair.Key;
        }

        /// <summary>
        /// A part with holes as the cut-out material draws it: each face that looks up made of the little quads of its solid texels (the detail surface's alpha at the face's coordinates, which the plan has
        /// made coordinates of the tile), the other faces as they are.
        /// </summary>
        private static MeshBuilder CutOut(MarkingPart part)
        {
            DetailSurface s = DetailSurfaces.Make(part.Detail);
            MeshBuilder mesh = part.Mesh, cut = new MeshBuilder();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int ia = mesh.Indices[t], ib = mesh.Indices[t + 1], ic = mesh.Indices[t + 2];
                Vec3 a = mesh.Positions[ia], b = mesh.Positions[ib], c = mesh.Positions[ic];
                if (mesh.Normals[ia].Y < 0.9f)
                {
                    int first = cut.VertexCount;
                    cut.Vertex(a, mesh.Normals[ia]);
                    cut.Vertex(b, mesh.Normals[ib]);
                    cut.Vertex(c, mesh.Normals[ic]);
                    cut.Tri(first, first + 1, first + 2);
                    continue;
                }

                float ua = mesh.U[ia] * s.Width, va = mesh.V[ia] * s.Height, ub = mesh.U[ib] * s.Width, vb = mesh.V[ib] * s.Height, uc = mesh.U[ic] * s.Width, vc = mesh.V[ic] * s.Height;
                float area = (ub - ua) * (vc - va) - (uc - ua) * (vb - va);
                if (Math.Abs(area) < 1e-6f)
                    continue;
                int i0 = (int)Math.Floor(Math.Min(ua, Math.Min(ub, uc))), i1 = (int)Math.Ceiling(Math.Max(ua, Math.Max(ub, uc)));
                int j0 = (int)Math.Floor(Math.Min(va, Math.Min(vb, vc))), j1 = (int)Math.Ceiling(Math.Max(va, Math.Max(vb, vc)));
                for (int j = j0; j < j1; j++)
                {
                    for (int i = i0; i < i1; i++)
                    {
                        float pu = i + 0.5f, pv = j + 0.5f;
                        float w0 = ((ub - pu) * (vc - pv) - (uc - pu) * (vb - pv)) / area, w1 = ((uc - pu) * (va - pv) - (ua - pu) * (vc - pv)) / area, w2 = 1f - w0 - w1;
                        if (w0 < 0f || w1 < 0f || w2 < 0f)
                            continue;
                        int ti = ((i % s.Width) + s.Width) % s.Width, tj = ((j % s.Height) + s.Height) % s.Height;
                        if (s.Alpha[tj * s.Width + ti] < 0.5f)
                            continue;
                        // The texel's corners where the face's coordinates put them.
                        Vec3 P(float du, float dv)
                        {
                            float qu = i + du, qv = j + dv;
                            float x0 = ((ub - qu) * (vc - qv) - (uc - qu) * (vb - qv)) / area, x1 = ((uc - qu) * (va - qv) - (ua - qu) * (vc - qv)) / area;
                            return a * x0 + b * x1 + c * (1f - x0 - x1);
                        }

                        int k = cut.VertexCount;
                        Vec3 n = mesh.Normals[ia];
                        cut.Vertex(P(0f, 0f), n);
                        cut.Vertex(P(1f, 0f), n);
                        cut.Vertex(P(1f, 1f), n);
                        cut.Vertex(P(0f, 1f), n);
                        cut.QuadFacing(k, k + 1, k + 2, k + 3, n);
                    }
                }
            }

            return cut;
        }
    }
}
