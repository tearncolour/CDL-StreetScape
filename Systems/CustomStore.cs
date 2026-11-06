using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Render;

namespace CDLC.TS
{
    /// <summary>
    /// The files of the player's own signs (0.12.29): the PNG files the player puts in ModsData/CDLC.TS/Custom are what the panel offers to import; an imported picture is kept as a copy in its images
    /// folder (named by the key of its bytes: Signs.CustomSigns.KeyOf), and the saved signs are the lines of signs.txt. Main thread.
    /// </summary>
    internal static class CustomStore
    {
        /// <summary>The largest file and picture that is imported (bytes, pixels a side): a larger one is refused before it is decoded.</summary>
        private const long kMaxBytes = 32L * 1024 * 1024;
        private const int kMaxSide = 8192;

        internal static string Folder { get; private set; }

        private static string Images => Path.Combine(Folder, "images");
        private static string SignsFile => Path.Combine(Folder, "signs.txt");

        /// <summary>A PNG file of the folder as the panel lists it.</summary>
        internal struct FileEntry
        {
            public string Name;
            public int Width, Height;
            public long Bytes;
        }

        /// <summary>Makes the folders (with a note of what goes there), points the painter at the pictures and reads the saved signs. Once, when the mod is loaded.</summary>
        internal static void Begin(string persistent)
        {
            Folder = Path.Combine(persistent, "ModsData", Mod.kModName, "Custom");
            try
            {
                Directory.CreateDirectory(Images);
                string note = Path.Combine(Folder, "README.txt");
                if (!File.Exists(note))
                {
                    File.WriteAllText(note,
                        "CDLC.TS custom signs / 自定义路牌\r\n\r\n"
                        + "Put PNG pictures here, then open the 自定义 (Custom) tab of the panel, press the + tile and import one.\r\n"
                        + "The pictures you imported are kept in images/, the signs you saved in signs.txt.\r\n\r\n"
                        + "把 PNG 图片放进这个文件夹，然后在面板的“自定义”标签页点“+”导入。\r\n"
                        + "导入后的图片保存在 images 文件夹，保存的路牌记录在 signs.txt。\r\n", System.Text.Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Custom signs: the folder " + Folder + " could not be made: " + ex.Message);
            }

            CustomPictures.Folder = Images;
            CustomPictures.Warn = Mod.log.Warn;
            CustomPictures.Info = Mod.log.Info;
            PictureCutShadow.Log = Mod.log.Info;
            PictureCutUse.Log = Mod.log.Info;
            s_MainThread = Environment.CurrentManagedThreadId;
            UpgradePictures();
            CustomSigns.Masks = MaskOf;
            CustomSigns.Contours = ContourOf;
            try
            {
                CustomSigns.Load(File.Exists(SignsFile) ? File.ReadAllText(SignsFile, System.Text.Encoding.UTF8) : "");
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Custom signs: " + SignsFile + " could not be read: " + ex.Message);
            }

            Mod.log.Info($"Custom signs: {CustomSigns.Saved.Count} saved; pictures to import go in {Folder}.");
        }

        private static readonly Dictionary<string, CustomMask> s_Masks = new Dictionary<string, CustomMask>();

        /// <summary>The mask of a kept picture (CustomSigns.Masks), read from its file the first time and kept; null for a picture with no clear pixel or none at all. Any thread.</summary>
        private static CustomMask MaskOf(string key)
        {
            lock (s_Masks)
            {
                if (s_Masks.TryGetValue(key, out CustomMask known))
                    return known;
                CustomMask mask = null;
                try
                {
                    string path = CustomPictures.MaskPathOf(key);
                    if (path != null && File.Exists(path))
                        mask = CustomMask.Decode(File.ReadAllBytes(path));
                }
                catch (Exception ex)
                {
                    Mod.log.Warn($"Custom signs: the mask of {key} could not be read: {ex.Message}");
                }

                s_Masks[key] = mask;
                return mask;
            }
        }

        private static readonly Dictionary<string, PictureContour> s_Contours = new Dictionary<string, PictureContour>();
        private static readonly HashSet<string> s_ContourAsked = new HashSet<string>();
        private static readonly object s_ContourLoad = new object();
        private static int s_MainThread, s_ContourLogs;

        /// <summary>
        /// The contour of a kept picture (CustomSigns.Contours, 0.12.74): from memory when it is there; else, off the main thread, read from the picture's TSM2 file or traced from the kept picture and kept; on the main
        /// thread nothing is read (a decode and a trace of a large picture is milliseconds): a worker is asked for it and null is returned, and the board is cut as 0.12.72 did. The pictures are read at the start
        /// (WarmContours), before a city is, so that this is rare. Any thread.
        /// </summary>
        private static PictureContour ContourOf(string key)
        {
            lock (s_Contours)
            {
                if (s_Contours.TryGetValue(key, out PictureContour known))
                    return known;
                if (Environment.CurrentManagedThreadId == s_MainThread)
                {
                    if (s_ContourAsked.Add(key))
                        System.Threading.Tasks.Task.Run(() => LoadContour(key));
                    return null;
                }
            }

            return LoadContour(key);
        }

        private static PictureContour LoadContour(string key)
        {
            lock (s_ContourLoad)
            {
                lock (s_Contours)
                {
                    if (s_Contours.TryGetValue(key, out PictureContour known))
                        return known;
                }

                PictureContour contour = null;
                string source;
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    contour = PictureContour.Load(CustomPictures.ContourPathOf(key), () => CustomPictures.ReadAlpha(key), out source);
                }
                catch (Exception ex)
                {
                    source = "failed: " + ex.GetType().Name + ": " + ex.Message;
                }

                lock (s_Contours)
                    s_Contours[key] = contour;
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (s_ContourLogs++ < 24)
                    Mod.log.Info($"Picture cut: the contour of {key}: {source}{(contour != null ? $", {contour.Loops.Count} loop(s), {contour.Points} points" : "")}, {ms:0} ms.");
                return contour;
            }
        }

        /// <summary>Reads the contours of the kept pictures that have clear pixels, on a worker, once, when the mod is loaded (not when features.pictureCut is off).</summary>
        internal static void WarmContours()
        {
            if (CustomSigns.PictureCutMode == FeatureState.Off || Folder == null)
                return;
            string images = Images;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    int done = 0;
                    foreach (string path in Directory.GetFiles(images, "*.mask"))
                    {
                        string key = Path.GetFileNameWithoutExtension(path);
                        if (CustomSigns.IsKey(key) && new FileInfo(path).Length > 0 && LoadContour(key) != null)
                            done++;
                    }

                    Mod.log.Info($"Picture cut: {Preferences.StateName(CustomSigns.PictureCutMode)}; the contours of {done} kept picture(s) are ready.");
                }
                catch (Exception ex)
                {
                    Mod.log.Warn("Picture cut: the contours could not be read: " + ex.Message);
                }
            });
        }

        /// <summary>The pictures kept by 0.12.29 (no mask beside them) done again as an import does it now (0.12.30: their alpha whole or nothing, their mask). Once, when the mod is loaded.</summary>
        private static void UpgradePictures()
        {
            try
            {
                int done = 0;
                foreach (string path in Directory.GetFiles(Images, "*.png"))
                {
                    string key = Path.GetFileNameWithoutExtension(path);
                    if (!CustomSigns.IsKey(key) || File.Exists(CustomPictures.MaskPathOf(key)))
                        continue;
                    if (CustomPictures.Upgrade(key, out string why))
                        done++;
                    else
                        Mod.log.Warn($"Custom signs: the picture {key} could not be done again: {why}");
                }

                if (done > 0)
                    Mod.log.Info($"Custom signs: {done} picture(s) of 0.12.29 done again (their alpha whole or nothing, a mask for the clear pixels).");
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Custom signs: the kept pictures could not be looked at: " + ex.Message);
            }
        }

        /// <summary>The PNG files the player put in the folder (not the kept copies), by name; their sizes are read from their headers only.</summary>
        internal static List<FileEntry> Files()
        {
            var list = new List<FileEntry>();
            if (Folder == null)
                return list;
            try
            {
                foreach (string path in Directory.GetFiles(Folder, "*.png"))
                {
                    var info = new FileInfo(path);
                    var head = new byte[24];
                    int read;
                    using (FileStream stream = info.OpenRead())
                        read = stream.Read(head, 0, head.Length);
                    Png.TrySize(read == head.Length ? head : null, out int w, out int h);
                    list.Add(new FileEntry { Name = info.Name, Width = w, Height = h, Bytes = info.Length });
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Custom signs: the folder could not be listed: " + ex.Message);
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>
        /// Imports a file of the folder (its name only: a path is refused): its picture is kept, and the settings of a new sign of it are returned. Null with the reason (for the panel) when it is not
        /// a PNG the painter can read, is too large, or is not there.
        /// </summary>
        internal static string Import(string name, out string why) =>
            ImportPicture(name, out string key, out int w, out int h, out why) ? CustomSigns.SpecForPicture(key, w, h, name) : null;

        /// <summary>As Import, for any board that takes a picture (0.12.49, a lamp-post ad): the kept picture's key and size.</summary>
        internal static bool ImportPicture(string name, out string key, out int kw, out int kh, out string why)
        {
            key = "";
            kw = kh = 0;
            why = "";
            try
            {
                if (Folder == null || string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    why = "not-png";
                    return false;
                }

                var info = new FileInfo(Path.Combine(Folder, name));
                if (!info.Exists)
                {
                    why = "gone";
                    return false;
                }

                if (info.Length > kMaxBytes)
                {
                    why = "too-large";
                    return false;
                }

                byte[] data = File.ReadAllBytes(info.FullName);
                if (!Png.TrySize(data, out int w, out int h))
                {
                    why = "not-png";
                    return false;
                }

                if (w > kMaxSide || h > kMaxSide)
                {
                    why = "too-large";
                    return false;
                }

                key = CustomSigns.KeyOf(data);
                lock (s_Masks)
                    s_Masks.Remove(key);
                lock (s_Contours)
                {
                    s_Contours.Remove(key);
                    s_ContourAsked.Remove(key);
                }

                if (!CustomPictures.Keep(data, key, out kw, out kh, out string failure))
                {
                    Mod.log.Warn($"Custom signs: {name} could not be imported: {failure}");
                    why = "unreadable";
                    return false;
                }

                // The contour the import traced is used as it is: a board made at once from the new picture is cut from it, not by a worker's trace of the kept copy a moment later (the import ran on this thread).
                if (CustomPictures.LastContour != null && CustomPictures.LastContourKey == key)
                {
                    lock (s_Contours)
                        s_Contours[key] = CustomPictures.LastContour;
                }

                Mod.log.Info($"Custom signs: {name} ({w} x {h} px, {data.Length / 1024} KB) imported as {key} ({kw} x {kh} px).");
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"Custom signs: {name} could not be imported: {ex.GetType().Name}: {ex.Message}");
                why = "unreadable";
                return false;
            }
        }

        /// <summary>Writes the saved signs (a new file, then put in place of the old one); false when it could not be written.</summary>
        internal static bool Save()
        {
            try
            {
                string temporary = SignsFile + ".tmp";
                File.WriteAllText(temporary, CustomSigns.Text(), System.Text.Encoding.UTF8);
                if (File.Exists(SignsFile))
                    File.Delete(SignsFile);
                File.Move(temporary, SignsFile);
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Custom signs: " + SignsFile + " could not be written: " + ex.Message);
                return false;
            }
        }
    }
}
