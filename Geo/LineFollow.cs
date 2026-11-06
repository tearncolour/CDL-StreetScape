using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>How the follow of a line item ended (LineFollow.Apply).</summary>
    public enum LineOutcome
    {
        /// <summary>Nothing to do: a road has no shape now, or the mapped line is not a path.</summary>
        Failed,

        /// <summary>The line is where it was written, to within LineFollow.Metres: nothing is written.</summary>
        Unchanged,

        /// <summary>The line has the shape of the text it has, moved or turned: only the position and the yaw are written, the text is kept (it is not made again).</summary>
        PoseOnly,

        /// <summary>The line's shape changed beyond the threshold: the new text, position and yaw are written.</summary>
        Rewritten
    }

    /// <summary>What the follow of a line made of it: the pieces its text has, and what it asked for when the text could not hold it.</summary>
    public struct LineStats
    {
        /// <summary>The chords of the mapped line.</summary>
        public int Pieces;

        /// <summary>The chords that were wanted so that none is longer than the limit given: more than RunPath.MaxPieces means the line has to be cut into more items, which the follow does not do.</summary>
        public int Wanted;

        /// <summary>The longest chord of the mapped line (m).</summary>
        public float LongestChord;

        /// <summary>How many of the mapped points stepped back along the line and were put on the last point before them (RunPath.StepForward, 0.12.97).</summary>
        public int SteppedBack;
    }

    /// <summary>
    /// A line item's relation to its roads, measured once when it is linked (0.12.74 follow work): for each point of its path the road it runs along (the one whose old curve is nearest) and the point's share of that
    /// road's length, its distance along, its lateral and its height; and the text it is written as now, as points of the world, to be compared with what the roads make of it. Owned by one thread at a time.
    /// </summary>
    public sealed class LineRelation
    {
        internal int Count;
        internal int[] Road = new int[RunPath.MaxPieces + 1];
        internal float[] Fraction = new float[RunPath.MaxPieces + 1], Length = new float[RunPath.MaxPieces + 1], Along = new float[RunPath.MaxPieces + 1], Lateral = new float[RunPath.MaxPieces + 1], Height = new float[RunPath.MaxPieces + 1];
        internal Vec3[] Written = new Vec3[RunPath.MaxPieces + 1];
        internal int WrittenCount;

        /// <summary>How many of the points took the road at their own level, though another road lying over or under it was nearer in the plane (RoadLevels).</summary>
        internal int Stacked;

        /// <summary>The longest chord of the line when it was measured (m).</summary>
        public float LongestChord { get; internal set; }

        /// <summary>The text, position and yaw the item has now.</summary>
        public string Path { get; internal set; }

        public Vec3 Position { get; internal set; }

        public float Yaw { get; internal set; }

        public int Points => Count;

        /// <summary>A relation of its own with the same numbers: Apply changes the relation it is given (it keeps what the caller is to write), so a caller that writes nothing applies a copy.</summary>
        internal LineRelation Copy()
        {
            var copy = new LineRelation { Count = Count, WrittenCount = WrittenCount, Stacked = Stacked, LongestChord = LongestChord, Path = Path, Position = Position, Yaw = Yaw };
            Array.Copy(Road, copy.Road, Road.Length);
            Array.Copy(Fraction, copy.Fraction, Fraction.Length);
            Array.Copy(Length, copy.Length, Length.Length);
            Array.Copy(Along, copy.Along, Along.Length);
            Array.Copy(Lateral, copy.Lateral, Lateral.Length);
            Array.Copy(Height, copy.Height, Height.Length);
            Array.Copy(Written, copy.Written, Written.Length);
            return copy;
        }
    }

    /// <summary>Buffers a line's follow works in: one per thread, reused (nothing is made per line but its text).</summary>
    public sealed class LineWorkspace
    {
        internal float[] Distance = new float[8], RoadY = new float[8];
        internal int[] Candidate = new int[8];
        internal Vec3[] Mapped = new Vec3[RunPath.MaxPieces + 1];
        internal readonly List<Vec3> Points = new List<Vec3>(RunPath.MaxPieces + 1);
        internal readonly List<Vec3> Read = new List<Vec3>(RunPath.MaxPieces + 1);
    }

    /// <summary>
    /// A line item made again on roads that changed (0.12.74 follow work): what PathFollow.TryRemap does with the roads' old curves, with the part that does not change from one frame of a drag to the next done once
    /// (Measure) and the rest by numbers (Apply): the points' places on the new curves, a resampling by the limit the caller gives, and the text made again only when the line's shape changed by more than a threshold.
    /// The threshold: Metres (1 mm) and Radians (0.05 degrees, 0.9 mrad, which is 0.9 mm over the 1 m the text's 1 cm chords and 0.1 mrad turns already cannot tell apart): the text has a resolution of a centimetre in
    /// length and 0.1 mrad in direction, so a change below that writes the same text again; and as the points are measured from the same relation each frame and compared with what is written, a change that stays
    /// below the threshold never adds up. Pure; Apply changes only the relation it is given and the workspace.
    /// </summary>
    public static class LineFollow
    {
        public const float Metres = 0.001f;

        public const float Radians = 8.7266e-4f;

        /// <summary>The relation of a line (its path, position and yaw as written) to the roads it runs along (their old curves); false for a path that is not a line or a road with no shape.</summary>
        public static bool TryMeasure(string path, Vec3 position, float yaw, FollowCurve[] old, int roads, LineWorkspace work, out LineRelation line)
        {
            line = null;
            List<Vec3> local = RunPath.Decode(path);
            if (local.Count < 2 || roads < 1)
                return false;
            List<Vec3> world = MarkingPolygon.ToWorld(local, position, yaw);
            var made = new LineRelation { Count = world.Count, Path = path, Position = position, Yaw = yaw, WrittenCount = world.Count };
            float longest = 0f, hint = -1f;
            int last = -1;
            for (int j = 0; j < world.Count; j++)
            {
                Vec3 p = world[j];
                if (!p.IsFinite)
                    return false;
                int road = Nearest(old, roads, p, work, out bool stacked);
                if (stacked)
                    made.Stacked++;
                if (road < 0 || !old[road].TryMeasure(p, 0f, road == last ? hint : -1f, out RoadRelation r, out float t))
                    return false;
                made.Road[j] = road;
                made.Fraction[j] = r.Fraction;
                made.Length[j] = r.Length;
                made.Along[j] = r.Along;
                made.Lateral[j] = r.Lateral;
                made.Height[j] = r.Height;
                made.Written[j] = p;
                last = road;
                hint = t;
                if (j > 0)
                    longest = Math.Max(longest, Vec3.DistanceXZ(world[j - 1], p));
            }

            made.LongestChord = longest;
            line = made;
            return true;
        }

        /// <summary>
        /// The road whose curve is nearest to p, as the full follow finds it (the distance to the point of the curve nearest by ClosestT); of roads that lie over one another the one at the point's level
        /// is taken (RoadLevels; stacked says one was left out for that). Every road of the line is looked at (a line has a handful, and it is measured once): one that is far in the plane may be the one at
        /// the point's level.
        /// </summary>
        private static int Nearest(FollowCurve[] roads, int count, Vec3 p, LineWorkspace work, out bool stacked)
        {
            stacked = false;
            if (work.Distance.Length < count)
            {
                work.Distance = new float[count];
                work.RoadY = new float[count];
                work.Candidate = new int[count];
            }

            int best = -1, found = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                FollowCurve road = roads[i];
                if (road == null || !road.IsUsable)
                    continue;
                float t = road.ClosestT(p, -1f, out _);
                road.At(t, out Vec3 on, out _);
                float distance = Vec3.DistanceXZ(on, p);
                work.Candidate[found] = i;
                work.Distance[found] = distance;
                work.RoadY[found] = on.Y;
                found++;
                if (distance < nearest)
                {
                    nearest = distance;
                    best = i;
                }
            }

            if (found < 2)
                return best;
            int level = RoadLevels.Pick(work.Distance, work.RoadY, found, p.Y);
            int chosen = level < 0 ? best : work.Candidate[level];
            stacked = chosen != best;
            return chosen;
        }

        /// <summary>
        /// The line on the roads' curves now. limit is the longest chord the line may have (m; 0 for no limit): the chords the roads' new lengths made longer are cut in as many equal parts of their relation as
        /// bring them under it, up to RunPath.MaxPieces chords (stats.Wanted says how many it would take). On Rewritten and PoseOnly the line's relation keeps what was written (the caller writes it).
        /// </summary>
        public static LineOutcome Apply(LineRelation line, FollowCurve[] now, FollowMode mode, float limit, LineWorkspace work, out string newPath, out Vec3 newPosition, out float newYaw, out LineStats stats)
        {
            newPath = line.Path;
            newPosition = line.Position;
            newYaw = line.Yaw;
            stats = default;
            Vec3[] mapped = work.Mapped;
            for (int j = 0; j < line.Count; j++)
            {
                FollowCurve road = now[line.Road[j]];
                if (road == null || !road.Place(line.Fraction[j], line.Length[j], line.Along[j], line.Lateral[j], line.Height[j], 0f, mode, out Vec3 at, out _))
                    return LineOutcome.Failed;
                mapped[j] = at;
            }

            // Each point is put on its own road by its own share of it: where two roads meet at a node that moved, the first points of the one after can land behind the last of the one before, and the line
            // doubles back (0.12.97: the owner's save of 5 Nov has noise walls with a step back of 3 m, a turn of 179 degrees, at such places). A point that steps back is taken to the last one kept.
            stats.SteppedBack = RunPath.StepForward(mapped, line.Count);
            int count = line.Count;
            if (limit > 0f)
                count = Resample(line, now, mode, limit, ref mapped, ref stats);
            else
                stats.Wanted = count - 1;
            stats.Pieces = count - 1;
            for (int j = 1; j < count; j++)
                stats.LongestChord = Math.Max(stats.LongestChord, Vec3.DistanceXZ(mapped[j - 1], mapped[j]));

            if (count == line.WrittenCount)
            {
                float worst = 0f;
                for (int j = 0; j < count; j++)
                    worst = Math.Max(worst, (mapped[j] - line.Written[j]).Length);
                if (worst <= Metres)
                    return LineOutcome.Unchanged;
                if (SameShape(line.Written, mapped, count, out Vec3 shift, out float turn))
                {
                    newPosition = mapped[0];
                    newYaw = RoadRelation.Wrap(line.Yaw + turn);
                    // What is shown is the written shape carried by the motion, not the mapped points: the next frame is compared with what is shown, so that what is below the threshold does not add up.
                    Carry(line.Written, count, mapped[0], turn);
                    line.Position = newPosition;
                    line.Yaw = newYaw;
                    return LineOutcome.PoseOnly;
                }
            }

            work.Points.Clear();
            for (int j = 0; j < count; j++)
                work.Points.Add(mapped[j]);
            string text = RunPath.Encode(work.Points, out float turned, work.Read);
            if (text.Length == 0)
                return LineOutcome.Failed;
            newPath = text;
            newPosition = mapped[0];
            newYaw = turned;
            Remember(line, text, newPosition, newYaw, work.Read);
            return LineOutcome.Rewritten;
        }

        private static void Carry(Vec3[] points, int count, Vec3 to, float turn)
        {
            Vec3 from = points[0];
            float s = (float)Math.Sin(turn), c = (float)Math.Cos(turn);
            for (int j = 0; j < count; j++)
            {
                float x = points[j].X - from.X, z = points[j].Z - from.Z;
                points[j] = new Vec3(to.X + x * c + z * s, to.Y + points[j].Y - from.Y, to.Z + z * c - x * s);
            }
        }

        private static void Remember(LineRelation line, string path, Vec3 position, float yaw, IList<Vec3> points, int count = -1)
        {
            line.Path = path;
            line.Position = position;
            line.Yaw = yaw;
            line.WrittenCount = count < 0 ? Math.Min(points.Count, line.Written.Length) : count;
            for (int j = 0; j < line.WrittenCount; j++)
                line.Written[j] = points[j];
        }

        /// <summary>Whether b is a rigid motion of a within Metres (a translation and a turn about the vertical): the motion is the one that takes a's first point to b's and a's first chord's heading to b's.</summary>
        private static bool SameShape(Vec3[] a, Vec3[] b, int count, out Vec3 shift, out float turn)
        {
            shift = b[0] - a[0];
            int far = count - 1;
            turn = RoadRelation.Wrap(RoadRelation.HeadingYaw(b[far] - b[0]) - RoadRelation.HeadingYaw(a[far] - a[0]));
            float s = (float)Math.Sin(turn), c = (float)Math.Cos(turn);
            for (int j = 0; j < count; j++)
            {
                float x = a[j].X - a[0].X, z = a[j].Z - a[0].Z;
                var image = new Vec3(b[0].X + x * c + z * s, b[0].Y + a[j].Y - a[0].Y, b[0].Z + z * c - x * s);
                if ((image - b[j]).Length > Metres)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The resampling for the case a road got longer (its length ratio above 1, the chords of the line grow with it and a bend's sag with the square of the chord): each run of chords between points of one road whose
        /// longest chord is over the limit is cut into equal chords of its mapped length, as many as bring them under the limit and never fewer than it had, the new points at the relation interpolated between the
        /// points either side (so they lie on the road's offset curve, not on the chord). The whole line has at most RunPath.MaxPieces chords: when more are wanted the runs that were cut keep fewer and
        /// stats.Wanted says what it would take; the rest is for the caller to cut into more items. A run is never given fewer chords than it had (an item that has 32 already is not cut at all), and the runs that are not too
        /// long keep their points.
        /// </summary>
        private static int Resample(LineRelation line, FollowCurve[] now, FollowMode mode, float limit, ref Vec3[] mapped, ref LineStats stats)
        {
            int count = line.Count;
            bool over = false;
            for (int j = 1; j < count && !over; j++)
                over = line.Road[j] == line.Road[j - 1] && Vec3.DistanceXZ(mapped[j - 1], mapped[j]) > limit;
            stats.Wanted = count - 1;
            if (!over)
                return count;

            // The runs: [from, to] are points of one road; a chord between two roads is a run of its own that is not cut.
            var from = new List<int>();
            var want = new List<int>();
            var floor = new List<int>();
            var cuttable = new List<bool>();
            int total = 0, wantedTotal = 0, cutTotal = 0;
            for (int j = 0; j < count - 1;)
            {
                int k = j + 1;
                while (k < count - 1 && line.Road[k + 1] == line.Road[j] && line.Road[k] == line.Road[j])
                    k++;
                bool same = line.Road[k] == line.Road[j];
                float length = 0f, longest = 0f;
                for (int m = j + 1; m <= k; m++)
                {
                    float d = Vec3.DistanceXZ(mapped[m - 1], mapped[m]);
                    length += d;
                    longest = Math.Max(longest, d);
                }

                int chords = k - j;
                int pieces = same && longest > limit ? Math.Max(chords, (int)Math.Ceiling(length / limit - 1e-4f)) : chords;
                from.Add(j);
                want.Add(pieces);
                floor.Add(chords);
                cuttable.Add(same && pieces > chords);
                total += pieces;
                if (cuttable[cuttable.Count - 1])
                    cutTotal += pieces;
                j = k;
            }

            wantedTotal = total;
            stats.Wanted = wantedTotal;
            // More than the text holds: the runs that are cut give up chords in proportion, the longest first.
            while (total > RunPath.MaxPieces && cutTotal > 0)
            {
                int widest = -1;
                for (int r = 0; r < want.Count; r++)
                {
                    if (cuttable[r] && want[r] > floor[r] && (widest < 0 || want[r] - floor[r] > want[widest] - floor[widest]))
                        widest = r;
                }

                if (widest < 0)
                    break;
                want[widest]--;
                total--;
                cutTotal--;
            }

            var fine = new Vec3[total + 1];
            int n = 0;
            fine[n++] = mapped[0];
            for (int r = 0; r < want.Count; r++)
            {
                int j = from[r], k = r + 1 < from.Count ? from[r + 1] : count - 1;
                int pieces = want[r];
                if (!cuttable[r] || pieces == k - j)
                {
                    for (int m = j + 1; m <= k; m++)
                        fine[n++] = mapped[m];
                    continue;
                }

                float length = 0f;
                for (int m = j + 1; m <= k; m++)
                    length += Vec3.DistanceXZ(mapped[m - 1], mapped[m]);
                int chord = j + 1;
                float chordStart = 0f, chordLength = Vec3.DistanceXZ(mapped[j], mapped[chord]);
                FollowCurve road = now[line.Road[j]];
                for (int q = 1; q < pieces; q++)
                {
                    float at = length * q / pieces;
                    while (chord < k && at > chordStart + chordLength)
                    {
                        chordStart += chordLength;
                        chord++;
                        chordLength = Vec3.DistanceXZ(mapped[chord - 1], mapped[chord]);
                    }

                    float u = chordLength < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, (at - chordStart) / chordLength));
                    int i0 = chord - 1, i1 = chord;
                    if (!road.Place(Lerp(line.Fraction[i0], line.Fraction[i1], u), line.Length[i1], Lerp(line.Along[i0], line.Along[i1], u), Lerp(line.Lateral[i0], line.Lateral[i1], u), Lerp(line.Height[i0], line.Height[i1], u), 0f, mode, out Vec3 point, out _))
                        point = Vec3.Lerp(mapped[i0], mapped[i1], u);
                    fine[n++] = point;
                }

                fine[n++] = mapped[k];
            }

            mapped = fine;
            return n;
        }

        /// <summary>The length along the ground of the line as it is written (m): what the item's path is.</summary>
        public static float WrittenLength(LineRelation line)
        {
            float length = 0f;
            for (int j = 1; j < line.WrittenCount; j++)
                length += Vec3.DistanceXZ(line.Written[j - 1], line.Written[j]);
            return length;
        }

        private static float Lerp(float a, float b, float k) => a + (b - a) * k;
    }
}
