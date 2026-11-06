using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.45 (the owner: "可选项中可以根据道路状态放置导向标线"; chosen: the road's conditions and its congestion): the guide lane lines' length, the arrows and the preview group by the road's state
    /// (AutoMarking.RoadGuide, RoadPreview, the arrows where a lane is added), the settings that choose them, and what the panel says was drawn.
    /// </summary>
    internal static class RoadStateTests
    {
        public static void RunAll()
        {
            Program.Run("road state: the guide lane lines' length", GuideLength);
            Program.Run("road state: the plan at an approach", PlanAtApproach);
            Program.Run("road state: arrows where a lane is added", AddedLane);
            Program.Run("road state: the settings and the panel's lines", SettingsAndText);
            Program.Run("road state: the clear tool's redraw is the automatic marking", ClearRedraw);
            Program.Run("small nodes: a line in parts where the areas meet", Parts);
            Program.Run("small nodes: the next segment's part of the node", NodePart);
            Program.Run("small nodes: on a curve", CurvedNode);
            Program.Run("arrows: their size is a setting", ArrowSizes);
            Program.Run("settings: the automatic marking's sections", Sections);
        }

        /// <summary>0.12.47: the items of a line do not cross where a segment's area meets a node's (RoadChain.Cuts), and its pattern goes on over them.</summary>
        private static void Parts()
        {
            var a = Piece(0f, 100f, 50f, new[] { 1.75f }, new[] { -1.75f }, ChainJoint.Pass, 0f, 100f);
            a.DrawnFrom = 0f;
            a.DrawnTo = 96f;
            var b = Piece(100f, 200f, 50f, new[] { 1.75f }, new[] { -1.75f }, ChainJoint.End, 0f, 100f);
            b.DrawnFrom = 4f;
            b.DrawnTo = 100f;
            var chain = new RoadChain(new[] { a, b });
            Program.Check(chain.Cuts.Count == 4 && Math.Abs(chain.Cuts[1] - 96f) < 1e-3f && Math.Abs(chain.Cuts[2] - 104f) < 1e-3f, "the cuts are where the pieces are drawn: " + string.Join(",", chain.Cuts));
            List<List<Vec3>> parts = chain.LineParts(10f, 190f, s => 0f);
            Program.Check(parts.Count == 3, "a line over the node in three parts (segment, node, segment): " + parts.Count);
            List<RunItem> items = MarkingRuns.Items(parts, out float total);
            Program.Near(total, 180f, 0.05f, "the whole line's length");
            Program.Check(items.Exists(i => Math.Abs(i.Start - 86f) < 0.05f) && items.Exists(i => Math.Abs(i.Start - 94f) < 0.05f), "an item starts at each cut, the pattern counted from the line's start");
            Program.Check(items.TrueForAll(i => i.Start < 86f - 0.05f || i.Start > 86f - 0.05f), "and the items are in order");
            Program.Check(chain.LineParts(10f, 96.5f, s => 0f).Count == 1, "a cut under a metre from an end does not cut");
            var reversed = Piece(0f, 50f, 50f, new[] { 1.75f }, new[] { -1.75f }, ChainJoint.End, 0f, 50f);
            reversed.Reversed = true;
            reversed.DrawnFrom = 2f;
            reversed.DrawnTo = 45f;
            var back = new RoadChain(new[] { reversed });
            Program.Check(Math.Abs(back.Cuts[0] - 5f) < 1e-3f && Math.Abs(back.Cuts[1] - 48f) < 1e-3f, "a reversed piece's cuts from the chain's start: " + string.Join(",", back.Cuts));
        }

        /// <summary>
        /// 0.12.47 (the owner: "小节点处没有标线"): a segment marked alone has the next segment's part of a plain node as a stub (ChainReader): its lines go on through the node to where the next
        /// segment is drawn, not to the node's middle only; the stub is no approach and no segment of the stretch.
        /// </summary>
        private static void NodePart()
        {
            var own = Piece(0f, 100f, 50f, new[] { 1.75f }, new[] { -1.75f }, ChainJoint.Pass, 0f, 100f);
            own.DrawnFrom = 0f;
            own.DrawnTo = 96f;
            var stub = Piece(100f, 160f, 50f, new[] { 1.75f }, new[] { -1.75f }, ChainJoint.End, 0f, 4f);
            stub.DrawnFrom = 4f;
            stub.DrawnTo = 60f;
            stub.Stub = true;
            var chain = new RoadChain(new[] { own, stub });
            AutoPlan plan = AutoMarking.Plan(chain, null, Options());
            AutoRun lane = null;
            foreach (AutoRun r in plan.Runs)
            {
                if ((r.Id == "mk.line.centre" || r.Id == "mk.line.doubleyellow") && (lane == null || r.S1 > lane.S1))
                    lane = r;
            }

            Program.Check(lane != null && Math.Abs(lane.S1 - 104f) < 1e-3f, "the centre line goes on through the node to where the next segment is drawn: " + (lane != null ? lane.S1.ToString("0.0") : "none"));
            Program.Check(plan.Runs.TrueForAll(r => r.S1 <= 104f + 1e-3f), "and no line goes into the next segment itself");
            List<List<Vec3>> parts = chain.LineParts(lane.S0, lane.S1, s => 0f);
            Program.Check(parts.Count == 2, "the node's part of the line is its own items (marking the next segment later takes them away)");
        }

        /// <summary>
        /// 0.12.48 (the owner: "在弯道小节点处还是有问题"): a curved segment marked alone, its plain node 6 m long on each side: its lines go on through the node to where the next segment is drawn, and
        /// the items cover the whole line, none crossing where the segment's area meets the node's.
        /// </summary>
        private static void CurvedNode()
        {
            // A bend of 90 degrees over two segments of about 80 m each, meeting at (60, 0, 60).
            var a = new ChainPiece { Curve = new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 33f), new Vec3(27f, 0f, 60f), new Vec3(60f, 0f, 60f)), Kmh = 50f, HasSpeed = true, After = ChainJoint.Pass };
            var b = new ChainPiece { Curve = new Bezier3(new Vec3(60f, 0f, 60f), new Vec3(93f, 0f, 60f), new Vec3(120f, 0f, 87f), new Vec3(120f, 0f, 120f)), Kmh = 50f, HasSpeed = true, After = ChainJoint.End };
            foreach (ChainPiece p in new[] { a, b })
            {
                p.Forward.AddRange(new[] { 1.75f, 5.25f });
                p.Backward.AddRange(new[] { -1.75f, -5.25f });
            }

            float lengthA = new ArcTable(a.Curve).Length;
            a.DrawnFrom = 0f;
            a.DrawnTo = lengthA - 6f;
            a.LaneFrom = 0f;
            a.LaneTo = float.MaxValue;
            b.DrawnFrom = 6f;
            b.DrawnTo = new ArcTable(b.Curve).Length;
            b.LaneFrom = 0f;
            b.LaneTo = 6f;
            b.Stub = true;
            var chain = new RoadChain(new[] { a, b });
            AutoPlan plan = AutoMarking.Plan(chain, null, Options());
            float end = lengthA + 6f;
            List<AutoRun> along = plan.Runs.FindAll(r => r.Id == "mk.line.lane" || r.Id == "mk.line.doubleyellow" || r.Id == "mk.line.edge");
            Program.Check(along.Count >= 4 && along.TrueForAll(r => Math.Abs(r.S1 - end) < 0.05f), "every line goes through the node on the bend to where the next segment is drawn: " + string.Join(", ", along.ConvertAll(r => r.S1.ToString("0.0"))));
            foreach (AutoRun run in along)
            {
                List<RunItem> items = MarkingRuns.Items(chain.LineParts(run.S0, run.S1, run.LateralAt), out float total);
                Vec3 cut = chain.PointAt(a.DrawnTo, run.LateralAt(a.DrawnTo));
                Program.Check(items.Count >= 2 && items.Exists(i => Vec3.DistanceXZ(i.Position, cut) < 0.1f), run.Id + ": an item starts where the node's area does");
                Vec3 last = chain.PointAt(run.S1, run.LateralAt(run.S1));
                List<Vec3> line = chain.Line(run.S0, run.S1, run.LateralAt, 0f);
                float ground = 0f;
                for (int k = 1; k < line.Count; k++)
                    ground += Vec3.DistanceXZ(line[k - 1], line[k]);
                Program.Near(total, ground, 0.05f, run.Id + ": the items cover the whole line on the bend, in parts");
                Program.Check(Vec3.DistanceXZ(line[line.Count - 1], last) < 0.05f, run.Id + ": to the end");
            }
        }

        private static void ArrowSizes()
        {
            string SizeOf(AutoStamp s) => s.Settings.Find(kv => kv.Key == "size").Value;
            (RoadChain chain, ChainApproach approach) = Approach(50f);
            AutoPlan auto = AutoMarking.Plan(chain, new[] { approach }, Options());
            Program.Check(auto.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal)).TrueForAll(s => SizeOf(s) == null), "auto: the arrows take the road's size when they are placed");
            AutoPlan big = AutoMarking.Plan(chain, new[] { approach }, Options((AutoMarking.ArrowSizeKey, "32")));
            List<AutoStamp> arrows = big.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal));
            Program.Check(arrows.Count > 0 && arrows.TrueForAll(s => SizeOf(s) == "32"), "Fig. 44: every arrow 9 m");
            float Nearest(AutoPlan p)
            {
                float most = float.MinValue;
                foreach (AutoStamp s in p.Stamps)
                {
                    if (s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal))
                        most = Math.Max(most, s.S);
                }

                return most;
            }

            Program.Near(Nearest(auto) - Nearest(big), 3f, 1e-3f, "the first group is placed for 9 m arrows (the road's are 6 m at 50 km/h)");
            Program.Check(AutoMarking.FixedArrowSize("33") == ArrowSize.Line33 && AutoMarking.FixedArrowSize("auto") == null && AutoMarking.FixedArrowSize("x") == null, "the settings read");
            GuideTemplate t = Template;
            GuideField size = t.FieldOf(AutoMarking.ArrowSizeKey);
            GuideSpec on = t.Default().With(AutoMarking.EndsKey, "1");
            Program.Check(size != null && size.Visible(on) && !size.Visible(on.With(AutoMarking.ArrowsKey, "0")) && size.Choices.Length == 5, "the setting is shown with the arrows, with the four figures and auto");
        }

        /// <summary>0.12.47 (the owner: "优化排版"): the automatic marking's settings come in three sections, each started by a field that is always shown.</summary>
        private static void Sections()
        {
            GuideTemplate t = Template;
            var starts = new List<string>();
            foreach (GuideField f in t.Fields)
            {
                if (f.SectionZh != null)
                {
                    starts.Add(f.SectionZh);
                    Program.Check(f.Visible == null && !string.IsNullOrEmpty(f.SectionEn), f.Key + ": a section starts with a field that is always shown, named in both languages");
                }
            }

            Program.Check(starts.Count == 3 && starts[0] == "范围" && starts[1] == "路口" && starts[2] == "沿路", "范围, 路口, 沿路: " + string.Join(",", starts));
            int ends = Array.FindIndex(t.Fields, f => f.Key == AutoMarking.EndsKey), approach = Array.FindIndex(t.Fields, f => f.Key == AutoMarking.ApproachGuideKey(0)), edge = Array.FindIndex(t.Fields, f => f.Key == AutoMarking.EdgeKey);
            Program.Check(ends < approach && approach < edge, "each approach's settings are with the junctions'");
        }

        /// <summary>0.12.46 (the owner, on 0.12.45: "这重标好像和之前没区别啊": the clicks were the clear tool's redraw, which drew the lines along only).</summary>
        private static void ClearRedraw()
        {
            GuideTemplate clear = MarkingTemplates.ById(VanillaClear.Id);
            GuideSpec segment = clear.Default().With(VanillaClear.RedrawKey, "1"), road = segment.With(VanillaClear.ModeKey, "r").With(VanillaClear.KeepKey, "all");
            GuideSpec a = AutoMarking.FromClear(Template, segment), b = AutoMarking.FromClear(Template, road);
            Program.Check(a.Is(AutoMarking.RangeKey, "e") && a.Is(AutoMarking.KeepKey, "lines") && b.Is(AutoMarking.RangeKey, "s") && b.Is(AutoMarking.KeepKey, "all"), "the clear tool's range and keep");
            AutoOptions o = AutoMarking.OptionsOf(a);
            Program.Check(o.Junctions && o.GuideByRoad && o.ArrowsByRoad && o.PreviewByRoad, "the junctions' guide lane lines and arrows by the road's state, as the automatic marking's defaults");
            // What the clear tool's redraw used to place are the kinds the automatic marking draws along: a stretch's panel finds those left on it.
            var kinds = new HashSet<string>();
            float[][] sides = { new[] { 1.75f }, new[] { 1.75f, 5.25f }, new[] { 1.75f, 5.25f, 8.75f } };
            foreach (float[] forward in sides)
            {
                foreach (float kmh in new[] { 40f, 100f })
                {
                    foreach (StandardLine line in StandardLines.Plan(forward, Array.ConvertAll(forward, x => -x), kmh, true))
                        kinds.Add(line.Id);
                    foreach (StandardLine line in StandardLines.Plan(forward, new float[0], kmh, true))
                        kinds.Add(line.Id);
                }
            }

            Program.Check(kinds.SetEquals(StandardLines.Ids), "StandardLines.Ids is what Plan draws: " + string.Join(",", kinds));
            string hint = HintText.Of(new HintInfo { Kind = "mark-clear", Name = "e", Count = 2, Left = 80f, Manual = true, Hung = true }, true);
            Program.Check(!hint.Contains("重画"), "the clear tool's hint no longer promises the old redraw: " + hint);
        }

        private static GuideTemplate Template => MarkingTemplates.ById(AutoMarking.Id);

        private static ChainPiece Piece(float s0, float s1, float kmh, float[] forward, float[] backward, ChainJoint after, float laneFrom = float.NaN, float laneTo = float.NaN)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(new Vec3(0f, 0f, s0), new Vec3(0f, 0f, s1)), Kmh = kmh, HasSpeed = true, After = after, LaneFrom = laneFrom, LaneTo = laneTo };
            piece.Forward.AddRange(forward);
            piece.Backward.AddRange(backward);
            return piece;
        }

        /// <summary>A road of two lanes each way, 200 m, ending at a signalled junction; the approach at the end with its traffic.</summary>
        private static (RoadChain, ChainApproach) Approach(float kmh = 50f, float flow = float.NaN, float queue = float.NaN, int lanes = 2, JunctionControl control = JunctionControl.Signals)
        {
            float[] forward = lanes == 2 ? new[] { 1.75f, 5.25f } : new[] { 1.75f }, backward = lanes == 2 ? new[] { -1.75f, -5.25f } : new[] { -1.75f };
            var chain = new RoadChain(new[] { Piece(0f, 200f, kmh, forward, backward, ChainJoint.End, 0f, 198f) });
            var approach = new ChainApproach { StopS = 198f, WithChain = true, Control = control, Flow = flow, Queue = queue };
            approach.Lanes.Add(new ApproachLane { Lateral = 1.75f, Straight = true, Left = true });
            if (lanes == 2)
                approach.Lanes.Add(new ApproachLane { Lateral = 5.25f, Straight = true, Right = true });
            return (chain, approach);
        }

        private static AutoOptions Options(params (string Key, string Value)[] settings)
        {
            GuideSpec spec = Template.Default().With(AutoMarking.EndsKey, "1");
            foreach ((string key, string value) in settings)
                spec = spec.With(key, value);
            return AutoMarking.OptionsOf(spec);
        }

        private static float GuideOf(AutoPlan plan) => plan.Runs.Find(r => r.Id == "mk.line.guidelane")?.Length ?? 0f;

        private static int ArrowsOf(AutoPlan plan) => plan.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal)).Count;

        private static void GuideLength()
        {
            float Guide(float kmh, float flow, float queue, float back = 196f)
            {
                (RoadChain chain, ChainApproach approach) = Approach(kmh, flow, queue);
                return AutoMarking.RoadGuide(approach, chain.Pieces[0], back);
            }

            Program.Near(Guide(50f, float.NaN, float.NaN), 30f, 1e-3f, "a city road, nothing known of its traffic: the standard's 30 m");
            Program.Near(Guide(60f, float.NaN, float.NaN), 40f, 1e-3f, "60 km/h: 40 m");
            Program.Near(Guide(80f, float.NaN, float.NaN), 50f, 1e-3f, "80 km/h: 50 m");
            Program.Near(Guide(50f, 1f, 47f), 60f, 1e-3f, "a queue of 47 m: 10 m more, rounded up to 5 m");
            Program.Near(Guide(50f, 1f, 12f), 30f, 1e-3f, "a short queue: 30 m");
            Program.Near(Guide(50f, 0.6f, 0f), 75f, 1e-3f, "at 0.6 of the free speed: halfway between 30 m and 120 m");
            Program.Near(Guide(50f, 0.95f, 0f), 30f, 1e-3f, "free flowing: 30 m");
            Program.Near(Guide(50f, 0.1f, 300f), AutoMarking.MaxRoadGuide, 1e-3f, "a jam and a long queue: at most 120 m");
            Program.Near(Guide(50f, float.NaN, 80f, 60f), 45f, 1e-3f, "60 m to the junction before: a quarter of it is left for changing lanes");
            Program.Near(Guide(50f, float.NaN, float.NaN, 34f), 29f, 1e-3f, "34 m: the lines as long as the approach allows (5 m short of it)");
            Program.Near(AutoMarking.Congestion(float.NaN), 0f, 1e-6f, "unknown traffic is no congestion");
            Program.Near(AutoMarking.Congestion(0.3f), 1f, 1e-6f, "at 0.3 of the free speed: full");
        }

        private static void PlanAtApproach()
        {
            (RoadChain chain, ChainApproach approach) = Approach(50f, 0.95f, 47f);
            AutoPlan plan = AutoMarking.Plan(chain, new[] { approach }, Options());
            Program.Near(GuideOf(plan), 60f, 1e-3f, "by the road: the queue's 60 m");
            Program.Check(ArrowsOf(plan) == 6, "guide lines of 50 m or more: the preview group too (three groups of two): " + ArrowsOf(plan));
            AutoEnd end = plan.Ends.Count == 1 ? plan.Ends[0] : null;
            Program.Check(end != null && end.Rank == 0 && Math.Abs(end.Guide - 60f) < 1e-3f && end.GuideByRoad && end.ArrowsByRoad && end.Groups == 3 && Math.Abs(end.Queue - 47f) < 1e-3f,
                "what was drawn at the approach is kept for the panel");

            (RoadChain quiet, ChainApproach calm) = Approach(50f, 0.95f, 0f);
            AutoPlan plain = AutoMarking.Plan(quiet, new[] { calm }, Options());
            Program.Check(Math.Abs(GuideOf(plain) - 30f) < 1e-3f && ArrowsOf(plain) == 4, "a quiet city road: 30 m and two groups");
            (RoadChain fast, ChainApproach quick) = Approach(60f);
            Program.Check(ArrowsOf(AutoMarking.Plan(fast, new[] { quick }, Options())) == 6, "60 km/h: the preview group");
            (RoadChain slow, ChainApproach jammed) = Approach(50f, 0.7f, 0f);
            Program.Check(ArrowsOf(AutoMarking.Plan(slow, new[] { jammed }, Options())) == 6, "a congested approach: the preview group");

            // One lane that nothing controls: nothing to choose between, no arrows; with signals they are drawn.
            (RoadChain lane, ChainApproach single) = Approach(50f, float.NaN, float.NaN, 1, JunctionControl.None);
            AutoPlan none = AutoMarking.Plan(lane, new[] { single }, Options());
            Program.Check(ArrowsOf(none) == 0 && none.Notes.Exists(n => n.Contains("one lane, no signals")), "one lane, no signals: no arrows, and the note says why");
            (RoadChain lit, ChainApproach signalled) = Approach(50f, float.NaN, float.NaN, 1, JunctionControl.Signals);
            Program.Check(ArrowsOf(AutoMarking.Plan(lit, new[] { signalled }, Options())) > 0, "one lane with signals: arrows");
            Program.Check(ArrowsOf(AutoMarking.Plan(lane, new[] { single }, Options((AutoMarking.ArrowsKey, "1")))) > 0, "set to two groups: drawn there too");

            // The fixed settings are as before 0.12.45.
            AutoPlan fixedPlan = AutoMarking.Plan(chain, new[] { approach }, Options((AutoMarking.GuideModeKey, "fixed"), (AutoMarking.GuideKey, "35"), (AutoMarking.ArrowsKey, "1"), (AutoMarking.PreviewKey, "0")));
            Program.Check(Math.Abs(GuideOf(fixedPlan) - 35f) < 1e-3f && ArrowsOf(fixedPlan) == 4 && !fixedPlan.Ends[0].GuideByRoad && !fixedPlan.Ends[0].ArrowsByRoad, "fixed: 35 m, two groups, whatever the queue");
            Program.Check(ArrowsOf(AutoMarking.Plan(fast, new[] { quick }, Options((AutoMarking.PreviewKey, "0")))) == 4, "the preview off: none at 60 km/h");
            // An approach's own length is the player's.
            Program.Near(GuideOf(AutoMarking.Plan(chain, new[] { approach }, Options((AutoMarking.ApproachGuideKey(0), "40")))), 40f, 1e-3f, "an approach's own 40 m before the road's 60 m");
        }

        private static void AddedLane()
        {
            // One lane each way for 100 m, then two each way to a signalled junction at 200 m: the right lane begins at the node at 100 m.
            var up = Piece(0f, 100f, 50f, new[] { 1.75f }, new[] { -1.75f }, ChainJoint.Pass, 0f, 100f);
            var down = Piece(100f, 200f, 50f, new[] { 1.75f, 5.25f }, new[] { -1.75f, -5.25f }, ChainJoint.End, 0f, 98f);
            var chain = new RoadChain(new[] { up, down });
            var approach = new ChainApproach { StopS = 198f, WithChain = true, Control = JunctionControl.Signals, PieceIndex = 1 };
            approach.Lanes.Add(new ApproachLane { Lateral = 1.75f, Straight = true, Left = true });
            approach.Lanes.Add(new ApproachLane { Lateral = 5.25f, Right = true });
            AutoPlan plan = AutoMarking.Plan(chain, new[] { approach }, Options());
            List<AutoStamp> atStart = plan.Stamps.FindAll(s => s.Id.StartsWith("mk.arrow.", StringComparison.Ordinal) && Math.Abs(s.S - (100f + AutoMarking.ArrowAfterAdded)) < 1e-3f);
            Program.Check(atStart.Count == 1 && atStart[0].Id == "mk.arrow.right" && Math.Abs(atStart[0].Lateral - 5.25f) < 1e-3f && atStart[0].WithChain,
                "the added lane's arrow where it begins (5.13.6), not the lane that goes on: " + atStart.Count);
            AutoPlan two = AutoMarking.Plan(chain, new[] { approach }, Options((AutoMarking.ArrowsKey, "1")));
            Program.Check(!two.Stamps.Exists(s => Math.Abs(s.S - (100f + AutoMarking.ArrowAfterAdded)) < 1e-3f), "two groups set by hand: none there");

            // Added after the first group (a lane that begins at the stop line): no arrow of its own.
            var upNear = Piece(0f, 194f, 50f, new[] { 1.75f }, new[] { -1.75f }, ChainJoint.Pass, 0f, 194f);
            var downNear = Piece(194f, 200f, 50f, new[] { 1.75f, 5.25f }, new[] { -1.75f, -5.25f }, ChainJoint.End, 0f, 4f);
            var near = new RoadChain(new[] { upNear, downNear });
            var late = new ChainApproach { StopS = 198f, WithChain = true, Control = JunctionControl.Signals, PieceIndex = 1 };
            late.Lanes.AddRange(approach.Lanes);
            Program.Check(!AutoMarking.Plan(near, new[] { late }, Options()).Stamps.Exists(s => Math.Abs(s.S - (194f + AutoMarking.ArrowAfterAdded)) < 1e-3f), "a lane added after the first group: no arrow of its own");
        }

        private static void SettingsAndText()
        {
            GuideTemplate t = Template;
            AutoOptions defaults = AutoMarking.OptionsOf(t.Default());
            Program.Check(defaults.GuideByRoad && defaults.ArrowsByRoad && defaults.PreviewByRoad && defaults.Arrows && !defaults.Preview, "by default all three by the road");
            AutoOptions off = Options((AutoMarking.ArrowsKey, "0"));
            Program.Check(!off.Arrows && !off.ArrowsByRoad, "no arrows");
            GuideSpec spec = t.Default().With(AutoMarking.EndsKey, "1");
            Func<GuideSpec, string, bool> shown = (s, key) => t.FieldOf(key).Visible == null || t.FieldOf(key).Visible(s);
            Program.Check(!shown(spec, AutoMarking.GuideKey) && shown(spec.With(AutoMarking.GuideModeKey, "fixed"), AutoMarking.GuideKey), "the length is typed only for a fixed length");
            Program.Check(shown(spec, AutoMarking.PreviewKey) && !shown(spec.With(AutoMarking.ArrowsKey, "0"), AutoMarking.PreviewKey), "the preview setting goes with the arrows");

            var end = new AutoEnd { Rank = 1, Guide = 60f, Groups = 3, GuideByRoad = true, ArrowsByRoad = true, Kmh = 50f, Flow = 0.62f, Queue = 47f };
            string zh = HintText.AutoEnd(end, true), en = HintText.AutoEnd(end, false);
            Program.Check(zh.StartsWith("进口2（粉圈）", StringComparison.Ordinal) && zh.Contains("导向车道线 60 m（按道路状态）") && zh.Contains("箭头 3 组") && zh.Contains("排队 47 m") && zh.Contains("通行速度 62%") && zh.Contains("限速 50 km/h"), zh);
            Program.Check(en.StartsWith("Approach 2 (pink)", StringComparison.Ordinal) && en.Contains("60 m (by the road)") && en.Contains("a queue of 47 m"), en);
            string bare = HintText.AutoEnd(new AutoEnd { Rank = 0, Guide = 0f, Groups = 0 }, true);
            Program.Check(bare.Contains("无导向车道线") && bare.Contains("无箭头") && !bare.Contains("；"), "nothing drawn, nothing known: " + bare);
        }
    }
}
