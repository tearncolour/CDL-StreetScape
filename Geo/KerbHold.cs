using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Whether the work that reads the road pieces' kerbs waits (pure): while the game is still loading some piece's geometry (FaceBook.BusyNow), a read would take that piece as one surface, so the
    /// reads are held back. The hold is of time, not of frames (a loading screen draws a frame in some 90 ms), and ends after maxSeconds of one run of it: a piece the game never finishes does not stop
    /// the markings for ever. Main thread.
    /// </summary>
    public sealed class KerbHold
    {
        private readonly double m_Max;
        private double m_Since = -1.0;
        private bool m_Expired, m_Reported;

        public KerbHold(double maxSeconds)
        {
            m_Max = maxSeconds;
        }

        /// <summary>Called once a frame there is work: whether busy pieces still wait (busy) and the time (now, seconds). True: hold the reads this frame.</summary>
        public bool Holds(int busy, double now)
        {
            if (busy <= 0)
            {
                m_Since = -1.0;
                m_Expired = m_Reported = false;
                return false;
            }

            if (m_Since < 0.0)
                m_Since = now;
            if (now - m_Since >= m_Max)
            {
                m_Expired = true;
                return false;
            }

            return true;
        }

        public bool Expired => m_Expired;

        /// <summary>True once for each run of the hold that ran out (for the log).</summary>
        public bool TakeExpiry()
        {
            if (!m_Expired || m_Reported)
                return false;
            m_Reported = true;
            return true;
        }

        public void Reset()
        {
            m_Since = -1.0;
            m_Expired = m_Reported = false;
        }
    }

    /// <summary>
    /// What was made while a piece's kerbs were not known (pure): the items are noted as they are made, and when the epoch has moved (FaceBook.Epoch: a busy piece became known) they are handed back, once,
    /// to be made again. An item made again from known pieces is not noted again. Main thread.
    /// </summary>
    public sealed class ProvisionalLedger<T>
    {
        private readonly HashSet<T> m_Items = new HashSet<T>();
        private int m_Epoch;

        public int Count => m_Items.Count;

        public void Note(T item) => m_Items.Add(item);

        /// <summary>The ledger is emptied for a new city; epoch is the one the new city starts at.</summary>
        public void Clear(int epoch)
        {
            m_Items.Clear();
            m_Epoch = epoch;
        }

        /// <summary>When the epoch is not the one last seen: the items noted so far go into redo and out of the ledger. True when there were some.</summary>
        public bool TakeRedo(int epoch, List<T> redo)
        {
            if (epoch == m_Epoch)
                return false;
            m_Epoch = epoch;
            if (m_Items.Count == 0)
                return false;
            redo.AddRange(m_Items);
            m_Items.Clear();
            return true;
        }
    }
}
