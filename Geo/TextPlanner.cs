using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    public enum TextWarningKind : byte
    {
        FontMissing,
        MissingCharacter,
        TextCut,
        HeldByRods,
        LongRod,
        SingleStud,
        FloatingIsland,

        /// <summary>The letters have too many points for one mesh (a tile's 16-bit indices): they were cut coarser (Count steps), or are drawn as bars when even that is not enough.</summary>
        TooDetailed,

        /// <summary>A text on an arc is bent less than asked (Count: the degrees it got), or the letters would meet on the inner side.</summary>
        ArcLimited
    }

    /// <summary>
    /// A hint for the editor of a text light box: what it is about and the numbers that go into it. No sentence: the editor looks the key up in the language files (CDLC.TS. + Key) and puts the numbers in, so
    /// the planner, which runs on a worker, knows no language (the owner, 2 Nov: a hint for a character the font lacks, a warning for a long rod, no cap on anything). Count: how many characters, islands or
    /// rods; Codepoint: the character the font lacks; Metres: the longest rod.
    /// </summary>
    public readonly struct TextWarning
    {
        public readonly TextWarningKind Kind;
        public readonly int Count, Codepoint;
        public readonly float Metres;

        public TextWarning(TextWarningKind kind, int count = 0, int codepoint = 0, float metres = 0f)
        {
            Kind = kind;
            Count = count;
            Codepoint = codepoint;
            Metres = metres;
        }

        private static readonly string[] s_Keys =
        {
            "UI.TextWarnFont", "UI.TextWarnMissing", "UI.TextWarnCut", "UI.TextWarnRods", "UI.TextWarnLongRod", "UI.TextWarnSingleStud", "UI.TextWarnFloating", "UI.TextWarnTooDetailed", "UI.TextWarnArcLimited"
        };

        public string Key => s_Keys[(int)Kind];
    }

    /// <summary>The editor's hints for a text: from the layout (characters the font lacks, a text that was cut) and the rear frame (islands held by rods, a long rod, islands with one stud or none). Pure.</summary>
    public static class TextWarnings
    {
        public static List<TextWarning> Of(TextLayoutResult layout, FramePlan frame)
        {
            var warnings = new List<TextWarning>();
            if (layout != null)
            {
                foreach (int codepoint in layout.Missing)
                    warnings.Add(new TextWarning(TextWarningKind.MissingCharacter, codepoint: codepoint));
                if (layout.Truncated)
                    warnings.Add(new TextWarning(TextWarningKind.TextCut, TextLayout.MaxChars));
                if (layout.Arc != null && layout.Arc.Limited)
                    warnings.Add(new TextWarning(TextWarningKind.ArcLimited, (int)Math.Round(layout.Arc.EffectiveSweep * 180.0 / Math.PI)));
            }

            if (frame == null)
                return warnings;
            if (frame.BridgedIslands > 0)
                warnings.Add(new TextWarning(TextWarningKind.HeldByRods, frame.BridgedIslands));
            if (frame.LongBridges > 0)
                warnings.Add(new TextWarning(TextWarningKind.LongRod, frame.LongBridges, metres: frame.LongestBridge));
            if (frame.SingleStudIslands > 0)
                warnings.Add(new TextWarning(TextWarningKind.SingleStud, frame.SingleStudIslands));
            if (frame.FloatingIslands > 0)
                warnings.Add(new TextWarning(TextWarningKind.FloatingIsland, frame.FloatingIslands));
            return warnings;
        }
    }

    /// <summary>
    /// Everything a text's plan depends on, immutable, with a key that is the same for the same text and numbers (DESIGN-text-lightbox.md 11): the input of the planner when it runs on a worker
    /// (LatestWorker&lt;TextPlanJob, TextPlanResult&gt;: the newest request is the one made). The font is named by an id the planner's resolver knows; the flatness of the curves is a share of the em (the
    /// level of detail the tile is built at). Two jobs are equal when every field is, and then their hashes are.
    /// </summary>
    public sealed class TextPlanJob : IEquatable<TextPlanJob>
    {
        public readonly string Text;
        public readonly int FontId;
        public readonly float LetterHeight, LetterSpacing, LineSpacing, RelativeFlatness;
        public readonly TextAlign Align;

        /// <summary>How the letters are dressed beyond the font (0.12.76); TextArt.Plain is what 0.12.75 made.</summary>
        public readonly TextArt Art;

        /// <summary>The 64-bit key of the job: what a result is kept under, and what a stale result is told from a current one by.</summary>
        public readonly ulong Key;

        public TextPlanJob(string text, int fontId, float letterHeight, float letterSpacing, float lineSpacing, TextAlign align, float relativeFlatness)
            : this(text, fontId, letterHeight, letterSpacing, lineSpacing, align, relativeFlatness, TextArt.Plain)
        {
        }

        public TextPlanJob(string text, int fontId, float letterHeight, float letterSpacing, float lineSpacing, TextAlign align, float relativeFlatness, TextArt art)
        {
            Art = art ?? TextArt.Plain;
            Text = text ?? "";
            FontId = fontId;
            LetterHeight = letterHeight;
            LetterSpacing = letterSpacing;
            LineSpacing = lineSpacing;
            Align = align;
            RelativeFlatness = relativeFlatness;
            var key = new Fingerprint();
            foreach (char c in Text)
                key.Add((int)c);
            key.Add(Text.Length);
            key.Add(fontId);
            key.Add(letterHeight);
            key.Add(letterSpacing);
            key.Add(lineSpacing);
            key.Add((int)align);
            key.Add(relativeFlatness);
            if (!Art.IsPlain)
                key.Add(Art.Key);
            Key = key.Value;
        }

        public TextStyle Style => new TextStyle { LetterHeight = LetterHeight, LetterSpacing = LetterSpacing, LineSpacing = LineSpacing, Align = Align };

        public bool Equals(TextPlanJob other) =>
            other != null && Key == other.Key && FontId == other.FontId && Align == other.Align && string.Equals(Text, other.Text, StringComparison.Ordinal) && Art.Equals(other.Art)
            && Same(LetterHeight, other.LetterHeight) && Same(LetterSpacing, other.LetterSpacing) && Same(LineSpacing, other.LineSpacing) && Same(RelativeFlatness, other.RelativeFlatness);

        public override bool Equals(object obj) => Equals(obj as TextPlanJob);

        public override int GetHashCode() => unchecked((int)Key ^ (int)(Key >> 32));

        private static bool Same(float a, float b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
    }

    /// <summary>
    /// What a text's plan made: the layout, the glyph of each placed character (null for a blank and for a character the font has no outline for), the rear frame, and the editor's hints. Made once and
    /// not changed, so the main thread and a worker may hold it together.
    /// </summary>
    public sealed class TextPlanResult
    {
        public TextPlanJob Job;
        public TextLayoutResult Layout;
        public GlyphMesh[] Meshes;
        public FramePlan Frame;
        public List<TextWarning> Warnings;

        /// <summary>How many times the flatness was doubled to bring the letters under TextPlans.MaxReturnVertices, the vertices of the return that are left, and whether even the coarsest cut was too much (the letters are then not drawn).</summary>
        public int Coarsened, Vertices;
        public bool TooHeavy;
    }

    /// <summary>
    /// The planner of a text light box as a function of a job: layout, the glyphs (from the shared cache, made when they are not in it), the rear frame, the hints.
    /// It holds no state of its own: the font and the cache are what it is given, and both are safe to share between threads, so any number of planners may run at once, on workers; none runs on the main
    /// thread (the main thread builds a job, asks for the result, and uses it when it is there). Plan is what LatestWorker is given.
    /// </summary>
    public sealed class TextPlanner
    {
        private readonly Func<int, FontOutlines> m_Fonts;
        private readonly GlyphCache m_Cache;

        /// <param name="fonts">The font of an id (null: none); it must be safe to call from any thread.</param>
        public TextPlanner(Func<int, FontOutlines> fonts, GlyphCache cache)
        {
            m_Fonts = fonts;
            m_Cache = cache;
        }

        public TextPlanResult Plan(TextPlanJob job)
        {
            TextFaces.Fonts(job, m_Fonts, out FontOutlines font, out FontOutlines fallback);
            if (font == null || !font.Ok)
            {
                return new TextPlanResult
                {
                    Job = job, Layout = new TextLayoutResult(), Meshes = new GlyphMesh[0], Warnings = new List<TextWarning> { new TextWarning(TextWarningKind.FontMissing) }
                };
            }

            TextLayoutResult layout = ArcTextLayout.Layout(job.Text, job.Style, TextFaces.Of(font, fallback), job.Art);
            int bucket = TextGlyphs.Bucket(job.RelativeFlatness);
            var meshes = new GlyphMesh[layout.Glyphs.Count];
            for (int i = 0; i < meshes.Length; i++)
            {
                PlacedGlyph placed = layout.Glyphs[i];
                meshes[i] = placed.Missing ? null : MeshOf(job, font, fallback, placed.Codepoint, bucket);
            }

            // A text on an arc has its frame planned on the straight line and carried over to the arc.
            FrameStyle frameStyle = TextStyles.FrameFor(job.LetterHeight, job.Art);
            FramePlan frame = LetterFrame.Plan(layout.Arc != null ? layout.Arc.Straight : layout, c => MeshOf(job, font, fallback, c, bucket), frameStyle);
            if (layout.Arc != null)
                frame = ArcTextLayout.BendFrame(frame, layout.Arc, frameStyle);
            return new TextPlanResult { Job = job, Layout = layout, Meshes = meshes, Frame = frame, Warnings = TextWarnings.Of(layout, frame) };
        }

        /// <summary>
        /// The glyph of a character as the job wants it: the chosen font's, or, for a character only the fallback has, the default font's moved into the chosen font's em box; then dressed as the job's art
        /// says (bolder, lighter, leaning). Every stage is kept in the cache under a key of its own.
        /// </summary>
        private GlyphMesh MeshOf(TextPlanJob job, FontOutlines font, FontOutlines fallback, int codepoint, int bucket)
        {
            // A sequence of code points, or one that neither the chosen font nor the default draws: the emoji and symbol fonts of the machine (0.12.78).
            if (TextClusters.IsClusterKey(codepoint) || (!HasGlyph(font, codepoint) && !HasGlyph(fallback, codepoint)))
            {
                GlyphMesh emoji = EmojiOf(codepoint, bucket);
                if (emoji != null || TextClusters.IsClusterKey(codepoint))
                    return emoji;
            }

            int fontId = job.FontId, variant = 0;
            GlyphMesh own;
            if (!TextFaces.FromFallback(font, fallback, codepoint))
            {
                own = m_Cache.GetOrBuild(font, job.FontId, codepoint, bucket);
            }
            else
            {
                fontId = TextFonts.DefaultId;
                float lift = TextFaces.Lift(font, fallback);
                if (Math.Abs(lift) < 1e-4f)
                {
                    own = m_Cache.GetOrBuild(fallback, TextFonts.DefaultId, codepoint, bucket);
                }
                else
                {
                    variant = unchecked(0x4C0000 + (int)Math.Round(lift * 10000f));
                    own = m_Cache.GetOrBuild(new GlyphKey(fontId, codepoint, bucket, variant), () => GlyphStyles.Translate(m_Cache.GetOrBuild(fallback, TextFonts.DefaultId, codepoint, bucket), 0f, lift));
                }
            }

            if (own == null || !job.Art.ChangesGlyphs)
                return own;
            // Leaning is about the middle of the chosen font's em box, so that a lean puts as much ink out on the left as on the right.
            float pivot = (font.EmTop + font.EmBottom) * 0.5f / font.UnitsPerEm;
            var styled = new GlyphKey(fontId, codepoint, bucket, Mixed(variant, job.Art.GlyphVariant, (int)Math.Round(pivot * 10000f)));
            return m_Cache.GetOrBuild(styled, () => GlyphStyles.Apply(own, job.Art, pivot, TextGlyphs.BucketFlatness(bucket), TextStyles.MinBand(job.LetterHeight, job.Art)));
        }

        private static bool HasGlyph(FontOutlines font, int codepoint) => font != null && font.TryAdvance(codepoint, out _);

        /// <summary>The glyph of a cluster from the machine's emoji fonts, kept in the cache under the cluster's key (a font id of its own: no font's own glyph is that cluster's); null when none draws it.</summary>
        private GlyphMesh EmojiOf(int key, int bucket)
        {
            IReadOnlyList<FontOutlines> emoji = TextFaces.EmojiFonts;
            if (emoji.Count == 0)
                return null;
            string cluster = TextClusters.TextOf(key);
            return m_Cache.GetOrBuild(new GlyphKey(EmojiFontId, key, bucket), () => EmojiMesh.FromChain(emoji, cluster, bucket));
        }

        /// <summary>The id the emoji glyphs are kept under in the glyph cache: no font has it.</summary>
        public const int EmojiFontId = 0x454D4F4A;

        /// <summary>The variant of a glyph that was moved (or not) and then dressed: never 0 (that is the font's own glyph) and never the variant it was made from.</summary>
        private static int Mixed(int moved, int dressing, int pivot)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + moved;
                h = h * 31 + dressing;
                h = h * 31 + pivot;
                return h == 0 || h == moved ? h + 1 : h;
            }
        }
    }
}
