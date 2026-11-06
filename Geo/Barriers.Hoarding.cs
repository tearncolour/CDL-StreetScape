using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The kinds of construction hoarding (施工围挡): the flat panel of 0.12.13 is the default, the others are this version's.</summary>
    public enum HoardingStyle
    {
        Flat,
        Steel,
        Module,
        Pvc,
        Mesh,
        Brick,
        Warning,
        Lamp,
        Gate
    }

    /// <summary>Where a hoarding carries flowers: troughs standing at its foot, or hung on its top.</summary>
    public enum HoardingFlowers
    {
        None,
        Foot,
        Top
    }

    /// <summary>What a hoarding is made of and carries (the template's fields).</summary>
    public sealed class HoardingOptions
    {
        public HoardingStyle Style;
        public float Height = 2f, Spacing = 3f;

        /// <summary>The panel's colour (steel sheet, board, mesh coating, gate leaf); the brick wall's own colour is Brick.</summary>
        public SolidColour Panel = SolidColour.Blue;

        public SolidColour Brick = SolidColour.Brick;

        /// <summary>The width of the gate's opening (m), Gate only.</summary>
        public float GateWidth = 6f;

        public HoardingFlowers Flowers;

        /// <summary>The side of the troughs: +1 on the line's right, -1 on its left, 0 on both.</summary>
        public int Side = 1;

        /// <summary>The game's plants in the troughs (GreenPlants.Flower or GreenPlants.Shrub).</summary>
        public string Plants = GreenPlants.Flower;
    }

    /// <summary>
    /// The styles of the construction hoarding (0.12.78; the owner: "施工围挡加入更多样式", and "带花的施工围挡"): Barriers.HoardingStyled. The flat panel (Hoarding, 0.12.13) is untouched and is what the default style
    /// builds, so an item saved before the styles existed draws as it always did. Every size here is this mod's estimate of the usual products and walls (no standard is quoted); each builder says what it
    /// follows. A hoarding has no levels of detail (it is not one of the long-reach barriers, BarrierLod). Pure.
    /// </summary>
    public static partial class Barriers
    {
        /// <summary>The corrugation of a colour-coated steel sheet (彩钢板): ribs 25 mm high, 250 mm apart (a trapezoid wave, each of its four flanks a quarter pitch long), the sheet 10 mm thick for the eye (the real sheet is under 1 mm).</summary>
        private const float RibPitch = 0.25f, RibHeight = 0.025f, SteelSheet = 0.01f;

        /// <summary>
        /// A hoarding of the given style along the line (a piece of a run that starts phase metres along the whole run, whole metres long; first and last say whether the piece holds the run's ends), with
        /// troughs of flowers when asked for (their plants into props). The flat style with no flowers is Hoarding.
        /// </summary>
        public static List<MarkingShape> HoardingStyled(IList<Vec3> line, HoardingOptions o, float phase, float whole, bool first, bool last, List<PropAnchor> props)
        {
            List<MarkingShape> shapes;
            float spacing = Math.Max(2f, Math.Min(4f, o.Spacing));
            if (o.Style == HoardingStyle.Flat || o.Style == HoardingStyle.Warning || o.Style == HoardingStyle.Lamp)
                shapes = Hoarding(line, o.Height, spacing, o.Panel, phase, last);
            else
                shapes = new List<MarkingShape>();
            if (line == null || line.Count < 2)
                return shapes;
            var c = new HoardingContext(shapes, line, o, spacing, phase, whole, first, last);
            if (c.Total <= 1e-3f)
                return shapes;
            switch (o.Style)
            {
                case HoardingStyle.Steel: SteelHoarding(c); break;
                case HoardingStyle.Module: ModuleHoarding(c); break;
                case HoardingStyle.Pvc: PvcHoarding(c); break;
                case HoardingStyle.Mesh: MeshHoarding(c); break;
                case HoardingStyle.Brick: BrickHoarding(c); break;
                case HoardingStyle.Warning: WarningBands(c); break;
                case HoardingStyle.Lamp: PostLamps(c); break;
                case HoardingStyle.Gate: GateHoarding(c); break;
            }

            if (o.Flowers != HoardingFlowers.None)
                HoardingTroughs(c, props);
            return shapes;
        }

        /// <summary>A piece of a run being built: its line and lengths, the style's numbers, and the shapes by material.</summary>
        private sealed class HoardingContext
        {
            public readonly List<MarkingShape> Shapes;
            public readonly IList<Vec3> Line;
            public readonly List<float> At = new List<float>();
            public readonly float Total, Height, Spacing, Phase, Whole;
            public readonly bool First, Last;
            public readonly HoardingOptions Options;
            public readonly SolidColour Panel, PostColour;

            public HoardingContext(List<MarkingShape> shapes, IList<Vec3> line, HoardingOptions o, float spacing, float phase, float whole, bool first, bool last)
            {
                Shapes = shapes;
                Line = line;
                Options = o;
                Total = RoadMarkings.Lengths(line, At);
                Height = Math.Max(1.5f, Math.Min(3f, o.Height));
                Spacing = spacing;
                Phase = phase;
                Whole = Math.Max(whole, phase + Total);
                First = first;
                Last = last;
                Panel = o.Panel;
                PostColour = o.Panel == SolidColour.White ? SolidColour.Metal : SolidColour.White;
            }

            /// <summary>The mesh of a material, made when first asked for (a material the style does not use has no empty part).</summary>
            public MeshBuilder Mesh(SolidColour solid, DetailKind detail = DetailKind.None)
            {
                foreach (MarkingShape shape in Shapes)
                {
                    if (shape.Solid == solid && shape.Detail == detail)
                        return shape.Mesh;
                }

                var made = new MarkingShape { Solid = solid, Detail = detail };
                Shapes.Add(made);
                return made.Mesh;
            }

            public Vec3 PointAt(float s) => RoadMarkings.PointAt(Line, At, s);

            public Vec3 HeadingAt(float s) => Barriers.HeadingAt(Line, At, s);

            public List<Vec3> Trim(float from, float to) => Trimmed(Line, At, Math.Max(0f, from), Math.Min(Total, to));
        }

        /// <summary>A piece of a run between two cuts: where it meets a post, or the end of the run (a free end wants a cap), or only the end of the piece (the next piece goes on from it).</summary>
        private struct Bay
        {
            public float S0, S1;
            public bool Post0, Post1, Cap0, Cap1;

            /// <summary>Whether a sweep along the bay is capped at its start and its end: where a post stands or the run ends (a join of two pieces goes on in the next one, open).</summary>
            public bool Capped0 => Post0 || Cap0;

            public bool Capped1 => Post1 || Cap1;
        }

        /// <summary>
        /// The bays between the posts (and the ends of a gate's opening, which are posts too; the part of a bay inside the opening is left out). posts are the posts' places along the piece, already without
        /// the ones in the opening. A bay at an end of the piece that is not a post of the run is open there (its sweep is not capped) unless the end is the run's.
        /// </summary>
        private static List<Bay> Bays(HoardingContext c, IList<float> posts, float gate0, float gate1)
        {
            bool gated = gate1 > gate0;
            var cuts = new List<(float s, bool post)> { (0f, false), (c.Total, false) };
            foreach (float p in posts)
                cuts.Add((p, true));
            if (gated)
            {
                if (gate0 > 1e-3f && gate0 < c.Total - 1e-3f)
                    cuts.Add((gate0, true));
                if (gate1 > 1e-3f && gate1 < c.Total - 1e-3f)
                    cuts.Add((gate1, true));
            }

            cuts.Sort((a, b) => a.s.CompareTo(b.s));
            var merged = new List<(float s, bool post)>();
            foreach ((float s, bool post) in cuts)
            {
                if (merged.Count > 0 && s - merged[merged.Count - 1].s < 2e-3f)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].s, merged[merged.Count - 1].post || post);
                else
                    merged.Add((s, post));
            }

            var bays = new List<Bay>();
            for (int i = 0; i + 1 < merged.Count; i++)
            {
                (float a, bool pa) = merged[i];
                (float b, bool pb) = merged[i + 1];
                float mid = (a + b) * 0.5f;
                if (b - a < 0.02f || (gated && mid > gate0 && mid < gate1))
                    continue;
                bays.Add(new Bay { S0 = a, S1 = b, Post0 = pa, Post1 = pb, Cap0 = !pa && a <= 1e-3f && c.First, Cap1 = !pb && b >= c.Total - 1e-3f && c.Last });
            }

            return bays;
        }

        /// <summary>The posts of a piece (every spacing, and at the run's end), except those inside a gate's opening.</summary>
        private static List<float> PostsOf(HoardingContext c, float gate0, float gate1)
        {
            List<float> all = Stations(c.Total, c.Spacing, c.Phase, c.Last);
            if (!(gate1 > gate0))
                return all;
            all.RemoveAll(s => s > gate0 - 1e-3f && s < gate1 + 1e-3f);
            return all;
        }

        /// <summary>The way a section turns at a post's face: a bay's panel starts this far off the post's middle (half the post's width).</summary>
        private static List<Vec3> BayLine(HoardingContext c, Bay b, float postHalf)
        {
            return c.Trim(b.S0 + (b.Post0 ? postHalf : 0f), b.S1 - (b.Post1 ? postHalf : 0f));
        }

        // ------------------------------------------------------------------ 彩钢板

        /// <summary>
        /// A colour-coated steel hoarding (彩钢板围挡): trapezoid-ribbed sheet running on past the posts (ribs every 250 mm, vertical), on a concrete kerb 300 mm high and 200 mm wide, under a 70 mm
        /// capping, held by square posts 100 mm every spacing. The kerb, the capping and the post sizes are estimates from photos of site hoardings; the rib 25 x 250 mm is the usual small-rib sheet's. The
        /// sheet is a shell of its two skins that is open along its top and foot (the capping and the kerb hide them).
        /// </summary>
        private static void SteelHoarding(HoardingContext c)
        {
            const float kerb = 0.30f, cap = 0.07f;
            float h = c.Height;
            Sweep(c.Mesh(SolidColour.Concrete), c.Line, Rect(-0.1f, 0.1f, -FootSink, kerb), true, c.First, c.Last);
            Corrugated(c.Mesh(c.Panel), c.Line, c.Phase, kerb - 0.05f, h - cap + 0.03f, c.First, c.Last);
            MeshBuilder frame = c.Mesh(c.PostColour);
            Sweep(frame, c.Line, Rect(-0.07f, 0.07f, h - cap, h), true, c.First, c.Last);
            foreach (float s in Stations(c.Total, c.Spacing, c.Phase, c.Last))
                Box(frame, c.PointAt(s), c.HeadingAt(s), 0.10f, 0.10f, -Embed, h - cap);
        }

        /// <summary>The trapezoid wave of a rib, 0 to 1 over one pitch: the valley, the way up, the crest and the way down, a quarter each.</summary>
        private static float Wave(double turns)
        {
            float f = (float)(turns - Math.Floor(turns));
            if (f < 0.25f)
                return 0f;
            if (f < 0.5f)
                return (f - 0.25f) * 4f;
            return f < 0.75f ? 1f : (1f - f) * 4f;
        }

        /// <summary>
        /// A ribbed sheet along the line from y0 to y1: its front and back skins (the back one thick behind the front), the ribs vertical and in step along the whole run (phase). A cut at every quarter
        /// pitch and every corner, so that the sheet follows a bend; flat faces; open at its top and foot.
        /// </summary>
        private static void Corrugated(MeshBuilder mesh, IList<Vec3> line, float phase, float y0, float y1, bool first, bool last)
        {
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(total > 1e-3f) || !(y1 > y0))
                return;
            double step = RibPitch * 0.25;
            var cuts = new List<float>();
            for (int i = 1; i + 1 < line.Count; i++)
                cuts.Add(at[i]);
            for (long k = (long)Math.Floor(phase / step) + 1; k * step - phase < total - 1e-4 && cuts.Count < 8000; k++)
                cuts.Add((float)(k * step - phase));
            cuts.Sort();
            var stops = new List<float> { 0f };
            foreach (float s in cuts)
            {
                if (s > stops[stops.Count - 1] + 1e-4f && s < total - 1e-4f)
                    stops.Add(s);
            }

            stops.Add(total);
            var points = new List<Vec3>(stops.Count);
            foreach (float s in stops)
                points.Add(RoadMarkings.PointAt(line, at, s));
            List<Frame> frames = Frames(points);
            var front = new float[stops.Count];
            for (int i = 0; i < stops.Count; i++)
                front[i] = Wave((stops[i] + phase) / RibPitch) * RibHeight - RibHeight * 0.5f + SteelSheet * 0.5f;

            float h = y1 - y0;
            for (int i = 0; i + 1 < frames.Count; i++)
            {
                Vec3 a = points[i], b = points[i + 1];
                float run = new Vec3(b.X - a.X, 0f, b.Z - a.Z).Length;
                if (run < 1e-5f)
                    continue;
                Vec3 ahead = (b - a).HeadingXZ(), right = ahead.RightXZ();
                float slope = (front[i + 1] - front[i]) / run;
                Vec3 n = MeshBuilder.Normalize(right - ahead * slope);
                float s0 = phase + stops[i], s1 = phase + stops[i + 1];
                foreach (float skin in new[] { 0f, -SteelSheet })
                {
                    Vec3 f0 = frames[i].P + frames[i].Right * (front[i] + skin), f1 = frames[i + 1].P + frames[i + 1].Right * (front[i + 1] + skin);
                    Vec3 face = skin == 0f ? n : n * -1f;
                    Quad(mesh, f0 + Up * y0, f1 + Up * y0, f1 + Up * y1, f0 + Up * y1, face, 0f, s0, 0f, s1, h, s1, h, s0);
                }
            }

            if (first)
                SheetEnd(mesh, frames[0], front[0], y0, y1, frames[0].Ahead * -1f);
            if (last)
                SheetEnd(mesh, frames[frames.Count - 1], front[frames.Count - 1], y0, y1, frames[frames.Count - 1].Ahead);
        }

        /// <summary>The end of a ribbed sheet: the edge between its two skins.</summary>
        private static void SheetEnd(MeshBuilder mesh, Frame f, float front, float y0, float y1, Vec3 normal)
        {
            Vec3 a = f.P + f.Right * front, b = f.P + f.Right * (front - SteelSheet);
            Quad(mesh, a + Up * y0, b + Up * y0, b + Up * y1, a + Up * y1, normal, 0f, y0, SteelSheet, y0, SteelSheet, y1, 0f, y1);
        }

        // ------------------------------------------------------------------ 装配式

        /// <summary>
        /// A modular hoarding (装配式围挡): prefabricated panels 50 mm thick between steel posts 100 mm (one module to a spacing), each on a concrete foot block 600 x 300 x 180 mm under the post, held
        /// by a bottom rail and a middle rail (70 mm wide, a little proud of the panel) and one capping 60 mm high along the top. A panel starts at the post's face, so the modules read one by one. Sizes are
        /// estimates from photos of site hoardings.
        /// </summary>
        private static void ModuleHoarding(HoardingContext c)
        {
            const float field = 0.22f, cap = 0.06f, post = 0.10f;
            float h = c.Height, top = h - cap, mid = (field + top) * 0.5f;
            MeshBuilder panels = c.Mesh(c.Panel), frame = c.Mesh(c.PostColour), feet = c.Mesh(SolidColour.Concrete);
            List<float> posts = PostsOf(c, 0f, 0f);
            foreach (Bay b in Bays(c, posts, 0f, 0f))
            {
                List<Vec3> span = BayLine(c, b, post * 0.5f);
                if (span.Count < 2)
                    continue;
                Sweep(panels, span, Rect(-0.025f, 0.025f, field, top), true, b.Capped0, b.Capped1);
                Sweep(frame, span, Rect(-0.035f, 0.035f, field - 0.04f, field + 0.03f), true, b.Capped0, b.Capped1);
                Sweep(frame, span, Rect(-0.035f, 0.035f, mid - 0.03f, mid + 0.03f), true, b.Capped0, b.Capped1);
            }

            Sweep(frame, c.Line, Rect(-0.065f, 0.065f, top, h), true, c.First, c.Last);
            foreach (float s in posts)
            {
                Vec3 p = c.PointAt(s), ahead = c.HeadingAt(s);
                Box(frame, p, ahead, post, post, -Embed, top);
                Box(feet, p, ahead, 0.30f, 0.60f, -FootSink, 0.18f);
            }
        }

        // ------------------------------------------------------------------ PVC

        /// <summary>
        /// A PVC hoarding (PVC围挡): boards stacked to the capping, each about 500 mm high and 50 mm thick with its edges chamfered (a groove between two boards, the board's joint), on a plinth 250 mm high,
        /// between plastic posts 120 mm every spacing with a cap on each. The boards are the panel colour, smooth plastic. Sizes are estimates from photos of PVC site hoardings.
        /// </summary>
        private static void PvcHoarding(HoardingContext c)
        {
            const float plinth = 0.25f, cap = 0.08f, post = 0.12f, half = 0.025f, chamfer = 0.012f;
            float h = c.Height, top = h - cap;
            int boards = Math.Max(2, (int)Math.Round((top - plinth) / 0.5f));
            float each = (top - plinth) / boards;
            Sweep(c.Mesh(SolidColour.DarkGray), c.Line, Rect(-0.08f, 0.08f, -FootSink, plinth), true, c.First, c.Last);
            MeshBuilder plank = c.Mesh(c.Panel, DetailKind.Plastic), frame = c.Mesh(c.PostColour);
            for (int i = 0; i < boards; i++)
            {
                float y0 = plinth + each * i, y1 = y0 + each;
                var profile = new List<(float u, float v)>
                {
                    (-half + chamfer, y0), (half - chamfer, y0), (half, y0 + chamfer), (half, y1 - chamfer),
                    (half - chamfer, y1), (-half + chamfer, y1), (-half, y1 - chamfer), (-half, y0 + chamfer)
                };
                Sweep(plank, c.Line, profile, true, c.First, c.Last);
            }

            Sweep(frame, c.Line, Rect(-0.05f, 0.05f, top, h), true, c.First, c.Last);
            foreach (float s in Stations(c.Total, c.Spacing, c.Phase, c.Last))
            {
                Vec3 p = c.PointAt(s), ahead = c.HeadingAt(s);
                Box(frame, p, ahead, post, post, -Embed, h + 0.03f);
                Block(frame, p, ahead, post + 0.04f, post + 0.04f, h + 0.03f, h + 0.06f);
            }
        }

        // ------------------------------------------------------------------ 护栏网

        /// <summary>
        /// A wire-mesh hoarding (护栏网围挡): welded 40 x 40 mm mesh panels (the stock works-edge size, DetailKind.GuardMesh) in a frame of round tube 36 mm, between tube posts 60 mm every spacing on concrete feet
        /// 600 x 250 x 120 mm, the mesh and frame in the panel colour (the green plastic-coated fence is the usual one). The tube sizes and the feet are estimates from photos.
        /// </summary>
        private static void MeshHoarding(HoardingContext c)
        {
            const float railR = 0.018f, postR = 0.03f, low = 0.12f;
            float h = c.Height, high = h - 0.08f;
            MeshBuilder wire = c.Mesh(c.Panel, DetailKind.GuardMesh), frame = c.Mesh(c.Panel), feet = c.Mesh(SolidColour.Concrete);
            List<float> posts = PostsOf(c, 0f, 0f);
            foreach (Bay b in Bays(c, posts, 0f, 0f))
            {
                List<Vec3> span = BayLine(c, b, postR);
                if (span.Count < 2)
                    continue;
                var at = new List<float>();
                RoadMarkings.Lengths(span, at);
                Sheet(wire, span, at, 0f, at[at.Count - 1], low, high, 0.004f, c.Phase + b.S0 + (b.Post0 ? postR : 0f));
                Rail(frame, span, 0f, low, railR, true, true);
                Rail(frame, span, 0f, high, railR, true, true);
            }

            foreach (float s in posts)
            {
                Vec3 p = c.PointAt(s);
                Solids.Tube(frame, p - Up * Embed, p + Up * h, postR, postR, 8, false, true);
                Box(feet, p, c.HeadingAt(s), 0.25f, 0.60f, -FootSink, 0.12f);
            }
        }

        // ------------------------------------------------------------------ 砖墙

        /// <summary>
        /// A brick hoarding (砖砌围墙): a wall one brick thick (240 mm) in running bond on a concrete footing 500 mm wide, brick pilasters one and a half bricks square (370 mm) at every spacing, a concrete
        /// coping 300 mm wide and 80 mm high over both. The brick colour is red, grey or a white render (the painted wall of a site). Sizes are the usual brick wall's.
        /// </summary>
        private static void BrickHoarding(HoardingContext c)
        {
            const float wall = 0.24f, pilaster = 0.37f, footing = 0.10f, cap = 0.08f;
            float h = c.Height, top = h - cap;
            MeshBuilder bricks = c.Mesh(c.Options.Brick), concrete = c.Mesh(SolidColour.Concrete);
            Sweep(concrete, c.Line, Rect(-0.25f, 0.25f, -Embed, footing), true, c.First, c.Last);
            // The wall's first face is its right one, so that the texture's u runs up it (the courses) and v along it, in step along the run.
            Sweep(bricks, c.Line, new List<(float u, float v)> { (wall * 0.5f, footing), (wall * 0.5f, top), (-wall * 0.5f, top), (-wall * 0.5f, footing) }, true, c.First, c.Last, c.Phase);
            Sweep(concrete, c.Line, Rect(-0.15f, 0.15f, top, h), true, c.First, c.Last);
            foreach (float s in Stations(c.Total, c.Spacing, c.Phase, c.Last))
                Block(bricks, c.PointAt(s), c.HeadingAt(s), pilaster, pilaster, footing, top);
        }

        // ------------------------------------------------------------------ 警示色

        /// <summary>
        /// Warning bands on the flat panel (警示色围挡): over its foot, on both faces, a band 350 mm high of yellow and black stripes at 45 degrees, each stripe 150 mm across, in step along the whole run.
        /// The band is a decal 3 mm proud of the panel. The sizes are estimates from photos.
        /// </summary>
        private static void WarningBands(HoardingContext c)
        {
            const float band = 0.35f, width = 0.15f, off = 0.033f;
            Stripes(c.Mesh(SolidColour.Yellow), c.Mesh(SolidColour.Black), c.Line, c.At, c.Phase, -FootSink + 0.05f, band, width, off);
        }

        /// <summary>
        /// The stripes of a band from y0 to y1 on both faces of a wall of the line: each a parallelogram that leans at 45 degrees (its top edge shifted along by the band's height), cut at the corners of
        /// the line and at the ends of the piece; even stripes (counted along the whole run) yellow, odd ones black.
        /// </summary>
        private static void Stripes(MeshBuilder yellow, MeshBuilder black, IList<Vec3> line, IList<float> at, float phase, float y0, float y1, float width, float off)
        {
            float shift = y1 - y0;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                float lo = at[i], hi = at[i + 1];
                if (hi - lo < 1e-3f)
                    continue;
                Vec3 right = (line[i + 1] - line[i]).HeadingXZ().RightXZ();
                long k0 = (long)Math.Floor((lo + phase - shift) / width) - 1, k1 = (long)Math.Ceiling((hi + phase) / width) + 1;
                for (long k = k0; k <= k1; k++)
                {
                    float g = (float)(k * (double)width - phase);
                    var poly = new List<(float s, float y)> { (g, y0), (g + width, y0), (g + width + shift, y1), (g + shift, y1) };
                    poly = ClipAlong(ClipAlong(poly, lo, true), hi, false);
                    if (poly.Count < 3)
                        continue;
                    MeshBuilder mesh = (k & 1) == 0 ? yellow : black;
                    foreach (float side in new[] { 1f, -1f })
                    {
                        Vec3 n = right * side;
                        int start = mesh.VertexCount;
                        foreach ((float s, float y) in poly)
                        {
                            Vec3 p = line[i] + (line[i + 1] - line[i]) * ((s - lo) / (hi - lo)) + Up * y + n * off;
                            mesh.Vertex(p, n, y - y0, phase + s);
                        }

                        for (int j = 1; j + 1 < poly.Count; j++)
                            mesh.TriFacing(start, start + j, start + j + 1, n);
                    }
                }
            }
        }

        /// <summary>The polygon cut at s = edge, keeping what is over (keepAbove) or under it in s (Sutherland-Hodgman against one line).</summary>
        private static List<(float s, float y)> ClipAlong(List<(float s, float y)> poly, float edge, bool keepAbove)
        {
            var kept = new List<(float s, float y)>();
            for (int i = 0; i < poly.Count; i++)
            {
                (float s, float y) a = poly[i], b = poly[(i + 1) % poly.Count];
                bool inA = keepAbove ? a.s >= edge : a.s <= edge, inB = keepAbove ? b.s >= edge : b.s <= edge;
                if (inA)
                    kept.Add(a);
                if (inA != inB)
                    kept.Add((edge, a.y + (b.y - a.y) * ((edge - a.s) / (b.s - a.s))));
            }

            return kept;
        }

        // ------------------------------------------------------------------ 警示灯

        /// <summary>
        /// A warning lamp on every post (警示灯): a black housing 140 mm square and 40 mm high on the post's top and an amber lens of 65 mm radius on it (it lights at night, TsMaterials: no glow by day). The
        /// panel and the posts are the flat hoarding's. The lamp's size is an estimate of the solar flasher.
        /// </summary>
        private static void PostLamps(HoardingContext c)
        {
            float top = c.Height + 0.05f;
            MeshBuilder housing = c.Mesh(SolidColour.Black), lens = c.Mesh(SolidColour.GlowGold);
            foreach (float s in Stations(c.Total, c.Spacing, c.Phase, c.Last))
            {
                Vec3 p = c.PointAt(s);
                Block(housing, p, c.HeadingAt(s), 0.14f, 0.14f, top, top + 0.04f);
                Sphere(lens, p + Up * (top + 0.045f), 0.065f, 8, 5);
            }
        }

        // ------------------------------------------------------------------ 工地大门

        /// <summary>The gate's two leaves meet in the middle of the run, so the run must be longer than the opening by this much (m) on either side.</summary>
        private const float GateMargin = 1f;

        /// <summary>
        /// A site gate (工地大门) in the middle of the run: an opening of the chosen width (4, 6 or 8 m) between two gate posts 220 mm square and 300 mm higher than the hoarding, closed by two leaves (each half
        /// the opening, 40 mm thick, in the panel colour) in a frame of 70 mm bars with a cross brace on each face; the hoarding on either side is the flat panel. A run shorter than the opening and 2 m
        /// of hoarding has no gate. The leaves and the posts at the opening are made by the piece that holds their start, and reach on along the line's last chord if it ends inside them (a gate across a
        /// bend of the run is straight). The sizes are estimates from photos.
        /// </summary>
        private static void GateHoarding(HoardingContext c)
        {
            float width = Math.Max(3f, Math.Min(10f, c.Options.GateWidth));
            bool gated = c.Whole >= width + 2f * GateMargin;
            float centre = c.Whole * 0.5f - c.Phase, g0 = gated ? centre - width * 0.5f : 0f, g1 = gated ? centre + width * 0.5f : 0f;
            float h = c.Height;
            MeshBuilder panels = c.Mesh(c.Panel), posts = c.Mesh(c.PostColour);
            List<float> places = PostsOf(c, g0, g1);
            foreach (Bay b in Bays(c, places, g0, g1))
            {
                List<Vec3> span = c.Trim(b.S0, b.S1);
                if (span.Count >= 2)
                    Sweep(panels, span, new List<(float u, float v)> { (-0.03f, -FootSink), (0.03f, -FootSink), (0.03f, h), (-0.03f, h) }, true, b.Capped0, b.Capped1);
            }

            foreach (float s in places)
                Box(posts, c.PointAt(s), c.HeadingAt(s), 0.08f, 0.08f, -Embed, h + 0.05f);
            if (!gated)
                return;
            foreach (float s in new[] { g0, g1 })
            {
                if (Holds(s, c.Total, c.Last))
                    Box(posts, PointBeyond(c.Line, c.At, s), c.HeadingAt(Math.Max(0f, Math.Min(c.Total, s))), 0.22f, 0.22f, -Embed, h + 0.30f);
            }

            foreach (float s0 in new[] { g0, centre })
            {
                if (Holds(s0, c.Total, c.Last))
                    GateLeaf(c, panels, posts, s0 + 0.03f, s0 + width * 0.5f - 0.03f);
            }
        }

        /// <summary>One leaf of a gate from s0 to s1 along the line (a point beyond the piece's end is on its last chord): a panel, a frame of bars, two stiles and a cross brace on each face.</summary>
        private static void GateLeaf(HoardingContext c, MeshBuilder panel, MeshBuilder frame, float s0, float s1)
        {
            float h = c.Height, bottom = 0.10f, topY = h - 0.10f;
            var span = new List<Vec3>();
            foreach (var point in SpanAt(c.Line, c.At, s0, s1))
                span.Add(point.P);
            Sweep(panel, span, Rect(-0.02f, 0.02f, bottom + 0.06f, topY - 0.06f), true, true, true);
            Sweep(frame, span, Rect(-0.035f, 0.035f, bottom, bottom + 0.07f), true, true, true);
            Sweep(frame, span, Rect(-0.035f, 0.035f, topY - 0.07f, topY), true, true, true);
            Vec3 ahead = (span[span.Count - 1] - span[0]).HeadingXZ(), right = ahead.RightXZ();
            foreach (float s in new[] { s0 + 0.035f, s1 - 0.035f })
                Block(frame, PointBeyond(c.Line, c.At, s), ahead, 0.07f, 0.07f, bottom, topY);
            Vec3 a = span[0] + ahead * 0.10f + Up * (bottom + 0.10f), b = span[span.Count - 1] - ahead * 0.10f + Up * (topY - 0.10f);
            foreach (float side in new[] { 1f, -1f })
                Solids.Tube(frame, a + right * (side * 0.045f), b + right * (side * 0.045f), 0.02f, 0.02f, 6, true, true);
        }

        // ------------------------------------------------------------------ flowers

        /// <summary>How far the hoarding's body (its foot: kerb, footing or block; its post) reaches from the line on either side (m), so that a trough clears it.</summary>
        private static float FootClear(HoardingStyle style)
        {
            switch (style)
            {
                case HoardingStyle.Steel: return 0.12f;
                case HoardingStyle.Module: return 0.16f;
                case HoardingStyle.Pvc: return 0.10f;
                case HoardingStyle.Mesh: return 0.13f;
                case HoardingStyle.Brick: return 0.26f;
                default: return 0.045f;
            }
        }

        private static float PostClear(HoardingStyle style)
        {
            switch (style)
            {
                case HoardingStyle.Steel: return 0.06f;
                case HoardingStyle.Module: return 0.06f;
                case HoardingStyle.Pvc: return 0.07f;
                case HoardingStyle.Mesh: return 0.04f;
                case HoardingStyle.Brick: return 0.19f;
                default: return 0.045f;
            }
        }

        /// <summary>The troughs (and the plants asked of the game for them) of a hoarding; sizes are estimates of the usual troughs.</summary>
        private const float FootLength = 1.5f, FootWidth = 0.35f, FootHigh = 0.32f, FootGap = 0.5f, TopLength = 1.0f, TopWidth = 0.22f, TopHigh = 0.22f, TopGap = 0.5f, TroughWall = 0.04f;

        /// <summary>
        /// Flower troughs along the hoarding (带花的施工围挡): troughs 1.5 x 0.35 x 0.32 m standing on the ground by its foot (Foot), or 1.0 x 0.22 x 0.22 m hung on its top (Top) with two brackets bolted to its
        /// face, on its right, its left or both sides, the soil 4 cm under the rim and one of the game's plants in each (the kind asked: flowering bushes or evergreen shrubs). The game's plants are not scaled and
        /// the smallest of them (GreenBushCity01, 1.40 m across; the flowering ones 1.74 m or more) are wider than any trough, so a plant spills over its trough and the panel (decoration only, as in the planter
        /// boxes). A trough belongs to the piece that holds its middle and may reach into the next. None stands in a gate's opening.
        /// </summary>
        private static void HoardingTroughs(HoardingContext c, List<PropAnchor> props)
        {
            bool foot = c.Options.Flowers == HoardingFlowers.Foot;
            float length = foot ? FootLength : TopLength, width = foot ? FootWidth : TopWidth, high = foot ? FootHigh : TopHigh, pitch = length + (foot ? FootGap : TopGap);
            float top = foot ? high : c.Height - 0.08f, bottom = foot ? -FootSink : top - high;
            float clear = (foot ? FootClear(c.Options.Style) : PostClear(c.Options.Style)) + (foot ? 0.02f : 0.01f);
            float face = Face(c.Options.Style);
            MeshBuilder walls = c.Mesh(SolidColour.ResinGreen, DetailKind.Plastic), soil = c.Mesh(SolidColour.Soil), bracket = c.Mesh(SolidColour.Metal);
            float gate0 = 0f, gate1 = 0f;
            if (c.Options.Style == HoardingStyle.Gate && c.Whole >= Math.Max(3f, Math.Min(10f, c.Options.GateWidth)) + 2f * GateMargin)
            {
                float half = Math.Max(3f, Math.Min(10f, c.Options.GateWidth)) * 0.5f;
                gate0 = c.Whole * 0.5f - c.Phase - half - 0.3f;
                gate1 = c.Whole * 0.5f - c.Phase + half + 0.3f;
            }

            float offset = length * 0.5f;
            for (long k = (long)Math.Ceiling((c.Phase - offset - 1e-4) / pitch); ; k++)
            {
                float s = (float)(k * (double)pitch + offset - c.Phase);
                if (s > c.Total + 1e-4f)
                    break;
                if (!Holds(s, c.Total, c.Last) || (gate1 > gate0 && s + length * 0.5f > gate0 && s - length * 0.5f < gate1))
                    continue;
                Vec3 centre = PointBeyond(c.Line, c.At, s), ahead = c.HeadingAt(Math.Max(0f, Math.Min(c.Total, s))), right = ahead.RightXZ();
                foreach (float side in c.Options.Side == 0 ? new[] { 1f, -1f } : new[] { c.Options.Side > 0 ? 1f : -1f })
                {
                    Vec3 p = centre + right * (side * (clear + width * 0.5f));
                    OpenBox(walls, soil, p, ahead, width, length, bottom, top, TroughWall, 0.04f);
                    AddPlants(props, p + Up * (top - 0.04f), ahead, length - 2f * TroughWall, width - 2f * TroughWall, c.Options.Plants, true);
                    if (foot)
                        continue;
                    foreach (float along in new[] { -0.3f, 0.3f })
                    {
                        Vec3 at = centre + ahead * along + right * (side * (face + clear) * 0.5f) + Up * (top - 0.08f);
                        Box(bracket, at, right, 0.03f, clear - face + 0.04f, -0.0125f, 0.0125f);
                    }
                }
            }
        }

        /// <summary>How far the panel's face is from the line (m): where a hung trough's bracket is bolted.</summary>
        private static float Face(HoardingStyle style)
        {
            switch (style)
            {
                case HoardingStyle.Steel: return 0.0175f;
                case HoardingStyle.Module: return 0.025f;
                case HoardingStyle.Pvc: return 0.025f;
                case HoardingStyle.Mesh: return 0.002f;
                case HoardingStyle.Brick: return 0.12f;
                default: return 0.03f;
            }
        }
    }
}
