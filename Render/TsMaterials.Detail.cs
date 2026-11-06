using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CDLC.TS.Geo;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CDLC.TS.Render
{
    /// <summary>
    /// The materials of the solids with baked detail (0.12.32; the owner: "需要加入法线烘焙细节"): a copy of the game's own opaque object material ("Default", BH/SG_DefaultShader, from its material
    /// library) with the detail surface's colour, normal and mask maps. The game's build of HDRP/Lit has no variant with a normal map (it draws a material that asks for one without it: the same build
    /// had none with an emissive map or alpha), the game's own shader is what draws every object of the game with them; Bus Driver's body wraps are copies of its cut-out material the same way. Without
    /// that material in the library the solids are drawn as before (Solid). The surfaces are baked once, off the main thread, from the mod's loading on (DetailSurfaces); the switch Detail (the panel's
    /// test switches) puts a flat normal map in place of the baked one on the same materials (the colour and the mask stay). Main thread.
    /// </summary>
    internal static partial class TsMaterials
    {
        private const string kDetailTemplateName = "Default", kCutoutTemplateName = "DefaultCutout", kGlassTemplateName = "DefaultGlass";

        /// <summary>The alpha a cut-out surface is cut at (as Bus Driver's wraps, which are cut out right in the game with it).</summary>
        private const float kCutoff = 0.5f;

        private static Task<Dictionary<DetailKind, DetailSurface>> s_Bakes;
        private static readonly Dictionary<DetailKind, (Texture2D Normal, Texture2D Mask)> s_DetailMaps = new Dictionary<DetailKind, (Texture2D, Texture2D)>();
        private static readonly Dictionary<(DetailKind, SolidColour), Material> s_Details = new Dictionary<(DetailKind, SolidColour), Material>();
        private static readonly Dictionary<(DetailKind, SolidColour), Texture2D> s_DetailColours = new Dictionary<(DetailKind, SolidColour), Texture2D>();
        private static Texture2D s_FlatNormal, s_GlassMask;
        private static Material s_DetailTemplate, s_CutoutTemplate, s_GlassTemplate;
        private static bool s_DetailLooked;

        /// <summary>Whether the detail maps are on (the panel's test switch; on by default).</summary>
        internal static bool DetailOn { get; private set; } = true;

        /// <summary>What the detail materials are made from, for the log and the panel: the game's material, or why not.</summary>
        internal static string DetailStatus { get; private set; } = "not used yet";

        /// <summary>Starts baking the detail surfaces off the main thread (at the mod's loading: they are ready long before a city is).</summary>
        internal static void BakeDetails()
        {
            if (s_Bakes != null)
                return;
            s_Bakes = Task.Run(() =>
            {
                var all = new Dictionary<DetailKind, DetailSurface>();
                foreach (DetailKind kind in (DetailKind[])Enum.GetValues(typeof(DetailKind)))
                {
                    DetailSurface surface = DetailSurfaces.Make(kind);
                    if (surface != null)
                        all[kind] = surface;
                }

                return all;
            });
        }

        /// <summary>The material of a solid of the colour with the kind's detail; the plain one (Solid) when the game's material is not there or a surface could not be baked.</summary>
        internal static Material Detail(DetailKind kind, SolidColour colour) => MakeDetail(kind, colour) ?? (s_Details[(kind, colour)] = Solid(colour));

        /// <summary>The paint of a marking (0.12.40): the road paint's detail in its colour; HDRP/Lit's flat colour (Marking) when the game's material is not there.</summary>
        internal static Material Paint(MarkingColour colour)
        {
            return MakeDetail(DetailKind.RoadPaint, PaintSolid(colour)) ?? Marking(colour);
        }

        /// <summary>The detail material of a kind in a colour, made once (a stage at a time when a tile asked for it early, TsMaterials.Warm.cs; made whole here when it is needed at once); null when the game's material is not there or the surface could not be baked.</summary>
        private static Material MakeDetail(DetailKind kind, SolidColour colour)
        {
            if (s_Details.TryGetValue((kind, colour), out Material made) && made != null)
                return made.name.StartsWith("CDLTS detail", StringComparison.Ordinal) ? made : null;
            return FinishNow(kind, colour);
        }

        /// <summary>
        /// The transparent sheets of a noise barrier (0.12.49, SolidGlass): a copy of the game's glass material ("DefaultGlass", BH/GlsShader, from its library), tinted in the colour, with the
        /// mask CDL Assets' cab glass uses (no metal, full occlusion, smooth: a rougher one draws the glass as a mosaic) and transparent screen-space reflections and their depth prepass off (with
        /// them on the glass is sprinkled with dots, CDL Assets 0.3.1). Null when the library has no glass or the copy fails: the caller draws an opaque tinted sheet. Not seen in the game yet.
        /// </summary>
        private static Material MakeGlass(SolidColour colour)
        {
            DetailTemplate();
            if (s_GlassTemplate == null)
                return null;
            Material m = null;
            try
            {
                MadeCount++;
                m = new Material(s_GlassTemplate);
                m.name = "CDLTS glass " + colour;
                m.hideFlags = HideFlags.DontSave;
                SolidGlass.Tint(colour, out float red, out float green, out float blue, out float alpha);
                var tint = new Color(red, green, blue, alpha);
                if (m.HasProperty("_BaseColor"))
                    m.SetColor("_BaseColor", tint);
                if (m.HasProperty("_MaskMap"))
                    m.SetTexture("_MaskMap", s_GlassMask != null ? s_GlassMask : (s_GlassMask = Solid4(new Color32(0, 255, 0, 255), true, "CDLTS glass mask")));
                m.SetFloat("colossal_LodFade", 1f);
                PrepareDecals(m);
                if (m.HasProperty("_ReceivesSSRTransparent"))
                    m.SetFloat("_ReceivesSSRTransparent", 0f);
                if (m.HasProperty("_TransparentDepthPrepassEnable"))
                    m.SetFloat("_TransparentDepthPrepassEnable", 0f);
                HDMaterial.ValidateMaterial(m);
                // Validation turns the prepass on again while transparent reflections are wanted anywhere; the template's transparent queue is kept.
                m.SetShaderPassEnabled("TransparentDepthPrepass", false);
                SettleDecals(m, "glass");
                return m;
            }
            catch (Exception error)
            {
                Mod.log.Warn($"Materials: the glass material {colour} could not be made, drawn opaque: {error.Message}");
                CleanupFailed(m, error);
                return null;
            }
        }

        /// <summary>Turns the detail maps on or off on every detail material (flat maps in their place), and logs it.</summary>
        internal static void SetDetail(bool on)
        {
            DetailOn = on;
            foreach (KeyValuePair<(DetailKind, SolidColour), Material> pair in s_Details)
            {
                if (pair.Value != null && pair.Value.name.StartsWith("CDLTS detail", StringComparison.Ordinal))
                    ApplyDetail(pair.Value, pair.Key.Item1, pair.Key.Item2);
            }

            Mod.log.Info($"Materials: baked detail {(on ? "on" : "off")} ({s_Details.Count} material(s); {DetailStatus})");
        }

        private static void ApplyDetail(Material m, DetailKind kind, SolidColour colour)
        {
            (Texture2D normal, Texture2D mask) = DetailMaps(kind);
            s_DetailColours.TryGetValue((kind, colour), out Texture2D baseMap);
            if (!DetailOn)
                normal = FlatNormal();

            if (m.HasProperty("_BaseColorMap"))
                m.SetTexture("_BaseColorMap", baseMap);
            if (m.HasProperty("_NormalMap"))
                m.SetTexture("_NormalMap", normal);
            if (m.HasProperty("_MaskMap"))
                m.SetTexture("_MaskMap", mask);
        }

        private static DetailSurface Surface(DetailKind kind)
        {
            BakeDetails();
            try
            {
                Dictionary<DetailKind, DetailSurface> all = s_Bakes.Result;
                return all.TryGetValue(kind, out DetailSurface surface) ? surface : null;
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Materials: the detail surfaces could not be baked: " + ex.GetBaseException().Message);
                return null;
            }
        }

        /// <summary>The game's opaque object material, looked for once in its material library (by name, else the first of its default shader that does not cut out); the library's names are logged.</summary>
        private static Material DetailTemplate()
        {
            if (s_DetailLooked)
                return s_DetailTemplate;
            s_DetailLooked = true;
            var names = new StringBuilder();
            Material byShader = null;
            for (int index = 0; index < 128; index++)
            {
                Material candidate;
                try
                {
                    candidate = Colossal.IO.AssetDatabase.AssetDatabase.global.resources.materialLibrary.GetMaterial(index);
                }
                catch (Exception)
                {
                    break;
                }

                if (candidate == null)
                    break;
                names.Append(names.Length > 0 ? ", " : "").Append(candidate.name).Append(" (").Append(candidate.shader != null ? candidate.shader.name : "?").Append(')');
                if (candidate.name == kDetailTemplateName && s_DetailTemplate == null)
                    s_DetailTemplate = candidate;
                if (candidate.name == kCutoutTemplateName && s_CutoutTemplate == null)
                    s_CutoutTemplate = candidate;
                if (candidate.name == kGlassTemplateName && s_GlassTemplate == null)
                    s_GlassTemplate = candidate;
                if (byShader == null && candidate.shader != null && candidate.shader.name.Contains("SG_DefaultShader") && candidate.name.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) < 0
                    && candidate.name.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) < 0)
                    byShader = candidate;
            }

            if (s_DetailTemplate == null)
                s_DetailTemplate = byShader;
            if (s_DetailTemplate != null)
            {
                Material t = s_DetailTemplate;
                var has = new StringBuilder();
                foreach (string property in new[] { "_BaseColorMap", "_NormalMap", "_MaskMap", "_BaseColor", "_Metallic", "_Smoothness", "colossal_LodFade", "_SupportDecals", "colossal_DecalLayerMask" })
                    has.Append(' ').Append(property).Append(t.HasProperty(property) ? "" : "(absent)");
                DetailStatus = $"the game's material \"{t.name}\" ({t.shader.name}, queue {t.renderQueue}, keywords {string.Join(" ", t.shaderKeywords)};{has}), the wire meshes' "
                    + (s_CutoutTemplate != null ? $"\"{s_CutoutTemplate.name}\" (_AlphaCutoff{(s_CutoutTemplate.HasProperty("_AlphaCutoff") ? "" : "(absent)")})" : "not there (drawn as solid sheets)")
                    + ", the noise barriers' glass " + (s_GlassTemplate != null ? $"\"{s_GlassTemplate.name}\" ({s_GlassTemplate.shader?.name}, queue {s_GlassTemplate.renderQueue})" : "not there (drawn opaque)");
            }
            else
            {
                DetailStatus = "the game's material library has no opaque material of its default shader: the solids are drawn plain";
            }

            Mod.log.Info($"Materials: baked detail from {DetailStatus}. The library: {names}.");
            return s_DetailTemplate;
        }

        private static (Texture2D Normal, Texture2D Mask) DetailMaps(DetailKind kind)
        {
            if (s_DetailMaps.TryGetValue(kind, out (Texture2D Normal, Texture2D Mask) maps) && maps.Normal != null)
                return maps;
            DetailSurface s = Surface(kind);
            DetailPixels.Maps(s, out byte[] normal, out byte[] mask);
            maps = (Texture(s, "normal", ToColors(normal), true), Texture(s, "mask", ToColors(mask), true));
            s_DetailMaps[kind] = maps;
            return maps;
        }

        /// <summary>The bytes of the baked detail's textures: each kind's normal and mask maps, the colours, the flat normal and the glass mask (FixedTextureBytes).</summary>
        private static long DetailTextureBytes()
        {
            long bytes = BytesOf(s_FlatNormal) + BytesOf(s_GlassMask);
            foreach ((Texture2D normal, Texture2D mask) in s_DetailMaps.Values)
                bytes += BytesOf(normal) + BytesOf(mask);
            foreach (Texture2D colour in s_DetailColours.Values)
                bytes += BytesOf(colour);
            return bytes;
        }

        private static Texture2D Texture(DetailSurface s, string what, Color32[] pixels, bool linear)
        {
            var texture = new Texture2D(s.Width, s.Height, TextureFormat.RGBA32, true, linear)
            {
                name = $"CDLTS detail {s.Kind} {what}",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private static Texture2D FlatNormal()
        {
            if (s_FlatNormal == null)
                s_FlatNormal = Solid4(new Color32(128, 128, 255, 255), true, "CDLTS flat normal");
            return s_FlatNormal;
        }

        private static Texture2D Solid4(Color32 c, bool linear, string name)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear) { name = name, hideFlags = HideFlags.DontSave };
            var pixels = new Color32[16];
            for (int i = 0; i < 16; i++)
                pixels[i] = c;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
