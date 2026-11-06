using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        /// <summary>
        /// What fixes a text's rear frame to its support (0.12.76, TextCarrier): on a post the slide channels behind the frame's own rails, long enough to reach each post, with a hoop where one meets a post
        /// and bolts through the rails; under an arm or a beam the bolts where the support's vertical channels (HangOnTubes) lie behind the rails. The board is in front of the support's back plane; the
        /// channel's front is that plane, the rails' back: they touch there. The generic channels of the support are not built for a board that has these (SignParts.TextOwns).
        /// </summary>
        private static void AddTextCarrier(SignParts parts, SupportLayout layout, Placement placement, TextPlanResult plan, Signs.TextBoard text, float cx, float cy, float backZ)
        {
            if (!Signs.TextSupports.Enabled || plan.Frame == null || plan.Frame.Rails.Count == 0 || placement.OnHead || placement.OnPost)
                return;
            SupportKind kind = layout.Kind;
            if (SupportKinds.OnWall(kind) || SupportKinds.OverRoad(kind) && !layout.Hanger)
                return;
            FrameStyle style = FrameStyle.For(text.Job.LetterHeight);
            float size = Math.Max(0.006f, style.RailSize);
            GuidePart main = placement.Board.Instance.Board.Main;
            CarrierPlan carrier;
            if (SupportKinds.Hung(kind) || SupportKinds.OnArm(kind) || layout.Hanger)
            {
                carrier = TextCarrier.Hung(plan.Frame, style, main.W, main.H, t_Reduced);
            }
            else
            {
                var poles = new List<float>();
                foreach (float px in layout.PoleX)
                    poles.Add(cx - px);
                carrier = TextCarrier.Post(plan.Frame, style, poles);
                parts.TextOwns = parts.TextOwns ?? new HashSet<Placement>();
                parts.TextOwns.Add(placement);
                float depth = SupportLayouts.RailDepth, width = SupportLayouts.RailWidth;
                foreach (CarrierChannel channel in carrier.Channels)
                {
                    float y = cy + channel.Level, x0 = cx - channel.To, x1 = cx - channel.From;
                    Solids.Box(parts.Metal, new Vec3((x0 + x1) * 0.5f, y, backZ - depth * 0.5f), new Vec3((x1 - x0) * 0.5f, width * 0.5f, depth * 0.5f));
                    foreach (float px in layout.PoleX)
                    {
                        if (px >= x0 - 1e-4f && px <= x1 + 1e-4f)
                            Hoop(parts, px, y, layout.PoleRadius, backZ - depth);
                    }
                }
            }

            foreach (CarrierBolt bolt in carrier.Bolts)
                Solids.Box(parts.Metal, new Vec3(cx - bolt.X, cy + bolt.Y, backZ + size + TextCarrier.BoltThickness * 0.5f), new Vec3(TextCarrier.BoltHalf, TextCarrier.BoltHalf, TextCarrier.BoltThickness * 0.5f));
        }
    }
}
