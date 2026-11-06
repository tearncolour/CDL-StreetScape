using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The heights a barrier's line stands on (0.12.20, kept window by window since 0.12.21): each point takes the height of the surface under it that a post can stand on, else the terrain's when that is
    /// within MaxGroundStep of the line's own height. The roads are read a window of points at a time, and the line of a run is made again at every half metre its end moves, so what the key pins is kept:
    /// a window whose points do not change is read once. The window the end is in is read with MovingReach metres to spare and that read is kept in one slot, so it is read again only when the end has
    /// left it. Where a short stretch of the line has no surface under it between two that have (the end of a segment, a junction's part that does not reach the edge, 0.12.60), its heights are the line
    /// between theirs and not the terrain's: the owner's line over a ramp's joint dipped 2.3 m into the ground under it and came up again. Main thread; the reading of the game's roads and terrain is the caller's.
    /// </summary>
    public sealed class SurfaceHeights
    {
        /// <summary>How many points of a line are read at once (about 60 m): the read of a diagonal road covers the box of its points, and what is cut out of it grows with the box's area.</summary>
        public const int Window = 30;

        /// <summary>How far (m) beyond the points of the window the end is in the read goes, so that the end can move on for a while without a new read.</summary>
        public const float MovingReach = 12f;

        /// <summary>How far (m) the terrain beside a road may be from the line for a barrier to stand on it; further (a bridge, an embankment) the line keeps its height.</summary>
        public const float MaxGroundStep = 3f;

        /// <summary>The longest stretch (m along the line) with no surface under it that is filled from the surfaces each side of it.</summary>
        public const float MaxHole = 24f;

        /// <summary>The longest stretch at the start or the end of the line (m) that is filled from the surface on its one side, by that surface's grade.</summary>
        public const float MaxEdgeHole = 12f;

        /// <summary>The steepest grade a filled stretch is given: the surface's own grade at the place it ends is read from a few points and need not be more than the road's.</summary>
        private const float kMaxFillGrade = 0.15f;

        /// <summary>How far (m) outside the surface a barrier's foot still takes the surface's height (0.12.63): the parapet or kerb of an elevated road, 0.35 m inside the road's edge, is not a surface a post stands on.</summary>
        public const float NearSurface = 2.5f;

        private const int kMaxKept = 4096;

        /// <summary>The heights of a window and which of its points have a surface under them.</summary>
        private sealed class Stretch
        {
            public List<Vec3> Heights;
            public bool[] On;
        }


        /// <summary>The surface in the box (world) as a fit made at the height originY (the heights it gives are relative to it); null when the roads give none.</summary>
        public delegate AreaFit ReadSurface(float minX, float minZ, float maxX, float maxZ, float originY);

        /// <summary>The box (world) of one read of the roads that Put makes, and the height its fit is made at: what ReadSurface is called with. The same numbers for the same window, so a read made beforehand is found by them.</summary>
        public readonly struct WindowRead : IEquatable<WindowRead>
        {
            public readonly float MinX, MinZ, MaxX, MaxZ, OriginY;

            public WindowRead(float minX, float minZ, float maxX, float maxZ, float originY)
            {
                MinX = minX;
                MinZ = minZ;
                MaxX = maxX;
                MaxZ = maxZ;
                OriginY = originY;
            }

            public bool Equals(WindowRead other) => MinX == other.MinX && MinZ == other.MinZ && MaxX == other.MaxX && MaxZ == other.MaxZ && OriginY == other.OriginY;

            public override bool Equals(object obj) => obj is WindowRead other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = MinX.GetHashCode();
                    h = h * 31 + MinZ.GetHashCode();
                    h = h * 31 + MaxX.GetHashCode();
                    h = h * 31 + MaxZ.GetHashCode();
                    return h * 31 + OriginY.GetHashCode();
                }
            }
        }

        private readonly Dictionary<string, Stretch> m_Kept = new Dictionary<string, Stretch>();
        private readonly HashSet<int> m_Seen = new HashSet<int>();
        private readonly List<int> m_Found = new List<int>();

        private string m_SlotKey;
        private AreaFit m_SlotFit;
        private float m_SlotOrigin, m_SlotMinX, m_SlotMaxX, m_SlotMinZ, m_SlotMaxZ;

        /// <summary>How many times the roads were read, over the life of this object (the tests and the log tell a kept window from a read one by it).</summary>
        public int Reads { get; private set; }

        public int KeptWindows => m_Kept.Count;

        /// <summary>
        /// The line with each point at the height it stands on. key names the line in the cache: the same key must mean the same points up to pinned (their number, from the first): all of them for a line
        /// that is fixed, all but the last for a line whose end follows the cursor (the grid of its points starts where it starts, and only the point at the end moves). ground gives the terrain's height at
        /// (x, y, z) or NaN. bridge: the stretches with no surface under them are filled from the surfaces beside them (a barrier's line); a line of paint keeps the terrain's heights there.
        /// </summary>
        public List<Vec3> Put(List<Vec3> line, string key, int pinned, ReadSurface read, Func<float, float, float, float> ground, bool bridge = true)
        {
            var result = new List<Vec3>(line.Count);
            var on = new List<bool>(line.Count);
            int index = 0;
            for (int from = 0; from < line.Count; from += Window, index++)
            {
                int to = Math.Min(line.Count, from + Window);
                bool settled = to <= pinned;
                string at = key + "|" + index;
                if (settled && m_Kept.TryGetValue(at, out Stretch kept) && kept.Heights.Count == to - from)
                {
                    result.AddRange(kept.Heights);
                    on.AddRange(kept.On);
                    continue;
                }

                Stretch window = Heights(line, from, to, at, settled, read, ground, bridge);
                if (settled)
                {
                    if (m_Kept.Count >= kMaxKept)
                        m_Kept.Clear();
                    m_Kept[at] = window;
                }

                result.AddRange(window.Heights);
                on.AddRange(window.On);
            }

            if (!bridge)
                return result;
            Bridge(result, on);
            FootProfile.Smooth(result, on);
            return result;
        }

        /// <summary>
        /// The stretches of the line with no surface under them filled from the surfaces that are (in place; windows are read apart, so this is done on the whole line): between two surfaces, the line
        /// between their heights (up to MaxHole); at the start or the end, the grade of the surface next to it (up to MaxEdgeHole). A line with no surface anywhere, or a stretch longer than that, keeps
        /// what it was given (the terrain's).
        /// </summary>
        private static List<Vec3> Bridge(List<Vec3> line, List<bool> on)
        {
            int n = line.Count;
            if (n < 2)
                return line;
            var along = new float[n];
            for (int i = 1; i < n; i++)
                along[i] = along[i - 1] + Vec3.DistanceXZ(line[i - 1], line[i]);
            int at = 0;
            while (at < n)
            {
                if (on[at])
                {
                    at++;
                    continue;
                }

                int a = at;
                while (at < n && !on[at])
                    at++;
                int b = at - 1;
                bool before = a > 0, after = b < n - 1;
                if (before && after)
                {
                    float span = along[b + 1] - along[a - 1];
                    if (span > MaxHole)
                        continue;
                    for (int k = a; k <= b; k++)
                    {
                        SetHeight(line, k, line[a - 1].Y + (line[b + 1].Y - line[a - 1].Y) * (span > 1e-4f ? (along[k] - along[a - 1]) / span : 0f));
                        on[k] = true;
                    }
                }
                else if (before && along[b] - along[a - 1] <= MaxEdgeHole)
                {
                    float grade = GradeAt(line, along, on, a - 1, -1);
                    for (int k = a; k <= b; k++)
                    {
                        SetHeight(line, k, line[a - 1].Y + grade * (along[k] - along[a - 1]));
                        on[k] = true;
                    }
                }
                else if (after && along[b + 1] - along[a] <= MaxEdgeHole)
                {
                    float grade = GradeAt(line, along, on, b + 1, 1);
                    for (int k = a; k <= b; k++)
                    {
                        SetHeight(line, k, line[b + 1].Y + grade * (along[k] - along[b + 1]));
                        on[k] = true;
                    }
                }
            }

            return line;
        }

        private static void SetHeight(List<Vec3> line, int index, float y) => line[index] = new Vec3(line[index].X, y, line[index].Z);

        /// <summary>The grade of the surface at point from, read over the next few points of it in the direction dir (up to 3, as far as they have a surface under them); 0 when there is none.</summary>
        private static float GradeAt(List<Vec3> line, float[] along, List<bool> on, int from, int dir)
        {
            int last = from;
            for (int step = 1; step <= 3; step++)
            {
                int k = from + dir * step;
                if (k < 0 || k >= line.Count || !on[k])
                    break;
                last = k;
            }

            float run = along[last] - along[from];
            if (Math.Abs(run) < 0.5f)
                return 0f;
            return Math.Max(-kMaxFillGrade, Math.Min(kMaxFillGrade, (line[last].Y - line[from].Y) / run));
        }

        /// <summary>
        /// The reads of the roads that Put(line, key, pinned, ..., bridge) would make now, in its order, added to into: the windows it has kept (a settled window that is in the cache) are not among them.
        /// Nothing is read or kept. For a line that is fixed (pinned is its length) these are all the reads it makes; the window of a moving end is listed whether or not the slot would serve it (the
        /// tool's preview does not ask).
        /// </summary>
        public void Windows(List<Vec3> line, string key, int pinned, bool bridge, List<WindowRead> into)
        {
            int index = 0;
            for (int from = 0; from < line.Count; from += Window, index++)
            {
                int to = Math.Min(line.Count, from + Window);
                bool settled = to <= pinned;
                if (settled && m_Kept.TryGetValue(key + "|" + index, out Stretch kept) && kept.Heights.Count == to - from)
                    continue;
                into.Add(ReadOf(line, from, to, settled, bridge, out _, out _, out _, out _));
            }
        }

        /// <summary>The read of the window from..to of a line: its box (the points' own, then widened by what the read reaches beyond them) and the height its fit is made at, and the points' own box.</summary>
        private static WindowRead ReadOf(List<Vec3> line, int from, int to, bool settled, bool bridge, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = minZ = float.MaxValue;
            maxX = maxZ = float.MinValue;
            float sumY = 0f;
            for (int i = from; i < to; i++)
            {
                minX = Math.Min(minX, line[i].X);
                minZ = Math.Min(minZ, line[i].Z);
                maxX = Math.Max(maxX, line[i].X);
                maxZ = Math.Max(maxZ, line[i].Z);
                sumY += line[i].Y;
            }

            // The read reaches NearSurface beyond the points too for a barrier's line: the surface a foot outside it takes its height from must be in what was read.
            float reach = (settled ? 1f : 1f + MovingReach) + (bridge ? NearSurface : 0f);
            return new WindowRead(minX - reach, minZ - reach, maxX + reach, maxZ + reach, sumY / (to - from));
        }

        /// <summary>Forgets what was read (a run is given up, the tool is left).</summary>
        public void Clear()
        {
            m_Kept.Clear();
            m_SlotKey = null;
            m_SlotFit = null;
        }

        private Stretch Heights(List<Vec3> line, int from, int to, string at, bool settled, ReadSurface read, Func<float, float, float, float> ground, bool bridge)
        {
            WindowRead box = ReadOf(line, from, to, settled, bridge, out float minX, out float minZ, out float maxX, out float maxZ);
            AreaFit fit;
            float origin = box.OriginY;
            if (!settled && m_SlotKey == at && minX >= m_SlotMinX && maxX <= m_SlotMaxX && minZ >= m_SlotMinZ && maxZ <= m_SlotMaxZ)
            {
                fit = m_SlotFit;
                origin = m_SlotOrigin;
            }
            else
            {
                float x0 = box.MinX, x1 = box.MaxX, z0 = box.MinZ, z1 = box.MaxZ;
                fit = read(x0, z0, x1, z1, origin);
                Reads++;
                if (!settled)
                {
                    m_SlotKey = at;
                    m_SlotFit = fit;
                    m_SlotOrigin = origin;
                    m_SlotMinX = x0;
                    m_SlotMaxX = x1;
                    m_SlotMinZ = z0;
                    m_SlotMaxZ = z1;
                }
            }

            var window = new Stretch { Heights = new List<Vec3>(to - from), On = new bool[to - from] };
            for (int i = from; i < to; i++)
            {
                Vec3 p = line[i];
                float y = 0f;
                float around = p.Y - origin;
                if (fit != null && (bridge ? fit.HeightAt(p.X, p.Z, around, MaxGroundStep, out y, out _, m_Seen, m_Found) : fit.HeightAt(p.X, p.Z, out y, out _, m_Seen, m_Found)))
                {
                    window.Heights.Add(new Vec3(p.X, origin + y, p.Z));
                    window.On[i - from] = true;
                    continue;
                }

                // Just outside the surface (a barrier's line): the surface's height, not the terrain's, which beside a deck is the ground under it; not a road under the deck, here and in what is under the point (0.12.68).
                if (bridge && fit != null && fit.HeightNear(p.X, p.Z, NearSurface, around, MaxGroundStep, out float nearY, m_Seen, m_Found))
                {
                    window.Heights.Add(new Vec3(p.X, origin + nearY, p.Z));
                    window.On[i - from] = true;
                    continue;
                }

                float terrain = ground(p.X, p.Y, p.Z);
                window.Heights.Add(!float.IsNaN(terrain) && Math.Abs(terrain - p.Y) <= MaxGroundStep ? new Vec3(p.X, terrain, p.Z) : p);
            }

            return window;
        }
    }
}
