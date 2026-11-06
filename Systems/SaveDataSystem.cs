using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Routes;
using Game.SceneFlow;
using Game.Serialization;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// The save-safe store (0.12.38, DESIGN-save-data.md; the owner: "我在存档中的摆放的TrafficSigns相关数据位置等等独立存储，这样我取消订阅或者过期，直接不加载，不会炸档，然后重新正确mod安装后还可以恢复，
    /// 如果期间修改了道路信息则取消这个道路的匹配"). The items carry LivePath, which the game's serializer leaves out, so a save holds none of them; when the game writes a save (SaveStripSystem, in its
    /// Serialize phase) their state is copied with the roads they stand at, the kerb styles' bits and the tags of the hidden markings are taken out for the writing (and given back after it,
    /// SaveRestoreSystem), and when the save is written the copy goes to a file of its own (SaveStore: a folder for the city, the game's sessionGuid; a file for the save, its SaveGameData's guid).
    /// When a save is loaded, its record (or, when it has none, the city's record that fits its roads best, if 95 % of its roads are the same) is put back: the items of a road that is no longer as
    /// it was are put away (not drawn) for the panel to place or delete. A save made before keeps its items (TsItem is still read) and moves them to the store at its next save. Main thread.
    /// </summary>
    internal sealed partial class SaveDataSystem : GameSystemBase
    {
        /// <summary>A record restored without asking when this share of its roads is the same (the owner: "吻合度很高时自动恢复").</summary>
        private const float kAutoScore = 0.95f;

        /// <summary>The frames after a load before the record is put back: the game connects the roads (ConnectedEdge) in its first updates.</summary>
        private const int kSettleFrames = 8;

        /// <summary>
        /// Under the loading screen (0.12.78) the restore waits for the roads' links too, and goes on without them after this many frames: it is then as late as the old rule was at the latest. The game
        /// builds the links in the Deserialize phase (ConnectedEdgeSystem), so the wait is a safety, not the usual case.
        /// </summary>
        private const int kEarlyGiveUpFrames = 120;

        /// <summary>This load began its restore at the end of the deserialization, under the loading screen (LoadWarmSystem.Early).</summary>
        private bool m_Early;

        /// <summary>The records of the city looked at when the save has none of its own, newest first.</summary>
        private const int kCandidates = 6;

        internal static SaveDataSystem Instance { get; private set; }

        internal static string Root => Path.Combine(Application.persistentDataPath, "ModsData", Mod.kModName, "Saves");

        private PrefabSystem m_Prefabs;
        private SaveGameSystem m_SaveGame;
        private EntityQuery m_Items, m_Untagged, m_Edges;
        private GameManager.EventGameSaveLoad m_OnSave;

        /// <summary>The game raises its save events from the thread its async Save continues on, which is the main thread when it was started there (all its callers do): checked at the first, and any that comes elsewhere is kept for this system's update.</summary>
        private MainThreadGate<(string Name, string Preview, bool Start, bool Success)> m_SaveEvents;
        private bool m_ThreadLogged, m_SerializerLogged;

        /// <summary>What the save being written holds of the mod, from SaveStripSystem until the game says it is written.</summary>
        private SaveRecord m_Writing;

        /// <summary>The save the game started (its event), whether this mod's step before the writing ran in it (BeforeWrite), and whether that step put the record off to the end of the save.</summary>
        private bool m_SaveStarted, m_Prepared, m_Deferred;

        /// <summary>
        /// What the game's saving or loading did that this store does not expect (0.12.51; the owner: "加检测"), each with a count, for the panel until another city is loaded: late (a save's record
        /// made after the writing), nosave (a save with no record), unreported (a save the game never said was done), leak (a save that held this mod's items), plants (the greening's plants came
        /// out of a save).
        /// </summary>
        private readonly Dictionary<string, int> m_Warnings = new Dictionary<string, int>();

        /// <summary>The kerb styles' bits and the markings' tags taken out for the writing, to give back.</summary>
        private readonly List<(Entity Edge, Upgraded Flags)> m_KerbsTaken = new List<(Entity, Upgraded)>();
        private List<Entity> m_TagsTaken;

        /// <summary>What was put away when a record was restored (the unmatched roads' items and kerb styles), its anchors its own; written into every save until placed or deleted.</summary>
        private SaveRecord m_Away = new SaveRecord();

        /// <summary>An extension's section is no larger than this (bytes): a larger one is not written, and its last bytes are kept.</summary>
        internal const int MaxSection = 4 << 20;

        /// <summary>
        /// The extensions' sections of the record restored (TsExtension.Sections): with whether the extension took its own (Taken, or Refused: its bytes are kept as they are at the next saves). A
        /// section of an id no extension registered is kept as it is.
        /// </summary>
        private readonly Dictionary<string, (StoredSection Section, bool Given, bool Taken)> m_Sections = new Dictionary<string, (StoredSection, bool, bool)>();

        /// <summary>The records of the city offered in the panel when none fitted well enough to restore without asking.</summary>
        private readonly List<(string Path, SaveRecord Header, float Score)> m_Offers = new List<(string, SaveRecord, float)>();

        private int m_LoadFrames = -1;
        private string m_LoadedSave = "", m_LoadedCity = "";

        /// <summary>When the save just loaded was written (UTC ticks, the game's last write time of it), 0 when it was not read.</summary>
        private long m_LoadedWritten;
        private int m_Revision;

        /// <summary>What the store did last, for the panel and the log.</summary>
        internal string Status { get; private set; } = "";

        /// <summary>Changes whenever what the panel shows changes.</summary>
        internal int Revision => m_Revision;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SaveGame = World.GetOrCreateSystemManaged<SaveGameSystem>();
            m_Items = GetEntityQuery(ComponentType.ReadOnly<TsItem>());
            m_Untagged = GetEntityQuery(ComponentType.ReadOnly<TsItem>(), ComponentType.Exclude<LivePath>());
            m_Edges = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Road>(), ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Game.Tools.Temp>());
            m_SaveEvents = new MainThreadGate<(string, string, bool, bool)>(Environment.CurrentManagedThreadId);
            m_OnSave = OnSaveEventFromGame;
            GameManager.instance.onGameSaveLoad += m_OnSave;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (GameManager.instance != null)
                GameManager.instance.onGameSaveLoad -= m_OnSave;
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_Early = LoadWarmSystem.Early(serializationContext);
            if (m_Early)
                BeginLoad(Purpose.LoadGame, GameMode.Game);
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (m_Early)
            {
                // Begun at the end of the deserialization (and maybe done): what was put back is not forgotten now; a restore not done yet goes on by the old rule.
                m_Early = false;
                return;
            }

            BeginLoad(purpose, mode);
        }

        /// <summary>What a saved game's load begins with: the state of the last city is let go, the save's own ids are read, and the restore is counted in frames.</summary>
        private void BeginLoad(Purpose purpose, GameMode mode)
        {
            m_Warnings.Clear();
            if (m_Writing != null)
                Unreported("before another city was loaded");
            m_SaveStarted = m_Prepared = m_Deferred = false;
            while (m_SaveEvents.TryTake(out _))
            {
            }

            // What a save took out of the roads belongs to the city that is left: the next world reuses entity indices.
            m_KerbsTaken.Clear();
            m_TagsTaken = null;
            m_Away = new SaveRecord();
            m_Offers.Clear();
            m_Sections.Clear();
            m_LoadFrames = -1;
            m_LoadedSave = m_LoadedCity = "";
            m_LoadedWritten = 0;
            Status = "";
            m_Revision++;
            if (mode != GameMode.Game || purpose != Purpose.LoadGame)
                return;
            ReadLoaded(out m_LoadedSave, out m_LoadedCity, out m_LoadedWritten);
            m_LoadFrames = 0;
        }

        /// <summary>Whether the links of the roads (ConnectedEdge at both ends) are there, from a sample of the roads.</summary>
        private bool RoadsLinked()
        {
            using (NativeArray<Entity> edges = m_Edges.ToEntityArray(Allocator.Temp))
            {
                int[] pick = EarlyRestore.SampleIndices(edges.Length, EarlyRestore.SampleMax);
                int linked = 0;
                foreach (int index in pick)
                {
                    Entity edge = edges[index];
                    if (EntityManager.TryGetComponent(edge, out Edge ends) && Links(ends.m_Start, edge) && Links(ends.m_End, edge))
                        linked++;
                }

                return EarlyRestore.Linked(pick.Length, linked);
            }
        }

        private bool Links(Entity node, Entity edge)
        {
            if (!EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> links))
                return false;
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i].m_Edge == edge)
                    return true;
            }

            return false;
        }

        /// <summary>The save just loaded: its own id (the guid of its SaveGameData, reached from the metadata the game loaded it by), its city's id (the sessionGuid the game keeps in every save) and when it was written.</summary>
        private void ReadLoaded(out string save, out string city, out long written)
        {
            save = city = "";
            written = 0;
            try
            {
                Colossal.Hash128 instigator = World.GetOrCreateSystemManaged<LoadGameSystem>().context.instigatorGuid;
                if (Colossal.IO.AssetDatabase.AssetDatabase.global.TryGetAsset(instigator, out Colossal.IO.AssetDatabase.IAssetData asset) && asset is Game.Assets.SaveGameMetadata meta)
                {
                    Game.Assets.SaveInfo info = meta.target;
                    city = info.sessionGuid.ToString();
                    if (info.saveGameData != null)
                        save = info.saveGameData.id.guid.ToString();
                    if (info.lastModified.Ticks > 0)
                        written = info.lastModified.ToUniversalTime().Ticks;
                }

                Mod.log.Info($"Save data: loaded save {(save.Length > 0 ? save : "(its id not found)")} of city {(city.Length > 0 ? city : "(not found)")} (metadata {instigator}).");
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Save data: the loaded save's ids could not be read: " + ex.Message);
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance.gameMode != GameMode.Game)
                return;
            while (m_SaveEvents.TryTake(out (string Name, string Preview, bool Start, bool Success) late))
                OnSaveEvent(late.Name, late.Preview, late.Start, late.Success);
            if (!m_Untagged.IsEmptyIgnoreFilter)
                EntityManager.AddComponent<LivePath>(m_Untagged);
            if (m_LoadFrames < 0)
                return;
            m_LoadFrames++;
            if (!EarlyRestore.Ready(m_LoadFrames, kSettleFrames, m_Early ? kEarlyGiveUpFrames : kSettleFrames, m_Early ? RoadsLinked : (Func<bool>)null))
                return;
            m_LoadFrames = -1;
            try
            {
                AfterLoad();
            }
            catch (Exception ex)
            {
                Status = "error: " + ex.Message;
                Mod.log.Error("Save data: the record could not be put back: " + ex);
            }

            m_Revision++;
            try
            {
                LoadWarmSystem.Instance?.ItemsRestored(m_Items.CalculateEntityCount());
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Load: the first-time work could not be told that the items are back: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ loading

        private void AfterLoad()
        {
            List<(string Path, SaveRecord Header)> records = m_LoadedCity.Length == 0 ? new List<(string, SaveRecord)>() : SaveStore.Headers(SaveStore.FolderOf(Root, m_LoadedCity));
            string ownPath = m_LoadedSave.Length == 0 ? null : records.Find(r => r.Header.SaveId == m_LoadedSave).Path;
            int own = m_Items.CalculateEntityCount();
            if (own > 0)
            {
                if (ownPath == null)
                {
                    Status = $"legacy:{own}";
                    Mod.log.Info($"Save data: the save holds {own} item(s) of its own (made before 0.12.38): they are kept, and the next save moves them to the store.");
                    return;
                }

                // A save that has a record of its own holds no item (LivePath leaves them out of it): this one was written by a game whose serializer no longer does. The record is of the same moment
                // and has what the save has not (the kerb styles, the items put away, the extensions' sections): the items the save brought are taken out, and the record is put back.
                using (NativeArray<Entity> leaked = m_Items.ToEntityArray(Allocator.Temp))
                {
                    EntityManager.DestroyEntity(m_Items);
                    ChunkSystem.Instance?.Forget(new List<Entity>(leaked));
                }

                Warn("leak", own);
                Mod.log.Warn($"Save data: the save holds {own} item(s) although it has a record of its own: the game wrote this mod's items into it (its serializer no longer leaves out LivePath?). "
                    + "They are taken out and the record put back; the saves made now hold this mod's items.");
                Restore(SaveStore.Load(ownPath), Roads(), "its own record, in place of the items the save held");
                return;
            }

            if (m_LoadedCity.Length == 0)
            {
                Status = "none";
                return;
            }

            if (records.Count == 0)
            {
                Status = "none";
                Mod.log.Info("Save data: no record of this city.");
                return;
            }

            Dictionary<long, List<(Entity Edge, RoadAnchor Anchor)>> roads = Roads();
            if (ownPath != null)
            {
                Restore(SaveStore.Load(ownPath), roads, "its own record");
                return;
            }

            // No record of this save (written while the mod was off, or lost): the city's newest ones, scored by their roads. A city the game gave no id (a save from before it had them) shares the
            // empty one with others: only the records of its name are looked at.
            string cityName = World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().cityName ?? "";
            bool noId = m_LoadedCity == Guid.Empty.ToString();
            // Only the records made no later than this save (0.12.102): the items of a save made after it are not in it, and every save of one city has the same roads, so the record of the newest
            // save fitted any of them: the signs edited in a save named afresh came back in the city's other saves, the ones that had no record.
            List<(string Path, SaveRecord Header)> ofTheCity = records.FindAll(r => !noId || r.Header.CityName == cityName);
            List<(string Path, SaveRecord Header)> earlier = SaveStore.Ancestors(ofTheCity, m_LoadedWritten, out int later);
            if (later > 0)
            {
                Mod.log.Info(m_LoadedWritten > 0
                    ? $"Save data: no record of this save (written {Time(m_LoadedWritten)}); {later} of the city's {ofTheCity.Count} record(s) were made after it and cannot hold its items: {(earlier.Count > 0 ? $"the {earlier.Count} before it are looked at" : "none is put back")} (newest {Time(ofTheCity[0].Header.Time)})."
                    : $"Save data: no record of this save, and when it was written is not known: none of the city's {ofTheCity.Count} record(s) is put back (the panel does not offer them either).");
            }

            var anchored = new HashSet<string>();
            for (int n = 0; n < earlier.Count && n < kCandidates; n++)
            {
                SaveRecord record = SaveStore.Load(earlier[n].Path);
                m_Offers.Add((earlier[n].Path, earlier[n].Header, record.Score(a => Match(a, roads, out _))));
                if (record.Items.Exists(i => i.Anchor >= 0 && i.Unmatched == AnchorChange.Same) || record.Kerbs.Exists(k => k.Unmatched == AnchorChange.Same))
                    anchored.Add(earlier[n].Path);
            }

            if (m_Offers.Count == 0)
            {
                Status = "none";
                return;
            }

            m_Offers.Sort((a, b) => b.Score.CompareTo(a.Score));
            Mod.log.Info("Save data: no record of this save; the city's: " + string.Join(", ", m_Offers.ConvertAll(o => $"\"{o.Header.SaveName}\" {Time(o.Header.Time)} {o.Score:P0}")));
            // Restored without asking only when it fits (the owner: "吻合度很高时自动恢复") and its fit was measured on roads: one whose items stand at no road fits any city.
            if (m_Offers[0].Score >= kAutoScore && anchored.Contains(m_Offers[0].Path))
            {
                (string path, SaveRecord header, float score) = m_Offers[0];
                m_Offers.Clear();
                Restore(SaveStore.Load(path), roads, $"the record of \"{header.SaveName}\" ({Time(header.Time)}, {score:P0} of its roads the same)");
                return;
            }

            Status = "offers";
        }

        /// <summary>The panel's choice of a record offered: restored as if it were the save's own.</summary>
        internal void RestoreOffer(int index)
        {
            if (index < 0 || index >= m_Offers.Count)
                return;
            (string path, SaveRecord header, float score) = m_Offers[index];
            m_Offers.Clear();
            try
            {
                Restore(SaveStore.Load(path), Roads(), $"the record of \"{header.SaveName}\" ({Time(header.Time)}, {score:P0} of its roads the same), chosen in the panel");
            }
            catch (Exception ex)
            {
                Status = "error: " + ex.Message;
                Mod.log.Error("Save data: the record chosen could not be put back: " + ex);
            }

            m_Revision++;
        }

        internal void IgnoreOffers()
        {
            m_Offers.Clear();
            Status = "ignored";
            m_Revision++;
        }

        /// <summary>
        /// Puts a record back: the items and kerb styles of the roads that are as they were, and of no road, are made again; those of a road that changed are kept in m_Away. One put away before is
        /// placed when its road is as it was when it was placed (it was put back as it had been, or the reason was wrong: 0.12.38 wrote another prefab's name for every road).
        /// </summary>
        private void Restore(SaveRecord record, Dictionary<long, List<(Entity Edge, RoadAnchor Anchor)>> roads, string which)
        {
            var clock = Stopwatch.StartNew();
            var edges = new Entity[record.Anchors.Count];
            var change = new AnchorChange[record.Anchors.Count];
            for (int a = 0; a < record.Anchors.Count; a++)
                change[a] = Match(record.Anchors[a], roads, out edges[a]);

            var away = new SaveRecord();
            var awayIndex = new Dictionary<int, int>();
            int AwayAnchor(int a)
            {
                if (a < 0)
                    return -1;
                if (!awayIndex.TryGetValue(a, out int i))
                {
                    awayIndex[a] = i = away.Anchors.Count;
                    away.Anchors.Add(record.Anchors[a]);
                }

                return i;
            }

            int placed = 0, kerbs = 0;
            foreach (StoredItem item in record.Items)
            {
                AnchorChange why = item.Anchor >= 0 ? change[item.Anchor] : AnchorChange.Same;
                if (why != AnchorChange.Same)
                {
                    item.Unmatched = why;
                    item.Anchor = AwayAnchor(item.Anchor);
                    away.Items.Add(item);
                    continue;
                }

                Create(item, item.Anchor >= 0 ? edges[item.Anchor] : Entity.Null);
                placed++;
            }

            foreach (StoredKerb kerb in record.Kerbs)
            {
                AnchorChange why = change[kerb.Anchor];
                if (why != AnchorChange.Same)
                {
                    kerb.Unmatched = why;
                    kerb.Anchor = AwayAnchor(kerb.Anchor);
                    away.Kerbs.Add(kerb);
                    continue;
                }

                SetKerb(edges[kerb.Anchor], kerb.Left, kerb.Right);
                kerbs++;
            }

            m_Away = away;
            m_Sections.Clear();
            foreach (KeyValuePair<string, StoredSection> pair in record.Sections)
                m_Sections[pair.Key] = (pair.Value, false, false);
            foreach (string id in new List<string>(m_Sections.Keys))
                GiveSection(id);
            Status = $"restored:{placed}:{away.Items.Count}:{AwayRoads()}";
            Mod.log.Info($"Save data: restored {which}: {placed} item(s) and {kerbs} kerb style(s) placed; {away.Items.Count} item(s) and {away.Kerbs.Count} kerb style(s) of {AwayRoads()} changed road(s) put away"
                + (away.Anchors.Count > 0 ? " (" + Reasons(away) + ")" : "") + $"; {m_RoadsMs + clock.Elapsed.TotalMilliseconds:0.0} ms in one update (the roads read {m_RoadsMs:0.0}).");
        }

        /// <summary>An extension registered its section (TsExtension.RegisterSection): a record restored before it did gives it its section now.</summary>
        internal void SectionRegistered(string id)
        {
            if (m_Sections.ContainsKey(id))
                GiveSection(id);
        }

        /// <summary>Gives a restored section to its extension, once (none registered: kept for later).</summary>
        private void GiveSection(string id)
        {
            (StoredSection section, bool given, bool _) = m_Sections[id];
            if (given || !TsExtension.Sections.TryGetValue(id, out TsExtension.Section extension))
                return;
            bool taken = false;
            try
            {
                taken = extension.Load(section.Data, section.Version);
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Save data: the extension's section \"{id}\" (version {section.Version}, {section.Data.Length} bytes) could not be read by it: {ex}");
            }

            m_Sections[id] = (section, true, taken);
            Mod.log.Info($"Save data: section \"{id}\" (version {section.Version}, {section.Data.Length} bytes) {(taken ? "taken by its extension" : "refused by its extension: its bytes are kept as they are")}.");
        }

        /// <summary>
        /// The extensions' sections of the save being written: each registered one's own bytes (SaveSection), but the bytes it refused or was not given kept as they are; those of ids no extension
        /// registered kept.
        /// </summary>
        private void AddSections(SaveRecord record)
        {
            foreach (KeyValuePair<string, (StoredSection Section, bool Given, bool Taken)> pair in m_Sections)
            {
                if (!pair.Value.Taken)
                    record.Sections[pair.Key] = pair.Value.Section;
            }

            foreach (KeyValuePair<string, TsExtension.Section> pair in TsExtension.Sections)
            {
                if (m_Sections.TryGetValue(pair.Key, out (StoredSection Section, bool Given, bool Taken) kept) && !kept.Taken)
                    continue;
                byte[] bytes = null;
                try
                {
                    bytes = pair.Value.Save();
                }
                catch (Exception ex)
                {
                    bool last = m_Sections.TryGetValue(pair.Key, out (StoredSection Section, bool Given, bool Taken) restored);
                    if (last)
                        record.Sections[pair.Key] = restored.Section;
                    Mod.log.Error($"Save data: the extension's section \"{pair.Key}\" could not be written by it ({(last ? "the bytes it was given are kept" : "none is kept for this save")}): {ex}");
                    continue;
                }

                if (bytes == null || bytes.Length == 0)
                    continue;
                if (bytes.Length > MaxSection)
                {
                    bool last = m_Sections.TryGetValue(pair.Key, out (StoredSection Section, bool Given, bool Taken) restored);
                    if (last)
                        record.Sections[pair.Key] = restored.Section;
                    Mod.log.Error($"Save data: the extension's section \"{pair.Key}\" is {bytes.Length} bytes, more than {MaxSection}: not written{(last ? "; the bytes it was given are kept" : "")}.");
                    continue;
                }

                record.Sections[pair.Key] = new StoredSection { Version = pair.Value.Version, Data = bytes };
            }
        }

        private static string Reasons(SaveRecord away)
        {
            var counts = new Dictionary<AnchorChange, int>();
            foreach (StoredItem i in away.Items)
                counts[i.Unmatched] = counts.TryGetValue(i.Unmatched, out int n) ? n + 1 : 1;
            var parts = new List<string>();
            foreach (KeyValuePair<AnchorChange, int> pair in counts)
                parts.Add(pair.Value + " " + pair.Key);
            return string.Join(", ", parts);
        }

        private int AwayRoads()
        {
            var roads = new HashSet<int>();
            foreach (StoredItem i in m_Away.Items)
                roads.Add(i.Anchor);
            foreach (StoredKerb k in m_Away.Kerbs)
                roads.Add(k.Anchor);
            return roads.Count;
        }

        private void Create(StoredItem stored, Entity edge)
        {
            var item = new TsItem
            {
                TypeHash = stored.TypeHash, Edge = edge, EdgeT = stored.EdgeT, Position = new float3(stored.Position.X, stored.Position.Y, stored.Position.Z), Yaw = stored.Yaw, Flags = stored.Flags,
                SupportHash = stored.SupportHash, Reach = stored.Reach, Group = stored.Group, Slot = stored.Slot, Lane = stored.Lane, OffsetX = stored.OffsetX, Turn = stored.Turn, BaseReach = stored.BaseReach,
                Style = stored.Style, BatchId = stored.BatchId, Rise = stored.Rise, Clearance = stored.Clearance
            };
            item.SetSpec(stored.Spec);
            Entity entity = EntityManager.CreateEntity(ComponentType.ReadWrite<TsItem>(), ComponentType.ReadWrite<LivePath>());
            EntityManager.SetComponentData(entity, item);
        }

        /// <summary>Sets a road's kerb styles (the two bits of each side of its Upgraded flags) and has the game make its composition again (Updated, at the start of the next frame: NextFrameUpdateSystem).</summary>
        private void SetKerb(Entity edge, byte left, byte right)
        {
            bool has = EntityManager.TryGetComponent(edge, out Upgraded upgraded);
            upgraded.m_Flags.m_Left = (CompositionFlags.Side)(((uint)upgraded.m_Flags.m_Left & ~Kerbs.Bits) | Kerbs.SetBits((KerbStyle)left));
            upgraded.m_Flags.m_Right = (CompositionFlags.Side)(((uint)upgraded.m_Flags.m_Right & ~Kerbs.Bits) | Kerbs.SetBits((KerbStyle)right));
            if (has)
                EntityManager.SetComponentData(edge, upgraded);
            else
                EntityManager.AddComponentData(edge, upgraded);
            NextFrameUpdateSystem.Instance.Mark(new[] { edge });
        }

        // ------------------------------------------------------------------ the roads

        /// <summary>The road segments now, by where their curves start and end.</summary>
        private Dictionary<long, List<(Entity Edge, RoadAnchor Anchor)>> Roads()
        {
            var clock = Stopwatch.StartNew();
            m_PrefabNames.Clear();
            var roads = new Dictionary<long, List<(Entity, RoadAnchor)>>();
            using (NativeArray<Entity> edges = m_Edges.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in edges)
                {
                    RoadAnchor anchor = AnchorOf(e);
                    if (!roads.TryGetValue(anchor.Key, out List<(Entity, RoadAnchor)> list))
                        roads[anchor.Key] = list = new List<(Entity, RoadAnchor)>(1);
                    list.Add((e, anchor));
                }
            }

            m_RoadsMs = clock.Elapsed.TotalMilliseconds;
            return roads;
        }

        /// <summary>How the road an anchor names is now: the segment that ends where it ended and differs least from it (Same when one is as it was).</summary>
        private static AnchorChange Match(RoadAnchor anchor, Dictionary<long, List<(Entity Edge, RoadAnchor Anchor)>> roads, out Entity edge)
        {
            edge = Entity.Null;
            if (!roads.TryGetValue(anchor.Key, out List<(Entity Edge, RoadAnchor Anchor)> list))
                return AnchorChange.Gone;
            AnchorChange best = AnchorChange.Gone;
            foreach ((Entity e, RoadAnchor now) in list)
            {
                AnchorChange c = anchor.Compare(now);
                if (best == AnchorChange.Gone || c == AnchorChange.Same)
                {
                    best = c;
                    edge = e;
                }
            }

            return best;
        }

        /// <summary>The names of the roads' prefabs for one pass over the roads: there are a few hundred prefabs for thousands of segments, and each name is a call into the engine.</summary>
        private readonly Dictionary<Entity, string> m_PrefabNames = new Dictionary<Entity, string>();
        private double m_RoadsMs, m_RecordMs;

        private string NameOf(Entity prefab)
        {
            if (!m_PrefabNames.TryGetValue(prefab, out string name))
                m_PrefabNames[prefab] = name = m_Prefabs.GetPrefabName(prefab);
            return name;
        }

        /// <summary>A road segment as it is now (its upgrades without the kerb styles' bits, which are not in the save).</summary>
        private RoadAnchor AnchorOf(Entity edge)
        {
            Curve curve = EntityManager.GetComponentData<Curve>(edge);
            Edge ends = EntityManager.GetComponentData<Edge>(edge);
            EntityManager.TryGetComponent(edge, out Upgraded upgraded);
            Vec3 V(float3 p) => new Vec3(p.x, p.y, p.z);
            return new RoadAnchor
            {
                Prefab = NameOf(EntityManager.GetComponentData<PrefabRef>(edge).m_Prefab), A = V(curve.m_Bezier.a), B = V(curve.m_Bezier.b), C = V(curve.m_Bezier.c), D = V(curve.m_Bezier.d),
                Left = (uint)upgraded.m_Flags.m_Left & ~Kerbs.Bits, Right = (uint)upgraded.m_Flags.m_Right & ~Kerbs.Bits, General = (uint)upgraded.m_Flags.m_General,
                StartDegree = Degree(ends.m_Start), EndDegree = Degree(ends.m_End)
            };
        }

        private byte Degree(Entity node) => EntityManager.TryGetBuffer(node, true, out DynamicBuffer<ConnectedEdge> edges) ? (byte)Math.Min(255, edges.Length) : (byte)0;

        private bool IsRoad(Entity edge) =>
            edge != Entity.Null && EntityManager.Exists(edge) && EntityManager.HasComponent<Edge>(edge) && EntityManager.HasComponent<Road>(edge) && !EntityManager.HasComponent<Deleted>(edge)
            && !EntityManager.HasComponent<Game.Tools.Temp>(edge);

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// The game is about to write a save (SaveStripSystem, its Serialize phase, before SerializerSystem): every item tagged so that it is left out, what the save holds of the mod copied, and the
        /// kerb styles' bits and the hidden markings' tags taken out (without Updated: nothing is drawn differently, and they are given back right after the writing).
        /// </summary>
        internal void BeforeWrite(Context context)
        {
            m_Prepared = true;
            if (m_Writing != null)
                Unreported("before the next save");
            if (context.purpose != Purpose.SaveGame || GameManager.instance.gameMode != GameMode.Game)
                return;
            if (!m_Untagged.IsEmptyIgnoreFilter)
                EntityManager.AddComponent<LivePath>(m_Untagged);
            CheckSerializer();
            // In the first frames after a load the record is not put back yet: this mod's items and the extensions' sections are not in the city, and a record of the save would hold none. None
            // is written: loading this save finds the city's records by their roads (AfterLoad), the one put back among them.
            if (m_LoadFrames >= 0)
            {
                Mod.log.Warn("Save data: a save written before the loaded save's record was put back: no record written for it.");
                return;
            }

            var clock = Stopwatch.StartNew();
            m_Writing = Record(context, true, out string wrong);
            m_RecordMs = clock.Elapsed.TotalMilliseconds;
            if (m_Writing != null)
                return;
            m_Deferred = true;
            Mod.log.Warn($"Save data: the roads' names read before the writing are not theirs ({wrong}): the game renumbered its prefabs before this mod's step (its save order changed?). "
                + "Nothing is taken out of the roads; the record is made when the save is done.");
        }

        /// <summary>The game's serializer is asked whether it takes the items (SerializerCheck): said once when it does not, and the first time it is known that it does.</summary>
        private void CheckSerializer()
        {
            bool? leaves = SerializerCheck.LeavesOut(World, m_Items);
            if (leaves == false)
            {
                Mod.log.Warn("Save data: the game's serializer takes this mod's items although they carry LivePath: this save holds them (the next load takes them out and puts the record back).");
            }
            else if (leaves == true && !m_SerializerLogged)
            {
                Mod.log.Info("Save data: the game's serializer leaves this mod's items out of a save (its query excludes LivePath).");
            }

            m_SerializerLogged |= leaves != null;
        }

        /// <summary>
        /// What a save holds of the mod: the items, the kerb styles and what was put away, at their roads, and the extensions' sections. strip (before the writing): the kerb styles' bits and the
        /// hidden markings' tags taken out of the roads, given back by AfterWrite. Null when the roads' names cannot be read right (PrefabNamesRight).
        /// </summary>
        private SaveRecord Record(Context context, bool strip, out string wrong)
        {
            if (!PrefabNamesRight(out wrong))
                return null;
            m_PrefabNames.Clear();
            var record = new SaveRecord
            {
                ModVersion = Mod.kVersion, City = Game.PSI.Telemetry.GetCurrentSession().ToString(), SaveId = context.instigatorGuid.ToString(), Time = DateTime.UtcNow.Ticks,
                CityName = World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().cityName ?? ""
            };
            var anchorOf = new Dictionary<Entity, int>();
            int Anchor(Entity edge)
            {
                if (!IsRoad(edge))
                    return -1;
                if (!anchorOf.TryGetValue(edge, out int i))
                {
                    anchorOf[edge] = i = record.Anchors.Count;
                    record.Anchors.Add(AnchorOf(edge));
                }

                return i;
            }

            using (NativeArray<Entity> items = m_Items.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in items)
                {
                    TsItem item = EntityManager.GetComponentData<TsItem>(e);
                    record.Items.Add(new StoredItem
                    {
                        TypeHash = item.TypeHash, EdgeT = item.EdgeT, Position = new Vec3(item.Position.x, item.Position.y, item.Position.z), Yaw = item.Yaw, Flags = item.Flags, SupportHash = item.SupportHash,
                        Spec = item.SpecText, Reach = item.Reach, Group = item.Group, Slot = item.Slot, Lane = item.Lane, OffsetX = item.OffsetX, Turn = item.Turn, BaseReach = item.BaseReach,
                        Style = item.Style, BatchId = item.BatchId, Rise = item.Rise, Clearance = item.Clearance, Anchor = Anchor(item.Edge)
                    });
                }
            }

            // The kerb styles: copied with their roads, and their bits out of the roads for the writing.
            if (strip)
                m_KerbsTaken.Clear();
            using (NativeArray<Entity> edges = m_Edges.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in edges)
                {
                    if (!EntityManager.TryGetComponent(e, out Upgraded upgraded) || (((uint)upgraded.m_Flags.m_Left | (uint)upgraded.m_Flags.m_Right) & Kerbs.Bits) == 0)
                        continue;
                    record.Kerbs.Add(new StoredKerb { Anchor = Anchor(e), Left = (byte)Kerbs.StyleOf((uint)upgraded.m_Flags.m_Left), Right = (byte)Kerbs.StyleOf((uint)upgraded.m_Flags.m_Right) });
                    if (!strip)
                        continue;
                    m_KerbsTaken.Add((e, upgraded));
                    Upgraded without = upgraded;
                    without.m_Flags.m_Left = (CompositionFlags.Side)((uint)without.m_Flags.m_Left & ~Kerbs.Bits);
                    without.m_Flags.m_Right = (CompositionFlags.Side)((uint)without.m_Flags.m_Right & ~Kerbs.Bits);
                    EntityManager.SetComponentData(e, without);
                }
            }

            // What was put away goes on, with its own anchors.
            int offset = record.Anchors.Count;
            record.Anchors.AddRange(m_Away.Anchors);
            foreach (StoredItem i in m_Away.Items)
            {
                StoredItem copy = Copy(i);
                copy.Anchor = i.Anchor < 0 ? -1 : i.Anchor + offset;
                record.Items.Add(copy);
            }

            foreach (StoredKerb k in m_Away.Kerbs)
                record.Kerbs.Add(new StoredKerb { Anchor = k.Anchor + offset, Left = k.Left, Right = k.Right, Unmatched = k.Unmatched });

            AddSections(record);
            if (strip)
                m_TagsTaken = ClearMarkingsSystem.Instance?.TakeTagsForSave();
            return record;
        }

        /// <summary>
        /// Whether the roads' prefabs are numbered as they are between saves (0.12.51): before the writing the game renumbers its prefabs (BeginPrefabSerializationSystem, undone by
        /// EndPrefabSerializationSystem, which also disables the component for a while), and a name read then is another prefab's; 0.12.38 wrote such names for every road, and every item came back
        /// unmatched. Each road's prefab, by the index its PrefabData holds (read whether it is enabled or not), must lead back to itself. A prefab whose asset is missing (no index) is named by its
        /// ID either way and is not looked at. wrong: the first that does not.
        /// </summary>
        private bool PrefabNamesRight(out string wrong)
        {
            wrong = "";
            var seen = new HashSet<Entity>();
            try
            {
                using (NativeArray<PrefabRef> refs = m_Edges.ToComponentDataArray<PrefabRef>(Allocator.Temp))
                {
                    foreach (PrefabRef r in refs)
                    {
                        if (!seen.Add(r.m_Prefab) || !EntityManager.HasComponent<PrefabData>(r.m_Prefab))
                            continue;
                        PrefabData data = EntityManager.GetComponentData<PrefabData>(r.m_Prefab);
                        if (data.m_Index < 0)
                            continue;
                        PrefabBase prefab = m_Prefabs.GetPrefab<PrefabBase>(data);
                        if (prefab == null || !m_Prefabs.TryGetEntity(prefab, out Entity back) || back != r.m_Prefab)
                        {
                            wrong = $"the prefab {r.m_Prefab} reads as {(prefab != null ? prefab.name : "nothing")}";
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                wrong = "the prefabs could not be read: " + ex.Message;
                return false;
            }

            return true;
        }

        /// <summary>The save is written (SaveRestoreSystem, after WriteSystem): the kerb styles' bits and the markings' tags given back.</summary>
        internal void AfterWrite()
        {
            foreach ((Entity edge, Upgraded copy) in m_KerbsTaken)
            {
                if (!EntityManager.Exists(edge) || !EntityManager.TryGetComponent(edge, out Upgraded now))
                    continue;
                now.m_Flags.m_Left = (CompositionFlags.Side)Kerbs.WithStyleOf((uint)now.m_Flags.m_Left, (uint)copy.m_Flags.m_Left);
                now.m_Flags.m_Right = (CompositionFlags.Side)Kerbs.WithStyleOf((uint)now.m_Flags.m_Right, (uint)copy.m_Flags.m_Right);
                EntityManager.SetComponentData(edge, now);
            }

            m_KerbsTaken.Clear();
            if (m_TagsTaken != null)
                ClearMarkingsSystem.Instance?.GiveTagsBack(m_TagsTaken);
            m_TagsTaken = null;
        }

        private void OnSaveEventFromGame(string saveName, string previewUri, bool start, bool success)
        {
            bool main = m_SaveEvents.Enter((saveName, previewUri, start, success));
            if (!m_ThreadLogged)
            {
                m_ThreadLogged = true;
                int thread = Environment.CurrentManagedThreadId;
                if (main)
                    Mod.log.Info($"Save data: the game's save event ({(start ? "start" : "done")}) came on the main thread (thread {thread}).");
                else
                    Mod.log.Warn($"Save data: the game's save event ({(start ? "start" : "done")}) came on thread {thread}, not the main thread: events from other threads are handled in this system's next update.");
            }

            if (main)
                OnSaveEvent(saveName, previewUri, start, success);
        }

        /// <summary>The game's save events: before it starts, the clearing lets the game make its lane arrows again for the save; when it is written, the copy goes to the store.</summary>
        private void OnSaveEvent(string saveName, string previewUri, bool start, bool success)
        {
            if (GameManager.instance.gameMode != GameMode.Game)
                return;
            if (start)
            {
                m_SaveStarted = true;
                m_Prepared = m_Deferred = false;
                ClearMarkingsSystem.Instance?.PauseForSave();
                return;
            }

            ClearMarkingsSystem.Instance?.ResumeAfterSave();
            // What was taken out for the writing is given back here too, when the game's writing failed before SaveRestoreSystem ran (nothing to do otherwise).
            AfterWrite();
            SaveRecord record = m_Writing;
            m_Writing = null;
            bool started = m_SaveStarted, prepared = m_Prepared, deferred = m_Deferred;
            m_SaveStarted = m_Prepared = m_Deferred = false;
            if (!success)
                return;
            // A save written with no record, other than right after a load where that is meant: made now.
            if (record == null && started && m_LoadFrames < 0)
                record = LateRecord(prepared, deferred);
            if (record == null)
                return;
            record.SaveName = saveName ?? "";
            try
            {
                List<string> removed = SaveStore.Write(Root, record);
                Status = $"saved:{record.Items.Count - m_Away.Items.Count}:{m_Away.Items.Count}";
                Mod.log.Info($"Save data: \"{record.SaveName}\" ({record.SaveId}) of city {record.City}: {record.Items.Count} item(s) ({m_Away.Items.Count} put away), {record.Kerbs.Count} kerb style(s), "
                    + $"{record.Anchors.Count} road(s){Sections(record)} written to the store{(removed.Count > 0 ? $"; {removed.Count} older record(s) of the same name removed" : "")} (made in {m_RecordMs:0.0} ms before the writing).");
            }
            catch (Exception ex)
            {
                Status = "error: " + ex.Message;
                Mod.log.Error("Save data: the record could not be written: " + ex);
            }

            m_Revision++;
        }

        /// <summary>
        /// The record of a save the game wrote without one (0.12.51): this mod's step before the writing did not run (the game's save order changed), put the record off (the prefabs were renumbered
        /// already) or failed. Made from the city as it is once the save is done, the prefabs numbered as before; the save itself then holds the kerb styles and the hidden markings, which only that
        /// step takes out. Null when it cannot be made either.
        /// </summary>
        private SaveRecord LateRecord(bool prepared, bool deferred)
        {
            string why = !prepared ? "the game wrote it without this mod's step before the writing (its save order changed?)"
                : deferred ? "the prefabs had been renumbered before this mod's step" : "this mod's step before the writing failed";
            Context context = m_SaveGame.context;
            SaveRecord record = null;
            string wrong = "the save's context is not a save's";
            try
            {
                if (context.purpose == Purpose.SaveGame)
                    record = Record(context, false, out wrong);
            }
            catch (Exception ex)
            {
                wrong = ex.Message;
                Mod.log.Error("Save data: the record of the save could not be made after it either: " + ex);
            }

            if (record == null)
            {
                Warn("nosave", 1);
                Mod.log.Warn($"Save data: the save has no record ({why}), and none could be made after it ({wrong}). Loading it finds the city's records by their roads.");
                return null;
            }

            Warn("late", 1);
            Mod.log.Warn($"Save data: the save had no record ({why}): it is made now, after the writing; the save holds the kerb styles and the hidden markings (they matter only without this mod).");
            return record;
        }

        /// <summary>
        /// The record of a save the game never said was done (its end event did not come, 0.12.51): written to the store now under a name of its own (records of one name are pruned to the newest),
        /// so that loading that save finds it; a save that failed leaves a record no save has.
        /// </summary>
        private void Unreported(string when)
        {
            SaveRecord record = m_Writing;
            m_Writing = null;
            Warn("unreported", 1);
            record.SaveName = $"({record.SaveId})";
            try
            {
                SaveStore.Write(Root, record);
                Mod.log.Warn($"Save data: the game did not say whether the save {record.SaveId} was written ({when}): its record is written to the store now, named {record.SaveName}.");
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Save data: the record of the save {record.SaveId}, which the game did not say was written, could not be written either: " + ex);
            }
        }

        /// <summary>Notes something the game's saving or loading did that this store does not expect (m_Warnings), for the panel.</summary>
        internal void Warn(string code, int count)
        {
            m_Warnings[code] = (m_Warnings.TryGetValue(code, out int n) ? n : 0) + count;
            m_Revision++;
        }

        private static string Sections(SaveRecord record)
        {
            if (record.Sections.Count == 0)
                return "";
            var parts = new List<string>();
            foreach (KeyValuePair<string, StoredSection> pair in record.Sections)
                parts.Add($"\"{pair.Key}\" {pair.Value.Data.Length} bytes");
            return ", the sections " + string.Join(", ", parts);
        }

        private static StoredItem Copy(StoredItem i) => new StoredItem
        {
            TypeHash = i.TypeHash, EdgeT = i.EdgeT, Position = i.Position, Yaw = i.Yaw, Flags = i.Flags, SupportHash = i.SupportHash, Spec = i.Spec, Reach = i.Reach, Group = i.Group, Slot = i.Slot,
            Lane = i.Lane, OffsetX = i.OffsetX, Turn = i.Turn, BaseReach = i.BaseReach, Style = i.Style, BatchId = i.BatchId, Rise = i.Rise, Clearance = i.Clearance, Anchor = i.Anchor, Unmatched = i.Unmatched
        };

        // ------------------------------------------------------------------ the panel

        /// <summary>
        /// The panel's view (JSON): what the store did, the warnings (code: count), the records offered, and the roads put away (index, why, how many items, a kerb style or not, where).
        /// </summary>
        internal string Json()
        {
            var json = new StringBuilder("{\"status\":\"").Append(Escape(Status)).Append("\",\"warn\":{");
            bool firstWarning = true;
            foreach (KeyValuePair<string, int> pair in m_Warnings)
            {
                json.Append(firstWarning ? "" : ",").Append('"').Append(pair.Key).Append("\":").Append(pair.Value);
                firstWarning = false;
            }

            json.Append("},\"offers\":[");
            for (int n = 0; n < m_Offers.Count; n++)
            {
                (string _, SaveRecord header, float score) = m_Offers[n];
                json.Append(n > 0 ? "," : "").Append("{\"name\":\"").Append(Escape(header.SaveName)).Append("\",\"time\":\"").Append(Time(header.Time)).Append("\",\"score\":")
                    .Append(Mathf.RoundToInt(score * 100f)).Append(",\"items\":").Append(header.ItemCount).Append('}');
            }

            json.Append("],\"away\":[");
            var items = new Dictionary<int, int>();
            var styled = new HashSet<int>();
            var why = new Dictionary<int, AnchorChange>();
            foreach (StoredItem i in m_Away.Items)
            {
                items[i.Anchor] = items.TryGetValue(i.Anchor, out int c) ? c + 1 : 1;
                why[i.Anchor] = i.Unmatched;
            }

            foreach (StoredKerb k in m_Away.Kerbs)
            {
                styled.Add(k.Anchor);
                why[k.Anchor] = k.Unmatched;
            }

            bool first = true;
            foreach (KeyValuePair<int, AnchorChange> pair in why)
            {
                int a = pair.Key;
                Vec3 at = a >= 0 ? m_Away.Anchors[a].Middle : Vec3.Zero;
                json.Append(first ? "" : ",").Append("{\"i\":").Append(a).Append(",\"why\":\"").Append(pair.Value.ToString().ToLowerInvariant()).Append("\",\"items\":")
                    .Append(items.TryGetValue(a, out int count) ? count : 0).Append(",\"kerb\":").Append(styled.Contains(a) ? "true" : "false").Append(",\"prefab\":\"")
                    .Append(Escape(a >= 0 ? m_Away.Anchors[a].Prefab : "")).Append("\",\"x\":").Append(at.X.ToString("0.0", CultureInfo.InvariantCulture)).Append(",\"z\":")
                    .Append(at.Z.ToString("0.0", CultureInfo.InvariantCulture)).Append('}');
                first = false;
            }

            return json.Append("]}").ToString();
        }

        /// <summary>The camera over where a road put away was.</summary>
        internal void Locate(int anchor)
        {
            if (anchor < 0 || anchor >= m_Away.Anchors.Count)
                return;
            Vec3 at = m_Away.Anchors[anchor].Middle;
            Game.Rendering.CameraUpdateSystem camera = World.GetOrCreateSystemManaged<Game.Rendering.CameraUpdateSystem>();
            if (camera.orbitCameraController != null)
            {
                camera.orbitCameraController.pivot = new Vector3(at.X, at.Y, at.Z);
                camera.activeCameraController = camera.orbitCameraController;
            }
        }

        /// <summary>
        /// The items and kerb style of a road put away (anchor; -1: all of them) placed after all: the items where they were, their road looked up again by ItemRoadSystem as for a newly placed
        /// item; the kerb style on the road whose curve passes nearest where the road's middle was.
        /// </summary>
        internal void Place(int anchor)
        {
            int items = 0, kerbs = 0;
            m_Away.Items.RemoveAll(i =>
            {
                if (anchor >= 0 && i.Anchor != anchor)
                    return false;
                Create(i, Entity.Null);
                items++;
                return true;
            });
            Dictionary<long, List<(Entity Edge, RoadAnchor Anchor)>> roads = null;
            m_Away.Kerbs.RemoveAll(k =>
            {
                if (anchor >= 0 && k.Anchor != anchor)
                    return false;
                roads = roads ?? Roads();
                Entity edge = Nearest(m_Away.Anchors[k.Anchor].Middle, roads);
                if (edge != Entity.Null)
                {
                    SetKerb(edge, k.Left, k.Right);
                    kerbs++;
                }

                return true;
            });
            Mod.log.Info($"Save data: {items} item(s) and {kerbs} kerb style(s) put away placed after all{(anchor >= 0 ? " (one road)" : "")}.");
            m_Revision++;
        }

        /// <summary>The items and kerb style of a road put away (-1: all of them) deleted.</summary>
        internal void Delete(int anchor)
        {
            int items = m_Away.Items.RemoveAll(i => anchor < 0 || i.Anchor == anchor), kerbs = m_Away.Kerbs.RemoveAll(k => anchor < 0 || k.Anchor == anchor);
            Mod.log.Info($"Save data: {items} item(s) and {kerbs} kerb style(s) put away deleted{(anchor >= 0 ? " (one road)" : "")}.");
            m_Revision++;
        }

        /// <summary>The road whose curve's middle is nearest a place (within 10 m); none when there is none.</summary>
        private static Entity Nearest(Vec3 at, Dictionary<long, List<(Entity Edge, RoadAnchor Anchor)>> roads)
        {
            Entity best = Entity.Null;
            float bestD = 10f;
            foreach (List<(Entity Edge, RoadAnchor Anchor)> list in roads.Values)
            {
                foreach ((Entity e, RoadAnchor a) in list)
                {
                    float d = Vec3.DistanceXZ(a.Middle, at);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = e;
                    }
                }
            }

            return best;
        }

        private static string Time(long ticks) => new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
    }

    /// <summary>Before the game's serializer: SaveDataSystem.BeforeWrite (0.12.38).</summary>
    internal sealed partial class SaveStripSystem : GameSystemBase
    {
        private SaveGameSystem m_Save;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Save = World.GetOrCreateSystemManaged<SaveGameSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                SaveDataSystem.Instance?.BeforeWrite(m_Save.context);
            }
            catch (Exception ex)
            {
                Mod.log.Error("Save data: the save could not be prepared (its items are left out all the same; the store keeps its last record): " + ex);
            }
        }
    }

    /// <summary>After the game's writer: SaveDataSystem.AfterWrite (0.12.38).</summary>
    internal sealed partial class SaveRestoreSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            try
            {
                SaveDataSystem.Instance?.AfterWrite();
            }
            catch (Exception ex)
            {
                Mod.log.Error("Save data: what was taken out for the save could not all be given back: " + ex);
            }
        }
    }
}
