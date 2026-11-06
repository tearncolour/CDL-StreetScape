using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Several placed items on one support (GB 5768.2 4.8.2, 4.8.6): items with the same non-zero Group are the boards of one support. They share the place, the facing, the support and the
    /// reach (the tool writes the same to each); the Slot is the order they were put on, which is what Assembly sorts by. A group is drawn as one support carrying all its boards and is picked
    /// as one, the board that is hit telling which of the items it is. Pure, so that it is checked offline.
    /// </summary>
    public static class ItemGroups
    {
        /// <summary>The order of the members of a group in every list that is made of one: by slot, then by type and settings so that the order never depends on how a dictionary was walked.</summary>
        public static int Compare(ItemRecord a, ItemRecord b)
        {
            int c = a.Slot.CompareTo(b.Slot);
            if (c != 0)
                return c;
            c = a.TypeHash.CompareTo(b.TypeHash);
            if (c != 0)
                return c;
            return string.CompareOrdinal(a.Spec ?? "", b.Spec ?? "");
        }

        /// <summary>
        /// The boards of a group's members (already in the order of Compare): the member's number is its place in the list, which is what a hit on a board tells. A member of a type that is
        /// not in the catalogue (a pack that is gone) has no board.
        /// </summary>
        public static List<AssemblyBoard> Boards(IReadOnlyList<ItemRecord> members)
        {
            var boards = new List<AssemblyBoard>();
            for (int i = 0; i < members.Count; i++)
            {
                SignDef def = SignCatalog.ByHash(members[i].TypeHash);
                if (def == null)
                    continue;
                boards.Add(new AssemblyBoard { Instance = SignInstances.Of(def, members[i].Spec), Slot = members[i].Slot, Member = i, Lane = members[i].Lane, OffsetX = members[i].OffsetX, Manual = members[i].Manual, Turn = members[i].Turn, Back = members[i].Back, Tilt = members[i].Tilt, Rise = members[i].Rise, Clearance = members[i].Clearance });
            }

            return boards;
        }

        /// <summary>
        /// The support of a group as it is drawn, with its layout and where each board is: its own when the lead member names one, else the planner's for the whole. The members must be in the
        /// order of Compare; the first is the lead (its support and reach count). Null when there is no board of a known type.
        /// </summary>
        public static AssemblyPlan PlanOf(IReadOnlyList<ItemRecord> members, out List<AssemblyBoard> ordered)
        {
            ordered = Assembly.Order(Boards(members));
            if (ordered.Count == 0)
                return null;
            ItemRecord lead = members[0];
            return Assembly.Plan(ordered, SupportCatalog.ByHash(lead.SupportHash), lead.Reach);
        }

        /// <summary>The layout of a group's support (see PlanOf); false when there is no board of a known type.</summary>
        public static bool LayoutOf(IReadOnlyList<ItemRecord> members, out SupportLayout layout, out SupportDef support, out SignDef total, out List<AssemblyBoard> ordered)
        {
            AssemblyPlan plan = PlanOf(members, out ordered);
            layout = default;
            support = null;
            total = null;
            if (plan == null)
                return false;
            layout = plan.Layout;
            support = plan.Support;
            total = plan.Total;
            return true;
        }

        /// <summary>The members of one group in the order of Compare, for any key (the renderer uses the entity; ties are broken by the key's own order).</summary>
        public static List<KeyValuePair<TKey, ItemRecord>> Sorted<TKey>(IEnumerable<KeyValuePair<TKey, ItemRecord>> members, IComparer<TKey> keyOrder)
        {
            var list = new List<KeyValuePair<TKey, ItemRecord>>(members);
            list.Sort((a, b) =>
            {
                int c = Compare(a.Value, b.Value);
                return c != 0 ? c : keyOrder.Compare(a.Key, b.Key);
            });
            return list;
        }

        /// <summary>
        /// The groups among some records, and the records that are on a support of their own. A group is one entry whatever its size; a single member left over (the others were deleted by
        /// another tool) is still a group of one, which draws as the single item it is.
        /// </summary>
        public static void Split<TKey>(IEnumerable<KeyValuePair<TKey, ItemRecord>> records, List<KeyValuePair<TKey, ItemRecord>> singles, Dictionary<uint, List<KeyValuePair<TKey, ItemRecord>>> groups)
        {
            foreach (KeyValuePair<TKey, ItemRecord> pair in records)
            {
                if (pair.Value.Group == 0u)
                {
                    singles.Add(pair);
                    continue;
                }

                if (!groups.TryGetValue(pair.Value.Group, out List<KeyValuePair<TKey, ItemRecord>> list))
                    groups[pair.Value.Group] = list = new List<KeyValuePair<TKey, ItemRecord>>();
                list.Add(pair);
            }
        }

        /// <summary>A group id that no item has (0 is "no group"): random, so that two games' saves that are put together do not clash, and checked against the ones in use.</summary>
        public static uint NewId(IEnumerable<ItemRecord> inUse, Random random)
        {
            var used = new HashSet<uint>();
            foreach (ItemRecord record in inUse)
            {
                if (record.Group != 0u)
                    used.Add(record.Group);
            }

            for (int i = 0; i < 64; i++)
            {
                uint id = (uint)random.Next(1, int.MaxValue);
                if (!used.Contains(id))
                    return id;
            }

            // Sixty-four collisions among billions: walk up from the last until a free one.
            uint next = (uint)random.Next(1, int.MaxValue);
            while (next == 0u || used.Contains(next))
                next++;
            return next;
        }

        /// <summary>The slot a board that is added goes to: after every board that is on the support.</summary>
        public static byte NextSlot(IEnumerable<ItemRecord> members)
        {
            int max = -1;
            foreach (ItemRecord record in members)
                max = Math.Max(max, record.Slot);
            return (byte)Math.Min(255, max + 1);
        }
    }
}
