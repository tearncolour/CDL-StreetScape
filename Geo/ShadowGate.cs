namespace CDLC.TS.Geo
{
    /// <summary>
    /// Which parts the shadow comparison is made for (0.12.75, pure): at most MaxParts in all, and in a frame no more than PerFrame given to the worker (what the main thread does for each is to copy
    /// the part's curves and stretches: a few microseconds), with no more than MaxOutstanding waiting for it. The comparison itself is made on a thread (ShadowWorker): the first version made it on the
    /// main thread inside a budget of 0.2 ms a frame and compared 22 of 1,095 parts in the owner's log. Main thread.
    /// </summary>
    public sealed class ShadowGate
    {
        public const int MaxParts = 2000;
        public const int PerFrame = 32;
        public const int MaxOutstanding = 256;

        private int m_Frame = -1, m_InFrame;

        /// <summary>Parts given to the worker, and parts that came while the frame or the queue was full.</summary>
        public int Sent { get; private set; }

        public int Skipped { get; private set; }

        public bool Finished => Sent >= MaxParts;

        /// <summary>Whether the part that is made now (in this frame, with this many comparisons waiting) is to be compared as well. A yes counts as sent.</summary>
        public bool Admit(int frame, int outstanding)
        {
            if (Finished)
                return false;
            if (frame != m_Frame)
            {
                m_Frame = frame;
                m_InFrame = 0;
            }

            if (outstanding >= MaxOutstanding || m_InFrame >= PerFrame)
            {
                Skipped++;
                return false;
            }

            m_InFrame++;
            Sent++;
            return true;
        }

        /// <summary>A part that was admitted made nothing to compare (it has no bands): it does not count.</summary>
        public void Withdraw()
        {
            if (Sent > 0)
                Sent--;
            if (m_InFrame > 0)
                m_InFrame--;
        }

        public void Reset()
        {
            m_Frame = -1;
            m_InFrame = 0;
            Sent = Skipped = 0;
        }
    }
}
