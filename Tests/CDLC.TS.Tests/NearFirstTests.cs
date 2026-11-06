using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The order of the work after a load (0.12.27, NearFirst; the owner: "主要还是加载时间优化"): what is near the camera first, nearest first, the rest after it in its own order.</summary>
    internal static class NearFirstTests
    {
        public static void RunAll()
        {
            Program.Run("near first: nearest first, the far ones wait in their order", Order);
            Program.Run("near first: a city's queue", City);
        }

        private static void Order()
        {
            var places = new Dictionary<int, (float, float)> { [1] = (900f, 0f), [2] = (10f, 0f), [3] = (0f, 300f), [4] = (2000f, 0f), [5] = (0f, -50f), [6] = (850f, 0f) };
            var queue = new List<int> { 1, 2, 3, 4, 5, 6, 7 };
            int near = NearFirst.Order(queue, e => places.TryGetValue(e, out (float, float) p) ? p : ((float, float)?)null, 0f, 0f, 800f);
            Program.Check(near == 4, "four within 800 m (one with no place counts as here): " + near);
            Program.Check(queue[0] == 7 && queue[1] == 2 && queue[2] == 5 && queue[3] == 3, "nearest first: " + string.Join(",", queue));
            Program.Check(queue[4] == 1 && queue[5] == 4 && queue[6] == 6, "the far ones after them, in the order they had");
            Program.Check(queue.Count == 7, "none lost");
            var empty = new List<int>();
            Program.Check(NearFirst.Order(empty, e => (0f, 0f), 0f, 0f, 800f) == 0 && empty.Count == 0, "an empty queue");
            var moved = new List<int> { 1, 2, 3, 4, 5, 6 };
            Program.Check(NearFirst.Order(moved, e => places[e], 1900f, 0f, 800f) == 1 && moved[0] == 4, "the camera elsewhere: what is near it now");
        }

        private static void City()
        {
            // A city of 30 000 signs over 10 km, the camera in it: what is within 800 m comes first, in the order of the distance, and the ordering is quick.
            var rng = new Random(27);
            var places = new (float, float)[30000];
            var queue = new List<int>(places.Length);
            for (int i = 0; i < places.Length; i++)
            {
                places[i] = ((float)(rng.NextDouble() * 10000.0), (float)(rng.NextDouble() * 10000.0));
                queue.Add(i);
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            int near = NearFirst.Order(queue, e => places[e], 5000f, 5000f, 800f);
            double ms = clock.Elapsed.TotalMilliseconds;
            Program.Check(near > 500 && near < 900, "about 2 % of the city is within 800 m: " + near);
            bool sorted = true;
            float last = -1f;
            for (int i = 0; i < near; i++)
            {
                (float x, float z) = places[queue[i]];
                float d = (x - 5000f) * (x - 5000f) + (z - 5000f) * (z - 5000f);
                sorted &= d >= last && d <= 800f * 800f;
                last = d;
            }

            Program.Check(sorted, "nearest first, all within");
            Program.Check(ms < 200.0, "ordered in " + ms.ToString("0.0") + " ms");
            Console.WriteLine($"  near first: a queue of {queue.Count} ordered in {ms:0.0} ms, {near} near the camera");
        }
    }
}
