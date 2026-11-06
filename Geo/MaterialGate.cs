using System;

namespace CDLC.TS.Geo
{
    /// <summary>What making the Unity object of a tile's part needs of its material: it is there (or a cheap copy of one), its data is still being made on a worker, or the main thread has to make it.</summary>
    public enum MaterialNeed
    {
        Have,
        Wait,
        Make
    }

    /// <summary>What a making of objects does with a part, given what its material needs.</summary>
    public enum GateAction
    {
        /// <summary>Make the part now.</summary>
        Go,

        /// <summary>The material is for a later update: this update has done other work (or made a material) already.</summary>
        Defer,

        /// <summary>The material's data is on its way from a worker: leave the part and come back.</summary>
        Wait
    }

    /// <summary>
    /// When the main thread may make a material in a tile's making of objects (0.12.76). The owner's log of 0.12.75 had one part of 460 vertices that took 40.7 ms because its material (a baked-detail
    /// surface with its textures and its validation) was made in it, and the first tile of the horizon 52 ms in 0.12.73: the budgets of the update (0.35 ms) are checked between parts, not inside one.
    /// A material is made in an update of its own: never with other work that has used half the budget, and one only. A part that has to wait for its material's data (made on a worker) waits, but not
    /// for ever: after WaitSeconds the main thread makes it as it always did.
    /// </summary>
    public static class MaterialGate
    {
        /// <summary>How long (s) a part waits for the data of its material from a worker before the main thread makes it itself.</summary>
        public const double WaitSeconds = 1.5;

        /// <summary>Whether a material may be made in this update: none yet, and either nothing was done before it or less than half the budget was spent.</summary>
        public static bool MayMake(double spentMs, int partsDone, int madeThisUpdate, double budgetMs) =>
            madeThisUpdate == 0 && (partsDone == 0 || spentMs <= budgetMs * 0.5);

        public static GateAction Decide(MaterialNeed need, double spentMs, int partsDone, int madeThisUpdate, double budgetMs, double waitedSeconds)
        {
            switch (need)
            {
                case MaterialNeed.Have:
                    return GateAction.Go;
                case MaterialNeed.Wait:
                    if (waitedSeconds < WaitSeconds)
                        return GateAction.Wait;
                    break;
            }

            return MayMake(spentMs, partsDone, madeThisUpdate, budgetMs) ? GateAction.Go : GateAction.Defer;
        }
    }
}
