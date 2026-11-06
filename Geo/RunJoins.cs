using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Where a barrier's line is started or ended it is drawn to the end of a barrier of its kind that is there already (0.12.60; the owner: "隔音墙吸附还是不够好", on the starts, ends and seams: two lines of
    /// the same wall put one after the other on one road overlapped over 9 m, the second starting where its click was and not where the first ended). The ends are distances along the road's chain.
    /// A barrier's end counts when it lies on the line the new one runs along (across the road within LateralReach of it, and level with it): the end of a wall on another road or deck is not its seam. Pure.
    /// </summary>
    public static class RunJoins
    {
        /// <summary>How near (m along the road) a start or an end is to an end that is there for it to be drawn to it.</summary>
        public const float Reach = 4f;

        /// <summary>How far (m) across the road an end may be from the line to be on it.</summary>
        public const float LateralReach = 1f;

        /// <summary>How far (m) above or below the road an end may be to be on it (a deck over it or under it is another road).</summary>
        public const float LevelReach = 3f;

        /// <summary>One barrier item of a run: where it starts, the way it first heads, and its path.</summary>
        public struct Item
        {
            public string Path;
            public Vec3 Position;
            public float Yaw;
        }

        /// <summary>The two ends of an item's line in the world; false for an item with no path.</summary>
        public static bool Ends(Item item, out Vec3 first, out Vec3 last)
        {
            first = item.Position;
            last = item.Position;
            List<Vec3> local = RunPath.Decode(item.Path);
            if (local.Count < 2)
                return false;
            List<Vec3> world = MarkingPolygon.ToWorld(local, item.Position, item.Yaw);
            first = world[0];
            last = world[world.Count - 1];
            return true;
        }

        /// <summary>The distance along the chain of a place, when it is on the line at that lateral (m right of the chain's direction); NaN when it is not.</summary>
        public static float Along(RoadChain chain, Vec3 place, float lateral)
        {
            float s = chain.ProjectLevel(place, out float across);
            if (float.IsNaN(s) || Math.Abs(across - lateral) > LateralReach)
                return float.NaN;
            return Math.Abs(chain.PointAt(s, across).Y - place.Y) > LevelReach ? float.NaN : s;
        }

        /// <summary>The distances along the chain of the ends of the items that are on the line at that lateral, both ends of each.</summary>
        public static List<float> Of(RoadChain chain, IEnumerable<Item> items, float lateral)
        {
            var ends = new List<float>();
            if (chain == null)
                return ends;
            foreach (Item item in items)
            {
                if (!Ends(item, out Vec3 first, out Vec3 last))
                    continue;
                foreach (Vec3 place in new[] { first, last })
                {
                    float s = Along(chain, place, lateral);
                    if (!float.IsNaN(s))
                        ends.Add(s);
                }
            }

            return ends;
        }

        /// <summary>The end nearest a distance, when one is within the reach of it; NaN when there is none.</summary>
        public static float Snap(IList<float> ends, float s, float within = Reach)
        {
            float best = float.NaN, distance = within;
            if (ends == null)
                return best;
            foreach (float end in ends)
            {
                float d = Math.Abs(end - s);
                if (d < distance)
                {
                    distance = d;
                    best = end;
                }
            }

            return best;
        }
    }
}
