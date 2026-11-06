using System;
using System.Collections.Generic;
using System.Threading;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    internal static class WarmSetTests
    {
        public static void RunAll()
        {
            Program.Run("warm set: a value that is not kept is made on a worker and waited for", Basics);
            Program.Run("warm set: a worker that leaves nothing kept gives one fallback; the limit of workers", Fallback);
            Program.Run("marking plans: TryKept never makes a plan and finds the one PlanOf kept", Kept);
        }

        private static void Kept()
        {
            SignDef def = SignCatalog.ChoiceById(MarkingTemplates.PlanterBoxId).Def;
            string spec = def.Template.Default().ToString();
            MarkingTemplates.ClearCaches();
            long parsed = MarkingTemplates.Parsed;
            Program.Check(!MarkingTemplates.TryKept(def, spec, out MarkingPlan none) && none == null, "before anything made it, TryKept says no");
            Program.Check(MarkingTemplates.Parsed == parsed, "and made nothing (no parse was counted)");
            MarkingPlan made = MarkingTemplates.PlanOf(def, spec);
            Program.Check(MarkingTemplates.TryKept(def, spec, out MarkingPlan plan) && ReferenceEquals(plan, made), "after PlanOf it is the same plan");
            string loose = "size=15";
            Program.Check(!MarkingTemplates.TryKept(def, loose, out _), "settings written another way are not the kept key until a PlanOf has seen them");
            MarkingPlan other = MarkingTemplates.PlanOf(def, loose);
            Program.Check(MarkingTemplates.TryKept(def, loose, out MarkingPlan again) && ReferenceEquals(again, other), "and are after it");
            Program.Check(!MarkingTemplates.TryKept(def, null, out _) && !MarkingTemplates.TryKept(def, "", out _), "settings of the empty text are not kept yet, null or not");
            MarkingTemplates.PlanOf(def, "");
            Program.Check(MarkingTemplates.TryKept(def, null, out _) && MarkingTemplates.TryKept(def, "", out _), "null settings are the empty text, as PlanOf takes them");
            Program.Check(MarkingTemplates.TryKept(null, spec, out MarkingPlan noDef) && noDef == null, "no sign: nothing to make, answered at once");
            MarkingTemplates.ClearCaches();
            Program.Check(!MarkingTemplates.TryKept(def, spec, out _), "a cleared cache keeps nothing");
            MarkingPlan reread = MarkingTemplates.PlanOf(def, spec);
            Program.Check(!ReferenceEquals(reread, made) && MarkingTemplates.TryKept(def, spec, out MarkingPlan back) && ReferenceEquals(back, reread), "made again after it, and kept again");
        }

        private static bool Until(Func<bool> done)
        {
            for (int i = 0; i < 2000 && !done(); i++)
                Thread.Sleep(1);
            return done();
        }

        private static void Basics()
        {
            var set = new WarmSet<string>();
            var kept = new HashSet<string>();
            var gate = new ManualResetEventSlim(false);
            int made = 0;
            Action makeA = () => { gate.Wait(); lock (kept) kept.Add("a"); Interlocked.Increment(ref made); };
            bool Has(string k) { lock (kept) return kept.Contains(k); }

            Program.Check(set.Ask("a", Has("a"), makeA) == Warmth.Waiting && set.InFlight == 1 && set.Started == 1, "not kept: a worker is started and the caller waits");
            Program.Check(set.Ask("a", Has("a"), makeA) == Warmth.Waiting && set.Started == 1, "asked again while it is made: still waiting, no second worker");
            gate.Set();
            Program.Check(Until(() => Has("a")), "the worker made it");
            Program.Check(set.Ask("a", Has("a"), makeA) == Warmth.Ready && set.InFlight == 0, "kept: ready, and forgotten");
            Program.Check(Until(() => Volatile.Read(ref made) == 1) && set.Fallbacks == 0, "made once, no fallback");
            Program.Check(set.Ask("b", true, () => throw new InvalidOperationException("never")) == Warmth.Ready && set.Started == 1, "a value that is kept at once needs no worker");
        }

        private static void Fallback()
        {
            var set = new WarmSet<int>(maxInFlight: 2);
            var done = new bool[4];
            var gates = new ManualResetEventSlim[] { new ManualResetEventSlim(false), new ManualResetEventSlim(false), new ManualResetEventSlim(false), new ManualResetEventSlim(false) };
            Action Make(int k) => () => { gates[k].Wait(); done[k] = true; };

            Program.Check(set.Ask(0, false, Make(0)) == Warmth.Waiting && set.Ask(1, false, Make(1)) == Warmth.Waiting && set.InFlight == 2, "two workers");
            Program.Check(set.Ask(2, false, Make(2)) == Warmth.Waiting && set.InFlight == 2 && set.Started == 2, "at the limit a third key waits without a worker");
            gates[0].Set();
            Program.Check(Until(() => done[0]), "the first worker finishes");
            Thread.Sleep(20);
            // It finished and nothing is kept (kept is false): the caller makes it.
            Program.Check(set.Ask(0, false, Make(0)) == Warmth.Fallback && set.Fallbacks == 1 && set.InFlight == 1, "a worker that left nothing kept gives a fallback, once");
            Program.Check(set.Ask(2, false, Make(2)) == Warmth.Waiting && set.Started == 3 && set.InFlight == 2, "and its place goes to the key that waited");
            Program.Check(set.Ask(0, false, Make(0)) == Warmth.Waiting && set.Started == 3, "the key that had its fallback waits again at the limit if the caller asks again");
            gates[1].Set();
            gates[2].Set();
            gates[3].Set();
            Program.Check(Until(() => done[1] && done[2]), "the others finish");
            set.Clear();
            Program.Check(set.InFlight == 0, "Clear forgets them");
            var one = new WarmSet<int>(maxInFlight: 0);
            Program.Check(one.Ask(1, false, () => { }) == Warmth.Waiting && one.Started == 1, "a limit under 1 is 1");
        }
    }
}
