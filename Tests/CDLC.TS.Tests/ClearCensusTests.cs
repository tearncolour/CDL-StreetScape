using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The census of the game's markings near the clearing areas (0.12.83): what each lane is classified as, what the line says, and when a census is due.</summary>
    internal static class ClearCensusTests
    {
        public static void RunAll()
        {
            Program.Run("census: a lane an area takes is Hidden with its tag and Missed without it", TakenLanes);
            Program.Run("census: a tag no area asks for is another mod's or left over", Tags);
            Program.Run("census: why a visible lane is left, by the first area that holds its middle", LeftByRules);
            Program.Run("census: a lane with only a part inside is partly in; with none, outside", PartlyAndOutside);
            Program.Run("census: the tally counts every lane and names few of each kind", TallyCounts);
            Program.Run("census: the line says the counts, the repair and the samples", Line);
            Program.Run("census: the load's census is due once, and again after ten seconds", ScheduleLoad);
            Program.Run("census: after a batch of Updated one a second later, never twice at a time, and a few a load", ScheduleUpdated);
            Program.Run("census: asked once however often, the fallback when the hold does not ask, a new load starts over", ScheduleAskedAndFallback);
            Program.Run("census: the file is looked for every two seconds", FileCadence);
        }

        private static ClearRegion Square(float min, float max, ClearScope scope = ClearScope.All, bool keepCrossings = false) =>
            new ClearRegion(new List<Vec3> { new Vec3(min, 0f, min), new Vec3(max, 0f, min), new Vec3(max, 0f, max), new Vec3(min, 0f, max) }, scope, keepCrossings);

        private static VanillaMarking At(float x, float z, MarkingOwner owner = MarkingOwner.Edge, bool crossing = false, float y = 0f) =>
            new VanillaMarking { X = x, Y = y, Z = z, Owner = owner, Crossing = crossing };

        private static ClearWhy Of(IList<ClearRegion> regions, VanillaMarking marking, bool hidden = false, bool others = false, params Vec3[] samples) =>
            ClearCensus.Classify(regions, marking, samples, hidden, others);

        private static void TakenLanes()
        {
            var regions = new List<ClearRegion> { Square(0f, 10f) };
            Program.Check(Of(regions, At(5f, 5f), hidden: true) == ClearWhy.Hidden, "taken and tagged: hidden");
            Program.Check(Of(regions, At(5f, 5f), hidden: false) == ClearWhy.Missed, "taken and not tagged: a miss of the mod");
            Program.Check(Of(regions, At(5f, 5f), hidden: true, others: true) == ClearWhy.Hidden, "taken and tagged by another mod too: still hidden");
            Program.Check(Of(null, At(5f, 5f)) == ClearWhy.Outside, "no areas: outside");
        }

        private static void Tags()
        {
            var regions = new List<ClearRegion> { Square(0f, 10f) };
            Program.Check(Of(regions, At(50f, 50f), hidden: true, others: true) == ClearWhy.KeptByOther, "tagged, no area takes it, another mod asks: kept by it");
            Program.Check(Of(regions, At(50f, 50f), hidden: true, others: false) == ClearWhy.TagStale, "tagged, no area and no other mod: a tag left over");
            Program.Check(Of(regions, At(5f, 5f, MarkingOwner.Other), hidden: true) == ClearWhy.TagStale, "tagged, inside an area that does not take it (a lot's): left over");
        }

        private static void LeftByRules()
        {
            var edges = new List<ClearRegion> { Square(0f, 10f, ClearScope.Edges) };
            var nodes = new List<ClearRegion> { Square(0f, 10f, ClearScope.Nodes) };
            var keeps = new List<ClearRegion> { Square(0f, 10f, ClearScope.All, true) };
            Program.Check(Of(edges, At(5f, 5f, MarkingOwner.Other)) == ClearWhy.NotOwned, "a lot's marking inside: no road");
            Program.Check(Of(edges, At(5f, 5f, MarkingOwner.Node)) == ClearWhy.OtherScope, "a junction's marking in an area of segments");
            Program.Check(Of(nodes, At(5f, 5f, MarkingOwner.Edge)) == ClearWhy.OtherScope, "a segment's marking in an area of junctions");
            Program.Check(Of(keeps, At(5f, 5f, MarkingOwner.Edge, crossing: true)) == ClearWhy.Crossing, "a line across the traffic that the area keeps");
            Program.Check(Of(keeps, At(5f, 5f, MarkingOwner.Edge, crossing: false)) == ClearWhy.Missed, "the same area takes a line along the traffic");
            var takes = new List<ClearRegion> { Square(0f, 10f, ClearScope.All, false) };
            Program.Check(Of(takes, At(5f, 5f, MarkingOwner.Edge, crossing: true, y: 3f)) == ClearWhy.OffSurface, "an area that takes the lines across leaves one on another deck for the deck, not for being a crossing");
            Program.Check(Of(takes, At(5f, 5f, MarkingOwner.Edge, crossing: true)) == ClearWhy.Missed, "and takes one on its own surface");
            Program.Check(Of(edges, At(5f, 5f, MarkingOwner.Edge, y: 3f)) == ClearWhy.OffSurface, "inside in the plan, 3 m above the area's surface: another deck");
            Program.Check(Of(edges, At(5f, 5f, MarkingOwner.Edge, y: 1.4f)) == ClearWhy.Missed, "1.4 m is still the area's surface");
            var two = new List<ClearRegion> { Square(0f, 10f, ClearScope.Nodes), Square(0f, 10f, ClearScope.Edges) };
            Program.Check(Of(two, At(5f, 5f, MarkingOwner.Edge)) == ClearWhy.Missed, "taken by the second area: not left by the first one's scope");
        }

        private static void PartlyAndOutside()
        {
            var regions = new List<ClearRegion> { Square(0f, 10f) };
            Program.Check(Of(regions, At(20f, 5f), samples: new[] { new Vec3(8f, 0f, 5f), new Vec3(32f, 0f, 5f) }) == ClearWhy.PartlyIn, "a long line whose end is inside and whose middle is not");
            Program.Check(Of(regions, At(20f, 5f), samples: new[] { new Vec3(15f, 0f, 5f), new Vec3(25f, 0f, 5f) }) == ClearWhy.Outside, "no point of it inside");
            var edges = new List<ClearRegion> { Square(0f, 10f, ClearScope.Edges) };
            Program.Check(Of(edges, At(20f, 5f, MarkingOwner.Node), samples: new[] { new Vec3(8f, 0f, 5f) }) == ClearWhy.Outside, "a part inside an area that would not take it is not 'partly in'");
            Program.Check(Of(regions, At(20f, 5f), samples: new[] { new Vec3(8f, 4f, 5f) }) == ClearWhy.Outside, "a part inside in the plan but on another deck is not 'partly in'");
            var keeps = new List<ClearRegion> { Square(0f, 10f, ClearScope.All, true) };
            Program.Check(Of(keeps, At(20f, 5f, MarkingOwner.Edge, crossing: true), samples: new[] { new Vec3(8f, 0f, 5f) }) == ClearWhy.Outside, "a part of a line across inside an area that keeps those is not 'partly in'");
            Program.Check(Of(keeps, At(20f, 5f, MarkingOwner.Edge, crossing: false), samples: new[] { new Vec3(8f, 0f, 5f) }) == ClearWhy.PartlyIn, "a part of a line along the traffic is");
        }

        private static void TallyCounts()
        {
            var tally = new ClearCensus.Tally();
            int asked = 0;
            for (int i = 0; i < 10; i++)
                tally.Add(ClearWhy.Missed, () => "m" + asked++);
            for (int i = 0; i < 5; i++)
                tally.Add(ClearWhy.PartlyIn, () => "p");
            for (int i = 0; i < 5; i++)
                tally.Add(ClearWhy.Hidden, () => "h" + asked++);
            tally.Add(ClearWhy.OffSurface, () => "o");
            tally.Add(ClearWhy.Outside, () => "x");
            for (int i = 0; i < 4; i++)
                tally.Add(ClearWhy.TagStale, () => "s");
            Program.Check(tally.Count(ClearWhy.Missed) == 10 && tally.Count(ClearWhy.PartlyIn) == 5 && tally.Count(ClearWhy.Hidden) == 5 && tally.Count(ClearWhy.TagStale) == 4 && tally.Total == 26, "every lane is counted");
            Program.Check(tally.Samples.Count == 4 + 2 + 1 + 2, "the samples are the caps in all (4 missed, 2 partly, 1 other deck, 2 left-over tags: nine), none of a hidden or an outside lane");
            Program.Check(asked == ClearCensus.MissedSamples, "a lane that is not named is not described (the work is not done)");
            Program.Check(tally.Samples[0].StartsWith("Missed ") && tally.Samples[ClearCensus.MissedSamples].StartsWith("PartlyIn "), "a sample says its kind");
        }

        private static void Line()
        {
            var tally = new ClearCensus.Tally();
            tally.Add(ClearWhy.Hidden, () => "h");
            tally.Add(ClearWhy.Hidden, () => "h");
            tally.Add(ClearWhy.Missed, () => "NA Edge Line at (1, 2, 3)");
            tally.Add(ClearWhy.Crossing, () => "c");
            tally.Add(ClearWhy.PartlyIn, () => "NA Lane Line at (4, 5, 6)");
            string line = ClearCensus.Format(CensusMoment.Load, 70, 63, 63, tally, 2, true);
            Program.Check(line.StartsWith("Clear: census (load): 70 clearing item(s), 63 area(s), 5 marking lane(s) near them: 2 hidden, 1 visible although an area takes them (hidden now);"), "the head: items, areas, lanes, hidden, missed and repaired: " + line);
            Program.Check(line.Contains("1 crossing(s) kept") && line.Contains("1 partly inside") && line.Contains("2 picture(s) of the game inside an area (taken away now)"), "the rules' counts and the pictures");
            Program.Check(line.Contains("; Missed NA Edge Line at (1, 2, 3); PartlyIn NA Lane Line at (4, 5, 6)"), "the samples come last");
            string many = ClearCensus.Format(CensusMoment.AfterUpdated, 70, 300, ClearCensus.MaxRegions, new ClearCensus.Tally(), 0, true);
            Program.Check(many.Contains("300 area(s) (256 looked at)") && many.StartsWith("Clear: census (after Updated):") && !many.Contains("(hidden now)") && !many.Contains("(taken away now)"), "areas not all looked at say so; nothing repaired says nothing: " + many);
            Program.Check(ClearCensus.Format(CensusMoment.TenSeconds, 0, 0, 0, new ClearCensus.Tally(), 0, true).Contains("(10 s): 0 clearing item(s), 0 area(s)"), "no area: the line is still made");
        }

        private static void ScheduleLoad()
        {
            var plan = new CensusSchedule();
            plan.Reset(100.0);
            Program.Check(!plan.Take(100.0, out _), "nothing is due before the hold ends");
            plan.AskLoad(112.0);
            Program.Check(plan.Take(112.0, out CensusMoment moment) && moment == CensusMoment.Load, "the load's census is due when the hold ends");
            Program.Check(!plan.Take(112.0, out _), "once");
            Program.Check(!plan.Take(121.9, out _), "the second waits ten seconds");
            Program.Check(plan.Take(122.0, out moment) && moment == CensusMoment.TenSeconds, "ten seconds after");
            Program.Check(!plan.Take(500.0, out _), "and no more");
            plan.AskLoad(130.0);
            Program.Check(!plan.Take(500.0, out _), "the hold asks once a load");
            plan.Reset(600.0);
            plan.AskLoad(612.0);
            Program.Check(plan.Take(612.0, out moment) && moment == CensusMoment.Load, "a new load is asked for again");
            plan.AskUpdated(700.0);
            plan.AskNow(700.0);
            plan.Reset(701.0);
            Program.Check(!plan.Take(702.0, out _), "what was due in the city before is not taken in the next");
            Program.Check(!new CensusSchedule().Take(100.0, out _), "a schedule no load armed has no census");
        }

        private static void ScheduleUpdated()
        {
            var plan = new CensusSchedule();
            plan.Reset(0.0);
            plan.AskLoad(1.0);
            plan.Take(1.0, out _);
            plan.Take(11.0, out _);
            plan.AskUpdated(20.0);
            plan.AskUpdated(20.2);
            Program.Check(!plan.Take(20.9, out _), "a second after the batch, not before");
            Program.Check(plan.Take(21.0, out CensusMoment moment) && moment == CensusMoment.AfterUpdated, "due a second after");
            Program.Check(!plan.Take(21.0, out _) && !plan.Take(21.3, out _), "the second batch that came while it waited asked for no second one");
            int taken = 1;
            for (int i = 0; i < 20; i++)
            {
                plan.AskUpdated(30.0 + i * 10);
                if (plan.Take(31.0 + i * 10, out _))
                    taken++;
            }

            Program.Check(taken == CensusSchedule.MaxAfterUpdated, "at most " + CensusSchedule.MaxAfterUpdated + " a load: " + taken);
            plan.Reset(1000.0);
            plan.AskUpdated(1000.0);
            Program.Check(plan.Take(1001.0, out _), "a new load counts again");
        }

        private static void ScheduleAskedAndFallback()
        {
            var plan = new CensusSchedule();
            plan.Reset(0.0);
            plan.AskNow(5.0);
            plan.AskNow(5.5);
            Program.Check(plan.Take(5.5, out CensusMoment moment) && moment == CensusMoment.Asked && !plan.Take(5.5, out _), "asked twice, taken once");
            Program.Check(!plan.Take(29.9, out _), "the fallback is not due before the load is thirty seconds old");
            Program.Check(plan.Take(30.0, out moment) && moment == CensusMoment.Load, "the hold did not ask: the load's census comes at thirty seconds");
            Program.Check(plan.Take(40.0, out moment) && moment == CensusMoment.TenSeconds, "and the second ten seconds after it");
            var asked = new CensusSchedule();
            asked.Reset(0.0);
            asked.AskLoad(12.0);
            asked.Take(12.0, out _);
            asked.Take(22.0, out _);
            Program.Check(!asked.Take(100.0, out _), "the fallback is off once the hold asked");
            var two = new CensusSchedule();
            two.Reset(0.0);
            two.AskNow(3.0);
            two.AskLoad(2.0);
            Program.Check(two.Take(10.0, out moment) && moment == CensusMoment.Load && two.Take(10.0, out moment) && moment == CensusMoment.Asked, "the earliest due comes first");
        }

        private static void FileCadence()
        {
            var plan = new CensusSchedule();
            Program.Check(plan.FileDue(10.0) && !plan.FileDue(11.9) && plan.FileDue(12.0) && !plan.FileDue(12.5), "every two seconds");
        }
    }
}
