using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Unity.Entities;
using UnityEngine;

namespace CDLC.TS
{
    /// <summary>
    /// The support being edited, drawn live (0.12.58; the owner: "优化立杆编辑时性能和实时渲染"). Every setting the editor changes used to rebuild the whole tile the support is in (on a worker) and put
    /// every mesh of it on again (on the main thread): the markings, the barriers and the other supports of the tile too, 10 to 40 ms of the frame for a change of the finish. Now the support and its
    /// boards are left out of their tile (ChunkSystem.SetHidden) while one of them is the item being edited, drawn as one mesh of their own (the preview's, as when a board is put on a support), and
    /// made again at once for each change: the tile is rebuilt when the support is picked and when it is let go, not between. Where the picture stands is the lead's place, so moving the support
    /// moves it; its posts stand on the ground the renderer read for it (the record's Ground), as in the tile.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>The live picture is up (the support is hidden from its tile), and the key of what it shows (LiveEdit.Key).</summary>
        private bool m_Live;
        private string m_LiveKey;

        /// <summary>Called every frame of the edit mode, after the keys of the frame were done: draws the edited support live, or nothing when there is none (a marking is paint on the road and has none).</summary>
        private void UpdateLiveEdit()
        {
            if (!TryReadEdit(out TsItem selected))
            {
                EndLiveEdit();
                HidePreview();
                return;
            }

            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, selected);
            ItemRecord lead = members[0].Value;
            SignDef def = SignCatalog.ByHash(lead.TypeHash);
            if (def == null || def.IsMarking)
            {
                EndLiveEdit();
                HidePreview();
                return;
            }

            // The ground the renderer read for the lead (it is not in the entity's data), as the tile has it.
            GroundProfile ground = ChunkSystem.Instance != null && ChunkSystem.Instance.Records.TryGetValue(members[0].Key, out ItemRecord stored) ? stored.Ground : null;
            List<ItemRecord> records = RecordsOf(members);
            // A text light box whose plan is not made yet is drawn as bars (provisional): the key changes when a plan has been made, so that its letters replace them.
            string key = LiveEdit.Key(records, ground) + (def.Template != null && (def.Template.Id == CDLC.TS.Geo.Signs.TextBoxes.TemplateId || def.Template.Id == CDLC.TS.Geo.Signs.StandBoxes.TemplateId) ? "|t" + TextPlans.Epoch : "");
            if (!m_Live || m_Preview == null || m_LiveKey != key)
            {
                SignParts parts = members.Count == 1 ? ChunkBuilder.PartsOfRecord(lead) : ChunkBuilder.PartsOfGroup(records);
                if (parts == null)
                {
                    EndLiveEdit();
                    HidePreview();
                    return;
                }

                DestroyPreview();
                PostGround.Offsets(parts, ground, out float[] top, out float[] bottom);
                GroundedFrom(parts, top, bottom, out MeshBuilder metal, out MeshBuilder concrete, out MeshBuilder[] solids);
                BuildPreview(parts, metal, concrete, solids, true);
                m_PreviewKey = key;
                m_LiveKey = key;

                // Hidden from the tile in the same frame that the picture is drawn; the key has every board in it, so the set is only made when it can have changed.
                var entities = new List<Entity>(members.Count);
                foreach (KeyValuePair<Entity, ItemRecord> member in members)
                    entities.Add(member.Key);
                ChunkSystem.SetHidden(entities);
            }

            m_Live = true;
            m_Preview.SetActive(true);
            m_Preview.transform.SetPositionAndRotation(new Vector3(lead.Position.X, lead.Position.Y, lead.Position.Z), Quaternion.Euler(0f, lead.Yaw * Mathf.Rad2Deg, 0f));
        }

        /// <summary>The support goes back into its tile: the edit is left, another mode begins, or the tool stops. Safe to call at any time.</summary>
        private void EndLiveEdit()
        {
            if (!m_Live)
                return;
            m_Live = false;
            m_LiveKey = null;
            ChunkSystem.SetHidden(null);
            DestroyPreview();
        }
    }
}
