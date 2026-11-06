using System.Collections.Generic;
using System.Threading;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The pure parts of the ECS and save audit (0.12.82): what the engine-bound code of the systems decides on.</summary>
    internal static class EcsAuditTests
    {
        public static void RunAll()
        {
            Program.Run("audit: the load's hold waits for the planters' scan only while the copies can still become usable", PlantHold);
            Program.Run("audit: the kerb bits of a save's copy are put back and no other flag is undone", KerbBitsBack);
            Program.Run("audit: what is deferred is each entity once, in order, alive ones only, and taken once", DeferredTake);
            Program.Run("audit: an event from another thread is kept in order for the main thread, one on it is handled at once", GateThreads);
        }

        private static void PlantHold()
        {
            Program.Check(!PlantWait.Waiting(false, true, 3, 0) && !PlantWait.Waiting(false, false, 3, 0), "no scan wanted: nothing to wait for");
            Program.Check(PlantWait.Waiting(true, true, 3, 9999), "the planters are ready and the scan runs: waits whatever the frames");
            Program.Check(PlantWait.Waiting(true, false, 3, 0) && PlantWait.Waiting(true, false, 3, PlantWait.PrepareFrames - 1), "copies not usable yet: waits for the frames they are given");
            Program.Check(!PlantWait.Waiting(true, false, 3, PlantWait.PrepareFrames), "copies still unusable after those frames: the hold does not wait for them");
            Program.Check(!PlantWait.Waiting(true, false, 0, 0), "no copy at all: nothing can come, no wait");
        }

        private static void KerbBitsBack()
        {
            const uint style = Kerbs.LowBit, other = 0x1u, added = 0x4u;
            uint copy = style | other;
            uint now = added;
            uint back = Kerbs.WithStyleOf(now, copy);
            Program.Check((back & Kerbs.Bits) == style, "the style bits of the copy are back");
            Program.Check((back & added) != 0, "a flag set since the copy was taken stays");
            Program.Check((back & other) == 0, "a flag the copy had and the road lost since is not given back (the whole copy would have)");
            Program.Check(Kerbs.WithStyleOf(style | Kerbs.HighBit, 0u) == 0u, "a style the copy did not have is cleared");
            Program.Check(Kerbs.WithStyleOf(copy, copy) == copy, "the road as the copy left it comes back as it was");
        }

        private static void DeferredTake()
        {
            var wanted = new Deferred<int>();
            wanted.Add(5);
            wanted.Add(3);
            wanted.Add(5);
            wanted.Add(9);
            Program.Check(wanted.Count == 3, "an entity asked for twice is one");
            List<int> taken = wanted.Take(i => i != 3);
            Program.Check(taken.Count == 2 && taken[0] == 5 && taken[1] == 9, "in the order asked, the one that is gone left out");
            Program.Check(wanted.Count == 0 && wanted.Take(i => true).Count == 0, "taken once: nothing is left for the next frame");
            wanted.Add(5);
            Program.Check(wanted.Take(i => true).Count == 1, "an entity taken may be asked for again");
            wanted.Add(1);
            wanted.Clear();
            Program.Check(wanted.Count == 0 && wanted.Take(i => true).Count == 0, "cleared: nothing left");
        }

        private static void GateThreads()
        {
            var gate = new MainThreadGate<int>(Thread.CurrentThread.ManagedThreadId);
            Program.Check(gate.OnMainThread && gate.Enter(7), "on the main thread: handled at once");
            Program.Check(!gate.TryTake(out _), "and nothing is kept for later");

            bool handledAtOnce = true;
            var other = new Thread(() =>
            {
                handledAtOnce = gate.Enter(1);
                gate.Enter(2);
                gate.Enter(3);
            });
            other.Start();
            other.Join();
            Program.Check(!handledAtOnce, "from another thread: not handled there");
            var order = new List<int>();
            while (gate.TryTake(out int item))
                order.Add(item);
            Program.Check(order.Count == 3 && order[0] == 1 && order[1] == 2 && order[2] == 3, "kept for the main thread in the order they came");

            int total = 0;
            var threads = new List<Thread>();
            for (int t = 0; t < 8; t++)
            {
                var worker = new Thread(() =>
                {
                    for (int i = 0; i < 500; i++)
                        gate.Enter(1);
                });
                threads.Add(worker);
                worker.Start();
            }

            foreach (Thread worker in threads)
                worker.Join();
            while (gate.TryTake(out int item))
                total += item;
            Program.Check(total == 8 * 500, "events from many threads at once are all kept, none lost");
        }
    }
}
