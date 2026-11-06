using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What ItemRoadSystem's round asks of the catalogue for each item it looks at (BENCH_ITEMROUND=1): the owner's 0.12.75 log has the round at 1.26 ms on average against 0.14 ms in 0.12.73, a round
    /// looking at 12 items, and since 0.12.74 each item that is beside its road is asked what kind of host it has (HostKindOf: SignCatalog.ByHash, SupportCatalog.For(...).Kind). Time per call over every
    /// sign of the catalogue (the slowest ten are named), for the hash of a sign, the hash of an unknown sign, and the planner's own choice (support hash 0). .NET Framework, not the game's Mono.
    /// </summary>
    internal static class ItemRound
    {
        private static int s_Sink;

        public static void Run()
        {
            IReadOnlyList<SignDef> all = SignCatalog.All;
            Console.WriteLine($"item round bench: {all.Count} signs in the catalogue");
            var ns = new List<KeyValuePair<double, string>>();
            double total = 0.0;
            foreach (SignDef def in all)
            {
                double t = Time(() => s_Sink += SignCatalog.ByHash(def.Hash) != null ? 1 : 0, 2000);
                ns.Add(new KeyValuePair<double, string>(t, def.Id));
                total += t;
            }

            ns.Sort((a, b) => b.Key.CompareTo(a.Key));
            Console.WriteLine($"SignCatalog.ByHash(hash of a sign): mean {total / all.Count:0} ns over the catalogue; the slowest: " + string.Join(", ", ns.GetRange(0, 5).ConvertAll(p => $"{p.Value} {p.Key:0} ns")));
            Console.WriteLine($"SignCatalog.ByHash(unknown hash): {Time(() => s_Sink += SignCatalog.ByHash(0xDEADBEEFu) != null ? 1 : 0, 200):0} ns");

            var support = new List<KeyValuePair<double, string>>();
            double supportTotal = 0.0;
            foreach (SignDef def in all)
            {
                double t = Time(() => s_Sink += (int)SupportCatalog.For(def, 0u).Kind, 2000);
                support.Add(new KeyValuePair<double, string>(t, def.Id));
                supportTotal += t;
            }

            support.Sort((a, b) => b.Key.CompareTo(a.Key));
            Console.WriteLine($"SupportCatalog.For(sign, 0).Kind (the planner's choice): mean {supportTotal / all.Count:0} ns; the slowest: " + string.Join(", ", support.GetRange(0, 5).ConvertAll(p => $"{p.Value} {p.Key:0} ns")));
            uint pole = SupportCatalog.All[0].Hash;
            double withHash = 0.0;
            foreach (SignDef def in all)
                withHash += Time(() => s_Sink += (int)SupportCatalog.For(def, pole).Kind, 500);
            Console.WriteLine($"SupportCatalog.For(sign, a stored hash).Kind: mean {withHash / all.Count:0} ns");

            double both = 0.0, worstBoth = 0.0;
            string worstId = "";
            foreach (SignDef def in all)
            {
                uint hash = def.Hash;
                double t = Time(() =>
                {
                    SignDef d = SignCatalog.ByHash(hash);
                    SupportKind kind = d == null ? SupportKind.SinglePole : SupportCatalog.For(d, 0u).Kind;
                    s_Sink += SupportKinds.OnWall(kind) ? 1 : SupportKinds.Hosted(kind) ? 2 : 0;
                }, 2000);
                both += t;
                if (t > worstBoth)
                {
                    worstBoth = t;
                    worstId = def.Id;
                }
            }

            Console.WriteLine($"HostKindOf's work for one item: mean {both / all.Count:0} ns, the slowest sign {worstId} {worstBoth:0} ns; a round of 12 items at the mean: {12 * both / all.Count / 1e6:0.000} ms, at the slowest: {12 * worstBoth / 1e6:0.000} ms");
            ItemLink.Run();
        }

        private static double Time(Action body, int calls)
        {
            for (int i = 0; i < Math.Min(calls, 200); i++)
                body();
            double best = double.MaxValue;
            for (int run = 0; run < 3; run++)
            {
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < calls; i++)
                    body();
                best = Math.Min(best, (Stopwatch.GetTimestamp() - t0) * 1e9 / Stopwatch.Frequency / calls);
            }

            return best;
        }
    }
}

namespace CDLC.TS.Bench
{
    /// <summary>The once-per-item link work of the road follow (ItemRoadSystem.NoteLinked: the relation of an item to its road is measured the first time the item is found beside it).</summary>
    internal static class ItemLink
    {
        private static int s_Sink;

        public static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("link work for one item (what NoteLinked does the first time: HostKindOf, PathOf, GroupOf, then LinkLine or LinkSingle)");
            Bezier3 road = FollowPerf.Edge(3);
            var cache = new FollowCache();
            var line = new List<Vec3>();
            FollowCurve table = cache.Get(road);
            for (int i = 0; i <= 32; i++)
            {
                float t = (0.02f + 0.9f * i / 32f);
                line.Add(road.Point(t) + road.Tangent(t).RightXZ() * 6f);
            }

            string path = RunPath.Encode(line, out float yaw);
            SignDef def = SignCatalog.ById("bar.noise.green");
            string spec = def.Template.Parse("height=300;bed=road;pn=green;where=r;off=0;jn=side;nd=j;g=0;ph=0;tl=6400").With(RunPath.Key, path).ToString();
            GuideSpec parsed = def.Template.Parse(spec);
            Console.WriteLine($"  path of {path.Length} characters, spec of {spec.Length} characters, the sign {def.Id}");
            var watch = new FollowWatch<int, int>();
            int id = 0;
            Vec3 at = line[0];

            Row("SignCatalog.ByHash (one call)", () => s_Sink += SignCatalog.ByHash(def.Hash) != null ? 1 : 0);
            Row("HostKindOf's work (ByHash + SupportCatalog.For + kind)", () =>
            {
                SignDef d = SignCatalog.ByHash(def.Hash);
                SupportKind kind = d == null ? SupportKind.SinglePole : SupportCatalog.For(d, 0u).Kind;
                s_Sink += SupportKinds.OnWall(kind) ? 1 : 0;
            });
            Row("PathOf (ByHash, IsRun, Parse the spec, Get the path)", () =>
            {
                SignDef d = SignCatalog.ByHash(def.Hash);
                s_Sink += d != null && d.IsMarking && MarkingTemplates.IsRun(d.Template) ? d.Template.Parse(spec).Get(RunPath.Key).Length : 0;
            });
            Row("GroupOf for a line (ByHash, Parse, With x2, ToString, hash)", () =>
            {
                SignDef d = SignCatalog.ByHash(def.Hash);
                GuideSpec s = d.Template.Parse(spec);
                string family = s.With(RunPath.Key, "").With(MarkingTemplates.PhaseKey, "").ToString();
                int h = 0;
                foreach (char c in family)
                    h = h * 31 + c;
                s_Sink += h;
            });
            Row("RunPath.Decode + MarkingPolygon.ToWorld (32 chords)", () => s_Sink += MarkingPolygon.ToWorld(RunPath.Decode(path), at, yaw).Count);
            Row("FollowWatch.LinkLine, one road (32 chords measured)", () => s_Sink += watch.LinkLine(++id & 1023, 1, new[] { 1 }, new[] { table }, path, at, yaw, 0) ? 1 : 0);
            FollowCurve second = cache.Get(FollowPerf.Edge(4));
            Row("FollowWatch.LinkLine, three roads", () => s_Sink += watch.LinkLine(++id & 1023, 1, new[] { 1, 2, 3 }, new[] { table, second, second }, path, at, yaw, 0) ? 1 : 0);
            Row("FollowWatch.LinkSingle (one item, one road)", () => s_Sink += watch.LinkSingle(++id & 1023, 1, table, at, yaw, 0) ? 1 : 0);
            Row("FollowCache.Get(curve) (the table of an edge, kept)", () => s_Sink += cache.Get(road) != null ? 1 : 0);
        }

        private static void Row(string name, Action body)
        {
            FollowPerf.Result r = FollowPerf.Measure(body);
            Console.WriteLine($"  {name,-62} {r.BestNs,10:0} ns {r.Bytes,9:0} B");
        }
    }
}
