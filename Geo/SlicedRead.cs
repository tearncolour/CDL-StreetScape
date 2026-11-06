using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A list of reads made a few at a time over several frames (0.12.75, pure): the windows of a long marking each need the roads under them read from the game, and a long line read in one go cost
    /// up to 38 ms on the main thread in the owner's log. A slice reads at least one window (so a slow one cannot stop it) and goes on while the time it has used stays under the budget; what is read
    /// is kept in the order of the windows, and a read that threw is left out. Main thread.
    /// </summary>
    public sealed class SlicedRead<TWindow, TResult>
    {
        private readonly List<TWindow> m_Windows;
        private readonly List<(TWindow Window, TResult Result)> m_Done = new List<(TWindow, TResult)>();
        private int m_Next;

        public SlicedRead(IEnumerable<TWindow> windows)
        {
            m_Windows = new List<TWindow>(windows);
        }

        public int Count => m_Windows.Count;

        /// <summary>How many windows are read (or left out), and whether all of them are.</summary>
        public int Next => m_Next;

        public bool Finished => m_Next >= m_Windows.Count;

        /// <summary>What was read, in the order of the windows.</summary>
        public IReadOnlyList<(TWindow Window, TResult Result)> Done => m_Done;

        /// <summary>Windows whose read threw (the first exception is kept): they are not in Done.</summary>
        public int Failed { get; private set; }

        public Exception FirstFailure { get; private set; }

        /// <summary>
        /// Reads windows with read until elapsedMs (the time the slice has used so far) reaches budgetMs; at least one when any is left. Returns how many it read.
        /// </summary>
        public int Slice(Func<TWindow, TResult> read, Func<double> elapsedMs, double budgetMs)
        {
            int taken = 0;
            while (m_Next < m_Windows.Count && (taken == 0 || elapsedMs() < budgetMs))
            {
                TWindow window = m_Windows[m_Next++];
                taken++;
                try
                {
                    m_Done.Add((window, read(window)));
                }
                catch (Exception ex)
                {
                    Failed++;
                    if (FirstFailure == null)
                        FirstFailure = ex;
                }
            }

            return taken;
        }
    }
}
