using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Game.Common;
using Game.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// Road follow (0.12.74 stage 1, 0.12.75 stages 2 and 3; DESIGN-follow-road.md, DESIGN-wiring-plan.md step C): when the game updates a road (Move It writes the new Curve in place and adds Updated; the game
    /// does it for lanes too) the items that stand by it are followed. The relation of an item to its road is measured when ItemRoadSystem finds the item beside the road (or links it to another) and is kept
    /// for the session. The preferences' roadFollow says off (no work at all), log (what each item would do is worked out by FollowWatch and counted in the log, and NOTHING is written) or on
    /// (the default since 0.12.82: the items are moved: every frame within 0.5 ms, the nearest the camera first, a tile's items in bursts with a gate between them so that the renderer's builds are not dropped, no undo step). A sign on
    /// a wall and a sign on the game's lamp post or signal pole (lamp ads on a post too) follow their host instead of a road (ItemRoadSystem.Hosts.cs, on only); a sign on another mod's pole or arm is
    /// not followed; the items of an automatic marking follow their road as any item does.
    /// </summary>
    internal sealed partial class ItemRoadSystem
    {
        /// <summary>At most this many items are looked at in a frame, and the frame's work stops starting new ones after this much of its time (ms).</summary>
        private const int kFollowItemsPerFrame = 200;

        private const double kFollowBudgetMs = 0.3;

        /// <summary>The time a frame of a writing follow may take (ms), kept by pacing the scheduler's cost budget with what the frames really took.</summary>
        private const double kFollowWriteBudgetMs = 0.5;

        /// <summary>The frames after a road's update in which the round that looks for an item's road leaves the item alone: its follow is behind the road by the gate.</summary>
        private const int kFollowHoldFrames = 120;

        /// <summary>The first reports are written in full; the rest only count, in the totals line.</summary>
        private const int kFollowDetailReports = 30;

        private const int kFollowWarnings = 20;
        private const float kFollowTotalsSeconds = 180f;

        /// <summary>The most roads a line is linked to (a chain of 64 m crosses a few).</summary>
        private const int kLineRoads = 8;

        private EntityQuery m_UpdatedEdges;
        private readonly FollowWatch<Entity, Entity> m_Follow = new FollowWatch<Entity, Entity>();
        private readonly FollowCache m_FollowCache = new FollowCache();
        private readonly HashSet<Entity> m_FollowSkipped = new HashSet<Entity>();
        private readonly List<Entity> m_LineEdges = new List<Entity>();
        private readonly List<FollowCurve> m_LineTables = new List<FollowCurve>();
        private Func<Entity, FollowCurve> m_TableOf;
        private FollowWatch<Entity, Entity>.PlaceOf m_PlaceOf;
        private Func<bool> m_FollowOutOfTime;
        private bool m_FollowActive, m_FollowWrites, m_FollowOverrunLogged;
        private int m_FollowFrame, m_FollowGate, m_FollowSkippedPath;
        private double m_FollowScale = 1.0, m_FollowFrameMs;

        /// <summary>The renderer's counters of tile builds at the burst's first frame with work (-1: no burst is going on).</summary>
        private int m_FollowSuperseded0 = -1, m_FollowBuilt0;


        private readonly List<FollowWrite<Entity>> m_FollowWritten = new List<FollowWrite<Entity>>();
        private long m_FollowStart;
        private int m_FollowLogged, m_FollowWarned, m_FollowFailures;
        private float m_FollowTotalsAt;

        private void CreateFollow()
        {
            m_UpdatedEdges = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Updated>() },
                None = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() }
            });
            CreateHosts();
            m_TableOf = TableOf;
            m_PlaceOf = PlaceOf;
            m_FollowOutOfTime = () => (Stopwatch.GetTimestamp() - m_FollowStart) * 1000.0 / Stopwatch.Frequency >= kFollowBudgetMs;
        }

        private void LoadFollow()
        {
            m_Follow.Clear();
            m_FollowCache.Clear();
            m_FollowSkipped.Clear();
            m_FollowLogged = m_FollowWarned = m_FollowFailures = 0;
            FeatureState state = PreferencesStore.Current.RoadFollow;
            m_FollowActive = FollowGate.Enabled(state);
            m_FollowWrites = FollowGate.Writes(state);
            m_Follow.Writing = m_FollowWrites;
            m_FollowFrame = 0;
            m_FollowScale = 1.0;
            m_FollowFrameMs = 0.0;
            m_FollowSuperseded0 = -1;
            m_FollowOverrunLogged = false;
            m_FollowSkippedPath = 0;
            m_HostRetry.Clear();
            m_HostMissed = 0;
            m_FollowGate = FollowGate.GateFrames(Math.Max(60.0, TileWorker.RecentBuildMs));
            m_Follow.Scheduler.TileGateFrames = m_FollowGate;
            m_Follow.Scheduler.TileOrder = true;
            m_Follow.Scheduler.AtomicGroupMax = FollowGate.GroupSlice;
            m_Follow.Scheduler.MaxWaitFrames = FollowGate.MaxWaitFrames;
            m_Follow.Scheduler.NearMetres = FollowGate.NearMetres;
            m_FollowTotalsAt = UnityEngine.Time.realtimeSinceStartup + kFollowTotalsSeconds;
            Mod.log.Info($"Follow: road follow is {Preferences.StateName(state)}" + (!m_FollowActive ? "."
                : m_FollowWrites ? $": the items are linked to their roads (or, on a wall or on the game's lamp post or signal pole, to their building or post) as they are found and MOVED with the road or the host when the game updates it (every frame within {kFollowWriteBudgetMs} ms, the nearest the camera first, a tile's items in bursts at least {m_FollowGate} frames apart, no undo step; a line that grows by more than {FollowGate.LengthChange * 100f:0} % is not followed). Set features.roadFollow to \"log\" or \"off\" in preferences.json to stop it."
                : ": the items are linked to their roads as they are found, and an updated road is looked at in the log only (nothing is written)."));
        }

        /// <summary>The item stands by this road: the pool (road to items) and the follow are told. The relation to the road is measured the first time, against the road's curve as it is then.</summary>
        private void NoteLinked(Entity entity, TsItem item, Entity edge)
        {
            long start = Stopwatch.GetTimestamp();
            NoteLinkedCore(entity, item, edge);
            m_LinkTicks += Stopwatch.GetTimestamp() - start;
        }

        /// <summary>Ticks the links of a round took (what NoteLinked spent): the log names the part of the round it is, so that a round that costs more than it did can be told from the item array and the check.</summary>
        private long m_LinkTicks;

        private void NoteLinkedCore(Entity entity, TsItem item, Entity edge)
        {
            m_Roads.NoteLinked(entity, edge);
            if (!m_FollowActive)
                return;
            HostKind host = HostKindOf(item);
            if (host != HostKind.None)
            {
                // Another mod's pole or arm first, where one is given: the board stands at its foot or on its arm, and the game's posts are not it.
                if (m_FollowWrites && host != HostKind.Building && TsExtension.ProvidesHosts && EnsureOtherHostLink(entity, item, host == HostKind.Foreign, UnityEngine.Time.realtimeSinceStartup))
                    return;
                if (m_FollowWrites && host != HostKind.Foreign)
                {
                    EnsureHostLink(entity, item, host, UnityEngine.Time.realtimeSinceStartup);
                    return;
                }

                // Not followed: in log the follow does not look at hosts, and an arm of another mod is not found without one.
                m_Follow.Forget(entity);
                if (m_FollowSkipped.Add(entity))
                    m_Follow.Totals.Skip(host == HostKind.Building ? FollowSkip.Wall : FollowSkip.Hosted);
                return;
            }

            if (m_Follow.IsLinkedTo(entity, edge))
                return;
            if (EntityManager.HasComponent<Game.Common.Updated>(edge))
                m_Follow.Totals.LinkedWhileUpdating++;

            var at = new Vec3(item.Position.x, item.Position.y, item.Position.z);
            string path = PathOf(item);
            int group = GroupOf(item, path);
            if (path.Length > 0 && LinkLine(entity, item, edge, path, at, group))
                return;
            if (path.Length == 0 && RoadReader.TryRead(EntityManager, edge, out Bezier3 curve, out _))
                m_Follow.LinkSingle(entity, edge, m_FollowCache.Get(curve), at, item.Yaw, group);
        }

        /// <summary>
        /// What the scheduler takes together: the boards of one support (their Group), and the items of one line (what the pieces of a run share: the sign, the settings but the path, the phase and the whole
        /// length, which each piece has its own of, and the whole length). 0 for an item that stands alone.
        /// </summary>
        private static int GroupOf(TsItem item, string path)
        {
            if (path.Length == 0)
                return unchecked((int)item.Group);
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            if (def?.Template == null)
                return 0;
            GuideSpec spec = def.Template.Parse(item.SpecText);
            if (!int.TryParse(spec.Get(MarkingTemplates.TotalKey), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int total) || total <= 0)
                return 0;
            string family = spec.With(RunPath.Key, "").With(MarkingTemplates.PhaseKey, "").ToString();
            unchecked
            {
                int hash = (int)item.TypeHash * 31 + total;
                foreach (char c in family)
                    hash = hash * 31 + c;
                return hash | 1;
            }
        }

        /// <summary>Where a piece of a run starts along the whole line and the line's length (cm), from its settings: -1 and 0 for an item that has none.</summary>
        private static void PhaseOf(TsItem item, out long phase, out long total)
        {
            phase = -1;
            total = 0;
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            if (def?.Template == null || def.Template.FieldOf(MarkingTemplates.PhaseKey) == null)
                return;
            GuideSpec spec = def.Template.Parse(item.SpecText);
            if (long.TryParse(spec.Get(MarkingTemplates.PhaseKey), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long p) && p >= 0
                && long.TryParse(spec.Get(MarkingTemplates.TotalKey), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long t) && t > 0)
            {
                phase = p;
                total = t;
            }
        }

        /// <summary>The path of a marking that runs along a road (a barrier, a rail, a lane line: the settings' path), or an empty text.</summary>
        private static string PathOf(TsItem item)
        {
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            GuideTemplate template = def?.Template;
            return def != null && def.IsMarking && MarkingTemplates.IsRun(template) ? template.Parse(item.SpecText).Get(RunPath.Key) : "";
        }

        /// <summary>
        /// A line is linked to every road it runs along: the road it names, and the roads of the net search tree that are beside some of its points (a few of them are looked at: the relation of each point is to the
        /// nearest road). The tree is the game's own, read once in an update.
        /// </summary>
        private bool LinkLine(Entity entity, TsItem item, Entity hint, string path, Vec3 at, int group)
        {
            List<Vec3> points = MarkingPolygon.ToWorld(RunPath.Decode(path), at, item.Yaw);
            if (points.Count < 2 || !RoadReader.TryRead(EntityManager, hint, out Bezier3 hintCurve, out _))
                return false;
            m_LineEdges.Clear();
            m_LineTables.Clear();
            m_LineEdges.Add(hint);
            m_LineTables.Add(m_FollowCache.Get(hintCurve));

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (Vec3 point in points)
            {
                minX = Math.Min(minX, point.X);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxZ = Math.Max(maxZ, point.Z);
            }

            EnsureTree();
            m_Found.Clear();
            float reach = EdgeHint.Beside + 1f;
            var iterator = new EdgeIterator { Box = new Colossal.Mathematics.Bounds2(new float2(minX - reach, minZ - reach), new float2(maxX + reach, maxZ + reach)), Found = m_Found };
            m_Tree.Iterate(ref iterator);
            int stride = Math.Max(1, points.Count / 8);
            m_LineLevel.Clear();
            m_LineOff.Clear();
            foreach (Entity candidate in m_Found)
            {
                if (candidate == hint || m_LineEdges.Contains(candidate) || !EntityManager.HasComponent<Edge>(candidate) || !RoadReader.TryRead(EntityManager, candidate, out Bezier3 curve, out float width))
                    continue;
                bool beside = false, level = false;
                for (int j = 0; j < points.Count && !level; j += stride)
                {
                    if (EdgeHint.IsBeside(curve, width, points[j], out float along, out _))
                    {
                        beside = true;
                        level = Math.Abs(points[j].Y - curve.Point(along).Y) <= RoadLevels.LevelGap;
                    }
                }

                if (beside)
                    (level ? m_LineLevel : m_LineOff).Add((candidate, m_FollowCache.Get(curve)));
            }

            // The roads at the level of some point come first: of the few a line is linked to, a viaduct's wall must have the viaduct, however many roads on the ground run under it.
            foreach ((Entity road, FollowCurve table) in m_LineLevel)
            {
                if (m_LineEdges.Count >= kLineRoads)
                    break;
                m_LineEdges.Add(road);
                m_LineTables.Add(table);
            }

            foreach ((Entity road, FollowCurve table) in m_LineOff)
            {
                if (m_LineEdges.Count >= kLineRoads)
                    break;
                m_LineEdges.Add(road);
                m_LineTables.Add(table);
            }

            PhaseOf(item, out long phase, out long total);
            return m_Follow.LinkLine(entity, hint, m_LineEdges.ToArray(), m_LineTables.ToArray(), path, at, item.Yaw, group, phase, total);
        }

        private readonly List<(Entity Road, FollowCurve Table)> m_LineLevel = new List<(Entity, FollowCurve)>(), m_LineOff = new List<(Entity, FollowCurve)>();

        private FollowCurve TableOf(Entity edge) => RoadReader.TryRead(EntityManager, edge, out Bezier3 curve, out _) ? m_FollowCache.Get(curve) : null;

        private bool PlaceOf(Entity entity, bool wantPath, out Vec3 position, out float yaw, out string path)
        {
            position = default;
            yaw = 0f;
            path = null;
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                return false;
            TsItem item = EntityManager.GetComponentData<TsItem>(entity);
            position = new Vec3(item.Position.x, item.Position.y, item.Position.z);
            yaw = item.Yaw;
            if (wantPath)
                path = PathOf(item);
            return true;
        }

        /// <summary>
        /// The roads the game updated this frame are told to the follow (one append each, and only when there are items linked), and the items of those that have some are looked at within kFollowBudgetMs. A
        /// frame in which no road was updated and nothing waits costs one query test.
        /// </summary>
        private void StepFollow()
        {
            if (!m_FollowActive)
                return;
            m_FollowFrame++;
            m_FollowStart = Stopwatch.GetTimestamp();
            if (m_FollowWrites)
            {
                DrainHosts();
                StepOtherHosts();
            }

            if (m_Follow.Count > 0 && !m_UpdatedEdges.IsEmptyIgnoreFilter)
            {
                using (NativeArray<Entity> edges = m_UpdatedEdges.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity edge in edges)
                    {
                        m_Follow.Dirty(edge);
                        m_Follow.Totals.UpdatedRoads++;
                        if (RoadReader.IsElevated(EntityManager, edge))
                            m_Follow.Totals.UpdatedElevated++;
                    }
                }
            }

            if (m_Follow.Settled)
                return;
            if (m_FollowWrites)
            {
                StepFollowWrite();
                return;
            }

            bool worked = m_Follow.Step(m_TableOf, m_PlaceOf, FollowMode.Fraction, kFollowItemsPerFrame, m_FollowOutOfTime);
            double ms = (Stopwatch.GetTimestamp() - m_FollowStart) * 1000.0 / Stopwatch.Frequency;
            if (!worked)
                return;
            m_Follow.NoteFrame(ms);
            SlowLog.Add("ItemRoadSystem: road follow", ms, SlowLog.IsSlow(ms) ? $"{m_Follow.Count} item(s) linked" : null);
            FollowReport report = m_Follow.Finished();
            if (report != null)
                LogFollow(report);
        }

        /// <summary>
        /// A frame of the writing follow: the roads that changed give the scheduler their items, the scheduler hands out those to follow now (within a budget paced by what the frames really cost, a tile's gate
        /// open), and what they are to be written as is written, without an undo step. Any failure of it puts the follow back to log for the session.
        /// </summary>
        private void StepFollowWrite()
        {
            ChunkSystem chunks = ChunkSystem.Instance;
            if (chunks != null)
            {
                // The camera and the point it looks at: what the player waits to see move is round the second, which on a tilted view is hundreds of metres from the first.
                if (chunks.TryCamera(out float cx, out float cz))
                    m_Follow.Scheduler.SetFocus(cx, cz, chunks.TryLookAt(out float lx, out float lz), lx, lz);
                if (chunks.HoldBuild == null)
                    chunks.HoldBuild = FollowHoldsBuild;
            }

            m_FollowFrameMs = FollowGate.NextFrameMs(m_FollowFrameMs, UnityEngine.Time.unscaledDeltaTime * 1000.0);
            double boostMs = FollowGate.BoostMs(m_Follow.Scheduler.QuietFrames(m_FollowFrame), m_FollowFrameMs);
            m_Follow.Scheduler.NearBoostNs = boostMs > 0.0 ? FollowGate.PacedBudgetNs(boostMs, m_FollowScale) : 0.0;
            if (m_FollowFrame % 60 == 0)
            {
                m_FollowGate = FollowGate.GateFrames(Math.Max(60.0, TileWorker.RecentBuildMs));
                m_Follow.Scheduler.TileGateFrames = m_FollowGate;
            }

            m_FollowWritten.Clear();
            bool worked;
            double writeMs = 0.0;
            int collections = GC.CollectionCount(0);
            try
            {
                worked = m_Follow.StepWrite(m_TableOf, m_PlaceOf, FollowMode.Fraction, m_FollowFrame, FollowGate.PacedBudgetNs(kFollowWriteBudgetMs, m_FollowScale), kFollowItemsPerFrame, m_FollowWritten, m_HostPoseOf);
                if (worked)
                {
                    if (m_FollowSuperseded0 < 0 && chunks != null)
                    {
                        m_FollowSuperseded0 = chunks.SupersededTotal;
                        m_FollowBuilt0 = chunks.BuiltTotal;
                    }

                    long written = Stopwatch.GetTimestamp();
                    WriteFollowed();
                    writeMs = (Stopwatch.GetTimestamp() - written) * 1000.0 / Stopwatch.Frequency;
                }
            }
            catch (Exception ex)
            {
                FollowBackToLog(ex);
                return;
            }

            double ms = (Stopwatch.GetTimestamp() - m_FollowStart) * 1000.0 / Stopwatch.Frequency;
            if (!worked)
                return;
            m_FollowScale = FollowGate.NextScale(m_FollowScale, ms * 1e6, m_Follow.LastCostNs);
            collections = GC.CollectionCount(0) - collections;
            m_Follow.NoteFrame(ms, m_FollowFrame, UnityEngine.Time.realtimeSinceStartup, writeMs, collections, boostMs > 0.0);
            if (ms > 3.0 * kFollowWriteBudgetMs && !m_FollowOverrunLogged)
            {
                m_FollowOverrunLogged = true;
                Mod.log.Warn($"Follow: a frame took {ms:0.00} ms for {m_Follow.LastTaken} item(s) ({m_FollowWritten.Count} written), more than 3 times the {kFollowWriteBudgetMs} ms budget; the budget is paced by what the frames cost (scale {m_FollowScale:0.0}). Logged once.");
            }

            SlowLog.Add("ItemRoadSystem: road follow", ms, SlowLog.IsSlow(ms) ? $"{m_Follow.Count} item(s) linked, {m_Follow.LastTaken} followed, {m_FollowWritten.Count} written" : null);
            FollowReport report = m_Follow.Finished();
            if (report != null)
            {
                if (chunks != null && m_FollowSuperseded0 >= 0)
                {
                    report.BuildsDropped = chunks.SupersededTotal - m_FollowSuperseded0;
                    report.BuildsAccepted = chunks.BuiltTotal - m_FollowBuilt0;
                }

                m_FollowSuperseded0 = -1;
                LogFollow(report);
            }
        }

        /// <summary>The renderer asks of a tile that is to be built: the follow is writing its items still.</summary>
        private bool FollowHoldsBuild(TileKey key) => m_FollowWrites && m_Follow.Scheduler.IsBursting(FollowWatch<Entity, Entity>.TileOf(key));

        /// <summary>Puts what the follow found into the items: the place and the facing, and for a line whose shape changed the new text. Written as the tool writes an item, with no undo step.</summary>
        private void WriteFollowed()
        {
            foreach (FollowWrite<Entity> write in m_FollowWritten)
            {
                if (!EntityManager.Exists(write.Item) || !EntityManager.HasComponent<TsItem>(write.Item))
                    continue;
                TsItem item = EntityManager.GetComponentData<TsItem>(write.Item);
                if (write.Path != null || write.HasPhase)
                {
                    SignDef def = SignCatalog.ByHash(item.TypeHash);
                    string spec = def?.Template == null ? null : write.Path != null ? MarkingTemplates.WithPath(def.Template, item.SpecText, write.Path) : item.SpecText;
                    if (spec != null && write.HasPhase)
                        spec = MarkingTemplates.WithPhase(def.Template, spec, write.PhaseCm, write.TotalCm);
                    // The settings are written in 480 bytes (GuideSpec.MaxBytes): a longer text loses the end of its longest field, the path, in the middle of its last chord, silently (0.12.99: the check
                    // here, over 500 bytes, could never be true: the text had been cut to 480 already, and a wall that was followed came back 2 m short). What was written is read back and is what was
                    // meant, or the item keeps its old text and is measured again where it stands.
                    string meant = def?.Template == null ? null : write.Path ?? MarkingTemplates.PathOf(def.Template, item.SpecText);
                    if (spec == null || MarkingTemplates.PathOf(def.Template, spec) != meant)
                    {
                        if (m_FollowSkippedPath++ < 5)
                            Mod.log.Warn($"Follow: the new path of the item {write.Item.Index}:{write.Item.Version} does not fit the item's settings (480 bytes): it is left where it is.");
                        continue;
                    }

                    item.SetSpec(spec);
                }

                if (!write.PhaseOnly)
                {
                    item.Position = new float3(write.Position.X, write.Position.Y, write.Position.Z);
                    item.Yaw = write.Yaw;
                }

                EntityManager.SetComponentData(write.Item, item);
            }
        }

        private void FollowBackToLog(Exception ex)
        {
            m_FollowWrites = false;
            m_Follow.Writing = false;
            if (ChunkSystem.Instance != null)
                ChunkSystem.Instance.HoldBuild = null;
            Mod.log.Error("Follow: writing the items failed, the follow is back to log (nothing more is written this session): " + ex);
        }

        private void LogFollow(FollowReport report)
        {
            if (m_FollowLogged++ < kFollowDetailReports)
            {
                Mod.log.Info(m_FollowWrites ? report.FormatWritten() : report.Format());
                if (m_FollowWrites)
                    Mod.log.Info(report.FormatTiming());
            }
            foreach (string warning in report.Warnings)
            {
                if (m_FollowWarned++ < kFollowWarnings)
                    Mod.log.Warn(warning);
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < m_FollowTotalsAt)
                return;
            string totals = m_FollowWrites ? m_Follow.Totals.TakeWrittenLine() : m_Follow.Totals.TakeLine();
            if (totals != null)
            {
                Mod.log.Info(totals);
                string levels = m_Follow.Totals.TakeLevelsLine();
                if (levels != null)
                    Mod.log.Info(levels);
                m_FollowTotalsAt = now + kFollowTotalsSeconds;
            }
        }
    }
}
