using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The direction of a board as it is drawn: the support's yaw and, only on a post that lets this board turn, its own turn. The complete support is planned first, just as in SignBuilder:
    /// a stack whose automatic support needs two posts, a board on an arm or beam, and a board on a wall do not turn even if an old item keeps a nonzero Turn.
    /// </summary>
    public static class BoardFacing
    {
        /// <summary>The facing of a board in the actual plan of its support. member is its index in the records from which the plan was made.</summary>
        public static float Yaw(AssemblyPlan plan, float supportYaw, int member)
        {
            Placement place = plan?.Placed.Find(p => p.Board.Member == member);
            if (place == null || (plan.Hung && !place.OnPost) || Math.Abs(place.Board.Turn) <= 1e-4f ||
                !Assembly.TurnAxis(plan.Support.Kind, plan.Layout, place.Board, out _))
                return supportYaw;
            return supportYaw + Assembly.WrapTurn(place.Board.Turn);
        }

        /// <summary>A board on a support of its own, using its template's current dimensions and its real support layout.</summary>
        public static float Yaw(ItemRecord record)
        {
            if (Math.Abs(record.Turn) <= 1e-4f)
                return record.Yaw;
            AssemblyPlan plan = ItemGroups.PlanOf(new[] { record }, out _);
            return Yaw(plan, record.Yaw, 0);
        }

        /// <summary>
        /// The facings of records in their input order. Members of a group are planned together in the renderer's lead order; the lead's yaw is the whole support's transform, while each board
        /// keeps its own turn. Unknown types have no board and retain the support's direction.
        /// </summary>
        public static float[] Yaws(IReadOnlyList<ItemRecord> records)
        {
            var yaws = new float[records.Count];
            var groups = new Dictionary<uint, List<KeyValuePair<int, ItemRecord>>>();
            for (int i = 0; i < records.Count; i++)
            {
                ItemRecord record = records[i];
                if (record.Group == 0u)
                {
                    yaws[i] = Yaw(record);
                    continue;
                }
                if (!groups.TryGetValue(record.Group, out List<KeyValuePair<int, ItemRecord>> group))
                    groups[record.Group] = group = new List<KeyValuePair<int, ItemRecord>>();
                group.Add(new KeyValuePair<int, ItemRecord>(i, record));
            }

            foreach (List<KeyValuePair<int, ItemRecord>> group in groups.Values)
            {
                List<KeyValuePair<int, ItemRecord>> sorted = ItemGroups.Sorted(group, Comparer<int>.Default);
                var members = new List<ItemRecord>(sorted.Count);
                foreach (KeyValuePair<int, ItemRecord> pair in sorted)
                    members.Add(pair.Value);
                bool turns = members.Exists(record => Math.Abs(record.Turn) > 1e-4f);
                AssemblyPlan plan = turns ? ItemGroups.PlanOf(members, out _) : null;
                for (int i = 0; i < members.Count; i++)
                    yaws[sorted[i].Key] = Yaw(plan, members[0].Yaw, i);
            }
            return yaws;
        }

        /// <summary>
        /// A live screen's direction, with its siblings from the renderer's indexed store. The current item replaces its stored snapshot so an edit read this frame is used immediately, and
        /// the siblings still determine whether the complete support has one post or several. This reads only the tile the support stands in.
        /// </summary>
        public static float Yaw<TKey>(ItemStore<TKey> store, TKey key, ItemRecord current) where TKey : IEquatable<TKey>
        {
            if (current.Group == 0u || store == null)
                return Yaw(current);
            var found = new List<KeyValuePair<TKey, ItemRecord>>();
            store.GroupMembers(current.Group, current.Position.X, current.Position.Z, found);
            int own = found.FindIndex(pair => EqualityComparer<TKey>.Default.Equals(pair.Key, key));
            if (own < 0)
                found.Add(new KeyValuePair<TKey, ItemRecord>(key, current));
            else
                found[own] = new KeyValuePair<TKey, ItemRecord>(key, current);
            found = ItemGroups.Sorted(found, Comparer<TKey>.Default);
            var members = new List<ItemRecord>(found.Count);
            own = -1;
            for (int i = 0; i < found.Count; i++)
            {
                members.Add(found[i].Value);
                if (EqualityComparer<TKey>.Default.Equals(found[i].Key, key))
                    own = i;
            }
            AssemblyPlan plan = Math.Abs(current.Turn) > 1e-4f ? ItemGroups.PlanOf(members, out _) : null;
            return Yaw(plan, members[0].Yaw, own);
        }

        /// <summary>Degrees clockwise from north, rounded as the map's protocol requires, and wrapped to 0..359.</summary>
        public static int Bearing(float yaw)
        {
            double degrees = yaw * 180.0 / Math.PI % 360.0;
            if (degrees < 0.0)
                degrees += 360.0;
            return (int)Math.Round(degrees) % 360;
        }
    }
}
