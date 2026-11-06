using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The turn of a board as the player sets it (the owner, 3 Nov: "旋转角度调节应该支持滑条无级调节和按键步进调节"): the steps of [ and ], the arithmetic of a step, of a slid or typed angle, and the
    /// angle the panel shows. The turn is a float of radians kept as it always was (TsItem.Turn of a board on its post, the tool's rotation of the sign about to be placed), so what is stored does not
    /// change: a step of 15 degrees is the same float as before, an angle of the slider is the same kind of float. A facing is shown in tenths of a degree in [-180, 180): half a turn is -180, as the
    /// button "turn round" has always stored it (adding 180 degrees to a float comes out a hair past pi, which the wrap takes to the other side). Pure.
    /// </summary>
    public static class TurnAdjust
    {
        /// <summary>UnityEngine.Mathf.Deg2Rad, written as Unity writes it: the turns the tool stored were made with that float.</summary>
        public const float Deg2Rad = (float)Math.PI * 2F / 360F;

        /// <summary>The degrees of a press of [ or ]: 15, with Shift 5, with Alt 1 (Alt first: both held is the finest).</summary>
        public const int CoarseDegrees = 15, FineDegrees = 5, FinestDegrees = 1;

        /// <summary>A step in radians, as the tool's rotation of the sign about to be placed adds it.</summary>
        public const float CoarseStep = 15f * Deg2Rad, FineStep = 5f * Deg2Rad, FinestStep = 1f * Deg2Rad;

        /// <summary>The slider: a facing from half a turn back to the last tenth before half a turn forward (the two ends are one facing; the left one is it), in tenths.</summary>
        public const float SliderMin = -180f, SliderMax = 179.9f, SliderStep = 0.1f;

        public static int StepDegrees(bool shift, bool alt) => alt ? FinestDegrees : shift ? FineDegrees : CoarseDegrees;

        /// <summary>The step of the sign being placed, in radians (a press adds or takes it from the rotation, which is not wrapped: the sign it makes is the same).</summary>
        public static float StepRadians(bool shift, bool alt) => alt ? FinestStep : shift ? FineStep : CoarseStep;

        /// <summary>The turn of a board on its post after a step of degrees (+ clockwise seen from above; 0 is "as its support faces" again), wrapped, and 0 when it is back where it began.</summary>
        public static float After(float turn, int degrees)
        {
            float next = degrees == 0 ? 0f : Assembly.WrapTurn(turn + degrees * Deg2Rad);
            return Math.Abs(next) < 0.05f * Deg2Rad ? 0f : next;
        }

        /// <summary>
        /// The turn (radians) of an angle in degrees as the slider or the box gives it: to a tenth, any number is a facing (200 is -160, 180.1 is -179.9); NaN when it is not a number. Half a turn is the
        /// float that "turn round" stores.
        /// </summary>
        public static float Absolute(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees))
                return float.NaN;
            double tenths = Math.Round(degrees * 10.0, MidpointRounding.AwayFromZero) / 10.0;
            tenths -= 360.0 * Math.Floor((tenths + 180.0) / 360.0);
            if (tenths <= -180.0)
                tenths = 180.0;
            float turn = Assembly.WrapTurn((float)tenths * Deg2Rad);
            return turn == 0f ? 0f : turn;
        }

        /// <summary>The facing the panel shows for a turn: degrees in [-180, 180), to a tenth (-0 is 0).</summary>
        public static float Shown(float turn)
        {
            if (float.IsNaN(turn) || float.IsInfinity(turn))
                return 0f;
            double degrees = Math.Round(turn * 180.0 / Math.PI * 10.0, MidpointRounding.AwayFromZero) / 10.0;
            degrees -= 360.0 * Math.Floor((degrees + 180.0) / 360.0);
            return degrees == 0.0 ? 0f : (float)degrees;
        }

        /// <summary>
        /// Whether the keys and the row of the turn act on the sign about to be placed on this support: every road path takes the rotation, a roof does, a wall's flat board and bracket take only the
        /// side they face and a signal's arm the face it is on (PlacementTool.Wall, PlacementTool.Arm).
        /// </summary>
        public static bool PlaceApplies(bool onWall, bool onArm, bool roof) => roof || (!onWall && !onArm);

        /// <summary>As above for the sign chosen and the support chosen (null: the planner's, which is a road support): a road marking is turned by the keys alone, its paths differ.</summary>
        public static bool PlaceApplies(SignDef sign, SupportDef support) =>
            sign != null && !sign.IsMarking && (support == null || PlaceApplies(SupportKinds.OnWall(support.Kind), SupportKinds.OnArm(support.Kind), support.Roof));
    }
}
