using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>The player's own signs from pictures (0.12.29; the owner: "加入支持png图片直接导入的标签页，允许创建自定义路牌…可以保存为专门路牌").</summary>
    internal static class CustomSignTests
    {
        public static void RunAll()
        {
            Program.Run("custom: a picture's settings, key and size", Picture);
            Program.Run("custom: the board has the shape and size asked for, the picture fills it as asked", Layout);
            Program.Run("custom: the saved signs are the catalogue's entries and are kept in their file", Saved);
            Program.Run("custom: the add tile", AddTile);
        }

        private static GuideElement ImageOf(GuideBoard board) => board.Main.Elements.Find(e => e.Kind == GuideElementKind.Image);

        private static void Picture()
        {
            byte[] a = Png.Encode(new byte[4 * 6 * 3], 6, 3), b = Png.Encode(new byte[4 * 6 * 4], 6, 4);
            Program.Check(Png.TrySize(a, out int w, out int h) && w == 6 && h == 3, "a PNG's size is read from its header: " + w + " x " + h);
            Program.Check(!Png.TrySize(new byte[30], out _, out _) && !Png.TrySize(null, out _, out _) && !Png.TrySize(new byte[10], out _, out _), "anything else is not a PNG");
            string ka = CustomSigns.KeyOf(a), kb = CustomSigns.KeyOf(b);
            Program.Check(CustomSigns.IsKey(ka) && CustomSigns.IsKey(kb) && ka != kb && ka == CustomSigns.KeyOf((byte[])a.Clone()), "the key is the file's: the same bytes the same key, others another: " + ka + " " + kb);
            Program.Check(!CustomSigns.IsKey("../../evil.png") && !CustomSigns.IsKey("ABCDEF0123456789") && !CustomSigns.IsKey("") && !CustomSigns.IsKey(null), "nothing but a key is read as one (it names a file)");

            GuideTemplate t = CustomSigns.Template;
            GuideSpec wide = t.Parse(CustomSigns.SpecForPicture(ka, 800, 400, "C:\\x\\公司标志.png"));
            Program.Check(wide.Get("img") == ka && wide.Get("pw") == "800" && wide.Get("ph") == "400" && wide.Get("name") == "公司标志" && wide.Get("w") == "1", "a wide picture: its key, size, name and a metre wide: " + wide);
            GuideSpec tall = t.Parse(CustomSigns.SpecForPicture(ka, 300, 600, "tall.png"));
            Program.Check(tall.Get("w") == "0.5" && tall.Get("name") == "tall", "a tall picture is a metre high: half a metre wide: " + tall.Get("w"));
            Program.Check(t.Parse("img=../x;pw=abc").Get("pw") == "" && t.Parse("img=../x").Get("img") == "../x", "the size is digits only (the key is checked where a file is named)");
            Program.Check(SignCatalog.ById(CustomSigns.TemplateId) != null && SignCatalog.ChoiceById(CustomSigns.TemplateId).Hidden, "the bare custom sign is in the catalogue and is not listed");
            Program.Check(CustomSigns.NameOf("name=") == "自定义路牌" && CustomSigns.NameOf("name=标志") == "标志", "a sign with no name is listed by the template's");
        }

        private static void Layout()
        {
            GuideTemplate t = CustomSigns.Template;
            GuideBoard Board(string spec) => t.Layout(t.Parse(spec));

            // Rectangles: the height follows the picture unless it is given; the corners are round or square.
            GuideBoard round = Board("shape=round;w=2;pw=800;ph=400");
            Program.Check(round.Main.Shape == GuidePartShape.RoundRect && Math.Abs(round.Main.W - 2f) < 1e-4f && Math.Abs(round.Main.H - 1f) < 1e-4f && round.Main.Radius > 0f && round.Main.Edge == 0f,
                $"round corners, 2 m wide, as high as the picture is (1 m), no border: {round.Main.W} x {round.Main.H}");
            GuideBoard given = Board("shape=rect;w=2;h=0.5;pw=800;ph=400");
            Program.Check(given.Main.Radius == 0f && Math.Abs(given.Main.H - 0.5f) < 1e-4f, "square corners, the height that is given");
            GuideElement whole = ImageOf(given);
            Program.Check(whole != null && Math.Abs(whole.H - 0.5f) < 1e-4f && Math.Abs(whole.W - 1f) < 1e-4f && Math.Abs(whole.X - 0.5f) < 1e-4f && whole.Y == 0f && !whole.Crop,
                $"whole: the picture as large as fits, in the middle: {whole?.X},{whole?.Y} {whole?.W} x {whole?.H}");
            GuideElement cover = ImageOf(Board("shape=rect;w=2;h=0.5;pw=800;ph=400;fit=cover"));
            Program.Check(cover.X == 0f && cover.Y == 0f && Math.Abs(cover.W - 2f) < 1e-4f && Math.Abs(cover.H - 0.5f) < 1e-4f && cover.Crop, "cropped: the board's box, the picture cut to it");
            GuideElement fill = ImageOf(Board("shape=rect;w=2;h=0.5;pw=800;ph=400;fit=fill"));
            Program.Check(fill.X == 0f && Math.Abs(fill.W - 2f) < 1e-4f && Math.Abs(fill.H - 0.5f) < 1e-4f && !fill.Crop, "stretched: the board's box");

            // The other shapes: their own outlines, the width their size; the picture inside the box.
            GuideBoard circle = Board("shape=circle;w=1.2;pw=500;ph=500");
            Program.Check(circle.Main.Shape == GuidePartShape.Circle && circle.Main.W == circle.Main.H && Math.Abs(circle.Main.W - 1.2f) < 1e-4f, "a circle of the diameter given");
            GuideBoard octagon = Board("shape=octagon;w=0.8");
            Program.Check(octagon.Main.Shape == GuidePartShape.Octagon && Math.Abs(octagon.Main.W - 0.8f) < 1e-4f, "an octagon");
            foreach (string shape in new[] { "triangle", "triangle.down" })
            {
                GuideBoard tri = Board("shape=" + shape + ";w=0.9;pw=100;ph=100");
                GuideElement image = ImageOf(tri);
                Program.Check(tri.Main.Shape == (shape == "triangle" ? GuidePartShape.Triangle : GuidePartShape.TriangleDown) && Math.Abs(tri.Main.Side - 0.9f) < 1e-4f && image.W <= tri.Main.W + 1e-4f && image.H <= tri.Main.H + 1e-4f,
                    $"a {shape} of side 0.9, the picture inside its box: {image.W} x {image.H} in {tri.Main.W} x {tri.Main.H}");
            }

            // Whole on a shape that is not a rectangle: every corner of the picture inside the outline, and the picture not much smaller than the largest that fits (a square in a circle or an octagon: d / sqrt 2;
            // in a triangle of side a: a square of side a sqrt 3 / (2 + sqrt 3), on the base).
            foreach (string shape in new[] { "circle", "octagon", "triangle", "triangle.down" })
            {
                foreach (string size in new[] { "pw=100;ph=100", "pw=400;ph=100", "pw=100;ph=300" })
                {
                    GuideBoard board = Board("shape=" + shape + ";w=1;fit=contain;" + size);
                    GuideElement image = ImageOf(board);
                    List<Point2> outline = GuideKit.PartOutline(board.Main);
                    bool inside = true;
                    foreach ((float cx, float cy) in new[] { (image.X, image.Y), (image.Right, image.Y), (image.Right, image.Bottom), (image.X, image.Bottom) })
                    {
                        float px = cx - board.Main.W * 0.5f, py = board.Main.H * 0.5f - cy;
                        for (int i = 0; i < outline.Count; i++)
                        {
                            Point2 a = outline[i], b = outline[(i + 1) % outline.Count];
                            inside &= (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X) >= -1e-4f;
                        }
                    }

                    Program.Check(inside, $"{shape} {size}: the whole picture is inside the outline");
                    if (size == "pw=100;ph=100")
                    {
                        float largest = shape == "circle" || shape == "octagon" ? 1f / (float)Math.Sqrt(2.0) : (float)(Math.Sqrt(3.0) / (2.0 + Math.Sqrt(3.0)));
                        Program.Check(image.W > largest * 0.93f, $"{shape}: the square is about as large as fits: {image.W:0.000} (largest about {largest:0.000})");
                    }
                }
            }

            // Sizes outside the limits are brought to them; a picture of no known size is square.
            GuideBoard big = Board("shape=rect;w=40;h=0.01");
            Program.Check(Math.Abs(big.Main.W - CustomSigns.MaxSize) < 1e-4f && Math.Abs(big.Main.H - CustomSigns.MinSize) < 1e-4f, $"at most {CustomSigns.MaxSize} m and at least {CustomSigns.MinSize} m: {big.Main.W} x {big.Main.H}");
            GuideBoard unknown = Board("shape=round;w=1");
            Program.Check(Math.Abs(unknown.Main.H - 1f) < 1e-4f && ImageOf(unknown).Text == "", "no picture yet: a square board, an empty picture (the ground shows)");
            Program.Check(Board("ground=blue").Main.Ground == GuideColour.Blue && Board("ground=nonsense").Main.Ground == GuideColour.White, "the ground colour (a colour that is not offered is white)");

            // 0.12.31: the picture's size and place on the board. A picture 1 x 0.5 on a board 2 x 1 (whole: 2 x 1), at half its size, against the left and the bottom edge; at twice, centred.
            GuideElement half = ImageOf(Board("shape=rect;w=2;h=1;pw=800;ph=400;size=50;px=0;py=100"));
            Program.Check(Math.Abs(half.W - 1f) < 1e-4f && Math.Abs(half.H - 0.5f) < 1e-4f && Math.Abs(half.X) < 1e-4f && Math.Abs(half.Y - 0.5f) < 1e-4f, $"half its size against the left and the bottom: {half.X},{half.Y} {half.W} x {half.H}");
            GuideElement right = ImageOf(Board("shape=rect;w=2;h=1;pw=800;ph=400;size=50;px=100;py=0"));
            Program.Check(Math.Abs(right.X - 1f) < 1e-4f && Math.Abs(right.Y) < 1e-4f, $"against the right and the top: {right.X},{right.Y}");
            GuideElement middle = ImageOf(Board("shape=rect;w=2;h=1;pw=800;ph=400;size=50"));
            Program.Check(Math.Abs(middle.X - 0.5f) < 1e-4f && Math.Abs(middle.Y - 0.25f) < 1e-4f, "50 % is where the fill puts it (the middle)");
            GuideElement twice = ImageOf(Board("shape=rect;w=2;h=1;pw=800;ph=400;size=200"));
            Program.Check(Math.Abs(twice.W - 4f) < 1e-4f && Math.Abs(twice.X + 1f) < 1e-4f && Math.Abs(twice.Y + 0.5f) < 1e-4f, $"twice its size about its middle (clipped to the board): {twice.X},{twice.Y} {twice.W}");
            GuideElement tiny = ImageOf(Board("shape=rect;w=2;h=1;pw=800;ph=400;size=1"));
            Program.Check(Math.Abs(tiny.W - 2f * (float)CustomSigns.MinScale / 100f) < 1e-4f, $"at least {CustomSigns.MinScale} %: {tiny.W}");
            // In a triangle a whole picture stands low; moved up it goes to the top of the box.
            GuideElement low = ImageOf(Board("shape=triangle;w=1;pw=100;ph=100")), up = ImageOf(Board("shape=triangle;w=1;pw=100;ph=100;py=0"));
            Program.Check(low.Y > 0.1f && Math.Abs(up.Y) < 1e-4f, $"a triangle's whole picture is low; at 0 it is at the top: {low.Y:0.000}, {up.Y:0.000}");
            Program.Check(Math.Abs(CustomSigns.PlaceAlong(0.3f, 1f, 25.0) - 0.15f) < 1e-5f && Math.Abs(CustomSigns.PlaceAlong(0.3f, 1f, 75.0) - 0.65f) < 1e-5f && Math.Abs(CustomSigns.PlaceAlong(0.3f, -1f, 100.0) + 1f) < 1e-5f,
                "between the fill's place and either edge in a straight line");

            // As placed: an instance of it is a board of one part with its picture, so the renderer and the painter draw it as any board.
            SignInstance instance = SignInstances.Of(SignCatalog.ById(CustomSigns.TemplateId), "shape=round;w=2;pw=800;ph=400;img=0123456789abcdef");
            Program.Check(instance.IsBoard && !instance.IsLed && instance.TexW > instance.TexH && Math.Abs(instance.Def.Width - 2f) < 1e-4f && SupportPlanner.Auto(instance.Def) != null,
                $"an instance: a board of {instance.Def.Width} x {instance.Def.Height} m, its picture {instance.TexW} x {instance.TexH} px, and a support is planned for it");
            Program.Check(CustomSigns.Is(SignCatalog.ById(CustomSigns.TemplateId).Hash) && !CustomSigns.Is(SignCatalog.ById("gb.prohib.01").Hash), "the template is told by its hash");
        }

        private static void Saved()
        {
            string before = CustomSigns.Text();
            try
            {
                CustomSigns.Load("c2\tname=甲;w=1.5;img=0123456789abcdef;pw=10;ph=10\nnot a line\nc2\tname=重复\nx7\tname=坏\r\nc10\tname=乙;shape=circle\r\n");
                IReadOnlyList<CustomSign> saved = CustomSigns.Saved;
                Program.Check(saved.Count == 2 && saved[0].Key == "c2" && saved[1].Key == "c10", "the file is read: two signs (a line that is not one, a repeated key and a key that is not cN are left out): " + saved.Count);
                SignChoice first = SignCatalog.ChoiceById(CustomSigns.ChoicePrefix + "c2");
                Program.Check(first != null && !first.Hidden && first.Group == CustomSigns.Group && first.Zh == "甲" && first.Def.Id == CustomSigns.TemplateId && CustomSigns.Template.Parse(first.Spec).Get("w") == "1.5",
                    "a saved sign is a listed entry of the custom tab: its name, the template, its settings");
                int listed = 0;
                foreach (SignChoice c in SignCatalog.Choices)
                    listed += c.Group == CustomSigns.Group && !c.Hidden ? 1 : 0;
                // The text light box (0.12.75) is listed in the same tab; the two saved signs are the others.
                Program.Check(listed == 2 + (CDLC.TS.Geo.Signs.TextBoxes.Enabled ? 1 : 0) + (CDLC.TS.Geo.Signs.StandBoxes.Enabled ? 1 : 0), "both are listed: " + listed);

                string key = CustomSigns.Add("name=丙;img=0123456789abcdef");
                Program.Check(key == "c11" && SignCatalog.ChoiceById(CustomSigns.ChoicePrefix + key)?.Zh == "丙", "a new sign takes the next number after the largest (c11), and is listed at once: " + key);
                Program.Check(CustomSigns.Update("c2", "name=甲二") && SignCatalog.ChoiceById(CustomSigns.ChoicePrefix + "c2").Zh == "甲二" && !CustomSigns.Update("c99", "name=x"), "a saved sign takes new settings; one that is not there does not");
                Program.Check(CustomSigns.Remove("c10") && !CustomSigns.Remove("c10") && SignCatalog.ChoiceById(CustomSigns.ChoicePrefix + "c10") == null, "a saved sign is deleted, once");
                Program.Check(CustomSigns.Add("name=丁") == "c12", "a deleted sign's number is not given again while a larger is there");

                string text = CustomSigns.Text();
                int version = CustomSigns.Version;
                CustomSigns.Load(text);
                Program.Check(CustomSigns.Text() == text && CustomSigns.Saved.Count == 3 && CustomSigns.Version != version, "what is written is read back the same");
                Program.Check(CustomSigns.ByChoiceId("custom.c2") != null && CustomSigns.ByChoiceId("custom.c404") == null && CustomSigns.ByChoiceId("gb.stop") == null && CustomSigns.ByChoiceId(null) == null, "a saved sign is found by its entry's id");
                Program.Check(CustomSigns.Template.Parse("name=" + new string('长', 60)).Get("name").Length == 16, "a name is at most 16 characters");
            }
            finally
            {
                CustomSigns.Load(before);
            }
        }

        private static void AddTile()
        {
            byte[] rgba = Thumbnails.AddTileRgba();
            int n = Thumbnails.Size, opaque = 0;
            for (int i = 3; i < rgba.Length; i += 4)
                opaque += rgba[i] > 0 ? 1 : 0;
            int at(int x, int y) => rgba[(y * n + x) * 4 + 3];
            Program.Check(rgba.Length == n * n * 4 && at(n / 2, n / 2) > 0 && at(2, 2) == 0 && at(n / 16, n / 16) > 0, "the add tile: a plus in the middle, a square near the edge, transparent outside it");
            Program.Check(opaque > n * 4 && opaque < n * n / 4, "it is mostly transparent: " + opaque + " of " + n * n);
            // Dashed: along the top edge of the square there are gaps.
            int gaps = 0;
            for (int x = n / 16; x < n - n / 16; x++)
                gaps += at(x, n / 16) == 0 ? 1 : 0;
            Program.Check(gaps > 10, "its edge is dashed: " + gaps + " gap pixels on the top edge");
            Program.Check(Png.TryDecode(Png.Encode(rgba, n, n, false), out byte[] back, out int w, out _, false) && w == n && back[(n / 2 * n + n / 2) * 4 + 3] == rgba[(n / 2 * n + n / 2) * 4 + 3], "it is written as a PNG");
            Program.Check(Thumbnails.AddTileFile.EndsWith(".png", StringComparison.Ordinal) && Thumbnails.AddTileFile.Contains("r" + Thumbnails.Revision), "its file carries the art's revision");
        }
    }
}
