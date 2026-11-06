using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    internal static class PerfStatsTests
    {
        public static void RunAll()
        {
            Program.Run("perf stats: counts, average, worst and the 1 ms and 8 ms counters", Counts);
            Program.Run("perf stats: percentiles are upper bounds from the histogram", Percentiles);
            Program.Run("perf stats: merge, reset and the summary line", MergeAndLine);
        }

        private static void Counts()
        {
            var s = new PerfStats();
            Program.Check(s.Calls == 0 && s.AverageMs == 0.0 && s.PercentileUpperMs(0.95) == 0.0 && s.OverOnePercent == 0.0, "nothing yet: all zero");
            foreach (double ms in new[] { 0.02, 0.5, 0.999, 1.0, 7.99, 8.0, 141.5 })
                s.Add(ms);
            Program.Check(s.Calls == 7, "seven calls");
            Program.Near((float)s.TotalMs, 0.02f + 0.5f + 0.999f + 1.0f + 7.99f + 8.0f + 141.5f, 1e-3f, "the total");
            Program.Check(s.MaxMs == 141.5, "the worst");
            Program.Check(s.OverOneMs == 4 && s.OverEightMs == 2, "1.0 and 8.0 count as over: " + s.OverOneMs + " " + s.OverEightMs);
            Program.Near((float)s.OverOnePercent, 100f * 4f / 7f, 1e-3f, "the share over 1 ms");
            s.Add(double.NaN);
            s.Add(-3.0);
            Program.Check(s.Calls == 9 && s.MaxMs == 141.5 && s.OverOneMs == 4, "NaN and negative times count as zero");
        }

        private static void Percentiles()
        {
            var s = new PerfStats();
            for (int i = 0; i < 90; i++)
                s.Add(0.02);
            for (int i = 0; i < 9; i++)
                s.Add(0.4);
            s.Add(30.0);
            Program.Check(s.PercentileUpperMs(0.5) <= 0.05, "p50 is in the first bucket: " + s.PercentileUpperMs(0.5));
            Program.Check(s.PercentileUpperMs(0.9) <= 0.05, "the 90th of 100 is still a fast one: " + s.PercentileUpperMs(0.9));
            double p95 = s.PercentileUpperMs(0.95);
            Program.Check(p95 >= 0.4 && p95 <= 0.5, "p95 is the bucket of the 0.4 ms calls: " + p95);
            Program.Check(s.PercentileUpperMs(0.99) >= 0.4 && s.PercentileUpperMs(0.99) <= 0.5, "the 99th of 100 is the last 0.4 ms call: " + s.PercentileUpperMs(0.99));
            Program.Check(s.PercentileUpperMs(1.0) == 30.0, "the 100th is the worst, not a bucket edge: " + s.PercentileUpperMs(1.0));
            Program.Check(s.PercentileUpperMs(0.0) <= 0.05 && s.PercentileUpperMs(-1.0) <= 0.05 && s.PercentileUpperMs(5.0) == 30.0, "a share outside 0 to 1 is clamped");
            var tiny = new PerfStats();
            tiny.Add(0.01);
            Program.Check(tiny.PercentileUpperMs(0.99) == 0.01, "the bound never exceeds the worst: " + tiny.PercentileUpperMs(0.99));
            var huge = new PerfStats();
            huge.Add(500.0);
            Program.Check(huge.PercentileUpperMs(0.5) == 500.0, "the last bucket reports the worst: " + huge.PercentileUpperMs(0.5));
            var edge = new PerfStats();
            for (int i = 0; i < 10; i++)
                edge.Add(1.0);
            Program.Check(edge.PercentileUpperMs(0.5) == 1.0 && edge.OverOneMs == 10, "a time of exactly an edge is over 1 ms: " + edge.PercentileUpperMs(0.5));

            // A time of exactly an edge is in the bucket above it (its bound is the next edge), unlike one just below.
            var onEdge = new PerfStats();
            for (int i = 0; i < 10; i++)
                onEdge.Add(0.5);
            onEdge.Add(50.0);
            Program.Check(onEdge.PercentileUpperMs(0.5) == 0.75, "0.5 ms is in the bucket from 0.5 to 0.75: " + onEdge.PercentileUpperMs(0.5));
            var below = new PerfStats();
            for (int i = 0; i < 10; i++)
                below.Add(0.4999);
            below.Add(50.0);
            Program.Check(below.PercentileUpperMs(0.5) == 0.5, "0.4999 ms is in the bucket below it: " + below.PercentileUpperMs(0.5));

            // The p-th update rounds up: the 95th percentile of ten updates is the tenth.
            var ten = new PerfStats();
            for (int i = 0; i < 9; i++)
                ten.Add(0.02);
            ten.Add(30.0);
            Program.Check(ten.PercentileUpperMs(0.95) == 30.0 && ten.PercentileUpperMs(0.9) <= 0.05, "p95 of ten calls is the slowest: " + ten.PercentileUpperMs(0.95) + " / p90 " + ten.PercentileUpperMs(0.9));
        }

        private static void MergeAndLine()
        {
            var window = new PerfStats();
            for (int i = 0; i < 98; i++)
                window.Add(0.1);
            window.Add(2.5);
            window.Add(12.0);
            var session = new PerfStats();
            session.Add(9.0);
            session.Merge(window);
            Program.Check(session.Calls == 101 && session.OverOneMs == 3 && session.OverEightMs == 2 && session.MaxMs == 12.0, "merged counters and worst: " + session.OverOneMs + " " + session.OverEightMs);
            Program.Near((float)session.TotalMs, 9.0f + 98f * 0.1f + 2.5f + 12.0f, 1e-3f, "merged total");
            Program.Check(session.PercentileUpperMs(0.5) <= 0.2, "the merged histogram has the window's buckets: " + session.PercentileUpperMs(0.5));
            window.Reset();
            Program.Check(window.Calls == 0 && window.MaxMs == 0.0 && window.OverOneMs == 0 && window.OverEightMs == 0 && window.TotalMs == 0.0 && window.PercentileUpperMs(0.5) == 0.0 && session.Calls == 101, "reset empties the window and not the session");
            window.Add(30.0);
            Program.Check(window.PercentileUpperMs(0.5) == 30.0, "and its buckets: a call after the reset is not placed among the old ones: " + window.PercentileUpperMs(0.5));

            var line = new PerfStats();
            for (int i = 0; i < 98; i++)
                line.Add(0.1);
            line.Add(1.5);
            line.Add(12.0);
            string text = line.Line("AreaFitSystem", 180.0, " at 11:00:59");
            Program.Check(text.StartsWith("* AreaFitSystem: 100 call(s)"), "more than 1 % over 1 ms is starred: " + text);
            Program.Check(text.Contains("load 0.1 ms/s") && text.Contains("worst 12.0 ms at 11:00:59") && text.Contains("over 1 ms 2 (2.0 %)") && text.Contains("over 8 ms 1"), "the numbers: " + text);
            var quiet = new PerfStats();
            for (int i = 0; i < 200; i++)
                quiet.Add(0.2);
            Program.Check(quiet.Line("KerbSystem", 10.0).StartsWith("KerbSystem: 200 call(s), load 4.0 ms/s"), "a quiet system has no star: " + quiet.Line("KerbSystem", 10.0));
            Program.Check(quiet.Line("X", 0.0).Contains("load 0.0 ms/s"), "no window length: load 0");
        }
    }
}
