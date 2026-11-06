using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// When the items of a saved game are put back under the loading screen (0.12.78). The game rebuilds the roads' links (ConnectedEdge) in the Deserialize phase, before this mod hears of the load
    /// (SystemOrder: ConnectedEdgeSystem), and the restore reads roads, so it waits for a few frames and for the links of a sample of roads to be there, and goes on anyway when they are not after GiveUp
    /// frames: then it is as late as it always was at the latest (the old rule was eight frames after the end of the loading). Pure.
    /// </summary>
    public static class EarlyRestore
    {
        public const int SampleMax = 128;

        /// <summary>Whether the restore may run: Frames updates since the saved game was deserialized (early) or since the loading ended (late: giveUp = settle, the links are not asked).</summary>
        public static bool Ready(int frames, int settle, int giveUp, Func<bool> linked)
        {
            if (frames < settle)
                return false;
            if (frames >= giveUp || linked == null)
                return true;
            return linked();
        }

        /// <summary>Evenly spaced indices of at most max of total roads: the first and the last are in, none twice.</summary>
        public static int[] SampleIndices(int total, int max)
        {
            if (total <= 0 || max <= 0)
                return new int[0];
            if (total <= max)
            {
                var all = new int[total];
                for (int i = 0; i < total; i++)
                    all[i] = i;
                return all;
            }

            if (max == 1)
                return new[] { 0 };
            var picked = new int[max];
            for (int i = 0; i < max; i++)
                picked[i] = (int)((long)i * (total - 1) / (max - 1));
            return picked;
        }

        /// <summary>The links are there when at least 49 of 50 of the sampled roads are linked at both ends (a broken road of the game does not hold the restore); no road sampled: nothing to wait for.</summary>
        public static bool Linked(int sampled, int linked)
        {
            if (sampled <= 0)
                return true;
            return (long)linked * 50 >= (long)sampled * 49;
        }
    }
}
