using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What a marking lane of the game inside an area's box is, to the clearing (the census, 0.12.83): whether it should be hidden and is, should be and is not, or is left visible and why.</summary>
    public enum ClearWhy
    {
        /// <summary>An area takes it and it is hidden.</summary>
        Hidden,

        /// <summary>An area takes it and it is visible: a miss of this mod (the census hides it).</summary>
        Missed,

        /// <summary>No area takes it and it carries the tag: another mod's (Racing's circuit).</summary>
        KeptByOther,

        /// <summary>No area takes it and it carries the tag, and no other mod asks for it: left from before.</summary>
        TagStale,

        /// <summary>Its middle is in an area but it belongs to neither a segment nor a junction (a building's lot).</summary>
        NotOwned,

        /// <summary>Its middle is in an area that takes only the segments' markings (or only the junctions') and it belongs to the other.</summary>
        OtherScope,

        /// <summary>A line across the traffic that the area keeps.</summary>
        Crossing,

        /// <summary>Its middle is inside an area in the plan but the area's surface is more than HeightTolerance above or below it (another deck).</summary>
        OffSurface,

        /// <summary>Its middle is outside every area but a part of it is inside one that would take it: cleared whole or not at all, by its middle.</summary>
        PartlyIn,

        /// <summary>Near an area's box and nowhere inside an area.</summary>
        Outside
    }

    /// <summary>When a census is taken.</summary>
    public enum CensusMoment
    {
        Load,
        TenSeconds,
        Asked,
        AfterUpdated
    }

    /// <summary>
    /// The census of the game's markings near the clearing areas (0.12.83; the owner: "加载存档之后会同时叠加显示原版标线"): one line in the log that says which of the game's markings that the player can
    /// see inside or beside an area are ones this mod should have hidden (a miss, which the census then hides) and which it leaves by its rules, so that the next log tells the two apart. Pure.
    /// </summary>
    public static class ClearCensus
    {
        /// <summary>The most samples of each kind a line carries.</summary>
        public const int MissedSamples = 4, PartlySamples = 2, SurfaceSamples = 2, StaleSamples = 2;

        /// <summary>The most areas a census looks at (the automatic marking places hundreds): the line says how many.</summary>
        public const int MaxRegions = 256;

        public static readonly ClearWhy[] All = (ClearWhy[])Enum.GetValues(typeof(ClearWhy));

        /// <summary>
        /// What a marking lane is: samples are points along its curve (not the middle). A lane an area takes is Hidden or Missed by its tag; a lane no area takes carries the tag (KeptByOther when another
        /// mod asks for it, else TagStale) or is visible for the reason the first area that holds its middle gives, or PartlyIn when only another point of it is inside an area that would take it.
        /// </summary>
        public static ClearWhy Classify(IList<ClearRegion> regions, in VanillaMarking marking, IList<Vec3> samples, bool hidden, bool othersHide)
        {
            if (VanillaClear.Covered(regions, marking))
                return hidden ? ClearWhy.Hidden : ClearWhy.Missed;
            if (hidden)
                return othersHide ? ClearWhy.KeptByOther : ClearWhy.TagStale;
            if (regions == null)
                return ClearWhy.Outside;
            foreach (ClearRegion region in regions)
            {
                if (region == null || !region.Contains(marking.X, marking.Z))
                    continue;
                if (marking.Owner == MarkingOwner.Other)
                    return ClearWhy.NotOwned;
                if (region.Scope == ClearScope.Edges && marking.Owner != MarkingOwner.Edge || region.Scope == ClearScope.Nodes && marking.Owner != MarkingOwner.Node)
                    return ClearWhy.OtherScope;
                if (region.KeepCrossings && marking.Crossing)
                    return ClearWhy.Crossing;
                return ClearWhy.OffSurface;
            }

            if (samples != null)
            {
                foreach (Vec3 at in samples)
                {
                    var part = new VanillaMarking { X = at.X, Y = at.Y, Z = at.Z, Owner = marking.Owner, Crossing = marking.Crossing };
                    if (VanillaClear.Covered(regions, part))
                        return ClearWhy.PartlyIn;
                }
            }

            return ClearWhy.Outside;
        }

        /// <summary>The counts of a census and the few lanes it names.</summary>
        public sealed class Tally
        {
            private readonly int[] m_Counts = new int[All.Length];
            private readonly List<string> m_Samples = new List<string>();
            private readonly int[] m_Named = new int[All.Length];

            public int Count(ClearWhy why) => m_Counts[(int)why];

            public int Total
            {
                get
                {
                    int sum = 0;
                    foreach (int n in m_Counts)
                        sum += n;
                    return sum;
                }
            }

            public IReadOnlyList<string> Samples => m_Samples;

            /// <summary>Counts one lane; names it (describe is asked only then) while its kind has samples left.</summary>
            public void Add(ClearWhy why, Func<string> describe)
            {
                m_Counts[(int)why]++;
                int cap = why == ClearWhy.Missed ? MissedSamples : why == ClearWhy.PartlyIn ? PartlySamples : why == ClearWhy.OffSurface ? SurfaceSamples : why == ClearWhy.TagStale ? StaleSamples : 0;
                if (m_Named[(int)why]++ < cap)
                    m_Samples.Add(why + " " + describe());
            }
        }

        /// <summary>The one line of a census: the counts first (what was seen and what was done), the lanes named after.</summary>
        public static string Format(CensusMoment moment, int items, int regions, int looked, Tally tally, int pictures, bool repaired)
        {
            var text = new StringBuilder();
            text.Append("Clear: census (").Append(Name(moment)).Append("): ").Append(items.ToString(CultureInfo.InvariantCulture)).Append(" clearing item(s), ").Append(regions.ToString(CultureInfo.InvariantCulture)).Append(" area(s)");
            if (looked < regions)
                text.Append(" (").Append(looked.ToString(CultureInfo.InvariantCulture)).Append(" looked at)");
            int missed = tally.Count(ClearWhy.Missed);
            text.Append(", ").Append(tally.Total.ToString(CultureInfo.InvariantCulture)).Append(" marking lane(s) near them: ").Append(tally.Count(ClearWhy.Hidden).ToString(CultureInfo.InvariantCulture)).Append(" hidden, ")
                .Append(missed.ToString(CultureInfo.InvariantCulture)).Append(" visible although an area takes them").Append(repaired && missed > 0 ? " (hidden now)" : "").Append("; ")
                .Append("left by the rules: ").Append(tally.Count(ClearWhy.Crossing).ToString(CultureInfo.InvariantCulture)).Append(" crossing(s) kept, ").Append(tally.Count(ClearWhy.OtherScope).ToString(CultureInfo.InvariantCulture)).Append(" of the other scope, ")
                .Append(tally.Count(ClearWhy.NotOwned).ToString(CultureInfo.InvariantCulture)).Append(" of no road, ").Append(tally.Count(ClearWhy.OffSurface).ToString(CultureInfo.InvariantCulture)).Append(" on another deck, ")
                .Append(tally.Count(ClearWhy.PartlyIn).ToString(CultureInfo.InvariantCulture)).Append(" partly inside (the middle is outside), ").Append(tally.Count(ClearWhy.Outside).ToString(CultureInfo.InvariantCulture)).Append(" outside; ")
                .Append(tally.Count(ClearWhy.KeptByOther).ToString(CultureInfo.InvariantCulture)).Append(" tagged by another mod, ").Append(tally.Count(ClearWhy.TagStale).ToString(CultureInfo.InvariantCulture)).Append(" tagged with no area; ")
                .Append(pictures.ToString(CultureInfo.InvariantCulture)).Append(" picture(s) of the game inside an area").Append(repaired && pictures > 0 ? " (taken away now)" : "");
            if (tally.Samples.Count > 0)
                text.Append("; ").Append(string.Join("; ", tally.Samples));
            return text.ToString();
        }

        public static string Name(CensusMoment moment) => moment == CensusMoment.Load ? "load" : moment == CensusMoment.TenSeconds ? "10 s" : moment == CensusMoment.Asked ? "asked" : "after Updated";
    }

    /// <summary>
    /// When the censuses are taken (ClearMarkingsSystem asks the clock): one when the load's hold ends and one 10 s after it (or, when the hold did not ask, 30 s after the load), one on request (the file
    /// the player or the log asks with), one after a batch of road pieces was given Updated (a second after it, and at most a few a load). Each is due once. Pure: the time is given.
    /// </summary>
    public sealed class CensusSchedule
    {
        public const double After = 10.0, UpdatedDelay = 1.0, FileEvery = 2.0, Fallback = 30.0;
        public const int MaxAfterUpdated = 8;

        private readonly List<KeyValuePair<double, CensusMoment>> m_Due = new List<KeyValuePair<double, CensusMoment>>();
        private double m_FallbackAt = -1.0;
        private bool m_LoadAsked;
        private int m_UpdatedAsked;
        private double m_FileAt = double.MinValue;

        /// <summary>A new city was loaded: nothing is due; the load's census is armed to come at loadedAt + Fallback if the hold does not ask for it.</summary>
        public void Reset(double loadedAt)
        {
            m_Due.Clear();
            m_LoadAsked = false;
            m_UpdatedAsked = 0;
            m_FallbackAt = loadedAt + Fallback;
        }

        /// <summary>The load's hold ended: the census is due now and again After seconds later. Once a load.</summary>
        public void AskLoad(double now)
        {
            if (m_LoadAsked)
                return;
            m_LoadAsked = true;
            m_FallbackAt = -1.0;
            m_Due.Add(new KeyValuePair<double, CensusMoment>(now, CensusMoment.Load));
            m_Due.Add(new KeyValuePair<double, CensusMoment>(now + After, CensusMoment.TenSeconds));
        }

        /// <summary>Road pieces were given Updated: a census a second later, not while one of that kind waits, and at most MaxAfterUpdated a load.</summary>
        public void AskUpdated(double now)
        {
            if (m_UpdatedAsked >= MaxAfterUpdated || Pending(CensusMoment.AfterUpdated))
                return;
            m_UpdatedAsked++;
            m_Due.Add(new KeyValuePair<double, CensusMoment>(now + UpdatedDelay, CensusMoment.AfterUpdated));
        }

        /// <summary>A census was asked for (the file): due now, once however often it is asked before it is taken.</summary>
        public void AskNow(double now)
        {
            if (!Pending(CensusMoment.Asked))
                m_Due.Add(new KeyValuePair<double, CensusMoment>(now, CensusMoment.Asked));
        }

        /// <summary>Whether the file that asks for a census is to be looked for now (every FileEvery seconds).</summary>
        public bool FileDue(double now)
        {
            if (now - m_FileAt < FileEvery)
                return false;
            m_FileAt = now;
            return true;
        }

        /// <summary>The census that is due, the earliest first, each once.</summary>
        public bool Take(double now, out CensusMoment moment)
        {
            if (!m_LoadAsked && m_FallbackAt >= 0.0 && now >= m_FallbackAt)
                AskLoad(now);
            int best = -1;
            for (int i = 0; i < m_Due.Count; i++)
            {
                if (m_Due[i].Key <= now && (best < 0 || m_Due[i].Key < m_Due[best].Key))
                    best = i;
            }

            if (best < 0)
            {
                moment = CensusMoment.Load;
                return false;
            }

            moment = m_Due[best].Value;
            m_Due.RemoveAt(best);
            return true;
        }

        private bool Pending(CensusMoment moment)
        {
            foreach (KeyValuePair<double, CensusMoment> due in m_Due)
            {
                if (due.Value == moment)
                    return true;
            }

            return false;
        }
    }
}
