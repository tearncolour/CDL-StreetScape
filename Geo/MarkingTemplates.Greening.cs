using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The road greening of 0.12.49 (Barriers.Greening, Barriers.Viaduct; scratch/research-greening/REPORT.md): templates of the group "barrier", drawn along a road like the others, listed in a tab of their
    /// own too (the series "green", with the planters of 0.12.32). Their plants are the game's (GreenPlants).
    /// </summary>
    public static partial class MarkingTemplates
    {
        public const string TreePitId = "bar.green.pit", GreenBeltId = "bar.green.belt", ViaductPlanterId = "bar.green.viaduct", FlowerBowlId = "bar.green.bowl";

        /// <summary>The series of the greening tab.</summary>
        public const string GreenSeries = "green";

        private static readonly HashSet<string> kGreenSeries = new HashSet<string> { TreePitId, GreenBeltId, ViaductPlanterId, FlowerBowlId, PlanterRailingId, PlanterBoxId, LawnFenceId, LivingWallId };

        /// <summary>The templates whose items have the game's plants in them (PlantSystem).</summary>
        public static readonly string[] PlantedIds = { PlanterRailingId, PlanterBoxId, TreePitId, GreenBeltId, ViaductPlanterId, FlowerBowlId, GreenNoiseId, HoardingId, LivingWallId };

        public static string GreenSeriesOf(string id) => id != null && kGreenSeries.Contains(id) ? GreenSeries : "";

        /// <summary>
        /// What is greening and no barrier (0.12.107, after the owner's word on the toll things: what belongs to a series alone is not in the barrier tab): the tree pit, the green belt, the viaduct planter and
        /// the flower bowl. The planters with a railing or a box, the lawn fence and the living wall are barriers of a kind, and stay in both tabs.
        /// </summary>
        public static bool GreenOnly(string id) => id == TreePitId || id == GreenBeltId || id == ViaductPlanterId || id == FlowerBowlId;

        private static GuideChoice[] TreeChoices(bool none)
        {
            var list = new List<GuideChoice>();
            if (none)
                list.Add(C("none", "无", "None"));
            foreach (var (species, _, zh, en) in GreenPlants.Trees)
                list.Add(C(species, zh, en));
            return list.ToArray();
        }

        private static readonly GuideChoice[] kKerbStone = { C("g", "花岗岩", "Granite"), C("c", "混凝土", "Concrete") };

        /// <summary>A pit's clear size (cm), or a continuous strip's width ("s" and its width): DB11/T 839-2025 5.1.3 (pits at least 1.2 m), CJJ/T 75-2023 4.3.4 (strips 1.5 m, 2.0 m for surface roots).</summary>
        private static bool Strip(GuideSpec s) => (s.Get("size") ?? "").StartsWith("s", System.StringComparison.Ordinal);

        private static readonly GuideChoice[] kGreenPlants = { C("flower", "开花灌木（月季等）", "Flowering bushes (roses)"), C("shrub", "常绿灌木", "Evergreen shrubs") };

        private static string Species(GuideSpec s, string key) => s.Is(key, "none") ? "" : s.Get(key);

        /// <summary>The line moved away from the traffic by metres (as BarrierRun moves it by the setback): a bed whose kerb is on the line by the roadside has its middle there.</summary>
        private static List<Vec3> Away(GuideSpec s, List<Vec3> line, float metres) => s.Is("where", "m") || !(metres > 0f) ? line : MarkingFills.Offset(line, s.Is("where", "l") ? metres : -metres);

        private static void AddGreening(List<GuideTemplate> list)
        {
            list.Add(T(TreePitId, "CJJ/T 75", "行道树树池", "Street trees in pits", "xdssc", W("行道树", "树池", "树篦子", "树池盖板", "护树架", "连续树池", "人行道", "street tree", "tree pit", "tree grate"),
                new[]
                {
                    GuideField.Choice("size", "树池", "Pits", "120", new[]
                    {
                        C("120", "单株树池 内距 1.2 m", "A pit to each tree, 1.2 m inside"), C("125", "单株树池 内距 1.25 m", "A pit to each tree, 1.25 m inside"), C("150", "单株树池 内距 1.5 m", "A pit to each tree, 1.5 m inside"),
                        C("s150", "连续树池 净宽 1.5 m（种灌木）", "Continuous strip 1.5 m (shrubs)"), C("s200", "连续树池 净宽 2.0 m（浅根乔木）", "Continuous strip 2.0 m (surface roots)")
                    }),
                    GuideField.Choice("kerb", "树池缘石", "Kerb", "0", new[] { C("0", "与人行道齐平", "Level with the footway"), C("10", "高出 10 cm", "10 cm up"), C("15", "高出 15 cm", "15 cm up") }),
                    GuideField.Choice("stone", "缘石材料", "Kerb stone", "g", kKerbStone),
                    GuideField.Choice("cover", "树池覆盖", "Cover", "iron", new[]
                    {
                        C("iron", "铸铁篦子", "Cast-iron grate"), C("resin", "树脂复合篦子（绿）", "Resin grate (green)"), C("resingrey", "树脂复合篦子（灰）", "Resin grate (grey)"),
                        C("frp", "玻璃钢格栅（绿）", "Glass-fibre grating (green)"), C("frpy", "玻璃钢格栅（黄）", "Glass-fibre grating (yellow)"), C("none", "无（裸土）", "None (soil)")
                    }, s => !Strip(s) && s.Is("kerb", "0")),
                    GuideField.Choice("tree", "树种", "Tree", "plane", TreeChoices(false)).In("植物", "Plants"),
                    GuideField.Choice("gap", "株距", "Spacing", "6", new[] { C("4", "4 m（小冠幅）", "4 m (small crowns)"), C("5", "5 m", "5 m"), C("6", "6 m", "6 m"), C("8", "8 m", "8 m") }),
                    GuideField.Flag("guard", "护树架（新栽）", "Tree guards (newly planted)", false, s => !Strip(s)),
                    GuideField.Choice("under", "树间种植", "Between the trees", "shrub", new[] { C("shrub", "灌木", "Shrubs"), C("flower", "开花灌木", "Flowering bushes"), C("none", "无", "None") }, Strip),
                    GuideField.Choice("where", "位置", "Where", "r", kBarrierSide).In("位置", "Place"),
                    GuideField.Digits("off", "树干离线（cm）", "Trunks from the line (cm)", "90", 3, s => !s.Is("where", "m"))
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    var props = new List<PropAnchor>();
                    List<Vec3> line = BarrierRun(s);
                    bool flush = s.Is("kerb", "0");
                    float top = flush ? Barriers.FlushTop : F(s, "kerb", 15f, 10f, 15f) / 100f, spacing = F(s, "gap", 6f, 4f, 8f);
                    SolidColour stone = s.Is("stone", "c") ? SolidColour.Concrete : SolidColour.Granite;
                    if (Strip(s))
                    {
                        var bed = new GreenBedOptions
                        {
                            Width = s.Is("size", "s200") ? 2f : 1.5f, KerbWidth = flush ? 0.10f : 0.12f, KerbTop = top, KerbColour = stone, Species = s.Get("tree"), TreeSpacing = spacing, Spacing = 1.5f,
                            Under = s.Is("under", "none") ? BeltPlants.None : s.Is("under", "flower") ? BeltPlants.Flowers : BeltPlants.Shrubs
                        };
                        return PlanWith(Barriers.GreenBelt(line, bed, Phase(s), Total(s), first, last, props), props);
                    }

                    var pits = new TreePitOptions
                    {
                        Size = F(s, "size", 120f, 120f, 150f) / 100f, KerbTop = top, KerbColour = stone, Species = s.Get("tree"), Spacing = spacing, Guard = s.Flag("guard"),
                        Grate = s.Is("cover", "none") ? GrateKind.None : s.Is("cover", "frp") || s.Is("cover", "frpy") ? GrateKind.Grating : GrateKind.Cast,
                        GrateColour = s.Is("cover", "resin") || s.Is("cover", "frp") ? SolidColour.ResinGreen : s.Is("cover", "resingrey") ? SolidColour.DarkGray : s.Is("cover", "frpy") ? SolidColour.Yellow : SolidColour.CastIron
                    };
                    return PlanWith(Barriers.TreePits(line, pits, Phase(s), props), props);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(GreenBeltId, "CJJ/T 75", "分车绿带（中央分隔带绿化）", "Median and separator green belt", "fcld", W("分车绿带", "中央分隔带", "绿化带", "绿篱", "球形灌木", "防眩", "立缘石", "median", "green belt", "hedge"),
                new[]
                {
                    GuideField.Choice("width", "净宽", "Width", "200", new[] { C("150", "1.5 m", "1.5 m"), C("200", "2.0 m", "2.0 m"), C("250", "2.5 m（主干路）", "2.5 m (arterial)"), C("300", "3.0 m", "3.0 m"), C("400", "4.0 m", "4.0 m") }),
                    GuideField.Choice("edge", "边缘", "Edging", "k15", new[]
                    {
                        C("k15", "立缘石 高出 15 cm", "Kerbs 15 cm up"), C("k20", "立缘石 高出 20 cm", "Kerbs 20 cm up"), C("low", "绿化带侧石 高出 5 cm", "Edging 5 cm up"), C("none", "无（种在原有分隔带上）", "None (on the road's own median)")
                    }),
                    GuideField.Choice("stone", "缘石材料", "Kerb stone", "g", kKerbStone, s => !s.Is("edge", "none")),
                    GuideField.Choice("under", "灌木层", "Shrubs", "hedge", new[] { C("hedge", "修剪绿篱", "Clipped hedge"), C("shrubs", "球形灌木", "Shrub balls"), C("flowers", "开花灌木", "Flowering bushes"), C("none", "无", "None") }).In("植物", "Plants"),
                    GuideField.Choice("gap", "灌木间距", "Shrub spacing", "3", new[] { C("1.5", "1.5 m", "1.5 m"), C("2", "2 m", "2 m"), C("3", "3 m", "3 m"), C("4", "4 m", "4 m"), C("5", "5 m", "5 m") }, s => s.Is("under", "shrubs") || s.Is("under", "flowers")),
                    GuideField.Choice("tree", "乔木", "Trees", "none", TreeChoices(true)),
                    GuideField.Choice("treegap", "乔木株距", "Tree spacing", "8", new[] { C("6", "6 m", "6 m"), C("8", "8 m", "8 m") }, s => !s.Is("tree", "none")),
                    GuideField.Choice("where", "位置", "Where", "m", kBarrierSide).In("位置", "Place"),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    var props = new List<PropAnchor>();
                    bool none = s.Is("edge", "none"), low = s.Is("edge", "low");
                    var bed = new GreenBedOptions
                    {
                        Width = F(s, "width", 200f, 150f, 400f) / 100f, KerbWidth = none ? 0f : low ? 0.08f : 0.15f, KerbTop = low ? 0.05f : s.Is("edge", "k20") ? 0.20f : 0.15f,
                        KerbColour = s.Is("stone", "c") ? SolidColour.Concrete : SolidColour.Granite, Species = Species(s, "tree"), TreeSpacing = F(s, "treegap", 8f, 6f, 8f), Spacing = F(s, "gap", 3f, 1.5f, 5f),
                        Under = s.Is("under", "shrubs") ? BeltPlants.Shrubs : s.Is("under", "flowers") ? BeltPlants.Flowers : s.Is("under", "none") ? BeltPlants.None : BeltPlants.Hedge
                    };
                    List<Vec3> line = Away(s, BarrierRun(s), bed.Width * 0.5f + bed.KerbWidth);
                    return PlanWith(Barriers.GreenBelt(line, bed, Phase(s), Total(s), first, last, props), props);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(ViaductPlanterId, "CJJ/T 75", "高架桥防撞墙花箱", "Planters on a viaduct's crash wall", "gjqfzqhx", W("高架", "高架桥", "防撞墙", "花箱", "桥梁花箱", "挂式花箱", "马鞍式", "月季", "立体绿化", "viaduct", "planter", "bridge"),
                new[]
                {
                    GuideField.Choice("mount", "安装方式", "Mounting", "hung", new[]
                    {
                        C("hung", "外挂式（挂在外侧）", "Hung on the outer face"), C("saddle", "骑跨式（马鞍）", "Astride (saddle)"), C("top", "置顶式", "On the top"), C("trough", "一体种植槽", "A trough cast with the wall")
                    }),
                    GuideField.Choice("length", "花箱长度", "Box length", "100", new[] { C("100", "1.0 m", "1.0 m"), C("120", "1.2 m", "1.2 m"), C("150", "1.5 m", "1.5 m"), C("200", "2.0 m", "2.0 m") }, s => !s.Is("mount", "trough")),
                    GuideField.Choice("box", "花箱颜色", "Box colour", "white", new[] { CWhite, C("green", "青绿", "Teal"), CDark, C("wood", "木纹", "Wood") }, s => !s.Is("mount", "trough")),
                    GuideField.Choice("plants", "植物", "Plants", "flower", new[] { C("flower", "月季（开花灌木）", "Roses (flowering bushes)"), C("shrub", "常绿灌木", "Evergreen shrubs"), C("mixed", "月季与灌木 2:1", "Roses and shrubs 2:1") }),
                    GuideField.Flag("wall", "含混凝土防撞墙", "With the concrete crash wall", true).In("防撞墙", "Crash wall"),
                    GuideField.Choice("height", "墙高", "Wall height", "100", new[] { C("90", "0.9 m", "0.9 m"), C("100", "1.0 m", "1.0 m"), C("110", "1.1 m", "1.1 m"), C("120", "1.2 m", "1.2 m") }),
                    GuideField.Choice("top", "墙顶宽", "Top width", "30", new[] { C("25", "0.25 m", "0.25 m"), C("30", "0.3 m", "0.3 m"), C("40", "0.4 m", "0.4 m"), C("50", "0.5 m", "0.5 m") }),
                    GuideField.Choice("where", "位置", "Where", "r", kBarrierSide).In("位置", "Place"),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    var props = new List<PropAnchor>();
                    var o = new ViaductPlanterOptions
                    {
                        Mount = s.Is("mount", "saddle") ? PlanterMount.Saddle : s.Is("mount", "top") ? PlanterMount.OnTop : s.Is("mount", "trough") ? PlanterMount.Trough : PlanterMount.Hung,
                        Wall = s.Flag("wall"), WallHeight = F(s, "height", 100f, 90f, 120f) / 100f, WallTop = F(s, "top", 30f, 25f, 50f) / 100f, BoxLength = F(s, "length", 100f, 100f, 200f) / 100f,
                        BoxColour = s.Is("box", "green") ? SolidColour.ResinGreen : Colour(s, "box", SolidColour.White), Plants = s.Is("plants", "shrub") ? GreenPlants.Shrub : GreenPlants.Flower, Mixed = s.Is("plants", "mixed")
                    };
                    List<MarkingShape> shapes = Barriers.ViaductPlanters(BarrierRun(s), o, s.Is("where", "l") ? -1 : 1, s.Is("where", "m"), Phase(s), Total(s), first, last, props);
                    return PlanWith(shapes, props);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(FlowerBowlId, "花钵", "人行道花钵", "Footway flower bowls", "rxdhb", W("花钵", "花盆", "花坛", "人行道", "圆形花箱", "石材花钵", "flower bowl", "planter", "urn"),
                new[]
                {
                    GuideField.Choice("style", "样式", "Style", "stone", new[] { C("stone", "石材花钵", "Stone bowl"), C("round", "圆形花箱（GRC）", "Round planter (GRC)") }),
                    GuideField.Choice("size", "直径", "Diameter", "100", new[] { C("80", "0.8 m", "0.8 m"), C("100", "1.0 m", "1.0 m"), C("120", "1.2 m", "1.2 m") }),
                    GuideField.Choice("colour", "材料", "Material", "granite", new[] { C("granite", "花岗岩", "Granite"), CWhite, C("concrete", "混凝土", "Concrete") }),
                    GuideField.Choice("plants", "植物", "Plants", "flower", kGreenPlants),
                    GuideField.Choice("gap", "间距", "Spacing", "5", new[] { C("3", "3 m", "3 m"), C("5", "5 m", "5 m"), C("8", "8 m", "8 m"), C("10", "10 m", "10 m"), C("15", "15 m", "15 m") }),
                    GuideField.Choice("where", "位置", "Where", "r", kBarrierSide).In("位置", "Place"),
                    Setback
                },
                s =>
                {
                    var props = new List<PropAnchor>();
                    List<MarkingShape> shapes = Barriers.FlowerBowls(BarrierRun(s), s.Is("style", "round") ? BowlStyle.Round : BowlStyle.Stone, F(s, "size", 100f, 80f, 120f) / 100f, Colour(s, "colour", SolidColour.Granite),
                        s.Is("plants", "shrub") ? GreenPlants.Shrub : GreenPlants.Flower, F(s, "gap", 5f, 3f, 15f), Phase(s), props);
                    return PlanWith(shapes, props);
                },
                true,
                null,
                BarrierGroup));
        }
    }
}
