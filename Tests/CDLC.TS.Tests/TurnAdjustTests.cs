using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The turn of a board (0.12.80: a slider and a box besides the keys): the steps of [ and ], that a step of 15 degrees is the float it always was (the stored turn of a board and the rotation of a sign
    /// about to be placed must not change), the angle a slider or the box gives, the wrap at half a turn, what the panel shows, and which supports take a rotation when placing.
    /// </summary>
    internal static class TurnAdjustTests
    {
        // What the tool computed before: Mathf.Deg2Rad, kRotateStep = 15f * Mathf.Deg2Rad, and TurnBoard's own arithmetic.
        private const float OldDeg2Rad = 0.0174532924f;
        private const float OldRotateStep = 15f * OldDeg2Rad;

        public static void RunAll()
        {
            Program.Run("turn: the steps of [ and ] and the float of a step as it always was", Steps);
            Program.Run("turn: a step of a board on its post is the old arithmetic, bit for bit", AfterIsTheOld);
            Program.Run("turn: the angle of the slider and the box, to a tenth, around half a turn", Absolute);
            Program.Run("turn: what the panel shows", Shown);
            Program.Run("turn: which supports take a rotation while placing", PlaceApplies);
            Program.Run("turn: a slider's commits and a key held are one step to take back", OneStep);
        }

        private sealed class TurnStore : EditHistory<int, string>.IStore
        {
            public readonly System.Collections.Generic.Dictionary<int, string> Items = new System.Collections.Generic.Dictionary<int, string>();

            public bool Exists(int key) => Items.ContainsKey(key);

            public string Read(int key) => Items[key];

            public void Write(int key, string value) => Items[key] = value;

            public int Create(string value) => throw new InvalidOperationException("a turn makes no item");

            public void Destroy(int key) => Items.Remove(key);
        }

        // The tool writes a turn with WriteItem (history.Before(entity, old, true), the frame and the time noted first): the writes of one board within EditHistory.MergeSeconds of each other are one step.
        private static void OneStep()
        {
            var store = new TurnStore();
            store.Items[1] = "0";
            var history = new EditHistory<int, string>();
            double[] times = { 0.0, 0.3, 0.9, 1.6 };
            string[] values = { "10", "20", "30", "40" };
            for (int i = 0; i < times.Length; i++)
            {
                history.At(100 + i * 5, times[i]);
                history.Before(1, store.Items[1], true);
                store.Items[1] = values[i];
            }

            Program.Check(history.UndoCount == 1, "four writes of a board in 1.6 s are one step: " + history.UndoCount);
            history.At(200, 4.0);
            history.Before(1, store.Items[1], true);
            store.Items[1] = "90";
            Program.Check(history.UndoCount == 2, "a write 2.4 s after the last is a step of its own: " + history.UndoCount);
            Program.Check(history.Undo(store, out _, out int changed, out _) && store.Items[1] == "40" && changed == 1, "one step back: the facing the slid board had before the last write: " + store.Items[1]);
            Program.Check(history.Undo(store, out _, out _, out _) && store.Items[1] == "0", "the next back: the facing it had before the drag: " + store.Items[1]);
        }

        private static void Steps()
        {
            Program.Check(TurnAdjust.StepDegrees(false, false) == 15 && TurnAdjust.StepDegrees(true, false) == 5 && TurnAdjust.StepDegrees(false, true) == 1, "15 degrees, Shift 5, Alt 1");
            Program.Check(TurnAdjust.StepDegrees(true, true) == 1, "Shift and Alt together are the finest");
            Program.Check(TurnAdjust.Deg2Rad == OldDeg2Rad, "the degree is Unity's float: " + TurnAdjust.Deg2Rad.ToString("R"));
            Program.Check(TurnAdjust.CoarseStep == OldRotateStep && TurnAdjust.StepRadians(false, false) == OldRotateStep, "the coarse step is kRotateStep");
            Program.Check(TurnAdjust.StepRadians(true, false) == 5f * OldDeg2Rad && TurnAdjust.StepRadians(false, true) == 1f * OldDeg2Rad && TurnAdjust.StepRadians(true, true) == TurnAdjust.FinestStep, "the finer steps in radians");

            // The rotation of the sign about to be placed: presses of the coarse step add up exactly as before, whatever the number of presses and their order.
            float before = 0f, after = 0f;
            int[] presses = { 1, 1, 1, -1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, 1, 1, 1 };
            foreach (int press in presses)
            {
                before += press * OldRotateStep;
                after += press * TurnAdjust.StepRadians(false, false);
                Program.Check(before == after, "the rotation after the press is the same float: " + before.ToString("R") + " and " + after.ToString("R"));
            }
        }

        private static float OldTurnBoard(float turn, int degrees)
        {
            float next = degrees == 0 ? 0f : Assembly.WrapTurn(turn + degrees * OldDeg2Rad);
            return Math.Abs(next) < 0.05f * OldDeg2Rad ? 0f : next;
        }

        private static void AfterIsTheOld()
        {
            float[] turns = { 0f, 0.2617994f, -0.2617994f, 1.5707964f, -1.5707964f, 3.0f, -3.0f, 3.1415925f, -3.1415925f, 0.0000003f, 2.9f, -2.9f };
            int[] steps = { 0, 1, -1, 5, -5, 15, -15, 90, -90, 180 };
            int checks = 0;
            foreach (float turn in turns)
                foreach (int degrees in steps)
                {
                    float old = OldTurnBoard(turn, degrees), now = TurnAdjust.After(turn, degrees);
                    Program.Check(old == now || (float.IsNaN(old) && float.IsNaN(now)), $"a turn of {turn:R} by {degrees} is {old:R} (now {now:R})");
                    checks++;
                }

            Program.Check(checks == turns.Length * steps.Length, "all combinations");
            float chain = 0f, oldChain = 0f;
            foreach (int degrees in new[] { 15, 15, 15, -5, -1, 90, 15, 180, 15, -15, -15, 5, 1, 1 })
            {
                chain = TurnAdjust.After(chain, degrees);
                oldChain = OldTurnBoard(oldChain, degrees);
                Program.Check(chain == oldChain, "a chain of steps is the old chain: " + chain.ToString("R"));
            }

            Program.Check(TurnAdjust.After(0.2617994f, -15) == 0f || Math.Abs(TurnAdjust.After(0.2617994f, -15)) < 0.05f * OldDeg2Rad, "there and back is no turn, not a turn of a few millionths");
        }

        private static void Absolute()
        {
            Program.Check(TurnAdjust.Absolute(15f) == TurnAdjust.After(0f, 15), "15 degrees on the slider is the float of one step of 15");
            Program.Check(TurnAdjust.Absolute(-90f) == TurnAdjust.After(0f, -90) && TurnAdjust.Absolute(90f) == TurnAdjust.After(0f, 90), "and ninety either way");
            Program.Check(TurnAdjust.Absolute(37.4f) == 37.4f * OldDeg2Rad, "a slid angle is stored as the angle times the degree");
            Program.Check(TurnAdjust.Absolute(0f) == 0f && TurnAdjust.Absolute(-0.04f) == 0f && TurnAdjust.Absolute(0.04f) == 0f && TurnAdjust.Absolute(360f) == 0f, "0 is no turn, and under a twentieth of a degree");
            Program.Check(BitConverter.ToInt32(BitConverter.GetBytes(TurnAdjust.Absolute(-0.04f)), 0) == 0 && BitConverter.ToInt32(BitConverter.GetBytes(TurnAdjust.Absolute(-360f)), 0) == 0, "and it is +0, not -0: the stored turn of an unturned board is the 0 it always was");
            Program.Check(float.IsNaN(TurnAdjust.Absolute(float.NaN)) && float.IsNaN(TurnAdjust.Absolute(float.PositiveInfinity)), "not a number is nothing");

            // Half a turn: both ends of the slider are one facing, and it is the float that "turn round" stores.
            float round = TurnAdjust.After(0f, 180);
            Program.Check(TurnAdjust.Absolute(180f) == round && TurnAdjust.Absolute(-180f) == round && TurnAdjust.Absolute(540f) == round, "half a turn is what the button stores");
            Program.Check(TurnAdjust.Shown(round) == -180f, "and the panel shows it as -180");

            // Any number is a facing: 179.9 and 0.2 more is -179.9, 200 is -160, a whole turn is none.
            Program.Check(TurnAdjust.Absolute(179.9f + 0.2f) == TurnAdjust.Absolute(-179.9f), "179.9 and 0.2 more is -179.9");
            Program.Check(TurnAdjust.Absolute(200f) == TurnAdjust.Absolute(-160f) && TurnAdjust.Absolute(-200f) == TurnAdjust.Absolute(160f), "200 is -160");
            Program.Check(TurnAdjust.Absolute(360f) == 0f && TurnAdjust.Absolute(-360f) == 0f && TurnAdjust.Absolute(720f) == 0f, "a whole turn is no turn");

            // The slider's every tenth comes back as it was set, the two ends included (-180 stands for half a turn; 179.9 is the last).
            int wrong = 0;
            for (int tenths = -1800; tenths <= 1799; tenths++)
            {
                float degrees = tenths / 10f;
                int back = (int)Math.Round(TurnAdjust.Shown(TurnAdjust.Absolute(degrees)) * 10.0);
                if (back != tenths)
                    wrong++;
            }

            Program.Check(wrong == 0, wrong + " of the 3,600 tenths of the slider do not come back as they were set");
            Program.Check(TurnAdjust.SliderMin == -180f && TurnAdjust.SliderMax == 179.9f && TurnAdjust.SliderStep == 0.1f, "the slider's range");
        }

        private static void Shown()
        {
            Program.Check(TurnAdjust.Shown(0f) == 0f && BitConverter.ToInt32(BitConverter.GetBytes(TurnAdjust.Shown(-0f)), 0) == 0, "no turn is 0, never -0");
            Program.Check(TurnAdjust.Shown(float.NaN) == 0f && TurnAdjust.Shown(float.PositiveInfinity) == 0f, "not a number is 0");
            Program.Check(TurnAdjust.Shown((float)Math.PI) == -180f && TurnAdjust.Shown(-(float)Math.PI) == -180f, "half a turn either way is -180");
            Program.Check(TurnAdjust.Shown(30f * OldDeg2Rad) == 30f && TurnAdjust.Shown(-45f * OldDeg2Rad) == -45f, "a turn in degrees");
            Program.Check(TurnAdjust.Shown(37.44f * OldDeg2Rad) == 37.4f && TurnAdjust.Shown(37.46f * OldDeg2Rad) == 37.5f, "to a tenth");
            // The rotation of a sign about to be placed is not wrapped (a press adds): 13 presses of 15 degrees are 195, which is -165.
            float rotation = 0f;
            for (int i = 0; i < 13; i++)
                rotation += TurnAdjust.StepRadians(false, false);
            Program.Check(TurnAdjust.Shown(rotation) == -165f, "13 presses of 15 degrees show as -165: " + TurnAdjust.Shown(rotation));
        }

        private static void PlaceApplies()
        {
            Program.Check(TurnAdjust.PlaceApplies(false, false, false), "a post, a gantry, a cantilever, an attached support: every road path takes the rotation");
            Program.Check(!TurnAdjust.PlaceApplies(true, false, false), "a wall's flat board and bracket: no rotation");
            Program.Check(!TurnAdjust.PlaceApplies(false, true, false), "a signal's arm: no rotation");
            Program.Check(TurnAdjust.PlaceApplies(true, false, true), "a roof takes it");
            Program.Check(TurnAdjust.PlaceApplies(false, false, true), "a roof flag wins");

            // The real catalogue: a sign on each of its supports.
            SignDef board = SignCatalog.ById("gb.prohib.09"), marking = SignCatalog.ById("mk.line.edge");
            Program.Check(board != null && !board.IsMarking && marking != null && marking.IsMarking, "a board and a marking of the catalogue");
            int applies = 0, refuses = 0;
            foreach (SupportDef support in SupportCatalog.All)
            {
                bool expected = support.Roof || (!SupportKinds.OnWall(support.Kind) && !SupportKinds.OnArm(support.Kind));
                Program.Check(TurnAdjust.PlaceApplies(board, support) == expected, support.Id + ": " + expected);
                Program.Check(!TurnAdjust.PlaceApplies(marking, support), support.Id + ": a marking takes the row on no support");
                if (expected)
                    applies++;
                else
                    refuses++;
            }

            Program.Check(applies > 10 && refuses >= 3, $"{applies} supports take it, {refuses} do not");
            Program.Check(TurnAdjust.PlaceApplies(board, SupportCatalog.ById("pole.single")) && TurnAdjust.PlaceApplies(board, SupportCatalog.ById("pole.double")) && TurnAdjust.PlaceApplies(board, SupportCatalog.ById("gantry.portal")), "posts and a gantry take it");
            Program.Check(!TurnAdjust.PlaceApplies(board, SupportCatalog.ById(SupportCatalog.WallFlat)) && TurnAdjust.PlaceApplies(board, SupportCatalog.ById(SupportCatalog.WallRoof)), "a flat wall board does not, a roof does");
            Program.Check(!TurnAdjust.PlaceApplies(board, SupportCatalog.ById("wall.bracket")) && !TurnAdjust.PlaceApplies(board, SupportCatalog.ById("attached.arm")), "a bracket and a signal's arm do not");
            Program.Check(TurnAdjust.PlaceApplies(board, null) && !TurnAdjust.PlaceApplies(null, null) && !TurnAdjust.PlaceApplies(marking, null), "the planner's support takes it, no sign and a marking do not");
        }
    }
}
