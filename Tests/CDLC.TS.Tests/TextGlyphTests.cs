using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The glyph meshes of a text (TextGlyphs, GlyphCache) and the rear frame of its channel letters (LetterFrame).</summary>
    internal static class TextGlyphTests
    {
        private const float Eps = 1e-4f;

        public static void RunAll()
        {
            Program.Run("glyphs: the flatness buckets are never coarser than asked for", Buckets);
            Program.Run("glyphs: loops, holes and areas of real characters", Loops);
            Program.Run("glyphs: every mesh is sound at three flatnesses", Sound);
            Program.Run("glyphs: the face, the back and the rim as the cut board makes them", Emission);
            Program.Run("glyphs: the cache keeps, counts, evicts by second chance and is the same for every user", Cache);
            Program.Run("frame: islands (a hole is one, strokes are three, strokes that cross or touch are one)", Islands);
            Program.Run("frame: the rails are at the heights that cross the most islands", RailHeights);
            Program.Run("frame: studs inside the filled area, bridges for a dot, the rails' extent and pieces, ties", Studs);
            Program.Run("frame: the effective area, the fill factor and the estimated mass", Figures);
            Program.Run("frame: real text: every stud in a filled triangle, nothing floating", RealText);
        }

        // ------------------------------------------------------------------ the glyphs

        private static void Buckets()
        {
            var random = new Random(3);
            bool never = true, near = true;
            for (int i = 0; i < 500; i++)
            {
                float r = (float)(TextGlyphs.MinRelative * Math.Pow(TextGlyphs.MaxRelative / TextGlyphs.MinRelative, random.NextDouble()));
                float own = TextGlyphs.BucketFlatness(TextGlyphs.Bucket(r));
                never &= own <= r * 1.000001f;
                near &= own > r / 1.4143f * 0.999999f;
            }

            Program.Check(never, "a bucket's flatness never exceeds the one asked for");
            Program.Check(near, "and is within a factor of the square root of 2 of it");
            Program.Check(TextGlyphs.Bucket(TextGlyphs.BucketFlatness(-14)) == -14 && TextGlyphs.Bucket(TextGlyphs.BucketFlatness(-9)) == -9, "a bucket's own flatness is in its bucket");
            Program.Check(TextGlyphs.Bucket(0f) == TextGlyphs.Bucket(TextGlyphs.MinRelative) && TextGlyphs.Bucket(10f) == TextGlyphs.Bucket(TextGlyphs.MaxRelative), "asked outside the range, the ends");
            Program.Check(TextGlyphs.Bucket(0.002f) > TextGlyphs.Bucket(0.0005f), "finer is a lower bucket");
        }

        private static GlyphMesh Mesh(FontOutlines font, char c, float relative = 0.002f) => TextGlyphs.Build(font, c, TextGlyphs.Bucket(relative));

        private static void Loops()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            GlyphMesh kou = Mesh(font, '口');
            Program.Check(kou != null && kou.Loops.Count == 2, "口 has two loops");
            if (kou == null || kou.Loops.Count != 2)
                return;
            float a0 = CutOutline.Area(kou.Loops[0]), a1 = CutOutline.Area(kou.Loops[1]);
            Program.Check((a0 > 0f) != (a1 > 0f), "one of them is a hole: they wind the opposite ways");
            Program.Near(kou.Area, a0 + a1, 1e-6f, "the filled area is the outer's less the hole's");
            Program.Check(kou.Area > 0f && kou.Area < 1f, "an area of an em square's part: " + kou.Area);
            var outer = a0 > 0f ? kou.Loops[0] : kou.Loops[1];
            var hole = a0 > 0f ? kou.Loops[1] : kou.Loops[0];
            Program.Check(CutOutline.Area(outer) > 0f && CutOutline.Area(hole) < 0f, "outer counter-clockwise, hole clockwise");
            Program.Check(TextGlyphs.Inside(outer, hole[0].X, hole[0].Y) || TextGlyphs.Inside(outer, (hole[0].X + hole[1].X) * 0.5f, (hole[0].Y + hole[1].Y) * 0.5f), "the hole is inside the outer");
            Program.Check(kou.Advance > 0.5f && kou.MinY < kou.MaxY && kou.MinX < kou.MaxX, "an advance and a box");

            // 小 is three strokes, each a loop of its own, and no hole.
            GlyphMesh xiao = Mesh(font, '小');
            Program.Check(xiao != null && xiao.Loops.TrueForAll(l => CutOutline.Area(l) > 0f), "小 has outer loops only");

            // A solid bar has four corners, and a finer cut never changes a straight edge.
            GlyphMesh bar = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0.4f, 0f, 0.6f, 0.7f) }, 1f);
            Program.Check(bar.Loops.Count == 1 && bar.Loops[0].Count == 4 && bar.FaceVertices == 4 && bar.FaceTriangles == 2, "a bar: four vertices, two triangles");
            Program.Near(bar.Area, 0.14f, Eps, "its area");
            Program.Near(bar.Perimeter, 1.8f, Eps, "its perimeter");
            Program.Check(bar.RimTriangles == 8 && bar.RimVertices == 16, "its rim: four flat sides of two triangles (every corner is sharp: no shared vertices)");

            // Deterministic: the same mesh twice.
            GlyphMesh again = Mesh(font, '口');
            Program.Check(again.Points.Length == kou.Points.Length && SameInts(again.Triangles, kou.Triangles), "the same glyph twice is the same mesh");
            Program.Check(Mesh(font, ' ') == null && TextGlyphs.Build(font, 0x2603, TextGlyphs.Bucket(0.002f)) == null, "a space and a missing character have no mesh");
            // A finer flatness has more loop points.
            Program.Check(Mesh(font, '国', 0.0002f).LoopPoints >= Mesh(font, '国', 0.005f).LoopPoints, "a finer flatness has at least as many points");
        }

        private static List<Point2> Rect(float x0, float y0, float x1, float y1, bool clockwise = false) =>
            clockwise ? new List<Point2> { new Point2(x0, y0), new Point2(x0, y1), new Point2(x1, y1), new Point2(x1, y0) } : new List<Point2> { new Point2(x0, y0), new Point2(x1, y0), new Point2(x1, y1), new Point2(x0, y1) };

        private static bool SameInts(int[] a, int[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        internal const string Sample = "永远发光字体灯箱路牌停公交铁路出租车校快速多乘员人慢学口小回国日目田三十ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        private static void Sound()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            int made = 0;
            var bad = new List<string>();
            foreach (float relative in new[] { 0.0007f, 0.002f, 0.005f })
            {
                foreach (char c in Sample)
                {
                    GlyphMesh mesh = Mesh(font, c, relative);
                    if (mesh == null)
                        continue;
                    made++;
                    if (!mesh.IsSound(out string why))
                        bad.Add($"{c}@{relative}: {why}");
                }
            }

            Program.Check(made >= 3 * 40, "the sample has glyphs: " + made);
            Program.Check(bad.Count == 0, bad.Count + " mesh(es) not sound: " + string.Join("; ", bad.GetRange(0, Math.Min(5, bad.Count))));

            // The check does find faults: a triangle of no area, an index out of range, a missing piece.
            GlyphMesh good = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0f, 0f, 1f, 1f) }, 1f);
            Program.Check(good.IsSound(out _), "a square is sound");
            var cut = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0f, 0f, 1f, 1f) }, 1f);
            cut.Triangles = new[] { cut.Triangles[0], cut.Triangles[1], cut.Triangles[2] };
            Program.Check(!cut.IsSound(out string missing) && missing.Contains("cover"), "a triangle left out is found: " + missing);
            var flat = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0f, 0f, 1f, 1f) }, 1f);
            flat.Triangles = new[] { 0, 0, 1, 0, 1, 2, 0, 2, 3 };
            Program.Check(!flat.IsSound(out _), "a triangle of no area is found");
            var range = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0f, 0f, 1f, 1f) }, 1f);
            range.Triangles = new[] { 0, 1, 9, 0, 2, 3 };
            Program.Check(!range.IsSound(out _), "an index out of range is found");
        }

        private static void Emission()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            foreach (char c in "口国永A")
            {
                GlyphMesh mesh = Mesh(font, c);
                if (mesh == null)
                    continue;
                var face = new MeshBuilder();
                var back = new MeshBuilder();
                mesh.AddTo(face, back, 0.3f, 0.1f, -0.05f, 0f, 0.03f, true);
                Program.Check(face.VertexCount == mesh.FaceVertices && face.TriangleCount == mesh.FaceTriangles, "the face of " + c + " has what was counted");
                Program.Check(back.VertexCount == mesh.FaceVertices + mesh.RimVertices && back.TriangleCount == mesh.FaceTriangles + mesh.RimTriangles, "the back and the rim of " + c + " have what was counted");
                Program.Check(mesh.Vertices == face.VertexCount + back.VertexCount && mesh.TriangleCount == face.TriangleCount + back.TriangleCount, "the totals of " + c);

                // The cut board's own way (Prims.LoopsFace on the loops placed) gives the same face: the same vertices, the same triangles, the same winding.
                var reference = new MeshBuilder();
                Prims.LoopsFace(reference, mesh.Placed(0.3f, 0.1f, -0.05f), 0.03f, true, 0f, 0f, null);
                Program.Check(reference.VertexCount == face.VertexCount && reference.TriangleCount == face.TriangleCount, "the same size as Prims.LoopsFace: " + c);
                Program.Check(SamePositions(reference, face), "the same vertices as Prims.LoopsFace: " + c);
                Program.Check(Facing(face, 1f) && Facing(reference, 1f), "the face's triangles face the viewer: " + c);
                // The back's own triangles (the first of its builder; the rim follows) face away.
                var backOnly = new MeshBuilder();
                backOnly.Positions.AddRange(back.Positions.GetRange(0, mesh.FaceVertices));
                backOnly.Indices.AddRange(back.Indices.GetRange(0, mesh.FaceTriangles * 3));
                Program.Check(Facing(backOnly, -1f), "the back's triangles face away from the viewer: " + c);
                Program.Check(!Facing(backOnly, 1f) || mesh.FaceTriangles == 0, "and not toward it: " + c);
                var rim = new MeshBuilder();
                Prims.LoopsRim(rim, mesh.Placed(0.3f, 0.1f, -0.05f), 0f, 0.03f, 0f, 0f, true);
                Program.Check(rim.VertexCount == mesh.RimVertices && rim.TriangleCount == mesh.RimTriangles, "the rim as Prims.LoopsRim makes it, in the same count: " + c);
            }
        }

        private static bool SamePositions(MeshBuilder a, MeshBuilder b)
        {
            var left = new List<string>();
            var right = new List<string>();
            foreach (var p in a.Positions)
                left.Add($"{p.X:0.00000},{p.Y:0.00000},{p.Z:0.00000}");
            foreach (var p in b.Positions)
                right.Add($"{p.X:0.00000},{p.Y:0.00000},{p.Z:0.00000}");
            left.Sort(StringComparer.Ordinal);
            right.Sort(StringComparer.Ordinal);
            return left.Count == right.Count && left.SequenceEqual(right);
        }

        private static bool Facing(MeshBuilder b, float z)
        {
            for (int t = 0; t + 2 < b.Indices.Count; t += 3)
            {
                var n = MeshBuilder.Cross(b.Positions[b.Indices[t + 1]] - b.Positions[b.Indices[t]], b.Positions[b.Indices[t + 2]] - b.Positions[b.Indices[t]]);
                if (n.Z * z < 0f)
                    return false;
            }

            return true;
        }

        private static void Cache()
        {
            GlyphMesh one = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0f, 0f, 1f, 1f) }, 1f);
            long each = one.Bytes;
            var cache = new GlyphCache(each * 3 + each / 2);
            int built = 0;
            GlyphMesh Make() { built++; return TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0f, 0f, 1f, 1f) }, 1f); }
            GlyphKey K(int c) => new GlyphKey(1, c, -14);

            GlyphMesh a1 = cache.GetOrBuild(K(1), Make);
            GlyphMesh a2 = cache.GetOrBuild(K(1), Make);
            Program.Check(built == 1 && ReferenceEquals(a1, a2), "the second asking is a hit and the same object");
            Program.Check(cache.Hits == 1 && cache.Misses == 1 && cache.Count == 1 && cache.Bytes == each, "the counters");
            Program.Check(cache.GetOrBuild(new GlyphKey(2, 1, -14), Make) != null && cache.GetOrBuild(new GlyphKey(1, 1, -13), Make) != null && built == 3, "another font or another bucket is another glyph");
            // Two keys of another font and character whose hashes collide are two glyphs: the key's equality, not only its hash, tells them apart.
            var keyA = new GlyphKey(1, 5, -14);
            var keyB = new GlyphKey(2, 5 ^ 397 ^ 794, -14);
            Program.Check(keyA.GetHashCode() == keyB.GetHashCode() && !keyA.Equals(keyB), "the two keys collide in the hash and differ");
            var fresh = new GlyphCache(each * 10);
            int made = 0;
            fresh.GetOrBuild(keyA, () => { made++; return Make(); });
            fresh.GetOrBuild(keyB, () => { made++; return Make(); });
            Program.Check(made == 2 && fresh.Count == 2, "and are kept apart: " + made);

            cache.Clear();
            cache = new GlyphCache(each * 3 + each / 2);
            built = 0;
            cache.GetOrBuild(K(1), Make);
            cache.GetOrBuild(K(2), Make);
            cache.GetOrBuild(K(3), Make);
            cache.GetOrBuild(K(1), Make);
            cache.GetOrBuild(K(4), Make);
            Program.Check(cache.Count == 3 && cache.Evictions == 1, "over the budget one goes: " + cache.Count + " held, " + cache.Evictions + " evicted");
            int before = built;
            cache.GetOrBuild(K(1), Make);
            Program.Check(built == before, "the glyph that was asked for since it was kept survived (second chance)");
            cache.GetOrBuild(K(2), Make);
            Program.Check(built == before + 1, "the oldest that was not asked for again went");
            Program.Check(cache.Bytes <= each * 3 + each / 2, "the budget holds: " + cache.Bytes);

            // A character with no outline is kept as nothing, and not built again.
            int nulls = 0;
            cache.GetOrBuild(K(99), () => { nulls++; return null; });
            Program.Check(cache.GetOrBuild(K(99), () => { nulls++; return null; }) == null && nulls == 1, "no glyph is remembered too");

            // One glyph larger than the whole budget is still held (the cache never empties itself).
            var tiny = new GlyphCache(1L);
            Program.Check(tiny.GetOrBuild(K(1), Make) != null && tiny.Count == 1, "a glyph over the budget is kept alone");
            tiny.GetOrBuild(K(2), Make);
            Program.Check(tiny.Count == 1 && tiny.Evictions == 1, "and replaced by the next");
            cache.Clear();
            Program.Check(cache.Count == 0 && cache.Bytes == 0, "clear empties it");
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>The synthetic letters (em units, the baseline at 0, ink up to 0.7): a bar, a ring, three strokes, a dot, a cross, two touching boxes, a ring with an island, a short bar.</summary>
        private static readonly Dictionary<int, GlyphMesh> Letters = MakeLetters();

        private static Dictionary<int, GlyphMesh> MakeLetters()
        {
            List<List<Point2>> One(List<Point2> l) => new List<List<Point2>> { l };
            var d = new Dictionary<int, GlyphMesh>();
            d['I'] = TextGlyphs.FromLoops(One(Rect(0.4f, 0f, 0.6f, 0.7f)), 1f);
            d['O'] = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0.1f, 0f, 0.9f, 0.7f), Rect(0.3f, 0.2f, 0.7f, 0.5f, true) }, 1f);
            d['X'] = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0.1f, 0.2f, 0.25f, 0.5f), Rect(0.45f, 0f, 0.55f, 0.7f), Rect(0.75f, 0.2f, 0.9f, 0.5f) }, 1f);
            d['.'] = TextGlyphs.FromLoops(One(Rect(0.4f, 0f, 0.55f, 0.15f)), 1f);
            d['+'] = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0.1f, 0.3f, 0.9f, 0.45f), Rect(0.4f, 0f, 0.6f, 0.7f) }, 1f);
            d['T'] = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0f, 0f, 0.4f, 0.3f), Rect(0.4f, 0f, 0.8f, 0.3f) }, 1f);
            d['R'] = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0.05f, 0f, 0.95f, 0.7f), Rect(0.15f, 0.1f, 0.85f, 0.6f, true), Rect(0.35f, 0.25f, 0.65f, 0.45f) }, 1f);
            d['s'] = TextGlyphs.FromLoops(One(Rect(0.3f, 0f, 0.6f, 0.1f)), 1f);
            // A low mark, a high one and a middle one: no two rails 0.4 of a letter apart cross all three.
            d['l'] = TextGlyphs.FromLoops(One(Rect(0.4f, 0f, 0.55f, 0.1f)), 1f);
            d['h'] = TextGlyphs.FromLoops(One(Rect(0.4f, 0.6f, 0.55f, 0.7f)), 1f);
            d['m'] = TextGlyphs.FromLoops(One(Rect(0.3f, 0.3f, 0.7f, 0.4f)), 1f);
            // A ring whose sides are 1.2 % of the em (3.6 mm at 0.3 m): too thin for a stud, thick bars above and below.
            d['r'] = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0.2f, 0f, 0.8f, 0.7f), Rect(0.212f, 0.15f, 0.788f, 0.55f, true) }, 1f);
            return d;
        }

        private static readonly TextFace Face = new TextFace(1000f, -200f, 800f, c => Letters.ContainsKey(c) ? 1000f : -1f);

        /// <summary>A text of the synthetic letters, one em wide each, em = letter height.</summary>
        private static FramePlan Plan(string text, float letterHeight = 0.3f, TextAlign align = TextAlign.Left)
        {
            TextLayoutResult layout = TextLayout.Layout(text, TextStyle.Of(letterHeight), Face);
            return LetterFrame.Plan(layout, c => Letters.TryGetValue(c, out GlyphMesh m) ? m : null, FrameStyle.For(letterHeight));
        }

        private static void Islands()
        {
            FramePlan o = Plan("O");
            Program.Check(o.Islands.Count == 1 && o.Islands[0].Pieces.Count == 1 && o.Islands[0].Pieces[0].Holes.Count == 1, "口 is one island with a hole");
            Program.Check(Plan("X").Islands.Count == 3, "three strokes are three islands");
            Program.Check(Plan("+").Islands.Count == 1, "two strokes that cross are one island");
            Program.Check(Plan("T").Islands.Count == 1, "two boxes that touch along an edge are one island");
            FramePlan r = Plan("R");
            Program.Check(r.Islands.Count == 2 && r.Islands.Exists(i => i.Pieces[0].Holes.Count == 1) && r.Islands.Exists(i => i.Pieces[0].Holes.Count == 0), "a ring with a square in its hole is two islands");
            Program.Check(Plan("II").Islands.Count == 2 && Plan("II").Islands[0].Glyph != Plan("II").Islands[1].Glyph, "the letters of a line are islands of their own");
            // The filled test: the middle of the hole is not in it, the ring is.
            FrameIsland island = o.Islands[0];
            float cx = island.MinX + (island.MaxX - island.MinX) * 0.5f, cy = island.MinY + (island.MaxY - island.MinY) * 0.5f;
            Program.Check(!island.Contains(cx, cy) && island.Contains(island.MinX + 0.01f, cy), "the middle of a ring is not filled, its side is");
            Program.Check(Plan("").Islands.Count == 0 && Plan("   ").Islands.Count == 0, "no letters, no islands");
        }

        private static void RailHeights()
        {
            FramePlan bars = Plan("IIII");
            Program.Check(bars.RailHeights.Count == 1, "one line, one pair of rails");
            (int line, float low, float high) = bars.RailHeights[0];
            float h = 0.3f;
            // Every bar spans 0 to 0.7 em of the 0.3 m letters: the rails are in it, and at least 0.4 of a letter apart.
            float inkLow = LetterBaseline(h), inkHigh = inkLow + 0.7f * h;
            Program.Check(low > inkLow && high < inkHigh && low < high, "the rails are inside the ink: " + low + ", " + high);
            Program.Check(high - low >= 0.4f * h - 1e-5f, "at least 0.4 of the letter height apart: " + (high - low) / h);
            Program.Check(bars.BridgedIslands == 0 && bars.FloatingIslands == 0 && bars.SingleStudIslands == 0, "four bars, all crossed by both rails");

            // A tall bar and a short one (0.1 em): the lower rail must be where the short one is, though a pair near a quarter and three quarters of the ink would miss it.
            FramePlan mixed = Plan("Is");
            (_, float mlow, float mhigh) = mixed.RailHeights[0];
            float shortTop = LetterBaseline(h) + 0.1f * h;
            Program.Check(mlow < shortTop, "the lower rail crosses the short bar: " + mlow + " under " + shortTop);
            Program.Check(mixed.BridgedIslands == 0, "so no island is left to a bridge");
            Program.Check(mhigh - mlow >= 0.4f * h - 1e-5f, "the gap holds");

            // Two lines have a pair each.
            Program.Check(Plan("I\nI").RailHeights.Count == 2, "a pair for each line");
            Program.Check(Plan("I\nI").RailHeights[0].Low > Plan("I\nI").RailHeights[1].High, "the first line is above the second");
        }

        /// <summary>The baseline height of the lone line of a text of this letter height, as the layout puts it (the box is the letter height, its middle at 0).</summary>
        private static float LetterBaseline(float letterHeight) => letterHeight * 0.5f - 0.8f * letterHeight;

        private static void Studs()
        {
            FramePlan bars = Plan("IIII");
            // The bars are 0.2 em wide, 6 cm at 0.3 m: narrower than the gap of two studs, so each gets one stud on a rail and a second on the other rail.
            Program.Check(bars.Studs.Count == 8, "four narrow bars have two studs each: " + bars.Studs.Count);
            foreach (FrameStud s in bars.Studs)
                Program.Check(bars.Islands[s.Island].Contains(s.At.X, s.At.Y), "a stud is in the filled area");

            // A wide bar (a line of the ring's top) gets two studs at its ends on one rail.
            FramePlan ring = Plan("O");
            Program.Check(ring.Studs.Count >= 2 && ring.Studs.TrueForAll(s => ring.Islands[s.Island].Contains(s.At.X, s.At.Y)), "the ring's studs are in its filled area, none in the hole");

            // A dot at the foot of a bar is crossed by the lower rail (the rails go where they cross the most islands): no bridge.
            FramePlan foot = Plan("I.");
            Program.Check(foot.BridgedIslands == 0 && foot.Bridges.Count == 0 && foot.FloatingIslands == 0, "a dot a rail crosses needs no bridge");
            Program.Check(foot.Studs.Exists(s => foot.Islands[s.Island].Glyph == 1 && foot.Islands[s.Island].Contains(s.At.X, s.At.Y)), "and has a stud inside it");

            // A low mark, a high one and a middle one: two rails cross two of them, and the third is held by its studs and rods to the nearest rail.
            FramePlan dot = Plan("lhm");
            Program.Check(dot.Islands.Count == 3 && dot.BridgedIslands == 1 && dot.FloatingIslands == 0 && dot.Bridges.Count >= 1, "one of three marks is bridged: " + dot.BridgedIslands);
            var rails = dot.RailHeights[0];
            Program.Check(dot.Bridges.TrueForAll(b => Math.Abs(b.A.X - b.B.X) < Eps && (Math.Abs(b.B.Y - rails.Low) < Eps || Math.Abs(b.B.Y - rails.High) < Eps) && b.Length > 0f), "each rod is upright and ends on a rail");
            Program.Check(dot.Bridges.TrueForAll(b => Math.Abs(Math.Abs(b.B.Y - b.A.Y) - Math.Min(Math.Abs(b.A.Y - rails.Low), Math.Abs(b.A.Y - rails.High))) < Eps), "to the nearer rail");
            var bridgedStuds = dot.Studs.FindAll(s => dot.Bridges.Exists(b => Math.Abs(b.A.X - s.At.X) < Eps && Math.Abs(b.A.Y - s.At.Y) < Eps));
            Program.Check(bridgedStuds.Count >= 1 && bridgedStuds.TrueForAll(s => dot.Islands[s.Island].Contains(s.At.X, s.At.Y)), "a rod starts at a stud inside the bridged mark");
            Program.Check(dot.FrameLength > dot.Rails.Sum(r => r.Length), "the rods are part of the frame's length");

            // The rails: one common length, the studs' extent and an overhang each end, in pieces of at most 3 m.
            float h = 1.0f;
            FramePlan wide = Plan("IIIIIIIIIIII", h);
            float x0 = float.MaxValue, x1 = float.MinValue;
            foreach (FrameStud s in wide.Studs)
            {
                x0 = Math.Min(x0, s.At.X);
                x1 = Math.Max(x1, s.At.X);
            }

            var low = wide.Rails.FindAll(s => Math.Abs(s.A.Y - wide.RailHeights[0].Low) < Eps);
            Program.Near(low[0].A.X, x0 - 0.05f, Eps, "the rail starts an overhang before the first stud");
            Program.Near(low[low.Count - 1].B.X, x1 + 0.05f, Eps, "and ends one after the last");
            float total = 0f;
            bool joined = true;
            for (int i = 0; i < low.Count; i++)
            {
                total += low[i].Length;
                joined &= i == 0 || Math.Abs(low[i].A.X - low[i - 1].B.X) < Eps;
            }

            int pieces = (int)Math.Ceiling((x1 - x0 + 0.1f) / 3f - 1e-4f);
            Program.Check(pieces >= 4 && low.Count == pieces && low.TrueForAll(s => s.Length <= 3f + Eps) && joined, $"{x1 - x0 + 0.1f:0.0} m of rail is {pieces} pieces of at most 3 m, end to end: {low.Count}");
            Program.Near(total, x1 - x0 + 0.1f, 1e-3f, "and their length is the extent");
            Program.Check(wide.Rails.Count == 2 * pieces && wide.Ties.Count == 0, "two rails of the same pieces, no ties for one line");

            FramePlan lines = Plan("II\nII");
            Program.Check(lines.Ties.Count == 2 && lines.Ties.TrueForAll(t => Math.Abs(t.A.X - t.B.X) < Eps), "two upright ties for two lines");
            float lowest = Math.Min(lines.RailHeights[0].Low, lines.RailHeights[1].Low), highest = Math.Max(lines.RailHeights[0].High, lines.RailHeights[1].High);
            Program.Check(Math.Abs(Math.Min(lines.Ties[0].A.Y, lines.Ties[0].B.Y) - lowest) < Eps && Math.Abs(Math.Max(lines.Ties[0].A.Y, lines.Ties[0].B.Y) - highest) < Eps, "from the lowest rail to the highest");
            Program.Check(lines.Rails.FindAll(s => s.Line == 0).Count == 2 && lines.Rails.FindAll(s => s.Line == 1).Count == 2, "two rails of each line");
            Program.Near(lines.Rails[0].A.X, lines.Rails[2].A.X, Eps, "all the rails are as long as each other");

            // The thin sides of a ring are not studded: a span the hole cuts is as narrow as the side, and a stud at the span's inset would stand in the hole.
            FramePlan thinRing = Plan("r");
            Program.Check(thinRing.Studs.Count > 0 && thinRing.Studs.TrueForAll(s => thinRing.Islands[s.Island].Contains(s.At.X, s.At.Y)), "a ring with thin sides has its studs in the filled area: " + thinRing.Studs.Count);
            Program.Check(thinRing.Studs.TrueForAll(s => s.At.Y < thinRing.Islands[0].MinY + 0.15f * 0.3f || s.At.Y > thinRing.Islands[0].MinY + 0.55f * 0.3f), "in its bars, none at the height of the hole");

            // A text with a mark too narrow for a stud: it floats, and says so.
            var thin = new Dictionary<int, GlyphMesh> { ['-'] = TextGlyphs.FromLoops(new List<List<Point2>> { Rect(0.4f, 0.3f, 0.4005f, 0.5f) }, 1f) };
            TextLayoutResult l = TextLayout.Layout("-", TextStyle.Of(0.3f), new TextFace(1000f, -200f, 800f, c => 1000f));
            FramePlan floating = LetterFrame.Plan(l, c => thin.TryGetValue(c, out GlyphMesh m) ? m : null, FrameStyle.For(0.3f));
            Program.Check(floating.FloatingIslands == 1 && floating.Studs.Count == 0, "a mark of 0.15 mm cannot hold a stud: floating");
        }

        private static void Figures()
        {
            FramePlan o = Plan("O", 0.3f);
            float em2 = 0.3f * 0.3f;
            Program.Near(o.EffectiveArea, (0.8f * 0.7f - 0.4f * 0.3f) * em2, 1e-6f, "the ring's filled area");
            Program.Near(o.InkWidth, 0.8f * 0.3f, 1e-5f, "the ink's width");
            Program.Near(o.InkHeight, 0.7f * 0.3f, 1e-5f, "the ink's height");
            Program.Near(o.FillFactor, 0.44f / 0.56f, 1e-4f, "the fill factor is the area over the ink's box");
            Program.Near(o.Perimeter, 4.4f * 0.3f, 1e-4f, "the loops' length");
            FrameStyle style = FrameStyle.For(0.3f);
            MassModel m = style.Mass;
            float tube = (style.RailSize / 0.04f) * (style.RailSize / 0.04f);
            float expected = o.EffectiveArea * (m.FaceKgPerM2 + m.LedKgPerM2) + o.Perimeter * style.Depth * m.ReturnKgPerM2 + o.FrameLength * m.RailKgPerM * tube + o.Studs.Count * m.StudKg;
            Program.Near(o.MassKg, expected, 1e-4f, "the mass is the formula's");
            Program.Check(o.MassKg > 0f && o.FrameLength > 0f, "a mass and a frame");
            Program.Check(Plan("OOO").MassKg > Plan("OO").MassKg && Plan("OO").MassKg > o.MassKg, "more letters weigh more");
            float twice = Plan("O", 0.6f).MassKg / o.MassKg;
            Program.Check(twice > 2f && twice < 4f, "a letter twice as high weighs more than twice and less than four times (the face grows with the square, the return's depth and the rails with the size): " + twice);
            Program.Check(Plan("").MassKg == 0f && Plan("").EffectiveArea == 0f, "nothing weighs nothing");
            Program.Check(LetterFrame.EstimatedMass(o, style) == o.MassKg, "EstimatedMass is what the plan holds");
        }

        private static void RealText()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            var cache = new GlyphCache(8L * 1024 * 1024);
            TextFace face = TextFace.Of(font);
            var random = new Random(75);
            int studs = 0, outside = 0, floating = 0, islands = 0, lines = 0;
            var examples = new List<string>();
            for (int n = 0; n < 40; n++)
            {
                var chars = new List<char>();
                int count = 1 + random.Next(12);
                for (int i = 0; i < count; i++)
                    chars.Add(TextGlyphTestsSample(random));
                string text = new string(chars.ToArray()) + (n % 3 == 0 ? "\n" + new string(chars.ToArray(), 0, Math.Min(4, chars.Count)) : "");
                float height = new[] { 0.3f, 0.5f, 1f }[n % 3];
                TextLayoutResult layout = TextLayout.Layout(text, TextStyle.Of(height), face);
                int bucket = TextGlyphs.Bucket(0.0005f / height * 1f + 0.002f);
                Func<int, GlyphMesh> meshOf = c => cache.GetOrBuild(new GlyphKey(1, c, bucket), () => TextGlyphs.Build(font, c, bucket));
                FramePlan plan = LetterFrame.Plan(layout, meshOf, FrameStyle.For(height));
                lines += layout.Lines;
                islands += plan.Islands.Count;
                floating += plan.FloatingIslands;
                if (plan.FloatingIslands > 0 && examples.Count < 4)
                    examples.Add(text.Replace("\n", "/") + "@" + height);
                foreach (FrameStud s in plan.Studs)
                {
                    studs++;
                    PlacedGlyph g = layout.Glyphs[plan.Islands[s.Island].Glyph];
                    if (!InFace(meshOf(g.Codepoint), layout.MetresPerEm, g.X, g.Y, s.At))
                        outside++;
                }
            }

            Program.Check(studs > 100 && islands > 100, $"studs and islands were found: {studs} studs, {islands} islands over {lines} lines");
            Program.Check(outside == 0, outside + " stud(s) of " + studs + " are not in a triangle of the glyph's face (not in the filled area, or in a hole)");
            Program.Check(floating == 0, floating + " island(s) too small for a stud, for example " + string.Join("; ", examples));
        }

        private static char TextGlyphTestsSample(Random random) => Sample[random.Next(Sample.Length)];

        /// <summary>Whether a point is in a triangle of the glyph's face placed at (x, y): the triangulation covers exactly the filled area, so this is the filled area's own test.</summary>
        private static bool InFace(GlyphMesh mesh, float metresPerEm, float x, float y, Point2 p)
        {
            for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
            {
                Point2 a = Place(mesh.Points[mesh.Triangles[t]], metresPerEm, x, y), b = Place(mesh.Points[mesh.Triangles[t + 1]], metresPerEm, x, y), c = Place(mesh.Points[mesh.Triangles[t + 2]], metresPerEm, x, y);
                float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
                bool negative = d1 < -1e-9f || d2 < -1e-9f || d3 < -1e-9f, positive = d1 > 1e-9f || d2 > 1e-9f || d3 > 1e-9f;
                if (!(negative && positive))
                    return true;
            }

            return false;
        }

        private static Point2 Place(Point2 p, float metresPerEm, float x, float y) => new Point2(x + p.X * metresPerEm, y + p.Y * metresPerEm);

        private static float Side(Point2 p, Point2 a, Point2 b) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
    }
}
