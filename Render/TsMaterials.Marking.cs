using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CDLC.TS.Geo;
using UnityEngine;

namespace CDLC.TS.Render
{
    /// <summary>
    /// The experiment of the road paint (0.12.83, Geo/MarkingExperiment.cs; ModsData/CDLC.TS/marking-experiment.txt, looked at once a second from ChunkSystem): the grain and the smoothness of the paint
    /// of the markings and whether its normals follow the ground, changed while the game runs. The paint of a marking is the baked detail of DetailKind.RoadPaint on the game's own shader (a normal map of
    /// the stones under the paint, 1.7 degrees on average, and a mask map with a smoothness of 0.2 on average) wherever that material was found; the plain HDRP/Lit paint (Marking) is the fallback, which has
    /// no normal map (the game's build of that shader has no variant with one). The grain and the smoothness are put on the paint's materials as other maps of the same surface (PaintMaps) and taken off again when
    /// the file goes; the paint of a game without the baked detail takes the smoothness as a number. The normals are the meshes' (SurfaceNormals): a change builds every tile again. Main thread.
    /// </summary>
    internal static partial class TsMaterials
    {
        private const string kMarkingFile = "marking-experiment.txt";

        private static string s_MarkingPath;
        private static DateTime s_MarkingStamp = DateTime.MaxValue;
        private static MarkingExperiment s_MarkingNow = MarkingExperiment.None;
        private static double s_NextMarkingWatch;
        private static bool s_MarkingSaid, s_PaintTouched;
        private static int s_MarkingFailures;
        private static float s_GrainOf;
        private static float s_SmoothOf = -1f;
        private static Texture2D s_GrainMap, s_SmoothMap;

        /// <summary>Once a second from ChunkSystem: the experiment file's stamp is looked at, the paint's materials are kept as it says (a colour made after it changed gets the same), and what the paint is drawn with is said once.</summary>
        internal static void PumpMarking()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < s_NextMarkingWatch)
                return;
            s_NextMarkingWatch = now + 1.0;
            try
            {
                CheckMarkingFile();
                KeepPaint(out _, out _);
                SayPaint();
            }
            catch (Exception ex)
            {
                if (s_MarkingFailures++ < 3)
                    Mod.log.Warn("Materials: the marking experiment failed: " + ex.Message);
            }
        }

        private static void CheckMarkingFile()
        {
            if (s_MarkingPath == null)
            {
                s_MarkingPath = Path.Combine(Application.persistentDataPath, "ModsData", Mod.kModName, kMarkingFile);
                WriteMarkingExample();
            }

            DateTime stamp = File.Exists(s_MarkingPath) ? File.GetLastWriteTimeUtc(s_MarkingPath) : DateTime.MinValue;
            if (stamp == s_MarkingStamp)
                return;
            var problems = new List<string>();
            MarkingExperiment next;
            try
            {
                next = stamp == DateTime.MinValue ? MarkingExperiment.None : MarkingExperiment.Parse(File.ReadAllText(s_MarkingPath), problems);
            }
            catch (IOException ex)
            {
                Mod.log.Warn($"Materials: {kMarkingFile} could not be read ({ex.Message}); looked at again in a second.");
                return;
            }

            bool atStart = s_MarkingStamp == DateTime.MaxValue;
            s_MarkingStamp = stamp;
            foreach (string problem in problems)
                Mod.log.Warn($"Materials: {kMarkingFile}, {problem}; the line is left out.");
            if (next.Equals(s_MarkingNow))
                return;
            s_MarkingNow = next;
            int tiles = 0;
            if (next.NormalsOn != SurfaceNormals.Enabled)
            {
                SurfaceNormals.Enabled = next.NormalsOn;
                tiles = ChunkSystem.Instance != null ? ChunkSystem.Instance.RebuildAll() : 0;
            }

            KeepPaint(out int detail, out int plain);
            Mod.log.Info($"Materials: {kMarkingFile} {(atStart ? "is read at the start" : "changed")}: {next.Describe()}; the paint's normals follow the ground: {(SurfaceNormals.Enabled ? "yes" : "no")}"
                + (tiles > 0 ? $", {tiles} tile(s) are built again" : "") + $"; {detail} baked-detail and {plain} plain paint material(s) given it.");
        }

        private static void WriteMarkingExample()
        {
            try
            {
                string example = Path.Combine(Path.GetDirectoryName(s_MarkingPath), "marking-experiment.example.txt");
                if (!File.Exists(s_MarkingPath) && !File.Exists(example))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(example));
                    File.WriteAllText(example, MarkingExperiment.Example());
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Mod.log.Warn("Materials: the example of the marking experiment could not be written: " + ex.Message);
            }
        }

        private static bool IsPaintDetail(KeyValuePair<(DetailKind, SolidColour), Material> pair) =>
            pair.Key.Item1 == DetailKind.RoadPaint && pair.Value != null && pair.Value.name.StartsWith("CDLTS detail", StringComparison.Ordinal);

        /// <summary>
        /// The paint's materials as the experiment says: the baked-detail ones with the normal map of the grain asked for and the mask map of the smoothness asked for (the baked maps when it asks for
        /// neither), the plain ones with the smoothness as a number. Gives how many of each were changed. Nothing is touched while the experiment asks for nothing and never did, and while the
        /// panel's test switch has the detail off (its flat maps are not ours to replace).
        /// </summary>
        private static void KeepPaint(out int detail, out int plain)
        {
            detail = plain = 0;
            MarkingExperiment e = s_MarkingNow;
            bool wanted = e.ChangesMaps;
            if (DetailOn && (wanted || s_PaintTouched))
            {
                var paint = new List<Material>();
                foreach (KeyValuePair<(DetailKind, SolidColour), Material> pair in s_Details)
                {
                    if (IsPaintDetail(pair))
                        paint.Add(pair.Value);
                }

                if (paint.Count > 0)
                {
                    DetailSurface surface = Surface(DetailKind.RoadPaint);
                    (Texture2D bakedNormal, Texture2D bakedMask) = DetailMaps(DetailKind.RoadPaint);
                    Texture2D oldGrain = s_GrainMap, oldSmooth = s_SmoothMap;
                    Texture2D normal = bakedNormal, mask = bakedMask;
                    if (e.GrainScale != 1f && surface != null)
                    {
                        if (s_GrainMap == null || s_GrainOf != e.GrainScale)
                        {
                            s_GrainMap = Texture(surface, "grain x" + e.GrainScale.ToString("0.##", CultureInfo.InvariantCulture), ToColors(PaintMaps.Grain(surface, e.GrainScale)), true);
                            s_GrainOf = e.GrainScale;
                        }

                        normal = s_GrainMap;
                    }
                    else
                    {
                        s_GrainMap = null;
                    }

                    if (e.Smoothness.HasValue && surface != null)
                    {
                        if (s_SmoothMap == null || s_SmoothOf != e.Smoothness.Value)
                        {
                            s_SmoothMap = Texture(surface, "smoothness " + e.Smoothness.Value.ToString("0.##", CultureInfo.InvariantCulture), ToColors(PaintMaps.Smoothness(surface, e.Smoothness.Value)), true);
                            s_SmoothOf = e.Smoothness.Value;
                        }

                        mask = s_SmoothMap;
                    }
                    else
                    {
                        s_SmoothMap = null;
                    }

                    foreach (Material m in paint)
                        detail += Point(m, "_NormalMap", normal) + Point(m, "_MaskMap", mask);
                    if (oldGrain != null && oldGrain != s_GrainMap)
                        UnityEngine.Object.Destroy(oldGrain);
                    if (oldSmooth != null && oldSmooth != s_SmoothMap)
                        UnityEngine.Object.Destroy(oldSmooth);
                    s_PaintTouched = wanted;
                }
            }

            float smooth = e.PlainSmooth;
            foreach (Material m in s_Marking)
            {
                if (m != null && m.HasProperty("_Smoothness") && Mathf.Abs(m.GetFloat("_Smoothness") - smooth) > 1e-6f)
                {
                    m.SetFloat("_Smoothness", smooth);
                    plain++;
                }
            }
        }

        private static int Point(Material m, string property, Texture2D map)
        {
            if (!m.HasProperty(property) || m.GetTexture(property) == map)
                return 0;
            m.SetTexture(property, map);
            return 1;
        }

        /// <summary>Once, when the first paint material exists: what the paint is drawn with, so that a log says whether the baked grain is on it.</summary>
        private static void SayPaint()
        {
            if (s_MarkingSaid)
                return;
            int detail = 0, plain = 0;
            foreach (KeyValuePair<(DetailKind, SolidColour), Material> pair in s_Details)
            {
                if (IsPaintDetail(pair))
                    detail++;
            }

            foreach (Material m in s_Marking)
            {
                if (m != null)
                    plain++;
            }

            if (detail + plain == 0)
                return;
            s_MarkingSaid = true;
            DetailSurface surface = detail > 0 ? Surface(DetailKind.RoadPaint) : null;
            Mod.log.Info($"Materials: the road paint is drawn with {detail} baked-detail material(s) of the game's shader"
                + (surface != null ? $" (a normal map with a grain of {PaintMaps.MeanTilt(surface):0.0} degrees on average, smoothness {PaintMaps.MeanSmooth(surface):0.00} on average, texture coordinates in world metres)" : "")
                + $" and {plain} plain HDRP/Lit material(s) (smoothness {MarkingExperiment.PlainSmoothness:0.00}, no normal map: the game's build of that shader has no variant with one).");
        }
    }
}
