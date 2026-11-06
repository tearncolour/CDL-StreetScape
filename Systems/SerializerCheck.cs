using System.Reflection;
using Game.Serialization;
using Unity.Collections;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>
    /// Whether the game's serializer leaves this mod's items out of a save (0.12.82). The saves, the plants and the extensions' hangings all rely on it: SerializerSystem's query excludes LivePath, which
    /// the mod puts on its items. Asked of the game's own query before the writing (EntityQuery.MatchesIgnoreFilter on an item), not found out from a save that came back with the items in it.
    /// </summary>
    internal static class SerializerCheck
    {
        private const int kLooked = 8;

        private static readonly FieldInfo s_Query = typeof(SerializerSystem).GetField("m_Query", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>True: the query takes none of the items looked at. False: it takes one. Null: it cannot be told (the field is not there any more, or there is no item).</summary>
        internal static bool? LeavesOut(World world, EntityQuery items)
        {
            if (s_Query == null || items.IsEmptyIgnoreFilter)
                return null;
            SerializerSystem serializer = world.GetExistingSystemManaged<SerializerSystem>();
            if (serializer == null || !(s_Query.GetValue(serializer) is EntityQuery query))
                return null;
            using (NativeArray<Entity> entities = items.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length && i < kLooked; i++)
                {
                    if (query.MatchesIgnoreFilter(entities[i]))
                        return false;
                }
            }

            return true;
        }
    }
}
