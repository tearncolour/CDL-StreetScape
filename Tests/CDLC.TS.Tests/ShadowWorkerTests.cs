using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The shadow of the adaptive sampler made on a worker (0.12.75) and the other parts of the read's cost that are pure: the capture of what a part is laid from, the runner that makes the comparison, the
    /// worker that takes them in order, the terrain gate and the sliced read. The game's reads (SurfaceReader, AreaFitSystem) are not here: they cannot be run offline.
    /// </summary>
    internal static class ShadowWorkerTests
    {
        public static void RunAll()
        {
            Program.Run("shadow: a job made by the capture gives the comparison the main thread made before", Equivalence);
            Program.Run("shadow: eight threads on one job and on many jobs give the same comparisons", Threads);
            Program.Run("shadow: the worker takes jobs in order, counts them, forgets them on Clear and keeps an error", Worker);
            Program.Run("shadow: a job holds nothing of the game and the pure parts it runs hold no state", Purity);
            Program.Run("shadow: the main thread's part (the capture) is small", CaptureCost);
            Program.Run("terrain gate: holds while the read back is pending, lets one through after the limit", Terrain);
            Program.Run("sliced read: at least one window a slice, in order, resumable, a failure left out", Sliced);
        }

        // ------------------------------------------------------------------ fixtures

        private sealed class Fixture
        {
            public ShadowJob Job;
            public PartComparison Reference;
            public bool Node;
            public string Source;
            public bool[] Fillers;
        }

        /// <summary>A part as SurfaceReader makes it: its fixed bands, the capture of what they were laid from, and the comparison made the way the main thread made it before the worker (the reference).</summary>
        private static Fixture Part(Random random, int i)
        {
            bool node = i % 3 == 2;
            var capture = new ShadowCapture();
            var fixedBands = new List<SurfaceStrip>();
            var probe = new SamplingProbe();
            List<SurfaceStrip> adaptive;
            if (!node)
            {
                Vec3[] c = SamplingTests.Centre(random, i % 2 == 0 ? 0.7 : 0.15);
                Bezier3 left = SamplingTests.Side(c, 1.75f), right = SamplingTests.Side(c, -1.75f);
                List<PaintableStretch> stretches = SamplingTests.Stretches(random);
                var zones = new[] { new Zones(i % 4 == 0, false), new Zones(false, i % 5 == 0) };
                adaptive = new List<SurfaceStrip>();
                for (int h = 0; h < 2; h++)
                {
                    capture.Segment("segment " + i, stretches);
                    capture.Half(left, right, zones[h]);
                    fixedBands.AddRange(SamplingTests.FixedBands(left, right, stretches, "segment " + i));
                    adaptive.AddRange(PartSampling.HalfBands(left, right, stretches, zones[h], "segment " + i, probe));
                }
            }
            else
            {
                Vec3[] c = SamplingTests.Centre(random, 1.1);
                Bezier3 outerLeft = SamplingTests.Side(c, 3.5f), middle = SamplingTests.Side(c, 0f), outerRight = SamplingTests.Side(c, -3.5f);
                var pieces = new List<NodePiece>
                {
                    new NodePiece(outerLeft, middle, SamplingTests.Stretches(random), false),
                    new NodePiece(middle, outerRight, SamplingTests.Stretches(random), i % 6 == 2)
                };
                foreach (NodePiece p in pieces)
                    fixedBands.AddRange(SamplingTests.FixedBands(p.Left, p.Right, p.Across, "junction of segment " + i));
                capture.Pieces("junction of segment " + i, pieces);
                adaptive = PartSampling.NodeBands(pieces, "junction of segment " + i, probe);
            }

            var reference = new PartComparison
            {
                Part = "part " + i,
                FixedMs = 0.03,
                FixedPieces = SamplingCheck.Pieces(fixedBands),
                AdaptivePieces = SamplingCheck.Pieces(adaptive),
                Measure = SamplingCheck.Measure(probe),
                Corners = SamplingCheck.Corners(fixedBands, adaptive, SamplingStats.CornerLimit),
                FixedArea = SamplingCheck.PlanArea(fixedBands),
                AdaptiveArea = SamplingCheck.PlanArea(adaptive)
            };
            Program.Check(capture.TryJob("part " + i, fixedBands, 0, 0.03, out ShadowJob job), "the capture makes a job for part " + i);
            return new Fixture { Job = job, Reference = reference, Node = node, Source = node ? "junction of segment " + i : "segment " + i, Fillers = node ? new[] { false, i % 6 == 2 } : null };
        }

        private static string Differ(in PartComparison a, in PartComparison b)
        {
            if (a.Part != b.Part || a.FixedPieces != b.FixedPieces || a.AdaptivePieces != b.AdaptivePieces)
                return "pieces or name";
            if (a.FixedArea != b.FixedArea || a.AdaptiveArea != b.AdaptiveArea)
                return "areas";
            if (a.Corners.Checked != b.Corners.Checked || a.Corners.Bad != b.Corners.Bad || a.Corners.CountDiffers != b.Corners.CountDiffers)
                return "corners";
            SamplingMeasure x = a.Measure, y = b.Measure;
            if (x.AdaptivePlan != y.AdaptivePlan || x.AdaptiveHeight != y.AdaptiveHeight || x.FixedPlan != y.FixedPlan || x.FixedHeight != y.FixedHeight || x.BetweenPlan != y.BetweenPlan || x.BetweenHeight != y.BetweenHeight || x.ReferenceArea != y.ReferenceArea)
                return "measure";
            return null;
        }

        // ------------------------------------------------------------------ the runner

        private static void Equivalence()
        {
            var random = new Random(20261107);
            int segments = 0, nodes = 0, differing = 0;
            string first = null;
            for (int i = 0; i < 90; i++)
            {
                Fixture part = Part(random, i);
                PartComparison made = ShadowRunner.Run(part.Job);
                string why = Differ(part.Reference, made);
                if (why != null && first == null)
                    first = $"part {i}: {why}";
                if (why != null)
                    differing++;
                if (part.Node)
                    nodes++;
                else
                    segments++;
                Program.Check(made.AdaptiveMs > 0.0 && made.ShadowMs >= made.AdaptiveMs && made.FixedMs == 0.03, "the times are set: " + made.AdaptiveMs + " " + made.ShadowMs);
                Program.Check(part.Job.Source == part.Source && part.Job.Part == "part " + i && part.Job.Node == part.Node, "the job says what part it is and what its bands are called");
                if (part.Node)
                    Program.Check(part.Job.Pieces.Length == 2 && part.Job.Pieces[0].Filler == part.Fillers[0] && part.Job.Pieces[1].Filler == part.Fillers[1], "a junction part's job keeps which of its pieces is a gore's filler");
            }

            Program.Check(segments == 60 && nodes == 30, $"both kinds of part were made: {segments} segments, {nodes} junction parts");
            Program.Check(differing == 0, "the comparison of the worker is the one the main thread made, to the bit: " + differing + " differ, the first " + first);

            // The corners are measured against the fixed bands the job holds: bands laid from curves 5 cm off do not meet the adaptive ones, and bands of another number are told.
            Vec3[] cc = SamplingTests.Centre(new Random(9), 0.3);
            Bezier3 cl = SamplingTests.Side(cc, 1.75f), cr = SamplingTests.Side(cc, -1.75f), shifted = SamplingTests.Side(cc, 1.80f);
            var across = new List<PaintableStretch> { new PaintableStretch(0f, 0.5f, 0f), new PaintableStretch(0.5f, 1f, 0f) };
            var off = new ShadowCapture();
            off.Segment("s", across);
            off.Half(cl, cr, Zones.None);
            List<SurfaceStrip> shiftedBands = SamplingTests.FixedBands(shifted, cr, across, "s");
            Program.Check(off.TryJob("off", shiftedBands, 0, 0.0, out ShadowJob offJob), "a job with fixed bands off by 5 cm");
            CornerResult direct = SamplingCheck.Corners(shiftedBands, PartSampling.HalfBands(cl, cr, across, Zones.None, "s", new SamplingProbe()), SamplingStats.CornerLimit);
            CornerResult viaRunner = ShadowRunner.Run(offJob).Corners;
            Program.Check(direct.Bad > 0 && viaRunner.Bad == direct.Bad && viaRunner.Checked == direct.Checked, "the corners off by 5 cm are found by the runner as they are found directly: " + direct.Bad + " " + viaRunner.Bad);
            List<SurfaceStrip> fewer = SamplingTests.FixedBands(cl, cr, across.GetRange(0, 1), "s");
            var count = new ShadowCapture();
            count.Segment("s", across);
            count.Half(cl, cr, Zones.None);
            Program.Check(count.TryJob("count", fewer, 0, 0.0, out ShadowJob countJob) && ShadowRunner.Run(countJob).Corners.CountDiffers, "fixed bands of another number are told");

            // The capture copies what it is given: a stretch list changed afterwards does not change the job.
            var stretches = new List<PaintableStretch> { new PaintableStretch(0f, 1f, 0f) };
            Vec3[] c = SamplingTests.Centre(new Random(5));
            Bezier3 left = SamplingTests.Side(c, 1.5f), right = SamplingTests.Side(c, -1.5f);
            var capture = new ShadowCapture();
            capture.Segment("s", stretches);
            capture.Half(left, right, Zones.None);
            List<SurfaceStrip> bands = SamplingTests.FixedBands(left, right, stretches, "s");
            Program.Check(capture.TryJob("p", bands, 0, 0.0, out ShadowJob job), "a job");
            stretches.Clear();
            stretches.Add(new PaintableStretch(0.4f, 0.5f, 3f));
            bands.Clear();
            Program.Check(job.Stretches.Length == 1 && job.Stretches[0].F0 == 0f && job.Stretches[0].F1 == 1f && job.FixedBands.Length == 1, "the job holds its own copies of the stretches and of the list of bands");

            // Nothing captured, nothing to compare, or the fixed bands that are not from first on: no job.
            var empty = new ShadowCapture();
            Program.Check(!empty.TryJob("p", bands, 0, 0.0, out _), "nothing captured: no job");
            var half = new ShadowCapture();
            half.Segment("s", stretches);
            half.Half(left, right, Zones.None);
            Program.Check(!half.TryJob("p", new List<SurfaceStrip>(), 0, 0.0, out _) && !half.TryJob("p", SamplingTests.FixedBands(left, right, stretches, "s"), 5, 0.0, out _) && !half.TryJob("p", null, 0, 0.0, out _), "no bands from first on: no job");
            var some = SamplingTests.FixedBands(left, right, new[] { new PaintableStretch(0f, 0.5f, 0f), new PaintableStretch(0.5f, 1f, 0f) }, "s");
            Program.Check(half.TryJob("p", some, 1, 0.0, out ShadowJob tail) && tail.FixedBands.Length == some.Count - 1 && ReferenceEquals(tail.FixedBands[0], some[1]), "the bands from first on, and only those");
            var segmentOnly = new ShadowCapture();
            segmentOnly.Segment("s", stretches);
            Program.Check(!segmentOnly.TryJob("p", some, 0, 0.0, out _), "stretches without a half: no job");
        }

        private static void Threads()
        {
            var random = new Random(20261108);
            var parts = new List<Fixture>();
            for (int i = 0; i < 24; i++)
                parts.Add(Part(random, i));

            // One job on eight threads that start together.
            ShadowJob one = parts[2].Job;
            PartComparison alone = ShadowRunner.Run(one);
            var results = new PartComparison[8];
            using (var start = new Barrier(8))
            {
                Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, k =>
                {
                    start.SignalAndWait();
                    for (int round = 0; round < 20; round++)
                        results[k] = ShadowRunner.Run(one);
                });
            }

            int bad = results.Count(r => Differ(alone, r) != null);
            Program.Check(bad == 0, "eight threads on one shared job: " + bad + " differ from the single-threaded one");

            // Many jobs on many threads against the single-threaded results.
            PartComparison[] expected = parts.Select(p => ShadowRunner.Run(p.Job)).ToArray();
            var got = new PartComparison[parts.Count];
            Parallel.For(0, parts.Count * 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, n =>
            {
                int i = n % parts.Count;
                PartComparison made = ShadowRunner.Run(parts[i].Job);
                if (Differ(expected[i], made) != null)
                    Interlocked.Increment(ref s_Bad);
                got[i] = made;
            });
            Program.Check(Volatile.Read(ref s_Bad) == 0, "the jobs run together give what they give alone: " + s_Bad);

            // The statistics are the same fed from the worker as fed directly (a part's numbers do not depend on who made them).
            var direct = new SamplingStats();
            var viaWorker = new SamplingStats();
            foreach (Fixture p in parts)
                direct.Add(p.Reference);
            foreach (PartComparison c in got)
                viaWorker.Add(c);
            Program.Check(direct.Compared == viaWorker.Compared && direct.OverTolerance == viaWorker.OverTolerance && direct.CornersBad == viaWorker.CornersBad && direct.AdaptivePlan == viaWorker.AdaptivePlan && direct.AdaptiveHeight == viaWorker.AdaptiveHeight
                && direct.AreaAdaptive == viaWorker.AreaAdaptive && direct.FixedPieces == viaWorker.FixedPieces && direct.AdaptivePieces == viaWorker.AdaptivePieces, "the statistics from the worker's results are the statistics of the direct ones");
        }

        private static int s_Bad;

        // ------------------------------------------------------------------ the worker

        private static ShadowDone WaitFor(ShadowWorker worker, int timeoutMs = 20000)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                if (worker.TryTake(out ShadowDone done))
                    return done;
                Thread.Sleep(1);
            }

            throw new TimeoutException("the shadow worker made nothing in " + timeoutMs + " ms");
        }

        private static void Worker()
        {
            var random = new Random(20261109);
            var parts = new List<Fixture>();
            for (int i = 0; i < 40; i++)
                parts.Add(Part(random, i));
            var worker = new ShadowWorker();
            Program.Check(worker.Outstanding == 0 && worker.Submitted == 0 && !worker.TryTake(out _), "a new worker has nothing");
            foreach (Fixture p in parts)
                worker.Submit(p.Job);
            Program.Check(worker.Outstanding == 40 && worker.Submitted == 40, "forty are out: " + worker.Outstanding);
            for (int i = 0; i < parts.Count; i++)
            {
                ShadowDone done = WaitFor(worker);
                Program.Check(done.Error == null && done.Comparison.Part == "part " + i && Differ(parts[i].Reference, done.Comparison) == null, "the comparison " + i + " comes in the order it was given and is right: " + done.Comparison.Part);
            }

            Program.Check(worker.Outstanding == 0 && worker.Failed == 0 && worker.WorkerMs > 0.0, "all taken, none failed, the thread's time counted: " + worker.WorkerMs);

            // A job that throws: the error comes back with it and the worker goes on.
            worker.Submit(new ShadowJob { Part = "broken", Source = "x", FixedBands = new SurfaceStrip[0], Halves = null, Stretches = null });
            worker.Submit(parts[0].Job);
            ShadowDone failed = WaitFor(worker), good = WaitFor(worker);
            Program.Check(failed.Error != null && good.Error == null && good.Comparison.Part == "part 0" && worker.Failed == 1, "an error is handed back, and the next job is made");

            // Clear forgets what waits and drops what is being made.
            for (int i = 0; i < 30; i++)
                worker.Submit(parts[i].Job);
            worker.Clear();
            Program.Check(worker.Outstanding == 0, "Clear: nothing is out");
            Thread.Sleep(300);
            int stray = 0;
            while (worker.TryTake(out _))
                stray++;
            Program.Check(stray == 0 && worker.Outstanding == 0, "and nothing that was out comes back: " + stray);
            worker.Submit(parts[3].Job);
            ShadowDone after = WaitFor(worker);
            Program.Check(after.Error == null && after.Comparison.Part == "part 3" && worker.Outstanding == 0, "after a Clear it works again");
        }

        // ------------------------------------------------------------------ nothing of the game, no state

        private static void Purity()
        {
            // A job is made of the mod's own pure types only: a Unity or game type in it could be read on a thread.
            foreach (Type type in new[] { typeof(ShadowJob), typeof(ShadowHalf), typeof(NodePiece), typeof(PaintableStretch), typeof(Zones), typeof(Bezier3), typeof(Vec3), typeof(SurfaceStrip), typeof(PartComparison), typeof(SamplingMeasure), typeof(CornerResult) })
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    Type of = field.FieldType.IsArray ? field.FieldType.GetElementType() : field.FieldType;
                    string ns = of.Namespace ?? "";
                    bool game = ns.StartsWith("Unity") || ns.StartsWith("Game") || ns.StartsWith("Colossal");
                    Program.Check(!game, type.Name + "." + field.Name + " is a " + of.FullName + ": nothing of the game goes to a thread");
                }
            }

            // What a thread runs holds no state that threads share: no static field that is writable or holds a collection that is.
            var bad = new List<string>();
            foreach (Type type in new[] { typeof(ShadowRunner), typeof(PartSampling), typeof(Flatten), typeof(SamplingCheck), typeof(SurfaceSection), typeof(SamplingProbe), typeof(ShadowCapture) })
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.IsLiteral)
                        continue;
                    bool mutableType = typeof(System.Collections.IDictionary).IsAssignableFrom(field.FieldType) || (typeof(System.Collections.IList).IsAssignableFrom(field.FieldType) && !field.FieldType.Name.StartsWith("ReadOnly") && !field.FieldType.IsArray)
                        || field.FieldType.Name.StartsWith("HashSet") || field.FieldType.Name.StartsWith("Queue");
                    if (!field.IsInitOnly || mutableType)
                        bad.Add(type.Name + "." + field.Name + " (" + field.FieldType.Name + ")");
                }
            }

            Program.Check(bad.Count == 0, "no shared mutable state in what a thread runs: " + string.Join(", ", bad));
        }

        private static void CaptureCost()
        {
            // What the main thread does for a part: the copies of the capture. Measured, so that a change that makes it dear is seen (it is bounded by PerFrame: a frame's admitted parts).
            var random = new Random(20261110);
            Vec3[] c = SamplingTests.Centre(random);
            Bezier3 left = SamplingTests.Side(c, 1.75f), right = SamplingTests.Side(c, -1.75f);
            List<PaintableStretch> stretches = SamplingTests.Stretches(random);
            List<SurfaceStrip> bands = SamplingTests.FixedBands(left, right, stretches, "s");
            int rounds = 2000;
            for (int warm = 0; warm < 50; warm++)
            {
                var w = new ShadowCapture();
                w.Segment("s", stretches);
                w.Half(left, right, Zones.None);
                w.Half(left, right, Zones.None);
                w.TryJob("p", bands, 0, 0.0, out _);
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < rounds; i++)
            {
                var capture = new ShadowCapture();
                capture.Segment("s", stretches);
                capture.Half(left, right, Zones.None);
                capture.Half(left, right, Zones.None);
                capture.TryJob("p", bands, 0, 0.0, out _);
            }

            double perPartMs = clock.Elapsed.TotalMilliseconds / rounds;
            Program.Check(perPartMs * ShadowGate.PerFrame < 0.2, $"a frame's most (32 parts) costs the main thread under 0.2 ms: {perPartMs * 1000.0:0.0} microseconds a part, {perPartMs * ShadowGate.PerFrame:0.000} ms");
        }

        // ------------------------------------------------------------------ the terrain gate

        private static void Terrain()
        {
            var gate = new TerrainGate();
            Program.Check(!gate.Hold(false) && gate.Held == 0, "nothing pending: nothing held");
            for (int i = 1; i <= 5; i++)
                Program.Check(gate.Hold(true), "pending: held, frame " + i);
            Program.Check(gate.Held == 5 && gate.Longest == 5 && gate.Forced == 0, "five frames held, the longest run five");
            Program.Check(!gate.Hold(false), "it is settled: let through");
            Program.Check(gate.Hold(true) && gate.Longest == 5, "a new run counts from one again, and the longest stays");

            gate = new TerrainGate();
            int held = 0;
            for (int frame = 0; frame < TerrainGate.MaxHoldFrames; frame++)
            {
                if (gate.Hold(true))
                    held++;
            }

            Program.Check(held == TerrainGate.MaxHoldFrames && gate.Forced == 0, "held for MaxHoldFrames while it stays pending");
            Program.Check(!gate.Hold(true) && gate.Forced == 1, "then one frame is let through to block, and counted");
            Program.Check(gate.Hold(true) && gate.Longest == TerrainGate.MaxHoldFrames, "and a new run of holds begins");

            // A terrain that is pending for ever lets one frame in every MaxHoldFrames + 1 through.
            gate = new TerrainGate();
            int through = 0;
            for (int frame = 0; frame < (TerrainGate.MaxHoldFrames + 1) * 5; frame++)
            {
                if (!gate.Hold(true))
                    through++;
            }

            Program.Check(through == 5 && gate.Forced == 5, "a flag that never clears lets the work through five times in five periods: " + through);
            gate.Reset();
            Program.Check(gate.Held == 0 && gate.Forced == 0 && gate.Longest == 0 && gate.Hold(true), "Reset forgets, and starts the run again");
        }

        // ------------------------------------------------------------------ the sliced read

        private static void Sliced()
        {
            var read = new SlicedRead<int, string>(Enumerable.Range(0, 10));
            Program.Check(read.Count == 10 && read.Next == 0 && !read.Finished && read.Done.Count == 0, "ten windows, none read");

            // A budget that is used up at once still reads one window a slice.
            int taken = read.Slice(w => "w" + w, () => 99.0, 0.3);
            Program.Check(taken == 1 && read.Next == 1 && read.Done.Count == 1 && read.Done[0].Window == 0 && read.Done[0].Result == "w0", "a slice over its budget reads one window");

            var edge = new SlicedRead<int, string>(Enumerable.Range(0, 4));
            Program.Check(edge.Slice(w => "w" + w, () => 0.3, 0.3) == 1, "a slice that has used exactly its budget reads no more than the one window");

            // A clock that advances 0.1 ms a window: three windows fit in 0.3 ms (the slice goes on while it has used less than the budget).
            double now = 0.0;
            taken = read.Slice(w => { now += 0.1; return "w" + w; }, () => now, 0.3);
            Program.Check(taken == 3 && read.Next == 4, "windows are read while the time used is under the budget (0.1, 0.2, then 0.3 is not under 0.3): " + taken);

            // Everything is read once, in order, whatever the slices.
            while (!read.Finished)
                read.Slice(w => "w" + w, () => 0.0, 0.2);
            Program.Check(read.Done.Count == 10 && read.Done.Select((d, i) => d.Window == i && d.Result == "w" + i).All(b => b), "every window once and in order");
            Program.Check(read.Slice(w => "x", () => 0.0, 1.0) == 0 && read.Done.Count == 10, "a finished read reads no more");

            // A window whose read throws is left out and counted; the others are read.
            var broken = new SlicedRead<int, string>(Enumerable.Range(0, 6));
            while (!broken.Finished)
                broken.Slice(w => w == 2 ? throw new InvalidOperationException("window 2") : "w" + w, () => 0.0, 1.0);
            Program.Check(broken.Done.Count == 5 && broken.Failed == 1 && broken.FirstFailure.Message == "window 2" && broken.Done.All(d => d.Window != 2), "a failed window is left out, counted, and its exception kept");
            var first = new SlicedRead<int, string>(Enumerable.Range(0, 3));
            first.Slice(w => throw new InvalidOperationException("w" + w), () => 99.0, 1.0);
            first.Slice(w => throw new InvalidOperationException("w" + w), () => 99.0, 1.0);
            Program.Check(first.Failed == 2 && first.FirstFailure.Message == "w0", "the first failure is the one kept");

            var none = new SlicedRead<int, string>(new int[0]);
            Program.Check(none.Finished && none.Slice(w => "x", () => 0.0, 1.0) == 0, "no windows: finished at once");
            var one = new SlicedRead<int, string>(new[] { 7 });
            one.Slice(w => "seven", () => 0.0, 1.0);
            Program.Check(one.Finished && one.Done[0].Result == "seven", "one window: one slice");
        }
    }
}
