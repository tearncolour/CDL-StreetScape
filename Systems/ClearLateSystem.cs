using CDLC.TS.Geo;
using Game;
using Game.SceneFlow;
using Unity.Entities;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// The end of ModificationEnd (0.12.86): the count of the tag before the late pass for the trace, then the late pass itself (ClearMarkingsSystem.ReHideLate). It is registered after the systems
    /// of the game and after those of other mods that run in the first place of the phase (UpdateBefore): the owner's log of 4 Nov showed the tag of every lane this mod hid gone in the same frame, and
    /// Anarchy's RemoveOverridenSystem and Move It's MIT_RemoveOverriddenSystem, which take it off updated lanes, run there. Main thread.
    /// </summary>
    internal sealed partial class ClearLateSystem : GameSystemBase
    {
        private bool m_Looked;

        [Preserve]
        protected override void OnUpdate()
        {
            if (GameManager.instance.gameMode != GameMode.Game)
                return;
            if (!m_Looked)
            {
                m_Looked = true;
                LogStrippers();
            }

            if (ClearTraceLog.Pending)
                ClearTraceLog.Phase(EntityManager, TracePhase.ModificationEndBeforeLate);
            ClearMarkingsSystem.Instance?.ReHideLate();
        }

        /// <summary>
        /// Once: the systems of other mods that take the tag off updated lanes, read by the name of their type (nothing of them is referred to): Anarchy's RemoveOverridenSystem (every updated lane that has an owner
        /// and the tag, under the default and the bulldozer tools) and Move It's MIT_RemoveOverriddenSystem (every updated entity that has the tag, while its tool runs).
        /// </summary>
        private void LogStrippers()
        {
            try
            {
                var found = new System.Collections.Generic.List<string>();
                foreach (ComponentSystemBase system in World.Systems)
                {
                    System.Type type = system.GetType();
                    if ((type.Name == "RemoveOverridenSystem" && (type.Namespace ?? "").Contains("OverridePrevention")) || type.Name == "MIT_RemoveOverriddenSystem")
                        found.Add(type.FullName);
                }

                if (found.Count > 0)
                    Mod.log.Info($"Clear: the system(s) {string.Join(", ", found)} of other mods take the tag off updated lanes (Anarchy's under the default and the bulldozer tools, Move It's while its tool runs); this mod tags the lanes of its areas again at the end of ModificationEnd.");
            }
            catch (System.Exception ex)
            {
                Mod.log.Warn("Clear: the systems of the world could not be looked at: " + ex.Message);
            }
        }
    }
}
