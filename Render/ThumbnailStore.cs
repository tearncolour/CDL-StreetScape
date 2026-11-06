using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using UnityEngine;

namespace CDLC.TS.Render
{
    /// <summary>
    /// Makes the thumbnails of the panel once, in the background, and tells the UI where they are. A thumbnail is a PNG file in the mod's data folder
    /// (ModsData/CDLC.TS/Thumbs) whose name carries a stamp of the art (Thumbnails.Revision), so a file is made only when it is not there yet; files of an older art
    /// are deleted. The folder is registered with the game's UI as a host (the way BusDriver does it for the wraps), and the UI asks for coui://cdlcts/&lt;file&gt;.
    /// The painting (GDI+) and the rendering (software) need no Unity objects, so they run on a worker thread; only the folder path and the host registration are
    /// done on the main thread, in Begin.
    /// </summary>
    internal static class ThumbnailStore
    {
        internal const string Host = "cdlcts";
        internal const string IconHost = "cdlcts-icons";
        internal const string TabIconUrl = "coui://" + IconHost + "/" + KerbTab.IconFile;

        private static readonly object s_Lock = new object();
        private static readonly Dictionary<string, string> s_Urls = new Dictionary<string, string>();
        private static bool s_Started;
        private static bool s_HostAdded;
        private static volatile bool s_Ready;
        private static int s_PreviewCounter;
        private static string s_LastPreview;

        /// <summary>True once every thumbnail that could be made has been made (some may be missing; see the log).</summary>
        internal static bool Ready => s_Ready;

        internal static string Folder { get; private set; }

        internal static string SignUrl(string id) => Url("s:" + id);

        internal static string SupportUrl(string id) => Url("p:" + id);

        /// <summary>The picture of the tile that adds a sign of the player's own (0.12.29); "" until the thumbnails are made.</summary>
        internal static string AddTileUrl => Url("add");

        /// <summary>The icon of a kerb style's road upgrade (0.12.33): its address is known before the file is made, as the upgrade is given it when it is made.</summary>
        internal static string KerbUrl(KerbStyle style) => $"coui://{Host}/{Thumbnails.KerbFile(style)}";

        /// <summary>
        /// The id a sign's thumbnail is named by: its catalogue id, and for a sign the player saved the hash of its settings too (a sign saved again with other settings is another picture, 0.12.29), and
        /// whether its picture has clear pixels (0.12.30: the pictures of 0.12.29 were done again, their alpha whole or nothing).
        /// </summary>
        private static string ThumbId(SignChoice choice)
        {
            if (choice.Group != CustomSigns.Group || choice.Id == TextBoxes.TemplateId || choice.Id == StandBoxes.TemplateId)
                return choice.Id;
            bool cut = CustomSigns.Masks?.Invoke(CustomSigns.Template.Parse(choice.Spec ?? "").Get("img")) != null;
            return choice.Id + "." + SignCatalog.HashOf((choice.Spec ?? "") + (cut ? "|cut" : "")).ToString("x8");
        }

        /// <summary>
        /// The thumbnail of one sign, made now (a sign the player just saved, 0.12.29): main thread, a few milliseconds. Its address is the catalogue's from then on; false when it could not be made.
        /// </summary>
        internal static bool MakeNow(SignChoice choice)
        {
            if (Folder == null || choice == null)
                return false;
            try
            {
                string file = Thumbnails.FileName("s", ThumbId(choice));
                string path = Path.Combine(Folder, file);
                if (!File.Exists(path))
                {
                    SignInstance instance = SignInstances.Of(choice.Def, choice.Spec);
                    byte[] data = LedScreens.PaintFace(instance, out int w, out int h);
                    File.WriteAllBytes(path, Thumbnails.SignPng(instance, data, w, h));
                }

                lock (s_Lock)
                    s_Urls["s:" + choice.Id] = $"coui://{Host}/{file}";
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Thumbnails: could not make the one of {choice.Id}: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static string Url(string key)
        {
            lock (s_Lock)
                return s_Urls.TryGetValue(key, out string url) ? url : "";
        }

        /// <summary>Main thread. Safe to call again: only the first call does anything. The fonts are SignFonts', chosen when the mod was loaded.</summary>
        internal static void Begin()
        {
            if (s_Started)
                return;
            s_Started = true;
            // The dots of the LED screens come from the painter's picture, which thumbnails need as much as the renderer does (they may be made before the first tile is built).
            SignBuilder.LedGrids = LedScreens.Grid;

            try
            {
                Folder = Path.Combine(Application.persistentDataPath, "ModsData", "CDLC.TS", "Thumbs");
                Directory.CreateDirectory(Folder);
                EnsureHost();
            }
            catch (Exception ex)
            {
                Mod.log.Error("Thumbnails: could not prepare the folder: " + ex);
                s_Ready = true;
                return;
            }

            // Pictures of a board in the editor that an earlier run left behind.
            try
            {
                foreach (string old in Directory.GetFiles(Folder, "edit-*.png"))
                    File.Delete(old);
            }
            catch (Exception)
            {
            }

            string folder = Folder;
            Task.Run(() => Generate(folder));
        }

        /// <summary>
        /// Registers the folder with the game's UI as the host of the thumbnails. The UI system may not exist yet when the mod's systems are created, so this is tried
        /// again every frame (TsUiSystem.OnUpdate) until it has worked. Main thread.
        /// </summary>
        internal static void EnsureHost()
        {
            if (s_HostAdded || Folder == null || Colossal.UI.UIManager.defaultUISystem == null)
                return;
            try
            {
                Colossal.UI.UIManager.defaultUISystem.AddHostLocation(Host, Folder, false);
                // Packaged tab art is independent of the generated asset-thumbnail cache.
                Colossal.UI.UIManager.defaultUISystem.AddHostLocation(IconHost, Path.Combine(Mod.Directory, "Icons"), false);
                s_HostAdded = true;
                Mod.log.Info("Thumbnails: registered UI hosts '" + Host + "' and '" + IconHost + "'.");
            }
            catch (Exception ex)
            {
                s_HostAdded = true;
                Mod.log.Error("Thumbnails: could not register the UI host: " + ex);
            }
        }

        /// <summary>A board asked for by the editor: its kind, its settings as written, and the key the editor knows the request by.</summary>
        private struct EditorRequest
        {
            public SignDef Def;
            public string Spec, Key;
        }

        /// <summary>A picture of a board made for the editor: the key of the request, its file in the thumbnail folder and its size.</summary>
        private struct EditorPicture
        {
            public string Key, File;
            public int Width, Height;
        }

        private static readonly LatestWorker<EditorRequest, EditorPicture> s_EditorWorker = new LatestWorker<EditorRequest, EditorPicture>(MakeEditorPicture,
            ex => Mod.log.Error("Thumbnails: could not make the editor's picture: " + ex.GetType().Name + ": " + ex.Message));

        /// <summary>
        /// A picture of a board as it is now is asked for, for the editor of its settings (0.12.63: on a worker; it was made on the main thread, 11 ms on the median board and up to 100 ms, at every
        /// change of a setting; 0.12.64: the board's layout too, 8 to 85 ms): the board laid out (SignInstances.Of, kept for the editor to read), its face as painted (up to 768 px), written to a file of
        /// its own in the folder the UI reads pictures from, under a new name each time (the UI would show the old one for the same address). A request overtaken by a newer one before it starts is not
        /// made. TryTakeEditorPreview has it; key names the request.
        /// </summary>
        internal static void RequestEditorPreview(SignDef def, string spec, string key)
        {
            if (Folder != null)
                s_EditorWorker.Submit(new EditorRequest { Def = def, Spec = spec, Key = key });
        }

        /// <summary>
        /// The picture made last, if it is the one of the board wanted (its SignInstance.Key): the file it replaces is deleted (the UI has moved on to the new address in the same update), its
        /// address and size are given. A picture of another board (the settings changed again while it was made) is deleted unseen. Main thread.
        /// </summary>
        internal static bool TryTakeEditorPreview(string wantedKey, out string url, out int width, out int height)
        {
            url = "";
            width = height = 0;
            if (!s_EditorWorker.TryTake(out EditorPicture picture))
                return false;
            if (picture.Key != wantedKey)
            {
                DeleteQuietly(picture.File);
                return false;
            }

            DeleteQuietly(s_LastPreview);
            s_LastPreview = picture.File;
            url = $"coui://{Host}/{picture.File}";
            width = picture.Width;
            height = picture.Height;
            return true;
        }

        private static EditorPicture MakeEditorPicture(EditorRequest request)
        {
            SignInstance instance = SignInstances.Of(request.Def, request.Spec);
            // A text light box has no picture of its own (its letters are solids): the editor shows them lit on a dark ground, from the plan (made here, on the worker, if it is not yet).
            Geo.Signs.TextBoard text = instance.Board?.Text;
            if (text != null && text.Layout != null)
            {
                byte[] lit = TextFacePreview.Raster(text, TextPlans.Get(text.Job), Raster.SolidRgb(text.Glow), out int tw, out int th);
                if (lit != null)
                {
                    string textFile = $"edit-{System.Threading.Interlocked.Increment(ref s_PreviewCounter)}.png";
                    File.WriteAllBytes(Path.Combine(Folder, textFile), Png.Encode(lit, tw, th));
                    return new EditorPicture { Key = request.Key, File = textFile, Width = tw, Height = th };
                }
            }

            byte[] raw = LedScreens.PaintFace(instance, out int w, out int h);
            // A board cut to a player's picture is seen in the editor as it will stand: clear where it is cut.
            LegendPainter.CutAway(instance, raw, w, h);
            string file = $"edit-{System.Threading.Interlocked.Increment(ref s_PreviewCounter)}.png";
            File.WriteAllBytes(Path.Combine(Folder, file), Png.Encode(raw, w, h));
            return new EditorPicture { Key = request.Key, File = file, Width = instance.TexW, Height = instance.TexH };
        }

        private static void DeleteQuietly(string file)
        {
            if (string.IsNullOrEmpty(file))
                return;
            try { File.Delete(Path.Combine(Folder, file)); }
            catch (Exception) { }
        }

        /// <summary>
        /// The picture of a road marking as it is now, seen from above on the road, for the editor (up to 256 px on its long side); the width and height of the picture are given. As EditorPreview, a file with a
        /// new name each time. Main thread; the plan's paint is made already (the words are made from the font, which is read if it has not been).
        /// </summary>
        internal static string MarkingPreview(MarkingPlan plan, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (Folder == null || plan == null)
                return "";
            try
            {
                byte[] raw = MarkingRaster.Render(plan, 256, out width, out height);
                return WriteEditorPicture(raw, width, height, false);
            }
            catch (Exception ex)
            {
                Mod.log.Error("Thumbnails: could not make the editor's picture of a marking: " + ex.GetType().Name + ": " + ex.Message);
                return "";
            }
        }

        /// <summary>Writes the picture as the next editor file; bottomFirst is whether its first row is its bottom (the face painter's is; a marking's picture starts at the top).</summary>
        private static string WriteEditorPicture(byte[] raw, int w, int h, bool bottomFirst = true)
        {
            string file = $"edit-{System.Threading.Interlocked.Increment(ref s_PreviewCounter)}.png";
            File.WriteAllBytes(Path.Combine(Folder, file), Png.Encode(raw, w, h, bottomFirst));
            DeleteQuietly(s_LastPreview);
            s_LastPreview = file;
            return $"coui://{Host}/{file}";
        }

        private static void Generate(string folder)
        {
            var clock = Stopwatch.StartNew();
            int made = 0, reused = 0, failed = 0;
            var used = new HashSet<string>();
            var urls = new Dictionary<string, string>();
            var pictures = new Dictionary<string, (byte[] data, int w, int h)>();

            (byte[] data, int w, int h) PictureOf(SignInstance instance)
            {
                if (!pictures.TryGetValue(instance.Key, out var picture))
                {
                    byte[] data = LedScreens.PaintFace(instance, out int w, out int h);
                    pictures[instance.Key] = picture = (data, w, h);
                }

                return picture;
            }

            void Make(string key, string kind, string id, Func<byte[]> png)
            {
                string file = Thumbnails.FileName(kind, id);
                used.Add(file);
                string path = Path.Combine(folder, file);
                try
                {
                    if (File.Exists(path))
                    {
                        reused++;
                    }
                    else
                    {
                        File.WriteAllBytes(path, png());
                        made++;
                    }

                    urls[key] = $"coui://{Host}/{file}";
                }
                catch (Exception ex)
                {
                    failed++;
                    Mod.log.Error($"Thumbnails: could not make {file}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            try
            {
                // The words of the markings come from the fonts; they are read for these pictures and let go of at the end.
                foreach (SignChoice choice in SignCatalog.Choices)
                {
                    SignChoice entry = choice;
                    if (entry.Hidden)
                        continue;
                    if (entry.Def.IsMarking)
                    {
                        // A marking's picture is the paint seen from above on the road, not a board's face.
                        Make("s:" + entry.Id, "m", entry.Id + ".r" + MarkingRaster.Revision, () => MarkingRaster.Png(MarkingTemplates.PlanOf(entry.Def, entry.Spec), Thumbnails.Size));
                        continue;
                    }

                    Make("s:" + entry.Id, "s", ThumbId(entry), () =>
                    {
                        SignInstance instance = SignInstances.Of(entry.Def, entry.Spec);
                        var picture = PictureOf(instance);
                        return Thumbnails.SignPng(instance, picture.data, picture.w, picture.h);
                    });
                }

                foreach (SupportDef def in SupportCatalog.Offerable)
                {
                    SupportDef support = def;
                    Make("p:" + support.Id, "p", support.Id, () =>
                    {
                        var picture = PictureOf(Thumbnails.CarrierFor(support));
                        return Thumbnails.SupportPng(support, picture.data, picture.w, picture.h);
                    });
                }

                foreach (KerbStyle style in Kerbs.Styles)
                {
                    KerbStyle kerb = style;
                    Make("k:" + (int)kerb, "k", Thumbnails.KerbId(kerb), () => Thumbnails.KerbPng(kerb));
                }

                string add = Thumbnails.AddTileFile;
                used.Add(add);
                if (!File.Exists(Path.Combine(folder, add)))
                    File.WriteAllBytes(Path.Combine(folder, add), Png.Encode(Thumbnails.AddTileRgba(), Thumbnails.Size, Thumbnails.Size, false));
                urls["add"] = $"coui://{Host}/{add}";

                // The thumbnails of an older art or of things that are gone.
                foreach (string file in Directory.GetFiles(folder, "*.png"))
                {
                    string name = Path.GetFileName(file);
                    if (!used.Contains(name) && !name.StartsWith("edit-", StringComparison.Ordinal))
                    {
                        try { File.Delete(file); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.log.Error("Thumbnails: " + ex);
            }
            finally
            {
                // The fonts (8 to 9 MB each) and the pictures of the markings' words are not kept for the menu: a city that has markings reads them again when it draws the first word.
                MarkingLetters.Release();
                MarkingTemplates.ClearCaches();
                // Added to what is there: a sign the player saved meanwhile has its thumbnail already (MakeNow).
                lock (s_Lock)
                {
                    foreach (KeyValuePair<string, string> pair in urls)
                        s_Urls[pair.Key] = pair.Value;
                }
                s_Ready = true;
                Mod.log.Info($"Thumbnails: {made} made, {reused} reused, {failed} failed in {clock.ElapsedMilliseconds} ms, fonts: {LegendPainter.FontSource}; folder {folder}");
            }
        }
    }
}
