using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// One character as the lit board is made of it (0.12.75): its loops in em units (y up, the baseline at 0; outer loops counter-clockwise, holes clockwise: what CutOutline.Triangulate and
    /// Prims.LoopsFace take), their triangulation, the box of the ink, and what the face, the back and the rim cost. Made once for a font, a character and a flatness bucket and shared (GlyphCache).
    /// </summary>
    public sealed class GlyphMesh
    {
        public List<List<Point2>> Loops;
        public Point2[] Points;
        public int[] Triangles;
        public float MinX, MinY, MaxX, MaxY, Advance;

        /// <summary>The filled area (outer loops less holes) and the length of all the loops, in em units (em squared, em).</summary>
        public float Area, Perimeter;

        /// <summary>The face (and the back) of a glyph is Points and Triangles; the rim, with its smooth corners, is counted by building it.</summary>
        public int FaceVertices, FaceTriangles, RimVertices, RimTriangles;

        public int LoopPoints;

        /// <summary>The neon style's glass: a tube swept along lines that the loops' band holds (0.12.76). Null for a flat letter. A glyph with a tube is drawn as the tube; its loops, points and triangles are the band the tube lies on, which the frame holds and the editor's picture shows.</summary>
        public NeonTube Tube;

        /// <summary>The layers of a colour emoji (0.12.78): this mesh is then their silhouette (the frame and the return use it), and the face is drawn as the layers, each in its own colour. Null for any other glyph.</summary>
        public EmojiPlan Emoji;

        /// <summary>Everything the glyph puts on a board: the face (an emoji's layers, not its silhouette), the back and the rim.</summary>
        public int Vertices => Tube != null ? Tube.Vertices : FaceVertices + RimVertices + (Emoji != null ? Emoji.FaceVertices : FaceVertices);

        public int TriangleCount => Tube != null ? Tube.Triangles : FaceTriangles + RimTriangles + (Emoji != null ? Emoji.FaceTriangles : FaceTriangles);

        /// <summary>
        /// The vertices of the mesh the letter's return makes (its back and its rim; the tube of a neon letter): what a text must keep under one mesh. An emoji's layers are counted here too: each is in the
        /// solid of its colour and the return's colour (black) is the one that the return is in, so the sum is what no solid can go over.
        /// </summary>
        public int ReturnVertices => Tube != null ? Tube.Vertices : FaceVertices + RimVertices + (Emoji != null ? Emoji.FaceVertices : 0);

        /// <summary>About what it holds in memory (bytes): for the cache's budget.</summary>
        public long Bytes => 160 + LoopPoints * 8L + Loops.Count * 40L + Points.Length * 8L + Triangles.Length * 4L + (Tube != null ? 64L + Tube.Points * 8L : 0L) + (Emoji != null ? Emoji.Bytes : 0L);

        /// <summary>
        /// Whether the mesh is sound: every number finite, the indices in range, no triangle of no area, and the triangles' areas add up to the filled area within a share (1 %: the triangulation
        /// leaves nothing out and covers nothing twice).
        /// </summary>
        public bool IsSound(out string why)
        {
            why = "";
            foreach (Point2 p in Points)
            {
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y))
                {
                    why = "a point is not finite";
                    return false;
                }
            }

            double sum = 0.0;
            for (int t = 0; t + 2 < Triangles.Length; t += 3)
            {
                int a = Triangles[t], b = Triangles[t + 1], c = Triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= Points.Length || b >= Points.Length || c >= Points.Length)
                {
                    why = "an index is out of range";
                    return false;
                }

                double area = Math.Abs(((double)Points[b].X - Points[a].X) * (Points[c].Y - Points[a].Y) - ((double)Points[b].Y - Points[a].Y) * (Points[c].X - Points[a].X)) * 0.5;
                if (area <= 1e-10)
                {
                    why = "a triangle has no area";
                    return false;
                }

                sum += area;
            }

            if (Math.Abs(sum - Area) > 0.01 * Math.Max(Area, 1e-9))
            {
                why = $"the triangles cover {sum:0.000000} of {Area:0.000000}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// The glyph put on a board as the cut board of SignBuilder puts its loops: the face (front) and the back from the triangulation that is kept (no triangulating again), the rim round every loop
        /// into the back's builder, all of it scaled to metres and moved to (x, y) as the viewer sees it (the vertex is at (-x, y), as Prims.LoopsFace puts it). Two builders: the face's material, the back's.
        /// </summary>
        public void AddTo(MeshBuilder face, MeshBuilder back, float metresPerEm, float x, float y, float backZ, float frontZ, bool smoothRim) =>
            AddTo(face, back, metresPerEm, x, y, backZ, frontZ, smoothRim, 0f);

        /// <summary>The same, turned about its origin by angle (radians, counter-clockwise as the viewer sees it) before it is moved to (x, y): a letter of a text on an arc (0.12.76).</summary>
        public void AddTo(MeshBuilder face, MeshBuilder back, float metresPerEm, float x, float y, float backZ, float frontZ, bool smoothRim, float angle)
        {
            if (Tube != null)
            {
                Tube.AddTo(face, metresPerEm, x, y, frontZ, angle);
                return;
            }

            var front = new Vec3(0f, 0f, 1f);
            var behind = new Vec3(0f, 0f, -1f);
            int faceFirst = face.VertexCount, backFirst = back.VertexCount;
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            foreach (Point2 p in Points)
            {
                float px = (cos * p.X - sin * p.Y) * metresPerEm, py = (sin * p.X + cos * p.Y) * metresPerEm;
                face.Vertex(new Vec3(-(x + px), y + py, frontZ), front);
                back.Vertex(new Vec3(-(x + px), y + py, backZ), behind);
            }

            for (int t = 0; t + 2 < Triangles.Length; t += 3)
            {
                face.TriFacing(faceFirst + Triangles[t], faceFirst + Triangles[t + 1], faceFirst + Triangles[t + 2], front);
                back.TriFacing(backFirst + Triangles[t], backFirst + Triangles[t + 1], backFirst + Triangles[t + 2], behind);
            }

            Prims.LoopsRim(back, Placed(metresPerEm, x, y, angle), backZ, frontZ, 0f, 0f, smoothRim);
        }

        /// <summary>
        /// A colour emoji put on a board (0.12.78): the back and the rim of the silhouette as for any letter, and the face as the layers of the font, each in the solid of its colour (the mesh builder
        /// that solid gives), the last layer in the plane of the face and each one before it EmojiPlan.LayerStep behind the next, so that no two faces are in one plane and the layers are all inside the
        /// letter's depth. Pure geometry: the caller passes the solids; on a curved wall an emoji is drawn as its silhouette in the glow colour (AddTo).
        /// </summary>
        public void AddEmojiTo(Func<SolidColour, MeshBuilder> solid, MeshBuilder back, float metresPerEm, float x, float y, float backZ, float frontZ, bool smoothRim, float angle)
        {
            var front = new Vec3(0f, 0f, 1f);
            var behind = new Vec3(0f, 0f, -1f);
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            int count = Emoji.Layers.Count;
            // The faces are never deeper than a third of the letter, however many layers there are.
            float step = Math.Min(EmojiPlan.LayerStep, Math.Max(0f, (frontZ - backZ) / 3f / Math.Max(1, count)));
            for (int i = 0; i < count; i++)
            {
                EmojiLayer layer = Emoji.Layers[i];
                float z = frontZ - (count - 1 - i) * step;
                MeshBuilder face = solid(layer.Colour);
                int first = face.VertexCount;
                foreach (Point2 p in layer.Mesh.Points)
                {
                    float px = (cos * p.X - sin * p.Y) * metresPerEm, py = (sin * p.X + cos * p.Y) * metresPerEm;
                    face.Vertex(new Vec3(-(x + px), y + py, z), front);
                }

                for (int t = 0; t + 2 < layer.Mesh.Triangles.Length; t += 3)
                    face.TriFacing(first + layer.Mesh.Triangles[t], first + layer.Mesh.Triangles[t + 1], first + layer.Mesh.Triangles[t + 2], front);
            }

            int backFirst = back.VertexCount;
            foreach (Point2 p in Points)
            {
                float px = (cos * p.X - sin * p.Y) * metresPerEm, py = (sin * p.X + cos * p.Y) * metresPerEm;
                back.Vertex(new Vec3(-(x + px), y + py, backZ), behind);
            }

            for (int t = 0; t + 2 < Triangles.Length; t += 3)
                back.TriFacing(backFirst + Triangles[t], backFirst + Triangles[t + 1], backFirst + Triangles[t + 2], behind);
            Prims.LoopsRim(back, Placed(metresPerEm, x, y, angle), backZ, frontZ, 0f, 0f, smoothRim);
        }

        /// <summary>The loops in metres, as the viewer sees them, moved to (x, y).</summary>
        public List<List<Point2>> Placed(float metresPerEm, float x, float y) => Placed(metresPerEm, x, y, 0f);

        /// <summary>The loops in metres, turned about the origin by angle (radians, counter-clockwise as the viewer sees it) and then moved to (x, y).</summary>
        public List<List<Point2>> Placed(float metresPerEm, float x, float y, float angle)
        {
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            var placed = new List<List<Point2>>(Loops.Count);
            foreach (List<Point2> loop in Loops)
            {
                var moved = new List<Point2>(loop.Count);
                foreach (Point2 p in loop)
                    moved.Add(new Point2(x + (cos * p.X - sin * p.Y) * metresPerEm, y + (sin * p.X + cos * p.Y) * metresPerEm));
                placed.Add(moved);
            }

            return placed;
        }
    }

    public readonly struct GlyphKey : IEquatable<GlyphKey>
    {
        public readonly int Font, Codepoint, Bucket;

        /// <summary>0 for the font's own glyph; another number for a glyph made from it (moved, sheared, made bolder, hollowed: TextPlanner), so that both are kept.</summary>
        public readonly int Variant;

        public GlyphKey(int font, int codepoint, int bucket) : this(font, codepoint, bucket, 0)
        {
        }

        public GlyphKey(int font, int codepoint, int bucket, int variant)
        {
            Font = font;
            Codepoint = codepoint;
            Bucket = bucket;
            Variant = variant;
        }

        public bool Equals(GlyphKey other) => Font == other.Font && Codepoint == other.Codepoint && Bucket == other.Bucket && Variant == other.Variant;

        public override bool Equals(object obj) => obj is GlyphKey other && Equals(other);

        public override int GetHashCode() => unchecked(((Font * 397 ^ Codepoint) * 397 ^ Bucket) * 397 ^ Variant);
    }

    /// <summary>
    /// The glyphs made so far, by font, character and flatness bucket, held within a budget of bytes (an estimate: GlyphMesh.Bytes) and shared by every text, so that a character is cut and triangulated
    /// once. SIEVE (SieveCache) decides what goes: a character met once does not push out the ones asked for again. Every member, the counters too, is guarded by the one lock, so that workers that build
    /// tiles and plan texts share one cache. The glyph is made outside the lock.
    /// </summary>
    public sealed class GlyphCache
    {
        /// <summary>
        /// What a cache is given when it is made, in the bytes GlyphMesh.Bytes counts: an ESTIMATE, and not read off a profile of the game. From Tests/Bench/Planner.cs (BENCH_PLANNER=1), at 0.5 mm flatness: a
        /// character of the 3,755 in common use weighs 3.4 KB at a letter of 0.3 m, 4.5 KB at 1 m, 7.3 KB at 3 m (at most 7.5, 9.9, 16.4 KB; a Latin letter or a figure a third of that), which is 12, 16 and 26 MB for
        /// all of them, and what the heap grew by on the .NET Framework agreed with the estimate to within 3 %. So 32 MB holds every common character at two sizes (0.3 and 1 m: 28 MB) or at 3 m alone (26 MB), or
        /// 10,000 glyphs of 0.3 m; a city's signs are assumed to name some hundreds of different characters at a few sizes (not measured). A constant and not a setting (the owner, 2 Nov: "常量吧").
        /// </summary>
        public const long DefaultBudgetBytes = 32L * 1024 * 1024;

        /// <summary>What a character the font has no outline for weighs: its place in the cache.</summary>
        private const long NoGlyphBytes = 64L;

        private readonly SieveCache<GlyphKey, GlyphMesh> m_Cache;
        private readonly object m_Lock = new object();
        private int m_Hits, m_Misses, m_Evictions;

        public GlyphCache(long budgetBytes)
        {
            m_Cache = new SieveCache<GlyphKey, GlyphMesh>(budgetBytes);
        }

        public int Count
        {
            get { lock (m_Lock) return m_Cache.Count; }
        }

        public long Bytes
        {
            get { lock (m_Lock) return m_Cache.Weight; }
        }

        public int Hits
        {
            get { lock (m_Lock) return m_Hits; }
        }

        public int Misses
        {
            get { lock (m_Lock) return m_Misses; }
        }

        public int Evictions
        {
            get { lock (m_Lock) return m_Evictions; }
        }

        /// <summary>The glyph for a key: the kept one, or what build makes (kept; null for a character the font has no outline for is kept too, so that it is not asked again). build runs outside the lock.</summary>
        public GlyphMesh GetOrBuild(GlyphKey key, Func<GlyphMesh> build) => TryHit(key, out GlyphMesh hit) ? hit : Keep(key, build());

        /// <summary>As above for a glyph of a font, made by TextGlyphs.Build: no delegate is made, so a hit makes no garbage (the other form makes one, 96 bytes, for every call).</summary>
        public GlyphMesh GetOrBuild(FontOutlines font, int fontId, int codepoint, int bucket)
        {
            var key = new GlyphKey(fontId, codepoint, bucket);
            return TryHit(key, out GlyphMesh hit) ? hit : Keep(key, TextGlyphs.Build(font, codepoint, bucket));
        }

        private bool TryHit(GlyphKey key, out GlyphMesh mesh)
        {
            lock (m_Lock)
            {
                bool hit = m_Cache.TryGet(key, out mesh);
                if (hit)
                    m_Hits++;
                else
                    m_Misses++;
                return hit;
            }
        }

        private GlyphMesh Keep(GlyphKey key, GlyphMesh made)
        {
            lock (m_Lock)
            {
                // Another worker made it meanwhile: its glyph is kept, so that every user has the same one.
                if (m_Cache.TryGet(key, out GlyphMesh already))
                    return already;
                int held = m_Cache.Count;
                // A glyph heavier than the whole budget is still kept, alone (weighed as the budget), and goes when the next comes.
                m_Cache.Set(key, made, Math.Min(made != null ? made.Bytes : NoGlyphBytes, m_Cache.Budget));
                m_Evictions += held + 1 - m_Cache.Count;
            }

            return made;
        }

        public void Clear()
        {
            lock (m_Lock)
                m_Cache.Clear();
        }
    }

    /// <summary>
    /// The glyphs of a font as meshes (0.12.75). The flatness of the curves is a share of the em, in buckets of a half power of two (so that a letter of 0.3 m and one of 3 m with the same flatness in
    /// metres do not share a glyph but two of the same relative flatness do): a bucket's own flatness is never coarser than the one asked for. Pure.
    /// </summary>
    public static class TextGlyphs
    {
        /// <summary>The relative flatnesses (of the em) that are asked for lie in this range: below it a curve would need more than the 16 segments there are, above it a letter is a polygon.</summary>
        public const float MinRelative = 1e-5f, MaxRelative = 0.05f;

        /// <summary>The bucket of a flatness as a share of the em: the largest bucket whose own flatness does not exceed it.</summary>
        public static int Bucket(float relativeFlatness)
        {
            double r = Math.Max(MinRelative, Math.Min(MaxRelative, relativeFlatness));
            // The bucket's own flatness comes back as a float: its rounding (1e-7 of it) must not drop it into the bucket below.
            return (int)Math.Floor(2.0 * Math.Log(r, 2.0) + 1e-6);
        }

        /// <summary>The flatness (a share of the em) a bucket cuts with.</summary>
        public static float BucketFlatness(int bucket) => (float)Math.Pow(2.0, bucket / 2.0);

        /// <summary>The mesh of a character at a flatness bucket; null when the font has no outline for it (a space has none).</summary>
        public static GlyphMesh Build(FontOutlines font, int codepoint, int bucket)
        {
            double flatness = BucketFlatness(bucket) * font.UnitsPerEm;
            if (!font.TryGlyph(codepoint, flatness, out GlyphOutline outline) || outline.IsEmpty)
                return null;
            return FromOutline(outline, font.UnitsPerEm);
        }

        /// <summary>The contours of an outline in em units as the font drew them (their winding says what is filled), with the points that repeat and the contours of no area left out.</summary>
        internal static List<List<Point2>> ContoursOf(GlyphOutline outline, float unitsPerEm)
        {
            var contours = new List<List<Point2>>();
            foreach (float[] c in outline.Contours)
            {
                var loop = new List<Point2>(c.Length / 2);
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    var p = new Point2(c[i] / unitsPerEm, c[i + 1] / unitsPerEm);
                    if (loop.Count == 0 || Math.Abs(p.X - loop[loop.Count - 1].X) + Math.Abs(p.Y - loop[loop.Count - 1].Y) > 1e-7f)
                        loop.Add(p);
                }

                while (loop.Count > 1 && Math.Abs(loop[0].X - loop[loop.Count - 1].X) + Math.Abs(loop[0].Y - loop[loop.Count - 1].Y) <= 1e-7f)
                    loop.RemoveAt(loop.Count - 1);
                if (loop.Count >= 3 && Math.Abs(CutOutline.Area(loop)) > 1e-9f)
                    contours.Add(loop);
            }

            return contours;
        }

        /// <summary>The mesh of an outline in font units: the contours as outer loops and holes (the dominant winding is the outer's, as MarkingLetters has it), in em units.</summary>
        public static GlyphMesh FromOutline(GlyphOutline outline, float unitsPerEm)
        {
            List<List<Point2>> contours = ContoursOf(outline, unitsPerEm);
            if (contours.Count == 0)
                return null;
            int largest = 0;
            for (int i = 1; i < contours.Count; i++)
            {
                if (Math.Abs(CutOutline.Area(contours[i])) > Math.Abs(CutOutline.Area(contours[largest])))
                    largest = i;
            }

            bool outerPositive = CutOutline.Area(contours[largest]) > 0f;
            // Outer loops counter-clockwise, holes clockwise, whichever way the font drew them.
            var outers = new List<List<Point2>>();
            var holes = new List<List<Point2>>();
            foreach (List<Point2> contour in contours)
            {
                bool outer = (CutOutline.Area(contour) > 0f) == outerPositive;
                if ((CutOutline.Area(contour) > 0f) != outer)
                    contour.Reverse();
                (outer ? outers : holes).Add(contour);
            }

            // A hole that no outer holds is a shape of its own (the font drew it the other way round): CutOutline.Triangulate would leave it out.
            var loops = new List<List<Point2>>(outers);
            foreach (List<Point2> hole in holes)
            {
                float mx = (hole[0].X + hole[1].X) * 0.5f, my = (hole[0].Y + hole[1].Y) * 0.5f;
                bool held = outers.Exists(o => Inside(o, mx, my));
                if (!held)
                    hole.Reverse();
                loops.Add(hole);
            }

            return FromLoops(loops, outline.Advance / unitsPerEm);
        }

        /// <summary>Whether a point is inside a loop (even-odd).</summary>
        internal static bool Inside(IList<Point2> loop, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                if ((loop[i].Y > y) != (loop[j].Y > y) && x < (loop[j].X - loop[i].X) * (y - loop[i].Y) / (loop[j].Y - loop[i].Y) + loop[i].X)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>
        /// A mesh from loops that are outer counter-clockwise and holes clockwise (any units: a glyph's em): triangulated by CutOutline, counted, and boxed.
        /// </summary>
        public static GlyphMesh FromLoops(List<List<Point2>> loops, float advance)
        {
            var mesh = new GlyphMesh { Loops = loops, Advance = advance };
            mesh.MinX = mesh.MinY = float.MaxValue;
            mesh.MaxX = mesh.MaxY = float.MinValue;
            double area = 0.0, perimeter = 0.0;
            foreach (List<Point2> loop in loops)
            {
                area += CutOutline.Area(loop);
                mesh.LoopPoints += loop.Count;
                for (int i = 0; i < loop.Count; i++)
                {
                    Point2 p = loop[i], q = loop[(i + 1) % loop.Count];
                    mesh.MinX = Math.Min(mesh.MinX, p.X);
                    mesh.MaxX = Math.Max(mesh.MaxX, p.X);
                    mesh.MinY = Math.Min(mesh.MinY, p.Y);
                    mesh.MaxY = Math.Max(mesh.MaxY, p.Y);
                    perimeter += Math.Sqrt((double)(q.X - p.X) * (q.X - p.X) + (double)(q.Y - p.Y) * (q.Y - p.Y));
                }
            }

            mesh.Area = (float)area;
            mesh.Perimeter = (float)perimeter;
            var points = new List<Point2>();
            var triangles = new List<int>();
            CutOutline.Triangulate(loops, points, triangles);
            mesh.Points = points.ToArray();
            mesh.Triangles = triangles.ToArray();
            mesh.FaceVertices = mesh.Points.Length;
            mesh.FaceTriangles = mesh.Triangles.Length / 3;
            var rim = new MeshBuilder();
            Prims.LoopsRim(rim, loops, 0f, 1f, 0f, 0f, true);
            mesh.RimVertices = rim.VertexCount;
            mesh.RimTriangles = rim.TriangleCount;
            return mesh;
        }
    }
}
