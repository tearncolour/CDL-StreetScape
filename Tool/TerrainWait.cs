using System;
using System.Diagnostics;
using System.Reflection;
using CDLC.TS.Render;
using Game.Simulation;

namespace CDLC.TS
{
    /// <summary>
    /// The terrain's height data without stalling the frame (0.12.75). TerrainSystem.GetHeightData(true) waits for the read back of the height map from the graphics card when the map has changed, and
    /// the game keeps no public flag for it: the private m_HeightMapChanged says so (the system clears it itself within a few frames). Pending reads it once, so that the work that needs the terrain
    /// can wait (TerrainGate); if the field is not there, in this version of the game, nothing waits and the call blocks as it always did. HeightData is that call, timed, so that the log says what it
    /// costs. Main thread.
    /// </summary>
    internal static class TerrainWait
    {
        private static readonly FieldInfo s_Changed = typeof(TerrainSystem).GetField("m_HeightMapChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool s_Failed;

        /// <summary>Whether the terrain's height map has changed and its read back is not done: a call of GetHeightData(true) would block now.</summary>
        internal static bool Pending(TerrainSystem terrain)
        {
            if (terrain == null || s_Failed || s_Changed == null || s_Changed.FieldType != typeof(bool))
                return false;
            try
            {
                return (bool)s_Changed.GetValue(terrain);
            }
            catch (Exception ex)
            {
                s_Failed = true;
                Mod.log.Warn("Terrain: the height map's flag could not be read; the markings' finishing blocks on the terrain as before: " + ex.Message);
                return false;
            }
        }

        /// <summary>GetHeightData(true) for a caller that needs the heights as they are now; the time it took goes to the log's statistics (a stall shows as the worst).</summary>
        internal static TerrainHeightData HeightData(TerrainSystem terrain)
        {
            if (terrain == null)
                return default;
            long lap = Stopwatch.GetTimestamp();
            TerrainHeightData data = terrain.GetHeightData(true);
            double ms = (Stopwatch.GetTimestamp() - lap) * 1000.0 / Stopwatch.Frequency;
            SlowLog.Add("Terrain: height data (waits for a read back)", ms, SlowLog.IsSlow(ms) ? "GetHeightData(true) blocked" : null);
            return data;
        }
    }
}
