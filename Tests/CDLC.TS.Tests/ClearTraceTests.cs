using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The trace of the lanes this mod hid (0.12.84): what became of a lane, when it is looked at, what is remembered and how the line reads.</summary>
    internal static class ClearTraceTests
    {
        public static void RunAll()
        {
            Program.Run("trace: a lane's fate is told apart: tagged, tag gone, entity gone, deleted", Fates);
            Program.Run("trace: an entity is one key, and two entities differ in index or version", Keys);
            Program.Run("trace: the three looks are due in the same frame, the next and 120 frames after, each once", Stages);
            Program.Run("trace: the ring remembers the last 512 lanes and forgets the oldest", RingBound);
            Program.Run("trace: a lane hidden again keeps one place and takes the new time", RingAgain);
            Program.Run("trace: the explanation says whether the same lane lost the tag", Explanation);
            Program.Run("trace: the tally counts every fate and names only a few of the lanes that are not tagged", Tally);
            Program.Run("trace: the line says the stage, the counts and the tag count of the world", Line);
        }

        private static void Fates()
        {
            Program.Check(ClearTrace.Judge(true, false, true) == LaneFate.Tagged, "exists, tagged: tagged");
            Program.Check(ClearTrace.Judge(true, false, false) == LaneFate.TagGone, "exists, not tagged: the tag is gone");
            Program.Check(ClearTrace.Judge(false, false, false) == LaneFate.Gone && ClearTrace.Judge(false, false, true) == LaneFate.Gone, "no entity: gone, whatever the rest says");
            Program.Check(ClearTrace.Judge(true, true, true) == LaneFate.Deleted && ClearTrace.Judge(true, true, false) == LaneFate.Deleted, "being deleted: deleted, tagged or not");
            Program.Check(ClearTrace.Judge(false, true, true) == LaneFate.Gone && ClearTrace.Judge(false, true, false) == LaneFate.Gone, "no entity: gone even when a stale Deleted is read");
        }

        private static void Keys()
        {
            Program.Check(ClearTrace.Key(5, 2) == ClearTrace.Key(5, 2), "the same entity, the same key");
            Program.Check(ClearTrace.Key(5, 2) != ClearTrace.Key(6, 2) && ClearTrace.Key(5, 2) != ClearTrace.Key(5, 3), "another index or another version is another key");
            Program.Check(ClearTrace.Key(-1, 1) != ClearTrace.Key(-1, 2) && ClearTrace.Key(int.MaxValue, 7) != ClearTrace.Key(0, 7), "keys of large and negative indices stay apart");
        }

        private static void Stages()
        {
            Program.Check(ClearTrace.Due(TraceStage.SameFrame, 0, 100, 100) && !ClearTrace.Due(TraceStage.SameFrame, 0, 100, 101), "the same-frame look is due in the frame itself only");
            Program.Check(!ClearTrace.Due(TraceStage.NextFrame, 0, 100, 100) && ClearTrace.Due(TraceStage.NextFrame, 0, 100, 101) && ClearTrace.Due(TraceStage.NextFrame, 0, 100, 150), "the next-frame look is due after the frame");
            Program.Check(!ClearTrace.Due(TraceStage.Later, 0, 100, 219) && ClearTrace.Due(TraceStage.Later, 0, 100, 220), "the late look is due 120 frames after");
            Program.Check(!ClearTrace.Due(TraceStage.SameFrame, 1 << (int)TraceStage.SameFrame, 100, 100), "a look done is not due again");
            Program.Check(ClearTrace.Due(TraceStage.NextFrame, 1 << (int)TraceStage.SameFrame, 100, 101), "another stage's mark does not stop it");
        }

        private static void RingBound()
        {
            var ring = new ClearTrace.HiddenRing();
            for (int i = 0; i < 600; i++)
                ring.Add(ClearTrace.Key(i, 1), i, i * 0.1, "pass");
            Program.Check(ring.Count == ClearTrace.Ring && ClearTrace.Ring == 512, "512 lanes are kept of 600: " + ring.Count);
            Program.Check(!ring.TryGet(ClearTrace.Key(0, 1), out _) && !ring.TryGet(ClearTrace.Key(87, 1), out _), "the oldest are forgotten");
            Program.Check(ring.TryGet(ClearTrace.Key(88, 1), out ClearTrace.Entry first) && first.Frame == 88 && first.Source == "pass", "the 512th from the end is the oldest kept");
            Program.Check(ring.TryGet(ClearTrace.Key(599, 1), out _), "the newest is kept");
            ring.Clear();
            Program.Check(ring.Count == 0 && !ring.TryGet(ClearTrace.Key(599, 1), out _), "cleared");
        }

        private static void RingAgain()
        {
            var ring = new ClearTrace.HiddenRing();
            for (int i = 0; i < 512; i++)
                ring.Add(ClearTrace.Key(i, 1), i, i, "pass");
            ring.Add(ClearTrace.Key(0, 1), 900, 9.5, "census");
            Program.Check(ring.Count == 512, "a lane hidden again is not a second entry");
            Program.Check(ring.TryGet(ClearTrace.Key(0, 1), out ClearTrace.Entry again) && again.Frame == 900 && again.Source == "census" && again.Time == 9.5, "it has the new frame, time and source");
            ring.Add(ClearTrace.Key(1000, 1), 901, 9.6, "pass");
            Program.Check(ring.Count == 512 && ring.TryGet(ClearTrace.Key(0, 1), out _) && !ring.TryGet(ClearTrace.Key(1, 1), out _), "the next new lane pushes out the oldest unseen lane (1), not the one hidden again (0)");
        }

        private static void Explanation()
        {
            string none = ClearTrace.Explain(false, default, 10.0);
            Program.Check(none.Contains("not hidden by this mod before") && !none.Contains("tag is gone"), "unknown: not hidden by this mod");
            var entry = new ClearTrace.Entry { Frame = 5, Time = 8.6, Source = "a pass" };
            string seen = ClearTrace.Explain(true, entry, 10.0);
            Program.Check(seen.Contains("1.4 s ago") && seen.Contains("a pass") && seen.Contains("the same lane, the tag is gone"), "known: how long ago, by what, and that the tag is gone: " + seen);
            Program.Check(ClearTrace.Explain(true, entry, 8.0).Contains("0.0 s ago"), "a clock that went back is 0.0 s, not negative");
        }

        private static void Tally()
        {
            var tally = new ClearTrace.FateTally();
            int described = 0;
            for (int i = 0; i < 5; i++)
                tally.Add(LaneFate.Tagged, () => { described++; return "x"; });
            for (int i = 0; i < 6; i++)
                tally.Add(LaneFate.TagGone, () => { described++; return "lane" + i; });
            tally.Add(LaneFate.Gone, () => { described++; return "g"; });
            tally.Add(LaneFate.Deleted, () => { described++; return "d"; });
            Program.Check(tally.Total == 13 && tally.Count(LaneFate.Tagged) == 5 && tally.Count(LaneFate.TagGone) == 6 && tally.Count(LaneFate.Gone) == 1 && tally.Count(LaneFate.Deleted) == 1, "every lane is counted by its fate");
            Program.Check(tally.Named.Count == ClearTrace.Samples && ClearTrace.Samples == 3, "three lanes are named");
            Program.Check(described == 3, "describe is asked only for the lanes that are named: " + described);
            Program.Check(tally.Named[0].StartsWith("TagGone lane0"), "the first lanes that lost the tag are the named ones");
        }

        private static void Line()
        {
            var tally = new ClearTrace.FateTally();
            for (int i = 0; i < 66; i++)
                tally.Add(LaneFate.TagGone, () => "\"NA Highway Edge Line\"");
            tally.Add(LaneFate.Tagged, () => "");
            string line = ClearTrace.Format(TraceStage.NextFrame, "a pass", 1, 0.04, tally, 542);
            Program.Check(line.StartsWith("Clear: trace (the next frame): 67 lane(s) hidden by a pass 1 frame(s), 0.0 s ago: 1 still tagged, 66 lost the tag, 0 are gone"), "the stage, the batch and the fates: " + line);
            Program.Check(line.Contains("542 lane(s) carry the tag in the world now"), "the tag count of the world");
            Program.Check(line.Contains("TagGone \"NA Highway Edge Line\""), "a lane that lost the tag is named");
            Program.Check(ClearTrace.Format(TraceStage.Later, "the census", 120, 2.0, new ClearTrace.FateTally(), 0).Contains("(later): 0 lane(s)"), "an empty batch reads");
        }
    }
}
