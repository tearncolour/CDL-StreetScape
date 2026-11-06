using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>How the letters of a text light box that leans are held (TextTilt.ModeOf): on the triangular frames of the standing light box's planner, on those frames on a roof, on spacers from a wall.</summary>
    public enum TextLeanMode
    {
        /// <summary>It does not lean (the tilt is 0, or the support cannot carry a leaning text): it is built as it always was.</summary>
        None,

        /// <summary>On a single, a double or a triple post: a post and a triangular frame at each (StandPlanner), the letters' rails on arms from the posts.</summary>
        Stand,

        /// <summary>On a roof (the roof support): the same frames stand on the roof behind the letters.</summary>
        Roof,

        /// <summary>On a flat wall: the top rail keeps its distance from the wall, the lower ones are further out, each on a spacer of its own length.</summary>
        Wall
    }

    /// <summary>
    /// The elevation angle of the text light box's letters (the owner, 2 Nov: "第一种纯字形灯箱也应该支持仰角"): they lean back from upright, their lit faces looking up a little, as the standing light box's do.
    /// The plan of a text (glyphs, rear frame) is made in the board's plane and does not depend on the angle; the builder turns what it built about a line along X: the lower back edge on a post and
    /// on a roof, the top rail's back on a wall. Pure: which supports carry it, where the arms meet the rails, and the turn.
    /// </summary>
    public static class TextTilt
    {
        /// <summary>The most the letters lean (degrees): the standing light box's range. The same number the other way is the most they look down (the depression angle, a negative tilt).</summary>
        public const float MaxDeg = 30f;

        /// <summary>The least that is a lean (degrees); under it the text is built upright, as it always was.</summary>
        public const float MinDeg = 0.05f;

        /// <summary>
        /// Which way a text with this tilt is held on a support. flatWall: the wall is flat (no radius was fitted to it), and textSupports: the 0.12.76 supports are on (the wall and roof frames are theirs);
        /// standTaken: another board already made the support's frame a standing light box's.
        /// </summary>
        public static TextLeanMode ModeOf(SupportKind kind, bool roof, float tiltDeg, bool triangular, bool textSupports, bool flatWall, bool standTaken)
        {
            bool leans = !float.IsNaN(tiltDeg) && Math.Abs(tiltDeg) >= MinDeg;
            // A text that looks down is not carried on a roof: the box would lean out over the roof's edge, and the frames of a roof stand behind it.
            if (roof && tiltDeg <= -MinDeg)
                return TextLeanMode.None;
            if (!leans && !triangular)
                return TextLeanMode.None;
            if (kind == SupportKind.SinglePole || kind == SupportKind.DoublePole || kind == SupportKind.TriplePole)
                return standTaken ? TextLeanMode.None : TextLeanMode.Stand;
            if (kind == SupportKind.WallFlat && textSupports)
            {
                if (roof)
                    return standTaken ? TextLeanMode.None : TextLeanMode.Roof;
                return leans && flatWall ? TextLeanMode.Wall : TextLeanMode.None;
            }

            return TextLeanMode.None;
        }

        /// <summary>
        /// The x (sign frame) of the frames that carry a text standing on a roof: at most TextRoofFrame.LegPitch apart, from LegInset in from one end of the board to LegInset in from the other, one at the
        /// middle of a board too narrow for two.
        /// </summary>
        public static float[] RoofFrameX(float boardWidth, float cx)
        {
            float inset = Math.Min(TextRoofFrame.LegInset, boardWidth * 0.25f), span = Math.Max(0f, boardWidth - 2f * inset);
            int pieces = Math.Max(1, (int)Math.Ceiling(span / TextRoofFrame.LegPitch));
            if (span < 0.2f)
                return new[] { cx };
            var xs = new float[pieces + 1];
            for (int i = 0; i <= pieces; i++)
                xs[i] = cx - span * 0.5f + span * i / pieces;
            return xs;
        }

        /// <summary>Where a point of the upright board (height y, depth z) is once the board is turned back about the line along X at (y0, z0); s and c are the sine and cosine of the angle (the turn of SignBuilder.LeanBack).</summary>
        public static void Turned(float y, float z, float y0, float z0, float s, float c, out float yOut, out float zOut)
        {
            float dy = y - y0, dz = z - z0;
            yOut = y0 + dy * c + dz * s;
            zOut = z0 - dy * s + dz * c;
        }

        /// <summary>The least a rail's back stands out further than the top rail's (m) for its spacer to have a tie, the tie's radius, and how far to the side of its spacer the tie runs (m).</summary>
        public const float TieMinReach = 0.1f, TieRadius = 0.007f, TieSide = 0.03f;

        /// <summary>A tie of a leaned text on a wall: from (WallY, WallZ) to the back of a rail at (Y, Z), in the vertical plane at X (frame x, as the plan's). Its start is on the wall: the end ring of the tube is square to its axis, so the start is moved along it until the ring's lowest point is at z = 0.</summary>
        public struct WallTie
        {
            public float X, WallY, WallZ, Y, Z;
        }

        /// <summary>
        /// The ties of a text that leans on a wall. A spacer that is horizontal and long carries the weight of the lower rails in bending, which a rod of its size cannot do; so a rail whose back stands out
        /// TieMinReach beyond the top rail's has a tie to the wall from above, at 45°, beside its spacer in a plane TieSide (or twice it) off, on its rail and clear of every other spacer and tie. The
        /// anchor is higher than the rail by the rail's reach, which is under the board's top while the tilt is under 45° (MaxDeg is 30°), so no tie crosses the board; ties of one column are parallel, so
        /// they do not cross each other. The turned heights and depths are the builder's: cyUp the board's middle, (yA, zA) the lower back edge, topZ the depth of the top of the back plane after the turn.
        /// </summary>
        public static List<WallTie> WallTies(IReadOnlyList<Point2> standoffs, IReadOnlyList<FrameSegment> rails, float cyUp, float yA, float zA, float tilt, float topZ)
        {
            var ties = new List<WallTie>();
            float s = (float)Math.Sin(tilt), c = (float)Math.Cos(tilt), clear = TextWallFrame.StandoffRadius + TieRadius + 0.004f;
            foreach (Point2 p in standoffs)
            {
                Turned(cyUp + p.Y, zA, yA, zA, s, c, out float y, out float z);
                if (z - topZ < TieMinReach)
                    continue;
                foreach (float side in new[] { TieSide, -TieSide, 2f * TieSide, -2f * TieSide })
                {
                    float x = p.X + side;
                    if (!OnRail(rails, x, p.Y) || NearAny(standoffs, ties, x, clear))
                        continue;
                    float zEnd = z + 0.002f, yWall = y + zEnd, rise = y - yWall, length = (float)Math.Sqrt(rise * rise + zEnd * zEnd);
                    float along = TieRadius * Math.Abs(rise) / length / zEnd;
                    ties.Add(new WallTie { X = x, WallY = yWall + rise * along, WallZ = zEnd * along, Y = y, Z = zEnd });
                    break;
                }
            }

            return ties;
        }

        private static bool OnRail(IReadOnlyList<FrameSegment> rails, float x, float y)
        {
            foreach (FrameSegment rail in rails)
            {
                if (Math.Abs((rail.A.Y + rail.B.Y) * 0.5f - y) < 1e-3f && x > Math.Min(rail.A.X, rail.B.X) + 0.004f && x < Math.Max(rail.A.X, rail.B.X) - 0.004f)
                    return true;
            }

            return false;
        }

        private static bool NearAny(IReadOnlyList<Point2> standoffs, List<WallTie> ties, float x, float clear)
        {
            foreach (Point2 q in standoffs)
            {
                if (Math.Abs(q.X - x) < clear)
                    return true;
            }

            foreach (WallTie t in ties)
            {
                if (Math.Abs(t.X - x) < clear)
                    return true;
            }

            return false;
        }

        /// <summary>The tilt a spec of a text light box asks for as typed (not held to the range).</summary>
        public static float Typed(GuideSpec spec)
        {
            double v = spec.Number("tilt", 0);
            return double.IsNaN(v) || double.IsInfinity(v) ? 0f : (float)v;
        }

        /// <summary>The angle held to its range (-MaxDeg, the depression, to MaxDeg, the elevation), in radians; negative when the letters look down.</summary>
        public static float Radians(float tiltDeg) => Math.Max(-MaxDeg, Math.Min(MaxDeg, float.IsNaN(tiltDeg) ? 0f : tiltDeg)) * (float)(Math.PI / 180.0);

        /// <summary>
        /// The tilt after the tilt keys (the = key one more degree, the - key one less, Shift five): from the whole degree the angle is at, through upright into the depression angle, held to -max..max (max: the
        /// range of the board, 30 for both).
        /// </summary>
        public static float Step(float typedDeg, int presses, bool shift, float max)
        {
            float now = Math.Max(-max, Math.Min(max, float.IsNaN(typedDeg) ? 0f : typedDeg));
            return Math.Max(-max, Math.Min(max, (float)Math.Round(now) + presses * (shift ? 5 : 1)));
        }

        /// <summary>The letters look down: a tilt of at least MinDeg the other way.</summary>
        public static bool Dips(float tiltDeg) => !float.IsNaN(tiltDeg) && tiltDeg <= -MinDeg;

        /// <summary>
        /// Where the arms meet the board along its height, as shares of it from its lower edge: the share of the highest rail and, when the text has rails far enough apart to take another, of the lowest
        /// (0 when it has not). boardHeight: the height of the board's box; the frame's y is from the text's middle.
        /// </summary>
        public static void Shares(FramePlan frame, float boardHeight, out float armShare, out float lowShare)
        {
            armShare = 0f;
            lowShare = 0f;
            if (frame == null || boardHeight < 0.05f)
                return;
            List<float> levels = TextCarrier.PostLevels(frame);
            if (levels.Count == 0)
                return;
            float high = levels[levels.Count - 1], low = levels[0];
            armShare = Math.Max(0.05f, Math.Min(0.98f, (high + boardHeight * 0.5f) / boardHeight));
            if (levels.Count > 1 && high - low > 0.2f)
                lowShare = Math.Max(0.02f, Math.Min(armShare - 0.1f, (low + boardHeight * 0.5f) / boardHeight));
        }

        private static readonly HashSet<string> s_Said = new HashSet<string>();

        /// <summary>
        /// Says once for each kind of support that a tilt asked for is not built there (a hung board has its own lean forward, BoardTilt; a curved wall, a bracket, a lamp post or a roof on feet are not
        /// carried): the line for the log, or null when it was said.
        /// </summary>
        public static string Note(SupportKind kind, bool roof, float tiltDeg)
        {
            string key = kind + (roof ? ".roof" : "");
            lock (s_Said)
            {
                if (!s_Said.Add(key))
                    return null;
            }

            if (tiltDeg < 0f)
            {
                return roof
                    ? $"Text light box: the depression angle of {-tiltDeg:0}° is not built on a roof (the box would lean out over its edge): the text stands upright there."
                    : $"Text light box: the depression angle of {-tiltDeg:0}° is built on a single, a double or a triple post and on a flat wall, not on {key}: the text stands upright there.";
            }

            return $"Text light box: the elevation angle of {tiltDeg:0}° is not built on {key} (it is built on a single, a double or a triple post, and with its base frame on a roof or a flat wall): the text stands upright there.";
        }

        /// <summary>For tests: forget what was said.</summary>
        internal static void ForgetNotes()
        {
            lock (s_Said)
                s_Said.Clear();
        }
    }
}
