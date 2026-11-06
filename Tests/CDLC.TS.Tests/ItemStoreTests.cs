using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The store of the placed items with its index by tile (0.7.0): changes one item at a time, knows which tiles changed and at which version, answers questions about a place or a line by the tiles they touch.</summary>
    internal static class ItemStoreTests
    {
        public static void RunAll()
        {
            Program.Run("item store: add, change, move, remove and what is marked", Changes);
            Program.Run("item store: a full read in slices keeps what it touched and what was added meanwhile", Pass);
            Program.Run("item store: snapshots and versions", Snapshots);
            Program.Run("item store: the items near a place are those a brute force finds", Near);
            Program.Run("item store: the items near a line include every item the line is near", NearLine);
            Program.Run("item store: the members of a group and the dictionary view", GroupsAndView);
            Program.Run("item store: markers made tile by tile are the markers made all at once", MarkersByTile);
        }

        private static ItemRecord At(float x, float z, uint type = 1u, string spec = null, uint group = 0u, int slot = 0) =>
            new ItemRecord(type, new Vec3(x, 0f, z), 0f, 0u, spec, 0f, group, slot);

        private static TileKey TileOf(float x, float z) => Tiles.Of(x, z);

        private static HashSet<TileKey> Take(ItemStore<int> store)
        {
            var set = new HashSet<TileKey>();
            store.TakeDirty(set);
            return set;
        }

        private static void Changes()
        {
            var store = new ItemStore<int>();
            Program.Check(store.Count == 0 && store.TileCount == 0 && !store.HasDirty, "empty");

            Program.Check(store.Upsert(1, At(10f, 10f)), "a new item is a change");
            Program.Check(store.Count == 1 && store.TileCount == 1 && store.HasItems(TileOf(10f, 10f)) && store.HasDirty, "it is there, its tile has it, the tile is dirty");
            HashSet<TileKey> dirty = Take(store);
            Program.Check(dirty.Count == 1 && dirty.Contains(TileOf(10f, 10f)) && !store.HasDirty, "TakeDirty gives the tile once and forgets it");

            int version = store.VersionOf(TileOf(10f, 10f));
            Program.Check(!store.Upsert(1, At(10f, 10f)) && !store.HasDirty && store.VersionOf(TileOf(10f, 10f)) == version, "the same record again changes nothing: no tile is marked, the version stays");

            Program.Check(store.Upsert(1, At(10f, 10f, 1u, "a=1")) && store.HasDirty && store.VersionOf(TileOf(10f, 10f)) > version, "another spec is a change: tile dirty, version up");
            Take(store);
            int before = store.Changes;
            Program.Check(store.Upsert(1, At(10f, 10f, 1u, "a=1")) == false && store.Changes == before, "and again the same");

            // A move to another tile marks both, and the old one has no items.
            float far = Tiles.Size * 5f + 3f;
            Program.Check(store.Upsert(1, At(far, 10f, 1u, "a=1")), "a move is a change");
            dirty = Take(store);
            Program.Check(dirty.Count == 2 && dirty.Contains(TileOf(10f, 10f)) && dirty.Contains(TileOf(far, 10f)), "both tiles are marked");
            Program.Check(!store.HasItems(TileOf(10f, 10f)) && store.HasItems(TileOf(far, 10f)) && store.TileCount == 1 && store.Count == 1, "the old tile is empty and gone, the new one has the item");

            // A change of the position inside a tile marks the tile once.
            Program.Check(store.Upsert(1, At(far + 1f, 10f, 1u, "a=1")), "a small move");
            Program.Check(Take(store).Count == 1, "inside the tile: one tile");

            // Removing marks the tile; an empty tile is not listed.
            Program.Check(store.Remove(1) && !store.Remove(1), "remove says whether it was there");
            dirty = Take(store);
            Program.Check(dirty.Contains(TileOf(far, 10f)) && store.Count == 0 && store.TileCount == 0 && new List<TileKey>(store.TileKeys).Count == 0, "the tile that lost its last item is marked, and no tile is listed");

            // Several items, several tiles.
            store.Upsert(1, At(0f, 0f));
            store.Upsert(2, At(1f, 1f));
            store.Upsert(3, At(500f, 0f));
            Program.Check(store.Count == 3 && store.TileCount == 2 && store.ItemCount(TileOf(0f, 0f)) == 2, "three items in two tiles");
            store.Clear();
            Program.Check(store.Count == 0 && store.TileCount == 0, "clear");
        }

        private static void Pass()
        {
            var store = new ItemStore<int>();
            for (int i = 0; i < 10; i++)
                store.Upsert(i, At(i * 7f, 0f));
            Take(store);

            // A pass that reads 0..5 and not 6..9 ends with 6..9 gone; an item added while it runs (10) is kept although the pass never read it.
            store.BeginPass();
            for (int i = 0; i < 6; i++)
                store.Touch(i, At(i * 7f, 0f));
            store.Upsert(10, At(500f, 500f));
            var removedKeys = new List<int>();
            int gone = store.EndPass(removedKeys);
            removedKeys.Sort();
            Program.Check(gone == 4 && removedKeys.Count == 4 && removedKeys[0] == 6 && removedKeys[3] == 9, "four items were not read, and the pass says which: " + gone + " / " + string.Join(",", removedKeys));
            Program.Check(store.Count == 7 && store.ContainsKey(0) && store.ContainsKey(5) && !store.ContainsKey(6) && !store.ContainsKey(9) && store.ContainsKey(10), "0..5 and the one added meanwhile are kept");
            Program.Check(Take(store).Contains(TileOf(0f, 0f)), "the tiles of the removed items are marked");

            // A pass that touches everything removes nothing, and marks nothing when nothing changed.
            Take(store);
            store.BeginPass();
            foreach (int key in new List<int>(store.Keys))
                store.Touch(key, store[key]);
            Program.Check(store.EndPass() == 0 && !store.HasDirty, "nothing was lost and nothing changed");

            // A pass that reads an item with other settings changes it.
            store.BeginPass();
            foreach (int key in new List<int>(store.Keys))
                store.Touch(key, key == 3 ? At(21f, 0f, 1u, "x=1") : store[key]);
            Program.Check(store.EndPass() == 0 && store.HasDirty && store[3].Spec == "x=1", "a changed item in a pass is a change");

            // Two passes in a row: the second removes what the first kept and the second did not read.
            int beforePass = store.Count;
            store.BeginPass();
            store.Touch(0, store[0]);
            int goneAgain = store.EndPass();
            Program.Check(goneAgain == beforePass - 1 && store.Count == 1 && store.ContainsKey(0), "a pass that reads one item leaves that one: " + store.Count + ", " + goneAgain + " gone");
        }

        private static void Snapshots()
        {
            var store = new ItemStore<int>();
            store.Upsert(1, At(1f, 1f, 7u));
            store.Upsert(2, At(2f, 2f, 8u));
            store.Upsert(3, At(900f, 900f, 9u));
            var copy = new List<ItemRecord>();
            int version = store.Snapshot(TileOf(1f, 1f), copy);
            Program.Check(copy.Count == 2 && version == store.VersionOf(TileOf(1f, 1f)), "the tile's two records and its version");

            store.Upsert(2, At(2f, 2f, 99u));
            Program.Check(copy.Count == 2 && copy[0].TypeHash != 99u && copy[1].TypeHash != 99u, "the copy does not follow the store");
            Program.Check(store.VersionOf(TileOf(1f, 1f)) != version, "the version moved on: a job that read the old one is out of date");

            // An empty tile: version continues where it was, an old job cannot match it.
            TileKey far = TileOf(900f, 900f);
            int farVersion = store.VersionOf(far);
            store.Remove(3);
            int emptied = store.VersionOf(far);
            Program.Check(emptied != farVersion, "removing an item changes the version of its tile");
            store.Upsert(4, At(901f, 901f));
            Program.Check(store.VersionOf(far) > emptied && store.VersionOf(far) > farVersion, "an item put there again goes on from the old version, never back to one that was seen: " + farVersion + " -> " + emptied + " -> " + store.VersionOf(far));
            Program.Check(store.VersionOf(new TileKey(1234, 5678)) == 0, "a tile that never had items is version 0");

            var none = new List<ItemRecord>();
            store.Snapshot(new TileKey(1234, 5678), none);
            Program.Check(none.Count == 0, "no records for a tile without items");
        }

        private static void Near()
        {
            var rng = new Random(11);
            var store = new ItemStore<int>();
            var all = new Dictionary<int, ItemRecord>();
            for (int i = 0; i < 3000; i++)
            {
                // Clustered, so that tiles have many items and some have none; also negative coordinates.
                float cx = (float)(rng.Next(8) * 700 - 2500), cz = (float)(rng.Next(8) * 650 - 2300);
                ItemRecord record = At(cx + (float)(rng.NextDouble() * 300 - 150), cz + (float)(rng.NextDouble() * 300 - 150));
                store.Upsert(i, record);
                all[i] = record;
            }

            int wrong = 0, queries = 0, found = 0;
            var result = new List<KeyValuePair<int, ItemRecord>>();
            foreach (float radius in new[] { 5f, 40f, 192f, 400f, 1500f, 20000f })
            {
                for (int q = 0; q < 120; q++)
                {
                    float x = (float)(rng.NextDouble() * 6000 - 3000), z = (float)(rng.NextDouble() * 6000 - 3000);
                    result.Clear();
                    store.ItemsNear(x, z, radius, result);
                    var got = new HashSet<int>();
                    foreach (KeyValuePair<int, ItemRecord> pair in result)
                        got.Add(pair.Key);

                    int expected = 0;
                    foreach (KeyValuePair<int, ItemRecord> pair in all)
                    {
                        float dx = pair.Value.Position.X - x, dz = pair.Value.Position.Z - z;
                        bool near = Math.Sqrt(dx * dx + dz * dz) <= radius;
                        if (near)
                            expected++;
                        if (near != got.Contains(pair.Key))
                            wrong++;
                    }

                    queries++;
                    found += expected;
                    if (got.Count != result.Count)
                        wrong++;
                }
            }

            Program.Check(wrong == 0, wrong + " item(s) differ from the brute force over " + queries + " queries");
            Program.Check(found > 2000, "the queries found something: " + found);
        }

        private static void NearLine()
        {
            var rng = new Random(5);
            var store = new ItemStore<int>();
            var all = new Dictionary<int, ItemRecord>();
            for (int i = 0; i < 4000; i++)
            {
                var record = new ItemRecord(1u, new Vec3((float)(rng.NextDouble() * 8000 - 4000), (float)(rng.NextDouble() * 30), (float)(rng.NextDouble() * 8000 - 4000)), 0f);
                store.Upsert(i, record);
                all[i] = record;
            }

            int missed = 0, lines = 0, near = 0, offered = 0;
            var result = new List<KeyValuePair<int, ItemRecord>>();
            for (int q = 0; q < 300; q++)
            {
                // A ray from a camera high above to the ground, steep and flat ones, short and 8 km long.
                var from = new Vec3((float)(rng.NextDouble() * 6000 - 3000), (float)(rng.NextDouble() * 400 + 2), (float)(rng.NextDouble() * 6000 - 3000));
                float length = q % 3 == 0 ? 30f : q % 3 == 1 ? 800f : 8000f;
                double a = rng.NextDouble() * Math.PI * 2, tilt = rng.NextDouble() * 1.4 + 0.05;
                var to = new Vec3(from.X + (float)(Math.Cos(a) * Math.Cos(tilt) * length), Math.Max(0f, from.Y - (float)(Math.Sin(tilt) * length)), from.Z + (float)(Math.Sin(a) * Math.Cos(tilt) * length));
                const float radius = 25f;
                result.Clear();
                store.ItemsNearSegment(from, to, radius, result);
                var got = new HashSet<int>();
                foreach (KeyValuePair<int, ItemRecord> pair in result)
                    got.Add(pair.Key);
                offered += got.Count;
                foreach (KeyValuePair<int, ItemRecord> pair in all)
                {
                    if (Picking.DistanceToSegment(pair.Value.Position, from, to) > radius)
                        continue;
                    near++;
                    if (!got.Contains(pair.Key))
                        missed++;
                }

                lines++;
            }

            Program.Check(missed == 0, missed + " item(s) near a line were not offered, over " + lines + " lines (" + near + " near)");
            Program.Check(near > 20, "the lines pass near some items: " + near);
            Program.Check(offered < 300 * 4000 / 3, "and the lines are not just offered the whole map: " + offered + " offered of " + 300 * 4000);
        }

        private static void MarkersByTile()
        {
            var rng = new Random(31);
            var store = new ItemStore<int>();
            var all = new List<ItemRecord>();
            var defs = new List<SignDef>(SignCatalog.All);
            uint group = 100u;
            int key = 0;
            for (int i = 0; i < 600; i++)
            {
                var at = new Vec3((float)(rng.NextDouble() * 3000 - 1500), 0f, (float)(rng.NextDouble() * 3000 - 1500));
                float yaw = (float)(rng.NextDouble() * 6.28);
                if (rng.Next(4) == 0)
                {
                    // A support of two or three boards: they stand in one place.
                    group++;
                    int boards = rng.Next(2, 4);
                    for (int b = 0; b < boards; b++)
                    {
                        var record = new ItemRecord(defs[rng.Next(defs.Count)].Hash, at, yaw, 0u, null, 0f, group, b);
                        store.Upsert(key++, record);
                        all.Add(record);
                    }
                }
                else
                {
                    var record = new ItemRecord(defs[rng.Next(defs.Count)].Hash, at, yaw);
                    store.Upsert(key++, record);
                    all.Add(record);
                }
            }

            List<TsMarker> whole = TsMarkers.Build(all);
            var byTile = new List<TsMarker>();
            foreach (TileKey tile in new List<TileKey>(store.TileKeys))
            {
                var records = new List<ItemRecord>();
                store.Snapshot(tile, records);
                byTile.AddRange(TsMarkers.Build(records));
            }

            Program.Check(whole.Count == byTile.Count && whole.Count > 400, "the same number of markers: " + whole.Count + " / " + byTile.Count);
            int differ = 0, queries = 0;
            foreach (float radius in new[] { 50f, 400f, 1200f, 5000f })
            {
                foreach (int mask in new[] { 0, TsMarkers.MaskOf(TsMarkers.KindPost), TsMarkers.MaskOf(TsMarkers.KindGantry) | TsMarkers.MaskOf(TsMarkers.KindCantilever) })
                {
                    for (int q = 0; q < 25; q++)
                    {
                        float x = (float)(rng.NextDouble() * 3000 - 1500), z = (float)(rng.NextDouble() * 3000 - 1500);
                        string a = TsMarkers.Query(whole, 7, x, z, radius, mask, 64), b = TsMarkers.Query(byTile, 7, x, z, radius, mask, 64);
                        queries++;
                        if (a != b)
                            differ++;
                    }
                }
            }

            Program.Check(differ == 0, differ + " of " + queries + " queries answer differently from the tiles");
        }

        private static void GroupsAndView()
        {
            var store = new ItemStore<int>();
            store.Upsert(1, At(10f, 10f, 1u, null, 77u, 0));
            store.Upsert(2, At(10f, 10f, 2u, null, 77u, 1));
            store.Upsert(3, At(10f, 10f, 3u, null, 88u, 0));
            store.Upsert(4, At(10f, 10f, 4u));
            store.Upsert(5, At(4000f, 4000f, 5u, null, 77u, 2));
            var members = new List<KeyValuePair<int, ItemRecord>>();
            store.GroupMembers(77u, 10f, 10f, members);
            var keys = new List<int>();
            foreach (KeyValuePair<int, ItemRecord> pair in members)
                keys.Add(pair.Key);
            keys.Sort();
            Program.Check(keys.Count == 2 && keys[0] == 1 && keys[1] == 2, "the two members of group 77 in that tile (not the one far away with the same number, not another group)");
            members.Clear();
            store.GroupMembers(0u, 10f, 10f, members);
            Program.Check(members.Count == 0, "group 0 is no group");

            Program.Check(store.Count == 5 && store.TryGetValue(3, out ItemRecord three) && three.TypeHash == 3u && !store.TryGetValue(99, out _) && store[4].TypeHash == 4u, "the dictionary view: count, lookup, indexer");
            int enumerated = 0, values = 0, keyCount = 0;
            foreach (KeyValuePair<int, ItemRecord> pair in store)
                enumerated++;
            foreach (ItemRecord record in store.Values)
                values++;
            foreach (int key in store.Keys)
                keyCount++;
            Program.Check(enumerated == 5 && values == 5 && keyCount == 5, "enumerations: " + enumerated + "/" + values + "/" + keyCount);
        }
    }
}
