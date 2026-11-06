using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>Estimated weights (kg) for the rear frame's mass: not a standard's figures, this mod's guess of the usual materials (a 3 mm acrylic face, LED modules, a 1 mm aluminium return, 4 cm aluminium tube).</summary>
    public struct MassModel
    {
        public float FaceKgPerM2, LedKgPerM2, ReturnKgPerM2, RailKgPerM, StudKg;

        public static MassModel Default => new MassModel { FaceKgPerM2 = 3.6f, LedKgPerM2 = 1.5f, ReturnKgPerM2 = 2.7f, RailKgPerM = 0.8f, StudKg = 0.05f };
    }

    /// <summary>The sizes of the rear frame of channel letters (m).</summary>
    public struct FrameStyle
    {
        /// <summary>A stud's radius; the least width of a letter's filled span that can hold one is four of them.</summary>
        public float StudRadius;

        /// <summary>The rails' square tube, and how far they go beyond the outermost studs, and the longest piece (spliced beyond it).</summary>
        public float RailSize, RailOverhang, MaxRailPiece;

        /// <summary>The two rails of a line are at least this share of the letter height apart.</summary>
        public float MinRailGap;

        /// <summary>How deep a letter's return is (for the mass).</summary>
        public float Depth;

        /// <summary>The heights tried for a rail across the ink of a line.</summary>
        public int RailSteps;

        /// <summary>
        /// A rod from a stud to a rail longer than this (m) is reported to the editor as a warning (FramePlan.LongBridges): LongRodShare of the letter height. Nothing is refused or moved for it, and the
        /// number of rods is not capped (the owner, 2 Nov: no cap on the bridged islands, a warning for a long rod).
        /// </summary>
        public float LongRod;

        /// <summary>
        /// A rod is long over this share of the letters' height. Read off the rods of 3,600 frames of random texts (Tests/Bench/Planner.cs, BENCH_PLANNER=1; letters of 0.3, 1 and 3 m): a rod is 0.16 to 0.19 of the
        /// height at the median, 0.25 to 0.28 at the 95th percentile, 0.30 at the 99th, 0.40 at the most. At 0.25 one text in five of the usual pool would be warned, at 0.30 one in a hundred (0.8 to 1.5 %).
        /// </summary>
        public const float LongRodShare = 0.30f;

        public MassModel Mass;

        /// <summary>The estimates for letters of a height: the studs and the tube smaller for small letters (a 5 mm stud and a 4 cm tube are for letters of 0.4 m and over).</summary>
        public static FrameStyle For(float letterHeight) => new FrameStyle
        {
            StudRadius = Math.Min(0.005f, 0.012f * letterHeight),
            RailSize = Math.Min(0.04f, 0.12f * letterHeight),
            RailOverhang = 0.05f,
            MaxRailPiece = 3f,
            MinRailGap = 0.4f,
            Depth = 0.06f,
            RailSteps = 48,
            LongRod = LongRodShare * letterHeight,
            Mass = MassModel.Default
        };
    }

    /// <summary>A connected filled piece of one letter, in metres: its pieces are outer loops (counter-clockwise) with their holes; two outer loops of one glyph that overlap or touch are one island.</summary>
    public sealed class FrameIsland
    {
        public sealed class Piece
        {
            public List<Point2> Outer;

            /// <summary>The outer loop's area (what CutOutline.Area gives), made once.</summary>
            public float OuterArea;

            public readonly List<List<Point2>> Holes = new List<List<Point2>>();
        }

        public int Line, Glyph;
        public readonly List<Piece> Pieces = new List<Piece>();
        public float MinX = float.MaxValue, MinY = float.MaxValue, MaxX = float.MinValue, MaxY = float.MinValue, Area;

        /// <summary>Whether a point is in the filled area: in an outer loop and in none of its holes.</summary>
        public bool Contains(float x, float y)
        {
            foreach (Piece piece in Pieces)
            {
                if (!TextGlyphs.Inside(piece.Outer, x, y))
                    continue;
                bool inHole = false;
                foreach (List<Point2> hole in piece.Holes)
                    inHole |= TextGlyphs.Inside(hole, x, y);
                if (!inHole)
                    return true;
            }

            return false;
        }
    }

    public struct FrameStud
    {
        public Point2 At;
        public int Island, Line;
    }

    /// <summary>A straight piece of the frame: a rail, a tie between lines, or the bridge from a stud to a rail.</summary>
    public struct FrameSegment
    {
        public Point2 A, B;
        public int Line;

        public float Length => (float)Math.Sqrt((double)(B.X - A.X) * (B.X - A.X) + (double)(B.Y - A.Y) * (B.Y - A.Y));
    }

    /// <summary>The rear frame of a text's channel letters, as data (positions and lengths in metres, y up, the text block's middle at 0, 0), and the figures the support planner and the editor need.</summary>
    public sealed class FramePlan
    {
        public readonly List<FrameIsland> Islands = new List<FrameIsland>();
        public readonly List<FrameStud> Studs = new List<FrameStud>();
        public readonly List<FrameSegment> Rails = new List<FrameSegment>();
        public readonly List<FrameSegment> Ties = new List<FrameSegment>();
        public readonly List<FrameSegment> Bridges = new List<FrameSegment>();

        /// <summary>The heights of each line's two rails, lower first (a line with no ink has none).</summary>
        public readonly List<(int Line, float Low, float High)> RailHeights = new List<(int, float, float)>();

        /// <summary>Islands no rail crosses (held by studs and bridges), islands held by one stud only (too narrow for two), islands too small for any stud (none is allowed in text of a sensible size).</summary>
        public int BridgedIslands, SingleStudIslands, FloatingIslands;

        /// <summary>The filled area of all the letters (m2), the box of the ink (m), their ratio, and the length of all the loops (m).</summary>
        public float EffectiveArea, InkWidth, InkHeight, FillFactor, Perimeter;

        /// <summary>The rails', ties' and bridges' total length (m), and the estimated mass of the letters and their frame (kg: see LetterFrame.EstimatedMass).</summary>
        public float FrameLength, MassKg;

        /// <summary>The longest rod from a stud to a rail (m; 0 when there is none) and how many rods are longer than FrameStyle.LongRod: for the editor's warning.</summary>
        public float LongestBridge;
        public int LongBridges;
    }

    /// <summary>
    /// The rear frame of channel letters (0.12.75, DESIGN-text-lightbox.md 6.2): letters are separate islands and something has to hold them. From the letters' loops: the islands (a 口 is one with a hole, a
    /// 小 is three; strokes of one glyph that overlap or touch are one), two rails for each line at the heights that cross the most islands, studs in every island (two where it is wide enough, inside the filled
    /// area, never in a hole), bridges from the studs of an island no rail crosses, rails with an overhang and in pieces of at most MaxRailPiece, ties between the lines. Estimates, not a standard's. Pure.
    /// </summary>
    public static class LetterFrame
    {
        private const float Touch = 1e-4f;

        public static FramePlan Plan(TextLayoutResult layout, Func<int, GlyphMesh> meshOf, FrameStyle style)
        {
            var plan = new FramePlan();
            float metresPerEm = layout.MetresPerEm;
            for (int g = 0; g < layout.Glyphs.Count; g++)
            {
                PlacedGlyph placed = layout.Glyphs[g];
                GlyphMesh mesh = placed.Missing ? null : meshOf(placed.Codepoint);
                if (mesh == null)
                    continue;
                plan.Perimeter += mesh.Perimeter * metresPerEm;
                plan.Islands.AddRange(Islands(mesh.Placed(metresPerEm, placed.X, placed.Y), placed.Line, g));
            }

            if (plan.Islands.Count == 0)
                return plan;
            float inkMinX = float.MaxValue, inkMaxX = float.MinValue, inkMinY = float.MaxValue, inkMaxY = float.MinValue;
            foreach (FrameIsland island in plan.Islands)
            {
                plan.EffectiveArea += island.Area;
                inkMinX = Math.Min(inkMinX, island.MinX);
                inkMaxX = Math.Max(inkMaxX, island.MaxX);
                inkMinY = Math.Min(inkMinY, island.MinY);
                inkMaxY = Math.Max(inkMaxY, island.MaxY);
            }

            plan.InkWidth = inkMaxX - inkMinX;
            plan.InkHeight = inkMaxY - inkMinY;
            plan.FillFactor = plan.InkWidth > 0f && plan.InkHeight > 0f ? plan.EffectiveArea / (plan.InkWidth * plan.InkHeight) : 0f;

            var scan = new Scan();
            for (int line = 0; line < layout.Lines; line++)
                HoldLine(plan, layout, line, style, scan);
            AddRails(plan, style);
            plan.MassKg = EstimatedMass(plan, style);
            return plan;
        }

        /// <summary>
        /// The estimated mass (kg): the filled area x (face + LED modules per m2) + the loops' length x depth x the return's kg per m2 + the frame's length x the tube's kg per m (scaled by the square of its size
        /// against 4 cm) + the studs. An estimate from the usual materials (MassModel), not a standard's figure.
        /// </summary>
        public static float EstimatedMass(FramePlan plan, FrameStyle style)
        {
            MassModel m = style.Mass;
            float tube = style.RailSize > 0f ? (style.RailSize / 0.04f) * (style.RailSize / 0.04f) : 1f;
            return plan.EffectiveArea * (m.FaceKgPerM2 + m.LedKgPerM2) + plan.Perimeter * style.Depth * m.ReturnKgPerM2 + plan.FrameLength * m.RailKgPerM * tube + plan.Studs.Count * m.StudKg;
        }

        // ------------------------------------------------------------------ islands

        /// <summary>The islands of one glyph's loops (outer counter-clockwise, holes clockwise, in metres).</summary>
        internal static List<FrameIsland> Islands(List<List<Point2>> loops, int line, int glyph)
        {
            var pieces = new List<FrameIsland.Piece>();
            foreach (List<Point2> loop in loops)
            {
                float area = CutOutline.Area(loop);
                if (area > 0f)
                    pieces.Add(new FrameIsland.Piece { Outer = loop, OuterArea = area });
            }

            foreach (List<Point2> loop in loops)
            {
                if (CutOutline.Area(loop) >= 0f)
                    continue;
                float mx = (loop[0].X + loop[1].X) * 0.5f, my = (loop[0].Y + loop[1].Y) * 0.5f;
                FrameIsland.Piece owner = null;
                float smallest = float.MaxValue;
                foreach (FrameIsland.Piece piece in pieces)
                {
                    if (piece.OuterArea < smallest && TextGlyphs.Inside(piece.Outer, mx, my))
                    {
                        smallest = piece.OuterArea;
                        owner = piece;
                    }
                }

                owner?.Holes.Add(loop);
            }

            // Pieces that overlap or touch are one island.
            var group = new int[pieces.Count];
            for (int i = 0; i < group.Length; i++)
                group[i] = i;
            for (int i = 0; i < pieces.Count; i++)
            {
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    if (Find(group, i) != Find(group, j) && Meet(pieces[i], pieces[j]))
                        group[Find(group, j)] = Find(group, i);
                }
            }

            var byRoot = new FrameIsland[pieces.Count];
            var order = new List<FrameIsland>();
            for (int i = 0; i < pieces.Count; i++)
            {
                int root = Find(group, i);
                FrameIsland island = byRoot[root];
                if (island == null)
                {
                    byRoot[root] = island = new FrameIsland { Line = line, Glyph = glyph };
                    order.Add(island);
                }

                island.Pieces.Add(pieces[i]);
                island.Area += pieces[i].OuterArea;
                foreach (List<Point2> hole in pieces[i].Holes)
                    island.Area += CutOutline.Area(hole);
                foreach (Point2 p in pieces[i].Outer)
                {
                    island.MinX = Math.Min(island.MinX, p.X);
                    island.MaxX = Math.Max(island.MaxX, p.X);
                    island.MinY = Math.Min(island.MinY, p.Y);
                    island.MaxY = Math.Max(island.MaxY, p.Y);
                }
            }

            return order;
        }

        private static int Find(int[] group, int i)
        {
            while (group[i] != i)
                i = group[i] = group[group[i]];
            return i;
        }

        private static bool Meet(FrameIsland.Piece a, FrameIsland.Piece b)
        {
            if (!BoxesMeet(a.Outer, b.Outer))
                return false;
            // The outer loop and then the holes of each, in that order.
            for (int i = 0; i <= a.Holes.Count; i++)
            {
                List<Point2> la = i == 0 ? a.Outer : a.Holes[i - 1];
                for (int j = 0; j <= b.Holes.Count; j++)
                {
                    List<Point2> lb = j == 0 ? b.Outer : b.Holes[j - 1];
                    if (BoxesMeet(la, lb) && LoopsMeet(la, lb))
                        return true;
                }
            }

            return Filled(a, b.Outer[0]) || Filled(b, a.Outer[0]);
        }

        private static bool Filled(FrameIsland.Piece piece, Point2 p)
        {
            if (!TextGlyphs.Inside(piece.Outer, p.X, p.Y))
                return false;
            foreach (List<Point2> hole in piece.Holes)
            {
                if (TextGlyphs.Inside(hole, p.X, p.Y))
                    return false;
            }

            return true;
        }

        private static bool BoxesMeet(List<Point2> a, List<Point2> b)
        {
            Box(a, out float ax0, out float ay0, out float ax1, out float ay1);
            Box(b, out float bx0, out float by0, out float bx1, out float by1);
            return ax0 <= bx1 + Touch && bx0 <= ax1 + Touch && ay0 <= by1 + Touch && by0 <= ay1 + Touch;
        }

        private static void Box(List<Point2> loop, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue;
            x1 = y1 = float.MinValue;
            foreach (Point2 p in loop)
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                y0 = Math.Min(y0, p.Y);
                y1 = Math.Max(y1, p.Y);
            }
        }

        private static bool LoopsMeet(List<Point2> a, List<Point2> b)
        {
            // An edge of a that is clear of the box of b is clear of every edge of b: only the others are tried against all of b's.
            Box(b, out float bx0, out float by0, out float bx1, out float by1);
            for (int i = 0, n = a.Count; i < n; i++)
            {
                Point2 p = a[i], q = a[i + 1 == n ? 0 : i + 1];
                float x0 = Math.Min(p.X, q.X) - Touch, x1 = Math.Max(p.X, q.X) + Touch, y0 = Math.Min(p.Y, q.Y) - Touch, y1 = Math.Max(p.Y, q.Y) + Touch;
                if (bx1 < x0 || bx0 > x1 || by1 < y0 || by0 > y1)
                    continue;
                for (int j = 0, m = b.Count; j < m; j++)
                {
                    Point2 r = b[j], s = b[j + 1 == m ? 0 : j + 1];
                    if (Math.Max(r.X, s.X) < x0 || Math.Min(r.X, s.X) > x1 || Math.Max(r.Y, s.Y) < y0 || Math.Min(r.Y, s.Y) > y1)
                        continue;
                    if (SegmentsMeet(p, q, r, s))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Whether two segments cross or one end is within Touch of the other segment.</summary>
        private static bool SegmentsMeet(Point2 p, Point2 q, Point2 r, Point2 s)
        {
            float d1 = Cross(p, q, r), d2 = Cross(p, q, s), d3 = Cross(r, s, p), d4 = Cross(r, s, q);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
                return true;
            return DistanceToSegment(r, p, q) <= Touch || DistanceToSegment(s, p, q) <= Touch || DistanceToSegment(p, r, s) <= Touch || DistanceToSegment(q, r, s) <= Touch;
        }

        private static float Cross(Point2 a, Point2 b, Point2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static float DistanceToSegment(Point2 p, Point2 a, Point2 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            float t = l2 < 1e-18f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            float ex = a.X + dx * t - p.X, ey = a.Y + dy * t - p.Y;
            return (float)Math.Sqrt(ex * ex + ey * ey);
        }

        // ------------------------------------------------------------------ scanning

        /// <summary>The heights a line's rails are tried at, evenly spaced upward from the ink's lowest point; where a value lies among them is estimated from the spacing and then settled against the heights themselves.</summary>
        private readonly struct Grid
        {
            public readonly float[] Heights;
            private readonly float m_Origin, m_PerUnit;

            public Grid(float[] heights, float origin, float span)
            {
                Heights = heights;
                m_Origin = origin;
                m_PerUnit = span > 0f ? heights.Length / span : 0f;
            }

            /// <summary>The first index whose height is not below value (the number of heights when none is above it).</summary>
            public int LowerBound(float value)
            {
                int steps = Heights.Length;
                float guess = (value - m_Origin) * m_PerUnit - 0.5f;
                int k = guess > 0f ? (guess < steps ? (int)guess : steps) : 0;
                while (k > 0 && Heights[k - 1] >= value)
                    k--;
                while (k < steps && Heights[k] < value)
                    k++;
                return k;
            }
        }

        /// <summary>The scratch of one plan: the lists and arrays that every scan of every island reuses, so that a scan makes nothing. A plan runs on one thread and makes its own, so that plans may run on several.</summary>
        private sealed class Scan
        {
            public float[] Xs = new float[64], From = new float[32], To = new float[32];

            /// <summary>The sweep of an island: every crossing of every piece at every grid height (Swept), where each piece's crossings at each height begin (Offsets) and how far they are filled (Fill).</summary>
            public float[] Swept = new float[256];
            public int[] Offsets = new int[64], Fill = new int[64];
            public readonly List<int> Members = new List<int>();
            public readonly List<(float X0, float X1)> AtLow = new List<(float, float)>(), AtHigh = new List<(float, float)>(), Found = new List<(float, float)>(), None = new List<(float, float)>();
            public readonly List<Point2> Studs = new List<Point2>(2);
        }

        /// <summary>
        /// The filled spans of an island at a height, left to right, in into (emptied first): each piece's loops cut by the horizontal line (even-odd within the piece), the pieces' spans joined where they
        /// meet. Makes nothing: the crossings and spans are in the scan's arrays.
        /// </summary>
        private static void SpansInto(FrameIsland island, float y, Scan scan, List<(float X0, float X1)> into)
        {
            int n = 0;
            foreach (FrameIsland.Piece piece in island.Pieces)
            {
                int count = 0;
                Crossings(piece.Outer, y, scan, ref count);
                foreach (List<Point2> hole in piece.Holes)
                    Crossings(hole, y, scan, ref count);
                Sort(scan.Xs, 0, count);
                n = Pair(scan.Xs, 0, count, scan, n);
            }

            Merge(scan, n, into);
        }

        /// <summary>The sorted crossings xs[start .. start + count) taken two by two as spans, after the n that scan holds; the new n.</summary>
        private static int Pair(float[] xs, int start, int count, Scan scan, int n)
        {
            for (int i = start; i + 1 < start + count; i += 2)
            {
                if (n == scan.From.Length)
                {
                    Array.Resize(ref scan.From, n * 2);
                    Array.Resize(ref scan.To, n * 2);
                }

                scan.From[n] = xs[i];
                scan.To[n++] = xs[i + 1];
            }

            return n;
        }

        /// <summary>The n spans of scan, joined where they meet, left to right, in into (emptied first; none when into is null); the width of the widest.</summary>
        private static float Merge(Scan scan, int n, List<(float X0, float X1)> into)
        {
            into?.Clear();
            // By their starts (an island has a few: insertion sort); two with one start join whichever comes first, so the order of equal starts does not matter.
            for (int i = 1; i < n; i++)
            {
                float from = scan.From[i], to = scan.To[i];
                int j = i - 1;
                while (j >= 0 && scan.From[j] > from)
                {
                    scan.From[j + 1] = scan.From[j];
                    scan.To[j + 1] = scan.To[j];
                    j--;
                }

                scan.From[j + 1] = from;
                scan.To[j + 1] = to;
            }

            float widest = 0f, x0 = 0f, x1 = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && scan.From[i] <= x1 + 1e-6f)
                {
                    x1 = Math.Max(x1, scan.To[i]);
                    continue;
                }

                if (i > 0)
                {
                    into?.Add((x0, x1));
                    widest = Math.Max(widest, x1 - x0);
                }

                x0 = scan.From[i];
                x1 = scan.To[i];
            }

            if (n > 0)
            {
                into?.Add((x0, x1));
                widest = Math.Max(widest, x1 - x0);
            }

            return widest;
        }

        /// <summary>
        /// Whether an island has a span wide enough for a stud at each height of the grid, into cut[k * count + column]: the answer of SpansInto at each height, from one pass over the edges instead of one pass for
        /// each of the heights (48 of them): an edge crosses the heights from its lower end up to, not including, its upper, which is a run of the grid (Grid.LowerBound). The crossings are the same
        /// numbers, and then are sorted, paired and joined as SpansInto does.
        /// </summary>
        private static void Sweep(FrameIsland island, Grid grid, float minSpan, Scan scan, bool[] cut, int column, int count)
        {
            int steps = grid.Heights.Length, stride = steps + 1, pieces = island.Pieces.Count;
            Grow(ref scan.Offsets, pieces * stride);
            Grow(ref scan.Fill, pieces * stride);
            int[] offsets = scan.Offsets;
            Array.Clear(offsets, 0, pieces * stride);
            // The heights some edge crosses are from first to last (not including): the island has no span at the others.
            int first = steps, last = 0;
            for (int p = 0; p < pieces; p++)
            {
                CountCrossings(island.Pieces[p].Outer, grid, offsets, p * stride, ref first, ref last);
                foreach (List<Point2> hole in island.Pieces[p].Holes)
                    CountCrossings(hole, grid, offsets, p * stride, ref first, ref last);
            }

            // The counts become where each piece's crossings at each height begin; the last of a piece's entries is where its crossings end.
            int total = 0;
            for (int p = 0; p < pieces; p++)
            {
                for (int k = 0; k < steps; k++)
                {
                    int crossings = offsets[p * stride + k];
                    offsets[p * stride + k] = total;
                    total += crossings;
                }

                offsets[p * stride + steps] = total;
            }

            Grow(ref scan.Swept, total);
            Array.Copy(offsets, scan.Fill, pieces * stride);
            for (int p = 0; p < pieces; p++)
            {
                PlaceCrossings(island.Pieces[p].Outer, grid, scan.Swept, scan.Fill, p * stride);
                foreach (List<Point2> hole in island.Pieces[p].Holes)
                    PlaceCrossings(hole, grid, scan.Swept, scan.Fill, p * stride);
            }

            for (int k = first; k < last; k++)
            {
                int n = 0;
                for (int p = 0; p < pieces; p++)
                {
                    int from = offsets[p * stride + k], to = offsets[p * stride + k + 1];
                    Sort(scan.Swept, from, to - from);
                    n = Pair(scan.Swept, from, to - from, scan, n);
                }

                cut[k * count + column] = (n > 0 ? Merge(scan, n, null) : 0f) >= minSpan;
            }
        }

        /// <summary>Ascending, in place: by insertion for the few crossings of one height (a stroke or two: 2 to 8), where Array.Sort costs more in its call than in the sorting; by Array.Sort beyond 24.</summary>
        private static void Sort(float[] values, int start, int count)
        {
            if (count > 24)
            {
                Array.Sort(values, start, count);
                return;
            }

            for (int i = start + 1; i < start + count; i++)
            {
                float v = values[i];
                int j = i - 1;
                while (j >= start && values[j] > v)
                {
                    values[j + 1] = values[j];
                    j--;
                }

                values[j + 1] = v;
            }
        }

        private static void Grow<T>(ref T[] array, int size)
        {
            if (array.Length < size)
                Array.Resize(ref array, Math.Max(size, array.Length * 2));
        }

        /// <summary>
        /// Adds 1 to counts[at + k] for each height k that an edge of the loop crosses (Crossings' test, (p.Y <= y and q.Y > y) or (q.Y <= y and p.Y > y), is y in [the lower end, the upper end)), and
        /// widens first and last to the heights crossed.
        /// </summary>
        private static void CountCrossings(List<Point2> loop, Grid grid, int[] counts, int at, ref int first, ref int last)
        {
            for (int i = 0, n = loop.Count; i < n; i++)
            {
                float py = loop[i].Y, qy = loop[i + 1 == n ? 0 : i + 1].Y;
                int from = grid.LowerBound(Math.Min(py, qy)), to = grid.LowerBound(Math.Max(py, qy));
                if (from >= to)
                    continue;
                first = Math.Min(first, from);
                last = Math.Max(last, to);
                for (int k = from; k < to; k++)
                    counts[at + k]++;
            }
        }

        private static void PlaceCrossings(List<Point2> loop, Grid grid, float[] xs, int[] fill, int at)
        {
            for (int i = 0, n = loop.Count; i < n; i++)
            {
                Point2 p = loop[i], q = loop[i + 1 == n ? 0 : i + 1];
                int to = grid.LowerBound(Math.Max(p.Y, q.Y));
                for (int k = grid.LowerBound(Math.Min(p.Y, q.Y)); k < to; k++)
                    xs[fill[at + k]++] = p.X + (grid.Heights[k] - p.Y) / (q.Y - p.Y) * (q.X - p.X);
            }
        }

        private static void Crossings(List<Point2> loop, float y, Scan scan, ref int count)
        {
            int n = loop.Count;
            for (int i = 0; i < n; i++)
            {
                Point2 p = loop[i], q = loop[i + 1 == n ? 0 : i + 1];
                if ((p.Y <= y && q.Y > y) || (q.Y <= y && p.Y > y))
                {
                    if (count == scan.Xs.Length)
                        Array.Resize(ref scan.Xs, count * 2);
                    scan.Xs[count++] = p.X + (y - p.Y) / (q.Y - p.Y) * (q.X - p.X);
                }
            }
        }

        // ------------------------------------------------------------------ rails and studs

        private static void HoldLine(FramePlan plan, TextLayoutResult layout, int line, FrameStyle style, Scan scan)
        {
            List<int> members = scan.Members;
            members.Clear();
            float inkMin = float.MaxValue, inkMax = float.MinValue;
            for (int i = 0; i < plan.Islands.Count; i++)
            {
                if (plan.Islands[i].Line != line)
                    continue;
                members.Add(i);
                inkMin = Math.Min(inkMin, plan.Islands[i].MinY);
                inkMax = Math.Max(inkMax, plan.Islands[i].MaxY);
            }

            if (members.Count == 0)
                return;
            int count = members.Count;
            float inkHeight = inkMax - inkMin;
            float minSpan = 4f * style.StudRadius;
            int steps = Math.Max(8, style.RailSteps);
            var heights = new float[steps];
            // cut[k * count + m]: the m-th island of the line has a span at height k wide enough for a stud.
            var cut = new bool[steps * count];
            for (int k = 0; k < steps; k++)
                heights[k] = inkMin + (k + 0.5f) / steps * inkHeight;
            var grid = new Grid(heights, inkMin, inkHeight);
            for (int m = 0; m < count; m++)
                Sweep(plan.Islands[members[m]], grid, minSpan, scan, cut, m, count);

            float gap = Math.Min(style.MinRailGap * layout.LetterHeight, 0.8f * inkHeight);
            int bestLow = 0, bestHigh = steps - 1, bestUnion = -1, bestBoth = -1;
            float bestSymmetry = float.MaxValue;
            for (int i = 0; i < steps; i++)
            {
                for (int j = i + 1; j < steps; j++)
                {
                    if (heights[j] - heights[i] < gap)
                        continue;
                    int union = 0, both = 0;
                    for (int m = 0; m < count; m++)
                    {
                        bool atI = cut[i * count + m], atJ = cut[j * count + m];
                        union += atI || atJ ? 1 : 0;
                        both += atI && atJ ? 1 : 0;
                    }

                    // Among rails that cross as many islands, those near a quarter and three quarters of the ink: the usual places for a frame.
                    float symmetry = Math.Abs(heights[i] - (inkMin + 0.25f * inkHeight)) + Math.Abs(heights[j] - (inkMin + 0.75f * inkHeight));
                    if (union > bestUnion || (union == bestUnion && (both > bestBoth || (both == bestBoth && symmetry < bestSymmetry))))
                    {
                        bestLow = i;
                        bestHigh = j;
                        bestUnion = union;
                        bestBoth = both;
                        bestSymmetry = symmetry;
                    }
                }
            }

            float low = heights[bestLow], high = heights[bestHigh];
            plan.RailHeights.Add((line, low, high));
            foreach (int index in members)
                Stud(plan, index, low, high, style, scan);
        }

        /// <summary>
        /// The studs of an island and, for one no rail crosses, the bridges. On the rail where the island reaches farthest (the lower on a tie): a stud at each end of its filled area, set in by 2.5 stud
        /// radii (or to the middle of a narrower span); where the two coincide (a narrow island) one stud, and a second on the other rail if that crosses the island. An island no rail crosses is scanned
        /// at its own heights, and a rod goes from each stud straight to the nearest rail.
        /// </summary>
        private static void Stud(FramePlan plan, int index, float low, float high, FrameStyle style, Scan scan)
        {
            FrameIsland island = plan.Islands[index];
            float minSpan = 4f * style.StudRadius, minGap = 4f * style.StudRadius, inset = 2.5f * style.StudRadius;
            List<(float X0, float X1)> atLow = scan.AtLow, atHigh = scan.AtHigh;
            SpansInto(island, low, scan, atLow);
            Wide(atLow, minSpan);
            SpansInto(island, high, scan, atHigh);
            Wide(atHigh, minSpan);
            bool railed = atLow.Count > 0 || atHigh.Count > 0;
            float y = low;
            List<(float X0, float X1)> spans = atLow;
            List<(float X0, float X1)> other = atHigh;
            float otherY = high;
            if (railed)
            {
                float reachLow = atLow.Count > 0 ? atLow[atLow.Count - 1].X1 - atLow[0].X0 : -1f, reachHigh = atHigh.Count > 0 ? atHigh[atHigh.Count - 1].X1 - atHigh[0].X0 : -1f;
                if (reachHigh > reachLow)
                {
                    y = high;
                    spans = atHigh;
                    other = atLow;
                    otherY = low;
                }
            }
            else
            {
                // No rail crosses it (a dot, a mark): its own heights, middle first.
                spans = null;
                foreach (float t in OwnHeights)
                {
                    y = island.MinY + t * (island.MaxY - island.MinY);
                    SpansInto(island, y, scan, scan.Found);
                    Wide(scan.Found, minSpan);
                    if (scan.Found.Count > 0)
                    {
                        spans = scan.Found;
                        break;
                    }
                }

                other = scan.None;
            }

            if (spans == null)
            {
                plan.FloatingIslands++;
                return;
            }

            float xl = spans[0].X0 + Math.Min((spans[0].X1 - spans[0].X0) * 0.5f, inset);
            var last = spans[spans.Count - 1];
            float xr = last.X1 - Math.Min((last.X1 - last.X0) * 0.5f, inset);
            List<Point2> studs = scan.Studs;
            studs.Clear();
            if (xr - xl >= minGap)
            {
                studs.Add(new Point2(xl, y));
                studs.Add(new Point2(xr, y));
            }
            else
            {
                studs.Add(new Point2((xl + xr) * 0.5f, y));
                if (other.Count > 0)
                {
                    (float X0, float X1) widest = other[0];
                    foreach ((float x0, float x1) in other)
                    {
                        if (x1 - x0 > widest.X1 - widest.X0)
                            widest = (x0, x1);
                    }

                    studs.Add(new Point2((widest.X0 + widest.X1) * 0.5f, otherY));
                }
                else
                {
                    plan.SingleStudIslands++;
                }
            }

            foreach (Point2 at in studs)
                plan.Studs.Add(new FrameStud { At = at, Island = index, Line = island.Line });
            if (!railed)
            {
                plan.BridgedIslands++;
                float rail = Math.Abs(y - low) <= Math.Abs(y - high) ? low : high;
                foreach (Point2 at in studs)
                    plan.Bridges.Add(new FrameSegment { A = at, B = new Point2(at.X, rail), Line = island.Line });
            }
        }

        /// <summary>The fractions of an island's height at which a stud is tried when no rail crosses it, the middle first.</summary>
        private static readonly float[] OwnHeights = { 0.5f, 0.4f, 0.6f, 0.3f, 0.7f, 0.2f, 0.8f, 0.1f, 0.9f };

        /// <summary>The spans that are at least minWidth wide, in place.</summary>
        private static void Wide(List<(float X0, float X1)> spans, float minWidth)
        {
            int kept = 0;
            for (int i = 0; i < spans.Count; i++)
            {
                if (!(spans[i].X1 - spans[i].X0 < minWidth))
                    spans[kept++] = spans[i];
            }

            spans.RemoveRange(kept, spans.Count - kept);
        }

        /// <summary>
        /// The rails: both of every line across one common length (the studs' extent of the whole text, an overhang beyond each end), in pieces of at most MaxRailPiece (spliced at their ends); for two
        /// lines or more, two ties at the ends from the lowest rail to the highest.
        /// </summary>
        private static void AddRails(FramePlan plan, FrameStyle style)
        {
            if (plan.Studs.Count == 0 || plan.RailHeights.Count == 0)
                return;
            float x0 = float.MaxValue, x1 = float.MinValue;
            foreach (FrameStud stud in plan.Studs)
            {
                x0 = Math.Min(x0, stud.At.X);
                x1 = Math.Max(x1, stud.At.X);
            }

            x0 -= style.RailOverhang;
            x1 += style.RailOverhang;
            float lowest = float.MaxValue, highest = float.MinValue;
            foreach ((int line, float low, float high) in plan.RailHeights)
            {
                Pieces(plan.Rails, new Point2(x0, low), new Point2(x1, low), line, style.MaxRailPiece);
                Pieces(plan.Rails, new Point2(x0, high), new Point2(x1, high), line, style.MaxRailPiece);
                lowest = Math.Min(lowest, low);
                highest = Math.Max(highest, high);
            }

            if (plan.RailHeights.Count >= 2)
            {
                Pieces(plan.Ties, new Point2(x0, lowest), new Point2(x0, highest), -1, style.MaxRailPiece);
                Pieces(plan.Ties, new Point2(x1, lowest), new Point2(x1, highest), -1, style.MaxRailPiece);
            }

            foreach (FrameSegment s in plan.Rails)
                plan.FrameLength += s.Length;
            foreach (FrameSegment s in plan.Ties)
                plan.FrameLength += s.Length;
            foreach (FrameSegment s in plan.Bridges)
            {
                float length = s.Length;
                plan.FrameLength += length;
                plan.LongestBridge = Math.Max(plan.LongestBridge, length);
                if (length > style.LongRod)
                    plan.LongBridges++;
            }
        }

        private static void Pieces(List<FrameSegment> into, Point2 a, Point2 b, int line, float longest)
        {
            float length = (float)Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y));
            int n = Math.Max(1, (int)Math.Ceiling(length / Math.Max(0.1f, longest) - 1e-4f));
            for (int i = 0; i < n; i++)
            {
                float t0 = (float)i / n, t1 = (float)(i + 1) / n;
                into.Add(new FrameSegment { A = new Point2(a.X + (b.X - a.X) * t0, a.Y + (b.Y - a.Y) * t0), B = new Point2(a.X + (b.X - a.X) * t1, a.Y + (b.Y - a.Y) * t1), Line = line });
            }
        }
    }
}
