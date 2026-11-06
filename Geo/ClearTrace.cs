using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>What became of a marking lane this mod hid, when it is looked at again.</summary>
    public enum LaneFate
    {
        /// <summary>The same lane still carries the tag.</summary>
        Tagged,

        /// <summary>The same lane exists and the tag is gone.</summary>
        TagGone,

        /// <summary>The lane's entity does not exist any more (the game made it again as another one, or took it away).</summary>
        Gone,

        /// <summary>The lane is being deleted.</summary>
        Deleted
    }

    /// <summary>When a hidden lane is looked at again: in the frame it was hidden in (the end of the modification phases, before the clean-up), at the start of the next frame, and some time after.</summary>
    public enum TraceStage
    {
        SameFrame,
        NextFrame,
        Later
    }

    /// <summary>
    /// The trace of the lanes this mod hid (0.12.84; the owner's log of 3 Nov: a pass hid 66 lanes and 0.4 s later the census found 77 lanes visible that an area takes, and the census' own repair was
    /// undone by the next census): which lanes were hidden when, and what became of them one look, one frame and 2 s after, so that the next log says whether the tag is taken off a lane that stays, or
    /// the game makes the lane again as another entity. Pure: the engine reads the lanes.
    /// </summary>
    public static class ClearTrace
    {
        /// <summary>How many hidden lanes are remembered, the most lines a load writes, how many lanes a line names, and the frames after which the late look is made.</summary>
        public const int Ring = 512, MaxLines = 18, Samples = 3, LaterFrames = 120;

        public static LaneFate Judge(bool exists, bool deleted, bool tagged) => !exists ? LaneFate.Gone : deleted ? LaneFate.Deleted : tagged ? LaneFate.Tagged : LaneFate.TagGone;

        /// <summary>An entity as one number (its index and version): equal entities, equal keys.</summary>
        public static long Key(int index, int version) => ((long)version << 32) | (uint)index;

        /// <summary>The stages that are due for a batch hidden in frame, given the stages done (a bit mask of TraceStage) and the frame now: the same frame, the next one after it, and LaterFrames after.</summary>
        public static bool Due(TraceStage stage, int done, int hiddenFrame, int nowFrame)
        {
            if ((done & (1 << (int)stage)) != 0)
                return false;
            switch (stage)
            {
                case TraceStage.SameFrame:
                    return nowFrame == hiddenFrame;
                case TraceStage.NextFrame:
                    return nowFrame > hiddenFrame;
                default:
                    return nowFrame >= hiddenFrame + LaterFrames;
            }
        }

        public static string Name(TraceStage stage) => stage == TraceStage.SameFrame ? "the same frame" : stage == TraceStage.NextFrame ? "the next frame" : "later";

        /// <summary>What was remembered of a hidden lane.</summary>
        public struct Entry
        {
            public int Frame;
            public double Time;
            public string Source;
        }

        /// <summary>The last Ring hidden lanes by their keys, the oldest forgotten first; a lane hidden again takes its place at the end with the new time.</summary>
        public sealed class HiddenRing
        {
            private readonly Dictionary<long, KeyValuePair<Entry, LinkedListNode<long>>> m_Entries = new Dictionary<long, KeyValuePair<Entry, LinkedListNode<long>>>();
            private readonly LinkedList<long> m_Order = new LinkedList<long>();

            public int Count => m_Entries.Count;

            public void Add(long key, int frame, double time, string source)
            {
                if (m_Entries.TryGetValue(key, out KeyValuePair<Entry, LinkedListNode<long>> old))
                    m_Order.Remove(old.Value);
                LinkedListNode<long> node = m_Order.AddLast(key);
                m_Entries[key] = new KeyValuePair<Entry, LinkedListNode<long>>(new Entry { Frame = frame, Time = time, Source = source }, node);
                while (m_Entries.Count > Ring)
                {
                    long oldest = m_Order.First.Value;
                    m_Order.RemoveFirst();
                    m_Entries.Remove(oldest);
                }
            }

            public bool TryGet(long key, out Entry entry)
            {
                if (m_Entries.TryGetValue(key, out KeyValuePair<Entry, LinkedListNode<long>> found))
                {
                    entry = found.Key;
                    return true;
                }

                entry = default;
                return false;
            }

            public void Clear()
            {
                m_Entries.Clear();
                m_Order.Clear();
            }
        }

        /// <summary>What a lane that is visible although an area takes it was, as far as the trace knows: hidden by this mod a moment ago (the same lane has lost the tag) or not hidden by it (a lane made after, or never).</summary>
        public static string Explain(bool known, in Entry entry, double now)
        {
            if (!known)
                return "not hidden by this mod before (a lane made since, or one never reached)";
            return string.Format(CultureInfo.InvariantCulture, "hidden by this mod {0:0.0} s ago ({1}): the same lane, the tag is gone", Math.Max(0.0, now - entry.Time), entry.Source);
        }

        /// <summary>The fates of one batch, and the lanes named for the ones that lost the tag or are gone.</summary>
        public sealed class FateTally
        {
            private readonly int[] m_Counts = new int[4];
            private readonly List<string> m_Samples = new List<string>();

            public int Count(LaneFate fate) => m_Counts[(int)fate];

            public int Total => m_Counts[0] + m_Counts[1] + m_Counts[2] + m_Counts[3];

            public IReadOnlyList<string> Named => m_Samples;

            /// <summary>Counts a lane; names it (describe is asked only then) when it is not tagged any more and the line has room.</summary>
            public void Add(LaneFate fate, Func<string> describe)
            {
                m_Counts[(int)fate]++;
                if (fate != LaneFate.Tagged && m_Samples.Count < Samples)
                    m_Samples.Add(fate + " " + describe());
            }
        }

        /// <summary>The line of one look at a batch: how many lanes it had, what became of them, and the tag count of the world, with the lanes named.</summary>
        public static string Format(TraceStage stage, string source, int frames, double seconds, FateTally tally, int taggedNow)
        {
            var text = new StringBuilder();
            text.Append("Clear: trace (").Append(Name(stage)).Append("): ").Append(tally.Total.ToString(CultureInfo.InvariantCulture)).Append(" lane(s) hidden by ").Append(source).Append(" ")
                .Append(frames.ToString(CultureInfo.InvariantCulture)).Append(" frame(s), ").Append(seconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s ago: ")
                .Append(tally.Count(LaneFate.Tagged).ToString(CultureInfo.InvariantCulture)).Append(" still tagged, ")
                .Append(tally.Count(LaneFate.TagGone).ToString(CultureInfo.InvariantCulture)).Append(" lost the tag, ")
                .Append(tally.Count(LaneFate.Gone).ToString(CultureInfo.InvariantCulture)).Append(" are gone (made again as other entities or taken away), ")
                .Append(tally.Count(LaneFate.Deleted).ToString(CultureInfo.InvariantCulture)).Append(" are being deleted; ")
                .Append(taggedNow.ToString(CultureInfo.InvariantCulture)).Append(" lane(s) carry the tag in the world now");
            if (tally.Named.Count > 0)
                text.Append("; ").Append(string.Join("; ", tally.Named));
            return text.ToString();
        }
    }
}
