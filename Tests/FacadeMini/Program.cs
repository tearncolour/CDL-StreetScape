using System;
using System.Collections.Generic;

namespace CDLC.TS.Tests
{
    /// <summary>The same Run, Check and Near as the whole suite's Program, for the small project that holds only the facade sampling's files.</summary>
    internal static class Program
    {
        private static int s_Checks;
        private static readonly List<string> s_Failures = new List<string>();

        private static int Main()
        {
            FacadeSamplingTests.RunAll();
            BuildingSurfaceTests.RunAll();
            Console.WriteLine($"{s_Checks} checks, {s_Failures.Count} failed");
            foreach (string failure in s_Failures)
                Console.WriteLine("  FAIL " + failure);
            return s_Failures.Count == 0 ? 0 : 1;
        }

        internal static void Run(string name, Action test)
        {
            try
            {
                test();
            }
            catch (Exception ex)
            {
                s_Failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        internal static void Check(bool ok, string message)
        {
            s_Checks++;
            if (!ok)
                s_Failures.Add(message);
        }

        internal static void Near(float actual, float expected, float tolerance, string message) =>
            Check(Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected}, got {actual}");
    }
}
