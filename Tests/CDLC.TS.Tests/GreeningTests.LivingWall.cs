using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The vertical greening wall (Barriers.LivingWall, MarkingTemplates.LivingWall): in the catalogue and the greening tab with levels of detail but no horizon tier, sound closed bodies on a straight, a
    /// bend and a slope, grounded and plumb, the sizes its options give, the plants the game's and thinned for a long run, the pieces of a run in step, and the three levels with one footprint.
    /// </summary>
    internal static partial class GreeningTests
    {
        private const string kWall = MarkingTemplates.LivingWallId;

        private static readonly string[] kWallVariants =
        {
            "", "style=s", "style=t", "style=s;thick=60;faces=2;height=300", "style=t;faces=2;height=150", "height=150;gap=70", "height=200;gap=50;thick=30", "where=m",
            "where=l;style=s;thick=30", "plants=m;post=3", "plants=s;post=2", "where=l;faces=2;style=t;thick=60"
        };

        private static void RunLivingWall()
        {
            Program.Run("living wall: the catalogue, its tab, levels without a horizon tier", WallCatalogue);
            Program.Run("living wall: closed bodies on a straight, a bend and a slope, finite, outward normals", WallBodies);
            Program.Run("living wall: grounded on a slope, plumb, the planted face toward the traffic", WallGround);
            Program.Run("living wall: the sizes the options give (height, thickness, troughs, posts)", WallSizes);
            Program.Run("living wall: the game's plants, thinned for a long run, in step across pieces", WallPlants);
            Program.Run("living wall: three levels with one footprint, the vertices of each", WallLevels);
        }

        private static MarkingPlan WallPlan(string settings, List<Vec3> line, int level = BarrierLod.Full, float phase = 0f, float total = 0f)
        {
            GuideTemplate t = MarkingTemplates.ById(kWall);
            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, WallSpec(settings).ToString(), RunPath.Encode(line, out _), phase, total), level);
        }

        /// <summary>A line every 5 m, as a run is cut for a wall (the template resolves a run at about 8 m).</summary>
        private static List<Vec3> Every5(float length, float grade = 0f)
        {
            var line = new List<Vec3>();
            for (float z = 0f; z < length - 1e-3f; z += 5f)
                line.Add(P(0f, z, z * grade));
            line.Add(P(0f, length, length * grade));
            return line;
        }

        /// <summary>The open edges (used an odd number of times, corners welded within 0.1 mm) of a mesh, as their two corners.</summary>
        private static List<(Vec3 A, Vec3 B)> OpenEdgeList(MeshBuilder mesh)
        {
            var ids = new Dictionary<(long, long, long), int>();
            var points = new List<Vec3>();
            int Id(Vec3 p)
            {
                var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
                if (!ids.TryGetValue(key, out int id))
                {
                    ids[key] = id = ids.Count;
                    points.Add(p);
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

            return edges.Where(e => e.Value % 2 == 1).Select(e => (points[e.Key.Item1], points[e.Key.Item2])).ToList();
        }

        /// <summary>The height of a line at the place of a point (its nearest segment in plan, interpolated).</summary>
        private static float GroundAt(List<Vec3> line, Vec3 p)
        {
            float best = float.MaxValue, y = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                float dx = line[i].X - line[i - 1].X, dz = line[i].Z - line[i - 1].Z, length2 = dx * dx + dz * dz;
                float t = length2 < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - line[i - 1].X) * dx + (p.Z - line[i - 1].Z) * dz) / length2));
                float ex = line[i - 1].X + dx * t - p.X, ez = line[i - 1].Z + dz * t - p.Z, d = ex * ex + ez * ez;
                if (d < best)
                {
                    best = d;
                    y = line[i - 1].Y + (line[i].Y - line[i - 1].Y) * t;
                }
            }

            return y;
        }

        private static GuideSpec WallSpec(string settings)
        {
            GuideSpec spec = MarkingTemplates.ById(kWall).Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                spec = spec.With(pair.Substring(0, pair.IndexOf('=')), pair.Substring(pair.IndexOf('=') + 1));
            return spec;
        }

        private static int Vertices(MarkingPlan plan) => plan.Parts.Sum(p => p.Mesh.VertexCount);

        private static void WallCatalogue()
        {
            SignDef def = SignCatalog.ById(kWall);
            SignChoice choice = SignCatalog.ChoiceById(kWall);
            Program.Check(def != null && def.IsMarking && def.Group == MarkingTemplates.BarrierGroup && MarkingTemplates.IsRun(def.Template), "in the catalogue, in the barriers' group, drawn along a road");
            Program.Check(choice != null && choice.Series == MarkingTemplates.GreenSeries, "listed in the greening tab");
            Program.Check(MarkingGround.Lies(def) && def.Template.FieldOf(BarrierAuto.JunctionKey) != null, "laid on the ground, with the barriers' junction setting");
            Program.Check(Array.IndexOf(MarkingTemplates.PlantedIds, kWall) >= 0, "among the planted templates (PlantSystem puts the game's plants at its anchors)");
            Program.Check(Array.IndexOf(MarkingTemplates.LeveledIds, kWall) >= 0 && Array.IndexOf(MarkingTemplates.LongReachIds, kWall) < 0 && !LongReach.Is(SignCatalog.HashOf(kWall)), "levels of detail, no horizon tier (a wall of 3 m is a speck at 3 km)");
            Program.Check(MarkingTemplates.LongReachIds.Length == 5, "the noise walls are still the five of the horizon tier");
            foreach (string key in new[] { "height", "style", "thick", "gap", "post", "faces", "bloom", "plants", "top", "where", "off" })
                Program.Check(def.Template.FieldOf(key) != null, "has the field " + key);
            Program.Check(MarkingTemplates.PlanOf(def.Template, def.Template.Default().ToString(), BarrierLod.Coarse) != MarkingTemplates.PlanOf(def.Template, def.Template.Default().ToString(), BarrierLod.Full), "the levels are different plans");
            Program.Check(MarkingTemplates.PlanOf(SignCatalog.ById(MarkingTemplates.ViaductPlanterId).Template, "", BarrierLod.Coarse) == MarkingTemplates.PlanOf(SignCatalog.ById(MarkingTemplates.ViaductPlanterId).Template, "", BarrierLod.Full), "a template that is not leveled keeps its one plan at every level");
        }

        private static void WallBodies()
        {
            var lines = new (string What, List<Vec3> Line, bool Buried)[]
            {
                ("straight", Every5(12f), true), ("slope 10%", Every5(20f, 0.1f), true), ("arc right", Arc(20f, 16f, true), false), ("arc left", Arc(20f, 16f, false), false), ("arc right on a slope", Arc(20f, 16f, true, 0.06f), false)
            };
            foreach (string variant in kWallVariants)
            {
                foreach ((string what, List<Vec3> line, bool buried) in lines)
                {
                    MarkingPlan plan = WallPlan(variant, line);
                    string name = $"{variant} on {what}";
                    Program.Check(plan.Parts.Count >= 6, name + ": its parts");
                    foreach (MarkingPart part in plan.Parts)
                    {
                        MeshBuilder mesh = part.Mesh;
                        bool finite = mesh.Positions.TrueForAll(p => !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z)) && mesh.Normals.TrueForAll(n => !float.IsNaN(n.X + n.Y + n.Z));
                        Program.Check(finite, $"{name}: {part.Solid} finite");
                        Program.Check(Wound(mesh), $"{name}: {part.Solid} faces outward");
                        List<(Vec3 A, Vec3 B)> open = OpenEdgeList(mesh);
                        if (part.Solid == SolidColour.Metal)
                        {
                            // The posts have no bottom: their open edges are the buried ones, 0.25 m under the ground.
                            Program.Check(open.All(e => e.A.Y < GroundAt(line, e.A) - 0.2f && Math.Abs(e.A.Y - e.B.Y) < 1e-4f), $"{name}: the posts' open edges are at their buried feet");
                        }
                        else if (part.Solid == SolidColour.DarkGray)
                        {
                            // The trellis's wires have no bottom: it stands in the soil of the foot trough, 0.36 m over the ground.
                            // Across a bend the nearest point of the line is not the wire's own station: up to the slope times the wire's offset (0.35 m) of difference in the test's ground.
                            float slack = what.StartsWith("arc", StringComparison.Ordinal) ? 0.025f : 0.01f;
                            Program.Check(open.All(e => Math.Abs(e.A.Y - (GroundAt(line, e.A) + Barriers.WallPlinth + Barriers.WallTrough)) < slack && Math.Abs(e.A.Y - e.B.Y) < 1e-4f), $"{name}: the wires' open edges are in the foot trough's soil");
                        }
                        else
                        {
                            Program.Check(open.Count == 0, $"{name}: {part.Solid} closed ({open.Count} open edges)");
                        }
                    }
                }
            }
        }

        private static void WallGround()
        {
            MarkingPlan plan = WallPlan("", Every5(20f, 0.1f));
            MarkingPart concrete = Part(plan, SolidColour.Concrete, DetailKind.Concrete), metal = Part(plan, SolidColour.Metal);
            float footStart = concrete.Mesh.Positions.Where(p => p.Z < 0.01f).Min(p => p.Y), footEnd = concrete.Mesh.Positions.Where(p => p.Z > 19.99f).Min(p => p.Y);
            Program.Check(Math.Abs(footStart + Barriers.Embed) < 1e-3f && Math.Abs(footEnd - (2f - Barriers.Embed)) < 1e-3f, $"the plinth's foot 0.25 m into the ground at both ends of a 10 % slope: {footStart}, {footEnd}");
            float topStart = metal.Mesh.Positions.Where(p => p.Z < 0.01f).Max(p => p.Y), topEnd = metal.Mesh.Positions.Where(p => p.Z > 19.99f).Max(p => p.Y);
            Program.Check(Math.Abs(topStart - 2.5f) < 1e-3f && Math.Abs(topEnd - 4.5f) < 1e-3f, $"the cap 2.5 m over the ground at both ends: {topStart}, {topEnd}");
            Bounds(metal.Mesh.Positions.Where(p => p.Z > 19.99f && p.Y > 4.3f), out Vec3 lo, out Vec3 hi);
            Bounds(metal.Mesh.Positions.Where(p => p.Z < 0.01f && p.Y > 2.3f), out Vec3 lo0, out Vec3 hi0);
            Program.Check(Math.Abs(lo.X - lo0.X) < 1e-3f && Math.Abs(hi.X - hi0.X) < 1e-3f, "the section is the same at both ends of the slope (plumb)");

            // The planted face is the one toward the traffic: the right of the line by default, the left for where=l, both for a median.
            foreach ((string settings, float sign) in new[] { ("where=r", 1f), ("where=l", -1f), ("where=m", 0f) })
            {
                MarkingPlan p = WallPlan(settings, Straight(8f));
                float body = Part(p, SolidColour.Concrete, DetailKind.Concrete).Mesh.Positions.Where(q => q.Y > 0.5f).Average(q => q.X);
                float skin = p.Parts.Where(q => q.Detail == DetailKind.Foliage).SelectMany(q => q.Mesh.Positions).Average(q => q.X);
                Program.Check(sign == 0f ? Math.Abs(skin - body) < 1e-3f : (skin - body) * sign > 0.05f, $"{settings}: the foliage on the traffic's side ({skin:0.000} against the body's {body:0.000})");
                float footX = Foot(p).Average(q => q.Local.X);
                Program.Check(sign == 0f ? Math.Abs(footX) < 1e-3f : footX * sign > 0.1f, $"{settings}: the foot trough's plants on the same side: {footX:0.000}");
            }
        }

        private static void WallSizes()
        {
            foreach (string variant in kWallVariants)
            {
                MarkingPlan plan = WallPlan(variant, Straight(10f));
                GuideSpec spec = WallSpec(variant);
                float height = float.Parse(spec.Get("height"), System.Globalization.CultureInfo.InvariantCulture) / 100f, pitch = float.Parse(spec.Get("gap"), System.Globalization.CultureInfo.InvariantCulture) / 100f;
                MarkingPart plinth = Part(plan, SolidColour.Concrete, DetailKind.Concrete), metal = Part(plan, SolidColour.Metal), trough = Part(plan, SolidColour.ResinGreen);
                Program.Check(Math.Abs(MaxY(metal) - height) < 1e-3f, $"{variant}: {height} m high");
                var layout = Barriers.WallLayout(new LivingWallOptions
                {
                    Height = height, Thickness = float.Parse(spec.Get("thick"), System.Globalization.CultureInfo.InvariantCulture) / 100f, TroughPitch = pitch,
                    Style = spec.Is("style", "s") ? WallStyle.Stepped : spec.Is("style", "t") ? WallStyle.Trellis : WallStyle.Flush, BothFaces = spec.Is("where", "m") || spec.Is("faces", "2")
                }, spec.Is("where", "l") ? -1 : 1);
                Bounds(plinth.Mesh.Positions.Where(p => p.Y < 0.1f), out Vec3 lo, out Vec3 hi);
                Program.Check(Math.Abs((hi.X - lo.X) - (layout.Width + 0.06f)) < 1e-3f, $"{variant}: the footprint {hi.X - lo.X:0.000} m is the layout's {layout.Width + 0.06f:0.000}");
                float asked = float.Parse(spec.Get("thick"), System.Globalization.CultureInfo.InvariantCulture) / 100f;
                Program.Check(layout.Width >= asked - 1e-3f || spec.Is("style", "t"), $"{variant}: at least the thickness asked for ({layout.Width:0.000} of {asked})");
                if (spec.Is("style", "f"))
                {
                    // A flush wall is what was asked: its body is what the troughs and the foliage leave, unless that is under 0.10 m (the body's least), then the wall is that much thicker.
                    float faces = layout.Faces, least = 0.10f + faces * (Barriers.WallSkin + 0.15f);
                    Program.Check(Math.Abs(layout.Width - Math.Max(asked, least)) < 1e-3f, $"{variant}: a flush wall is {Math.Max(asked, least):0.000} m thick, built {layout.Width:0.000}");
                }
                Program.Check(layout.Width <= 0.8f, $"{variant}: no more than 0.8 m thick ({layout.Width:0.000})");
                int rows = Intervals(trough.Mesh.Positions.Select(p => p.Y), 0.2f).Count;
                Program.Check(rows == layout.Rows.Count, $"{variant}: {layout.Rows.Count} troughs, {rows} found in the mesh");
                Program.Check(spec.Is("style", "t") ? layout.Rows.Count == 1 : layout.Rows.Count >= 2 && layout.Rows.Count <= 6, $"{variant}: the rows' number {layout.Rows.Count}");
                for (int k = 1; k < layout.Rows.Count; k++)
                    Program.Check(Math.Abs(layout.Rows[k].Y - layout.Rows[k - 1].Y - pitch) < 1e-4f, $"{variant}: row {k} a pitch over the one before");
                Program.Check(layout.Rows.All(r => r.Y + Barriers.WallTrough + 0.03f <= height - Barriers.WallCap - 0.0f), $"{variant}: every trough and its soil clear of the cap");
                if (spec.Is("style", "s"))
                {
                    for (int k = 1; k < layout.Rows.Count; k++)
                        Program.Check(layout.Rows[k].Depth < layout.Rows[k - 1].Depth, $"{variant}: row {k} steps back");
                }
                else if (spec.Is("style", "f"))
                {
                    Program.Check(layout.Rows.All(r => Math.Abs(r.Depth - layout.Rows[0].Depth) < 1e-4f), $"{variant}: flush troughs are of one depth");
                }
            }

            // The posts every PostSpacing of the whole run (and at its far end).
            foreach (float spacing in new[] { 2f, 2.5f, 3f })
            {
                MarkingPlan plan = WallPlan($"post={spacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}", Every5(12f));
                MarkingPart metal = Part(plan, SolidColour.Metal);
                int posts = Intervals(metal.Mesh.Positions.Where(p => p.Y < -0.2f).Select(p => p.Z), 0.2f).Count;
                int expected = (int)Math.Floor(12f / spacing + 1e-4f) + 1 + (12f - Math.Floor(12f / spacing + 1e-4f) * spacing > spacing * 0.25f ? 1 : 0);
                Program.Check(posts == expected, $"posts every {spacing} m in 12 m: {posts} of {expected}");
            }
        }

        private static List<PropAnchor> Foot(MarkingPlan plan) => plan.BakedProps.Where(p => p.Kind != GreenPlants.Pot).ToList();

        private static List<PropAnchor> Pots(MarkingPlan plan) => plan.BakedProps.Where(p => p.Kind == GreenPlants.Pot).ToList();

        private static void WallPlants()
        {
            MarkingPlan one = WallPlan("", Straight(40f)), both = WallPlan("faces=2", Straight(40f));
            Program.Check(Foot(one).Count == 37 && Foot(both).Count == 74, $"one bush every 1.1 m of 40 m on a face: {Foot(one).Count}, two faces {Foot(both).Count}");
            Program.Check(Pots(one).Count == 27 && Pots(both).Count == 27, $"a pot every 1.5 m on the cap, one row whatever the faces: {Pots(one).Count} and {Pots(both).Count}");
            foreach (PropAnchor p in Foot(both))
            {
                Program.Check(p.Kind == GreenPlants.Flower && p.Room > 0f && p.Room <= 0.5f, "a flowering bush with room for one");
                Program.Check(Math.Abs(p.Local.X) < 0.4f && Math.Abs(p.Local.Y - (Barriers.WallPlinth + Barriers.WallTrough + 0.01f)) < 1e-3f, $"on the soil of the foot trough, inside the footprint: {p.Local}");
            }

            foreach (PropAnchor p in Pots(both))
            {
                Program.Check(Math.Abs(p.Local.X) < 1e-3f && Math.Abs(p.Local.Y - 2.5f) < 1e-3f && Math.Abs(p.Room - Barriers.WallPotRoom) < 1e-6f, $"a pot in the middle of the cap, 2.5 m up: {p.Local}");
                Program.Check(GreenPlants.PrefabsOf(p.Kind).Count == 2 && GreenPlants.PrefabsOf(p.Kind)[0] == "FlowerPot02", "the pot kind has the game's two pots, the narrow one first");
            }

            // PlantSystem.Choose takes the copies up to three times the room wide, else the narrowest alone: FlowerPot02 is 0.52 m across and FlowerPot01 0.84 m (the dump), so a room over 0.17 m would let the wide pot in.
            Program.Check(Barriers.WallPotRoom * 3f < 0.52f + 0.02f && Barriers.WallPotRoom * 3f < 0.84f, $"the pots' room keeps the narrow pot only: {Barriers.WallPotRoom * 3f} m against 0.84 m");
            Program.Check(Pots(WallPlan("top=0", Straight(40f))).Count == 0 && Foot(WallPlan("top=0", Straight(40f))).Count == 37, "the toggle takes the pots away and leaves the foot trough's plants");
            Program.Check(Pots(WallPlan("", Every5(20f, 0.1f))).All(p => Math.Abs(p.Local.Y - (2.5f + p.Local.Z * 0.1f)) < 1e-3f), "the pots stand on the cap on a slope");
            Program.Check(Pots(one).Count * 1.5f - 40f < 1.5f && one.BakedProps.Count / 4f < 17f, $"entities per 10 m: {one.BakedProps.Count / 4f:0.0} on one face, {both.BakedProps.Count / 4f:0.0} on both");

            MarkingPlan mixed = WallPlan("plants=m", Straight(33f));
            int shrubs = Foot(mixed).Count(p => p.Kind == GreenPlants.Shrub);
            Program.Check(shrubs * 3 >= Foot(mixed).Count - 3 && shrubs * 3 <= Foot(mixed).Count + 3 && Foot(mixed).Any(p => p.Kind == GreenPlants.Flower), $"roses and shrubs 2:1: {shrubs} of {Foot(mixed).Count}");
            Program.Check(Foot(WallPlan("plants=s", Straight(10f))).All(p => p.Kind == GreenPlants.Shrub), "shrubs only");

            // A long run is thinned: the caps hold however long it is and however many faces.
            foreach (float length in new[] { 100f, 400f, 1000f })
            {
                MarkingPlan plan = WallPlan("faces=2", Straight(length), BarrierLod.Full, 0f, length);
                Program.Check(Foot(plan).Count <= Barriers.WallPlantCap + 2 && Foot(plan).Count >= 20 && Pots(plan).Count <= Barriers.WallPotCap + 1 && Pots(plan).Count >= 10, $"{length} m, two faces: {Foot(plan).Count} bushes (cap {Barriers.WallPlantCap}) and {Pots(plan).Count} pots (cap {Barriers.WallPotCap})");
            }

            // Two pieces of one run: the plants of the whole, each once.
            MarkingPlan whole = WallPlan("faces=2", Straight(40f), BarrierLod.Full, 0f, 40f);
            MarkingPlan a = WallPlan("faces=2", Straight(20f), BarrierLod.Full, 0f, 40f), b = WallPlan("faces=2", Straight(20f), BarrierLod.Full, 20f, 40f);
            var together = new List<Vec3>(a.BakedProps.ConvertAll(p => p.Local));
            together.AddRange(b.BakedProps.ConvertAll(p => p.Local + new Vec3(0f, 0f, 20f)));
            Program.Check(together.Count == whole.BakedProps.Count && whole.BakedProps.TrueForAll(p => together.Exists(q => (q - p.Local).Length < 1e-3f)), $"two pieces: the plants of the whole ({together.Count} of {whole.BakedProps.Count})");
        }

        private static void WallLevels()
        {
            foreach (string variant in new[] { "", "style=s", "style=t", "faces=2;height=300" })
            {
                int previous = int.MaxValue;
                Vec3 lo0 = default, hi0 = default;
                var counts = new List<int>();
                for (int level = BarrierLod.Full; level <= BarrierLod.Coarse; level++)
                {
                    MarkingPlan plan = WallPlan(variant, Every5(10f), level);
                    int vertices = Vertices(plan);
                    counts.Add(vertices);
                    Program.Check(vertices < previous && vertices > 0, $"{variant} level {level}: {vertices} vertices, fewer than the level before");
                    previous = vertices;
                    int Of(SolidColour colour) => plan.Parts.Where(p => p.Solid == colour).Sum(p => p.Mesh.VertexCount);
                    bool full = level == BarrierLod.Full;
                    Program.Check((Of(SolidColour.Soil) > 0) == full && (Of(SolidColour.DarkGray) > 0) == full && (Of(SolidColour.ResinGreen) > 0) == (level != BarrierLod.Coarse), $"{variant} level {level}: soil and the drip pipe (and the trellis) at the full level only, the troughs down to the reduced one");
                    foreach (MarkingPart part in plan.Parts)
                        Program.Check(Wound(part.Mesh) && OpenEdgeList(part.Mesh).All(e => e.A.Y <= Barriers.WallPlinth + Barriers.WallTrough + 0.01f && Math.Abs(e.A.Y - e.B.Y) < 1e-4f), $"{variant} level {level}: {part.Solid} sound (open edges only at the posts' and wires' feet)");
                    Bounds(plan.Parts.SelectMany(p => p.Mesh.Positions).Where(p => p.Y > 0f), out Vec3 lo, out Vec3 hi);
                    if (level == BarrierLod.Full)
                    {
                        lo0 = lo;
                        hi0 = hi;
                        Program.Check(plan.Parts.Sum(p => p.Mesh.VertexCount) <= 2000 || variant.Contains("style=t"), $"{variant} level 0: {vertices} vertices in 10 m (the budget is about 2,000)");
                    }
                    else
                    {
                        Program.Check(Math.Abs(hi.Y - hi0.Y) < 1e-3f && Math.Abs(hi.X - hi0.X) < 0.05f && Math.Abs(lo.X - lo0.X) < 0.05f, $"{variant} level {level}: the same height and footprint as the full level");
                    }
                }

                Program.Check(true, $"{variant}: vertices per 10 m at the three levels: {counts[0]} / {counts[1]} / {counts[2]}");
            }

            MarkingPlan far = WallPlan("", Every5(10f), BarrierLod.Coarse);
            List<MarkingPart> drawn = far.Parts.Where(p => p.Mesh.VertexCount > 0).ToList();
            Program.Check(drawn.All(p => p.Solid == SolidColour.Concrete || p.Solid == SolidColour.Green || p.Solid == SolidColour.BlossomMixed || p.Solid == SolidColour.Metal), "the far level: the body, a green slab, a band and the cap only");
            Program.Check(drawn.Any(p => p.Solid == SolidColour.BlossomMixed) && drawn.Where(p => p.Solid == SolidColour.Green).All(p => p.Detail == DetailKind.Painted) && far.BakedProps.Count == 0, "the far slab has a band, no foliage detail and no plants");
        }
    }
}
