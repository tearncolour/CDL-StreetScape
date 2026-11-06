using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What a plant of a planter is to be: where it stands, its heading, the hash of those (it is made again when the hash changes) and the prop of the plan it stands for.</summary>
    public struct PlantWish
    {
        public Vec3 At;
        public float Yaw;
        public uint Hash;
        public PropAnchor Anchor;
    }

    /// <summary>The hash of a plant's place, heading, index and kind: the same figures as before 0.12.72 (the species and the turn of a plant come of it, so a city keeps its plants), without the array it made each call.</summary>
    public static class PlantHash
    {
        public static uint Of(Vec3 at, float yaw, uint index, string kind)
        {
            unchecked
            {
                uint h = SignCatalog.HashOf(kind ?? "");
                h = (h ^ (uint)(int)Math.Round(at.X * 100f)) * 16777619u;
                h = (h ^ (uint)(int)Math.Round(at.Y * 100f)) * 16777619u;
                h = (h ^ (uint)(int)Math.Round(at.Z * 100f)) * 16777619u;
                h = (h ^ (uint)(int)Math.Round(yaw * 1000f)) * 16777619u;
                return (h ^ index) * 16777619u;
            }
        }
    }

    /// <summary>
    /// The plants of the planters as the system keeps them (0.12.72; the owner's profile: PlantSystem 2.36 ms in the first minutes): which plant stands for which place of which item, and the work to
    /// bring that in line with what the items want now, in slices that stop at a deadline. A pass offers the wish of every place (Offer: one already there as wished is left; one that differs goes to
    /// the plants to take away and is wished again), then Sweep takes away what stands and was not offered, then TryTake hands out the wishes still to be made. Nothing here knows the game: a key is
    /// an item and an index, a plant is whatever the system keeps (an entity). Main thread.
    /// </summary>
    public sealed class PlantLedger<TKey, TPlant> where TKey : IEquatable<TKey>
    {
        private struct Standing
        {
            public TPlant Plant;
            public uint Hash;
        }

        private readonly Dictionary<TKey, Standing> m_Standing = new Dictionary<TKey, Standing>();
        private readonly Dictionary<TKey, PlantWish> m_Wished = new Dictionary<TKey, PlantWish>();
        private readonly List<TKey> m_Pending = new List<TKey>();
        private readonly HashSet<TKey> m_Offered = new HashSet<TKey>();
        private readonly List<TKey> m_Sweep = new List<TKey>();
        private int m_SweepAt;

        /// <summary>The plants that stand.</summary>
        public int Count => m_Standing.Count;

        /// <summary>The wishes not made yet.</summary>
        public int PendingCount => m_Pending.Count;

        /// <summary>How many plants were made and how many were taken away, over the life of the ledger (for the log).</summary>
        public int Made { get; private set; }

        public int Gone { get; private set; }

        /// <summary>Starts a pass: nothing is offered yet, and the plants that stand are listed for the sweep at its end.</summary>
        public void BeginPass()
        {
            m_Offered.Clear();
            m_Sweep.Clear();
            m_Sweep.AddRange(m_Standing.Keys);
            m_SweepAt = 0;
        }

        /// <summary>
        /// The wish of a place. A plant that stands there as wished and is still alive is left (false). Otherwise the plant that stands there, if any, is added to gone (the caller takes it away)
        /// and the wish is queued (true).
        /// </summary>
        public bool Offer(TKey key, in PlantWish wish, Func<TPlant, bool> alive, List<TPlant> gone)
        {
            if (m_Standing.TryGetValue(key, out Standing standing))
            {
                m_Offered.Add(key);
                if (standing.Hash == wish.Hash && alive(standing.Plant))
                    return false;
                gone.Add(standing.Plant);
                m_Standing.Remove(key);
                Gone++;
            }

            m_Wished[key] = wish;
            m_Pending.Add(key);
            return true;
        }

        /// <summary>
        /// The end of a pass, a slice at a time: the plants that stand and were not offered are added to gone. True when the whole list was gone through; false when the deadline (a
        /// Stopwatch.GetTimestamp() value) came first, and the next call goes on.
        /// </summary>
        public bool Sweep(long deadline, List<TPlant> gone)
        {
            int n = 0;
            while (m_SweepAt < m_Sweep.Count)
            {
                TKey key = m_Sweep[m_SweepAt++];
                if (!m_Offered.Contains(key) && m_Standing.TryGetValue(key, out Standing standing))
                {
                    gone.Add(standing.Plant);
                    m_Standing.Remove(key);
                    Gone++;
                }

                if ((++n & 63) == 0 && Stopwatch.GetTimestamp() >= deadline)
                    break;
            }

            if (m_SweepAt < m_Sweep.Count)
                return false;
            m_Sweep.Clear();
            m_Offered.Clear();
            return true;
        }

        /// <summary>The next wish to be made (the newest first); false when there is none. The caller makes the plant and says so with Placed.</summary>
        public bool TryTake(out TKey key, out PlantWish wish)
        {
            while (m_Pending.Count > 0)
            {
                key = m_Pending[m_Pending.Count - 1];
                m_Pending.RemoveAt(m_Pending.Count - 1);
                if (m_Wished.TryGetValue(key, out wish))
                {
                    m_Wished.Remove(key);
                    return true;
                }
            }

            key = default;
            wish = default;
            return false;
        }

        /// <summary>A plant was made for a wish that was taken.</summary>
        public void Placed(TKey key, TPlant plant, uint hash)
        {
            m_Standing[key] = new Standing { Plant = plant, Hash = hash };
            Made++;
        }

        /// <summary>Everything is forgotten (the city's objects were cleared): the plants are wished again from the items.</summary>
        public void Clear()
        {
            m_Standing.Clear();
            m_Wished.Clear();
            m_Pending.Clear();
            m_Offered.Clear();
            m_Sweep.Clear();
            m_SweepAt = 0;
        }
    }
}
