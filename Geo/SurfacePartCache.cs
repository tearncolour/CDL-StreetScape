using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// What the read of one part of a road made (DESIGN-surface-cache.md): the strips, and what it added to the read's counts of segments and junction parts, with the fingerprint of what it was made
    /// from. The strips are shared by every read that takes them: nobody may change them (SurfaceStrip's lists cannot be written to from outside; its flags are set by the read that makes it).
    /// </summary>
    public sealed class SurfacePart
    {
        public ulong Fingerprint;
        public SurfaceStrip[] Strips;
        public int Segments, Junctions;
    }

    /// <summary>
    /// The cache of the parts of roads by key (the game's edge, which part, which rule), bounded by an estimated number of bytes (SieveCache). An entry is good when its key is there and its
    /// fingerprint is the one asked for; a key that is there with another fingerprint is a road that changed (Stale), and is made again and put over it. Pure, so that it is tested offline.
    /// </summary>
    public sealed class SurfacePartCache<TKey> where TKey : IEquatable<TKey>
    {
        /// <summary>An estimate, not a measure: a point is a Vec3 (12 bytes), and a strip has about 64 bytes of its own (the object, two lists, the wrapper and the strings it points to).</summary>
        public const int BytesPerPoint = 12, BytesPerStrip = 64;

        private readonly SieveCache<TKey, SurfacePart> m_Cache;

        public SurfacePartCache(long budgetBytes)
        {
            m_Cache = new SieveCache<TKey, SurfacePart>(budgetBytes);
        }

        public int Count => m_Cache.Count;

        /// <summary>The estimated bytes held and the budget.</summary>
        public long Bytes => m_Cache.Weight;

        public long Budget => m_Cache.Budget;

        /// <summary>Asks that were served, that were not (Stale included), and that found the key with another fingerprint.</summary>
        public int Hits { get; private set; }

        public int Misses { get; private set; }

        public int Stale { get; private set; }

        /// <summary>
        /// Adds what the part made to a read, when it is held for this fingerprint: its strips after the ones that are there, its counts to segments and junctions. False when it is not held (or held
        /// for other inputs): the caller makes the part and keeps it.
        /// </summary>
        public bool TryAdd(TKey key, ulong fingerprint, List<SurfaceStrip> into, ref int segments, ref int junctions)
        {
            if (m_Cache.TryGet(key, out SurfacePart part))
            {
                if (part.Fingerprint == fingerprint)
                {
                    Hits++;
                    into.AddRange(part.Strips);
                    segments += part.Segments;
                    junctions += part.Junctions;
                    return true;
                }

                Stale++;
            }

            Misses++;
            return false;
        }

        /// <summary>Keeps what a part made: the strips of a read from index first on, and the counts it added. An entry heavier than the budget is not kept.</summary>
        public void Keep(TKey key, ulong fingerprint, List<SurfaceStrip> from, int first, int segments, int junctions)
        {
            var strips = new SurfaceStrip[from.Count - first];
            from.CopyTo(first, strips, 0, strips.Length);
            m_Cache.Set(key, new SurfacePart { Fingerprint = fingerprint, Strips = strips, Segments = segments, Junctions = junctions }, WeightOf(strips));
        }

        public void Clear()
        {
            m_Cache.Clear();
            Hits = Misses = Stale = 0;
        }

        /// <summary>The estimated bytes of strips: the points of both lists and a fixed part for each strip.</summary>
        public static long WeightOf(SurfaceStrip[] strips)
        {
            long bytes = 0;
            foreach (SurfaceStrip strip in strips)
                bytes += BytesPerPoint * (long)(strip.Left.Count + strip.Right.Count) + BytesPerStrip;
            return Math.Max(1, bytes);
        }
    }
}
