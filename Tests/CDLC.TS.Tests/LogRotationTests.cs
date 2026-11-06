using System.IO;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    internal static class LogRotationTests
    {
        public static void RunAll() => Program.Run("log rotation: the last session's log is kept beside the new one", Keep);

        private static void Keep()
        {
            string dir = Path.Combine(System.AppContext.BaseDirectory, "tmp", "ts-logrot-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Program.Check(!LogRotation.KeepPrevious(dir, "T.Mod"), "no log yet: nothing to keep");
                File.WriteAllText(Path.Combine(dir, "T.Mod.log"), "");
                Program.Check(!LogRotation.KeepPrevious(dir, "T.Mod") && !File.Exists(Path.Combine(dir, "T.Mod-prev.log")), "an empty log is not kept");
                File.WriteAllText(Path.Combine(dir, "T.Mod.log"), "first session");
                Program.Check(LogRotation.KeepPrevious(dir, "T.Mod") && File.ReadAllText(Path.Combine(dir, "T.Mod-prev.log")) == "first session", "the log is copied to -prev");
                File.WriteAllText(Path.Combine(dir, "T.Mod.log"), "second session");
                Program.Check(LogRotation.KeepPrevious(dir, "T.Mod") && File.ReadAllText(Path.Combine(dir, "T.Mod-prev.log")) == "second session" && File.ReadAllText(Path.Combine(dir, "T.Mod.log")) == "second session", "the next start replaces -prev and leaves the log as it is");
                Program.Check(!LogRotation.KeepPrevious(Path.Combine(dir, "missing"), "T.Mod") && !LogRotation.KeepPrevious("bad\0path", "T.Mod"), "a missing or bad folder is no error");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
