using System;
using System.Collections.Generic;
using System.Globalization;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>Why an item is not followed at all (the stage-1 log counts them).</summary>
    public enum FollowSkip
    {
        /// <summary>A sign on a building's wall: its place is the building's.</summary>
        Wall,

        /// <summary>A sign on a pole or an arm of another mod's, or (in log) on a post or a wall: it has no road relation, and no event for its host.</summary>
        Hosted
    }

    /// <summary>The plausibility gate of stage 1 (DESIGN-wiring-plan.md step C): how far an item may move when its road changed, before the log calls it a surprise.</summary>
    public static class FollowGate
    {
        /// <summary>The allowance (m) over the road's own change.</summary>
        public const float Slack = 1f;

        /// <summary>
        /// Whether the follow does any work for a state of the roadFollow preference: off does none (no relation is measured, no road is looked at); log is stage 1 (it looks and counts, nothing is written); on
        /// writes (stage 2 and 3); shadow has no meaning for a step with no old result and is log.
        /// </summary>
        public static bool Enabled(FeatureState state) => state != FeatureState.Off;

        /// <summary>Whether the follow writes: only the state on does (the default since 0.12.82; log and shadow only look).</summary>
        public static bool Writes(FeatureState state) => state == FeatureState.On;

        /// <summary>The frames a tile's items wait between two bursts of writes (DESIGN-follow-perf.md: N = 9 + D, D the frames a tile build takes, 3 to 24): the renderer drops a build of a tile whose item was written meanwhile.</summary>
        public static int GateFrames(double buildMs) => 9 + Math.Max(3, Math.Min(24, (int)Math.Ceiling(buildMs / 16.7)));

        /// <summary>
        /// The budget (ns of the scheduler's costs) for the next frame so that the real time stays about realBudgetMs: scale is how much more the work took than the costs said (an average of the last frames),
        /// kept between a fifth and 20 times. The first frame has scale 1.
        /// </summary>
        public static double PacedBudgetNs(double realBudgetMs, double scale) => realBudgetMs * 1e6 / Math.Max(0.2, Math.Min(20.0, scale));

        /// <summary>
        /// The scheduler's options of a Writing follow: a line with more items than GroupSlice is taken a slice at a time (a wall of 24 pieces is 1.5 km: shorter walls are followed whole, so that the phases of their
        /// pieces are made again in one go, LinePhases), an item waits MaxWaitFrames before it goes first whatever its distance, and what is within NearMetres of what the player looks at is near.
        /// </summary>
        public const int GroupSlice = 24, MaxWaitFrames = 400;

        public const float NearMetres = 200f;

        /// <summary>The frames a road must have been quiet (no event reached an item of it) before the near boost starts: a road that is dragged has an event every frame, and a dragged road's items are written again at the next frame.</summary>
        public const int BoostQuietFrames = 3;

        /// <summary>
        /// The extra real time (ms) a frame may give the items near what the player looks at, once the road he moved has been dropped: 2 ms while the game runs at 45 frames a second or better, 1 ms down to
        /// 25, none below (the follow must not make a slow game slower). The frame time is the system's smoothed average. The rest of the items go on at the base budget.
        /// </summary>
        public static double BoostMs(int quietFrames, double frameMs)
        {
            if (quietFrames < BoostQuietFrames || !(frameMs > 0.0))
                return 0.0;
            return frameMs <= 22.0 ? 2.0 : frameMs <= 40.0 ? 1.0 : 0.0;
        }

        /// <summary>The smoothed frame time: a fifth of the last frame's, the rest the old average (the first frame is the average).</summary>
        public static double NextFrameMs(double average, double frameMs) => !(average > 0.0) ? frameMs : average * 0.8 + frameMs * 0.2;

        /// <summary>The new scale: the old one and what the frame showed (its real ns over the costs' ns) mixed, one part in four of the new.</summary>
        public static double NextScale(double scale, double realNs, double costNs) => costNs <= 0.0 ? scale : Math.Max(0.2, Math.Min(20.0, scale * 0.75 + realNs / costNs * 0.25));

        /// <summary>A line whose length changed by more than this share is better made again than followed (the chords grow with it, and so does a bend's sag).</summary>
        public const float LengthChange = 0.10f;

        /// <summary>The longest chord a followed line may have (m): RunPath.Split's own limit, which is what a line is made with.</summary>
        public const float LineChordLimit = 3.5f;

        /// <summary>
        /// The most an item of a road may be expected to move: the largest move of a control point of the road's curve (a point of a Bezier curve is a weighted mean of its control points, so no point moves
        /// more than the control point that moves most, for the same parameter), a metre of slack for the share of the length that is not the same parameter, and the swing of the item about the road's point when the
        /// road turns: its distance from that point, across the road and (for an item past the road's end) along it, times the turn. A heuristic for the log to point at surprises with, not a proof; until 0.12.82 it
        /// had the distance across only, and an item in a junction's mouth, past the end, swung further than it allowed (the owner's log of 3 Nov: 54 moves over it, some by 0.02 to 0.5 m, a few by 2 to 4 m).
        /// </summary>
        public static float Bound(float controlMove, float lateral, float yawChange, float along = 0f) => controlMove + Slack + (float)Math.Sqrt(lateral * lateral + along * along) * Math.Abs(yawChange);

        /// <summary>The largest move of the four control points of a curve to another.</summary>
        public static float ControlMove(Bezier3 before, Bezier3 after) =>
            Math.Max(Math.Max((after.A - before.A).Length, (after.B - before.B).Length), Math.Max((after.C - before.C).Length, (after.D - before.D).Length));
    }

    /// <summary>What a burst of follow work found (FollowWatch.Finish): a drag of a road is many frames, and one report says what they would have done to the items.</summary>
    public sealed class FollowReport
    {
        /// <summary>How many moves are kept for the median (the first of a burst); the largest is kept over all.</summary>
        public const int MaxKept = 2048;

        /// <summary>How many warnings a report keeps.</summary>
        public const int MaxWarnings = 5;

        public int Frames;
        public double Ms, WorstFrameMs;

        /// <summary>Roads that changed and have items of ours.</summary>
        public int Edges;

        public int Singles, Moved, Unchanged, Detached, Unmeasurable, Rigid, Full;
        public int Lines, LinesUnchanged, LinesPoseOnly, LinesRewritten, LinesFailed, LinesDetached, LinesWantMore, LinesLonger;

        /// <summary>Items that stood on a road no longer there or with no shape: they are measured again at the next check.</summary>
        public int Stale;

        /// <summary>What a Writing follow wrote: single items, lines; and the lines that grew too much (or want more chords than the text holds) and were left where they were, for a rebuild.</summary>
        public int Written, LinesWritten, LinesNeedRebuild;

        /// <summary>Lines whose mapped points stepped back along the line, and were put straight on (LineFollow, RunPath.StepForward; 0.12.97).</summary>
        public int LinesSteppedBack;

        /// <summary>Items looked at that are fixed to a host (a lamp post, a signal pole, a building) and not to a road; they are counted among the singles too.</summary>
        public int Hosted;

        public int OverBound;
        public float LargestSingle, LargestLine, LargestLengthChange;
        public readonly List<string> Warnings = new List<string>();

        private readonly List<float> m_Moves = new List<float>();

        /// <summary>The largest move of any item, a single item's or a line's worst point.</summary>
        public float LargestMove => Math.Max(LargestSingle, LargestLine);

        /// <summary>Items of both kinds looked at.</summary>
        public int Looked => Singles + Lines;

        /// <summary>The median of the single items' moves that were kept, 0 when there is none.</summary>
        public float Median
        {
            get
            {
                if (m_Moves.Count == 0)
                    return 0f;
                var sorted = new List<float>(m_Moves);
                sorted.Sort();
                return sorted[sorted.Count / 2];
            }
        }

        public void NoteFrame(double ms)
        {
            Frames++;
            Ms += ms;
            if (ms > WorstFrameMs)
                WorstFrameMs = ms;
        }

        /// <summary>Where the main thread's time of the frames with work went (ms): the scheduler's pick, writing the items, and the rest (the follow itself and what the system does round it).</summary>
        public double TakeMs, WriteMs;

        /// <summary>Garbage collections that ran during the frames with work, and during the worst of them (Mono collects everything every time: a collection is a hitch of its own).</summary>
        public int Collections, WorstCollections;

        /// <summary>The worst frame's pick and write times (ms), and how many frames ran with the near boost.</summary>
        public double WorstTakeMs, WorstWriteMs;

        public int BoostedFrames;

        /// <summary>Pieces of walls whose start along the line was made again, and runs that were left because their pieces are not laid end to end (LinePhases).</summary>
        public int PhasesWritten, RunsNotRebased;

        /// <summary>The first and the last frame with work (the system's frame counter) and their times (s): the burst's span, in which a frame without work is one that waited for the tile gate or for a new event.</summary>
        public int FirstFrame = -1, LastFrame = -1;

        public double FirstSeconds, LastSeconds;

        /// <summary>The tile builds the renderer threw away during the burst (an item of the tile was written while it was built), and how many finished.</summary>
        public int BuildsDropped, BuildsAccepted;

        /// <summary>A frame with work and what it cost, part by part.</summary>
        public void NoteFrame(double ms, int frame, double seconds, double takeMs, double writeMs, int collections, bool boosted)
        {
            if (ms > WorstFrameMs)
            {
                WorstTakeMs = takeMs;
                WorstWriteMs = writeMs;
                WorstCollections = collections;
            }

            NoteFrame(ms);
            TakeMs += takeMs;
            WriteMs += writeMs;
            Collections += collections;
            if (boosted)
                BoostedFrames++;
            if (FirstFrame < 0)
            {
                FirstFrame = frame;
                FirstSeconds = seconds;
            }

            LastFrame = frame;
            LastSeconds = seconds;
        }

        /// <summary>What a warning says of a single item whose move is over the bound: the numbers the bound was made of, for the owner's log to tell a surprise from a slack bound.</summary>
        public static string DescribeSingle(string item, float controls, float lateral, float along, float turnedRadians) => string.Format(CultureInfo.InvariantCulture,
            "{0} (the road's control points moved {1:0.00} m, the item is {2:0.0} m across and {3:0.0} m along, it turned {4:0} degrees)", item, controls, lateral, along, turnedRadians * 57.29578f);

        /// <summary>The same for a line: how far the roads' control points moved, the widest lateral of its points, how far it turned and by how much its length changed.</summary>
        public static string DescribeLine(string item, float controls, float lateral, float turnedRadians, float lengthRatio) => string.Format(CultureInfo.InvariantCulture,
            "{0} (a line: the roads' control points moved {1:0.00} m, up to {2:0.0} m from the road, it turned {3:0} degrees, its length changed to {4:0.00} times)", item, controls, lateral, turnedRadians * 57.29578f, lengthRatio);

        /// <summary>The burst's span in frames (first to last frame with work, both counted).</summary>
        public int SpanFrames => FirstFrame < 0 ? 0 : LastFrame - FirstFrame + 1;

        /// <summary>The second line of a burst of a Writing follow: how long it lasted and where the time of its frames went (log only; the line says what the owner's next log is to be read for).</summary>
        public string FormatTiming() => string.Format(CultureInfo.InvariantCulture,
            "Follow (timing): from the first frame with work to the last {0} frame(s), {1:0.00} s; {2} of them with work ({3} boosted), {4} waiting; the frames with work took {5:0.0} ms: pick {6:0.0}, writing the items {7:0.0}, the follow and the rest {8:0.0}; "
            + "{9} garbage collection(s) in them; the worst frame {10:0.0} ms (pick {11:0.0}, writing {12:0.0}, {13} collection(s)); the renderer threw {14} tile build(s) away and finished {15}; {16} piece(s) of walls had their start along the line made again, {17} run(s) were not laid end to end and were left.",
            SpanFrames, LastSeconds - FirstSeconds, Frames, BoostedFrames, Math.Max(0, SpanFrames - Frames), Ms, TakeMs, WriteMs, Math.Max(0.0, Ms - TakeMs - WriteMs), Collections,
            WorstFrameMs, WorstTakeMs, WorstWriteMs, WorstCollections, BuildsDropped, BuildsAccepted, PhasesWritten, RunsNotRebased);

        internal void AddSingle(FollowOutcome outcome, float move, float bound, string who)
        {
            Singles++;
            switch (outcome)
            {
                case FollowOutcome.Unchanged:
                    Unchanged++;
                    return;
                case FollowOutcome.Detached:
                    Detached++;
                    return;
                case FollowOutcome.Unmeasurable:
                    Unmeasurable++;
                    return;
            }

            Moved++;
            if (move > LargestSingle)
                LargestSingle = move;
            if (m_Moves.Count < MaxKept)
                m_Moves.Add(move);
            Over(move, bound, who);
        }

        internal void AddLine(LineOutcome outcome, float move, float bound, float lengthRatio, bool wantsMore, string who)
        {
            Lines++;
            switch (outcome)
            {
                case LineOutcome.Failed:
                    LinesFailed++;
                    return;
                case LineOutcome.Unchanged:
                    LinesUnchanged++;
                    return;
                case LineOutcome.PoseOnly:
                    LinesPoseOnly++;
                    break;
                default:
                    LinesRewritten++;
                    break;
            }

            if (wantsMore)
                LinesWantMore++;
            float change = Math.Abs(lengthRatio - 1f);
            if (change > FollowGate.LengthChange)
                LinesLonger++;
            if (change > LargestLengthChange)
                LargestLengthChange = change;
            if (move > LargestLine)
                LargestLine = move;
            Over(move, bound, who);
        }

        private void Over(float move, float bound, string who)
        {
            if (move <= bound)
                return;
            OverBound++;
            if (Warnings.Count < MaxWarnings)
                Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Follow: {0} would move {1:0.00} m, more than the road's own change allows ({2:0.00} m).", who, move, bound));
        }

        /// <summary>The burst's one line for the log of a Writing follow: what was written, and what was left for a rebuild.</summary>
        public string FormatWritten() => string.Format(CultureInfo.InvariantCulture,
            "Follow (written): {0} road(s) with items changed, {1} item(s) looked at in {2} frame(s), {3:0.00} ms in all, worst frame {4:0.00} ms; "
            + "wrote {5} single item(s) (median move {6:0.00} m, largest {7:0.00} m; {8} by a rigid motion, {9} by the full follow) and {10} line(s) ({11} pose only, {12} rewritten, largest point move {13:0.00} m); "
            + "{14} line(s) NOT followed, they grew by more than {15:0} % or want more than {16} chords (largest growth {17:0.0} %): they need a rebuild; "
            + "{18} unchanged, {19} moved by someone else, {20} without a usable road or host, {21} line(s) failed, {22} stale; {23} over the plausible bound; {24} of the items are fixed to a host (lamp post, signal pole, building), not a road.",
            Edges, Looked, Frames, Ms, WorstFrameMs, Written, Median, LargestSingle, Rigid, Full, LinesWritten, LinesPoseOnly, LinesRewritten, LargestLine,
            LinesNeedRebuild, FollowGate.LengthChange * 100f, RunPath.MaxPieces, LargestLengthChange * 100f,
            Unchanged + LinesUnchanged, Detached + LinesDetached, Unmeasurable, LinesFailed, Stale, OverBound, Hosted)
            + (LinesSteppedBack > 0 ? string.Format(CultureInfo.InvariantCulture, " {0} line(s) had points that stepped back along the road (a node that moved) and were taken straight on.", LinesSteppedBack) : "");

        /// <summary>The burst's one line for the log.</summary>
        public string Format() => string.Format(CultureInfo.InvariantCulture,
            "Follow (log only, nothing written): {0} road(s) with items changed, {1} item(s) looked at in {2} frame(s), {3:0.00} ms in all, worst frame {4:0.00} ms; "
            + "singles {5}: {6} would move (median {7:0.00} m, largest {8:0.00} m), {9} unchanged, {10} moved by someone else, {11} without a usable road, {12} by a rigid motion, {13} by the full follow; "
            + "lines {14}: {15} pose only, {16} rewritten, {17} unchanged, {18} failed, {19} moved by someone else, {20} want more than {21} chords, {22} changed length by more than {23:0} % (largest {24:0.0} %), largest point move {25:0.00} m; "
            + "{26} stale; {27} over the plausible bound.",
            Edges, Looked, Frames, Ms, WorstFrameMs, Singles, Moved, Median, LargestSingle, Unchanged, Detached, Unmeasurable, Rigid, Full,
            Lines, LinesPoseOnly, LinesRewritten, LinesUnchanged, LinesFailed, LinesDetached, LinesWantMore, RunPath.MaxPieces, LinesLonger, FollowGate.LengthChange * 100f, LargestLengthChange * 100f, LargestLine,
            Stale, OverBound);
    }

    /// <summary>What the follow has done since the city was loaded, for the line the log gets every few minutes while there is something new in it.</summary>
    public sealed class FollowTotals
    {
        public int Bursts, Looked, Moved, LinesMoved, OverBound, Unmeasurable, Detached, LinesWantMore, LinesLonger, Written, LinesWritten, LinesNeedRebuild;
        public int SinglesLinked, LinesLinked, LinkFailed, SkippedWall, SkippedHosted, HostedLinked, HostedLooked;

        /// <summary>
        /// What the levels of the roads did (0.12.86): singles and lines linked to a road whose centre line is more than RoadLevels.LevelGap in height from the item (a wall on a viaduct linked to the road under
        /// it), the points of lines that took the road at their own level though another one was nearer in the plane, items linked while the game was updating their road (their relation is measured on a
        /// curve that is on its way), and the roads the game updated: all, those that are elevated, those with no item of this mod.
        /// </summary>
        public int SinglesOtherLevel, LinesOtherLevel, StackedPoints, LinkedWhileUpdating, UpdatedRoads, UpdatedElevated, UpdatedNoItems;

        /// <summary>The first items linked to a road at another level, with the height between them (at most MaxLevelSamples).</summary>
        public readonly List<string> LevelSamples = new List<string>();

        public const int MaxLevelSamples = 5;

        private int m_LevelsReported = -1;
        public float LargestMove;
        public double Ms;

        /// <summary>Bursts counted when the line was last made: a line is only made when it has something new.</summary>
        private int m_Reported;

        public void Add(FollowReport report)
        {
            Bursts++;
            Looked += report.Looked;
            Moved += report.Moved + report.LinesPoseOnly + report.LinesRewritten;
            LinesMoved += report.LinesPoseOnly + report.LinesRewritten;
            OverBound += report.OverBound;
            Unmeasurable += report.Unmeasurable + report.LinesFailed;
            Detached += report.Detached + report.LinesDetached;
            LinesWantMore += report.LinesWantMore;
            LinesLonger += report.LinesLonger;
            Written += report.Written;
            HostedLooked += report.Hosted;
            LinesWritten += report.LinesWritten;
            LinesNeedRebuild += report.LinesNeedRebuild;
            LargestMove = Math.Max(LargestMove, report.LargestMove);
            Ms += report.Ms;
        }

        /// <summary>An item linked to a road at another level: counted, and the first few named.</summary>
        public void NoteOtherLevel(string item, float height, bool line)
        {
            if (line)
                LinesOtherLevel++;
            else
                SinglesOtherLevel++;
            if (LevelSamples.Count < MaxLevelSamples)
                LevelSamples.Add(string.Format(CultureInfo.InvariantCulture, "{0} ({1}, {2:+0.0;-0.0} m from its road's centre line)", item, line ? "a line" : "a single item", height));
        }

        /// <summary>The line about the levels, or null when nothing in it changed since it was last asked for.</summary>
        public string TakeLevelsLine()
        {
            int print = SinglesOtherLevel * 31 + LinesOtherLevel * 7 + StackedPoints * 3 + LinkedWhileUpdating * 5 + UpdatedRoads * 11 + UpdatedElevated * 13 + UpdatedNoItems * 17;
            if (print == m_LevelsReported)
                return null;
            m_LevelsReported = print;
            return string.Format(CultureInfo.InvariantCulture,
                "Follow (levels), since the city was loaded: the game updated {0} road(s), {1} of them elevated, {2} with no item of this mod; linked to a road more than {3:0} m from the item in height: {4} single item(s), {5} line(s){6}; "
                + "{7} point(s) of lines took the road at their own level, though another road was nearer in the plane; {8} item(s) were linked while the game was updating their road.",
                UpdatedRoads, UpdatedElevated, UpdatedNoItems, RoadLevels.LevelGap, SinglesOtherLevel, LinesOtherLevel, LevelSamples.Count > 0 ? ": " + string.Join("; ", LevelSamples) : "", StackedPoints, LinkedWhileUpdating);
        }

        public void Skip(FollowSkip why)
        {
            switch (why)
            {
                case FollowSkip.Wall:
                    SkippedWall++;
                    break;
                default:
                    SkippedHosted++;
                    break;
            }
        }

        /// <summary>The totals' line of a Writing follow, or null when no burst has finished since it was last asked for.</summary>
        public string TakeWrittenLine()
        {
            if (Bursts == m_Reported)
                return null;
            m_Reported = Bursts;
            return string.Format(CultureInfo.InvariantCulture,
                "Follow (written), since the city was loaded: {0} burst(s) of road changes, {1} item(s) looked at, {2} single item(s) and {3} line(s) written, {4} line(s) not followed (need a rebuild), largest move {5:0.00} m, {6} over the plausible bound, {7} moved by someone else, {8} without a usable road, {9:0.0} ms in all; "
                + "linked: {10} single item(s), {11} line(s), {12} fixed to a host, {13} could not be measured; {14} of the items looked at are fixed to a host; not followed: {15} (no host found, or on another mod's pole or arm).",
                Bursts, Looked, Written, LinesWritten, LinesNeedRebuild, LargestMove, OverBound, Detached, Unmeasurable, Ms, SinglesLinked, LinesLinked, HostedLinked, LinkFailed, HostedLooked, SkippedWall + SkippedHosted);
        }

        /// <summary>The totals' line, or null when no burst has finished since it was last asked for.</summary>
        public string TakeLine()
        {
            if (Bursts == m_Reported)
                return null;
            m_Reported = Bursts;
            return string.Format(CultureInfo.InvariantCulture,
                "Follow (log only), since the city was loaded: {0} burst(s) of road changes, {1} item(s) looked at, {2} would move ({3} of them lines), largest move {4:0.00} m, {5} over the plausible bound, {6} moved by someone else, {7} without a usable road, {8} lines want more than {9} chords, {10} lines changed length by more than {11:0} %, {12:0.0} ms in all; "
                + "linked: {13} single item(s), {14} line(s), {15} could not be measured; not followed: {16} on a wall, {17} on a host.",
                Bursts, Looked, Moved, LinesMoved, LargestMove, OverBound, Detached, Unmeasurable, LinesWantMore, RunPath.MaxPieces, LinesLonger, FollowGate.LengthChange * 100f, Ms,
                SinglesLinked, LinesLinked, LinkFailed, SkippedWall, SkippedHosted);
        }
    }
}
