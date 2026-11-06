using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The game's plants the road greening asks for (0.12.49; the owner: "所有的绿化相关植物花卉都应该尽量使用原版"): a kind names what a place wants (PropAnchor.Kind), and each kind is a list of the game's own
    /// prefabs (PlantPrefabSystem copies them, PlantSystem chooses among them where they are put). Nothing green is modelled by the mod. The six bushes of the planters (0.12.32) were copied in the game
    /// (CDLC.TS.Mod.log, 29 Oct: all six, their widths); every other name is from the Placemaker mod's dump of the owner's game catalogue (ModsData/Placemaker/catalog-dump.json, 26 Oct, flagged
    /// vanilla) and has not been put in a city by this mod yet: a name the game does not have is left out (nothing drawn, logged). Pure.
    /// </summary>
    public static class GreenPlants
    {
        /// <summary>A planter's plant: any of the cultivated bushes, flowering or not (0.12.32).</summary>
        public const string Planter = "planter";

        /// <summary>A flowering bush (栽培花丛): the roses of an elevated road's boxes, a flower belt.</summary>
        public const string Flower = "flower";

        /// <summary>A cultivated green bush (栽培灌木丛): the clipped shrub balls (球形灌木) of a median, the shrubs of a tree strip.</summary>
        public const string Shrub = "shrub";

        /// <summary>
        /// A piece of the game's low trimmed hedge (低篱笆组件 02, the piece its low hedge fence is made of): 4.2 m long along its +Z, 1.24 m wide and 1.19 m high (the dump's bounds); laid end to end along a
        /// belt as a clipped hedge (绿篱), only ever turned end for end.
        /// </summary>
        public const string Hedge = "hedge";

        public const float HedgeLength = 4.2f, HedgeWidth = 1.24f, HedgeHeight = 1.19f;

        /// <summary>
        /// A flower pot (花盆) for the top of a wall or a ledge: the game's FlowerPot02 (0.52 x 0.92 x 0.49 m, the dump's bounds) first, then FlowerPot01 (0.84 x 1.03 x 0.84 m). A place with a room of 0.15 m has only the
        /// narrowest (PlantSystem.Choose takes the copies up to three times the room, or the narrowest alone). Not seen in a city yet: a name the game does not have is left out and logged.
        /// </summary>
        public const string Pot = "pot";

        /// <summary>A street tree of a species ("tree.plane"), young (".young": the state of a tree still held by its guard).</summary>
        public const string TreePrefix = "tree.", YoungSuffix = ".young";

        /// <summary>
        /// The street trees: the species' keys and the game's tree each is drawn with. 英桐 is the London plane (二球悬铃木, the 法国梧桐 of Shanghai and Nanjing), the royal palm the street palm of Guangzhou,
        /// Shenzhen and Xiamen; the game's linden, chestnut and poplar stand for the broadleaved street trees of the north. The game has no ginkgo, camphor or pagoda tree.
        /// </summary>
        public static readonly (string Species, string Prefab, string Zh, string En)[] Trees =
        {
            ("plane", "NA_LondonPlaneTree01", "悬铃木（英桐）", "London plane"), ("linden", "NA_LindenTree01", "椴树", "Linden"), ("chestnut", "EU_ChestnutTree01", "栗树", "Chestnut"),
            ("poplar", "EU_PoplarTree01", "杨树", "Poplar"), ("palm", "RoyalPalmTree01", "大王椰", "Royal palm")
        };

        private static readonly string[] kFlowers = { "FlowerBushCity01", "FlowerBushCity02", "FlowerBushCity03" };
        private static readonly string[] kShrubs = { "GreenBushCity01", "GreenBushCity02", "GreenBushCity03" };
        private static readonly string[] kHedges = { "HedgePieceLow02" };
        private static readonly string[] kPots = { "FlowerPot02", "FlowerPot01" };
        private static readonly string[] kNone = new string[0];

        private static string[] s_All;

        /// <summary>Every prefab a kind may use: the planters' six first, in the order 0.12.32 copied them.</summary>
        public static string[] All
        {
            get
            {
                if (s_All != null)
                    return s_All;
                var all = new List<string>(kFlowers);
                all.AddRange(kShrubs);
                all.AddRange(kHedges);
                foreach (var (_, prefab, _, _) in Trees)
                    all.Add(prefab);
                all.AddRange(kPots);
                return s_All = all.ToArray();
            }
        }

        public static string TreeKind(string species, bool young) => TreePrefix + species + (young ? YoungSuffix : "");

        /// <summary>The game's prefabs a kind is drawn with (none for a kind that is not known).</summary>
        public static IReadOnlyList<string> PrefabsOf(string kind)
        {
            switch (kind)
            {
                case Planter:
                {
                    var both = new List<string>(kFlowers);
                    both.AddRange(kShrubs);
                    return both;
                }
                case Flower: return kFlowers;
                case Shrub: return kShrubs;
                case Hedge: return kHedges;
                case Pot: return kPots;
            }

            if (kind == null || !kind.StartsWith(TreePrefix, StringComparison.Ordinal))
                return kNone;
            string species = kind.Substring(TreePrefix.Length);
            if (species.EndsWith(YoungSuffix, StringComparison.Ordinal))
                species = species.Substring(0, species.Length - YoungSuffix.Length);
            foreach (var (key, prefab, _, _) in Trees)
            {
                if (key == species)
                    return new[] { prefab };
            }

            return kNone;
        }

        /// <summary>Whether a kind is laid along the line it is put on (a hedge piece) rather than turned at random as a plant is.</summary>
        public static bool KeepsHeading(string kind) => kind == Hedge;

        public static bool IsTree(string kind) => kind != null && kind.StartsWith(TreePrefix, StringComparison.Ordinal);

        public static bool IsYoung(string kind) => IsTree(kind) && kind.EndsWith(YoungSuffix, StringComparison.Ordinal);
    }
}
