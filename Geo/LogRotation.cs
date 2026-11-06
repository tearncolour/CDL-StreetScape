using System;
using System.IO;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Keeps the last session's log: the game writes CDLC.TS.Mod.log afresh at every start, and a report read after a restart (2 Nov: the 0.12.74 play was gone when it was looked at) has nothing to read.
    /// Called before the logger is created, while the file is the last session's and nobody holds it.
    /// </summary>
    public static class LogRotation
    {
        public static bool KeepPrevious(string logsFolder, string name)
        {
            try
            {
                string current = Path.Combine(logsFolder, name + ".log");
                if (!File.Exists(current) || new FileInfo(current).Length == 0)
                    return false;
                File.Copy(current, Path.Combine(logsFolder, name + "-prev.log"), true);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return false;
            }
        }
    }
}
