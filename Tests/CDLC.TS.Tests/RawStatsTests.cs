using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The histograms of the B0 probe: where the bucket edges are (they decide what the log says about the game's raw flow figures), and what is counted apart.</summary>
    internal static class RawStatsTests
    {
        public static void RunAll()
        {
            Program.Run("raw stats: the byte buckets", Buckets);
            Program.Run("raw stats: the ratio buckets", Ratios);
        }

        private static void Buckets()
        {
            // The edges: each side of every limit.
            foreach ((int value, int bucket) in new[] { (0, 0), (1, 1), (63, 1), (64, 2), (127, 2), (128, 3), (191, 3), (192, 4), (254, 4), (255, 5) })
                Program.Check(ByteHistogram.BucketOf(value) == bucket, value + " is in bucket " + bucket + ": " + ByteHistogram.BucketOf(value));
            Program.Check(ByteHistogram.BucketOf(-5) == 0 && ByteHistogram.BucketOf(256) == 5 && ByteHistogram.BucketOf(int.MaxValue) == 5 && ByteHistogram.BucketOf(int.MinValue) == 0, "values outside 0 to 255 are kept to the ends");

            // Every byte is in exactly one bucket, the buckets are in order, and all six are used.
            var all = new ByteHistogram();
            int previous = 0;
            for (int v = 0; v <= 255; v++)
            {
                all.Add(v);
                int b = ByteHistogram.BucketOf(v);
                Program.Check(b >= previous && b >= 0 && b < ByteHistogram.BucketCount, "buckets never go back: " + v);
                previous = b;
            }

            Program.Check(all.Count == 256 && all.Buckets[0] == 1 && all.Buckets[1] == 63 && all.Buckets[2] == 64 && all.Buckets[3] == 64 && all.Buckets[4] == 63 && all.Buckets[5] == 1, "256 values fall 1, 63, 64, 64, 63, 1");
            int sum = 0;
            foreach (int n in all.Buckets)
                sum += n;
            Program.Check(sum == all.Count, "the buckets add up to the count");

            // The text the log carries.
            var h = new ByteHistogram();
            h.Add(0);
            h.Add(0);
            h.Add(200);
            h.Add(255);
            Program.Check(h.ToString() == "0:2 1-63:0 64-127:0 128-191:0 192-254:1 255:1", "the text: " + h);
            Program.Check(new ByteHistogram().ToString() == "0:0 1-63:0 64-127:0 128-191:0 192-254:0 255:0" && new ByteHistogram().Count == 0, "an empty one");

            // The text is the same in another culture (it is written to a log that is read by a person and pasted into a report).
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var big = new ByteHistogram();
                for (int i = 0; i < 12345; i++)
                    big.Add(0);
                Program.Check(big.ToString().StartsWith("0:12345 "), "no group separator: " + big);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }
        }

        private static void Ratios()
        {
            var h = new RatioHistogram();
            foreach (float r in new[] { 0f, 0.05f, 0.0999f, 0.1f, 0.55f, 0.9f, 0.9999f, 1f })
                h.Add(r);
            Program.Check(h.Tenths[0] == 3 && h.Tenths[1] == 1 && h.Tenths[5] == 1 && h.Tenths[9] == 3 && h.Count == 8, "0, 0.05, 0.0999 in the first tenth; 0.1 in the second; 1.0 in the last: " + h);
            int inTenths = 0;
            foreach (int n in h.Tenths)
                inTenths += n;
            Program.Check(inTenths == 8 && h.NotANumber == 0 && h.OutOfRange == 0, "all eight are in the tenths");

            // What is not a ratio is counted apart and does not land in a tenth.
            var odd = new RatioHistogram();
            foreach (float r in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.01f, 1.0001f, 7f, 0.5f })
                odd.Add(r);
            Program.Check(odd.NotANumber == 3 && odd.OutOfRange == 3 && odd.Count == 7, "three that are not numbers, three out of range: " + odd);
            int tenths = 0;
            foreach (int n in odd.Tenths)
                tenths += n;
            Program.Check(tenths == 1 && odd.Tenths[5] == 1, "only 0.5 is in a tenth");

            // Of: nothing over nothing is 'no data', not 0 and not 1.
            Program.Check(float.IsNaN(RatioHistogram.Of(0f, 0f)) && float.IsNaN(RatioHistogram.Of(5f, 0f)) && float.IsNaN(RatioHistogram.Of(1f, -1f)), "no duration is no data");
            Program.Near(RatioHistogram.Of(30f, 60f), 0.5f, 1e-6f, "30 over 60");
            Program.Near(RatioHistogram.Of(0f, 60f), 0f, 1e-6f, "driven nowhere in 60: 0");
            var none = new RatioHistogram();
            none.Add(RatioHistogram.Of(0f, 0f));
            Program.Check(none.NotANumber == 1 && none.Tenths[0] == 0 && none.Tenths[9] == 0, "a road with no data is counted apart: neither free nor jammed");

            Program.Check(new RatioHistogram().ToString() == "0-10%:0 10-20%:0 20-30%:0 30-40%:0 40-50%:0 50-60%:0 60-70%:0 70-80%:0 80-90%:0 90-100%:0 no-data:0 out-of-range:0", "an empty one: " + new RatioHistogram());
            Program.Check(h.ToString().EndsWith("no-data:0 out-of-range:0") && odd.ToString().EndsWith("no-data:3 out-of-range:3"), "the text carries the two counts");

            // The ratio of the game: more distance than the duration allows (a vehicle faster than the limit) is above 1 and is counted apart, not as free.
            var fast = new RatioHistogram();
            fast.Add(RatioHistogram.Of(130f, 100f));
            Program.Check(fast.OutOfRange == 1 && fast.Tenths[9] == 0, "a ratio of 1.3 is out of range");
        }
    }
}
