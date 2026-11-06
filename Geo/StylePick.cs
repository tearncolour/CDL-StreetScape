using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The eyedropper of the dock (0.12.88; the owner: a button under the three of the dock, click a placed item to reuse its style and settings, then place). The tool's mode of it, and what a pick
    /// takes of the item besides its entry, its written settings, its support, its tilt and its paint (those go through Choose, ChooseSupport and the tool's own fields): the span or arm and the
    /// clearance the player had set by hand for the support, and the distance of a post from the kerb. Where the item stands, which way it faces, which road it is linked to and how its boards are
    /// stacked belong to the place it was put and are not taken. Pure.
    /// </summary>
    public static class StylePick
    {
        /// <summary>The tool's mode of the eyedropper (PlacementTool.Mode): 0 is placing, 1 deleting and 2 editing.</summary>
        public const int Mode = 3;

        /// <summary>The mode a request of the panel means: the three the tool had, the eyedropper, and anything else is placing.</summary>
        public static int ModeOf(int requested) => requested == 1 || requested == 2 || requested == Mode ? requested : 0;

        /// <summary>
        /// Whether pointing at the items passes the paint over: placing looks for a support to join and does (a sign put over a marked road joined the marking, 0.12.12); deleting, editing, the
        /// eyedropper and Ctrl while placing take paint as well (Ctrl while placing could not pick a marking before 0.12.88).
        /// </summary>
        public static bool PassesPaint(int mode, bool picking) => mode == 0 && !picking;

        /// <summary>The part of the key of the last answer of the pointing that tells its readings apart: the same ray gives another answer in another mode, and with Ctrl held.</summary>
        public static int PointingKey(int mode, bool picking) => mode * 2 + (picking ? 1 : 0);

        /// <summary>What a pick takes besides the entry: a span or arm and a clearance of 0 are the planner's and the support's own again.</summary>
        public struct Carried
        {
            public float Span, Clearance;
            public bool HasMargin;
            public float Margin;
        }

        /// <summary>
        /// The span or arm (only one the player set: an automatic one is the planner's again for the next support) and the clearance of a hung support, and the margin of a post, a cantilever or a gantry
        /// that stands at a road's side (margin: KerbMargin.Of, NaN where it cannot be read: on a strip between the carriageways, on a road that is not there any more). A support that has none of these
        /// carries none.
        /// </summary>
        public static Carried Of(SupportKind kind, bool reachByHand, bool hasBaseReach, float baseReach, float reach, float clearance, float margin)
        {
            // SpanSlider answers NaN for a support that has no span or clearance (a post, a wall, a host), which is none here.
            var carried = new Carried
            {
                Span = reachByHand ? Zero(SpanSlider.Typed(kind, hasBaseReach ? baseReach : reach)) : 0f,
                Clearance = Zero(SpanSlider.TypedClearance(kind, clearance))
            };

            if (KerbMargin.Edits(kind) && !float.IsNaN(margin) && !float.IsInfinity(margin))
            {
                carried.HasMargin = true;
                carried.Margin = KerbMargin.Clamp(margin);
            }

            return carried;
        }

        private static float Zero(float value) => float.IsNaN(value) ? 0f : value;
    }
}
