using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>What a lane of a road is to its cross-section: on the carriageway (cars, parking, tracks, cycles), a footway, or neither (a connection, a wire).</summary>
    public enum CrossLaneKind
    {
        Other = 0,
        Carriageway = 1,
        Pedestrian = 2
    }

    /// <summary>A lane across a road: where its middle is (m to the right of the edge's centre line) and how wide it is.</summary>
    public struct CrossLane
    {
        public float Lateral, Width;
        public CrossLaneKind Kind;

        public CrossLane(float lateral, float width, CrossLaneKind kind)
        {
            Lateral = lateral;
            Width = width;
            Kind = kind;
        }
    }

    /// <summary>
    /// The real width of a road (0.12.1): where its carriageway ends on each side (the kerb), read from its lanes, and where a sign's support stands for it. The game gives a road one width, its whole
    /// cross-section with the footways; a city road of 40 m with footways of 7 m has a carriageway of 26 m, and until 0.12.0 a post stood 0.6 m inside the 40 (6 m behind the kerb) and a gantry spanned
    /// 39 m. Now, on a side with a footway beyond the carriageway, the post stands so that the board's inner edge is Clearance behind the kerb (GB 5768.2 4.8.10: at least 0.25 m from the carriageway's
    /// outer edge; 0.5 m is this mod's choice), and never beyond where it stood before; on a side without one (a motorway's shoulder) it stands as before, near the outer edge of the road. Pure.
    /// </summary>
    public sealed class Carriageway
    {
        /// <summary>How far behind the kerb (m) the board's inner edge is by default.</summary>
        public const float Clearance = 0.5f;

        /// <summary>The player's margin (PlacementTool.Margin) that is the default; what it is set to more or less moves the post out or in by that much from where the default puts it.</summary>
        public const float DefaultMargin = -0.6f;

        /// <summary>The longest arm of a cantilever made by itself to reach over the lanes (m): longer ones are rare and stiff; not from the standard.</summary>
        public const float MaxAutoArm = 12f;

        public bool Ok;

        /// <summary>The lateral places of the carriageway's outer edges (Left below Right), and whether a footway lies beyond each.</summary>
        public float Left, Right;

        public bool FootwayLeft, FootwayRight;

        /// <summary>Half the road's whole width (the game's).</summary>
        public float RoadHalf;

        public static Carriageway Of(IList<CrossLane> lanes, float roadWidth)
        {
            var c = new Carriageway { RoadHalf = Math.Max(0f, float.IsNaN(roadWidth) ? 0f : roadWidth * 0.5f), Left = float.MaxValue, Right = float.MinValue };
            if (lanes == null)
                return c;
            foreach (CrossLane lane in lanes)
            {
                if (lane.Kind != CrossLaneKind.Carriageway || float.IsNaN(lane.Lateral) || float.IsInfinity(lane.Lateral) || !(lane.Width > 0f) || lane.Width > 20f)
                    continue;
                c.Left = Math.Min(c.Left, lane.Lateral - lane.Width * 0.5f);
                c.Right = Math.Max(c.Right, lane.Lateral + lane.Width * 0.5f);
            }

            if (c.Left > c.Right)
            {
                c.Left = c.Right = 0f;
                return c;
            }

            c.Ok = true;
            foreach (CrossLane lane in lanes)
            {
                if (lane.Kind != CrossLaneKind.Pedestrian || float.IsNaN(lane.Lateral))
                    continue;
                if (lane.Lateral > c.Right - 0.1f)
                    c.FootwayRight = true;
                if (lane.Lateral < c.Left + 0.1f)
                    c.FootwayLeft = true;
            }

            return c;
        }

        /// <summary>The kerb on a side (+1 right, -1 left): the carriageway's edge there, as a distance from the centre line (positive).</summary>
        public float Kerb(int side) => side > 0 ? Right : -Left;

        public bool Footway(int side) => side > 0 ? FootwayRight : FootwayLeft;

        /// <summary>
        /// How far from the centre line (m, positive, on the side) a support's post stands: innerReach is how far the board reaches from the post toward the road (SupportLayout.InnerReach), margin the
        /// player's margin. Without a footway, or with none read, as until 0.12.0: half the road's width, the margin and what the board reaches beyond 0.4 m (reachOut).
        /// </summary>
        public float PostDistance(int side, float innerReach, float margin, float reachOut)
        {
            float before = RoadHalf + margin + reachOut;
            if (!Ok || !Footway(side))
                return before;
            float atKerb = Kerb(side) + Clearance + Math.Max(0f, innerReach) + (margin - DefaultMargin);
            return Math.Max(0f, Math.Min(before, atKerb));
        }

        /// <summary>The span of a gantry whose first post is on a side: from that post to one standing as far out on the other side (each by its own kerb).</summary>
        public float GantrySpan(int side, float innerReach, float margin, float reachOut) =>
            PostDistance(side, innerReach, margin, reachOut) + PostDistance(-side, innerReach, margin, reachOut);

        /// <summary>
        /// The arm of a cantilever on a side whose board is to hang over the middle of the lanes it is for (lanes: their lateral places, those of the traffic that sees it): the distance from the post to
        /// that middle, and the half of the board and the 0.45 m the board keeps from the arm's free end (SupportLayouts), up to the next 10 cm; at most MaxAutoArm. 0 when no lane is given (the support's own arm).
        /// </summary>
        public static float CantileverArm(float postLateral, IList<float> lanes, float boardWidth)
        {
            if (lanes == null || lanes.Count == 0 || float.IsNaN(postLateral))
                return 0f;
            float low = float.MaxValue, high = float.MinValue;
            foreach (float v in lanes)
            {
                if (float.IsNaN(v) || float.IsInfinity(v))
                    continue;
                low = Math.Min(low, v);
                high = Math.Max(high, v);
            }

            if (low > high)
                return 0f;
            float middle = (low + high) * 0.5f;
            float arm = Math.Abs(postLateral - middle) + Math.Max(0f, boardWidth) * 0.5f + 0.45f;
            // Up to the next 10 cm: the board reaches at least as far as asked.
            return Math.Min(MaxAutoArm, (float)Math.Ceiling(arm * 10f - 1e-3f) / 10f);
        }
    }
}
