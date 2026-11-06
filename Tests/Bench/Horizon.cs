using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What the horizon tier costs (BENCH_HORIZON=1, 0.12.73; the owner: "全封闭隔音墙离远了就消失了"): the plan with the horizon's window against the far limit's, on a map of tens of thousands of tiles; and one
    /// kilometre of each noise wall at each level of detail: vertices, an estimate of the memory, and how many tiles and parts (draw calls) it is made of at the horizon.
    /// </summary>
    internal static class Horizon
    {
        /// <summary>One tile in this many has a wall: 100 is a city with some 40 km of wall, 10 an extreme of 400 km.</summary>
        private static int WallShare = 100;

        public static void Run()
        {
            Plans();
            WallShare = 10;
            Plans();
            Walls();
        }

        private static double Us(int repeats, Action action)
        {
            action();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                action();
            return clock.Elapsed.TotalMilliseconds * 1000.0 / repeats;
        }

        private static void Plans()
        {
            // A map of 200 x 200 tiles (38 km a side) that all have items, a tenth of them with a wall: the plan looks up each tile of the window.
            var items = new HashSet<TileKey>();
            var walls = new HashSet<TileKey>();
            var rng = new Random(5);
            for (int x = -100; x < 100; x++)
            {
                for (int z = -100; z < 100; z++)
                {
                    var key = new TileKey(x, z);
                    items.Add(key);
                    if (rng.Next(WallShare) == 0)
                        walls.Add(key);
                }
            }

            var built = new Dictionary<TileKey, TileTier>();
            var toBuild = new List<TileWish>();
            var toDrop = new List<TileKey>();
            Func<TileKey, TileTier> current = k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None;
            StreamOptions options = StreamOptions.Default;
            var withoutWalls = Us(2000, () => { toBuild.Clear(); toDrop.Clear(); TileStreamer.PlanAround(items.Contains, built.Keys, current, 0f, 0f, options, toBuild, toDrop); });
            int farTiles = toBuild.Count;
            var withWalls = Us(2000, () => { toBuild.Clear(); toDrop.Clear(); TileStreamer.PlanAround(items.Contains, built.Keys, current, 0f, 0f, options, toBuild, toDrop, null, walls); });
            int horizonTiles = toBuild.Count;
            foreach (TileWish wish in toBuild)
                built[wish.Key] = wish.Tier;
            var steady = Us(2000, () => { toBuild.Clear(); toDrop.Clear(); TileStreamer.PlanAround(items.Contains, built.Keys, current, 0f, 0f, options, toBuild, toDrop, null, walls); });
            Console.WriteLine($"plan, a map of {items.Count} tiles with items, {walls.Count} with a wall (1 in {WallShare}): the far limit's window {withoutWalls:0.0} us ({farTiles} tiles to build), the horizon's {withWalls:0.0} us ({horizonTiles} tiles to build), "
                + $"with those built {steady:0.0} us ({built.Count} built tiles walked); a plan is made every 6 frames at most, so {steady / 6.0:0.0} us a frame");
        }

        private static List<ItemRecord> Wall(GuideTemplate template, string settings, float length)
        {
            var records = new List<ItemRecord>();
            GuideSpec spec = template.Default();
            foreach (string pair in settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            const float piece = 60f;
            SignDef def = SignCatalog.ById(template.Id);
            for (float from = 0f; from < length - 0.1f; from += piece)
            {
                var line = new List<Vec3>();
                for (float s = from; s <= Math.Min(length, from + piece) + 0.01f; s += 2f)
                    line.Add(new Vec3(s, 0f, 0f));
                string path = RunPath.Encode(line, out float yaw);
                records.Add(new ItemRecord(def.Hash, line[0], yaw, 0u, MarkingTemplates.WithRun(template, spec.ToString(), path, from, length), 0f, 0u, 0, 0, 0f));
            }

            return records;
        }

        private static void Walls()
        {
            foreach ((string id, string settings) in new[]
            {
                (MarkingTemplates.NoiseBarrierId, ""), (MarkingTemplates.NoiseCoverId, "form=full"), (MarkingTemplates.NoiseCoverId, "form=semi"), (MarkingTemplates.CementNoiseId, "band=upper"),
                (MarkingTemplates.BrickNoiseId, "band=mid"), (MarkingTemplates.GreenNoiseId, "")
            })
            {
                GuideTemplate template = MarkingTemplates.ById(id);
                List<ItemRecord> records = Wall(template, settings, 1000f);
                var text = new System.Text.StringBuilder();
                foreach (TileTier tier in new[] { TileTier.Near, TileTier.Mid, TileTier.Far, TileTier.Horizon })
                {
                    var byTile = new Dictionary<TileKey, List<ItemRecord>>();
                    foreach (ItemRecord record in records)
                    {
                        TileKey key = Tiles.Of(record.Position.X, record.Position.Z);
                        if (!byTile.TryGetValue(key, out List<ItemRecord> list))
                            byTile[key] = list = new List<ItemRecord>();
                        list.Add(record);
                    }

                    long vertices = 0, indices = 0;
                    int parts = 0;
                    var clock = Stopwatch.StartNew();
                    foreach (KeyValuePair<TileKey, List<ItemRecord>> pair in byTile)
                    {
                        foreach (ChunkPart part in ChunkBuilder.BuildDetailed(pair.Value, Tiles.Centre(pair.Key), ChunkDetail.Of(tier), out _, 30000))
                        {
                            vertices += part.Mesh.VertexCount;
                            indices += part.Mesh.Indices.Count;
                            parts++;
                        }
                    }

                    // 48 bytes a vertex (position, normal, uv, tangent) and 2 bytes an index (4 over 65 000 vertices in a mesh: the parts here are cut at 30 000).
                    double mb = (vertices * 48.0 + indices * 2.0) / 1048576.0;
                    text.Append($"\n    {tier,-7}: {vertices / 1000.0:0.0}k vertices ({vertices / 1000.0:0} a metre), {mb:0.0} MB, {byTile.Count} tiles, {parts} parts (draw calls), built in {clock.Elapsed.TotalMilliseconds:0} ms on a worker");
                }

                Console.WriteLine($"1 km of {id}{(settings.Length > 0 ? " " + settings : "")} ({records.Count} items):{text}");
            }
        }
    }
}
