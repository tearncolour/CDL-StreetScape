using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Which map tiles have to be rebuilt when the set of items goes from one state to the next. Pure, so that it is checked offline: the renderer
    /// feeds it the records it had and the records it just read, and rebuilds the tiles it names.
    /// </summary>
    public static class TileDiff
    {
        /// <summary>
        /// Whether two records are drawn alike: every field that ChunkBuilder reads (Auto, which only says what the automatic marking replaces, is not one). Until 0.12.58 the lean (0.12.55) and the style
        /// (0.12.57) were missing, so a change of either alone was taken for none: the store kept the old record and the tile was not rebuilt until another field changed (EditRenderTests checks that
        /// every field is here).
        /// </summary>
        public static bool Same(ItemRecord a, ItemRecord b) =>
            a.Tilt == b.Tilt && a.Rise == b.Rise && a.Clearance == b.Clearance && a.Style == b.Style && a.TypeHash == b.TypeHash && a.SupportHash == b.SupportHash && string.Equals(a.Spec ?? "", b.Spec ?? "", System.StringComparison.Ordinal) && a.Reach == b.Reach && a.Group == b.Group && a.Slot == b.Slot && a.Lane == b.Lane && a.OffsetX == b.OffsetX && a.Turn == b.Turn && a.Back == b.Back && a.Manual == b.Manual && a.Hidden == b.Hidden && ReferenceEquals(a.Fit, b.Fit) && ReferenceEquals(a.Ground, b.Ground) && ReferenceEquals(a.Laid, b.Laid) && a.Yaw == b.Yaw && a.Position.X == b.Position.X && a.Position.Y == b.Position.Y && a.Position.Z == b.Position.Z;

        /// <summary>
        /// The tiles that differ: the tile of every record that is new, gone or changed (both tiles when a record moved to another one). The keys
        /// are whatever identifies an item (the renderer uses the entity).
        /// </summary>
        public static HashSet<TileKey> Dirty<TKey>(IReadOnlyDictionary<TKey, ItemRecord> before, IReadOnlyDictionary<TKey, ItemRecord> after)
        {
            var dirty = new HashSet<TileKey>();
            foreach (KeyValuePair<TKey, ItemRecord> pair in after)
            {
                bool known = before.TryGetValue(pair.Key, out ItemRecord old);
                if (known && Same(old, pair.Value))
                    continue;

                dirty.Add(Tiles.Of(pair.Value.Position.X, pair.Value.Position.Z));
                if (known)
                    dirty.Add(Tiles.Of(old.Position.X, old.Position.Z));
            }

            foreach (KeyValuePair<TKey, ItemRecord> pair in before)
            {
                if (!after.ContainsKey(pair.Key))
                    dirty.Add(Tiles.Of(pair.Value.Position.X, pair.Value.Position.Z));
            }

            return dirty;
        }

        /// <summary>The records grouped by tile.</summary>
        public static Dictionary<TileKey, List<ItemRecord>> ByTile<TKey>(IReadOnlyDictionary<TKey, ItemRecord> records)
        {
            var result = new Dictionary<TileKey, List<ItemRecord>>();
            foreach (ItemRecord record in records.Values)
            {
                TileKey key = Tiles.Of(record.Position.X, record.Position.Z);
                if (!result.TryGetValue(key, out List<ItemRecord> list))
                    result[key] = list = new List<ItemRecord>();
                list.Add(record);
            }

            return result;
        }
    }
}
