using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The construction hoarding's template (施工围挡): the styles and the flower troughs of 0.12.78 on the flat panel of 0.12.13 (Barriers.HoardingStyled).</summary>
    public static partial class MarkingTemplates
    {
        private static HoardingStyle HoardingStyleOf(GuideSpec s)
        {
            switch (s.Get("style"))
            {
                case "steel": return HoardingStyle.Steel;
                case "module": return HoardingStyle.Module;
                case "pvc": return HoardingStyle.Pvc;
                case "mesh": return HoardingStyle.Mesh;
                case "brick": return HoardingStyle.Brick;
                case "warn": return HoardingStyle.Warning;
                case "lamp": return HoardingStyle.Lamp;
                case "gate": return HoardingStyle.Gate;
                default: return HoardingStyle.Flat;
            }
        }

        private static GuideTemplate HoardingTemplate()
        {
            return T(HoardingId, "施工围挡", "施工围挡", "Construction hoarding", "sgwd", W("围挡", "施工围挡", "工地", "挡板", "彩钢板", "装配式", "PVC", "护栏网", "砖墙", "警示灯", "大门", "花箱", "hoarding", "construction fence", "gate"),
                new[]
                {
                    GuideField.Choice("style", "样式", "Style", "flat", new[]
                    {
                        C("flat", "平板", "Flat panel"), C("steel", "彩钢板", "Colour-coated steel sheet"), C("module", "装配式", "Modular panels"), C("pvc", "PVC 板", "PVC boards"), C("mesh", "护栏网", "Wire mesh"),
                        C("brick", "砖墙", "Brick wall"), C("warn", "警示色", "Warning stripes"), C("lamp", "警示灯", "Warning lamps"), C("gate", "工地大门", "Site gate")
                    }),
                    GuideField.Choice("colour", "颜色", "Colour", "b", new[] { C("b", "蓝色", "Blue"), C("w", "白色", "White"), C("g", "绿色", "Green") }, s => !s.Is("style", "brick")),
                    GuideField.Choice("brick", "砖色", "Brick", "red", new[] { C("red", "红砖", "Red brick"), C("grey", "青砖", "Grey brick"), C("white", "白色抹灰", "White render") }, s => s.Is("style", "brick")),
                    GuideField.Choice("height", "高度", "Height", "200", new[] { C("180", "1.8 m", "1.8 m"), C("200", "2.0 m", "2.0 m"), C("250", "2.5 m", "2.5 m") }),
                    GuideField.Choice("post", "立柱间距", "Post spacing", "3", new[] { C("2", "2 m", "2 m"), C("3", "3 m", "3 m"), C("4", "4 m", "4 m") }),
                    GuideField.Choice("gate", "大门宽度", "Gate width", "6", new[] { C("4", "4 m", "4 m"), C("6", "6 m", "6 m"), C("8", "8 m", "8 m") }, s => s.Is("style", "gate")),
                    GuideField.Choice("flowers", "花箱", "Flowers", "none", new[] { C("none", "无", "None"), C("foot", "地面花箱", "Troughs on the ground"), C("top", "顶部挂箱", "Troughs hung on top") }),
                    GuideField.Choice("plants", "植物", "Plants", "flower", kGreenPlants, s => !s.Is("flowers", "none")),
                    GuideField.Choice("side", "花箱位置", "Trough side", "r", new[] { C("r", "右侧", "Right"), C("l", "左侧", "Left"), C("b", "两侧", "Both") }, s => !s.Is("flowers", "none"))
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    SolidColour panel = s.Is("colour", "w") ? SolidColour.White : s.Is("colour", "g") ? SolidColour.Green : SolidColour.Blue;
                    float height = F(s, "height", 200f, 150f, 300f) / 100f, post = F(s, "post", 3f, 2f, 4f);
                    // An item saved before the styles drew only this: its settings have neither key, and the defaults give the flat panel with no flowers.
                    if (s.Is("style", "flat") && s.Is("flowers", "none"))
                        return Plan(Barriers.Hoarding(BarrierRun(s), height, post, panel, Phase(s), last));
                    var o = new HoardingOptions
                    {
                        Style = HoardingStyleOf(s), Height = height, Spacing = post, Panel = panel,
                        Brick = s.Is("brick", "grey") ? SolidColour.GreyBrick : s.Is("brick", "white") ? SolidColour.White : SolidColour.Brick,
                        GateWidth = F(s, "gate", 6f, 4f, 8f),
                        Flowers = s.Is("flowers", "foot") ? HoardingFlowers.Foot : s.Is("flowers", "top") ? HoardingFlowers.Top : HoardingFlowers.None,
                        Side = s.Is("side", "l") ? -1 : s.Is("side", "b") ? 0 : 1,
                        Plants = s.Is("plants", "shrub") ? GreenPlants.Shrub : GreenPlants.Flower
                    };
                    var props = new List<PropAnchor>();
                    List<MarkingShape> shapes = Barriers.HoardingStyled(BarrierRun(s), o, Phase(s), Total(s), first, last, props);
                    return PlanWith(shapes, props);
                },
                true,
                null,
                BarrierGroup);
        }
    }
}
