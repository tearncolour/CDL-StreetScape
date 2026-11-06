using System.Collections.Generic;
using CDLC.TS.Geo;
using Unity.Entities;
using UnityEngine;

namespace CDLC.TS
{
    /// <summary>
    /// Each board of a stack on one post turned on its own about the post (0.12.8; the owner: "一个杆子上多个牌子情况下也应该支持能够调整每一个牌子的朝向"): the editor's slider, box and buttons and the
    /// keys [ and ] (15 degrees, Shift 5, Alt 1, held they repeat; TurnAdjust) and \ (half a turn), in edit mode and, for the sign about to be placed, in place mode (0.12.80; the owner: "旋转角度调节应该
    /// 支持滑条无级调节和按键步进调节"). The turn of a board that stands is the board's (TsItem.Turn), from the way its support faces; the support and the other boards stay as they are.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>What the keys asked of the board being edited this frame (degrees; 180 for \), read by ReadKeys in edit mode.</summary>
        private int m_EditTurn;

        /// <summary>Whether the board being edited can turn on its post (Assembly.TurnAxis: a single post, a lamp post or a signal pole, the post of a hung support).</summary>
        private bool EditedBoardTurns(in TsItem item)
        {
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            AssemblyPlan plan = ItemGroups.PlanOf(RecordsOf(members), out List<AssemblyBoard> ordered);
            return EditedBoardTurns(members, plan, ordered);
        }

        /// <summary>As above, from the members of the support and its plan, when they have been read already.</summary>
        private bool EditedBoardTurns(List<KeyValuePair<Entity, ItemRecord>> members, AssemblyPlan plan, List<AssemblyBoard> ordered)
        {
            int own = members.FindIndex(pair => pair.Key == EditEntity);
            AssemblyBoard self = own >= 0 && ordered != null ? ordered.Find(b => b.Member == own) : null;
            if (plan == null || self == null)
                return false;
            Placement placement = plan.Placed.Find(p => p.Board.Member == own);
            if (plan.Hung && (placement == null || !placement.OnPost))
                return false;
            return Assembly.TurnAxis(plan.Support.Kind, plan.Layout, self, out _);
        }

        /// <summary>
        /// Turns the board being edited about its post by degrees (+ clockwise seen from above, as ] turns a sign that is being placed); 0 faces it as its support does again. Nothing for a board that
        /// cannot turn (on two posts, an arm, a beam or a wall). Placing: the rotation of the sign about to be placed by degrees, 180 is the half turn (\) and 0 takes both back.
        /// </summary>
        internal void TurnBoard(int degrees)
        {
            if (Mode == 0)
            {
                TurnPlaced(degrees);
                return;
            }

            if (Mode != 2 || !TryReadEdit(out TsItem item) || !EditedBoardTurns(item))
                return;
            TurnTo(item, TurnAdjust.After(item.Turn, degrees));
        }

        /// <summary>The turn of the board being edited as the slider or the box gives it (degrees: any number is a facing); placing: the rotation of the sign about to be placed.</summary>
        internal void SetTurn(float degrees)
        {
            float turn = TurnAdjust.Absolute(degrees);
            if (float.IsNaN(turn))
                return;
            if (Mode == 0)
            {
                // The half turn (\) is in the facing the row shows (PlacedTurnDegrees): the angle set is the whole of it.
                if (!PlaceTurns() || (turn == m_Rotation && !m_Flip))
                    return;
                m_Rotation = turn;
                m_Flip = false;
                EditorRevision++;
                return;
            }

            if (Mode == 2 && TryReadEdit(out TsItem item) && EditedBoardTurns(item))
                TurnTo(item, turn);
        }

        /// <summary>Writes the turn of the board being edited, as one step of the history (the writes of one board within two seconds are one: a slider's commits, a key held).</summary>
        private void TurnTo(in TsItem item, float turn)
        {
            if (turn == item.Turn)
                return;
            TsItem next = item;
            next.Turn = turn;
            WriteItem(EditEntity, next);
            EditorRevision++;
            Mod.log.Info($"Edit: the board is turned {TurnAdjust.Shown(turn):+0.#;-0.#;0} deg on its post from the way its support faces.");
        }

        /// <summary>The rotation and the half turn of the sign about to be placed, as ReadKeys and the editor's buttons change them.</summary>
        private void TurnPlaced(int degrees)
        {
            if (!PlaceTurns())
                return;
            if (degrees == 0)
            {
                if (m_Rotation == 0f && !m_Flip)
                    return;
                m_Rotation = 0f;
                m_Flip = false;
            }
            else if (degrees == 180)
            {
                m_Flip = !m_Flip;
            }
            else
            {
                m_Rotation += degrees * TurnAdjust.Deg2Rad;
            }

            EditorRevision++;
        }

        /// <summary>The facing the editor shows for the sign about to be placed: its rotation with the half turn (\) in it, as "turn round" adds 180 degrees to a board that stands.</summary>
        private float PlacedTurnDegrees() => TurnAdjust.Shown(m_Rotation + (m_Flip ? Mathf.PI : 0f));

        /// <summary>Whether the sign about to be placed takes a rotation on the support chosen now (TurnAdjust.PlaceApplies): the editor's row, and the keys' effect, are there only then.</summary>
        private bool PlaceTurns() => TurnAdjust.PlaceApplies(SignCatalog.ChoiceById(SelectedId)?.Def, SelectedSupport == null ? null : SupportCatalog.ById(SelectedSupport));

        /// <summary>The turn of the board being edited (degrees, -180 to 180), and whether it can turn: for the editor.</summary>
        internal bool EditedTurn(out float degrees)
        {
            degrees = 0f;
            if (Mode != 2 || !TryReadEdit(out TsItem item) || !EditedBoardTurns(item))
                return false;
            degrees = item.Turn * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>The board being edited hangs on a gantry's beam, whose two faces take boards (0.12.22), and whether it is on the back.</summary>
        internal bool EditedFace(out bool back)
        {
            back = false;
            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return false;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            AssemblyPlan plan = ItemGroups.PlanOf(RecordsOf(members), out _);
            int own = members.FindIndex(pair => pair.Key == EditEntity);
            Placement placement = plan?.Placed.Find(p => p.Board.Member == own);
            if (placement == null || plan.Support.Kind != SupportKind.Gantry || placement.OnPost)
                return false;
            back = placement.Back;
            return true;
        }

        /// <summary>
        /// Moves the board being edited to the other face of its gantry (the editor's button, \ in edit mode): at the nearest free place there to where it is now, facing the other way; nothing when that
        /// face has no room for it.
        /// </summary>
        internal void FlipBoard()
        {
            if (!EditedFace(out bool back) || !TryReadEdit(out TsItem item))
                return;
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            int own = members.FindIndex(pair => pair.Key == EditEntity);
            List<AssemblyBoard> all = ItemGroups.Boards(RecordsOf(members));
            AssemblyBoard self = all.Find(b => b.Member == own);
            ItemRecord lead = members[0].Value;
            SupportDef support = SupportCatalog.ByHash(lead.SupportHash);
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(all), support, lead.Reach);
            Placement now = plan.Placed.Find(p => p.Board.Member == own);
            AttachResult moved = Attach.Evaluate(new AttachRequest
            {
                Existing = all.FindAll(b => b.Member != own), Support = support, Reach = lead.Reach, Board = self.Instance, Member = own, Slot = self.Slot, Moving = true, Grow = true, Offset = now.Cx, Back = !back, Rise = self.Rise
            });
            if (!moved.Ok)
            {
                LastRefusal = new HintInfo { Kind = RefusalKind(moved, false), Count = members.Count, Left = moved.SpaceBefore, Need = self.Instance.Def.Width, Hung = true, Name = self.Instance.Def.Zh, Bad = true, Max = moved.MaxBoards, Span = moved.Span };
                EditorRevision++;
                Mod.log.Info($"Edit: the board does not fit on the gantry's {(back ? "front" : "back")}: {moved.Reason}.");
                return;
            }

            LastRefusal = default;
            WriteBoards(members, moved, null, null);
            RefitReach(EntitiesOf(members, Entity.Null), "a board went to the other face");
            EditorRevision++;
            Mod.log.Info($"Edit: the board went to the gantry's {(back ? "front" : "back")} at {moved.Offset:0.00} m.");
        }

        /// <summary>The keys of a turn in edit mode, done once a frame (UpdateEdit): \ on a gantry's beam moves the board to the other face.</summary>
        private void TurnEditedByKeys()
        {
            int degrees = m_EditTurn;
            m_EditTurn = 0;
            if (degrees == 180 && EditedFace(out _))
                FlipBoard();
            else if (degrees != 0)
                TurnBoard(degrees);
        }
    }
}
