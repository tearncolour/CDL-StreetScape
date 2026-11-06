using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What the wiring of the adaptive sampler costs and what a simplified line would do (BENCH_SAMPLING=1; 0.12.75): the time of laying a part's bands by each sampler and of the shadow comparison (the
    /// budget is 0.2 ms a frame), and LineSimplify on a chain's line (what it does to the pieces, the items, the path text and the heading of an item; it is not wired).
    /// </summary>
    internal static class Sampling
    {
        public static void Run()
        {
            Cost();
            Console.WriteLine();
            Lines();
        }

        // ------------------------------------------------------------------ the cost of a part

        private static Vec3[] Centre(Random random, double turn, double rise = 1.0)
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
                y += (random.NextDouble() - 0.5) * 2.0 * rise;
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

        private static void Cost()
        {
            Console.WriteLine("Laying the bands of a part by each sampler, and the shadow comparison (microseconds a part, .NET Framework build, one thread; the game's Mono is slower). Heights move up to 10 cm between control points (a real road), or up to 1 m (steep):");
            Console.WriteLine("part                            | fixed lay | adaptive lay | measure | corners+areas | whole shadow | pieces fixed -> adaptive");
            foreach ((string name, double turn, bool node, int stretches, double rise) in new[] { ("half, nearly straight", 0.02, false, 4, 0.05), ("half, gentle, 4 stretches", 0.15, false, 4, 0.1), ("half, gentle, steep heights", 0.15, false, 4, 1.0), ("half, bend, 4 stretches", 0.7, false, 4, 0.1), ("half, bend, 8 stretches", 0.7, false, 8, 0.1), ("junction part, 3 pcs, 4 str.", 1.1, true, 4, 0.1), ("junction part, 3 pcs, 8 str.", 1.1, true, 8, 0.1) })
            {
                var random = new Random(11);
                const int N = 400;
                var inputs = new List<(Bezier3 L, Bezier3 M, Bezier3 R, List<PaintableStretch> S)>();
                for (int i = 0; i < N; i++)
                {
                    Vec3[] c = Centre(random, turn, rise);
                    inputs.Add((Side(c, 3.5f), Side(c, 0f), Side(c, -3.5f), Across(stretches)));
                }

                double tFixed = 0, tAdaptive = 0, tMeasure = 0, tOther = 0, tWhole = 0;
                long fixedPieces = 0, adaptivePieces = 0;
                for (int pass = 0; pass < 3; pass++)
                {
                    tFixed = tAdaptive = tMeasure = tOther = tWhole = 0;
                    fixedPieces = adaptivePieces = 0;
                    foreach ((Bezier3 l, Bezier3 m, Bezier3 r, List<PaintableStretch> s) in inputs)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        var fixedBands = new List<SurfaceStrip>();
                        if (node)
                        {
                            fixedBands.AddRange(Fixed(l, m, s));
                            fixedBands.AddRange(Fixed(m, r, s));
                            fixedBands.AddRange(Fixed(l, r, new List<PaintableStretch> { new PaintableStretch(0f, 1f, 0.05f) }));
                        }
                        else
                        {
                            fixedBands = Fixed(l, r, s);
                        }

                        long t1 = Stopwatch.GetTimestamp();
                        var probe = new SamplingProbe();
                        List<SurfaceStrip> adaptiveBands;
                        if (node)
                        {
                            adaptiveBands = PartSampling.NodeBands(new List<NodePiece> { new NodePiece(l, m, s, false), new NodePiece(m, r, s, false), new NodePiece(l, r, new List<PaintableStretch> { new PaintableStretch(0f, 1f, 0.05f) }, true) }, "n", probe);
                        }
                        else
                        {
                            adaptiveBands = PartSampling.HalfBands(l, r, s, new Zones(true, false), "h", probe);
                        }

                        long t2 = Stopwatch.GetTimestamp();
                        SamplingMeasure measure = SamplingCheck.Measure(probe);
                        long t3 = Stopwatch.GetTimestamp();
                        CornerResult corners = SamplingCheck.Corners(fixedBands, adaptiveBands, SamplingStats.CornerLimit);
                        double a = SamplingCheck.PlanArea(fixedBands), b = SamplingCheck.PlanArea(adaptiveBands);
                        long t4 = Stopwatch.GetTimestamp();
                        if (corners.Bad < 0 || a + b < 0 || measure.AdaptivePlan < 0)
                            Console.WriteLine("?");
                        tFixed += t1 - t0;
                        tAdaptive += t2 - t1;
                        tMeasure += t3 - t2;
                        tOther += t4 - t3;
                        tWhole += t2 - t1 + t3 - t2 + t4 - t3;
                        fixedPieces += SamplingCheck.Pieces(fixedBands);
                        adaptivePieces += SamplingCheck.Pieces(adaptiveBands);
                    }
                }

                double us = 1e6 / Stopwatch.Frequency / N;
                Console.WriteLine($"{name,-30} | {tFixed * us,9:0.0} | {tAdaptive * us,12:0.0} | {tMeasure * us,7:0.0} | {tOther * us,13:0.0} | {tWhole * us,12:0.0} | {fixedPieces / (double)N,6:0.0} -> {adaptivePieces / (double)N,6:0.0}");
            }
        }

        // ------------------------------------------------------------------ lines

        /// <summary>A line of the given length along an arc of radius r (0: straight), sampled every step metres, with a crest of the given height.</summary>
        private static List<Vec3> Line(double radius, double length, double step, double crest)
        {
            var line = new List<Vec3>();
            for (double s = 0.0; s <= length + 1e-9; s += step)
            {
                double y = crest * Math.Sin(2.0 * Math.PI * s / length) * 0.5;
                if (radius <= 0.0)
                {
                    line.Add(new Vec3((float)s, (float)y, 0f));
                }
                else
                {
                    double a = s / radius;
                    line.Add(new Vec3((float)(radius * Math.Sin(a)), (float)y, (float)(radius * (1.0 - Math.Cos(a)))));
                }
            }

            return line;
        }

        private static void Lines()
        {
            Console.WriteLine("LineSimplify (10 mm plan, 3 mm height, longest chord 6 m) on a chain's line of 400 m sampled every 2 m, with a crest of 2 m; not wired.");
            Console.WriteLine("what an item holds: the pieces of its path (at most 32), its length (at most 100 m) and the text of its path in the settings; ph and tl are lengths (cm) and do not change.");
            Console.WriteLine("line               | points now -> simplified | items now -> after | pieces in all now -> after | path chars in all now -> after | worst off the line (plan, height mm) | worst heading error (deg)");
            foreach (double radius in new[] { 0.0, 1000.0, 300.0, 100.0, 30.0 })
            {
                List<Vec3> line = Line(radius, 400.0, 2.0, 2.0);
                List<Vec3> simple = LineSimplify.Simplify(line, PartSampling.TolXz, PartSampling.TolY, 6f);
                List<List<Vec3>> now = RunPath.Split(line, MarkingRuns.MaxItemLength), after = RunPath.Split(simple, MarkingRuns.MaxItemLength, 6f);
                int piecesNow = 0, piecesAfter = 0, charsNow = 0, charsAfter = 0;
                foreach (List<Vec3> run in now)
                {
                    piecesNow += run.Count - 1;
                    charsNow += (RunPath.Encode(run, out _) ?? "").Length;
                }

                foreach (List<Vec3> run in after)
                {
                    piecesAfter += run.Count - 1;
                    charsAfter += (RunPath.Encode(run, out _) ?? "").Length;
                }

                Off(line, simple, out double plan, out double height, out double heading);
                Console.WriteLine($"{(radius <= 0 ? "straight" : "R " + radius + " m"),-18} | {line.Count,6} -> {simple.Count,6}        | {now.Count,5} -> {after.Count,5}    | {piecesNow,8} -> {piecesAfter,6}       | {charsNow,10} -> {charsAfter,6}          | {plan * 1000.0,8:0.0}, {height * 1000.0,6:0.0}                    | {heading,6:0.00}");
            }
        }

        /// <summary>How far the simplified line is from the original: at each original point, the distance to the simplified polyline (plan) and the difference of height there; and the angle between the simplified chord and the original line's own direction there.</summary>
        private static void Off(List<Vec3> line, List<Vec3> simple, out double plan, out double height, out double heading)
        {
            plan = height = heading = 0.0;
            for (int i = 0; i < line.Count; i++)
            {
                double best = double.MaxValue, bestHeight = 0.0, bestAngle = 0.0;
                Vec3 own = line[Math.Min(line.Count - 1, i + 1)] - line[Math.Max(0, i - 1)];
                for (int k = 0; k + 1 < simple.Count; k++)
                {
                    Vec3 a = simple[k], b = simple[k + 1];
                    double dx = b.X - a.X, dz = b.Z - a.Z, l2 = dx * dx + dz * dz;
                    double t = l2 > 1e-12 ? Math.Max(0.0, Math.Min(1.0, ((line[i].X - a.X) * dx + (line[i].Z - a.Z) * dz) / l2)) : 0.0;
                    double ex = a.X + dx * t - line[i].X, ez = a.Z + dz * t - line[i].Z, d = Math.Sqrt(ex * ex + ez * ez);
                    if (d < best)
                    {
                        best = d;
                        bestHeight = Math.Abs(a.Y + (b.Y - a.Y) * t - line[i].Y);
                        bestAngle = Math.Abs(Math.Atan2(dx * own.Z - dz * own.X, dx * own.X + dz * own.Z)) * 180.0 / Math.PI;
                    }
                }

                plan = Math.Max(plan, best);
                height = Math.Max(height, bestHeight);
                heading = Math.Max(heading, bestAngle);
            }
        }
    }
}
