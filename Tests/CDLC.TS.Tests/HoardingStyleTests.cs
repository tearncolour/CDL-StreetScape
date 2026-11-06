using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The styles of the construction hoarding and its flower troughs (0.12.78): their sizes, how a run of pieces goes on, the feet, the closed bodies, and the plants asked of the game.</summary>
    internal static class HoardingStyleTests
    {
        private static readonly string[] kStyles = { "steel", "module", "pvc", "mesh", "brick", "warn", "lamp", "gate" };

        public static void RunAll()
        {
            Program.Run("hoarding styles: the template and its fields", Template);
            Program.Run("hoarding styles: every style stands in the ground, is finite and faces its normals", Bodies);
            Program.Run("hoarding styles: the colour-coated steel sheet", Steel);
            Program.Run("hoarding styles: modular, PVC, mesh and brick", Others);
            Program.Run("hoarding styles: warning stripes, lamps and the site gate", Marked);
            Program.Run("hoarding styles: a run of two pieces is the run of one", Pieces);
            Program.Run("hoarding styles: bends, slopes and short runs", Bends);
            Program.Run("hoarding styles: flower troughs and their plants", Flowers);
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static List<Vec3> Straight(float length) => new List<Vec3> { P(0f, 0f), P(0f, length) };

        private static MarkingPlan PlanOf(string settings, List<Vec3> line, float phase = 0f, float total = 0f)
        {
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.HoardingId);
            string path = RunPath.Encode(line, out _);
            GuideSpec spec = t.Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), path, phase, total));
        }

        /// <summary>All the parts of a colour (and a detail, when asked for) as one mesh, null when there are none.</summary>
        private static MeshBuilder Mesh(MarkingPlan plan, SolidColour colour, DetailKind? detail = null)
        {
            MeshBuilder all = null;
            foreach (MarkingPart p in plan.Parts)
            {
                if (p.Solid != colour || p.Mesh.VertexCount == 0 || (detail.HasValue && p.Detail != detail.Value))
                    continue;
                all = all ?? new MeshBuilder();
                all.Append(p.Mesh, Vec3.Zero, 0f);
            }

            return all;
        }

        private static int Vertices(MarkingPlan plan) => plan.Parts.Sum(p => p.Mesh.VertexCount);

        private static bool Finite(MarkingPlan plan) => plan.Parts.All(part => part.Mesh.Positions.All(p => p.IsFinite) && part.Mesh.Normals.All(n => n.IsFinite));

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

        private static float Area(MeshBuilder mesh)
        {
            double area = 0.0;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                area += MeshBuilder.Cross(mesh.Positions[mesh.Indices[t + 1]] - mesh.Positions[mesh.Indices[t]], mesh.Positions[mesh.Indices[t + 2]] - mesh.Positions[mesh.Indices[t]]).Length * 0.5;
            return (float)area;
        }

        private static float TotalArea(params MarkingPlan[] plans) => plans.Sum(plan => plan.Parts.Sum(p => Area(p.Mesh)));

        /// <summary>The edges of a mesh's triangles (their corners welded within 0.1 mm) used an odd number of times: the open edges of a body; a sheet drawn on both its sides has none.</summary>
        private static List<(Vec3 A, Vec3 B)> OpenEdges(MeshBuilder mesh)
        {
            var ids = new Dictionary<(long, long, long), int>();
            var at = new List<Vec3>();
            int Id(Vec3 p)
            {
                var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
                if (!ids.TryGetValue(key, out int id))
                {
                    ids[key] = id = ids.Count;
                    at.Add(p);
                }

                return id;
            }

            var edges = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = Id(mesh.Positions[mesh.Indices[t]]), b = Id(mesh.Positions[mesh.Indices[t + 1]]), c = Id(mesh.Positions[mesh.Indices[t + 2]]);
                if (a == b || b == c || a == c)
                    continue;
                foreach ((int p, int q) in new[] { (a, b), (b, c), (c, a) })
                {
                    var e = p < q ? (p, q) : (q, p);
                    edges[e] = edges.TryGetValue(e, out int n) ? n + 1 : 1;
                }
            }

            return edges.Where(e => e.Value % 2 == 1).Select(e => (at[e.Key.Item1], at[e.Key.Item2])).ToList();
        }

        private static void Extent(MeshBuilder mesh, out Vec3 lo, out Vec3 hi) => mesh.Bounds(out lo, out hi);

        private static float MaxAbsX(MeshBuilder mesh) => mesh.Positions.Max(p => Math.Abs(p.X));

        // ------------------------------------------------------------------ the template

        private static void Template()
        {
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.HoardingId);
            GuideSpec spec = t.Default();
            Program.Check(spec.Is("style", "flat") && spec.Is("flowers", "none") && spec.Is("colour", "b") && spec.Is("height", "200") && spec.Is("post", "3"), "the defaults are the flat blue panel, 2 m, posts 3 m, no flowers");
            Program.Check(t.FieldOf("style").Choices.Length == 9, "nine styles: " + t.FieldOf("style").Choices.Length);
            foreach (string key in new[] { "style", "colour", "brick", "height", "post", "gate", "flowers", "plants", "side" })
                Program.Check(t.FieldOf(key) != null, "the field " + key);

            GuideField colour = t.FieldOf("colour"), brick = t.FieldOf("brick"), gate = t.FieldOf("gate"), plants = t.FieldOf("plants"), side = t.FieldOf("side");
            Program.Check(colour.Visible(spec) && !brick.Visible(spec) && !gate.Visible(spec) && !plants.Visible(spec) && !side.Visible(spec), "the flat panel shows the colour only");
            GuideSpec bricks = spec.With("style", "brick");
            Program.Check(!colour.Visible(bricks) && brick.Visible(bricks), "a brick wall has its own colours, not the panel's");
            Program.Check(gate.Visible(spec.With("style", "gate")) && !gate.Visible(bricks), "the gate's width only for a gate");
            GuideSpec flowers = spec.With("flowers", "foot");
            Program.Check(plants.Visible(flowers) && side.Visible(flowers) && !plants.Visible(spec.With("flowers", "none")), "the plants and the side only when there are troughs");
            Program.Check(Array.IndexOf(MarkingTemplates.PlantedIds, MarkingTemplates.HoardingId) >= 0, "the plant pass visits the hoardings");

            // A setting saved before the styles has none of the new keys: it is the flat panel with no flowers, as it was.
            GuideSpec old = t.Parse("colour=g;height=250;post=2");
            Program.Check(old.Is("style", "flat") && old.Is("flowers", "none"), "an old setting is the flat panel");
            string path = RunPath.Encode(Straight(10f), out _);
            ulong a = HoardingRegressionTests.Hash(MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, "colour=g;height=250;post=2", path, 0f)));
            ulong b = HoardingRegressionTests.Hash(PlanOf("colour=g;height=250;post=2", Straight(10f)));
            Program.Check(a == b, "an old setting and the same with the new keys at their defaults draw the same");

            // The flat style draws what Hoarding draws, with nothing added.
            MarkingPlan flat = PlanOf("colour=w;height=180;post=2", Straight(12f));
            Program.Check(flat.Parts.Count == 2 && flat.Props.Count == 0, "two parts and no props: " + flat.Parts.Count + ", " + flat.Props.Count);
        }

        // ------------------------------------------------------------------ bodies

        private static void Bodies()
        {
            foreach (string style in kStyles)
            {
                MarkingPlan plan = PlanOf("style=" + style + ";colour=g", Straight(20f));
                Program.Check(plan.HasSolids && plan.Parts.TrueForAll(p => p.Solid.HasValue) && Finite(plan), style + ": solid parts, finite");
                float low = plan.Parts.Min(p => p.Mesh.Positions.Min(v => v.Y));
                Program.Check(low <= -Barriers.FootSink + 1e-4f, style + ": stands in the ground: " + low);
                foreach (MarkingPart part in plan.Parts)
                    Program.Check(Wound(part.Mesh), style + ": every triangle of the " + part.Solid + " faces its normal");
                foreach (MarkingPart part in plan.Parts)
                {
                    // A wire mesh is a sheet of two faces 4 mm apart and a stripe is a decal (one face, 3 mm proud of the panel): neither is a body.
                    if (part.Detail == DetailKind.GuardMesh || (style == "warn" && (part.Solid == SolidColour.Yellow || part.Solid == SolidColour.Black)))
                        continue;
                    // Open edges are only under the ground (the feet of boxes), or, for the steel sheet, along its top and foot under the capping and the kerb.
                    List<(Vec3 A, Vec3 B)> open = OpenEdges(part.Mesh).Where(e => Math.Max(e.A.Y, e.B.Y) > -Barriers.FootSink + 1e-3f).ToList();
                    if (style == "steel" && part.Solid == SolidColour.Green)
                        open = open.Where(e => !(Math.Abs(e.A.Y - e.B.Y) < 1e-4f && (Math.Abs(e.A.Y - 0.25f) < 1e-4f || Math.Abs(e.A.Y - 1.96f) < 1e-4f))).ToList();
                    Program.Check(open.Count == 0, style + ": the " + part.Solid + " is closed above the ground: " + open.Count + " open edges, the first " + (open.Count > 0 ? open[0].A.ToString() : ""));
                }
            }
        }

        // ------------------------------------------------------------------ steel

        private static void Steel()
        {
            MarkingPlan plan = PlanOf("style=steel;colour=b;height=200;post=3", Straight(10f));
            MeshBuilder sheet = Mesh(plan, SolidColour.Blue), kerb = Mesh(plan, SolidColour.Concrete), frame = Mesh(plan, SolidColour.White);
            Program.Check(sheet != null && kerb != null && frame != null, "the sheet, the kerb and the capping with the posts");
            Extent(kerb, out Vec3 klo, out Vec3 khi);
            Program.Near(khi.Y, 0.30f, 1e-4f, "the kerb 300 mm high");
            Program.Near(khi.X - klo.X, 0.20f, 1e-4f, "and 200 mm wide");
            Extent(sheet, out Vec3 slo, out Vec3 shi);
            Program.Near(slo.Y, 0.25f, 1e-4f, "the sheet's foot 50 mm into the kerb");
            Program.Near(shi.Y, 2.0f - 0.07f + 0.03f, 1e-4f, "its top 30 mm into the capping");
            Extent(frame, out Vec3 flo, out Vec3 fhi);
            Program.Near(fhi.Y, 2.0f, 1e-4f, "the capping's top is the hoarding's height");
            Program.Check(shi.Y < fhi.Y, "the sheet's top is under the capping's");

            // Four distinct depths (two skins, valley and crest), the ribs 25 mm deep and 10 mm thick.
            var depths = new SortedSet<int>(sheet.Positions.Select(p => (int)Math.Round(p.X * 1e4f)));
            Program.Check(depths.Count == 4, "four depths across: " + string.Join(",", depths));
            int[] d = depths.ToArray();
            Program.Check(d.Length == 4 && Math.Abs((d[3] - d[0]) - 350) <= 1 && Math.Abs((d[1] - d[0]) - 100) <= 1 && Math.Abs((d[3] - d[2]) - 100) <= 1, "25 mm of rib and 10 mm of sheet: " + string.Join(",", d));
            // The ribs repeat every 250 mm, and there is a cut at every quarter pitch.
            int rows = sheet.Positions.Select(p => (int)Math.Round(p.Z * 1e4f)).Distinct().Count();
            Program.Check(rows == 161, "a cut every 62.5 mm over 10 m: " + rows);
            Program.Check(sheet.Positions.All(p => Math.Abs(p.Z / 0.0625f - Math.Round(p.Z / 0.0625f)) < 1e-3f), "the cuts are at the quarter pitches");
            Program.Check(sheet.VertexCount == 160 * 8 + 8, "1,288 vertices in 10 m: " + sheet.VertexCount);

            // A corner on a flank of a rib, 5.09 m along (the pitch's 20.36th: 0.44 of the way up the flank, a path keeps whole centimetres): the front skin is 0.44 of the rib over the valley, 3.5 mm over the sheet's middle.
            MeshBuilder flank = Mesh(PlanOf("style=steel;colour=b", new List<Vec3> { P(0f, 0f), P(0f, 5.09f), P(0f, 10f) }), SolidColour.Blue);
            float[] across = flank.Positions.Where(v => Math.Abs(v.Z - 5.09f) < 1e-4f).Select(v => (float)Math.Round(v.X, 4)).Distinct().OrderBy(x => x).ToArray();
            Program.Check(across.Length == 2 && Math.Abs(across[0] + 0.0065f) < 1e-4f && Math.Abs(across[1] - 0.0035f) < 1e-4f, "the flank is a straight slope: depths at its middle " + string.Join(",", across));

            // The ribs go on across the join of two pieces: the sheet's depth at the end of one is the depth at the start of the next.
            MarkingPlan whole = PlanOf("style=steel", Straight(12f), 0f, 12f), a = PlanOf("style=steel", Straight(4.3f), 0f, 12f), b = PlanOf("style=steel", Straight(7.7f), 4.3f, 12f);
            float EndX(MarkingPlan p, bool end)
            {
                MeshBuilder m = Mesh(p, SolidColour.Blue);
                float z = end ? m.Positions.Max(v => v.Z) : m.Positions.Min(v => v.Z);
                return m.Positions.Where(v => Math.Abs(v.Z - z) < 1e-4f && v.Y < 0.3f).Select(v => v.X).Max();
            }

            Program.Near(EndX(a, true), EndX(b, false), 1e-4f, "the front skin's depth at the join");
            Program.Near(TotalArea(a, b), TotalArea(whole), 0.01f, "two pieces are as much sheet as one");
        }

        // ------------------------------------------------------------------ the others

        private static void Others()
        {
            // Modular: foot blocks 600 x 300 x 180 mm under every post, panels 50 mm thick that start at the post's face, the capping on top.
            MarkingPlan module = PlanOf("style=module;colour=b;height=200;post=3", Straight(12f));
            MeshBuilder feet = Mesh(module, SolidColour.Concrete);
            Extent(feet, out Vec3 flo, out Vec3 fhi);
            Program.Near(fhi.Y, 0.18f, 1e-4f, "a foot block 180 mm high");
            Program.Near(fhi.X - flo.X, 0.30f, 1e-4f, "300 mm wide");
            Program.Check(feet.Positions.Where(v => Math.Abs(v.Y - 0.18f) < 1e-4f).Select(v => (int)Math.Round(v.Z * 1e3f)).Distinct().Count() == 5 * 2, "a block to each of five posts (12 m, every 3 m, and the end)");
            Program.Near(Mesh(module, SolidColour.White).Positions.Max(v => v.Y), 2.0f, 1e-4f, "the capping at the hoarding's height");
            MeshBuilder fieldBlue = Mesh(module, SolidColour.Blue);
            Extent(fieldBlue, out Vec3 blo, out Vec3 bhi);
            Program.Near(bhi.X - blo.X, 0.05f, 1e-4f, "panels 50 mm thick");
            Program.Near(blo.Y, 0.22f, 1e-4f, "from 220 mm");
            Program.Near(bhi.Y, 2.0f - 0.06f, 1e-4f, "to the capping");
            Program.Check(fieldBlue.Positions.All(v => Math.Abs(v.Z - 3f * (float)Math.Round(v.Z / 3f)) > 0.0499f), "no panel reaches into a post: every cut is a post's face (50 mm off its middle) or the run's end");
            Program.Check(fieldBlue.Positions.Any(v => Math.Abs(v.Z - 2.95f) < 1e-4f) && fieldBlue.Positions.Any(v => Math.Abs(v.Z - 3.05f) < 1e-4f), "the panels stop at the post's face on either side");

            // PVC: boards of about 500 mm (three on 2 m), grooves between, smooth plastic, on a plinth 250 mm.
            MarkingPlan pvc = PlanOf("style=pvc;colour=w;height=200;post=3", Straight(9f));
            MeshBuilder plank = Mesh(pvc, SolidColour.White, DetailKind.Plastic);
            Program.Check(plank != null, "the boards are smooth plastic");
            Extent(plank, out Vec3 plo, out Vec3 phi);
            Program.Near(plo.Y, 0.25f, 1e-4f, "the lowest board on the plinth");
            Program.Near(phi.Y, 2.0f - 0.08f, 1e-4f, "the top board under the capping");
            int levels = plank.Positions.Select(v => (int)Math.Round(v.Y * 1e3f)).Distinct().Count();
            Program.Check(levels == 10, "three boards, a joint at each meeting and the chamfers either side: " + levels);
            Program.Check(MaxAbsX(plank) <= 0.0251f, "boards 50 mm thick");
            Extent(Mesh(pvc, SolidColour.DarkGray), out Vec3 dlo, out Vec3 dhi);
            Program.Near(dhi.Y, 0.25f, 1e-4f, "the plinth 250 mm high");

            // Wire mesh: 40 x 40 mm cut-out panels in the panel colour, two rails, posts of tube on feet.
            MarkingPlan mesh = PlanOf("style=mesh;colour=g;height=200;post=3", Straight(9f));
            MeshBuilder wire = Mesh(mesh, SolidColour.Green, DetailKind.GuardMesh);
            Program.Check(wire != null && DetailSurfaces.HasHoles(DetailKind.GuardMesh), "the panels are the works mesh with holes");
            Extent(wire, out Vec3 wlo, out Vec3 whi);
            Program.Near(wlo.Y, 0.12f, 1e-4f, "the mesh from 120 mm");
            Program.Near(whi.Y, 1.92f, 1e-4f, "to 80 mm under the top");
            Program.Check(!mesh.Parts.Any(p => p.Solid == SolidColour.Green && p.Detail == DetailKind.GuardMesh && p.Mesh.Positions.Any(v => Math.Abs(v.Z - 3f) < 0.02f && v.Y > 0.3f && v.Y < 1.8f)), "no mesh in a post");
            Extent(Mesh(mesh, SolidColour.Concrete), out Vec3 mlo, out Vec3 mhi);
            Program.Near(mhi.Y, 0.12f, 1e-4f, "feet 120 mm high");

            // Brick: a wall 240 mm, pilasters 370 mm, a coping 300 mm and 80 mm; red, grey or a white render.
            foreach ((string name, SolidColour solid) in new[] { ("red", SolidColour.Brick), ("grey", SolidColour.GreyBrick), ("white", SolidColour.White) })
            {
                MarkingPlan brick = PlanOf("style=brick;brick=" + name + ";height=250;post=3", new List<Vec3> { P(0f, 0f), P(0f, 1.5f), P(0f, 9f) });
                MeshBuilder wall = Mesh(brick, solid);
                Program.Check(wall != null, name + ": the wall's colour");
                Extent(wall, out Vec3 wl, out Vec3 wh);
                Program.Near(wh.X - wl.X, 0.37f, 1e-4f, name + ": the pilasters 370 mm");
                Program.Near(wh.Y, 2.5f - 0.08f, 1e-4f, name + ": under the coping");
                Program.Check(Math.Abs(wall.Positions.Where(v => Math.Abs(v.Z - 1.5f) < 1e-4f).Max(v => v.X) - 0.12f) < 1e-4f, name + ": a wall 240 mm thick between the pilasters");
                if (solid != SolidColour.White)
                    Program.Check(brick.Parts.Any(p => p.Solid == solid && p.Detail == DetailKind.Brick), name + ": drawn with the brick courses");
                MeshBuilder coping = Mesh(brick, SolidColour.Concrete);
                Extent(coping, out Vec3 clo, out Vec3 chi);
                Program.Near(chi.Y, 2.5f, 1e-4f, name + ": the coping's top is the hoarding's height");
            }

            // The courses go on across a join: the wall's texture runs along the whole run (4.3 m at the second piece's start, in tiles of the brick detail), so a brick is not cut by it.
            MeshBuilder second = Mesh(PlanOf("style=brick", Straight(7.7f), 4.3f, 12f), SolidColour.Brick);
            float[] along = Enumerable.Range(0, second.VertexCount).Where(i => Math.Abs(Math.Abs(second.Positions[i].X) - 0.12f) < 1e-4f && second.Positions[i].Z < 0.01f).Select(i => second.V[i]).ToArray();
            Program.Check(along.Length > 0 && along.All(v => Math.Abs(v - 4.3f / DetailSurfaces.TileOf(DetailKind.Brick).V) < 1e-4f), "the wall's texture starts at 4.3 m on the second piece: " + string.Join(",", along.Distinct()));

            // The brick colour has its own field; the panel colour does not touch it.
            Program.Check(Mesh(PlanOf("style=brick;colour=g", Straight(9f)), SolidColour.Green) == null, "the brick wall is not the panel's colour");
        }

        // ------------------------------------------------------------------ stripes, lamps, gate

        private static void Marked()
        {
            // Stripes: yellow and black, both faces, 350 mm band, never past the piece's ends, in step along a whole run.
            MarkingPlan warn = PlanOf("style=warn;colour=g", Straight(10f));
            MeshBuilder yellow = Mesh(warn, SolidColour.Yellow), black = Mesh(warn, SolidColour.Black);
            Program.Check(yellow != null && black != null, "yellow and black stripes");
            Extent(yellow, out Vec3 ylo, out Vec3 yhi);
            Program.Near(yhi.Y, 0.35f, 1e-4f, "a band 350 mm high");
            Program.Check(yellow.Positions.All(v => Math.Abs(Math.Abs(v.X) - 0.033f) < 1e-4f), "a decal 3 mm proud of each face");
            Program.Check(yellow.Positions.Any(v => v.X > 0f) && yellow.Positions.Any(v => v.X < 0f), "on both faces");
            Program.Check(Math.Min(ylo.Z, black.Positions.Min(v => v.Z)) >= -1e-4f && Math.Max(yhi.Z, black.Positions.Max(v => v.Z)) <= 10f + 1e-4f, "within the piece");
            Program.Check(Math.Abs(Area(yellow) - Area(black)) < 0.1f * Area(yellow), "as much of the one as of the other: " + Area(yellow) + " and " + Area(black));
            MarkingPlan warnWhole = PlanOf("style=warn;colour=g", Straight(10f), 0f, 10f), warnA = PlanOf("style=warn;colour=g", Straight(3.37f), 0f, 10f), warnB = PlanOf("style=warn;colour=g", Straight(6.63f), 3.37f, 10f);
            Program.Near(Area(Mesh(warnA, SolidColour.Yellow)) + Area(Mesh(warnB, SolidColour.Yellow)), Area(Mesh(warnWhole, SolidColour.Yellow)), 1e-3f, "the yellow of two pieces is that of one");
            Program.Near(Area(Mesh(warnA, SolidColour.Black)) + Area(Mesh(warnB, SolidColour.Black)), Area(Mesh(warnWhole, SolidColour.Black)), 1e-3f, "and the black");
            // The stripes lean: at the band's top an edge is a band's height further along than at its foot.
            // The stripes lean: each edge at the band's top is a band's height (0.38 m: from 30 mm under the line to 350 mm) further along than at its foot, and the stripes are 150 mm across.
            float[] foot = yellow.Positions.Where(v => v.X > 0f && Math.Abs(v.Y + 0.03f) < 1e-4f && v.Z > 1f && v.Z < 9f).Select(v => v.Z).ToArray();
            float[] high = yellow.Positions.Where(v => v.X > 0f && Math.Abs(v.Y - 0.35f) < 1e-4f && v.Z > 1.4f && v.Z < 9f).Select(v => v.Z).ToArray();
            Program.Check(foot.Length > 4 && high.Length > 4 && high.All(z => foot.Any(f => Math.Abs(z - 0.38f - f) < 1e-3f)), "each stripe leans by the band's height: " + foot.Length + " foot corners, " + high.Length + " top corners");
            Program.Check(foot.Distinct().Count() > 4 && foot.Select(z => (float)Math.Round(z / 0.3f, 3)).All(q => Math.Abs(q - Math.Round(q)) < 0.01f || Math.Abs(q * 2f - Math.Round(q * 2f)) < 0.01f), "the stripes are 150 mm across, a yellow and a black in 300 mm");

            // Lamps: a lens on every post, over the post's top.
            MarkingPlan lamp = PlanOf("style=lamp;colour=b;height=200;post=3", Straight(12f));
            MeshBuilder lens = Mesh(lamp, SolidColour.GlowGold);
            Program.Check(lens != null && Mesh(lamp, SolidColour.Black) != null, "an amber lens and a black housing");
            Extent(lens, out Vec3 llo, out Vec3 lhi);
            Program.Near(lhi.Y, 2.0f + 0.05f + 0.045f + 0.065f, 1e-4f, "the lens over the post's top");
            int lenses = lens.Positions.Where(v => Math.Abs(v.Y - lhi.Y) < 1e-4f).Select(v => (int)Math.Round(v.Z * 100f)).Distinct().Count();
            Program.Check(lenses == 5, "a lens on each of five posts (12 m): " + lenses);
            Program.Check(lamp.Parts.Any(p => p.Solid == SolidColour.Blue) && lamp.Parts.Any(p => p.Solid == SolidColour.White), "the flat panel and its posts under them");

            // Gate: an opening of the chosen width in the middle, two leaves in it, two tall posts; none in a run too short.
            foreach (int width in new[] { 4, 6, 8 })
            {
                MarkingPlan gate = PlanOf("style=gate;colour=b;height=200;post=3;gate=" + width, Straight(24f));
                float g0 = 12f - width * 0.5f, g1 = 12f + width * 0.5f;
                MeshBuilder blue = Mesh(gate, SolidColour.Blue);
                Program.Check(!blue.Positions.Any(v => Math.Abs(Math.Abs(v.X) - 0.03f) < 1e-4f && v.Z > g0 + 1e-3f && v.Z < g1 - 1e-3f), width + " m: the hoarding's panel stops at the opening");
                Program.Check(blue.Positions.Any(v => Math.Abs(Math.Abs(v.X) - 0.03f) < 1e-4f && v.Z < g0 - 1f) && blue.Positions.Any(v => Math.Abs(Math.Abs(v.X) - 0.03f) < 1e-4f && v.Z > g1 + 1f), width + " m: and goes on either side");
                var leaves = blue.Positions.Where(v => Math.Abs(Math.Abs(v.X) - 0.02f) < 1e-4f).ToList();
                Program.Check(leaves.Count > 0 && leaves.Min(v => v.Z) >= g0 - 1e-3f && leaves.Max(v => v.Z) <= g1 + 1e-3f, width + " m: two leaves fill the opening");
                Program.Near(leaves.Min(v => v.Z), g0 + 0.03f, 1e-3f, width + " m: the first leaf starts at the post");
                Program.Near(leaves.Max(v => v.Z), g1 - 0.03f, 1e-3f, width + " m: the second ends at the post");
                Program.Check(!leaves.Any(v => v.Z > 11.98f && v.Z < 12.02f), width + " m: they meet in the middle");
                MeshBuilder posts = Mesh(gate, SolidColour.White);
                Program.Near(posts.Positions.Max(v => v.Y), 2.0f + 0.30f, 1e-4f, width + " m: the gate posts stand 300 mm over the hoarding");
                float[] tops = posts.Positions.Where(v => Math.Abs(v.Y - 2.3f) < 1e-4f).Select(v => (float)Math.Round(v.Z, 2)).Distinct().OrderBy(z => z).ToArray();
                Program.Check(tops.Length == 4 && Math.Abs(tops[0] - (g0 - 0.11f)) < 0.011f && Math.Abs(tops[1] - (g0 + 0.11f)) < 0.011f && Math.Abs(tops[2] - (g1 - 0.11f)) < 0.011f && Math.Abs(tops[3] - (g1 + 0.11f)) < 0.011f, width + " m: a gate post at each end of the opening: " + string.Join(",", tops));
                Program.Check(!posts.Positions.Any(v => Math.Abs(v.Y + Barriers.Embed) < 1e-4f && v.Z > g0 + 0.2f && v.Z < g1 - 0.2f), width + " m: no hoarding post in the opening");
            }

            MarkingPlan none = PlanOf("style=gate;gate=6", Straight(6f));
            Program.Check(Mesh(none, SolidColour.White).Positions.All(v => v.Y < 2.1f), "a run of 6 m has no room for a 6 m gate: no tall post");
            // Pieces: the leaves and the gate posts belong to the piece that holds their start, the rest is in step.
            MarkingPlan whole = PlanOf("style=gate;gate=6", Straight(24f), 0f, 24f), a = PlanOf("style=gate;gate=6", Straight(10f), 0f, 24f), b = PlanOf("style=gate;gate=6", Straight(14f), 10f, 24f);
            Program.Near(TotalArea(a, b), TotalArea(whole), 0.02f, "a gate across two pieces is the gate of one");
            MarkingPlan c = PlanOf("style=gate;gate=6", Straight(13f), 0f, 24f), d = PlanOf("style=gate;gate=6", Straight(11f), 13f, 24f);
            Program.Near(TotalArea(c, d), TotalArea(whole), 0.02f, "and when the cut falls inside the opening");
        }

        // ------------------------------------------------------------------ pieces and bends

        private static void Pieces()
        {
            foreach (string style in kStyles)
            {
                MarkingPlan whole = PlanOf("style=" + style, Straight(20f), 0f, 20f);
                foreach (float cut in new[] { 8.3f, 6f, 9.99f })
                {
                    MarkingPlan a = PlanOf("style=" + style, Straight(cut), 0f, 20f), b = PlanOf("style=" + style, Straight(20f - cut), cut, 20f);
                    float one = TotalArea(whole), two = TotalArea(a, b);
                    Program.Check(Math.Abs(one - two) < 0.01f * one, $"{style}, cut at {cut}: the surface of two pieces is that of one: {one:0.000} and {two:0.000}");
                    Program.Check(Vertices(a) + Vertices(b) >= Vertices(whole) - 8, $"{style}, cut at {cut}: nothing is lost at the join");
                }
            }
        }

        private static void Bends()
        {
            var lines = new List<List<Vec3>>
            {
                new List<Vec3> { P(0f, 0f), P(0f, 5f), P(4f, 9f), P(9f, 9f) },
                new List<Vec3> { P(0f, 0f, 0f), P(0f, 6f, 0.4f), P(1f, 12f, 0.1f) },
                new List<Vec3> { P(0f, 0f), P(0f, 2.5f), P(0.3f, 4f), P(2f, 4.2f) },
                Straight(1f), Straight(0.5f), Straight(0.05f),
                new List<Vec3> { P(0f, 0f), P(0f, 0f) }, new List<Vec3> { P(0f, 0f) }
            };
            foreach (string style in kStyles)
            {
                foreach (string extra in new[] { "", ";flowers=foot", ";flowers=top;side=b" })
                {
                    foreach (List<Vec3> line in lines)
                    {
                        MarkingPlan plan = null;
                        try
                        {
                            plan = PlanOf("style=" + style + extra, line);
                        }
                        catch (Exception e)
                        {
                            Program.Check(false, $"{style}{extra}: {line.Count} points, {line[line.Count - 1]}: threw {e.GetType().Name} {e.Message}");
                        }

                        if (plan != null)
                        {
                            Program.Check(Finite(plan), $"{style}{extra}: {line.Count} points, {line[line.Count - 1]}: finite");
                            Program.Check(plan.Parts.All(p => Wound(p.Mesh)), $"{style}{extra}: {line.Count} points, {line[line.Count - 1]}: facing its normals");
                        }
                    }
                }
            }

            // A bent run follows the bend: the steel sheet's corner is mitred, its depth keeps in step either side, and it stays 10 mm thick across.
            MarkingPlan bend = PlanOf("style=steel;colour=b", lines[0]);
            MeshBuilder sheet = Mesh(bend, SolidColour.Blue);
            Program.Check(sheet.Positions.Any(v => v.Z > 8.9f && v.X > 8f) && sheet.Positions.Any(v => v.Z < 0.1f), "the sheet runs along the bent line");
            Program.Check(Wound(sheet) && OpenEdges(sheet).All(e => Math.Abs(e.A.Y - e.B.Y) < 1e-4f), "and is open only along its top and its foot");
        }

        // ------------------------------------------------------------------ flowers

        private static void Flowers()
        {
            foreach (string style in new[] { "flat" }.Concat(kStyles))
            {
                MarkingPlan none = PlanOf("style=" + style, Straight(10f));
                Program.Check(none.Props.Count == 0 && none.BakedProps.Count == 0 && Mesh(none, SolidColour.ResinGreen) == null && Mesh(none, SolidColour.Soil) == null, style + ": no flowers, no troughs, no plants by default");
            }

            // Foot troughs: 1.5 x 0.35 x 0.32 m every 2 m: five in 10 m, one plant each.
            MarkingPlan foot = PlanOf("style=flat;flowers=foot;side=r", Straight(10f));
            Program.Check(foot.Props.Count == 5, "five plants in 10 m (five troughs of one): " + foot.Props.Count);
            Program.Check(foot.Props.All(p => p.Kind == GreenPlants.Flower && p.Room > 0f), "flowering bushes, each with its room");
            MeshBuilder trough = Mesh(foot, SolidColour.ResinGreen);
            Extent(trough, out Vec3 tlo, out Vec3 thi);
            Program.Near(thi.Y, 0.32f, 1e-4f, "a trough 320 mm high");
            Program.Near(tlo.X, 0.065f, 1e-4f, "on the right, clear of the posts");
            Program.Near(thi.X - tlo.X, 0.35f, 1e-4f, "350 mm wide");
            Program.Check(Mesh(foot, SolidColour.Soil) != null && Mesh(foot, SolidColour.Soil).Positions.Max(v => v.Y) < 0.32f - 0.039f, "soil 4 cm under the rim");
            Program.Check(foot.Props.All(p => p.Local.Y > 0.25f && p.Local.Y < 0.32f && p.Local.X > 0.06f && p.Local.X < 0.42f), "the plants stand in the troughs");
            Program.Check(foot.Props.Select(p => (float)Math.Round(p.Local.Z, 3)).SequenceEqual(new[] { 0.75f, 2.75f, 4.75f, 6.75f, 8.75f }), "one in the middle of each trough: " + string.Join(",", foot.Props.Select(p => Math.Round(p.Local.Z, 2))));
            Program.Check(PlanOf("style=flat;flowers=foot;plants=shrub", Straight(10f)).Props.All(p => p.Kind == GreenPlants.Shrub), "evergreen shrubs when asked for");
            Extent(Mesh(PlanOf("style=flat;flowers=foot;side=l", Straight(10f)), SolidColour.ResinGreen), out Vec3 llo, out Vec3 lhi);
            Program.Check(lhi.X < -0.06f && llo.X < -0.4f, "the left side");
            MarkingPlan both = PlanOf("style=flat;flowers=foot;side=b", Straight(10f));
            Program.Check(both.Props.Count == 10 && Mesh(both, SolidColour.ResinGreen).Positions.Any(v => v.X > 0.3f) && Mesh(both, SolidColour.ResinGreen).Positions.Any(v => v.X < -0.3f), "both sides: ten plants");

            // Hung troughs: 1.0 x 0.22 x 0.22 m under the top, two brackets each, 1.5 m apart: seven in 10 m.
            MarkingPlan top = PlanOf("style=flat;flowers=top;height=200", Straight(10f));
            Program.Check(top.Props.Count == 7, "seven plants in 10 m (seven troughs of one): " + top.Props.Count);
            MeshBuilder hung = Mesh(top, SolidColour.ResinGreen);
            Extent(hung, out Vec3 hlo, out Vec3 hhi);
            Program.Near(hhi.Y, 2.0f - 0.08f, 1e-4f, "hung 80 mm under the top");
            Program.Near(hhi.Y - hlo.Y, 0.22f, 1e-4f, "220 mm high");
            Program.Check(hlo.X >= 0.055f - 1e-4f, "clear of the posts' faces");
            Program.Check(Mesh(top, SolidColour.Metal) != null && Mesh(top, SolidColour.Metal).VertexCount == 7 * 2 * 20, "two brackets to a trough: " + Mesh(top, SolidColour.Metal)?.VertexCount);
            Program.Check(top.Props.All(p => p.Local.Y > 1.65f), "the plants are up in the troughs");

            // Troughs clear the body of every style: none touches a post, a foot block, a kerb or a footing.
            foreach (string style in kStyles.Where(s => s != "gate"))
            {
                MarkingPlan plan = PlanOf("style=" + style + ";flowers=foot;height=200", Straight(12f));
                MeshBuilder walls = Mesh(plan, SolidColour.ResinGreen);
                float inner = walls.Positions.Min(v => v.X);
                float body = plan.Parts.Where(p => p.Solid != SolidColour.ResinGreen && p.Solid != SolidColour.Soil).Max(p => p.Mesh.Positions.Where(v => v.Y < 0.4f).DefaultIfEmpty(Vec3.Zero).Max(v => v.X));
                Program.Check(inner >= body + 0.01f, $"{style}: the trough's inner wall at {inner:0.000} is clear of the body at {body:0.000}");
                Program.Check(plan.Props.Count == 6 && plan.Props.All(p => p.Local.X > inner), style + ": six plants in 12 m, in the troughs: " + plan.Props.Count);
            }

            // The gate's opening has no trough in it or by it.
            MarkingPlan gate = PlanOf("style=gate;gate=6;flowers=foot", Straight(24f));
            MeshBuilder gw = Mesh(gate, SolidColour.ResinGreen);
            Program.Check(!gw.Positions.Any(v => v.Z > 9f - 0.3f + 1e-3f && v.Z < 15f + 0.3f - 1e-3f), "no trough in the gate's opening or within 300 mm of it");
            Program.Check(gate.Props.Count > 0 && gate.Props.All(p => p.Local.Z < 8.8f || p.Local.Z > 15.2f), "and no plant there");

            // Two pieces: a trough belongs to the piece holding its middle; the whole has as many troughs as the pieces together.
            MarkingPlan whole = PlanOf("style=steel;flowers=foot;side=b", Straight(20f), 0f, 20f);
            foreach (float cut in new[] { 8.3f, 1.75f, 2.75f, 4.75f })
            {
                MarkingPlan a = PlanOf("style=steel;flowers=foot;side=b", Straight(cut), 0f, 20f), b = PlanOf("style=steel;flowers=foot;side=b", Straight(20f - cut), cut, 20f);
                Program.Check(a.Props.Count + b.Props.Count == whole.Props.Count, $"cut at {cut}: {a.Props.Count} + {b.Props.Count} plants as in the whole {whole.Props.Count}");
                Program.Near(TotalArea(a, b), TotalArea(whole), 0.02f, $"cut at {cut}: the troughs and the steel sheet add up");
            }

            // The numbers for the report: entities per 10 m.
            Console.WriteLine($"HOARDING-NUMBERS plants/10 m: foot one side {foot.Props.Count}, foot both {both.Props.Count}, top one side {top.Props.Count}, top both {PlanOf("style=flat;flowers=top;side=b", Straight(10f)).Props.Count}");
            var hundred = new List<Vec3>();
            for (float s = 0f; s <= 100f; s += 4f)
                hundred.Add(P(0f, s));
            foreach (string style in new[] { "flat" }.Concat(kStyles))
            {
                MarkingPlan ten = PlanOf("style=" + style, Straight(10f), 0f, 10f);
                string line = $"HOARDING-NUMBERS {style}: vertices/10 m {Vertices(ten)}, triangles/10 m {ten.Parts.Sum(p => p.Mesh.TriangleCount)}, with foot troughs one side {Vertices(PlanOf("style=" + style + ";flowers=foot", Straight(10f), 0f, 10f))}";
                foreach (string extra in new[] { "", ";flowers=foot;side=b" })
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    MarkingPlan big = PlanOf("style=" + style + extra, hundred, 0f, 100f);
                    line += $"; 100 m{(extra == "" ? "" : " with troughs both sides")}: {clock.Elapsed.TotalMilliseconds:0.0} ms, {Vertices(big)} vertices, {big.Props.Count} plants";
                }

                Console.WriteLine(line);
            }
        }
    }
}
