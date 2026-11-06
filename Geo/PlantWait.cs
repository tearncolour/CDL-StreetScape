namespace CDLC.TS.Geo
{
    /// <summary>
    /// Whether the load's hold on the loading screen waits for the planters' look through the save's objects (PlantSystem.FirstUseWaiting). The look needs the plant copies, which the game initializes a
    /// few frames after the load; a game with no copy, or copies that never get their object data, would otherwise hold every load for the whole cap with nothing to wait for.
    /// </summary>
    public static class PlantWait
    {
        /// <summary>The frames the copies get to become usable before the hold stops waiting for them.</summary>
        public const int PrepareFrames = 60;

        public static bool Waiting(bool scanWanted, bool ready, int copies, int framesNotReady) =>
            scanWanted && (ready || (copies > 0 && framesNotReady < PrepareFrames));
    }
}
