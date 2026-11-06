using System.Collections.Generic;
using CDLC.TS.Geo;
using Colossal.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Unity.Entities;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// What became of the marking lanes this mod hid (0.12.84, ClearTrace): every batch the passes and the census hide is looked at again in the frame it was hidden in (at ModificationEnd, after every
    /// system of the game that could touch the tag and before the clean-up), at the start of the next frame (Modification1) and 120 frames later, and the log says whether the tag stayed, the same lane
    /// lost it, or the lane is not there any more (the game made it again as another entity). The owner's log of 3 Nov: a pass hid 66 lanes and 0.4 s later none carried the tag. Main thread.
    /// </summary>
    internal static class ClearTraceLog
    {
        private sealed class Batch
        {
            public Entity[] Lanes;
            public int Frame;
            public double Time;
            public string Source;
            public int Done;

            /// <summary>The counts of the first few batches of a load at each place of the frame (ClearPhases); null for the others.</summary>
            public ClearPhases.Tally Phases;

            public string FirstMissing;
        }

        private static readonly ClearTrace.HiddenRing s_Ring = new ClearTrace.HiddenRing();
        private static readonly List<Batch> s_Batches = new List<Batch>();
        private static int s_Lines, s_Probed;
        private static bool s_ShownAtHiding, s_ShownLost;

        /// <summary>Whether a look is still to be made: the systems below do nothing while it is not.</summary>
        internal static bool Pending => s_Batches.Count > 0;

        /// <summary>A new city: nothing is remembered and the log has all its lines again.</summary>
        internal static void Reset()
        {
            s_Ring.Clear();
            s_Batches.Clear();
            s_Lines = s_Probed = 0;
            s_ShownAtHiding = s_ShownLost = false;
        }

        /// <summary>The lanes hidden just now (the tag was put on them): remembered, and looked at again at the three stages while the log has lines left.</summary>
        internal static void Hid(EntityManager em, List<Entity> lanes, string source)
        {
            if (lanes.Count == 0)
                return;
            if (!s_ShownAtHiding)
            {
                s_ShownAtHiding = true;
                Mod.log.Info($"Clear: trace: the first lane hidden ({lanes[0].Index}:{lanes[0].Version}) has the components {ComponentsOf(em, lanes[0])}.");
            }

            int frame = UnityEngine.Time.frameCount;
            double time = UnityEngine.Time.realtimeSinceStartupAsDouble;
            foreach (Entity lane in lanes)
                s_Ring.Add(ClearTrace.Key(lane.Index, lane.Version), frame, time, source);
            if (s_Lines < ClearTrace.MaxLines)
            {
                var batch = new Batch { Lanes = lanes.ToArray(), Frame = frame, Time = time, Source = source };
                if (s_Probed < ClearPhases.ProbeBatches)
                {
                    s_Probed++;
                    batch.Phases = new ClearPhases.Tally(batch.Lanes.Length);
                    Count(em, batch, TracePhase.AfterHide);
                }

                s_Batches.Add(batch);
            }
        }

        /// <summary>How many lanes of the batches hidden in this frame carry the tag at this place of the frame, for the batches that are followed phase by phase.</summary>
        internal static void Phase(EntityManager em, TracePhase phase)
        {
            int now = UnityEngine.Time.frameCount;
            foreach (Batch batch in s_Batches)
            {
                if (batch.Phases != null && batch.Frame == now && !batch.Phases.Has(phase))
                    Count(em, batch, phase);
            }
        }

        private static void Count(EntityManager em, Batch batch, TracePhase phase)
        {
            int tagged = 0;
            Entity missing = Entity.Null;
            foreach (Entity lane in batch.Lanes)
            {
                if (em.Exists(lane) && em.HasComponent<Overridden>(lane))
                    tagged++;
                else if (missing == Entity.Null && em.Exists(lane))
                    missing = lane;
            }

            batch.Phases.Set(phase, tagged);
            if (batch.FirstMissing == null && missing != Entity.Null)
                batch.FirstMissing = $"{Describe(em, missing, LaneFate.TagGone)}, owner is {OwnerKind(em, missing)}{(em.HasComponent<Game.Tools.Temp>(missing) ? ", Temp" : "")}, seen at {ClearPhases.Name(phase)}";
        }

        private static string OwnerKind(EntityManager em, Entity lane) =>
            !em.TryGetComponent(lane, out Owner link) || !em.Exists(link.m_Owner) ? "none" : em.HasComponent<Edge>(link.m_Owner) ? "an edge" : em.HasComponent<Node>(link.m_Owner) ? "a node" : "another entity";

        /// <summary>What the trace knows of a lane that is visible: hidden by this mod a moment ago, or not.</summary>
        internal static string Explain(Entity lane)
        {
            bool known = s_Ring.TryGet(ClearTrace.Key(lane.Index, lane.Version), out ClearTrace.Entry entry);
            return ClearTrace.Explain(known, entry, UnityEngine.Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>The looks that are due at this stage in this frame.</summary>
        internal static void Look(EntityManager em, TraceStage stage)
        {
            int now = UnityEngine.Time.frameCount;
            for (int i = s_Batches.Count - 1; i >= 0; i--)
            {
                Batch batch = s_Batches[i];
                if (ClearTrace.Due(stage, batch.Done, batch.Frame, now))
                {
                    batch.Done |= 1 << (int)stage;
                    if (stage == TraceStage.SameFrame && batch.Phases != null)
                        Mod.log.Info(ClearPhases.Format(batch.Source, batch.Phases, batch.FirstMissing));
                    if (s_Lines < ClearTrace.MaxLines)
                        Report(em, batch, stage, now);
                }

                if ((batch.Done & 6) == 6 || s_Lines >= ClearTrace.MaxLines)
                    s_Batches.RemoveAt(i);
            }
        }

        private static void Report(EntityManager em, Batch batch, TraceStage stage, int now)
        {
            var tally = new ClearTrace.FateTally();
            foreach (Entity lane in batch.Lanes)
            {
                bool exists = em.Exists(lane);
                LaneFate fate = ClearTrace.Judge(exists, exists && em.HasComponent<Deleted>(lane), exists && em.HasComponent<Overridden>(lane));
                tally.Add(fate, () => Describe(em, lane, fate));
                if (fate == LaneFate.TagGone && !s_ShownLost)
                {
                    s_ShownLost = true;
                    Mod.log.Info($"Clear: trace: the first lane that lost the tag ({lane.Index}:{lane.Version}) has the components {ComponentsOf(em, lane)}.");
                }
            }

            s_Lines++;
            int tagged = ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.TaggedNow() : -1;
            Mod.log.Info(ClearTrace.Format(stage, batch.Source, now - batch.Frame, UnityEngine.Time.realtimeSinceStartupAsDouble - batch.Time, tally, tagged));
        }

        /// <summary>The component types of an entity, in one short list: to see whether a lane that lost the tag lost anything else.</summary>
        private static string ComponentsOf(EntityManager em, Entity lane)
        {
            if (!em.Exists(lane))
                return "(none: the entity is gone)";
            using (var types = em.GetComponentTypes(lane, Unity.Collections.Allocator.Temp))
            {
                var names = new List<string>(types.Length);
                for (int i = 0; i < types.Length; i++)
                    names.Add(types[i].GetManagedType()?.Name ?? "?");
                names.Sort(string.CompareOrdinal);
                return string.Join(" ", names);
            }
        }

        /// <summary>One lane named in a line: its prefab, whose it is and what the game is doing to it and to its owner now (Created and Updated are still on them in the frame's own look).</summary>
        private static string Describe(EntityManager em, Entity lane, LaneFate fate)
        {
            if (fate == LaneFate.Gone)
                return $"{lane.Index}:{lane.Version}";
            string name = em.TryGetComponent(lane, out PrefabRef prefabRef) && ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.PrefabNameOf(prefabRef.m_Prefab) : "?";
            string owner = "no owner";
            if (em.TryGetComponent(lane, out Owner link) && em.Exists(link.m_Owner))
                owner = $"owner {link.m_Owner.Index}:{link.m_Owner.Version}{(em.HasComponent<Updated>(link.m_Owner) ? " Updated" : "")}{(em.HasComponent<Created>(link.m_Owner) ? " Created" : "")}{(TsExtension.IsReserved(link.m_Owner) ? " reserved by another mod" : "")}";
            return $"\"{name}\" {lane.Index}:{lane.Version}{(em.HasComponent<Created>(lane) ? " Created" : "")}{(em.HasComponent<Updated>(lane) ? " Updated" : "")}{(TsExtension.HidesMarking(lane) ? " wanted hidden by another mod" : "")}, {owner}";
        }
    }

    /// <summary>The look in the frame the lanes were hidden in: ModificationEnd, after every modification system and before the clean-up takes Created and Updated off.</summary>
    internal sealed partial class ClearTraceEndSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            if (!ClearTraceLog.Pending || GameManager.instance.gameMode != GameMode.Game)
                return;
            ClearTraceLog.Phase(EntityManager, TracePhase.ModificationEndAfterLate);
            ClearTraceLog.Look(EntityManager, TraceStage.SameFrame);
        }
    }

    /// <summary>The count at the end of Modification4B, after this mod's own pass (0.12.86).</summary>
    internal sealed partial class ClearProbe4BSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            if (ClearTraceLog.Pending && GameManager.instance.gameMode == GameMode.Game)
                ClearTraceLog.Phase(EntityManager, TracePhase.Modification4BEnd);
        }
    }

    /// <summary>The count at the end of Modification5, after the game's search trees (0.12.86).</summary>
    internal sealed partial class ClearProbe5System : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            if (ClearTraceLog.Pending && GameManager.instance.gameMode == GameMode.Game)
                ClearTraceLog.Phase(EntityManager, TracePhase.Modification5End);
        }
    }

    /// <summary>The looks at the start of the next frame and 120 frames later: the first modification phase, before any of the game's lane systems has run in the frame.</summary>
    internal sealed partial class ClearTraceStartSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
        {
            if (!ClearTraceLog.Pending || GameManager.instance.gameMode != GameMode.Game)
                return;
            ClearTraceLog.Look(EntityManager, TraceStage.NextFrame);
            ClearTraceLog.Look(EntityManager, TraceStage.Later);
        }
    }
}
