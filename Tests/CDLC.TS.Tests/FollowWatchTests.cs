using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using static CDLC.TS.Tests.FollowFixture;

namespace CDLC.TS.Tests
{
    /// <summary>Road follow stage 1 (0.12.74): what the items of a changed road would do is worked out and counted, and nothing is written (FollowWatch, FollowReport, FollowGate).</summary>
    internal static class FollowWatchTests
    {
        public static void RunAll()
        {
            Program.Run("follow watch: a road moved takes its item the road's own distance, by the rigid motion, and nothing is written", MovedRoad);
            Program.Run("follow watch: the relation is kept as it is: the same change is the same answer each time", KeptRelation);
            Program.Run("follow watch: a road that did not change leaves the item unchanged", Unchanged);
            Program.Run("follow watch: a bent road takes the item by the full follow, within the plausible bound", BentRoad);
            Program.Run("follow watch: an item someone moved is detached and is measured again where it stands", DetachedItem);
            Program.Run("follow watch: items linked at different versions of one road follow from their own", KeyedByBaseline);
            Program.Run("follow watch: updates that come while an item waits are one look", Coalescing);
            Program.Run("follow watch: the cap, the time and at least one item in a step", CapAndTime);
            Program.Run("follow watch: a road that is gone or has no shape makes its items stale until they are linked again", StaleItems);
            Program.Run("follow watch: forgotten, pruned and unlinked items are not looked at", ForgetAndPrune);
            Program.Run("follow watch: roads with no items of ours cost no report, and the drain of many roads stops on time", NoItems);
            Program.Run("follow watch: a line moved bodily is a pose only; the same change gives the same answer again", LineMoved);
            Program.Run("follow watch: a line on a road that got longer is rewritten, flagged for its length and for more chords than the text holds", LineLonger);
            Program.Run("follow watch: a line over two roads of which one moved, a line that was moved by someone, a road that has no shape", LineOthers);
            Program.Run("follow gate: the bound, the control point move, the warnings and their cap", Gate);
            Program.Run("follow report: the median, the largest, the kept moves and the line of the log", ReportText);
            Program.Run("follow watch: a burst is reported when it is over or after MaxBurstFrames, and the totals' line is made once", Bursts);
        }

        /// <summary>A map of roads and of the items on them, as the system gives them to the watch.</summary>
        private sealed class Scene
        {
            public readonly FollowWatch<int, int> Watch = new FollowWatch<int, int>();
            public readonly Dictionary<int, Bezier3> Roads = new Dictionary<int, Bezier3>();
            public readonly Dictionary<int, (Vec3 At, float Yaw, string Path)> Items = new Dictionary<int, (Vec3, float, string)>();
            public readonly FollowCache Cache = new FollowCache();
            public Func<int, FollowCurve> TableOf;

            public Scene() => TableOf = edge => Roads.TryGetValue(edge, out Bezier3 curve) ? Cache.Get(curve) : null;

            public bool Place(int item, bool wantPath, out Vec3 at, out float yaw, out string path)
            {
                at = default;
                yaw = 0f;
                path = null;
                if (!Items.TryGetValue(item, out var place))
                    return false;
                at = place.At;
                yaw = place.Yaw;
                path = wantPath ? place.Path : null;
                return true;
            }

            public bool Single(int item, int edge, Vec3 at, float yaw = 0f)
            {
                Items[item] = (at, yaw, null);
                return Watch.LinkSingle(item, edge, TableOf(edge), at, yaw);
            }

            public bool Line(int item, int[] edges, string path, Vec3 at, float yaw)
            {
                Items[item] = (at, yaw, path);
                var tables = new FollowCurve[edges.Length];
                for (int i = 0; i < edges.Length; i++)
                    tables[i] = TableOf(edges[i]);
                return Watch.LinkLine(item, edges[0], edges, tables, path, at, yaw);
            }

            /// <summary>Steps until nothing waits (each step a frame of 0.1 ms) and gives the report of the burst.</summary>
            public FollowReport Run(int maxItems = 200, Func<bool> outOfTime = null)
            {
                FollowReport report = null;
                for (int guard = 0; guard < 10000 && !Watch.Settled; guard++)
                {
                    if (Watch.Step(TableOf, Place, FollowMode.Fraction, maxItems, outOfTime ?? (() => false)))
                        Watch.NoteFrame(0.1);
                    report = Watch.Finished() ?? report;
                }

                return report ?? Watch.Finished();
            }
        }

        private static Bezier3 Straight(float x, float length = 100f) => Bezier3.Line(new Vec3(x, 0f, 0f), new Vec3(x, 0f, length));

        private static void MovedRoad()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            var at = new Vec3(3f, 0f, 40f);
            Program.Check(scene.Single(7, 1, at), "the item is linked");
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f);
            scene.Watch.Dirty(1);
            FollowReport report = scene.Run();
            Program.Check(report != null && report.Looked == 1 && report.Moved == 1 && report.Unchanged == 0, "one item looked at, one would move");
            Program.Near(report.LargestMove, 10f, 0.01f, "it would move as far as the road did");
            Program.Check(report.Rigid == 1 && report.Full == 0, "by the rigid motion");
            Program.Check(report.OverBound == 0 && report.Warnings.Count == 0, "which is no surprise");
            Program.Check(scene.Items[7].At.X == 3f && scene.Items[7].At.Z == 40f, "and the place the game has is where it was: nothing is written");
        }

        private static void KeptRelation()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            scene.Single(7, 1, new Vec3(3f, 0f, 40f));
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f);
            float first = 0f;
            for (int i = 0; i < 4; i++)
            {
                scene.Watch.Dirty(1);
                FollowReport report = scene.Run();
                if (i == 0)
                    first = report.LargestMove;
                Program.Near(report.LargestMove, first, 1e-4f, "update " + i + ": the same road gives the same move (nothing was refreshed or remembered as written)");
            }

            scene.Roads[1] = Straight(0f);
            scene.Watch.Dirty(1);
            Program.Check(scene.Run().Unchanged == 1, "the road put back: the item is where its relation puts it, unchanged");
        }

        private static void Unchanged()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            scene.Single(7, 1, new Vec3(-4f, 1f, 80f));
            scene.Watch.Dirty(1);
            FollowReport report = scene.Run();
            Program.Check(report.Unchanged == 1 && report.Moved == 0 && report.LargestMove == 0f, "an update that did not change the curve changes nothing");
        }

        private static void BentRoad()
        {
            var scene = new Scene();
            Bezier3 road = Straight(0f);
            scene.Roads[1] = road;
            scene.Single(1, 1, new Vec3(3f, 0f, 30f));
            scene.Single(2, 1, new Vec3(-2f, 0f, 85f));
            scene.Roads[1] = Neighbour(road, new Vec3(15f, 0f, 0f));
            scene.Watch.Dirty(1);
            FollowReport report = scene.Run();
            Program.Check(report.Moved == 2 && report.Full == 2 && report.Rigid == 0, "a node dragged is no rigid motion: both are followed in full");
            Program.Check(report.LargestMove > 5f && report.LargestMove < 16f, "the one near the node moves almost as far as the node");
            Program.Check(report.OverBound == 0, "and neither is a surprise (" + report.LargestMove + ")");
        }

        private static void DetachedItem()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            scene.Single(1, 1, new Vec3(3f, 0f, 40f));
            scene.Items[1] = (new Vec3(6f, 0f, 40f), 0f, null);
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f);
            scene.Watch.Dirty(1);
            FollowReport report = scene.Run();
            Program.Check(report.Detached == 1 && report.Moved == 0, "an item that is not where it was measured is detached, not moved");
            scene.Watch.Dirty(1);
            report = scene.Run();
            Program.Check(report.Detached == 0 && report.Unchanged == 1, "its relation was measured again where it stands, on the road as it is: nothing to do");
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(5f, 0f, 0f), 0f);
            scene.Watch.Dirty(1);
            report = scene.Run();
            Program.Near(report.LargestMove, 5f, 0.01f, "and it follows the road from there");
        }

        private static void KeyedByBaseline()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            scene.Single(1, 1, new Vec3(3f, 0f, 40f));
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f);
            scene.Watch.Dirty(1);
            scene.Run();
            scene.Single(2, 1, new Vec3(13f, 0f, 50f));
            scene.Single(3, 1, new Vec3(13f, 0f, 60f));
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(15f, 0f, 0f), 0f);
            scene.Watch.Dirty(1);
            FollowReport report = scene.Run();
            Program.Near(report.LargestSingle, 25f, 0.01f, "the item linked first would move 25 m from where it was measured");
            Program.Near(report.Median, 15f, 0.01f, "the two linked after the first move would move 15 m: each from the version of the road it was measured on");
            Program.Check(report.Rigid == 3, "all three by a rigid motion, each from its own baseline");
        }

        private static void Coalescing()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            scene.Single(1, 1, new Vec3(3f, 0f, 40f));
            scene.Single(2, 1, new Vec3(-3f, 0f, 60f));
            for (int i = 0; i < 5; i++)
            {
                scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(1f, 0f, 0f), 0f);
                scene.Watch.Dirty(1);
            }

            FollowReport report = scene.Run();
            Program.Check(report.Looked == 2 && report.Edges == 5, "five updates before the step: two items looked at once each (" + report.Looked + ")");
            Program.Near(report.LargestMove, 5f, 0.01f, "at the road as it is now");
        }

        private static void CapAndTime()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            for (int i = 0; i < 500; i++)
                scene.Single(i, 1, new Vec3(3f, 0f, i * 0.2f));
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(2f, 0f, 0f), 0f);
            scene.Watch.Dirty(1);
            int steps = 0;
            FollowReport report = null;
            while (!scene.Watch.Settled)
            {
                steps++;
                Program.Check(scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 200, () => false), "step " + steps + " followed items");
                scene.Watch.NoteFrame(0.2);
                report = scene.Watch.Finished();
                Program.Check(report == null || scene.Watch.Settled, "no report while items wait");
            }

            Program.Check(steps == 3 && report != null && report.Looked == 500 && report.Frames == 3, "500 items at 200 a step: three frames, one report of 500 (" + steps + ")");

            for (int i = 0; i < 3; i++)
                scene.Single(1000 + i, 1, new Vec3(-3f, 0f, i * 10f));
            scene.Watch.Dirty(1);
            int stepsOne = 0;
            while (!scene.Watch.Settled && stepsOne < 1000)
            {
                stepsOne++;
                scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 200, () => true);
                scene.Watch.NoteFrame(0.1);
            }

            Program.Check(stepsOne == 503, "with no time at all the first item of a step is still taken: one item a step, none starved (" + stepsOne + " steps for 503 items)");
        }

        private static void StaleItems()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            scene.Single(1, 1, new Vec3(3f, 0f, 40f));
            scene.Roads.Remove(1);
            scene.Watch.Dirty(1);
            FollowReport report = scene.Run();
            Program.Check(report.Unmeasurable == 1 && !scene.Watch.IsLinkedTo(1, 1), "a road with no curve: unmeasurable, and the item is not linked until it is linked again");
            scene.Watch.Dirty(1);
            Program.Check(!scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 200, () => false), "a stale item is skipped, not looked at again");
            scene.Roads[2] = Straight(20f);
            Program.Check(scene.Single(1, 2, new Vec3(23f, 0f, 40f)) && scene.Watch.IsLinkedTo(1, 2) && !scene.Watch.IsLinkedTo(1, 1), "linked to another road, it is its item and not the first road's");

            var gone = new Scene();
            gone.Roads[1] = Straight(0f);
            gone.Single(1, 1, new Vec3(3f, 0f, 40f));
            gone.Watch.EdgeGone(1);
            Program.Check(!gone.Watch.IsLinkedTo(1, 1), "a road that is gone: its items are stale");
            gone.Watch.Dirty(1);
            Program.Check(gone.Watch.Settled || !gone.Watch.Step(gone.TableOf, gone.Place, FollowMode.Fraction, 200, () => false), "and nothing is looked at for it");

            var unusable = new Scene();
            unusable.Roads[1] = Straight(0f);
            unusable.Single(1, 1, new Vec3(3f, 0f, 40f));
            unusable.Roads[1] = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0.1f));
            unusable.Watch.Dirty(1);
            Program.Check(unusable.Run().Unmeasurable == 1, "a road shorter than a half metre is unmeasurable");
            var nowhere = new Scene();
            nowhere.Roads[1] = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0.1f));
            Program.Check(!nowhere.Single(1, 1, new Vec3(1f, 0f, 0f)) && nowhere.Watch.Totals.LinkFailed == 1, "an item cannot be linked to a road with no shape");
        }

        private static void ForgetAndPrune()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            for (int i = 0; i < 4; i++)
                scene.Single(i, 1, new Vec3(3f, 0f, 10f * i));
            scene.Watch.Forget(0);
            Program.Check(scene.Watch.Count == 3 && !scene.Watch.IsLinkedTo(0, 1), "a forgotten item is gone");
            Program.Check(scene.Watch.Prune(i => i == 3) == 2 && scene.Watch.Count == 1, "pruned: the items that are not alive go");
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(1f, 0f, 0f), 0f);
            scene.Watch.Dirty(1);
            Program.Check(scene.Run().Looked == 1, "and only the one left is looked at");
            scene.Watch.Dirty(1);
            scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 0, () => false);
            scene.Watch.Forget(3);
            Program.Check(!scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 200, () => false), "an item forgotten while it waited is skipped");
            Program.Check(scene.Watch.Settled, "nothing is left waiting");
            scene.Watch.Clear();
            Program.Check(scene.Watch.Count == 0 && scene.Watch.Settled && scene.Watch.Totals.SinglesLinked == 0, "a city loaded clears it and the totals");
        }

        private static void NoItems()
        {
            var scene = new Scene();
            Program.Check(!scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 200, () => false) && scene.Watch.Finished() == null, "nothing to do is no work and no report");
            for (int i = 0; i < 1000; i++)
                scene.Watch.Dirty(i);
            Program.Check(!scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 200, () => true) && !scene.Watch.Settled, "with no time left the roads are drained 256 at a time: some are left");
            int steps = 1;
            while (!scene.Watch.Settled && steps < 20)
            {
                scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 200, () => true);
                steps++;
            }

            Program.Check(scene.Watch.Settled && steps == 4, "and are all drained in four steps (" + steps + ")");
            Program.Check(scene.Watch.Finished() == null, "roads that have no items make no report");
        }

        /// <summary>A line along x = 3 beside a road at x = 0: points every step metres from z = 0, as a path.</summary>
        private static string LinePath(float from, float to, float step, float x, out Vec3 at, out float yaw)
        {
            var points = new List<Vec3>();
            for (float z = from; z <= to + 1e-3f; z += step)
                points.Add(new Vec3(x, 0f, z));
            string path = RunPath.Encode(points, out yaw);
            at = points[0];
            return path;
        }

        private static void LineMoved()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            string path = LinePath(0f, 60f, 2f, 3f, out Vec3 at, out float yaw);
            Program.Check(path.Length > 0 && scene.Line(10, new[] { 1 }, path, at, yaw), "the line is linked to its road");
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(5f, 0f, 0f), 0f);
            for (int i = 0; i < 3; i++)
            {
                scene.Watch.Dirty(1);
                FollowReport report = scene.Run();
                Program.Check(report.Lines == 1 && report.LinesPoseOnly == 1 && report.LinesRewritten == 0, "update " + i + ": a pose only (the text is kept)");
                Program.Near(report.LargestMove, 5f, 0.01f, "update " + i + ": the line would move as far as the road");
                Program.Check(report.LinesLonger == 0 && report.OverBound == 0 && report.LinesWantMore == 0, "update " + i + ": nothing to flag");
            }
        }

        private static void LineLonger()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f, 60f);
            string path = LinePath(0f, 60f, 2f, 3f, out Vec3 at, out float yaw);
            scene.Line(10, new[] { 1 }, path, at, yaw);
            scene.Roads[1] = Straight(0f, 75f);
            scene.Watch.Dirty(1);
            FollowReport report = scene.Run();
            Program.Check(report.LinesRewritten == 1 && report.LinesLonger == 1, "a road a quarter longer: the text is rewritten and the line is flagged");
            Program.Near(report.LargestLengthChange, 0.25f, 0.01f, "by a quarter");
            Program.Check(report.LinesWantMore == 0, "and the chords of 2.5 m are still under the limit");

            var full = new Scene();
            full.Roads[1] = Straight(0f, 102.4f);
            string longPath = LinePath(0f, 102.4f, 3.2f, 3f, out Vec3 start, out float startYaw);
            full.Line(11, new[] { 1 }, longPath, start, startYaw);
            full.Roads[1] = Straight(0f, 122.88f);
            full.Watch.Dirty(1);
            FollowReport wanting = full.Run();
            Program.Check(wanting.LinesLonger == 1 && wanting.LinesWantMore == 1, "a line of 32 chords on a road a fifth longer wants more chords than the text holds");
        }

        private static void LineOthers()
        {
            var scene = new Scene();
            scene.Roads[1] = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 40f));
            scene.Roads[2] = Bezier3.Line(new Vec3(0f, 0f, 40f), new Vec3(0f, 0f, 80f));
            string path = LinePath(2f, 78f, 3f, 3f, out Vec3 at, out float yaw);
            Program.Check(scene.Line(10, new[] { 1, 2 }, path, at, yaw), "a line over two roads");
            scene.Roads[2] = Rigid(scene.Roads[2], new Vec3(6f, 0f, 0f), 0f);
            scene.Watch.Dirty(2);
            FollowReport report = scene.Run();
            Program.Check(report.Lines == 1 && report.LinesRewritten == 1, "only the second road updated: the line is looked at once, and its shape changes");
            Program.Near(report.LargestMove, 6f, 0.05f, "its far end moves as far as the road that moved");
            Program.Check(report.OverBound == 0, "which is no surprise");

            var moved = new Scene();
            moved.Roads[1] = Straight(0f, 60f);
            string again = LinePath(0f, 60f, 2f, 3f, out Vec3 start, out float startYaw);
            moved.Line(10, new[] { 1 }, again, start, startYaw);
            moved.Items[10] = (start + new Vec3(2f, 0f, 0f), startYaw, again);
            moved.Roads[1] = Rigid(moved.Roads[1], new Vec3(5f, 0f, 0f), 0f);
            moved.Watch.Dirty(1);
            report = moved.Run();
            Program.Check(report.LinesDetached == 1 && report.Lines == 0, "a line someone moved is detached, and counts as no line followed");
            moved.Watch.Dirty(1);
            report = moved.Run();
            Program.Check(report.Lines == 1 && report.LinesUnchanged == 1, "measured again where it stands on the road as it is: unchanged");

            var edited = new Scene();
            edited.Roads[1] = Straight(0f, 60f);
            string first = LinePath(0f, 60f, 2f, 3f, out start, out startYaw);
            edited.Line(10, new[] { 1 }, first, start, startYaw);
            edited.Items[10] = (start, startYaw, LinePath(0f, 40f, 2f, 3f, out _, out _));
            edited.Watch.Dirty(1);
            Program.Check(edited.Run().LinesDetached == 1, "a line whose text was edited is detached");

            var shapeless = new Scene();
            shapeless.Roads[1] = Straight(0f, 60f);
            string text = LinePath(0f, 60f, 2f, 3f, out start, out startYaw);
            shapeless.Line(10, new[] { 1 }, text, start, startYaw);
            shapeless.Roads.Remove(1);
            shapeless.Watch.Dirty(1);
            report = shapeless.Run();
            Program.Check(report.LinesFailed == 1 && !shapeless.Watch.IsLinkedTo(10, 1), "a road that is gone: the line fails and is stale");
            var none = new Scene();
            none.Roads[1] = Straight(0f);
            Program.Check(!none.Line(10, new[] { 1 }, "garbage", new Vec3(0f, 0f, 0f), 0f) && none.Watch.Totals.LinkFailed == 1, "a text that is not a path is not linked");
        }

        private static void Gate()
        {
            Program.Near(FollowGate.Bound(6f, 3f, 0.5f), 8.5f, 1e-5f, "the bound is the control move, a metre and the lateral's swing");
            Program.Near(FollowGate.Bound(0f, -3f, -0.5f), 2.5f, 1e-5f, "signs do not matter");
            Program.Near(FollowGate.Bound(6f, 3f, 0.5f, 4f), 9.5f, 1e-5f, "an item 4 m past the road's end swings on the 5 m it is from the road's point: 6 + 1 + 5 x 0.5");
            Program.Near(FollowGate.Bound(6f, 0f, 0.5f, -4f), 9f, 1e-5f, "along counts by its size, whichever way");
            Program.Near(FollowGate.Bound(1f, 3f, 0.5f), FollowGate.Bound(1f, 3f, 0.5f, 0f), 1e-6f, "no along is none");
            Bezier3 road = Straight(0f);
            Program.Near(FollowGate.ControlMove(road, road), 0f, 0f, "no change");
            Bezier3 dragged = new Bezier3(road.A, road.B, road.C, road.D + new Vec3(3f, 4f, 0f));
            Program.Near(FollowGate.ControlMove(road, dragged), 5f, 1e-5f, "the node that moved most counts, in three dimensions");

            var report = new FollowReport();
            report.AddSingle(FollowOutcome.Moved, 20f, 5f, "item 9");
            Program.Check(report.OverBound == 1 && report.Warnings.Count == 1 && report.Warnings[0].Contains("item 9") && report.Warnings[0].Contains("20.00") && report.Warnings[0].Contains("5.00"), "a move over the bound is counted and said");
            report.AddSingle(FollowOutcome.Moved, 5f, 5f, "item 10");
            Program.Check(report.OverBound == 1, "a move at the bound is not over it");
            for (int i = 0; i < 30; i++)
                report.AddSingle(FollowOutcome.Moved, 50f, 5f, "item " + i);
            Program.Check(report.OverBound == 31 && report.Warnings.Count == FollowReport.MaxWarnings, "the count goes on and the warnings are capped (" + report.Warnings.Count + ")");
            Program.Check(FollowGate.LengthChange == 0.10f && FollowGate.LineChordLimit == 3.5f, "the line's limits are the plan's");
        }

        private static void ReportText()
        {
            var report = new FollowReport();
            foreach (float move in new[] { 1f, 2f, 3f, 4f, 100f })
                report.AddSingle(FollowOutcome.Moved, move, 1000f, "x");
            Program.Near(report.Median, 3f, 0f, "the median of five");
            Program.Near(report.LargestMove, 100f, 0f, "the largest");
            var many = new FollowReport();
            for (int i = 0; i < FollowReport.MaxKept; i++)
                many.AddSingle(FollowOutcome.Moved, 1f, 10f, "x");
            for (int i = 0; i < 3 * FollowReport.MaxKept; i++)
                many.AddSingle(FollowOutcome.Moved, 5f, 10f, "x");
            many.AddSingle(FollowOutcome.Moved, 99f, 200f, "x");
            Program.Near(many.LargestMove, 99f, 0f, "the largest is kept over all moves though only the first are kept for the median");
            Program.Near(many.Median, 1f, 0f, "and the median is of those kept");
            report.AddSingle(FollowOutcome.Unchanged, 0f, 0f, "x");
            report.AddSingle(FollowOutcome.Detached, 0f, 0f, "x");
            report.AddSingle(FollowOutcome.Unmeasurable, 0f, 0f, "x");
            report.AddLine(LineOutcome.Rewritten, 12.5f, 100f, 1.3f, true, "line");
            report.AddLine(LineOutcome.Failed, 0f, 0f, 1f, false, null);
            report.NoteFrame(0.25);
            report.NoteFrame(0.75);
            report.Edges = 3;
            string text = report.Format();
            Program.Check(text.StartsWith("Follow (log only, nothing written): 3 road(s) with items changed, 10 item(s) looked at in 2 frame(s), 1.00 ms in all, worst frame 0.75 ms;"), "the line starts with the burst: " + text);
            Program.Check(text.Contains("singles 8: 5 would move (median 3.00 m, largest 100.00 m), 1 unchanged, 1 moved by someone else, 1 without a usable road"), "the singles: " + text);
            Program.Check(text.Contains("lines 2: 0 pose only, 1 rewritten, 0 unchanged, 1 failed, 0 moved by someone else, 1 want more than 32 chords, 1 changed length by more than 10 % (largest 30.0 %), largest point move 12.50 m"), "the lines: " + text);
            Program.Check(text.EndsWith("0 stale; 0 over the plausible bound."), "and the surprises: " + text);
            Program.Check(!text.Contains("\n"), "one line");
        }

        private static void Bursts()
        {
            var scene = new Scene();
            scene.Roads[1] = Straight(0f);
            for (int i = 0; i < 100; i++)
                scene.Single(i, 1, new Vec3(3f, 0f, i * 0.5f));
            scene.Roads[1] = Rigid(scene.Roads[1], new Vec3(1f, 0f, 0f), 0f);
            scene.Watch.Dirty(1);
            FollowReport early = null;
            int frames = 0;
            while (early == null && frames < 200)
            {
                frames++;
                scene.Watch.Step(scene.TableOf, scene.Place, FollowMode.Fraction, 1, () => false);
                scene.Watch.NoteFrame(0.1);
                early = scene.Watch.Finished();
            }

            Program.Check(early != null && frames == FollowWatch<int, int>.MaxBurstFrames && early.Frames == frames && !scene.Watch.Settled, "a burst that goes on is reported after MaxBurstFrames frames (" + frames + ")");
            FollowReport rest = scene.Run(1);
            Program.Check(rest != null && rest.Looked == 40, "and the rest is the next burst's: 40 items (" + (rest == null ? -1 : rest.Looked) + ")");

            FollowTotals totals = scene.Watch.Totals;
            Program.Check(totals.Bursts == 2 && totals.Looked == 100 && totals.Moved == 100, "the totals have both");
            totals.Skip(FollowSkip.Wall);
            totals.Skip(FollowSkip.Hosted);
            totals.Skip(FollowSkip.Hosted);
            string line = totals.TakeLine();
            Program.Check(line != null && line.Contains("2 burst(s)") && line.Contains("100 item(s) looked at") && line.Contains("100 single item(s)") && line.Contains("1 on a wall, 2 on a host."), "the totals' line: " + line);
            Program.Check(totals.TakeLine() == null, "and it is made once until there is something new");
            scene.Watch.Dirty(1);
            scene.Run();
            Program.Check(totals.TakeLine() != null, "a new burst makes a new line");
        }
    }
}
