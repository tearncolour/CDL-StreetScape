using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The rear frame planner as it was at 0.12.75 (the commit of the first text pipeline), kept unchanged as the reference the optimised LetterFrame is compared with, exactly, over many texts
    /// (PlannerPerfTests). Not used by the mod. If LetterFrame's output is changed on purpose, this copy is not: the test then says what differs.
    /// </summary>
    internal static class LetterFrameReference
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

            for (int line = 0; line < layout.Lines; line++)
                HoldLine(plan, layout, line, style);
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
                if (CutOutline.Area(loop) > 0f)
                    pieces.Add(new FrameIsland.Piece { Outer = loop });
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
                    float area = CutOutline.Area(piece.Outer);
                    if (area < smallest && TextGlyphs.Inside(piece.Outer, mx, my))
                    {
                        smallest = area;
                        owner = piece;
                    }
                }

                owner?.Holes.Add(loop);
            }

            // Pieces that overlap or touch are one island.
            var group = new int[pieces.Count];
            for (int i = 0; i < group.Length; i++)
                group[i] = i;
            int Find(int i)
            {
                while (group[i] != i)
                    i = group[i] = group[group[i]];
                return i;
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    if (Find(i) != Find(j) && Meet(pieces[i], pieces[j]))
                        group[Find(j)] = Find(i);
                }
            }

            var islands = new Dictionary<int, FrameIsland>();
            var order = new List<FrameIsland>();
            for (int i = 0; i < pieces.Count; i++)
            {
                int root = Find(i);
                if (!islands.TryGetValue(root, out FrameIsland island))
                {
                    islands[root] = island = new FrameIsland { Line = line, Glyph = glyph };
                    order.Add(island);
                }

                island.Pieces.Add(pieces[i]);
                island.Area += CutOutline.Area(pieces[i].Outer);
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

        private static bool Meet(FrameIsland.Piece a, FrameIsland.Piece b)
        {
            if (!BoxesMeet(a.Outer, b.Outer))
                return false;
            var loopsA = new List<List<Point2>> { a.Outer };
            loopsA.AddRange(a.Holes);
            var loopsB = new List<List<Point2>> { b.Outer };
            loopsB.AddRange(b.Holes);
            foreach (List<Point2> la in loopsA)
            {
                foreach (List<Point2> lb in loopsB)
                {
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
            for (int i = 0; i < a.Count; i++)
            {
                Point2 p = a[i], q = a[(i + 1) % a.Count];
                float x0 = Math.Min(p.X, q.X) - Touch, x1 = Math.Max(p.X, q.X) + Touch, y0 = Math.Min(p.Y, q.Y) - Touch, y1 = Math.Max(p.Y, q.Y) + Touch;
                for (int j = 0; j < b.Count; j++)
                {
                    Point2 r = b[j], s = b[(j + 1) % b.Count];
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

        /// <summary>The filled spans of an island at a height, left to right: each piece's loops cut by the horizontal line (even-odd within the piece), the pieces' spans joined where they meet.</summary>
        internal static List<(float X0, float X1)> Spans(FrameIsland island, float y)
        {
            var all = new List<(float X0, float X1)>();
            var xs = new List<float>();
            foreach (FrameIsland.Piece piece in island.Pieces)
            {
                xs.Clear();
                Crossings(piece.Outer, y, xs);
                foreach (List<Point2> hole in piece.Holes)
                    Crossings(hole, y, xs);
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                    all.Add((xs[i], xs[i + 1]));
            }

            all.Sort((a, b) => a.X0.CompareTo(b.X0));
            var merged = new List<(float X0, float X1)>();
            foreach ((float x0, float x1) in all)
            {
                if (merged.Count > 0 && x0 <= merged[merged.Count - 1].X1 + 1e-6f)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].X0, Math.Max(merged[merged.Count - 1].X1, x1));
                else
                    merged.Add((x0, x1));
            }

            return merged;
        }

        private static void Crossings(List<Point2> loop, float y, List<float> xs)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                Point2 p = loop[i], q = loop[(i + 1) % loop.Count];
                if ((p.Y <= y && q.Y > y) || (q.Y <= y && p.Y > y))
                    xs.Add(p.X + (y - p.Y) / (q.Y - p.Y) * (q.X - p.X));
            }
        }

        // ------------------------------------------------------------------ rails and studs

        private static void HoldLine(FramePlan plan, TextLayoutResult layout, int line, FrameStyle style)
        {
            var members = new List<int>();
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
            float inkHeight = inkMax - inkMin;
            float minSpan = 4f * style.StudRadius;
            int steps = Math.Max(8, style.RailSteps);
            var heights = new float[steps];
            var cut = new bool[steps][];
            for (int k = 0; k < steps; k++)
            {
                heights[k] = inkMin + (k + 0.5f) / steps * inkHeight;
                cut[k] = new bool[members.Count];
                for (int m = 0; m < members.Count; m++)
                    cut[k][m] = Widest(Spans(plan.Islands[members[m]], heights[k])) >= minSpan;
            }

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
                    for (int m = 0; m < members.Count; m++)
                    {
                        union += cut[i][m] || cut[j][m] ? 1 : 0;
                        both += cut[i][m] && cut[j][m] ? 1 : 0;
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
                Stud(plan, index, low, high, style);
        }

        private static float Widest(List<(float X0, float X1)> spans)
        {
            float widest = 0f;
            foreach ((float x0, float x1) in spans)
                widest = Math.Max(widest, x1 - x0);
            return widest;
        }

        /// <summary>
        /// The studs of an island and, for one no rail crosses, the bridges. On the rail where the island reaches farthest (the lower on a tie): a stud at each end of its filled area, set in by 2.5 stud
        /// radii (or to the middle of a narrower span); where the two coincide (a narrow island) one stud, and a second on the other rail if that crosses the island. An island no rail crosses is scanned
        /// at its own heights, and a rod goes from each stud straight to the nearest rail.
        /// </summary>
        private static void Stud(FramePlan plan, int index, float low, float high, FrameStyle style)
        {
            FrameIsland island = plan.Islands[index];
            float minSpan = 4f * style.StudRadius, minGap = 4f * style.StudRadius, inset = 2.5f * style.StudRadius;
            List<(float X0, float X1)> atLow = Wide(Spans(island, low), minSpan), atHigh = Wide(Spans(island, high), minSpan);
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
                foreach (float t in new[] { 0.5f, 0.4f, 0.6f, 0.3f, 0.7f, 0.2f, 0.8f, 0.1f, 0.9f })
                {
                    y = island.MinY + t * (island.MaxY - island.MinY);
                    List<(float X0, float X1)> found = Wide(Spans(island, y), minSpan);
                    if (found.Count > 0)
                    {
                        spans = found;
                        break;
                    }
                }

                other = new List<(float X0, float X1)>();
            }

            if (spans == null)
            {
                plan.FloatingIslands++;
                return;
            }

            float xl = spans[0].X0 + Math.Min((spans[0].X1 - spans[0].X0) * 0.5f, inset);
            var last = spans[spans.Count - 1];
            float xr = last.X1 - Math.Min((last.X1 - last.X0) * 0.5f, inset);
            var studs = new List<Point2>();
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

        private static List<(float X0, float X1)> Wide(List<(float X0, float X1)> spans, float minWidth)
        {
            spans.RemoveAll(s => s.X1 - s.X0 < minWidth);
            return spans;
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
                plan.FrameLength += s.Length;
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
