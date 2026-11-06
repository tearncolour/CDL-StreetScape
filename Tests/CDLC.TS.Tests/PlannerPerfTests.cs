using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The planners as the owner asked for them on 2 Nov (DESIGN-text-lightbox.md 11, DESIGN-support-styles.md 9; "优化规划器性能，且不能占用主线程"): the optimised rear frame equals the one it replaced, to the bit,
    /// over hundreds of texts (LetterFrameReference is the old one, kept unchanged); the catalogue's hash lookup is the walk it replaced; a cache hit makes no garbage; the job has a stable key; the hints; and
    /// the planners give the same answers when many threads run them at once, which is what running them on workers asks of them.
    /// </summary>
    internal static class PlannerPerfTests
    {
        public static void RunAll()
        {
            Program.Run("planners: the rear frame is exactly the reference's over hundreds of texts, heights and styles", FrameEqualsReference);
            Program.Run("planners: strokes that share an edge exactly are one span of the frame's scan, as the reference has them", TouchingStrokes);
            Program.Run("planners: the rods are measured: the longest and the number over the limit, with no cap on anything", Rods);
            Program.Run("planners: the support catalogue's lookup by hash is what the walk through it was", ByHashIsTheWalk);
            Program.Run("planners: a glyph cache hit of a font's glyph makes no garbage, its counters add up, its budget is a constant", GlyphCacheHits);
            Program.Run("planners: a text job has a key that follows its fields", JobKey);
            Program.Run("planners: the editor's hints for a text", Warnings);
            Program.Run("planners: the sign catalogue is made once when eight threads ask first at the same time", CatalogueOnce);
            Program.Run("planners: eight threads at once give the answers one thread gives", Threads);
            Program.Run("planners: the worker makes the newest text's plan and drops what was overtaken", WorkerGlue);
        }

        private static readonly float[] Heights = { 0.3f, 0.5f, 1f, 3f };

        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        // ------------------------------------------------------------------ the frame against the reference

        /// <summary>The first thing in which two plans differ, by the bits of every number; null when they are the same.</summary>
        internal static string Differ(FramePlan a, FramePlan b)
        {
            if (a.Islands.Count != b.Islands.Count)
                return $"islands {a.Islands.Count} against {b.Islands.Count}";
            for (int i = 0; i < a.Islands.Count; i++)
            {
                FrameIsland x = a.Islands[i], y = b.Islands[i];
                if (x.Line != y.Line || x.Glyph != y.Glyph || Bits(x.MinX) != Bits(y.MinX) || Bits(x.MaxX) != Bits(y.MaxX) || Bits(x.MinY) != Bits(y.MinY) || Bits(x.MaxY) != Bits(y.MaxY) || Bits(x.Area) != Bits(y.Area) || x.Pieces.Count != y.Pieces.Count)
                    return $"island {i}'s box, area or pieces";
            }

            string list = DifferSegments("rails", a.Rails, b.Rails) ?? DifferSegments("ties", a.Ties, b.Ties) ?? DifferSegments("bridges", a.Bridges, b.Bridges);
            if (list != null)
                return list;
            if (a.Studs.Count != b.Studs.Count)
                return $"studs {a.Studs.Count} against {b.Studs.Count}";
            for (int i = 0; i < a.Studs.Count; i++)
            {
                if (Bits(a.Studs[i].At.X) != Bits(b.Studs[i].At.X) || Bits(a.Studs[i].At.Y) != Bits(b.Studs[i].At.Y) || a.Studs[i].Island != b.Studs[i].Island || a.Studs[i].Line != b.Studs[i].Line)
                    return $"stud {i}";
            }

            if (a.RailHeights.Count != b.RailHeights.Count)
                return "rail heights";
            for (int i = 0; i < a.RailHeights.Count; i++)
            {
                if (a.RailHeights[i].Line != b.RailHeights[i].Line || Bits(a.RailHeights[i].Low) != Bits(b.RailHeights[i].Low) || Bits(a.RailHeights[i].High) != Bits(b.RailHeights[i].High))
                    return $"rail heights of line {i}";
            }

            if (a.BridgedIslands != b.BridgedIslands || a.SingleStudIslands != b.SingleStudIslands || a.FloatingIslands != b.FloatingIslands)
                return "the counts of bridged, single-stud and floating islands";
            if (Bits(a.EffectiveArea) != Bits(b.EffectiveArea) || Bits(a.InkWidth) != Bits(b.InkWidth) || Bits(a.InkHeight) != Bits(b.InkHeight) || Bits(a.FillFactor) != Bits(b.FillFactor) || Bits(a.Perimeter) != Bits(b.Perimeter))
                return "the area, the ink's box, the fill or the perimeter";
            if (Bits(a.FrameLength) != Bits(b.FrameLength) || Bits(a.MassKg) != Bits(b.MassKg))
                return "the frame's length or the mass";
            return null;
        }

        private static string DifferSegments(string name, List<FrameSegment> a, List<FrameSegment> b)
        {
            if (a.Count != b.Count)
                return $"{name} {a.Count} against {b.Count}";
            for (int i = 0; i < a.Count; i++)
            {
                if (Bits(a[i].A.X) != Bits(b[i].A.X) || Bits(a[i].A.Y) != Bits(b[i].A.Y) || Bits(a[i].B.X) != Bits(b[i].B.X) || Bits(a[i].B.Y) != Bits(b[i].B.Y) || a[i].Line != b[i].Line)
                    return $"{name} {i}";
            }

            return null;
        }

        private static IEnumerable<FrameStyle> Styles(float height)
        {
            yield return FrameStyle.For(height);
            // Studs that need a wide span and few heights to choose from: more islands that no rail crosses, held by one stud or by none.
            FrameStyle coarse = FrameStyle.For(height);
            coarse.StudRadius *= 3f;
            coarse.RailSteps = 12;
            yield return coarse;
            FrameStyle wide = FrameStyle.For(height);
            wide.MinRailGap = 0.7f;
            yield return wide;
        }

        private static void FrameEqualsReference()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFace face = TextFace.Of(font);
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            List<string> texts = TextFixture.RandomTexts(300, 20261102);
            int plans = 0, bridged = 0, single = 0, floating = 0, missing = 0, multiLine = 0;
            string firstDifference = null;
            foreach (string text in texts)
            {
                foreach (float height in Heights)
                {
                    TextLayoutResult layout = TextLayout.Layout(text, TextStyle.Of(height), face);
                    int bucket = TextGlyphs.Bucket(0.0005f / 0.3f);
                    Func<int, GlyphMesh> meshOf = c => cache.GetOrBuild(font, 1, c, bucket);
                    foreach (FrameStyle style in Styles(height))
                    {
                        FramePlan now = LetterFrame.Plan(layout, meshOf, style);
                        FramePlan old = LetterFrameReference.Plan(layout, meshOf, style);
                        string differs = Differ(now, old);
                        if (differs != null && firstDifference == null)
                            firstDifference = $"\"{text.Replace('\n', '/')}\" at {height} m: {differs}";
                        plans++;
                        bridged += old.BridgedIslands > 0 ? 1 : 0;
                        single += old.SingleStudIslands > 0 ? 1 : 0;
                        floating += old.FloatingIslands > 0 ? 1 : 0;
                        missing += layout.Missing.Count > 0 ? 1 : 0;
                        multiLine += layout.Lines > 1 ? 1 : 0;
                    }
                }
            }

            Console.WriteLine($"  planners: {plans} frame plans of {texts.Count} texts, {bridged} with rods, {single} with a single-stud island, {floating} with a floating one, {missing} with a missing character, {multiLine} of several lines");
            Program.Check(firstDifference == null, "every plan equals the reference's to the bit" + (firstDifference == null ? "" : ": " + firstDifference));
            Program.Check(plans == texts.Count * Heights.Length * 3, "every text, height and style was planned: " + plans);
            Program.Check(bridged > 20 && single > 20 && floating > 5, $"the branches were met: bridged {bridged}, single-stud {single}, floating {floating} plans");
            Program.Check(missing > 10 && multiLine > 100, $"characters the font lacks ({missing} plans) and several lines ({multiLine}) were met");
        }

        private static List<Point2> Rectangle(float x0, float x1) => new List<Point2> { new Point2(x0, 0f), new Point2(x1, 0f), new Point2(x1, 1f), new Point2(x0, 1f) };

        /// <summary>The fonts' strokes seldom meet at an edge to the last bit, so the random texts do not show the join of two spans that do: each of these is narrower than a stud's span, the two together are not.</summary>
        private static void TouchingStrokes()
        {
            const float w = 1f / 64f;
            GlyphMesh mesh = TextGlyphs.FromLoops(new List<List<Point2>> { Rectangle(0f, w), Rectangle(w, 2f * w) }, 1f);
            var layout = new TextLayoutResult { MetresPerEm = 1f, LetterHeight = 1f };
            layout.Glyphs.Add(new PlacedGlyph { Codepoint = 'x' });
            layout.LineWidths.Add(1f);
            FrameStyle style = FrameStyle.For(1f);
            Func<int, GlyphMesh> meshOf = c => mesh;
            FramePlan now = LetterFrame.Plan(layout, meshOf, style);
            Program.Check(Differ(now, LetterFrameReference.Plan(layout, meshOf, style)) == null, "the plan equals the reference's");
            Program.Check(now.Islands.Count == 1 && now.FloatingIslands == 0 && now.Studs.Count > 0, "the two strokes are one island with a stud, not two spans too narrow for one");
        }

        // ------------------------------------------------------------------ the rods

        private static void Rods()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFace face = TextFace.Of(font);
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            int plansWithRods = 0, plansWithLong = 0, plans = 0;
            bool figuresRight = true, noneLongWhenNoRods = true;
            foreach (string text in TextFixture.RandomTexts(200, 7))
            {
                foreach (float height in new[] { 0.3f, 1f })
                {
                    TextLayoutResult layout = TextLayout.Layout(text, TextStyle.Of(height), face);
                    FrameStyle style = FrameStyle.For(height);
                    FramePlan plan = LetterFrame.Plan(layout, c => cache.GetOrBuild(font, 1, c, 0), style);
                    float longest = 0f;
                    int over = 0;
                    foreach (FrameSegment rod in plan.Bridges)
                    {
                        longest = Math.Max(longest, rod.Length);
                        over += rod.Length > style.LongRod ? 1 : 0;
                    }

                    figuresRight &= Bits(plan.LongestBridge) == Bits(longest) && plan.LongBridges == over;
                    noneLongWhenNoRods &= plan.Bridges.Count > 0 || (plan.LongestBridge == 0f && plan.LongBridges == 0);
                    plans++;
                    plansWithRods += plan.Bridges.Count > 0 ? 1 : 0;
                    plansWithLong += plan.LongBridges > 0 ? 1 : 0;
                }
            }

            Program.Check(figuresRight, "the longest rod and the number over the limit are what the rods are");
            Program.Check(noneLongWhenNoRods, "a plan with no rod has no longest rod");
            Program.Check(plansWithRods > 0 && plansWithRods < plans, $"some of {plans} plans have rods ({plansWithRods}), not all");
            Program.Check(plansWithLong * 10 < plans, $"the warning is for the few: {plansWithLong} of {plans} plans have a rod over {FrameStyle.LongRodShare} of the letter height");
            Program.Check(FrameStyle.For(0.3f).LongRod == FrameStyle.LongRodShare * 0.3f && FrameStyle.For(2f).LongRod == FrameStyle.LongRodShare * 2f && FrameStyle.LongRodShare > 0f, "a rod is long over a share of the letter's height");

            // No cap: forty islands held by rods are forty, and the hints say so.
            var many = new FramePlan { BridgedIslands = 40, LongBridges = 12, LongestBridge = 0.2f };
            List<TextWarning> warnings = TextWarnings.Of(new TextLayoutResult(), many);
            Program.Check(warnings.Exists(w => w.Kind == TextWarningKind.HeldByRods && w.Count == 40) && warnings.Exists(w => w.Kind == TextWarningKind.LongRod && w.Count == 12), "forty islands held by rods are reported as forty");
        }

        // ------------------------------------------------------------------ the catalogue

        private static SupportDef ByHashWalk(uint hash)
        {
            if (hash == 0u)
                return null;
            foreach (SupportDef def in SupportCatalog.All)
            {
                if (def.Hash == hash)
                    return def;
            }

            return null;
        }

        private static void ByHashIsTheWalk()
        {
            bool all = true;
            foreach (SupportDef def in SupportCatalog.All)
                all &= ReferenceEquals(SupportCatalog.ByHash(def.Hash), ByHashWalk(def.Hash));
            Program.Check(all, "every support is found by its hash, as the walk found it");
            var random = new Random(3);
            bool unknown = SupportCatalog.ByHash(0u) == null;
            for (int i = 0; i < 100000 && unknown; i++)
            {
                uint hash = (uint)random.Next() ^ ((uint)random.Next() << 1);
                unknown = ReferenceEquals(SupportCatalog.ByHash(hash), ByHashWalk(hash));
            }

            Program.Check(unknown, "0 and 100,000 hashes that are in no support find what the walk found: none");
            // Where two supports had one hash the first of them was found: the table must not have taken the second.
            var seen = new Dictionary<uint, SupportDef>();
            bool firstWins = true;
            foreach (SupportDef def in SupportCatalog.All)
            {
                if (!seen.ContainsKey(def.Hash))
                    seen[def.Hash] = def;
                firstWins &= ReferenceEquals(SupportCatalog.ByHash(def.Hash), seen[def.Hash]);
            }

            Program.Check(firstWins, "the first of the supports with one hash is the one found");
            SignDef sign = SignCatalog.All[0];
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            Program.Check(ReferenceEquals(SupportCatalog.For(sign, gantry.Hash), gantry) && ReferenceEquals(SupportCatalog.For(sign, 0u), SupportPlanner.Auto(sign)), "For gives the support of the hash, or the planner's");
        }

        // ------------------------------------------------------------------ the glyph cache

        private static void GlyphCacheHits()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            int[] codepoints = { '永', '口', '小', 'A', ' ' };
            foreach (int c in codepoints)
                cache.GetOrBuild(font, 1, c, 0);
            // Two of the same call: the first of each (a miss) made the glyph; now each is a hit.
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                cache.GetOrBuild(font, 1, codepoints[i % codepoints.Length], 0);
            long garbage = GC.GetAllocatedBytesForCurrentThread() - before;
            Program.Check(garbage == 0, "a thousand hits made no garbage: " + garbage + " bytes");
            Program.Check(cache.Hits == 1000 && cache.Misses == codepoints.Length, $"hits {cache.Hits}, misses {cache.Misses}");
            Program.Check(ReferenceEquals(cache.GetOrBuild(font, 1, '永', 0), cache.GetOrBuild(new GlyphKey(1, '永', 0), () => null)), "the two forms of the call share one glyph");
            Program.Check(cache.GetOrBuild(font, 1, ' ', 0) == null, "a character with no outline is kept as none and not asked again");
            Program.Check(GlyphCache.DefaultBudgetBytes >= 16L * 1024 * 1024 && GlyphCache.DefaultBudgetBytes <= 256L * 1024 * 1024, "the default budget is a constant of tens of megabytes: " + GlyphCache.DefaultBudgetBytes);
        }

        // ------------------------------------------------------------------ the job

        private static void JobKey()
        {
            TextPlanJob a = new TextPlanJob("永远发光", 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f);
            TextPlanJob same = new TextPlanJob("永远发光", 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f);
            Program.Check(a.Equals(same) && a.Key == same.Key && a.GetHashCode() == same.GetHashCode(), "the same text and numbers: one key");
            var others = new[]
            {
                new TextPlanJob("永远发光灯", 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f), new TextPlanJob("永远发光", 2, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f),
                new TextPlanJob("永远发光", 1, 0.31f, 0f, 1.15f, TextAlign.Centre, 0.002f), new TextPlanJob("永远发光", 1, 0.3f, 0.1f, 1.15f, TextAlign.Centre, 0.002f),
                new TextPlanJob("永远发光", 1, 0.3f, 0f, 1.2f, TextAlign.Centre, 0.002f), new TextPlanJob("永远发光", 1, 0.3f, 0f, 1.15f, TextAlign.Left, 0.002f),
                new TextPlanJob("永远发光", 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.004f), new TextPlanJob("光发远永", 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f)
            };
            bool each = true;
            var keys = new HashSet<ulong> { a.Key };
            foreach (TextPlanJob other in others)
            {
                each &= !a.Equals(other) && a.Key != other.Key;
                keys.Add(other.Key);
            }

            Program.Check(each, "a change of any field, or of the order of the characters, is another job and another key");
            Program.Check(keys.Count == others.Length + 1, "the nine keys are all different");
            Program.Check(new TextPlanJob(null, 1, 0.3f, 0f, 1.15f, TextAlign.Left, 0.002f).Text == "", "no text is the empty text");
            Program.Check(a.Style.LetterHeight == 0.3f && a.Style.LineSpacing == 1.15f && a.Style.Align == TextAlign.Centre, "the job's style is the layout's");
        }

        // ------------------------------------------------------------------ the hints

        private static void Warnings()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            var planner = new TextPlanner(id => id == 1 ? font : null, new GlyphCache(GlyphCache.DefaultBudgetBytes));
            TextPlanResult clean = planner.Plan(new TextPlanJob("永远发光", 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f));
            Program.Check(clean.Warnings.Count == 0 && clean.Frame.Studs.Count > 0 && clean.Meshes.Length == 4, "a plain text has no hint");
            TextPlanResult absent = planner.Plan(new TextPlanJob("永☃远☃", 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f));
            Program.Check(absent.Warnings.Count == 1 && absent.Warnings[0].Kind == TextWarningKind.MissingCharacter && absent.Warnings[0].Codepoint == 0x2603, "a character the font lacks is named once");
            Program.Check(absent.Meshes[1] == null && absent.Layout.Glyphs[1].Missing, "and it is a blank, not a mesh");
            TextPlanResult cut = planner.Plan(new TextPlanJob(new string('永', 40), 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f));
            Program.Check(cut.Warnings.Exists(w => w.Kind == TextWarningKind.TextCut && w.Count == TextLayout.MaxChars), "a text cut at the limit says so");
            TextPlanResult none = planner.Plan(new TextPlanJob("永", 9, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f));
            Program.Check(none.Warnings.Count == 1 && none.Warnings[0].Kind == TextWarningKind.FontMissing && none.Frame == null && none.Layout.Glyphs.Count == 0, "no font: a hint and nothing planned");
            var keys = new HashSet<string>();
            bool prefixed = true;
            foreach (TextWarningKind kind in Enum.GetValues(typeof(TextWarningKind)))
            {
                string key = new TextWarning(kind).Key;
                keys.Add(key);
                prefixed &= key != null && key.StartsWith("UI.TextWarn", StringComparison.Ordinal);
            }

            Program.Check(keys.Count == Enum.GetValues(typeof(TextWarningKind)).Length && prefixed, "every kind has its own language key");

            // A frame with a single-stud island, a floating one, rods, a long rod: the hints in order.
            var frame = new FramePlan { BridgedIslands = 2, LongBridges = 1, LongestBridge = 0.12f, SingleStudIslands = 3, FloatingIslands = 1 };
            var layout = new TextLayoutResult();
            layout.Missing.Add(0x41);
            layout.Truncated = true;
            List<TextWarning> all = TextWarnings.Of(layout, frame);
            var kinds = new List<TextWarningKind>();
            foreach (TextWarning w in all)
                kinds.Add(w.Kind);
            Program.Check(string.Join(",", kinds) == "MissingCharacter,TextCut,HeldByRods,LongRod,SingleStud,FloatingIsland", "the hints in their order: " + string.Join(",", kinds));
            Program.Check(all[3].Metres == 0.12f && all[3].Count == 1 && all[4].Count == 3 && all[5].Count == 1, "with their numbers");
            Program.Check(TextWarnings.Of(null, null).Count == 0, "nothing planned, nothing said");
        }

        // ------------------------------------------------------------------ threads

        private static string Describe(TextPlanResult r)
        {
            string layout = r.Layout.Glyphs.Count + "/" + r.Layout.Lines;
            for (int i = 0; i < r.Layout.Glyphs.Count; i++)
                layout += "," + Bits(r.Layout.Glyphs[i].X) + ":" + Bits(r.Layout.Glyphs[i].Y) + (r.Meshes[i] != null ? "m" : "-");
            string warnings = "";
            foreach (TextWarning w in r.Warnings)
                warnings += w.Kind + ":" + w.Count + ":" + w.Codepoint + ":" + Bits(w.Metres) + ";";
            return layout + "|" + warnings;
        }

        /// <summary>The catalogue's two tables are put back to "not built yet" (they are private: by reflection) and eight threads ask for it at once: every one must get the same table, and it must be whole.</summary>
        private static void CatalogueOnce()
        {
            System.Reflection.FieldInfo every = typeof(SignCatalog).GetField("s_Every", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            System.Reflection.FieldInfo templates = typeof(SignCatalog).GetField("s_Templates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Program.Check(every != null && templates != null, "the catalogue's two lazily built tables are where the test looks for them");
            if (every == null || templates == null)
                return;
            IReadOnlyList<SignDef> before = SignCatalog.All;
            int expected = before.Count;
            int wrong = 0, differing = 0;
            for (int round = 0; round < 6; round++)
            {
                every.SetValue(null, null);
                templates.SetValue(null, null);
                var seen = new IReadOnlyList<SignDef>[8];
                var start = new Barrier(8);
                var threads = new Thread[8];
                for (int t = 0; t < 8; t++)
                {
                    int index = t;
                    threads[t] = new Thread(() =>
                    {
                        start.SignalAndWait();
                        seen[index] = SignCatalog.All;
                    });
                    threads[t].Start();
                }

                foreach (Thread thread in threads)
                    thread.Join();
                foreach (IReadOnlyList<SignDef> table in seen)
                {
                    if (table == null || table.Count != expected)
                        wrong++;
                    else if (!ReferenceEquals(table, seen[0]))
                        differing++;
                }
            }

            Program.Check(wrong == 0, "every thread got a whole catalogue of " + expected + " signs (" + wrong + " did not)");
            Program.Check(differing == 0, "and all eight got the same table, 6 rounds over: " + differing + " thread(s) got another");
            // The tables are left built, as every other test expects them.
            Program.Check(SignCatalog.All.Count == expected, "the catalogue is whole again after the test");
        }

        private static void Threads()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            // The catalogue is made on first use without a lock (SignCatalog.Every): the game asks for it on the main thread long before any worker does, and so does this.
            IReadOnlyList<SignDef> signs = SignCatalog.All;
            var contexts = new[] { new SupportContext { LanesSameDirection = 1 }, new SupportContext { LanesSameDirection = 3 }, new SupportContext { LanesSameDirection = 2, ExitLeftOrWide = true } };

            // What one thread says, first.
            var supportExpected = new List<string>();
            foreach (StyleProfile profile in StyleProfiles.All)
            {
                foreach (SignDef sign in signs)
                {
                    foreach (SupportContext context in contexts)
                    {
                        PlannedSupport planned = SupportPlanner.AutoStyled(sign, context, profile, false);
                        supportExpected.Add(planned.Support.Id + "|" + planned.StoredStyle + "|" + planned.StoredSupportHash);
                    }
                }
            }

            List<string> texts = TextFixture.RandomTexts(24, 99);
            var jobs = new List<TextPlanJob>();
            foreach (string text in texts)
            {
                jobs.Add(new TextPlanJob(text, 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f));
                jobs.Add(new TextPlanJob(text, 1, 1f, 0.05f, 1.2f, TextAlign.Left, 0.0006f));
            }

            var reference = new TextPlanner(id => font, new GlyphCache(GlyphCache.DefaultBudgetBytes));
            var textExpected = new List<string>();
            var frameExpected = new List<FramePlan>();
            foreach (TextPlanJob job in jobs)
            {
                TextPlanResult r = reference.Plan(job);
                textExpected.Add(Describe(r));
                frameExpected.Add(r.Frame);
            }

            // Eight threads share one font and one cache that is small enough to evict while they run, so that the lock is used for hits, misses, makes and evictions at once.
            const int threadCount = 8, rounds = 3;
            var shared = new GlyphCache(300L * 1024);
            var planner = new TextPlanner(id => font, shared);
            var start = new Barrier(threadCount);
            var wrong = new List<string>();
            var workers = new List<Thread>();
            for (int t = 0; t < threadCount; t++)
            {
                int me = t;
                var thread = new Thread(() =>
                {
                    start.SignalAndWait();
                    var mistakes = new List<string>();
                    try
                    {
                        for (int round = 0; round < rounds; round++)
                        {
                            // Each thread walks the jobs from its own place, so that they meet the same glyphs at different times.
                            for (int n = 0; n < jobs.Count; n++)
                            {
                                int i = (n + me * 5 + round) % jobs.Count;
                                TextPlanResult r = planner.Plan(jobs[i]);
                                if (Describe(r) != textExpected[i])
                                    mistakes.Add("text " + i + " differs");
                                else if (Differ(r.Frame, frameExpected[i]) is string differs)
                                    mistakes.Add("frame " + i + ": " + differs);
                            }

                            int k = 0;
                            foreach (StyleProfile profile in StyleProfiles.All)
                            {
                                foreach (SignDef sign in signs)
                                {
                                    foreach (SupportContext context in contexts)
                                    {
                                        PlannedSupport planned = SupportPlanner.AutoStyled(sign, context, profile, false);
                                        if (planned.Support.Id + "|" + planned.StoredStyle + "|" + planned.StoredSupportHash != supportExpected[k++])
                                            mistakes.Add("support " + (k - 1) + " differs");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        mistakes.Add("threw " + ex.GetType().Name);
                    }

                    lock (wrong)
                        wrong.AddRange(mistakes);
                }) { IsBackground = true };
                workers.Add(thread);
                thread.Start();
            }

            bool finished = true;
            foreach (Thread thread in workers)
                finished &= thread.Join(60000);
            long calls = shared.Hits + shared.Misses;
            Program.Check(finished, "the threads finished");
            Program.Check(wrong.Count == 0, "every plan of every thread equals what one thread made" + (wrong.Count == 0 ? "" : ": " + wrong[0] + " (" + wrong.Count + " in all)"));
            Program.Check(shared.Evictions > 0, "the cache evicted while they ran: " + shared.Evictions);
            Program.Check(shared.Bytes <= 300L * 1024 + 64L * 1024 && shared.Count > 0, "and kept to its budget: " + shared.Bytes + " bytes, " + shared.Count + " glyphs");
            long expectedCalls = 0;
            foreach (TextPlanJob job in jobs)
                expectedCalls += CallsOf(font, job);
            Console.WriteLine($"  planners: {threadCount} threads x {rounds} rounds of {jobs.Count} texts and {supportExpected.Count} supports; the cache: {shared.Hits} hits, {shared.Misses} misses, {shared.Evictions} evictions, {shared.Count} glyphs, {shared.Bytes} bytes");
            Program.Check(calls == expectedCalls * threadCount * rounds, $"its counters add up: {calls} hits and misses for {expectedCalls * threadCount * rounds} asks");
        }

        /// <summary>How many times a plan of a job asks the cache for a glyph: once for each placed character that is not a blank, and again for each by the frame's planner.</summary>
        private static long CallsOf(FontOutlines font, TextPlanJob job)
        {
            TextLayoutResult layout = TextLayout.Layout(job.Text, job.Style, TextFace.Of(font));
            long asks = 0;
            foreach (PlacedGlyph g in layout.Glyphs)
                asks += g.Missing ? 0 : 2;
            return asks;
        }

        // ------------------------------------------------------------------ the worker

        private static void WorkerGlue()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            var gate = new ManualResetEventSlim(false);
            var made = new List<string>();
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            var planner = new TextPlanner(id => font, cache);
            var worker = new LatestWorker<TextPlanJob, TextPlanResult>(job =>
            {
                lock (made)
                    made.Add(job.Text);
                if (job.Text == "一")
                    gate.Wait(5000);
                return planner.Plan(job);
            });
            TextPlanJob Job(string text) => new TextPlanJob(text, 1, 0.3f, 0f, 1.15f, TextAlign.Centre, 0.002f);
            worker.Submit(Job("一"));
            Program.Check(Until(() => { lock (made) return made.Count == 1; }), "the first job starts");
            worker.Submit(Job("永"));
            worker.Submit(Job("远"));
            worker.Submit(Job("发光"));
            gate.Set();
            TextPlanResult last = null;
            Program.Check(Until(() =>
            {
                if (worker.TryTake(out TextPlanResult r))
                    last = r;
                return last != null && last.Job.Text == "发光";
            }), "the newest job's plan comes");
            lock (made)
                Program.Check(string.Join(",", made) == "一,发光", "the two between were never planned: " + string.Join(",", made));
            Program.Check(last != null && last.Job.Equals(Job("发光")) && last.Frame.Studs.Count > 0 && last.Meshes.Length == 2, "the result is of the job asked for, and carries its job");
        }

        private static bool Until(Func<bool> done, int ms = 10000)
        {
            var clock = Stopwatch.StartNew();
            while (!done())
            {
                if (clock.ElapsedMilliseconds > ms)
                    return false;
                Thread.Sleep(1);
            }

            return true;
        }
    }
}
