using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What the tool does every frame while it is open, the parts that are plain code, timed on Mono (what the game adds, the entity reads, is not here: the tool's own stage timers in the game's log
    /// are for that). Each line is the average of many runs of one thing the tool does in a frame while the cursor is over a road.
    /// </summary>
    internal static class Frame
    {
        private static double Time(int runs, Action action)
        {
            for (int i = 0; i < Math.Min(50, runs / 10 + 1); i++)
                action();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < runs; i++)
                action();
            return clock.Elapsed.TotalMilliseconds * 1000.0 / runs;
        }

        private static void Line(string what, double us) => Console.WriteLine($"  {what,-70} {us,9:0.0} us");

        public static void Run()
        {
            Console.WriteLine("per-frame work of the tool (microseconds a frame, on this runtime):");
            var curve = new Bezier3(new Vec3(0, 0, 0), new Vec3(60, 0, 30), new Vec3(140, 0, 30), new Vec3(200, 0, 0));
            var cursor = new Vec3(90f, 0f, 40f);

            Line("ArcTable (64 steps)", Time(20000, () => new ArcTable(curve)));
            var arc = new ArcTable(curve);
            Line("EdgeSnap.Solve (ClosestT + the rest)", Time(20000, () => EdgeSnap.Solve(arc, 12f, cursor, EdgeSnapOptions.Default)));
            Line("  of which Bezier3.ClosestT (32 samples + 24 refinements)", Time(20000, () => curve.ClosestT(cursor)));
            Line("ArcTable + Solve, as the tool does it three times a frame", Time(20000, () => { for (int i = 0; i < 3; i++) EdgeSnap.Solve(new ArcTable(curve), 12f, cursor, EdgeSnapOptions.Default); }));

            SignDef prohibit = SignCatalog.ById("gb.prohib.09"), exit = SignCatalog.ById("guide.exit.advance"), junction = SignCatalog.ById("gb.guide8.01");
            string draft = "";
            Line("GuideTemplates.ResolveSpeed on an auto board", Time(20000, () => GuideTemplates.ResolveSpeed(exit, draft, 60f, true, false)));
            string spec = GuideTemplates.ResolveSpeed(exit, draft, 60f, true, false);
            Line("SignInstances.Of on a cached guide board (parse, write, look up)", Time(20000, () => SignInstances.Of(exit, spec)));
            Line("SignInstances.Of on a cached classic board", Time(20000, () => SignInstances.Of(prohibit, null)));
            string jspec = GuideTemplates.ResolveSpeed(junction, draft, 60f, true, false);
            var found = new JrResult { Found = true, Distance = 80f, ApproachEdge = 1, OwnName = "中山路" };
            found.Arms.Add(new JrArm { Edge = 2, Name = "长江路", Road = 2, Angle = 90f, Lanes = 2, CanLeave = true, Direction = JrDirection.Left });
            found.Arms.Add(new JrArm { Edge = 3, Name = "长江路", Road = 2, Angle = -90f, Lanes = 2, CanLeave = true, Direction = JrDirection.Right });
            found.Arms.Add(new JrArm { Edge = 4, Name = "中山路", Road = 1, Angle = 0f, Lanes = 3, CanLeave = true, Direction = JrDirection.Ahead });
            Line("JunctionFill.Apply with a cached reading (a junction sign)", Time(5000, () => JunctionFill.Apply(junction, jspec, found, out _)));

            SupportDef support = SupportCatalog.ById(SupportCatalog.SinglePole);
            SignInstance instance = SignInstances.Of(exit, spec);
            Line("SupportLayouts.Of", Time(20000, () => SupportLayouts.Of(instance.Def, support)));
            Line("SupportPlanner.AutoFor", Time(20000, () => SupportPlanner.AutoFor(instance.Def, new SupportContext { LanesSameDirection = 2 })));

            // The lane report the tool builds each frame with strings.
            var laterals = new List<float>();
            var forwards = new List<bool>();
            for (int i = 0; i < 12; i++)
            {
                laterals.Add(-4.5f + i * 0.8f);
                forwards.Add(i % 2 == 0);
            }

            Line("LaneMath.CountDistinct + the report's strings and sort (12 lanes)", Time(5000, () =>
            {
                LaneMath.CountDistinct(laterals, forwards, out List<float> f, out List<float> b);
                var places = new List<string>();
                foreach (float x in b) places.Add(x.ToString("0.0") + "B");
                foreach (float x in f) places.Add(x.ToString("0.0") + "F");
                places.Sort((p, q) => float.Parse(p.Substring(0, p.Length - 1), System.Globalization.CultureInfo.InvariantCulture).CompareTo(float.Parse(q.Substring(0, q.Length - 1), System.Globalization.CultureInfo.InvariantCulture)));
                string.Join(" ", places);
            }));
            Line("LaneMath.CountDistinct alone (12 lanes)", Time(20000, () => LaneMath.CountDistinct(laterals, forwards, out _, out _)));

            // The overlay's two lines of the road's edges.
            Line("Marking.Offset x 2 (the two road edges, 60 m, as DrawOverlay does)", Time(5000, () =>
            {
                float s = arc.SAt(arc.TAt(100f));
                foreach (float lateral in new[] { -6f, 6f })
                    Marking.Offset(arc, s - 30f, s + 30f, lateral, 3f);
            }));

            // The hint's text and the strings of the note.
            var hint = new HintInfo { Kind = "attach", Count = 3, Left = 1.2f, X = 12.5f, Name = "示1", Hung = true, Span = 14f };
            Line("HintText.Of (a hint)", Time(20000, () => HintText.Of(hint, true)));
            Line("the note string of a placement (about what the tool writes)", Time(20000, () => string.Format("road {0}:{1} width {2:0.0} m, limit {3:0} km/h, t {4:0.000}, side {5}, {6} lane(s) this way ({7}+{8}), post {9:+0.0;-0.0} m from the edge (board reaches {10:0.0} m toward the road)", 1, 2, 12.0, 60.0, 0.5, "right", 3, 3, 3, 0.4, 0.5)));

            // Pointing at a sign: a city of 1000 signs round the camera, the ray over it.
            var rng = new Random(5);
            var store = new ItemStore<int>();
            var defs = new List<SignDef>(SignCatalog.All);
            for (int i = 0; i < 1000; i++)
                store.Upsert(i, new ItemRecord(defs[rng.Next(defs.Count)].Hash, new Vec3((float)(rng.NextDouble() * 1500 - 750), 0f, (float)(rng.NextDouble() * 1500 - 750)), 0f));
            var from = new Vec3(0f, 40f, 0f);
            var to = new Vec3(300f, 0f, 200f);
            var pairs = new List<KeyValuePair<int, ItemRecord>>();
            Line("FindUnderCursor: store.ItemsNearSegment, Split (1000 signs, 70 m)", Time(5000, () =>
            {
                pairs.Clear();
                store.ItemsNearSegment(from, to, 70f, pairs);
                var singles = new List<KeyValuePair<int, ItemRecord>>();
                var groups = new Dictionary<uint, List<KeyValuePair<int, ItemRecord>>>();
                ItemGroups.Split(pairs, singles, groups);
            }));
            int candidates = 0;
            pairs.Clear();
            store.ItemsNearSegment(from, to, 70f, pairs);
            foreach (KeyValuePair<int, ItemRecord> p in pairs)
                candidates += Picking.DistanceToSegment(p.Value.Position, from, to) <= 70f ? 1 : 0;
            Console.WriteLine($"    (the tiles near the ray hold {pairs.Count} signs, {candidates} of them within 70 m of the ray)");
            Line("  and for each sign within 70 m: PartsOfRecord (cached) + Picking.Pick", Time(2000, () =>
            {
                foreach (KeyValuePair<int, ItemRecord> p in pairs)
                {
                    if (Picking.DistanceToSegment(p.Value.Position, from, to) > 70f)
                        continue;
                    SignParts parts = ChunkBuilder.PartsOfRecord(p.Value);
                    if (parts != null)
                        Picking.Pick(parts.Picks, p.Value.Position, p.Value.Yaw, from, to, out _);
                }
            }));
            Line("Spacing.NearestSame over the signs of the tiles round the cursor", Time(20000, () =>
            {
                var near = new List<KeyValuePair<int, ItemRecord>>();
                store.ItemsNear(10f, 10f, 1.6f, near);
                var records = new List<ItemRecord>();
                foreach (KeyValuePair<int, ItemRecord> p in near) records.Add(p.Value);
                Spacing.NearestSame(records, new Vec3(10f, 0f, 10f), 7u);
            }));
        }
    }
}
