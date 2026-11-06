using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using Colossal.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>
    /// The warm-up under the loading screen (0.12.78): the first-use costs the logs show, moved here by WarmUp. Each step is for one line of the owner's logs, named in its Evidence; a cost with no line
    /// is not warmed (a guess would only lengthen the loading).
    /// </summary>
    internal sealed partial class LoadWarmSystem
    {
        private const string kWarm = "warm-up";

        /// <summary>The main steps' share of an update at the steady scale (LoadBudget makes it 6 ms under the loading screen).</summary>
        private const double kWarmMs = 1.0;

        /// <summary>Most compositions the kerb step looks through (a city has some dozens; the cap is for a modded one).</summary>
        private const int kMaxCompositions = 800;

        private WarmUp m_WarmUp;
        private EntityQuery m_RoadEdges;
        private Entity[] m_Compositions;
        private int m_CompositionAt;

        private static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

        private void CreateWarmUp()
        {
            m_RoadEdges = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Composition>(), ComponentType.ReadOnly<Road>(), ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Game.Tools.Temp>());
        }

        private void BeginWarmUp(LoadGate gate)
        {
            gate.Add(kWarm, 1f);
            m_Compositions = null;
            m_CompositionAt = 0;
            m_WarmUp = new WarmUp(NowMs, StartWorker);
            if (TextBoxes.Enabled)
                m_WarmUp.Add(new WarmStep("text-plan", WarmThread.Worker, "Text boxes: the first plan of a text took 4.7 ms on a worker and a text drawn before it shows lit bars", TextPlan));
            if (FollowGate.Enabled(PreferencesStore.Current.RoadFollow))
                m_WarmUp.Add(new WarmStep("follow", WarmThread.Worker, "Follow: the first frame of the road follow in a session took 20.3 ms (3 Nov 20:35), 7.5 ms (22:21) and 20.9 ms (4 Nov 01:21) for a handful of items", FollowJit));
            m_WarmUp.Add(new WarmStep("kerb-pieces", WarmThread.Main, "SurfaceReader: a road piece's kerb mesh read (first time) took 13.1 ms, 17 pieces in a session", KerbPieces));
        }

        private void PumpWarmUp(LoadGate gate)
        {
            if (m_WarmUp == null)
                return;
            double ms = m_WarmUp.Pump(LoadBudget.Scaled(kWarmMs));
            gate.AddMs(kWarm, ms);
            gate.Report(kWarm, m_WarmUp.Waiting);
        }

        /// <summary>Drops what is not done (the hold ended) and says what each step did, for the end line; empty when there was no warm-up.</summary>
        private string CloseWarmUp()
        {
            WarmUp warm = m_WarmUp;
            if (warm == null)
                return "";
            m_WarmUp = null;
            m_Compositions = null;
            warm.Cancel();
            return warm.Describe() + "; " + SurfaceReader.KerbSummary();
        }

        /// <summary>One thread for all the worker steps, below the normal priority: the loading screen's own work and the tile workers come first.</summary>
        private static void StartWorker(Action work)
        {
            var thread = new System.Threading.Thread(() => work()) { IsBackground = true, Name = "CDLC.TS warm-up", Priority = System.Threading.ThreadPriority.BelowNormal };
            thread.Start();
        }

        /// <summary>A plan of two letters in the default font: the font is read, the planner and the glyph cache have run once (TextPlans makes it on the calling thread, which is not the main one).</summary>
        /// <summary>The road follow's code run once over made-up roads and items (FollowWarm), with the same generic arguments ItemRoadSystem has: compiled under the loading screen on a worker.</summary>
        private static bool FollowJit() => FollowWarm.Run(new Entity { Index = 2, Version = 1 }, new Entity { Index = 3, Version = 1 }, new Entity { Index = 1, Version = 1 }) > 0;

        private static bool TextPlan()
        {
            var job = new TextPlanJob("路A", TextFonts.DefaultId, 0.5f, 0f, 1.15f, TextAlign.Centre, TextPlans.RelativeFlatness(0.5f));
            TextPlans.Get(job);
            return true;
        }

        /// <summary>
        /// The kerb meshes of the road pieces the city's roads are made of, read here instead of at the first line, post or marking laid over such a road (SurfaceReader.Pieces keeps them by piece). One
        /// slice a call: as many compositions as fit in a millisecond, so that the budget decides how many calls a frame gets. Under the loading screen the game is still loading the geometry of many
        /// pieces, and reading one of those breaks its loading for good (GeometryGate, 0.12.78): such a piece is skipped here, and SurfaceReader.Poll, which AreaFitSystem and KerbSystem call each frame,
        /// reads it when the game is done. So the step ends with one pass over the compositions and never waits for the game's own loading.
        /// </summary>
        private bool KerbPieces()
        {
            EntityManager em = EntityManager;
            if (m_Compositions == null)
                m_Compositions = DistinctCompositions(em);
            long begin = Stopwatch.GetTimestamp();
            while (m_CompositionAt < m_Compositions.Length)
            {
                Entity composition = m_Compositions[m_CompositionAt++];
                if (em.TryGetBuffer(composition, true, out DynamicBuffer<NetCompositionPiece> pieces))
                    SurfaceReader.Pieces(em, pieces, null);
                if ((Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency >= 1.0)
                    break;
            }

            return m_CompositionAt >= m_Compositions.Length;
        }

        private Entity[] DistinctCompositions(EntityManager em)
        {
            var seen = new HashSet<Entity>();
            var found = new List<Entity>();
            using (NativeArray<Entity> edges = m_RoadEdges.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity edge in edges)
                {
                    if (found.Count >= kMaxCompositions)
                        break;
                    if (em.TryGetComponent(edge, out Composition composition) && composition.m_Edge != Entity.Null && seen.Add(composition.m_Edge))
                        found.Add(composition.m_Edge);
                }
            }

            return found.ToArray();
        }
    }
}
