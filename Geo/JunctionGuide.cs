using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One junction guide line: its points (world) and whether it is yellow (it goes on from the centre line) or white.</summary>
    public sealed class GuideLine
    {
        public List<Vec3> Points;
        public bool Yellow;
    }

    /// <summary>
    /// The junction guide lines (路口导向线, 线8; GB 5768.3-2025 5.6) of the left turns of an approach (0.12.2): 5.6.3 has them where the junction is large, has two left-turn lanes or more, is irregular or
    /// busy; 5.6.4 puts them on the left of the lane they guide, and between the lanes when they guide two or more; 5.6.2 makes them dashed 2 m on 2 m off, 15 wide, the colour of the line they go on from (the
    /// left line of the leftmost lane goes on from the centre line: yellow; one between two lanes from a lane line: white). The paths are the game's turns through the junction (a lane's end to the exit lane's
    /// start); the lines start where the approach's crosswalk (or waiting area) ends and stop EndClear before the exit. Pure.
    /// </summary>
    public static class JunctionGuide
    {
        /// <summary>A left turn this long (m) or longer makes a junction large (5.6.3): this mod's figure.</summary>
        public const float LargeTurn = 40f;

        /// <summary>How far before the end of a turn (the exit, its crosswalk) the lines stop (m): this mod's figure.</summary>
        public const float EndClear = 5f;

        /// <summary>Whether the guide lines are drawn for the left turns of an approach by the setting.</summary>
        public static bool Wanted(JunctionGuides mode, IList<List<Vec3>> turns)
        {
            if (mode == JunctionGuides.None || turns == null || turns.Count == 0)
                return false;
            if (mode == JunctionGuides.All || turns.Count >= 2)
                return true;
            foreach (List<Vec3> turn in turns)
            {
                if (WaitingArea.Length(turn) >= LargeTurn)
                    return true;
            }

            return false;
        }

        /// <summary>A path resampled at n + 1 points evenly along it.</summary>
        private static List<Vec3> Even(IList<Vec3> path, int n)
        {
            float length = WaitingArea.Length(path);
            var points = new List<Vec3>(n + 1);
            for (int i = 0; i <= n; i++)
                points.Add(WaitingArea.At(path, length * i / n, out _));
            return points;
        }

        /// <summary>
        /// The lines of the left turns (from the driver's left; each with its lane's width): on the left of the first (yellow when it is the approach's leftmost lane), and between each two. Each cut to start at
        /// startAt metres along its turn and end EndClear before its end; none shorter than 2 m.
        /// </summary>
        public static List<GuideLine> Lines(IList<List<Vec3>> turns, IList<float> widths, bool leftmostYellow, float startAt)
        {
            var lines = new List<GuideLine>();
            if (turns == null || turns.Count == 0)
                return lines;
            const int n = 48;
            var even = new List<List<Vec3>>();
            foreach (List<Vec3> turn in turns)
                even.Add(turn != null && turn.Count >= 2 ? Even(turn, n) : null);
            if (even[0] != null)
            {
                float w = widths != null && widths.Count > 0 ? widths[0] : MarkingSnap.DefaultLane;
                lines.Add(new GuideLine { Points = MarkingFills.Offset(even[0], -w * 0.5f), Yellow = leftmostYellow });
            }

            for (int k = 1; k < even.Count; k++)
            {
                if (even[k - 1] == null || even[k] == null)
                    continue;
                var between = new List<Vec3>(n + 1);
                for (int i = 0; i <= n; i++)
                    between.Add(Vec3.Lerp(even[k - 1][i], even[k][i], 0.5f));
                lines.Add(new GuideLine { Points = between, Yellow = false });
            }

            var cut = new List<GuideLine>();
            foreach (GuideLine line in lines)
            {
                float length = WaitingArea.Length(line.Points);
                float from = Math.Max(0f, startAt), to = length - EndClear;
                if (to - from < 2f)
                    continue;
                cut.Add(new GuideLine { Points = WaitingArea.Cut(line.Points, from, to, 2f), Yellow = line.Yellow });
            }

            return cut;
        }
    }
}
