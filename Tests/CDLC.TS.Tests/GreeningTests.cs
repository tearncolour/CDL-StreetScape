using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The road greening of 0.12.49 (Barriers.Greening, Barriers.Viaduct, MarkingTemplates.Greening, GreenPlants): in the catalogue and its own tab, sound closed bodies, the sizes the clauses set, the
    /// pits' grates and guards, the belts' plants inside them and in step along a run, the viaduct's boxes held by the wall and clear of it on a straight, a bend and a slope, the run's ends finished, the
    /// plants the game's own (no modelled foliage) and each inside its pit or planter.
    /// </summary>
    internal static partial class GreeningTests
    {
        private static readonly string[] kIds = { MarkingTemplates.TreePitId, MarkingTemplates.GreenBeltId, MarkingTemplates.ViaductPlanterId, MarkingTemplates.FlowerBowlId };

        /// <summary>The settings each template is checked with besides its defaults: every form, cover, mount and plant.</summary>
        private static readonly (string Id, string Settings)[] kVariants =
        {
            (MarkingTemplates.TreePitId, ""), (MarkingTemplates.TreePitId, "cover=resin;guard=1"), (MarkingTemplates.TreePitId, "cover=frp;size=150"), (MarkingTemplates.TreePitId, "cover=none;tree=palm"),
            (MarkingTemplates.TreePitId, "kerb=15;guard=1;stone=c"), (MarkingTemplates.TreePitId, "size=s200;under=flower"), (MarkingTemplates.TreePitId, "size=s150;kerb=10;under=none"),
            (MarkingTemplates.GreenBeltId, ""), (MarkingTemplates.GreenBeltId, "width=150;edge=k20"), (MarkingTemplates.GreenBeltId, "width=400;tree=plane"), (MarkingTemplates.GreenBeltId, "width=300;under=shrubs;tree=linden;treegap=6"),
            (MarkingTemplates.GreenBeltId, "under=flowers;edge=low"), (MarkingTemplates.GreenBeltId, "edge=none;under=none;tree=poplar"), (MarkingTemplates.GreenBeltId, "where=r;off=50"),
            (MarkingTemplates.ViaductPlanterId, ""), (MarkingTemplates.ViaductPlanterId, "mount=saddle;length=200"), (MarkingTemplates.ViaductPlanterId, "mount=top;box=green"),
            (MarkingTemplates.ViaductPlanterId, "mount=trough;plants=mixed"), (MarkingTemplates.ViaductPlanterId, "where=m;mount=saddle;box=wood"), (MarkingTemplates.ViaductPlanterId, "where=l;wall=0;height=110;top=50"),
            (MarkingTemplates.ViaductPlanterId, "where=m;mount=hung"), (MarkingTemplates.FlowerBowlId, ""), (MarkingTemplates.FlowerBowlId, "style=round;colour=white;size=120;plants=shrub")
        };

        public static void RunAll()
        {
            Program.Run("greening: the catalogue, its tab, sound bodies, no modelled foliage", Catalogue);
            Program.Run("greening: the game's plants by kind", Plants);
            Program.Run("greening: the grate, the grating and the poles' timber (detail with holes)", Grates);
            Program.Run("greening: tree pits (CJJ/T 75-2023 4.3, DB11/T 839-2025 5.1, 5.9)", Pits);
            Program.Run("greening: green belts and tree strips (CJJ/T 75-2023 4.2, CJJ 37-2012 5.5.2)", Belts);
            Program.Run("greening: a belt's ends and its pieces in step, on a bend and a slope", BeltRuns);
            Program.Run("greening: viaduct planters held by the wall, clear of it, on a bend and a slope", Viaduct);
            Program.Run("greening: viaduct boxes of a run's pieces in step, the ends", ViaductRuns);
            Program.Run("greening: flower bowls", Bowls);
            RunLivingWall();
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static List<Vec3> Straight(float length, float grade = 0f) => new List<Vec3> { P(0f, 0f), P(0f, length, length * grade) };

        /// <summary>
        /// The centre of an Arc as its plan has it: a run's path is written turned so that its first chord heads +Z (RunPath.Encode), the circle with it. A point at the angle atan2(x, z) is turned back by
        /// the yaw the encoding took off.
        /// </summary>
        private static Vec3 ArcCentre(List<Vec3> arc, float r, bool right)
        {
            RunPath.Encode(arc, out float yaw);
            float x = right ? r : -r, c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            return new Vec3(x * c, 0f, x * s);
        }

        /// <summary>An arc from the origin heading +Z, radius r, turning right (its centre at (r, 0)) or left (at (-r, 0)), points every 2 m.</summary>
        private static List<Vec3> Arc(float r, float length, bool right, float grade = 0f)
        {
            var points = new List<Vec3>();
            for (float s = 0f; s <= length + 1e-4f; s += 2f)
            {
                double a = s / r;
                float x = r - r * (float)Math.Cos(a);
                points.Add(P(right ? x : -x, r * (float)Math.Sin(a), s * grade));
            }

            return points;
        }

        private static MarkingPlan PlanOf(string id, string settings, List<Vec3> line, float phase = 0f, float total = 0f)
        {
            GuideTemplate t = MarkingTemplates.ById(id);
            GuideSpec spec = t.Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                spec = spec.With(pair.Substring(0, pair.IndexOf('=')), pair.Substring(pair.IndexOf('=') + 1));
            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), RunPath.Encode(line, out _), phase, total));
        }

        private static MarkingPart Part(MarkingPlan plan, SolidColour colour, DetailKind? detail = null) =>
            plan.Parts.Find(p => p.Solid == colour && (detail == null || p.Detail == detail.Value) && p.Mesh.VertexCount > 0);

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

        /// <summary>The edges of a mesh's triangles (their corners welded within 0.1 mm) used an odd number of times: none for a closed body.</summary>
        private static int OpenEdges(MeshBuilder mesh)
        {
            var ids = new Dictionary<(long, long, long), int>();
            int Id(Vec3 p)
            {
                var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
                if (!ids.TryGetValue(key, out int id))
                    ids[key] = id = ids.Count;
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

            int open = 0;
            foreach (int n in edges.Values)
                open += n % 2;
            return open;
        }

        private static void Bounds(IEnumerable<Vec3> points, out Vec3 lo, out Vec3 hi)
        {
            lo = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            hi = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Vec3 p in points)
            {
                lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
            }
        }

        private static IEnumerable<Vec3> AllPoints(MarkingPlan plan)
        {
            foreach (MarkingPart part in plan.Parts)
            {
                foreach (Vec3 p in part.Mesh.Positions)
                    yield return p;
            }
        }

        private static void Catalogue()
        {
            foreach (string id in kIds)
            {
                SignDef def = SignCatalog.ById(id);
                SignChoice choice = SignCatalog.ChoiceById(id);
                Program.Check(def != null && def.IsMarking && def.Group == MarkingTemplates.BarrierGroup && MarkingTemplates.IsRun(def.Template), id + ": in the catalogue, in the barriers' group, drawn along a road");
                Program.Check(choice != null && choice.Series == MarkingTemplates.GreenSeries, id + ": listed in the greening tab");
                Program.Check(MarkingGround.Lies(def), id + ": laid on the ground");
                Program.Check(def.Template.FieldOf(BarrierAuto.JunctionKey) != null, id + ": has the barriers' junction setting");
            }

            foreach (string id in new[] { MarkingTemplates.PlanterRailingId, MarkingTemplates.PlanterBoxId, MarkingTemplates.LawnFenceId })
                Program.Check(SignCatalog.ChoiceById(id).Series == MarkingTemplates.GreenSeries && SignCatalog.ChoiceById(id).Group == MarkingTemplates.BarrierGroup, id + ": the planters of 0.12.32 are in the greening tab too");
            Program.Check(SignCatalog.ChoiceById(MarkingTemplates.ConeId).Series == CDLC.TS.Geo.Signs.TollSigns.Group, "the toll tab keeps its cones");
            Program.Check((int)SolidColour.CastIron == 30 && (int)SolidColour.ResinGreen == 31, "the greening's colours are 30 and 31 (saved by number)");
            Program.Check(new SignParts().Solids.Length > (int)SolidColour.ResinGreen, "a support's solids have room for them");
            Program.Check(Raster.SolidRgb(SolidColour.CastIron)[0] < 80 && Raster.SolidRgb(SolidColour.ResinGreen)[1] > Raster.SolidRgb(SolidColour.ResinGreen)[0], "their colours in the pictures");

            foreach ((string id, string settings) in kVariants)
            {
                string what = id + " [" + settings + "]";
                foreach (List<Vec3> line in new[] { Straight(30f), Arc(25f, 30f, true), Straight(30f, 0.05f) })
                {
                    MarkingPlan plan = PlanOf(id, settings, line);
                    Program.Check(plan != null && plan.HasSolids && plan.TriangleCount >= 12, what + ": a body: " + plan?.TriangleCount);
                    Program.Check(plan.TriangleCount < 20000, what + ": not too heavy for 30 m: " + plan.TriangleCount + " triangles");
                    foreach (MarkingPart part in plan.Parts)
                    {
                        Program.Check(part.Solid.HasValue, what + ": only solid parts");
                        // The owner: every plant from the game; nothing green is modelled (the teal of a resin grate or a box is not foliage).
                        Program.Check(part.Solid != SolidColour.Green && part.Solid != SolidColour.GlowGreen, what + ": no green solid standing in for a plant");
                        if (part.Mesh.VertexCount == 0)
                            continue;
                        Program.Check(Wound(part.Mesh), what + ": every triangle faces out (" + part.Solid + ")");
                        Program.Check(part.Mesh.Positions.TrueForAll(p => p.IsFinite), what + ": finite points");
                    }

                    Program.Check(plan.BakedProps.Count > 0, what + ": has the game's plants: " + plan.BakedProps.Count);
                    foreach (PropAnchor prop in plan.BakedProps)
                        Program.Check(GreenPlants.PrefabsOf(prop.Kind).Count > 0 && prop.Room > 0.1f && prop.Local.IsFinite, what + ": a plant of a known kind with room: " + prop.Kind + " " + prop.Room);
                    if (line[line.Count - 1].Y != 0f)
                        continue;
                    Bounds(AllPoints(plan), out Vec3 lo, out Vec3 hi);
                    Program.Check(lo.Y > -0.7f && hi.Y < 2.1f, what + ": stands on its line: " + lo.Y + " .. " + hi.Y);
                }
            }
        }

        private static void Plants()
        {
            string[] all = GreenPlants.All;
            string[] planters = { "FlowerBushCity01", "FlowerBushCity02", "FlowerBushCity03", "GreenBushCity01", "GreenBushCity02", "GreenBushCity03" };
            for (int i = 0; i < planters.Length; i++)
                Program.Check(all[i] == planters[i], "the planters' six first, as 0.12.32 copied them: " + all[i]);
            Program.Check(new HashSet<string>(all).Count == all.Length, "no prefab twice");
            Program.Check(GreenPlants.PrefabsOf(GreenPlants.Planter).Count == 6 && GreenPlants.PrefabsOf(GreenPlants.Flower).Count == 3 && GreenPlants.PrefabsOf(GreenPlants.Shrub).Count == 3, "the bushes by kind");
            Program.Check(GreenPlants.PrefabsOf(GreenPlants.Hedge)[0] == "HedgePieceLow02" && GreenPlants.KeepsHeading(GreenPlants.Hedge) && !GreenPlants.KeepsHeading(GreenPlants.Shrub), "the hedge piece keeps the line's heading");
            foreach (var (species, prefab, _, _) in GreenPlants.Trees)
            {
                Program.Check(GreenPlants.PrefabsOf(GreenPlants.TreeKind(species, false))[0] == prefab && GreenPlants.PrefabsOf(GreenPlants.TreeKind(species, true))[0] == prefab, species + ": its tree, young or not");
                Program.Check(Array.IndexOf(all, prefab) >= 0, species + ": its prefab is copied");
            }

            Program.Check(GreenPlants.PrefabsOf(GreenPlants.Pot).Count == 2 && Array.IndexOf(all, "FlowerPot02") > Array.IndexOf(all, "NA_LondonPlaneTree01") && Array.IndexOf(all, "FlowerPot01") >= 0 && GreenPlants.PrefabsOf(GreenPlants.Planter).Count == 6 && !GreenPlants.KeepsHeading(GreenPlants.Pot), "the pots are copied after the trees and are not the planters' bushes");
            Program.Check(GreenPlants.IsYoung("tree.plane.young") && !GreenPlants.IsYoung("tree.plane") && !GreenPlants.IsYoung("shrub"), "a guarded tree is young");
            Program.Check(GreenPlants.PrefabsOf("tree.oak").Count == 0 && GreenPlants.PrefabsOf("ivy").Count == 0 && GreenPlants.PrefabsOf(null).Count == 0, "an unknown kind has no prefab (nothing is put)");
            Program.Check(Array.IndexOf(MarkingTemplates.PlantedIds, MarkingTemplates.PlanterRailingId) >= 0 && Array.IndexOf(MarkingTemplates.PlantedIds, MarkingTemplates.ViaductPlanterId) >= 0, "the planted templates");
        }

        private static void Grates()
        {
            DetailSurface grate = DetailSurfaces.Make(DetailKind.TreeGrate), grating = DetailSurfaces.Make(DetailKind.FrpGrating), timber = DetailSurfaces.Make(DetailKind.Timber);
            Program.Check(DetailSurfaces.HasHoles(DetailKind.TreeGrate) && DetailSurfaces.HasHoles(DetailKind.FrpGrating) && !DetailSurfaces.HasHoles(DetailKind.Timber), "the grates have holes, the timber none");
            Program.Check(grate.Alpha != null && grating.Alpha != null && timber.Alpha == null, "alpha maps");
            float Open(DetailSurface s)
            {
                int open = 0;
                foreach (float a in s.Alpha)
                    open += a < 0.5f ? 1 : 0;
                return open / (float)s.Alpha.Length;
            }

            float t = DetailSurfaces.GrateTile;
            Program.Check(Open(grate) > 0.2f && Open(grate) < 0.45f, "a cast grate is a quarter to two fifths open: " + Open(grate));
            Program.Near(Open(grating), (1f - 0.0065f / 0.038f) * (1f - 0.0065f / 0.038f), 0.03f, "the grating's cells 38 mm between bars 6.5 mm");
            Program.Check(DetailSurfaces.GrateSolid(t * 0.5f, t * 0.5f) < -0.2f, "the trunk's hole in the middle, 0.41 m across");
            Program.Check(DetailSurfaces.GrateSolid(0.02f, t * 0.5f) > 0.01f && DetailSurfaces.GrateSolid(t * 0.5f, t - 0.02f) > 0.01f, "the border solid");
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    // A guard's pole (50 mm) goes through each corner hole with room to spare.
                    float u = t * (0.5f + sx * 0.4f), v = t * (0.5f + sz * 0.4f);
                    Program.Check(DetailSurfaces.GrateSolid(u, v) < -0.025f, $"a corner hole at {u:0.00}, {v:0.00} takes a pole");
                }
            }

            // The bars between the slots of a ring are as wide as the slots, and not thinner than 15 mm.
            int changes = 0;
            bool was = DetailSurfaces.GrateSolid(t * 0.5f + 0.265f * t, t * 0.5f) > 0f;
            float shortest = float.MaxValue, run = 0f;
            const int steps = 4000;
            for (int i = 1; i <= steps; i++)
            {
                double a = 2.0 * Math.PI * i / steps;
                bool solid = DetailSurfaces.GrateSolid(t * 0.5f + 0.265f * t * (float)Math.Cos(a), t * 0.5f + 0.265f * t * (float)Math.Sin(a)) > 0f;
                run += (float)(2.0 * Math.PI * 0.265 * t / steps);
                if (solid != was)
                {
                    if (changes++ > 0)
                        shortest = Math.Min(shortest, run);
                    run = 0f;
                    was = solid;
                }
            }

            Program.Check(changes > 60 && shortest > 0.015f, $"the inner ring: {changes / 2} slots, the narrowest bar or slot {shortest * 1000f:0} mm");
        }
    }
}
