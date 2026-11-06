using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The cache of what the tool reads about the road under the cursor (0.7.1): an entry is good for its time to live, then it is made again; the cache never holds more than its size.</summary>
    internal static class ExpiringCacheTests
    {
        public static void RunAll()
        {
            Program.Run("expiring cache: an entry is made once while it is good and again when it is not", Basics);
            Program.Run("expiring cache: it never holds more than its size, and the expired go first", Bounded);
        }

        private sealed class Box
        {
            public int N;
        }

        private static void Basics()
        {
            var cache = new ExpiringCache<int, Box>(2f, 10);
            int made = 0;
            Func<Box> make = () => new Box { N = ++made };

            Box a = cache.Get(1, 10f, make);
            Program.Check(made == 1 && a.N == 1 && cache.Misses == 1 && cache.Hits == 0, "the first call makes it");
            Program.Check(ReferenceEquals(cache.Get(1, 10.5f, make), a) && ReferenceEquals(cache.Get(1, 11.99f, make), a) && made == 1 && cache.Hits == 2, "within the time to live it is the same one and nothing is made");
            Box b = cache.Get(1, 12f, make);
            Program.Check(!ReferenceEquals(a, b) && made == 2, "at the time to live it is made again");
            Program.Check(ReferenceEquals(cache.Get(1, 13f, make), b), "and the new one is good for its own time");

            // Another key is another entry.
            Box other = cache.Get(2, 13f, make);
            Program.Check(!ReferenceEquals(other, b) && made == 3 && cache.Count == 2, "another key is another entry");

            // A clock that goes back (a game that was loaded: the time of the session starts again) does not keep an entry for ever.
            Box old = cache.Get(3, 1000f, make);
            Box fresh = cache.Get(3, 5f, make);
            Program.Check(!ReferenceEquals(old, fresh), "an entry made at a later time than now is not good");

            // Remove and Clear.
            cache.Remove(2);
            int before = made;
            cache.Get(2, 13.1f, make);
            Program.Check(made == before + 1, "a removed entry is made again");
            cache.Clear();
            Program.Check(cache.Count == 0, "clear");

            // The value is made once even when the maker throws the first time (nothing is kept for it).
            var throwing = new ExpiringCache<int, Box>(2f, 4);
            bool threw = false;
            try
            {
                throwing.Get(1, 0f, () => throw new InvalidOperationException("no"));
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Program.Check(threw && throwing.Count == 0, "a maker that throws leaves nothing in the cache");
            Program.Check(throwing.Get(1, 0f, () => new Box { N = 5 }).N == 5, "and the next call makes it");
        }

        private static void Bounded()
        {
            var cache = new ExpiringCache<int, Box>(5f, 8);
            for (int i = 0; i < 8; i++)
                cache.Get(i, 0f, () => new Box());
            Program.Check(cache.Count == 8, "filled");

            // All are still good: a ninth starts the cache again (it cannot know which to drop), and the new one is there.
            Box ninth = cache.Get(100, 1f, () => new Box { N = 9 });
            Program.Check(cache.Count <= 8 && ReferenceEquals(cache.Get(100, 1.1f, () => new Box()), ninth), "a full cache of good entries starts again, with the new one in it: " + cache.Count);

            // Expired entries go first: the good ones stay.
            var mixed = new ExpiringCache<int, Box>(5f, 6);
            for (int i = 0; i < 3; i++)
                mixed.Get(i, 0f, () => new Box());
            for (int i = 3; i < 6; i++)
                mixed.Get(i, 4f, () => new Box());
            int made = 0;
            Box good = mixed.Get(4, 4.5f, () => { made++; return new Box(); });
            Program.Check(made == 0, "(entry 4 is good at 4.5)");
            mixed.Get(99, 6f, () => new Box());
            Program.Check(ReferenceEquals(mixed.Get(4, 6.1f, () => { made++; return new Box(); }), good) && made == 0, "the entries made at 4 s are still there at 6 s when a new one is added: only the expired (made at 0 s) went");
            Program.Check(mixed.Count == 4, "three good ones and the new one are held: " + mixed.Count);

            // A long run never holds more than the size.
            var run = new ExpiringCache<int, Box>(1f, 50);
            var rng = new Random(2);
            int worst = 0;
            for (int i = 0; i < 20000; i++)
            {
                run.Get(rng.Next(400), i * 0.01f, () => new Box());
                worst = Math.Max(worst, run.Count);
            }

            Program.Check(worst <= 50, "a long run holds at most 50: " + worst);
            Program.Check(run.Hits > 1000, "and it hits: " + run.Hits + " hits against " + run.Misses + " misses");
        }
    }
}
