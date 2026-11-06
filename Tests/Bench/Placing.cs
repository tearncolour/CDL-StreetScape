using System;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What the pure part of one frame of placing a sign costs (BENCH_PLACING=1, 0.12.65; the owner: "性能占用不要超过1ms", the game's profile had PlacementTool at 1.51 ms): the calls of the sign-placing
    /// path that do not touch the game's ECS, as it makes them every frame the cursor is over a road, in microseconds each (the game's Mono is slower).
    /// </summary>
    internal static class Placing
    {
        private static double Us(int repeats, Action action)
        {
            action();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++)
                action();
            return clock.Elapsed.TotalMilliseconds * 1000.0 / repeats;
        }

        public static void Run()
        {
            SignDef def = SignCatalog.ById("guide.exit.advance") ?? SignCatalog.ById("gb.prohib.01");
            string draft = def.Template != null ? def.Template.Default().ToString() : "";
            var curve = new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(30f, 0f, 20f), new Vec3(60f, 0f, 50f), new Vec3(100f, 0f, 90f));
            ArcTable arc = new ArcTable(curve);
            Vec3 cursor = new Vec3(40f, 0f, 36f);
            string spec = GuideTemplates.ResolveSpeed(def, draft, 60f, true, false);
            SignInstance instance = SignInstances.Of(def, spec);
            SupportDef support = SupportPlanner.Auto(instance.Def);
            var rows = new (string, double)[]
            {
                ("EdgeSnap.Solve (three a frame)", Us(5000, () => EdgeSnap.Solve(arc, 12f, cursor, EdgeSnapOptions.Default))),
                ("GuideTemplates.ResolveSpeed", Us(5000, () => GuideTemplates.ResolveSpeed(def, draft, 60f, true, false))),
                ("template.Parse(spec)", Us(5000, () => def.Template?.Parse(spec))),
                ("SignInstances.Of (cached)", Us(20000, () => SignInstances.Of(def, spec))),
                ("SupportPlanner.AutoFor", Us(5000, () => SupportPlanner.AutoFor(instance.Def, new SupportContext { LanesSameDirection = 2 }))),
                ("SupportLayouts.Of", Us(5000, () => SupportLayouts.Of(instance.Def, support))),
                ("SupportCatalog.For", Us(20000, () => SupportCatalog.For(instance.Def, support.Hash))),
                ("SignCatalog.ChoiceById", Us(20000, () => SignCatalog.ChoiceById(def.Id)))
            };
            Console.WriteLine($"one frame of placing {def.Id}: the pure calls, microseconds each:");
            double sum = 0;
            foreach ((string name, double us) in rows)
            {
                Console.WriteLine($"  {name,-34} {us,9:0.0} us");
                sum += us;
            }

            Console.WriteLine($"  {"all of them once (Solve x3)",-34} {sum + 2 * rows[0].Item2,9:0.0} us");
        }
    }
}
