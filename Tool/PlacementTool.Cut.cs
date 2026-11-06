using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Game.Rendering;
using Game.Tools;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CDLC.TS
{
    /// <summary>
    /// Deleting a stretch of a line (0.12.73; the owner: "升级删除工具，支持只删除一段"). In delete mode, Shift held over an item of a line drawn along a road (a barrier, a wall, a marking: its settings
    /// have a path) shows the whole line it is a part of, and a click sets where the stretch starts, a second click where it ends: what is between is deleted, the ends stay (LineCut: the items are
    /// shortened, split and moved up so that the parts keep their pattern). A click without Shift deletes the whole item as before. Everything of one cut is one step of the history (Ctrl+Z).
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>How far from the line (m, the ray against the line lifted by kCutLift) the cursor may be to point at it.</summary>
        private const float kCutReach = 6f;

        /// <summary>The height of the middle of a wall above its line (m), where the ray is taken against it.</summary>
        private const float kCutLift = 0.6f;

        /// <summary>No stretch is shorter than this (m): two clicks in one place are no stretch.</summary>
        private const float kCutLeast = 0.5f;

        /// <summary>The longest line looked for (items): the chain of a line is followed no further.</summary>
        private const int kCutMaxItems = 64;

        private readonly List<(Entity Entity, CutPiece Piece)> m_CutItems = new List<(Entity, CutPiece)>();
        private readonly List<KeyValuePair<Entity, ItemRecord>> m_CutNear = new List<KeyValuePair<Entity, ItemRecord>>();
        private List<Vec3> m_CutLine = new List<Vec3>();
        private List<float> m_CutAt = new List<float>();
        private Entity m_CutSeed;
        private int m_CutChanges = -1;
        private bool m_CutStarted;
        private float m_CutFrom;

        private void ResetCut()
        {
            m_CutStarted = false;
            m_CutSeed = Entity.Null;
            m_CutItems.Clear();
        }

        /// <summary>Esc or a right click: a stretch that was started is given up (true), else the tool is left as always.</summary>
        private bool CancelCut()
        {
            if (!m_CutStarted)
                return false;
            Mod.log.Info("Tool: the stretch that was started is given up.");
            ResetCut();
            m_Outcome = "the stretch is given up";
            return true;
        }

        /// <summary>The item as a piece of a line, or false when it has no path (a sign, an area, a marking that is put by one click).</summary>
        private bool TryCutPiece(Entity entity, out CutPiece piece)
        {
            piece = null;
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                return false;
            TsItem item = EntityManager.GetComponentData<TsItem>(entity);
            SignDef def = SignCatalog.ByHash(item.TypeHash);
            GuideTemplate template = def?.Template;
            if (def == null || !def.IsMarking || !MarkingTemplates.IsRun(template))
                return false;
            GuideSpec spec = template.Parse(item.SpecText);
            string path = spec.Get(RunPath.Key);
            if (path.Length == 0)
                return false;
            // What the items of one line share: the sign and the settings but the path, the phase and the whole length (which each item has its own of).
            string family = item.TypeHash + "|" + spec.With(RunPath.Key, "").With(MarkingTemplates.PhaseKey, "").With(MarkingTemplates.TotalKey, "");
            piece = new CutPiece(family, new Vec3(item.Position.x, item.Position.y, item.Position.z), item.Yaw, path, Cm(spec, MarkingTemplates.PhaseKey), Cm(spec, MarkingTemplates.TotalKey));
            return piece.Valid;
        }

        private static int Cm(GuideSpec spec, string key) => int.TryParse(spec.Get(key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int cm) ? cm : 0;

        /// <summary>The line the item is a part of: the items that go on from it each way (LineCut.Follows), in order; false when it is none.</summary>
        private bool BuildCutChain(Entity seed)
        {
            m_CutItems.Clear();
            if (!TryCutPiece(seed, out CutPiece first))
                return false;
            uint hash = EntityManager.GetComponentData<TsItem>(seed).TypeHash;
            m_CutItems.Add((seed, first));
            for (int guard = 0; guard < kCutMaxItems; guard++)
            {
                CutPiece last = m_CutItems[m_CutItems.Count - 1].Piece;
                List<Vec3> points = last.Points();
                if (!NextOf(hash, points[points.Count - 1], MarkingRuns.MaxItemLength * 0.05f, c => LineCut.Follows(last, c), out Entity next, out CutPiece nextPiece))
                    break;
                m_CutItems.Add((next, nextPiece));
            }

            for (int guard = 0; guard < kCutMaxItems; guard++)
            {
                CutPiece head = m_CutItems[0].Piece;
                // The item before ends where this starts, and starts up to a whole item's length before: it is looked for round there.
                if (!NextOf(hash, head.Start, MarkingRuns.MaxItemLength + 10f, c => LineCut.Follows(c, head), out Entity before, out CutPiece beforePiece))
                    break;
                m_CutItems.Insert(0, (before, beforePiece));
            }

            var pieces = new List<CutPiece>(m_CutItems.Count);
            foreach ((Entity Entity, CutPiece Piece) each in m_CutItems)
                pieces.Add(each.Piece);
            m_CutLine = LineCut.Line(pieces, out m_CutAt);
            return true;
        }

        /// <summary>An item of the sign near a place (not one of the line already) that the test takes for the next or the one before.</summary>
        private bool NextOf(uint hash, Vec3 near, float radius, System.Func<CutPiece, bool> goes, out Entity found, out CutPiece piece)
        {
            m_CutNear.Clear();
            ChunkSystem.Instance.Store.ItemsNear(near.X, near.Z, radius, m_CutNear);
            foreach (KeyValuePair<Entity, ItemRecord> pair in m_CutNear)
            {
                if (pair.Value.TypeHash != hash || m_CutItems.Exists(each => each.Entity == pair.Key) || !TryCutPiece(pair.Key, out CutPiece candidate) || !goes(candidate))
                    continue;
                found = pair.Key;
                piece = candidate;
                return true;
            }

            found = Entity.Null;
            piece = null;
            return false;
        }

        /// <summary>
        /// The cut part of the delete mode, called with what is under the cursor: true when it took the frame (Shift over a line, or a stretch that is started). The line is drawn in orange, the
        /// stretch in red; the first click sets the start and the second the end.
        /// </summary>
        private bool UpdateCut(OverlayRenderSystem.Buffer buffer, bool click, bool found, Entity under)
        {
            if (!m_CutStarted)
            {
                if (!m_Shift || !found)
                    return false;
                int changes = ChunkSystem.Instance.Store.Changes;
                if (under != m_CutSeed || changes != m_CutChanges)
                {
                    m_CutSeed = under;
                    m_CutChanges = changes;
                    if (!BuildCutChain(under))
                    {
                        m_CutSeed = Entity.Null;
                        return false;
                    }
                }
            }
            else if (!CutChainAlive())
            {
                Mod.log.Info("Tool: the line of the stretch was changed; the stretch is given up.");
                ResetCut();
                return false;
            }

            if (m_CutLine.Count < 2)
                return false;
            Line3.Segment ray = ToolRaycastSystem.CalculateRaycastLine(m_Camera.activeCamera);
            Vec3 from = RoadReader.ToVec(ray.a), to = RoadReader.ToVec(ray.b);
            Vec3 along = to - from;
            if (along.Length > kPickRange)
                to = from + along * (kPickRange / along.Length);
            float s = LineCut.Nearest(m_CutLine, m_CutAt, from, to, kCutLift, out float gap);
            bool valid = gap <= kCutReach;
            DrawCutLine(buffer, m_CutLine, new Color(1f, 0.6f, 0.1f, 0.8f), 0.06f);
            if (!m_CutStarted)
            {
                SetHint("cut-start", m_CutItems.Count);
                if (m_WantText)
                    Debug = $"Cut: {m_CutItems.Count} item(s) in this line, {m_CutAt[m_CutAt.Count - 1] - m_CutAt[0]:0.0} m; Shift and click the start of the stretch.";
                if (valid)
                    buffer.DrawCircle(new Color(1f, 0.15f, 0.1f, 0.9f), Lifted(LineCut.PointAt(m_CutLine, m_CutAt, s)), 1.2f);
                if (click)
                {
                    if (valid)
                    {
                        m_CutStarted = true;
                        m_CutFrom = s;
                        m_Outcome = $"the stretch starts at {s:0.0} m of a line of {m_CutItems.Count} item(s)";
                    }
                    else
                    {
                        m_Outcome = "ignored: the cursor is not at the line";
                    }
                }

                return true;
            }

            float a = Mathf.Min(m_CutFrom, s), b = Mathf.Max(m_CutFrom, s);
            if (valid)
                DrawCutLine(buffer, LineCut.Stretch(m_CutLine, m_CutAt, a, b), new Color(1f, 0.15f, 0.1f, 0.95f), 0.22f);
            buffer.DrawCircle(new Color(1f, 0.15f, 0.1f, 0.9f), Lifted(LineCut.PointAt(m_CutLine, m_CutAt, m_CutFrom)), 1.2f);
            int touched = 0;
            foreach ((Entity Entity, CutPiece Piece) each in m_CutItems)
            {
                if (each.Piece.PhaseCm < b * 100f && each.Piece.EndCm > a * 100f)
                    touched++;
            }

            bool enough = valid && b - a >= kCutLeast;
            SetHint("cut-end", touched, b - a, 0f, 0f, false, false, "", !valid);
            if (m_WantText)
                Debug = $"Cut: the stretch {b - a:0.0} m over {touched} item(s); click the end.";
            if (click)
            {
                if (enough)
                    Cut(a, b);
                else
                    m_Outcome = valid ? "ignored: the stretch is too short" : "ignored: the cursor is not at the line";
            }

            return true;
        }

        /// <summary>The items of the line are all still there (one deleted while the stretch is started gives it up).</summary>
        private bool CutChainAlive()
        {
            foreach ((Entity Entity, CutPiece Piece) each in m_CutItems)
            {
                if (!EntityManager.Exists(each.Entity) || !EntityManager.HasComponent<TsItem>(each.Entity))
                    return false;
            }

            return true;
        }

        private static float3 Lifted(Vec3 p) => new float3(p.X, p.Y + 0.15f, p.Z);

        private static void DrawCutLine(OverlayRenderSystem.Buffer buffer, List<Vec3> line, Color colour, float width)
        {
            for (int i = 1; i < line.Count; i++)
                buffer.DrawLine(colour, new Line3.Segment(new float3(line[i - 1].X, line[i - 1].Y + kCutLift, line[i - 1].Z), new float3(line[i].X, line[i].Y + kCutLift, line[i].Z)), width);
        }

        /// <summary>The stretch from a to b (m along the line) is taken out: the items are written, made and removed as LineCut says, in one step of the history.</summary>
        private void Cut(float a, float b)
        {
            var pieces = new List<CutPiece>(m_CutItems.Count);
            foreach ((Entity Entity, CutPiece Piece) each in m_CutItems)
                pieces.Add(each.Piece);
            string name = SignCatalog.ByHash(EntityManager.GetComponentData<TsItem>(m_CutItems[0].Entity).TypeHash)?.Id ?? "line";
            if (!LineCut.Cut(pieces, a, b, out List<CutEdit> edits))
            {
                m_Outcome = "ignored: the stretch is outside the line";
                return;
            }

            int written = 0, made = 0, removed = 0;
            foreach (CutEdit edit in edits)
            {
                Entity entity = m_CutItems[edit.Piece].Entity;
                if (edit.Kind == CutKind.Remove)
                {
                    DestroyItem(entity);
                    removed++;
                    continue;
                }

                TsItem item = EntityManager.GetComponentData<TsItem>(entity);
                SignDef def = SignCatalog.ByHash(item.TypeHash);
                item.Position = new float3(edit.Start.X, edit.Start.Y, edit.Start.Z);
                item.Yaw = edit.Yaw;
                item.SetSpec(MarkingTemplates.WithRun(def.Template, item.SpecText, edit.Path, edit.PhaseCm / 100f, edit.TotalCm / 100f));
                if (edit.Kind == CutKind.Write)
                {
                    WriteItem(entity, item);
                    written++;
                }
                else
                {
                    NewItem(item);
                    made++;
                }
            }

            m_Outcome = $"deleted a stretch of {b - a:0.0} m of {name}: {removed} item(s) removed, {written} shortened or moved, {made} made";
            Mod.log.Info("Tool: " + m_Outcome + ".");
            ResetCut();
        }
    }
}
