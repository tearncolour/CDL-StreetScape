using System;

namespace CDLC.TS.Geo
{
    public static partial class SupportLayouts
    {
        /// <summary>
        /// A board hung under a structure over the road (0.12.81, SupportDef.Hanger): the sizes are this mod's estimates, GB 5768.2 draws no hung board over the road. The tube the channels are clamped to is φ64, φ90
        /// or φ114 by the board's width (to 2 m, to 4 m, wider), the rods φ24, φ32 or φ40 by it (to 2.5 m, to 5 m, wider), one to about HangerPitch of the board's width, two at the least; a plate PlateThick thick,
        /// of OverRoad.PlateHalf's half width, under the soffit at each rod and brace, sunk PlateInto into it so that its top face is not the structure's.
        /// </summary>
        public const float HangerPitch = 1.5f, PlateThick = 0.012f, PlateInto = 0.004f;

        public static float HangerTubeRadius(float width) => width <= 2f ? 0.032f : width <= 4f ? 0.045f : 0.057f;

        public static float HangerRodRadius(float width) => width <= 2.5f ? 0.012f : width <= 5f ? 0.016f : 0.020f;

        /// <summary>The soffit a hung board is laid out for (m over the road): the item's reach; without one, the soffit that has the board at the free height with the rods' own length above it.</summary>
        public static float SoffitOf(float reach, SignDef sign) => reach > 0f ? reach : OverRoad.MinFree + sign.Height + sign.ExtraTop + OverRoad.HangLength;

        /// <summary>
        /// The height of a hung board's lower edge for a soffit (OverRoad.HungLowerEdge). A soffit too low for the board (a saved item whose structure was lower than the tool lets one hang under, or a
        /// reach that was edited by hand) leaves it MinHang of rod and is no lower than half a metre: a layout is always made, the tool is what refuses.
        /// </summary>
        private static float HungBottom(float soffit, SignDef sign)
        {
            float total = sign.Height + sign.ExtraTop;
            return Math.Max(0.5f, Math.Min(OverRoad.HungLowerEdge(soffit, total), soffit - total - OverRoad.MinHang));
        }

        /// <summary>The X of the rods along a board of this width (m, from its middle): inset from each end, the pitch between neighbours at most HangerPitch.</summary>
        public static float[] HangerPositions(float width)
        {
            float inset = Math.Max(0.1f, Math.Min(0.35f, 0.1f * width)), half = Math.Max(0.05f, width * 0.5f - inset);
            // The ends count as rods: two at the least, whatever the width.
            int count = (int)Math.Ceiling(2f * half / HangerPitch - 1e-3f) + 1;
            var xs = new float[count];
            for (int i = 0; i < count; i++)
                xs[i] = -half + 2f * half * i / (count - 1);
            return xs;
        }

        /// <summary>
        /// The layout of a board hung under a structure (soffit: the height of its underside over the road): the origin on the axis of the tube the board's channels are clamped to, which passes behind the board
        /// ArmInset under its top as a cantilever's arm does; the rods stand on it and reach the soffit.
        /// </summary>
        private static void OverHung(ref SupportLayout layout, SignDef sign, float soffit)
        {
            float tube = HangerTubeRadius(sign.Width);
            layout.PoleRadius = 0f;
            layout.PoleX = new float[0];
            layout.BoardCentreX = 0f;
            layout.ArmY = layout.HangY = layout.BoardTop - ArmInset(sign.Height);
            layout.ArmRootRadius = layout.ArmTipRadius = layout.HangRadius = tube;
            layout.BackZ = tube + ClampRoom;
            layout.PoleTop = soffit;
            layout.InnerReach = sign.Width * 0.5f;
            layout.Hanger = true;
            layout.HangerTop = soffit;
            layout.HangerXs = HangerPositions(sign.Width);
        }
    }
}
