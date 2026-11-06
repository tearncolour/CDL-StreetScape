using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>A sign the player saved from a picture: its key (the catalogue's id is ChoicePrefix + key) and the settings of the custom template it starts from.</summary>
    public sealed class CustomSign
    {
        public string Key, Spec;
    }

    /// <summary>
    /// The player's own signs from PNG pictures (0.12.29; the owner: "加入支持png图片直接导入的标签页，允许创建自定义路牌…可以保存为专门路牌"). One template whose settings carry everything a board needs: the
    /// picture (the key of its copy in ModsData/CDLC.TS/Custom/images and its size in pixels), the board's shape and size, how the picture fills it and the colour of what it leaves. A placed item
    /// keeps all of that in its own settings, so it stays as it was when the saved sign is changed or deleted. The saved signs (Saved) are the catalogue's entries of the tab 自定义, each the template
    /// with its settings, as a preset is. Not of the standard. Pure; the files are read and written by the mod (CustomStore).
    /// </summary>
    public static class CustomSigns
    {
        public const string TemplateId = "custom.png", Group = "custom", ChoicePrefix = "custom.", Code = "自定义";

        /// <summary>The smallest and the largest a side of a board may be (m).</summary>
        public const float MinSize = 0.1f, MaxSize = 8f;

        /// <summary>The longest side a kept picture has (px): the face of a board is painted at 768 px at the most, and a picture is drawn by many boards.</summary>
        public const int MaxPicture = 1024;

        private static readonly GuideChoice[] kShapes =
        {
            new GuideChoice("round", "矩形（圆角）", "Rectangle, round corners"), new GuideChoice("rect", "矩形（直角）", "Rectangle, square corners"), new GuideChoice("circle", "圆形", "Circle"),
            new GuideChoice("octagon", "八边形", "Octagon"), new GuideChoice("triangle", "三角形（尖角朝上）", "Triangle, apex up"), new GuideChoice("triangle.down", "三角形（尖角朝下）", "Triangle, apex down")
        };

        private static readonly GuideChoice[] kFits =
        {
            new GuideChoice("contain", "完整显示（留边）", "Whole picture (margins)"), new GuideChoice("cover", "裁切填满", "Fill, cropped"), new GuideChoice("fill", "拉伸填满", "Fill, stretched")
        };

        private static readonly GuideChoice[] kGrounds =
        {
            new GuideChoice("white", "白色", "White"), new GuideChoice("black", "黑色", "Black"), new GuideChoice("blue", "蓝色", "Blue"), new GuideChoice("green", "绿色", "Green"),
            new GuideChoice("yellow", "黄色", "Yellow"), new GuideChoice("red", "红色", "Red"), new GuideChoice("brown", "棕色", "Brown"), new GuideChoice("gray", "灰色", "Grey")
        };

        private static bool HasHeight(GuideSpec s) => s.Is("shape", "round") || s.Is("shape", "rect");

        private static readonly GuideTemplate s_Template = new GuideTemplate
        {
            Id = TemplateId,
            Code = Code,
            Zh = "自定义路牌",
            En = "Custom sign",
            Group = Group,
            Keywords = new[] { "自定义", "图片", "PNG", "custom", "picture" },
            Pinyin = "zdylp",
            Fields = new[]
            {
                GuideField.Text("name", "名称", "Name", "自定义路牌", 16),
                GuideField.Choice("shape", "形状", "Shape", "round", kShapes),
                GuideField.Digits("w", "宽度（m）", "Width (m)", "1", 4),
                GuideField.Digits("h", "高度（m，空为按图片比例）", "Height (m; empty: as the picture)", "", 4, HasHeight),
                GuideField.Choice("fit", "图片填充", "Picture", "contain", kFits),
                // Where the picture is on the board and how large (0.12.31; the owner: "可以调整png位置和大小"): 100 % and 50 % are where the fill puts it.
                GuideField.Digits("size", "图片大小（%）", "Picture size (%)", "100", 3),
                GuideField.Digits("px", "左右位置（0 到 100）", "Across (0 to 100)", "50", 3),
                GuideField.Digits("py", "上下位置（0 到 100）", "Up and down (0 to 100)", "50", 3),
                GuideField.Choice("ground", "底色（透明处和留边）", "Ground (clear parts and margins)", "white", kGrounds),
                // 0.12.30 cut every board to its picture; since 0.12.31 the board keeps its shape and its ground shows through the clear parts (the owner: "保留牌型和底色…透明部分相当于会露出底色"), and the
                // cut is a choice.
                GuideField.Flag("cut", "按图片透明处裁掉牌子", "Cut by the picture's clear parts", false),
                // Set when the picture is imported, never by hand: the key of the kept copy and its size in pixels.
                GuideField.Text("img", "（内部）图片", "(internal) picture", "", 16, s => false),
                GuideField.Digits("pw", "（内部）图片宽", "(internal) picture width", "", 5, s => false),
                GuideField.Digits("ph", "（内部）图片高", "(internal) picture height", "", 5, s => false)
            },
            Layout = Layout
        };

        public static GuideTemplate Template => s_Template;

        public static IEnumerable<GuideTemplate> All()
        {
            yield return s_Template;
        }

        /// <summary>Whether a template hash is the custom sign's.</summary>
        public static bool Is(uint templateHash) => templateHash == s_Template.Hash;

        /// <summary>
        /// The masks of the pictures that have clear pixels, by key (0.12.30, CustomMask: CustomStore reads them from the files beside the pictures); null, or null for a key: the picture is opaque and its
        /// board is its whole outline. Called from any thread.
        /// </summary>
        public static Func<string, CustomMask> Masks;

        /// <summary>
        /// The contours of the pictures (0.12.74, PictureContour: the picture's own outline where the mask has 192 cells), by key; null, or null for a key: none is ready yet. The mod's CustomStore reads them
        /// without waiting where a thread is the main one. Called from any thread.
        /// </summary>
        public static Func<string, PictureContour> Contours;

        /// <summary>features.pictureCut of the preferences: Off cuts as 0.12.72 did; Shadow (and Log) cuts that way and, off the building thread, also from the contour and says how the two differ; On cuts from the contour.</summary>
        public static FeatureState PictureCutMode = FeatureState.Shadow;

        /// <summary>The part of a picture (0 to 1 each way, v down) a box shows: the whole of it, or for a cropped fill its middle in the box's proportions (height over width).</summary>
        public static void SourceOf(float pictureAspect, float boxAspect, bool crop, out float u0, out float v0, out float u1, out float v1)
        {
            u0 = v0 = 0f;
            u1 = v1 = 1f;
            if (!crop || pictureAspect <= 0f || boxAspect <= 0f)
                return;
            if (pictureAspect > boxAspect)
            {
                float keep = boxAspect / pictureAspect;
                v0 = (1f - keep) * 0.5f;
                v1 = v0 + keep;
            }
            else
            {
                float keep = pictureAspect / boxAspect;
                u0 = (1f - keep) * 0.5f;
                u1 = u0 + keep;
            }
        }

        /// <summary>How many cells the longer side of a board is cut in (a board of a metre to about 6 mm).</summary>
        private const int kCutCells = 160;

        /// <summary>
        /// The board of a picture with clear pixels: the cells of the part's box that are inside its outline and on an opaque cell of the picture where it is drawn (outside the picture, in the margins, the
        /// board is cut too), traced into loops. Null when no cell is opaque (the whole outline is kept, so that the item can still be seen and picked). shape: a convex outline round the part's middle
        /// (anticlockwise, y up) instead of the part's own (0.12.49: a lamp-post light box with an arched top).
        /// </summary>
        internal static List<List<Point2>> Cut(GuidePart part, GuideElement image, CustomMask mask, float pictureAspect, List<Point2> shape = null)
        {
            FeatureState mode = PictureCutMode;
            Func<string, PictureContour> contours = Contours;
            if (mode == FeatureState.Off || contours == null)
                return CellCut(part, image, mask, pictureAspect, shape);
            PictureContour contour = IsKey(image.Text) ? contours(image.Text) : null;
            if (mode == FeatureState.On)
            {
                if (contour == null)
                {
                    PictureCutUse.NoteNotReady();
                }
                else
                {
                    PictureCutResult made = PictureCut.Make(contour, shape ?? GuideKit.PartOutline(part), part.W, part.H, image.X, image.Y, image.W, image.H, image.Crop, pictureAspect);
                    PictureCutUse.Note(image.Text, made);
                    if (made.Loops != null)
                        return made.Loops.Count > 0 ? made.Loops : null;
                }

                return CellCut(part, image, mask, pictureAspect, shape);
            }

            long start = Stopwatch.GetTimestamp();
            List<List<Point2>> old = CellCut(part, image, mask, pictureAspect, shape);
            double oldMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (contour == null)
                PictureCutShadow.NoteNotReady();
            else
                PictureCutShadow.Offer(new PictureCutShadow.Job
                {
                    Key = image.Text,
                    Contour = contour,
                    Outline = new List<Point2>(shape ?? GuideKit.PartOutline(part)),
                    PartW = part.W,
                    PartH = part.H,
                    ImageX = image.X,
                    ImageY = image.Y,
                    ImageW = image.W,
                    ImageH = image.H,
                    Crop = image.Crop,
                    PictureAspect = pictureAspect,
                    Old = old,
                    OldMs = oldMs
                });
            return old;
        }

        /// <summary>The board as 0.12.72 cut it, from the 192-cell mask: what Off gives, what the shadow compares the contour with, and where a contour cannot be used.</summary>
        internal static List<List<Point2>> CellCut(GuidePart part, GuideElement image, CustomMask mask, float pictureAspect, List<Point2> shape = null)
        {
            List<Point2> outline = shape ?? GuideKit.PartOutline(part);
            bool InOutline(float x, float y)
            {
                for (int i = 0; i < outline.Count; i++)
                {
                    Point2 a = outline[i], b = outline[(i + 1) % outline.Count];
                    if ((b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X) < 0f)
                        return false;
                }

                return true;
            }

            float cell = Math.Max(part.W, part.H) / kCutCells;
            int nx = Math.Max(1, (int)Math.Ceiling(part.W / cell - 1e-3f)), ny = Math.Max(1, (int)Math.Ceiling(part.H / cell - 1e-3f));
            float cw = part.W / nx, ch = part.H / ny;
            SourceOf(pictureAspect, image.H / Math.Max(1e-4f, image.W), image.Crop, out float u0, out float v0, out float u1, out float v1);
            var cells = new bool[nx * ny];
            bool any = false;
            for (int j = 0; j < ny; j++)
            {
                float y = (j + 0.5f) * ch;
                for (int i = 0; i < nx; i++)
                {
                    float x = (i + 0.5f) * cw;
                    float px = (x - image.X) / image.W, py = (y - image.Y) / image.H;
                    bool opaque = px >= 0f && px < 1f && py >= 0f && py < 1f && InOutline(x - part.W * 0.5f, part.H * 0.5f - y) && mask.AtUv(u0 + px * (u1 - u0), v0 + py * (v1 - v0));
                    cells[j * nx + i] = opaque;
                    any |= opaque;
                }
            }

            if (!any)
                return null;
            List<List<Point2>> loops = CutOutline.Trace((i, j) => cells[j * nx + i], nx, ny, cw, ch, -part.W * 0.5f, part.H * 0.5f);
            return loops.Count > 0 ? loops : null;
        }

        private static float Size(double metres, float fallback) =>
            double.IsNaN(metres) || double.IsInfinity(metres) || metres <= 0.0 ? fallback : (float)Math.Max(MinSize, Math.Min(MaxSize, metres));

        private static GuideColour Ground(string name)
        {
            switch (name)
            {
                case "black": return GuideColour.Black;
                case "blue": return GuideColour.Blue;
                case "green": return GuideColour.Green;
                case "yellow": return GuideColour.Yellow;
                case "red": return GuideColour.Red;
                case "brown": return GuideColour.Brown;
                case "gray": return GuideColour.Gray;
                default: return GuideColour.White;
            }
        }

        /// <summary>
        /// The board: one part of the shape and size asked for, in the ground colour, with the picture as its one element (GuideElementKind.Image, its key as the text). Whole: the picture as large
        /// as fits, on a rectangle in its middle, on another shape inside the outline (Inscribed); cropped: the part's box, the picture cut to its proportions (GuideElement.Crop); stretched: the
        /// part's box. Then made larger or smaller about its middle (size) and moved (PlaceAlong). The painter clips it to the outline (a rectangle's round corners round the picture's), and the
        /// ground shows where the picture is clear. A picture of no known size is taken as square. With cut, a picture with clear pixels cuts the board to its opaque part (Cut): the margins and the
        /// ground go with the clear pixels.
        /// </summary>
        private static GuideBoard Layout(GuideSpec s)
        {
            float w = Size(s.Number("w", 1.0), 1f);
            double pw = s.Number("pw", 0.0), ph = s.Number("ph", 0.0);
            float aspect = pw >= 1.0 && ph >= 1.0 ? (float)(ph / pw) : 1f;
            var part = new GuidePart { Ground = Ground(s.Get("ground")) };
            string shape = s.Get("shape");
            switch (shape)
            {
                case "circle":
                    part.Shape = GuidePartShape.Circle;
                    part.W = part.H = w;
                    part.Radius = w * 0.5f;
                    break;
                case "octagon":
                    part.Shape = GuidePartShape.Octagon;
                    part.W = part.H = w;
                    break;
                case "triangle":
                case "triangle.down":
                {
                    bool up = shape == "triangle";
                    float radius = 0.05f * w;
                    GuideKit.TriangleBounds(w, radius, up, out _, out _, out float tw, out float th);
                    part.Shape = up ? GuidePartShape.Triangle : GuidePartShape.TriangleDown;
                    part.W = tw;
                    part.H = th;
                    part.Side = w;
                    part.Radius = radius;
                    break;
                }
                default:
                {
                    float h = s.Get("h").Length > 0 ? Size(s.Number("h", w * aspect), w * aspect) : Size(w * aspect, w);
                    part.W = w;
                    part.H = h;
                    part.Radius = shape == "rect" ? 0f : 0.06f * Math.Min(w, h);
                    break;
                }
            }

            float bx = 0f, by = 0f, bw = part.W, bh = part.H;
            string fit = s.Get("fit");
            if (fit == "contain" && part.Shape == GuidePartShape.RoundRect)
            {
                bw = Math.Min(part.W, part.H / aspect);
                bh = bw * aspect;
                bx = (part.W - bw) * 0.5f;
                by = (part.H - bh) * 0.5f;
            }
            else if (fit == "contain")
            {
                Inscribed(part, aspect, out bx, out by, out bw, out bh);
            }

            float k = (float)Math.Max(MinScale, Math.Min(MaxScale, s.Number("size", 100.0))) / 100f;
            if (float.IsNaN(k))
                k = 1f;
            bx += bw * (1f - k) * 0.5f;
            by += bh * (1f - k) * 0.5f;
            bw *= k;
            bh *= k;
            bx = PlaceAlong(bx, part.W - bw, s.Number("px", 50.0));
            by = PlaceAlong(by, part.H - bh, s.Number("py", 50.0));

            var picture = new GuideElement { Kind = GuideElementKind.Image, X = bx, Y = by, W = bw, H = bh, Text = s.Get("img"), Crop = fit == "cover" };
            part.Elements.Add(picture);
            // Cut (0.12.30): the board is only where the picture is opaque; a pixel that is not wholly opaque is clear (CustomMask).
            CustomMask mask = s.Flag("cut") && IsKey(picture.Text) ? Masks?.Invoke(picture.Text) : null;
            if (mask != null)
            {
                part.Cut = Cut(part, picture, mask, aspect);
                picture.Opaque = part.Cut != null;
            }

            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        /// <summary>The smallest and the largest size of a picture on its board (% of what its fill makes it).</summary>
        public const double MinScale = 10.0, MaxScale = 400.0;

        /// <summary>
        /// Where a picture's edge is (m from the part's edge, one way) for a position of 0 to 100: 50 where its fill put it (at), 0 against the part's near edge, 100 against its far edge (free: the part
        /// less the picture, below 0 when the picture is the larger), in a straight line between.
        /// </summary>
        public static float PlaceAlong(float at, float free, double position)
        {
            float p = double.IsNaN(position) ? 50f : (float)Math.Max(0.0, Math.Min(100.0, position));
            return p <= 50f ? at * p / 50f : at + (free - at) * (p - 50f) / 50f;
        }

        /// <summary>
        /// The largest box of the picture's proportions (height over width) inside a convex outline, in the part's frame (from its top left, y down): centred across, at the height that gives the most
        /// (the middle of a circle or an octagon, low in a triangle that stands on its base). Each height is searched for its scale by halving, the corners checked against the outline's sides.
        /// </summary>
        private static void Inscribed(GuidePart part, float aspect, out float x, out float y, out float w, out float h)
        {
            List<Point2> outline = GuideKit.PartOutline(part);
            bool Inside(float px, float py)
            {
                // Counter-clockwise: every side has the point on its left.
                for (int i = 0; i < outline.Count; i++)
                {
                    Point2 a = outline[i], b = outline[(i + 1) % outline.Count];
                    if ((b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X) < -1e-6f)
                        return false;
                }

                return true;
            }

            float best = 0f, bestY = 0f;
            const int heights = 40;
            for (int k = 0; k <= heights; k++)
            {
                float cy = -part.H * 0.5f + part.H * k / heights, lo = 0f, hi = Math.Max(part.W, part.H / aspect);
                for (int step = 0; step < 24; step++)
                {
                    float mid = (lo + hi) * 0.5f, hw = mid * 0.5f, hh = mid * aspect * 0.5f;
                    if (Inside(-hw, cy - hh) && Inside(hw, cy - hh) && Inside(hw, cy + hh) && Inside(-hw, cy + hh))
                        lo = mid;
                    else
                        hi = mid;
                }

                if (lo > best)
                {
                    best = lo;
                    bestY = cy;
                }
            }

            w = best;
            h = best * aspect;
            x = (part.W - w) * 0.5f;
            y = part.H * 0.5f - bestY - h * 0.5f;
        }

        /// <summary>
        /// The settings of a picture just imported: its key and size, the file's name (without its extension, cut to the field) as the sign's name, and a width of a metre for its longer side
        /// (the height follows the picture).
        /// </summary>
        public static string SpecForPicture(string key, int width, int height, string fileName)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(fileName ?? "");
            float w = width >= height || width <= 0 || height <= 0 ? 1f : (float)width / height;
            GuideSpec spec = s_Template.Default()
                .With("img", key)
                .With("pw", width.ToString(CultureInfo.InvariantCulture))
                .With("ph", height.ToString(CultureInfo.InvariantCulture))
                .With("w", w.ToString("0.##", CultureInfo.InvariantCulture));
            if (name.Length > 0)
                spec = spec.With("name", name);
            return spec.ToString();
        }

        /// <summary>The name a sign is listed by: its own, or the template's when it has none.</summary>
        public static string NameOf(string spec)
        {
            string name = s_Template.Parse(spec).Get("name");
            return name.Length > 0 ? name : s_Template.Zh;
        }

        /// <summary>The key of a picture: FNV-1a (64 bits) of its file's bytes, as 16 hexadecimal digits, so that the same file is kept once and another file is never taken for it.</summary>
        public static string KeyOf(byte[] data)
        {
            ulong h = 14695981039346656037ul;
            foreach (byte b in data)
            {
                h ^= b;
                h *= 1099511628211ul;
            }

            return h.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>Whether a text is a picture's key (what goes into a file name: nothing else is read as one).</summary>
        public static bool IsKey(string key)
        {
            if (key == null || key.Length != 16)
                return false;
            foreach (char c in key)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ the saved signs

        private static CustomSign[] s_Saved = new CustomSign[0];

        /// <summary>The saved signs in the order they were saved. A new array at every change (the thumbnails' worker may be reading the old one).</summary>
        public static IReadOnlyList<CustomSign> Saved => s_Saved;

        /// <summary>Changes whenever the saved signs do (the catalogue is made again).</summary>
        public static int Version { get; private set; }

        public static CustomSign ByChoiceId(string id)
        {
            if (id == null || !id.StartsWith(ChoicePrefix, StringComparison.Ordinal))
                return null;
            string key = id.Substring(ChoicePrefix.Length);
            foreach (CustomSign sign in s_Saved)
            {
                if (sign.Key == key)
                    return sign;
            }

            return null;
        }

        /// <summary>Reads the saved signs from the text of their file: a line for each, the key, a tab, the settings. Lines that are not that, keys that are not "c" and a number, and repeated keys are left out.</summary>
        public static void Load(string text)
        {
            var list = new List<CustomSign>();
            var seen = new HashSet<string>();
            foreach (string line in (text ?? "").Split('\n'))
            {
                string trimmed = line.TrimEnd('\r');
                int tab = trimmed.IndexOf('\t');
                if (tab <= 0)
                    continue;
                string key = trimmed.Substring(0, tab);
                if (NumberOf(key) < 0 || !seen.Add(key))
                    continue;
                list.Add(new CustomSign { Key = key, Spec = s_Template.Parse(trimmed.Substring(tab + 1)).ToString() });
            }

            s_Saved = list.ToArray();
            Version++;
        }

        /// <summary>The text of the file of the saved signs (Load reads it back).</summary>
        public static string Text()
        {
            var sb = new StringBuilder();
            foreach (CustomSign sign in s_Saved)
                sb.Append(sign.Key).Append('\t').Append(sign.Spec).Append('\n');
            return sb.ToString();
        }

        private static int NumberOf(string key) =>
            key != null && key.Length > 1 && key[0] == 'c' && int.TryParse(key.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;

        /// <summary>Saves a new sign with these settings; its key (the next number after the largest there is, so that a deleted sign's key is not given again in the same list).</summary>
        public static string Add(string spec)
        {
            int next = 1;
            foreach (CustomSign sign in s_Saved)
                next = Math.Max(next, NumberOf(sign.Key) + 1);
            string key = "c" + next.ToString(CultureInfo.InvariantCulture);
            var list = new List<CustomSign>(s_Saved) { new CustomSign { Key = key, Spec = s_Template.Parse(spec).ToString() } };
            s_Saved = list.ToArray();
            Version++;
            return key;
        }

        /// <summary>Gives a saved sign new settings; false when there is no sign of that key.</summary>
        public static bool Update(string key, string spec)
        {
            var list = new List<CustomSign>(s_Saved);
            int at = list.FindIndex(sign => sign.Key == key);
            if (at < 0)
                return false;
            list[at] = new CustomSign { Key = key, Spec = s_Template.Parse(spec).ToString() };
            s_Saved = list.ToArray();
            Version++;
            return true;
        }

        public static bool Remove(string key)
        {
            var list = new List<CustomSign>(s_Saved);
            if (list.RemoveAll(sign => sign.Key == key) == 0)
                return false;
            s_Saved = list.ToArray();
            Version++;
            return true;
        }
    }
}
