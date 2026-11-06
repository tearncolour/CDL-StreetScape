using System;
using System.Collections.Generic;
using System.Text;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Random texts for the tests and the bench of the text light box's planners (PlannerPerfTests, BENCH_PLANNER=1): one to three lines of up to twelve characters drawn from a pool of Chinese
    /// characters of every shape the frame planner meets (a 口 with a hole, a 小 of three strokes, a 一 that is a bar, marks that no rail crosses), letters and figures, and now and then a character the
    /// bundled font has no glyph for. Deterministic for a seed. Pure.
    /// </summary>
    internal static class TextFixture
    {
        private const string Chinese = "永远发光字体灯箱路牌停公交铁路出租车校快速多乘员人慢学口小回国日目田三十中华人民共和国交通标志专用字体一二三上下大天水火山川木林森点。、，·丶亻氵忄冫宀艹";
        private const string Latin = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-./";

        /// <summary>Characters the bundled font probably has no outline for (a snowman, a Thai letter, a Greek one): the text keeps their place as a blank.</summary>
        private const string Absent = "☃กὠ";

        public static List<string> RandomTexts(int count, int seed)
        {
            var random = new Random(seed);
            var texts = new List<string>(count);
            for (int t = 0; t < count; t++)
            {
                int lines = 1 + random.Next(3);
                var sb = new StringBuilder();
                for (int l = 0; l < lines; l++)
                {
                    if (l > 0)
                        sb.Append('\n');
                    int length = 1 + random.Next(12);
                    bool latin = random.Next(5) == 0;
                    for (int i = 0; i < length; i++)
                    {
                        int pick = random.Next(40);
                        if (pick == 0)
                            sb.Append(Absent[random.Next(Absent.Length)]);
                        else if (latin || pick < 6)
                            sb.Append(Latin[random.Next(Latin.Length)]);
                        else
                            sb.Append(Chinese[random.Next(Chinese.Length)]);
                    }
                }

                texts.Add(sb.ToString());
            }

            return texts;
        }
    }
}
