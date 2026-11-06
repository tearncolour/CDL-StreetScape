using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What the road check does with one item (EdgeHint.Decide).</summary>
    public enum EdgeCheck
    {
        /// <summary>The item stands beside the road its hint names: nothing to do.</summary>
        Beside,

        /// <summary>The hint does not hold and a search for the nearest road is due.</summary>
        Scan,

        /// <summary>The hint does not hold, no road was within reach the last time, and it is not time to look again.</summary>
        Wait
    }

    /// <summary>
    /// The pure part of the road check (ItemRoadSystem): when an item looks for its road again, and how it finds the nearest one without measuring every road of the city. Until 0.6.0 an item that
    /// was not beside its road was measured against every edge of the map (a few tens of thousands, each a curve search of about 60 evaluations: some 40 ms for one item), and an item with no road
    /// in reach (one placed on open ground, or already marked an orphan) did that again every round, for ever: with a few dozen of them the check took 0.6 to 3.3 s of the main thread a round,
    /// every 1.5 s (the slow-frame log of the 0.6.0 session: 62 rounds, 1875 ms on average).
    /// </summary>
    public static class EdgeHint
    {
        /// <summary>How far from the centre line of an edge an item may stand and still count as beside it (on top of half the width), and how far a search for another road looks (m).</summary>
        public const float Beside = 12f;

        /// <summary>
        /// How far past the end of a road (m) an item may stand and still be taken over by that road when the road it stood by was deleted (TakesOver). A road that replaces the old one (a split, two edges
        /// joined) covers the item along its length or ends at the item; one that only has the deleted road's place beyond its end does not take over what stood on that place.
        /// </summary>
        public const float TakeOverReach = 6f;

        /// <summary>The nearest point of a road is "an end" when its parameter is within this of 0 or 1 (the search by the boxes of the nearest point ends within a millionth of it).</summary>
        public const float EndT = 0.001f;

        /// <summary>Whether a place is beside a road: within half the road's width and Beside of its centre line (in the plane); where along the road its nearest point is (t), and how far from the line.</summary>
        public static bool IsBeside(Bezier3 curve, float width, Vec3 at, out float t, out float distance)
        {
            t = curve.ClosestT(at, 8);
            distance = Vec3.DistanceXZ(curve.Point(t), at);
            return distance <= width * 0.5f + Beside;
        }

        /// <summary>
        /// Whether a road that is still there takes over an item whose own road was deleted (0.12.100; the owner, 5 Nov 21:4x: a wall at the edge of a road 24 m wide stayed in the field when he deleted the
        /// road's last segment): it is beside the road (IsBeside), and not beyond its end by more than TakeOverReach. The wall was 22.8 m from the end of the segment before the deleted one, within the 24 m
        /// (half the width and Beside) that "beside" gave from that end point, and was relinked to it; it stood 19.4 m past the end along the road. overrun is how far past an end the place is along the
        /// road's direction there (0 where the nearest point of the road is not an end).
        /// </summary>
        public static bool TakesOver(Bezier3 curve, float width, Vec3 at, out float t, out float distance, out float overrun)
        {
            overrun = 0f;
            if (!IsBeside(curve, width, at, out t, out distance))
                return false;
            if (t > EndT && t < 1f - EndT)
                return true;
            // The nearest point is an end: how far the place is beyond it, along the way the road points out of that end.
            Vec3 on = curve.Point(t);
            Vec3 outward = curve.Tangent(t).HeadingXZ() * (t <= EndT ? -1f : 1f);
            overrun = Math.Max(0f, (at.X - on.X) * outward.X + (at.Z - on.Z) * outward.Z);
            return overrun <= TakeOverReach;
        }

        /// <summary>How long an item that found no road in reach waits before it looks again (s): a road is built or moved rarely, and the item keeps its place meanwhile.</summary>
        public const float OrphanRetrySeconds = 60f;

        /// <summary>How long a table of the edges is used before it is made again (s).</summary>
        public const float TableSeconds = 20f;

        /// <summary>Decides what to do with an item: beside its road, to be searched for a road, or to wait.</summary>
        public static EdgeCheck Decide(bool beside, bool orphan, float now, float notBefore)
        {
            if (beside)
                return EdgeCheck.Beside;
            if (orphan && now < notBefore)
                return EdgeCheck.Wait;
            return EdgeCheck.Scan;
        }

        /// <summary>The nearest edge to a point within radius (XZ distance to the curve), by the boxes of the edges: -1 when there is none. The curve of a candidate is asked for by index.</summary>
        public static int Nearest(EdgeBoxes boxes, Func<int, Bezier3> curveOf, float x, float z, float radius, List<int> scratch, out float t, out float distance) =>
            Nearest(boxes, curveOf, x, float.NaN, z, radius, scratch, out t, out distance);

        [ThreadStatic]
        private static float[] s_Distance, s_Height, s_Along;

        /// <summary>
        /// As above, and of edges that lie over one another the one at the point's height (RoadLevels: a post on a viaduct is by the viaduct, not by the road on the ground under it); a point with no height
        /// (NaN) takes the nearest in the plane.
        /// </summary>
        public static int Nearest(EdgeBoxes boxes, Func<int, Bezier3> curveOf, float x, float y, float z, float radius, List<int> scratch, out float t, out float distance)
        {
            t = 0f;
            distance = radius;
            scratch.Clear();
            boxes.Candidates(x, z, radius, scratch);
            if (s_Distance == null || s_Distance.Length < scratch.Count)
            {
                int size = Math.Max(16, scratch.Count);
                s_Distance = new float[size];
                s_Height = new float[size];
                s_Along = new float[size];
            }

            var at = new Vec3(x, 0f, z);
            int found = 0;
            for (int i = 0; i < scratch.Count; i++)
            {
                Bezier3 curve = curveOf(scratch[i]);
                float ct = curve.ClosestT(at, 8);
                Vec3 on = curve.Point(ct);
                float d = Vec3.DistanceXZ(on, at);
                if (!(d < radius))
                    continue;
                // The candidates that are in reach, in the order of the boxes: scratch is kept for the ones that are (the index of the edge), the rest of the arrays are by position in it.
                scratch[found] = scratch[i];
                s_Distance[found] = d;
                s_Height[found] = on.Y;
                s_Along[found] = ct;
                found++;
            }

            int pick = RoadLevels.Pick(s_Distance, s_Height, found, y);
            if (pick < 0)
                return -1;
            distance = s_Distance[pick];
            t = s_Along[pick];
            return scratch[pick];
        }
    }

    /// <summary>
    /// The boxes (in the XZ plane) of a list of edges, to throw away the edges that cannot be near a point before the curve is measured. The box of a Bezier curve's four control points holds the curve,
    /// so an edge outside the box grown by the radius is farther than the radius. Arrays that are reused: no allocation once they have grown to the size of the map.
    /// </summary>
    public sealed class EdgeBoxes
    {
        private float[] m_MinX = new float[1024], m_MaxX = new float[1024], m_MinZ = new float[1024], m_MaxZ = new float[1024];
        private int m_Count;

        public int Count => m_Count;

        public void Clear() => m_Count = 0;

        /// <summary>Adds the box of a Bezier curve given by its four control points (X and Z of each).</summary>
        public void Add(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz)
        {
            if (m_Count == m_MinX.Length)
            {
                int size = m_MinX.Length * 2;
                Array.Resize(ref m_MinX, size);
                Array.Resize(ref m_MaxX, size);
                Array.Resize(ref m_MinZ, size);
                Array.Resize(ref m_MaxZ, size);
            }

            m_MinX[m_Count] = Math.Min(Math.Min(ax, bx), Math.Min(cx, dx));
            m_MaxX[m_Count] = Math.Max(Math.Max(ax, bx), Math.Max(cx, dx));
            m_MinZ[m_Count] = Math.Min(Math.Min(az, bz), Math.Min(cz, dz));
            m_MaxZ[m_Count] = Math.Max(Math.Max(az, bz), Math.Max(cz, dz));
            m_Count++;
        }

        /// <summary>The indices of the edges whose box, grown by radius, holds the point.</summary>
        public void Candidates(float x, float z, float radius, List<int> into)
        {
            for (int i = 0; i < m_Count; i++)
            {
                if (x >= m_MinX[i] - radius && x <= m_MaxX[i] + radius && z >= m_MinZ[i] - radius && z <= m_MaxZ[i] + radius)
                    into.Add(i);
            }
        }
    }
}
