using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Loops (outer ones counter-clockwise, holes clockwise, y up: the material is on the left of every loop) cut to a convex polygon (0.12.74; the picture cut of DESIGN-wiring-plan.md step D). Sutherland-Hodgman on
    /// each loop alone leaves zero-width bridges along the clip line wherever a concave loop leaves and enters again, and cannot open a hole that reaches the line. So the loops are cut by one line at a time as a
    /// set: the pieces of every loop that are inside are chains from the point where the loop enters to the point where it leaves, and along the line the material runs from each leaving point to the next
    /// entering point in the line's direction, which is how the chains of all the loops are joined. Pure.
    /// </summary>
    public static class LoopClip
    {
        /// <summary>The smallest area (m2 in the board's frame) of a loop kept.</summary>
        private const double MinArea = 1e-10;

        /// <summary>
        /// The loops cut to a convex polygon given counter-clockwise (y up): what is inside it, as new loops (or the loop itself where it lies wholly inside). Null when the cut was not consistent
        /// (the pieces of the loops did not pair up along a line: a loop that is not simple, or meets the line in a way the arithmetic cannot tell); the caller keeps what it had.
        /// </summary>
        public static List<List<Point2>> ToConvex(List<List<Point2>> loops, IList<Point2> convex)
        {
            List<List<Point2>> current = loops;
            double size = 0.0;
            foreach (Point2 p in convex)
                size = Math.Max(size, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
            double eps = 1e-7 * Math.Max(1.0, size);
            for (int i = 0; i < convex.Count && current.Count > 0; i++)
            {
                current = HalfPlane(current, convex[i], convex[(i + 1) % convex.Count], eps);
                if (current == null)
                    return null;
            }

            var result = new List<List<Point2>>(current.Count);
            foreach (List<Point2> loop in current)
            {
                if (loop.Count >= 3 && Math.Abs(CutOutline.Area(loop)) >= MinArea)
                    result.Add(loop);
            }

            return result;
        }

        private sealed class Chain
        {
            public List<Point2> Points = new List<Point2>();
            public double Enter, Leave;
            public int Next = -1;
            public bool Seen;
        }

        /// <summary>The loops cut to the left of the line from a to b.</summary>
        private static List<List<Point2>> HalfPlane(List<List<Point2>> loops, Point2 a, Point2 b, double eps)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-12)
                return loops;
            double ux = dx / length, uy = dy / length;
            var result = new List<List<Point2>>(loops.Count);
            var chains = new List<Chain>();
            double[] side = new double[16];
            foreach (List<Point2> loop in loops)
            {
                int n = loop.Count;
                if (side.Length < n)
                    side = new double[Math.Max(n, side.Length * 2)];
                int inside = 0;
                for (int i = 0; i < n; i++)
                {
                    side[i] = (dx * (loop[i].Y - a.Y) - dy * (loop[i].X - a.X)) / length;
                    if (side[i] >= -eps)
                        inside++;
                }

                if (inside == n)
                {
                    result.Add(loop);
                    continue;
                }

                if (inside == 0)
                    continue;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (side[i] >= -eps || side[j] < -eps)
                        continue;
                    // The loop enters here; the chain runs on through the points inside to where it leaves.
                    var chain = new Chain();
                    chain.Points.Add(Crossing(loop[i], loop[j], side[i], side[j]));
                    chain.Enter = Along(chain.Points[0], a, ux, uy);
                    int k = j;
                    for (int guard = 0; guard < n && side[k] >= -eps; guard++)
                    {
                        chain.Points.Add(loop[k]);
                        k = (k + 1) % n;
                    }

                    int last = (k + n - 1) % n;
                    Point2 leave = Crossing(loop[last], loop[k], side[last], side[k]);
                    chain.Points.Add(leave);
                    chain.Leave = Along(leave, a, ux, uy);
                    chains.Add(chain);
                }
            }

            if (chains.Count == 0)
                return result;

            // The pieces that only touch the line are nothing.
            chains.RemoveAll(c => Length(c.Points) <= 4.0 * eps);
            var byEnter = new List<int>(chains.Count);
            for (int i = 0; i < chains.Count; i++)
                byEnter.Add(i);
            byEnter.Sort((p, q) => chains[p].Enter.CompareTo(chains[q].Enter));
            var taken = new bool[chains.Count];
            for (int i = 0; i < chains.Count; i++)
            {
                // The next entering point along the line; a chain that begins where it ends is not its own successor.
                int pick = -1;
                foreach (int candidate in byEnter)
                {
                    double gap = chains[candidate].Enter - chains[i].Leave;
                    if (gap < -4.0 * eps || (candidate == i && gap <= 4.0 * eps))
                        continue;
                    pick = candidate;
                    break;
                }

                if (pick < 0 || taken[pick])
                    return null;
                taken[pick] = true;
                chains[i].Next = pick;
            }

            foreach (Chain start in chains)
            {
                if (start.Seen)
                    continue;
                var points = new List<Point2>();
                Chain at = start;
                do
                {
                    at.Seen = true;
                    foreach (Point2 p in at.Points)
                        AddDistinct(points, p, eps);
                    at = chains[at.Next];
                }
                while (at != start && !at.Seen);
                if (at != start)
                    return null;
                if (points.Count > 1 && Same(points[0], points[points.Count - 1], eps))
                    points.RemoveAt(points.Count - 1);
                if (points.Count >= 3)
                    result.Add(points);
            }

            return result;
        }

        private static double Along(Point2 p, Point2 a, double ux, double uy) => (p.X - a.X) * ux + (p.Y - a.Y) * uy;

        private static Point2 Crossing(Point2 p, Point2 q, double sp, double sq)
        {
            double t = Math.Abs(sp - sq) > 1e-300 ? Math.Max(0.0, Math.Min(1.0, sp / (sp - sq))) : 0.0;
            return new Point2((float)(p.X + (q.X - p.X) * t), (float)(p.Y + (q.Y - p.Y) * t));
        }

        private static double Length(List<Point2> points)
        {
            double sum = 0.0;
            for (int i = 0; i + 1 < points.Count; i++)
                sum += Math.Sqrt((double)(points[i + 1].X - points[i].X) * (points[i + 1].X - points[i].X) + (double)(points[i + 1].Y - points[i].Y) * (points[i + 1].Y - points[i].Y));
            return sum;
        }

        private static bool Same(Point2 p, Point2 q, double eps) => Math.Abs(p.X - q.X) <= eps && Math.Abs(p.Y - q.Y) <= eps;

        private static void AddDistinct(List<Point2> points, Point2 p, double eps)
        {
            if (points.Count == 0 || !Same(points[points.Count - 1], p, eps))
                points.Add(p);
        }
    }
}
