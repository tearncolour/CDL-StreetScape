using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.50 (the owner: "优化交互逻辑"): taking back and doing again (EditHistory) over a store of numbered items like the world's entities (a key made again is a new one), and picking a placed
    /// item's settings without its place (MarkingTemplates.Unplaced).
    /// </summary>
    internal static class HistoryTests
    {
        public static void RunAll()
        {
            Program.Run("history: a click's items taken back and done again", Steps);
            Program.Run("history: removed items made again under new keys, the steps before still find them", Remade);
            Program.Run("history: changes of one item close together are one step", Merge);
            Program.Run("history: a new step forgets what was taken back; at most MaxSteps", Limits);
            Program.Run("history: picking keeps the player's settings, not the place", Unplaced);
        }

        private sealed class Store : EditHistory<int, string>.IStore
        {
            public readonly Dictionary<int, string> Items = new Dictionary<int, string>();
            private int m_Next = 1;

            public bool Exists(int key) => Items.ContainsKey(key);

            public string Read(int key) => Items[key];

            public void Write(int key, string value) => Items[key] = value;

            public int Create(string value)
            {
                int key = m_Next++;
                Items[key] = value;
                return key;
            }

            public void Destroy(int key) => Items.Remove(key);

            public override string ToString()
            {
                var values = new List<string>(Items.Values);
                values.Sort(StringComparer.Ordinal);
                return string.Join(",", values);
            }
        }

        private static int Make(Store store, EditHistory<int, string> history, string value)
        {
            int key = store.Create(value);
            history.Created(key);
            return key;
        }

        private static void Write(Store store, EditHistory<int, string> history, int key, string value)
        {
            history.Before(key, store.Items[key], true);
            store.Items[key] = value;
        }

        private static void Remove(Store store, EditHistory<int, string> history, int key)
        {
            history.Before(key, store.Items[key], false);
            store.Items.Remove(key);
        }

        private static void Steps()
        {
            var store = new Store();
            var history = new EditHistory<int, string>();
            history.At(1, 0.0);
            int a = Make(store, history, "a");
            history.At(2, 10.0);
            // One click: a line of three pieces and the support it was attached to written.
            int b = Make(store, history, "b1");
            Make(store, history, "b2");
            Make(store, history, "b3");
            Write(store, history, a, "a+");
            Program.Check(history.UndoCount == 2 && store.ToString() == "a+,b1,b2,b3", "two steps: " + store);
            Program.Check(history.Undo(store, out int made, out int changed, out int removed) && store.ToString() == "a" && removed == 3 && changed == 1 && made == 0, $"the click taken back: {store} ({made} made, {changed} written, {removed} removed)");
            Program.Check(history.Redo(store, out made, out changed, out removed) && store.ToString() == "a+,b1,b2,b3" && made == 3 && changed == 1, "done again: " + store);
            Program.Check(history.Undo(store, out _, out _, out _) && history.Undo(store, out _, out _, out _) && store.Items.Count == 0, "both taken back: nothing left");
            Program.Check(!history.Undo(store, out _, out _, out _), "nothing more to take back");
            Program.Check(history.Redo(store, out _, out _, out _) && store.ToString() == "a", "the first again");
            Program.Check(history.Redo(store, out _, out _, out _) && store.ToString() == "a+,b1,b2,b3" && !history.Redo(store, out _, out _, out _), "the second again, nothing more");
            // What the game made of an item after the click (the road it took, its laid path) is what doing again gives back.
            store.Items[b] = "b1 laid";
            history.Undo(store, out _, out _, out _);
            history.Redo(store, out _, out _, out _);
            Program.Check(store.Items.ContainsValue("b1 laid"), "done again as the game had made it: " + store);
        }

        private static void Remade()
        {
            var store = new Store();
            var history = new EditHistory<int, string>();
            history.At(1, 0.0);
            int a = Make(store, history, "a");
            history.At(2, 10.0);
            Write(store, history, a, "a2");
            history.At(3, 20.0);
            Remove(store, history, a);
            Program.Check(store.Items.Count == 0, "deleted");
            history.Undo(store, out int made, out _, out _);
            Program.Check(made == 1 && store.ToString() == "a2" && !store.Exists(a), "the deletion taken back: made again under a new key");
            int now = history.Current(a);
            Program.Check(now != a && store.Items[now] == "a2", "the old key leads to the new one");
            // The step before (a written) still finds it, and so does the one before that (a made).
            history.Undo(store, out _, out int changed, out _);
            Program.Check(changed == 1 && store.ToString() == "a", "the write before it taken back on the new key: " + store);
            history.Undo(store, out _, out _, out int removed);
            Program.Check(removed == 1 && store.Items.Count == 0, "the first step: the item removed");
            history.Redo(store, out _, out _, out _);
            history.Redo(store, out _, out _, out _);
            history.Redo(store, out _, out _, out _);
            Program.Check(store.Items.Count == 0, "all done again: deleted at the end");
            history.Undo(store, out _, out _, out _);
            Program.Check(store.ToString() == "a2", "and back once more: " + store);
        }

        private static void Merge()
        {
            var store = new Store();
            var history = new EditHistory<int, string>();
            history.At(1, 0.0);
            int a = Make(store, history, "");
            // A field typed letter by letter, a frame each, 0.3 s apart: one step.
            string text = "";
            for (int i = 0; i < 5; i++)
            {
                history.At(10 + i, 5.0 + i * 0.3);
                text += "x";
                Write(store, history, a, text);
            }

            Program.Check(history.UndoCount == 2, "five letters, one step: " + history.UndoCount);
            history.Undo(store, out _, out _, out _);
            Program.Check(store.Items[a] == "", "taken back to before the first letter");
            history.Redo(store, out _, out _, out _);
            // Three seconds later: another step; another item: another step.
            history.At(30, 9.0);
            Write(store, history, a, "later");
            int b = Make(store, history, "b");
            history.At(31, 9.1);
            Write(store, history, b, "b2");
            Program.Check(history.UndoCount == 4, "a pause or another item starts a step: " + history.UndoCount);
            // A step that made an item and wrote it is not one another write joins.
            history.At(32, 9.2);
            Write(store, history, b, "b3");
            Program.Check(history.UndoCount == 4, "a write of the same item close after joins the write before: " + history.UndoCount);
        }

        private static void Limits()
        {
            var store = new Store();
            var history = new EditHistory<int, string>();
            for (int i = 0; i < EditHistory<int, string>.MaxSteps + 20; i++)
            {
                history.At(i, i * 10.0);
                Make(store, history, "i" + i);
            }

            Program.Check(history.UndoCount == EditHistory<int, string>.MaxSteps, "at most MaxSteps: " + history.UndoCount);
            history.Undo(store, out _, out _, out _);
            history.Undo(store, out _, out _, out _);
            Program.Check(history.RedoCount == 2, "two to do again");
            history.At(1000, 5000.0);
            Make(store, history, "new");
            Program.Check(history.RedoCount == 0, "a new step forgets them");
            history.Clear();
            Program.Check(history.UndoCount == 0 && history.RedoCount == 0 && !history.Undo(store, out _, out _, out _), "another city: nothing");
        }

        private static void Unplaced()
        {
            GuideTemplate bays = GuideTemplates.ById("mk.bay.perpendicular");
            string placed = bays.Default().With("pitch", "2.7").With("depth", "5").With(MarkingBend.Key, "100:5,5").With(MarkingBend.PlaceKey, MarkingBend.Drawn).With(MarkingBend.FillKey, "2600")
                .With(GroundGrid.Key, "0,2,0,30,2,2,1,1,1,1").With("g", "0.03").ToString();
            GuideSpec picked = bays.Parse(MarkingTemplates.Unplaced(bays, placed));
            Program.Check(picked.Get("pitch") == "2.7" && picked.Get("depth") == "5" && picked.Is(MarkingBend.PlaceKey, MarkingBend.Drawn), "bays: the player's sizes and placing kept");
            Program.Check(picked.Get(MarkingBend.Key) == "" && picked.Get(MarkingBend.FillKey) == "" && picked.Get(GroundGrid.Key) == "" && picked.Get("g") == "0", "bays: their line, length, ground and grade left behind");
            GuideTemplate line = GuideTemplates.ById("mk.line.lane");
            string run = MarkingTemplates.WithPath(line, line.Default().With("width", "15").ToString(), "300:0:0|300:10:0");
            GuideSpec lane = line.Parse(MarkingTemplates.Unplaced(line, run));
            Program.Check(lane.Get(RunPath.Key) == "" && lane.Get("width") == line.Parse(run).Get("width"), "a lane line: its path left behind, its width kept");
            GuideTemplate fence = GuideTemplates.ById("bar.fence.bj");
            Program.Check(fence != null && fence.Parse(MarkingTemplates.Unplaced(fence, fence.Default().ToString())).ToString() == fence.Default().ToString(), "a barrier's defaults are its defaults");
        }
    }
}
