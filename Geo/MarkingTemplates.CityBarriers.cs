using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The city railings and barriers of 0.12.32 (Barriers.City): templates of the group "barrier", placed like the others along a road.</summary>
    public static partial class MarkingTemplates
    {
        public const string HongKongFenceId = "bar.fence.hk", PedestrianRailingId = "bar.fence.ped", LowRailingId = "bar.fence.low", LawnFenceId = "bar.lawn", FlexPostId = "bar.flexpost",
            PlanterRailingId = "bar.planter", PlanterBoxId = "bar.planterbox", CombinedId = "bar.combo", BeamAndPostId = "bar.beampost", MovableId = "bar.movable";

        /// <summary>
        /// The ends of a median railing that come down at a gap pedestrians cross (GB 50688 7.6.1-1), over how many metres, and whether a separating post stands in the gap. A barrier drawn along a
        /// road is one piece for each stretch between two junctions, so its two ends are the junctions: "两端" is a railing that comes down on its way into every junction (0.12.106: the owner, "城市道路
        /// 栏杆加入一个快到路口时候高度降低的可选项": it was there, as "行人断口渐降", and not found). The field's own default stays "none": a railing placed before 0.12.32 has no gap in its settings, and a
        /// missing key is the default, so it must read as it always did; a new draft of these railings starts with "两端" (StartSettings).
        /// </summary>
        private static readonly GuideField kGapField = GuideField.Choice("gap", "路口前降至 0.7 m", "Lowered to 0.7 m at a junction", "n",
            new[] { C("n", "无", "None"), C("s", "起点", "At the start"), C("e", "终点", "At the end"), C("b", "两端", "At both ends") });

        /// <summary>
        /// How long the railing stays at 0.70 m at an end (GA/T 1567-2019 4.2.6: 10 m at least; GB 50688 7.6.1-1 for a gap pedestrians cross: the stopping sight distance, 20 to 70 m at 20 to 60 km/h). The
        /// field's default stays 40, as the entries' default texts are pinned to what they were; a new draft starts with 10 (StartSettings).
        /// </summary>
        private static readonly GuideField kGapLengthField = GuideField.Choice("glen", "低段长度（≥10 m）", "Low section (10 m or more)", "40",
            new[] { C("10", "10 m（GA/T 1567）", "10 m (GA/T 1567)"), C("20", "20 m（20 km/h）", "20 m (20 km/h)"), C("30", "30 m（30 km/h）", "30 m (30 km/h)"), C("40", "40 m（40 km/h）", "40 m (40 km/h)"), C("60", "60 m（50 km/h）", "60 m (50 km/h)"), C("70", "70 m（60 km/h）", "70 m (60 km/h)") },
            s => !s.Is("gap", "n"));

        /// <summary>
        /// How long the ramp from the low end up to the railing's height is (0.12.106): 15 m, the least GA/T 1567-2019 4.2.6 allows, a quick drop (the owner, after the 20 to 70 m slope it had: "不是很慢的降低，而是
        /// 很快的降低"); 5 and 10 m are steeper than the standard. Written only when it is not 15 (a railing of before has none: it reads as 15).
        /// </summary>
        private static readonly GuideField kGapRampField = GuideField.Choice("gramp", "渐变长度", "Slope length", "15",
            new[] { C("5", "5 m（很陡）", "5 m (steep)"), C("10", "10 m", "10 m"), C("15", "15 m（GA/T 1567）", "15 m (GA/T 1567)"), C("20", "20 m", "20 m"), C("30", "30 m", "30 m") },
            s => !s.Is("gap", "n")).AsSparse();

        private static readonly GuideField kGapPostField = GuideField.Flag("gpost", "断口设分隔柱", "A separating post at the gap", true, s => !s.Is("gap", "n"));

        /// <summary>The median railings that have the gap setting (kGapField): they come down at their ends.</summary>
        private static readonly string[] kLowersAtEnds = { CityFenceId, BeijingFenceId, ZincFenceId, HongKongFenceId };

        /// <summary>
        /// What a new draft of an entry starts with where that is not the template's defaults (0.12.106): the median railings start lowered at both ends (a junction) with a low end of 10 m and the 15 m ramp
        /// before it (GA/T 1567-2019 4.2.6), the others as their fields say. Only the
        /// manual tool's choice of an entry reads it: a railing in a save, one the extension API places and the entry's picture are as the settings say.
        /// </summary>
        public static string StartSettings(string id) => Array.IndexOf(kLowersAtEnds, id) >= 0 ? "gap=b;glen=10" : "";

        private static GapTaper Taper(GuideSpec s) =>
            new GapTaper { AtStart = s.Is("gap", "s") || s.Is("gap", "b"), AtEnd = s.Is("gap", "e") || s.Is("gap", "b"), Length = F(s, "glen", 40f, 10f, 70f), Ramp = F(s, "gramp", 15f, 5f, 30f) };

        private static void AddGapPosts(GuideSpec s, List<Vec3> line, bool first, bool last, List<MarkingShape> shapes)
        {
            if (s.Is("gap", "n") || !s.Flag("gpost"))
                return;
            GapTaper taper = Taper(s);
            if (taper.AtStart && first)
                shapes.AddRange(Barriers.GapPost(line, true));
            if (taper.AtEnd && last)
                shapes.AddRange(Barriers.GapPost(line, false));
        }

        private static MarkingPlan PlanWith(List<MarkingShape> shapes, List<PropAnchor> props)
        {
            MarkingPlan plan = Plan(shapes);
            plan.Props.AddRange(props);
            return plan;
        }

        private static SolidColour Colour(GuideSpec s, string key, SolidColour fallback)
        {
            switch (s.Get(key))
            {
                case "metal": return SolidColour.Metal;
                case "steel": return SolidColour.Steel;
                case "white": return SolidColour.White;
                case "dark": return SolidColour.DarkGray;
                case "yellow": return SolidColour.Yellow;
                case "orange": return SolidColour.Orange;
                case "green": return SolidColour.Green;
                case "blue": return SolidColour.Blue;
                case "wood": return SolidColour.Wood;
                case "concrete": return SolidColour.Concrete;
                case "black": return SolidColour.Black;
                case "red": return SolidColour.Red;
                case "granite": return SolidColour.Granite;
                case "brown": return SolidColour.DarkBrown;
                default: return fallback;
            }
        }

        private static GuideChoice CMetal => C("metal", "镀锌银灰", "Galvanised grey");
        private static GuideChoice CSteel => C("steel", "不锈钢", "Stainless steel");
        private static GuideChoice CWhite => C("white", "白色", "White");
        private static GuideChoice CDark => C("dark", "深灰", "Dark grey");

        private static readonly GuideChoice[] kRoadsideOnly = { C("r", "路侧（车流在线的右侧）", "Roadside (the traffic on the line's right)"), C("l", "路侧（车流在线的左侧）", "Roadside (the traffic on the line's left)") };

        private static GuideField Setback => GuideField.Digits("off", "外移（cm，离开车流）", "Setback (cm, from the traffic)", "0", 3, s => !s.Is("where", "m"));

        private static void AddCityBarriers(List<GuideTemplate> list)
        {
            list.Add(T(HongKongFenceId, "深标Ⅰ型", "港式护栏（深标Ⅰ型）", "Hong Kong type railing", "gshl", W("港式", "甲型", "深标", "深标一型", "护栏", "隔离栏", "中央隔离", "railing", "hong kong"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "110", new[] { C("100", "1.0 m", "1.0 m"), C("110", "1.1 m", "1.1 m"), C("120", "1.2 m", "1.2 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "metal", new[] { CMetal, CWhite, CDark }),
                    GuideField.Choice("where", "位置", "Where", "m", kBarrierSide),
                    Setback,
                    kGapField, kGapLengthField, kGapRampField, kGapPostField
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    List<Vec3> line = BarrierRun(s);
                    List<MarkingShape> shapes = Barriers.HongKongFence(line, F(s, "height", 110f, 100f, 120f) / 100f, Colour(s, "colour", SolidColour.Metal), Taper(s), Phase(s), Total(s), first, last);
                    AddGapPosts(s, line, first, last, shapes);
                    return Plan(shapes);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(PedestrianRailingId, "GB 50688", "人行护栏", "Pedestrian railing", "rxhl", W("人行护栏", "人行道护栏", "栏杆", "扶手", "桥梁", "railing", "pedestrian", "guard rail"),
                new[]
                {
                    GuideField.Choice("height", "净高", "Height", "110", new[] { C("90", "0.9 m（最低）", "0.9 m (the least)"), C("110", "1.1 m", "1.1 m"), C("140", "1.4 m（桥上非机动车道旁）", "1.4 m (on a bridge by a cycle track)") }),
                    GuideField.Choice("colour", "颜色", "Colour", "steel", new[] { CSteel, CWhite, CDark, CMetal }),
                    GuideField.Choice("where", "位置", "Where", "r", kBarrierSide),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.PedestrianRailing(BarrierRun(s), F(s, "height", 110f, 90f, 140f) / 100f, Colour(s, "colour", SolidColour.Steel), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(LowRailingId, "GB 50688", "机非隔离栏", "Low separating railing", "jfglh", W("机非隔离", "矮护栏", "分隔栏杆", "非机动车道", "隔离栏", "low railing", "cycle lane"),
                new[]
                {
                    GuideField.Digits("height", "高度（cm）", "Height (cm)", "80", 3),
                    GuideField.Choice("colour", "颜色", "Colour", "yellow", new[] { C("yellow", "黄色", "Yellow"), CWhite, CSteel }),
                    GuideField.Choice("where", "位置", "Where", "m", kBarrierSide),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.LowRailing(BarrierRun(s), F(s, "height", 80f, 50f, 120f) / 100f, Colour(s, "colour", SolidColour.Yellow), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(LawnFenceId, "草坪护栏", "草坪护栏", "Lawn fence", "cphl", W("草坪护栏", "绿化带", "草坪", "矮栏", "花坛", "lawn fence", "edging"),
                new[]
                {
                    GuideField.Choice("style", "样式", "Style", "pickets", new[] { C("pickets", "竖条式", "Pickets"), C("hoops", "拱形", "Hoops") }),
                    GuideField.Choice("height", "高度", "Height", "40", new[] { C("30", "0.3 m", "0.3 m"), C("40", "0.4 m", "0.4 m"), C("50", "0.5 m", "0.5 m"), C("60", "0.6 m", "0.6 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "white", new[] { CWhite, C("green", "绿色", "Green"), CDark })
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.LawnFence(BarrierRun(s), F(s, "height", 40f, 30f, 60f) / 100f, s.Is("style", "hoops"), Colour(s, "colour", SolidColour.White), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(FlexPostId, "GB 50688", "弹性警示柱", "Flexible separating posts", "txjsz", W("警示柱", "弹性柱", "分隔柱", "隔离柱", "防撞柱", "flexible post", "delineator"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "75", new[] { C("45", "0.45 m", "0.45 m"), C("75", "0.75 m", "0.75 m"), C("100", "1.0 m", "1.0 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "orange", new[] { C("orange", "橙色", "Orange"), C("yellow", "黄色", "Yellow"), C("green", "绿色", "Green") }),
                    GuideField.Digits("gap", "间距（m）", "Spacing (m)", "1.5", 4)
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    return Plan(Barriers.FlexPosts(BarrierRun(s), F(s, "height", 75f, 45f, 100f) / 100f, Colour(s, "colour", SolidColour.Orange), F(s, "gap", 1.5f, 0.5f, 20f), Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(PlanterRailingId, "花箱护栏", "花箱护栏", "Railing with planters", "hxhl", W("花箱", "花箱护栏", "绿化护栏", "花盆", "栏杆", "planter", "flower box", "green railing"),
                new[]
                {
                    GuideField.Choice("height", "栏杆高度", "Railing height", "100", new[] { C("80", "0.8 m", "0.8 m"), C("100", "1.0 m", "1.0 m"), C("120", "1.2 m", "1.2 m") }),
                    GuideField.Choice("rail", "栏杆颜色", "Railing colour", "dark", new[] { CDark, CWhite, CMetal }),
                    GuideField.Choice("box", "花箱颜色", "Planter colour", "white", new[] { CWhite, C("green", "绿色", "Green"), C("wood", "木纹", "Wood"), CDark }),
                    GuideField.Choice("where", "位置", "Where", "m", kBarrierSide),
                    Setback
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    var props = new List<PropAnchor>();
                    List<MarkingShape> shapes = Barriers.PlanterRailing(BarrierRun(s), F(s, "height", 100f, 80f, 120f) / 100f, Colour(s, "rail", SolidColour.DarkGray), Colour(s, "box", SolidColour.White), Phase(s), last, props);
                    return PlanWith(shapes, props);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(PlanterBoxId, "花箱隔离", "花箱隔离墩", "Planter boxes", "hxgld", W("花箱", "隔离墩", "花箱隔离", "绿化隔离", "花坛", "planter", "planter box", "separator"),
                new[]
                {
                    GuideField.Choice("size", "规格（长×宽×高）", "Size (long x wide x high)", "15", new[] { C("10", "1.0×0.4×0.5 m", "1.0 x 0.4 x 0.5 m"), C("15", "1.5×0.5×0.6 m", "1.5 x 0.5 x 0.6 m"), C("20", "2.0×0.6×0.7 m", "2.0 x 0.6 x 0.7 m") }),
                    GuideField.Digits("gap", "间隔（m）", "Gap (m)", "0.5", 4),
                    GuideField.Choice("colour", "颜色", "Colour", "white", new[] { CWhite, C("concrete", "混凝土", "Concrete"), C("wood", "木纹", "Wood"), C("green", "绿色", "Green") })
                },
                s =>
                {
                    var props = new List<PropAnchor>();
                    float size = F(s, "size", 15f, 10f, 20f);
                    float length = size / 10f, width = size <= 10f ? 0.4f : size >= 20f ? 0.6f : 0.5f, height = size <= 10f ? 0.5f : size >= 20f ? 0.7f : 0.6f;
                    List<MarkingShape> shapes = Barriers.PlanterBoxes(BarrierRun(s), length, width, height, F(s, "gap", 0.5f, 0f, 5f), Colour(s, "colour", SolidColour.White), Phase(s), props);
                    return PlanWith(shapes, props);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(CombinedId, "GB 50688", "组合式护栏", "Combined barrier", "zhshl", W("组合式", "护栏", "桥梁护栏", "混凝土", "钢梁", "combined barrier", "bridge parapet"),
                new[]
                {
                    GuideField.Choice("height", "总高", "Height", "110", new[] { C("110", "1.1 m", "1.1 m"), C("120", "1.2 m", "1.2 m"), C("130", "1.3 m", "1.3 m") }),
                    GuideField.Choice("wall", "混凝土墙高", "Concrete wall", "60", new[] { C("50", "0.5 m", "0.5 m"), C("60", "0.6 m", "0.6 m"), C("70", "0.7 m", "0.7 m") }),
                    GuideField.Choice("colour", "钢构颜色", "Steel colour", "metal", new[] { CMetal, CDark, C("blue", "蓝色", "Blue") }),
                    GuideField.Choice("where", "位置", "Where", "r", kBarrierSide),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.CombinedBarrier(BarrierRun(s), F(s, "height", 110f, 110f, 130f) / 100f, F(s, "wall", 60f, 50f, 70f) / 100f, Colour(s, "colour", SolidColour.Metal), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(BeamAndPostId, "GB 50688", "金属梁柱式护栏", "Metal beam and post barrier", "jslzshl", W("梁柱式", "金属护栏", "桥梁护栏", "钢护栏", "横梁", "beam and post", "bridge rail"),
                new[]
                {
                    GuideField.Choice("beams", "横梁", "Beams", "2", new[] { C("2", "两根", "Two"), C("3", "三根", "Three") }),
                    GuideField.Choice("height", "高度", "Height", "110", new[] { C("110", "1.1 m", "1.1 m"), C("120", "1.2 m", "1.2 m"), C("130", "1.3 m", "1.3 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "metal", new[] { CMetal, CDark, CWhite }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.BeamAndPost(BarrierRun(s), I(s, "beams", 2, 2, 3), F(s, "height", 110f, 110f, 130f) / 100f, Colour(s, "colour", SolidColour.Metal), s.Is("where", "l") ? -1 : 1, Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(MovableId, "GB 50688", "活动护栏（中央分隔带开口）", "Movable barrier (median opening)", "hdhl", W("活动护栏", "开口", "中央分隔带", "插拔式", "movable barrier", "median opening", "gate"),
                new GuideField[0],
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.MovableBarrier(BarrierRun(s), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
        }
    }
}
