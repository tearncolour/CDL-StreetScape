using System;
using System.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The pacing of the fits under the loading screen (0.12.80): the ordering of the fit queues by frames or seconds (Cadence.OrderDue) and the take step's budget as a share of the frame (LoadBudget.Take).
    /// The drain model replays the owner's 0.12.79 log with the production functions and the costs the log gives; it is a model of the loop, not of the game (the game's frame rate, its own tasks and the
    /// worker's timing are not in it).
    /// </summary>
    internal static class HoldBudgetTests
    {
        public static void RunAll()
        {
            Program.Run("hold budget: the fit queues are ordered after 30 frames or half a second, at once when nothing near waits", OrderDue);
            Program.Run("hold budget: the take is a share of the frame, never below the scaled budget, capped, unknown frame time changes nothing", ByFrame);
            Program.Run("hold budget: the frame average follows the frames, one long frame does not open the budget", Average);
            Program.Run("hold budget: the drain model: the fits near the camera are done sooner at the loading screen's frame rates, and nothing changes at a fast one", Model);
        }

        private static void OrderDue()
        {
            Program.Check(!Cadence.OrderDue(false, 0, 99, 9f) && !Cadence.OrderDue(false, 5, 99, 9f), "nothing new waits: never ordered");
            Program.Check(!Cadence.OrderDue(true, 0, 1, 9f) && Cadence.OrderDue(true, 0, 2, 0f), "nothing near waits: at the second frame, whatever the time");
            Program.Check(!Cadence.OrderDue(true, 3, 29, 0.49f), "near ones wait and new ones came: not yet at 29 frames and 0.49 s");
            Program.Check(Cadence.OrderDue(true, 3, 30, 0.1f), "30 frames: ordered (a fast frame rate)");
            Program.Check(Cadence.OrderDue(true, 3, 5, 0.5f), "half a second: ordered at 11 frames a second, not after 30 frames (2.7 s)");
            Program.Check(!Cadence.OrderDue(true, 3, 15, 0.49f) && Cadence.OrderDue(true, 3, 15, 0.5f), "the edge of the seconds");
            Program.Check(Cadence.OrderDue(true, 38, 30, 0.5f) && !Cadence.OrderDue(true, 38, 0, 0f), "38 near, as in the log: the counts say the same");
        }

        private static void ByFrame()
        {
            // The scaled steady budget is 1.5 ms for 0.25.
            Program.Near((float)LoadBudget.Take(0.25, true, 109.0), 5.45f, 1e-4f, "109 ms frames (the owner's log): 5.45 ms");
            Program.Near((float)LoadBudget.Take(0.25, true, 50.0), 2.5f, 1e-4f, "50 ms frames: 2.5 ms");
            Program.Near((float)LoadBudget.Take(0.25, true, 16.7), 1.5f, 1e-4f, "60 frames a second: the scaled budget, as before");
            Program.Near((float)LoadBudget.Take(0.25, true, 30.0), 1.5f, 1e-4f, "30 ms frames: 1.5 ms is still the more");
            Program.Near((float)LoadBudget.Take(0.25, true, 400.0), 6.0f, 1e-4f, "400 ms frames: capped at 6 ms");
            Program.Near((float)LoadBudget.Take(0.25, false, 109.0), 0.25f, 1e-6f, "not loading: the steady budget");
            Program.Near((float)LoadBudget.Take(0.25, true, 0.0), 1.5f, 1e-4f, "frame time not known: the scaled budget");
            Program.Near((float)LoadBudget.Take(0.25, true, -5.0), 1.5f, 1e-4f, "a negative frame time is not known");
            Program.Near((float)LoadBudget.Take(0.0, true, 109.0), 0f, 1e-6f, "a budget that is off stays off");
            Program.Near((float)LoadBudget.ByFrame(2.0, 0.1, 14.0, true, 109.0), 12.0f, 1e-4f, "a larger budget keeps the scaled value when it is more (12 against 10.9)");
            Program.Near((float)LoadBudget.ByFrame(2.0, 0.2, 14.0, true, 109.0), 14.0f, 1e-4f, "a share of 21.8 ms is cut to the cap of 14");

            LoadBudget.Loading = true;
            LoadBudget.FrameMs = 109.0;
            Program.Near((float)LoadBudget.Take(0.25), 5.45f, 1e-4f, "the flag and the average are the ones the systems read");
            LoadBudget.Loading = false;
            Program.Near((float)LoadBudget.Take(0.25), 0.25f, 1e-6f, "the flag off: steady");
            LoadBudget.FrameMs = 0.0;
        }

        private static void Average()
        {
            LoadBudget.FrameMs = 0.0;
            LoadBudget.NoteFrame(0.1);
            Program.Near((float)LoadBudget.FrameMs, 100f, 1e-3f, "the first frame is the average");
            LoadBudget.NoteFrame(0.2);
            Program.Near((float)LoadBudget.FrameMs, 120f, 1e-3f, "a fifth of the way to the next: 120");
            LoadBudget.NoteFrame(5.0);
            Program.Near((float)LoadBudget.FrameMs, 120f + (500f - 120f) * 0.2f, 1e-2f, "a frame of 5 s counts as 500 ms: 196, not 1,096");
            LoadBudget.FrameMs = 0.0;
            LoadBudget.NoteFrame(-1.0);
            Program.Check(LoadBudget.FrameMs == 0.0, "a clock that went back is no frame");
            LoadBudget.FrameMs = 0.0;
        }

        // ------------------------------------------------------------------ the model

        /// <summary>The costs the owner's 0.12.79 log gives: a take 0.22 ms (the figure that makes the old take budget of 1.5 ms drain 66 fits a second at 9 frames a second, as the log shows), a submit 0.9 ms (reading for the worker: average 0.91 ms).</summary>
        private const double kTakeCost = 0.22, kSubmitCost = 0.9, kPassBudget = 12.0, kSteadyTake = 0.25;

        private const int kMaxOut = 48;

        private sealed class Drain
        {
            public double OrderedAt, DoneAt, WorstOurMs, OurMs;
            public int Frames, Takes;
        }

        /// <summary>
        /// The fits near the camera after the owner's 0.12.79 log: 38 slow ones wait near the camera (markings whose roads are read in windows: they went from 38 to 12 in 0.9 s, 1.5 a frame at 20 frames
        /// a second), 1,018 more come, unordered, of which 259 are near once the queues are ordered; the last ordering was 5 frames ago. Each frame: the queues are ordered when due; the take step takes
        /// what the worker made, within the take budget; the pass submits within 12 ms (the take's time counts in it, the clock starts before it) while fewer than 48 requests are out; what is submitted
        /// is made by the worker before the next frame. Done when nothing near waits and nothing is out. baseMs is the frame without this mod's work.
        /// </summary>
        private static Drain Run(double baseMs, bool frames30Only, bool takeByFrame)
        {
            int near = 0, unordered = 1018, ready = 0, framesSince = 5;
            double slow = 38.0;
            double t = 0.0, sinceOrder = 5 * (baseMs / 1000.0), ema = baseMs;
            var result = new Drain { OrderedAt = -1.0 };
            while ((near + (int)Math.Ceiling(slow) + unordered + ready > 0) && result.Frames < 100000)
            {
                result.Frames++;
                double ours = 0.0;
                int nearNow = near + (int)Math.Ceiling(slow);
                if (unordered > 0 && (frames30Only ? (nearNow == 0 ? framesSince >= 2 : framesSince >= 30) : Cadence.OrderDue(true, nearNow, framesSince, (float)sinceOrder)))
                {
                    near += 259;
                    unordered = 0;
                    framesSince = 0;
                    sinceOrder = 0.0;
                    result.OrderedAt = t;
                }

                double takeBudget = takeByFrame ? LoadBudget.Take(kSteadyTake, true, ema) : LoadBudget.Scaled(kSteadyTake, true);
                int taken = 0;
                while ((taken == 0 || ours < takeBudget) && ready > 0)
                {
                    ready--;
                    taken++;
                    ours += kTakeCost;
                }

                result.Takes += taken;
                int submitted = 0;
                while (near > 0 && ours < kPassBudget && ready + submitted < kMaxOut)
                {
                    near--;
                    submitted++;
                    ours += kSubmitCost;
                }

                ready += submitted;
                slow = Math.Max(0.0, slow - 1.5);
                double frame = baseMs + ours;
                t += frame / 1000.0;
                sinceOrder += frame / 1000.0;
                framesSince++;
                ema += (frame - ema) * 0.2;
                result.OurMs += ours;
                if (ours > result.WorstOurMs)
                    result.WorstOurMs = ours;
            }

            result.DoneAt = t;
            return result;
        }

        private static void Model()
        {
            var table = new StringBuilder();
            foreach (double baseMs in new[] { 100.0, 45.0, 16.0 })
            {
                Drain old = Run(baseMs, true, false), now = Run(baseMs, false, true);
                string row = $"  base frame {baseMs,5:0} ms ({1000.0 / (baseMs + 8.0),4:0.0} fps with the old rules' work): ordered at {old.OrderedAt:0.00} s -> {now.OrderedAt:0.00} s; near fits done at {old.DoneAt:0.00} s -> {now.DoneAt:0.00} s "
                    + $"({old.Frames} -> {now.Frames} frames, {old.Takes} -> {now.Takes} takes); our work a frame: worst {old.WorstOurMs:0.0} -> {now.WorstOurMs:0.0} ms, in all {old.OurMs:0} -> {now.OurMs:0} ms";
                table.AppendLine(row);
                Program.Check(old.Frames < 100000 && now.Frames < 100000, "the model ends: " + row);
                Program.Check(now.WorstOurMs <= kPassBudget + kSubmitCost + 1e-9, "a frame of this mod's work is the pass budget and one item over at the most, not more: " + row);
                if (baseMs >= 90.0)
                {
                    Program.Check(old.DoneAt - now.DoneAt >= 2.0, "at the loading screen's 9 frames a second the near fits are done at least 2 s sooner: " + row);
                    Program.Check(now.OrderedAt < old.OrderedAt - 1.0 && now.OrderedAt <= 0.6, "and the queues are ordered more than a second sooner, within the half second: " + row);
                }
                else if (baseMs >= 40.0)
                {
                    Program.Check(now.DoneAt < old.DoneAt, "at 20 frames a second sooner as well: " + row);
                }
            }

            if (Environment.GetEnvironmentVariable("HOLDMODEL") == "1")
                Console.WriteLine(table.ToString().TrimEnd());
        }
    }
}
