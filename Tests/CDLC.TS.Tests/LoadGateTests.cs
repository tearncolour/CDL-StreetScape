using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The state of the first-time work under the loading screen (LoadGate) and its budgets (LoadBudget): the hold ends by itself, whatever else happens.</summary>
    internal static class LoadGateTests
    {
        public static void RunAll()
        {
            Program.Run("load gate: begins only for a saved game loaded into the game, with the switch on, once", Begin);
            Program.Run("load gate: done when every source has had nothing waiting for three updates in a row", Done);
            Program.Run("load gate: the cap ends it whatever is waiting", Cap);
            Program.Run("load gate: errors, the screen going, a new load and a dispose end it at once and only once", Stops);
            Program.Run("load gate: progress is what was taken from the waiting work, never 1 while running", Progress);
            Program.Run("load budget: the loading budgets grow six times to at most 14 ms and the steady ones are not touched", Budget);
            Program.Run("early restore: waits the settle frames, then the links, and goes on without them at the give-up frame", EarlyReady);
            Program.Run("early restore: the sample of roads is even, bounded, in order and without repeats", EarlySample);
            Program.Run("early restore: the links are there when 49 of 50 of the sampled roads have them", EarlyLinked);
        }

        private static void EarlyReady()
        {
            int asked = 0;
            System.Func<bool> never = () => { asked++; return false; };
            System.Func<bool> always = () => { asked++; return true; };
            Program.Check(!EarlyRestore.Ready(0, 3, 10, always) && !EarlyRestore.Ready(2, 3, 10, always) && asked == 0, "before the settle frames: no, and the links are not even asked");
            Program.Check(EarlyRestore.Ready(3, 3, 10, always) && asked == 1, "at the settle frame the links are asked and are there: yes");
            Program.Check(!EarlyRestore.Ready(5, 3, 10, never), "the links are not there yet: wait");
            Program.Check(!EarlyRestore.Ready(9, 3, 10, never), "one frame before the give-up: still wait");
            asked = 0;
            Program.Check(EarlyRestore.Ready(10, 3, 10, never) && asked == 0, "at the give-up frame it goes on without them (and does not ask)");
            Program.Check(EarlyRestore.Ready(50, 3, 10, never), "after it as well");
            Program.Check(EarlyRestore.Ready(3, 3, 3, null) && !EarlyRestore.Ready(2, 3, 3, null), "the late rule (give-up = settle, no links asked): the old eight-frame behaviour");
            Program.Check(EarlyRestore.Ready(4, 3, 10, null), "no way to ask: not held");
        }

        private static void EarlySample()
        {
            Program.Check(EarlyRestore.SampleIndices(0, 5).Length == 0 && EarlyRestore.SampleIndices(5, 0).Length == 0 && EarlyRestore.SampleIndices(-3, 5).Length == 0, "nothing to sample: empty");
            int[] few = EarlyRestore.SampleIndices(4, 10);
            Program.Check(few.Length == 4 && few[0] == 0 && few[3] == 3, "fewer roads than the sample: all of them");
            int[] some = EarlyRestore.SampleIndices(1000, 128);
            Program.Check(some.Length == 128 && some[0] == 0 && some[127] == 999, "the first and the last are in: " + some[0] + " " + some[127]);
            bool ordered = true;
            for (int i = 1; i < some.Length; i++)
                ordered &= some[i] > some[i - 1];
            Program.Check(ordered, "strictly increasing: none twice");
            int[] one = EarlyRestore.SampleIndices(1000, 1);
            Program.Check(one.Length == 1 && one[0] == 0, "a sample of one is the first");
            int[] edge = EarlyRestore.SampleIndices(129, 128);
            bool unique = true;
            for (int i = 1; i < edge.Length; i++)
                unique &= edge[i] != edge[i - 1];
            Program.Check(edge.Length == 128 && unique && edge[127] == 128, "one over the sample: still 128, none twice, ends at the last");
            int[] huge = EarlyRestore.SampleIndices(int.MaxValue, 128);
            Program.Check(huge.Length == 128 && huge[127] == int.MaxValue - 1 && huge[1] > 0, "a huge count does not overflow");
        }

        private static void EarlyLinked()
        {
            Program.Check(EarlyRestore.Linked(0, 0) && EarlyRestore.Linked(-1, 0), "no road sampled: nothing to wait for");
            Program.Check(EarlyRestore.Linked(128, 128) && EarlyRestore.Linked(50, 50), "all linked: yes");
            Program.Check(EarlyRestore.Linked(100, 98) && EarlyRestore.Linked(50, 49), "one road in fifty broken (the game's own): still yes");
            Program.Check(!EarlyRestore.Linked(100, 97) && !EarlyRestore.Linked(50, 48) && !EarlyRestore.Linked(128, 0), "more broken than that: no");
            Program.Check(EarlyRestore.Linked(1, 1) && !EarlyRestore.Linked(1, 0), "one sampled road: it must be linked");
        }

        private sealed class Clock
        {
            public double Now;
        }

        private static LoadGate Gate(Clock clock, double cap = LoadGate.DefaultCapSeconds) => new LoadGate(() => clock.Now, cap);

        private static LoadGate Started(Clock clock, params string[] sources)
        {
            LoadGate gate = Gate(clock);
            Program.Check(gate.Begin(true, true, true), "begun");
            foreach (string source in sources)
                gate.Add(source, 1f);
            return gate;
        }

        private static void Begin()
        {
            var clock = new Clock();
            Program.Check(!Gate(clock).Begin(false, true, true), "the switch is off: not begun");
            Program.Check(!Gate(clock).Begin(true, false, true), "not a saved game (a new game, the menu): not begun");
            Program.Check(!Gate(clock).Begin(true, true, false), "not the game mode (the editor): not begun");
            LoadGate gate = Gate(clock);
            Program.Check(gate.Begin(true, true, true) && gate.Running && gate.End == LoadEnd.None, "begun and running");
            Program.Check(!gate.Begin(true, true, true), "never twice for one load");
            gate.Stop(LoadEnd.Screen);
            Program.Check(!gate.Running && gate.Begin(true, true, true) && gate.Running, "after the end another load may begin");
            Program.Check(!Gate(clock).Step() && Gate(clock).Progress == 0f && Gate(clock).WaitingNow == 0, "a gate that never began does nothing");
        }

        private static void Done()
        {
            var clock = new Clock();
            LoadGate gate = Started(clock, "tiles", "fits");
            gate.Report("tiles", 5);
            gate.Report("fits", 0, false);
            Program.Check(gate.Step() && gate.Running, "a source that cannot say yet holds it");
            gate.Report("fits", 0);
            Program.Check(gate.Step() && gate.Running, "tiles still have 5 waiting");
            gate.Report("tiles", 0);
            for (int i = 1; i < LoadGate.StableUpdates; i++)
                Program.Check(gate.Step() && gate.Running, "nothing waiting, but not for long enough yet: update " + i);
            Program.Check(!gate.Step() && !gate.Running && gate.End == LoadEnd.Done, "nothing waiting for the third update in a row: done");
            Program.Check(!gate.Step(), "a finished gate stays finished");

            LoadGate flicker = Started(clock, "a");
            flicker.Report("a", 0);
            flicker.Step();
            flicker.Step();
            flicker.Report("a", 1);
            Program.Check(flicker.Step() && flicker.Running, "work that came back resets the run");
            flicker.Report("a", 0);
            flicker.Step();
            flicker.Step();
            Program.Check(flicker.Running && !flicker.Step() && flicker.End == LoadEnd.Done, "and the three updates are counted again from there");

            LoadGate unsaid = Started(clock, "a", "b");
            unsaid.Report("a", 0);
            unsaid.Report("b", 0, false);
            for (int i = 0; i < LoadGate.StableUpdates * 3; i++)
                unsaid.Step();
            Program.Check(unsaid.Running && unsaid.End == LoadEnd.None, "a source that never says (nothing waiting, not known) holds it for as many updates as there are");
            unsaid.Report("b", 4, false);
            Program.Check(unsaid.WaitingNow == 0, "what an unknown source says is not counted as waiting: " + unsaid.WaitingNow);
            unsaid.Report("b", 0);
            unsaid.Step();
            unsaid.Step();
            Program.Check(unsaid.Step() == false && unsaid.End == LoadEnd.Done, "once it says, the three quiet updates are counted from there");

            LoadGate empty = Started(clock);
            Program.Check(empty.Step() && empty.Step() && empty.Step() && empty.Step() && empty.Running, "a gate with no source is never done by itself (the cap or an end decides)");
        }

        private static void Cap()
        {
            var clock = new Clock();
            LoadGate gate = Started(clock, "tiles");
            gate.Report("tiles", 100);
            clock.Now = LoadGate.DefaultCapSeconds - 0.01;
            Program.Check(gate.Step() && gate.Running, "just before the cap it goes on");
            clock.Now = LoadGate.DefaultCapSeconds;
            Program.Check(!gate.Step() && gate.End == LoadEnd.Cap && !gate.Running, "at the cap it ends, with the work still waiting");
            Program.Check(gate.WaitingNow == 100 && gate.Describe().Contains("tiles 100 of 100 waiting"), "and says what was left: " + gate.Describe());
            Program.Near((float)gate.Seconds, (float)LoadGate.DefaultCapSeconds, 1e-3f, "the seconds are fixed at the end");
            clock.Now = 99.0;
            Program.Near((float)gate.Seconds, (float)LoadGate.DefaultCapSeconds, 1e-3f, "and do not run on");

            LoadGate unknown = Gate(clock, 5.0);
            clock.Now = 200.0;
            unknown.Begin(true, true, true);
            unknown.Add("x", 1f);
            unknown.Report("x", 0, false);
            clock.Now = 205.0;
            Program.Check(!unknown.Step() && unknown.End == LoadEnd.Cap, "a source that never becomes known cannot hold the screen past the cap");
        }

        private static void Stops()
        {
            var clock = new Clock();
            foreach (LoadEnd reason in new[] { LoadEnd.Error, LoadEnd.Screen, LoadEnd.Disposed, LoadEnd.NoWork })
            {
                LoadGate gate = Started(clock, "a");
                gate.Report("a", 7);
                gate.Stop(reason, "because");
                Program.Check(!gate.Running && gate.End == reason && gate.Detail == "because" && !gate.Step() && gate.Progress == 1f, reason + " ends it, and progress is complete");
                gate.Stop(LoadEnd.Cap, "again");
                Program.Check(gate.End == reason && gate.Detail == "because", reason + ": the first reason stays");
            }

            LoadGate idle = Gate(clock);
            idle.Stop(LoadEnd.Error);
            Program.Check(idle.End == LoadEnd.None && !idle.Running, "stopping what never began changes nothing");
        }

        private static void Progress()
        {
            var clock = new Clock();
            LoadGate gate = Started(clock, "a", "b");
            Program.Near(gate.Progress, 0f, 1e-6f, "nothing known: 0");
            gate.Report("a", 10);
            gate.Report("b", 4);
            Program.Near(gate.Progress, 0f, 1e-6f, "all of it still waiting: 0");
            gate.Report("a", 5);
            Program.Near(gate.Progress, 0.25f, 1e-6f, "half of one of two equal sources taken: a quarter");
            gate.Report("a", 0);
            gate.Report("b", 0);
            Program.Check(gate.Progress <= 0.999f && gate.Progress > 0.99f, "everything taken: just under 1 while it runs: " + gate.Progress);

            LoadGate weighted = Gate(clock);
            weighted.Begin(true, true, true);
            weighted.Add("big", 3f);
            weighted.Add("small", 1f);
            weighted.Report("big", 2);
            weighted.Report("small", 1);
            weighted.Report("big", 0);
            Program.Near(weighted.Progress, 0.75f, 1e-6f, "the weights count: 3 of 4");
            weighted.Add("big", 9f);
            weighted.Report("nobody", 3);
            Program.Near(weighted.Progress, 0.75f, 1e-6f, "a source added twice keeps its first weight; one that is not added is ignored");

            LoadGate high = Started(clock, "a");
            high.Report("a", 4);
            high.Report("a", 8);
            high.Report("a", 2);
            Program.Near(high.Progress, 0.75f, 1e-6f, "the total is the most that was waiting at once: 6 of 8 taken");
            high.AddMs("a", 12.4);
            high.AddMs("a", 0.3);
            Program.Check(high.Describe().Contains("a 2 of 8 waiting, 13 ms"), "the line has the milliseconds: " + high.Describe());
        }

        private static void Budget()
        {
            Program.Near((float)LoadBudget.Scaled(0.25, false), 0.25f, 1e-6f, "steady: untouched");
            Program.Near((float)LoadBudget.Scaled(0.25, true), 1.5f, 1e-6f, "loading: six times");
            Program.Near((float)LoadBudget.Scaled(2.0, true), 12.0f, 1e-6f, "2 ms is 12");
            Program.Near((float)LoadBudget.Scaled(3.0, true), 14.0f, 1e-6f, "never more than 14 ms: 18 is cut to 14");
            Program.Near((float)LoadBudget.Scaled(20.0, true), 20.0f, 1e-6f, "a steady budget over the cap is not made smaller");
            Program.Near((float)LoadBudget.Scaled(0.0, true), 0f, 1e-6f, "zero stays zero");
            LoadBudget.Loading = false;
            Program.Near((float)LoadBudget.Scaled(0.4), 0.4f, 1e-6f, "the flag is off: steady");
            LoadBudget.Loading = true;
            Program.Near((float)LoadBudget.Scaled(0.4), 2.4f, 1e-6f, "the flag is on: loading");
            LoadBudget.Loading = false;
        }
    }
}
