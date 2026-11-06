using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>What a standing light box adds to its board (GuideBoard.Stand, 0.12.76): how it leans, how high it stands, how deep the housing is and how it is lit. The text is the board's TextBoard.</summary>
    public sealed class StandBoard
    {
        /// <summary>How far the box leans back from upright (degrees, -StandBoxes.MaxTilt to StandBoxes.MaxTilt; negative: its face looks down); it is built as the support allows (StandBoxes.Framed).</summary>
        public float TiltDeg;

        /// <summary>The height of the box's lower back edge above the ground (m).</summary>
        public float Lift;

        /// <summary>The housing's depth (m) and the light-coloured margin round the text (m).</summary>
        public float Depth, Margin;

        /// <summary>The face is lit and the letters dark (the classic light box); else the face is dark and the letters lit.</summary>
        public bool LitGround;

        /// <summary>The colour of the housing and its bezel.</summary>
        public SolidColour Frame;
    }

    /// <summary>
    /// The standing light box (立式灯箱, 0.12.76; the owner, 2 Nov: "加入能放置在地面竖直的灯箱类型，设计对应支架，支持调节仰角，生成对应三角支架"): a shallow housing with a lit face and the text of the text light
    /// box on it, standing on the ground on the posts of the support the planner gives it (one, two or three), each with its own triangular frame (StandPlanner). Either the face is lit and the letters dark,
    /// or the face is dark and the letters lit. A board like any other (its box is laid out from the text and the margin), in the player's own tab beside the text light box. The box leans back by the tilt.
    /// Not of a standard: sizes and weights are this mod's estimates.
    /// </summary>
    public static class StandBoxes
    {
        public const string TemplateId = "custom.stand", Group = "custom", Code = "立式灯箱";

        public const float MaxTilt = 30f, Bezel = 0.035f, MinBox = 0.3f;

        private static readonly GuideChoice[] kFaces =
        {
            new GuideChoice("ground", "底板发光（字深色）", "Lit face, dark letters"), new GuideChoice("letters", "字发光（底深色）", "Dark face, lit letters")
        };

        private static readonly GuideChoice[] kAligns =
        {
            new GuideChoice("centre", "居中", "Centre"), new GuideChoice("left", "靠左", "Left"), new GuideChoice("right", "靠右", "Right")
        };

        private static readonly GuideChoice[] kGlows =
        {
            new GuideChoice("warm", "暖白", "Warm white"), new GuideChoice("white", "冷白", "Cool white"), new GuideChoice("red", "红", "Red"), new GuideChoice("gold", "金黄", "Gold"),
            new GuideChoice("blue", "蓝", "Blue"), new GuideChoice("pink", "粉", "Pink"), new GuideChoice("green", "绿", "Green")
        };

        private static readonly GuideChoice[] kFrames =
        {
            new GuideChoice("steel", "不锈钢", "Stainless steel"), new GuideChoice("black", "黑", "Black"), new GuideChoice("white", "白", "White"), new GuideChoice("gray", "深灰", "Dark grey"),
            new GuideChoice("gold", "金", "Gold")
        };

        private static readonly GuideField s_Font = GuideField.Choice("font", "字体", "Font", TextFonts.DefaultChoice, TextFonts.Choices(), s => TextFonts.Files.Count > 0);

        private static readonly GuideTemplate s_Template = new GuideTemplate
        {
            Id = TemplateId,
            Code = Code,
            Zh = "立式灯箱（落地）",
            En = "Standing light box",
            Group = Group,
            Keywords = new[] { "立式", "灯箱", "落地", "广告牌", "仰角", "stand", "light box", "pylon", "billboard", "sign" },
            Pinyin = "lsdx",
            Standalone = true,
            Fields = new[]
            {
                GuideField.Text("text", "文字（\\n 换行）", "Text (\\n is a new line)", "立式灯箱", 40).Full().Unicode(),
                s_Font,
                GuideField.Choice("face", "发光方式", "Lit part", "ground", kFaces),
                GuideField.Digits("h", "字高（m）", "Letter height (m)", "0.4", 5),
                GuideField.Digits("sp", "字距（%）", "Letter spacing (%)", "0", 3),
                GuideField.Digits("ls", "行距（%）", "Line pitch (%)", "115", 3),
                GuideField.Choice("align", "对齐", "Alignment", "centre", kAligns),
                GuideField.Choice("glow", "发光颜色", "Glow colour", "warm", kGlows),
                GuideField.Choice("frame", "箱体颜色", "Housing colour", "steel", kFrames),
                GuideField.Digits("margin", "边距（cm）", "Margin (cm)", "8", 2),
                GuideField.Digits("depth", "箱厚（cm）", "Housing depth (cm)", "20", 2),
                GuideField.Digits("tilt", "仰角（负为俯角）", "Tilt (− looks down)", "10", 3).Signed().Slider(-MaxTilt, MaxTilt, 1),
                GuideField.Digits("lift", "离地（cm）", "Height of the lower edge (cm)", "30", 3)
            },
            Layout = Layout
        };

        public static GuideTemplate Template => s_Template;

        public static IEnumerable<GuideTemplate> All()
        {
            yield return s_Template;
        }

        /// <summary>features.standBox: the template is in the catalogue when it is on; items already placed keep drawing either way.</summary>
        public static bool Enabled
        {
            get => s_Template.Standalone;
            set => s_Template.Standalone = value;
        }

        /// <summary>The choices of the font field made again from the fonts folder.</summary>
        public static void RefreshFonts() => s_Font.Choices = TextFonts.Choices();

        public static bool Is(uint templateHash) => templateHash == s_Template.Hash;

        /// <summary>Whether a support's posts are the ones the box stands on with its frames (a single, a double or a triple post); on any other support the box is made flat and upright, as a lit board.</summary>
        public static bool Framed(SupportKind kind) => kind == SupportKind.SinglePole || kind == SupportKind.DoublePole || kind == SupportKind.TriplePole;

        private static float Number(GuideSpec s, string key, double fallback, double low, double high)
        {
            double v = s.Number(key, fallback);
            if (double.IsNaN(v) || double.IsInfinity(v))
                v = fallback;
            return (float)Math.Max(low, Math.Min(high, v));
        }

        /// <summary>The tilt a spec asks for as typed (not held to the range: Hints says when it is).</summary>
        public static float TypedTilt(GuideSpec s)
        {
            double v = s.Number("tilt", 10);
            return double.IsNaN(v) || double.IsInfinity(v) ? 10f : (float)v;
        }

        public static SolidColour FrameOf(string name)
        {
            switch (name)
            {
                case "black": return SolidColour.Black;
                case "white": return SolidColour.White;
                case "gray": return SolidColour.DarkGray;
                case "gold": return SolidColour.Gold;
                default: return SolidColour.Steel;
            }
        }

        /// <summary>
        /// The board: one part, the lit face with its margin and the bezel round it; the payload carries the text (its job, layout and colours: the text light box's, so the plan is shared with it) and
        /// the stand's settings. The housing is as deep as the field says: the board's face is that far in front of its back.
        /// </summary>
        private static GuideBoard Layout(GuideSpec s)
        {
            TextPlanJob job = TextBoxes.JobOf(s, out bool fontFell);
            FontOutlines font = TextFonts.Get(job.FontId);
            float depth = Number(s, "depth", 20, 8, 40) / 100f;
            var stand = new StandBoard
            {
                TiltDeg = Number(s, "tilt", 10, -MaxTilt, MaxTilt),
                Lift = Number(s, "lift", 30, 25, 200) / 100f,
                Depth = depth,
                Margin = Number(s, "margin", 8, 2, 60) / 100f,
                LitGround = s.Get("face") != "letters",
                Frame = FrameOf(s.Get("frame"))
            };
            var text = new TextBoard
            {
                Job = job,
                Glow = TextBoxes.GlowOf(s.Get("glow")),
                Return = SolidColour.Black,
                Depth = depth,
                Stand = 0f,
                FontFell = fontFell,
                NoFont = font == null
            };
            float width = TextBoxes.MinBox, height = TextBoxes.MinBox;
            if (font != null)
            {
                text.Layout = TextLayout.Layout(job.Text, job.Style, TextFace.Of(font));
                width = Math.Max(TextBoxes.MinBox, text.Layout.Width);
                height = Math.Max(TextBoxes.MinBox, text.Layout.Height);
            }

            float edge = stand.Margin + Bezel;
            var part = new GuidePart { Ground = GuideColour.Black, W = Math.Max(MinBox, width + 2f * edge), H = Math.Max(MinBox, height + 2f * edge), Radius = 0f };
            var board = new GuideBoard { H = job.LetterHeight, Text = text, Stand = stand };
            board.Parts.Add(part);
            return board;
        }

        /// <summary>The editor's notes: the text light box's (the font, the characters it lacks, the text cut), and a tilt that was held to its range.</summary>
        public static List<string> Hints(string spec, bool zh)
        {
            List<string> lines = TextBoxes.Hints(spec, zh);
            GuideSpec parsed = s_Template.Parse(spec ?? "");
            float typed = TypedTilt(parsed);
            if (Math.Abs(typed) > MaxTilt)
                lines.Add(zh ? $"倾角最大 ±{MaxTilt:0}°：按 {Math.Sign(typed) * MaxTilt:0}° 做。" : $"The tilt is at most ±{MaxTilt:0}°: made at {Math.Sign(typed) * MaxTilt:0}°.");
            return lines;
        }

        /// <summary>Where the log goes (set at the start of the game); null while there is none.</summary>
        public static Action<string> Log;

        private static int s_Notes;

        /// <summary>The first few times the ground under a stand's feet is far from the one it was drawn for (a tile is built from the ground profile, on a worker): one line each.</summary>
        public static void NoteFeet(StandPlan plan, float[] offsets)
        {
            if (Log == null || plan == null || offsets == null || System.Threading.Volatile.Read(ref s_Notes) >= 12)
                return;
            string note = FeetNote(plan, offsets);
            if (note.Length > 0 && System.Threading.Interlocked.Increment(ref s_Notes) <= 12)
                Log(note);
        }

        /// <summary>A line for the log about the feet of a stand that stands on very uneven ground: what StandPlanner.FootWarnings found, in words.</summary>
        public static string FeetNote(StandPlan plan, IList<float> offsets)
        {
            List<StandWarning> warnings = StandPlanner.FootWarnings(plan, offsets);
            if (warnings.Count == 0)
                return "";
            var parts = new List<string>();
            foreach (StandWarning w in warnings)
            {
                parts.Add(w.Kind == StandWarningKind.FootOff
                    ? $"foot {w.Foot} {w.Value:+0.00;-0.00} m from the ground it was drawn for"
                    : $"frame at foot {w.Foot}: its two feet differ by {Math.Abs(w.Value):0.00} m");
            }

            return "standing light box on uneven ground: " + string.Join("; ", parts) + " (the post and its feet follow the ground, the box stays where it is).";
        }
    }
}
