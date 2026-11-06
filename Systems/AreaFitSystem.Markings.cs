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
    /// <summary>
    /// The markings laid on the ground as it is now (0.12.32, MarkingGround): every line, barrier and thing put down with one click is laid on what is under it (StandHeights) the way the areas are fitted
    /// (a few at a time, nearest the camera first, again when a road under it changes) and read by the renderer with the item (LaidFor). How far the placed ones were off the ground is written to the
    /// log: the first ones each, and all of them once the city's markings are laid.
    /// </summary>
    internal sealed partial class AreaFitSystem
    {
        private sealed class LaidEntry
        {
            public string Key = "";
            public MarkingGround.Laid Laid;
            public float MinX, MinZ, MaxX, MaxZ;
            public bool Boxed;
            public float X, Z;

            /// <summary>Laid for Key, whatever it came to: nothing to lay is a result too, and is not asked for again until the item changes (0.12.59: it was, at every read, and every read of a marking that had none made the next).</summary>
            public bool Made;

            /// <summary>The roads read for it, and kept, took a road piece as one surface (the game was still loading it); forgotten with the kept reads (ForgetStand).</summary>
            public bool Provisional;
        }

        private const int kLaidLogged = 10, kCutLogged = 10, kKerbLogged = 10;
        private int m_CutLogged, m_CutWarned, m_KerbLogged, m_LaidHeld;

        private readonly Dictionary<Entity, LaidEntry> m_Laid = new Dictionary<Entity, LaidEntry>();
        private readonly List<Entity> m_LaidQueue = new List<Entity>();
        private readonly HashSet<Entity> m_LaidQueued = new HashSet<Entity>();
        private readonly Dictionary<uint, bool> m_Lies = new Dictionary<uint, bool>();
        private StandHeights m_Stand;
        private int m_LaidNear, m_LaidOrdered, m_LaidMade, m_LaidOff, m_LaidLogged, m_LaidAgain, m_NoLay;
        private float m_LaidWorst;
        private Entity m_LaidWorstItem;
        private bool m_LaidReported;

        /// <summary>The marking laid on the ground, for the renderer: null for an item that does not lie on the ground, or while it is not laid yet (it is then queued, and drawn as it was placed).</summary>
        internal MarkingGround.Laid LaidFor(Entity entity, in ItemRecord record)
        {
            if (!Lies(record.TypeHash))
            {
                if (m_Laid.Count > 0)
                    m_Laid.Remove(entity);
                return null;
            }

            string key = KeyOf(record);
            if (m_Laid.TryGetValue(entity, out LaidEntry entry) && entry.Key == key)
            {
                if (!entry.Made)
                    QueueLaid(entity);
                return entry.Laid;
            }

            if (entry == null)
                m_Laid[entity] = entry = new LaidEntry();
            entry.Key = key;
            entry.Laid = null;
            entry.Made = false;
            entry.X = record.Position.X;
            entry.Z = record.Position.Z;
            QueueLaid(entity);
            return null;
        }

        private bool Lies(uint hash)
        {
            if (!m_Lies.TryGetValue(hash, out bool lies))
                m_Lies[hash] = lies = MarkingGround.Lies(SignCatalog.ByHash(hash));
            return lies;
        }

        private void QueueLaid(Entity entity)
        {
            if (!m_LaidOut.IsPending(entity) && m_LaidQueued.Add(entity))
            {
                m_LaidQueue.Add(entity);
                m_LaidReported = false;
            }
        }

        private (float X, float Z)? LaidPlace(Entity entity) => m_Laid.TryGetValue(entity, out LaidEntry entry) ? (entry.X, entry.Z) : ((float, float)?)null;

        private void MakeLaid(Stopwatch clock, double budget)
        {
            // The markings whose roads are being read, a few windows a frame, go first.
            for (int i = 0; i < m_LaidContinue.Count && clock.Elapsed.TotalMilliseconds < budget && !WorkerFull;)
            {
                Entity reading = m_LaidContinue[i];
                int provisional = SurfaceReader.ProvisionalReads;
                SubmitLaid(reading);
                NoteProvisional(kLaidKind, reading, provisional);
                if (i < m_LaidContinue.Count && m_LaidContinue[i] == reading)
                    i++;
            }

            int index = 0;
            while (index < m_LaidNear && clock.Elapsed.TotalMilliseconds < budget && !WorkerFull)
            {
                Entity entity = m_LaidQueue[index++];
                m_LaidQueued.Remove(entity);
                int provisional = SurfaceReader.ProvisionalReads;
                if (Async)
                    SubmitLaid(entity);
                else
                    Lay(entity);
                NoteProvisional(kLaidKind, entity, provisional);
            }

            m_LaidQueue.RemoveRange(0, index);
            m_LaidNear -= index;
            m_LaidOrdered -= index;
            if (m_LaidQueue.Count == 0 && m_LaidMade > 0 && !m_LaidReported)
            {
                m_LaidReported = true;
                Mod.log.Info($"Marking ground: {m_LaidMade} marking(s) laid on the ground as it is; {m_LaidOff} of them had been more than {MarkingGround.Off * 100f:0} cm off it"
                    + (m_LaidOff > 0 ? $" (the worst {m_LaidWorst * 100f:+0;-0} cm, + above the ground, item {m_LaidWorstItem.Index}:{m_LaidWorstItem.Version})." : "."));
            }
        }

        private void Lay(Entity entity)
        {
            if (!m_Laid.TryGetValue(entity, out LaidEntry entry))
                return;
            if (!EntityManager.Exists(entity) || !EntityManager.TryGetComponent(entity, out TsItem item))
            {
                m_Laid.Remove(entity);
                return;
            }

            ItemRecord record = ChunkSystem.ToRecord(item);
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            m_Stand = m_Stand ?? new StandHeights(World);
            m_Stand.Observer = ShadowObserver;
            bool paint = def != null && def.Group != MarkingTemplates.BarrierGroup;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            long lap = Stopwatch.GetTimestamp();
            int provisionalBefore = SurfaceReader.ProvisionalReads;
            MarkingGround.Laid made = MarkingGround.Lay(def, record.Spec, record.Position, record.Yaw, world =>
            {
                foreach (Vec3 p in world)
                {
                    minX = Math.Min(minX, p.X);
                    minZ = Math.Min(minZ, p.Z);
                    maxX = Math.Max(maxX, p.X);
                    maxZ = Math.Max(maxZ, p.Z);
                }

                return m_Stand.Put(EntityManager, world, $"{entity.Index}:{entity.Version}|{entry.Key}", world.Count, paint, m_LaidPrefetch);
            });
            // A kerb line laid from a read that took a road piece as one surface would stand on the kerb: it keeps what it had (as placed, when nothing), and is laid when the piece is known.
            bool provisional = entry.Provisional || SurfaceReader.ProvisionalReads != provisionalBefore;
            MarkingGround.Laid laid = MarkingGround.Settle(def, made, entry.Laid, provisional);
            bool held = made != null && !ReferenceEquals(laid, made);
            if (laid != null && RunCut.Wants(def))
            {
                laid.Cut = CutFor(entity, def, record, laid);
                // The paint reaches the strip's width beside the path: a road that changes there changes the cut.
                minX -= RunCut.Reach;
                minZ -= RunCut.Reach;
                maxX += RunCut.Reach;
                maxZ += RunCut.Reach;
            }

            double ms = (Stopwatch.GetTimestamp() - lap) * 1000.0 / Stopwatch.Frequency;
            SlowLog.Add("AreaFitSystem: one marking laid", ms, SlowLog.IsSlow(ms) ? $"{def?.Id} at ({record.Position.X:0}, {record.Position.Z:0}), {laid?.Points} point(s)" : null);
            bool had = entry.Laid != null;
            if (had && !held)
                m_LaidAgain++;
            entry.Key = KeyOf(record);
            entry.Laid = laid;
            entry.Made = true;
            entry.Boxed = laid != null;
            entry.MinX = minX;
            entry.MinZ = minZ;
            entry.MaxX = maxX;
            entry.MaxZ = maxZ;
            if (held)
            {
                NoteKerbLine(entity, def, record, made, true);
            }
            else if (laid != null)
            {
                m_LaidMade++;
                if (MarkingTemplates.IsKerbLine(def.Id))
                    NoteKerbLine(entity, def, record, laid, false);
                if (Math.Abs(laid.Worst) > MarkingGround.Off)
                {
                    m_LaidOff++;
                    if (Math.Abs(laid.Worst) > Math.Abs(m_LaidWorst))
                    {
                        m_LaidWorst = laid.Worst;
                        m_LaidWorstItem = entity;
                    }

                    if (m_LaidLogged++ < kLaidLogged)
                        Mod.log.Info($"Marking ground: item {entity.Index}:{entity.Version} ({def?.Id}) at ({record.Position.X:0.0}, {record.Position.Y:0.00}, {record.Position.Z:0.0}) had been {laid.Worst * 100f:+0;-0} cm off the ground at its worst of {laid.Points} point(s); laid on it.");
                }
            }
            else if (m_NoLay++ < kNoneLogged)
            {
                Mod.log.Info($"Marking ground: item {entity.Index}:{entity.Version} ({def?.Id}, {record.Spec?.Length ?? 0} characters of settings) has nothing to lay.");
            }

            // The renderer has the item without it already: it is read again for a marking that is laid, or was and is not.
            if (!held && (laid != null || had))
                m_Ready.Add(entity);
        }

        /// <summary>
        /// A kerb line laid at the foot of its kerb (made) or kept as it was because the road piece in front of its face is still being loaded by the game (held): the first of each are written, the first
        /// of the held ones as a warning (a piece that the game does not load keeps them so until the camera brings one to it).
        /// </summary>
        private void NoteKerbLine(Entity entity, SignDef def, in ItemRecord record, MarkingGround.Laid laid, bool held)
        {
            if (held)
                m_LaidHeld++;
            if (m_KerbLogged++ >= kKerbLogged && !(held && m_LaidHeld == 1))
                return;
            string side = def.Template.Parse(record.Spec).Is("side", "r") ? "r" : "l";
            if (held)
            {
                string text = $"Marking ground: kerb line item {entity.Index}:{entity.Version} ({def.Id}, side {side}) kept at the heights it was placed with: a road piece it reads is still being loaded by the game ({SurfaceReader.KerbSummary()}); it is laid when the piece is known.";
                if (m_LaidHeld == 1)
                    Mod.log.Warn(text);
                else
                    Mod.log.Info(text);
                return;
            }

            MarkingGround.PathHeights(def.Template, laid.Spec, record.Position.Y + laid.Dy, out float low, out float high);
            Mod.log.Info($"Marking ground: kerb line item {entity.Index}:{entity.Version} ({def.Id}) laid at the foot of its kerb (side {side}, {laid.Points} point(s), heights {low:0.00} to {high:0.00} m, the road pieces in front of it known).");
        }

        /// <summary>The kept reads of the roads are forgotten: no marking's heights are from a provisional read any more (the next lay reads again, and says so if it is still provisional).</summary>
        private void ForgetStand()
        {
            m_Stand?.Clear();
            foreach (LaidEntry entry in m_Laid.Values)
                entry.Provisional = false;
        }

        /// <summary>
        /// The road under the paint of a wide marking, when some of the paint lies over none at its height (RunCut, 0.12.83): the roads in the box of the paint are read (as for the heights, on the main
        /// thread) and cut for heights, and the fit goes to the tile builders (Laid.Cut) only then; a marking whose paint lies on the road is drawn as it was laid. Null too when no road was read in the box:
        /// a marking far from every road is not the road's to cut.
        /// </summary>
        private AreaFit CutFor(Entity entity, SignDef def, in ItemRecord record, MarkingGround.Laid laid)
        {
            long lap = Stopwatch.GetTimestamp();
            MarkingPlan plan = MarkingTemplates.PlanOf(def, laid.Spec);
            if (plan == null || plan.Parts.Count == 0)
                return null;
            var origin = new Vec3(record.Position.X, record.Position.Y + laid.Dy, record.Position.Z);
            RunCut.WorldBox(plan, origin, record.Yaw, out float minX, out float minZ, out float maxX, out float maxZ);
            List<SurfaceStrip> strips;
            try
            {
                strips = m_Stand.StripsOf(EntityManager, new SurfaceHeights.WindowRead(minX, minZ, maxX, maxZ, origin.Y), true);
            }
            catch (Exception ex)
            {
                if (m_CutWarned++ < 5)
                    Mod.log.Warn("The road's surface could not be read for the cut of a marking; it is drawn as laid: " + ex.Message);
                return null;
            }

            if (strips.Count == 0)
                return null;
            FitResult cut = FitCut.Run(new FitJob { Heights = true, Strips = strips, Origin = origin, Yaw = record.Yaw, MinX = plan.MinX, MaxX = plan.MaxX, MinZ = plan.MinZ, MaxZ = plan.MaxZ, Margin = RunCut.ReadMargin });
            SurfaceReader.NoteFitMs(true, cut.CutMs);
            AreaFit needed = RunCut.Needed(plan, cut.Fit, out float share);
            SlowLog.Since("AreaFitSystem: a marking's cut to the road", lap);
            if (needed != null && m_CutLogged++ < kCutLogged)
            {
                Mod.log.Info($"Marking ground: item {entity.Index}:{entity.Version} ({def.Id}) at ({record.Position.X:0.0}, {record.Position.Y:0.00}, {record.Position.Z:0.0}): {share * 100f:0.0} % of its paint lay over no road at its height "
                    + $"(beyond the road's edge, or over the ground or the water): cut to the road ({strips.Count} band(s) read).");
            }

            return needed;
        }

        /// <summary>The markings over a road segment or junction that was changed or removed are laid again; what was read of the roads is forgotten.</summary>
        private void LaidRoadsChanged(List<(float, float, float, float)> boxes)
        {
            if (m_Laid.Count == 0)
                return;
            ForgetStand();
            foreach (KeyValuePair<Entity, LaidEntry> pair in m_Laid)
            {
                LaidEntry entry = pair.Value;
                if (!entry.Boxed)
                    continue;
                foreach ((float x0, float z0, float x1, float z1) in boxes)
                {
                    if (x1 >= entry.MinX - 2f && x0 <= entry.MaxX + 2f && z1 >= entry.MinZ - 2f && z0 <= entry.MaxZ + 2f)
                    {
                        QueueLaid(pair.Key);
                        break;
                    }
                }
            }
        }

        private void ClearLaid()
        {
            m_Laid.Clear();
            m_LaidQueue.Clear();
            m_LaidQueued.Clear();
            m_LaidReading.Clear();
            m_LaidContinue.Clear();
            m_Stand?.Clear();
            m_LaidNear = m_LaidOrdered = m_LaidMade = m_LaidOff = m_LaidLogged = m_LaidAgain = m_NoLay = m_LaidHeld = m_KerbLogged = 0;
            m_LaidWorst = 0f;
            m_LaidWorstItem = Entity.Null;
            m_LaidReported = false;
        }

        private string DescribeLaid() => $"markings on the ground: {m_Laid.Count} marking(s), {m_LaidQueue.Count} waiting ({m_LaidNear} near the camera), {m_LaidMade} laid ({m_LaidAgain} laid again after a road changed), {m_LaidOff} had been off it, {m_LaidHeld} kerb line(s) kept as placed while a road piece is loading";
    }
}
