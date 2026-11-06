using System;

namespace CDLC.TS.Geo
{
    /// <summary>Which bound held a lower edge back (WallHeight.Limit).</summary>
    public enum HeightBound
    {
        None = 0,
        Lowest = 1,
        Highest = 2
    }

    /// <summary>
    /// How high a board may be fixed on a building's wall (0.12.80; the owner: "高层建筑下我的建筑物附着高度好像限制死了"). Until 0.12.79 four bounds held a board low whatever the building was: the lower
    /// edge was clamped to 60 m (WallMount.MaxHeight) when the cursor, the editor's buttons or the arrow keys set it; the arrow keys' lift in the placing mode was kept within 30 m of the cursor
    /// (FacadeMove.MaxShift); and a saved reach over 200 m was read back as 0, the board's foot. The highest is now the building's own top (its prefab's ObjectGeometryData bounds, from the ECS) and a
    /// margin, never under the old 60 m (so what was placed stays as it was) and never over Ceiling; where the building is not known (editing) it is Ceiling. Pure.
    /// </summary>
    public static class WallHeight
    {
        /// <summary>The highest any lower edge may be whatever the building says (m above the foot of the wall), and the highest a stored reach is read back as; a bound for a prefab whose bounds are nonsense.</summary>
        public const float Ceiling = 500f;

        /// <summary>How far over a building's top a lower edge may be put (m): the parapet and the lift shaft stand over the bounds' top, and a board is fixed to the wall, not floating.</summary>
        public const float Margin = 5f;

        /// <summary>The editor's coarsest button for the height of a wall board (m): a tower of 150 m is fifteen presses of it.</summary>
        public const float CoarseButton = 10f;

        /// <summary>How long a held up or down arrow goes on at its own step (s), and until when at five times it: a tower of 150 m is reached in a few seconds, and a press after the key was let go is one step again.</summary>
        public const float HoldFast = 1.0f, HoldFaster = 2.5f;

        /// <summary>
        /// The highest a lower edge may be on a building whose prefab's bounds reach buildingTop above the foot of its wall (m; NaN: not known, the bound is then Ceiling, so that not knowing never holds a
        /// board down): the top and Margin, at least WallMount.MaxHeight (the old bound stays the least), at most Ceiling.
        /// </summary>
        public static float Highest(float buildingTop)
        {
            if (float.IsNaN(buildingTop) || float.IsInfinity(buildingTop))
                return Ceiling;
            return Math.Max(WallMount.MaxHeight, Math.Min(Ceiling, buildingTop + Margin));
        }

        /// <summary>A lower edge within WallMount.MinHeight and highest (the caller has rounded it: the standard height is kept as it is); which bound held it, if one did.</summary>
        public static float Limit(float lower, float highest, out HeightBound bound)
        {
            bound = HeightBound.None;
            if (lower < WallMount.MinHeight)
            {
                bound = HeightBound.Lowest;
                return WallMount.MinHeight;
            }

            if (lower > highest)
            {
                bound = HeightBound.Highest;
                return highest;
            }

            return lower;
        }

        /// <summary>A stored reach or base reach as it is read back: 0 to Ceiling, else 0 (the support's own). The bound was 200 until 0.12.80, which gave a board fixed higher than that its foot back.</summary>
        public static float ReadReach(float stored) => stored >= 0f && stored <= Ceiling ? stored : 0f;

        /// <summary>One press of an up or down arrow as a distance (m): the step of FacadeMove (0.1, Shift 0.5), five times it once the key has been held HoldFast seconds, twenty times after HoldFaster.</summary>
        public static float LiftStep(bool shift, float heldSeconds) =>
            FacadeMove.StepOf(shift) * (heldSeconds >= HoldFaster ? 20f : heldSeconds >= HoldFast ? 5f : 1f);

        /// <summary>
        /// The lift the arrow keys have given a board in the placing mode, after one more step (delta, m, + up): to a hundredth of a metre, within Ceiling either way (it was FacadeMove.MaxShift, 30 m,
        /// like a move along the facade; the height itself is kept within the building's own bound).
        /// </summary>
        public static float Lift(float offset, float delta) => Math.Max(-Ceiling, Math.Min(Ceiling, (float)Math.Round((offset + delta) * 100f) / 100f));

        /// <summary>The line of the log for a wall's height: what the cursor asked for, the ground, the edge asked for and kept, the bounds, and the bound that held it.</summary>
        public static string Describe(float cursorY, float ground, float asked, float kept, float highest, HeightBound bound) =>
            $"height: cursor {cursorY:0.0}, ground {ground:0.0}, asked {asked:0.0} m, kept {kept:0.0} m (lowest {WallMount.MinHeight:0.0}, highest {highest:0.0}){(bound == HeightBound.None ? "" : bound == HeightBound.Highest ? ", held by the highest" : ", held by the lowest")}";
    }
}
