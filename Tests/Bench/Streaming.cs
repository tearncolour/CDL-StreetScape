using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// The same city built the way 0.6.1 did (every tile of the map, full size) and the way 0.7.0 does (the tiles near the camera, each at the detail of its distance), measured on the runtime this is
    /// run with: the pure parts of the work only (the records, the plan, the meshes of the tiles, the sizes of the pictures); what the game adds (Unity objects, painting, uploading) is counted as
    /// the number and size of what it would be asked to make. The city is dense in the middle and thin at the edges (a normal distribution round the camera), which is how a map is used.
    /// </summary>
    internal static class Streaming
    {
        private static readonly Random Rng = new Random(424242);

        private static double Gauss() => Math.Sqrt(-2.0 * Math.Log(1.0 - Rng.NextDouble())) * Math.Cos(2.0 * Math.PI * Rng.NextDouble());

        private static string Spec(SignDef def)
        {
            if (def.Template == null)
                return null;
            GuideSpec spec = def.Template.Default();
            foreach (GuideField field in def.Template.Fields)
            {
                if (Rng.Next(3) != 0)
                    continue;
                if (field.Kind == GuideFieldKind.Choice && field.Choices != null && field.Choices.Length > 0)
                {
                    spec = spec.With(field.Key, field.Choices[Rng.Next(field.Choices.Length)].Value);
                }
                else if (field.Kind == GuideFieldKind.Text)
                {
                    int length = Math.Max(1, Math.Min(field.MaxLength, 1 + Rng.Next(4)));
                    var chars = new char[length];
                    for (int i = 0; i < length; i++)
                        chars[i] = field.DigitsOnly ? (char)('0' + Rng.Next(10)) : (char)('A' + Rng.Next(26));
                    spec = spec.With(field.Key, field.Sanitize(new string(chars)));
                }
            }

            return spec.ToString();
        }

        public static void Run(int n, float sigma)
        {
            var defs = new List<SignDef>(SignCatalog.All);
            var store = new ItemStore<int>();
            var clock = Stopwatch.StartNew();
            var records = new List<ItemRecord>(n);
            for (int i = 0; i < n; i++)
            {
                SignDef def = defs[Rng.Next(defs.Count)];
                var p = new Vec3((float)(Gauss() * sigma), 0f, (float)(Gauss() * sigma));
                records.Add(new ItemRecord(def.Hash, p, (float)(Rng.NextDouble() * Math.PI * 2), 0u, Spec(def)));
            }

            double makeMs = clock.Elapsed.TotalMilliseconds;

            // The first read of the game's items into the store (what the sliced pass does, without the game's part).
            GC.Collect();
            long heap0 = GC.GetTotalMemory(true);
            clock.Restart();
            for (int i = 0; i < n; i++)
                store.Upsert(i, records[i]);
            double readMs = clock.Elapsed.TotalMilliseconds;
            long heap1 = GC.GetTotalMemory(false);

            ChunkBuilder.ClearCaches();
            GC.Collect();
            long heapBase = GC.GetTotalMemory(true);

            // 0.6.1: every tile at full size.
            clock.Restart();
            var oldStats = BuildAll(store, null, 0f, 0f, false);
            double oldMs = clock.Elapsed.TotalMilliseconds;

            GC.Collect();
            long heapOld = GC.GetTotalMemory(true) - heapBase;
            ChunkBuilder.ClearCaches();
            GC.Collect();
            long heapBase2 = GC.GetTotalMemory(true);

            // 0.7.0: the plan, and the tiles it asks for.
            var built = new Dictionary<TileKey, TileTier>();
            var toBuild = new List<TileWish>();
            var toDrop = new List<TileKey>();
            clock.Restart();
            const int plans = 50;
            for (int i = 0; i < plans; i++)
            {
                toBuild.Clear();
                toDrop.Clear();
                TileStreamer.PlanAround(store.HasItems, built.Keys, k => TileTier.None, (float)(Rng.NextDouble() * 200 - 100), (float)(Rng.NextDouble() * 200 - 100), StreamOptions.Default, toBuild, toDrop);
            }

            double planMs = clock.Elapsed.TotalMilliseconds / plans;
            toBuild.Clear();
            TileStreamer.PlanAround(store.HasItems, built.Keys, k => TileTier.None, 0f, 0f, StreamOptions.Default, toBuild, toDrop);

            clock.Restart();
            var newStats = BuildAll(store, toBuild, 0f, 0f, true);
            double newMs = clock.Elapsed.TotalMilliseconds;
            GC.Collect();
            long heapNew = GC.GetTotalMemory(true) - heapBase2;

            Console.WriteLine($"{n,6} signs (sigma {sigma:0} m): {store.TileCount} tiles; first read into the store {readMs:0} ms ({(heap1 - heap0) / 1048576} MB of records); a plan round the camera {planMs:0.00} ms");
            Console.WriteLine($"         0.6.1, all tiles at full size : {oldStats.Tiles} tiles, {oldMs:0} ms of building, {oldStats.Vertices / 1000000.0:0.0}M vertices ({oldStats.Vertices * 32 / 1048576} MB), {oldStats.Parts} renderers, {oldStats.Faces} different pictures = {oldStats.FaceBytes / 1048576} MB");
            Console.WriteLine($"         0.7.0, by distance            : {newStats.Tiles} tiles, {newMs:0} ms of building (on workers), {newStats.Vertices / 1000000.0:0.0}M vertices ({newStats.Vertices * 32 / 1048576} MB), {newStats.Parts} renderers, {newStats.Faces} different pictures = {newStats.FaceBytes / 1048576} MB as RGBA32, {newStats.FaceBc1Bytes / 1048576} MB as BC1 (0.12.27)");
            Console.WriteLine($"         managed memory held by the caches afterwards: 0.6.1 {heapOld / 1048576} MB, 0.7.0 {heapNew / 1048576} MB");
            Console.WriteLine($"                                         near {newStats.NearTiles} / middle {newStats.MidTiles} / far {newStats.FarTiles} tiles; pictures full {newStats.FaceFull} / half {newStats.FaceHalf} / small {newStats.FaceSmall}");

            // The same tiles on four threads: the same meshes (counts), and how long it takes.
            if (toBuild.Count > 0)
                Threaded(store, toBuild, newStats);
        }

        private sealed class Stats
        {
            public int Tiles, Parts, Faces, NearTiles, MidTiles, FarTiles, FaceFull, FaceHalf, FaceSmall;
            public long Vertices, FaceBytes, FaceBc1Bytes;
        }

        private static Stats BuildAll(ItemStore<int> store, List<TileWish> wishes, float camX, float camZ, bool tiered)
        {
            var stats = new Stats();
            var faces = new HashSet<string>();
            var list = new List<ItemRecord>();
            IEnumerable<KeyValuePair<TileKey, TileTier>> tiles;
            var plan = new List<KeyValuePair<TileKey, TileTier>>();
            if (wishes == null)
            {
                foreach (TileKey key in store.TileKeys)
                    plan.Add(new KeyValuePair<TileKey, TileTier>(key, TileTier.Near));
            }
            else
            {
                foreach (TileWish wish in wishes)
                    plan.Add(new KeyValuePair<TileKey, TileTier>(wish.Key, wish.Tier));
            }

            tiles = plan;
            foreach (KeyValuePair<TileKey, TileTier> pair in tiles)
            {
                list.Clear();
                store.Snapshot(pair.Key, list);
                ChunkDetail detail = tiered ? ChunkDetail.Of(pair.Value) : ChunkDetail.Full;
                List<ChunkPart> parts = ChunkBuilder.BuildDetailed(list, Tiles.Centre(pair.Key), detail, out _, 30000);
                stats.Tiles++;
                if (pair.Value == TileTier.Near) stats.NearTiles++; else if (pair.Value == TileTier.Mid) stats.MidTiles++; else stats.FarTiles++;
                foreach (ChunkPart part in parts)
                {
                    stats.Vertices += part.Mesh.VertexCount;
                    stats.Parts++;
                    if (FaceDetail.TryParse(part.Material, out string key, out int level) && faces.Add(part.Material))
                    {
                        SignInstance instance = SignInstances.FromKey(key);
                        if (instance == null || FaceDetail.NeverPainted(instance, level))
                            continue;
                        SignInstance sized = FaceDetail.Sized(instance, FaceDetail.MaxSide(level));
                        long bytes = (long)sized.TexW * sized.TexH * 4 * 4 / 3;
                        stats.FaceBytes += bytes;
                        // As the game holds them since 0.12.27: BC1 with the mips when the picture is whole blocks (every board of the catalogue is), else RGBA32.
                        stats.FaceBc1Bytes += BlockCompress.CanEncode(sized.TexW, sized.TexH) ? BlockCompress.Bc1ChainSize(sized.TexW, sized.TexH) : bytes;
                        if (level == FaceDetail.Full) stats.FaceFull++; else if (level == FaceDetail.Half) stats.FaceHalf++; else stats.FaceSmall++;
                    }
                }
            }

            stats.Faces = faces.Count;
            return stats;
        }

        private static void Threaded(ItemStore<int> store, List<TileWish> wishes, Stats expected)
        {
            // Snapshots are taken on this thread, the builds are done on four.
            var jobs = new List<KeyValuePair<TileWish, List<ItemRecord>>>();
            foreach (TileWish wish in wishes)
            {
                var items = new List<ItemRecord>();
                store.Snapshot(wish.Key, items);
                jobs.Add(new KeyValuePair<TileWish, List<ItemRecord>>(wish, items));
            }

            long vertices = 0, parts = 0;
            int errors = 0, next = -1;
            var clock = Stopwatch.StartNew();
            var threads = new List<Thread>();
            for (int t = 0; t < 4; t++)
            {
                var thread = new Thread(() =>
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= jobs.Count)
                            return;
                        try
                        {
                            TileWish wish = jobs[i].Key;
                            List<ChunkPart> built = ChunkBuilder.BuildDetailed(jobs[i].Value, Tiles.Centre(wish.Key), ChunkDetail.Of(wish.Tier), out _, 30000);
                            long v = 0;
                            foreach (ChunkPart part in built)
                                v += part.Mesh.VertexCount;
                            Interlocked.Add(ref vertices, v);
                            Interlocked.Add(ref parts, built.Count);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errors);
                            Console.WriteLine("  a thread failed: " + ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                });
                threads.Add(thread);
                thread.Start();
            }

            foreach (Thread thread in threads)
                thread.Join();
            bool same = vertices == expected.Vertices && parts == expected.Parts;
            Console.WriteLine($"                                         on 4 threads: {clock.Elapsed.TotalMilliseconds:0} ms, {errors} error(s), {(same ? "the same meshes" : "DIFFERENT MESHES: " + vertices + " vertices, " + parts + " parts")}");
        }
    }
}
