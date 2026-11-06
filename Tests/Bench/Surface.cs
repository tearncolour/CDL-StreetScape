using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using CDLC.TS.Tests;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What reading a window of road surface costs in its pure part (BENCH_SURFACE=1): AreaFit.FromStrips over the bands of a junction (66 bands, 16 samples each, as the owner's log of 31 Oct says of a
    /// window that took 92 ms) and the heights of the 30 points of a window. The game's own reading (the search tree, the compositions) is not here.
    /// </summary>
    internal static class Surface
    {
        public static void Run()
        {
            Console.WriteLine("road surface of a window of 30 points (60 m): FromStrips and HeightAt, milliseconds:");
            foreach (int bands in new[] { 12, 30, 66, 120 })
            {
                List<SurfaceStrip> strips = SurfaceFixture.Junction(bands, 7);
                AreaFit fit = null;
                const int repeats = 20;
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < repeats; i++)
                    fit = AreaFit.FromStrips(strips, new Vec3(0f, 0f, 0f), 0f, -30f, 30f, -3f, 3f);
                double from = clock.Elapsed.TotalMilliseconds / repeats;
                var seen = new HashSet<int>();
                var found = new List<int>();
                clock.Restart();
                for (int i = 0; i < repeats; i++)
                {
                    for (int p = 0; p < 30; p++)
                        fit.HeightAt(-29f + p * 2f, 0.5f, out _, out _, seen, found);
                }

                AreaFit loose = null;
                clock.Restart();
                for (int i = 0; i < repeats; i++)
                    loose = AreaFit.ForHeights(strips, new Vec3(0f, 0f, 0f), 0f, -30f, 30f, -3f, 3f);
                double forHeights = clock.Elapsed.TotalMilliseconds / repeats;
                Console.WriteLine("      " + SurfaceFixture.Summary(fit));
                Console.WriteLine("      " + SurfaceFixture.Summary(loose) + $"   <- ForHeights {forHeights:0.00} ms");
                Console.WriteLine($"  {bands,4} bands: FromStrips {from,8:0.00} ms ({fit.PieceCount} pieces), 30 heights {clock.Elapsed.TotalMilliseconds / repeats,8:0.000} ms");
            }
        }
    }
}
