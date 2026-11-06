using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The road follow's code run once over made-up roads and items (0.12.86): a post and a wall beside a road that is then moved, followed for four frames. With the generic arguments of the real follow
    /// (the game's entities) it compiles the follow's methods on a worker under the loading screen instead of in the first frame a road is moved, which took 7 to 21 ms of the main thread in the owner's
    /// three sessions. Nothing of the game is touched: its own watch, its own curves.
    /// </summary>
    public static class FollowWarm
    {
        /// <summary>The scenario's writes: how many items the made-up road's move gave a new place (two: the post and the wall).</summary>
        public static int Run<TItem, TEdge>(TItem post, TItem wall, TEdge road) where TItem : IEquatable<TItem> where TEdge : IEquatable<TEdge>
        {
            var watch = new FollowWatch<TItem, TEdge> { Writing = true };
            var curve = new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(10f, 0f, 3f), new Vec3(20f, 0f, 6f), new Vec3(30f, 0f, 9f));
            var shift = new Vec3(3f, 0f, -2f);
            var line = new List<Vec3>();
            for (int i = 0; i <= 10; i++)
                line.Add(curve.Point(i / 10f) + new Vec3(0f, 0f, 4f));
            string path = RunPath.Encode(line, out float lineYaw);
            Vec3 postAt = curve.Point(0.5f) + new Vec3(0f, 0f, 5f);
            var table = new FollowCurve(curve);
            watch.LinkSingle(post, road, table, postAt, 0f);
            watch.LinkLine(wall, road, new[] { road }, new[] { table }, path, line[0], lineYaw);
            var moved = new FollowCurve(new Bezier3(curve.A + shift, curve.B + shift, curve.C + shift, curve.D + shift));
            FollowWatch<TItem, TEdge>.PlaceOf placeOf = (TItem item, bool wantPath, out Vec3 position, out float yaw, out string itemPath) =>
            {
                bool isPost = item.Equals(post);
                position = isPost ? postAt : line[0];
                yaw = isPost ? 0f : lineYaw;
                itemPath = wantPath && !isPost ? path : null;
                return true;
            };
            var writes = new List<FollowWrite<TItem>>();
            watch.Dirty(road);
            int written = 0;
            for (int frame = 1; frame <= 4; frame++)
            {
                writes.Clear();
                watch.StepWrite(e => moved, placeOf, FollowMode.Fraction, frame, 5e6, 200, writes);
                written += writes.Count;
            }

            return written;
        }
    }
}
