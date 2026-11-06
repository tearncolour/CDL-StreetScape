using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// What an area is judged by once the road under it is cut (AreaFitRules.OnRoad): the plan and settings of the marking and its kind. Immutable once made; the plan is the kept one (MarkingTemplates.PlanOf).
    /// </summary>
    public sealed class AreaRule
    {
        public MarkingPlan Plan;
        public string Spec;
        public SignDef Def;
    }

    /// <summary>
    /// One cutting of the bands of the roads' surface, as the main thread hands it to a worker (0.12.74; the owner's log of 0.12.73: two single frames of 88 and 100 ms were area fits, 121 height fits
    /// took 143 ms). Everything in it is read only after it is made: the strips are the cache's (SurfaceStrip never changes once read), the rest are numbers. Pure.
    /// </summary>
    public sealed class FitJob
    {
        /// <summary>Given by the worker that takes it.</summary>
        public long Id;

        /// <summary>The cells for heights (AreaFit.ForHeights), else the pieces that cut paint (AreaFit.FromStrips).</summary>
        public bool Heights;

        public IList<SurfaceStrip> Strips;
        public Vec3 Origin;
        public float Yaw, MinX, MaxX, MinZ, MaxZ, Margin = 1f;

        /// <summary>For an area: whether it is on the road is judged in the same job (the cover is a drape of its outline: as much work as the cut).</summary>
        public AreaRule Rule;

        /// <summary>The synchronous result of the same job, when the worker's is only to be compared with it (WorkerMode.Shadow).</summary>
        public FitResult? Reference;

        /// <summary>How big it is: the cells of the bands, a rough measure of its cost.</summary>
        public int Cells
        {
            get
            {
                int n = 0;
                if (Strips != null)
                {
                    foreach (SurfaceStrip strip in Strips)
                        n += Math.Max(0, Math.Min(strip.Left.Count, strip.Right.Count) - 1);
                }

                return n;
            }
        }
    }

    public struct FitResult
    {
        public AreaFit Fit;

        /// <summary>For a job with a rule: whether the area is painted on the road (the fit is then the one to use); else false.</summary>
        public bool OnRoad;

        public float Cover;

        /// <summary>The time of the cut itself, and of the judgement after it (ms).</summary>
        public double CutMs, RuleMs;
    }

    /// <summary>The cutting of a job: one function for the main thread and the workers, so that they cannot differ. No state of its own, nothing shared written. Pure.</summary>
    public static class FitCut
    {
        public static FitResult Run(FitJob job)
        {
            long begin = Stopwatch.GetTimestamp();
            AreaFit fit = job.Heights
                ? AreaFit.ForHeights(job.Strips, job.Origin, job.Yaw, job.MinX, job.MaxX, job.MinZ, job.MaxZ, job.Margin)
                : AreaFit.FromStrips(job.Strips, job.Origin, job.Yaw, job.MinX, job.MaxX, job.MinZ, job.MaxZ, job.Margin);
            long cut = Stopwatch.GetTimestamp();
            var result = new FitResult { Fit = fit, CutMs = Ms(cut - begin) };
            if (job.Rule != null)
            {
                result.OnRoad = AreaFitRules.OnRoad(fit, job.Rule.Plan, job.Rule.Spec, job.Rule.Def, out float cover);
                result.Cover = cover;
                if (result.OnRoad)
                    fit.Note = $"{fit.Note}; {cover * 100f:0} % of the area is on the road";
                result.RuleMs = Ms(Stopwatch.GetTimestamp() - cut);
            }

            return result;
        }

        /// <summary>Where two results of one job differ, to the bit; null when they do not. The time they took is not compared.</summary>
        public static string Differ(FitResult expected, FitResult actual)
        {
            if (expected.OnRoad != actual.OnRoad || BitConverter.DoubleToInt64Bits(expected.Cover) != BitConverter.DoubleToInt64Bits(actual.Cover))
                return $"on the road {expected.OnRoad}/{actual.OnRoad}, cover {expected.Cover}/{actual.Cover}";
            if (expected.Fit == null || actual.Fit == null)
                return expected.Fit == actual.Fit ? null : "one has no fit";
            return expected.Fit.SameAs(actual.Fit, out string why) ? null : why;
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
