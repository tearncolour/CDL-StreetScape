using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One side of a pair of lines: solid when Off is 0, else dashed On/Off (m); its width (m) and colour.</summary>
    public struct LineSide
    {
        public float On, Off, Width;
        public MarkingColour Colour;

        public LineSide(float width, MarkingColour colour, float on = 0f, float off = 0f)
        {
            Width = width;
            Colour = colour;
            On = on;
            Off = off;
        }

        public static LineSide Solid(float width, MarkingColour colour) => new LineSide(width, colour);
        public static LineSide Dashed(float width, MarkingColour colour, float on, float off) => new LineSide(width, colour, on, off);
    }

    /// <summary>
    /// The longitudinal and transverse lines of GB 5768.3-2025 that the text dimensions fully (the figures only confirm them). All along a centre line of points in the ground plane, the traffic
    /// running from the first point to the last; X right. Each returns one shape per colour. Pure.
    /// Clauses: 5.3.2 (线4 tidal lane), 5.3.3 (tidal stop line), 5.4.2/5.4.3 (线5/线6 edge lines), 5.5.2 (线7 left-turn waiting area lines), 5.6.2 (线8 junction guide line), 5.7.2 (线9 lane-change
    /// line), 5.9.2 (线12 chevron and 线13 half-disc distance marks), 5.15.7 (线41 zigzag warning), 5.16.2 (线42 bicycle crossing), 6.1.2 to 6.1.4 (线43 double yellow, 线44 yellow solid-and-dashed,
    /// 线45 single yellow), 6.2.2/6.2.3 (线46 white solid, 线47 white solid-and-dashed), 6.5.2.2 (线54 give-way line), 6.9.1/6.9.2 (线59/线60 bus and HOV lane lines), 6.10.2 (线61 guide lane line).
    /// </summary>
    public static class MarkingLines
    {
        /// <summary>One line along the centre: solid when side.Off is 0, else dashed from the phase (m into the pattern).</summary>
        public static MarkingShape Line(IList<Vec3> centre, LineSide side, float phase = 0f) =>
            RoadMarkings.Dashed(centre, new DashSpec(side.Off > 0f ? side.On : 1f, side.Off > 0f ? side.Off : 0f, side.Width, side.Colour), phase);

        /// <summary>
        /// Two lines gap apart (the clear space between them, m) about the centre: the left one (the driver's left, -X) and the right one, each solid or dashed. 线4 (two yellow dashed, gap 10 to 15 cm,
        /// the dashes of the road's lane line), 线43 (two yellow solid, gap 10 to 30), 线44 (yellow solid and 4-6 dashed), 线47 (white solid and the road's dashes), the tidal stop line (white, 50/50
        /// dashes), 线7 (two dashed 50/50, 15 wide), 线42 (two white 100/100 dashed, 8 or 10 wide, 2 m or more apart centre to centre).
        /// </summary>
        public static List<MarkingShape> Pair(IList<Vec3> centre, float gap, LineSide left, LineSide right, float phase = 0f)
        {
            var shapes = new List<MarkingShape>();
            if (centre == null || centre.Count < 2 || float.IsNaN(gap) || gap < 0f)
                return shapes;
            shapes.Add(Line(MarkingFills.Offset(centre, -(gap + left.Width) * 0.5f), left, phase));
            shapes.Add(Line(MarkingFills.Offset(centre, (gap + right.Width) * 0.5f), right, phase));
            return shapes;
        }

        // ---- 5.3.2 线4 潮汐车道线: two yellow dashed lines 15 cm wide, 10 to 15 cm apart, dashed as the road's lane line (6-9, 3-9 or 2-4); one at each side of the tidal lane (the caller places two).
        public static List<MarkingShape> TidalLaneLine(IList<Vec3> centre, DashSpec roadLaneLine, float gap = 0.10f, float phase = 0f) =>
            Pair(centre, Math.Max(0.10f, Math.Min(0.15f, gap)), LineSide.Dashed(0.15f, MarkingColour.Yellow, roadLaneLine.On, roadLaneLine.Off), LineSide.Dashed(0.15f, MarkingColour.Yellow, roadLaneLine.On, roadLaneLine.Off), phase);

        // ---- 5.3.3 潮汐车道停止线: white solid and dashed, both 15 wide, 10 to 15 apart, the dashes 50/50; the dashed one on the side of the junction's centre (the caller says which).
        public static List<MarkingShape> TidalStopLine(IList<Vec3> centre, bool dashedOnRight, float gap = 0.10f, float phase = 0f)
        {
            LineSide solid = LineSide.Solid(0.15f, MarkingColour.White), dashed = LineSide.Dashed(0.15f, MarkingColour.White, 0.5f, 0.5f);
            return Pair(centre, Math.Max(0.10f, Math.Min(0.15f, gap)), dashedOnRight ? solid : dashed, dashedOnRight ? dashed : solid, phase);
        }

        // ---- 5.4.2/5.4.3 线5 车行道边缘实线 and 线6 车行道边缘虚线 (60 on, 30 off, "密虚线"): 15 or 20 wide (10 on special roads); white on the right, yellow on the left (both white on a two-way single lane).
        public static readonly float[] EdgeWidths = { 0.15f, 0.20f, 0.10f };

        public static MarkingShape EdgeLine(IList<Vec3> centre, bool leftSide, bool dashed, float width = 0.15f, bool twoWaySingleLane = false, float phase = 0f)
        {
            MarkingColour colour = leftSide && !twoWaySingleLane ? MarkingColour.Yellow : MarkingColour.White;
            float w = RoadMarkings.NearestOf(EdgeWidths, width);
            return Line(centre, dashed ? LineSide.Dashed(w, colour, 0.6f, 0.3f) : LineSide.Solid(w, colour), phase);
        }

        // ---- 5.5.2 线7 左弯待转区线: two parallel dashed lines, 15 wide, 50 on 50 off; the one that continues the left (yellow) edge line is yellow, the other white. The lines are given as two centre lines.
        public static List<MarkingShape> WaitingAreaLines(IList<Vec3> yellowSide, IList<Vec3> whiteSide, float phase = 0f) =>
            new List<MarkingShape> { Line(yellowSide, LineSide.Dashed(0.15f, MarkingColour.Yellow, 0.5f, 0.5f), phase), Line(whiteSide, LineSide.Dashed(0.15f, MarkingColour.White, 0.5f, 0.5f), phase) };

        // ---- 5.6.2 线8 路口导向线: dashed 2 m on 2 m off along a curve or a straight, 15 wide (8 when it guides bicycles), the colour of the lane line it continues.
        public static MarkingShape JunctionGuideLine(IList<Vec3> centre, MarkingColour colour, bool forBicycles = false, float phase = 0f) =>
            Line(centre, LineSide.Dashed(forBicycles ? 0.08f : 0.15f, colour, 2f, 2f), phase);

        // ---- 6.12.2 线65 出入口标线: the thick dashed line (粗虚线, "3-3 线": 45 wide, 3 m on 3 m off) between the through lanes and the lane that leaves or joins, and the thick solid line (粗实线, 45 wide, 2 m or
        // more) where it meets the nose (Fig. 94). A line along a road; the solid part is given in metres of the whole line (solidFrom to solidTo), phase is where this piece of it starts.
        public const float ThickWidth = 0.45f, ThickDash = 3f, ThickMinSolid = 2f;

        public static MarkingShape ThickLine(IList<Vec3> centre, float phase = 0f, float solidFrom = 0f, float solidTo = 0f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (centre == null || centre.Count < 2 || float.IsNaN(phase) || float.IsInfinity(phase))
                return shape;
            var at = new List<float>();
            float length = RoadMarkings.Lengths(centre, at);
            if (!(length > 0f))
                return shape;
            float a = Math.Max(0f, (float.IsNaN(solidFrom) ? 0f : solidFrom) - phase), b = Math.Min(length, (float.IsNaN(solidTo) ? 0f : solidTo) - phase);
            bool solid = b - a > 0.01f;
            var intervals = new List<Dash>();
            foreach (Dash dash in Marking.Dashes(length, ThickDash, ThickDash, phase))
            {
                if (!solid || dash.End <= a || dash.Start >= b)
                {
                    intervals.Add(dash);
                    continue;
                }

                // The part of a dash that the solid line does not cover stays a dash.
                if (dash.Start < a - 0.05f)
                    intervals.Add(new Dash(dash.Start, a));
                if (dash.End > b + 0.05f)
                    intervals.Add(new Dash(b, dash.End));
            }

            if (solid)
                intervals.Add(new Dash(a, b));
            foreach (Dash d in intervals)
            {
                var points = new List<Vec3> { RoadMarkings.PointAt(centre, at, d.Start) };
                for (int i = 0; i < centre.Count; i++)
                {
                    if (at[i] > d.Start + 1e-4f && at[i] < d.End - 1e-4f && Vec3.DistanceXZ(centre[i], points[points.Count - 1]) > 1e-4f)
                        points.Add(centre[i]);
                }

                points.Add(RoadMarkings.PointAt(centre, at, d.End));
                RoadMarkings.Ribbon(shape.Mesh, points, ThickWidth);
            }

            return shape;
        }

        // ---- 5.7.2 线9 换道线: white dashed 1 m on 1 m off, as wide as the guide lane line it joins.
        public static MarkingShape LaneChangeLine(IList<Vec3> centre, float width = 0.15f, float phase = 0f) => Line(centre, LineSide.Dashed(width, MarkingColour.White, 1f, 1f), phase);

        // ---- 5.9.2 a) 线12 折线车距确认标线: white chevrons 3 m across, 40 or 45 wide, apex 60 degrees, pointing the way the traffic runs; two make a group 5 m apart; groups 50 m apart.
        public const float ChevronSpan = 3f, ChevronInGroup = 5f, ChevronGroupSpacing = 50f, ChevronApexDegrees = 60f;

        /// <summary>One chevron whose apex is at (x, z): the arms go back and out at 30 degrees from the axis to 1.5 m each side.</summary>
        public static void Chevron(MeshBuilder mesh, float x, float z, float width = 0.4f, float span = ChevronSpan)
        {
            float back = span * 0.5f / (float)Math.Tan(ChevronApexDegrees * 0.5 * Math.PI / 180.0);
            RoadMarkings.Ribbon(mesh, new List<Vec3> { new Vec3(x - span * 0.5f, 0f, z - back), new Vec3(x, 0f, z), new Vec3(x + span * 0.5f, 0f, z - back) }, width);
        }

        /// <summary>The chevrons of a lane from z = 0: groups of two (5 m apart) every 50 m, as many groups as fit in the length; the apex of the first at z = 0 + back.</summary>
        public static MarkingShape DistanceChevrons(float length, int groups = 2, float width = 0.4f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (!(length > 0f) || groups < 1)
                return shape;
            float w = RoadMarkings.NearestOf(RoadMarkings.CrosswalkStripeWidths, width);
            float back = ChevronSpan * 0.5f / (float)Math.Tan(ChevronApexDegrees * 0.5 * Math.PI / 180.0);
            for (int g = 0; g < groups; g++)
            {
                for (int k = 0; k < 2; k++)
                {
                    float apex = back + g * ChevronGroupSpacing + k * ChevronInGroup;
                    if (apex > length)
                        return shape;
                    Chevron(shape.Mesh, 0f, apex, w);
                }
            }

            return shape;
        }

        // ---- 5.9.2 b) 线13 半圆状车距确认标线: white half discs of radius 30 cm outside the edge line, their flat side toward it 3 to 5 cm away, every 50 m.
        public const float HalfDiscRadius = 0.3f, HalfDiscGap = 0.04f, HalfDiscSpacing = 50f;

        /// <summary>The half discs along an edge line (points), on its right (outside, +X of the way it runs) or left, from the start every 50 m.</summary>
        public static MarkingShape HalfDiscs(IList<Vec3> edgeLine, float edgeWidth, bool rightSide = true, float phase = 0f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            if (edgeLine == null || edgeLine.Count < 2)
                return shape;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(edgeLine, at);
            // The discs are at every 50 m of the whole line, the first at its start: a piece of it that starts phase metres along has the discs after that place (one at the place itself belongs to the piece before),
            // and one at its own end if there is one.
            float first = 0f;
            if (!float.IsNaN(phase) && !float.IsInfinity(phase) && phase > 0f)
                first = (float)(Math.Floor(phase / HalfDiscSpacing + 1e-6) + 1.0) * HalfDiscSpacing - phase;
            for (float s = first; s <= total + 1e-4f; s += HalfDiscSpacing)
            {
                Vec3 centre = RoadMarkings.PointAt(edgeLine, at, s);
                int i = Math.Min(edgeLine.Count - 2, Math.Max(0, IndexAt(at, s)));
                Vec3 heading = (edgeLine[i + 1] - edgeLine[i]).HeadingXZ();
                Vec3 right = heading.RightXZ() * (rightSide ? 1f : -1f);
                Vec3 flat = centre + right * (edgeWidth * 0.5f + HalfDiscGap);
                var disc = new List<Vec3>();
                // The half disc bulges away from the line: from heading forward, round the outside, to heading back.
                double a0 = Math.Atan2(heading.Z, heading.X);
                for (int k = 0; k <= 12; k++)
                {
                    double a = a0 - (rightSide ? 1 : -1) * Math.PI * k / 12;
                    disc.Add(flat + new Vec3((float)Math.Cos(a), 0f, (float)Math.Sin(a)) * HalfDiscRadius);
                }

                MarkingFills.Solid(shape.Mesh, disc);
            }

            return shape;
        }

        private static int IndexAt(List<float> at, float s)
        {
            for (int i = 1; i < at.Count; i++)
            {
                if (s <= at[i])
                    return i - 1;
            }

            return at.Count - 2;
        }

        // ---- 5.15.7 线41 注意前方路面状况标记: a white zigzag, 20 wide, apex angle 60 degrees; its height (across) and extent (along) as needed.
        /// <summary>A zigzag from z = 0 along the lane, swinging between x = -height/2 and +height/2, each leg at 60 degrees from the axis, with a 60-degree interior apex.</summary>
        public static MarkingShape Zigzag(float length, float height, float width = 0.2f, MarkingColour colour = MarkingColour.White, bool startLeft = true)
        {
            var shape = new MarkingShape { Colour = colour };
            if (!(length > 0f) || !(height > 0f) || !(width > 0f))
                return shape;
            float leg = height * (float)Math.Tan(30.0 * Math.PI / 180.0);
            var points = new List<Vec3>();
            float x = startLeft ? -height * 0.5f : height * 0.5f;
            for (float z = 0f; z <= length + 1e-4f; z += leg)
            {
                points.Add(new Vec3(x, 0f, z));
                x = -x;
            }

            if (points.Count < 2)
                return shape;
            ZigzagRibbon(shape.Mesh, points, width);
            return shape;
        }

        internal static void ZigzagRibbon(MeshBuilder mesh, IList<Vec3> points, float width)
        {
            var up = new Vec3(0f, 1f, 0f);
            int first = mesh.VertexCount;
            for (int i = 0; i < points.Count; i++)
            {
                Vec3 incoming = (points[Math.Max(0, i - 1)] - points[i]).HeadingXZ();
                Vec3 outgoing = (points[Math.Min(points.Count - 1, i + 1)] - points[i]).HeadingXZ();
                Vec3 right;
                if (i == 0)
                    right = outgoing.RightXZ() * (width * 0.5f);
                else if (i == points.Count - 1)
                    right = (incoming * -1f).RightXZ() * (width * 0.5f);
                else
                {
                    Vec3 bisector = (outgoing - incoming).HeadingXZ().RightXZ();
                    right = bisector * (width * 0.5f / Vec3.Dot(bisector, outgoing.RightXZ()));
                }
                mesh.Vertex(points[i] - right, up, 0f, i);
                mesh.Vertex(points[i] + right, up, 1f, i);
                if (i > 0)
                    mesh.QuadFacing(first + 2 * i - 2, first + 2 * i - 1, first + 2 * i + 1, first + 2 * i, up);
            }
        }

        // ---- 5.16.2/5.16.3 线42 非机动车横道线: two white dashed lines 8 or 10 wide, 1 m on 1 m off, 2 m or more apart (centre to centre, the width of the cycle lane).
        public static List<MarkingShape> BicycleCrossing(IList<Vec3> centre, float crossingWidth = 2f, float lineWidth = 0.1f)
        {
            float w = Math.Abs(lineWidth - 0.08f) < Math.Abs(lineWidth - 0.1f) ? 0.08f : 0.1f;
            float apart = Math.Max(2f, crossingWidth);
            return Pair(centre, apart - w, LineSide.Dashed(w, MarkingColour.White, 1f, 1f), LineSide.Dashed(w, MarkingColour.White, 1f, 1f));
        }

        // ---- 6.1.2 线43 双黄实线: two yellow solid lines 15 wide (10 in special cases), 10 to 30 apart; 50 or more apart they are filled with yellow 40/45 hatching at 45 degrees, 1 m pitch (6.1.2).
        public static List<MarkingShape> DoubleYellow(IList<Vec3> centre, float gap = 0.10f, float width = 0.15f)
        {
            float w = Math.Abs(width - 0.10f) < Math.Abs(width - 0.15f) ? 0.10f : 0.15f;
            float g = Math.Max(0.10f, gap);
            List<MarkingShape> shapes = Pair(centre, g, LineSide.Solid(w, MarkingColour.Yellow), LineSide.Solid(w, MarkingColour.Yellow));
            if (g >= 0.5f && centre != null && centre.Count >= 2)
            {
                var band = new MarkingShape { Colour = MarkingColour.Yellow };
                List<Vec3> leftEdge = MarkingFills.Offset(centre, -g * 0.5f), rightEdge = MarkingFills.Offset(centre, g * 0.5f);
                var area = new List<Vec3>(leftEdge);
                for (int i = rightEdge.Count - 1; i >= 0; i--)
                    area.Add(rightEdge[i]);
                MarkingFills.Hatch(band.Mesh, AreaIndex.FromStrip(leftEdge, rightEdge), area, 0.4f, 1f, 45f);
                MarkingFills.FollowLine(band.Mesh, 0, centre);
                shapes.Add(band);
            }

            return shapes;
        }

        // ---- 6.1.3 线44 黄色虚实线: yellow, 15 wide (10), 10 to 30 apart, the dashed one 4 m on 6 m off; the dashed line on the side that may cross.
        public static List<MarkingShape> YellowSolidDashed(IList<Vec3> centre, bool dashedOnRight, float gap = 0.10f, float width = 0.15f, float phase = 0f)
        {
            float w = Math.Abs(width - 0.10f) < Math.Abs(width - 0.15f) ? 0.10f : 0.15f;
            LineSide solid = LineSide.Solid(w, MarkingColour.Yellow), dashed = LineSide.Dashed(w, MarkingColour.Yellow, 4f, 6f);
            return Pair(centre, Math.Max(0.10f, Math.Min(0.30f, gap)), dashedOnRight ? solid : dashed, dashedOnRight ? dashed : solid, phase);
        }

        // ---- 6.1.4 线45 单黄实线: yellow solid, 15 wide, up to 30 on a wide road.
        public static MarkingShape SingleYellow(IList<Vec3> centre, float width = 0.15f) => Line(centre, LineSide.Solid(Math.Max(0.15f, Math.Min(0.30f, width)), MarkingColour.Yellow));

        // ---- 6.2.2 线46 禁止跨越同向车行道分界线: white solid, 10 or 15 wide.
        public static MarkingShape WhiteSolid(IList<Vec3> centre, float width = 0.15f) => Line(centre, LineSide.Solid(Math.Abs(width - 0.10f) < Math.Abs(width - 0.15f) ? 0.10f : 0.15f, MarkingColour.White));

        // ---- 6.2.3 线47 白色虚实线: white, 15 wide, 10 to 30 apart, the dashed one as the road's lane line.
        public static List<MarkingShape> WhiteSolidDashed(IList<Vec3> centre, bool dashedOnRight, DashSpec roadLaneLine, float gap = 0.10f, float phase = 0f)
        {
            LineSide solid = LineSide.Solid(0.15f, MarkingColour.White), dashed = LineSide.Dashed(0.15f, MarkingColour.White, roadLaneLine.On, roadLaneLine.Off);
            return Pair(centre, Math.Max(0.10f, Math.Min(0.30f, gap)), dashedOnRight ? solid : dashed, dashedOnRight ? dashed : solid, phase);
        }

        // ---- 6.5.2.2 线54 减速让行线: two white dashed lines (60 on, 20 off, 20 wide, 20 apart) across the lanes and, on each motor lane, a white triangle pointing at the driver, 1.2 m wide at its
        // base and 3 m high; Fig. 77 a) draws it hollow with 15-wide strokes, its base 200 to 250 behind the near line.
        public const float GiveWayTriangleBase = 1.2f, GiveWayTriangleHeight = 3f, GiveWayTriangleStroke = 0.15f;

        /// <summary>The line across a span centred on x = 0 (the pair centred on z = 0, the traffic from -Z) and a hollow triangle for each lane centre, its base triangleGap (2 m, Fig. 77: 200 to 250) behind the near line.</summary>
        public static List<MarkingShape> GiveWayDashed(float span, IList<float> laneCentres = null, float triangleGap = RoadMarkings.GiveWayTextGap)
        {
            var shapes = new List<MarkingShape>();
            if (!(span > 0f) || float.IsInfinity(span))
                return shapes;
            var across = new List<Vec3> { new Vec3(-span * 0.5f, 0f, 0f), new Vec3(span * 0.5f, 0f, 0f) };
            // Across the road the line runs +X; "left" of that run is +Z (ahead), so the near line (toward -Z) is the right one.
            shapes.AddRange(Pair(across, 0.2f, LineSide.Dashed(0.2f, MarkingColour.White, 0.6f, 0.2f), LineSide.Dashed(0.2f, MarkingColour.White, 0.6f, 0.2f)));
            if (laneCentres != null)
            {
                var triangles = new MarkingShape { Colour = MarkingColour.White };
                float nearEdge = -(0.2f + 0.1f);
                float gap = float.IsNaN(triangleGap) ? RoadMarkings.GiveWayTextGap : Math.Max(0f, triangleGap);
                foreach (float x in laneCentres)
                {
                    if (float.IsNaN(x) || float.IsInfinity(x))
                        continue;
                    float baseZ = nearEdge - gap;
                    MarkingFills.Outline(triangles.Mesh, new List<Vec3> { new Vec3(x - GiveWayTriangleBase * 0.5f, 0f, baseZ), new Vec3(x + GiveWayTriangleBase * 0.5f, 0f, baseZ), new Vec3(x, 0f, baseZ - GiveWayTriangleHeight) }, GiveWayTriangleStroke);
                }

                shapes.Add(triangles);
            }

            return shapes;
        }

        // ---- 6.9.1/6.9.2 线59 公交车专用车道线 and 线60 多乘员车专用车道线: white dashed 5 m on 1 m off, 20 or 25 wide; the words (公交, 快速公交, 多乘员, and a time) are placed by the caller (5.14 sizes).
        public static MarkingShape ReservedLaneLine(IList<Vec3> centre, float width = 0.2f, float phase = 0f) =>
            Line(centre, LineSide.Dashed(Math.Abs(width - 0.25f) < Math.Abs(width - 0.2f) ? 0.25f : 0.2f, MarkingColour.White, 5f, 1f), phase);

        // ---- 6.10.2 线61 导向车道线: white solid, as wide as the lane line it continues (10 or 15), 30 m or more long.
        public const float GuideLaneMinLength = 30f;

        public static MarkingShape GuideLaneLine(IList<Vec3> centre, float width = 0.15f) => WhiteSolid(centre, width);
    }
}
