using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What covers a tree pit (0.12.49): nothing (the soil), a cast grate with radial slots (iron or resin composite), or a cut piece of glass-fibre grating.</summary>
    public enum GrateKind
    {
        None,
        Cast,
        Grating
    }

    /// <summary>A row of street trees in pits (行道树树池, 0.12.49): the pits' size and kerbs, what covers them, the trees and their guards.</summary>
    public sealed class TreePitOptions
    {
        /// <summary>The clear inside of a pit (m): DB11/T 839-2025 5.1.3, "矩形或正方形行道树树池边框内距应不小于 120 cm"; the grates are made 1.2, 1.25 and 1.5 m.</summary>
        public float Size = 1.2f;

        /// <summary>How far the pit's kerb stands over the footway (m): Barriers.FlushTop for one level with it (CJJ/T 75-2023 4.3.6), or raised.</summary>
        public float KerbTop = Barriers.FlushTop;

        public GrateKind Grate = GrateKind.Cast;
        public SolidColour GrateColour = SolidColour.CastIron, KerbColour = SolidColour.Granite;

        /// <summary>A four-pole guard (四角支撑) round each tree, which is then a young one.</summary>
        public bool Guard;

        public string Species = "plane";
        public float Spacing = 6f;
    }

    /// <summary>What grows under the trees, or alone, in a green belt or a tree strip (0.12.49): the game's low trimmed hedge, its cultivated bushes as shrub balls, its flowering bushes, or nothing.</summary>
    public enum BeltPlants
    {
        Hedge,
        Shrubs,
        Flowers,
        None
    }

    /// <summary>A planted bed along a road (0.12.49): a median or separator green belt (分车绿带), or a continuous tree strip in a footway (连续树池).</summary>
    public sealed class GreenBedOptions
    {
        /// <summary>The bed's clear width between its kerbs (m).</summary>
        public float Width = 1.5f;

        /// <summary>Its kerbs' width (0: none, the bed is the soil on the game's own median) and how far they stand over the ground (m).</summary>
        public float KerbWidth = 0.15f, KerbTop = 0.15f;

        public SolidColour KerbColour = SolidColour.Granite;
        public BeltPlants Under = BeltPlants.Hedge;

        /// <summary>The trees' species (GreenPlants.Trees; empty: none), their spacing (m) and whether they are young.</summary>
        public string Species = "";

        public float TreeSpacing = 8f;
        public bool YoungTrees;

        /// <summary>How far apart the shrub balls or the flowering bushes of a row are (m).</summary>
        public float Spacing = 3f;
    }

    /// <summary>
    /// The road greening of 0.12.49 (the owner: "道路绿化系列，包含普通常见道路绿化，还有高架路绿化花箱之类"; scratch/research-greening/REPORT.md): what the mod builds is only the hard parts, the pits and grates,
    /// the kerbs and edging, the planter boxes and their brackets, the tree guards; every tree, shrub, hedge and flower is the game's own (PropAnchor, GreenPlants, PlantSystem). Upright parts stand
    /// plumb; a straight module (a pit, a box) follows the run's slope along it and stands at its middle's height (Module). Sizes are quoted by clause where a standard sets them; the rest are the
    /// suppliers' or this mod's estimates from photos, and the templates say which. Pure.
    /// </summary>
    public static partial class Barriers
    {
        /// <summary>
        /// How far the top of a kerb or a grate that should be level with the footway stands over it (m). The game's footway cannot be cut: a pit is laid on it, and the soil under the grate's slots must
        /// show over the paving, not under it.
        /// </summary>
        public const float FlushTop = 0.035f;

        /// <summary>
        /// A straight piece's frame at a place of a run: O the place, R its right and A its way along (level), K the run's slope there: (x, y, z) is O + x R + z A, raised by y and by the slope along z.
        /// Sides stay plumb, the top follows the slope.
        /// </summary>
        private readonly struct Module
        {
            public readonly Vec3 O, R, A;
            public readonly float K;

            public Module(Vec3 o, Vec3 ahead, float slope)
            {
                O = o;
                A = ahead.HeadingXZ();
                R = A.RightXZ();
                K = slope;
            }

            public Vec3 At(float x, float y, float z) => O + R * x + A * z + Up * (y + K * z);

            public static Module On(IList<Vec3> line, IList<float> at, float s) => new Module(RoadMarkings.PointAt(line, at, s), HeadingAt(line, at, s), SlopeAt(line, at, s));
        }

        /// <summary>The rise over the run of the line's segment at s.</summary>
        private static float SlopeAt(IList<Vec3> line, IList<float> at, float s)
        {
            int i = 1;
            while (i < line.Count - 1 && at[i] < s)
                i++;
            float run = at[i] - at[i - 1];
            return run > 1e-4f ? (line[i].Y - line[i - 1].Y) / run : 0f;
        }

        /// <summary>
        /// A closed box in a module's frame between x0 and x1 across (either way round), y0..y1 up, between z0 and z1 along: six faces, its top and bottom on the slope. Texture coordinates in metres, v along its longer level side (a kerb's
        /// joints and a batten's fibres run along it).
        /// </summary>
        private static void Block(MeshBuilder mesh, Module m, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            if (x0 > x1)
                (x0, x1) = (x1, x0);
            if (z0 > z1)
                (z0, z1) = (z1, z0);
            bool alongX = x1 - x0 > z1 - z0;
            Vec3 top = MeshBuilder.Normalize(Up - m.A * m.K);
            (float, float) Flat(float x, float z) => alongX ? (z, x) : (x, z);
            foreach ((float y, Vec3 n) in new[] { (y1, top), (y0, top * -1f) })
            {
                (float ua, float va) = Flat(x0, z0);
                (float ub, float vb) = Flat(x1, z0);
                (float uc, float vc) = Flat(x1, z1);
                (float ud, float vd) = Flat(x0, z1);
                Quad(mesh, m.At(x0, y, z0), m.At(x1, y, z0), m.At(x1, y, z1), m.At(x0, y, z1), n, ua, va, ub, vb, uc, vc, ud, vd);
            }

            foreach ((float x, Vec3 n) in new[] { (x1, m.R), (x0, m.R * -1f) })
                Quad(mesh, m.At(x, y0, z0), m.At(x, y1, z0), m.At(x, y1, z1), m.At(x, y0, z1), n, y0, z0, y1, z0, y1, z1, y0, z1);
            foreach ((float z, Vec3 n) in new[] { (z1, m.A), (z0, m.A * -1f) })
                Quad(mesh, m.At(x0, y0, z), m.At(x1, y0, z), m.At(x1, y1, z), m.At(x0, y1, z), n, y0, x0, y0, x1, y1, x1, y1, x0);
        }

        /// <summary>A rectangle (across u0..u1, up v0..v1) for a swept part, its corners in order.</summary>
        /// <summary>A rectangle of a section (u across, v up), for Sweep and the runs' Along (the greening's and the railings' of 0.12.49).</summary>
        private static List<(float u, float v)> Rect(float u0, float u1, float v0, float v1) => new List<(float u, float v)> { (u0, v0), (u1, v0), (u1, v1), (u0, v1) };

        /// <summary>The part of a line from from to to metres along it (at: its points' distances, total: its length).</summary>
        private static List<Vec3> Trimmed(IList<Vec3> line, IList<float> at, float from, float to)
        {
            var part = new List<Vec3>();
            if (to - from < 0.01f)
                return part;
            part.Add(RoadMarkings.PointAt(line, at, from));
            for (int i = 0; i < line.Count; i++)
            {
                if (at[i] > from + 0.01f && at[i] < to - 0.01f)
                    part.Add(line[i]);
            }

            part.Add(RoadMarkings.PointAt(line, at, to));
            return part;
        }

        private static float YawOf(Vec3 ahead) => (float)Math.Atan2(ahead.X, ahead.Z);

        /// <summary>Whether a thing whose middle is s metres along a piece of a run (of total metres) belongs to it: one at the join of two pieces is the later one's, one at the very end the last's.</summary>
        private static bool Holds(float s, float total, bool last) => s >= -1e-4f && (s < total - 1e-4f || (last && s <= total + 1e-4f));

        /// <summary>
        /// Street trees in pits (行道树树池) every spacing metres along the line, which is the trees' line: each pit a square of the clear size inside a granite kerb 0.10 m wide (0.12 m raised), the soil
        /// in it, and over the soil, when the kerb is level with the footway, a grate in the kerb's ring (CJJ/T 75-2023 4.3.5: "树池应覆盖树池箅子，且应与人行路面齐平"); a raised pit is open, its soil
        /// 8 cm under its rim (DB11/T 839-2025 6.1.1.3: 8 to 12 cm). The tree is the game's, on the soil in the middle; a guard's four poles stand in the grate's corner holes (DB11/T 839-2025 5.9: four
        /// poles, 1.5 to 2 m high) and carry a square of battens round the trunk.
        /// </summary>
        public static List<MarkingShape> TreePits(IList<Vec3> line, TreePitOptions o, float phase, List<PropAnchor> props)
        {
            var kerb = new MarkingShape { Solid = o.KerbColour };
            var soil = new MarkingShape { Solid = SolidColour.Soil };
            var grate = new MarkingShape { Solid = o.GrateColour, Detail = o.Grate == GrateKind.Grating ? DetailKind.FrpGrating : DetailKind.TreeGrate };
            var guard = new MarkingShape { Solid = SolidColour.Wood, Detail = DetailKind.Timber };
            var shapes = new List<MarkingShape> { kerb, soil, grate, guard };
            if (line == null || line.Count < 2)
                return shapes;
            float a = Math.Max(1f, Math.Min(2f, o.Size)), top = Math.Max(FlushTop, Math.Min(0.2f, o.KerbTop));
            bool flush = top <= FlushTop + 1e-4f, grated = flush && o.Grate != GrateKind.None;
            float k = flush ? 0.10f : 0.12f, h = a * 0.5f, soilTop = grated ? FlushTop - GrateThickness : flush ? 0.01f : top - 0.08f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in MarkingStuds.Stations(total, Math.Max(3f, o.Spacing), phase))
            {
                Module m = Module.On(line, at, s);
                Block(kerb.Mesh, m, -h - k, -h, -FootSink, top, -h - k, h + k);
                Block(kerb.Mesh, m, h, h + k, -FootSink, top, -h - k, h + k);
                Block(kerb.Mesh, m, -h, h, -FootSink, top, -h - k, -h);
                Block(kerb.Mesh, m, -h, h, -FootSink, top, h, h + k);
                Block(soil.Mesh, m, -h, h, -FootSink, soilTop, -h, h);
                if (grated)
                {
                    if (o.Grate == GrateKind.Cast)
                        CastGrate(grate.Mesh, m, a, soilTop, top);
                    else
                        Grating(grate.Mesh, m, a, soilTop, top, phase + s);
                }

                if (o.Guard)
                    TreeGuard(guard.Mesh, m, a, soilTop);
                props?.Add(new PropAnchor { Local = m.At(0f, soilTop, 0f), Yaw = YawOf(m.A), Kind = GreenPlants.TreeKind(o.Species, o.Guard), Room = a });
            }

            return shapes;
        }

        /// <summary>How thick a grate is (m): the composite ones are 30 mm (a supplier's sheet), the cast ones 30 to 50 mm.</summary>
        public const float GrateThickness = 0.03f;

        /// <summary>A gap round the trunk in a cut grating (m, its side): the suppliers' photos show a square of about 0.4 m cut out.</summary>
        public const float GratingHole = 0.4f;

        /// <summary>
        /// A cast grate filling the pit's a x a, from y0 to y1: its top one quad that spans the grate's tile (DetailKind.TreeGrate: its slots, the trunk's hole and the corner holes cut out, so the soil
        /// shows through them), its edges on the tile's solid border.
        /// </summary>
        private static void CastGrate(MeshBuilder mesh, Module m, float a, float y0, float y1)
        {
            float h = a * 0.5f, t = DetailSurfaces.GrateTile;
            Vec3 up = MeshBuilder.Normalize(Up - m.A * m.K);
            Quad(mesh, m.At(-h, y1, -h), m.At(h, y1, -h), m.At(h, y1, h), m.At(-h, y1, h), up, 0f, 0f, t, 0f, t, t, 0f, t);
            // The edges take a little square of the border: solid all over.
            const float b0 = 0.01f, b1 = 0.04f;
            foreach ((float x, Vec3 n) in new[] { (h, m.R), (-h, m.R * -1f) })
                Quad(mesh, m.At(x, y0, -h), m.At(x, y1, -h), m.At(x, y1, h), m.At(x, y0, h), n, b0, b0, b1, b0, b1, b1, b0, b1);
            foreach ((float z, Vec3 n) in new[] { (h, m.A), (-h, m.A * -1f) })
                Quad(mesh, m.At(-h, y0, z), m.At(h, y0, z), m.At(h, y1, z), m.At(-h, y1, z), n, b0, b0, b1, b0, b1, b1, b0, b1);
        }

        /// <summary>
        /// A piece of glass-fibre grating filling the pit's a x a round a square hole for the trunk (GratingHole), from y0 to y1: four plates, its texture in metres along the whole run (from: where the
        /// pit's middle is along it) so the cells of the pits line up as cut from stock.
        /// </summary>
        private static void Grating(MeshBuilder mesh, Module m, float a, float y0, float y1, float from)
        {
            float h = a * 0.5f, g = Math.Min(GratingHole, a * 0.5f) * 0.5f;
            Vec3 up = MeshBuilder.Normalize(Up - m.A * m.K);
            void Top(float x0, float x1, float z0, float z1) =>
                Quad(mesh, m.At(x0, y1, z0), m.At(x1, y1, z0), m.At(x1, y1, z1), m.At(x0, y1, z1), up, x0, from + z0, x1, from + z0, x1, from + z1, x0, from + z1);
            // A side x = c from z0 to z1 facing n, and a side z = c from x0 to x1.
            void SideX(float c, float z0, float z1, Vec3 n) => Quad(mesh, m.At(c, y0, z0), m.At(c, y1, z0), m.At(c, y1, z1), m.At(c, y0, z1), n, from + z0, y0, from + z0, y1, from + z1, y1, from + z1, y0);
            void SideZ(float c, float x0, float x1, Vec3 n) => Quad(mesh, m.At(x0, y0, c), m.At(x1, y0, c), m.At(x1, y1, c), m.At(x0, y1, c), n, x0, y0, x1, y0, x1, y1, x0, y1);

            // Two plates the whole length beside the hole and two short ones before and after it; the outer edges and the hole's.
            Top(-h, -g, -h, h);
            Top(g, h, -h, h);
            Top(-g, g, -h, -g);
            Top(-g, g, g, h);
            SideX(h, -h, h, m.R);
            SideX(-h, -h, h, m.R * -1f);
            SideZ(h, -h, h, m.A);
            SideZ(-h, -h, h, m.A * -1f);
            SideX(-g, -g, g, m.R);
            SideX(g, -g, g, m.R * -1f);
            SideZ(-g, -g, g, m.A);
            SideZ(g, -g, g, m.A * -1f);
        }

        /// <summary>
        /// A tree guard (四角支撑, DB11/T 839-2025 5.9.1 to 5.9.3: four poles, 1.5 to 2 m high): peeled poles 50 mm across (a supplier's 5 x 200 cm) from 0.3 m down in the soil at the pit's corner holes
        /// (0.4 of its side from its middle each way) leaning in to 1.65 m, and a square of battens 50 mm on them at 1.4 to 1.55 m round the trunk, one pair on the other as nailed, 0.25 m clear inside for
        /// the trunk (the game's trunk is not known here; the mod does not tie it).
        /// </summary>
        private static void TreeGuard(MeshBuilder mesh, Module m, float a, float soilTop)
        {
            const float r = 0.025f, bottom = -0.3f, peak = 1.65f, apex = 0.14f, batten = 0.05f;
            float c = 0.4f * a;
            float Lean(float y) => c + (apex - c) * (y - bottom) / (peak - bottom);
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                    Solids.Tube(mesh, m.At(sx * c, soilTop + bottom, sz * c), m.At(sx * apex, soilTop + peak, sz * apex), r, r, 8, false, true);
            }

            // The upper pair along the run, the lower pair across it, each on the poles' outer sides and 8 mm into them (nailed).
            foreach ((float y, bool along) in new[] { (1.50f, true), (1.45f, false) })
            {
                float q = Lean(y), off = q + r + batten * 0.5f - 0.008f, reach = q + r + 0.04f;
                foreach (float side in new[] { -1f, 1f })
                {
                    if (along)
                        Block(mesh, m, side * off - batten * 0.5f, side * off + batten * 0.5f, soilTop + y - batten * 0.5f, soilTop + y + batten * 0.5f, -reach, reach);
                    else
                        Block(mesh, m, -reach, reach, soilTop + y - batten * 0.5f, soilTop + y + batten * 0.5f, side * off - batten * 0.5f, side * off + batten * 0.5f);
                }
            }
        }

        /// <summary>
        /// A planted bed along the line, which is its middle: kerbs of kerbWidth either side of its clear width (none for 0: the soil on the game's own median), returned across its ends at the ends of the
        /// whole run (the side kerbs and the soil stop at the returns), the soil in it to soilTop; the texture of the kerbs goes on along the run (phase) so their 1 m joints are in step.
        /// </summary>
        private static void Bed(MeshBuilder kerbs, MeshBuilder soil, IList<Vec3> line, IList<float> at, float total, float width, float kerbWidth, float kerbTop, float soilTop, float phase, bool first, bool last)
        {
            float k = Math.Max(0f, kerbWidth), half = width * 0.5f, d0 = first ? k : 0f, d1 = last ? k : 0f;
            List<Vec3> inner = Trimmed(line, at, d0, total - d1);
            if (inner.Count >= 2)
            {
                if (k > 0f)
                {
                    Sweep(kerbs, inner, Rect(half, half + k, -FootSink, kerbTop), true, true, true, phase + d0);
                    Sweep(kerbs, inner, Rect(-half - k, -half, -FootSink, kerbTop), true, true, true, phase + d0);
                }

                Sweep(soil, inner, Rect(-half, half, -FootSink, soilTop), true, true, true, phase + d0);
            }

            if (!(k > 0f))
                return;
            if (first)
                Block(kerbs, Module.On(line, at, 0f), -half - k, half + k, -FootSink, kerbTop, 0f, k);
            if (last)
                Block(kerbs, Module.On(line, at, total), -half - k, half + k, -FootSink, kerbTop, -k, 0f);
        }

        /// <summary>
        /// A green belt (分车绿带) or a continuous tree strip (连续树池) along the line, which is its middle: the bed (Bed) and its plants, all the game's, laid out along the whole run (phase, whole: this
        /// piece's start along it and its length) so that the pieces go on in step. The trees in its middle, the run's ends kept 0.75 m clear (CJJ/T 75-2023 4.2.2: "分车绿带内乔木树干中心距路缘石内侧水平
        /// 投影距离不宜小于 0.75m"), as many as fit at the spacing, centred; under them, in the spans between them (1 m clear of each trunk) or along all the bed: the game's low trimmed hedge in rows of
        /// pieces end to end, a chain centred in each span and a shrub ball in what is left at its ends if there is room; shrub balls or flowering bushes in rows at their spacing, staggered.
        /// </summary>
        public static List<MarkingShape> GreenBelt(IList<Vec3> line, GreenBedOptions o, float phase, float whole, bool first, bool last, List<PropAnchor> props)
        {
            var kerbs = new MarkingShape { Solid = o.KerbColour };
            var soil = new MarkingShape { Solid = SolidColour.Soil };
            var shapes = new List<MarkingShape> { kerbs, soil };
            if (line == null || line.Count < 2)
                return shapes;
            float w = Math.Max(1f, Math.Min(8f, o.Width)), k = Math.Max(0f, Math.Min(0.3f, o.KerbWidth));
            float top = k > 0f ? Math.Max(FlushTop, Math.Min(0.3f, o.KerbTop)) : 0f;
            float soilTop = k > 0f ? Math.Max(0.01f, top - (top > 0.1f ? 0.05f : top > FlushTop + 1e-4f ? 0.02f : 0.025f)) : 0.02f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + total;
            Bed(kerbs.Mesh, soil.Mesh, line, at, total, w, k, top, soilTop, phase, first, last);
            if (props == null)
                return shapes;

            Vec3 Along(float d) => d < 0f ? line[0] + (line[1] - line[0]).HeadingXZ() * d
                : d > total ? line[line.Count - 1] + (line[line.Count - 1] - line[line.Count - 2]).HeadingXZ() * (d - total) : RoadMarkings.PointAt(line, at, d);

            void Put(float g, float x, string kind, float room)
            {
                float s = g - phase;
                if (!Holds(s, total, last))
                    return;
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                float y = soilTop;
                if (kind == GreenPlants.Hedge)
                {
                    // A straight hedge piece along the chord of its 4.2 m of the line, half way between the chord's middle and the line: on a bend its ends and its middle are as far off the line.
                    Vec3 a = Along(s - GreenPlants.HedgeLength * 0.5f), b = Along(s + GreenPlants.HedgeLength * 0.5f);
                    ahead = (b - a).HeadingXZ();
                    p = (p + (a + b) * 0.5f) * 0.5f;
                    // Sunk 8 cm into the soil: its 4.2 m on a slope of 4 % shows no gap under its ends.
                    y -= 0.08f;
                }

                props.Add(new PropAnchor { Local = p + ahead.RightXZ() * x + Up * y, Yaw = YawOf(ahead), Kind = kind, Room = room });
            }

            var trees = new List<float>();
            float clear = k + 0.75f;
            if (GreenPlants.PrefabsOf(GreenPlants.TreeKind(o.Species, false)).Count > 0 && whole - 2f * clear >= 0f)
            {
                float spacing = Math.Max(4f, o.TreeSpacing);
                int n = (int)Math.Floor((whole - 2f * clear) / spacing + 1e-4f) + 1;
                float start = (whole - (n - 1) * spacing) * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    trees.Add(start + i * spacing);
                    Put(start + i * spacing, 0f, GreenPlants.TreeKind(o.Species, o.YoungTrees), w);
                }
            }

            float from = k + 0.05f, to = whole - k - 0.05f;
            switch (o.Under)
            {
                case BeltPlants.Hedge:
                {
                    int rows = Math.Max(1, (int)Math.Floor((w + 0.06f) / 1.3f));
                    var spans = new List<(float, float)>();
                    float s0 = from;
                    foreach (float t in trees)
                    {
                        spans.Add((s0, t - 1f));
                        s0 = t + 1f;
                    }

                    spans.Add((s0, to));
                    foreach ((float a, float b) in spans)
                    {
                        float length = b - a, pitch = GreenPlants.HedgeLength - 0.1f;
                        int pieces = (int)Math.Floor((length - 0.1f) / pitch + 1e-4f);
                        float chain = pieces > 0 ? pitch * pieces + 0.1f : 0f, spare = (length - chain) * 0.5f;
                        for (int r = 0; r < rows; r++)
                        {
                            float x = (r - (rows - 1) * 0.5f) * (GreenPlants.HedgeWidth + 0.02f);
                            for (int i = 0; i < pieces; i++)
                                Put(a + spare + 0.05f + pitch * (i + 0.5f), x, GreenPlants.Hedge, GreenPlants.HedgeWidth);
                            // What a chain leaves at its ends, or a span too short for a piece: shrub balls, the nearest of the game's bushes to a clipped hedge.
                            if (pieces == 0 && length >= 1.2f)
                            {
                                int balls = Math.Max(1, (int)Math.Floor(length / 1.5f));
                                for (int i = 0; i < balls; i++)
                                    Put(a + length * (i + 0.5f) / balls, x, GreenPlants.Shrub, Math.Min(length / balls, GreenPlants.HedgeWidth));
                            }
                            else if (pieces > 0 && spare >= 1.2f)
                            {
                                Put(a + spare * 0.5f, x, GreenPlants.Shrub, spare);
                                Put(b - spare * 0.5f, x, GreenPlants.Shrub, spare);
                            }
                        }
                    }

                    break;
                }
                case BeltPlants.Shrubs:
                case BeltPlants.Flowers:
                {
                    bool shrubs = o.Under == BeltPlants.Shrubs;
                    float spacing = Math.Max(1f, o.Spacing), edge = shrubs ? 0.7f : 0.5f;
                    int rows = shrubs ? (w >= 3f ? 2 : 1) : Math.Max(1, (int)Math.Floor(w / 1.6f));
                    for (int r = 0; r < rows; r++)
                    {
                        float x = (r - (rows - 1) * 0.5f) * w / rows, lead = from + edge + (r % 2) * spacing * 0.5f;
                        int n = (int)Math.Floor((to - edge - lead) / spacing + 1e-4f) + 1;
                        for (int i = 0; i < n; i++)
                        {
                            float g = lead + i * spacing;
                            if (!trees.Exists(t => Math.Abs(t - g) < 1f))
                                Put(g, x, shrubs ? GreenPlants.Shrub : GreenPlants.Flower, Math.Min(spacing, w / rows));
                        }
                    }

                    break;
                }
            }

            return shapes;
        }
    }
}
