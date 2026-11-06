using System;

namespace CDLC.TS.Geo
{
    /// <summary>The class of a road, which sets the colour of its stones' figures (GB 5768.2-2022 8.6.5, 8.6.6): national in red, provincial in blue, county and township in black.</summary>
    public enum PostRoadClass
    {
        National,
        Provincial,
        Local
    }

    /// <summary>What stands at one place along a run of kilometre stones and hundred-metre posts: a stone with its kilometre, or a post with the hundreds digit.</summary>
    public readonly struct PostMark
    {
        public readonly bool Stone;
        public readonly int Km, Hundred;

        public PostMark(bool stone, int km, int hundred)
        {
            Stone = stone;
            Km = km;
            Hundred = hundred;
        }
    }

    /// <summary>
    /// The sizes and the numbering of the roadside posts (0.12.78; the owner, 3 Nov: "地面高速常见的路边立桩"). Three things stand in a run along a road: the 柱式轮廓标 that carries a 百米牌 (GB 5768.2-2022 9.4.5.2,
    /// Fig. 287; the post of GB/T 24970-2020 5.1), and the 公路里程碑 and 百米桩 of a road that is not an expressway (8.6.5, 8.6.6, Fig. 243, 245). Sizes are marked where they come from: the figures'
    /// numbers (centimetres, as the standard prints them); GB/T 24970's, which were read from exam questions and web pages that quote the clause (the standard's own text was not seen); and estimates. Pure.
    /// </summary>
    public static class RoadPosts
    {
        // ---------------------------------------------------------------- 柱式轮廓标 (GB/T 24970-2020 5.1.1.3 as quoted; Fig. 287 for the rest)

        /// <summary>The post's cross-section is an isosceles triangle with rounded corners, 100 mm along the base and 120 mm high; the whole post is 1250 mm long (quoted from GB/T 24970-2020 5.1.1.3).</summary>
        public const float TriangleBase = 0.100f, TriangleHeight = 0.120f, PostLength = 1.250f;

        /// <summary>How much of the post is in the ground (m): an estimate (the standard's figure gives the length, not the depth); the rest, 1.0 m, stands.</summary>
        public const float Buried = 0.25f;

        /// <summary>The black mark round the top of the post is 250 mm long and holds a 180 x 40 mm reflector (quoted from GB/T 24970-2020).</summary>
        public const float BandLength = 0.250f, BarHeight = 0.180f, BarWidth = 0.040f;

        /// <summary>The middle of the black mark above the ground (m): GB 5768.3-2025 8.2.2 f) puts a delineator's reflector at 70 cm (as MarkingStuds reads it); an estimate for this post.</summary>
        public const float BandCentre = 0.70f;

        /// <summary>How steeply the top and the black mark slope across the post (rise per metre across): read off Fig. 287, where both rise 60 over 102 across the face; an estimate. The top slopes down to the carriageway (GB/T 24970).</summary>
        public const float Slope = 0.55f;

        /// <summary>A corner of the post's section is rounded: how far from the corner the rounding starts (m), an estimate.</summary>
        public const float Rounding = 0.014f;

        /// <summary>The 百米牌 on the post: 70 mm across, the share of the post's face Fig. 287 gives it (68 of 102); the standard's 10 and 15 cm are for plates that stand on their own (9.4.5.2).</summary>
        public const float PlateDiameter = 0.070f;

        /// <summary>The spacing of the posts on a motorway's straight (m), GB 5768.3-2025 8.2.2 c) as MarkingStuds has it, and of the hundred-metre plates (9.4.5.2).</summary>
        public const float PostSpacing = 50f, PlateSpacing = 100f;

        // ---------------------------------------------------------------- 里程碑 (Fig. 243), 百米桩 (Fig. 245); centimetres as printed, in metres here

        /// <summary>里程碑 (Fig. 243): 40 wide and 60 high for a kilometre of up to three digits, 50 and 70 for four; 15 deep; the top corners cut 5; 40 (50 for four digits) into the ground (0.12.78 built it 30 deep, the 百米桩's).</summary>
        public const float StoneDepth = 0.15f, StoneChamfer = 0.05f;

        /// <summary>The stone's size for a kilometre of this many digits (Fig. 243's note: the figures in brackets for up to three).</summary>
        public static void StoneSize(int digits, out float width, out float height, out float frameBottom, out float frameHeight, out float buried)
        {
            bool four = digits >= 4;
            width = four ? 0.50f : 0.40f;
            height = four ? 0.70f : 0.60f;
            frameBottom = four ? 0.30f : 0.25f;
            frameHeight = four ? 0.25f : 0.20f;
            buried = four ? 0.50f : 0.40f;
        }

        /// <summary>The line of the stone's road number is 5 high and 5 below the top's cut (Fig. 243, the three 5s of the left side); the gap to the frame is 5.</summary>
        public const float CodeHeight = 0.05f;

        /// <summary>百米桩 (Fig. 245): 12 square, 25 high, the top corners cut 2; the figure 8 high, 4 below the top; 30 into the ground.</summary>
        public const float HundredSide = 0.12f, HundredHeight = 0.25f, HundredChamfer = 0.02f, HundredDigit = 0.08f, HundredDigitTop = 0.04f;

        /// <summary>How deep the 百米桩 is in the ground (Fig. 245: 30).</summary>
        public const float HundredBuried = 0.30f;

        // ---------------------------------------------------------------- 公路界碑 (Fig. 246), 道口标柱 (Fig. 247); centimetres as printed, in metres here

        /// <summary>
        /// 公路界碑 (Fig. 246, 8.6.7): 15 wide, 50 above the ground and 70 in it, the top's two corners cut 2 by 2 (the flat top is 11 across); "方柱体", a square prism: the figure draws one face, so the depth is the
        /// width, 15 (an assumption).
        /// </summary>
        public const float BoundaryWidth = 0.15f, BoundaryDepth = 0.15f, BoundaryHeight = 0.50f, BoundaryChamfer = 0.02f, BoundaryBuried = 0.70f;

        /// <summary>The characters 公路界 on both faces (Fig. 246): each 8 high and wide (the font's em box), 2.5 between them, the first 5 under the top (the cut's 2 and 3 more), the last 16 over the ground.</summary>
        public const float BoundaryCharacter = 0.08f, BoundaryGap = 0.025f, BoundaryTopMargin = 0.05f;

        public const string BoundaryText = "公路界";

        /// <summary>
        /// The spacing of the boundary stones (8.6.7: "一般每隔200 m～500 m设置一块，曲线段可适当加密"): the two ends of the standard's range, and the spacing this mod takes for "denser on curves", which the standard
        /// gives no number for (an estimate: half the least).
        /// </summary>
        public const float BoundaryGap200 = 200f, BoundaryGap500 = 500f, BoundaryGapDense = 100f;

        /// <summary>道口标柱 (Fig. 247 b, 8.6.8): phi 12 and 80 above the ground, in bands of 20 from the top: red, white, red, white; elastic material with reflective film. The depth in the ground is not a number of the figure (it draws about 25: an estimate).</summary>
        public const float JunctionRadius = 0.06f, JunctionHeight = 0.80f, JunctionBand = 0.20f, JunctionBuried = 0.25f;

        /// <summary>Where the two posts of one side of a branch's mouth stand (Fig. 247 a): 200 and 400 from where the corner's curve begins, along the main line's edge (m).</summary>
        public static readonly float[] JunctionDistances = { 2f, 4f };

        /// <summary>The figures' colour by the class of the road (8.6.5, 8.6.6).</summary>
        public static SolidColour ColourOf(PostRoadClass road) => road == PostRoadClass.National ? SolidColour.Red : road == PostRoadClass.Provincial ? SolidColour.Blue : SolidColour.Black;

        // ---------------------------------------------------------------- the numbering

        /// <summary>
        /// What stands at the index-th place of a run that starts at kilometre km and hundred hm (0 to 9), a hundred metres a place: a stone where the hundred is 0, else a post with the hundred's digit.
        /// A run cut in pieces numbers by the place's index in the whole run (the caller works it out from the phase), so a piece goes on where the last ended.
        /// </summary>
        public static PostMark Hundreds(int km, int hm, int index)
        {
            long h = (long)Math.Max(0, km) * 10 + Math.Max(0, Math.Min(9, hm)) + Math.Max(0, index);
            int digit = (int)(h % 10);
            return new PostMark(digit == 0, (int)(h / 10), digit);
        }

        /// <summary>What stands at the index-th place of a run of stones only (a kilometre each): the kilometre.</summary>
        public static PostMark Kilometres(int km, int index) => new PostMark(true, (int)Math.Min(int.MaxValue, (long)Math.Max(0, km) + Math.Max(0, index)), 0);

        /// <summary>The index of the place at this distance along the whole run (m) when places are spacing apart: the grid the places stand on goes on across the pieces of a run.</summary>
        public static int IndexAt(float distance, float spacing) => spacing > 0f ? (int)Math.Round(distance / spacing) : 0;

        /// <summary>Whether the place with this index carries a plate when the posts are spacing apart: a plate every PlateSpacing metres (every post when they are 100 m apart or more).</summary>
        public static bool HasPlate(int index, float spacing)
        {
            if (spacing >= PlateSpacing - 1e-3f)
                return true;
            int per = (int)Math.Max(1, Math.Round(PlateSpacing / Math.Max(1f, spacing)));
            return index % per == 0;
        }

        /// <summary>The number of plates before this index: the plates count 100 m each, so the post at index i carries the hundred of i / per.</summary>
        public static int PlateIndex(int index, float spacing)
        {
            if (spacing >= PlateSpacing - 1e-3f)
                return index;
            int per = (int)Math.Max(1, Math.Round(PlateSpacing / Math.Max(1f, spacing)));
            return index / per;
        }

        public static string Digits(int value) => Math.Max(0, value).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
