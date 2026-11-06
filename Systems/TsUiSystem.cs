using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Render;
using Colossal.UI.Binding;
using Game.SceneFlow;
using Game.UI;
using Newtonsoft.Json;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// The bindings of CDLC.TS.mjs: which toolbar category is ours, the catalogue of signs for the search, what the tool has chosen and sees, the editor of the settings of a guide
    /// board (built from the fields of its template, so a new template needs no new UI), and the triggers of the panel (choose a sign, place, edit or delete, change a setting, leave,
    /// and the phase 1 test switches for the render queue and the night glow).
    /// </summary>
    internal sealed partial class TsUiSystem : UISystemBase
    {
        private const string kGroup = "cdlcts";

        private ValueBinding<Entity> m_Category;
        private ValueBinding<string> m_Status;
        private ValueBinding<string> m_Catalog;
        private ValueBinding<string> m_Selected;
        private ValueBinding<string> m_Supports;
        private ValueBinding<string> m_Support;
        private ValueBinding<string> m_Editor;
        private ValueBinding<int> m_Mode;

        /// <summary>The distance from the kerb the next support is placed with (m; PlacementTool.Margin), for the slider.</summary>
        private ValueBinding<float> m_Margin;

        /// <summary>Whether the next post stands in the road (PlacementTool.Mid: the "/" key and the support page's button), for the button.</summary>
        private ValueBinding<bool> m_Mid;
        private ValueBinding<bool> m_ToolActive;
        private ValueBinding<string> m_Hint;
        private ValueBinding<string> m_Lang;
        private ValueBinding<int> m_Night;
        private ValueBinding<int> m_NightGain;
        private ValueBinding<int> m_ViewDistance;

        /// <summary>
        /// What the mod holds and what each draw distance would take (0.12.104, Footprint.Build), as JSON: made only while the panel shows it (it asks with WatchFootprint) and at most every
        /// FootprintSeconds, for it walks the tiles and the item records.
        /// </summary>
        private ValueBinding<string> m_Footprint;
        private bool m_FootprintWatched;
        private float m_FootprintAt = -100f;
        private readonly FootprintInput m_FootprintInput = new FootprintInput();
        private readonly long[] m_FaceBytes = new long[3];
        private const float kFootprintSeconds = 2f;
        private ValueBinding<int> m_LedDay;
        private ValueBinding<int> m_LedNight;
        private ValueBinding<bool> m_Detail;
        private ValueBinding<int> m_LampLit, m_LampGlow;
        private ValueBinding<int> m_ClearAreas;
        private ValueBinding<int> m_ClearHidden;

        /// <summary>How many items have no road in reach (TsItem.FlagOrphan): in a save played before 0.12.5 they may be another city's, carried over by a load in the same session (0.12.5).</summary>
        private ValueBinding<int> m_Orphans;

        /// <summary>The save-safe store (0.12.38, SaveDataSystem.Json), sent when it changes.</summary>
        private ValueBinding<string> m_SaveData;
        private int m_SaveDataRevision = -1;

        /// <summary>The player's own signs (0.12.29): the tile that adds one, the folder, the PNG files in it and what the last import or save said, as JSON.</summary>
        private ValueBinding<string> m_Custom;
        private List<CustomStore.FileEntry> m_CustomFiles = new List<CustomStore.FileEntry>();
        private string m_CustomNote = "";
        private bool m_CustomNoteBad;
        private int m_CustomVersion;
        private bool m_CustomDirty = true;
        private EntityQuery m_ItemQuery;
        private int m_Frame;

        // What the line of help and the editor were made from: they are made again only when it changes (0.7.1; they were made at every frame the tool was open, a JSON text and a formatted string each).
        private HintInfo m_HintSeen;
        private bool m_HintActive;
        private bool m_HintSeenZh, m_HintHave;
        private (int, string, int, int, int, int, bool, bool) m_EditorKey;
        private bool m_EditorKeyHave;
        private bool m_ThumbsPublished;

        /// <summary>Whether the supports published are those with another mod's arm (SupportCatalog.Listed): that mod joins some seconds after this one, and the list is made again when it has.</summary>
        private bool m_HostsListed;
        private int m_UiErrors;
        private string m_PreviewKey = "";
        private string m_PreviewUrl = "";

        private sealed class CatalogEntry
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("code")] public string Code;
            [JsonProperty("ce")] public string CodeEn;
            [JsonProperty("zh")] public string Zh;
            [JsonProperty("en")] public string En;
            [JsonProperty("kw")] public string[] Keywords;
            [JsonProperty("py")] public string Pinyin;
            [JsonProperty("grp")] public string Group;
            [JsonProperty("ser")] public string Series;
            [JsonProperty("so", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool SeriesOnly;
            [JsonProperty("thumb")] public string Thumb;
            [JsonProperty("cfg")] public bool Configurable;
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            // The thumbnails are made in the background; the catalogue is sent again with their addresses when they are there (OnUpdate).
            ThumbnailStore.Begin();
            AddBinding(m_Category = new ValueBinding<Entity>(kGroup, "Category", Entity.Null));
            AddBinding(m_Status = new ValueBinding<string>(kGroup, "Status", ""));
            AddBinding(m_Catalog = new ValueBinding<string>(kGroup, "Catalog", CatalogJson()));
            AddBinding(m_Selected = new ValueBinding<string>(kGroup, "Selected", ""));
            AddBinding(m_Supports = new ValueBinding<string>(kGroup, "Supports", SupportsJson()));
            AddBinding(m_Support = new ValueBinding<string>(kGroup, "Support", ""));
            AddBinding(m_Editor = new ValueBinding<string>(kGroup, "Editor", ""));
            AddBinding(m_Mode = new ValueBinding<int>(kGroup, "Mode", 0));
            AddBinding(m_Margin = new ValueBinding<float>(kGroup, "Margin", PlacementTool.Margin));
            AddBinding(new TriggerBinding<float>(kGroup, "SetMargin", PlacementTool.SetMargin));
            AddBinding(m_Mid = new ValueBinding<bool>(kGroup, "Mid", PlacementTool.Mid));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetMid", PlacementTool.SetMid));
            AddBinding(new TriggerBinding<float>(kGroup, "SetKerb", metres => PlacementTool.Instance?.SetEditedKerb(metres)));
            // 0.12.85: the position of a support that stands, by slider and box: along its road or wall, from the road's centre line, and the lower edge of a board on a wall.
            AddBinding(new TriggerBinding<float>(kGroup, "SetAlong", metres => PlacementTool.Instance?.SetEditedAlong(metres)));
            AddBinding(new TriggerBinding<float>(kGroup, "SetCentre", metres => PlacementTool.Instance?.SetEditedCentre(metres)));
            AddBinding(new TriggerBinding<float>(kGroup, "SetLower", metres => PlacementTool.Instance?.SetEditedWallHeight(metres)));
            AddProfileBindings();
            AddBinding(m_ToolActive = new ValueBinding<bool>(kGroup, "ToolActive", false));
            AddBinding(m_Hint = new ValueBinding<string>(kGroup, "Hint", ""));
            // "zh" or "en": the panel names signs, supports and profiles in this language (the catalogue carries both).
            AddBinding(m_Lang = new ValueBinding<string>(kGroup, "Lang", Chinese() ? "zh" : "en"));
            AddBinding(m_Night = new ValueBinding<int>(kGroup, "Night", Mathf.RoundToInt(TsMaterials.NightNits * 10f)));
            AddBinding(m_NightGain = new ValueBinding<int>(kGroup, "NightGain", Mathf.RoundToInt(TsMaterials.NightGain * 10f)));
            // 0.12.103: how far the signs and markings are drawn (m), kept in preferences.json.
            AddBinding(m_ViewDistance = new ValueBinding<int>(kGroup, "ViewDistance", Mathf.RoundToInt(PreferencesStore.Current.ViewDistance)));
            AddBinding(m_Footprint = new ValueBinding<string>(kGroup, "Footprint", ""));
            AddBinding(new TriggerBinding<bool>(kGroup, "WatchFootprint", on =>
            {
                m_FootprintWatched = on;
                m_FootprintAt = -100f;
                if (!on)
                    m_Footprint.Update("");
            }));
            // The glow of the LED screens (variable-message signs): by day in hundreds of nits, at night in nits.
            AddBinding(m_LedDay = new ValueBinding<int>(kGroup, "LedDay", Mathf.RoundToInt(TsMaterials.LedDayNits / 100f)));
            AddBinding(m_LedNight = new ValueBinding<int>(kGroup, "LedNight", Mathf.RoundToInt(TsMaterials.LedNightNits)));
            // 0.12.32: the barriers' baked detail (normal maps) on or off, to compare.
            AddBinding(m_Detail = new ValueBinding<bool>(kGroup, "Detail", TsMaterials.DetailOn));
            // The areas that clear the game's markings and how many of its markings carry the tag (mk.clear): the panel shows them with the button that restores all.
            AddBinding(m_ClearAreas = new ValueBinding<int>(kGroup, "ClearAreas", 0));
            AddBinding(m_ClearHidden = new ValueBinding<int>(kGroup, "ClearHidden", 0));
            AddBinding(m_Orphans = new ValueBinding<int>(kGroup, "Orphans", 0));
            AddBinding(m_SaveData = new ValueBinding<string>(kGroup, "SaveData", ""));
            // 0.12.38: the store's choices: a record offered restored or all ignored; a road put away (its index, -1: all) shown on the map, placed after all or deleted.
            AddBinding(new TriggerBinding<int>(kGroup, "SaveDataRestore", index => SaveDataSystem.Instance?.RestoreOffer(index)));
            AddBinding(new TriggerBinding(kGroup, "SaveDataIgnore", () => SaveDataSystem.Instance?.IgnoreOffers()));
            AddBinding(new TriggerBinding<int>(kGroup, "SaveDataLocate", anchor => SaveDataSystem.Instance?.Locate(anchor)));
            AddBinding(new TriggerBinding<int>(kGroup, "SaveDataPlace", anchor => SaveDataSystem.Instance?.Place(anchor)));
            AddBinding(new TriggerBinding<int>(kGroup, "SaveDataDelete", anchor => SaveDataSystem.Instance?.Delete(anchor)));
            AddBinding(m_Custom = new ValueBinding<string>(kGroup, "Custom", ""));
            m_CustomVersion = CustomSigns.Version;
            AddBinding(new TriggerBinding(kGroup, "ListCustomFiles", () =>
            {
                m_CustomFiles = CustomStore.Files();
                m_CustomDirty = true;
            }));
            AddBinding(new TriggerBinding(kGroup, "OpenCustomFolder", () =>
            {
                if (CustomStore.Folder != null)
                    Application.OpenURL("file:///" + CustomStore.Folder.Replace('\\', '/'));
            }));
            AddBinding(new TriggerBinding<string>(kGroup, "ImportCustom", ImportCustom));
            // 0.12.49: a picture of the folder on the lamp-post ad the editor shows, or (an empty name) taken off it.
            AddBinding(new TriggerBinding<string>(kGroup, "UsePicture", UsePicture));
            AddBinding(new TriggerBinding(kGroup, "SaveCustom", SaveCustom));
            AddBinding(new TriggerBinding<string>(kGroup, "UpdateCustom", UpdateCustom));
            AddBinding(new TriggerBinding<string>(kGroup, "DeleteCustom", DeleteCustom));
            m_ItemQuery = GetEntityQuery(ComponentType.ReadOnly<TsItem>());

            AddBinding(new TriggerBinding<string>(kGroup, "Select", id => PlacementTool.Instance?.Choose(id)));
            AddBinding(new TriggerBinding<string>(kGroup, "SetSupport", id => PlacementTool.Instance?.ChooseSupport(id)));
            AddBinding(new TriggerBinding<int>(kGroup, "SetMode", mode => PlacementTool.Instance?.SetMode(mode)));
            AddBinding(new TriggerBinding<string>(kGroup, "EditField", payload =>
            {
                // "target|key=value": the target is the editor revision that named the board or draft, not whichever one is selected when a delayed message arrives.
                int bar = payload == null ? -1 : payload.IndexOf('|');
                int eq = bar < 0 ? -1 : payload.IndexOf('=', bar + 1);
                if (bar > 0 && eq > bar + 1 && int.TryParse(payload.Substring(0, bar), out int target))
                    PlacementTool.Instance?.EditField(target, payload.Substring(bar + 1, eq - bar - 1), payload.Substring(eq + 1));
            }));
            AddBinding(new TriggerBinding<float>(kGroup, "SetSpan", metres => PlacementTool.Instance?.SetSpan(metres)));
            AddBinding(new TriggerBinding<float>(kGroup, "SetClearance", metres => PlacementTool.Instance?.SetClearance(metres)));
            AddBinding(new TriggerBinding<int>(kGroup, "SelectBoard", member => PlacementTool.Instance?.SelectBoard(member)));
            AddBinding(new TriggerBinding<int>(kGroup, "RemoveBoard", member => PlacementTool.Instance?.RemoveBoard(member)));
            AddBinding(new TriggerBinding<int>(kGroup, "ReplaceBoard", member => PlacementTool.Instance?.ReplaceBoard(member)));
            AddBinding(new TriggerBinding(kGroup, "AddBoardHere", () => PlacementTool.Instance?.AddBoardHere()));
            AddBinding(new TriggerBinding<int>(kGroup, "SetLane", lane => PlacementTool.Instance?.SetLane(lane)));
            AddBinding(new TriggerBinding<int>(kGroup, "NudgeBoard", code => PlacementTool.Instance?.NudgeBoard(code)));
            AddBinding(new TriggerBinding<int>(kGroup, "TurnBoard", degrees => PlacementTool.Instance?.TurnBoard(degrees)));
            AddBinding(new TriggerBinding<float>(kGroup, "SetTurn", degrees => PlacementTool.Instance?.SetTurn(degrees)));
            AddBinding(new TriggerBinding<int>(kGroup, "SetTilt", degrees => PlacementTool.Instance?.SetTilt(degrees)));
            AddBinding(new TriggerBinding<int>(kGroup, "SetFinish", finish => PlacementTool.Instance?.SetStyle(finish: finish)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetBlackBack", on => PlacementTool.Instance?.SetStyle(blackBack: on ? 1 : 0)));
            AddBinding(new TriggerBinding<int>(kGroup, "SetRootPaint", root => PlacementTool.Instance?.SetStyle(root: root)));
            AddBinding(new TriggerBinding(kGroup, "FlipBoard", () => PlacementTool.Instance?.FlipBoard()));
            AddBinding(new TriggerBinding(kGroup, "SpreadLanes", () => PlacementTool.Instance?.SpreadLanes()));
            AddBinding(new TriggerBinding(kGroup, "ReadJunction", () => PlacementTool.Instance?.ReadJunctionNow()));
            AddBinding(new TriggerBinding(kGroup, "FitFacade", () => PlacementTool.Instance?.FitFacadeNow()));
            AddBinding(new TriggerBinding<int>(kGroup, "SetReadJunctions", on => PlacementTool.SetReadJunctionsOn(on != 0)));
            AddBinding(new TriggerBinding(kGroup, "AutoForget", () => PlacementTool.Instance?.ForgetAutoChoice()));
            AddBinding(new TriggerBinding(kGroup, "AutoUndo", () => PlacementTool.Instance?.UndoAuto()));
            AddBinding(new TriggerBinding(kGroup, "AutoRemoveOld", () => PlacementTool.Instance?.RemoveOldLines()));
            AddBinding(new TriggerBinding(kGroup, "Leave", () => PlacementTool.Instance?.Leave()));
            AddBinding(new TriggerBinding<string>(kGroup, "UiError", text =>
            {
                // What the script could not draw, as the script reports it: the only way to see a script error from outside the game.
                if (m_UiErrors++ < 20)
                    Mod.log.Error("UI script: " + text);
            }));
            // In tenths of a nit: the steps of the panel are 0, 0.2, 0.5, 1, 2, 4 and 8 nits (a whole number could not say 0.5).
            AddBinding(new TriggerBinding<int>(kGroup, "SetNight", tenths => TsMaterials.SetNightNits(tenths / 10f)));
            // The brightness of the faces at night as a factor, in tenths: 10 is none (1.0), 20 doubles it.
            AddBinding(new TriggerBinding<int>(kGroup, "SetNightGain", tenths => TsMaterials.SetNightGain(tenths / 10f)));
            AddBinding(new TriggerBinding<int>(kGroup, "SetViewDistance", metres =>
            {
                float far = StreamOptions.ClampFar(metres);
                ChunkSystem.Instance?.SetViewDistance(far);
                PreferencesStore.SaveViewDistance(far);
                m_ViewDistance.Update(Mathf.RoundToInt(far));
            }));
            AddBinding(new TriggerBinding<int>(kGroup, "SetLedDay", hundreds => TsMaterials.SetLedNits(hundreds * 100f, TsMaterials.LedNightNits)));
            AddBinding(new TriggerBinding<int>(kGroup, "SetLedNight", nits => TsMaterials.SetLedNits(TsMaterials.LedDayNits, nits)));
            AddBinding(new TriggerBinding<bool>(kGroup, "SetDetail", on => TsMaterials.SetDetail(on)));
            // 0.12.49: the lamp-post ads' light boxes and LED figures, and their lanterns, at full night (nits).
            AddBinding(m_LampLit = new ValueBinding<int>(kGroup, "LampLit", Mathf.RoundToInt(TsMaterials.LitNits)));
            AddBinding(m_LampGlow = new ValueBinding<int>(kGroup, "LampGlow", Mathf.RoundToInt(TsMaterials.GlowNits)));
            AddBinding(new TriggerBinding<int>(kGroup, "SetLampLit", nits => TsMaterials.SetLampGlow(nits, TsMaterials.GlowNits)));
            AddBinding(new TriggerBinding<int>(kGroup, "SetLampGlow", nits => TsMaterials.SetLampGlow(TsMaterials.LitNits, nits)));
            // 0.12.1: the support that stands moved along or across its road (PlacementTool.MoveStep's codes), and its span or arm from the road (FitSpan; SetSpan sets it by hand, 0.12.78).
            AddBinding(new TriggerBinding<int>(kGroup, "MoveSupport", code => PlacementTool.Instance?.MoveSupport(code)));
            AddBinding(new TriggerBinding(kGroup, "FitSpan", () => PlacementTool.Instance?.FitSpan()));
            // 0.12.5: the items with no road in reach are deleted (after the panel's confirmation): before 0.12.5 the items of a city stayed in the world when another was loaded, and went into its file.
            AddBinding(new TriggerBinding(kGroup, "DeleteOrphans", DeleteOrphans));
            AddBinding(new TriggerBinding(kGroup, "RestoreMarkings", () =>
            {
                Mod.log.Info("Panel: restore all of the game's markings.");
                ClearMarkingsSystem.Instance?.RestoreAll();
            }));
        }

        // ------------------------------------------------------------------ the player's own signs (0.12.29)

        private sealed class CustomFile
        {
            [JsonProperty("n")] public string Name;
            [JsonProperty("w")] public int Width;
            [JsonProperty("h")] public int Height;
            [JsonProperty("kb")] public long Kb;
        }

        private sealed class CustomModel
        {
            [JsonProperty("add")] public string Add;
            [JsonProperty("folder")] public string Folder;
            [JsonProperty("files")] public List<CustomFile> Files = new List<CustomFile>();
            [JsonProperty("note")] public string Note;
            [JsonProperty("bad")] public bool Bad;
        }

        private string CustomJson()
        {
            var model = new CustomModel { Add = ThumbnailStore.AddTileUrl, Folder = CustomStore.Folder ?? "", Note = m_CustomNote, Bad = m_CustomNoteBad };
            foreach (CustomStore.FileEntry file in m_CustomFiles)
                model.Files.Add(new CustomFile { Name = file.Name, Width = file.Width, Height = file.Height, Kb = (file.Bytes + 1023) / 1024 });
            return JsonConvert.SerializeObject(model);
        }

        private void Say(string zh, string en, bool bad = false)
        {
            m_CustomNote = Chinese() ? zh : en;
            m_CustomNoteBad = bad;
            m_CustomDirty = true;
        }

        /// <summary>A file of the folder imported: its picture kept, and the tool given a new sign of it to set and place (the editor shows its settings and its picture).</summary>
        private void ImportCustom(string name)
        {
            PlacementTool tool = PlacementTool.Instance;
            string spec = CustomStore.Import(name, out string why);
            if (spec == null || tool == null)
            {
                switch (why)
                {
                    case "too-large": Say($"{name} 太大（最大 32 MB、每边 8192 像素）。", $"{name} is too large (32 MB, 8192 px a side at most).", true); break;
                    case "gone": Say($"{name} 已不在文件夹里。", $"{name} is no longer in the folder.", true); break;
                    case "unreadable": Say($"{name} 无法读取。", $"{name} cannot be read.", true); break;
                    default: Say($"{name} 不是 PNG 图片。", $"{name} is not a PNG picture.", true); break;
                }

                return;
            }

            tool.SetMode(0);
            tool.Choose(CustomSigns.TemplateId, spec);
            Say($"已导入 {name}：在设置窗口调整后保存。", $"{name} imported: adjust it in the settings, then save.");
        }

        private void UsePicture(string name)
        {
            PlacementTool tool = PlacementTool.Instance;
            if (tool == null)
                return;
            if (string.IsNullOrEmpty(name))
            {
                tool.SetPicture("", 0, 0);
                Say("已去掉图片，恢复图案。", "The picture is taken off: the print is back.");
                return;
            }

            if (!CustomStore.ImportPicture(name, out string key, out int w, out int h, out string why))
            {
                Say(why == "too-large" ? $"{name} 太大（最大 32 MB、每边 8192 像素）。" : why == "gone" ? $"{name} 已不在文件夹里。" : $"{name} 无法读取或不是 PNG 图片。",
                    why == "too-large" ? $"{name} is too large (32 MB, 8192 px a side at most)." : why == "gone" ? $"{name} is no longer in the folder." : $"{name} cannot be read or is not a PNG picture.", true);
                return;
            }

            tool.SetPicture(key, w, h);
            Say($"已换上 {name}。", $"{name} is on it now.");
        }

        /// <summary>The settings of the custom sign the editor shows (the draft, or the edited item's); null when it shows no custom sign.</summary>
        private static string CustomSpecShown()
        {
            PlacementTool.EditorView view = PlacementTool.Instance != null ? PlacementTool.Instance.GetEditorView() : default;
            return view.Active && view.Def != null && view.Def.Id == CustomSigns.TemplateId ? view.Spec : null;
        }

        private void SaveCustom()
        {
            string spec = CustomSpecShown();
            if (spec == null)
                return;
            string key = CustomSigns.Add(spec);
            if (!CustomStore.Save())
            {
                CustomSigns.Remove(key);
                Say("路牌没能保存（见日志）。", "The sign could not be saved (see the log).", true);
                return;
            }

            SignChoice choice = SignCatalog.ChoiceById(CustomSigns.ChoicePrefix + key);
            ThumbnailStore.MakeNow(choice);
            // A draft becomes the saved sign that is being placed; an edited item stays as it is.
            if (PlacementTool.Mode != 2)
                PlacementTool.Instance?.Choose(choice.Id);
            Say($"已保存为路牌：{choice.Zh}。", $"Saved as a sign: {choice.En}.");
        }

        private void UpdateCustom(string key)
        {
            string spec = CustomSpecShown();
            if (spec == null || PlacementTool.SelectedId != CustomSigns.ChoicePrefix + key || !CustomSigns.Update(key, spec))
                return;
            if (!CustomStore.Save())
            {
                Say("修改没能保存（见日志）。", "The change could not be saved (see the log).", true);
                return;
            }

            SignChoice choice = SignCatalog.ChoiceById(CustomSigns.ChoicePrefix + key);
            ThumbnailStore.MakeNow(choice);
            Say($"已保存修改：{choice.Zh}（已放置的路牌不变）。", $"Saved: {choice.En} (the signs placed before stay as they are).");
        }

        private void DeleteCustom(string key)
        {
            string name = CustomSigns.ByChoiceId(CustomSigns.ChoicePrefix + key) is CustomSign sign ? CustomSigns.NameOf(sign.Spec) : key;
            if (!CustomSigns.Remove(key))
                return;
            CustomStore.Save();
            if (PlacementTool.SelectedId == CustomSigns.ChoicePrefix + key)
                PlacementTool.Instance?.Leave();
            Say($"已删除路牌：{name}（已放置的路牌不变）。", $"Deleted: {name} (the signs placed before stay).");
        }

        private sealed class SupportEntry
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("zh")] public string Zh;
            [JsonProperty("en")] public string En;
            [JsonProperty("fam")] public string Family;
            [JsonProperty("thumb")] public string Thumb;

            /// <summary>The support is a post, which the in-road button puts across the road (MidRoad.Takes).</summary>
            [JsonProperty("mid")] public bool Mid;
        }

        private static string SupportsJson()
        {
            var list = new List<SupportEntry>();
            foreach (SupportDef def in SupportCatalog.Listed)
                list.Add(new SupportEntry { Id = def.Id, Zh = def.Zh, En = def.En, Family = def.Family, Thumb = ThumbnailStore.SupportUrl(def.Id), Mid = MidRoad.Takes(def.Kind) });
            return JsonConvert.SerializeObject(list);
        }

        private static string CatalogJson()
        {
            var list = new List<CatalogEntry>();
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (choice.Hidden)
                    continue;
                list.Add(new CatalogEntry
                {
                    Id = choice.Id, Code = choice.Code, CodeEn = choice.CodeEn, Zh = choice.Zh, En = choice.En, Keywords = choice.Keywords, Pinyin = choice.Pinyin, Group = choice.Group, Series = choice.Series, SeriesOnly = choice.SeriesOnly,
                    Thumb = ThumbnailStore.SignUrl(choice.Id), Configurable = choice.Def.Template != null && choice.Def.Template.Fields.Length > 0
                });
            }

            return JsonConvert.SerializeObject(list);
        }

        // ------------------------------------------------------------------ the editor

        private sealed class EditorChoice
        {
            [JsonProperty("v")] public string Value;
            [JsonProperty("l")] public string Label;
        }

        private sealed class EditorField
        {
            [JsonProperty("k")] public string Key;
            [JsonProperty("l")] public string Label;
            [JsonProperty("t")] public string Kind;
            [JsonProperty("v")] public string Value;
            [JsonProperty("max")] public int Max;
            [JsonProperty("ch")] public List<EditorChoice> Choices;

            /// <summary>A number with a slider (GuideField.Slider): from, to, step; null for any other field.</summary>
            [JsonProperty("r", NullValueHandling = NullValueHandling.Ignore)] public double[] Range;

            /// <summary>A text over the whole width of the panel, its label above it (GuideField.Wide).</summary>
            [JsonProperty("w", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool Wide;

            /// <summary>0.12.47: the heading of the section the field starts (GuideField.In); null: none.</summary>
            [JsonProperty("sec", NullValueHandling = NullValueHandling.Ignore)] public string Section;
        }

        private sealed class EditorBoard
        {
            [JsonProperty("m")] public int Member;
            [JsonProperty("code")] public string Code;
            [JsonProperty("title")] public string Title;
            [JsonProperty("lane")] public int Lane;
            [JsonProperty("x")] public float X;
        }

        private sealed class EditorModel
        {
            /// <summary>Changes when the editor starts to show another sign: the panel rebuilds its inputs then (an input keeps what the player typed otherwise).</summary>
            [JsonProperty("id")] public int Id;
            [JsonProperty("mode")] public string Mode;
            [JsonProperty("code")] public string Code;
            [JsonProperty("title")] public string Title;
            [JsonProperty("preview")] public string Preview;
            [JsonProperty("pw")] public int PictureWidth;
            [JsonProperty("ph")] public int PictureHeight;
            [JsonProperty("plan")] public string Plan;

            /// <summary>A road marking: it has no support and no boards, so the editor shows only its settings (and its picture from above).</summary>
            [JsonProperty("marking")] public bool Marking;
            [JsonProperty("info")] public List<string> Info = new List<string>();

            /// <summary>The lines about the support (its form, the number of boards, the measures), for the editor's second tab; and whether it is a beam or an arm (the move buttons are left and right, not up and down).</summary>
            [JsonProperty("supportInfo")] public List<string> SupportInfo = new List<string>();
            [JsonProperty("hung")] public bool Hung;
            [JsonProperty("reach")] public float Reach;
            [JsonProperty("reachKind")] public string ReachKind = "";

            /// <summary>The slider of the span or the arm (0.12.78): its range and step, and whether the length is the player's (a placed one set by hand, or the one the next is placed with).</summary>
            [JsonProperty("reachMin")] public float ReachMin;
            [JsonProperty("reachMax")] public float ReachMax;
            [JsonProperty("reachStep")] public float ReachStep;
            [JsonProperty("reachHand")] public bool ReachHand;

            /// <summary>The clearance slider of a hung support (0.12.78): the height of the lower edge of its boards, its range and step, and whether it is the player's. clearMax 0: the support has none to set.</summary>
            [JsonProperty("clear")] public float Clear;
            [JsonProperty("clearMin")] public float ClearMin;
            [JsonProperty("clearMax")] public float ClearMax;
            [JsonProperty("clearStep")] public float ClearStep;
            [JsonProperty("clearHand")] public bool ClearHand;

            /// <summary>The support's post takes boards of its own (a hung support: the lane row offers "on the post"), and the support is on a wall (1 flat, 2 a bracket; 0 not: the move buttons say along the wall and up and down).</summary>
            [JsonProperty("posts")] public bool PostBoards;
            [JsonProperty("wall")] public int Wall;

            /// <summary>The board is on another mod's signal arm (0.12.57): the editor's move buttons are left and right along the arm and up and down.</summary>
            [JsonProperty("onArm")] public bool OnArm;

            /// <summary>The margin (m) a placed post stands at, for the slider of the editor's move rows; absent for any support that is not moved across by a margin (PlacementTool.TryEditedKerb).</summary>
            [JsonProperty("kerb", NullValueHandling = NullValueHandling.Ignore)] public float? Kerb;

            /// <summary>The position of the edited support along its road or its wall (m from where it stood when it was picked; PlacementTool.TryEditedAlong), the distance of a support the margin does not describe from its
            /// road's centre line and the furthest that slider goes (TryEditedCentre), and the lower edge of a board on a wall (TryEditedWallHeight): each absent where the editor has no such control (0.12.85).</summary>
            [JsonProperty("along", NullValueHandling = NullValueHandling.Ignore)] public float? Along;
            [JsonProperty("centre", NullValueHandling = NullValueHandling.Ignore)] public float? Centre;
            [JsonProperty("centreMax", NullValueHandling = NullValueHandling.Ignore)] public float? CentreMax;
            [JsonProperty("lower", NullValueHandling = NullValueHandling.Ignore)] public float? Lower;

            /// <summary>The board being edited, or the sign about to be placed, turns (0.12.8: the editor shows the row that turns it), and its facing (degrees to a tenth, -180 to 180, + clockwise seen from above).</summary>
            [JsonProperty("turns")] public bool Turns;
            [JsonProperty("turn")] public float Turn;

            /// <summary>The board being edited is on a gantry's beam: the editor shows the row that moves it to the other face (0.12.22), and it is on the back.</summary>
            [JsonProperty("faces")] public bool Faces;
            [JsonProperty("back")] public bool Back;

            /// <summary>The support's boards lean forward (0.12.55: the editor shows the row of the lean), and how far (degrees).</summary>
            [JsonProperty("leans")] public bool Leans;
            [JsonProperty("tilt")] public int Tilt;

            /// <summary>
            /// The support is painted (0.12.57: the editor shows the finish's rows): its finish (0 galvanised, 1 graphite black, 2 dark grey, 3 light grey), the boards' black backs, the paint on the posts'
            /// root (0 none, 1 立面标记, 2 blue with rings).
            /// </summary>
            [JsonProperty("painted")] public bool Painted;
            [JsonProperty("finish")] public int Finish;
            [JsonProperty("blackBack")] public bool BlackBack;
            [JsonProperty("root")] public int Root;

            /// <summary>The support's posts can be painted at the root: the editor shows the row of it for such a support only (0.12.58).</summary>
            [JsonProperty("rootable")] public bool Rootable;

            /// <summary>The boards of the edited support in the order they hang (m: member, code, title, lane, x), the member being edited (sel), the lanes the boards face (the X of each from the driver's left) and why there are none ("ok", "post", "noroad"), the lane of the board being edited, and warnings.</summary>
            [JsonProperty("boards")] public List<EditorBoard> Boards = new List<EditorBoard>();
            [JsonProperty("sel")] public int Selected = -1;
            [JsonProperty("max")] public int MaxBoards = 4;
            [JsonProperty("lanes")] public List<float> Lanes = new List<float>();
            [JsonProperty("laneState")] public string LaneState = "";
            [JsonProperty("lane")] public int Lane;
            [JsonProperty("warn")] public List<string> Warn = new List<string>();
            [JsonProperty("fields")] public List<EditorField> Fields = new List<EditorField>();

            /// <summary>For a junction sign (路1, 路3, 路4, 路6, 路7): whether placing reads the junction ahead, and what the last reading said; null for any other sign.</summary>
            [JsonProperty("jn", NullValueHandling = NullValueHandling.Ignore)] public EditorJunction Junction;

            /// <summary>The board is a text light box on a flat wall support: the editor offers to read the wall's curve again (PlacementTool.FitFacadeNow).</summary>
            [JsonProperty("fw", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool FitWall;

            /// <summary>For a board that takes a picture (0.12.49, a lamp-post ad): whether it has one now; null for any other.</summary>
            [JsonProperty("pic", NullValueHandling = NullValueHandling.Ignore)] public EditorPicture Picture;

            /// <summary>For the automatic marking (0.12.44): the stretch chosen on the map, if one is (on), its length, segments and approaches, and whether the panel's settings are still to be drawn.</summary>
            [JsonProperty("auto", NullValueHandling = NullValueHandling.Ignore)] public EditorAuto Auto;

            /// <summary>For a sign of the player's own (0.12.29): the key of the saved sign being placed ("" for a new picture or a placed item); null for any other sign.</summary>
            [JsonProperty("custom", NullValueHandling = NullValueHandling.Ignore)] public EditorCustom Custom;
        }

        private sealed class EditorCustom
        {
            [JsonProperty("key")] public string Key = "";
        }

        private sealed class EditorPicture
        {
            [JsonProperty("has")] public bool Has;
        }

        private sealed class EditorAuto
        {
            [JsonProperty("on")] public bool On;
            [JsonProperty("len")] public float Length;
            [JsonProperty("segs")] public int Segments;
            [JsonProperty("appr")] public int Approaches;
            [JsonProperty("pending")] public bool Pending;

            /// <summary>0.12.45: what was drawn at each approach, and by what state of the road (HintText.AutoEnd).</summary>
            [JsonProperty("ends")] public List<string> Ends = new List<string>();

            /// <summary>0.12.46: how many lines along on the stretch are no automatic markings (an earlier redraw's, or the player's).</summary>
            [JsonProperty("old")] public int Old;
        }

        private sealed class EditorJunction
        {
            [JsonProperty("on")] public bool On;
            [JsonProperty("note")] public string Note = "";
            [JsonProperty("notes")] public List<string> Notes = new List<string>();
        }

        private static bool Chinese()
        {
            string id = GameManager.instance?.localizationManager?.activeLocaleId ?? "en-US";
            return id.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The editor of what the tool shows (a sign picked on the map, or the draft of the one about to be placed): its title, the settings that apply to it now, and a picture of it.</summary>
        private string EditorJson()
        {
            PlacementTool tool = PlacementTool.Instance;
            if (tool == null)
                return "";
            PlacementTool.EditorView view = tool.GetEditorView();
            if (!view.Active || view.Def == null)
                return "";

            bool zh = Chinese();
            SignChoice choice = view.IsItem ? null : SignCatalog.ChoiceById(PlacementTool.SelectedId);
            var model = new EditorModel
            {
                Id = PlacementTool.EditorTarget,
                Mode = view.IsItem ? "item" : "draft",
                Code = zh ? (choice != null ? choice.Code : view.Def.Code) : (choice != null ? choice.CodeEn : CodeNames.En(view.Def.Code)),
                Title = zh ? (choice != null ? choice.Zh : view.Def.Zh) : (choice != null ? choice.En : view.Def.En),
                Preview = "",
                Plan = view.Def.IsMarking ? "" : view.PlanId ?? SupportPlanner.Auto(SignInstances.Of(view.Def, view.Spec).Def).Id
            };
            if (view.HasReach && view.Support != null)
            {
                model.Reach = view.Reach;
                model.ReachKind = view.ReachFor == SupportKind.Gantry ? "span" : view.ReachFor == SupportKind.DoubleCantilever ? "arms" : "arm";
                model.ReachMin = SpanSlider.Min(view.ReachFor);
                model.ReachMax = SpanSlider.Max(view.ReachFor);
                model.ReachStep = SpanSlider.Step;
                model.ReachHand = view.ReachByHand;
                model.Clear = view.Clearance;
                model.ClearMin = SupportLayouts.MinClearance;
                model.ClearMax = SupportLayouts.MaxClearance;
                model.ClearStep = SpanSlider.ClearanceStep;
                model.ClearHand = view.ClearanceByHand;
            }

            model.PostBoards = view.IsItem && view.PostBoards;
            model.Wall = view.IsItem ? view.Wall : 0;
            model.OnArm = view.IsItem && view.OnArm;
            if (view.IsItem && tool.TryEditedKerb(out float kerb))
                model.Kerb = kerb;
            if (view.IsItem && tool.TryEditedAlong(out float along))
                model.Along = along;
            if (view.IsItem && tool.TryEditedCentre(out float centre, out float roadHalf))
            {
                model.Centre = centre;
                model.CentreMax = SupportShift.CentreMax(roadHalf);
            }

            if (view.IsItem && tool.TryEditedWallHeight(out float lower))
                model.Lower = lower;
            model.Turns = view.Turns;
            model.Turn = model.Turns ? view.TurnDegrees : 0f;
            model.Faces = view.IsItem && view.Faces;
            model.Back = model.Faces && view.Back;
            model.Leans = view.Leans;
            model.Painted = view.Painted;
            model.Finish = (int)view.Style.Finish;
            model.BlackBack = view.Style.BlackBack;
            model.Root = (int)view.Style.Root;
            model.Rootable = view.RootPaintable;
            model.Tilt = view.Leans ? Mathf.RoundToInt(view.TiltSteps * BoardTilt.Step) : 0;
            if (view.IsItem && view.Support != null && SupportKinds.Hosted(view.Support.Kind) && tool.HostMissing(PlacementTool.EditEntity))
                model.Warn.Add(HintText.HostGone(zh));
            string refusal = HintText.Of(PlacementTool.LastRefusal, zh);
            if (view.IsItem && refusal.Length > 0)
                model.Warn.Add(refusal);
            if (view.IsItem && view.BoardList != null)
            {
                foreach (PlacementTool.BoardLine line in view.BoardList)
                    model.Boards.Add(new EditorBoard { Member = line.Member, Code = zh ? line.Code : CodeNames.En(line.Code), Title = zh ? line.Zh : line.En, Lane = line.Lane, X = line.X });
                model.Selected = view.SelectedMember;
                model.MaxBoards = view.MaxBoards > 0 ? view.MaxBoards : 4;
                model.Lane = view.SelectedLane;
                model.LaneState = view.LaneState ?? "";
                if (view.LaneCentres != null)
                    model.Lanes.AddRange(view.LaneCentres);
                if (view.Overlaps != null)
                {
                    foreach (int[] pair in view.Overlaps)
                    {
                        string a = CodeOf(view.BoardList, pair[0], zh), b = CodeOf(view.BoardList, pair[1], zh);
                        model.Warn.Add(zh ? $"{a} 与 {b} 横向重叠：换车道或改成自动" : $"{a} and {b} overlap: move one to another lane or set it to automatic");
                    }
                }
            }

            // An automatic screen (text, speed, lane) says what the road ahead says: the note shows what that is now (the editor is made again when it changes: see the key).
            if (view.IsItem && view.Def != null && VmsAuto.IsAutoScreen(view.Def.Hash, view.Spec ?? ""))
            {
                VmsAuto.Kind kind = VmsAuto.KindOf(view.Def.Hash);
                string says = VmsAuto.Describe(kind, VmsAuto.Get(PlacementTool.EditEntity.Index), zh);
                string follows = kind == VmsAuto.Kind.Speed
                    ? (zh ? "限速随路，事故拥堵时降低。现在：" : "Follows the road; lowered for a jam. Now: ")
                    : kind == VmsAuto.Kind.Lane
                        ? (zh ? "自动显示所在车道的状态。现在：" : "Shows the state of its lane. Now: ")
                        : (zh ? "内容自动跟随前方道路，畅通时关闭。现在：" : "Follows the road ahead; off when it is clear. Now: ");
                model.Warn.Add(follows + says);
            }

            model.FitWall = view.IsItem && PlacementTool.CanFitFacade(view.Def, view.Support);

            // A junction sign has the switch for reading the junction ahead and, for one that stands, the button; what the last reading said is shown under them.
            if (JunctionFill.CanRead(view.Def.Id))
            {
                model.Junction = new EditorJunction { On = PlacementTool.ReadJunctions };
                JrNote read = PlacementTool.LastReadNote;
                if (read != null)
                {
                    model.Junction.Note = read.Text(zh);
                    model.Junction.Notes.AddRange(read.Notes(zh));
                }
            }

            if (!view.IsItem && view.Def.Id == AutoMarking.Id)
            {
                PlacementTool.AutoChoice chosen = PlacementTool.AutoChosen;
                model.Auto = new EditorAuto { On = chosen.On, Length = Mathf.Round(chosen.Length), Segments = chosen.Segments, Approaches = chosen.Approaches, Pending = chosen.Pending, Old = chosen.Old };
                if (chosen.Ends != null)
                {
                    foreach (AutoEnd end in chosen.Ends)
                        model.Auto.Ends.Add(HintText.AutoEnd(end, zh));
                }
            }

            if (view.Def.Template != null && view.Def.Template.FieldOf(LampAds.PictureKey) != null)
                model.Picture = new EditorPicture { Has = CustomSigns.IsKey(view.Def.Template.Parse(view.Spec).Get(LampAds.PictureKey)) };

            // A text light box: what the font and the frame say of the text, on the sign's page (the plan is made on a worker: the editor is made again when one is, see the key).
            if (view.Def.Template != null && view.Def.Template.Id == TextBoxes.TemplateId)
            {
                foreach (string hint in TextBoxes.Hints(view.Spec, zh))
                    model.Info.Add(hint);
            }

            if (view.Def.Template != null && view.Def.Template.Id == StandBoxes.TemplateId)
            {
                foreach (string hint in StandBoxes.Hints(view.Spec, zh))
                    model.Info.Add(hint);
            }

            if (view.Def.Id == CustomSigns.TemplateId)
            {
                CustomSign saved = view.IsItem ? null : CustomSigns.ByChoiceId(PlacementTool.SelectedId);
                model.Custom = new EditorCustom { Key = saved != null ? saved.Key : "" };
            }

            GuideTemplate template = view.Def.Template;
            if (template != null)
            {
                GuideSpec spec = template.Parse(view.Spec);
                foreach (GuideField field in template.Fields)
                {
                    if ((field.Visible != null && !field.Visible(spec)) || (field.DraftOnly && view.IsItem))
                        continue;
                    var f = new EditorField { Key = field.Key, Label = zh ? field.Zh : field.En, Value = spec.Get(field.Key), Max = field.Clusters ? 0 : field.MaxLength, Wide = field.Wide, Section = zh ? field.SectionZh : field.SectionEn };
                    f.Kind = field.Kind == GuideFieldKind.Choice ? "choice" : field.Kind == GuideFieldKind.Flag ? "flag" : "text";
                    if (field.HasSlider)
                        f.Range = new[] { field.SliderMin, field.SliderMax, field.SliderStep };
                    if (field.Kind == GuideFieldKind.Choice)
                    {
                        f.Choices = new List<EditorChoice>();
                        foreach (GuideChoice c in field.Choices)
                        {
                            // "Automatic" is the draft's: a board that stands in the world has its band.
                            if (c.Value == "auto" && view.IsItem)
                                continue;
                            f.Choices.Add(new EditorChoice { Value = c.Value, Label = zh ? c.Zh : c.En });
                        }
                    }

                    model.Fields.Add(f);
                }

                // A road marking: its picture from above and the measures of its paint; there is no support to show.
                if (view.Def.IsMarking)
                {
                    AddMarkingEditor(model, view, spec, zh);
                    return JsonConvert.SerializeObject(model);
                }

                // The board laid out and its picture made on a worker when its settings change (the thumbnail folder is the UI's host of pictures); until they are there the one before is shown, at its
                // own size, and its measures (0.12.63, 0.12.64: the layout was made here, 8 to 85 ms).
                // The line settings (SignLine) are not of the picture: a change of them does not lay the board out again.
                string text = SignLine.Stripped(view.Def, spec.ToString());
                string requestKey = view.Def.Id + "|" + text;
                if (requestKey != m_PreviewKey)
                {
                    m_PreviewKey = requestKey;
                    if (!m_PreviewIsBoard)
                    {
                        m_PreviewIsBoard = true;
                        m_PreviewUrl = "";
                        m_PreviewW = m_PreviewH = 0;
                    }

                    ThumbnailStore.RequestEditorPreview(view.Def, text, requestKey);
                }

                SignInstance instance = SignInstances.Cached(view.Def, text);
                if (instance != null)
                {
                    m_LastInstance = instance;
                    m_LastInstanceKind = view.Def.Id;
                }
                else if (m_LastInstanceKind == view.Def.Id)
                {
                    instance = m_LastInstance;
                }

                model.Preview = m_PreviewUrl;
                model.PictureWidth = m_PreviewW;
                model.PictureHeight = m_PreviewH;
                if (instance != null)
                    AddInfo(model, instance, view, spec, zh);
            }

            return JsonConvert.SerializeObject(model);
        }

        private int m_MarkingPreviewW, m_MarkingPreviewH;

        /// <summary>The picture in the editor is a board's (made on a worker) and its size; the picture of a marking is made at once.</summary>
        private bool m_PreviewIsBoard;
        private int m_PreviewW, m_PreviewH;

        /// <summary>The board last laid out for the editor and the kind of sign it is of: its measures stand in while the worker lays out the next.</summary>
        private SignInstance m_LastInstance;
        private string m_LastInstanceKind = "";

        /// <summary>The tools that work on a stretch of road (the automatic marking, the clearing of the game's markings, the automatic barriers): their picture and measures are no item's.</summary>
        private static bool IsBrush(string id) => id == AutoMarking.Id || id == VanillaClear.Id || id == BarrierAuto.Id;

        /// <summary>
        /// The editor of a road marking (GB 5768.3): the paint seen from above, made again when its settings change, its measures, and what it is; a tool that works on a stretch (IsBrush) has its note
        /// only (0.12.47: the owner, "去掉调试性内容，精简文字说明").
        /// </summary>
        private void AddMarkingEditor(EditorModel model, PlacementTool.EditorView view, GuideSpec spec, bool zh)
        {
            model.Marking = true;
            string note = view.Def.Template.MarkingNote?.Invoke(spec, zh);
            if (IsBrush(view.Def.Id))
            {
                if (!string.IsNullOrEmpty(note))
                    model.Info.Add(note);
                return;
            }

            string text = spec.ToString();
            MarkingPlan plan = MarkingTemplates.PlanOf(view.Def, text);
            string key = "mark|" + view.Def.Id + "|" + text + "|" + MarkingLetters.Version;
            if (key != m_PreviewKey)
            {
                m_PreviewKey = key;
                m_PreviewIsBoard = false;
                m_PreviewUrl = ThumbnailStore.MarkingPreview(plan, out m_MarkingPreviewW, out m_MarkingPreviewH);
            }

            model.Preview = m_PreviewUrl;
            model.PictureWidth = m_MarkingPreviewW;
            model.PictureHeight = m_MarkingPreviewH;
            if (plan != null && plan.Parts.Count > 0)
            {
                // A marking bent along the road (0.12.49) is measured as it is made, straight.
                bool bent = plan.Bend != null;
                float across = bent ? plan.StraightMaxX - plan.StraightMinX : plan.MaxX - plan.MinX, along = bent ? plan.StraightMaxZ - plan.StraightMinZ : plan.MaxZ - plan.MinZ;
                // Paint on an upright surface (the elevation marking) is measured across and up the wall, not along the road.
                bool upright = plan.Parts.TrueForAll(p => p.Upright);
                if (plan.HasSolids)
                {
                    // A road barrier: its length along the line and its height.
                    float high = 0f;
                    foreach (MarkingPart part in plan.Parts)
                    {
                        foreach (CDLC.TS.Snap.Vec3 p in part.Mesh.Positions)
                            high = System.Math.Max(high, p.Y);
                    }

                    model.Info.Add(zh ? $"长 {along:0.0} m，高 {high:0.00} m" : $"{along:0.0} m long, {high:0.00} m high");
                }
                else if (upright)
                {
                    float up = 0f;
                    foreach (MarkingPart part in plan.Parts)
                    {
                        foreach (CDLC.TS.Snap.Vec3 p in part.Mesh.Positions)
                            up = System.Math.Max(up, p.Y);
                    }

                    model.Info.Add(zh ? $"立面 宽 {across:0.0} m × 高 {up:0.0} m" : $"{across:0.0} m wide x {up:0.0} m high");
                }
                else
                {
                    model.Info.Add(zh ? $"漆面 {across:0.0} m（横）× {along:0.0} m（纵）" : $"Paint {across:0.0} m across x {along:0.0} m along");
                }
            }

            if (!string.IsNullOrEmpty(note))
                model.Info.Add(note);
        }

        private static string CodeOf(List<PlacementTool.BoardLine> lines, int member, bool zh)
        {
            foreach (PlacementTool.BoardLine line in lines)
            {
                if (line.Member == member)
                    return zh ? line.Code : CodeNames.En(line.Code);
            }

            return "?";
        }

        /// <summary>The measures of the board and its support, in the player's language: what it is in metres and centimetres, so that its size can be judged against the road.</summary>
        private static void AddInfo(EditorModel model, SignInstance instance, PlacementTool.EditorView view, GuideSpec spec, bool zh)
        {
            GuideBoard board = instance.Board;
            GuidePart main = board.Main;
            bool auto = spec.Is("speed", "auto");
            model.Info.Add(zh ? $"牌面 {main.W:0.00} × {main.H:0.00} m" + (board.Parts.Count > 1 ? $"，连编号牌总高 {instance.BoundsY1 - instance.BoundsY0:0.00} m" : "")
                              : $"Board {main.W:0.00} x {main.H:0.00} m" + (board.Parts.Count > 1 ? $", {instance.BoundsY1 - instance.BoundsY0:0.00} m with the number plate" : ""));
            if (spec.Template.FieldOf("speed") != null)
            {
                string band = spec.Get("speed");
                model.Info.Add(auto
                    ? (zh ? "字高：按所在道路限速取档" : "Character height: by the road's speed limit")
                    : (zh ? $"字高 {board.H * 100f:0} cm（{BandName(band)}）" : $"Character height {board.H * 100f:0} cm ({BandName(band)})"));
            }

            foreach (string note in Bilingual.Notes(board.English, zh))
                model.Info.Add(note);
            SupportDef support = view.Support ?? SupportCatalog.For(instance.Def, view.SupportHash);
            SupportLayout layout = view.Support != null ? view.Layout : SupportLayouts.Of(instance.Def, support, view.Reach);
            // One compact line of what the player reads while deciding: the kind, the post, the clearance, the arm or span (how many boards there are is the boards' own heading).
            string span = support.Kind == SupportKind.Gantry ? (zh ? $" · 跨度 {layout.ArmLength:0.0} m · 梁高 {layout.BeamDepth:0.0} m" : $" · span {layout.ArmLength:0.0} m · beam {layout.BeamDepth:0.0} m")
                : support.Kind == SupportKind.FCantilever ? (zh ? $" · 双臂 {layout.ArmLength:0.0} m · 相距 {layout.ArmYs[0] - layout.ArmYs[1]:0.00} m" : $" · two arms {layout.ArmLength:0.0} m · {layout.ArmYs[0] - layout.ArmYs[1]:0.00} m apart")
                : SupportKinds.OneWayArm(support.Kind) ? (zh ? $" · 臂 {layout.ArmLength:0.0} m" : $" · arm {layout.ArmLength:0.0} m")
                : support.Kind == SupportKind.DoubleCantilever ? (zh ? $" · 每臂 {layout.ArmLength:0.0} m" : $" · {layout.ArmLength:0.0} m each arm") : "";
            if (SupportKinds.OnWall(support.Kind))
                model.SupportInfo.Add(zh ? $"{support.Zh} · 离地 {layout.BottomClearance:0.0} m" + (support.Kind == SupportKind.WallBracket ? $" · 伸出 {layout.ArmLength:0.00} m" : $" · 离墙 {SupportLayouts.WallGap * 100f:0} cm")
                                         : $"{support.En} · {layout.BottomClearance:0.0} m up" + (support.Kind == SupportKind.WallBracket ? $" · {layout.ArmLength:0.00} m out" : $" · {SupportLayouts.WallGap * 100f:0} cm off the wall"));
            else
                model.SupportInfo.Add(zh ? $"{support.Zh} · 立柱 Φ{layout.PoleRadius * 2000f:0} · 离地 {layout.BottomClearance:0.0} m{span}"
                                         : $"{support.En} · post Ø{layout.PoleRadius * 2000f:0} · {layout.BottomClearance:0.0} m up{span}");
            model.Hung = SupportKinds.Hung(support.Kind);
        }

        private static string BandName(string band) => band == "100" ? "100–120 km/h" : band == "80" ? "71–99 km/h" : band == "60" ? "40–70 km/h" : band == "30" ? "<40 km/h" : band;

        /// <summary>The items with no road in reach.</summary>
        private int CountOrphans()
        {
            if (m_ItemQuery.IsEmptyIgnoreFilter)
                return 0;
            int count = 0;
            using (var items = m_ItemQuery.ToComponentDataArray<TsItem>(Unity.Collections.Allocator.Temp))
            {
                foreach (TsItem item in items)
                {
                    if (item.Orphan)
                        count++;
                }
            }

            return count;
        }

        /// <summary>The panel's button (after its confirmation): every item with no road in reach is destroyed.</summary>
        private void DeleteOrphans()
        {
            var doomed = new List<Entity>();
            using (var entities = m_ItemQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
            using (var items = m_ItemQuery.ToComponentDataArray<TsItem>(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (items[i].Orphan)
                        doomed.Add(entities[i]);
                }
            }

            // As the tool's own deletions: Ctrl+Z brings them back.
            foreach (Entity e in doomed)
            {
                if (PlacementTool.Instance != null)
                    PlacementTool.Instance.DestroyItem(e);
                else
                    EntityManager.DestroyEntity(e);
            }

            Mod.log.Info($"Panel: deleted {doomed.Count} item(s) with no road in reach (of {doomed.Count + m_ItemQuery.CalculateEntityCount()}).");
            m_Orphans.Update(0);
        }

        [Preserve]
        /// <summary>The figures of the memory panel, every FootprintSeconds while it is open: the renderer's tiles and items, the pictures by detail, the fixed textures, the kept pictures, the card's memory.</summary>
        private void UpdateFootprint()
        {
            if (!m_FootprintWatched || ChunkSystem.Instance == null)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - m_FootprintAt < kFootprintSeconds)
                return;
            m_FootprintAt = now;
            FootprintInput input = m_FootprintInput;
            ChunkSystem.Instance.CollectFootprint(input);
            TsMaterials.FaceBytesByDetail(m_FaceBytes);
            input.FaceFull = m_FaceBytes[FaceDetail.Full];
            input.FaceHalf = m_FaceBytes[FaceDetail.Half];
            input.FaceSmall = m_FaceBytes[FaceDetail.Small];
            input.DetailBytes = TsMaterials.FixedTextureBytes();
            input.PictureBytes = CustomPictures.KeptBytes();
            input.VramBytes = SystemInfo.graphicsMemorySize * 1024L * 1024L;
            m_Footprint.Update(Footprint.Build(input).ToJson());
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            ThumbnailStore.EnsureHost();
            if (!m_ThumbsPublished && ThumbnailStore.Ready)
            {
                m_ThumbsPublished = true;
                m_CustomDirty = true;
                m_Catalog.Update(CatalogJson());
                m_Supports.Update(SupportsJson());
            }

            if (m_HostsListed != TsExtension.ProvidesHosts)
            {
                m_HostsListed = !m_HostsListed;
                m_Supports.Update(SupportsJson());
            }

            // The saved signs changed (one saved, changed or deleted): the catalogue lists them.
            if (m_CustomVersion != CustomSigns.Version)
            {
                m_CustomVersion = CustomSigns.Version;
                m_Catalog.Update(CatalogJson());
            }

            if (m_CustomDirty)
            {
                m_CustomDirty = false;
                m_Custom.Update(CustomJson());
            }

            m_Category.Update(ToolbarTabSystem.CategoryEntity);
            m_Status.Update(ToolbarTabSystem.Status);
            m_Selected.Update(PlacementTool.SelectedId ?? "");
            m_Support.Update(PlacementTool.EffectiveSupportId() ?? "");
            m_Mode.Update(PlacementTool.Mode);
            m_Margin.Update(PlacementTool.Margin);
            m_Mid.Update(PlacementTool.Mid);
            UpdateProfileBindings();
            m_ToolActive.Update(PlacementTool.IsActive);
            // The line of help, whether it is a refusal (the panel colours it) and the keys of the tool now (0.12.47, the keys' own panel): {"t":"...","bad":true,"k":[["key","what it does"]]}. Made
            // again only when what it says changed.
            bool zh = Chinese();
            m_Lang.Update(zh ? "zh" : "en");
            HintInfo hint = PlacementTool.Hint;
            bool active = PlacementTool.IsActive;
            if (!m_HintHave || m_HintSeenZh != zh || !m_HintSeen.Equals(hint) || m_HintActive != active)
            {
                m_HintHave = true;
                m_HintSeenZh = zh;
                m_HintSeen = hint;
                m_HintActive = active;
                m_Hint.Update(active ? JsonConvert.SerializeObject(new { t = HintText.Of(hint, zh), bad = hint.Bad, k = HintText.Keys(hint, zh) }) : "");
            }

            UpdateFootprint();
            m_Night.Update(Mathf.RoundToInt(TsMaterials.NightNits * 10f));
            m_NightGain.Update(Mathf.RoundToInt(TsMaterials.NightGain * 10f));
            m_LedDay.Update(Mathf.RoundToInt(TsMaterials.LedDayNits / 100f));
            m_LedNight.Update(Mathf.RoundToInt(TsMaterials.LedNightNits));
            m_Detail.Update(TsMaterials.DetailOn);
            m_LampLit.Update(Mathf.RoundToInt(TsMaterials.LitNits));
            m_LampGlow.Update(Mathf.RoundToInt(TsMaterials.GlowNits));

            // The picture of the board that the worker made: the editor shows it from the next build.
            if (m_PreviewIsBoard && ThumbnailStore.TryTakeEditorPreview(m_PreviewKey, out string madeUrl, out int madeW, out int madeH))
            {
                m_PreviewUrl = madeUrl;
                m_PreviewW = madeW;
                m_PreviewH = madeH;
                m_EditorKeyHave = false;
            }

            // The editor is rebuilt only when what it shows changed (a setting, the support, another sign, the language).
            var editorKey = (PlacementTool.Mode, PlacementTool.SelectedId, PlacementTool.EditorTarget, PlacementTool.EditorRevision, PlacementTool.ReadNoteRevision + VmsAuto.Changes * 1000, TextPlans.Epoch, active, zh);
            if (!m_EditorKeyHave || !m_EditorKey.Equals(editorKey))
            {
                m_EditorKeyHave = true;
                m_EditorKey = editorKey;
                m_Editor.Update(active ? EditorJson() : "");
            }

            // At once: a road placed or deleted goes from the list before it can be pressed again.
            SaveDataSystem store = SaveDataSystem.Instance;
            if (store != null && store.Revision != m_SaveDataRevision)
            {
                m_SaveDataRevision = store.Revision;
                m_SaveData.Update(store.Json());
            }

            if (++m_Frame % 30 == 0)
            {
                m_ClearAreas.Update(ChunkSystem.Instance != null ? ChunkSystem.Instance.ClearAreaCount : 0);
                m_ClearHidden.Update(ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.HiddenNow : 0);

                // Counted while the tool is open (the save data's section shows it), and once after a load for the log.
                if (active)
                    m_Orphans.Update(CountOrphans());
            }
        }
    }
}
