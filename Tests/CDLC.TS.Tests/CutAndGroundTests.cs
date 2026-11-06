using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.30: the boards cut to the opaque part of a player's picture (CustomMask, CutOutline; the owner: "PNG应该支持透明通道，但是不允许半透明，半透明一律纯透明处理"), and the markings and things put down with one
    /// click laid on the ground under them (GroundGrid; "减速带、预制的地面区域都不能吸附地面高度").
    /// </summary>
    internal static class CutAndGroundTests
    {
        public static void RunAll()
        {
            Program.Run("cut: the mask of a picture's alpha (nearly opaque is opaque, the rest clear)", Mask);
            Program.Run("cut: the outline of the opaque cells, with its holes, and its triangles", Outline);
            Program.Run("cut: a custom board is cut to its picture, and the sign's mesh follows the cut", Board);
            Program.Run("ground: the grid of heights, its text and its heights between the points", Grid);
            Program.Run("ground: a stamp is laid on the grid, a gate and a booth stay upright", Stamps);
        }

        private static byte[] Rgba(int w, int h, Func<int, int, byte> alpha)
        {
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                    rgba[(y * w + x) * 4 + 3] = alpha(x, y);
            }

            return rgba;
        }

        private static void Mask()
        {
            Program.Check(CustomMask.FromAlpha(Rgba(8, 8, (x, y) => 255), 8, 8) == null && CustomMask.FromAlpha(Rgba(8, 8, (x, y) => 252), 8, 8) == null,
                "an opaque picture has no mask; one stored at 252 (opaque to the eye) neither");
            CustomMask half = CustomMask.FromAlpha(Rgba(8, 4, (x, y) => (byte)(x < 4 ? 255 : 128)), 8, 4);
            Program.Check(half != null && half.Width == 8 && half.Height == 4 && half.At(0, 0) && half.At(3, 3) && !half.At(4, 0) && !half.At(7, 3), "a half-transparent half is clear");
            CustomMask big = CustomMask.FromAlpha(Rgba(1000, 500, (x, y) => (byte)(x == 500 ? 0 : 255)), 1000, 500);
            Program.Check(big.Width == CustomMask.MaxSide && big.Height == CustomMask.MaxSide / 2, $"at most {CustomMask.MaxSide} cells on the longer side: {big.Width} x {big.Height}");
            int clear = 0;
            for (int i = 0; i < big.Cells.Length; i++)
                clear += big.Cells[i] ? 0 : 1;
            Program.Check(clear == big.Height, "a cell with one clear pixel is clear (one column of them): " + clear);
            CustomMask back = CustomMask.Decode(half.Encode());
            Program.Check(back != null && back.Width == 8 && back.Height == 4 && Array.TrueForAll(new[] { 0, 3, 4, 31 }, i => back.Cells[i] == half.Cells[i]), "it is written and read back");
            Program.Check(CustomMask.Decode(new byte[0]) == null && CustomMask.Decode(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }) == null, "an empty or foreign file is no mask");
            Program.Check(half.AtUv(0.1f, 0.1f) && !half.AtUv(0.9f, 0.1f) && !half.AtUv(1.5f, 0.5f), "looked up by the picture's coordinates");
        }

        private static float AreaOf(List<List<Point2>> loops)
        {
            float sum = 0f;
            foreach (List<Point2> loop in loops)
                sum += CutOutline.Area(loop);
            return sum;
        }

        private static float TrianglesArea(List<Point2> points, List<int> triangles)
        {
            float sum = 0f;
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Point2 a = points[triangles[t]], b = points[triangles[t + 1]], c = points[triangles[t + 2]];
                sum += ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) * 0.5f;
            }

            return sum;
        }

        private static void Outline()
        {
            // A square of 4 x 4 cells with a hole of 2 x 2 in it, cells of 1: an outer loop of area 16 and a hole of -4.
            bool Ring(int i, int j) => i < 4 && j < 4 && !(i >= 1 && i <= 2 && j >= 1 && j <= 2);
            List<List<Point2>> loops = CutOutline.Trace(Ring, 4, 4, 1f, 1f, 0f, 4f);
            Program.Check(loops.Count == 2, "a ring is an outer loop and a hole: " + loops.Count);
            float outer = Math.Max(CutOutline.Area(loops[0]), CutOutline.Area(loops[1])), hole = Math.Min(CutOutline.Area(loops[0]), CutOutline.Area(loops[1]));
            Program.Check(Math.Abs(outer - 16f) < 1e-3f && Math.Abs(hole + 4f) < 1e-3f, $"the outer counter-clockwise (16), the hole clockwise (-4): {outer}, {hole}");
            var points = new List<Point2>();
            var triangles = new List<int>();
            CutOutline.Triangulate(loops, points, triangles);
            Program.Check(Math.Abs(TrianglesArea(points, triangles) - 12f) < 1e-3f, "its triangles cover 12, counter-clockwise: " + TrianglesArea(points, triangles));

            // Two cells that touch at a corner only are two loops (they do not cross there).
            List<List<Point2>> two = CutOutline.Trace((i, j) => (i == 0 && j == 0) || (i == 1 && j == 1), 2, 2, 1f, 1f, 0f, 2f);
            Program.Check(two.Count == 2 && Math.Abs(AreaOf(two) - 2f) < 1e-3f, "two cells that touch at a corner are two loops: " + two.Count);

            // A disc of 40 cells across: one loop, its steps smoothed to far fewer points, its area the disc's within a few cells.
            bool Disc(int i, int j) => (i + 0.5f - 20f) * (i + 0.5f - 20f) + (j + 0.5f - 20f) * (j + 0.5f - 20f) < 400f;
            List<List<Point2>> disc = CutOutline.Trace(Disc, 40, 40, 0.025f, 0.025f, -0.5f, 0.5f);
            float area = AreaOf(disc), round = (float)(Math.PI * 0.25);
            Program.Check(disc.Count == 1 && Math.Abs(area - round) < 0.02f, $"a disc: one loop of the disc's area ({round:0.000}): {area:0.000}");
            Program.Check(disc[0].Count < 60, "its steps are smoothed away: " + disc[0].Count + " points (a staircase has some 160)");
            Program.Check(CutOutline.Trace((i, j) => false, 3, 3, 1f, 1f, 0f, 3f).Count == 0, "no opaque cell, no loop");
        }

        private static void Board()
        {
            Func<string, CustomMask> before = CustomSigns.Masks;
            try
            {
                // A picture whose right half is clear: the board of it is its left half.
                CustomMask half = CustomMask.FromAlpha(Rgba(64, 64, (x, y) => (byte)(x < 32 ? 255 : 0)), 64, 64);
                CustomSigns.Masks = key => key == "00000000000000aa" ? half : null;
                SignDef def = SignCatalog.ById(CustomSigns.TemplateId);
                GuideBoard Board(string spec) => def.Template.Layout(def.Template.Parse(spec));
                GuideBoard cut = Board("shape=rect;w=1;pw=64;ph=64;img=00000000000000aa;cut=1");
                GuidePart part = cut.Main;
                Program.Check(part.Cut != null && Math.Abs(AreaOf(part.Cut) - 0.5f) < 0.01f, "the board is the picture's opaque half: " + (part.Cut != null ? AreaOf(part.Cut) : 0f));
                GuideElement image = part.Elements.Find(e => e.Kind == GuideElementKind.Image);
                Program.Check(image.Opaque, "its picture is drawn without its alpha (its clear pixels carry their neighbours' colours)");
                Program.Check(Board("shape=rect;w=1;pw=64;ph=64;img=00000000000000bb;cut=1").Main.Cut == null, "a picture with no mask is the whole board, as before");
                // 0.12.31: by default the board keeps its shape and ground, which shows through the picture's clear parts (its alpha is drawn).
                GuideBoard kept = Board("shape=rect;w=1;pw=64;ph=64;img=00000000000000aa");
                Program.Check(kept.Main.Cut == null && !kept.Main.Elements.Find(e => e.Kind == GuideElementKind.Image).Opaque, "without cut the board is whole and the ground shows through the clear parts");

                // The margins of a whole picture go with its clear pixels; a circle's outline cuts it too.
                GuideBoard wide = Board("shape=rect;w=2;h=1;pw=64;ph=64;img=00000000000000aa;cut=1;fit=contain");
                Program.Check(Math.Abs(AreaOf(wide.Main.Cut) - 0.5f) < 0.02f, "the margins are cut: the opaque half of a picture of 1 x 1 in a board of 2 x 1: " + AreaOf(wide.Main.Cut));
                GuideBoard disc = Board("shape=circle;w=1;pw=64;ph=64;img=00000000000000aa;cut=1;fit=fill");
                Program.Check(Math.Abs(AreaOf(disc.Main.Cut) - (float)(Math.PI / 8)) < 0.02f, "a circle cuts it to half a disc: " + AreaOf(disc.Main.Cut));
                foreach (List<Point2> loop in disc.Main.Cut)
                {
                    foreach (Point2 p in loop)
                        Program.Check(p.X <= 0.01f && p.X * p.X + p.Y * p.Y <= 0.26f, "every point of the cut is in the left half of the disc: " + p.X + "," + p.Y);
                }

                // The sign's mesh: the face is the cut (its area), and the item is still planned a support and picked by its outline.
                SignInstance instance = SignInstances.Of(def, "shape=rect;w=1;pw=64;ph=64;img=00000000000000aa;cut=1");
                SignParts parts = SignBuilder.Build(instance, null);
                float faceArea = 0f;
                foreach (FacePart face in parts.Faces)
                    faceArea += MeshArea(face.Mesh);
                Program.Check(Math.Abs(faceArea - 0.5f) < 0.02f, "the face of the sign is the cut: " + faceArea);
                Program.Check(parts.Picks.Count > 0 && SupportPlanner.Auto(instance.Def) != null, "it can be picked and stands on a support");
            }
            finally
            {
                CustomSigns.Masks = before;
            }
        }

        private static float MeshArea(MeshBuilder mesh)
        {
            float sum = 0f;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Snap.Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
                float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                sum += (float)Math.Sqrt(cx * cx + cy * cy + cz * cz) * 0.5f;
            }

            return sum;
        }

        private static void Grid()
        {
            GroundGrid.Count(-1.75f, 1.75f, -0.2f, 0.2f, out int nx, out int nz);
            Program.Check(nx == 3 && nz == 2, $"a hump 3.5 m across and 0.4 m along: 3 by 2 points: {nx} x {nz}");
            GroundGrid.Count(-1.1f, 1.1f, -14f, 14f, out nx, out nz);
            Program.Check(nx == 2 && nz == GroundGrid.MaxAlong, $"an island 28 m long: {GroundGrid.MaxAlong} points along at the most: {nz}");
            var grid = new GroundGrid { X0 = -1f, X1 = 1f, Z0 = 0f, Z1 = 4f, Nx = 2, Nz = 3, Heights = new[] { 0f, 0.2f, 0.1f, 0.3f, 0.4f, 0.6f } };
            Program.Check(Math.Abs(grid.At(-1f, 0f)) < 1e-5f && Math.Abs(grid.At(1f, 4f) - 0.6f) < 1e-5f && Math.Abs(grid.At(0f, 2f) - 0.2f) < 1e-5f && Math.Abs(grid.At(0f, 3f) - 0.35f) < 1e-5f,
                "its heights at the points and between them (bilinear)");
            Program.Check(Math.Abs(grid.At(-5f, -5f)) < 1e-5f && Math.Abs(grid.At(5f, 9f) - 0.6f) < 1e-5f, "beyond the box, the nearest edge's");
            string text = grid.ToString();
            GroundGrid back = GroundGrid.Parse(text);
            Program.Check(text == "-100,100,0,400,2,3,0,20,10,30,40,60" && back != null && back.ToString() == text, "written in centimetres and read back: " + text);
            Program.Check(GroundGrid.Parse("") == null && GroundGrid.Parse("1,2,3") == null && GroundGrid.Parse("-100,100,0,400,2,3,0,20") == null && GroundGrid.Parse("-100,100,0,400,2,3,0,20,10,30,40,9999") == null
                && GroundGrid.Parse("100,-100,0,400,2,2,0,0,0,0") == null, "a text that is not a grid is none (the item lies flat)");
            Program.Check(new GroundGrid { X0 = 0f, X1 = 1f, Z0 = 0f, Z1 = 1f, Nx = 2, Nz = 2, Heights = new[] { 0f, 0.005f, -0.004f, 0f } }.Flat, "heights within a centimetre are flat");
        }

        private static void Stamps()
        {
            SignDef hump = SignCatalog.ById(MarkingTemplates.HumpId), island = SignCatalog.ById(MarkingTemplates.TollIslandId), gate = SignCatalog.ById(MarkingTemplates.GateId);
            Program.Check(MarkingTemplates.Drapes(MarkingTemplates.HumpId) && MarkingTemplates.Drapes(MarkingTemplates.TollIslandId) && MarkingTemplates.Drapes("mk.toll.plaza") && MarkingTemplates.Drapes("mk.stop")
                && !MarkingTemplates.Drapes(MarkingTemplates.GateId) && !MarkingTemplates.Drapes(MarkingTemplates.BoothId) && !MarkingTemplates.Drapes(MarkingTemplates.GoreId),
                "the hump, the island and the painted stamps are laid on the ground; the gate and the booth stand upright; the fitted areas fit on their own");
            Program.Check(hump.Template.FieldOf(GroundGrid.Key) != null && gate.Template.FieldOf(GroundGrid.Key) != null && SignCatalog.ById("mk.line.edge")?.Template.FieldOf(GroundGrid.Key) == null,
                "a stamp has the setting, a line along the road (which has its own path's heights) not");

            // A hump across a road that falls 10 cm from its left to its right: each end of it at the ground's height.
            MarkingPlan flat = MarkingTemplates.PlanOf(hump, hump.Template.Default().ToString());
            var tilt = new GroundGrid { X0 = flat.MinX, X1 = flat.MaxX, Z0 = flat.MinZ, Z1 = flat.MaxZ, Nx = 2, Nz = 2, Heights = new[] { 0.05f, -0.05f, 0.05f, -0.05f } };
            MarkingPlan laid = MarkingTemplates.PlanOf(hump, MarkingTemplates.WithGround(hump.Template, hump.Template.Default().ToString(), tilt));
            float leftLow = float.MaxValue, rightLow = float.MaxValue, flatLow = float.MaxValue;
            foreach (MarkingPart part in laid.Parts)
            {
                foreach (Snap.Vec3 p in part.Mesh.Positions)
                {
                    if (p.X < flat.MinX + 0.05f)
                        leftLow = Math.Min(leftLow, p.Y);
                    if (p.X > flat.MaxX - 0.05f)
                        rightLow = Math.Min(rightLow, p.Y);
                }
            }

            foreach (MarkingPart part in flat.Parts)
            {
                foreach (Snap.Vec3 p in part.Mesh.Positions)
                    flatLow = Math.Min(flatLow, p.Y);
            }

            Program.Check(Math.Abs(leftLow - (flatLow + 0.05f)) < 0.01f && Math.Abs(rightLow - (flatLow - 0.05f)) < 0.01f, $"its left end 5 cm up, its right end 5 cm down: {leftLow - flatLow:0.000}, {rightLow - flatLow:0.000}");
            Program.Check(laid.MinX == flat.MinX && laid.MaxZ == flat.MaxZ, "its box on the ground is the same");
            string islandSpec = MarkingTemplates.WithGround(island.Template, island.Template.Default().ToString(),
                new GroundGrid { X0 = -1.1f, X1 = 1.1f, Z0 = -14f, Z1 = 14f, Nx = 2, Nz = GroundGrid.MaxAlong, Heights = new float[2 * GroundGrid.MaxAlong] });
            Program.Check(island.Template.Parse(islandSpec).Get(GroundGrid.Key) == "", "a flat ground is not written (the item is as before)");
            var full = new GroundGrid { X0 = -2.1f, X1 = 2.1f, Z0 = -30f, Z1 = 30f, Nx = GroundGrid.MaxAcross, Nz = GroundGrid.MaxAlong, Heights = new float[GroundGrid.MaxAcross * GroundGrid.MaxAlong] };
            for (int k = 0; k < full.Heights.Length; k++)
                full.Heights[k] = -1.95f + k * 0.13f;
            string fullSpec = MarkingTemplates.WithGround(island.Template, island.Template.Default().ToString(), full);
            Program.Check(island.Template.Parse(fullSpec).Get(GroundGrid.Key) == full.ToString() && System.Text.Encoding.UTF8.GetByteCount(fullSpec) < GuideSpec.MaxBytes,
                "the largest grid is kept whole in the item's settings: " + System.Text.Encoding.UTF8.GetByteCount(fullSpec) + " bytes");
        }
    }
}
