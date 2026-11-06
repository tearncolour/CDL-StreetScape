using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The writing follow (roadFollow on; stages 2 and 3): what FollowWatch.StepWrite hands to be written, that a road moved away and back gives every item its place again (singles and lines), that an item
    /// someone moved is not dragged, that a relinked item follows only its new road, the scheduler's order, groups, budget and tile gate end to end against a model of the renderer's tile builds.
    /// </summary>
    internal static class FollowWriteTests
    {
        public static void RunAll()
        {
            Program.Run("follow write: a road moved takes its items by the rigid motion and they are handed out to be written", MovedRoad);
            Program.Run("follow write: the same road again writes nothing, and nothing waits", Quiet);
            Program.Run("follow write: a road dragged away and back by Move It's rule gives every single item its place again", ReturnsSingles);
            Program.Run("follow write: a road moved bodily and put back gives the items their places again (undo)", ReturnsRigid);
            Program.Run("follow write: a line dragged away and back is where it was, to the text's centimetre", ReturnsLines);
            Program.Run("follow write: an item someone moved is not dragged, it is measured again where it stands", Detached);
            Program.Run("follow write: an item linked again to another road follows that road only", Relinked);
            Program.Run("follow write: a road that is gone makes its items stale, and linking them again makes them follow", StaleThenLinked);
            Program.Run("follow write: a line that grew by more than a tenth is left where it is and counted for a rebuild", NeedsRebuild);
            Program.Run("follow write: the nearest the focus is written first, a group together, the cap and the budget hold", OrderAndBudget);
            Program.Run("follow write: a tile's items are written in bursts a gate apart and the renderer's builds are accepted", TileGate);
            Program.Run("follow write: a burst the cap cut goes on in the next frames without waiting for the gate", CutBurst);
            Program.Run("follow write: an item is held while its road changes or it waits, and not when the follow does not write", Holds);
            Program.Run("follow write: the gate, the pacing of the budget and the tile number", GatePacing);
            Program.Run("follow write: the report and the totals say what was written", Reports);
            Program.Run("follow host: the motion between two poses takes the first to the second, a place and a facing", Between);
            Program.Run("follow host: an item fixed to a post that was moved and turned goes by the same motion", HostMoved);
            Program.Run("follow host: a post moved away and back gives the item its place again", HostReturns);
            Program.Run("follow host: an item someone moved is not dragged, and a host that is gone or has no pose makes it stale until it is linked again", HostDetachedAndGone);
            Program.Run("follow host: items on roads and on hosts share the budget, the groups and the gate, and a follow that only logs leaves hosts alone", HostMixed);
        }

        private sealed class WScene
        {
            public readonly FollowWatch<int, int> Watch = new FollowWatch<int, int> { Writing = true };
            public readonly Dictionary<int, Bezier3> Roads = new Dictionary<int, Bezier3>();
            public readonly Dictionary<int, (Vec3 At, float Yaw, string Path)> Items = new Dictionary<int, (Vec3, float, string)>();
            public readonly FollowCache Cache = new FollowCache();
            public readonly List<FollowWrite<int>> Writes = new List<FollowWrite<int>>();
            public readonly List<(int Frame, int Item)> Written = new List<(int, int)>();
            public readonly Dictionary<int, (Vec3 At, float Yaw)> Hosts = new Dictionary<int, (Vec3, float)>();
            public Func<int, FollowCurve> TableOf;
            public int Frame;
            public double BudgetNs = 5e8;
            public int Cap = 200;

            public WScene() => TableOf = edge => Roads.TryGetValue(edge, out Bezier3 curve) ? Cache.Get(curve) : null;

            public bool HostPose(int host, out Vec3 at, out float yaw)
            {
                at = default;
                yaw = 0f;
                if (!Hosts.TryGetValue(host, out var pose))
                    return false;
                at = pose.At;
                yaw = pose.Yaw;
                return true;
            }

            /// <summary>An item fixed to a host that stands at a pose.</summary>
            public bool Hosted(int item, int host, Vec3 at, float yaw, int group = 0)
            {
                Items[item] = (at, yaw, null);
                Hosts.TryGetValue(host, out var pose);
                return Watch.LinkHosted(item, host, pose.At, pose.Yaw, at, yaw, group);
            }

            public void MoveHost(int host, Vec3 at, float yaw)
            {
                Hosts[host] = (at, yaw);
                Watch.Dirty(host);
            }

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

            public bool Single(int item, int edge, Vec3 at, float yaw = 0f, int group = 0)
            {
                Items[item] = (at, yaw, null);
                return Watch.LinkSingle(item, edge, TableOf(edge), at, yaw, group);
            }

            public bool Line(int item, int[] edges, string path, Vec3 at, float yaw)
            {
                Items[item] = (at, yaw, path);
                var tables = new FollowCurve[edges.Length];
                for (int i = 0; i < edges.Length; i++)
                    tables[i] = TableOf(edges[i]);
                return Watch.LinkLine(item, edges[0], edges, tables, path, at, yaw);
            }

            /// <summary>One frame: the writes are made to the items as the system makes them. How many were written.</summary>
            public int Step()
            {
                Writes.Clear();
                if (Watch.StepWrite(TableOf, Place, FollowMode.Fraction, Frame, BudgetNs, Cap, Writes, HostPose))
                    Watch.NoteFrame(0.1);
                foreach (FollowWrite<int> write in Writes)
                {
                    Items[write.Item] = (write.Position, write.Yaw, write.Path ?? Items[write.Item].Path);
                    Written.Add((Frame, write.Item));
                }

                Frame++;
                return Writes.Count;
            }

            /// <summary>Frames until nothing waits (at most maxFrames).</summary>
            public int Settle(int maxFrames = 400)
            {
                int total = 0;
                for (int i = 0; i < maxFrames && (!Watch.Settled || i == 0); i++)
                    total += Step();
                return total;
            }

            public void Move(int edge, Bezier3 curve)
            {
                Roads[edge] = curve;
                Watch.Dirty(edge);
            }
        }

        private static Bezier3 Straight(float x, float length = 100f) => Bezier3.Line(new Vec3(x, 0f, 0f), new Vec3(x, 0f, length));

        private static void MovedRoad()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f);
            Program.Check(scene.Single(7, 1, new Vec3(103f, 0f, 40f), 0.2f) && scene.Single(8, 1, new Vec3(96f, 1f, 70f), -0.4f), "two items are linked");
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f));
            int written = scene.Settle();
            Program.Check(written == 2, "both are written: " + written);
            Program.Near(scene.Items[7].At.X, 113f, 2e-3f, "the first goes the road's 10 m: " + scene.Items[7].At.X);
            Program.Near(scene.Items[7].At.Z, 40f, 2e-3f, "and no further along");
            Program.Near(scene.Items[8].At.X, 106f, 2e-3f, "the second too");
            Program.Near(scene.Items[8].At.Y, 1f, 2e-3f, "at its own height");
            Program.Near(scene.Items[7].Yaw, 0.2f, 1e-3f, "facing as before");
            FollowReport report = scene.Watch.Finished();
            Program.Check(report != null && report.Written == 2 && report.Rigid == 2 && report.Full == 0 && report.Edges == 1, "the report: one road, two written by the rigid motion");
        }

        private static void Quiet()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f);
            scene.Single(7, 1, new Vec3(103f, 0f, 40f));
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f));
            scene.Settle();
            Vec3 first = scene.Items[7].At;
            Program.Check(scene.Watch.Settled, "nothing waits");
            scene.Watch.Dirty(1);
            int written = scene.Settle();
            Program.Check(written == 0 && scene.Items[7].At.X == first.X && scene.Items[7].At.Z == first.Z, "the road updated for another reason, in the same place: nothing is written");
            Program.Check(!scene.Watch.StepWrite(scene.TableOf, scene.Place, FollowMode.Fraction, scene.Frame, 1e9, 200, scene.Writes), "a frame with no event does no work");
        }

        private static float YawDiff(float a, float b) => Math.Abs(RoadRelation.Wrap(a - b));

        private static void ReturnsSingles()
        {
            double worst = 0.0, worstYaw = 0.0;
            for (int seed = 0; seed < 30; seed++)
            {
                var random = new Random(900 + seed);
                var scene = new WScene();
                Bezier3 basis = FollowFixture.RandomCurve(random, 3000f, 0.25f, 150f);
                scene.Roads[1] = basis;
                var start = new List<(Vec3 At, float Yaw)>();
                for (int i = 0; i < 12; i++)
                {
                    Vec3 place = FollowFixture.RandomPlace(random, basis);
                    float yaw = (float)(random.NextDouble() * 6.0 - 3.0);
                    if (!scene.Single(i, 1, place, yaw))
                        continue;
                    start.Add((place, yaw));
                }

                var delta = new Vec3((float)(random.NextDouble() * 12.0 - 6.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 12.0 - 6.0));
                for (int step = 1; step <= 40; step++)
                {
                    scene.Move(1, FollowFixture.Neighbour(basis, delta * (step / 40f)));
                    scene.Settle();
                }

                for (int step = 39; step >= 0; step--)
                {
                    scene.Move(1, FollowFixture.Neighbour(basis, delta * (step / 40f)));
                    scene.Settle();
                }

                for (int i = 0; i < start.Count; i++)
                {
                    worst = Math.Max(worst, (scene.Items[i].At - start[i].At).Length);
                    worstYaw = Math.Max(worstYaw, YawDiff(scene.Items[i].Yaw, start[i].Yaw));
                }
            }

            Program.Check(worst <= 3e-3, "after 80 writes each of a drag away and back every item is within 3 mm of where it was: " + worst.ToString("0.00000"));
            Program.Check(worstYaw <= 2e-3, "and faces as it did, within 2 mrad: " + worstYaw.ToString("0.00000"));
        }

        private static void ReturnsRigid()
        {
            double worst = 0.0;
            for (int seed = 0; seed < 20; seed++)
            {
                var random = new Random(500 + seed);
                var scene = new WScene();
                Bezier3 basis = FollowFixture.RandomCurve(random, 3000f, 0.3f, 150f);
                scene.Roads[1] = basis;
                var start = new List<Vec3>();
                for (int i = 0; i < 10; i++)
                {
                    Vec3 place = FollowFixture.RandomPlace(random, basis);
                    if (scene.Single(i, 1, place, 0.5f))
                        start.Add(place);
                }

                for (int step = 1; step <= 60; step++)
                {
                    scene.Move(1, FollowFixture.Rigid(basis, new Vec3(0.7f * step, 0.01f * step, -0.5f * step), 0.01f * step));
                    scene.Settle();
                }

                scene.Move(1, basis);
                scene.Settle();
                for (int i = 0; i < start.Count; i++)
                    worst = Math.Max(worst, (scene.Items[i].At - start[i]).Length);
            }

            Program.Check(worst <= 2e-3, "after 60 moves of a road and its being put back every item is within 2 mm of where it was: " + worst.ToString("0.00000"));
        }

        private static string LineOn(Bezier3 road, float lateral, out Vec3 at, out float yaw, out List<Vec3> world)
        {
            var table = new FollowCurve(road);
            world = new List<Vec3>();
            int chords = Math.Max(4, Math.Min(32, (int)Math.Ceiling(table.Length * 0.9f / 3f)));
            for (int i = 0; i <= chords; i++)
            {
                float t = table.TAt(table.Length * (0.05f + 0.9f * i / chords));
                table.At(t, out Vec3 on, out Vec3 heading);
                world.Add(on + new Vec3(heading.Z, 0f, -heading.X) * lateral);
            }

            string path = RunPath.Encode(world, out yaw);
            at = world[0];
            world = MarkingPolygon.ToWorld(RunPath.Decode(path), at, yaw);
            return path;
        }

        private static void ReturnsLines()
        {
            double worst = 0.0;
            for (int seed = 0; seed < 12; seed++)
            {
                var random = new Random(77 + seed);
                var scene = new WScene();
                Bezier3 basis = FollowFixture.RandomCurve(random, 3000f, 0.2f, 100f);
                scene.Roads[1] = basis;
                string path = LineOn(basis, 5f + (float)(random.NextDouble() * 4.0), out Vec3 at, out float yaw, out List<Vec3> original);
                if (!scene.Line(1, new[] { 1 }, path, at, yaw))
                    continue;
                Vec3 side = basis.Tangent(0.5f).RightXZ();
                Vec3 delta = side * (float)(random.NextDouble() * 6.0 + 2.0);
                for (int step = 1; step <= 20; step++)
                {
                    scene.Move(1, FollowFixture.Neighbour(basis, delta * (step / 20f)));
                    scene.Settle();
                }

                for (int step = 19; step >= 0; step--)
                {
                    scene.Move(1, FollowFixture.Neighbour(basis, delta * (step / 20f)));
                    scene.Settle();
                }

                var item = scene.Items[1];
                List<Vec3> back = MarkingPolygon.ToWorld(RunPath.Decode(item.Path), item.At, item.Yaw);
                Program.Check(back.Count == original.Count, "seed " + seed + ": the line has its points again: " + back.Count + " of " + original.Count);
                for (int j = 0; j < Math.Min(back.Count, original.Count); j++)
                    worst = Math.Max(worst, Vec3.DistanceXZ(back[j], original[j]));
            }

            Program.Check(worst <= 0.03, "after 40 writes of a line away and back every point is within 3 cm of where it was (the text holds a centimetre, its turns 0.1 mrad over the line): " + worst.ToString("0.0000"));
        }

        private static void Detached()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f);
            scene.Single(7, 1, new Vec3(103f, 0f, 40f));
            scene.Single(8, 1, new Vec3(97f, 0f, 60f));
            scene.Items[7] = (new Vec3(108f, 0f, 40f), 0f, null);
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f));
            scene.Settle();
            Program.Check(scene.Items[7].At.X == 108f, "the item the player moved stays where he put it");
            Program.Near(scene.Items[8].At.X, 107f, 2e-3f, "the other goes with the road");
            FollowReport report = scene.Watch.Finished();
            Program.Check(report != null && report.Detached == 1 && report.Written == 1, "one detached, one written");
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(5f, 0f, 0f), 0f));
            scene.Settle();
            Program.Near(scene.Items[7].At.X, 113f, 2e-3f, "the next move of the road takes it on from its new place: " + scene.Items[7].At.X);
        }

        private static void Relinked()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f);
            scene.Roads[2] = Straight(400f);
            scene.Single(7, 1, new Vec3(103f, 0f, 40f));
            // The system finds the item beside the other road (the first was replaced, or the player moved the item): the relation is measured again.
            scene.Items[7] = (new Vec3(397f, 0f, 30f), 0f, null);
            Program.Check(scene.Watch.LinkSingle(7, 2, scene.TableOf(2), new Vec3(397f, 0f, 30f), 0f), "linked to the second road");
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f));
            Program.Check(scene.Settle() == 0 && scene.Items[7].At.X == 397f, "the first road moves: the item does not");
            scene.Move(2, FollowFixture.Rigid(scene.Roads[2], new Vec3(0f, 0f, 8f), 0f));
            scene.Settle();
            Program.Near(scene.Items[7].At.Z, 38f, 2e-3f, "the second does: " + scene.Items[7].At.Z);
        }

        private static void StaleThenLinked()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f);
            scene.Single(7, 1, new Vec3(103f, 0f, 40f));
            scene.Watch.EdgeGone(1);
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f));
            Program.Check(scene.Settle() == 0 && scene.Items[7].At.X == 103f, "a road that is gone: its item is stale and stays");
            Program.Check(!scene.Watch.IsLinkedTo(7, 1), "and is not a linked item");
            Program.Check(scene.Watch.LinkSingle(7, 1, scene.TableOf(1), new Vec3(103f, 0f, 40f), 0f), "it is linked again to the road as it is now");
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(5f, 0f, 0f), 0f));
            scene.Settle();
            Program.Near(scene.Items[7].At.X, 108f, 2e-3f, "and follows from there: " + scene.Items[7].At.X);
        }

        private static void NeedsRebuild()
        {
            var scene = new WScene();
            Bezier3 road = Straight(100f, 100f);
            scene.Roads[1] = road;
            string path = LineOn(road, 6f, out Vec3 at, out float yaw, out _);
            Program.Check(scene.Line(1, new[] { 1 }, path, at, yaw), "the line is linked");
            // Longer by 30 %: the end of the road goes 30 m on.
            scene.Move(1, Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 130f)));
            Program.Check(scene.Settle() == 0, "a road 30 % longer: nothing is written");
            Program.Check(scene.Items[1].At.Z == at.Z && scene.Items[1].Path == path, "the line is where and as it was");
            FollowReport report = scene.Watch.Finished();
            Program.Check(report != null && report.LinesNeedRebuild == 1 && report.LinesWritten == 0, "and counted as one that needs a rebuild");
            Program.Check(report.FormatWritten().Contains("1 line(s) NOT followed"), "the log line says so: " + report.FormatWritten());
            // Longer by 5 %: it follows.
            scene.Move(1, Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 105f)));
            int written = scene.Settle();
            Program.Check(written == 1, "a road 5 % longer: the line is written: " + written);
            report = scene.Watch.Finished();
            Program.Check(report != null && report.LinesWritten == 1 && report.LinesNeedRebuild == 0, "counted as written");
            // 8 % longer but with chords that were near the limit already: 33 chords are wanted and the text holds 32, so it is left.
            var tight = new WScene();
            Bezier3 longRoad = Straight(100f, 118f);
            tight.Roads[1] = longRoad;
            string tightPath = LineOn(longRoad, 6f, out Vec3 tightAt, out float tightYaw, out _);
            tight.Line(1, new[] { 1 }, tightPath, tightAt, tightYaw);
            tight.Move(1, Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 127.4f)));
            Program.Check(tight.Settle() == 0 && tight.Watch.Finished().LinesNeedRebuild == 1, "a line that is 8 % longer and wants more chords than the text holds is left too");
            // A short line of short chords (12 of 3 m): 15 % longer is left though its chords would still do, 9 % longer is followed.
            var shortLine = new WScene();
            Bezier3 shortRoad = Straight(100f, 40f);
            shortLine.Roads[1] = shortRoad;
            string shortPath = LineOn(shortRoad, 6f, out Vec3 shortAt, out float shortYaw, out _);
            shortLine.Line(1, new[] { 1 }, shortPath, shortAt, shortYaw);
            shortLine.Move(1, Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 46f)));
            Program.Check(shortLine.Settle() == 0 && shortLine.Watch.Finished().LinesNeedRebuild == 1, "a line 15 % longer is left, by its growth alone");
            shortLine.Move(1, Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 43.6f)));
            Program.Check(shortLine.Settle() == 1, "a line 9 % longer is followed");
            // Shorter: followed (the chords shrink with it).
            scene.Move(1, Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(100f, 0f, 70f)));
            Program.Check(scene.Settle() == 1, "a road 30 % shorter: the line is written");
        }

        private static void OrderAndBudget()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f, 3000f);
            for (int i = 0; i < 40; i++)
                scene.Single(i, 1, new Vec3(103f, 0f, 20f + i * 70f));
            scene.Watch.Scheduler.SetFocus(103f, 20f + 30 * 70f, false, 0f, 0f);
            scene.Cap = 5;
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(2f, 0f, 0f), 0f));
            scene.Step();
            var first = new List<int>();
            foreach ((int frame, int item) in scene.Written)
                first.Add(item);
            first.Sort();
            Program.Check(first.Count == 5 && first.TrueForAll(i => Math.Abs(i - 30) <= 2), "five at a time, those round the camera first: " + string.Join(",", first));
            Program.Check(scene.Watch.LastTaken == 5 && Math.Abs(scene.Watch.LastCostNs - 5 * FollowWatch<int, int>.SingleNs) < 1.0, "the frame knows what it took and what the costs of it were: " + scene.Watch.LastTaken + " " + scene.Watch.LastCostNs);

            // A group is written together, even where it is more than the cap.
            var group = new WScene();
            group.Roads[1] = Straight(100f, 3000f);
            for (int i = 0; i < 3; i++)
                group.Single(i, 1, new Vec3(103f, 0f, 500f), 0f, 5);
            for (int i = 3; i < 30; i++)
                group.Single(i, 1, new Vec3(103f, 0f, 20f + i * 90f));
            group.Watch.Scheduler.SetFocus(103f, 500f, false, 0f, 0f);
            group.Cap = 2;
            group.Move(1, FollowFixture.Rigid(group.Roads[1], new Vec3(2f, 0f, 0f), 0f));
            group.Step();
            Program.Check(group.Written.Count == 3 && group.Written.TrueForAll(w => w.Item < 3), "a group of three comes whole in the first frame though the cap is two: " + group.Written.Count);

            // The cap and the budget: 100 items, five items' costs a frame is 5 items.
            var costly = new WScene { BudgetNs = 5.0 * FollowWatch<int, int>.SingleNs };
            costly.Roads[1] = Straight(100f, 3000f);
            for (int i = 0; i < 100; i++)
                costly.Single(i, 1, new Vec3(103f, 0f, 10f + i * 29f));
            costly.Move(1, FollowFixture.Rigid(costly.Roads[1], new Vec3(2f, 0f, 0f), 0f));
            int a = costly.Step();
            Program.Check(a == 5, "the budget holds the frame to five items: " + a);
            int frames = 1, total = a;
            while (!costly.Watch.Settled && frames < 100)
            {
                total += costly.Step();
                frames++;
            }

            Program.Check(total == 100 && frames == 20, "and every item is written in the end, in 20 frames: " + total + " in " + frames);
        }

        /// <summary>The renderer's tile builds as ChunkSystem makes them: a plan every 6 frames, a build of 3 frames, dropped when an item of the tile was written meanwhile.</summary>
        private sealed class TileModel
        {
            public readonly int[] Version, BuildStarted, DoneAt;
            public readonly bool[] Stale, Busy;
            public int Accepted, Discarded, Started;

            public TileModel(int tiles)
            {
                Version = new int[tiles];
                BuildStarted = new int[tiles];
                DoneAt = new int[tiles];
                Stale = new bool[tiles];
                Busy = new bool[tiles];
            }

            public void Frame(int frame)
            {
                for (int t = 0; t < Version.Length; t++)
                {
                    if (Busy[t] && DoneAt[t] <= frame)
                    {
                        Busy[t] = false;
                        if (BuildStarted[t] == Version[t])
                            Accepted++;
                        else
                        {
                            Discarded++;
                            Stale[t] = true;
                        }
                    }
                }

                if (frame % 6 != 0)
                    return;
                for (int t = 0; t < Version.Length; t++)
                {
                    if (Stale[t] && !Busy[t])
                    {
                        Busy[t] = true;
                        Stale[t] = false;
                        BuildStarted[t] = Version[t];
                        DoneAt[t] = frame + 3;
                        Started++;
                    }
                }
            }
        }

        private static (int Accepted, int Started, List<(int Frame, int Item)> Written, WScene Scene) Drag(int gate)
        {
            var scene = new WScene();
            scene.Watch.Scheduler.TileGateFrames = gate;
            scene.Roads[1] = Straight(100f, 150f);
            scene.Roads[2] = Straight(400f, 150f);
            for (int i = 0; i < 60; i++)
            {
                scene.Single(i, 1, new Vec3(96f + i % 9, 0f, 10f + i * 2f));
                scene.Single(100 + i, 2, new Vec3(396f + i % 9, 0f, 10f + i * 2f));
            }

            var model = new TileModel(2);
            Bezier3 a = scene.Roads[1], b = scene.Roads[2];
            for (int frame = 0; frame < 160; frame++)
            {
                if (frame < 90)
                {
                    scene.Move(1, FollowFixture.Rigid(a, new Vec3(0.17f * (frame + 1), 0f, 0f), 0f));
                    scene.Move(2, FollowFixture.Rigid(b, new Vec3(0.17f * (frame + 1), 0f, 0f), 0f));
                }

                int before = scene.Written.Count;
                scene.Step();
                for (int w = before; w < scene.Written.Count; w++)
                {
                    int tile = scene.Written[w].Item >= 100 ? 1 : 0;
                    model.Version[tile]++;
                    model.Stale[tile] = true;
                }

                model.Frame(frame);
            }

            return (model.Accepted, model.Started, scene.Written, scene);
        }

        private static void TileGate()
        {
            var open = Drag(0);
            var gated = Drag(14);
            Program.Check(gated.Accepted >= open.Accepted * 3 && gated.Accepted >= 10, "with the gate the renderer takes most of its builds (" + gated.Accepted + " of " + gated.Started + "), without it nearly none (" + open.Accepted + " of " + open.Started + ")");
            foreach (int tile in new[] { 0, 1 })
            {
                var frames = new List<int>();
                foreach ((int frame, int item) in gated.Written)
                {
                    if ((item >= 100 ? 1 : 0) == tile && !frames.Contains(frame))
                        frames.Add(frame);
                }

                int tooClose = 0;
                for (int i = 1; i < frames.Count; i++)
                    tooClose += frames[i] - frames[i - 1] < 14 ? 1 : 0;
                Program.Check(frames.Count >= 5 && tooClose == 0, "tile " + tile + ": " + frames.Count + " bursts, none within 14 frames of the one before");
            }

            var items = gated.Scene.Items;
            double worst = 0.0;
            for (int i = 0; i < 60; i++)
            {
                float expectedX = 96f + i % 9 + 0.17f * 90f;
                worst = Math.Max(worst, Math.Abs(items[i].At.X - expectedX));
                worst = Math.Max(worst, Math.Abs(items[100 + i].At.X - (expectedX + 300f)));
            }

            Program.Check(worst < 5e-3 && gated.Scene.Watch.Settled, "after the drag every item is where the road puts it, within 5 mm: " + worst.ToString("0.0000"));
            int lastWrite = gated.Written[gated.Written.Count - 1].Frame;
            Program.Check(lastWrite <= 90 + 14 + 3, "and it was there within the gate of the end of the drag: frame " + lastWrite);
        }

        private static void CutBurst()
        {
            var scene = new WScene { Cap = 200 };
            scene.Watch.Scheduler.TileGateFrames = 14;
            scene.Roads[1] = Straight(100f, 3000f);
            for (int i = 0; i < 500; i++)
                scene.Single(i, 1, new Vec3(103f, 0f, 3f + i * 2f));
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(1f, 0f, 0f), 0f));
            var frames = new List<int>();
            for (int f = 0; f < 60; f++)
            {
                if (scene.Step() > 0)
                    frames.Add(f);
            }

            Program.Check(frames.Count >= 3 && frames[0] == 0 && frames[1] == 1 && frames[2] == 2, "500 items under a cap of 200: three frames running, the gate does not stop a burst: " + string.Join(",", frames));
            Program.Check(scene.Written.Count == 500 && scene.Watch.Settled, "all 500 are written");
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(1f, 0f, 0f), 0f));
            int next = -1;
            for (int f = 0; f < 60 && next < 0; f++)
            {
                if (scene.Step() > 0)
                    next = scene.Frame - 1;
            }

            Program.Check(next >= 2 + 14, "the next burst waits for the gate: frame " + next);
        }

        private static void Holds()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f);
            scene.Single(7, 1, new Vec3(103f, 0f, 40f));
            Program.Check(!scene.Watch.Holds(7, 0, 120), "an item whose road was not updated is not held");
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f));
            scene.Watch.Scheduler.TileGateFrames = 0;
            scene.Step();
            Program.Check(scene.Watch.Holds(7, scene.Frame, 120), "a road updated this frame: held");
            Program.Check(scene.Watch.Holds(7, scene.Frame + 100, 120) && !scene.Watch.Holds(7, scene.Frame + 200, 120), "held for 120 frames after the last update, and then not");
            // Waiting for the gate of its tile: held although the road was last updated long ago (holdFrames 0).
            var gated = new WScene();
            gated.Watch.Scheduler.TileGateFrames = 14;
            gated.Roads[1] = Straight(100f);
            gated.Single(7, 1, new Vec3(103f, 0f, 40f));
            gated.Move(1, FollowFixture.Rigid(gated.Roads[1], new Vec3(1f, 0f, 0f), 0f));
            gated.Settle();
            gated.Move(1, FollowFixture.Rigid(gated.Roads[1], new Vec3(1f, 0f, 0f), 0f));
            gated.Step();
            Program.Check(!gated.Watch.Settled && gated.Watch.Holds(7, gated.Frame + 50, 0), "an item that waits for the gate is held");
            var log = new FollowWatch<int, int>();
            var table = new FollowCurve(Straight(100f));
            log.LinkSingle(7, 1, table, new Vec3(103f, 0f, 40f), 0f);
            log.Dirty(1);
            Program.Check(!log.Holds(7, 0, 120), "a follow that does not write holds nothing");
        }

        private static void GatePacing()
        {
            Program.Check(FollowGate.GateFrames(0) == 12 && FollowGate.GateFrames(100) == 15 && FollowGate.GateFrames(1e6) == 33, "N = 9 + D, D from the build time within 3 to 24 frames: " + FollowGate.GateFrames(0) + " " + FollowGate.GateFrames(100) + " " + FollowGate.GateFrames(1e6));
            Program.Check(FollowGate.GateFrames(51.0) == 9 + 4 && FollowGate.GateFrames(50.0) == 12, "a frame's rounding up");
            Program.Near((float)FollowGate.PacedBudgetNs(0.5, 1.0), 500000f, 1f, "the budget at scale 1 is the time asked");
            Program.Near((float)FollowGate.PacedBudgetNs(0.5, 2.0), 250000f, 1f, "work that costs twice what the costs say gets half the costs' budget");
            Program.Check(FollowGate.PacedBudgetNs(0.5, 1e6) == 0.5e6 / 20.0 && FollowGate.PacedBudgetNs(0.5, 0.0) == 0.5e6 / 0.2, "the scale is kept between a fifth and 20");
            double scale = 1.0;
            for (int i = 0; i < 40; i++)
                scale = FollowGate.NextScale(scale, 3000.0, 1000.0);
            Program.Check(Math.Abs(scale - 3.0) < 0.05, "the scale follows the real cost: " + scale);
            Program.Check(FollowGate.NextScale(2.0, 0.0, 0.0) == 2.0, "no costs, no change");
            Program.Near((float)FollowGate.NextScale(1.0, 3000.0, 1000.0), 1.5f, 1e-5f, "one frame that cost three times the costs moves the scale a quarter of the way: " + FollowGate.NextScale(1.0, 3000.0, 1000.0));
            Program.Check(FollowGate.Writes(FeatureState.On) && !FollowGate.Writes(FeatureState.Log) && !FollowGate.Writes(FeatureState.Shadow) && !FollowGate.Writes(FeatureState.Off), "only on writes");
            Program.Check(FollowWatch<int, int>.TileOf(new Vec3(-30000f, 0f, -30000f)) >= 0 && FollowWatch<int, int>.TileOf(new Vec3(30000f, 0f, 30000f)) >= 0, "a tile number is never negative");
            Program.Check(FollowWatch<int, int>.TileOf(new Vec3(100f, 0f, 10f)) != FollowWatch<int, int>.TileOf(new Vec3(400f, 0f, 10f)) && FollowWatch<int, int>.TileOf(new Vec3(100f, 0f, 10f)) == FollowWatch<int, int>.TileOf(new Vec3(110f, 0f, 20f)), "two places in one tile have one number, two tiles two");
        }

        private static void Between()
        {
            var from = new Vec3(10f, 2f, -5f);
            RigidMotion same = RigidMotion.Between(from, 0.7f, from, 0.7f);
            var p = new Vec3(13f, 4f, 3f);
            Vec3 q = same.Map(p);
            Program.Check((q - p).Length < 1e-4f && Math.Abs(same.MapYaw(1.1f) - 1.1f) < 1e-5f, "the same pose to itself is no motion");
            RigidMotion move = RigidMotion.Between(from, 0.3f, new Vec3(-20f, 5f, 40f), 0.3f);
            Vec3 moved = move.Map(p);
            Program.Check((moved - new Vec3(-17f, 7f, 48f)).Length < 1e-3f && Math.Abs(move.MapYaw(0.5f) - 0.5f) < 1e-5f, "a shift: " + moved.X + " " + moved.Y + " " + moved.Z);
            RigidMotion turn = RigidMotion.Between(from, 0f, from, (float)Math.PI / 2f);
            Vec3 turned = turn.Map(from + new Vec3(0f, 0f, 3f));
            Program.Check((turned - (from + new Vec3(3f, 0f, 0f))).Length < 1e-3f, "a quarter turn clockwise from above takes a place ahead of the post to its right (yaw 0 faces +Z): " + turned.X + " " + turned.Z);
            Program.Check(Math.Abs(turn.MapYaw(0f) - (float)Math.PI / 2f) < 1e-4f && Math.Abs(turn.Turn - (float)Math.PI / 2f) < 1e-4f, "and the facing turns with it");
            RigidMotion wrap = RigidMotion.Between(from, 3f, from, -3f);
            Program.Check(Math.Abs(wrap.Turn - (2f * (float)Math.PI - 6f)) < 1e-4f, "the turn is the short way round: " + wrap.Turn);
        }

        private static void HostMoved()
        {
            var scene = new WScene();
            scene.Hosts[1000] = (new Vec3(50f, 1f, 20f), 0.4f);
            var at = new Vec3(51f, 2f, 22f);
            Program.Check(scene.Hosted(7, 1000, at, 1.0f), "the item is fixed to its post");
            Vec3 post = new Vec3(80f, 3f, -10f);
            scene.MoveHost(1000, post, 1.4f);
            int written = scene.Settle();
            Program.Check(written == 1, "written once: " + written);
            RigidMotion motion = RigidMotion.Between(new Vec3(50f, 1f, 20f), 0.4f, post, 1.4f);
            Vec3 expected = motion.Map(at);
            Program.Check((scene.Items[7].At - expected).Length < 1e-3f, "where the post's motion takes it: " + scene.Items[7].At.X + " " + scene.Items[7].At.Z + " expected " + expected.X + " " + expected.Z);
            Program.Near(scene.Items[7].Yaw, 2.0f, 1e-4f, "facing as much further round as the post turned: " + scene.Items[7].Yaw);
            Program.Near(scene.Items[7].At.Y, 4f, 1e-3f, "at the height the post's foot went up to: " + scene.Items[7].At.Y);
            FollowReport report = scene.Watch.Finished();
            Program.Check(report != null && report.Hosted == 1 && report.Written == 1, "the report counts it as one on a host");
            Program.Check(report.FormatWritten().Contains("1 of the items are fixed to a host"), "the line says so: " + report.FormatWritten());
            scene.Watch.Dirty(1000);
            Program.Check(scene.Settle() == 0, "the post updated for another reason, where it is: nothing is written");
        }

        private static void HostReturns()
        {
            double worst = 0.0, worstYaw = 0.0;
            for (int seed = 0; seed < 20; seed++)
            {
                var random = new Random(31 + seed);
                var scene = new WScene();
                var host0 = new Vec3((float)(random.NextDouble() * 4000 - 2000), (float)(random.NextDouble() * 30), (float)(random.NextDouble() * 4000 - 2000));
                float yaw0 = (float)(random.NextDouble() * 6.0 - 3.0);
                scene.Hosts[1000] = (host0, yaw0);
                var start = new List<(Vec3 At, float Yaw)>();
                for (int i = 0; i < 6; i++)
                {
                    var place = host0 + new Vec3((float)(random.NextDouble() * 6 - 3), (float)(random.NextDouble() * 5), (float)(random.NextDouble() * 6 - 3));
                    float yaw = (float)(random.NextDouble() * 6.0 - 3.0);
                    scene.Hosted(i, 1000, place, yaw);
                    start.Add((place, yaw));
                }

                for (int step = 1; step <= 50; step++)
                {
                    scene.MoveHost(1000, host0 + new Vec3(0.4f * step, 0.02f * step, -0.3f * step), yaw0 + 0.03f * step);
                    scene.Settle();
                }

                scene.MoveHost(1000, host0, yaw0);
                scene.Settle();
                for (int i = 0; i < start.Count; i++)
                {
                    worst = Math.Max(worst, (scene.Items[i].At - start[i].At).Length);
                    worstYaw = Math.Max(worstYaw, YawDiff(scene.Items[i].Yaw, start[i].Yaw));
                }
            }

            Program.Check(worst <= 2e-3 && worstYaw <= 1e-3, "after 50 moves of a post and its being put back every item is within 2 mm and 1 mrad of where it was: " + worst.ToString("0.00000") + " " + worstYaw.ToString("0.00000"));
        }

        private static void HostDetachedAndGone()
        {
            var scene = new WScene();
            scene.Hosts[1000] = (new Vec3(50f, 0f, 20f), 0f);
            scene.Hosted(7, 1000, new Vec3(51f, 0f, 20f), 0f);
            scene.Hosted(8, 1000, new Vec3(49f, 0f, 20f), 0f);
            scene.Items[7] = (new Vec3(60f, 0f, 20f), 0f, null);
            scene.MoveHost(1000, new Vec3(50f, 0f, 30f), 0f);
            scene.Settle();
            Program.Check(scene.Items[7].At.Z == 20f && scene.Items[7].At.X == 60f, "the item someone moved is not dragged");
            Program.Near(scene.Items[8].At.Z, 30f, 1e-3f, "the other goes with its post");
            FollowReport report = scene.Watch.Finished();
            Program.Check(report != null && report.Detached == 1 && report.Written == 1, "one detached, one written");
            scene.MoveHost(1000, new Vec3(50f, 0f, 35f), 0f);
            scene.Settle();
            Program.Near(scene.Items[7].At.Z, 25f, 1e-3f, "the next move takes it on from where it was put: " + scene.Items[7].At.Z);

            // The post is deleted: its items are stale, and not written.
            scene.Watch.EdgeGone(1000);
            scene.MoveHost(1000, new Vec3(50f, 0f, 60f), 0f);
            Program.Check(scene.Settle() == 0 && !scene.Watch.IsHostLinked(7) && !scene.Watch.IsHostLinked(8), "a host that is gone: stale, nothing written");
            // A new post stands there: the item is linked to it and follows it.
            scene.Hosts[1001] = (new Vec3(50f, 0f, 60f), 0f);
            Program.Check(scene.Watch.LinkHosted(7, 1001, new Vec3(50f, 0f, 60f), 0f, scene.Items[7].At, scene.Items[7].Yaw) && scene.Watch.IsHostLinked(7), "linked to the new post");
            scene.MoveHost(1001, new Vec3(70f, 0f, 60f), 0f);
            float before = scene.Items[7].At.X;
            scene.Settle();
            Program.Near(scene.Items[7].At.X, before + 20f, 1e-3f, "and follows it: " + scene.Items[7].At.X);

            // A host with no pose (the entity is gone before the event is told): stale, counted.
            var lost = new WScene();
            lost.Hosts[1000] = (new Vec3(0f, 0f, 0f), 0f);
            lost.Hosted(1, 1000, new Vec3(1f, 0f, 0f), 0f);
            lost.Hosts.Remove(1000);
            lost.Watch.Dirty(1000);
            Program.Check(lost.Settle() == 0 && !lost.Watch.IsHostLinked(1), "a host with no pose: stale");
            FollowReport lostReport = lost.Watch.Finished();
            Program.Check(lostReport != null && lostReport.Unmeasurable == 1, "counted as without a usable host");
            Program.Check(!lost.Watch.LinkHosted(2, 1000, new Vec3(float.NaN, 0f, 0f), 0f, new Vec3(1f, 0f, 0f), 0f), "a pose that is not a number is not linked");
        }

        private static void HostMixed()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f, 3000f);
            scene.Hosts[1000] = (new Vec3(103f, 0f, 500f), 0f);
            for (int i = 0; i < 3; i++)
                scene.Hosted(i, 1000, new Vec3(103f, 0f, 500f), 0f, 9);
            for (int i = 3; i < 30; i++)
                scene.Single(i, 1, new Vec3(103f, 0f, 20f + i * 90f));
            scene.Watch.Scheduler.SetFocus(103f, 500f, false, 0f, 0f);
            scene.Cap = 2;
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(2f, 0f, 0f), 0f));
            scene.MoveHost(1000, new Vec3(103f, 0f, 510f), 0f);
            scene.Step();
            Program.Check(scene.Written.Count == 3 && scene.Written.TrueForAll(w => w.Item < 3), "the three on one post are one group, taken first (nearest the camera) and whole: " + scene.Written.Count);
            scene.Cap = 200;
            scene.Settle();
            Program.Check(scene.Written.Count == 30 && scene.Watch.Settled, "the road's items follow too");
            Program.Near(scene.Items[0].At.Z, 510f, 1e-3f, "the post's: " + scene.Items[0].At.Z);
            Program.Near(scene.Items[10].At.X, 105f, 2e-3f, "and the road's: " + scene.Items[10].At.X);

            // A follow that only logs does not look at hosts.
            var log = new FollowWatch<int, int>();
            log.LinkHosted(5, 1000, new Vec3(0f, 0f, 0f), 0f, new Vec3(1f, 0f, 0f), 0f);
            log.Dirty(1000);
            log.Step(e => null, scene.Place, FollowMode.Fraction, 10, () => false);
            Program.Check(log.Finished() == null && log.Settled && log.IsHostLinked(5), "a follow that only logs takes no look at a host");
        }

        private static void Reports()
        {
            var scene = new WScene();
            scene.Roads[1] = Straight(100f);
            scene.Single(7, 1, new Vec3(103f, 0f, 40f));
            scene.Move(1, FollowFixture.Rigid(scene.Roads[1], new Vec3(10f, 0f, 0f), 0f));
            scene.Settle();
            FollowReport report = scene.Watch.Finished();
            string text = report.FormatWritten();
            Program.Check(text.StartsWith("Follow (written):") && text.Contains("wrote 1 single item(s)") && text.Contains("by a rigid motion"), "the burst's line: " + text);
            string totals = scene.Watch.Totals.TakeWrittenLine();
            Program.Check(totals != null && totals.StartsWith("Follow (written), since the city was loaded:") && totals.Contains("1 single item(s) and 0 line(s) written"), "the totals' line: " + totals);
            Program.Check(scene.Watch.Totals.TakeWrittenLine() == null, "and it is made once");
        }
    }
}
