namespace CDLC.TS.Geo
{
    /// <summary>
    /// Which system fonts a text light box may take symbols and emoji from when the fonts of the text lack a cluster (0.12.78, the owner: "支持Emoji"), in the order they are tried: Segoe UI Emoji (colour
    /// layers) before Segoe UI Symbol (plain outlines of the same pictures), then the Noto fonts and Symbola of other systems. A font is never shipped: the machine has these or it has not. The chain of a
    /// text is the text's own font, the default font when the text takes the fallback (TextFaces.Fonts), and these (TextFaces.UseEmojiFonts).
    /// </summary>
    public static class TextChain
    {
        public static readonly string[] EmojiFiles = { "seguiemj.ttf", "seguisym.ttf", "NotoColorEmoji.ttf", "NotoEmoji-Regular.ttf", "Symbola.ttf" };
    }
}
