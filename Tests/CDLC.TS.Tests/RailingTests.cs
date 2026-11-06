using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The railings and fences of 0.12.34 (Barriers.Railings, MarkingTemplates.Railings): sound bodies, the sizes the clauses and the suppliers give, the pieces of a run in step, the wire meshes.</summary>
    internal static class RailingTests
    {
        private static readonly string[] kIds =
        {
            MarkingTemplates.BeijingFenceId, MarkingTemplates.ZincFenceId, MarkingTemplates.CrowdBarrierId, MarkingTemplates.BridgeRailingId, MarkingTemplates.StoneBalustradeId,
            MarkingTemplates.ExpresswayFenceId, MarkingTemplates.AntiThrowId, MarkingTemplates.StoneBallId, MarkingTemplates.FauxWoodId, MarkingTemplates.PalisadeId, MarkingTemplates.EdgeGuardId
        };

        public static void RunAll()
        {
            Program.Run("railings: the catalogue and sound bodies, on a straight and on a bend", Catalogue);
            Program.Run("railings: the Beijing-style U's and the zinc-steel uprights", Fences);
            Program.Run("railings: the bridge railing's and the palisade's clear gaps (CJJ 11 9.5.1)", Gaps);
            Program.Run("railings: heights (JTG D81 8.1.2, GB 50688 7.7.3, JGJ 80 4.3.1)", Heights);
            Program.Run("railings: crowd barriers end to end across the pieces of a run", Crowd);
            Program.Run("railings: the stone balustrade and the stone balls", Stone);
            Program.Run("railings: the wire meshes cut out, their coverage kept in the mips", Meshes);
            Program.Run("detail: texture coordinates in tiles after baking (the W-beam's bolts every 4 m)", Tiles);
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static List<Vec3> Straight(float length) => new List<Vec3> { P(0f, 0f), P(0f, length) };

        private static List<Vec3> Bend()
        {
            var line = new List<Vec3>();
            for (int i = 0; i <= 16; i++)
            {
                double t = i / 16.0 * Math.PI * 0.5;
                line.Add(P((float)(25.0 - 25.0 * Math.Cos(t)), (float)(25.0 * Math.Sin(t))));
            }

            return line;
        }

        private static MarkingPlan PlanOf(string id, string settings, List<Vec3> line, float phase = 0f, float total = 0f)
        {
            GuideTemplate t = MarkingTemplates.ById(id);
            string path = RunPath.Encode(line, out _);
            GuideSpec spec = t.Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), path, phase, total));
        }

        private static void Bounds(IEnumerable<MeshBuilder> meshes, out Vec3 lo, out Vec3 hi)
        {
            lo = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            hi = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MeshBuilder mesh in meshes)
            {
                foreach (Vec3 p in mesh.Positions)
                {
                    lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                    hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
                }
            }
        }

        private static IEnumerable<MeshBuilder> Meshes(MarkingPlan plan)
        {
            foreach (MarkingPart part in plan.Parts)
                yield return part.Mesh;
        }

        private static bool Wound(MeshBuilder mesh)
        {
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                if (n.Length > 1e-8f && Vec3.Dot(n, mesh.Normals[mesh.Indices[t]]) < 0f)
                    return false;
            }

            return true;
        }

        private static void Catalogue()
        {
            foreach (string id in kIds)
            {
                SignDef def = SignCatalog.ById(id);
                Program.Check(def != null && def.IsMarking && def.Group == MarkingTemplates.BarrierGroup && MarkingTemplates.IsRun(def.Template), id + ": in the catalogue, in the barriers' group, drawn along a road");
                Program.Check(MarkingGround.Lies(def), id + ": laid on the ground");
                foreach (List<Vec3> line in new[] { Straight(30f), Bend() })
                {
                    MarkingPlan plan = PlanOf(id, "", line);
                    Program.Check(plan != null && plan.HasSolids && plan.TriangleCount > 20, id + ": a body: " + plan?.TriangleCount);
                    foreach (MarkingPart part in plan.Parts)
                    {
                        Program.Check(part.Solid.HasValue, id + ": only solid parts");
                        Program.Check(Wound(part.Mesh), id + ": every triangle faces out (" + part.Solid + " " + part.Detail + ")");
                        bool finite = true;
                        foreach (Vec3 p in part.Mesh.Positions)
                            finite &= p.IsFinite;
                        Program.Check(finite, id + ": every point a number");
                        bool tiled = true;
                        for (int i = 0; i < part.Mesh.VertexCount; i++)
                            tiled &= !float.IsNaN(part.Mesh.U[i]) && !float.IsNaN(part.Mesh.V[i]);
                        Program.Check(tiled, id + ": texture coordinates are numbers");
                    }

                    Program.Check(plan.TriangleCount < 80000, id + ": not too heavy for its run: " + plan.TriangleCount);
                }

                MarkingPlan straight = PlanOf(id, "", Straight(30f));
                Bounds(Meshes(straight), out Vec3 lo, out Vec3 hi);
                Program.Check(lo.Z > -0.5f && hi.Z < 31.5f && Math.Abs(lo.X) < 1f && Math.Abs(hi.X) < 1f, id + ": stays along its 30 m line: " + lo + " .. " + hi);
            }
        }

        /// <summary>The middles along Z of the parts' vertices at height y within dy, near X = 0, clustered by gap.</summary>
        private static List<float> Columns(MeshBuilder mesh, float y, float dy, float gap)
        {
            var zs = new List<float>();
            foreach (Vec3 p in mesh.Positions)
            {
                if (Math.Abs(p.Y - y) <= dy && Math.Abs(p.X) < 0.05f)
                    zs.Add(p.Z);
            }

            zs.Sort();
            var middles = new List<float>();
            int first = 0;
            for (int i = 1; i <= zs.Count; i++)
            {
                if (i == zs.Count || zs[i] - zs[i - 1] > gap)
                {
                    float sum = 0f;
                    for (int k = first; k < i; k++)
                        sum += zs[k];
                    middles.Add(sum / (i - first));
                    first = i;
                }
            }

            return middles;
        }

        private static void Fences()
        {
            // Beijing style: on a panel of 3 m eleven U's, each two legs 0.18 m apart, the U's 0.25 m apart; the top of the U's at the height.
            List<MarkingShape> bj = Barriers.BeijingFence(Straight(3f), 1.2f, SolidColour.White, SolidColour.Yellow, default, 0f, 0f, true, true);
            // The legs' feet on the channel (its top 0.26 m up): rings of the legs' tubes there.
            List<float> legs = Columns(bj[0].Mesh, 0.26f, 0.0005f, 0.03f);
            legs.RemoveAll(z => z < 0.06f || z > 2.94f);
            Program.Check(legs.Count == 22, "22 legs on a panel (eleven U's): " + legs.Count);
            if (legs.Count == 22)
            {
                Program.Near(legs[1] - legs[0], 0.18f, 0.005f, "a U's legs 180 mm apart");
                Program.Near(legs[2] - legs[1], 0.07f, 0.005f, "70 mm between two U's");
            }

            Bounds(new[] { bj[0].Mesh }, out _, out Vec3 bjHi);
            Program.Near(bjHi.Y, 1.23f, 0.005f, "the posts' tops just over the height (1.2 m and their caps)");
            // Lowered at a gap: the U's come down to 0.70 m at the run's end.
            var taper = new GapTaper { AtEnd = true, Length = 20f };
            List<MarkingShape> low = Barriers.BeijingFence(Straight(30f), 1.2f, SolidColour.White, SolidColour.Yellow, taper, 0f, 30f, true, true);
            float top = float.MinValue;
            foreach (Vec3 p in low[0].Mesh.Positions)
            {
                if (p.Z > 29.5f)
                    top = Math.Max(top, p.Y);
            }

            Program.Check(top < 0.75f, "at a pedestrians' gap the railing is 0.70 m high (GB 50688 7.6.1-1): " + top);

            // Zinc steel: the uprights eighteen spaces to a 3.08 m panel, blue below and white above, the top rail's top 0.1 m under the posts' tops.
            List<MarkingShape> zinc = Barriers.ZincSteelFence(Straight(3.08f), 1.2f, default, 0f, 0f, true, true);
            // The blue parts' feet on the bottom rail (0.285 m): a box each, its corners 50 mm apart along.
            List<float> blue = Columns(zinc[1].Mesh, 0.285f, 0.0005f, 0.06f);
            Program.Check(blue.Count == 17, "17 uprights between two posts: " + blue.Count);
            if (blue.Count > 2)
                Program.Near(blue[1] - blue[0], 3.08f / 18f, 0.003f, "the uprights 171 mm apart");
            Program.Check(zinc[0].Mesh.Positions.Exists(p => Math.Abs(p.X) < 1e-4f && Math.Abs(p.Y - 1.1f) < 1e-3f), "the top rail's top at 1.1 m on 1.2 m posts (a tender's drawing; GB 50688 7.6.1: 1.10 m)");
        }

        private static void Gaps()
        {
            // The bridge railing's uprights: the clear gap between two no more than 110 mm (CJJ 11 9.5.1).
            List<MarkingShape> bridge = Barriers.BridgeRailing(Straight(6f), 1.1f, SolidColour.Blue, 0f, true, true);
            // The uprights' feet on the bottom rail (0.3 m up).
            List<float> bars = Columns(bridge[0].Mesh, 0.3f, 0.0005f, 0.02f);
            float widest = 0f;
            for (int i = 1; i < bars.Count; i++)
            {
                float clear = bars[i] - bars[i - 1] - 0.0318f;
                if (clear < 0.2f)
                    widest = Math.Max(widest, clear);
            }

            Program.Check(bars.Count > 30 && widest <= 0.11f, $"the bridge railing's clear gaps no more than 110 mm: {bars.Count} uprights, the widest {widest:0.000} m");
            List<MarkingShape> palisade = Barriers.Palisade(Straight(6f), 1.8f, SolidColour.Black, 0f, true, true);
            // The pickets' feet (0.08 m up): a box each, its corners 25 mm apart along.
            List<float> pickets = Columns(palisade[0].Mesh, 0.08f, 0.0005f, 0.05f);
            float picketGap = 0f;
            for (int i = 1; i < pickets.Count; i++)
            {
                float clear = pickets[i] - pickets[i - 1] - 0.025f;
                if (clear < 0.2f)
                    picketGap = Math.Max(picketGap, clear);
            }

            Program.Check(pickets.Count > 30 && picketGap <= 0.11f, $"the palisade's clear gaps no more than 110 mm: {pickets.Count} pickets, the widest {picketGap:0.000} m");
            Bounds(new[] { palisade[0].Mesh }, out _, out Vec3 hi);
            Program.Near(hi.Y, 1.87f, 0.005f, "the palisade's spears at its height, the posts' caps a little over");
        }

        private static void Heights()
        {
            foreach (string h in new[] { "150", "180", "200" })
            {
                MarkingPlan plan = PlanOf(MarkingTemplates.ExpresswayFenceId, "height=" + h, Straight(10f));
                float top = float.MinValue;
                foreach (MarkingPart part in plan.Parts)
                {
                    if (part.Detail == DetailKind.WeldedMesh)
                    {
                        foreach (Vec3 p in part.Mesh.Positions)
                            top = Math.Max(top, p.Y);
                    }
                }

                Program.Check(top >= float.Parse(h) / 100f - 0.06f && top <= float.Parse(h) / 100f, $"the fence's mesh reaches {h} cm (under its top rail): {top}");
                Program.Check(plan.Parts.Exists(p => p.Detail == DetailKind.WeldedMesh), "the fence has a welded mesh");
            }

            MarkingPlan screen = PlanOf(MarkingTemplates.AntiThrowId, "height=200", Straight(10f));
            Bounds(Meshes(screen), out _, out Vec3 shi);
            Program.Near(shi.Y, 2f, 0.005f, "the anti-throw screen 2.0 m from the footway (GB 50688 7.7.3)");
            MarkingPlan onWall = PlanOf(MarkingTemplates.AntiThrowId, "height=180;wall=100", Straight(10f));
            Bounds(Meshes(onWall), out _, out Vec3 whi);
            Program.Near(whi.Y, 2.8f, 0.005f, "1.8 m over a 1.0 m wall (JTG D81 9.1.2)");
            Program.Check(screen.Parts.Exists(p => p.Detail == DetailKind.FineMesh), "the screen has a fine mesh");

            List<MarkingShape> guard = Barriers.EdgeGuard(Straight(6f), true, 0f, true, true);
            Program.Check(guard[0].Mesh.Positions.Exists(p => Math.Abs(p.X) <= 0.0151f && Math.Abs(p.Y - 1.2f) < 1e-3f), "the edge guard's top rail at 1.2 m (JGJ 80 4.3.1)");
            List<float> posts = Columns(guard[1].Mesh, 0.45f, 0.1f, 0.1f);
            for (int i = 1; i < posts.Count; i++)
                Program.Check(posts[i] - posts[i - 1] <= 2.001f, "the edge guard's posts no more than 2 m apart: " + (posts[i] - posts[i - 1]));
            Bounds(new[] { guard[2].Mesh }, out Vec3 mlo, out _);
            Program.Near(mlo.Y, 0.18f, 0.005f, "the mesh over a toe board 0.18 m high");
        }

        private static void Crowd()
        {
            // A run of 20 m in two pieces (the second starting 9 m along): the panels' starts are every 2.05 m along the whole run, none twice and none left out where the pieces meet. A panel's frame
            // stands on the ground at both its ends (rings 0.03 m up): the ends' rings, 50 mm apart where two panels meet, alternate start and end.
            var starts = new List<float>();
            foreach ((List<Vec3> line, float phase, bool last) in new[] { (Straight(9f), 0f, false), (new List<Vec3> { P(0f, 9f), P(0f, 20f) }, 9f, true) })
            {
                List<MarkingShape> shapes = Barriers.CrowdBarriers(line, true, CrowdScheme.Galvanised, phase, last);
                foreach (Vec3 p in shapes[0].Mesh.Positions)
                {
                    // The near end of a panel: its frame's lowest point at its start.
                    if (Math.Abs(p.Y - 0.03f) < 0.002f && Math.Abs(p.X) < 0.02f)
                        starts.Add(p.Z);
                }
            }

            var ends = new List<float>();
            starts.Sort();
            for (int i = 0; i < starts.Count; i++)
            {
                // A ring 32 mm across has points 11 mm apart along; two panels' rings are 18 mm apart.
                if (i == 0 || starts[i] - starts[i - 1] > 0.015f)
                    ends.Add(starts[i]);
            }

            Program.Check(ends.Count == 18, "nine panels of 2.0 m in 20 m (the tenth would go past the end), two ends each: " + ends.Count);
            for (int i = 2; i + 1 < ends.Count; i += 2)
                Program.Near(ends[i] - ends[i - 2], 2.05f, 0.02f, "a panel every 2.05 m across the pieces");
            List<MarkingShape> banded = Barriers.CrowdBarriers(Straight(10f), true, CrowdScheme.BlackYellow, 0f, true);
            Program.Check(banded[0].Mesh.VertexCount > 0 && banded[1].Mesh.VertexCount > 0, "black and yellow bands");
            Bounds(new[] { banded[0].Mesh, banded[1].Mesh }, out _, out Vec3 hi);
            Program.Near(hi.Y, 1.2f + 0.016f, 0.005f, "a 1.2 m panel");
        }

        private static void Stone()
        {
            List<MarkingShape> rail = Barriers.StoneBalustrade(Straight(4f), 1.1f, SolidColour.Granite, 0f, true, true);
            Program.Check(rail[0].Detail == DetailKind.Stone, "stone with its grain");
            Bounds(new[] { rail[0].Mesh }, out _, out Vec3 hi);
            Program.Near(hi.Y, 1.1f * 1.27f, 0.005f, "the posts about 1.3 times the railing (營造法式)");
            Program.Check(rail[0].Mesh.Positions.Exists(p => Math.Abs(p.X) < 1e-4f && Math.Abs(p.Y - 1.1f) < 1e-3f), "the coping at 1.10 m (CJJ 11 9.5.1)");
            // 8 m: posts at 0, 1.9, 3.8, 5.7, 7.6 and one at the end, 0.4 m on.
            List<MarkingShape> ending = Barriers.StoneBalustrade(Straight(8f), 1.1f, SolidColour.Granite, 0f, true, true);
            Program.Check(ending[0].Mesh.Positions.Exists(p => Math.Abs(p.Z - 8f) < 1e-3f && p.Y > 1.3f), "a balustrade ends at a post");
            List<MarkingShape> balls = Barriers.StoneBalls(Straight(6f), 0.5f, 1.5f, SolidColour.Granite, 0f, true);
            Bounds(new[] { balls[0].Mesh }, out Vec3 blo, out Vec3 bhi);
            Program.Near(bhi.Y, 0.15f + 0.25f * 0.9f + 0.25f, 0.005f, "a 0.5 m ball on a plinth 0.15 m high");
            Program.Check(bhi.X <= 0.251f && blo.X >= -0.251f, "the ball 0.5 m across: " + blo.X + " .. " + bhi.X);
            // The balls' equators (0.375 m up): two points each near X = 0, 0.5 m apart.
            Program.Check(Columns(balls[0].Mesh, 0.375f, 0.001f, 0.6f).Count == 5, "five balls in 6 m at 1.5 m");
            List<float> crowded = Columns(Barriers.StoneBalls(Straight(6.5f), 0.5f, 1.5f, SolidColour.Granite, 0f, true)[0].Mesh, 0.375f, 0.001f, 0.6f);
            Program.Check(crowded.Count == 5, "no ball at the end 0.5 m from the one before it: " + crowded.Count);
            // The panels' fields are sunk inside their borders: the panel is 0.1 m thick at its border and 0.06 m in its field.
            Program.Check(rail[0].Mesh.Positions.Exists(p => Math.Abs(Math.Abs(p.X) - 0.03f) < 1e-4f) && rail[0].Mesh.Positions.Exists(p => Math.Abs(Math.Abs(p.X) - 0.05f) < 1e-4f), "a field 60 mm thick inside a border 100 mm thick");
        }

        private static void Meshes()
        {
            foreach ((DetailKind kind, float up, float along, float wire) in new[] { (DetailKind.WeldedMesh, 0.075f, 0.15f, 0.004f), (DetailKind.FineMesh, 0.05f, 0.05f, 0.003f), (DetailKind.GuardMesh, 0.04f, 0.04f, 0.0038f) })
            {
                Program.Check(DetailSurfaces.HasHoles(kind), kind + ": has holes");
                DetailSurface s = DetailSurfaces.Make(kind);
                Program.Check(s != null && s.Alpha != null && s.Alpha.Length == s.Width * s.Height, kind + ": an alpha map");
                int over = 0;
                foreach (float a in s.Alpha)
                    over += a > 0.5f ? 1 : 0;
                float coverage = over / (float)s.Alpha.Length;
                float expected = 1f - (1f - wire / up) * (1f - wire / along);
                Program.Near(coverage, expected, 0.02f, kind + ": as much wire as the mesh has");
                // The tile's first and last columns and rows are alike (a wire at the edge goes on across it).
                int differ = 0;
                for (int j = 0; j < s.Height; j++)
                    differ += Math.Abs(s.Alpha[j * s.Width] - s.Alpha[j * s.Width + s.Width - 1]) > 0.5f ? 1 : 0;
                for (int i = 0; i < s.Width; i++)
                    differ += Math.Abs(s.Alpha[i] - s.Alpha[(s.Height - 1) * s.Width + i]) > 0.5f ? 1 : 0;
                Program.Check(differ == 0, kind + ": no seam where the tile repeats: " + differ);
                List<float[]> mips = DetailSurfaces.CoverageMips(s.Alpha, s.Width, s.Height, 0.5f);
                Program.Check(mips.Count > 5, kind + ": its mips");
                for (int level = 1; level < mips.Count; level++)
                {
                    int o = 0;
                    foreach (float a in mips[level])
                        o += a >= 0.5f ? 1 : 0;
                    float c = o / (float)mips[level].Length;
                    Program.Check(c <= coverage + 0.5f / mips[level].Length + 1e-6f, $"{kind}: mip {level} keeps no more wire than the mesh has: {c:0.000} of {coverage:0.000}");
                    if (mips[level].Length >= 256)
                        Program.Check(c >= coverage * 0.5f, $"{kind}: mip {level} keeps most of the wire (it would vanish a few metres away): {c:0.000} of {coverage:0.000}");
                }
            }

            foreach (DetailKind kind in (DetailKind[])Enum.GetValues(typeof(DetailKind)))
            {
                if (!DetailSurfaces.HasHoles(kind))
                    Program.Check(DetailSurfaces.Make(kind)?.Alpha == null, kind + ": solid");
            }

            // A mesh is drawn in the pictures as a screen: what is behind it shows through.
            var sheet = new MeshBuilder();
            Solids.Box(sheet, new Vec3(0f, 0.5f, 0f), new Vec3(0.5f, 0.5f, 0.001f));
            var back = new MeshBuilder();
            Solids.Box(back, new Vec3(0f, 0.5f, 1f), new Vec3(0.6f, 0.6f, 0.01f));
            var bodies = new List<(MeshBuilder Mesh, byte[] Rgb)> { (sheet, new byte[] { 0, 200, 0 }), (back, new byte[] { 200, 0, 0 }) };
            byte[] seen = Raster.RenderBodies(bodies, new Vec3(0f, 0f, 1f), 64, out int w, out int h, new RasterOptions { Transparent = true }, new HashSet<MeshBuilder> { sheet });
            int green = 0, red = 0;
            for (int y = h / 4; y < h * 3 / 4; y++)
            {
                for (int x = w / 4; x < w * 3 / 4; x++)
                {
                    int k = (y * w + x) * 4;
                    green += seen[k + 1] > seen[k] ? 1 : 0;
                    red += seen[k] > seen[k + 1] ? 1 : 0;
                }
            }

            Program.Check(green > 0 && red > 0 && Math.Abs(green - red) < (green + red) / 4, $"a screen shows what is behind it: {green} of its own pixels, {red} behind");
        }

        private static void Tiles()
        {
            // A W-beam 8 m long: its beam's texture coordinates along run 0 .. 2 tiles (one tile every 4 m), so that the splice and the bolts come every 4 m, where the posts are.
            MarkingPlan plan = PlanOf(MarkingTemplates.WBeamId, "", Straight(8f));
            MarkingPart beam = plan.Parts.Find(p => p.Detail == DetailKind.WBeam4 || p.Detail == DetailKind.WBeam2);
            Program.Check(beam != null, "the beam has its detail");
            if (beam == null)
                return;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < beam.Mesh.VertexCount; i++)
            {
                if (Math.Abs(beam.Mesh.Normals[i].Z) > 0.5f)
                    continue;
                lo = Math.Min(lo, beam.Mesh.V[i]);
                hi = Math.Max(hi, beam.Mesh.V[i]);
            }

            Program.Check(hi - lo > 1.8f && hi - lo < 2.3f, $"8 m of beam is two tiles of 4 m: v from {lo:0.00} to {hi:0.00}");
            (float su, float sv) = DetailSurfaces.TileOf(DetailKind.Concrete);
            Program.Check(su == 0.5f && sv == 0.5f, "concrete's tile 0.5 m");
            foreach (DetailKind kind in (DetailKind[])Enum.GetValues(typeof(DetailKind)))
            {
                DetailSurface s = DetailSurfaces.Make(kind);
                (float u, float v) = DetailSurfaces.TileOf(kind);
                Program.Check(kind == DetailKind.None ? s == null && u == 0f : s != null && Math.Abs(s.SizeU - u) < 1e-6f && Math.Abs(s.SizeV - v) < 1e-6f, kind + ": its surface as big as its tile");
            }
        }
    }
}
