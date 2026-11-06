using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// How high a board may be fixed on a building (0.12.80, WallHeight): the bounds that held a board at 60 m (the lower edge's clamp), 30 m (the lift of the placing mode) and 200 m (a stored reach read
    /// back) are replaced by the building's own top; everything the old code gave up to 60 m is unchanged.
    /// </summary>
    internal static class WallHeightTests
    {
        public static void RunAll()
        {
            Program.Run("wall height: the 3-argument LowerEdge is the base commit's over a grid, whatever the height", Golden);
            Program.Run("wall height: the highest follows the building, 60 m the least, the ceiling the most, unknown is the ceiling", Highest);
            Program.Run("wall height: a tower's lower edge is kept (150 m), the bound that held it is told", Tower);
            Program.Run("wall height: the lift of the placing mode is the old one within 30 m and goes on beyond", Lift);
            Program.Run("wall height: a held up or down key speeds up and climbs a tower in a few seconds", Hold);
            Program.Run("wall height: a stored reach is read back to the ceiling, as before below 200 m", ReadBack);
            Program.Run("wall height: the save record keeps a reach of 250 m", SavedHigh);
        }

        /// <summary>The 30,544 results of the base commit's LowerEdge over the grid (made on 54026d3 in a temporary worktree: boards 0.3 to 2.4 m, four standard heights, the cursor from -5 to 700 m, NaN and the infinities).</summary>
        private const int kGoldenCount = 30544;

        private const string kGoldenHash = "b3cadcc361c4d821";

        private static IEnumerable<(float Middle, float Board, float Standard)> Grid()
        {
            float[] boards = { 0.3f, 0.6f, 1.2f, 2.4f }, standards = { 2.3f, 2.25f, 1.8f, 5.0f };
            var middles = new List<float> { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
            for (float m = -5f; m <= 700f; m += 0.37f)
                middles.Add(m);
            foreach (float b in boards)
                foreach (float s in standards)
                    foreach (float m in middles)
                        yield return (m, b, s);
        }

        private static string Hash(Func<float, float, float, float> make, out int count)
        {
            ulong h = 1469598103934665603UL;
            count = 0;
            foreach ((float middle, float board, float standard) in Grid())
            {
                h = (h ^ (ulong)BitConverter.SingleToInt32Bits(make(middle, board, standard))) * 1099511628211UL;
                count++;
            }

            return h.ToString("x16");
        }

        private static void Golden()
        {
            string hash = Hash((m, b, s) => WallMount.LowerEdge(m, b, s), out int count);
            Program.Check(count == kGoldenCount && hash == kGoldenHash, $"the old overload: {count} results, hash {hash} against the base's {kGoldenHash}");
            string explicitSixty = Hash((m, b, s) => WallHeight.Limit(WallMount.Asked(m, b, s), WallMount.MaxHeight, out _), out _);
            Program.Check(explicitSixty == kGoldenHash, "the new path with the old 60 m is the same results: " + explicitSixty);
            Program.Check(WallMount.MaxHeight == 60f && WallMount.MinHeight == 0.2f, "the old bounds are the least of the new ones");
        }

        private static void Highest()
        {
            Program.Near(WallHeight.Highest(float.NaN), WallHeight.Ceiling, 1e-6f, "a building that is not known does not hold a board down");
            Program.Near(WallHeight.Highest(float.PositiveInfinity), WallHeight.Ceiling, 1e-6f, "an infinite top is not known either");
            Program.Near(WallHeight.Highest(20f), 60f, 1e-6f, "a low building: the old 60 m, so what was placed stays placeable");
            Program.Near(WallHeight.Highest(0f), 60f, 1e-6f, "a top of nothing: 60 m");
            Program.Near(WallHeight.Highest(-3f), 60f, 1e-6f, "a top under the foot: 60 m");
            Program.Near(WallHeight.Highest(54f), 60f, 1e-6f, "the top and the margin under 60 m: 60 m");
            Program.Near(WallHeight.Highest(100f), 105f, 1e-6f, "a tower of 100 m: its top and the margin");
            Program.Near(WallHeight.Highest(250f), 255f, 1e-6f, "a tower of 250 m");
            Program.Near(WallHeight.Highest(800f), WallHeight.Ceiling, 1e-6f, "bounds that are nonsense: the ceiling");
            Program.Check(WallHeight.Ceiling >= 200f && WallHeight.Margin > 0f, "the ceiling is over the reach the saves could keep until now");
        }

        private static void Tower()
        {
            // The cursor 120.9 m up the wall of a 150 m tower, a board of 0.6 m: the board's middle at the cursor.
            float highest = WallHeight.Highest(150f), standard = 2.3f;
            float asked = WallMount.Asked(120.9f, 0.6f, standard);
            float kept = WallHeight.Limit(asked, highest, out HeightBound bound);
            Program.Near(asked, 120.6f, 1e-4f, "asked: the middle at the cursor, to 10 cm");
            Program.Near(kept, 120.6f, 1e-4f, "kept as asked on a tower");
            Program.Check(bound == HeightBound.None, "no bound held it");
            Program.Near(WallMount.LowerEdge(120.9f, 0.6f, standard), 60f, 1e-6f, "the old overload still gives 60: this is what the owner met");
            kept = WallHeight.Limit(WallMount.Asked(400f, 0.6f, standard), highest, out bound);
            Program.Near(kept, highest, 1e-4f, "over the roof: the building's highest");
            Program.Check(bound == HeightBound.Highest, "and the highest is told");
            kept = WallHeight.Limit(WallMount.Asked(0.1f, 0.6f, standard), highest, out bound);
            Program.Near(kept, WallMount.MinHeight, 1e-6f, "under the foot: the lowest");
            Program.Check(bound == HeightBound.Lowest, "and the lowest is told");
            // A low building keeps its 60 m: a board over its roof is refused as it was.
            kept = WallHeight.Limit(90f, WallHeight.Highest(18f), out bound);
            Program.Near(kept, 60f, 1e-6f, "a building of 18 m: 60 m at most");
            Program.Check(bound == HeightBound.Highest, "held by the highest");
            string line = WallHeight.Describe(131f, 10f, 450f, 105f, 105f, HeightBound.Highest);
            Program.Check(line.Contains("cursor 131.0") && line.Contains("ground 10.0") && line.Contains("asked 450.0") && line.Contains("kept 105.0") && line.Contains("highest 105.0") && line.Contains("held by the highest"),
                "the log line says the cursor, the ground, what was asked and kept, the bounds and which held: " + line);
            Program.Check(!WallHeight.Describe(3f, 0f, 2.3f, 2.3f, 60f, HeightBound.None).Contains("held"), "and nothing of a bound when none held");
        }

        private static void Lift()
        {
            bool same = true;
            int checks = 0;
            for (float offset = -29.5f; offset <= 29.5f; offset += 0.37f)
            {
                foreach (int dir in new[] { -1, 1 })
                    foreach (bool shift in new[] { false, true })
                    {
                        float old = FacadeMove.Accumulate(offset, dir, shift);
                        float now = WallHeight.Lift(offset, dir * FacadeMove.StepOf(shift));
                        float next = offset + dir * FacadeMove.StepOf(shift);
                        if (Math.Abs(next) <= FacadeMove.MaxShift && BitConverter.SingleToInt32Bits(old) != BitConverter.SingleToInt32Bits(now))
                            same = false;
                        checks++;
                    }
            }

            Program.Check(same && checks > 400, $"within 30 m: the old accumulator's results, bit for bit ({checks} steps)");
            Program.Near(FacadeMove.Accumulate(30f, 1, true), 30f, 1e-6f, "the old accumulator stopped at 30 m");
            Program.Near(WallHeight.Lift(30f, 0.5f), 30.5f, 1e-5f, "the lift goes on past it");
            Program.Near(WallHeight.Lift(120f, 10f), 130f, 1e-5f, "to a hundred metres and more");
            Program.Near(WallHeight.Lift(499f, 10f), WallHeight.Ceiling, 1e-5f, "and stops at the ceiling");
            Program.Near(WallHeight.Lift(-499f, -10f), -WallHeight.Ceiling, 1e-5f, "either way");
            // The edit mode's lift: FacadeMove.Height within the ceiling.
            Program.Near(FacadeMove.Height(59.8f, 0.5f, WallMount.MinHeight, WallHeight.Ceiling), 60.3f, 1e-4f, "the edit mode's arrow passes 60 m");
            Program.Near(FacadeMove.Height(59.8f, 0.5f, WallMount.MinHeight, WallMount.MaxHeight), 60f, 1e-4f, "(and held at 60 m as before when the old bound is given)");
            Program.Near(FacadeMove.Height(499.8f, 10f, WallMount.MinHeight, WallHeight.Ceiling), WallHeight.Ceiling, 1e-4f, "the edit mode stops at the ceiling");
        }

        /// <summary>Seconds a held arrow takes to climb metres, as ArrowPress repeats: the first press at once, then from 0.4 s every 0.07 s, each press the step of the time it has been held.</summary>
        private static float SecondsToClimb(float metres, bool shift)
        {
            float climbed = FacadeMove.StepOf(shift), t = 0.4f;
            while (climbed < metres && t < 120f)
            {
                climbed += WallHeight.LiftStep(shift, t);
                t += 0.07f;
            }

            return t;
        }

        private static void Hold()
        {
            Program.Near(WallHeight.LiftStep(false, 0f), 0.1f, 1e-6f, "a first press: 0.1 m");
            Program.Near(WallHeight.LiftStep(true, 0f), 0.5f, 1e-6f, "with Shift 0.5 m");
            Program.Near(WallHeight.LiftStep(false, 0.99f), 0.1f, 1e-6f, "held less than a second: the same");
            Program.Near(WallHeight.LiftStep(false, 1f), 0.5f, 1e-6f, "held a second: five times");
            Program.Near(WallHeight.LiftStep(true, 1.5f), 2.5f, 1e-6f, "with Shift five times 0.5");
            Program.Near(WallHeight.LiftStep(false, 2.5f), 2f, 1e-6f, "held two and a half seconds: twenty times");
            Program.Near(WallHeight.LiftStep(true, 10f), 10f, 1e-6f, "with Shift 10 m a press");
            float plain = SecondsToClimb(150f, false), shifted = SecondsToClimb(150f, true);
            float old = 150f / (FacadeMove.CoarseStep / 0.07f);
            Program.Check(plain <= 10f && shifted <= 5f, $"150 m takes {plain:0.0} s and {shifted:0.0} s with Shift (it took {old:0} s at the old best step)");
            Program.Check(shifted < plain, "the Shift is the faster");
            Program.Check(WallHeight.CoarseButton == 10f, "the editor's coarse button is 10 m");
        }

        private static void ReadBack()
        {
            bool same = true;
            for (float r = -5f; r <= 200f; r += 0.37f)
            {
                float old = r >= 0f && r <= 200f ? r : 0f;
                if (BitConverter.SingleToInt32Bits(old) != BitConverter.SingleToInt32Bits(WallHeight.ReadReach(r)))
                    same = false;
            }

            Program.Check(same, "from -5 to 200 m: the old rule's result, bit for bit");
            Program.Near(WallHeight.ReadReach(250f), 250f, 1e-6f, "250 m is read back (it was 0, the board's foot)");
            Program.Near(WallHeight.ReadReach(WallHeight.Ceiling), WallHeight.Ceiling, 1e-6f, "the ceiling is read back");
            Program.Near(WallHeight.ReadReach(WallHeight.Ceiling + 1f), 0f, 1e-6f, "over the ceiling: 0");
            Program.Near(WallHeight.ReadReach(float.NaN), 0f, 1e-6f, "not a number: 0");
            Program.Near(WallHeight.ReadReach(float.PositiveInfinity), 0f, 1e-6f, "infinite: 0");
            Program.Near(WallHeight.ReadReach(-0.5f), 0f, 1e-6f, "negative: 0");
        }

        private static void SavedHigh()
        {
            var record = new SaveRecord { ModVersion = "0.12.80", City = "8b1a9953-c461-4f8a-9e1c-123456789abc", SaveId = "a1b2c3", SaveName = "t", CityName = "t", Time = 638660000000000000L };
            record.Items.Add(new StoredItem { TypeHash = 9, Position = new Vec3(1f, 2f, 3f), Reach = 250.4f, BaseReach = 0f, Anchor = -1 });
            var stream = new MemoryStream();
            record.Write(stream);
            stream.Position = 0;
            SaveRecord back = SaveRecord.Read(stream, false);
            Program.Check(back.Items.Count == 1 && back.Items[0].Reach == 250.4f, "the record keeps a reach of 250.4 m: " + (back.Items.Count == 1 ? back.Items[0].Reach.ToString() : "no item"));
        }
    }
}
