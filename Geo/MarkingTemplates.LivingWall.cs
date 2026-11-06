using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The vertical greening wall (垂直绿化墙, 花墙) of a barrier line (Barriers.LivingWall): a template of the group "barrier", listed in the greening tab, its plants the game's (GreenPlants).</summary>
    public static partial class MarkingTemplates
    {
        public const string LivingWallId = "bar.livingwall";

        /// <summary>The templates that have levels of detail (BarrierLod) without being seen from the horizon (LongReachIds): a wall of 3 m is a speck at 3 km.</summary>
        public static readonly string[] LeveledIds = { LivingWallId };

        private static void AddLivingWall(List<GuideTemplate> list)
        {
            list.Add(T(LivingWallId, "垂直绿化", "垂直绿化墙（花墙）", "Vertical greening wall", "czlhq", W("花墙", "垂直绿化", "绿化墙", "立体绿化", "植物墙", "花架", "攀爬", "围墙", "living wall", "green wall", "vertical garden", "trellis"),
                new[]
                {
                    GuideField.Choice("height", "墙高", "Wall height", "250", new[] { C("150", "1.5 m", "1.5 m"), C("200", "2.0 m", "2.0 m"), C("250", "2.5 m", "2.5 m"), C("300", "3.0 m", "3.0 m") }),
                    GuideField.Choice("style", "样式", "Style", "f", new[]
                    {
                        C("f", "模块平贴（花槽层层相叠）", "Flush modules (troughs stacked)"), C("s", "阶梯花槽（逐层后退）", "Stepped terraces"), C("t", "格栅攀爬（脚槽加钢格栅）", "Trellis (a foot trough and a steel trellis)")
                    }),
                    GuideField.Choice("thick", "厚度", "Thickness", "45", new[] { C("30", "0.3 m", "0.3 m"), C("45", "0.45 m", "0.45 m"), C("60", "0.6 m", "0.6 m") }),
                    GuideField.Choice("gap", "花槽间距", "Trough spacing", "60", new[] { C("50", "0.5 m", "0.5 m"), C("60", "0.6 m", "0.6 m"), C("70", "0.7 m", "0.7 m") }, s => !s.Is("style", "t")),
                    GuideField.Choice("post", "立柱间距", "Post spacing", "2.5", new[] { C("2", "2.0 m", "2.0 m"), C("2.5", "2.5 m", "2.5 m"), C("3", "3.0 m", "3.0 m") }),
                    GuideField.Choice("faces", "绿化面", "Planted faces", "1", new[] { C("1", "单面（朝向车流）", "One face (toward the traffic)"), C("2", "双面", "Both faces") }, s => !s.Is("where", "m")),
                    GuideField.Choice("bloom", "花色", "Flower colour", "m", new[]
                    {
                        C("m", "红粉黄白混色", "Red, pink, yellow and white mixed"), C("r", "红色", "Red"), C("p", "粉色", "Pink"), C("y", "黄色", "Yellow"), C("w", "白色", "White"), C("l", "仅绿叶", "Leaves only")
                    }).In("植物", "Plants"),
                    GuideField.Choice("plants", "脚槽植物", "Foot trough", "f", new[] { C("f", "月季（开花灌木）", "Roses (flowering bushes)"), C("s", "常绿灌木", "Evergreen shrubs"), C("m", "月季与灌木 2:1", "Roses and shrubs 2:1") }).In("植物", "Plants"),
                    GuideField.Flag("top", "墙顶花盆", "Flower pots on the top", true).In("植物", "Plants"),
                    GuideField.Choice("where", "位置", "Where", "r", kBarrierSide).In("位置", "Place"),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    var props = new List<PropAnchor>();
                    var o = new LivingWallOptions
                    {
                        Height = F(s, "height", 250f, 150f, 300f) / 100f, Thickness = F(s, "thick", 45f, 30f, 60f) / 100f, TroughPitch = F(s, "gap", 60f, 50f, 70f) / 100f, PostSpacing = F(s, "post", 2.5f, 2f, 3f),
                        Style = s.Is("style", "s") ? WallStyle.Stepped : s.Is("style", "t") ? WallStyle.Trellis : WallStyle.Flush,
                        BothFaces = s.Is("where", "m") || s.Is("faces", "2"), Mixed = s.Is("plants", "m"), TopPlants = s.Flag("top"), Skin = WallFoliage.OfChoice(s.Get("bloom")) ?? SolidColour.Green, FootPlants = s.Is("plants", "s") ? GreenPlants.Shrub : GreenPlants.Flower, Level = t_Level
                    };
                    List<MarkingShape> shapes = Barriers.LivingWall(BarrierRun(s), o, s.Is("where", "l") ? -1 : 1, Phase(s), Total(s), first, last, props);
                    return PlanWith(shapes, props);
                },
                true,
                null,
                BarrierGroup));
        }
    }
}
