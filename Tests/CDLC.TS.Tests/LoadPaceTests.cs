using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The frames-or-seconds rule of the periodic jobs (Cadence) and the log's picture of the load's hold in time (LoadPace).</summary>
    internal static class LoadPaceTests
    {
        public static void RunAll()
        {
            Program.Run("cadence: due after the frames or after the seconds, whichever comes first", CadenceDue);
            Program.Run("cadence: the made fits go to the renderer at once when nothing near waits, else in batches of 30 frames or half a second", Batches);
            Program.Run("load pace: the gaps between the updates are counted in buckets, the longest is kept with the collections that ran in it", Gaps);
            Program.Run("load pace: when each source was last busy, which was the last and how long the gate went on after it", LastBusy);
            Program.Run("load pace: the timeline is one sample every half second, bounded, with unknown shown as ?", Timeline);
            Program.Run("load pace: nothing before the begin or without an update", Empty);
            Program.Run("load pace: a second hold shows nothing of the first", Again);
        }

        private static void CadenceDue()
        {
            Program.Check(Cadence.Due(6, 0f, 6, 0.1f), "the sixth frame is due whatever the time");
            Program.Check(Cadence.Due(1, 0.1f, 6, 0.1f), "a tenth of a second is due whatever the frames");
            Program.Check(!Cadence.Due(5, 0.099f, 6, 0.1f) && !Cadence.Due(0, 0f, 6, 0.1f), "neither: not due");
            Program.Check(!Cadence.Due(5, 5 / 60f, 6, 0.1f) && Cadence.Due(6, 6 / 60f, 6, 0.1f), "at 60 frames a second it is due at the sixth frame, as the frame count alone had it");
            Program.Check(!Cadence.Due(1, 1 / 11f, 6, 0.1f) && Cadence.Due(2, 2 / 11f, 6, 0.1f), "at 11 frames a second it is due at the second frame, not the sixth");
            Program.Check(!Cadence.Due(29, 0.49f, 30, 0.5f) && Cadence.Due(29, 0.5f, 30, 0.5f) && Cadence.Due(30, 0.1f, 30, 0.5f), "30 frames or half a second");
        }

        private static void Batches()
        {
            Program.Check(!Cadence.BatchDue(0, 0, 99, 9f) && !Cadence.BatchDue(0, 5, 99, 9f), "nothing made: nothing to give, whatever the time");
            Program.Check(Cadence.BatchDue(1, 0, 0, 0f), "nothing waits within reach of the camera: given at once");
            Program.Check(!Cadence.BatchDue(3, 5, 29, 0.4f), "some wait near, the batch is not due: held");
            Program.Check(Cadence.BatchDue(3, 5, 30, 0.1f), "30 frames: a batch (a fast frame rate)");
            Program.Check(Cadence.BatchDue(3, 5, 6, 0.5f), "half a second: a batch at 12 frames a second, not after 30 frames (2.5 s)");
            Program.Check(!Cadence.BatchDue(3, 5, 29, 0.49f), "neither of the two");
        }

        private static void Gaps()
        {
            double t = 10.0;
            var pace = new LoadPace(() => t);
            pace.Begin(5, 2, 1);
            foreach (double gap in new[] { 0.015, 0.015, 0.015, 0.015, 0.015, 0.015, 0.03, 0.03, 0.06, 0.1 })
            {
                t += gap;
                pace.Tick(5, 2, 1);
            }

            // Collections that ran early are not the longest gap's.
            t += 0.015;
            pace.Tick(8, 2, 1);
            t += 2.4;
            pace.Tick(9, 2, 2);
            string text = pace.Describe();
            Program.Check(pace.Updates == 12, "twelve updates: " + pace.Updates);
            Program.Check(text.StartsWith("12 update(s) in 2.7 s: half within 20 ms, 95 % within >320 ms, 1 gap(s) of 250 ms or more, the longest 2400 ms from 0.3 s"), "the numbers: " + text);
            Program.Check(text.Contains("(garbage collections in it, generation 0/1/2: 1/0/1)"), "the collections of the longest gap alone, not since the begin: " + text);

            // A gap of 0.2 s is not a stall; one of 0.25 s is.
            t = 0.0;
            var calm = new LoadPace(() => t);
            calm.Begin(0, 0, 0);
            t += 0.2;
            calm.Tick(0, 0, 0);
            Program.Check(calm.Describe().Contains(", 0 gap(s) of 250 ms or more, the longest 200 ms"), "0.2 s is no stall: " + calm.Describe());
            t = 0.0;
            var stall = new LoadPace(() => t);
            stall.Begin(0, 0, 0);
            t = 0.25;
            stall.Tick(0, 0, 0);
            Program.Check(stall.Describe().Contains(", 1 gap(s) of 250 ms or more, the longest 250 ms"), "0.25 s is a stall: " + stall.Describe());

            // A gap on the edge of a bucket is in that bucket; of two equal longest gaps the first is kept.
            t = 0.0;
            var edge = new LoadPace(() => t);
            edge.Begin(0, 0, 0);
            t = 0.32;
            edge.Tick(0, 0, 0);
            Program.Check(edge.Describe().Contains("half within 320 ms, 95 % within 320 ms"), "0.32 s is in the 320 ms bucket: " + edge.Describe());
            t = 0.0;
            var twin = new LoadPace(() => t);
            twin.Begin(0, 0, 0);
            t = 0.5;
            twin.Tick(1, 0, 0);
            t = 1.0;
            twin.Tick(2, 0, 0);
            Program.Check(twin.Describe().Contains("the longest 500 ms from 0.0 s (garbage collections in it, generation 0/1/2: 1/0/0)"), "the first of two equal gaps: " + twin.Describe());

            // A new begin forgets the last hold.
            twin.Begin(2, 0, 0);
            Program.Check(twin.Updates == 0 && twin.Describe() == "", "a new begin starts again");

            // The share is "at least": ten gaps of 10 ms and ten of 100 ms are half within 20 ms and 95 % within 160 ms.
            t = 0.0;
            var share = new LoadPace(() => t);
            share.Begin(0, 0, 0);
            for (int i = 0; i < 20; i++)
            {
                t += i < 10 ? 0.01 : 0.1;
                share.Tick(0, 0, 0);
            }

            Program.Check(share.Describe().Contains("half within 20 ms, 95 % within 160 ms"), "the share is at least half, at least 95 %: " + share.Describe());
        }

        private static void Again()
        {
            double t = 0.0;
            var pace = new LoadPace(() => t);
            pace.Begin(0, 0, 0);
            t = 0.1; pace.Tick(0, 0, 0); pace.Sample(Counts(5, 5, 5));
            t = 0.2; pace.Tick(0, 0, 0); pace.Sample(Counts(0, 0, 0));
            pace.Begin(0, 0, 0);
            t = 0.3; pace.Tick(0, 0, 0); pace.Sample(Counts(0, 0, 0));
            string text = pace.Describe();
            Program.Check(!text.Contains("last busy") && text.Contains("every 0.5 s: 0.1 s 0/0/0") && !text.Contains("5/5/5"), "the second hold shows nothing of the first (its busy times and its samples), and counts from its own begin: " + text);
        }

        private static List<KeyValuePair<string, int>> Counts(int a, int b, int c) =>
            new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("a", a), new KeyValuePair<string, int>("b", b), new KeyValuePair<string, int>("c", c) };

        private static void LastBusy()
        {
            double t = 0.0;
            var pace = new LoadPace(() => t);
            pace.Begin(0, 0, 0);
            t = 0.2; pace.Tick(0, 0, 0); pace.Sample(Counts(3, 2, 0));
            t = 1.0; pace.Tick(0, 0, 0); pace.Sample(Counts(1, 2, 0));
            t = 2.0; pace.Tick(0, 0, 0); pace.Sample(Counts(0, 1, 0));
            t = 2.4; pace.Tick(0, 0, 0); pace.Sample(Counts(0, 0, 0));
            t = 2.45;
            string text = pace.Describe();
            Program.Check(text.Contains("last busy: a 1.0 s, b 2.0 s;"), "a source that never waited is not listed: " + text);
            Program.Check(text.Contains("the last was b, the gate went on 0.45 s after it"), "the last source and the time after it: " + text);

            // Two sources last busy at the same moment: the first listed is the last one named.
            t = 0.0;
            var same = new LoadPace(() => t);
            same.Begin(0, 0, 0);
            t = 0.5; same.Tick(0, 0, 0); same.Sample(Counts(0, 4, 4));
            t = 1.0; same.Tick(0, 0, 0); same.Sample(Counts(0, 0, 0));
            Program.Check(same.Describe().Contains("the last was b, the gate went on 0.50 s after it"), "a tie names the first: " + same.Describe());

            // A source that cannot say yet is busy.
            t = 0.0;
            var unknown = new LoadPace(() => t);
            unknown.Begin(0, 0, 0);
            t = 0.1; unknown.Tick(0, 0, 0); unknown.Sample(Counts(0, -1, 0));
            t = 0.3; unknown.Tick(0, 0, 0); unknown.Sample(Counts(0, 0, 0));
            Program.Check(unknown.Describe().Contains("last busy: b 0.1 s;"), "unknown counts as busy: " + unknown.Describe());
        }

        private static void Timeline()
        {
            double t = 0.0;
            var pace = new LoadPace(() => t);
            pace.Begin(0, 0, 0);
            foreach (double at in new[] { 0.0, 0.2, 0.4, 0.5, 0.7, 1.0, 1.2 })
            {
                t = at;
                pace.Tick(0, 0, 0);
                pace.Sample(Counts((int)(at * 10), at < 0.6 ? -1 : 0, 7));
            }

            string text = pace.Describe();
            Program.Check(text.Contains("waiting (a/b/c) every 0.5 s: 0.0 s 0/?/7, 0.5 s 5/?/7, 1.0 s 10/0/7"), "one sample at the first update and then at 0.5 s and 1.0 s, unknown as ?: " + text);

            // A sample that comes late is followed by the next whole half second, not by half a second after it.
            t = 0.0;
            var late = new LoadPace(() => t);
            late.Begin(0, 0, 0);
            foreach (double at in new[] { 0.0, 0.7, 1.0 })
            {
                t = at;
                late.Tick(0, 0, 0);
                late.Sample(Counts(1, 1, 1));
            }

            Program.Check(late.Describe().Contains("0.0 s 1/1/1, 0.7 s 1/1/1, 1.0 s 1/1/1"), "the half seconds are those of the clock: " + late.Describe());

            t = 0.0;
            var long_ = new LoadPace(() => t);
            long_.Begin(0, 0, 0);
            for (int i = 0; i < 1000; i++)
            {
                t = i * 0.1;
                long_.Tick(0, 0, 0);
                long_.Sample(Counts(1, 1, 1));
            }

            string all = long_.Describe();
            string samples = all.Substring(all.IndexOf("every 0.5 s: ") + "every 0.5 s: ".Length);
            Program.Check(samples.Split(new[] { ", " }, System.StringSplitOptions.None).Length == LoadPace.MaxSamples, "a long hold keeps at most " + LoadPace.MaxSamples + " samples");
        }

        private static void Empty()
        {
            double t = 0.0;
            var pace = new LoadPace(() => t);
            Program.Check(pace.Describe() == "", "before the begin there is nothing to say");
            pace.Tick(0, 0, 0);
            pace.Sample(Counts(1, 1, 1));
            Program.Check(pace.Updates == 0 && pace.Describe() == "", "a tick before the begin is not counted");
            pace.Begin(0, 0, 0);
            Program.Check(pace.Describe() == "", "no update since the begin: nothing to say");
        }
    }
}
