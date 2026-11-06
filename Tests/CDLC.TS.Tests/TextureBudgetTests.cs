using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The memory budget of the face pictures (0.7.0): over it the distances shrink, well under it they grow back, in between they stay; they never go below a floor or above the defaults.</summary>
    internal static class TextureBudgetTests
    {
        public static void RunAll()
        {
            Program.Run("texture budget: the scale goes down over the budget and up when well under it", Steps);
            Program.Run("texture budget: a long run settles near the budget and does not flicker", Settles);
            Program.Run("texture budget: the options are scaled where they should be", Options);
        }

        private static void Steps()
        {
            const long budget = 1000;
            Program.Check(TextureBudget.Next(1f, 1001, budget) < 1f, "over the budget: down");
            Program.Check(TextureBudget.Next(1f, 1000, budget) == 1f, "at the budget: as it is");
            Program.Check(TextureBudget.Next(1f, 700, budget) == 1f, "under the budget but not well under: as it is");
            Program.Check(TextureBudget.Next(0.5f, 700, budget) == 0.5f, "(and a scale that is down stays there in the middle)");
            Program.Check(TextureBudget.Next(0.5f, 500, budget) > 0.5f, "well under the budget: up");
            Program.Check(TextureBudget.Next(1f, 10, budget) == 1f, "never above 1");
            Program.Check(TextureBudget.Next(0.99f, 10, budget) <= 1f, "a step up does not pass 1");
            Program.Check(TextureBudget.Next(TextureBudget.MinScale, 5000, budget) == TextureBudget.MinScale, "never below the floor");
            Program.Check(TextureBudget.Next(0.26f, 5000, budget) >= TextureBudget.MinScale, "a step down does not pass the floor");
            Program.Check(TextureBudget.Next(0.6f, 0, budget) > 0.6f && TextureBudget.Next(0.6f, 599, budget) > 0.6f && TextureBudget.Next(0.6f, 600, budget) == 0.6f, "the edge of 'well under' is 60 per cent of the budget");
        }

        private static void Settles()
        {
            // A city whose pictures take 4 MB for each per cent of the scale (a model): the budget is 384 MB, so the scale that fits is about 0.96... use a heavier one: 12 MB for each per cent.
            const long budget = TextureBudget.DefaultBytes;
            float scale = 1f;
            int changes = 0;
            long bytes = 0;
            float last = scale;
            for (int second = 0; second < 300; second++)
            {
                bytes = (long)(scale * 100 * 12L * 1024 * 1024);
                float next = TextureBudget.Next(scale, bytes, budget);
                if (Math.Abs(next - scale) > 1e-6f)
                    changes++;
                scale = next;
                if (second == 200)
                    last = scale;
            }

            long finalBytes = (long)(scale * 100 * 12L * 1024 * 1024);
            Program.Check(finalBytes <= budget * 1.2 && finalBytes >= budget * 0.4, $"the pictures end near the budget: {finalBytes / 1048576} MB of {budget / 1048576} MB at scale {scale:0.00}");
            Program.Check(Math.Abs(scale - last) < 0.2f, "after the first minutes it has settled: " + last + " / " + scale);

            // Over the budget at the start: it comes down in a few steps, and when the city thins out it comes back.
            scale = 1f;
            int steps = 0;
            while (TextureBudget.Next(scale, (long)(budget * 3), budget) < scale && steps < 100)
            {
                scale = TextureBudget.Next(scale, (long)(budget * 3), budget);
                steps++;
            }

            Program.Check(scale == TextureBudget.MinScale && steps < 20, $"three times the budget: down to the floor in {steps} step(s)");
            int up = 0;
            while (scale < 1f && up < 200)
            {
                scale = TextureBudget.Next(scale, 0, budget);
                up++;
            }

            Program.Check(scale == 1f && up < 40, $"an empty place: back to 1 in {up} step(s)");
            Program.Check(changes < 100, "the scale did not change every second for ever: " + changes);
        }

        private static void Options()
        {
            StreamOptions o = StreamOptions.Default;
            StreamOptions half = TextureBudget.Scaled(o, 0.5f);
            Program.Near(half.NearMetres, o.NearMetres * 0.5f, 1e-4f, "near is halved");
            Program.Near(half.MidMetres, o.MidMetres * 0.5f, 1e-4f, "middle is halved");
            Program.Check(half.FarMetres == o.FarMetres && half.Hysteresis == o.Hysteresis, "far and the hysteresis are not");
            StreamOptions floor = TextureBudget.Scaled(o, TextureBudget.MinScale);
            Program.Check(floor.NearMetres > 0f && floor.NearMetres < floor.MidMetres && floor.MidMetres < floor.FarMetres, "at the floor the tiers are still ordered: " + floor.NearMetres + " / " + floor.MidMetres + " / " + floor.FarMetres);
        }
    }
}
