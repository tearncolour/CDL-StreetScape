using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// The heights of what is under a line of points in the game (SurfaceHeights over SurfaceReader and the terrain), for the tool's preview and for laying the placed markings on the ground
    /// (AreaFitSystem, MarkingGround). A barrier stands on what a post may stand on (the carriageway, a median, a footway, a deck); paint lies on the carriageway where there is one under the point and on
    /// those others only where there is not (a marking on a footway or a square). Main thread.
    /// </summary>
    internal sealed class StandHeights
    {
        private readonly SurfaceHeights m_Heights = new SurfaceHeights();
        private readonly Game.Net.SearchSystem m_NetSearch;
        private readonly Game.Prefabs.PrefabSystem m_Prefabs;
        private readonly TerrainSystem m_Terrain;
        private EntityManager m_EntityManager;
        private TerrainHeightData m_Ground;
        private bool m_GroundRead, m_Paint, m_Tracks;
        private int m_Warned;
        private Dictionary<SurfaceHeights.WindowRead, AreaFit> m_Prefetched;

        /// <summary>Called with the job and its fit after each cut made here on the main thread (the shadow of the worker compares them).</summary>
        internal Action<FitJob, FitResult> Observer;

        /// <summary>How many reads of a Put that was given prefetched cuts were not among them and were read and cut here.</summary>
        internal int Missed { get; private set; }

        internal StandHeights(World world)
        {
            m_NetSearch = world.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
            m_Prefabs = world.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
            m_Terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
        }

        internal int Reads => m_Heights.Reads;

        internal int KeptWindows => m_Heights.KeptWindows;

        internal void Clear() => m_Heights.Clear();

        /// <summary>
        /// The line with each point at the height of what is under it (SurfaceHeights.Put: key names the line in the cache, the first pinned points of it are fixed). paint: the carriageway first, as above.
        /// </summary>
        internal List<Vec3> Put(EntityManager em, List<Vec3> line, string key, int pinned, bool paint, bool tracks = false)
        {
            if (line == null || line.Count == 0)
                return line;
            m_EntityManager = em;
            // A line along a railway, a metro or a tram line (0.12.66) stands on what is there; paint is for roads only.
            m_Tracks = tracks;
            m_Paint = paint && !tracks;
            m_GroundRead = false;
            return m_Heights.Put(line, (tracks ? "t|" : paint ? "p|" : "s|") + key, pinned, ReadSurface, GroundAt, !m_Paint);
        }

        /// <summary>
        /// The reads Put would make for this line (the windows that are not kept): the caller reads the roads under each (StripsOf, a few at a time), has them cut elsewhere and gives the fits to Put
        /// (prefetched). The state of the line's windows is not changed.
        /// </summary>
        internal List<SurfaceHeights.WindowRead> Windows(List<Vec3> line, string key, int pinned, bool paint, bool tracks = false)
        {
            var windows = new List<SurfaceHeights.WindowRead>();
            if (line == null || line.Count == 0)
                return windows;
            m_Heights.Windows(line, (tracks ? "t|" : paint ? "p|" : "s|") + key, pinned, !(paint && !tracks), windows);
            return windows;
        }

        /// <summary>The strips of a window's read, from the game's roads now (main thread); an empty list when there is no surface under it. Throws what the read throws.</summary>
        internal List<SurfaceStrip> StripsOf(EntityManager em, SurfaceHeights.WindowRead window, bool paint, bool tracks = false)
        {
            m_EntityManager = em;
            m_Tracks = tracks;
            m_Paint = paint && !tracks;
            return StripsFor(window.MinX, window.MinZ, window.MaxX, window.MaxZ);
        }

        /// <summary>As Put, with the fits of its reads made beforehand (by Prepare's windows): a read that is among them is not read again; one that is not (a window kept then and lost since) is read and cut here. A null fit is a read of no surface.</summary>
        internal List<Vec3> Put(EntityManager em, List<Vec3> line, string key, int pinned, bool paint, Dictionary<SurfaceHeights.WindowRead, AreaFit> prefetched, bool tracks = false)
        {
            m_Prefetched = prefetched;
            try
            {
                return Put(em, line, key, pinned, paint, tracks);
            }
            finally
            {
                m_Prefetched = null;
            }
        }

        /// <summary>The strips a read of the box is cut from: what a post may stand on, and for paint the carriageway's own too (the stand ones then only fill).</summary>
        private List<SurfaceStrip> StripsFor(float minX, float minZ, float maxX, float maxZ)
        {
            SurfaceReader.Read stand = SurfaceReader.Around(m_EntityManager, m_NetSearch, m_Prefabs, minX, minZ, maxX, maxZ, true, false, m_Tracks);
            var strips = new List<SurfaceStrip>(stand.Strips.Count);
            if (m_Paint)
            {
                // What a post stands on only fills what the paintable carriageway leaves (SurfaceStrip.Filler): a point by a kerb takes the carriageway's height, not the footway's beside it.
                // The strips of a read are shared with the cache: the filler ones are copies.
                strips.AddRange(SurfaceReader.Around(m_EntityManager, m_NetSearch, m_Prefabs, minX, minZ, maxX, maxZ).Strips);
                foreach (SurfaceStrip strip in stand.Strips)
                    strips.Add(strip.AsFiller());
            }
            else
            {
                strips.AddRange(stand.Strips);
            }

            return strips;
        }

        private AreaFit ReadSurface(float minX, float minZ, float maxX, float maxZ, float originY)
        {
            if (m_Prefetched != null)
            {
                if (m_Prefetched.TryGetValue(new SurfaceHeights.WindowRead(minX, minZ, maxX, maxZ, originY), out AreaFit made))
                    return made;
                Missed++;
            }

            try
            {
                List<SurfaceStrip> strips = StripsFor(minX, minZ, maxX, maxZ);
                if (strips.Count == 0)
                    return null;
                var job = new FitJob { Heights = true, Strips = strips, Origin = new Vec3(0f, originY, 0f), MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
                FitResult cut = FitCut.Run(job);
                SurfaceReader.NoteFitMs(true, cut.CutMs);
                Observer?.Invoke(job, cut);
                return cut.Fit;
            }
            catch (Exception ex)
            {
                if (m_Warned++ < 5)
                    Mod.log.Warn("The road's surface could not be read; the line keeps its heights: " + ex.Message);
                return null;
            }
        }

        private float GroundAt(float x, float y, float z)
        {
            if (!m_GroundRead)
            {
                m_Ground = TerrainWait.HeightData(m_Terrain);
                m_GroundRead = true;
            }

            return m_Ground.isCreated ? TerrainUtils.SampleHeight(ref m_Ground, new float3(x, y, z)) : float.NaN;
        }
    }
}
