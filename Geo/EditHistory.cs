using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// What the player did with the tool, step by step, so that it can be taken back and done again (0.12.50; the owner: "优化交互逻辑"): Ctrl+Z, Ctrl+Y. A step is everything the tool created,
    /// changed or removed in one frame (a click: a line of many pieces, a stretch marked by the standard, a support with its boards): each item's value before (none when it was created) is noted the
    /// first time the step touches it, and its value after is read when the step is taken back (what the game's systems made of it meanwhile included). Taking back writes each value before again,
    /// makes again what was removed and removes what was created; doing again the other way round. An item made again is another one (another entity): the old key is followed to the new one, so that
    /// the steps before it still find it. A step that only changed one item that the step before also only changed, within MergeSeconds, joins it (a field typed letter by letter, a board moved
    /// press by press). Pure: the store is the game's (PlacementTool) or a test's.
    /// </summary>
    public sealed class EditHistory<TKey, TValue> where TKey : IEquatable<TKey>
    {
        /// <summary>Where the items are: the world's entities with their TsItem.</summary>
        public interface IStore
        {
            bool Exists(TKey key);
            TValue Read(TKey key);
            void Write(TKey key, TValue value);
            TKey Create(TValue value);
            void Destroy(TKey key);
        }

        /// <summary>The most steps kept: the oldest goes.</summary>
        public const int MaxSteps = 100;

        /// <summary>How close in time (s) two changes of one item are to be one step.</summary>
        public const double MergeSeconds = 2.0;

        private sealed class Change
        {
            public TKey Key;
            public bool Had, Has;
            public TValue Before, After;
        }

        private sealed class Step
        {
            public readonly List<Change> Changes = new List<Change>();
            public long Frame;
            public double Time;

            /// <summary>Only one item, which was there before and still is: a step a later change of it may join.</summary>
            public bool Write;
        }

        private readonly List<Step> m_Undo = new List<Step>();
        private readonly List<Step> m_Redo = new List<Step>();
        private readonly Dictionary<TKey, TKey> m_Moved = new Dictionary<TKey, TKey>();
        private Step m_Open;
        private long m_Frame = -1;
        private double m_Time;

        public int UndoCount => m_Undo.Count;

        public int RedoCount => m_Redo.Count;

        /// <summary>The frame and the time (s) of what is noted next: a step is what one frame does.</summary>
        public void At(long frame, double seconds)
        {
            if (frame != m_Frame)
                m_Open = null;
            m_Frame = frame;
            m_Time = seconds;
        }

        /// <summary>Forgets every step (another city).</summary>
        public void Clear()
        {
            m_Undo.Clear();
            m_Redo.Clear();
            m_Moved.Clear();
            m_Open = null;
        }

        /// <summary>The key an item has now (made again under another one by a step taken back or done again).</summary>
        public TKey Current(TKey key)
        {
            for (int guard = 0; guard < 1000 && m_Moved.TryGetValue(key, out TKey next); guard++)
                key = next;
            return key;
        }

        /// <summary>An item was created (it was not there before the step).</summary>
        public void Created(TKey key)
        {
            Step step = Open(key, false);
            if (Find(step, key) == null)
                step.Changes.Add(new Change { Key = key });
            step.Write = false;
        }

        /// <summary>An item that is there is about to be written (write) or removed: its value now is its value before the step, unless the step already has it.</summary>
        public void Before(TKey key, TValue before, bool write)
        {
            Step step = Open(key, write);
            if (Find(step, key) == null)
                step.Changes.Add(new Change { Key = key, Had = true, Before = before });
            step.Write = write && step.Changes.Count == 1 && step.Changes[0].Had;
        }

        private static Change Find(Step step, TKey key)
        {
            foreach (Change change in step.Changes)
            {
                if (change.Key.Equals(key))
                    return change;
            }

            return null;
        }

        private Step Open(TKey key, bool write)
        {
            if (m_Open != null)
                return m_Open;
            m_Redo.Clear();
            Step last = m_Undo.Count > 0 ? m_Undo[m_Undo.Count - 1] : null;
            if (write && last != null && last.Write && last.Changes[0].Key.Equals(key) && m_Time - last.Time <= MergeSeconds)
            {
                last.Time = m_Time;
                last.Frame = m_Frame;
                return m_Open = last;
            }

            m_Open = new Step { Frame = m_Frame, Time = m_Time };
            m_Undo.Add(m_Open);
            if (m_Undo.Count > MaxSteps)
                m_Undo.RemoveAt(0);
            return m_Open;
        }

        /// <summary>Takes the last step back: false when there is none. made, changed, removed: how many items it made again, wrote back and removed.</summary>
        public bool Undo(IStore store, out int made, out int changed, out int removed)
        {
            made = changed = removed = 0;
            m_Open = null;
            if (m_Undo.Count == 0)
                return false;
            Step step = m_Undo[m_Undo.Count - 1];
            m_Undo.RemoveAt(m_Undo.Count - 1);
            for (int i = step.Changes.Count - 1; i >= 0; i--)
            {
                Change change = step.Changes[i];
                TKey key = Current(change.Key);
                bool exists = store.Exists(key);
                change.Has = exists;
                change.After = exists ? store.Read(key) : default;
                Apply(store, key, exists, change.Had, change.Before, ref made, ref changed, ref removed);
            }

            m_Redo.Add(step);
            return true;
        }

        /// <summary>Does the last step taken back again: false when there is none.</summary>
        public bool Redo(IStore store, out int made, out int changed, out int removed)
        {
            made = changed = removed = 0;
            m_Open = null;
            if (m_Redo.Count == 0)
                return false;
            Step step = m_Redo[m_Redo.Count - 1];
            m_Redo.RemoveAt(m_Redo.Count - 1);
            foreach (Change change in step.Changes)
            {
                TKey key = Current(change.Key);
                Apply(store, key, store.Exists(key), change.Has, change.After, ref made, ref changed, ref removed);
            }

            m_Undo.Add(step);
            return true;
        }

        /// <summary>An item (its key now) made what it should be: written, made again (the key then leads to the new one) or removed.</summary>
        private void Apply(IStore store, TKey key, bool exists, bool want, TValue value, ref int made, ref int changed, ref int removed)
        {
            if (want && exists)
            {
                store.Write(key, value);
                changed++;
            }
            else if (want)
            {
                TKey fresh = store.Create(value);
                if (!fresh.Equals(key))
                    m_Moved[key] = fresh;
                made++;
            }
            else if (exists)
            {
                store.Destroy(key);
                removed++;
            }
        }
    }
}
