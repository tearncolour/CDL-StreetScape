using System;
using System.Collections;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Every placed item as a record, with an index by map tile: what the renderer and the tool ask the items. Until 0.7.0 the renderer read all the items again every 2.5 s into a new
    /// dictionary and the tool walked all of them every frame (split into singles and groups, and measured for the spacing rule): fine for a few hundred signs, a cost that grew with the
    /// number of signs. Here an item is added, changed or removed on its own (Upsert, Remove), a tile knows its own items, and a question about a place or a line (ItemsNear, ItemsNearSegment) only
    /// looks at the tiles it touches. A full read of the items is still possible, in slices, as a pass (BeginPass, Touch, EndPass: what was not touched is gone). Pure, so that it is tested offline
    /// and measured on Mono (Tests/Bench). Main thread (a job on a worker gets a Snapshot, not the store).
    /// </summary>
    public sealed class ItemStore<TKey> : IReadOnlyDictionary<TKey, ItemRecord> where TKey : IEquatable<TKey>
    {
        private struct Entry
        {
            public ItemRecord Record;
            public TileKey Tile;
            public int Pass;
        }

        private sealed class Bucket
        {
            public readonly HashSet<TKey> Keys = new HashSet<TKey>();

            /// <summary>How many of the tile's items are seen from far (LongReach): the tile has the horizon tier only while there is one.</summary>
            public int LongCount;

            /// <summary>Changes whenever an item of the tile is added, changed or removed: a job that was started at another version is out of date.</summary>
            public int Version;
        }

        private readonly Dictionary<TKey, Entry> m_Items = new Dictionary<TKey, Entry>();
        private readonly Dictionary<TileKey, Bucket> m_Tiles = new Dictionary<TileKey, Bucket>();
        private readonly HashSet<TileKey> m_Dirty = new HashSet<TileKey>();
        private readonly HashSet<TileKey> m_LongReach = new HashSet<TileKey>();
        private readonly Dictionary<TileKey, int> m_Removed = new Dictionary<TileKey, int>();
        private int m_Pass;
        private int m_Changes;

        public int Count => m_Items.Count;

        /// <summary>The number of tiles that have items.</summary>
        public int TileCount => m_Tiles.Count;

        /// <summary>Goes up by one for every change of any item: a number to compare, not a count of anything.</summary>
        public int Changes => m_Changes;

        public bool HasDirty => m_Dirty.Count > 0;

        /// <summary>The tiles that have items.</summary>
        public IEnumerable<TileKey> TileKeys => m_Tiles.Keys;

        // ------------------------------------------------------------------ changes

        /// <summary>
        /// Adds an item, or changes it when it is there. True when something changed (a record that is the same as the one that was there changes nothing, and marks no tile). Either way the item
        /// counts as read in the pass that is running, so that an item that is added while a full read is under way is not taken for one that is gone when it ends.
        /// </summary>
        public bool Upsert(TKey key, in ItemRecord record)
        {
            TileKey tile = Tiles.Of(record.Position.X, record.Position.Z);
            if (m_Items.TryGetValue(key, out Entry old))
            {
                if (TileDiff.Same(old.Record, record))
                {
                    if (old.Pass != m_Pass)
                    {
                        old.Pass = m_Pass;
                        m_Items[key] = old;
                    }

                    return false;
                }

                bool wasLong = LongReach.Is(old.Record.TypeHash), isLong = LongReach.Is(record.TypeHash);
                if (!old.Tile.Equals(tile))
                {
                    Detach(key, old.Tile, wasLong);
                    Attach(key, tile, isLong);
                }
                else
                {
                    if (wasLong != isLong)
                    {
                        Bucket bucket = m_Tiles[tile];
                        bucket.LongCount += isLong ? 1 : -1;
                        if (bucket.LongCount == (isLong ? 1 : 0))
                        {
                            if (isLong)
                                m_LongReach.Add(tile);
                            else
                                m_LongReach.Remove(tile);
                        }
                    }

                    Bump(old.Tile);
                }

                m_Items[key] = new Entry { Record = record, Tile = tile, Pass = m_Pass };
                m_Changes++;
                return true;
            }

            Attach(key, tile, LongReach.Is(record.TypeHash));
            m_Items[key] = new Entry { Record = record, Tile = tile, Pass = m_Pass };
            m_Changes++;
            return true;
        }

        /// <summary>Removes an item. True when it was there.</summary>
        public bool Remove(TKey key)
        {
            if (!m_Items.TryGetValue(key, out Entry old))
                return false;
            m_Items.Remove(key);
            Detach(key, old.Tile, LongReach.Is(old.Record.TypeHash));
            m_Changes++;
            return true;
        }

        private void Attach(TKey key, TileKey tile, bool longReach)
        {
            if (!m_Tiles.TryGetValue(tile, out Bucket bucket))
            {
                m_Tiles[tile] = bucket = new Bucket();
                // A tile that was emptied and has items again goes on counting its versions from where it was, so that an old job cannot match it by chance.
                if (m_Removed.TryGetValue(tile, out int was))
                {
                    bucket.Version = was;
                    m_Removed.Remove(tile);
                }
            }

            bucket.Keys.Add(key);
            if (longReach && bucket.LongCount++ == 0)
                m_LongReach.Add(tile);
            bucket.Version++;
            m_Dirty.Add(tile);
        }

        private void Detach(TKey key, TileKey tile, bool longReach)
        {
            if (!m_Tiles.TryGetValue(tile, out Bucket bucket))
                return;
            bucket.Keys.Remove(key);
            if (longReach && --bucket.LongCount == 0)
                m_LongReach.Remove(tile);
            bucket.Version++;
            m_Dirty.Add(tile);
            if (bucket.Keys.Count == 0)
            {
                m_Tiles.Remove(tile);
                m_Removed[tile] = bucket.Version;
            }
        }

        private void Bump(TileKey tile)
        {
            if (m_Tiles.TryGetValue(tile, out Bucket bucket))
                bucket.Version++;
            m_Dirty.Add(tile);
        }

        /// <summary>Every tile changed: what was built from the items is built again, and a build that was read at the old versions is out of date (a setting of the build, not an item, changed). Returns how many tiles.</summary>
        public int BumpAll()
        {
            foreach (TileKey tile in m_Tiles.Keys)
                Bump(tile);
            return m_Tiles.Count;
        }

        /// <summary>Forgets everything.</summary>
        public void Clear()
        {
            m_Items.Clear();
            m_Tiles.Clear();
            m_LongReach.Clear();
            m_Dirty.Clear();
            m_Removed.Clear();
            m_Changes++;
        }

        // ------------------------------------------------------------------ a full read in slices

        /// <summary>Starts a pass: every item that is Touched until EndPass is kept, the others are removed at the end.</summary>
        public void BeginPass() => m_Pass++;

        /// <summary>Adds or changes an item as part of the pass; the item is kept when the pass ends.</summary>
        public bool Touch(TKey key, in ItemRecord record) => Upsert(key, record);

        /// <summary>Ends the pass: the items that were not Touched since BeginPass are removed. Returns how many; their keys are added to removed when it is given.</summary>
        public int EndPass(List<TKey> removed = null)
        {
            List<TKey> gone = null;
            foreach (KeyValuePair<TKey, Entry> pair in m_Items)
            {
                if (pair.Value.Pass != m_Pass)
                    (gone ?? (gone = new List<TKey>())).Add(pair.Key);
            }

            if (gone == null)
                return 0;
            foreach (TKey key in gone)
            {
                Remove(key);
                removed?.Add(key);
            }

            return gone.Count;
        }

        // ------------------------------------------------------------------ tiles that changed

        /// <summary>Moves the tiles that changed since the last call into the set, and forgets them here. A tile that lost its last item is in it too (it has no items any more).</summary>
        public void TakeDirty(HashSet<TileKey> into)
        {
            foreach (TileKey tile in m_Dirty)
                into.Add(tile);
            m_Dirty.Clear();
        }

        /// <summary>The version of a tile's items (0 for a tile that never had any): a job that read the tile at another version is out of date.</summary>
        public int VersionOf(TileKey tile)
        {
            if (m_Tiles.TryGetValue(tile, out Bucket bucket))
                return bucket.Version;
            return m_Removed.TryGetValue(tile, out int was) ? was : 0;
        }

        public bool HasItems(TileKey tile) => m_Tiles.ContainsKey(tile);

        /// <summary>Whether the tile has an item that is seen from far (LongReach).</summary>
        public bool HasLongReach(TileKey tile) => m_LongReach.Contains(tile);

        /// <summary>The tiles that have such an item: read, not changed (the planner walks it).</summary>
        public HashSet<TileKey> LongReachTiles => m_LongReach;

        public int ItemCount(TileKey tile) => m_Tiles.TryGetValue(tile, out Bucket bucket) ? bucket.Keys.Count : 0;

        /// <summary>The records of one tile, copied (a worker reads the copy while the store goes on changing); only those seen from far when longReachOnly. Returns the version the copy was made at.</summary>
        public int Snapshot(TileKey tile, List<ItemRecord> into, bool longReachOnly = false)
        {
            if (!m_Tiles.TryGetValue(tile, out Bucket bucket))
                return VersionOf(tile);
            foreach (TKey key in bucket.Keys)
            {
                ItemRecord record = m_Items[key].Record;
                if (!longReachOnly || LongReach.Is(record.TypeHash))
                    into.Add(record);
            }

            return bucket.Version;
        }

        // ------------------------------------------------------------------ questions about a place

        /// <summary>The items within radius (XZ distance) of a point. Looks at the tiles round the point only, or at every tile when the circle covers more tiles than there are.</summary>
        public void ItemsNear(float x, float z, float radius, List<KeyValuePair<TKey, ItemRecord>> into)
        {
            float r2 = radius * radius;
            int x0 = (int)Math.Floor((x - radius) / Tiles.Size), x1 = (int)Math.Floor((x + radius) / Tiles.Size);
            int z0 = (int)Math.Floor((z - radius) / Tiles.Size), z1 = (int)Math.Floor((z + radius) / Tiles.Size);
            long span = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
            if (span > m_Tiles.Count)
            {
                foreach (KeyValuePair<TileKey, Bucket> pair in m_Tiles)
                {
                    if (pair.Key.X >= x0 && pair.Key.X <= x1 && pair.Key.Z >= z0 && pair.Key.Z <= z1)
                        AddNear(pair.Value, x, z, r2, into);
                }

                return;
            }

            for (int tx = x0; tx <= x1; tx++)
            {
                for (int tz = z0; tz <= z1; tz++)
                {
                    if (m_Tiles.TryGetValue(new TileKey(tx, tz), out Bucket bucket))
                        AddNear(bucket, x, z, r2, into);
                }
            }
        }

        private void AddNear(Bucket bucket, float x, float z, float r2, List<KeyValuePair<TKey, ItemRecord>> into)
        {
            foreach (TKey key in bucket.Keys)
            {
                ItemRecord record = m_Items[key].Record;
                float dx = record.Position.X - x, dz = record.Position.Z - z;
                if (dx * dx + dz * dz <= r2)
                    into.Add(new KeyValuePair<TKey, ItemRecord>(key, record));
            }
        }

        /// <summary>
        /// The items that could be within radius (XZ) of a segment: those of every tile whose box is that near. A superset (the caller measures what it needs): the work is a measure of every tile
        /// that has items, and of the items of the few that are near the line, not of every item.
        /// </summary>
        public void ItemsNearSegment(Vec3 from, Vec3 to, float radius, List<KeyValuePair<TKey, ItemRecord>> into)
        {
            float half = Tiles.Size * 0.7072f;
            foreach (KeyValuePair<TileKey, Bucket> pair in m_Tiles)
            {
                // In the XZ plane, where it can only be nearer than in space: nothing that is near the line in space is left out.
                Vec3 centre = Tiles.Centre(pair.Key);
                if (DistanceXZToSegment(centre.X, centre.Z, from.X, from.Z, to.X, to.Z) > radius + half)
                    continue;
                foreach (TKey key in pair.Value.Keys)
                    into.Add(new KeyValuePair<TKey, ItemRecord>(key, m_Items[key].Record));
            }
        }

        private static float DistanceXZToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float length2 = dx * dx + dz * dz;
            float t = length2 < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((px - ax) * dx + (pz - az) * dz) / length2));
            float ex = ax + dx * t - px, ez = az + dz * t - pz;
            return (float)Math.Sqrt(ex * ex + ez * ez);
        }

        /// <summary>The items of a group (the boards of one support) that stand in the tile of a place: a group stands in one place, so its members are in one tile.</summary>
        public void GroupMembers(uint group, float x, float z, List<KeyValuePair<TKey, ItemRecord>> into)
        {
            if (group == 0u || !m_Tiles.TryGetValue(Tiles.Of(x, z), out Bucket bucket))
                return;
            foreach (TKey key in bucket.Keys)
            {
                ItemRecord record = m_Items[key].Record;
                if (record.Group == group)
                    into.Add(new KeyValuePair<TKey, ItemRecord>(key, record));
            }
        }

        // ------------------------------------------------------------------ the dictionary view

        public bool ContainsKey(TKey key) => m_Items.ContainsKey(key);

        public bool TryGetValue(TKey key, out ItemRecord value)
        {
            if (m_Items.TryGetValue(key, out Entry entry))
            {
                value = entry.Record;
                return true;
            }

            value = default;
            return false;
        }

        public ItemRecord this[TKey key] => m_Items[key].Record;

        public IEnumerable<TKey> Keys => m_Items.Keys;

        public IEnumerable<ItemRecord> Values
        {
            get
            {
                foreach (KeyValuePair<TKey, Entry> pair in m_Items)
                    yield return pair.Value.Record;
            }
        }

        public IEnumerator<KeyValuePair<TKey, ItemRecord>> GetEnumerator()
        {
            foreach (KeyValuePair<TKey, Entry> pair in m_Items)
                yield return new KeyValuePair<TKey, ItemRecord>(pair.Key, pair.Value.Record);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
