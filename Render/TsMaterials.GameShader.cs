using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CDLC.TS.Render
{
    /// <summary>
    /// This mod's own HDRP/Lit materials drawn with the game's shader-graph material (0.12.83; Geo/GameShader.cs has the modes and the reasons): the posts, beams and frames, the faces, and what glows,
    /// each when its family is routed at the mode. A material is moved where it stands (its shader and properties are changed, the object stays), so that every renderer that holds it follows at once
    /// and no tile is rebuilt; the same function puts it back. The recipe is the baked details' (TsMaterials.Detail.cs): the template's properties, the maps carrying colour, metallic and smoothness, a
    /// flat normal, the decal layer Vehicles, ValidateMaterial, the queue. Main thread. Nothing here has run in the game: the look of the game's shader on a plain colour, and whether its decals leave
    /// the posts alone, are for the owner's eyes (the experiment file's gameShader line, live).
    /// </summary>
    internal static partial class TsMaterials
    {
        /// <summary>What a material made by MakeLit was asked to be: HDRP/Lit's colour and scalars, kept so that it can go to the game's shader and come back.</summary>
        private sealed class LitSpec
        {
            public Color Colour;
            public float Metallic, Smoothness;
        }

        private static FeatureState? s_GameShaderLine;
        private static GameShaderMode s_GameMode = GameShaderMode.Off;
        private static bool s_GameBroken, s_GameShaderLogged;
        private static Settled s_FaceTemplateEntry;
        private static readonly Dictionary<int, Texture2D> s_PlainMasks = new Dictionary<int, Texture2D>(), s_GameColours = new Dictionary<int, Texture2D>();

        /// <summary>What is drawn with the game's shader now (the wanted mode, less what the template cannot do, off after a failure).</summary>
        internal static GameShaderMode GameMode => s_GameMode;

        /// <summary>The preferences' gameShader line (null when the file has none), read at the mod's loading, before any material is made.</summary>
        internal static void SetGameShaderPreference(FeatureState? line) => s_GameShaderLine = line;

        /// <summary>
        /// Works out the mode from the experiment file and the preferences, against what the game's template material can do, and when it changed moves the materials that exist (apply) or only takes it
        /// (at the start, before any material is). A missing template, a missing property and a failure to move one each end in a line in the log and in HDRP/Lit for what cannot move.
        /// </summary>
        private static void RefreshGameMode(bool apply)
        {
            GameShaderMode wanted = GameShaderPolicy.Resolve(s_GameShaderLine, s_Experiment.GameShader);
            GameShaderMode next = GameShaderMode.Off;
            if (wanted != GameShaderMode.Off && !s_GameBroken)
            {
                Material template = DetailTemplate();
                if (template == null)
                {
                    Mod.log.Warn($"Materials: game shader {GameShaderRoute.Name(wanted)} wanted, but the game's material library has no opaque material of its default shader: HDRP/Lit stays.");
                }
                else
                {
                    next = GameShaderNeeds.Allowed(wanted, template.HasProperty);
                    if (next != wanted)
                        Mod.log.Warn($"Materials: game shader {GameShaderRoute.Name(wanted)} wanted; the template \"{template.name}\" ({template.shader.name}) lacks {string.Join(", ", GameShaderNeeds.Missing(wanted, template.HasProperty))}: {GameShaderRoute.Name(next)} is used.");
                    LogGameShader(template);
                }
            }

            if (next == s_GameMode)
                return;
            s_GameMode = next;
            if (!apply)
            {
                Mod.log.Info($"Materials: game shader {GameShaderRoute.Name(next)} (read at the start).");
                return;
            }

            SwapAll();
        }

        /// <summary>Once: the game's shader's properties and the template's keywords and the values that matter, so that the next log says what this version had to guess.</summary>
        private static void LogGameShader(Material template)
        {
            if (s_GameShaderLogged)
                return;
            s_GameShaderLogged = true;
            Shader shader = template.shader;
            var sb = new StringBuilder();
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(shader.GetPropertyName(i)).Append('(').Append(TypeLetter(shader.GetPropertyType(i))).Append(')');
            }

            var values = new StringBuilder();
            foreach (string name in new[] { "_Metallic", "_Smoothness", "_EmissiveIntensity", "_EmissiveExposureWeight", "colossal_LodFade", "_NormalScale", "_CoatStrength" })
            {
                if (template.HasProperty(name))
                    values.Append(' ').Append(name).Append('=').Append(template.GetFloat(name).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            foreach (string name in new[] { "_BaseColor", "_EmissiveColor" })
            {
                if (template.HasProperty(name))
                {
                    Vector4 v = template.GetVector(name);
                    values.Append(' ').Append(name).Append("=(").Append(v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(v.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                        .Append(v.z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(v.w.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(')');
                }
            }

            Mod.log.Info($"Materials: the game's shader {shader.name} has {count} properties: {sb}. Template \"{template.name}\": queue {template.renderQueue}, instancing {template.enableInstancing}, keywords [{string.Join(" ", template.shaderKeywords)}], values{values}.");
        }

        private static char TypeLetter(UnityEngine.Rendering.ShaderPropertyType type)
        {
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color: return 'C';
                case UnityEngine.Rendering.ShaderPropertyType.Vector: return 'V';
                case UnityEngine.Rendering.ShaderPropertyType.Float: return 'F';
                case UnityEngine.Rendering.ShaderPropertyType.Range: return 'R';
                case UnityEngine.Rendering.ShaderPropertyType.Texture: return 'T';
                default: return 'I';
            }
        }

        /// <summary>Moves every material that exists to where the mode says (a plan from the registry), and logs what went each way; a failure to move one puts everything back to HDRP/Lit for the session.</summary>
        private static void SwapAll()
        {
            var entries = new List<GameShaderEntry>(s_Settled.Count);
            var live = new List<Settled>(s_Settled.Count);
            foreach (Settled e in s_Settled)
            {
                // A material with no spec (a face cloned from a template that was dropped) cannot be moved either way.
                if (e.Material == null || e.Family == null || e.Spec == null)
                    continue;
                entries.Add(new GameShaderEntry { Family = e.Family, Lit = IsLit(e), OnGame = e.OnGame });
                live.Add(e);
            }

            var toGame = new List<int>();
            var toHdrp = new List<int>();
            GameShaderSwap.Plan(s_GameMode, entries, toGame, toHdrp);
            foreach (int i in toHdrp)
                ToHdrp(live[i]);
            var done = new List<int>(toGame.Count);
            foreach (int i in toGame)
            {
                if (!ToGame(live[i]))
                {
                    GameShaderFailed();
                    return;
                }

                done.Add(i);
            }

            Mod.log.Info("Materials: " + GameShaderSwap.Describe(s_GameMode, entries, done, toHdrp) + ".");
        }

        /// <summary>A material could not be moved: the game's shader is off for the session and what moved goes back.</summary>
        private static void GameShaderFailed()
        {
            s_GameBroken = true;
            s_GameMode = GameShaderMode.Off;
            Mod.log.Warn("Materials: the game's shader is off for this session: a material could not be moved; the ones that were go back to HDRP/Lit.");
            SwapAll();
        }

        /// <summary>A material just registered (SettleDecals): moved when the mode routes its family.</summary>
        private static void RouteNew(Settled e)
        {
            if (s_GameMode == GameShaderMode.Off || e.Spec == null)
                return;
            bool want = GameShaderRoute.Moves(s_GameMode, e.Family, IsLit(e));
            if (want == e.OnGame)
                return;
            if (!want)
                ToHdrp(e);
            else if (!ToGame(e))
                GameShaderFailed();
        }

        /// <summary>A face lit from inside (its material is named "CDLTS " and the face's key, which s_LitFaces holds).</summary>
        private static bool IsLit(Settled e) =>
            e.Family == "face" && e.Material != null && e.Material.name.StartsWith("CDLTS ", StringComparison.Ordinal) && s_LitFaces.Contains(e.Material.name.Substring(6));

        /// <summary>
        /// The maps a plain material holds on the game's shader are made once and kept for the session: the solids and the steel are kept across cities (Clear leaves them), so their maps must be (s_Flat's
        /// textures are dropped with the faces). A few dozen of 4 by 4 pixels.
        /// </summary>
        private static Texture GameColour(byte r, byte g, byte b)
        {
            int key = (r << 16) | (g << 8) | b;
            if (s_GameColours.TryGetValue(key, out Texture2D colour) && colour != null)
                return colour;
            colour = Solid4(new Color32(r, g, b, 255), false, "CDLTS plain colour " + key.ToString("x6"));
            s_GameColours[key] = colour;
            return colour;
        }

        private static Texture PlainMask(float metallic, float smoothness)
        {
            GameShaderLook.MaskBytes(metallic, smoothness, out byte metal, out byte smooth);
            int key = (metal << 8) | smooth;
            if (s_PlainMasks.TryGetValue(key, out Texture2D mask) && mask != null)
                return mask;
            mask = Solid4(new Color32(metal, 0, 0, smooth), true, $"CDLTS plain mask {metal} {smooth}");
            s_PlainMasks[key] = mask;
            return mask;
        }

        /// <summary>
        /// HDRP/Lit to the game's material: the shader, the template's properties and keywords, then ours. A plain colour goes into its colour map (the same numbers HDRP/Lit was given, GameShaderLook) with
        /// _BaseColor white; a face keeps its picture and its night factor in _BaseColor. False when anything throws (the caller turns the mode off).
        /// </summary>
        private static bool ToGame(Settled e)
        {
            Material m = e.Material, template = DetailTemplate();
            try
            {
                bool face = e.Family == "face";
                Texture picture = face && m.HasProperty("_BaseColorMap") ? m.GetTexture("_BaseColorMap") : null;
                Vector4 factor = face && m.HasProperty("_BaseColor") ? m.GetVector("_BaseColor") : Vector4.one;
                Vector4 emission = m.HasProperty("_EmissiveColor") ? m.GetVector("_EmissiveColor") : Vector4.zero;
                m.shader = template.shader;
                e.OnGame = true;
                m.CopyPropertiesFromMaterial(template);
                ResetKeywords(m, template.shaderKeywords);
                m.enableInstancing = template.enableInstancing;
                m.SetVector("_BaseColor", face ? factor : PlainBase(e.Spec.Colour));
                if (face)
                {
                    m.SetTexture("_BaseColorMap", picture != null ? picture : Texture2D.whiteTexture);
                }
                else
                {
                    GameShaderLook.ColourBytes(e.Spec.Colour.r, e.Spec.Colour.g, e.Spec.Colour.b, out byte red, out byte green, out byte blue);
                    m.SetTexture("_BaseColorMap", GameColour(red, green, blue));
                }

                m.SetTexture("_MaskMap", PlainMask(e.Spec.Metallic, e.Spec.Smoothness));
                m.SetTexture("_NormalMap", FlatNormal());
                if (m.HasProperty("_EmissiveColorMap"))
                    m.SetTexture("_EmissiveColorMap", Texture2D.whiteTexture);
                if (m.HasProperty("_EmissiveIntensity"))
                    m.SetFloat("_EmissiveIntensity", 1f);
                if (m.HasProperty("_EmissiveExposureWeight"))
                    m.SetFloat("_EmissiveExposureWeight", 1f);
                if (m.HasProperty("colossal_LodFade"))
                    m.SetFloat("colossal_LodFade", 1f);
                PrepareDecals(m);
                Validate(m);
                m.renderQueue = s_Experiment.Queue ?? Queue;
                ApplyKeyword(m, logFix: false);
                // After ValidateMaterial, as the LED and the lantern factories write it: validation may work the emission out of other properties.
                if (m.HasProperty("_EmissiveColor"))
                    m.SetVector("_EmissiveColor", emission);
                NoteSwap(e);
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"Materials: \"{m.name}\" could not be moved to the game's shader: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// The game's material back to HDRP/Lit as MakeLit made it: the plain colour and scalars from the spec, a face's picture and factor as they are now, the maps of the game's shader taken out (HDRP/Lit
        /// shares their names, and a mask map or a normal map on it would ask for variants the game's build does not have). A failure is logged and the material is left as it is; it is not a reason to stop.
        /// </summary>
        private static void ToHdrp(Settled e)
        {
            Material m = e.Material;
            try
            {
                bool face = e.Family == "face";
                Texture picture = face && m.HasProperty("_BaseColorMap") ? m.GetTexture("_BaseColorMap") : null;
                Vector4 factor = face && m.HasProperty("_BaseColor") ? m.GetVector("_BaseColor") : Vector4.zero;
                Vector4 emission = m.HasProperty("_EmissiveColor") ? m.GetVector("_EmissiveColor") : Vector4.zero;
                Shader lit = s_Shader != null ? s_Shader : (s_Shader = Shader.Find("HDRP/Lit"));
                m.shader = lit;
                e.OnGame = false;
                ResetKeywords(m, Array.Empty<string>());
                m.enableInstancing = true;
                foreach (string map in new[] { "_MaskMap", "_NormalMap", "_EmissiveColorMap" })
                {
                    if (m.HasProperty(map))
                        m.SetTexture(map, null);
                }

                if (face)
                    m.SetVector("_BaseColor", factor);
                else
                    m.SetColor("_BaseColor", e.Spec.Colour);
                if (m.HasProperty("_Color") && !face)
                    m.SetColor("_Color", e.Spec.Colour);
                if (m.HasProperty("_BaseColorMap"))
                    m.SetTexture("_BaseColorMap", picture);
                if (m.HasProperty("_Metallic"))
                    m.SetFloat("_Metallic", e.Spec.Metallic);
                if (m.HasProperty("_Smoothness"))
                    m.SetFloat("_Smoothness", e.Spec.Smoothness);
                PrepareDecals(m);
                Validate(m);
                m.renderQueue = s_Experiment.Queue ?? Queue;
                ApplyKeyword(m, logFix: false);
                if (m.HasProperty("_EmissiveColor"))
                    m.SetVector("_EmissiveColor", emission);
                NoteSwap(e);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"Materials: \"{m.name}\" could not be put back to HDRP/Lit: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static int s_ValidateFailures;

        /// <summary>ValidateMaterial as the baked details call it: a failure is a line in the log (the first few), not the end of the move.</summary>
        private static void Validate(Material m)
        {
            try
            {
                HDMaterial.ValidateMaterial(m);
            }
            catch (Exception ex)
            {
                if (s_ValidateFailures++ < 3)
                    Mod.log.Warn($"Materials: ValidateMaterial failed for \"{m.name}\" on {m.shader.name}: {ex.Message}");
            }
        }

        private static void ResetKeywords(Material m, string[] keep)
        {
            foreach (string keyword in m.shaderKeywords)
                m.DisableKeyword(keyword);
            foreach (string keyword in keep)
                m.EnableKeyword(keyword);
        }

        /// <summary>The audit's line for the first material of a family in each state it is moved to (the shader, the layer, the keyword, the queue).</summary>
        private static void NoteSwap(Settled e)
        {
            string line = s_Audit.Note(e.Family, Snapshot(e.Material));
            if (line != null)
                Mod.log.Info(line);
        }

    }
}
