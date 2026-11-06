using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The automatic marking's second part (0.12.2, the owner's "继续做…"): what changes along a stretch and what is special in it.
    /// - Where the lanes change at a node (7.1): a line of the same kind on both sides of the node at another place across is one line that moves over in a smooth S (MarkingTransition) over the transition's
    ///   length (formula 1 and Table 4 when the road gets narrower in the traffic's direction, 20 m to 30 m when it gets wider: 25 m); the line of a lane that ends becomes the change line (线9, 5.7.1, 7.1.3:
    ///   1 m on 1 m off) from ChangeLead metres before the transition to its end; the line of a lane that begins starts where the transition ends, and near a junction it is the change line from the taper
    ///   to the guide lane line (5.7.3).
    /// - Where a driver cannot see far enough to overtake on a road of one lane each way (6.1.3, 6.1.4; Table A.1's overtaking sight distance, SightDistance from the road's own curves and crests): the crossable
    ///   centre line (线1) becomes the yellow solid-and-dashed line (线44) with its dashed side for the traffic that may overtake, or the single yellow solid line (线45) where neither may.
    /// - Bus lanes (6.9.1): the lines beside them are the bus lane line (线59, 5 m on 1 m off, 20 cm), an outermost bus lane keeps its edge line, and the word 公交 (5.14) is at the start of the lane after each
    ///   junction and again every BusWordEvery metres. Cycle lanes (5.4.5): the line between them and the motor lanes is an edge line (white solid), whatever the kerb rule says, and the cycle lane's own outer
    ///   edge has one where no kerb marks it; the cycle mark with its arrow (线35, 线36; 5.15.1: half the lane wide, to 10 cm) at the start of the lane and every BikeMarkEvery metres; the stop line covers
    ///   them (6.4.3).
    /// - A crosswalk away from the junctions (at a node the stretch goes on through): the lines along break there (5.8.5); with the game's crossing lines cleared, the crosswalk (线10, 3 m at least across a
    ///   segment, 5.8.3), a stop line before it each way (5.8.6, 1.5 m: 6.4.4) and, where no signal controls it, the warning diamonds (线11, Fig. 14) in each lane.
    /// Pure.
    /// </summary>
    public static partial class AutoMarking
    {
        /// <summary>The keys of the settings of 0.12.2.</summary>
        public const string TaperKey = "taper", SightKey = "sight", SpecialKey = "special", MidCrossKey = "midcross", WaitingKey = "waiting", JunctionGuideKey = "jguide";

        /// <summary>The largest move across of a line at a node that is a transition (m): more is two lines.</summary>
        public const float MaxShift = 4.5f;

        /// <summary>How far before a transition a lane that ends has the change line (m; 7.1.3 says from the warning sign, which is not placed here: this mod's figure).</summary>
        public const float ChangeLead = 30f;

        /// <summary>How near a junction (m) a lane that begins is a turn lane, whose line is the change line to the guide lane line (5.7.3): this mod's figure.</summary>
        public const float TurnLaneReach = 200f;

        /// <summary>The shortest no-passing or passing zone kept (m): a shorter one is made the stricter of its neighbours.</summary>
        public const float MinZone = 30f;

        /// <summary>How far after the start of a bus or cycle lane its first word or mark is, and how often it is repeated (m; 6.9.1 asks it again after every junction and "at suitable places" between).</summary>
        public const float MarkLead = 10f, BusWordEvery = 300f, BikeMarkEvery = 150f;

        /// <summary>Where the crossing warning diamonds are before the stop line of a crosswalk away from a junction (m, their middles; Fig. 14: 30 to 50, and 10 to 20 between the two).</summary>
        public const float DiamondNear = 30f, DiamondGap = 15f;

        private static bool IsCentre(string id) => id == "mk.line.centre" || id == "mk.line.doubleyellow" || id == "mk.line.yellowsd" || id == "mk.line.yellowsingle";

        private static string SettingsText(AutoRun run) => string.Join(";", run.Settings.ConvertAll(kv => kv.Key + "=" + kv.Value));

        // ------------------------------------------------------------------ bus and cycle lanes in the lines of a piece

        private static bool Near(List<float> values, float x)
        {
            foreach (float v in values)
            {
                if (Math.Abs(v - x) <= MarkingSnap.MergeWithin)
                    return true;
            }

            return false;
        }

        /// <summary>The outermost motor lane on a side is a bus lane, or a cycle lane lies beyond the motor lanes on it: its edge line is drawn whatever the kerb rule says.</summary>
        private static bool ForcedEdge(ChainPiece piece, bool outerRight, bool outerLeft)
        {
            var all = new List<float>(piece.With);
            all.AddRange(piece.Against);
            List<float> lanes = MarkingSnap.Merged(all);
            if (lanes.Count == 0)
                return false;
            var bus = new List<float>(piece.BusWith);
            bus.AddRange(piece.BusAgainst);
            if (outerRight && (piece.BikeWith.Count > 0 && piece.BikeWith.Exists(b => b > lanes[lanes.Count - 1]) || Near(bus, lanes[lanes.Count - 1])))
                return true;
            if (outerLeft && (piece.BikeAgainst.Count > 0 && piece.BikeAgainst.Exists(b => b < lanes[0]) || Near(bus, lanes[0])))
                return true;
            return false;
        }

        /// <summary>The lines beside a bus lane are the bus lane line; the cycle lanes' outer edges have their lines where no kerb marks them.</summary>
        private static void SpecialLines(ChainPiece piece, List<StandardLine> lines, AutoOptions options)
        {
            var bus = new List<float>(piece.BusWith);
            bus.AddRange(piece.BusAgainst);
            if (bus.Count > 0)
            {
                var all = new List<float>(piece.With);
                all.AddRange(piece.Against);
                List<float> lanes = MarkingSnap.Merged(all);
                for (int i = 0; i < lines.Count; i++)
                {
                    StandardLine line = lines[i];
                    if (line.Id != "mk.line.lane")
                        continue;
                    float below = float.NaN, above = float.NaN;
                    foreach (float lane in lanes)
                    {
                        if (lane < line.Lateral && (float.IsNaN(below) || lane > below))
                            below = lane;
                        if (lane > line.Lateral && (float.IsNaN(above) || lane < above))
                            above = lane;
                    }

                    if ((!float.IsNaN(below) && Near(bus, below)) || (!float.IsNaN(above) && Near(bus, above)))
                        lines[i] = new StandardLine { Lateral = line.Lateral, Id = "mk.line.reserved", Key = "", Value = "" };
                }
            }

            if (options.Edges == EdgeLines.None)
                return;
            float half = Math.Max(0.5f, piece.BikeWidth * 0.5f);
            if (piece.BikeWith.Count > 0 && !piece.KerbRight)
            {
                float outer = float.MinValue;
                foreach (float b in piece.BikeWith)
                    outer = Math.Max(outer, b + half);
                lines.Add(new StandardLine { Lateral = outer, Id = "mk.line.edge", Key = "side", Value = "r" });
            }

            if (piece.BikeAgainst.Count > 0 && !piece.KerbLeft)
            {
                float outer = float.MaxValue;
                foreach (float b in piece.BikeAgainst)
                    outer = Math.Min(outer, b - half);
                lines.Add(new StandardLine { Lateral = outer, Id = "mk.line.edge", Key = "side", Value = "r" });
            }
        }

        /// <summary>A stop line over the lanes of a direction (lateral and span in the chain's frame) made to cover the cycle lanes on that side too (6.4.3).</summary>
        private static void CoverBikes(ChainPiece piece, bool withChain, ref float lateral, ref float span)
        {
            List<float> bikes = withChain ? piece.BikeWith : piece.BikeAgainst;
            if (bikes.Count == 0 || !(span > 0f))
                return;
            float from = lateral - span * 0.5f, to = lateral + span * 0.5f, half = Math.Max(0.5f, piece.BikeWidth * 0.5f);
            foreach (float b in bikes)
            {
                from = Math.Min(from, b - half);
                to = Math.Max(to, b + half);
            }

            lateral = (from + to) * 0.5f;
            span = to - from;
        }

        // ------------------------------------------------------------------ 7.1 transitions and 5.7 change lines

        private static void Transitions(RoadChain chain, AutoPlan plan)
        {
            for (int i = 0; i + 1 < chain.Pieces.Count; i++)
            {
                ChainPiece prev = chain.Pieces[i], next = chain.Pieces[i + 1];
                if (prev.After != ChainJoint.Pass)
                    continue;
                float node = prev.S1;
                float kmh = Math.Max(prev.HasSpeed ? prev.Kmh : 50f, next.HasSpeed ? next.Kmh : 50f);
                var ending = plan.Runs.FindAll(r => Math.Abs(r.S1 - node) < 0.05f && r.S0 < node - 0.5f && !r.Shifts);
                var starting = plan.Runs.FindAll(r => Math.Abs(r.S0 - node) < 0.05f && r.S1 > node + 0.5f && !r.Shifts);

                // The pairs: a line of the same kind and settings each side of the node, nearest first.
                var pairs = new List<(AutoRun A, AutoRun B, float D)>();
                foreach (AutoRun a in ending)
                {
                    foreach (AutoRun b in starting)
                    {
                        float d = Math.Abs(a.Lateral - b.Lateral);
                        bool sameSide = IsCentre(a.Id) || Math.Sign(a.Lateral) == Math.Sign(b.Lateral);
                        if (a.Id == b.Id && SettingsText(a) == SettingsText(b) && d > LineJoin && d <= MaxShift && sameSide)
                            pairs.Add((a, b, d));
                    }
                }

                pairs.Sort((p, q) => p.D.CompareTo(q.D));
                var used = new HashSet<AutoRun>();
                // The transition of the edge line on each side, for the change line of a lane that ends there (the formula for a lane's width when there is none).
                float drop = MarkingTransition.Length(kmh, MarkingSnap.DefaultLane, true), dropRight = drop, dropLeft = drop;
                foreach ((AutoRun a, AutoRun b, float d) in pairs)
                {
                    if (used.Contains(a) || used.Contains(b))
                        continue;
                    used.Add(a);
                    used.Add(b);
                    // Which way the road gets narrower for the traffic this line belongs to, and so where the move is (TransitionPlace).
                    TransitionPlace(a.Lateral, b.Lateral, IsCentre(a.Id), out bool narrowing, out bool after);
                    float length = narrowing ? MarkingTransition.Length(kmh, d, true) : MarkingTransition.WideningDefault;
                    float s0 = after ? node : node - length, s1 = after ? node + length : node;
                    // Kept on the line's own two pieces, with a little left over.
                    float lo = a.S0 + 2f, hi = b.S1 - 2f;
                    s0 = Math.Max(lo, s0);
                    s1 = Math.Min(hi, s1);
                    if (s1 - s0 < 0.5f * length)
                        plan.Notes.Add($"the transition at {node:0} m of the {a.Id} is {Math.Max(0f, s1 - s0):0} m (7.1.2 asks {length:0} m): the segments are short");
                    if (s1 - s0 < 1f)
                    {
                        s0 = Math.Max(lo, node - 0.5f);
                        s1 = Math.Min(hi, node + 0.5f);
                    }

                    if (a.Id == "mk.line.edge" && narrowing)
                    {
                        if (a.Lateral > 0f)
                            dropRight = length;
                        else
                            dropLeft = length;
                    }

                    var merged = Copy(a);
                    merged.S1 = b.S1;
                    merged.ShiftS0 = s0;
                    merged.ShiftS1 = s1;
                    merged.ShiftTo = b.Lateral;
                    merged.AddedAt = b.AddedAt;
                    plan.Runs.Remove(a);
                    plan.Runs.Remove(b);
                    plan.Runs.Add(merged);
                }

                // A lane line that ends at the node, or begins there, without a line on the other side: a lane ends or begins (on the right of the chain the traffic runs with it, on the left against).
                foreach (AutoRun a in ending)
                {
                    if (used.Contains(a) || (a.Id != "mk.line.lane" && a.Id != "mk.line.reserved"))
                        continue;
                    if (a.Lateral > 0f)
                        ChangeBefore(plan, a, node, -1f, dropRight);
                    else
                        Begin(plan, a, node, -1f);
                }

                foreach (AutoRun b in starting)
                {
                    if (used.Contains(b) || (b.Id != "mk.line.lane" && b.Id != "mk.line.reserved"))
                        continue;
                    if (b.Lateral > 0f)
                        Begin(plan, b, node, 1f);
                    else
                        ChangeBefore(plan, b, node, 1f, dropLeft);
                }
            }
        }

        /// <summary>
        /// Where a line that moves from a (before the node) to b (after it) moves: for a line on the right of the chain (its traffic runs with it) the road gets narrower when the line comes in (b below a)
        /// and the move ends at the node; it gets wider when the line goes out, and the move starts at the node. On the left (the traffic runs against the chain, from b to a) the other way round. A centre line
        /// narrows one way: the move is on the side of the node that way comes from.
        /// </summary>
        public static void TransitionPlace(float a, float b, bool centre, out bool narrowing, out bool after)
        {
            if (centre)
            {
                narrowing = true;
                after = b < a;
                return;
            }

            narrowing = b < a;
            after = a + b > 0f ? !narrowing : narrowing;
        }

        /// <summary>The line of a lane that ends at the node (on the side dir, -1 before it, +1 after it in the chain's distance) is the change line for the transition and ChangeLead before it.</summary>
        private static void ChangeBefore(AutoPlan plan, AutoRun run, float node, float side, float transition)
        {
            float reach = transition + ChangeLead;
            float cut = node + side * reach;
            plan.Runs.Remove(run);
            AutoRun change = Copy(run);
            change.Id = "mk.line.change";
            change.Settings.Clear();
            change.AddedAt = float.NaN;
            if (side < 0f)
            {
                cut = Math.Max(run.S0, cut);
                change.S0 = cut;
                if (cut - run.S0 >= MarkingRuns.MinLength)
                {
                    AutoRun rest = Copy(run);
                    rest.S1 = cut;
                    plan.Runs.Add(rest);
                }
            }
            else
            {
                cut = Math.Min(run.S1, cut);
                change.S1 = cut;
                if (run.S1 - cut >= MarkingRuns.MinLength)
                {
                    AutoRun rest = Copy(run);
                    rest.S0 = cut;
                    plan.Runs.Add(rest);
                }
            }

            if (change.Length >= MarkingRuns.MinLength)
                plan.Runs.Add(change);
        }

        /// <summary>The line of a lane that begins at the node (on the side, as ChangeBefore) starts where the widening ends; it remembers where the lane began.</summary>
        private static void Begin(AutoPlan plan, AutoRun run, float node, float side)
        {
            float length = MarkingTransition.WideningDefault;
            if (side > 0f)
                run.S0 = Math.Min(run.S1, node + length);
            else
                run.S1 = Math.Max(run.S0, node - length);
            run.AddedAt = node;
            if (run.Length < MarkingRuns.MinLength)
                plan.Runs.Remove(run);
        }

        /// <summary>At a junction the lane line of a lane that began within TurnLaneReach of it is the change line from where the lane began to the guide lane line (5.7.3).</summary>
        private static void ChangeToGuide(AutoPlan plan, float lateral, float guideStart, ChainApproach approach)
        {
            float dir = approach.Dir;
            foreach (AutoRun run in plan.Runs)
            {
                if ((run.Id != "mk.line.lane" && run.Id != "mk.line.reserved") || float.IsNaN(run.AddedAt))
                    continue;
                float before = (guideStart - run.AddedAt) * dir;
                if (before <= 0f || before > TurnLaneReach || Math.Abs(run.LateralAt(guideStart - dir * 1f) - lateral) > LineJoin)
                    continue;
                bool touches = dir > 0f ? Math.Abs(run.S1 - guideStart) < 0.5f : Math.Abs(run.S0 - guideStart) < 0.5f;
                if (!touches)
                    continue;
                run.Id = "mk.line.change";
                run.Settings.Clear();
                if (dir > 0f)
                    run.S0 = Math.Min(run.S0, run.AddedAt);
                else
                    run.S1 = Math.Max(run.S1, run.AddedAt);
                run.AddedAt = float.NaN;
            }
        }

        // ------------------------------------------------------------------ 6.1.3, 6.1.4 no passing where the sight is short

        /// <summary>The class of a stretch of a centre line of one lane each way: 0 both may overtake (线1), 1 the traffic with the chain may (线44, dashed on its side), 2 against it (线44 the other side), 3 none (线45).</summary>
        public static int PassingClass(bool withMay, bool againstMay) => withMay && againstMay ? 0 : withMay ? 1 : againstMay ? 2 : 3;

        /// <summary>
        /// The classes of the places of a stretch made into zones: a zone shorter than MinZone (in places Step apart) takes the stricter class of its neighbours (3 strictest, then 1 and 2, then 0). In place.
        /// </summary>
        public static void SmoothZones(int[] classes, float step)
        {
            int minPlaces = Math.Max(1, (int)Math.Ceiling(MinZone / step));
            for (int pass = 0; pass < 3; pass++)
            {
                int i = 0;
                while (i < classes.Length)
                {
                    int j = i;
                    while (j + 1 < classes.Length && classes[j + 1] == classes[i])
                        j++;
                    int count = j - i + 1;
                    if (count < minPlaces && !(i == 0 && j == classes.Length - 1))
                    {
                        int left = i > 0 ? classes[i - 1] : -1, right = j + 1 < classes.Length ? classes[j + 1] : -1;
                        int to = Strict(left) >= Strict(right) ? left : right;
                        if (to >= 0 && Strict(to) > Strict(classes[i]))
                        {
                            for (int k = i; k <= j; k++)
                                classes[k] = to;
                        }
                    }

                    i = j + 1;
                }
            }
        }

        private static int Strict(int c) => c == 3 ? 3 : c == 1 || c == 2 ? 2 : c == 0 ? 1 : 0;

        private static void NoPassing(RoadChain chain, AutoPlan plan)
        {
            List<AutoRun> centres = plan.Runs.FindAll(r => r.Id == "mk.line.centre");
            if (centres.Count == 0)
                return;
            float[] withSight = null, againstSight = null;
            float maxNeed = 0f;
            foreach (ChainPiece piece in chain.Pieces)
            {
                float m = SightDistance.Overtaking(piece.HasSpeed ? piece.Kmh : 50f);
                if (!float.IsNaN(m))
                    maxNeed = Math.Max(maxNeed, m);
            }
            if (maxNeed <= 0f)
                return;
            foreach (AutoRun run in centres)
            {
                ChainPiece mid = chain.Pieces[chain.PieceAt((run.S0 + run.S1) * 0.5f)];
                if (mid.With.Count == 0 || mid.Against.Count == 0)
                    continue;
                if (withSight == null)
                {
                    float driver = Average(mid.With), oncoming = Average(mid.Against);
                    Func<float, float> room = s => Math.Max(2f, chain.Pieces[chain.PieceAt(s)].HalfWidth);
                    withSight = SightDistance.Ahead(chain, true, driver, oncoming, room, maxNeed);
                    againstSight = SightDistance.Ahead(chain, false, oncoming, driver, room, maxNeed);
                }

                int k0 = (int)Math.Ceiling(run.S0 / SightDistance.Step), k1 = Math.Min(withSight.Length - 1, (int)Math.Floor(run.S1 / SightDistance.Step));
                if (k1 < k0)
                    continue;
                var classes = new int[k1 - k0 + 1];
                for (int k = k0; k <= k1; k++)
                {
                    ChainPiece at = chain.Pieces[chain.PieceAt(k * SightDistance.Step)];
                    float need = SightDistance.Overtaking(at.HasSpeed ? at.Kmh : 50f);
                    classes[k - k0] = float.IsNaN(need) ? 0 : PassingClass(withSight[k] >= need - 1e-3f, againstSight[k] >= need - 1e-3f);
                }

                SmoothZones(classes, SightDistance.Step);
                bool any = false;
                foreach (int c in classes)
                    any |= c != 0;
                if (!any)
                    continue;

                // The run in zones: each from the middle between the last place of the one before and its first place.
                plan.Runs.Remove(run);
                int start = 0;
                for (int i = 1; i <= classes.Length; i++)
                {
                    if (i < classes.Length && classes[i] == classes[start])
                        continue;
                    float a = start == 0 ? run.S0 : (k0 + start - 0.5f) * SightDistance.Step;
                    float b = i == classes.Length ? run.S1 : (k0 + i - 0.5f) * SightDistance.Step;
                    AutoRun zone = Copy(run);
                    zone.S0 = Math.Max(run.S0, a);
                    zone.S1 = Math.Min(run.S1, b);
                    zone.Settings.Clear();
                    int c = classes[start];
                    zone.Id = c == 0 ? "mk.line.centre" : c == 3 ? "mk.line.yellowsingle" : "mk.line.yellowsd";
                    if (c == 1 || c == 2)
                        zone.Settings.Add(new KeyValuePair<string, string>("side", c == 1 ? "r" : "l"));
                    if (zone.Length >= MarkingRuns.MinLength)
                        plan.Runs.Add(zone);
                    start = i;
                }
            }
        }

        private static float Average(List<float> values)
        {
            float sum = 0f;
            foreach (float v in values)
                sum += v;
            return values.Count == 0 ? 0f : sum / values.Count;
        }

        // ------------------------------------------------------------------ 6.9.1, 5.15.2 the marks of bus and cycle lanes

        /// <summary>A lane of a kind along the chain: where it is across and the stretch it runs over (through the nodes where it stays at its place).</summary>
        private struct LaneSpan
        {
            public float Lateral, S0, S1;
            public bool WithChain;
        }

        private static List<LaneSpan> Spans(RoadChain chain, Func<ChainPiece, List<float>> lanes, bool withChain)
        {
            var spans = new List<LaneSpan>();
            var open = new List<LaneSpan>();
            for (int i = 0; i < chain.Pieces.Count; i++)
            {
                ChainPiece piece = chain.Pieces[i];
                bool through = i > 0 && chain.Pieces[i - 1].After == ChainJoint.Pass;
                var next = new List<LaneSpan>();
                foreach (float x in MarkingSnap.Merged(lanes(piece)))
                {
                    int k = through ? open.FindIndex(o => Math.Abs(o.Lateral - x) <= LineJoin) : -1;
                    if (k >= 0)
                    {
                        LaneSpan o = open[k];
                        open.RemoveAt(k);
                        o.S1 = piece.LanesS1At(x);
                        next.Add(o);
                    }
                    else
                    {
                        next.Add(new LaneSpan { Lateral = x, S0 = piece.LanesS0At(x), S1 = piece.LanesS1At(x), WithChain = withChain });
                    }
                }

                spans.AddRange(open);
                open = next;
            }

            spans.AddRange(open);
            return spans;
        }

        private static void SpecialLanes(RoadChain chain, AutoPlan plan)
        {
            foreach (bool with in new[] { true, false })
            {
                foreach (LaneSpan span in Spans(chain, p => with ? p.BusWith : p.BusAgainst, with))
                    Marks(chain, plan, span, "mk.text", BusWordEvery, new KeyValuePair<string, string>("text", "公交"));
                foreach (LaneSpan span in Spans(chain, p => with ? p.BikeWith : p.BikeAgainst, with))
                {
                    ChainPiece piece = chain.Pieces[chain.PieceAt((span.S0 + span.S1) * 0.5f)];
                    // 5.15.1: half the lane wide, to 10 cm.
                    int width = (int)Math.Round(Math.Max(0.6f, piece.BikeWidth * 0.5f) * 10f) * 10;
                    Marks(chain, plan, span, "mk.sym.bicycle", BikeMarkEvery, new KeyValuePair<string, string>("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture)), new KeyValuePair<string, string>("arrow", "1"));
                }
            }
        }

        /// <summary>A mark at the start of a lane (MarkLead into it, the way its traffic runs) and again every so many metres, while 20 m of the lane are left after it.</summary>
        private static void Marks(RoadChain chain, AutoPlan plan, LaneSpan span, string id, float every, params KeyValuePair<string, string>[] settings)
        {
            if (span.S1 - span.S0 < MarkLead + 20f)
                return;
            float dir = span.WithChain ? 1f : -1f, start = span.WithChain ? span.S0 : span.S1, end = span.WithChain ? span.S1 : span.S0;
            for (float d = MarkLead; (end - (start + dir * d)) * dir >= 20f; d += every)
            {
                var stamp = new AutoStamp { Id = id, S = start + dir * d, Lateral = span.Lateral, WithChain = span.WithChain };
                stamp.Settings.AddRange(settings);
                plan.Stamps.Add(stamp);
            }
        }

        // ------------------------------------------------------------------ 5.8 crosswalks away from the junctions

        /// <summary>The lines from s0 to s1 are cut out (those whose lateral there is within the range, all of them by default).</summary>
        private static void CutAll(AutoPlan plan, float s0, float s1, float lateralFrom = float.NegativeInfinity, float lateralTo = float.PositiveInfinity)
        {
            for (int i = plan.Runs.Count - 1; i >= 0; i--)
            {
                AutoRun run = plan.Runs[i];
                if (run.S1 <= s0 || run.S0 >= s1)
                    continue;
                float at = run.LateralAt(Math.Max(run.S0, Math.Min(run.S1, (s0 + s1) * 0.5f)));
                if (at < lateralFrom || at > lateralTo)
                    continue;
                plan.Runs.RemoveAt(i);
                if (s0 - run.S0 >= MarkingRuns.MinLength)
                {
                    AutoRun a = Copy(run);
                    a.S1 = s0;
                    plan.Runs.Add(a);
                }

                if (run.S1 - s1 >= MarkingRuns.MinLength)
                {
                    AutoRun b = Copy(run);
                    b.S0 = s1;
                    plan.Runs.Add(b);
                }
            }
        }

        private static void Crossings(RoadChain chain, AutoPlan plan, AutoOptions options)
        {
            foreach (ChainCrossing c in chain.Crossings)
            {
                CutAll(plan, c.S0 - CrosswalkClear, c.S1 + CrosswalkClear);
                plan.Stamps.RemoveAll(st => st.S >= c.S0 - 12f && st.S <= c.S1 + 12f && st.Id != "mk.crosswalk");
                if (!options.CrossLines)
                {
                    plan.Notes.Add($"the crosswalk at {c.Middle:0} m: the lines break there; the game's crosswalk is kept (clear all of the game's markings to have the standard's)");
                    continue;
                }

                ChainPiece piece = chain.Pieces[chain.PieceAt(c.Middle)];
                MarkingSpot spot = MarkingSnap.Fit(MarkingAnchor.Road, piece.With, piece.Against, 0f, out float span, out int lanes);
                float lateral = spot.Lateral;
                CoverBikes(piece, true, ref lateral, ref span);
                CoverBikes(piece, false, ref lateral, ref span);
                if (span > 0.5f)
                {
                    var walk = new AutoStamp { Id = "mk.crosswalk", S = c.Middle, Lateral = lateral, WithChain = true, Span = span, Lanes = lanes };
                    // 5.8.3: 3 m at least across a segment, in whole metres.
                    walk.Settings.Add(new KeyValuePair<string, string>("len", Math.Max(3, (int)Math.Ceiling(c.S1 - c.S0 - 0.01f)).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    plan.Stamps.Add(walk);
                }

                float half = StopWidth * 0.5f;
                float centre = SideBoundary(piece, true);
                foreach (bool with in new[] { true, false })
                {
                    List<float> mine = with ? piece.With : piece.Against;
                    if (mine.Count == 0)
                        continue;
                    float dir = with ? 1f : -1f, near = with ? c.S0 : c.S1;
                    float stopS = near - dir * (StopBeforeCrosswalk + half), stopEdge = stopS - dir * half;
                    MarkingSpot stop = MarkingSnap.Fit(MarkingAnchor.Block, mine, with ? piece.Against : piece.With, Average(mine), out float stopSpan, out int stopLanes);
                    float stopLateral = stop.Lateral;
                    CoverBikes(piece, with, ref stopLateral, ref stopSpan);
                    if (stopSpan > 0.5f)
                        plan.Stamps.Add(new AutoStamp { Id = "mk.stop", S = stopS, Lateral = stopLateral, WithChain = with, Span = stopSpan, Lanes = stopLanes });
                    // The lines of this side end at the stop line.
                    float lo = Math.Min(stopEdge, near), hi = Math.Max(stopEdge, near);
                    if (float.IsNaN(centre))
                        CutAll(plan, lo, hi);
                    else if (with)
                        CutAll(plan, lo, hi, centre - 0.3f);
                    else
                        CutAll(plan, lo, hi, float.NegativeInfinity, centre + 0.3f);
                    if (c.Signals)
                        continue;
                    foreach (float lane in MarkingSnap.Merged(mine))
                    {
                        foreach (float back in new[] { DiamondNear, DiamondNear + DiamondGap })
                        {
                            float s = stopEdge - dir * back;
                            if (s > 0f && s < chain.Length)
                                plan.Stamps.Add(new AutoStamp { Id = "mk.diamond", S = s, Lateral = lane, WithChain = with });
                        }
                    }
                }
            }
        }

        /// <summary>The line between the two directions on a piece (as SideBoundary), seen from the traffic with the chain or against it.</summary>
        public static float SideBoundary(ChainPiece piece, bool withChain) => SideBoundary(piece, new ChainApproach { WithChain = withChain });
    }
}
