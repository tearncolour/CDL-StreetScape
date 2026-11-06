using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The live picture of a support while it is edited (0.12.58; the owner: "优化立杆编辑时性能和实时渲染"). The tile that holds it is rebuilt and its meshes put on again for every change of a setting,
    /// which is most of what an edit cost (the whole tile, the markings in it too); the support being edited is drawn apart instead, as the one that is put on a support is (Attach): it is left out of its
    /// tile (ChunkSystem.SetHidden) while it is selected, one rebuild when it is picked and one when it is let go, and every change of its settings between is a mesh of its own. Pure.
    /// </summary>
    public static class LiveEdit
    {
        /// <summary>
        /// What makes the live picture another one: everything of the boards that is drawn except where the support stands (the picture is moved there), the style and the lean included, and the
        /// ground its posts stand on. Equal keys, equal pictures.
        /// </summary>
        public static string Key(IReadOnlyList<ItemRecord> records, GroundProfile ground)
        {
            var key = new StringBuilder();
            foreach (ItemRecord r in records)
            {
                key.Append(r.TypeHash).Append(':').Append(r.SupportHash).Append(':').Append(r.Spec ?? "").Append(':').Append(r.Reach.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(r.Group).Append(':').Append(r.Slot).Append(':').Append(r.Lane).Append(':').Append(r.OffsetX.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(r.Turn.ToString("R", CultureInfo.InvariantCulture)).Append(r.Back ? 'B' : 'F').Append(r.Manual ? 'M' : 'a').Append(':')
                    .Append(r.Tilt.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(r.Rise.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(r.Clearance.ToString("R", CultureInfo.InvariantCulture)).Append(':').Append(r.Style).Append('&');
            }

            return key.Append('|').Append(ground?.Version ?? 0).ToString();
        }
    }
}
