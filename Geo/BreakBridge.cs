using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// 0.12.100 (the owner, 5 Nov 23:0x, "这种岔路口会折角", with a phone photo of a noise wall that steps sideways across a junction in "连续通过" mode): a wall drawn across a break of the chain
    /// is made in two stretches, one on each side, and joined here in the plane, not by the chain's distance. The break is 0.1 m long in the chain's distance (RoadChain.BreakLength) and 6 m
    /// across on the ground at his junction: the next road started 5.9 m to the side of the line the one before it was heading on and turned 19 degrees, and a wall 14 m out from the centre line of
    /// a road that turns is 4.6 m from where it would be on the next one. The log's wall made a 6.2 m chord against the way it ran (turns of 87 and -130 degrees). The stretches are cut back from the
    /// break (as little as possible, at most MaxTrim m on either side) to a pair of points that a smooth curve joins: each end's own heading kept, the chord ahead of both, the sharpest turn between
    /// two chords of the curve at most MaxTurn degrees. Where none does, the pair with the gentlest turn that still goes forward; where none goes forward, the stretches are put end to end as they were.
    /// Only one stretch is cut back when the caller says which (Cut): the wall of the road that is the narrower there is the nearer to its road, and cutting the other one would take the curve through
    /// the wider road's lanes (a road that widens across the break: the wall leaves the narrow road's edge early, it does not run on into the wide road). Pure.
    /// </summary>
    public static class BreakBridge
    {
        /// <summary>Which of the two stretches may be cut back.</summary>
        public enum Cut
        {
            Both,
            Before,
            After
        }

        /// <summary>The spacing of the points of the curve (m).</summary>
        public const float Step = 2f;

        /// <summary>The sharpest turn (degrees) allowed between two chords of the curve, and between the curve and the line it leaves and joins: a radius of 11 m at 2 m chords.</summary>
        public const float MaxTurn = 10f;

        /// <summary>The farthest the stretches are cut back from the break on either side (m): 16 m across in two arcs of 11 m radius takes 22 m, and the cubic is not an arc.</summary>
        public const float MaxTrim = 40f;

        /// <summary>How far the heading of a stretch is measured round the point where it is cut (m, each way): the polyline of a bend is a string of chords.</summary>
        private const float kWindow = 1f;

        /// <summary>The cut-back distances tried on either side (m), from none up to MaxTrim.</summary>
        private static readonly float[] kTrims = { 0f, 1f, 2f, 3f, 4f, 6f, 8f, 10f, 12f, 16f, 20f, 24f, 32f, MaxTrim };

        private struct Candidate
        {
            public float TrimBefore, TrimAfter, Turn;
            public bool Forward, Smooth;
            public Vec3 From, To;
            public List<Vec3> Inside;
        }

        /// <summary>
        /// The two stretches joined (in the order the line runs): the first up to its cut, the curve, the second from its cut. Either empty, or of one point, gives the other as it is. allowed: which of the
        /// stretches may be cut back; when none of that one's cuts goes forward, either may.
        /// </summary>
        public static List<Vec3> Join(IList<Vec3> before, IList<Vec3> after, Cut allowed = Cut.Both)
        {
            var joined = new List<Vec3>();
            if (before != null)
                joined.AddRange(before);
            if (after == null || after.Count == 0)
                return joined;
            if (before == null || before.Count < 2 || after.Count < 2)
            {
                joined.AddRange(after);
                return joined;
            }

            float[] lengthBefore = Cumulative(before), lengthAfter = Cumulative(after);
            float totalBefore = lengthBefore[lengthBefore.Length - 1], totalAfter = lengthAfter[lengthAfter.Length - 1];
            bool found = Search(before, lengthBefore, after, lengthAfter, allowed, out Candidate best);
            if (!found && allowed != Cut.Both)
                found = Search(before, lengthBefore, after, lengthAfter, Cut.Both, out best);
            if (!found)
            {
                joined.AddRange(after);
                return joined;
            }

            // The first stretch up to the cut, the curve, the second stretch from its cut.
            joined.Clear();
            float keepBefore = totalBefore - best.TrimBefore;
            for (int i = 0; i < before.Count && lengthBefore[i] < keepBefore - 1e-3f; i++)
                joined.Add(before[i]);
            joined.Add(best.From);
            joined.AddRange(best.Inside);
            joined.Add(best.To);
            for (int i = 0; i < after.Count; i++)
            {
                if (lengthAfter[i] > best.TrimAfter + 1e-3f)
                    joined.Add(after[i]);
            }

            return joined;
        }

        /// <summary>The best of the cuts the stretches allow (and are long enough for) that go forward.</summary>
        private static bool Search(IList<Vec3> before, float[] lengthBefore, IList<Vec3> after, float[] lengthAfter, Cut allowed, out Candidate best)
        {
            best = default;
            bool found = false;
            float totalBefore = lengthBefore[lengthBefore.Length - 1], totalAfter = lengthAfter[lengthAfter.Length - 1];
            foreach (float trimBefore in kTrims)
            {
                if (trimBefore > totalBefore + 1e-3f)
                    break;
                if (trimBefore > 0f && allowed == Cut.After)
                    break;
                Vec3 from = PointAt(before, lengthBefore, totalBefore - trimBefore);
                Vec3 headingFrom = HeadingAt(before, lengthBefore, totalBefore - trimBefore);
                foreach (float trimAfter in kTrims)
                {
                    if (trimAfter > totalAfter + 1e-3f)
                        break;
                    if (trimAfter > 0f && allowed == Cut.Before)
                        break;
                    Vec3 to = PointAt(after, lengthAfter, trimAfter);
                    Candidate c = Evaluate(from, headingFrom, to, HeadingAt(after, lengthAfter, trimAfter));
                    c.TrimBefore = trimBefore;
                    c.TrimAfter = trimAfter;
                    if (c.Forward && (!found || Better(c, best)))
                    {
                        best = c;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>Whether a is the pair to take over b: a smooth one before one that is not, then the less cut back, then the gentler turn (and, with none smooth, the gentler turn first).</summary>
        private static bool Better(Candidate a, Candidate b)
        {
            if (a.Smooth != b.Smooth)
                return a.Smooth;
            float cutA = a.TrimBefore + a.TrimAfter, cutB = b.TrimBefore + b.TrimAfter;
            if (a.Smooth)
                return cutA < cutB - 1e-3f || (Math.Abs(cutA - cutB) <= 1e-3f && a.Turn < b.Turn - 1e-3f);
            return a.Turn < b.Turn - 1e-3f || (Math.Abs(a.Turn - b.Turn) <= 1e-3f && cutA < cutB - 1e-3f);
        }

        /// <summary>
        /// The curve from one point to another, each with its heading (a cubic Hermite, the end vectors as long as the chord), its points every Step m, and how it measures up: whether the chord goes
        /// ahead of both headings (the cosine of 60 degrees at least), the sharpest turn along it from the heading it leaves to the heading it joins, whether that turn is within MaxTurn.
        /// </summary>
        private static Candidate Evaluate(Vec3 from, Vec3 headingFrom, Vec3 to, Vec3 headingTo)
        {
            var c = new Candidate { From = from, To = to, Inside = new List<Vec3>() };
            Vec3 chord = to - from;
            float length = chord.LengthXZ;
            Vec3 ahead = chord.HeadingXZ();
            // Two points that are one (a cut that meets the other stretch) have no chord: the headings are what must agree.
            c.Forward = length < 1e-3f ? Vec3.Dot(headingFrom, headingTo) >= 0.5f : Vec3.Dot(ahead, headingFrom) >= 0.5f && Vec3.Dot(ahead, headingTo) >= 0.5f;
            if (!c.Forward || length < 1e-3f)
            {
                c.Turn = c.Forward ? Angle(headingFrom, headingTo) : 180f;
                c.Smooth = c.Forward && c.Turn <= MaxTurn;
                return c;
            }

            int n = Math.Max(1, (int)Math.Ceiling(length / Step));
            var points = new List<Vec3>(n + 1) { from };
            Vec3 m0 = headingFrom * length, m1 = headingTo * length;
            for (int i = 1; i < n; i++)
            {
                float t = (float)i / n, t2 = t * t, t3 = t2 * t;
                float h00 = 2f * t3 - 3f * t2 + 1f, h10 = t3 - 2f * t2 + t, h01 = -2f * t3 + 3f * t2, h11 = t3 - t2;
                Vec3 p = from * h00 + m0 * h10 + to * h01 + m1 * h11;
                points.Add(new Vec3(p.X, from.Y + (to.Y - from.Y) * t, p.Z));
            }

            points.Add(to);
            // A bridge that is straight (within the tolerance of a barrier's line) keeps no points between its ends.
            bool straight = true;
            for (int i = 1; i < n && straight; i++)
                straight = DistanceFromChord(points[i], from, ahead) <= BarrierPath.Tolerance;
            if (straight)
            {
                points.RemoveRange(1, points.Count - 2);
                n = 1;
            }

            float turn = Angle(headingFrom, (points[1] - points[0]).HeadingXZ());
            for (int i = 2; i < points.Count; i++)
                turn = Math.Max(turn, Angle((points[i - 1] - points[i - 2]).HeadingXZ(), (points[i] - points[i - 1]).HeadingXZ()));
            turn = Math.Max(turn, Angle((points[points.Count - 1] - points[points.Count - 2]).HeadingXZ(), headingTo));
            for (int i = 1; i < points.Count - 1; i++)
                c.Inside.Add(points[i]);
            c.Turn = turn;
            c.Smooth = turn <= MaxTurn;
            return c;
        }

        private static float DistanceFromChord(Vec3 p, Vec3 origin, Vec3 heading) => Math.Abs((p.X - origin.X) * heading.Z - (p.Z - origin.Z) * heading.X);

        /// <summary>The angle between two ground headings (degrees).</summary>
        private static float Angle(Vec3 a, Vec3 b) => (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, a.X * b.X + a.Z * b.Z))) * 180.0 / Math.PI);

        /// <summary>The distance along the line, in the ground plane, of each of its points.</summary>
        private static float[] Cumulative(IList<Vec3> line)
        {
            var length = new float[line.Count];
            for (int i = 1; i < line.Count; i++)
                length[i] = length[i - 1] + Vec3.DistanceXZ(line[i - 1], line[i]);
            return length;
        }

        /// <summary>The point of the line at a distance along it (clamped to the line).</summary>
        private static Vec3 PointAt(IList<Vec3> line, float[] length, float distance)
        {
            distance = Math.Max(0f, Math.Min(length[length.Length - 1], distance));
            int i = 1;
            while (i < line.Count - 1 && length[i] < distance)
                i++;
            float span = length[i] - length[i - 1];
            return span < 1e-6f ? line[i] : Vec3.Lerp(line[i - 1], line[i], Math.Max(0f, Math.Min(1f, (distance - length[i - 1]) / span)));
        }

        /// <summary>The way the line heads at a distance along it: from kWindow before to kWindow after (clamped to the line's ends), in the ground plane.</summary>
        private static Vec3 HeadingAt(IList<Vec3> line, float[] length, float distance)
        {
            Vec3 a = PointAt(line, length, distance - kWindow), b = PointAt(line, length, distance + kWindow);
            return (b - a).HeadingXZ();
        }
    }
}
