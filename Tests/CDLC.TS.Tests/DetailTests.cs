using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The baked detail of 0.12.32 (DetailSurfaces, DetailUv, MeshTangents, the solids' texture coordinates and materials).</summary>
    internal static class DetailTests
    {
        public static void RunAll()
        {
            Program.Run("detail: every surface baked, unit normals, no seam where the tile repeats", Surfaces);
            Program.Run("detail: the W-beam's bolts and lap where the posts are", BeamBolts);
            Program.Run("detail: texture coordinates in metres on swept parts, boxes and tubes", Coordinates);
            Program.Run("detail: faces made without coordinates get them", Ensure);
            Program.Run("detail: tangents along u, square to the normal, the right handedness", Tangents);
            Program.Run("detail: the materials' names and the default kinds", Names);
        }

        private static void Surfaces()
        {
            var watch = Stopwatch.StartNew();
            foreach (DetailKind kind in (DetailKind[])Enum.GetValues(typeof(DetailKind)))
            {
                DetailSurface s = DetailSurfaces.Make(kind);
                if (kind == DetailKind.None)
                {
                    Program.Check(s == null, "none for None");
                    continue;
                }

                Program.Check(s != null && s.Width * s.Height == s.Relief.Length && s.Normals.Length == s.Relief.Length * 3, kind + ": baked");
                float worstLength = 0f, maxTilt = 0f, worstSeam = 0f, maxRelief = 0f;
                bool finite = true, inRange = true;
                for (int k = 0; k < s.Relief.Length; k++)
                {
                    float x = s.Normals[k * 3], y = s.Normals[k * 3 + 1], z = s.Normals[k * 3 + 2];
                    finite &= !float.IsNaN(x + y + z) && !float.IsInfinity(x + y + z);
                    worstLength = Math.Max(worstLength, Math.Abs((float)Math.Sqrt(x * x + y * y + z * z) - 1f));
                    maxTilt = Math.Max(maxTilt, (float)Math.Acos(Math.Min(1f, z)));
                    maxRelief = Math.Max(maxRelief, Math.Abs(s.Relief[k]));
                    inRange &= s.Shade[k] > 0.5f && s.Shade[k] <= 1f && s.Metal[k] >= 0f && s.Metal[k] <= 1f && s.Smooth[k] >= 0f && s.Smooth[k] <= 1f;
                }

                // Across the tile's edges (the last column to the first, the last row to the first) the relief steps no more than between two texels inside it the same way.
                float insideU = 0f, insideV = 0f, seamU = 0f, seamV = 0f;
                for (int j = 0; j < s.Height; j++)
                {
                    seamU = Math.Max(seamU, Math.Abs(s.Relief[j * s.Width + s.Width - 1] - s.Relief[j * s.Width]));
                    for (int i = 1; i < s.Width; i++)
                        insideU = Math.Max(insideU, Math.Abs(s.Relief[j * s.Width + i] - s.Relief[j * s.Width + i - 1]));
                }

                for (int i = 0; i < s.Width; i++)
                {
                    seamV = Math.Max(seamV, Math.Abs(s.Relief[(s.Height - 1) * s.Width + i] - s.Relief[i]));
                    for (int j = 1; j < s.Height; j++)
                        insideV = Math.Max(insideV, Math.Abs(s.Relief[j * s.Width + i] - s.Relief[(j - 1) * s.Width + i]));
                }

                worstSeam = Math.Max(seamU - insideU * 1.5f, seamV - insideV * 1.5f);
                Program.Check(finite && worstLength < 1e-4f, $"{kind}: unit normals ({worstLength})");
                Program.Check(inRange, kind + ": shade and mask in range");
                Program.Check(maxRelief > 1e-6f && maxRelief < 0.05f, $"{kind}: a relief under 5 cm (the rule for a normal map): {maxRelief * 1000f:0.00} mm");
                Program.Check(maxTilt < 1.45f, $"{kind}: no texel turned to the side: {maxTilt:0.00} rad");
                Program.Check(worstSeam <= 1e-6f, $"{kind}: no seam where it repeats (across {seamU * 1000f:0.000} mm against {insideU * 1000f:0.000} inside, along {seamV * 1000f:0.000} against {insideV * 1000f:0.000})");
                DetailSurface again = DetailSurfaces.Make(kind);
                Program.Check(again.Relief[again.Relief.Length / 3] == s.Relief[s.Relief.Length / 3] && again.Normals[17] == s.Normals[17], kind + ": the same every time");
            }

            Console.WriteLine($"  detail: every surface baked twice in {watch.ElapsedMilliseconds} ms");
        }

        private static float ReliefAt(DetailSurface s, float u, float v)
        {
            int i = Math.Max(0, Math.Min(s.Width - 1, (int)(u / s.SizeU * s.Width))), j = Math.Max(0, Math.Min(s.Height - 1, (int)(v / s.SizeV * s.Height)));
            return s.Relief[j * s.Width + i];
        }

        private static void BeamBolts()
        {
            DetailSurface four = DetailSurfaces.Make(DetailKind.WBeam4), two = DetailSurfaces.Make(DetailKind.WBeam2);
            float width = DetailSurfaces.ProfileLength(Barriers.BeamProfile(false));
            Program.Near(four.SizeU, width, 1e-5f, "the beam's tile is as wide as its profile is long");
            Program.Check(ReliefAt(four, width * 0.5f, 0.001f) > 0.010f, "the post bolt at the splice (on the lap): " + ReliefAt(four, width * 0.5f, 0.001f));
            Program.Check(ReliefAt(four, width * 0.22f, 0.051f) > 0.010f && ReliefAt(four, width * 0.78f, 3.949f) > 0.010f, "the splice bolts either side of the joint");
            Program.Check(ReliefAt(four, width * 0.5f, 2f) < 0.001f, "no bolt half way with posts every 4 m");
            Program.Check(ReliefAt(two, width * 0.5f, 2f) > 0.008f, "a post bolt half way with posts every 2 m");
            Program.Check(Math.Abs(ReliefAt(four, width * 0.4f, 0.1f) - 0.003f) < 0.001f && ReliefAt(four, width * 0.4f, 1f) < 0.0005f, "the lap is the beam's thickness proud, over the joint only");
            // The beam is swept with v from where its whole run starts: the posts (every 4 m from there) fall on the splices.
            MarkingPlan plan = MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.WBeamId),
                MarkingTemplates.WithRun(MarkingTemplates.ById(MarkingTemplates.WBeamId), MarkingTemplates.ById(MarkingTemplates.WBeamId).Default().With("ends", "none").ToString(),
                    RunPath.Encode(new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 10f) }, out _), 6f, 40f));
            MarkingPart beam = plan.Parts.Find(p => p.Detail == DetailKind.WBeam4);
            float vAtStart = float.NaN;
            for (int i = 0; i < beam.Mesh.VertexCount; i++)
            {
                if (Math.Abs(beam.Mesh.Positions[i].Z) < 1e-3f)
                    vAtStart = beam.Mesh.V[i];
            }

            // The piece starts 6 m along the run: its first post is 2 m in (8 m along), and v grows with the length along the beam, in tiles of 4 m.
            Program.Near(vAtStart * DetailSurfaces.BeamPeriod, 6f, 1e-3f, "the beam's v at the piece's start is where the piece starts along the whole run");
            Program.Near((vAtStart * DetailSurfaces.BeamPeriod + 2f) % DetailSurfaces.BeamPeriod, 0f, 1e-3f, "so the first post, 2 m in, is at a splice of the texture");
        }

        private static float UvArea(MeshBuilder mesh)
        {
            float area = 0f;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                area += Math.Abs((mesh.U[b] - mesh.U[a]) * (mesh.V[c] - mesh.V[a]) - (mesh.U[c] - mesh.U[a]) * (mesh.V[b] - mesh.V[a])) * 0.5f;
            }

            return area;
        }

        private static float Area(MeshBuilder mesh)
        {
            float area = 0f;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                area += MeshBuilder.Cross(mesh.Positions[mesh.Indices[t + 1]] - mesh.Positions[mesh.Indices[t]], mesh.Positions[mesh.Indices[t + 2]] - mesh.Positions[mesh.Indices[t]]).Length * 0.5f;
            return area;
        }

        private static void Coordinates()
        {
            // A square bar 0.1 m across swept 5 m: its texture covers as much as its four sides (metres both ways).
            var sweep = new MeshBuilder();
            Barriers.Sweep(sweep, new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 5f) }, new List<(float u, float v)> { (-0.05f, 0f), (0.05f, 0f), (0.05f, 0.1f), (-0.05f, 0.1f) }, true, false, false, 7f);
            Program.Near(UvArea(sweep), Area(sweep), 1e-3f, "a swept bar's texture covers its area in square metres");
            float minV = float.MaxValue, maxV = float.MinValue;
            foreach (float v in sweep.V)
            {
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }

            Program.Check(Math.Abs(minV - 7f) < 1e-4f && Math.Abs(maxV - 12f) < 1e-4f, $"v runs from where the line starts along the whole run: {minV} .. {maxV}");
            var box = new MeshBuilder();
            Barriers.Box(box, new Vec3(3f, 0f, 4f), new Vec3(0f, 0f, 1f), 0.4f, 0.6f, 0f, 0.8f);
            Program.Near(UvArea(box), Area(box), 1e-3f, "a box's texture covers its five faces in square metres");
            var tube = new MeshBuilder();
            Solids.Tube(tube, new Vec3(0f, 0f, 0f), new Vec3(0f, 2f, 0f), 0.05f, 0.05f, 24, false, false);
            Program.Near(UvArea(tube), Area(tube), 0.01f * Area(tube), "a tube's texture covers its side in square metres");
        }

        private static void Ensure()
        {
            var mesh = new MeshBuilder();
            Solids.Box(mesh, new Vec3(1f, 1f, 1f), new Vec3(0.5f, 0.25f, 0.1f));
            Program.Check(UvArea(mesh) < 1e-6f, "a convex box has no coordinates of its own");
            DetailUv.Ensure(mesh);
            Program.Near(UvArea(mesh), Area(mesh), 1e-3f, "given a box map in metres");
            var plan = new MarkingPlan();
            var shape = new MarkingShape { Solid = SolidColour.Concrete };
            Solids.Box(shape.Mesh, new Vec3(0f, 0.5f, 0f), new Vec3(0.2f, 0.5f, 0.3f));
            plan.Shapes.Add(shape);
            plan.Bake(0f);
            (float su, float sv) = DetailSurfaces.TileOf(DetailKind.Concrete);
            Program.Near(UvArea(plan.Parts[0].Mesh) * su * sv, Area(plan.Parts[0].Mesh), 1e-3f, "a plan's detail part gets them when it is baked, in tiles of its kind (the game's shader has no tiling)");
        }

        private static void Tangents()
        {
            var mesh = new MeshBuilder();
            Barriers.Box(mesh, new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 1f), 1f, 2f, 0f, 1f);
            float[] t = MeshTangents.Compute(mesh);
            bool square = true, unit = true, along = true;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var tangent = new Vec3(t[i * 4], t[i * 4 + 1], t[i * 4 + 2]);
                square &= Math.Abs(Vec3.Dot(tangent, mesh.Normals[i])) < 1e-4f;
                unit &= Math.Abs(tangent.Length - 1f) < 1e-4f && Math.Abs(Math.Abs(t[i * 4 + 3]) - 1f) < 1e-6f;
            }

            // On the top (normal up, u across to +X, v along +Z): the tangent is +X and the bitangent (normal x tangent, times w) is +Z.
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                if (mesh.Normals[i].Y < 0.99f)
                    continue;
                var tangent = new Vec3(t[i * 4], t[i * 4 + 1], t[i * 4 + 2]);
                Vec3 bitangent = MeshBuilder.Cross(mesh.Normals[i], tangent) * t[i * 4 + 3];
                along &= tangent.X > 0.99f && bitangent.Z > 0.99f;
            }

            Program.Check(square && unit, "tangents unit, square to the normals, w of one");
            Program.Check(along, "on the top the tangent is +u and the bitangent +v");
            var none = new MeshBuilder();
            int a = none.Vertex(new Vec3(0f, 0f, 0f), new Vec3(0f, 1f, 0f)), b = none.Vertex(new Vec3(1f, 0f, 0f), new Vec3(0f, 1f, 0f)), c = none.Vertex(new Vec3(0f, 0f, 1f), new Vec3(0f, 1f, 0f));
            none.Tri(a, b, c);
            float[] fallback = MeshTangents.Compute(none);
            Program.Check(!float.IsNaN(fallback[0]) && Math.Abs(fallback[1]) < 1e-6f, "a face without coordinates gets a tangent square to its normal");
        }

        private static void Names()
        {
            foreach (DetailKind kind in new[] { DetailKind.Concrete, DetailKind.WBeam2, DetailKind.Reflective })
            {
                foreach (SolidColour colour in new[] { SolidColour.Metal, SolidColour.DarkGray, SolidColour.White })
                {
                    string name = ChunkBuilder.DetailMaterial(kind, colour);
                    Program.Check(ChunkBuilder.DetailOf(name, out DetailKind k, out SolidColour c) && k == kind && c == colour && !ChunkBuilder.IsMarkingMaterial(name), "a detail material's name says its kind and colour: " + name);
                }
            }

            Program.Check(!ChunkBuilder.DetailOf("detail:0:1", out _, out _) && !ChunkBuilder.DetailOf("detail:99:1", out _, out _) && !ChunkBuilder.DetailOf(ChunkBuilder.MetalMaterial, out _, out _), "not a detail material: none, an unknown kind, the metal");
            Program.Check(MarkingPlan.DetailOf(SolidColour.Concrete) == DetailKind.Concrete && MarkingPlan.DetailOf(SolidColour.Metal) == DetailKind.Galvanised && MarkingPlan.DetailOf(SolidColour.Blue) == DetailKind.Painted
                && MarkingPlan.DetailOf(SolidColour.Wood) == DetailKind.Wood, "a solid's default kind by its colour");
        }
    }
}
