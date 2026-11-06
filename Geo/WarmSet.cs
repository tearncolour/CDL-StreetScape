using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CDLC.TS.Geo
{
    /// <summary>What a WarmSet says about a key: its value is there, a worker is making it, or it is for the caller to make.</summary>
    public enum Warmth
    {
        Ready,
        Waiting,
        Fallback
    }

    /// <summary>
    /// Keeps a thing that is slow to make off the main thread: asked for a key whose value is not kept yet, it has a worker make it and says Waiting until it is there. A worker that finished and left
    /// nothing kept (it threw, or the value was pushed out of its cache) gives Fallback once: the caller makes it itself, as it did before there was a worker, so that nothing waits for ever. PlantSystem
    /// (0.12.75): a planter's plan was made on the main thread when the pass offered it, 3 to 30 ms for a long one, and a check between records could not cut that short.
    /// </summary>
    public sealed class WarmSet<TKey>
    {
        private readonly Dictionary<TKey, Task> m_Tasks = new Dictionary<TKey, Task>();
        private readonly int m_MaxInFlight;

        public WarmSet(int maxInFlight = 4)
        {
            m_MaxInFlight = Math.Max(1, maxInFlight);
        }

        /// <summary>How many are being made.</summary>
        public int InFlight => m_Tasks.Count;

        /// <summary>How many workers were started and how many times a fallback was given (for the log).</summary>
        public int Started { get; private set; }

        public int Fallbacks { get; private set; }

        /// <summary>
        /// kept: whether the value is there now. make runs on a worker. Main thread only. At the limit of workers in flight a key that has none is Waiting without one: it is asked again.
        /// </summary>
        public Warmth Ask(TKey key, bool kept, Action make)
        {
            if (kept)
            {
                m_Tasks.Remove(key);
                return Warmth.Ready;
            }

            if (m_Tasks.TryGetValue(key, out Task task))
            {
                if (!task.IsCompleted)
                    return Warmth.Waiting;
                m_Tasks.Remove(key);
                Fallbacks++;
                return Warmth.Fallback;
            }

            if (m_Tasks.Count >= m_MaxInFlight)
                return Warmth.Waiting;
            m_Tasks[key] = Task.Run(make);
            Started++;
            return Warmth.Waiting;
        }

        /// <summary>Forgets what is being made (a city is left): the workers finish, their results are kept by whatever they fill.</summary>
        public void Clear() => m_Tasks.Clear();
    }
}
