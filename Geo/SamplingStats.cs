using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>One part made by both samplers, as the shadow comparison measured it.</summary>
    public struct PartComparison
    {
        /// <summary>What the part is, for the log ("segment 123", "junction part 123:1").</summary>
        public string Part;

        /// <summary>The time (ms) of laying the bands by each sampler (not the composition's stretches, which both take from the same place), and of the whole comparison.</summary>
        public double FixedMs, AdaptiveMs, ShadowMs;

        public int FixedPieces, AdaptivePieces;
        public SamplingMeasure Measure;
        public CornerResult Corners;

        /// <summary>The plan areas (m2) of the part's bands as each sampler laid them.</summary>
        public double FixedArea, AdaptiveArea;
    }

    /// <summary>
    /// What the shadow comparison adds up (0.12.75, pure; the gate of DESIGN-wiring-plan.md B): counters, the worst of each deviation, the parts over the tolerance, the corners, the areas, and the
    /// twenty worst parts; and when the log is to be told. Bounded: nothing here grows with the number of parts but the counters. Main thread.
    /// </summary>
    public sealed class SamplingStats
    {
        public const int WorstKept = 20;
        public const int FirstSummaryAt = 100, FinalSummaryAt = ShadowGate.MaxParts, MaxSummaries = 6;
        public const double SummarySeconds = 180.0;

        /// <summary>Float noise allowed over a tolerance (m): the bound is exact, the arithmetic is not.</summary>
        public const float Slack = 0.0002f;

        /// <summary>A corner further apart than this (m) is a mismatch; the gate's share of an area (a fraction).</summary>
        public const float CornerLimit = 0.001f;
        public const double AreaLimit = 0.005;

        private sealed class Worst
        {
            public string Part;
            public double Ratio;
            public SamplingMeasure Measure;
            public int FixedPieces, AdaptivePieces;
        }

        private readonly List<Worst> m_Worst = new List<Worst>();
        private int m_Prints, m_ComparedAtPrint;
        private bool m_FirstPrinted, m_FinalPrinted;
        private double m_LastPrint = -1.0;

        public int Compared { get; private set; }

        public int OverTolerance { get; private set; }

        public int CornersChecked { get; private set; }

        public int CornersBad { get; private set; }

        public int CountDiffers { get; private set; }

        /// <summary>What the sampler in use made (all parts that were made, not only the compared ones).</summary>
        public int MadeParts { get; private set; }

        public long MadePieces { get; private set; }

        public double MadeMs { get; private set; }

        public double FixedMs { get; private set; }

        public double AdaptiveMs { get; private set; }

        public double ShadowMs { get; private set; }

        public double WorstShadowMs { get; private set; }

        public long FixedPieces { get; private set; }

        public long AdaptivePieces { get; private set; }

        public float AdaptivePlan { get; private set; }

        public float AdaptiveHeight { get; private set; }

        public float FixedPlan { get; private set; }

        public float FixedHeight { get; private set; }

        public float BetweenPlan { get; private set; }

        public float BetweenHeight { get; private set; }

        /// <summary>The largest share (a fraction) by which a part's area is off the dense reference: the adaptive one, the fixed one; and the adaptive off the fixed.</summary>
        public double AreaAdaptive { get; private set; }

        public double AreaFixed { get; private set; }

        public double AreaBetween { get; private set; }

        private static string F(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

        public void Reset()
        {
            m_Worst.Clear();
            m_Prints = m_ComparedAtPrint = 0;
            m_FirstPrinted = m_FinalPrinted = false;
            m_LastPrint = -1.0;
            Compared = OverTolerance = CornersChecked = CornersBad = CountDiffers = MadeParts = 0;
            MadePieces = FixedPieces = AdaptivePieces = 0;
            MadeMs = FixedMs = AdaptiveMs = ShadowMs = WorstShadowMs = 0.0;
            AdaptivePlan = AdaptiveHeight = FixedPlan = FixedHeight = BetweenPlan = BetweenHeight = 0f;
            AreaAdaptive = AreaFixed = AreaBetween = 0.0;
        }

        /// <summary>A part was made by the sampler in use (not compared): its pieces and the time of laying its bands.</summary>
        public void NoteMade(int pieces, double ms)
        {
            MadeParts++;
            MadePieces += pieces;
            MadeMs += ms;
        }

        public void Add(in PartComparison c)
        {
            Compared++;
            FixedMs += c.FixedMs;
            AdaptiveMs += c.AdaptiveMs;
            ShadowMs += c.ShadowMs;
            WorstShadowMs = Math.Max(WorstShadowMs, c.ShadowMs);
            FixedPieces += c.FixedPieces;
            AdaptivePieces += c.AdaptivePieces;
            SamplingMeasure m = c.Measure;
            AdaptivePlan = Math.Max(AdaptivePlan, m.AdaptivePlan);
            AdaptiveHeight = Math.Max(AdaptiveHeight, m.AdaptiveHeight);
            FixedPlan = Math.Max(FixedPlan, m.FixedPlan);
            FixedHeight = Math.Max(FixedHeight, m.FixedHeight);
            BetweenPlan = Math.Max(BetweenPlan, m.BetweenPlan);
            BetweenHeight = Math.Max(BetweenHeight, m.BetweenHeight);
            if (m.AdaptivePlan > PartSampling.TolXz + Slack || m.AdaptiveHeight > PartSampling.TolY + Slack)
                OverTolerance++;
            CornersChecked += c.Corners.Checked;
            CornersBad += c.Corners.Bad;
            if (c.Corners.CountDiffers)
                CountDiffers++;
            if (m.ReferenceArea > 1e-6)
            {
                AreaAdaptive = Math.Max(AreaAdaptive, Math.Abs(c.AdaptiveArea - m.ReferenceArea) / m.ReferenceArea);
                AreaFixed = Math.Max(AreaFixed, Math.Abs(c.FixedArea - m.ReferenceArea) / m.ReferenceArea);
            }

            if (c.FixedArea > 1e-6)
                AreaBetween = Math.Max(AreaBetween, Math.Abs(c.AdaptiveArea - c.FixedArea) / c.FixedArea);
            KeepWorst(c);
        }

        private void KeepWorst(in PartComparison c)
        {
            double ratio = Math.Max(c.Measure.AdaptivePlan / PartSampling.TolXz, c.Measure.AdaptiveHeight / PartSampling.TolY);
            if (m_Worst.Count >= WorstKept && ratio <= m_Worst[m_Worst.Count - 1].Ratio)
                return;
            var entry = new Worst { Part = c.Part, Ratio = ratio, Measure = c.Measure, FixedPieces = c.FixedPieces, AdaptivePieces = c.AdaptivePieces };
            int at = m_Worst.FindIndex(w => w.Ratio < ratio);
            if (at < 0)
                m_Worst.Add(entry);
            else
                m_Worst.Insert(at, entry);
            if (m_Worst.Count > WorstKept)
                m_Worst.RemoveAt(m_Worst.Count - 1);
        }

        /// <summary>The reasons the gate of the plan is not met so far (none: it is). The gate is meant for ON; with nothing compared it says so.</summary>
        public List<string> GateFailures()
        {
            var why = new List<string>();
            if (Compared == 0)
            {
                why.Add("nothing compared yet");
                return why;
            }

            if (OverTolerance > 0)
                why.Add($"{OverTolerance} part(s) over the tolerance ({PartSampling.TolXz * 1000f:0} mm plan, {PartSampling.TolY * 1000f:0} mm height)");
            if (CornersBad > 0 || CountDiffers > 0)
                why.Add($"{CornersBad} corner(s) over 1 mm and {CountDiffers} part(s) with another number of bands");
            if (AreaAdaptive > AreaLimit)
                why.Add($"an area {AreaAdaptive * 100.0:0.00} % off the reference (limit {AreaLimit * 100.0:0.0} %)");
            if (AdaptiveMs > FixedMs)
                why.Add($"the adaptive sampling took {AdaptiveMs:0.0} ms against {FixedMs:0.0} ms");
            return why;
        }

        /// <summary>One line: the counters, for the load line and the slow lines (empty when nothing was made).</summary>
        public string Describe(FeatureState mode, ShadowGate gate)
        {
            var text = new StringBuilder();
            text.Append(F($"sampling {Preferences.StateName(mode)}: {MadeParts} part(s) made with {MadePieces} piece(s) in {MadeMs:0.0} ms"));
            if (mode == FeatureState.Shadow || Compared > 0)
            {
                text.Append(F($"; {Compared} compared, {gate.Skipped} not taken (the frame or the queue was full); pieces fixed {FixedPieces}, adaptive {AdaptivePieces}; laying them fixed {FixedMs:0.0} ms, adaptive {AdaptiveMs:0.0} ms, the comparison {ShadowMs:0.0} ms (dearest part {WorstShadowMs:0.00} ms)"));
                text.Append(F($"; off the true curve by at most {AdaptivePlan * 1000f:0.0} mm in the plan and {AdaptiveHeight * 1000f:0.0} in height (the fixed {FixedPlan * 1000f:0.0} and {FixedHeight * 1000f:0.0}), the adaptive off the fixed by {BetweenPlan * 1000f:0.0} and {BetweenHeight * 1000f:0.0}"));
                text.Append(F($"; {OverTolerance} part(s) over the tolerance; corners over 1 mm {CornersBad} of {CornersChecked}, {CountDiffers} part(s) with another number of bands; areas off the dense reference by at most adaptive {AreaAdaptive * 100.0:0.00} %, fixed {AreaFixed * 100.0:0.00} %, adaptive off fixed {AreaBetween * 100.0:0.00} %"));
            }

            return text.ToString();
        }

        /// <summary>The summary the log gets: the counters, the gate, and the worst parts.</summary>
        public string Report(FeatureState mode, ShadowGate gate)
        {
            var text = new StringBuilder("Sampling: " + Describe(mode, gate));
            List<string> failures = GateFailures();
            text.Append(failures.Count == 0 ? "\n  gate: met so far (to turn it on: the adaptive result is used)" : "\n  gate: not met: " + string.Join("; ", failures));
            foreach (Worst w in m_Worst)
            {
                text.Append(F($"\n  {w.Part}: {w.Ratio:0.00} of the tolerance (adaptive {w.Measure.AdaptivePlan * 1000f:0.0} mm / {w.Measure.AdaptiveHeight * 1000f:0.0} mm, fixed {w.Measure.FixedPlan * 1000f:0.0} / {w.Measure.FixedHeight * 1000f:0.0}, between {w.Measure.BetweenPlan * 1000f:0.0} / {w.Measure.BetweenHeight * 1000f:0.0}), pieces {w.FixedPieces} -> {w.AdaptivePieces}"));
            }

            return text.ToString();
        }

        /// <summary>
        /// Whether the log is to get the summary now: when 100 parts are compared, when the last one is (MaxParts), and every three minutes after the last summary while there are new comparisons, at most
        /// MaxSummaries in all. Asked after each comparison, so it needs no timer of its own.
        /// </summary>
        public bool SummaryDue(double nowSeconds)
        {
            if (m_Prints >= MaxSummaries || Compared == 0)
                return false;
            if (m_LastPrint < 0.0)
                m_LastPrint = nowSeconds;
            if (Compared >= FinalSummaryAt && !m_FinalPrinted)
                return true;
            if (Compared >= FirstSummaryAt && !m_FirstPrinted)
                return true;
            return Compared > m_ComparedAtPrint && nowSeconds - m_LastPrint >= SummarySeconds;
        }

        /// <summary>The summary was written.</summary>
        public void MarkPrinted(double nowSeconds)
        {
            m_Prints++;
            m_LastPrint = nowSeconds;
            m_ComparedAtPrint = Compared;
            if (Compared >= FirstSummaryAt)
                m_FirstPrinted = true;
            if (Compared >= FinalSummaryAt)
                m_FinalPrinted = true;
        }
    }
}
