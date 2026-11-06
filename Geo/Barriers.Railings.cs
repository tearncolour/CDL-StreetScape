using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The colours of a crowd barrier (铁马): galvanised, or banded black and yellow (road works) or red and white.</summary>
    public enum CrowdScheme
    {
        Galvanised,
        BlackYellow,
        RedWhite
    }

    /// <summary>
    /// More railings and fences (0.12.34; the owner: "增加更多栏杆样式"), from scratch/research-railings/REPORT.md: a size is a clause's where a standard sets it, else a supplier's stock size or a tender's,
    /// else an estimate from photos, and each builder says which. Pure.
    /// </summary>
    public static partial class Barriers
    {
        /// <summary>A point s along the line, or beyond its ends along its first or last chord (a thing of this piece of a run that reaches into the next).</summary>
        private static Vec3 PointBeyond(IList<Vec3> line, IList<float> at, float s)
        {
            int n = line.Count;
            if (s < 0f)
                return line[0] + (line[1] - line[0]).HeadingXZ() * s;
            if (s > at[n - 1])
                return line[n - 1] + (line[n - 1] - line[n - 2]).HeadingXZ() * (s - at[n - 1]);
            return RoadMarkings.PointAt(line, at, s);
        }

        /// <summary>The points of the line from s0 to s1 (its ends there and its corners between) with their distances along it.</summary>
        private static List<(Vec3 P, float S)> SpanAt(IList<Vec3> line, IList<float> at, float s0, float s1)
        {
            var points = new List<(Vec3 P, float S)> { (PointBeyond(line, at, s0), s0) };
            for (int i = 1; i + 1 < line.Count; i++)
            {
                if (at[i] > s0 + 1e-4f && at[i] < s1 - 1e-4f)
                    points.Add((line[i], at[i]));
            }

            points.Add((PointBeyond(line, at, s1), s1));
            return points;
        }

        /// <summary>
        /// A flat upright sheet along the line from s0 to s1, from y0 to y1 over it, thick across: its two faces (a wire mesh, drawn cut out), u its height over y0 and v the distance along the whole run
        /// (phase + s), so that the wires go on in step from piece to piece.
        /// </summary>
        private static void Sheet(MeshBuilder mesh, IList<Vec3> line, IList<float> at, float s0, float s1, float y0, float y1, float thick, float phase)
        {
            List<(Vec3 P, float S)> points = SpanAt(line, at, s0, s1);
            float h = y1 - y0;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                (Vec3 a, float sa) = points[i];
                (Vec3 b, float sb) = points[i + 1];
                if (sb - sa < 1e-4f)
                    continue;
                Vec3 r = (b - a).HeadingXZ().RightXZ();
                foreach (float k in new[] { 1f, -1f })
                {
                    Vec3 o = r * (k * thick * 0.5f);
                    Quad(mesh, a + o + Up * y0, b + o + Up * y0, b + o + Up * y1, a + o + Up * y1, r * k, 0f, phase + sa, 0f, phase + sb, h, phase + sb, h, phase + sa);
                }
            }
        }

        private static readonly int[][] s_PyramidFaces = { new[] { 0, 1, 2, 3 }, new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 0, 4 } };

        /// <summary>A pyramid (a post's cap, a spear's tip): its base width across and length along at y over p, its tip rise over that.</summary>
        private static void Pyramid(MeshBuilder mesh, Vec3 p, Vec3 ahead, float width, float length, float y, float rise)
        {
            Vec3 a = ahead.HeadingXZ(), r = a.RightXZ();
            Vec3 x = r * (width * 0.5f), z = a * (length * 0.5f), b = p + Up * y;
            Solids.Convex(mesh, new[] { b - x - z, b + x - z, b + x + z, b - x + z, p + Up * (y + rise) }, s_PyramidFaces);
        }

        /// <summary>A sphere of radius r: slices round it, stacks from pole to pole, smooth; texture coordinates in metres round it and up it.</summary>
        private static void Sphere(MeshBuilder mesh, Vec3 centre, float r, int slices, int stacks)
        {
            var rows = new int[stacks + 1, slices + 1];
            for (int j = 0; j <= stacks; j++)
            {
                double lat = -Math.PI / 2 + Math.PI * j / stacks;
                for (int i = 0; i <= slices; i++)
                {
                    double lon = 2.0 * Math.PI * i / slices;
                    var n = new Vec3((float)(Math.Cos(lat) * Math.Cos(lon)), (float)Math.Sin(lat), (float)(Math.Cos(lat) * Math.Sin(lon)));
                    rows[j, i] = mesh.Vertex(centre + n * r, n, (float)lon * r, (float)(lat + Math.PI / 2) * r);
                }
            }

            for (int j = 0; j < stacks; j++)
            {
                for (int i = 0; i < slices; i++)
                {
                    int a = rows[j, i], b = rows[j, i + 1], c = rows[j + 1, i + 1], d = rows[j + 1, i];
                    Vec3 facing = mesh.Positions[a] + mesh.Positions[b] + mesh.Positions[c] + mesh.Positions[d] - centre * 4f;
                    if (j == 0)
                        mesh.TriFacing(a, c, d, facing);
                    else if (j == stacks - 1)
                        mesh.TriFacing(a, b, c, facing);
                    else
                        mesh.QuadFacing(a, b, c, d, facing);
                }
            }
        }

        /// <summary>
        /// The Beijing-style railing (京式护栏, the inverted-U kind; in Shenzhen 乙型, its bases on the pavement: SJG 189-2025 12.9.3): square-tube posts 60 x 80 mm every 3 m on loose cast bases 400 x 300
        /// x 180 mm, a channel 40 x 60 mm low between them and on it inverted U's of 16 mm round bar, their legs 180 mm apart and 70 mm between two U's (eleven to a panel), joined by small plates; a yellow
        /// reflective strip high on each post. Suppliers' stock sizes (no standard sets them); it comes down at a pedestrians' gap (taper, GB 50688 7.6.1-1).
        /// </summary>
        public static List<MarkingShape> BeijingFence(IList<Vec3> line, float height, SolidColour colour, SolidColour baseColour, GapTaper taper, float phase, float whole, bool first, bool last)
        {
            var metal = new MarkingShape { Solid = colour };
            var bases = new MarkingShape { Solid = baseColour, Detail = baseColour == SolidColour.Concrete ? DetailKind.Concrete : DetailKind.Painted };
            var film = new MarkingShape { Solid = SolidColour.Yellow, Detail = DetailKind.Reflective };
            var shapes = new List<MarkingShape> { metal, bases, film };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.6f, Math.Min(1.3f, height));
            const float spacing = 3f, baseTop = 0.18f, channelY = 0.2f, channel = 0.06f, leg = 0.09f, pitch = 0.25f, r = 0.008f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + total;
            float H(float s) => taper.HeightAt(phase + s, whole, height);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                float h = H(s);
                Box(bases.Mesh, p, ahead, 0.4f, 0.3f, -FootSink, baseTop - 0.05f);
                Box(bases.Mesh, p, ahead, 0.26f, 0.18f, baseTop - 0.05f, baseTop);
                Box(metal.Mesh, p, ahead, 0.06f, 0.08f, baseTop, h + 0.03f);
                Box(film.Mesh, p, ahead, 0.064f, 0.03f, h - 0.3f, h - 0.05f);
            }

            Bar(metal.Mesh, line, 0f, channelY, 0.04f, channel, first, last);
            float foot = channelY + channel;
            foreach (float s in MarkingStuds.Stations(total, pitch, phase))
            {
                if (NearStation(s, spacing, phase, 0.1f))
                    continue;
                float h = H(s);
                var arc = new List<Vec3>();
                for (int k = 0; k <= 6; k++)
                {
                    double a = Math.PI * k / 6;
                    arc.Add(PointBeyond(line, at, s - leg * (float)Math.Cos(a)) + Up * (h - leg + leg * (float)Math.Sin(a)));
                }

                Solids.Tube(metal.Mesh, PointBeyond(line, at, s - leg) + Up * foot, arc[0], r, r, 6, false, false);
                for (int k = 0; k < 6; k++)
                    Solids.Tube(metal.Mesh, arc[k], arc[k + 1], r, r, 6, false, false);
                Solids.Tube(metal.Mesh, arc[6], PointBeyond(line, at, s + leg) + Up * foot, r, r, 6, false, false);
                // The plate that joins this U to the next one, half way up.
                if (!NearStation(s + pitch, spacing, phase, 0.1f))
                {
                    float mid = foot + (h - foot) * 0.5f;
                    Box(metal.Mesh, PointBeyond(line, at, s + pitch * 0.5f), HeadingAt(line, at, Math.Max(0f, Math.Min(total, s))), 0.016f, pitch - 2f * leg, mid - 0.025f, mid + 0.025f);
                }
            }

            return shapes;
        }

        /// <summary>
        /// The blue and white zinc-steel railing (蓝白锌钢护栏): square posts 80 mm every 3.08 m with pyramid caps on plastic bases 400 x 300 x 150 mm, a D-shaped top rail 40 x 55 mm whose top is 0.1 m
        /// under the posts' tops, an oval bottom rail 30 x 55 mm at 0.23 m, and flat uprights 30 x 50 mm, eighteen spaces to a panel (171 mm), white above and with blue reflective film below a line that
        /// rises toward the middle of each panel; an orange reflector on both faces of each post. Supplier and tender sizes; the blue's line is estimated from photos. It comes down at a gap (taper).
        /// </summary>
        public static List<MarkingShape> ZincSteelFence(IList<Vec3> line, float height, GapTaper taper, float phase, float whole, bool first, bool last)
        {
            var white = new MarkingShape { Solid = SolidColour.White };
            var blue = new MarkingShape { Solid = SolidColour.Blue, Detail = DetailKind.Reflective };
            var reflector = new MarkingShape { Solid = SolidColour.Orange, Detail = DetailKind.Reflective };
            var bases = new MarkingShape { Solid = SolidColour.Black, Detail = DetailKind.Plastic };
            var shapes = new List<MarkingShape> { white, blue, reflector, bases };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.6f, Math.Min(1.2f, height));
            const float spacing = 3.08f, pitch = spacing / 18f, baseTop = 0.15f, bottom = 0.23f, oval = 0.055f, rail = 0.055f, below = 0.1f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + total;
            float H(float s) => taper.HeightAt(phase + s, whole, height);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                float h = H(s);
                Box(bases.Mesh, p, ahead, 0.4f, 0.3f, -FootSink, baseTop);
                Box(white.Mesh, p, ahead, 0.08f, 0.08f, baseTop, h);
                Pyramid(white.Mesh, p, ahead, 0.09f, 0.09f, h, 0.04f);
                Box(reflector.Mesh, p, ahead, 0.084f, 0.05f, h - 0.25f, h - 0.13f);
            }

            var d = new List<(float u, float v)> { (-0.02f, 0f), (0.02f, 0f), (0.02f, 0.035f), (0.0141f, 0.0491f), (0f, rail), (-0.0141f, 0.0491f), (-0.02f, 0.035f) };
            var top = new List<(float u, float v)>();
            foreach ((float u, float v) in d)
                top.Add((u, v + height - below - rail));
            Sweep(white.Mesh, Raised(line, at, s => H(s) - height), top, true, first, last);
            var ring = new List<(float u, float v)>();
            for (int k = 0; k < 10; k++)
            {
                double a = 2.0 * Math.PI * k / 10;
                ring.Add((0.015f * (float)Math.Cos(a), bottom + oval * 0.5f + oval * 0.5f * (float)Math.Sin(a)));
            }

            Sweep(white.Mesh, line, ring, true, first, last);
            float low = bottom + oval;
            foreach (float s in MarkingStuds.Stations(total, pitch, phase))
            {
                if (NearStation(s, spacing, phase, 0.08f))
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                float high = H(s) - below - rail;
                double frac = ((phase + s) % spacing) / spacing;
                float boundary = low + (high - low) * (0.35f + 0.3f * (float)Math.Sin(Math.PI * frac));
                Box(blue.Mesh, p, ahead, 0.03f, 0.05f, low, boundary);
                Box(white.Mesh, p, ahead, 0.03f, 0.05f, boundary, high);
            }

            return shapes;
        }

        /// <summary>
        /// Crowd barriers (铁马) end to end, 50 mm apart where they are hooked together: panels 2.0 x 1.2 m (or 1.5 x 1.0 m) of 32 mm tube with rounded top corners on two feet 0.6 m long across the line.
        /// Galvanised: a bottom rail at 0.15 m, uprights of 14 mm tube every 150 mm, flat feet. Black and yellow (road works) or red and white: the frame in bands 0.2 m long, a bottom rail a quarter up,
        /// five uprights, bowed feet. A panel is straight from where it starts; the piece of a run it starts in draws it. Suppliers' sizes and photos; no standard sets them.
        /// </summary>
        public static List<MarkingShape> CrowdBarriers(IList<Vec3> line, bool big, CrowdScheme scheme, float phase, bool last)
        {
            bool banded = scheme != CrowdScheme.Galvanised;
            SolidColour first = scheme == CrowdScheme.BlackYellow ? SolidColour.Black : scheme == CrowdScheme.RedWhite ? SolidColour.Red : SolidColour.Steel;
            SolidColour second = scheme == CrowdScheme.BlackYellow ? SolidColour.Yellow : SolidColour.White;
            var a = new MarkingShape { Solid = first };
            var b = new MarkingShape { Solid = second };
            var shapes = new List<MarkingShape> { a, b };
            if (line == null || line.Count < 2)
                return shapes;
            float w = big ? 2f : 1.5f, h = big ? 1.2f : 1f, pitch = w + 0.05f;
            const float r = 0.016f, corner = 0.08f, foot = 0.3f, band = 0.2f;
            float low = banded ? h * 0.25f : 0.15f, feet = banded ? 0.1f : 0.03f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            void Member(Vec3 p, Vec3 q, float radius, bool striped)
            {
                int n = striped ? Math.Max(1, (int)Math.Ceiling((q - p).Length / band)) : 1;
                for (int k = 0; k < n; k++)
                    Solids.Tube((k % 2 == 0 ? a : b).Mesh, Vec3.Lerp(p, q, k / (float)n), Vec3.Lerp(p, q, (k + 1) / (float)n), radius, radius, 8, false, false);
            }

            foreach (float s0 in MarkingStuds.Stations(total, pitch, phase))
            {
                if (s0 >= total - 1e-3f || (last && s0 + w > total + 0.01f))
                    continue;
                Vec3 p0 = PointBeyond(line, at, s0), p1 = PointBeyond(line, at, s0 + w);
                Vec3 along = (p1 - p0).HeadingXZ(), right = along.RightXZ();
                Vec3 P(float x, float y, float across = 0f) => Vec3.Lerp(p0, p1, x / w) + right * across + Up * y;
                var frame = new List<Vec3> { P(0f, feet) };
                for (int k = 0; k <= 4; k++)
                {
                    double t = Math.PI * 0.5 * k / 4;
                    frame.Add(P(corner - corner * (float)Math.Cos(t), h - corner + corner * (float)Math.Sin(t)));
                }

                for (int k = 0; k <= 4; k++)
                {
                    double t = Math.PI * 0.5 * k / 4;
                    frame.Add(P(w - corner + corner * (float)Math.Sin(t), h - corner + corner * (float)Math.Cos(t)));
                }

                frame.Add(P(w, feet));
                for (int k = 0; k + 1 < frame.Count; k++)
                    Member(frame[k], frame[k + 1], r, banded && (frame[k + 1] - frame[k]).Length > band);
                Member(P(0f, low), P(w, low), r, banded);
                int uprights = banded ? 5 : (int)Math.Floor(w / 0.15f) - 1;
                for (int k = 1; k <= uprights; k++)
                {
                    float x = w * k / (uprights + 1);
                    float br = banded ? 0.009f : 0.007f;
                    Solids.Tube(a.Mesh, P(x, low), P(x, h), br, br, 6, false, false);
                }

                foreach (float x in new[] { 0f, w })
                {
                    if (!banded)
                    {
                        Solids.Tube(a.Mesh, P(x, 0.016f, -foot), P(x, 0.016f, foot), r, r, 8, true, true);
                        Solids.Tube(a.Mesh, P(x, 0.016f), P(x, feet), r, r, 8, false, false);
                        continue;
                    }

                    var bow = new List<Vec3>();
                    for (int k = 0; k <= 6; k++)
                    {
                        float across = -foot + 2f * foot * k / 6f, t = across / foot;
                        bow.Add(P(x, 0.016f + (feet - 0.016f) * (1f - t * t), across));
                    }

                    for (int k = 0; k < 6; k++)
                        Solids.Tube(a.Mesh, bow[k], bow[k + 1], r, r, 8, k == 0, k == 5);
                }
            }

            return shapes;
        }

        /// <summary>
        /// A city bridge's railing of stainless composite tube (不锈钢复合管桥梁栏杆) on a concrete plinth 0.3 m wide and 0.2 m high: steel plate posts 120 x 12 mm every 2 m (painted) on base plates, a top
        /// rail of 80 mm tube at the height, a middle rail of 51 mm at 0.55 of it and a bottom one at 0.3 m, uprights of 32 mm tube every 140 mm (a clear gap of 108 mm: no more than 110 mm, CJJ 11-2011
        /// (2019) 9.5.1). The height is from the walking surface: 1.10 m by a footway, 1.40 m by a cycle track (CJJ 11 9.5.1). The parts' sizes are a supplier's.
        /// </summary>
        public static List<MarkingShape> BridgeRailing(IList<Vec3> line, float height, SolidColour postColour, float phase, bool first, bool last)
        {
            var steel = new MarkingShape { Solid = SolidColour.Steel };
            var posts = new MarkingShape { Solid = postColour };
            var concrete = new MarkingShape { Solid = SolidColour.Concrete };
            var shapes = new List<MarkingShape> { steel, posts, concrete };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(1.1f, Math.Min(1.5f, height));
            const float plinth = 0.2f, spacing = 2f, gap = 0.14f, lowY = 0.3f, topR = 0.04f, railR = 0.0254f, barR = 0.0159f;
            Bar(concrete.Mesh, line, 0f, -Embed, 0.3f, plinth + Embed, first, last);
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                Box(posts.Mesh, p, ahead, 0.2f, 0.16f, plinth, plinth + 0.02f);
                Box(posts.Mesh, p, ahead, 0.12f, 0.012f, plinth + 0.02f, height - topR);
            }

            Rail(steel.Mesh, line, 0f, height - topR, topR, first, last);
            Rail(steel.Mesh, line, 0f, height * 0.55f, railR, first, last);
            Rail(steel.Mesh, line, 0f, lowY, railR, first, last);
            foreach (float s in MarkingStuds.Stations(total, gap, phase))
            {
                if (NearStation(s, spacing, phase, 0.03f))
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Solids.Tube(steel.Mesh, p + Up * lowY, p + Up * (height - topR), barR, barR, 8, false, false);
            }

            return shapes;
        }

        /// <summary>
        /// A stone balustrade (石材栏杆: 望柱 and 栏板): a sill 0.35 m wide and 0.15 m high, panels 0.1 m thick between square posts 0.18 m every 1.9 m, each a field 20 mm sunk on both faces inside a
        /// border 80 mm wide (the field's depth estimated from photos), a rounded coping 0.14 m wide on the panels at the height, the posts 1.27 times as high (營造法式: a post 1.3 times the railing's height) with a cap and a pointed head. Heights: 1.10 m on a bridge or by a footway (CJJ 11 9.5.1), 1.05 m in a
        /// park (GB 51192-2016 8.2.2). The parts' sizes are suppliers' and tenders' (posts 180 mm, panels 100 mm, coping 140 mm, sill 350 mm).
        /// </summary>
        public static List<MarkingShape> StoneBalustrade(IList<Vec3> line, float height, SolidColour colour, float phase, bool first, bool last)
        {
            var stone = new MarkingShape { Solid = colour, Detail = DetailKind.Stone };
            var shapes = new List<MarkingShape> { stone };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.8f, Math.Min(1.3f, height));
            const float sill = 0.15f, panel = 0.1f, field = 0.06f, border = 0.08f, coping = 0.08f, post = 0.18f, spacing = 1.9f;
            float under = height - coping;
            Bar(stone.Mesh, line, 0f, -Embed, 0.35f, sill + Embed, first, last);
            Bar(stone.Mesh, line, 0f, sill, panel, border, first, last);
            Bar(stone.Mesh, line, 0f, sill + border, field, under - sill - 2f * border, first, last);
            Bar(stone.Mesh, line, 0f, under - border, panel, border, first, last);
            var d = new List<(float u, float v)> { (-0.07f, 0f), (0.07f, 0f), (0.07f, 0.03f), (0.05f, 0.065f), (0f, coping), (-0.05f, 0.065f), (-0.07f, 0.03f) };
            var top = new List<(float u, float v)>();
            foreach ((float u, float v) in d)
                top.Add((u, v + height - coping));
            Sweep(stone.Mesh, line, top, true, first, last);
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            float postTop = height * 1.27f - 0.14f;
            // A balustrade ends at a post: the run's end gets one however near the last.
            List<float> posts = Stations(total, spacing, phase, last);
            if (last && (posts.Count == 0 || total - posts[posts.Count - 1] > 0.05f))
                posts.Add(total);
            foreach (float s in posts)
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                // The panels' borders beside the post, either side of it.
                foreach (float k in new[] { -1f, 1f })
                {
                    float side = s + k * (post + border) * 0.5f;
                    if (side > 0f && side < total)
                        Box(stone.Mesh, RoadMarkings.PointAt(line, at, side), ahead, panel, border, sill + border, under - border);
                }

                Box(stone.Mesh, p, ahead, post, post, sill, postTop);
                Box(stone.Mesh, p, ahead, post + 0.04f, post + 0.04f, postTop, postTop + 0.04f);
                Box(stone.Mesh, p, ahead, post - 0.02f, post - 0.02f, postTop + 0.04f, postTop + 0.06f);
                Pyramid(stone.Mesh, p, ahead, post - 0.02f, post - 0.02f, postTop + 0.06f, 0.08f);
            }

            return shapes;
        }

        /// <summary>
        /// An expressway's boundary fence (公路隔离栅): welded mesh (WeldedMesh: 150 x 75 mm, wire 4 mm, within GB/T 26941-2025's 75 x 75 to 200 x 75 and 3.5 to 5 mm) in frames of 30 mm tube on round
        /// posts 60 mm every 2.5 m with caps (26941 5.2.6.12: caps are required), from 0.1 m over the ground to the height: at least 1.5 m, 1.8 m near towns (JTG D81-2017 8.1.2), 1.8 m on an urban
        /// expressway (GB 50688 7.7.3). With an arm (cranked 45 degrees, 0.3 m: 26941 5.2.6.2) toward side (+1 the line's right) three strands of barbed wire, the barbs not drawn. No standard sets the
        /// colour.
        /// </summary>
        public static List<MarkingShape> ExpresswayFence(IList<Vec3> line, float height, bool arm, int side, SolidColour colour, float phase, bool first, bool last)
        {
            var frame = new MarkingShape { Solid = colour };
            var mesh = new MarkingShape { Solid = colour, Detail = DetailKind.WeldedMesh };
            var wire = new MarkingShape { Solid = SolidColour.Metal };
            var shapes = new List<MarkingShape> { frame, mesh, wire };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(1.5f, Math.Min(2.5f, height));
            const float spacing = 2.5f, postR = 0.03f, frameR = 0.015f, low = 0.1f, reach = 0.3f;
            float s45 = reach * 0.7071f, toward = side >= 0 ? 1f : -1f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                Solids.Tube(frame.Mesh, p + Up * -Embed, p + Up * height, postR, postR, 10, false, true);
                Solids.Tube(frame.Mesh, p + Up * height, p + Up * (height + 0.02f), postR + 0.004f, postR + 0.004f, 10, false, true);
                foreach (float k in new[] { -1f, 1f })
                {
                    Vec3 q = PointBeyond(line, at, s + k * (postR + frameR + 0.005f));
                    Solids.Tube(frame.Mesh, q + Up * low, q + Up * (height - 0.05f), frameR, frameR, 6, false, false);
                }

                if (arm)
                    Solids.Tube(frame.Mesh, p + Up * (height - 0.05f), p + ahead.RightXZ() * (toward * s45) + Up * (height - 0.05f + s45), 0.02f, 0.02f, 6, false, true);
            }

            Rail(frame.Mesh, line, 0f, low, frameR, first, last);
            Rail(frame.Mesh, line, 0f, height - 0.05f, frameR, first, last);
            Sheet(mesh.Mesh, line, at, 0f, total, low, height - 0.05f, 0.002f, phase);
            if (arm)
            {
                foreach (float f in new[] { 1f / 3f, 2f / 3f, 1f })
                    Rail(wire.Mesh, line, toward * s45 * f, height - 0.05f + s45 * f, 0.0014f, first, last);
            }

            return shapes;
        }

        /// <summary>
        /// An anti-throw screen on a bridge over a railway, an expressway or a busy road (桥梁防落物网): fine welded mesh (FineMesh: 50 x 50 mm, wire 3 mm; GB 50688 7.7.3: no larger than 50 x 100 mm) in
        /// frames of 50 x 40 mm angle, panels 2.5 m long between posts 55 x 50 mm; the height at least 2.0 m from the footway (GB 50688 7.7.3, CJJ 11 9.5.4) or 1.8 m from a highway bridge's parapet (JTG
        /// D81-2017 9.1.2), on the ground or on a concrete parapet wall high (0: none). The parts' sizes are a supplier's; the colour is not verified.
        /// </summary>
        public static List<MarkingShape> AntiThrowScreen(IList<Vec3> line, float height, float wall, SolidColour colour, float phase, bool first, bool last)
        {
            var frame = new MarkingShape { Solid = colour };
            var mesh = new MarkingShape { Solid = colour, Detail = DetailKind.FineMesh };
            var concrete = new MarkingShape { Solid = SolidColour.Concrete };
            var shapes = new List<MarkingShape> { frame, mesh, concrete };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(1.8f, Math.Min(3f, height));
            wall = Math.Max(0f, Math.Min(1.2f, wall));
            const float spacing = 2.5f, angle = 0.05f, angleDeep = 0.04f;
            if (wall > 0f)
                Sweep(concrete.Mesh, line, new List<(float u, float v)> { (-0.2f, -Embed), (0.2f, -Embed), (0.2f, wall - 0.03f), (0.17f, wall), (-0.17f, wall), (-0.2f, wall - 0.03f) }, true, first, last);
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            float foot = wall > 0f ? wall : -Embed, y0 = wall + 0.05f, y1 = wall + height;
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                Box(frame.Mesh, p, ahead, 0.055f, 0.05f, foot, y1);
                foreach (float k in new[] { -1f, 1f })
                    Box(frame.Mesh, PointBeyond(line, at, s + k * 0.05f), ahead, angleDeep, angle, y0, y1 - angle);
            }

            Bar(frame.Mesh, line, 0f, y0 - angle, angleDeep, angle, first, last);
            Bar(frame.Mesh, line, 0f, y1 - angle, angleDeep, angle, first, last);
            Sheet(mesh.Mesh, line, at, 0f, total, y0, y1 - angle, 0.002f, phase);
            return shapes;
        }

        /// <summary>
        /// Stone balls as bollards (隔离石球): granite balls of the diameter on a square plinth 0.7 of it wide and 0.3 of it high, of the same block, spacing metres apart (at least 0.3 m between two).
        /// Suppliers' sizes (25 to 60 cm) and photos; no standard sets them (separating posts stand 1.3 to 1.5 m apart, GB 50688 7.6.2).
        /// </summary>
        public static List<MarkingShape> StoneBalls(IList<Vec3> line, float diameter, float spacing, SolidColour colour, float phase, bool last)
        {
            var stone = new MarkingShape { Solid = colour, Detail = DetailKind.Stone };
            var shapes = new List<MarkingShape> { stone };
            if (line == null || line.Count < 2)
                return shapes;
            diameter = Math.Max(0.25f, Math.Min(0.8f, diameter));
            spacing = Math.Max(diameter + 0.3f, Math.Min(20f, spacing));
            float r = diameter * 0.5f, plinth = diameter * 0.3f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            List<float> places = Stations(total, spacing, phase, last);
            // The ball at the run's end only where it is not crowded onto the one before it.
            if (last && places.Count > 1 && places[places.Count - 1] - places[places.Count - 2] < diameter + 0.3f)
                places.RemoveAt(places.Count - 1);
            foreach (float s in places)
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                Box(stone.Mesh, p, ahead, diameter * 0.7f, diameter * 0.7f, -FootSink, plinth);
                Sphere(stone.Mesh, p + Up * (plinth + r * 0.9f), r, 18, 10);
            }

            return shapes;
        }

        /// <summary>
        /// A railing of cast concrete made to look like logs (仿木栏杆): low, round posts 0.14 m every 2 m and two log rails at 0.45 and 0.85 of the height; or a viewing platform's, round posts 0.2 m every
        /// 1.8 m, a log top rail 0.14 m, square balusters 0.09 m every 0.45 m and a log rail low down. Sizes estimated from photos. A park's protective railing is at least 1.05 m high (GB 51192-2016 8.2.2)
        /// and its gaps no more than 0.11 m where children play (8.2.3): the platform style's are wider.
        /// </summary>
        public static List<MarkingShape> FauxWood(IList<Vec3> line, float height, bool platform, float phase, bool first, bool last)
        {
            var log = new MarkingShape { Solid = SolidColour.Wood, Detail = DetailKind.Wood };
            var shapes = new List<MarkingShape> { log };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.5f, Math.Min(1.3f, height));
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            float spacing = platform ? 1.8f : 2f, postR = platform ? 0.1f : 0.07f;
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Solids.Tube(log.Mesh, p + Up * -Embed, p + Up * (height + (platform ? 0.05f : 0.02f)), postR, postR, 10, false, true);
            }

            if (!platform)
            {
                Rail(log.Mesh, line, 0f, height * 0.45f, 0.045f, first, last);
                Rail(log.Mesh, line, 0f, height * 0.85f, 0.045f, first, last);
                return shapes;
            }

            const float topR = 0.07f, lowR = 0.045f, lowY = 0.15f, baluster = 0.09f, every = 0.45f;
            Rail(log.Mesh, line, 0f, height - topR, topR, first, last);
            Rail(log.Mesh, line, 0f, lowY, lowR, first, last);
            foreach (float s in MarkingStuds.Stations(total, every, phase))
            {
                if (NearStation(s, spacing, phase, 0.15f))
                    continue;
                Box(log.Mesh, RoadMarkings.PointAt(line, at, s), HeadingAt(line, at, s), baluster, baluster, lowY + lowR, height - 2f * topR + 0.01f);
            }

            return shapes;
        }

        /// <summary>
        /// A spear-top palisade along a compound, a school or a park (锌钢围墙护栏): square posts 60 mm every 3 m with caps, three rails 40 x 40 mm, pickets 25 x 25 mm every 130 mm (a clear gap of 105 mm,
        /// within the usual 110) to 0.06 m under the height and spear tips to it. Stock and tender sizes; the posts' size is estimated.
        /// </summary>
        public static List<MarkingShape> Palisade(IList<Vec3> line, float height, SolidColour colour, float phase, bool first, bool last)
        {
            var metal = new MarkingShape { Solid = colour };
            var shapes = new List<MarkingShape> { metal };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(1f, Math.Min(2.2f, height));
            const float spacing = 3f, post = 0.06f, rail = 0.04f, picket = 0.025f, every = 0.13f, tip = 0.06f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                Box(metal.Mesh, p, ahead, post, post, -Embed, height + 0.03f);
                Pyramid(metal.Mesh, p, ahead, post + 0.01f, post + 0.01f, height + 0.03f, 0.04f);
            }

            foreach (float y in new[] { 0.12f, (height - 0.2f) * 0.5f, height - 0.22f })
                Bar(metal.Mesh, line, 0f, y, rail, rail, first, last);
            foreach (float s in MarkingStuds.Stations(total, every, phase))
            {
                if (NearStation(s, spacing, phase, 0.05f))
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                Box(metal.Mesh, p, ahead, picket, picket, 0.08f, height - tip);
                Pyramid(metal.Mesh, p, ahead, 0.035f, 0.035f, height - tip, tip);
            }

            return shapes;
        }

        /// <summary>
        /// The standard edge guard round a street excavation or a shaft (定型化临边防护栏, JGJ 80-2016 4.3.1): posts 50 x 50 mm every 2 m (no more than 2 m) on base plates, the top rail at 1.2 m, a middle
        /// rail half way down to a toe board 0.18 m high, 40 x 40 mm mesh between (GuardMesh); posts and toe board in red and white or black and yellow stripes 0.3 m long (3.0.13). Stock panel sizes; the
        /// mesh's colour is estimated.
        /// </summary>
        public static List<MarkingShape> EdgeGuard(IList<Vec3> line, bool redWhite, float phase, bool first, bool last)
        {
            var a = new MarkingShape { Solid = redWhite ? SolidColour.Red : SolidColour.Black };
            var b = new MarkingShape { Solid = redWhite ? SolidColour.White : SolidColour.Yellow };
            var mesh = new MarkingShape { Solid = redWhite ? SolidColour.Red : SolidColour.Black, Detail = DetailKind.GuardMesh };
            var shapes = new List<MarkingShape> { a, b, mesh };
            if (line == null || line.Count < 2)
                return shapes;
            const float spacing = 2f, top = 1.2f, toe = 0.18f, rail = 0.03f, stripe = 0.3f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                Box(a.Mesh, p, ahead, 0.15f, 0.15f, -0.005f, 0.01f);
                for (int k = 0; k * stripe < top + 0.05f; k++)
                    Box((k % 2 == 0 ? a : b).Mesh, p, ahead, 0.05f, 0.05f, 0.01f + k * stripe, Math.Min(top + 0.05f, 0.01f + (k + 1) * stripe));
            }

            Bar(a.Mesh, line, 0f, top - rail, rail, rail, first, last);
            Bar(a.Mesh, line, 0f, (top + toe) * 0.5f - rail * 0.5f, rail, rail, first, last);
            // The toe board's stripes in step along the whole run: the first one of a piece begins before it when the piece does not start on the pattern.
            for (float s = -(((phase % stripe) + stripe) % stripe); s < total - 1e-3f; s += stripe)
            {
                float s0 = Math.Max(0f, s), s1 = Math.Min(total, s + stripe);
                if (s1 - s0 < 1e-3f)
                    continue;
                int k = (int)Math.Round((phase + s) / stripe);
                Bar((k % 2 == 0 ? a : b).Mesh, SpanAt(line, at, s0, s1).ConvertAll(q => q.P), 0f, 0f, 0.01f, toe, true, true);
            }

            Sheet(mesh.Mesh, line, at, 0f, total, toe, top - rail, 0.002f, phase);
            return shapes;
        }
    }
}
