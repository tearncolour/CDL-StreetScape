using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    public enum GuideColour
    {
        Green,
        Blue,
        White,
        Red,
        Yellow,
        Black,
        Orange,
        Brown,
        Gray,

        /// <summary>The painted steel of an electric box (0.12.23): light grey, as RAL 7035, and stainless steel.</summary>
        LightGray,
        Steel,

        FluorescentFireRed,

        /// <summary>The colours of the lamp-post ads' prints (0.12.49, Signs.LampAds): gold, a dark red and a navy blue.</summary>
        Gold,
        DarkRed,
        Navy,

        /// <summary>Pink neon in the catalogue's picture of a neon figure (0.12.49, Signs.Neon.Print).</summary>
        Pink,

        /// <summary>The identification colours of Shanghai's lines 1 to 23 (0.12.57, Signs.WayfindingSigns.LineRgb; the draft's fig. A.1).</summary>
        Line1, Line2, Line3, Line4, Line5, Line6, Line7, Line8, Line9, Line10, Line11, Line12, Line13, Line14, Line15, Line16, Line17, Line18, Line19, Line20, Line21, Line22, Line23,

        /// <summary>The fluorescent yellow-green of GB 5768.2 Table 1 and 2 (0.12.80): the ground of 警10, 警11, 警12 and 辅14, the frame of 示16 and of the school bus 示40.</summary>
        FluorescentYellowGreen
    }

    /// <summary>
    /// The colours of a guide board as RGB. PROVISIONAL: picked to look like the printed figures of GB 5768.2-2022; the standard's colour limits (GB 5768.1) have not been
    /// used, so none of these is claimed to be inside them.
    /// </summary>
    public static class GuideColours
    {
        public static void Rgb(GuideColour colour, out byte r, out byte g, out byte b)
        {
            switch (colour)
            {
                case GuideColour.Green: r = 0; g = 122; b = 64; break;
                case GuideColour.Blue: r = 0; g = 72; b = 160; break;
                case GuideColour.White: r = 250; g = 250; b = 246; break;
                case GuideColour.Red: r = 214; g = 28; b = 36; break;
                // GB 5768.2 amendment 1: a separate colour class. This sRGB display approximation cannot reproduce physical fluorescence.
                case GuideColour.FluorescentFireRed: r = 255; g = 80; b = 55; break;
                // The printed colour of GB 5768.2-2022's figures (#9DC337 in Fig. 97 b and 128; the standard gives no chromaticity) in the light of the panel's yellow: the figures print the yellow-green
                // at 0.862 of the yellow's luminance, so the printed colour is scaled in linear light by 1.2865 to 0.862 of this panel's yellow (hue and relative saturation kept; no channel clips).
                case GuideColour.FluorescentYellowGreen: r = 176; g = 218; b = 63; break;
                case GuideColour.Yellow: r = 255; g = 214; b = 0; break;
                case GuideColour.Orange: r = 255; g = 120; b = 20; break;
                case GuideColour.Brown: r = 112; g = 66; b = 36; break;
                case GuideColour.Gray: r = 120; g = 124; b = 128; break;
                case GuideColour.LightGray: r = 204; g = 207; b = 202; break;
                case GuideColour.Steel: r = 186; g = 190; b = 194; break;
                case GuideColour.Gold: r = 232; g = 182; b = 46; break;
                case GuideColour.DarkRed: r = 150; g = 14; b = 22; break;
                case GuideColour.Navy: r = 18; g = 40; b = 96; break;
                case GuideColour.Pink: r = 245; g = 110; b = 170; break;
                default:
                    if (colour >= GuideColour.Line1 && colour <= GuideColour.Line23)
                    {
                        byte[] line = Signs.WayfindingSigns.LineRgb((int)colour - (int)GuideColour.Line1 + 1);
                        r = line[0];
                        g = line[1];
                        b = line[2];
                        break;
                    }

                    r = 18; g = 18; b = 18;
                    break;
            }
        }
    }

    public enum GuideElementKind
    {
        Rect,
        Oval,
        Text,
        Arrow,
        Polygon,

        /// <summary>Contours filled together by the even-odd rule (a pictogram read from a path: GuidePath).</summary>
        Path,

        /// <summary>A picture the player imported (0.12.29, Signs.CustomSigns): its key is the text, drawn stretched to the box and clipped to the part's outline.</summary>
        Image
    }

    /// <summary>The outline of a part. The box (W x H) is the part's bounding box; round boards use W as the diameter, an octagon's W is the distance between parallel sides, a triangle's W its side.</summary>
    public enum GuidePartShape
    {
        RoundRect,
        Circle,
        Octagon,
        Triangle,
        TriangleDown,

        /// <summary>A board pointed at its right end (路7 a, Fig. 212 a, E.173 a); GuidePart.Side is the length of the point. It has a liner and a border as a rounded rectangle has.</summary>
        PointRight,

        /// <summary>The same pointed at its left end.</summary>
        PointLeft
    }

    /// <summary>
    /// Cell: Chinese characters, each in a square cell as high as the box, a gap between cells (the standard's character height h is the side of the cell).
    /// Ink: letters and digits, scaled so that what is drawn (not the font's line) is as high as the box, and squeezed if it is wider than the box.
    /// </summary>
    public enum GuideTextMode
    {
        Cell,
        Ink,

        /// <summary>Latin text of capitals and lower case (0.12.78, Bilingual): the capital's height is the box's, the baseline on the box's bottom whatever the letters, squeezed when wider than the box.</summary>
        Line
    }

    public enum GuideAlign
    {
        Left,
        Centre,
        Right
    }

    /// <summary>The six arrows of GB 5768.2 8.1.8 (Fig. 204): a right, b right exit / diagonal right, c ahead, d left exit / diagonal left, e left, f down (overhead boards only).</summary>
    public enum GuideArrow
    {
        Right,
        UpRight,
        Up,
        UpLeft,
        Left,
        Down
    }

    /// <summary>One drawn thing on a part of a board. The box is in metres in the part's own frame: from its top left corner, x to the right, y down.</summary>
    public sealed class GuideElement
    {
        public GuideElementKind Kind;
        public float X, Y, W, H;
        public GuideColour Colour;

        /// <summary>Rect: the corner radii at the top and at the bottom (0 is square). A stroke is drawn inside the box.</summary>
        public float RadiusTop, RadiusBottom;

        public float StrokeWidth;
        public GuideColour Stroke;

        /// <summary>True for what a sign's figure is made of (added through a Canvas): the figure must lie inside the part's Safe region, which the tests check for every sign.</summary>
        public bool Art;

        /// <summary>Rect: how far it is drawn beyond the part's inside on the left, the top and the right (over the border, outside the clip): for a band that meets the border in the same colour.</summary>
        public float Bleed;

        /// <summary>
        /// A band that ends at the bottom of the inside: the bleed grows it over the border below as well (0.12.78). Without it the band's lower edge and the inside's ground meet on the same anti-aliased
        /// edge and leave a hairline of the ground's colour across the band (路19's band has it, fainter).
        /// </summary>
        public bool BleedBottom;

        public string Text;

        /// <summary>Ink text: the string whose ink height sets the scale instead of the text's own (null: the text's own).</summary>
        public string InkRef;

        public GuideTextMode Mode;
        public GuideAlign Align;

        /// <summary>Cell text: the space between two cells.</summary>
        public float Gap;

        public GuideArrow Arrow;

        /// <summary>Polygon: x, y pairs in the part's frame.</summary>
        public float[] Points;

        /// <summary>Path: the contours (x, y pairs each) in the part's frame.</summary>
        public List<float[]> Contours;

        /// <summary>Image: the picture is cut to the proportions of the box and fills it (a cropped fill); else it is stretched to the box.</summary>
        public bool Crop;

        /// <summary>
        /// Image: drawn with its alpha left out (0.12.30): a picture with clear pixels has them carry the colours of their neighbours, and its board is cut where they are (GuidePart.Cut), so the face's
        /// edges are not darkened by them.
        /// </summary>
        public bool Opaque;

        public float Right => X + W;
        public float Bottom => Y + H;

        public GuideElement Moved(float dx, float dy)
        {
            var copy = (GuideElement)MemberwiseClone();
            copy.X += dx;
            copy.Y += dy;
            if (Points != null)
            {
                copy.Points = (float[])Points.Clone();
                for (int i = 0; i + 1 < copy.Points.Length; i += 2)
                {
                    copy.Points[i] += dx;
                    copy.Points[i + 1] += dy;
                }
            }

            if (Contours != null)
                copy.Contours = GuidePath.Map(Contours, 1f, 1f, dx, dy);

            return copy;
        }
    }

    /// <summary>A group of elements with a size, built on its own and then put on a part: how the pieces of a board (a road plate, a place name, a distance) are made once and reused.</summary>
    public sealed class GuideBlock
    {
        public float W, H;
        public readonly List<GuideElement> Elements = new List<GuideElement>();

        public GuideBlock(float w, float h)
        {
            W = w;
            H = h;
        }

        public GuideBlock Add(GuideElement element)
        {
            Elements.Add(element);
            return this;
        }

        /// <summary>Puts a block into another at an offset, inside the box of the other.</summary>
        public GuideBlock Add(GuideBlock block, float x, float y)
        {
            foreach (GuideElement e in block.Elements)
                Elements.Add(e.Moved(x, y));
            return this;
        }
    }

    /// <summary>
    /// One board of a sign: a rounded rectangle of a ground colour with a liner and a white border (GB 5768.2 Fig. 3: each at most 0.1 h wide, outer radius 0.4 h, the border's
    /// 0.3 h, the inside 0.2 h, h being the height of the Chinese characters), and the elements on it. The first part of a board is the board that hangs on the support;
    /// others (the exit number plate on top of an exit advance board) are plates fixed to it.
    /// </summary>
    public sealed class GuidePart
    {
        /// <summary>The outer box, from the main board's top left corner.</summary>
        public float X, Y, W, H;

        /// <summary>The region the figure of the sign must stay inside, as x, y pairs in the part's frame (the inside of the ring of a prohibition sign, of the edge of a warning sign): null when the part has none.</summary>
        public float[] Safe;

        /// <summary>
        /// Triangles: the nominal side of the triangle (the standard's A) before its corners are rounded; W and H are then the real width and height of the rounded outline. 0: W. Pointed boards: the length
        /// of the point, from where its sides leave the top and bottom edges to its tip.
        /// </summary>
        public float Side;

        /// <summary>The outline. Only RoundRect has a liner and a border (Fig. 3, guide boards); the other shapes are drawn with rings and bands as elements.</summary>
        public GuidePartShape Shape = GuidePartShape.RoundRect;

        /// <summary>
        /// The board as it is cut, when it is not its whole outline (0.12.30: the opaque part of a player's picture, CutOutline): loops round the middle of the part's box, y up, as the viewer sees the
        /// face, the material on the left of each. Null: the outline. The supports and the picking go by the outline still.
        /// </summary>
        public List<List<Point2>> Cut;

        public GuideColour Ground;
        public GuideColour BorderColour = GuideColour.White;

        /// <summary>The widths of the liner (the outer ring in the ground colour) and of the border.</summary>
        public float Liner, Border;

        /// <summary>The outer radius (of the liner); the border's is one liner width less, the inside's one border width less again.</summary>
        public float Radius;

        public readonly List<GuideElement> Elements = new List<GuideElement>();

        /// <summary>The width of liner and border together: the space the elements keep out of.</summary>
        public float Edge => Liner + Border;

        public float InnerRadius => Math.Max(0f, Radius - Liner - Border);

        public GuidePart Add(GuideBlock block, float x, float y)
        {
            foreach (GuideElement e in block.Elements)
                Elements.Add(e.Moved(x, y));
            return this;
        }
    }

    /// <summary>
    /// The body behind a board that is the door of a box (an electric box): how deep it is from the back of the board (where the channels and clamps are, as for any board) to the door, its colour,
    /// a rain hood over it, louvers in its sides, the handle that stands out of the door (in the door's frame, from its top left corner, x to the viewer's right), and the lower edge it has when it is
    /// the only thing on its support (0: the support's).
    /// </summary>
    public sealed class BoxBody
    {
        public float Depth;
        public SolidColour Colour;
        public bool Hood, SideLouvers;
        public float HandleX, HandleY, HandleW, HandleH;
        public float Clearance;
    }

    /// <summary>What a lamp-post ad is (0.12.49, Signs.LampAds): how its parts hang and what the builder makes round them (SignBuilder.PoleAds).</summary>
    public enum PoleAdKind
    {
        /// <summary>A banner (道旗) between a top and a bottom arm.</summary>
        Banner,

        /// <summary>A light box (灯箱): a box with a print on both faces, held by two short arms.</summary>
        Box,

        /// <summary>A rigid panel (宣传牌), held as a box is.</summary>
        Panel,

        /// <summary>A flat figure cut to its outline (a Chinese knot, an LED figure), hung from an arm by a cord.</summary>
        Knot,

        /// <summary>Lanterns (灯笼): built bodies hung from an arm; the part has no face.</summary>
        Lantern,

        /// <summary>A neon figure (霓虹造型灯, Signs.Neon): strips of neon flex along its strokes (PoleAd.Neon), hung from an arm at its top and held by one at its body's middle; the part has no face.</summary>
        Neon
    }

    /// <summary>A stroke of a neon figure (0.12.49, Signs.Neon): a strip of neon flex through the points (as the viewer sees the part, round its middle), closed or not, in a glowing colour.</summary>
    public sealed class NeonStroke
    {
        public readonly List<Point2> Points = new List<Point2>();
        public bool Closed;
        public SolidColour Colour;
    }

    /// <summary>A part's neon (PoleAd.Neon): its strokes, the steel rods that hold them together (Signs.Neon.Brace), and the middle of its body (y round the part's middle), where the lower arm holds it.</summary>
    public sealed class NeonArt
    {
        public readonly List<NeonStroke> Strokes = new List<NeonStroke>();
        public readonly List<(Point2 A, Point2 B)> Rods = new List<(Point2 A, Point2 B)>();
        public float BodyY;
    }

    /// <summary>
    /// How a lamp-post ad hangs (0.12.49): one part beside the post (on the sign's +X, the viewer's left: the road's side of a post at the right of the traffic) or a pair, one each side, each Gap
    /// from the post's surface; the parts' plane through the post's axis (z = 0), Depth thick; printed on both faces (TwoSided); lit at night (Lit: the faces glow, lanterns glow); the hardware's colour;
    /// the lower edge's height (Clearance, m).
    /// </summary>
    public sealed class PoleAd
    {
        public PoleAdKind Kind;
        public float Gap, Depth, Clearance;
        public bool Pair, TwoSided, Lit;
        public SolidColour Hardware;

        /// <summary>Lanterns: how many on a string, the kind (0 round, 1 hexagonal palace lantern, 2 long) and gold instead of red.</summary>
        public int Lanterns = 1, LanternKind;
        public bool LanternGold;

        /// <summary>A neon figure: the strokes of each part, and how wide its strips are (m).</summary>
        public readonly List<NeonArt> Neon = new List<NeonArt>();
        public float NeonWidth;

        /// <summary>
        /// A light box's or a panel's design (0.12.49, Signs.LampBoxes): what is round each part and what holds it, in the frame of the first part (round its middle, y up, as the viewer sees it: its
        /// side towards the post is +x; the builder mirrors all of it for the second part of a pair). Plates are flat ornaments and frames, Rods tubes in the parts' plane, Balls finials, Arms the
        /// straight arms from the post (a hoop round the post at each), Hoops the hoops of what reaches the post by itself (a frame's bars, a crook arm).
        /// </summary>
        public readonly List<PoleAdPlate> Plates = new List<PoleAdPlate>();
        public readonly List<PoleAdRod> Rods = new List<PoleAdRod>();
        public readonly List<(Point2 Centre, float Radius, SolidColour Colour)> Balls = new List<(Point2, float, SolidColour)>();
        public readonly List<PoleAdArm> Arms = new List<PoleAdArm>();
        public readonly List<float> Hoops = new List<float>();

        /// <summary>The colour of a box's or a panel's edge (its frame): the hardware's unless the design paints it.</summary>
        public SolidColour Edge;

        /// <summary>
        /// 抱柱 (0.12.49): one part round the post, its middle on the post's axis, the post through it (collars where it goes in at WrapTop and out at WrapBottom, round the part's middle); else the
        /// parts are beside the post.
        /// </summary>
        public bool Wrap;
        public float WrapTop, WrapBottom;

        /// <summary>How far the design reaches above the parts' top and below their bottom (m): the lower edge (Clearance) is its lowest point, and the post of its own reaches past its top.</summary>
        public float Above, Below;
    }

    /// <summary>
    /// A flat ornament or frame of a lamp-post ad (0.12.49, PoleAd.Plates): loops round the part's middle as the viewer sees it (outer ones anticlockwise, holes clockwise, y up), a solid between
    /// Z - Half and Z + Half (Z = 0: the parts' plane), in its colour; LitColour at night when the ad is lit (an LED ornament glows, painted steel does not: the same colour).
    /// </summary>
    public sealed class PoleAdPlate
    {
        public List<List<Point2>> Loops;
        public float Z, Half;
        public SolidColour Colour, LitColour;
    }

    /// <summary>A tube of a lamp-post ad (0.12.49, PoleAd.Rods) through the points (round the part's middle, as the viewer sees it) in the parts' plane, bent where they turn, closed at its ends when asked.</summary>
    public sealed class PoleAdRod
    {
        public List<Point2> Path;
        public float Radius;
        public SolidColour Colour;
        public bool CapStart, CapEnd;
    }

    /// <summary>
    /// A straight arm of a lamp-post ad (0.12.49, PoleAd.Arms) from the post to Reach at the height Y (round the part's middle, as the viewer sees it), with a hoop round the post; it ends in a bracket
    /// plate along the edge it holds (its x changing by Slope for each metre up: an oval's side), Along each way along the edge and Half each way across the parts' plane.
    /// </summary>
    public sealed class PoleAdArm
    {
        public float Y, Reach, Slope, Along, Half;
    }

    /// <summary>A laid-out board: the character height it was drawn with and its parts. Pure data; the painter draws it and the mesh builder cuts the outlines of its parts.</summary>
    public sealed class GuideBoard
    {
        /// <summary>The height of the Chinese characters (m) the board was laid out for.</summary>
        public float H;

        /// <summary>
        /// An LED screen (GB 5768.2 4.11, a variable-message sign): the pitch of its dots (m); 0 for a board that is not one. Its picture is painted like any board's (black, the figure in
        /// its colours) and read dot by dot (Led.Quantize); it has the one part, the screen.
        /// </summary>
        public float LedPitch;

        /// <summary>The most dots this screen may have; 0 is Led.MaxDots. The traffic-state screen asks for more (it is a map that has to be read at a distance, and few of its dots are lit).</summary>
        public int LedMaxDots;

        /// <summary>How many pixels of the painted picture a dot of this screen is read from, each way; 0 is Led.SamplesPerDot (six).</summary>
        public int LedSamples;

        /// <summary>The board is the door of a box (0.12.23, an electric box: Signs.Cabinets); null for a sign. The picture is the door, the body behind it is painted solids.</summary>
        public BoxBody Box;

        /// <summary>The board is a lamp-post ad or decoration (0.12.49): how it hangs; null for any other.</summary>
        public PoleAd PoleAd;

        /// <summary>The board is a text light box (0.12.75, Signs.TextBoxes): channel letters made from the text, not a picture; null for any other.</summary>
        public Signs.TextBoard Text;

        /// <summary>The board is a standing light box (0.12.76, Signs.StandBoxes): its text is Text, how it leans and stands is this; null for any other.</summary>
        public Signs.StandBoard Stand;

        /// <summary>What the English lines under the Chinese texts came to (Bilingual); null when the board has none asked for.</summary>
        public BilingualReport English;

        public readonly List<GuidePart> Parts = new List<GuidePart>();

        public GuidePart Main => Parts[0];

        /// <summary>The box round all parts, from the main board's top left corner (y down); x and y of the box's top left and its size.</summary>
        public void Bounds(out float x0, out float y0, out float x1, out float y1)
        {
            x0 = float.MaxValue;
            y0 = float.MaxValue;
            x1 = float.MinValue;
            y1 = float.MinValue;
            foreach (GuidePart p in Parts)
            {
                x0 = Math.Min(x0, p.X);
                y0 = Math.Min(y0, p.Y);
                x1 = Math.Max(x1, p.X + p.W);
                y1 = Math.Max(y1, p.Y + p.H);
            }
        }

        /// <summary>How far the parts other than the main board reach above the main board's top edge (m).</summary>
        public float ExtraTop
        {
            get
            {
                float top = 0f;
                foreach (GuidePart p in Parts)
                    top = Math.Max(top, -p.Y);
                return top;
            }
        }
    }
}
