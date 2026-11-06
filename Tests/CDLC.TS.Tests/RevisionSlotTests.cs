using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    internal static class RevisionSlotTests
    {
        public static void RunAll()
        {
            Program.Run("revision slot: kept while the revision stands, made again when it moves", Basics);
            Program.Run("revision slot: full, clear and a make that throws", Edges);
        }

        private sealed class Box
        {
            public int Size;
        }

        private static void Basics()
        {
            var slot = new RevisionSlot<Box>();
            int makes = 0;
            Func<Box> make = () => { makes++; return new Box(); };
            Box first = slot.Get(5, make);
            Program.Check(makes == 1 && slot.Made == 1 && slot.Hits == 0, "the first ask makes it");
            Program.Check(ReferenceEquals(slot.Get(5, make), first) && makes == 1 && slot.Hits == 1, "the same revision is the kept value");
            Box second = slot.Get(6, make);
            Program.Check(!ReferenceEquals(second, first) && makes == 2 && slot.Made == 2, "a newer revision makes it again");
            Program.Check(ReferenceEquals(slot.Get(6, make), second), "and keeps that one");
            Box older = slot.Get(5, make);
            Program.Check(!ReferenceEquals(older, second) && makes == 3, "an older revision is not the kept one either: the revision must be the same, not only not newer");
            Program.Check(slot.Made == 3 && slot.Hits == 2, "counters: " + slot.Made + " made, " + slot.Hits + " kept");
        }

        private static void Edges()
        {
            var slot = new RevisionSlot<Box>();
            int makes = 0;
            Func<Box> make = () => { makes++; return new Box(); };
            Func<Box, bool> full = b => b.Size >= 3;
            Box a = slot.Get(1, make, full);
            a.Size = 2;
            Program.Check(ReferenceEquals(slot.Get(1, make, full), a), "under the size limit it is kept");
            a.Size = 3;
            Box b = slot.Get(1, make, full);
            Program.Check(!ReferenceEquals(b, a) && makes == 2, "at the size limit it is made again for the same revision");
            slot.Clear();
            Program.Check(!ReferenceEquals(slot.Get(1, make, full), b) && makes == 3, "after Clear it is made again");

            Box kept = slot.Get(7, make);
            bool threw = false;
            try
            {
                slot.Get(8, () => throw new InvalidOperationException("no"));
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Program.Check(threw, "a make that throws throws");
            Program.Check(ReferenceEquals(slot.Get(7, make), kept), "and the value of the revision before is still the kept one");
            Program.Check(slot.Made == 4, "a make that threw is not counted as made: " + slot.Made);
        }
    }
}
