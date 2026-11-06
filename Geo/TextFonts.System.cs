using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CDLC.TS.Geo
{
    /// <summary>A face of a font file as the list is made of it: the file's name (not its path), and what FontInfo read.</summary>
    public readonly struct SystemFace
    {
        public readonly string File;
        public readonly FontFaceInfo Info;

        public SystemFace(string file, FontFaceInfo info)
        {
            File = file;
            Info = info;
        }
    }

    /// <summary>
    /// The system's fonts as choices of the text light box (0.12.76, the owner: "支持不同字体切换"): the fonts of the system's fonts folders (C:\Windows\Fonts and the user's own) that this reader can draw,
    /// one for each family (its regular face), those with Chinese first. Nothing of them is copied or shipped: a text keeps the file's name (and the face of a collection) and the file is read when a text
    /// uses it. The listing of the names is quick and done at once; the reading of what each file says about itself is done on a worker, a bounded part at a time, and again when the list is rescanned.
    /// </summary>
    public static partial class TextFonts
    {
        /// <summary>The value of a system font in a text's settings: this prefix, the file's name, and "#" and the face's number for a face of a collection other than the first.</summary>
        public const string SystemPrefix = "sys:";

        /// <summary>The most system fonts the list holds, and how long one run of the scan reads files before it hands on to the next (ms).</summary>
        public const int MaxSystemChoices = 100, ScanBudgetMs = 1500;

        public static readonly string[] Extensions = { ".ttf", ".otf", ".ttc" };

        private sealed class Parsed
        {
            public long Size, Ticks;
            public List<FontFaceInfo> Faces;
        }

        private struct Source
        {
            public string Path;
            public int Face;
        }

        private static string[] s_SystemFolders = new string[0];
        private static Dictionary<string, string> s_SystemFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Parsed> s_Parsed = new Dictionary<string, Parsed>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Source> s_SystemIds = new Dictionary<int, Source>();
        private static GuideChoice[] s_SystemChoices = new GuideChoice[0];
        private static int s_ListVersion, s_Scanning;
        private static long s_LastRescan;

        /// <summary>Goes up when the list of choices changes (a file appeared or went, or the system's fonts were read).</summary>
        public static int ListVersion => Volatile.Read(ref s_ListVersion);

        /// <summary>Called, on the worker that found it, when the list of choices changed: the template makes its font field again.</summary>
        public static Action Changed;

        /// <summary>Where the bounded lines about the scan go (the mod's log); null: nowhere.</summary>
        public static Action<string> Log;

        /// <summary>The system's fonts folders to take fonts from (the names are listed now; the files are read on a worker). No folders: no system fonts.</summary>
        public static void UseSystem(IEnumerable<string> folders, bool scanNow = true)
        {
            var list = new List<string>();
            foreach (string folder in folders ?? new string[0])
            {
                if (!string.IsNullOrEmpty(folder) && !list.Contains(folder))
                    list.Add(folder);
            }

            lock (s_Lock)
            {
                s_SystemFolders = list.ToArray();
                s_SystemFiles = ListSystemFiles(s_SystemFolders);
            }

            if (scanNow)
                RescanSoon(0.0);
        }

        /// <summary>The fonts folders of the system on this machine: the shared one and the one of the user's own installs.</summary>
        public static List<string> DefaultSystemFolders()
        {
            var folders = new List<string>();
            try
            {
                string shared = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                if (!string.IsNullOrEmpty(shared))
                    folders.Add(shared);
                string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (!string.IsNullOrEmpty(local))
                    folders.Add(Path.Combine(local, "Microsoft", "Windows", "Fonts"));
            }
            catch (Exception)
            {
                // No folder to name is no folder.
            }

            return folders;
        }

        /// <summary>The font files of folders by their names (the first folder that has a name wins); a folder that is not there or cannot be read has none.</summary>
        public static Dictionary<string, string> ListSystemFiles(IEnumerable<string> folders)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders ?? new string[0])
            {
                try
                {
                    if (!Directory.Exists(folder))
                        continue;
                    foreach (string path in Directory.GetFiles(folder))
                    {
                        string name = Path.GetFileName(path);
                        if (Array.IndexOf(Extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0 && !files.ContainsKey(name))
                            files[name] = path;
                    }
                }
                catch (Exception)
                {
                    // A folder that cannot be read has no fonts for us.
                }
            }

            return files;
        }

        /// <summary>A value a text may keep for its font: "sys:" and a file that the system's folders have, and a face number that a collection could have. True for any such name at once, before the scan has read the file.</summary>
        public static bool IsKnownValue(string value) => TrySystem(value, out _);

        private static bool TrySystem(string value, out Source source)
        {
            source = default;
            if (string.IsNullOrEmpty(value) || !value.StartsWith(SystemPrefix, StringComparison.Ordinal))
                return false;
            string rest = value.Substring(SystemPrefix.Length);
            int face = 0;
            int hash = rest.LastIndexOf('#');
            if (hash >= 0)
            {
                if (!int.TryParse(rest.Substring(hash + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out face) || face > 63)
                    return false;
                rest = rest.Substring(0, hash);
            }

            if (rest.Length == 0 || rest != Path.GetFileName(rest))
                return false;
            lock (s_Lock)
            {
                if (!s_SystemFiles.TryGetValue(rest, out string path))
                    return false;
                source = new Source { Path = path, Face = face };
                return true;
            }
        }

        /// <summary>
        /// The choices that follow the default and the player's files: one for each family of the system's fonts that the reader can draw, the regular face of each, those that have Chinese first (then
        /// by name), and a font that has no Chinese says so (a text in Chinese is then set in the default font where it lacks a character: the text's own switch). At most max.
        /// </summary>
        public static List<GuideChoice> CuratedChoices(IEnumerable<SystemFace> faces, int max)
        {
            var best = new Dictionary<string, SystemFace>(StringComparer.OrdinalIgnoreCase);
            foreach (SystemFace face in faces ?? new SystemFace[0])
            {
                FontFaceInfo info = face.Info;
                if (info == null || !info.Supported || !(info.CoversCjk || info.CoversLatin) || info.DisplayEn.Length == 0 || info.DisplayEn[0] == '@')
                    continue;
                string family = info.DisplayEn;
                if (!best.TryGetValue(family, out SystemFace held) || Better(face, held))
                    best[family] = face;
            }

            var chosen = new List<SystemFace>(best.Values);
            chosen.Sort((a, b) =>
            {
                int c = b.Info.CoversCjk.CompareTo(a.Info.CoversCjk);
                return c != 0 ? c : string.Compare(a.Info.DisplayEn, b.Info.DisplayEn, StringComparison.OrdinalIgnoreCase);
            });
            var choices = new List<GuideChoice>();
            foreach (SystemFace face in chosen)
            {
                if (choices.Count >= max)
                    break;
                FontFaceInfo info = face.Info;
                string value = SystemPrefix + face.File + (info.Face > 0 ? "#" + info.Face : "");
                choices.Add(new GuideChoice(value, info.DisplayZh + (info.CoversCjk ? "" : "（无中文）"), info.DisplayEn + (info.CoversCjk ? "" : " (no CJK)")));
            }

            return choices;
        }

        /// <summary>Whether a face is the better one to stand for its family: the regular upright one, else the nearest to regular weight; between equals the earlier file.</summary>
        private static bool Better(SystemFace a, SystemFace b)
        {
            if (a.Info.IsRegular != b.Info.IsRegular)
                return a.Info.IsRegular;
            if (a.Info.Italic != b.Info.Italic)
                return !a.Info.Italic;
            int da = Math.Abs(a.Info.WeightClass - 400), db = Math.Abs(b.Info.WeightClass - 400);
            if (da != db)
                return da < db;
            return string.Compare(a.File, b.File, StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// Looks at the folders again without a restart: the player's fonts folder (a font dropped there appears) and the system's. Reads at most ScanBudgetMs of files and hands the rest on to a run of its
        /// own; tells Changed when the list differs. Runs on a worker; a run that is going on is not started twice.
        /// </summary>
        public static void Rescan()
        {
            if (Interlocked.Exchange(ref s_Scanning, 1) != 0)
                return;
            bool more = false;
            try
            {
                more = ScanOnce();
            }
            catch (Exception ex)
            {
                Log?.Invoke("Text boxes: the font scan failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
            finally
            {
                Volatile.Write(ref s_Scanning, 0);
            }

            if (more)
                Task.Run((Action)Rescan);
        }

        /// <summary>Rescans on the calling thread, waiting for a scan that is going on first, until everything is read (the tests: a scan that was started by the editor must not decide the result).</summary>
        public static void RescanAndWait()
        {
            while (Interlocked.Exchange(ref s_Scanning, 1) != 0)
                Thread.Sleep(5);
            try
            {
                while (ScanOnce())
                {
                }
            }
            finally
            {
                Volatile.Write(ref s_Scanning, 0);
            }
        }

        /// <summary>Starts a rescan on a worker unless one was started less than minSeconds ago (the editor asks every time it is made).</summary>
        public static void RescanSoon(double minSeconds)
        {
            long now = Stopwatch.GetTimestamp();
            lock (s_Lock)
            {
                if (s_LastRescan != 0 && (now - s_LastRescan) < minSeconds * Stopwatch.Frequency)
                    return;
                s_LastRescan = now;
            }

            Task.Run((Action)Rescan);
        }

        private static bool ScanOnce()
        {
            string folder;
            string[] system;
            lock (s_Lock)
            {
                folder = s_Folder;
                system = s_SystemFolders;
            }

            List<string> files = Scan(folder);
            Dictionary<string, string> names = ListSystemFiles(system);
            long start = Stopwatch.GetTimestamp();
            bool more = false;
            foreach (KeyValuePair<string, string> file in names)
            {
                string path = file.Value;
                long size = 0, ticks = 0;
                try
                {
                    var info = new FileInfo(path);
                    size = info.Length;
                    ticks = info.LastWriteTimeUtc.Ticks;
                }
                catch (Exception)
                {
                    continue;
                }

                lock (s_Lock)
                {
                    if (s_Parsed.TryGetValue(path, out Parsed known) && known.Size == size && known.Ticks == ticks)
                        continue;
                }

                if ((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency > ScanBudgetMs)
                {
                    more = true;
                    break;
                }

                List<FontFaceInfo> faces = FontInfo.Probe(path);
                lock (s_Lock)
                    s_Parsed[path] = new Parsed { Size = size, Ticks = ticks, Faces = faces };
            }

            var all = new List<SystemFace>();
            foreach (KeyValuePair<string, string> file in names)
            {
                Parsed parsed;
                lock (s_Lock)
                    s_Parsed.TryGetValue(file.Value, out parsed);
                if (parsed == null)
                    continue;
                foreach (FontFaceInfo face in parsed.Faces)
                    all.Add(new SystemFace(file.Key, face));
            }

            List<GuideChoice> curated = CuratedChoices(all, MaxSystemChoices);
            bool changed;
            lock (s_Lock)
            {
                changed = !SameChoices(s_SystemChoices, curated) || !SameList(s_Files, files);
                s_Files = files;
                s_SystemFiles = names;
                s_SystemChoices = curated.ToArray();
                if (changed)
                    Volatile.Write(ref s_ListVersion, s_ListVersion + 1);
            }

            if (changed)
            {
                lock (s_Lock)
                    Version++;
                Log?.Invoke($"Text boxes: the font list has {files.Count} file(s) of the player's and {curated.Count} of the system's.");
                Changed?.Invoke();
            }

            return more;
        }

        private static bool SameChoices(GuideChoice[] a, List<GuideChoice> b)
        {
            if (a.Length != b.Count)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Value != b[i].Value || a[i].Zh != b[i].Zh || a[i].En != b[i].En)
                    return false;
            }

            return true;
        }

        private static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        /// <summary>The system fonts as choices now (what the scan has read so far).</summary>
        internal static GuideChoice[] SystemChoices()
        {
            lock (s_Lock)
                return s_SystemChoices;
        }

        /// <summary>Remembers where a system font's id leads (IdOf: the id is made from the value, and the file is read by the id).</summary>
        private static void RememberSystem(string value, int id)
        {
            if (TrySystem(value, out Source source))
            {
                lock (s_Lock)
                    s_SystemIds[id] = source;
            }
        }

        private static bool SystemSource(int id, out Source source)
        {
            lock (s_Lock)
                return s_SystemIds.TryGetValue(id, out source);
        }
    }
}
