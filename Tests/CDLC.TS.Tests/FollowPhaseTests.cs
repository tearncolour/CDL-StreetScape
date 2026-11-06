using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// A wall's pieces after the follow (0.12.83 follow work, the owner: "隔音墙的MoveIt跟随不是很好"): the pieces carry where they start along the whole line (the phase) and the line's length; when a road got
    /// longer or shorter the pieces followed it but kept their phases, so every joint had a panel of the wrong width and a wall that got shorter lost its end. LinePhases makes the phases again from the lengths
    /// the pieces have, and the watch writes them with the pieces (or alone, for a piece whose road did not change). The wall simulation (FollowWallSim) measures the panels with and without it.
    /// </summary>
    internal static class FollowPhaseTests
    {
        public static void RunAll()
        {
            Program.Run("phases: the pieces' phases are the sums of the lengths before them, the total the sum of all", Rebase);
            Program.Run("phases: pieces that are not laid end to end, or numbers that are not numbers, are left", NotAChain);
            Program.Run("phases: a template's phase and total are set, and a setting with none is left as it is", Template);
            Program.Run("phases: a wall on a road that got longer or shorter keeps its panels (the watch writes the new phases)", WallPanels);
            Program.Run("phases: a wall moved twice and a wall on a hill: the phases are always the ground's", WallsMovedTwice);
            Program.Run("phases: a piece whose road did not change gets a write of its phase alone, and a wall moved bodily none", PhaseOnlyAndBodily);
            Program.Run("phases: a run that is cut or has a piece that is gone is left, and counted", LeftRuns);
            Program.Run("phases: a line followed over several frames is made again once, when its last piece is followed", OverSeveralFrames);
            Program.Run("phases: the last piece of a line, moved only, still gets its phase", LastPieceKeepsItsLength);
            Program.Run("phases: a result that waits to be written keeps its line waiting", ReadyPieces);
            Program.Run("phases: a line as long as it was keeps the phase of the pieces before the change, and the total", TotalKept);
            Program.Run("phases: pieces with no total, no group or no phase get none", NoPhaseNoWrite);
            Program.Run("phases: a run with a stale piece is left and counted", StalePiece);
            Program.Run("phases: the wall simulation, a node dragged on a curved chain: the panels are the fresh placement's", Simulation);
        }

        private static void Rebase()
        {
            var phase = new List<long> { 0, 6400, 12800 };
            var at = new List<float> { 64f, 64f, 40f };
            var now = new List<float> { 66.5f, 64f, 41.25f };
            var made = new long[3];
            Program.Check(LinePhases.TryRebase(phase, at, now, 16800, made, out long total), "three pieces laid end to end are a chain");
            Program.Check(made[0] == 0 && made[1] == 6650 && made[2] == 13050 && total == 17175, $"the phases {string.Join(",", made)} and the total {total}");
            var shuffled = new List<long> { 12800, 0, 6400 };
            var shuffledAt = new List<float> { 40f, 64f, 64f };
            var shuffledNow = new List<float> { 41.25f, 66.5f, 64f };
            var again = new long[3];
            Program.Check(LinePhases.TryRebase(shuffled, shuffledAt, shuffledNow, 16800, again, out long other), "the order the caller gives them in does not matter");
            Program.Check(again[0] == 13050 && again[1] == 0 && again[2] == 6650 && other == 17175, "each piece gets its own phase: " + string.Join(",", again));
            var shorter = new List<float> { 60f, 64f, 36f };
            var shortMade = new long[3];
            Program.Check(LinePhases.TryRebase(phase, at, shorter, 16800, shortMade, out long shortTotal) && shortMade[2] == 12400 && shortTotal == 16000, "a run that got shorter has a shorter total");
            Program.Check(LinePhases.TryRebase(new List<long> { 500 }, new List<float> { 10f }, new List<float> { 10.4f }, 1500, new long[1], out long one) && one == 1540, "one piece that does not start at 0 keeps its start: " + one);
            var same = new long[3];
            Program.Check(LinePhases.TryRebase(phase, at, at, 16800, same, out long unchanged) && same.SequenceEqual(phase) && unchanged == 16800, "lengths that did not change give the phases that were");
            Program.Check(LinePhases.ChainToleranceCm == LineCut.PhaseSlackCm && LineCut.PhaseSlackCm == 6, "the slack is the one LineCut takes: 6 cm");
            Program.Check(LinePhases.TryRebase(new List<long> { 0, 6403 }, new List<float> { 64f, 10f }, new List<float> { 64f, 10f }, 7403, new long[2], out _), "3 cm of rounding is within the tolerance");
            Program.Check(!LinePhases.TryRebase(new List<long> { 0, 6407 }, new List<float> { 64f, 10f }, new List<float> { 64f, 10f }, 7407, new long[2], out _), "7 cm is not");
            Program.Check(LinePhases.TryRebase(new List<long> { 0, 6406 }, new List<float> { 64f, 10f }, new List<float> { 64f, 10f }, 7406, new long[2], out _), "6 cm is (the tolerance is inclusive)");
            Program.Check(LinePhases.TryRebase(new List<long> { 0 }, new List<float> { 64f }, new List<float> { 64f }, 6406, new long[1], out _) && !LinePhases.TryRebase(new List<long> { 0 }, new List<float> { 64f }, new List<float> { 64f }, 6407, new long[1], out _), "and so is the total's");
        }

        private static void NotAChain()
        {
            var made = new long[3];
            Program.Check(!LinePhases.TryRebase(new List<long> { 0, 6400, 9000 }, new List<float> { 64f, 64f, 10f }, new List<float> { 64f, 64f, 10f }, 9100, made, out _), "a piece that does not start where the one before ended");
            Program.Check(!LinePhases.TryRebase(new List<long> { 0, 6400, 12800 }, new List<float> { 64f, 64f, 10f }, new List<float> { 64f, 64f, 10f }, 20000, made, out _), "a total that is not where the last ends");
            Program.Check(!LinePhases.TryRebase(new List<long> { 0, 0, 6400 }, new List<float> { 64f, 64f, 10f }, new List<float> { 64f, 64f, 10f }, 7400, made, out _), "two pieces that start together");
            Program.Check(!LinePhases.TryRebase(new List<long>(), new List<float>(), new List<float>(), 0, made, out _), "no pieces");
            Program.Check(!LinePhases.TryRebase(new List<long> { -1 }, new List<float> { 1f }, new List<float> { 1f }, 100, new long[1], out _), "a negative phase");
            Program.Check(!LinePhases.TryRebase(new List<long> { 0 }, new List<float> { float.NaN }, new List<float> { 1f }, 100, new long[1], out _) && !LinePhases.TryRebase(new List<long> { 0 }, new List<float> { 1f }, new List<float> { float.PositiveInfinity }, 100, new long[1], out _), "a length that is not a number");
            Program.Check(!LinePhases.TryRebase(new List<long> { 0, 6400 }, new List<float> { 64f }, new List<float> { 64f, 64f }, 0, made, out _), "lists of different lengths");
            Program.Check(LinePhases.TryRebase(new List<long> { 0, 6400 }, new List<float> { 64f, 64f }, new List<float> { 64f, 64f }, 0, made, out long open) && open == 12800, "a total that is not known (0) is not checked");
            Program.Check(!LinePhases.TryRebase(new List<long> { 0 }, new List<float> { float.NaN }, new List<float> { 1f }, 0, new long[1], out _), "a length at the phase that is not a number, with no total to catch it");
            Program.Check(!LinePhases.TryRebase(new List<long> { 0 }, new List<float> { 1f }, new List<float> { float.NaN }, 0, new long[1], out _), "a length now that is not a number, with no total to catch it");
        }

        private static void Template()
        {
            GuideTemplate template = MarkingTemplates.ById("bar.noise");
            string spec = MarkingTemplates.WithRun(template, template.Default().ToString(), "100:0:0", 12.5f, 150f);
            string back = MarkingTemplates.WithPhase(template, spec, 1337, 18000);
            GuideSpec parsed = template.Parse(back);
            Program.Check(parsed.Get(MarkingTemplates.PhaseKey) == "1337" && parsed.Get(MarkingTemplates.TotalKey) == "18000" && parsed.Get(RunPath.Key) == "100:0:0", "the phase and the total are set and the path is kept: " + back);
            Program.Check(MarkingTemplates.WithPhase(template, spec, -1, 18000) == template.Parse(spec).ToString(), "a negative phase changes nothing");
            string noTotal = MarkingTemplates.WithPhase(template, spec, 700, 0);
            Program.Check(template.Parse(noTotal).Get(MarkingTemplates.PhaseKey) == "700" && template.Parse(noTotal).Get(MarkingTemplates.TotalKey) == "15000", "a total of 0 leaves the total that was");
            GuideTemplate plain = MarkingTemplates.ById("mk.stop");
            string stop = plain.Default().ToString();
            Program.Check(MarkingTemplates.WithPhase(plain, stop, 100, 200) == plain.Parse(stop).ToString(), "settings that have no phase are returned as they are");
        }

        // ------------------------------------------------------------------ a wall on the watch

        private sealed class Wall
        {
            public readonly FollowWatch<int, int> Watch = new FollowWatch<int, int> { Writing = true };
            public readonly Dictionary<int, Bezier3> Roads = new Dictionary<int, Bezier3>();
            public readonly FollowCache Cache = new FollowCache();
            public readonly List<FollowWrite<int>> Writes = new List<FollowWrite<int>>();
            public readonly Dictionary<int, (Vec3 At, float Yaw, string Path, long Phase, long Total)> State = new Dictionary<int, (Vec3, float, string, long, long)>();
            public int Frame, PhaseOnlyWrites, PhaseWrites, PhaseOnlyMoved, Cap = 200;
            public readonly List<FollowWrite<int>> AllWrites = new List<FollowWrite<int>>();
            public FollowWallSim.Chain Chain;
            public int Pieces;

            /// <summary>perRoad: each piece is linked to the roads it lies on and not to all of them; totalUnknown: the pieces' settings have a phase and no total.</summary>
            public Wall(FollowWallSim.Chain chain, float lateral, int group = 1, bool phased = true, bool perRoad = false, bool totalUnknown = false)
            {
                Chain = chain;
                for (int i = 0; i < chain.Roads.Length; i++)
                    Roads[i + 1] = chain.Roads[i];
                List<FollowWallSim.Item> items = FollowWallSim.Place(chain, lateral);
                Pieces = items.Count;
                long total = (long)Math.Round((items[items.Count - 1].PhaseM + items[items.Count - 1].PathLength) * 100.0);
                int[] edges = Enumerable.Range(1, chain.Roads.Length).ToArray();
                for (int i = 0; i < items.Count; i++)
                {
                    long phase = (long)Math.Round(items[i].PhaseM * 100.0);
                    State[i] = (items[i].Position, items[i].Yaw, items[i].Path, phase, total);
                    int[] own = perRoad ? edges.Where(e => chain.Start[e - 1] < items[i].PhaseM + items[i].PathLength - 0.5f && chain.Start[e - 1] + chain.Arcs[e - 1].Length > items[i].PhaseM + 0.5f).ToArray() : edges;
                    FollowCurve[] tables = own.Select(TableOf).ToArray();
                    Watch.LinkLine(i, own[0], own, tables, items[i].Path, items[i].Position, items[i].Yaw, group, phased ? phase : -1, phased && !totalUnknown ? total : 0);
                }
            }

            public FollowCurve TableOf(int edge) => Roads.TryGetValue(edge, out Bezier3 curve) ? Cache.Get(curve) : null;

            public bool Place(int item, bool wantPath, out Vec3 at, out float yaw, out string path)
            {
                at = default;
                yaw = 0f;
                path = null;
                if (!State.TryGetValue(item, out var s))
                    return false;
                at = s.At;
                yaw = s.Yaw;
                path = wantPath ? s.Path : null;
                return true;
            }

            public void Move(int edge, Bezier3 curve)
            {
                Roads[edge] = curve;
                Watch.Dirty(edge);
            }

            public void Settle()
            {
                for (int i = 0; i < 400 && (!Watch.Settled || i == 0); i++)
                    Step();
            }

            /// <summary>One frame of the follow, its writes applied to the pieces as the system applies them.</summary>
            public void Step()
            {
                Writes.Clear();
                if (Watch.StepWrite(TableOf, Place, FollowMode.Fraction, Frame, 1e9, Cap, Writes))
                    Watch.NoteFrame(0.1);
                foreach (FollowWrite<int> w in Writes)
                {
                    AllWrites.Add(w);
                    var s = State[w.Item];
                    if (w.PhaseOnly && (!w.Position.Equals(s.At) || w.Yaw != s.Yaw))
                        PhaseOnlyMoved++;
                    if (!w.PhaseOnly)
                        s = (w.Position, w.Yaw, s.Path, s.Phase, s.Total);
                    if (w.Path != null)
                        s.Path = w.Path;
                    if (w.HasPhase)
                    {
                        s.Phase = w.PhaseCm;
                        s.Total = w.TotalCm;
                        PhaseWrites++;
                        if (w.PhaseOnly)
                            PhaseOnlyWrites++;
                    }

                    State[w.Item] = s;
                }

                Frame++;
            }

            /// <summary>The widths of the panels (posts every 4 m from the line's start) with the phases and lengths the pieces have now.</summary>
            public List<float> Panels() => FollowWallSim.Panels(Enumerable.Range(0, Pieces).Select(i => (State[i].Phase / 100f, RunPath.Length(State[i].Path))).ToList(), 4f);

            /// <summary>The largest distance between where a piece's phase says it starts and where the pieces before it end (m).</summary>
            public float PhaseError()
            {
                float run = 0f, worst = 0f;
                for (int i = 0; i < Pieces; i++)
                {
                    worst = Math.Max(worst, Math.Abs(State[i].Phase / 100f - run));
                    run += RunPath.Length(State[i].Path);
                }

                return worst;
            }
        }

        private static void WallPanels()
        {
            foreach (float stretch in new[] { 0.02f, 0.05f, 0.08f, -0.03f })
            {
                FollowWallSim.Chain chain = FollowWallSim.Bend(1, 320f, 150f);
                var wall = new Wall(chain, 3.7f);
                Program.Check(wall.Pieces >= 4 && wall.PhaseError() < 0.06f, "a wall of several pieces placed: " + wall.Pieces + " pieces, phases off by " + wall.PhaseError());
                wall.Move(1, FollowWallSim.Dragged(chain, FollowWallSim.AlongFor(chain, stretch))[0]);
                wall.Settle();
                var after = new FollowWallSim.Chain(new[] { wall.Roads[1] });
                float shortest = wall.Panels().Min(), longest = wall.Panels().Max();
                Program.Check(shortest > 3.9f && longest < 4.1f, $"stretch {stretch * 100:0}%: every panel is 4 m (4 m posts, the line is {after.Length:0.0} m): {shortest:0.000} to {longest:0.000}");
                Program.Check(wall.PhaseError() < 0.06f, $"and every piece starts where the one before ends: off by {wall.PhaseError():0.000} m");
                long end = wall.State[wall.Pieces - 1].Phase + (long)Math.Round(RunPath.Length(wall.State[wall.Pieces - 1].Path) * 100.0);
                Program.Check(Math.Abs(wall.State[0].Total - end) <= 5 && Enumerable.Range(0, wall.Pieces).All(i => wall.State[i].Total == wall.State[0].Total), $"all of them have the new total, where the last ends: {wall.State[0].Total} against {end}");
                Program.Check(wall.State[0].Phase == 0, "the first still starts at 0");
                Program.Check(wall.PhaseWrites >= wall.Pieces - 1, $"the pieces after the first have their phases in their writes: {wall.PhaseWrites}");
                Program.Check(wall.PhaseOnlyWrites == 0, "and every piece was followed in the same frame, so none needed a write of its own");
                FollowReport report = wall.Watch.Finished();
                Program.Check(report != null && report.PhasesWritten == wall.PhaseWrites && report.RunsNotRebased == 0, "the report counts them");
            }
        }

        private static void WallsMovedTwice()
        {
            // The phases a burst wrote are the ones the next burst starts from: the same wall dragged longer and then shorter than it was, and then back.
            FollowWallSim.Chain chain = FollowWallSim.Bend(1, 320f, 150f);
            var wall = new Wall(chain, 3.7f);
            foreach (float stretch in new[] { 0.06f, -0.04f, 0.0f, 0.03f })
            {
                wall.Move(1, FollowWallSim.Dragged(chain, FollowWallSim.AlongFor(chain, stretch))[0]);
                wall.Settle();
                float shortest = wall.Panels().Min(), longest = wall.Panels().Max();
                Program.Check(shortest > 3.9f && longest < 4.1f && wall.PhaseError() < 0.06f, $"after {stretch * 100:0}% (not the first move): panels {shortest:0.000} to {longest:0.000}, phases off by {wall.PhaseError():0.000} m");
            }

            // The phases are of the ground: a wall on a road that climbs has its pieces' phases counted on the ground, not along the slope.
            FollowWallSim.Chain hill = FollowWallSim.Bend(1, 320f, 150f, climb: 25f);
            var climbing = new Wall(hill, 3.7f);
            climbing.Move(1, FollowWallSim.Dragged(hill, FollowWallSim.AlongFor(hill, 0.05f))[0]);
            climbing.Settle();
            Program.Check(climbing.Panels().Min() > 3.9f && climbing.Panels().Max() < 4.1f && climbing.PhaseError() < 0.06f, $"a climbing wall: panels {climbing.Panels().Min():0.000} to {climbing.Panels().Max():0.000}, phases off by {climbing.PhaseError():0.000} m");
        }

        private static void PhaseOnlyAndBodily()
        {
            // A wall on two roads: only the first road gets longer. The pieces on the second are not followed, and their phases move with the pieces before them.
            FollowWallSim.Chain chain = FollowWallSim.Bend(2, 200f, 150f);
            var wall = new Wall(chain, 3.7f);
            Bezier3 longer = FollowWallSim.DragStart(chain.Roads[0], Vec3.Zero);
            Vec3 dir = chain.Roads[0].Tangent(0f).HeadingXZ();
            longer = FollowWallSim.DragStart(chain.Roads[0], dir * -9f);
            Vec3 startMoved = longer.A;
            wall.Move(1, longer);
            wall.Settle();
            var fresh = new FollowWallSim.Chain(new[] { longer, chain.Roads[1] });
            Program.Check(Math.Abs(fresh.Length - chain.Length) > 5f, "the first road is a good deal longer: " + (fresh.Length - chain.Length));
            Program.Check(wall.PhaseOnlyWrites > 0, $"the pieces whose road did not change are written for their phase alone: {wall.PhaseOnlyWrites}");
            Program.Check(wall.PhaseOnlyMoved == 0, $"and such a write carries the place and the facing the piece has: {wall.PhaseOnlyMoved} of them did not");
            Program.Check(wall.PhaseError() < 0.06f, "and every piece starts where the one before ends: " + wall.PhaseError());
            var lastPiece = wall.State[wall.Pieces - 1];
            Program.Check(Enumerable.Range(0, wall.Pieces).All(i => wall.State[i].Total == lastPiece.Total), "one total for all");

            // A wall moved bodily: the pieces are the same length, the phases are not touched, nothing is written for them.
            var rigid = new Wall(FollowWallSim.Bend(2, 200f, 150f), 3.7f);
            for (int i = 1; i <= 2; i++)
                rigid.Move(i, FollowWallSim.RigidAbout(rigid.Roads[i], rigid.Roads[1].A, new Vec3(30f, 1f, -20f), 0.3f));
            rigid.Settle();
            Program.Check(rigid.PhaseWrites == 0 && rigid.PhaseError() < 0.06f, $"moved bodily: no phase is written ({rigid.PhaseWrites}) and they are right");

            // A piece that is written alone with a phase only is not moved: its place is as it was.
            var alone = new Wall(FollowWallSim.Bend(2, 200f, 150f), 3.7f);
            Vec3 where = alone.State[alone.Pieces - 1].At;
            float facing = alone.State[alone.Pieces - 1].Yaw;
            alone.Move(1, FollowWallSim.DragStart(alone.Roads[1], alone.Chain.Roads[0].Tangent(0f).HeadingXZ() * -9f));
            alone.Settle();
            Program.Check(alone.State[alone.Pieces - 1].At.Equals(where) && alone.State[alone.Pieces - 1].Yaw == facing, "the last piece's place is not changed by the write of its phase");
        }

        private static FollowWallSim.Chain Straight200() => new FollowWallSim.Chain(new[] { Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f)), Bezier3.Line(new Vec3(0f, 0f, 100f), new Vec3(0f, 0f, 200f)) });

        private static void NodeAlong(Wall wall, float z)
        {
            wall.Move(1, Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, z)));
            wall.Move(2, Bezier3.Line(new Vec3(0f, 0f, z), new Vec3(0f, 0f, 200f)));
        }

        private static void TotalKept()
        {
            // The node between two straight roads goes 3 m along: one road 3 m longer, the other 3 m shorter, the line as long as it was (200 m: the pieces' lengths are exact centimetres).
            var wall = new Wall(Straight200(), 3.7f);
            Program.Check(wall.Pieces == 4 && wall.State[0].Total == 20000, "a wall of four pieces and 200 m: " + wall.Pieces + ", " + wall.State[0].Total);
            NodeAlong(wall, 103f);
            wall.Settle();
            Program.Check(wall.State[0].Phase == 0 && wall.State[1].Phase == 6592 && wall.State[2].Phase == 13016 && wall.State[3].Phase == 19224, "the phases are the sums of the new lengths: " + string.Join(",", Enumerable.Range(0, 4).Select(i => wall.State[i].Phase)));
            Program.Check(Enumerable.Range(0, 4).All(i => wall.State[i].Total == 20000), "and the total is what it was");
            Program.Check(wall.AllWrites.Where(w => w.Item == 0).All(w => !w.HasPhase), "the first piece starts at 0 and the line is as long as it was: nothing of its phase is written");
            Program.Check(wall.AllWrites.Where(w => w.Item == 2).All(w => w.HasPhase && w.PhaseCm == 13016 && w.TotalCm == 20000), "a piece that starts elsewhere has it in its write");
            Program.Check(wall.PhaseError() < 0.011f, "every piece starts where the one before ends: " + wall.PhaseError());
        }

        private static void NoPhaseNoWrite()
        {
            // Pieces that carry no total, or are in no group, are never given a phase.
            foreach ((string what, Wall wall) in new[] { ("no total", new Wall(Straight200(), 3.7f, totalUnknown: true)), ("no group", new Wall(Straight200(), 3.7f, group: 0)), ("no phase", new Wall(Straight200(), 3.7f, phased: false)) })
            {
                NodeAlong(wall, 103f);
                wall.Settle();
                Program.Check(wall.AllWrites.Count > 0 && wall.PhaseWrites == 0 && wall.PhaseOnlyWrites == 0, $"{what}: the pieces follow ({wall.AllWrites.Count} writes) and none is written a phase ({wall.PhaseWrites})");
                FollowReport report = wall.Watch.Finished();
                Program.Check(report != null && report.PhasesWritten == 0 && report.RunsNotRebased == 0, $"{what}: and the report has no run in it");
            }
        }

        private static void StalePiece()
        {
            // The pieces on the second road lose their road: the first road gets longer and its pieces follow, but the run cannot be laid end to end again without the lengths of the others.
            var wall = new Wall(Straight200(), 3.7f, perRoad: true);
            wall.Watch.EdgeGone(2);
            NodeAlong(wall, 103f);
            wall.Settle();
            FollowReport report = wall.Watch.Finished();
            Program.Check(wall.AllWrites.Any(w => w.Item == 0) && wall.PhaseWrites == 0 && report != null && report.RunsNotRebased == 1 && report.PhasesWritten == 0, "a run with a stale piece is left and counted, and no phase is written: " + wall.PhaseWrites + ", " + report?.RunsNotRebased);
        }

        private static void OverSeveralFrames()
        {
            // A wall of several pieces taken two at a time (a line of more than 2 items is cut): the phases are made again once the last piece has been followed, not at each frame's share.
            FollowWallSim.Chain chain = FollowWallSim.Bend(1, 320f, 150f);
            var wall = new Wall(chain, 3.7f) { Cap = 2 };
            wall.Watch.Scheduler.AtomicGroupMax = 2;
            wall.Move(1, FollowWallSim.Dragged(chain, FollowWallSim.AlongFor(chain, 0.05f))[0]);
            wall.Step();
            Program.Check(wall.AllWrites.Count == 2 && wall.Watch.Scheduler.GroupWaiting(1), "the first frame follows two of the pieces and the line still waits: " + wall.AllWrites.Count);
            Program.Check(wall.PhaseWrites == 0, "and no phase is written yet: " + wall.PhaseWrites);
            int frames = 1;
            while (wall.Watch.Scheduler.GroupWaiting(1) && frames < 20)
            {
                Program.Check(wall.PhaseWrites == 0, $"frame {frames}: the line still waits and no phase has been written: {wall.PhaseWrites}");
                wall.Step();
                frames++;
            }

            Program.Check(frames == (wall.Pieces + 1) / 2 && wall.PhaseWrites > 0, $"the frame that follows the last piece writes the phases at once: frame {frames} of {(wall.Pieces + 1) / 2}, {wall.PhaseWrites} written");
            wall.Settle();
            Program.Check(wall.PhaseError() < 0.06f && wall.Panels().Min() > 3.9f && wall.Panels().Max() < 4.1f, $"every piece starts where the one before ends and the panels are 4 m: {wall.PhaseError():0.000}, {wall.Panels().Min():0.00} to {wall.Panels().Max():0.00}");
            Program.Check(wall.PhaseOnlyWrites > 0 && wall.PhaseOnlyMoved == 0, $"the pieces of the first frame are written their phase alone: {wall.PhaseOnlyWrites}, {wall.PhaseOnlyMoved} that moved");
            var s = new FollowScheduler();
            s.Add(0, 7, 0f, 0f, 100f);
            s.Add(1, 7, 10f, 0f, 100f);
            s.Add(2, 0, 20f, 0f, 100f);
            s.Link(0, 1);
            s.Link(2, 1);
            Program.Check(!s.GroupWaiting(7) && !s.GroupWaiting(99), "nothing waits before an event, and a group that is not there does not");
            s.EdgeChanged(1, 1, 0);
            Program.Check(s.GroupWaiting(7) && !s.GroupWaiting(0 + 99), "an event for one piece of a line: the line waits");
            var jobs = new List<FollowJob>();
            s.Take(0, 1e9, 100, jobs);
            Program.Check(!s.GroupWaiting(7), "taken: it is being followed and does not wait (the line's other piece is on no road that changed)");
            foreach (FollowJob j in jobs)
                s.Wrote(j.Item, j.Version);
            Program.Check(!s.GroupWaiting(7), "written: it does not wait");
            var cut = new FollowScheduler { AtomicGroupMax = 1 };
            for (int i = 0; i < 3; i++)
            {
                cut.Add(i, 7, i, 0f, 100f);
                cut.Link(i, 1);
            }

            cut.EdgeChanged(1, 1, 0);
            jobs.Clear();
            cut.Take(0, 1e9, 1, jobs);
            Program.Check(jobs.Count == 1 && cut.GroupWaiting(7), "cut and taken one piece at a time: the line still waits for the other two");
            foreach (FollowJob j in jobs)
                cut.Wrote(j.Item, j.Version);
            jobs.Clear();
            cut.Take(1, 1e9, 5, jobs);
            Program.Check(jobs.Count == 2 && !cut.GroupWaiting(7), "and the rest taken: it does not");
        }

        private static void LastPieceKeepsItsLength()
        {
            // The first road is longer by 3 m and the second is only moved (the same length): the pieces on the second are followed last and by their pose alone, so their own lengths do not change when
            // the line is quiet; the phases are still made again, for the lengths the pieces before them changed in the frames before.
            var wall = new Wall(Straight200(), 3.7f) { Cap = 1 };
            wall.Watch.Scheduler.AtomicGroupMax = 2;
            wall.Move(1, Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 103f)));
            wall.Move(2, Bezier3.Line(new Vec3(0f, 0f, 103f), new Vec3(0f, 0f, 203f)));
            wall.Settle();
            Program.Check(wall.PhaseError() < 0.011f && wall.State[3].Phase > 19200 && wall.State[3].Total == 20300, $"the last piece starts where the one before ends and the total is 3 m more: phase {wall.State[3].Phase}, total {wall.State[3].Total}, off by {wall.PhaseError():0.000}");
            Program.Check(wall.AllWrites.Any(w => w.Item == 3 && w.HasPhase), "and it was written its phase");
        }

        private static void ReadyPieces()
        {
            // A piece that a worker has followed and that waits to be written is waiting too.
            var s = new FollowScheduler();
            for (int i = 0; i < 2; i++)
            {
                s.Add(i, 7, i, 0f, 100f);
                s.Link(i, 1);
            }

            s.EdgeChanged(1, 1, 0);
            var jobs = new List<FollowJob>();
            s.TakeAll(0, 10, jobs);
            Program.Check(jobs.Count == 2 && !s.GroupWaiting(7), "both are with the worker: nothing waits to be followed");
            s.Deliver(0, 1, 0);
            Program.Check(s.GroupWaiting(7), "one result is back and waits to be written: the line waits");
        }

        private static void LeftRuns()
        {
            // A phase that does not match where the piece before ends (a run that was cut by hand): left, and counted.
            FollowWallSim.Chain chain = FollowWallSim.Bend(1, 320f, 150f);
            var cut = new Wall(chain, 3.7f);
            cut.State[2] = (cut.State[2].At, cut.State[2].Yaw, cut.State[2].Path, cut.State[2].Phase + 1000, cut.State[2].Total);
            var tampered = new Wall(chain, 3.7f, phased: false);
            FollowWatch<int, int> watch = new FollowWatch<int, int> { Writing = true };
            var linked = new Wall(chain, 3.7f);
            linked.Watch.Forget(2);
            linked.Move(1, FollowWallSim.Dragged(chain, FollowWallSim.AlongFor(chain, 0.05f))[0]);
            linked.Settle();
            FollowReport report = linked.Watch.Finished();
            Program.Check(linked.PhaseWrites == 0 && report != null && report.RunsNotRebased > 0 && report.PhasesWritten == 0, "a piece that is gone (not linked any more) leaves a run that is not laid end to end: left and counted");

            // Pieces with no phase (placed before the phase was kept, or items that carry none) are never touched.
            tampered.Move(1, FollowWallSim.Dragged(chain, FollowWallSim.AlongFor(chain, 0.05f))[0]);
            tampered.Settle();
            Program.Check(tampered.PhaseWrites == 0, "pieces linked with no phase get none");
        }

        private static void Simulation()
        {
            // The whole simulation's cases, with the phases the watch writes in place of the ones the pieces had: a node dragged, 2 to 8 % longer, on one road and on a chain of four, at 3.7 and 10 m.
            float worst = 0f, worstStale = 0f;
            int cases = 0;
            foreach ((FollowWallSim.Chain chain, string name) in new[] { (FollowWallSim.Bend(1, 160f, 120f), "one road"), (FollowWallSim.Bend(4, 60f, 90f), "four roads"), (FollowWallSim.Bend(4, 80f, 60f), "four tight") })
            {
                foreach (float lateral in new[] { 3.7f, 10f })
                {
                    foreach (float stretch in new[] { 0.02f, 0.05f, 0.08f, -0.02f })
                    {
                        var wall = new Wall(chain, lateral);
                        Bezier3[] now = FollowWallSim.Dragged(chain, FollowWallSim.AlongFor(chain, stretch));
                        float stale = FollowWallSim.Measure("", chain, lateral, now).PanelLongest;
                        for (int i = 0; i < now.Length; i++)
                            wall.Move(i + 1, now[i]);
                        wall.Settle();
                        if (wall.Watch.Finished()?.RunsNotRebased > 0)
                            Program.Check(false, $"{name} at {lateral} m, {stretch * 100:0}%: the run was left");
                        worst = Math.Max(worst, wall.PhaseError());
                        worstStale = Math.Max(worstStale, stale);
                        cases++;
                    }
                }
            }

            Program.Check(cases == 24 && worst < 0.06f, $"{cases} cases: every piece starts where the one before ends, the worst off by {worst:0.000} m (the pieces kept their phases: panels up to {worstStale:0.0} m)");
        }
    }
}
