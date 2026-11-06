using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// How a text was set on an arc (ArcTextLayout.Bend, 0.12.76): the straight layout it came from (the rear frame is planned on that and carried over), the layout with its letters turned along the
    /// arc, and the circle: its direction (an arch has the letters outside the circle, a smile inside), the sweep asked for and the one the widest line got, the radius of that line, and the spacing
    /// the letters keep on the side of the circle they crowd. Made once with the layout, not changed. This is this mod's design, not a standard's.
    /// </summary>
    public sealed class ArcInfo
    {
        /// <summary>The least and the most a text is bent (degrees).</summary>
        public const float MinSweep = 10f, MaxSweep = 300f;

        /// <summary>
        /// The circle through the middle of the em box of the tightest line is at least this many letter heights in radius: there the side of the em box nearest the centre is 0.8 of the way in, so that
        /// letters of a font that fill their advance (a Chinese character) do not run into each other. A text that asks for more is bent less (Limited), and says so.
        /// </summary>
        public const float MinRadiusInHeights = 2.5f;

        public readonly TextLayoutResult Straight;

        /// <summary>The bent layout (set once, by Bend).</summary>
        public TextLayoutResult Bent;

        /// <summary>1 for an arch (the letters stand on the outside of the circle, the ends of the text drop), -1 for a smile (the letters hang inside it, the ends rise).</summary>
        public readonly int Direction;

        /// <summary>The sweep asked for and the one the widest line has (radians), the share of the spacing the em box's nearest side keeps (1 for no bend), and the radius (m) of the circle through the em box's middle of the widest line.</summary>
        public readonly float Sweep, EffectiveSweep, Squeeze, Radius;

        private readonly float m_MiddleY, m_CentreX, m_CentreY;

        internal ArcInfo(TextLayoutResult straight, int direction, float sweep, float effectiveSweep, float squeeze, float radius, float middleY, float centreX, float centreY)
        {
            Straight = straight;
            Direction = direction;
            Sweep = sweep;
            EffectiveSweep = effectiveSweep;
            Squeeze = squeeze;
            Radius = radius;
            m_MiddleY = middleY;
            m_CentreX = centreX;
            m_CentreY = centreY;
        }

        /// <summary>The text is bent less than it was asked to be.</summary>
        public bool Limited => EffectiveSweep < Sweep * (1f - 1e-4f);

        /// <summary>The radius of the circle that the middle of the em box of a line at height y (m, in the straight layout) is on.</summary>
        public float RadiusAt(float y) => Radius + Direction * (y - m_MiddleY);

        /// <summary>
        /// A point of the straight layout bent: how far along the line it is becomes the angle on the circle of its height, so that a rail along a line is an arc of its own circle. The circle's centre is
        /// where Bend put it (the text block's middle is the origin).
        /// </summary>
        public Point2 Bend(float x, float y)
        {
            float rho = RadiusAt(y), theta = x / rho;
            return new Point2(m_CentreX + rho * (float)Math.Sin(theta), m_CentreY + Direction * rho * (float)Math.Cos(theta));
        }

        /// <summary>A point of the straight layout carried with a glyph as a rigid body: where it is in the glyph's frame, put in the glyph's place and turned as the glyph is.</summary>
        public Point2 Carry(int glyph, Point2 p)
        {
            PlacedGlyph source = Straight.Glyphs[glyph], to = Bent.Glyphs[glyph];
            return ArcTextLayout.Turned(to.Angle, p.X - source.X, p.Y - source.Y, to.X, to.Y);
        }
    }

    /// <summary>
    /// A text on an arc (0.12.76, the owner: "支持部分艺术字"; this mod's design): the straight layout (TextLayout) with each character turned rigidly to stand upright on a circle. A character's
    /// cell (its advance by the em box) has its middle on the circle of its line, at the angle that its distance from the text's middle is along that circle, so the letters keep their spacing where the
    /// em box is middle high and are wider apart on the outer side of the circle, closer on the inner. Lines are concentric, one letter height apart as they were; the widest line is the one that has
    /// the sweep asked for. The block is the box of all the cells, and the text's middle is put at its middle. A bend that would crowd the inner side more than ArcInfo.MinRadiusInHeights allows is
    /// bent less. Pure.
    /// </summary>
    public static class ArcTextLayout
    {
        /// <summary>The layout of a text in a dressing: straight, or bent on the arc the dressing asks for.</summary>
        public static TextLayoutResult Layout(string text, TextStyle style, TextFace face, TextArt art)
        {
            TextLayoutResult straight = TextLayout.Layout(text, style, face);
            return art == null || art.Sweep == 0f ? straight : Bend(straight, Math.Abs(art.Sweep), art.Sweep > 0f ? 1 : -1, art.Slant);
        }

        /// <summary>A point (x, y) in a frame turned by angle (radians, counter-clockwise), then moved by (dx, dy).</summary>
        internal static Point2 Turned(float angle, float x, float y, float dx, float dy)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            return new Point2(dx + c * x - s * y, dy + s * x + c * y);
        }

        /// <summary>The height of the middle of a line's em box in the straight layout (m): the lines are a pitch apart, the block's middle is 0.</summary>
        private static float MiddleOf(TextLayoutResult straight, int line, float pitch) => straight.Height * 0.5f - line * pitch - straight.LetterHeight * 0.5f;

        /// <summary>
        /// The layout bent on an arc of sweepDegrees (limited to ArcInfo.MinSweep to MaxSweep, and by the crowding), as an arch (direction above 0) or a smile; the straight layout when there is no
        /// letter or no direction. The result's Arc holds the straight layout, for the frame, and the circle. A letter that leans (slant: the tangent of its angle) leans out of its cell by half the lean of
        /// the cell's height at the top and the bottom, and the box has room for that.
        /// </summary>
        public static TextLayoutResult Bend(TextLayoutResult straight, float sweepDegrees, int direction, float slant = 0f)
        {
            if (straight == null || straight.Glyphs.Count == 0 || direction == 0 || straight.Arc != null)
                return straight;
            int d = direction > 0 ? 1 : -1;
            float degrees = float.IsNaN(sweepDegrees) ? ArcInfo.MinSweep : Math.Max(ArcInfo.MinSweep, Math.Min(ArcInfo.MaxSweep, sweepDegrees));
            float asked = degrees * (float)(Math.PI / 180.0);
            float height = straight.LetterHeight, pitch = straight.Lines > 1 ? (straight.Height - height) / (straight.Lines - 1) : 0f;
            int widest = 0;
            for (int l = 1; l < straight.Lines; l++)
            {
                if (straight.LineWidths[l] > straight.LineWidths[widest])
                    widest = l;
            }

            float widestY = MiddleOf(straight, widest, pitch), widestWidth = straight.LineWidths[widest];
            // The line nearest the centre has the smallest radius: it sets how far the circle may close.
            var inked = new bool[straight.Lines];
            foreach (PlacedGlyph glyph in straight.Glyphs)
                inked[glyph.Line] = true;
            float nearest = 0f;
            for (int l = 0; l < straight.Lines; l++)
            {
                if (inked[l])
                    nearest = Math.Min(nearest, d * (MiddleOf(straight, l, pitch) - widestY));
            }

            float radius = Math.Max(widestWidth / asked, ArcInfo.MinRadiusInHeights * height - nearest);
            float effective = widestWidth / radius;
            var glyphs = new List<PlacedGlyph>(straight.Glyphs.Count);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, squeeze = 1f;
            float bottom = straight.EmBottomMetres, top = straight.EmTopMetres;
            foreach (PlacedGlyph source in straight.Glyphs)
            {
                float middleY = MiddleOf(straight, source.Line, pitch), rho = radius + d * (middleY - widestY);
                float theta = (source.X + source.Advance * 0.5f) / rho, angle = -d * theta;
                // The middle of the cell is on the circle; the origin is where the straight layout has it from that middle, turned with the cell.
                Point2 origin = Turned(angle, -source.Advance * 0.5f, source.Y - middleY, rho * (float)Math.Sin(theta), d * rho * (float)Math.Cos(theta));
                glyphs.Add(new PlacedGlyph { Codepoint = source.Codepoint, Line = source.Line, X = origin.X, Y = origin.Y, Missing = source.Missing, Advance = source.Advance, Angle = angle });
                squeeze = Math.Min(squeeze, (rho - height * 0.5f) / rho);
                for (int corner = 0; corner < 4; corner++)
                {
                    float lean = ((corner & 2) != 0 ? 0.5f : -0.5f) * slant * height;
                    Point2 at = Turned(angle, ((corner & 1) != 0 ? source.Advance : 0f) + lean, (corner & 2) != 0 ? top : bottom, origin.X, origin.Y);
                    minX = Math.Min(minX, at.X);
                    maxX = Math.Max(maxX, at.X);
                    minY = Math.Min(minY, at.Y);
                    maxY = Math.Max(maxY, at.Y);
                }
            }

            float midX = (minX + maxX) * 0.5f, midY = (minY + maxY) * 0.5f;
            var bent = new TextLayoutResult
            {
                Scale = straight.Scale, MetresPerEm = straight.MetresPerEm, LetterHeight = straight.LetterHeight, EmTopMetres = top, EmBottomMetres = bottom,
                Width = maxX - minX, Height = maxY - minY, Truncated = straight.Truncated
            };
            foreach (PlacedGlyph glyph in glyphs)
            {
                PlacedGlyph moved = glyph;
                moved.X -= midX;
                moved.Y -= midY;
                bent.Glyphs.Add(moved);
            }

            bent.LineWidths.AddRange(straight.LineWidths);
            bent.Missing.AddRange(straight.Missing);
            bent.Arc = new ArcInfo(straight, d, asked, effective, squeeze, radius, widestY, -midX, -midY) { Bent = bent };
            return bent;
        }

        // ------------------------------------------------------------------ the rear frame

        /// <summary>The angle between two points of a rail that is cut (radians): a rail is a polyline of the arc, a few degrees to a piece.</summary>
        private const float RailStep = 4f * (float)(Math.PI / 180.0);

        /// <summary>
        /// The rear frame of a text on an arc: the one planned on the straight layout, carried over. The islands and the studs go with their glyph as a rigid body (a stud is where it was in its
        /// letter); the rails are arcs of the circle of their height, through every stud they hold (a stud of a letter that is turned is a little off the circle: the rail runs through it, so that it
        /// holds), cut at most a few degrees to a piece; the ties between lines and the rods from a stud to a rail join the carried ends. The figures (length, mass, the long rods) are worked out again.
        /// </summary>
        public static FramePlan BendFrame(FramePlan frame, ArcInfo arc, FrameStyle style)
        {
            var bent = new FramePlan
            {
                BridgedIslands = frame.BridgedIslands, SingleStudIslands = frame.SingleStudIslands, FloatingIslands = frame.FloatingIslands, EffectiveArea = frame.EffectiveArea, Perimeter = frame.Perimeter
            };
            bent.RailHeights.AddRange(frame.RailHeights);
            foreach (FrameIsland island in frame.Islands)
                bent.Islands.Add(Carried(island, arc));
            foreach (FrameStud stud in frame.Studs)
                bent.Studs.Add(new FrameStud { At = arc.Carry(frame.Islands[stud.Island].Glyph, stud.At), Island = stud.Island, Line = stud.Line });
            foreach (FrameSegment rail in frame.Rails)
                AddRail(bent.Rails, rail, frame, bent, arc);
            foreach (FrameSegment tie in frame.Ties)
                bent.Ties.Add(new FrameSegment { A = arc.Bend(tie.A.X, tie.A.Y), B = arc.Bend(tie.B.X, tie.B.Y), Line = tie.Line });
            foreach (FrameSegment rod in frame.Bridges)
            {
                int stud = StudAt(frame, rod.A);
                bent.Bridges.Add(new FrameSegment { A = stud >= 0 ? bent.Studs[stud].At : arc.Bend(rod.A.X, rod.A.Y), B = arc.Bend(rod.B.X, rod.B.Y), Line = rod.Line });
            }

            foreach (FrameSegment s in bent.Rails)
                bent.FrameLength += s.Length;
            foreach (FrameSegment s in bent.Ties)
                bent.FrameLength += s.Length;
            foreach (FrameSegment s in bent.Bridges)
            {
                float length = s.Length;
                bent.FrameLength += length;
                bent.LongestBridge = Math.Max(bent.LongestBridge, length);
                if (length > style.LongRod)
                    bent.LongBridges++;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (FrameIsland island in bent.Islands)
            {
                minX = Math.Min(minX, island.MinX);
                maxX = Math.Max(maxX, island.MaxX);
                minY = Math.Min(minY, island.MinY);
                maxY = Math.Max(maxY, island.MaxY);
            }

            if (bent.Islands.Count > 0)
            {
                bent.InkWidth = maxX - minX;
                bent.InkHeight = maxY - minY;
                bent.FillFactor = bent.InkWidth > 0f && bent.InkHeight > 0f ? bent.EffectiveArea / (bent.InkWidth * bent.InkHeight) : 0f;
            }

            bent.MassKg = LetterFrame.EstimatedMass(bent, style);
            return bent;
        }

        private static FrameIsland Carried(FrameIsland island, ArcInfo arc)
        {
            var moved = new FrameIsland { Line = island.Line, Glyph = island.Glyph, Area = island.Area };
            foreach (FrameIsland.Piece piece in island.Pieces)
            {
                var made = new FrameIsland.Piece { Outer = Carried(piece.Outer, island.Glyph, arc), OuterArea = piece.OuterArea };
                foreach (List<Point2> hole in piece.Holes)
                    made.Holes.Add(Carried(hole, island.Glyph, arc));
                moved.Pieces.Add(made);
                foreach (Point2 p in made.Outer)
                {
                    moved.MinX = Math.Min(moved.MinX, p.X);
                    moved.MaxX = Math.Max(moved.MaxX, p.X);
                    moved.MinY = Math.Min(moved.MinY, p.Y);
                    moved.MaxY = Math.Max(moved.MaxY, p.Y);
                }
            }

            return moved;
        }

        private static List<Point2> Carried(List<Point2> loop, int glyph, ArcInfo arc)
        {
            var moved = new List<Point2>(loop.Count);
            foreach (Point2 p in loop)
                moved.Add(arc.Carry(glyph, p));
            return moved;
        }

        /// <summary>The stud that a rod starts at (the rod's end is the stud's own point), or -1.</summary>
        private static int StudAt(FramePlan frame, Point2 at)
        {
            for (int i = 0; i < frame.Studs.Count; i++)
            {
                if (frame.Studs[i].At.X == at.X && frame.Studs[i].At.Y == at.Y)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// One piece of a rail (straight in the straight layout, at one height) as a polyline of the bent one: from its two ends, through the studs of its line that are on it, and cut where two of those
        /// are more than RailStep apart. Between two it keeps the circle of the height, moved by how far the two are off it (so that the rail is exactly at each stud).
        /// </summary>
        private static void AddRail(List<FrameSegment> into, FrameSegment rail, FramePlan straight, FramePlan bent, ArcInfo arc)
        {
            float y = rail.A.Y, x0 = Math.Min(rail.A.X, rail.B.X), x1 = Math.Max(rail.A.X, rail.B.X);
            var stops = new List<KeyValuePair<float, Point2>> { new KeyValuePair<float, Point2>(x0, arc.Bend(x0, y)) };
            for (int i = 0; i < straight.Studs.Count; i++)
            {
                FrameStud stud = straight.Studs[i];
                if (stud.Line == rail.Line && Math.Abs(stud.At.Y - y) < 1e-4f && stud.At.X > x0 + 1e-5f && stud.At.X < x1 - 1e-5f)
                    stops.Add(new KeyValuePair<float, Point2>(stud.At.X, bent.Studs[i].At));
            }

            stops.Add(new KeyValuePair<float, Point2>(x1, arc.Bend(x1, y)));
            stops.Sort((a, b) => a.Key.CompareTo(b.Key));
            float rho = arc.RadiusAt(y);
            Point2 previous = stops[0].Value;
            for (int i = 1; i < stops.Count; i++)
            {
                float xa = stops[i - 1].Key, xb = stops[i].Key;
                Point2 offA = Off(stops[i - 1].Value, arc.Bend(xa, y)), offB = Off(stops[i].Value, arc.Bend(xb, y));
                int cuts = Math.Max(1, (int)Math.Ceiling((xb - xa) / rho / RailStep - 1e-4f));
                for (int k = 1; k <= cuts; k++)
                {
                    float t = (float)k / cuts, x = xa + (xb - xa) * t;
                    Point2 on = arc.Bend(x, y), at = k == cuts ? stops[i].Value : new Point2(on.X + offA.X + (offB.X - offA.X) * t, on.Y + offA.Y + (offB.Y - offA.Y) * t);
                    into.Add(new FrameSegment { A = previous, B = at, Line = rail.Line });
                    previous = at;
                }
            }
        }

        private static Point2 Off(Point2 a, Point2 b) => new Point2(a.X - b.X, a.Y - b.Y);
    }
}
