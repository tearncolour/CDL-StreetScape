using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using Unity.Entities;

namespace CDLC.TS
{
    /// <summary>
    /// What an extension (AutoRoadLiner, 0.12.94) needs to put items down and take them back, through the placement tool so that they are items like any other: in the history (Ctrl+Z reaches them,
    /// and an extension's own tool can call Undo and Redo), followed with the road they stand at, saved beside the save. Everything made or removed in one frame is one step of the history
    /// (EditHistory.At), so an extension that does a whole re-marking in one update has made one step; across frames it is several. The owner is the item's batch id (TsItem.BatchId, saved since
    /// version 9, read by nothing but the batch API): an extension makes it from its own name and its segment ("CDLC.TS.ARL/…"), so that it finds its items and no other mod's. Its items are not
    /// automatic ones (TsItem.FlagAuto): the city's automatic marking never removes them, and keeps off the roads the extension reserves through its IsReservedRoad hook. Main thread.
    /// </summary>
    internal static class TsEdit
    {
        /// <summary>The tool and the renderer are there (a city is loaded).</summary>
        internal static bool Ready => PlacementTool.Instance != null && ChunkSystem.Instance != null;

        /// <summary>How many more items there may be (PlacementTool.kMaxItems less the ones now); 0 when not ready.</summary>
        internal static int Budget => Ready ? System.Math.Max(0, PlacementTool.kMaxItems - ChunkSystem.Instance.Records.Count) : 0;

        /// <summary>One item, in the history; ownerId goes into its batch id (0: none). Null entity when not ready or the sign has no settings text to write.</summary>
        internal static Entity Create(SignDef def, string spec, Vec3 position, float yaw, Entity edge, float edgeT, uint ownerId) =>
            Ready && def != null ? PlacementTool.Instance.PlaceForExtension(def, spec, position, yaw, edge, edgeT, ownerId) : Entity.Null;

        /// <summary>One item removed, in the history; an entity that is not there is left alone.</summary>
        internal static void Remove(Entity entity) => PlacementTool.Instance?.DestroyItem(entity);

        /// <summary>The owner an item was made with (its batch id); 0 for an item of the player's, of the automatic marking or an entity that is not an item.</summary>
        internal static uint OwnerOf(Entity entity)
        {
            if (!Ready)
                return 0u;
            EntityManager em = PlacementTool.Instance.EntityManager;
            return em.Exists(entity) && em.HasComponent<TsItem>(entity) ? em.GetComponentData<TsItem>(entity).BatchId : 0u;
        }

        /// <summary>
        /// Adds to the list every item made with this owner (read from the items' batch ids, so a pass over all the items: not for every frame). The entities are those of now; after an Undo or a Redo
        /// they are others (see there).
        /// </summary>
        internal static void ItemsOf(uint ownerId, System.Collections.Generic.List<Entity> into)
        {
            if (ownerId == 0u || into == null || !Ready)
                return;
            EntityManager em = PlacementTool.Instance.EntityManager;
            foreach (Entity entity in ChunkSystem.Instance.Records.Keys)
            {
                if (em.Exists(entity) && em.HasComponent<TsItem>(entity) && em.GetComponentData<TsItem>(entity).BatchId == ownerId)
                    into.Add(entity);
            }
        }

        /// <summary>
        /// Takes the last step back (Ctrl+Z); false when there was none. What the step made is removed and what it removed is made again **as other entities**: an entity an extension kept from before
        /// is no longer valid after this, and it finds its items again by ItemsOf and their places.
        /// </summary>
        internal static bool Undo() => Ready && PlacementTool.Instance.UndoOrRedo(false);

        /// <summary>Does the step taken back again (Ctrl+Y); false when there was none. As Undo: the items it makes or removes are other entities than before.</summary>
        internal static bool Redo() => Ready && PlacementTool.Instance.UndoOrRedo(true);
    }
}
