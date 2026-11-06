using System;
using CDLC.TS.Geo;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>
    /// Where the Realistic Information Extension (CDLC.TS.RIE, a mod of its own since 0.12.29; the owner: "将mod中所有和游戏产生数据交换的部分…移动出mod，作为 Realistic Information Extension 拓展使用")
    /// joins this mod. Everything that reads the game's live state (the congestion, accidents and blocked lanes the screens show), the interface other mods read (TsApi), the markings protocol with
    /// CDL : Racing and the read-only probes are the extension's; this mod draws what it is given. Without it the screens show their own content (LiveContent) and no other mod is asked anything:
    /// the hooks below answer no. The extension sees this mod's internals (InternalsVisibleTo) and sets them in its OnLoad, once. Main thread.
    /// </summary>
    internal static class TsExtension
    {
        /// <summary>The version of the single points below (the fields the Realistic Information Extension sets, and Join): an extension made for another refuses to join. Kept at 1 in API 2 so that it still joins.</summary>
        internal static readonly int Api = 1;

        /// <summary>
        /// 0.12.93: what an extension can use besides those points. 2 adds Register (several extensions at once, each with its own hooks) and the sections' sharing; a new extension checks
        /// ApiLevel >= 2 and an old one keeps checking Api, which did not change.
        /// </summary>
        internal static readonly int ApiLevel = 2;

        /// <summary>The extension that joined through Join and its version ("" none).</summary>
        internal static string Joined { get; private set; } = "";

        /// <summary>The extensions that joined through Register (API 2), with what each answers; the single points below are asked first, then these.</summary>
        internal static readonly ExtensionRegistry<Entity> Registry = new ExtensionRegistry<Entity>();

        /// <summary>Whether another mod (CDL : Racing) wants a marking lane of the game hidden: the clearing areas leave its tag on (ClearMarkingsSystem).</summary>
        internal static Func<Entity, bool> OthersHideMarking = _ => false;

        /// <summary>Whether a road segment or junction is one another mod draws its own lines on (a circuit of CDL : Racing): the automatic marking leaves it alone.</summary>
        internal static Func<Entity, bool> IsReservedRoad = _ => false;

        /// <summary>Changes when those roads do (a part of the automatic marking's cache key); -1 without them.</summary>
        internal static Func<int> ReservedRevision = () => -1;

        private static readonly Action<float, float, float, System.Collections.Generic.List<HostCandidate>, System.Collections.Generic.List<HostArm>> s_NoHosts = (x, z, radius, posts, arms) => { };

        /// <summary>
        /// 0.12.51: the poles and the arms of another mod (the extension's signal poles) within a radius of (x, z) that boards may be fixed to: the poles go into the first list (as signal poles, with
        /// their radius, their highest and their heads' heights: HostCandidate), the arms into the second (HostArm). The lists come empty; nothing is added without the extension. Main thread.
        /// </summary>
        internal static Action<float, float, float, System.Collections.Generic.List<HostCandidate>, System.Collections.Generic.List<HostArm>> OtherHosts = s_NoHosts;

        /// <summary>
        /// Whether an extension gives poles and arms (0.12.106): the single point has been set (the extension finds it by name and type: do not rename or retype OtherHosts) or a registered one has its own.
        /// What belongs to such poles (the support on a signal arm, the boards' following of them) is there only then.
        /// </summary>
        internal static bool ProvidesHosts => !ReferenceEquals(OtherHosts, s_NoHosts) || Registry.ProvidesHosts;

        /// <summary>A section of an extension in the store beside each save (0.12.41, SaveDataSystem): its format version and the extension's own writer and reader.</summary>
        internal sealed class Section
        {
            public int Version;
            public Func<byte[]> Save;
            public Func<byte[], int, bool> Load;
        }

        /// <summary>The sections registered, by the extension's id.</summary>
        internal static readonly System.Collections.Generic.Dictionary<string, Section> Sections = new System.Collections.Generic.Dictionary<string, Section>();

        /// <summary>
        /// An extension's data kept with this mod's beside each save, not in it (0.12.41, DESIGN-save-data.md 4.9). save is called whenever a save is written (main thread, before the game writes its
        /// file), never before the loaded save's record was put back (a save in those first frames writes no record): its bytes (null or empty: none) go into the save's record, at most
        /// SaveDataSystem.MaxSection; when it throws or gives more, the bytes it was given at the load are kept. load is called once when a record with a section of this id is restored (the city's
        /// entities are there, its loading complete), with the version that wrote it; false (or an exception) means it was not taken, and the section's old bytes are then kept at the next saves
        /// instead of calling save, so that a newer extension's data is never overwritten by an older one. A section of an id no extension registered is kept untouched. The callbacks' exceptions are
        /// caught and logged. Registered again under the same id, the new one replaces the old.
        /// </summary>
        internal static void RegisterSection(string id, int version, Func<byte[]> save, Func<byte[], int, bool> load)
        {
            if (string.IsNullOrEmpty(id) || save == null || load == null)
                throw new ArgumentException("a section needs an id, a writer and a reader");
            Sections[id] = new Section { Version = version, Save = save, Load = load };
            Mod.log.Info($"Extension: section \"{id}\" (version {version}) registered: kept beside each save.");
            SaveDataSystem.Instance?.SectionRegistered(id);
        }

        /// <summary>The extension joins through the single points: from now on the screens are drawn with what it publishes (the Realistic Information Extension, which reads the game for them).</summary>
        internal static void Join(string name)
        {
            Joined = name ?? "";
            LiveContent.On = Joined.Length > 0 || Registry.ProvidesLiveContent;
            Mod.log.Info("Extension: " + Joined + " joined (interface version " + Api + "): the screens follow the game.");
        }

        /// <summary>
        /// 0.12.93 (API 2): an extension joins with its own hooks beside the others (AutoRoadLiner). The screens follow the game only when one of them says it supplies live content
        /// (hooks.ProvidesLiveContent): an extension that does not would switch on screens that have nothing to show. Registered again under the same name, the new hooks replace the old.
        /// </summary>
        internal static void Register(ExtensionRegistry<Entity>.Hooks hooks)
        {
            Registry.Add(hooks);
            LiveContent.On = Joined.Length > 0 || Registry.ProvidesLiveContent;
            Mod.log.Info($"Extension: {hooks.Name} registered (interface level {ApiLevel}){(hooks.ProvidesLiveContent ? ": the screens follow the game" : "")}.");
        }

        /// <summary>Whether a marking lane is one an extension hides itself (the single point, then the registered ones).</summary>
        internal static bool HidesMarking(Entity lane) => OthersHideMarking(lane) || Registry.HidesMarking(lane);

        /// <summary>Whether a road segment or junction is one an extension draws its own lines on.</summary>
        internal static bool IsReserved(Entity road) => IsReservedRoad(road) || Registry.IsReservedRoad(road);

        /// <summary>Changes when the roads of any extension do: ReservedRevision as it was when nothing is registered, mixed with the registered ones' otherwise.</summary>
        internal static int ReservedKey() => Registry.FoldRevision(ReservedRevision());

        /// <summary>The poles and arms of every extension, added to the two lists in the order the extensions joined.</summary>
        internal static void AskHosts(float x, float z, float radius, System.Collections.Generic.List<HostCandidate> posts, System.Collections.Generic.List<HostArm> arms)
        {
            OtherHosts(x, z, radius, posts, arms);
            Registry.AddHosts(x, z, radius, posts, arms);
        }
    }
}
