using System;
using System.Diagnostics;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;
using RaycastHit = Game.Common.RaycastHit;

namespace CDLC.TS
{
    /// <summary>
    /// The tool that places, edits and deletes the signs. It is a tool of the game's own tool system (so that the game's cursor handling, input
    /// barriers and tool switching apply), but it never touches the game's prefab placement: a sign is a TsItem entity made here.
    ///
    /// Place: the road under the cursor is read (centre curve and cross-section width), the sign is snapped to the side of the road the cursor
    /// is on and turned to face the approaching traffic, and a preview of the real model stands there. The settings of a guide board are a draft
    /// (the figure of the standard that was picked) that the panel edits before and after the sign is placed. Left click places, a right click or
    /// Esc leaves, [ and ] turn it, \ turns it round, the numpad + and - move it in or out. Edit: pointing at a sign of the map and clicking
    /// selects it, and the panel then edits its settings and its support. Delete: the sign under the cursor (found with our own ray, since the
    /// game's bulldozer cannot see them) is highlighted and a left click removes it. Placing a sign with the cursor over a placed sign or its support does not put it on the road: it joins that
    /// support as one more board (up to four, GB 5768.2 4.8.2; see PlacementTool.Attach.cs): the items of one support share a group id and are told apart by their slots. A click on a board
    /// deletes that board, on the support all its boards; editing the support (its form, its reach) changes it for all of them. Keys are read only while no text field has the keyboard.
    /// </summary>
    internal sealed partial class PlacementTool : ToolBaseSystem
    {
        internal const int kMaxItems = 100000;

        /// <summary>The distance from the road's edge that the default margin was set for: half the width of the 80 cm sign (m). A wider board is moved out by what it reaches beyond this.</summary>
        private const float kReferenceReach = 0.4f;

        public override string toolID => "CDLC.TS.Placement";

        internal static PlacementTool Instance { get; private set; }

        /// <summary>The id of the entry chosen in the panel (null: none), and the mode: 0 place, 1 delete, 2 edit, 3 add to a support.</summary>
        internal static string SelectedId { get; private set; }
        internal static int Mode { get; private set; }

        /// <summary>The written settings of the sign that is about to be placed (a guide board's), starting from the figure that was picked; empty for a sign without settings.</summary>
        internal static string Draft { get; private set; } = "";

        /// <summary>The sign picked for editing (Entity.Null: none).</summary>
        internal static Entity EditEntity { get; private set; }

        /// <summary>Bumped whenever what the editor shows changes (a field, the support) and, separately, when it starts to show another sign: the panel rebuilds its fields on the latter.</summary>
        internal static int EditorRevision { get; private set; }
        internal static int EditorTarget { get; private set; }

        /// <summary>The id of the support style chosen in the panel; null is the planner's choice for the board. It stays while another sign is chosen.</summary>
        internal static string SelectedSupport { get; private set; }

        /// <summary>How far the boards placed next on an arm or a beam lean forward (0.12.55, steps of BoardTilt.Step).</summary>
        internal static int PlaceTilt { get; private set; } = BoardTilt.Default;

        /// <summary>How the support of the sign placed next is painted (0.12.57).</summary>
        internal static SupportStyle PlaceStyle { get; private set; }

        private static uint SupportHashOf(string id) => SupportCatalog.ById(id)?.Hash ?? 0u;

        /// <summary>The distance of a sign from the edge of the road (m, positive outwards), adjustable with the < and > keys and the slider of the support page.</summary>
        internal static float Margin { get; private set; } = Carriageway.DefaultMargin;

        private static int s_MarginLogs;

        /// <summary>The one way the margin is set (keys, the numpad alias, the slider): in whole tenths inside the range; the log says what arrived, so that "the keys do nothing" can be told from a key that never came.</summary>
        internal static void SetMargin(float value)
        {
            float was = Margin;
            Margin = KerbMargin.Clamp(value);
            if (s_MarginLogs++ < 60)
                Mod.log.Info($"Tool: distance from the kerb {was:0.0} -> {Margin:0.0} m.");
        }

        /// <summary>One line for the panel: what the tool sees under the cursor.</summary>
        internal static string Debug { get; private set; } = "";

        internal static bool IsActive => Instance != null && Instance.m_ToolSystem.activeTool == Instance;

        /// <summary>The road under the cursor while a board is being placed (Entity.Null: none); read by the extension's probe.</summary>
        internal static Entity HoverEdge { get; private set; }

        /// <summary>The keys of the faces the preview shows, so that the materials of faces that are no longer used can be dropped without dropping these.</summary>
        internal static string[] PreviewFaceKeys { get; private set; }

        private OverlayRenderSystem m_Overlay;
        private CameraUpdateSystem m_Camera;
        private readonly System.Random m_Random = new System.Random();
        private float m_Rotation;
        private bool m_Flip;
        private int m_Placed;
        private GameObject m_Preview;
        private string m_PreviewKey;

        [Preserve]
        public PlacementTool()
        {
        }

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            m_Overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_ToolSystem.EventToolChanged += OnToolChanged;
        }

        [Preserve]
        protected override void OnDestroy()
        {
            ShortcutBarrier.End();
            if (m_ToolSystem != null)
                m_ToolSystem.EventToolChanged -= OnToolChanged;
            DestroyPreview();
            Instance = null;
            base.OnDestroy();
        }

        private void OnToolChanged(ToolBaseSystem tool)
        {
            if (tool == this)
                return;

            // Another tool took over (a vanilla toolbar item, the default tool, a save being loaded): nothing stays chosen, and what was read about the roads is forgotten.
            EdgeInfo.Reset();
            SelectedId = null;
            Mode = 0;
            Draft = "";
            ClearEdit();
            EndAttach();
            ResetRun();
            m_RunNudge = 0f;
            m_PlaceHeight = float.NaN;
            Debug = "";
            SetHint("");
        }

        // ------------------------------------------------------------------ the panel's side

        /// <summary>The settings a new draft of an entry starts with: its own (a saved custom sign), else where the median railings start (MarkingTemplates.StartSettings), else none (the template's defaults).</summary>
        private static string StartDraft(SignChoice choice) => !string.IsNullOrEmpty(choice.Spec) ? choice.Spec : MarkingTemplates.StartSettings(choice.Id);

        /// <summary>Chooses an entry of the panel, with its own settings or (spec) with these (0.12.29: a picture just imported, CustomStore).</summary>
        internal void Choose(string id, string spec = null)
        {
            SignChoice choice = SignCatalog.ChoiceById(id);
            if (choice == null)
                return;
            m_Carrying.ChoseEntry();
            if (Mode == 2 && TryReadEdit(out _))
            {
                // While a sign of the map is being edited, choosing an entry only names the sign that the editor's Replace and Add use; the Place button starts placing it. (Until 0.3.2 it
                // started placing at once and dropped the sign that was being edited.)
                SelectedId = id;
                Draft = spec ?? StartDraft(choice);
                EditorRevision++;
                return;
            }

            SelectedId = id;
            Mode = 0;
            Draft = spec ?? StartDraft(choice);
            ClearEdit();
            ResetRun();
            m_RunNudge = 0f;
            m_PlaceHeight = float.NaN;
            m_Rotation = 0f;
            m_Flip = false;
            EditorTarget++;
            EditorRevision++;
            Activate();
        }

        /// <summary>Chooses the support style (an empty id: the planner's choice). While a sign of the map is being edited it is that sign's support that changes.</summary>
        internal void ChooseSupport(string id)
        {
            m_Carrying.ChoseSupport();
            uint hash = SupportHashOf(id);
            if (Mode == 2 && TryReadEdit(out TsItem item))
            {
                // A sign on a wall stays on its wall and a sign that stands stays where it stands (0.12.2): the place of one is no place for the other. Between the two wall supports the sign keeps its place
                // on the wall and turns (WallMount.Convert); a bracket keeps the way it faces.
                SupportDef now = SupportCatalog.ByHash(item.SupportHash), next = SupportCatalog.ById(id);
                // The panel names a mirror as the support it mirrors: choosing that one keeps the arm the way it reaches (0.12.25: a cantilever from a median).
                if (now != null && next != null && SupportKinds.OneWayArm(now.Kind) && SupportCatalog.ListedOf(now) == next)
                    return;
                bool wasWall = now != null && SupportKinds.OnWall(now.Kind), toWall = next != null && SupportKinds.OnWall(next.Kind);
                // A board on a signal's arm hangs where the arm is (0.12.51): it is placed onto an arm, and stays on it.
                bool wasArm = now != null && SupportKinds.OnArm(now.Kind), toArm = next != null && SupportKinds.OnArm(next.Kind);
                // A board over the road hangs where a structure is (0.12.81): it is placed onto one, and stays on it. On its face or hung under it (0.12.81) is not changed either: the reach means another height.
                bool wasOver = now != null && SupportKinds.OverRoad(now.Kind), toOver = next != null && SupportKinds.OverRoad(next.Kind);
                if (wasOver != toOver || wasOver && now.Hanger != next.Hanger)
                {
                    LastRefusal = new HintInfo { Kind = "support-over", Bad = true };
                    EditorRevision++;
                    Mod.log.Info($"Edit: the support of {(wasOver ? "a board over the road" : "a standing sign")} is not changed to {id ?? "the planned one"}: a board goes onto a structure over the road when it is placed.");
                    return;
                }

                if (wasArm != toArm)
                {
                    LastRefusal = new HintInfo { Kind = "support-arm", Bad = true };
                    EditorRevision++;
                    Mod.log.Info($"Edit: the support of {(wasArm ? "a board on a signal arm" : "a standing sign")} is not changed to {id ?? "the planned one"}: a board goes onto an arm when it is placed.");
                    return;
                }

                if (wasWall != toWall)
                {
                    LastRefusal = new HintInfo { Kind = toWall ? "support-to-wall" : "support-from-wall", Bad = true };
                    EditorRevision++;
                    Mod.log.Info($"Edit: the support of {(wasWall ? "a sign on a wall" : "a standing sign")} is not changed to {id ?? "the planned one"}: {(toWall ? "a sign that stands does not go onto a wall" : "a sign on a wall does not leave it")}.");
                    return;
                }

                float yaw = item.Yaw;
                if (toWall)
                {
                    if (now.Kind == next.Kind)
                        return;
                    int facing = now.Kind == SupportKind.WallBracket ? WallMount.FacingOf(item.Yaw, now.Side) : 1;
                    yaw = WallMount.Convert(now.Kind, now.Side, item.Yaw, next.Kind, facing, out int side);
                    if (next.Kind == SupportKind.WallBracket && (side < 0) != (next.Side < 0))
                        next = SupportCatalog.ById(next.MirrorId) ?? next;
                    hash = next.Hash;
                }

                LastRefusal = default;
                // The support is one for all the boards on it. Between one that hangs its boards and one that stacks them a board's place means another thing (along the beam, up the post:
                // 0.12.24): the boards take the standard's places again.
                List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
                AssemblyPlan before = ItemGroups.PlanOf(RecordsOf(members), out List<AssemblyBoard> ordered);
                bool replace = before != null && before.Hung != SupportKinds.Hung(Assembly.Plan(ordered, SupportCatalog.ByHash(hash), item.Reach).Support.Kind);
                foreach (KeyValuePair<Entity, ItemRecord> member in members)
                {
                    TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                    each.SupportHash = hash;
                    each.Yaw = yaw;
                    // The clearance of one support is not the next one's (a height-limit frame is lower than a gantry).
                    each.Clearance = 0f;
                    if (next?.DefaultFinish is SupportFinish finish)
                    {
                        SupportStyle style = SupportStyle.Of(each.Style);
                        style.Finish = finish;
                        each.Style = style.Packed;
                    }
                    if (replace)
                    {
                        each.Lane = 0;
                        each.OffsetX = 0f;
                    }

                    WriteItem(member.Key, each);
                }

                EditorRevision++;
                Mod.log.Info($"Support of the edited sign: {(hash == 0u ? "planned from the board" : SupportCatalog.ByHash(hash)?.Id ?? id)}.");
                return;
            }

            string supportBefore = SelectedSupport;
            SelectedSupport = SupportCatalog.ById(id) != null ? id : null;
            if (supportBefore != SelectedSupport)
                ResetSpan();
            // A support with a finish of its own (0.12.57: the Shanghai pole's graphite) takes it; the backs and the root stay as they were.
            if (SupportCatalog.ById(id)?.DefaultFinish is SupportFinish chosen)
                PlaceStyle = new SupportStyle { Finish = chosen, BlackBack = PlaceStyle.BlackBack, Root = PlaceStyle.Root };
            EditorRevision++;
            Mod.log.Info($"Support: {SelectedSupport ?? "planned from the board"}.");
        }

        /// <summary>The next (or previous) support in the order: the planner's choice, then the catalogue.</summary>
        private void CycleSupport(int step)
        {
            IReadOnlyList<SupportDef> listed = SupportCatalog.Listed;
            int count = listed.Count + 1;
            int index = 0;
            string current = EffectiveSupportId();
            for (int i = 0; i < listed.Count; i++)
            {
                if (listed[i].Id == current)
                    index = i + 1;
            }

            index = ((index + step) % count + count) % count;
            ChooseSupport(index == 0 ? null : listed[index - 1].Id);
        }

        /// <summary>The id of the support chosen for what the panel is showing (null: the planner's choice); a hidden mirror (a bracket that reaches the other way) is named as the support it mirrors.</summary>
        internal static string EffectiveSupportId()
        {
            if (Mode == 2 && Instance != null && Instance.TryReadEdit(out TsItem item))
                return SupportCatalog.ListedOf(SupportCatalog.ByHash(item.SupportHash))?.Id;
            return SelectedSupport;
        }

        private int m_PlaceLaneLogs;
        private Entity m_LanesLogEdge;
        private int m_SpeedLogs;
        private Entity m_SpeedLogEdge;
        private int m_NoRoadLogs;
        private Game.Net.SearchSystem m_NoRoadSearch;
        private float3 m_NoRoadAt = new float3(float.MaxValue, 0f, 0f);

        /// <summary>For the log: what an entity the ray found is (edge, node, terrain, an object, nothing).</summary>
        private string KindOf(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return "nothing";
            var kinds = new List<string>();
            if (EntityManager.HasComponent<Edge>(entity))
                kinds.Add("an edge");
            if (EntityManager.HasComponent<Node>(entity))
                kinds.Add("a node");
            if (EntityManager.HasComponent<Game.Common.Terrain>(entity))
                kinds.Add("the terrain");
            if (EntityManager.HasComponent<Game.Objects.Object>(entity))
                kinds.Add("an object");
            return kinds.Count > 0 ? string.Join(" and ", kinds) : "something else";
        }
        private int m_LaneLogs;
        private string m_LaneLogKey = "";

        /// <summary>
        /// The lanes the lead of a support sees (see RoadReader.TryLaneCentres), those beside or behind the first post left out; false when none could be read. What was read is written to the
        /// log when it changes (the first thirty times): the numbers of lanes and where they are, which is what a report of "the lanes are wrong" is checked against.
        /// </summary>
        private bool TryLaneCentresOf(in TsItem lead, List<float> centres)
        {
            SupportDef leadSupport = SupportCatalog.ByHash(lead.SupportHash);
            int arms = leadSupport == null ? 1 : leadSupport.Kind == SupportKind.DoubleCantilever ? 0 : SupportKinds.OneWayArm(leadSupport.Kind) && leadSupport.Side < 0 ? -1 : 1;
            bool ok = RoadReader.TryLaneCentres(EntityManager, lead.Edge, lead.Position, lead.Yaw, centres, out RoadReader.LaneReport report, arms);
            string key = $"{lead.Edge.Index}:{lead.Edge.Version}|{lead.Position.x:0.0}|{lead.Position.z:0.0}|{lead.Yaw:0.00}|{centres.Count}";
            if (key != m_LaneLogKey && m_LaneLogs++ < 30)
            {
                m_LaneLogKey = key;
                string xs = string.Join(", ", centres.ConvertAll(x => x.ToString("0.0")));
                Mod.log.Info($"Lanes: edge {lead.Edge.Index}:{lead.Edge.Version} at {lead.Position}, yaw {lead.Yaw * Mathf.Rad2Deg:0.#} deg: {report}; lanes from the driver's left at X = [{xs}] m from the first post{(ok ? "" : " (none)")}.");
            }

            return ok;
        }

        /// <summary>The settings a sign that is put on the map or on a support gets: the draft's, with the size band of the road its support stands at.</summary>
        private string SpecForNew(SignDef def, Entity edge, float3 position, float yaw)
        {
            float kmh = 0f;
            bool highway = false;
            bool hasSpeed = false;
            if (edge != Entity.Null && EntityManager.Exists(edge))
            {
                // From the cache of what was read about the road (EdgeInfo): this is asked at every frame the cursor is over a support.
                hasSpeed = EdgeInfo.Of(EntityManager, edge).TrySpeed(EntityManager, out kmh, out RoadReader.SpeedReport report);
                highway = hasSpeed && report.Highway;
            }

            string spec = GuideTemplates.ResolveSpeed(def, Draft, hasSpeed ? kmh : 0f, hasSpeed, highway);
            // A junction sign takes its names and form from the junction ahead of the place it goes to.
            return ApplyJunction(def, spec, edge, position, yaw, "joined");
        }

        /// <summary>The editor works on another board of the support it is editing (the member's place in the list the editor shows).</summary>
        internal void SelectBoard(int member)
        {
            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            if (member < 0 || member >= members.Count || members[member].Key == EditEntity)
                return;
            EditEntity = members[member].Key;
            EditorTarget++;
            EditorRevision++;
        }

        /// <summary>
        /// The editor's four buttons: the same as the arrow keys. 1 right, -1 left, 2 down, -2 up (as the screen shows; the beam's left and right are the screen's, like the keys').
        /// </summary>
        internal void NudgeBoard(int code)
        {
            m_ArrowX = code == 1 ? 1 : code == -1 ? -1 : 0;
            m_ArrowY = code == 2 ? 1 : code == -2 ? -1 : 0;
            m_Shift = false;
            try
            {
                MoveEditedBoard();
            }
            finally
            {
                m_ArrowX = 0;
                m_ArrowY = 0;
            }
        }

        /// <summary>Takes a board off the support it is on (the last board of a support is not taken off: Delete takes the whole support).</summary>
        internal void RemoveBoard(int member)
        {
            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            if (members.Count <= 1 || member < 0 || member >= members.Count)
                return;

            Entity gone = members[member].Key;
            Entity next = Entity.Null;
            foreach (KeyValuePair<Entity, ItemRecord> other in members)
            {
                if (other.Key != gone)
                {
                    next = other.Key;
                    break;
                }
            }

            if (gone == EditEntity)
                EditEntity = next;
            DestroyItem(gone);
            RefitReach(EntitiesOf(members, gone), "a board was taken off");

            // A support with one board left is a sign on a support of its own again (it keeps its lane).
            if (members.Count - 1 == 1)
            {
                TsItem last = EntityManager.GetComponentData<TsItem>(next);
                last.Group = 0u;
                last.Slot = 0;
                WriteItem(next, last);
            }

            EditorTarget++;
            EditorRevision++;
            Mod.log.Info($"Edit: took board {member + 1} off a support of {members.Count}.");
        }

        /// <summary>Makes a board another sign: the one chosen in the list. Its place on the support (slot, lane) stays.</summary>
        internal void ReplaceBoard(int member)
        {
            SignChoice choice = SignCatalog.ChoiceById(SelectedId);
            if (choice?.Def == null || Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            if (member < 0 || member >= members.Count)
                return;

            Entity entity = members[member].Key;
            TsItem target = EntityManager.GetComponentData<TsItem>(entity);
            SignDef def = choice.Def;
            target.TypeHash = def.Hash;
            target.Spec = default;
            target.SetSpec(def.Template != null ? SpecForNew(def, target.Edge, target.Position, target.Yaw) : "");
            WriteItem(entity, target);
            RefitReach(EntitiesOf(members, Entity.Null), "a board was changed");
            EditorTarget++;
            EditorRevision++;
            Mod.log.Info($"Edit: board {member + 1} of a support of {members.Count} is now {def.Id}, settings '{target.SpecText}'.");
        }

        /// <summary>Puts the sign chosen in the list on the support that is being edited, as one more board (up to four).</summary>
        internal void AddBoardHere()
        {
            SignChoice choice = SignCatalog.ChoiceById(SelectedId);
            if (choice?.Def == null || Mode != 2 || !TryReadEdit(out TsItem item) || ChunkSystem.Instance == null || ChunkSystem.Instance.Records.Count >= kMaxItems)
                return;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            TryAddBoard(members, choice);
        }

        /// <summary>One lane for each board of the support being edited, from the driver's left in the order they hang (Assembly.SpreadOverLanes). Only an arm or a beam with lanes to read.</summary>
        internal void SpreadLanes()
        {
            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            var records = new List<ItemRecord>();
            foreach (KeyValuePair<Entity, ItemRecord> member in members)
                records.Add(member.Value);
            AssemblyPlan plan = ItemGroups.PlanOf(records, out List<AssemblyBoard> ordered);
            var centres = new List<float>();
            if (plan == null || !plan.Hung || !TryLaneCentresOf(EntityManager.GetComponentData<TsItem>(members[0].Key), centres))
                return;

            // The boards on the post stay there: the others go over the lanes.
            List<AssemblyBoard> overRoad = ordered.FindAll(b => !Assembly.IsPostBoard(b));
            List<KeyValuePair<int, float>> spread = Assembly.SpreadOverLanes(overRoad, centres);
            ordered = overRoad;
            for (int i = 0; i < ordered.Count; i++)
            {
                Entity entity = members[ordered[i].Member].Key;
                TsItem each = EntityManager.GetComponentData<TsItem>(entity);
                each.Lane = (byte)Mathf.Clamp(spread[i].Key, 0, 255);
                each.OffsetX = spread[i].Value;
                WriteItem(entity, each);
            }

            RefitReach(EntitiesOf(members, Entity.Null), "the boards were spread over the lanes");
            EditorRevision++;
            Mod.log.Info($"Edit: {ordered.Count} board(s) spread over {Mathf.Min(ordered.Count, centres.Count)} of {centres.Count} lane(s).");
        }

        /// <summary>
        /// Puts the board being edited over a lane of the road (1 is the leftmost of the lanes it faces, from the driver's seat; 0 side by side with the others), or on the post (Assembly.OnPost: a small
        /// board, with room under the arm; LastRefusal says why not). Only an arm or a beam has lanes, and a post that takes boards.
        /// </summary>
        internal void SetLane(int lane)
        {
            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            if (lane == Assembly.OnPost)
            {
                List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
                int own = members.FindIndex(pair => pair.Key == EditEntity);
                AssemblyPlan plan = ItemGroups.PlanOf(RecordsOf(members), out List<AssemblyBoard> all);
                AssemblyBoard self = all.Find(b => b.Member == own);
                if (plan == null || self == null || !SupportKinds.PostBoards(plan.Support.Kind))
                    return;
                AttachResult onPost = Attach.Evaluate(new AttachRequest
                {
                    Existing = all.FindAll(b => b.Member != own), Support = plan.Support, Reach = members[0].Value.Reach, Board = self.Instance, Member = own, Slot = self.Slot, OnPost = true
                });
                if (!onPost.Ok)
                {
                    LastRefusal = new HintInfo { Kind = onPost.Reason == AttachReason.Large ? "refuse-large" : "refuse-post-height", Count = members.Count, Left = onPost.SpaceBefore, Bad = true };
                    EditorRevision++;
                    Mod.log.Info($"Edit: the board does not go on the post: {onPost.Reason} ({onPost.SpaceBefore:0.0} m of room under the arm).");
                    return;
                }

                LastRefusal = default;
                item.Lane = (byte)Assembly.OnPost;
                item.OffsetX = 0f;
                item.Rise = 0f;
            }
            else if (lane <= 0)
            {
                item.Lane = 0;
                item.OffsetX = 0f;
                // Off the post onto the arm or the beam: it faces as the support does (0.12.8), and comes back to the post that way.
                item.Turn = 0f;
            }
            else
            {
                List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
                var records = new List<ItemRecord>();
                foreach (KeyValuePair<Entity, ItemRecord> member in members)
                    records.Add(member.Value);
                AssemblyPlan plan = ItemGroups.PlanOf(records, out _);
                var centres = new List<float>();
                if (plan == null || !plan.Hung || !TryLaneCentresOf(EntityManager.GetComponentData<TsItem>(members[0].Key), centres) || lane > centres.Count || lane > 255)
                    return;
                item.Lane = (byte)lane;
                item.OffsetX = centres[lane - 1];
                item.Turn = 0f;
            }

            WriteItem(EditEntity, item);
            RefitReach(EntitiesOf(MembersOf(EditEntity, item), Entity.Null), "a board was moved");
            EditorRevision++;
            Mod.log.Info($"Edit: the board is now {(item.Lane == 0 ? "side by side with the others" : item.Lane == Assembly.OnPost ? "on the post" : $"over lane {item.Lane}, {item.OffsetX:0.0} m from the first post")}.");
        }

        /// <summary>Changes one setting of a guide board: of the sign being edited, or of the draft of the one about to be placed.</summary>
        internal void EditField(int target, string key, string value)
        {
            // The UI may still have the old field on screen when the world selects another sign, or after the tool was left.
            if (target != EditorTarget || !IsActive)
                return;
            if (Mode == 2 && TryReadEdit(out TsItem item))
            {
                SignDef def = SignCatalog.ByHash(item.TypeHash);
                if (def?.Template == null)
                    return;
                GuideSpec seed = def.Template.Parse(item.SpecText);
                GuideSpec spec = seed.With(key, value);
                // GuideSpec has a byte limit. Editing a visible option of an older, full clearing must not silently shorten its hidden outline or matching heights.
                if (def.Id == VanillaClear.Id && key != MarkingPolygon.Key && key != ClearElevation.Key &&
                    (spec.Get(MarkingPolygon.Key) != seed.Get(MarkingPolygon.Key) || spec.Get(ClearElevation.Key) != seed.Get(ClearElevation.Key)))
                    return;
                string text = spec.ToString();
                if (text == seed.ToString())
                    return;
                item.SetSpec(text);
                // A marking the automatic marking placed is the player's once it is edited: marking the stretch again leaves it.
                item.Flags = (byte)(item.Flags & ~TsItem.FlagAuto);
                WriteItem(EditEntity, item);
                EditorRevision++;
                return;
            }

            SignChoice choice = SignCatalog.ChoiceById(SelectedId);
            if (choice?.Def.Template == null)
                return;
            GuideSpec draftSeed = choice.Def.Template.Parse(Draft);
            GuideSpec draftSpec = draftSeed.With(key, value);
            if (choice.Def.Id == VanillaClear.Id && key != MarkingPolygon.Key && key != ClearElevation.Key &&
                (draftSpec.Get(MarkingPolygon.Key) != draftSeed.Get(MarkingPolygon.Key) || draftSpec.Get(ClearElevation.Key) != draftSeed.Get(ClearElevation.Key)))
                return;
            string before = draftSeed.ToString();
            string after = draftSpec.ToString();
            if (before == after)
                return;
            Draft = after;
            EditorRevision++;
        }

        /// <summary>
        /// A picture on the board the editor shows (0.12.49, a lamp-post ad: its template has LampAds.PictureKey): the kept picture's key and size, and the style that shows it; an empty key takes the
        /// picture off (the print comes back).
        /// </summary>
        internal void SetPicture(string key, int width, int height)
        {
            EditorView view = GetEditorView();
            if (!view.Active || view.Def?.Template?.FieldOf(Geo.Signs.LampAds.PictureKey) == null)
                return;
            bool on = key.Length > 0;
            EditField(EditorTarget, Geo.Signs.LampAds.PictureKey, key);
            EditField(EditorTarget, "pw", on ? width.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            EditField(EditorTarget, "ph", on ? height.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            EditField(EditorTarget, "style", on ? "pic" : "vert");
        }

        /// <summary>Sets the span of the gantry or the arm of the cantilever of the sign being edited (m); 0 is the support's default.</summary>
        internal void SetReach(float metres)
        {
            if (Mode != 2 || !TryReadEdit(out TsItem item) || float.IsNaN(metres))
                return;
            metres = Mathf.Clamp(metres, 0f, 60f);
            if (Mathf.Abs(item.Reach - metres) < 0.05f)
                return;
            foreach (KeyValuePair<Entity, ItemRecord> member in MembersOf(EditEntity, item))
            {
                TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                // What the player sets is the support's own length (0.12.9): boards make it longer when they need it, and give it back when they are taken off.
                each.Reach = metres;
                each.BaseReach = metres;
                each.Flags |= TsItem.FlagBaseReach | TsItem.FlagReachByHand;
                WriteItem(member.Key, each);
            }

            EditorRevision++;
        }

        /// <summary>How far the boards of the support being edited lean forward (0.12.55, BoardTilt: degrees, to its steps); while placing, the boards placed next.</summary>
        internal void SetTilt(int degrees)
        {
            int steps = Mathf.Clamp(Mathf.RoundToInt(degrees / BoardTilt.Step), 0, BoardTilt.Max);
            if (Mode == 2 && TryReadEdit(out TsItem item))
            {
                foreach (KeyValuePair<Entity, ItemRecord> member in MembersOf(EditEntity, item))
                {
                    TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                    each.SetTilt(steps);
                    WriteItem(member.Key, each);
                }

                Mod.log.Info($"Edit: the boards of the support lean {steps * BoardTilt.Step:0}°.");
            }
            else
            {
                PlaceTilt = steps;
            }

            EditorRevision++;
        }

        /// <summary>
        /// How the support being edited is painted (0.12.57, SupportStyle): its finish (0 to 3), its boards' black backs (0 off, 1 on), the paint on its posts' root (0 to 2); what is negative stays as
        /// it is. While placing, the support placed next.
        /// </summary>
        internal void SetStyle(int finish = -1, int blackBack = -1, int root = -1)
        {
            SupportStyle Changed(SupportStyle now)
            {
                if (finish >= 0)
                    now.Finish = (SupportFinish)Mathf.Clamp(finish, 0, 3);
                if (blackBack >= 0)
                    now.BlackBack = blackBack > 0;
                if (root >= 0)
                    now.Root = (RootPaint)Mathf.Clamp(root, 0, 2);
                return now;
            }

            if (Mode == 2 && TryReadEdit(out TsItem item))
            {
                byte style = Changed(SupportStyle.Of(item.Style)).Packed;
                foreach (KeyValuePair<Entity, ItemRecord> member in MembersOf(EditEntity, item))
                {
                    TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                    each.Style = style;
                    WriteItem(member.Key, each);
                }

                SupportStyle now = SupportStyle.Of(style);
                Mod.log.Info($"Edit: the support is painted {now.Finish}{(now.BlackBack ? ", the boards' backs black" : "")}, root paint {now.Root}.");
            }
            else
            {
                PlaceStyle = Changed(PlaceStyle);
                Profiles.Touch(finish, blackBack, root);
            }

            EditorRevision++;
        }

        /// <summary>What the panel's editor shows: the sign (or draft), its settings, and its support. Active is false when there is nothing to edit.</summary>
        internal struct EditorView
        {
            public bool Active;
            public bool IsItem;
            public SignDef Def;
            public string Spec;
            public uint SupportHash;

            /// <summary>The reach of the sign being edited (the span of a gantry, the arm of a cantilever), and whether its support has one.</summary>
            public float Reach;
            public bool HasReach;

            /// <summary>The kind of support the reach is for (the editor's labels and the slider's range), and whether the length is the player's (a placed one: set by hand; the next one: the override).</summary>
            public SupportKind ReachFor;
            public bool ReachByHand;

            /// <summary>The height of the lower edge of the boards of a hung support the clearance slider shows (m; the layout's, or the player's for the one about to be placed), and whether it is the player's (TsItem.Clearance).</summary>
            public float Clearance;
            public bool ClearanceByHand;

            /// <summary>The support that is drawn (that of the whole support's boards when there are several), the layout it has, the id the planner would choose for the whole, and how many boards are on it and which of them (1-based, from the top or left) this is.</summary>
            public SupportDef Support;
            public SupportLayout Layout;
            public string PlanId;
            public int Boards;
            public int Index;

            /// <summary>The most boards the support being edited carries (4 on a post or an arm, 12 on a gantry).</summary>
            public int MaxBoards;

            /// <summary>The boards on the support in the order they hang, with where each stands; the lanes of the road that the boards face (the sign-frame X of each, from the driver's left); why there are none ("post": the support has no lanes, "noroad": none could be read, "ok": there are); the pairs of boards that overlap; and the board being edited, its lane and its place.</summary>
            public List<BoardLine> BoardList;
            public List<float> LaneCentres;
            public string LaneState;
            public List<int[]> Overlaps;
            public int SelectedMember;
            public int SelectedLane;

            /// <summary>The support's post takes boards of its own (SupportKinds.PostBoards), and the support is on a wall: 1 flat, 2 a bracket (0: not).</summary>
            public bool PostBoards;
            public int Wall;

            /// <summary>The board being edited turns on its post (0.12.8), and how far it is turned (degrees, + clockwise seen from above).</summary>
            public bool Turns;
            public float TurnDegrees;

            /// <summary>The board being edited is on a gantry's beam (it may go to the other face, 0.12.22), and it is on the back.</summary>
            public bool Faces, Back;

            /// <summary>The board is on another mod's signal arm (0.12.57: the editor's buttons move it along the arm and up and down by the room its heads leave).</summary>
            public bool OnArm;

            /// <summary>The support's posts can be painted at the root (SupportKinds.RootPaintable); the editor's row of it is shown for such a support only.</summary>
            public bool RootPaintable;

            /// <summary>The support's boards lean forward (0.12.55: an arm or a beam), and by how many steps of BoardTilt.Step.</summary>
            public bool Leans;
            public int TiltSteps;

            /// <summary>How the support is painted (0.12.57); Painted: the sign has a support (not a marking).</summary>
            public bool Painted;
            public SupportStyle Style;

        }

        /// <summary>One board of an edited support, for the editor's list.</summary>
        internal struct BoardLine
        {
            public int Member;
            public string Code, Zh, En;
            public int Lane;
            public float X;
        }

        internal EditorView GetEditorView()
        {
            if (Mode == 2 && TryReadEdit(out TsItem item))
            {
                SignDef def = SignCatalog.ByHash(item.TypeHash);
                // A road marking has no support and no boards: its settings are all there is.
                if (def != null && def.IsMarking)
                    return new EditorView { Active = true, IsItem = true, Def = def, Spec = item.SpecText, Boards = 1, Index = 1, MaxBoards = 1, LaneState = "post" };
                if (def != null)
                {
                    // The support and its layout are those of the whole of the boards on it.
                    List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
                    var records = new List<ItemRecord>();
                    foreach (KeyValuePair<Entity, ItemRecord> member in members)
                        records.Add(member.Value);
                    int own = members.FindIndex(pair => pair.Key == EditEntity);
                    AssemblyPlan plan = ItemGroups.PlanOf(records, out List<AssemblyBoard> ordered);
                    if (plan != null)
                    {
                        SupportLayout layout = plan.Layout;
                        SupportDef support = plan.Support;
                        bool overhead = plan.Hung;
                        int place = ordered.FindIndex(board => board.Member == own);
                        var lines = new List<BoardLine>();
                        foreach (Placement placement in plan.Placed)
                        {
                            SignDef named = SignCatalog.ByHash(records[placement.Board.Member].TypeHash) ?? placement.Def;
                            lines.Add(new BoardLine { Member = placement.Board.Member, Code = named.Code, Zh = named.Zh, En = named.En, Lane = placement.Board.Lane, X = placement.Cx });
                        }

                        var centres = new List<float>();
                        string laneState = overhead ? "noroad" : "post";
                        if (overhead && TryLaneCentresOf(EntityManager.GetComponentData<TsItem>(members[0].Key), centres))
                            laneState = "ok";
                        return new EditorView
                        {
                            Active = true,
                            IsItem = true,
                            Def = def,
                            Spec = item.SpecText,
                            SupportHash = item.SupportHash,
                            Reach = overhead ? layout.ArmLength : 0f,
                            HasReach = overhead,
                            ReachFor = support.Kind,
                            ReachByHand = item.ReachByHand,
                            Clearance = layout.BottomClearance,
                            ClearanceByHand = item.Clearance > 0f,
                            Support = support,
                            Layout = layout,
                            PlanId = SupportPlanner.Auto(Assembly.TotalDef(ordered, false), ordered.Count > 1).Id,
                            Boards = members.Count,
                            MaxBoards = Assembly.MaxFor(support.Kind),
                            Index = place >= 0 ? place + 1 : own + 1,
                            BoardList = lines,
                            LaneCentres = centres,
                            LaneState = laneState,
                            Overlaps = Assembly.Overlaps(plan.Placed, overhead),
                            SelectedMember = own,
                            SelectedLane = item.Lane,
                            PostBoards = SupportKinds.PostBoards(support.Kind),
                            Wall = support.Kind == SupportKind.WallFlat ? 1 : support.Kind == SupportKind.WallBracket ? 2 : 0,
                            Turns = EditedTurn(out float turnDegrees),
                            TurnDegrees = turnDegrees,
                            Faces = EditedFace(out bool back),
                            Back = back,
                            OnArm = SupportKinds.OnArm(support.Kind),
                            RootPaintable = SupportKinds.RootPaintable(support.Kind, layout),
                            Leans = BoardTilt.Leans(support),
                            TiltSteps = item.TiltSteps,
                            Painted = true,
                            Style = SupportStyle.Of(item.Style)
                        };
                    }
                }
            }

            SignChoice choice = Mode == 0 ? SignCatalog.ChoiceById(SelectedId) : null;
            if (choice != null && choice.Def.IsMarking)
                return new EditorView { Active = true, IsItem = false, Def = choice.Def, Spec = Draft, Boards = 1, Index = 1 };
            if (choice != null)
            {
                SignInstance draft = SignInstances.Of(choice.Def, Draft);
                // With the support automatic the planner's choice in the profile in force, and the paint it comes with, are what the board will get.
                PlannedSupport styled = SelectedSupport == null ? Profiles.Plan(draft.Def, default, PlaceStyle) : default;
                SupportDef drafted = SelectedSupport == null ? styled.Support : SupportCatalog.For(draft.Def, SupportHashOf(SelectedSupport));
                var drafting = new EditorView
                {
                    Active = true,
                    IsItem = false,
                    Def = choice.Def,
                    Spec = Draft,
                    SupportHash = SupportHashOf(SelectedSupport),
                    Support = drafted,
                    Layout = SupportLayouts.Of(draft.Def, drafted, 0f),
                    PlanId = SelectedSupport == null ? styled.Support.Id : SupportPlanner.Auto(draft.Def).Id,
                    Boards = 1,
                    Index = 1,
                    RootPaintable = SupportKinds.RootPaintable(drafted.Kind, SupportLayouts.Of(draft.Def, drafted, 0f)),
                    Leans = BoardTilt.Leans(drafted),
                    TiltSteps = PlaceTilt,
                    Painted = true,
                    Style = SelectedSupport == null ? styled.Style : PlaceStyle,
                    Turns = PlaceTurns(),
                    TurnDegrees = PlacedTurnDegrees()
                };
                DraftSpan(ref drafting, draft.Def, drafted);
                return drafting;
            }

            return default;
        }

        /// <summary>
        /// The items on the support an item is on, read from the entities themselves (the renderer's copy may be a frame behind) and in the order of ItemGroups.Compare: the item alone when it
        /// has no group. The first is the lead.
        /// </summary>
        private List<KeyValuePair<Entity, ItemRecord>> MembersOf(Entity entity, in TsItem item)
        {
            var list = new List<KeyValuePair<Entity, ItemRecord>> { new KeyValuePair<Entity, ItemRecord>(entity, ChunkSystem.ToRecord(item)) };
            if (item.Group == 0u || ChunkSystem.Instance == null)
                return list;
            var sameTile = new List<KeyValuePair<Entity, ItemRecord>>();
            ChunkSystem.Instance.Store.GroupMembers(item.Group, item.Position.x, item.Position.z, sameTile);
            foreach (KeyValuePair<Entity, ItemRecord> pair in sameTile)
            {
                if (pair.Key == entity || pair.Value.Group != item.Group || !EntityManager.Exists(pair.Key) || !EntityManager.HasComponent<TsItem>(pair.Key))
                    continue;
                TsItem actual = EntityManager.GetComponentData<TsItem>(pair.Key);
                if (actual.Group == item.Group)
                    list.Add(new KeyValuePair<Entity, ItemRecord>(pair.Key, ChunkSystem.ToRecord(actual)));
            }

            return ItemGroups.Sorted(list, Comparer<Entity>.Default);
        }

        private bool TryReadEdit(out TsItem item)
        {
            item = default;
            if (EditEntity == Entity.Null || !EntityManager.Exists(EditEntity) || !EntityManager.HasComponent<TsItem>(EditEntity))
                return false;
            item = EntityManager.GetComponentData<TsItem>(EditEntity);
            return true;
        }

        private void ClearEdit()
        {
            EndLiveEdit();
            LastRefusal = default;
            if (EditEntity != Entity.Null)
            {
                EditEntity = Entity.Null;
                m_ShiftEntity = Entity.Null;
                EditorTarget++;
                EditorRevision++;
            }
        }

        internal void SetMode(int mode)
        {
            // 0 place (over a support the sign joins it), 1 delete, 2 edit, 3 the eyedropper (StylePick). (The old 3, add to a support, was a mode until 0.4.0; placing over a support does that now.)
            Mode = StylePick.ModeOf(mode);
            m_Carrying.ModeChanged(Mode);
            ResetRun();
            if (Mode != 2)
            {
                ClearEdit();
                EndLiveEdit();
            }
            EditorTarget++;
            EditorRevision++;
            if (Mode != 0 || SelectedId != null)
                Activate();
        }

        internal void Leave()
        {
            ShortcutBarrier.End();
            ResetFacade();
            ResetSpan();
            SelectedId = null;
            Mode = 0;
            Draft = "";
            m_Carrying.Left();
            ClearEdit();
            EndAttach();
            ResetRun();
            SetHint("");
            if (m_ToolSystem.activeTool == this)
                m_ToolSystem.activeTool = m_DefaultToolSystem;
        }

        private void Activate()
        {
            if (GameManager.instance.gameMode != GameMode.Game || !m_ToolSystem.actionMode.IsGame())
                return;
            if (m_ToolSystem.activeTool != this)
                m_ToolSystem.activeTool = this;
        }

        // ------------------------------------------------------------------ the tool

        [Preserve]
        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            requireUnderground = false;
            applyAction.shouldBeEnabled = true;
            secondaryApplyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            if (GameManager.instance.gameMode == GameMode.Game && m_ToolSystem.actionMode.IsGame())
                ShortcutBarrier.Begin();
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            ShortcutBarrier.End();
            ResetSpan();
            // Destroyed, not hidden: the materials are rebuilt when a city is left, and a kept preview would hold dead ones. The support being edited goes back into its tile.
            EndLiveEdit();
            EndAttach();
            ResetRun();
            DestroyPreview();
            base.OnStopRunning();
        }

        // The game's own elevation control (Page Up / Page Down, the toolbar's switch) is ours too: down, the ray sees only roads in tunnels, as the game's net tool does, and the game shows the tunnels.
        public override bool allowUnderground => true;

        public override void SetUnderground(bool underground) => requireUnderground = underground;

        public override void ElevationUp() => requireUnderground = false;

        public override void ElevationDown() => requireUnderground = true;

        public override void ElevationScroll() => requireUnderground = !requireUnderground;

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            if (Mode == 0 && SelectedId != null)
            {
                // With a wall support chosen the ray takes the buildings (their meshes: the hit has the normal of the wall) instead of the roads (0.12.2).
                bool wall = WallChosen() && !(SignCatalog.ChoiceById(SelectedId)?.Def?.IsMarking ?? false);
                if (wall)
                {
                    m_ToolRaycastSystem.typeMask = TypeMask.StaticObjects | TypeMask.Terrain;
                }
                else if (requireUnderground)
                {
                    // Without the terrain: it is nearer than a tunnel under it, and would be what the ray hits.
                    m_ToolRaycastSystem.typeMask = TypeMask.Net;
                    m_ToolRaycastSystem.collisionMask = CollisionMask.Underground;
                }
                else
                {
                    // A road in a tunnel is Underground to the game (NetUtils.GetCollisionMask), which the base's ground and overground mask leaves out; a road in a cutting has the ground bits too (the
                    // log of 5 Nov: OnGround, Overground, Underground) and is not missed for the mask but because the terrain above it is nearer to the camera: PlacementTool.Cutting puts the camera's ray
                    // through it. With the terrain in the ray a tunnel under a hill is not hit unless the underground control is down.
                    m_ToolRaycastSystem.typeMask = TypeMask.Net | TypeMask.Terrain;
                    m_ToolRaycastSystem.collisionMask |= CollisionMask.Underground;
                }

                m_ToolRaycastSystem.netLayerMask = Layer.Road | Layer.PublicTransportRoad;
                // A barrier or a noise wall may be drawn along a railway, a metro line or a tram line too (0.12.66); the automatic barriers are by the kind of road and keep to roads.
                SignDef chosen = SignCatalog.ChoiceById(SelectedId)?.Def;
                if (chosen != null && chosen.Group == MarkingTemplates.BarrierGroup && chosen.Id != BarrierAuto.Id)
                    m_ToolRaycastSystem.netLayerMask |= Layer.TrainTrack | Layer.SubwayTrack | Layer.TramTrack;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            long frameStart = Stopwatch.GetTimestamp();
            int collections = GC.CollectionCount(0);
            InputManager input = InputManager.instance;
            ShortcutBarrier.Tick();

            // The mouse and the keyboard are told apart. While a text field has the keyboard the game lets only mouse devices through, so a click
            // in the world still reaches the action; 0.1.0 also refused clicks then, and a search box that kept its focus swallowed every click.
            // Only the keys this tool reads itself (Keyboard.current, which the game's masking does not cover) wait for the field.
            m_PlaceStyleNow = PlaceStyle;
            m_Typing = input.hasInputFieldFocus;
            m_World = input.controlOverWorld;
            bool applied = ApplyPressed();
            m_Click = m_World && applied;
            m_Outcome = null;

            // The lines of text for the log (what the tool sees, why a place is refused) are made on a click only: they are built with string formatting (0.7.1; they were a part of the garbage the
            // game's panel counted), and since 0.12.47 the panel does not show them.
            m_WantText = m_Click;

            m_ArrowX = 0;
            m_ArrowY = 0;
            m_Picking = false;
            if (!m_Typing)
                ReadKeys();

            if (m_World && (cancelAction.WasPressedThisFrame() || secondaryApplyAction.WasPressedThisFrame()))
            {
                // A marking line that was started is cancelled first; the tool is left by the next one.
                if ((Mode == 0 && CancelRun()) || (Mode == 1 && CancelCut()))
                    return inputDeps;
                Mod.log.Info("Tool: left by a right click or Esc.");
                Leave();
                return inputDeps;
            }

            // The areas that clear the game's markings are seen only here: while a marking is chosen, or signs are being edited or deleted.
            if (Mode != 0 || (SignCatalog.ChoiceById(SelectedId)?.Def?.IsMarking ?? false))
                DrawClearAreas();
            // The live picture of the support being edited belongs to the edit mode alone.
            if (Mode != 2)
                EndLiveEdit();
            if (Mode == 1)
                UpdateDelete(m_Click);
            else if (Mode == 2)
                UpdateEdit(m_Click);
            else if ((m_Picking || Mode == StylePick.Mode) && m_World)
                UpdatePick(m_Click);
            else if (Mode == StylePick.Mode)
                UpdatePickIdle();
            else
                UpdatePlace(m_Click);

            if (applied)
                LogClick();
            if (m_WantText)
                Debug = (Debug ?? "") + $"  [mouse over world {(m_World ? "yes" : "NO")}, text field has the keyboard {(m_Typing ? "YES" : "no")}, clicks {m_Clicks}, placed {m_Placed}]";
            // A click may have placed, deleted or changed something: the next frame points again, whatever the store says yet.
            if (m_Click)
                m_PickMemoHave = false;
            // A slow frame says whether the runtime collected garbage in it (0.11.0 had lone 24 to 28 ms frames in which the tool did next to nothing: a collection of the game's 1.8 GB heap is the
            // likely cause, and this tells it).
            double frameMs = (Stopwatch.GetTimestamp() - frameStart) * 1000.0 / Stopwatch.Frequency;
            SlowLog.Add("Tool: a frame (all of OnUpdate)", frameMs, SlowLog.IsSlow(frameMs) ? $"mode {Mode}, {(GC.CollectionCount(0) != collections ? "a garbage collection ran in it" : "no garbage collection in it")}" : null);
            return inputDeps;
        }

        /// <summary>Ctrl is held while placing: a click picks what is under the cursor (UpdatePick) instead of placing.</summary>
        private bool m_Picking;

        /// <summary>The pole the eyedropper took with Shift held, put down together at the next click (CarryPick); none after any other choice.</summary>
        private readonly Carrying m_Carrying = new Carrying();

        /// <summary>Whether this frame builds the lines of text (a click).</summary>
        private bool m_WantText = true;
        private bool m_Typing, m_World, m_Click;
        private string m_Outcome;
        private int m_Clicks;

        /// <summary>Every click of the left button while the tool runs, with what the tool made of it: this is what a "the click does nothing" report is read from.</summary>
        private void LogClick()
        {
            m_Clicks++;
            if (m_Clicks <= 200)
                Mod.log.Info($"Click {m_Clicks}: mode {(Mode == 1 ? "delete" : Mode == 2 ? "edit" : Mode == StylePick.Mode ? "pick" : "place")}, sign {SelectedId ?? "-"}, mouse over world {m_World}, text field focus {m_Typing} -> {m_Outcome ?? (m_World ? "no outcome (no sign chosen?)" : "ignored: the mouse is over the UI")}");
        }

        private int m_DirectClickLogs;

        /// <summary>The tool's click this frame: the game's Apply action, or the left button read directly while Shift, Ctrl or Alt is held (the action is silent then, see ClickRead).</summary>
        private bool ApplyPressed()
        {
            if (applyAction.WasPressedThisFrame())
                return true;
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse == null || keyboard == null)
                return false;
            bool modifier = keyboard[Key.LeftShift].isPressed || keyboard[Key.RightShift].isPressed || keyboard[Key.LeftCtrl].isPressed || keyboard[Key.RightCtrl].isPressed
                || keyboard[Key.LeftAlt].isPressed || keyboard[Key.RightAlt].isPressed;
            if (!ClickRead.Direct(false, mouse.leftButton.wasPressedThisFrame, modifier))
                return false;
            if (m_DirectClickLogs++ < 5)
                Mod.log.Info("Tool: a click with Shift, Ctrl or Alt held: the game's Apply action did not fire, the left mouse button was read directly.");
            return true;
        }

        private void ReadKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            bool ctrl = keyboard[Key.LeftCtrl].isPressed || keyboard[Key.RightCtrl].isPressed, shift = keyboard[Key.LeftShift].isPressed || keyboard[Key.RightShift].isPressed;
            m_Picking = ctrl && Mode == 0;
            if (ctrl && (keyboard[Key.Z].wasPressedThisFrame || keyboard[Key.Y].wasPressedThisFrame))
            {
                UndoOrRedo(keyboard[Key.Y].wasPressedThisFrame || shift);
                return;
            }

            // The eyedropper reads no key of placing: a turn, a flip or another support would be the next sign's before it is picked.
            if (Mode == StylePick.Mode)
                return;

            // [ and ] turn by 15 degrees, Shift 5, Alt 1; held, they repeat as the arrow keys do.
            bool alt = keyboard[Key.LeftAlt].isPressed || keyboard[Key.RightAlt].isPressed;
            int turn = Mathf.Clamp(ArrowPress(keyboard, Key.RightBracket) - ArrowPress(keyboard, Key.LeftBracket), -1, 1);
            if (Mode == 2)
            {
                // In edit mode the keys turn the board being edited on its post (0.12.8); the turn of the next sign to be placed stays as it was.
                m_EditTurn = keyboard[Key.Backslash].wasPressedThisFrame ? 180 : turn * TurnAdjust.StepDegrees(shift, alt);
            }
            else
            {
                bool flip = keyboard[Key.Backslash].wasPressedThisFrame;
                m_Rotation += turn * TurnAdjust.StepRadians(shift, alt);
                if (flip)
                    m_Flip = !m_Flip;
                if (turn != 0 || flip)
                    EditorRevision++;
            }

            if (keyboard[Key.Semicolon].wasPressedThisFrame) CycleSupport(-1);
            if (keyboard[Key.Quote].wasPressedThisFrame) CycleSupport(1);
            // < and > are the comma and the period keys (Shift does not matter: typing < holds it); the numpad + and - stay as a second pair.
            int kerb = Mathf.Clamp(ArrowPress(keyboard, Key.Period) + ArrowPress(keyboard, Key.NumpadPlus) - ArrowPress(keyboard, Key.Comma) - ArrowPress(keyboard, Key.NumpadMinus), -1, 1);
            if (kerb != 0 && Mode == 0)
                SetMargin(KerbMargin.Stepped(Margin, kerb));
            else if (kerb != 0 && Mode == 2)
                StepEditedKerb(kerb);
            if (Mode == 0)
                ReadMidKey(keyboard);
            ReadArrows(keyboard);
            ReadStandKeys(keyboard);
        }

        // ------------------------------------------------------------------ place

        private void UpdatePlace(bool click)
        {
            SignChoice choice = SignCatalog.ChoiceById(SelectedId);
            SignDef def = choice?.Def;
            if (def == null)
            {
                EndAttach();
                HidePreview();
                Debug = "Choose a sign in the panel.";
                SetHint("choose");
                m_Outcome = "ignored: no sign is chosen";
                return;
            }

            // A road marking is paint: it has no support, and it never joins one (PlacementTool.Marking.cs).
            if (def.IsMarking && m_World)
            {
                UpdatePlaceMarking(choice, click);
                return;
            }

            if (def.IsMarking)
            {
                // The stretch chosen for the automatic marking is shown, and drawn again when its settings change, while the cursor is over the panel too (0.12.44).
                if (def.Id == AutoMarking.Id && TickAutoChoice(def, def.Template))
                    ShowAutoChoice(def, def.Template, false);
                HidePreview();
                return;
            }

            // A line of signs that was started (PlacementTool.Linear.cs): its end follows the cursor along the road, whatever the cursor is over. Left when its setting is turned off.
            if (m_RunActive && m_LineSign)
            {
                if (SignLine.On(def, Draft))
                {
                    EndAttach();
                    UpdateLine(def, click);
                    return;
                }

                ResetRun();
            }

            // Over a placed sign or its support the sign joins it (a post: a stack of up to four small boards; a beam or an arm: boards side by side where there is room).
            long lap = Stopwatch.GetTimestamp();
            bool attached = m_World && !m_Carrying.Active && TryUpdateAttach(choice, click);
            SlowLog.Since("Tool: pointing at the signs under the cursor", lap);
            if (attached)
                return;
            EndAttach();

            // A wall support: the sign goes on the wall under the cursor, not by a road (PlacementTool.Wall.cs); a signal's arm: on the arm near the cursor (PlacementTool.Arm.cs).
            SupportDef chosenSupport = SupportCatalog.ById(SelectedSupport);
            if (chosenSupport != null && SupportKinds.OnWall(chosenSupport.Kind))
            {
                UpdatePlaceWall(choice, chosenSupport, click);
                return;
            }

            if (chosenSupport != null && SupportKinds.OnArm(chosenSupport.Kind))
            {
                UpdatePlaceOnArm(choice, chosenSupport, click);
                return;
            }

            if (chosenSupport != null && SupportKinds.OverRoad(chosenSupport.Kind))
            {
                UpdatePlaceOverRoad(choice, chosenSupport, click);
                return;
            }

            lap = Stopwatch.GetTimestamp();
            bool hasRay = GetRaycastResult(out Entity owner, out RaycastHit hit);
            SlowLog.Since("Tool: the game's raycast result", lap);
            if (!hasRay)
            {
                HidePreview();
                Debug = "Move the cursor over a road or the ground.";
                SetHint("road");
                m_Outcome = "ignored: the raycast found neither a road nor the ground under the cursor";
                return;
            }

            Vec3 position;
            float yaw;
            Entity edge = Entity.Null;
            float edgeT = 0f;
            bool valid = true;
            string note;
            // The terrain above a road in a cutting is what the game's ray hits first: the road under the cursor takes its place (PlacementTool.Cutting).
            RoadBelowGround(ref owner, ref hit);
            float3 cursor = hit.m_HitPosition;
            Bezier3 curve = default;
            float width = 0f;
            EdgeSnapResult snap = default;
            lap = Stopwatch.GetTimestamp();

            // What the game keeps about the road under the cursor is read once in a few seconds, not at every frame (EdgeInfo).
            EdgeInfo info = null;
            bool onRoad = false;
            if (RoadReader.TryResolveEdge(EntityManager, owner, hit.m_HitEntity, cursor, out edge))
            {
                info = EdgeInfo.Of(EntityManager, edge);
                if (info.CurveOk)
                {
                    onRoad = true;
                    curve = info.Curve;
                    width = info.Width;
                }
            }

            // A board's size follows the speed of the road it stands at: the character height is chosen by the design speed (GB 5768.2 Table 3), and a draft that says
            // "auto" takes the band of the road under the cursor (the game's own speed limit of the lanes), 40 to 70 km/h when there is no road.
            float kmh = 0f;
            RoadReader.SpeedReport speedReport = default;
            bool hasSpeed = onRoad && info.TrySpeed(EntityManager, out kmh, out speedReport);
            if (onRoad && edge != m_SpeedLogEdge && m_SpeedLogs++ < 50)
            {
                m_SpeedLogEdge = edge;
                Mod.log.Info($"Speed: edge {edge.Index}:{edge.Version}: {speedReport}; used {(hasSpeed ? kmh.ToString("0.#") + " km/h" : "none")}; level {RoadReader.LevelOf(EntityManager, edge)}.");
            }
            else if (!onRoad && m_NoRoadLogs < 30 && math.distance(cursor, m_NoRoadAt) > 8f)
            {
                // What the ray found where there is no road (0.12.94: a cutting or a tunnel the player says does not snap): its owner, what kind of entity that is, and where.
                m_NoRoadLogs++;
                m_NoRoadAt = cursor;
                m_NoRoadSearch = m_NoRoadSearch ?? World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
                Mod.log.Info($"Tool: no road under the cursor at ({cursor.x:0.0}, {cursor.y:0.0}, {cursor.z:0.0}): the ray's owner {owner.Index}:{owner.Version} is {KindOf(owner)}, its hit entity {hit.m_HitEntity.Index}:{hit.m_HitEntity.Version} is {KindOf(hit.m_HitEntity)}; underground view {(requireUnderground ? "on" : "off")}; near: {NearRoads.Describe(EntityManager, m_NoRoadSearch, cursor, 40f)}.");
            }
            // For the extension's probe of the road under the cursor (0.12.29: the probes are the extension's).
            HoverEdge = onRoad ? edge : Entity.Null;

            string spec = GuideTemplates.ResolveSpeed(def, Draft, hasSpeed ? kmh : 0f, hasSpeed, speedReport.Highway);

            // A junction sign (路1, 路3, 路4, 路6, 路7) takes its names, form and lanes from the junction ahead of the place it goes to, which is read for the place the cursor snaps to (kept while the cursor stays
            // near it); what the player typed in the draft is replaced by what was read, and when nothing can be read the draft stands (the editor says why).
            if (onRoad && JunctionFill.CanRead(def.Id) && ReadJunctions)
            {
                EdgeSnapResult early = EdgeSnap.Solve(info.Arc, width, RoadReader.ToVec(cursor), EdgeSnapOptions.Default);
                float readYaw = early.Yaw + (m_Flip ? Mathf.PI : 0f) + m_Rotation;
                spec = ApplyJunction(def, spec, edge, new float3(early.Position.X, early.Position.Y, early.Position.Z), readYaw, "place");
            }
            else if (!onRoad && JunctionFill.CanRead(def.Id))
            {
                SetReadNote(null);
            }

            // The board's own size decides the support and how far out it stands: the inner edge of a wide board must not reach further toward the road than the
            // small signs' (GB 5768.2 4.8.10 asks 25 cm from the carriageway), so a board is moved out by what its structure reaches beyond the reference. When the support is the
            // planner's, the road decides between a post and a gantry (4.8.12 c: three lanes or more in the direction, an exit on the left or on a wide ramp).
            SignInstance instance = SignInstances.Of(def, spec);
            uint supportHash = SupportHashOf(SelectedSupport);
            int lanesSame = 0;
            string lanesNote = "";
            if (onRoad && info.TryLanes(EntityManager, out int lanesForward, out int lanesBackward, out RoadReader.LanesReport lanesReport))
            {
                if (edge != m_LanesLogEdge && m_PlaceLaneLogs++ < 50)
                {
                    m_LanesLogEdge = edge;
                    Mod.log.Info($"Road lanes: edge {edge.Index}:{edge.Version} width {width:0.#} m: {lanesReport}.");
                }

                // The sign is on the right of the edge's direction when Side is positive, and is for the traffic that runs that way. (Solve is called below; the side comes from the cursor.)
                Vec3 cursorAt = RoadReader.ToVec(cursor);
                EdgeSnapResult probe = EdgeSnap.Solve(info.Arc, width, cursorAt, EdgeSnapOptions.Default);
                lanesSame = probe.Side > 0 ? lanesForward : lanesBackward;
                if (m_WantText)
                    lanesNote = $", {lanesSame} lane(s) this way ({lanesForward}+{lanesBackward})";
            }

            GuideSpec parsedSpec = def.Template != null ? def.Template.Parse(spec) : null;
            bool exitLeft = parsedSpec != null && (parsedSpec.Is("xside", "l") || (parsedSpec.Is("arrow", "d") && def.Id.StartsWith("guide.exit", StringComparison.Ordinal)));
            float doublePost = float.NaN;
            SupportDef support;
            if (supportHash == 0u)
            {
                PlannedSupport styled = PlanSupport(instance.Def, new SupportContext { LanesSameDirection = lanesSame, ExitLeftOrWide = exitLeft, Span = GantrySpanAt(info, onRoad, cursor, instance.Def, lanesSame, exitLeft) }, m_Click);
                support = styled.Support;
                m_PlaceStyleNow = styled.Style;
                // A form that only the road asked for is saved with the item (so that it is drawn the same whatever the road later becomes); the board's own is left as 'planned'.
                if (support.Id != SupportPlanner.Auto(instance.Def).Id)
                    supportHash = support.Hash;
            }
            else
            {
                support = SupportCatalog.For(instance.Def, supportHash);
            }

            // An attached support (0.6.0) is fixed to a lamp post or a signal pole of the game's: the post nearest to the cursor is the host, the sign stands at its foot and faces the traffic of the road
            // beside it. Nothing of the board is moved out from the road for it (the post is where it is).
            bool hosted = SupportKinds.Hosted(support.Kind);
            HostCandidate host = default;
            string hostRefusal = null;
            m_NearPosts.Clear();
            if (hosted)
                hostRefusal = FindHost(instance.Def, onRoad, cursor, out host);

            SupportLayout planned = SupportLayouts.Of(instance.Def, support);
            float reachOut = hosted ? 0f : Mathf.Max(0f, planned.InnerReach - kReferenceReach);
            // 0.12.1: the post stands by the real road (Carriageway): on a side with a footway, the board's inner edge half a metre behind the kerb, not at the outer edge of the footway. The span of a
            // gantry is from that post to one as far out on the other side; the arm of a cantilever reaches over the middle of the lanes of its traffic.
            Carriageway carriageway = onRoad ? info.Carriageway(EntityManager) : null;
            int roadSide = onRoad ? EdgeSnap.Solve(info.Arc, width, RoadReader.ToVec(cursor), EdgeSnapOptions.Default).Side : 1;
            float postDistance = onRoad ? carriageway.PostDistance(roadSide, planned.InnerReach, Margin, reachOut) : 0f;
            float reach = 0f;
            if (onRoad && support.Kind == SupportKind.Gantry)
                reach = Mathf.Clamp(carriageway.GantrySpan(roadSide, planned.InnerReach, Margin, reachOut), SupportLayouts.MinSpan, SupportLayouts.MaxSpan);
            else if (onRoad && SupportKinds.OneWayArm(support.Kind) && info.TryLaterals(EntityManager, out List<float> armForward, out List<float> armBackward))
                reach = Carriageway.CantileverArm(roadSide * postDistance, roadSide > 0 ? armForward : armBackward, instance.Def.Width);
            reach = Mathf.Round(reach * 10f) / 10f;
            // On another mod's pole (0.12.51) the reach is its radius, which the clamps fit (to the millimetre).
            if (hosted && hostRefusal == null && host.Radius > 0f)
                reach = Mathf.Round(Mathf.Min(host.Radius, SupportLayouts.MaxHostRadius) * 1000f) / 1000f;
            string stripNote = "", midNote = "";
            bool midActive = false;
            m_StripLo = m_StripHi = float.NaN;
            if (onRoad)
            {
                var options = EdgeSnapOptions.Default;
                options.Side = roadSide > 0 ? SnapSide.Right : SnapSide.Left;
                options.Margin = postDistance - width * 0.5f;
                if (hosted && hostRefusal == null)
                {
                    // The side of the road is the side the post is on, and the sign stands at its foot.
                    var foot = new Vec3(host.X, host.Y, host.Z);
                    snap = EdgeSnap.Solve(info.Arc, width, foot, options);
                    position = foot;
                }
                else
                {
                    // The raycast puts the centre line at m_Position and the surface at m_HitPosition: their difference is the height of the surface.
                    snap = SignLine.PostSnap(info.Arc, width, RoadReader.ToVec(cursor), roadSide, postDistance, hit.m_HitPosition.y - hit.m_Position.y);
                    position = snap.Position;
                    // Over a raised strip between the carriageways a sign on posts stands on it (0.12.24); with the "/" key on it stands in the road where the cursor is.
                    if (TryStrip(info, edge, RoadReader.ToVec(cursor), instance, ref support, ref supportHash, ref reach, ref snap, out stripNote))
                        position = snap.Position;
                    else if (midActive = TryMid(info, RoadReader.ToVec(cursor), support, carriageway, ref snap, out midNote))
                        position = snap.Position;
                }

                if (support.Kind == SupportKind.DoubleCantilever)
                {
                    // The two boards of a double cantilever face the same traffic (Fig. 6 b), so its post stands in that traffic, on an island or between lanes: where the cursor is across
                    // the road (within the road and the margin), not at the road's side. The side of the cursor still decides which traffic the boards face.
                    Vec3 onCentre = curve.Point(snap.T);
                    Vec3 across = snap.Heading.RightXZ();
                    float limit = width * 0.5f + Margin;
                    float lateral = Mathf.Clamp(Vec3.Dot(RoadReader.ToVec(cursor) - onCentre, across), -limit, limit);
                    position = new Vec3(onCentre.X + across.X * lateral, position.Y, onCentre.Z + across.Z * lateral);
                    doublePost = lateral;
                }

                yaw = snap.Yaw;
                edgeT = snap.T;
                valid = !snap.TooFar;
                note = m_WantText ? $"road {edge.Index}:{edge.Version} width {width:0.0} m{(hasSpeed ? $", limit {kmh:0} km/h" : "")}" +
                    (carriageway.Ok ? $", carriageway {carriageway.Left:+0.0;-0.0} to {carriageway.Right:+0.0;-0.0} m{(carriageway.Footway(roadSide) ? " (footway beyond)" : "")}" : ", carriageway not read") +
                    $", t {snap.T:0.000}, side {(snap.Side > 0 ? "right" : "left")}{lanesNote}" + (stripNote.Length > 0 ? stripNote : midNote.Length > 0 ? midNote : $", post {postDistance:0.0} m from the centre line (board reaches {planned.InnerReach:0.0} m toward the road)") : "";
            }
            else
            {
                edge = Entity.Null;
                position = RoadReader.ToVec(cursor);
                yaw = 0f;
                note = "free (no road under the cursor)";
            }

            reach = PlaceSpanFor(support, hosted, onRoad, carriageway, width, roadSide, postDistance, planned.InnerReach, reach);
            float clearance = PlaceClearanceFor(support, hosted, planned.BottomClearance);
            yaw += (m_Flip ? Mathf.PI : 0f) + m_Rotation;

            // No post to fix it to (or a board that is too large for one): the place is refused, and the hint says which.
            if (hostRefusal != null)
            {
                valid = false;
                if (m_WantText)
                    note += "; " + hostRefusal;
            }
            else if (hosted)
            {
                if (m_WantText)
                    note += $"; fixed to the {HostPick.Name(host.Kind)} post #{host.Id}";
            }

            // The board's lower edge on its post or the host, by the arrow keys (0.12.24): down to the ground; its own post grows with it.
            bool byHand = !SupportKinds.Hung(support.Kind);
            if (byHand && m_ArrowY != 0)
            {
                float from0 = float.IsNaN(m_PlaceHeight) ? instance.Def.BottomClearance : m_PlaceHeight;
                float step = m_Shift ? Attach.HeightStepCoarse : Attach.HeightStep;
                m_PlaceHeight = Mathf.Round(Mathf.Clamp(from0 - m_ArrowY * step, Assembly.LowestOnPost, hosted ? Assembly.HighestOnHost : Assembly.HighestOnPost) * 100f) / 100f;
            }

            float placeHeight = byHand ? m_PlaceHeight : float.NaN;
            // Another mod's pole keeps its top and its heads' heights (0.12.51): the board goes to the nearest height that is free, or not at all.
            if (hosted && hostRefusal == null && (host.Highest > 0f || host.Busy != null))
            {
                float wanted = float.IsNaN(placeHeight) ? instance.Def.BottomClearance : placeHeight;
                float fits = HostPick.FitHeight(host, wanted, instance.Def.Height + instance.Def.ExtraTop, Assembly.LowestOnPost, Assembly.HighestOnHost);
                if (float.IsNaN(fits))
                {
                    hostRefusal = "host-space";
                    valid = false;
                }
                else if (Mathf.Abs(fits - wanted) > 1e-3f)
                {
                    placeHeight = Mathf.Round(fits * 100f) / 100f;
                }
            }

            if (m_WantText && !float.IsNaN(placeHeight))
                note += $", lower edge {placeHeight:0.00} m by hand";

            var records = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records : null;
            if (records != null)
            {
                if (!CarryPick.Fits(records.Count, m_Carrying.Active ? m_Carrying.Pole.Count : 1, kMaxItems))
                {
                    valid = false;
                    if (m_WantText)
                        note += "; too many signs";
                }
                else
                {
                    float nearest = NearestSameHere(position, def.Hash);
                    if (nearest < Spacing.MinDistance)
                    {
                        valid = false;
                        if (m_WantText)
                            note += $"; another {def.Code} is {nearest:0.00} m away";
                    }
                }
            }

            if (m_WantText)
            {
                if (!float.IsNaN(doublePost))
                    note += $", post {doublePost:+0.0;-0.0} m across the road from the centre line";
                note += ", " + support.Zh + (supportHash == 0u ? "（按牌面）" : SelectedSupport == null ? "（按路况）" : "") + (support.Kind == SupportKind.Gantry ? $", span {SupportLayouts.Of(instance.Def, support, reach).ArmLength:0.0} m" : "");
                Debug = note + (valid ? "" : " [cannot place]");
            }
            float shownHeight = float.IsNaN(placeHeight) ? instance.Def.BottomClearance : placeHeight;
            // With the sign's line setting on the click starts a line (0.12.69), so the hint says that.
            bool startsLine = !midActive && StartsLine(def, spec, support, onRoad, hosted, stripNote);
            // The line setting is on and the cursor is over a strip between the carriageways: no line is drawn there, and it is not a single sign either (0.12.70).
            bool lineOnStrip = onRoad && !hosted && stripNote.Length > 0 && SignLine.On(def, spec);
            if (lineOnStrip)
                SetHint("line-strip", 0, 0f, 0f, 0f, false, false, support.Zh, true);
            else if (startsLine)
                SetHint("line-start", 0, 0f, 0f, 0f, false, false, support.Zh, !valid);
            else if (hosted)
                SetHint(hostRefusal ?? "host", 0, HostPick.Radius, hostRefusal == "host-large" ? SupportPlanner.AreaOf(instance.Def) : 0f, shownHeight, false, false, support.Zh, !valid, host: HostPick.Name(host.Kind));
            else
                SetHint("place", 0, 0f, 0f, byHand ? shownHeight : 0f, false, SupportKinds.Hung(support.Kind), support.Zh, !valid, midKey: MidRoad.Takes(support.Kind), midOn: Mid, midHere: midActive);
            // The two parts that are the game's (a preview made of Unity objects, the overlay's lines) timed apart (0.12.65), to tell which the frame's time is: the pure calls of the frame are 12 us.
            long stage = Stopwatch.GetTimestamp();
            ShowPreview(instance, supportHash, reach, position, yaw, placeHeight, clearance);
            SlowLog.Since("Tool: placing: the preview", stage);
            if (m_WantText && m_GroundNote.Length > 0)
                Debug = (Debug ?? "") + "; " + m_GroundNote;
            stage = Stopwatch.GetTimestamp();
            DrawOverlay(onRoad, info?.Arc, width, snap, position, yaw, valid, def);
            SlowLog.Since("Tool: placing: the overlay", stage);
            SlowLog.Since("Tool: placing (road, speed, lanes, snap, preview, overlay)", lap);

            if (click)
            {
                if (lineOnStrip)
                {
                    m_Outcome = "refused: a line of signs is not drawn on a strip between the carriageways";
                }
                else if (valid && startsLine)
                {
                    StartLine(def, spec, supportHash, planned, reachOut, placeHeight, edge, snap, position, hit.m_HitPosition.y - hit.m_Position.y, note);
                }
                else if (valid)
                {
                    LogSpan(support);
                    Entity placed = Place(def, SignLine.Stripped(def, spec), supportHash, reach, position, yaw, edge, edgeT, note, (byte)(m_SpanByHand ? TsItem.FlagReachByHand : 0), float.IsNaN(placeHeight) ? 0f : placeHeight, clearance);
                    int pole = CarriedHere(def) ? PlaceCarried(placed) : 0;
                    m_Outcome = "placed: " + note + (pole > 1 ? $"; with its pole of {pole} board(s)" : "");
                }
                else
                {
                    m_Outcome = "refused: " + note;
                }
            }
        }

        /// <summary>The lower edge the arrow keys gave the next board on its post (0.12.24, AssemblyBoard.OffsetX); NaN: the board's own. Kept from sign to sign until another entry is chosen.</summary>
        private float m_PlaceHeight = float.NaN;

        private Entity Place(SignDef def, string spec, uint supportHash, float reach, Vec3 position, float yaw, Entity edge, float edgeT, string note, byte flags = 0, float height = 0f, float clearance = 0f, uint batchId = 0u)
        {
            var item = new TsItem
            {
                BatchId = batchId,
                TypeHash = def.Hash,
                Edge = edge,
                EdgeT = edgeT,
                Position = new float3(position.X, position.Y, position.Z),
                Yaw = yaw,
                Flags = (byte)(flags | TsItem.FlagBaseReach),
                SupportHash = supportHash,
                Reach = reach,
                BaseReach = reach,
                OffsetX = height,
                Clearance = SupportLayouts.ClampClearance(clearance)
            };
            item.SetSpec(def.Template != null ? spec : "");
            // A board on an arm or a beam leans forward (0.12.55); the support is painted as chosen (0.12.57).
            if (!def.IsMarking && BoardTilt.Leans(SupportCatalog.For(def, supportHash)))
                item.SetTilt(PlaceTilt);
            if (!def.IsMarking)
                item.Style = m_PlaceStyleNow.Packed;
            Entity entity = NewItem(item);

            m_Placed++;
            int known = ChunkSystem.Instance != null ? ChunkSystem.Instance.Records.Count : -1;
            if (m_Placed <= 60 && (flags & TsItem.FlagAuto) == 0)
                Mod.log.Info($"Placed {def.Id} at {position}, yaw {yaw * Mathf.Rad2Deg:0.#} deg ({known} item(s) were known to the renderer), settings '{item.SpecText}': {note}.");
            return entity;
        }

        // ------------------------------------------------------------------ pointing at a sign (delete and edit)

        /// <summary>The sign under the cursor, found by our own ray against the pick shapes of each sign (the board, each post, the foot, the arm).</summary>
        private bool FindUnderCursor(out Entity best, out ItemRecord bestRecord) => FindUnderCursor(out best, out bestRecord, out _, out _);

        private bool FindUnderCursor(out Entity best, out ItemRecord bestRecord, out bool onBoard) => FindUnderCursor(out best, out bestRecord, out onBoard, out _);

        /// <summary>
        /// As above. Of several boards on one support the one that was hit is the sign when a board was hit (onBoard), else (a post, the foot, the arm) the first of them: the support was
        /// pointed at, not one of its boards. shape: the name of what was hit ("board", "post", "foot", "arm", "plate").
        /// </summary>
        private bool FindUnderCursor(out Entity best, out ItemRecord bestRecord, out bool onBoard, out string shape)
        {

            best = Entity.Null;
            bestRecord = default;
            onBoard = true;
            shape = null;
            Camera camera = m_Camera.activeCamera;
            if (camera == null || ChunkSystem.Instance == null)
                return false;

            Line3.Segment ray = ToolRaycastSystem.CalculateRaycastLine(camera);
            Vec3 from = RoadReader.ToVec(ray.a), to = RoadReader.ToVec(ray.b);

            // Only what is within reach of the cursor can be pointed at: a sign 600 m along the ray is a speck (and the parts of every sign near a ray 4 km long, all the way to the horizon, would be built
            // to find that out).
            Vec3 along = to - from;
            float length = along.Length;
            if (length > kPickRange)
                to = from + along * (kPickRange / length);

            // The same ray over the same signs is the same answer (the camera and the mouse did not move, nothing was placed, moved or changed): the answer of the last frame stands.
            bool paintPassed = StylePick.PassesPaint(Mode, m_Picking);
            var memoKey = (from.X, from.Y, from.Z, to.X, to.Y, to.Z, ChunkSystem.Instance.Store.Changes, StylePick.PointingKey(Mode, m_Picking));
            if (m_PickMemoHave && m_PickMemoKey.Equals(memoKey) && (m_PickMemoBest == Entity.Null || EntityManager.Exists(m_PickMemoBest)))
            {
                best = m_PickMemoBest;
                bestRecord = m_PickMemoRecord;
                onBoard = m_PickMemoOnBoard;
                shape = m_PickMemoShape;
                return best != Entity.Null;
            }

            float bestDistance = float.MaxValue;
            var singles = new List<KeyValuePair<Entity, ItemRecord>>();
            var groups = new Dictionary<uint, List<KeyValuePair<Entity, ItemRecord>>>();
            m_Candidates.Clear();
            ChunkSystem.Instance.Store.ItemsNearSegment(from, to, kPickRadius, m_Candidates);
            ItemGroups.Split(m_Candidates, singles, groups);
            foreach (KeyValuePair<Entity, ItemRecord> pair in singles)
            {
                if (Picking.DistanceToSegment(pair.Value.Position, from, to) > kPickRadius)
                    continue;
                // Placing (Mode 0) looks for a support to join; paint on the road is none, and its pick box covers the lanes under it (0.12.12: a sign put over a marked road joined the marking).
                if (paintPassed && (SignCatalog.ByHash(pair.Value.TypeHash)?.IsMarking ?? false))
                    continue;
                // One board on a support of its own; over a lane it stands where its lane is (PartsOfRecord).
                SignParts single = ChunkBuilder.PartsOfRecord(pair.Value);
                if (single != null && Picking.Pick(single.Picks, pair.Value.Position, pair.Value.Yaw, from, to, out float distance, out PickShape singleShape) && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pair.Key;
                    bestRecord = pair.Value;
                    onBoard = true;
                    shape = singleShape?.Name;
                }
            }

            foreach (KeyValuePair<uint, List<KeyValuePair<Entity, ItemRecord>>> pair in groups)
            {
                List<KeyValuePair<Entity, ItemRecord>> members = ItemGroups.Sorted(pair.Value, Comparer<Entity>.Default);
                if (Picking.DistanceToSegment(members[0].Value.Position, from, to) > kPickRadius)
                    continue;
                var records = new List<ItemRecord>(members.Count);
                foreach (KeyValuePair<Entity, ItemRecord> member in members)
                    records.Add(member.Value);
                SignParts parts = ChunkBuilder.PartsOfGroup(records);
                if (parts == null)
                    continue;
                ItemRecord lead = records[0];
                if (Picking.Pick(parts.Picks, lead.Position, lead.Yaw, from, to, out float distance, out PickShape groupShape) && distance < bestDistance)
                {
                    bestDistance = distance;
                    bool board = groupShape.Name == "board" && groupShape.Member >= 0 && groupShape.Member < members.Count;
                    best = board ? members[groupShape.Member].Key : members[0].Key;
                    bestRecord = board ? members[groupShape.Member].Value : members[0].Value;
                    onBoard = board;
                    shape = groupShape.Name;
                }
            }

            // The area that clears the game's markings lies under the lines over the whole road, and a line is a hand's breadth wide: a click that missed the line took the area (the owner's log of 4 Nov:
            // four of five clicks in the edit mode and two in the delete mode, the second two brought the game's lines back over the owner's). A line within kLineGrow of the ray is the answer then.
            if (best != Entity.Null && SignCatalog.ByHash(bestRecord.TypeHash)?.Id == VanillaClear.Id)
            {
                float lineDistance = float.MaxValue;
                foreach (KeyValuePair<Entity, ItemRecord> pair in singles)
                {
                    SignDef marking = SignCatalog.ByHash(pair.Value.TypeHash);
                    if (marking == null || !marking.IsMarking || marking.Id == VanillaClear.Id || Picking.DistanceToSegment(pair.Value.Position, from, to) > kPickRadius)
                        continue;
                    SignParts single = ChunkBuilder.PartsOfRecord(pair.Value);
                    if (single != null && Picking.Pick(single.Picks, pair.Value.Position, pair.Value.Yaw, from, to, out float distance, out PickShape lineShape, kLineGrow) && distance < lineDistance)
                    {
                        lineDistance = distance;
                        best = pair.Key;
                        bestRecord = pair.Value;
                        shape = lineShape?.Name;
                    }
                }
            }

            // The renderer's store is a frame behind the entities: a sign deleted by the click before is still in it (0.12.1 threw at a second quick click in delete mode, reading the TsItem of an
            // entity that was gone). Such a hit is no hit, and it is not remembered.
            if (best != Entity.Null && (!EntityManager.Exists(best) || !EntityManager.HasComponent<TsItem>(best)))
            {
                best = Entity.Null;
                bestRecord = default;
                m_PickMemoHave = false;
                return false;
            }

            m_PickMemoHave = true;
            m_PickMemoKey = memoKey;
            m_PickMemoBest = best;
            m_PickMemoRecord = bestRecord;
            m_PickMemoOnBoard = onBoard;
            m_PickMemoShape = shape;
            return best != Entity.Null;
        }

        // The last answer of FindUnderCursor, with the ray and the state of the items it was made for.
        private string m_PickMemoShape;
        private bool m_PickMemoHave, m_PickMemoOnBoard;
        private (float, float, float, float, float, float, int, int) m_PickMemoKey;
        private Entity m_PickMemoBest;
        private ItemRecord m_PickMemoRecord;

        private void UpdateDelete(bool click)
        {
            EndAttach();
            HidePreview();
            SetHint("delete");
            if (m_Camera.activeCamera == null || ChunkSystem.Instance == null)
            {
                Debug = "No camera.";
                m_Outcome = "ignored: no camera";
                return;
            }

            bool found = FindUnderCursor(out Entity best, out ItemRecord bestRecord, out bool onBoard);
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            // Shift over a line along a road, or a stretch that was started: only a stretch of it is deleted (0.12.73).
            if (UpdateCut(buffer, click, found, best))
                return;
            if (!found)
            {
                Debug = "Delete: point at a sign.";
                if (click)
                    m_Outcome = "ignored: no sign under the cursor";
                return;
            }

            SignDef hovered = SignCatalog.ByHash(bestRecord.TypeHash);
            if (hovered == null || !EntityManager.Exists(best) || !EntityManager.HasComponent<TsItem>(best))
            {
                Debug = "Delete: point at a sign.";
                if (click)
                    m_Outcome = "ignored: the sign under the cursor is gone";
                return;
            }

            bool grouped = bestRecord.Group != 0u;
            if (m_WantText)
                Debug = !grouped ? $"Delete {hovered.Zh} ({hovered.Code}), click to remove."
                : onBoard ? $"Delete the board {hovered.Zh} ({hovered.Code}) from its support, click to remove it (the others stay)."
                : "Delete the support and all its boards, click to remove them (point at a board to remove only that one).";
            var at = new float3(bestRecord.Position.X, bestRecord.Position.Y + 0.1f, bestRecord.Position.Z);
            buffer.DrawCircle(new UnityEngine.Color(1f, 0.15f, 0.1f, 0.9f), at, 1.4f);

            if (click)
            {
                TsItem hit = EntityManager.GetComponentData<TsItem>(best);
                List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(best, hit);
                if (grouped && onBoard)
                {
                    if (best == EditEntity)
                        ClearEdit();
                    DestroyItem(best);

                    // A support with one board left is a sign on a support of its own again.
                    var rest = new List<Entity>();
                    foreach (KeyValuePair<Entity, ItemRecord> member in members)
                    {
                        if (member.Key != best)
                            rest.Add(member.Key);
                    }

                    if (rest.Count == 1 && EntityManager.Exists(rest[0]) && EntityManager.HasComponent<TsItem>(rest[0]))
                    {
                        TsItem last = EntityManager.GetComponentData<TsItem>(rest[0]);
                        last.Group = 0u;
                        last.Slot = 0;
                        WriteItem(rest[0], last);
                    }

                    RefitReach(rest, "a board was deleted");

                    m_Outcome = $"deleted the board {hovered.Id} from a support of {members.Count}";
                }
                else
                {
                    // A sign of its own, or the support itself: it goes with all its boards.
                    foreach (KeyValuePair<Entity, ItemRecord> member in members)
                    {
                        if (member.Key == EditEntity)
                            ClearEdit();
                        if (EntityManager.Exists(member.Key))
                            DestroyItem(member.Key);
                    }

                    m_Outcome = members.Count > 1 ? $"deleted a support with {members.Count} boards" : $"deleted {hovered.Id}";
                }
            }
        }

        private void UpdateEdit(bool click)
        {
            EndAttach();
            ServeFacadeFit();
            if (EditEntity != Entity.Null && !TryReadEdit(out _))
                ClearEdit();

            // The arrow keys move the board that is being edited; the line of help says which keys that is (the beam's: left and right; a post's: up and down). [ ] and \ turn it on its post.
            MoveEditedBoard();
            TurnEditedByKeys();
            // The support is drawn live, and left out of its tile (PlacementTool.Live.cs), whatever the keys and the editor did to it this frame.
            UpdateLiveEdit();
            if (TryReadEdit(out TsItem selected))
            {
                List<KeyValuePair<Entity, ItemRecord>> group = MembersOf(EditEntity, selected);
                AssemblyPlan plan = ItemGroups.PlanOf(RecordsOf(group), out List<AssemblyBoard> ordered);
                int own = group.FindIndex(pair => pair.Key == EditEntity);
                Placement placed = plan?.Placed.Find(p => p.Board.Member == own);
                // Up and down a post (0.12.24): X is where the board is.
                bool byHeight = placed != null && (placed.OnPost || (!plan.Hung && !SupportKinds.OnWall(plan.Support.Kind)));
                SetHint("move", group.Count, 0f, 0f, byHeight ? placed.Bottom : 0f, false, plan != null && plan.Hung, grew: byHeight && OwnPost(plan.Support.Kind), turns: EditedBoardTurns(group, plan, ordered), kerb: plan != null && KerbMargin.Applies(plan.Support.Kind));
            }
            else
            {
                SetHint("edit");
            }

            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();

            // The sign being edited keeps a ring round it.
            if (TryReadEdit(out TsItem edited))
                buffer.DrawCircle(new UnityEngine.Color(0.3f, 1f, 0.5f, 0.95f), new float3(edited.Position.x, edited.Position.y + 0.1f, edited.Position.z), 1.8f);

            if (m_Camera.activeCamera == null || ChunkSystem.Instance == null)
            {
                Debug = "No camera.";
                m_Outcome = "ignored: no camera";
                return;
            }

            bool found = FindUnderCursor(out Entity best, out ItemRecord record, out bool onBoard);
            if (!found)
            {
                Debug = EditEntity == Entity.Null ? "Edit: point at a sign and click it." : "Edit: the panel shows the green sign; point at another to change it.";
                if (click)
                    m_Outcome = "ignored: no sign under the cursor";
                return;
            }

            SignDef hovered = SignCatalog.ByHash(record.TypeHash);
            if (m_WantText)
                Debug = $"Edit {hovered.Zh} ({hovered.Code}), click to select.";
            if (best != EditEntity)
                buffer.DrawCircle(new UnityEngine.Color(0.35f, 0.8f, 1f, 0.9f), new float3(record.Position.X, record.Position.Y + 0.1f, record.Position.Z), 1.4f);

            if (click)
            {
                if (best != EditEntity)
                {
                    EditEntity = best;
                    EditorTarget++;
                    EditorRevision++;
                }

                m_Outcome = $"selected {hovered.Id} for editing, settings '{record.Spec}'";
            }
        }

        // ------------------------------------------------------------------ preview and overlay

        private SignParts m_HeightParts;
        private string m_HeightPartsKey;

        private void ShowPreview(SignInstance instance, uint supportHash, float reach, Vec3 position, float yaw, float height = float.NaN, float clearance = 0f)
        {
            SupportDef support = SupportCatalog.For(instance.Def, supportHash);
            // The board leans as it will when placed (0.12.56).
            float tilt = !instance.Def.IsMarking && BoardTilt.Leans(support) ? BoardTilt.Radians(PlaceTilt) : 0f;
            string built = instance.Key + "|" + support.Hash + "|" + Mathf.RoundToInt(reach * 10f) + (float.IsNaN(height) ? "" : "|h" + Mathf.RoundToInt(height * 100f)) + (clearance > 0f ? "|c" + Mathf.RoundToInt(clearance * 100f) : "") + "|t" + PlaceTilt + "|s" + m_PlaceStyleNow.Packed;
            SignParts parts = null;
            // A pole the eyedropper took with Shift held is shown with all its boards, as the placed one will be drawn (PartsOfGroup).
            if (CarriedHere(instance.Def))
            {
                built += "|pole" + m_Carrying.Pole.Key + "|t" + PlaceTilt;
                parts = ChunkBuilder.PartsOfGroup(CarryPick.Records(m_Carrying.Pole, instance.Spec, tilt, supportHash, reach, m_PlaceStyleNow.Packed, position, yaw));
            }

            bool pole = parts != null;
            if (!pole && float.IsNaN(height) && tilt == 0f && !(clearance > 0f))
            {
                parts = ChunkBuilder.PartsOf(instance, supportHash, reach, SupportDetail.Full, instance.Def.IsMarking ? (byte)0 : m_PlaceStyleNow.Packed);
            }
            else if (!pole)
            {
                // At a height of its own (the arrow keys), or leaning, the board is a support with one board put there, as the placed item will be drawn; made again when that changes.
                if (m_HeightPartsKey != built)
                {
                    m_HeightParts = SignBuilder.BuildAssembly(new[] { new AssemblyBoard { Instance = instance, OffsetX = float.IsNaN(height) ? 0f : height, Tilt = tilt, Clearance = clearance } }, support, reach, SupportDetail.Full, m_PlaceStyleNow);
                    m_HeightPartsKey = built;
                }

                parts = m_HeightParts;
            }

            // A support on posts is shown standing on the ground under them (0.12.5): its ground is read again when it moves half a metre or turns. 0.12.90 (the owner: "做预览重建的优化"; the log of
            // 4 Nov had the preview made again about nine times a second while the cursor moved, 7.9 ms each, 84 % of the tool's load): the preview is made again only when its shape changed; for a move
            // it is kept while the offsets of the feet are the same, and else only the meshes that move with the ground are replaced.
            string shape = built + (parts.Provisional ? "|wait" : "");
            string place = parts.Feet.Count > 0 ? $"|{Mathf.Round(position.X * 2f)}|{Mathf.Round(position.Y * 2f)}|{Mathf.Round(position.Z * 2f)}|{Mathf.Round(yaw * 50f)}" : "";
            if (!pole)
                PreviewFaceKeys = new[] { ChunkBuilder.FacePrefix + instance.Key };
            if (m_Preview == null || m_PreviewKey != shape)
            {
                long lap = Stopwatch.GetTimestamp();
                DestroyPreview();
                BuildPreview(parts, position, yaw, reach);
                m_PreviewKey = shape;
                m_PreviewPlace = place;
                SlowLog.Since("Tool: the preview: made again (a new shape)", lap);
            }
            else if (place != m_PreviewPlace)
            {
                MoveGround(position, yaw, reach);
                m_PreviewPlace = place;
            }

            m_Preview.SetActive(true);
            m_Preview.transform.SetPositionAndRotation(new Vector3(position.X, position.Y, position.Z), Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f));
        }

        private Game.Net.SearchSystem m_GroundNetSearch;
        private Game.Prefabs.PrefabSystem m_GroundPrefabs;
        private Game.Simulation.TerrainSystem m_GroundTerrain;

        /// <summary>The ground under the posts of a support that stands where the preview is (null when it has no post, or the ground could not be read: nothing moves then).</summary>
        private GroundProfile GroundUnder(SignParts parts, Vec3 position, float yaw, float reach)
        {
            m_GroundNote = "";
            if (parts.Feet.Count == 0)
                return null;
            try
            {
                m_GroundNetSearch = m_GroundNetSearch ?? World.GetOrCreateSystemManaged<Game.Net.SearchSystem>();
                m_GroundPrefabs = m_GroundPrefabs ?? World.GetOrCreateSystemManaged<Game.Prefabs.PrefabSystem>();
                m_GroundTerrain = m_GroundTerrain ?? World.GetOrCreateSystemManaged<Game.Simulation.TerrainSystem>();
                // Timed apart (0.12.75): the cut of the ground under the posts is made here on the main thread at every half metre the preview moves, and was inside the preview's 25 to 34 ms.
                long groundStart = Stopwatch.GetTimestamp();
                GroundProfile profile = AreaFitSystem.ComputeGround(EntityManager, m_GroundNetSearch, m_GroundPrefabs, m_GroundTerrain, position, yaw, reach, out string note);
                double groundMs = (Stopwatch.GetTimestamp() - groundStart) * 1000.0 / Stopwatch.Frequency;
                SlowLog.Add("Tool: the preview's ground under the posts", groundMs, SlowLog.IsSlow(groundMs) ? note : null);
                m_GroundNote = $"posts on the ground ({note})";
                return profile;
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Tool: the preview's ground failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The metal, the concrete and the painted solids of a support with its feet moved by the offsets (PostGround.Offsets): the plain meshes when nothing moves. False when the grounding failed and the
        /// plain meshes are what is returned.
        /// </summary>
        private static bool GroundedFrom(SignParts parts, float[] top, float[] bottom, out MeshBuilder metal, out MeshBuilder concrete, out MeshBuilder[] solids)
        {
            metal = parts.Metal;
            concrete = parts.Concrete;
            solids = parts.Solids;
            if (parts.Feet.Count == 0 || !PostGround.Moves(top, bottom))
                return true;
            try
            {
                parts.FootTags(out int[] metalTags, out int[] concreteTags, out int[][] solidTags);
                var movedMetal = new MeshBuilder();
                PostGround.Append(movedMetal, parts.Metal, metalTags, top, bottom, Vec3.Zero, 0f);
                var movedConcrete = new MeshBuilder();
                PostGround.Append(movedConcrete, parts.Concrete, concreteTags, top, bottom, Vec3.Zero, 0f);
                var movedSolids = (MeshBuilder[])parts.Solids.Clone();
                for (int colour = 0; colour < movedSolids.Length; colour++)
                {
                    if (solidTags[colour] == null)
                        continue;
                    movedSolids[colour] = new MeshBuilder();
                    PostGround.Append(movedSolids[colour], parts.Solids[colour], solidTags[colour], top, bottom, Vec3.Zero, 0f, parts.Feet);
                }

                metal = movedMetal;
                concrete = movedConcrete;
                solids = movedSolids;
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Tool: the grounding of a support failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>What the last preview's posts stood on (for the line of what the tool sees).</summary>
        private string m_GroundNote = "";

        // What the preview of ShowPreview stands on, kept so that a move of the cursor can be answered without making it again: where it was last read (ShowPreview's place), the parts its meshes were
        // made from, the offsets of the feet they were made with (null when the grounding failed), and the objects whose meshes move with the ground.
        private string m_PreviewPlace;
        private SignParts m_PreviewParts;
        private float[] m_PreviewTop, m_PreviewBottom;
        private GameObject m_PreviewMetal, m_PreviewConcrete;
        private GameObject[] m_PreviewSolids;

        /// <summary>The renderers of the preview where it will stand: the metal, the concrete and the paint with its posts on the ground under them, and the face of each board with its own picture.</summary>
        private void BuildPreview(SignParts parts, Vec3 position, float yaw, float reach)
        {
            PostGround.Offsets(parts, GroundUnder(parts, position, yaw, reach), out float[] top, out float[] bottom);
            long lap = Stopwatch.GetTimestamp();
            bool grounded = GroundedFrom(parts, top, bottom, out MeshBuilder metalMesh, out MeshBuilder concreteMesh, out MeshBuilder[] solids);
            SlowLog.Since("Tool: the preview: the posts moved to the ground (meshes)", lap);
            lap = Stopwatch.GetTimestamp();
            BuildPreview(parts, metalMesh, concreteMesh, solids);
            SlowLog.Since("Tool: the preview: the renderers made", lap);
            m_PreviewParts = parts;
            m_PreviewTop = grounded ? top : null;
            m_PreviewBottom = bottom;
        }

        /// <summary>
        /// The cursor moved half a metre or turned and the preview's shape is the same (0.12.90): the ground under the posts is read again, and the preview stays as it is while the offsets of its feet are the
        /// same to the eye (PostGround.Same); else only the meshes that move with the ground (the metal, the concrete, the paint of the posts' roots) are replaced, in the objects they are in: the faces, the
        /// lit dots and the other solids do not depend on the ground and are not made again.
        /// </summary>
        private void MoveGround(Vec3 position, float yaw, float reach)
        {
            long lap = Stopwatch.GetTimestamp();
            SignParts parts = m_PreviewParts;
            PostGround.Offsets(parts, GroundUnder(parts, position, yaw, reach), out float[] top, out float[] bottom);
            if (PostGround.Same(m_PreviewTop, m_PreviewBottom, top, bottom))
            {
                SlowLog.Since("Tool: the preview: the ground is the same (kept)", lap);
                return;
            }

            // Cleared first: whatever happens below, the next move reads and judges again.
            m_PreviewTop = null;
            bool grounded = GroundedFrom(parts, top, bottom, out MeshBuilder metal, out MeshBuilder concrete, out MeshBuilder[] solids);
            parts.FootTags(out _, out _, out int[][] solidTags);
            if (m_PreviewMetal != null)
                ReplaceMesh(m_PreviewMetal, metal, "CDLTS preview metal");
            if (m_PreviewConcrete != null)
                ReplaceMesh(m_PreviewConcrete, concrete, "CDLTS preview concrete");
            for (int colour = 0; colour < solids.Length; colour++)
            {
                if (m_PreviewSolids[colour] != null && solidTags[colour] != null)
                    ReplaceMesh(m_PreviewSolids[colour], solids[colour], "CDLTS preview solid " + colour);
            }

            m_PreviewTop = grounded ? top : null;
            m_PreviewBottom = bottom;
            SlowLog.Since("Tool: the preview: the ground changed (meshes replaced)", lap);
        }

        /// <summary>The mesh of a preview object replaced by one made from the data; the old one is destroyed, and a failure leaves the old one in place.</summary>
        private static void ReplaceMesh(GameObject child, MeshBuilder data, string meshName)
        {
            var filter = child.GetComponent<MeshFilter>();
            Mesh old = filter.sharedMesh, made = null;
            try
            {
                made = MeshUpload.ToMesh(data, meshName);
                filter.sharedMesh = made;
            }
            catch (Exception error)
            {
                MeshUpload.CleanupFailed(made, null, error);
                throw;
            }

            if (old != null)
                UnityEngine.Object.Destroy(old);
        }

        /// <summary>The renderers of the preview: the metal, the concrete and the face of each board with its own picture.</summary>
        private void BuildPreview(SignParts parts) => BuildPreview(parts, parts.Metal, parts.Concrete, parts.Solids);

        /// <summary>With shadows (the live picture of the support being edited: the tile it left cast them) or without (a preview to place).</summary>
        private void BuildPreview(SignParts parts, MeshBuilder metalMesh, MeshBuilder concreteMesh, MeshBuilder[] solids, bool shadows = false)
        {
            var keys = new List<string>();
            m_Preview = CreatePreviewRoot();
            Material metal = TsMaterials.Solid(parts.MetalColour);
            Material concrete = TsMaterials.Concrete();
            if (metal != null)
                m_PreviewMetal = AddPreviewRenderer("metal", metalMesh, "CDLTS preview metal", metal, 0f, shadows);
            if (concrete != null && concreteMesh.VertexCount > 0)
                m_PreviewConcrete = AddPreviewRenderer("concrete", concreteMesh, "CDLTS preview concrete", concrete, 0f, shadows);
            foreach (FacePart part in parts.Faces)
            {
                string faceKey = ChunkBuilder.FacePrefix + part.Instance.Key;
                keys.Add(faceKey);
                Material face = TsMaterials.Face(faceKey, true);
                if (face != null)
                    AddPreviewRenderer("face", part.Mesh, "CDLTS preview face", face, 0f, shadows);
            }

            // The lit dots of an LED screen (variable-message sign), in front of its face.
            for (int colour = 1; colour < parts.Leds.Length; colour++)
            {
                MeshBuilder dots = parts.Leds[colour];
                Material led = dots != null && dots.VertexCount > 0 ? TsMaterials.LedMaterial(colour) : null;
                if (led != null)
                    AddPreviewRenderer("led", dots, "CDLTS preview led " + colour, led);
            }

            // The painted solids: the body of an electric box, black backs, the paint on the posts' root.
            m_PreviewSolids = new GameObject[solids.Length];
            for (int colour = 0; colour < solids.Length; colour++)
            {
                MeshBuilder solid = solids[colour];
                Material paint = solid != null && solid.VertexCount > 0 ? TsMaterials.Solid((SolidColour)colour) : null;
                if (paint != null)
                    m_PreviewSolids[colour] = AddPreviewRenderer("solid", solid, "CDLTS preview solid " + colour, paint, 0f, shadows && !SolidGlass.Is((SolidColour)colour));
            }

            PreviewFaceKeys = keys.ToArray();
        }

        private static GameObject CreatePreviewRoot()
        {
            GameObject root = null;
            try
            {
                root = new GameObject("CDLTS preview");
                root.SetActive(false);
                root.hideFlags = HideFlags.DontSave;
                root.layer = 0;
                return root;
            }
            catch (Exception error)
            {
                MeshUpload.CleanupFailed(null, root, error);
                throw;
            }
        }

        private GameObject NewChild(string name)
        {
            GameObject child = null;
            try
            {
                child = new GameObject(name);
                child.hideFlags = HideFlags.DontSave;
                child.layer = 0;
                child.transform.SetParent(m_Preview.transform, false);
                return child;
            }
            catch (Exception error)
            {
                MeshUpload.CleanupFailed(null, child, error);
                throw;
            }
        }

        private GameObject AddPreviewRenderer(string childName, MeshBuilder data, string meshName, Material material, float raise = 0f, bool shadows = false)
        {
            Mesh mesh = null;
            GameObject child = null;
            try
            {
                mesh = MeshUpload.ToMesh(data, meshName);
                child = NewChild(childName);
                if (raise != 0f)
                    child.transform.localPosition = new Vector3(0f, raise, 0f);
                MeshUpload.AddRenderer(child, mesh, material, shadows);
                return child;
            }
            catch (Exception error)
            {
                MeshUpload.CleanupFailed(mesh, child, error);
                throw;
            }
        }

        private void HidePreview()
        {
            if (m_Preview != null)
                m_Preview.SetActive(false);
        }

        private void DestroyPreview()
        {
            MeshUpload.RetryCleanup();
            if (m_Preview == null)
                return;
            foreach (MeshFilter filter in m_Preview.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                    UnityEngine.Object.Destroy(filter.sharedMesh);
            }

            UnityEngine.Object.Destroy(m_Preview);
            m_Preview = null;
            m_PreviewKey = null;
            m_PreviewPlace = null;
            m_PreviewParts = null;
            m_PreviewTop = m_PreviewBottom = null;
            m_PreviewMetal = m_PreviewConcrete = null;
            m_PreviewSolids = null;
            PreviewFaceKeys = null;
        }

        private void DrawOverlay(bool onRoad, ArcTable arc, float width, EdgeSnapResult snap, Vec3 position, float yaw, bool valid, SignDef def)
        {
            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();

            var colour = valid ? new UnityEngine.Color(0.2f, 1f, 0.35f, 0.9f) : new UnityEngine.Color(1f, 0.2f, 0.15f, 0.9f);
            var at = new float3(position.X, position.Y + 0.1f, position.Z);
            buffer.DrawCircle(colour, at, 0.9f);
            var look = new float3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * 1.6f;
            buffer.DrawLine(colour, new Line3.Segment(at, at + look), 0.12f);

            // The other posts an attached sign could have been fixed to (the chosen one has the ring above).
            var other = new UnityEngine.Color(1f, 0.85f, 0.1f, 0.7f);
            foreach (HostCandidate post in m_NearPosts)
            {
                if (Mathf.Abs(post.X - position.X) < 0.05f && Mathf.Abs(post.Z - position.Z) < 0.05f)
                    continue;
                buffer.DrawCircle(other, new float3(post.X, post.Y + 0.1f, post.Z), 0.45f);
            }

            if (!onRoad)
                return;

            // The two edges of the road's cross-section, 30 m either side of the cursor: to see whether the width the game reports is the one it draws. And the kerbs of the strip the sign stands
            // on (0.12.24), 15 m either side.
            float s = arc.SAt(snap.T);
            var edgeColour = new UnityEngine.Color(1f, 0.85f, 0.1f, 0.8f);
            var stripColour = new UnityEngine.Color(0.2f, 0.85f, 1f, 0.85f);
            foreach (float lateral in new[] { -width * 0.5f, width * 0.5f, m_StripLo, m_StripHi })
            {
                if (float.IsNaN(lateral))
                    continue;
                bool strip = lateral == m_StripLo || lateral == m_StripHi;
                List<Vec3> line = Marking.Offset(arc, s - (strip ? 15f : 30f), s + (strip ? 15f : 30f), lateral, 3f);
                for (int i = 1; i < line.Count; i++)
                {
                    var a = new float3(line[i - 1].X, line[i - 1].Y + 0.12f, line[i - 1].Z);
                    var b = new float3(line[i].X, line[i].Y + 0.12f, line[i].Z);
                    buffer.DrawLine(strip ? stripColour : edgeColour, new Line3.Segment(a, b), 0.1f);
                }
            }
        }

        /// <summary>When the mod is unloaded: do not leave our tool running.</summary>
        internal static void Shutdown()
        {
            ShortcutBarrier.End();
            if (Instance != null && Instance.m_ToolSystem != null && Instance.m_ToolSystem.activeTool == Instance)
                Instance.m_ToolSystem.activeTool = Instance.m_DefaultToolSystem;
        }
    }
}
