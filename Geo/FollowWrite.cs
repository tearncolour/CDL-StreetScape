using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What a Writing follow found for an item: where it is to be put and which way it faces; for a line the new text too (null: the text stays, only the place and the facing change).</summary>
    public struct FollowWrite<TItem>
    {
        public TItem Item;
        public Vec3 Position;
        public float Yaw;
        public string Path;
        public bool IsLine;

        /// <summary>For a piece of a wall whose start along the whole line (and the line's length) changed with the lengths of the pieces before it: the new phase and total (cm), see LinePhases.</summary>
        public bool HasPhase;

        public long PhaseCm, TotalCm;

        /// <summary>Only the phase and total are to be written: the item's place and facing are left as they are (a piece that was not followed, or was moved by someone else).</summary>
        public bool PhaseOnly;
    }
}
