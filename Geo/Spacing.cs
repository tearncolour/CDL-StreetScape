using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static class Spacing
    {
        /// <summary>Two signs of one type are not put closer together than this (m, in the ground plane).</summary>
        public const float MinDistance = 0.4f;

        /// <summary>
        /// The distance in the ground plane to the nearest item of the same type, or float.MaxValue when there is none. The placement tool refuses
        /// a click when this is under MinDistance, and logs the distance with every click so that a refusal can be read from the log.
        /// </summary>
        public static float NearestSame(IEnumerable<ItemRecord> items, Vec3 position, uint typeHash)
        {
            float best = float.MaxValue;
            foreach (ItemRecord item in items)
            {
                if (item.TypeHash != typeHash)
                    continue;
                float distance = Vec3.DistanceXZ(item.Position, position);
                if (distance < best)
                    best = distance;
            }

            return best;
        }

        public static bool TooClose(IEnumerable<ItemRecord> items, Vec3 position, uint typeHash) => NearestSame(items, position, typeHash) < MinDistance;
    }
}
