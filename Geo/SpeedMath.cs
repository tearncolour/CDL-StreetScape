namespace CDLC.TS.Geo
{
    /// <summary>
    /// The unit of the speed limits the game keeps. Pure, so that it is checked offline: the game's reader (RoadReader) hands the raw figures to this.
    /// The game keeps speeds in metres per second: CarLane.m_SpeedLimit and RoadComposition.m_SpeedLimit hold the road prefab's km/h figure divided by 3.6. The log of 0.4.8 (2026-10-18)
    /// shows both side by side on a road of 100 km/h: 27.778 and 27.778. 0.4.2 to 0.4.8 took the composition's figure for km/h, so that every road came out at 28 km/h, the smallest size band.
    /// </summary>
    public static class SpeedMath
    {
        /// <summary>km/h per metre per second.</summary>
        public const float KmhPerGameUnit = 3.6f;

        /// <summary>A speed in the game's unit (metres per second) in km/h.</summary>
        public static float ToKmh(float gameUnits) => gameUnits * KmhPerGameUnit;

        /// <summary>
        /// The speed limit of a road in km/h from the raw figures: the road's own (the cross-section's RoadComposition.m_SpeedLimit; zero or less when it has none) when there is one, else
        /// the highest limit of its car lanes (zero or less when it has none). Both in the game's unit. False when neither is there.
        /// </summary>
        public static bool TryKmh(float composition, float laneMax, out float kmh)
        {
            if (composition > 0f)
            {
                kmh = ToKmh(composition);
                return true;
            }

            kmh = laneMax > 0f ? ToKmh(laneMax) : 0f;
            return laneMax > 0f;
        }
    }
}
