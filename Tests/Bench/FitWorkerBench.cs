using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using CDLC.TS.Tests;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What moving the cutting of the roads' surface to a worker costs and saves the main thread (BENCH_FITWORKER=1): the pure part only. The game's reading of the roads (the search tree, the compositions), the
    /// finish that needs the terrain and the ECS, and Mono are not here. Shapes: the owner's log of 0.12.73 has area fits of 406 pieces from 66 bands (88 ms) and 278 from 34 bands (100 ms: the same
    /// kind of work with a heavier area); SurfaceFixture's bands (16 samples a band, a junction of crossing roads) give a similar cell count. Bytes are GC.GetAllocatedBytesForCurrentThread.
    /// </summary>
    internal static class FitWorkerBench
    {
        public static void Run()
        {
            Console.WriteLine("Cutting of the roads' surface, main thread against worker (ms, least of 5 runs of 8):");
            Warm();
            var shapes = new List<(string Name, FitJob Job)>();
            foreach (int bands in new[] { 66, 34 })
            {
                List<SurfaceStrip> strips = SurfaceFixture.Junction(bands, bands);
                foreach (string id in new[] { MarkingTemplates.ChannelId, MarkingTemplates.GridId })
                    shapes.Add(($"area {id} {bands} bands", AreaJob(id, strips)));
                shapes.Add(($"heights window {bands} bands", new FitJob { Heights = true, Strips = strips, Origin = new Vec3(0f, 0.1f, 0f), MinX = -31f, MaxX = 31f, MinZ = -4.5f, MaxZ = 4.5f }));
            }

            Console.WriteLine("  what the main thread did for each item before (all of this runs on the worker now):");
            foreach ((string name, FitJob job) in shapes)
            {
                FitResult result = default;
                (double ms, double bytes) = Measure(() => result = FitCut.Run(job));
                Console.WriteLine($"    {name,-34} {ms,8:0.00} ms  {bytes / 1024.0,8:0.0} KB   ({result.Fit.PieceCount} {(job.Heights ? "cells" : "pieces")}, cut {result.CutMs:0.00} ms, judgement {result.RuleMs:0.00} ms, {job.Cells} cells of bands)");
            }

            Console.WriteLine("  what the main thread does per item now (giving the job and taking its result; the ECS reads of both are not here):");
            foreach ((string name, FitJob job) in shapes)
            {
                // One thread of the worker is busy with long jobs while the main thread gives and takes: the lock is contended as in the game.
                var worker = new FitWorker(2);
                var ledger = new FitLedger<int>();
                const int items = 40;
                var begin = new double[items];
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < items; i++)
                {
                    FitLedger<int>.Request request = ledger.Begin(i, "k", 0f, 0f, 1f, 1f);
                    ledger.Add(request, worker, Clone(job));
                    ledger.Seal(request);
                }

                double give = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / items;
                double giveBytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)items;
                int taken = 0;
                double takeMs = 0.0;
                long takeBytes = 0;
                var clock = Stopwatch.StartNew();
                while (taken < items && clock.ElapsedMilliseconds < 60000)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    while (worker.TryTake(out FitDone done))
                        ledger.Accept(done);
                    while (ledger.TryTakeComplete(out _))
                        taken++;
                    long spent = Stopwatch.GetTimestamp() - t0;
                    if (spent > 0)
                    {
                        takeMs += spent * 1000.0 / Stopwatch.Frequency;
                        takeBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
                    }

                    Thread.Sleep(1);
                }

                Console.WriteLine($"    {name,-34} give {give * 1000.0,7:0.0} us {giveBytes,6:0} B   take {takeMs / items * 1000.0,7:0.0} us {takeBytes / (double)items,6:0} B   ({taken} of {items} back)");
            }

            Console.WriteLine("  the heights of a laid marking: SurfaceHeights.Put on the main thread, reading and cutting each window, or with the cuts made on the worker:");
            List<SurfaceStrip> big = SurfaceFixture.Junction(66, 9);
            Func<float, float, float, float> ground = (x, y, z) => (float)Math.Sin(x * 0.1) * 0.5f + y * 0.2f;
            foreach (int count in new[] { 33, 90 })
            {
                List<Vec3> line = Line(count);
                FitJob JobOf(float x0, float z0, float x1, float z1, float y) => new FitJob { Heights = true, Strips = big, Origin = new Vec3(0f, y, 0f), MinX = x0, MaxX = x1, MinZ = z0, MaxZ = z1 };
                (double syncMs, double syncBytes) = Measure(() => new SurfaceHeights().Put(line, "k", count, (x0, z0, x1, z1, y) => FitCut.Run(JobOf(x0, z0, x1, z1, y)).Fit, ground, true), 5, 3);
                var windows = new List<SurfaceHeights.WindowRead>();
                new SurfaceHeights().Windows(line, "k", count, true, windows);
                var fits = new Dictionary<SurfaceHeights.WindowRead, AreaFit>();
                foreach (SurfaceHeights.WindowRead w in windows)
                    fits[w] = FitCut.Run(JobOf(w.MinX, w.MinZ, w.MaxX, w.MaxZ, w.OriginY)).Fit;
                (double lookMs, double lookBytes) = Measure(() => new SurfaceHeights().Put(line, "k", count, (x0, z0, x1, z1, y) => fits[new SurfaceHeights.WindowRead(x0, z0, x1, z1, y)], ground, true), 5, 40);
                (double listMs, _) = Measure(() => new SurfaceHeights().Windows(line, "k", count, true, new List<SurfaceHeights.WindowRead>()), 5, 200);
                Console.WriteLine($"    {count} points, {windows.Count} window(s): reading and cutting {syncMs,7:0.00} ms {syncBytes / 1024.0,7:0.0} KB;  with the cuts made {lookMs,7:0.000} ms {lookBytes / 1024.0,6:0.0} KB;  listing the windows {listMs * 1000.0,5:0.0} us");
            }

            {
                SignDef def = SignCatalog.ById("bar.fret");
                var points = new List<Vec3>();
                for (float z = 0f; z <= 70f; z += 2f)
                    points.Add(new Vec3(0f, 0.01f * z, z));
                string spec = MarkingTemplates.WithRun(def.Template, def.Template.Default().ToString(), RunPath.Encode(points, out _), 0f);
                (double finding, double findBytes) = Measure(() => MarkingGround.Lay(def, spec, new Vec3(100f, 10f, 200f), 0.4f, world => world), 5, 40);
                Console.WriteLine($"  finding the line of a barrier of {points.Count} points (Lay with a callback that takes it; the main thread does this once more for each marking now): {finding * 1000.0,6:0} us {findBytes / 1024.0,6:0.0} KB");
            }

            Console.WriteLine("  a queue of 120 area cuts (66 bands, channel), by threads: wall time, the worker's own time, how long a job waited:");
            FitJob area = AreaJob(MarkingTemplates.ChannelId, SurfaceFixture.Junction(66, 66));
            Measure(() => FitCut.Run(area), 1, 3);
            foreach (int threads in new[] { 1, 2, 4 })
            {
                var worker = new FitWorker(threads);
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 120; i++)
                    worker.Submit(Clone(area));
                var waits = new List<double>();
                int got = 0;
                long mainTicks = 0;
                while (got < 120 && clock.ElapsedMilliseconds < 120000)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    while (worker.TryTake(out FitDone done))
                    {
                        got++;
                        waits.Add(done.WaitedMs);
                    }

                    mainTicks += Stopwatch.GetTimestamp() - t0;
                    Thread.Sleep(1);
                }

                double wall = clock.Elapsed.TotalMilliseconds;
                waits.Sort();
                (_, _, _, double workerMs, double worst, _, _) = worker.Counters;
                Console.WriteLine($"    {threads} thread(s): {wall,8:0} ms wall for {got}, worker {workerMs,8:0} ms (longest {worst:0.0}), a job waited {waits[waits.Count / 2],7:0} ms (median) {waits[waits.Count * 95 / 100],7:0} ms (95th), main thread polled {mainTicks * 1000.0 / Stopwatch.Frequency,6:0.00} ms");
            }
        }

        /// <summary>The first use of the worker and the ledger compiles them and starts a thread: not what the main thread pays in the game after the first frame.</summary>
        private static void Warm()
        {
            var worker = new FitWorker(2);
            var ledger = new FitLedger<int>();
            FitJob small = AreaJob(MarkingTemplates.ChannelId, SurfaceFixture.Junction(6, 1));
            for (int i = 0; i < 6; i++)
            {
                FitLedger<int>.Request request = ledger.Begin(i, "k", 0f, 0f, 1f, 1f);
                ledger.Add(request, worker, Clone(small));
            }

            var clock = Stopwatch.StartNew();
            int taken = 0;
            while (taken < 6 && clock.ElapsedMilliseconds < 20000)
            {
                while (worker.TryTake(out FitDone done))
                    ledger.Accept(done);
                while (ledger.TryTakeComplete(out _))
                    taken++;
                Thread.Sleep(1);
            }
        }

        private static List<Vec3> Line(int count)
        {
            var line = new List<Vec3>();
            for (int i = 0; i < count; i++)
                line.Add(new Vec3(-28f + i * 0.6f, 0.2f * (float)Math.Sin(i * 0.2), 0.5f * (float)Math.Sin(i * 0.1)));
            return line;
        }

        private static FitJob AreaJob(string id, List<SurfaceStrip> strips)
        {
            SignDef def = SignCatalog.ById(id);
            string spec = def.Template.Default().ToString();
            MarkingPlan plan = MarkingTemplates.PlanOf(def, spec);
            return new FitJob { Strips = strips, Origin = new Vec3(1f, 0.1f, -2f), Yaw = 0.3f, MinX = plan.MinX, MaxX = plan.MaxX, MinZ = plan.MinZ, MaxZ = plan.MaxZ, Rule = new AreaRule { Plan = plan, Spec = spec, Def = def } };
        }

        private static FitJob Clone(FitJob job) => new FitJob { Heights = job.Heights, Strips = job.Strips, Origin = job.Origin, Yaw = job.Yaw, MinX = job.MinX, MaxX = job.MaxX, MinZ = job.MinZ, MaxZ = job.MaxZ, Margin = job.Margin, Rule = job.Rule };

        /// <summary>The least time of runs of the action (ms), and the bytes it allocated on this thread.</summary>
        private static (double Ms, double Bytes) Measure(Action action, int runs = 5, int repeats = 8)
        {
            action();
            double best = double.MaxValue, bytes = 0.0;
            for (int run = 0; run < runs; run++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < repeats; i++)
                    action();
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / repeats;
                if (ms < best)
                {
                    best = ms;
                    bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)repeats;
                }
            }

            return (best, bytes);
        }
    }
}
