using System;

namespace CDLC.TS.Geo
{
    public static partial class SupportLayouts
    {
        /// <summary>
        /// A height-limit frame's sizes (0.12.78; the owner's photo of a tunnel entrance's frame, which GB 5768.2 does not draw, so all of them are this mod's estimates from it): a column is two tubes
        /// φ90 a LimitColumnGap apart across the road joined by rungs φ40 every LimitRungPitch; the beam's chords φ60 and its Warren diagonals φ40 stand in front of the columns, reach LimitOverhang
        /// beyond their outer tubes, and the columns end LimitPostAbove over the beam.
        /// </summary>
        public const float LimitColumnRadius = 0.045f, LimitColumnGap = 0.18f, LimitRungRadius = 0.02f, LimitRungPitch = 0.55f, LimitChordRadius = 0.03f, LimitWebRadius = 0.02f, LimitOverhang = 0.25f, LimitPostAbove = 0.12f;

        /// <summary>The depth of the light beam on a span (m): 0.4 m at the shortest span, 0.6 m from 20 m, in a straight line between.</summary>
        public static float LimitDepth(float span) => 0.4f + 0.2f * Math.Max(0f, Math.Min(1f, (span - MinSpan) / (20f - MinSpan)));

        /// <summary>The length of a Warren bay along the beam for a depth (m): a diagonal at 36 degrees to the chords, the photo's triangles.</summary>
        public static float LimitBay(float depth) => 1.4f * depth;

        /// <summary>The z of the columns' axes: behind the beam, their tubes touching the chords' (4 mm into them).</summary>
        public const float LimitColumnZ = -(LimitColumnRadius + LimitChordRadius - 0.004f);

        /// <summary>The plate under a column's two tubes: 0.39 by 0.21 m, 16 mm, four bolts, no ribs (none are in the photo).</summary>
        public static BasePlate LimitBase()
        {
            float hx = LimitColumnGap * 0.5f + LimitColumnRadius + 0.06f, hz = LimitColumnRadius + 0.06f;
            return new BasePlate
            {
                HalfX = hx, HalfZ = hz, Thick = 0.016f, LowerThick = 0.016f, BoltRadius = 0.008f, BoltAbove = 0.04f,
                BoltX = new[] { -(hx - 0.03f), hx - 0.03f, -(hx - 0.03f), hx - 0.03f }, BoltZ = new[] { -(hz - 0.03f), -(hz - 0.03f), hz - 0.03f, hz - 0.03f },
                RibHigh = 0.1f, RibOut = 0.05f, RibTop = 0.02f, RibEnd = 0.02f, RibThick = 0.01f, RibAngles = new float[0]
            };
        }

        /// <summary>
        /// The layout of a height-limit frame over what the gantry's layout made of the span and the board (SupportDef.HeightLimit): the beam's lower chord has its underside at the clearance, the
        /// board hangs in front with its lower edge there too, and the columns take no small boards (no room on them).
        /// </summary>
        private static void LimitFrame(ref SupportLayout layout)
        {
            float depth = LimitDepth(layout.ArmLength), low = layout.BottomClearance + LimitChordRadius;
            layout.HeightLimit = true;
            layout.PoleRadius = LimitColumnRadius;
            layout.BeamDepth = depth;
            layout.BeamWidth = 0f;
            layout.ChordRadius = LimitChordRadius;
            layout.ChordYs = new[] { low, low + depth };
            layout.ArmY = low + depth * 0.5f;
            layout.HangY = low;
            layout.HangRadius = LimitChordRadius;
            layout.PoleTop = low + depth + LimitChordRadius + LimitPostAbove;
            layout.BackZ = LimitChordRadius + ClampRoom;
            layout.Base = LimitBase();
            layout.InnerReach = layout.Base.HalfX + PedestalMargin;
            layout.PostClearance = SupportCatalog.RoadsideClearance;
            layout.PostTop = layout.PostClearance;
            layout.PostBackZ = LimitColumnRadius + PlateDepth + RailDepth;
        }
    }
}
