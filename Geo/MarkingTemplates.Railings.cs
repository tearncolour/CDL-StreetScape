using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The railings and fences of 0.12.34 (Barriers.Railings, scratch/research-railings/REPORT.md): templates of the group "barrier", placed like the others along a road.</summary>
    public static partial class MarkingTemplates
    {
        public const string BeijingFenceId = "bar.fence.bj", ZincFenceId = "bar.fence.zinc", CrowdBarrierId = "bar.crowd", BridgeRailingId = "bar.bridgerail", StoneBalustradeId = "bar.stone",
            ExpresswayFenceId = "bar.fence.mesh", AntiThrowId = "bar.antithrow", StoneBallId = "bar.stoneball", FauxWoodId = "bar.fauxwood", PalisadeId = "bar.palisade", EdgeGuardId = "bar.edgeguard";

        private static void AddRailings(List<GuideTemplate> list)
        {
            list.Add(T(BeijingFenceId, "京式", "京式护栏（乙型）", "Beijing-style railing", "jshl", W("京式", "乙型", "倒U", "n型", "中央隔离", "隔离栏", "护栏", "railing", "beijing", "median fence"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "120", new[] { C("70", "0.7 m", "0.7 m"), C("110", "1.1 m", "1.1 m"), C("120", "1.2 m", "1.2 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "white", new[] { CWhite, CMetal, C("yellow", "黄色", "Yellow") }),
                    GuideField.Choice("base", "底座", "Bases", "yellow", new[] { C("yellow", "黄色铸铁", "Yellow cast iron"), C("black", "黑色", "Black"), C("concrete", "水泥", "Concrete") }),
                    GuideField.Choice("where", "位置", "Where", "m", kBarrierSide),
                    Setback,
                    kGapField, kGapLengthField, kGapRampField, kGapPostField
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    List<Vec3> line = BarrierRun(s);
                    List<MarkingShape> shapes = Barriers.BeijingFence(line, F(s, "height", 120f, 70f, 120f) / 100f, Colour(s, "colour", SolidColour.White), Colour(s, "base", SolidColour.Yellow), Taper(s), Phase(s), Total(s), first, last);
                    AddGapPosts(s, line, first, last, shapes);
                    return Plan(shapes);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(ZincFenceId, "锌钢护栏", "蓝白锌钢隔离栏", "Blue and white zinc-steel railing", "lbxghl", W("锌钢", "蓝白", "市政护栏", "交通护栏", "隔离栏", "中央隔离", "zinc steel", "median fence", "blue"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "120", new[] { C("60", "0.6 m", "0.6 m"), C("80", "0.8 m", "0.8 m"), C("100", "1.0 m", "1.0 m"), C("120", "1.2 m（扶手 1.1 m）", "1.2 m (rail 1.1 m)") }),
                    GuideField.Choice("where", "位置", "Where", "m", kBarrierSide),
                    Setback,
                    kGapField, kGapLengthField, kGapRampField, kGapPostField
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    List<Vec3> line = BarrierRun(s);
                    List<MarkingShape> shapes = Barriers.ZincSteelFence(line, F(s, "height", 120f, 60f, 120f) / 100f, Taper(s), Phase(s), Total(s), first, last);
                    AddGapPosts(s, line, first, last, shapes);
                    return Plan(shapes);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(CrowdBarrierId, "铁马", "铁马（移动护栏）", "Crowd barriers", "tm", W("铁马", "移动护栏", "临时护栏", "施工", "围栏", "crowd barrier", "temporary", "road works"),
                new[]
                {
                    GuideField.Choice("style", "样式", "Style", "yb", new[] { C("yb", "黑黄（施工）", "Black and yellow (road works)"), C("rw", "红白", "Red and white"), C("gal", "镀锌（活动）", "Galvanised (events)") }),
                    GuideField.Choice("size", "规格", "Size", "big", new[] { C("big", "2.0×1.2 m", "2.0 x 1.2 m"), C("small", "1.5×1.0 m", "1.5 x 1.0 m") }),
                    GuideField.Choice("where", "位置", "Where", "r", kBarrierSide),
                    Setback
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    CrowdScheme scheme = s.Is("style", "gal") ? CrowdScheme.Galvanised : s.Is("style", "rw") ? CrowdScheme.RedWhite : CrowdScheme.BlackYellow;
                    return Plan(Barriers.CrowdBarriers(BarrierRun(s), !s.Is("size", "small"), scheme, Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(BridgeRailingId, "CJJ 11", "不锈钢复合管桥梁栏杆", "Stainless tube bridge railing", "qllg", W("桥梁栏杆", "不锈钢", "复合管", "人行道栏杆", "桥", "bridge railing", "stainless", "parapet"),
                new[]
                {
                    GuideField.Choice("height", "高度（自人行道面）", "Height (from the footway)", "120", new[] { C("110", "1.1 m（人行道）", "1.1 m (footway)"), C("120", "1.2 m", "1.2 m"), C("140", "1.4 m（非机动车道旁）", "1.4 m (by a cycle track)") }),
                    GuideField.Choice("colour", "立柱颜色", "Post colour", "blue", new[] { C("blue", "蓝色", "Blue"), CSteel, C("green", "绿色", "Green"), CWhite }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.BridgeRailing(BarrierRun(s), F(s, "height", 120f, 110f, 140f) / 100f, Colour(s, "colour", SolidColour.Blue), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(StoneBalustradeId, "石栏杆", "石材栏杆（望柱栏板）", "Stone balustrade", "sclg", W("石栏杆", "石材", "望柱", "栏板", "汉白玉", "花岗岩", "桥", "河边", "stone", "balustrade", "granite"),
                new[]
                {
                    GuideField.Choice("height", "栏板高度", "Height", "110", new[] { C("90", "0.9 m", "0.9 m"), C("105", "1.05 m（公园）", "1.05 m (park)"), C("110", "1.1 m（桥、人行道）", "1.1 m (bridge, footway)") }),
                    GuideField.Choice("colour", "石材", "Stone", "granite", new[] { C("granite", "花岗岩（芝麻灰）", "Grey granite"), C("white", "汉白玉", "White marble") }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.StoneBalustrade(BarrierRun(s), F(s, "height", 110f, 90f, 110f) / 100f, Colour(s, "colour", SolidColour.Granite), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(ExpresswayFenceId, "JTG D81", "公路隔离栅（钢丝网）", "Expressway boundary fence", "glglz", W("隔离栅", "隔离网", "护栏网", "钢丝网", "高速", "铁丝网", "fence", "boundary fence", "wire mesh", "expressway"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "180", new[] { C("150", "1.5 m（最低）", "1.5 m (the least)"), C("180", "1.8 m（城镇附近）", "1.8 m (near towns)"), C("200", "2.0 m", "2.0 m") }),
                    GuideField.Flag("arm", "顶部刺铁丝", "Barbed wire on top", false),
                    GuideField.Choice("colour", "颜色", "Colour", "green", new[] { C("green", "绿色（浸塑）", "Green (coated)"), CMetal, CDark }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.ExpresswayFence(BarrierRun(s), F(s, "height", 180f, 150f, 200f) / 100f, s.Flag("arm"), s.Is("where", "l") ? 1 : -1, Colour(s, "colour", SolidColour.Green), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(AntiThrowId, "GB 50688", "桥梁防落物网", "Anti-throw screen", "qlflwy", W("防落物网", "防抛网", "防护网", "桥", "跨线桥", "anti-throw", "screen", "overpass"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "200", new[] { C("180", "1.8 m（公路，自护栏顶）", "1.8 m (highway, over the parapet)"), C("200", "2.0 m（城市，自人行道面）", "2.0 m (city, from the footway)"), C("250", "2.5 m", "2.5 m") }),
                    GuideField.Choice("wall", "下部", "Under it", "0", new[] { C("0", "直接立在桥面/人行道", "On the deck or the footway"), C("100", "1.0 m 混凝土墙", "A concrete wall 1.0 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "green", new[] { C("green", "绿色", "Green"), CMetal, C("blue", "蓝色", "Blue") }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.AntiThrowScreen(BarrierRun(s), F(s, "height", 200f, 180f, 250f) / 100f, F(s, "wall", 0f, 0f, 100f) / 100f, Colour(s, "colour", SolidColour.Green), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(StoneBallId, "石球", "隔离石球", "Stone balls", "glsq", W("石球", "挡车石", "隔离石球", "路障", "花岗岩", "stone ball", "bollard", "granite"),
                new[]
                {
                    GuideField.Choice("size", "直径", "Diameter", "50", new[] { C("40", "0.4 m", "0.4 m"), C("50", "0.5 m", "0.5 m"), C("60", "0.6 m", "0.6 m") }),
                    GuideField.Digits("gap", "间距（m）", "Spacing (m)", "1.5", 4),
                    GuideField.Choice("colour", "石材", "Stone", "granite", new[] { C("granite", "花岗岩（芝麻灰）", "Grey granite"), C("white", "汉白玉", "White marble") })
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    return Plan(Barriers.StoneBalls(BarrierRun(s), F(s, "size", 50f, 40f, 60f) / 100f, F(s, "gap", 1.5f, 0.5f, 20f), Colour(s, "colour", SolidColour.Granite), Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(FauxWoodId, "仿木", "仿木栏杆", "Faux log railing", "fmlg", W("仿木", "木栏杆", "栅栏", "公园", "景区", "栏杆", "log", "faux wood", "park"),
                new[]
                {
                    GuideField.Choice("style", "样式", "Style", "log", new[] { C("log", "矮栏（两道横木）", "Low (two rails)"), C("platform", "观景台（立柱、扶手、方栏杆）", "Platform (posts, rail, balusters)") }),
                    GuideField.Choice("height", "高度", "Height", "80", new[] { C("60", "0.6 m", "0.6 m"), C("80", "0.8 m", "0.8 m"), C("105", "1.05 m（公园护栏）", "1.05 m (park)"), C("120", "1.2 m", "1.2 m") }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.FauxWood(BarrierRun(s), F(s, "height", 80f, 60f, 120f) / 100f, s.Is("style", "platform"), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(PalisadeId, "围墙护栏", "锌钢围墙护栏", "Spear-top palisade", "xgwqhl", W("围墙护栏", "围栏", "锌钢", "小区", "学校", "栅栏", "palisade", "fence", "railings"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "180", new[] { C("120", "1.2 m", "1.2 m"), C("150", "1.5 m", "1.5 m"), C("180", "1.8 m", "1.8 m"), C("200", "2.0 m", "2.0 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "black", new[] { C("black", "黑色", "Black"), CWhite, C("green", "绿色", "Green"), CDark }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.Palisade(BarrierRun(s), F(s, "height", 180f, 120f, 200f) / 100f, Colour(s, "colour", SolidColour.Black), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(EdgeGuardId, "JGJ 80", "临边防护栏（施工）", "Works edge guard", "lbfhl", W("临边防护", "基坑护栏", "施工护栏", "防护栏", "红白", "edge guard", "excavation", "road works"),
                new[]
                {
                    GuideField.Choice("colour", "颜色", "Colour", "rw", new[] { C("rw", "红白", "Red and white"), C("yb", "黄黑", "Yellow and black") }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.EdgeGuard(BarrierRun(s), !s.Is("colour", "yb"), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
        }
    }
}
