using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>Whether a text light box on a building has its base frame (托架框) or is fixed by its letters' own studs only.</summary>
    public enum TextMount
    {
        /// <summary>The planner's frame: rails behind the letters, spacers from the rails to the wall (or legs from them to the roof), diagonal braces for a large text.</summary>
        Auto,

        /// <summary>No base frame: the letters' studs go straight to the wall (or the letters stand on feet on a roof).</summary>
        None
    }

    /// <summary>What a text on a wall is fixed with (TextWallFrame.Plan): the spacers from the rails to the wall, the diagonal braces between a line's two rails, and what the studs alone can carry.</summary>
    public sealed class WallFramePlan
    {
        /// <summary>The middles of the spacers on the rails (frame plan coordinates), each from the rail's back to the wall.</summary>
        public readonly List<Point2> Standoffs = new List<Point2>();

        /// <summary>The diagonals between the two rails of a line, in the rails' plane.</summary>
        public readonly List<FrameSegment> Braces = new List<FrameSegment>();

        /// <summary>The letters carry a frame: braces are built.</summary>
        public bool Large;

        /// <summary>No base frame: the studs reach the wall themselves.</summary>
        public bool StudsToWall;

        /// <summary>No base frame, and the studs carry more than StudCapacityKg each (an estimate): the editor says so in one line.</summary>
        public bool TooHeavyForStuds;

        /// <summary>The mass each stud carries when there is no frame (kg).</summary>
        public float KgPerStud;

        /// <summary>The spacers and braces left out because a stud is in the way.</summary>
        public int DroppedStandoffs, DroppedBraces;
    }

    /// <summary>
    /// The base frame of a text light box on a building's wall (0.12.76; the owner: "建筑吸附的时候也进行支架规划器，有底架子"): the rails of the letters' rear frame (LetterFrame) are the frame, held off the
    /// wall by spacers at the rails' ends and every StandoffPitch along them, and braced diagonally between a line's two rails where the text is large. Without the frame (the owner's "可以选择不放托架框"
    /// the letters' own studs reach the wall: the planner still counts the load of each and says when it is more than a stud carries. The pitch, the load and the sizes are this mod's estimates, not a
    /// standard's. Pure.
    /// </summary>
    public static class TextWallFrame
    {
        /// <summary>The most a rail goes between spacers (m), how far in from a rail's end the first stands (m), and the filled area (m2) or the longest rail (m) from which a text is large.</summary>
        public const float StandoffPitch = 1.0f, StandoffInset = 0.08f, LargeArea = 1.5f, LargeRail = 2.4f;

        /// <summary>What a stud on its own carries (kg): an estimate for an M6 stud screwed into a plug in masonry, with a margin.</summary>
        public const float StudCapacityKg = 5f;

        /// <summary>The radius of a spacer and of a brace (m).</summary>
        public const float StandoffRadius = 0.010f, BraceRadius = 0.006f;

        public static bool IsLarge(FramePlan frame)
        {
            if (frame == null)
                return false;
            if (frame.EffectiveArea > LargeArea)
                return true;
            foreach (FrameSegment rail in frame.Rails)
            {
                if (rail.Length > LargeRail)
                    return true;
            }

            return false;
        }

        public static WallFramePlan Plan(FramePlan frame, FrameStyle style, TextMount mount)
        {
            var plan = new WallFramePlan();
            if (frame == null)
                return plan;
            plan.Large = IsLarge(frame);
            if (mount == TextMount.None)
            {
                plan.StudsToWall = true;
                plan.KgPerStud = frame.Studs.Count > 0 ? frame.MassKg / frame.Studs.Count : 0f;
                plan.TooHeavyForStuds = plan.KgPerStud > StudCapacityKg;
                return plan;
            }

            float stud = Math.Max(0.0015f, style.StudRadius);
            float clear = StandoffRadius + stud + 0.002f;
            foreach (FrameSegment rail in frame.Rails)
            {
                float x0 = Math.Min(rail.A.X, rail.B.X), x1 = Math.Max(rail.A.X, rail.B.X), y = (rail.A.Y + rail.B.Y) * 0.5f;
                float inset = Math.Min(StandoffInset, (x1 - x0) * 0.25f);
                int pieces = Math.Max(1, (int)Math.Ceiling((x1 - x0 - 2f * inset) / StandoffPitch));
                for (int i = 0; i <= pieces; i++)
                {
                    float x = x0 + inset + (x1 - x0 - 2f * inset) * i / pieces;
                    TryStandoff(plan, frame, x, y, clear);
                }
            }

            if (plan.Large)
                AddBraces(plan, frame, stud);
            return plan;
        }

        private static void TryStandoff(WallFramePlan plan, FramePlan frame, float x, float y, float clear)
        {
            foreach (float dx in new[] { 0f, 0.03f, -0.03f, 0.06f, -0.06f })
            {
                float sx = x + dx;
                bool free = true;
                foreach (FrameStud stud in frame.Studs)
                {
                    float ex = stud.At.X - sx, ey = stud.At.Y - y;
                    if (ex * ex + ey * ey < clear * clear)
                    {
                        free = false;
                        break;
                    }
                }

                foreach (Point2 other in plan.Standoffs)
                {
                    if (free && Math.Abs(other.X - sx) < 2f * StandoffRadius + 0.004f && Math.Abs(other.Y - y) < 2f * StandoffRadius + 0.004f)
                        free = false;
                }

                if (free)
                {
                    plan.Standoffs.Add(new Point2(sx, y));
                    return;
                }
            }

            plan.DroppedStandoffs++;
        }

        /// <summary>Diagonals between the low and the high rail of each line, alternating in direction (a Warren truss), each clear of every stud.</summary>
        private static void AddBraces(WallFramePlan plan, FramePlan frame, float stud)
        {
            float clear = BraceRadius + stud + 0.003f;
            foreach ((int Line, float Low, float High) heights in frame.RailHeights)
            {
                float a = float.MinValue, b = float.MaxValue;
                bool low = false, high = false;
                float lowFrom = float.MaxValue, lowTo = float.MinValue, highFrom = float.MaxValue, highTo = float.MinValue;
                foreach (FrameSegment rail in frame.Rails)
                {
                    if (rail.Line != heights.Line)
                        continue;
                    float y = (rail.A.Y + rail.B.Y) * 0.5f, x0 = Math.Min(rail.A.X, rail.B.X), x1 = Math.Max(rail.A.X, rail.B.X);
                    if (Math.Abs(y - heights.Low) < 1e-3f)
                    {
                        low = true;
                        lowFrom = Math.Min(lowFrom, x0);
                        lowTo = Math.Max(lowTo, x1);
                    }
                    else if (Math.Abs(y - heights.High) < 1e-3f)
                    {
                        high = true;
                        highFrom = Math.Min(highFrom, x0);
                        highTo = Math.Max(highTo, x1);
                    }
                }

                if (!low || !high)
                    continue;
                a = Math.Max(lowFrom, highFrom);
                b = Math.Min(lowTo, highTo);
                float gap = heights.High - heights.Low;
                if (b - a < gap * 1.2f || gap < 0.05f)
                    continue;
                int bays = Math.Max(1, (int)Math.Round((b - a) / (2f * gap)));
                for (int i = 0; i < bays * 2; i++)
                {
                    float xa = a + (b - a) * i / (bays * 2), xb = a + (b - a) * (i + 1) / (bays * 2);
                    bool up = i % 2 == 0;
                    TryBrace(plan, frame, heights.Line, up ? new Point2(xa, heights.Low) : new Point2(xa, heights.High), up ? new Point2(xb, heights.High) : new Point2(xb, heights.Low), clear);
                }
            }
        }

        private static void TryBrace(WallFramePlan plan, FramePlan frame, int line, Point2 from, Point2 to, float clear)
        {
            foreach (float shift in new[] { 0f, 0.04f, -0.04f, 0.08f, -0.08f, 0.14f, -0.14f })
            {
                var a = new Point2(from.X + shift, from.Y);
                var b = new Point2(to.X + shift, to.Y);
                bool free = true;
                foreach (FrameStud stud in frame.Studs)
                {
                    if (DistanceToSegment(stud.At, a, b) < clear)
                    {
                        free = false;
                        break;
                    }
                }

                if (free)
                {
                    plan.Braces.Add(new FrameSegment { A = a, B = b, Line = line });
                    return;
                }
            }

            plan.DroppedBraces++;
        }

        /// <summary>The distance from a point to a segment (m).</summary>
        public static float DistanceToSegment(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-12 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            double ex = a.X + dx * t - p.X, ey = a.Y + dy * t - p.Y;
            return (float)Math.Sqrt(ex * ex + ey * ey);
        }
    }

    /// <summary>A leg from the roof up to the frame's top rail (frame plan x; the heights are above the roof).</summary>
    public struct RoofLeg
    {
        public float X, Top;
    }

    /// <summary>A rear brace: from a leg at height From down and back to a foot on the roof Depth behind the board's back plane.</summary>
    public struct RoofBrace
    {
        public float X, From, Depth;
    }

    /// <summary>The frame of a text standing on a roof (TextRoofFrame.Plan).</summary>
    public sealed class RoofFramePlan
    {
        public readonly List<RoofLeg> Legs = new List<RoofLeg>();
        public readonly List<RoofBrace> Braces = new List<RoofBrace>();

        /// <summary>No base frame: a leg from the roof to the lowest stud of each letter (the middle of the plan x, the stud's height above the roof).</summary>
        public readonly List<RoofLeg> Feet = new List<RoofLeg>();

        /// <summary>A single line of text stood on feet (mount none); false when it was refused (more than one line: the frame is built).</summary>
        public bool OnFeet;

        /// <summary>The reason a frame-less roof mount was refused, or "".</summary>
        public string Refused = "";

        /// <summary>The footprint on the roof in the plan's x and the board's z behind its back plane (m, positive behind): the least x, the largest x and the deepest brace foot.</summary>
        public float MinX, MaxX, DepthBehind;

        /// <summary>The height of the highest point above the roof (m).</summary>
        public float Top;
    }

    /// <summary>
    /// A text light box standing on the top of a building (0.12.76; the owner: "可以选择不放托架框，然后能支持放到建筑顶部"): the board's lower edge LowerEdge above the roof, legs from the roof to the frame's
    /// top rail under its ends and at most LegPitch apart, rear braces at the outer legs (and every BracePitch between) for a tall or wide text, each standing on a foot plate; or, without the frame, a
    /// leg from the roof to the lowest stud of each letter of a single line. The sizes are this mod's estimates. Pure.
    /// </summary>
    public static class TextRoofFrame
    {
        public const float LegPitch = 2.0f, BracePitch = 3.0f, LegInset = 0.1f, TallTop = 1.2f, WideText = 3.0f, FootHalf = 0.08f, FootThickness = 0.012f, LegHalf = 0.02f, FeetRadius = 0.007f;

        /// <summary>How far behind the board's back plane the rear braces of a roof frame go (m) for a text whose top is this high above the roof and which is this wide: none for a low, narrow one.</summary>
        public static float DepthFor(float top, float width) => top > TallTop || width > WideText ? Math.Max(0.4f, Math.Min(2.0f, 0.6f * top)) : 0f;

        /// <summary>
        /// lowerEdge: the height of the text block's lower edge above the roof; boardHeight: the text block's height (the frame plan's y = 0 is its middle); lines: how many lines the text has.
        /// </summary>
        public static RoofFramePlan Plan(FramePlan frame, FrameStyle style, TextMount mount, float lowerEdge, float boardHeight, int lines)
        {
            var plan = new RoofFramePlan();
            if (frame == null || frame.Rails.Count == 0)
                return plan;
            float low = Math.Max(0f, lowerEdge), lift = low + boardHeight * 0.5f;
            float from = float.MaxValue, to = float.MinValue, topRail = float.MinValue;
            foreach (FrameSegment rail in frame.Rails)
            {
                from = Math.Min(from, Math.Min(rail.A.X, rail.B.X));
                to = Math.Max(to, Math.Max(rail.A.X, rail.B.X));
                topRail = Math.Max(topRail, (rail.A.Y + rail.B.Y) * 0.5f);
            }

            plan.MinX = from - 0.1f;
            plan.MaxX = to + 0.1f;
            float top = lift + topRail + 0.03f;
            plan.Top = low + boardHeight;
            if (mount == TextMount.None)
            {
                if (lines > 1)
                {
                    plan.Refused = "more than one line";
                }
                else
                {
                    // One foot under the lowest stud of each letter.
                    var lowest = new Dictionary<int, FrameStud>();
                    foreach (FrameStud stud in frame.Studs)
                    {
                        if (!lowest.TryGetValue(stud.Island, out FrameStud held) || stud.At.Y < held.At.Y)
                            lowest[stud.Island] = stud;
                    }

                    var feet = new List<RoofLeg>();
                    foreach (FrameStud stud in lowest.Values)
                        feet.Add(new RoofLeg { X = stud.At.X, Top = lift + stud.At.Y });
                    feet.Sort((a, b) => a.X.CompareTo(b.X));
                    // Two feet closer than a foot plate are one: the second is dropped.
                    foreach (RoofLeg foot in feet)
                    {
                        if (plan.Feet.Count == 0 || foot.X - plan.Feet[plan.Feet.Count - 1].X >= 2f * FootHalf + 0.01f)
                            plan.Feet.Add(foot);
                    }

                    plan.OnFeet = plan.Feet.Count > 0;
                    if (plan.OnFeet)
                        return plan;
                    plan.Refused = "no letter to stand";
                }
            }

            float span = to - from;
            float inset = Math.Min(LegInset, span * 0.25f);
            int pieces = Math.Max(1, (int)Math.Ceiling((span - 2f * inset) / LegPitch));
            for (int i = 0; i <= pieces; i++)
                plan.Legs.Add(new RoofLeg { X = from + inset + (span - 2f * inset) * i / pieces, Top = top });
            if (top > TallTop || span > WideText)
            {
                float depth = DepthFor(top, span);
                plan.DepthBehind = depth;
                for (int i = 0; i < plan.Legs.Count; i++)
                {
                    bool outer = i == 0 || i == plan.Legs.Count - 1;
                    if (outer || (i % 2 == 0 && span > BracePitch))
                        plan.Braces.Add(new RoofBrace { X = plan.Legs[i].X, From = top * 0.8f, Depth = depth });
                }
            }

            return plan;
        }
    }
}
