using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Mathematics;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// Barriers along a stretch by its kind of road (0.12.15, bar.auto, BarrierAuto): the stretch under the cursor is read, planned and shown as overlay lines; a click takes away the barriers an earlier click
    /// placed on it (TsItem.FlagAuto, of the barriers' group; never the player's own) and places the plan's as ordinary items marked FlagAuto.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private string m_BarrierKey = "";

        /// <summary>
        /// Names the chain m_BarrierRead (its first segment and their number), and whether another segment of it brings the same chain when read from: the cursor moving on along the road keeps the plan and
        /// its surface heights (RoadChain's reader reaches the same ends from any of its segments unless it stopped at its limits, ChainRead.Capped).
        /// </summary>
        private string m_BarrierChain = "";
        private bool m_BarrierWhole;
        private ChainRead m_BarrierRead;
        private List<BarrierRun> m_BarrierRuns = new List<BarrierRun>();
        private RoadClass m_BarrierClass;
        private readonly List<List<Vec3>> m_BarrierLines = new List<List<Vec3>>();
        private int m_BarrierItems;
        private float m_BarrierSurface;

        private void UpdateBarrierAuto(GuideTemplate template, bool click, RaycastHit hit, Entity edge, bool onRoad)
        {
            HidePreview();
            if (!onRoad)
            {
                SetHint("bar-auto", 0, 0f, 0f, 0f, false, false, "road", true);
                Debug = "Move the cursor over a road.";
                if (click)
                    m_Outcome = "refused: no road under the cursor";
                return;
            }

            GuideSpec draft = template.Parse(Draft);
            bool single = draft.Is(BarrierAuto.RangeKey, "e");
            float surface = hit.m_HitPosition.y - hit.m_Position.y;
            bool inChain = !single && m_BarrierWhole && m_BarrierRead.Edges.Contains(edge);
            string key = $"{(inChain ? m_BarrierChain : $"{edge.Index}:{edge.Version}")}|{Draft}|{m_RunNudge:0.00}";
            if (key != m_BarrierKey)
            {
                long made = System.Diagnostics.Stopwatch.GetTimestamp();
                m_BarrierSurface = surface;
                m_BarrierRead = ChainReader.Read(EntityManager, edge, false, false, single);
                m_BarrierWhole = !single && !m_BarrierRead.Capped;
                m_BarrierChain = m_BarrierRead.Chain != null && m_BarrierRead.Edges.Count > 0 ? $"{m_BarrierRead.Edges[0].Index}:{m_BarrierRead.Edges[0].Version}|{m_BarrierRead.Edges.Count}" : "";
                m_BarrierKey = $"{(m_BarrierWhole ? m_BarrierChain : $"{edge.Index}:{edge.Version}")}|{Draft}|{m_RunNudge:0.00}";
                m_BarrierRuns = new List<BarrierRun>();
                m_BarrierLines.Clear();
                m_BarrierItems = 0;
                if (m_BarrierRead.Chain != null && m_BarrierRead.Chain.Pieces.Count > 0)
                {
                    m_BarrierClass = BarrierAuto.ClassOf(draft, RoadReader.IsHighwayRules(EntityManager, edge), m_BarrierRead.Chain);
                    RoadChain read = m_BarrierRead.Chain;
                    m_BarrierRuns = BarrierAuto.Plan(read, m_BarrierClass, draft.Flag(BarrierAuto.SidesKey), draft.Flag(BarrierAuto.MedianKey), BarrierAuto.JunctionOf(draft));
                    // Anti-glare panels on the medians of W-beam and concrete (0.12.16).
                    if (draft.Flag(BarrierAuto.GlareKey))
                    {
                        foreach (BarrierRun run in m_BarrierRuns)
                        {
                            if (run.Setting("where") == "m" && (run.Id == MarkingTemplates.WBeamId || run.Id == MarkingTemplates.ConcreteBarrierId))
                                run.Settings.Add(new KeyValuePair<string, string>("glare", "1"));
                        }
                    }
                    // The arrow keys move the roadside lines nearer the road or away from it (0.12.22); the medians stay in the middle.
                    foreach (BarrierRun run in m_BarrierRuns)
                    {
                        if (run.EdgeSide != 0 && m_RunNudge != 0f)
                        {
                            run.EdgeOffset += m_RunNudge;
                            run.Lateral = run.LateralAt(read, (run.S0 + run.S1) * 0.5f);
                        }
                    }

                    foreach (BarrierRun run in m_BarrierRuns)
                    {
                        List<Vec3> line = AutoLine(read, run);
                        if (line.Count < 2)
                            continue;
                        m_BarrierLines.Add(OverlayLine.Simplify(line));
                        m_BarrierItems += MarkingRuns.Items(line, PathBudgetOf(run.Id, run.Settings)).Count;
                    }
                }

                SlowLog.Since("Tool: the automatic barriers' stretch read and planned", made);
            }

            RoadChain chain = m_BarrierRead?.Chain;
            int count = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : 0;
            bool valid = chain != null && chain.Pieces.Count > 0 && m_BarrierItems > 0 && count + m_BarrierItems <= kMaxItems;
            if (m_WantText)
                Debug = $"auto barriers ({(single ? "segment" : "stretch")}, {m_BarrierClass}): {m_BarrierRead?.Log}; {m_BarrierRuns.Count} line(s), {m_BarrierItems} item(s)" + (valid ? "" : " [cannot place]");
            SetHint("bar-auto", m_BarrierItems, chain != null ? chain.Length : 0f, m_BarrierRuns.Count, m_RunNudge, false, false, m_BarrierClass.ToString(), !valid);
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            UnityEngine.Color colour = valid ? kAutoLine : kClearBad;
            foreach (List<Vec3> line in m_BarrierLines)
            {
                for (int i = 1; i < line.Count; i++)
                    buffer.DrawLine(colour, new Line3.Segment(new float3(line[i - 1].X, line[i - 1].Y + 0.5f, line[i - 1].Z), new float3(line[i].X, line[i].Y + 0.5f, line[i].Z)), 0.15f);
            }

            if (!click)
                return;
            if (!valid)
            {
                m_Outcome = "refused: " + (chain == null || chain.Pieces.Count == 0 ? "no road could be read" : m_BarrierItems == 0 ? $"no barrier for a {m_BarrierClass} road with these settings" : "too many signs");
                return;
            }

            PlaceBarrierAuto();
        }

        /// <summary>
        /// A planned line of the stretch on the surface under it. The key names the chain and the line, so that the preview and the click, and the cursor on another segment of the chain, have one read
        /// between them; the roads do not change under the tool (the tool is left, and the heights forgotten, for anything that builds).
        /// </summary>
        private List<Vec3> AutoLine(RoadChain chain, BarrierRun run) =>
            OnSurface(chain.LineAdaptive(run.S0, run.S1, s => run.LateralAt(chain, s), m_BarrierSurface), $"auto|{m_BarrierChain}|{run.Id}|{run.S0:0.00}|{run.S1:0.00}|{run.EdgeSide}|{run.EdgeOffset:0.00}|{run.Lateral:0.00}|{m_BarrierSurface:0.000}", false);

        /// <summary>The click: the automatic barriers an earlier click put on the stretch go, and the plan's are placed.</summary>
        private void PlaceBarrierAuto()
        {
            RoadChain chain = m_BarrierRead.Chain;
            ItemStore<Entity> store = ChunkSystem.Instance.Store;
            var near = new List<KeyValuePair<Entity, ItemRecord>>();
            var gone = new HashSet<Entity>();
            foreach (ChainPiece piece in chain.Pieces)
            {
                Vec3 middle = chain.PointAt((piece.S0 + piece.S1) * 0.5f, 0f);
                near.Clear();
                store.ItemsNear(middle.X, middle.Z, (piece.S1 - piece.S0) * 0.5f + piece.HalfWidth + 4f, near);
                foreach (KeyValuePair<Entity, ItemRecord> pair in near)
                {
                    if (!pair.Value.Auto || gone.Contains(pair.Key) || SignCatalog.ByHash(pair.Value.TypeHash)?.Group != MarkingTemplates.BarrierGroup)
                        continue;
                    float s = chain.Project(pair.Value.Position, out float lateral);
                    if (s >= piece.S0 - 0.5f && s <= piece.S1 + 0.5f && Math.Abs(lateral) <= piece.HalfWidth + 3f && Math.Abs(pair.Value.Position.Y - middle.Y) < 6f)
                        gone.Add(pair.Key);
                }
            }

            foreach (Entity e in gone)
            {
                if (EntityManager.Exists(e))
                    DestroyItem(e);
            }

            int placed = 0, budget = kMaxItems - ChunkSystem.Instance.Records.Count;
            foreach (BarrierRun run in m_BarrierRuns)
            {
                SignDef def = SignCatalog.ById(run.Id);
                GuideTemplate t = def?.Template;
                if (t == null)
                    continue;
                GuideSpec spec = t.Default();
                foreach (KeyValuePair<string, string> kv in run.Settings)
                    spec = spec.With(kv.Key, kv.Value);
                List<Vec3> path = AutoLine(chain, run);
                float total = 0f;
                for (int i = 1; i < path.Count; i++)
                    total += Vec3.DistanceXZ(path[i - 1], path[i]);
                foreach (RunItem item in MarkingRuns.Items(path, MarkingTemplates.PathBudget(t, spec.ToString())))
                {
                    if (placed >= budget)
                        break;
                    Entity at = m_BarrierRead.EdgeAt(run.S0 + item.Start);
                    EdgeInfo atInfo = EdgeInfo.Of(EntityManager, at);
                    Place(def, MarkingTemplates.WithRun(t, spec.ToString(), item.Path, item.Start, total), 0u, 0f, item.Position, item.Yaw, at, atInfo.CurveOk ? atInfo.Curve.ClosestT(item.Position) : 0f, "auto barrier", TsItem.FlagAuto);
                    placed++;
                }
            }

            m_Outcome = $"placed the barriers of a {m_BarrierClass} road along {chain.Length:0} m: {m_BarrierRuns.Count} line(s) as {placed} item(s), {gone.Count} earlier automatic barrier item(s) replaced";
            Mod.log.Info("Tool: " + m_Outcome + "; " + m_BarrierRead.Log + ".");
            EditorRevision++;
        }
    }
}
