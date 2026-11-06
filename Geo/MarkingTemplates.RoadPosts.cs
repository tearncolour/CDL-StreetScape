using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The roadside posts of GB 5768.2-2022 8.6 (Barriers.RoadPosts): templates of the group "barrier", drawn along a road like the others.</summary>
    public static partial class MarkingTemplates
    {
        public const string DelineatorPostId = "bar.delpost", MilePostId = "bar.milepost", BoundaryStoneId = "bar.boundary", JunctionPostId = "bar.junctionpost";

        /// <summary>The spacing of the places of a run that has no road under it yet (the picture in the panel, the cursor's first look): a few metres, so that it shows a post and the ones after it.</summary>
        private const float kPostPreviewGap = 3f;

        private static bool HasPath(GuideSpec s) => RunPath.Decode(s.Get(RunPath.Key)).Count >= 2;

        private static void AddRoadPosts(List<GuideTemplate> list)
        {
            list.Add(T(DelineatorPostId, "图287", "柱式轮廓标（附百米牌）", "Post delineators with 100 m plates", "zslkb",
                W("柱式轮廓标", "轮廓标", "百米牌", "百米桩", "路64", "高速", "立桩", "里程", "delineator", "post", "hectometre", "guide post"),
                new[]
                {
                    GuideField.Choice("gap", "间隔", "Spacing", "50", new[] { C("50", "50 m（高速公路直线段）", "50 m (motorway straight)"), C("100", "100 m（每根都附牌）", "100 m (a plate on each)") }),
                    GuideField.Flag("plate", "附百米牌", "100 m plates", true),
                    GuideField.Digits("km", "起点里程（km）", "Start kilometre", "238", 4, s => s.Flag("plate")),
                    GuideField.Digits("hm", "起点百米数", "Start hundred", "1", 1, s => s.Flag("plate")),
                    GuideField.Choice("colour", "反射体颜色", "Reflector colour", "w", new[] { C("w", "白色（右侧）", "White (right side)"), C("y", "黄色（左侧）", "Yellow (left side)") }),
                    GuideField.Choice("where", "位置", "Where", "l", kRoadsideOnly).In("位置", "Place"),
                    Setback
                },
                s =>
                {
                    float gap = HasPath(s) ? F(s, "gap", 50f, 50f, 100f) : kPostPreviewGap;
                    return Plan(Barriers.PostDelineators(BarrierRun(s), gap, s.Flag("plate"), I(s, "km", 238, 0, 9999), I(s, "hm", 1, 0, 9), s.Is("colour", "y"), s.Is("where", "l") ? -1 : 1, Phase(s)));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(MilePostId, "图243", "公路里程碑和百米桩", "Kilometre stones and 100 m posts", "glgdb",
                W("里程碑", "百米桩", "公里桩", "里程桩", "桩号", "里程", "国道", "省道", "公路", "kilometre stone", "milestone", "hundred metre post"),
                new[]
                {
                    GuideField.Choice("kind", "放置", "Puts out", "all", new[] { C("all", "里程碑和百米桩", "Stones and posts"), C("km", "只放里程碑", "Stones only"), C("hm", "只放百米桩", "Posts only") }),
                    GuideField.Choice("class", "公路等级（字色）", "Road class (figure colour)", "g", new[] { C("g", "国道（红字）", "National (red)"), C("s", "省道（蓝字）", "Provincial (blue)"), C("x", "县道、乡道（黑字）", "County, township (black)") }),
                    GuideField.Text("code", "道路编号", "Road number", "G312", 6, s => !s.Is("kind", "hm")),
                    GuideField.Digits("km", "起点里程（km）", "Start kilometre", "238", 4),
                    GuideField.Digits("hm", "起点百米数", "Start hundred", "0", 1, s => !s.Is("kind", "km")),
                    GuideField.Digits("gap", "间隔（m，空为标准）", "Spacing (m, empty: standard)", "", 4),
                    GuideField.Choice("where", "位置", "Where", "l", kRoadsideOnly).In("位置", "Place"),
                    Setback
                },
                s =>
                {
                    MilePostKind kind = s.Is("kind", "km") ? MilePostKind.Stones : s.Is("kind", "hm") ? MilePostKind.Hundreds : MilePostKind.All;
                    PostRoadClass road = s.Is("class", "s") ? PostRoadClass.Provincial : s.Is("class", "x") ? PostRoadClass.Local : PostRoadClass.National;
                    float standard = kind == MilePostKind.Stones ? 1000f : 100f, typed = F(s, "gap", 0f, 0f, 2000f);
                    float gap = !HasPath(s) ? kPostPreviewGap : typed >= 2f ? typed : standard;
                    // The posts alone have no 0: the stone stands there, so a run of posts that starts at 0 starts at 1.
                    int hm = I(s, "hm", 0, 0, 9);
                    return Plan(Barriers.MilePosts(BarrierRun(s), kind, road, s.Get("code"), I(s, "km", 238, 0, 9999), kind == MilePostKind.Hundreds && hm == 0 ? 1 : hm, gap, Phase(s)));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(BoundaryStoneId, "图246", "公路界碑", "Right-of-way boundary stones", "gljb",
                W("公路界碑", "界碑", "公路界", "用地界", "分界", "路界", "boundary stone", "right of way", "boundary marker"),
                new[]
                {
                    GuideField.Choice("gap", "间隔", "Spacing", "200", new[] { C("200", "200 m", "200 m"), C("500", "500 m", "500 m"), C("100", "100 m（弯道加密）", "100 m (on curves)") }),
                    GuideField.Choice("where", "位置", "Where", "l", kRoadsideOnly).In("位置", "Place"),
                    Setback
                },
                s => Plan(Barriers.BoundaryStones(BarrierRun(s), HasPath(s) ? F(s, "gap", RoadPosts.BoundaryGap200, RoadPosts.BoundaryGapDense, RoadPosts.BoundaryGap500) : kPostPreviewGap, Phase(s))),
                true,
                null,
                BarrierGroup));
            list.Add(T(JunctionPostId, "图247", "道口标柱", "Junction posts", "dkbz",
                W("道口标柱", "标柱", "支路口", "路口", "弹性标柱", "红白", "junction post", "flexible post", "branch road"),
                new[]
                {
                    GuideField.Choice("at", "路口在", "Junction at", "s", new[] { C("s", "线的起点", "The line's start"), C("e", "线的终点", "The line's end") }),
                    GuideField.Choice("where", "位置", "Where", "l", kRoadsideOnly).In("位置", "Place"),
                    Setback
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    return Plan(Barriers.JunctionPosts(BarrierRun(s), Phase(s), Total(s), last, s.Is("at", "e")));
                },
                true,
                null,
                BarrierGroup));
        }
    }
}
