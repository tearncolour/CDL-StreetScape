using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The player's distance of a support from the kerb (PlacementTool.Margin; 0.12.74, the owner has no numpad: "改成<>调整整个支架和路缘的距离"): the range, the 0.1 m step the keys and the slider
    /// share, and, for a post that stands, the margin it would have been placed with.
    /// </summary>
    public static class KerbMargin
    {
        public const float Min = -8f, Max = 6f, Step = 0.1f;

        /// <summary>A margin as the keys and the slider may set it: whole tenths of a metre inside the range, so that a thousand presses never drift. Not a number: the default.</summary>
        public static float Clamp(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return Carriageway.DefaultMargin;
            float tenths = (float)Math.Round(Math.Max(Min, Math.Min(Max, value)) * 10.0, MidpointRounding.AwayFromZero);
            return tenths / 10f;
        }

        /// <summary>One press of a key: direction +1 (farther out, &gt;) or -1 (nearer the road, &lt;).</summary>
        public static float Stepped(float current, int direction) => Clamp(Clamp(current) + Math.Sign(direction) * Step);

        /// <summary>Only a post (one, two or three) is moved across its road by the margin: an arm, a beam, a wall or another mod's pole depends on more than the post's distance.</summary>
        public static bool Applies(SupportKind kind) => kind == SupportKind.SinglePole || kind == SupportKind.DoublePole || kind == SupportKind.TriplePole;

        /// <summary>
        /// The supports whose post stands at a margin from the kerb when it is placed (Carriageway.PostDistance), so that the editor moves it by the same margin (0.12.85): a post, the post of an arm, of an
        /// F arm and of a gantry (its span follows, SupportShift.SpanAtMargin). A double cantilever's post stands where the cursor was across the road: it has the distance from the centre line.
        /// </summary>
        public static bool Edits(SupportKind kind) => Applies(kind) || kind == SupportKind.Cantilever || kind == SupportKind.FCantilever || kind == SupportKind.Gantry;

        /// <summary>
        /// The margin a post that stands at distance m from its road's centre line would have been placed with, given the distance placing gives at the default margin (Carriageway.PostDistance):
        /// the distance is that one plus what the margin is over the default, in both of its cases (before and at the kerb). NaN when it is not a margin the player can set, which is how a post on a
        /// strip between the carriageways is told from one at the roadside.
        /// </summary>
        public static float Of(float distance, float distanceAtDefault)
        {
            float margin = Carriageway.DefaultMargin + (Math.Abs(distance) - distanceAtDefault);
            return float.IsNaN(margin) || margin < Min - 0.05f || margin > Max + 0.05f ? float.NaN : margin;
        }

        /// <summary>How far (m, + away from the road) a post is to be moved across to stand at the margin target from where it stands at current (the margin it has, Of).</summary>
        public static float Across(float target, float current) => Clamp(target) - current;
    }
}
