using System;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Render;

namespace CDLC.TS
{
    /// <summary>The mod's preferences (ModsData/CDLC.TS/preferences.json), read once when the mod loads; a missing file is made with the defaults so that there is a file to edit, a damaged one is left alone.</summary>
    internal static class PreferencesStore
    {
        internal static Preferences Current { get; private set; } = new Preferences();

        private static string s_Path;
        private static int s_SaveFailures;

        internal static void Begin(string persistentDataPath)
        {
            string path = Path.Combine(persistentDataPath, "ModsData", Mod.kModName, Preferences.FileName);
            s_Path = path;
            Current = Preferences.Load(path, out PreferencesRead how, out string problem);
            SurfaceReader.Sampling = Current.AdaptiveSampling;
            ChunkSystem.StartingViewDistance = Current.ViewDistance;
            AreaFitSystem.WorkerMode = Current.AreaFitWorker == FeatureState.Off ? FitWorkerMode.Off : Current.AreaFitWorker == FeatureState.Shadow ? FitWorkerMode.Shadow : FitWorkerMode.On;
            CustomSigns.PictureCutMode = Current.PictureCut;
            TextBoxes.Enabled = Current.TextBox != FeatureState.Off;
            StandBoxes.Enabled = Current.StandBox != FeatureState.Off;
            StandBoxes.Log = Mod.log.Info;
            PlacementTool.InitStyle(Current.Profile, Current.StyleProfiles != FeatureState.Off);
            TextSupports.Enabled = Current.TextSupports != FeatureState.Off;
            LoadWarmSystem.Enabled = Current.LoadWarm != FeatureState.Off;
            TsMaterials.SetGameShaderPreference(Current.GameShaderLine);
            ToolbarTabSystem.KerbsInTab = Current.KerbTab != FeatureState.Off;
            switch (how)
            {
                case PreferencesRead.Missing:
                    try
                    {
                        Current.Save(path);
                        Mod.log.Info($"Preferences: made {path} with the defaults (profile {Current.Profile}, road follow {Preferences.StateName(Current.RoadFollow)}, area fit worker {Preferences.StateName(Current.AreaFitWorker)}, adaptive sampling {Preferences.StateName(Current.AdaptiveSampling)}); edit it and start the game again to change them.");
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Mod.log.Warn($"Preferences: {path} could not be made ({ex.Message}); the defaults are used.");
                    }

                    break;
                case PreferencesRead.Damaged:
                    Mod.log.Warn($"Preferences: {path} is {problem}; the defaults are used and the file is left as it is.");
                    break;
                default:
                    Mod.log.Info($"Preferences: read {path}: profile {Current.Profile}, road follow {Preferences.StateName(Current.RoadFollow)}, area fit worker {Preferences.StateName(Current.AreaFitWorker)}, adaptive sampling {Preferences.StateName(Current.AdaptiveSampling)}.");
                    break;
            }

            // After the mode is known: the contours of the kept pictures are read, or traced, on a worker (CustomStore.Begin ran before the preferences).
            CustomStore.WarmContours();
            BeginTextBoxes(persistentDataPath);
        }

        /// <summary>The text light box (0.12.75): this is the main thread (the plans are never made on it), the log, and the fonts folder, scanned for the font choice and the default font read on a worker.</summary>
        private static void BeginTextBoxes(string persistentDataPath)
        {
            TextPlans.MarkMainThread();
            TextPlans.Log = Mod.log.Info;
            string fonts = Path.Combine(persistentDataPath, "ModsData", Mod.kModName, "Fonts");
            TextFonts.Log = Mod.log.Info;
            TextFonts.Begin(fonts);
            if (Current.SystemFonts != FeatureState.Off)
            {
                TextFonts.UseSystem(TextFonts.DefaultSystemFolders());
                TextEmojiFonts.Log = Mod.log.Info;
                TextEmojiFonts.Begin(TextFonts.DefaultSystemFolders());
            }
            TextBoxes.RefreshFonts();
            StandBoxes.RefreshFonts();
            Mod.log.Info(TextFonts.Files.Count == 0
                ? $"Text boxes: no font of its own in {fonts}: put a .ttf, .otf or .ttc file there: it is in the text light box's font list the next time the box is edited (no restart)."
                : $"Text boxes: {TextFonts.Files.Count} font file(s) in {fonts}.");
        }

        /// <summary>The player chose a support style profile: kept in the file (written at once, beside the old one; a file that was damaged is not written over). A failure to write is logged and the choice stays for the session.</summary>
        internal static void SaveProfile(string id)
        {
            Current.Profile = id;
            Persist("the profile");
        }

        /// <summary>The player set the draw distance of the signs and markings (0.12.103): kept the same way.</summary>
        internal static void SaveViewDistance(float metres)
        {
            Current.ViewDistance = metres;
            Persist("the draw distance");
        }

        private static void Persist(string what)
        {
            if (s_Path == null || !Current.Changed)
                return;
            try
            {
                if (!Current.Save(s_Path) && s_SaveFailures++ < 3)
                    Mod.log.Warn($"Preferences: {what} is not saved: the file is damaged and is left as it is.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (s_SaveFailures++ < 3)
                    Mod.log.Warn($"Preferences: {what} could not be saved ({ex.Message}); it stays for this session.");
            }
        }
    }
}
