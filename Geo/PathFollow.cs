using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A line item (a barrier, a rail, a lane line: an item with a baked path) made again on a road whose curve changed (0.12.74): the path's points are put in the world, mapped from the old curve
    /// to the new (RoadRelation.TryMapPoints) and written again as a path, so the item has the new position, yaw and text. A smooth map of the line, not a rebuild: the chain is not read again, so
    /// the line has the road's new shape but not what only a rebuild gives (the lengths the road's lanes now have, a junction cut that moved, the posts' spacing when the length changed). Pure.
    /// </summary>
    public static class PathFollow
    {
        /// <summary>
        /// The path text, the position and the yaw an item gets when its road went from the old curve to the new; false (nothing to write) when the item has no path, either road has no usable shape,
        /// or the mapped line is not a path (a chord shorter than RunPath.MinChord all through).
        /// </summary>
        public static bool TryRemap(string path, Vec3 position, float yaw, ArcTable old, ArcTable now, FollowMode mode, out string newPath, out Vec3 newPosition, out float newYaw) =>
            TryRemap(path, position, yaw, new[] { new RoadRelation.RoadPair { Old = old, Now = now } }, mode, out newPath, out newPosition, out newYaw);

        /// <summary>As above, for a line that runs along several roads (a chain): each point goes by the road its old curve is nearest to.</summary>
        public static bool TryRemap(string path, Vec3 position, float yaw, IList<RoadRelation.RoadPair> roads, FollowMode mode, out string newPath, out Vec3 newPosition, out float newYaw)
        {
            newPath = "";
            newPosition = position;
            newYaw = yaw;
            List<Vec3> local = RunPath.Decode(path);
            if (local.Count < 2)
                return false;
            var mapped = new List<Vec3>(local.Count);
            if (!RoadRelation.TryMapPoints(roads, mode, MarkingPolygon.ToWorld(local, position, yaw), mapped))
                return false;
            string text = RunPath.Encode(mapped, out float turned);
            if (text.Length == 0)
                return false;
            newPath = text;
            newPosition = mapped[0];
            newYaw = turned;
            return true;
        }
    }
}
