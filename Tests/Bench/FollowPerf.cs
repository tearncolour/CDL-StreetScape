using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.FollowReference;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// The cost of making the items of a road follow it, item by item and in batches (BENCH_FOLLOWPERF=1; 0.12.74 follow-road work, the owner: "继续优化道路跟随的算法和性能表现还有响应速度"): ns, bytes and
    /// generation-0 collections of a call, best and worst of several runs, and the curve evaluations a call makes. Pure parts only, one thread. The baseline rows are the frozen copy of ca86e49 (FollowReference).
    /// </summary>
    internal static partial class FollowPerf
    {
        internal struct Result
        {
            public double BestNs, WorstNs, Bytes;
            public int Collections;
            public double Evals;
        }

        /// <summary>Several runs of a call: ns, bytes and collections for one call, and the curve evaluations of one call (the frozen copy counts them; the new code reports its own through evals).</summary>
        internal static Result Measure(Action call, Func<long> evals = null, int runs = 5, double runMs = 120.0)
        {
            call();
            call();
            var clock = Stopwatch.StartNew();
            int probe = 0;
            while (clock.Elapsed.TotalMilliseconds < 20.0)
            {
                call();
                probe++;
            }

            int repeats = Math.Max(1, (int)(probe * runMs / clock.Elapsed.TotalMilliseconds));
            var result = new Result { BestNs = double.MaxValue };
            GC.Collect();
            int collections = GC.CollectionCount(0);
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long totalCalls = 0;
            for (int r = 0; r < runs; r++)
            {
                clock.Restart();
                for (int i = 0; i < repeats; i++)
                    call();
                double ns = clock.Elapsed.TotalMilliseconds * 1e6 / repeats;
                result.BestNs = Math.Min(result.BestNs, ns);
                result.WorstNs = Math.Max(result.WorstNs, ns);
                totalCalls += repeats;
            }

            result.Bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / (double)totalCalls;
            result.Collections = GC.CollectionCount(0) - collections;
            if (evals != null)
            {
                long before = evals();
                call();
                result.Evals = evals() - before;
            }

            return result;
        }

        internal static void Row(string name, Result r, int items = 1)
        {
            string per = items > 1 ? $" {r.BestNs / items,9:0} ns/item" : "";
            Console.WriteLine($"  {name,-54} {r.BestNs,12:0} ns best {r.WorstNs,12:0} worst {r.Bytes,10:0} B {r.Collections,4} gen0 {r.Evals,7:0} evals{per}");
        }

        internal static Bezier3 Edge(int i)
        {
            Vec3 start = new Vec3(i * 3f, 0.2f * i, i * 60f);
            Vec3 end = start + new Vec3((float)Math.Sin(i * 0.6) * 10f, 0.2f, 60f);
            return new Bezier3(start, start + new Vec3(0f, 0f, 20f), end - new Vec3(0f, 0f, 20f), end);
        }

        internal static Bezier3 Dragged(Bezier3 c, int frame, int frames)
        {
            // A drag of a node: the far end of the curve stays, the near end and its handle move by a growing share of (40, 1, -25): a bend, not a move.
            float k = frame / (float)frames;
            var d = new Vec3(40f * k, 1f * k, -25f * k);
            return new Bezier3(c.A + d, c.B + d * 0.6f, c.C, c.D);
        }

        /// <summary>The place of an item given by the relation it would have: a fraction, a lateral, a height and a yaw on a curve.</summary>
        internal static (Vec3 position, float yaw)[] Items(Bezier3 curve, int count, int seed)
        {
            var random = new Random(seed);
            var items = new (Vec3, float)[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)random.NextDouble();
                Vec3 on = curve.Point(t), right = curve.Tangent(t).RightXZ();
                items[i] = (on + right * (float)(random.NextDouble() * 16.0 - 8.0) + new Vec3(0f, (float)random.NextDouble() * 2f, 0f), (float)(random.NextDouble() * 6.0 - 3.0));
            }

            return items;
        }

        public static void Run()
        {
            Console.WriteLine($"road follow baseline (frozen copy of ca86e49), {(Type.GetType("Mono.Runtime") != null ? "Mono" : ".NET " + Environment.Version)}, {IntPtr.Size * 8}-bit");
            Bezier3 road = Edge(3), now = Dragged(road, 45, 90);
            RefArc oldArc = new RefArc(road), nowArc = new RefArc(now);
            var items = Items(road, 1000, 5);
            var relations = new RefRelation[items.Length];
            for (int i = 0; i < items.Length; i++)
                RefRelation.TryMeasure(oldArc, items[i].position, items[i].yaw, out relations[i]);

            Console.WriteLine("1. one item, one edge");
            int k = 0;
            Row("measure (ClosestT + relation), arc table given", Measure(() => RefRelation.TryMeasure(oldArc, items[k++ % 1000].position, 0.3f, out _), () => Ref.Evals));
            Row("apply to a new curve, arc table given", Measure(() => relations[k++ % 1000].TryApply(nowArc, FollowMode.Fraction, out _, out _), () => Ref.Evals));
            Row("per edge: an arc table (64 steps)", Measure(() => new RefArc(now), () => Ref.Evals));
            Row("per edge: closest point search (32 samples + 24 steps)", Measure(() => Ref.ClosestT(road, items[k++ % 1000].position), () => Ref.Evals));

            Console.WriteLine("2. a line item of 32 chords of 2 m, along one road (the smooth map, as the game would run it)");
            var line = new List<Vec3>();
            for (int i = 0; i <= 32; i++)
            {
                float t = oldArc.TAt(oldArc.Length * (0.02f + 0.9f * i / 32f));
                line.Add(road.Point(t) + road.Tangent(t).RightXZ() * 6f);
            }

            string path = RunPath.Encode(line, out float lineYaw);
            var pair = new[] { new RefRelation.Pair { Old = oldArc, Now = nowArc } };
            Row("follow of a 32-chord line (decode, map, encode)", Measure(() => RefPathFollow.TryRemap(path, line[0], lineYaw, pair, FollowMode.Fraction, out _, out _, out _), () => Ref.Evals));
            var mapped = new List<Vec3>();
            Row("  of which the map of its 33 points", Measure(() => RefRelation.TryMapPoints(pair, FollowMode.Fraction, line, mapped), () => Ref.Evals), 33);
            Row("  of which Encode of the mapped points", Measure(() => RunPath.Encode(mapped, out _)));
            RefRelation.TryMapPoints(pair, FollowMode.Fraction, line, mapped);
            List<Vec3> local = RunPath.Decode(path);
            Row("  of which Decode and ToWorld", Measure(() => MarkingPolygon.ToWorld(RunPath.Decode(path), line[0], lineYaw)));

            Console.WriteLine("3. a batch of items on ONE edge, one frame of a drag (arc table of the new curve + each item applied)");
            foreach (int n in new[] { 1, 10, 100, 1000 })
            {
                int count = n;
                Row($"{count} item(s)", Measure(() =>
                {
                    var arc = new RefArc(now);
                    for (int i = 0; i < count; i++)
                        relations[i].TryApply(arc, FollowMode.Fraction, out _, out _);
                }, () => Ref.Evals), count);
            }

            Console.WriteLine("4. a batch of items on 17 ADJACENT edges (a node drag moves two; a whole road moves all), one frame: 17 arc tables + each item applied");
            var edges = new Bezier3[17];
            var moved = new Bezier3[17];
            var edgeItems = new (Vec3 position, float yaw)[17][];
            var edgeRelations = new RefRelation[17][];
            for (int e = 0; e < 17; e++)
            {
                edges[e] = Edge(e);
                moved[e] = Dragged(edges[e], 45, 90);
                RefArc a = new RefArc(edges[e]);
                edgeItems[e] = Items(edges[e], 60, 100 + e);
                edgeRelations[e] = new RefRelation[60];
                for (int i = 0; i < 60; i++)
                    RefRelation.TryMeasure(a, edgeItems[e][i].position, edgeItems[e][i].yaw, out edgeRelations[e][i]);
            }

            foreach (int n in new[] { 17, 170, 1020 })
            {
                int per = n / 17;
                Row($"{n} item(s) on 17 edges", Measure(() =>
                {
                    for (int e = 0; e < 17; e++)
                    {
                        var arc = new RefArc(moved[e]);
                        for (int i = 0; i < per; i++)
                            edgeRelations[e][i].TryApply(arc, FollowMode.Fraction, out _, out _);
                    }
                }, () => Ref.Evals), n);
            }

            Console.WriteLine("5. lines: 17 items of 32 chords, one per edge, each mapped by the one road it runs along");
            var texts = new string[17];
            var lineYaws = new float[17];
            var lineStarts = new Vec3[17];
            var linePairs = new RefRelation.Pair[17][];
            for (int e = 0; e < 17; e++)
            {
                RefArc a = new RefArc(edges[e]);
                var pts = new List<Vec3>();
                for (int i = 0; i <= 32; i++)
                {
                    float t = a.TAt(a.Length * (0.02f + 0.9f * i / 32f));
                    pts.Add(edges[e].Point(t) + edges[e].Tangent(t).RightXZ() * 6f);
                }

                texts[e] = RunPath.Encode(pts, out lineYaws[e]);
                lineStarts[e] = pts[0];
                linePairs[e] = new[] { new RefRelation.Pair { Old = a, Now = new RefArc(moved[e]) } };
            }

            Row("17 line items, a node drag frame (arc tables prebuilt)", Measure(() =>
            {
                for (int e = 0; e < 17; e++)
                    RefPathFollow.TryRemap(texts[e], lineStarts[e], lineYaws[e], linePairs[e], FollowMode.Fraction, out _, out _, out _);
            }, () => Ref.Evals), 17);
            GC.KeepAlive(local);
            RunNew();
        }

        static partial void RunNewPart();

        private static void RunNew() => RunNewPart();
    }
}
