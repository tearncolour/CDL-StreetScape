using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A marking of one piece laid along the road (0.12.49; the owner: "停车线/公交岛划线可以跟随弯道/用线吸附放置"): parking bays, the bus stops, the no-parking lines and the distance chevrons are made
    /// straight in their own frame (x across, z along from 0) and then bent along a line, each point (x, z) moved to the point of the line z along it and x to its right. The line is kept in the item's
    /// settings (Key) as chords of one length (cm) and the turn of each from the one before (1e-3 rad, positive toward +X, the driver's right), written "S:t,t,...": the first chord leaves the item's
    /// position heading +Z. Beyond its ends the line goes on straight. Between the chords' ends the right of the line is mitred, so that a point x across lies on the line's offset by x exactly.
    /// Pure.
    /// </summary>
    public sealed class MarkingBend
    {
        /// <summary>The settings of a bendable marking: the line (Key), the length it was drawn along the road (FillKey, cm; empty for one placed with one click) and how it is placed (PlaceKey).</summary>
        public const string Key = "bend", FillKey = "fill", PlaceKey = "place";

        /// <summary>The value of PlaceKey for a marking drawn from a start to an end along the road; one click places the others.</summary>
        public const string Drawn = "line";

        /// <summary>The most chords of a line (the settings' field is 509 bytes and the ground under the marking takes up to 200 of them), and the shortest (m).</summary>
        public const int MaxPieces = 32;

        public const float MinChord = 1f;

        /// <summary>How far apart along z the paint is cut before it is bent (m): a straight edge of the paint follows the line in pieces no longer than this.</summary>
        public const float CutStep = 1f;

        private readonly List<Vec3> m_Points;
        private readonly float[] m_S;
        private readonly Vec3[] m_Rights;

        private MarkingBend(List<Vec3> points)
        {
            m_Points = points;
            int n = points.Count;
            m_S = new float[n];
            for (int i = 1; i < n; i++)
                m_S[i] = m_S[i - 1] + Vec3.DistanceXZ(points[i - 1], points[i]);
            m_Rights = new Vec3[n];
            for (int i = 0; i < n; i++)
            {
                Vec3 before = i > 0 ? Right(points[i - 1], points[i]) : Right(points[0], points[1]);
                Vec3 after = i < n - 1 ? Right(points[i], points[i + 1]) : before;
                Vec3 mitre = (before + after).HeadingXZ();
                // As long as needed to reach the offset of both chords: 1 / cos of half the turn.
                float cos = Math.Max(0.5f, mitre.X * after.X + mitre.Z * after.Z);
                m_Rights[i] = mitre * (1f / cos);
            }
        }

        /// <summary>The length of the line (m).</summary>
        public float Length => m_S[m_S.Length - 1];

        /// <summary>The line in the item's frame: from the origin, heading +Z, flat.</summary>
        public IReadOnlyList<Vec3> Points => m_Points;

        private static Vec3 Right(Vec3 a, Vec3 b)
        {
            Vec3 d = (b - a).HeadingXZ();
            return new Vec3(d.Z, 0f, -d.X);
        }

        /// <summary>
        /// The text of a line in the world (its first point the item's position) and the yaw the item must have (the first chord's direction): the line cut into equal chords, MinChord at the least and
        /// at most MaxPieces of them, its heights left out (the ground under the marking is read apart: GroundGrid). Empty when the line is shorter than a chord or its points are not numbers.
        /// </summary>
        public static string Encode(IList<Vec3> line, out float yaw)
        {
            yaw = 0f;
            if (line == null || line.Count < 2)
                return "";
            var lengths = new List<float> { 0f };
            for (int i = 1; i < line.Count; i++)
            {
                if (!line[i].IsFinite || !line[i - 1].IsFinite)
                    return "";
                lengths.Add(lengths[i - 1] + Vec3.DistanceXZ(line[i - 1], line[i]));
            }

            float total = lengths[lengths.Count - 1];
            if (total < MinChord)
                return "";
            int pieces = Math.Max(1, Math.Min(MaxPieces, (int)Math.Floor(total / MinChord)));
            int chordCm = (int)Math.Round(total / pieces * 100f);
            float chord = chordCm / 100f;
            var points = new List<Vec3>(pieces + 1);
            int k = 0;
            for (int i = 0; i <= pieces; i++)
            {
                float s = Math.Min(total, i * chord);
                while (k < lengths.Count - 2 && lengths[k + 1] < s)
                    k++;
                float span = lengths[k + 1] - lengths[k], f = span < 1e-6f ? 0f : (s - lengths[k]) / span;
                points.Add(Vec3.Lerp(line[k], line[k + 1], f));
            }

            var sb = new StringBuilder(chordCm.ToString(CultureInfo.InvariantCulture)).Append(':');
            // The direction the reader will have after the rounding of each turn, so that the rounding does not add up (as RunPath).
            double written = 0.0;
            for (int i = 0; i < pieces; i++)
            {
                double direction = Math.Atan2(points[i + 1].X - points[i].X, points[i + 1].Z - points[i].Z);
                if (i == 0)
                {
                    yaw = (float)direction;
                    written = direction;
                    continue;
                }

                int turn = (int)Math.Round(RunPath.Wrap(direction - written) * 1000.0);
                written += turn / 1000.0;
                if (i > 1)
                    sb.Append(',');
                sb.Append(turn.ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>The line of a text (Encode); null for an empty or damaged one.</summary>
        public static MarkingBend Decode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            int colon = text.IndexOf(':');
            if (colon <= 0 || !int.TryParse(text.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int chordCm) || chordCm < 1)
                return null;
            float chord = chordCm / 100f;
            var points = new List<Vec3> { Vec3.Zero, new Vec3(0f, 0f, chord) };
            double direction = 0.0;
            string rest = text.Substring(colon + 1);
            if (rest.Length > 0)
            {
                foreach (string part in rest.Split(','))
                {
                    if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int turn) || points.Count > MaxPieces)
                        return null;
                    direction += turn / 1000.0;
                    Vec3 last = points[points.Count - 1];
                    points.Add(new Vec3(last.X + chord * (float)Math.Sin(direction), 0f, last.Z + chord * (float)Math.Cos(direction)));
                }
            }

            return new MarkingBend(points);
        }

        /// <summary>Where the point (x, z) of the straight marking goes (flat; the height is the marking's own).</summary>
        public Vec3 Map(float x, float z)
        {
            int last = m_Points.Count - 1;
            if (z <= 0f)
            {
                Vec3 d = (m_Points[1] - m_Points[0]).HeadingXZ();
                return m_Points[0] + d * z + Right(m_Points[0], m_Points[1]) * x;
            }

            if (z >= m_S[last])
            {
                Vec3 d = (m_Points[last] - m_Points[last - 1]).HeadingXZ();
                return m_Points[last] + d * (z - m_S[last]) + Right(m_Points[last - 1], m_Points[last]) * x;
            }

            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (m_S[mid] <= z)
                    lo = mid;
                else
                    hi = mid;
            }

            float f = (z - m_S[lo]) / Math.Max(1e-6f, m_S[hi] - m_S[lo]);
            return Vec3.Lerp(m_Points[lo], m_Points[hi], f) + Vec3.Lerp(m_Rights[lo], m_Rights[hi], f) * x;
        }

        /// <summary>The places along z where a mesh is cut before it is bent: the chords' ends and every CutStep, within the line and the mesh.</summary>
        public List<float> CutsWithin(float z0, float z1)
        {
            var cuts = new List<float>();
            float end = Math.Min(z1, Length);
            for (float z = (float)Math.Ceiling(Math.Max(0f, z0) / CutStep) * CutStep; z < end; z += CutStep)
                cuts.Add(z);
            foreach (float s in m_S)
            {
                if (s > z0 && s < end)
                    cuts.Add(s);
            }

            cuts.Sort();
            var kept = new List<float>(cuts.Count);
            foreach (float c in cuts)
            {
                if (kept.Count == 0 || c - kept[kept.Count - 1] > 0.01f)
                    kept.Add(c);
            }

            return kept;
        }

        /// <summary>
        /// The triangles of a mesh cut along the lines z = each of cuts (sorted): each triangle into the pieces between them, made as fans; a point where an edge is cut is made once for the edge and the
        /// cut (two triangles that share the edge share it), its position, normal and texture coordinates in between those of the edge's ends.
        /// </summary>
        public static void Cut(MeshBuilder mesh, List<float> cuts)
        {
            if (cuts.Count == 0)
                return;
            const float eps = 1e-4f;
            var indices = new List<int>(mesh.Indices);
            mesh.Indices.Clear();
            var made = new Dictionary<(int, int, int), int>();
            int Crossing(int a, int b, int c)
            {
                var key = (Math.Min(a, b), Math.Max(a, b), c);
                if (made.TryGetValue(key, out int at))
                    return at;
                Vec3 pa = mesh.Positions[a], pb = mesh.Positions[b];
                float t = (cuts[c] - pa.Z) / (pb.Z - pa.Z);
                Vec3 normal = MeshBuilder.Normalize(Vec3.Lerp(mesh.Normals[a], mesh.Normals[b], t));
                at = mesh.Vertex(Vec3.Lerp(pa, pb, t), normal, mesh.U[a] + (mesh.U[b] - mesh.U[a]) * t, mesh.V[a] + (mesh.V[b] - mesh.V[a]) * t);
                made[key] = at;
                return at;
            }

            var polygon = new List<int>(8);
            for (int t = 0; t + 2 < indices.Count; t += 3)
            {
                int[] corner = { indices[t], indices[t + 1], indices[t + 2] };
                float lowZ = Math.Min(mesh.Positions[corner[0]].Z, Math.Min(mesh.Positions[corner[1]].Z, mesh.Positions[corner[2]].Z));
                float highZ = Math.Max(mesh.Positions[corner[0]].Z, Math.Max(mesh.Positions[corner[1]].Z, mesh.Positions[corner[2]].Z));
                int first = cuts.BinarySearch(lowZ + eps);
                first = first < 0 ? ~first : first + 1;
                int lastCut = first - 1;
                while (lastCut + 1 < cuts.Count && cuts[lastCut + 1] < highZ - eps)
                    lastCut++;
                if (lastCut < first)
                {
                    mesh.Indices.AddRange(corner);
                    continue;
                }

                // The slabs between the cuts the triangle crosses: its corners in each, and the points where its edges cross the slab's two cuts, in the order round the triangle.
                for (int slab = first; slab <= lastCut + 1; slab++)
                {
                    float lo = slab > first ? cuts[slab - 1] : float.MinValue, hi = slab <= lastCut ? cuts[slab] : float.MaxValue;
                    polygon.Clear();
                    for (int e = 0; e < 3; e++)
                    {
                        int a = corner[e], b = corner[(e + 1) % 3];
                        float za = mesh.Positions[a].Z, zb = mesh.Positions[b].Z;
                        if (za >= lo - eps && za <= hi + eps)
                            polygon.Add(a);
                        var crossings = new List<(float T, int Vertex)>(2);
                        foreach (int c in new[] { slab - 1, slab })
                        {
                            if (c < first || c > lastCut)
                                continue;
                            float z = cuts[c];
                            if ((za < z - eps && zb > z + eps) || (za > z + eps && zb < z - eps))
                                crossings.Add(((z - za) / (zb - za), Crossing(a, b, c)));
                        }

                        crossings.Sort((p, q) => p.T.CompareTo(q.T));
                        foreach ((float _, int vertex) in crossings)
                            polygon.Add(vertex);
                    }

                    for (int i = 1; i + 1 < polygon.Count; i++)
                    {
                        Vec3 p0 = mesh.Positions[polygon[0]], p1 = mesh.Positions[polygon[i]], p2 = mesh.Positions[polygon[i + 1]];
                        if (MeshBuilder.Cross(p1 - p0, p2 - p0).Length > 1e-9f)
                            mesh.Tri(polygon[0], polygon[i], polygon[i + 1]);
                    }
                }
            }
        }
    }
}
