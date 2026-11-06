using System;
using System.Collections.Concurrent;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// What reaches the main thread from another one (the game's save events, which its async Save raises from whichever thread it continues on): handled at once when it comes on the main thread,
    /// else kept in order for the next update of the system, which is on it. The entity manager is not to be written from another thread.
    /// </summary>
    public sealed class MainThreadGate<T>
    {
        private readonly int m_MainThread;
        private readonly ConcurrentQueue<T> m_Waiting = new ConcurrentQueue<T>();

        public MainThreadGate(int mainThread)
        {
            m_MainThread = mainThread;
        }

        public bool OnMainThread => Environment.CurrentManagedThreadId == m_MainThread;

        /// <summary>True: the caller is on the main thread and handles the item itself. False: the item was kept for TryTake.</summary>
        public bool Enter(T item)
        {
            if (OnMainThread)
                return true;
            m_Waiting.Enqueue(item);
            return false;
        }

        public bool TryTake(out T item) => m_Waiting.TryDequeue(out item);
    }
}
