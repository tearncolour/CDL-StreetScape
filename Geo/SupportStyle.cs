namespace CDLC.TS.Geo
{
    /// <summary>The paint of a support's steel (0.12.57; scratch/research-supports-urban/REPORT.md card B).</summary>
    public enum SupportFinish : byte
    {
        /// <summary>Hot-dip galvanised, unpainted: highways, rural roads, most of Beijing.</summary>
        Galvanised = 0,

        /// <summary>RAL 9011 graphite black, matte (Shanghai DG/TJ 08-2362 A.5.8-1; Hangzhou's poles are matte black too).</summary>
        Graphite = 1,

        /// <summary>A dark grey powder coat (Guangzhou: a photo estimate).</summary>
        DarkGrey = 2,

        /// <summary>A light grey powder coat (Jiangsu: a photo estimate).</summary>
        LightGrey = 3
    }

    /// <summary>The paint on the lower part of a support's round posts over its finish (0.12.57; the owner: "涂装应该支持根部不同颜色油漆"; SignBuilder.PaintRoot).</summary>
    public enum RootPaint : byte
    {
        None = 0,

        /// <summary>立面标记 (GB 51038-2015 14.6.2): yellow and black stripes at 45°, 15 cm wide and 15 cm apart, from the base to 2.5 m, the side that slopes down toward the carriageway.</summary>
        Marking = 1,

        /// <summary>The blue root of the Qidong 2018 drawing 交施-07 杆件喷塑大样: blue to 1000 mm, then white and blue rings of 100 mm, three of each.</summary>
        BlueRings = 2
    }

    /// <summary>
    /// Which parts of the paint the player has set since the style profile was chosen (0.12.74; the owner: "带默认涂装，但是玩家可以改"): a profile's paint is a default, and a part the player changed keeps
    /// the player's value whatever the profile or a variant would give. Choosing a profile (or another one) clears them, as choosing a support with a finish of its own sets the finish today.
    /// </summary>
    [System.Flags]
    public enum PaintTouch : byte
    {
        None = 0,
        Finish = 1,
        BlackBack = 2,
        Root = 4
    }

    /// <summary>
    /// How a support is painted (0.12.57; the owner's "杆件涂装选项（黑色哑光、深灰、浅灰），以及黑色牌背"): the finish of its steel (posts, arms, channels, clamps, board backs), whether the boards' backs
    /// are painted black whatever the steel is (Beijing DB11/T 493.1 4.3.9: grey, black or metal; Hangzhou 5.7: black), and the paint on the posts' root. Kept in one byte (TsItem.Style): the finish
    /// in the two low bits, the black back in the third, the root paint in the fourth and fifth.
    /// </summary>
    public struct SupportStyle
    {
        public SupportFinish Finish;
        public bool BlackBack;
        public RootPaint Root;

        public static SupportStyle Of(byte packed) => new SupportStyle { Finish = (SupportFinish)(packed & 3), BlackBack = (packed & 4) != 0, Root = (RootPaint)((packed >> 3) & 3) };

        public byte Packed => (byte)(((byte)Finish & 3) | (BlackBack ? 4 : 0) | (((byte)Root & 3) << 3));

        /// <summary>The colour the support's steel is drawn in.</summary>
        public SolidColour MetalColour => Finish == SupportFinish.Graphite ? SolidColour.Graphite : Finish == SupportFinish.DarkGrey ? SolidColour.PoleDarkGrey
            : Finish == SupportFinish.LightGrey ? SolidColour.PoleLightGrey : SolidColour.Metal;

        /// <summary>The backs of the boards are drawn apart, black, when they are black on a steel that is not.</summary>
        public bool BlackBacks => BlackBack && Finish != SupportFinish.Graphite;
    }
}
