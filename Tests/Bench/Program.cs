using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What the mod costs as the number of signs grows, measured on whatever runtime this is run with (the .NET Framework build is meant for Mono: Unity's MonoBleedingEdge is what the game runs on,
    /// and the game's slow-frame log is what the real figures are; this tells which parts scale and by how much). No Unity here: the pure parts only (records, tile diff, the meshes of the
    /// tiles, the road check's search). Usage: mono CDLC.TS.Bench.exe [edges] [items ...]
    /// </summary>
    internal static class Program
    {
        private static readonly Random Rng = new Random(20261019);

        private static int Main(string[] args)
        {
            Console.WriteLine($"runtime: {(Type.GetType("Mono.Runtime") != null ? "Mono " + Type.GetType("Mono.Runtime").GetMethod("GetDisplayName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, null) : ".NET " + Environment.Version)}, {IntPtr.Size * 8}-bit");
            int edges = args.Length > 0 ? int.Parse(args[0]) : 25000;
            var counts = new List<int>();
            for (int i = 1; i < args.Length; i++)
                counts.Add(int.Parse(args[i]));
            if (counts.Count == 0)
                counts.AddRange(new[] { 300, 3000, 10000, 30000 });

            if (Environment.GetEnvironmentVariable("BENCH_PARTS") == "1")
                Parts.Report();
            // BENCH_FONT: the font file of the boards and the road words (none: no road words, the boards in an installed family).
            SignFonts.Use(Environment.GetEnvironmentVariable("BENCH_FONT"), null);
            if (Environment.GetEnvironmentVariable("BENCH_ADAPTIVE") == "1")
            {
                Adaptive.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_SAMPLING") == "1")
            {
                Sampling.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_SHADOW") == "1")
            {
                ShadowWorkerBench.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_MARKINGS") == "1")
            {
                Markings.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_VMS") == "1")
            {
                Vms.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_AUTO") == "1")
            {
                Auto.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_PLACING") == "1")
            {
                Placing.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_EDITOR") == "1")
            {
                Editor.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_CUTSHAPE") == "1")
            {
                CutShape.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_PICTURECUT") == "1")
            {
                PictureCutBench.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_ITEMROUND") == "1")
            {
                ItemRound.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_SAVEROADS") == "1")
            {
                SaveRoads.Run(5000, 20000);
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_PLANNER") == "1")
            {
                Planner.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_TEXT") == "1")
            {
                Text.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_SURFACE") == "1")
            {
                Surface.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_EDIT") == "1")
            {
                Edit.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_FRAME") == "1")
            {
                Frame.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_HORIZON") == "1")
            {
                Horizon.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_MAINTHREAD") == "1")
            {
                MainThread.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_FITWORKER") == "1")
            {
                FitWorkerBench.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_FOLLOW") == "1")
            {
                Follow.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_FOLLOWSIM") == "1")
            {
                FollowSim.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_FOLLOWPERF") == "1")
            {
                FollowPerf.Run();
                return 0;
            }

            if (Environment.GetEnvironmentVariable("BENCH_DRAG") == "1")
            {
                Drag.Run();
                return 0;
            }

            EdgeSearch(edges);
            if (Environment.GetEnvironmentVariable("BENCH_OLD") == "1")
            {
                foreach (int n in counts)
                    Scale(n);
            }
            else
            {
                foreach (int n in counts)
                    Streaming.Run(n, 1500f);
            }
            return 0;
        }

        // ------------------------------------------------------------------ the road check

        private static Bezier3 Straight(float x0, float z0, float x1, float z1) =>
            new Bezier3(new Vec3(x0, 0f, z0), new Vec3(x0 + (x1 - x0) / 3f, 0f, z0 + (z1 - z0) / 3f), new Vec3(x0 + 2f * (x1 - x0) / 3f, 0f, z0 + 2f * (z1 - z0) / 3f), new Vec3(x1, 0f, z1));

        private static void EdgeSearch(int edges)
        {
            var curves = new List<Bezier3>();
            var boxes = new EdgeBoxes();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < edges; i++)
            {
                float x = (float)(Rng.NextDouble() * 14000 - 7000), z = (float)(Rng.NextDouble() * 14000 - 7000), length = (float)(Rng.NextDouble() * 200 + 20);
                Bezier3 c = Straight(x, z, x + length * (Rng.Next(2) == 0 ? 1 : 0.6f), z + length * (Rng.Next(2) == 0 ? 0 : 0.8f));
                curves.Add(c);
                boxes.Add(c.A.X, c.A.Z, c.B.X, c.B.Z, c.C.X, c.C.Z, c.D.X, c.D.Z);
            }

            double tableMs = clock.Elapsed.TotalMilliseconds;
            var scratch = new List<int>();
            Func<int, Bezier3> curveOf = i => curves[i];
            for (int warm = 0; warm < 50; warm++)
                EdgeHint.Nearest(boxes, curveOf, warm * 10f, 0f, 12f, scratch, out _, out _);

            clock.Restart();
            const int searches = 2000;
            for (int n = 0; n < searches; n++)
                EdgeHint.Nearest(boxes, curveOf, (float)(Rng.NextDouble() * 14000 - 7000), (float)(Rng.NextDouble() * 14000 - 7000), 12f, scratch, out _, out _);
            double perBoxes = clock.Elapsed.TotalMilliseconds / searches;

            // The old way, for one item: every edge measured.
            clock.Restart();
            var at = new Vec3(100f, 0f, 100f);
            float sink = 0f;
            const int brutes = 20;
            for (int b = 0; b < brutes; b++)
            {
                for (int e = 0; e < curves.Count; e++)
                    sink += curves[e].ClosestT(at, 8);
            }

            double perBrute = clock.Elapsed.TotalMilliseconds / brutes;
            Console.WriteLine($"road check, {edges} edges: table of boxes {tableMs:0.0} ms (with making the curves), a search by boxes {perBoxes * 1000:0.0} us, measuring every edge {perBrute:0.0} ms (sink {sink:0})");
        }

        // ------------------------------------------------------------------ what scales with the number of signs

        private static string RandomSpec(SignDef def)
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

        private static void Scale(int n)
        {
            GC.Collect();
            long heapBefore = GC.GetTotalMemory(true);
            var clock = Stopwatch.StartNew();
            var defs = new List<SignDef>(SignCatalog.All);
            var records = new Dictionary<int, ItemRecord>(n);

            // A city of about 10 km x 10 km: signs along roads, in groups of the sort the standard puts together (a few boards per support). Every kind of sign in the catalogue, with
            // guide boards of settings of their own (a third of their fields changed), so that the faces are mostly different from one another, which is the heavy case.
            for (int i = 0; i < n; i++)
            {
                SignDef def = defs[Rng.Next(defs.Count)];
                var position = new Vec3((float)(Rng.NextDouble() * 10000 - 5000), 0f, (float)(Rng.NextDouble() * 10000 - 5000));
                records[i] = new ItemRecord(def.Hash, position, (float)(Rng.NextDouble() * Math.PI * 2), 0u, RandomSpec(def));
            }

            double makeMs = clock.Elapsed.TotalMilliseconds;

            // Resync as 0.6.1 does it: a new dictionary of records, the diff against the old, the grouping by tile.
            var fresh = new Dictionary<int, ItemRecord>(records);
            clock.Restart();
            HashSet<TileKey> dirty = TileDiff.Dirty(new Dictionary<int, ItemRecord>(), fresh);
            Dictionary<TileKey, List<ItemRecord>> byTile = TileDiff.ByTile(fresh);
            double diffMs = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            HashSet<TileKey> dirtyAgain = TileDiff.Dirty(records, fresh);
            double diffSameMs = clock.Elapsed.TotalMilliseconds;

            // All the tiles at once (what 0.6.1 does at loading), and only the tiles within 900 m of a camera (what streaming would do).
            var cameraTiles = new List<TileKey>();
            foreach (TileKey key in byTile.Keys)
            {
                Vec3 centre = Tiles.Centre(key);
                if (Math.Sqrt(centre.X * centre.X + centre.Z * centre.Z) < 900.0 + Tiles.Size * 0.7)
                    cameraTiles.Add(key);
            }

            clock.Restart();
            long vertices = 0, metalV = 0, faceV = 0, ledV = 0, concreteV = 0;
            int parts = 0, itemsBuilt = 0, skipped = 0;
            double worst = 0.0;
            foreach (KeyValuePair<TileKey, List<ItemRecord>> pair in byTile)
            {
                var one = Stopwatch.StartNew();
                List<ChunkPart> built = ChunkBuilder.Build(pair.Value, Tiles.Centre(pair.Key), out int skip);
                worst = Math.Max(worst, one.Elapsed.TotalMilliseconds);
                skipped += skip;
                itemsBuilt += pair.Value.Count;
                foreach (ChunkPart part in built)
                {
                    vertices += part.Mesh.VertexCount;
                    if (part.Material == ChunkBuilder.MetalMaterial) metalV += part.Mesh.VertexCount;
                    else if (part.Material == ChunkBuilder.ConcreteMaterial) concreteV += part.Mesh.VertexCount;
                    else if (part.Material.StartsWith(ChunkBuilder.LedPrefix)) ledV += part.Mesh.VertexCount;
                    else faceV += part.Mesh.VertexCount;
                    parts++;
                }
            }

            double allMs = clock.Elapsed.TotalMilliseconds;
            int near = 0;
            foreach (TileKey key in cameraTiles)
                near += byTile[key].Count;

            // The meshes' size once uploaded: position + normal + uv, 32 bytes a vertex.
            long heapAfter = GC.GetTotalMemory(false);
            int faces = CountFaces(byTile);
            Console.WriteLine($"       vertices: metal {metalV:N0} ({metalV / (double)n:0} a sign), concrete {concreteV:N0}, faces {faceV:N0} ({faceV / (double)n:0} a sign), LED dots {ledV:N0} ({ledV / (double)n:0} a sign)");
            Console.WriteLine($"{n,6} signs: make {makeMs:0} ms | resync: diff {diffMs:0.0} ms (empty to full), {diffSameMs:0.0} ms (same again), {byTile.Count} tiles | build ALL tiles {allMs:0} ms (worst tile {worst:0} ms), " +
                              $"{vertices:N0} vertices = {vertices * 32 / 1048576} MB of meshes, {parts} parts, {faces} different faces ({faces * 1.7:0} MB of pictures at 1.7 MB), {skipped} skipped | " +
                              $"within 900 m of the middle: {cameraTiles.Count} tiles, {near} signs | managed heap {(heapAfter - heapBefore) / 1048576} MB more");
            GC.KeepAlive(dirty);
            GC.KeepAlive(dirtyAgain);
        }

        private static int CountFaces(Dictionary<TileKey, List<ItemRecord>> byTile)
        {
            var faces = new HashSet<string>();
            foreach (List<ItemRecord> list in byTile.Values)
            {
                foreach (ItemRecord item in list)
                {
                    SignDef def = SignCatalog.ByHash(item.TypeHash);
                    if (def != null)
                        faces.Add(SignInstances.Of(def, item.Spec).Key);
                }
            }

            return faces.Count;
        }
    }
}
