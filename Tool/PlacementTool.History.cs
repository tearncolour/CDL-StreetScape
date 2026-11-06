using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Game.Rendering;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CDLC.TS
{
    /// <summary>
    /// Taking back and doing again (0.12.50, EditHistory; the owner: "优化交互逻辑"): every item the tool or the panel creates, writes or removes goes through NewItem, WriteItem and DestroyItem, which note
    /// it in the history; Ctrl+Z takes the last step back, Ctrl+Y or Ctrl+Shift+Z does it again. Picking (Ctrl and a click on a placed sign, marking or barrier): its kind and its settings, without what
    /// belongs to where it was put (MarkingTemplates.Unplaced), and its support become what is placed next. The history is of one city: it is forgotten when another is read (ChunkSystem).
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private sealed class ItemStore : EditHistory<Entity, TsItem>.IStore
        {
            private readonly EntityManager m_Entities;

            public ItemStore(EntityManager entities) => m_Entities = entities;

            public bool Exists(Entity key) => m_Entities.Exists(key) && m_Entities.HasComponent<TsItem>(key);

            public TsItem Read(Entity key) => m_Entities.GetComponentData<TsItem>(key);

            public void Write(Entity key, TsItem value) => m_Entities.SetComponentData(key, value);

            public Entity Create(TsItem value)
            {
                Entity entity = m_Entities.CreateEntity();
                m_Entities.AddComponentData(entity, value);
                return entity;
            }

            public void Destroy(Entity key)
            {
                m_Entities.DestroyEntity(key);
                ChunkSystem.Instance?.Forget(key);
            }
        }

        private readonly EditHistory<Entity, TsItem> m_History = new EditHistory<Entity, TsItem>();

        /// <summary>How many steps there are to take back and to do again (for the keys' panel).</summary>
        internal static int UndoSteps => Instance?.m_History.UndoCount ?? 0;
        internal static int RedoSteps => Instance?.m_History.RedoCount ?? 0;

        /// <summary>Forgets the history: another city is read (its entities are not this one's).</summary>
        internal static void ForgetHistory() => Instance?.m_History.Clear();

        private void NoteNow() => m_History.At(UnityEngine.Time.frameCount, UnityEngine.Time.realtimeSinceStartupAsDouble);

        /// <summary>An item made, noted as made by this step.</summary>
        private Entity NewItem(TsItem item)
        {
            Entity entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, item);
            NoteNow();
            m_History.Created(entity);
            if (!item.Orphan)
                ItemRoadSystem.NoteMade(entity, item.Edge);
            return entity;
        }

        /// <summary>An item written, its value before noted.</summary>
        private void WriteItem(Entity entity, TsItem item)
        {
            NoteNow();
            m_History.Before(entity, EntityManager.GetComponentData<TsItem>(entity), true);
            EntityManager.SetComponentData(entity, item);
        }

        /// <summary>An item removed, its value noted (the panel's tidying removes through this too).</summary>
        internal void DestroyItem(Entity entity)
        {
            if (!EntityManager.Exists(entity))
                return;
            if (EntityManager.HasComponent<TsItem>(entity))
            {
                NoteNow();
                m_History.Before(entity, EntityManager.GetComponentData<TsItem>(entity), false);
            }

            EntityManager.DestroyEntity(entity);
            ChunkSystem.Instance?.Forget(entity);
        }

        /// <summary>An item for an extension (TsEdit.Create): placed as the tool places one, in the history, followed with the road, its owner in the batch id.</summary>
        internal Entity PlaceForExtension(SignDef def, string spec, Vec3 position, float yaw, Entity edge, float edgeT, uint ownerId) =>
            Place(def, spec, 0u, 0f, position, yaw, edge, edgeT, "by an extension", 0, 0f, 0f, ownerId);

        /// <summary>Ctrl+Z (back) and Ctrl+Y or Ctrl+Shift+Z (again); a line being drawn is given up first. False when there was no step.</summary>
        internal bool UndoOrRedo(bool again)
        {
            ResetRun();
            ForgetAutoChoice();
            var store = new ItemStore(EntityManager);
            bool done = again ? m_History.Redo(store, out int made, out int changed, out int removed) : m_History.Undo(store, out made, out changed, out removed);
            if (EditEntity != Entity.Null && !TryReadEdit(out _))
                ClearEdit();
            EditorRevision++;
            m_PickMemoHave = false;
            string what = again ? "done again" : "taken back";
            m_Outcome = done ? $"the last step {what}: {made} item(s) made again, {changed} written back, {removed} removed" : $"nothing to be {what}";
            Mod.log.Info($"History: {m_Outcome} ({m_History.UndoCount} step(s) to take back, {m_History.RedoCount} to do again).");
            return done;
        }

        /// <summary>
        /// Ctrl held while placing, or the eyedropper of the dock (StylePick.Mode): the sign, marking or barrier under the cursor is ringed, and a click picks it (its entry, its settings without where it
        /// was put, its support, its tilt and paint, and what StylePick.Of takes), to place more of the same: the tool is placing again with it.
        /// </summary>
        private void UpdatePick(bool click)
        {
            EndAttach();
            HidePreview();
            ItemRecord record = default;
            Entity entity = Entity.Null;
            bool found = m_Camera.activeCamera != null && ChunkSystem.Instance != null && FindUnderCursor(out entity, out record, out _);
            SignDef def = found ? SignCatalog.ByHash(record.TypeHash) : null;
            // With Shift held a click takes every board of the pole the item stands on (CarryPick): the ring and the line of help say how many.
            CarryPick pole = def != null && !def.IsMarking && CarryPick.Wants(Mode, m_Picking, ShiftHeld(), m_Typing) && CarryPick.Takes(SupportCatalog.ByHash(record.SupportHash)?.Kind) ? PoleOf(entity) : null;
            if (def == null && Mode == StylePick.Mode)
                SetHint("pick-mode");
            else
                SetHint("pick", pole != null ? pole.Count : 0, 0f, 0f, 0f, false, false, def != null ? def.Zh : "", def == null, nameEn: def != null ? def.En : "");
            if (def == null)
            {
                if (click)
                    m_Outcome = "ignored: nothing to pick under the cursor";
                return;
            }

            OverlayRenderSystem.Buffer buffer = m_Overlay.GetBuffer(out JobHandle dependencies);
            dependencies.Complete();
            var ring = pole != null ? new UnityEngine.Color(1f, 0.65f, 0.2f, 0.95f) : new UnityEngine.Color(0.35f, 0.9f, 1f, 0.95f);
            foreach (float radius in CarryPick.RingRadii(pole != null ? pole.Count : 1))
                buffer.DrawCircle(ring, new float3(record.Position.X, record.Position.Y + 0.1f, record.Position.Z), radius);
            if (!click)
                return;
            SignChoice choice = EntryOf(def);
            if (choice == null)
            {
                m_Outcome = $"refused: {def.Id} has no entry in the panel to pick";
                return;
            }

            string spec = def.Template != null ? MarkingTemplates.Unplaced(def.Template, record.Spec) : "";
            SupportDef support = def.IsMarking ? null : SupportCatalog.ByHash(record.SupportHash);
            Choose(choice.Id, spec);
            if (!def.IsMarking)
            {
                ChooseSupport(support != null ? support.Id : "");
                PlaceTilt = BoardTilt.StepsOf(record.Tilt);
                PlaceStyle = SupportStyle.Of(record.Style);
                // A board picked is copied as it is painted: no profile dresses it again.
                Profiles.Touch(0, 0, 0);
                TakeSettingsOf(entity, support);
                m_Carrying.Take(pole);
            }
            m_Outcome = $"picked {def.Id} ({choice.Id}) with its settings '{spec}'{(support != null ? ", support " + support.Id : "")}{m_PickedExtra}{(pole != null ? $", and the {pole.Count} boards of its pole" : "")}";
            Mod.log.Info("Tool: " + m_Outcome + ".");
        }

        private string m_PickedExtra = "";

        /// <summary>The panel's entry of a sign (the one with its own id, else any that has it).</summary>
        private static SignChoice EntryOf(SignDef def) => SignCatalog.ChoiceById(def.Id) ?? SignCatalog.Choices.FirstOrDefault(each => each.Def == def);

        private static bool ShiftHeld()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && (keyboard[Key.LeftShift].isPressed || keyboard[Key.RightShift].isPressed);
        }

        /// <summary>The boards of the pole the item stands on, for a pick of all of them; null when it has one board of its own kind of entry, or the support puts boards down one by one (CarryPick.Takes).</summary>
        private CarryPick PoleOf(Entity entity)
        {
            if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                return null;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(entity, EntityManager.GetComponentData<TsItem>(entity));
            CarryPick pole = CarryPick.Of(RecordsOf(members), members.FindIndex(pair => pair.Key == entity), def => EntryOf(def) != null, out int skipped);
            if (skipped > 0 && m_PoleSkipLogs++ < 10)
                Mod.log.Info($"Tool: {skipped} board(s) of the pole are not picked: their sign has no entry in the panel, or is no more in the catalogue.");
            return pole;
        }

        private int m_PoleSkipLogs;

        /// <summary>The eyedropper with the cursor off the world (over the panel): nothing is ringed, and the line of help says what it is for.</summary>
        private void UpdatePickIdle()
        {
            EndAttach();
            HidePreview();
            SetHint("pick-mode");
        }

        /// <summary>
        /// The span or arm, the clearance and the distance from the kerb that the picked support was made with go to the next support (StylePick.Of): what the player had set by hand, and for the span the
        /// planner's again when the picked one was automatic. A support the planner chose (no hash) has no kind to tie them to, and what cannot be read is left as it is.
        /// </summary>
        private void TakeSettingsOf(Entity entity, SupportDef support)
        {
            m_PickedExtra = "";
            if (support == null || !EntityManager.Exists(entity) || !EntityManager.HasComponent<TsItem>(entity))
                return;
            TsItem item = EntityManager.GetComponentData<TsItem>(entity);
            StylePick.Carried carried = StylePick.Of(support.Kind, item.ReachByHand, item.HasBaseReach, item.BaseReach, item.Reach, item.Clearance, TryKerbOf(item, out float margin) ? margin : float.NaN);
            m_PlaceSpan.Clear();
            if (carried.Span > 0f)
                m_PlaceSpan.Set(support.Kind, carried.Span);
            if (carried.Clearance > 0f)
                m_PlaceSpan.SetClearance(support.Kind, carried.Clearance);
            if (carried.HasMargin)
                SetMargin(carried.Margin);
            EditorRevision++;
            m_PickedExtra = $", span {carried.Span:0.0} m, clearance {carried.Clearance:0.0} m{(carried.HasMargin ? $", {carried.Margin:0.0} m from the kerb" : "")}";
        }
    }
}
