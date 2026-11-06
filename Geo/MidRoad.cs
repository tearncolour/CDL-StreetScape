using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A sign on posts of its own standing in the road itself (the owner: "这种路牌应该能摆在路中间"; the mandatory signs 示7 and 示8 stand at the end of a traffic island, a refuge island or a central
    /// median, GB 5768.2 6.6): where across the road the post stands when the player asks for it (the "/" key), and which traffic the board faces. All laterals are metres from the road's centre line,
    /// positive to the right of its direction. A raised strip in the cross-section is StripPost's: this is for the carriageway where the cross-section has none. GB 5768.2 4.8.10 asks 25 cm from the
    /// edge of a carriageway or an island for a roadside sign; where the post is in the carriageway the player has decided that there is an island. Pure.
    /// </summary>
    public static class MidRoad
    {
        /// <summary>The centre line draws the post to it within this (m).</summary>
        public const float CentreMagnet = 0.35f;

        /// <summary>The cursor is over the road this far (m) beyond the carriageway's edges: past it the post is at the road's side as before.</summary>
        public const float Reach = 0.3f;

        /// <summary>The post keeps this far (m) in from the carriageway's edges.</summary>
        public const float EdgeGap = 0.3f;

        /// <summary>Only the posts (one, two or three) are put across a road by hand; an arm, a beam, a wall or another mod's pole are not.</summary>
        public static bool Takes(SupportKind kind) => KerbMargin.Applies(kind);

        private static void Span(bool carriagewayOk, float left, float right, float roadHalf, out float lo, out float hi)
        {
            lo = carriagewayOk ? left : -roadHalf;
            hi = carriagewayOk ? right : roadHalf;
        }

        /// <summary>Whether the cursor is over the road's carriageway (its edges as read, else half the road's width each side of the centre line), Reach beyond; NaN is not (every comparison with it is false).</summary>
        public static bool Inside(float cursor, bool carriagewayOk, float left, float right, float roadHalf)
        {
            Span(carriagewayOk, left, right, roadHalf, out float lo, out float hi);
            return hi - lo > 2f * EdgeGap && cursor >= lo - Reach && cursor <= hi + Reach;
        }

        /// <summary>The lateral of the post: the cursor's, EdgeGap in from the edges, drawn to the centre line within CentreMagnet when that is on the carriageway.</summary>
        public static float Lateral(float cursor, bool carriagewayOk, float left, float right, float roadHalf)
        {
            Span(carriagewayOk, left, right, roadHalf, out float lo, out float hi);
            float min = lo + EdgeGap, max = hi - EdgeGap;
            float at = min > max ? (lo + hi) * 0.5f : Math.Max(min, Math.Min(max, cursor));
            return Math.Abs(at) <= CentreMagnet && lo <= 0f && hi >= 0f ? 0f : at;
        }

        /// <summary>
        /// The traffic the board faces: that of the lane nearest to the post (+1: the traffic that travels the way of the road's direction, which the board faces back at; -1 the other); on a tie, or with
        /// no lanes read, the traffic on the post's side of the centre line (the right of the road's direction is the forward traffic's).
        /// </summary>
        public static int Traffic(float lateral, IList<float> forward, IList<float> backward)
        {
            int side = lateral >= 0f ? 1 : -1;
            float best = float.MaxValue;
            int way = 0;
            Nearest(forward, 1, lateral, ref best, ref way);
            Nearest(backward, -1, lateral, ref best, ref way);
            return way == 0 || way == Tie ? side : way;
        }

        /// <summary>Marks two lanes of opposite traffic as near as each other.</summary>
        private const int Tie = int.MinValue;

        private static void Nearest(IList<float> lanes, int traffic, float lateral, ref float best, ref int way)
        {
            if (lanes == null)
                return;
            foreach (float lane in lanes)
            {
                if (float.IsNaN(lane) || float.IsInfinity(lane))
                    continue;
                float d = Math.Abs(lane - lateral);
                if (d < best - 1e-3f)
                {
                    best = d;
                    way = traffic;
                }
                else if (d <= best + 1e-3f && way != traffic)
                {
                    way = Tie;
                }
            }
        }
    }
}
