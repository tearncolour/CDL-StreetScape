namespace CDLC.TS.Geo
{
    /// <summary>
    /// Whether ItemRoadSystem's round has anything to look at. The round walks the items a dozen at a time to see that each is still beside its road; once a whole walk from some state of the world has
    /// changed nothing, another one finds the same until the world changes: the road network (NetRevision), the number of items, or an item written (the tool, an undo, a save restored, the system's own
    /// flags). Until then the rounds are skipped, and the system costs the few queries of its frames (the owner's 0.12.75 log: a round of 12 items every 15 frames was 0.14 ms at best).
    /// </summary>
    public sealed class RoundGate
    {
        private int m_Revision = -1, m_Count = -1, m_Checked;
        private bool m_Settled;

        /// <summary>
        /// Whether to make a round now. busy: something is under way that needs the rounds whatever the world says (the first check after a load, a table of edges being made). A change of the world
        /// (or an edit) starts a walk afresh from the state it finds.
        /// </summary>
        public bool Needed(bool busy, int revision, int count, bool edited)
        {
            if (edited || revision != m_Revision || count != m_Count)
            {
                m_Revision = revision;
                m_Count = count;
                m_Checked = 0;
                m_Settled = false;
            }

            return busy || !m_Settled;
        }

        /// <summary>A round looked at done items of items: when the walk since the last change has been over all of them, the gate settles.</summary>
        public void Checked(int done, int items)
        {
            m_Checked += done;
            if (m_Checked >= items)
                m_Settled = true;
        }

        /// <summary>The world was replaced (a city was loaded): nothing is known of it.</summary>
        public void Reset()
        {
            m_Checked = 0;
            m_Settled = false;
        }

        public bool Settled => m_Settled;
    }
}
