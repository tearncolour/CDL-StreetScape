using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>The BRT series (0.12.22; the owner: "加入BRT系列道路标识"): the boards, the standard's entries listed with them, the presets of the markings and the reserved hours on the road.</summary>
    internal static class BrtTests
    {
        public static void RunAll()
        {
            Program.Run("brt: the series lists the standard's entries and its own boards", Series);
            Program.Run("brt: the boards are laid out with what they are set to", Boards);
            Program.Run("brt: the reserved hours come before the words on the road (Fig. 87)", Hours);
        }

        private static void Series()
        {
            var listed = new HashSet<string>();
            foreach (SignChoice c in SignCatalog.Choices)
            {
                if (c.Series == BrtSigns.Group)
                    listed.Add(c.Id);
            }

            foreach (string id in BrtSigns.Series)
                Program.Check(listed.Contains(id), "the BRT tab lists " + id);
            foreach (string id in new[] { "brt.station", "brt.nocars", "brt.lane.ends", "brt.notice" })
            {
                SignChoice c = SignCatalog.ChoiceById(id);
                Program.Check(c != null && c.Group == BrtSigns.Group && c.Series == BrtSigns.Group && !c.Def.IsMarking, id + " is a board of the BRT kind");
                Program.Check(c != null && c.Code == "非国标" && c.Zh.Contains("常见做法"), id + " says it is not the standard's");
            }

            SignChoice mand = SignCatalog.ChoiceById("gb.mand.28");
            Program.Check(mand.Group == "mandatory" && mand.Series == BrtSigns.Group, "示28 stays a mandatory sign and is listed in the series too");
            SignChoice word = SignCatalog.ChoiceById("brt.mk.text");
            Program.Check(word != null && word.Group == "marking" && word.Def.IsMarking, "a preset of a marking is a marking (it is placed as one)");
            foreach (GuidePreset p in GuideTemplates.Presets)
                Program.Check(SignCatalog.ChoiceById(p.Id).Group == SignCatalog.ById(p.Template).Group, p.Id + ": a preset is of its template's kind");
            Program.Check(SignCatalog.ChoiceById("gb.exit.adv.2km").Group == "guide", "the exit boards' presets are still guide boards");

            Program.Check(SignCatalog.ChoiceById("gb.mand.27").Series == "", "示27 (buses, not BRT) is not in the series");
        }

        private static void Boards()
        {
            SignDef station = SignCatalog.ById("brt.station");
            GuideTemplate t = station.Template;
            GuideBoard plain = t.Layout(t.Default()), noWay = t.Layout(t.Default().With("arrow", "none").With("dist", ""));
            Program.Check(plain.Main.H > noWay.Main.H + 0.1f, "without the arrow and the distance the station board is a row lower");
            Program.Check(t.Layout(t.Default().With("ground", "green")).Main.Ground == GuideColour.Green && plain.Main.Ground == GuideColour.Blue, "blue by default, green when set");
            GuideBoard longName = t.Layout(t.Default().With("name", "国际会展中心北站"));
            Program.Check(longName.Main.W > plain.Main.W, "a longer name makes the board wider");

            GuideTemplate no = SignCatalog.ById("brt.nocars").Template;
            GuideBoard withHours = no.Layout(no.Default().With("time", "7:00-9:00 17:00-19:00")), without = no.Layout(no.Default());
            Program.Check(withHours.Main.H > without.Main.H && without.Main.BorderColour == GuideColour.Red, "the hours add a row; the border is red");
            GuideTemplate ends = SignCatalog.ById("brt.lane.ends").Template;
            Program.Check(ends.Default().Get("kind") == "start" && ends.Layout(ends.Default().With("kind", "end")).Main.W > 0f, "the start by default, the end when set");
            foreach (string id in new[] { "brt.station", "brt.nocars", "brt.lane.ends", "brt.notice" })
            {
                GuideTemplate each = SignCatalog.ById(id).Template;
                float small = each.Layout(each.Default().With("speed", "30")).Main.H, large = each.Layout(each.Default().With("speed", "100")).Main.H;
                Program.Check(large > small, id + ": larger characters on a faster road");
            }
        }

        private static void Hours()
        {
            MarkingLetters.Font = FontLetterTests.Font;
            GuideTemplate word = MarkingTemplates.ById(MarkingTemplates.WordId);
            MarkingPlan plain = MarkingTemplates.PlanOf(word, "text=快速公交"), timed = MarkingTemplates.PlanOf(word, SignCatalog.ChoiceById("brt.mk.text.time").Spec);
            Program.Check(Math.Abs(plain.MinZ) < 1e-3f || plain.MinZ >= 0f, "the words start at the item: " + plain.MinZ);
            Program.Check(timed.MaxZ > plain.MaxZ + 2f, $"two rows of hours make it longer: {plain.MaxZ:0.0} to {timed.MaxZ:0.0} m");
            Program.Check(timed.TriangleCount > plain.TriangleCount, "and they are painted");
            Program.Near(MarkingTemplates.PlanOf(word, "text=快速公交;time=").MaxZ, plain.MaxZ, 1e-4f, "no hours: as before");
            Program.Near(MarkingTemplates.PlanOf(word, "text=快速公交;time=7-9，17-19").MaxZ, timed.MaxZ, 1e-4f, "a Chinese comma separates them too");
        }
    }
}
