using System;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A count of byte values in six buckets (0 | 1-63 | 64-127 | 128-191 | 192-254 | 255), for the log of the raw flow offsets of the game's car lanes (0 free flow, 255 a jam): the two ends are
    /// buckets of their own because they are the values that mean something. Pure, so that the buckets are tested; the probes and the traffic-state screen's reader share it.
    /// </summary>
    public sealed class ByteHistogram
    {
        public const int BucketCount = 6;

        public readonly int[] Buckets = new int[BucketCount];

        public int Count { get; private set; }

        /// <summary>The bucket of a value: 0 for 0, 1 for 1 to 63, 2 for 64 to 127, 3 for 128 to 191, 4 for 192 to 254, 5 for 255 (values outside 0 to 255 are kept to the ends).</summary>
        public static int BucketOf(int value) => value <= 0 ? 0 : value < 64 ? 1 : value < 128 ? 2 : value < 192 ? 3 : value < 255 ? 4 : 5;

        public void Add(int value)
        {
            Buckets[BucketOf(value)]++;
            Count++;
        }

        public override string ToString() => $"0:{Buckets[0]} 1-63:{Buckets[1]} 64-127:{Buckets[2]} 128-191:{Buckets[3]} 192-254:{Buckets[4]} 255:{Buckets[5]}";
    }

    /// <summary>
    /// A count of ratios (0 to 1) in ten buckets of a tenth each (1.0 is in the last), with the ones that are not numbers or are outside 0 to 1 counted apart: for the log of the road's traffic
    /// flow figures (distance over duration; a road nobody has driven has 0 over 0). Pure.
    /// </summary>
    public sealed class RatioHistogram
    {
        public readonly int[] Tenths = new int[10];

        /// <summary>Not a number (nothing divided by nothing), infinite, below 0 or above 1.</summary>
        public int NotANumber, OutOfRange;

        public int Count { get; private set; }

        public void Add(float ratio)
        {
            Count++;
            if (float.IsNaN(ratio) || float.IsInfinity(ratio))
            {
                NotANumber++;
                return;
            }

            if (ratio < 0f || ratio > 1f)
            {
                OutOfRange++;
                return;
            }

            Tenths[Math.Min(9, (int)(ratio * 10f))]++;
        }

        /// <summary>The ratio of a total distance over a total duration; NaN when there was no duration (the road has no data), which Add counts apart.</summary>
        public static float Of(float distance, float duration) => duration > 0f ? distance / duration : float.NaN;

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Tenths.Length; i++)
                sb.Append(i == 0 ? "" : " ").Append((i * 10).ToString(CultureInfo.InvariantCulture)).Append('-').Append(((i + 1) * 10).ToString(CultureInfo.InvariantCulture)).Append("%:").Append(Tenths[i].ToString(CultureInfo.InvariantCulture));
            return sb.Append(" no-data:").Append(NotANumber.ToString(CultureInfo.InvariantCulture)).Append(" out-of-range:").Append(OutOfRange.ToString(CultureInfo.InvariantCulture)).ToString();
        }
    }
}
