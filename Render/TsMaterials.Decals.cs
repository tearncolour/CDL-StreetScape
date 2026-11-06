using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using UnityEngine;

namespace CDLC.TS.Render
{
    /// <summary>
    /// The decal settings of this mod's materials (0.12.80; Geo/DecalSettings.cs has the policy and the reasons). Each factory writes them before HDMaterial.ValidateMaterial (which turns
    /// _SupportDecals 0 into the keyword _DISABLE_DECALS) with PrepareDecals, and after it, with the queue, calls SettleDecals: the keyword is read back and set by hand when it is not what the
    /// policy wants, the material is audited and kept for the experiment file. All main thread, once per material made, never per frame (PumpDecals does a file stamp a second).
    /// </summary>
    internal static partial class TsMaterials
    {
        private const string kExperimentFile = "material-experiment.txt";

        private sealed class Settled
        {
            public Material Material;
            public int BaseQueue;

            /// <summary>The audit family (solid, face, detail ...), what HDRP/Lit was asked for (null for the game's own materials), and whether this mod moved the material to the game's shader (TsMaterials.GameShader.cs).</summary>
            public string Family;
            public LitSpec Spec;
            public bool OnGame;
        }

        private static readonly List<Settled> s_Settled = new List<Settled>();
        private static Settled s_LastEntry;
        private static readonly DecalAudit s_Audit = new DecalAudit();
        private static MaterialExperiment s_Experiment = MaterialExperiment.None;
        private static string s_ExperimentPath;
        private static DateTime s_ExperimentStamp = DateTime.MinValue;
        private static double s_NextWatch, s_LastSettled;
        private static bool s_Reported = true, s_ExperimentRead;
        private static int s_KeywordFixed;
        private static int s_PruneAt = 4096;

        /// <summary>How many materials needed their _DISABLE_DECALS keyword set by hand after ValidateMaterial (0 when ValidateMaterial does what the policy wants).</summary>
        internal static int KeywordFixed => s_KeywordFixed;

        /// <summary>Written before ValidateMaterial: the properties it reads. The layer only where the material has the property (the game's shader graphs; HDRP/Lit has none).</summary>
        private static void PrepareDecals(Material m)
        {
            EnsureExperiment();
            if (m.HasProperty("_SupportDecals"))
                m.SetFloat("_SupportDecals", s_Experiment.SupportDecals ?? DecalPolicy.SupportDecals);
            if (m.HasProperty("colossal_DecalLayerMask"))
                m.SetFloat("colossal_DecalLayerMask", DecalMasks.ToFloat(s_Experiment.DecalMask ?? DecalPolicy.ObjectMask));
        }

        /// <summary>Written after ValidateMaterial and the queue: the keyword is checked, the material audited and kept for the experiment file.</summary>
        private static void SettleDecals(Material m, string family, LitSpec spec = null, bool onGame = false)
        {
            ApplyKeyword(m, logFix: true);
            if (s_Experiment.Queue.HasValue)
                m.renderQueue = s_Experiment.Queue.Value;
            var entry = new Settled { Material = m, BaseQueue = s_Experiment.Queue.HasValue ? Queue : m.renderQueue, Family = family, Spec = spec, OnGame = onGame };
            // Faces are made and dropped all session: the entries of destroyed materials go when the list has grown.
            if (s_Settled.Count >= s_PruneAt)
            {
                s_Settled.RemoveAll(e => e.Material == null);
                s_PruneAt = Math.Max(4096, s_Settled.Count * 2);
            }

            s_Settled.Add(entry);
            s_LastEntry = entry;
            s_LastSettled = Time.realtimeSinceStartupAsDouble;
            RouteNew(entry);
            string line = s_Audit.Note(family, Snapshot(m));
            if (line != null)
            {
                Mod.log.Info(line);
                s_Reported = false;
            }
        }

        private static void ApplyKeyword(Material m, bool logFix)
        {
            bool has = m.HasProperty("_SupportDecals");
            bool? wanted = DecalPolicy.DisableKeywordWanted(has, has ? m.GetFloat("_SupportDecals") : 0f, s_Experiment.DisableDecals);
            if (!wanted.HasValue || m.IsKeywordEnabled(DecalPolicy.DisableKeyword) == wanted.Value)
                return;
            if (wanted.Value)
                m.EnableKeyword(DecalPolicy.DisableKeyword);
            else
                m.DisableKeyword(DecalPolicy.DisableKeyword);
            s_KeywordFixed++;
            if (logFix && s_KeywordFixed <= 8)
                Mod.log.Warn($"Materials: {DecalPolicy.DisableKeyword} was {(wanted.Value ? "off" : "on")} on \"{m.name}\" after ValidateMaterial; set {(wanted.Value ? "on" : "off")} by hand.");
        }

        private static DecalSnapshot Snapshot(Material m)
        {
            bool hasSupport = m.HasProperty("_SupportDecals"), hasMask = m.HasProperty("colossal_DecalLayerMask");
            return new DecalSnapshot
            {
                Shader = m.shader != null ? m.shader.name : "no shader",
                Queue = m.renderQueue,
                HasSupport = hasSupport,
                Support = hasSupport ? m.GetFloat("_SupportDecals") : 0f,
                HasMask = hasMask,
                Mask = hasMask ? DecalMasks.FromFloat(m.GetFloat("colossal_DecalLayerMask")) : 0,
                KeywordOn = m.IsKeywordEnabled(DecalPolicy.DisableKeyword),
                Vt = m.IsKeywordEnabled("ENABLE_VT"),
                Keywords = string.Join(" ", m.shaderKeywords)
            };
        }

        private const string kExample = "# material-experiment.example.txt: copy this file to material-experiment.txt (same folder) and take the # off a line to try it; the game looks at the file once a second.\r\n"
            + "# Deleting material-experiment.txt puts every default back. The log (CDLC.TS.Mod.log) says what each change did: 'Materials: material-experiment.txt changed'.\r\n"
            + "# decalMask = Other           # the layer of the materials that have the property (the baked details and the glass, not the plain posts and faces): layer names (Terrain Roads Buildings Vehicles Creatures Other) joined by |, or 0x40, or none. The default is Vehicles.\r\n"
            + "# supportDecals = 1           # 1: the materials take the decals of the game (then disableDecals = off too), 0: they do not (the default)\r\n"
            + "# disableDecals = off         # the keyword _DISABLE_DECALS: on (the default for materials with supportDecals 0) or off\r\n"
            + "# queue = 2000                # the render queue, 1000 to 5000; the default is 2225 (glass keeps its own)\r\n"
            + "# renderingDecals = none     # the decal layers of the renderers (HDRP/Lit takes its decal layers from bits 8 to 15 of their rendering layer mask): layer names, 0x40, 8 or none. The default is every layer\r\n"
            + "# renderingLayer = 0x8ff      # the renderers' whole rendering layer mask (a number, all or none)\r\n"
            + "# gameShader = faces         # draw this mod's own materials with the game's shader, which has the decal layer: off (HDRP/Lit), plain (posts, beams, frames), faces (and the boards' faces) or lit (and the lit faces, lanterns and neon; the LED dots always stay on HDRP/Lit). The default is lit\r\n"
            + "# whiteMax = 0.72            # how bright a near-white surface may be in light (0.2 to 1): the white of a noise wall, a hoarding, a louvre panel; lower holds them more (try 0.6, then 0.5). The default is 0.72; the markings and the sign faces are not held\r\n";

        private static void EnsureExperiment()
        {
            if (s_ExperimentRead)
                return;
            s_ExperimentRead = true;
            CheckExperimentFile(false);
            RefreshGameMode(false);
            WriteExample();
        }

        /// <summary>Once, when neither file is there: a commented copy of the keys beside where the experiment file goes, so that there is something to copy.</summary>
        private static void WriteExample()
        {
            try
            {
                string example = Path.Combine(Path.GetDirectoryName(s_ExperimentPath), "material-experiment.example.txt");
                if (!File.Exists(s_ExperimentPath) && !File.Exists(example))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(example));
                    File.WriteAllText(example, kExample);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Mod.log.Warn("Materials: the example of the experiment file could not be written: " + ex.Message);
            }
        }

        /// <summary>Every frame from PumpFaces: once a second the experiment file's stamp is looked at, and a few seconds after the last material was made the audit and the census are said once.</summary>
        internal static void PumpDecals()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < s_NextWatch)
                return;
            s_NextWatch = now + 1.0;
            CheckExperimentFile(true);
            if (!s_Reported && s_Settled.Count > 0 && now - s_LastSettled > 6.0)
            {
                s_Reported = true;
                Mod.log.Info(s_Audit.Summary() + $"; {DecalPolicy.DisableKeyword} set by hand on {s_KeywordFixed}; the objects' layer {DecalMasks.Describe(DecalPolicy.ObjectMask)}, renderers' rendering layer mask {DecalMasks.DescribeRendering(MeshUpload.RenderingLayer)}.");
                LogCensus();
            }
        }

        private static void CheckExperimentFile(bool apply)
        {
            if (s_ExperimentPath == null)
                s_ExperimentPath = Path.Combine(Application.persistentDataPath, "ModsData", Mod.kModName, kExperimentFile);
            MaterialExperiment next;
            try
            {
                DateTime stamp = File.Exists(s_ExperimentPath) ? File.GetLastWriteTimeUtc(s_ExperimentPath) : DateTime.MinValue;
                if (stamp == s_ExperimentStamp)
                    return;
                s_ExperimentStamp = stamp;
                var problems = new List<string>();
                next = stamp == DateTime.MinValue ? MaterialExperiment.None : MaterialExperiment.Parse(File.ReadAllText(s_ExperimentPath), problems);
                foreach (string problem in problems)
                    Mod.log.Warn($"Materials: {kExperimentFile}, {problem}; the line is left out.");
            }
            catch (IOException ex)
            {
                Mod.log.Warn($"Materials: {kExperimentFile} could not be read ({ex.Message}); looked at again when its time changes.");
                return;
            }

            if (next.Equals(s_Experiment))
                return;
            s_Experiment = next;
            MeshUpload.RenderingLayer = next.RenderingLayer ?? uint.MaxValue;
            RefreshGameMode(apply);
            if (!apply)
            {
                Mod.log.Info($"Materials: {kExperimentFile} is read at the start: {next.Describe()}.");
                return;
            }

            int materials = ApplyExperiment(), renderers = MeshUpload.ApplyRenderingLayer(), whites = ApplyWhiteCap();
            Mod.log.Info($"Materials: {kExperimentFile} changed: {next.Describe()}; {materials} material(s) and {renderers} of {MeshUpload.RenderersMade} renderer(s) made so far were given it; the ceiling of a near-white surface is {WhiteCeiling:0.00} ({whites} material(s) set).");
            Mod.log.Info("Materials: " + MeshUpload.MaskCensus() + ".");
            if (renderers == 0 && MeshUpload.RenderersMade > 0)
                Mod.log.Warn($"Materials: no renderer is left of the {MeshUpload.RenderersMade} made; a change of the rendering layer reaches no object.");
        }

        /// <summary>The ceiling of a near-white surface's light now (AlbedoCap): the experiment file's whiteMax, else the default.</summary>
        private static float WhiteCeiling => s_Experiment.WhiteMax ?? AlbedoCap.Default;

        /// <summary>The base colour factor of a baked detail: its colour is the number itself as light.</summary>
        private static Vector4 DetailBase(SolidColour colour)
        {
            Color c = SolidRgb(colour);
            float f = AlbedoCap.Factor(c.r, c.g, c.b, WhiteCeiling);
            return new Vector4(f, f, f, 1f);
        }

        /// <summary>The base colour factor of a plain material on the game's shader: its colour is a Color property turned to linear light, as HDRP/Lit takes it.</summary>
        private static Vector4 PlainBase(Color colour)
        {
            Color l = colour.linear;
            float f = AlbedoCap.Factor(l.r, l.g, l.b, WhiteCeiling);
            return new Vector4(f, f, f, 1f);
        }

        /// <summary>The factor of every baked detail and every plain solid, steel and concrete on the game's shader again, after the experiment's whiteMax changed. The markings and the faces are not held.</summary>
        private static int ApplyWhiteCap()
        {
            int done = 0;
            foreach (KeyValuePair<(DetailKind, SolidColour), Material> pair in s_Details)
            {
                Material m = pair.Value;
                if (m == null || !m.name.StartsWith("CDLTS detail", StringComparison.Ordinal) || !m.HasProperty("_BaseColor"))
                    continue;
                m.SetVector("_BaseColor", DetailBase(pair.Key.Item2));
                done++;
            }

            foreach (Settled entry in s_Settled)
            {
                if (entry.Material == null || !entry.OnGame || !(entry.Family == "solid" || entry.Family == "metal" || entry.Family == "concrete") || !entry.Material.HasProperty("_BaseColor"))
                    continue;
                entry.Material.SetVector("_BaseColor", PlainBase(entry.Spec.Colour));
                done++;
            }

            return done;
        }

        private static int ApplyExperiment()
        {
            int mask = s_Experiment.DecalMask ?? DecalPolicy.ObjectMask;
            float support = s_Experiment.SupportDecals ?? DecalPolicy.SupportDecals;
            int done = 0;
            for (int i = s_Settled.Count - 1; i >= 0; i--)
            {
                Settled entry = s_Settled[i];
                if (entry.Material == null)
                {
                    s_Settled.RemoveAt(i);
                    continue;
                }

                Material m = entry.Material;
                if (m.HasProperty("_SupportDecals"))
                    m.SetFloat("_SupportDecals", support);
                if (m.HasProperty("colossal_DecalLayerMask"))
                    m.SetFloat("colossal_DecalLayerMask", DecalMasks.ToFloat(mask));
                ApplyKeyword(m, logFix: false);
                m.renderQueue = s_Experiment.Queue ?? entry.BaseQueue;
                done++;
            }

            return done;
        }

        /// <summary>One line: whether the shaders of this mod's materials declare the keyword _DISABLE_DECALS at all (a keyword the compiled shader lacks cannot be turned on; one it declares may still have no variant).</summary>
        private static void LogKeywordSpaces()
        {
            var seen = new HashSet<Shader>();
            var parts = new List<string>();
            foreach (Settled entry in s_Settled)
            {
                Shader shader = entry.Material != null ? entry.Material.shader : null;
                if (shader == null || !seen.Add(shader))
                    continue;
                string[] names = shader.keywordSpace.keywordNames;
                parts.Add($"{shader.name} {(Array.IndexOf(names, DecalPolicy.DisableKeyword) >= 0 ? "declares" : "does not declare")} it ({names.Length} keywords)");
            }

            Mod.log.Info($"Materials: {DecalPolicy.DisableKeyword} in the shaders' keyword spaces: {string.Join("; ", parts)}.");
        }

        /// <summary>One line: which layers the game's decal materials list (the census), so that the log says whether any lists the layer of this mod's objects.</summary>
        private static void LogCensus()
        {
            try
            {
                var decals = new List<DecalCensus.Item>();
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || m.shader == null || m.shader.name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    decals.Add(new DecalCensus.Item { Mask = m.HasProperty("colossal_DecalLayerMask") ? DecalMasks.FromFloat(m.GetFloat("colossal_DecalLayerMask")) : (int?)null });
                }

                Mod.log.Info(DecalCensus.Format(decals));
                LogKeywordSpaces();
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset asset)
                {
                    var settings = asset.currentPlatformRenderPipelineSettings;
                    Mod.log.Info($"Materials: the pipeline's decals {(settings.supportDecals ? "on" : "off")}, decal layers {(settings.supportDecalLayers ? "on" : "off")}.");
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Materials: the census of the game's decals failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
