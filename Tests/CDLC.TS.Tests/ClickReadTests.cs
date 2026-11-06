using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    internal static class ClickReadTests
    {
        public static void RunAll() => Program.Run("click read: a modified click the game's action did not give is read from the button", Direct);

        private static void Direct()
        {
            Program.Check(ClickRead.Direct(false, true, true), "the button went down, a modifier is held, the action is silent: the click is the tool's");
            Program.Check(!ClickRead.Direct(true, true, true), "the action fired too: it is the same click, not a second one");
            Program.Check(!ClickRead.Direct(false, true, false), "no modifier: the action is the only reader (a click over a text field or the UI is not the tool's)");
            Program.Check(!ClickRead.Direct(false, false, true), "a modifier alone is no click");
            Program.Check(!ClickRead.Direct(true, false, false) && !ClickRead.Direct(true, false, true) && !ClickRead.Direct(false, false, false), "nothing pressed, nothing read");
        }
    }
}
