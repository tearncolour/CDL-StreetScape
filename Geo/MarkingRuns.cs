using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One item of a run: where it starts, the way it first heads, and its path (RunPath).</summary>
    public struct RunItem
    {
        public Vec3 Position;
        public float Yaw;
        public string Path;

        /// <summary>How far along the whole line (m, on the ground) this item starts: a dashed line goes on in the pattern it was in where the item before it ended.</summary>
        public float Start;

        /// <summary>
        /// For a barrier (0.12.19): where this item starts along its own part of the line (m) and how long that part is: each part between two junctions or crosswalks is a barrier of its own, with its
        /// own ends (a round end, a flare, a cap). 0 and 0 for a marking (its pattern goes on over the gaps).
        /// </summary>
        public float PartStart, PartLength;
    }

    /// <summary>
    /// A marking that runs along a road, from where the player clicked first to where they clicked last: the line along the road's curve at a lateral offset from its centre, and the items it is cut into
    /// (RunPath.MaxPieces pieces and MaxItemLength metres at most each, so that the settings of an item stay small). Pure.
    /// </summary>
    public static class MarkingRuns
    {
        /// <summary>The longest a single item runs (m); a longer run is several items that join end to end.</summary>
        public const float MaxItemLength = 100f;

        /// <summary>The shortest a run is (m): shorter is a click that did not move.</summary>
        public const float MinLength = 1f;

        /// <summary>The longest a run can be in one go (m): 3 km is about 50 items.</summary>
        public const float MaxLength = 3000f;

        /// <summary>
        /// How far apart the points of a run's line are taken along the curve (m). A curved lane line follows its curve closely when the pieces are short (a chord of 2 m on a bend of 10 m radius is 5 cm off),
        /// and the points are taken at whole steps from where the run starts, so that moving the end of a run does not move the points before it (the items before the last are the same, and are kept).
        /// </summary>
        public const float Step = 2f;

        /// <summary>
        /// The most characters the path of an item may have when the other settings are short (the settings take 480 bytes in all, GuideSpec.MaxBytes; those of a marking line are about 40). The settings
        /// of a wall are 120 bytes and more: MarkingTemplates.PathBudget gives the room an item has, and the items below take it (0.12.99: with 380 a path on a bend and a slope was cut by GuideSpec.Build
        /// in the middle of its last chord, and the wall was 2 m short at the end of each such item).
        /// </summary>
        public const int MaxPathChars = 380;

        /// <summary>The least room a path is given (five chords): a spec with other settings so long that less is left is not made shorter than this.</summary>
        public const int MinPathChars = 60;

        /// <summary>The distance along the curve of the point nearest to the place (m).</summary>
        public static float ProjectS(ArcTable arc, Vec3 cursor) => arc.SAt(arc.Curve.ClosestT(cursor));

        /// <summary>
        /// The line of a run along an edge: from distance s0 to s1 (m along the curve; the line is drawn from s0 to s1, backwards when s1 is the smaller), at lateral metres right of the centre line (looking
        /// along the curve, whatever way the run goes), raised by surface (the road's surface above its centre line). Empty when shorter than MinLength or longer than MaxLength.
        /// </summary>
        public static List<Vec3> Line(ArcTable arc, float s0, float s1, float lateral, float surface)
        {
            var result = new List<Vec3>();
            if (arc == null || float.IsNaN(s0) || float.IsNaN(s1) || float.IsNaN(lateral) || float.IsNaN(surface) || float.IsInfinity(s0) || float.IsInfinity(s1))
                return result;
            s0 = Math.Max(0f, Math.Min(arc.Length, s0));
            s1 = Math.Max(0f, Math.Min(arc.Length, s1));
            float length = Math.Abs(s1 - s0);
            if (length < MinLength || length > MaxLength)
                return result;
            float sign = s1 >= s0 ? 1f : -1f;
            int whole = (int)Math.Floor(length / Step);
            for (int i = 0; i <= whole; i++)
            {
                float d = i * Step;
                // The last whole step may be nearly the end: it is left out, and the end is added in its place.
                if (length - d < MinStep && i > 0)
                    break;
                result.Add(PointAt(arc, s0 + sign * d, lateral, surface));
            }

            result.Add(PointAt(arc, s1, lateral, surface));
            return result;
        }

        /// <summary>A point nearer than this (m) to the end of the line is not kept as a point of its own.</summary>
        private const float MinStep = 0.5f;

        private static Vec3 PointAt(ArcTable arc, float s, float lateral, float surface)
        {
            float t = arc.TAt(s);
            Vec3 heading = arc.Curve.Tangent(t).HeadingXZ();
            Vec3 p = arc.Curve.Point(t) + heading.RightXZ() * lateral;
            return new Vec3(p.X, p.Y + surface, p.Z);
        }

        /// <summary>The items a line is cut into, each with its start, first heading and path. Nothing for a line that is too short or not made of numbers.</summary>
        public static List<RunItem> Items(IList<Vec3> line, int maxPathChars = MaxPathChars)
        {
            var items = new List<RunItem>();
            float start = 0f;
            foreach (List<Vec3> run in RunPath.Split(line, MaxItemLength))
            {
                Fit(run, items, 0, start, maxPathChars);
                start += GroundLength(run, 0, run.Count);
            }

            return items;
        }

        /// <summary>
        /// The items of a line in parts (0.12.47, RoadChain.LineParts): each part's items apart, so that none crosses from one part into the next; Start counts from the first part's start, so that a
        /// dashed line's pattern goes on over them. total: the whole line's length on the ground.
        /// </summary>
        public static List<RunItem> Items(IList<List<Vec3>> parts, out float total, int maxPathChars = MaxPathChars)
        {
            var items = new List<RunItem>();
            total = 0f;
            foreach (List<Vec3> part in parts)
            {
                if (part.Count < 2)
                    continue;
                foreach (RunItem item in Items(part, maxPathChars))
                {
                    RunItem moved = item;
                    moved.Start += total;
                    items.Add(moved);
                }

                total += GroundLength(part, 0, part.Count);
            }

            return items;
        }

        private static float GroundLength(List<Vec3> points, int from, int to)
        {
            float length = 0f;
            for (int i = from + 1; i < to; i++)
                length += Vec3.DistanceXZ(points[i - 1], points[i]);
            return length;
        }

        /// <summary>An item for a run of points; one whose path is too long for the settings is cut in two at the middle point (and so on).</summary>
        private static void Fit(List<Vec3> run, List<RunItem> items, int depth, float start, int maxPathChars)
        {
            string path = RunPath.Encode(run, out float yaw);
            if (path.Length == 0)
                return;
            if (path.Length > maxPathChars && run.Count > 2 && depth < 6)
            {
                int middle = run.Count / 2;
                Fit(run.GetRange(0, middle + 1), items, depth + 1, start, maxPathChars);
                Fit(run.GetRange(middle, run.Count - middle), items, depth + 1, start + GroundLength(run, 0, middle + 1), maxPathChars);
                return;
            }

            items.Add(new RunItem { Position = run[0], Yaw = yaw, Path = path, Start = start });
        }
    }
}
