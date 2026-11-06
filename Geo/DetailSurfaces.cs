using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What a solid part's surface is made of, for the detail under 5 cm that is baked into its normal map instead of being modelled (0.12.32; the owner: "需要加入法线烘焙细节").</summary>
    public enum DetailKind
    {
        None,
        Concrete,
        Galvanised,
        Painted,
        Plastic,
        Wood,
        Soil,
        Reflective,

        /// <summary>A W-beam's beam with its splice joint and bolts every 4 m, and a post bolt there (posts every 4 m).</summary>
        WBeam4,

        /// <summary>The same with a post bolt every 2 m too (posts every 2 m).</summary>
        WBeam2,

        /// <summary>A granite kerb (0.12.33): its grain and the joints of its stones every metre along it.</summary>
        KerbStone,

        /// <summary>The concrete flags of a gutter (平石): their joints every half metre along it.</summary>
        GutterStone,

        /// <summary>Granite or marble without joints (0.12.34): a balustrade's posts and panels, stone balls.</summary>
        Stone,

        /// <summary>Welded wire mesh with holes (0.12.34, drawn cut out): an expressway fence's, 150 mm along by 75 mm up, wire 4 mm (GB/T 26941-2025: 75 x 75 to 200 x 75, wire 3.5 to 5).</summary>
        WeldedMesh,

        /// <summary>Fine welded mesh, 50 x 50 mm, wire 3 mm: an anti-throw screen's (GB 50688 7.7.3: no larger than 50 x 100 mm).</summary>
        FineMesh,

        /// <summary>The 40 x 40 mm mesh of a works edge guard, wire 3.8 mm (the usual stock panels).</summary>
        GuardMesh,

        /// <summary>
        /// Road paint (0.12.40): a thin coat over asphalt, its stones under it and dirt in it, whole (0.12.42: 0.12.40 wore it through in places, which read as damage to the paint). The markings'
        /// paint, with texture coordinates in world metres (ChunkBuilder.PaintUv), so that the pattern goes on from one marking to the next.
        /// </summary>
        RoadPaint,

        /// <summary>
        /// A tree grate (0.12.49, Barriers.Greening; 树池篦子) with its slots cut out: one grate to the tile, whatever its size (its top's coordinates span the tile): a solid border, the hole for the trunk,
        /// three rings of radial slots, four round holes at the corners for a tree guard's poles, the seam between its two halves.
        /// </summary>
        TreeGrate,

        /// <summary>Moulded glass-fibre grating (玻璃钢格栅), cells 38 x 38 mm between bars 6.5 mm wide, cut out (the suppliers' stock grating cut on site to a tree pit).</summary>
        FrpGrating,

        /// <summary>A peeled round timber pole (a tree guard's): the fibres along it and a few checks, matte.</summary>
        Timber,

        /// <summary>
        /// The sound-absorbing face of a noise barrier's panel (0.12.49, Barriers.NoiseBarrier): rows of stamped louvres (百叶孔) on 1.0 mm sheet, the joint of two stacked modules every 0.5 m up it
        /// (u up the face from the panel's foot, v along it from the panel's end).
        /// </summary>
        Louvre,

        /// <summary>The perforated face of a lightweight cement panel (0.12.49, 水泥声屏障): staggered rows of slots in a 600 mm module (u up from its foot, v along).</summary>
        CementSlots,

        /// <summary>A brick wall's face in running bond (0.12.49): bricks 240 x 53 mm with 10 mm joints (u up the wall, v along the courses: a wall swept along a run).</summary>
        Brick,

        /// <summary>A painted W-beam (0.12.78, the guardrail's colour): the relief and bolts of WBeam4 without its spangle, in a satin paint (metal 0.1, smoothness about 0.6); the colour is the shape's solid.</summary>
        WBeam4Painted,

        /// <summary>The same with a post bolt every 2 m too (WBeam2, painted).</summary>
        WBeam2Painted,

        /// <summary>
        /// The foliage of a vertical greening wall's face (0.12.78, DetailSurfaces.Foliage): overlapping leaves with blossoms on them, the blossoms' colour the solid's (SolidColour.Blossom*, else leaf green only); u up the face, v along it.
        /// </summary>
        Foliage
    }

    /// <summary>
    /// A baked detail surface: over a tile of the surface's own coordinates (u across and v along, in metres, repeating), the normals of the small relief (pores, spangles, grain, bolts), how much
    /// light reaches the bottom of it (the cavity, darkening the colour) and the metal, coat and smoothness of the game's mask map (BH/SG_DefaultShader: R metallic, G coat, B none, A smoothness).
    /// Row j of a map is v = (j + 0.5) / Height of the tile's length, column i is u likewise. Pure: made off the main thread, the same offline and in the game.
    /// </summary>
    public sealed class DetailSurface
    {
        public DetailKind Kind;
        public int Width, Height;

        /// <summary>The tile's size (m): across (u) and along (v).</summary>
        public float SizeU, SizeV;

        /// <summary>The relief (m), row by row.</summary>
        public float[] Relief;

        /// <summary>The normal map: x, y, z of each texel, -1 to 1, row by row.</summary>
        public float[] Normals;

        /// <summary>The colour factor (the cavity and small changes of the material's colour), 0 to 1.</summary>
        public float[] Shade;

        /// <summary>The mask map, 0 to 1: metallic, coat, smoothness.</summary>
        public float[] Metal, Coat, Smooth;

        /// <summary>Where there is something (1) and where there is a hole (0) in a wire mesh (0.12.34); null for a solid surface. A surface with holes is drawn with the game's cut-out material.</summary>
        public float[] Alpha;

        /// <summary>The foliage surface's blossoms (DetailKind.Foliage): how much of a texel is a blossom (0 to 1) and which of the mixed palette's colours a blossom has (0 to 1); null for every other kind.</summary>
        public float[] Mix, Hue;
    }

    /// <summary>
    /// The detail surfaces of the barriers' materials: each a height field of the material's small relief, made from deterministic noise and shapes that repeat with the tile (so the same in every game
    /// and without a seam), baked into a tangent-space normal map (central differences of the height, +u the tangent, +v the bitangent) and a cavity. The sizes are this mod's estimates of the real
    /// surfaces, not a standard's: pores of cast concrete 0.5 to 1.5 mm, spangles of hot-dip galvanising about 18 mm, the orange peel of a powder coat, the grain and joints of pine boards, clods of
    /// soil, the microprisms of reflective sheeting, and on the W-beam the lap of the next beam at the splice (3 mm, the beam's thickness) and the domed heads of the splice's eight bolts and the post
    /// bolt (M16: 26 mm across, 10 mm high, on 50 mm washers; positions estimated). Pure.
    /// </summary>
    public static partial class DetailSurfaces
    {
        /// <summary>The length of a W-beam's texture along it (m): one beam between two splices.</summary>
        public const float BeamPeriod = 4f;

        /// <summary>How far the splice's lap reaches either side of the joint (m).</summary>
        public const float LapHalf = 0.16f;

        /// <summary>
        /// The size of a kind's tile (m, across u and along v; 0 for None). The texture coordinates of a part are made in metres and divided by it when its plan is baked (MarkingPlan.Bake, ToTile): the
        /// game's shader takes a coordinate of 1 as the whole texture, it has no tiling of its own. The W-beam's tile is as wide as the beam's profile is long (u runs along the profile from its lower edge).
        /// </summary>
        public static (float U, float V) TileOf(DetailKind kind)
        {
            switch (kind)
            {
                case DetailKind.Concrete:
                case DetailKind.Galvanised:
                case DetailKind.Soil:
                case DetailKind.Stone:
                    return (0.5f, 0.5f);
                case DetailKind.Painted:
                case DetailKind.Plastic:
                    return (0.25f, 0.25f);
                case DetailKind.Wood:
                    return (1f, 0.5f);
                case DetailKind.Reflective:
                    return (0.06f, 0.06f);
                case DetailKind.KerbStone:
                case DetailKind.GutterStone:
                    return (0.5f, 1f);
                case DetailKind.WBeam4:
                case DetailKind.WBeam2:
                case DetailKind.WBeam4Painted:
                case DetailKind.WBeam2Painted:
                    return (ProfileLength(Barriers.BeamProfile(false)), BeamPeriod);
                case DetailKind.WeldedMesh:
                    return (0.3f, 0.3f);
                case DetailKind.FineMesh:
                case DetailKind.GuardMesh:
                    return (0.2f, 0.2f);
                case DetailKind.RoadPaint:
                    return (2f, 2f);
                case DetailKind.TreeGrate:
                    return (GrateTile, GrateTile);
                case DetailKind.FrpGrating:
                    return (0.152f, 0.152f);
                case DetailKind.Timber:
                    return (0.2f, 1f);
                case DetailKind.Louvre:
                    return (LouvreModule, LouvreColumn);
                case DetailKind.CementSlots:
                    return (CementModule, 0.4f);
                case DetailKind.Brick:
                    return (2f * BrickCourse, 2f * BrickPitch);
                case DetailKind.Foliage:
                    return (FoliageUp, FoliageAlong);
                default:
                    return (0f, 0f);
            }
        }

        /// <summary>Whether a kind is a wire mesh with holes (drawn with the game's cut-out material, and in the pictures as a screen).</summary>
        public static bool HasHoles(DetailKind kind) =>
            kind == DetailKind.WeldedMesh || kind == DetailKind.FineMesh || kind == DetailKind.GuardMesh || kind == DetailKind.TreeGrate || kind == DetailKind.FrpGrating;

        /// <summary>The tile of a tree grate (m): a grate of 1.2 m, the usual size; a grate of another size has its top's coordinates scaled to it, so the pattern is the grate's own.</summary>
        public const float GrateTile = 1.2f;

        /// <summary>A part's texture coordinates, in metres, made coordinates of the kind's tile.</summary>
        public static void ToTile(MeshBuilder mesh, DetailKind kind)
        {
            (float su, float sv) = TileOf(kind);
            if (!(su > 0f) || !(sv > 0f))
                return;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                mesh.U[i] /= su;
                mesh.V[i] /= sv;
            }
        }

        /// <summary>The surface of a kind (null for None).</summary>
        public static DetailSurface Make(DetailKind kind)
        {
            (float su, float sv) = TileOf(kind);
            if (!(su > 0f))
                return null;
            var t = new Tiling(su, sv);
            switch (kind)
            {
                case DetailKind.Concrete:
                    return Bake(kind, 512, 512, t, (u, v) => ConcreteRelief(t, u, v), (u, v) => (0f, 0f, 0.16f + 0.08f * t.Fbm(u, v, 4f, 3, 11)), 0.88f);
                case DetailKind.Galvanised:
                    return Bake(kind, 256, 256, t, (u, v) => SpangleRelief(t, u, v), (u, v) => (0.85f, 0f, 0.5f + 0.15f * (t.Cell(u, v, 0.018f, 3).value - 0.5f)), 0.95f);
                case DetailKind.Painted:
                    return Bake(kind, 256, 256, t, (u, v) => 0.00005f * t.Fbm(u, v, 300f, 3, 5), (u, v) => (0f, 0.3f, 0.55f), 1f);
                case DetailKind.Plastic:
                    return Bake(kind, 256, 256, t, (u, v) => 0.00002f * t.Fbm(u, v, 500f, 2, 7), (u, v) => (0f, 0f, 0.5f), 1f);
                case DetailKind.Wood:
                    return Bake(kind, 512, 256, t, (u, v) => WoodRelief(t, u, v), (u, v) => (0f, 0f, 0.25f + 0.05f * t.Fbm(u, v, 6f, 2, 13)), 0.85f);
                case DetailKind.Soil:
                    return Bake(kind, 256, 256, t, (u, v) => SoilRelief(t, u, v), (u, v) => (0f, 0f, 0.06f), 0.7f);
                case DetailKind.Reflective:
                    return Bake(kind, 128, 128, t, (u, v) => 0.00003f * Math.Min(1f, t.Cell(u, v, 0.003f, 41).d / 0.0015f), (u, v) => (0f, 0.6f, 0.8f), 1f);
                case DetailKind.KerbStone:
                    return Bake(kind, 256, 512, t, (u, v) => GraniteRelief(t, u, v), (u, v) => (0f, 0f, 0.3f + 0.1f * t.Fbm(u, v, 8f, 2, 51)), 0.85f, (u, v) => GraniteShade(t, u, v));
                case DetailKind.GutterStone:
                    return Bake(kind, 256, 512, t, (u, v) => ConcreteRelief(t, u, v) + Joint(v, 0.5f, 0.004f, 0.003f), (u, v) => (0f, 0f, 0.18f + 0.08f * t.Fbm(u, v, 4f, 3, 61)), 0.85f);
                case DetailKind.Stone:
                    return Bake(kind, 256, 256, t, (u, v) => GraniteRelief(t, u, v, false), (u, v) => (0f, 0f, 0.35f + 0.1f * t.Fbm(u, v, 8f, 2, 55)), 0.85f, (u, v) => GraniteShade(t, u, v));
                case DetailKind.WBeam4:
                case DetailKind.WBeam2:
                {
                    List<(float u, float v)> bolts = BeamBolts(su, kind == DetailKind.WBeam2);
                    return Bake(kind, 128, 2048, t, (u, v) => SpangleRelief(t, u, v) + BeamRelief(u, v, bolts),
                        (u, v) => (0.85f, 0f, 0.5f + 0.15f * (t.Cell(u, v, 0.018f, 3).value - 0.5f)), 0.95f);
                }
                case DetailKind.WBeam4Painted:
                case DetailKind.WBeam2Painted:
                {
                    // The galvanised beam's relief, bolts and size; a coat of paint has no spangle, only a faint orange peel (0.03 mm), and the paint is satin: metal 0.1, smoothness 0.56 to 0.64.
                    List<(float u, float v)> bolts = BeamBolts(su, kind == DetailKind.WBeam2Painted);
                    return Bake(kind, 128, 2048, t, (u, v) => 0.00003f * t.Fbm(u, v, 120f, 2, 81) + BeamRelief(u, v, bolts),
                        (u, v) => (0.1f, 0f, 0.6f + 0.04f * t.Fbm(u, v, 40f, 2, 82)), 0.95f);
                }
                case DetailKind.WeldedMesh:
                    return Wires(kind, t, 256, 0.075f, 0.15f, 0.002f);
                case DetailKind.FineMesh:
                    return Wires(kind, t, 256, 0.05f, 0.05f, 0.0015f);
                case DetailKind.GuardMesh:
                    return Wires(kind, t, 256, 0.04f, 0.04f, 0.0019f);
                case DetailKind.RoadPaint:
                    return Bake(kind, 512, 512, t, (u, v) => PaintRelief(t, u, v), (u, v) => (0f, 0f, 0.2f + 0.06f * t.Fbm(u, v, 2f, 2, 73)), 0.78f, (u, v) => PaintShade(t, u, v));
                case DetailKind.TreeGrate:
                {
                    float texel = t.SizeU / 512f;
                    return Bake(kind, 512, 512, t, (u, v) => GrateRelief(t, u, v), (u, v) => (0.25f, 0f, 0.3f + 0.08f * t.Fbm(u, v, 5f, 2, 81)), 0.8f, (u, v) => 0.88f + 0.12f * t.Fbm(u, v, 3f, 3, 82),
                        (u, v) => Math.Max(0f, Math.Min(1f, GrateSolid(u, v) / texel + 0.5f)));
                }
                case DetailKind.FrpGrating:
                    return Wires(kind, t, 256, 0.038f, 0.038f, 0.00325f);
                case DetailKind.Timber:
                    return Bake(kind, 128, 512, t, (u, v) => TimberRelief(t, u, v), (u, v) => (0f, 0f, 0.22f + 0.04f * t.Fbm(u, v, 8f, 2, 93)), 0.85f, (u, v) => 0.86f + 0.14f * t.Fbm(u, v, 4f, 3, 94));

                case DetailKind.Louvre:
                    return Bake(kind, 256, 128, t, (u, v) => LouvreRelief(t, u, v), (u, v) => (0f, 0.3f, 0.5f), 0.85f, LouvreShade);
                case DetailKind.CementSlots:
                    return Bake(kind, 256, 256, t, (u, v) => CementRelief(t, u, v), (u, v) => (0f, 0f, 0.14f + 0.06f * t.Fbm(u, v, 4f, 2, 91)), 0.85f, CementShade);
                case DetailKind.Foliage:
                    return FoliageSurface(kind, t);
                case DetailKind.Brick:
                    return Bake(kind, 128, 512, t, (u, v) => BrickRelief(t, u, v), (u, v) => (0f, 0f, 0.18f + 0.06f * t.Fbm(u, v, 6f, 2, 95)), 0.8f, (u, v) => BrickShade(t, u, v));
                default:
                    return null;
            }
        }

        /// <summary>
        /// A welded wire mesh: round wires of radius r, one across every up metres (u) and one up every along metres (v), the crossings welded (the higher of the two); its alpha whole on a wire and
        /// nothing between, with half a texel of soft edge for the mips. Coated wire: no metal, a little gloss.
        /// </summary>
        private static DetailSurface Wires(DetailKind kind, Tiling t, int size, float up, float along, float r)
        {
            float texel = t.SizeU / size;
            float Distance(float x, float period) => Math.Abs(Wrap(x, period));
            float Round(float d) => d < r ? (float)Math.Sqrt(r * r - d * d) : 0f;
            return Bake(kind, size, size, t, (u, v) => Math.Max(Round(Distance(u, up)), Round(Distance(v, along))), (u, v) => (0f, 0.1f, 0.45f), 0.9f, null,
                (u, v) => Math.Max(0f, Math.Min(1f, (r - Math.Min(Distance(u, up), Distance(v, along))) / texel + 0.5f)));
        }

        /// <summary>The length of a profile from its first corner to its last (m): what u runs over on a swept part (Barriers.Sweep).</summary>
        public static float ProfileLength(IList<(float u, float v)> profile)
        {
            float length = 0f;
            for (int i = 1; i < profile.Count; i++)
                length += (float)Math.Sqrt((profile[i].u - profile[i - 1].u) * (profile[i].u - profile[i - 1].u) + (profile[i].v - profile[i - 1].v) * (profile[i].v - profile[i - 1].v));
            return length;
        }

        /// <summary>
        /// Where the bolts of a two-wave beam are (u along its profile from the lower edge, v along it from a splice, m): the splice's eight in two rows across the beam and four columns 0.1 m apart over
        /// the lap; the post bolt in the beam's middle at the splice (and half way along with posts every 2 m).
        /// </summary>
        public static List<(float u, float v)> BeamBolts(float width, bool dense)
        {
            var bolts = new List<(float u, float v)>();
            foreach (float v in new[] { -0.15f, -0.05f, 0.05f, 0.15f })
            {
                foreach (float u in new[] { width * 0.22f, width * 0.78f })
                    bolts.Add((u, v));
            }

            bolts.Add((width * 0.5f, 0f));
            if (dense)
                bolts.Add((width * 0.5f, BeamPeriod * 0.5f));
            return bolts;
        }

        /// <summary>A beam's relief: the lap of the next beam over this one about the splice (3 mm proud, its end a step), and the domed bolt heads on their washers.</summary>
        private static float BeamRelief(float u, float v, List<(float u, float v)> bolts)
        {
            float d = Wrap(v, BeamPeriod);
            float lap = d > -LapHalf && d < LapHalf ? 0.003f * Smooth01((d + LapHalf) / 0.02f) * Smooth01((LapHalf - d) / 0.0015f) : 0f;
            float h = lap;
            foreach ((float bu, float bv) in bolts)
            {
                float dv = Wrap(v - bv, BeamPeriod), du = u - bu;
                float r = (float)Math.Sqrt(du * du + dv * dv);
                if (r >= 0.025f)
                    continue;
                float washer = 0.003f * Smooth01((0.025f - r) / 0.0015f);
                float head = r < 0.013f ? 0.010f * (float)Math.Sqrt(Math.Max(0f, 1f - (r / 0.013f) * (r / 0.013f))) : 0f;
                h = Math.Max(h, lap + washer + head);
            }

            return h;
        }

        private static float Wrap(float d, float period)
        {
            d %= period;
            if (d > period * 0.5f)
                d -= period;
            else if (d < -period * 0.5f)
                d += period;
            return d;
        }

        private static float Smooth01(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * (3f - 2f * t);
        }

        /// <summary>A joint across a line of stones every period (m) along it: a groove width wide and depth deep.</summary>
        private static float Joint(float v, float period, float width, float depth)
        {
            float d = Math.Abs(Wrap(v, period));
            return d < width * 0.5f ? -depth * (1f - (d / (width * 0.5f)) * (d / (width * 0.5f))) : 0f;
        }

        /// <summary>
        /// Paint over asphalt (estimates from photos of worn markings: no standard sets them): the stones of the asphalt under the coat (cells of 16 mm, the tops 0.6 mm proud) and a fine grain of the
        /// coat's glass beads.
        /// </summary>
        private static float PaintRelief(Tiling t, float u, float v)
        {
            (float d, float value) = t.Cell(u, v, 0.016f, 71);
            return 0.0006f * Smooth01(1f - d / 0.009f) * (0.5f + 0.5f * value) + 0.00008f * t.Fbm(u, v, 60f, 2, 72);
        }

        /// <summary>The coat's colour: dirt in patches of a metre and of a few centimetres, the stones' tops a little cleaner than the hollows between them (the cavity darkens those too).</summary>
        private static float PaintShade(Tiling t, float u, float v)
        {
            (float d, float value) = t.Cell(u, v, 0.016f, 71);
            float top = Smooth01(1f - d / 0.009f) * value;
            return Math.Min(1f, 0.84f + 0.08f * t.Fbm(u, v, 1f, 3, 75) + 0.04f * t.Fbm(u, v, 25f, 2, 76) + 0.08f * top);
        }

        /// <summary>Sawn and flamed granite: a fine grain, the crystals a hair proud, and (joints) a joint every metre (a kerb's stones are 1 m long).</summary>
        private static float GraniteRelief(Tiling t, float u, float v, bool joints = true)
        {
            (float d, float value) = t.Cell(u, v, 0.004f, 52);
            return 0.00012f * t.Fbm(u, v, 80f, 3, 53) + (value > 0.6f ? 0.00006f * Smooth01(1f - d / 0.002f) : 0f) + (joints ? Joint(v, 1f, 0.005f, 0.003f) : 0f);
        }

        /// <summary>Grey granite's colour: light quartz and feldspar with dark mica grains (cells of about 4 mm), a little cloudy.</summary>
        private static float GraniteShade(Tiling t, float u, float v)
        {
            (float d, float value) = t.Cell(u, v, 0.004f, 52);
            float grain = value < 0.25f ? 0.55f : value > 0.85f ? 1.1f : 0.95f;
            return Math.Min(1f, grain * (0.92f + 0.08f * t.Fbm(u, v, 3f, 2, 54)));
        }

        /// <summary>Cast concrete: a gently waving face, the aggregate just under it, and pores (pits 0.5 to 1.5 mm across, up to 1.5 mm deep) in about a third of the 6 mm cells.</summary>
        private static float ConcreteRelief(Tiling t, float u, float v)
        {
            float h = 0.0004f * t.Fbm(u, v, 6f, 3, 1) + 0.00025f * t.Fbm(u, v, 60f, 3, 2);
            (float d, float value) = t.Cell(u, v, 0.006f, 4);
            if (value > 0.7f)
            {
                float radius = 0.00025f + 0.0005f * (value - 0.7f) / 0.3f;
                if (d < radius)
                    h -= 0.0015f * (1f - d / radius);
            }

            return h;
        }

        /// <summary>
        /// Hot-dip galvanising: spangles (cells of about 18 mm) each a facet tilted up to half a degree, their boundaries a hair proud; the spangles show mostly by their sheen (the mask's smoothness by
        /// cell), the relief only catches a low light.
        /// </summary>
        private static float SpangleRelief(Tiling t, float u, float v)
        {
            (float d, float value, float gu, float gv, float other) = t.CellFacet(u, v, 0.018f, 3);
            float facet = 0.00008f * ((value - 0.5f) * 2f * gu + (other - 0.5f) * 2f * gv);
            return facet + 0.00002f * Smooth01(1f - d / 0.0015f);
        }

        /// <summary>Pine boards along u: the grain (rings drawn out along the board), and a groove every 0.125 m across where two boards meet.</summary>
        private static float WoodRelief(Tiling t, float u, float v)
        {
            float board = v / 0.125f;
            float seam = Math.Abs(board - (float)Math.Round(board)) * 0.125f;
            float groove = seam < 0.002f ? -0.0012f * (1f - seam / 0.002f) : 0f;
            float rings = (float)Math.Sin((v * 88f + 3f * t.Fbm(u, v, 2f, 3, 21)) * Math.PI);
            return 0.00025f * rings * rings + 0.0001f * t.Fbm(u, v, 30f, 2, 22) + groove;
        }

        /// <summary>
        /// How far a place of a tree grate (u, v in m on its 1.2 m tile) is from the nearest of its holes (m; + on the iron, - in a hole). The proportions are this mod's reading of suppliers' photos of
        /// the common radial cast grates (scratch/research-greening/REPORT.md): a border 66 mm wide, the trunk's hole 0.41 m across in a hub ring 48 mm wide, three rings of slots about 22 mm wide (as
        /// many as keep the bars as wide as the slots), the third reaching out into the corners, round holes 84 mm across in bosses at the corners, their middles 0.12 m in from the edges.
        /// </summary>
        public static float GrateSolid(float u, float v)
        {
            const float size = GrateTile, border = 0.055f, hole = 0.17f, hub = 0.21f, boss = 0.055f, cornerHole = 0.035f, corner = 0.40f;
            float x = u / size - 0.5f, y = v / size - 0.5f, r = (float)Math.Sqrt(x * x + y * y);
            float cx = Math.Abs(x) - corner, cy = Math.Abs(y) - corner, rc = (float)Math.Sqrt(cx * cx + cy * cy);
            float d = Math.Min((r - hole) * size, (rc - cornerHole) * size);
            double angle = Math.Atan2(y, x);
            float edge = 0.5f - border - Math.Max(Math.Abs(x), Math.Abs(y));
            foreach ((float r0, float r1, bool outer) in new[] { (hub + 0.015f, 0.305f, false), (0.325f, 0.405f, false), (0.425f, 1f, true) })
            {
                float mid = outer ? 0.5f : (r0 + r1) * 0.5f;
                int n = (int)Math.Round(2.0 * Math.PI * mid / 0.045);
                float period = (float)(2.0 * Math.PI * r / n) * size;
                double phase = angle / (2.0 * Math.PI) * n;
                float frac = (float)(phase - Math.Floor(phase));
                float bar = (Math.Abs(frac - 0.5f) - 0.25f) * period;
                float band = -Math.Min(r - r0, outer ? edge : r1 - r) * size;
                float slot = Math.Max(band, bar);
                if (outer)
                    slot = Math.Max(slot, (boss - rc) * size);
                d = Math.Min(d, slot);
            }

            return d;
        }

        /// <summary>A cast grate's relief: the edges of the holes rounded over 3 mm, the hub ring 1.5 mm and the border 1 mm proud, a groove where the two halves meet (across the middle of v), casting texture.</summary>
        private static float GrateRelief(Tiling t, float u, float v)
        {
            float d = GrateSolid(u, v);
            float h = -0.003f * (1f - Smooth01(d / 0.003f));
            float x = u / GrateTile - 0.5f, y = v / GrateTile - 0.5f, r = (float)Math.Sqrt(x * x + y * y);
            if (r > 0.17f && r < 0.21f)
                h += 0.0015f * Smooth01((r - 0.17f) * GrateTile / 0.003f) * Smooth01((0.21f - r) * GrateTile / 0.003f);
            float edge = 0.5f - Math.Max(Math.Abs(x), Math.Abs(y));
            if (edge < 0.055f)
                h += 0.001f * Smooth01((0.055f - edge) * GrateTile / 0.003f);
            float seam = Math.Abs(y) * GrateTile;
            if (seam < 0.002f && d > 0f)
                h -= 0.002f * (1f - seam / 0.002f);
            return h + 0.00008f * t.Fbm(u, v, 60f, 2, 83);
        }

        /// <summary>Peeled timber, v along the pole: fibres (lines along it: noise across u only) and a gentle unevenness.</summary>
        private static float TimberRelief(Tiling t, float u, float v) => 0.00012f * t.Fbm(u, 0f, 150f, 2, 91) + 0.00006f * t.Fbm(u, v, 20f, 2, 95);
        /// <summary>The height of a noise barrier's panel module (m, the suppliers' 500 mm) and the pitch of its louvres' columns along it (m: 8 to a 1960 mm panel, 10 to a 2460 mm one).</summary>
        public const float LouvreModule = 0.5f, LouvreColumn = 0.245f;

        /// <summary>
        /// A louvred sheet (estimated from close photos of stock panels, REPORT.md: no standard sets the stamping): eight rows of louvres 52 mm apart from 70 mm over the module's foot, each a hood
        /// 175 mm long and 20 mm high raised 3.5 mm at its lower edge, where it is open, and flush at its upper; the folded edges of two modules meet at a groove 8 mm wide and 3 mm deep.
        /// </summary>
        private static float LouvreRelief(Tiling t, float u, float v)
        {
            float joint = Math.Abs(Wrap(u, LouvreModule));
            float h = joint < 0.004f ? -0.003f * (1f - joint / 0.004f) : 0f;
            float row = (u - 0.07f) / 0.052f;
            int k = (int)Math.Floor(row);
            float within = (row - k) * 0.052f;
            if (k >= 0 && k < 8 && within < 0.02f)
                h += 0.0035f * (1f - within / 0.02f) * Smooth01((v - 0.035f) / 0.008f) * Smooth01((0.21f - v) / 0.008f);
            return h + 0.00004f * t.Fbm(u, v, 300f, 2, 81);
        }

        /// <summary>The slot under each louvre's open edge, seen into: dark.</summary>
        private static float LouvreShade(float u, float v)
        {
            if (v < 0.04f || v > 0.205f)
                return 1f;
            for (int k = 0; k < 8; k++)
            {
                float edge = 0.07f + 0.052f * k;
                if (u >= edge - 0.004f && u < edge)
                    return 0.35f;
            }

            return 1f;
        }

        /// <summary>The height of a cement panel (m, the suppliers' 4000 x 600 x 100 mm panels).</summary>
        public const float CementModule = 0.6f;

        /// <summary>
        /// Whether (u, v) of a cement panel's module is in one of its slots (estimated from a supplier's photos, REPORT.md): ten rows 50 mm apart from 70 mm over the module's foot, slots 120 mm long and
        /// 18 mm high every 200 mm along, every other row shifted half that.
        /// </summary>
        private static float SlotDepth(float u, float v)
        {
            float row = (u - 0.07f) / 0.05f;
            int k = (int)Math.Floor(row);
            float up = (row - k) * 0.05f;
            if (k < 0 || k > 9 || up > 0.018f)
                return 0f;
            float along = Math.Abs(Wrap(v - (k % 2 == 0 ? 0.1f : 0f), 0.2f));
            if (along > 0.06f)
                return 0f;
            // The slot's ends and edges rounded over 3 mm, its floor 8 mm in.
            float edge = Math.Min(Math.Min(up, 0.018f - up), 0.06f - along);
            return SlotFloor * Smooth01(edge / 0.003f);
        }

        private static float CementRelief(Tiling t, float u, float v)
        {
            float joint = Math.Abs(Wrap(u, CementModule));
            return 0.0003f * t.Fbm(u, v, 40f, 3, 92) - SlotDepth(u, v) - (joint < 0.003f ? 0.003f * (1f - joint / 0.003f) : 0f);
        }

        private const float SlotFloor = 0.008f;

        private static float CementShade(float u, float v) => 1f - 0.55f * SlotDepth(u, v) / SlotFloor;

        /// <summary>A brick's length with its joint (m) and a course's height with its joint: the standard brick of 240 x 115 x 53 mm laid with 10 mm joints.</summary>
        public const float BrickPitch = 0.25f, BrickCourse = 0.063f;

        /// <summary>How far (u up, v along) of a brick face is from the nearest joint (m), and the brick it is in (its middle, for its own shade).</summary>
        private static (float d, float cu, float cv) BrickAt(float u, float v)
        {
            int course = (int)Math.Floor(u / BrickCourse);
            float up = u - course * BrickCourse, shift = (course & 1) * BrickPitch * 0.5f, along = v + shift;
            int brick = (int)Math.Floor(along / BrickPitch);
            float w = along - brick * BrickPitch;
            float d = Math.Min(Math.Min(w, BrickPitch - 0.01f - w), Math.Min(up, BrickCourse - 0.01f - up));
            return (d, (course + 0.5f) * BrickCourse, (brick + 0.5f) * BrickPitch - shift);
        }

        /// <summary>Bricks a little uneven, the joints 5 mm in, their edges rounded over 2 mm.</summary>
        private static float BrickRelief(Tiling t, float u, float v)
        {
            (float d, _, _) = BrickAt(u, v);
            return d < 0f ? -0.005f : -0.005f * (1f - Smooth01(d / 0.002f)) + 0.0004f * t.Fbm(u, v, 30f, 3, 93);
        }

        /// <summary>Each brick its own shade, the joints darker.</summary>
        private static float BrickShade(Tiling t, float u, float v)
        {
            (float d, float cu, float cv) = BrickAt(u, v);
            return d < 0f ? 0.62f : 0.84f + 0.14f * t.Fbm(cu, cv, 9f, 1, 94);
        }

        /// <summary>Soil in a planter: clods (up to 1 cm) and a few pebbles.</summary>
        private static float SoilRelief(Tiling t, float u, float v)
        {
            float h = 0.004f * t.Fbm(u, v, 20f, 4, 31);
            (float d, float value) = t.Cell(u, v, 0.02f, 32);
            if (value > 0.85f && d < 0.004f)
                h += 0.003f * (float)Math.Sqrt(1f - (d / 0.004f) * (d / 0.004f));
            return h;
        }

        // ------------------------------------------------------------------ baking

        private static DetailSurface Bake(DetailKind kind, int width, int height, Tiling t, Func<float, float, float> relief, Func<float, float, (float metal, float coat, float smooth)> mask, float cavityLow,
            Func<float, float, float> colour = null, Func<float, float, float> alpha = null)
        {
            var s = new DetailSurface
            {
                Kind = kind, Width = width, Height = height, SizeU = t.SizeU, SizeV = t.SizeV,
                Relief = new float[width * height], Normals = new float[width * height * 3], Shade = new float[width * height],
                Metal = new float[width * height], Coat = new float[width * height], Smooth = new float[width * height], Alpha = alpha != null ? new float[width * height] : null
            };
            float du = t.SizeU / width, dv = t.SizeV / height;
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float u = (i + 0.5f) * du, v = (j + 0.5f) * dv;
                    int k = j * width + i;
                    s.Relief[k] = relief(u, v);
                    (float metal, float coat, float smooth) = mask(u, v);
                    s.Metal[k] = metal;
                    s.Coat[k] = coat;
                    s.Smooth[k] = Math.Max(0f, Math.Min(1f, smooth));
                    if (alpha != null)
                        s.Alpha[k] = alpha(u, v);
                }
            }

            // The normals from the height's slope (central differences, the tile repeating), the cavity from how far a texel is under the mean of its neighbourhood.
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    float hx = (H(s, i + 1, j) - H(s, i - 1, j)) / (2f * du), hy = (H(s, i, j + 1) - H(s, i, j - 1)) / (2f * dv);
                    Vec3 n = MeshBuilder.Normalize(new Vec3(-hx, -hy, 1f));
                    int k = (j * width + i) * 3;
                    s.Normals[k] = n.X;
                    s.Normals[k + 1] = n.Y;
                    s.Normals[k + 2] = n.Z;
                    float around = 0f;
                    for (int dj = -2; dj <= 2; dj++)
                    {
                        for (int di = -2; di <= 2; di++)
                            around += H(s, i + di, j + dj);
                    }

                    float below = around / 25f - H(s, i, j);
                    float own = colour != null ? colour((i + 0.5f) * du, (j + 0.5f) * dv) : 1f;
                    s.Shade[j * width + i] = Math.Max(cavityLow * 0.6f, Math.Min(1f, own * Math.Max(cavityLow, Math.Min(1f, 1f - below * 120f))));
                }
            }

            return s;
        }

        /// <summary>
        /// The alpha of each mip level of a cut-out map (level 0 the map's own, each next half the size each way): averaged from the level above, then the texels with the most of it (ties broken by
        /// a 4 x 4 ordered dither) whole and the others nothing, as many whole as level 0 has over the cutoff. A thin wire averages to less than the cutoff, and a far level to the same value all over:
        /// cut at the cutoff the mesh would vanish a few metres away or turn into a solid sheet; so it thins out into a dither, a haze as a real one does.
        /// </summary>
        public static List<float[]> CoverageMips(float[] alpha, int width, int height, float cutoff)
        {
            var levels = new List<float[]> { alpha };
            int over = 0;
            foreach (float a in alpha)
                over += a > cutoff ? 1 : 0;
            float coverage = over / (float)alpha.Length;
            float[] above = alpha;
            int w = width, h = height;
            while (w > 1 || h > 1)
            {
                int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
                var level = new float[nw * nh];
                for (int j = 0; j < nh; j++)
                {
                    for (int i = 0; i < nw; i++)
                    {
                        int i0 = Math.Min(w - 1, i * 2), i1 = Math.Min(w - 1, i * 2 + 1), j0 = Math.Min(h - 1, j * 2), j1 = Math.Min(h - 1, j * 2 + 1);
                        level[j * nw + i] = (above[j0 * w + i0] + above[j0 * w + i1] + above[j1 * w + i0] + above[j1 * w + i1]) * 0.25f;
                    }
                }

                var order = new int[level.Length];
                var key = new float[level.Length];
                for (int k = 0; k < level.Length; k++)
                {
                    order[k] = k;
                    key[k] = -(level[k] + 1e-4f * Bayer4[(k / nw % 4) * 4 + k % nw % 4] / 16f);
                }

                Array.Sort(key, order);
                int keep = (int)Math.Round(coverage * level.Length);
                var cut = new float[level.Length];
                for (int r = 0; r < keep; r++)
                    cut[order[r]] = 1f;
                levels.Add(cut);
                above = level;
                w = nw;
                h = nh;
            }

            return levels;
        }

        private static readonly int[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        private static float H(DetailSurface s, int i, int j) => s.Relief[((j % s.Height + s.Height) % s.Height) * s.Width + (i % s.Width + s.Width) % s.Width];

        /// <summary>
        /// Noise and cells that repeat with a tile of SizeU by SizeV metres: a frequency (cycles per metre) or a cell size is rounded to a whole number of them across the tile, and the lattice is
        /// taken modulo that number, so the tile's last texel meets its first.
        /// </summary>
        private readonly struct Tiling
        {
            public readonly float SizeU, SizeV;

            public Tiling(float sizeU, float sizeV)
            {
                SizeU = sizeU;
                SizeV = sizeV;
            }

            private static float Hash(int x, int y, int seed)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    return (h ^ (h >> 16)) / (float)uint.MaxValue;
                }
            }

            private static int Mod(int a, int n) => (a % n + n) % n;

            private static float Value(float x, float y, int px, int py, int seed)
            {
                int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
                float fx = x - x0, fy = y - y0;
                fx = fx * fx * (3f - 2f * fx);
                fy = fy * fy * (3f - 2f * fy);
                int xa = Mod(x0, px), xb = Mod(x0 + 1, px), ya = Mod(y0, py), yb = Mod(y0 + 1, py);
                float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
                return (a + (b - a) * fx) * (1f - fy) + (c + (d - c) * fx) * fy;
            }

            /// <summary>Fractal noise of about the frequency (cycles per metre), -1 to 1.</summary>
            public float Fbm(float u, float v, float frequency, int octaves, int seed)
            {
                int px = Math.Max(1, (int)Math.Round(frequency * SizeU)), py = Math.Max(1, (int)Math.Round(frequency * SizeV));
                float x = u / SizeU * px, y = v / SizeV * py;
                float sum = 0f, amplitude = 1f, total = 0f;
                for (int o = 0; o < octaves; o++)
                {
                    sum += amplitude * (Value(x, y, px, py, seed + o * 17) * 2f - 1f);
                    total += amplitude;
                    amplitude *= 0.5f;
                    x *= 2f;
                    y *= 2f;
                    px *= 2;
                    py *= 2;
                }

                return sum / total;
            }

            /// <summary>The distance (m) to the nearest of the points scattered one in each cell of about the size, and that point's value (0 to 1).</summary>
            public (float d, float value) Cell(float u, float v, float size, int seed)
            {
                (float d, float value, _, _, _) = CellFacet(u, v, size, seed);
                return (d, value);
            }

            /// <summary>As Cell, with where the place is from the point in cells (-1 to 1) and a second value of the point.</summary>
            public (float d, float value, float gu, float gv, float other) CellFacet(float u, float v, float size, int seed)
            {
                int nu = Math.Max(1, (int)Math.Round(SizeU / size)), nv = Math.Max(1, (int)Math.Round(SizeV / size));
                float su = SizeU / nu, sv = SizeV / nv;
                int cx = (int)Math.Floor(u / su), cy = (int)Math.Floor(v / sv);
                float best = float.MaxValue, value = 0f, gu = 0f, gv = 0f, other = 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy, wx = Mod(x, nu), wy = Mod(y, nv);
                        float px = (x + Hash(wx, wy, seed)) * su, py = (y + Hash(wx, wy, seed + 1)) * sv;
                        float d = (float)Math.Sqrt((u - px) * (u - px) + (v - py) * (v - py));
                        if (d < best)
                        {
                            best = d;
                            value = Hash(wx, wy, seed + 2);
                            other = Hash(wx, wy, seed + 3);
                            gu = (u - px) / su;
                            gv = (v - py) / sv;
                        }
                    }
                }

                return (best, value, gu, gv, other);
            }
        }
    }
}
