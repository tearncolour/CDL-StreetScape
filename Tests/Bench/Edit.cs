using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What changing a support costs while it is edited or shown as the next to place (BENCH_EDIT=1; 0.12.58): every support built from scratch for a board on it in full and in reduced detail (its
    /// time and vertices), the cache's hit (what a tile pays for a support it has), and a tile with a number of the supports put together (what an edit makes a worker do again). Pure parts only.
    /// </summary>
    internal static class Edit
    {
        private static double Time(int repeats, Action action)
        {
            action();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                action();
            return clock.Elapsed.TotalMilliseconds / repeats;
        }

        public static void Run()
        {
            Console.WriteLine("building a support for a board, milliseconds (full | reduced) and vertices of metal + solids:");
            SignDef plate = SignCatalog.ById("gb17733.street"), wide = SignCatalog.ById("gb.exit.adv.2km") != null ? SignCatalog.ById("gb.exit.adv.2km") : SignCatalog.ById("guide.exit.advance");
            double total = 0;
            foreach (SupportDef support in SupportCatalog.All)
            {
                SignDef def = SupportKinds.Hung(support.Kind) && wide != null ? wide : plate;
                SignInstance instance = SignInstances.Of(def, null);
                SignParts full = null, reduced = null;
                double a = Time(20, () => full = SignBuilder.Build(instance, support, 0f, SupportDetail.Full));
                double b = Time(20, () => reduced = SignBuilder.Build(instance, support, 0f, SupportDetail.Reduced));
                int solids = 0;
                foreach (MeshBuilder s in full.Solids)
                    solids += s?.VertexCount ?? 0;
                total += a;
                Console.WriteLine($"  {support.Id,-24} {a,7:0.00} | {b,6:0.00} ms   {full.Metal.VertexCount,6} metal {solids,5} solids, reduced {reduced.Metal.VertexCount,5}");
            }

            Console.WriteLine($"  all supports once: {total:0.0} ms");

            Console.WriteLine("a tile of supports (what an edit of one makes a worker do again), milliseconds:");
            foreach (int n in new[] { 10, 50, 200 })
            {
                var items = new List<ItemRecord>();
                var supports = new List<SupportDef>(SupportCatalog.Offerable);
                for (int i = 0; i < n; i++)
                {
                    SupportDef support = supports[i % supports.Count];
                    SignDef def = SupportKinds.Hung(support.Kind) && wide != null ? wide : plate;
                    items.Add(new ItemRecord(def.Hash, new Vec3(i * 6f, 0f, 0f), 0f, support.Hash));
                }

                double cold = Time(1, () =>
                {
                    ChunkBuilder.ClearCaches();
                    ChunkBuilder.Build(items, Vec3.Zero, out _);
                });
                double warm = Time(5, () => ChunkBuilder.Build(items, Vec3.Zero, out _));
                // One item of them changed (its style): every other support is in the cache.
                ItemRecord changed = items[0];
                int flip = 0;
                double edited = Time(5, () =>
                {
                    changed.Style = (byte)(++flip & 3);
                    items[0] = changed;
                    ChunkBuilder.Build(items, Vec3.Zero, out _);
                });
                Console.WriteLine($"  {n,4} supports: cold {cold,8:0.0}   warm {warm,8:0.0}   one edited {edited,8:0.0}");
            }
        }
    }
}
