namespace CDLC.TS.Geo
{
    /// <summary>
    /// Whether the work that reads the terrain waits a frame (0.12.75, pure). The game's TerrainSystem.GetHeightData(true) blocks the main thread on a read back of the height map from the graphics card
    /// when the map has changed (a city loaded, a road built or moved, terraforming): the owner's 0.12.75 log has frames of 39 and 73 ms in the finishing of a 31-point marking, which costs 0.2 ms
    /// otherwise. The terrain system clears the flag itself within a few frames, so the work that needs the terrain is held back while it is set, and only after MaxHoldFrames of it is let through to
    /// block as it always did (a player who terraforms all the time still gets his markings). Main thread.
    /// </summary>
    public sealed class TerrainGate
    {
        public const int MaxHoldFrames = 120;

        private int m_Waited;

        /// <summary>Frames the work was held back in all, times it was let through to block, and the longest run of held frames.</summary>
        public int Held { get; private set; }

        public int Forced { get; private set; }

        public int Longest { get; private set; }

        /// <summary>Called once a frame there is work: whether the terrain still waits for a read back (pending). True: hold the work that reads the terrain this frame.</summary>
        public bool Hold(bool pending)
        {
            if (!pending)
            {
                m_Waited = 0;
                return false;
            }

            if (m_Waited >= MaxHoldFrames)
            {
                m_Waited = 0;
                Forced++;
                return false;
            }

            m_Waited++;
            Held++;
            if (m_Waited > Longest)
                Longest = m_Waited;
            return true;
        }

        public void Reset()
        {
            m_Waited = 0;
            Held = Forced = Longest = 0;
        }
    }
}
