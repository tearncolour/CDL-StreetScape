using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The ledger of the planters' plants and their hash (0.12.72): what is made, kept, taken away, and the work in slices.</summary>
    internal static class PlantLedgerTests
    {
        public static void RunAll()
        {
            Program.Run("plants: the hash is what it was before 0.12.72 and makes no garbage", Hash);
            Program.Run("plants: a place offered is made once, kept while it is as wished, made again when it changes", Offering);
            Program.Run("plants: what stands and is not offered again is taken away by the sweep, in slices", Sweeping);
            Program.Run("plants: a plant that is not alive is made again; the newest wish is made first; clear forgets all", Edges);
        }

        private static PlantWish Wish(uint hash) => new PlantWish { At = new Vec3(1f, 2f, 3f), Yaw = 0.5f, Hash = hash, Anchor = new PropAnchor { Kind = "bush", Room = 1.5f } };

        /// <summary>The hash as it was written before (an array of the five numbers).</summary>
        private static uint Reference(Vec3 at, float yaw, uint index, string kind)
        {
            unchecked
            {
                uint h = SignCatalog.HashOf(kind ?? "");
                int[] values = { (int)Math.Round(at.X * 100f), (int)Math.Round(at.Y * 100f), (int)Math.Round(at.Z * 100f), (int)Math.Round(yaw * 1000f), (int)index };
                foreach (int v in values)
                    h = (h ^ (uint)v) * 16777619u;
                return h;
            }
        }

        private static void Hash()
        {
            var random = new Random(7);
            bool same = true;
            string[] kinds = { "bush", "tree", "hedge", "", null, "NA_LindenTree01" };
            for (int i = 0; i < 2000 && same; i++)
            {
                var at = new Vec3((float)(random.NextDouble() * 8000 - 4000), (float)(random.NextDouble() * 600 - 100), (float)(random.NextDouble() * 8000 - 4000));
                float yaw = (float)(random.NextDouble() * 12.6 - 6.3);
                uint index = (uint)random.Next(0, 400);
                string kind = kinds[random.Next(kinds.Length)];
                same = PlantHash.Of(at, yaw, index, kind) == Reference(at, yaw, index, kind);
            }

            Program.Check(same, "2000 places give the hash of the array version");
            Program.Check(PlantHash.Of(new Vec3(1f, 2f, 3f), 0.5f, 0, "bush") != PlantHash.Of(new Vec3(1f, 2f, 3f), 0.5f, 1, "bush"), "another index is another hash");
            Program.Check(PlantHash.Of(new Vec3(1f, 2f, 3f), 0.5f, 0, "bush") != PlantHash.Of(new Vec3(1f, 2f, 3f), 0.5f, 0, "tree"), "another kind is another hash");
            Program.Check(PlantHash.Of(new Vec3(1f, 2f, 3f), 0.5f, 0, "bush") != PlantHash.Of(new Vec3(1.05f, 2f, 3f), 0.5f, 0, "bush"), "a place 5 cm away is another hash");
            PlantHash.Of(new Vec3(1f, 2f, 3f), 0.5f, 0, "bush");
            long before = GC.GetAllocatedBytesForCurrentThread();
            uint sink = 0;
            for (int i = 0; i < 10000; i++)
                sink += PlantHash.Of(new Vec3(i, 2f, 3f), 0.5f, 3, "bush");
            long garbage = GC.GetAllocatedBytesForCurrentThread() - before;
            Program.Check(garbage < 4096 && sink != 1u, $"10000 hashes allocate nothing worth naming: {garbage} bytes");
        }

        private static readonly Func<int, bool> Alive = plant => plant >= 0;

        private static void Offering()
        {
            var ledger = new PlantLedger<(int, int), int>();
            var gone = new List<int>();
            ledger.BeginPass();
            Program.Check(ledger.Offer((1, 0), Wish(10), Alive, gone) && ledger.Offer((1, 1), Wish(11), Alive, gone) && ledger.PendingCount == 2 && gone.Count == 0, "two new places are wished");
            Program.Check(ledger.Sweep(long.MaxValue, gone) && gone.Count == 0, "a sweep with nothing standing takes nothing");
            int made = 0;
            while (ledger.TryTake(out (int, int) key, out PlantWish wish))
                ledger.Placed(key, 100 + made++, wish.Hash);
            Program.Check(made == 2 && ledger.Count == 2 && ledger.Made == 2 && ledger.PendingCount == 0, "both are made, once");

            ledger.BeginPass();
            bool again = ledger.Offer((1, 0), Wish(10), Alive, gone) | ledger.Offer((1, 1), Wish(11), Alive, gone);
            Program.Check(!again && ledger.PendingCount == 0 && gone.Count == 0, "the same wishes leave the plants where they are");
            Program.Check(ledger.Sweep(long.MaxValue, gone) && gone.Count == 0 && ledger.Count == 2, "and the sweep leaves them");

            ledger.BeginPass();
            Program.Check(ledger.Offer((1, 0), Wish(99), Alive, gone) && gone.Count == 1 && ledger.Count == 1 && ledger.Gone == 1 && ledger.PendingCount == 1, "a changed hash takes the plant away and wishes another");
            ledger.Offer((1, 1), Wish(11), Alive, gone);
            ledger.Sweep(long.MaxValue, gone);
            Program.Check(gone.Count == 1 && ledger.Count == 1, "the plant that is as wished stays");
            Program.Check(ledger.TryTake(out (int, int) k2, out PlantWish w2) && k2.Equals((1, 0)) && w2.Hash == 99, "the wish that was queued is the new one");
            ledger.Placed(k2, 200, w2.Hash);
            Program.Check(!ledger.TryTake(out _, out _), "nothing else is queued");
        }

        private static void Sweeping()
        {
            var ledger = new PlantLedger<(int, int), int>();
            var gone = new List<int>();
            ledger.BeginPass();
            for (int i = 0; i < 300; i++)
                ledger.Offer((i, 0), Wish((uint)i), Alive, gone);
            while (ledger.TryTake(out (int, int) key, out PlantWish wish))
                ledger.Placed(key, key.Item1, wish.Hash);
            Program.Check(ledger.Count == 300, "300 plants stand");

            ledger.BeginPass();
            for (int i = 0; i < 300; i += 2)
                ledger.Offer((i, 0), Wish((uint)i), Alive, gone);
            int calls = 0;
            // A deadline that has passed: each call goes through a block of 64 and stops.
            while (!ledger.Sweep(Stopwatch.GetTimestamp() - 1, gone))
                calls++;
            Program.Check(calls >= 2 && calls <= 5, "an expired deadline cuts the sweep into slices: " + calls + " cut(s)");
            Program.Check(gone.Count == 150 && ledger.Count == 150 && ledger.Gone == 150, "the 150 places not offered again lost their plants, and they are counted: " + gone.Count);
            bool odd = true;
            foreach (int plant in gone)
                odd &= plant % 2 == 1;
            Program.Check(odd, "and they are the odd ones");
            gone.Clear();
            Program.Check(ledger.Sweep(long.MaxValue, gone) && gone.Count == 0, "a second sweep finds nothing more");

            // A key offered while its plant stands, then changed and wished again, is not swept as well.
            ledger.BeginPass();
            ledger.Offer((0, 0), Wish(555), Alive, gone);
            Program.Check(ledger.Sweep(long.MaxValue, gone) && gone.Count == 1 + 149, "the changed one went once, the others were not offered: " + gone.Count);
        }

        private static void Edges()
        {
            var ledger = new PlantLedger<(int, int), int>();
            var gone = new List<int>();
            ledger.BeginPass();
            ledger.Offer((1, 0), Wish(1), Alive, gone);
            ledger.Offer((2, 0), Wish(2), Alive, gone);
            Program.Check(ledger.TryTake(out (int, int) first, out _) && first.Equals((2, 0)), "the newest wish is made first");
            ledger.Placed(first, 5, 2);
            Program.Check(ledger.TryTake(out (int, int) second, out _) && second.Equals((1, 0)), "then the older");
            ledger.Placed(second, -7, 1);

            // A plant that is not alive (the game took it away) is made again, and the dead one is handed over to be dealt with.
            ledger.BeginPass();
            Program.Check(ledger.Offer((1, 0), Wish(1), Alive, gone) && gone.Count == 1 && gone[0] == -7, "a plant that is gone is wished again");
            Program.Check(!ledger.Offer((2, 0), Wish(2), Alive, gone), "and one that is alive is not");
            ledger.Sweep(long.MaxValue, gone);

            // A place wished twice in a pass (a plan that lists it twice) is made once.
            Program.Check(ledger.Offer((3, 0), Wish(3), Alive, gone) && ledger.Offer((3, 0), Wish(3), Alive, gone), "a place is wished twice");
            int taken = 0;
            while (ledger.TryTake(out (int, int) key, out PlantWish wish))
            {
                ledger.Placed(key, 300 + taken++, wish.Hash);
            }

            Program.Check(taken == 2 && ledger.Count == 3, "each wish of the pass is made once, a place that is wished twice included: " + taken);

            ledger.Clear();
            Program.Check(ledger.Count == 0 && ledger.PendingCount == 0 && !ledger.TryTake(out _, out _), "clear forgets the plants and the wishes");
            ledger.BeginPass();
            Program.Check(ledger.Sweep(long.MaxValue, gone), "and a pass after it starts clean");
        }
    }
}
