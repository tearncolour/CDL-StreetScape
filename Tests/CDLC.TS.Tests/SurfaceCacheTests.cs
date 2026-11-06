using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The cache of the roads' surface by part (0.12.73, DESIGN-surface-cache.md): the SIEVE cache with weights, the fingerprint, the cache of parts and what a read takes from it.</summary>
    internal static class SurfaceCacheTests
    {
        public static void RunAll()
        {
            Program.Run("sieve: a hit marks an entry, a visited entry outlives one round of eviction, the oldest unvisited goes", Eviction);
            Program.Run("sieve: the hand goes on from where it stopped, and starts from the tail again past the newest entry", Hand);
            Program.Run("sieve: a pass of entries met once leaves the older entries that were used", Scan);
            Program.Run("sieve: the budget is a weight: light entries go for a heavy one, one heavier than the budget is not kept", Weights);
            Program.Run("sieve: a key set again, the counts, clear, and the slots of evicted entries are used again", Housekeeping);
            Program.Run("sieve: 3000 random operations give what a plain list kept in the same way gives", Differential);
            Program.Run("fingerprint: the same numbers the same hash, any one changed another, order matters, -0 is not 0", Prints);
            Program.Run("surface parts: a part is kept for its fingerprint, a changed road is made again, the weight is the points", Parts);
            Program.Run("surface parts: a read that takes parts from the cache is the read that makes them", Reads);
        }

        // ------------------------------------------------------------------ the SIEVE cache

        /// <summary>The keys of 0 to 29 that the cache holds. A probe is a hit and marks the entry: it is made last, when nothing more is put in.</summary>
        private static string Held(SieveCache<int, int> cache)
        {
            var keys = new List<int>();
            for (int key = 0; key < 30; key++)
            {
                if (cache.TryGet(key, out _))
                    keys.Add(key);
            }

            return string.Join(",", keys);
        }

        private static SieveCache<int, int> Fill(long budget, params int[] keys)
        {
            var cache = new SieveCache<int, int>(budget);
            foreach (int key in keys)
                cache.Set(key, key);
            return cache;
        }

        private static void Eviction()
        {
            // 1 was hit: the hand clears its bit and takes 2, the oldest that was not hit.
            SieveCache<int, int> cache = Fill(3, 1, 2, 3);
            cache.TryGet(1, out _);
            cache.Set(4, 4);
            Program.Check(Held(cache) == "1,3,4", "1 was visited and stays, 2 went: " + Held(cache));

            // The bit of 1 was cleared by the hand's pass: it is not kept for ever. The hand is on 3 now, and takes it, not 1.
            cache = Fill(3, 1, 2, 3);
            cache.TryGet(1, out _);
            cache.Set(4, 4);
            cache.Set(5, 5);
            Program.Check(Held(cache) == "1,4,5", "the next one to go is 3, where the hand stopped: " + Held(cache));

            // No hits: a queue.
            cache = Fill(3, 1, 2, 3, 4);
            Program.Check(Held(cache) == "2,3,4", "with no hit the oldest goes first");
            cache = Fill(3, 1, 2, 3, 4, 5);
            Program.Check(Held(cache) == "3,4,5", "and then the next");
        }

        private static void Hand()
        {
            // 2 and 3 are hit: 1 goes (the hand then rests on 2); 5 comes: the hand clears 2 and 3 and takes 4, the newest, so it has nowhere to rest.
            SieveCache<int, int> cache = Fill(3, 1, 2, 3);
            cache.TryGet(2, out _);
            cache.TryGet(3, out _);
            cache.Set(4, 4);
            cache.Set(5, 5);
            Program.Check(Held(cache) == "2,3,5", "the hand went on from 2 to 3 to 4: " + Held(cache));

            // The hand rests nowhere: it starts from the tail (2, whose bit is clear) and takes it.
            cache = Fill(3, 1, 2, 3);
            cache.TryGet(2, out _);
            cache.TryGet(3, out _);
            cache.Set(4, 4);
            cache.Set(5, 5);
            cache.Set(6, 6);
            Program.Check(Held(cache) == "3,5,6", "from the tail again: " + Held(cache));

            // Everything was hit: the hand clears them all, comes round past the newest and takes the oldest.
            cache = Fill(2, 1, 2);
            cache.TryGet(1, out _);
            cache.TryGet(2, out _);
            cache.Set(3, 3);
            Program.Check(Held(cache) == "2,3", "a whole round of visited entries: " + Held(cache));
        }

        private static void Scan()
        {
            // 0 and 1 are used, then ten entries are met once: the hand follows the newest ones and takes only them.
            SieveCache<int, int> cache = Fill(4, 0, 1, 2, 3);
            cache.TryGet(0, out _);
            cache.TryGet(1, out _);
            for (int i = 10; i < 20; i++)
                cache.Set(i, i);
            Program.Check(Held(cache) == "0,1,18,19", "the older entries that were used are still there, the scan went through: " + Held(cache));
        }

        private static void Weights()
        {
            var cache = new SieveCache<string, int>(100);
            cache.Set("a", 1, 30);
            cache.Set("b", 2, 30);
            cache.Set("c", 3, 30);
            Program.Check(cache.Weight == 90 && cache.Count == 3, "three of 30 are 90 of 100");
            Program.Check(cache.Set("big", 9, 70), "a heavy one is kept");
            Program.Check(cache.Count == 2 && cache.Weight == 100, "two of the light ones went for it (160 over 100, then 130 over 100): " + cache.Count + " held, " + cache.Weight);
            Program.Check(!cache.TryGet("a", out _) && !cache.TryGet("b", out _) && cache.TryGet("c", out _) && cache.TryGet("big", out _), "the two oldest, not the newest");

            Program.Check(!cache.Set("huge", 1, 101) && cache.Count == 2 && cache.Weight == 100 && !cache.TryGet("huge", out _), "one heavier than the whole budget is not kept and pushes nothing out");
            Program.Check(cache.Set("none", 1, 0) && cache.Weight == 100 && cache.Count == 3, "a weight of 0 is kept for nothing");
            Program.Check(!cache.Set("big", 5, 150) && !cache.TryGet("big", out _) && cache.Weight == 30, "a key set again with more than the budget is gone, the old value with it, and its weight is given back");

            var mixed = new SieveCache<int, int>(10);
            mixed.Set(1, 1, 4);
            mixed.Set(2, 2, 4);
            mixed.TryGet(1, out _);
            mixed.Set(3, 3, 4);
            Program.Check(mixed.Weight == 8 && Held(mixed) == "1,3", "the visited one stays, the weight of the one that went is given back");
            mixed = new SieveCache<int, int>(10);
            mixed.Set(1, 1, 4);
            mixed.Set(2, 2, 4);
            mixed.TryGet(1, out _);
            mixed.Set(3, 3, 4);
            mixed.Set(4, 4, 6);
            Program.Check(mixed.Weight == 10 && mixed.Count == 2, "a weight of 6 takes what it must (8 and 6 are over 10: one of 4 goes) and no more: " + mixed.Count + " held, " + mixed.Weight);
        }

        private static void Housekeeping()
        {
            var cache = new SieveCache<int, string>(2);
            cache.Set(1, "a");
            cache.Set(1, "b");
            Program.Check(cache.Count == 1 && cache.Weight == 1 && cache.TryGet(1, out string v) && v == "b", "a key set again has the new value, once");
            cache.Set(1, "c", 2);
            Program.Check(cache.Count == 1 && cache.Weight == 2 && cache.TryGet(1, out v) && v == "c", "a key set again with another weight is taken out and put in");
            cache.TryGet(9, out _);
            Program.Check(cache.Hits == 2 && cache.Misses == 1, "the hits and the misses are counted: " + cache.Hits + ", " + cache.Misses);
            cache.Clear();
            Program.Check(cache.Count == 0 && cache.Weight == 0 && cache.Hits == 0 && cache.Misses == 0, "clear forgets the entries, the weight and the counts");
            Program.Check(!cache.TryGet(1, out _), "and what was there is not there");
            for (int round = 0; round < 50; round++)
            {
                cache.Set(round, "x");
                cache.Set(1000 + round, "y");
            }

            Program.Check(cache.Count == 2 && cache.Weight == 2, "after many evictions the cache holds what the budget allows");
        }

        /// <summary>SIEVE as the paper has it, on a plain list (index 0 the oldest): what the cache must do whatever its slots and links.</summary>
        private sealed class Reference
        {
            private sealed class Entry
            {
                public int Key;
                public long Weight;
                public bool Visited;
            }

            private readonly List<Entry> m_List = new List<Entry>();
            private readonly long m_Budget;
            private Entry m_Hand;

            public Reference(long budget) { m_Budget = budget; }

            public int Count => m_List.Count;

            public long Weight { get; private set; }

            public bool TryGet(int key)
            {
                Entry e = m_List.Find(x => x.Key == key);
                if (e == null)
                    return false;
                e.Visited = true;
                return true;
            }

            public bool Set(int key, long weight)
            {
                Entry e = m_List.Find(x => x.Key == key);
                if (e != null)
                {
                    if (e.Weight == weight && weight <= m_Budget)
                    {
                        e.Visited = true;
                        return true;
                    }

                    Remove(e);
                }

                if (weight > m_Budget)
                    return false;
                while (Weight + weight > m_Budget)
                    Evict();
                m_List.Add(new Entry { Key = key, Weight = weight });
                Weight += weight;
                return true;
            }

            private void Evict()
            {
                int at = m_Hand != null ? m_List.IndexOf(m_Hand) : 0;
                while (m_List[at].Visited)
                {
                    m_List[at].Visited = false;
                    at = at + 1 < m_List.Count ? at + 1 : 0;
                }

                m_Hand = at + 1 < m_List.Count ? m_List[at + 1] : null;
                Remove(m_List[at]);
            }

            private void Remove(Entry e)
            {
                if (m_Hand == e)
                {
                    int next = m_List.IndexOf(e) + 1;
                    m_Hand = next < m_List.Count ? m_List[next] : null;
                }

                m_List.Remove(e);
                Weight -= e.Weight;
            }
        }

        private static void Differential()
        {
            var random = new Random(11);
            var cache = new SieveCache<int, int>(20);
            var reference = new Reference(20);
            string wrong = null;
            for (int step = 0; step < 3000 && wrong == null; step++)
            {
                int key = random.Next(0, 25);
                if (random.Next(2) == 0)
                {
                    long weight = random.Next(0, 8) == 0 ? random.Next(18, 26) : random.Next(1, 7);
                    bool a = cache.Set(key, step, weight), b = reference.Set(key, weight);
                    if (a != b)
                        wrong = $"step {step}: Set({key}, {weight}) {a} against {b}";
                }
                else
                {
                    bool a = cache.TryGet(key, out _), b = reference.TryGet(key);
                    if (a != b)
                        wrong = $"step {step}: TryGet({key}) {a} against {b}";
                }

                if (wrong == null && (cache.Count != reference.Count || cache.Weight != reference.Weight))
                    wrong = $"step {step}: {cache.Count} held, weight {cache.Weight}, against {reference.Count}, {reference.Weight}";
            }

            Program.Check(wrong == null, wrong ?? "same");
        }

        // ------------------------------------------------------------------ the fingerprint

        private static ulong Of(params float[] values)
        {
            var fingerprint = new Fingerprint();
            foreach (float v in values)
                fingerprint.Add(v);
            return fingerprint.Value;
        }

        private static void Prints()
        {
            Program.Check(Of(1f, 2f, 3f) == Of(1f, 2f, 3f), "the same numbers give the same hash");
            Program.Check(Of(1f, 2f, 3f) != Of(1f, 2f, 3.0000002f), "a number that changes by one step of a float gives another");
            Program.Check(Of(1f, 2f, 3f) != Of(3f, 2f, 1f) && Of(1f, 2f) != Of(2f, 1f), "the order matters");
            Program.Check(Of(1f, 2f) != Of(1f, 2f, 0f) && Of() != Of(0f), "a number more, or a zero, is another input");
            Program.Check(Of(0f) != Of(-0f), "-0 is not 0: a false match (a stale result) is the harm, an extra miss only costs a recomputation");
            var one = new Fingerprint();
            one.Add(7);
            var two = new Fingerprint();
            two.Add(7);
            two.Add(0);
            Program.Check(one.Value != two.Value, "an int more is another input");
            var asInt = new Fingerprint();
            asInt.Add(5);
            var asFloat = new Fingerprint();
            asFloat.Add(5f);
            Program.Check(asInt.Value != asFloat.Value, "an int and a float of the same value are not the same input");
            bool distinct = true;
            var seen = new HashSet<ulong>();
            for (int i = 0; i < 20000 && distinct; i++)
                distinct = seen.Add(Of(i * 0.37f, i, -i * 1.5f));
            Program.Check(distinct, "20000 different inputs give 20000 hashes");
        }

        // ------------------------------------------------------------------ the cache of parts

        private static SurfaceStrip Strip(int points, float x)
        {
            var strip = new SurfaceStrip { Source = "fixture " + x, Elevated = x > 5f };
            for (int i = 0; i < points; i++)
                strip.Add(new Vec3(x, 0.01f * i, i), new Vec3(x + 3f, 0.01f * i, i));
            return strip;
        }

        private static void Parts()
        {
            var cache = new SurfacePartCache<(int, int, int)>(100000);
            var into = new List<SurfaceStrip>();
            int segments = 0, junctions = 0;
            Program.Check(!cache.TryAdd((1, 0, 0), 11UL, into, ref segments, ref junctions) && cache.Misses == 1 && into.Count == 0, "a part not held is a miss");

            var made = new List<SurfaceStrip> { Strip(3, 0f) };
            int first = made.Count;
            made.Add(Strip(4, 1f));
            made.Add(Strip(5, 2f));
            cache.Keep((1, 0, 0), 11UL, made, first, 1, 2);
            Program.Check(cache.Count == 1 && cache.Bytes == SurfacePartCache<int>.WeightOf(new[] { made[1], made[2] }), "the strips from index first on are kept, not the ones before");
            Program.Check(cache.Bytes == SurfacePartCache<int>.BytesPerPoint * (2 * 4 + 2 * 5) + 2 * SurfacePartCache<int>.BytesPerStrip, "the weight is 12 bytes a point of both lists and 64 a strip");

            into.Add(Strip(1, 9f));
            segments = 5;
            junctions = 6;
            Program.Check(cache.TryAdd((1, 0, 0), 11UL, into, ref segments, ref junctions) && into.Count == 3 && ReferenceEquals(into[1], made[1]) && ReferenceEquals(into[2], made[2]), "a part held for this fingerprint adds its strips after the ones there, the same strips");
            Program.Check(segments == 6 && junctions == 8 && cache.Hits == 1, "and its counts to the read's");

            int before = into.Count;
            Program.Check(!cache.TryAdd((1, 0, 0), 12UL, into, ref segments, ref junctions) && into.Count == before && cache.Stale == 1 && cache.Misses == 2, "another fingerprint is a miss, and the road that changed is counted");
            Program.Check(!cache.TryAdd((1, 1, 0), 11UL, into, ref segments, ref junctions) && !cache.TryAdd((1, 0, 1), 11UL, into, ref segments, ref junctions) && cache.Stale == 1, "another part or another rule of the same edge is another entry");

            cache.Keep((1, 0, 0), 12UL, made, first, 1, 2);
            Program.Check(cache.Count == 1 && cache.TryAdd((1, 0, 0), 12UL, into, ref segments, ref junctions) && !cache.TryAdd((1, 0, 0), 11UL, into, ref segments, ref junctions), "kept again for the new fingerprint, it replaces the old");

            var tiny = new SurfacePartCache<int>(1000);
            tiny.Keep(1, 1UL, new List<SurfaceStrip> { Strip(100, 0f) }, 0, 0, 0);
            Program.Check(tiny.Count == 0 && tiny.Bytes == 0, "a part heavier than the budget is not kept");
            var small = new List<SurfaceStrip> { Strip(5, 0f) };
            long one = SurfacePartCache<int>.WeightOf(new[] { small[0] });
            for (int key = 2; key < 10; key++)
                tiny.Keep(key, 1UL, small, 0, 0, 0);
            Program.Check(tiny.Count == 1000 / one && tiny.Bytes == tiny.Count * one && tiny.Bytes <= tiny.Budget, "the budget is in bytes: as many as fit (" + one + " each): " + tiny.Count);
            cache.Clear();
            Program.Check(cache.Count == 0 && cache.Hits == 0 && cache.Misses == 0 && cache.Stale == 0, "clear forgets the parts and the counts");
        }

        // A road made of parts, each from the curves it is given (as the read's MakeEdge makes them): the part's inputs are its fingerprint, as the game's curves are in the read.
        private sealed class Part
        {
            public float Bend, Width;
            public float[] Fractions;

            public ulong Print()
            {
                var fingerprint = new Fingerprint();
                fingerprint.Add(Bend);
                fingerprint.Add(Width);
                foreach (float f in Fractions)
                    fingerprint.Add(f);
                return fingerprint.Value;
            }
        }

        private static List<SurfaceStrip> Make(Part part, int index)
        {
            var strips = new List<SurfaceStrip>();
            for (int s = 0; s + 1 < part.Fractions.Length; s += 2)
            {
                var stretch = new PaintableStretch(part.Fractions[s], part.Fractions[s + 1], 0.02f * s);
                Func<float, Vec3> left = t => new Vec3(index * 20f + part.Bend * t * t, 0.1f * t, 20f * t);
                Func<float, Vec3> right = t => new Vec3(index * 20f + part.Width + part.Bend * t * t, 0.1f * t, 20f * t);
                List<SurfaceStrip> bands = SurfaceSection.Bands(left, right, stretch, 16, "part " + index);
                foreach (SurfaceStrip band in bands)
                    band.Elevated = s == 2;
                strips.AddRange(bands);
            }

            return strips;
        }

        private static List<SurfaceStrip> ReadAll(Part[] parts, SurfacePartCache<(int, int, int)> cache, ref int made)
        {
            var read = new List<SurfaceStrip>();
            int segments = 0, junctions = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                (int, int, int) key = (i, 0, 0);
                ulong print = parts[i].Print();
                if (cache != null && cache.TryAdd(key, print, read, ref segments, ref junctions))
                    continue;
                int first = read.Count;
                read.AddRange(Make(parts[i], i));
                made++;
                cache?.Keep(key, print, read, first, 1, 0);
            }

            return read;
        }

        private static bool Same(List<SurfaceStrip> a, List<SurfaceStrip> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Source != b[i].Source || a[i].Elevated != b[i].Elevated || a[i].Filler != b[i].Filler || a[i].Left.Count != b[i].Left.Count || a[i].Right.Count != b[i].Right.Count)
                    return false;
                for (int k = 0; k < a[i].Left.Count; k++)
                {
                    if (!a[i].Left[k].Equals(b[i].Left[k]) || !a[i].Right[k].Equals(b[i].Right[k]))
                        return false;
                }
            }

            return true;
        }

        private static void Reads()
        {
            var parts = new Part[6];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = new Part { Bend = 0.5f * i, Width = 6f + i, Fractions = new[] { 0f, 0.3f, 0.35f, 0.9f } };
            var cache = new SurfacePartCache<(int, int, int)>(1 << 20);
            int madeA = 0, madeB = 0, madeC = 0, madeD = 0, none = 0, tinyMade = 0;
            List<SurfaceStrip> plain = ReadAll(parts, null, ref madeA);
            List<SurfaceStrip> first = ReadAll(parts, cache, ref madeB);
            List<SurfaceStrip> second = ReadAll(parts, cache, ref madeC);
            Program.Check(plain.Count == 12 && madeA == 6 && madeB == 6 && madeC == 0, "the first read made the parts, the second made none: " + madeB + ", " + madeC);
            Program.Check(Same(plain, first) && Same(plain, second), "a read that takes its parts from the cache is the read that makes them: the same strips in the same order, point for point");
            bool shared = true;
            for (int i = 0; i < first.Count; i++)
                shared &= ReferenceEquals(first[i], second[i]);
            Program.Check(shared, "and the strips are the ones that were kept");

            parts[3].Bend += 0.001f;
            List<SurfaceStrip> after = ReadAll(parts, cache, ref madeD);
            Program.Check(madeD == 1 && cache.Stale == 1, "a road whose curve changed is made again, alone: " + madeD);
            List<SurfaceStrip> plainAfter = ReadAll(parts, null, ref none);
            Program.Check(Same(plainAfter, after) && !Same(plain, after), "and the read is the new one, not the old");

            var tiny = new SurfacePartCache<(int, int, int)>(2 * SurfacePartCache<int>.WeightOf(new[] { first[0], first[1] }));
            List<SurfaceStrip> a = ReadAll(parts, tiny, ref tinyMade);
            List<SurfaceStrip> b = ReadAll(parts, tiny, ref tinyMade);
            Program.Check(Same(plainAfter, a) && Same(plainAfter, b) && tiny.Bytes <= tiny.Budget, "a cache too small for the whole road still gives the same read");

            // first[1] is the strip of the second stretch of a part, made elevated by Make.
            SurfaceStrip filler = first[1].AsFiller();
            Program.Check(first[1].Elevated && !first[0].Elevated && filler.Filler && filler.Elevated && !first[1].Filler && filler.Source == first[1].Source && filler.Left.Count == first[1].Left.Count && filler.Left[3].Equals(first[1].Left[3]),
                "a filler copy of a shared strip has the points of the strip, its elevation and the flag, and the strip is not changed");
            Program.Check(!first[0].AsFiller().Elevated, "and the copy of one that is not elevated is not");
            Program.Check(((IList<Vec3>)first[0].Left).IsReadOnly && ((IList<Vec3>)first[0].Right).IsReadOnly, "the lists of a strip are read only to the users of a read");
        }
    }
}
