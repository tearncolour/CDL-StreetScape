using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A sign on posts of its own on a raised strip between the carriageways (0.12.24; the owner: "有栏杆的情况下又放不下路牌杆了，也不能吸附到中间BRT车道的路沿"): a kerbed median or the island between a BRT lane
    /// and the others, as the road's cross-section tells (SurfaceSection.Raised). The sign stands on the strip under the cursor, for the traffic beside it on the cursor's side, its posts clear of
    /// what stands on the strip (a barrier's line in its middle). All laterals are from the road's centre line, positive to the right of its direction.
    /// </summary>
    public static class StripPost
    {
        /// <summary>A strip narrower than this (m) is a kerb with no room for a post.</summary>
        public const float MinWidth = 0.4f;

        /// <summary>A board's edge is kept this far in from the kerbs (m) where the strip is wide enough (GB 5768.2 4.8.10: 25 cm from the carriageway).</summary>
        public const float BoardInset = 0.25f;

        /// <summary>Between a post and a kerb's edge or a barrier (m).</summary>
        public const float PostGap = 0.1f;

        /// <summary>The strip under the cursor or within MarkingSnap.KerbInset of it, wide enough for a post; false when there is none.</summary>
        public static bool Under(IList<(float Lo, float Hi)> raised, float cursor, out float lo, out float hi)
        {
            lo = hi = 0f;
            if (raised == null || float.IsNaN(cursor))
                return false;
            foreach ((float a, float b) in raised)
            {
                if (cursor < a - MarkingSnap.KerbInset || cursor > b + MarkingSnap.KerbInset || b - a < MinWidth)
                    continue;
                lo = a;
                hi = b;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Where across the strip [lo, hi] the sign's origin goes. A narrow strip (MarkingSnap.NarrowStrip) has it in its middle; on a wide one it is where the cursor is, the board (half its width) BoardInset
        /// in from the kerbs when there is room. From there, the nearest place where each post (at origin + postX, of the radius) stands on the strip and clear of blocked (what stands on the
        /// strip, as laterals). clear is false when there is no such place: the place wanted is given.
        /// </summary>
        public static float Across(float lo, float hi, float cursor, float half, IReadOnlyList<float> postX, float radius, IReadOnlyList<(float Lo, float Hi)> blocked, out bool clear)
        {
            float middle = (lo + hi) * 0.5f, min = lo + half + BoardInset, max = hi - half - BoardInset;
            float wanted = hi - lo <= MarkingSnap.NarrowStrip || min > max ? middle : Math.Max(min, Math.Min(max, cursor));

            float keep = radius + PostGap, first = float.MaxValue, last = float.MinValue;
            foreach (float x in postX)
            {
                first = Math.Min(first, x);
                last = Math.Max(last, x);
            }

            var allowed = new List<(float Lo, float Hi)> { (lo + keep - first, hi - keep - last) };
            if (blocked != null)
            {
                foreach ((float b0, float b1) in blocked)
                {
                    foreach (float x in postX)
                        allowed = Without(allowed, b0 - keep - x, b1 + keep - x);
                }
            }

            float best = float.NaN, distance = float.MaxValue;
            foreach ((float a, float b) in allowed)
            {
                if (a > b)
                    continue;
                float at = Math.Max(a, Math.Min(b, wanted));
                if (Math.Abs(at - wanted) < distance)
                {
                    distance = Math.Abs(at - wanted);
                    best = at;
                }
            }

            clear = !float.IsNaN(best);
            return clear ? best : wanted;
        }

        /// <summary>The intervals with the open interval (cut0, cut1) taken out.</summary>
        private static List<(float Lo, float Hi)> Without(List<(float Lo, float Hi)> intervals, float cut0, float cut1)
        {
            var left = new List<(float Lo, float Hi)>(intervals.Count + 1);
            foreach ((float a, float b) in intervals)
            {
                if (cut1 <= a || cut0 >= b)
                {
                    left.Add((a, b));
                    continue;
                }

                if (cut0 > a)
                    left.Add((a, cut0));
                if (cut1 < b)
                    left.Add((cut1, b));
            }

            return left;
        }

        /// <summary>
        /// The traffic the sign is for: that of the lane nearest to the strip on the cursor's side of its middle, else on the other side (+1: the traffic that travels the way of the road's
        /// direction, -1 the other); lanesSide is the side of the strip that lane is on (+1 the larger laterals). With no lanes read, the side of the road the strip is on (traffic keeps to the
        /// right), its lanes on the driver's left.
        /// </summary>
        public static int Traffic(float lo, float hi, float cursor, IList<float> forward, IList<float> backward, out int lanesSide)
        {
            float middle = (lo + hi) * 0.5f;
            lanesSide = cursor >= middle ? 1 : -1;
            int near = NearestLane(lo, hi, lanesSide > 0, forward, backward);
            if (near == 0)
            {
                lanesSide = -lanesSide;
                near = NearestLane(lo, hi, lanesSide > 0, forward, backward);
            }

            if (near != 0)
                return near;
            int way = middle >= 0f ? 1 : -1;
            lanesSide = -way;
            return way;
        }

        /// <summary>
        /// A cantilever on the strip (0.12.25) reaches over the lanes of its traffic on lanesSide (Traffic): to the driver's left when the strip is on the traffic's right (the sign's +X, as from the
        /// roadside), else to the right (the mirrored arm, SupportCatalog.CantileverRight). The driver's right is the larger laterals for the traffic along the road's direction.
        /// </summary>
        public static bool ArmToRight(int traffic, int lanesSide) => lanesSide == traffic;

        /// <summary>The lanes (laterals) beyond the post the way toward (+1: the larger laterals): the ones an arm that reaches that way is over.</summary>
        public static List<float> Beyond(IList<float> lanes, float post, int toward)
        {
            var beyond = new List<float>();
            if (lanes == null)
                return beyond;
            foreach (float lateral in lanes)
            {
                if ((lateral - post) * toward > 0f)
                    beyond.Add(lateral);
            }

            return beyond;
        }

        private static int NearestLane(float lo, float hi, bool right, IList<float> forward, IList<float> backward)
        {
            int way = 0;
            float best = float.MaxValue;
            for (int list = 0; list < 2; list++)
            {
                IList<float> lanes = list == 0 ? forward : backward;
                if (lanes == null)
                    continue;
                foreach (float lateral in lanes)
                {
                    float d = right ? lateral - hi : lo - lateral;
                    if (d > 0f && d < best)
                    {
                        best = d;
                        way = list == 0 ? 1 : -1;
                    }
                }
            }

            return way;
        }
    }
}
