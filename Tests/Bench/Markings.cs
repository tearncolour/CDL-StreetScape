using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What the road markings cost (BENCH_MARKINGS=1): making the paint of each kind (a plan, once per setting), a city's worth of markings built into tiles, pointing at them, and the triangles and vertices
    /// they put on the screen. Pure parts only, on this runtime (Unity's Mono when run with it).
    /// </summary>
    internal static class Markings
    {
        private static double Time(int repeats, Action action)
        {
            action();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                action();
            return clock.Elapsed.TotalMilliseconds * 1000.0 / repeats;
        }

        private static void Line(string what, double microseconds) => Console.WriteLine($"  {what,-78} {microseconds,10:0.0} us");

        public static void Run()
        {
            Console.WriteLine("road markings: making the paint of one item from its settings (no cache), microseconds:");
            var worst = new List<(string, double, int)>();
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                string spec = t.Default().ToString();
                if (MarkingTemplates.IsRun(t))
                {
                    // A run piece of 60 m with a bend: what most of a city's lines are.
                    var arc = new List<Vec3>();
                    for (int i = 0; i <= 30; i++)
                        arc.Add(new Vec3((float)(200.0 * (1.0 - Math.Cos(i * 2.0 / 200.0))), 0f, (float)(200.0 * Math.Sin(i * 2.0 / 200.0))));
                    spec = MarkingTemplates.WithRun(t, spec, RunPath.Encode(arc, out _), 0f, 60f);
                }

                GuideSpec parsed = t.Parse(spec);
                MarkingPlan plan = null;
                double us = Time(20, () =>
                {
                    plan = t.Marking(parsed);
                    plan.Bake(0f);
                });
                worst.Add((t.Id, us, plan.TriangleCount));
            }

            worst.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            foreach ((string id, double us, int count) in worst.GetRange(0, Math.Min(15, worst.Count)))
                Line($"{id} ({count} triangles)", us);
            double sum = 0;
            int triangles = 0;
            foreach ((string _, double us, int count) in worst)
            {
                sum += us;
                triangles += count;
            }

            Line($"all {worst.Count} kinds once, {triangles} triangles", sum);

            // A city: a grid of 40 x 40 roads 120 m apart, every block's approaches with a stop line, a crosswalk and three arrows, every road with lane lines in pieces, an island at every fourth junction.
            var records = new List<ItemRecord>();
            var rng = new Random(20261020);
            GuideTemplate lane = MarkingTemplates.ById("mk.line.lane"), stop = MarkingTemplates.ById("mk.stop"), walk = MarkingTemplates.ById("mk.crosswalk"), island = MarkingTemplates.ById("mk.channel");
            SignDef laneDef = SignCatalog.ById("mk.line.lane"), stopDef = SignCatalog.ById("mk.stop"), walkDef = SignCatalog.ById("mk.crosswalk"), islandDef = SignCatalog.ById("mk.channel");
            SignDef[] arrows = { SignCatalog.ById("mk.arrow.left"), SignCatalog.ById("mk.arrow.straight"), SignCatalog.ById("mk.arrow.right") };
            var piece = new List<Vec3>();
            for (int i = 0; i <= 25; i++)
                piece.Add(new Vec3(0f, 0f, i * 4f));
            string lanePath = RunPath.Encode(piece, out _);
            for (int gx = 0; gx < 40; gx++)
            {
                for (int gz = 0; gz < 40; gz++)
                {
                    float x = gx * 120f, z = gz * 120f;
                    // Every piece of a line has a path of its own (a city’s roads bend each their own way): the settings differ, and the plans are not shared.
                    for (int k = 0; k < 4; k++)
                    {
                        var own = new List<Vec3>();
                        // A gentle curve of its own: two numbers (2 cm steps) that no two pieces share, up to 3.3 m aside over the 100 m.
                        int id = (gx * 40 + gz) * 4 + k;
                        double bend = 0.02 * (id % 100), twist = 0.02 * (id / 100);
                        for (int i = 0; i <= 25; i++)
                        {
                            double s = i / 25.0;
                            own.Add(new Vec3((float)(bend * s * s + twist * s * s * s), 0f, i * 4f));
                        }
                        records.Add(new ItemRecord(laneDef.Hash, new Vec3(x + 1.75f + 3.5f * (k % 2) - 3.5f * (k / 2), 0f, z + 10f), 0f, 0u, MarkingTemplates.WithRun(lane, lane.Default().ToString(), RunPath.Encode(own, out _), 0f, 100f)));
                    }
                    records.Add(new ItemRecord(stopDef.Hash, new Vec3(x + 3.5f, 0f, z + 112f), 0f, 0u, stop.Default().ToString()));
                    records.Add(new ItemRecord(walkDef.Hash, new Vec3(x, 0f, z + 115f), 0f, 0u, walk.Default().ToString()));
                    for (int a = 0; a < 3; a++)
                        records.Add(new ItemRecord(arrows[a].Hash, new Vec3(x + 1.75f + 3.5f * a, 0f, z + 100f), 0f, 0u, arrows[a].Template.Default().ToString()));
                    if ((gx + gz) % 4 == 0)
                    {
                        var corners = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(4f, 0f, 2f), new Vec3(6f, 0f, 12f), new Vec3(2f, 0f, 20f), new Vec3(-1f, 0f, 10f) };
                        records.Add(new ItemRecord(islandDef.Hash, new Vec3(x - 20f, 0f, z + 60f), (float)rng.NextDouble(), 0u, MarkingTemplates.WithPolygon(island, island.Default().ToString(), MarkingPolygon.Encode(corners))));
                    }
                }
            }

            Console.WriteLine($"a city of {records.Count} markings (lane lines, stop lines, crosswalks, arrows, islands) on 40 x 40 junctions:");
            MarkingTemplates.ClearCaches();
            var clock = Stopwatch.StartNew();
            int skipped = 0, vertices = 0, tris = 0, parts = 0;
            // Tiles of 192 m as the renderer cuts them.
            var tiles = new Dictionary<(int, int), List<ItemRecord>>();
            foreach (ItemRecord r in records)
            {
                var key = ((int)Math.Floor(r.Position.X / 192f), (int)Math.Floor(r.Position.Z / 192f));
                if (!tiles.TryGetValue(key, out List<ItemRecord> list))
                    tiles[key] = list = new List<ItemRecord>();
                list.Add(r);
            }

            double coldMs = 0, warmMs = 0, worstTile = 0;
            foreach (KeyValuePair<(int, int), List<ItemRecord>> tile in tiles)
            {
                var tileClock = Stopwatch.StartNew();
                List<ChunkPart> built = ChunkBuilder.Build(tile.Value, new Vec3(tile.Key.Item1 * 192f, 0f, tile.Key.Item2 * 192f), out int s);
                double ms = tileClock.Elapsed.TotalMilliseconds;
                coldMs += ms;
                worstTile = Math.Max(worstTile, ms);
                skipped += s;
                foreach (ChunkPart p in built)
                {
                    parts++;
                    vertices += p.Mesh.VertexCount;
                    tris += p.Mesh.TriangleCount;
                }
            }

            foreach (KeyValuePair<(int, int), List<ItemRecord>> tile in tiles)
            {
                var tileClock = Stopwatch.StartNew();
                ChunkBuilder.Build(tile.Value, new Vec3(tile.Key.Item1 * 192f, 0f, tile.Key.Item2 * 192f), out _);
                warmMs += tileClock.Elapsed.TotalMilliseconds;
            }

            Console.WriteLine($"  {tiles.Count} tiles: built the first time in {coldMs:0} ms (worst tile {worstTile:0.0} ms), again with the plans kept in {warmMs:0} ms; {parts} meshes, {vertices} vertices, {tris} triangles; {skipped} skipped");
            Console.WriteLine($"  per marking: {(double)vertices / records.Count:0.0} vertices, {(double)tris / records.Count:0.0} triangles; in {clock.Elapsed.TotalMilliseconds:0} ms in all");
            Console.WriteLine($"  plans kept: {MarkingTemplates.CachedPlans}, {MarkingTemplates.CachedVertices} vertices");

            // Pointing: the pick shapes of a marking, as FindUnderCursor builds them for each candidate.
            ItemRecord sample = records[0];
            Line("PartsOfRecord of a lane line piece (pick boxes; plan kept)", Time(2000, () => ChunkBuilder.PartsOfRecord(sample)));
            ItemRecord islandSample = records.Find(r => r.TypeHash == islandDef.Hash);
            Line("PartsOfRecord of an island (pick boxes; plan kept)", Time(2000, () => ChunkBuilder.PartsOfRecord(islandSample)));
            Line("PlanOf from the cache (a lane line piece)", Time(20000, () => MarkingTemplates.PlanOf(lane, sample.Spec)));
        }
    }
}
