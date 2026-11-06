using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Colossal.Entities;
using Game;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// The road upgrades of the kerb styles (0.12.33; route A of scratch/design/RESEARCH-kerbs-upgrades.md): copies of the game's "Grass" upgrade (a side upgrade, applied to the side the cursor is on)
    /// with no requirements of their own, to which KerbFlagSystem gives two side flags no part of the game reads (Kerbs.Bits). They are added in the PrefabUpdate phase before PrefabInitializeSystem (a
    /// prefab added anywhere else is never initialized, see ToolbarTabSystem), without the template's unlocking (a copy added in a city would wait for an unlock that has happened already). Their icons
    /// are thumbnails (ThumbnailStore.KerbUrl). Since 0.12.85 they are the members of the mod's tab in the Roads menu (KerbTab); the net tool starts on an upgrade from any group: what it does is read
    /// from the prefab's own flags, not from where the toolbar lists it.

    /// </summary>
    internal sealed partial class KerbUpgradeSystem : GameSystemBase
    {
        internal const string kTemplate = "Grass";

        internal static readonly List<(KerbStyle Style, PrefabBase Prefab)> Added = new List<(KerbStyle, PrefabBase)>();

        /// <summary>The side flags of the template: a road whose mask has them has a side the game lets grass onto, and takes the kerb styles too.</summary>
        internal static CompositionFlags.Side TemplateSides { get; private set; }

        private PrefabSystem m_Prefabs;
        private EntityQuery m_Placeable;
        private bool m_Done, m_DumpDone;
        private double m_DumpFrom = -1.0, m_DumpTried = -1.0;

        /// <summary>The dump of the road pieces is tried this often (seconds) while the game is loading their geometry, and given up after this long (it is made at the next start).</summary>
        private const double kDumpEvery = 1.0, kDumpGiveUp = 300.0;

        /// <summary>The name of a style's upgrade (its texts: Assets.NAME[...] and Assets.DESCRIPTION[...] in lang/*.json).</summary>
        internal static string NameOf(KerbStyle style) => "CDLTS Kerb " + style;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Placeable = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<PlaceableNetData>());
        }

        /// <summary>
        /// The dump of the game's road pieces (PieceDump), once a version. It reads their meshes, which the game must not be loading (GeometryGate: it was done in the first frame of a city, in 0.12.78 under
        /// the loading screen), so it is tried each second until it can be, and given up after five minutes.
        /// </summary>
        private void TryPieceDump()
        {
            if (m_DumpDone)
                return;
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (m_DumpFrom < 0.0)
                m_DumpFrom = now;
            if (now - m_DumpFrom > kDumpGiveUp)
            {
                m_DumpDone = true;
                Mod.log.Info("Kerb styles: the game was still loading road pieces after five minutes: their dump is left for the next start.");
                return;
            }

            if (now - m_DumpTried < kDumpEvery)
                return;
            m_DumpTried = now;
            m_DumpDone = PieceDump.WriteOnce(EntityManager, m_Prefabs);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance == null || GameManager.instance.gameMode != GameMode.Game)
                return;
            TryPieceDump();
            if (m_Done)
                return;
            m_Done = true;
            PrefabBase template = FindTemplate(out PlaceableNetData placeable);
            TemplateSides = placeable.m_SetUpgradeFlags.m_Left | placeable.m_SetUpgradeFlags.m_Right;
            if (template == null || template.GetComponent<NetUpgrade>() == null || TemplateSides == 0)
            {
                Mod.log.Warn($"Kerb styles: the game's \"{kTemplate}\" side upgrade was not found: no kerb upgrades.");
                return;
            }

            int priority = template.TryGetExactly(out UIObject ui) ? ui.m_Priority : 0;
            UIAssetCategoryPrefab tab = ToolbarTabSystem.KerbsInTab ? ToolbarTabSystem.Instance?.Category : null;
            foreach (KerbStyle style in Kerbs.Styles)
            {
                PrefabBase copy = template.Clone(NameOf(style));
                copy.Remove<ObsoleteIdentifiers>();
                foreach (ComponentBase component in copy.components.ToArray())
                {
                    if (component is UnlockableBase)
                        copy.Remove(component.GetType());
                }

                NetUpgrade upgrade = copy.GetComponent<NetUpgrade>();
                upgrade.m_SetState = new NetPieceRequirements[0];
                upgrade.m_UnsetState = new NetPieceRequirements[0];
                if (copy.TryGetExactly(out UIObject copyUi))
                {
                    copyUi.m_Priority = KerbTab.Priority(style, priority, tab != null);
                    if (tab != null)
                        copyUi.m_Group = tab;
                    copyUi.m_Icon = ThumbnailStore.KerbUrl(style);
                }

                if (m_Prefabs.AddPrefab(copy))
                    Added.Add((style, copy));
            }

            string group = tab != null ? tab.name + " (the mod's tab)" : ui?.m_Group?.name ?? "none";
            Mod.log.Info($"Kerb styles: {Added.Count} road upgrade(s) added from the game's \"{template.name}\" (priority {priority}, group {group}, roads with side flags {TemplateSides}).");
        }

        private PrefabBase FindTemplate(out PlaceableNetData placeable)
        {
            using (NativeArray<Entity> entities = m_Placeable.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in entities)
                {
                    placeable = EntityManager.GetComponentData<PlaceableNetData>(e);
                    if ((placeable.m_PlacementFlags & PlacementFlags.IsUpgrade) != 0
                        && m_Prefabs.TryGetPrefab(EntityManager.GetComponentData<PrefabData>(e), out PrefabBase prefab) && prefab != null && prefab.name == kTemplate)
                        return prefab;
                }
            }

            placeable = default;
            return null;
        }
    }

    /// <summary>
    /// Gives the kerb styles' upgrades their flags (PlaceableNetData: the style's two bits set and the others cleared on the right; the net tool takes them to the left for a cursor on the left) and lets
    /// the roads the template fits on take them (NetData.m_SideFlagMask: the tool offers an upgrade only to a road whose mask has its bits). In the PrefabUpdate phase after NetInitializeSystem, which writes both of
    /// them for a prefab that is Created: again whenever a net prefab is. The flags change no piece of any road (none asks for them), so a road keeps its meshes; without this mod the bits in a saved
    /// road do nothing.
    /// </summary>
    internal sealed partial class KerbFlagSystem : GameSystemBase
    {
        private PrefabSystem m_Prefabs;
        private EntityQuery m_Roads, m_Created;
        private bool m_Patched;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Roads = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<RoadData>(), ComponentType.ReadWrite<NetData>());
            m_Created = GetEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<NetData>(), ComponentType.ReadOnly<Game.Common.Created>());
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (KerbUpgradeSystem.Added.Count == 0 || (m_Patched && m_Created.IsEmptyIgnoreFilter))
                return;
            int upgrades = 0;
            foreach ((KerbStyle style, PrefabBase prefab) in KerbUpgradeSystem.Added)
            {
                if (!m_Prefabs.TryGetEntity(prefab, out Entity entity) || !EntityManager.TryGetComponent(entity, out PlaceableNetData placeable))
                    continue;
                placeable.m_SetUpgradeFlags = new CompositionFlags(default, default, (CompositionFlags.Side)Kerbs.SetBits(style));
                placeable.m_UnsetUpgradeFlags = new CompositionFlags(default, default, (CompositionFlags.Side)Kerbs.ClearBits(style));
                EntityManager.SetComponentData(entity, placeable);
                upgrades++;
            }

            int roads = 0;
            var bits = (CompositionFlags.Side)Kerbs.Bits;
            using (NativeArray<Entity> entities = m_Roads.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity e in entities)
                {
                    NetData data = EntityManager.GetComponentData<NetData>(e);
                    if ((data.m_SideFlagMask & KerbUpgradeSystem.TemplateSides) == 0 || (data.m_SideFlagMask & bits) == bits)
                        continue;
                    data.m_SideFlagMask |= bits;
                    EntityManager.SetComponentData(e, data);
                    roads++;
                }
            }

            if (!m_Patched || roads > 0)
                Mod.log.Info($"Kerb styles: the flags written into {upgrades} upgrade(s); {roads} more road(s) take them.");
            m_Patched = true;
        }
    }
}
