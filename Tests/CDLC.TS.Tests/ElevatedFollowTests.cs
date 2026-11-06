using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// A wall on a viaduct that Move It moves (0.12.86; the owner: "在高架路上使用MoveIt好像隔音墙之类的跟随计算有问题"). A line item is linked to every road it runs along and each of its points to the road
    /// nearest to it; that was the distance on the ground plane only, so where the viaduct crosses a road on the ground, or runs beside one, the wall's points over it belonged to the road below and stayed
    /// behind when the deck was moved. Here a deck 9 m over a road on the ground is moved sideways and the wall that stands on it is followed as the production LineFollow does it. Pure.
    /// </summary>
    internal static class ElevatedFollowTests
    {
        public static void RunAll()
        {
            Program.Run("elevated follow: a wall on a deck that crosses a road on the ground goes with the deck", Crossing);
            Program.Run("elevated follow: a wall on a deck beside a road on the ground goes with the deck", Beside);
            Program.Run("elevated follow: a road with no shape at the head of the list does not shift the numbering", JunkFirst);
            Program.Run("elevated follow: a wall on the ground under a deck stays with the road on the ground", UnderTheDeck);
            Program.Run("elevated follow: levels (the pure rule) and the cases that must not change", Levels);
            Program.Run("elevated follow: a post is searched a road by its height too (EdgeHint.Nearest)", SearchByHeight);
            Program.Run("elevated follow: the log counts what is linked at another level", LevelCounts);
            Program.Run("elevated follow: the warm-up's scenario follows a moved road", WarmScenario);
            Program.Run("elevated follow: the production map of a line's points takes the road at their level", MapPoints);
            Program.Run("elevated follow: the roads the game updated with no item of ours are counted", UpdatedCensus);
        }

        private static void WarmScenario()
        {
            int written = FollowWarm.Run(2, 3, 1);
            Program.Check(written >= 2, $"the made-up road's move gives the post and the wall a new place ({written} write(s)): the warm-up runs the code that writes");
            Program.Check(FollowWarm.Run(2, 3, 1) == written, "and does the same every time");
        }

        private static void LevelCounts()
        {
            var watch = new FollowWatch<int, int>();
            var deck = new FollowCurve(Deck(0f));
            var ground = new FollowCurve(Ground);
            // A post on the deck linked to the road on the ground (as the plane distance did): 9 m from its road's centre line.
            Program.Check(watch.LinkSingle(1, 7, ground, new Vec3(-3.7f, 9f, 0f), 0f), "a post is linked to the road on the ground");
            Program.Check(watch.Totals.SinglesOtherLevel == 1 && watch.Totals.LevelSamples.Count == 1 && watch.Totals.LevelSamples[0].Contains("+9.0 m"), "and counted at another level, with its height: " + (watch.Totals.LevelSamples.Count > 0 ? watch.Totals.LevelSamples[0] : "-"));
            Program.Check(watch.LinkSingle(2, 8, deck, new Vec3(-3.7f, 9f, 10f), 0f) && watch.Totals.SinglesOtherLevel == 1, "a post on its own deck is not counted");
            Program.Check(watch.LinkSingle(3, 7, ground, new Vec3(-3.7f, 2.9f, 0f), 0f) && watch.Totals.SinglesOtherLevel == 1, "2.9 m is the level (3 m is the limit)");
            Program.Check(watch.LinkSingle(5, 7, ground, new Vec3(-3.7f, 3f, 0f), 0f) && watch.Totals.SinglesOtherLevel == 1, "3 m is the level");
            Program.Check(watch.LinkSingle(4, 7, ground, new Vec3(-3.7f, -3.1f, 0f), 0f) && watch.Totals.SinglesOtherLevel == 2, "3.1 m under it is not (a deck's post at the road's side under it)");

            List<Vec3> points = WallPoints(Deck(0f), 3.7f);
            string path = RunPath.Encode(points, out float yaw);
            Program.Check(watch.LinkLine(10, 8, new[] { 8, 7 }, new[] { deck, ground }, path, points[0], yaw), "the wall on the deck is linked to the deck and the road on the ground");
            Program.Check(watch.Totals.LinesOtherLevel == 0 && watch.Totals.StackedPoints > 0, $"no line at another level; {watch.Totals.StackedPoints} point(s) took the road at their own level: the road on the ground was nearer at the crossing and near it");
            Program.Check(watch.LinkLine(11, 7, new[] { 7 }, new[] { ground }, path, points[0], yaw), "the same wall linked to the road on the ground only");
            Program.Check(watch.Totals.LinesOtherLevel == 1, "is a line at another level");

            watch.Totals.UpdatedRoads = 3;
            watch.Totals.UpdatedElevated = 1;
            watch.Totals.UpdatedNoItems = 2;
            watch.Totals.LinkedWhileUpdating = 4;
            string line = watch.Totals.TakeLevelsLine();
            Program.Check(line != null && line.Contains("updated 3 road(s), 1 of them elevated, 2 with no item") && line.Contains("2 single item(s), 1 line(s)") && line.Contains("4 item(s) were linked while"), "the line says it: " + line);
            Program.Check(watch.Totals.TakeLevelsLine() == null, "and is not repeated while nothing changed");
            watch.Totals.UpdatedRoads++;
            Program.Check(watch.Totals.TakeLevelsLine() != null, "it is made again when something did");
            for (int i = 0; i < 20; i++)
                watch.Totals.NoteOtherLevel("item" + i, 5f, false);
            Program.Check(watch.Totals.LevelSamples.Count == FollowTotals.MaxLevelSamples, "at most " + FollowTotals.MaxLevelSamples + " samples are kept");
        }

        private static void SearchByHeight()
        {
            // Edge 0: the road on the ground along x = -2; edge 1: the deck 9 m over it along x = 0. A post on the deck at x = -3.7 is nearer the road on the ground (1.7 m) than the deck (3.7 m) in the plane.
            Bezier3[] curves = { Straight(new Vec3(-2f, 0f, -100f), new Vec3(-2f, 0f, 100f)), Straight(new Vec3(0f, 9f, -100f), new Vec3(0f, 9f, 100f)) };
            var boxes = new EdgeBoxes();
            foreach (Bezier3 c in curves)
                boxes.Add(c.A.X, c.A.Z, c.B.X, c.B.Z, c.C.X, c.C.Z, c.D.X, c.D.Z);
            var scratch = new List<int>();
            int OnDeck(float y) => EdgeHint.Nearest(boxes, i => curves[i], -3.7f, y, 0f, EdgeHint.Beside, scratch, out _, out _);
            Program.Check(EdgeHint.Nearest(boxes, i => curves[i], -3.7f, 0f, EdgeHint.Beside, scratch, out _, out float plane) == 0 && Math.Abs(plane - 1.7f) < 0.01f, "without a height: the nearest in the plane, as before (the road on the ground, 1.7 m)");
            Program.Check(OnDeck(float.NaN) == 0, "a height that is no number is no height");
            Program.Check(OnDeck(9f) == 1, "a post at the deck's height has the deck");
            Program.Check(OnDeck(0f) == 0, "a post at the ground's height has the road on the ground");
            int deck = EdgeHint.Nearest(boxes, i => curves[i], -3.7f, 9f, 0f, EdgeHint.Beside, scratch, out float along, out float distance);
            Program.Check(deck == 1 && Math.Abs(distance - 3.7f) < 0.01f && along > 0.45f && along < 0.55f, $"and its distance and place along the road are the deck's ({distance:0.00} m, t {along:0.00})");
            Program.Check(EdgeHint.Nearest(boxes, i => curves[i], -40f, 9f, 0f, EdgeHint.Beside, scratch, out _, out _) == -1, "out of reach: none");
            Program.Check(EdgeHint.Nearest(boxes, i => curves[i], 9f, 9f, 0f, 3f, scratch, out _, out _) == -1, "a radius that holds no road: none");

            // A road whose box holds the point but which is farther than the radius is no candidate, and the index returned is still the right edge's (edge 0 is a wide U: 15 m from the point at its nearest).
            Bezier3[] withU = { new Bezier3(new Vec3(-30f, 0f, -30f), new Vec3(-30f, 0f, 30f), new Vec3(30f, 0f, 30f), new Vec3(30f, 0f, -30f)), curves[0], curves[1] };
            var wide = new EdgeBoxes();
            foreach (Bezier3 c in withU)
                wide.Add(c.A.X, c.A.Z, c.B.X, c.B.Z, c.C.X, c.C.Z, c.D.X, c.D.Z);
            int pick = EdgeHint.Nearest(wide, i => withU[i], -3.7f, 9f, 0f, EdgeHint.Beside, scratch, out float alongU, out float distanceU);
            Program.Check(pick == 2 && Math.Abs(distanceU - 3.7f) < 0.01f, $"the edges that were skipped do not shift the answer: edge {pick}, {distanceU:0.00} m");
        }

        private static void MapPoints()
        {
            // The production map of a line's points (RoadRelation.TryMapPoints, the reference of the batch path) takes the road at the point's level too.
            Bezier3 deckBefore = Deck(0f), deckAfter = Deck(5f);
            var pairs = new List<RoadRelation.RoadPair>
            {
                new RoadRelation.RoadPair { Old = new ArcTable(Ground), Now = new ArcTable(Ground) },
                new RoadRelation.RoadPair { Old = new ArcTable(deckBefore), Now = new ArcTable(deckAfter) }
            };
            List<Vec3> points = WallPoints(deckBefore, 3.7f);
            var mapped = new List<Vec3>();
            Program.Check(RoadRelation.TryMapPoints(pairs, FollowMode.Fraction, points, mapped) && mapped.Count == points.Count, "the wall's points are mapped");
            float worst = 0f;
            for (int i = 0; i < points.Count; i++)
                worst = Math.Max(worst, (mapped[i] - (points[i] + new Vec3(5f, 0f, 0f))).Length);
            Program.Check(worst < 0.05f, $"all of them go 5 m with the deck, those over the crossing too: worst {worst:0.000} m");
        }

        private static void UpdatedCensus()
        {
            var watch = new FollowWatch<int, int>();
            var table = new FollowCurve(Ground);
            Program.Check(watch.LinkSingle(1, 7, table, new Vec3(0f, 0f, 3f), 0f), "an item is linked to road 7");
            watch.Dirty(7);
            watch.Dirty(99);
            watch.Step(e => table, (int item, bool wantPath, out Vec3 position, out float yaw, out string path) => { position = new Vec3(0f, 0f, 3f); yaw = 0f; path = null; return true; }, FollowMode.Fraction, 200, () => false);
            Program.Check(watch.Totals.UpdatedNoItems == 1, $"a road the game updated that has no item of ours is counted ({watch.Totals.UpdatedNoItems}), the one with an item is not");

            var writing = new FollowWatch<int, int> { Writing = true };
            Program.Check(writing.LinkSingle(1, 7, table, new Vec3(0f, 0f, 3f), 0f), "an item is linked to road 7 in the writing follow");
            writing.Dirty(7);
            writing.Dirty(99);
            writing.StepWrite(e => table, (int item, bool wantPath, out Vec3 position, out float yaw, out string path) => { position = new Vec3(0f, 0f, 3f); yaw = 0f; path = null; return true; }, FollowMode.Fraction, 1, 5e6, 200, new List<FollowWrite<int>>());
            Program.Check(writing.Totals.UpdatedNoItems == 1, $"and in the writing follow too ({writing.Totals.UpdatedNoItems})");
        }

        private static readonly Bezier3 Ground = Straight(new Vec3(-100f, 0f, 0f), new Vec3(100f, 0f, 0f));

        private static Bezier3 Straight(Vec3 a, Vec3 d) => new Bezier3(a, Vec3.Lerp(a, d, 1f / 3f), Vec3.Lerp(a, d, 2f / 3f), d);

        /// <summary>A deck 9 m over the ground that crosses the road on the ground at right angles at x = 0.</summary>
        private static Bezier3 Deck(float shiftX) => Straight(new Vec3(shiftX, 9f, -100f), new Vec3(shiftX, 9f, 100f));

        /// <summary>A wall 3.7 m to the right of the deck's way (x - 3.7 for a road that runs +z), as the path of a line item: points every 4 m on the deck's height.</summary>
        private static List<Vec3> WallPoints(Bezier3 deck, float lateral)
        {
            var points = new List<Vec3>();
            for (float z = -60f; z <= 60f; z += 4f)
            {
                float t = (z + 100f) / 200f;
                Vec3 on = deck.Point(t);
                points.Add(new Vec3(on.X + lateral, on.Y, z));
            }

            return points;
        }

        private static float Worst(Bezier3 deckBefore, Bezier3 deckAfter, Bezier3 groundBefore, Bezier3 groundAfter, float lateral, bool deckFirst, out int stayed, bool junkFirst = false)
        {
            List<Vec3> points = WallPoints(deckBefore, lateral);
            string path = RunPath.Encode(points, out float yaw);
            Vec3 position = points[0];
            FollowCurve[] old = deckFirst ? new[] { new FollowCurve(deckBefore), new FollowCurve(groundBefore) } : new[] { new FollowCurve(groundBefore), new FollowCurve(deckBefore) };
            FollowCurve[] now = deckFirst ? new[] { new FollowCurve(deckAfter), new FollowCurve(groundAfter) } : new[] { new FollowCurve(groundAfter), new FollowCurve(deckAfter) };
            if (junkFirst)
            {
                // A road with no length at the head of the list (no shape to follow): the roads that are left are numbered after it.
                var none = new FollowCurve(new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f)));
                old = new[] { none, old[0], old[1] };
                now = new[] { none, now[0], now[1] };
            }

            var work = new LineWorkspace();
            Program.Check(LineFollow.TryMeasure(path, position, yaw, old, old.Length, work, out LineRelation relation), "the wall is measured");
            LineOutcome outcome = LineFollow.Apply(relation, now, FollowMode.Fraction, FollowGate.LineChordLimit, work, out string newPath, out Vec3 newPosition, out float newYaw, out _);
            Program.Check(outcome != LineOutcome.Failed, "the wall is followed");
            List<Vec3> after = outcome == LineOutcome.Unchanged ? Decode(path, position, yaw) : Decode(newPath, newPosition, newYaw);
            List<Vec3> expected = WallPoints(deckAfter, lateral);
            float worst = 0f;
            stayed = 0;
            for (int i = 0; i < expected.Count; i++)
            {
                float miss = DistanceToLine(after, expected[i]);
                worst = Math.Max(worst, miss);
                if (miss > 1f)
                    stayed++;
            }

            return worst;
        }

        private static List<Vec3> Decode(string path, Vec3 position, float yaw) => MarkingPolygon.ToWorld(RunPath.Decode(path), position, yaw);

        /// <summary>How far a point is from a polyline (m, in 3D).</summary>
        private static float DistanceToLine(List<Vec3> line, Vec3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 a = line[i], b = line[i + 1], ab = b - a;
                float len2 = ab.X * ab.X + ab.Y * ab.Y + ab.Z * ab.Z;
                float u = len2 <= 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y + (p.Z - a.Z) * ab.Z) / len2));
                best = Math.Min(best, (a + ab * u - p).Length);
            }

            return best;
        }

        private static void Crossing()
        {
            // The wall is 3.7 m to the side of the deck's centre line, and near z = 0 the road on the ground is nearer to its points than the deck is: the deck is 3.7 m away in the plane, the road 0.
            Bezier3 before = Deck(0f), after = Deck(5f);
            foreach (bool deckFirst in new[] { true, false })
            {
                float worst = Worst(before, after, Ground, Ground, 3.7f, deckFirst, out int stayed);
                Program.Check(worst < 0.15f && stayed == 0, $"the whole wall goes 5 m with the deck (deck {(deckFirst ? "first" : "second")}): worst {worst:0.00} m, {stayed} point(s) stayed behind");
            }
        }

        private static void Beside()
        {
            // The road on the ground runs along under the deck, 2 m to the wall's side of the deck's centre line: all along the wall it is nearer to the wall (1.7 m) than the deck's centre line is (3.7 m).
            Bezier3 road = Straight(new Vec3(-2f, 0f, -100f), new Vec3(-2f, 0f, 100f));
            Bezier3 before = Deck(0f), after = Deck(4f);
            foreach (bool deckFirst in new[] { true, false })
            {
                float worst = Worst(before, after, road, road, -3.7f, deckFirst, out int stayed);
                Program.Check(worst < 0.15f && stayed == 0, $"the wall goes with the deck beside a road on the ground (deck {(deckFirst ? "first" : "second")}): worst {worst:0.00} m, {stayed} point(s) stayed behind");
            }
        }

        private static void JunkFirst()
        {
            Bezier3 before = Deck(0f), after = Deck(5f);
            float worst = Worst(before, after, Ground, Ground, 3.7f, true, out int stayed, true);
            Program.Check(worst < 0.15f && stayed == 0, $"the wall goes with the deck with an empty road first in the list: worst {worst:0.00} m, {stayed} point(s) stayed behind");
        }

        private static void UnderTheDeck()
        {
            // A wall on the ground beside the road on the ground, under a deck that is moved away: the wall must stay with the road (the deck is 9 m over it).
            Bezier3 deckBefore = Deck(0f), deckAfter = Deck(6f);
            var points = new List<Vec3>();
            for (float x = -60f; x <= 60f; x += 2f)
                points.Add(new Vec3(x, 0f, 3.7f));
            string path = RunPath.Encode(points, out float yaw);
            FollowCurve[] old = { new FollowCurve(Ground), new FollowCurve(deckBefore) };
            FollowCurve[] now = { new FollowCurve(Ground), new FollowCurve(deckAfter) };
            var work = new LineWorkspace();
            Program.Check(LineFollow.TryMeasure(path, points[0], yaw, old, old.Length, work, out LineRelation relation), "the wall on the ground is measured");
            LineOutcome outcome = LineFollow.Apply(relation, now, FollowMode.Fraction, FollowGate.LineChordLimit, work, out string newPath, out Vec3 newPosition, out float newYaw, out _);
            Program.Check(outcome == LineOutcome.Unchanged, "the road on the ground did not move: the wall under the deck did not either (" + outcome + ")");
            if (outcome != LineOutcome.Unchanged)
            {
                List<Vec3> after = Decode(newPath, newPosition, newYaw);
                float worst = 0f;
                foreach (Vec3 p in points)
                    worst = Math.Max(worst, DistanceToLine(after, p));
                Program.Check(worst < 0.05f, $"and it stood where it was: worst {worst:0.00} m");
            }
        }

        private static void Levels()
        {
            float[] distance = new float[4], roadY = new float[4];
            int Pick(int count, float pointY, params float[] pairs)
            {
                for (int i = 0; i < count; i++)
                {
                    distance[i] = pairs[i * 2];
                    roadY[i] = pairs[i * 2 + 1];
                }

                return RoadLevels.Pick(distance, roadY, count, pointY);
            }

            Program.Check(Pick(0, 0f) == -1, "no road: none");
            Program.Check(Pick(1, 50f, 4f, 0f) == 0, "one road: it, however far above or below it the point is");
            Program.Check(Pick(2, 0.2f, 3f, 0f, 2f, 0.5f) == 1, "two roads at one level: the nearer in the plane, as before");
            Program.Check(Pick(2, 0f, 3f, 0f, 3f, 0f) == 0, "a tie keeps the first, as before");
            Program.Check(Pick(2, 9f, 3.7f, 9f, 0f, 0f) == 0, "a point on the deck: the deck, though the road on the ground is nearer in the plane");
            Program.Check(Pick(2, 9f, 0f, 0f, 3.7f, 9f) == 1, "(the other order)");
            Program.Check(Pick(2, 0f, 2f, 0f, 0.5f, 9f) == 0, "a point on the ground: the road on the ground, though the deck over it is nearer in the plane");
            Program.Check(Pick(2, 4.5f, 2f, 0f, 3f, 9f) == 0, "a point between the levels, 4.5 m from each: the nearer in the plane, as before (both are off its level)");
            Program.Check(Pick(2, 5f, 6f, 0f, 1f, 9f) == 1, "a point 4 m under the deck and 5 m over the road: neither is at its level (3 m), the nearer in the plane");
            Program.Check(Pick(3, 9f, 1f, 0f, 2f, 9f, 3f, 9.5f) == 1, "three roads: the two decks at its level, the nearer of them, the road on the ground is left out");
            Program.Check(Pick(2, 5f, 3f, 0f, 2f, 3f) == 1, "two roads 3 m apart in height are not stacked (a ramp beside a road): the nearer in the plane");
            Program.Check(Pick(2, 6f, 3f, 3f, 2f, 0f) == 1, "a cutting: the point 3 m over one road and 6 m over the other: not at the level of either by the 3 m rule at 6 m: the nearer");
            Program.Check(Pick(2, 5f, 1f, 0f, 3f, 5f) == 1, "roads 5 m apart in height are stacked: the one at the point's level, though the other is nearer");
            Program.Check(Pick(2, 5f, 1f, 0f, 3f, 4f) == 0, "roads exactly 4 m apart are not: the nearer in the plane");
            Program.Check(Pick(2, 3f, 2f, 0f, 1f, 9f) == 0, "a point exactly 3 m over a road is at its level");
            Program.Check(Pick(2, 3.01f, 2f, 0f, 1f, 9f) == 1, "3.01 m over it is not: no road is at its level, the nearer in the plane");
            Program.Check(Pick(3, 3f, 1f, 0f, 3f, 5f, 2f, 12f) == 0, "two roads at the point's level 5 m apart (one exactly 3 m from it) are not stacked on each other: the nearer of them, the road far above is left out");
            Program.Check(RoadLevels.LevelGap == 3f && RoadLevels.StackGap == 4f, "the two limits");
        }
    }
}
