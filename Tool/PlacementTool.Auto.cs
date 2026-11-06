using System;
using System.Collections.Generic;
using System.Globalization;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Net;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// Marking a stretch of road by the standard (0.12.2, mk.auto, AutoMarking): the stretch under the cursor (the segments between the junctions at its ends, through the nodes where the road only goes
    /// on; or the segment alone) is read (ChainReader), planned (AutoMarking) and shown as overlay lines with its surface outlined; a click clears the game's markings there (mk.clear items, as the clearing
    /// tool places them), takes away what an earlier click on the stretch placed (the items with TsItem.FlagAuto inside it: never the player's own), and places the plan's markings as ordinary items, each
    /// marked FlagAuto. Since 0.12.44 the stretch marked is chosen, with a record that keeps its settings: a click on it later chooses it again, the panel's settings draw it again, and it can be undone.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private string m_AutoKey = "";
        private ChainRead m_AutoRead;
        private AutoPlan m_AutoPlan;
        /// <summary>The plan's lines as the overlay draws them (OverlayLine: without the points they do not need).</summary>
        private readonly List<List<Vec3>> m_AutoLines = new List<List<Vec3>>();
        private readonly List<KeyValuePair<Vec3, Vec3>> m_AutoMarks = new List<KeyValuePair<Vec3, Vec3>>();
        private int m_AutoItems;
        private float m_AutoSurface;
        private int m_AutoLogs;

        /// <summary>
        /// The stretch chosen (0.12.44; the owner: "支持选中道路后修改配置"): its record (an item of mk.auto that draws nothing, AutoMarking.RecordKey), what the overlay shows of it (its surface, and a ring at
        /// each approach in the colour of its rank as the panel names it), the settings it was last marked with, and when the panel's settings last changed: it is drawn again kAutoDelay after that.
        /// </summary>
        private Entity m_AutoSelected = Entity.Null;
        private readonly HashSet<Entity> m_AutoSelectedEdges = new HashSet<Entity>();
        private readonly List<List<Vec3>> m_AutoSelectedOutline = new List<List<Vec3>>();
        private readonly List<AutoRing> m_AutoRings = new List<AutoRing>();
        private string m_AutoMarkedDraft = "", m_AutoDraftSeen = "";
        private float m_AutoDraftAt, m_AutoSelectedLength;
        private int m_AutoSelectedApproaches;

        /// <summary>What was drawn at each approach of the chosen stretch when it was last marked (0.12.45: the guide markings by the road's state), for the panel.</summary>
        private readonly List<AutoEnd> m_AutoSelectedEnds = new List<AutoEnd>();

        /// <summary>
        /// The lines along on the chosen stretch's surface that are no automatic markings (0.12.46): of the kinds the automatic marking draws along (StandardLines.Ids), placed by hand or by the clear
        /// tool's redraw before 0.12.46 (an ordinary item, which the marking leaves: it lies under the new lines). The panel offers to delete them.
        /// </summary>
        private readonly List<Entity> m_AutoSelectedOld = new List<Entity>();

        /// <summary>The record of the stretch under the cursor when it was marked since 0.12.44: a click chooses it.</summary>
        private Entity m_AutoHovered = Entity.Null;

        private const float kAutoDelay = 0.4f;

        private struct AutoRing
        {
            public Vec3 Centre;
            public float Diameter;
            public int Rank;
        }

        /// <summary>The stretch chosen, for the panel: whether one is, its length, segments and approaches, and whether the panel's settings are still to be drawn.</summary>
        internal struct AutoChoice
        {
            public bool On, Pending;
            public float Length;
            public int Segments, Approaches, Old;
            public IReadOnlyList<AutoEnd> Ends;
        }

        internal static AutoChoice AutoChosen
        {
            get
            {
                PlacementTool tool = Instance;
                if (tool == null || tool.m_AutoSelected == Entity.Null)
                    return default;
                return new AutoChoice
                {
                    On = true, Pending = Draft != tool.m_AutoMarkedDraft, Length = tool.m_AutoSelectedLength, Segments = tool.m_AutoSelectedEdges.Count, Approaches = tool.m_AutoSelectedApproaches,
                    Ends = tool.m_AutoSelectedEnds, Old = tool.m_AutoSelectedOld.Count
                };
            }
        }

        /// <summary>
        /// What the automatic marking places inside the junctions at the ends of the stretch (0.12.2): left-turn waiting areas (mk.waiting, 5.5) at signalled approaches with a left-turn lane, and the junction
        /// guide lines (mk.line.junction, 5.6) of the left turns. Each with its settings and place; a guide line as a polyline cut into run items when it is placed.
        /// </summary>
        private sealed class JunctionExtra
        {
            public string Id, Spec;
            public Vec3 Position;
            public float Yaw;
            public List<Vec3> Line;
            public Entity Edge;
        }

        private readonly List<JunctionExtra> m_AutoExtras = new List<JunctionExtra>();

        private static readonly UnityEngine.Color kAutoLine = new UnityEngine.Color(0.35f, 0.85f, 1f, 0.9f);
        private static readonly UnityEngine.Color kAutoMark = new UnityEngine.Color(1f, 1f, 1f, 0.95f);
        private static readonly UnityEngine.Color kAutoMarked = new UnityEngine.Color(1f, 1f, 1f, 0.6f);
        private static readonly UnityEngine.Color kAutoChosen = new UnityEngine.Color(1f, 1f, 1f, 0.9f);

        /// <summary>The rings of the chosen stretch's approaches by rank, as MarkingTemplates.ApproachColoursZh names them: green, pink, yellow, purple.</summary>
        private static readonly UnityEngine.Color[] kApproachColours =
        {
            new UnityEngine.Color(0.2f, 0.9f, 0.3f, 0.95f), new UnityEngine.Color(1f, 0.3f, 0.8f, 0.95f), new UnityEngine.Color(1f, 0.9f, 0.1f, 0.95f), new UnityEngine.Color(0.6f, 0.4f, 1f, 0.95f)
        };

        /// <summary>The tool over the roads; settings: the automatic marking's (the panel's, or the clear tool's ClearByAuto).</summary>
        private void UpdateAuto(SignDef def, GuideTemplate template, bool click, Vec3 cursorV, RaycastHit hit, Entity edge, EdgeInfo info, bool onRoad, string settings)
        {
            HidePreview();
            if (m_PolyPoints.Count > 0)
                ResetPolygon();
            // Over the chosen stretch, or off the roads, the tool shows the chosen stretch; over another road what a click does there.
            if (TickAutoChoice(def, template) && (!onRoad || m_AutoSelectedEdges.Contains(edge)))
            {
                ShowAutoChoice(def, template, click);
                return;
            }

            if (!onRoad)
            {
                SetHint("mark-auto", 0, 0f, 0f, 0f, false, false, "road", true);
                Debug = "Move the cursor over a road.";
                if (click)
                    m_Outcome = "refused: no road under the cursor";
                return;
            }

            // A road of CDL : Racing's circuit is marked by Racing (its own lines): the automatic marking leaves it (0.12.6, the markings protocol).
            if (TsExtension.IsReserved(edge))
            {
                SetHint("mark-auto-circuit", 0, 0f, 0f, 0f, false, false, "", true);
                Debug = $"road {edge.Index}:{edge.Version} is a road of CDL : Racing's circuit: Racing draws its lines.";
                if (click)
                    m_Outcome = "refused: a road of CDL : Racing's circuit";
                DrawAuto(false, true, true);
                return;
            }

            // Another road is marked with the panel's settings without the chosen stretch's own (its approaches').
            GuideSpec draft = AutoMarking.WithoutStretch(template.Parse(settings));
            bool single = draft.Is(AutoMarking.RangeKey, "e");
            bool keepCrossings = !draft.Is(AutoMarking.KeepKey, "all");
            float surface = hit.m_HitPosition.y - hit.m_Position.y;
            // Read again for another road, other settings, or when the areas that clear the game's markings changed (the store changes too often: an automatic screen writes its items).
            int areas = ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.Revision : 0;
            string key = $"{edge.Index}:{edge.Version}|{draft}|{areas}|{TsExtension.ReservedKey()}";
            if (key != m_AutoKey)
            {
                m_AutoKey = key;
                m_AutoSurface = surface;
                ReadAuto(edge, draft, single, keepCrossings, surface);
                m_AutoHovered = FindAutoRecord(def, cursorV);
            }

            RoadChain chain = m_AutoRead.Chain;
            int approaches = m_AutoRead.Approaches.Count;
            if (m_AutoHovered != Entity.Null && EntityManager.Exists(m_AutoHovered))
            {
                // A stretch marked since 0.12.44: a click chooses it with the settings it was marked with, so what the panel's would draw is not shown.
                bool old = EntityManager.TryGetComponent(m_AutoHovered, out TsItem hovered) && AutoMarking.IsOld(template.Parse(hovered.SpecText));
                SetHint("mark-auto-marked", 0, chain != null ? chain.Length : 0f, m_AutoRead.Segments, approaches, old, false, single ? "e" : "s");
                DrawAuto(true, true, false);
                if (click)
                    ChooseAuto(def, template, m_AutoHovered);
                return;
            }

            bool valid = AutoValid();
            if (m_WantText)
                Debug = $"auto marking ({(single ? "segment" : "stretch")}): {m_AutoRead?.Log}; {m_AutoPlan?.Runs.Count ?? 0} line(s), {m_AutoPlan?.Stamps.Count ?? 0} mark(s), {m_AutoItems} item(s)" +
                    (m_AutoPlan != null && m_AutoPlan.Notes.Count > 0 ? "; " + string.Join("; ", m_AutoPlan.Notes) : "") + (valid ? "" : " [cannot place]");
            SetHint("mark-auto", m_AutoItems, chain != null ? chain.Length : 0f, m_AutoRead.Segments, approaches, !keepCrossings, draft.Flag(AutoMarking.EndsKey), single ? "e" : "s", !valid);
            DrawAuto(valid, true, true);
            if (!click)
                return;
            if (!valid)
            {
                m_Outcome = "refused: " + AutoRefusal();
                return;
            }

            if (PlaceAuto(draft, keepCrossings))
                ChooseMarked(def, template, draft, edge);
            m_AutoKey = "";
        }

        /// <summary>Whether the stretch just read can be marked: it was read, it has something to draw, and its items with its record fit in the store.</summary>
        private bool AutoValid()
        {
            RoadChain chain = m_AutoRead?.Chain;
            int count = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : 0;
            return chain != null && chain.Pieces.Count > 0 && m_AutoItems > 0 && count + m_AutoItems + 1 <= kMaxItems;
        }

        private string AutoRefusal()
        {
            RoadChain chain = m_AutoRead?.Chain;
            return chain == null || chain.Pieces.Count == 0 ? "no road could be read" : m_AutoItems == 0 ? "nothing to draw" : "too many signs";
        }

        // ------------------------------------------------------------------ the chosen stretch (0.12.44)

        /// <summary>
        /// The chosen stretch at every frame the automatic marking is the tool's (the cursor over the panel too): let go when its record is gone (deleted, or its road rebuilt), and drawn again kAutoDelay
        /// after the panel's settings last changed. True while one is chosen.
        /// </summary>
        private bool TickAutoChoice(SignDef def, GuideTemplate template)
        {
            if (m_AutoSelected == Entity.Null)
                return false;
            if (!ChosenRecord(out TsItem record))
            {
                Mod.log.Info("Tool: the record of the chosen stretch is gone (deleted, or its road was rebuilt); nothing is chosen.");
                ForgetAutoChoice();
                return false;
            }

            float now = UnityEngine.Time.unscaledTime;
            if (Draft != m_AutoDraftSeen)
            {
                m_AutoDraftSeen = Draft;
                m_AutoDraftAt = now;
            }

            if (Draft != m_AutoMarkedDraft && now - m_AutoDraftAt >= kAutoDelay)
                RemarkAuto(def, template, record);
            return m_AutoSelected != Entity.Null;
        }

        private bool ChosenRecord(out TsItem record)
        {
            record = default;
            return m_AutoSelected != Entity.Null && EntityManager.Exists(m_AutoSelected) && EntityManager.TryGetComponent(m_AutoSelected, out record) && RoadReader.IsLiveEdge(EntityManager, record.Edge);
        }

        /// <summary>The chosen stretch as the panel's settings draw it, with a ring at each of its approaches; a click on it draws it at once.</summary>
        private void ShowAutoChoice(SignDef def, GuideTemplate template, bool click)
        {
            if (!ChosenRecord(out TsItem record))
                return;
            int areas = ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.Revision : 0;
            string key = $"chosen {m_AutoSelected.Index}:{m_AutoSelected.Version}|{Draft}|{areas}|{TsExtension.ReservedKey()}";
            if (key != m_AutoKey)
            {
                m_AutoKey = key;
                m_AutoHovered = Entity.Null;
                ReadChosen(template, record, template.Parse(Draft));
            }

            RoadChain chain = m_AutoRead.Chain;
            bool valid = AutoValid(), pending = Draft != m_AutoMarkedDraft;
            if (m_WantText)
                Debug = $"auto marking, the chosen stretch: {m_AutoRead.Log}; {m_AutoItems} item(s) with the panel's settings{(pending ? ", to be drawn" : "")}" + (valid ? "" : " [cannot place]");
            SetHint("mark-auto-chosen", m_AutoItems, chain != null ? chain.Length : 0f, m_AutoSelectedEdges.Count, m_AutoSelectedApproaches, pending, false, "", !valid);
            DrawAuto(valid, false, true);
            // A click on it draws it again (0.12.48: before, it did only when its settings had changed): the way to have the road as it is now, or a marking taken away by hand, drawn again.
            if (click)
                RemarkAuto(def, template, record);
        }

        /// <summary>Reads the chosen stretch with these settings from the segment its record is on, at the record's surface.</summary>
        private void ReadChosen(GuideTemplate template, TsItem record, GuideSpec draft)
        {
            m_AutoSurface = AutoMarking.SurfaceOf(template.Parse(record.SpecText));
            ReadAuto(record.Edge, draft, draft.Is(AutoMarking.RangeKey, "e"), !draft.Is(AutoMarking.KeepKey, "all"), m_AutoSurface);
        }

        /// <summary>The chosen stretch marked again with the panel's settings: its earlier markings and its record go, and a new record keeps the settings.</summary>
        private void RemarkAuto(SignDef def, GuideTemplate template, TsItem record)
        {
            GuideSpec draft = template.Parse(Draft);
            ReadChosen(template, record, draft);
            m_AutoKey = "";
            // Tried once for these settings: what cannot be drawn is not tried again at every frame.
            m_AutoMarkedDraft = Draft;
            if (!AutoValid())
            {
                m_Outcome = "refused: the chosen stretch with these settings: " + AutoRefusal();
                Mod.log.Info("Tool: " + m_Outcome + ".");
                EditorRevision++;
                return;
            }

            Entity before = m_AutoSelected;
            if (!PlaceAuto(draft, !draft.Is(AutoMarking.KeepKey, "all")))
                return;
            if (EntityManager.Exists(before))
                DestroyItem(before);
            m_AutoSelected = PlaceAutoRecord(def, draft, record.Edge);
            TakeAutoChoice(template);
        }

        /// <summary>A click on a stretch marked before: it is chosen, and the panel shows the settings it was marked with; one marked by older rules is drawn again with them (0.12.48).</summary>
        private void ChooseAuto(SignDef def, GuideTemplate template, Entity entity)
        {
            if (!EntityManager.TryGetComponent(entity, out TsItem record) || !RoadReader.IsLiveEdge(EntityManager, record.Edge))
            {
                m_Outcome = "refused: the stretch's record has no road";
                return;
            }

            GuideSpec draft = AutoMarking.DraftOf(template.Parse(record.SpecText));
            m_AutoSelected = entity;
            Draft = draft.ToString();
            ReadChosen(template, record, draft);
            m_AutoKey = "";
            TakeAutoChoice(template);
            EditorTarget++;
            m_Outcome = $"chose the stretch marked by the standard ({m_AutoSelectedEdges.Count} segment(s), {m_AutoSelectedLength:0} m); its settings are in the panel";
            if (!AutoMarking.IsOld(template.Parse(record.SpecText)))
                return;
            Mod.log.Info($"Tool: the chosen stretch was marked by older rules ({template.Parse(record.SpecText).Get(AutoMarking.RulesKey)}, now {AutoMarking.Rules}): it is drawn again with its settings.");
            RemarkAuto(def, template, record);
        }

        /// <summary>A stretch just marked is chosen: its record keeps the settings, and the panel shows them with its approaches.</summary>
        private void ChooseMarked(SignDef def, GuideTemplate template, GuideSpec draft, Entity edge)
        {
            m_AutoSelected = PlaceAutoRecord(def, draft, edge);
            Draft = draft.ToString();
            TakeAutoChoice(template);
            EditorTarget++;
        }

        /// <summary>
        /// The record of the stretch just marked: an item of mk.auto that draws nothing, on the middle of the segment it was read from, inside its surface, so that marking the stretch again takes it away with
        /// the rest; with the settings, the surface and the number of approaches.
        /// </summary>
        private Entity PlaceAutoRecord(SignDef def, GuideSpec draft, Entity edge)
        {
            EdgeInfo info = EdgeInfo.Of(EntityManager, edge);
            Vec3 at = info.Curve.Point(0.5f) + new Vec3(0f, m_AutoSurface, 0f);
            string spec = AutoMarking.RecordOf(draft, m_AutoSurface, m_AutoRead.Approaches.Count).ToString();
            return Place(def, spec, 0u, 0f, at, 0f, edge, 0.5f, "the record of a stretch marked by the standard", TsItem.FlagAuto);
        }

        /// <summary>The stretch just read is the chosen one (m_AutoSelected): what the overlay shows of it, and the panel's settings with the number of its approaches.</summary>
        private void TakeAutoChoice(GuideTemplate template)
        {
            m_AutoHovered = Entity.Null;
            m_AutoSelectedEdges.Clear();
            foreach (Entity e in m_AutoRead.Edges)
            {
                if (!m_AutoRead.Stubs.Contains(e))
                    m_AutoSelectedEdges.Add(e);
            }
            m_AutoSelectedOutline.Clear();
            foreach (ClearPiece piece in m_ClearPieces)
                m_AutoSelectedOutline.Add(piece.Drawn);
            RoadChain chain = m_AutoRead.Chain;
            m_AutoSelectedLength = chain != null ? chain.Length : 0f;
            m_AutoSelectedApproaches = Math.Min(m_AutoRead.Approaches.Count, AutoMarking.MaxApproaches);
            m_AutoSelectedEnds.Clear();
            if (m_AutoPlan != null)
                m_AutoSelectedEnds.AddRange(m_AutoPlan.Ends.FindAll(e => e.Rank < AutoMarking.MaxApproaches));
            FindOldLines();
            m_AutoRings.Clear();
            int[] ranks = AutoMarking.Ranks(m_AutoRead.Approaches);
            for (int i = 0; i < ranks.Length && chain != null; i++)
            {
                ChainApproach approach = m_AutoRead.Approaches[i];
                if (ranks[i] >= AutoMarking.MaxApproaches || approach.Lanes.Count == 0)
                    continue;
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (ApproachLane lane in approach.Lanes)
                {
                    lo = Math.Min(lo, lane.Lateral);
                    hi = Math.Max(hi, lane.Lateral);
                }

                // Over the approach's lanes, before its stop line.
                Vec3 centre = chain.PointAt(approach.StopS - approach.Dir * 4f, (lo + hi) * 0.5f, m_AutoSurface);
                m_AutoRings.Add(new AutoRing { Centre = centre, Diameter = hi - lo + 4f, Rank = ranks[i] });
            }

            Draft = template.Parse(Draft).With(AutoMarking.ApproachesKey, m_AutoSelectedApproaches.ToString(CultureInfo.InvariantCulture)).ToString();
            m_AutoMarkedDraft = m_AutoDraftSeen = Draft;
            EditorRevision++;
        }

        /// <summary>Nothing is chosen any more (a right click, Esc, the panel's button, another entry or mode): the panel's settings lose the chosen stretch's own.</summary>
        internal void ForgetAutoChoice()
        {
            if (m_AutoSelected == Entity.Null)
                return;
            m_AutoSelected = Entity.Null;
            m_AutoSelectedEdges.Clear();
            m_AutoSelectedOutline.Clear();
            m_AutoSelectedEnds.Clear();
            m_AutoSelectedOld.Clear();
            m_AutoRings.Clear();
            m_AutoKey = "";
            GuideTemplate template = SelectedId == AutoMarking.Id ? SignCatalog.ById(AutoMarking.Id)?.Template : null;
            if (template != null)
                Draft = AutoMarking.WithoutStretch(template.Parse(Draft)).ToString();
            EditorTarget++;
            EditorRevision++;
        }

        /// <summary>
        /// The panel's button: the chosen stretch's automatic marking is undone: what it placed on its surface and at its junctions, its record, and the areas on it that clear the game's markings (they are
        /// drawn there again).
        /// </summary>
        internal void UndoAuto()
        {
            SignDef def = SignCatalog.ById(AutoMarking.Id);
            if (def?.Template == null || ChunkSystem.Instance == null || !ChosenRecord(out TsItem record))
                return;
            // Read as it was marked (the record's settings): the waiting areas and guide lines at its junctions are found where they were placed.
            ReadChosen(def.Template, record, AutoMarking.DraftOf(def.Template.Parse(record.SpecText)));
            var gone = new HashSet<Entity> { m_AutoSelected };
            int markings = TakeStretch(gone, true, out int areas);
            foreach (Entity e in gone)
            {
                if (EntityManager.Exists(e))
                    DestroyItem(e);
            }

            ItemStore<Entity> store = ChunkSystem.Instance.Store;
            foreach (JunctionExtra extra in m_AutoExtras)
            {
                SignDef extraDef = SignCatalog.ById(extra.Id);
                if (extraDef == null)
                    continue;
                if (extra.Line == null)
                {
                    markings += RemoveAutoNear(store, extraDef.Hash, extra.Position, gone);
                    continue;
                }

                foreach (RunItem item in MarkingRuns.Items(extra.Line, ExtraBudget(extra)))
                    markings += RemoveAutoNear(store, extraDef.Hash, item.Position, gone);
            }

            m_Outcome = $"undid the automatic marking of the chosen stretch: {markings} marking(s) and {areas} area(s) taken away, the game's markings are drawn there again";
            Mod.log.Info("Tool: " + m_Outcome + ".");
            ForgetAutoChoice();
        }

        /// <summary>The lines along on the surface of the stretch just read that are no automatic markings (m_AutoSelectedOld).</summary>
        private void FindOldLines()
        {
            m_AutoSelectedOld.Clear();
            if (ChunkSystem.Instance == null)
                return;
            var near = new List<KeyValuePair<Entity, ItemRecord>>();
            var seen = new HashSet<Entity>();
            foreach (ClearPiece piece in m_ClearPieces)
            {
                if (piece.Scope != ClearScope.Edges)
                    continue;
                ClearRegion area = ItemsOnPiece(piece, near);
                foreach (KeyValuePair<Entity, ItemRecord> pair in near)
                {
                    SignDef def = pair.Value.Auto ? null : SignCatalog.ByHash(pair.Value.TypeHash);
                    if (def != null && StandardLines.Ids.Contains(def.Id) && area.Contains(pair.Value.Position) && seen.Add(pair.Key) && EntityManager.Exists(pair.Key))
                        m_AutoSelectedOld.Add(pair.Key);
                }
            }
        }

        /// <summary>The panel's button: the lines along on the chosen stretch that are no automatic markings are deleted (pressed twice in the panel).</summary>
        internal void RemoveOldLines()
        {
            if (!ChosenRecord(out _) || m_AutoSelectedOld.Count == 0)
                return;
            int removed = 0;
            foreach (Entity e in m_AutoSelectedOld)
            {
                if (!EntityManager.Exists(e))
                    continue;
                DestroyItem(e);
                removed++;
            }

            m_AutoSelectedOld.Clear();
            m_Outcome = $"deleted {removed} line(s) along the chosen stretch that were no automatic markings";
            Mod.log.Info("Tool: " + m_Outcome + ".");
            EditorRevision++;
        }

        /// <summary>The record of a stretch marked since 0.12.44 on the surface of the stretch just read, the nearest the cursor; none when there is none.</summary>
        private Entity FindAutoRecord(SignDef def, Vec3 cursor)
        {
            if (ChunkSystem.Instance == null)
                return Entity.Null;
            var near = new List<KeyValuePair<Entity, ItemRecord>>();
            Entity best = Entity.Null;
            float nearest = float.MaxValue;
            foreach (ClearPiece piece in m_ClearPieces)
            {
                ClearRegion area = ItemsOnPiece(piece, near);
                foreach (KeyValuePair<Entity, ItemRecord> pair in near)
                {
                    if (pair.Value.TypeHash != def.Hash || !pair.Value.Auto || !area.Contains(pair.Value.Position) || !def.Template.Parse(pair.Value.Spec).Flag(AutoMarking.RecordKey))
                        continue;
                    float d = Vec3.DistanceXZ(pair.Value.Position, cursor);
                    if (d < nearest)
                    {
                        nearest = d;
                        best = pair.Key;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// The items near a piece of the stretch's surface, into near; returns the piece's area (grown by half a metre), which tells those on it. reach: how much further than the piece the items are looked
        /// for (the store is searched by where an item starts, and a line belongs where its middle is: AutoOwner.Reach).
        /// </summary>
        private static ClearRegion ItemsOnPiece(ClearPiece piece, List<KeyValuePair<Entity, ItemRecord>> near, float reach = 0f)
        {
            Vec3 middle = Centroid(piece.Outline);
            float radius = 0f;
            foreach (Vec3 p in piece.Outline)
                radius = Math.Max(radius, Vec3.DistanceXZ(p, middle));
            near.Clear();
            ChunkSystem.Instance.Store.ItemsNear(middle.X, middle.Z, radius + 2f + reach, near);
            return new ClearRegion(VanillaClear.Grow(piece.Outline, 0.5f), piece.Scope, false, piece.Strip);
        }

        /// <summary>
        /// What an earlier marking of the stretch placed on its surface (the automatic items inside its areas; not the automatic barriers, BarrierAuto's), into gone. With all (the undo) the areas there
        /// that clear the game's markings go too, else only those that keep the game's crossing lines when these are to go now (ClearPiece.Upgrade). Returns how many markings; areas counts the areas.
        /// </summary>
        private int TakeStretch(HashSet<Entity> gone, bool all, out int areas)
        {
            SignDef clearDef = SignCatalog.ById(VanillaClear.Id);
            var near = new List<KeyValuePair<Entity, ItemRecord>>();
            int markings = 0;
            areas = 0;
            foreach (ClearPiece piece in m_ClearPieces)
            {
                ClearRegion area = ItemsOnPiece(piece, near, AutoOwner.Reach);
                foreach (KeyValuePair<Entity, ItemRecord> pair in near)
                {
                    if (gone.Contains(pair.Key))
                        continue;
                    // By where a line belongs (its middle), not where it starts: the node between two segments is in the areas of both, and each started its lines there (AutoOwner).
                    if (pair.Value.Auto && AutoOwner.Belongs(pair.Value, area))
                    {
                        if (SignCatalog.ByHash(pair.Value.TypeHash)?.Group == MarkingTemplates.BarrierGroup)
                            continue;
                        gone.Add(pair.Key);
                        markings++;
                    }
                    else if ((all || piece.Upgrade) && clearDef != null && pair.Value.TypeHash == clearDef.Hash)
                    {
                        ClearRegion old = ClearRegion.Of(pair.Value);
                        if (old != null && old.Scope == piece.Scope && (all || old.KeepCrossings) && old.Contains(piece.Position))
                        {
                            gone.Add(pair.Key);
                            areas++;
                        }
                    }
                }
            }

            return markings;
        }

        /// <summary>Reads the stretch, plans it, and works out the areas that clear the game's markings there and what the overlay shows.</summary>
        private void ReadAuto(Entity edge, GuideSpec draft, bool single, bool keepCrossings, float surface)
        {
            m_AutoRead = ChainReader.Read(EntityManager, edge, false, true, single, true, true);
            m_AutoLines.Clear();
            m_AutoMarks.Clear();
            m_AutoExtras.Clear();
            m_AutoItems = 0;
            m_AutoPlan = null;
            m_ClearPieces.Clear();
            m_ClearEdges.Clear();
            m_ClearPassNodes.Clear();
            m_ClearLength = 0f;
            m_ClearKey = "";
            if (m_AutoRead.Chain == null || m_AutoRead.Chain.Pieces.Count == 0)
                return;
            RoadChain chain = m_AutoRead.Chain;
            AutoOptions options = AutoMarking.OptionsOf(draft);
            m_AutoPlan = AutoMarking.Plan(chain, m_AutoRead.Approaches, options);
            foreach (AutoRun run in m_AutoPlan.Runs)
            {
                List<Vec3> line = chain.Line(run.S0, run.S1, run.LateralAt, surface);
                if (line.Count < 2)
                    continue;
                m_AutoLines.Add(OverlayLine.Simplify(line));
                m_AutoItems += MarkingRuns.Items(chain.LineParts(run.S0, run.S1, run.LateralAt, surface), out _, PathBudgetOf(run.Id, run.Settings)).Count;
            }

            ReadJunctionExtras(options);
            foreach (JunctionExtra extra in m_AutoExtras)
            {
                if (extra.Line != null)
                {
                    m_AutoLines.Add(OverlayLine.Simplify(extra.Line));
                    m_AutoItems += MarkingRuns.Items(extra.Line, ExtraBudget(extra)).Count;
                }
                else
                {
                    m_AutoMarks.Add(new KeyValuePair<Vec3, Vec3>(extra.Position, new Vec3(Mathf.Sin(extra.Yaw), 0f, Mathf.Cos(extra.Yaw))));
                    m_AutoItems++;
                }
            }

            foreach (AutoStamp stamp in m_AutoPlan.Stamps)
            {
                Vec3 at = chain.PointAt(stamp.S, stamp.Lateral, surface);
                Vec3 heading = chain.HeadingAt(stamp.S) * (stamp.WithChain ? 1f : -1f);
                m_AutoMarks.Add(new KeyValuePair<Vec3, Vec3>(at, heading));
                m_AutoItems++;
            }

            // The areas that clear the game's markings: each segment's surface, and the nodes the stretch goes on through (a segment alone: its ends where it only meets the next segment).
            foreach (Entity e in m_AutoRead.Edges)
            {
                // A stub's segment is not cleared: only its part of the node, which the node's area has.
                if (!m_AutoRead.Stubs.Contains(e))
                    AddEdgePieces(e);
            }
            var nodes = new HashSet<Entity>(m_AutoRead.PassNodes);
            if (single && EntityManager.TryGetComponent(edge, out Edge own))
            {
                foreach (Entity n in new[] { own.m_Start, own.m_End })
                {
                    if (LiveEdgesAt(n) == 2)
                        nodes.Add(n);
                }
            }

            foreach (Entity n in nodes)
            {
                m_ClearPassNodes.Add(n);
                AddNodePiece(n);
            }

            IReadOnlyList<ClearRegion> regions = ClearMarkingsSystem.Instance != null ? ClearMarkingsSystem.Instance.Regions : null;
            foreach (ClearPiece piece in m_ClearPieces)
            {
                Vec3 middle = Centroid(piece.Outline);
                if (regions != null)
                {
                    foreach (ClearRegion region in regions)
                    {
                        if ((region.Scope != piece.Scope && region.Scope != ClearScope.All) || !region.Contains(piece.Position))
                            continue;
                        // An area that keeps the game's crossing lines does not do when they are to go now: it is replaced.
                        if (!region.KeepCrossings || keepCrossings)
                            piece.Done = true;
                        else
                            piece.Upgrade = true;
                    }
                }

                if (piece.Upgrade)
                    piece.Done = false;
                if (!piece.Done)
                    m_AutoItems++;
            }
        }

        /// <summary>The waiting areas and the junction guide lines of the junctions the stretch ends at (and crosses), for each approach as the options say.</summary>
        private void ReadJunctionExtras(AutoOptions options)
        {
            m_AutoExtras.Clear();
            if (!options.Junctions || m_AutoRead?.Chain == null)
                return;
            GuideTemplate waiting = MarkingTemplates.ById(MarkingTemplates.WaitingId);
            foreach (ChainApproach approach in m_AutoRead.Approaches)
            {
                if (approach.PieceIndex < 0 || approach.PieceIndex >= m_AutoRead.Edges.Count)
                    continue;
                Entity edge = m_AutoRead.Edges[approach.PieceIndex];
                bool forward = approach.WithChain != m_AutoRead.Chain.Pieces[approach.PieceIndex].Reversed;
                float guideFrom = float.IsNaN(approach.CrosswalkFar) ? 1f : Math.Max(1f, approach.CrosswalkFar + 1f);
                // 5.5.3: a waiting area where a signal controls the approach and it has a left-turn lane.
                if (options.Waiting && waiting != null && approach.Control == JunctionControl.Signals && approach.HasLeftOnlyLane)
                {
                    if (WaitingFor(waiting, waiting.Default().ToString(), edge, forward, out WaitingRead read, out string spec, out Vec3 position, out float yaw, out float length, out string problem))
                    {
                        m_AutoExtras.Add(new JunctionExtra { Id = MarkingTemplates.WaitingId, Spec = spec, Position = position, Yaw = yaw, Edge = edge });
                        guideFrom = Math.Max(guideFrom, length + 1f);
                    }
                    else
                    {
                        m_AutoPlan.Notes.Add($"no waiting area at {approach.StopS:0} m: {problem}");
                    }
                }

                // 5.6: the guide lines of the left turns.
                if (!JunctionGuide.Wanted(options.Guides, approach.LeftTurns))
                    continue;
                // The turns are the game's lane curves, on the junction's surface (as the waiting area's axis is).
                foreach (GuideLine line in JunctionGuide.Lines(approach.LeftTurns, approach.LeftWidths, approach.LeftFromLeftmost, guideFrom))
                    m_AutoExtras.Add(new JunctionExtra { Id = "mk.line.junction", Spec = line.Yellow ? "y" : "w", Line = line.Points, Edge = edge });
            }
        }

        /// <summary>The characters a path of one item of an automatic run may have (MarkingTemplates.PathBudget): the run's settings over its template's defaults, as PlaceAuto writes them.</summary>
        private static int PathBudgetOf(string id, IEnumerable<KeyValuePair<string, string>> settings)
        {
            GuideTemplate t = SignCatalog.ById(id)?.Template;
            if (t == null)
                return MarkingRuns.MaxPathChars;
            GuideSpec spec = t.Default();
            foreach (KeyValuePair<string, string> kv in settings)
                spec = spec.With(kv.Key, kv.Value);
            return MarkingTemplates.PathBudget(t, spec.ToString());
        }

        /// <summary>The same for a junction's guide line (its colour is its only setting, as PlaceAuto writes it).</summary>
        private static int ExtraBudget(JunctionExtra extra)
        {
            GuideTemplate t = SignCatalog.ById(extra.Id)?.Template;
            return t == null ? MarkingRuns.MaxPathChars : MarkingTemplates.PathBudget(t, t.Default().With("colour", extra.Spec).ToString());
        }

        /// <summary>Removes the automatic items of a type within 1.5 m of a place (what an earlier click put there); returns how many.</summary>
        private int RemoveAutoNear(ItemStore<Entity> store, uint typeHash, Vec3 at, HashSet<Entity> gone)
        {
            var near = new List<KeyValuePair<Entity, ItemRecord>>();
            store.ItemsNear(at.X, at.Z, 1.5f, near);
            int removed = 0;
            foreach (KeyValuePair<Entity, ItemRecord> pair in near)
            {
                if (!pair.Value.Auto || pair.Value.TypeHash != typeHash || gone.Contains(pair.Key) || !VanillaClear.NearSameSurface(pair.Value.Position, at, 1.5f) || !EntityManager.Exists(pair.Key))
                    continue;
                gone.Add(pair.Key);
                DestroyItem(pair.Key);
                removed++;
            }

            return removed;
        }

        /// <summary>
        /// The overlay: the stretch just read (its surface, white when it is a stretch marked before, and unless lines is false what is to be drawn on it) and the chosen stretch with its approaches' rings.
        /// </summary>
        private void DrawAuto(bool valid, bool pieces, bool lines)
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            if (pieces)
            {
                foreach (ClearPiece piece in m_ClearPieces)
                    DrawOutline(buffer, piece.Drawn, !lines ? kAutoMarked : valid ? kClearNew : kClearBad, 0.2f);
            }

            if (lines)
            {
                UnityEngine.Color colour = valid ? kAutoLine : kClearBad;
                foreach (List<Vec3> line in m_AutoLines)
                {
                    for (int i = 1; i < line.Count; i++)
                        buffer.DrawLine(colour, new Line3.Segment(new float3(line[i - 1].X, line[i - 1].Y + 0.12f, line[i - 1].Z), new float3(line[i].X, line[i].Y + 0.12f, line[i].Z)), 0.12f);
                }

                foreach (KeyValuePair<Vec3, Vec3> mark in m_AutoMarks)
                {
                    var at = new float3(mark.Key.X, mark.Key.Y + 0.12f, mark.Key.Z);
                    buffer.DrawCircle(kAutoMark, at, 0.5f);
                    buffer.DrawLine(kAutoMark, new Line3.Segment(at, at + new float3(mark.Value.X, 0f, mark.Value.Z) * 1.5f), 0.1f);
                }
            }

            foreach (List<Vec3> outline in m_AutoSelectedOutline)
                DrawOutline(buffer, outline, kAutoChosen, 0.35f);
            foreach (AutoRing ring in m_AutoRings)
                buffer.DrawCircle(kApproachColours[ring.Rank], new UnityEngine.Color(0f, 0f, 0f, 0f), 0.4f, 0, new float2(0f, 1f), new float3(ring.Centre.X, ring.Centre.Y + 0.15f, ring.Centre.Z), ring.Diameter);
        }

        /// <summary>The click: the earlier automatic markings of the stretch go, the game's markings are cleared, and the plan is placed. False when nothing could be.</summary>
        private bool PlaceAuto(GuideSpec draft, bool keepCrossings)
        {
            RoadChain chain = m_AutoRead.Chain;
            ItemStore<Entity> store = ChunkSystem.Instance.Store;
            var gone = new HashSet<Entity>();
            SignDef clearDef = SignCatalog.ById(VanillaClear.Id);
            GuideTemplate clearTemplate = clearDef?.Template;
            var preparedAreas = new Dictionary<ClearPiece, string>();
            foreach (ClearPiece piece in m_ClearPieces)
            {
                if (piece.Done)
                    continue;
                string scope = piece.Scope == ClearScope.Nodes ? "n" : piece.Scope == ClearScope.All ? "a" : "e";
                string spec = clearTemplate == null ? null : ClearElevation.WithOutline(clearTemplate.Default().With(VanillaClear.KeepKey, keepCrossings ? "lines" : "all").With(VanillaClear.ScopeKey, scope), piece.Outline, piece.Position, piece.Yaw, piece.Strip);
                if (spec == null)
                {
                    m_Outcome = "refused: the complete outline and surface heights cannot be saved; existing markings were kept";
                    return false;
                }
                preparedAreas.Add(piece, spec);
            }

            // What an earlier click placed here, and (when the area is replaced) the area that kept the game's crossing lines.
            int replaced = TakeStretch(gone, false, out int upgraded);
            foreach (Entity e in gone)
            {
                if (EntityManager.Exists(e))
                    DestroyItem(e);
            }

            // The areas that clear the game's markings.
            int cleared = 0;
            foreach (ClearPiece piece in m_ClearPieces)
            {
                if (piece.Done || clearTemplate == null)
                    continue;
                string spec = preparedAreas[piece];
                float edgeT = 0f;
                if (piece.Edge != Entity.Null)
                {
                    EdgeInfo pieceInfo = EdgeInfo.Of(EntityManager, piece.Edge);
                    edgeT = pieceInfo.CurveOk ? pieceInfo.Curve.ClosestT(piece.Position) : 0f;
                }

                Place(clearDef, spec, 0u, 0f, piece.Position, piece.Yaw, piece.Edge, edgeT, "an area that clears the game's markings (auto)");
                piece.Done = true;
                cleared++;
            }

            // The lines along.
            int placed = 0, budget = kMaxItems - ChunkSystem.Instance.Records.Count;
            foreach (AutoRun run in m_AutoPlan.Runs)
            {
                SignDef lineDef = SignCatalog.ById(run.Id);
                GuideTemplate t = lineDef?.Template;
                if (t == null)
                    continue;
                GuideSpec spec = t.Default();
                foreach (KeyValuePair<string, string> kv in run.Settings)
                    spec = spec.With(kv.Key, kv.Value);
                // In parts where a segment's area meets a node's: marking either again takes away whole items.
                List<RunItem> items = MarkingRuns.Items(chain.LineParts(run.S0, run.S1, run.LateralAt, m_AutoSurface), out float total, MarkingTemplates.PathBudget(t, spec.ToString()));
                foreach (RunItem item in items)
                {
                    if (placed >= budget)
                        break;
                    Entity at = m_AutoRead.EdgeAt(run.S0 + item.Start);
                    EdgeInfo atInfo = EdgeInfo.Of(EntityManager, at);
                    Place(lineDef, MarkingTemplates.WithRun(t, spec.ToString(), item.Path, item.Start, total), 0u, 0f, item.Position, item.Yaw, at, atInfo.CurveOk ? atInfo.Curve.ClosestT(item.Position) : 0f, "auto", TsItem.FlagAuto);
                    placed++;
                }
            }

            // The marks: stop and give-way lines, arrows.
            foreach (AutoStamp stamp in m_AutoPlan.Stamps)
            {
                SignDef markDef = SignCatalog.ById(stamp.Id);
                GuideTemplate t = markDef?.Template;
                if (t == null || placed >= budget)
                    continue;
                GuideSpec spec = t.Default();
                foreach (KeyValuePair<string, string> kv in stamp.Settings)
                    spec = spec.With(kv.Key, kv.Value);
                ChainPiece piece = chain.Pieces[chain.PieceAt(stamp.S)];
                string text = MarkingTemplates.ResolveSize(t, spec.ToString(), piece.Kmh, piece.HasSpeed);
                text = MarkingTemplates.WithFit(t, text, stamp.Span, stamp.Lanes);
                float grade = Mathf.Clamp(chain.GradeAt(stamp.S) * (stamp.WithChain ? 1f : -1f), -0.3f, 0.3f);
                text = MarkingTemplates.WithGrade(t, text, grade);
                Vec3 position = chain.PointAt(stamp.S, stamp.Lateral, m_AutoSurface);
                float yaw = EdgeSnap.YawOf(chain.HeadingAt(stamp.S)) + (stamp.WithChain ? 0f : Mathf.PI);
                Entity at = m_AutoRead.EdgeAt(stamp.S);
                EdgeInfo atInfo = EdgeInfo.Of(EntityManager, at);
                Place(markDef, text, 0u, 0f, position, yaw, at, atInfo.CurveOk ? atInfo.Curve.ClosestT(position) : 0f, "auto", TsItem.FlagAuto);
                placed++;
            }

            // Inside the junctions: the waiting areas and the guide lines. Those are outside the stretch's surface, so what an earlier click placed there is found by its place: an automatic item of the
            // same kind within 1.5 m of where a new one goes.
            foreach (JunctionExtra extra in m_AutoExtras)
            {
                SignDef extraDef = SignCatalog.ById(extra.Id);
                GuideTemplate t = extraDef?.Template;
                if (t == null || placed >= budget)
                    continue;
                EdgeInfo atInfo = EdgeInfo.Of(EntityManager, extra.Edge);
                if (extra.Line == null)
                {
                    replaced += RemoveAutoNear(store, extraDef.Hash, extra.Position, gone);
                    Place(extraDef, extra.Spec, 0u, 0f, extra.Position, extra.Yaw, extra.Edge, atInfo.CurveOk ? atInfo.Curve.ClosestT(extra.Position) : 0f, "auto", TsItem.FlagAuto);
                    placed++;
                    continue;
                }

                string spec = t.Default().With("colour", extra.Spec).ToString();
                float total = WaitingArea.Length(extra.Line);
                foreach (RunItem item in MarkingRuns.Items(extra.Line, MarkingTemplates.PathBudget(t, spec)))
                {
                    if (placed >= budget)
                        break;
                    replaced += RemoveAutoNear(store, extraDef.Hash, item.Position, gone);
                    Place(extraDef, MarkingTemplates.WithRun(t, spec, item.Path, item.Start, total), 0u, 0f, item.Position, item.Yaw, extra.Edge, atInfo.CurveOk ? atInfo.Curve.ClosestT(item.Position) : 0f, "auto", TsItem.FlagAuto);
                    placed++;
                }
            }

            m_Outcome = $"marked {chain.Length:0} m by the standard: {placed} marking(s), {cleared} area(s) cleared, {replaced} earlier automatic marking(s) replaced" + (upgraded > 0 ? $", {upgraded} area(s) widened to all of the game's markings" : "");
            if (m_AutoLogs++ < 40)
            {
                var lines = new List<string> { "Tool: " + m_Outcome + "; " + m_AutoRead.Log + "." };
                foreach (ChainApproach a in m_AutoRead.Approaches)
                {
                    var lanes = new List<string>();
                    foreach (ApproachLane l in a.Lanes)
                        lanes.Add($"{l.Lateral:+0.0;-0.0}:{(l.Left ? "L" : "")}{(l.Straight ? "S" : "")}{(l.Right ? "R" : "")}{(l.UTurn ? "U" : "")}");
                    lines.Add($"  approach at {a.StopS:0.0} m {(a.WithChain ? "with" : "against")} the chain, {a.Control}, crosswalk {(float.IsNaN(a.CrosswalkAt) ? "none" : a.CrosswalkAt.ToString("0.0") + " m beyond")}, lanes {string.Join(" ", lanes)}, flow {(float.IsNaN(a.Flow) ? "-" : a.Flow.ToString("0.00"))}, queue {(float.IsNaN(a.Queue) ? "-" : a.Queue.ToString("0") + " m")}");
                }

                foreach (AutoEnd e in m_AutoPlan.Ends)
                    lines.Add("  " + HintText.AutoEnd(e, false));
                foreach (string note in m_AutoPlan.Notes)
                    lines.Add("  note: " + note);
                Mod.log.Info(string.Join("\n", lines));
            }

            EditorRevision++;
            return true;
        }
    }
}
