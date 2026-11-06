using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The path of a marking that runs along a road (a lane line, a kerb line, a hatched island's edge), kept as text in the item's settings so that it is saved with the item and drawn the same
    /// whatever the road later becomes. The path is a polyline in the item's own frame: it starts at the origin heading +Z (the item's yaw turns it into the world), and each piece is a chord
    /// of a length (cm), a turn from the direction of the chord before (1e-4 rad, positive toward +X, the driver's right) and a grade (rise over run, per mille); the first chord has no turn.
    /// Written "L:T:G|L:T:G|..." in whole numbers: a piece is about ten characters, so at most MaxPieces pieces fit the settings' field, and a longer run is several items. Pure.
    /// </summary>
    public static class RunPath
    {
        /// <summary>The most pieces of one path (the field the settings are saved in is 509 bytes, and the other settings of a marking take some).</summary>
        public const int MaxPieces = 32;

        /// <summary>The largest grade (per mille) a piece has: a grade of 1 is 45 degrees; Decode ends the path at a piece of more.</summary>
        public const int MaxGrade = 1000;

        /// <summary>The shortest chord kept (m): a shorter one is a repeated point.</summary>
        public const float MinChord = 0.05f;

        /// <summary>The key of the path in a marking's settings.</summary>
        public const string Key = "path";

        /// <summary>
        /// The text of a polyline (world points along the run, the first the item's position) and the yaw the item must have (the direction of the first chord). Chords of 0.05 m or less are
        /// dropped; null and an empty text when nothing is left or the points are not numbers. At most MaxPieces pieces are written: the rest of the line is not (the caller splits it).
        /// </summary>
        public static string Encode(IList<Vec3> points, out float yaw) => Encode(points, out yaw, null);

        /// <summary>
        /// As above, and when reader is given it is filled with the polyline that Decode (and MarkingPolygon.ToWorld at the first point and the yaw) will give back for the text: the points the rounding of each
        /// piece leaves, which the writer has at hand, so that a caller that keeps what it wrote need not decode it again.
        /// </summary>
        public static string Encode(IList<Vec3> points, out float yaw, List<Vec3> reader)
        {
            yaw = 0f;
            reader?.Clear();
            if (points == null || points.Count < 2)
                return "";
            var kept = new List<Vec3>(points.Count);
            foreach (Vec3 p in points)
            {
                if (!p.IsFinite)
                    return "";
                if (kept.Count == 0 || Vec3.DistanceXZ(kept[kept.Count - 1], p) > MinChord)
                    kept.Add(p);
            }

            if (kept.Count < 2)
                return "";
            var sb = new StringBuilder();
            // What the reader will have after the rounding of each piece, so that the rounding does not add up: the next turn is made against the direction the written pieces give, and the next chord
            // from the place they give to the true next point (0.12.73: the lengths were rounded each on its own, and 24 chords of 2.4949 m ended 118 mm short).
            double written = 0.0, writtenY = 0.0, writtenX = kept[0].X, writtenZ = kept[0].Z;
            int count = Math.Min(kept.Count - 1, MaxPieces);
            reader?.Add(kept[0]);
            for (int i = 0; i < count; i++)
            {
                double dx = kept[i + 1].X - writtenX, dz = kept[i + 1].Z - writtenZ;
                double length = Math.Sqrt(dx * dx + dz * dz);
                double direction = Math.Atan2(dx, dz);
                if (i == 0)
                {
                    yaw = (float)direction;
                    written = direction;
                }

                // The turn from the direction the reader has, to the true direction of this chord, wrapped to a half turn.
                double turn = i == 0 ? 0.0 : Wrap(direction - written);
                int turnUnits = (int)Math.Round(turn * 10000.0);
                written += turnUnits / 10000.0;
                int lengthCm = (int)Math.Round(length * 100.0);
                writtenX += lengthCm / 100.0 * Math.Sin(written);
                writtenZ += lengthCm / 100.0 * Math.Cos(written);
                // The rise against the height the reader has (0.12.20): the rounding of each grade does not add up along the 32 chords of a barrier that follows the road's surface.
                double rise = kept[i + 1].Y - (kept[0].Y + writtenY);
                // Not over the grade the reader takes (Decode ends a path at a piece of more): a sheer step is spread over the chords after it, as the rounding is.
                int grade = lengthCm > 0 ? Math.Max(-MaxGrade, Math.Min(MaxGrade, (int)Math.Round(rise / (lengthCm / 100.0) * 1000.0))) : 0;
                writtenY += grade / 1000.0 * (lengthCm / 100.0);
                reader?.Add(new Vec3((float)writtenX, (float)(kept[0].Y + writtenY), (float)writtenZ));
                if (i > 0)
                    sb.Append('|');
                sb.Append(lengthCm.ToString(CultureInfo.InvariantCulture)).Append(':').Append(turnUnits.ToString(CultureInfo.InvariantCulture)).Append(':').Append(grade.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>The polyline of a path in the item's frame: from the origin, heading +Z, at height 0. An empty or damaged path gives an empty list; pieces that are not whole numbers end it there.</summary>
        public static List<Vec3> Decode(string path)
        {
            var points = new List<Vec3>();
            if (string.IsNullOrEmpty(path))
                return points;
            double x = 0.0, y = 0.0, z = 0.0, direction = 0.0;
            points.Add(Vec3.Zero);
            string[] pieces = path.Split('|');
            int count = Math.Min(pieces.Length, MaxPieces);
            for (int i = 0; i < count; i++)
            {
                string[] part = pieces[i].Split(':');
                if (part.Length != 3 || !int.TryParse(part[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lengthCm)
                    || !int.TryParse(part[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int turnUnits) || !int.TryParse(part[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade))
                    break;
                if (lengthCm <= 0 || lengthCm > 100000 || Math.Abs(turnUnits) > 31416 || Math.Abs(grade) > MaxGrade)
                    break;
                direction += turnUnits / 10000.0;
                double length = lengthCm / 100.0;
                x += length * Math.Sin(direction);
                z += length * Math.Cos(direction);
                y += length * grade / 1000.0;
                points.Add(new Vec3((float)x, (float)y, (float)z));
            }

            points = WithoutSpurs(points);
            if (points.Count < 2)
                points.Clear();
            return points;
        }

        /// <summary>
        /// 0.12.97: the cosine under which a chord turns back on the one before it (120 degrees): a barrier's path does not double back on itself. The paths the follow made at a node that moved did: the owner's
        /// save of 5 Nov has noise walls whose path goes 6 m on, steps back 3 m on the same line (turn 179 degrees) and goes on again (turn 163), seen as a switchback at a place where the road changed.
        /// A sharpest real corner of a wall is a right angle.
        /// </summary>
        public const float SpurCosine = -0.5f;

        /// <summary>
        /// The points of a polyline without the ones that step back (0.12.97): a point whose chord from the last point kept turns back on the one before it by more than 120 degrees (SpurCosine) is left out,
        /// and so is each point after it until one that lies ahead; a path that ends in such a step ends where it was. The same list when there is nothing to leave out (an ordinary path is not changed by a bit),
        /// else a new one. The first chord is never turned back on (nothing is before it). Pure.
        /// </summary>
        public static List<Vec3> WithoutSpurs(List<Vec3> points)
        {
            if (points == null || points.Count < 3)
                return points;
            List<Vec3> kept = null;
            float dirX = 0f, dirZ = 0f;
            Vec3 last = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                Vec3 p = points[i];
                float dx = p.X - last.X, dz = p.Z - last.Z;
                float length = (float)Math.Sqrt(dx * dx + dz * dz);
                if (length > 1e-4f && (dirX != 0f || dirZ != 0f) && (dx * dirX + dz * dirZ) / length < SpurCosine)
                {
                    if (kept == null)
                    {
                        kept = new List<Vec3>(points.Count);
                        for (int k = 0; k < i; k++)
                            kept.Add(points[k]);
                    }

                    continue;
                }

                kept?.Add(p);
                if (length > 1e-4f)
                {
                    dirX = dx / length;
                    dirZ = dz / length;
                }

                last = p;
            }

            return kept ?? points;
        }

        /// <summary>
        /// The same rule for points that must stay where they are in a list (0.12.97, LineFollow.Apply: the mapped points are indexed with the line's relation): a point that steps back is put on the last point
        /// kept, so that the chord to it is nothing and RunPath.Encode drops it. Returns how many were put back.
        /// </summary>
        public static int StepForward(Vec3[] points, int count)
        {
            if (points == null || count < 3)
                return 0;
            int moved = 0;
            float dirX = 0f, dirZ = 0f;
            Vec3 last = points[0];
            for (int i = 1; i < count; i++)
            {
                Vec3 p = points[i];
                float dx = p.X - last.X, dz = p.Z - last.Z;
                float length = (float)Math.Sqrt(dx * dx + dz * dz);
                if (length > 1e-4f && (dirX != 0f || dirZ != 0f) && (dx * dirX + dz * dirZ) / length < SpurCosine)
                {
                    points[i] = last;
                    moved++;
                    continue;
                }

                if (length > 1e-4f)
                {
                    dirX = dx / length;
                    dirZ = dz / length;
                }

                last = p;
            }

            return moved;
        }

        /// <summary>The length along the ground of a path (m).</summary>
        public static float Length(string path)
        {
            List<Vec3> points = Decode(path);
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
                total += Vec3.DistanceXZ(points[i - 1], points[i]);
            return total;
        }

        /// <summary>
        /// Cuts a polyline into runs of at most maxLength (m) and MaxPieces pieces each, ready for items: each run starts where the one before ended (the points are shared), so the runs join. The
        /// polyline is first resampled so that no chord is longer than maxChord (m).
        /// </summary>
        public static List<List<Vec3>> Split(IList<Vec3> points, float maxLength, float maxChord = 3.5f)
        {
            var runs = new List<List<Vec3>>();
            if (points == null || points.Count < 2 || !(maxLength > 0f) || !(maxChord > 0f))
                return runs;
            var fine = new List<Vec3> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if (!points[i].IsFinite)
                    return new List<List<Vec3>>();
                float d = Vec3.DistanceXZ(points[i - 1], points[i]);
                int pieces = Math.Max(1, (int)Math.Ceiling(d / maxChord));
                for (int k = 1; k <= pieces; k++)
                    fine.Add(Vec3.Lerp(points[i - 1], points[i], k / (float)pieces));
            }

            var run = new List<Vec3> { fine[0] };
            float length = 0f;
            for (int i = 1; i < fine.Count; i++)
            {
                float d = Vec3.DistanceXZ(fine[i - 1], fine[i]);
                if (d <= MinChord)
                    continue;
                if (run.Count - 1 >= MaxPieces || (length + d > maxLength && run.Count > 1))
                {
                    runs.Add(run);
                    run = new List<Vec3> { fine[i - 1] };
                    length = 0f;
                }

                run.Add(fine[i]);
                length += d;
            }

            if (run.Count > 1)
                runs.Add(run);
            return runs;
        }

        internal static double Wrap(double angle)
        {
            while (angle > Math.PI)
                angle -= 2.0 * Math.PI;
            while (angle < -Math.PI)
                angle += 2.0 * Math.PI;
            return angle;
        }
    }
}
