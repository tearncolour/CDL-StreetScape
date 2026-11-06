using System;
using System.Linq;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The city railings and barriers of 0.12.32 (Barriers.City, MarkingTemplates.CityBarriers): in the catalogue, sound bodies, the sizes GB 50688 sets, the lowering at a gap and the planters' plants.</summary>
    internal static class CityBarrierTests
    {
        private static readonly string[] kIds =
        {
            MarkingTemplates.HongKongFenceId, MarkingTemplates.PedestrianRailingId, MarkingTemplates.LowRailingId, MarkingTemplates.LawnFenceId, MarkingTemplates.FlexPostId,
            MarkingTemplates.PlanterRailingId, MarkingTemplates.PlanterBoxId, MarkingTemplates.CombinedId, MarkingTemplates.BeamAndPostId, MarkingTemplates.MovableId
        };

        public static void RunAll()
        {
            Program.Run("city barriers: the catalogue and sound bodies", Catalogue);
            Program.Run("city barriers: the pedestrian railing's height and bars (GB 50688 7.5.2)", Pedestrian);
            Program.Run("city barriers: the Hong Kong type's panels", HongKong);
            Program.Run("city barriers: a median railing comes down at a pedestrians' gap (GB 50688 7.6.1)", Gap);
            Program.Run("city barriers: a new median railing starts lowered at its ends, an old one is as it was (0.12.106)", StartsLowered);
            Program.Run("city barriers: separating posts and the lawn fence", PostsAndLawn);
            Program.Run("city barriers: the planters and their plants", Planters);
            Program.Run("city barriers: the combined, the beam-and-post and the movable barrier", Crash);
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static List<Vec3> Straight(float length) => new List<Vec3> { P(0f, 0f), P(0f, length) };

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

        private static void Bounds(MarkingPlan plan, Func<MarkingPart, bool> which, out Vec3 lo, out Vec3 hi)
        {
            lo = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            hi = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MarkingPart part in plan.Parts)
            {
                if (!which(part))
                    continue;
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                    hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
                }
            }
        }

        /// <summary>The highest point of the solids within dz of z, the separating post at a gap (yellow) not counted.</summary>
        private static float TopNear(MarkingPlan plan, float z, float dz)
        {
            float top = float.MinValue;
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid == SolidColour.Yellow)
                    continue;
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    if (Math.Abs(p.Z - z) <= dz)
                        top = Math.Max(top, p.Y);
                }
            }

            return top;
        }

        /// <summary>The middles along Z of the upright tubes of radius r whose bottom rings are at height y (their points within r of X = 0), between z0 and z1.</summary>
        private static List<float> TubeMiddles(MeshBuilder mesh, float y, float r, float z0, float z1)
        {
            var zs = new List<float>();
            foreach (Vec3 p in mesh.Positions)
            {
                if (Math.Abs(p.Y - y) < 1e-3f && Math.Abs(p.X) <= r + 1e-4f && p.Z > z0 && p.Z < z1)
                    zs.Add(p.Z);
            }

            zs.Sort();
            var middles = new List<float>();
            int first = 0;
            for (int i = 1; i <= zs.Count; i++)
            {
                if (i == zs.Count || zs[i] - zs[i - 1] > r * 1.5f)
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
                MarkingPlan plan = PlanOf(id, "", Straight(30f));
                Program.Check(plan != null && plan.HasSolids && plan.TriangleCount > 20, id + ": a body: " + plan?.TriangleCount);
                foreach (MarkingPart part in plan.Parts)
                {
                    Program.Check(part.Solid.HasValue, id + ": only solid parts");
                    Program.Check(Wound(part.Mesh), id + ": every triangle faces out (" + part.Solid + ")");
                    foreach (Vec3 p in part.Mesh.Positions)
                    {
                        if (!p.IsFinite)
                        {
                            Program.Check(false, id + ": a point that is not a number");
                            break;
                        }
                    }
                }

                Bounds(plan, p => true, out Vec3 lo, out Vec3 hi);
                Program.Check(lo.Z > -2f && hi.Z < 32f && Math.Abs(lo.X) < 2f && Math.Abs(hi.X) < 2f, id + ": stays along its 30 m line: " + lo + " .. " + hi);
                Program.Check(plan.TriangleCount < 60000, id + ": not too heavy for 30 m: " + plan.TriangleCount);
            }
        }

        private static void Pedestrian()
        {
            foreach (string height in new[] { "90", "110", "140" })
            {
                MarkingPlan plan = PlanOf(MarkingTemplates.PedestrianRailingId, "height=" + height, Straight(20f));
                Bounds(plan, p => true, out _, out Vec3 hi);
                float h = float.Parse(height) / 100f;
                Program.Check(hi.Y >= h && hi.Y < h + 0.06f, $"a railing of {height} cm reaches its height (the rail's top): {hi.Y}");
            }

            // The bars' clear gap: no more than 0.11 m (7.5.2-3). The bars' middles along Z at a height between the rails.
            List<Vec3> line = Straight(4f);
            var shapes = Barriers.PedestrianRailing(line, 1.1f, SolidColour.Steel, 0f, true, true);
            List<float> list = TubeMiddles(shapes[0].Mesh, 0.10f, 0.01f, 0.1f, 3.9f);
            float widest = 0f;
            for (int i = 1; i < list.Count; i++)
                widest = Math.Max(widest, list[i] - list[i - 1]);
            // Where a post stands the bar is left out: the gaps either side of the post are the post's, 60 mm wide.
            Program.Check(list.Count > 20 && widest < 0.2f, $"bars all along 4 m but at the posts: {list.Count} bars, the widest gap {widest:0.000} m");
            float bars = 0f;
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i] - list[i - 1] < 0.2f)
                    bars = Math.Max(bars, list[i] - list[i - 1] - 0.02f);
            }

            Program.Check(bars <= 0.11f + 1e-3f && bars > 0.09f, "the clear gap between two bars: " + bars);
        }

        private static void HongKong()
        {
            var shapes = Barriers.HongKongFence(Straight(15f), 1.1f, SolidColour.Metal, default, 0f, 0f, true, true);
            // Eleven 16 mm bars to a 1.5 m panel: count the bars' tubes by their middles at 0.6 m.
            List<float> bars = TubeMiddles(shapes[0].Mesh, 0.14f, 0.008f, 0.03f, 1.47f);
            Program.Check(bars.Count == 11, "eleven bars in the first panel: " + bars.Count);
            Program.Check(bars.Count > 1 && Math.Abs(bars[1] - bars[0] - 0.125f) < 0.002f, "125 mm apart (109 mm between them)");
            Bounds(Plan(shapes), part => part.Solid == SolidColour.White, out Vec3 lo, out Vec3 hi);
            Program.Check(lo.Z < 0.1f && hi.Z > 11.9f && hi.Y > 1.0f, "white film on the posts every 6 m (at 0, 6 and 12 m): " + lo + " .. " + hi);
        }

        private static MarkingPlan Plan(List<MarkingShape> shapes)
        {
            var plan = new MarkingPlan();
            plan.Shapes.AddRange(shapes);
            plan.Bake(0f);
            return plan;
        }

        private static void Gap()
        {
            foreach (string id in new[] { MarkingTemplates.CityFenceId, MarkingTemplates.HongKongFenceId })
            {
                MarkingPlan plain = PlanOf(id, "height=110", Straight(80f));
                // GA/T 1567-2019 4.2.6 (0.12.106): 0.70 m for the last 10 m, and a ramp of 15 m up from there: full high from 25 m before the end (the owner: the drop is quick, not a slope of 40 m).
                MarkingPlan lowered = PlanOf(id, "height=110;gap=b;glen=10", Straight(80f));
                Program.Near(TopNear(plain, 79f, 1.6f), TopNear(plain, 40f, 1.6f), 0.01f, id + ": without lowering the end is as high as the middle");
                float end = TopNear(lowered, 79.9f, 0.3f), low = TopNear(lowered, 72f, 1.0f), full = TopNear(lowered, 40f, 1.6f), halfway = TopNear(lowered, 62.5f, 0.6f), before = TopNear(lowered, 50f, 1.6f);
                Program.Check(end <= GapTaper.Low + 0.06f && low <= GapTaper.Low + 0.06f, $"{id}: 0.70 m (+ the post's cap) at the end and over the last 10 m: {end:0.00}, {low:0.00}");
                Program.Check(full >= 1.09f && before >= 1.09f, $"{id}: full height from 25 m before the end: {before:0.00}, {full:0.00}");
                Program.Check(halfway > GapTaper.Low + 0.1f && halfway < full - 0.1f, $"{id}: half way up the ramp in between (17.5 m from the end): {halfway:0.00}");
                Program.Check(lowered.Parts.Exists(p => p.Solid == SolidColour.Yellow), id + ": a separating post at the gap");
                // The ramp's length is a setting: 5 m is steeper (full from 15 m before the end, not 25), the default 15 m is the standard's.
                MarkingPlan steep = PlanOf(id, "height=110;gap=b;glen=10;gramp=5", Straight(80f));
                float steepFull = TopNear(steep, 58f, 1.6f), steepMid = TopNear(steep, 67.5f, 0.6f), steepLow = TopNear(steep, 74f, 1.0f);
                Program.Check(steepFull >= 1.08f && steepLow <= GapTaper.Low + 0.06f && steepMid > steepLow + 0.1f && steepMid < steepFull - 0.1f, $"{id}: a ramp of 5 m: full 22 m before the end, low 6 m before it, between them 12.5 m before it: {steepFull:0.00}, {steepMid:0.00}, {steepLow:0.00}");
                // A piece in the middle of a long run (the run's ends are elsewhere) is not lowered.
                MarkingPlan piece = PlanOf(id, "height=110;gap=b;glen=10", Straight(30f), 100f, 300f);
                Program.Check(TopNear(piece, 15f, 1f) >= 1.09f && !piece.Parts.Exists(p => p.Solid == SolidColour.Yellow), id + ": a piece far from both ends keeps its height and has no post");
            }

            // The profile itself: Low for Length from an end, then a straight ramp of Ramp metres up to the height.
            var taper = new GapTaper { AtEnd = true, Length = 10f, Ramp = 15f };
            Program.Near(taper.HeightAt(100f, 100f, 1.1f), GapTaper.Low, 1e-4f, "the taper: 0.70 m at the end");
            Program.Near(taper.HeightAt(95f, 100f, 1.1f), GapTaper.Low, 1e-4f, "the taper: 0.70 m 5 m from the end");
            Program.Near(taper.HeightAt(90f, 100f, 1.1f), GapTaper.Low, 1e-4f, "the taper: 0.70 m where the low end ends (10 m)");
            Program.Near(taper.HeightAt(82.5f, 100f, 1.1f), 0.9f, 1e-4f, "the taper: half way up the ramp, 17.5 m from the end, is half way between 0.70 and 1.1");
            Program.Near(taper.HeightAt(86.25f, 100f, 1.1f), 0.8f, 1e-4f, "the taper: a quarter of the way up the ramp (13.75 m from the end): it rises with the distance, not falls");
            Program.Near(taper.HeightAt(78.75f, 100f, 1.1f), 1.0f, 1e-4f, "the taper: three quarters of the way up (21.25 m from the end)");
            Program.Near(taper.HeightAt(75f, 100f, 1.1f), 1.1f, 1e-4f, "the taper: full where the ramp ends (25 m)");
            Program.Near(taper.HeightAt(60f, 100f, 1.1f), 1.1f, 1e-4f, "the taper: full beyond it");
            Program.Near(taper.HeightAt(0f, 100f, 1.1f), 1.1f, 1e-4f, "the taper at the end only: full at the start");
            var both = new GapTaper { AtStart = true, AtEnd = true, Length = 10f, Ramp = 15f };
            Program.Near(both.HeightAt(0f, 100f, 1.1f), GapTaper.Low, 1e-4f, "both ends: 0.70 m at the start");
            Program.Near(both.HeightAt(17.5f, 100f, 1.1f), 0.9f, 1e-4f, "both ends: half way up the ramp from the start");
            Program.Near(both.HeightAt(50f, 100f, 1.1f), 1.1f, 1e-4f, "both ends: full in the middle");
            Program.Near(both.HeightAt(20f, 30f, 1.1f), GapTaper.Low + 0.4f * 0.2f + 0f, 0.2f, "a stretch of 30 m between two junctions: the nearer end rules, so it never goes up to full between them");
            Program.Check(both.HeightAt(15f, 30f, 1.1f) < 1.1f - 0.01f, "a stretch of 30 m has no full-height point at its middle (15 m from each end is on the ramp)");
            Program.Near(both.HeightAt(15f, 20f, 1.1f), GapTaper.Low, 1e-4f, "a stretch of 20 m: 10 m from each end, low all the way");
            // A ramp that is none steps; a low end that is none is a slope that reaches 0.70 m at the tip; neither is no taper; a railing not above 0.70 m is as it is.
            Program.Near(new GapTaper { AtEnd = true, Length = 10f, Ramp = 0f }.HeightAt(89f, 100f, 1.1f), 1.1f, 1e-4f, "no ramp: full just before the low end (11 m from the end)");
            Program.Near(new GapTaper { AtEnd = true, Length = 10f, Ramp = 0f }.HeightAt(90f, 100f, 1.1f), GapTaper.Low, 1e-4f, "no ramp: low at the low end's edge");
            Program.Near(new GapTaper { AtEnd = true, Length = 0f, Ramp = 20f }.HeightAt(90f, 100f, 1.1f), 0.9f, 1e-4f, "no low end: the ramp starts at the tip: half way up 10 m from it");
            Program.Near(new GapTaper { AtEnd = true, Length = 0f, Ramp = 20f }.HeightAt(100f, 100f, 1.1f), GapTaper.Low, 1e-4f, "no low end: 0.70 m at the tip");
            Program.Near(new GapTaper { AtEnd = true }.HeightAt(100f, 100f, 1.1f), 1.1f, 1e-4f, "neither a low end nor a ramp: no taper");
            Program.Near(taper.HeightAt(100f, 100f, 0.7f), 0.7f, 1e-4f, "a railing of 0.70 m is as it is");
            Program.Near(taper.HeightAt(100f, 100f, 0.5f), 0.5f, 1e-4f, "one lower than that too");
            Program.Near(new GapTaper { Length = 10f, Ramp = 15f }.HeightAt(100f, 100f, 1.1f), 1.1f, 1e-4f, "no end asked for: full");
        }

        private static readonly string[] kMedianRailings = { MarkingTemplates.CityFenceId, MarkingTemplates.BeijingFenceId, MarkingTemplates.ZincFenceId, MarkingTemplates.HongKongFenceId };

        /// <summary>
        /// The owner (6 Nov: "城市道路栏杆加入一个快到路口时候高度降低的可选项"): the setting was there as "行人断口渐降" with the default "none", and not found. A draft of a median railing now starts with "两端"
        /// (MarkingTemplates.StartSettings); the field's default stays "none", because a railing placed before 0.12.32 (his save has them: "scheme=bw;height=100;post=3;feet=1;jn=side;g=0") has no gap in its
        /// settings, and a missing key is the default.
        /// </summary>
        private static void StartsLowered()
        {
            foreach (string id in kMedianRailings)
            {
                GuideTemplate t = MarkingTemplates.ById(id);
                Program.Check(MarkingTemplates.StartSettings(id) == "gap=b;glen=10", id + ": a new draft starts lowered at both ends, with a low end of 10 m");
                GuideSpec start = t.Parse(MarkingTemplates.StartSettings(id)), plain = t.Parse("");
                Program.Check(start.Is("gap", "b") && start.Is("glen", "10") && start.Is("gramp", "15") && start.Flag("gpost"), id + ": at both ends, a low end of 10 m and a ramp of 15 m, with a separating post");
                Program.Check(plain.Is("gap", "n") && t.Default().Is("gap", "n") && t.Default().Is("glen", "40") && t.Default().Is("gramp", "15"), id + ": the fields' own defaults are as they were (none, 40 m): a settings text with no gap reads as it always did");
                // Everything but the gap and the low end is what the template's defaults are; the ramp is written only when it is not 15 m.
                Program.Check(start.With("gap", "n").With("glen", "40").ToString() == t.Default().ToString() && !start.ToString().Contains("gramp"), id + ": the start differs from the defaults in the gap and the low end only");
                Program.Check(t.Parse("gap=b;gramp=5").ToString().Contains("gramp=5") && t.Parse("gap=b;gramp=7").Is("gramp", "15"), id + ": a ramp that is not 15 m is written; one that is not a choice is 15");
                // The low end, the ramp and the post are asked for only while the railing comes down at all.
                foreach (string key in new[] { "glen", "gramp", "gpost" })
                {
                    GuideField shown = t.FieldOf(key);
                    Program.Check(shown != null && shown.Visible(start) && !shown.Visible(plain), id + ": the " + key + " setting shows only where the railing comes down");
                }
                // A railing of an old save (no gap in its text) has no lowering: as high at its ends as in its middle.
                MarkingPlan old = PlanOf(id, "height=110", Straight(80f));
                Program.Near(TopNear(old, 79f, 1.6f), TopNear(old, 40f, 1.6f), 0.01f, id + ": an old railing is as high at its end as in its middle");
                int oldVertices = old.Parts.Sum(p => p.Mesh.VertexCount);
                // A new one, from the start settings, is lowered at both ends and full high in the middle.
                MarkingPlan fresh = PlanOf(id, "height=110;" + MarkingTemplates.StartSettings(id), Straight(120f));
                float first = TopNear(fresh, 0.1f, 0.3f), last = TopNear(fresh, 119.9f, 0.3f), mid = TopNear(fresh, 60f, 1.6f), at30 = TopNear(fresh, 30f, 1.0f);
                Program.Check(first <= GapTaper.Low + 0.06f && last <= GapTaper.Low + 0.06f && mid >= 1.09f && at30 >= 1.09f, $"{id}: new, 0.70 m at both ends and full from 25 m in: {first:0.00}, {at30:0.00}, {mid:0.00}, {last:0.00}");
                // A separating post stands at each end of a new one (the same railing 80 m long has the posts the old one has not).
                MarkingPlan fresh80 = PlanOf(id, "height=110;" + MarkingTemplates.StartSettings(id), Straight(80f));
                Program.Check(fresh80.Parts.Sum(p => p.Mesh.VertexCount) > oldVertices, id + ": the new one has the separating posts the old one has not");
            }

            // Other entries start as their fields say.
            foreach (string id in new[] { MarkingTemplates.WBeamId, MarkingTemplates.ConcreteBarrierId, MarkingTemplates.ConeId, MarkingTemplates.PedestrianRailingId, MarkingTemplates.LowRailingId, MarkingTemplates.LawnFenceId, "gb.prohib.01", "", null })
                Program.Check(MarkingTemplates.StartSettings(id) == "", (id ?? "null") + ": no start settings");
            // The setting's label says what it does, in both languages, within the text budget.
            GuideField field = MarkingTemplates.ById(MarkingTemplates.CityFenceId).FieldOf("gap");
            Program.Check(field.Zh.Contains("路口") && field.En.Contains("junction") && field.Zh.Length <= TextBudgetTests.Label.Zh && field.En.Length <= TextBudgetTests.Label.En, "the label: " + field.Zh + " / " + field.En);
        }

        private static void PostsAndLawn()
        {
            MarkingPlan posts = PlanOf(MarkingTemplates.FlexPostId, "gap=1.5", Straight(15f));
            var places = new SortedSet<float>();
            foreach (MarkingPart part in posts.Parts)
            {
                if (part.Solid != SolidColour.Black)
                    continue;
                foreach (Vec3 p in part.Mesh.Positions)
                    places.Add((float)Math.Round(p.Z / 0.5) * 0.5f);
            }

            Program.Check(places.Count >= 10, "a post every 1.5 m over 15 m: " + places.Count);
            foreach (string style in new[] { "pickets", "hoops" })
            {
                MarkingPlan lawn = PlanOf(MarkingTemplates.LawnFenceId, $"style={style};height=40", Straight(10f));
                Bounds(lawn, p => true, out Vec3 lo, out Vec3 hi);
                Program.Check(hi.Y > 0.35f && hi.Y < 0.5f && lo.Y < 0f, $"a lawn fence of {style}, 0.4 m, its feet in the ground: {lo.Y} .. {hi.Y}");
            }
        }

        private static void Planters()
        {
            MarkingPlan railing = PlanOf(MarkingTemplates.PlanterRailingId, "height=100", Straight(20f));
            Program.Check(railing.Parts.Exists(p => p.Solid == SolidColour.Soil), "the railing's planters have soil");
            Program.Check(railing.Props.Count >= 16 && railing.Props.Count <= 20, "two plants in each of the nine or ten boxes of 20 m: " + railing.Props.Count);
            foreach (PropAnchor prop in railing.BakedProps)
                Program.Check(prop.Kind == "planter" && prop.Room > 0.2f && prop.Room <= 0.8f && Math.Abs(prop.Local.Y - 1.22f) < 0.01f, $"a plant on the soil of its box at 1.22 m: {prop.Local} room {prop.Room}");
            MarkingPlan boxes = PlanOf(MarkingTemplates.PlanterBoxId, "size=15;gap=0.5", Straight(20f));
            Program.Check(boxes.Props.Count >= 18 && boxes.Props.Count <= 20, "two plants in each of the ten boxes of 1.5 m with 0.5 m between them: " + boxes.Props.Count);
            // Laid on a 5 % grade, the plants go up with the boxes.
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.PlanterBoxId);
            List<Vec3> slope = new List<Vec3> { P(0f, 0f), P(0f, 20f, 1f) };
            MarkingPlan sloped = MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, t.Default().ToString(), RunPath.Encode(slope, out _), 0f));
            PropAnchor far = sloped.BakedProps[sloped.BakedProps.Count - 1];
            Program.Check(far.Local.Y > 0.9f + 0.54f, "a plant at the far end is as high as its box on the slope: " + far.Local);
        }

        private static void Crash()
        {
            MarkingPlan combined = PlanOf(MarkingTemplates.CombinedId, "height=110;wall=60", Straight(10f));
            Bounds(combined, p => p.Solid == SolidColour.Concrete, out _, out Vec3 wall);
            Bounds(combined, p => p.Solid != SolidColour.Concrete, out _, out Vec3 steel);
            Program.Near(wall.Y, 0.6f, 0.01f, "the combined barrier's wall is 0.6 m");
            Program.Near(steel.Y, 1.1f, 0.01f, "its beam's top at 1.1 m");
            MarkingPlan right = PlanOf(MarkingTemplates.BeamAndPostId, "where=r", Straight(10f)), left = PlanOf(MarkingTemplates.BeamAndPostId, "where=l", Straight(10f));
            Bounds(right, p => p.Solid != SolidColour.Concrete, out Vec3 rlo, out Vec3 rhi);
            Bounds(left, p => p.Solid != SolidColour.Concrete, out Vec3 llo, out Vec3 lhi);
            Program.Check(rhi.X > 0.15f && llo.X < -0.15f, $"the beams face the traffic: right {rhi.X}, left {llo.X}");
            MarkingPlan movable = PlanOf(MarkingTemplates.MovableId, "", Straight(10f));
            Bounds(movable, p => p.Solid == SolidColour.Metal, out Vec3 mlo, out Vec3 mhi);
            Program.Check(mlo.X < -0.2f && mhi.X > 0.2f, "the movable barrier has a beam on both faces");
        }
    }
}
