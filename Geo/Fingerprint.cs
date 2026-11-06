using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A 64-bit hash of numbers fed in a fixed order (DESIGN-surface-cache.md): what a cached result was made from is hashed when it is kept, and again when it is asked for; the same hash means the
    /// same inputs, up to the chance of a collision (about 2^-64 for any two different lists). Floats go in by their bit patterns, so -0 and 0 are two inputs and so are two NaNs of other bits: a
    /// false match is the harm (a stale result), an extra miss only costs a recomputation. The order matters. Not for secrecy, and not stable between versions of this code.
    /// </summary>
    public struct Fingerprint
    {
        private ulong m_Hash;
        private bool m_Started;

        public ulong Value => m_Started ? m_Hash : Seed;

        private const ulong Seed = 0xcbf29ce484222325UL;

        public void Add(float value) => Mix((ulong)BitConverter.DoubleToInt64Bits(value));

        public void Add(int value) => Mix((uint)value);

        public void Add(ulong value) => Mix(value);

        private void Mix(ulong word)
        {
            unchecked
            {
                ulong h = (m_Started ? m_Hash : Seed) ^ word;
                h *= 0xff51afd7ed558ccdUL;
                h ^= h >> 33;
                h *= 0xc4ceb9fe1a85ec53UL;
                h ^= h >> 33;
                m_Hash = h;
                m_Started = true;
            }
        }
    }
}
