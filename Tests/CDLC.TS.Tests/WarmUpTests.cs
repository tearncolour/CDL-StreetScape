using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The warm-up of the first-use costs under the loading screen (WarmUp): budgets, the failure and slow policies, the worker's one start, and the cancel.</summary>
    internal static class WarmUpTests
    {
        public static void RunAll()
        {
            Program.Run("warm-up: a main step is called until it says it is done, then never again", MainDone);
            Program.Run("warm-up: the main steps share the budget round robin, with at least one call an update", MainBudget);
            Program.Run("warm-up: a step that throws is failed once and left, the others go on", MainThrows);
            Program.Run("warm-up: a main step slow twice in a row is dropped, one slow call is not", MainSlow);
            Program.Run("warm-up: the worker's steps start once, in order, and failures do not stop the next", WorkerRuns);
            Program.Run("warm-up: cancel drops what is left, the worker stops before its next step, and nothing is called after it", CancelDrops);
            Program.Run("warm-up: a worker that cannot be started fails its steps, an empty list waits for nothing, a late or doubled step is refused", Edges);
            Program.Run("warm-up: a worker on a real thread ends and is counted", RealThread);
        }

        private sealed class Clock
        {
            public double Ms;
        }

        private static WarmUp Make(Clock clock, List<Action> workers, double slowMs = WarmUp.DefaultSlowCallMs) => new WarmUp(() => clock.Ms, work => workers.Add(work), slowMs);

        private static void MainDone()
        {
            var clock = new Clock();
            WarmUp warm = Make(clock, new List<Action>());
            int calls = 0;
            warm.Add(new WarmStep("a", WarmThread.Main, "test", () => ++calls >= 3));
            Program.Check(warm.Waiting == 1, "one waiting");
            warm.Pump(1000.0);
            Program.Check(calls == 3 && warm.Waiting == 0 && warm.Steps[0].State == WarmState.Done, "called three times in one generous update, then done: " + calls);
            warm.Pump(1000.0);
            Program.Check(calls == 3, "a finished step is not called again");
            Program.Check(warm.Steps[0].Calls == 3, "the calls are counted: " + warm.Steps[0].Calls);

            WarmUp first = Make(clock, new List<Action>());
            int once = 0;
            first.Add(new WarmStep("b", WarmThread.Main, "test", () => { once++; return true; }));
            first.Pump(1000.0);
            Program.Check(once == 1 && first.Steps[0].State == WarmState.Done, "done on its first call");
        }

        private static void MainBudget()
        {
            var clock = new Clock();
            WarmUp warm = Make(clock, new List<Action>());
            int a = 0, b = 0;
            warm.Add(new WarmStep("a", WarmThread.Main, "test", () => { a++; clock.Ms += 2.0; return false; }));
            warm.Add(new WarmStep("b", WarmThread.Main, "test", () => { b++; clock.Ms += 2.0; return false; }));
            double took = warm.Pump(5.0);
            Program.Check(a + b == 3 && a == 2 && b == 1, "2 ms a call and 5 ms: three calls, the last one crosses the budget: a " + a + " b " + b);
            Program.Near((float)took, 6f, 1e-3f, "and the update took what the calls took");
            warm.Pump(5.0);
            Program.Check(a + b == 6 && a == 3 && b == 3, "the next update goes on where this left off: a " + a + " b " + b);

            warm.Pump(0.0);
            Program.Check(a + b == 7, "a budget of nothing still gives one call (no step starves)");
            warm.Pump(-5.0);
            Program.Check(a + b == 8, "so does a negative one");

            var exact = new Clock();
            WarmUp even = Make(exact, new List<Action>());
            int evens = 0;
            even.Add(new WarmStep("e", WarmThread.Main, "test", () => { evens++; exact.Ms += 2.0; return false; }));
            even.Pump(4.0);
            Program.Check(evens == 2, "a budget of exactly two calls' time gives two calls: " + evens);

            var stuck = new Clock();
            WarmUp never = Make(stuck, new List<Action>());
            int spins = 0;
            never.Add(new WarmStep("c", WarmThread.Main, "test", () => { spins++; return false; }));
            never.Pump(1000.0);
            Program.Check(spins > 1 && spins <= 4096 && never.Waiting == 1, "a step that never ends on a clock that does not move still ends the update: " + spins);
        }

        private static void MainThrows()
        {
            var clock = new Clock();
            WarmUp warm = Make(clock, new List<Action>());
            int bad = 0, good = 0;
            warm.Add(new WarmStep("bad", WarmThread.Main, "test", () => { bad++; throw new InvalidOperationException("no"); }));
            warm.Add(new WarmStep("good", WarmThread.Main, "test", () => ++good >= 2));
            warm.Pump(1000.0);
            Program.Check(bad == 1 && warm.Steps[0].State == WarmState.Failed && warm.Steps[0].Failure.Contains("InvalidOperationException: no"), "failed once, with the reason: " + warm.Steps[0].Failure);
            Program.Check(good == 2 && warm.Steps[1].State == WarmState.Done && warm.Waiting == 0 && warm.Failed == 1, "the other finished: " + good);
            Program.Check(warm.Describe().Contains("bad failed") && warm.Describe().Contains("good done"), "the line says both: " + warm.Describe());
        }

        private static void MainSlow()
        {
            var clock = new Clock();
            WarmUp warm = Make(clock, new List<Action>(), 30.0);
            int calls = 0;
            warm.Add(new WarmStep("slow", WarmThread.Main, "test", () => { calls++; clock.Ms += 31.0; return false; }));
            warm.Pump(1.0);
            Program.Check(warm.Steps[0].State == WarmState.Waiting && calls == 1, "one slow call: still going");
            warm.Pump(1.0);
            Program.Check(warm.Steps[0].State == WarmState.Skipped && calls == 2 && warm.Skipped == 1 && warm.Waiting == 0 && warm.Steps[0].Failure.Contains("dropped"), "two in a row: dropped");

            WarmUp edge = Make(clock, new List<Action>(), 30.0);
            edge.Add(new WarmStep("edge", WarmThread.Main, "test", () => { clock.Ms += 30.0; return false; }));
            edge.Pump(1.0);
            edge.Pump(1.0);
            Program.Check(edge.Steps[0].State == WarmState.Skipped, "a call of exactly the slow limit counts as slow");

            WarmUp once = Make(clock, new List<Action>(), 30.0);
            int n = 0;
            once.Add(new WarmStep("mixed", WarmThread.Main, "test", () => { n++; clock.Ms += n % 2 == 0 ? 5.0 : 31.0; return n >= 5; }));
            for (int i = 0; i < 5 && once.Waiting > 0; i++)
                once.Pump(1.0);
            Program.Check(n == 5 && once.Steps[0].State == WarmState.Done, "a quick call between the slow ones starts the count again: " + n + " " + once.Steps[0].State);

            WarmUp last = Make(clock, new List<Action>(), 30.0);
            last.Add(new WarmStep("lastslow", WarmThread.Main, "test", () => { clock.Ms += 99.0; return true; }));
            last.Pump(1.0);
            Program.Check(last.Steps[0].State == WarmState.Done, "a slow call that finishes the step is done, not dropped");
        }

        private static void WorkerRuns()
        {
            var clock = new Clock();
            var workers = new List<Action>();
            WarmUp warm = Make(clock, workers);
            var order = new List<string>();
            warm.Add(new WarmStep("w1", WarmThread.Worker, "test", () => { order.Add("w1"); return true; }));
            warm.Add(new WarmStep("m", WarmThread.Main, "test", () => true));
            warm.Add(new WarmStep("w2", WarmThread.Worker, "test", () => { order.Add("w2"); throw new InvalidOperationException("boom"); }));
            warm.Add(new WarmStep("w3", WarmThread.Worker, "test", () => { order.Add("w3"); return true; }));
            Program.Check(workers.Count == 0, "nothing starts before the first pump");
            warm.Pump(1.0);
            warm.Pump(1.0);
            Program.Check(workers.Count == 1, "one worker, started once: " + workers.Count);
            Program.Check(warm.Waiting == 3 && warm.Steps[0].State == WarmState.Waiting, "the worker's steps wait until it has run them");
            workers[0]();
            Program.Check(string.Join(",", order) == "w1,w2,w3", "in order: " + string.Join(",", order));
            Program.Check(warm.Steps[0].State == WarmState.Done && warm.Steps[2].State == WarmState.Failed && warm.Steps[3].State == WarmState.Done && warm.Waiting == 0, "w2 failed, the others done, nothing waits");
            Program.Check(warm.Steps[2].Failure.Contains("boom") && warm.Describe().Contains("(worker)"), "the failure is told: " + warm.Describe());

            var clock2 = new Clock();
            var queued = new List<Action>();
            WarmUp running = Make(clock2, queued);
            int seenWaiting = -1;
            WarmState seenState = WarmState.Waiting;
            running.Add(new WarmStep("w", WarmThread.Worker, "test", () => { seenWaiting = running.Waiting; seenState = running.Steps[0].State; return true; }));
            running.Pump(1.0);
            queued[0]();
            Program.Check(seenWaiting == 1 && seenState == WarmState.Running, "a worker step that is running is still waited for: " + seenWaiting + " " + seenState);
        }

        private static void CancelDrops()
        {
            var clock = new Clock();
            var workers = new List<Action>();
            WarmUp warm = Make(clock, workers);
            int main = 0, ran = 0;
            warm.Add(new WarmStep("m", WarmThread.Main, "test", () => { main++; return false; }));
            warm.Add(new WarmStep("w1", WarmThread.Worker, "test", () => { ran++; warm.Cancel(); return true; }));
            warm.Add(new WarmStep("w2", WarmThread.Worker, "test", () => { ran += 10; return true; }));
            warm.Pump(1.0);
            int before = main;
            workers[0]();
            Program.Check(ran == 1, "the worker stopped before its next step: " + ran);
            Program.Check(warm.Cancelled && warm.Waiting == 0 && warm.Skipped == 3, "everything is finished at once, the step that ended after the cancel stays dropped: " + warm.Describe());
            warm.Pump(1000.0);
            Program.Check(main == before, "no call after the cancel");

            var started = new List<Action>();
            WarmUp early = Make(clock, started);
            int calls = 0;
            early.Add(new WarmStep("m", WarmThread.Main, "test", () => { calls++; return true; }));
            early.Add(new WarmStep("w", WarmThread.Worker, "test", () => { calls += 100; return true; }));
            early.Cancel();
            early.Pump(1000.0);
            Program.Check(calls == 0 && early.Steps[0].State == WarmState.Skipped && early.Steps[1].State == WarmState.Skipped && started.Count == 0, "cancelled before the first pump: never called, no worker started");
            early.Cancel();
            Program.Check(early.Steps[0].State == WarmState.Skipped, "a second cancel changes nothing");

            WarmUp finished = Make(clock, new List<Action>());
            finished.Add(new WarmStep("m", WarmThread.Main, "test", () => true));
            finished.Pump(1000.0);
            finished.Cancel();
            Program.Check(finished.Steps[0].State == WarmState.Done, "a finished step is not turned into dropped");
        }

        private static void Edges()
        {
            var clock = new Clock();
            WarmUp throwing = new WarmUp(() => clock.Ms, work => throw new InvalidOperationException("no thread"));
            throwing.Add(new WarmStep("w", WarmThread.Worker, "test", () => true));
            throwing.Add(new WarmStep("m", WarmThread.Main, "test", () => true));
            throwing.Pump(1000.0);
            Program.Check(throwing.Steps[0].State == WarmState.Failed && throwing.Steps[0].Failure.Contains("could not be started") && throwing.Steps[1].State == WarmState.Done && throwing.Waiting == 0, "the thread could not be made: the worker's steps failed, the main one went on");

            WarmUp empty = Make(clock, new List<Action>());
            Program.Check(empty.Waiting == 0 && empty.Describe() == "no step" && empty.Pump(5.0) == 0.0, "an empty list waits for nothing");

            WarmUp warm = Make(clock, new List<Action>());
            Program.Check(warm.Add(new WarmStep("a", WarmThread.Main, "test", () => true)), "added");
            Program.Check(!warm.Add(new WarmStep("a", WarmThread.Main, "test", () => true)) && warm.Steps.Count == 1, "the same name twice is refused");
            Program.Check(!warm.Add(null), "null is refused");
            bool refused = false;
            try
            {
                new WarmStep("blind", WarmThread.Main, "  ", () => true);
            }
            catch (ArgumentException)
            {
                refused = true;
            }

            Program.Check(refused, "a step with no evidence is not made");
            warm.Pump(1.0);
            Program.Check(!warm.Add(new WarmStep("late", WarmThread.Worker, "test", () => true)) && warm.Steps.Count == 1, "after the first pump nothing more is added");
        }

        private static void RealThread()
        {
            WarmUp warm = new WarmUp(() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency, work =>
            {
                var thread = new System.Threading.Thread(() => work()) { IsBackground = true };
                thread.Start();
            });
            warm.Add(new WarmStep("w1", WarmThread.Worker, "test", () => { System.Threading.Thread.Sleep(20); return true; }));
            warm.Add(new WarmStep("w2", WarmThread.Worker, "test", () => true));
            warm.Pump(1.0);
            for (int i = 0; i < 200 && warm.Waiting > 0; i++)
                System.Threading.Thread.Sleep(10);
            Program.Check(warm.Waiting == 0 && warm.Steps[0].State == WarmState.Done && warm.Steps[1].State == WarmState.Done, "both done on the thread: " + warm.Describe());
            Program.Check(warm.Steps[0].Ms >= 15.0, "the milliseconds are the step's own: " + warm.Steps[0].Ms);
        }
    }
}
