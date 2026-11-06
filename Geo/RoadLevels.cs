using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Which of several roads an item or a point of a line belongs to when some of them lie over one another (0.12.86; the owner: "在高架路上使用MoveIt好像隔音墙之类的跟随计算有问题", "位置错了。大部分情况下留在原地"). The
    /// follow measured the distance to a road on the ground plane only, so a wall on a viaduct belonged to a road on the ground that is nearer in the plane (under the deck, or where the viaduct crosses it) and
    /// stayed behind when only the viaduct was moved. A road is at the point's level when the point is within LevelGap of its centre line in height; a road that is not, while another one is, and that lies
    /// StackGap or more over or under it, is left out. Where no road is at the point's level, or all are, or the roads are not stacked (a ramp beside a road), it is the nearer in the plane, as it was. Pure.
    /// </summary>
    public static class RoadLevels
    {
        /// <summary>How far in height (m) a point may be from a road's centre line and still be at its level: a wall's foot on a deck is on it, the foot of a post beside a road on the ground is within a footway's rise.</summary>
        public const float LevelGap = 3f;

        /// <summary>How far apart in height (m) the centre lines of two roads must be to lie over one another: a deck over a road is 4.5 m clear and as thick again; a ramp beside a road is less.</summary>
        public const float StackGap = 4f;

        /// <summary>
        /// The index of the road the point belongs to, of count candidates: distance[i] is its distance to road i in the ground plane and roadY[i] the height of road i's centre line at the nearest point; -1 for none.
        /// The first of equally near roads wins, as before.
        /// </summary>
        public static int Pick(IList<float> distance, IList<float> roadY, int count, float pointY)
        {
            int at = 0;
            for (int i = 0; i < count; i++)
            {
                if (System.Math.Abs(pointY - roadY[i]) <= LevelGap)
                    at++;
            }

            bool levels = at > 0 && at < count;
            int best = -1;
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (levels && Displaced(roadY, count, pointY, i))
                    continue;
                if (distance[i] < nearest)
                {
                    nearest = distance[i];
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Whether another road lies over or under the point's level road: road i is not at the point's level, and a road that is lies more than StackGap from it.</summary>
        public static bool Displaced(IList<float> roadY, int count, float pointY, int i)
        {
            if (System.Math.Abs(pointY - roadY[i]) <= LevelGap)
                return false;
            for (int j = 0; j < count; j++)
            {
                if (System.Math.Abs(pointY - roadY[j]) <= LevelGap && System.Math.Abs(roadY[i] - roadY[j]) > StackGap)
                    return true;
            }

            return false;
        }
    }
}
