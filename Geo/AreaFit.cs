using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A band of road surface that paint may go on (0.12.4): between two lines of points that run side by side, as many points each, in the world (the heights are the surface's). Made by SurfaceReader from
    /// the pieces of a road's cross-section that are its surface at the carriageway's height and do not stop traffic (not a barrier, a kerb, a footway or a median), along the road's own sides.
    /// </summary>
    public sealed class SurfaceStrip
    {
        private readonly List<Vec3> m_Left, m_Right;

        public SurfaceStrip() : this(new List<Vec3>(), new List<Vec3>())
        {
        }

        private SurfaceStrip(List<Vec3> left, List<Vec3> right)
        {
            m_Left = left;
            m_Right = right;
            Left = new ReadOnlyCollection<Vec3>(left);
            Right = new ReadOnlyCollection<Vec3>(right);
        }

        /// <summary>
        /// The two lines of points, read only: a strip is shared by every read that takes it from SurfacePartCache, so it is built with Add while it is made and never changed after it has been read.
        /// What a user of a read needs to be different (the post-height read's filler) is a copy: AsFiller.
        /// </summary>
        public ReadOnlyCollection<Vec3> Left { get; }

        public ReadOnlyCollection<Vec3> Right { get; }

        /// <summary>One more place along the strip: a point of each line.</summary>
        public void Add(Vec3 left, Vec3 right)
        {
            m_Left.Add(left);
            m_Right.Add(right);
        }

        /// <summary>A copy that is a filler (Filler) and shares the points of this strip.</summary>
        public SurfaceStrip AsFiller() => new SurfaceStrip(m_Left, m_Right) { Source = Source, Elevated = Elevated, Filler = true };

        /// <summary>What it was read from (for the log).</summary>
        public string Source = "";

        /// <summary>The surface of an elevated road (a bridge, a viaduct: 0.12.5): what stands beside it is not on the ground below.</summary>
        public bool Elevated;

        /// <summary>
        /// A band that only fills what the others leave (0.12.15): the gore between the halves of a junction part, whose surface is guessed (a straight line across from one middle line to the other). The
        /// junction's other parts often lie over it; their own surface is kept there, and the guess only where nothing else is.
        /// </summary>
        public bool Filler;
    }

    /// <summary>
    /// Where an area marking (an island, a gore, a box grid) can be painted and how high the surface is there (0.12.4; the owner: "根据护栏等等抛去无效区域，然后填充导流线，且需要贴合地面，即存在绘制区域地面不平的情况"):
    /// convex pieces in the item's frame (the item's position is the origin, +Z the way its yaw points), none overlapping another, each with the plane of the surface it lies on (y = A x + B z + C, relative
    /// to the item's height). The marking's paint is laid out from its outline as before and then cut to these pieces and lifted onto their planes (Drape): it stops at a barrier, a kerb or the grass
    /// between two roads, and it lies on a road that rises, falls or twists under it. Made on the main thread from the roads (or the ground, for an area on no road), used by the tile builders. Pure.
    /// </summary>
    public sealed class AreaFit
    {
        /// <summary>How far above or below the item's height (m) a road's surface may be to be painted on.</summary>
        public const float MaxRise = 12f;

        /// <summary>The longest side (m) a cell of the ground's surface has (an area on no road).</summary>
        public const float GroundStep = 2f;

        private readonly List<Vec3[]> m_Pieces = new List<Vec3[]>();
        private readonly List<float> m_A = new List<float>(), m_B = new List<float>(), m_C = new List<float>();
        private readonly List<bool> m_Elevated = new List<bool>();
        private readonly List<float> m_MinX = new List<float>(), m_MaxX = new List<float>(), m_MinZ = new List<float>(), m_MaxZ = new List<float>();
        private List<int>[] m_Grid;
        private float m_GridX, m_GridZ, m_Cell = 2f;
        private int m_Cols, m_Rows;

        private static int s_Versions;

        /// <summary>Different for every fit made, on whichever thread (the renderer tells a new fit of an item from the one it drew).</summary>
        public readonly int Version = System.Threading.Interlocked.Increment(ref s_Versions);

        /// <summary>Made from road surfaces (else from the ground: the whole area is painted, on the ground's heights).</summary>
        public bool OnRoad;

        /// <summary>What it was made from, for the log and the panel.</summary>
        public string Note = "";

        public int PieceCount => m_Pieces.Count;

        /// <summary>
        /// Whether the other fit is this one to the bit: its pieces, planes, bounds, the index over them, the kind and the note (not Version, which is different for every fit). The fits of the worker and of the
        /// main thread are made by one function from the same strips and are told apart by nothing else (FitWorker); why says where the first difference is.
        /// </summary>
        public bool SameAs(AreaFit other, out string why)
        {
            why = null;
            if (other == null)
            {
                why = "no fit";
                return false;
            }

            if (OnRoad != other.OnRoad || m_Loose != other.m_Loose || Note != other.Note)
                why = $"kind or note: {OnRoad}/{other.OnRoad}, {Note}/{other.Note}";
            else if (m_Pieces.Count != other.m_Pieces.Count)
                why = $"piece count {m_Pieces.Count}/{other.m_Pieces.Count}";
            else if (BitsOf(m_Cell) != BitsOf(other.m_Cell) || m_Cols != other.m_Cols || m_Rows != other.m_Rows || BitsOf(m_GridX) != BitsOf(other.m_GridX) || BitsOf(m_GridZ) != BitsOf(other.m_GridZ))
                why = "the index over the pieces";
            else
            {
                for (int i = 0; i < m_Pieces.Count && why == null; i++)
                {
                    Vec3[] a = m_Pieces[i], b = other.m_Pieces[i];
                    if (a.Length != b.Length)
                        why = $"piece {i} has {a.Length}/{b.Length} corners";
                    for (int k = 0; k < a.Length && why == null; k++)
                    {
                        if (BitsOf(a[k].X) != BitsOf(b[k].X) || BitsOf(a[k].Y) != BitsOf(b[k].Y) || BitsOf(a[k].Z) != BitsOf(b[k].Z))
                            why = $"piece {i} corner {k}";
                    }

                    if (why == null && (BitsOf(m_A[i]) != BitsOf(other.m_A[i]) || BitsOf(m_B[i]) != BitsOf(other.m_B[i]) || BitsOf(m_C[i]) != BitsOf(other.m_C[i]) || m_Elevated[i] != other.m_Elevated[i]))
                        why = $"piece {i} plane or elevation";
                    if (why == null && (BitsOf(m_MinX[i]) != BitsOf(other.m_MinX[i]) || BitsOf(m_MaxX[i]) != BitsOf(other.m_MaxX[i]) || BitsOf(m_MinZ[i]) != BitsOf(other.m_MinZ[i]) || BitsOf(m_MaxZ[i]) != BitsOf(other.m_MaxZ[i])))
                        why = $"piece {i} bounds";
                }

                if (why == null && (m_Grid == null) != (other.m_Grid == null))
                    why = "one has an index";
                for (int c = 0; why == null && m_Grid != null && c < m_Grid.Length; c++)
                {
                    List<int> a = m_Grid[c], b = other.m_Grid[c];
                    if ((a == null) != (b == null) || (a != null && !SameInts(a, b)))
                        why = $"index cell {c}";
                }
            }

            return why == null;
        }

        private static long BitsOf(float v) => BitConverter.DoubleToInt64Bits(v);

        private static bool SameInts(List<int> a, List<int> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        public IReadOnlyList<Vec3[]> Pieces => m_Pieces;

        /// <summary>The height of the surface (item frame) at a place inside a piece; false outside every piece.</summary>
        public bool HeightAt(float x, float z, out float y) => HeightAt(x, z, out y, out _);

        /// <summary>As HeightAt, and whether the piece is of an elevated road.</summary>
        public bool HeightAt(float x, float z, out float y, out bool elevated) => HeightAt(x, z, out y, out elevated, new HashSet<int>(), new List<int>());

        /// <summary>As HeightAt, with the caller's own lists for the search (many places asked one after another).</summary>
        public bool HeightAt(float x, float z, out float y, out bool elevated, HashSet<int> seen, List<int> found) =>
            HeightAt(x, z, 0f, float.PositiveInfinity, out y, out elevated, seen, found);

        /// <summary>
        /// As HeightAt, taking only a piece whose plane at the place is within within metres of around (the line's own height, item frame; 0.12.68): a barrier's foot at a deck's edge, off the deck's
        /// own surface, is over the road that crosses under the deck, and took its height.
        /// </summary>
        public bool HeightAt(float x, float z, float around, float within, out float y, out bool elevated, HashSet<int> seen, List<int> found)
        {
            y = 0f;
            elevated = false;
            foreach (int i in Candidates(x, x, z, z, seen, found))
            {
                float height = m_A[i] * x + m_B[i] * z + m_C[i];
                if (Inside(m_Pieces[i], x, z) && Math.Abs(height - around) <= within)
                {
                    y = height;
                    elevated = m_Elevated[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The height of the surface nearest a place that is not on it, within reach metres of one of its pieces (0.12.63): the plane of that piece, carried out to the place. For a barrier whose foot is
        /// a little outside what a post may stand on (the outer piece of an elevated road is a parapet or a kerb, and the slope falls away 2 or 3 m beside it): the terrain there is the ground under the
        /// deck, and a barrier that took it hung down the retaining wall. Only a piece whose plane, carried out to the place, is within within metres of around (the line's own height, item frame) counts
        /// (0.12.68): a road that crosses under a deck lies directly under a foot at the deck's edge, nearer than the deck's own piece, and the wall dived 8.5 m into it. False when no piece is that near.
        /// </summary>
        public bool HeightNear(float x, float z, float reach, float around, float within, out float y, HashSet<int> seen, List<int> found)
        {
            y = 0f;
            int best = -1;
            float bestDistance = reach;
            foreach (int i in Candidates(x - reach, x + reach, z - reach, z + reach, seen, found))
            {
                if (Math.Abs(m_A[i] * x + m_B[i] * z + m_C[i] - around) > within)
                    continue;
                float d = DistanceTo(m_Pieces[i], x, z);
                if (d < bestDistance || (d == bestDistance && best >= 0 && i < best))
                {
                    bestDistance = d;
                    best = i;
                }
            }

            if (best < 0)
                return false;
            y = m_A[best] * x + m_B[best] * z + m_C[best];
            return true;
        }

        /// <summary>How far (m, in X and Z) a place is from a convex piece: 0 inside it.</summary>
        private static float DistanceTo(Vec3[] piece, float x, float z)
        {
            if (Inside(piece, x, z))
                return 0f;
            float nearest = float.MaxValue;
            for (int e = 0; e < piece.Length; e++)
            {
                Vec3 a = piece[e], b = piece[(e + 1) % piece.Length];
                float dx = b.X - a.X, dz = b.Z - a.Z, length2 = dx * dx + dz * dz;
                float t = length2 > 1e-12f ? Math.Max(0f, Math.Min(1f, ((x - a.X) * dx + (z - a.Z) * dz) / length2)) : 0f;
                float px = a.X + t * dx - x, pz = a.Z + t * dz - z;
                nearest = Math.Min(nearest, (float)Math.Sqrt(px * px + pz * pz));
            }

            return nearest;
        }

        /// <summary>
        /// The fit of an area from bands of road surface (world): the item's frame is at origin turned by yaw; only the cells of the bands that reach within margin of the box (item frame, the outline's)
        /// are kept. A cell that lies over one kept before keeps only what is not under it (the road's segments and junctions meet edge to edge, but their pieces may overlap a little).
        /// </summary>
        public static AreaFit FromStrips(IList<SurfaceStrip> strips, Vec3 origin, float yaw, float minX, float maxX, float minZ, float maxZ, float margin = 1f)
        {
            var fit = new AreaFit { OnRoad = true };
            if (strips == null)
                return fit;
            List<KeyValuePair<Vec3[], bool>> ordered = Ordered(strips, origin, yaw, minX - margin, maxX + margin, minZ - margin, maxZ + margin);
            fit.m_Building = new Dictionary<long, List<int>>();
            foreach (KeyValuePair<Vec3[], bool> t in ordered)
                fit.AddDisjoint(t.Key, t.Value);
            fit.m_Building = null;
            fit.Index();
            fit.Note = $"{fit.m_Pieces.Count} piece(s) of road surface from {strips.Count} band(s)";
            return fit;
        }

        /// <summary>
        /// The fit as FromStrips, for asking the height at places only (0.12.59): its pieces are the cells of the bands as they are, one over another where the bands overlap, and the first of them in the
        /// order FromStrips puts them in is the one a place is on (HeightAt takes the first it finds: the cells of one grid cell are listed in that order). The same heights come of it, without cutting every cell by every one before it, which took most of the time of a window of a
        /// junction (a window of 66 bands: 22 ms, now 0.4 ms in the bench); a fit that is to cut paint (Drape) must be made by FromStrips.
        /// </summary>
        public static AreaFit ForHeights(IList<SurfaceStrip> strips, Vec3 origin, float yaw, float minX, float maxX, float minZ, float maxZ, float margin = 1f)
        {
            var fit = new AreaFit { OnRoad = true, m_Loose = true };
            if (strips == null)
                return fit;
            foreach (KeyValuePair<Vec3[], bool> t in Ordered(strips, origin, yaw, minX - margin, maxX + margin, minZ - margin, maxZ + margin))
            {
                if (Math.Abs(Polygon.SignedArea2(t.Key)) >= 1e-6f)
                    fit.Add(t.Key, PlaneOf(t.Key[0], t.Key[1], t.Key[2]), t.Value);
            }

            fit.Index();
            fit.Note = $"{fit.m_Pieces.Count} cell(s) of road surface from {strips.Count} band(s), for heights";
            return fit;
        }

        /// <summary>Whether the pieces overlap (ForHeights): the first one a place is in is its surface, and paint cannot be cut to them.</summary>
        private bool m_Loose;

        /// <summary>The triangles of the bands that reach the box (item frame) and are within MaxRise of the item, in the order they lie in a fit: nearer the item's height first, the fillers after all the others.</summary>
        private static List<KeyValuePair<Vec3[], bool>> Ordered(IList<SurfaceStrip> strips, Vec3 origin, float yaw, float x0, float x1, float z0, float z1)
        {
            var triangles = new List<Vec3[]>();
            var raised = new Dictionary<Vec3[], bool>();
            var filler = new HashSet<Vec3[]>();
            foreach (SurfaceStrip strip in strips)
            {
                int n = Math.Min(strip.Left.Count, strip.Right.Count);
                if (n < 2)
                    continue;
                List<Vec3> left = MarkingPolygon.ToLocal(strip.Left, origin, yaw), right = MarkingPolygon.ToLocal(strip.Right, origin, yaw);
                for (int i = 0; i + 1 < n; i++)
                {
                    Vec3 a = left[i], b = right[i], c = right[i + 1], d = left[i + 1];
                    float qx0 = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), qx1 = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
                    float qz0 = Math.Min(Math.Min(a.Z, b.Z), Math.Min(c.Z, d.Z)), qz1 = Math.Max(Math.Max(a.Z, b.Z), Math.Max(c.Z, d.Z));
                    if (qx1 < x0 || qx0 > x1 || qz1 < z0 || qz0 > z1)
                        continue;
                    var t0 = new[] { a, b, c };
                    var t1 = new[] { a, c, d };
                    triangles.Add(t0);
                    triangles.Add(t1);
                    raised[t0] = strip.Elevated;
                    raised[t1] = strip.Elevated;
                    if (strip.Filler)
                    {
                        filler.Add(t0);
                        filler.Add(t1);
                    }
                }
            }

            // Where two surfaces lie over each other (a bridge over the road the area is on), the one nearer the item's height is kept: the cells are put in by how far their middles are from it, and
            // one put in later keeps only what the earlier ones do not cover. A surface far above or below (a deck over the road, a tunnel under it) is left out. Filler bands go in after all the others
            // (2 * MaxRise added to their order), so they keep only what nothing else covers.
            var ordered = new List<KeyValuePair<float, Vec3[]>>();
            foreach (Vec3[] t in triangles)
            {
                float y = Math.Abs((t[0].Y + t[1].Y + t[2].Y) / 3f);
                if (y <= MaxRise)
                    ordered.Add(new KeyValuePair<float, Vec3[]>(filler.Contains(t) ? y + 2f * MaxRise : y, t));
            }

            ordered.Sort((p, q) => p.Key.CompareTo(q.Key));
            var result = new List<KeyValuePair<Vec3[], bool>>(ordered.Count);
            foreach (KeyValuePair<float, Vec3[]> t in ordered)
                result.Add(new KeyValuePair<Vec3[], bool>(t.Value, raised[t.Value]));
            return result;
        }

        /// <summary>The fit of an area on no road: cells of the ground every GroundStep metres over the box (item frame), on the heights heightAt gives (item frame, relative to the item's height).</summary>
        public static AreaFit FromGround(Func<float, float, float> heightAt, float minX, float maxX, float minZ, float maxZ)
        {
            var fit = new AreaFit { OnRoad = false };
            if (heightAt == null || !(maxX > minX) || !(maxZ > minZ))
                return fit;
            int cols = Math.Max(1, Math.Min(200, (int)Math.Ceiling((maxX - minX) / GroundStep))), rows = Math.Max(1, Math.Min(200, (int)Math.Ceiling((maxZ - minZ) / GroundStep)));
            float dx = (maxX - minX) / cols, dz = (maxZ - minZ) / rows;
            var heights = new float[cols + 1, rows + 1];
            for (int i = 0; i <= cols; i++)
            {
                for (int j = 0; j <= rows; j++)
                {
                    float h = heightAt(minX + i * dx, minZ + j * dz);
                    heights[i, j] = float.IsNaN(h) || float.IsInfinity(h) ? 0f : h;
                }
            }

            for (int i = 0; i < cols; i++)
            {
                for (int j = 0; j < rows; j++)
                {
                    float ax = minX + i * dx, az = minZ + j * dz;
                    Vec3 a = new Vec3(ax, heights[i, j], az), b = new Vec3(ax + dx, heights[i + 1, j], az), c = new Vec3(ax + dx, heights[i + 1, j + 1], az + dz), d = new Vec3(ax, heights[i, j + 1], az + dz);
                    fit.Add(new[] { a, b, c }, PlaneOf(a, b, c));
                    fit.Add(new[] { a, c, d }, PlaneOf(a, c, d));
                }
            }

            fit.Index();
            fit.Note = $"the ground ({cols} x {rows} cells: no road under the area)";
            return fit;
        }

        private readonly Dictionary<MeshBuilder, MeshBuilder> m_Draped = new Dictionary<MeshBuilder, MeshBuilder>();

        /// <summary>Drape, kept for each mesh (the plans of the markings are kept, so a tile built again cuts nothing again). Safe from several tile builders at once.</summary>
        public MeshBuilder DrapedOf(MeshBuilder paint)
        {
            if (paint == null)
                return new MeshBuilder();
            lock (m_Draped)
            {
                if (m_Draped.TryGetValue(paint, out MeshBuilder done))
                    return done;
            }

            MeshBuilder draped = Drape(paint);
            lock (m_Draped)
                m_Draped[paint] = draped;
            return draped;
        }

        /// <summary>
        /// The paint cut to the pieces and laid on their surface: every triangle of it is cut to each piece it overlaps and its corners are put at the piece's height. The paint's own heights are not kept
        /// (an area's paint is flat in its frame, or on the slope a gore was given before 0.12.4: the surface takes the place of both). The normals are the surface's. What lies over no piece is left out.
        /// </summary>
        public MeshBuilder Drape(MeshBuilder paint)
        {
            if (m_Loose)
                throw new InvalidOperationException("A fit for heights has cells over one another and cannot cut paint: make it with FromStrips.");
            var result = new MeshBuilder();
            if (paint == null || m_Pieces.Count == 0)
                return result;
            var subject = new List<Vec3>(8);
            var scratch = new List<Vec3>(8);
            var seen = new HashSet<int>();
            var found = new List<int>();
            var seamSeen = new HashSet<int>();
            var seamFound = new List<int>();
            for (int t = 0; t + 2 < paint.Indices.Count; t += 3)
            {
                Vec3 p0 = paint.Positions[paint.Indices[t]], p1 = paint.Positions[paint.Indices[t + 1]], p2 = paint.Positions[paint.Indices[t + 2]];
                float x0 = Math.Min(p0.X, Math.Min(p1.X, p2.X)), x1 = Math.Max(p0.X, Math.Max(p1.X, p2.X)), z0 = Math.Min(p0.Z, Math.Min(p1.Z, p2.Z)), z1 = Math.Max(p0.Z, Math.Max(p1.Z, p2.Z));
                const float lift = 0f;
                foreach (int i in Candidates(x0, x1, z0, z1, seen, found))
                {
                    if (m_MaxX[i] < x0 || m_MinX[i] > x1 || m_MaxZ[i] < z0 || m_MinZ[i] > z1)
                        continue;
                    subject.Clear();
                    subject.Add(p0);
                    subject.Add(p1);
                    subject.Add(p2);
                    Vec3[] piece = m_Pieces[i];
                    for (int e = 0; e < piece.Length && subject.Count >= 3; e++)
                        ClipBy(subject, scratch, piece[e], piece[(e + 1) % piece.Length]);
                    if (subject.Count < 3 || Math.Abs(Polygon.SignedArea2(subject)) < 1e-7f)
                        continue;
                    Vec3 normal = MeshBuilder.Normalize(new Vec3(-m_A[i], 1f, -m_B[i]));
                    int first = -1;
                    for (int k = 0; k < subject.Count; k++)
                    {
                        Vec3 q = subject[k];
                        int v = result.Vertex(new Vec3(q.X, SeamHeight(i, q.X, q.Z, seamSeen, seamFound) + lift, q.Z), normal);
                        if (first < 0)
                            first = v;
                    }

                    for (int k = 1; k + 1 < subject.Count; k++)
                        result.Tri(first, first + k, first + k + 1);
                }
            }

            return result;
        }

        /// <summary>How far (m) the surface under a corner of paint may be from the paint's own height for the paint to lie on it (DrapeNear): the road's camber across a strip, not another road.</summary>
        public const float NearWithin = 1f;

        /// <summary>How far (m) the planes of two pieces may differ at an edge for the paint to take the higher one there (DrapeNear): the steps between the pieces of one surface, not a kerb.</summary>
        private const float kNearSeam = 0.05f;

        /// <summary>The planes of two pieces are as near the paint's own when their gaps round to the same multiple of this (m) (DrapeNear): the pieces of one surface differ by the roundings of the roads' bands, and the order of the fit decides.</summary>
        private const float kNearTie = 0.05f;

        private readonly Dictionary<MeshBuilder, (MeshBuilder Mesh, float Lost)> m_DrapedNear = new Dictionary<MeshBuilder, (MeshBuilder, float)>();

        /// <summary>DrapeNear with NearWithin, kept for each mesh as DrapedOf keeps its own: the marking is laid once and every tile build after it reads the result. Safe from several tile builders at once.</summary>
        public MeshBuilder DrapedNearOf(MeshBuilder paint, out float lost)
        {
            lost = 0f;
            if (paint == null)
                return new MeshBuilder();
            lock (m_DrapedNear)
            {
                if (m_DrapedNear.TryGetValue(paint, out var done))
                {
                    lost = done.Lost;
                    return done.Mesh;
                }
            }

            MeshBuilder draped = DrapeNear(paint, NearWithin, out lost);
            lock (m_DrapedNear)
                m_DrapedNear[paint] = (draped, lost);
            return draped;
        }

        /// <summary>
        /// The paint cut to the pieces it lies on at its own height (0.12.83; the owner's picture of a hatched strip with bars over the water and on the grass under a bridge): as Drape, but the paint
        /// keeps the height it was laid at. A piece takes a part of a triangle only where its plane is within within metres of the paint's own at every corner of that part, and where two pieces do, the
        /// one nearer the paint's height takes it first (a deck and the road under it, a ramp over the road it starts on: not the piece nearer the item's origin, as in the fit's order); the others keep
        /// only what it leaves. What lies over no piece at its height (beyond the deck's edge, over the ground or the water) is left out, lost is its area (m2). The paint's own heights are only read, not
        /// kept: the surface's take their place. The pieces may overlap (ForHeights).
        /// </summary>
        public MeshBuilder DrapeNear(MeshBuilder paint, float within, out float lost)
        {
            lost = 0f;
            var result = new MeshBuilder();
            if (paint == null)
                return result;
            if (m_Pieces.Count == 0)
            {
                lost = AreaFitRules.Area(paint);
                return result;
            }

            var seen = new HashSet<int>();
            var found = new List<int>();
            var seamSeen = new HashSet<int>();
            var seamFound = new List<int>();
            var scratch = new List<Vec3>(8);
            var order = new List<(long Key, int Piece)>();
            for (int t = 0; t + 2 < paint.Indices.Count; t += 3)
            {
                Vec3 p0 = paint.Positions[paint.Indices[t]], p1 = paint.Positions[paint.Indices[t + 1]], p2 = paint.Positions[paint.Indices[t + 2]];
                float area2 = Polygon.SignedArea2(new[] { p0, p1, p2 });
                if (Math.Abs(area2) < 1e-7f)
                    continue;
                // Clipped counter-clockwise, given back as the paint was wound.
                bool flipped = area2 < 0f;
                (float a, float b, float c) own = PlaneOf(p0, p1, p2);
                List<Vec3> triangle = flipped ? new List<Vec3> { p0, p2, p1 } : new List<Vec3> { p0, p1, p2 };
                float x0 = Math.Min(p0.X, Math.Min(p1.X, p2.X)), x1 = Math.Max(p0.X, Math.Max(p1.X, p2.X)), z0 = Math.Min(p0.Z, Math.Min(p1.Z, p2.Z)), z1 = Math.Max(p0.Z, Math.Max(p1.Z, p2.Z));
                order.Clear();
                foreach (int i in Candidates(x0, x1, z0, z1, seen, found))
                {
                    if (m_MaxX[i] < x0 || m_MinX[i] > x1 || m_MaxZ[i] < z0 || m_MinZ[i] > z1)
                        continue;
                    List<Vec3> inside = ClippedTo(triangle, m_Pieces[i], scratch);
                    if (inside.Count < 3)
                        continue;
                    float gap = Gap(i, inside, own);
                    // Pieces of one surface, which differ by less than kNearTie, come in the order of the fit, the fillers last.
                    if (gap <= within)
                        order.Add(((long)Math.Round(gap / kNearTie), i));
                }

                order.Sort((p, q) => p.Key != q.Key ? p.Key.CompareTo(q.Key) : p.Piece.CompareTo(q.Piece));
                var parts = new List<List<Vec3>> { triangle };
                foreach ((long _, int i) in order)
                {
                    if (parts.Count == 0)
                        break;
                    Vec3[] piece = m_Pieces[i];
                    var rest = new List<List<Vec3>>();
                    foreach (List<Vec3> part in parts)
                    {
                        List<Vec3> inside = ClippedTo(part, piece, scratch);
                        if (inside.Count < 3 || Math.Abs(Polygon.SignedArea2(inside)) < 1e-6f)
                        {
                            rest.Add(part);
                            continue;
                        }

                        Vec3 normal = MeshBuilder.Normalize(new Vec3(-m_A[i], 1f, -m_B[i]));
                        int first = -1;
                        for (int k = 0; k < inside.Count; k++)
                        {
                            Vec3 q = inside[k];
                            int v = result.Vertex(new Vec3(q.X, SeamHeight(i, q.X, q.Z, seamSeen, seamFound, kNearSeam), q.Z), normal);
                            if (first < 0)
                                first = v;
                        }

                        for (int k = 1; k + 1 < inside.Count; k++)
                        {
                            if (flipped)
                                result.Tri(first, first + k + 1, first + k);
                            else
                                result.Tri(first, first + k, first + k + 1);
                        }

                        Subtract(part, piece, rest);
                    }

                    parts = rest;
                }

                foreach (List<Vec3> part in parts)
                    lost += Math.Abs(Polygon.SignedArea2(part)) * 0.5f;
            }

            return result;
        }

        /// <summary>The part of a convex polygon (counter-clockwise) that lies over a convex piece, as a new polygon.</summary>
        private static List<Vec3> ClippedTo(List<Vec3> polygon, Vec3[] piece, List<Vec3> scratch)
        {
            var inside = new List<Vec3>(polygon);
            for (int e = 0; e < piece.Length && inside.Count >= 3; e++)
                ClipBy(inside, scratch, piece[e], piece[(e + 1) % piece.Length]);
            return inside;
        }

        /// <summary>How far (m) the plane of piece i is from the paint's own plane at the worst corner of a part of the paint that lies over the piece.</summary>
        private float Gap(int i, List<Vec3> part, (float a, float b, float c) own)
        {
            float worst = 0f;
            foreach (Vec3 q in part)
                worst = Math.Max(worst, Math.Abs(m_A[i] * q.X + m_B[i] * q.Z + m_C[i] - (own.a * q.X + own.b * q.Z + own.c)));
            return worst;
        }

        // ------------------------------------------------------------------ building

        private void Add(Vec3[] piece, (float a, float b, float c) plane, bool elevated = false)
        {
            // Corners that fall on one another (a cut through a corner gives two) are one: an edge of no length has no side, and a piece cut by it would keep what it should not.
            piece = Distinct(piece);
            if (piece.Length < 3 || Math.Abs(Polygon.SignedArea2(piece)) < 1e-6f)
                return;
            // Counter-clockwise (in X, Z, as Polygon.Cross sees it), so that inside is the left of every edge.
            if (Polygon.SignedArea2(piece) < 0f)
                Array.Reverse(piece);
            m_Pieces.Add(piece);
            m_A.Add(plane.a);
            m_B.Add(plane.b);
            m_C.Add(plane.c);
            m_Elevated.Add(elevated);
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (Vec3 p in piece)
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                z0 = Math.Min(z0, p.Z);
                z1 = Math.Max(z1, p.Z);
            }

            m_MinX.Add(x0);
            m_MaxX.Add(x1);
            m_MinZ.Add(z0);
            m_MaxZ.Add(z1);
            if (m_Building == null)
                return;
            int at = m_Pieces.Count - 1;
            int c0 = BuildCell(x0), c1 = BuildCell(x1), r0 = BuildCell(z0), r1 = BuildCell(z1);
            if ((long)(c1 - c0 + 1) * (r1 - r0 + 1) > kBuildCells)
            {
                m_Wide.Add(at);
                return;
            }

            for (int c = c0; c <= c1; c++)
            {
                for (int r = r0; r <= r1; r++)
                {
                    long key = ((long)c << 32) | (uint)r;
                    if (!m_Building.TryGetValue(key, out List<int> cell))
                        m_Building[key] = cell = new List<int>(4);
                    cell.Add(at);
                }
            }
        }

        /// <summary>
        /// While a fit is built from many triangles: the pieces so far by cell of kBuildCell metres (only the pieces near a new triangle are cut from it, not every piece before it), and those too wide for
        /// the cells, which every triangle meets. Dropped when the fit is built (Index takes over).
        /// </summary>
        private Dictionary<long, List<int>> m_Building;
        private readonly List<int> m_Wide = new List<int>();
        private readonly List<int> m_Near = new List<int>();
        private const float kBuildCell = 4f;
        private const int kBuildCells = 1024;

        private static int BuildCell(float v) => (int)Math.Floor(v / kBuildCell);

        /// <summary>The indices of the pieces so far that may lie under a box, in the order they were added (the cuts are made in that order, as they were when every piece was looked at).</summary>
        private List<int> Near(float x0, float x1, float z0, float z1)
        {
            m_Near.Clear();
            m_Near.AddRange(m_Wide);
            int c0 = BuildCell(x0), c1 = BuildCell(x1), r0 = BuildCell(z0), r1 = BuildCell(z1);
            if ((long)(c1 - c0 + 1) * (r1 - r0 + 1) > kBuildCells)
            {
                m_Near.Clear();
                for (int i = 0; i < m_Pieces.Count; i++)
                    m_Near.Add(i);
                return m_Near;
            }

            for (int c = c0; c <= c1; c++)
            {
                for (int r = r0; r <= r1; r++)
                {
                    if (m_Building.TryGetValue(((long)c << 32) | (uint)r, out List<int> cell))
                        m_Near.AddRange(cell);
                }
            }

            m_Near.Sort();
            int kept = 0;
            for (int i = 0; i < m_Near.Count; i++)
            {
                if (i == 0 || m_Near[i] != m_Near[i - 1])
                    m_Near[kept++] = m_Near[i];
            }

            m_Near.RemoveRange(kept, m_Near.Count - kept);
            return m_Near;
        }

        /// <summary>A triangle of a band added as what of it no piece kept before covers (convex pieces), each on the triangle's own plane.</summary>
        private void AddDisjoint(Vec3[] triangle, bool elevated)
        {
            if (Math.Abs(Polygon.SignedArea2(triangle)) < 1e-6f)
                return;
            (float a, float b, float c) plane = PlaneOf(triangle[0], triangle[1], triangle[2]);
            var parts = new List<List<Vec3>> { new List<Vec3>(triangle) };
            float x0 = Math.Min(triangle[0].X, Math.Min(triangle[1].X, triangle[2].X)), x1 = Math.Max(triangle[0].X, Math.Max(triangle[1].X, triangle[2].X));
            float z0 = Math.Min(triangle[0].Z, Math.Min(triangle[1].Z, triangle[2].Z)), z1 = Math.Max(triangle[0].Z, Math.Max(triangle[1].Z, triangle[2].Z));
            List<int> near = Near(x0, x1, z0, z1);
            for (int k = 0; k < near.Count && parts.Count > 0; k++)
            {
                int i = near[k];
                if (m_MaxX[i] <= x0 || m_MinX[i] >= x1 || m_MaxZ[i] <= z0 || m_MinZ[i] >= z1)
                    continue;
                var next = new List<List<Vec3>>();
                foreach (List<Vec3> part in parts)
                    Subtract(part, m_Pieces[i], next);
                parts = next;
            }

            foreach (List<Vec3> part in parts)
            {
                if (part.Count >= 3 && Math.Abs(Polygon.SignedArea2(part)) > 1e-5f)
                    Add(part.ToArray(), plane, elevated);
            }
        }

        /// <summary>The corners of a polygon without those that are on the one before (to a millimetre), the last against the first too.</summary>
        internal static Vec3[] Distinct(IList<Vec3> polygon)
        {
            var kept = new List<Vec3>(polygon.Count);
            foreach (Vec3 p in polygon)
            {
                if (kept.Count == 0 || Vec3.DistanceXZ(kept[kept.Count - 1], p) > 1e-3f)
                    kept.Add(p);
            }

            while (kept.Count > 1 && Vec3.DistanceXZ(kept[0], kept[kept.Count - 1]) <= 1e-3f)
                kept.RemoveAt(kept.Count - 1);
            return kept.ToArray();
        }

        /// <summary>The convex polygon a less the convex polygon b (counter-clockwise), as convex pieces that do not overlap: for each edge of b, what of a lies outside it and inside the edges before it.</summary>
        internal static void Subtract(List<Vec3> a, Vec3[] b, List<List<Vec3>> into)
        {
            b = Distinct(b);
            if (b.Length >= 3 && Polygon.SignedArea2(b) < 0f)
                Array.Reverse(b);
            var scratch = new List<Vec3>(8);
            var rest = new List<Vec3>(a);
            if (Polygon.SignedArea2(rest) < 0f)
                rest.Reverse();
            // Nothing of a under b (they only touch): a stays whole.
            var common = new List<Vec3>(rest);
            for (int e = 0; e < b.Length && common.Count >= 3; e++)
                ClipBy(common, scratch, b[e], b[(e + 1) % b.Length]);
            if (common.Count < 3 || Math.Abs(Polygon.SignedArea2(common)) < 1e-5f)
            {
                into.Add(rest);
                return;
            }

            bool overlaps = false;
            for (int e = 0; e < b.Length && rest.Count >= 3; e++)
            {
                Vec3 p = b[e], q = b[(e + 1) % b.Length];
                // What lies outside the edge (to its right) is not under b: it is kept.
                var outside = new List<Vec3>(rest);
                ClipBy(outside, scratch, q, p);
                if (outside.Count >= 3 && Math.Abs(Polygon.SignedArea2(outside)) > 1e-6f)
                    into.Add(outside);
                // What lies inside it goes on to the next edge.
                ClipBy(rest, scratch, p, q);
                overlaps = true;
            }

            if (!overlaps)
                into.Add(new List<Vec3>(a));
        }

        /// <summary>A polygon cut to the left of the line from a to b (in X, Z; Sutherland-Hodgman with one edge), in place. The heights of new corners are taken along the polygon's edges.</summary>
        internal static void ClipBy(List<Vec3> poly, List<Vec3> scratch, Vec3 a, Vec3 b)
        {
            scratch.Clear();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vec3 p = poly[i], q = poly[(i + 1) % n];
                float dp = Polygon.Cross(a, b, p), dq = Polygon.Cross(a, b, q);
                if (dp >= 0f)
                    scratch.Add(p);
                if ((dp >= 0f) != (dq >= 0f))
                {
                    float t = dp / (dp - dq);
                    scratch.Add(Vec3.Lerp(p, q, t));
                }
            }

            poly.Clear();
            poly.AddRange(scratch);
        }

        /// <summary>How near the edge of a piece (m) a point counts as on it, for SeamHeight: the corners a cut makes are on the edge to a float's error.</summary>
        private const float SeamTolerance = 0.002f;

        /// <summary>
        /// The height of the paint at a point of piece own (0.12.12): the piece's plane, or where the point is on the edge between pieces of one surface (within 0.5 m of each other), the highest of their
        /// planes there. Each piece has a plane of its own and the two sides of an edge between two of them need not agree (a segment and a junction's part, the halves of a crowned road): the paint of
        /// each side was laid at its own height, with a step at the edge and the lower side under the road where the road bulges, which showed as a fine flickering seam ("微微的渲染问题").
        /// </summary>
        private float SeamHeight(int own, float x, float z, HashSet<int> seen, List<int> found, float limit = 0.5f)
        {
            float y = m_A[own] * x + m_B[own] * z + m_C[own];
            foreach (int i in Candidates(x - SeamTolerance, x + SeamTolerance, z - SeamTolerance, z + SeamTolerance, seen, found))
            {
                if (i == own || !InsideNear(m_Pieces[i], x, z, SeamTolerance))
                    continue;
                float other = m_A[i] * x + m_B[i] * z + m_C[i];
                if (other > y && other - y < limit)
                    y = other;
            }

            return y;
        }

        /// <summary>Whether a point is inside a (counter-clockwise, convex) piece or within tolerance (m) of it.</summary>
        private static bool InsideNear(Vec3[] piece, float x, float z, float tolerance)
        {
            var p = new Vec3(x, 0f, z);
            for (int e = 0; e < piece.Length; e++)
            {
                Vec3 a = piece[e], b = piece[(e + 1) % piece.Length];
                float length = Vec3.DistanceXZ(a, b);
                if (length > 1e-6f && Polygon.Cross(a, b, p) / length < -tolerance)
                    return false;
            }

            return true;
        }

        private static bool Inside(Vec3[] piece, float x, float z)
        {
            var p = new Vec3(x, 0f, z);
            for (int e = 0; e < piece.Length; e++)
            {
                if (Polygon.Cross(piece[e], piece[(e + 1) % piece.Length], p) < -1e-6f)
                    return false;
            }

            return true;
        }

        /// <summary>The plane y = a x + b z + c through three points (flat when they are in a line on the ground).</summary>
        internal static (float a, float b, float c) PlaneOf(Vec3 p, Vec3 q, Vec3 r)
        {
            float ux = q.X - p.X, uy = q.Y - p.Y, uz = q.Z - p.Z, vx = r.X - p.X, vy = r.Y - p.Y, vz = r.Z - p.Z;
            // The normal (u x v); y = a x + b z + c with a = -nx / ny, b = -nz / ny.
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (Math.Abs(ny) < 1e-9f)
                return (0f, 0f, (p.Y + q.Y + r.Y) / 3f);
            float a = -nx / ny, b = -nz / ny;
            return (a, b, p.Y - a * p.X - b * p.Z);
        }

        // ------------------------------------------------------------------ the index of the pieces

        private void Index()
        {
            if (m_Pieces.Count == 0)
                return;
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < m_Pieces.Count; i++)
            {
                x0 = Math.Min(x0, m_MinX[i]);
                x1 = Math.Max(x1, m_MaxX[i]);
                z0 = Math.Min(z0, m_MinZ[i]);
                z1 = Math.Max(z1, m_MaxZ[i]);
            }

            m_Cell = 2f;
            while ((Math.Ceiling((x1 - x0) / m_Cell) + 1) * (Math.Ceiling((z1 - z0) / m_Cell) + 1) > 8192)
                m_Cell *= 1.5f;
            m_GridX = x0;
            m_GridZ = z0;
            m_Cols = (int)Math.Ceiling((x1 - x0) / m_Cell) + 1;
            m_Rows = (int)Math.Ceiling((z1 - z0) / m_Cell) + 1;
            m_Grid = new List<int>[m_Cols * m_Rows];
            for (int i = 0; i < m_Pieces.Count; i++)
            {
                for (int cx = Col(m_MinX[i]); cx <= Col(m_MaxX[i]); cx++)
                {
                    for (int cz = Row(m_MinZ[i]); cz <= Row(m_MaxZ[i]); cz++)
                    {
                        int k = cz * m_Cols + cx;
                        if (m_Grid[k] == null)
                            m_Grid[k] = new List<int>(4);
                        m_Grid[k].Add(i);
                    }
                }
            }
        }

        private int Col(float x) => Math.Max(0, Math.Min(m_Cols - 1, (int)Math.Floor((x - m_GridX) / m_Cell)));

        private int Row(float z) => Math.Max(0, Math.Min(m_Rows - 1, (int)Math.Floor((z - m_GridZ) / m_Cell)));

        /// <summary>The pieces whose cells meet a box (each once), into the lists given (the caller's own: a fit is read by several tile builders at once).</summary>
        private List<int> Candidates(float x0, float x1, float z0, float z1, HashSet<int> seen, List<int> found)
        {
            found.Clear();
            if (m_Grid == null)
                return found;
            seen.Clear();
            for (int cx = Col(x0); cx <= Col(x1); cx++)
            {
                for (int cz = Row(z0); cz <= Row(z1); cz++)
                {
                    List<int> list = m_Grid[cz * m_Cols + cx];
                    if (list == null)
                        continue;
                    foreach (int i in list)
                    {
                        if (seen.Add(i))
                            found.Add(i);
                    }
                }
            }

            return found;
        }
    }
}
