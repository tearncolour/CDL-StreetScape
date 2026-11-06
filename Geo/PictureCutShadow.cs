using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One board of a picture cut that was made both ways: the cell cut of 0.12.72 (what the board uses) and the cut from the contour.</summary>
    public sealed class CutComparison
    {
        public string Key;
        public int OldVertices, OldTriangles, NewVertices, NewTriangles;
        public double OldMs, NewMs;

        /// <summary>Areas (m2 on the board): the old cut, the new cut, what only the old one covers, what only the new one covers.</summary>
        public double AreaOld, AreaNew, OldOnly, NewOnly;

        public float Tolerance;

        /// <summary>Why the contour was not used (empty when it was): the new cut's fields are then zero.</summary>
        public string Reason = "";

        /// <summary>The share of the contour's opaque area that the old cut does not cover: what a board loses of the picture.</summary>
        public double Lost => AreaNew > 1e-12 ? NewOnly / AreaNew : 0.0;

        /// <summary>The share of the old cut that is not on the contour's opaque area: what the board shows of what is clear.</summary>
        public double Excess => AreaOld > 1e-12 ? OldOnly / AreaOld : 0.0;
    }

    /// <summary>The comparisons of the picture cut, counted without limit and kept as summaries: the first lines in full, the sums, and the worst by what the old cut loses. Not thread safe: PictureCutShadow holds its lock.</summary>
    public sealed class CutShadowStats
    {
        public const int Limit = 2000, Worst = 20, Detailed = 8;

        private static readonly int[] s_SummaryAt = { 50, 250, 1000, Limit };

        public int Count, Fallbacks, Dropped, NotReady;
        public double OldMs, NewMs, LostSum, ExcessSum, WorstLost, WorstExcess;
        public long OldVertices, NewVertices, OldTriangles, NewTriangles;
        public int Relaxed, OverBudget;
        public readonly List<CutComparison> WorstList = new List<CutComparison>();
        public readonly Dictionary<string, int> Reasons = new Dictionary<string, int>();

        public bool Full => Count >= Limit;

        /// <summary>Counts a comparison; the lines for the log (none after the limit).</summary>
        public List<string> Add(CutComparison c)
        {
            var lines = new List<string>();
            if (Full)
                return lines;
            Count++;
            OldMs += c.OldMs;
            OldVertices += c.OldVertices;
            OldTriangles += c.OldTriangles;
            if (c.Reason.Length > 0)
            {
                Fallbacks++;
                Reasons[c.Reason.StartsWith("over the budget") ? "over the budget" : c.Reason] = (Reasons.TryGetValue(c.Reason.StartsWith("over the budget") ? "over the budget" : c.Reason, out int n) ? n : 0) + 1;
                if (c.Reason.StartsWith("over the budget"))
                    OverBudget++;
            }
            else
            {
                NewMs += c.NewMs;
                NewVertices += c.NewVertices;
                NewTriangles += c.NewTriangles;
                LostSum += c.Lost;
                ExcessSum += c.Excess;
                WorstLost = Math.Max(WorstLost, c.Lost);
                WorstExcess = Math.Max(WorstExcess, c.Excess);
                if (c.Tolerance > 0.5f + 1e-3f)
                    Relaxed++;
                Rank(c);
            }

            if (Count <= Detailed)
                lines.Add("Picture cut shadow: " + Describe(c));
            foreach (int at in s_SummaryAt)
            {
                if (Count == at)
                    lines.Add(Summary(at == Limit));
            }

            return lines;
        }

        private void Rank(CutComparison c)
        {
            int at = WorstList.Count;
            while (at > 0 && WorstList[at - 1].Lost < c.Lost)
                at--;
            if (at >= Worst)
                return;
            WorstList.Insert(at, c);
            if (WorstList.Count > Worst)
                WorstList.RemoveAt(WorstList.Count - 1);
        }

        public static string Describe(CutComparison c)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            if (c.Reason.Length > 0)
                return $"{c.Key}: the contour was not used ({c.Reason}); the cell cut {c.OldVertices} vertices, {c.OldTriangles} triangles, {c.OldMs.ToString("0.00", ci)} ms";
            return $"{c.Key}: cell cut {c.OldVertices} v, {c.OldTriangles} t, {c.OldMs.ToString("0.00", ci)} ms, area {(c.AreaOld * 1e4).ToString("0.0", ci)} cm2; contour {c.NewVertices} v, {c.NewTriangles} t, {c.NewMs.ToString("0.00", ci)} ms, area {(c.AreaNew * 1e4).ToString("0.0", ci)} cm2, tolerance {c.Tolerance.ToString("0.00", ci)} px; "
                + $"the cell cut loses {(c.Lost * 100.0).ToString("0.00", ci)} % of the contour's opaque area and shows {(c.Excess * 100.0).ToString("0.00", ci)} % of its own beyond it (symmetric difference {((c.OldOnly + c.NewOnly) * 1e4).ToString("0.00", ci)} cm2)";
        }

        public string Summary(bool final)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            int used = Count - Fallbacks;
            var text = new StringBuilder();
            text.Append("Picture cut shadow").Append(final ? " (the last, " + Limit + " compared)" : "").Append(": ").Append(Count).Append(" board(s) compared, ").Append(used).Append(" with the contour, ").Append(Fallbacks).Append(" where it was not used");
            if (Reasons.Count > 0)
            {
                text.Append(" (");
                bool first = true;
                foreach (KeyValuePair<string, int> r in Reasons)
                {
                    text.Append(first ? "" : "; ").Append(r.Key).Append(" x").Append(r.Value);
                    first = false;
                }

                text.Append(')');
            }

            if (used > 0)
            {
                text.Append("; mean per board: cell cut ").Append((OldVertices / (double)Count).ToString("0", ci)).Append(" v ").Append((OldTriangles / (double)Count).ToString("0", ci)).Append(" t ").Append((OldMs / Count).ToString("0.00", ci)).Append(" ms, contour ")
                    .Append((NewVertices / (double)used).ToString("0", ci)).Append(" v ").Append((NewTriangles / (double)used).ToString("0", ci)).Append(" t ").Append((NewMs / used).ToString("0.00", ci)).Append(" ms (").Append(Relaxed).Append(" relaxed above 0.5 px)");
                text.Append("; the cell cut loses ").Append((LostSum / used * 100.0).ToString("0.00", ci)).Append(" % of the opaque area on average (worst ").Append((WorstLost * 100.0).ToString("0.00", ci)).Append(" %) and shows ").Append((ExcessSum / used * 100.0).ToString("0.00", ci))
                    .Append(" % beyond it (worst ").Append((WorstExcess * 100.0).ToString("0.00", ci)).Append(" %)");
            }

            if (Dropped > 0 || NotReady > 0)
                text.Append("; ").Append(Dropped).Append(" not compared (a queue of boards waited), ").Append(NotReady).Append(" without a contour yet");
            if (final && WorstList.Count > 0)
            {
                text.Append("; the worst ").Append(WorstList.Count).Append(" by what the cell cut loses:");
                foreach (CutComparison c in WorstList)
                    text.Append("\n  ").Append(Describe(c));
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// The board of a picture cut both ways, off the thread that builds it (0.12.74; the shadow of DESIGN-wiring-plan.md step D): the board is built from the cell cut as before, and the contour's cut, the area they
    /// differ by and the costs of each are worked out here, on a task of the thread pool, from a job that holds copies of what it needs. The first 2,000 are compared and kept as counters (the first few in full, the
    /// worst 20); nothing is written after that and no job is queued. A job that waits behind 32 others is dropped (counted). Used from any thread.
    /// </summary>
    public static class PictureCutShadow
    {
        private const int kQueue = 32, kRows = 400;

        /// <summary>What a board gives the shadow: everything PictureCut.Make takes (copied: the part may be changed after) and the cell cut that the board uses.</summary>
        public sealed class Job
        {
            public string Key;
            public PictureContour Contour;
            public List<Point2> Outline;
            public float PartW, PartH, ImageX, ImageY, ImageW, ImageH, PictureAspect;
            public bool Crop;
            public List<List<Point2>> Old;
            public double OldMs;
        }

        private static readonly object s_Lock = new object();
        private static readonly ConcurrentQueue<Job> s_Queue = new ConcurrentQueue<Job>();
        private static int s_Waiting, s_Running;
        private static CutShadowStats s_Stats = new CutShadowStats();

        /// <summary>Where the lines go (the mod's log); null: nowhere.</summary>
        public static Action<string> Log;

        /// <summary>The counters, for a test or a summary (a snapshot of the numbers that matter is not made: read it from one thread).</summary>
        public static CutShadowStats Stats
        {
            get
            {
                lock (s_Lock)
                    return s_Stats;
            }
        }

        /// <summary>Forgets the counts (a test starts again).</summary>
        public static void Reset()
        {
            Drain();
            lock (s_Lock)
                s_Stats = new CutShadowStats();
        }

        /// <summary>Counts a board that had no contour yet.</summary>
        public static void NoteNotReady()
        {
            lock (s_Lock)
                s_Stats.NotReady++;
        }

        /// <summary>Gives a board to the shadow; returns at once. False when it was not taken (the limit is reached, or too many wait).</summary>
        public static bool Offer(Job job)
        {
            lock (s_Lock)
            {
                if (s_Stats.Full)
                    return false;
                if (s_Waiting >= kQueue)
                {
                    s_Stats.Dropped++;
                    return false;
                }

                s_Waiting++;
            }

            s_Queue.Enqueue(job);
            if (Interlocked.CompareExchange(ref s_Running, 1, 0) == 0)
                Task.Run((Action)Work);
            return true;
        }

        /// <summary>Waits until the queued boards are done (a test; the mod never waits).</summary>
        public static void Drain()
        {
            var spin = new SpinWait();
            while (Volatile.Read(ref s_Running) != 0 || !s_Queue.IsEmpty)
                spin.SpinOnce();
        }

        private static void Work()
        {
            try
            {
                for (;;)
                {
                    while (s_Queue.TryDequeue(out Job job))
                    {
                        CutComparison comparison = Compare(job);
                        List<string> lines;
                        lock (s_Lock)
                        {
                            s_Waiting--;
                            lines = s_Stats.Add(comparison);
                        }

                        foreach (string line in lines)
                            Log?.Invoke(line);
                    }

                    Volatile.Write(ref s_Running, 0);
                    if (s_Queue.IsEmpty || Interlocked.CompareExchange(ref s_Running, 1, 0) != 0)
                        return;
                }
            }
            catch (Exception ex)
            {
                Volatile.Write(ref s_Running, 0);
                Log?.Invoke("Picture cut shadow: stopped by " + ex.GetType().Name + ": " + ex.Message);
                lock (s_Lock)
                    s_Stats.Count = CutShadowStats.Limit;
            }
        }

        /// <summary>The comparison of one board (pure; the job's contour is cut and set against the cell cut).</summary>
        public static CutComparison Compare(Job job)
        {
            var c = new CutComparison { Key = job.Key, OldMs = job.OldMs };
            Counts(job.Old, out c.OldVertices, out c.OldTriangles);
            PictureCutResult cut = PictureCut.Make(job.Contour, job.Outline, job.PartW, job.PartH, job.ImageX, job.ImageY, job.ImageW, job.ImageH, job.Crop, job.PictureAspect);
            c.NewMs = cut.Ms;
            if (cut.Loops == null)
            {
                c.Reason = cut.Reason.Length > 0 ? cut.Reason : "no result";
                return c;
            }

            c.NewVertices = cut.Vertices;
            c.NewTriangles = cut.Triangles;
            c.Tolerance = cut.Tolerance;
            PictureCut.Compare(job.Old ?? new List<List<Point2>>(), cut.Loops, kRows, out c.AreaOld, out c.AreaNew, out c.OldOnly, out c.NewOnly);
            return c;
        }

        /// <summary>The vertices and the triangles of a board cut to loops, as it is built (the face and the back, the edge); none for no loops.</summary>
        public static void Counts(List<List<Point2>> loops, out int vertices, out int triangles)
        {
            vertices = triangles = 0;
            if (loops == null)
                return;
            foreach (List<Point2> loop in loops)
                vertices += loop.Count;
            var points = new List<Point2>();
            var indices = new List<int>();
            CutOutline.Triangulate(loops, points, indices);
            triangles = 2 * (indices.Count / 3) + 2 * vertices;
        }
    }
}

namespace CDLC.TS.Geo
{
    /// <summary>The boards cut from the contour when it is the cut in use (features.pictureCut on): counted, the first few said in full and a summary at 50, 250 and 1,000 boards; where the contour could not be used the board is cut as 0.12.72 did and the reason is counted. Any thread.</summary>
    public static class PictureCutUse
    {
        private static readonly object s_Lock = new object();
        private static int s_Used, s_Fallbacks, s_NotReady, s_Relaxed, s_Lines;
        private static long s_Vertices, s_Triangles;
        private static double s_Ms, s_WorstMs;
        private static readonly Dictionary<string, int> s_Reasons = new Dictionary<string, int>();

        /// <summary>Where the lines go (the mod's log); null: nowhere.</summary>
        public static Action<string> Log;

        public static int Used { get { lock (s_Lock) return s_Used; } }

        public static int Fallbacks { get { lock (s_Lock) return s_Fallbacks; } }

        public static int NotReady { get { lock (s_Lock) return s_NotReady; } }

        public static void Reset()
        {
            lock (s_Lock)
            {
                s_Used = s_Fallbacks = s_NotReady = s_Relaxed = s_Lines = 0;
                s_Vertices = s_Triangles = 0;
                s_Ms = s_WorstMs = 0.0;
                s_Reasons.Clear();
            }
        }

        public static void NoteNotReady()
        {
            lock (s_Lock)
                s_NotReady++;
        }

        public static void Note(string key, PictureCutResult r)
        {
            string line = null;
            var ci = CultureInfo.InvariantCulture;
            lock (s_Lock)
            {
                if (r.Loops != null)
                {
                    s_Used++;
                    s_Vertices += r.Vertices;
                    s_Triangles += r.Triangles;
                    s_Ms += r.Ms;
                    s_WorstMs = Math.Max(s_WorstMs, r.Ms);
                    if (r.Tolerance > 0.5f + 1e-3f)
                        s_Relaxed++;
                }
                else
                {
                    s_Fallbacks++;
                    string reason = r.Reason.StartsWith("over the budget") ? "over the budget" : r.Reason;
                    s_Reasons[reason] = (s_Reasons.TryGetValue(reason, out int n) ? n : 0) + 1;
                }

                int total = s_Used + s_Fallbacks;
                if (s_Lines < CutShadowStats.Detailed)
                {
                    s_Lines++;
                    line = r.Loops != null
                        ? $"Picture cut: {key} cut from the contour: {r.Loops.Count} loop(s), {r.Vertices} vertices, {r.Triangles} triangles at {r.Tolerance.ToString("0.00", ci)} px, {r.Ms.ToString("0.00", ci)} ms"
                        : $"Picture cut: {key}: the contour was not used ({r.Reason}); cut as 0.12.72 did";
                }
                else if (total == 50 || total == 250 || total == 1000)
                {
                    line = $"Picture cut: {total} board(s): {s_Used} from the contour ({s_Relaxed} relaxed above 0.5 px; mean {(s_Used > 0 ? s_Vertices / (double)s_Used : 0.0).ToString("0", ci)} vertices, {(s_Used > 0 ? s_Triangles / (double)s_Used : 0.0).ToString("0", ci)} triangles, {(s_Used > 0 ? s_Ms / s_Used : 0.0).ToString("0.00", ci)} ms, worst {s_WorstMs.ToString("0.00", ci)} ms), {s_Fallbacks} cut as 0.12.72 did, {s_NotReady} without a contour yet";
                    foreach (KeyValuePair<string, int> reason in s_Reasons)
                        line += $"; {reason.Key} x{reason.Value}";
                }
            }

            if (line != null)
                Log?.Invoke(line);
        }
    }
}
