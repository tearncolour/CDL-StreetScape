using CDLC.TS.Geo;
using CDLC.TS.Snap;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>
    /// What the tool has read about a road edge, kept for a few seconds (0.7.1). The tool asks for the curve and width of the road under the cursor, its speed limit, whether it runs by the highway rules
    /// and its lanes at every frame the cursor is over it; each is a walk over the edge's sub-lanes with an entity read or three for each lane (and the lanes' report builds strings), and none of it changes
    /// while the player moves the mouse. The parts are read when first asked for. The curve table (ArcTable) is made once instead of three times a frame. Main thread.
    /// </summary>
    internal sealed class EdgeInfo
    {
        /// <summary>How long what was read is trusted (s): a road is changed by the player, a few times a minute at the most, and a new edge is a new entity.</summary>
        internal const float Ttl = 3f;

        private static readonly ExpiringCache<Entity, EdgeInfo> s_Cache = new ExpiringCache<Entity, EdgeInfo>(Ttl, 48);

        internal Entity Edge;
        internal bool CurveOk;
        internal Bezier3 Curve;
        internal float Width;
        internal ArcTable Arc;

        private bool m_SpeedRead, m_SpeedOk, m_LanesRead, m_LanesOk;
        private float m_Kmh;
        private RoadReader.SpeedReport m_Speed;
        private int m_Forward, m_Backward;
        private RoadReader.LanesReport m_Lanes;

        /// <summary>The information of an edge, from the cache or read now.</summary>
        internal static EdgeInfo Of(EntityManager em, Entity edge)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            return s_Cache.Get(edge, now, () => Read(em, edge));
        }

        /// <summary>Forgets everything (a city is loaded or left).</summary>
        internal static void Reset() => s_Cache.Clear();

        internal static int Hits => s_Cache.Hits;

        internal static int Misses => s_Cache.Misses;

        private static EdgeInfo Read(EntityManager em, Entity edge)
        {
            var info = new EdgeInfo { Edge = edge };
            if (RoadReader.TryRead(em, edge, out Bezier3 curve, out float width))
            {
                info.CurveOk = true;
                info.Curve = curve;
                info.Width = width;
                info.Arc = new ArcTable(curve);
            }

            return info;
        }

        /// <summary>The speed limit (km/h) and the raw figures it was read from (RoadReader.TrySpeedKmh).</summary>
        internal bool TrySpeed(EntityManager em, out float kmh, out RoadReader.SpeedReport report)
        {
            if (!m_SpeedRead)
            {
                m_SpeedRead = true;
                m_SpeedOk = RoadReader.TrySpeedKmh(em, Edge, out m_Kmh, out m_Speed);
            }

            kmh = m_Kmh;
            report = m_Speed;
            return m_SpeedOk;
        }

        private bool m_LateralsRead, m_LateralsOk;
        private readonly System.Collections.Generic.List<float> m_LateralsForward = new System.Collections.Generic.List<float>();
        private readonly System.Collections.Generic.List<float> m_LateralsBackward = new System.Collections.Generic.List<float>();

        /// <summary>
        /// Where the car lanes are across the edge (m right of its centre line), those that run with the edge and those against it (RoadReader.TryLaneLaterals): the places a road marking is put at. Read once
        /// for the edge and kept with the rest; the lists are the cache's, not to be changed.
        /// </summary>
        internal bool TryLaterals(EntityManager em, out System.Collections.Generic.List<float> forward, out System.Collections.Generic.List<float> backward)
        {
            if (!m_LateralsRead)
            {
                m_LateralsRead = true;
                m_LateralsOk = RoadReader.TryLaneLaterals(em, Edge, m_LateralsForward, m_LateralsBackward);
            }

            forward = m_LateralsForward;
            backward = m_LateralsBackward;
            return m_LateralsOk;
        }

        private Carriageway m_Carriageway;

        /// <summary>Where the carriageway's edges (the kerbs) are and whether a footway lies beyond each (Carriageway), read once for the edge; one that is not Ok when the lanes could not be read.</summary>
        internal Carriageway Carriageway(EntityManager em)
        {
            if (m_Carriageway == null)
            {
                var lanes = new System.Collections.Generic.List<CrossLane>();
                RoadReader.TryCrossLanes(em, Edge, lanes);
                m_Carriageway = Geo.Carriageway.Of(lanes, Width);
            }

            return m_Carriageway;
        }

        /// <summary>The distinct car lanes each way and the report of how they were told apart (RoadReader.TryLanes).</summary>
        internal bool TryLanes(EntityManager em, out int forward, out int backward, out RoadReader.LanesReport report)
        {
            if (!m_LanesRead)
            {
                m_LanesRead = true;
                m_LanesOk = RoadReader.TryLanes(em, Edge, out m_Forward, out m_Backward, out m_Lanes);
            }

            forward = m_Forward;
            backward = m_Backward;
            report = m_Lanes;
            return m_LanesOk;
        }
    }
}
