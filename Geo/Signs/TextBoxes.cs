using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// What a text light box adds to its board (GuideBoard.Text, 0.12.75): the job its plan is made from, the layout of the text (its box and its lines: known at once, before any plan), the colours, and
    /// how far the letters stand from what holds them. The plan (glyph meshes, rear frame) is made apart and kept (TextPlans).
    /// </summary>
    public sealed class TextBoard
    {
        public TextPlanJob Job;

        /// <summary>The layout of the text in the font that is used: null when there is no font at all.</summary>
        public TextLayoutResult Layout;

        public SolidColour Glow, Return;

        /// <summary>How deep a letter's return is, and how far the letters stand from the rails (or the wall): the studs' length (m).</summary>
        public float Depth, Stand;

        /// <summary>On a building: the planner's base frame or none (the studs of the letters straight to the wall, or feet on a roof); and the radius of a curved wall the board is bent to (m, signed; 0 flat).</summary>
        public TextMount Mount;
        public float Radius;

        /// <summary>How far the letters lean back (degrees, -TextTilt.MaxDeg to TextTilt.MaxDeg: the elevation angle, negative the depression angle: they look down), and whether they stand on the triangular frames of the standing light box even when upright (a post, a roof).</summary>
        public float TiltDeg;
        public bool Triangular;

        /// <summary>The font that was named is not there (another PC's): the default one is used.</summary>
        public bool FontFell;

        /// <summary>The font could not be read at all (the default one either): nothing is drawn.</summary>
        public bool NoFont;

        /// <summary>From the back plane to the face of the letters (m).</summary>
        public float Reach => Stand + Depth;

        /// <summary>The ink area the support planner counts for the board (m2): the plan's when it is made, else an estimate from the characters (a third of the letter height squared for a wide one, a ninth for any other).</summary>
        public float InkArea()
        {
            TextPlanResult plan = TextPlans.Peek(Job);
            if (plan != null && plan.Frame != null && plan.Frame.EffectiveArea > 0f)
                return plan.Frame.EffectiveArea;
            return EstimatedInk(Layout, Job.LetterHeight);
        }

        /// <summary>An estimate of the ink of a layout before its glyphs are made: this mod's guess (a third of the letter height squared for a Chinese character, a ninth for a letter or a figure), not a measurement.</summary>
        public static float EstimatedInk(TextLayoutResult layout, float letterHeight)
        {
            if (layout == null)
                return 0f;
            float area = 0f;
            foreach (PlacedGlyph glyph in layout.Glyphs)
            {
                if (glyph.Missing || glyph.Codepoint == ' ')
                    continue;
                area += (TextLayout.IsWide(glyph.Codepoint) ? 0.33f : 0.2f * 0.55f) * letterHeight * letterHeight;
            }

            return area;
        }
    }

    /// <summary>
    /// The text light box (文字灯箱, 0.12.75; the owner, 2 Nov: "加入一个文字输入风格裁切，支持做成文字灯箱，可以选择字体和发光颜色，自动计算支撑架，支持挂靠在常规路牌支架和吸附建筑上"). Form 1 of
    /// DESIGN-text-lightbox.md: channel letters, each character its own solid (a lit face in the glow colour, a return of its own colour and depth, no plate behind), on the rails and studs the rear frame
    /// plans, flat on a wall or on a post. A board like any other: it is laid out from its settings (the box of the text), so every support and every move of the tool applies; its letters are made by the
    /// builder from the plan (TextPlans). In the player's own tab beside the picture import. Not of a standard: the sizes and weights are this mod's estimates.
    /// </summary>
    public static class TextBoxes
    {
        public const string TemplateId = "custom.text", Group = "custom", Code = "文字灯箱";

        /// <summary>The smallest and the largest letter (m), the least the box is (m), and the studs' length (m): a rail's size and a little over.</summary>
        public const float MinLetter = 0.15f, MaxLetter = 3f, MinBox = 0.1f, Stand = 0.045f;

        private static readonly GuideChoice[] kAligns =
        {
            new GuideChoice("centre", "居中", "Centre"), new GuideChoice("left", "靠左", "Left"), new GuideChoice("right", "靠右", "Right")
        };

        private static readonly GuideChoice[] kGlows =
        {
            new GuideChoice("warm", "暖白", "Warm white"), new GuideChoice("white", "冷白", "Cool white"), new GuideChoice("red", "红", "Red"), new GuideChoice("gold", "金黄", "Gold"),
            new GuideChoice("blue", "蓝", "Blue"), new GuideChoice("pink", "粉", "Pink"), new GuideChoice("green", "绿", "Green")
        };

        private static readonly GuideChoice[] kMounts = { new GuideChoice("auto", "自动", "Automatic"), new GuideChoice("none", "无", "None") };

        private static readonly GuideChoice[] kReturns =
        {
            new GuideChoice("black", "黑", "Black"), new GuideChoice("white", "白", "White"), new GuideChoice("steel", "不锈钢", "Stainless steel"), new GuideChoice("gray", "深灰", "Dark grey"),
            new GuideChoice("gold", "金", "Gold")
        };

        // The font row is always shown: the default, the player's files and the system's fonts the reader can draw (TextFonts). A system font's file name is accepted at once, before the list has read it.
        private static readonly GuideField s_Font = FontField();

        private static GuideField FontField()
        {
            GuideField field = GuideField.Choice("font", "字体", "Font", TextFonts.DefaultChoice, TextFonts.Choices());
            field.Accepts = TextFonts.IsKnownValue;
            return field;
        }

        private static readonly GuideTemplate s_Template = new GuideTemplate
        {
            Id = TemplateId,
            Code = Code,
            Zh = "文字灯箱（发光字）",
            En = "Text light box (channel letters)",
            Group = Group,
            Keywords = new[] { "文字", "灯箱", "发光字", "招牌", "text", "light box", "channel letters", "sign" },
            Pinyin = "wzdx",
            Standalone = true,
            Fields = new[]
            {
                GuideField.Text("text", "文字（\\n 换行）", "Text (\\n is a new line)", "文字灯箱", 40).Full().Unicode(),
                s_Font,
                GuideField.Flag("fb", "缺字用默认字体补", "Fill gaps (default font)", true, s => s.Get("font") != TextFonts.DefaultChoice),
                GuideField.Choice("style", "字形", "Style", TextStyles.Plain, TextStyles.Choices()),
                GuideField.Choice("curve", "弧形", "Curve", TextStyles.Straight, TextStyles.CurveChoices()),
                GuideField.Digits("sweep", "弧度（°）", "Sweep (°)", "120", 3, s => s.Get("curve") != TextStyles.Straight),
                GuideField.Digits("h", "字高（m）", "Letter height (m)", "0.5", 5),
                GuideField.Digits("sp", "字距（%）", "Letter spacing (%)", "0", 3),
                GuideField.Digits("ls", "行距（%）", "Line pitch (%)", "115", 3),
                GuideField.Choice("align", "对齐", "Alignment", "centre", kAligns),
                GuideField.Choice("glow", "发光颜色", "Glow colour", "warm", kGlows),
                GuideField.Choice("glow2", "侧壁发光色", "Side glow colour", "gold", kGlows, s => TextStyles.IsTwoTone(s.Get("style"))),
                GuideField.Choice("ret", "侧壁颜色", "Return colour", "black", kReturns, s => !TextStyles.IsTwoTone(s.Get("style")) && !TextStyles.IsNeon(s.Get("style"))),
                GuideField.Digits("depth", "厚度（cm）", "Letter depth (cm)", "6", 3, s => !TextStyles.IsNeon(s.Get("style"))),
                GuideField.Digits("tilt", "仰角（负为俯角）", "Tilt (− looks down)", "0", 3).Signed().Slider(-TextTilt.MaxDeg, TextTilt.MaxDeg, 1),
                GuideField.Flag("tri", "三角支架", "Triangular frame", false),
                GuideField.Choice("mount", "建筑托架框", "Wall frame", "auto", kMounts),
                // The radius of a curved wall (m, signed), set by the tool where the facade was read: not a thing to type.
                GuideField.Text("arc", "弧度半径", "Facade radius", "0", 8, s => false)
            },
            Layout = Layout
        };

        static TextBoxes()
        {
            TextFonts.Changed += OnFontsChanged;
        }

        /// <summary>The font list changed on a worker: the field's choices are made again and what was made without the new list is asked for again.</summary>
        private static void OnFontsChanged()
        {
            RefreshFonts();
            TextPlans.BumpEpoch();
        }

        public static GuideTemplate Template => s_Template;

        public static IEnumerable<GuideTemplate> All()
        {
            yield return s_Template;
        }

        /// <summary>features.textBox: the template is in the catalogue when it is on; items already placed keep drawing either way.</summary>
        public static bool Enabled
        {
            get => s_Template.Standalone;
            set => s_Template.Standalone = value;
        }

        /// <summary>The choices of the font field made again from the fonts folder (the list is built when the template is, which may be before the folder is known).</summary>
        public static void RefreshFonts() => s_Font.Choices = TextFonts.Choices();

        /// <summary>Whether a template hash is the text light box's.</summary>
        public static bool Is(uint templateHash) => templateHash == s_Template.Hash;

        private static float Number(GuideSpec s, string key, double fallback, double low, double high)
        {
            double v = s.Number(key, fallback);
            if (double.IsNaN(v) || double.IsInfinity(v))
                v = fallback;
            return (float)Math.Max(low, Math.Min(high, v));
        }

        public static SolidColour GlowOf(string name)
        {
            switch (name)
            {
                case "white": return SolidColour.GlowWhite;
                case "red": return SolidColour.GlowRed;
                case "gold": return SolidColour.GlowGold;
                case "blue": return SolidColour.GlowBlue;
                case "pink": return SolidColour.GlowPink;
                case "green": return SolidColour.GlowGreen;
                default: return SolidColour.GlowWarm;
            }
        }

        public static SolidColour ReturnOf(string name)
        {
            switch (name)
            {
                case "white": return SolidColour.White;
                case "steel": return SolidColour.Steel;
                case "gray": return SolidColour.DarkGray;
                case "gold": return SolidColour.Gold;
                default: return SolidColour.Black;
            }
        }

        private static TextAlign AlignOf(string name) => name == "left" ? TextAlign.Left : name == "right" ? TextAlign.Right : TextAlign.Centre;

        /// <summary>The text of the settings with the typed \n made a line break.</summary>
        public static string TextOf(GuideSpec s) => s.Get("text").Replace("\\n", "\n");

        /// <summary>The job a spec's plan is made from, and the font id it names (the default's when the named one cannot be read).</summary>
        internal static TextPlanJob JobOf(GuideSpec s, out bool fontFell)
        {
            float height = Number(s, "h", 0.5, MinLetter, MaxLetter);
            float spacing = Number(s, "sp", 0, 0, 50) / 100f;
            float lines = Number(s, "ls", 115, 80, 200) / 100f;
            int fontId = TextFonts.IdOf(s.Get("font"));
            fontFell = fontId != TextFonts.DefaultId && TextFonts.Get(fontId) == null;
            if (fontFell)
                fontId = TextFonts.DefaultId;
            float sweep = TextStyles.SignedSweep(s.Get("curve"), Number(s, "sweep", 120, ArcInfo.MinSweep, ArcInfo.MaxSweep));
            TextArt art = TextStyles.Art(s.Get("style"), fontId != TextFonts.DefaultId && s.Flag("fb"), sweep);
            return new TextPlanJob(TextOf(s), fontId, height, spacing, lines, AlignOf(s.Get("align")), TextPlans.RelativeFlatness(height), art);
        }

        /// <summary>
        /// The board: one part, the box of the text (its width and height from the layout, at least MinBox), no picture; the payload carries the job, the layout, the colours and the depth. The
        /// layout is the cheap part (advances of at most 36 characters): the glyphs and the frame are the plan's, made on a worker.
        /// </summary>
        private static GuideBoard Layout(GuideSpec s)
        {
            TextPlanJob job = JobOf(s, out bool fontFell);
            TextFaces.Fonts(job, TextFonts.Get, out FontOutlines font, out FontOutlines fallback);
            var text = new TextBoard
            {
                Job = job,
                Glow = GlowOf(s.Get("glow")),
                Return = TextStyles.IsTwoTone(s.Get("style")) ? GlowOf(s.Get("glow2")) : ReturnOf(s.Get("ret")),
                Depth = Number(s, "depth", 6, 3, 12) / 100f,
                Stand = Stand,
                Mount = s.Get("mount") == "none" ? TextMount.None : TextMount.Auto,
                Radius = Number(s, "arc", 0, -ArcFit.MaxRadius, ArcFit.MaxRadius),
                TiltDeg = Number(s, "tilt", 0, -TextTilt.MaxDeg, TextTilt.MaxDeg),
                Triangular = s.Flag("tri"),
                FontFell = fontFell,
                NoFont = font == null
            };
            float width = MinBox, height = MinBox;
            if (font != null)
            {
                text.Layout = ArcTextLayout.Layout(job.Text, job.Style, TextFaces.Of(font, fallback), job.Art);
                // A leaning letter reaches out on both sides by half the lean of its height: the box has room for it (on an arc the layout's box has it already).
                width = Math.Max(MinBox, text.Layout.Width + (text.Layout.Arc == null ? job.Art.Slant * job.LetterHeight : 0f));
                // A neon tube is a diameter deep, whatever depth was set: it is the tube that the studs hold.
                if (job.Art.Effect == TextEffect.Neon)
                    text.Depth = 2f * TextStyles.TubeRadius(job.LetterHeight, job.Art) * text.Layout.MetresPerEm;
                height = Math.Max(MinBox, text.Layout.Height);
            }

            var part = new GuidePart { Ground = GuideColour.Black, W = width, H = height, Radius = 0f };
            var board = new GuideBoard { H = job.LetterHeight, Text = text };
            board.Parts.Add(part);
            return board;
        }

        /// <summary>
        /// The editor's notes of a text light box (the settings as written): only what the player can act on (TextHints.Shown): the font that is not here, the characters it lacks, a text cut at the
        /// limits, a long rod, an island too small to hold, a text too heavy for one mesh. The figures of the frame (islands, studs, rails, area, mass) are in the log (TextPlans), the font folder's
        /// note is in the log at the start of the game. Pure but for the plan it asks for: it asks the cache and never makes one; before the plan only the layout's notes are given.
        /// </summary>
        public static List<string> Hints(string spec, bool zh)
        {
            var lines = new List<string>();
            TextFonts.RescanSoon(4.0);
            GuideSpec parsed = s_Template.Parse(spec ?? "");
            TextPlanJob job = JobOf(parsed, out bool fontFell);
            if (fontFell)
                lines.Add(zh ? "找不到所选字体（不在本机？）：改用默认字体。" : "Font not found on this PC: the default font is used.");
            TextPlanResult plan = TextPlans.Peek(job);
            FontOutlines hintFont = TextFonts.Get(job.FontId);
            if (hintFont != null)
            {
                // The one line when the length made the support a gantry (0.12.76): the layout is the cheap part.
                string gantry = TextSupports.Hint(TextLayout.Layout(job.Text, job.Style, TextFace.Of(hintFont)).Width, zh);
                if (gantry != null)
                    lines.Add(gantry);
            }

            List<TextWarning> warnings = plan != null ? plan.Warnings : null;
            if (plan == null)
            {
                TextFaces.Fonts(job, TextFonts.Get, out FontOutlines font, out FontOutlines fallback);
                if (font != null)
                    warnings = TextWarnings.Of(ArcTextLayout.Layout(job.Text, job.Style, TextFaces.Of(font, fallback), job.Art), null);
            }

            if (warnings != null)
            {
                foreach (TextWarning warning in warnings)
                {
                    if (TextHints.Shown(warning))
                        lines.Add(TextHints.Of(warning, zh));
                }
            }

            // Without the base frame on a building the studs carry the letters (0.12.76): one line when that is more than a stud carries.
            if (TextSupports.Enabled && plan != null && plan.Frame != null && parsed.Get("mount") == "none")
            {
                WallFramePlan studs = TextWallFrame.Plan(plan.Frame, FrameStyle.For(job.LetterHeight), TextMount.None);
                if (studs.TooHeavyForStuds)
                    lines.Add(zh ? $"无托架框：每个螺柱承重约 {studs.KgPerStud:0} kg，偏重：改回自动。" : $"No frame: each stud carries about {studs.KgPerStud:0} kg, too much: set to auto.");
            }

            return lines;
        }
    }

    /// <summary>The editor's sentences for the hints of a text light box (TextWarning): both languages, the numbers put in. The planner knows no language.</summary>
    public static class TextHints
    {
        /// <summary>
        /// Whether the editor says it: only what the player can act on. That strokes are held by rods, that a narrow one has one stud, and that the curves were cut a step coarser are worked out by
        /// the planner and the builder and only go in the log; a text drawn as bars (TooDetailed with a length) is something to change.
        /// </summary>
        public static bool Shown(TextWarning warning)
        {
            switch (warning.Kind)
            {
                case TextWarningKind.HeldByRods:
                case TextWarningKind.SingleStud:
                    return false;
                case TextWarningKind.TooDetailed:
                    return warning.Metres > 0f;
                default:
                    return true;
            }
        }

        public static string Of(TextWarning warning, bool zh)
        {
            string number = warning.Count.ToString(CultureInfo.InvariantCulture);
            switch (warning.Kind)
            {
                case TextWarningKind.FontMissing:
                    return zh ? "没有可用的字体：什么也画不出来。" : "No font can be read: nothing can be drawn.";
                case TextWarningKind.MissingCharacter:
                    string shown = warning.Codepoint > 0x20 ? TextClusters.TextOf(warning.Codepoint) : "?";
                    if (shown == "�")
                        shown = "?";
                    return zh ? $"字体里没有“{shown}”：这个字留空。" : $"The font has no \"{shown}\": it is left blank.";
                case TextWarningKind.TextCut:
                    return zh ? $"最多 3 行、每行 12 字、共 {number} 字：多的未排入。" : $"Up to 3 lines of 12, {number} characters: the rest is left out.";
                case TextWarningKind.HeldByRods:
                    return zh ? $"{number} 个笔画块不在横杆上，用细杆接到横杆。" : $"{number} stroke group(s) are not crossed by a rail: held by rods to it.";
                case TextWarningKind.LongRod:
                    return zh
                        ? $"{number} 根接杆较长（最长 {warning.Metres.ToString("0.00", CultureInfo.InvariantCulture)} m，超过字高的 30%）：放大字、加大字距，或贴墙安装。"
                        : $"{number} rod(s) are long (the longest {warning.Metres.ToString("0.00", CultureInfo.InvariantCulture)} m, over 30% of the letter height): make the letters larger, or set it on a wall.";
                case TextWarningKind.SingleStud:
                    return zh ? $"{number} 个笔画块太窄，只能打一个螺柱。" : $"{number} stroke group(s) are too narrow for two studs: one stud each.";
                case TextWarningKind.FloatingIsland:
                    return zh ? $"{number} 个笔画块太小，无法固定：请放大字。" : $"{number} stroke group(s) too small to fix: enlarge the letters.";
                case TextWarningKind.ArcLimited:
                    return zh ? $"弧度最大 {number}°：再弯内侧会挤在一起。" : $"Bent to {number}° at most: the inside letters would crowd.";
                case TextWarningKind.TooDetailed:
                    return warning.Metres > 0f
                        ? (zh ? "字太多太复杂，只画成发光条：请减字或缩小。" : "Too detailed: drawn as lit bars. Use fewer or smaller letters.")
                        : (zh ? "字的细节很多：曲线放粗了 " + number + " 级，以装进一个网格。" : "Many details: the curves were cut " + number + " step(s) coarser to fit one mesh.");
                default:
                    return "";
            }
        }
    }
}
