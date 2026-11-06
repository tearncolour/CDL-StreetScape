using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The thumbnails of the panel: for a sign, its face seen from the front with its real outline; for a support, a whole sign on it seen from above at an angle.
    /// Transparent background, a dark line round the shape, rendered at twice the size and shrunk for smooth edges. Pure: it takes the painted picture of the face as
    /// bytes (the GDI+ painter lives in Render and is not needed to test this), and returns the PNG.
    /// </summary>
    public static class Thumbnails
    {
        /// <summary>The edge of a thumbnail (pixels).</summary>
        public const int Size = 160;

        public const int Supersample = 2;

        /// <summary>Raise this when the art of the thumbnails changes, so that the files of the old art are not reused.</summary>
        public const int Revision = 5;

        private const float PadFraction = 0.08f;

        /// <summary>The size a picture is drawn at before it is framed (pixels).</summary>
        private static readonly int Inner = (int)(Size * Supersample * (1f - 2f * PadFraction));

        private static readonly Vec3 Front = new Vec3(0f, 0f, -1f);
        private static readonly Vec3 Oblique = new Vec3(-0.55f, -0.30f, -0.78f);
        private static readonly Vec3 KerbView = new Vec3(0.6f, -0.7f, 0.4f);

        /// <summary>The sign that is drawn on a support in its thumbnail: a long board (an exit advance board) for the double post and the cantilever, the stop sign for the others.</summary>
        public static SignInstance CarrierFor(SupportDef support)
        {
            if (support.Kind == SupportKind.TriplePole)
                return SignInstances.Of(SignCatalog.ById("guide.exit.advance"), "to1=连云港市;to2=石家庄市;dist=500;unit=m;xnum=145");
            // A street-name plate post (0.12.57) with a plate of its city's size and colour; Xuzhou's the B type (a branch road's), which tells its thumbnail from Shanghai's A.
            if (support.Plate != PlateStyle.None && SignCatalog.ById("gb17733.street") != null)
                return SignInstances.Of(SignCatalog.ById("gb17733.street"), support.Plate == PlateStyle.Hangzhou ? "len=1120;wid=360;run=ns" : support.Plate == PlateStyle.Shenzhen ? "len=1200;wid=360" : support.Plate == PlateStyle.Xuzhou ? "len=1200;wid=360;run=ns" : "len=1500;wid=450");
            // A single cantilever with a board of one arm (22MR601 1F, up to 2.3 m²: a road-name plate); the F type and the double cantilever with the exit board, on two arms or more.
            if (support.Kind == SupportKind.Cantilever && SignCatalog.ById("gb17733.street") != null)
                return SignInstances.Of(SignCatalog.ById("gb17733.street"), null);
            // A height-limit frame (0.12.78) with the board that names its height.
            if (support.HeightLimit && SignCatalog.ById("gb.prohib.36") != null)
                return SignInstances.Of(SignCatalog.ById("gb.prohib.36"), "height=" + support.Clearance.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            if (support.Kind == SupportKind.DoublePole || SupportKinds.Arm(support.Kind))
                return SignInstances.Of(SignCatalog.ById("guide.exit.advance"), GuideTemplates.SpecOf(GuideTemplates.PresetById("gb.exit.adv.2km")).ToString());
            // A street-name plate (GB 17733) is what is most often on a wall; over a road it is a direction board (Fig. 8 a shows two rectangular boards).
            if (SupportKinds.OverRoad(support.Kind))
                return SignInstances.Of(SignCatalog.ById("guide.exit.advance"), GuideTemplates.SpecOf(GuideTemplates.PresetById("gb.exit.adv.2km")).ToString());
            if (SupportKinds.OnWall(support.Kind) && SignCatalog.ById("gb17733.street") != null)
                return SignInstances.Of(SignCatalog.ById("gb17733.street"), null);
            return SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null);
        }

        /// <summary>
        /// The name of a thumbnail's file: kind ("s" sign, "p" support), the id, and a stamp of the art's revision, the fonts (SignFonts.Stamp: another font makes them again, 0.12.28) and the id, so that
        /// a change of art changes the name.
        /// </summary>
        public static string FileName(string kind, string id)
        {
            var safe = new StringBuilder();
            foreach (char c in id)
                safe.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_');
            uint stamp = SignCatalog.HashOf(id + "|" + Revision + "|" + SignFonts.Stamp);
            return $"{kind}-{safe}-{stamp:x8}.png";
        }

        /// <summary>The file of the tile that adds a sign of the player's own (0.12.29): a dashed square with a plus in it.</summary>
        public static string AddTileFile => $"add-r{Revision}.png";

        /// <summary>
        /// The picture of that tile, RGBA with the top row first: transparent, a light dashed square a sixteenth of the edge in from it, and a plus in the middle, as the asset menu's tiles are pictures on
        /// the game's own button.
        /// </summary>
        public static byte[] AddTileRgba()
        {
            const int n = Size, inset = Size / 16, stroke = 4, dash = 14, gap = 9, arm = Size / 5, bar = 8;
            var rgba = new byte[n * n * 4];
            void Dot(int x, int y)
            {
                int at = (y * n + x) * 4;
                rgba[at] = 236;
                rgba[at + 1] = 240;
                rgba[at + 2] = 242;
                rgba[at + 3] = 235;
            }

            int lo = inset, hi = n - 1 - inset;
            for (int along = lo; along <= hi; along++)
            {
                // The dashes run from each corner, so that the four corners are drawn.
                if ((along - lo) % (dash + gap) >= dash)
                    continue;
                for (int w = 0; w < stroke; w++)
                {
                    Dot(along, lo + w);
                    Dot(along, hi - w);
                    Dot(lo + w, along);
                    Dot(hi - w, along);
                }
            }

            int c = n / 2;
            for (int a = -arm; a <= arm; a++)
            {
                for (int w = -bar / 2; w < bar / 2; w++)
                {
                    Dot(c + a, c + w);
                    Dot(c + w, c + a);
                }
            }

            return rgba;
        }

        public static byte[] SignRgba(SignInstance instance, byte[] picture, int pw, int ph)
        {
            SignParts parts = SignBuilder.Build(Bold(instance), null);
            // A sign with no face is made of painted solids alone (a neon sign's strokes, the lanterns' bodies): the face layer drew nothing of them and its thumbnail was empty (0.12.59).
            return Compose(parts, picture, pw, ph, Front, null, 0f, HasFace(parts) ? RasterLayers.Face : RasterLayers.Face | RasterLayers.Solids);
        }

        /// <summary>The widest a neon strip may be (mm): at 15 a strip is one pixel of a thumbnail, and its outline takes it.</summary>
        private const string ThumbnailNeonLine = "40";

        /// <summary>The sign as its thumbnail draws it: a neon sign with its strips at the widest, so that they show at 160 pixels (0.12.59).</summary>
        private static SignInstance Bold(SignInstance instance)
        {
            if (instance.Def.Template == null || instance.Def.Template.Id != Signs.LampAds.NeonId || instance.Settings == null)
                return instance;
            return SignInstances.Of(instance.Def, instance.Settings.With("line", ThumbnailNeonLine).ToString());
        }

        private static bool HasFace(SignParts parts)
        {
            foreach (FacePart face in parts.Faces)
            {
                if (face.Mesh != null && face.Mesh.VertexCount > 0)
                    return true;
            }

            foreach (MeshBuilder led in parts.Leds)
            {
                if (led != null && led.VertexCount > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The frame of an over-road support's tile: the point it looks at and its half size. The board close, as a wall's, for the one flat on a face; for a hung one the board from its lower edge, the rods and the
        /// stand-in's slab to its top, and the board's width.
        /// </summary>
        internal static float OverFrame(SupportLayout layout, float boardWidth, float boardHeight, out Vec3 at)
        {
            if (!layout.Hanger)
            {
                at = new Vec3(0f, layout.BoardCentreY, 0f);
                return Math.Max(boardWidth, boardHeight) * 0.5f + 0.9f;
            }

            float top = layout.HangerTop + SignBuilder.OverStandInThick;
            at = new Vec3(0f, (layout.BottomClearance + top) * 0.5f, 0f);
            return Math.Max((top - layout.BottomClearance) * 0.5f, boardWidth * 0.5f) + 0.25f;
        }

        public static byte[] SupportRgba(SupportDef support, byte[] carrierPicture, int pw, int ph)
        {
            SignInstance carrier = CarrierFor(support);
            SignParts parts = SignBuilder.Build(carrier, support);
            // An attached support draws no post of its own; its thumbnail shows the pole it would be on.
            if (SupportKinds.Hosted(support.Kind))
                SignBuilder.AddHostStandIn(parts);
            // An over-road support draws no structure: its thumbnail shows a beam and a deck over the board (Fig. 8 a), as close as a wall's.
            if (SupportKinds.OverRoad(support.Kind))
            {
                SignBuilder.AddOverStandIn(parts);
                float framed = OverFrame(parts.Layout, carrier.Def.Width, carrier.Def.Height, out Vec3 at);
                return Compose(parts, carrierPicture, pw, ph, Oblique, at, framed, RasterLayers.All);
            }

            // A wall support draws no wall; its thumbnail shows a piece of one, and the board on it close.
            if (SupportKinds.OnWall(support.Kind))
            {
                SignBuilder.AddWallStandIn(parts, support);
                SupportLayout w = parts.Layout;
                float near = Math.Max(carrier.Def.Width, carrier.Def.Height) * 0.5f + 0.5f;
                return Compose(parts, carrierPicture, pw, ph, Oblique, new Vec3(w.BoardCentreX * 0.7f, w.BoardCentreY, 0f), near, RasterLayers.All);
            }

            SupportLayout l = parts.Layout;
            // The space truss and the height-limit frame (0.12.78) are shown whole, columns and beam; the portal gantry's tile is as it was (the middle of its beam and the board).
            if (ShownWhole(support))
            {
                float side = WholeHalf(l);
                if (support.HeightLimit)
                    Thicken(parts, side * TileThickening);
                return Compose(parts, carrierPicture, pw, ph, Oblique, WholeFocus(l), side, RasterLayers.All);
            }

            // The whole post (or the whole post and arm) in the window.
            Vec3 focus = SupportKinds.OneWayArm(support.Kind) ? new Vec3(l.ArmLength * 0.5f, l.PoleTop * 0.5f, 0f) : support.Kind == SupportKind.DoubleCantilever ? new Vec3(0f, l.PoleTop * 0.5f, 0f) : new Vec3(l.BoardCentreX, l.PoleTop * 0.5f, 0f);
            float half = SupportKinds.OneWayArm(support.Kind) ? Math.Max(l.PoleTop * 0.5f, l.ArmLength * 0.5f) + 0.35f : support.Kind == SupportKind.DoubleCantilever ? Math.Max(l.PoleTop * 0.5f, l.ArmLength) + 0.35f : Math.Max(l.PoleTop * 0.5f + 0.15f, carrier.Def.Width * 0.5f + 0.3f);
            return Compose(parts, carrierPicture, pw, ph, Oblique, focus, half, RasterLayers.All);
        }

        /// <summary>The supports whose tile shows the whole frame (0.12.78): the space truss and the height-limit frame; the portal gantry's is the middle of its beam, as it was.</summary>
        public static bool ShownWhole(SupportDef support) => support.SpaceTruss || support.HeightLimit;

        /// <summary>The window of a whole-frame tile: centred on the middle of the span at half the frame's height, its half side the larger of the frame's half height and half its span (with the columns' room), and a margin.</summary>
        public static Vec3 WholeFocus(SupportLayout l) => new Vec3(l.ArmLength * 0.5f, l.PoleTop * 0.5f, 0f);

        public static float WholeHalf(SupportLayout l) => Math.Max(l.PoleTop * 0.5f, l.ArmLength * 0.5f + 0.5f) + 0.35f;

        /// <summary>How much a height-limit frame's tubes are thickened in its tile, as a fraction of the window's half side: its tubes are under a pixel wide, so the bands would be lost in the outline.</summary>
        public const float TileThickening = 0.012f;

        /// <summary>The coloured tubes pushed out along their normals by grow metres: a round tube's radius grows by it, its bands stay where they are (a tile's picture, not the support).</summary>
        public static void Thicken(SignParts parts, float grow)
        {
            foreach (MeshBuilder mesh in parts.Solids)
            {
                if (mesh == null)
                    continue;
                for (int i = 0; i < mesh.Positions.Count; i++)
                    mesh.Positions[i] = mesh.Positions[i] + mesh.Normals[i] * grow;
            }
        }

        /// <summary>The id of a kerb style's upgrade icon (0.12.33; .2: the stone 0.2 m wide, 0.12.34) and its file.</summary>
        public static string KerbId(KerbStyle style) => "kerb" + (int)style + ".2";

        public static string KerbFile(KerbStyle style) => FileName("k", KerbId(style));

        /// <summary>
        /// The icon of a kerb style's road upgrade (0.12.33): two stones of the kerb between the carriageway and a footway of red pavers, seen from above the carriageway (the stones shorter than
        /// their real metre, so that the joint shows).
        /// </summary>
        public static byte[] KerbRgba(KerbStyle style)
        {
            const float stone = 0.55f, joint = 0.015f, rise = 0.18f, length = 2f * stone + joint;
            var bodies = new List<(MeshBuilder Mesh, byte[] Rgb)>();
            for (int i = 0; i < 2; i++)
            {
                var line = new KerbLine { Style = style, Rise = rise, Footway = 1 };
                line.Foot.Add(new Vec3(0f, 0f, i * (stone + joint)));
                line.Foot.Add(new Vec3(0f, 0f, i * (stone + joint) + stone));
                foreach (MarkingShape shape in Kerbs.Shapes(line))
                {
                    if (shape.Mesh.VertexCount > 0)
                        bodies.Add((shape.Mesh, Raster.SolidRgb(shape.Solid ?? SolidColour.Concrete)));
                }
            }

            var road = new MeshBuilder();
            Solids.Box(road, new Vec3(-0.35f, -0.03f, length * 0.5f), new Vec3(0.35f, 0.03f, length * 0.5f));
            var footway = new MeshBuilder();
            Solids.Box(footway, new Vec3(0.3f, rise - 0.03f, length * 0.5f), new Vec3(0.3f, 0.03f, length * 0.5f));
            bodies.Add((road, new byte[] { 54, 56, 60 }));
            bodies.Add((footway, new byte[] { 160, 98, 80 }));
            byte[] drawn = Raster.RenderBodies(bodies, KerbView, Inner, out int w, out int h, new RasterOptions { Transparent = true });
            return Frame(drawn, w, h);
        }

        public static byte[] KerbPng(KerbStyle style) => Png.Encode(KerbRgba(style), Size, Size);

        public static byte[] SignPng(SignInstance instance, byte[] picture, int pw, int ph) => Png.Encode(SignRgba(instance, picture, pw, ph), Size, Size);

        public static byte[] SupportPng(SupportDef support, byte[] carrierPicture, int pw, int ph) => Png.Encode(SupportRgba(support, carrierPicture, pw, ph), Size, Size);

        /// <summary>The first column and the first row that have a solid pixel (alpha of 128 or more); 0 when there is none.</summary>
        private static void GetFirstSolid(byte[] rgba, int w, int h, out int firstX, out int firstY)
        {
            firstX = w;
            firstY = h;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (rgba[(y * w + x) * 4 + 3] < 128)
                        continue;
                    firstX = Math.Min(firstX, x);
                    firstY = Math.Min(firstY, y);
                    break;
                }
            }

            if (firstX == w)
                firstX = 0;
            if (firstY == h)
                firstY = 0;
        }

        /// <summary>The offset moved by at most one pixel so that offset + first is a multiple of Supersample.</summary>
        private static int Snap(int offset, int first)
        {
            int rest = (offset + first) % Supersample;
            return rest == 0 ? offset : offset - rest;
        }

        private static byte[] Compose(SignParts parts, byte[] picture, int pw, int ph, Vec3 dir, Vec3? focus, float half, RasterLayers layers)
        {
            byte[] drawn = Raster.Render(parts, picture, pw, ph, dir, Inner, out int w, out int h, focus, half, new RasterOptions { Transparent = true, Layers = layers });
            return Frame(drawn, w, h);
        }

        /// <summary>
        /// A picture drawn at Inner centred on a square transparent canvas, outlined and shrunk to Size. The first solid column and row are put on an even pixel: the outline is Supersample pixels thick
        /// and is shrunk by Supersample, so it is a whole pixel of the thumbnail only when it starts on a multiple of Supersample (a slender board's line fell between two pixels and came out grey, or not at all).
        /// </summary>
        private static byte[] Frame(byte[] drawn, int w, int h)
        {
            int big = Size * Supersample;
            GetFirstSolid(drawn, w, h, out int firstX, out int firstY);
            int offsetX = Snap((big - w) / 2, firstX), offsetY = Snap((big - h) / 2, firstY);
            byte[] canvas = ImageOps.Canvas(big, big);
            ImageOps.Blit(canvas, big, big, drawn, w, h, offsetX, offsetY);
            ImageOps.Outline(canvas, big, big, Supersample, 28, 34, 42);
            return ImageOps.Downsample(canvas, big, big, Supersample, out _, out _);
        }
    }
}
