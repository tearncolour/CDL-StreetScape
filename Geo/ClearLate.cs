using System;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The places in one frame at which the tag of a lane this mod hid is looked at (0.12.86): right after the tag was put on, at the end of Modification4B (where this mod hides), at the end of
    /// Modification5 (after the game's search trees), at the end of ModificationEnd before this mod's late pass, and after it.
    /// </summary>
    public enum TracePhase
    {
        AfterHide,
        Modification4BEnd,
        Modification5End,
        ModificationEndBeforeLate,
        ModificationEndAfterLate
    }

    /// <summary>
    /// The phase bisection of the trace (0.12.86; the owner's log of 4 Nov: every batch of lanes this mod hid had lost the tag by the end of the same frame): for the first few batches the number of
    /// lanes that still carry the tag is counted at each of the TracePhase places, so that the log says between which two places it went. Pure: the engine counts.
    /// </summary>
    public static class ClearPhases
    {
        /// <summary>How many batches of a load are followed phase by phase.</summary>
        public const int ProbeBatches = 6;

        public static string Name(TracePhase phase)
        {
            switch (phase)
            {
                case TracePhase.AfterHide:
                    return "right after the tag was put on";
                case TracePhase.Modification4BEnd:
                    return "the end of Modification4B";
                case TracePhase.Modification5End:
                    return "the end of Modification5";
                case TracePhase.ModificationEndBeforeLate:
                    return "the end of ModificationEnd before the late pass";
                default:
                    return "the end of ModificationEnd after the late pass";
            }
        }

        /// <summary>How many lanes of one batch carried the tag at each place; -1 for a place not looked at.</summary>
        public sealed class Tally
        {
            private readonly int[] m_Tagged = { -1, -1, -1, -1, -1 };

            public Tally(int total)
            {
                Total = total;
            }

            public int Total { get; }

            public bool Has(TracePhase phase) => m_Tagged[(int)phase] >= 0;

            public int Tagged(TracePhase phase) => m_Tagged[(int)phase];

            public void Set(TracePhase phase, int tagged)
            {
                m_Tagged[(int)phase] = Math.Max(0, Math.Min(Total, tagged));
            }

            /// <summary>The first place, in the order of a frame, at which fewer lanes carried the tag than the batch has; -1 when no place saw a loss.</summary>
            public int FirstLoss()
            {
                for (int i = 0; i < m_Tagged.Length; i++)
                {
                    if (m_Tagged[i] >= 0 && m_Tagged[i] < Total)
                        return i;
                }

                return -1;
            }
        }

        /// <summary>The one line of a batch: the counts at each place, and where the tag was first missing.</summary>
        public static string Format(string source, Tally tally, string firstMissing)
        {
            var text = new StringBuilder();
            text.Append("Clear: trace (phases): ").Append(tally.Total.ToString(CultureInfo.InvariantCulture)).Append(" lane(s) hidden by ").Append(source).Append(", carrying the tag");
            for (int i = 0; i < 5; i++)
            {
                var phase = (TracePhase)i;
                text.Append(i == 0 ? ": " : "; ").Append(Name(phase)).Append(' ').Append(tally.Has(phase) ? tally.Tagged(phase).ToString(CultureInfo.InvariantCulture) : "-");
            }

            int loss = tally.FirstLoss();
            text.Append(". ").Append(loss < 0 ? "No place saw a loss" : "The tag was first missing at " + Name((TracePhase)loss));
            if (firstMissing != null && firstMissing.Length > 0)
                text.Append("; the first lane that was missing: ").Append(firstMissing);
            return text.ToString();
        }
    }

    /// <summary>
    /// What the late pass of this mod takes (0.12.86; Systems/ClearLateSystem.cs, at the end of ModificationEnd, after every system of the game and of the other mods that run before it): a
    /// lane the game made or updated in this frame that an area takes and that carries no tag, because something took the tag off it after this mod's pass of Modification4B (Anarchy's
    /// RemoveOverridenSystem does it to every updated lane that has an owner and the tag, under the default and the bulldozer tools, and Move It's MIT_RemoveOverriddenSystem to every updated entity that has it while its tool runs). Pure.
    /// </summary>
    public static class LateRehide
    {
        /// <summary>Whether a lane is tagged again: it has no tag and an area takes it (the engine asks the costly question, whether an area takes it, only of a lane without the tag).</summary>
        public static bool Needs(bool tagged, bool takenByArea) => !tagged && takenByArea;
    }
}
