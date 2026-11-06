using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>
    /// Putting down a whole pole (0.12.88; the owner: with a key held the eyedropper takes all the boards of the pole): the pole the pick carried (CarryPick, m_Carrying) is placed with one click,
    /// the board that was clicked as a single sign is placed (Place, so its place, support and reach are the ones placing works out) and the others made from it, each on the slot, the lane and
    /// the height it had on the support it came from.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>The pole carried is the one the chosen entry places: the board that was clicked is the sign that is chosen.</summary>
        private bool CarriedHere(SignDef def) => m_Carrying.Active && m_Carrying.Pole.Boards[m_Carrying.Pole.Lead].Def == def;

        /// <summary>
        /// Makes the other boards of the carried pole beside the placed one (lead) and writes the place each has. The items of a click are one step of the history (a step is what one frame does), so
        /// that one undo takes the whole pole back. The arm or the span is made as long as the boards need, as after any change of the boards (RefitReach). Returns how many boards the pole has.
        /// </summary>
        private int PlaceCarried(Entity lead)
        {
            CarryPick pole = m_Carrying.Pole;
            if (lead == Entity.Null || !EntityManager.Exists(lead) || !EntityManager.HasComponent<TsItem>(lead) || ChunkSystem.Instance == null)
                return 0;
            TsItem first = EntityManager.GetComponentData<TsItem>(lead);
            uint group = ItemGroups.NewId(ChunkSystem.Instance.Records.Values, m_Random);
            var made = new List<Entity>(pole.Count) { lead };
            for (int i = 0; i < pole.Count; i++)
            {
                CarriedBoard board = pole.Boards[i];
                BoardFields fields = CarryPick.FieldsOf(board);
                TsItem item = first;
                if (i != pole.Lead)
                {
                    item.TypeHash = board.Def.Hash;
                    item.Spec = default;
                    item.SetSpec(board.Def.Template != null ? board.Spec : "");
                    item.SetTilt(BoardTilt.Leans(SupportCatalog.For(board.Def, item.SupportHash)) ? fields.TiltSteps : 0);
                }

                item.Group = group;
                item.Slot = fields.Slot;
                item.Lane = fields.Lane;
                item.OffsetX = fields.OffsetX;
                item.Turn = fields.Turn;
                item.Rise = fields.Rise;
                item.Clearance = fields.Clearance;
                item.Flags = fields.Back ? (byte)(item.Flags | TsItem.FlagBack) : (byte)(item.Flags & ~TsItem.FlagBack);
                item.Flags = fields.Manual ? (byte)(item.Flags | TsItem.FlagManual) : (byte)(item.Flags & ~TsItem.FlagManual);
                if (i == pole.Lead)
                    WriteItem(lead, item);
                else
                    made.Add(NewItem(item));
            }

            RefitReach(made, "a pole was placed");
            EditorRevision++;
            m_Placed += pole.Count - 1;
            if (m_Placed <= 60)
                Mod.log.Info($"Placed a pole of {pole.Count} boards at {first.Position}: {string.Join(", ", pole.Boards.ConvertAll(b => b.Def.Id))}.");
            return pole.Count;
        }
    }
}
