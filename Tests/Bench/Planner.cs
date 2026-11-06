using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;
using CDLC.TS.Tests;

namespace CDLC.TS.Bench
{
    /// <summary>
    /// What the planners cost (BENCH_PLANNER=1; DESIGN-text-lightbox.md 11, DESIGN-support-styles.md 9): the support planner over the whole catalogue (every sign, 8 road contexts, a stack or not, every style
    /// profile), the catalogue's lookups, and the text light box's pieces (layout, a glyph cold, the cache warm, the rear frame) at 4, 12 and 36 characters and three letter heights. Time per call (the fastest
    /// of five runs: the least disturbed), bytes allocated per call (GC.GetAllocatedBytesForCurrentThread), the collections a million calls would make, and the worst single call (a timer's own cost
    /// is in it). The runtime is whatever runs it: the .NET Framework here, not the game's Mono, where allocations cost more than the figures of time say. No Unity. Then the numbers the constants are set
    /// from: what a glyph weighs (GlyphCache.DefaultBudgetBytes), how long the rods of the frames of random texts are (FrameStyle.LongRodShare), and the two caches that were weighed and not shipped.
    /// </summary>
    internal static class Planner
    {
        private const string Chinese = "永远发光字体灯箱路牌停公交铁路出租车校快速多乘员人慢学口小回国日目田三十中华人民共和国交通标志专用字体";

        private static readonly SupportContext[] Contexts =
        {
            new SupportContext { LanesSameDirection = 0 }, new SupportContext { LanesSameDirection = 1 }, new SupportContext { LanesSameDirection = 2 }, new SupportContext { LanesSameDirection = 3 },
            new SupportContext { LanesSameDirection = 0, ExitLeftOrWide = true }, new SupportContext { LanesSameDirection = 1, ExitLeftOrWide = true },
            new SupportContext { LanesSameDirection = 2, ExitLeftOrWide = true }, new SupportContext { LanesSameDirection = 3, ExitLeftOrWide = true }
        };

        private static int s_Sink;

        public static void Run()
        {
            Console.WriteLine("planner bench: time per call (fastest of 5 runs), bytes per call, collections (gen0/1/2) per million calls, the worst single call (timer cost included)");
            Console.WriteLine("name                                          calls      ns/call   bytes/call   gc/Mcalls   worst ns");
            Support();
            Catalogue();
            FontOutlines font = FindFont();
            if (font != null)
            {
                TextPieces(font);
                WholePlan(font);
                GlyphWeights(font);
                RodLengths(font);
                Caches(font);
            }

            Console.WriteLine("(sink " + s_Sink + ")");
        }

        private sealed class Result
        {
            public double Ns, Bytes, Gen0, Gen1, Gen2, Worst;
        }

        /// <summary>body(i) is call number i of calls. One warm-up pass, five measured, then a pass that times every call.</summary>
        private static Result Measure(string name, int calls, Action<int> body, bool worst = true)
        {
            for (int i = 0; i < calls; i++)
                body(i);
            double best = double.MaxValue;
            long bytes = 0;
            int g0 = 0, g1 = 0, g2 = 0;
            long total = 0;
            for (int run = 0; run < 5; run++)
            {
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                int c0 = GC.CollectionCount(0), c1 = GC.CollectionCount(1), c2 = GC.CollectionCount(2);
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < calls; i++)
                    body(i);
                long t1 = Stopwatch.GetTimestamp();
                bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                g0 += GC.CollectionCount(0) - c0;
                g1 += GC.CollectionCount(1) - c1;
                g2 += GC.CollectionCount(2) - c2;
                total += calls;
                best = Math.Min(best, (t1 - t0) * 1e9 / Stopwatch.Frequency / calls);
            }

            double slowest = 0.0;
            if (worst)
            {
                for (int i = 0; i < calls; i++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    body(i);
                    slowest = Math.Max(slowest, (Stopwatch.GetTimestamp() - t0) * 1e9 / Stopwatch.Frequency);
                }
            }

            var r = new Result { Ns = best, Bytes = (double)bytes / calls, Gen0 = g0 * 1e6 / total, Gen1 = g1 * 1e6 / total, Gen2 = g2 * 1e6 / total, Worst = slowest };
            Console.WriteLine($"{name,-44} {calls,8} {r.Ns,12:0.0} {r.Bytes,12:0.0} {r.Gen0,6:0.0}/{r.Gen1:0.0}/{r.Gen2:0.0} {r.Worst,10:0}");
            return r;
        }

        // ------------------------------------------------------------------ the support planner

        private static void Support()
        {
            List<SignDef> signs = new List<SignDef>(SignCatalog.All);
            int contexts = Contexts.Length;
            Console.WriteLine($"-- the support planner: {signs.Count} signs x {contexts} contexts, a stack or not");
            Measure("Auto(sign)", signs.Count, i => s_Sink += SupportPlanner.Auto(signs[i]).Kind == SupportKind.SinglePole ? 1 : 0);
            Measure("Auto(sign, stack)", signs.Count, i => s_Sink += SupportPlanner.Auto(signs[i], true).Kind == SupportKind.SinglePole ? 1 : 0);
            Measure("AutoFor(sign, context)", signs.Count * contexts, i => s_Sink += SupportPlanner.AutoFor(signs[i / contexts], Contexts[i % contexts]).Kind == SupportKind.SinglePole ? 1 : 0);
            foreach (StyleProfile profile in StyleProfiles.All)
            {
                StyleProfile p = profile;
                Measure("AutoStyled(" + p.Id + ")", signs.Count * contexts * 2, i => s_Sink += SupportPlanner.AutoStyled(signs[i / (contexts * 2)], Contexts[(i / 2) % contexts], p, (i & 1) == 1).Note.Length);
            }

            // Resolve alone, from a plain support already chosen.
            var plains = new SupportDef[signs.Count];
            var street = new bool[signs.Count];
            for (int i = 0; i < signs.Count; i++)
            {
                plains[i] = SupportPlanner.Auto(signs[i]);
                street[i] = PlateHead.FitsDef(signs[i]);
            }

            StyleProfile shanghai = StyleProfiles.Shanghai;
            Measure("Resolve(shanghai)", signs.Count, i => s_Sink += SupportPlanner.Resolve(plains[i], street[i], signs[i], shanghai).Note.Length);
            Measure("PlateHead.FitsDef(sign)", signs.Count, i => s_Sink += PlateHead.FitsDef(signs[i]) ? 1 : 0);
        }

        // ------------------------------------------------------------------ the catalogue's lookups

        private static void Catalogue()
        {
            IReadOnlyList<SupportDef> all = SupportCatalog.All;
            Console.WriteLine($"-- the catalogue: {all.Count} supports");
            string first = all[0].Id, middle = all[all.Count / 2].Id, last = all[all.Count - 1].Id;
            uint hashFirst = all[0].Hash, hashLast = all[all.Count - 1].Hash;
            Measure("ById(first)", 100000, i => s_Sink += SupportCatalog.ById(first) != null ? 1 : 0, false);
            Measure("ById(middle)", 100000, i => s_Sink += SupportCatalog.ById(middle) != null ? 1 : 0, false);
            Measure("ById(last)", 100000, i => s_Sink += SupportCatalog.ById(last) != null ? 1 : 0, false);
            Measure("ById(unknown)", 100000, i => s_Sink += SupportCatalog.ById("no.such.support") != null ? 1 : 0, false);
            Measure("ByHash(first)", 100000, i => s_Sink += SupportCatalog.ByHash(hashFirst) != null ? 1 : 0, false);
            Measure("ByHash(last)", 100000, i => s_Sink += SupportCatalog.ByHash(hashLast) != null ? 1 : 0, false);
            Measure("ByHash(unknown)", 100000, i => s_Sink += SupportCatalog.ByHash(0xDEADBEEFu) != null ? 1 : 0, false);
            Measure("SupportDef.Hash (one support)", 100000, i => s_Sink += (int)(all[last.Length % all.Count].Hash & 1u), false);
            SignDef sign = SignCatalog.All[0];
            Measure("For(sign, hash of the last)", 100000, i => s_Sink += SupportCatalog.For(sign, hashLast) != null ? 1 : 0, false);
            Measure("For(sign, 0)", 100000, i => s_Sink += SupportCatalog.For(sign, 0u) != null ? 1 : 0, false);
            StyleProfile shanghai = StyleProfiles.Shanghai;
            Measure("StyleProfile.VariantsOf(form)", 100000, i => s_Sink += shanghai.VariantsOf((SupportForm)(i % 12)).Count, false);
            Measure("SupportForms.Of(plain, false)", 100000, i => s_Sink += (int)SupportForms.Of(all[i % all.Count], false), false);
        }

        // ------------------------------------------------------------------ the text light box

        private static FontOutlines FindFont()
        {
            string file = Environment.GetEnvironmentVariable("BENCH_FONT");
            if (string.IsNullOrEmpty(file))
            {
                string dir = AppContext.BaseDirectory;
                for (int i = 0; i < 10 && dir != null && file == null; i++)
                {
                    string candidate = Path.Combine(dir, "Fonts", SignFonts.BundledCjk);
                    file = File.Exists(candidate) ? candidate : null;
                    dir = Path.GetDirectoryName(dir);
                }
            }

            if (file == null || !File.Exists(file))
            {
                Console.WriteLine("no font: set BENCH_FONT or run it below the repository");
                return null;
            }

            var font = new FontOutlines(File.ReadAllBytes(file));
            return font.Ok ? font : null;
        }

        private static string TextOf(int characters)
        {
            if (characters <= 12)
                return Chinese.Substring(0, characters);
            return Chinese.Substring(0, 12) + "\n" + Chinese.Substring(12, 12) + "\n" + Chinese.Substring(24, characters - 24);
        }

        private static void TextPieces(FontOutlines font)
        {
            TextFace face = TextFace.Of(font);
            Console.WriteLine("-- the text light box's pieces (the bundled font, flatness 0.5 mm on a letter of 0.3 m: a share of the em)");
            foreach (int characters in new[] { 4, 12, 36 })
            {
                string text = TextOf(characters);
                foreach (float height in new[] { 0.3f, 1f, 3f })
                {
                    float h = height;
                    TextStyle style = TextStyle.Of(h);
                    TextLayoutResult layout = TextLayout.Layout(text, style, face);
                    int bucket = TextGlyphs.Bucket(0.0005f / 0.3f);
                    string tag = $"{characters} chars, {h:0.0} m";
                    Measure("TextLayout.Layout, " + tag, 2000, i => s_Sink += TextLayout.Layout(text, style, face).Glyphs.Count, false);
                    var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
                    var distinct = new List<int>();
                    foreach (PlacedGlyph g in layout.Glyphs)
                    {
                        if (!g.Missing && !distinct.Contains(g.Codepoint))
                            distinct.Add(g.Codepoint);
                    }

                    if (h == 0.3f)
                        Measure("TextGlyphs.Build (cold), " + tag + ", per glyph", distinct.Count, i => s_Sink += TextGlyphs.Build(font, distinct[i], bucket) != null ? 1 : 0, false);
                    Func<int, GlyphMesh> meshOf = c => cache.GetOrBuild(font, 1, c, bucket);
                    foreach (int c in distinct)
                        meshOf(c);
                    Measure("GlyphCache.GetOrBuild (warm), " + tag, layout.Glyphs.Count, i => s_Sink += meshOf(layout.Glyphs[i].Codepoint) != null ? 1 : 0, false);
                    if (h == 0.3f)
                        Measure("GlyphCache.GetOrBuild (warm, closure form), " + tag, layout.Glyphs.Count, i => s_Sink += cache.GetOrBuild(new GlyphKey(1, layout.Glyphs[i].Codepoint, bucket), () => TextGlyphs.Build(font, layout.Glyphs[i].Codepoint, bucket)) != null ? 1 : 0, false);
                    FrameStyle frame = FrameStyle.For(h);
                    Measure("LetterFrame.Plan, " + tag, characters == 36 ? 40 : 200, i => s_Sink += LetterFrame.Plan(layout, meshOf, frame).Studs.Count);
                }
            }
        }

        // ------------------------------------------------------------------ a whole text's plan, as a worker makes it

        private static void WholePlan(FontOutlines font)
        {
            Console.WriteLine("-- TextPlanner.Plan: layout, glyphs, rear frame and hints of one job (what a worker does; cold = an empty glyph cache)");
            foreach (int characters in new[] { 4, 12, 36 })
            {
                var job = new TextPlanJob(TextOf(characters), 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.0005f / 0.3f);
                var warm = new TextPlanner(id => font, new GlyphCache(GlyphCache.DefaultBudgetBytes));
                warm.Plan(job);
                Measure($"TextPlanner.Plan (warm), {characters} chars", characters == 36 ? 40 : 200, i => s_Sink += warm.Plan(job).Frame.Studs.Count);
                Measure($"TextPlanner.Plan (cold), {characters} chars", 20, i => s_Sink += new TextPlanner(id => font, new GlyphCache(GlyphCache.DefaultBudgetBytes)).Plan(job).Frame.Studs.Count);
                Measure($"new TextPlanJob, {characters} chars (the main thread's part)", 20000, i => s_Sink += (int)(new TextPlanJob(job.Text, 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f).Key & 1UL), false);
            }
        }

        // ------------------------------------------------------------------ what a glyph weighs

        /// <summary>The 3,755 characters of the first level of GB 2312 (rows 16 to 55): the characters in common use, which a road sign's text is made of nearly entirely.</summary>
        private static List<int> CommonCharacters()
        {
            Encoding gb = Encoding.GetEncoding("GB2312");
            var list = new List<int>();
            for (int row = 0xB0; row <= 0xD7; row++)
            {
                for (int col = 0xA1; col <= 0xFE; col++)
                {
                    if (row == 0xD7 && col > 0xF9)
                        break;
                    string c = gb.GetString(new[] { (byte)row, (byte)col });
                    if (c.Length == 1 && c[0] != '\uFFFD')
                        list.Add(c[0]);
                }
            }

            return list;
        }

        /// <summary>The share (%) of the values over a limit.</summary>
        private static double Over(List<double> values, double limit)
        {
            int n = 0;
            foreach (double v in values)
                n += v > limit ? 1 : 0;
            return values.Count == 0 ? 0.0 : 100.0 * n / values.Count;
        }

        private static double Percentile(List<double> sorted, double p) => sorted.Count == 0 ? 0.0 : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];

        private static void GlyphWeights(FontOutlines font)
        {
            List<int> common = CommonCharacters();
            const string latin = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            Console.WriteLine($"-- what a glyph weighs (GlyphMesh.Bytes, the cache's estimate) at 0.5 mm flatness: {common.Count} common characters (GB 2312 level 1) and {latin.Length} Latin letters and figures");
            Console.WriteLine("letter height   bucket   CJK glyphs: n  mean B  p95 B  max B   total MB   | Latin: mean B   | verts mean   tris mean   | heap MB per 1000 CJK (measured)");
            foreach (float height in new[] { 0.3f, 1f, 3f })
            {
                int bucket = TextGlyphs.Bucket(0.0005f / height);
                var bytes = new List<double>();
                double verts = 0, tris = 0, total = 0;
                GC.Collect();
                long heap0 = GC.GetTotalMemory(true);
                var kept = new List<GlyphMesh>(common.Count);
                foreach (int c in common)
                {
                    GlyphMesh mesh = TextGlyphs.Build(font, c, bucket);
                    if (mesh == null)
                        continue;
                    kept.Add(mesh);
                    bytes.Add(mesh.Bytes);
                    total += mesh.Bytes;
                    verts += mesh.Vertices;
                    tris += mesh.TriangleCount;
                }

                long heap1 = GC.GetTotalMemory(true);
                bytes.Sort();
                double latinBytes = 0;
                int latinCount = 0;
                foreach (char c in latin)
                {
                    GlyphMesh mesh = TextGlyphs.Build(font, c, bucket);
                    if (mesh == null)
                        continue;
                    latinBytes += mesh.Bytes;
                    latinCount++;
                }

                Console.WriteLine($"{height,8:0.0} m  {bucket,7} {bytes.Count,16} {total / bytes.Count,7:0} {Percentile(bytes, 0.95),6:0} {bytes[bytes.Count - 1],6:0} {total / 1048576.0,10:0.0}   | {latinBytes / Math.Max(1, latinCount),12:0}   | {verts / bytes.Count,9:0} {tris / bytes.Count,11:0}   | {(heap1 - heap0) / 1048576.0 / bytes.Count * 1000,8:0.0}");
                s_Sink += kept.Count;
            }

            Console.WriteLine($"the default budget is {GlyphCache.DefaultBudgetBytes / 1048576.0:0} MB");
        }

        // ------------------------------------------------------------------ the rods

        private static void RodLengths(FontOutlines font)
        {
            TextFace face = TextFace.Of(font);
            List<int> common = CommonCharacters();
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            Console.WriteLine("-- the rods of random texts' frames, as a share of the letter height (no cap on anything: this is where the warning's limit is read from)");
            Console.WriteLine("texts                      plans  with rods  rods held/plan (median p95 max)   rod length / letter height: median  p90   p95   p99   max   | longest rod per plan: median  p95  max   | plans warned at a limit of 0.25  0.30  0.35  (rods over it, %)");
            var random = new Random(20261102);
            var uniform = new List<string>();
            for (int t = 0; t < 600; t++)
            {
                var sb = new StringBuilder();
                int lines = 1 + random.Next(3);
                for (int l = 0; l < lines; l++)
                {
                    if (l > 0)
                        sb.Append('\n');
                    int length = 2 + random.Next(11);
                    for (int i = 0; i < length; i++)
                        sb.Append((char)common[random.Next(common.Count)]);
                }

                uniform.Add(sb.ToString());
            }

            foreach ((string name, List<string> texts) in new[] { ("the tests' pool", TextFixture.RandomTexts(600, 20261102)), ("any common character", uniform) })
            {
                foreach (float height in new[] { 0.3f, 1f, 3f })
                {
                    int bucket = TextGlyphs.Bucket(0.0005f / height);
                    var rods = new List<double>();
                    var longest = new List<double>();
                    var held = new List<double>();
                    int plans = 0, withRods = 0;
                    foreach (string text in texts)
                    {
                        TextLayoutResult layout = TextLayout.Layout(text, TextStyle.Of(height), face);
                        FramePlan plan = LetterFrame.Plan(layout, c => cache.GetOrBuild(font, 1, c, bucket), FrameStyle.For(height));
                        plans++;
                        held.Add(plan.BridgedIslands);
                        double worst = 0;
                        foreach (FrameSegment rod in plan.Bridges)
                        {
                            rods.Add(rod.Length / height);
                            worst = Math.Max(worst, rod.Length / height);
                        }

                        longest.Add(worst);
                        withRods += plan.Bridges.Count > 0 ? 1 : 0;
                    }

                    rods.Sort();
                    longest.Sort();
                    held.Sort();
                    Console.WriteLine($"{name,-20} {height,3:0.0} m {plans,6} {withRods,10} {Percentile(held, 0.5),12:0} {Percentile(held, 0.95),5:0} {held[held.Count - 1],4:0} {Percentile(rods, 0.5),22:0.000} {Percentile(rods, 0.9),6:0.000} {Percentile(rods, 0.95),5:0.000} {Percentile(rods, 0.99),5:0.000} {(rods.Count > 0 ? rods[rods.Count - 1] : 0),5:0.000}   | {Percentile(longest, 0.5),20:0.000} {Percentile(longest, 0.95),5:0.000} {longest[longest.Count - 1],5:0.000}   | {Over(longest, 0.25),19:0.0} % {Over(longest, 0.30),5:0.0} % {Over(longest, 0.35),5:0.0} %  ({Over(rods, 0.25):0.0} {Over(rods, 0.30):0.0} {Over(rods, 0.35):0.0})");
                }
            }

            Console.WriteLine($"FrameStyle.LongRodShare = {FrameStyle.LongRodShare}");
        }

        // ------------------------------------------------------------------ two caches that were weighed

        /// <summary>The support planner's result kept under a key of the sign, the road, the profile and the stack, bounded by its size: what a "PlannerCache" would be. Only for the figures; not in the mod.</summary>
        private sealed class SupportCacheProbe
        {
            private readonly Dictionary<ulong, PlannedSupport> m_Map = new Dictionary<ulong, PlannedSupport>();
            private readonly object m_Lock = new object();

            public PlannedSupport Get(SignDef sign, SupportContext context, StyleProfile profile, bool stack)
            {
                var key = new Fingerprint();
                key.Add(sign.Id.GetHashCode());
                key.Add(context.LanesSameDirection);
                key.Add(context.ExitLeftOrWide ? 1 : 0);
                key.Add(profile.Id.GetHashCode());
                key.Add(stack ? 1 : 0);
                ulong k = key.Value;
                lock (m_Lock)
                {
                    if (m_Map.TryGetValue(k, out PlannedSupport hit))
                        return hit;
                }

                PlannedSupport made = SupportPlanner.AutoStyled(sign, context, profile, stack);
                lock (m_Lock)
                {
                    if (m_Map.Count >= 4096)
                        m_Map.Clear();
                    m_Map[k] = made;
                }

                return made;
            }
        }

        /// <summary>What GlyphCache was before 0.12.75's SieveCache: a second-chance FIFO under one lock, same byte weights. Only for the comparison; not in the mod.</summary>
        private sealed class FifoGlyphs
        {
            private sealed class Entry
            {
                public GlyphMesh Mesh;
                public long Bytes;
                public bool Visited;
            }

            private readonly Dictionary<GlyphKey, Entry> m_Entries = new Dictionary<GlyphKey, Entry>();
            private readonly Queue<GlyphKey> m_Order = new Queue<GlyphKey>();
            private readonly long m_Budget;
            private readonly object m_Lock = new object();
            private long m_Bytes;

            public FifoGlyphs(long budget)
            {
                m_Budget = budget;
            }

            public int Hits { get; private set; }

            public GlyphMesh Get(GlyphKey key, Func<GlyphMesh> build)
            {
                lock (m_Lock)
                {
                    if (m_Entries.TryGetValue(key, out Entry hit))
                    {
                        hit.Visited = true;
                        Hits++;
                        return hit.Mesh;
                    }
                }

                GlyphMesh made = build();
                lock (m_Lock)
                {
                    if (m_Entries.TryGetValue(key, out Entry already))
                        return already.Mesh;
                    var entry = new Entry { Mesh = made, Bytes = made != null ? made.Bytes : 64L };
                    m_Entries[key] = entry;
                    m_Order.Enqueue(key);
                    m_Bytes += entry.Bytes;
                    while (m_Bytes > m_Budget && m_Entries.Count > 1)
                    {
                        GlyphKey oldest = m_Order.Dequeue();
                        Entry old = m_Entries[oldest];
                        if (old.Visited)
                        {
                            old.Visited = false;
                            m_Order.Enqueue(oldest);
                            continue;
                        }

                        m_Entries.Remove(oldest);
                        m_Bytes -= old.Bytes;
                    }
                }

                return made;
            }
        }

        private static void Caches(FontOutlines font)
        {
            List<SignDef> signs = new List<SignDef>(SignCatalog.All);
            int contexts = Contexts.Length;
            Console.WriteLine("-- a PlannerCache for the support planner: a keyed hit (key, lock, lookup) against making the plan again");
            var probe = new SupportCacheProbe();
            StyleProfile shanghai = StyleProfiles.Shanghai;
            int total = signs.Count * contexts * 2;
            for (int i = 0; i < total; i++)
                probe.Get(signs[i / (contexts * 2)], Contexts[(i / 2) % contexts], shanghai, (i & 1) == 1);
            Measure("cache hit, AutoStyled(shanghai)", total, i => s_Sink += probe.Get(signs[i / (contexts * 2)], Contexts[(i / 2) % contexts], shanghai, (i & 1) == 1).Note.Length, false);
            Measure("no cache, AutoStyled(shanghai)", total, i => s_Sink += SupportPlanner.AutoStyled(signs[i / (contexts * 2)], Contexts[(i / 2) % contexts], shanghai, (i & 1) == 1).Note.Length, false);

            Console.WriteLine("-- the glyph cache's policy: the second-chance FIFO it had (FifoGlyphs) against SIEVE (SieveCache, what GlyphCache is now) on 3,755 common characters, one bucket");
            List<int> common = CommonCharacters();
            int bucket = TextGlyphs.Bucket(0.0005f / 0.3f);
            var meshes = new Dictionary<int, GlyphMesh>();
            long all = 0;
            foreach (int c in common)
            {
                GlyphMesh mesh = TextGlyphs.Build(font, c, bucket);
                meshes[c] = mesh;
                all += mesh != null ? mesh.Bytes : 64L;
            }

            // A hit's cost, with everything held.
            var fifo = new FifoGlyphs(all * 2);
            var sieve = new GlyphCache(all * 2);
            foreach (int c in common)
            {
                fifo.Get(new GlyphKey(1, c, bucket), () => meshes[c]);
                sieve.GetOrBuild(new GlyphKey(1, c, bucket), () => meshes[c]);
            }

            Measure("hit, the second-chance FIFO (the old GlyphCache)", 100000, i => s_Sink += fifo.Get(new GlyphKey(1, common[i % common.Count], bucket), null) != null ? 1 : 0, false);
            Measure("hit, GlyphCache (SIEVE)", 100000, i => s_Sink += sieve.GetOrBuild(new GlyphKey(1, common[i % common.Count], bucket), null) != null ? 1 : 0, false);

            // Hits of a stream: a few characters often (a skewed draw), and now and then a character met once (a long walk through the font), the cache a share of what holds them all.
            Console.WriteLine("hits of 200,000 requests (a skewed draw over the characters, one request in 20 uniform), budget as a share of all 3,755 glyphs:");
            foreach (double share in new[] { 0.1, 0.2, 0.33, 0.5 })
            {
                var a = new FifoGlyphs((long)(all * share));
                var b = new GlyphCache((long)(all * share));
                var random = new Random(5);
                for (int n = 0; n < 200000; n++)
                {
                    int index = n % 20 == 19 ? random.Next(common.Count) : (int)(common.Count * Math.Pow(random.NextDouble(), 3.0));
                    int c = common[index];
                    var key = new GlyphKey(1, c, bucket);
                    a.Get(key, () => meshes[c]);
                    b.GetOrBuild(key, () => meshes[c]);
                }

                Console.WriteLine($"  budget {share,4:0.00}: second-chance FIFO {a.Hits / 2000.0,6:0.0} %   SIEVE {b.Hits / 2000.0,6:0.0} %   (SIEVE evicted {b.Evictions})");
            }
        }
    }
}
