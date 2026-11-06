using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>0.12.63: the editor's picture is made on a worker and only the newest request is made (LatestWorker).</summary>
    internal static class LatestWorkerTests
    {
        public static void RunAll()
        {
            Program.Run("latest worker: a job that is overtaken before it starts is dropped", Overtaken);
            Program.Run("latest worker: the result is taken once, and a job that fails does not stop the next", Failing);
            Program.Run("latest worker: the caller does not wait for the work", NotBlocking);
        }

        private static bool Until(Func<bool> done, int ms = 5000)
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

        private static void Overtaken()
        {
            var gate = new ManualResetEventSlim(false);
            var ran = new List<int>();
            var worker = new LatestWorker<int, int>(job =>
            {
                lock (ran)
                    ran.Add(job);
                if (job == 1)
                    gate.Wait(5000);
                return job * 10;
            });
            worker.Submit(1);
            Program.Check(Until(() => { lock (ran) return ran.Count == 1; }), "the first job starts");
            // While the first is being made, three more come: only the last is made after it.
            worker.Submit(2);
            worker.Submit(3);
            worker.Submit(4);
            gate.Set();
            int last = 0;
            Program.Check(Until(() =>
            {
                if (worker.TryTake(out int r))
                    last = r;
                return last == 40;
            }), "the newest job's result comes: " + last);
            lock (ran)
                Program.Check(ran.Count == 2 && ran[0] == 1 && ran[1] == 4, "2 and 3 were never made: " + string.Join(", ", ran));
            Program.Check(worker.Made == 2, "made " + worker.Made);
            Program.Check(!worker.TryTake(out _), "a result is taken once");
        }

        private static void Failing()
        {
            var errors = new List<string>();
            var worker = new LatestWorker<int, int>(job =>
            {
                if (job == 1)
                    throw new InvalidOperationException("no picture");
                return job;
            }, ex => { lock (errors) errors.Add(ex.Message); });
            worker.Submit(1);
            Program.Check(Until(() => { lock (errors) return errors.Count == 1; }), "the failure is reported");
            Program.Check(!worker.TryTake(out _), "and gives no result");
            worker.Submit(2);
            int got = 0;
            Program.Check(Until(() => worker.TryTake(out got)) && got == 2, "the next job is made after a failure: " + got);
        }

        private static void NotBlocking()
        {
            var gate = new ManualResetEventSlim(false);
            var worker = new LatestWorker<int, int>(job => { gate.Wait(5000); return job; });
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
                worker.Submit(i);
            long ms = clock.ElapsedMilliseconds;
            gate.Set();
            Program.Check(ms < 200, $"a hundred submits of work that takes seconds took {ms} ms of the caller's");
            int got = -1;
            Program.Check(Until(() => { if (worker.TryTake(out int r)) got = r; return got == 99; }), "and the last is the one made: " + got);
        }
    }
}
