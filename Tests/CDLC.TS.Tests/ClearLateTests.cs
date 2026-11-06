using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The late pass and the phase bisection of the trace (0.12.86; the owner's log of 4 Nov: every lane this mod hid had lost the tag by the end of the same frame, and the game's own code does not take it
    /// off a marking lane: Anarchy's RemoveOverridenSystem does, to every updated lane that has an owner and the tag, under the default and the bulldozer tools).
    /// </summary>
    internal static class ClearLateTests
    {
        public static void RunAll()
        {
            Program.Run("late: a lane is tagged again when it has no tag and an area takes it, and in no other case", Needs);
            Program.Run("late: the phase tally keeps what each place saw, clamps it, and tells a place not looked at from a loss", Tally);
            Program.Run("late: the first loss is the first place at which fewer lanes carried the tag", FirstLoss);
            Program.Run("late: the phases line has the five places, the counts and where the tag was first missing", Line);
            Program.Run("late: the five places have distinct names and six batches of a load are followed", Names);
            Program.Run("late: a frame with a system that takes the tag off updated lanes: without the late pass no lane keeps it, with it every lane an area takes does", Frame);
            Program.Run("late: the late pass leaves alone a lane shown on purpose, a lane no area takes, a tagged lane and a lane the stripper cannot reach", FrameOthers);
        }

        private static void Needs()
        {
            Program.Check(LateRehide.Needs(false, true), "no tag and an area takes it: tagged again");
            Program.Check(!LateRehide.Needs(true, true), "a tagged lane is not touched");
            Program.Check(!LateRehide.Needs(false, false), "a lane no area takes stays as it is (a lane shown on purpose)");
            Program.Check(!LateRehide.Needs(true, false), "tagged and not taken: nothing");
        }

        private static void Tally()
        {
            var tally = new ClearPhases.Tally(8);
            Program.Check(tally.Total == 8 && !tally.Has(TracePhase.AfterHide) && tally.Tagged(TracePhase.AfterHide) == -1, "a new tally has seen no place");
            tally.Set(TracePhase.Modification4BEnd, 8);
            Program.Check(tally.Has(TracePhase.Modification4BEnd) && tally.Tagged(TracePhase.Modification4BEnd) == 8 && !tally.Has(TracePhase.Modification5End), "a place that was looked at is told from one that was not");
            tally.Set(TracePhase.Modification5End, 99);
            Program.Check(tally.Tagged(TracePhase.Modification5End) == 8, "a count above the batch is the batch");
            tally.Set(TracePhase.ModificationEndBeforeLate, -3);
            Program.Check(tally.Has(TracePhase.ModificationEndBeforeLate) && tally.Tagged(TracePhase.ModificationEndBeforeLate) == 0, "a negative count is no lane: 0, and the place was looked at");
        }

        private static void FirstLoss()
        {
            ClearPhases.Tally T(params int[] counts)
            {
                var t = new ClearPhases.Tally(8);
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] >= 0)
                        t.Set((TracePhase)i, counts[i]);
                }

                return t;
            }

            Program.Check(T(8, 8, 8, 0, 8).FirstLoss() == (int)TracePhase.ModificationEndBeforeLate, "the tag went in ModificationEnd (Anarchy's place): the place before the late pass");
            Program.Check(T(8, 8, 8, 8, 8).FirstLoss() == -1, "no loss: none");
            Program.Check(T(8, -1, -1, 8, 8).FirstLoss() == -1, "places not looked at are no loss");
            Program.Check(T(8, 5, 5, 5, 5).FirstLoss() == (int)TracePhase.Modification4BEnd, "a loss before the end of Modification4B names that place");
            Program.Check(T(5, 5, 5, 5, 5).FirstLoss() == (int)TracePhase.AfterHide, "a loss at once names the first place");
            Program.Check(T(8, 8, 8, 0, 8).FirstLoss() != (int)TracePhase.ModificationEndAfterLate, "a tag put on again by the late pass is not a loss after it");
            Program.Check(T(8, -1, 3, -1, -1).FirstLoss() == (int)TracePhase.Modification5End, "a loss is found even when the places before it were skipped");
        }

        private static void Line()
        {
            var tally = new ClearPhases.Tally(8);
            tally.Set(TracePhase.AfterHide, 8);
            tally.Set(TracePhase.Modification4BEnd, 8);
            tally.Set(TracePhase.Modification5End, 8);
            tally.Set(TracePhase.ModificationEndBeforeLate, 0);
            tally.Set(TracePhase.ModificationEndAfterLate, 8);
            string line = ClearPhases.Format("a pass over changed areas", tally, "\"NA Highway Edge Line\" 939363:1 Updated, owner is an edge");
            Program.Check(line.StartsWith("Clear: trace (phases): 8 lane(s) hidden by a pass over changed areas"), "the line says the batch: " + line);
            Program.Check(line.Contains("the end of Modification4B 8") && line.Contains("the end of Modification5 8") && line.Contains("before the late pass 0") && line.Contains("after the late pass 8"), "the counts of the places: " + line);
            Program.Check(line.Contains("The tag was first missing at the end of ModificationEnd before the late pass"), "where it was first missing: " + line);
            Program.Check(line.Contains("the first lane that was missing: \"NA Highway Edge Line\" 939363:1"), "the lane named: " + line);

            var unseen = new ClearPhases.Tally(3);
            unseen.Set(TracePhase.AfterHide, 3);
            string quiet = ClearPhases.Format("the census", unseen, null);
            Program.Check(quiet.Contains("the end of Modification4B -") && quiet.Contains("No place saw a loss") && !quiet.Contains("the first lane that was missing"), "a place not looked at is a dash, no loss is said, no lane named: " + quiet);
        }

        private static void Names()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 5; i++)
                seen.Add(ClearPhases.Name((TracePhase)i));
            Program.Check(seen.Count == 5, "five places, five names");
            Program.Check(ClearPhases.ProbeBatches == 6, "six batches of a load are followed phase by phase");
            Program.Check((int)TracePhase.AfterHide == 0 && (int)TracePhase.ModificationEndAfterLate == 4, "the places are in the order of a frame");
        }

        // A lane in a frame: what a system that takes the tag off updated lanes can see of it.
        private sealed class Lane
        {
            public bool Tagged, Updated, Owned = true, Excluded, TakenByArea;
        }

        /// <summary>
        /// The systems of a frame that matter, in their order: this mod's pass of Modification4B (tag and Updated), the stripper in the first place of ModificationEnd (a lane that has an owner, Updated and
        /// the tag, none of the components it leaves alone: the tag off and Updated on), and the late pass at the end of ModificationEnd.
        /// </summary>
        private static void Simulate(List<Lane> lanes, bool stripperOn, bool late)
        {
            foreach (Lane lane in lanes)
            {
                if (lane.TakenByArea && !lane.Tagged)
                {
                    lane.Tagged = true;
                    lane.Updated = true;
                }
            }

            if (stripperOn)
            {
                foreach (Lane lane in lanes)
                {
                    if (lane.Owned && lane.Updated && lane.Tagged && !lane.Excluded)
                    {
                        lane.Tagged = false;
                        lane.Updated = true;
                    }
                }
            }

            if (late)
            {
                foreach (Lane lane in lanes)
                {
                    if (lane.Updated && LateRehide.Needs(lane.Tagged, !lane.Tagged && lane.TakenByArea))
                        lane.Tagged = true;
                }
            }
        }

        private static List<Lane> Eight()
        {
            var lanes = new List<Lane>();
            for (int i = 0; i < 8; i++)
                lanes.Add(new Lane { TakenByArea = true });
            return lanes;
        }

        private static int TaggedCount(List<Lane> lanes)
        {
            int n = 0;
            foreach (Lane lane in lanes)
                n += lane.Tagged ? 1 : 0;
            return n;
        }

        private static void Frame()
        {
            List<Lane> without = Eight();
            Simulate(without, true, false);
            Program.Check(TaggedCount(without) == 0, "the owner's log: 8 lanes hidden, a stripper in the frame, no late pass: 0 carry the tag");
            List<Lane> with = Eight();
            Simulate(with, true, true);
            Program.Check(TaggedCount(with) == 8, "the same frame with the late pass: all 8 carry the tag");
            List<Lane> clean = Eight();
            Simulate(clean, false, true);
            Program.Check(TaggedCount(clean) == 8, "no stripper in the world: the late pass finds nothing to do and all 8 stay tagged");

            // The next frames: the tag stays (no Updated, so the stripper has nothing to take).
            foreach (Lane lane in with)
                lane.Updated = false;
            Simulate(with, true, true);
            Program.Check(TaggedCount(with) == 8, "a frame later, with no Updated on the lanes: the stripper cannot take the tag, it stays");
            // The game updates the lanes again (a road changed): the stripper takes it, the late pass puts it back.
            foreach (Lane lane in with)
                lane.Updated = true;
            Simulate(with, true, true);
            Program.Check(TaggedCount(with) == 8, "the game updates the lanes again: the stripper takes the tag and the late pass puts it back, in each such frame");
            foreach (Lane lane in with)
                lane.Updated = true;
            Simulate(with, true, false);
            Program.Check(TaggedCount(with) == 0, "and without the late pass that frame loses all of them");
        }

        private static void FrameOthers()
        {
            // A lane shown on purpose (an area was taken away): it is updated and untagged and no area takes it.
            var shown = new Lane { Updated = true };
            // A lane no area takes that carries another mod's tag, updated: only the stripper can touch it, never the late pass.
            var others = new Lane { Tagged = true, Updated = true, TakenByArea = false };
            // A tagged lane of an area the stripper leaves alone (it has a component the stripper excludes): never needs the late pass.
            var kept = new Lane { Tagged = true, Updated = true, TakenByArea = true, Excluded = true };
            // A lane with no owner: the stripper's query does not take it.
            var noOwner = new Lane { Tagged = true, Updated = true, TakenByArea = true, Owned = false };
            var lanes = new List<Lane> { shown, others, kept, noOwner };
            Simulate(lanes, true, true);
            Program.Check(!shown.Tagged, "a lane shown on purpose is not tagged by the late pass");
            Program.Check(!others.Tagged, "another mod's lane, updated, that no area of this mod takes: the stripper took its tag and this mod does not put it back");
            Program.Check(kept.Tagged && noOwner.Tagged, "lanes the stripper cannot reach keep the tag, and the late pass leaves them");
        }
    }
}
