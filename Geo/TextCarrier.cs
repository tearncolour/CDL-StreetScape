using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>A channel of the support behind the letters' rails, in the frame plan's coordinates (metres from the middle of the text, x as the viewer sees it, y up). A horizontal one lies at the height Level from x From to x To; a vertical one at the x Level from y From to y To.</summary>
    public struct CarrierChannel
    {
        public bool Vertical;
        public float Level, From, To;
    }

    /// <summary>A bolt through a rail into the carrier: the middle of its head, where a rail meets a channel (frame plan coordinates).</summary>
    public struct CarrierBolt
    {
        public float X, Y;
    }

    /// <summary>How the rear frame of a text meets what carries it (TextCarrier): the channels the support has behind it, the bolts through the rails, and what could not be held.</summary>
    public sealed class CarrierPlan
    {
        public readonly List<CarrierChannel> Channels = new List<CarrierChannel>();
        public readonly List<CarrierBolt> Bolts = new List<CarrierBolt>();

        /// <summary>The places a rail lies over a channel, bolt or not; the rails no channel meets; the bolts left out because a stud is in the way.</summary>
        public int Crossings, UnheldRails, DroppedBolts;
    }

    /// <summary>
    /// Where the rear frame of channel letters is fixed to the support that carries them (0.12.76, DESIGN-wiring-plan.md F2). The frame (LetterFrame: studs, rails, ties) floats until something holds
    /// its rails: on a post the support's horizontal slide channels are put behind the frame's own top and bottom rails (and a middle one for a tall text), where the generic support puts them by the
    /// board's box and meets no rail; under an arm or a beam the support's vertical channels (SignBuilder.HangOnTubes) cross the rails, and the bolts are put where they do. The sizes (a bolt's head, how far
    /// apart bolts are) are this mod's estimates, not a standard's. Pure; the builder draws what it says.
    /// </summary>
    public static class TextCarrier
    {
        /// <summary>A bolt head's half side and thickness (m), the longest a rail goes between bolts (m: an estimate), and how far a post's channel reaches past the post (m).</summary>
        public const float BoltHalf = 0.011f, BoltThickness = 0.003f, BoltPitch = 1.2f, PostReach = 0.15f;

        /// <summary>The slide channel's width (m; SupportLayouts.RailWidth): a bolt stays inside it.</summary>
        public const float ChannelWidth = 0.068f;

        /// <summary>The gap between vertical channels across a board under an arm or a beam (SignBuilder.ChannelPitch).</summary>
        public const float ChannelPitch = 0.4f;

        /// <summary>The distance a bolt keeps from a stud's axis: its half diagonal and the stud's radius.</summary>
        private static float Clearance(float studRadius) => BoltHalf * 1.4143f + studRadius + 0.001f;

        /// <summary>
        /// The x of the vertical channels across a board of this width under an arm or a beam, from its middle (SignBuilder.HangOnTubes: they stand 6 cm in from the outline where the tubes cross, at most
        /// ChannelPitch apart, evenly; one at the middle of a board too narrow for two; reduced detail: two or three).
        /// </summary>
        public static float[] HungChannelXs(float boardWidth, bool reduced)
        {
            float half = Math.Min(boardWidth * 0.5f - ChannelWidth * 0.5f, Math.Max(0f, (boardWidth - 0.12f) * 0.5f));
            int channels = half < ChannelWidth ? 1 : reduced ? (boardWidth > 3f ? 3 : 2) : Math.Max(2, (int)Math.Ceiling(2f * half / ChannelPitch - 1e-3f) + 1);
            var xs = new float[channels];
            for (int i = 0; i < channels; i++)
                xs[i] = channels == 1 ? 0f : -half + 2f * half * i / (channels - 1);
            return xs;
        }

        /// <summary>The heights the carrier's channels go at on a post: the lowest rail, the highest, and for a text taller than TallText the rail nearest the middle.</summary>
        public static List<float> PostLevels(FramePlan frame)
        {
            var levels = new List<float>();
            if (frame == null || frame.Rails.Count == 0)
                return levels;
            float low = float.MaxValue, high = float.MinValue;
            foreach (FrameSegment rail in frame.Rails)
            {
                float y = (rail.A.Y + rail.B.Y) * 0.5f;
                low = Math.Min(low, y);
                high = Math.Max(high, y);
            }

            levels.Add(low);
            if (high - low > 1e-3f)
            {
                if (high - low > TallText)
                {
                    float middle = (low + high) * 0.5f, best = low, bestDistance = float.MaxValue;
                    foreach (FrameSegment rail in frame.Rails)
                    {
                        float y = (rail.A.Y + rail.B.Y) * 0.5f, d = Math.Abs(y - middle);
                        if (y > low + 0.05f && y < high - 0.05f && d < bestDistance)
                        {
                            best = y;
                            bestDistance = d;
                        }
                    }

                    if (best > low)
                        levels.Add(best);
                }

                levels.Add(high);
            }

            return levels;
        }

        /// <summary>A text taller than this (m) from its lowest rail to its highest has a channel between.</summary>
        public const float TallText = 1.2f;

        /// <summary>
        /// On a post (single, double, triple, or a host pole): a channel behind the frame's lowest and highest rail (and a middle one for a tall text), long enough to reach every post by PostReach and to run
        /// the length of the rails behind it, and bolts through the rails at each post and at most BoltPitch apart along the channel. poleX: the x of the posts as the viewer sees them from the
        /// text's middle.
        /// </summary>
        public static CarrierPlan Post(FramePlan frame, FrameStyle style, IReadOnlyList<float> poleX)
        {
            var plan = new CarrierPlan();
            List<float> levels = PostLevels(frame);
            if (levels.Count == 0)
                return plan;
            foreach (float level in levels)
            {
                float from = float.MaxValue, to = float.MinValue;
                foreach (FrameSegment rail in frame.Rails)
                {
                    if (Math.Abs((rail.A.Y + rail.B.Y) * 0.5f - level) > 0.02f)
                        continue;
                    from = Math.Min(from, Math.Min(rail.A.X, rail.B.X));
                    to = Math.Max(to, Math.Max(rail.A.X, rail.B.X));
                }

                if (from > to)
                    continue;
                if (poleX != null)
                {
                    foreach (float px in poleX)
                    {
                        from = Math.Min(from, px - PostReach);
                        to = Math.Max(to, px + PostReach);
                    }
                }

                plan.Channels.Add(new CarrierChannel { Vertical = false, Level = level, From = from, To = to });
            }

            AddBolts(plan, frame, style, poleX);
            CountHeld(plan, frame);
            return plan;
        }

        /// <summary>
        /// Under an arm or a beam (or on a signal's arm): the support's vertical channels across the board (HungChannelXs, from y bottom to y top of the board's box, in the plan's coordinates) and a
        /// bolt where a rail lies over each. A rail between two channels is held by neither (UnheldRails): it is held by the letters' studs on the rails that are.
        /// </summary>
        public static CarrierPlan Hung(FramePlan frame, FrameStyle style, float boardWidth, float boardHeight, bool reduced)
        {
            var plan = new CarrierPlan();
            if (frame == null || frame.Rails.Count == 0)
                return plan;
            // The generic channel runs from 10 % of the board's height above its lower edge to its top.
            float yFrom = -boardHeight * 0.4f, yTo = boardHeight * 0.5f;
            foreach (float x in HungChannelXs(boardWidth, reduced))
                plan.Channels.Add(new CarrierChannel { Vertical = true, Level = x, From = yFrom, To = yTo });
            AddBolts(plan, frame, style, null);
            CountHeld(plan, frame);
            return plan;
        }

        /// <summary>The bolts: where a rail lies over a vertical channel, and, for a horizontal channel, at the posts and every BoltPitch along it; none within Clearance of a stud.</summary>
        private static void AddBolts(CarrierPlan plan, FramePlan frame, FrameStyle style, IReadOnlyList<float> poleX)
        {
            float clear = Clearance(Math.Max(0.0015f, style.StudRadius));
            foreach (CarrierChannel channel in plan.Channels)
            {
                if (channel.Vertical)
                {
                    foreach (FrameSegment rail in frame.Rails)
                    {
                        float y = (rail.A.Y + rail.B.Y) * 0.5f, x0 = Math.Min(rail.A.X, rail.B.X), x1 = Math.Max(rail.A.X, rail.B.X);
                        if (y < channel.From || y > channel.To || channel.Level < x0 - 1e-4f || channel.Level > x1 + 1e-4f)
                            continue;
                        plan.Crossings++;
                        TryBolt(plan, frame, channel.Level, y, clear, Math.Min(ChannelWidth * 0.5f - BoltHalf, 0.02f), 0f);
                    }

                    continue;
                }

                foreach (FrameSegment rail in frame.Rails)
                {
                    float y = (rail.A.Y + rail.B.Y) * 0.5f, x0 = Math.Min(rail.A.X, rail.B.X), x1 = Math.Max(rail.A.X, rail.B.X);
                    if (Math.Abs(y - channel.Level) > 0.02f)
                        continue;
                    float a = Math.Max(x0, channel.From), b = Math.Min(x1, channel.To);
                    if (b <= a)
                        continue;
                    plan.Crossings++;
                    var at = new List<float>();
                    if (poleX != null)
                    {
                        foreach (float px in poleX)
                        {
                            if (px >= a && px <= b)
                                at.Add(px);
                        }
                    }

                    // The ends (inset a little) and enough between for BoltPitch.
                    float inset = Math.Min(0.06f, (b - a) * 0.25f);
                    int pieces = Math.Max(1, (int)Math.Ceiling((b - a - 2f * inset) / BoltPitch));
                    for (int i = 0; i <= pieces; i++)
                        at.Add(a + inset + (b - a - 2f * inset) * i / pieces);
                    foreach (float x in at)
                        TryBolt(plan, frame, x, y, clear, 0.04f, 0.04f);
                }
            }
        }

        /// <summary>A bolt at (x, y), or the nearest place within slide along the rail (and lift up or down) where no stud is within clear; none (counted) when there is no such place.</summary>
        private static void TryBolt(CarrierPlan plan, FramePlan frame, float x, float y, float clear, float slide, float lift)
        {
            foreach (float dx in new[] { 0f, slide * 0.5f, -slide * 0.5f, slide, -slide })
            {
                foreach (float dy in new[] { 0f, lift, -lift })
                {
                    float bx = x + dx, by = y + dy;
                    if (Near(frame, bx, by, clear) || TooCloseToBolt(plan, bx, by))
                        continue;
                    plan.Bolts.Add(new CarrierBolt { X = bx, Y = by });
                    return;
                }
            }

            plan.DroppedBolts++;
        }

        private static bool Near(FramePlan frame, float x, float y, float clear)
        {
            foreach (FrameStud stud in frame.Studs)
            {
                float dx = stud.At.X - x, dy = stud.At.Y - y;
                if (dx * dx + dy * dy < clear * clear)
                    return true;
            }

            return false;
        }

        private static bool TooCloseToBolt(CarrierPlan plan, float x, float y)
        {
            foreach (CarrierBolt bolt in plan.Bolts)
            {
                if (Math.Abs(bolt.X - x) < BoltHalf * 2f + 0.002f && Math.Abs(bolt.Y - y) < BoltHalf * 2f + 0.002f)
                    return true;
            }

            return false;
        }

        /// <summary>The rails that no channel of the plan lies behind: they are held by the letters' studs on the rails that are, and the editor does not say it.</summary>
        private static void CountHeld(CarrierPlan plan, FramePlan frame)
        {
            foreach (FrameSegment rail in frame.Rails)
            {
                float y = (rail.A.Y + rail.B.Y) * 0.5f, x0 = Math.Min(rail.A.X, rail.B.X), x1 = Math.Max(rail.A.X, rail.B.X);
                bool held = false;
                foreach (CarrierChannel channel in plan.Channels)
                {
                    if (channel.Vertical ? channel.Level >= x0 - 1e-4f && channel.Level <= x1 + 1e-4f && y >= channel.From && y <= channel.To
                        : Math.Abs(channel.Level - y) <= 0.02f && Math.Min(x1, channel.To) > Math.Max(x0, channel.From))
                    {
                        held = true;
                        break;
                    }
                }

                if (!held)
                    plan.UnheldRails++;
            }
        }
    }
}
