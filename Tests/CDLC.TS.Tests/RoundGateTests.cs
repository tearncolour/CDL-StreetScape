using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    internal static class RoundGateTests
    {
        public static void RunAll()
        {
            Program.Run("round gate: rounds until one whole walk found nothing new, then none until the world changes", Walk);
            Program.Run("round gate: a change during a walk starts it again; busy, edits, reset", Changes);
        }

        /// <summary>Plays rounds of 12 over n items while nothing changes; returns the rounds made until the gate refused one.</summary>
        private static int RoundsUntilSettled(RoundGate gate, int revision, int items, int perRound = 12)
        {
            int rounds = 0;
            while (gate.Needed(false, revision, items, false))
            {
                gate.Checked(perRound, items);
                if (++rounds > 10000)
                    break;
            }

            return rounds;
        }

        private static void Walk()
        {
            var gate = new RoundGate();
            Program.Check(gate.Needed(false, 5, 1123, false) && !gate.Settled, "a fresh gate wants the first round");
            gate.Checked(12, 1123);
            int rounds = 1 + RoundsUntilSettled(gate, 5, 1123);
            Program.Check(rounds == (1123 + 11) / 12 && gate.Settled, $"the walk over 1123 items at 12 a round is {(1123 + 11) / 12} rounds, then the gate settles: {rounds}");
            Program.Check(!gate.Needed(false, 5, 1123, false) && !gate.Needed(false, 5, 1123, false), "settled: no round while nothing changes");
            Program.Check(gate.Needed(false, 6, 1123, false) && !gate.Settled, "a road network that changed wants a walk");
            Program.Check(RoundsUntilSettled(gate, 6, 1123) == (1123 + 11) / 12 && gate.Settled, "and settles again after a whole walk");
            Program.Check(!gate.Needed(false, 6, 1123, false), "settled at the new revision");
            Program.Check(gate.Needed(false, 6, 1124, false) && !gate.Settled, "an item more wants a walk");
            var empty = new RoundGate();
            Program.Check(empty.Needed(false, 0, 0, false), "no item: one round to learn it");
            empty.Checked(0, 0);
            Program.Check(!empty.Needed(false, 0, 0, false) && empty.Needed(false, 0, 1, false), "and none until an item appears");
            var one = new RoundGate();
            one.Needed(false, 1, 5, false);
            one.Checked(12, 5);
            Program.Check(one.Settled, "a round that took more than the items there are is a whole walk");
        }

        private static void Changes()
        {
            var gate = new RoundGate();
            gate.Needed(false, 1, 100, false);
            gate.Checked(60, 100);
            Program.Check(gate.Needed(false, 1, 100, false) && !gate.Settled, "half a walk: the next round is needed");
            Program.Check(gate.Needed(false, 2, 100, false), "the network changed half way: needed");
            gate.Checked(50, 100);
            Program.Check(!gate.Settled, "the 60 before the change do not count toward the walk since it: 50 of 100");
            gate.Checked(50, 100);
            Program.Check(gate.Settled && !gate.Needed(false, 2, 100, false), "100 since the change settle it");

            Program.Check(gate.Needed(false, 2, 100, true) && !gate.Settled, "an edited item starts a walk");
            gate.Checked(100, 100);
            Program.Check(gate.Settled, "walked");
            Program.Check(gate.Needed(true, 2, 100, false), "busy (the first check, a table being made) wants the round even when settled");
            Program.Check(gate.Settled, "and does not unsettle it");
            Program.Check(!gate.Needed(false, 2, 100, false), "busy over: settled still");

            var again = new RoundGate();
            again.Needed(false, 1, 10, false);
            again.Checked(10, 10);
            again.Reset();
            Program.Check(!again.Settled && again.Needed(false, 1, 10, false), "after a reset (a load) a round is needed at the very same numbers");
            var negative = new RoundGate();
            Program.Check(negative.Needed(false, -1, -1, false), "a fresh gate is not settled by numbers equal to its sentinel");
        }
    }
}
