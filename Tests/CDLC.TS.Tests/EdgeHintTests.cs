using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The road check of the items (0.6.0): the slow-frame log of the 0.6.0 session showed the old check at 0.6 to 3.3 s a round, every 1.5 s, because an item with no road in reach was measured against
    /// every edge of the map every round. The search by boxes must give the same road as measuring them all, and the decision when to look again must not let that happen again.
    /// </summary>
    internal static class EdgeHintTests
    {
        public static void RunAll()
        {
            Program.Run("edge hint: what is done with an item", Decide);
            Program.Run("edge hint: the boxes find the same road as measuring every edge", SameAsBruteForce);
            Program.Run("edge hint: a search over a map's worth of edges is cheap", Cost);
            Program.Run("edge hint: an item with no road in reach is not measured against the map every round", NoRepeatedScans);
        }

        private static Bezier3 Straight(float x0, float z0, float x1, float z1) =>
            new Bezier3(new Vec3(x0, 0f, z0), new Vec3(x0 + (x1 - x0) / 3f, 0f, z0 + (z1 - z0) / 3f), new Vec3(x0 + 2f * (x1 - x0) / 3f, 0f, z0 + 2f * (z1 - z0) / 3f), new Vec3(x1, 0f, z1));

        private static void Add(EdgeBoxes boxes, Bezier3 c) => boxes.Add(c.A.X, c.A.Z, c.B.X, c.B.Z, c.C.X, c.C.Z, c.D.X, c.D.Z);

        private static void Decide()
        {
            Program.Check(EdgeHint.Decide(true, false, 10f, 0f) == EdgeCheck.Beside, "beside its road: nothing to do");
            Program.Check(EdgeHint.Decide(true, true, 10f, 1000f) == EdgeCheck.Beside, "an orphan that is beside a road again is beside it, whatever the wait says");
            Program.Check(EdgeHint.Decide(false, false, 10f, 0f) == EdgeCheck.Scan, "a hint that does not hold is searched at once");
            Program.Check(EdgeHint.Decide(false, false, 10f, 1000f) == EdgeCheck.Scan, "a hint that does not hold is searched at once even if a wait was recorded for it (it was not an orphan)");
            Program.Check(EdgeHint.Decide(false, true, 10f, 70f) == EdgeCheck.Wait, "an orphan that found no road waits");
            Program.Check(EdgeHint.Decide(false, true, 70f, 70f) == EdgeCheck.Scan && EdgeHint.Decide(false, true, 71f, 70f) == EdgeCheck.Scan, "and looks again when the time is up");
            Program.Check(EdgeHint.Decide(false, true, 5f, 0f) == EdgeCheck.Scan, "an orphan nobody has measured yet (after loading) is searched once");
            Program.Check(EdgeHint.OrphanRetrySeconds >= 30f && EdgeHint.TableSeconds >= 5f && EdgeHint.Beside == 12f, "the intervals: a minute for an orphan, 20 s for the table, 12 m of reach");
        }

        private static void SameAsBruteForce()
        {
            var rng = new Random(20261019);
            var curves = new List<Bezier3>();
            var boxes = new EdgeBoxes();
            // Straight roads along the axes have boxes with no width: the reach has to be added to the box, or an item beside such a road is not found.
            for (int i = 0; i < 600; i++)
            {
                float x = (float)(rng.NextDouble() * 4000 - 2000), z = (float)(rng.NextDouble() * 4000 - 2000), length = (float)(rng.NextDouble() * 250 + 20);
                int kind = i % 8;
                Bezier3 c;
                switch (kind)
                {
                    case 0: c = Straight(x, z, x + length, z); break;
                    case 1: c = Straight(x, z, x, z + length); break;
                    case 2: c = Straight(x, z, x + length * 0.7f, z + length * 0.7f); break;
                    case 3: c = new Bezier3(new Vec3(x, 0f, z), new Vec3(x + length * 0.5f, 0f, z + length * 0.9f), new Vec3(x + length, 0f, z + length * 0.9f), new Vec3(x + length * 1.2f, 0f, z)); break;
                    // Arches whose middle bulges beyond both ends, to each of the four sides: the box has to hold the control points, not only the two ends.
                    case 4: c = new Bezier3(new Vec3(x, 0f, z), new Vec3(x + length * 0.8f, 0f, z + length * 0.3f), new Vec3(x + length * 0.8f, 0f, z + length * 0.7f), new Vec3(x, 0f, z + length)); break;
                    case 5: c = new Bezier3(new Vec3(x, 0f, z), new Vec3(x - length * 0.8f, 0f, z + length * 0.3f), new Vec3(x - length * 0.8f, 0f, z + length * 0.7f), new Vec3(x, 0f, z + length)); break;
                    case 6: c = new Bezier3(new Vec3(x, 0f, z), new Vec3(x + length * 0.3f, 0f, z + length * 0.8f), new Vec3(x + length * 0.7f, 0f, z + length * 0.8f), new Vec3(x + length, 0f, z)); break;
                    default: c = new Bezier3(new Vec3(x, 0f, z), new Vec3(x + length * 0.3f, 0f, z - length * 0.8f), new Vec3(x + length * 0.7f, 0f, z - length * 0.8f), new Vec3(x + length, 0f, z)); break;
                }

                curves.Add(c);
                Add(boxes, c);
            }

            Program.Check(boxes.Count == 600, "the boxes know the edges");
            var scratch = new List<int>();
            int found = 0, missed = 0, near = 0;
            for (int n = 0; n < 4000; n++)
            {
                // Items scattered over the map, and a third of them put next to a road (up to 15 m from its middle) so that both sides of the reach are tried.
                float x, z;
                if (n % 3 == 0)
                {
                    Bezier3 c = curves[rng.Next(curves.Count)];
                    Vec3 p = c.Point((float)rng.NextDouble());
                    double angle = rng.NextDouble() * Math.PI * 2, d = rng.NextDouble() * 15;
                    x = p.X + (float)(Math.Cos(angle) * d);
                    z = p.Z + (float)(Math.Sin(angle) * d);
                    near++;
                }
                else
                {
                    x = (float)(rng.NextDouble() * 4400 - 2200);
                    z = (float)(rng.NextDouble() * 4400 - 2200);
                }

                int index = EdgeHint.Nearest(boxes, i => curves[i], x, z, EdgeHint.Beside, scratch, out float t, out float distance);

                // The brute force: every edge, the same curve search.
                int best = -1;
                float bestDistance = EdgeHint.Beside, bestT = 0f;
                var at = new Vec3(x, 0f, z);
                for (int e = 0; e < curves.Count; e++)
                {
                    float ct = curves[e].ClosestT(at, 8);
                    float d = Vec3.DistanceXZ(curves[e].Point(ct), at);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = e;
                        bestT = ct;
                    }
                }

                if (best >= 0)
                    found++;
                if (index != best)
                {
                    missed++;
                    if (missed <= 3)
                        Program.Check(false, $"item {n} at {x:0.0}, {z:0.0}: the boxes found edge {index}, measuring every edge found {best} ({bestDistance:0.00} m)");
                    continue;
                }

                if (best >= 0)
                {
                    Program.Near(distance, bestDistance, 1e-4f, $"item {n}: the same distance");
                    Program.Near(t, bestT, 1e-5f, $"item {n}: the same place along the edge");
                }
            }

            Program.Check(missed == 0, missed + " item(s) found another road than the brute force");
            Program.Check(found > 600 && near > 1000, "the test has items beside roads and items with none: " + found + " found, " + near + " put next to a road");
        }

        private static void Cost()
        {
            // A map's worth of edges (the game's has about 20 000): one search must be a small fraction of a millisecond, a table of them cheap to make. The old way measured every edge for every item.
            var rng = new Random(7);
            var curves = new List<Bezier3>();
            var boxes = new EdgeBoxes();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 25000; i++)
            {
                float x = (float)(rng.NextDouble() * 14000 - 7000), z = (float)(rng.NextDouble() * 14000 - 7000), length = (float)(rng.NextDouble() * 200 + 20);
                Bezier3 c = Straight(x, z, x + length * (rng.Next(2) == 0 ? 1 : 0.6f), z + length * (rng.Next(2) == 0 ? 0 : 0.8f));
                curves.Add(c);
                Add(boxes, c);
            }

            double buildMs = clock.Elapsed.TotalMilliseconds;
            var scratch = new List<int>();
            Func<int, Bezier3> curveOf = i => curves[i];
            EdgeHint.Nearest(boxes, curveOf, 0f, 0f, 12f, scratch, out _, out _);
            clock.Restart();
            const int searches = 2000;
            for (int n = 0; n < searches; n++)
                EdgeHint.Nearest(boxes, curveOf, (float)(rng.NextDouble() * 14000 - 7000), (float)(rng.NextDouble() * 14000 - 7000), 12f, scratch, out _, out _);
            double perSearch = clock.Elapsed.TotalMilliseconds / searches;

            // For the record: what measuring every edge costs for one item (the old way).
            clock.Restart();
            var at = new Vec3(100f, 0f, 100f);
            float sink = 0f;
            for (int e = 0; e < curves.Count; e++)
                sink += curves[e].ClosestT(at, 8);
            double brute = clock.Elapsed.TotalMilliseconds;
            Console.WriteLine($"  edge hint cost: 25000 edges, boxes made in {buildMs:0.0} ms, {perSearch * 1000:0.0} us per search by boxes, {brute:0.0} ms per search by measuring every edge (sink {sink:0})");

            // The boxes must throw almost every edge away: a box test that lets a fifth of the map through (one axis only) is as correct and as slow.
            long candidates = 0;
            for (int n = 0; n < 500; n++)
            {
                scratch.Clear();
                boxes.Candidates((float)(rng.NextDouble() * 14000 - 7000), (float)(rng.NextDouble() * 14000 - 7000), 12f, scratch);
                candidates += scratch.Count;
            }

            double meanCandidates = candidates / 500.0;
            Console.WriteLine($"  edge hint cost: {meanCandidates:0.00} candidate edge(s) of 25000 on average");
            Program.Check(meanCandidates < 25.0, "a point has only a few candidate edges, not a share of the map: " + meanCandidates.ToString("0.0"));
            Program.Check(perSearch < 0.5, "a search by boxes takes under half a millisecond over 25000 edges: " + perSearch.ToString("0.000") + " ms");
            Program.Check(brute > perSearch * 20, "the brute force is at least 20 times slower (so the test would see the old way): " + brute.ToString("0.0") + " ms against " + perSearch.ToString("0.000") + " ms");
        }

        private static void NoRepeatedScans()
        {
            // A model of the system's rounds: 300 items, 90 of them with no road in reach. Over ten minutes of rounds every 15 frames at 60 fps (0.25 s) the number of full searches must be what the
            // retry interval allows (each orphan once a minute), not one for every orphan every round. The model uses the same decision function the system does.
            const int items = 300, orphans = 90, perRound = 12;
            float now = 0f;
            var notBefore = new float[items];
            int cursor = 0, scans = 0, waits = 0;
            for (int round = 0; round < 2400; round++)
            {
                now += 0.25f;
                for (int n = 0; n < perRound; n++)
                {
                    int i = (cursor + n) % items;
                    bool beside = i >= orphans;
                    EdgeCheck check = EdgeHint.Decide(beside, i < orphans, now, notBefore[i]);
                    if (check == EdgeCheck.Scan)
                    {
                        scans++;
                        notBefore[i] = now + EdgeHint.OrphanRetrySeconds;
                    }
                    else if (check == EdgeCheck.Wait)
                    {
                        waits++;
                    }
                }

                cursor = (cursor + perRound) % items;
            }

            int minutes = 10;
            int allowed = orphans * (minutes + 1) + 5;
            Program.Check(scans <= allowed, $"the orphans are searched about once a minute each: {scans} search(es) in ten minutes, at most {allowed}");
            Program.Check(scans >= orphans * (minutes - 1), $"and they are searched again, not forgotten: {scans}");
            Program.Check(waits > scans * 5, $"most visits of an orphan only wait: {waits} waits against {scans} searches");

            // What the old way did: the same rounds without the wait.
            int oldScans = 0;
            cursor = 0;
            for (int round = 0; round < 2400; round++)
            {
                for (int n = 0; n < perRound; n++)
                {
                    int i = (cursor + n) % items;
                    if (EdgeHint.Decide(i >= orphans, i < orphans, 0f, 0f) == EdgeCheck.Scan)
                        oldScans++;
                }

                cursor = (cursor + perRound) % items;
            }

            Program.Check(oldScans > scans * 8, $"the old way (no wait) searched {oldScans} times in the same rounds, ten times more: the model sees the difference");
        }
    }
}
