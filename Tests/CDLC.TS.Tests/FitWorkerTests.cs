using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The cuts of the roads' surface made on a worker (0.12.74, Geo/FitCut.cs, FitWorker.cs, FitLedger.cs, SurfaceHeights.Windows): the worker's cut is the main thread's, to the bit, on any thread; a request
    /// that a road or the item has outdated is not used; the reads a Put would make are the ones made beforehand. The ECS half (reading the roads, taking the cuts, drawing) is not here: nothing offline
    /// reads the game's roads.
    /// </summary>
    internal static class FitWorkerTests
    {
        public static void RunAll()
        {
            Program.Run("fit worker: SameAs tells fits apart to the bit", SameAs);
            Program.Run("fit worker: FitCut is the fit's own cut, and the worker's is the main thread's, to the bit, on 1, 2 and 4 threads", WorkerEqualsMain);
            Program.Run("fit worker: 8 threads started together on strips they share give the single thread's fits", EightThreads);
            Program.Run("fit worker: the version of a fit is different on every thread", Versions);
            Program.Run("fit worker: the windows a Put would read are the reads it makes", WindowsAreReads);
            Program.Run("fit worker: a Put with its fits made beforehand gives the heights of a Put that reads", PrefetchedPut);
            Program.Run("fit worker: a marking asks for the heights of one line, the same line however its answer comes", LayAsksOnce);
            Program.Run("fit worker: the ledger gives a request when all its cuts are back, and says when a road or the item outdated it", Ledger);
            Program.Run("fit worker: Clear forgets what waits, a cut that throws is told and the next is made", ClearAndErrors);
            Program.Run("fit worker: the shadow's comparison finds a cut that differs", ShadowCompare);
            Program.Run("fit worker: nothing the cut touches is static state that threads share", NoStatics);
        }

        // ------------------------------------------------------------------ fixtures

        private static FitJob RandomJob(Random random, int bands, bool heights, List<SurfaceStrip> strips = null)
        {
            float R(double from, double to) => (float)(from + random.NextDouble() * (to - from));
            return new FitJob
            {
                Heights = heights,
                Strips = strips ?? SurfaceFixture.Junction(bands, random.Next()),
                Origin = new Vec3(R(-5, 5), R(-1, 1), R(-5, 5)),
                Yaw = R(0, 6.28),
                MinX = -R(5, 30),
                MaxX = R(5, 30),
                MinZ = -R(2, 20),
                MaxZ = R(2, 20),
                Margin = random.Next(2) == 0 ? 1f : 0.5f
            };
        }

        private static AreaFit Direct(FitJob job) => job.Heights
            ? AreaFit.ForHeights(job.Strips, job.Origin, job.Yaw, job.MinX, job.MaxX, job.MinZ, job.MaxZ, job.Margin)
            : AreaFit.FromStrips(job.Strips, job.Origin, job.Yaw, job.MinX, job.MaxX, job.MinZ, job.MaxZ, job.Margin);

        private static FitJob AreaJobOf(string id, List<SurfaceStrip> strips, float yaw)
        {
            SignDef def = SignCatalog.ById(id);
            string spec = def.Template.Default().ToString();
            MarkingPlan plan = MarkingTemplates.PlanOf(def, spec);
            return new FitJob
            {
                Strips = strips,
                Origin = new Vec3(1f, 0.1f, -2f),
                Yaw = yaw,
                MinX = plan.MinX,
                MaxX = plan.MaxX,
                MinZ = plan.MinZ,
                MaxZ = plan.MaxZ,
                Rule = new AreaRule { Plan = plan, Spec = spec, Def = def }
            };
        }

        private static List<FitDone> TakeAll(FitWorker worker, int count, int timeoutMs = 30000)
        {
            var taken = new List<FitDone>();
            var clock = Stopwatch.StartNew();
            while (taken.Count < count && clock.ElapsedMilliseconds < timeoutMs)
            {
                if (worker.TryTake(out FitDone done))
                    taken.Add(done);
                else
                    Thread.Sleep(1);
            }

            return taken;
        }

        private static bool SameResult(FitResult a, FitResult b) => FitCut.Differ(a, b) == null;

        // ------------------------------------------------------------------ the tests

        private static void SameAs()
        {
            var random = new Random(3);
            FitJob job = RandomJob(random, 34, false);
            AreaFit a = Direct(job), b = Direct(job);
            Program.Check(a.Version != b.Version, "two fits are two versions");
            Program.Check(a.SameAs(b, out string why) && why == null, "one job cut twice is the same to the bit: " + why);
            Program.Check(a.PieceCount > 50, "a cut that has pieces to compare: " + a.PieceCount);

            FitJob moved = new FitJob { Strips = job.Strips, Origin = new Vec3(job.Origin.X + 1e-3f, job.Origin.Y, job.Origin.Z), Yaw = job.Yaw, MinX = job.MinX, MaxX = job.MaxX, MinZ = job.MinZ, MaxZ = job.MaxZ, Margin = job.Margin };
            Program.Check(!a.SameAs(Direct(moved), out why) && why != null, "a millimetre of origin is a difference: " + why);
            FitJob heights = new FitJob { Heights = true, Strips = job.Strips, Origin = job.Origin, Yaw = job.Yaw, MinX = job.MinX, MaxX = job.MaxX, MinZ = job.MinZ, MaxZ = job.MaxZ, Margin = job.Margin };
            Program.Check(!a.SameAs(Direct(heights), out why) && why != null, "cells for heights are another kind of fit: " + why);
            AreaFit noted = Direct(job);
            noted.Note += "!";
            Program.Check(!a.SameAs(noted, out why) && why.Contains("note"), "a note is compared: " + why);
            Program.Check(!a.SameAs(null, out why) && why == "no fit", "no fit is not a fit");
            // The strips are the same but one is of an elevated road: the pieces are where they were, and the fit is another.
            var raised = new List<SurfaceStrip>();
            foreach (SurfaceStrip strip in job.Strips)
            {
                var copy = new SurfaceStrip { Elevated = !strip.Elevated, Filler = strip.Filler, Source = strip.Source };
                for (int i = 0; i < strip.Left.Count; i++)
                    copy.Add(strip.Left[i], strip.Right[i]);
                raised.Add(copy);
            }

            FitJob elevated = new FitJob { Strips = raised, Origin = job.Origin, Yaw = job.Yaw, MinX = job.MinX, MaxX = job.MaxX, MinZ = job.MinZ, MaxZ = job.MaxZ, Margin = job.Margin };
            AreaFit raisedFit = Direct(elevated);
            Program.Check(raisedFit.PieceCount == a.PieceCount && !a.SameAs(raisedFit, out why) && why.Contains("plane or elevation"), "only which pieces are of an elevated road differs: " + why);
            FitJob narrow = new FitJob { Strips = job.Strips, Origin = job.Origin, Yaw = job.Yaw, MinX = job.MinX, MaxX = job.MinX + 3f, MinZ = job.MinZ, MaxZ = job.MinZ + 3f, Margin = job.Margin };
            Program.Check(!a.SameAs(Direct(narrow), out why), "another box is another fit: " + why);
        }

        private static void WorkerEqualsMain()
        {
            var random = new Random(11);
            var jobs = new List<FitJob>();
            var reference = new Dictionary<FitJob, FitResult>();
            int[] shapes = { 3, 6, 12, 34, 66 };
            for (int i = 0; i < 90; i++)
                jobs.Add(RandomJob(random, shapes[i % shapes.Length], i % 3 != 0));
            // The two shapes of the owner's log (66 bands: 406 pieces; 34 bands: 278) cut for paint, with the area's own judgement, and for heights at the box of a window of 30 points.
            List<SurfaceStrip> big = SurfaceFixture.Junction(66, 7), mid = SurfaceFixture.Junction(34, 5);
            foreach (string id in new[] { MarkingTemplates.ChannelId, MarkingTemplates.GridId, MarkingTemplates.SimpleGridId, MarkingTemplates.GoreId })
            {
                jobs.Add(AreaJobOf(id, big, 0f));
                jobs.Add(AreaJobOf(id, mid, 0.6f));
            }

            jobs.Add(new FitJob { Heights = true, Strips = big, Origin = new Vec3(0f, 0.2f, 0f), MinX = -31f, MaxX = 31f, MinZ = -4.5f, MaxZ = 4.5f });
            jobs.Add(new FitJob { Heights = true, Strips = mid, Origin = new Vec3(0f, -0.3f, 0f), MinX = -31f, MaxX = 31f, MinZ = -4.5f, MaxZ = 4.5f });

            int rules = 0, onRoad = 0;
            foreach (FitJob job in jobs)
            {
                FitResult own = FitCut.Run(job);
                AreaFit direct = Direct(job);
                if (job.Rule == null)
                {
                    bool same = own.Fit.SameAs(direct, out string why);
                    Program.Check(same, "FitCut adds nothing to the fit's own cut: " + why);
                }
                else
                {
                    rules++;
                    bool on = AreaFitRules.OnRoad(direct, job.Rule.Plan, job.Rule.Spec, job.Rule.Def, out float cover);
                    if (on)
                    {
                        onRoad++;
                        direct.Note = $"{direct.Note}; {cover * 100f:0} % of the area is on the road";
                    }

                    string differs = FitCut.Differ(new FitResult { Fit = direct, OnRoad = on, Cover = cover }, own);
                    Program.Check(differs == null, "FitCut is the judgement Compute made: " + differs);
                }

                reference[job] = own;
            }

            Console.WriteLine($"  {jobs.Count} jobs, {rules} with the area's own judgement ({onRoad} on the road); 66 bands cut to {reference[jobs[jobs.Count - 2]].Fit.PieceCount} cells, {reference[jobs[jobs.Count - 4]].Fit.PieceCount} pieces for paint");

            foreach (int threads in new[] { 1, 2, 4 })
            {
                var worker = new FitWorker(threads);
                foreach (FitJob job in jobs)
                    worker.Submit(job);
                List<FitDone> done = TakeAll(worker, jobs.Count);
                int different = 0, errors = 0;
                bool inOrder = true;
                for (int i = 1; i < done.Count; i++)
                    inOrder &= done[i].Job.Id > done[i - 1].Job.Id;
                if (threads == 1)
                    Program.Check(inOrder, "one thread makes the jobs in the order they were given");
                foreach (FitDone d in done)
                {
                    if (d.Error != null)
                        errors++;
                    else if (!SameResult(reference[d.Job], d.Result))
                        different++;
                }

                (long submitted, long taken, long failed, double workerMs, _, int most, _) = worker.Counters;
                Program.Check(done.Count == jobs.Count && errors == 0 && different == 0, $"{threads} thread(s): {done.Count} of {jobs.Count} back, {errors} threw, {different} differ");
                Program.Check(submitted == jobs.Count && taken == jobs.Count && failed == 0 && workerMs > 0.0 && most >= 1 && worker.InFlight == 0, $"{threads} thread(s): the counters say {submitted} given, {taken} taken, {most} most in flight, {worker.InFlight} left");
            }
        }

        private static void EightThreads()
        {
            List<SurfaceStrip> big = SurfaceFixture.Junction(66, 7);
            var jobs = new List<FitJob> { AreaJobOf(MarkingTemplates.ChannelId, big, 0f), AreaJobOf(MarkingTemplates.GridId, big, 0.9f), AreaJobOf(MarkingTemplates.GoreId, big, -0.4f) };
            var random = new Random(5);
            jobs.Add(RandomJob(random, 66, true, big));
            jobs.Add(RandomJob(random, 66, false, big));
            var reference = new FitResult[jobs.Count];
            for (int i = 0; i < jobs.Count; i++)
                reference[i] = FitCut.Run(jobs[i]);

            int failures = 0;
            for (int round = 0; round < 3; round++)
            {
                var results = new FitResult[8 * jobs.Count];
                var errors = new Exception[8];
                using (var start = new ManualResetEventSlim(false))
                {
                    var threads = new Thread[8];
                    for (int t = 0; t < 8; t++)
                    {
                        int n = t;
                        threads[t] = new Thread(() =>
                        {
                            try
                            {
                                start.Wait();
                                for (int k = 0; k < jobs.Count; k++)
                                    results[n * jobs.Count + k] = FitCut.Run(jobs[(k + n) % jobs.Count]);
                            }
                            catch (Exception ex)
                            {
                                errors[n] = ex;
                            }
                        });
                        threads[t].Start();
                    }

                    start.Set();
                    foreach (Thread thread in threads)
                        thread.Join();
                }

                for (int t = 0; t < 8; t++)
                {
                    if (errors[t] != null)
                        failures++;
                    for (int k = 0; k < jobs.Count; k++)
                    {
                        if (!SameResult(reference[(k + t) % jobs.Count], results[t * jobs.Count + k]))
                            failures++;
                    }
                }
            }

            Program.Check(failures == 0, $"8 threads on the strips they share, 3 rounds of {jobs.Count} cuts each: {failures} differ or threw");
        }

        private static void Versions()
        {
            const int threadCount = 8, each = 4000;
            var versions = new int[threadCount * each];
            using (var start = new ManualResetEventSlim(false))
            {
                var threads = new Thread[threadCount];
                for (int t = 0; t < threadCount; t++)
                {
                    int n = t;
                    threads[t] = new Thread(() =>
                    {
                        start.Wait();
                        for (int i = 0; i < each; i++)
                            versions[n * each + i] = AreaFit.FromGround((x, z) => 0f, 0f, 1f, 0f, 1f).Version;
                    });
                    threads[t].Start();
                }

                start.Set();
                foreach (Thread thread in threads)
                    thread.Join();
            }

            var distinct = new HashSet<int>(versions);
            Program.Check(distinct.Count == versions.Length, $"{versions.Length} fits made at once on {threadCount} threads have {distinct.Count} versions");
        }

        private static List<Vec3> RandomLine(Random random, int count)
        {
            float x = (float)(random.NextDouble() * 20 - 10), z = (float)(random.NextDouble() * 20 - 10), heading = (float)(random.NextDouble() * 6.28);
            var line = new List<Vec3>(count);
            for (int i = 0; i < count; i++)
            {
                line.Add(new Vec3(x, 0.3f * (float)Math.Sin(i * 0.2), z));
                heading += (float)(random.NextDouble() - 0.5) * 0.2f;
                x += 2f * (float)Math.Cos(heading);
                z += 2f * (float)Math.Sin(heading);
            }

            return line;
        }

        private static void WindowsAreReads()
        {
            var random = new Random(21);
            int lines = 0, reads = 0;
            foreach (int count in new[] { 1, 2, 5, 29, 30, 31, 60, 61, 100 })
            {
                foreach (bool bridge in new[] { true, false })
                {
                    foreach (int pinned in new[] { count, count / 2 })
                    {
                        List<Vec3> line = RandomLine(random, count);
                        var heights = new SurfaceHeights();
                        var wanted = new List<SurfaceHeights.WindowRead>();
                        heights.Windows(line, "k", pinned, bridge, wanted);
                        var made = new List<SurfaceHeights.WindowRead>();
                        heights.Put(line, "k", pinned, (x0, z0, x1, z1, y) =>
                        {
                            made.Add(new SurfaceHeights.WindowRead(x0, z0, x1, z1, y));
                            return null;
                        }, (x, y, z) => float.NaN, bridge);
                        bool same = wanted.Count == made.Count;
                        for (int i = 0; same && i < made.Count; i++)
                            same = wanted[i].Equals(made[i]);
                        Program.Check(same, $"{count} points, pinned {pinned}, bridge {bridge}: {wanted.Count} windows listed, {made.Count} read");
                        lines++;
                        reads += made.Count;
                        // What is kept is not read again: the settled windows are in the cache after the Put, and are not listed.
                        var again = new List<SurfaceHeights.WindowRead>();
                        heights.Windows(line, "k", pinned, bridge, again);
                        int unsettled = 0;
                        for (int from = 0; from < count; from += SurfaceHeights.Window)
                        {
                            if (Math.Min(count, from + SurfaceHeights.Window) > pinned)
                                unsettled++;
                        }

                        Program.Check(again.Count == unsettled, $"{count} points, pinned {pinned}: {again.Count} windows listed after a Put, {unsettled} are not settled");
                    }
                }
            }

            Console.WriteLine($"  {lines} lines, {reads} reads listed before they were made");
        }

        private static void PrefetchedPut()
        {
            var random = new Random(33);
            List<SurfaceStrip> strips = SurfaceFixture.Junction(66, 9);
            Func<float, float, float, float> ground = (x, y, z) => (x * x + z * z) % 7f < 1f ? float.NaN : (float)Math.Sin(x * 0.1) * 0.5f + y * 0.2f;
            FitJob JobOf(float x0, float z0, float x1, float z1, float y) => new FitJob { Heights = true, Strips = strips, Origin = new Vec3(0f, y, 0f), MinX = x0, MaxX = x1, MinZ = z0, MaxZ = z1 };
            int differ = 0, lines = 0;
            foreach (int count in new[] { 7, 33, 90 })
            {
                foreach (bool bridge in new[] { true, false })
                {
                    List<Vec3> line = RandomLine(random, count);
                    var reading = new SurfaceHeights();
                    List<Vec3> expected = reading.Put(line, "k", count, (x0, z0, x1, z1, y) => FitCut.Run(JobOf(x0, z0, x1, z1, y)).Fit, ground, bridge);

                    var beforehand = new SurfaceHeights();
                    var windows = new List<SurfaceHeights.WindowRead>();
                    beforehand.Windows(line, "k", count, bridge, windows);
                    var worker = new FitWorker(2);
                    var jobs = new Dictionary<long, SurfaceHeights.WindowRead>();
                    foreach (SurfaceHeights.WindowRead w in windows)
                        jobs[worker.Submit(JobOf(w.MinX, w.MinZ, w.MaxX, w.MaxZ, w.OriginY))] = w;
                    var fits = new Dictionary<SurfaceHeights.WindowRead, AreaFit>();
                    foreach (FitDone d in TakeAll(worker, windows.Count))
                        fits[jobs[d.Job.Id]] = d.Result.Fit;
                    int missed = 0;
                    List<Vec3> actual = beforehand.Put(line, "k", count, (x0, z0, x1, z1, y) =>
                    {
                        if (fits.TryGetValue(new SurfaceHeights.WindowRead(x0, z0, x1, z1, y), out AreaFit fit))
                            return fit;
                        missed++;
                        return null;
                    }, ground, bridge);

                    bool same = actual.Count == expected.Count && missed == 0;
                    for (int i = 0; same && i < expected.Count; i++)
                        same = BitConverter.DoubleToInt64Bits(actual[i].X) == BitConverter.DoubleToInt64Bits(expected[i].X) && BitConverter.DoubleToInt64Bits(actual[i].Y) == BitConverter.DoubleToInt64Bits(expected[i].Y)
                            && BitConverter.DoubleToInt64Bits(actual[i].Z) == BitConverter.DoubleToInt64Bits(expected[i].Z);
                    if (!same)
                        differ++;
                    lines++;
                }
            }

            Program.Check(differ == 0, $"{lines} lines: the heights with the fits made beforehand on the worker are the heights of the reads, to the bit ({differ} differ)");
        }

        private static void Ledger()
        {
            var worker = new FitWorker(2);
            var ledger = new FitLedger<int>();
            var random = new Random(8);
            List<SurfaceStrip> strips = SurfaceFixture.Junction(12, 4);
            FitJob Job() => RandomJob(random, 12, true, strips);
            SurfaceHeights.WindowRead W(int n) => new SurfaceHeights.WindowRead(n, 0f, n + 1f, 1f, 0f);

            void Pump(int waitFor = 10000)
            {
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < waitFor && worker.InFlight > 0)
                {
                    while (worker.TryTake(out FitDone done))
                        ledger.Accept(done);
                    Thread.Sleep(1);
                }
            }

            // Three cuts for one item: given when all three are back, with where each belongs.
            FitLedger<int>.Request first = ledger.Begin(1, "one", 0f, 0f, 10f, 10f);
            for (int n = 0; n < 3; n++)
                ledger.Add(first, worker, Job(), W(n));
            ledger.Seal(first);
            Program.Check(ledger.IsPending(1) && ledger.Count == 1 && !ledger.TryTakeComplete(out _), "pending until its cuts are back");
            Pump();
            Program.Check(ledger.TryTakeComplete(out FitLedger<int>.Request got) && ReferenceEquals(got, first) && got.Results.Count == 3 && got.Results.ContainsKey(W(2)), "all three are given together, each by its window");
            Program.Check(got.WorkerMs > 0.0 && got.UsableFor("one") && !got.UsableFor("two") && got.Failure == null, "usable for the item as it was and not for the item as it is now");
            Program.Check(!ledger.IsPending(1) && ledger.Count == 0 && ledger.Of(1) == null, "taken once");

            // One cut at a time: the request is not given while one is missing.
            FitLedger<int>.Request two = ledger.Begin(9, "two", 0f, 0f, 1f, 1f);
            ledger.Add(two, worker, Job(), W(0));
            ledger.Add(two, worker, Job(), W(1));
            ledger.Seal(two);
            List<FitDone> firstBack = TakeAll(worker, 1);
            Program.Check(firstBack.Count == 1 && ledger.Accept(firstBack[0]) && !ledger.TryTakeComplete(out _) && ledger.IsPending(9), "one of two cuts back: not given yet");
            List<FitDone> secondBack = TakeAll(worker, 1);
            Program.Check(secondBack.Count == 1 && ledger.Accept(secondBack[0]) && ledger.TryTakeComplete(out FitLedger<int>.Request both) && ReferenceEquals(both, two) && both.Results.Count == 2, "both back: given");

            // A road changes in the box after the read: stale, and not usable, whatever else is right; a road far away or beyond the margin is not.
            FitLedger<int>.Request near = ledger.Begin(2, "k", 0f, 0f, 10f, 10f), far = ledger.Begin(3, "k", 50f, 50f, 60f, 60f), edge = ledger.Begin(4, "k", 0f, 20f, 10f, 30f);
            foreach (FitLedger<int>.Request r in new[] { near, far, edge })
                ledger.Add(r, worker, Job());
            int marked = ledger.MarkStale(new List<(float, float, float, float)> { (11.5f, 0f, 20f, 10f), (200f, 200f, 210f, 210f) }, 2f);
            Program.Check(marked == 1 && near.Stale && !far.Stale && !edge.Stale, $"a road within the margin of a box marks only that request: {marked} marked");
            Pump();
            int taken = 0;
            while (ledger.TryTakeComplete(out FitLedger<int>.Request r))
            {
                taken++;
                Program.Check(r.UsableFor("k") == !r.Stale, "stale is not usable, the others are");
            }

            Program.Check(taken == 3, "all three come back, the stale one too: the caller decides: " + taken);

            // Forgotten and replaced requests are not given; their cuts are taken and dropped.
            FitLedger<int>.Request gone = ledger.Begin(5, "g", 0f, 0f, 1f, 1f);
            ledger.Add(gone, worker, Job());
            ledger.Forget(5);
            FitLedger<int>.Request old = ledger.Begin(6, "old", 0f, 0f, 1f, 1f);
            ledger.Add(old, worker, Job());
            FitLedger<int>.Request current = ledger.Begin(6, "new", 0f, 0f, 1f, 1f);
            ledger.Add(current, worker, Job());
            Pump();
            Program.Check(ledger.TryTakeComplete(out FitLedger<int>.Request only) && ReferenceEquals(only, current) && !ledger.TryTakeComplete(out _) && !ledger.IsPending(5), "a forgotten or replaced request is not given");

            // An item with nothing to cut is complete at once; a cut that is not this ledger's is not accepted.
            FitLedger<int>.Request empty = ledger.Begin(7, "e", 0f, 0f, 1f, 1f);
            ledger.Seal(empty);
            Program.Check(ledger.TryTakeComplete(out FitLedger<int>.Request none) && ReferenceEquals(none, empty) && none.Results.Count == 0, "nothing to cut: complete at once");
            var other = new FitWorker(1);
            other.Submit(Job());
            FitDone foreign = TakeAll(other, 1)[0];
            Program.Check(!ledger.Accept(foreign), "another worker's cut is not accepted");

            // A request read in slices (a marking's roads, a few windows a frame): its first cut comes back while it is still being read, and it is not complete then (the owner's 0.12.77 log: it was
            // taken with nothing yet to say what had been read, a NullReferenceException three times, and the cutting went back to the main thread for the session).
            FitLedger<int>.Request sliced = ledger.Begin(10, "slices", 0f, 0f, 10f, 10f, true);
            ledger.Add(sliced, worker, Job(), W(0));
            Pump();
            Program.Check(sliced.Waiting == 0 && ledger.IsPending(10) && !ledger.TryTakeComplete(out _), "its first cut is back and it is still being read: not complete");
            ledger.Add(sliced, worker, Job(), W(1));
            ledger.Seal(sliced);
            Program.Check(!ledger.TryTakeComplete(out _), "sealed with a cut out: not complete");
            Pump();
            Program.Check(ledger.TryTakeComplete(out FitLedger<int>.Request slicedGot) && ReferenceEquals(slicedGot, sliced) && slicedGot.Results.Count == 2 && !ledger.TryTakeComplete(out _), "sealed and both cuts back: given once, with both");
            FitLedger<int>.Request slicedFast = ledger.Begin(11, "fast", 0f, 0f, 1f, 1f, true);
            ledger.Add(slicedFast, worker, Job(), W(0));
            Pump();
            ledger.Seal(slicedFast);
            Program.Check(ledger.TryTakeComplete(out FitLedger<int>.Request fastGot) && ReferenceEquals(fastGot, slicedFast) && !ledger.TryTakeComplete(out _), "every cut back before the seal: the seal completes it, once");
            FitLedger<int>.Request slicedNone = ledger.Begin(12, "none", 0f, 0f, 1f, 1f, true);
            Program.Check(!ledger.TryTakeComplete(out _), "a request being read with nothing given yet is not complete");
            ledger.Seal(slicedNone);
            Program.Check(ledger.TryTakeComplete(out FitLedger<int>.Request noneGot) && ReferenceEquals(noneGot, slicedNone), "sealed with nothing to cut: complete");

            // Clear.
            FitLedger<int>.Request last = ledger.Begin(8, "l", 0f, 0f, 1f, 1f);
            ledger.Add(last, worker, Job());
            ledger.Clear();
            worker.Clear();
            Program.Check(ledger.Count == 0 && !ledger.IsPending(8) && !ledger.TryTakeComplete(out _), "cleared");
        }

        private static void LayAsksOnce()
        {
            // The main thread finds the line a marking is laid along by running Lay with a callback that only takes it, and the cuts are made for the windows of that line: the second run, with the real
            // heights, must ask for the very same points, once.
            var position = new Vec3(100f, 10.2f, 200f);
            int checkedLines = 0;
            foreach ((string id, string settings) in new[] { ("mk.line.lane", ""), ("mk.line.edge", ""), ("mk.kerb.nopark", "side=r;height=20"), ("mk.kerb.nopark", "side=l;height=20"), ("bar.fret", ""), ("mk.hump.small", null) })
            {
                SignDef def = SignCatalog.ById(id);
                GuideSpec spec = def.Template.Default();
                foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    spec = spec.With(pair.Substring(0, pair.IndexOf('=')), pair.Substring(pair.IndexOf('=') + 1));
                string text = spec.ToString();
                if (settings != null)
                {
                    var points = new List<Vec3>();
                    for (float z = 0f; z <= 70f; z += 2f)
                        points.Add(new Vec3(0f, 0.01f * z, z));
                    text = MarkingTemplates.WithRun(def.Template, text, RunPath.Encode(points, out _), 0f);
                }

                int calls = 0, calls2 = 0;
                List<Vec3> first = null, second = null;
                MarkingGround.Laid a = MarkingGround.Lay(def, text, position, 0.4f, world =>
                {
                    calls++;
                    first = new List<Vec3>(world);
                    return world;
                });
                MarkingGround.Laid b = MarkingGround.Lay(def, text, position, 0.4f, world =>
                {
                    calls2++;
                    second = new List<Vec3>(world);
                    var on = new List<Vec3>(world.Count);
                    foreach (Vec3 p in world)
                        on.Add(new Vec3(p.X, 9f + 0.3f * (float)Math.Sin(p.X + p.Z), p.Z));
                    return on;
                });
                bool same = first != null && second != null && first.Count == second.Count;
                for (int i = 0; same && i < first.Count; i++)
                    same = first[i].X == second[i].X && first[i].Y == second[i].Y && first[i].Z == second[i].Z;
                Program.Check(calls == 1 && calls2 == 1 && same && a != null && b != null, $"{id} {settings}: asked {calls}/{calls2} time(s), the same {first?.Count} points: {same}");
                checkedLines++;
            }

            Program.Check(checkedLines == 6, "every kind was tried");
        }

        private sealed class ThrowingStrips : IList<SurfaceStrip>
        {
            public int Count => 1;

            public bool IsReadOnly => true;

            public SurfaceStrip this[int index]
            {
                get => throw new InvalidOperationException("the strips could not be read");
                set => throw new NotSupportedException();
            }

            public IEnumerator<SurfaceStrip> GetEnumerator() => throw new InvalidOperationException("the strips could not be read");

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public void Add(SurfaceStrip item) => throw new NotSupportedException();

            public void Clear() => throw new NotSupportedException();

            public bool Contains(SurfaceStrip item) => false;

            public void CopyTo(SurfaceStrip[] array, int arrayIndex)
            {
            }

            public int IndexOf(SurfaceStrip item) => -1;

            public void Insert(int index, SurfaceStrip item) => throw new NotSupportedException();

            public bool Remove(SurfaceStrip item) => false;

            public void RemoveAt(int index) => throw new NotSupportedException();
        }

        private static void ClearAndErrors()
        {
            var random = new Random(17);
            var worker = new FitWorker(1);
            for (int i = 0; i < 40; i++)
                worker.Submit(RandomJob(random, 66, false));
            worker.Clear();
            Program.Check(worker.InFlight == 0 && !worker.TryTake(out _), "nothing is in flight after Clear");
            FitJob marker = RandomJob(random, 6, true);
            long id = worker.Submit(marker);
            List<FitDone> done = TakeAll(worker, 1);
            Thread.Sleep(300);
            Program.Check(done.Count == 1 && done[0].Job.Id == id && !worker.TryTake(out _) && worker.InFlight == 0, "only the job given after Clear comes back, and nothing of the earlier ones comes later");
            Program.Check(worker.Counters.Dropped <= 1, "and the jobs that waited are not made at all (only one that was already being made is dropped): " + worker.Counters.Dropped);

            var failing = new FitWorker(2);
            long bad = failing.Submit(new FitJob { Strips = new ThrowingStrips(), MinX = -1f, MaxX = 1f, MinZ = -1f, MaxZ = 1f });
            FitJob good = RandomJob(random, 6, true);
            failing.Submit(good);
            List<FitDone> both = TakeAll(failing, 2);
            FitDone failed = both.Find(d => d.Job.Id == bad), fine = both.Find(d => d.Job.Id != bad);
            Program.Check(both.Count == 2 && failed.Error != null && failed.Error.Message.Contains("could not be read") && fine.Error == null && fine.Result.Fit != null, "a cut that throws says so and the next is made");
            Program.Check(failing.Counters.Failed == 1 && failing.Counters.Taken == 2, "and the counters have it");

            var ledger = new FitLedger<int>();
            FitLedger<int>.Request request = ledger.Begin(1, "k", 0f, 0f, 1f, 1f);
            ledger.Add(request, failing, new FitJob { Strips = new ThrowingStrips(), MinX = -1f, MaxX = 1f, MinZ = -1f, MaxZ = 1f });
            var clock = Stopwatch.StartNew();
            while (!ledger.TryTakeComplete(out request) && clock.ElapsedMilliseconds < 10000)
            {
                while (failing.TryTake(out FitDone d))
                    ledger.Accept(d);
                Thread.Sleep(1);
            }

            Program.Check(request != null && request.Failure.HasValue && !request.UsableFor("k"), "a request with a failed cut is not usable");
        }

        private static void ShadowCompare()
        {
            List<SurfaceStrip> big = SurfaceFixture.Junction(66, 7);
            FitJob job = AreaJobOf(MarkingTemplates.ChannelId, big, 0.2f);
            FitResult sync = FitCut.Run(job);
            Program.Check(FitCut.Differ(sync, FitCut.Run(job)) == null, "the same job twice does not differ");

            var worker = new FitWorker(1);
            job.Reference = sync;
            worker.Submit(job);
            FitDone done = TakeAll(worker, 1)[0];
            Program.Check(done.Job.Reference.HasValue && FitCut.Differ(done.Job.Reference.Value, done.Result) == null, "a job given with its synchronous result comes back equal to it");

            FitJob moved = AreaJobOf(MarkingTemplates.ChannelId, big, 0.2f);
            moved.Origin = new Vec3(moved.Origin.X, moved.Origin.Y, moved.Origin.Z + 0.001f);
            Program.Check(FitCut.Differ(sync, FitCut.Run(moved)) != null, "a millimetre of the origin differs");
            FitResult flipped = sync;
            flipped.OnRoad = !flipped.OnRoad;
            Program.Check(FitCut.Differ(sync, flipped) != null && FitCut.Differ(sync, new FitResult { Fit = null }) != null, "the judgement and a missing fit differ");
            FitResult covered = sync;
            covered.Cover += 1e-6f;
            Program.Check(FitCut.Differ(sync, covered) != null, "a millionth of the cover differs");
        }

        private static void NoStatics()
        {
            // The strips are the cache's, shared by every job; the code that cuts them writes only to what it makes. The static fields of what it runs: constants, read-only numbers and text only, with the two
            // that are not: the fits' version counter (Interlocked, tested above) and the area kinds' lazily made set, which only IsAreaHash (not run by a cut) uses.
            var allowed = new HashSet<string> { "AreaFit.s_Versions", "AreaFitRules.s_AreaHashes" };
            var bad = new List<string>();
            foreach (Type type in new[] { typeof(FitCut), typeof(FitJob), typeof(FitResult), typeof(AreaRule), typeof(FitWorker), typeof(FitLedger<int>), typeof(AreaFit), typeof(AreaFitRules), typeof(SurfaceStrip), typeof(Polygon), typeof(MarkingPolygon), typeof(MarkingFills), typeof(SurfaceHeights) })
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    bool constant = field.IsLiteral;
                    bool readOnlyData = field.IsInitOnly && (field.FieldType.IsPrimitive || field.FieldType == typeof(string));
                    string name = type.Name.Split('`')[0] + "." + field.Name;
                    if (!constant && !readOnlyData && !allowed.Contains(name))
                        bad.Add(name);
                }
            }

            Program.Check(bad.Count == 0, "no static field that is not a constant or a read-only number or text: " + string.Join(", ", bad));
        }
    }
}
