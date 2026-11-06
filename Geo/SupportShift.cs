using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>Which control moves a support that stands across its road: its distance from the kerb, its distance from the road's centre line, or none.</summary>
    public enum ShiftAcross
    {
        None,
        Kerb,
        Centre
    }

    /// <summary>
    /// The position controls of the editor for a support that stands (0.12.85; the owner: "在编辑模式下对应立架的位置也应该支持调整": sliders and boxes for along and across, and for the arms, the gantries and
    /// the attached supports too): which support gets which control, and the numbers they work in. The step buttons (PlacementTool.MoveStep) stay as they were. A support on a lamp post or a signal
    /// pole of the game, on another mod's arm, or on a structure over the road is tied to what carries it and is not moved by these. Pure.
    /// </summary>
    public static class SupportShift
    {
        /// <summary>The slider of the position along a road or a wall goes this far (m) each way from where the support stood when it was picked; a number typed in may go to AlongLimit.</summary>
        public const float AlongWindow = 50f, AlongLimit = 500f;

        /// <summary>The step of the sliders and the boxes (m).</summary>
        public const float Step = 0.1f;

        /// <summary>The slider of the distance from the centre line goes this far (m) past the road's half width: the margin's own range beyond the kerb (KerbMargin.Max).</summary>
        public const float CentrePast = KerbMargin.Max;

        /// <summary>A support that stands on the ground by a road: a post, an arm, a gantry.</summary>
        public static bool Ground(SupportKind kind) =>
            kind == SupportKind.SinglePole || kind == SupportKind.DoublePole || kind == SupportKind.TriplePole || kind == SupportKind.Cantilever || kind == SupportKind.FCantilever
            || kind == SupportKind.DoubleCantilever || kind == SupportKind.Gantry;

        /// <summary>Whether the editor offers the position along the road (a support on the ground) or along the wall (a board on a wall).</summary>
        public static bool Along(SupportKind kind) => Ground(kind) || SupportKinds.OnWall(kind);

        /// <summary>Whether the editor offers the lower edge of the board by height: a board on a wall (the other supports have a clearance of their own, SpanSlider).</summary>
        public static bool Height(SupportKind kind) => SupportKinds.OnWall(kind);

        /// <summary>
        /// The control that moves the support across its road. The distance from the kerb (KerbMargin) when the support stands by a kerb and the margin it stands at can be worked out; else, for any support
        /// on the ground, the distance from the centre line (a double cantilever's post stands in the traffic, a post may stand in the road or on a strip); none for the others.
        /// </summary>
        public static ShiftAcross Across(SupportKind kind, bool marginKnown) => !Ground(kind) ? ShiftAcross.None : KerbMargin.Edits(kind) && marginKnown ? ShiftAcross.Kerb : ShiftAcross.Centre;

        /// <summary>A number in whole tenths of a metre, so that a thousand moves never drift.</summary>
        public static float Tenths(float value) => (float)Math.Round(value * 10.0, MidpointRounding.AwayFromZero) / 10f;

        /// <summary>The position along asked for: whole tenths within AlongLimit; not a number is not a position.</summary>
        public static float ClampAlong(float value) => float.IsNaN(value) || float.IsInfinity(value) ? float.NaN : Tenths(Math.Max(-AlongLimit, Math.Min(AlongLimit, value)));

        /// <summary>How far (m) to move from the position the support shows to the one asked for.</summary>
        public static float AlongDelta(float target, float shown)
        {
            float asked = ClampAlong(target);
            return float.IsNaN(asked) || float.IsNaN(shown) ? 0f : asked - shown;
        }

        /// <summary>+1 when a support that faces yaw (radians; its facing is (sin, cos) in the ground plane) faces the way the road runs (heading), -1 against it: what "+ toward the traffic" means for the buttons, the slider and the box alike.</summary>
        public static float Sense(float yaw, float headingX, float headingZ) => (float)Math.Sin(yaw) * headingX + (float)Math.Cos(yaw) * headingZ >= 0f ? 1f : -1f;

        /// <summary>The position along a road: how far (m) the support is from where it stood, by its place on the road's length (arc), + toward the traffic that sees it (sense +1) or the other way (-1).</summary>
        public static float Displacement(float arc, float arcAtPick, float sense) => (sense >= 0f ? 1f : -1f) * (arc - arcAtPick);

        /// <summary>The position along a wall: how far (m) the support is from where it stood, measured on the wall's direction.</summary>
        public static float Displacement(Vec3 position, Vec3 atPick, Vec3 along) => (position.X - atPick.X) * along.X + (position.Z - atPick.Z) * along.Z;

        /// <summary>The furthest the slider of the distance from the centre line goes (m): the road's half width and CentrePast, to a tenth.</summary>
        public static float CentreMax(float roadHalf) => float.IsNaN(roadHalf) || roadHalf < 0f ? CentrePast : Tenths(roadHalf + CentrePast);

        /// <summary>The distance from the centre line asked for: whole tenths from 0 to CentreMax; not a number is not a distance.</summary>
        public static float ClampCentre(float value, float roadHalf) => float.IsNaN(value) || float.IsInfinity(value) ? float.NaN : Tenths(Math.Max(0f, Math.Min(CentreMax(roadHalf), value)));

        /// <summary>How far (m, + away from the centre line) to move from the distance the support stands at to the one asked for.</summary>
        public static float CentreDelta(float target, float distance, float roadHalf)
        {
            float asked = ClampCentre(target, roadHalf);
            return float.IsNaN(asked) || float.IsNaN(distance) ? 0f : asked - distance;
        }

        /// <summary>The lower edge of a board on a wall asked for: whole tenths between the least and the most a board is put at; not a number is not a height.</summary>
        public static float ClampHeight(float value) => float.IsNaN(value) || float.IsInfinity(value) ? float.NaN : Tenths(Math.Max(WallMount.MinHeight, Math.Min(WallHeight.Ceiling, value)));

        /// <summary>How far (m, + up) to move a board on a wall from the lower edge it has to the one asked for.</summary>
        public static float HeightDelta(float target, float lower)
        {
            float asked = ClampHeight(target);
            return float.IsNaN(asked) || float.IsNaN(lower) ? 0f : asked - lower;
        }

        /// <summary>
        /// The span of a gantry whose near post is moved to the margin: as placing would have made it at that margin (the near post and the far one each by its own kerb, Carriageway.GantrySpan), within the
        /// least and the most a span has, to a tenth. A span the player set by hand is not this one (PlacementTool keeps it).
        /// </summary>
        public static float SpanAtMargin(Carriageway carriageway, int side, float innerReach, float margin, float reachOut) =>
            Tenths(Math.Max(SupportLayouts.MinSpan, Math.Min(SupportLayouts.MaxSpan, carriageway.GantrySpan(side, innerReach, margin, reachOut))));
    }
}
