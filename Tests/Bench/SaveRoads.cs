using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// The pure part of what a save's record and its restore do with the roads (BENCH_SAVEROADS=1; the ECS audit, 0.12.82): the segments keyed by where they end, and each anchor matched against them
    /// (SaveDataSystem.Roads, Match). The engine part (reading each segment, CreateEntity per item, the prefab names) cannot be run here: the restore's log line says what it cost in the game.
    /// </summary>
    internal static class SaveRoads
    {
        private static int s_Sink;

        public static void Run(int roads, int items)
        {
            var rng = new Random(7);
            var segments = new List<RoadAnchor>(roads);
            for (int i = 0; i < roads; i++)
            {
                float x = (float)rng.NextDouble() * 8000f, z = (float)rng.NextDouble() * 8000f;
                segments.Add(new RoadAnchor { Prefab = "Road" + i % 250, A = new Vec3(x, 0f, z), B = new Vec3(x + 10f, 0f, z), C = new Vec3(x + 20f, 0f, z), D = new Vec3(x + 30f, 0f, z), StartDegree = 3, EndDegree = 3 });
            }

            var clock = Stopwatch.StartNew();
            var byKey = new Dictionary<long, List<RoadAnchor>>();
            foreach (RoadAnchor anchor in segments)
            {
                if (!byKey.TryGetValue(anchor.Key, out List<RoadAnchor> list))
                    byKey[anchor.Key] = list = new List<RoadAnchor>(1);
                list.Add(anchor);
            }

            double build = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            int same = 0;
            for (int i = 0; i < items; i++)
            {
                RoadAnchor anchor = segments[i % roads];
                if (byKey.TryGetValue(anchor.Key, out List<RoadAnchor> list))
                {
                    foreach (RoadAnchor now in list)
                        same += anchor.Compare(now) == AnchorChange.Same ? 1 : 0;
                }
            }

            s_Sink += same;
            Console.WriteLine($"save roads bench: {roads} segment(s) keyed in {build:0.0} ms, {items} item anchor(s) matched in {clock.Elapsed.TotalMilliseconds:0.0} ms ({same} the same)");
        }
    }
}
