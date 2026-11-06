using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The material of a solid part of a road barrier (MarkingShape.Solid): galvanised steel and concrete are the signs' own materials, the rest painted colours.</summary>
    public enum SolidColour
    {
        Metal = 0,
        Concrete = 1,
        White = 2,
        Blue = 3,
        Yellow = 4,
        Black = 5,
        Orange = 6,
        Red = 7,
        Green = 8,

        /// <summary>The painted and the stainless steel of an electric box (0.12.23, Cabinets).</summary>
        LightGray = 9,
        Steel = 10,

        /// <summary>The soil of a planter, a planter's wood and a railing's dark grey paint (0.12.32, Barriers.City).</summary>
        Soil = 11,
        Wood = 12,
        DarkGray = 13,

        /// <summary>The grey granite of a kerb (0.12.33, Kerbs).</summary>
        Granite = 14,

        /// <summary>
        /// The lamp-post decorations (0.12.49, Signs.LampAds): gold paint (a lantern's caps and tassel) and what glows at night (TsMaterials: no glow by day): a red and a gold lantern's body, warm white.
        /// </summary>
        Gold = 15,
        GlowRed = 16,
        GlowGold = 17,
        GlowWarm = 18,

        /// <summary>The other colours of neon flex (0.12.49, Signs.Neon): cool white, blue, pink and green.</summary>
        GlowWhite = 19,
        GlowBlue = 20,
        GlowPink = 21,
        GlowGreen = 22,

        /// <summary>The coffee-brown powder coat of an aluminium classical railing (0.12.49, Barriers.More: the most usual colour of the 仿古 railings in the photos of scratch/research-barriers).</summary>
        DarkBrown = 23,

        /// <summary>The transparent sheets of a noise barrier (0.12.49, Barriers.Noise; TsMaterials: the game's glass): clear, blue-tinted and green-tinted acrylic or polycarbonate.</summary>
        Glass = 24,
        GlassBlue = 25,
        GlassGreen = 26,

        /// <summary>Red and grey clay bricks (0.12.49, the brick sound wall).</summary>
        Brick = 27,
        GreyBrick = 28,

        /// <summary>
        /// 0.12.57, a support's finish (SupportStyle): RAL 9011 graphite black (the RAL swatch #25282A, ral-farben.de's overview of the RAL classic colours), and the dark and the light grey of painted
        /// poles, estimated from photos (Guangzhou Tianhe #3A3B40, backlit, so a little dark; Jiangsu #B1B3BF on the lit side: scratch/research-finish).
        /// </summary>
        Graphite = 29,

        /// <summary>The road greening (0.12.49, Barriers.Greening): a cast-iron tree grate's dark iron, and the teal green of resin composite grates and plastic planter boxes.</summary>
        CastIron = 30,
        ResinGreen = 31,

        PoleDarkGrey = 32,
        PoleLightGrey = 33,

        /// <summary>
        /// The foliage of a vertical greening wall (0.12.78, DetailKind.Foliage, WallFoliage): the leaf green with blossoms of this colour on it, red, pink, yellow, white, or the four mixed. As a plain solid (the wall's far
        /// level) each is the blossoms' own colour.
        /// </summary>
        BlossomRed = 34,
        BlossomPink = 35,
        BlossomYellow = 36,
        BlossomWhite = 37,
        BlossomMixed = 38
    }

    /// <summary>The solids that are transparent sheets (0.12.49): drawn with the game's glass, no baked detail, seen through in the pictures.</summary>
    public static class SolidGlass
    {
        public static bool Is(SolidColour colour) => colour == SolidColour.Glass || colour == SolidColour.GlassBlue || colour == SolidColour.GlassGreen;

        /// <summary>
        /// The tint of a glass (red, green, blue, 0 to 1) and how much of it shows over what is behind (0.12.106, here so that it is tested: TsMaterials makes the material from it). Clear 0.22, the blue tint
        /// 0.34, the green 0.46: the owner (6 Nov: "绿色时候太亮而且颜色不够深", with a picture of the Photoshop Hue/Saturation he used on it: green range, saturation +5, lightness -44) found the green too light
        /// and not deep, the tint was (0.48, 0.80, 0.62) at 0.34; now it is the same tint taken through that adjustment, at 0.46. Not a colour of glass: white.
        /// </summary>
        public static void Tint(SolidColour colour, out float red, out float green, out float blue, out float alpha)
        {
            switch (colour)
            {
                case SolidColour.Glass:
                    (red, green, blue, alpha) = (0.80f, 0.88f, 0.86f, 0.22f);
                    break;
                case SolidColour.GlassBlue:
                    (red, green, blue, alpha) = (0.42f, 0.66f, 0.86f, 0.34f);
                    break;
                case SolidColour.GlassGreen:
                    (red, green, blue, alpha) = (0.18f, 0.54f, 0.34f, 0.46f);
                    break;
                default:
                    (red, green, blue, alpha) = (1f, 1f, 1f, 1f);
                    break;
            }
        }
    }

    /// <summary>The solids that glow at night (0.12.49): the lanterns' bodies and neon flex; TsMaterials lights them.</summary>
    public static class SolidGlow
    {
        public static readonly SolidColour[] All =
        {
            SolidColour.GlowRed, SolidColour.GlowGold, SolidColour.GlowWarm, SolidColour.GlowWhite, SolidColour.GlowBlue, SolidColour.GlowPink, SolidColour.GlowGreen
        };

        public static bool Is(SolidColour colour) => Array.IndexOf(All, colour) >= 0;
    }

    /// <summary>How a W-beam guardrail ends at the ends of its whole run (0.12.14).</summary>
    public enum BeamEnd
    {
        /// <summary>0.12.19, the default: in a median round at both ends; by the roadside flared where the traffic comes to it and round where it leaves (BeamEnds).</summary>
        Auto = -1,

        None,

        /// <summary>圆头式: the beam turns round the end post in a half circle.</summary>
        Round,

        /// <summary>外展式: the last metres bend away from the traffic.</summary>
        Flare
    }

    /// <summary>The colours of a city median fence: its frame (posts and rails) and its bars.</summary>
    public enum FenceScheme
    {
        White,
        BlueWhite,
        YellowBlack
    }

    /// <summary>
    /// Road barriers along a line (0.12.13, decoration only: the game's traffic does not see them), the line in the item's frame with its heights as a run marking's path (RunPath). Upright parts stand
    /// plumb whatever the line's slope; sections across the line are upright too. Sizes are the usual ones of the Chinese standards and products, quoted by clause in the templates
    /// (MarkingTemplates.Barriers); where a standard leaves a size open the value is this mod's choice and says so. Pure.
    /// </summary>
    public static partial class Barriers
    {
        private static readonly Vec3 Up = new Vec3(0f, 1f, 0f);

        /// <summary>How far posts go into the ground (m), so that a post on a slope or a crowned road does not show its foot.</summary>
        public const float Embed = 0.25f;

        /// <summary>How far a loose thing's foot (a cone's, a water barrier's) reaches under the line (m): no gap under it on a crowned road, little enough not to show.</summary>
        public const float FootSink = 0.08f;

        /// <summary>A place on a line with the line's right (+X of the run's direction) there: the mitre of the two chords that meet at it, as long as makes the sides of a swept section parallel.</summary>
        private struct Frame
        {
            public Vec3 P, Right, Ahead;
        }

        /// <summary>
        /// The frames of a line; startAhead and endAhead (0.12.49, where a piece of a run meets the next one: JoinAheads) put the section at its first and its last point square to that way instead of
        /// the chord, unscaled, so that the two pieces' sections there are one.
        /// </summary>
        private static List<Frame> Frames(IList<Vec3> line, Vec3? startAhead = null, Vec3? endAhead = null)
        {
            var frames = new List<Frame>();
            if (line == null || line.Count < 2)
                return frames;
            for (int i = 0; i < line.Count; i++)
            {
                Vec3? given = i == 0 ? startAhead : i == line.Count - 1 ? endAhead : null;
                if (given.HasValue)
                {
                    Vec3 way = given.Value.HeadingXZ();
                    frames.Add(new Frame { P = line[i], Right = way.RightXZ(), Ahead = way });
                    continue;
                }

                Vec3 before = (i > 0 ? line[i] - line[i - 1] : line[1] - line[0]).HeadingXZ();
                Vec3 after = (i + 1 < line.Count ? line[i + 1] - line[i] : line[i] - line[i - 1]).HeadingXZ();
                Vec3 ahead = (before + after).HeadingXZ();
                Vec3 right = ahead.RightXZ();
                // The mitre: a section at a bend is longer across by 1 / cos(half the turn) so that the parts keep their width along both chords (at most twice, at a hairpin).
                float cos = Math.Max(0.5f, Vec3.Dot(right, before.RightXZ()));
                frames.Add(new Frame { P = line[i], Right = right * (1f / cos), Ahead = ahead });
            }

            return frames;
        }

        /// <summary>
        /// A section swept along the line: profile is (across, up) in metres, across + to the line's right, its corners in order round the section; closed joins the last corner to the first and caps the
        /// ends (capStart, capEnd; a closed profile must be convex for its caps); an open profile is a sheet, drawn on both its sides. Flat faces, a quad for each side of the profile between two frames.
        /// The texture coordinates are metres: u along the profile from its first corner, v along the line from vStart (the distance along the whole run where the line starts, so that the detail of a
        /// run of pieces goes on in step: the W-beam's splices fall at its posts).
        /// </summary>
        public static void Sweep(MeshBuilder mesh, IList<Vec3> line, IList<(float u, float v)> profile, bool closed, bool capStart, bool capEnd, float vStart = 0f, Vec3? startAhead = null, Vec3? endAhead = null)
        {
            List<Frame> frames = Frames(line, startAhead, endAhead);
            if (frames.Count < 2 || profile == null || profile.Count < 2)
                return;
            int edges = closed ? profile.Count : profile.Count - 1;
            var along = new float[frames.Count];
            along[0] = vStart;
            for (int i = 1; i < frames.Count; i++)
                along[i] = along[i - 1] + (frames[i].P - frames[i - 1].P).Length;
            var across = new float[profile.Count + 1];
            for (int j = 1; j <= profile.Count; j++)
            {
                (float pu, float pv) = profile[j - 1];
                (float qu, float qv) = profile[j % profile.Count];
                across[j] = across[j - 1] + (float)Math.Sqrt((qu - pu) * (qu - pu) + (qv - pv) * (qv - pv));
            }

            // Which way round the profile is (the outward side of an edge of a closed one).
            float area = 0f;
            for (int j = 0; j < profile.Count; j++)
            {
                (float u0, float v0) = profile[j];
                (float u1, float v1) = profile[(j + 1) % profile.Count];
                area += u0 * v1 - u1 * v0;
            }

            float outward = area >= 0f ? 1f : -1f;
            for (int i = 0; i + 1 < frames.Count; i++)
            {
                Frame f0 = frames[i], f1 = frames[i + 1];
                Vec3 chordRight = (f1.P - f0.P).HeadingXZ().RightXZ();
                for (int j = 0; j < edges; j++)
                {
                    (float ua, float va) = profile[j];
                    (float ub, float vb) = profile[(j + 1) % profile.Count];
                    Vec3 a0 = f0.P + f0.Right * ua + Up * va, b0 = f0.P + f0.Right * ub + Up * vb, a1 = f1.P + f1.Right * ua + Up * va, b1 = f1.P + f1.Right * ub + Up * vb;
                    // The edge's outward normal in the section (u, v), put in the world with the chord's right.
                    float nu = (vb - va) * outward, nv = -(ub - ua) * outward;
                    Vec3 n = MeshBuilder.Normalize(chordRight * nu + Up * nv);
                    float u0 = across[j], u1 = across[j + 1], s0 = along[i], s1 = along[i + 1];
                    Quad(mesh, a0, b0, b1, a1, n, u0, s0, u1, s0, u1, s1, u0, s1);
                    if (!closed)
                        Quad(mesh, a0, b0, b1, a1, n * -1f, u0, s0, u1, s0, u1, s1, u0, s1);
                }
            }

            if (closed)
            {
                if (capStart)
                    Cap(mesh, frames[0], profile, frames[0].Ahead * -1f);
                if (capEnd)
                    Cap(mesh, frames[frames.Count - 1], profile, frames[frames.Count - 1].Ahead);
            }
        }

        private static void Quad(MeshBuilder mesh, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 normal, float ua, float va, float ub, float vb, float uc, float vc, float ud, float vd)
        {
            int ia = mesh.Vertex(a, normal, ua, va), ib = mesh.Vertex(b, normal, ub, vb), ic = mesh.Vertex(c, normal, uc, vc), id = mesh.Vertex(d, normal, ud, vd);
            mesh.QuadFacing(ia, ib, ic, id, normal);
        }

        /// <summary>An end of a swept part: the profile itself, its texture coordinates its own (across, up).</summary>
        private static void Cap(MeshBuilder mesh, Frame f, IList<(float u, float v)> profile, Vec3 normal)
        {
            int first = mesh.VertexCount;
            foreach ((float u, float v) in profile)
                mesh.Vertex(f.P + f.Right * u + Up * v, normal, u, v);
            for (int k = 1; k + 1 < profile.Count; k++)
                mesh.TriFacing(first, first + k, first + k + 1, normal);
        }

        /// <summary>
        /// An upright box: its middle on the ground at bottom, its width across (along right) and its length along (ahead), from y0 to y1 over bottom; five faces (no bottom). The texture coordinates are
        /// metres: on a side u along it and v its height, on the top u across and v along.
        /// </summary>
        public static void Box(MeshBuilder mesh, Vec3 bottom, Vec3 ahead, float width, float length, float y0, float y1)
        {
            Vec3 a = ahead.HeadingXZ(), r = a.RightXZ();
            Vec3 x = r * (width * 0.5f), z = a * (length * 0.5f);
            Vec3 lo = bottom + Up * y0, hi = bottom + Up * y1;
            Vec3[] c = { x * -1f - z, x - z, x + z, x * -1f + z };
            for (int k = 0; k < 4; k++)
            {
                Vec3 p = c[k], q = c[(k + 1) % 4];
                float side = (q - p).Length;
                Quad(mesh, lo + p, lo + q, hi + q, hi + p, MeshBuilder.Normalize((p + q) * 0.5f), 0f, y0, side, y0, side, y1, 0f, y1);
            }

            Quad(mesh, hi + c[0], hi + c[1], hi + c[2], hi + c[3], Up, 0f, 0f, width, 0f, width, length, 0f, length);
        }

        /// <summary>The places (m along the line, in order) of things every spacing along a piece of a run that starts phase m along the whole line, and at its far end when last.</summary>
        private static List<float> Stations(float length, float spacing, float phase, bool last)
        {
            List<float> at = MarkingStuds.Stations(length, spacing, phase);
            if (last && (at.Count == 0 || length - at[at.Count - 1] > spacing * 0.25f))
                at.Add(length);
            return at;
        }

        private static Vec3 HeadingAt(IList<Vec3> line, IList<float> at, float s)
        {
            int i = 1;
            while (i < line.Count - 1 && at[i] < s)
                i++;
            return (line[i] - line[i - 1]).HeadingXZ();
        }

        // ------------------------------------------------------------------ the barriers

        /// <summary>
        /// The two-wave beam's section (GB/T 31439.1 DB01: 310 mm high, 85 mm deep): across from its back (0) toward the traffic, up from its middle. Corners from the bottom flange up; the waves are
        /// rounded by short straights. The three-wave beam (DB02: 506 mm) has a third wave between them.
        /// </summary>
        public static List<(float u, float v)> BeamProfile(bool threeWave)
        {
            var two = new List<(float u, float v)> { (0.010f, -0.155f), (0.055f, -0.135f), (0.085f, -0.085f), (0.075f, -0.050f), (0.030f, -0.015f), (0.030f, 0.015f), (0.075f, 0.050f), (0.085f, 0.085f), (0.055f, 0.135f), (0.010f, 0.155f) };
            if (!threeWave)
                return two;
            return new List<(float u, float v)>
            {
                (0.010f, -0.253f), (0.055f, -0.233f), (0.085f, -0.180f), (0.070f, -0.140f), (0.030f, -0.105f), (0.040f, -0.065f), (0.085f, -0.025f), (0.085f, 0.025f), (0.040f, 0.065f),
                (0.030f, 0.105f), (0.070f, 0.140f), (0.085f, 0.180f), (0.055f, 0.233f), (0.010f, 0.253f)
            };
        }

        /// <summary>
        /// A W-beam guardrail (波形梁钢护栏): round posts every spacing, a block-out on each (防阻块) and the beam on the traffic side (side: +1 the traffic is on the line's right, -1 on its left), or on both
        /// sides of the posts in a median (both). The beam's middle at 600 mm (two-wave) or 697 mm (three-wave) over the ground, the posts' tops just under the beam's top.
        /// </summary>
        public static List<MarkingShape> WBeam(IList<Vec3> line, bool threeWave, float spacing, int side, bool both, float phase, bool first, bool last) =>
            WBeam(line, threeWave, spacing, side, both, phase, first, last, BeamEnd.None, 0f);

        /// <summary>How long the flare of a flared end is (m) and how far it bends away (m): this mod's estimate of the usual installation, not a clause.</summary>
        public const float FlareLength = 10f, FlareOffset = 1f;

        /// <summary>
        /// As above, with the ends of the whole run (whole: its length, 0 when not known: this piece is all of it): turned round the end post (Round), or the last FlareLength bent FlareOffset away from
        /// the traffic (Flare; not in a median).
        /// </summary>
        public static List<MarkingShape> WBeam(IList<Vec3> line, bool threeWave, float spacing, int side, bool both, float phase, bool first, bool last, BeamEnd ends, float whole) =>
            WBeam(line, threeWave, spacing, side, both, phase, first, last, ends, whole, SolidColour.Metal);

        /// <summary>As above in a colour (0.12.78): Metal is the galvanised guardrail, any other solid a painted one (see the form with each end).</summary>
        public static List<MarkingShape> WBeam(IList<Vec3> line, bool threeWave, float spacing, int side, bool both, float phase, bool first, bool last, BeamEnd ends, float whole, SolidColour paint)
        {
            BeamEnds(ends, side, both, out BeamEnd startEnd, out BeamEnd endEnd);
            return WBeam(line, threeWave, spacing, side, both, phase, first, last, startEnd, endEnd, whole, paint);
        }

        /// <summary>
        /// What each end of a W-beam is: as asked, or by Auto: in a median (both) round at both; by the roadside (right-hand traffic) the end where the traffic comes to it flared and the other round. The
        /// traffic next to the barrier runs with the line when it is on the line's left (side -1: a barrier on the traffic's right) and against it when it is on the line's right. A flare is never in a median.
        /// </summary>
        public static void BeamEnds(BeamEnd ends, int side, bool both, out BeamEnd startEnd, out BeamEnd endEnd)
        {
            if (ends != BeamEnd.Auto)
            {
                startEnd = endEnd = both && ends == BeamEnd.Flare ? BeamEnd.Round : ends;
                return;
            }

            if (both)
            {
                startEnd = endEnd = BeamEnd.Round;
                return;
            }

            bool withLine = side < 0;
            startEnd = withLine ? BeamEnd.Flare : BeamEnd.Round;
            endEnd = withLine ? BeamEnd.Round : BeamEnd.Flare;
        }

        /// <summary>As above, each end its own (startEnd at the start of the whole run, endEnd at its end; Auto is not taken here).</summary>
        public static List<MarkingShape> WBeam(IList<Vec3> line, bool threeWave, float spacing, int side, bool both, float phase, bool first, bool last, BeamEnd startEnd, BeamEnd endEnd, float whole) =>
            WBeam(line, threeWave, spacing, side, both, phase, first, last, startEnd, endEnd, whole, SolidColour.Metal);

        /// <summary>
        /// As above in a colour (0.12.78): Metal is the galvanised guardrail; any other solid is a painted one, the beam with its painted detail (WBeam4Painted, WBeam2Painted) and the posts and their
        /// blocks in the same colour. The round and flared ends are the beam's own sweep, so they take its colour.
        /// </summary>
        public static List<MarkingShape> WBeam(IList<Vec3> line, bool threeWave, float spacing, int side, bool both, float phase, bool first, bool last, BeamEnd startEnd, BeamEnd endEnd, float whole, SolidColour paint)
        {
            var metal = new MarkingShape { Solid = paint };
            // The beam has its splices and bolts baked in (DetailSurfaces.WBeam): one splice every 4 m, at a post, and a bolt at every post.
            bool dense = spacing < 3f;
            var beam = new MarkingShape { Solid = paint, Detail = paint == SolidColour.Metal ? (dense ? DetailKind.WBeam2 : DetailKind.WBeam4) : (dense ? DetailKind.WBeam2Painted : DetailKind.WBeam4Painted) };
            var shapes = new List<MarkingShape> { metal, beam };
            if (line == null || line.Count < 2)
                return shapes;
            float sign0 = side >= 0 ? 1f : -1f;
            if (!both && (startEnd == BeamEnd.Flare || endEnd == BeamEnd.Flare))
                line = Flared(line, phase, whole, -sign0, startEnd == BeamEnd.Flare, endEnd == BeamEnd.Flare);
            float postR = threeWave ? 0.070f : 0.057f, block = 0.18f, centre = threeWave ? 0.697f : 0.600f, half = threeWave ? 0.253f : 0.155f, top = centre + half - 0.02f;
            float sign = sign0;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s), right = ahead.RightXZ();
                Solids.Tube(metal.Mesh, p + Up * -Embed, p + Up * top, postR, postR, 10, false, true);
                foreach (float k in both ? new[] { 1f, -1f } : new[] { sign })
                    Box(metal.Mesh, p + right * (k * (postR + block * 0.5f)), ahead, block, 0.14f, centre - half * 0.55f, centre + half * 0.55f);
            }

            List<(float u, float v)> section = BeamProfile(threeWave);
            foreach (float k in both ? new[] { 1f, -1f } : new[] { sign })
            {
                float back = postR + block;
                var placed = new List<(float u, float v)>(section.Count);
                foreach ((float u, float v) in section)
                    placed.Add((k * (back + u), centre + v));
                Sweep(beam.Mesh, line, placed, false, false, false, phase);
            }

            float roundBack = postR + block;
            if (first && startEnd == BeamEnd.Round)
                RoundEnd(beam.Mesh, line[0], (line[1] - line[0]).HeadingXZ() * -1f, both ? 1f : -sign, roundBack, centre, section);
            if (last && endEnd == BeamEnd.Round)
                RoundEnd(beam.Mesh, line[line.Count - 1], (line[line.Count - 1] - line[line.Count - 2]).HeadingXZ(), both ? -1f : sign, roundBack, centre, section);

            return shapes;
        }

        /// <summary>
        /// A round end (圆头): the beam turned in a half circle round the end post, from the side k (+1 the line's right, -1 its left, of the line's own direction) round the end to the other side, with the
        /// face out. outward is the way the run leaves the post at this end.
        /// </summary>
        private static void RoundEnd(MeshBuilder mesh, Vec3 post, Vec3 outward, float k, float radius, float centre, List<(float u, float v)> section)
        {
            Vec3 right = outward.RightXZ();
            var arc = new List<Vec3>();
            const int steps = 10;
            for (int i = 0; i <= steps; i++)
            {
                double a = Math.PI * i / steps;
                // From the side k (a = 0) round the far side (a = pi / 2) to the other (a = pi).
                arc.Add(post + right * (float)(k * radius * Math.Cos(a)) + outward * (float)(radius * Math.Sin(a)));
            }

            // Going round from +right through outward, the post is on the path's left: the face (u > 0) out is the path's right when k = +1, its left when k = -1.
            var placed = new List<(float u, float v)>(section.Count);
            foreach ((float u, float v) in section)
                placed.Add((k * u, centre + v));
            // A metre into the beam's texture: no splice or post bolt falls on the bend.
            Sweep(mesh, arc, placed, false, false, false, 1f);
        }

        /// <summary>
        /// The line with its first and last FlareLength (of the whole run: phase, the piece's start along it; whole, its length) moved away from the traffic (away: +1 the line's right, -1 its left) by up to
        /// FlareOffset, smoothly (a parabola from the straight run).
        /// </summary>
        public static List<Vec3> Flared(IList<Vec3> line, float phase, float whole, float away) => Flared(line, phase, whole, away, true, true);

        /// <summary>As above, at the start of the whole run (atStart), at its end (atEnd), or both.</summary>
        public static List<Vec3> Flared(IList<Vec3> line, float phase, float whole, float away, bool atStart, bool atEnd)
        {
            var at = new List<float>();
            float length = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + length;
            // Sampled every metre near the ends so that the bend is smooth, and where it ends.
            var points = new List<Vec3>();
            List<float> places = EndPlaces(at, phase, whole, FlareLength);

            var offsets = new List<float>(places.Count);
            foreach (float s in places)
            {
                float g = phase + s, fromStart = atStart ? Math.Max(0f, 1f - g / FlareLength) : 0f, fromEnd = atEnd ? Math.Max(0f, 1f - (whole - g) / FlareLength) : 0f, f = Math.Max(fromStart, fromEnd);
                offsets.Add(FlareOffset * f * f);
            }

            var sampled = new List<Vec3>(places.Count);
            foreach (float s in places)
                sampled.Add(RoadMarkings.PointAt(line, at, s));
            for (int i = 0; i < sampled.Count; i++)
            {
                Vec3 ahead = (i + 1 < sampled.Count ? sampled[i + 1] - sampled[i] : sampled[i] - sampled[i - 1]).HeadingXZ();
                points.Add(sampled[i] + ahead.RightXZ() * (away * offsets[i]));
            }

            return points;
        }

        /// <summary>
        /// A concrete barrier of the New Jersey shape (混凝土护栏, 新泽西型): a 75 mm upright lip, a 55° face to 330 mm and an 84° face to the top; 810 mm high with 610 mm at its foot in a median (both faces),
        /// or one face toward the traffic and an upright back by the roadside. Height may be set higher (the lower faces stay, the upper one goes on). Capped at the ends of the whole run.
        /// </summary>
        public static List<MarkingShape> Concrete(IList<Vec3> line, float height, bool both, int side, bool first, bool last)
        {
            var concrete = new MarkingShape { Solid = SolidColour.Concrete };
            var shapes = new List<MarkingShape> { concrete };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.6f, Math.Min(1.5f, height));
            float foot = 0.305f, lip = 0.075f, knee = 0.33f, kneeHalf = foot - (knee - lip) / (float)Math.Tan(55.0 * Math.PI / 180.0), topHalf = kneeHalf - (height - knee) / (float)Math.Tan(84.0 * Math.PI / 180.0);
            // The face toward the traffic (u > 0 when it is on the right), from the foot up; the other side mirrored (a median) or upright at the top's back.
            var face = new List<(float u, float v)> { (foot, -Embed), (foot, lip), (kneeHalf, knee), (topHalf, height) };
            var profile = new List<(float u, float v)>();
            float sign = side >= 0 ? 1f : -1f;
            if (both)
            {
                foreach ((float u, float v) in face)
                    profile.Add((u, v));
                for (int i = face.Count - 1; i >= 0; i--)
                    profile.Add((-face[i].u, face[i].v));
            }
            else
            {
                // By the roadside: the back upright on the line, the face toward the traffic, the top as wide as a median's.
                foreach ((float u, float v) in face)
                    profile.Add((sign * (u + topHalf), v));
                profile.Add((0f, height));
                profile.Add((0f, -Embed));
            }

            Sweep(concrete.Mesh, line, profile, true, first, last);
            return shapes;
        }

        /// <summary>
        /// A city median fence (城市道路中央隔离栏): square posts every spacing (60 mm) on concrete feet, a bottom and a top rail and upright bars between them every 150 mm (this mod's choice of the usual
        /// products; GB 50688 7.6.1-1 sets a median railing's height, 1.10 m, not its pattern). It comes down at the gaps pedestrians cross (taper, 0.12.32; whole: the length of the whole run).
        /// </summary>
        public static List<MarkingShape> CityFence(IList<Vec3> line, float height, float spacing, FenceScheme scheme, bool feet, float phase, bool last, GapTaper taper = default, float whole = 0f)
        {
            SolidColour frameColour = scheme == FenceScheme.BlueWhite ? SolidColour.Blue : scheme == FenceScheme.YellowBlack ? SolidColour.Yellow : SolidColour.White;
            SolidColour barColour = scheme == FenceScheme.YellowBlack ? SolidColour.Black : SolidColour.White;
            var frame = new MarkingShape { Solid = frameColour };
            var bars = new MarkingShape { Solid = barColour };
            var base_ = new MarkingShape { Solid = SolidColour.Concrete };
            var shapes = new List<MarkingShape> { frame, bars, base_ };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.6f, Math.Min(1.6f, height));
            const float post = 0.06f, rail = 0.04f, bar = 0.02f, barGap = 0.15f, footHeight = 0.12f;
            float bottom = feet ? footHeight + 0.06f : 0.12f, lowRail = bottom, highRail = height - 0.05f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + total;
            float H(float s) => taper.HeightAt(phase + s, whole, height);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                Box(frame.Mesh, p, ahead, post, post, feet ? footHeight : -Embed, H(s));
                if (feet)
                    Box(base_.Mesh, p, ahead, 0.30f, 0.40f, -Embed, footHeight);
            }

            Sweep(frame.Mesh, line, new List<(float u, float v)> { (-rail * 0.5f, lowRail), (rail * 0.5f, lowRail), (rail * 0.5f, lowRail + rail), (-rail * 0.5f, lowRail + rail) }, true, false, false);
            Sweep(frame.Mesh, Raised(line, at, s => H(s) - height), new List<(float u, float v)> { (-rail * 0.5f, highRail), (rail * 0.5f, highRail), (rail * 0.5f, highRail + rail), (-rail * 0.5f, highRail + rail) }, true, false, false);
            foreach (float s in MarkingStuds.Stations(total, barGap, phase))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Box(bars.Mesh, p, HeadingAt(line, at, s), bar, bar, lowRail + rail, H(s) - 0.05f);
            }

            return shapes;
        }

        /// <summary>
        /// Traffic cones (交通锥, GB/T 24720) every spacing along the line: a square black foot, an orange body tapering to the top and two white reflective bands; height 450, 700 or 900 mm, the rest
        /// in proportion to the 700 mm cone's (380 mm foot).
        /// </summary>
        public static List<MarkingShape> Cones(IList<Vec3> line, float height, float spacing, float phase, bool last)
        {
            var orange = new MarkingShape { Solid = SolidColour.Orange, Detail = DetailKind.Plastic };
            var white = new MarkingShape { Solid = SolidColour.White, Detail = DetailKind.Reflective };
            var foot = new MarkingShape { Solid = SolidColour.Black, Detail = DetailKind.Plastic };
            var shapes = new List<MarkingShape> { orange, white, foot };
            if (line == null || line.Count < 2)
                return shapes;
            float k = Math.Max(0.4f, Math.Min(1.0f, height)) / 0.7f;
            float baseH = 0.04f * k, r0 = 0.13f * k, r1 = 0.025f * k, h = 0.7f * k;
            // The body from the foot's top to the tip, in bands: orange, white, orange, white, orange (the bands at about the cone's middle and upper third).
            float[] cuts = { baseH, 0.28f * h, 0.36f * h, 0.56f * h, 0.66f * h, h };
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                // The foot reaches into the road: a cone on a chord of a crowned or bending road would otherwise stand on air at its corners (0.12.14: "路锥比地面微微高一点").
                Box(foot.Mesh, p, ahead, 0.38f * k, 0.38f * k, -FootSink, baseH);
                for (int i = 0; i + 1 < cuts.Length; i++)
                {
                    float y0 = cuts[i], y1 = cuts[i + 1];
                    float ra = r0 + (r1 - r0) * (y0 - baseH) / (h - baseH), rb = r0 + (r1 - r0) * (y1 - baseH) / (h - baseH);
                    Solids.Tube((i % 2 == 0 ? orange : white).Mesh, p + Up * y0, p + Up * y1, ra, rb, 14, false, i + 2 == cuts.Length);
                }
            }

            return shapes;
        }
        // ------------------------------------------------------------------ stage 2 (0.12.14)

        /// <summary>A closed ring of a tube's section (radius r, sides) round the line, for sweeping a cable or a rail.</summary>
        private static List<(float u, float v)> Ring(float r, int sides, float u0 = 0f, float v0 = 0f)
        {
            var ring = new List<(float u, float v)>(sides);
            for (int i = 0; i < sides; i++)
            {
                double a = 2.0 * Math.PI * i / sides;
                ring.Add((u0 + (float)(r * Math.Cos(a)), v0 + (float)(r * Math.Sin(a))));
            }

            return ring;
        }

        /// <summary>
        /// The places (m along a piece of a run) of its points and, near the ends of the whole run (within reach of either), one every metre and one where the reach ends: what a line that changes near its
        /// ends (a flare, cables going down) is sampled at, so that it is straight again exactly where it should be.
        /// </summary>
        private static List<float> EndPlaces(IList<float> at, float phase, float whole, float reach)
        {
            var places = new List<float>();
            for (int i = 0; i < at.Count; i++)
            {
                if (i > 0)
                {
                    for (float s = at[i - 1] + 1f; s < at[i] - 0.05f; s += 1f)
                    {
                        float g = phase + s;
                        if (g < reach || whole - g < reach)
                            places.Add(s);
                    }

                    foreach (float g in new[] { reach, whole - reach })
                    {
                        float s = g - phase;
                        if (s > at[i - 1] + 0.05f && s < at[i] - 0.05f)
                            places.Add(s);
                    }
                }

                places.Add(at[i]);
            }

            places.Sort();
            return places;
        }

        /// <summary>The line at a height over it that falls to low (m) over the last fall metres of the whole run at each end (phase and whole as Flared's): a cable going down to its anchor.</summary>
        private static List<Vec3> Anchored(IList<Vec3> line, float height, float low, float fall, float phase, float whole)
        {
            var at = new List<float>();
            float length = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + length;
            List<float> places = EndPlaces(at, phase, whole, fall);

            var result = new List<Vec3>(places.Count);
            foreach (float s in places)
            {
                float g = phase + s, f = Math.Min(1f, Math.Min(g, whole - g) / fall);
                result.Add(RoadMarkings.PointAt(line, at, s) + Up * (low + (height - low) * Math.Max(0f, f)));
            }

            return result;
        }

        /// <summary>
        /// A cable guardrail (缆索护栏): steel posts every spacing, cables (18 mm) at 100 mm steps from 530 mm up through them, going down to concrete anchors over the last 6 m of the whole run. The sizes
        /// are this mod's estimate of the usual installations, not a clause.
        /// </summary>
        public static List<MarkingShape> Cable(IList<Vec3> line, int cables, float spacing, float phase, bool first, bool last, float whole)
        {
            var metal = new MarkingShape { Solid = SolidColour.Metal };
            var concrete = new MarkingShape { Solid = SolidColour.Concrete };
            var shapes = new List<MarkingShape> { metal, concrete };
            if (line == null || line.Count < 2)
                return shapes;
            cables = Math.Max(3, Math.Min(5, cables));
            const float lowest = 0.53f, step = 0.10f;
            float top = lowest + step * (cables - 1) + 0.12f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + total;
            // The cables come down over the last 6 m, or a quarter of a short run.
            float fall = Math.Min(6f, whole * 0.25f);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                float g = phase + s;
                // No post where the cables have come down to their anchor.
                if (g < fall - 0.5f || whole - g < fall - 0.5f)
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Solids.Tube(metal.Mesh, p + Up * -Embed, p + Up * top, 0.04f, 0.04f, 8, false, true);
            }

            for (int c = 0; c < cables; c++)
                Sweep(metal.Mesh, Anchored(line, lowest + step * c, 0.15f, fall, phase, whole), Ring(0.009f, 6), true, false, false);
            if (first)
                Box(concrete.Mesh, line[0], (line[1] - line[0]).HeadingXZ(), 0.6f, 0.8f, -Embed, 0.25f);
            if (last)
                Box(concrete.Mesh, line[line.Count - 1], (line[line.Count - 1] - line[line.Count - 2]).HeadingXZ(), 0.6f, 0.8f, -Embed, 0.25f);
            return shapes;
        }

        /// <summary>A body made of boxes one over another, each band in its colour (no faces at the same place, so nothing flickers): bands are (top over the ground, colour) from the bottom up.</summary>
        private static void Banded(Dictionary<SolidColour, MarkingShape> shapes, Vec3 p, Vec3 ahead, float width, float length, float bottom, IList<(float top, SolidColour colour)> bands)
        {
            float y = bottom;
            foreach ((float top, SolidColour colour) in bands)
            {
                if (!shapes.TryGetValue(colour, out MarkingShape shape))
                    shapes[colour] = shape = new MarkingShape { Solid = colour };
                if (top > y)
                    Box(shape.Mesh, p, ahead, width, length, y, top);
                y = top;
            }
        }

        private static List<MarkingShape> ShapesOf(Dictionary<SolidColour, MarkingShape> shapes) => new List<MarkingShape>(shapes.Values);

        /// <summary>
        /// Warning posts (示警桩) by a rural road: square posts 150 mm, height over the ground, white with two bands (red or black) at the top, every spacing. Sizes are estimates of the usual ones.
        /// </summary>
        public static List<MarkingShape> WarningPosts(IList<Vec3> line, float height, float spacing, SolidColour band, float phase, bool last)
        {
            var shapes = new Dictionary<SolidColour, MarkingShape>();
            if (line == null || line.Count < 2)
                return new List<MarkingShape> { new MarkingShape { Solid = SolidColour.White } };
            height = Math.Max(0.6f, Math.Min(1.5f, height));
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Banded(shapes, p, HeadingAt(line, at, s), 0.15f, 0.15f, -Embed, new[] { (height - 0.45f, SolidColour.White), (height - 0.30f, band), (height - 0.15f, SolidColour.White), (height, band) });
            }

            return ShapesOf(shapes);
        }

        /// <summary>Warning blocks (示警墩): concrete blocks 400 mm wide, 600 mm along, 600 mm high, in three bands (white, red, white or yellow, black, yellow), every spacing. Sizes are estimates.</summary>
        public static List<MarkingShape> WarningBlocks(IList<Vec3> line, float spacing, bool yellowBlack, float phase, bool last)
        {
            var shapes = new Dictionary<SolidColour, MarkingShape>();
            if (line == null || line.Count < 2)
                return new List<MarkingShape> { new MarkingShape { Solid = SolidColour.White } };
            SolidColour light = yellowBlack ? SolidColour.Yellow : SolidColour.White, dark = yellowBlack ? SolidColour.Black : SolidColour.Red;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Banded(shapes, p, HeadingAt(line, at, s), 0.4f, 0.6f, -FootSink, new[] { (0.2f, light), (0.4f, dark), (0.6f, light) });
            }

            return ShapesOf(shapes);
        }

        /// <summary>
        /// City bollards (路桩): round posts 120 mm, 750 mm high, every spacing: stainless steel with two red reflective bands, or yellow with black bands. Sizes are estimates of the usual products.
        /// </summary>
        public static List<MarkingShape> Bollards(IList<Vec3> line, float spacing, bool yellowBlack, float phase, bool last)
        {
            SolidColour body = yellowBlack ? SolidColour.Yellow : SolidColour.Metal, band = yellowBlack ? SolidColour.Black : SolidColour.Red;
            var bodyShape = new MarkingShape { Solid = body };
            var bandShape = new MarkingShape { Solid = band, Detail = DetailKind.Reflective };
            var shapes = new List<MarkingShape> { bodyShape, bandShape };
            if (line == null || line.Count < 2)
                return shapes;
            const float r = 0.06f, h = 0.75f;
            // Bottom up: body, band, body, band, body, the top capped.
            float[] cuts = { -Embed, 0.50f, 0.56f, 0.62f, 0.68f, h };
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                for (int i = 0; i + 1 < cuts.Length; i++)
                    Solids.Tube((i % 2 == 0 ? bodyShape : bandShape).Mesh, p + Up * cuts[i], p + Up * cuts[i + 1], r, r, 12, false, i + 2 == cuts.Length);
            }

            return shapes;
        }

        /// <summary>
        /// The plain water-filled barriers (水马, WaterStyle.Plain): plastic units 1.5 m long end to end, 800 mm high, 450 mm at the foot and 200 mm at the top, the even ones of the whole run in a and
        /// the odd ones in b. Sizes are estimates of the usual units. Each unit follows the line.
        /// </summary>
        private static void PlainWater(IList<Vec3> line, MarkingShape a, MarkingShape b, float phase)
        {
            const float unit = 1.5f, gap = 0.03f;
            var profile = new List<(float u, float v)> { (-0.225f, -FootSink), (0.225f, -FootSink), (0.225f, 0.15f), (0.10f, 0.80f), (-0.10f, 0.80f), (-0.225f, 0.15f) };
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            // The units along the whole run: unit n from n * unit, its colour by n (so a run of pieces alternates on).
            int n = (int)Math.Floor(phase / unit + 1e-4);
            for (float s0 = n * unit - phase; s0 < total - 0.2f; s0 += unit, n++)
            {
                float from = Math.Max(0f, s0), to = Math.Min(total, s0 + unit - gap);
                if (to - from < 0.2f)
                    continue;
                var piece = new List<Vec3> { RoadMarkings.PointAt(line, at, from), RoadMarkings.PointAt(line, at, (from + to) * 0.5f), RoadMarkings.PointAt(line, at, to) };
                Sweep((n % 2 == 0 ? a : b).Mesh, piece, profile, true, true, true);
            }
        }

        /// <summary>
        /// Crash drums (防撞桶): plastic drums 900 mm across and 950 mm high every spacing, orange with two white bands or yellow with two black bands. Sizes are estimates of the usual drums.
        /// </summary>
        public static List<MarkingShape> CrashDrums(IList<Vec3> line, float spacing, bool yellowBlack, float phase, bool last)
        {
            var body = new MarkingShape { Solid = yellowBlack ? SolidColour.Yellow : SolidColour.Orange, Detail = DetailKind.Plastic };
            var band = new MarkingShape { Solid = yellowBlack ? SolidColour.Black : SolidColour.White, Detail = DetailKind.Reflective };
            var shapes = new List<MarkingShape> { body, band };
            if (line == null || line.Count < 2)
                return shapes;
            const float r = 0.45f;
            float[] cuts = { -FootSink, 0.45f, 0.57f, 0.70f, 0.82f, 0.95f };
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, Math.Max(spacing, 0.95f), phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                for (int i = 0; i + 1 < cuts.Length; i++)
                    Solids.Tube((i % 2 == 0 ? body : band).Mesh, p + Up * cuts[i], p + Up * cuts[i + 1], r, r, 18, false, i + 2 == cuts.Length);
            }

            return shapes;
        }

        /// <summary>
        /// A construction hoarding (施工围挡): panels 60 mm thick from the ground to height, in a colour (blue, white or green), on white square posts (80 mm) every spacing a little higher than the panels.
        /// Sizes are estimates of the usual hoardings.
        /// </summary>
        public static List<MarkingShape> Hoarding(IList<Vec3> line, float height, float spacing, SolidColour panel, float phase, bool last)
        {
            var panels = new MarkingShape { Solid = panel };
            var posts = new MarkingShape { Solid = panel == SolidColour.White ? SolidColour.Metal : SolidColour.White };
            var shapes = new List<MarkingShape> { panels, posts };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(1.5f, Math.Min(3f, height));
            Sweep(panels.Mesh, line, new List<(float u, float v)> { (-0.03f, -FootSink), (0.03f, -FootSink), (0.03f, height), (-0.03f, height) }, true, true, last);
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Box(posts.Mesh, p, HeadingAt(line, at, s), 0.08f, 0.08f, -Embed, height + 0.05f);
            }

            return shapes;
        }

        /// <summary>The turn of an anti-glare panel from square across the road (radians): the panels overlap for a driver looking ahead and leave the view across open. This mod's estimate.</summary>
        public const float GlareAngle = 0.21f;

        /// <summary>
        /// Anti-glare panels (防眩板) on a median barrier (0.12.16, the owner: "中央波状护栏加入一个参数项目，就是绿色的防眩板"): a steel rail along the line at base over the ground, green panels 200 mm wide and 750 mm
        /// high every gap on it, each turned GlareAngle from square across the road; on a W-beam the rail stands on short uprights at the posts (postSpacing; 0 on a concrete barrier, which carries it on its
        /// top). Sizes are the usual products' estimates.
        /// </summary>
        public static List<MarkingShape> GlarePanels(IList<Vec3> line, float base_, float gap, float postSpacing, float phase, bool last)
        {
            var green = new MarkingShape { Solid = SolidColour.Green };
            var metal = new MarkingShape { Solid = SolidColour.Metal };
            var shapes = new List<MarkingShape> { green, metal };
            if (line == null || line.Count < 2)
                return shapes;
            const float rail = 0.05f, height = 0.75f, width = 0.2f, thickness = 0.025f;
            Sweep(metal.Mesh, line, new List<(float u, float v)> { (-rail * 0.5f, base_), (rail * 0.5f, base_), (rail * 0.5f, base_ + rail), (-rail * 0.5f, base_ + rail) }, true, true, last);
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (postSpacing > 0f)
            {
                foreach (float s in Stations(total, postSpacing, phase, last))
                    Box(metal.Mesh, RoadMarkings.PointAt(line, at, s), HeadingAt(line, at, s), 0.04f, 0.04f, base_ - 0.2f, base_);
            }

            float c = (float)Math.Cos(GlareAngle), sn = (float)Math.Sin(GlareAngle);
            foreach (float s in MarkingStuds.Stations(total, Math.Max(0.3f, gap), phase + gap * 0.5f))
            {
                Vec3 ahead = HeadingAt(line, at, s), right = ahead.RightXZ();
                // The panel's face looks along the road, turned a little: its width across, its thickness along.
                Box(green.Mesh, RoadMarkings.PointAt(line, at, s), ahead * c + right * sn, width, thickness, base_ + rail, base_ + rail + height);
            }

            return shapes;
        }
    }
}
