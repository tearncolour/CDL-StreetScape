using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The top of a noise barrier's wall (0.12.49).</summary>
    public enum NoiseTop
    {
        /// <summary>直立型.</summary>
        Upright,

        /// <summary>折角型 (折臂式): the posts go on NoiseArm at NoiseArmAngle toward the traffic with two modules on them.</summary>
        Cranked,

        /// <summary>弧形: the posts bend toward the traffic in three straight lengths of a module, at 7.5, 22.5 and 37.5 degrees from upright (45 degrees in all), a module on each.</summary>
        Arc
    }

    /// <summary>What a noise barrier stands on (0.12.49).</summary>
    public enum NoiseBase
    {
        /// <summary>A concrete strip footing 0.5 m wide, 0.3 m over the ground (by a road on the ground).</summary>
        Plinth,

        /// <summary>A low concrete wall 0.4 m wide, as high as asked.</summary>
        Wall,

        /// <summary>A bridge's crash wall (防撞墙) with the F shape toward the traffic, 0.5 m across its top, as high as asked (0.81 or 1.0 m).</summary>
        CrashWall
    }

    /// <summary>Which of a noise barrier's upright modules are transparent (0.12.49).</summary>
    public enum GlassBand
    {
        None,

        /// <summary>Two modules (1.0 m) half way up.</summary>
        Middle,

        /// <summary>Four modules (2.0 m) half way up: a supplier's drawing of a 4 m barrier has 1 m of metal, 2 m of PC sheet and 1 m of metal.</summary>
        Wide,

        /// <summary>From 1.0 m up to under the top module.</summary>
        Upper,

        /// <summary>All but the lowest module.</summary>
        All
    }

    /// <summary>A noise barrier's wall (0.12.49): the height of its panels (m, whole modules), its top, its foundation, the spacing of its posts, its transparent band and its colours.</summary>
    public struct NoiseStyle
    {
        public float Height, BaseHeight, Spacing;
        public NoiseTop Top;
        public NoiseBase Base;
        public GlassBand Band;
        public SolidColour Panel, Glass, Post;

        /// <summary>How much of it is built (BarrierLod): 0 all, 1 what is seen from 70 to 250 m, 2 what is seen from farther; its footprint and its height are the same.</summary>
        public int Level;
    }

    /// <summary>A noise barrier that covers the road (0.12.49): one side and a cantilevered roof (半封闭), or both sides and an arched roof (全封闭).</summary>
    public enum NoiseCover
    {
        Semi,
        Full
    }

    /// <summary>A sound wall that is not of metal panels (0.12.49).</summary>
    public enum SoundWall
    {
        /// <summary>水泥声屏障: lightweight cement panels between H posts.</summary>
        Cement,

        /// <summary>砖墙组合式声屏障: a brick wall with pilasters and metal panels over it.</summary>
        Brick
    }

    /// <summary>
    /// Noise barriers (声屏障, 0.12.49; the owner: "追加隔音墙部分，高架隔音和其他隔音墙"), from scratch/research-barriers/REPORT.md. GB 50688-2011 (2019) 7.9 asks for one where a road's noise is over GB 3096 and
    /// other measures do not bring it down, by its acoustic design (HJ/T 90) and for its own weight and the wind, and names the kinds (直板式、圆弧式、悬臂式、半封闭式、全封闭式, its commentary);
    /// CJJ 11-2011 (2019) 9.6.5 asks for one on a viaduct by hospitals, schools and homes and for an enclosed one to be checked for snow too. Neither sets sizes: the posts, the panels, the spacings and
    /// the base plates are suppliers' stock sizes and drawings (H-section posts 2.0 to 2.5 m apart, HW100 to HW150; sound-absorbing panels 1960 or 2460 x 500 x 80 mm stacked in the posts' channels;
    /// PC sheet 15 mm; cement panels 4000 x 600 x 100 mm on HW150 posts 4 m apart), the enclosures' arches and the planting are estimated from photos, and each builder says which. A wall is a path in
    /// the section (SectionPath) that its posts follow and its modules are laid along. Pure.
    /// </summary>
    public static partial class Barriers
    {
        /// <summary>The height of a noise barrier's panel module (m).</summary>
        public const float NoiseModule = 0.5f;

        /// <summary>The cranked top's arm: its angle from upright toward the traffic (about 30 degrees in a supplier's drawing) and its length (two modules).</summary>
        public const float NoiseArmAngle = (float)(Math.PI / 6.0), NoiseArm = 1f;

        /// <summary>The thickness of the metal panels and of the PC sheets (m).</summary>
        private const float PanelThick = 0.08f, SheetThick = 0.015f;

        /// <summary>
        /// A path in a barrier's section: u across (+ the line's right), y up. Each length's normal is its direction turned a quarter clockwise (the line's right for a length going up); a point off the
        /// path at a corner is on the line that halves the corner (a mitre), so that what is laid on two lengths meets there with neither a gap nor an overlap.
        /// </summary>
        private sealed class SectionPath
        {
            private readonly List<(float u, float y)> m_Points;
            private readonly List<float> m_At = new List<float> { 0f };
            private readonly List<(float u, float y)> m_Dir = new List<(float u, float y)>(), m_Normal = new List<(float u, float y)>();

            public SectionPath(List<(float u, float y)> points)
            {
                m_Points = points;
                for (int i = 1; i < points.Count; i++)
                {
                    float du = points[i].u - points[i - 1].u, dy = points[i].y - points[i - 1].y, l = (float)Math.Sqrt(du * du + dy * dy);
                    m_Dir.Add((du / l, dy / l));
                    m_Normal.Add((dy / l, -du / l));
                    m_At.Add(m_At[m_At.Count - 1] + l);
                }
            }

            public float Length => m_At[m_At.Count - 1];

            public int Count => m_Points.Count;

            public float At(int i) => m_At[i];

            /// <summary>Which length s is on (the earlier one at a corner).</summary>
            public int LengthOf(float s)
            {
                int i = 0;
                while (i + 2 < m_Points.Count && s > m_At[i + 1] + 1e-4f)
                    i++;
                return i;
            }

            /// <summary>The point s along the path and o off it along its normal; beyond its ends along its first or last length.</summary>
            public (float u, float y) Point(float s, float o)
            {
                for (int j = 1; j + 1 < m_Points.Count; j++)
                {
                    if (Math.Abs(s - m_At[j]) >= 1e-4f)
                        continue;
                    (float u, float y) a = m_Normal[j - 1], b = m_Normal[j];
                    float mu = a.u + b.u, my = a.y + b.y, ml = (float)Math.Sqrt(mu * mu + my * my);
                    float k = o / ((mu * b.u + my * b.y) / ml);
                    return (m_Points[j].u + mu / ml * k, m_Points[j].y + my / ml * k);
                }

                int i = s < 0f ? 0 : s > Length ? m_Points.Count - 2 : LengthOf(s);
                float t = s - m_At[i];
                return (m_Points[i].u + m_Dir[i].u * t + m_Normal[i].u * o, m_Points[i].y + m_Dir[i].y * t + m_Normal[i].y * o);
            }

            /// <summary>A strip of the path from s0 to s1 between the offsets o0 and o1: a convex section of four corners.</summary>
            public List<(float u, float v)> Strip(float s0, float s1, float o0, float o1) => new List<(float u, float v)> { Point(s0, o0), Point(s0, o1), Point(s1, o1), Point(s1, o0) };
        }

        /// <summary>An H section: flange width B (along the line), depth D (across), web Tw and flanges Tf (m).</summary>
        private struct HSection
        {
            public float B, D, Tw, Tf;

            public HSection(float b, float d, float tw, float tf)
            {
                B = b;
                D = d;
                Tw = tw;
                Tf = tf;
            }
        }

        /// <summary>
        /// The post a wall of panels needs by the length of its path (a supplier's rule: HW100 x 100 x 6 x 8 up to 3 m, HW125 x 125 x 6.5 x 9 up to 5 m, HW150 x 150 x 7 x 10 above, with the
        /// sizes of its table).
        /// </summary>
        private static HSection HFor(float length) => length > 5.01f ? new HSection(0.15f, 0.15f, 0.007f, 0.010f) : length > 3.01f ? new HSection(0.125f, 0.125f, 0.0065f, 0.009f) : new HSection(0.10f, 0.10f, 0.006f, 0.008f);

        /// <summary>The parts of a noise barrier: its panels and their faces, its glass and the glass's frames, its posts, their bolts, its concrete and its rubber strips.</summary>
        private sealed class NoiseParts
        {
            public readonly MarkingShape Panel, Face, Glass, Frame, Steel, Bolts, Concrete, Rubber;

            /// <summary>What the cap strip over a wall's top module is: the panels' own colour, or galvanised steel over cement.</summary>
            public MarkingShape Cap;

            private readonly int m_Level;

            public NoiseParts(SolidColour panel, DetailKind face, SolidColour glass, SolidColour post, int level = BarrierLod.Full)
            {
                m_Level = level;
                Panel = new MarkingShape { Solid = panel };
                Cap = Panel;
                Face = new MarkingShape { Solid = panel, Detail = face };
                Glass = new MarkingShape { Solid = SolidGlass.Is(glass) ? glass : SolidColour.Glass };
                Frame = new MarkingShape { Solid = SolidColour.LightGray };
                Steel = new MarkingShape { Solid = post };
                Bolts = new MarkingShape { Solid = SolidColour.Metal };
                Concrete = new MarkingShape { Solid = SolidColour.Concrete };
                Rubber = new MarkingShape { Solid = SolidColour.Black, Detail = DetailKind.Plastic };
            }

            /// <summary>The shapes of the wall at its level: the bolts, the rubber and the glass frames are left out from level 1, the louvre sheets and the steel from level 2 (its posts and ribs).</summary>
            public List<MarkingShape> All => m_Level == BarrierLod.Full ? new List<MarkingShape> { Panel, Face, Glass, Frame, Steel, Bolts, Concrete, Rubber }
                : m_Level == BarrierLod.Reduced ? new List<MarkingShape> { Panel, Face, Glass, Steel, Concrete }
                : new List<MarkingShape> { Panel, Glass, Concrete };
        }

        /// <summary>
        /// The foundation of a wall whose posts stand at uc across the run's line, its traffic toward k (+1 the line's right): along the whole line, closed at the ends of the whole run; its top over the
        /// line. The crash wall's face is the F shape (a 75 mm upright lip, 55 degrees to 255 mm, 84 degrees to the top: the F-shape barrier JTG D81-2017's commentary to 6.3.4 recommends for bridges, with
        /// the usual proportions) and its back upright; its top 0.5 m, the plinth and the low wall are estimates.
        /// </summary>
        private static float NoiseFoundation(MeshBuilder concrete, WallLine run, NoiseBase base_, float height, float uc, float k)
        {
            List<(float u, float v)> profile;
            float top;
            switch (base_)
            {
                case NoiseBase.CrashWall:
                {
                    top = Math.Max(0.6f, Math.Min(1.2f, height));
                    float half = 0.25f, knee = half + (top - 0.255f) / (float)Math.Tan(84.0 * Math.PI / 180.0), foot = knee + 0.18f / (float)Math.Tan(55.0 * Math.PI / 180.0);
                    profile = new List<(float u, float v)>
                    {
                        (uc - k * half, -Embed), (uc + k * foot, -Embed), (uc + k * foot, 0.075f), (uc + k * knee, 0.255f), (uc + k * half, top - 0.02f), (uc + k * (half - 0.02f), top),
                        (uc - k * (half - 0.02f), top), (uc - k * half, top - 0.02f)
                    };
                    break;
                }

                case NoiseBase.Wall:
                    top = Math.Max(0.4f, Math.Min(1.2f, height));
                    profile = new List<(float u, float v)> { (uc - 0.2f, -Embed), (uc + 0.2f, -Embed), (uc + 0.2f, top - 0.03f), (uc + 0.17f, top), (uc - 0.17f, top), (uc - 0.2f, top - 0.03f) };
                    break;
                default:
                    top = 0.3f;
                    profile = new List<(float u, float v)> { (uc - 0.25f, -Embed), (uc + 0.25f, -Embed), (uc + 0.25f, top - 0.02f), (uc + 0.23f, top), (uc - 0.23f, top), (uc - 0.25f, top - 0.02f) };
                    break;
            }

            run.Along(concrete, 0f, run.Total, profile);
            return top;
        }

        /// <summary>The path of a wall's posts at uc from its panels' foot: upright for height, then its top (Cranked, Arc) toward k.</summary>
        private static SectionPath WallPath(float uc, float foot, float height, NoiseTop top, float k)
        {
            var points = new List<(float u, float y)> { (uc, foot), (uc, foot + height) };
            if (top == NoiseTop.Cranked)
                points.Add((uc + k * NoiseArm * (float)Math.Sin(NoiseArmAngle), foot + height + NoiseArm * (float)Math.Cos(NoiseArmAngle)));
            if (top == NoiseTop.Arc)
            {
                foreach (double degrees in new[] { 7.5, 22.5, 37.5 })
                {
                    (float u, float y) p = points[points.Count - 1];
                    double a = degrees * Math.PI / 180.0;
                    points.Add((p.u + k * NoiseModule * (float)Math.Sin(a), p.y + NoiseModule * (float)Math.Cos(a)));
                }
            }

            return new SectionPath(points);
        }

        /// <summary>Which modules of a path are glass: the band among its first upright ones; none on its top.</summary>
        private static bool[] GlassModules(int modules, int upright, GlassBand band)
        {
            var glass = new bool[modules];
            int from = 0, to = 0;
            switch (band)
            {
                case GlassBand.Middle:
                    to = upright >= 4 ? (from = (upright - 2) / 2) + 2 : 0;
                    break;
                case GlassBand.Wide:
                {
                    int count = Math.Min(4, upright - 2);
                    to = count >= 2 ? (from = (upright - count) / 2) + count : 0;
                    break;
                }

                case GlassBand.Upper:
                    from = 2;
                    to = upright - 1;
                    break;
                case GlassBand.All:
                    from = 1;
                    to = upright;
                    break;
            }

            for (int m = from; m < to && m < upright; m++)
                glass[m] = true;
            return glass;
        }

        /// <summary>
        /// The line a part of a noise barrier is built along (0.12.49): the run's own line (a foundation), the line taken straight from post to post (PostLine: a wall of panels is straight between its
        /// posts, and a bay that ended near a corner of the road's line would fold back on itself where the wall reaches far across it), or such a line moved across (Moved: an enclosure's far wall).
        /// Its posts are distances along it (from the one at or before its start to the one at or after its end, as PanelPosts) and the corner each is at (PostCorner, -1 beyond its ends); StartAhead and
        /// EndAhead the ways of the planes where the piece meets the pieces before and after it (JoinAheads; null at an end of the whole run); First whether the piece starts the whole run.
        /// </summary>
        private sealed class WallLine
        {
            public List<Vec3> Points;
            public readonly List<float> At = new List<float>();
            public float Total;
            public readonly List<float> Posts = new List<float>();
            public readonly List<int> PostCorner = new List<int>();
            public Vec3? StartAhead, EndAhead;
            public bool First;

            public WallLine(List<Vec3> points, Vec3? startAhead, Vec3? endAhead, bool first)
            {
                Points = points;
                Total = RoadMarkings.Lengths(points, At);
                StartAhead = startAhead;
                EndAhead = endAhead;
                First = first;
            }

            /// <summary>A point s along the line, or beyond its ends along its end chords.</summary>
            public Vec3 Point(float s) => PointBeyond(Points, At, s);

            /// <summary>The way across the line at corner i as a swept part has it: the mitre's right, longer by 1 / cos(half the turn), at an inner corner; at an end the shared plane's, else the chord's.</summary>
            public Vec3 Across(int i)
            {
                int n = Points.Count;
                if (i == 0)
                    return StartAhead.HasValue ? StartAhead.Value.HeadingXZ().RightXZ() : (Points[1] - Points[0]).HeadingXZ().RightXZ();
                if (i == n - 1)
                    return EndAhead.HasValue ? EndAhead.Value.HeadingXZ().RightXZ() : (Points[n - 1] - Points[n - 2]).HeadingXZ().RightXZ();
                Vec3 before = (Points[i] - Points[i - 1]).HeadingXZ(), right = Ahead(i).RightXZ();
                return right * (1f / Math.Max(0.5f, Vec3.Dot(right, before.RightXZ())));
            }

            /// <summary>The way along the line at corner i: the mitre's at an inner one, the shared plane's or the chord's at an end.</summary>
            public Vec3 Ahead(int i)
            {
                int n = Points.Count;
                if (i == 0)
                    return StartAhead.HasValue ? StartAhead.Value.HeadingXZ() : (Points[1] - Points[0]).HeadingXZ();
                if (i == n - 1)
                    return EndAhead.HasValue ? EndAhead.Value.HeadingXZ() : (Points[n - 1] - Points[n - 2]).HeadingXZ();
                return ((Points[i] - Points[i - 1]).HeadingXZ() + (Points[i + 1] - Points[i]).HeadingXZ()).HeadingXZ();
            }

            /// <summary>
            /// A part of constant section (profile) along the line from s0 to s1, the part of it in this piece: closed at its own ends (closed), not where the piece cuts it; where the run goes on into
            /// the next piece its section there is the shared plane, open. Its texture's v from its own start, or from v0 when given.
            /// </summary>
            public void Along(MeshBuilder mesh, float s0, float s1, IList<(float u, float v)> profile, bool closed = true, float? v0 = null)
            {
                float c0 = Math.Max(0f, s0), c1 = Math.Min(Total, s1);
                if (c1 - c0 < 1e-3f)
                    return;
                bool joinStart = c0 <= 1e-4f && StartAhead.HasValue, joinEnd = c1 >= Total - 1e-4f && EndAhead.HasValue;
                Sweep(mesh, SpanAt(Points, At, c0, c1).ConvertAll(q => q.P), profile, closed, closed && !joinStart && c0 <= s0 + 1e-5f, closed && !joinEnd && c1 >= s1 - 1e-5f, v0 ?? c0 - s0,
                    joinStart ? StartAhead : null, joinEnd ? EndAhead : null);
            }

            /// <summary>The places between which a thing spans from post to post (a purlin, a cap between ribs): each post's corner, and the line's ends where they are not a post (the shared planes).</summary>
            public List<(int Corner, bool Post)> Stops()
            {
                var stops = new List<(int, bool)>();
                bool startPost = PostCorner.Contains(0), endPost = PostCorner.Contains(Points.Count - 1);
                if (!startPost)
                    stops.Add((0, false));
                for (int i = 0; i < Posts.Count; i++)
                {
                    if (PostCorner[i] >= 0)
                        stops.Add((PostCorner[i], true));
                }

                if (!endPost)
                    stops.Add((Points.Count - 1, false));
                return stops;
            }
        }

        /// <summary>
        /// The ways the run goes where this piece meets the one before it and the one after (null at an end of the whole run): the tangent there of the circle through its first (last) three points,
        /// which is what the next piece's own first points give on a curve of the same radius; its end chord with too few points.
        /// </summary>
        private static void JoinAheads(IList<Vec3> line, bool first, bool last, out Vec3? start, out Vec3? end)
        {
            int n = line.Count;
            start = first ? (Vec3?)null : n > 2 ? Tangent(line[1] - line[0], line[2] - line[1], false) : (line[1] - line[0]).HeadingXZ();
            end = last ? (Vec3?)null : n > 2 ? Tangent(line[n - 1] - line[n - 2], line[n - 2] - line[n - 3], true) : (line[n - 1] - line[n - 2]).HeadingXZ();
        }

        /// <summary>The tangent at the end of chord c of the circle through it and the chord other next to it (atEnd: other is the chord before c, c ends the line; else other comes after c).</summary>
        private static Vec3 Tangent(Vec3 c, Vec3 other, bool atEnd)
        {
            Vec3 a = c.HeadingXZ(), b = other.HeadingXZ();
            float lc = c.LengthXZ, lo = other.LengthXZ;
            double turn = atEnd ? Angle(b, a) : Angle(a, b), t = (atEnd ? turn : -turn) * lc / (lc + lo);
            float cos = (float)Math.Cos(t), sin = (float)Math.Sin(t);
            return new Vec3(a.X * cos - a.Z * sin, 0f, a.X * sin + a.Z * cos);
        }

        /// <summary>The angle from the way a to the way b in the ground plane (radians; turning a by it gives b).</summary>
        private static double Angle(Vec3 a, Vec3 b) => Math.Atan2(a.X * b.Z - a.Z * b.X, a.X * b.X + a.Z * b.Z);

        /// <summary>The run's own line for a foundation: its posts left out.</summary>
        private static WallLine RunLine(IList<Vec3> line, bool first, bool last)
        {
            JoinAheads(line, first, last, out Vec3? start, out Vec3? end);
            return new WallLine(new List<Vec3>(line), start, end, first);
        }

        /// <summary>The run's line taken straight from post to post (posts: as PanelPosts gives them along the line), its posts at its corners.</summary>
        private static WallLine PostLine(IList<Vec3> line, List<float> posts, bool first, bool last)
        {
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            var points = new List<Vec3> { line[0] };
            foreach (float s in posts)
            {
                if (s > 1e-3f && s < total - 1e-3f)
                    points.Add(RoadMarkings.PointAt(line, at, s));
            }

            points.Add(line[line.Count - 1]);
            JoinAheads(line, first, last, out Vec3? start, out Vec3? end);
            var w = new WallLine(points, start, end, first);
            int corner = 1;
            foreach (float s in posts)
            {
                if (s <= 1e-3f)
                {
                    w.Posts.Add(Math.Min(0f, s));
                    w.PostCorner.Add(s >= -1e-3f ? 0 : -1);
                }
                else if (s >= total - 1e-3f)
                {
                    w.Posts.Add(w.Total + Math.Max(0f, s - total));
                    w.PostCorner.Add(s <= total + 1e-3f ? points.Count - 1 : -1);
                }
                else
                {
                    w.Posts.Add(w.At[corner]);
                    w.PostCorner.Add(corner++);
                }
            }

            return w;
        }

        /// <summary>A line moved u across (its corners along Across: a wall of an enclosure stands where the ribs over its posts come down), its posts at the same corners.</summary>
        private static WallLine Moved(WallLine f, float u)
        {
            var points = new List<Vec3>(f.Points.Count);
            for (int i = 0; i < f.Points.Count; i++)
                points.Add(f.Points[i] + f.Across(i) * u);
            var w = new WallLine(points, f.StartAhead, f.EndAhead, f.First);
            for (int i = 0; i < f.Posts.Count; i++)
            {
                int c = f.PostCorner[i];
                w.PostCorner.Add(c);
                w.Posts.Add(c >= 0 ? w.At[c] : f.Posts[i] <= 0f ? f.Posts[i] : w.Total + (f.Posts[i] - f.Total));
            }

            return w;
        }

        /// <summary>
        /// A solid spanning from one stop of a line to the next (a purlin, a cap between ribs): its section (u across the line, y up: four corners) at each end, on the side face of the rib at a post
        /// (half RibWidth along the post's way) or on the shared plane at an end of the piece.
        /// </summary>
        private static void Span(MeshBuilder mesh, WallLine w, (int Corner, bool Post) from, (int Corner, bool Post) to, IList<(float u, float v)> section)
        {
            var c = new Vec3[8];
            for (int e = 0; e < 2; e++)
            {
                (int corner, bool post) = e == 0 ? from : to;
                Vec3 p = w.Points[corner] + w.Ahead(corner) * (post ? (e == 0 ? 0.5f : -0.5f) * RibWidth : 0f), across = w.Across(corner);
                for (int k = 0; k < 4; k++)
                    c[e * 4 + k] = p + across * section[k].u + Up * section[k].v;
            }

            Hexa(mesh, c);
        }

        /// <summary>
        /// An H post along a path in the section (its axis on the path, its flanges along the line): from its base plate (plate thick over baseTop, a stiffener on each flange and four anchor nuts) each
        /// length of the path a piece mitred to the next, and an end plate where the path ends unless open (a rib goes on from it).
        /// </summary>
        private static void HPost(MeshBuilder steel, MeshBuilder bolts, Vec3 p, Vec3 ahead, SectionPath path, HSection h, float baseTop, float plate, bool open)
        {
            Vec3 a = ahead.HeadingXZ(), r = a.RightXZ();
            float plateAlong = h.B + 0.12f, plateAcross = h.D + 0.16f, bottom = baseTop + plate;
            Block(steel, p, a, plateAcross, plateAlong, baseTop, bottom);
            foreach (float sa in new[] { -1f, 1f })
            {
                foreach (float su in new[] { -1f, 1f })
                {
                    Vec3 q = p + a * (sa * (h.B * 0.5f + 0.035f)) + r * (su * (h.D * 0.5f + 0.05f));
                    Solids.Tube(bolts, q + Up * bottom, q + Up * (bottom + 0.025f), 0.015f, 0.015f, 6, true, true);
                }

                Vec3 flange = p + r * (sa * h.D * 0.5f);
                Solids.Prism(steel, flange + Up * bottom, flange + r * (sa * 0.07f) + Up * bottom, flange + Up * (bottom + 0.12f), a * 0.005f);
            }

            Vec3 X(float along, (float u, float y) q) => p + a * along + r * q.u + Up * q.y;
            var section = new[] { (-h.B * 0.5f, h.B * 0.5f, h.D * 0.5f - h.Tf, h.D * 0.5f), (-h.B * 0.5f, h.B * 0.5f, -h.D * 0.5f, -h.D * 0.5f + h.Tf), (-h.Tw * 0.5f, h.Tw * 0.5f, -h.D * 0.5f + h.Tf, h.D * 0.5f - h.Tf) };
            for (int i = 0; i + 1 < path.Count; i++)
            {
                float s0 = path.At(i), s1 = path.At(i + 1);
                foreach ((float a0, float a1, float o0, float o1) in section)
                    Hexa(steel, new[] { X(a0, path.Point(s0, o0)), X(a1, path.Point(s0, o0)), X(a1, path.Point(s0, o1)), X(a0, path.Point(s0, o1)), X(a0, path.Point(s1, o0)), X(a1, path.Point(s1, o0)), X(a1, path.Point(s1, o1)), X(a0, path.Point(s1, o1)) });
            }

            if (open)
                return;
            float end = path.Length, hb = h.B * 0.5f + 0.005f, hd = h.D * 0.5f + 0.005f;
            Hexa(steel, new[] { X(-hb, path.Point(end, -hd)), X(hb, path.Point(end, -hd)), X(hb, path.Point(end, hd)), X(-hb, path.Point(end, hd)),
                X(-hb, path.Point(end + 0.006f, -hd)), X(hb, path.Point(end + 0.006f, -hd)), X(hb, path.Point(end + 0.006f, hd)), X(-hb, path.Point(end + 0.006f, hd)) });
        }

        /// <summary>A post of level 1 (BarrierLod): its base plate and one box for each length of the path, the post's width and depth, with no flanges, web, stiffeners or bolts.</summary>
        private static void PlainPost(MeshBuilder steel, Vec3 p, Vec3 ahead, SectionPath path, HSection h, float baseTop, float plate)
        {
            Vec3 a = ahead.HeadingXZ(), r = a.RightXZ();
            Block(steel, p, a, h.D + 0.16f, h.B + 0.12f, baseTop, baseTop + plate);
            Vec3 X(float along, (float u, float y) q) => p + a * along + r * q.u + Up * q.y;
            float hb = h.B * 0.5f, hd = h.D * 0.5f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                float s0 = path.At(i), s1 = path.At(i + 1);
                Hexa(steel, new[] { X(-hb, path.Point(s0, -hd)), X(hb, path.Point(s0, -hd)), X(hb, path.Point(s0, hd)), X(-hb, path.Point(s0, hd)),
                    X(-hb, path.Point(s1, -hd)), X(hb, path.Point(s1, -hd)), X(hb, path.Point(s1, hd)), X(-hb, path.Point(s1, hd)) });
            }
        }

        /// <summary>
        /// The panels of one bay (a0 to a1 along the wall's line, between the webs of its posts): along the path a metal module (thick, its face toward k a sheet 1 mm in front with the face's detail,
        /// its texture's u from the module's foot, up to faceTop: not under the cap strip) or a run of glass (a PC sheet in aluminium frames 50 mm high at its top and its bottom) on each length;
        /// mitred where the path bends.
        /// </summary>
        private static void NoiseBay(NoiseParts parts, WallLine w, float a0, float a1, SectionPath path, bool[] glass, float k, float thick, float module, float faceTop, int level)
        {
            float face = k * (thick * 0.5f + 0.001f);
            for (int m = 0; m < glass.Length;)
            {
                float s0 = m * module;
                if (!glass[m])
                {
                    // From level 2 the modules of a run are one strip, with no louvre sheet on it: from far a wall is its colour, not its joints.
                    int run = m + 1;
                    while (level >= BarrierLod.Coarse && run < glass.Length && !glass[run])
                        run++;
                    w.Along(parts.Panel.Mesh, a0, a1, path.Strip(s0, run * module, -thick * 0.5f, thick * 0.5f));
                    if (level < BarrierLod.Coarse)
                        w.Along(parts.Face.Mesh, a0, a1, new List<(float u, float v)> { path.Point(s0, face), path.Point(Math.Min(s0 + module, faceTop), face) }, false);
                    m = run;
                    continue;
                }

                int n = m + 1;
                while (n < glass.Length && glass[n] && path.LengthOf((n + 0.5f) * module) == path.LengthOf((m + 0.5f) * module))
                    n++;
                float s1 = n * module;
                const float frame = 0.05f;
                if (level == BarrierLod.Full)
                {
                    w.Along(parts.Frame.Mesh, a0, a1, path.Strip(s0, s0 + frame, -thick * 0.5f, thick * 0.5f));
                    w.Along(parts.Frame.Mesh, a0, a1, path.Strip(s1 - frame, s1, -thick * 0.5f, thick * 0.5f));
                    w.Along(parts.Glass.Mesh, a0, a1, path.Strip(s0 + frame, s1 - frame, -SheetThick * 0.5f, SheetThick * 0.5f));
                }
                else
                {
                    w.Along(parts.Glass.Mesh, a0, a1, path.Strip(s0, s1, -SheetThick * 0.5f, SheetThick * 0.5f));
                }

                m = n;
            }
        }

        /// <summary>How far a wall's cap strip (a channel over the top module, 3 mm proud of its faces) reaches down it (m).</summary>
        private const float CapDepth = 0.03f;

        /// <summary>
        /// A wall of panels along its line: H posts at its posts, in every bay the panels (NoiseBay) 17 mm short of each web, a cap strip over the top module between the flanges unless the path goes on
        /// into a roof (capped false), and a rubber strip under the bottom one between the base plates.
        /// </summary>
        private static void NoiseWall(NoiseParts parts, WallLine w, SectionPath path, HSection h, bool[] glass, float k, float baseTop, float plate, bool capped, bool open, float thick, float module, int level)
        {
            // From level 2 the panels are one run along the whole line, with no posts, no rubber strip and no cap: what is seen from far (BarrierLod).
            if (level >= BarrierLod.Coarse)
            {
                NoiseBay(parts, w, 0f, w.Total, path, glass, k, thick, module, path.Length, level);
                return;
            }

            foreach (float s in w.Posts)
            {
                if (!OwnPost(s, w.Total, w.First))
                    continue;
                if (level == BarrierLod.Full)
                    HPost(parts.Steel.Mesh, parts.Bolts.Mesh, w.Point(s), AheadAt(w.Points, w.At, s), path, h, baseTop, plate, open);
                else
                    PlainPost(parts.Steel.Mesh, w.Point(s), AheadAt(w.Points, w.At, s), path, h, baseTop, plate);
            }

            float clear = h.Tw * 0.5f + 0.017f, plateAlong = h.B + 0.12f;
            for (int i = 0; i + 1 < w.Posts.Count; i++)
            {
                float a0 = w.Posts[i] + clear, a1 = w.Posts[i + 1] - clear;
                if (a1 - a0 < 0.15f)
                    continue;
                NoiseBay(parts, w, a0, a1, path, glass, k, thick, module, capped ? path.Length - CapDepth : path.Length, level);
                if (level != BarrierLod.Full)
                    continue;
                w.Along(parts.Rubber.Mesh, w.Posts[i] + plateAlong * 0.5f, w.Posts[i + 1] - plateAlong * 0.5f, Rect(-0.03f, 0.03f, baseTop, baseTop + plate));
                if (capped)
                {
                    float o = thick * 0.5f + 0.003f, end = path.Length;
                    w.Along(parts.Cap.Mesh, w.Posts[i] + h.B * 0.5f + 0.002f, w.Posts[i + 1] - h.B * 0.5f - 0.002f, path.Strip(end - CapDepth, end + 0.01f, -o, o));
                }
            }
        }

        /// <summary>The plate an H post stands on (m).</summary>
        private const float BasePlate = 0.02f;

        /// <summary>
        /// A metal noise barrier (金属声屏障) by a road or on a bridge: on its foundation (style.Base) H posts every style.Spacing (2.0 m for 1960 mm panels, 2.5 m for 2460 mm) carrying modules of
        /// sound-absorbing panel 500 mm high and 80 mm thick, their louvred face toward the traffic (side: +1 the traffic is on the line's right), a band of them glass (style.Band), a cap strip on top; a
        /// cranked or an arched top leans over the traffic (style.Top). Suppliers' sizes and drawings (REPORT.md); the foundations, the plates and the strips are estimated.
        /// </summary>
        public static List<MarkingShape> NoiseBarrier(IList<Vec3> line, NoiseStyle style, int side, float phase, bool first, bool last)
        {
            var parts = new NoiseParts(style.Panel, DetailKind.Louvre, style.Glass, style.Post, style.Level);
            List<MarkingShape> shapes = parts.All;
            if (line == null || line.Count < 2)
                return shapes;
            line = Thinned(line, style.Level);
            float k = side >= 0 ? 1f : -1f, height = Modules(style.Height, NoiseModule, 3, 12) * NoiseModule;
            float baseTop = NoiseFoundation(parts.Concrete.Mesh, RunLine(line, first, last), style.Base, style.BaseHeight, 0f, k);
            SectionPath path = WallPath(0f, baseTop + BasePlate, height, style.Top, k);
            int modules = (int)Math.Round(path.Length / NoiseModule);
            WallLine w = PanelLine(line, style.Level, style.Spacing, 3f, phase, first, last);
            NoiseWall(parts, w, path, HFor(path.Length), GlassModules(modules, (int)Math.Round(height / NoiseModule), style.Band), k, baseTop, BasePlate, true, false, PanelThick, NoiseModule, style.Level);
            return shapes;
        }

        /// <summary>The line a wall of a level is drawn along: as it is up to level 1, thinned for level 2, where no post or rib is drawn and a ring of vertices every 2 m on a straight stretch is waste (BarrierLod.Simplify).</summary>
        private static IList<Vec3> Thinned(IList<Vec3> line, int level) => level >= BarrierLod.Coarse ? BarrierLod.Simplify(line, BarrierLod.Tolerance) : line;

        /// <summary>The line the panels of a wall are swept along: taken straight from post to post up to level 1 (PostLine), and for level 2, where no post is drawn, the run's own line (RunLine), which has a corner where the road bends and not at every post.</summary>
        private static WallLine PanelLine(IList<Vec3> line, int level, float spacing, float most, float phase, bool first, bool last) =>
            level >= BarrierLod.Coarse ? RunLine(line, first, last) : PostLine(line, Posts(line, spacing, most, phase, last), first, last);

        /// <summary>The posts of a piece every spacing (1.5 m at least, most at most) along the whole run (PanelPosts).</summary>
        private static List<float> Posts(IList<Vec3> line, float spacing, float most, float phase, bool last)
        {
            var at = new List<float>();
            return PanelPosts(RoadMarkings.Lengths(line, at), Math.Max(1.5f, Math.Min(most, spacing)), phase, last);
        }

        /// <summary>A height as whole modules (at least least, at most most).</summary>
        private static int Modules(float height, float module, int least, int most) => Math.Max(least, Math.Min(most, (int)Math.Round(height / module)));

        /// <summary>
        /// An enclosure's box ribs: their depth (m, across their path) and their width (along the line), estimated from photos; their outer face RibProud out from the wall posts' outer flanges, and
        /// their foot RibFoot down over the posts' tops (the rib holds the post's end, no face of one on a face of the other). The roof's sheet passes through them SheetIn under their outer face.
        /// </summary>
        private const float RibDepth = 0.3f, RibWidth = 0.16f, RibProud = 0.002f, RibFoot = 0.02f, SheetIn = 0.006f;

        /// <summary>
        /// A noise barrier that covers the road (0.12.49), on bridges' crash walls by default (style.Base). Semi (半封闭): the wall of NoiseBarrier on the line, its posts (HW150) going on as box ribs
        /// over the traffic (side +1: on the line's right): a bend of 2 m radius to 75 degrees and straight to reach metres across from the line, an edge beam along their tips. Full (全封闭): the line is
        /// the middle of the road, the two walls span metres apart facing in, the ribs one arch from wall to wall: bends of 2.5 m radius to 65 degrees and an arc through the crown. Over the ribs a PC
        /// sheet (style.Glass) held in them, purlins between them every 1.5 m or so, a cap where the roof meets each wall. Everything spans straight from rib to rib (the line taken from post to post: a
        /// far wall stands on that line moved across, where the ribs come down). The ribs' shape and sizes are estimated from photos and a supplier's drawing (REPORT.md); the walls are the metal panels
        /// of NoiseBarrier.
        /// </summary>
        public static List<MarkingShape> NoiseCoverBarrier(IList<Vec3> line, NoiseCover cover, NoiseStyle style, float reach, float span, int side, float phase, bool first, bool last)
        {
            var parts = new NoiseParts(style.Panel, DetailKind.Louvre, style.Glass, style.Post, style.Level);
            List<MarkingShape> shapes = parts.All;
            if (line == null || line.Count < 2)
                return shapes;
            line = Thinned(line, style.Level);
            float height = Modules(style.Height, NoiseModule, 6, 10) * NoiseModule;
            int upright = (int)Math.Round(height / NoiseModule);
            bool[] glass = GlassModules(upright, upright, style.Band);
            var h = new HSection(0.15f, 0.15f, 0.007f, 0.010f);
            WallLine run = RunLine(line, first, last), f = PanelLine(line, style.Level, style.Spacing, 3f, phase, first, last);
            var walls = new List<(WallLine Line, float K)>();
            SectionPath rib;
            float outer, baseTop = 0f;
            if (cover == NoiseCover.Full)
            {
                float a = Math.Max(4f, Math.Min(15f, span * 0.5f));
                foreach (float k in new[] { 1f, -1f })
                {
                    baseTop = NoiseFoundation(parts.Concrete.Mesh, run, style.Base, style.BaseHeight, -k * a, k);
                    walls.Add((Moved(f, -k * a), k));
                }

                rib = ArchPath(a, baseTop + BasePlate + height, style.Level);
                outer = -(h.D * 0.5f + RibProud);
            }
            else
            {
                float k = side >= 0 ? 1f : -1f;
                baseTop = NoiseFoundation(parts.Concrete.Mesh, run, style.Base, style.BaseHeight, 0f, k);
                walls.Add((f, k));
                rib = ArmPath(baseTop + BasePlate + height, Math.Max(2.5f, Math.Min(8f, reach)), k, style.Level);
                outer = -k * (h.D * 0.5f + RibProud);
            }

            SectionPath wallPath = WallPath(0f, baseTop + BasePlate, height, NoiseTop.Upright, 1f);
            foreach ((WallLine w, float k) in walls)
            {
                NoiseWall(parts, w, wallPath, h, glass, k, baseTop, BasePlate, false, true, PanelThick, NoiseModule, style.Level);
                if (style.Level != BarrierLod.Full)
                    continue;
                // Where the roof meets the wall: a cap over the top module from its inner face to the posts' outer flanges, under the roof's sheet, from rib to rib.
                float inside = k * (PanelThick * 0.5f + 0.003f), outside = -k * h.D * 0.5f, end = wallPath.Length;
                List<(float u, float v)> cap = wallPath.Strip(end - CapDepth, end + 0.01f, Math.Min(inside, outside), Math.Max(inside, outside));
                List<(int Corner, bool Post)> stops = w.Stops();
                for (int i = 0; i + 1 < stops.Count; i++)
                    Span(parts.Panel.Mesh, w, stops[i], stops[i + 1], cap);
            }

            float inner = outer + Math.Sign(-outer) * RibDepth, sheet = outer + Math.Sign(-outer) * SheetIn;
            float o0 = Math.Min(outer, inner), o1 = Math.Max(outer, inner);
            // The ribs are not drawn from level 2: from far the roof's sheet is the roof (BarrierLod).
            for (int i = 0; style.Level < BarrierLod.Coarse && i < f.Posts.Count; i++)
            {
                if (f.PostCorner[i] >= 0 && OwnPost(f.Posts[i], f.Total, first))
                    Rib(parts.Steel.Mesh, f.Points[f.PostCorner[i]], f.Ahead(f.PostCorner[i]), f.Across(f.PostCorner[i]), rib, o0, o1);
            }

            var roof = new List<(float u, float v)>();
            for (int i = 0; i < rib.Count; i++)
                roof.Add(rib.Point(rib.At(i), sheet));
            f.Along(parts.Glass.Mesh, 0f, f.Total, roof, false);
            float purlinIn = sheet + Math.Sign(-outer) * 0.004f, purlinOut = purlinIn + Math.Sign(-outer) * 0.1f;
            int purlins = style.Level == BarrierLod.Full ? Math.Max(2, (int)Math.Round(rib.Length / 1.5f)) : 0;
            List<(int Corner, bool Post)> ribs = f.Stops();
            for (int j = 1; j < purlins; j++)
            {
                float s = rib.Length * j / purlins;
                List<(float u, float v)> section = rib.Strip(s - 0.04f, s + 0.04f, Math.Min(purlinIn, purlinOut), Math.Max(purlinIn, purlinOut));
                for (int i = 0; i + 1 < ribs.Count; i++)
                    Span(parts.Steel.Mesh, f, ribs[i], ribs[i + 1], section);
            }

            // The semi-enclosure's edge beam along the ribs' tips, as deep as they are.
            if (cover == NoiseCover.Semi && style.Level < BarrierLod.Coarse)
                f.Along(parts.Steel.Mesh, 0f, f.Total, rib.Strip(rib.Length, rib.Length + 0.2f, o0, o1));
            return shapes;
        }

        /// <summary>A box rib along a path in the section between the offsets o0 and o1, RibWidth along ahead, each length mitred to the next, square at its ends; its section's u measured along across.</summary>
        private static void Rib(MeshBuilder steel, Vec3 p, Vec3 ahead, Vec3 across, SectionPath path, float o0, float o1)
        {
            Vec3 a = ahead.HeadingXZ();
            Vec3 X(float along, (float u, float y) q) => p + a * along + across * q.u + Up * q.y;
            float h = RibWidth * 0.5f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                float s0 = path.At(i), s1 = path.At(i + 1);
                Hexa(steel, new[] { X(-h, path.Point(s0, o0)), X(h, path.Point(s0, o0)), X(h, path.Point(s0, o1)), X(-h, path.Point(s0, o1)), X(-h, path.Point(s1, o0)), X(h, path.Point(s1, o0)), X(h, path.Point(s1, o1)), X(-h, path.Point(s1, o1)) });
            }
        }

        /// <summary>An arc of a path: centre (cu, cy), radius r, from angle a0 to a1 (radians, from +u toward +y), in lengths of at most step metres; its first point left out (the path is there).</summary>
        private static void ArcPoints(List<(float u, float y)> points, float cu, float cy, float r, double a0, double a1, float step, bool even = false)
        {
            int n = Math.Max(1, (int)Math.Ceiling(r * Math.Abs(a1 - a0) / step));
            if (even && n % 2 == 1)
                n++;
            for (int i = 1; i <= n; i++)
            {
                double t = a0 + (a1 - a0) * i / n;
                points.Add((cu + r * (float)Math.Cos(t), cy + r * (float)Math.Sin(t)));
            }
        }

        /// <summary>
        /// The ribs of a full enclosure from the left wall's top (-a, knee) to the right one's (each RibFoot down over its post): a bend of 2.5 m radius from upright to 65 degrees, an arc through the
        /// crown tangent to both bends and the right bend down to the right wall's top (lengths of at most 0.5 m in the bends and 1.5 m in the arc).
        /// </summary>
        private static SectionPath ArchPath(float a, float knee, int level)
        {
            // The lengths of the bends and of the crown by level (BarrierLod): from level 1 the crown is in an even number of lengths, so that its top is a point of the path and the roof is as high as it was.
            float bend = level == BarrierLod.Full ? 0.5f : level == BarrierLod.Reduced ? 1f : 2.5f, crown = level == BarrierLod.Full ? 1.5f : level == BarrierLod.Reduced ? 3f : 6f;
            const float r = 2.5f;
            double theta = 65.0 * Math.PI / 180.0;
            float big = (a - r * (1f - (float)Math.Cos(theta))) / (float)Math.Cos(theta), centre = knee + r * (float)Math.Sin(theta) - big * (float)Math.Sin(theta);
            var points = new List<(float u, float y)> { (-a, knee - RibFoot), (-a, knee) };
            ArcPoints(points, -a + r, knee, r, Math.PI, Math.PI - theta, bend);
            ArcPoints(points, 0f, centre, big, Math.PI - theta, theta, crown, level != BarrierLod.Full);
            ArcPoints(points, a - r, knee, r, theta, 0.0, bend);
            points.Add((a, knee - RibFoot));
            return new SectionPath(points);
        }

        /// <summary>The ribs of a semi-enclosure from the wall's top (0, knee): a bend of 2 m radius toward k to 75 degrees from upright, then straight to reach across from the line.</summary>
        private static SectionPath ArmPath(float knee, float reach, float k, int level)
        {
            const float r = 2f;
            double theta = 75.0 * Math.PI / 180.0;
            var points = new List<(float u, float y)> { (0f, knee - RibFoot), (0f, knee) };
            var bend = new List<(float u, float y)>();
            ArcPoints(bend, r, knee, r, Math.PI, Math.PI - theta, level == BarrierLod.Full ? 0.5f : level == BarrierLod.Reduced ? 1f : 2.5f);
            foreach ((float u, float y) in bend)
                points.Add((k * u, y));
            (float eu, float ey) = points[points.Count - 1];
            float straight = (reach - Math.Abs(eu)) / (float)Math.Sin(theta);
            points.Add((eu + k * straight * (float)Math.Sin(theta), ey + straight * (float)Math.Cos(theta)));
            return new SectionPath(points);
        }

        /// <summary>A brick sound wall's concrete coping (its height, m), its pilasters (one and a half bricks square: 370 mm) and its wall (one brick thick: 240 mm).</summary>
        private const float Coping = 0.08f, Pilaster = 0.37f, BrickWall = 0.24f;

        /// <summary>
        /// A sound wall that is not of metal panels (0.12.49). Cement (水泥声屏障): HW150 posts every style.Spacing (a supplier's 4 m) on the foundation, cement panels 600 mm high and 100 mm thick
        /// between them (a supplier's 4000 x 600 x 100 mm), their perforated face toward the traffic (side), a band of PC sheet as asked, a galvanised cap on top. Brick (砖墙组合式, after a patent's
        /// drawing): a brick wall (bricks: red or grey) one brick thick on a concrete footing, pilasters one and a half bricks square at the posts every style.Spacing, a concrete coping over both, and on
        /// it the posts and the metal panels of NoiseBarrier, whole modules to style.Height over the ground (the brick wall about lower high, made up to them). The cement panels' slots and the brick
        /// wall's sizes are estimated from photos.
        /// </summary>
        public static List<MarkingShape> SoundWallBarrier(IList<Vec3> line, SoundWall kind, NoiseStyle style, float lower, SolidColour bricks, int side, float phase, bool first, bool last)
        {
            bool cement = kind == SoundWall.Cement;
            var parts = new NoiseParts(cement ? SolidColour.Concrete : style.Panel, cement ? DetailKind.CementSlots : DetailKind.Louvre, style.Glass, style.Post, style.Level);
            var brick = new MarkingShape { Solid = bricks == SolidColour.GreyBrick ? SolidColour.GreyBrick : SolidColour.Brick };
            if (cement)
                parts.Cap = parts.Bolts;
            List<MarkingShape> shapes = parts.All;
            shapes.Add(brick);
            if (line == null || line.Count < 2)
                return shapes;
            line = Thinned(line, style.Level);
            float k = side >= 0 ? 1f : -1f;
            WallLine w = PanelLine(line, style.Level, style.Spacing, 4f, phase, first, last);
            if (cement)
            {
                float baseTop = NoiseFoundation(parts.Concrete.Mesh, RunLine(line, first, last), style.Base, style.BaseHeight, 0f, k);
                int modules = Modules(style.Height, DetailSurfaces.CementModule, 3, 6);
                SectionPath path = WallPath(0f, baseTop + BasePlate, modules * DetailSurfaces.CementModule, NoiseTop.Upright, k);
                NoiseWall(parts, w, path, new HSection(0.15f, 0.15f, 0.007f, 0.010f), GlassModules(modules, modules, style.Band), k, baseTop, BasePlate, true, false, 0.1f, DetailSurfaces.CementModule, style.Level);
                return shapes;
            }

            const float footing = 0.1f;
            int upper = Modules(style.Height - Math.Max(0.6f, Math.Min(1.8f, lower)) - Coping - BasePlate, NoiseModule, 2, 8);
            lower = Math.Max(0.6f, Math.Min(1.8f, style.Height - upper * NoiseModule - Coping - BasePlate));
            w.Along(parts.Concrete.Mesh, 0f, w.Total, new List<(float u, float v)> { (-0.25f, -Embed), (0.25f, -Embed), (0.25f, footing), (-0.25f, footing) });
            w.Along(parts.Concrete.Mesh, 0f, w.Total, new List<(float u, float v)> { (-0.2f, lower), (0.2f, lower), (0.2f, lower + Coping - 0.02f), (0.17f, lower + Coping), (-0.17f, lower + Coping), (-0.2f, lower + Coping - 0.02f) });
            foreach (float s in w.Posts)
            {
                if (style.Level < BarrierLod.Coarse && OwnPost(s, w.Total, first))
                    Block(brick.Mesh, w.Point(s), AheadAt(w.Points, w.At, s), Pilaster, Pilaster, footing, lower);
            }

            // The wall one body from its start to its end, through the pilasters; its texture's v the distance along the whole run, so that the courses go on in step from piece to piece.
            w.Along(brick.Mesh, 0f, w.Total, Rect(-BrickWall * 0.5f, BrickWall * 0.5f, footing, lower), true, phase);
            float top = lower + Coping;
            SectionPath panels = WallPath(0f, top + BasePlate, upper * NoiseModule, NoiseTop.Upright, k);
            NoiseWall(parts, w, panels, HFor(panels.Length), GlassModules(upper, upper, style.Band), k, top, BasePlate, true, false, PanelThick, NoiseModule, style.Level);
            return shapes;
        }

        /// <summary>
        /// The planting bed of a planted noise barrier (m): its inside across, the height of its walls over the ground and their thickness, the soil's depth under their tops, and how far apart its
        /// plants are along it. The bed is as wide as the game's smallest shrubs (GreenBushCity01 and 02: 1.40 m across, scratch/research-plants) need; the rest is estimated from photos of planted
        /// barriers (REPORT.md).
        /// </summary>
        private const float BedInside = 1.5f, BedWall = 0.12f, BedHigh = 0.5f, BedSoil = 0.05f, BedPitch = 1.6f;

        /// <summary>
        /// A planted noise barrier (绿化声屏障: a planting bed at the foot of a metal noise barrier, after photos of planted barriers by city roads, REPORT.md): the posts and metal panels of NoiseBarrier
        /// on a concrete plinth (HW125 every style.Spacing), and against the plinth on one side (bedSide: +1 the line's right) a concrete bed BedInside wide with the game's own shrubs in its soil every
        /// BedPitch (props of Kind "planter", made by PlantSystem from its copies of the game's bushes); its louvred face toward the traffic (side). No plant is modelled. A wall planted up its face (a living
        /// wall) is not made: the game's smallest shrubs are 1.40 m across and 0.97 m high, too big for its troughs.
        /// </summary>
        public static List<MarkingShape> GreenNoiseBarrier(IList<Vec3> line, NoiseStyle style, int side, int bedSide, float phase, bool first, bool last, List<PropAnchor> props)
        {
            var parts = new NoiseParts(style.Panel, DetailKind.Louvre, style.Glass, style.Post, style.Level);
            var soil = new MarkingShape { Solid = SolidColour.Soil };
            List<MarkingShape> shapes = parts.All;
            shapes.Add(soil);
            if (line == null || line.Count < 2)
                return shapes;
            line = Thinned(line, style.Level);
            float k = side >= 0 ? 1f : -1f, b = bedSide >= 0 ? 1f : -1f, height = Modules(style.Height, NoiseModule, 4, 8) * NoiseModule;
            var h = new HSection(0.125f, 0.125f, 0.0065f, 0.009f);
            WallLine run = RunLine(line, first, last);
            float baseTop = NoiseFoundation(parts.Concrete.Mesh, run, NoiseBase.Plinth, 0f, 0f, k);
            SectionPath path = WallPath(0f, baseTop + BasePlate, height, NoiseTop.Upright, k);
            WallLine w = PanelLine(line, style.Level, style.Spacing, 2.5f, phase, first, last);
            NoiseWall(parts, w, path, h, new bool[(int)Math.Round(height / NoiseModule)], k, baseTop, BasePlate, true, false, PanelThick, NoiseModule, style.Level);
            // The bed against the plinth's side (0.25 m off the line): its back and front walls along the run, its ends at the ends of the whole run, the soil between them.
            const float plinthHalf = 0.25f;
            float back = plinthHalf, front = plinthHalf + BedWall + BedInside;
            run.Along(parts.Concrete.Mesh, 0f, run.Total, Section(b, (back, -Embed), (back + BedWall, -Embed), (back + BedWall, BedHigh), (back, BedHigh)));
            run.Along(parts.Concrete.Mesh, 0f, run.Total, Section(b, (front, -Embed), (front + BedWall, -Embed), (front + BedWall, BedHigh), (front, BedHigh)));
            float s0 = first ? BedWall : 0f, s1 = last ? run.Total - BedWall : run.Total;
            run.Along(soil.Mesh, s0, s1, Section(b, (back + BedWall, -Embed), (front, -Embed), (front, BedHigh - BedSoil), (back + BedWall, BedHigh - BedSoil)));
            List<(float u, float v)> end = Section(b, (back + BedWall, -Embed), (front, -Embed), (front, BedHigh), (back + BedWall, BedHigh));
            if (first)
                run.Along(parts.Concrete.Mesh, 0f, BedWall, end);
            if (last)
                run.Along(parts.Concrete.Mesh, run.Total - BedWall, run.Total, end);
            if (props == null)
                return shapes;
            // The plants down the bed's middle, every BedPitch along the whole run from half a pitch in. PlantSystem picks a plant up to three times its room across (Choose), so the room is a third of
            // the bed: what it picks reaches no further from the middle than half the bed.
            float middle = back + BedWall + BedInside * 0.5f;
            foreach (float s in MarkingStuds.Stations(run.Total, BedPitch, phase + BedPitch * 0.5f))
            {
                if ((first && s < BedPitch * 0.4f) || (last && s > run.Total - BedPitch * 0.4f))
                    continue;
                Vec3 p = run.Point(s), ahead = HeadingAt(run.Points, run.At, s);
                props.Add(new PropAnchor { Local = p + ahead.RightXZ() * (b * middle) + Up * (BedHigh - BedSoil), Yaw = (float)Math.Atan2(ahead.X, ahead.Z), Kind = "planter", Room = BedInside / 3f });
            }

            return shapes;
        }

        /// <summary>A section's corners given on the side +u, put on the side k.</summary>
        private static List<(float u, float v)> Section(float k, params (float u, float v)[] corners)
        {
            var section = new List<(float u, float v)>(corners.Length);
            foreach ((float u, float v) in corners)
                section.Add((k * u, v));
            return section;
        }
    }
}
