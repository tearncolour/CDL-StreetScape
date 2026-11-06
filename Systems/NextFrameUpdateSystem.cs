using System.Collections.Generic;
using CDLC.TS.Geo;
using Game;
using Game.Common;
using Game.SceneFlow;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// Gives entities Updated at the first modification phase of the next frame (0.12.82). A system of this mod that runs after the game's modification systems in a frame (MainLoop after
    /// ModificationSystem, or the end of Modification4B) cannot make them look at a road: PrepareCleanUpSystem, the last system of MainLoop, collects every entity with Updated and CleanUpSystem takes
    /// it off again at the end of the frame, so the game's net and object systems, which had run already, never see it (road kerb styles put back after a load, lane arrows asked for after an area
    /// went). Registered at Modification1, as PlantSystem is: nothing of the game that reads Updated runs before it.
    /// </summary>
    internal sealed partial class NextFrameUpdateSystem : GameSystemBase
    {
        private const int kLogged = 8;

        private readonly Deferred<Entity> m_Wanted = new Deferred<Entity>();
        private int m_Logged;

        internal static NextFrameUpdateSystem Instance { get; private set; }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        internal void Mark(IEnumerable<Entity> entities)
        {
            foreach (Entity entity in entities)
                m_Wanted.Add(entity);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (m_Wanted.Count == 0)
                return;
            if (GameManager.instance.gameMode != GameMode.Game)
            {
                m_Wanted.Clear();
                return;
            }

            List<Entity> alive = m_Wanted.Take(entity => EntityManager.Exists(entity) && !EntityManager.HasComponent<Deleted>(entity));
            if (alive.Count == 0)
                return;
            using (var array = new NativeArray<Entity>(alive.ToArray(), Allocator.Temp))
                EntityManager.AddComponent<Updated>(array);
            if (m_Logged++ < kLogged)
                Mod.log.Info($"Updated: {alive.Count} road piece(s) given Updated at the start of the frame, where the game's net and object systems still look at them.");
            ClearMarkingsSystem.Instance?.AskCensus(CensusMoment.AfterUpdated);
        }
    }
}
