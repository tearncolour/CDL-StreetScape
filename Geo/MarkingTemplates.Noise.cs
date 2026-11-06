using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The noise barriers of 0.12.49 (Barriers.Noise, scratch/research-barriers/REPORT.md): templates of the group "barrier", placed like the others along a road or a viaduct.</summary>
    public static partial class MarkingTemplates
    {
        public const string NoiseBarrierId = "bar.noise", NoiseCoverId = "bar.noise.cover", CementNoiseId = "bar.noise.cement", BrickNoiseId = "bar.noise.brick", GreenNoiseId = "bar.noise.green";

        private static readonly GuideChoice[] kNoisePanel = { C("grey", "银灰", "Silver grey"), C("green", "绿色", "Green"), C("blue", "蓝色", "Blue"), C("white", "白色", "White"), CDark };

        private static readonly GuideChoice[] kNoiseGlass = { C("cl", "透明", "Clear"), C("bl", "淡蓝", "Blue tint"), C("gr", "淡绿", "Green tint") };

        private static readonly GuideChoice[] kNoisePost = { CMetal, C("green", "绿色", "Green"), C("blue", "蓝色", "Blue"), CDark };

        private static readonly GuideChoice[] kNoiseBand =
            { C("none", "无", "None"), C("mid", "中部 1 m", "1 m half way up"), C("wide", "中部 2 m", "2 m half way up"), C("upper", "1 m 以上至顶板", "From 1 m to under the top"), C("all", "除底板外全透明", "All but the lowest module") };

        private static GuideField NoiseBaseField(string def) => GuideField.Choice("base", "基础", "Under it", def, new[]
        {
            C("pl", "混凝土条基（路基）", "A concrete footing (on the ground)"), C("w50", "混凝土矮墙 0.5 m", "A concrete wall 0.5 m"), C("w100", "混凝土矮墙 1.0 m", "A concrete wall 1.0 m"),
            C("c81", "桥梁防撞墙 0.81 m（F 型）", "A bridge's crash wall 0.81 m (F shape)"), C("c100", "桥梁防撞墙 1.0 m（F 型）", "A bridge's crash wall 1.0 m (F shape)")
        });

        private static SolidColour PanelColour(GuideSpec s) =>
            s.Is("pn", "green") ? SolidColour.Green : s.Is("pn", "blue") ? SolidColour.Blue : s.Is("pn", "white") ? SolidColour.White : s.Is("pn", "dark") ? SolidColour.DarkGray : SolidColour.LightGray;

        /// <summary>A noise barrier's settings as its builders take them; height the panels' (cm) by its key and default.</summary>
        private static NoiseStyle Noise(GuideSpec s, float defaultHeight, float lowest, float highest, float defaultSpacing)
        {
            string base_ = s.Get("base");
            return new NoiseStyle
            {
                Height = F(s, "height", defaultHeight, lowest, highest) / 100f,
                Spacing = F(s, "post", defaultSpacing, 2f, 4f),
                Top = s.Is("top", "crank") ? NoiseTop.Cranked : s.Is("top", "arc") ? NoiseTop.Arc : NoiseTop.Upright,
                Base = base_.StartsWith("c", System.StringComparison.Ordinal) ? NoiseBase.CrashWall : base_.StartsWith("w", System.StringComparison.Ordinal) ? NoiseBase.Wall : NoiseBase.Plinth,
                BaseHeight = base_ == "c100" || base_ == "w100" ? 1f : base_ == "c81" ? 0.81f : 0.5f,
                Band = s.Is("band", "mid") ? GlassBand.Middle : s.Is("band", "wide") ? GlassBand.Wide : s.Is("band", "upper") ? GlassBand.Upper : s.Is("band", "all") ? GlassBand.All : GlassBand.None,
                Panel = PanelColour(s),
                Glass = s.Is("gl", "bl") ? SolidColour.GlassBlue : s.Is("gl", "gr") ? SolidColour.GlassGreen : SolidColour.Glass,
                Post = Colour(s, "pc", SolidColour.Metal),
                Level = t_Level
            };
        }

        private static void AddNoiseBarriers(List<GuideTemplate> list)
        {
            list.Add(T(NoiseBarrierId, "声屏障", "金属声屏障（直立、折角、弧形）", "Metal noise barrier", "jsspz", W("声屏障", "隔音屏", "隔音墙", "降噪", "吸声板", "百叶", "高架", "桥梁", "折角", "弧形", "noise barrier", "sound wall", "acoustic"),
                new[]
                {
                    GuideField.Choice("height", "屏体高度（直立部分）", "Panel height (upright part)", "300", new[] { C("200", "2.0 m", "2.0 m"), C("250", "2.5 m", "2.5 m"), C("300", "3.0 m", "3.0 m"), C("350", "3.5 m", "3.5 m"), C("400", "4.0 m", "4.0 m"), C("500", "5.0 m", "5.0 m") }),
                    GuideField.Choice("top", "顶部", "Top", "up", new[] { C("up", "直立型", "Upright"), C("crank", "折角型（向车道折 30°、1 m）", "Cranked (1 m, 30° toward the road)"), C("arc", "弧形（向车道弯 45°、1.5 m）", "Arched (1.5 m bending 45° toward the road)") }),
                    GuideField.Choice("band", "透明隔声板", "Transparent panels", "mid", kNoiseBand),
                    GuideField.Choice("pn", "吸声板颜色", "Panel colour", "grey", kNoisePanel),
                    GuideField.Choice("gl", "透明板", "Sheet", "cl", kNoiseGlass, s => !s.Is("band", "none")),
                    GuideField.Choice("pc", "立柱颜色", "Post colour", "metal", kNoisePost),
                    NoiseBaseField("c81"),
                    GuideField.Choice("post", "立柱间距", "Post spacing", "2", new[] { C("2", "2.0 m（板长 1960 mm）", "2.0 m (1960 mm panels)"), C("2.5", "2.5 m（板长 2460 mm）", "2.5 m (2460 mm panels)") }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.NoiseBarrier(BarrierRun(s), Noise(s, 300f, 150f, 500f, 2f), s.Is("where", "l") ? -1 : 1, Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(NoiseCoverId, "声屏障", "半封闭、全封闭声屏障（高架）", "Semi- and fully enclosed noise barrier", "fbspz", W("全封闭", "半封闭", "声屏障", "隔声罩", "隔音棚", "高架", "拱形", "悬臂", "enclosure", "noise tunnel", "noise barrier", "viaduct"),
                new[]
                {
                    GuideField.Choice("form", "形式", "Kind", "full", new[] { C("semi", "半封闭（悬臂伸向车道）", "Semi-enclosed (a cantilevered roof)"), C("full", "全封闭（沿道路中线画）", "Fully enclosed (along the road's middle)") }),
                    GuideField.Choice("span", "两侧屏体间距", "Between the walls", "12", new[] { C("9", "9 m", "9 m"), C("12", "12 m", "12 m"), C("16", "16 m", "16 m"), C("20", "20 m", "20 m"), C("26", "26 m", "26 m") }, s => s.Is("form", "full")),
                    GuideField.Choice("arm", "悬臂伸出", "Roof reach", "4", new[] { C("3", "3 m", "3 m"), C("4", "4 m", "4 m"), C("6", "6 m", "6 m"), C("8", "8 m", "8 m") }, s => s.Is("form", "semi")),
                    GuideField.Choice("height", "侧墙高度", "Wall height", "400", new[] { C("300", "3.0 m", "3.0 m"), C("400", "4.0 m", "4.0 m"), C("500", "5.0 m", "5.0 m") }),
                    GuideField.Choice("band", "侧墙透明板", "Transparent wall panels", "wide", kNoiseBand),
                    GuideField.Choice("pn", "吸声板颜色", "Panel colour", "grey", kNoisePanel),
                    GuideField.Choice("gl", "顶棚及透明板", "Roof and sheets", "cl", kNoiseGlass),
                    GuideField.Choice("pc", "钢结构颜色", "Steel colour", "metal", kNoisePost),
                    NoiseBaseField("c81"),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly, s => s.Is("form", "semi")),
                    GuideField.Digits("off", "外移（cm，离开车流）", "Setback (cm, from the traffic)", "0", 3, s => s.Is("form", "semi"))
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    bool full = s.Is("form", "full");
                    List<Vec3> line = full ? Run(s, 8f) : BarrierRun(s);
                    return Plan(Barriers.NoiseCoverBarrier(line, full ? NoiseCover.Full : NoiseCover.Semi, Noise(s, 400f, 300f, 500f, 2f), F(s, "arm", 4f, 3f, 8f), F(s, "span", 12f, 9f, 26f),
                        s.Is("where", "l") ? -1 : 1, Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(CementNoiseId, "声屏障", "水泥声屏障", "Cement noise barrier", "snspz", W("水泥声屏障", "混凝土声屏障", "轻质水泥", "声屏障", "隔音墙", "铁路", "cement", "concrete", "noise barrier"),
                new[]
                {
                    GuideField.Choice("height", "屏体高度", "Panel height", "240", new[] { C("180", "1.8 m（3 块）", "1.8 m (3 panels)"), C("240", "2.4 m（4 块）", "2.4 m (4 panels)"), C("300", "3.0 m（5 块）", "3.0 m (5 panels)"), C("360", "3.6 m（6 块）", "3.6 m (6 panels)") }),
                    GuideField.Choice("band", "透明隔声板", "Transparent panels", "none", new[] { C("none", "无", "None"), C("upper", "上部", "The upper ones") }),
                    GuideField.Choice("gl", "透明板", "Sheet", "cl", kNoiseGlass, s => !s.Is("band", "none")),
                    NoiseBaseField("pl"),
                    GuideField.Choice("post", "立柱间距", "Post spacing", "4", new[] { C("2", "2.0 m", "2.0 m"), C("4", "4.0 m（板长 4 m）", "4.0 m (4 m panels)") }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    NoiseStyle style = Noise(s, 240f, 180f, 360f, 4f);
                    style.Post = SolidColour.Metal;
                    return Plan(Barriers.SoundWallBarrier(BarrierRun(s), SoundWall.Cement, style, 0f, SolidColour.Brick, s.Is("where", "l") ? -1 : 1, Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(BrickNoiseId, "声屏障", "砖墙组合式声屏障", "Brick and panel sound wall", "zqzhsspz", W("砖墙", "砖砌", "隔音墙", "声屏障", "组合式", "围墙", "brick", "sound wall", "noise barrier"),
                new[]
                {
                    GuideField.Choice("lower", "砖墙高度", "Brick wall height", "120", new[] { C("80", "0.8 m", "0.8 m"), C("120", "1.2 m", "1.2 m"), C("160", "1.6 m", "1.6 m") }),
                    GuideField.Choice("height", "总高", "Height", "300", new[] { C("250", "2.5 m", "2.5 m"), C("300", "3.0 m", "3.0 m"), C("350", "3.5 m", "3.5 m"), C("400", "4.0 m", "4.0 m") }),
                    GuideField.Choice("brick", "砖", "Bricks", "red", new[] { C("red", "红砖", "Red brick"), C("grey", "青砖", "Grey brick") }),
                    GuideField.Choice("band", "透明隔声板", "Transparent panels", "none", kNoiseBand),
                    GuideField.Choice("pn", "吸声板颜色", "Panel colour", "grey", kNoisePanel),
                    GuideField.Choice("gl", "透明板", "Sheet", "cl", kNoiseGlass, s => !s.Is("band", "none")),
                    GuideField.Choice("pc", "立柱颜色", "Post colour", "metal", kNoisePost),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    NoiseStyle style = Noise(s, 300f, 250f, 400f, 2.5f);
                    style.Spacing = 2.5f;
                    return Plan(Barriers.SoundWallBarrier(BarrierRun(s), SoundWall.Brick, style, F(s, "lower", 120f, 80f, 160f) / 100f, s.Is("brick", "grey") ? SolidColour.GreyBrick : SolidColour.Brick,
                        s.Is("where", "l") ? -1 : 1, Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(GreenNoiseId, "声屏障", "绿化声屏障（墙前种植池）", "Noise barrier with a planting bed", "lhspz", W("绿化声屏障", "生态声屏障", "植物声屏障", "绿化", "种植池", "花池", "声屏障", "green", "planted", "noise barrier", "planting bed"),
                new[]
                {
                    GuideField.Choice("height", "屏体高度", "Panel height", "300", new[] { C("200", "2.0 m", "2.0 m"), C("300", "3.0 m", "3.0 m"), C("400", "4.0 m", "4.0 m") }),
                    GuideField.Choice("bed", "种植池", "Planting bed", "road", new[] { C("road", "朝车道一侧", "On the road side"), C("back", "背向车道一侧", "On the far side") }),
                    GuideField.Choice("pn", "吸声板颜色", "Panel colour", "green", kNoisePanel),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    var props = new List<PropAnchor>();
                    int side = s.Is("where", "l") ? -1 : 1;
                    List<MarkingShape> shapes = Barriers.GreenNoiseBarrier(BarrierRun(s), Noise(s, 300f, 200f, 400f, 2f), side, s.Is("bed", "back") ? -side : side, Phase(s), first, last, props);
                    return PlanWith(shapes, props);
                },
                true,
                null,
                BarrierGroup));
        }
    }
}
