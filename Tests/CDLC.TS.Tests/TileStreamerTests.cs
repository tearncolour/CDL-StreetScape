using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>Which tiles are built and at which tier (0.7.0): by distance, with a hysteresis, nearest first.</summary>
    internal static class TileStreamerTests
    {
        public static void RunAll()
        {
            Program.Run("tile streamer: the distance to a tile", Distance);
            Program.Run("tile streamer: the tier by distance and the hysteresis", Tiers);
            Program.Run("tile streamer: a camera moving about a limit does not rebuild at every step", NoFlicker);
            Program.Run("tile streamer: the plan builds the nearest first and drops what is out of reach", PlanOrder);
            Program.Run("tile streamer: a tile whose items changed is built again, and the plan can ask the store instead of keeping a set", StaleAndStore);
            Program.Run("tile streamer: the plan from the tiles round the camera is the plan from every tile", PlanAroundEqualsPlan);
            Program.Run("tile streamer: a map of 20000 tiles is planned in a fraction of a millisecond a thousand", Scale);
            Program.Run("tile streamer: the far limit is the player's (0.12.103): its range, the options it makes, the tiles it builds", FarLimit);
            Program.Run("tile streamer: a built tile is shown inside its reach, a share farther once shown", Reach);
        }

        private static void FarLimit()
        {
            // The range and the steps of the panel.
            Program.Check(StreamOptions.DefaultFarMetres == 700f && StreamOptions.LeastFarMetres == 300f && StreamOptions.MostFarMetres == 3000f, "the default 700 m, from 300 m to 3 km");
            Program.Check(StreamOptions.ClampFar(700f) == 700f && StreamOptions.ClampFar(1234f) == 1234f && StreamOptions.ClampFar(300f) == 300f && StreamOptions.ClampFar(3000f) == 3000f, "a number in the range is as it is, the ends too");
            Program.Check(StreamOptions.ClampFar(299.9f) == 300f && StreamOptions.ClampFar(-5f) == 300f && StreamOptions.ClampFar(0f) == 300f, "below the range: the least");
            Program.Check(StreamOptions.ClampFar(3000.1f) == 3000f && StreamOptions.ClampFar(1e9f) == 3000f, "above it: the most");
            Program.Check(StreamOptions.ClampFar(float.NaN) == 700f && StreamOptions.ClampFar(float.PositiveInfinity) == 700f && StreamOptions.ClampFar(float.NegativeInfinity) == 700f, "not a number: the default");
            float[] steps = StreamOptions.FarSteps;
            bool ordered = steps.Length >= 5 && Array.IndexOf(steps, 700f) >= 0;
            for (int i = 0; i < steps.Length; i++)
                ordered &= steps[i] >= StreamOptions.LeastFarMetres && steps[i] <= StreamOptions.MostFarMetres && (i == 0 || steps[i] > steps[i - 1]);
            Program.Check(ordered && steps[0] == 300f && steps[steps.Length - 1] == 3000f, "the steps rise from the least to the most and have the default");

            // The options: only the far limit (and the horizon, when it would be inside it) move.
            StreamOptions d = StreamOptions.Default, same = StreamOptions.WithFar(700f);
            Program.Check(same.NearMetres == d.NearMetres && same.MidMetres == d.MidMetres && same.FarMetres == d.FarMetres && same.HorizonMetres == d.HorizonMetres && same.Hysteresis == d.Hysteresis, "the default far limit makes the default options");
            StreamOptions twoKm = StreamOptions.WithFar(2000f);
            Program.Check(twoKm.FarMetres == 2000f && twoKm.NearMetres == d.NearMetres && twoKm.MidMetres == d.MidMetres && twoKm.HorizonMetres == 3000f && twoKm.Hysteresis == d.Hysteresis, "2 km: the near and middle limits, the horizon and the hysteresis stay");
            bool horizonBeyond = true;
            foreach (float step in StreamOptions.FarSteps)
                horizonBeyond &= StreamOptions.WithFar(step).HorizonMetres >= StreamOptions.WithFar(step).FarMetres;
            Program.Check(horizonBeyond && StreamOptions.WithFar(3000f).HorizonMetres == 3000f && StreamOptions.WithFar(9999f).FarMetres == 3000f && StreamOptions.WithFar(9999f).HorizonMetres == 3000f, "3 km: the horizon is not inside the far limit at any step; a larger one is the most");
            Program.Check(StreamOptions.WithFar(100f).FarMetres == 300f && StreamOptions.WithFar(100f).MidMetres < 300f, "a short one is 300 m, beyond the middle limit");
            Program.Check(StreamOptions.WithFar(float.NaN).FarMetres == 700f, "not a number: the default options");

            // The tiles: a tile 1500 m away is built far with a far limit of 2 km, and is not at the default; the hysteresis is the share of the limit.
            StreamOptions normal = StreamOptions.Default, far = StreamOptions.WithFar(2000f);
            Program.Check(TileStreamer.Wanted(1500f, TileTier.None, normal) == TileTier.None && TileStreamer.Wanted(1500f, TileTier.None, far) == TileTier.Far, "1500 m: nothing at 700 m, far at 2 km");
            Program.Check(TileStreamer.Wanted(1999f, TileTier.None, far) == TileTier.Far && TileStreamer.Wanted(2001f, TileTier.None, far) == TileTier.None, "the far limit is 2 km");
            Program.Check(TileStreamer.Wanted(2000f * (1f + far.Hysteresis) - 1f, TileTier.Far, far) == TileTier.Far && TileStreamer.Wanted(2000f * (1f + far.Hysteresis) + 1f, TileTier.Far, far) == TileTier.None, "a far tile keeps its tier 15 % past 2 km");
            Program.Check(TileStreamer.Wanted(500f, TileTier.None, far) == TileTier.Far && TileStreamer.Wanted(200f, TileTier.None, far) == TileTier.Mid && TileStreamer.Wanted(50f, TileTier.None, far) == TileTier.Near, "the near and middle tiers are where they were");
            // A wall's tile past the far limit and inside the horizon is the horizon tier, at 2 km as at the default.
            Program.Check(TileStreamer.Wanted(2500f, TileTier.None, far, true) == TileTier.Horizon, "a wall's tile between the far limit and the horizon is the horizon tier");
            Program.Check(TileStreamer.Wanted(1500f, TileTier.None, far, true) == TileTier.Far, "a wall's tile inside the far limit is far, as the others");

            // PlanAround: the window of tiles round the camera is the far limit's.
            var origin = new TileKey(0, 0);
            float camX = Tiles.Size * 0.5f, camZ = Tiles.Size * 0.5f;
            TileKey farTile = new TileKey((int)Math.Floor((camX + 1500f) / Tiles.Size), 0);
            var have = new Dictionary<TileKey, TileTier>();
            Func<TileKey, TileTier> current = k => have.TryGetValue(k, out TileTier t) ? t : TileTier.None;
            foreach ((StreamOptions options, bool expectBuilt, string name) in new[] { (normal, false, "700 m"), (far, true, "2 km") })
            {
                var build = new List<TileWish>();
                var drop = new List<TileKey>();
                TileStreamer.PlanAround(k => k.Equals(farTile) || k.Equals(origin), have.Keys, current, camX, camZ, options, build, drop);
                bool built = build.Exists(w => w.Key.Equals(farTile));
                Program.Check(built == expectBuilt, $"with a far limit of {name} the tile 1500 m away is {(expectBuilt ? "" : "not ")}planned: {built}");
                Program.Check(build.Exists(w => w.Key.Equals(origin)), $"the tile under the camera is planned with {name}");
            }

            // The tile built at 2 km is dropped when the player brings the limit back to 700 m, and kept at 1 km it was within.
            have[farTile] = TileTier.Far;
            var toBuild = new List<TileWish>();
            var toDrop = new List<TileKey>();
            TileStreamer.PlanAround(k => k.Equals(farTile), have.Keys, current, camX, camZ, normal, toBuild, toDrop);
            Program.Check(toDrop.Contains(farTile), "a far tile 1500 m away is dropped when the limit goes back to 700 m");
            toDrop.Clear();
            TileStreamer.PlanAround(k => k.Equals(farTile), have.Keys, current, camX, camZ, far, toBuild, toDrop);
            Program.Check(!toDrop.Contains(farTile), "and kept at 2 km");
        }

        private static void Reach()
        {
            float hysteresis = 0.1f;
            Program.Check(TileStreamer.InReach(699f, 700f, true, hysteresis) && TileStreamer.InReach(769f, 700f, true, hysteresis) && !TileStreamer.InReach(771f, 700f, true, hysteresis), "a tile that is shown stays up to 10 % past its reach");
            Program.Check(TileStreamer.InReach(629f, 700f, false, hysteresis) && !TileStreamer.InReach(631f, 700f, false, hysteresis), "one that is not shown comes in 10 % inside it");
            Program.Check(!TileStreamer.InReach(700f, 700f, false, 0f) && !TileStreamer.InReach(700f, 700f, true, 0f) && TileStreamer.InReach(699.9f, 700f, false, 0f), "with no hysteresis the reach is the limit and the limit itself is out");
            Program.Check(TileStreamer.InReach(1500f, 2000f, false, hysteresis) && !TileStreamer.InReach(1500f, 700f, true, hysteresis), "the reach is the one given: 2 km shows what 700 m hides");
            Program.Check(TileStreamer.InReach(0f, 300f, false, hysteresis), "under the camera: shown");
        }

        private static void Distance()
        {
            var tile = new TileKey(2, -3);
            float x0 = 2 * Tiles.Size, z0 = -3 * Tiles.Size;
            Program.Near(TileStreamer.DistanceToTile(tile, x0 + 10f, z0 + 10f), 0f, 1e-4f, "inside: 0");
            Program.Near(TileStreamer.DistanceToTile(tile, x0, z0), 0f, 1e-4f, "on the corner: 0");
            Program.Near(TileStreamer.DistanceToTile(tile, x0 - 30f, z0 + 50f), 30f, 1e-3f, "left of it: the distance to the edge");
            Program.Near(TileStreamer.DistanceToTile(tile, x0 + Tiles.Size + 40f, z0 + 50f), 40f, 1e-3f, "right of it");
            Program.Near(TileStreamer.DistanceToTile(tile, x0 + 10f, z0 + Tiles.Size + 70f), 70f, 1e-3f, "above it");
            Program.Near(TileStreamer.DistanceToTile(tile, x0 - 30f, z0 - 40f), 50f, 1e-3f, "off a corner: the distance to the corner (3-4-5)");
            Program.Near(TileStreamer.DistanceToTile(tile, x0 + Tiles.Size + 30f, z0 + Tiles.Size + 40f), 50f, 1e-3f, "off the other corner");
        }

        private static void Tiers()
        {
            StreamOptions o = StreamOptions.Default;
            Program.Check(o.NearMetres < o.MidMetres && o.MidMetres < o.FarMetres && o.Hysteresis > 0f && o.Hysteresis < 0.5f, "the defaults are ordered");
            Program.Check(o.NearMetres == 70f && o.MidMetres == 250f && o.FarMetres == 700f && o.Hysteresis == 0.15f, "the defaults are 70 m, 250 m and 700 m with 15 per cent: " + o.NearMetres + "/" + o.MidMetres + "/" + o.FarMetres + "/" + o.Hysteresis);

            Program.Check(TileStreamer.Wanted(0f, TileTier.None, o) == TileTier.Near, "at 0 m a new tile is near");
            Program.Check(TileStreamer.Wanted(o.NearMetres - 1f, TileTier.None, o) == TileTier.Near, "just inside the near limit");
            Program.Check(TileStreamer.Wanted(o.NearMetres + 1f, TileTier.None, o) == TileTier.Mid, "just outside it: middle");
            Program.Check(TileStreamer.Wanted(o.MidMetres + 1f, TileTier.None, o) == TileTier.Far, "just outside the middle limit: far");
            Program.Check(TileStreamer.Wanted(o.FarMetres + 1f, TileTier.None, o) == TileTier.None, "beyond the far limit nothing");
            Program.Check(TileStreamer.Wanted(o.FarMetres, TileTier.None, o) == TileTier.None, "the limit itself is outside");

            // A tile that has a tier keeps it a little beyond its limit, and only a little.
            float within = o.NearMetres * (1f + o.Hysteresis) - 1f, beyond = o.NearMetres * (1f + o.Hysteresis) + 1f;
            Program.Check(TileStreamer.Wanted(within, TileTier.Near, o) == TileTier.Near, "a near tile stays near inside the hysteresis");
            Program.Check(TileStreamer.Wanted(beyond, TileTier.Near, o) == TileTier.Mid, "and goes to middle beyond it");
            Program.Check(TileStreamer.Wanted(within, TileTier.Mid, o) == TileTier.Mid, "a middle tile does not become near there");
            Program.Check(TileStreamer.Wanted(o.FarMetres * (1f + o.Hysteresis) - 1f, TileTier.Far, o) == TileTier.Far && TileStreamer.Wanted(o.FarMetres * (1f + o.Hysteresis) + 1f, TileTier.Far, o) == TileTier.None, "a far tile is dropped beyond the far limit and its hysteresis");
            Program.Check(TileStreamer.Wanted(o.FarMetres + 1f, TileTier.Far, o) == TileTier.Far, "a far tile just past the limit is kept");
            Program.Check(TileStreamer.Wanted(o.FarMetres + 1f, TileTier.None, o) == TileTier.None, "one that is not built is not built there");

            // Monotone: at a greater distance a tile never has more than at a smaller one (same current tier).
            foreach (TileTier current in new[] { TileTier.None, TileTier.Far, TileTier.Mid, TileTier.Near })
            {
                TileTier last = TileTier.Near;
                for (float d = 0f; d < 1500f; d += 3f)
                {
                    TileTier tier = TileStreamer.Wanted(d, current, o);
                    Program.Check(tier <= last, $"with {current}: at {d} m {tier} is not more than at the distance before ({last})");
                    last = tier;
                }
            }
        }

        private static void NoFlicker()
        {
            // A camera that goes back and forth over the near limit by a few metres: the tile changes once, not at every step.
            StreamOptions o = StreamOptions.Default;
            foreach (float limit in new[] { o.NearMetres, o.MidMetres, o.FarMetres })
            {
                TileTier tier = TileTier.None;
                int changes = 0;
                for (int step = 0; step < 400; step++)
                {
                    float distance = limit + (float)Math.Sin(step * 0.3) * 6f;
                    TileTier next = TileStreamer.Wanted(distance, tier, o);
                    if (next != tier)
                        changes++;
                    tier = next;
                }

                Program.Check(changes <= 2, $"going back and forth by 6 m about {limit} m changes the tile {changes} time(s), at most the first build and one more");
            }

            // Without the hysteresis the same walk changes it all the time (the test sees the difference).
            StreamOptions none = o;
            none.Hysteresis = 0f;
            TileTier plain = TileTier.None;
            int plainChanges = 0;
            for (int step = 0; step < 400; step++)
            {
                TileTier next = TileStreamer.Wanted(o.NearMetres + (float)Math.Sin(step * 0.3) * 6f, plain, none);
                if (next != plain)
                    plainChanges++;
                plain = next;
            }

            Program.Check(plainChanges > 20, "with no hysteresis the same walk rebuilds the tile " + plainChanges + " times");
        }

        private static void PlanOrder()
        {
            StreamOptions o = StreamOptions.Default;
            var built = new Dictionary<TileKey, TileTier>();
            var withItems = new List<TileKey>();
            // Tiles in a row east of the camera, 0 to 9 tiles away, and one far away.
            for (int i = 0; i < 10; i++)
                withItems.Add(new TileKey(i, 0));
            withItems.Add(new TileKey(100, 100));
            float camX = Tiles.Size * 0.5f, camZ = Tiles.Size * 0.5f;

            var toBuild = new List<TileWish>();
            var toDrop = new List<TileKey>();
            TileStreamer.Plan(withItems, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, camX, camZ, o, toBuild, toDrop);
            Program.Check(toDrop.Count == 0, "nothing to drop when nothing is built");
            Program.Check(!toBuild.Exists(w => w.Key.X == 100), "a tile far away is not built");
            for (int i = 1; i < toBuild.Count; i++)
                Program.Check(toBuild[i - 1].Distance <= toBuild[i].Distance, "nearest first: " + toBuild[i - 1].Distance + " then " + toBuild[i].Distance);
            Program.Check(toBuild.Count > 0 && toBuild[0].Key.X == 0 && toBuild[0].Tier == TileTier.Near, "the tile under the camera is first and near");
            Program.Check(toBuild[toBuild.Count - 1].Tier == TileTier.Far || toBuild[toBuild.Count - 1].Tier == TileTier.Mid, "the farthest of them has a lower tier");

            // Once they are built as planned, there is nothing more to do.
            foreach (TileWish wish in toBuild)
                built[wish.Key] = wish.Tier;
            toBuild.Clear();
            TileStreamer.Plan(withItems, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, camX, camZ, o, toBuild, toDrop);
            Program.Check(toBuild.Count == 0 && toDrop.Count == 0, "a plan that was carried out leaves nothing to do");

            // The camera moves a long way: the tiles that were built are dropped, new ones are built, and the old tiles are not rebuilt.
            camX = Tiles.Size * 100.5f;
            camZ = Tiles.Size * 100.5f;
            TileStreamer.Plan(withItems, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, camX, camZ, o, toBuild, toDrop);
            Program.Check(toDrop.Count == built.Count, "all the tiles that were built are far now: " + toDrop.Count + " of " + built.Count);
            Program.Check(toBuild.Count == 1 && toBuild[0].Key.X == 100 && toBuild[0].Tier == TileTier.Near, "the one at the camera is built, near");

            // A built tile whose items are gone is dropped too.
            built.Clear();
            built[new TileKey(50, 50)] = TileTier.Mid;
            toBuild.Clear();
            toDrop.Clear();
            TileStreamer.Plan(new List<TileKey>(), built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, 0f, 0f, o, toBuild, toDrop);
            Program.Check(toDrop.Count == 1 && toDrop[0].X == 50, "a built tile with no items is dropped");

            // A tier change of a built tile is a build (the tile is made again with its new detail).
            built.Clear();
            built[new TileKey(0, 0)] = TileTier.Far;
            toBuild.Clear();
            toDrop.Clear();
            TileStreamer.Plan(new[] { new TileKey(0, 0) }, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, 10f, 10f, o, toBuild, toDrop);
            Program.Check(toBuild.Count == 1 && toBuild[0].Tier == TileTier.Near && toDrop.Count == 0, "a far tile the camera comes to is built again as near");
        }

        private static void StaleAndStore()
        {
            StreamOptions o = StreamOptions.Default;
            var built = new Dictionary<TileKey, TileTier> { { new TileKey(0, 0), TileTier.Near }, { new TileKey(3, 0), TileTier.Far } };
            var withItems = new List<TileKey> { new TileKey(0, 0), new TileKey(3, 0) };
            var stale = new HashSet<TileKey> { new TileKey(0, 0) };
            var toBuild = new List<TileWish>();
            var toDrop = new List<TileKey>();
            float x = Tiles.Size * 0.5f, z = Tiles.Size * 0.5f;
            TileStreamer.Plan(withItems, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, x, z, o, toBuild, toDrop, null, stale.Contains);
            Program.Check(toBuild.Count == 1 && toBuild[0].Key.X == 0 && toBuild[0].Tier == TileTier.Near && toDrop.Count == 0, "a built tile that is stale is built again at its tier; the one that is not stale is left");

            // A stale tile that is too far to be built is dropped, not built.
            stale.Add(new TileKey(3, 0));
            toBuild.Clear();
            TileStreamer.Plan(withItems, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, x + Tiles.Size * 40f, z, o, toBuild, toDrop, null, stale.Contains);
            Program.Check(toBuild.Count == 0 && toDrop.Count == 2, "stale tiles that are out of reach are dropped: " + toBuild.Count + " built, " + toDrop.Count + " dropped");

            // A tile that is stale but not built, and too far to be, is neither built nor dropped (there is nothing to drop).
            var farOnly = new List<TileKey> { new TileKey(90, 90) };
            var noneBuilt = new Dictionary<TileKey, TileTier>();
            toBuild.Clear();
            toDrop.Clear();
            TileStreamer.Plan(farOnly, noneBuilt.Keys, k => TileTier.None, x, z, o, toBuild, toDrop, null, k => true);
            Program.Check(toBuild.Count == 0 && toDrop.Count == 0, "an unbuilt far tile that is stale does nothing: " + toBuild.Count + " built, " + toDrop.Count + " dropped");

            // Tiles in reach that are not built are built, as ever.
            built.Clear();
            toBuild.Clear();
            toDrop.Clear();
            TileStreamer.Plan(withItems, built.Keys, k => TileTier.None, x, z, o, toBuild, toDrop, null, k => true);
            Program.Check(toBuild.Count >= 1 && toDrop.Count == 0, "tiles in reach are built");

            // The store's answer instead of a set: a built tile with no items is dropped.
            built.Clear();
            built[new TileKey(50, 50)] = TileTier.Far;
            toBuild.Clear();
            toDrop.Clear();
            var have = new HashSet<TileKey> { new TileKey(0, 0) };
            TileStreamer.Plan(have, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, x, z, o, toBuild, toDrop, have.Contains, null);
            Program.Check(toDrop.Count == 1 && toDrop[0].X == 50 && toBuild.Count == 1, "a built tile the store has no items for is dropped, with no set kept: " + toDrop.Count + " dropped, " + toBuild.Count + " built");
        }

        private static string Describe(List<TileWish> build, List<TileKey> drop)
        {
            var lines = new List<string>();
            foreach (TileWish w in build)
                lines.Add($"B {w.Key} {w.Tier} {w.Distance:0.0}");
            foreach (TileKey k in drop)
                lines.Add("D " + k);
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        private static void PlanAroundEqualsPlan()
        {
            var rng = new Random(77);
            StreamOptions o = StreamOptions.Default;
            int scenes = 0, builds = 0, drops = 0;
            for (int scene = 0; scene < 300; scene++)
            {
                // Tiles with items over a map of 120 x 120 tiles, a random set of them built at random tiers (some of them with no items now, some far away), some stale.
                var withItems = new HashSet<TileKey>();
                int count = rng.Next(5, 900);
                for (int i = 0; i < count; i++)
                    withItems.Add(new TileKey(rng.Next(-60, 60), rng.Next(-60, 60)));
                var built = new Dictionary<TileKey, TileTier>();
                var stale = new HashSet<TileKey>();
                foreach (TileKey key in withItems)
                {
                    int roll = rng.Next(10);
                    if (roll < 3)
                        built[key] = (TileTier)rng.Next(1, 4);
                    if (roll == 0)
                        stale.Add(key);
                }

                for (int i = 0; i < 20; i++)
                    built[new TileKey(rng.Next(-70, 70), rng.Next(-70, 70))] = (TileTier)rng.Next(1, 4);

                float x = (float)(rng.NextDouble() * 22000 - 11000), z = (float)(rng.NextDouble() * 22000 - 11000);
                if (scene % 3 == 0)
                {
                    // Close to where tiles are built, so that the plans have something to say.
                    TileKey near = new List<TileKey>(withItems)[rng.Next(withItems.Count)];
                    x = (near.X + (float)rng.NextDouble()) * Tiles.Size;
                    z = (near.Z + (float)rng.NextDouble()) * Tiles.Size;
                }

                Func<TileKey, TileTier> current = k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None;
                var buildA = new List<TileWish>();
                var dropA = new List<TileKey>();
                TileStreamer.Plan(withItems, built.Keys, current, x, z, o, buildA, dropA, withItems.Contains, stale.Contains);
                var buildB = new List<TileWish>();
                var dropB = new List<TileKey>();
                TileStreamer.PlanAround(withItems.Contains, built.Keys, current, x, z, o, buildB, dropB, stale.Contains);
                scenes++;
                builds += buildA.Count;
                drops += dropA.Count;
                if (Describe(buildA, dropA) != Describe(buildB, dropB))
                {
                    Program.Check(false, $"scene {scene} at {x:0}, {z:0}: the plan round the camera is not the plan of every tile ({buildA.Count}/{dropA.Count} against {buildB.Count}/{dropB.Count})");
                    break;
                }

                for (int i = 1; i < buildB.Count; i++)
                {
                    if (buildB[i - 1].Distance > buildB[i].Distance)
                    {
                        Program.Check(false, "the plan round the camera is not nearest first");
                        break;
                    }
                }
            }

            Program.Check(scenes == 300 && builds > 200 && drops > 200, "the scenes have builds and drops to compare: " + builds + " builds, " + drops + " drops");
        }

        private static void Scale()
        {
            var tiles = new List<TileKey>();
            var rng = new Random(3);
            var built = new Dictionary<TileKey, TileTier>();
            for (int i = 0; i < 20000; i++)
                tiles.Add(new TileKey(rng.Next(-60, 60), rng.Next(-60, 60)));
            var distinct = new HashSet<TileKey>(tiles);
            var toBuild = new List<TileWish>();
            var toDrop = new List<TileKey>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            const int plans = 20;
            for (int i = 0; i < plans; i++)
            {
                toBuild.Clear();
                toDrop.Clear();
                TileStreamer.Plan(distinct, built.Keys, k => built.TryGetValue(k, out TileTier t) ? t : TileTier.None, (float)(rng.NextDouble() * 4000), (float)(rng.NextDouble() * 4000), StreamOptions.Default, toBuild, toDrop);
            }

            double perPlan = clock.Elapsed.TotalMilliseconds / plans;
            Console.WriteLine($"  tile streamer: a plan over {distinct.Count} tiles takes {perPlan:0.00} ms");
            Program.Check(perPlan < 10.0, "a plan over every tile of a big map is cheap: " + perPlan.ToString("0.00") + " ms");
            Program.Check(toBuild.Count < distinct.Count / 3, "and builds only the tiles near the camera: " + toBuild.Count + " of " + distinct.Count);
        }
    }
}
