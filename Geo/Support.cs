using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo
{
    public enum SupportKind
    {
        /// <summary>单柱式: one post behind the board, for small and middle boards (GB 5768.2 4.8.12 a: 中、小型尺寸的禁令、指示、警告等标志).</summary>
        SinglePole,

        /// <summary>双柱式 (多柱式): two posts, for rectangular boards (4.8.12 a: 长方形标志).</summary>
        DoublePole,

        /// <summary>悬臂式: a post beside the road with an arm reaching over the carriageway; the board hangs under the arm (4.8.12 b).</summary>
        Cantilever,

        /// <summary>多柱式 with three posts: for the widest boards (more than 6 m), whose two posts would stand too far apart.</summary>
        TriplePole,

        /// <summary>门架式 (Fig. 7): a post on each side of the carriageway and a truss beam across it; the boards hang from the beam with their lower edges level (4.8.12 c).</summary>
        Gantry,

        /// <summary>双悬臂式 (Fig. 6 b): one post and an arm each side of it, the two arms of the same length; the boards on both arms face the same traffic. Appended last: the saved support is a hash of the id, not this number.</summary>
        DoubleCantilever,

        /// <summary>
        /// 附着式 (4.8.12 d, Fig. 8 b): the boards are fixed to a post that is not ours, a street lamp or a traffic signal's pole, which stands where the sign's own post would: what is built is the stack of boards
        /// on its channels and the clamps round the host; the post, its cap and its foot are not drawn (the game draws the host). Small boards only, as on a post. Appended last.
        /// </summary>
        Attached,

        /// <summary>
        /// 附着式 on a wall, fixed flat (0.12.2; GB 5768.2 4.8.12 d, GB 17733 5.1.2): the board's back on spacers WallGap in front of a building's wall, which is the plane z = 0 of the sign's frame; the sign faces
        /// out of the wall (+Z). The origin is on the wall at the height of the ground; the height of the board's lower edge is the item's reach. Any board (a wall carries a large one). Appended last.
        /// </summary>
        WallFlat,

        /// <summary>
        /// 附着式 on a wall with a bracket (0.12.2): a plate on the wall, an arm out of it along the sign's X (to the side SupportDef.Side says), a tie above and a knee below, and the board hanging under
        /// the arm on flat bars, its plane square to the wall (it faces along the wall, as a street-name plate at a corner does). The origin is the root of the arm on the wall at the height of the
        /// ground; the height of the board's lower edge is the item's reach. Small boards only, as on a post. Appended last.
        /// </summary>
        WallBracket,

        /// <summary>
        /// F 型悬臂式 (0.12.31; the owner: "F悬臂…是国内常见的一个牌子后有两个悬臂杆支持这样"): one post with two arms one above the other, reaching the same way, both behind the boards, which are clamped to
        /// both (the upper arm in their upper part, the lower in their lower part) and stand side by side as on a single cantilever. 0.12.26 to 0.12.30 had a row of boards on each of up to three arms
        /// (多悬臂式); the items of then all stand on the one row now. Appended last.
        /// </summary>
        FCantilever,

        /// <summary>
        /// 附着式 on a signal's arm (0.12.51): the board is clamped to the arm of another mod's signal pole (TsExtension.OtherHosts, HostArm), which passes behind its upper part as a cantilever's arm does;
        /// what is built is the board, its channels and the clamps, not the arm or the pole. The origin is on the arm's axis at the board's middle, the board faces the way the arm's heads do (its +Z),
        /// and the reach is the arm's radius there. One board to an item. Appended last.
        /// </summary>
        AttachedArm,

        /// <summary>
        /// 路上方附着式 (0.12.81; GB 5768.2 4.8.12 d, Fig. 8 a): the board is fixed flat to the side face of a structure that crosses over the road (an overpass, a bridge, an elevated road), above the lanes
        /// it is for, facing the traffic. The structure is the game's and is not drawn; the board stands on spacers as one flat on a wall does. The origin is on the face at the road's height; the height of
        /// the lower edge above it is the item's reach. Appended last.
        /// </summary>
        OverRoad
    }

    /// <summary>What the forms of support have in common.</summary>
    public static class SupportKinds
    {
        /// <summary>The boards hang side by side over the road: an arm, two arms or a beam.</summary>
        public static bool Hung(SupportKind kind) => kind == SupportKind.Cantilever || kind == SupportKind.DoubleCantilever || kind == SupportKind.Gantry || kind == SupportKind.FCantilever;

        /// <summary>A post with an arm (or two: both ways, or one above the other) and a free end.</summary>
        public static bool Arm(SupportKind kind) => kind == SupportKind.Cantilever || kind == SupportKind.DoubleCantilever || kind == SupportKind.FCantilever;

        /// <summary>A cantilever whose arms all reach one way (SupportDef.Side): the single and the F type, not the double.</summary>
        public static bool OneWayArm(SupportKind kind) => kind == SupportKind.Cantilever || kind == SupportKind.FCantilever;

        /// <summary>The boards are fixed to what is not the mod's and the support does not draw: a post of the game's or of another mod (a lamp post, a signal pole), or another mod's signal arm.</summary>
        public static bool Hosted(SupportKind kind) => kind == SupportKind.Attached || kind == SupportKind.AttachedArm;

        /// <summary>The board is clamped to another mod's signal arm (0.12.51).</summary>
        public static bool OnArm(SupportKind kind) => kind == SupportKind.AttachedArm;

        /// <summary>
        /// Whether the posts of a support can be painted at the root (0.12.58; SignBuilder.PaintRoot is called for them): every post and cantilever, a gantry on round posts or round legs (the portal's space truss's H legs, a
        /// span over PlanarGantryMax, and a height-limit frame's yellow-black columns are not painted); not a support on a wall or on another mod's pole or arm, which has no post of its own.
        /// </summary>
        public static bool RootPaintable(SupportKind kind, SupportLayout layout) => !Hosted(kind) && !OnWall(kind) && !OverRoad(kind) && !(kind == SupportKind.Gantry && (layout.BeamWidth > 0f && !layout.RoundLegs || layout.HeightLimit));

        /// <summary>The boards are fixed to a building's wall (flat on it, or on a bracket): no foundation, no post; the reach is the height of the lower edge.</summary>
        public static bool OnWall(SupportKind kind) => kind == SupportKind.WallFlat || kind == SupportKind.WallBracket;

        /// <summary>The board is fixed to the face of a structure over the road (0.12.81): no foundation, no post, no road of its own; the reach is the height of the lower edge above the road.</summary>
        public static bool OverRoad(SupportKind kind) => kind == SupportKind.OverRoad;

        /// <summary>The post of a hung support takes small boards of its own, stacked as on a single post under the lowest arm's or chord's joint (Assembly.OnPost): a cantilever's, a double cantilever's, an F type's, a gantry's first post.</summary>
        public static bool PostBoards(SupportKind kind) => Arm(kind) || kind == SupportKind.Gantry;
    }

    /// <summary>
    /// A style of support for a sign board. The choice is made for each board by SupportPlanner (the "auto" support, which is what a sign gets unless one is picked) from the board's
    /// shape and size, as GB 5768.2 4.8.12 asks: single post for small and middle signs, more posts for rectangular boards; the sizes of the parts follow from the board (SupportLayouts).
    /// The standard gives the forms, the clearance (4.8.11: lower edge 1.5 to 2.5 m above the road, 2.1 m where there are pedestrians, 2.3 m with non-motor traffic) and the setback of the
    /// board's inner edge (4.8.10: at least 25 cm from the carriageway); post sizes, foundations, the arm and the 5.5 m overhead clearance are makers' common values or provisional, not clauses.
    /// </summary>
    public sealed class SupportDef
    {
        public string Id;
        public string Zh;
        public string En;
        public SupportKind Kind;

        /// <summary>The height of the lower edge of the board above the ground (m); 0 takes the roadside value (4.8.11).</summary>
        public float Clearance;

        /// <summary>Cantilever: how far the arm reaches from the post (m) at the least.</summary>
        public float ArmLength;

        /// <summary>
        /// A wall bracket: the arm reaches from the wall to +X of the sign's frame (1) or to -X (-1). A board seen from either way along the wall needs both, and a turn of the sign does not make one of the
        /// other (it would put the arm into the wall): the mirror is a support of its own, named by MirrorId and Hidden from the panel, that the tool takes when the board faces the other way. A
        /// cantilever (0.12.25): its arm reaches to the driver's left (1, from the roadside) or to the driver's right (-1, from a median on the traffic's left).
        /// </summary>
        public int Side = 1;

        public string MirrorId;

        /// <summary>Not listed in the panel (SupportCatalog.Listed): the mirror of a listed support.</summary>
        public bool Hidden;

        /// <summary>A single cantilever whose one arm is cranked (0.12.55, 22MR601 p.78: SupportLayouts.CrankAngle, CrankLength); with boards for two arms or more it has them straight.</summary>
        public bool Crank;

        /// <summary>八角锥杆 (0.12.57): the post and the arms octagonal and tapered, the arms on square or round flanges (SupportLayouts.OctaArm, OctaPost, JointFlangeOf).</summary>
        public bool Octagonal;

        /// <summary>
        /// 上海型综合杆 (0.12.57, Shanghai DG/TJ 08-2362-2021): a twelve-sided tapered main pole (SupportLayouts.MainPole) on a buried flange, the arms as 八角锥杆's, the Ø160 upper pole with its lamp,
        /// collars, slots and door (SignBuilder.IntegratedFittings).
        /// </summary>
        public bool Integrated;

        /// <summary>The finish a support takes when it is chosen (0.12.57: the Shanghai pole's RAL 9011, DG/TJ 08-2362 A.5.8-1); null keeps the one chosen before.</summary>
        public SupportFinish? DefaultFinish;

        /// <summary>A street-name plate post (路名牌立柱, 0.12.57) of a city's style (PlateHead): its first board stands on the head at its top; None for the other supports.</summary>
        public PlateStyle Plate;

        /// <summary>杭州框架式悬臂 (0.12.57, SignBuilder.FramedArms): the boards in frames hung from the upper arm, a lower arm to the frames; they do not lean.</summary>
        public bool Framed;

        /// <summary>
        /// A board of SupportKind.OverRoad that hangs under the structure (0.12.81) instead of standing flat on its face: rods from plates on the soffit to a tube clamped behind the board, two sway braces back to the
        /// soffit (SignBuilder.BuildOverHung). The item's reach is then the soffit's height over the road, not the lower edge's; the board leans as one on an arm does (BoardTilt).
        /// </summary>
        public bool Hanger;

        /// <summary>Standing on the top of a building (0.12.76, a text light box's roof frame): a wall support (Kind WallFlat) whose plane stands upright on the roof, the origin on the roof under the board's middle.</summary>
        public bool Roof;

        /// <summary>A gantry whose beam is a space truss (front and back chords in two planes) at any span, on two laced round legs at each end (0.12.78); the portal gantry has one only over PlanarGantryMax, on H legs.</summary>
        public bool SpaceTruss;

        /// <summary>A free-standing height-limit frame (0.12.78): ladder columns and a light Warren beam in yellow and black bands, the board hung on its front; Clearance is the free height under the beam.</summary>
        public bool HeightLimit;

        /// <summary>The family the panel groups the support under (SupportFamilies: the form of it, whatever its post or arm is made of); see SupportCatalog.FamilyOf.</summary>
        public string Family => SupportFamilies.Of(this);

        public uint Hash => SignCatalog.HashOf(Id);
    }

    /// <summary>
    /// The supports by form (0.12.58; the owner: "整合立杆类型"): the panel shows a row of families and, under the one chosen, its variants, in place of one row of every support (27 of them by 0.12.57).
    /// The mirrors are the listed one's: a family is of the form and a variant of what it is made of or how it is dressed (a round or an octagonal post, a cranked arm, a city's style).
    /// </summary>
    public static class SupportFamilies
    {
        public const string Post = "post", Cantilever = "cantilever", Gantry = "gantry", Attached = "attached", Plate = "plate";

        /// <summary>The families in the order the panel shows them, with their names.</summary>
        public static readonly (string Id, string Zh, string En)[] All =
        {
            (Post, "立柱式", "Posts"), (Cantilever, "悬臂式", "Cantilevers"), (Gantry, "门架式", "Gantries"), (Attached, "附着式", "Attached"), (Plate, "路名牌立柱", "Street-name posts")
        };

        public static string Of(SupportDef def)
        {
            if (def == null)
                return Post;
            if (def.Plate != PlateStyle.None)
                return Plate;
            switch (def.Kind)
            {
                case SupportKind.Cantilever:
                case SupportKind.DoubleCantilever:
                case SupportKind.FCantilever:
                    return Cantilever;
                case SupportKind.Gantry:
                    return Gantry;
                case SupportKind.Attached:
                case SupportKind.AttachedArm:
                case SupportKind.WallFlat:
                case SupportKind.WallBracket:
                case SupportKind.OverRoad:
                    return Attached;
                default:
                    return Post;
            }
        }
    }

    public static class SupportCatalog
    {
        public const string SinglePole = "pole.single";
        public const string DoublePole = "pole.double";
        public const string TriplePole = "pole.triple";
        public const string Cantilever = "cantilever.single";
        public const string CantileverRight = "cantilever.single.r";

        /// <summary>The cranked single cantilever and its mirror (0.12.55).</summary>
        public const string CrankCantilever = "cantilever.crank", CrankCantileverRight = "cantilever.crank.r";

        /// <summary>八角锥杆 (0.12.57): the single cantilever and its mirror, the double cantilever (T type).</summary>
        public const string OctaCantilever = "cantilever.octa", OctaCantileverRight = "cantilever.octa.r", OctaDoubleCantilever = "cantilever.octa.double";

        /// <summary>上海型综合杆 (0.12.57) and its mirror.</summary>
        public const string Integrated = "integrated.sh", IntegratedRight = "integrated.sh.r";

        /// <summary>杭州框架式悬臂 (0.12.57) and its mirror.</summary>
        public const string Framed = "cantilever.framed", FramedRight = "cantilever.framed.r";

        /// <summary>路名牌立柱 (0.12.57) of Shanghai, Xuzhou, Hangzhou and Shenzhen.</summary>
        public const string PlateShanghai = "plate.sh", PlateXuzhou = "plate.xz", PlateHangzhou = "plate.hz", PlateShenzhen = "plate.sz";
        /// <summary>The F type and its mirror; the ids are those of the 多悬臂式 of 0.12.26, which saved items name.</summary>
        public const string FCantilever = "cantilever.multi", FCantileverRight = "cantilever.multi.r";
        public const string Gantry = "gantry.portal";

        /// <summary>The space-truss gantry and the height-limit frame (0.12.78); a mod before it does not know their hashes.</summary>
        public const string GantrySpace = "gantry.space", GantryLimit = "gantry.limit";
        public const string DoubleCantilever = "cantilever.double";
        public const string AttachedPole = "attached.pole";
        public const string WallFlat = "wall.flat";
        public const string WallRoof = "wall.roof";
        public const string WallBracket = "wall.bracket";
        public const string WallBracketLeft = "wall.bracket.l";
        public const string AttachedArm = "attached.arm";

        /// <summary>The support over the road (0.12.81); a mod before it does not know its hash.</summary>
        public const string OverRoad = "attached.over";

        /// <summary>The same hung on rods under the structure (0.12.81); a mod before it does not know its hash either.</summary>
        public const string OverRoadHung = "attached.over.hung";

        /// <summary>The height of the lower edge of a board on a roadside post: 4.8.11 asks 1.5 to 2.5 m, at least 2.1 m with pedestrians and 2.3 m with non-motor traffic; 2.3 m meets all three.</summary>
        public const float RoadsideClearance = 2.3f;

        private static readonly SupportDef[] s_All =
        {
            new SupportDef { Id = SinglePole, Zh = "单柱式", En = "Single post", Kind = SupportKind.SinglePole },
            new SupportDef { Id = DoublePole, Zh = "双柱式", En = "Double post", Kind = SupportKind.DoublePole },
            new SupportDef { Id = TriplePole, Zh = "三柱式", En = "Triple post", Kind = SupportKind.TriplePole },
            // 悬臂式: the board's lower edge above the clearance of the road (the summaries say 5.0 to 5.5 m, a maker's and an encyclopedia's figure, not a clause).
            // Named 单悬臂式 since 0.12.26, when the F type came (the id stays: it is what saved items name).
            new SupportDef { Id = Cantilever, Zh = "单悬臂式", En = "Single cantilever", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = 1, MirrorId = CantileverRight },
            // Its mirror (0.12.25): on a median, the post at the traffic's left and the arm over its lanes to the right.
            new SupportDef { Id = CantileverRight, Zh = "单悬臂式", En = "Single cantilever", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = -1, MirrorId = Cantilever, Hidden = true },
            // The cranked one of 22MR601 p.78 (0.12.55; the owner: "单悬臂横梁的 18° 折弯"), and its mirror.
            new SupportDef { Id = CrankCantilever, Zh = "单悬臂式（折臂）", En = "Single cantilever (cranked arm)", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = 1, MirrorId = CrankCantileverRight, Crank = true },
            new SupportDef { Id = CrankCantileverRight, Zh = "单悬臂式（折臂）", En = "Single cantilever (cranked arm)", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = -1, MirrorId = CrankCantilever, Crank = true, Hidden = true },
            // 八角锥杆 (0.12.57; the owner's "八角锥杆"): octagonal tapered post and arms, single (and its mirror) and double (the makers' T type).
            new SupportDef { Id = OctaCantilever, Zh = "单悬臂式（八角锥杆）", En = "Single cantilever (octagonal taper)", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = 1, MirrorId = OctaCantileverRight, Octagonal = true },
            new SupportDef { Id = OctaCantileverRight, Zh = "单悬臂式（八角锥杆）", En = "Single cantilever (octagonal taper)", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = -1, MirrorId = OctaCantilever, Octagonal = true, Hidden = true },
            new SupportDef { Id = OctaDoubleCantilever, Zh = "双悬臂式（八角锥杆）", En = "Double cantilever (octagonal taper)", Kind = SupportKind.DoubleCantilever, Clearance = 5.5f, ArmLength = 5.0f, Octagonal = true },
            // 上海型综合杆 (0.12.57; the owner's "上海型综合杆（多杆合一，石墨黑十二棱主杆）"): a cantilever on the Shanghai multi-function pole, and its mirror.
            new SupportDef { Id = Integrated, Zh = "上海型综合杆", En = "Shanghai multi-function pole", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = 1, MirrorId = IntegratedRight, Integrated = true, DefaultFinish = SupportFinish.Graphite },
            new SupportDef { Id = IntegratedRight, Zh = "上海型综合杆", En = "Shanghai multi-function pole", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = -1, MirrorId = Integrated, Integrated = true, DefaultFinish = SupportFinish.Graphite, Hidden = true },
            // 杭州框架式悬臂 (0.12.57; the owner's "杭州框架式悬挂悬臂"): the poles matte black (2015 guide 5.9.1), and its mirror.
            new SupportDef { Id = Framed, Zh = "框架式悬臂（杭州）", En = "Framed cantilever (Hangzhou)", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = 1, MirrorId = FramedRight, Framed = true, DefaultFinish = SupportFinish.Graphite },
            new SupportDef { Id = FramedRight, Zh = "框架式悬臂（杭州）", En = "Framed cantilever (Hangzhou)", Kind = SupportKind.Cantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = -1, MirrorId = Framed, Framed = true, DefaultFinish = SupportFinish.Graphite, Hidden = true },
            // 路名牌立柱 (0.12.57; the owner's "路名牌立柱（上海、徐州、杭州、深圳四种样式）"): the plate on a head at the post's top (PlateHead). Their posts' paint: Shanghai's silver and Xuzhou's
            // silver-grey (DB3203/T 1080 5.4.2) as the light grey, Hangzhou's black (2015 guide 5.9.1, fig. B.1), Shenzhen's light grey (photos).
            new SupportDef { Id = PlateShanghai, Zh = "路名牌立柱（上海）", En = "Street-name plate post (Shanghai)", Kind = SupportKind.SinglePole, Plate = PlateStyle.Shanghai, DefaultFinish = SupportFinish.LightGrey },
            new SupportDef { Id = PlateXuzhou, Zh = "路名牌立柱（徐州）", En = "Street-name plate post (Xuzhou)", Kind = SupportKind.SinglePole, Plate = PlateStyle.Xuzhou, DefaultFinish = SupportFinish.LightGrey },
            new SupportDef { Id = PlateHangzhou, Zh = "路名牌立柱（杭州）", En = "Street-name plate post (Hangzhou)", Kind = SupportKind.SinglePole, Plate = PlateStyle.Hangzhou, DefaultFinish = SupportFinish.Graphite },
            new SupportDef { Id = PlateShenzhen, Zh = "路名牌立柱（深圳）", En = "Street-name plate post (Shenzhen)", Kind = SupportKind.SinglePole, Plate = PlateStyle.Shenzhen, DefaultFinish = SupportFinish.LightGrey },
            // 门架式: the same clearance as the cantilever (a provisional figure); the span is the road's width (the tool sets it when the board is placed) and 14 m when it is not known.
            new SupportDef { Id = Gantry, Zh = "门架式", En = "Gantry", Kind = SupportKind.Gantry, Clearance = 5.5f, ArmLength = 14.0f },
            // 门架（空间桁架，前后双层）(0.12.78; the owner's expressway photo): the beam's chords in a front and a back plane at any span; the clearance and the span as the portal's.
            new SupportDef { Id = GantrySpace, Zh = "门架（空间桁架，前后双层）", En = "Gantry, space truss", Kind = SupportKind.Gantry, Clearance = 5.5f, ArmLength = 14.0f, SpaceTruss = true },
            // 限高架（黄黑）(0.12.78; the owner's photo of a tunnel entrance's height-limit frame): 4.5 m free under the beam, the span of the road, 8 m when it is not known.
            new SupportDef { Id = GantryLimit, Zh = "限高架（黄黑）", En = "Height-limit frame (yellow-black)", Kind = SupportKind.Gantry, Clearance = 4.5f, ArmLength = 8.0f, HeightLimit = true },
            // 双悬臂式 (4.8.12 b, Fig. 6 b): the same arm as the cantilever on each side of the post; ArmLength is the length of each.
            new SupportDef { Id = DoubleCantilever, Zh = "双悬臂式", En = "Double cantilever", Kind = SupportKind.DoubleCantilever, Clearance = 5.5f, ArmLength = 5.0f },
            // 附着式 (4.8.12 d, Fig. 8): fixed to a lamp post or a signal pole of the game's; the lower edge of the board is at the roadside clearance, 2.3 m, above the foot of the post.
            new SupportDef { Id = AttachedPole, Zh = "附着式（路灯杆、信号灯杆）", En = "Attached (lamp post, signal pole)", Kind = SupportKind.Attached },
            // 附着式 on a building's wall (0.12.2): flat on it, or on a bracket out of it; the bracket that reaches the other way is the hidden mirror.
            new SupportDef { Id = WallFlat, Zh = "附着式（墙面直装）", En = "On a wall (flat)", Kind = SupportKind.WallFlat },
            // On the top of a building (0.12.76, the text light box's roof frame; any board is refused by the tool but a text): upright on the roof, on legs and rear braces or on feet.
            new SupportDef { Id = WallRoof, Zh = "屋顶", En = "Roof", Kind = SupportKind.WallFlat, Roof = true },
            new SupportDef { Id = WallBracket, Zh = "附着式（墙面支架）", En = "On a wall (bracket)", Kind = SupportKind.WallBracket, Side = 1, MirrorId = WallBracketLeft },
            new SupportDef { Id = WallBracketLeft, Zh = "附着式（墙面支架）", En = "On a wall (bracket)", Kind = SupportKind.WallBracket, Side = -1, MirrorId = WallBracket, Hidden = true },
            // F 型悬臂式 (0.12.26 as 多悬臂式, the F type of two arms behind the boards since 0.12.31): the cantilever's clearance and arm. And its mirror, as the cantilever's.
            new SupportDef { Id = FCantilever, Zh = "F 型悬臂式", En = "F-type cantilever", Kind = SupportKind.FCantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = 1, MirrorId = FCantileverRight },
            new SupportDef { Id = FCantileverRight, Zh = "F 型悬臂式", En = "F-type cantilever", Kind = SupportKind.FCantilever, Clearance = 5.5f, ArmLength = 5.0f, Side = -1, MirrorId = FCantilever, Hidden = true },
            // On the arm of another mod's signal pole (0.12.51): the arm's height is the board's (it passes behind the board's upper part).
            new SupportDef { Id = AttachedArm, Zh = "附着式（信号灯横杆）", En = "Attached (signal arm)", Kind = SupportKind.AttachedArm },
            // 路上方附着式 (0.12.81; GB 5768.2 4.8.12 d, Fig. 8 a): flat on the face of a bridge, an overpass or an elevated road over the lanes; the height of the lower edge is the structure's.
            new SupportDef { Id = OverRoad, Zh = "附着式（路上方，桥梁、立交）", En = "Attached (over the road, bridge)", Kind = SupportKind.OverRoad },
            // The same kind hung under the structure on rods (0.12.81, the owner: "吊挂版本也做"): Fig. 8 a draws no hung board, so its construction is this mod's.
            new SupportDef { Id = OverRoadHung, Zh = "附着式（路上方，桥下吊挂）", En = "Attached (over the road, hung under)", Kind = SupportKind.OverRoad, Hanger = true }
        };

        private static readonly SupportDef[] s_Listed = Array.FindAll(s_All, def => !def.Hidden);
        private static readonly SupportDef[] s_ListedAlone = Array.FindAll(s_Listed, def => !SupportKinds.OnArm(def.Kind));

        /// <summary>
        /// Whether another mod gives poles and arms to fix boards to (0.12.106; the owner, 7 Nov: without the Realistic Information Extension what belongs to it must not show): set by the mod to what
        /// TsExtension.ProvidesHosts says. Without it the support on another mod's signal arm is not offered, as there is no arm to put a board on.
        /// </summary>
        public static Func<bool> HostsProvided { get; set; } = () => false;

        /// <summary>
        /// The supports by the hash of their id (what an item saves): made once, as the catalogue is, so a lookup is one probe and not the hash of every id (27 of them, 325 ns a call: a board that has a support
        /// of its own asked for it at every frame of placing). Where two ids have one hash the first in the catalogue is the one found, as the walk through it found it. Read only, so any thread may ask.
        /// </summary>
        private static readonly Dictionary<uint, SupportDef> s_ByHash = ByHashTable();

        private static Dictionary<uint, SupportDef> ByHashTable()
        {
            var table = new Dictionary<uint, SupportDef>(s_All.Length);
            foreach (SupportDef def in s_All)
            {
                uint hash = def.Hash;
                if (hash != 0u && !table.ContainsKey(hash))
                    table[hash] = def;
            }

            return table;
        }

        /// <summary>Every support, the hidden mirrors too: what a saved hash is looked up in.</summary>
        public static IReadOnlyList<SupportDef> All => s_All;

        /// <summary>The supports the panel offers (and the , . keys go round): all but the hidden mirrors, and the one on another mod's arm while no mod gives arms (HostsProvided).</summary>
        public static IReadOnlyList<SupportDef> Listed => HostsProvided() ? s_Listed : s_ListedAlone;

        /// <summary>The supports the panel may offer at some time (Listed with the arm's): the ones whose pictures are made at the start, when the other mod may not have joined yet.</summary>
        public static IReadOnlyList<SupportDef> Offerable => s_Listed;

        /// <summary>The support as the panel names it: a hidden mirror is shown as the listed one it mirrors.</summary>
        public static SupportDef ListedOf(SupportDef def) => def != null && def.Hidden ? ById(def.MirrorId) ?? def : def;

        public static SupportDef Default => s_All[0];

        /// <summary>The arm of a cantilever (single or F type) reaching to the driver's right (right) or left, which is the support or its mirror (SupportDef.Side); any other support as it is.</summary>
        public static SupportDef Facing(SupportDef def, bool right)
        {
            if (def == null || def.MirrorId == null || (def.Side < 0) == right)
                return def;
            return ById(def.MirrorId) ?? def;
        }

        public static SupportDef ById(string id)
        {
            foreach (SupportDef def in s_All)
            {
                if (def.Id == id)
                    return def;
            }

            return null;
        }

        /// <summary>The support with this hash; null for 0 (automatic) and for a hash that is not in the catalogue (a pack that is gone).</summary>
        public static SupportDef ByHash(uint hash)
        {
            return hash != 0u && s_ByHash.TryGetValue(hash, out SupportDef def) ? def : null;
        }

        /// <summary>The support an item is drawn with: its own when it is known, else the one the planner makes for this board.</summary>
        public static SupportDef For(SignDef sign, uint supportHash) => ByHash(supportHash) ?? SupportPlanner.Auto(sign);
    }

    /// <summary>What the road says about the form of the support (GB 5768.2 4.8.12): how many lanes the sign's traffic has, and whether the exit is on the left.</summary>
    public struct SupportContext
    {
        /// <summary>The car lanes of the traffic the sign is for; 0 when not known.</summary>
        public int LanesSameDirection;

        /// <summary>An exit board for an exit on the left of the carriageway (4.8.12 c 7), or for an exit ramp of two lanes or more (c 6).</summary>
        public bool ExitLeftOrWide;

        /// <summary>The span a gantry would have here (m): the road's width between the posts; 0 when it is not known (the gantry's default span is taken).</summary>
        public float Span;
    }

    /// <summary>The automatic support: which form a board gets from its shape and size.</summary>
    public static class SupportPlanner
    {
        /// <summary>
        /// The form when the road is known: the gantry where the standard asks for one that a post cannot do (4.8.12 c): three lanes or more in the direction (3), an exit on the left or a
        /// ramp of two lanes or more (6, 7); the space-truss gantry over SupportLayouts.SpaceGantryFrom (0.12.78: the owner's photo of an expressway's double-layer gantry), the portal below it. Anything else is the board's own choice (Auto). Not decided here: narrow space, a wide road, large vehicles hiding the sign (b, c 1 2 4 5), which
        /// need what the game does not tell; the panel's picks cover them.
        /// </summary>
        public static SupportDef AutoFor(SignDef sign, SupportContext context)
        {
            bool rectangular = sign.Shape == ShapeKind.Rect || sign.Shape == ShapeKind.RoundRect;
            bool guide = sign.Template != null && sign.Group == "guide";
            if (rectangular && guide && (context.LanesSameDirection >= 3 || context.ExitLeftOrWide))
                return SupportCatalog.ById(SupportLayouts.SpaceGantryFor(context.Span) ? SupportCatalog.GantrySpace : SupportCatalog.Gantry);
            return Auto(sign);
        }

        /// <summary>
        /// The widest board that one post carries, the tallest (m) and the largest (m²: 22MR601 p.76, note 9, a φ114 post carries 1.6 m²; the largest board on one post read is 1.0 × 1.2 m, REPORT.md
        /// roadside, main difference 7): larger rectangular boards are of the kind the standard puts on more posts.
        /// </summary>
        public const float SinglePostMaxWidth = 1.6f, SinglePostMaxHeight = 2.4f, SinglePostMaxArea = 1.6f;

        /// <summary>The widest board that two posts carry (m); beyond it three.</summary>
        public const float DoublePostMaxWidth = 6.0f;

        /// <summary>
        /// Whether a board can be fixed to a post that is not ours (an attached support, 4.8.12 d): the boards of a single post, the small ones; a wide or tall board, or an overhead screen, needs
        /// posts of its own. Hosts are lamp posts and signal poles, which carry what a single post carries.
        /// </summary>
        public static bool FitsHost(SignDef sign) => sign != null && (sign.Group == Signs.LampAds.Group || Auto(sign).Kind == SupportKind.SinglePole);

        /// <summary>
        /// Whether a board goes on a signal's arm (0.12.51): a road name plate or a lane sign as Chinese junctions hang them there (2 x 0.6 m, 1.0 x 1.5 m), up to MaxArmWidth by MaxArmHeight and
        /// MaxArmArea, this mod's estimate of what a signal arm carries beside its heads; not a lamp-post ad (it has its own fixings) nor an electric box.
        /// </summary>
        public static bool FitsArm(SignDef sign) =>
            sign != null && sign.Group != Signs.LampAds.Group && sign.Group != Signs.Cabinets.Group && sign.Width <= MaxArmWidth && sign.Height <= MaxArmHeight && AreaOf(sign) <= MaxArmArea;

        /// <summary>The area a board counts for in the planner: its box, or its ink when it has one (a row of channel letters, SignDef.InkArea): letters carry less than a plate of their box.</summary>
        public static float AreaOf(SignDef sign)
        {
            float box = sign.Width * sign.Height;
            if (sign.InkArea == null)
                return box;
            float ink = sign.InkArea();
            return ink > 0f && ink < box ? ink : box;
        }

        public const float MaxArmWidth = 4f, MaxArmHeight = 1.6f, MaxArmArea = 4f;

        /// <summary>Whether a board goes on a wall with this support: flat on a wall any board does; a bracket carries what a single post carries (FitsHost).</summary>
        public static bool FitsWall(SignDef sign, SupportKind kind) => sign != null && (kind == SupportKind.WallFlat || FitsHost(sign));

        /// <summary>
        /// GB 5768.2 4.8.12 a: a single post for medium and small prohibition, mandatory and warning signs (circles, octagons, triangles, small squares), several posts for rectangular boards.
        /// So: round, octagonal and small rectangular boards on one post; wide rectangular boards on two posts, and on three when they are more than 6 m wide. The overhead forms (cantilever,
        /// gantry) are chosen by the road (4.8.12 b and c: narrow space, wide roads, three lanes or more) and are picked in the panel. stack: the sign is the whole of several stacked boards
        /// (Assembly.TotalDef), which stay on one post as 22MR601 stacks them (up to four) whatever the box round them; SinglePostMaxArea is a board's own.
        /// </summary>
        public static SupportDef Auto(SignDef sign, bool stack = false)
        {
            // A variable-message sign (4.11.6: before the exits of an expressway, in tunnels, over the lanes) hangs over the road: a lane-control screen for each lane, a text screen across it.
            // The traffic-state screen is a large board at the side of the road on an arm, as the ones over city roads are; the others hang over the lanes on a gantry.
            if (sign.Group == "variable")
                return SupportCatalog.ById(sign.Id == TrafficMap.TemplateId ? SupportCatalog.Cantilever : SupportCatalog.Gantry);

            // A text light box longer than two posts carry hangs on a gantry (0.12.76; Signs.TextSupports): the planner's rules for any board go on for the shorter ones.
            SupportDef text = Signs.TextSupports.Auto(sign);
            if (text != null)
                return text;

            // A toll station's name hangs on its canopy: a gantry across the plaza (0.12.26).
            if (sign.Id == Signs.TollSigns.StationId)
                return SupportCatalog.ById(SupportCatalog.Gantry);

            // A lamp-post ad goes on the game's lamp posts (0.12.49).
            if (sign.Group == Signs.LampAds.Group)
                return SupportCatalog.ById(SupportCatalog.AttachedPole);

            // The pedestrian and metro boards (0.12.57) are a single post's (the metro board 1640 wide on one, fig. 20) or a lamp post's (合杆).
            if (sign.Group == Signs.WayfindingSigns.Group)
                return SupportCatalog.ById(SupportCatalog.SinglePole);

            bool rectangular = sign.Shape == ShapeKind.Rect || sign.Shape == ShapeKind.RoundRect;
            if (!rectangular || (sign.Width <= SinglePostMaxWidth && sign.Height <= SinglePostMaxHeight && (stack || AreaOf(sign) <= SinglePostMaxArea)))
                return SupportCatalog.ById(SupportCatalog.SinglePole);
            return SupportCatalog.ById(sign.Width <= DoublePostMaxWidth ? SupportCatalog.DoublePole : SupportCatalog.TriplePole);
        }

        /// <summary>The longest arm of an octagonal tapered arm or of the multi-function pole's: Shanghai DG/TJ 08-2362-2021 A.4.2-3 lists the arms from 3 to 16 m (REPORT.md card A).</summary>
        public const float OctaArmMax = 16f;

        /// <summary>
        /// The support for a board in the style of a profile (DESIGN-support-styles.md): the form as Auto and AutoFor decide it (the road's context makes a gantry of a guide board, several boards on one
        /// post count as one stack), then the profile's variant of that form, then its paint. The player's own pick of a support is the caller's, before this. Auto and AutoFor are not changed.
        /// </summary>
        public static PlannedSupport AutoStyled(SignDef sign, SupportContext context, StyleProfile profile, bool stack = false, SupportStyle player = default, PaintTouch touched = PaintTouch.None)
        {
            SupportDef plain = AutoFor(sign, context);
            if (stack && plain.Kind != SupportKind.Gantry)
                plain = Auto(sign, true);
            return Resolve(plain, PlateHead.FitsDef(sign), sign, profile, player, touched);
        }

        /// <summary>
        /// The profile's variant for the plain support of a board, or the plain one: the first of the form's variants that can hold the board (Holds), with a note of each that could not; the paint of the
        /// profile, a variant's own finish over the profile's (as the editor does when a support with a finish of its own is chosen: the backs and the root stay), and any part the player has set since the
        /// profile was chosen (touched, the values in player) over both. streetName: the board is a street or lane name sign (PlateHead.FitsDef); for a stack, any of its boards.
        /// </summary>
        public static PlannedSupport Resolve(SupportDef plain, bool streetName, SignDef sign, StyleProfile profile, SupportStyle player = default, PaintTouch touched = PaintTouch.None)
        {
            SupportDef chosen = plain;
            string note = "";
            IReadOnlyList<string> wanted = profile.VariantsOf(SupportForms.Of(plain, streetName));
            for (int i = 0; i < wanted.Count; i++)
            {
                SupportDef variant = SupportCatalog.ById(wanted[i]);
                if (Holds(variant, sign, out string why))
                {
                    chosen = variant;
                    break;
                }

                note += (note.Length > 0 ? "; " : "") + variant.Id + " not used: " + why;
            }

            if (note.Length > 0)
                note += "; took " + chosen.Id;
            var style = new SupportStyle
            {
                Finish = (touched & PaintTouch.Finish) != 0 ? player.Finish : chosen.DefaultFinish ?? profile.Finish,
                BlackBack = (touched & PaintTouch.BlackBack) != 0 ? player.BlackBack : profile.BlackBack,
                Root = (touched & PaintTouch.Root) != 0 ? player.Root : profile.Root
            };
            return new PlannedSupport { Support = chosen, Plain = plain, Style = style, Note = note };
        }

        /// <summary>
        /// Whether a variant can hold the board: the arms of the octagonal and multi-function types are listed to OctaArmMax, and a board that needs a longer arm (BoardFromPost, its width, ArmTipInside:
        /// SupportLayouts) goes to the next variant. The others are limited only by SupportLayouts.MaxSpan, which the layout enforces itself.
        /// </summary>
        private static bool Holds(SupportDef variant, SignDef sign, out string why)
        {
            why = "";
            if (!SupportKinds.Arm(variant.Kind) || !(variant.Octagonal || variant.Integrated))
                return true;
            float arm = Math.Max(variant.ArmLength, Assembly.BoardFromPost + sign.Width - Assembly.ArmTipInside);
            if (arm <= OctaArmMax)
                return true;
            why = string.Format(CultureInfo.InvariantCulture, "its arm would be {0:0.#} m, over the {1:0} m the standard lists", arm, OctaArmMax);
            return false;
        }
    }

    /// <summary>A support the planner resolved in a style: the support (a variant of the profile, or the plain one), the plain one the planner decided on, the paint, and why a variant was not used (empty when none was skipped).</summary>
    public struct PlannedSupport
    {
        public SupportDef Support, Plain;
        public SupportStyle Style;
        public string Note;

        /// <summary>
        /// The hash an item stores (TsItem.SupportHash): the variant's when the profile changed the support, else 0, the planner's choice, which goes on following the board when it is edited (what the panel
        /// stores when no support is picked). Every support a profile names is in the catalogue since 0.12.57, so a mod of 0.12.66 to 0.12.72 draws the same support from it.
        /// </summary>
        public uint StoredSupportHash => Support.Id == Plain.Id ? 0u : Support.Hash;

        /// <summary>The byte an item stores (TsItem.Style).</summary>
        public byte StoredStyle => Style.Packed;
    }

    /// <summary>The measures of one board on one support: everything the builder and the tests need to agree about.</summary>
    public struct SupportLayout
    {
        public SupportKind Kind;

        /// <summary>The radius of the post (m): of the tube; a polygon's corners' at its foot (y = 0).</summary>
        public float PoleRadius;

        /// <summary>A tapered post (0.12.57, 八角锥杆): the radius of its corners at its top (PoleTop); 0 for a straight post. And its sides: 0 round, else a polygon with a flat to the front (+Z).</summary>
        public float PoleTopRadius;

        public int PoleSides;

        /// <summary>The arms are 八角锥杆's (0.12.57: tapered octagons on square or round plates, SupportLayouts.OctaArm); the support is the Shanghai multi-function pole (SupportDef.Integrated).</summary>
        public bool OctaArms, Integrated;

        /// <summary>A street-name plate post (0.12.57): its style and head; None and null for the other supports.</summary>
        public PlateStyle Plate;
        public PlateHead Head;

        /// <summary>杭州框架式悬臂 (0.12.57): the boards in frames hung from the upper arm (ArmY); the lower arm's height.</summary>
        public bool Framed;
        public float LowerArmY;

        /// <summary>The post's radius (of its corners) at a height.</summary>
        public float PoleRadiusAt(float y) => PoleTopRadius <= 0f || PoleTop <= 0f ? PoleRadius : PoleRadius + (PoleTopRadius - PoleRadius) * Math.Max(0f, Math.Min(1f, y / PoleTop));

        /// <summary>The arm's radius (of its corners) at a distance from the post's axis (m).</summary>
        public float ArmRadiusAt(float x) => ArmLength <= 0f ? ArmRootRadius : ArmRootRadius + (ArmTipRadius - ArmRootRadius) * Math.Max(0f, Math.Min(1f, Math.Abs(x) / ArmLength));

        /// <summary>The sideways positions of the posts (m, in the sign's frame, +X toward the road for a cantilever); one for the cantilever.</summary>
        public float[] PoleX;

        public float BottomClearance;
        public float BoardCentreX, BoardCentreY, BoardTop;

        /// <summary>The planes of the board's back and front in the sign's frame (+Z is the way the sign faces).</summary>
        public float BackZ, FrontZ;

        /// <summary>Standing on a roof (SupportDef.Roof).</summary>
        public bool Roof;

        /// <summary>The top of the post.</summary>
        public float PoleTop;

        /// <summary>Cantilever: the arm's length; gantry: the distance between the posts (the span). And the height of the arm's or beam's axis.</summary>
        public float ArmLength, ArmY;

        /// <summary>
        /// Gantry only: the depth of the truss beam (from the axis of the lower chord to the axis of the upper), how far its back chords are behind the front ones (0: a planar truss), the radius of
        /// the chords and their heights (the planar truss's three, the space truss's two in each plane).
        /// </summary>
        public float BeamDepth, BeamWidth, ChordRadius;

        /// <summary>Gantry only: the space truss stands on round legs, two at each end (SupportDef.SpaceTruss), not on 22MR601's H legs.</summary>
        public bool RoundLegs;

        /// <summary>Gantry only: a height-limit frame (SupportDef.HeightLimit): two ladder columns at each end, the beam a light Warren truss in a single plane.</summary>
        public bool HeightLimit;

        public float[] ChordYs;

        /// <summary>Cantilever only: the radius of the arms (m): of uniform tube, root and tip the same; tapered octagons' corners (八角锥杆) at their root and their tip.</summary>
        public float ArmRootRadius, ArmTipRadius;

        /// <summary>Cantilever only: how near the post's axis a board's edge may come (m).</summary>
        public float BoardFrom;

        /// <summary>A cranked arm (0.12.55, SupportDef.Crank): how much lower than ArmY its root on the post is and how far out the bend is (m); 0 for a straight arm.</summary>
        public float CrankRise, CrankRun;

        /// <summary>Wall bracket only: how far out along the arm its knee reaches (m).</summary>
        public float KneeX;

        /// <summary>The base of the post (the cantilever's, each gantry post's or leg's); null for the other supports.</summary>
        public BasePlate Base;

        /// <summary>The way the arm of a cantilever reaches in the sign's frame: +1 to +X (the driver's left), -1 to -X (SupportDef.Side). 1 for every other support (a gantry's span is to +X).</summary>
        public float ArmSide;

        /// <summary>The heights of the axes of a cantilever's arms, top first (one to three, ArmCount); null for any other support.</summary>
        public float[] ArmYs;

        /// <summary>
        /// Cantilever and gantry: the height of the axis of the tube the boards are clamped to (the arm; the beam's lower chord) and the largest radius of that tube. The tube passes behind the
        /// upper part of the boards, which stand in front of it (GB 5768.2 Fig. 7): vertical channels on their backs are clamped round it. Nothing hangs.
        /// </summary>
        public float HangY, HangRadius;

        /// <summary>
        /// A board hung under a structure over the road (SupportDef.Hanger, 0.12.81): the height of the soffit the rods' plates are on (the rods' top), and where the rods and the braces' ends are along X (the
        /// rods' feet are on the tube at HangY; one rod to about 1.5 m of the board's width, two at the least). The origin is on the tube's axis.
        /// </summary>
        public bool Hanger;
        public float HangerTop;
        public float[] HangerXs;

        /// <summary>Where the rails on the board's back are on a post (fractions of the board's height from its middle: SupportLayouts.RailKFor).</summary>
        public float[] RailK;

        /// <summary>How far toward the road the structure that stands beside it reaches from the sign's origin (m): half the board for the posts, the foundation for the cantilever (whose board is over the road).</summary>
        public float InnerReach;

        /// <summary>
        /// The hung supports whose post takes boards of its own (SupportKinds.PostBoards): the lower edge of the lowest of them, the plane of their backs (in front of the post, on a hoop's backing and a
        /// channel), and the highest their stack may reach (under the flange of the lowest arm or chord).
        /// </summary>
        public float PostClearance, PostBackZ, PostTop;
    }

    public static partial class SupportLayouts
    {
        /// <summary>The thickness of the board's folded edge (m).</summary>
        public const float BoardThickness = 0.03f;

        /// <summary>
        /// The radius of the post an attached board is fixed to (m). The game does not give the radius of its lamp posts and signal poles (the size of their prefab is of the whole object with its arm
        /// and lamp head), so this is a figure of the usual poles, 18 cm across: provisional, to be looked at in the game.
        /// </summary>
        public const float HostRadius = 0.09f;

        /// <summary>
        /// The radius a hosted support's reach is taken as (0.12.51): another mod's pole or arm gives its own, kept as the item's reach; 0, or anything over MaxHostRadius (the span or the arm a support
        /// had before it was changed to this one), is the game's post (HostRadius) or, on an arm, ArmHostRadius.
        /// </summary>
        public static float HostRadiusOf(float reach, float fallback) => reach > 0f && reach <= MaxHostRadius ? reach : fallback;

        public const float MaxHostRadius = 0.5f, ArmHostRadius = 0.08f;

        /// <summary>
        /// The slide channel on the board's back (22MR601 p.141: aluminium 68 × 19), how deep and how wide; and the hoop's backing between the post and the channel (抱箍衬底 5 mm, the hoop's ears on
        /// it): the post's face 31 mm behind the board's back, in the 25 to 45 mm of the photos (REPORT.md roadside, main difference 12).
        /// </summary>
        public const float RailDepth = 0.019f, RailWidth = 0.068f, PlateDepth = 0.012f;

        /// <summary>
        /// Overhead supports: the room between the channel on the board's back and the collar round the tube. A Chinese overhead board is fixed straight onto the arm or the beam, on vertical
        /// slide channels (滑动槽钢) held by clamps (抱箍): the tube passes behind the board, which covers it, as in GB 5768.2 Fig. 7 (the boards in front of the truss, their lower edges level).
        /// </summary>
        public const float ClampRoom = PlateDepth + RailDepth;

        /// <summary>
        /// The arms of a cantilever by its boards (22MR601 pp.78-85): one up to 2.3 m² (1F), two 0.9 m apart up to 5 m² (2F), three 1.2 m apart above (3F); the F type two at the least. They stay behind
        /// the board, no further apart than its height less 0.3 m allows, and at least MinArmSpacing apart, else one fewer (the F type keeps two, its lower arm then under a low board).
        /// </summary>
        public static int ArmCount(float area, float boardHeight, bool f, out float spacing)
        {
            int arms = area <= 2.3f ? 1 : area <= 5f ? 2 : 3;
            if (f)
                arms = Math.Max(arms, 2);
            while (true)
            {
                spacing = arms == 3 ? 1.2f : 0.9f;
                if (arms == 1)
                    return 1;
                spacing = Math.Min(spacing, (boardHeight - 0.3f) / (arms - 1));
                if (spacing >= MinArmSpacing)
                    return arms;
                if (f && arms == 2)
                {
                    spacing = MinArmSpacing;
                    return 2;
                }

                arms--;
            }
        }

        public const float MinArmSpacing = 0.4f;

        /// <summary>
        /// The radius of a cantilever's post by the area of the boards it carries (22MR601 pp.78-92, seamless tube): φ219 to 2.3 m², φ273 to 5 m², φ377 to 15 m², φ426 (the elevated roads' largest)
        /// above; a double cantilever's, by both sides' area, φ273 to 15 m², φ325 to 25 m², φ377 above.
        /// </summary>
        public static float ArmPostRadius(float area, bool twin) => twin
            ? (area <= 15f ? 0.1365f : area <= 25f ? 0.1625f : 0.1885f)
            : (area <= 2.3f ? 0.1095f : area <= 5f ? 0.1365f : area <= 15f ? 0.1885f : 0.213f);

        /// <summary>How far above the upper arm's axis a cantilever's post ends (22MR601 pp.78, 80, 84: 300 mm; a cranked arm's root).</summary>
        public const float ArmPostAbove = 0.3f;

        /// <summary>
        /// The cranked arm of 22MR601 p.78 (φ219 1F): from the post rising 18° (the post and the arm at 108°) for 1580 mm (taken from the post's axis: the drawing's line starts at the post), then level
        /// (2500 there); the boards on the level part, CrankBoardGap beyond the bend at the least.
        /// </summary>
        public const float CrankAngle = (float)(18.0 * Math.PI / 180.0), CrankLength = 1.58f, CrankBoardGap = 0.1f;

        /// <summary>
        /// The joint of an arm or a chord to its post (22MR601 pp.78, 80, 95): a stub of the tube welded to the post, a pair of flanges 20 mm thick bolted together (their middle JointStub out from the
        /// post's face, scaled from the p.80 elevation, ±10 %), ribs on four sides of the stub and of the arm. The flanges' radius: φ300 for a 1F arm on φ219, φ320 for a gantry's chord, φ350 else;
        /// the space truss's chords end on plates of φ300 on its legs.
        /// </summary>
        public static float JointFlangeOf(SupportLayout layout) => layout.Kind == SupportKind.Gantry
            ? (layout.BeamWidth > 0f ? 0.15f : 0.16f)
            : layout.OctaArms ? OctaFlangeHalf(layout.ArmLength, layout.ArmYs?.Length ?? 1)
            : (layout.ArmRootRadius <= 0.057f && layout.PoleRadius <= 0.11f ? 0.15f : 0.175f);

        /// <summary>
        /// 八角锥杆's arm flanges (Shanghai DG/TJ 08-2362-2021 table A.4.2-4, fig. A.4.3): a single arm on a square plate, 法兰1 430 × 430 with 6-M24 on a 350 square to 10 m, 法兰2 470 × 470 with
        /// 6-M30 on 380 beyond; the parallel arms of 2F and 3F on round 法兰3 (Ø350, 8-M16 on Ø290, arms to 6 m) or 法兰4 (8-M20, 8 m). Half the plate's side or its radius.
        /// </summary>
        public static float OctaFlangeHalf(float armLength, int arms) => arms > 1 ? 0.175f : armLength <= 10f ? 0.215f : 0.235f;

        /// <summary>
        /// 八角锥杆's arms (DG/TJ 08-2362-2021 table A.4.2-3, diameters corner to corner, fig. A.4.1): a single arm tapers from 150 (3 and 4 m), 200 (5, 6), 220 (8), 240 (10), 270 (12), 285 (14) or 300
        /// (16 m and longer) to 120 at its tip, the row of the next length up; the parallel arms of 2F and 3F are octagons of 140 (4 to 6 m) or 160 (8 m and longer) all along. Root and tip radii.
        /// </summary>
        public static void OctaArm(float length, int arms, out float root, out float tip)
        {
            if (arms > 1)
            {
                root = tip = length <= 6f ? 0.07f : 0.08f;
                return;
            }

            root = length <= 4f ? 0.075f : length <= 6f ? 0.10f : length <= 8f ? 0.11f : length <= 10f ? 0.12f : length <= 12f ? 0.135f : length <= 14f ? 0.1425f : 0.15f;
            tip = 0.06f;
        }

        /// <summary>
        /// 八角锥杆's post (a maker's L-type octagonal signal poles, zjtcn.com's listing quoted in scratch/research-octagon/REPORT.md; no standard gives them): 280 at the foot and 220 at the top (corner to
        /// corner) with an arm to 9 m, 330 and 280 with a 12 m arm. The first for a single arm to 9 m carrying boards to 5 m², the second else (a double cantilever, longer arms, larger boards), over
        /// the post's own height. Foot and top radii.
        /// </summary>
        public static void OctaPost(float armLength, float area, bool twin, out float foot, out float top)
        {
            bool light = !twin && armLength <= 9f && area <= 5f;
            foot = light ? 0.14f : 0.165f;
            top = light ? 0.11f : 0.14f;
        }

        /// <summary>
        /// The rated bending moment of 八角锥杆's arms (DG/TJ 08-2362-2021 table A.4.2-3, kN·m): a single arm 20 (3, 4 m), 50 (5, 6), 65 (8), 75 (10), 95 (12), 110 (14), 145 (16 m and longer);
        /// a parallel arm 30 (to 6 m) or 40.
        /// </summary>
        public static float ArmMoment(float length, int arms) => arms > 1 ? (length <= 6f ? 30f : 40f)
            : length <= 4f ? 20f : length <= 6f ? 50f : length <= 8f ? 65f : length <= 10f ? 75f : length <= 12f ? 95f : length <= 14f ? 110f : 145f;

        /// <summary>
        /// The Shanghai pole's main pole (DG/TJ 08-2362-2021 table A.4.2-1, corner to corner, a twelve-sided section: 2018 guide 5.3.6) by its arms' moments together against the table's ratings:
        /// 主杆5 280 to 240 (100 kN·m), 主杆6 300 to 240 (110), 主杆7 320 to 280 (200). Foot and top radii.
        /// </summary>
        public static void MainPole(float armLength, int arms, out float foot, out float top)
        {
            float moment = ArmMoment(armLength, arms) * arms;
            foot = moment <= 100f ? 0.14f : moment <= 110f ? 0.15f : 0.16f;
            top = moment <= 110f ? 0.12f : 0.14f;
        }

        /// <summary>
        /// How far the Shanghai main pole goes over its upper arm's axis (DG/TJ 08-2362-2021 fig. A.2.3-2: 235 for a single arm; fig. A.2.3-5: 220 for parallel ones), where its upper pole begins.
        /// </summary>
        public static float MainPoleAbove(int arms) => arms > 1 ? 0.22f : 0.235f;

        /// <summary>
        /// 杭州框架式悬臂 (0.12.57), measured on photo c9 (Fengqi Rd, side view, ±15 %) and the 2015 guide's fig. A.1 (their mean where both show it): the hangers' length (the upper arm's axis over
        /// the frame's top; c9: 0.11 of the board's height, about 0.3 m), how far the upper arm passes the boards' outer edge (×board width: 0.06 and 0.11), how far the upper and the lower arm reach
        /// beyond the post (×reach: 0.09 and 0.10; 0.066 and 0.07), the hangers' places in from the outer edge (×width: 0.15 and 0.14) and the post's edge (fig. A.1: 0.07; c9 hides it), the lower
        /// arm at the frame's middle (c9 0.48, fig. A.1 0.59 from the top), the post 0.5 m over the upper arm (c9). The frame's face 60 mm and depth 100 mm, the hangers φ60: estimates.
        /// </summary>
        public const float FrameHang = 0.3f, FrameTipPast = 0.085f, FrameBeyondPole = 0.09f, LowerBeyondPole = 0.07f, HangerInOuter = 0.14f, HangerInInner = 0.08f, FramePoleAbove = 0.5f,
            FrameFace = 0.06f, FrameDepth = 0.10f, HangerRadius = 0.03f;

        public const float JointStub = 0.19f, JointPlate = 0.02f;

        /// <summary>A gantry: the longest span of the planar truss (the atlas has it for 10 and 14 m, the space truss for 18 and 26 m), and the half width of the space truss's H legs (HW200 × 200).</summary>
        public const float PlanarGantryMax = 16f, GantryLegHalf = 0.1f;

        /// <summary>
        /// The road's gantry is a space truss (SupportCatalog.GantrySpace) over this span (m; 0.12.78: this mod's rule from the owner's photo, no standard asks for it): an expressway's gantry is wider than one
        /// carriageway of three lanes; under it the portal gantry's planar truss of 22MR601's 10 m design is enough. A span not known (0) is the gantry's default, 14 m.
        /// </summary>
        public const float SpaceGantryFrom = 10f;

        public static bool SpaceGantryFor(float span) => (span > 0f ? span : DefaultSpan) > SpaceGantryFrom;

        /// <summary>The depth of a space truss on a span (m; 0.12.78): 22MR601's 1.6 m from PlanarGantryMax, less under it in a straight line to 1.2 m at MinSpan (this mod's estimate, so that a short span's beam is not a deep wall).</summary>
        public static float SpaceTrussDepth(float span)
        {
            const float shallow = 1.2f, deep = 1.6f;
            return shallow + (deep - shallow) * Math.Max(0f, Math.Min(1f, (span - MinSpan) / (PlanarGantryMax - MinSpan)));
        }

        /// <summary>The radius of the round legs of a space-truss gantry (0.12.78: φ219 as the cantilever's smallest post; the photo's legs are tubes laced at each end, their size is an estimate).</summary>
        public const float SpaceLegRadius = 0.1095f;

        /// <summary>How far the pedestal of a post's foundation reaches beyond the base plate (22MR601 p.81: 700 × 600 under 600 × 500).</summary>
        public const float PedestalMargin = 0.05f;

        /// <summary>
        /// How far below the top of the (tallest) board the axis of a cantilever's arm is: the board's own middle when it is small (the arm is centred on it, up and down), and for a tall
        /// board 0.2 to 0.45 m below its top (a tenth of its height); between 1.0 and 1.8 m high one runs over into the other.
        /// </summary>
        public static float ArmInset(float boardHeight)
        {
            float tall = Math.Max(0.2f, Math.Min(0.45f, 0.1f * boardHeight));
            float t = Math.Max(0f, Math.Min(1f, (boardHeight - 1.0f) / 0.8f));
            return 0.5f * boardHeight * (1f - t) + tall * t;
        }

        /// <summary>
        /// The tube of a roadside post by the board it carries (REPORT.md roadside 2.1, 2.2): on one post φ76 for a board up to 0.7 m wide (S6: triangle 700, circle 600), φ89 up to 1.2 m² (22MR601 p.74,
        /// S5, S10, S11: 800 boards; S8: 1.0 × 1.2 m), φ114 above (p.76: to 1.6 m²); on two or three by the area each post carries, φ89 to 1 m² (S7: 1800 × 800 on two), φ114 to 2.5 m² and φ140 to
        /// 4.5 m² (this mod's steps: nothing read gives these), φ219 to 7 m² (S10: 6 m² a post) and φ273 above (S10: 7.5 m²).
        /// </summary>
        public static float PoleRadiusFor(float boardWidth, float boardHeight, SupportKind kind)
        {
            float area = boardWidth * boardHeight;
            if (kind != SupportKind.DoublePole && kind != SupportKind.TriplePole)
                return boardWidth <= 0.7f ? 0.038f : area <= 1.2f ? 0.0445f : 0.057f;
            float perPost = area / (kind == SupportKind.DoublePole ? 2 : 3);
            return perPost <= 1f ? 0.0445f : perPost <= 2.5f ? 0.057f : perPost <= 4.5f ? 0.07f : perPost <= 7f ? 0.1095f : 0.1365f;
        }

        /// <summary>
        /// Where a board's horizontal channels are on a post (fractions of its height from its middle; 22MR601 pp.136, 141 and the photos, REPORT.md roadside, main difference 2): two on a board under
        /// 0.8 m, else three at the least and one more for every 0.4 m of the height between them (a 1.0 × 1.2 m board four), spread over ±0.38 of the height (two at ±0.3).
        /// </summary>
        public static float[] RailKFor(float height)
        {
            if (height < 0.8f)
                return new[] { -0.3f, 0.3f };
            int n = Math.Max(3, (int)Math.Ceiling((height - 0.2f) / 0.4f - 1e-3f) + 1);
            var k = new float[n];
            for (int i = 0; i < n; i++)
                k[i] = -0.38f + 0.76f * i / (n - 1);
            return k;
        }

        /// <summary>
        /// The top of a roadside post for a board (its middle at centreY): PostAboveRail over the board's top channel and 3 cm under its top, behind it (22MR601 pp.74, 76; REPORT.md roadside, main
        /// difference 5: at or 50 to 150 mm above the top channel, below the board's top edge).
        /// </summary>
        public static float PostTopFor(float centreY, float height)
        {
            float[] k = RailKFor(height);
            return Math.Min(centreY + height * 0.5f - 0.03f, centreY + k[k.Length - 1] * height + PostAboveRail);
        }

        public const float PostAboveRail = 0.1f;

        /// <summary>How far in from each edge of the board a double post's posts stand (a fraction of its width: the mean of five photos, 0.22; REPORT.md roadside 2.2), and at least how far apart.</summary>
        public const float DoublePostInset = 0.22f, DoublePostMinGap = 0.6f;

        /// <summary>The span of a gantry when the road is not known (m), and the longest and the shortest a gantry is built.</summary>
        public const float DefaultSpan = 14f, MinSpan = 6f, MaxSpan = 40f;

        /// <summary>The room between a wall and the back of a board fixed flat on it (m): the spacers.</summary>
        public const float WallGap = 0.02f;

        /// <summary>A board standing on a roof: its back plane (m): the legs of the frame stand behind it, to z = 0.</summary>
        public const float RoofBackZ = 0.05f;

        /// <summary>A wall bracket: the radius of its arm, the room from the wall to the board, beyond the board, and between the arm and the top of the board (m); the knee under the arm (m down the wall).</summary>
        public const float BracketArmRadius = 0.024f, BracketWallGap = 0.20f, BracketTip = 0.06f, BracketHang = 0.05f, BracketKnee = 0.18f;

        /// <summary>How high above the arm the tie of a bracket meets the wall: a third of the arm, 0.2 to 0.45 m.</summary>
        public static float BracketRise(float armLength) => Math.Max(0.2f, Math.Min(0.45f, 0.35f * armLength));

        public static SupportLayout Of(SignDef sign, SupportDef support) => Of(sign, support, 0f);

        /// <summary>
        /// The layout of a board on a support. reach: the span of a gantry or the arm of a cantilever (m), 0 for the support's default; the layout makes it long enough for the board.
        /// </summary>
        public static SupportLayout Of(SignDef sign, SupportDef support, float reach) => Of(sign, support, reach, 0f);

        /// <summary>The shortest and the highest the lower edge of the boards of a hung support is set to by hand (m; 0.12.78, the clearance slider).</summary>
        public const float MinClearance = 3f, MaxClearance = 9f;

        /// <summary>A clearance as it is stored and read back: 0 (the support's own) for none or not a number, else within MinClearance and MaxClearance, in centimetres.</summary>
        public static float ClampClearance(float clearance) => float.IsNaN(clearance) || clearance <= 0f ? 0f : (float)Math.Round(Math.Max(MinClearance, Math.Min(MaxClearance, clearance)), 2);

        /// <summary>As above, with the clearance asked for (m; 0: the support's own): only a hung support takes it, held to MinClearance and MaxClearance; the truss, the arms and the posts follow it.</summary>
        public static SupportLayout Of(SignDef sign, SupportDef support, float reach, float clearance)
        {
            var layout = new SupportLayout { Kind = support.Kind, ArmSide = SupportKinds.Arm(support.Kind) && support.Side < 0 ? -1f : 1f };
            // On a wall the reach is the height of the lower edge above the foot of the wall (0: the sign's own clearance, the roadside 2.3 m).
            bool wall = SupportKinds.OnWall(support.Kind) || SupportKinds.OverRoad(support.Kind);
            // A hung board's reach is its soffit's height; its lower edge follows from it (OverRoad.HungLowerEdge).
            float soffit = support.Hanger ? SoffitOf(reach, sign) : 0f;
            layout.BottomClearance = support.Hanger ? HungBottom(soffit, sign)
                : wall ? (reach > 0f ? reach : sign.BottomClearance)
                : clearance > 0f && SupportKinds.Hung(support.Kind) ? Math.Max(MinClearance, Math.Min(MaxClearance, clearance))
                : support.Clearance > 0f ? support.Clearance : sign.BottomClearance;
            layout.BoardCentreY = layout.BottomClearance + sign.Height * 0.5f;
            layout.BoardTop = layout.BottomClearance + sign.Height;

            layout.RailK = RailKFor(sign.Height);

            if (support.Kind == SupportKind.Gantry)
            {
                // 门架 (22MR601 pp.94-107): to PlanarGantryMax the 10 and 14 m design, a planar truss of three chords φ140 a metre apart on straight φ377 posts, their tops 0.4 m above the top chord;
                // longer the 18 and 26 m design, a space truss 1.6 m deep and 0.5 m (0.75 m over 22 m) from front to back, chords φ95 (φ114), on two H legs at each end laced together, their tops
                // 0.5 m above. The chords are bolted to the posts; there are no knee braces. The span is the road's, the boards Assembly.GantryClear from each post at the least.
                float span = reach > 0f ? reach : support.ArmLength;
                span = Math.Max(Math.Max(span, MinSpan), sign.Width + 2f * Assembly.GantryClear);
                span = Math.Min(span, MaxSpan);
                bool space = support.SpaceTruss || span > PlanarGantryMax;
                layout.ArmLength = span;
                layout.RoundLegs = support.SpaceTruss;
                layout.PoleRadius = support.SpaceTruss ? SpaceLegRadius : space ? GantryLegHalf : 0.1885f;
                layout.BeamDepth = support.SpaceTruss ? SpaceTrussDepth(span) : space ? 1.6f : 2.0f;
                layout.BeamWidth = space ? (span > 22f ? 0.75f : 0.5f) : 0f;
                layout.ChordRadius = space ? (span > 22f ? 0.057f : 0.0475f) : 0.07f;
                layout.PoleX = new[] { 0f, span };
                layout.BoardCentreX = span * 0.5f;
                // The boards stand in front of the truss with their lower edges at the clearance, clamped to the chords that pass behind them (Fig. 7). The truss is as low as the clearance allows (its
                // lower surface at it); under a board taller than the truss and the chords it is raised, its top level with the board's.
                float cover = layout.BeamDepth + 2f * layout.ChordRadius;
                float beamBottom = sign.Height <= cover ? layout.BottomClearance + layout.ChordRadius : layout.BottomClearance + sign.Height - layout.BeamDepth - layout.ChordRadius;

                layout.BoardCentreY = layout.BottomClearance + sign.Height * 0.5f;
                layout.BoardTop = layout.BottomClearance + sign.Height;
                layout.ArmY = beamBottom + layout.BeamDepth * 0.5f;
                layout.ChordYs = space ? new[] { beamBottom, beamBottom + layout.BeamDepth } : new[] { beamBottom, beamBottom + 1f, beamBottom + 2f };
                layout.PoleTop = beamBottom + layout.BeamDepth + (space ? 0.5f : 0.4f);
                layout.HangY = beamBottom;
                layout.HangRadius = layout.ChordRadius;
                layout.BackZ = layout.ChordRadius + ClampRoom;
                layout.Base = support.SpaceTruss ? BasePlate.ArmPost(SpaceLegRadius) : space ? BasePlate.GantryLeg() : BasePlate.Gantry();
                layout.InnerReach = layout.Base.HalfX + PedestalMargin;
                // Small boards on the first post: from the roadside clearance up to under the flange of its lowest chord.
                layout.PostClearance = SupportCatalog.RoadsideClearance;
                layout.PostBackZ = layout.PoleRadius + PlateDepth + RailDepth;
                layout.PostTop = beamBottom - JointFlangeOf(layout) - 0.10f;
                if (support.HeightLimit)
                    LimitFrame(ref layout);
            }
            else if (SupportKinds.Arm(support.Kind))
            {
                // 悬臂 (22MR601 pp.78-92): a straight seamless post sized by the boards' area, straight arms of uniform tube bolted to stubs on it (JointFlangeOf), no brace; one arm (1F) up to 2.3 m²,
                // two 0.9 m apart (2F) up to 5 m², three 1.2 m apart (3F) above; the F type has two at the least. The arms are centred on the board, a single one as ArmInset has it, the post's top
                // ArmPostAbove over the upper one. The boards start BoardFromPost from the post's axis and the arm ends ArmTipInside short of the farthest board's edge, behind it (p.80). A double
                // cantilever is this on both sides of its post, which carries both.
                float area = sign.Width * (sign.Height + sign.ExtraTop);
                bool twin = support.Kind == SupportKind.DoubleCantilever;
                int arms = ArmCount(area, sign.Height, support.Kind == SupportKind.FCantilever, out float spacing);
                layout.PoleRadius = ArmPostRadius(twin ? 2f * area : area, twin);
                layout.ArmRootRadius = layout.ArmTipRadius = area <= 5f ? 0.057f : 0.07f;
                layout.PoleX = new[] { 0f };
                float middle = arms == 1 ? layout.BoardTop - ArmInset(sign.Height) : layout.BoardCentreY;
                layout.ArmYs = new float[arms];
                for (int i = 0; i < arms; i++)
                    layout.ArmYs[i] = middle + spacing * ((arms - 1) * 0.5f - i);
                layout.ArmY = layout.ArmYs[0];
                bool crank = support.Crank && arms == 1;
                layout.CrankRise = crank ? CrankLength * (float)Math.Sin(CrankAngle) : 0f;
                layout.CrankRun = crank ? CrankLength * (float)Math.Cos(CrankAngle) : 0f;
                layout.PoleTop = layout.ArmY - layout.CrankRise + ArmPostAbove;
                layout.HangY = layout.ArmY;
                layout.HangRadius = layout.ArmRootRadius;
                layout.BoardFrom = Math.Max(Assembly.BoardFromPost, crank ? layout.CrankRun + CrankBoardGap : 0f);
                // The arm is the reach that was asked for (0: the support's own), made long enough for the board.
                float length = Math.Max(reach > 0f ? reach : support.ArmLength, layout.BoardFrom + sign.Width - Assembly.ArmTipInside);
                length = Math.Min(length, MaxSpan);
                layout.ArmLength = length;
                layout.BoardCentreX = layout.ArmSide * Math.Max(length + Assembly.ArmTipInside - sign.Width * 0.5f, layout.BoardFrom + sign.Width * 0.5f);
                if (support.Framed)
                {
                    // The boards in frames under the one upper arm, which passes them by FrameTipPast of their width; the lower arm at their middle; the board's plane through the arms' axes.
                    layout.Framed = true;
                    layout.ArmYs = new[] { layout.BoardTop + Math.Max(FrameFace, sign.ExtraTop) + FrameHang };
                    layout.ArmY = layout.HangY = layout.ArmYs[0];
                    layout.LowerArmY = layout.BoardCentreY;
                    layout.CrankRise = layout.CrankRun = 0f;
                    layout.BoardFrom = Assembly.BoardFromPost;
                    length = Math.Min(MaxSpan, Math.Max(reach > 0f ? reach : support.ArmLength, layout.BoardFrom + sign.Width * (1f + FrameTipPast)));
                    layout.ArmLength = length;
                    layout.BoardCentreX = layout.ArmSide * (length - FrameTipPast * sign.Width - sign.Width * 0.5f);
                    layout.PoleTop = layout.ArmY + FramePoleAbove;
                }

                if (support.Octagonal || support.Integrated)
                {
                    layout.OctaArms = true;
                    layout.Integrated = support.Integrated;
                    OctaArm(length, arms, out layout.ArmRootRadius, out layout.ArmTipRadius);
                    if (support.Integrated)
                    {
                        MainPole(length, arms, out layout.PoleRadius, out layout.PoleTopRadius);
                        layout.PoleSides = 12;
                        layout.PoleTop = layout.ArmY + MainPoleAbove(arms);
                    }
                    else
                    {
                        OctaPost(length, area, twin, out layout.PoleRadius, out layout.PoleTopRadius);
                        layout.PoleSides = 8;
                    }

                    layout.HangRadius = layout.ArmRootRadius;
                }

                layout.BackZ = layout.Framed ? FrameDepth * 0.5f - BoardThickness - 0.01f : layout.ArmRootRadius + ClampRoom;
                // The Shanghai pole's flange is under the paving (Jinke Rd photo; Xuzhou buries its plate posts' too): 法兰9 or 法兰10 as 八角锥杆's, not drawn but counted in the room it needs.
                layout.Base = support.Octagonal || support.Integrated ? BasePlate.OctaPost(layout.PoleRadius) : BasePlate.ArmPost(layout.PoleRadius);
                layout.InnerReach = layout.Base.HalfX + PedestalMargin;
                // Small boards on the post: from the roadside clearance up to under the flange of the lowest arm.
                layout.PostClearance = SupportCatalog.RoadsideClearance;
                layout.PostBackZ = layout.PoleRadius + PlateDepth + RailDepth;
                layout.PostTop = layout.Framed ? layout.LowerArmY - 0.2f : layout.ArmYs[layout.ArmYs.Length - 1] - layout.CrankRise - JointFlangeOf(layout) - 0.10f;
            }
            else if (support.Kind == SupportKind.WallFlat)
            {
                // Flat on the wall (the plane z = 0): the back on spacers, nothing beside it and nothing above it.
                layout.PoleRadius = 0f;
                layout.PoleX = new float[0];
                layout.BoardCentreX = 0f;
                layout.BackZ = support.Roof ? RoofBackZ : WallGap;
                layout.Roof = support.Roof;
                layout.PoleTop = layout.BoardTop + sign.ExtraTop;
                layout.InnerReach = sign.Width * 0.5f;
            }
            else if (support.Kind == SupportKind.OverRoad && support.Hanger)
            {
                OverHung(ref layout, sign, soffit);
            }
            else if (support.Kind == SupportKind.OverRoad)
            {
                // Flat on the face of a structure over the road (0.12.81): as on a wall, the back on spacers WallGap in front of the face (z = 0), nothing beside the board and nothing above it. The reach is
                // the height of its lower edge above the road (OverRoad.LowerEdge): 0 gives the sign's own clearance, which the tool never writes.
                layout.PoleRadius = 0f;
                layout.PoleX = new float[0];
                layout.BoardCentreX = 0f;
                layout.BackZ = WallGap;
                layout.PoleTop = layout.BoardTop + sign.ExtraTop;
                layout.InnerReach = sign.Width * 0.5f;
            }
            else if (support.Kind == SupportKind.WallBracket)
            {
                // On a bracket: the arm from the wall (the plane x = 0) along the side's X, the board under it from BracketWallGap out, its plane the arm's (z = 0), the tie's root above the arm.
                float side = support.Side < 0 ? -1f : 1f;
                float length = BracketWallGap + sign.Width + BracketTip;
                layout.PoleRadius = 0f;
                layout.PoleX = new float[0];
                layout.ArmLength = length;
                layout.ArmRootRadius = BracketArmRadius;
                layout.ArmTipRadius = BracketArmRadius;
                layout.ArmY = layout.BoardTop + sign.ExtraTop + BracketHang + BracketArmRadius;
                layout.HangY = layout.ArmY;
                layout.HangRadius = BracketArmRadius;
                layout.KneeX = BracketWallGap * 0.8f;
                layout.BoardCentreX = side * (BracketWallGap + sign.Width * 0.5f);
                layout.PoleTop = layout.ArmY + BracketRise(length);
                layout.BackZ = -BoardThickness * 0.5f;
                layout.InnerReach = 0f;
            }
            else if (support.Kind == SupportKind.AttachedArm)
            {
                // On another mod's signal arm (0.12.51): the origin on the arm's axis, which passes behind the board as a cantilever's does (ArmInset below its top); the board stands in front of it on
                // channels clamped round it. No post, no arm of ours.
                float radius = HostRadiusOf(reach, ArmHostRadius);
                layout.PoleRadius = 0f;
                layout.PoleX = new float[0];
                layout.ArmY = 0f;
                layout.ArmYs = new[] { 0f };
                layout.ArmRootRadius = layout.ArmTipRadius = radius;
                layout.HangY = 0f;
                layout.HangRadius = radius;
                layout.BoardTop = ArmInset(sign.Height);
                layout.BottomClearance = layout.BoardTop - sign.Height;
                layout.BoardCentreY = layout.BottomClearance + sign.Height * 0.5f;
                layout.BoardCentreX = 0f;
                layout.PoleTop = layout.BoardTop + sign.ExtraTop;
                layout.BackZ = radius + ClampRoom;
                layout.InnerReach = 0f;
            }
            else
            {
                layout.PoleRadius = support.Kind == SupportKind.Attached ? HostRadiusOf(reach, HostRadius) : PoleRadiusFor(sign.Width, sign.Height, support.Kind);
                layout.BoardCentreX = 0f;
                if (support.Plate != PlateStyle.None)
                {
                    // A street-name plate post: the post of its style for the plate's type, the plate's lower edge at its style's height (Assembly.ArrangePlate puts it on the head).
                    layout.Plate = support.Plate;
                    layout.Head = PlateHead.Of(support.Plate, PlateHead.Large(sign.Width, sign.Height));
                    layout.PoleRadius = layout.Head.PostRadius;
                    layout.BottomClearance = layout.Head.Clearance;
                    layout.BoardCentreY = layout.BottomClearance + sign.Height * 0.5f;
                    layout.BoardTop = layout.BottomClearance + sign.Height;
                }

                if (support.Kind == SupportKind.DoublePole)
                {
                    float half = Math.Max(sign.Width * (0.5f - DoublePostInset), DoublePostMinGap * 0.5f);
                    layout.PoleX = new[] { -half, half };
                }
                else if (support.Kind == SupportKind.TriplePole)
                {
                    // Three posts: at the middle and 0.3 m in from the edges, at most 3 m apart from one another.
                    float half = Math.Min(Math.Max(sign.Width * 0.5f - 0.3f, 0.6f), 3.0f);
                    layout.PoleX = new[] { -half, 0f, half };
                }
                else
                {
                    layout.PoleX = new[] { 0f };
                }

                layout.PoleTop = PostTopFor(layout.BoardCentreY, sign.Height);
                layout.BackZ = layout.PoleRadius + PlateDepth + RailDepth;
                layout.Base = support.Kind == SupportKind.Attached ? null : BasePlate.Post(layout.PoleRadius);
                layout.InnerReach = Math.Max(sign.Width * 0.5f, layout.PoleX[layout.PoleX.Length - 1] + (layout.Base == null ? 0f : layout.Base.HalfX + PedestalMargin));
            }

            layout.FrontZ = layout.BackZ + BoardThickness;
            return layout;
        }
    }
}
