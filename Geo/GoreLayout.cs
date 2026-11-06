using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What GoreLayout worked out at a fork: the gore's two sides from the nose to the far end, and how it was found.</summary>
    public sealed class GoreSolution
    {
        /// <summary>The through carriageway's edge and the ramp's, both from the nose outward (away from the junction), at most MaxSidePoints each, in the frame the sides were given in.</summary>
        public List<Vec3> Main = new List<Vec3>(), Ramp = new List<Vec3>();

        public Vec3 Nose;

        /// <summary>The length of the gore along the main side (m), and how far apart the sides are at its far end (m).</summary>
        public float Length, FarWidth;

        /// <summary>The sides did not meet: the nose is where they are nearest, and it is that wide (m).</summary>
        public bool Blunt;

        public float NoseGap;

        /// <summary>How far along the main side the nose is from where that side was given to start (m; negative: before it, on the line its first piece makes).</summary>
        public float NoseAt;
    }

    /// <summary>
    /// The gore of an exit or an entry (线65, Fig. 94) where a carriageway and a ramp part: from the edges of the two carriageways on the gore's side, found where they meet (the nose) and where they are as far
    /// apart as the gore is wide (its far end, the end of the paint; or its greatest length), and the lead line before the nose (6.12.2: the thick dashed line, 3-3, between the through lanes and the lane that
    /// leaves, and the thick solid line at least 2 m long up to the nose), which lies on the approach where the nose's own line would be. Both edges are given away from the junction; an edge that starts
    /// a little beyond the point where the carriageways part (the game's road pieces start at the edge of the junction) is carried back along its first piece to find that point. Pure.
    /// </summary>
    public static class GoreLayout
    {
        public const int MaxSidePoints = 12;

        /// <summary>How close the sides are (m) to be taken as meeting at the nose.</summary>
        public const float Touch = 0.05f;

        /// <summary>The narrowest a gore may be at its far end to be one (m): two edges that do not part are not a fork.</summary>
        public const float MinFarWidth = 0.6f;

        /// <summary>The greatest angle (degrees) between the two carriageways where they part: more is a junction, not a gore.</summary>
        public const float MaxAngle = 45f;

        private static float[] Lengths(IList<Vec3> line)
        {
            var at = new float[line.Count];
            for (int i = 1; i < line.Count; i++)
                at[i] = at[i - 1] + Vec3.DistanceXZ(line[i - 1], line[i]);
            return at;
        }

        /// <summary>The nearest point of a line of points to a place, and the distance along the line to it; clamped is whether that is an end of the line.</summary>
        public static Vec3 Nearest(IList<Vec3> line, Vec3 p, out float along, out bool clamped)
        {
            Vec3 best = line[0];
            float bestD = float.MaxValue, run = 0f;
            along = 0f;
            clamped = true;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 a = line[i], b = line[i + 1];
                float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz, len = (float)Math.Sqrt(len2);
                float t = len2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2));
                Vec3 q = Vec3.Lerp(a, b, t);
                float d = Vec3.DistanceXZ(p, q);
                if (d < bestD)
                {
                    bestD = d;
                    best = q;
                    along = run + len * t;
                    clamped = (i == 0 && t <= 1e-6f) || (i == line.Count - 2 && t >= 1f - 1e-6f);
                }

                run += len;
            }

            return best;
        }

        /// <summary>A line of points carried back along its first piece by a distance (a point put before its first).</summary>
        public static List<Vec3> ExtendBack(IList<Vec3> line, float distance)
        {
            var result = new List<Vec3>(line.Count + 1);
            if (line.Count >= 2 && distance > 0f)
            {
                Vec3 h = (line[1] - line[0]).HeadingXZ();
                Vec3 first = line[0] - h * distance;
                // The height of the line goes on as its first piece rises.
                float run = Vec3.DistanceXZ(line[0], line[1]);
                float rise = run > 1e-4f ? (line[1].Y - line[0].Y) / run : 0f;
                result.Add(new Vec3(first.X, line[0].Y - rise * distance, first.Z));
            }

            result.AddRange(line);
            return result;
        }

        /// <summary>
        /// Finds the gore between two edges given away from the junction: mainAway (the through carriageway's edge on the gore's side) and rampAway (the ramp's edge on the gore's side), the ramp to the right of
        /// the main (rampSide +1) or its left (-1) looking away from the junction. The far end is where the sides are farWidth apart or maxLength from the nose, whichever is first. Null when the edges do not part
        /// within the length (they are parallel, or the ramp is on the other side).
        /// </summary>
        public static GoreSolution Solve(IList<Vec3> mainAway, IList<Vec3> rampAway, int rampSide, float farWidth, float maxLength, float extend = 40f)
        {
            if (mainAway == null || rampAway == null || mainAway.Count < 2 || rampAway.Count < 2 || !(farWidth > 0f) || !(maxLength > 0f))
                return null;
            List<Vec3> main = ExtendBack(mainAway, extend), ramp = ExtendBack(rampAway, extend);
            float[] at = Lengths(main);
            float total = at[main.Count - 1];
            const float step = 0.25f;
            int count = (int)Math.Ceiling(total / step) + 1;
            if (count < 4 || count > 40000)
                return null;
            var points = new Vec3[count];
            var gaps = new float[count];
            var valid = new bool[count];
            for (int i = 0; i < count; i++)
            {
                float s = Math.Min(total, i * step);
                MarkingAreas.PointAt(main, at, s, out Vec3 p, out Vec3 h);
                points[i] = p;
                Vec3 q = Nearest(ramp, p, out _, out bool clamped);
                gaps[i] = Vec3.Dot(q - p, h.RightXZ()) * rampSide;
                valid[i] = !clamped;
            }

            // The far end: the first place past the meeting where the sides are farWidth apart.
            int far = -1;
            for (int i = 0; i < count; i++)
            {
                if (valid[i] && gaps[i] >= farWidth)
                {
                    far = i;
                    break;
                }
            }

            if (far < 0)
            {
                // They never get that far apart: the widest place is the far end, if it is wide enough to be a gore.
                float widest = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    if (valid[i] && gaps[i] > widest)
                    {
                        widest = gaps[i];
                        far = i;
                    }
                }

                if (far < 0 || widest < MinFarWidth)
                    return null;
            }

            // The nose: the last place before the far end where the sides cross (the gap goes from 0 or less to more), at the crossing itself between the two samples; if they never cross, where they are
            // nearest (touching when that is within Touch, else a blunt nose).
            int nose = -1;
            float noseS = 0f;
            for (int i = far - 1; i >= 0; i--)
            {
                if (valid[i] && gaps[i] <= 0f)
                {
                    nose = i;
                    float g0 = gaps[i], g1 = gaps[Math.Min(count - 1, i + 1)];
                    float f = g1 - g0 > 1e-6f ? Math.Max(0f, Math.Min(1f, -g0 / (g1 - g0))) : 0f;
                    noseS = (i + f) * step;
                    break;
                }
            }

            bool blunt = false;
            if (nose < 0)
            {
                float nearest = float.MaxValue;
                for (int i = 0; i < far; i++)
                {
                    if (valid[i] && gaps[i] < nearest)
                    {
                        nearest = gaps[i];
                        nose = i;
                    }
                }

                if (nose < 0)
                    return null;
                noseS = nose * step;
                blunt = nearest > Touch;
            }

            // The gore no longer than maxLength.
            int last = far;
            if ((last - nose) * step > maxLength)
                last = nose + (int)Math.Floor(maxLength / step);
            if (last - nose < 4 || gaps[last] < MinFarWidth)
                return null;

            // The angle at which they part: the directions of the two sides at the nose.
            Vec3 mainHeading = (points[Math.Min(count - 1, nose + 4)] - points[nose]).HeadingXZ();
            Vec3 rampAtNose = Nearest(ramp, points[nose], out float rampAlong, out _);
            float[] rampAt = Lengths(ramp);
            MarkingAreas.PointAt(ramp, rampAt, rampAlong + 1f, out Vec3 rampAhead, out _);
            Vec3 rampHeading = (rampAhead - rampAtNose).HeadingXZ();
            double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vec3.Dot(mainHeading, rampHeading)))) * 180.0 / Math.PI;
            if (angle > MaxAngle)
                return null;

            var mainSide = new List<Vec3>();
            var rampSide2 = new List<Vec3>();
            MarkingAreas.PointAt(main, at, noseS, out Vec3 noseMain, out _);
            mainSide.Add(noseMain);
            rampSide2.Add(Nearest(ramp, noseMain, out _, out _));
            for (int i = nose + 1; i <= last; i++)
            {
                mainSide.Add(points[i]);
                rampSide2.Add(Nearest(ramp, points[i], out _, out _));
            }

            var solution = new GoreSolution
            {
                Blunt = blunt,
                NoseGap = Math.Max(0f, gaps[nose]),
                Length = last * step - noseS,
                FarWidth = gaps[last],
                NoseAt = noseS - (main.Count > mainAway.Count ? extend : 0f)
            };
            if (!blunt)
            {
                // Where they meet the two sides start at one point.
                Vec3 meet = (mainSide[0] + rampSide2[0]) * 0.5f;
                mainSide[0] = meet;
                rampSide2[0] = meet;
            }

            solution.Main = MarkingAreas.Resample(mainSide, Math.Min(MaxSidePoints, Math.Max(2, mainSide.Count)));
            solution.Ramp = MarkingAreas.Resample(rampSide2, Math.Min(MaxSidePoints, Math.Max(2, rampSide2.Count)));
            solution.Nose = solution.Main[0];
            return solution;
        }

        /// <summary>
        /// The lead line of an exit (6.12.2): on the approach (a line of points running the way the traffic runs, toward the junction: its centre line) at the place across it where the nose is, from length m before
        /// the nose up to the nose, ending at the nose; the approach's line is used as far as it goes and then straight on to the nose. For an entry give the road that leaves the junction reversed, and reverse the
        /// result (LeadAfter).
        /// </summary>
        public static List<Vec3> LeadBefore(IList<Vec3> approach, Vec3 nose, float length)
        {
            var lead = new List<Vec3>();
            if (approach == null || approach.Count < 2 || !(length > 0.5f))
                return lead;
            int n = approach.Count;
            Vec3 end = approach[n - 1], heading = (approach[n - 1] - approach[n - 2]).HeadingXZ();
            float lateral = Vec3.Dot(nose - end, heading.RightXZ());
            List<Vec3> divider = MarkingFills.Offset(approach, lateral);
            // The divider's height is the road's: the approach's own, at the nose's height difference from it at the end.
            float lift = nose.Y - end.Y;
            // The whole line: the divider up to the nose (when the nose is past the approach's end, on to it straight through the junction; when it is before, as far as the nose's place on it), then the nose.
            var full = new List<Vec3>();
            float beyond = Vec3.Dot(nose - divider[n - 1], heading);
            if (beyond >= 0f)
            {
                foreach (Vec3 p in divider)
                    full.Add(new Vec3(p.X, p.Y + lift, p.Z));
            }
            else
            {
                float[] dividerAt = Lengths(divider);
                Nearest(divider, nose, out float noseAlong, out _);
                for (int i = 0; i < n && dividerAt[i] < noseAlong - 0.05f; i++)
                    full.Add(new Vec3(divider[i].X, divider[i].Y + lift, divider[i].Z));
            }

            if (full.Count == 0 || Vec3.DistanceXZ(full[full.Count - 1], nose) > 0.05f)
                full.Add(nose);
            else
                full[full.Count - 1] = nose;
            if (full.Count < 2)
                return lead;

            // Its last length metres, ending at the nose.
            float[] at = Lengths(full);
            float total = at[full.Count - 1], start = Math.Max(0f, total - length);
            MarkingAreas.PointAt(full, at, start, out Vec3 first, out _);
            lead.Add(first);
            for (int i = 0; i < full.Count; i++)
            {
                if (at[i] > start + 0.05f)
                    lead.Add(full[i]);
            }

            return lead.Count >= 2 ? lead : new List<Vec3>();
        }

        /// <summary>The lead line of an entry: from the nose along the road that leaves the junction (given the way the traffic runs, from the junction on), length m long.</summary>
        public static List<Vec3> LeadAfter(IList<Vec3> departure, Vec3 nose, float length)
        {
            if (departure == null || departure.Count < 2)
                return new List<Vec3>();
            var reversed = new List<Vec3>(departure);
            reversed.Reverse();
            List<Vec3> lead = LeadBefore(reversed, nose, length);
            lead.Reverse();
            return lead;
        }
    }
}
