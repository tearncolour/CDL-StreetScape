using System;
using System.Diagnostics;
using CDLC.TS.Geo;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// The preview of a variable-message sign on a gantry as the tool makes it when the cursor moves to another road (0.11.0's game log: "placing" 13 to 40 ms with gb.vms.text and gb.vms.lane on a gantry):
    /// the parts of the sign for a new speed of the road and a new span, not kept. BENCH_VMS=1.
    /// </summary>
    internal static class Vms
    {
        public static void Run()
        {
            SignBuilder.LedGrids = CDLC.TS.Render.LedScreens.Grid;
            Console.WriteLine("variable-message signs on a gantry, the parts made anew (ms):");
            uint gantry = SupportCatalog.ById("gantry.portal").Hash;
            foreach (string id in new[] { "gb.vms.text", "gb.vms.lane", "gb.vms.speed", "gb.vms.traffic", "guide.exit.advance" })
            {
                SignDef def = SignCatalog.ById(id);
                if (def == null)
                {
                    Console.WriteLine("  " + id + ": not in the catalogue");
                    continue;
                }

                string spec = def.Template != null ? def.Template.Default().ToString() : null;
                if (id == "gb.vms.text")
                    spec = def.Template.Parse(spec).With("l1", "前方事故").With("l2", "减速慢行").ToString();
                // Each run another span (as the cursor moves over roads of other widths), so that nothing is found in the cache.
                double total = 0, worst = 0;
                int runs = 0, leds = 0, metal = 0;
                for (int i = 0; i < 12; i++)
                {
                    float reach = 20f + i * 0.7f;
                    // The dots painted again too (the tool met a new speed band: a new instance, so a new grid).
                    if (Environment.GetEnvironmentVariable("BENCH_VMS_COLD") == "1")
                        CDLC.TS.Render.LedScreens.Clear();
                    // As the tool on roads of other speeds: the band of the road written into the settings (the screen's layout does not use it).
                    string here = spec;
                    if (Environment.GetEnvironmentVariable("BENCH_VMS_BANDS") == "1" && def.Template != null && def.Template.FieldOf("speed") != null)
                        here = def.Template.Parse(spec).With("speed", new[] { "40", "60", "100", "120" }[i % 4]).ToString();
                    var clock = Stopwatch.StartNew();
                    SignParts parts = ChunkBuilder.PartsOf(SignInstances.Of(def, here), gantry, reach);
                    double ms = clock.Elapsed.TotalMilliseconds;
                    if (i >= 2)
                    {
                        total += ms;
                        worst = Math.Max(worst, ms);
                        runs++;
                    }

                    leds = 0;
                    foreach (MeshBuilder m in parts.Leds)
                        leds += m == null ? 0 : m.VertexCount;
                    metal = parts.Metal.VertexCount;
                }

                Console.WriteLine($"  {id,-22} {total / runs,7:0.00} ms a time (worst {worst:0.00}); metal {metal} vertices, LED dots {leds} vertices; grids painted {CDLC.TS.Render.LedScreens.Painted}, found by the picture {CDLC.TS.Render.LedScreens.FoundByPicture}");
            }
        }
    }
}
