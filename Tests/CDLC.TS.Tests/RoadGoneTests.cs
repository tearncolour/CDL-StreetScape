using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>0.12.72, event-driven in 0.12.73: the items of a deleted road are found in the pool by the road and decided at once (RoadGone); an item that never had a road, one a road is still beside, and one on a building's wall stay.</summary>
    internal static class RoadGoneTests
    {
        public static void RunAll()
        {
            Program.Run("road gone: the verdict (a road in reach or an item that is not an item of a road keeps it; none removes it)", Verdicts);
            Program.Run("road gone: an item that was never linked is never queued", NeverLinked);
            Program.Run("road gone: a road that dies queues its items at once, once each, with one lookup", Queueing);
            Program.Run("road gone: a road that is replaced (a road in reach when the item is decided) keeps its items, relinked", Replaced);
            Program.Run("road gone: a road that is gone removes its items in the update it died in", Removed);
            Program.Run("road gone: at most MaxPerUpdate items are decided in an update, the rest in the next, in the order they were queued", Capped);
            Program.Run("road gone: an item linked to another road is no longer one of the first road's; a forgotten item is nobody's", Moved);
            Program.Run("road gone: the pool holds no road without items, and prunes the items that are gone", Pool);
            Program.Run("road gone: cleared when a city is loaded", Cleared);
        }

        /// <summary>A decision made from what the system found out about each item: whether it is exempt and whether a road is in reach.</summary>
        private static RoadGone<int, int>.Decide Look(HashSet<int> exempt, HashSet<int> inReach, List<int> looked = null) =>
            waiting =>
            {
                looked?.Add(waiting.Item);
                return RoadGone<int, int>.Verdict(exempt != null && exempt.Contains(waiting.Item), inReach != null && inReach.Contains(waiting.Item));
            };

        private static RoadGone<int, int>.Decide Nothing() => Look(null, null);

        private static void Verdicts()
        {
            Program.Check(RoadGone<int, int>.Verdict(false, true) == RoadGoneVerdict.Keep, "a road in reach keeps the item");
            Program.Check(RoadGone<int, int>.Verdict(false, false) == RoadGoneVerdict.Remove, "no road in reach and an item of a road: removed");
            Program.Check(RoadGone<int, int>.Verdict(true, false) == RoadGoneVerdict.Keep, "a sign on a building's wall stays though no road is in reach");
            Program.Check(RoadGone<int, int>.Verdict(true, true) == RoadGoneVerdict.Keep, "and with a road in reach");
        }

        private static void NeverLinked()
        {
            var gone = new RoadGone<int, int>();
            Program.Check(gone.EdgeDied(7) == 0 && gone.Waits == 0, "a road nobody was linked to dies: nothing is queued");
            var removed = new List<RoadGone<int, int>.Waiting>();
            Program.Check(gone.Drain(20, Nothing(), removed) == 0 && removed.Count == 0, "and nothing is decided, so nothing is removed");
        }

        private static void Queueing()
        {
            var gone = new RoadGone<int, int>();
            gone.NoteLinked(1, 10);
            gone.NoteLinked(2, 10);
            gone.NoteLinked(3, 11);
            Program.Check(gone.Linked == 3 && gone.Roads == 2, "three items are linked, to two roads");
            Program.Check(gone.EdgeDied(10) == 2 && gone.Waits == 2, "road 10 dies: its two items are queued");
            Program.Check(gone.Linked == 1 && gone.Roads == 1, "they are no longer in the pool, nor is the road");
            Program.Check(gone.EdgeDied(10) == 0 && gone.Waits == 2, "a second report of the same death queues nothing more");
            gone.NoteLinked(1, 12);
            Program.Check(gone.EdgeDied(12) == 0 && gone.Waits == 2, "an item that waits and is linked again to a road that dies is not queued twice");
            Program.Check(gone.EdgeDied(11) == 1 && gone.Waits == 3, "another road's item is queued by its own death");
        }

        private static void Replaced()
        {
            var gone = new RoadGone<int, int>();
            gone.NoteLinked(1, 10);
            gone.NoteLinked(2, 10);
            gone.NoteLinked(3, 10);
            gone.EdgeDied(10);
            var removed = new List<RoadGone<int, int>.Waiting>();
            var looked = new List<int>();
            // The road was upgraded in the frame it was deleted in: a new road is in reach of two items and none of the third (it stood by a stretch that is not rebuilt).
            int count = gone.Drain(20, Look(null, new HashSet<int> { 1, 2 }, looked), removed);
            Program.Check(count == 3 && looked.Count == 3 && gone.Waits == 0, "all three were decided once, none waits any more");
            Program.Check(removed.Count == 1 && removed[0].Item == 3 && removed[0].Road == 10, "only the one with no road in reach is removed, with the road that died for the log");
            gone.NoteLinked(1, 20);
            gone.NoteLinked(2, 20);
            Program.Check(gone.EdgeDied(20) == 2, "relinked to the new road they are its items: when it dies they are queued again");
        }

        private static void Removed()
        {
            var gone = new RoadGone<int, int>();
            gone.NoteLinked(1, 10);
            gone.NoteLinked(2, 10);
            gone.EdgeDied(10);
            var removed = new List<RoadGone<int, int>.Waiting>();
            var looked = new List<int>();
            int count = gone.Drain(20, Look(null, null, looked), removed);
            Program.Check(count == 2 && removed.Count == 2 && gone.Waits == 0, "both are decided in the first drain after the death and removed");
            Program.Check(removed[0].Road == 10 && removed[1].Road == 10 && removed[0].Item + removed[1].Item == 3, "each knows the road that died, for the log");
            Program.Check(gone.Drain(20, Nothing(), removed) == 0 && removed.Count == 2, "nothing is removed twice");

            // Signs on a building's wall are not removed with the road, the others are.
            gone.NoteLinked(5, 11);
            gone.NoteLinked(6, 11);
            gone.EdgeDied(11);
            removed.Clear();
            gone.Drain(20, Look(new HashSet<int> { 5 }, null), removed);
            Program.Check(removed.Count == 1 && removed[0].Item == 6, "the item on a wall stays and the other goes");
        }

        private static void Capped()
        {
            var gone = new RoadGone<int, int>();
            for (int i = 0; i < 45; i++)
                gone.NoteLinked(i, 10);
            gone.EdgeDied(10);
            var removed = new List<RoadGone<int, int>.Waiting>();
            Program.Check(gone.Drain(RoadGone<int, int>.MaxPerUpdate, Nothing(), removed) == 20 && removed.Count == 20 && gone.Waits == 25, "the first update decides 20 and 25 are left");
            Program.Check(gone.Drain(RoadGone<int, int>.MaxPerUpdate, Nothing(), removed) == 20 && removed.Count == 40 && gone.Waits == 5, "the next another 20");
            Program.Check(gone.Drain(RoadGone<int, int>.MaxPerUpdate, Nothing(), removed) == 5 && removed.Count == 45 && gone.Waits == 0, "and the last 5");
            var seen = new HashSet<int>();
            foreach (RoadGone<int, int>.Waiting w in removed)
                seen.Add(w.Item);
            Program.Check(seen.Count == 45, "every item once");
            Program.Check(RoadGone<int, int>.MaxPerUpdate == 20, "the cap is 20");

            // What waits is decided with what is known when its turn comes: a road that was made meanwhile keeps the items that were left.
            for (int i = 0; i < 25; i++)
                gone.NoteLinked(100 + i, 11);
            gone.EdgeDied(11);
            removed.Clear();
            var late = new HashSet<int>();
            for (int i = 20; i < 25; i++)
                late.Add(100 + i);
            gone.Drain(20, Nothing(), removed);
            gone.Drain(20, Look(null, late), removed);
            Program.Check(removed.Count == 20 && gone.Waits == 0, "the five left after the cap were kept by the road that was there when their turn came");
        }

        private static void Moved()
        {
            var gone = new RoadGone<int, int>();
            gone.NoteLinked(1, 10);
            gone.NoteLinked(1, 11);
            Program.Check(gone.Linked == 1 && gone.Roads == 1, "one item is one link, and the road it left has no items in the pool");
            Program.Check(gone.EdgeDied(10) == 0 && gone.Waits == 0, "the road it left dies: nothing happens to it");
            Program.Check(gone.EdgeDied(11) == 1 && gone.Waits == 1, "the road it is linked to dies: it is queued");
            gone.NoteLinked(1, 11);
            gone.NoteLinked(1, 11);
            Program.Check(gone.Linked == 1, "noted again and again it is still one link");

            // An item found beside no road (an orphan) is not in the pool, so no road's death takes it.
            gone.Forget(1);
            Program.Check(gone.Linked == 0 && gone.Roads == 0 && gone.EdgeDied(11) == 0, "a forgotten item is nobody's and its road is gone from the pool");
            gone.Forget(99);
            Program.Check(gone.Linked == 0, "forgetting an item that is not in the pool does nothing");
        }

        private static void Pool()
        {
            var gone = new RoadGone<int, int>();
            for (int i = 0; i < 6; i++)
                gone.NoteLinked(i, 10 + i % 3);
            Program.Check(gone.Roads == 3 && gone.Linked == 6, "six items on three roads");
            var alive = new HashSet<int> { 0, 1, 2 };
            int pruned = gone.Prune(alive.Contains);
            Program.Check(pruned == 3 && gone.Linked == 3 && gone.Roads == 3, "the three that are gone leave the pool");
            alive.Remove(0);
            alive.Remove(3);
            pruned = gone.Prune(alive.Contains);
            Program.Check(pruned == 1 && gone.Linked == 2, "and the next one that is gone");
            for (int i = 0; i < 2; i++)
                gone.Prune(i2 => false);
            Program.Check(gone.Linked == 0 && gone.Roads == 0, "a pool of items that are all gone is empty, and has no road left");
            Program.Check(gone.Prune(i2 => false) == 0, "pruning an empty pool does nothing");
            for (int i = 0; i < 100; i++)
                gone.NoteLinked(i, i % 7);
            for (int i = 0; i < 100; i++)
                gone.NoteLinked(i, 50 + i % 5);
            Program.Check(gone.Roads == 5 && gone.Linked == 100, "items moved from road to road leave no empty road behind: " + gone.Roads);
        }

        private static void Cleared()
        {
            var gone = new RoadGone<int, int>();
            gone.NoteLinked(1, 10);
            gone.NoteLinked(2, 11);
            gone.EdgeDied(10);
            gone.Clear();
            Program.Check(gone.Linked == 0 && gone.Waits == 0 && gone.Roads == 0, "nothing is linked and nothing waits");
            Program.Check(gone.EdgeDied(11) == 0, "a road of the old city that is reported queues nothing");
            var removed = new List<RoadGone<int, int>.Waiting>();
            Program.Check(gone.Drain(20, Nothing(), removed) == 0 && removed.Count == 0, "nothing is removed");
            gone.NoteLinked(3, 10);
            Program.Check(gone.EdgeDied(10) == 1, "and the pool works again after it");
        }
    }
}
