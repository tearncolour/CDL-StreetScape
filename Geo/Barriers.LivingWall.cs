using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>How a vertical greening wall's faces are built: troughs of one depth with the wall's face between them, troughs that step back as they rise, or a steel trellis in front of the face over a foot trough.</summary>
    public enum WallStyle
    {
        Flush,
        Stepped,
        Trellis
    }

    /// <summary>A vertical greening wall (垂直绿化墙, 花墙): its height and nominal thickness, the style, how far apart the troughs and the frame's posts are, one face or both, and what grows on it.</summary>
    public sealed class LivingWallOptions
    {
        /// <summary>The wall's height over the ground (m): 1.5 to 3.0. This mod's choice, no standard sets it.</summary>
        public float Height = 2.5f;

        /// <summary>The thickness asked for (m), 0.3 to 0.6: a flush wall's body is what its troughs leave, a stepped or trellis wall's troughs make it up (never shallower than 0.12 m, so a thin two-faced wall is a little thicker than asked). LivingWallLayout.Width is what is built.</summary>
        public float Thickness = 0.45f;

        public WallStyle Style;

        /// <summary>The distance between the troughs' bottoms (m), 0.5 to 0.7.</summary>
        public float TroughPitch = 0.6f;

        /// <summary>The distance between the frame's posts (m), 2 to 3.</summary>
        public float PostSpacing = 2.5f;

        public bool BothFaces, TopPlants = true, Mixed;

        /// <summary>The game's plants of the foot trough: GreenPlants.Flower or GreenPlants.Shrub.</summary>
        public string FootPlants = GreenPlants.Flower;

        /// <summary>The face's colour: a blossom colour (SolidColour.Blossom*, WallFoliage: leaves with blossoms of it on them) or Green for leaves only. The far level's slab is plain green with a band of the blossoms' colour.</summary>
        public SolidColour Skin = SolidColour.BlossomMixed;

        /// <summary>How much of it is built (BarrierLod): 0 all, 1 no soil, pipe or trellis wires, 2 a green slab with a band.</summary>
        public int Level;
    }

    /// <summary>
    /// Where the parts of a living wall are across and up, worked out once from its options (Barriers.LivingWallLayout): the concrete body's extent across (u, + to the line's right), the troughs' heights and
    /// depths, the footprint's width. The wall is centred on its line: the planted face of a one-faced wall is the one toward the traffic.
    /// </summary>
    public sealed class LivingWallLayout
    {
        public float Height, Core, Width, CoreLo, CoreHi, Trellis;
        public int Faces;

        /// <summary>The troughs from the bottom: the height of each one's bottom over the ground and how far it stands out from the face panel (m).</summary>
        public readonly List<(float Y, float Depth)> Rows = new List<(float, float)>();

        /// <summary>The faces' signs: +1 (toward the line's right) or -1.</summary>
        public readonly List<int> Sides = new List<int>();

        /// <summary>The u of a face's panel (its surface of the body) and the way out of it.</summary>
        public float Base(int fs) => Faces == 2 ? fs * Core * 0.5f : fs > 0 ? CoreHi : CoreLo;
    }

    public static partial class Barriers
    {
        /// <summary>The plinth's top over the ground (m), the foliage panel's thickness, a trough's height, the cap's height (m).</summary>
        public const float WallPlinth = 0.20f, WallSkin = 0.02f, WallTrough = 0.16f, WallCap = 0.06f;

        /// <summary>The most plants a run's foot troughs take, and the most pots on its top (the game's plants are entities: a long wall is thinned, not refused).</summary>
        public const int WallPlantCap = 300, WallPotCap = 150;

        /// <summary>The pots on the top stand every 1.5 m of the whole run, each with a room of 0.15 m (the narrowest of the game's pots, GreenPlants.Pot).</summary>
        public const float WallPotGap = 1.5f, WallPotRoom = 0.15f;

        /// <summary>The wall's dimensions for options and the side the traffic is on (+1 right, -1 left): see LivingWallLayout.</summary>
        public static LivingWallLayout WallLayout(LivingWallOptions o, int side)
        {
            float height = Math.Max(1.5f, Math.Min(3f, o.Height)), pitch = Math.Max(0.5f, Math.Min(0.7f, o.TroughPitch)), thick = Math.Max(0.3f, Math.Min(0.6f, o.Thickness));
            int faces = o.BothFaces ? 2 : 1;
            var l = new LivingWallLayout { Height = height, Faces = faces };
            int fs = side >= 0 ? 1 : -1;
            // A trough must clear the cap, the drip pipe under it and the soil mound: its bottom at most 0.2 m under the cap.
            int rows = Math.Max(1, (int)Math.Floor((height - WallCap - 0.2f - WallPlinth) / pitch + 1e-4f) + 1);
            float outer = Math.Max(0.12f, (thick - 0.10f) / faces - WallSkin);
            float flush = 0.15f;
            switch (o.Style)
            {
                case WallStyle.Stepped:
                    l.Core = 0.10f;
                    for (int k = 0; k < rows; k++)
                        l.Rows.Add((WallPlinth + k * pitch, rows == 1 ? outer : outer + (0.08f - outer) * k / (rows - 1)));
                    break;
                case WallStyle.Trellis:
                    l.Core = 0.10f;
                    l.Rows.Add((WallPlinth, outer));
                    l.Trellis = WallSkin + 0.05f;
                    break;
                default:
                    l.Core = Math.Max(0.10f, thick - faces * (WallSkin + flush));
                    for (int k = 0; k < rows; k++)
                        l.Rows.Add((WallPlinth + k * pitch, flush));
                    break;
            }

            float deepest = 0f;
            foreach ((float _, float d) in l.Rows)
                deepest = Math.Max(deepest, d);
            l.Width = l.Core + faces * (WallSkin + deepest);
            if (faces == 2)
            {
                l.CoreLo = -l.Core * 0.5f;
                l.CoreHi = l.Core * 0.5f;
                l.Sides.Add(1);
                l.Sides.Add(-1);
            }
            else
            {
                // The back against the far side of the footprint, the planted face toward the traffic.
                l.CoreLo = fs > 0 ? -l.Width * 0.5f : l.Width * 0.5f - l.Core;
                l.CoreHi = l.CoreLo + l.Core;
                l.Sides.Add(fs);
            }

            return l;
        }

        /// <summary>A section's rectangle from a to b across (b the outer edge: the edge there runs up, so a texture on a face stands upright whichever face it is), v0 to v1 up.</summary>
        private static List<(float u, float v)> FaceRect(float a, float b, float v0, float v1) => new List<(float u, float v)> { (a, v0), (b, v0), (b, v1), (a, v1) };

        /// <summary>
        /// A vertical greening wall (垂直绿化墙, 花墙) along the line, which is its middle: a concrete plinth and body, on each planted face a thin foliage panel and rows of troughs (plastic with soil in them), galvanised
        /// steel posts of the frame every PostSpacing proud of the faces, a steel cap, and a drip pipe under it. Flush: troughs of one depth every pitch; Stepped: troughs that step back as they rise; Trellis: a
        /// foot trough and a steel trellis (wires every 0.4 m) 50 mm off the face, fixed to the posts. The plants are the game's (PropAnchor): flowering bushes or shrubs in the foot trough, flower pots on the cap
        /// (WallPlants). What is between the troughs is the mod's own foliage surface (DetailKind.Foliage, WallFoliage: leaves with blossoms of the colour asked for), because no prefab of the game fits a face 0.3 to 0.6 m
        /// thick (the dump's thinnest greenery is a pot 0.52 m across, its hedge pieces are 1.1 m deep, its "climbing frames" are playground gear 2 to 4 m high). Sizes are this mod's estimates of the usual modular and
        /// trellis systems. Level 1 leaves out the soil, the pipe and the trellis wires; level 2 is the body, a green slab with a band of the blossoms' colour and the cap.
        /// </summary>
        public static List<MarkingShape> LivingWall(IList<Vec3> line, LivingWallOptions o, int side, float phase, float whole, bool first, bool last, List<PropAnchor> props)
        {
            var concrete = new MarkingShape { Solid = SolidColour.Concrete, Detail = DetailKind.Concrete };
            bool far = o.Level >= BarrierLod.Coarse;
            var skin = new MarkingShape { Solid = far ? SolidColour.Green : o.Skin, Detail = far ? DetailKind.None : DetailKind.Foliage };
            var trough = new MarkingShape { Solid = SolidColour.ResinGreen, Detail = DetailKind.Plastic };
            var soil = new MarkingShape { Solid = SolidColour.Soil, Detail = DetailKind.Soil };
            var steel = new MarkingShape { Solid = SolidColour.Metal, Detail = DetailKind.Galvanised };
            var dark = new MarkingShape { Solid = SolidColour.DarkGray };
            var band = new MarkingShape { Solid = WallFoliage.Is(o.Skin) ? o.Skin : SolidColour.Green };
            var shapes = new List<MarkingShape> { concrete, skin, trough, soil, steel, dark, band };
            if (line == null || line.Count < 2)
                return shapes;
            LivingWallLayout l = WallLayout(o, side);
            float h = l.Height, half = l.Width * 0.5f, capBottom = h - WallCap;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + total;

            Sweep(concrete.Mesh, line, Rect(-half - 0.03f, half + 0.03f, -Embed, WallPlinth), true, first, last, phase);
            Sweep(concrete.Mesh, line, Rect(l.CoreLo, l.CoreHi, WallPlinth - 0.02f, capBottom + 0.02f), true, first, last, phase);
            Sweep(steel.Mesh, line, Rect(-half - 0.02f, half + 0.02f, capBottom, h), true, first, last, phase);
            foreach (int fs in l.Sides)
            {
                float b = l.Base(fs);
                Sweep(skin.Mesh, line, FaceRect(b, b + fs * WallSkin, WallPlinth, capBottom), true, first, last, phase);
                if (far)
                {
                    if (WallFoliage.Is(o.Skin))
                        Sweep(band.Mesh, line, FaceRect(b + fs * (WallSkin - 0.002f), b + fs * (WallSkin + 0.006f), h * 0.5f - 0.15f, h * 0.5f + 0.15f), true, first, last, phase);
                    continue;
                }

                foreach ((float y, float depth) in l.Rows)
                {
                    Sweep(trough.Mesh, line, FaceRect(b + fs * (WallSkin - 0.01f), b + fs * (WallSkin + depth), y, y + WallTrough), true, first, last, phase);
                    if (o.Level == BarrierLod.Full)
                        Sweep(soil.Mesh, line, FaceRect(b + fs * (WallSkin + 0.02f), b + fs * (WallSkin + depth - 0.02f), y + WallTrough - 0.01f, y + WallTrough + 0.01f), true, first, last, phase);
                }

                if (o.Level == BarrierLod.Full)
                    Sweep(dark.Mesh, line, Ring(0.012f, 8, b + fs * (WallSkin + 0.014f), capBottom - 0.015f), true, first, last, phase);
                if (o.Level == BarrierLod.Full && l.Trellis > 0f)
                    TrellisRails(dark.Mesh, line, l, fs, b, capBottom, phase, first, last);
            }

            if (far)
                return shapes;

            // The posts: across the body and proud of the faces (to the trellis, which they carry), every PostSpacing of the whole run and at its far end.
            float reach = l.Trellis > 0f ? l.Trellis + 0.012f : WallSkin + 0.015f;
            float lo = l.Faces == 2 ? l.CoreLo - reach : (side >= 0 ? l.CoreLo - 0.015f : l.CoreLo - reach), hi = l.Faces == 2 ? l.CoreHi + reach : (side >= 0 ? l.CoreHi + reach : l.CoreHi + 0.015f);
            foreach (float s in Stations(total, Math.Max(2f, o.PostSpacing), phase, last))
            {
                Vec3 ahead = HeadingAt(line, at, s);
                Box(steel.Mesh, RoadMarkings.PointAt(line, at, s) + ahead.RightXZ() * ((lo + hi) * 0.5f), ahead, hi - lo, 0.08f, -Embed, capBottom + 0.01f);
            }

            if (o.Level == BarrierLod.Full && l.Trellis > 0f)
                TrellisWires(dark.Mesh, line, at, total, l, phase);
            if (props != null)
                WallPlants(line, at, total, l, o, phase, whole, props);
            return shapes;
        }

        /// <summary>The trellis's horizontal rails (12 mm square, every 0.4 m from above the foot trough to under the pipe) on a face, 50 mm off it.</summary>
        private static void TrellisRails(MeshBuilder mesh, IList<Vec3> line, LivingWallLayout l, int fs, float b, float capBottom, float phase, bool first, bool last)
        {
            float u = b + fs * l.Trellis;
            for (float y = l.Rows[0].Y + WallTrough + 0.25f; y < capBottom - 0.1f; y += 0.4f)
                Sweep(mesh, line, FaceRect(u - 0.006f, u + 0.006f, y - 0.006f, y + 0.006f), true, first, last, phase);
        }

        /// <summary>The trellis's upright wires (12 mm square) every 0.4 m of the whole run, from the foot trough's soil to under the pipe, on the planted faces.</summary>
        private static void TrellisWires(MeshBuilder mesh, IList<Vec3> line, IList<float> at, float total, LivingWallLayout l, float phase)
        {
            float y0 = l.Rows[0].Y + WallTrough, y1 = l.Height - WallCap - 0.1f;
            foreach (float s in MarkingStuds.Stations(total, 0.4f, phase))
            {
                Vec3 ahead = HeadingAt(line, at, s), p = RoadMarkings.PointAt(line, at, s);
                foreach (int fs in l.Sides)
                    Box(mesh, p + ahead.RightXZ() * (l.Base(fs) + fs * l.Trellis), ahead, 0.012f, 0.012f, y0, y1);
            }
        }

        /// <summary>
        /// The game's plants of a wall: a flowering bush or a shrub every 1.1 m of the whole run in the foot trough of each planted face (the run's total thinned to WallPlantCap), and with TopPlants a flower pot
        /// every 1.5 m on the cap (thinned to WallPotCap), laid on the whole run's
        /// grid (a piece owns the stations after its start up to and with its end), so that the pieces of a run go on in step and none stands twice.
        /// </summary>
        private static void WallPlants(IList<Vec3> line, IList<float> at, float total, LivingWallLayout l, LivingWallOptions o, float phase, float whole, List<PropAnchor> props)
        {
            float gap = Math.Max(1.1f, whole * l.Faces / WallPlantCap);
            (float y, float depth) = l.Rows[0];
            float soilTop = y + WallTrough + 0.01f;
            foreach (float s in MarkingStuds.Stations(total, gap, phase))
            {
                Vec3 ahead = HeadingAt(line, at, s), p = RoadMarkings.PointAt(line, at, s);
                int index = (int)Math.Round((phase + s) / gap);
                string kind = o.Mixed ? (index % 3 == 2 ? GreenPlants.Shrub : GreenPlants.Flower) : o.FootPlants;
                foreach (int fs in l.Sides)
                    props.Add(new PropAnchor { Local = p + ahead.RightXZ() * (l.Base(fs) + fs * (WallSkin + depth * 0.5f)) + Up * soilTop, Yaw = YawOf(ahead), Kind = kind, Room = 0.45f });
            }

            if (!o.TopPlants)
                return;
            // Pots on the cap, in the middle of the footprint, on the whole run's grid too (the cap's top is the wall's height).
            float potGap = Math.Max(WallPotGap, whole / WallPotCap);
            foreach (float s in MarkingStuds.Stations(total, potGap, phase))
            {
                Vec3 ahead = HeadingAt(line, at, s);
                props.Add(new PropAnchor { Local = RoadMarkings.PointAt(line, at, s) + Up * l.Height, Yaw = YawOf(ahead), Kind = GreenPlants.Pot, Room = WallPotRoom });
            }
        }
    }
}
