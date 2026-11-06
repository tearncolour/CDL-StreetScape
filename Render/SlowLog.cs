using System.Collections.Generic;
using CDLC.TS.Geo;
using UnityEngine;
using UnityEngine.Profiling;

namespace CDLC.TS.Render
{
    /// <summary>
    /// Says which part of this mod makes a frame slow: every update of one of its systems is timed, one that takes 8 ms or more is written to the log with what it was doing (the first few of a system and
    /// every record, SlowPolicy), and every three minutes a summary says how many calls each system had, its average and its worst time (when it was and what it was doing), how many were over 8 ms, and how much memory the mod holds (the
    /// pictures of the faces, the managed heap). Added in 0.6.0 after the owner reported a stutter every few seconds that the log could neither confirm nor rule out. Main thread.
    /// </summary>
    internal static class SlowLog
    {
        private const double kSlowMs = 8.0;
        private const float kSummarySeconds = 180f;

        private sealed class Stat
        {
            public readonly PerfStats Window = new PerfStats(), Session = new PerfStats();
            public string WorstAt, WorstDetail;
            public double WindowWorst;
        }

        private static readonly Dictionary<string, Stat> s_Stats = new Dictionary<string, Stat>();
        private static readonly SlowPolicy s_Policy = new SlowPolicy();
        private static float s_NextSummary = -1f, s_WindowStart = -1f;

        /// <summary>A stage that began at Stopwatch.GetTimestamp() = start took until now: Add with the milliseconds. For the stages inside a system's update (0.7.1).</summary>
        internal static void Since(string stage, long start, string detail = null) =>
            Add(stage, (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency, detail);

        /// <summary>Whether an update of this length is written to the log: callers build the detail text only then (an update that is not slow must not allocate a string to say so every frame).</summary>
        internal static bool IsSlow(double ms) => ms >= kSlowMs;

        /// <summary>One update of a system took ms; detail says what it did (a rebuild of two tiles, a reading), and is only written when the update was slow.</summary>
        internal static void Add(string system, double ms, string detail = null)
        {
            if (!s_Stats.TryGetValue(system, out Stat stat))
                s_Stats[system] = stat = new Stat();
            stat.Window.Add(ms);
            if (ms > stat.WindowWorst)
            {
                stat.WindowWorst = ms;
                if (ms >= kSlowMs)
                {
                    stat.WorstAt = System.DateTime.Now.ToString("HH:mm:ss");
                    stat.WorstDetail = detail;
                }
            }

            if (ms < kSlowMs)
                return;
            SlowWrite write = s_Policy.Judge(system, ms);
            if (write != SlowWrite.No)
                Mod.log.Info($"Slow: {system} took {ms:0.0} ms{(write == SlowWrite.Record ? " (a record)" : "")}{(string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")")}");
        }

        /// <summary>Writes the summary when three minutes have passed since the last one (and the first time, three minutes after the first call); call it every frame from one system.</summary>
        internal static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (s_NextSummary < 0f)
            {
                s_NextSummary = now + kSummarySeconds;
                s_WindowStart = now;
                return;
            }

            if (now < s_NextSummary)
                return;
            double seconds = now - s_WindowStart;
            s_NextSummary = now + kSummarySeconds;
            s_WindowStart = now;
            var lines = new List<string> { $"Perf (last {seconds:0} s; load = ms of the main thread a second, p50/p95/p99 = at most this many ms, * = more than 1 % of the calls took 1 ms or more):" };
            foreach (KeyValuePair<string, Stat> pair in s_Stats)
            {
                Stat s = pair.Value;
                if (s.Window.Calls > 0)
                {
                    lines.Add("  " + s.Window.Line(pair.Key, seconds, WorstText(s)));
                    s.Session.Merge(s.Window);
                    if (s.Session.Calls > s.Window.Calls)
                        lines.Add($"    since the start: {s.Session.Calls} call(s), p95 <= {s.Session.PercentileUpperMs(0.95):0.##}, p99 <= {s.Session.PercentileUpperMs(0.99):0.##}, worst {s.Session.MaxMs:0.0} ms, over 1 ms {s.Session.OverOneMs} ({s.Session.OverOnePercent:0.0} %), over 8 ms {s.Session.OverEightMs}");
                }

                s.WorstAt = null;
                s.WorstDetail = null;
                s.WindowWorst = 0.0;
                s.Window.Reset();
            }

            lines.Add("  " + Memory());
            lines.Add("  " + MeshUpload.MaskCensus());
            Mod.log.Info(string.Join("\n", lines));
        }

        /// <summary>When the window's worst call was and what it was doing, for a call that was slow (the detail is built only then); nothing for the others.</summary>
        private static string WorstText(Stat s) => s.WorstAt == null ? "" : $" at {s.WorstAt}{(string.IsNullOrEmpty(s.WorstDetail) ? "" : " (" + s.WorstDetail + ")")}";

        /// <summary>
        /// What the mod holds, for the log and the panel: the face pictures (their number and an estimate of their size on the graphics card); then, apart, the whole game's managed heap and what Unity has
        /// allocated for the whole game (every mod's and the game's own: no part of them can be told to be this mod's; 0.12.3 put them in the same line and the owner took them for the mod's).
        /// </summary>
        internal static string Memory()
        {
            string bc1 = FacePainter.Bc1Mode > 0 ? "BC1" : FacePainter.Bc1Mode < 0 ? "RGBA32 (BC1 off)" : "BC1 being checked";
            string encoded = FacePainter.Encoded > 0 ? $", {FacePainter.Encoded} encoded in {FacePainter.EncodeMs / FacePainter.Encoded:0.0} ms each on the second worker{(FacePainter.EncodeError.Length > 0 ? " (last error: " + FacePainter.EncodeError + ")" : "")}" : "";
            return $"this mod's face pictures: {TsMaterials.FaceTextureCount}, about {TsMaterials.FaceTextureBytes / 1048576f:0.0} MB ({bc1}{encoded}). The whole game (all mods together): managed heap {Profiler.GetMonoUsedSizeLong() / 1048576f:0} MB of {Profiler.GetMonoHeapSizeLong() / 1048576f:0} MB, Unity allocated {Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0} MB";
        }
    }
}
