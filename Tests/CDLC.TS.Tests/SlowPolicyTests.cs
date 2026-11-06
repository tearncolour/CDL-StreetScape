using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    internal static class SlowPolicyTests
    {
        public static void RunAll()
        {
            Program.Run("slow log: the first lines of a system, then only records", Basics);
            Program.Run("slow log: the caps", Caps);
        }

        private static void Basics()
        {
            var p = new SlowPolicy(firstEach: 3, recordEach: 6, firstTotal: 100, recordTotal: 40);
            for (int i = 0; i < 3; i++)
                Program.Check(p.Judge("tool", 10.0 + i) == SlowWrite.First, "the first three slow updates are written: " + i);
            Program.Check(p.Judge("tool", 12.0) == SlowWrite.No, "the fourth, nothing special, is not");
            Program.Check(p.Judge("tool", 29.0) == SlowWrite.No, "29 ms is under the floor of 30 ms although it is more than 1.5 times the peak");
            Program.Check(p.Judge("tool", 31.0) == SlowWrite.No, "31 ms is not 1.5 times the peak of 29 ms (written or not, the peak counts)");
            Program.Check(p.Judge("tool", 44.0) == SlowWrite.No, "44 ms is not 1.5 times the peak of 31 ms (46.5)");
            Program.Check(p.Judge("tool", 70.0) == SlowWrite.Record, "70 ms is more than 1.5 times the peak of 44 ms (66): a record");
            Program.Check(p.Judge("tool", 141.5) == SlowWrite.Record, "the 141.5 ms frame of the owner's log is told");
            Program.Check(p.Judge("tool", 100.0) == SlowWrite.No, "a smaller one afterwards is not");
            Program.Check(p.Judge("area", 35.0) == SlowWrite.First, "another system has its own first lines");
            var low = new SlowPolicy(firstEach: 0, recordEach: 6, firstTotal: 100, recordTotal: 40);
            Program.Check(low.Judge("x", 30.0) == SlowWrite.Record, "30 ms, the floor, is a record when nothing was slow before");
            Program.Check(low.Judge("x", 44.9) == SlowWrite.No, "44.9 ms is under 1.5 times 30 ms");
            var inclusive = new SlowPolicy(firstEach: 0, recordEach: 6, firstTotal: 100, recordTotal: 40);
            inclusive.Judge("y", 30.0);
            Program.Check(inclusive.Judge("y", 45.0) == SlowWrite.Record, "the factor is inclusive: 45 ms is 1.5 times 30 ms");
        }

        private static void Caps()
        {
            var p = new SlowPolicy(firstEach: 1, recordEach: 2, firstTotal: 2, recordTotal: 3);
            Program.Check(p.Judge("a", 10.0) == SlowWrite.First && p.Judge("b", 10.0) == SlowWrite.First, "two systems use the two first lines of the session");
            Program.Check(p.Judge("c", 10.0) == SlowWrite.No, "the third system's first slow update is not written: the session's first lines are used up");
            Program.Check(p.Judge("c", 40.0) == SlowWrite.Record, "but a record of it is");
            Program.Check(p.Judge("c", 70.0) == SlowWrite.Record, "its second record");
            Program.Check(p.Judge("c", 150.0) == SlowWrite.No, "a system writes at most recordEach records");
            Program.Check(p.Judge("a", 40.0) == SlowWrite.Record, "the session's third record");
            Program.Check(p.Judge("a", 100.0) == SlowWrite.No && p.Judge("b", 100.0) == SlowWrite.No, "no more records after the session's cap of three");
        }
    }
}
