using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// Registers the tab "CDL:StreetScape China Pack" (until 0.12.48 "CDL:TrafficSigns China Pack"; its key "CDLTS China Pack" stays) in the Roads menu of the game's toolbar as an ordinary asset category
    /// (UIAssetCategoryPrefab). Its members are the kerb styles' road upgrades (KerbUpgradeSystem, 0.12.85; until then one marker prefab of ours that did nothing, so that the toolbar, which drops a
    /// category with no members, listed the tab). The tab's title is the localization key SubServices.NAME[name] (lang/*.json). The tab's content is drawn by CDLC.TS.mjs, which draws its panel when
    /// this category is selected.
    ///
    /// This system runs in the PrefabUpdate phase right before PrefabInitializeSystem (Mod.cs): a prefab added anywhere else is
    /// created with the right archetype but is never initialized (the game initializes entities that carry Created, and only in
    /// that phase), so its UIObjectData and its place in the menu's group buffer stay empty. 0.0.1 and 0.0.2 registered in
    /// MainLoop and the tab did not appear; their log said "initialized" only because the archetype had UIAssetCategoryData.
    /// The log now says what the group buffers really hold, and a tab that is still not wired after a few frames is wired with the
    /// game's own AddElement (the call the initialization would have made), and that is logged.
    /// </summary>
    internal sealed partial class ToolbarTabSystem : GameSystemBase
    {
        internal const string kCategoryName = "CDLTS China Pack";

        /// <summary>Candidate names of the Roads menu, in order of preference (matched ignoring case).</summary>
        private static readonly string[] kMenuNames = { "Roads", "Road", "RoadsMenu", "Roads Menu" };

        private const int kPollFrames = 30;
        private const int kMaxPolls = 600;
        private const int kVerifyAfterFrames = 3;

        /// <summary>How long after a game has loaded the tab's lock is looked at (frames): the game's unlock pass is in the first update after the load, whichever system runs first.</summary>
        private const int kLockCheckFrames = 120;
        private const int kMaxLockLogs = 3;

        /// <summary>The category entity once it is wired into the menu, for the UI binding (Entity.Null before).</summary>
        internal static Entity CategoryEntity { get; private set; }
        internal static string Status { get; private set; } = "waiting for the toolbar menus";
        internal static ToolbarTabSystem Instance { get; private set; }

        private PrefabSystem m_Prefabs;
        private EntityQuery m_Menus;
        private UIAssetMenuPrefab m_Roads;
        private int m_Frame;
        private int m_Polls;
        private int m_FramesSinceRegister;
        private bool m_Registered;
        private bool m_Verified;
        private UIAssetCategoryPrefab m_Category;
        private int m_FramesSinceMembers;
        private bool m_MembersChecked;
        private int m_LockCheckIn, m_LockLogs;

        /// <summary>Whether the kerb styles are the tab's members (the preference kerbTab, read at the start; a restart changes it).</summary>
        internal static bool KerbsInTab = true;

        /// <summary>The category once it has been added as a prefab; null before, and when its registration failed (KerbUpgradeSystem then lists the upgrades in the template's group).</summary>
        internal UIAssetCategoryPrefab Category => m_Registered ? m_Category : null;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            // A menu that is still Created has not been initialized yet; wait one frame for it.
            m_Menus = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<UIAssetMenuData>(), ComponentType.ReadOnly<PrefabData>() },
                None = new[] { ComponentType.ReadOnly<Game.Common.Created>() }
            });
            Instance = this;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (m_Registered)
            {
                if (!m_Verified && ++m_FramesSinceRegister >= kVerifyAfterFrames)
                    VerifyOrWire();
                else if (m_Verified && !m_MembersChecked && KerbsInTab && KerbUpgradeSystem.Added.Count > 0 && ++m_FramesSinceMembers >= kVerifyAfterFrames)
                    VerifyMembers();
                if (m_LockCheckIn > 0 && --m_LockCheckIn == 0)
                    CheckLock();
                return;
            }

            if (++m_Frame % kPollFrames != 0)
                return;

            if (++m_Polls > kMaxPolls)
            {
                Status = "gave up: no Roads menu found";
                Mod.log.Warn("Toolbar tab: no menu matching " + string.Join(", ", kMenuNames) + " was found; the tab is not registered.");
                Enabled = false;
                return;
            }

            if (m_Menus.IsEmptyIgnoreFilter)
                return;

            UIAssetMenuPrefab roads = FindRoadsMenu();
            if (roads == null)
                return;

            try
            {
                Register(roads);
                m_Registered = true;
                Status = "registered under menu " + roads.name + ", not verified yet";
            }
            catch (Exception ex)
            {
                Status = "registration failed: " + ex.Message;
                Mod.log.Error("Toolbar tab registration failed: " + ex);
                Enabled = false;
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode == GameMode.Game && m_Registered && KerbsInTab)
                m_LockCheckIn = kLockCheckFrames;
        }

        /// <summary>
        /// A category is Locked until the game's unlock pass finds a member of it that is not (UIGroupPrefab: its requirements are its members, any one). The pass runs when a game has loaded; the members were
        /// added just before it. The log says what the tab is after the load; a tab that is still locked with its members in it is unlocked here (it asks for nothing: the kerb styles are not locked).
        /// </summary>
        private void CheckLock()
        {
            try
            {
                if (!m_Prefabs.TryGetEntity(m_Category, out Entity category))
                    return;
                bool locked = EntityManager.HasEnabledComponent<Locked>(category);
                int members = EntityManager.TryGetBuffer(category, true, out DynamicBuffer<UIGroupElement> elements) ? elements.Length : 0;
                if (locked && members > 0)
                    EntityManager.SetComponentEnabled<Locked>(category, false);
                if (m_LockLogs++ < kMaxLockLogs)
                    Mod.log.Info($"Toolbar tab: after the load the tab holds {members} member(s) and was {(locked ? "locked" + (members > 0 ? ": unlocked by this mod" : "") : "unlocked by the game")}.");
            }
            catch (Exception ex)
            {
                Mod.log.Error("Toolbar tab: the lock could not be checked: " + ex);
            }
        }

        private UIAssetMenuPrefab FindRoadsMenu()
        {
            var menus = new List<UIAssetMenuPrefab>();
            using (NativeArray<Entity> entities = m_Menus.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    if (m_Prefabs.TryGetPrefab(entity, out UIAssetMenuPrefab menu))
                        menus.Add(menu);
                }
            }

            if (m_Polls == 1)
                LogMenus(menus);

            foreach (string wanted in kMenuNames)
            {
                foreach (UIAssetMenuPrefab menu in menus)
                {
                    if (string.Equals(menu.name, wanted, StringComparison.OrdinalIgnoreCase))
                        return menu;
                }
            }

            return null;
        }

        private void LogMenus(List<UIAssetMenuPrefab> menus)
        {
            var text = new StringBuilder("Toolbar menus (" + menus.Count + "):");
            foreach (UIAssetMenuPrefab menu in menus)
            {
                text.Append("\n  ").Append(menu.name).Append(" ->");
                if (!m_Prefabs.TryGetEntity(menu, out Entity entity) || !EntityManager.TryGetBuffer(entity, true, out DynamicBuffer<UIGroupElement> elements))
                    continue;
                foreach (UIGroupElement element in elements)
                {
                    if (m_Prefabs.TryGetPrefab(element.m_Prefab, out PrefabBase category))
                        text.Append(' ').Append(category.name);
                }
            }

            Mod.log.Info(text.ToString());
        }

        private void Register(UIAssetMenuPrefab roads)
        {
            m_Roads = roads;
            m_Category = ScriptableObject.CreateInstance<UIAssetCategoryPrefab>();
            m_Category.name = kCategoryName;
            m_Category.m_Menu = roads;
            // The priority orders the tab among its neighbours; the category adds itself to its menu, so its own group stays empty.
            UIObject categoryUi = m_Category.AddComponent<UIObject>();
            categoryUi.m_Group = null;
            categoryUi.m_Priority = 900;
            categoryUi.m_Icon = Render.ThumbnailStore.TabIconUrl;

            if (!m_Prefabs.AddPrefab(m_Category))
                throw new InvalidOperationException("PrefabSystem.AddPrefab refused the category");

            Mod.log.Info("Toolbar tab: added category '" + kCategoryName + "' to menu '" + roads.name + "' in the PrefabUpdate phase.");
        }

        /// <summary>What the initialization of the category has to have done: it knows its menu and is in the menu's group buffer.</summary>
        private bool IsWired(out string detail)
        {
            detail = "";
            if (!m_Prefabs.TryGetEntity(m_Roads, out Entity menu) || !m_Prefabs.TryGetEntity(m_Category, out Entity category))
            {
                detail = "an entity is missing";
                return false;
            }

            bool categoryKnowsMenu = EntityManager.TryGetComponent(category, out UIAssetCategoryData data) && data.m_Menu == menu;
            bool inMenu = Contains(menu, category);
            detail = $"categoryKnowsMenu={categoryKnowsMenu} inMenuBuffer={inMenu}";
            return categoryKnowsMenu && inMenu;
        }

        private bool Contains(Entity group, Entity member)
        {
            if (!EntityManager.TryGetBuffer(group, true, out DynamicBuffer<UIGroupElement> elements))
                return false;
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].m_Prefab == member)
                    return true;
            }

            return false;
        }

        private void VerifyOrWire()
        {
            m_Verified = true;
            try
            {
                if (IsWired(out string before))
                {
                    CategoryEntity = m_Prefabs.GetEntity(m_Category);
                    Status = "wired by the game's initialization";
                    Mod.log.Info("Toolbar tab: wired by the game's own initialization (" + before + "), category entity " + CategoryEntity + ".");
                    return;
                }

                Mod.log.Warn("Toolbar tab: the initialization did not wire the tab (" + before + "); wiring it with the game's own AddElement.");
                Wire();
                bool ok = IsWired(out string after);
                Status = ok ? "wired by this mod (initialization did not)" : "wiring failed: " + after;
                Mod.log.Info("Toolbar tab: after wiring: " + after + ".");
                if (ok)
                    CategoryEntity = m_Prefabs.GetEntity(m_Category);
            }
            catch (Exception ex)
            {
                Status = "verification failed: " + ex.Message;
                Mod.log.Error("Toolbar tab verification failed: " + ex);
            }
        }

        /// <summary>The same writes UIAssetCategoryPrefab.LateInitialize and UIObject.LateInitialize make.</summary>
        private void Wire()
        {
            Entity menu = m_Prefabs.GetEntity(m_Roads);
            Entity category = m_Prefabs.GetEntity(m_Category);

            EntityManager.SetComponentData(category, new UIAssetCategoryData(menu));
            EntityManager.SetComponentData(category, new UIObjectData { m_Group = Entity.Null, m_Priority = 900 });
            if (!Contains(menu, category))
                m_Roads.AddElement(EntityManager, category);
        }

        /// <summary>
        /// The kerb styles' upgrades are in the tab's group buffer and know the tab (what UIObject.LateInitialize does for a prefab added in the PrefabUpdate phase before PrefabInitializeSystem, as KerbUpgradeSystem
        /// adds them); one that is not is wired with the same writes. The log says what the tab holds, in the order the toolbar sorts it (the priority).
        /// </summary>
        private void VerifyMembers()
        {
            m_MembersChecked = true;
            try
            {
                Entity category = m_Prefabs.GetEntity(m_Category);
                var members = new List<(int Priority, string Name)>();
                int wired = 0;
                foreach ((KerbStyle style, PrefabBase prefab) in KerbUpgradeSystem.Added)
                {
                    if (!prefab.TryGetExactly(out UIObject ui) || ui.m_Group != m_Category || !m_Prefabs.TryGetEntity(prefab, out Entity entity))
                        continue;
                    bool knows = EntityManager.TryGetComponent(entity, out UIObjectData data) && data.m_Group == category;
                    if (!knows || !Contains(category, entity))
                    {
                        EntityManager.SetComponentData(entity, new UIObjectData { m_Group = category, m_Priority = ui.m_Priority });
                        if (!Contains(category, entity))
                            m_Category.AddElement(EntityManager, entity);
                        wired++;
                    }

                    members.Add((ui.m_Priority, prefab.name));
                }

                members.Sort();
                Mod.log.Info($"Toolbar tab: the tab holds {members.Count} member(s) ({string.Join(", ", members.ConvertAll(m => m.Name + " " + m.Priority))}); {wired} wired by this mod, the game's initialization did the rest.");
            }
            catch (Exception ex)
            {
                Mod.log.Error("Toolbar tab: the members could not be checked: " + ex);
            }
        }

        /// <summary>
        /// What the toolbar sees, in the terms of ToolbarUISystem.GetSortedCategories: the entries of the Roads menu, and for each whether it
        /// has category data and members (an entry without either is not listed). Ours is marked. For the phase 0 log.
        /// </summary>
        internal string DescribeToolbar()
        {
            if (m_Roads == null || m_Category == null)
                return "Toolbar state: nothing registered";

            var text = new StringBuilder("Toolbar state:");
            if (!m_Prefabs.TryGetEntity(m_Roads, out Entity menu) || !EntityManager.TryGetBuffer(menu, true, out DynamicBuffer<UIGroupElement> elements))
                return text.Append(" the Roads menu has no group buffer").ToString();

            text.Append(" menu ").Append(menu).Append(" lists ").Append(elements.Length).Append(" entries:");
            m_Prefabs.TryGetEntity(m_Category, out Entity ours);
            for (int i = 0; i < elements.Length; i++)
            {
                Entity entry = elements[i].m_Prefab;
                string name = m_Prefabs.TryGetPrefab(entry, out PrefabBase prefab) ? prefab.name : "?";
                bool data = EntityManager.HasComponent<UIAssetCategoryData>(entry);
                int members = EntityManager.TryGetBuffer(entry, true, out DynamicBuffer<UIGroupElement> inner) ? inner.Length : -1;
                bool locked = EntityManager.HasEnabledComponent<Locked>(entry);
                text.Append("\n  ").Append(entry == ours ? "* " : "  ").Append(name).Append(" data=").Append(data).Append(" members=").Append(members).Append(" locked=").Append(locked);
            }

            IsWired(out string wired);
            text.Append("\n  wiring: ").Append(wired);
            return text.ToString();
        }
    }
}
