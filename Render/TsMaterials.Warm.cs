using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CDLC.TS.Geo;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CDLC.TS.Render
{
    /// <summary>
    /// The baked-detail materials made a stage at a time (0.12.76). One went in a single call before: the owner's 0.12.75 log had a part of 460 vertices that took 40.7 ms because its material (a tree
    /// grate: two 512 x 512 maps, the colour map with the coverage of its holes and compressed, the material validated) was made in it, and the first tile of the horizon 52 ms in 0.12.73. The pixels
    /// of the maps are made on workers (DetailPixels); the main thread takes one stage after another (DetailStages) within a budget of its own, the tile waits for its material
    /// (MaterialGate), and a material that is needed before it is ready is made whole as before. Main thread.
    /// </summary>
    internal static partial class TsMaterials
    {
        private sealed class DetailJob
        {
            internal DetailKind Kind;
            internal SolidColour Colour;
            internal DetailStage Stage = DetailStage.Prepare;
            internal bool Done, Holes;
            internal Material Result, Template;
            internal DetailSurface Surface;
            internal Task<Color32[]> Pixels;
            internal Task<List<float[]>> Coverage;
            internal Texture2D ColourTexture, NormalTexture;
            internal Color32[] MaskPixels;
            internal int Level = 1;
            internal double Ms, WorstMs;
            internal DetailStage WorstStage;
            internal int Stages;
        }

        private enum StepResult
        {
            /// <summary>The data of the stage is still being made on a worker: nothing was done.</summary>
            Waiting,

            /// <summary>Part of the stage was done and it has more to do (the levels of the holes' coverage).</summary>
            Partial,

            /// <summary>The stage is done, or the job is.</summary>
            Advanced
        }

        private static readonly Dictionary<(DetailKind, SolidColour), DetailJob> s_DetailJobs = new Dictionary<(DetailKind, SolidColour), DetailJob>();
        private static readonly List<DetailJob> s_WarmOrder = new List<DetailJob>();
        private static readonly Dictionary<DetailKind, Task<(Color32[] Normal, Color32[] Mask)>> s_MapPixels = new Dictionary<DetailKind, Task<(Color32[] Normal, Color32[] Mask)>>();
        private static readonly Dictionary<DetailKind, DetailJob> s_MapOwners = new Dictionary<DetailKind, DetailJob>();
        private static int s_WarmMade, s_WarmLogged;
        private static double s_WarmMs, s_WarmWorstMs;
        private static string s_WarmWorstWhat = "";

        /// <summary>What the staged making has done, for the load line and the slow line of the chunk renderer.</summary>
        internal static string WarmSummary =>
            $"{s_WarmMade} detail material(s) made in stages, {s_WarmMs:0} ms of the main thread, the longest stage {s_WarmWorstMs:0.0} ms{(s_WarmWorstWhat.Length > 0 ? " (" + s_WarmWorstWhat + ")" : "")}, {s_WarmOrder.Count} on the way";

        /// <summary>The detail materials that are asked for and not yet made (the load's gate waits for them).</summary>
        internal static int WarmWaiting => s_WarmOrder.Count;

        private static SolidColour PaintSolid(MarkingColour colour) =>
            colour == MarkingColour.Yellow ? SolidColour.Yellow : colour == MarkingColour.Red ? SolidColour.Red : colour == MarkingColour.Black ? SolidColour.Black : SolidColour.White;

        private static bool MapsMade(DetailKind kind) => s_DetailMaps.TryGetValue(kind, out (Texture2D Normal, Texture2D Mask) maps) && maps.Normal != null;

        private static Color32[] ToColors(byte[] rgba)
        {
            var colours = new Color32[rgba.Length / 4];
            for (int i = 0; i < colours.Length; i++)
                colours[i] = new Color32(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            return colours;
        }

        // ------------------------------------------------------------------ what a part needs

        /// <summary>
        /// What making the Unity object of a tile's part needs of its material (a part's material name as ChunkBuilder makes it): Have (it is made, or a cheap copy), Wait (a detail material is being
        /// made in stages: its job is started here if it is not yet), Make (the main thread makes it: a validation). Asking for the parts of a tile that is built is the warming up.
        /// </summary>
        internal static MaterialNeed Need(string material)
        {
            if (material.StartsWith(ChunkBuilder.LedPrefix, StringComparison.Ordinal))
            {
                int colour = int.TryParse(material.Substring(ChunkBuilder.LedPrefix.Length), out int parsed) ? parsed : 0;
                return colour < 1 || colour >= s_Leds.Length || s_Leds[colour] != null ? MaterialNeed.Have : MaterialNeed.Make;
            }

            if (ChunkBuilder.DetailOf(material, out DetailKind kind, out SolidColour solid))
                return NeedOfDetail(kind, solid, false, MarkingColour.White);
            if (material == ChunkBuilder.MetalMaterial)
                return s_Metal != null ? MaterialNeed.Have : MaterialNeed.Make;
            if (material == ChunkBuilder.ConcreteMaterial)
                return s_Concrete != null ? MaterialNeed.Have : MaterialNeed.Make;
            if (material.StartsWith(ChunkBuilder.SolidPrefix, StringComparison.Ordinal))
                return NeedOfSolid(ChunkBuilder.SolidColourOf(material));
            if (ChunkBuilder.IsMarkingMaterial(material))
            {
                MarkingColour paint = ChunkBuilder.MarkingColourOf(material);
                return NeedOfDetail(DetailKind.RoadPaint, PaintSolid(paint), true, paint);
            }

            // A face: a copy of the template (made once), its picture comes later.
            return s_Faces.TryGetValue(material, out Material face) && face != null || s_FaceTemplate != null ? MaterialNeed.Have : MaterialNeed.Make;
        }

        private static MaterialNeed NeedOfSolid(SolidColour colour)
        {
            if (colour == SolidColour.Metal)
                return s_Metal != null ? MaterialNeed.Have : MaterialNeed.Make;
            if (colour == SolidColour.Concrete)
                return s_Concrete != null ? MaterialNeed.Have : MaterialNeed.Make;
            return s_Solids[Mathf.Clamp((int)colour, 0, s_Solids.Length - 1)] != null ? MaterialNeed.Have : MaterialNeed.Make;
        }

        private static MaterialNeed NeedOfDetail(DetailKind kind, SolidColour colour, bool paint, MarkingColour marking)
        {
            if (s_Details.TryGetValue((kind, colour), out Material made) && made != null)
                return MaterialNeed.Have;
            DetailJob job = JobFor(kind, colour);
            if (!job.Done)
                return MaterialNeed.Wait;
            return paint ? (s_Marking[Mathf.Clamp((int)marking, 0, s_Marking.Length - 1)] != null ? MaterialNeed.Have : MaterialNeed.Make) : NeedOfSolid(colour);
        }

        private static DetailJob JobFor(DetailKind kind, SolidColour colour)
        {
            if (s_DetailJobs.TryGetValue((kind, colour), out DetailJob job))
                return job;
            job = new DetailJob { Kind = kind, Colour = colour };
            s_DetailJobs[(kind, colour)] = job;
            s_WarmOrder.Add(job);
            return job;
        }

        // ------------------------------------------------------------------ the stages

        /// <summary>Takes stages of the jobs that are on the way, in the order they were asked for, while the update has spent less than budgetMs; returns the milliseconds it spent. Main thread, every update.</summary>
        internal static double WarmStep(double budgetMs)
        {
            if (s_WarmOrder.Count == 0)
                return 0.0;
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < s_WarmOrder.Count && DetailStages.MayStart(clock.Elapsed.TotalMilliseconds, budgetMs);)
            {
                DetailJob job = s_WarmOrder[i];
                bool partial = false;
                while (!job.Done && DetailStages.MayStart(clock.Elapsed.TotalMilliseconds, budgetMs))
                {
                    double before = clock.Elapsed.TotalMilliseconds;
                    DetailStage stage = job.Stage;
                    StepResult result = Step(job, budgetMs - before, false);
                    if (result == StepResult.Waiting)
                        break;
                    Account(job, stage, clock.Elapsed.TotalMilliseconds - before);
                    if (result == StepResult.Partial)
                    {
                        partial = true;
                        break;
                    }
                }

                if (job.Done)
                {
                    s_WarmOrder.Remove(job);
                    FinishLog(job);
                    continue;
                }

                if (partial)
                    break;
                i++;
            }

            return clock.Elapsed.TotalMilliseconds;
        }

        private static void Account(DetailJob job, DetailStage stage, double ms)
        {
            job.Ms += ms;
            job.Stages++;
            s_WarmMs += ms;
            if (ms > job.WorstMs)
            {
                job.WorstMs = ms;
                job.WorstStage = stage;
            }

            if (ms > s_WarmWorstMs)
            {
                s_WarmWorstMs = ms;
                s_WarmWorstWhat = $"{job.Kind} {job.Colour} {stage}";
            }
        }

        private static void FinishLog(DetailJob job)
        {
            s_WarmMade++;
            if (s_WarmLogged++ < 30)
                Mod.log.Info($"Materials: the detail material {job.Kind} {job.Colour} {(job.Result != null ? "was made" : "could not be made (drawn plain)")} in {job.Stages} stage(s), {job.Ms:0.0} ms of the main thread, the longest stage {job.WorstStage} {job.WorstMs:0.0} ms.");
        }

        /// <summary>The detail material of a kind in a colour made whole, as before the stages: the job is taken to its end here; null when the game's material is not there or the surface could not be baked.</summary>
        private static Material FinishNow(DetailKind kind, SolidColour colour)
        {
            DetailJob job = JobFor(kind, colour);
            var clock = Stopwatch.StartNew();
            int guard = 0;
            while (!job.Done)
            {
                if (++guard > 64)
                    throw new InvalidOperationException($"The detail material {kind} {colour} did not come to an end.");
                DetailStage stage = job.Stage;
                double before = clock.Elapsed.TotalMilliseconds;
                Step(job, double.MaxValue, true);
                Account(job, stage, clock.Elapsed.TotalMilliseconds - before);
            }

            if (s_WarmOrder.Remove(job))
                FinishLog(job);
            return job.Result;
        }

        private static StepResult Step(DetailJob job, double remainingMs, bool block)
        {
            try
            {
                return StepStage(job, remainingMs, block);
            }
            catch (Exception error)
            {
                Fail(job, error);
                return StepResult.Advanced;
            }
        }

        private static StepResult StepStage(DetailJob job, double remainingMs, bool block)
        {
            switch (job.Stage)
            {
                case DetailStage.Prepare:
                {
                    Material template = DetailTemplate();
                    if (template == null)
                        return End(job);
                    BakeDetails();
                    if (!block && !s_Bakes.IsCompleted)
                        return StepResult.Waiting;
                    DetailSurface surface = Surface(job.Kind);
                    if (surface != null && surface.Alpha != null)
                        template = s_CutoutTemplate;
                    if (template == null || surface == null)
                        return End(job);
                    job.Template = template;
                    job.Surface = surface;
                    job.Holes = surface.Alpha != null && DetailSurfaces.HasHoles(surface.Kind);
                    Color rgb = SolidRgb(job.Colour).gamma;
                    float red = rgb.r, green = rgb.g, blue = rgb.b;
                    SolidColour solid = job.Colour;
                    bool foliage = surface.Kind == DetailKind.Foliage;
                    job.Pixels = Task.Run(() => ToColors(foliage ? DetailPixels.FoliageColour(surface, solid) : DetailPixels.Colour(surface, red, green, blue)));
                    if (job.Holes)
                        job.Coverage = Task.Run(() => DetailSurfaces.CoverageMips(surface.Alpha, surface.Width, surface.Height, kCutoff));
                    if (!MapsMade(job.Kind) && !s_MapPixels.ContainsKey(job.Kind))
                    {
                        s_MapPixels[job.Kind] = Task.Run(() =>
                        {
                            DetailPixels.Maps(surface, out byte[] normal, out byte[] mask);
                            return (ToColors(normal), ToColors(mask));
                        });
                    }

                    return Advance(job);
                }

                case DetailStage.Colour:
                    if (!block && !job.Pixels.IsCompleted)
                        return StepResult.Waiting;
                    job.ColourTexture = Texture(job.Surface, "colour", job.Pixels.Result, false);
                    job.Pixels = null;
                    return Advance(job);

                case DetailStage.Holes:
                {
                    if (!block && !job.Coverage.IsCompleted)
                        return StepResult.Waiting;
                    List<float[]> alphas = job.Coverage.Result;
                    Texture2D texture = job.ColourTexture;
                    var clock = Stopwatch.StartNew();
                    while (job.Level < texture.mipmapCount && job.Level < alphas.Count)
                    {
                        Color32[] mip = texture.GetPixels32(job.Level);
                        float[] alpha = alphas[job.Level];
                        for (int k = 0; k < mip.Length && k < alpha.Length; k++)
                            mip[k].a = Byte(alpha[k]);
                        texture.SetPixels32(mip, job.Level);
                        job.Level++;
                        if (!block && clock.Elapsed.TotalMilliseconds >= remainingMs && job.Level < texture.mipmapCount && job.Level < alphas.Count)
                            return StepResult.Partial;
                    }

                    texture.Apply(false, false);
                    job.Coverage = null;
                    return Advance(job);
                }

                case DetailStage.Compress:
                    job.ColourTexture.Compress(true);
                    return Advance(job);

                case DetailStage.Normal:
                {
                    if (MapsMade(job.Kind))
                        return Advance(job);
                    if (s_MapOwners.TryGetValue(job.Kind, out DetailJob owner) && owner != job && !owner.Done)
                    {
                        if (!block)
                            return StepResult.Waiting;
                        // Another colour of the kind is making the maps: it is taken to the end of them first (a material made whole waits for nobody).
                        while (!owner.Done && !MapsMade(job.Kind) && owner.Stage <= DetailStage.Mask)
                            Step(owner, double.MaxValue, true);
                        if (MapsMade(job.Kind))
                            return Advance(job);
                    }

                    Task<(Color32[] Normal, Color32[] Mask)> pixels = s_MapPixels[job.Kind];
                    if (!block && !pixels.IsCompleted)
                        return StepResult.Waiting;
                    s_MapOwners[job.Kind] = job;
                    (Color32[] normal, Color32[] mask) = pixels.Result;
                    job.NormalTexture = Texture(job.Surface, "normal", normal, true);
                    job.MaskPixels = mask;
                    return Advance(job);
                }

                case DetailStage.Mask:
                    s_DetailMaps[job.Kind] = (job.NormalTexture, Texture(job.Surface, "mask", job.MaskPixels, true));
                    s_MapPixels.Remove(job.Kind);
                    s_MapOwners.Remove(job.Kind);
                    job.MaskPixels = null;
                    job.NormalTexture = null;
                    return Advance(job);

                case DetailStage.Material:
                    return MakeDetailMaterial(job);

                default:
                    job.Done = true;
                    return StepResult.Advanced;
            }
        }

        private static StepResult Advance(DetailJob job)
        {
            job.Stage = DetailStages.Next(job.Stage, job.Holes, MapsMade(job.Kind));
            return StepResult.Advanced;
        }

        private static StepResult End(DetailJob job)
        {
            job.Done = true;
            job.Stage = DetailStage.Done;
            return StepResult.Advanced;
        }

        private static StepResult MakeDetailMaterial(DetailJob job)
        {
            DetailKind kind = job.Kind;
            SolidColour colour = job.Colour;
            DetailSurface surface = job.Surface;
            Material m = null;
            try
            {
                MadeCount++;
                m = new Material(job.Template);
                m.name = $"CDLTS detail {kind} {colour}";
                m.hideFlags = HideFlags.DontSave;
                // White, held to the ceiling of a near-white surface's light (AlbedoCap: the baked colour is the number itself as light, brighter than the plain route's).
                if (m.HasProperty("_BaseColor"))
                    m.SetVector("_BaseColor", DetailBase(colour));
                // Fully in: the game's batches feed the fade per instance, a plain renderer gets none (Bus Driver's wraps).
                m.SetFloat("colossal_LodFade", 1f);
                PrepareDecals(m);
                if (surface.Alpha != null && m.HasProperty("_AlphaCutoff"))
                    m.SetFloat("_AlphaCutoff", kCutoff);
                s_DetailColours[(kind, colour)] = job.ColourTexture;
                ApplyDetail(m, kind, colour);
                try
                {
                    HDMaterial.ValidateMaterial(m);
                }
                catch (Exception ex)
                {
                    Mod.log.Warn("Materials: ValidateMaterial failed for the detail material " + m.name + ": " + ex.Message);
                }

                m.renderQueue = Queue;
                SettleDecals(m, "detail");
                s_Details[(kind, colour)] = m;
                job.Result = m;
                job.Done = true;
                job.Stage = DetailStage.Done;
                return StepResult.Advanced;
            }
            catch (Exception error)
            {
                if (m != null)
                    ReleaseFailedMaterial(m, error);
                throw;
            }
        }

        private static void ReleaseFailedMaterial(Material m, Exception error)
        {
            try { CleanupFailed(m, error); }
            catch (AggregateException) { }
        }

        /// <summary>A stage threw: the job is over, the material is drawn plain (Detail falls back to the solid of the colour) as it was when the one call threw, and what the job made and kept nowhere is released.</summary>
        private static void Fail(DetailJob job, Exception error)
        {
            job.Done = true;
            job.Stage = DetailStage.Done;
            job.Result = null;
            Mod.log.Warn($"Materials: the detail material {job.Kind} {job.Colour} could not be made, drawn plain: {error.GetBaseException().Message}");
            var failures = new List<Exception>();
            ReleaseJob(job, failures);
            if (s_MapOwners.TryGetValue(job.Kind, out DetailJob owner) && owner == job)
                s_MapOwners.Remove(job.Kind);
        }

        /// <summary>What a job made and kept nowhere is destroyed: its colour map unless the material took it, its normal map unless the kind's maps took it.</summary>
        private static void ReleaseJob(DetailJob job, List<Exception> failures)
        {
            if (job.ColourTexture != null && !s_DetailColours.ContainsValue(job.ColourTexture))
                DropOwned(job.ColourTexture, failures);
            if (job.NormalTexture != null)
                DropOwned(job.NormalTexture, failures);
            job.ColourTexture = null;
            job.NormalTexture = null;
            job.Pixels = null;
            job.Coverage = null;
            job.MaskPixels = null;
        }

        /// <summary>A city was left: the jobs that are not at their end are dropped with what they made (the materials that are made stay, as they always did).</summary>
        private static void ClearWarm(List<Exception> failures)
        {
            foreach (DetailJob job in s_WarmOrder)
            {
                if (job.Done)
                    continue;
                job.Done = true;
                job.Stage = DetailStage.Done;
                ReleaseJob(job, failures);
                s_DetailJobs.Remove((job.Kind, job.Colour));
            }

            s_WarmOrder.Clear();
            s_MapOwners.Clear();
        }
    }
}
