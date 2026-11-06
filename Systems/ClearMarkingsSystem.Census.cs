using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// The census of the game's markings near the areas (0.12.83, ClearCensus): one line in the log at the end of a load's hold, 10 s after, on request and after road pieces were given Updated, which
    /// says how many markings an area takes and are hidden, how many an area takes and are NOT (the census hides them: the same rule as every pass), and why the others are left visible. The owner
    /// reported the game's markings showing together with the mod's after loading a save: this tells a miss of this mod from the rules.
    /// </summary>
    internal sealed partial class ClearMarkingsSystem
    {
        private const string kCensusFile = "clear-census.txt";

        private readonly CensusSchedule m_Census = new CensusSchedule();
        private bool m_CensusHinted;

        /// <summary>The census is wanted when the update can take it: the load's hold ended (LoadWarmSystem), or road pieces were given Updated (NextFrameUpdateSystem).</summary>
        internal void AskCensus(CensusMoment moment)
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (moment == CensusMoment.Load)
                m_Census.AskLoad(now);
            else if (moment == CensusMoment.AfterUpdated)
                m_Census.AskUpdated(now);
            else
                m_Census.AskNow(now);
        }

        /// <summary>Looks for the file that asks for a census (and takes it away), and takes the census that is due while the areas are settled and no save is being written.</summary>
        private string StepCensus(ChunkSystem chunks)
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (m_Census.FileDue(now))
            {
                string path = Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", Mod.kModName, kCensusFile);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        m_Census.AskNow(now);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    m_Census.AskNow(now);
                }
            }

            if (m_SavePaused || m_Loaded || chunks.ClearChanges != m_SeenChanges || !m_Census.Take(now, out CensusMoment moment))
                return null;
            return TakeCensus(moment, chunks);
        }

        private string TakeCensus(CensusMoment moment, ChunkSystem chunks)
        {
            var clock = Stopwatch.StartNew();
            var tally = new ClearCensus.Tally();
            int regions = m_RegionList.Count, looked = Math.Min(regions, ClearCensus.MaxRegions), pictures = 0;
            if (looked > 0)
            {
                var boxes = new List<ClearBox>(looked);
                for (int i = 0; i < looked; i++)
                    boxes.Add(m_RegionList[i].Box);
                m_Candidates.Clear();
                m_CandidateSet.Clear();
                CollectInBoxes(boxes);
                var samples = new List<Vec3>(4);
                foreach (Entity lane in m_Candidates)
                    CountLane(lane, samples, tally);
                Apply("the census");
                m_ToHide.Clear();
                m_ToShow.Clear();
                List<Entity> objects = FoundIn(boxes);
                pictures = RemoveCovered(objects);
                m_Candidates.Clear();
                m_CandidateSet.Clear();
            }

            string line = ClearCensus.Format(moment, chunks.ClearItems.Count, regions, looked, tally, pictures, true) + $"; {m_Hidden.CalculateEntityCount()} lane(s) carry the tag in the world";
            if (!m_CensusHinted)
            {
                m_CensusHinted = true;
                line += $". (Another census: make a file {kCensusFile} in ModsData/{Mod.kModName}; it is taken away.)";
            }

            Mod.log.Info(line + $". {clock.Elapsed.TotalMilliseconds:0.0} ms.");
            return $"census ({ClearCensus.Name(moment)})";
        }

        /// <summary>One lane of the census: classified by what the areas say of it; one that an area takes and that is visible goes into m_ToHide (as Decide does).</summary>
        private void CountLane(Entity lane, List<Vec3> samples, ClearCensus.Tally tally)
        {
            sbyte kind = Kind(lane, out MarkingOwner owner, out float3 middle);
            if (kind == kNotMarking)
                return;
            var marking = new VanillaMarking { X = middle.x, Y = middle.y, Z = middle.z, Owner = owner, Crossing = kind == kAcross };
            bool hidden = EntityManager.HasComponent<Overridden>(lane);
            samples.Clear();
            if (EntityManager.TryGetComponent(lane, out Curve curve))
            {
                foreach (float t in new[] { 0f, 0.25f, 0.75f, 1f })
                {
                    float3 at = MathUtils.Position(curve.m_Bezier, t);
                    samples.Add(new Vec3(at.x, at.y, at.z));
                }
            }

            ClearWhy why = ClearCensus.Classify(m_RegionList, marking, samples, hidden, hidden && TsExtension.HidesMarking(lane));
            tally.Add(why, () => Describe(lane, owner, middle) + (why == ClearWhy.Missed ? " [" + ClearTraceLog.Explain(lane) + "]" : ""));
            if (why == ClearWhy.Missed)
                m_ToHide.Add(lane);
        }

        private string Describe(Entity lane, MarkingOwner owner, float3 middle)
        {
            string name = EntityManager.TryGetComponent(lane, out PrefabRef prefabRef) ? NameOf(prefabRef.m_Prefab) : "";
            return $"\"{name}\" of a {(owner == MarkingOwner.Edge ? "segment" : owner == MarkingOwner.Node ? "junction" : "lot")} at ({middle.x:0}, {middle.y:0}, {middle.z:0})";
        }
    }
}
