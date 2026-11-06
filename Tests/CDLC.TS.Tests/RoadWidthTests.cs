using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The real width of a road (0.12.1): the carriageway and its kerbs from the lanes, where a post stands, a gantry's span and a cantilever's arm, and the markings that span the lanes.</summary>
    internal static class RoadWidthTests
    {
        public static void RunAll()
        {
            Program.Run("road width: the carriageway and the footways from the lanes", Carriageways);
            Program.Run("road width: posts by the kerb, gantries from kerb to kerb, arms over the lanes", Supports);
            Program.Run("road width: stop lines and crosswalks span the lanes", Markings);
        }

        private static CrossLane Car(float x, float w = 3f) => new CrossLane(x, w, CrossLaneKind.Carriageway);

        private static CrossLane Foot(float x) => new CrossLane(x, 2f, CrossLaneKind.Pedestrian);

        /// <summary>The city road of the owner's log: 40 m wide, eight car lanes 3 m wide at 2.5 to 11.5 m each side of the centre line, footways beyond.</summary>
        private static List<CrossLane> City() => new List<CrossLane>
        {
            Car(-11.5f), Car(-8.5f), Car(-5.5f), Car(-2.5f), Car(2.5f), Car(5.5f), Car(8.5f), Car(11.5f), Foot(-16.5f), Foot(16.5f), new CrossLane(0f, 0.3f, CrossLaneKind.Other)
        };

        private static void Carriageways()
        {
            Carriageway city = Carriageway.Of(City(), 40f);
            Program.Check(city.Ok, "read");
            Program.Near(city.Left, -13f, 1e-4f, "the left kerb at -13 m");
            Program.Near(city.Right, 13f, 1e-4f, "the right kerb at 13 m");
            Program.Check(city.FootwayLeft && city.FootwayRight, "a footway beyond each");
            Program.Near(city.RoadHalf, 20f, 1e-4f, "half the road");
            Program.Near(city.Kerb(1), 13f, 1e-4f, "the kerb on the right, as a distance");
            Program.Near(city.Kerb(-1), 13f, 1e-4f, "and on the left");

            // A motorway: lanes and shoulders, no footway.
            Carriageway motorway = Carriageway.Of(new List<CrossLane> { Car(-6f, 4f), Car(-2f, 4f), Car(2f, 4f), Car(6f, 4f) }, 21f);
            Program.Check(motorway.Ok && !motorway.FootwayLeft && !motorway.FootwayRight, "a motorway has no footway");
            // A one-sided street: a footway on the right only, a parking lane on the left (a carriageway lane too).
            Carriageway oneSided = Carriageway.Of(new List<CrossLane> { new CrossLane(-5f, 2f, CrossLaneKind.Carriageway), Car(-2.5f), Car(2.5f), Foot(7f) }, 16f);
            Program.Check(oneSided.Ok && !oneSided.FootwayLeft && oneSided.FootwayRight, "a footway on one side");
            Program.Near(oneSided.Left, -6f, 1e-4f, "the parking lane is the carriageway's edge");
            // A footway on a median (between the carriageways) is no kerb of the outside.
            Carriageway median = Carriageway.Of(new List<CrossLane> { Car(-8f), Foot(0f), Car(8f) }, 30f);
            Program.Check(median.Ok && !median.FootwayLeft && !median.FootwayRight, "a footway in the middle is not beyond the carriageway");
            Program.Check(!Carriageway.Of(new List<CrossLane> { Foot(3f) }, 10f).Ok && !Carriageway.Of(null, 10f).Ok, "no car lane: not read");
            Program.Check(!Carriageway.Of(new List<CrossLane> { new CrossLane(float.NaN, 3f, CrossLaneKind.Carriageway), new CrossLane(2f, 0f, CrossLaneKind.Carriageway) }, 10f).Ok, "lanes of no place or no width are left out");
        }

        private static void Supports()
        {
            Carriageway city = Carriageway.Of(City(), 40f);
            // A post with a board that reaches 0.3 m toward the road: its inner edge half a metre behind the kerb: 13 + 0.5 + 0.3.
            Program.Near(city.PostDistance(1, 0.3f, Carriageway.DefaultMargin, 0f), 13.8f, 1e-4f, "a post on a footway: 0.5 m behind the kerb with the board's reach");
            Program.Check(city.PostDistance(1, 0.3f, Carriageway.DefaultMargin, 0f) < 20f - 0.6f, "not 0.6 m inside the whole road (19.4 m), as until 0.12.0");
            Program.Near(city.PostDistance(1, 0.3f, Carriageway.DefaultMargin + 0.4f, 0f), 14.2f, 1e-4f, "the player's margin moves it out by as much");
            Program.Near(city.PostDistance(-1, 0.3f, Carriageway.DefaultMargin, 0f), 13.8f, 1e-4f, "the same on the left");
            // Never beyond where it stood before (a narrow footway).
            Carriageway narrow = Carriageway.Of(new List<CrossLane> { Car(-1.5f), Car(1.5f), Foot(3.5f), Foot(-3.5f) }, 7.5f);
            Program.Near(narrow.PostDistance(1, 0.3f, Carriageway.DefaultMargin, 0f), 3.75f - 0.6f, 1e-4f, "a narrow footway: no farther out than the road's edge less the margin");
            Carriageway motorway = Carriageway.Of(new List<CrossLane> { Car(-6f, 4f), Car(-2f, 4f), Car(2f, 4f), Car(6f, 4f) }, 21f);
            Program.Near(motorway.PostDistance(1, 0.3f, Carriageway.DefaultMargin, 0.2f), 10.5f - 0.6f + 0.2f, 1e-4f, "no footway: as before (the road's edge, the margin, the reach beyond 0.4 m)");
            Program.Near(new Carriageway { RoadHalf = 8f }.PostDistance(1, 0.3f, -0.6f, 0f), 7.4f, 1e-4f, "nothing read: as before");

            // A gantry: from the post on the right to one as far out on the left: 2 x 13.8 = 27.6 m, not the 38.8 of 0.12.0.
            Program.Near(city.GantrySpan(1, 0.3f, Carriageway.DefaultMargin, 0f), 27.6f, 1e-4f, "a gantry kerb to kerb with the clearance");
            Carriageway oneSided = Carriageway.Of(new List<CrossLane> { new CrossLane(-5f, 2f, CrossLaneKind.Carriageway), Car(-2.5f), Car(2.5f), Foot(7f) }, 16f);
            Program.Near(oneSided.GantrySpan(1, 0.3f, Carriageway.DefaultMargin, 0f), (4f + 0.8f) + (8f - 0.6f), 1e-4f, "one-sided: by each side's own rule");
            Program.Near(oneSided.GantrySpan(-1, 0.3f, Carriageway.DefaultMargin, 0f), oneSided.GantrySpan(1, 0.3f, Carriageway.DefaultMargin, 0f), 1e-4f, "the same from either side");

            // A cantilever over the lanes of its traffic: the four lanes at 2.5 to 11.5 m (middle 7), the post at 13.8 m, a board 4 m wide: 6.8 + 2 + 0.45 = 9.25 -> 9.3 (to the 10 cm).
            Program.Near(Carriageway.CantileverArm(13.8f, new[] { 2.5f, 5.5f, 8.5f, 11.5f }, 4f), 9.3f, 1e-4f, "an arm over the middle of the lanes");
            Program.Near(Carriageway.CantileverArm(-13.8f, new[] { -2.5f, -11.5f }, 4f), 9.3f, 1e-4f, "on the left side, the same");
            Program.Near(Carriageway.CantileverArm(30f, new[] { -10f, 10f }, 4f), Carriageway.MaxAutoArm, 1e-4f, "never longer than 12 m");
            Program.Check(Carriageway.CantileverArm(5f, null, 4f) == 0f && Carriageway.CantileverArm(5f, new float[0], 4f) == 0f && Carriageway.CantileverArm(float.NaN, new[] { 1f }, 4f) == 0f, "no lanes: the support's own arm");
        }

        private static void Markings()
        {
            // Two lanes each way 3.5 m wide: a stop line for the traffic with the edge spans its two lanes, from the centre line to the edge: 0 to 7 m, middle 3.5.
            var forward = new List<float> { 1.75f, 5.25f };
            var backward = new List<float> { -1.75f, -5.25f };
            MarkingSpot stop = MarkingSnap.Fit(MarkingAnchor.Block, forward, backward, 4f, out float span, out int lanes);
            Program.Near(span, 7f, 1e-4f, "the stop line spans the two lanes of its traffic");
            Program.Near(stop.Lateral, 3.5f, 1e-4f, "and stands in their middle");
            Program.Check(lanes == 2 && stop.Forward && stop.Snapped, "two lanes, the traffic with the edge");
            MarkingSpot back = MarkingSnap.Fit(MarkingAnchor.Block, forward, backward, -2f, out float backSpan, out int backLanes);
            Program.Check(!back.Forward && Math.Abs(back.Lateral + 3.5f) < 1e-4f && Math.Abs(backSpan - 7f) < 1e-4f && backLanes == 2, "the other way, the other side");
            MarkingSpot walk = MarkingSnap.Fit(MarkingAnchor.Road, forward, backward, 4f, out float walkSpan, out int walkLanes);
            Program.Check(Math.Abs(walkSpan - 14f) < 1e-4f && Math.Abs(walk.Lateral) < 1e-4f && walkLanes == 4, "a crosswalk spans the whole carriageway: " + walkSpan);
            // Three lanes one way and one the other: the stop line of the three ends at the centre line between the directions.
            MarkingSnap.Fit(MarkingAnchor.Block, new List<float> { 1.5f, 4.5f, 7.5f }, new List<float> { -1.5f }, 5f, out float three, out int threeLanes);
            Program.Near(three, 9f, 1e-4f, "three lanes of 3 m: 9 m");
            Program.Check(threeLanes == 3, "three lanes");
            // A median 9 m wide: each direction's lanes end at their own edge, half a lane beyond the lane next to the median.
            MarkingSpot divided = MarkingSnap.Fit(MarkingAnchor.Block, new List<float> { 6f, 9.5f }, new List<float> { -6f, -9.5f }, 7f, out float dividedSpan, out _);
            Program.Near(dividedSpan, 7f, 1e-4f, "with a median: the lanes' own width");
            Program.Near(divided.Lateral, 7.75f, 1e-4f, "in their middle");
            // Lanes of other widths: 2 and 5 m with the edge, -2 against: the lane at 2 reaches down to the centre line at 0 (half its gap below), up to 3.5; the lane at 5 up to 6.5 (half its gap of 3).
            MarkingSpot uneven = MarkingSnap.Fit(MarkingAnchor.Block, new List<float> { 2f, 5f }, new List<float> { -2f }, 3f, out float unevenSpan, out _);
            Program.Near(unevenSpan, 6.5f, 1e-4f, "lanes of other widths: from the centre line to the outer lane's edge");
            Program.Near(uneven.Lateral, 3.25f, 1e-4f, "in the middle of that, not of the lanes' middles (3.5)");
            // Against the edge: lanes at -5 and -2, the other way at 1.5: from -6.5 (half the gap of 3 beyond -5) to the centre line at -0.25.
            MarkingSnap.Fit(MarkingAnchor.Block, new List<float> { 1.5f }, new List<float> { -2f, -5f }, -3f, out float againstSpan, out _);
            Program.Near(againstSpan, 6.25f, 1e-4f, "against the edge: up to the centre line between the directions");
            // One lane alone: a lane's default width.
            MarkingSnap.Fit(MarkingAnchor.Block, new List<float> { 0f }, null, 0f, out float single, out int singleLanes);
            Program.Check(Math.Abs(single - MarkingSnap.DefaultLane) < 1e-4f && singleLanes == 1, "one lane: " + single);
            // No lanes, or a marking that does not span: the spot of Stamp and no span.
            MarkingSnap.Fit(MarkingAnchor.Block, null, null, 2f, out float none, out int noLanes);
            MarkingSnap.Fit(MarkingAnchor.Lane, forward, backward, 2f, out float laneSpan, out _);
            Program.Check(none == 0f && noLanes == 0 && laneSpan == 0f, "no lanes or an arrow: no span");

            // The settings: span and lanes written when fitting is on; nothing changes when it is off or nothing was read.
            GuideTemplate yield = MarkingTemplates.ById("mk.giveway.yield");
            string fitted = MarkingTemplates.WithFit(yield, yield.Default().ToString(), 10.53f, 3);
            Program.Check(yield.Parse(fitted).Get("span") == "10.5" && yield.Parse(fitted).Get("lanes") == "3", "the yield line takes 10.5 m and 3 lanes: " + fitted);
            string off = MarkingTemplates.WithFit(yield, yield.Parse(yield.Default().ToString()).With(MarkingTemplates.FitKey, "0").ToString(), 10.5f, 3);
            Program.Check(yield.Parse(off).Get("span") == "7", "fitting off: its own span");
            Program.Check(yield.Parse(MarkingTemplates.WithFit(yield, yield.Default().ToString(), 0f, 0)).Get("span") == "7", "nothing read: its own span");
            Program.Check(yield.Parse(MarkingTemplates.WithFit(yield, yield.Default().ToString(), 12f, 9)).Get("lanes") == "6", "lanes held to the choices");
            GuideTemplate walkT = MarkingTemplates.ById("mk.crosswalk");
            Program.Check(walkT.Parse(MarkingTemplates.WithFit(walkT, walkT.Default().ToString(), 26f, 8)).Get("span") == "26", "the crosswalk takes the carriageway's width");
            GuideTemplate arrow = MarkingTemplates.ById("mk.arrow.left");
            Program.Check(MarkingTemplates.WithFit(arrow, arrow.Default().ToString(), 7f, 2) == arrow.Parse(arrow.Default().ToString()).ToString(), "a marking with no fitting is as it was");
            foreach (string id in new[] { "mk.stop", "mk.giveway.stop", "mk.giveway.yield", "mk.crosswalk", "mk.bicyclecrossing" })
            {
                GuideTemplate t = MarkingTemplates.ById(id);
                Program.Check(t.FieldOf(MarkingTemplates.FitKey) != null && t.Parse(t.Default().ToString()).Flag(MarkingTemplates.FitKey), id + " fits by default");
                Program.Check(MarkingSnap.AnchorOf(id) == MarkingAnchor.Block || MarkingSnap.AnchorOf(id) == MarkingAnchor.Road, id + " spans lanes");
            }

            // 0.12.1: clearing the game's markings draws nothing by default (the owner cleared to draw his own; the lines drawn again looked as if nothing was cleared).
            GuideTemplate clear = MarkingTemplates.ById(VanillaClear.Id);
            Program.Check(!clear.Parse(clear.Default().ToString()).Flag(VanillaClear.RedrawKey), "clearing does not draw the standard's lines unless asked");
        }
    }
}
