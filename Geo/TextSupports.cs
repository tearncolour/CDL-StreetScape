using System;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>Why the planner gave a text board the support it did (the log; the editor says it only when it switches to a gantry).</summary>
    public enum TextSupportReason
    {
        /// <summary>Not decided here: the planner's rules for any board (one post, two, three) apply.</summary>
        Standard,

        /// <summary>The text is wider than two posts carry (more than six metres): a gantry, not three posts.</summary>
        LongOnGantry,

        /// <summary>Wider than the longest gantry the mod builds: still a gantry, and the board hangs over its ends.</summary>
        TooLongForGantry
    }

    /// <summary>
    /// Which support a text light box gets by its length (0.12.76; the owner, 2 Nov: "过长的文本可以切换到龙门架支架"). GB 5768.2 4.8.12 a puts a rectangular board on two posts and, beyond six metres, on
    /// three (the planner's rule for any board: SupportPlanner.DoublePostMaxWidth); a line of lit letters that long is carried by a gantry here, a post on each side of the carriageway and a truss beam
    /// across it, from which the letters hang on their rear frame: this mod's choice for text, not a clause. Shorter text keeps the planner's rules (a post for a small board, two for a wide one), which count the
    /// ink of the letters and not the box. The player's own pick of a support is never changed.
    /// </summary>
    public static class TextSupports
    {
        /// <summary>The width (m) beyond which a text goes on a gantry: where the planner would pass from two posts to three.</summary>
        public const float GantryMinWidth = SupportPlanner.DoublePostMaxWidth;

        /// <summary>features.textSupports: off keeps what 0.12.75 did (the planner's rules for any board, studs straight to a wall, no carrier fixings).</summary>
        public static bool Enabled { get; set; } = true;

        public static TextSupportReason Choose(float width, out string supportId)
        {
            supportId = null;
            if (!Enabled || float.IsNaN(width) || width <= GantryMinWidth)
                return TextSupportReason.Standard;
            supportId = SupportCatalog.Gantry;
            return width > SupportLayouts.MaxSpan ? TextSupportReason.TooLongForGantry : TextSupportReason.LongOnGantry;
        }

        /// <summary>Whether a sign is a text light box's (a text board by its template).</summary>
        public static bool IsText(SignDef sign) => sign != null && sign.Template != null && sign.Template.Id == TextBoxes.TemplateId;

        /// <summary>The automatic support of a text board where the length decides (a gantry), else null: the planner goes on with its own rules.</summary>
        public static SupportDef Auto(SignDef sign)
        {
            if (!IsText(sign))
                return null;
            return Choose(sign.Width, out string id) == TextSupportReason.Standard ? null : SupportCatalog.ById(id);
        }

        /// <summary>The editor's one line when the length made it a gantry (null otherwise).</summary>
        public static string Hint(float width, bool zh)
        {
            switch (Choose(width, out _))
            {
                case TextSupportReason.LongOnGantry:
                    return zh ? "文字很长：自动支撑用门架。" : "A long text: the automatic support is a gantry.";
                case TextSupportReason.TooLongForGantry:
                    return zh ? $"文字超过门架最长跨度 {SupportLayouts.MaxSpan:0} m：请缩小字或减少字数。" : $"Text exceeds the longest gantry ({SupportLayouts.MaxSpan:0} m): shrink or shorten it.";
                default:
                    return null;
            }
        }
    }
}
