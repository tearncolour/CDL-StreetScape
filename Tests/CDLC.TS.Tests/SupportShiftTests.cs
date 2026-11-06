using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The position controls of the editor for a support that stands (0.12.85; the owner: "在编辑模式下对应立架的位置也应该支持调整"): which support gets which control, and the numbers they work in.</summary>
    internal static class SupportShiftTests
    {
        public static void RunAll()
        {
            Program.Run("support shift: which support gets which control", Controls);
            Program.Run("support shift: the supports the kerb distance moves", Kerb);
            Program.Run("support shift: the position along a road and a wall", Along);
            Program.Run("support shift: the distance from the centre line", Centre);
            Program.Run("support shift: the lower edge of a board on a wall", Lower);
            Program.Run("support shift: a gantry's span at a margin", Span);
        }

        private static CrossLane Car(float x) => new CrossLane(x, 3f, CrossLaneKind.Carriageway);

        private static CrossLane Foot(float x) => new CrossLane(x, 2f, CrossLaneKind.Pedestrian);

        /// <summary>The city road of the owner's log (RoadWidthTests): 40 m wide, eight car lanes, footways beyond; a post stands 13.8 m from the centre line.</summary>
        private static Carriageway City() => Carriageway.Of(new List<CrossLane>
        {
            Car(-11.5f), Car(-8.5f), Car(-5.5f), Car(-2.5f), Car(2.5f), Car(5.5f), Car(8.5f), Car(11.5f), Foot(-16.5f), Foot(16.5f)
        }, 40f);

        private static void Controls()
        {
            // Every kind is classified here: a support appended later fails this until someone says what the editor does for it.
            var ground = new HashSet<SupportKind> { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.TriplePole, SupportKind.Cantilever, SupportKind.FCantilever, SupportKind.DoubleCantilever, SupportKind.Gantry };
            var walls = new HashSet<SupportKind> { SupportKind.WallFlat, SupportKind.WallBracket };
            var tied = new HashSet<SupportKind> { SupportKind.Attached, SupportKind.AttachedArm, SupportKind.OverRoad };
            foreach (SupportKind kind in Enum.GetValues(typeof(SupportKind)))
                Program.Check(ground.Contains(kind) || walls.Contains(kind) || tied.Contains(kind), kind + ": classified (a new support says here what the position controls are)");
            foreach (SupportKind kind in ground)
            {
                Program.Check(SupportShift.Ground(kind) && SupportShift.Along(kind) && !SupportShift.Height(kind), kind + ": a support on the ground has the position along and no lower edge");
                Program.Check(SupportShift.Across(kind, true) != ShiftAcross.None && SupportShift.Across(kind, false) == ShiftAcross.Centre, kind + ": across by the margin when it is known, else by the distance from the centre line");
            }

            foreach (SupportKind kind in new[] { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.TriplePole, SupportKind.Cantilever, SupportKind.FCantilever, SupportKind.Gantry })
                Program.Check(SupportShift.Across(kind, true) == ShiftAcross.Kerb, kind + ": the post stands at a margin from the kerb");
            Program.Check(SupportShift.Across(SupportKind.DoubleCantilever, true) == ShiftAcross.Centre, "a double cantilever's post stands in the traffic: the distance from the centre line, even where a margin could be worked out");
            foreach (SupportKind kind in walls)
            {
                Program.Check(!SupportShift.Ground(kind) && SupportShift.Along(kind) && SupportShift.Height(kind) && SupportShift.Across(kind, true) == ShiftAcross.None, kind + ": along the wall and the lower edge, nothing across");
            }

            foreach (SupportKind kind in tied)
                Program.Check(!SupportShift.Ground(kind) && !SupportShift.Along(kind) && !SupportShift.Height(kind) && SupportShift.Across(kind, true) == ShiftAcross.None && SupportShift.Across(kind, false) == ShiftAcross.None,
                    kind + ": fixed to a host or to a structure: the sliders and boxes do not move it off");
        }

        private static void Kerb()
        {
            foreach (SupportKind kind in new[] { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.TriplePole, SupportKind.Cantilever, SupportKind.FCantilever, SupportKind.Gantry })
                Program.Check(KerbMargin.Edits(kind), kind + ": the editor moves its post by the margin");
            foreach (SupportKind kind in new[] { SupportKind.DoubleCantilever, SupportKind.Attached, SupportKind.AttachedArm, SupportKind.OverRoad, SupportKind.WallFlat, SupportKind.WallBracket })
                Program.Check(!KerbMargin.Edits(kind), kind + ": not by the margin");
            // What the "/" key puts in the road is still the posts only: the arms and the gantry are not put across a road by hand.
            foreach (SupportKind kind in Enum.GetValues(typeof(SupportKind)))
                Program.Check(MidRoad.Takes(kind) == (kind == SupportKind.SinglePole || kind == SupportKind.DoublePole || kind == SupportKind.TriplePole), kind + ": the mid-road mode is for the posts");
        }

        private static void Along()
        {
            Program.Check(SupportShift.AlongWindow == 50f && SupportShift.AlongLimit == 500f && SupportShift.Step == 0.1f, "the slider goes 50 m each way, the box 500, in tenths (the panel's alongRange says the same)");
            Program.Near(SupportShift.ClampAlong(12.34f), 12.3f, 1e-5f, "whole tenths");
            // 0.25 and 1.25 are exact in a float: a true half (to even they would be 0.2 and 1.2).
            Program.Near(SupportShift.ClampAlong(0.25f), 0.3f, 1e-5f, "a half rounds away from zero");
            Program.Near(SupportShift.ClampAlong(1.25f), 1.3f, 1e-5f, "a half past 1 too");
            Program.Near(SupportShift.ClampAlong(-0.25f), -0.3f, 1e-5f, "on either side");
            Program.Near(SupportShift.ClampAlong(900f), 500f, 1e-5f, "a typed number goes no further than 500 m");
            Program.Near(SupportShift.ClampAlong(-900f), -500f, 1e-5f, "either way");
            Program.Check(float.IsNaN(SupportShift.ClampAlong(float.NaN)) && float.IsNaN(SupportShift.ClampAlong(float.PositiveInfinity)), "not a number is not a position");
            Program.Near(SupportShift.AlongDelta(7.5f, 2f), 5.5f, 1e-5f, "from the position shown to the one asked for");
            Program.Near(SupportShift.AlongDelta(-3f, 4.2f), -7.2f, 1e-5f, "back past where it stood");
            Program.Near(SupportShift.AlongDelta(7.5f, 7.5f), 0f, 1e-6f, "where it is: no move");
            Program.Near(SupportShift.AlongDelta(float.NaN, 3f), 0f, 1e-6f, "a text that is no number moves nothing");
            Program.Near(SupportShift.AlongDelta(3f, float.NaN), 0f, 1e-6f, "nor does a position that cannot be read");
            Program.Near(SupportShift.AlongDelta(900f, 0f), 500f, 1e-5f, "the box is held at its limit");

            // On a road: the sign faces the way the road runs (+1) or against it (-1); the buttons' "+ toward the traffic" is the sign's direction.
            Program.Near(SupportShift.Displacement(130f, 100f, 1f), 30f, 1e-5f, "along the road's direction: the arc grew");
            Program.Near(SupportShift.Displacement(130f, 100f, -1f), -30f, 1e-5f, "against it: the same step on the road is the other way for the sign");
            Program.Near(SupportShift.Displacement(70f, 100f, -1f), 30f, 1e-5f, "against it, back along the road is forward for the sign");
            Program.Near(SupportShift.Displacement(100f, 100f, 1f), 0f, 1e-6f, "not moved: 0");
            // Which way is "toward the traffic": the sign faces (sin yaw, cos yaw); the road runs along its heading.
            Program.Near(SupportShift.Sense(0f, 0f, 1f), 1f, 0f, "facing +Z on a road that runs +Z: with the road");
            Program.Near(SupportShift.Sense(0f, 0f, -1f), -1f, 0f, "against it");
            Program.Near(SupportShift.Sense((float)Math.PI * 0.5f, 1f, 0f), 1f, 0f, "facing +X on a road that runs +X");
            Program.Near(SupportShift.Sense((float)Math.PI, 0f, 1f), -1f, 0f, "turned round: against it");
            Program.Near(SupportShift.Sense(0f, 1f, 0f), 1f, 0f, "square across the road: counted with it, as the move buttons always did");
            // The box and the slider ask for a position, the move goes by (sense x metres) along the road: the position read afterwards is the one asked for, whichever way the sign faces.
            foreach (float sense in new[] { 1f, -1f })
            {
                float arcAtPick = 100f, shown = 0f, target = 7.5f;
                float delta = SupportShift.AlongDelta(target, shown);
                Program.Near(SupportShift.Displacement(arcAtPick + sense * delta, arcAtPick, sense), target, 1e-5f, "a move to a position reads back as that position (sense " + sense + ")");
                shown = SupportShift.Displacement(arcAtPick + sense * delta, arcAtPick, sense);
                Program.Near(SupportShift.Displacement(arcAtPick + sense * (delta + SupportShift.AlongDelta(-2f, shown)), arcAtPick, sense), -2f, 1e-5f, "and a second move from there (sense " + sense + ")");
            }

            // On a wall: the wall's direction.
            var along = new Vec3(0f, 0f, 1f);
            Program.Near(SupportShift.Displacement(new Vec3(5f, 9f, 12f), new Vec3(5f, 9f, 4f), along), 8f, 1e-5f, "the displacement is measured on the wall's direction");
            Program.Near(SupportShift.Displacement(new Vec3(8f, 9f, 4f), new Vec3(5f, 9f, 4f), along), 0f, 1e-5f, "a move off the wall's direction is not along it");
            Program.Near(SupportShift.Displacement(new Vec3(5f, 20f, 4f), new Vec3(5f, 9f, 4f), along), 0f, 1e-5f, "nor is a change of height");
        }

        private static void Centre()
        {
            Program.Near(SupportShift.CentreMax(8f), 14f, 1e-5f, "the slider goes the margin's range past the road's half width");
            Program.Near(SupportShift.CentreMax(20.04f), 26f, 1e-5f, "to a tenth");
            Program.Near(SupportShift.CentreMax(float.NaN), SupportShift.CentrePast, 1e-5f, "a road of no width: the range alone");
            Program.Near(SupportShift.CentreMax(-3f), SupportShift.CentrePast, 1e-5f, "a negative half width is none");
            Program.Near(SupportShift.ClampCentre(5.55f, 8f), 5.6f, 1e-5f, "whole tenths");
            Program.Near(SupportShift.ClampCentre(-2f, 8f), 0f, 1e-5f, "never past the centre line: the support keeps its side");
            Program.Near(SupportShift.ClampCentre(40f, 8f), 14f, 1e-5f, "never past the slider's end");
            Program.Check(float.IsNaN(SupportShift.ClampCentre(float.NaN, 8f)), "not a number is not a distance");
            Program.Near(SupportShift.CentreDelta(10f, 7f, 8f), 3f, 1e-5f, "away from the centre line is positive");
            Program.Near(SupportShift.CentreDelta(2f, 7f, 8f), -5f, 1e-5f, "toward it is negative");
            Program.Near(SupportShift.CentreDelta(-5f, 3f, 8f), -3f, 1e-5f, "a negative number comes to the centre line");
            Program.Near(SupportShift.CentreDelta(float.NaN, 3f, 8f), 0f, 1e-6f, "a text that is no number moves nothing");
            Program.Near(SupportShift.CentreDelta(3f, float.NaN, 8f), 0f, 1e-6f, "nor does a distance that cannot be read");
        }

        private static void Lower()
        {
            Program.Near(SupportShift.ClampHeight(0f), WallMount.MinHeight, 1e-5f, "never below the least a board is put at");
            Program.Near(SupportShift.ClampHeight(900f), WallHeight.Ceiling, 1e-5f, "never above the ceiling (a tower: the box takes what the building allows)");
            Program.Near(SupportShift.ClampHeight(3.04f), 3f, 1e-5f, "whole tenths");
            Program.Check(float.IsNaN(SupportShift.ClampHeight(float.NaN)), "not a number is not a height");
            Program.Near(SupportShift.HeightDelta(4.5f, 2.5f), 2f, 1e-5f, "up from the lower edge it has");
            Program.Near(SupportShift.HeightDelta(1f, 2.5f), -1.5f, 1e-5f, "down");
            Program.Near(SupportShift.HeightDelta(150f, 2.5f), 147.5f, 1e-4f, "a typed 150 m is climbed in one move");
            Program.Near(SupportShift.HeightDelta(float.NaN, 2.5f), 0f, 1e-6f, "a text that is no number moves nothing");
        }

        private static void Span()
        {
            Carriageway city = City();
            float reachOut = 0f, inner = 0.3f;
            Program.Near(SupportShift.SpanAtMargin(city, 1, inner, Carriageway.DefaultMargin, reachOut), 27.6f, 1e-4f, "at the default margin: kerb to kerb with the clearance, as placing makes it");
            Program.Near(SupportShift.SpanAtMargin(city, 1, inner, Carriageway.DefaultMargin + 1f, reachOut), 29.6f, 1e-4f, "a metre out: both posts a metre out: two metres more");
            Program.Near(SupportShift.SpanAtMargin(city, -1, inner, Carriageway.DefaultMargin + 1f, reachOut), 29.6f, 1e-4f, "whichever side the near post is on");
            // The near post at 14.8 m and the span 29.6 m: the far post stands at -14.8, as far out as the near one, as placing at that margin would put it.
            float near = city.PostDistance(1, inner, Carriageway.DefaultMargin + 1f, reachOut);
            Program.Near(near - SupportShift.SpanAtMargin(city, 1, inner, Carriageway.DefaultMargin + 1f, reachOut), -city.PostDistance(-1, inner, Carriageway.DefaultMargin + 1f, reachOut), 1e-4f, "the far post is as far out on its side");
            Program.Near(SupportShift.SpanAtMargin(new Carriageway { RoadHalf = 2f }, 1, inner, -0.6f, reachOut), SupportLayouts.MinSpan, 1e-4f, "never shorter than the shortest span");
            Program.Near(SupportShift.SpanAtMargin(new Carriageway { RoadHalf = 60f }, 1, inner, 6f, reachOut), SupportLayouts.MaxSpan, 1e-4f, "never longer than the longest");
            Program.Near(SupportShift.SpanAtMargin(new Carriageway { RoadHalf = 10f }, 1, inner, -0.6f, reachOut), 18.8f, 1e-4f, "a road whose lanes were not read: its half width and the margin, as before");
        }
    }
}
