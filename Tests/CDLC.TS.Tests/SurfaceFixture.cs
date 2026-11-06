using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The bands of a junction's surface for AreaFit.FromStrips, made up (the tests and BENCH_SURFACE use the same ones): every fourth band runs another way, each a lane wide, 16 samples along 60 m.</summary>
    internal static class SurfaceFixture
    {
        public static List<SurfaceStrip> Junction(int bands, int seed)
        {
            var random = new Random(seed);
            var strips = new List<SurfaceStrip>();
            for (int k = 0; k < bands; k++)
            {
                double angle = (k % 4) * Math.PI / 2.0 + (random.NextDouble() - 0.5) * 0.3;
                double across = (k / 4 % 6 - 2.5) * 3.5;
                var strip = new SurfaceStrip { Elevated = k % 9 == 8, Filler = k % 7 == 6 };
                for (int i = 0; i < 16; i++)
                {
                    double along = -30.0 + 60.0 * i / 15.0;
                    double wobble = Math.Sin(i * 0.4 + k) * 0.2;
                    double cx = along * Math.Cos(angle) - (across + wobble) * Math.Sin(angle), cz = along * Math.Sin(angle) + (across + wobble) * Math.Cos(angle);
                    double nx = -Math.Sin(angle), nz = Math.Cos(angle);
                    strip.Add(new Vec3((float)(cx - nx * 1.75), (float)(0.02 * i + 0.3 * (k % 3)), (float)(cz - nz * 1.75)), new Vec3((float)(cx + nx * 1.75), (float)(0.02 * i + 0.3 * (k % 3)), (float)(cz + nz * 1.75)));
                }

                strips.Add(strip);
            }

            return strips;
        }

        /// <summary>What a fit says over a grid of 0.5 m: how many places are on a piece and the sum of the heights there (to the millimetre), and the elevated ones among them.</summary>
        public static string Summary(AreaFit fit)
        {
            int found = 0, raised = 0;
            double sum = 0.0;
            var seen = new HashSet<int>();
            var list = new List<int>();
            for (float x = -32f; x <= 32f; x += 0.5f)
            {
                for (float z = -8f; z <= 8f; z += 0.5f)
                {
                    if (!fit.HeightAt(x, z, out float y, out bool elevated, seen, list))
                        continue;
                    found++;
                    sum += y;
                    if (elevated)
                        raised++;
                }
            }

            return $"{fit.PieceCount} pieces, {found} places, {raised} elevated, height sum {sum:0.000}";
        }
    }
}
