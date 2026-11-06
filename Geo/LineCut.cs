using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// One item of a line drawn along a road (a wall, a rail, a lane line), as the cut sees it (0.12.73): where it starts and which way, its path (RunPath: chords of a length in cm, a turn in 1e-4 rad
    /// and a grade in per mille), where it starts along the whole line (PhaseCm) and how long the whole line is (TotalCm; 0: not known), and Family, a text that is equal for the items of one line (the
    /// sign and the settings the line's items share). The chords are the valid ones, as RunPath.Decode reads them.
    /// </summary>
    public sealed class CutPiece
    {
        public readonly string Family;
        public readonly Vec3 Start;
        public readonly float Yaw;
        public readonly string Path;
        public readonly int PhaseCm, TotalCm;
        public readonly int[] Len, Turn, Grade;
        public readonly int LengthCm;

        public CutPiece(string family, Vec3 start, float yaw, string path, int phaseCm, int totalCm)
        {
            Family = family ?? "";
            Start = start;
            Yaw = yaw;
            Path = path ?? "";
            PhaseCm = phaseCm;
            TotalCm = totalCm;
            var len = new List<int>();
            var turn = new List<int>();
            var grade = new List<int>();
            string[] pieces = Path.Length == 0 ? new string[0] : Path.Split('|');
            for (int i = 0; i < pieces.Length && i < RunPath.MaxPieces; i++)
            {
                string[] part = pieces[i].Split(':');
                if (part.Length != 3 || !int.TryParse(part[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int l) || !int.TryParse(part[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int t)
                    || !int.TryParse(part[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) || l <= 0 || l > 100000 || Math.Abs(t) > 31416 || Math.Abs(g) > RunPath.MaxGrade)
                    break;
                len.Add(l);
                turn.Add(t);
                grade.Add(g);
                LengthCm += l;
            }

            Len = len.ToArray();
            Turn = turn.ToArray();
            Grade = grade.ToArray();
        }

        /// <summary>Whether the item has a path to cut.</summary>
        public bool Valid => Len.Length > 0;

        public int EndCm => PhaseCm + LengthCm;

        /// <summary>The points of the path in the world: the start, and the end of each chord.</summary>
        public List<Vec3> Points()
        {
            var points = new List<Vec3>(Len.Length + 1) { Start };
            double dir = Yaw, x = Start.X, y = Start.Y, z = Start.Z;
            for (int i = 0; i < Len.Length; i++)
            {
                dir += Turn[i] / 10000.0;
                double l = Len[i] / 100.0;
                x += l * Math.Sin(dir);
                z += l * Math.Cos(dir);
                y += l * Grade[i] / 1000.0;
                points.Add(new Vec3((float)x, (float)y, (float)z));
            }

            return points;
        }
    }

    /// <summary>What the cut does to an item: write it as it is now given, remove it, or make a new one that is like the item Piece.</summary>
    public enum CutKind { Write, Remove, Add }

    public struct CutEdit
    {
        public int Piece;
        public CutKind Kind;
        public Vec3 Start;
        public float Yaw;
        public string Path;
        public int PhaseCm, TotalCm;
    }

    /// <summary>
    /// A stretch taken out of a line (0.12.73; the owner: "升级删除工具，支持只删除一段"): the line is the items that go on from one another (the phase of the next is where the one before ends, the same
    /// whole length, the same settings, a start where the one before ends), and a stretch between two distances along it is removed. What is cut off is made again as items of their own: the
    /// part before it keeps its start and is told the line ends where the stretch begins (so that it has an end post or a cap there), the part after it starts where the stretch ends, with phase 0, and the items after it
    /// are moved up by the same (so that the pattern of the part after is its own, from its own start). Whole items inside the stretch are removed; one that the stretch ends in is shortened (its path cut in a chord, so
    /// that the angles of the rest are the same); one that holds the whole stretch is shortened and made again for the part after. A part shorter than a metre is taken with the stretch. Pure.
    /// </summary>
    public static class LineCut
    {
        /// <summary>How far (cm) the phase of the next item may be from where the one before ends: the lengths and the phases are rounded in the run's text.</summary>
        public const int PhaseSlackCm = 6;

        /// <summary>How far (m) the next item may start from where the one before ends.</summary>
        public const float JoinGap = 0.35f;

        /// <summary>The shortest part of a line that is kept (cm), as MarkingRuns.MinLength.</summary>
        public const int MinKeepCm = 100;

        /// <summary>Whether b is the item that goes on from a in one line.</summary>
        public static bool Follows(CutPiece a, CutPiece b)
        {
            if (!a.Valid || !b.Valid || a.Family != b.Family || a.TotalCm != b.TotalCm || Math.Abs(b.PhaseCm - a.EndCm) > PhaseSlackCm)
                return false;
            List<Vec3> end = a.Points();
            return Vec3.DistanceXZ(end[end.Count - 1], b.Start) <= JoinGap;
        }

        /// <summary>Where a candidate goes in a chain of items: -1 before the first, 1 after the last, 0 where it does not join.</summary>
        public static int Place(IList<CutPiece> chain, CutPiece candidate)
        {
            if (chain.Count == 0)
                return 0;
            if (Follows(chain[chain.Count - 1], candidate))
                return 1;
            return Follows(candidate, chain[0]) ? -1 : 0;
        }

        /// <summary>The whole line's points, in order (the points two items share are one), and the distance of each along the line (m: the phase of the first item and the chords' lengths).</summary>
        public static List<Vec3> Line(IList<CutPiece> chain, out List<float> at)
        {
            var points = new List<Vec3>();
            at = new List<float>();
            double along = chain.Count > 0 ? chain[0].PhaseCm / 100.0 : 0.0;
            foreach (CutPiece piece in chain)
            {
                List<Vec3> own = piece.Points();
                for (int i = 0; i < own.Count; i++)
                {
                    if (i == 0 && points.Count > 0)
                        continue;
                    if (i > 0)
                        along += piece.Len[i - 1] / 100.0;
                    points.Add(own[i]);
                    at.Add((float)along);
                }
            }

            return points;
        }

        /// <summary>
        /// Where along a line (m) the ray passes nearest it, the line's points lifted by lift (the middle of a wall); gap is how near (m, in space). The ray is the segment from..to.
        /// </summary>
        public static float Nearest(IList<Vec3> points, IList<float> at, Vec3 from, Vec3 to, float lift, out float gap)
        {
            gap = float.MaxValue;
            if (points.Count == 0)
                return 0f;
            float best = at[0];
            if (points.Count == 1)
            {
                gap = DistanceToSegment(points[0] + new Vec3(0f, lift, 0f), from, to);
                return best;
            }

            for (int i = 1; i < points.Count; i++)
            {
                Vec3 a = points[i - 1] + new Vec3(0f, lift, 0f), b = points[i] + new Vec3(0f, lift, 0f);
                float distance = Approach(a, b, from, to, out float t);
                if (distance < gap)
                {
                    gap = distance;
                    best = at[i - 1] + (at[i] - at[i - 1]) * t;
                }
            }

            return best;
        }

        private static float DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 d = b - a;
            float l2 = Vec3.Dot(d, d);
            float t = l2 > 1e-9f ? Math.Max(0f, Math.Min(1f, Vec3.Dot(p - a, d) / l2)) : 0f;
            return (p - (a + d * t)).Length;
        }

        /// <summary>The nearest approach of the segments a..b and from..to (Ericson, Real-Time Collision Detection 5.1.9): their distance, and where on a..b (0 to 1) it is.</summary>
        private static float Approach(Vec3 a, Vec3 b, Vec3 from, Vec3 to, out float onAb)
        {
            Vec3 d1 = b - a, d2 = to - from, r = a - from;
            float aa = Vec3.Dot(d1, d1), e = Vec3.Dot(d2, d2), f = Vec3.Dot(d2, r);
            float s, t;
            if (aa <= 1e-9f && e <= 1e-9f)
            {
                onAb = 0f;
                return r.Length;
            }

            if (aa <= 1e-9f)
            {
                s = 0f;
                t = Math.Max(0f, Math.Min(1f, f / e));
            }
            else
            {
                float c = Vec3.Dot(d1, r);
                if (e <= 1e-9f)
                {
                    t = 0f;
                    s = Math.Max(0f, Math.Min(1f, -c / aa));
                }
                else
                {
                    float bb = Vec3.Dot(d1, d2), denom = aa * e - bb * bb;
                    s = denom > 1e-9f ? Math.Max(0f, Math.Min(1f, (bb * f - c * e) / denom)) : 0f;
                    t = (bb * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Math.Max(0f, Math.Min(1f, -c / aa));
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Math.Max(0f, Math.Min(1f, (bb - c) / aa));
                    }
                }
            }

            onAb = s;
            return ((a + d1 * s) - (from + d2 * t)).Length;
        }

        /// <summary>The part of a line between two distances along it (m), for the overlay: the points between, and the ends where the distances are.</summary>
        public static List<Vec3> Stretch(IList<Vec3> points, IList<float> at, float a, float b)
        {
            var result = new List<Vec3>();
            if (points.Count < 2 || b <= a)
                return result;
            result.Add(PointAt(points, at, a));
            for (int i = 0; i < points.Count; i++)
            {
                if (at[i] > a && at[i] < b)
                    result.Add(points[i]);
            }

            result.Add(PointAt(points, at, b));
            return result;
        }

        /// <summary>The point of a line at a distance along it (m); the line's ends beyond them.</summary>
        public static Vec3 PointAt(IList<Vec3> points, IList<float> at, float s)
        {
            if (s <= at[0])
                return points[0];
            for (int i = 1; i < points.Count; i++)
            {
                if (s <= at[i])
                    return Vec3.Lerp(points[i - 1], points[i], at[i] > at[i - 1] ? (s - at[i - 1]) / (at[i] - at[i - 1]) : 0f);
            }

            return points[points.Count - 1];
        }

        /// <summary>
        /// The edits that take the stretch from a to b (m along the line) out of the chain of items. False when there is nothing to take (the stretch is outside the line or has no length). Each edit names
        /// the item (Piece, an index into chain) it writes, removes, or is made like.
        /// </summary>
        public static bool Cut(IList<CutPiece> chain, float a, float b, out List<CutEdit> edits)
        {
            edits = new List<CutEdit>();
            if (chain.Count == 0 || !(b > a))
                return false;
            int start = chain[0].PhaseCm, end = chain[chain.Count - 1].EndCm;
            int aCm = Math.Max(start, (int)Math.Round(a * 100f)), bCm = Math.Min(end, (int)Math.Round(b * 100f));
            if (bCm <= aCm)
                return false;
            int whole = chain[0].TotalCm > 0 ? chain[0].TotalCm : end;

            // A part of an item that would be left shorter than the least is taken with the stretch.
            foreach (CutPiece piece in chain)
            {
                if (piece.PhaseCm < aCm && aCm < piece.EndCm && aCm - piece.PhaseCm < MinKeepCm)
                    aCm = piece.PhaseCm;
            }

            foreach (CutPiece piece in chain)
            {
                if (piece.PhaseCm < bCm && bCm < piece.EndCm && piece.EndCm - bCm < MinKeepCm)
                    bCm = piece.EndCm;
            }

            for (int i = 0; i < chain.Count; i++)
            {
                CutPiece piece = chain[i];
                if (piece.EndCm <= aCm)
                {
                    Write(edits, i, piece, piece.Start, piece.Yaw, piece.Path, piece.PhaseCm, aCm);
                    continue;
                }

                if (piece.PhaseCm >= bCm)
                {
                    Write(edits, i, piece, piece.Start, piece.Yaw, piece.Path, piece.PhaseCm - bCm, whole - bCm);
                    continue;
                }

                bool before = piece.PhaseCm < aCm, after = piece.EndCm > bCm;
                if (!before && !after)
                {
                    edits.Add(new CutEdit { Piece = i, Kind = CutKind.Remove });
                    continue;
                }

                if (before)
                {
                    Split(piece, aCm - piece.PhaseCm, out string head, out _, out _, out _);
                    Write(edits, i, piece, piece.Start, piece.Yaw, head, piece.PhaseCm, aCm);
                }

                if (after)
                {
                    Split(piece, bCm - piece.PhaseCm, out _, out string tail, out Vec3 from, out float yaw);
                    var edit = new CutEdit { Piece = i, Kind = before ? CutKind.Add : CutKind.Write, Start = from, Yaw = yaw, Path = tail, PhaseCm = 0, TotalCm = whole - bCm };
                    edits.Add(edit);
                }
            }

            return true;
        }

        private static void Write(List<CutEdit> edits, int index, CutPiece piece, Vec3 start, float yaw, string path, int phaseCm, int totalCm)
        {
            if (path == piece.Path && phaseCm == piece.PhaseCm && totalCm == piece.TotalCm)
                return;
            edits.Add(new CutEdit { Piece = index, Kind = CutKind.Write, Start = start, Yaw = yaw, Path = path, PhaseCm = phaseCm, TotalCm = totalCm });
        }

        /// <summary>
        /// A path cut at a distance from its start (cm, inside it): the path before it, the path after it, and where and which way the one after starts. A cut in a chord shortens it in the part
        /// before and starts the part after with the rest of it, turned the way the chord ran, so that the angles of every chord are as they were.
        /// </summary>
        public static void Split(CutPiece piece, int atCm, out string head, out string tail, out Vec3 from, out float yaw)
        {
            var before = new List<string>();
            var after = new List<string>();
            double dir = piece.Yaw, x = piece.Start.X, y = piece.Start.Y, z = piece.Start.Z;
            int done = 0;
            from = piece.Start;
            yaw = piece.Yaw;
            bool cut = false;
            for (int i = 0; i < piece.Len.Length; i++)
            {
                dir += piece.Turn[i] / 10000.0;
                int length = piece.Len[i];
                if (!cut && atCm < done + length)
                {
                    cut = true;
                    int first = atCm - done;
                    double d = first / 100.0;
                    from = new Vec3((float)(x + d * Math.Sin(dir)), (float)(y + d * piece.Grade[i] / 1000.0), (float)(z + d * Math.Cos(dir)));
                    yaw = (float)RunPath.Wrap(dir);
                    if (first > 0)
                        before.Add(Chord(first, piece.Turn[i], piece.Grade[i]));
                    after.Add(Chord(length - first, 0, piece.Grade[i]));
                }
                else if (cut)
                {
                    after.Add(Chord(length, piece.Turn[i], piece.Grade[i]));
                }
                else
                {
                    before.Add(Chord(length, piece.Turn[i], piece.Grade[i]));
                }

                double l = length / 100.0;
                x += l * Math.Sin(dir);
                z += l * Math.Cos(dir);
                y += l * piece.Grade[i] / 1000.0;
                done += length;
            }

            if (!cut)
            {
                from = new Vec3((float)x, (float)y, (float)z);
                yaw = (float)RunPath.Wrap(dir);
            }

            head = string.Join("|", before);
            tail = string.Join("|", after);
        }

        private static string Chord(int length, int turn, int grade) =>
            new StringBuilder().Append(length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(turn.ToString(CultureInfo.InvariantCulture)).Append(':').Append(grade.ToString(CultureInfo.InvariantCulture)).ToString();
    }
}
