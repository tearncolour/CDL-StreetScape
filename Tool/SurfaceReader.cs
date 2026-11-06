using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.IO.AssetDatabase;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// Reads the surface of the roads under an area marking (0.12.4): every road segment and junction whose box meets the area's (the game's search tree of nets), each part of it (the segment's two
    /// halves, EdgeGeometry; the junction's part next to each of its segments, StartNodeGeometry or EndNodeGeometry) with the pieces of its cross-section (the composition of the segment, or of its end at
    /// the junction: NetCompositionPiece, NetPieceData). The pieces that can be painted (SurfaceSection.Paintable) become bands along the part's sides: across a part, a place x from the composition's middle
    /// is at x / width + 1/2 of the way from its left side to its right (as the game puts its lanes: LaneSystem), a junction's part whose middle bends in two halves. The first readings of a session are
    /// written to the log with every piece and what it was taken for (it is the probe of how the game keeps them). Read only. Main thread.
    /// </summary>
    internal static class SurfaceReader
    {
        private const int kSamples = PartSampling.FixedSamples;
        private const int kMaxLogged = 6;
        private static int s_Logged, s_LoggedStand, s_LoggedTrack;

        /// <summary>
        /// What the parts of roads that were read are kept for (0.12.73; the owner: "缓存容量大一些"): an estimated number of bytes (SurfacePartCache.WeightOf: 12 bytes a point and 64 a strip), not a count and not
        /// a measure. A part is some 2 to 5 KB, so this is some twenty to thirty thousand parts. Held until the city is left (Reset); the log says how full it gets (Summary).
        /// </summary>
        private const long kPartBytes = 64L * 1024 * 1024;

        /// <summary>How many compositions' stretches are kept (a few thousand: a city has some hundreds of kinds of road, each in a few rules).</summary>
        private const long kStretchSets = 4096;

        private static readonly SurfacePartCache<(Entity, int, int)> s_PartCache = new SurfacePartCache<(Entity, int, int)>(kPartBytes);

        /// <summary>What a composition's cross-section makes of the stretches that can be painted or stood on (by rule), with the print of what they were made from.</summary>
        private sealed class StretchSet
        {
            public ulong Print;
            public List<PaintableStretch> Stretches;
        }

        private static readonly SieveCache<(Entity, int), StretchSet> s_Stretches = new SieveCache<(Entity, int), StretchSet>(kStretchSets);

        /// <summary>
        /// The sampler the bands are laid by (features.adaptiveSampling, 0.12.75): Off the fixed 16 steps of every band, as until 0.12.73; Shadow the same, and the adaptive sampler lays each part that is made
        /// beside it into a scratch read to be compared (nothing of that is kept or used); On the adaptive sampler's bands are the ones used. Set once when the mod loads.
        /// </summary>
        internal static FeatureState Sampling { get; set; } = FeatureState.Shadow;

        private static readonly SamplingStats s_SamplingStats = new SamplingStats();
        private static readonly ShadowGate s_ShadowGate = new ShadowGate();
        private static readonly ShadowWorker s_ShadowWorker = new ShadowWorker();
        private static bool s_ShadowBroken;
        private static int s_ShadowFailures;

        /// <summary>How the bands of a part are laid: by the fixed steps or the adaptive sampler, which ends of the segment are at a junction (the adaptive sampler keeps a regular grid there), where the curves go to be measured, what is kept for the part's shadow, and the time it took.</summary>
        private sealed class Sampler
        {
            public readonly bool Adaptive, StartJunction, EndJunction;
            public readonly SamplingProbe Probe;
            public readonly ShadowCapture Capture;
            public long Ticks;

            public Sampler(bool adaptive, bool startJunction = false, bool endJunction = false, SamplingProbe probe = null, ShadowCapture capture = null)
            {
                Adaptive = adaptive;
                StartJunction = startJunction;
                EndJunction = endJunction;
                Probe = probe;
                Capture = capture;
            }

            public double Ms => Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        // What the reads cost since the city was loaded (0.12.73; the owner asked whether a hash table could make the loading faster): the reads, the time in them, the parts of edges they went
        // through and how many of those had been read before in the same way (the repeats are what the cache of the parts saves), and the cutting of the cells that the reads are made for
        // (AreaFit.FromStrips for an area, ForHeights for a post or a barrier's feet): the number of cuts and the time in them.
        private static readonly HashSet<(Entity, int, int)> s_Seen = new HashSet<(Entity, int, int)>();
        private static int s_Reads, s_Parts, s_Repeated, s_AreaFits, s_HeightFits;
        private static double s_ReadMs, s_AreaFitMs, s_HeightFitMs;

        /// <summary>The city was loaded or left: what was kept and counted is forgotten.</summary>
        internal static void Reset()
        {
            s_Seen.Clear();
            s_PartCache.Clear();
            s_Stretches.Clear();
            s_Reads = s_Parts = s_Repeated = s_AreaFits = s_HeightFits = 0;
            s_ReadMs = s_AreaFitMs = s_HeightFitMs = 0.0;
            s_SamplingStats.Reset();
            s_ShadowGate.Reset();
            s_ShadowWorker.Clear();
            s_ShadowCopyMs = 0.0;
        }

        /// <summary>One cutting of the cells that began at the Stopwatch timestamp start has ended: of an area (heights false) or of the heights under posts and feet.</summary>
        internal static void NoteFit(bool heights, long start) =>
            NoteFitMs(heights, (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        /// <summary>One cutting that took ms, wherever it was made (the main thread, or a worker whose result the main thread has taken: the count and the time are of the cutting, not of the main thread's).</summary>
        internal static void NoteFitMs(bool heights, double ms)
        {
            if (heights)
            {
                s_HeightFits++;
                s_HeightFitMs += ms;
            }
            else
            {
                s_AreaFits++;
                s_AreaFitMs += ms;
            }
        }

        internal static string Summary() =>
            string.Format(CultureInfo.InvariantCulture,
                "{0} read(s) of the roads' surface took {1:0} ms, {2} edge part(s) read, {3} of them read before the same way; kept: {4} hit(s), {5} miss(es) of which {6} of a road that changed, {7} part(s) held (about {8:0.0} of {9:0} MB, estimated), "
                + "{10} hit(s) and {11} miss(es) of the compositions' stretches; cutting: {12} area fit(s) {13:0} ms, {14} height fit(s) {15:0} ms",
                s_Reads, s_ReadMs, s_Parts, s_Repeated, s_PartCache.Hits, s_PartCache.Misses, s_PartCache.Stale, s_PartCache.Count, s_PartCache.Bytes / 1048576.0, s_PartCache.Budget / 1048576.0,
                s_Stretches.Hits, s_Stretches.Misses, s_AreaFits, s_AreaFitMs, s_HeightFits, s_HeightFitMs)
            + "; " + KerbSummary()
            + "; " + s_SamplingStats.Describe(Sampling, s_ShadowGate)
            + (Sampling == FeatureState.Shadow ? string.Format(CultureInfo.InvariantCulture, "; shadow: {0} given to the worker (the main thread {1:0.0} ms in all), {2} on the worker's thread {3:0} ms, {4} failed", s_ShadowGate.Sent, s_ShadowCopyMs, s_ShadowWorker.Submitted, s_ShadowWorker.WorkerMs, s_ShadowWorker.Failed) : "");

        /// <summary>The rule a read is made by (what its stretches are): 0 paintable, 1 standable, 2 a track's.</summary>
        private static int Mode(bool standable) => s_Tracks ? 2 : standable ? 1 : 0;

        private static void CountPart(Entity edge, int part, bool standable)
        {
            s_Parts++;
            if (!s_Seen.Add((edge, part, Mode(standable))))
                s_Repeated++;
        }

        /// <summary>Set while Around reads the surface of the tracks (0.12.66): the edges taken are the railway, metro and tram ones, and the pieces are judged by SurfaceSection.StandableTrack. Main thread.</summary>
        private static bool s_Tracks;

        internal sealed class Read
        {
            public readonly List<SurfaceStrip> Strips = new List<SurfaceStrip>();
            public int Segments, Junctions;
            public string Report = "";

            /// <summary>A road piece of what was read has kerbs the game was still loading (the piece is taken as one surface): the strips are provisional, and are not what the same read gives once it is known.</summary>
            public bool Pending;
        }

        private struct Iterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 Box;
            public List<Entity> Found;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, Box);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, Box))
                    Found.Add(item);
            }
        }

        private static Vec3 V(float3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>The bands of road surface that can be painted over a box of the ground (world X, Z); with standable, those a post may stand on (0.12.5).</summary>
        /// <summary>
        /// As above (0.12.66; the owner: "支持吸附到铁路和地铁线路"): with tracks, the surface of the railway, metro and tram edges that a post or a wall may stand on, and no road's. Roads are read exactly as
        /// they were when tracks is false.
        /// </summary>
        internal static Read Around(EntityManager em, Game.Net.SearchSystem search, PrefabSystem prefabs, float minX, float minZ, float maxX, float maxZ, bool standable, bool forceLog, bool tracks)
        {
            bool before = s_Tracks;
            s_Tracks = tracks;
            try
            {
                return Around(em, search, prefabs, minX, minZ, maxX, maxZ, standable || tracks, forceLog);
            }
            finally
            {
                s_Tracks = before;
            }
        }

        /// <summary>The bands of road surface that can be painted over a box of the ground (world X, Z); with standable, those a post may stand on (0.12.5).</summary>
        internal static Read Around(EntityManager em, Game.Net.SearchSystem search, PrefabSystem prefabs, float minX, float minZ, float maxX, float maxZ, bool standable = false, bool forceLog = false)
        {
            var read = new Read();
            long begin = System.Diagnostics.Stopwatch.GetTimestamp();
            bool log = forceLog || (s_Tracks ? s_LoggedTrack < kMaxLogged : standable ? s_LoggedStand < kMaxLogged : s_Logged < kMaxLogged);
            var report = log ? new StringBuilder() : null;
            try
            {
                NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = search.GetNetSearchTree(true, out JobHandle dependencies);
                dependencies.Complete();
                var found = new List<Entity>();
                var iterator = new Iterator { Box = new Bounds2(new float2(minX - 1f, minZ - 1f), new float2(maxX + 1f, maxZ + 1f)), Found = found };
                long lapTree = System.Diagnostics.Stopwatch.GetTimestamp();
                tree.Iterate(ref iterator);
                SlowLog.Since("SurfaceReader: the net search tree", lapTree);
                var parts = new HashSet<(Entity, int)>();
                foreach (Entity e in found)
                {
                    if (!Usable(em, e))
                        continue;
                    if (em.HasComponent<Edge>(e))
                    {
                        // The segment and its two ends at the junctions.
                        for (int part = 0; part < 3; part++)
                        {
                            if (parts.Add((e, part)))
                                ReadPart(em, prefabs, e, part, read, report, standable);
                        }
                    }
                    else if (em.HasComponent<Node>(e) && em.TryGetBuffer(e, true, out DynamicBuffer<ConnectedEdge> edges))
                    {
                        for (int i = 0; i < edges.Length; i++)
                        {
                            Entity edge = edges[i].m_Edge;
                            if (!Usable(em, edge) || !em.TryGetComponent(edge, out Edge ends))
                                continue;
                            bool start = ends.m_Start == e;
                            if (!start && ends.m_End != e)
                                continue;
                            if (parts.Add((edge, start ? 1 : 2)))
                                ReadPart(em, prefabs, edge, start ? 1 : 2, read, report, standable);
                        }
                    }
                }

                read.Report = $"Surface{(s_Tracks ? " (of the tracks, where posts stand)" : standable ? " (where posts stand)" : "")}: {read.Segments} segment part(s) and {read.Junctions} junction part(s) over ({minX:0.0}, {minZ:0.0}) .. ({maxX:0.0}, {maxZ:0.0}), {read.Strips.Count} band(s) that can be painted"
                    + (report != null && report.Length > 0 ? ":" + report : ".");
            }
            catch (Exception ex)
            {
                // Whatever the game's data does, drawing a marking must not fail because of it: the area is then painted as it is drawn.
                read.Strips.Clear();
                read.Report = "Surface: reading failed: " + ex;
            }

            if (log)
            {
                if (s_Tracks)
                    s_LoggedTrack++;
                else if (standable)
                    s_LoggedStand++;
                else
                    s_Logged++;
                Mod.log.Info(read.Report);
            }

            s_Reads++;
            s_ReadMs += (System.Diagnostics.Stopwatch.GetTimestamp() - begin) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            return read;
        }

        private static bool Usable(EntityManager em, Entity e) =>
            e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e) && !em.HasComponent<Game.Tools.Temp>(e)
            && (s_Tracks ? em.HasComponent<Game.Net.TrainTrack>(e) || em.HasComponent<Game.Net.SubwayTrack>(e) || em.HasComponent<Game.Net.TramTrack>(e) : em.HasComponent<Road>(e));

        /// <summary>
        /// One part of an edge: 0 its segment, 1 and 2 its junction parts at the start and the end. With a report (the read is logged) the part is made and nothing is kept or taken, so that the log says
        /// what the code computes; else it is taken from SurfacePartCache when its fingerprint (what it is made from, below) is the one it was kept for, or made and kept. A part that is not there, or too
        /// short to make a band, makes nothing and is not kept (finding that out is cheaper than the print).
        /// </summary>
        private static void ReadPart(EntityManager em, PrefabSystem prefabs, Entity edge, int part, Read read, StringBuilder report, bool standable)
        {
            CountPart(edge, part, standable);
            if (!em.TryGetComponent(edge, out Composition composition))
                return;
            EdgeGeometry geometry = default;
            EdgeNodeGeometry node = default;
            Entity used;
            if (part == 0)
            {
                if (!em.TryGetComponent(edge, out geometry))
                    return;
                used = composition.m_Edge;
            }
            else
            {
                if (!TryNodeGeometry(em, edge, part == 1, out node))
                    return;
                used = part == 1 ? composition.m_StartNode : composition.m_EndNode;
            }

            bool adaptive = Sampling == FeatureState.On, startJunction = false, endJunction = false;
            if (part == 0 && adaptive)
                Junctions(em, edge, out startJunction, out endJunction);
            if (report != null)
            {
                MakePart(em, prefabs, edge, part, geometry, node, used, 0UL, read, report, standable, new Sampler(adaptive, startJunction, endJunction));
                return;
            }

            // What the part's bands are made from: the rule, the composition (CompositionPrint) and the curves of the part, all that MakeEdge and MakeNode read. The game gives a changed road new curves
            // and a new composition entity, so nothing needs to tell the cache that a road changed.
            long lapPrint = System.Diagnostics.Stopwatch.GetTimestamp();
            ulong print = CompositionPrint(em, used, out bool complete);
            if (!complete)
            {
                read.Pending = true;
                s_Provisional++;
            }

            var fingerprint = new Fingerprint();
            fingerprint.Add(Mode(standable));
            fingerprint.Add(print);
            PartSampling.AddPrint(ref fingerprint, adaptive);
            if (part == 0)
            {
                // The zones of a segment's halves are where the edge's ends have a junction part: that is read from the nodes' geometry, which the segment's own curves do not say.
                if (adaptive)
                {
                    fingerprint.Add(startJunction ? 1 : 0);
                    fingerprint.Add(endJunction ? 1 : 0);
                }

                AddSegment(ref fingerprint, geometry.m_Start);
                AddSegment(ref fingerprint, geometry.m_End);
            }
            else
            {
                AddSegment(ref fingerprint, node.m_Left);
                AddSegment(ref fingerprint, node.m_Right);
                AddBezier(ref fingerprint, node.m_Middle);
                fingerprint.Add(node.m_MiddleRadius);
            }

            var key = (edge, part, Mode(standable));
            bool kept = s_PartCache.TryAdd(key, fingerprint.Value, read.Strips, ref read.Segments, ref read.Junctions);
            SlowLog.Since("SurfaceReader: a part's print and the cache's answer", lapPrint);
            if (kept)
                return;
            long lapMade = System.Diagnostics.Stopwatch.GetTimestamp();
            int first = read.Strips.Count, segments = read.Segments, junctions = read.Junctions;
            // The shadow of the adaptive sampler: the fixed path says what it lays the bands from as it lays them, and the comparison is made on a worker (ShadowWorker).
            ShadowCapture capture = null;
            if (Sampling == FeatureState.Shadow && !s_ShadowBroken && s_ShadowGate.Admit(UnityEngine.Time.frameCount, s_ShadowWorker.Outstanding))
            {
                capture = new ShadowCapture();
                if (part == 0)
                    Junctions(em, edge, out startJunction, out endJunction);
            }

            var sampler = new Sampler(adaptive, startJunction, endJunction, null, capture);
            MakePart(em, prefabs, edge, part, geometry, node, used, print, read, null, standable, sampler);
            s_PartCache.Keep(key, fingerprint.Value, read.Strips, first, read.Segments - segments, read.Junctions - junctions);
            int pieces = 0;
            for (int i = first; i < read.Strips.Count; i++)
                pieces += Math.Max(0, read.Strips[i].Left.Count - 1);
            s_SamplingStats.NoteMade(pieces, sampler.Ms);
            if (capture != null)
                GiveShadow(capture, edge, part, read.Strips, first, sampler.Ms);
            SlowLog.Since("SurfaceReader: a part made", lapMade);
        }

        /// <summary>The ends of an edge that have a junction part (StartNodeGeometry or EndNodeGeometry with a length): where the adaptive sampler keeps its regular grid.</summary>
        private static void Junctions(EntityManager em, Entity edge, out bool start, out bool end)
        {
            start = TryNodeGeometry(em, edge, true, out _);
            end = TryNodeGeometry(em, edge, false, out _);
        }

        /// <summary>
        /// The shadow of the adaptive sampler (0.12.75): the part the fixed sampler has just made is given to the worker with the curves and stretches it was laid from, and the adaptive sampler lays it again
        /// there and compares the two (SamplingCheck: how far each is from the curves, the corners, the plan areas). What the main thread does is to copy those. The shadow must not cost the read: it
        /// is switched off for the session after three failures, and an exception here does not reach the read.
        /// </summary>
        private static void GiveShadow(ShadowCapture capture, Entity edge, int part, List<SurfaceStrip> strips, int first, double fixedMs)
        {
            long lap = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (strips.Count > first && capture.TryJob(part == 0 ? $"segment {edge.Index}" : $"junction part {edge.Index}:{part}", strips, first, fixedMs, out ShadowJob job))
                {
                    s_ShadowWorker.Submit(job);
                    s_ShadowCopyMs += (System.Diagnostics.Stopwatch.GetTimestamp() - lap) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    return;
                }
            }
            catch (Exception ex)
            {
                s_ShadowBroken = true;
                Mod.log.Warn("Sampling: the shadow comparison could not be given to its worker and is off for this session: " + ex);
            }

            s_ShadowGate.Withdraw();
        }

        /// <summary>The main thread's time giving parts to the shadow's worker (ms): the cost of the shadow to the frame.</summary>
        private static double s_ShadowCopyMs;

        private const int kDrainPerFrame = 64;

        /// <summary>
        /// Takes the comparisons the worker has made and adds them to the statistics (called every frame by the system that reads the surface; a frame with nothing waiting costs a lock). The first
        /// summary is written after 100 parts, the last after 2000, and one every three minutes in between when there is something new. A failed comparison counts against the shadow: three, and it is off.
        /// </summary>
        internal static void DrainShadow()
        {
            if (s_ShadowWorker.Outstanding == 0)
                return;
            long lap = System.Diagnostics.Stopwatch.GetTimestamp();
            int taken = 0;
            while (taken < kDrainPerFrame && s_ShadowWorker.TryTake(out ShadowDone done))
            {
                taken++;
                if (done.Error != null)
                {
                    if (++s_ShadowFailures >= 3 && !s_ShadowBroken)
                    {
                        s_ShadowBroken = true;
                        Mod.log.Warn("Sampling: three shadow comparisons failed and the shadow is off for this session: " + done.Error);
                    }

                    continue;
                }

                s_SamplingStats.Add(done.Comparison);
            }

            if (taken > 0)
            {
                double now = UnityEngine.Time.realtimeSinceStartup;
                if (s_SamplingStats.SummaryDue(now))
                {
                    Mod.log.Info(s_SamplingStats.Report(Sampling, s_ShadowGate));
                    s_SamplingStats.MarkPrinted(now);
                }
            }

            SlowLog.Since("SurfaceReader: the shadow's results taken", lap);
        }

        private static void MakePart(EntityManager em, PrefabSystem prefabs, Entity edge, int part, EdgeGeometry geometry, EdgeNodeGeometry node, Entity composition, ulong print, Read read, StringBuilder report, bool standable, Sampler sampler)
        {
            if (part == 0)
                MakeEdge(em, prefabs, edge, geometry, composition, print, read, report, standable, sampler);
            else
                MakeNode(em, prefabs, edge, part == 1, node, composition, print, read, report, standable, sampler);
        }

        /// <summary>The junction part of an edge at its start or its end: false when the edge has none, or it is too short to make a band.</summary>
        private static bool TryNodeGeometry(EntityManager em, Entity edge, bool start, out EdgeNodeGeometry g)
        {
            g = default;
            if (start)
            {
                if (!em.TryGetComponent(edge, out StartNodeGeometry sg))
                    return false;
                g = sg.m_Geometry;
            }
            else
            {
                if (!em.TryGetComponent(edge, out EndNodeGeometry eg))
                    return false;
                g = eg.m_Geometry;
            }

            return g.m_Left.m_Length.x > 0.05f || g.m_Left.m_Length.y > 0.05f || g.m_Right.m_Length.x > 0.05f || g.m_Right.m_Length.y > 0.05f;
        }

        /// <summary>
        /// What the stretches of a composition are made from (added to a part's fingerprint, and the key of the kept stretches): the entity, its width, middle offset and flags, and every piece of its
        /// cross-section (the piece's entity, offset, size and flags). Not in it, because they are constant for the session (they are looked up by the piece's entity, which is in it): the piece's own
        /// data (NetPieceData.m_SurfaceHeights) and mesh (the kerbs: Kerbs.FacesOf, kept by piece in s_Faces). The design of the cache left the pieces out; MakeEdge reads them through Stretches.
        /// The one thing of a piece's mesh that does change in a session is whether it is known: a kerb piece the game was still loading is taken as one surface (complete false), and what is made of it
        /// must not be kept as what the piece gives, so a bit for each kerb piece says whether its mesh was known (it is read here when the game lets it be).
        /// </summary>
        private static ulong CompositionPrint(EntityManager em, Entity composition, out bool complete)
        {
            complete = true;
            var fingerprint = new Fingerprint();
            fingerprint.Add(composition.Index);
            fingerprint.Add(composition.Version);
            if (composition == Entity.Null || !em.TryGetComponent(composition, out NetCompositionData data) || !em.TryGetBuffer(composition, true, out DynamicBuffer<NetCompositionPiece> buffer))
            {
                fingerprint.Add(-1);
                return fingerprint.Value;
            }

            fingerprint.Add(data.m_Width);
            fingerprint.Add(data.m_MiddleOffset);
            fingerprint.Add((int)data.m_Flags.m_General);
            fingerprint.Add((int)data.m_Flags.m_Left);
            fingerprint.Add((int)data.m_Flags.m_Right);
            for (int i = 0; i < buffer.Length; i++)
            {
                NetCompositionPiece p = buffer[i];
                fingerprint.Add(p.m_Piece.Index);
                fingerprint.Add(p.m_Piece.Version);
                fingerprint.Add(p.m_Offset.x);
                fingerprint.Add(p.m_Offset.y);
                fingerprint.Add(p.m_Offset.z);
                fingerprint.Add(p.m_Size.x);
                fingerprint.Add(p.m_Size.y);
                fingerprint.Add(p.m_Size.z);
                fingerprint.Add((int)p.m_SectionFlags);
                fingerprint.Add((int)p.m_PieceFlags);
                if (NeedsFaces(em, p))
                {
                    bool known = FacesOf(em, p.m_Piece, out _);
                    fingerprint.Add(known ? 1 : 0);
                    complete &= known;
                }
            }

            return fingerprint.Value;
        }

        private static void AddBezier(ref Fingerprint fingerprint, Bezier4x3 b)
        {
            AddPoint(ref fingerprint, b.a);
            AddPoint(ref fingerprint, b.b);
            AddPoint(ref fingerprint, b.c);
            AddPoint(ref fingerprint, b.d);
        }

        private static void AddPoint(ref Fingerprint fingerprint, float3 p)
        {
            fingerprint.Add(p.x);
            fingerprint.Add(p.y);
            fingerprint.Add(p.z);
        }

        private static void AddSegment(ref Fingerprint fingerprint, Segment s)
        {
            AddBezier(ref fingerprint, s.m_Left);
            AddBezier(ref fingerprint, s.m_Right);
            fingerprint.Add(s.m_Length.x);
            fingerprint.Add(s.m_Length.y);
        }

        private static void MakeEdge(EntityManager em, PrefabSystem prefabs, Entity edge, EdgeGeometry geometry, Entity composition, ulong print, Read read, StringBuilder report, bool standable, Sampler sampler)
        {
            List<PaintableStretch> stretches = Stretches(em, prefabs, composition, print, out float width, out bool elevated, report, $"segment {edge.Index}", standable);
            if (stretches == null)
                return;
            read.Segments++;
            long begin = System.Diagnostics.Stopwatch.GetTimestamp();
            Segment[] halves = { geometry.m_Start, geometry.m_End };
            for (int h = 0; h < halves.Length; h++)
            {
                Segment segment = halves[h];
                if (!(segment.m_Length.x > 0.05f || segment.m_Length.y > 0.05f))
                    continue;
                Bezier4x3 l = segment.m_Left, r = segment.m_Right;
                if (sampler.Adaptive)
                {
                    // The first half's start is the edge's start, the second half's end is the edge's end: a junction there keeps its regular grid.
                    var zones = new Zones(h == 0 && sampler.StartJunction, h == 1 && sampler.EndJunction);
                    AddBands(read, PartSampling.HalfBands(CurveConvert.Curve(l), CurveConvert.Curve(r), stretches, zones, $"segment {edge.Index}", sampler.Probe), elevated);
                    continue;
                }

                if (sampler.Capture != null)
                {
                    sampler.Capture.Segment($"segment {edge.Index}", stretches);
                    sampler.Capture.Half(CurveConvert.Curve(l), CurveConvert.Curve(r), new Zones(h == 0 && sampler.StartJunction, h == 1 && sampler.EndJunction));
                }

                foreach (PaintableStretch s in stretches)
                    AddBands(read, SurfaceSection.Bands(t => V(MathUtils.Position(l, t)), t => V(MathUtils.Position(r, t)), s, kSamples, $"segment {edge.Index}"), elevated);
            }

            sampler.Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - begin;
        }

        private static void MakeNode(EntityManager em, PrefabSystem prefabs, Entity edge, bool start, EdgeNodeGeometry g, Entity nodeComposition, ulong print, Read read, StringBuilder report, bool standable, Sampler sampler)
        {
            List<PaintableStretch> stretches = Stretches(em, prefabs, nodeComposition, print, out float width, out bool elevated, report, $"junction part of segment {edge.Index} at its {(start ? "start" : "end")}", standable);
            if (stretches == null)
                return;
            read.Junctions++;
            string source = $"junction of segment {edge.Index}";
            // A junction's part next to a segment (EdgeNodeGeometry), as the game's own debug view draws it (NetDebugSystem) and its GeometrySystem reads it:
            // - an ordinary junction (no middle radius): two halves side by side across the road, both from the segment's end into the junction: m_Left from its left side (m_Left.m_Left) to its middle
            //   line (m_Left.m_Right), m_Right from the middle line (m_Right.m_Left) to its right side (m_Right.m_Right). The middle line runs to the junction's middle, so the halves fan out from it:
            //   between two roads that meet, the asphalt up to the middle is theirs. The middle is at 0.5 + MiddleOffset / width of the part's cross-section, which is in the part's own frame (mirrored at
            //   a segment's start: CompositionSelectSystem inverts the start's composition).
            // - a junction whose middle bends (MiddleRadius > 0: a roundabout, a fixed node): two pieces one after the other along the way, m_Left from the segment's end to the middle and m_Right on
            //   from there, each the whole width.
            // 0.12.4 to 0.12.6 had the two cases the other way round, and 0.12.7 to 0.12.9 drew an ordinary part as one band from its left side straight to its right: the corner of asphalt between
            // that line and the junction's middle was left out, and an island at a merge was cut there (the owner's screenshots of 21 and 22 Oct: 39 % of it on the road).
            var pieces = new List<(Bezier4x3 Left, Bezier4x3 Right, List<PaintableStretch> Stretches, bool Filler)>();
            if (g.m_MiddleRadius > 0f)
            {
                pieces.Add((g.m_Left.m_Left, g.m_Left.m_Right, stretches, false));
                pieces.Add((g.m_Right.m_Left, g.m_Right.m_Right, stretches, false));
            }
            else
            {
                float split = 0.5f;
                if (em.TryGetComponent(nodeComposition, out NetCompositionData data) && data.m_Width > 0.01f)
                    split = 0.5f + data.m_MiddleOffset / data.m_Width;
                var leftHalf = new List<PaintableStretch>();
                var rightHalf = new List<PaintableStretch>();
                foreach (PaintableStretch s in stretches)
                {
                    foreach ((bool right, PaintableStretch local) in SurfaceSection.SplitAcross(s, split))
                        (right ? rightHalf : leftHalf).Add(local);
                }

                pieces.Add((g.m_Left.m_Left, g.m_Left.m_Right, leftHalf, false));
                pieces.Add((g.m_Right.m_Left, g.m_Right.m_Right, rightHalf, false));
                // The halves' middle lines need not be one curve (GeometrySystem takes the junction's middle line half way between them): where they part, at a fork, the asphalt between them is the
                // gore's (0.12.14: the owner's island at a fork was cut there, 52 % on the road). It is painted when the middle is inside a stretch that is.
                bool middlePainted = stretches.Exists(s => s.F0 < split - 1e-3f && s.F1 > split + 1e-3f);
                float gap = Math.Max(math.distance(g.m_Left.m_Right.a.xz, g.m_Right.m_Left.a.xz), Math.Max(math.distance(g.m_Left.m_Right.d.xz, g.m_Right.m_Left.d.xz), math.distance(MathUtils.Position(g.m_Left.m_Right, 0.5f).xz, MathUtils.Position(g.m_Right.m_Left, 0.5f).xz)));
                // The game's mesh lays that gore on three curves, the left half's middle line, the junction's middle line (m_Middle) and the right half's (BatchDataHelpers.CalculateNodeParameters): two bands,
                // one each side of the middle line (0.12.18: one band straight from one half's line to the other's missed the asphalt where the middle line bows out, at the tip of the owner's island).
                // A part with no middle line of its own (m_Middle not set) has the one band.
                if (middlePainted && gap > 0.05f)
                {
                    var gore = new List<PaintableStretch> { new PaintableStretch(0f, 1f, stretches.Find(s => s.F0 < split && s.F1 > split).Y) };
                    bool hasMiddle = MathUtils.Length(g.m_Middle.xz) > 0.05f;
                    if (hasMiddle)
                    {
                        pieces.Add((g.m_Left.m_Right, g.m_Middle, gore, true));
                        pieces.Add((g.m_Middle, g.m_Right.m_Left, gore, true));
                    }
                    else
                    {
                        pieces.Add((g.m_Left.m_Right, g.m_Right.m_Left, gore, true));
                    }
                }
                if (report != null)
                    report.Append(string.Format(CultureInfo.InvariantCulture, "\n    halves at {0:0.000} of the width; their middle lines {1:0.00} m apart at most{2}; left half {3} to {4}, right half {5} to {6}, middle {7} to {8}", split, gap,
                        middlePainted && gap > 0.05f ? " (the gore between them painted)" : "", P(g.m_Left.m_Left.a), P(g.m_Left.m_Right.d), P(g.m_Right.m_Left.a), P(g.m_Right.m_Right.d), P(g.m_Middle.a), P(g.m_Middle.d)));
            }

            long begin = System.Diagnostics.Stopwatch.GetTimestamp();
            if (sampler.Adaptive || sampler.Capture != null)
            {
                // One list of places for every curve of the part (the halves, the middle line and the gore meet along shared curves: separate lists open a sliver).
                var nodePieces = new List<NodePiece>(pieces.Count);
                foreach ((Bezier4x3 left, Bezier4x3 right, List<PaintableStretch> across, bool filler) in pieces)
                {
                    if (!(MathUtils.Length(left.xz) < 0.05f && MathUtils.Length(right.xz) < 0.05f))
                        nodePieces.Add(new NodePiece(CurveConvert.Curve(left), CurveConvert.Curve(right), across, filler));
                }

                sampler.Capture?.Pieces(source, nodePieces);
                if (sampler.Adaptive)
                {
                    AddBands(read, PartSampling.NodeBands(nodePieces, source, sampler.Probe), elevated);
                    sampler.Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - begin;
                    return;
                }

                begin = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            foreach ((Bezier4x3 left, Bezier4x3 right, List<PaintableStretch> across, bool filler) in pieces)
            {
                if (MathUtils.Length(left.xz) < 0.05f && MathUtils.Length(right.xz) < 0.05f)
                    continue;
                foreach (PaintableStretch s in across)
                {
                    List<SurfaceStrip> bands = SurfaceSection.Bands(t => V(MathUtils.Position(left, t)), t => V(MathUtils.Position(right, t)), s, kSamples, source);
                    foreach (SurfaceStrip band in bands)
                        band.Filler = filler;
                    AddBands(read, bands, elevated);
                }
            }

            sampler.Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - begin;
        }

        private static string P(float3 p) => string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", p.x, p.y, p.z);

        private static void AddBands(Read read, List<SurfaceStrip> bands, bool elevated)
        {
            foreach (SurfaceStrip band in bands)
                band.Elevated = elevated;
            read.Strips.AddRange(bands);
        }

        /// <summary>The stretches of a composition that can be painted, or stood on (null when it has no pieces), with its width and whether it is elevated; its pieces go to the report.</summary>
        private static List<PaintableStretch> Stretches(EntityManager em, PrefabSystem prefabs, Entity composition, ulong print, out float width, out bool elevated, StringBuilder report, string what, bool standable)
        {
            width = 0f;
            elevated = false;
            if (composition == Entity.Null || !em.TryGetComponent(composition, out NetCompositionData data) || !em.TryGetBuffer(composition, true, out DynamicBuffer<NetCompositionPiece> buffer))
                return null;
            width = data.m_Width;
            elevated = (data.m_Flags.m_General & CompositionFlags.General.Elevated) != 0;
            // Kept by the composition and the rule with the print of what they were made from (CompositionPrint); the stretches are shared and read only. A logged read makes them and keeps nothing.
            var key = (composition, Mode(standable));
            if (report == null && s_Stretches.TryGet(key, out StretchSet kept) && kept.Print == print)
                return kept.Stretches;
            List<SectionPiece> pieces = Pieces(em, buffer, report != null ? prefabs : null);
            var lines = report != null ? new List<string>() : null;
            List<PaintableStretch> stretches = s_Tracks ? SurfaceSection.StandableTrack(pieces, width, lines) : standable ? SurfaceSection.Standable(pieces, width, lines) : SurfaceSection.Paintable(pieces, width, lines);
            if (report != null)
            {
                report.Append(string.Format(CultureInfo.InvariantCulture, "\n  {0}: width {1:0.00} m{3}, {2} piece(s), {4} across ", what, width, pieces.Count, elevated ? " (elevated)" : "", standable ? "stand" : "paint"));
                report.Append(stretches.Count == 0 ? "nothing" : string.Join(", ", stretches.ConvertAll(s => string.Format(CultureInfo.InvariantCulture, "{0:0.000}..{1:0.000}", s.F0, s.F1))));
                foreach (string line in lines)
                    report.Append("\n    ").Append(line);
            }
            else
            {
                s_Stretches.Set(key, new StretchSet { Print = print, Stretches = stretches });
            }

            return stretches;
        }

        /// <summary>
        /// The kerbs inside the game's piece meshes (Kerbs.FacesOf, in the piece's own frame), by piece prefab; read once a load, the first time a piece is met. A piece without a readable mesh has none, and
        /// that is kept; a piece the game is still loading is not (GeometryGate): it is taken as one surface for now and tried again (Poll), and the reads that took it so are provisional (Read.Pending).
        /// </summary>
        private static readonly FaceBook<Entity, Kerbs.PieceFaces> s_Faces = new FaceBook<Entity, Kerbs.PieceFaces>();

        private static int s_Provisional, s_BusyLogged, s_PolledFrame = -1;

        internal static void ClearFaces() => s_Faces.Clear();

        /// <summary>How many road pieces the game is still loading that a read needed (see Poll), and the count that moves each time one of them becomes known.</summary>
        internal static int KerbBusy => s_Faces.BusyNow;

        internal static int KerbEpoch => s_Faces.Epoch;

        /// <summary>How many reads were made with a kerb piece taken as one surface because the game was still loading it: the count a caller sees move while it makes an item is a sign that the item is provisional.</summary>
        internal static int ProvisionalReads => s_Provisional;

        /// <summary>Tries the pieces the game was still loading again, once a frame at the most; the systems that read the roads call it every frame there is a piece waiting.</summary>
        internal static void Poll(EntityManager em)
        {
            if (s_Faces.BusyNow == 0 || s_PolledFrame == UnityEngine.Time.frameCount)
                return;
            s_PolledFrame = UnityEngine.Time.frameCount;
            s_Faces.Poll(piece => Resolve(em, piece));
        }

        /// <summary>For the log: how many pieces were read, how many were busy when first asked for, how many are now, how many reads were provisional.</summary>
        internal static string KerbSummary() => $"kerb pieces: {s_Faces.Count} known, {s_Faces.Deferred} were still loading by the game when first asked for, {s_Faces.BusyNow} still are, {s_Provisional} read(s) taken them as one surface";

        /// <summary>
        /// The pieces of a composition's cross-section (with their prefabs' names when prefabs is given, for the log). A surface piece without lanes whose surface has a step (a footway: the game keeps the
        /// strip at the carriageway's level in front of its kerb inside it, 0.4 m, 1.5 m with a cycle lane, 2 m with parking) is split at the kerbs of its mesh (0.12.37, Kerbs.Split): until then the whole
        /// piece was at the footway's height, and paint over that strip (a line by a cycle lane), a kerb line's foot and a barrier there stood 20 cm up.
        /// </summary>
        internal static List<SectionPiece> Pieces(EntityManager em, DynamicBuffer<NetCompositionPiece> buffer, PrefabSystem prefabs) => Pieces(em, buffer, prefabs, out _);

        /// <summary>As above; complete is false when a kerb piece the game was still loading was taken as one surface (the pieces are provisional: Poll and read again).</summary>
        internal static List<SectionPiece> Pieces(EntityManager em, DynamicBuffer<NetCompositionPiece> buffer, PrefabSystem prefabs, out bool complete)
        {
            complete = true;
            var pieces = new List<SectionPiece>(buffer.Length + 2);
            for (int i = 0; i < buffer.Length; i++)
            {
                NetCompositionPiece p = buffer[i];
                float top = p.m_Offset.y;
                if (em.TryGetComponent(p.m_Piece, out NetPieceData data))
                    top += math.cmax(data.m_SurfaceHeights);

                var piece = new SectionPiece
                {
                    X = p.m_Offset.x,
                    Width = p.m_Size.x,
                    Top = top,
                    Surface = (p.m_PieceFlags & NetPieceFlags.Surface) != 0,
                    BlocksTraffic = (p.m_PieceFlags & NetPieceFlags.BlockTraffic) != 0,
                    Layer = (p.m_PieceFlags & (NetPieceFlags.Side | NetPieceFlags.Top | NetPieceFlags.Bottom)) != 0,
                    OtherLevel = (p.m_SectionFlags & (NetSectionFlags.Underground | NetSectionFlags.Overhead | NetSectionFlags.Hidden | NetSectionFlags.HiddenSurface)) != 0,
                    RoadLanes = (p.m_PieceFlags & NetPieceFlags.HasRoadLanes) != 0,
                    Name = prefabs != null ? NameOf(prefabs, p.m_Piece) : null
                };
                if (NeedsFaces(em, p))
                {
                    if (FacesOf(em, p.m_Piece, out Kerbs.PieceFaces faces))
                    {
                        Kerbs.Split(piece, faces.Placed((p.m_SectionFlags & NetSectionFlags.Invert) != 0, p.m_Offset.x, p.m_Offset.y), pieces);
                        continue;
                    }

                    complete = false;
                }

                pieces.Add(piece);
            }

            if (!complete)
                s_Provisional++;
            return pieces;
        }

        /// <summary>A surface piece without lanes whose surface has a step: the pieces Pieces splits at the kerbs of their meshes.</summary>
        private static bool NeedsFaces(EntityManager em, in NetCompositionPiece p) =>
            em.TryGetComponent(p.m_Piece, out NetPieceData data) && math.cmax(data.m_SurfaceHeights) - math.cmin(data.m_SurfaceHeights) >= Kerbs.MinRise
            && (p.m_PieceFlags & NetPieceFlags.Surface) != 0 && (p.m_PieceFlags & (NetPieceFlags.Side | NetPieceFlags.Top | NetPieceFlags.Bottom)) == 0
            && (p.m_SectionFlags & (NetSectionFlags.Underground | NetSectionFlags.Overhead | NetSectionFlags.Hidden | NetSectionFlags.HiddenSurface)) == 0 && (p.m_PieceFlags & NetPieceFlags.HasRoadLanes) == 0;

        /// <summary>The piece's kerbs, read the first time it is met; false when the game is still loading its geometry (they are then not known: see s_Faces).</summary>
        private static bool FacesOf(EntityManager em, Entity prefab, out Kerbs.PieceFaces faces)
        {
            if (s_Faces.TryGetKnown(prefab, out faces))
                return true;
            return s_Faces.TryResolve(prefab, piece => Resolve(em, piece), out faces);
        }

        private static (PieceRead Kind, Kerbs.PieceFaces Faces) Resolve(EntityManager em, Entity prefab)
        {
            long lap = System.Diagnostics.Stopwatch.GetTimestamp();
            PieceRead kind = ReadMesh(em, prefab, out List<Vec3> points);
            if (kind != PieceRead.Busy)
                SlowLog.Since("SurfaceReader: a road piece's kerb mesh read (first time)", lap);
            return (kind, Kerbs.FacesOf(points));
        }

        internal static GeometryState StateOf(GeometryAsset asset)
        {
            ref GeometryAsset.Loading loading = ref asset.loading;
            return new GeometryState(loading.m_AsyncLoadingScheduled, loading.m_AsyncLoadingStarted, loading.m_AsyncLoadingDone, loading.m_HeaderLoaded, asset.data.IsValid, asset.state == LoadState.Object);
        }

        /// <summary>
        /// The vertices of a piece's meshes in its own frame, one of each place (mm across and up, 0.1 m along): Ready; NoData for a piece without a readable mesh; Busy when the game is still loading
        /// the piece's geometry and nothing was read (GeometryGate: ObtainMeshes and ReleaseMeshes would break that loading for good).
        /// </summary>
        private static PieceRead ReadMesh(EntityManager em, Entity prefab, out List<Vec3> points)
        {
            points = null;
            PrefabSystem prefabs = em.World.GetOrCreateSystemManaged<PrefabSystem>();
            if (!prefabs.TryGetPrefab(prefab, out NetPiecePrefab piece) || piece == null || !piece.hasGeometryAsset)
                return PieceRead.NoData;
            GeometryAsset asset = piece.geometryAsset;
            GeometryState state = StateOf(asset);
            if (!GeometryGate.CanRead(state))
            {
                if (s_BusyLogged++ < 8)
                    Mod.log.Info($"Kerbs: the road piece \"{piece.name}\" is still being loaded by the game ({state}): its mesh is read when it is done.");
                return PieceRead.Busy;
            }

            UnityEngine.Mesh[] meshes;
            try
            {
                meshes = piece.ObtainMeshes();
            }
            catch (Exception ex)
            {
                // Some pieces have a geometry asset without vertex data, and the game asserts when it would decompress it (0.12.36). ObtainMeshes counts a holder only once its meshes are made, so there is
                // nothing to release; what the failed read loaded of an asset that was idle is let go, so that it is idle again for the game's own request. The piece is kept as one without kerbs.
                if (state.Idle)
                    asset.UnloadPartial(true);
                Mod.log.Warn($"Kerbs: the mesh of the road piece \"{piece.name}\" could not be read ({ex.GetType().Name}: {ex.Message}); it is taken as one surface.");
                return PieceRead.NoData;
            }

            if (meshes == null)
                return PieceRead.NoData;
            try
            {
                var seen = new HashSet<(int, int, int)>();
                var found = new List<Vec3>();
                foreach (UnityEngine.Mesh mesh in meshes)
                {
                    if (mesh == null || !mesh.isReadable)
                        continue;
                    foreach (UnityEngine.Vector3 v in mesh.vertices)
                    {
                        if (seen.Add(((int)Math.Round(v.x * 1000f), (int)Math.Round(v.y * 1000f), (int)Math.Round(v.z * 10f))))
                            found.Add(new Vec3(v.x, v.y, v.z));
                    }
                }

                if (found.Count == 0)
                    return PieceRead.NoData;
                points = found;
                return PieceRead.Ready;
            }
            finally
            {
                piece.ReleaseMeshes();
            }
        }

        private static string NameOf(PrefabSystem prefabs, Entity prefab)
        {
            try
            {
                return prefabs != null && prefabs.TryGetPrefab(prefab, out PrefabBase p) && p != null ? p.name : "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
