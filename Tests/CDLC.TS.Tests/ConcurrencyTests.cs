using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Tiles are built on worker threads (0.7.0) while the main thread asks for the same caches every frame in the tool's modes. The build of one sign must not make another thread wait for it
    /// (an LED screen takes a tenth of a second to build), and tiles built at the same time on several threads must come out as they do on one.
    /// </summary>
    internal static class ConcurrencyTests
    {
        public static void RunAll()
        {
            Program.Run("concurrency: a slow build does not hold up another thread's", SlowBuild);
            Program.Run("concurrency: threads that ask for the same new board get one object", OneBoard);
            Program.Run("concurrency: tiles built on four threads are the tiles built on one, with the caches turning over", SameTiles);
        }

        private static SignDef Def(string id)
        {
            SignDef def = SignCatalog.ById(id);
            if (def == null)
                throw new InvalidOperationException("no sign " + id);
            return def;
        }

        /// <summary>A board of this sign that no other test has: the first text field of its template set to digits.</summary>
        private static string Variant(string id, string digits)
        {
            SignDef def = Def(id);
            GuideSpec spec = def.Template.Default();
            foreach (GuideField field in def.Template.Fields)
            {
                if (field.Kind == GuideFieldKind.Text)
                    return spec.With(field.Key, field.Sanitize(digits)).ToString();
            }

            throw new InvalidOperationException(id + " has no text field");
        }

        private static void SlowBuild()
        {
            Func<SignInstance, LedGrid> keep = SignBuilder.LedGrids;
            try
            {
                var started = new ManualResetEvent(false);
                SignBuilder.LedGrids = instance =>
                {
                    started.Set();
                    Thread.Sleep(400);
                    var grid = new LedGrid(6, 4);
                    grid.Dots[1] = LedColour.Red;
                    return grid;
                };

                SignInstance screen = SignInstances.Of(Def("gb.vms.text"), Variant("gb.vms.text", "7771"));
                SignInstance other = SignInstances.Of(Def("gb.prohib.09"), null);
                SignInstance otherToo = SignInstances.Of(Def("gb.warn.01"), null);
                
                SignParts slow = null;
                var worker = new Thread(() => slow = ChunkBuilder.PartsOf(screen, SupportCatalog.ById(SupportCatalog.Gantry).Hash, 0f, SupportDetail.Full));
                worker.Start();
                Program.Check(started.WaitOne(5000), "the slow build has started");
                Program.Check(!ReferenceEquals(screen, SignInstances.Of(Def("gb.vms.text"), null)), "(the slow screen is a board of its own)");

                // While it sleeps, this thread gets other parts at once, both a cached kind and a new one, and a group.
                var clock = Stopwatch.StartNew();
                SignParts a = ChunkBuilder.PartsOf(other, 0u, 0f, SupportDetail.Full);
                SignParts b = ChunkBuilder.PartsOf(otherToo, SupportCatalog.ById(SupportCatalog.Cantilever).Hash, 0f, SupportDetail.Reduced);
                SignParts c = ChunkBuilder.PartsOfGroup(new[] { new ItemRecord(Def("gb.prohib.09").Hash, Vec3.Zero, 0f, 0u, null, 0f, 5u, 0), new ItemRecord(Def("gb.warn.01").Hash, Vec3.Zero, 0f, 0u, null, 0f, 5u, 1) });
                double ms = clock.Elapsed.TotalMilliseconds;
                Program.Check(a != null && b != null && c != null, "the other parts are made");
                Program.Check(ms < 200.0, "and they were not held up by the 400 ms build in the other thread: " + ms.ToString("0") + " ms");

                worker.Join();
                Program.Check(slow != null && slow.Faces.Count == 1, "the slow build finishes");
                SignParts again = ChunkBuilder.PartsOf(screen, SupportCatalog.ById(SupportCatalog.Gantry).Hash, 0f, SupportDetail.Full);
                Program.Check(ReferenceEquals(again, slow), "and it is kept: asked again it is the same object");

                // Two threads that want the same slow one at once: both get a result, and they get the same object (the first to finish is the one that stays).
                SignInstance screen2 = SignInstances.Of(Def("gb.vms.text"), Variant("gb.vms.text", "7772"));
                SignParts r1 = null, r2 = null;
                var t1 = new Thread(() => r1 = ChunkBuilder.PartsOf(screen2, 0u, 0f, SupportDetail.Full));
                var t2 = new Thread(() => r2 = ChunkBuilder.PartsOf(screen2, 0u, 0f, SupportDetail.Full));
                t1.Start();
                t2.Start();
                t1.Join();
                t2.Join();
                Program.Check(r1 != null && r2 != null && ReferenceEquals(r1, r2), "both threads get the same parts: the first to finish is the one that stays");
                SignParts kept = ChunkBuilder.PartsOf(screen2, 0u, 0f, SupportDetail.Full);
                Program.Check(ReferenceEquals(kept, r1) || ReferenceEquals(kept, r2), "and the one that is kept is the one of a thread that built it");
            }
            finally
            {
                SignBuilder.LedGrids = keep;
            }
        }

        private static void OneBoard()
        {
            // Eight threads ask at the same moment for each of many boards that nobody has made: whichever makes it, all get the same object (one board is one object: the face materials and the meshes are
            // named by it, and a board that existed twice would be a second picture).
            SignInstances.ClearCaches();
            SignDef def = Def("guide.exit.advance");
            GuideSpec defaults = def.Template.Default();
            const int boards = 200, threads = 8;
            var results = new SignInstance[threads, boards];
            var start = new ManualResetEvent(false);
            var workers = new List<Thread>();
            for (int t = 0; t < threads; t++)
            {
                int me = t;
                var thread = new Thread(() =>
                {
                    start.WaitOne();
                    for (int i = 0; i < boards; i++)
                        results[me, i] = SignInstances.Of(def, defaults.With("to1", "同" + i).With("dist", (i % 9 + 1).ToString()).ToString());
                });
                workers.Add(thread);
                thread.Start();
            }

            start.Set();
            foreach (Thread thread in workers)
                thread.Join();
            int different = 0;
            for (int i = 0; i < boards; i++)
            {
                for (int t = 1; t < threads; t++)
                {
                    if (!ReferenceEquals(results[0, i], results[t, i]))
                        different++;
                }
            }

            Program.Check(different == 0, different + " board(s) came out as more than one object when threads asked at once");
        }

        private static string Fingerprint(List<ChunkPart> parts)
        {
            var lines = new List<string>();
            foreach (ChunkPart part in parts)
                lines.Add(part.Material + ":" + part.Mesh.VertexCount + ":" + part.Mesh.Indices.Count);
            lines.Sort(StringComparer.Ordinal);
            return string.Join("|", lines);
        }

        private static void SameTiles()
        {
            // Nothing kept by an earlier test (the caches are global): the tiles are built from what they are made of, here and on the threads.
            ChunkBuilder.ClearCaches();
            Func<SignInstance, LedGrid> keepGrids = SignBuilder.LedGrids;
            SignBuilder.LedGrids = i =>
            {
                var grid = new LedGrid(6, 4);
                grid.Dots[2] = LedColour.Amber;
                return grid;
            };
            try
            {
                SameTilesCore();
            }
            finally
            {
                SignBuilder.LedGrids = keepGrids;
                ChunkBuilder.ClearCaches();
            }
        }

        private static void SameTilesCore()
        {
            // Many different boards (more than a generation of the caches holds), in tiles of 40 items each.
            var rng = new Random(9);
            var defs = new List<SignDef>(SignCatalog.All);
            var tiles = new List<List<ItemRecord>>();
            for (int t = 0; t < 36; t++)
            {
                var items = new List<ItemRecord>();
                for (int i = 0; i < 40; i++)
                {
                    SignDef def = defs[rng.Next(defs.Count)];
                    string spec = null;
                    if (def.Template != null)
                    {
                        GuideSpec s = def.Template.Default();
                        foreach (GuideField field in def.Template.Fields)
                        {
                            if (field.Kind == GuideFieldKind.Text && rng.Next(2) == 0)
                                s = s.With(field.Key, field.Sanitize(rng.Next(1000, 99999).ToString()));
                        }

                        spec = s.ToString();
                    }

                    uint support = rng.Next(5) == 0 ? SupportCatalog.ById(SupportCatalog.Cantilever).Hash : 0u;
                    items.Add(new ItemRecord(def.Hash, new Vec3(t * 192f + (float)rng.NextDouble() * 150f, 0f, (float)rng.NextDouble() * 150f), (float)(rng.NextDouble() * 6.28), support, spec));
                }

                tiles.Add(items);
            }

            var tiers = new[] { TileTier.Near, TileTier.Mid, TileTier.Far };
            var expected = new string[tiles.Count * tiers.Length];
            for (int t = 0; t < tiles.Count; t++)
            {
                for (int k = 0; k < tiers.Length; k++)
                    expected[t * tiers.Length + k] = Fingerprint(ChunkBuilder.BuildDetailed(tiles[t], new Vec3(t * 192f + 96f, 0f, 96f), ChunkDetail.Of(tiers[k]), out _, 30000));
            }

            // The same on four threads, each taking jobs in a different order, with the caches cleared by the number of different boards.
            int wrong = 0, errors = 0, next = -1;
            var threads = new List<Thread>();
            for (int w = 0; w < 4; w++)
            {
                int worker = w;
                var thread = new Thread(() =>
                {
                    var order = new List<int>();
                    for (int i = 0; i < expected.Length; i++)
                        order.Add(i);
                    var shuffle = new Random(worker + 1);
                    for (int i = order.Count - 1; i > 0; i--)
                    {
                        int j = shuffle.Next(i + 1);
                        int tmp = order[i];
                        order[i] = order[j];
                        order[j] = tmp;
                    }

                    foreach (int job in order)
                    {
                        try
                        {
                            int t = job / tiers.Length, k = job % tiers.Length;
                            string got = Fingerprint(ChunkBuilder.BuildDetailed(tiles[t], new Vec3(t * 192f + 96f, 0f, 96f), ChunkDetail.Of(tiers[k]), out _, 30000));
                            if (got != expected[job])
                            {
                                if (Interlocked.Increment(ref wrong) <= 2)
                                {
                                    string[] a = expected[job].Split('|'), b = got.Split('|');
                                    var only = new List<string>();
                                    foreach (string x in a) if (Array.IndexOf(b, x) < 0) only.Add("expected " + x);
                                    foreach (string x in b) if (Array.IndexOf(a, x) < 0) only.Add("got " + x);
                                    Console.WriteLine("  tile " + t + " tier " + tiers[k] + " differs: " + string.Join("; ", only.GetRange(0, Math.Min(6, only.Count))));
                                }
                            }
                        }
                        catch (Exception)
                        {
                            Interlocked.Increment(ref errors);
                        }
                    }

                    Interlocked.Increment(ref next);
                });
                threads.Add(thread);
                thread.Start();
            }

            foreach (Thread thread in threads)
                thread.Join();
            Program.Check(errors == 0, errors + " build(s) threw on a worker thread");
            Program.Check(wrong == 0, wrong + " tile(s) built on a worker thread differ from the same tile built alone");
            Program.Check(next == 3, "all four threads finished");
        }
    }
}
