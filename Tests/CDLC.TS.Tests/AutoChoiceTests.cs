using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.44 (the owner: "优化清除 布线后自动布线的功能，比如路口的实线，支持选中道路后修改配置"): the settings of each approach of a chosen stretch (AutoMarking.Ranks, GuideAt, ArrowsAt), the record that keeps
    /// a stretch's settings (AutoMarking.RecordOf), and what the panel and the hint show of them.
    /// </summary>
    internal static class AutoChoiceTests
    {
        public static void RunAll()
        {
            Program.Run("auto choice: the approaches by where their stop is", ApproachRanks);
            Program.Run("auto choice: each approach's guide lane lines and arrows", ApproachSettings);
            Program.Run("auto choice: the record of a stretch", Record);
            Program.Run("auto choice: the panel's fields and the hints", PanelAndHints);
        }

        private static GuideTemplate Template => MarkingTemplates.ById(AutoMarking.Id);

        /// <summary>A road of two lanes each way, 200 m, signalled at both ends: the approach at the start against the chain, the one at the end with it.</summary>
        private static (RoadChain, ChainApproach[]) BothEnds()
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 200f)), Kmh = 50f, HasSpeed = true, After = ChainJoint.End, LaneFrom = 2f, LaneTo = 198f };
            piece.Forward.AddRange(new[] { 1.75f, 5.25f });
            piece.Backward.AddRange(new[] { -1.75f, -5.25f });
            var end = new ChainApproach { StopS = 198f, WithChain = true, Control = JunctionControl.Signals, CrosswalkAt = 0.5f };
            end.Lanes.Add(new ApproachLane { Lateral = 1.75f, Straight = true, Left = true });
            end.Lanes.Add(new ApproachLane { Lateral = 5.25f, Straight = true, Right = true });
            var start = new ChainApproach { StopS = 2f, WithChain = false, Control = JunctionControl.Signals, CrosswalkAt = 0.5f };
            start.Lanes.Add(new ApproachLane { Lateral = -1.75f, Straight = true, Left = true });
            start.Lanes.Add(new ApproachLane { Lateral = -5.25f, Straight = true, Right = true });
            // Listed end first: the ranks are by the stop's place, not by the order the reader found them in.
            return (new RoadChain(new[] { piece }), new[] { end, start });
        }

        private static AutoOptions Options(params (string Key, string Value)[] settings)
        {
            GuideSpec spec = Template.Default().With(AutoMarking.EndsKey, "1");
            foreach ((string key, string value) in settings)
                spec = spec.With(key, value);
            return AutoMarking.OptionsOf(spec);
        }

        private static List<AutoRun> Guides(AutoPlan plan, bool positive) => plan.Runs.FindAll(r => r.Id == "mk.line.guidelane" && (r.Lateral > 0f) == positive);

        private static List<AutoStamp> Arrows(AutoPlan plan, bool positive) => plan.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal) && (s.Lateral > 0f) == positive);

        private static void ApproachRanks()
        {
            (_, ChainApproach[] approaches) = BothEnds();
            int[] ranks = AutoMarking.Ranks(approaches);
            Program.Check(ranks.Length == 2 && ranks[0] == 1 && ranks[1] == 0, "the approach whose stop is nearer the chain's start is approach 1: " + string.Join(",", ranks));
            var same = new[] { new ChainApproach { StopS = 5f }, new ChainApproach { StopS = 5f }, new ChainApproach { StopS = 1f } };
            int[] tied = AutoMarking.Ranks(same);
            Program.Check(tied[2] == 0 && tied[0] == 1 && tied[1] == 2, "two at one place keep the reader's order");
            Program.Check(AutoMarking.Ranks(null).Length == 0 && AutoMarking.Ranks(new ChainApproach[0]).Length == 0, "none: no ranks");
        }

        private static void ApproachSettings()
        {
            (RoadChain chain, ChainApproach[] approaches) = BothEnds();
            AutoPlan both = AutoMarking.Plan(chain, approaches, Options());
            Program.Check(Guides(both, true).Count == 1 && Guides(both, false).Count == 1, "by default both approaches have their guide lane line");
            Program.Check(Arrows(both, true).Count > 0 && Arrows(both, false).Count > 0, "and their arrows");
            Program.Near(Guides(both, true)[0].Length, AutoMarking.MinGuide, 1e-3f, "30 m long");

            // Approach 1 (the one at the start) without guide lane lines; approach 2's 45 m.
            AutoPlan own = AutoMarking.Plan(chain, approaches, Options((AutoMarking.ApproachGuideKey(0), "0"), (AutoMarking.ApproachGuideKey(1), "45")));
            Program.Check(Guides(own, false).Count == 0, "approach 1 set to 0: no guide lane line");
            Program.Check(Guides(own, true).Count == 1 && Math.Abs(Guides(own, true)[0].Length - 45f) < 1e-3f, "approach 2 set to 45: 45 m");
            Program.Check(own.Runs.Exists(r => r.Id == "mk.line.lane" && r.Lateral < 0f && r.S0 < 10f), "approach 1's lane line runs on to the stop line instead");
            Program.Check(Arrows(own, false).Count == 2 && Arrows(both, false).Count == 4, "its arrows stay, one group: the second is at the start of the guide lane lines (5.13.6)");

            // An empty setting is as the stretch's; the stretch's length goes on applying to the others.
            AutoPlan longer = AutoMarking.Plan(chain, approaches, Options((AutoMarking.GuideModeKey, "fixed"), (AutoMarking.GuideKey, "50"), (AutoMarking.ApproachGuideKey(0), "")));
            Program.Check(Guides(longer, false).Count == 1 && Math.Abs(Guides(longer, false)[0].Length - 50f) < 1e-3f && Math.Abs(Guides(longer, true)[0].Length - 50f) < 1e-3f, "empty: the stretch's 50 m");

            // The arrows of one approach off, and on for one while the stretch has none.
            AutoPlan noArrows = AutoMarking.Plan(chain, approaches, Options((AutoMarking.ApproachArrowsKey(1), "0")));
            Program.Check(Arrows(noArrows, true).Count == 0 && Arrows(noArrows, false).Count == Arrows(both, false).Count, "approach 2's arrows off, approach 1's stay");
            AutoPlan onlyOne = AutoMarking.Plan(chain, approaches, Options((AutoMarking.ArrowsKey, "0"), (AutoMarking.ApproachArrowsKey(0), "1")));
            Program.Check(Arrows(onlyOne, false).Count == Arrows(both, false).Count && Arrows(onlyOne, true).Count == 0, "the stretch's arrows off, approach 1's on");

            AutoOptions parsed = Options((AutoMarking.ApproachGuideKey(2), "999"), (AutoMarking.ApproachArrowsKey(3), "1"));
            Program.Check(parsed.GuideAt.Length == AutoMarking.MaxApproaches && float.IsNaN(parsed.GuideAt[0]) && parsed.GuideAt[2] == 200f && parsed.ArrowsAt[3] == 1 && parsed.ArrowsAt[0] == -1,
                "read: empty is the stretch's, a length is at most 200 m, an arrow choice 1 or the stretch's");
            Program.Check(AutoMarking.Plan(chain, approaches, Options((AutoMarking.ApproachGuideKey(3), "0"))).Runs.Count == both.Runs.Count, "a setting of an approach the stretch does not have changes nothing");
        }

        private static void Record()
        {
            GuideTemplate t = Template;
            GuideSpec draft = t.Default().With(AutoMarking.EndsKey, "1").With(AutoMarking.ApproachGuideKey(1), "0").With(AutoMarking.ApproachArrowsKey(3), "0");
            GuideSpec record = AutoMarking.RecordOf(draft, 0.354f, 6);
            Program.Check(record.Flag(AutoMarking.RecordKey) && record.Get(AutoMarking.SurfaceKey) == "35" && record.Get(AutoMarking.ApproachesKey) == "4", "a record: 35 cm over the centre line, 4 approaches at most");
            Program.Near(AutoMarking.SurfaceOf(record), 0.35f, 1e-4f, "its surface read back");
            // 0.12.48: the version of the rules it was drawn by; a record of 0.12.44 to 0.12.47 has none and is drawn again when it is chosen.
            Program.Check(record.Get(AutoMarking.RulesKey) == AutoMarking.Rules.ToString() && !AutoMarking.IsOld(record), "a record keeps the rules it was drawn by");
            Program.Check(AutoMarking.IsOld(t.Parse("ends=1;rec=1;sf=20;na=2")) && AutoMarking.IsOld(record.With(AutoMarking.RulesKey, "1")), "an older record, or one of no version, is old");
            Program.Check(AutoMarking.DraftOf(record).Get(AutoMarking.RulesKey) == "0" && AutoMarking.WithoutStretch(record).Get(AutoMarking.RulesKey) == "0", "the panel's settings do not carry it");
            Program.Check(AutoMarking.RecordOf(draft, -0.2f, 1).Get(AutoMarking.SurfaceKey) == "0", "a surface below the centre line is kept as 0");
            GuideSpec back = AutoMarking.DraftOf(t.Parse(record.ToString()));
            Program.Check(!back.Flag(AutoMarking.RecordKey) && back.Get(AutoMarking.SurfaceKey) == "0" && back.Get(AutoMarking.ApproachesKey) == "4"
                && back.Get(AutoMarking.ApproachGuideKey(1)) == "0" && back.Get(AutoMarking.ApproachArrowsKey(3)) == "0", "the settings it keeps come back to the panel, with its approaches");
            GuideSpec other = AutoMarking.WithoutStretch(back);
            bool cleared = other.Get(AutoMarking.ApproachesKey) == "0";
            for (int k = 0; k < AutoMarking.MaxApproaches; k++)
                cleared &= other.Get(AutoMarking.ApproachGuideKey(k)) == "" && other.Get(AutoMarking.ApproachArrowsKey(k)) == "";
            Program.Check(cleared && other.Flag(AutoMarking.EndsKey), "another road: the stretch's own settings go, the rest stay");

            // Every setting filled in still fits the item's field, nothing cut off.
            GuideSpec full = t.Default().With(AutoMarking.EndsKey, "1");
            for (int k = 0; k < AutoMarking.MaxApproaches; k++)
                full = full.With(AutoMarking.ApproachGuideKey(k), "120").With(AutoMarking.ApproachArrowsKey(k), "1");
            string text = AutoMarking.RecordOf(full, 12.34f, 4).ToString();
            Program.Check(Encoding.UTF8.GetByteCount(text) <= GuideSpec.MaxBytes && t.Parse(text).Get(AutoMarking.ApproachGuideKey(3)) == "120" && t.Parse(text).Get(AutoMarking.SurfaceKey) == "1234",
                "a record with every setting fits its field whole: " + Encoding.UTF8.GetByteCount(text) + " bytes");

            // It draws nothing and is no failure for the renderer.
            Program.Check(MarkingTemplates.PlanOf(t, record.ToString()).Parts.Count == 0 && MarkingTemplates.PlanOf(t, draft.ToString()).Parts.Count > 0, "a record draws nothing (the entry's picture does)");
            var item = new ItemRecord { TypeHash = SignCatalog.ById(AutoMarking.Id).Hash, Position = new Vec3(10f, 5f, 10f), Spec = record.ToString(), Auto = true };
            List<ChunkPart> parts = ChunkBuilder.Build(new[] { item }, Vec3.Zero, out int skipped);
            Program.Check(skipped == 0 && parts.Count == 0, "the renderer has nothing to draw for it and does not count it as skipped: " + skipped);
        }

        private static void PanelAndHints()
        {
            GuideTemplate t = Template;
            bool Shown(GuideSpec s, string key)
            {
                GuideField field = t.FieldOf(key);
                return field != null && (field.Visible == null || field.Visible(s));
            }

            GuideSpec none = t.Default().With(AutoMarking.EndsKey, "1");
            GuideSpec two = none.With(AutoMarking.ApproachesKey, "2");
            Program.Check(!Shown(none, AutoMarking.ApproachGuideKey(0)) && !Shown(none, AutoMarking.ApproachArrowsKey(0)), "nothing chosen: no approach settings");
            Program.Check(Shown(two, AutoMarking.ApproachGuideKey(1)) && Shown(two, AutoMarking.ApproachArrowsKey(1)) && !Shown(two, AutoMarking.ApproachGuideKey(2)), "a chosen stretch of two approaches: theirs, not a third's");
            Program.Check(!Shown(two.With(AutoMarking.EndsKey, "0"), AutoMarking.ApproachGuideKey(0)), "without the junctions: none");
            Program.Check(!Shown(two, AutoMarking.RecordKey) && !Shown(two, AutoMarking.SurfaceKey) && !Shown(two, AutoMarking.ApproachesKey), "the record's own fields are never shown");
            Program.Check(t.FieldOf(AutoMarking.ApproachGuideKey(1)).Zh.Contains(MarkingTemplates.ApproachColoursZh[1]) && t.FieldOf(AutoMarking.ApproachArrowsKey(3)).En.Contains(MarkingTemplates.ApproachColoursEn[3]),
                "each approach's settings name the colour of its ring");

            foreach (bool zh in new[] { true, false })
            {
                string pending = HintText.Of(new HintInfo { Kind = "mark-auto-chosen", Count = 40, Left = 180f, Need = 2f, X = 2f, Manual = true }, zh);
                string ready = HintText.Of(new HintInfo { Kind = "mark-auto-chosen", Count = 40, Left = 180f, Need = 2f, X = 0f }, zh);
                string bad = HintText.Of(new HintInfo { Kind = "mark-auto-chosen", Left = 180f, Need = 2f, X = 2f, Bad = true }, zh);
                Program.Check(pending.Contains(zh ? "稍后自动重画" : "in a moment"), (zh ? "zh" : "en") + ": settings to be drawn, and the approaches' rings");
                Program.Check(!ready.Contains(zh ? "彩色圈" : "rings") && ready.Contains("40"), (zh ? "zh" : "en") + ": no approaches, no rings; the items it would place");
                Program.Check(bad.Contains(zh ? "画不了" : "Cannot be drawn"), (zh ? "zh" : "en") + ": what cannot be drawn says so");
                string marked = HintText.Of(new HintInfo { Kind = "mark-auto-marked", Left = 180f, Need = 2f, Name = "s" }, zh);
                Program.Check(marked.Contains(zh ? "单击选中" : "click to choose"), (zh ? "zh" : "en") + ": a stretch marked before is chosen by a click");
                Program.Check(!HintText.Of(new HintInfo { Kind = "mark-auto", Count = 3, Left = 100f, Need = 1f, Name = "s" }, zh).Contains(zh ? "再次单击" : "second click"), (zh ? "zh" : "en") + ": a second click no longer replaces");
            }
        }
    }
}
