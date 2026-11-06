namespace CDLC.TS.Geo
{
    /// <summary>The city whose street-name plate post (路名牌立柱, 0.12.57) a support is; None for every other support.</summary>
    public enum PlateStyle : byte
    {
        None = 0,
        Shanghai = 1,
        Xuzhou = 2,
        Hangzhou = 3,
        Shenzhen = 4
    }

    /// <summary>
    /// A street-name plate post's head and post (0.12.57; scratch/research-supports-urban/REPORT.md card D): the plate stands on the head at the post's top, its plane through the post's axis, printed
    /// on both faces. The head is a stack from the top: an oval tray (TopA along the plate, TopB across, TopThick thick), a neck of the cone's top radius, a second oval, and a cone down to the post;
    /// Shenzhen's is a flare instead (Flare). Sizes in metres, radii and half axes.
    /// </summary>
    public sealed class PlateHead
    {
        public float Height, TopA, TopB, TopThick, Neck, SecondA, SecondB, SecondThick, ConeTop, ConeBottom;

        /// <summary>Shenzhen's head: a round flare from FlareBottom at its foot to FlareTop at its top, Height tall (no trays).</summary>
        public bool Flare;
        public float FlareBottom, FlareTop;

        /// <summary>The post's radius, its sides (24: Shenzhen's fluted post, drawn as 24 flats; else round), the plate's thickness and its lower edge's height.</summary>
        public float PostRadius, PlateDepth, Clearance;
        public int Flutes;

        /// <summary>The gap between the lower edge of a plate on its head and the top of a board under the head on the same post (Hangzhou fig. B.2: 290 under the plate, 50 under the head).</summary>
        public float GapUnder => Height + 0.05f;

        /// <summary>
        /// Whether a board goes on a plate post's head: a street or a lane name sign of GB 17733 (the plates of Xuzhou's, Shanghai's, Hangzhou's and Shenzhen's posts); a post with no such board
        /// is a plain post (Assembly.Plan), and the pedestrian strips and the other boards hang under the plate (Hangzhou figs. B.2, B.3).
        /// </summary>
        public static bool Fits(SignInstance instance) => instance.Board != null && FitsDef(instance.Def);

        /// <summary>Whether a sign is one of those (the planner knows the sign's definition, not an instance of it).</summary>
        public static bool FitsDef(SignDef def) => def.Id == "gb17733.street" || def.Id == "gb17733.lane";

        /// <summary>A plate of the A type (Xuzhou DB3203/T 1080-2025 5.1.2: 1500 × 450, for main and secondary roads) by its size; else B (1200 × 360, branch roads).</summary>
        public static bool Large(float width, float height) => height >= 0.40f || width >= 1.4f;

        /// <summary>
        /// The head and post of a style for a plate of the A type or not:
        /// Xuzhou DB3203/T 1080-2025 figs. 2-7 (A: oval 400 × 200 × 20, a 20 neck, oval 250 × 160 × 20, a cone 190 high, 250 in all, on φ89; B: 330 × 170, 230 × 140, cone 140, 200 in all, on φ76;
        /// the cone's top Ø120 or Ø100 scaled from figs. 3 and 6, an estimate), the plate 65/60 or 45/40 thick (fig. 1: their mean), its lower edge at 2.5 m;
        /// Shanghai the same head (measured on the Nanjing Rd W photo: 400 tray, 290 second disc, about 250 in all; seen from below an oval) and posts (DB31/T 416-2008 as quoted: A 1500 × 450 on Ø89 at
        /// 2500, B 1200 × 360 on Ø76 at 2140);
        /// Hangzhou 2015 guide fig. B.1 (330 tray 240 tall on Ø76, the plate's lower edge at 2000; scaled at 0.39 px/mm: tray 330 × 190 × 15, a 17 neck, 270 × 155 × 18, cone Ø206 to Ø85 over 190;
        /// the plate about 75 thick in its side view, an estimate);
        /// Shenzhen (a maker's listing: plate 1200 × 360 of 8 mm aluminium at 2000, a 24-flute post OD 76; the flared head scaled from the Guangpei Rd photo, 1.1 and 1.9 times the post's width at its
        /// foot and top, 2.4 times tall: estimates, ±15 %).
        /// </summary>
        public static PlateHead Of(PlateStyle style, bool large)
        {
            switch (style)
            {
                case PlateStyle.Hangzhou:
                    return new PlateHead
                    {
                        Height = 0.24f, TopA = 0.165f, TopB = 0.095f, TopThick = 0.015f, Neck = 0.017f, SecondA = 0.135f, SecondB = 0.0775f, SecondThick = 0.018f, ConeTop = 0.103f, ConeBottom = 0.0425f,
                        PostRadius = 0.038f, PlateDepth = 0.075f, Clearance = 2.0f
                    };
                case PlateStyle.Shenzhen:
                    return new PlateHead { Height = 0.183f, Flare = true, FlareBottom = 0.042f, FlareTop = 0.072f, PostRadius = 0.038f, Flutes = 24, PlateDepth = 0.008f, Clearance = 2.0f };
                default:
                    var head = large
                        ? new PlateHead { Height = 0.25f, TopA = 0.20f, TopB = 0.10f, SecondA = 0.125f, SecondB = 0.08f, ConeTop = 0.06f, PostRadius = 0.0445f, PlateDepth = 0.0625f }
                        : new PlateHead { Height = 0.20f, TopA = 0.165f, TopB = 0.085f, SecondA = 0.115f, SecondB = 0.07f, ConeTop = 0.05f, PostRadius = 0.038f, PlateDepth = 0.0425f };
                    head.TopThick = head.Neck = head.SecondThick = 0.02f;
                    head.ConeBottom = head.PostRadius + 0.004f;
                    head.Clearance = style == PlateStyle.Shanghai && !large ? 2.14f : 2.5f;
                    return head;
            }
        }
    }
}
