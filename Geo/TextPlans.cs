using System;
using System.Diagnostics;
using System.Threading;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The text light boxes' plans, shared (0.12.75, DESIGN-text-lightbox.md 11): one glyph cache, one planner, and a small cache of the plans made, by the job's key. The tile worker and the editor's
    /// worker ask Get and wait for a plan they do not have (they are workers); the main thread never waits: it gets the plan if there is one, else null, and the plan is made by a LatestWorker (the
    /// newest request only), so that typing makes no queue and the main thread does no planning. Epoch goes up when a plan is made, so that what was drawn without one (the builder shows a lit
    /// bar for each line meanwhile) is built again. Safe from any thread.
    /// </summary>
    public static class TextPlans
    {
        /// <summary>The most plans kept (TwoGenCache: the ones not used for a whole generation go; twice this many at most).</summary>
        private const int kMaxPlans = 48;

        /// <summary>
        /// The most vertices the return of a text may have: one mesh holds 60,000 (Unity's 16-bit indices; ChunkBuilder.MaxVertices) and a tile's builder takes a solid whole. Over it the letters are cut
        /// coarser, the flatness doubled up to MaxCoarsening times, and drawn as bars when that is not enough.
        /// </summary>
        public const int MaxReturnVertices = 56000, MaxCoarsening = 3;

        /// <summary>The limit in use: MaxReturnVertices, lowered by the tests to reach the cases that need a text of a hundred thousand vertices.</summary>
        public static int Limit = MaxReturnVertices;

        private static readonly GlyphCache s_Glyphs = new GlyphCache(GlyphCache.DefaultBudgetBytes);
        private static readonly TextPlanner s_Planner = new TextPlanner(TextFonts.Get, s_Glyphs);
        private static readonly TwoGenCache<TextPlanJob, TextPlanResult> s_Plans = new TwoGenCache<TextPlanJob, TextPlanResult>(kMaxPlans);
        private static readonly object s_Lock = new object();
        private static readonly LatestWorker<TextPlanJob, bool> s_Worker = new LatestWorker<TextPlanJob, bool>(job =>
        {
            Make(job);
            return true;
        }, ex => Log?.Invoke("Text boxes: a plan failed: " + ex.Message));

        private static TextPlanJob s_Requested;
        private static int s_MainThread, s_Epoch;
        private static int s_Made, s_Deferred, s_Waited;
        private static double s_MakeMs, s_WorstMs;
        private static int s_Logged;

        /// <summary>Where the bounded log lines go (the mod's log); null: nowhere.</summary>
        public static Action<string> Log;

        /// <summary>The first lines a session writes about the plans made: the later ones are in the counters.</summary>
        private const int kMaxLogged = 12;

        /// <summary>Goes up whenever a plan is made: a preview that was built without one asks again when this changes.</summary>
        public static int Epoch => Volatile.Read(ref s_Epoch);

        /// <summary>A preview or an editor that was made before something it depends on changed (the list of fonts) asks again.</summary>
        public static void BumpEpoch() => Interlocked.Increment(ref s_Epoch);

        /// <summary>Records the calling thread as the main one: from now on it does not wait for a plan. Called once when the mod loads (a test that does not call it waits, as a worker does).</summary>
        public static void MarkMainThread() => s_MainThread = Environment.CurrentManagedThreadId;

        /// <summary>Forgets the main thread: for the tests, which borrow a thread for it.</summary>
        public static void ForgetMainThread() => s_MainThread = 0;

        public static bool OnMainThread => s_MainThread != 0 && Environment.CurrentManagedThreadId == s_MainThread;

        public static GlyphCache Glyphs => s_Glyphs;

        /// <summary>The plans made since the start, the requests the main thread could not have answered at once, the plans a worker waited for, the milliseconds spent making and the longest one.</summary>
        public static void Counters(out int made, out int deferred, out int waited, out double ms, out double worstMs)
        {
            lock (s_Lock)
            {
                made = s_Made;
                deferred = s_Deferred;
                waited = s_Waited;
                ms = s_MakeMs;
                worstMs = s_WorstMs;
            }
        }

        /// <summary>The plan of a job if one is kept; null otherwise.</summary>
        public static TextPlanResult Peek(TextPlanJob job)
        {
            lock (s_Lock)
                return s_Plans.TryGet(job, out TextPlanResult hit) ? hit : null;
        }

        /// <summary>
        /// The plan of a job. A worker that has none makes it (and waits); the main thread gets null and the plan is made on a worker, the newest request only. A job for a font that is not there has a
        /// plan with a warning and no letters.
        /// </summary>
        public static TextPlanResult Get(TextPlanJob job)
        {
            TextPlanResult hit = Peek(job);
            if (hit != null)
                return hit;
            if (!OnMainThread)
            {
                lock (s_Lock)
                    s_Waited++;
                return Make(job);
            }

            lock (s_Lock)
            {
                s_Deferred++;
                if (job.Equals(s_Requested))
                    return null;
                s_Requested = job;
            }

            s_Worker.Submit(job);
            return null;
        }

        private static TextPlanResult Make(TextPlanJob job)
        {
            TextPlanResult already = Peek(job);
            if (already != null)
                return already;
            long start = Stopwatch.GetTimestamp();
            TextPlanResult made;
            try
            {
                made = Fitted(job);
            }
            catch (Exception ex)
            {
                made = new TextPlanResult { Job = job, Layout = new TextLayoutResult(), Meshes = new GlyphMesh[0], Warnings = new System.Collections.Generic.List<TextWarning> { new TextWarning(TextWarningKind.FontMissing) } };
                Log?.Invoke("Text boxes: a plan failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            string line = null;
            lock (s_Lock)
            {
                if (s_Plans.TryGet(job, out TextPlanResult other))
                    return other;
                s_Plans.Set(job, made);
                s_Made++;
                s_MakeMs += ms;
                s_WorstMs = Math.Max(s_WorstMs, ms);
                if (s_Logged++ < kMaxLogged)
                {
                    int loops = 0, points = 0, triangles = 0;
                    foreach (GlyphMesh mesh in made.Meshes)
                    {
                        if (mesh == null)
                            continue;
                        loops += mesh.Loops.Count;
                        points += mesh.LoopPoints;
                        triangles += mesh.TriangleCount;
                    }

                    line = $"Text boxes: a plan of {made.Layout.Glyphs.Count} character(s) in {ms:0.0} ms: {loops} loop(s), {points} point(s), {triangles} triangle(s); "
                        + (made.Frame != null ? $"{made.Frame.Islands.Count} island(s), {made.Frame.Studs.Count} stud(s), {made.Frame.Rails.Count} rail piece(s), {made.Frame.BridgedIslands} held by rods, effective area {made.Frame.EffectiveArea:0.00} m2, about {made.Frame.MassKg:0} kg (an estimate)" : "no frame")
                        + $"; the glyph cache holds {s_Glyphs.Count} glyph(s), {s_Glyphs.Bytes / 1024} KB, {s_Glyphs.Hits} hit(s) and {s_Glyphs.Misses} miss(es)";
                }
            }

            Interlocked.Increment(ref s_Epoch);
            if (line != null)
                Log?.Invoke(line);
            return made;
        }

        /// <summary>The vertices of the mesh the letters' returns come to (each letter's back and its rim): what must stay under one mesh.</summary>
        public static int ReturnVertices(TextPlanResult plan)
        {
            int vertices = 0;
            foreach (GlyphMesh mesh in plan.Meshes)
            {
                if (mesh != null)
                    vertices += mesh.ReturnVertices;
            }

            return vertices;
        }

        /// <summary>The plan of a job, its letters cut coarser while they are over one mesh (doubling the flatness, at most MaxCoarsening times); the result is kept under the job that asked.</summary>
        private static TextPlanResult Fitted(TextPlanJob job)
        {
            TextPlanResult made = s_Planner.Plan(job);
            int steps = 0, vertices = ReturnVertices(made);
            TextPlanJob step = job;
            while (vertices > Limit && steps < MaxCoarsening && step.RelativeFlatness < TextGlyphs.MaxRelative)
            {
                step = new TextPlanJob(job.Text, job.FontId, job.LetterHeight, job.LetterSpacing, job.LineSpacing, job.Align, Math.Min(TextGlyphs.MaxRelative, step.RelativeFlatness * 2f), job.Art);
                made = s_Planner.Plan(step);
                vertices = ReturnVertices(made);
                steps++;
            }

            made.Job = job;
            made.Coarsened = steps;
            made.Vertices = vertices;
            made.TooHeavy = vertices > Limit;
            if (steps > 0 || made.TooHeavy)
                made.Warnings.Add(new TextWarning(TextWarningKind.TooDetailed, steps, metres: made.TooHeavy ? 1f : 0f));
            return made;
        }

        /// <summary>The relative flatness (a share of the em) a letter of this height is cut with: half a millimetre, within the range the glyph cache's buckets cover.</summary>
        public static float RelativeFlatness(float letterHeight) => Math.Max(TextGlyphs.MinRelative, Math.Min(TextGlyphs.MaxRelative, 0.0005f / Math.Max(0.01f, letterHeight)));

        /// <summary>Lets every plan and glyph go (a city is left, or a test wants to start from nothing).</summary>
        public static void Clear()
        {
            lock (s_Lock)
            {
                s_Plans.Clear();
                s_Requested = null;
            }

            s_Glyphs.Clear();
            Interlocked.Increment(ref s_Epoch);
        }
    }
}
