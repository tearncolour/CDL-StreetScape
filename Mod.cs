using System.IO;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Prefabs;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Game.SceneFlow;

namespace CDLC.TS
{
    public sealed class Mod : IMod
    {
        public const string kModName = "CDLC.TS";
        public const string kVersion = "0.12.108";

        // Before the logger opens its file: what the last session wrote stays as CDLC.TS.Mod-prev.log.
        private static readonly bool s_PreviousKept = CDLC.TS.Geo.LogRotation.KeepPrevious(Path.Combine(UnityEngine.Application.persistentDataPath, "Logs"), "CDLC.TS.Mod");

        public static readonly ILog log = LogManager.GetLogger($"{kModName}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        /// <summary>The folder of this mod's dll; the reports of the phase 0 probes are written next to the game's logs, not here.</summary>
        internal static string Directory { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"{kModName} {kVersion} loading");
            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                Directory = Path.GetDirectoryName(asset.path);

            TsLocale.Register(Directory);
            SupportCatalog.HostsProvided = () => TsExtension.ProvidesHosts;
            ChooseFonts();
            CustomStore.Begin(UnityEngine.Application.persistentDataPath);
            PreferencesStore.Begin(UnityEngine.Application.persistentDataPath);
            // Off the main thread from now on: the baked detail of the barriers' materials is ready long before a city is.
            TsMaterials.BakeDetails();

            // PrefabUpdate, before the system that initializes new prefabs: anywhere else the prefabs are never initialized (see ToolbarTabSystem).
            updateSystem.UpdateBefore<ToolbarTabSystem, PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<PlantPrefabSystem, PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateBefore<KerbUpgradeSystem, PrefabInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            // After the system that writes the flags and masks of new net prefabs.
            updateSystem.UpdateAfter<KerbFlagSystem, NetInitializeSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAt<KerbSystem>(SystemUpdatePhase.MainLoop);
            // The store of the placed items, outside the save (0.12.38, DESIGN-save-data.md): around the game's writing of a save, in its Serialize phase.
            updateSystem.UpdateAt<SaveDataSystem>(SystemUpdatePhase.MainLoop);
            // Before the game renumbers its prefabs for the writing (BeginPrefabSerializationSystem): PrefabSystem.GetPrefabName gives another prefab's name between it and its end.
            updateSystem.UpdateBefore<SaveStripSystem, Game.Serialization.BeginPrefabSerializationSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SaveRestoreSystem, Game.Serialization.WriteSystem>(SystemUpdatePhase.Serialize);
            // Where the game's object systems take the new plants in (their Created and Updated are gone after the frame's clean-up).
            updateSystem.UpdateAt<PlantSystem>(SystemUpdatePhase.Modification1);
            // What this mod's later systems ask the game's net and object systems to look at (Updated) is given at the start of the next frame: in the frame it is asked in they have run, and Updated is taken off at its end.
            updateSystem.UpdateAt<NextFrameUpdateSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<ChunkSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAt<ItemRoadSystem>(SystemUpdatePhase.ModificationEnd);
            // After the barrier that makes the game's new lanes (SecondaryLaneSystem runs in Modification4B): a new marking of the game in an area is hidden before it is drawn.
            updateSystem.UpdateAfter<ClearMarkingsSystem>(SystemUpdatePhase.Modification4B);
            // The tag of a lane an area takes is taken off again by another mod's system in the first place of ModificationEnd (Anarchy's RemoveOverridenSystem): counted at the ends of Modification4B and 5 and put on
            // again at the end of ModificationEnd, before the trace's look (0.12.86).
            updateSystem.UpdateAfter<ClearProbe4BSystem>(SystemUpdatePhase.Modification4B);
            updateSystem.UpdateAfter<ClearProbe5System>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAfter<ClearLateSystem>(SystemUpdatePhase.ModificationEnd);
            // What became of the lanes it hid: looked at in the same frame, after every modification system and before the clean-up, and at the start of the next one (0.12.84).
            updateSystem.UpdateAfter<ClearTraceEndSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateAt<ClearTraceStartSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<AreaFitSystem>(SystemUpdatePhase.MainLoop);
            // After the systems whose first-time work it watches (the load's hold on the loading screen, 0.12.78).
            updateSystem.UpdateAt<LoadWarmSystem>(SystemUpdatePhase.MainLoop);
            // The batch interface (TsBatchApi, 0.12.67): matches a submitted batch to roads over several frames and writes it in one.
            updateSystem.UpdateAt<TsBatchSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAt<PlacementTool>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<TsUiSystem>(SystemUpdatePhase.UIUpdate);
        }

        /// <summary>
        /// The fonts of the boards and the road words (0.12.28, SignFonts): the player's in ModsData/CDLC.TS/Fonts before the mod's own. The folder is made, with a note of the names it takes, so that a
        /// player can find where to put a font.
        /// </summary>
        private static void ChooseFonts()
        {
            string folder = Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", kModName, "Fonts");
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                string note = Path.Combine(folder, "README.txt");
                if (!File.Exists(note))
                {
                    File.WriteAllText(note,
                        "CDLC.TS fonts / 字体\r\n\r\n"
                        + "Put a font here to use it instead of the one the mod carries (.ttf or .otf; start the game again):\r\n"
                        + "  cjk.ttf   (or 中文.ttf)  the Chinese characters\r\n"
                        + "  latin.ttf (or 西文.ttf)  the letters and figures\r\n"
                        + "Without a latin font the letters and figures are the Chinese font's own.\r\n\r\n"
                        + "把字体放在这里即可替换 mod 自带的字体（.ttf 或 .otf，重启游戏后生效）：\r\n"
                        + "  cjk.ttf 或 中文.ttf：汉字\r\n"
                        + "  latin.ttf 或 西文.ttf：字母和数字\r\n"
                        + "没有西文字体时，字母和数字用中文字体自带的。\r\n", System.Text.Encoding.UTF8);
                }
            }
            catch (System.Exception ex)
            {
                log.Warn("Fonts: the folder " + folder + " could not be made: " + ex.Message);
            }

            CDLC.TS.Geo.SignFonts.Resolve(folder, Directory != null ? Path.Combine(Directory, "Fonts") : null);
            log.Info("Fonts: " + CDLC.TS.Geo.SignFonts.Describe(false) + " (" + (CDLC.TS.Geo.SignFonts.Cjk ?? "no file") + (CDLC.TS.Geo.SignFonts.Latin != null ? ", " + CDLC.TS.Geo.SignFonts.Latin : "") + "); the player's go in " + folder + ".");
        }

        public void OnDispose()
        {
            log.Info($"{kModName} disposing");
            PlacementTool.Shutdown();
            CustomPictures.Clear();
            CDLC.TS.Geo.TrafficMap.Clear();
            CDLC.TS.Geo.VmsAuto.Clear();
            TsLocale.Unregister();
        }
    }
}
