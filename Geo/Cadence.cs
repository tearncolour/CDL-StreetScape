namespace CDLC.TS.Geo
{
    /// <summary>
    /// The turn of a periodic job, counted in frames and in seconds, whichever comes first. A job paced only in frames waits five times as long under the loading screen, where the frames are 50 to 90 ms
    /// (the owner's 0.12.78 log: 68 updates in 6.3 s), as it does at 60 frames a second; with both, a fast frame rate keeps the frame count and a slow one the time.
    /// </summary>
    public static class Cadence
    {
        /// <summary>The fits that are made are given to the renderer in batches every this many frames or seconds while some wait within reach of the camera.</summary>
        public const int BatchFrames = 30;

        public const float BatchSeconds = 0.5f;

        public static bool Due(int framesSince, float secondsSince, int frames, float seconds) => framesSince >= frames || secondsSince >= seconds;

        /// <summary>
        /// The fit queues are ordered again, nearest the camera first (AreaFitSystem.OrderQueues), when entries that are not ordered yet wait: at once (2 frames) while nothing near waits, else every this many
        /// frames or seconds. In frames only it was 1.5 to 3.3 s under the loading screen: the owner's 0.12.79 log has 1,046 fits waiting at 1.5 s, 297 at 2.1 s, the 749 far ones ordered out of the wait by it.
        /// </summary>
        public const int OrderFrames = 30;

        public const float OrderSeconds = 0.5f;

        public static bool OrderDue(bool fresh, int near, int framesSince, float secondsSince) =>
            fresh && (near == 0 ? framesSince >= 2 : Due(framesSince, secondsSince, OrderFrames, OrderSeconds));

        /// <summary>
        /// Whether the made fits are given to the renderer now (AreaFitSystem.TakeReady): there are some, and nothing waits within reach of the camera, or a batch is due. Every item given is read again
        /// and rebuilds its tile, so while many wait they go in batches.
        /// </summary>
        public static bool BatchDue(int ready, int nearQueued, int framesSince, float secondsSince) =>
            ready > 0 && (nearQueued == 0 || Due(framesSince, secondsSince, BatchFrames, BatchSeconds));
    }
}
