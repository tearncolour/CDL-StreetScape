using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The barrier and railing styles of 0.12.49 (Barriers.More, Barriers.Noise; scratch/research-barriers/REPORT.md): templates of the group "barrier", placed like the others along a road.</summary>
    public static partial class MarkingTemplates
    {
        public const string FretRailingId = "bar.fret", RingRailingId = "bar.fence.ring";

        private static void AddMoreBarriers(List<GuideTemplate> list)
        {
            AddNoiseBarriers(list);
            list.Add(T(FretRailingId, "仿古栏杆", "中式仿古栏杆（回纹）", "Classical Chinese railing", "zsfglg", W("仿古", "回纹", "回字纹", "中式", "铝艺", "铝代木", "古建", "河道", "公园", "直棂", "classical", "chinese", "fret", "railing"),
                new[]
                {
                    GuideField.Choice("pattern", "花格", "Lattice", "fret", new[] { C("fret", "回纹", "Fret"), C("bars", "直棂", "Upright bars") }),
                    GuideField.Choice("height", "高度", "Height", "110", new[] { C("100", "1.0 m", "1.0 m"), C("105", "1.05 m（公园）", "1.05 m (park)"), C("110", "1.1 m（桥、人行道）", "1.1 m (bridge, footway)"), C("120", "1.2 m", "1.2 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "brown", new[] { C("brown", "古铜咖啡色", "Bronze brown"), C("wood", "木色", "Wood colour"), CDark, C("black", "黑色", "Black") }),
                    GuideField.Choice("where", "位置", "Where", "r", kRoadsideOnly),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.FretRailing(BarrierRun(s), F(s, "height", 110f, 100f, 120f) / 100f, s.Is("pattern", "bars") ? FretPattern.Bars : FretPattern.Fret, Colour(s, "colour", SolidColour.DarkBrown), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(RingRailingId, "景观护栏", "城市景观护栏（圆环）", "City railing with rings", "csjghl", W("景观护栏", "文化护栏", "花型护栏", "圆环", "市政护栏", "中央隔离", "隔离栏", "railing", "decorative", "ring", "median fence"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "110", new[] { C("100", "1.0 m", "1.0 m"), C("110", "1.1 m", "1.1 m"), C("120", "1.2 m", "1.2 m") }),
                    GuideField.Choice("colour", "颜色", "Colour", "white", new[] { CWhite, CDark, C("blue", "蓝色", "Blue"), C("green", "绿色", "Green") }),
                    GuideField.Choice("where", "位置", "Where", "m", kBarrierSide),
                    Setback
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.RingRailing(BarrierRun(s), F(s, "height", 110f, 100f, 120f) / 100f, Colour(s, "colour", SolidColour.White), Phase(s), first, last));
                },
                true,
                null,
                BarrierGroup));
        }
    }
}
