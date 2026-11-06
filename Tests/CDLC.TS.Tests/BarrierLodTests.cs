using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The long-reach tier and the levels of the noise walls' geometry (0.12.73; the owner: "全封闭隔音墙离远了就消失了").</summary>
    internal static class BarrierLodTests
    {
        public static void RunAll()
        {
            Program.Run("barrier lod: which items are seen from far, and the store's count of them", Classification);
            Program.Run("barrier lod: the horizon tier is for a tile with long-reach items, up to its own limit, with the hysteresis", Tiers);
            Program.Run("barrier lod: the planner builds a horizon tile for a wall and drops it again, and leaves a tile of signs alone", Planner);
            Program.Run("barrier lod: the levels of the noise walls: less to build at each, the same footprint and height, sound meshes", Levels);
            Program.Run("barrier lod: a tile at the horizon is made of the long-reach items only", HorizonTile);
            Program.Run("barrier lod: the thinned line of level 2", Thinning);
        }

        private static readonly (string Id, string Settings)[] Forms =
        {
            (MarkingTemplates.NoiseBarrierId, ""), (MarkingTemplates.NoiseBarrierId, "top=arc;band=upper"), (MarkingTemplates.NoiseBarrierId, "top=crank;band=all"),
            (MarkingTemplates.NoiseCoverId, "form=full"), (MarkingTemplates.NoiseCoverId, "form=full;span=20"), (MarkingTemplates.NoiseCoverId, "form=semi"), (MarkingTemplates.CementNoiseId, "band=upper"),
            (MarkingTemplates.BrickNoiseId, "band=mid"), (MarkingTemplates.GreenNoiseId, "")
        };

        private static MarkingPlan PlanOf(string id, string settings, List<Vec3> line, int level)
        {
            GuideTemplate t = MarkingTemplates.ById(id);
            GuideSpec spec = t.Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), RunPath.Encode(line, out _), 0f, 0f), level);
        }

        private static List<Vec3> Straight(float length)
        {
            var line = new List<Vec3>();
            for (float s = 0f; s < length + 0.01f; s += 2f)
                line.Add(new Vec3(0f, 0f, s));
            return line;
        }

        private static List<Vec3> Curve()
        {
            var line = new List<Vec3>();
            for (int i = 0; i <= 40; i++)
            {
                double a = i / 40.0 * 70.0 * Math.PI / 180.0;
                line.Add(new Vec3((float)(60.0 - 60.0 * Math.Cos(a)), 0f, (float)(60.0 * Math.Sin(a))));
            }

            return line;
        }

        /// <summary>The length of the wall that is built from a line: an item holds 32 chords at most (RunPath.MaxPieces), the rest of a long line is another item.</summary>
        private static float Metres(List<Vec3> line) => RunPath.Length(RunPath.Encode(line, out _));

        private static void Box(MarkingPlan plan, out Vec3 lo, out Vec3 hi)
        {
            lo = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            hi = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MarkingPart part in plan.Parts)
            {
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                    hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
                }
            }
        }

        private static int Vertices(MarkingPlan plan) => plan.VertexCount;

        /// <summary>The area of the transparent sheets (m2): the sheets of a level are whole, not short of the frames that are left out.</summary>
        private static float GlassArea(MarkingPlan plan)
        {
            float area = 0f;
            foreach (MarkingPart part in plan.Parts)
            {
                if (!part.Solid.HasValue || !SolidGlass.Is(part.Solid.Value))
                    continue;
                MeshBuilder mesh = part.Mesh;
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                    area += MeshBuilder.Cross(mesh.Positions[mesh.Indices[t + 1]] - mesh.Positions[mesh.Indices[t]], mesh.Positions[mesh.Indices[t + 2]] - mesh.Positions[mesh.Indices[t]]).Length * 0.5f;
            }

            return area;
        }

        // ------------------------------------------------------------------ classification

        private static void Classification()
        {
            foreach (string id in MarkingTemplates.LongReachIds)
                Program.Check(LongReach.Is(SignCatalog.HashOf(id)), id + " is seen from far");
            Program.Check(!LongReach.Is(SignCatalog.HashOf(MarkingTemplates.FretRailingId)) && !LongReach.Is(SignCatalog.HashOf("gb.prohib.01")) && !LongReach.Is(0u), "a railing and a sign are not");
            Program.Check(MarkingTemplates.LongReachIds.Length == 5, "the five noise walls");

            var store = new ItemStore<int>();
            ItemRecord wall = new ItemRecord(SignCatalog.HashOf(MarkingTemplates.NoiseCoverId), new Vec3(10f, 0f, 10f), 0f, 0u, "", 0f, 0u, 0, 0, 0f);
            ItemRecord sign = new ItemRecord(SignCatalog.HashOf("gb.prohib.01"), new Vec3(20f, 0f, 20f), 0f, 0u, "", 0f, 0u, 0, 0, 0f);
            TileKey tile = Tiles.Of(10f, 10f);
            Program.Check(!store.HasLongReach(tile), "an empty tile has none");
            store.Upsert(1, sign);
            Program.Check(store.HasItems(tile) && !store.HasLongReach(tile), "a tile of signs has items and no long-reach one");
            store.Upsert(2, wall);
            Program.Check(store.HasLongReach(tile), "a wall in it: the tile has one");
            var all = new List<ItemRecord>();
            var only = new List<ItemRecord>();
            store.Snapshot(tile, all);
            store.Snapshot(tile, only, true);
            Program.Check(all.Count == 2 && only.Count == 1 && only[0].TypeHash == wall.TypeHash, "the snapshot of the tile for the horizon is the wall alone");
            store.Remove(1);
            Program.Check(store.HasLongReach(tile), "the sign taken away, the wall is still there");
            store.Remove(2);
            Program.Check(!store.HasLongReach(tile) && !store.HasItems(tile), "the wall taken away, the tile has none");

            // An item that changes type (a wall replaced by a sign at the same key) and one that moves to another tile carry the count with them.
            store.Upsert(3, wall);
            store.Upsert(3, sign);
            Program.Check(!store.HasLongReach(tile) && store.HasItems(tile), "a wall that became a sign is not counted");
            store.Upsert(3, wall);
            store.Upsert(3, new ItemRecord(wall.TypeHash, new Vec3(1000f, 0f, 1000f), 0f, 0u, "", 0f, 0u, 0, 0, 0f));
            Program.Check(!store.HasLongReach(tile) && store.HasLongReach(Tiles.Of(1000f, 1000f)), "a wall that moved takes its count to the other tile");
            store.Clear();
            Program.Check(!store.HasLongReach(Tiles.Of(1000f, 1000f)), "clear forgets it");

            // A pass: what was not touched is removed with its count.
            store.Upsert(4, wall);
            store.BeginPass();
            store.EndPass();
            Program.Check(!store.HasLongReach(tile), "a pass that did not see the wall removed it and its count");
        }

        // ------------------------------------------------------------------ the tiers

        private static void Tiers()
        {
            StreamOptions o = StreamOptions.Default;
            Program.Check(o.HorizonMetres >= 2000f && o.HorizonMetres > o.FarMetres, "the horizon is far beyond the far limit: " + o.HorizonMetres);
            Program.Check(TileStreamer.Wanted(o.FarMetres + 1f, TileTier.None, o, true) == TileTier.Horizon, "a tile with a wall just beyond the far limit is at the horizon");
            Program.Check(TileStreamer.Wanted(o.FarMetres + 1f, TileTier.None, o, false) == TileTier.None, "a tile with no wall is nothing there");
            Program.Check(TileStreamer.Wanted(o.FarMetres + 1f, TileTier.None, o) == TileTier.None, "and the three-argument form is as it was");
            Program.Check(TileStreamer.Wanted(o.FarMetres - 1f, TileTier.None, o, true) == TileTier.Far, "inside the far limit a tile with a wall is far, with all its items");
            Program.Check(TileStreamer.Wanted(o.HorizonMetres - 1f, TileTier.None, o, true) == TileTier.Horizon, "up to its limit");
            Program.Check(TileStreamer.Wanted(o.HorizonMetres + 1f, TileTier.None, o, true) == TileTier.None, "beyond it nothing");
            Program.Check(TileStreamer.Wanted(o.HorizonMetres + 1f, TileTier.Horizon, o, true) == TileTier.Horizon, "a horizon tile just past the limit is kept (the hysteresis)");
            Program.Check(TileStreamer.Wanted(o.HorizonMetres * (1f + o.Hysteresis) + 1f, TileTier.Horizon, o, true) == TileTier.None, "and dropped beyond it");
            Program.Check(TileStreamer.Wanted(o.FarMetres + 1f, TileTier.Far, o, true) == TileTier.Far, "a far tile just past the far limit stays far (the hysteresis)");
            Program.Check(TileStreamer.Wanted(o.FarMetres * (1f + o.Hysteresis) + 1f, TileTier.Far, o, true) == TileTier.Horizon, "beyond it, with a wall, it is the horizon's");
            Program.Check(TileStreamer.Wanted(o.FarMetres * (1f + o.Hysteresis) + 1f, TileTier.Far, o, false) == TileTier.None, "with no wall it is dropped, as it was");
            Program.Check(TileTier.Horizon < TileTier.Far && TileTier.Far < TileTier.Mid && TileTier.Mid < TileTier.Near && TileTier.None < TileTier.Horizon, "the tiers are in order of detail");
            Program.Check(TextureBudget.Scaled(o, 0.5f).HorizonMetres == o.HorizonMetres, "the texture budget leaves the horizon alone");
            Program.Check(FaceDetail.SupportOf(TileTier.Horizon) == SupportDetail.Reduced && BarrierLod.Of(TileTier.Near) == BarrierLod.Full && BarrierLod.Of(TileTier.Mid) == BarrierLod.Reduced
                && BarrierLod.Of(TileTier.Far) == BarrierLod.Coarse && BarrierLod.Of(TileTier.Horizon) == BarrierLod.Coarse, "the levels of the tiers");
        }

        private static void Planner()
        {
            StreamOptions o = StreamOptions.Default;
            var wallTile = new TileKey(30, 0);       // 30 * 192 = 5760 m: too far
            var near = new TileKey(10, 0);           // 1920 m from the camera at 0: horizon
            var signsOnly = new TileKey(10, 5);
            var items = new HashSet<TileKey> { near, signsOnly, wallTile };
            var longReach = new HashSet<TileKey> { near, wallTile };
            var built = new Dictionary<TileKey, TileTier>();
            var toBuild = new List<TileWish>();
            var toDrop = new List<TileKey>();
            TileStreamer.PlanAround(items.Contains, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, 0f, 0f, o, toBuild, toDrop, null, longReach);
            Program.Check(toBuild.Count == 1 && toBuild[0].Key.Equals(near) && toBuild[0].Tier == TileTier.Horizon && toDrop.Count == 0, "the wall's tile at 1.9 km is built for the horizon, the tile of signs and the far wall are not: " + toBuild.Count);
            built[near] = TileTier.Horizon;
            toBuild.Clear();
TileStreamer.PlanAround(items.Contains, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, 0f, 0f, o, toBuild, toDrop, null, longReach);
            Program.Check(toBuild.Count == 0 && toDrop.Count == 0, "a plan again changes nothing");
            // Without the long-reach test the plan is as it was: nothing at that distance.
            toBuild.Clear();
            built.Clear();
            TileStreamer.PlanAround(items.Contains, built.Keys, k => TileTier.None, 0f, 0f, o, toBuild, toDrop);
            Program.Check(toBuild.Count == 0, "with no long-reach test the window is the far limit's and nothing is built at 1.9 km");
            // The camera comes to the wall: the tile is built again as near.
            built[near] = TileTier.Horizon;
            toBuild.Clear();
            TileStreamer.PlanAround(items.Contains, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, near.X * Tiles.Size + 10f, 10f, o, toBuild, toDrop, null, longReach);
            Program.Check(toBuild.Count >= 1 && toBuild[0].Key.Equals(near) && toBuild[0].Tier == TileTier.Near, "the camera in the tile: built again, near, with all its items");
            // A tile with a wall beyond the horizon is not looked at: its tier is not asked for (the plan's cost on a map with walls everywhere).
            var asked = new List<TileKey>();
            var farWalls = new HashSet<TileKey>();
            for (int i = 0; i < 200; i++)
                farWalls.Add(new TileKey(100 + i, 0));
            farWalls.Add(near);
            var everything = new HashSet<TileKey>(farWalls);
            toBuild.Clear();
            toDrop.Clear();
            built.Clear();
            TileStreamer.PlanAround(everything.Contains, built.Keys, k => { asked.Add(k); return TileTier.None; }, 0f, 0f, o, toBuild, toDrop, null, farWalls);
            Program.Check(asked.Count == 1 && asked[0].Equals(near) && toBuild.Count == 1, "of 201 tiles with a wall only the one within the horizon's reach is looked at: " + asked.Count);
            // The wall taken away while the tile is at the horizon: dropped.
            toDrop.Clear();
            toBuild.Clear();
            toDrop.Clear();
            built[near] = TileTier.Horizon;
            longReach.Remove(near);
            TileStreamer.PlanAround(items.Contains, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, 0f, 0f, o, toBuild, toDrop, null, longReach);
            Program.Check(toDrop.Contains(near), "a horizon tile whose wall is gone is dropped");
        }

        // ------------------------------------------------------------------ the geometry

        private static void Levels()
        {
            var report = new System.Text.StringBuilder();
            foreach (bool curved in new[] { false, true })
            {
                List<Vec3> line = curved ? Curve() : Straight(120f);
                float metres = Metres(line);
                foreach ((string id, string settings) in Forms)
                {
                    string what = id + (settings.Length > 0 ? "|" + settings : "") + (curved ? " curved" : "");
                    var counts = new int[3];
                    var lo = new Vec3[3];
                    var hi = new Vec3[3];
                    var glassArea = new float[3];
                    for (int level = 0; level < 3; level++)
                    {
                        MarkingPlan plan = PlanOf(id, settings, line, level);
                        counts[level] = Vertices(plan);
                        glassArea[level] = GlassArea(plan);
                        Box(plan, out lo[level], out hi[level]);
                        foreach (MarkingPart part in plan.Parts)
                        {
                            bool finite = true;
                            for (int i = 0; i < part.Mesh.VertexCount; i++)
                                finite &= part.Mesh.Positions[i].IsFinite && part.Mesh.Normals[i].IsFinite;
                            bool inRange = true, whole = part.Mesh.Indices.Count % 3 == 0;
                            foreach (int index in part.Mesh.Indices)
                                inRange &= index >= 0 && index < part.Mesh.VertexCount;
                            Program.Check(finite && inRange && whole, $"{what} level {level}: finite points and normals, indices in range");
                            Program.Check(Wound(part.Mesh), $"{what} level {level}: every triangle faces its normal ({part.Solid} {part.Detail})");
                            Program.Check(!Degenerate(part.Mesh), $"{what} level {level}: no triangle of no area ({part.Solid} {part.Detail})");
                        }

                        // The steel (the posts, the ribs: Metal in these walls) is there up to level 1 and not at level 2, where a wall is its panels.
                        bool steel = plan.Parts.Exists(p => p.Solid == SolidColour.Metal);
                        Program.Check(level == BarrierLod.Coarse ? !steel : steel, $"{what} level {level}: the steel is {(steel ? "there" : "not there")}");
                    }

                    Program.Check(counts[0] > counts[1] && counts[1] > counts[2] && counts[2] > 0, $"{what}: fewer vertices at each level: {counts[0]}, {counts[1]}, {counts[2]}");
                    // Measured: level 1 is 29 to 40 % of level 0; level 2 (a wall as strips along a thinned line, no posts, ribs or joints) is 1.5 to 3.5 % on a straight line and 5 to 13 % on a bend of 60 m radius, which it has to follow.
                    float limit2 = curved ? 0.15f : 0.05f;
                    Program.Check(counts[1] <= counts[0] * 0.45f && counts[2] <= counts[0] * limit2, $"{what}: level 1 at most 45 % of level 0 and level 2 at most {limit2 * 100f:0} %: {counts[1] * 100f / counts[0]:0} %, {counts[2] * 100f / counts[0]:0.0} %");
                    // The sheets of the lower levels are in one piece where level 0 has a frame at each end of a run: never less glass.
                    Program.Check(glassArea[1] >= glassArea[0] - 0.01f && glassArea[2] >= glassArea[0] - 0.01f && (glassArea[0] == 0f || glassArea[1] > glassArea[0] && glassArea[2] > glassArea[0]),
                        $"{what}: the glass is not short of its frames at the lower levels: {glassArea[0]:0.0}, {glassArea[1]:0.0}, {glassArea[2]:0.0} m2");
                    for (int level = 1; level < 3; level++)
                    {
                        // The box of the walls is the same but for what level 0 has proud of it, measured (a straight line along Z and a curved one): across the line the semi enclosure's edge beam (0.27 m), along it
                        // the end posts' plates (0.19 m), up the cap strip and the rib over the roof sheet (0.06 m). A crown that was not a point of the arch would be 0.17 m low.
                        float dx = Math.Max(Math.Abs(lo[level].X - lo[0].X), Math.Abs(hi[level].X - hi[0].X)), dz = Math.Max(Math.Abs(lo[level].Z - lo[0].Z), Math.Abs(hi[level].Z - hi[0].Z));
                        float dy = Math.Max(Math.Abs(lo[level].Y - lo[0].Y), Math.Abs(hi[level].Y - hi[0].Y));
                        Program.Check(dx <= 0.3f && dz <= 0.2f && dy <= 0.06f, $"{what} level {level}: the same box as level 0 within 0.3 m across, 0.2 m along and 0.06 m up: {dx:0.000}, {dz:0.000}, {dy:0.000}");
                    }

                    if (!curved)
                        report.Append($"\n  {what}: {counts[0] / metres:0} / {counts[1] / metres:0} / {counts[2] / metres:0} vertices a metre");
                }
            }

            Console.WriteLine("LOD " + report);
            // A template without levels has its one plan at every level.
            List<Vec3> rail = Straight(40f);
            MarkingPlan a = MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.FretRailingId), MarkingTemplates.WithRun(MarkingTemplates.ById(MarkingTemplates.FretRailingId), MarkingTemplates.ById(MarkingTemplates.FretRailingId).Default().ToString(), RunPath.Encode(rail, out _), 0f, 0f), 0);
            MarkingPlan b = MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.FretRailingId), MarkingTemplates.WithRun(MarkingTemplates.ById(MarkingTemplates.FretRailingId), MarkingTemplates.ById(MarkingTemplates.FretRailingId).Default().ToString(), RunPath.Encode(rail, out _), 0f, 0f), 2);
            Program.Check(ReferenceEquals(a, b), "a railing has the one plan at every level");
            // Level 0 is what it always was: the two lookups that do not name a level give the same plan.
            List<Vec3> wall = Straight(60f);
            MarkingPlan zero = PlanOf(MarkingTemplates.NoiseCoverId, "form=full", wall, 0);
            GuideTemplate cover = MarkingTemplates.ById(MarkingTemplates.NoiseCoverId);
            MarkingPlan plain = MarkingTemplates.PlanOf(cover, MarkingTemplates.WithRun(cover, cover.Default().With("form", "full").ToString(), RunPath.Encode(wall, out _), 0f, 0f));
            Program.Check(ReferenceEquals(zero, plain), "the plan without a level is the plan at level 0");
            Program.Check(!ReferenceEquals(zero, PlanOf(MarkingTemplates.NoiseCoverId, "form=full", wall, 2)), "and level 2 is another plan");
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

        private static bool Degenerate(MeshBuilder mesh)
        {
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
                if (MeshBuilder.Cross(b - a, c - a).Length < 1e-10f)
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ the thinned line

        private static void Thinning()
        {
            // A straight line of 32 chords: the first and the last three points and nothing between.
            var straight = new List<Vec3>();
            for (int i = 0; i <= 32; i++)
                straight.Add(new Vec3(i * 2f, 0f, 0f));
            List<Vec3> thin = BarrierLod.Simplify(straight, BarrierLod.Tolerance);
            Program.Check(thin.Count == 6 && thin[0].X == 0f && thin[1].X == 2f && thin[2].X == 4f && thin[3].X == 60f && thin[5].X == 64f, "a straight line keeps its first and last three points only: " + thin.Count);
            Program.Check(BarrierLod.Simplify(straight, BarrierLod.Tolerance).Count == thin.Count && BarrierLod.Simplify(thin, BarrierLod.Tolerance).Count == thin.Count, "and thinning it again changes nothing");

            // A short line is as it was.
            var shortLine = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(2f, 0f, 0f), new Vec3(4f, 0f, 0f), new Vec3(6f, 0f, 0f), new Vec3(8f, 0f, 0f), new Vec3(10f, 0f, 0f) };
            Program.Check(BarrierLod.Simplify(shortLine, 1f).Count == 6, "a line of six points is as it was");

            // A bend: every point that is further than the tolerance from what stands for it is kept, and no dropped point is.
            foreach (float radius in new[] { 30f, 120f, 600f })
            {
                var arc = new List<Vec3>();
                for (int i = 0; i <= 32; i++)
                {
                    double a = i * 2.0 / radius;
                    arc.Add(new Vec3((float)(radius * Math.Sin(a)), i * 0.01f, (float)(radius * (1.0 - Math.Cos(a)))));
                }

                List<Vec3> kept = BarrierLod.Simplify(arc, BarrierLod.Tolerance);
                float worst = 0f, lengthArc = 0f, lengthKept = 0f;
                foreach (Vec3 p in arc)
                {
                    float nearest = float.MaxValue;
                    for (int i = 0; i + 1 < kept.Count; i++)
                        nearest = Math.Min(nearest, BarrierLod.DistanceToSegment(p, kept[i], kept[i + 1]));
                    worst = Math.Max(worst, nearest);
                }

                for (int i = 1; i < arc.Count; i++)
                    lengthArc += (arc[i] - arc[i - 1]).Length;
                for (int i = 1; i < kept.Count; i++)
                    lengthKept += (kept[i] - kept[i - 1]).Length;
                Program.Check(worst <= BarrierLod.Tolerance + 1e-4f, $"a bend of {radius:0} m: no point is more than the tolerance from the thinned line: {worst * 1000f:0.0} mm");
                // A chord of c m on a bend of r m is c * c / (8 r) off it: 5 cm lets 4 m chords go at 30 m (none dropped), 7 m at 120 m and 15 m at 600 m.
                int most = radius < 100f ? arc.Count : radius < 300f ? arc.Count * 6 / 10 : 12;
                Program.Check(kept.Count <= most && (radius < 100f || kept.Count < arc.Count) && Math.Abs(lengthKept - lengthArc) < lengthArc * 0.002f, $"a bend of {radius:0} m: {kept.Count} of {arc.Count} points, the length kept to 0.2 %");
                bool ends = kept[0].Equals(arc[0]) && kept[1].Equals(arc[1]) && kept[2].Equals(arc[2]) && kept[kept.Count - 1].Equals(arc[arc.Count - 1]) && kept[kept.Count - 2].Equals(arc[arc.Count - 2]) && kept[kept.Count - 3].Equals(arc[arc.Count - 3]);
                Program.Check(ends, $"a bend of {radius:0} m: the first and the last three points are the line's own");
            }

            // The height is kept as the points are: a step in a straight line (a ramp's foot) is a point kept.
            var ramp = new List<Vec3>();
            for (int i = 0; i <= 32; i++)
                ramp.Add(new Vec3(i * 2f, i < 16 ? 0f : (i - 16) * 0.5f, 0f));
            List<Vec3> ramped = BarrierLod.Simplify(ramp, BarrierLod.Tolerance);
            bool foot = false;
            foreach (Vec3 p in ramped)
                foot |= Math.Abs(p.X - 32f) < 1e-3f;
            Program.Check(foot && ramped.Count < ramp.Count, "the foot of a ramp is a point that stays: " + ramped.Count + " points");
            Program.Check(BarrierLod.Simplify(new List<Vec3>(), 0.05f).Count == 0, "no line, no points");
        }

        // ------------------------------------------------------------------ the tile

        private static void HorizonTile()
        {
            GuideTemplate cover = MarkingTemplates.ById(MarkingTemplates.NoiseCoverId);
            SignDef coverDef = SignCatalog.ById(MarkingTemplates.NoiseCoverId);
            SignDef sign = SignCatalog.ById("gb.prohib.01");
            string spec = MarkingTemplates.WithRun(cover, cover.Default().With("form", "full").ToString(), RunPath.Encode(Straight(60f), out _), 0f, 0f);
            var wall = new ItemRecord(coverDef.Hash, new Vec3(96f, 0f, 20f), 0f, 0u, spec, 0f, 0u, 0, 0, 0f);
            var board = new ItemRecord(sign.Hash, new Vec3(80f, 0f, 40f), 0f, 0u, sign.Template != null ? sign.Template.Default().ToString() : "", 0f, 0u, 0, 0, 0f);
            Vec3 origin = Tiles.Centre(Tiles.Of(96f, 20f));
            var items = new List<ItemRecord> { wall, board };

            List<ChunkPart> all = ChunkBuilder.BuildDetailed(items, origin, ChunkDetail.Of(TileTier.Near), out int skippedNear);
            List<ChunkPart> horizon = ChunkBuilder.BuildDetailed(items, origin, ChunkDetail.Of(TileTier.Horizon), out int skippedHorizon);
            List<ChunkPart> far = ChunkBuilder.BuildDetailed(items, origin, ChunkDetail.Of(TileTier.Far), out _);
            int near = 0, onlyWall = 0, farCount = 0;
            bool faces = false;
            foreach (ChunkPart p in all)
                near += p.Mesh.VertexCount;
            foreach (ChunkPart p in horizon)
            {
                onlyWall += p.Mesh.VertexCount;
                faces |= p.Material.StartsWith(ChunkBuilder.FacePrefix, StringComparison.Ordinal);
            }

            foreach (ChunkPart p in far)
                farCount += p.Mesh.VertexCount;
            Program.Check(skippedNear == 0 && skippedHorizon == 0, "nothing is skipped: " + skippedNear + ", " + skippedHorizon);
            Program.Check(onlyWall > 0 && !faces, "the horizon tile has the wall and no sign's face");
            Program.Check(onlyWall < farCount && farCount < near, $"the horizon tile is lighter than the far one, which is lighter than the near: {onlyWall}, {farCount}, {near}");
            // The wall alone at each tier has the level of the tier: the same walls as the plans at the levels, in vertices.
            var wallOnly = new List<ItemRecord> { wall };
            int[] byTier = new int[5];
            foreach (TileTier tier in new[] { TileTier.Near, TileTier.Mid, TileTier.Far, TileTier.Horizon })
            {
                foreach (ChunkPart p in ChunkBuilder.BuildDetailed(wallOnly, origin, ChunkDetail.Of(tier), out _))
                    byTier[(int)tier] += p.Mesh.VertexCount;
            }

            Program.Check(byTier[(int)TileTier.Near] > byTier[(int)TileTier.Mid] && byTier[(int)TileTier.Mid] > byTier[(int)TileTier.Far] && byTier[(int)TileTier.Far] == byTier[(int)TileTier.Horizon] && byTier[(int)TileTier.Horizon] > 0,
                $"the wall alone is lighter at each tier, and the far and the horizon tiles have the same coarse wall: {byTier[(int)TileTier.Near]}, {byTier[(int)TileTier.Mid]}, {byTier[(int)TileTier.Far]}, {byTier[(int)TileTier.Horizon]}");
            List<ChunkPart> signAlone = ChunkBuilder.BuildDetailed(new List<ItemRecord> { board }, origin, ChunkDetail.Of(TileTier.Horizon), out int none);
            Program.Check(signAlone.Count == 0 && none == 0, "a tile of signs alone at the horizon is empty and nothing is skipped");
        }
    }
}
