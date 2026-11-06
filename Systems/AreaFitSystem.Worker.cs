using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>Where the cutting of the roads' surface is done: Off on the main thread as before; Shadow too, and each cut (the first 200) is made on a worker as well and compared to the bit; On on a worker.</summary>
    internal enum FitWorkerMode
    {
        Off,
        Shadow,
        On
    }

    /// <summary>
    /// The cutting of the roads' surface for the area fits, the posts' grounds and the laid markings made off the main thread (0.12.74; the owner's log of 0.12.73: two frames of 88 and 100 ms were area fits,
    /// the 121 height fits took 143 ms). The main thread reads what the cut needs from the game (the roads: SurfaceReader, the item) and gives it to a FitWorker as a job of read-only strips (Geo/FitCut.cs);
    /// the cut is made there by the function the main thread used, and taken here a frame or more later (a few tenths of a millisecond a frame) and finished where it always was (the area's own judgement and
    /// the terrain, the posts' profile, the laid line's heights): the renderer reads the item again as it did. A cut is not used when a road changed in its box (ledger) or the item is not what it was
    /// when it was read: the item is asked for again. Until it is made the item is drawn as it was while it waited in the queue.
    /// </summary>
    internal sealed partial class AreaFitSystem
    {
        /// <summary>Where the cuts are made. Settable at any time (a request already given is taken whatever the mode is now).</summary>
        internal static FitWorkerMode WorkerMode { get; set; } = FitWorkerMode.On;

        /// <summary>The most a frame spends taking cuts (ms; the first is always taken, so that a slow finish cannot stop them).</summary>
        private const double kTakeMs = 0.25;

        /// <summary>The most items out at the worker or made and not yet taken (each holds its strips and fits).</summary>
        private const int kMaxOutstanding = 48;

        /// <summary>The first cuts made by the worker are also made here and compared (up to this many), as long as they are cheap: a cut that took a few tenths of a millisecond.</summary>
        private const int kChecks = 100;

        /// <summary>A cut with at most this many cells of bands is checked on the main thread (about 0.3 ms): for heights, and for the pieces that cut paint.</summary>
        private const int kCheckHeightCells = 800, kCheckPieceCells = 60;

        /// <summary>In Shadow, how many cuts are compared.</summary>
        private const int kShadowJobs = 200;

        private const int kMaxFailures = 3;

        /// <summary>A marking's roads are read a few windows at a time: a slice goes on while it has used less than this (ms), and always reads one window.</summary>
        private const double kReadSliceMs = 0.3;

        private FitWorker m_Worker = new FitWorker();
        private readonly FitLedger<Entity> m_AreaOut = new FitLedger<Entity>(), m_LaidOut = new FitLedger<Entity>();
        private readonly FitLedger<(float, float, float, float, uint, float)> m_GroundOut = new FitLedger<(float, float, float, float, uint, float)>();
        private readonly HashSet<Entity> m_GroundWaiting = new HashSet<Entity>();
        private readonly Dictionary<Entity, LaidRead> m_LaidReading = new Dictionary<Entity, LaidRead>();
        private readonly List<Entity> m_LaidContinue = new List<Entity>();
        private readonly Queue<FitDone> m_ShadowDone = new Queue<FitDone>();
        private Dictionary<SurfaceHeights.WindowRead, AreaFit> m_LaidPrefetch;
        private Action<FitJob, FitResult> m_ShadowOf;
        private bool m_WorkerBroken;
        private int m_Discarded, m_WorkerFailures, m_Checked, m_CheckDiffs, m_ShadowSent, m_ShadowCompared, m_ShadowDiffs, m_LaidSlices, m_LaidSliced, m_LaidReadWarned;
        private double m_SubmitMs, m_TakeMs, m_WorstTakeMs;
        private int m_Takes;

        private bool Async => WorkerMode == FitWorkerMode.On && !m_WorkerBroken;

        private bool WorkerFull => Async && m_AreaOut.Count + m_GroundOut.Count + m_LaidOut.Count >= kMaxOutstanding;

        /// <summary>Items whose cut is out or made and not yet taken: they wait as they did in the queue (the renderer holds the first build for them, the batches of ready items wait for them).</summary>
        /// <summary>What is still to be fitted near the camera and what is out at the worker, for the load's hold on the loading screen.</summary>
        internal int FirstUseWaiting => NearWaiting + Outstanding;

        private int Outstanding => m_AreaOut.Count + m_GroundWaiting.Count + m_LaidOut.Count;

        private Action<FitJob, FitResult> ShadowObserver => WorkerMode == FitWorkerMode.Shadow ? m_ShadowOf ?? (m_ShadowOf = ShadowOf) : null;

        private void ResetWorker()
        {
            m_Worker.Clear();
            m_Worker = new FitWorker();
            m_AreaOut.Clear();
            m_GroundOut.Clear();
            m_LaidOut.Clear();
            m_GroundWaiting.Clear();
            m_ShadowDone.Clear();
            m_LaidReading.Clear();
            m_LaidContinue.Clear();
            m_TerrainGate.Reset();
            m_Discarded = m_WorkerFailures = m_ShadowSent = m_ShadowCompared = m_ShadowDiffs = m_LaidSlices = m_LaidSliced = 0;
            m_SubmitMs = m_TakeMs = m_WorstTakeMs = 0.0;
            m_Takes = m_TakesBefore = 0;
            m_TakeMsBefore = 0.0;
        }

        private int m_PassFull, m_PassBudget, m_PassDry, m_TakesBefore;
        private double m_TakeMsBefore;

        /// <summary>The load's hold begins: the pass counts and the take count start from here.</summary>
        internal void BeginHoldCounts()
        {
            m_PassFull = m_PassBudget = m_PassDry = 0;
            m_TakesBefore = m_Takes;
            m_TakeMsBefore = m_TakeMs;
        }

        /// <summary>What ended the fits' passes during the hold (the worker full of requests that were not taken, the budget, nothing left to submit) and what the takes cost: says what paced the drain.</summary>
        internal string HoldCounts() =>
            $"fit passes ended by the full worker {m_PassFull}, the budget {m_PassBudget}, nothing left to submit {m_PassDry}; {m_Takes - m_TakesBefore} take(s) in {m_TakeMs - m_TakeMsBefore:0} ms";

        /// <summary>After a failure of the system: what is out is forgotten, and the items are asked for again when the renderer reads them.</summary>
        private void DropOutstanding()
        {
            m_Worker.Clear();
            m_AreaOut.Clear();
            m_GroundOut.Clear();
            m_LaidOut.Clear();
            m_GroundWaiting.Clear();
            m_ShadowDone.Clear();
            m_LaidReading.Clear();
            m_LaidContinue.Clear();
        }

        /// <summary>The system threw while the worker was on: after a few of these the cutting goes back to the main thread, where it never threw.</summary>
        private void AsyncFailed()
        {
            if (WorkerMode != FitWorkerMode.On || m_WorkerBroken || ++m_WorkerFailures < kMaxFailures)
                return;
            m_WorkerBroken = true;
            Mod.log.Error("The area fits failed " + kMaxFailures + " times with the fit worker on; the roads' surface is cut on the main thread from now on.");
        }

        /// <summary>Roads changed in these boxes: what is out for items there was read from the roads as they were.</summary>
        private void StaleOutstanding(List<(float, float, float, float)> boxes)
        {
            m_AreaOut.MarkStale(boxes, 2f);
            m_GroundOut.MarkStale(boxes, 2f);
            m_LaidOut.MarkStale(boxes, 2f);
        }

        internal string DescribeWorker()
        {
            (long submitted, long taken, long failed, double workerMs, double worstMs, int most, _) = m_Worker.Counters;
            return $"fit worker: {WorkerMode}{(m_WorkerBroken ? " (switched off: a cut differed or failed)" : "")}, {submitted} cut(s) given, {taken} taken, {m_Discarded} request(s) not used (a road or the item changed), {failed} failed, "
                + $"{m_Stand?.Missed ?? 0} read(s) not made beforehand, on the worker {workerMs:0} ms (the longest {worstMs:0.0}), the main thread {m_SubmitMs:0} ms to read for it and {m_TakeMs:0} ms to take (the longest {m_WorstTakeMs:0.00}), "
                + $"{most} most in flight, {m_Checked} checked on the main thread ({m_CheckDiffs} differ); shadow: {m_ShadowSent} sent, {m_ShadowCompared} compared, {m_ShadowDiffs} differ; "
                + $"markings read in {m_LaidSlices} slice(s), {m_LaidSliced} of them over more than one; terrain: the finishing was held {m_TerrainGate.Held} frame(s) (the longest run {m_TerrainGate.Longest}), let through to block {m_TerrainGate.Forced} time(s)";
        }

        // ------------------------------------------------------------------ taking

        /// <summary>
        /// Takes what the worker made and finishes the items whose cuts are all made, a few tenths of a millisecond of a frame. Not finishing (finish false: the terrain is not ready for what a finish reads)
        /// still hands the worker's results to their requests, so that they do not pile up there.
        /// </summary>
        private void TakeFinished(bool finish = true)
        {
            if (m_Worker.InFlight == 0 && Outstanding == 0 && m_ShadowDone.Count == 0)
                return;
            long begin = Stopwatch.GetTimestamp();
            while (m_Worker.TryTake(out FitDone done))
            {
                if (!m_AreaOut.Accept(done) && !m_GroundOut.Accept(done) && !m_LaidOut.Accept(done))
                    m_ShadowDone.Enqueue(done);
            }

            if (!finish)
                return;
            int taken = 0;
            double takeMs = LoadBudget.Take(kTakeMs);
            while ((taken == 0 || Elapsed(begin) < takeMs) && TakeOne())
                taken++;
            double ms = Elapsed(begin);
            m_TakeMs += ms;
            m_Takes += taken;
            if (taken > 0 && ms > m_WorstTakeMs)
                m_WorstTakeMs = ms;
        }

        private static double Elapsed(long begin) => (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;

        private bool TakeOne()
        {
            if (m_AreaOut.TryTakeComplete(out FitLedger<Entity>.Request area))
                TakeArea(area);
            else if (m_GroundOut.TryTakeComplete(out FitLedger<(float, float, float, float, uint, float)>.Request ground))
                TakeGround(ground);
            else if (m_LaidOut.TryTakeComplete(out FitLedger<Entity>.Request laid))
            {
                // Lay reads again a window that was not among the fits given to it.
                int provisional = SurfaceReader.ProvisionalReads;
                TakeLaid(laid);
                NoteProvisional(kLaidKind, laid.Owner, provisional);
            }
            else if (m_ShadowDone.Count > 0)
                CompareShadow(m_ShadowDone.Dequeue());
            else
                return false;
            return true;
        }

        /// <summary>The request is not used: the item is asked for again (when a worker keeps failing, the cutting goes back to the main thread for good).</summary>
        private void NotUsed(FitDone? failed)
        {
            m_Discarded++;
            if (failed.HasValue && failed.Value.Error != null && ++m_WorkerFailures >= kMaxFailures && !m_WorkerBroken)
            {
                m_WorkerBroken = true;
                Mod.log.Error("The fit worker failed " + kMaxFailures + " times; the roads' surface is cut on the main thread from now on: " + failed.Value.Error);
            }
        }

        /// <summary>
        /// A cut that is cheap enough is made on the main thread too, for the first few of them, and the two are compared to the bit: the worker is only trusted while they agree. If they do not, the worker is
        /// off from then on and the main thread's cut is the one used.
        /// </summary>
        private FitResult Checked(FitJob job, FitResult result)
        {
            if (m_Checked >= kChecks || job.Cells > (job.Heights ? kCheckHeightCells : kCheckPieceCells))
                return result;
            m_Checked++;
            FitResult again = FitCut.Run(job);
            string why = FitCut.Differ(again, result);
            if (why == null)
                return result;
            m_CheckDiffs++;
            m_WorkerBroken = true;
            Mod.log.Error("A cut made by the fit worker differs from the main thread's (" + why + "); the roads' surface is cut on the main thread from now on.");
            return again;
        }

        // ------------------------------------------------------------------ shadow

        private void ShadowOf(FitJob job, FitResult result)
        {
            if (m_ShadowSent >= kShadowJobs)
                return;
            m_ShadowSent++;
            m_Worker.Submit(new FitJob { Heights = job.Heights, Strips = job.Strips, Origin = job.Origin, Yaw = job.Yaw, MinX = job.MinX, MaxX = job.MaxX, MinZ = job.MinZ, MaxZ = job.MaxZ, Margin = job.Margin, Rule = job.Rule, Reference = result });
        }

        private void CompareShadow(FitDone done)
        {
            m_ShadowCompared++;
            string why = done.Error != null ? "the cut threw: " + done.Error.Message : done.Job.Reference.HasValue ? FitCut.Differ(done.Job.Reference.Value, done.Result) : null;
            if (why == null)
                return;
            if (m_ShadowDiffs++ < 5)
                Mod.log.Error("A cut made by the fit worker differs from the main thread's (" + why + ").");
        }

        // ------------------------------------------------------------------ areas

        private sealed class GroundState
        {
            public GroundRead Read;
            public List<Entity> Entities;
            public Vec3 Position;
            public float Yaw;
        }

        /// <summary>The first half of Make: the roads under the area are read here and the cut is given to the worker.</summary>
        private void SubmitArea(Entity entity)
        {
            if (!m_Entries.TryGetValue(entity, out Entry entry))
                return;
            if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
            {
                m_Entries.Remove(entity);
                return;
            }

            long lap = Stopwatch.GetTimestamp();
            ItemRecord record = ChunkSystem.ToRecord(item);
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            AreaRead area = ReadArea(EntityManager, m_NetSearch, m_Prefabs, def, record.Spec, record.Position, record.Yaw);
            if (area == null || area.Read.Strips.Count == 0)
            {
                // Nothing to cut (no paint, or no road under it): the ground's heights, here, as Make does.
                AreaFit fit = area == null ? null : FinishArea(EntityManager, m_NetSearch, m_Prefabs, m_Terrain, area, default, record.Position, record.Yaw);
                double ms = Elapsed(lap);
                SlowLog.Add("AreaFitSystem: one area's fit", ms, SlowLog.IsSlow(ms) ? $"{def?.Id} at ({record.Position.X:0}, {record.Position.Z:0}), {fit?.Note}" : null);
                CompleteArea(entity, entry, record, def, fit, area?.MinX ?? 0f, area?.MinZ ?? 0f, area?.MaxX ?? 0f, area?.MaxZ ?? 0f);
                return;
            }

            FitLedger<Entity>.Request request = m_AreaOut.Begin(entity, KeyOf(record), area.MinX, area.MinZ, area.MaxX, area.MaxZ);
            request.State = area;
            m_AreaOut.Add(request, m_Worker, AreaJob(area, def, record.Spec, record.Position, record.Yaw));
            Gave(request, lap, "an area", def?.Id);
        }

        private void Gave(FitLedger<Entity>.Request request, long lap, string what, string id)
        {
            request.MainMs = Elapsed(lap);
            m_SubmitMs += request.MainMs;
            SlowLog.Add("AreaFitSystem: reading for the worker", request.MainMs, SlowLog.IsSlow(request.MainMs) ? $"{what} {id}" : null);
        }

        private void TakeArea(FitLedger<Entity>.Request request)
        {
            long lap = Stopwatch.GetTimestamp();
            Entity entity = request.Owner;
            if (!m_Entries.TryGetValue(entity, out Entry entry))
                return;
            if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
            {
                m_Entries.Remove(entity);
                return;
            }

            ItemRecord record = ChunkSystem.ToRecord(item);
            if (!request.UsableFor(KeyOf(record)))
            {
                NotUsed(request.Failure);
                Queue(entity);
                return;
            }

            FitDone done = request.Results[default];
            FitResult cut = Checked(done.Job, done.Result);
            SurfaceReader.NoteFitMs(false, cut.CutMs);
            var area = (AreaRead)request.State;
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            AreaFit fit = FinishArea(EntityManager, m_NetSearch, m_Prefabs, m_Terrain, area, cut, record.Position, record.Yaw);
            double ms = Elapsed(lap);
            SlowLog.Add("AreaFitSystem: one area's fit", ms, SlowLog.IsSlow(ms) ? $"{def?.Id} at ({record.Position.X:0}, {record.Position.Z:0}), {fit?.Note}; on the worker {request.WorkerMs:0.0} ms" : null);
            CompleteArea(entity, entry, record, def, fit, area.MinX, area.MinZ, area.MaxX, area.MaxZ);
        }

        // ------------------------------------------------------------------ post grounds

        /// <summary>The first half of MakeGround: the roads along the posts are read here and the cut is given to the worker; a sign whose spot is being made waits for it (the boards of one support share a profile).</summary>
        private void SubmitGround(Entity entity)
        {
            if (!m_Grounds.TryGetValue(entity, out GroundEntry entry))
                return;
            if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
            {
                m_Grounds.Remove(entity);
                return;
            }

            long lap = Stopwatch.GetTimestamp();
            ItemRecord record = ChunkSystem.ToRecord(item);
            var key = GroundKeyOf(record);
            if (m_SameSpot.TryGetValue(key, out GroundProfile known) && known != null)
            {
                CompleteGround(entity, entry, record, key, known);
                return;
            }

            FitLedger<(float, float, float, float, uint, float)>.Request pending = m_GroundOut.Of(key);
            if (pending != null)
            {
                ((GroundState)pending.State).Entities.Add(entity);
                m_GroundWaiting.Add(entity);
                return;
            }

            GroundRead ground = ReadGround(EntityManager, m_NetSearch, m_Prefabs, record.Position, record.Yaw, record.Reach);
            if (ground.Read.Strips.Count == 0)
            {
                SurfaceReader.NoteFitMs(true, 0.0);
                GroundProfile profile = FinishGround(m_Terrain, ground, null, record.Position, record.Yaw, out _);
                double ms = Elapsed(lap);
                SlowLog.Add("AreaFitSystem: one post ground", ms, SlowLog.IsSlow(ms) ? $"at ({record.Position.X:0}, {record.Position.Z:0}), {profile.Note}" : null);
                RememberGround(entity, record, key, profile);
                CompleteGround(entity, entry, record, key, profile);
                return;
            }

            FitLedger<(float, float, float, float, uint, float)>.Request request = m_GroundOut.Begin(key, "", ground.MinX, ground.MinZ, ground.MaxX, ground.MaxZ);
            request.State = new GroundState { Read = ground, Entities = new List<Entity> { entity }, Position = record.Position, Yaw = record.Yaw };
            m_GroundWaiting.Add(entity);
            m_GroundOut.Add(request, m_Worker, GroundJob(ground, record.Position, record.Yaw));
            request.MainMs = Elapsed(lap);
            m_SubmitMs += request.MainMs;
            SlowLog.Add("AreaFitSystem: reading for the worker", request.MainMs, SlowLog.IsSlow(request.MainMs) ? $"a post ground at ({record.Position.X:0}, {record.Position.Z:0})" : null);
        }

        private void TakeGround(FitLedger<(float, float, float, float, uint, float)>.Request request)
        {
            long lap = Stopwatch.GetTimestamp();
            var state = (GroundState)request.State;
            foreach (Entity entity in state.Entities)
                m_GroundWaiting.Remove(entity);
            if (request.Stale || request.Failure != null)
            {
                NotUsed(request.Failure);
                foreach (Entity entity in state.Entities)
                    QueueGround(entity);
                return;
            }

            FitDone made = request.Results[default];
            FitResult cut = Checked(made.Job, made.Result);
            SurfaceReader.NoteFitMs(true, cut.CutMs);
            GroundProfile profile = FinishGround(m_Terrain, state.Read, cut.Fit, state.Position, state.Yaw, out _);
            bool remembered = false;
            foreach (Entity entity in state.Entities)
            {
                if (!m_Grounds.TryGetValue(entity, out GroundEntry entry))
                    continue;
                if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
                {
                    m_Grounds.Remove(entity);
                    continue;
                }

                ItemRecord record = ChunkSystem.ToRecord(item);
                var key = GroundKeyOf(record);
                if (!key.Equals(request.Owner))
                {
                    m_Discarded++;
                    QueueGround(entity);
                    continue;
                }

                if (!remembered)
                {
                    RememberGround(entity, record, key, profile);
                    remembered = true;
                }

                CompleteGround(entity, entry, record, key, profile);
            }

            double ms = Elapsed(lap);
            SlowLog.Add("AreaFitSystem: one post ground", ms, SlowLog.IsSlow(ms) ? $"{profile.Note}; on the worker {request.WorkerMs:0.0} ms" : null);
        }

        // ------------------------------------------------------------------ laid markings

        /// <summary>A marking whose roads are being read, a few windows a frame: what it was read for and what is read so far.</summary>
        private sealed class LaidRead
        {
            public string Key, Id;
            public bool Paint;
            public FitLedger<Entity>.Request Request;
            public SlicedRead<SurfaceHeights.WindowRead, List<SurfaceStrip>> Windows;
            public readonly List<SurfaceHeights.WindowRead> None = new List<SurfaceHeights.WindowRead>();
            public int Given, Slices;
            public double MainMs;
        }

        /// <summary>The marking is not being read any more (it is gone, changed, or done): what was out for it is forgotten.</summary>
        private void DropLaidRead(Entity entity)
        {
            if (m_LaidReading.Remove(entity))
                m_LaidOut.Forget(entity);
            m_LaidContinue.Remove(entity);
        }

        /// <summary>
        /// The first half of Lay: the line the marking is laid along is found (Lay run with a callback that only takes the line), the roads under each window of it are read here, a few windows a frame
        /// (0.12.75: a long line read in one go took up to 38 ms), and the cuts are given to the worker as they are read. The request is made at the first slice, so that a road that changes under the
        /// marking while it is being read makes it stale as it would one that is read. The second half is Lay itself, with those cuts in place of its own reads.
        /// </summary>
        private void SubmitLaid(Entity entity)
        {
            if (!m_Laid.TryGetValue(entity, out LaidEntry entry))
            {
                DropLaidRead(entity);
                return;
            }

            if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
            {
                m_Laid.Remove(entity);
                DropLaidRead(entity);
                return;
            }

            long lap = Stopwatch.GetTimestamp();
            ItemRecord record = ChunkSystem.ToRecord(item);
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            m_Stand = m_Stand ?? new StandHeights(World);
            if (!m_LaidReading.TryGetValue(entity, out LaidRead state))
            {
                state = BeginLaidRead(entity, entry, record, def, lap);
                if (state == null)
                    return;
            }
            else if (state.Key != entry.Key)
            {
                // The item changed while its roads were being read: what was read is not its.
                DropLaidRead(entity);
                QueueLaid(entity);
                return;
            }

            ReadLaidWindows(entity, state, lap);
        }

        /// <summary>The line of the marking and the windows of it that are to be read; null when there is nothing to read (the marking is made as before, or has nothing to cut).</summary>
        private LaidRead BeginLaidRead(Entity entity, LaidEntry entry, ItemRecord record, SignDef def, long lap)
        {
            List<Vec3> line = null;
            int asked = 0;
            MarkingGround.Lay(def, record.Spec, record.Position, record.Yaw, world =>
            {
                asked++;
                line = world;
                return world;
            });
            SlowLog.Since("AreaFitSystem: a marking's line", lap);
            if (asked != 1 || line.Count == 0)
            {
                // Nothing laid along a line, or not as usual: made as before.
                Lay(entity);
                return null;
            }

            bool paint = def != null && def.Group != MarkingTemplates.BarrierGroup;
            long lapWindows = Stopwatch.GetTimestamp();
            List<SurfaceHeights.WindowRead> windows = m_Stand.Windows(line, $"{entity.Index}:{entity.Version}|{entry.Key}", line.Count, paint);
            SlowLog.Since("AreaFitSystem: a marking's windows", lapWindows);
            if (windows.Count == 0)
            {
                // Every window of it is kept already: nothing to cut.
                Lay(entity);
                return null;
            }

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (SurfaceHeights.WindowRead window in windows)
            {
                minX = Math.Min(minX, window.MinX);
                minZ = Math.Min(minZ, window.MinZ);
                maxX = Math.Max(maxX, window.MaxX);
                maxZ = Math.Max(maxZ, window.MaxZ);
            }

            var state = new LaidRead
            {
                Key = entry.Key,
                Id = def?.Id,
                Paint = paint,
                Request = m_LaidOut.Begin(entity, entry.Key, minX, minZ, maxX, maxZ, true),
                Windows = new SlicedRead<SurfaceHeights.WindowRead, List<SurfaceStrip>>(windows)
            };
            m_LaidReading[entity] = state;
            return state;
        }

        /// <summary>One slice of the roads' reading for a marking, and the end of it when all its windows are read (or a road changed under it: what is read is then of no use, and it is asked for again).</summary>
        private void ReadLaidWindows(Entity entity, LaidRead state, long lap)
        {
            FitLedger<Entity>.Request request = state.Request;
            int before = state.Windows.Done.Count;
            if (!request.Stale)
                state.Windows.Slice(window => m_Stand.StripsOf(EntityManager, window, state.Paint), () => Elapsed(lap), LoadBudget.Scaled(kReadSliceMs));
            for (int i = before; i < state.Windows.Done.Count; i++)
            {
                (SurfaceHeights.WindowRead window, List<SurfaceStrip> strips) = state.Windows.Done[i];
                if (strips.Count == 0)
                {
                    state.None.Add(window);
                    continue;
                }

                m_LaidOut.Add(request, m_Worker, new FitJob { Heights = true, Strips = strips, Origin = new Vec3(0f, window.OriginY, 0f), MinX = window.MinX, MaxX = window.MaxX, MinZ = window.MinZ, MaxZ = window.MaxZ }, window);
            }

            if (state.Windows.Failed > 0 && m_LaidReadWarned++ < 5)
                Mod.log.Warn("The road's surface could not be read; the line keeps its heights: " + state.Windows.FirstFailure?.Message);
            double ms = Elapsed(lap);
            state.MainMs += ms;
            state.Slices++;
            m_LaidSlices++;
            m_SubmitMs += ms;
            SlowLog.Add("AreaFitSystem: reading for the worker", ms, SlowLog.IsSlow(ms) ? $"a marking {state.Id}, window {state.Windows.Next} of {state.Windows.Count}" : null);
            if (!state.Windows.Finished && !request.Stale)
            {
                if (!m_LaidContinue.Contains(entity))
                    m_LaidContinue.Add(entity);
                return;
            }

            if (state.Slices > 1)
                m_LaidSliced++;
            request.MainMs = state.MainMs;
            request.State = state.None;
            m_LaidReading.Remove(entity);
            m_LaidContinue.Remove(entity);
            m_LaidOut.Seal(request);
        }

        private void TakeLaid(FitLedger<Entity>.Request request)
        {
            Entity entity = request.Owner;
            if (!m_Laid.TryGetValue(entity, out LaidEntry entry))
                return;
            if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
            {
                m_Laid.Remove(entity);
                return;
            }

            if (!request.UsableFor(entry.Key) || KeyOf(ChunkSystem.ToRecord(item)) != request.Key)
            {
                NotUsed(request.Failure);
                QueueLaid(entity);
                return;
            }

            var fits = new Dictionary<SurfaceHeights.WindowRead, AreaFit>(request.Results.Count + 4);
            foreach (KeyValuePair<SurfaceHeights.WindowRead, FitDone> pair in request.Results)
            {
                FitResult cut = Checked(pair.Value.Job, pair.Value.Result);
                SurfaceReader.NoteFitMs(true, cut.CutMs);
                fits[pair.Key] = cut.Fit;
            }

            foreach (SurfaceHeights.WindowRead window in (List<SurfaceHeights.WindowRead>)request.State)
                fits[window] = null;
            m_LaidPrefetch = fits;
            try
            {
                Lay(entity);
            }
            finally
            {
                m_LaidPrefetch = null;
            }
        }
    }
}
