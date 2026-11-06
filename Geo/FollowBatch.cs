using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The items of a few roads followed together (0.12.74 follow work): the relations, where the game has the items and where the follow puts them are kept as arrays of numbers (struct of arrays, grown by
    /// doubling and reused with Clear: nothing is made per item, no closure, no list of objects), and Run goes over a slice of them with the table of each edge's new curve. An edge whose curve went to its new one by a
    /// translation and a turn (RigidMotion) takes its items by that motion, from where the relation put them on the curve it was measured on (Base): no search, no evaluation of the curve. The others are made as
    /// RoadRelation.TryApply makes them. The rules of Follow (Detached, Unmeasurable, Unchanged, Moved) are the same as RoadRelation.Follow's. One owner at a time; on a worker it is filled, run and read there.
    /// </summary>
    public sealed class FollowBatch
    {
        private struct Edge
        {
            public FollowCurve Baseline, Now;
            public bool Rigid;
            public RigidMotion Motion;
        }

        private Edge[] m_Edges = new Edge[8];
        private int m_EdgeCount;
        private int m_Count;

        // The relation, the place it put the item on the curve it was measured on, where the follow put the item last, and where the game has it now.
        private float[] m_Fraction, m_Length, m_Along, m_Lateral, m_Height, m_RelYaw;
        private float[] m_BaseX, m_BaseY, m_BaseZ, m_BaseYaw;
        private float[] m_PlacedX, m_PlacedY, m_PlacedZ, m_PlacedYaw;
        private float[] m_CurX, m_CurY, m_CurZ, m_CurYaw;
        private int[] m_EdgeOf;
        private float[] m_OutX, m_OutY, m_OutZ, m_OutYaw;
        private FollowOutcome[] m_Outcome;

        /// <summary>How many items were taken by a rigid motion, and how many by the full follow, since Clear.</summary>
        public int RigidItems { get; private set; }

        public int FullItems { get; private set; }

        public int Count => m_Count;

        public int EdgeCount => m_EdgeCount;

        public FollowBatch(int capacity = 64) => Grow(Math.Max(4, capacity));

        /// <summary>Forgets the items and the edges; the arrays are kept.</summary>
        public void Clear()
        {
            for (int i = 0; i < m_EdgeCount; i++)
                m_Edges[i] = default;
            m_EdgeCount = 0;
            m_Count = 0;
            RigidItems = FullItems = 0;
        }

        /// <summary>A road: the table of the curve its items' relations were measured on. The index to give Add and SetNow.</summary>
        public int AddEdge(FollowCurve baseline)
        {
            if (m_EdgeCount == m_Edges.Length)
                Array.Resize(ref m_Edges, m_Edges.Length * 2);
            m_Edges[m_EdgeCount] = new Edge { Baseline = baseline, Now = baseline };
            return m_EdgeCount++;
        }

        /// <summary>The road's curve now. With detectRigid the motion from the baseline to it is looked for (RigidMotion.TryDetect) and used for the items when there is one. True when the edge is taken as rigid.</summary>
        public bool SetNow(int edge, FollowCurve now, bool detectRigid)
        {
            Edge e = m_Edges[edge];
            e.Now = now;
            e.Rigid = detectRigid && e.Baseline != null && now != null && RigidMotion.TryDetect(e.Baseline.Curve, now.Curve, RigidMotion.Tolerance, out e.Motion);
            m_Edges[edge] = e;
            return e.Rigid;
        }

        /// <summary>
        /// An item of a road: its relation, the place the relation put it on the baseline curve (its place when it was measured: what a rigid motion carries), and where the game has it and its yaw now. The
        /// index of the item.
        /// </summary>
        public int Add(int edge, in RoadRelation relation, Vec3 baseAt, float baseYaw, Vec3 current, float currentYaw)
        {
            if (m_Count == m_Fraction.Length)
                Grow(m_Count * 2);
            int i = m_Count++;
            m_EdgeOf[i] = edge;
            m_Fraction[i] = relation.Fraction;
            m_Length[i] = relation.Length;
            m_Along[i] = relation.Along;
            m_Lateral[i] = relation.Lateral;
            m_Height[i] = relation.Height;
            m_RelYaw[i] = relation.Yaw;
            m_BaseX[i] = baseAt.X;
            m_BaseY[i] = baseAt.Y;
            m_BaseZ[i] = baseAt.Z;
            m_BaseYaw[i] = baseYaw;
            m_PlacedX[i] = relation.PlacedAt.X;
            m_PlacedY[i] = relation.PlacedAt.Y;
            m_PlacedZ[i] = relation.PlacedAt.Z;
            m_PlacedYaw[i] = relation.PlacedYaw;
            m_CurX[i] = current.X;
            m_CurY[i] = current.Y;
            m_CurZ[i] = current.Z;
            m_CurYaw[i] = currentYaw;
            return i;
        }

        /// <summary>What the follow found for an item after Run: its outcome, and the place and yaw to write when Moved (the item's own place otherwise).</summary>
        public FollowOutcome Result(int item, out Vec3 position, out float yaw)
        {
            position = new Vec3(m_OutX[item], m_OutY[item], m_OutZ[item]);
            yaw = m_OutYaw[item];
            return m_Outcome[item];
        }

        /// <summary>The edge of an item.</summary>
        public int EdgeOf(int item) => m_EdgeOf[item];

        /// <summary>Follows the items from..from+count-1 (a slice, so that a frame's budget can take some of them): the outcome, the place and the yaw of each.</summary>
        public void Run(FollowMode mode, int from, int count)
        {
            int end = Math.Min(m_Count, from + Math.Max(0, count));
            for (int i = Math.Max(0, from); i < end; i++)
            {
                m_OutX[i] = m_CurX[i];
                m_OutY[i] = m_CurY[i];
                m_OutZ[i] = m_CurZ[i];
                m_OutYaw[i] = m_CurYaw[i];
                float fx = m_CurX[i] - m_PlacedX[i], fy = m_CurY[i] - m_PlacedY[i], fz = m_CurZ[i] - m_PlacedZ[i];
                if (m_Length[i] < RoadRelation.MinLength || Math.Sqrt(fx * fx + fy * fy + fz * fz) > RoadRelation.PlacedTolerance || Math.Abs(FollowCurve.Fold(m_CurYaw[i] - m_PlacedYaw[i])) > 1e-3f)
                {
                    m_Outcome[i] = FollowOutcome.Detached;
                    continue;
                }

                Edge edge = m_Edges[m_EdgeOf[i]];
                Vec3 at;
                float turned;
                if (edge.Now == null || !edge.Now.IsUsable)
                {
                    m_Outcome[i] = FollowOutcome.Unmeasurable;
                    continue;
                }

                if (edge.Rigid)
                {
                    at = edge.Motion.Map(new Vec3(m_BaseX[i], m_BaseY[i], m_BaseZ[i]));
                    turned = edge.Motion.MapYaw(m_BaseYaw[i]);
                    RigidItems++;
                }
                else
                {
                    if (!edge.Now.Place(m_Fraction[i], m_Length[i], m_Along[i], m_Lateral[i], m_Height[i], m_RelYaw[i], mode, out at, out turned))
                    {
                        m_Outcome[i] = FollowOutcome.Unmeasurable;
                        continue;
                    }

                    FullItems++;
                }

                float dx = at.X - m_CurX[i], dy = at.Y - m_CurY[i], dz = at.Z - m_CurZ[i];
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) <= RoadRelation.WriteTolerance && Math.Abs(FollowCurve.Fold(turned - m_CurYaw[i])) <= 1e-4f)
                {
                    m_Outcome[i] = FollowOutcome.Unchanged;
                    continue;
                }

                m_OutX[i] = at.X;
                m_OutY[i] = at.Y;
                m_OutZ[i] = at.Z;
                m_OutYaw[i] = turned;
                m_Outcome[i] = FollowOutcome.Moved;
            }
        }

        private void Grow(int capacity)
        {
            Array.Resize(ref m_Fraction, capacity);
            Array.Resize(ref m_Length, capacity);
            Array.Resize(ref m_Along, capacity);
            Array.Resize(ref m_Lateral, capacity);
            Array.Resize(ref m_Height, capacity);
            Array.Resize(ref m_RelYaw, capacity);
            Array.Resize(ref m_BaseX, capacity);
            Array.Resize(ref m_BaseY, capacity);
            Array.Resize(ref m_BaseZ, capacity);
            Array.Resize(ref m_BaseYaw, capacity);
            Array.Resize(ref m_PlacedX, capacity);
            Array.Resize(ref m_PlacedY, capacity);
            Array.Resize(ref m_PlacedZ, capacity);
            Array.Resize(ref m_PlacedYaw, capacity);
            Array.Resize(ref m_CurX, capacity);
            Array.Resize(ref m_CurY, capacity);
            Array.Resize(ref m_CurZ, capacity);
            Array.Resize(ref m_CurYaw, capacity);
            Array.Resize(ref m_EdgeOf, capacity);
            Array.Resize(ref m_OutX, capacity);
            Array.Resize(ref m_OutY, capacity);
            Array.Resize(ref m_OutZ, capacity);
            Array.Resize(ref m_OutYaw, capacity);
            Array.Resize(ref m_Outcome, capacity);
        }
    }
}
