using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>How a junction controls the traffic of one approach, as its lanes say.</summary>
    public enum JunctionControl
    {
        None = 0,
        Signals = 1,
        Stop = 2,
        Yield = 3
    }

    /// <summary>A car lane of an approach to a junction: where it is across the chain (m right of the chain's direction) and where its traffic may go at the junction.</summary>
    public sealed class ApproachLane
    {
        public float Lateral;
        public bool Straight, Left, Right, UTurn;

        public int Directions => (Straight ? 1 : 0) + (Left ? 1 : 0) + (Right ? 1 : 0) + (UTurn ? 1 : 0);
    }

    /// <summary>The traffic of a chain that drives into a junction: where its car lanes end, which way it runs, how the junction controls it, its lanes and a crosswalk across it.</summary>
    public sealed class ChainApproach
    {
        /// <summary>The chain's distance (m) where the approach's car lanes end (the mouth of the junction).</summary>
        public float StopS;

        /// <summary>The traffic runs the chain's way (the junction is at the larger distance).</summary>
        public bool WithChain;

        public JunctionControl Control;

        /// <summary>How far beyond StopS (m, toward the junction; negative: before it) the near side of a crosswalk across the approach is; NaN when there is none.</summary>
        public float CrosswalkAt = float.NaN;

        /// <summary>
        /// 0.12.54: the game draws a line across the approach near its stop (a stop or give-way line of its road style). Keeping the game's lines across (AutoOptions.CrossLines false) leaves the stop line
        /// to it only where it has one: the North American style has none at signals.
        /// </summary>
        public bool GameCrossLine = true;

        /// <summary>
        /// 0.12.51: the near side of that crosswalk across the road (lateral of the chain, and how far beyond StopS it is there), from the chain's left to its right; a crosswalk at a slant is nearer at
        /// one side than at the other (CrosswalkAt is its nearest). Empty when there is none or it was not sampled.
        /// </summary>
        public readonly List<(float Lateral, float Near)> CrosswalkEdge = new List<(float Lateral, float Near)>();

        /// <summary>How far beyond StopS the crosswalk's near side is at a lateral (held at its ends beyond them); CrosswalkAt when it has no edge, NaN with no crosswalk.</summary>
        public float CrosswalkNearAt(float lateral)
        {
            if (CrosswalkEdge.Count == 0)
                return CrosswalkAt;
            if (lateral <= CrosswalkEdge[0].Lateral)
                return CrosswalkEdge[0].Near;
            for (int i = 1; i < CrosswalkEdge.Count; i++)
            {
                (float x0, float n0) = CrosswalkEdge[i - 1];
                (float x1, float n1) = CrosswalkEdge[i];
                if (lateral <= x1)
                    return x1 - x0 < 1e-4f ? Math.Min(n0, n1) : n0 + (n1 - n0) * (lateral - x0) / (x1 - x0);
            }

            return CrosswalkEdge[CrosswalkEdge.Count - 1].Near;
        }

        /// <summary>The crosswalk's nearest between two laterals (its edge's samples between them and the two ends); CrosswalkAt when it has no edge.</summary>
        public float CrosswalkNearest(float lo, float hi)
        {
            if (CrosswalkEdge.Count == 0)
                return CrosswalkAt;
            float nearest = Math.Min(CrosswalkNearAt(lo), CrosswalkNearAt(hi));
            foreach ((float x, float near) in CrosswalkEdge)
            {
                if (x > lo && x < hi)
                    nearest = Math.Min(nearest, near);
            }

            return nearest;
        }

        /// <summary>The approach's car lanes from the driver's left.</summary>
        public readonly List<ApproachLane> Lanes = new List<ApproachLane>();

        /// <summary>The direction along the chain the traffic runs: +1 with it, -1 against it.</summary>
        public float Dir => WithChain ? 1f : -1f;

        /// <summary>The piece of the chain the approach is on (the reader's), and how far beyond StopS the far side of the crosswalk is (NaN: none).</summary>
        public int PieceIndex = -1;
        public float CrosswalkFar = float.NaN;

        /// <summary>
        /// The left turns through the junction (0.12.2, for the junction guide lines of 5.6): for each approach lane from the driver's left that turns left, the path of its turn (world points, from the lane's
        /// end to the exit lane's start) and the lane's width; and whether the first of them is the approach's leftmost lane (its left line goes on from the centre line: yellow).
        /// </summary>
        public readonly List<List<Vec3>> LeftTurns = new List<List<Vec3>>();
        public readonly List<float> LeftWidths = new List<float>();
        public bool LeftFromLeftmost;

        /// <summary>A lane turns left only (no straight on, no right turn): a left-turn lane, which a waiting area is for (5.5.3).</summary>
        public bool HasLeftOnlyLane => Lanes.Exists(l => l.Left && !l.Straight && !l.Right);

        /// <summary>
        /// 0.12.45, the approach's traffic for the guide markings by the road's state: the mean speed ratio of its car lanes in the game's traffic flow (1 free, 0 at a standstill; an average over hours of
        /// the game; NaN when no car was measured on them), and the longest queue standing in one of its lanes when it was read (m from the lanes' end; 0 none; NaN not read).
        /// </summary>
        public float Flow = float.NaN, Queue = float.NaN;
    }

    /// <summary>When the junction guide lines (5.6, 线8) are drawn.</summary>
    public enum JunctionGuides
    {
        /// <summary>By 5.6.3: two left-turn lanes or more, or a large junction (a left turn of LargeTurn metres or more).</summary>
        Auto = 0,
        All = 1,
        None = 2
    }

    /// <summary>Which edge lines the automatic marking draws.</summary>
    public enum EdgeLines
    {
        /// <summary>By GB 5768.3 5.4.4 and 5.4.5: on roads of four lanes or more, and on a side without a footway (the road's edge is not marked by a kerb).</summary>
        Auto = 0,
        All = 1,
        None = 2
    }

    /// <summary>What the automatic marking of a stretch draws.</summary>
    public struct AutoOptions
    {
        /// <summary>At the junctions the stretch ends at: guide lane lines, guide arrows and (with CrossLines) the stop or give-way line.</summary>
        public bool Junctions;

        /// <summary>The lines across the traffic (stop lines, give-way lines) are drawn: the game's own are cleared.</summary>
        public bool CrossLines;

        /// <summary>At a junction where nothing controls the approach (no signals, no stop or yield): a stop line all the same.</summary>
        public bool StopUncontrolled;

        /// <summary>How long the guide lane lines are (m; 6.10.3: 30 m at least).</summary>
        public float GuideLength;

        /// <summary>Guide arrows in the guide lanes (5.13.6: two groups), and a third group upstream as a preview.</summary>
        public bool Arrows, Preview;

        /// <summary>
        /// 0.12.45: the guide lane lines' length, whether an approach has arrows (and arrows where a lane is added) and the preview group by the road's state (AutoMarking.RoadGuide, RoadPreview) instead of
        /// GuideLength, Arrows alone and Preview.
        /// </summary>
        public bool GuideByRoad, ArrowsByRoad, PreviewByRoad;

        /// <summary>0.12.47: the guide arrows' size as the arrows' size setting has it ("auto" by the road's speed; "30" to "33").</summary>
        public string ArrowSize;

        public EdgeLines Edges;

        /// <summary>
        /// 0.12.2: transitions where the lanes change at a node (7.1) with change lines (5.7); no-passing lines where the sight is short (6.1.3, 6.1.4); bus and cycle lanes (6.9.1, 5.4.5, 5.15.2); crosswalks
        /// away from junctions (5.8.5, 5.8.6); waiting areas (5.5) and junction guide lines (5.6) inside the junctions at the ends.
        /// </summary>
        public bool Transitions, NoPassing, Special, Crossings, Waiting;

        public JunctionGuides Guides;

        /// <summary>
        /// 0.12.44: the settings of each approach of the stretch (its rank: the approaches by where along the chain their stop is, AutoMarking.Ranks), for a stretch the player chose: the guide lane
        /// lines' length (NaN: GuideLength; 0: none) and the arrows (-1: Arrows; 0 off; 1 on). Null: none.
        /// </summary>
        public float[] GuideAt;
        public int[] ArrowsAt;

        public static AutoOptions Default => new AutoOptions
        {
            Junctions = true, CrossLines = false, StopUncontrolled = false, GuideLength = AutoMarking.MinGuide, Arrows = true, Preview = false, Edges = EdgeLines.Auto,
            Transitions = true, NoPassing = true, Special = true, Crossings = true, Waiting = true, Guides = JunctionGuides.Auto
        };
    }

    /// <summary>A marking that runs along the chain, from S0 to S1 (S0 below S1), at a lateral: its template and the settings that differ from its defaults.</summary>
    public sealed class AutoRun
    {
        public string Id;
        public readonly List<KeyValuePair<string, string>> Settings = new List<KeyValuePair<string, string>>();
        public float Lateral, S0, S1;

        public float Length => S1 - S0;

        /// <summary>A line that moves across over a transition (7.1): from Lateral to ShiftTo between ShiftS0 and ShiftS1 in a smooth S (MarkingTransition.Smooth); NaN when it does not move.</summary>
        public float ShiftS0 = float.NaN, ShiftS1 = float.NaN, ShiftTo = float.NaN;

        /// <summary>A lane line of a lane that was added at a node at this distance (NaN: none): near a junction it becomes the change line from there to the guide lane line (5.7.3).</summary>
        public float AddedAt = float.NaN;

        public bool Shifts => !float.IsNaN(ShiftTo) && ShiftS1 > ShiftS0;

        /// <summary>Where across the line is at a distance along the chain.</summary>
        public float LateralAt(float s) => !Shifts ? Lateral : Lateral + (ShiftTo - Lateral) * MarkingTransition.Smooth((s - ShiftS0) / (ShiftS1 - ShiftS0));

        public string Setting(string key)
        {
            foreach (KeyValuePair<string, string> kv in Settings)
            {
                if (kv.Key == key)
                    return kv.Value;
            }

            return null;
        }

        public override string ToString() => $"{Id} at {Lateral:+0.00;-0.00} m, {S0:0.0}..{S1:0.0}" + (Settings.Count > 0 ? " (" + string.Join(",", Settings.ConvertAll(kv => kv.Key + "=" + kv.Value)) + ")" : "");
    }

    /// <summary>A marking of one piece on the chain: at a distance and a lateral (its origin), facing its traffic (with the chain or against it); for a line across the lanes its span and lanes.</summary>
    public sealed class AutoStamp
    {
        public string Id;
        public readonly List<KeyValuePair<string, string>> Settings = new List<KeyValuePair<string, string>>();
        public float S, Lateral;
        public bool WithChain;
        public float Span;
        public int Lanes;

        public override string ToString() => $"{Id} at {S:0.0} m, {Lateral:+0.00;-0.00} m, {(WithChain ? "with" : "against")} the chain" + (Span > 0f ? $", {Span:0.0} m across" : "");
    }

    /// <summary>What the automatic marking drew at one approach (0.12.45, for the panel): its rank, its guide lane lines' length, its groups of arrows, and the road's state they were made by.</summary>
    public sealed class AutoEnd
    {
        public int Rank;
        public float Guide;
        public int Groups;

        /// <summary>The length and the arrows are by the road's state (not the player's settings).</summary>
        public bool GuideByRoad, ArrowsByRoad;

        public float Kmh = float.NaN, Flow = float.NaN, Queue = float.NaN;
    }

    /// <summary>What the automatic marking of a stretch is: its runs and its stamps, and notes on what it did not do.</summary>
    public sealed class AutoPlan
    {
        public readonly List<AutoRun> Runs = new List<AutoRun>();
        public readonly List<AutoStamp> Stamps = new List<AutoStamp>();
        public readonly List<string> Notes = new List<string>();

        /// <summary>What was drawn at each approach, in the order of their ranks.</summary>
        public readonly List<AutoEnd> Ends = new List<AutoEnd>();
    }

    /// <summary>
    /// The automatic marking of a stretch of road by GB 5768.3-2025 (0.12.2, mk.auto: "按国标自动重标这一段"): the lines along it from its lanes and speed, joined through the nodes where the road only goes
    /// on (StandardLines, piece by piece, a line going on where the next piece has the same line at the same place within LineJoin), and at each junction it ends at: the guide lane lines (线61, 6.10:
    /// white solid, the lane lines' width, GuideLength long, 30 m at least, ending at the stop line), the guide arrows in each lane by where its traffic may go (5.13.5: no more than two directions on one
    /// arrow; 5.13.6: the first group 1 m to 5 m before the stop line, the second with its tail level with the start of the guide lane lines, a third 30 m to 50 m upstream as a preview), and the stop
    /// line (线52, 6.4.3: from the centre line over the approach's lanes; 6.4.4: 1 m to 3 m before a crosswalk), or for a stop or a yield the give-way lines (线53, 线54). The edge lines by 5.4.4 (roads of
    /// four lanes or more) and 5.4.5 (not where a kerb marks the edge), dashed beside parking (5.4.6). Each is an ordinary marking item afterwards. Pure.
    /// </summary>
    public static partial class AutoMarking
    {
        public const string Id = "mk.auto";

        /// <summary>
        /// Whether the automatic marking is on offer (0.12.106; the owner, 7 Nov: "暂时先把标线中自动标线下了吧，后边全部交给ARL拓展实现"): false (the default of the property), it is taken down for now: its entry is not in the panel (a hidden
        /// choice) and the clear tool's "then mark it by the standard" does nothing. The code is as it was and the tests still run it, so that it can be put back, or read by the extension that takes its
        /// place (the lane and marking work is the ARL extension's from here). Markings the automatic marking placed are ordinary items and stay.
        /// </summary>
        internal static bool Offered { get; set; }

        /// <summary>The keys of the settings: the stretch (s: to the junctions at its ends, e: the segment alone), the game's markings that are cleared (lines: the lines along; all: the stop lines and the
        /// crosswalks too, and then the stop lines are drawn), the junctions' guide lines and arrows, the guide lines' length, the arrows, the preview arrows, the edge lines, a stop line where nothing
        /// controls the approach.</summary>
        public const string RangeKey = "range", KeepKey = VanillaClear.KeepKey, EndsKey = "ends", GuideKey = "guide", ArrowsKey = "arrows", PreviewKey = "preview", EdgeKey = "edge", FreeStopKey = "stopfree";

        /// <summary>0.12.45: the guide lane lines' length by the road's state ("road", the default) or GuideKey's ("fixed"); ArrowsKey and PreviewKey take "road" too.</summary>
        public const string GuideModeKey = "gmode", ByRoad = "road";

        /// <summary>0.12.47: the guide arrows' size ("auto": by the road's speed, RoadArrows.SizeFor; else the arrows' own size setting: "30" to "33", Figs. 42 to 45).</summary>
        public const string ArrowSizeKey = "asize";

        /// <summary>
        /// 0.12.44: the record of a stretch the automatic marking marked (an item of this template that draws nothing: RecordKey "1"), with the settings it was marked with, the height of the road's surface
        /// over its centre line where it was clicked (SurfaceKey, cm) and how many approaches it has (ApproachesKey: the panel shows a pair of settings for each).
        /// </summary>
        public const string RecordKey = "rec", SurfaceKey = "sf", ApproachesKey = "na";

        /// <summary>
        /// 0.12.48: the version of the rules a stretch's record was drawn by (RulesKey; none before 0.12.48: 0). Rules 2 is 0.12.47's: a segment marked alone has its lines through the plain nodes at its
        /// ends, and no item of a line crosses into a node's area. A stretch of older rules is drawn again with its settings when it is chosen.
        /// </summary>
        public const string RulesKey = "rv";
        public const int Rules = 2;

        /// <summary>Whether a record was drawn by older rules than these.</summary>
        public static bool IsOld(GuideSpec record) => record.Number(RulesKey, 0.0) < Rules;

        /// <summary>The most approaches a stretch has settings for.</summary>
        public const int MaxApproaches = 4;

        /// <summary>The settings of approach k (0.12.44): its guide lane lines' length (m; empty: as GuideKey; 0: none) and its arrows ("": as ArrowsKey; "1" on; "0" off).</summary>
        public static string ApproachGuideKey(int k) => "g" + k;

        public static string ApproachArrowsKey(int k) => "w" + k;

        /// <summary>
        /// The rank of each approach (0.12.44): its place when they are ordered by where along the chain their stop is (the chain is read from the same segment each time, so a stretch's approaches have
        /// the same ranks however often it is marked).
        /// </summary>
        public static int[] Ranks(IList<ChainApproach> approaches)
        {
            int n = approaches?.Count ?? 0;
            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;
            Array.Sort(order, (a, b) => approaches[a].StopS != approaches[b].StopS ? approaches[a].StopS.CompareTo(approaches[b].StopS) : a.CompareTo(b));
            var ranks = new int[n];
            for (int r = 0; r < n; r++)
                ranks[order[r]] = r;
            return ranks;
        }

        /// <summary>The settings without those of one stretch (0.12.44): its approaches' and its record's. Another road is marked with these.</summary>
        public static GuideSpec WithoutStretch(GuideSpec spec)
        {
            spec = spec.With(RecordKey, "0").With(SurfaceKey, "0").With(ApproachesKey, "0").With(RulesKey, "0");
            for (int k = 0; k < MaxApproaches; k++)
                spec = spec.With(ApproachGuideKey(k), "").With(ApproachArrowsKey(k), "");
            return spec;
        }

        /// <summary>The record of a stretch marked with these settings: the height of its surface over the centre line (m, kept in cm) and how many approaches it has.</summary>
        public static GuideSpec RecordOf(GuideSpec draft, float surface, int approaches)
        {
            int cm = (int)Math.Round(Math.Max(0f, Math.Min(999.99f, surface)) * 100f);
            return draft.With(RecordKey, "1").With(SurfaceKey, cm.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .With(ApproachesKey, Math.Max(0, Math.Min(MaxApproaches, approaches)).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .With(RulesKey, Rules.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The automatic marking's settings for the clear tool's "then mark it by the standard" (0.12.46): its defaults, with the clear tool's range (a segment, or the whole road to its junctions: the
        /// stretch) and which of the game's markings go (the same key and values).
        /// </summary>
        public static GuideSpec FromClear(GuideTemplate auto, GuideSpec clear) =>
            auto.Default().With(RangeKey, clear.Is(VanillaClear.ModeKey, "r") ? "s" : "e").With(KeepKey, clear.Get(VanillaClear.KeepKey));

        /// <summary>The settings a record keeps, as the panel shows them (the surface stays the record's).</summary>
        public static GuideSpec DraftOf(GuideSpec record) => record.With(RecordKey, "0").With(SurfaceKey, "0").With(RulesKey, "0");

        /// <summary>The height (m) of a record's surface over the centre line of its road.</summary>
        public static float SurfaceOf(GuideSpec record) => (float)record.Number(SurfaceKey, 0.0) / 100f;

        /// <summary>The options a draft of mk.auto says.</summary>
        public static AutoOptions OptionsOf(GuideSpec spec)
        {
            AutoOptions o = AutoOptions.Default;
            if (spec == null)
                return o;
            o.Junctions = spec.Flag(EndsKey);
            o.CrossLines = spec.Is(KeepKey, "all");
            o.StopUncontrolled = spec.Flag(FreeStopKey);
            double guide = spec.Number(GuideKey, MinGuide);
            o.GuideLength = (float)Math.Max(MinGuide, Math.Min(200.0, double.IsNaN(guide) ? MinGuide : guide));
            o.GuideByRoad = !spec.Is(GuideModeKey, "fixed");
            o.Arrows = !spec.Is(ArrowsKey, "0");
            o.ArrowsByRoad = spec.Is(ArrowsKey, ByRoad);
            o.Preview = spec.Is(PreviewKey, "1");
            o.PreviewByRoad = spec.Is(PreviewKey, ByRoad);
            o.ArrowSize = FixedArrowSize(spec.Get(ArrowSizeKey)).HasValue ? spec.Get(ArrowSizeKey) : "auto";
            o.Edges = spec.Is(EdgeKey, "all") ? EdgeLines.All : spec.Is(EdgeKey, "none") ? EdgeLines.None : EdgeLines.Auto;
            o.Transitions = spec.Flag(TaperKey);
            o.NoPassing = spec.Flag(SightKey);
            o.Special = spec.Flag(SpecialKey);
            o.Crossings = spec.Flag(MidCrossKey);
            o.Waiting = spec.Flag(WaitingKey);
            o.Guides = spec.Is(JunctionGuideKey, "all") ? JunctionGuides.All : spec.Is(JunctionGuideKey, "none") ? JunctionGuides.None : JunctionGuides.Auto;
            o.GuideAt = new float[MaxApproaches];
            o.ArrowsAt = new int[MaxApproaches];
            for (int k = 0; k < MaxApproaches; k++)
            {
                string g = spec.Get(ApproachGuideKey(k));
                o.GuideAt[k] = g.Length == 0 || !double.TryParse(g, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double metres) ? float.NaN : (float)Math.Max(0.0, Math.Min(200.0, metres));
                string w = spec.Get(ApproachArrowsKey(k));
                o.ArrowsAt[k] = w == "1" ? 1 : w == "0" ? 0 : -1;
            }

            return o;
        }

        /// <summary>The shortest guide lane lines (6.10.3).</summary>
        public const float MinGuide = 30f;

        /// <summary>
        /// The guide lane lines by the road's state (0.12.45): at most this long (m), and as long as the queue standing in the approach and this much more (m, the room to choose the lane before the
        /// queue). This mod's figures: 6.10.3 names the factors (the queues, the road's line, the junctions' spacing, the traffic's management) and gives no numbers.
        /// </summary>
        public const float MaxRoadGuide = 120f, QueueMargin = 10f;

        /// <summary>Two lines of two pieces this near across (m) are one line going on.</summary>
        public const float LineJoin = 0.6f;

        /// <summary>Where the first group of arrows ends before the stop line (m; 5.13.6: 1 m to 5 m), and the gap between the second group and the preview group (30 m to 50 m).</summary>
        public const float ArrowBeforeStop = 2f, PreviewGap = 30f;

        /// <summary>How far before a crosswalk the stop line is (m; 6.4.4: 1 m to 3 m).</summary>
        public const float StopBeforeCrosswalk = 1.5f;

        /// <summary>
        /// 0.12.54: a crosswalk at a slant whose near side is this much (m) farther at one lane than at another has the stop line stepped lane by lane, each lane's piece StopBeforeCrosswalk before it there
        /// (GB 51038-2015 13.5.4: square to the centre line, "部分车道的停止线适当后移"; the owner's picture of a slanted crosswalk).
        /// </summary>
        public const float StopStep = 0.5f;

        /// <summary>The stop line's width (m; 6.4.2: 20, 30 or 40 cm; 30 is this mod's choice).</summary>
        public const float StopWidth = 0.3f;

        /// <summary>The least room an approach leaves before its guide lines for them to be shorter than asked (m): an approach shorter than the guide lines and this has its guide lines over all of it but this.</summary>
        public const float GuideRoom = 5f;

        private sealed class Track
        {
            public StandardLine Line;
            public float S0, S1, Weighted, Length;
            public int LastPiece;
        }

        private static bool SameLine(StandardLine a, StandardLine b) =>
            a.Id == b.Id && (a.Key ?? "") == (b.Key ?? "") && (a.Value ?? "") == (b.Value ?? "") && (a.Key2 ?? "") == (b.Key2 ?? "") && (a.Value2 ?? "") == (b.Value2 ?? "") && Math.Abs(a.Lateral - b.Lateral) <= LineJoin;

        private static bool IsEdge(StandardLine line) => line.Id == "mk.line.edge";

        /// <summary>The lines of one piece by the standard, with the edge lines the options keep (and dashed beside parking).</summary>
        public static List<StandardLine> PieceLines(ChainPiece piece, AutoOptions options)
        {
            List<StandardLine> lines = StandardLines.Plan(piece.With, piece.Against, piece.Kmh, piece.HasSpeed);
            int lanes = MarkingSnap.Merged(piece.With).Count + MarkingSnap.Merged(piece.Against).Count;
            var kept = new List<StandardLine>();
            float min = float.MaxValue, max = float.MinValue;
            foreach (StandardLine line in lines)
            {
                min = Math.Min(min, line.Lateral);
                max = Math.Max(max, line.Lateral);
            }

            foreach (StandardLine line in lines)
            {
                if (!IsEdge(line))
                {
                    kept.Add(line);
                    continue;
                }

                // Which side of the carriageway the edge line is on: the outermost lines are the road's edges (right of the chain: the larger lateral); one beside a median is inside.
                bool outerRight = line.Lateral >= max - 1e-3f, outerLeft = line.Lateral <= min + 1e-3f;
                bool kerb = (outerRight && piece.KerbRight) || (outerLeft && piece.KerbLeft);
                // A cycle lane beyond the motor lanes asks for the line between them (5.4.5, 机非分界线), an outermost bus lane for its edge line (6.9.1, Fig. 87).
                bool forced = options.Special && options.Edges != EdgeLines.None && ForcedEdge(piece, outerRight, outerLeft);
                bool keep = forced || options.Edges == EdgeLines.All || (options.Edges == EdgeLines.Auto && (lanes >= 4 || ((outerRight || outerLeft) && !kerb)));
                if (!keep)
                    continue;
                StandardLine edge = line;
                // 5.4.6: dashed where vehicles may stop at the side.
                if ((outerRight && piece.ParkRight) || (outerLeft && piece.ParkLeft))
                {
                    edge.Key2 = "form";
                    edge.Value2 = "dashed";
                }

                kept.Add(edge);
            }

            if (options.Special)
                SpecialLines(piece, kept, options);
            return kept;
        }

        /// <summary>The settings of a planned line: its key and value, and the second (an edge line beside parking carries its form).</summary>
        private static void AddSettings(List<KeyValuePair<string, string>> into, StandardLine line)
        {
            if (!string.IsNullOrEmpty(line.Key))
                into.Add(new KeyValuePair<string, string>(line.Key, line.Value ?? ""));
            if (!string.IsNullOrEmpty(line.Key2))
                into.Add(new KeyValuePair<string, string>(line.Key2, line.Value2 ?? ""));
        }

        /// <summary>The plan of a chain with the approaches to the junctions at its ends (and those it crosses).</summary>
        public static AutoPlan Plan(RoadChain chain, IList<ChainApproach> approaches, AutoOptions options)
        {
            var plan = new AutoPlan();
            if (chain == null || chain.Pieces.Count == 0)
                return plan;
            options.GuideLength = Math.Max(MinGuide, float.IsNaN(options.GuideLength) ? MinGuide : Math.Min(200f, options.GuideLength));

            // ---- the lines along, piece by piece, joined through the nodes
            var open = new List<Track>();
            var done = new List<Track>();
            for (int i = 0; i < chain.Pieces.Count; i++)
            {
                ChainPiece piece = chain.Pieces[i];
                ChainJoint before = i > 0 ? chain.Pieces[i - 1].After : ChainJoint.End;
                List<StandardLine> lines = PieceLines(piece, options);
                var next = new List<Track>();
                foreach (StandardLine line in lines)
                {
                    // A line goes on from the piece before through a node where the road only goes on (to the node's middle and on from it); at a junction every line ends where the lanes do (a
                    // crosswalk and the junction's own lines are across the mouth of every arm, 5.8.5).
                    Track track = before == ChainJoint.Pass ? open.Find(t => t.LastPiece == i - 1 && SameLine(t.Line, line)) : null;
                    float from = before == ChainJoint.Pass ? piece.S0 : piece.LanesS0At(line.Lateral);
                    float to = piece.After == ChainJoint.Pass && i < chain.Pieces.Count - 1 ? piece.S1 : piece.LanesS1At(line.Lateral);
                    if (to - from < 0.5f)
                        continue;
                    if (track == null)
                        track = new Track { Line = line, S0 = from };
                    else
                        open.Remove(track);

                    track.S1 = to;
                    track.Weighted += line.Lateral * (to - from);
                    track.Length += to - from;
                    track.LastPiece = i;
                    next.Add(track);
                }

                foreach (Track t in open)
                    done.Add(t);
                open = next;
            }

            done.AddRange(open);
            foreach (Track t in done)
            {
                if (t.S1 - t.S0 < MarkingRuns.MinLength)
                    continue;
                var run = new AutoRun { Id = t.Line.Id, Lateral = t.Length > 0f ? t.Weighted / t.Length : t.Line.Lateral, S0 = t.S0, S1 = t.S1 };
                AddSettings(run.Settings, t.Line);
                plan.Runs.Add(run);
            }

            // ---- 0.12.2: the transitions at the nodes, the no-passing lines, the bus and cycle lanes' marks, the crosswalks away from the junctions
            if (options.Transitions)
                Transitions(chain, plan);
            if (options.NoPassing)
                NoPassing(chain, plan);
            if (options.Special)
                SpecialLanes(chain, plan);
            if (options.Crossings)
                Crossings(chain, plan, options);

            if (options.Junctions && approaches != null)
            {
                // ---- the junctions
                int[] ranks = Ranks(approaches);
                for (int i = 0; i < approaches.Count; i++)
                    Junction(chain, approaches[i], ranks[i], options, plan);
                plan.Ends.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            }

            plan.Runs.Sort((a, b) => a.S0 != b.S0 ? a.S0.CompareTo(b.S0) : a.Lateral.CompareTo(b.Lateral));
            return plan;
        }

        /// <summary>The arrow for a lane, by where its traffic may go: none for no direction or for more than two (5.13.5).</summary>
        public static string ArrowFor(ApproachLane lane)
        {
            bool s = lane.Straight, l = lane.Left, r = lane.Right, u = lane.UTurn;
            // A U-turn with a left turn and straight on is the straight-or-left arrow: the left arrow lets a U-turn be made where it is not forbidden (5.13.3, Table 1).
            if (s && l && r)
                return null;
            if (s && l)
                return "mk.arrow.straightleft";
            if (s && r)
                return "mk.arrow.straightright";
            if (s && u)
                return "mk.arrow.straightuturn";
            if (l && u)
                return "mk.arrow.leftuturn";
            if (l && r)
                return "mk.arrow.leftright";
            if (s)
                return "mk.arrow.straight";
            if (l)
                return "mk.arrow.left";
            if (r)
                return "mk.arrow.right";
            if (u)
                return "mk.arrow.uturn";
            return null;
        }

        /// <summary>The arrows' length for a speed (m), as the arrows' size setting will make it (RoadArrows.SizeFor).</summary>
        public static float ArrowLength(ChainPiece piece) => RoadArrows.Length(piece != null && piece.HasSpeed ? RoadArrows.SizeFor(piece.Kmh) : ArrowSize.Line31);

        /// <summary>The arrows' length (m) with the stretch's size setting (0.12.47): a fixed size's, else the road speed's.</summary>
        public static float ArrowLength(ChainPiece piece, AutoOptions options)
        {
            ArrowSize? size = FixedArrowSize(options.ArrowSize);
            return size.HasValue ? RoadArrows.Length(size.Value) : ArrowLength(piece);
        }

        /// <summary>The size an arrows' size setting names ("30" to "33", Figs. 42 to 45); null for "auto" or anything else.</summary>
        public static ArrowSize? FixedArrowSize(string setting) =>
            setting == "30" ? ArrowSize.Line30 : setting == "31" ? ArrowSize.Line31 : setting == "32" ? ArrowSize.Line32 : setting == "33" ? ArrowSize.Line33 : (ArrowSize?)null;

        /// <summary>An arrow of the plan with the stretch's size when it is a fixed one (else the item's "auto" takes the road's when it is placed).</summary>
        private static AutoStamp Arrow(string id, float s, float lateral, bool withChain, AutoOptions options)
        {
            var stamp = new AutoStamp { Id = id, S = s, Lateral = lateral, WithChain = withChain };
            if (FixedArrowSize(options.ArrowSize).HasValue)
                stamp.Settings.Add(new KeyValuePair<string, string>("size", options.ArrowSize));
            return stamp;
        }

        /// <summary>How far beyond the approach's StopS (m, toward the junction) its piece's lanes reach at a lateral: 0 at the nearer corner of a slanted mouth (ChainPiece.LanesS1At).</summary>
        private static float MouthBeyond(ChainPiece piece, ChainApproach approach, float lateral) =>
            ((approach.WithChain ? piece.LanesS1At(lateral) : piece.LanesS0At(lateral)) - approach.StopS) * approach.Dir;

        private static void Junction(RoadChain chain, ChainApproach approach, int rank, AutoOptions options, AutoPlan plan)
        {
            if (approach.Lanes.Count == 0)
                return;
            float dir = approach.Dir;
            int pieceIndex = chain.PieceAt(approach.StopS - dir * 0.5f);
            ChainPiece piece = chain.Pieces[pieceIndex];
            var end = new AutoEnd { Rank = rank, Kmh = piece.HasSpeed ? piece.Kmh : float.NaN, Flow = approach.Flow, Queue = approach.Queue };
            plan.Ends.Add(end);
            // How far back the approach has its lanes: to the start of its lanes on the piece (or further, through the nodes before it).
            float back = ApproachRoom(chain, pieceIndex, approach);

            // The stop line (or the give-way line), over the approach's lanes from the centre line (6.4.3: Fit is given the approach's lanes as the ones "with" the frame, the others as the opposite, both
            // in the chain's frame) and its cycle lanes; square to the lanes, before the crosswalk's nearest point within its span when there is one (a crosswalk at a slant is farther at the other end).
            float half = StopWidth * 0.5f;
            string crossId = approach.Control == JunctionControl.Stop ? "mk.giveway.stop" : approach.Control == JunctionControl.Yield ? "mk.giveway.yield" : "mk.stop";
            // The game's lines across are kept: the standard's is drawn where the game draws none (0.12.54).
            bool drawsCross = options.CrossLines || !approach.GameCrossLine;
            bool cross = drawsCross && (approach.Control != JunctionControl.None || options.StopUncontrolled);
            float cursor = approach.Lanes[approach.Lanes.Count / 2].Lateral;
            MarkingSpot spot = MarkingSnap.Fit(MarkingAnchor.Block, approach.WithChain ? piece.With : piece.Against, approach.WithChain ? piece.Against : piece.With, cursor, out float span, out int lanes);
            CoverBikes(piece, approach.WithChain, ref spot.Lateral, ref span);
            float lo = spot.Lateral - span * 0.5f, hi = spot.Lateral + span * 0.5f;
            bool spans = span > 0.5f;
            // How far back from StopS a stop line between two laterals is: within the approach's lanes at every lateral it spans (the slanted mouth is straight, so its nearest is at an end) and
            // StopBeforeCrosswalk before the crosswalk's nearest point there.
            float StopBack(float a, float b)
            {
                float crosswalk = b - a > 0.05f ? approach.CrosswalkNearest(a, b) : approach.CrosswalkAt;
                float mouth = b - a > 0.05f ? Math.Min(MouthBeyond(piece, approach, a), MouthBeyond(piece, approach, b)) : 0f;
                return Math.Max(half - mouth, float.IsNaN(crosswalk) ? float.MinValue : StopBeforeCrosswalk + half - crosswalk);
            }

            // A piece of the stop line for each lane (equal parts of its span) when the crosswalk is at a slant across them, else one line.
            int pieces = spans && lanes > 1 ? lanes : 1;
            var backs = new float[pieces];
            float width = span / pieces;
            for (int k = 0; k < pieces; k++)
                backs[k] = spans ? StopBack(lo + k * width, lo + (k + 1) * width) : StopBack(0f, 0f);
            float deepest = float.MinValue, shallowest = float.MaxValue;
            foreach (float b in backs)
            {
                deepest = Math.Max(deepest, b);
                shallowest = Math.Min(shallowest, b);
            }

            if (deepest - shallowest < StopStep)
            {
                pieces = 1;
                width = span;
                backs = new[] { deepest };
            }

            float stopS = approach.StopS - dir * deepest;
            // The guide lines and the arrows are square to the stop line, or to where it would be when the game's crossing lines are kept; with none by the standard (nothing controls the approach)
            // they reach the crosswalk where it crosses them, as the lines do. Where the line steps, each lane's own piece; a line between two lanes ends at the one farther back.
            bool square = cross || !drawsCross;
            float stopEdge = square ? stopS - dir * half : CrossAt(cursor);
            float StopEdgeAt(float lateral)
            {
                if (!square)
                    return CrossAt(lateral);
                if (pieces == 1)
                    return stopEdge;
                float back = float.MinValue;
                for (int k = 0; k < pieces; k++)
                {
                    float a = lo + k * width - 0.3f, b = lo + (k + 1) * width + 0.3f;
                    if (lateral >= a && lateral <= b)
                        back = Math.Max(back, backs[k]);
                }

                return approach.StopS - dir * ((back > float.MinValue ? back : deepest) + half);
            }

            if (cross && spans)
            {
                for (int k = 0; k < pieces; k++)
                    plan.Stamps.Add(new AutoStamp { Id = crossId, S = approach.StopS - dir * backs[k], Lateral = lo + (k + 0.5f) * width, WithChain = approach.WithChain, Span = width, Lanes = pieces == 1 ? lanes : 1 });
            }
            else if (drawsCross && approach.Control == JunctionControl.None)
            {
                plan.Notes.Add($"no stop line at {approach.StopS:0} m: nothing controls the approach");
            }

            // The lines along end before the crosswalk (5.8.5) where it crosses each (and at the mouth where it does, on a slant: LanesS1At), and those of the approach's side at the stop line when there
            // is one: the lines that end at this junction.
            float centre = SideBoundary(piece, approach);
            float CrossAt(float lateral)
            {
                float open = MouthBeyond(piece, approach, lateral);
                float near = approach.CrosswalkNearAt(lateral);
                return approach.StopS + dir * (float.IsNaN(near) ? open : Math.Min(open, near - CrosswalkClear));
            }

            for (int i = plan.Runs.Count - 1; i >= 0; i--)
            {
                AutoRun run = plan.Runs[i];
                bool here = approach.WithChain ? run.S1 >= approach.StopS - 0.05f && run.S0 < approach.StopS : run.S0 <= approach.StopS + 0.05f && run.S1 > approach.StopS;
                if (!here)
                    continue;
                float at = run.LateralAt(approach.StopS);
                bool ours = float.IsNaN(centre) || (approach.WithChain ? at >= centre - 0.3f : at <= centre + 0.3f);
                float limit = cross && ours ? StopEdgeAt(at) : CrossAt(at);
                if (approach.WithChain)
                    run.S1 = Math.Min(run.S1, limit);
                else
                    run.S0 = Math.Max(run.S0, limit);
                if (run.S1 - run.S0 < MarkingRuns.MinLength)
                    plan.Runs.RemoveAt(i);
            }

            // The guide lane lines: the lane lines between the approach's lanes become solid for the last GuideLength metres before the stop line (this approach's own length when the player set one:
            // 0 draws none, a length under 30 m is the player's choice).
            float own = options.GuideAt != null && rank < options.GuideAt.Length ? options.GuideAt[rank] : float.NaN;
            end.GuideByRoad = float.IsNaN(own) && options.GuideByRoad;
            float guide = !float.IsNaN(own) ? own : options.GuideByRoad ? RoadGuide(approach, piece, back) : options.GuideLength;
            if (guide > 0f && back < guide + GuideRoom)
            {
                guide = Math.Max(0f, back - GuideRoom);
                if (guide < MinGuide)
                    plan.Notes.Add($"the approach at {approach.StopS:0} m is {back:0} m long: guide lines {guide:0} m (the standard asks 30 m at least)");
            }

            float guideStart = stopEdge - dir * guide;
            if (approach.Lanes.Count >= 2 && guide >= MarkingRuns.MinLength)
            {
                for (int k = 1; k < approach.Lanes.Count; k++)
                {
                    float between = (approach.Lanes[k - 1].Lateral + approach.Lanes[k].Lateral) * 0.5f;
                    float guideEnd = StopEdgeAt(between);
                    CutLaneLine(plan, between, Math.Min(guideStart, guideEnd), Math.Max(guideStart, guideEnd));
                    var line = new AutoRun { Id = "mk.line.guidelane", Lateral = between, S0 = Math.Min(guideStart, guideEnd), S1 = Math.Max(guideStart, guideEnd) };
                    plan.Runs.Add(line);
                    ChangeToGuide(plan, between, guideStart, approach);
                }

                end.Guide = guide;
            }

            // The arrows.
            int ownArrows = options.ArrowsAt != null && rank < options.ArrowsAt.Length ? options.ArrowsAt[rank] : -1;
            if (ownArrows == 0 || (ownArrows < 0 && !options.Arrows))
                return;
            end.ArrowsByRoad = ownArrows < 0 && options.ArrowsByRoad;
            // By the road's state an approach of one lane that no signal controls has nothing to choose between: no arrows ("如设置导向箭头", 5.13.6: they are not required).
            if (end.ArrowsByRoad && approach.Lanes.Count < 2 && approach.Control != JunctionControl.Signals)
            {
                plan.Notes.Add($"no arrows at {approach.StopS:0} m by the road: one lane, no signals");
                return;
            }

            float length = ArrowLength(piece, options);
            var groups = new List<float>();
            // The first group: the head ArrowBeforeStop before the stop line; the arrow's origin is its tail.
            float first = stopEdge - dir * (ArrowBeforeStop + length);
            groups.Add(first);
            // The second at the start of the guide lane: its tail level with the start of the lines, when that is clear of the first.
            if (guide >= MarkingRuns.MinLength && (first - guideStart) * dir >= length + 2f)
                groups.Add(guideStart);
            if ((options.PreviewByRoad ? RoadPreview(approach, piece, guide) : options.Preview) && groups.Count == 2)
            {
                float third = guideStart - dir * (PreviewGap + length);
                if ((third - (approach.StopS - dir * back)) * dir >= 0f)
                    groups.Add(third);
                else
                    plan.Notes.Add($"no preview arrows at {approach.StopS:0} m: the approach is too short");
            }

            if ((first - (approach.StopS - dir * back)) * dir < 0f)
            {
                plan.Notes.Add($"no arrows at {approach.StopS:0} m: the approach is shorter than an arrow");
                return;
            }

            foreach (ApproachLane lane in approach.Lanes)
            {
                string id = ArrowFor(lane);
                if (id == null)
                {
                    if (lane.Directions > 2)
                        plan.Notes.Add($"no arrow in the lane at {lane.Lateral:+0.0;-0.0} m: it goes {lane.Directions} ways (5.13.5: no more than two on an arrow)");
                    continue;
                }

                // The first group before the lane's own piece of a stepped stop line.
                for (int g = 0; g < groups.Count; g++)
                    plan.Stamps.Add(Arrow(id, g == 0 ? StopEdgeAt(lane.Lateral) - dir * (ArrowBeforeStop + length) : groups[g], lane.Lateral, approach.WithChain, options));
            }

            end.Groups = groups.Count;
            if (end.ArrowsByRoad)
                AddedLaneArrows(chain, approach, pieceIndex, back, groups, length, options, plan);
        }

        /// <summary>
        /// The guide lane lines' length by the road's state (0.12.45; 6.10.3: 30 m at least, longer by the queues, the road's line, the junctions' spacing and the traffic's management; this mod's figures):
        /// 40 m from 60 km/h and 50 m from 80 km/h (the lane is chosen at speed before the lines); the queue standing now and QueueMargin more; on a congested road up to MaxRoadGuide by how congested
        /// it is (Congestion); rounded up to 5 m. A quarter of the approach (5 m at least) is left after the junction before it for changing lanes, unless that leaves less than 30 m.
        /// </summary>
        public static float RoadGuide(ChainApproach approach, ChainPiece piece, float back)
        {
            float want = MinGuide;
            if (piece.HasSpeed)
                want = piece.Kmh >= 80f ? 50f : piece.Kmh >= 60f ? 40f : MinGuide;
            if (!float.IsNaN(approach.Queue))
                want = Math.Max(want, approach.Queue + QueueMargin);
            want = Math.Max(want, MinGuide + Congestion(approach.Flow) * (MaxRoadGuide - MinGuide));
            want = Math.Min(MaxRoadGuide, (float)Math.Ceiling(want / 5f - 1e-4f) * 5f);
            float spacing = back - Math.Max(GuideRoom, back * 0.25f);
            return Math.Min(want, Math.Max(spacing, Math.Min(MinGuide, back - GuideRoom)));
        }

        /// <summary>How congested an approach is by its speed ratio (this mod's scale): 0 at 0.9 of the free speed or more, 1 at 0.3 or less; 0 when it is not known.</summary>
        public static float Congestion(float flow) => float.IsNaN(flow) ? 0f : Math.Max(0f, Math.Min(1f, (0.9f - flow) / 0.6f));

        /// <summary>
        /// The preview group by the road's state (5.13.6: "根据实际需要"): where the lane must be chosen early: guide lane lines of 50 m or more, 60 km/h or more, or a congested approach (Congestion 0.3 or more).
        /// </summary>
        public static bool RoadPreview(ChainApproach approach, ChainPiece piece, float guide) =>
            guide >= 50f || (piece.HasSpeed && piece.Kmh >= 60f) || Congestion(approach.Flow) >= 0.3f;

        /// <summary>How far after the node where a lane is added the tail of its arrow is (m).</summary>
        public const float ArrowAfterAdded = 5f;

        /// <summary>
        /// The arrows at the start of each lane added before the junction, by the road's state (5.13.6: "交叉口增加车道或车道功能发生改变的，应在相应的车道起点设置导向箭头"): at each node within the approach where
        /// the road goes on with more lanes its way, an approach lane the piece before the node does not have gets its arrow ArrowAfterAdded after the node, when that is before the first group and clear of
        /// every group by an arrow and 2 m.
        /// </summary>
        private static void AddedLaneArrows(RoadChain chain, ChainApproach approach, int pieceIndex, float back, List<float> groups, float length, AutoOptions options, AutoPlan plan)
        {
            float dir = approach.Dir;
            for (int i = pieceIndex; ; )
            {
                int j = i - (approach.WithChain ? 1 : -1);
                if (j < 0 || j >= chain.Pieces.Count || (approach.WithChain ? chain.Pieces[j].After : chain.Pieces[i].After) != ChainJoint.Pass)
                    return;
                ChainPiece down = chain.Pieces[i], up = chain.Pieces[j];
                float node = approach.WithChain ? down.S0 : down.S1;
                if ((approach.StopS - node) * dir > back)
                    return;
                List<float> now = MarkingSnap.Merged(approach.WithChain ? down.With : down.Against), before = MarkingSnap.Merged(approach.WithChain ? up.With : up.Against);
                float tail = node + dir * ArrowAfterAdded;
                if (now.Count > before.Count && (groups[0] - tail) * dir > 0f && groups.TrueForAll(g => Math.Abs(g - tail) >= length + 2f))
                {
                    foreach (ApproachLane lane in approach.Lanes)
                    {
                        string id = ArrowFor(lane);
                        if (id != null && now.Exists(l => Math.Abs(l - lane.Lateral) <= LineJoin) && !before.Exists(l => Math.Abs(l - lane.Lateral) <= LineJoin))
                            plan.Stamps.Add(Arrow(id, tail, lane.Lateral, approach.WithChain, options));
                    }
                }

                i = j;
            }
        }

        /// <summary>How far the lines keep from a crosswalk's near side (m).</summary>
        public const float CrosswalkClear = 0.5f;

        /// <summary>
        /// The lateral of the line between the approach's traffic and the other way's on its piece (the middle between the innermost lanes of the two, the centre line); NaN on a one-way piece. The lines
        /// on the approach's side of it, and on it, are the approach's.
        /// </summary>
        public static float SideBoundary(ChainPiece piece, ChainApproach approach)
        {
            List<float> mine = MarkingSnap.Merged(approach.WithChain ? piece.With : piece.Against), theirs = MarkingSnap.Merged(approach.WithChain ? piece.Against : piece.With);
            if (mine.Count == 0 || theirs.Count == 0)
                return float.NaN;
            // With the chain the approach is on the right (the larger laterals): its innermost lane is its smallest, the other way's the largest of theirs; against the chain the other way round.
            float a = approach.WithChain ? mine[0] : mine[mine.Count - 1], b = approach.WithChain ? theirs[theirs.Count - 1] : theirs[0];
            return (a + b) * 0.5f;
        }

        /// <summary>How far back from its stop the approach's lanes run (m): to where the lanes of its piece start, on through the nodes before it where the road goes on.</summary>
        private static float ApproachRoom(RoadChain chain, int pieceIndex, ChainApproach approach)
        {
            float dir = approach.Dir;
            int i = pieceIndex;
            float far = approach.WithChain ? chain.Pieces[i].LanesS0 : chain.Pieces[i].LanesS1;
            while (true)
            {
                int j = i - (approach.WithChain ? 1 : -1);
                if (j < 0 || j >= chain.Pieces.Count)
                    break;
                ChainJoint between = approach.WithChain ? chain.Pieces[j].After : chain.Pieces[i].After;
                if (between != ChainJoint.Pass)
                    break;
                i = j;
                far = approach.WithChain ? chain.Pieces[i].LanesS0 : chain.Pieces[i].LanesS1;
            }

            return Math.Max(0f, (approach.StopS - far) * dir);
        }

        /// <summary>The lane line at a lateral (within LineJoin) loses the stretch from s0 to s1: it is cut in two, or shortened, or dropped.</summary>
        private static void CutLaneLine(AutoPlan plan, float lateral, float s0, float s1)
        {
            for (int i = plan.Runs.Count - 1; i >= 0; i--)
            {
                AutoRun run = plan.Runs[i];
                if ((run.Id != "mk.line.lane" && run.Id != "mk.line.reserved") || run.S1 <= s0 || run.S0 >= s1 || Math.Abs(run.LateralAt(Math.Max(run.S0, Math.Min(run.S1, (s0 + s1) * 0.5f))) - lateral) > LineJoin)
                    continue;
                plan.Runs.RemoveAt(i);
                if (s0 - run.S0 >= MarkingRuns.MinLength)
                {
                    var a = Copy(run);
                    a.S1 = s0;
                    plan.Runs.Add(a);
                }

                if (run.S1 - s1 >= MarkingRuns.MinLength)
                {
                    var b = Copy(run);
                    b.S0 = s1;
                    plan.Runs.Add(b);
                }
            }
        }

        private static AutoRun Copy(AutoRun run)
        {
            var copy = new AutoRun { Id = run.Id, Lateral = run.Lateral, S0 = run.S0, S1 = run.S1, ShiftS0 = run.ShiftS0, ShiftS1 = run.ShiftS1, ShiftTo = run.ShiftTo, AddedAt = run.AddedAt };
            copy.Settings.AddRange(run.Settings);
            return copy;
        }
    }
}
