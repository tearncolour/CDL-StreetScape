using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// How much of a tile is built (0.7.0). Until 0.6.1 every tile of the map was built at loading and kept, whatever its distance: the cost of loading and the memory of the meshes and the
    /// pictures grew with the number of signs in the whole city, though nothing farther than the far limit is drawn (700 m since 0.7.1: a sign 700 m away is a speck, and a tile of them is built, drawn and shadowed for it). Now a tile has the detail its distance asks for, and none beyond.
    /// </summary>
    public enum TileTier
    {
        /// <summary>Not built: nothing of it exists in the renderer.</summary>
        None = 0,

        /// <summary>
        /// Horizon (0.12.73; the owner: "全封闭隔音墙离远了就消失了"): beyond the far limit, only the items that are seen from far (LongReach: the noise walls and enclosures), in the coarse shape of the far
        /// tier, with no shadows. A tile that has none of them is not built at this distance.
        /// </summary>
        Horizon = 1,

        /// <summary>Far: small pictures on the faces, no LED dots, the supports in reduced detail, the barriers coarse.</summary>
        Far = 2,

        /// <summary>Middle: half-size pictures, the LED dots, the supports in full detail, the barriers reduced.</summary>
        Mid = 3,

        /// <summary>Near: the faces at full size, the barriers in full detail.</summary>
        Near = 4
    }

    /// <summary>The distances (m, from the camera to the nearest point of the tile) up to which a tile has each tier, and how far beyond them it keeps it once it has it.</summary>
    public struct StreamOptions
    {
        public float NearMetres, MidMetres, FarMetres;

        /// <summary>How far the items of LongReach are built and shown (m). 3000 m: a wall 4 m high is 1 px at about 2.5 km on a screen 1080 px high with a 60 degree view, so it is a line and not a speck up to there.</summary>
        public float HorizonMetres;

        /// <summary>A tile that has a tier keeps it until the distance passes its limit by this share of it (0.15: 15 % more): a camera moving about a limit does not rebuild the tile at every step.</summary>
        public float Hysteresis;

        public static StreamOptions Default => new StreamOptions { NearMetres = 70f, MidMetres = 250f, FarMetres = 700f, HorizonMetres = 3000f, Hysteresis = 0.15f };

        /// <summary>
        /// 0.12.103 (the owner, 6 Nov: "建议给标线和牌子设置加一个消失距离的调整 感觉现在消失的距离有点短"): the far limit is the distance up to which every sign and marking is drawn, and it is the player's to set
        /// (the panel's display settings, kept in preferences.json): between LeastFarMetres and MostFarMetres, the panel's steps in FarSteps. A far tile has small pictures, no LED dots, reduced supports
        /// and casts no shadow, so a longer reach costs meshes and their draw calls, not shadow casters; the near and middle limits do not move.
        /// </summary>
        public const float DefaultFarMetres = 700f, LeastFarMetres = 300f, MostFarMetres = 3000f;

        public static readonly float[] FarSteps = { 300f, 500f, 700f, 1000f, 1500f, 2000f, 3000f };

        /// <summary>The far limit as the options hold it: a number within the range (a value that is not one, or NaN, is the default).</summary>
        public static float ClampFar(float farMetres) =>
            float.IsNaN(farMetres) || float.IsInfinity(farMetres) ? DefaultFarMetres : Math.Max(LeastFarMetres, Math.Min(MostFarMetres, farMetres));

        /// <summary>The defaults with another far limit. The horizon (3 km) is the most a far limit can be: at the most there is nothing beyond the far tiles for it.</summary>
        public static StreamOptions WithFar(float farMetres)
        {
            StreamOptions options = Default;
            options.FarMetres = ClampFar(farMetres);
            return options;
        }

        public float LimitOf(TileTier tier)
        {
            switch (tier)
            {
                case TileTier.Near: return NearMetres;
                case TileTier.Mid: return MidMetres;
                case TileTier.Far: return FarMetres;
                case TileTier.Horizon: return HorizonMetres;
                default: return 0f;
            }
        }
    }

    /// <summary>A tile that has to change: what it is to have, and how far it is (the nearest are done first).</summary>
    public struct TileWish
    {
        public TileKey Key;
        public TileTier Tier;
        public float Distance;
    }

    /// <summary>Which tiles are built, at which tier. Pure, so that it is tested offline.</summary>
    public static class TileStreamer
    {
        /// <summary>The distance in the XZ plane from a point to the nearest point of a tile's square: 0 when the point is inside it.</summary>
        public static float DistanceToTile(TileKey key, float x, float z)
        {
            float x0 = key.X * Tiles.Size, z0 = key.Z * Tiles.Size;
            float dx = Math.Max(Math.Max(x0 - x, 0f), x - (x0 + Tiles.Size));
            float dz = Math.Max(Math.Max(z0 - z, 0f), z - (z0 + Tiles.Size));
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Whether a tile is within a distance of a point, without the square root (the planner asks it of every tile with a wall).</summary>
        private static bool Within(TileKey key, float x, float z, float limit)
        {
            float x0 = key.X * Tiles.Size, z0 = key.Z * Tiles.Size;
            float dx = Math.Max(Math.Max(x0 - x, 0f), x - (x0 + Tiles.Size)), dz = Math.Max(Math.Max(z0 - z, 0f), z - (z0 + Tiles.Size));
            return dx * dx + dz * dz < limit * limit;
        }

        /// <summary>
        /// Whether a built tile is shown (0.12.103: the renderer's own test, here so that it is tested): its distance from the camera is inside its reach, which is a share (hysteresis) farther for a tile
        /// that is shown already and a share nearer for one that is not, so that a camera at the limit does not make it flash.
        /// </summary>
        public static bool InReach(float distance, float reach, bool wasShown, float hysteresis) => distance < (wasShown ? reach * (1f + hysteresis) : reach * (1f - hysteresis));

        private static readonly TileTier[] s_Tiers = { TileTier.Horizon, TileTier.Far, TileTier.Mid, TileTier.Near };

        /// <summary>The tier a tile at this distance is to have, given the tier it has: each tier is had up to its limit, and a tier that is had already up to its limit and the hysteresis.</summary>
        public static TileTier Wanted(float distance, TileTier current, StreamOptions options) => Wanted(distance, current, options, false);

        /// <summary>As above; longReach says that the tile has items that are seen from far (LongReach): only a tile that has them has the horizon tier.</summary>
        public static TileTier Wanted(float distance, TileTier current, StreamOptions options, bool longReach)
        {
            TileTier wanted = TileTier.None;
            for (int i = 0; i < s_Tiers.Length; i++)
            {
                TileTier tier = s_Tiers[i];
                if (tier == TileTier.Horizon && !longReach)
                    continue;
                float limit = options.LimitOf(tier) * (current >= tier ? 1f + options.Hysteresis : 1f);
                if (distance < limit)
                    wanted = tier;
            }

            return wanted;
        }

        /// <summary>
        /// The tiles that have to change, from what is built: build (a tile that has none and is to have one, or has another) nearest first, and drop (a tile that has one and is to have none).
        /// The tiles are those that have items and those that are built (a built tile whose items are gone is dropped). A tile for which stale says so is built again whatever its tier.
        /// </summary>
        public static void Plan(IEnumerable<TileKey> withItems, IEnumerable<TileKey> built, Func<TileKey, TileTier> current, float x, float z, StreamOptions options, List<TileWish> toBuild, List<TileKey> toDrop,
            Func<TileKey, bool> hasItems = null, Func<TileKey, bool> stale = null)
        {
            // hasItems tells whether a tile has items (the renderer's store knows); without it the tiles that were seen are kept in a set, which is an allocation for every plan.
            HashSet<TileKey> seen = hasItems == null ? new HashSet<TileKey>() : null;
            foreach (TileKey key in withItems)
            {
                seen?.Add(key);
                TileTier have = current(key);
                float distance = DistanceToTile(key, x, z);
                TileTier want = Wanted(distance, have, options);

                // A tile that is built and whose items changed since is built again, at the tier it is to have.
                if (want == have && !(want != TileTier.None && stale != null && stale(key)))
                    continue;
                if (want == TileTier.None)
                    toDrop.Add(key);
                else
                    toBuild.Add(new TileWish { Key = key, Tier = want, Distance = distance });
            }

            foreach (TileKey key in built)
            {
                bool items = hasItems != null ? hasItems(key) : seen.Contains(key);
                if (!items && current(key) != TileTier.None)
                    toDrop.Add(key);
            }

            toBuild.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        /// <summary>
        /// The same plan as Plan, from the tiles round the point only: the window of tiles that holds every tile any tier can reach (the far limit and its hysteresis), looked up one by one, and the
        /// tiles that are built. A plan costs about the same on a map of a hundred thousand signs as on one of a hundred: Plan looks at every tile that has items. A built tile that is outside the
        /// window, or has no items, is dropped.
        /// </summary>
        public static void PlanAround(Func<TileKey, bool> hasItems, IEnumerable<TileKey> built, Func<TileKey, TileTier> current, float x, float z, StreamOptions options, List<TileWish> toBuild, List<TileKey> toDrop,
            Func<TileKey, bool> stale = null, HashSet<TileKey> longReach = null)
        {
            float reach = options.FarMetres * (1f + options.Hysteresis);
            int x0 = (int)Math.Floor((x - reach) / Tiles.Size), x1 = (int)Math.Floor((x + reach) / Tiles.Size);
            int z0 = (int)Math.Floor((z - reach) / Tiles.Size), z1 = (int)Math.Floor((z + reach) / Tiles.Size);
            for (int tx = x0; tx <= x1; tx++)
            {
                for (int tz = z0; tz <= z1; tz++)
                {
                    var key = new TileKey(tx, tz);
                    if (hasItems(key))
                        Consider(key, longReach != null && longReach.Contains(key), current, x, z, options, toBuild, toDrop, stale);
                }
            }

            // The tiles with a wall beyond the window (up to the horizon): from the store's set of them, a few hundred at most on a big map, and not a look at each of the thousand tiles a window of
            // 3 km would hold (0.12.73: the bench, Tests/Bench/Horizon.cs).
            // Only those within the horizon's reach are looked at (a distance, no look-up of the tile's tier); a built one beyond it is dropped in the loop below.
            float horizon = options.HorizonMetres * (1f + options.Hysteresis);
            if (longReach != null)
            {
                foreach (TileKey key in longReach)
                {
                    if (!(key.X >= x0 && key.X <= x1 && key.Z >= z0 && key.Z <= z1) && Within(key, x, z, horizon))
                        Consider(key, true, current, x, z, options, toBuild, toDrop, stale);
                }
            }

            foreach (TileKey key in built)
            {
                if (current(key) == TileTier.None)
                    continue;
                bool inWindow = key.X >= x0 && key.X <= x1 && key.Z >= z0 && key.Z <= z1;
                if (!hasItems(key) || (!inWindow && !(longReach != null && longReach.Contains(key) && Within(key, x, z, horizon))))
                    toDrop.Add(key);
            }

            toBuild.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        /// <summary>What a tile is to have at its distance: a wish to build it, or to drop it, or nothing when it is as it should be.</summary>
        private static void Consider(TileKey key, bool longReach, Func<TileKey, TileTier> current, float x, float z, StreamOptions options, List<TileWish> toBuild, List<TileKey> toDrop, Func<TileKey, bool> stale)
        {
            TileTier have = current(key);
            float distance = DistanceToTile(key, x, z);
            TileTier want = Wanted(distance, have, options, longReach);
            // A tile that is built and whose items changed since is built again, at the tier it is to have.
            if (want == have && !(want != TileTier.None && stale != null && stale(key)))
                return;
            if (want == TileTier.None)
                toDrop.Add(key);
            else
                toBuild.Add(new TileWish { Key = key, Tier = want, Distance = distance });
        }
    }
}
