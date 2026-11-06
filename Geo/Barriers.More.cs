using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The lattice in a classical railing's panels (0.12.49, Barriers.FretRailing).</summary>
    public enum FretPattern
    {
        /// <summary>回纹: two tall frames side by side and a wide one across them, held to the panel's frame by short bars.</summary>
        Fret,

        /// <summary>直棂: upright bars.</summary>
        Bars
    }

    /// <summary>The kinds of water-filled barrier (水马, 0.12.49).</summary>
    public enum WaterStyle
    {
        /// <summary>The plain tapered unit of 0.12.14.</summary>
        Plain,

        /// <summary>三孔水马: three holes through it and three notches in its top.</summary>
        ThreeHole,

        /// <summary>围挡水马: a fence-like unit, a broad foot and a thin panel 1.8 m high.</summary>
        Fence
    }

    /// <summary>
    /// More barrier and railing styles (0.12.49; the owner: "制作更多样式，包括灯箱样式和护栏样式"), from scratch/research-barriers/REPORT.md: a classical railing, a city railing with a ring in each panel
    /// and two more kinds of water-filled barrier (the noise barriers are Barriers.Noise), and the shapes they share. A size is a supplier's stock size or drawing where one was found, else an estimate
    /// from photos, and each builder says which. What runs along a run is drawn along its line and meets the next piece's on the plane they share (WallLine, JoinAheads); what stands at a place (a post,
    /// a ring, a rigid unit) is drawn once, by the piece it starts or stands in. Pure.
    /// </summary>
    public static partial class Barriers
    {
        // ------------------------------------------------------------------ shapes

        /// <summary>
        /// A closed solid of six flat faces: c[0..3] its bottom round it, c[4..7] over them in the same order (a box, a box cut at a slant, a parallelepiped). Each face faces away from the middle; its
        /// texture coordinates are metres (Face): on a side u up it and v along the bottom's edge, as a swept part's (Sweep) are on an upright face.
        /// </summary>
        private static void Hexa(MeshBuilder mesh, IList<Vec3> c)
        {
            Vec3 middle = Vec3.Zero;
            foreach (Vec3 p in c)
                middle += p;
            middle = middle * 0.125f;
            Face(mesh, c[0], c[1], c[2], c[3], middle);
            Face(mesh, c[4], c[5], c[6], c[7], middle);
            for (int i = 0; i < 4; i++)
                Face(mesh, c[i], c[(i + 1) % 4], c[(i + 1) % 4 + 4], c[i + 4], middle);
        }

        /// <summary>A flat face of four corners of a solid, facing away from the solid's middle; its texture coordinates metres from a, u toward d and v toward b.</summary>
        private static void Face(MeshBuilder mesh, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 middle)
        {
            Vec3 n = MeshBuilder.Normalize(MeshBuilder.Cross(c - a, d - b));
            if (Vec3.Dot(n, (a + b + c + d) * 0.25f - middle) < 0f)
                n = n * -1f;
            Vec3 eu = MeshBuilder.Normalize(d - a), ev = MeshBuilder.Normalize(b - a - eu * Vec3.Dot(b - a, eu));
            Quad(mesh, a, b, c, d, n, 0f, 0f, Vec3.Dot(b - a, eu), Vec3.Dot(b - a, ev), Vec3.Dot(c - a, eu), Vec3.Dot(c - a, ev), Vec3.Dot(d - a, eu), Vec3.Dot(d - a, ev));
        }

        /// <summary>An upright box closed on all six sides (Box leaves its bottom off, for what stands in the ground): its middle at p, across to the right of ahead, along it, from y0 to y1 over p.</summary>
        private static void Block(MeshBuilder mesh, Vec3 p, Vec3 ahead, float across, float along, float y0, float y1)
        {
            Vec3 a = ahead.HeadingXZ(), r = a.RightXZ();
            Vec3 x = r * (across * 0.5f), z = a * (along * 0.5f);
            Vec3[] round = { x * -1f - z, x - z, x + z, x * -1f + z };
            var c = new Vec3[8];
            for (int k = 0; k < 4; k++)
            {
                c[k] = p + round[k] + Up * y0;
                c[k + 4] = p + round[k] + Up * y1;
            }

            Hexa(mesh, c);
        }

        /// <summary>The vector v turned by the turn that takes the direction from to the direction to (both unit), about the axis square to both.</summary>
        private static Vec3 Turned(Vec3 v, Vec3 from, Vec3 to)
        {
            Vec3 axis = MeshBuilder.Cross(from, to);
            float sin = axis.Length, cos = Vec3.Dot(from, to);
            if (sin < 1e-6f)
                return v;
            Vec3 k = axis * (1f / sin);
            return v * cos + MeshBuilder.Cross(k, v) * sin + k * (Vec3.Dot(k, v) * (1f - cos));
        }

        /// <summary>
        /// A round pipe of radius r along a path (loop: round to its first point again), its joints mitred: at each corner the two straight lengths meet on the plane that halves the turn, with neither a
        /// gap nor an overlap round the bend; the section is carried along the path without twisting (a loop's twist is shared out over its lengths). Its ends capped where asked, square to it, or open on
        /// the planes of startPlane and endPlane (their normals: where a piece of a run meets the next one, JoinAheads); smooth normals square to each length; texture coordinates in metres round it and
        /// along it.
        /// </summary>
        private static void Pipe(MeshBuilder mesh, IList<Vec3> path, float r, int sides, bool loop, bool capStart, bool capEnd, Vec3? startPlane = null, Vec3? endPlane = null)
        {
            var points = new List<Vec3>();
            foreach (Vec3 p in path)
            {
                if (points.Count == 0 || (p - points[points.Count - 1]).Length > 1e-5f)
                    points.Add(p);
            }

            if (loop && points.Count > 2 && (points[0] - points[points.Count - 1]).Length < 1e-5f)
                points.RemoveAt(points.Count - 1);
            int n = points.Count, lengths = loop ? n : n - 1;
            if (n < 2 || (loop && n < 3))
                return;
            var dirs = new Vec3[lengths];
            for (int i = 0; i < lengths; i++)
                dirs[i] = MeshBuilder.Normalize(points[(i + 1) % n] - points[i]);
            var frames = new Vec3[lengths];
            frames[0] = MeshBuilder.Normalize(MeshBuilder.Cross(Math.Abs(dirs[0].Y) < 0.9f ? Up : new Vec3(1f, 0f, 0f), dirs[0]));
            for (int i = 1; i < lengths; i++)
                frames[i] = Turned(frames[i - 1], dirs[i - 1], dirs[i]);
            if (loop)
            {
                Vec3 back = Turned(frames[lengths - 1], dirs[lengths - 1], dirs[0]);
                float twist = (float)Math.Atan2(Vec3.Dot(MeshBuilder.Cross(frames[0], back), dirs[0]), Vec3.Dot(frames[0], back));
                for (int i = 1; i < lengths; i++)
                {
                    float a = -twist * i / lengths;
                    Vec3 side = MeshBuilder.Cross(dirs[i], frames[i]);
                    frames[i] = frames[i] * (float)Math.Cos(a) + side * (float)Math.Sin(a);
                }
            }

            float along = 0f;
            for (int i = 0; i < lengths; i++)
            {
                Vec3 d = dirs[i], u = frames[i], w = MeshBuilder.Cross(d, u);
                Vec3 a = points[i], b = points[(i + 1) % n];
                // The planes the length ends on: halving the turn at a joint, the given one or square to it at an end of the path.
                Vec3 from = loop || i > 0 ? MeshBuilder.Normalize(dirs[(i + lengths - 1) % lengths] + d) : startPlane ?? d;
                Vec3 to = loop || i + 1 < lengths ? MeshBuilder.Normalize(d + dirs[(i + 1) % lengths]) : endPlane ?? d;
                float length = (b - a).Length;
                var ringA = new int[sides + 1];
                var ringB = new int[sides + 1];
                var radials = new Vec3[sides + 1];
                for (int k = 0; k <= sides; k++)
                {
                    double angle = 2.0 * Math.PI * k / sides;
                    Vec3 o = u * (float)Math.Cos(angle) + w * (float)Math.Sin(angle);
                    radials[k] = o;
                    Vec3 offset = o * r;
                    Vec3 pa = a + offset - d * (Vec3.Dot(offset, from) / Vec3.Dot(d, from));
                    Vec3 pb = b + offset - d * (Vec3.Dot(offset, to) / Vec3.Dot(d, to));
                    float round = (float)angle * r;
                    ringA[k] = mesh.Vertex(pa, o, round, along + Vec3.Dot(pa - a, d));
                    ringB[k] = mesh.Vertex(pb, o, round, along + length + Vec3.Dot(pb - b, d));
                }

                for (int k = 0; k < sides; k++)
                    mesh.QuadFacing(ringA[k], ringA[k + 1], ringB[k + 1], ringB[k], radials[k] + radials[k + 1]);
                if (!loop && i == 0 && capStart)
                    PipeCap(mesh, a, d * -1f, u, w, r, sides);
                if (!loop && i + 1 == lengths && capEnd)
                    PipeCap(mesh, b, d, u, w, r, sides);
                along += length;
            }
        }

        /// <summary>A pipe's flat end: a disc of radius r round centre in the plane of u and w, facing normal.</summary>
        private static void PipeCap(MeshBuilder mesh, Vec3 centre, Vec3 normal, Vec3 u, Vec3 w, float r, int sides)
        {
            int middle = mesh.Vertex(centre, normal, 0f, 0f);
            var ring = new int[sides];
            for (int k = 0; k < sides; k++)
            {
                double angle = 2.0 * Math.PI * k / sides;
                ring[k] = mesh.Vertex(centre + (u * (float)Math.Cos(angle) + w * (float)Math.Sin(angle)) * r, normal, (float)Math.Cos(angle) * r, (float)Math.Sin(angle) * r);
            }

            for (int k = 0; k < sides; k++)
                mesh.TriFacing(middle, ring[k], ring[(k + 1) % sides], normal);
        }

        /// <summary>The way the line runs at s (horizontal): the mitre of the two chords at a corner of the line, so that a post there stands square to both.</summary>
        private static Vec3 AheadAt(IList<Vec3> line, IList<float> at, float s)
        {
            for (int i = 1; i + 1 < line.Count; i++)
            {
                if (Math.Abs(at[i] - s) < 1e-3f)
                    return ((line[i] - line[i - 1]).HeadingXZ() + (line[i + 1] - line[i]).HeadingXZ()).HeadingXZ();
            }

            int k = 1;
            while (k < line.Count - 1 && at[k] < s)
                k++;
            return (line[k] - line[k - 1]).HeadingXZ();
        }

        // ------------------------------------------------------------------ runs of panels

        /// <summary>
        /// The posts of a run of panels round a piece of it (phase: where the piece starts along the whole run): every spacing along the whole run and, at the run's end (last), one at the end itself
        /// (the one within 5 cm of it moved onto it); from the one at or before the piece's start to the one at or after its end, so that the panels across the piece's ends are known.
        /// </summary>
        private static List<float> PanelPosts(float total, float spacing, float phase, bool last)
        {
            var posts = new List<float>();
            double k = Math.Floor(phase / spacing + 1e-6);
            for (int n = 0; n < 4000; n++, k++)
            {
                float s = (float)(k * spacing - phase);
                if (last && s > total - 0.05f)
                {
                    posts.Add(total);
                    break;
                }

                posts.Add(s);
                if (s >= total - 1e-4f)
                    break;
            }

            return posts;
        }

        /// <summary>Whether a post at s is this piece's: one where two pieces meet is the earlier piece's (as MarkingStuds.Stations has it), the run's first is the first piece's.</summary>
        private static bool OwnPost(float s, float total, bool first) => s <= total + 1e-4f && (s > 1e-4f || (first && s > -1e-4f));

        /// <summary>Whether an upright of a panel at s is this piece's: from its start up to its end, the end itself only at the run's end (within 0.1 mm, so that two pieces never both take it).</summary>
        private static bool OwnUpright(float s, float total, bool last) => s >= -1e-4f && (s < total - 1e-4f || (last && s <= total + 1e-4f));

        /// <summary>
        /// A pipe along a figure in the plane of the line: path points (s along the line, y over it), put on the line's points (or beyond its ends along its end chords); the caller draws a figure in the
        /// piece its middle is in.
        /// </summary>
        private static void Figure(MeshBuilder mesh, IList<Vec3> line, IList<float> at, IList<(float s, float y)> path, float r, int sides, bool loop, bool capStart, bool capEnd)
        {
            var points = new List<Vec3>(path.Count);
            foreach ((float s, float y) in path)
                points.Add(PointBeyond(line, at, s) + Up * y);
            Pipe(mesh, points, r, sides, loop, capStart, capEnd);
        }

        /// <summary>An arc of the figure's path: centre (cs, cy), radius rs along and ry up, from angle a0 to a1 (radians), steps lengths.</summary>
        private static void Arc(List<(float s, float y)> path, float cs, float cy, float rs, float ry, double a0, double a1, int steps)
        {
            for (int k = 0; k <= steps; k++)
            {
                double a = a0 + (a1 - a0) * k / steps;
                path.Add((cs + rs * (float)Math.Cos(a), cy + ry * (float)Math.Sin(a)));
            }
        }

        /// <summary>
        /// The bars of a lattice from its centre lines (verticals: x, y0 to y1; horizontals: y, x0 to x1; metres in the panel's plane), bar wide, as rectangles that touch and never overlap: an upright goes
        /// on half a bar past an end that meets a level line, to cover the joint; a level bar is cut where an upright crosses or meets it. An end that meets no line of the lattice (it meets the frame) stays.
        /// </summary>
        private static List<(float x0, float x1, float y0, float y1, bool upright)> LatticeBars(List<(float x, float y0, float y1)> verticals, List<(float y, float x0, float x1)> horizontals, float bar)
        {
            const float eps = 1e-4f;
            float half = bar * 0.5f;
            bool OnLevel(float x, float y) => horizontals.Exists(h => Math.Abs(h.y - y) < eps && x > h.x0 - eps && x < h.x1 + eps);
            bool OnUpright(float x, float y) => verticals.Exists(v => Math.Abs(v.x - x) < eps && y > v.y0 - eps && y < v.y1 + eps);
            var bars = new List<(float x0, float x1, float y0, float y1, bool upright)>();
            var uprights = new List<(float x0, float x1, float y0, float y1)>();
            foreach ((float x, float y0, float y1) in verticals)
            {
                (float x0, float x1, float b0, float b1) u = (x - half, x + half, OnLevel(x, y0) ? y0 - half : y0, OnLevel(x, y1) ? y1 + half : y1);
                uprights.Add(u);
                bars.Add((u.x0, u.x1, u.b0, u.b1, true));
            }

            foreach ((float y, float x0, float x1) in horizontals)
            {
                var pieces = new List<(float a, float b)> { (OnUpright(x0, y) ? x0 - half : x0, OnUpright(x1, y) ? x1 + half : x1) };
                foreach ((float ux0, float ux1, float uy0, float uy1) in uprights)
                {
                    if (uy1 <= y - half + eps || uy0 >= y + half - eps)
                        continue;
                    var next = new List<(float a, float b)>();
                    foreach ((float a, float b) in pieces)
                    {
                        if (ux1 <= a + eps || ux0 >= b - eps)
                        {
                            next.Add((a, b));
                            continue;
                        }

                        if (ux0 > a + eps)
                            next.Add((a, ux0));
                        if (ux1 < b - eps)
                            next.Add((ux1, b));
                    }

                    pieces = next;
                }

                foreach ((float a, float b) in pieces)
                    bars.Add((a, b, y - half, y + half, false));
            }

            return bars;
        }

        // ------------------------------------------------------------------ classical railings

        /// <summary>The bay of a classical railing (m, between two posts' middles); every third post is a main post.</summary>
        public const float FretBay = 1f;

        /// <summary>
        /// An aluminium classical railing (中式仿古栏杆, 铝代木) by a river, in a park or an old street: main posts 120 mm square every three bays (3 m) with a cap plate and a pyramid-topped block over
        /// the height, small posts 50 mm on foot plates between; a handrail 70 x 50 mm from main post to main post with its top at the height; under it in each bay a frame of 40 mm bars (the top one
        /// 60 mm under the handrail, the bottom one 80 mm over the ground) and in it a lattice of bars 25 mm wide and 20 mm deep: the 回纹 of the photos (Fret: two tall frames side by side and a wide one
        /// across them, short bars to the frame, the figure the same turned half round), or upright bars no more than 100 mm apart (Bars, and a bay too narrow for the figure). The figure is measured
        /// from photos, the sizes are estimated from them (REPORT.md). A park's railing is at least 1.05 m high (GB 51192-2016 8.2.2); only the bars keep the 0.11 m gaps of a children's area (8.2.3).
        /// </summary>
        public static List<MarkingShape> FretRailing(IList<Vec3> line, float height, FretPattern pattern, SolidColour colour, float phase, bool first, bool last)
        {
            var metal = new MarkingShape { Solid = colour, Detail = DetailKind.Painted };
            var shapes = new List<MarkingShape> { metal };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.9f, Math.Min(1.3f, height));
            const float main = 0.12f, small = 0.05f, rail = 0.04f, bar = 0.025f, depth = 0.02f, hand = 0.05f, gap = 0.06f, low = 0.08f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            WallLine run = RunLine(line, first, last);
            List<float> posts = PanelPosts(total, FretBay, phase, last), mains = PanelPosts(total, 3f * FretBay, phase, last);
            bool IsMain(float s) => mains.Exists(m => Math.Abs(m - s) < 1e-3f);
            float handBottom = height - hand, frameTop = handBottom - gap, inner0 = low + rail, inner1 = frameTop - rail, postTop = height + 0.1f;
            foreach (float s in posts)
            {
                if (!OwnPost(s, total, first))
                    continue;
                Vec3 p = PointBeyond(line, at, s), ahead = AheadAt(line, at, s);
                if (IsMain(s))
                {
                    Box(metal.Mesh, p, ahead, main, main, -Embed, postTop);
                    Block(metal.Mesh, p, ahead, main + 0.04f, main + 0.04f, postTop, postTop + 0.02f);
                    Block(metal.Mesh, p, ahead, main + 0.01f, main + 0.01f, postTop + 0.02f, postTop + 0.07f);
                    Pyramid(metal.Mesh, p, ahead, main + 0.01f, main + 0.01f, postTop + 0.07f, 0.03f);
                }
                else
                {
                    Block(metal.Mesh, p, ahead, 0.1f, 0.1f, -0.01f, 0.012f);
                    Block(metal.Mesh, p, ahead, small, small, 0.012f, handBottom);
                }
            }

            var handrail = new List<(float u, float v)> { (-0.035f, handBottom), (0.035f, handBottom), (0.035f, height - 0.012f), (0.023f, height), (-0.023f, height), (-0.035f, height - 0.012f) };
            for (int i = 0; i + 1 < mains.Count; i++)
                run.Along(metal.Mesh, mains[i] + main * 0.5f, mains[i + 1] - main * 0.5f, handrail);
            for (int i = 0; i + 1 < posts.Count; i++)
            {
                float x0 = posts[i] + (IsMain(posts[i]) ? main : small) * 0.5f, x1 = posts[i + 1] - (IsMain(posts[i + 1]) ? main : small) * 0.5f;
                if (x1 - x0 < 0.1f)
                    continue;
                run.Along(metal.Mesh, x0, x1, Rect(-rail * 0.5f, rail * 0.5f, low, low + rail));
                run.Along(metal.Mesh, x0, x1, Rect(-rail * 0.5f, rail * 0.5f, frameTop - rail, frameTop));
                foreach ((float b0, float b1, float y0, float y1, bool upright) in Lattice(pattern, x1 - x0, inner1 - inner0, bar))
                {
                    if (!upright)
                    {
                        run.Along(metal.Mesh, x0 + b0, x0 + b1, Rect(-depth * 0.5f, depth * 0.5f, inner0 + y0, inner0 + y1));
                        continue;
                    }

                    float s = x0 + (b0 + b1) * 0.5f;
                    if (OwnUpright(s, total, last))
                        Block(metal.Mesh, PointBeyond(line, at, s), AheadAt(line, at, s), depth, b1 - b0, inner0 + y0, inner0 + y1);
                }
            }

            return shapes;
        }

        /// <summary>
        /// The bars of a bay's lattice w wide and h high inside its frame (metres from its inside's lower corner at the bay's start). Fret: the figure measured from the photos as fractions of the
        /// inside (the tall frames from 0.11 to 0.47 and 0.53 to 0.89 across and 0.14 to 0.86 up, the wide one 0.29 to 0.71 by 0.33 to 0.67, the short bars to the frame at their middles and at 0.34
        /// and 0.56 up the side), with its half-turned copy; Bars, or a bay under 0.6 m: uprights evenly no more than 125 mm apart.
        /// </summary>
        private static List<(float x0, float x1, float y0, float y1, bool upright)> Lattice(FretPattern pattern, float w, float h, float bar)
        {
            if (pattern == FretPattern.Fret && w >= 0.6f)
            {
                var verticals = new List<(float x, float y0, float y1)>();
                var horizontals = new List<(float y, float x0, float x1)>();
                foreach ((float x, float y0, float y1) in new[] { (0.11f, 0.14f, 0.86f), (0.47f, 0.14f, 0.86f), (0.29f, 0.33f, 0.67f), (0.29f, 0.86f, 1f), (0.29f, 0f, 0.14f) })
                {
                    verticals.Add((x * w, y0 * h, y1 * h));
                    verticals.Add(((1f - x) * w, (1f - y1) * h, (1f - y0) * h));
                }

                foreach ((float y, float x0, float x1) in new[] { (0.14f, 0.11f, 0.47f), (0.86f, 0.11f, 0.47f), (0.33f, 0.29f, 0.71f), (0.34f, 0f, 0.11f), (0.56f, 0f, 0.11f) })
                {
                    horizontals.Add((y * h, x0 * w, x1 * w));
                    horizontals.Add(((1f - y) * h, (1f - x1) * w, (1f - x0) * w));
                }

                return LatticeBars(verticals, horizontals, bar);
            }

            var bars = new List<(float x0, float x1, float y0, float y1, bool upright)>();
            int n = Math.Max(1, (int)Math.Ceiling(w / 0.125f) - 1);
            for (int j = 1; j <= n; j++)
            {
                float x = w * j / (n + 1);
                bars.Add((x - bar * 0.5f, x + bar * 0.5f, 0f, h, true));
            }

            return bars;
        }

        // ------------------------------------------------------------------ the ring railing

        /// <summary>
        /// A city railing with a ring in each panel (城市景观护栏, after a supplier's photo: REPORT.md): square posts 100 mm every 3 m with pyramid caps and an orange reflector on each face, on black
        /// plastic bases 400 x 300 x 150 mm; in each panel round top and bottom rails 50 mm from post to post (the top one's top at the height, the bottom one's middle 0.22 m up), uprights 40 mm 0.17 m inside the posts
        /// and between them two rails 32 mm: the upper one up and over an arch round an upright oval ring that stands on a short bar from the bottom rail, the lower one turned down into the bottom rail
        /// either side of it. A panel under 2.2 m has the two rails straight. The figure is measured from the photo and scales with the height; the posts and bases are the zinc-steel railings' stock sizes.
        /// </summary>
        public static List<MarkingShape> RingRailing(IList<Vec3> line, float height, SolidColour colour, float phase, bool first, bool last)
        {
            var frame = new MarkingShape { Solid = colour };
            var reflector = new MarkingShape { Solid = SolidColour.Orange, Detail = DetailKind.Reflective };
            var bases = new MarkingShape { Solid = SolidColour.Black, Detail = DetailKind.Plastic };
            var shapes = new List<MarkingShape> { frame, reflector, bases };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.9f, Math.Min(1.3f, height));
            const float spacing = 3f, post = 0.1f, baseTop = 0.15f, railR = 0.025f, uprightR = 0.02f, r = 0.016f, inset = 0.17f, yLow = 0.22f, lead = 0.05f;
            float yTop = height - railR, span = yTop - yLow, k = span / 0.85f;
            float yUpper = yLow + 0.555f * span, yLower = yLow + 0.32f * span, yRing = yLow + 0.515f * span;
            float arch = 0.21f * k, fillet = 0.05f * k, ringS = 0.125f * k, ringY = 0.17f * k, legs = 0.18f * k, turn = 0.06f * k;
            float yArch = Math.Max(yRing + 0.09f * k, yUpper + fillet);
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            WallLine run = RunLine(line, first, last);
            List<float> posts = PanelPosts(total, spacing, phase, last);
            foreach (float s in posts)
            {
                if (!OwnPost(s, total, first))
                    continue;
                Vec3 p = PointBeyond(line, at, s), ahead = AheadAt(line, at, s);
                Box(bases.Mesh, p, ahead, 0.4f, 0.3f, -FootSink, baseTop);
                Block(frame.Mesh, p, ahead, post, post, baseTop, height + 0.02f);
                Pyramid(frame.Mesh, p, ahead, post + 0.01f, post + 0.01f, height + 0.02f, 0.04f);
                Block(reflector.Mesh, p, ahead, post + 0.004f, 0.05f, height - 0.25f, height - 0.13f);
            }

            for (int i = 0; i + 1 < posts.Count; i++)
            {
                // Each panel's top and bottom rails from post to post, welded to their faces.
                LevelPipe(frame.Mesh, run, posts[i] + post * 0.5f, posts[i + 1] - post * 0.5f, yTop, railR, true, true);
                LevelPipe(frame.Mesh, run, posts[i] + post * 0.5f, posts[i + 1] - post * 0.5f, yLow, railR, true, true);
                float e0 = posts[i] + inset, e1 = posts[i + 1] - inset;
                if (e1 - e0 < 0.3f)
                    continue;
                foreach (float e in new[] { e0, e1 })
                {
                    if (!OwnUpright(e, total, last))
                        continue;
                    Vec3 q = PointBeyond(line, at, e);
                    Solids.Tube(frame.Mesh, q + Up * yLow, q + Up * yTop, uprightR, uprightR, 10, true, true);
                }

                if (posts[i + 1] - posts[i] < 2.2f)
                {
                    LevelPipe(frame.Mesh, run, e0, e1, yUpper, r, true, true);
                    LevelPipe(frame.Mesh, run, e0, e1, yLower, r, true, true);
                    continue;
                }

                // The rails follow the line up to a short straight lead into the figure, which the piece that has its middle draws whole; both are capped where they meet (on a bend the line's chord
                // and the figure's lead are not quite one way: the caps close the seam).
                float c = (posts[i] + posts[i + 1]) * 0.5f;
                LevelPipe(frame.Mesh, run, e0, c - arch - fillet - lead, yUpper, r, true, true);
                LevelPipe(frame.Mesh, run, c + arch + fillet + lead, e1, yUpper, r, true, true);
                LevelPipe(frame.Mesh, run, e0, c - legs - turn - lead, yLower, r, true, true);
                LevelPipe(frame.Mesh, run, c + legs + turn + lead, e1, yLower, r, true, true);
                if (!OwnUpright(c, total, last))
                    continue;
                var over = new List<(float s, float y)> { (c - arch - fillet - lead, yUpper) };
                Arc(over, c - arch - fillet, yUpper + fillet, fillet, fillet, -Math.PI * 0.5, 0.0, 3);
                Arc(over, c, yArch, arch, arch, Math.PI, 0.0, 10);
                Arc(over, c + arch + fillet, yUpper + fillet, fillet, fillet, Math.PI, Math.PI * 1.5, 3);
                over.Add((c + arch + fillet + lead, yUpper));
                Figure(frame.Mesh, line, at, over, r, 8, false, true, true);
                var ring = new List<(float s, float y)>();
                Arc(ring, c, yRing, ringS, ringY, 0.0, Math.PI * 2.0 * 19 / 20, 19);
                Figure(frame.Mesh, line, at, ring, r, 8, true, false, false);
                Figure(frame.Mesh, line, at, new List<(float s, float y)> { (c, yLow), (c, yRing - ringY) }, r, 8, false, true, true);
                foreach (float side in new[] { -1f, 1f })
                {
                    var down = new List<(float s, float y)> { (c + side * (legs + turn + lead), yLower) };
                    Arc(down, c + side * (legs + turn), yLower - turn, turn, turn, Math.PI * 0.5, side < 0f ? 0.0 : Math.PI, 4);
                    down.Add((c + side * legs, yLow));
                    Figure(frame.Mesh, line, at, down, r, 8, false, true, true);
                }
            }

            return shapes;
        }

        /// <summary>
        /// A round pipe of radius r at y over the run's line from s0 to s1, the part of it in this piece, capped at its own ends where asked; where the run goes on into the next piece, open on the plane
        /// the pieces share.
        /// </summary>
        private static void LevelPipe(MeshBuilder mesh, WallLine run, float s0, float s1, float y, float r, bool capStart, bool capEnd)
        {
            float c0 = Math.Max(0f, s0), c1 = Math.Min(run.Total, s1);
            if (c1 - c0 < 1e-3f)
                return;
            bool joinStart = c0 <= 1e-4f && run.StartAhead.HasValue, joinEnd = c1 >= run.Total - 1e-4f && run.EndAhead.HasValue;
            List<Vec3> points = SpanAt(run.Points, run.At, c0, c1).ConvertAll(q => q.P + Up * y);
            Pipe(mesh, points, r, 8, false, capStart && !joinStart && c0 <= s0 + 1e-5f, capEnd && !joinEnd && c1 >= s1 - 1e-5f, joinStart ? run.StartAhead : null, joinEnd ? run.EndAhead : null);
        }

        // ------------------------------------------------------------------ water-filled barriers

        /// <summary>
        /// Water-filled barriers (水马) of a style end to end, 30 mm apart, alternately first and second (the same colour twice for one colour). Plain: the units of 0.12.14 (PlainWater). ThreeHole
        /// (三孔水马): units 1.5 m long and 0.8 m high, 420 mm wide at the foot and 220 mm at the top, three holes 150 x 210 mm through them and three notches 120 mm across in the top, two reflective
        /// patches on each face. Fence (围挡水马): units 1.0 m long and 1.8 m high, a foot 350 mm wide going up to a panel 50 mm thick from 0.62 m, a raised field on each face, a white, red and white
        /// reflective strip at the top and white ones at the sides. A unit of these two is rigid: drawn straight and whole by the piece of the run it starts in. Suppliers' sizes (REPORT.md); the places of
        /// the holes, the notches and the strips are measured from their photos.
        /// </summary>
        public static List<MarkingShape> WaterBarriers(IList<Vec3> line, WaterStyle style, SolidColour first, SolidColour second, float phase, bool last)
        {
            var a = new MarkingShape { Solid = first, Detail = DetailKind.Plastic };
            var b = second == first ? a : new MarkingShape { Solid = second, Detail = DetailKind.Plastic };
            var white = new MarkingShape { Solid = SolidColour.White, Detail = DetailKind.Reflective };
            var red = new MarkingShape { Solid = SolidColour.Red, Detail = DetailKind.Reflective };
            var shapes = new List<MarkingShape> { a };
            if (b != a)
                shapes.Add(b);
            if (style != WaterStyle.Plain)
                shapes.Add(white);
            if (style == WaterStyle.Fence)
                shapes.Add(red);
            if (line == null || line.Count < 2)
                return shapes;
            if (style == WaterStyle.Plain)
            {
                PlainWater(line, a, b, phase);
                return shapes;
            }

            float unit = style == WaterStyle.ThreeHole ? 1.5f : 1f;
            const float gap = 0.03f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            int n = (int)Math.Floor(phase / unit + 1e-4);
            for (float s0 = n * unit - phase; s0 < total - 1e-3f; s0 += unit, n++)
            {
                if (s0 < -1e-3f)
                    continue;
                if (last && s0 + unit - gap > total + 0.01f)
                    break;
                Vec3 p0 = PointBeyond(line, at, s0), p1 = PointBeyond(line, at, s0 + unit - gap);
                MeshBuilder body = (n % 2 == 0 ? a : b).Mesh;
                if (style == WaterStyle.ThreeHole)
                    ThreeHoleUnit(body, white.Mesh, p0, p1);
                else
                    FenceUnit(body, white.Mesh, red.Mesh, p0, p1);
            }

            return shapes;
        }

        /// <summary>
        /// A three-hole unit from p0 to p1 (its length along the ground; its foot FootSink under them): both tapered faces with the holes, the rim round its outline and through the holes (smooth round
        /// the holes and the notches, sharp at the corners), and its reflective patches.
        /// </summary>
        private static void ThreeHoleUnit(MeshBuilder body, MeshBuilder film, Vec3 p0, Vec3 p1)
        {
            const float h = 0.8f, foot = 0.21f, top = 0.11f, notch = 0.06f, holeHalf = 0.075f, holeStraight = 0.03f, holeY = 0.4f;
            float length = Vec3.DistanceXZ(p0, p1), slope = (top - foot) / h;
            Vec3 d = p1 - p0, along = d.HeadingXZ(), right = along.RightXZ();
            float Half(float y) => foot + slope * y;
            Vec3 Surface(float x, float y, float side) => p0 + d * (x / length) + Up * y + right * (side * Half(y));
            // The outline seen from the right, x along and y up: counter-clockwise round the unit (the notches in its top), clockwise round each hole.
            var outer = new List<Point2> { new Point2(0f, -FootSink), new Point2(length, -FootSink), new Point2(length, h) };
            foreach (float f in new[] { 0.75f, 0.5f, 0.25f })
            {
                for (int k = 0; k <= 8; k++)
                {
                    double t = -Math.PI * k / 8;
                    outer.Add(new Point2(f * length + notch * (float)Math.Cos(t), h + notch * (float)Math.Sin(t)));
                }
            }

            outer.Add(new Point2(0f, h));
            var loops = new List<List<Point2>> { outer };
            foreach (float f in new[] { 0.25f, 0.5f, 0.75f })
            {
                var hole = new List<Point2>();
                for (int k = 0; k <= 8; k++)
                {
                    double t = -Math.PI * k / 8;
                    hole.Add(new Point2(f * length + holeHalf * (float)Math.Cos(t), holeY - holeStraight + holeHalf * (float)Math.Sin(t)));
                }

                for (int k = 0; k <= 8; k++)
                {
                    double t = Math.PI - Math.PI * k / 8;
                    hole.Add(new Point2(f * length + holeHalf * (float)Math.Cos(t), holeY + holeStraight + holeHalf * (float)Math.Sin(t)));
                }

                loops.Add(hole);
            }

            var points = new List<Point2>();
            var triangles = new List<int>();
            CutOutline.Triangulate(loops, points, triangles);
            foreach (float side in new[] { 1f, -1f })
            {
                Vec3 normal = MeshBuilder.Normalize(MeshBuilder.Cross(d * (1f / length), Up + right * (side * slope)));
                if (Vec3.Dot(normal, right * side) < 0f)
                    normal = normal * -1f;
                int start = body.VertexCount;
                foreach (Point2 q in points)
                    body.Vertex(Surface(q.X, q.Y, side), normal, q.X, q.Y);
                for (int t = 0; t + 2 < triangles.Count; t += 3)
                    body.TriFacing(start + triangles[t], start + triangles[t + 1], start + triangles[t + 2], normal);
                foreach (float f in new[] { 0.14f, 0.68f })
                {
                    float x0 = f * length, x1 = (f + 0.18f) * length;
                    var corners = new[] { Surface(x0, 0.6f, side), Surface(x1, 0.6f, side), Surface(x1, 0.66f, side), Surface(x0, 0.66f, side) };
                    var patch = new Vec3[8];
                    for (int c = 0; c < 4; c++)
                    {
                        patch[c] = corners[c];
                        patch[c + 4] = corners[c] + normal * 0.002f;
                    }

                    Hexa(film, patch);
                }
            }

            (float x, float y) Smooth((float x, float y) own, (float x, float y) other)
            {
                if (own.x * other.x + own.y * other.y < 0.77f)
                    return own;
                float sx = own.x + other.x, sy = own.y + other.y, l = (float)Math.Sqrt(sx * sx + sy * sy);
                return (sx / l, sy / l);
            }

            Vec3 Out((float x, float y) n) => MeshBuilder.Normalize(along * n.x + Up * n.y);
            foreach (List<Point2> loop in loops)
            {
                int m = loop.Count;
                var normals = new (float x, float y)[m];
                for (int i = 0; i < m; i++)
                {
                    Point2 p = loop[i], q = loop[(i + 1) % m];
                    float dx = q.X - p.X, dy = q.Y - p.Y, l = (float)Math.Sqrt(dx * dx + dy * dy);
                    normals[i] = (dy / l, -dx / l);
                }

                float around = 0f;
                for (int i = 0; i < m; i++)
                {
                    Point2 p = loop[i], q = loop[(i + 1) % m];
                    float edge = (float)Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                    Vec3 np = Out(Smooth(normals[i], normals[(i + m - 1) % m])), nq = Out(Smooth(normals[i], normals[(i + 1) % m]));
                    int fp = body.Vertex(Surface(p.X, p.Y, 1f), np, around, 0f), fq = body.Vertex(Surface(q.X, q.Y, 1f), nq, around + edge, 0f);
                    int bq = body.Vertex(Surface(q.X, q.Y, -1f), nq, around + edge, 2f * Half(q.Y)), bp = body.Vertex(Surface(p.X, p.Y, -1f), np, around, 2f * Half(p.Y));
                    body.QuadFacing(fp, fq, bq, bp, Out(normals[i]));
                    around += edge;
                }
            }
        }

        /// <summary>A fence-like unit from p0 to p1: its foot and its panel swept, a raised field on each face, the reflective strips at its top and its sides.</summary>
        private static void FenceUnit(MeshBuilder body, MeshBuilder white, MeshBuilder red, Vec3 p0, Vec3 p1)
        {
            var unit = new List<Vec3> { p0, p1 };
            Sweep(body, unit, new List<(float u, float v)> { (-0.175f, -FootSink), (0.175f, -FootSink), (0.175f, 0.12f), (0.025f, 0.62f), (-0.025f, 0.62f), (-0.175f, 0.12f) }, true, true, true);
            Sweep(body, unit, Rect(-0.025f, 0.025f, 0.62f, 1.8f), true, true, true);
            Vec3 d = p1 - p0, right = d.HeadingXZ().RightXZ();
            Vec3 X(float x, float y, float u) => p0 + d * x + Up * y + right * u;
            void Slab(MeshBuilder mesh, float x0, float x1, float y0, float y1, float u0, float u1) =>
                Hexa(mesh, new[] { X(x0, y0, u0), X(x1, y0, u0), X(x1, y0, u1), X(x0, y0, u1), X(x0, y1, u0), X(x1, y1, u0), X(x1, y1, u1), X(x0, y1, u1) });
            foreach (float s in new[] { 1f, -1f })
            {
                Slab(body, 0.12f, 0.88f, 0.72f, 1.62f, s * 0.025f, s * 0.031f);
                Slab(white, 0.30f, 0.43f, 1.67f, 1.73f, s * 0.025f, s * 0.027f);
                Slab(red, 0.43f, 0.57f, 1.67f, 1.73f, s * 0.025f, s * 0.027f);
                Slab(white, 0.57f, 0.70f, 1.67f, 1.73f, s * 0.025f, s * 0.027f);
                Slab(white, 0.04f, 0.07f, 0.85f, 1.0f, s * 0.025f, s * 0.027f);
                Slab(white, 0.93f, 0.96f, 0.85f, 1.0f, s * 0.025f, s * 0.027f);
            }
        }
    }
}
