using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The bounded cache of the meshes and boards (0.7.0): it never holds more than twice its capacity and keeps what is used.</summary>
    internal static class CacheTests
    {
        public static void RunAll()
        {
            Program.Run("two-generation cache: bounded, and what is used stays", Basics);
            Program.Run("two-generation cache: a working set that fits is never rebuilt, one cleared when full would be", WorkingSet);
            Program.Run("weighted two-generation cache: bounded by what the entries weigh", Weighted);
            Program.Run("marking plans: found by the written settings, kept by their vertices, pick shapes made once", Plans);
        }

        private static void Weighted()
        {
            // A budget of 100: entries weigh their value.
            var cache = new WeightedTwoGenCache<int, int>(100, v => v);
            Program.Check(cache.Count == 0 && cache.Weight == 0 && !cache.TryGet(1, out _), "empty");
            for (int i = 0; i < 10; i++)
                cache.Set(i, 10);
            Program.Check(cache.Count == 10 && cache.Weight == 100, "filled to the budget: all held, weight 100: " + cache.Weight);

            // One more of 10 passes the budget: the ten turn old and are still found; what is found moves up (its weight with it).
            cache.Set(100, 10);
            Program.Check(cache.Count == 11 && cache.Weight == 110 && cache.TryGet(0, out int v0) && v0 == 10, "after the rotation the old entries are still there");
            Program.Check(cache.Weight == 110 && cache.Count == 11, "moving one up keeps the count and the weight: " + cache.Count + " " + cache.Weight);

            // Ten more: 0 was used in this generation and stays; 5 was not and is gone.
            for (int i = 101; i < 111; i++)
                cache.Set(i, 10);
            Program.Check(cache.TryGet(0, out _), "0 was used in the last generation and is kept");
            Program.Check(!cache.TryGet(5, out _), "5 was not used for a whole generation and is gone");

            // A generation holds the whole budget: ten of 10, then nine more, and the first is still there (it would be gone had the first generation turned old at 90).
            var exact = new WeightedTwoGenCache<int, int>(100, v => v);
            for (int i = 0; i < 19; i++)
                exact.Set(i, 10);
            Program.Check(exact.TryGet(0, out _), "a generation of exactly the budget is not turned old before the next entry");

            // Heavy and light entries at random: never more than twice the budget and one entry.
            var rng = new Random(3);
            var random = new WeightedTwoGenCache<int, int>(1000, v => v);
            long worst = 0;
            for (int i = 0; i < 50000; i++)
            {
                random.Set(i, 1 + rng.Next(rng.Next(10) == 0 ? 400 : 20));
                if (i % 4 == 0)
                    random.TryGet(i - rng.Next(200), out _);
                worst = Math.Max(worst, random.Weight);
            }

            Program.Check(worst <= 2000 + 400, "bounded by twice the budget and one entry: " + worst);
            long sum = 0;
            for (int i = 0; i < 50000; i++)
            {
                if (random.TryGet(i, out int w))
                    sum += w;
            }

            Program.Check(sum == random.Weight, "the weight is the sum of what is held: " + sum + " " + random.Weight);

            // Replacing an entry counts its new weight, not both; an entry heavier than the budget is held alone; clear empties the weight too.
            var small = new WeightedTwoGenCache<string, int>(50, v => v);
            small.Set("a", 20);
            small.Set("a", 30);
            Program.Check(small.Count == 1 && small.Weight == 30, "replacing: " + small.Weight);
            small.Set("big", 500);
            Program.Check(small.TryGet("big", out int big) && big == 500 && small.TryGet("a", out _), "a heavy entry is held (and the light one as old)");
            small.Set("c", 1);
            Program.Check(small.TryGet("big", out _) && small.TryGet("c", out _), "the next one rotates");
            small.Clear();
            Program.Check(small.Count == 0 && small.Weight == 0 && !small.TryGet("c", out _), "clear");
            var zero = new WeightedTwoGenCache<int, int>(0, v => 0);
            zero.Set(1, 1);
            zero.Set(2, 2);
            Program.Check(zero.Count == 2 && zero.Weight == 2, "a weight of 0 counts as 1, a budget of 0 as 1: " + zero.Count + " " + zero.Weight);
        }

        private static void Plans()
        {
            GuideTemplate lane = MarkingTemplates.ById("mk.line.whitesolid");
            var arc = new List<Vec3>();
            for (int i = 0; i <= 30; i++)
                arc.Add(new Vec3((float)(30.0 * (1.0 - Math.Cos(i * 0.03))), 0f, (float)(30.0 * Math.Sin(i * 0.03))));
            string canonical = lane.Parse(MarkingTemplates.WithPath(lane, lane.Default().ToString(), RunPath.Encode(arc, out _))).ToString();
            // The same settings written in another order (as an old save or a hand edit might have them): the same plan.
            string[] fields = canonical.Split(';');
            Array.Reverse(fields);
            string reordered = string.Join(";", fields);
            Program.Check(reordered != canonical && lane.Parse(reordered).ToString() == canonical, "the reordered settings parse to the same");
            MarkingPlan a = MarkingTemplates.PlanOf(lane, canonical), b = MarkingTemplates.PlanOf(lane, reordered), c = MarkingTemplates.PlanOf(lane, reordered);
            Program.Check(a != null && ReferenceEquals(a, b) && ReferenceEquals(b, c), "one plan for both writings");
            // The second writing is kept as a second key of the same plan: looked up again it is not parsed.
            long parsedBefore = MarkingTemplates.Parsed;
            int keysBefore = MarkingTemplates.CachedPlans;
            Program.Check(ReferenceEquals(MarkingTemplates.PlanOf(lane, reordered), a) && ReferenceEquals(MarkingTemplates.PlanOf(lane, canonical), a), "found again by both writings");
            Program.Check(MarkingTemplates.Parsed == parsedBefore && MarkingTemplates.CachedPlans == keysBefore, "without parsing, and without keeping more: " + (MarkingTemplates.Parsed - parsedBefore) + " parse(s)");
            string third = string.Join(";", new[] { fields[1], fields[0] }) + ";" + string.Join(";", fields, 2, fields.Length - 2);
            MarkingPlan d3 = MarkingTemplates.PlanOf(lane, third);
            Program.Check(ReferenceEquals(d3, a) && MarkingTemplates.Parsed == parsedBefore + 1 && MarkingTemplates.CachedPlans == keysBefore + 1, "a third writing: parsed once, kept as one more key of the same plan");

            // A plan made from a writing that is not the parser's is kept under both: found again by that writing without parsing.
            var other2 = new List<Vec3>(arc);
            other2[29] = other2[29] + new Vec3(0.3f, 0f, 0f);
            string fresh2 = lane.Parse(MarkingTemplates.WithPath(lane, lane.Default().ToString(), RunPath.Encode(other2, out _))).ToString();
            string[] f2 = fresh2.Split(';');
            Array.Reverse(f2);
            string rev2 = string.Join(";", f2);
            MarkingPlan made = MarkingTemplates.PlanOf(lane, rev2);
            long p2 = MarkingTemplates.Parsed;
            Program.Check(ReferenceEquals(MarkingTemplates.PlanOf(lane, rev2), made) && MarkingTemplates.Parsed == p2 && ReferenceEquals(MarkingTemplates.PlanOf(lane, fresh2), made) && MarkingTemplates.Parsed == p2, "a new plan is kept under the writing it was asked by and the parser's");

            // A plan for a preview (not kept): made, not kept, and the kept ones stay.
            var preview = new List<Vec3>(arc);
            preview[28] = preview[28] + new Vec3(0.2f, 0f, 0f);
            string previewSpec = MarkingTemplates.WithPath(lane, lane.Default().ToString(), RunPath.Encode(preview, out _));
            int keysNow = MarkingTemplates.CachedPlans;
            MarkingPlan shown = MarkingTemplates.PlanOf(lane, previewSpec, false);
            Program.Check(shown != null && shown.Parts.Count > 0 && MarkingTemplates.CachedPlans == keysNow, "a preview's plan is made and not kept");
            Program.Check(!ReferenceEquals(MarkingTemplates.PlanOf(lane, previewSpec, false), shown), "so asking again makes it again");
            Program.Check(ReferenceEquals(MarkingTemplates.PlanOf(lane, canonical, false), a), "a preview of a kept plan is the kept plan");
            Program.Check(a.VertexCount > 0 && a.VertexCount == CountVertices(a), "the plan weighs its vertices: " + a.VertexCount);
            Program.Check(MarkingTemplates.CachedPlans > 0 && MarkingTemplates.CachedVertices >= a.VertexCount, "the cache reports what it holds");
            // Another path is another plan.
            arc[30] = arc[30] + new Vec3(0.5f, 0f, 0f);
            MarkingPlan other = MarkingTemplates.PlanOf(lane, MarkingTemplates.WithPath(lane, lane.Default().ToString(), RunPath.Encode(arc, out _)));
            Program.Check(other != null && !ReferenceEquals(other, a), "another path, another plan");

            // The pick shapes: made once, the same as PickShapes makes, with the plan's bounds.
            SignParts picks = a.CachedPickParts();
            Program.Check(ReferenceEquals(picks, a.CachedPickParts()), "made once");
            List<PickShape> fresh = new List<PickShape>(a.PickShapes(2f, 120));
            bool same = fresh.Count == picks.Picks.Count;
            for (int i = 0; same && i < fresh.Count; i++)
                same = fresh[i].Min.Equals(picks.Picks[i].Min) && fresh[i].Max.Equals(picks.Picks[i].Max);
            Program.Check(same && fresh.Count > 1, "the boxes are those of PickShapes: " + fresh.Count + " " + picks.Picks.Count);
            Program.Check(picks.Min.X == a.MinX && picks.Min.Z == a.MinZ && picks.Max.X == a.MaxX && picks.Max.Z == a.MaxZ && picks.Max.Y > picks.Min.Y, "and the bounds those of the plan");
            ItemRecord record = new ItemRecord(lane.Hash, new Vec3(10f, 0f, 20f), 0f, 0u, reordered);
            Program.Check(ReferenceEquals(ChunkBuilder.PartsOfRecord(record), picks), "PartsOfRecord of the marking gives them, whichever way its settings are written");

            // Pick shapes asked for from many threads at once: one set.
            MarkingPlan fork = MarkingTemplates.PlanOf(lane, MarkingTemplates.WithPath(lane, lane.Default().ToString(), RunPath.Encode(new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(1f, 0f, 20f), new Vec3(4f, 0f, 40f) }, out _)));
            var got = new SignParts[8];
            var threads = new System.Threading.Thread[8];
            for (int t = 0; t < 8; t++)
            {
                int k = t;
                threads[t] = new System.Threading.Thread(() => got[k] = fork.CachedPickParts());
            }

            foreach (System.Threading.Thread t in threads)
                t.Start();
            foreach (System.Threading.Thread t in threads)
                t.Join();
            bool one = true;
            foreach (SignParts p in got)
                one &= ReferenceEquals(p, got[0]) && p != null;
            Program.Check(one, "eight threads, one set of pick shapes");
        }

        private static int CountVertices(MarkingPlan plan)
        {
            int n = 0;
            foreach (MarkingPart p in plan.Parts)
                n += p.Mesh.Positions.Count;
            return n;
        }

        private static void Basics()
        {
            var cache = new TwoGenCache<int, string>(10);
            Program.Check(cache.Capacity == 10 && cache.Count == 0 && !cache.TryGet(1, out _), "empty");
            for (int i = 0; i < 10; i++)
                cache.Set(i, "v" + i);
            Program.Check(cache.Count == 10 && cache.TryGet(3, out string v3) && v3 == "v3", "filled to the capacity: all held");

            // The next new entry makes the first ten the old generation; they are still found, and what is found moves up.
            cache.Set(100, "new");
            Program.Check(cache.Count == 11 && cache.TryGet(0, out string v0) && v0 == "v0" && cache.TryGet(100, out _), "after the rotation the old entries are still there");

            // A second rotation drops what was not used in between.
            for (int i = 101; i < 111; i++)
                cache.Set(i, "n" + i);
            Program.Check(cache.TryGet(0, out _), "0 was used in the last generation and is kept");
            Program.Check(!cache.TryGet(5, out _), "5 was not used for a whole generation and is gone");

            // Never more than twice the capacity, whatever is put in.
            var big = new TwoGenCache<int, int>(50);
            for (int i = 0; i < 100000; i++)
            {
                big.Set(i, i);
                if (i % 3 == 0)
                    big.TryGet(i / 2, out _);
                if (big.Count > 100)
                {
                    Program.Check(false, "more than twice the capacity: " + big.Count);
                    break;
                }
            }

            Program.Check(big.Count <= 100, "bounded: " + big.Count);

            // Setting an entry that is there replaces it and does not rotate.
            var small = new TwoGenCache<int, int>(2);
            small.Set(1, 1);
            small.Set(2, 2);
            small.Set(1, 11);
            small.Set(2, 22);
            Program.Check(small.Count == 2 && small.TryGet(1, out int one) && one == 11 && small.TryGet(2, out int two) && two == 22, "replacing does not push anything out");
            small.Clear();
            Program.Check(small.Count == 0 && !small.TryGet(1, out _), "clear");
            Program.Check(new TwoGenCache<int, int>(0).Capacity == 1, "a capacity of 0 is 1");
        }

        private static void WorkingSet()
        {
            // 300 kinds used over and over, a cache of 400: nothing is ever built twice. With 600 kinds used in turn the old way (emptied when full) builds every one every time.
            var rng = new Random(1);
            int built = 0;
            var cache = new TwoGenCache<int, int>(400);
            for (int i = 0; i < 20000; i++)
            {
                int key = rng.Next(300);
                if (!cache.TryGet(key, out _))
                {
                    built++;
                    cache.Set(key, key);
                }
            }

            Program.Check(built == 300, "a working set that fits is built once each: " + built);

            // 500 kinds in a cache of 400 each way: the two generations keep most of what is used, the old way clears it.
            int builtTwoGen = 0, builtClear = 0;
            var twoGen = new TwoGenCache<int, int>(400);
            var plain = new Dictionary<int, int>();
            for (int i = 0; i < 40000; i++)
            {
                int key = rng.Next(500);
                if (!twoGen.TryGet(key, out _))
                {
                    builtTwoGen++;
                    twoGen.Set(key, key);
                }

                if (!plain.ContainsKey(key))
                {
                    builtClear++;
                    if (plain.Count >= 400)
                        plain.Clear();
                    plain[key] = key;
                }
            }

            Program.Check(builtTwoGen < builtClear / 2, $"a set a little over the capacity: {builtTwoGen} builds against {builtClear} for a cache that is emptied when it is full");
        }
    }
}
