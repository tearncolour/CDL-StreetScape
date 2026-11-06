using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What moving the adaptive sampler's shadow to a worker saves the main thread (BENCH_SHADOW=1; 0.12.75): the time the main thread spent on a part's shadow before (laying the part again with the
    /// adaptive sampler, measuring it, comparing it: the whole comparison) against what it spends now (the capture: copying the curves and the stretches and the list of bands), and what a worker
    /// makes of it (parts a second on one thread). Microseconds a part, .NET Framework build; the game's Mono is slower.
    /// </summary>
    internal static class ShadowWorkerBench
    {
        public static void Run()
        {
            Console.WriteLine("The shadow of a part: on the main thread before, and what the main thread does now (copies for the worker), microseconds a part.");
            Console.WriteLine("part                            | before: whole comparison | now: the capture and the job | worker: one comparison | parts a second on one worker");
            foreach ((string name, double turn, bool node, int stretches) in new[] { ("half, nearly straight", 0.02, false, 4), ("half, gentle, 4 stretches", 0.15, false, 4), ("half, tight", 0.7, false, 4), ("junction part, 3 pieces", 1.1, true, 4) })
            {
                var random = new Random(11);
                const int N = 400;
                var parts = new List<(Bezier3 L, Bezier3 M, Bezier3 R, List<PaintableStretch> S, List<SurfaceStrip> Fixed)>();
                for (int i = 0; i < N; i++)
                {
                    Vec3[] c = Centre(random, turn);
                    Bezier3 l = Side(c, 3.5f), m = Side(c, 0f), r = Side(c, -3.5f);
                    List<PaintableStretch> s = Across(stretches);
                    var fixedBands = new List<SurfaceStrip>();
                    if (node)
                    {
                        fixedBands.AddRange(Fixed(l, m, s));
                        fixedBands.AddRange(Fixed(m, r, s));
                    }
                    else
                    {
                        fixedBands = Fixed(l, r, s);
                    }

                    parts.Add((l, m, r, s, fixedBands));
                }

                double before = 0, now = 0, worker = 0;
                for (int pass = 0; pass < 4; pass++)
                {
                    before = now = worker = 0;
                    foreach ((Bezier3 l, Bezier3 m, Bezier3 r, List<PaintableStretch> s, List<SurfaceStrip> fixedBands) in parts)
                    {
                        // Before: the main thread lays the part again, measures it and compares (the old Shadow).
                        long t0 = Stopwatch.GetTimestamp();
                        var probe = new SamplingProbe();
                        List<SurfaceStrip> adaptive;
                        if (node)
                            adaptive = PartSampling.NodeBands(new List<NodePiece> { new NodePiece(l, m, s, false), new NodePiece(m, r, s, false) }, "n", probe);
                        else
                            adaptive = PartSampling.HalfBands(l, r, s, new Zones(true, false), "h", probe);
                        SamplingMeasure measure = SamplingCheck.Measure(probe);
                        CornerResult corners = SamplingCheck.Corners(fixedBands, adaptive, SamplingStats.CornerLimit);
                        double a = SamplingCheck.PlanArea(fixedBands), b = SamplingCheck.PlanArea(adaptive);
                        long t1 = Stopwatch.GetTimestamp();
                        if (corners.Bad < 0 || a + b < 0 || measure.AdaptivePlan < 0)
                            Console.WriteLine("?");

                        // Now: the capture, as SurfaceReader makes it while it lays the fixed bands, and the job.
                        var capture = new ShadowCapture();
                        if (node)
                        {
                            capture.Pieces("n", new List<NodePiece> { new NodePiece(l, m, s, false), new NodePiece(m, r, s, false) });
                        }
                        else
                        {
                            capture.Segment("h", s);
                            capture.Half(l, r, new Zones(true, false));
                            capture.Half(l, r, new Zones(false, false));
                        }

                        capture.TryJob("p", fixedBands, 0, 0.0, out ShadowJob job);
                        long t2 = Stopwatch.GetTimestamp();
                        PartComparison done = ShadowRunner.Run(job);
                        long t3 = Stopwatch.GetTimestamp();
                        if (done.Corners.Bad < 0)
                            Console.WriteLine("?");
                        before += t1 - t0;
                        now += t2 - t1;
                        worker += t3 - t2;
                    }
                }

                double us = 1e6 / Stopwatch.Frequency / N;
                Console.WriteLine($"{name,-30} | {before * us,24:0.0} | {now * us,28:0.0} | {worker * us,22:0.0} | {1e6 / (worker * us),28:0}");
            }

            Console.WriteLine();
            Console.WriteLine("2,000 parts through one worker thread, the main thread only giving them (a frame gives at most 32):");
            {
                var random = new Random(3);
                var jobs = new List<ShadowJob>();
                for (int i = 0; i < 2000; i++)
                {
                    Vec3[] c = Centre(random, 0.4);
                    Bezier3 l = Side(c, 3.5f), r = Side(c, -3.5f);
                    List<PaintableStretch> s = Across(4);
                    var capture = new ShadowCapture();
                    capture.Segment("h", s);
                    capture.Half(l, r, new Zones(i % 3 == 0, false));
                    capture.Half(l, r, new Zones(false, i % 4 == 0));
                    capture.TryJob("p" + i, Fixed(l, r, s), 0, 0.0, out ShadowJob job);
                    jobs.Add(job);
                }

                var worker = new ShadowWorker();
                var clock = Stopwatch.StartNew();
                foreach (ShadowJob job in jobs)
                    worker.Submit(job);
                double giveMs = clock.Elapsed.TotalMilliseconds;
                int taken = 0;
                while (taken < jobs.Count)
                {
                    if (worker.TryTake(out ShadowDone done))
                        taken++;
                    else
                        System.Threading.Thread.Sleep(1);
                }

                Console.WriteLine($"giving 2,000 jobs took the main thread {giveMs:0.0} ms in all; the worker made them in {clock.Elapsed.TotalMilliseconds:0} ms ({worker.WorkerMs:0} ms of its thread), {worker.Failed} failed");
            }
        }

        private static Vec3[] Centre(Random random, double turn)
        {
            var c = new Vec3[4];
            double heading = random.NextDouble() * Math.PI * 2.0, x = 0.0, z = 0.0, y = 0.0;
            for (int i = 0; i < 4; i++)
            {
                c[i] = new Vec3((float)x, (float)y, (float)z);
                double length = 8.0 + random.NextDouble() * 22.0;
                heading += (random.NextDouble() - 0.5) * turn;
                x += Math.Sin(heading) * length;
                z += Math.Cos(heading) * length;
                y += (random.NextDouble() - 0.5) * 2.0;
            }

            return c;
        }

        private static Bezier3 Side(Vec3[] c, float offset)
        {
            var p = new Vec3[4];
            for (int i = 0; i < 4; i++)
            {
                Vec3 ahead = (c[Math.Min(3, i + 1)] - c[Math.Max(0, i - 1)]).HeadingXZ();
                p[i] = c[i] + new Vec3(-ahead.Z, 0f, ahead.X) * offset;
            }

            return new Bezier3(p[0], p[1], p[2], p[3]);
        }

        private static List<PaintableStretch> Across(int stretches)
        {
            var list = new List<PaintableStretch>();
            for (int i = 0; i < stretches; i++)
                list.Add(new PaintableStretch(i / (float)stretches, (i + 0.9f) / stretches, 0.01f * i));
            return list;
        }

        private static List<SurfaceStrip> Fixed(Bezier3 l, Bezier3 r, IList<PaintableStretch> stretches)
        {
            var bands = new List<SurfaceStrip>();
            foreach (PaintableStretch s in stretches)
                bands.AddRange(SurfaceSection.Bands(t => l.Point(t), t => r.Point(t), s, PartSampling.FixedSamples, "b"));
            return bands;
        }
    }
}
