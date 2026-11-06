using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace CDLC.TS.Render
{
    /// <summary>
    /// The materials of the signs: HDRP/Lit, built the way the other CDL mods build theirs for this game's shipped shader (no keywords: a
    /// base colour map and scalar metallic, smoothness and emission only). Everything standing on a road is put into the render queue of the
    /// depth prepass for decals (2225) with the decal layer "Other": without that the road's own lane-marking decals are painted onto it.
    /// Main thread only.
    /// </summary>
    internal static partial class TsMaterials
    {
        /// <summary>The render queue of everything we draw: 2225 is HDRenderQueue.Priority.OpaqueDecal (in the depth prepass for decals). The panel can switch it to 2000 to see what the road's decals do to the signs then.</summary>
        internal const int Queue = 2225;

        /// <summary>
        /// How much brighter a sign face is at full night than by day, as a factor on the colour of the face itself (the colours keep their proportions: a dark blue stays a dark blue and a black
        /// figure stays black, they only catch more of the light that falls on them, the way retroreflective sheeting does). 2 from 0.3.2 to 0.12.29; 1 (none) since 0.12.30: the game's exposure
        /// brings a dusk and a night up to the brightness of a day, and a face twice as bright as its colour was white and washed out (the owner's "好像过曝了一样", on boards placed while the night level
        /// went from 0.5 to 1). The test switches still raise it, and it comes only in the dark (FaceNight).
        /// </summary>
        internal static float NightGain = 1f;

        /// <summary>The part of the night level the faces' brightness follows: none until half way into the dusk, then up to the whole at full night, smoothly.</summary>
        internal static float FaceNight(float level)
        {
            float t = Mathf.Clamp01((level - 0.5f) * 2f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// A glow of the whole face at full night (nits), multiplied by the face's average colour. Off by default: a face has no emissive map in this game's HDRP/Lit, so the glow is one flat colour
        /// added over everything. At 8 white (0.3.0) it drowned the figure and the ring (a round sign was a white ball); at 0.5 tinted (0.3.1) it still lifted the dark colours and made the whole
        /// face pale. It stays as a setting of the test switches for the places no lamp lights.
        /// </summary>
        internal static float NightNits = 0f;

        /// <summary>
        /// The glow of the lit dots of an LED screen (nits), by day and at full night. By day an LED must outshine the sun to be read, at night it must not dazzle (GB 5768.2 4.11.9: the
        /// brightness of an LED variable-message sign is lowered when the light is low). Between the two the figure follows the night level on a log scale. 40 at night (0.5.2 to 0.5.6) was seen in the game on 2026-10-19 and the owner found it a little too bright: 15 now. PROVISIONAL.
        /// </summary>
        internal static float LedDayNits = 20000f, LedNightNits = 15f;

        // LedDayNits was 6000 until 0.12.92: on HDRP/Lit, where the dots stay, the owner saw no glow at all by day at 6000 (5 Nov, after the same 6000 had been blown out on the game's shader) and chose 20000, the
        // panel's 200, between its steps 120 and 250, in the game.

        private static readonly Material[] s_Leds = new Material[6];
        private static Material s_Metal, s_Concrete;
        private static readonly Dictionary<string, Material> s_Faces = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Texture2D> s_Textures = new Dictionary<string, Texture2D>();

        /// <summary>The average colour (linear) of each face's picture, which its night glow is tinted with.</summary>
        private static readonly Dictionary<string, Vector3> s_Tints = new Dictionary<string, Vector3>();

        /// <summary>When each face's material was made (Time.realtimeSinceStartup), for the check that its picture came (CheckPictures); and how many times painting a face failed.</summary>
        private static readonly Dictionary<string, float> s_FaceBorn = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> s_PaintFailures = new Dictionary<string, int>();

        /// <summary>The faces that are painted steel, not retroreflective sheeting (the doors of electric boxes, 0.12.23): they keep their colour at night, as the body beside them does.</summary>
        private static readonly HashSet<string> s_Matte = new HashSet<string>();

        /// <summary>
        /// The faces lit from inside at night (0.12.49: light boxes and LED figures on lamp posts, PoleAd.Lit): they glow in their average colour (this game's HDRP/Lit has no emissive map, so the glow
        /// is one colour over the whole face) LitNits at full night, and not by day.
        /// </summary>
        private static readonly HashSet<string> s_LitFaces = new HashSet<string>();

        /// <summary>
        /// How bright a lit face (LitNits) and a lantern's body, neon and the letters of a text light box (GlowNits) are at full night (nits, times their colour); the display settings set them for the session.
        /// GlowNits was 6 until 0.12.90: the owner saw the glowing text light box on the game's shader and asked for a little less ("稍微减少一点点亮度"); 5 is a sixth less, and the settings' steps 4 and 6 are
        /// either side of it.
        /// </summary>
        internal static float LitNits = 3f, GlowNits = 5f;
        private static float s_Night = -1f;

        internal static string LastError { get; private set; } = "";

        private static Material MakeLit(string name, Color colour, Texture2D map, float metallic, float smoothness)
        {
            Shader shader = s_Shader != null ? s_Shader : (s_Shader = Shader.Find("HDRP/Lit"));
            if (shader == null)
            {
                LastError = "Shader HDRP/Lit not found";
                return null;
            }

            RetryCleanup();
            Material m = null;
            try
            {
                MadeCount++;
                m = new Material(shader);
                m.name = name;
                m.hideFlags = HideFlags.DontSave;
                m.enableInstancing = true;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
                if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
                if (map != null && m.HasProperty("_BaseColorMap")) m.SetTexture("_BaseColorMap", map);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
                PrepareDecals(m);
                if (m.HasProperty("_EmissiveColor")) m.SetVector("_EmissiveColor", Vector4.zero);
                try
                {
                    HDMaterial.ValidateMaterial(m);
                }
                catch (Exception ex)
                {
                    LastError = "ValidateMaterial: " + ex.Message;
                    Mod.log.Warn("Materials: ValidateMaterial failed for " + name + ": " + ex.Message);
                    throw;
                }

                // After ValidateMaterial, which resets the queue.
                m.renderQueue = Queue;
                SettleDecals(m, GameShaderRoute.FamilyOf(name), new LitSpec { Colour = colour, Metallic = metallic, Smoothness = smoothness });
                return m;
            }
            catch (Exception error)
            {
                CleanupFailed(m, error);
                throw;
            }
        }

        private static readonly Material[] s_Marking = new Material[4];

        /// <summary>
        /// The paint of a road marking (GB 5768.3 4.3: white or yellow): a lit material, rough (a painted road is not a mirror), no emission. A marking lies 3 cm above the road, at the same queue as the
        /// signs, and casts no shadow (ChunkSystem). The reflectivity the standard asks for (4.4.1) is a property of the paint in the real world; here it is only that the paint is lit by the headlights.
        /// </summary>
        internal static Material Marking(MarkingColour colour)
        {
            int i = Mathf.Clamp((int)colour, 0, s_Marking.Length - 1);
            if (s_Marking[i] != null)
                return s_Marking[i];
            switch (i)
            {
                case 1:
                    return s_Marking[i] = MakeLit("CDLTS marking yellow", new Color(0.95f, 0.74f, 0.06f, 1f), null, 0f, 0.28f);
                case 2:
                    return s_Marking[i] = MakeLit("CDLTS marking red", new Color(0.78f, 0.07f, 0.05f, 1f), null, 0f, 0.28f);
                case 3:
                    return s_Marking[i] = MakeLit("CDLTS marking black", new Color(0.025f, 0.025f, 0.03f, 1f), null, 0f, 0.28f);
                default:
                    return s_Marking[i] = MakeLit("CDLTS marking white", new Color(0.88f, 0.88f, 0.86f, 1f), null, 0f, 0.28f);
            }
        }

        private static readonly Material[] s_Solids = new Material[SignParts.SolidCount];

        /// <summary>The painted steel or plastic of a road barrier (Barriers): lit, a little glossy, no emission. Steel and concrete are the signs' own materials.</summary>
        internal static Material Solid(SolidColour colour)
        {
            if (colour == SolidColour.Metal)
                return Metal();
            if (colour == SolidColour.Concrete)
                return Concrete();
            int i = Mathf.Clamp((int)colour, 0, s_Solids.Length - 1);
            if (s_Solids[i] != null)
                return s_Solids[i];
            if (SolidGlow.Is(colour))
                return s_Solids[i] = Glowing(colour);
            if (SolidGlass.Is(colour))
                return s_Solids[i] = MakeGlass(colour) ?? MakeLit("CDLTS solid " + colour, SolidRgb(colour), null, 0f, 0.9f);
            (float metallic, float smoothness) = colour == SolidColour.Steel || colour == SolidColour.Gold ? (0.7f, 0.6f) : colour == SolidColour.Soil ? (0f, 0.1f) : colour == SolidColour.Wood ? (0f, 0.3f)
                : colour == SolidColour.DarkGray ? (0.2f, 0.45f) : colour == SolidColour.Black ? (0f, 0.35f) : colour == SolidColour.Orange || colour == SolidColour.Red ? (0f, 0.4f)
                : colour == SolidColour.Green || colour == SolidColour.LightGray ? (0.1f, 0.4f) : colour == SolidColour.CastIron ? (0.45f, 0.3f) : colour == SolidColour.ResinGreen ? (0f, 0.35f)
                // A support's powder coat (0.12.57): RAL 9011 is 40 % gloss (DG/TJ 08-2362 A.5.8-1), the greys as much.
                : colour == SolidColour.Graphite || colour == SolidColour.PoleDarkGrey || colour == SolidColour.PoleLightGrey ? (0.05f, 0.42f) : (0.1f, 0.45f);
            return s_Solids[i] = MakeLit("CDLTS solid " + colour, SolidRgb(colour), null, metallic, smoothness);
        }

        /// <summary>The linear colour of a solid of the colour (the plain material's and the detail material's).</summary>
        private static Color SolidRgb(SolidColour colour)
        {
            switch (colour)
            {
                case SolidColour.Metal: return new Color(0.60f, 0.62f, 0.64f, 1f);
                case SolidColour.Concrete: return new Color(0.62f, 0.62f, 0.60f, 1f);
                case SolidColour.Blue: return new Color(0.05f, 0.22f, 0.55f, 1f);
                case SolidColour.Yellow: return new Color(0.92f, 0.70f, 0.05f, 1f);
                case SolidColour.Black: return new Color(0.03f, 0.03f, 0.035f, 1f);
                case SolidColour.Orange: return new Color(0.95f, 0.30f, 0.04f, 1f);
                case SolidColour.Red: return new Color(0.78f, 0.06f, 0.05f, 1f);
                case SolidColour.Green: return new Color(0.10f, 0.42f, 0.22f, 1f);
                case SolidColour.LightGray: return new Color(0.80f, 0.81f, 0.79f, 1f);
                case SolidColour.Steel: return new Color(0.73f, 0.745f, 0.76f, 1f);
                case SolidColour.Soil: return new Color(0.20f, 0.14f, 0.09f, 1f);
                case SolidColour.Wood: return new Color(0.45f, 0.30f, 0.17f, 1f);
                case SolidColour.DarkGray: return new Color(0.18f, 0.19f, 0.20f, 1f);
                case SolidColour.Granite: return new Color(0.62f, 0.62f, 0.61f, 1f);
                case SolidColour.Gold: return new Color(0.66f, 0.42f, 0.08f, 1f);
                case SolidColour.GlowRed: return new Color(0.70f, 0.05f, 0.04f, 1f);
                case SolidColour.GlowGold: return new Color(0.85f, 0.55f, 0.10f, 1f);
                case SolidColour.GlowWarm: return new Color(0.95f, 0.85f, 0.65f, 1f);
                case SolidColour.GlowWhite: return new Color(0.88f, 0.88f, 0.96f, 1f);
                case SolidColour.GlowBlue: return new Color(0.08f, 0.28f, 0.95f, 1f);
                case SolidColour.GlowPink: return new Color(0.95f, 0.22f, 0.52f, 1f);
                case SolidColour.GlowGreen: return new Color(0.12f, 0.78f, 0.28f, 1f);
                case SolidColour.CastIron: return new Color(0.045f, 0.043f, 0.04f, 1f);
                case SolidColour.ResinGreen: return new Color(0.05f, 0.30f, 0.22f, 1f);
                case SolidColour.DarkBrown: return new Color(0.085f, 0.058f, 0.045f, 1f);
                case SolidColour.Glass:
                case SolidColour.GlassBlue:
                case SolidColour.GlassGreen:
                    SolidGlass.Tint(colour, out float red, out float green, out float blue, out _);
                    return new Color(red, green, blue, 1f);
                case SolidColour.Brick: return new Color(0.36f, 0.12f, 0.07f, 1f);
                case SolidColour.GreyBrick: return new Color(0.30f, 0.31f, 0.31f, 1f);
                // #25282A, #3A3B40 and #B1B3BF (SolidColour) made linear.
                case SolidColour.Graphite: return new Color(0.0185f, 0.0212f, 0.0232f, 1f);
                case SolidColour.PoleDarkGrey: return new Color(0.0423f, 0.0437f, 0.0513f, 1f);
                case SolidColour.PoleLightGrey: return new Color(0.4397f, 0.4508f, 0.5210f, 1f);
                default:
                {
                    if (!WallFoliage.Is(colour))
                        return new Color(0.86f, 0.87f, 0.86f, 1f);
                    // A blossom colour as a plain solid (the wall's far band): the blossoms' own colour, made linear.
                    (float r, float g, float b) = WallFoliage.Blossom(colour);
                    return new Color(r, g, b, 1f).linear;
                }
            }
        }

        /// <summary>The material of a tile's or a preview's part by its name (ChunkBuilder's material names) other than a face's or a lit dot's: steel, concrete, paint or a barrier's solid.</summary>
        internal static Material OfBody(string material) =>
            ChunkBuilder.DetailOf(material, out DetailKind kind, out SolidColour colour) ? Detail(kind, colour)
            : material == ChunkBuilder.MetalMaterial ? Metal() : material == ChunkBuilder.ConcreteMaterial ? Concrete()
            : material.StartsWith(ChunkBuilder.SolidPrefix, System.StringComparison.Ordinal) ? Solid(ChunkBuilder.SolidColourOf(material))
            : ChunkBuilder.IsMarkingMaterial(material) ? Paint(ChunkBuilder.MarkingColourOf(material)) : null;

        internal static Material Metal()
        {
            if (s_Metal == null)
                s_Metal = MakeLit("CDLTS metal", new Color(0.60f, 0.62f, 0.64f, 1f), null, 0.3f, 0.45f);
            return s_Metal;
        }

        internal static Material Concrete()
        {
            if (s_Concrete == null)
                s_Concrete = MakeLit("CDLTS concrete", new Color(0.62f, 0.62f, 0.60f, 1f), null, 0f, 0.12f);
            return s_Concrete;
        }

        // Where the pictures of the faces are held: the material of each (named by the board and the detail: FaceDetail.Material), the picture that was painted for it, and counters of what is held.
        private static int s_TextureCount;
        private static long s_TextureBytes;
        private static readonly Dictionary<int, Texture2D> s_Flat = new Dictionary<int, Texture2D>();
        private static Shader s_Shader;

        /// <summary>The material every face is cloned from: made and validated once (HDMaterial.ValidateMaterial is the slow part of making a material), so that a face costs a clone, not a validation.</summary>
        private static Material s_FaceTemplate;

        /// <summary>How many face pictures are held, and about how many bytes they take on the graphics card (RGBA32 with its mip chain: a third more than the first level).</summary>
        internal static int FaceTextureCount => s_TextureCount;

        /// <summary>How many materials were made this session (each one is validated: milliseconds the first time); a slow frame says whether it made one.</summary>
        internal static int MadeCount { get; private set; }

        internal static long FaceTextureBytes => s_TextureBytes;

        /// <summary>How many face materials there are (a material is there as soon as a tile that has the face is built, its picture a little later).</summary>
        internal static int FaceMaterialCount => s_Faces.Count;

        /// <summary>
        /// The face pictures' bytes by the detail they were painted at (FaceDetail.Full, Half, Small: index 0 to 2), read from the pictures that are held (0.12.104, the panel's figures): the sum of the three is
        /// FaceTextureBytes.
        /// </summary>
        internal static void FaceBytesByDetail(long[] into)
        {
            Array.Clear(into, 0, into.Length);
            foreach (KeyValuePair<string, Texture2D> pair in s_Textures)
            {
                if (FaceDetail.TryParse(pair.Key, out _, out int detail) && detail >= 0 && detail < into.Length)
                    into[detail] += BytesOf(pair.Value);
            }
        }

        /// <summary>The bytes of the textures that do not depend on the tiles (0.12.104): the baked detail maps and their colours, and the small flat ones.</summary>
        internal static long FixedTextureBytes()
        {
            long bytes = DetailTextureBytes();
            foreach (Texture2D flat in s_Flat.Values)
                bytes += BytesOf(flat);
            return bytes;
        }

        private static long BytesOf(Texture2D texture) =>
            texture == null ? 0L : texture.format == TextureFormat.DXT1 ? BlockCompress.Bc1ChainSize(texture.width, texture.height) : (long)texture.width * texture.height * 4 * 4 / 3;

        /// <summary>How many BC1 pictures were read back and found right (the first three are: FacePainter.Bc1Mode).</summary>
        private static int s_Bc1Verified;

        /// <summary>
        /// A face's texture from its BC1 blocks with their mips (0.12.27): the blocks as they are (LoadRawTextureData), no mips made here. While FacePainter.Bc1Mode is 0 the texture is read back and
        /// compared with the blocks as BlockCompress reads them, all of the first level; three right and BC1 is on, one wrong (or a call that throws) and it is off for the session, the face made from its
        /// painter's bytes as before. Null when it is off.
        /// </summary>
        private static Texture2D Bc1Texture(PaintedFace painted)
        {
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(painted.Width, painted.Height, TextureFormat.DXT1, BlockCompress.LevelCount(painted.Width, painted.Height), false);
                texture.LoadRawTextureData(painted.Blocks);
                if (FacePainter.Bc1Mode == 0)
                {
                    string wrong = Bc1Mismatch(texture, painted);
                    if (wrong != null)
                    {
                        Bc1Off(painted, wrong);
                        UnityEngine.Object.Destroy(texture);
                        return null;
                    }

                    if (++s_Bc1Verified >= 3)
                    {
                        FacePainter.Bc1Mode = 1;
                        Mod.log.Info($"Materials: the faces are BC1 with their mips (an eighth of RGBA32): the first {s_Bc1Verified} were read back and are right.");
                    }
                }

                return texture;
            }
            catch (Exception ex)
            {
                if (FacePainter.Bc1Mode != 0)
                    throw;
                // While it is being checked a failure only turns BC1 off: the painter's bytes are still there.
                Bc1Off(painted, ex.GetType().Name + ": " + ex.Message);
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
                return null;
            }
        }

        private static void Bc1Off(PaintedFace painted, string why)
        {
            FacePainter.Bc1Mode = -1;
            Mod.log.Warn($"Materials: BC1 faces are off for this session, RGBA32 as before: '{painted.Material}' {painted.Width}x{painted.Height}: {why}");
        }

        /// <summary>Why the first level of the texture is not what the blocks say (null when it is): every pixel within 4 of BlockCompress's reading (GPUs and Unity round the thirds their own way).</summary>
        private static string Bc1Mismatch(Texture2D texture, PaintedFace painted)
        {
            Color32[] got = texture.GetPixels32(0);
            int w = painted.Width, h = painted.Height;
            if (got.Length != w * h)
                return $"{got.Length} pixels read back for {w}x{h}";
            byte[] want = BlockCompress.DecodeBc1(painted.Blocks, 0, w, h);
            int bad = 0, first = -1;
            for (int i = 0; i < got.Length; i++)
            {
                if (Math.Abs(got[i].r - want[i * 4]) > 4 || Math.Abs(got[i].g - want[i * 4 + 1]) > 4 || Math.Abs(got[i].b - want[i * 4 + 2]) > 4)
                {
                    bad++;
                    if (first < 0)
                        first = i;
                }
            }

            return bad == 0 ? null : $"{bad} of {got.Length} pixels read back differ, the first at ({first % w}, {first / w}): {got[first]} for ({want[first * 4]}, {want[first * 4 + 1]}, {want[first * 4 + 2]})";
        }

        /// <summary>
        /// The material of a face (a name made by FaceDetail.Material). It is there at once, in the colour of the board's ground; the picture is painted on the worker (FacePainter) and put on the
        /// material by PumpFaces when it is ready, at the size that fits the detail. Urgent faces (the preview of the sign being placed) are painted first. A face seen from afar that is never painted
        /// (an LED screen) keeps its colour.
        /// </summary>
        internal static Material Face(string material, bool urgent = false)
        {
            if (s_Faces.TryGetValue(material, out Material cached) && cached != null)
            {
                // A face with no picture and none on its way keeps the colour of its ground for ever: asking is cheap (FacePainter.Request does nothing for one that is asked for), so every use asks again (0.12.58).
                if (urgent || !s_Textures.ContainsKey(material))
                    RequestPicture(material, urgent);
                return cached;
            }

            byte r = 150, g = 150, b = 150;
            if (FaceDetail.TryParse(material, out string key, out int detail))
            {
                SignInstance instance = SignInstances.FromKey(key);
                FaceDetail.Placeholder(instance, out r, out g, out b);
                if (instance?.Board?.Box != null)
                    s_Matte.Add(material);
                if (instance?.Board?.PoleAd != null && instance.Board.PoleAd.Lit)
                    s_LitFaces.Add(material);
                // An internally lit board (0.12.80, SignLighting; GB 5768.2 4.8.16 b): its face glows at night as a lamp-post ad's does, and not by day.
                else if (instance?.Settings != null && SignLighting.Of(instance.Settings) == LightingKind.Internal)
                    s_LitFaces.Add(material);
            }

            Texture2D flat = FlatTexture(r, g, b);
            Material m = CloneFace("CDLTS " + material, flat);
            if (m != null)
            {
                try
                {
                    // Until the picture is there the glow is tinted with the colour the face has.
                    s_Tints[material] = new Vector3(Mathf.GammaToLinearSpace(r / 255f), Mathf.GammaToLinearSpace(g / 255f), Mathf.GammaToLinearSpace(b / 255f));
                    ApplyNight(material, m, Mathf.Max(0f, s_Night));
                    RequestPicture(material, urgent);
                    s_Faces[material] = m;
                    s_FaceBorn[material] = Time.realtimeSinceStartup;
                }
                catch (Exception error)
                {
                    s_Tints.Remove(material);
                    s_FaceBorn.Remove(material);
                    CleanupFailed(m, error);
                    throw;
                }
            }

            return m;
        }

        private static Material CloneFace(string name, Texture2D flat)
        {
            if (s_FaceTemplate == null)
            {
                s_FaceTemplate = MakeLit("CDLTS face template", Color.white, flat, 0f, 0.55f);
                if (s_FaceTemplate == null)
                    return null;
                s_FaceTemplateEntry = s_LastEntry;
            }

            RetryCleanup();
            Material m = null;
            try
            {
                MadeCount++;
                m = new Material(s_FaceTemplate);
                m.name = name;
                m.hideFlags = HideFlags.DontSave;
                m.enableInstancing = true;
                if (m.HasProperty("_BaseColorMap"))
                    m.SetTexture("_BaseColorMap", flat);
                // A copy of the validated template keeps its keywords, its queue and its shader (the game's, when the template was moved: a face lit from inside is put back by the route): the experiment file, the audit and the route need to know it.
                SettleDecals(m, "face", s_FaceTemplateEntry?.Spec, s_FaceTemplateEntry != null && s_FaceTemplateEntry.OnGame);
                return m;
            }
            catch (Exception error)
            {
                CleanupFailed(m, error);
                throw;
            }
        }

        private static void RequestPicture(string material, bool urgent)
        {
            if (!FaceDetail.TryParse(material, out string key, out int detail))
                return;
            if (s_Textures.ContainsKey(material))
                return;
            SignInstance instance = SignInstances.FromKey(key);
            if (instance == null || FaceDetail.NeverPainted(instance, detail))
                return;
            FacePainter.Request(material, instance, detail, urgent);
        }

        private sealed class PendingFace
        {
            internal PaintedFace Painted;
            internal Material Target;
            internal bool CheckSlot;
            internal Texture Slot;
        }

        // FacePainter keeps the dequeued key wanted, so GPU failures retry its result on the original cached material.
        private static PendingFace s_PendingFace;

        private static bool TryTakePicture(out PaintedFace painted)
        {
            PendingFace retry = s_PendingFace;
            if (retry != null)
            {
                bool current = s_Faces.TryGetValue(retry.Painted.Material, out Material material) && material != null && ReferenceEquals(material, retry.Target);
                if (current && retry.CheckSlot)
                    current = material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") == retry.Slot;
                s_PendingFace = null;
                if (current)
                {
                    painted = retry.Painted;
                    return true;
                }
            }
            return FacePainter.TryTake(out painted);
        }

        /// <summary>A tiny texture of one colour, shared by every face that has that colour until its picture is there (a few bytes, not a picture of its own).</summary>
        private static Texture2D FlatTexture(byte r, byte g, byte b)
        {
            int id = (r << 16) | (g << 8) | b;
            if (s_Flat.TryGetValue(id, out Texture2D flat) && flat != null)
                return flat;
            RetryCleanup();
            flat = null;
            try
            {
                flat = new Texture2D(4, 4, TextureFormat.RGBA32, false, false);
                flat.name = "CDLTS flat " + id.ToString("x6");
                flat.hideFlags = HideFlags.DontSave;
                flat.wrapMode = TextureWrapMode.Clamp;
                flat.filterMode = FilterMode.Bilinear;
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(r, g, b, 255);
                flat.SetPixels32(pixels);
                flat.Apply(false, true);
                s_Flat[id] = flat;
                return flat;
            }
            catch (Exception error)
            {
                CleanupFailed(flat, error);
                throw;
            }
        }

        private static int s_Logged;
        private static bool s_RawOk = true;
        private static int s_RawVerified;

        private static void SetFirstLevel(Texture2D texture, PaintedFace painted)
        {
            Exception rawError = null;
            if (s_RawOk)
            {
                try
                {
                    texture.SetPixelData(painted.Raw, 0);
                    if (s_RawVerified < 3)
                    {
                        s_RawVerified++;
                        if (!SameAsRaw(texture, painted))
                        {
                            s_RawOk = false;
                            Mod.log.Warn($"Materials: SetPixelData did not give the picture of '{painted.Material}' ({painted.Width}x{painted.Height}) back as it was put in: SetPixels32 is used from now on.");
                        }
                        else if (s_RawVerified == 3)
                        {
                            Mod.log.Info("Materials: SetPixelData is used for the pictures of the faces (the first three were read back and are right).");
                        }
                    }
                }
                catch (Exception ex)
                {
                    rawError = ex;
                    s_RawOk = false;
                    Mod.log.Warn("Materials: SetPixelData failed (" + ex.GetType().Name + ": " + ex.Message + "): SetPixels32 is used from now on.");
                }
            }

            if (s_RawOk)
                return;
            byte[] raw = painted.Raw;
            var pixels = new Color32[painted.Width * painted.Height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(raw[i * 4], raw[i * 4 + 1], raw[i * 4 + 2], raw[i * 4 + 3]);
            try { texture.SetPixels32(pixels); }
            catch (Exception error)
            {
                if (rawError != null) throw new AggregateException("Both face texture upload paths failed.", rawError, error);
                throw;
            }
        }

        /// <summary>Whether the first level of the texture holds the painter's bytes: the corners, the middle and a few places between.</summary>
        private static bool SameAsRaw(Texture2D texture, PaintedFace painted)
        {
            Color32[] got = texture.GetPixels32(0);
            int w = painted.Width, h = painted.Height;
            if (got.Length != w * h)
                return false;
            byte[] raw = painted.Raw;
            int[] places = { 0, w - 1, (h - 1) * w, h * w - 1, (h / 2) * w + w / 2, (h / 3) * w + w / 3, (h * 2 / 3) * w + w * 2 / 3 };
            foreach (int p in places)
            {
                if (got[p].r != raw[p * 4] || got[p].g != raw[p * 4 + 1] || got[p].b != raw[p * 4 + 2] || got[p].a != raw[p * 4 + 3])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Puts the pictures that have been painted on the materials of their faces: a texture for each (made from the colours the worker made, which is the one call that is known to work in this
        /// game's Unity), as many as fit in the budget and at least one. Main thread, every frame. Returns how many were made.
        /// </summary>
        internal static int PumpFaces(double budgetMs)
        {
            RetryCleanup();
            PumpDecals();
            int made = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (TryTakePicture(out PaintedFace painted))
            {
                if (painted.Error != null)
                {
                    LastError = "painting " + painted.Material + ": " + painted.Error;
                    Mod.log.Error("Materials: could not paint '" + painted.Material + "': " + painted.Error);
                    // No longer asked for, so that the next use asks again (it kept the colour of its ground for ever until 0.12.58); three times at most.
                    s_PaintFailures.TryGetValue(painted.Material, out int failures);
                    s_PaintFailures[painted.Material] = failures + 1;
                    if (failures < 3)
                        FacePainter.Forget(painted.Material);
                }
                else if (!s_Faces.TryGetValue(painted.Material, out Material gone) || gone == null)
                {
                    // The picture came when its material was not there (a face dropped and not yet made again): it is not kept, and the next one made for it asks again.
                    FacePainter.Forget(painted.Material);
                }
                else if (s_Faces.TryGetValue(painted.Material, out Material m) && m != null)
                {
                    s_Textures.TryGetValue(painted.Material, out Texture2D old);
                    long oldBytes = BytesOf(old);
                    Texture2D texture = null;
                    Texture previous = null;
                    bool bindingAttempted = false;
                    long bytes;
                    try
                    {
                        // BC1 with the mips made on the worker (0.12.27), else RGBA32 as before.
                        texture = painted.Blocks != null && FacePainter.Bc1Mode >= 0 ? Bc1Texture(painted) : null;
                        bool blocks = texture != null;
                        if (!blocks)
                            texture = new Texture2D(painted.Width, painted.Height, TextureFormat.RGBA32, true, false);
                        texture.name = "CDLTS face " + painted.Material;
                        texture.hideFlags = HideFlags.DontSave;
                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Trilinear;
                        texture.anisoLevel = painted.Detail == FaceDetail.Full ? 8 : painted.Detail == FaceDetail.Half ? 4 : 1;

                        // RGBA32: the first level only, and the mips are made by Apply(true) (LoadRawTextureData wants the data of every level of a texture with mips: 0.1.0 gave it the first level only, the
                        // call threw, and every face stayed white). SetPixelData takes the painter's bytes as they are; the first three pictures are read back and compared, and if it did not do what it should
                        // (or throws) the colours are made from the bytes and SetPixels32 is used, for this picture and for the rest. Not readable afterwards (0.6.0): only the copy on the graphics card is kept.
                        if (!blocks)
                            SetFirstLevel(texture, painted);
                        texture.Apply(!blocks, true);
                        bytes = BytesOf(texture);
                        if (m.HasProperty("_BaseColorMap"))
                        {
                            previous = m.GetTexture("_BaseColorMap");
                            bindingAttempted = true;
                            m.SetTexture("_BaseColorMap", texture);
                        }
                    }
                    catch (Exception error)
                    {
                        s_PendingFace = new PendingFace { Painted = painted, Target = m, CheckSlot = bindingAttempted, Slot = previous };
                        if (bindingAttempted) CleanupFailedSwap(m, previous, texture, error);
                        else CleanupFailed(texture, error);
                        throw;
                    }

                    s_Textures[painted.Material] = texture;
                    s_TextureBytes += bytes - oldBytes;
                    if (old == null) s_TextureCount++;
                    s_Tints[painted.Material] = painted.Tint;
                    try { ApplyNight(painted.Material, m, Mathf.Max(0f, s_Night)); }
                    catch (Exception error)
                    {
                        s_PendingFace = new PendingFace { Painted = painted, Target = m, CheckSlot = true, Slot = texture };
                        try { ReleaseOwned(old); }
                        catch (Exception releaseError) { throw new AggregateException("Face update and retired texture cleanup failed.", error, releaseError); }
                        throw;
                    }
                    ReleaseOwned(old);
                    if (s_Logged++ < 40)
                        Mod.log.Info($"Materials: painted '{painted.Material}' {painted.Width}x{painted.Height} in {painted.Ms:0.0} ms on the worker, {texture.format}, {bytes / 1024} KB, font: {LegendPainter.FontSource}");
                }

                made++;
                if (clock.Elapsed.TotalMilliseconds >= budgetMs)
                    break;
            }

            CheckPictures();
            return made;
        }

        private static float s_NextPictureCheck;
        private static readonly HashSet<string> s_Said = new HashSet<string>();

        /// <summary>
        /// Every few seconds: the faces whose picture has not come in PictureWaitSeconds (and is not on its way: nothing is queued, painted or waiting to be put on) are said once in the log and asked
        /// for again. A picture that is late is a queue; one that is lost is a face that stays the colour of its ground, which is what the owner's screenshot of 31 Oct showed (a grey board).
        /// </summary>
        private static void CheckPictures()
        {
            float now = Time.realtimeSinceStartup;
            if (now < s_NextPictureCheck)
                return;
            s_NextPictureCheck = now + 4f;
            if (FacePainter.Waiting > 0 || FacePainter.Pending > 0)
                return;
            List<string> stale = null;
            foreach (KeyValuePair<string, Material> pair in s_Faces)
            {
                if (pair.Value == null || s_Textures.ContainsKey(pair.Key) || !s_FaceBorn.TryGetValue(pair.Key, out float born) || now - born < PictureWaitSeconds)
                    continue;
                if (!FaceDetail.TryParse(pair.Key, out string key, out int detail) || FaceDetail.NeverPainted(SignInstances.FromKey(key), detail))
                    continue;
                (stale ?? (stale = new List<string>())).Add(pair.Key);
            }

            if (stale == null)
                return;
            foreach (string material in stale)
            {
                if (s_Said.Add(material) && s_Said.Count <= 20)
                    Mod.log.Warn($"Materials: the face '{material}' has had no picture for {PictureWaitSeconds:0} s with none queued, painted or waiting: asked for again.");
                FacePainter.Forget(material);
                RequestPicture(material, true);
            }
        }

        /// <summary>How long a face may be without its picture, with the painter idle, before it is taken for lost.</summary>
        internal const float PictureWaitSeconds = 8f;

        /// <summary>
        /// The material of the lit dots of one colour (LedColour 1 to 5): black, and a glow of its colour; the dots are geometry in front of the screen's face, because this game's HDRP/Lit has no
        /// emissive map to make a texture glow in the lit places only.
        /// </summary>
        internal static Material LedMaterial(int colour)
        {
            if (colour < 1 || colour >= s_Leds.Length)
                return null;
            if (s_Leds[colour] != null)
                return s_Leds[colour];
            Material m = MakeLit("CDLTS led " + colour, Color.black, null, 0f, 0.2f);
            if (m == null)
                return null;
            try { ApplyLed(colour, m, Mathf.Max(0f, s_Night)); }
            catch (Exception error)
            {
                CleanupFailed(m, error);
                throw;
            }
            s_Leds[colour] = m;
            Mod.log.Info($"Materials: LED material {(LedColour)colour}, glow {LedNits(Mathf.Max(0f, s_Night)):0} nits (day {LedDayNits:0}, night {LedNightNits:0})");
            return m;
        }

        /// <summary>The day and the night glow of the LED screens (nits), from the panel's test switches: the dots are written again at once.</summary>
        internal static void SetLedNits(float day, float night)
        {
            LedDayNits = Mathf.Clamp(day, 100f, 100000f);
            LedNightNits = Mathf.Clamp(night, 1f, 5000f);
            ApplyLeds(Mathf.Max(0f, s_Night));
            Mod.log.Info($"Materials: LED glow {LedDayNits:0} nits by day, {LedNightNits:0} at full night (now {LedNits(Mathf.Max(0f, s_Night)):0})");
        }

        /// <summary>The glow of an LED at a night level (0 by day, 1 at full night): between the two figures on a log scale.</summary>
        internal static float LedNits(float night) =>
            Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(1f, LedDayNits)), Mathf.Log(Mathf.Max(1f, LedNightNits)), Mathf.Clamp01(night)));

        private static void ApplyLed(int colour, Material m, float night)
        {
            if (m == null || !m.HasProperty("_EmissiveColor"))
                return;
            Led.Rgb((LedColour)colour, out byte r, out byte g, out byte b);
            // A lit dot is its whole cell (Led.CellLight): as bright as the dots were, on more of the screen.
            float nits = LedNits(night) * Led.CellLight;
            // Linear light, as the cab's LEDs of Bus Driver are set (the colour of the lamp in linear space times its nits).
            m.SetVector("_EmissiveColor", new Vector4(Mathf.GammaToLinearSpace(r / 255f) * nits, Mathf.GammaToLinearSpace(g / 255f) * nits, Mathf.GammaToLinearSpace(b / 255f) * nits, 1f));
        }

        private static void ApplyLeds(float night)
        {
            for (int colour = 1; colour < s_Leds.Length; colour++)
                ApplyLed(colour, s_Leds[colour], night);
        }

        /// <summary>Drops the materials and pictures of the faces that are not in the set (the faces of boards that no longer exist); the metal and the concrete stay.</summary>
        internal static void Prune(HashSet<string> keep)
        {
            List<string> gone = null;
            foreach (string key in s_Faces.Keys)
            {
                if (!keep.Contains(key))
                    (gone ?? (gone = new List<string>())).Add(key);
            }

            if (gone == null)
                return;
            var failures = new List<Exception>();
            foreach (string key in gone)
            {
                s_Textures.TryGetValue(key, out Texture2D t);
                long bytes = BytesOf(t);
                if (s_Faces.TryGetValue(key, out Material m) && m != null)
                    DropOwned(m, failures);
                s_Faces.Remove(key);
                if (t != null)
                {
                    DropOwned(t, failures);
                    s_TextureBytes -= bytes;
                    s_TextureCount--;
                }

                s_Textures.Remove(key);
                s_Tints.Remove(key);
                s_FaceBorn.Remove(key);
                s_PaintFailures.Remove(key);
                s_Said.Remove(key);
                s_Matte.Remove(key);
                s_LitFaces.Remove(key);
                if (s_PendingFace != null && s_PendingFace.Painted.Material == key)
                    s_PendingFace = null;
                try { FacePainter.Forget(key); }
                catch (Exception error) { failures.Add(error); }
            }

            Mod.log.Info($"Materials: dropped {gone.Count} unused face picture(s), {s_Faces.Count} kept.");
            if (failures.Count != 0) throw new AggregateException("Face pruning failed; owned cleanup is retained.", failures);
        }

        private static Material Glowing(SolidColour colour)
        {
            Material m = MakeLit("CDLTS glow " + colour, SolidRgb(colour), null, 0f, 0.5f);
            ApplyGlow(colour, m, Mathf.Max(0f, s_Night));
            return m;
        }

        private static void ApplyGlow(SolidColour colour, Material m, float level)
        {
            if (m == null || !m.HasProperty("_EmissiveColor"))
                return;
            Color c = SolidRgb(colour);
            float nits = GlowNits * FaceNight(level);
            m.SetVector("_EmissiveColor", new Vector4(c.r * nits, c.g * nits, c.b * nits, 1f));
        }

        private static void ApplyGlows(float level)
        {
            foreach (SolidColour colour in SolidGlow.All)
                ApplyGlow(colour, s_Solids[(int)colour], level);
        }

        /// <summary>The display settings' brightness of the lit faces and of the lanterns and neon at full night (nits).</summary>
        internal static void SetLampGlow(float litNits, float glowNits)
        {
            LitNits = Mathf.Clamp(litNits, 0f, 64f);
            GlowNits = Mathf.Clamp(glowNits, 0f, 64f);
            float level = Mathf.Max(0f, s_Night);
            foreach (string key in s_LitFaces)
            {
                if (s_Faces.TryGetValue(key, out Material m) && m != null)
                    ApplyNight(key, m, level);
            }

            ApplyGlows(level);
            Mod.log.Info($"Materials: lamp-post ads at full night: lit faces {LitNits:0.#} nits, lanterns and neon {GlowNits:0.#} nits");
        }

        internal static void SetNightGain(float gain)
        {
            NightGain = Mathf.Clamp(gain, 1f, 16f);
            foreach (KeyValuePair<string, Material> pair in s_Faces)
            {
                if (pair.Value != null)
                    ApplyNight(pair.Key, pair.Value, Mathf.Max(0f, s_Night));
            }

            Mod.log.Info($"Materials: night brightness of the faces x{NightGain:0.0#} at full night");
        }

        internal static void SetNightNits(float nits)
        {
            NightNits = Mathf.Clamp(nits, 0f, 64f);
            foreach (KeyValuePair<string, Material> pair in s_Faces)
            {
                if (pair.Value != null)
                    ApplyNight(pair.Key, pair.Value, Mathf.Max(0f, s_Night));
            }

            Mod.log.Info($"Materials: night glow {NightNits:0.0#} nits at full night (times the colour of the face)");
        }

        /// <summary>0 by day, 1 in the dark: the faces glow a little at night so they can be read without a lamp on them.</summary>
        internal static void SetNight(float level)
        {
            level = Mathf.Clamp01(level);
            if (Mathf.Approximately(level, s_Night))
                return;

            s_Night = level;
            foreach (KeyValuePair<string, Material> pair in s_Faces)
            {
                if (pair.Value != null)
                    ApplyNight(pair.Key, pair.Value, level);
            }

            ApplyLeds(level);
            ApplyGlows(level);

            Mod.log.Info($"Materials: night level {level:0.00} (faces x{1f + (NightGain - 1f) * FaceNight(level):0.00}, glow {NightNits * level:0.0##} nits times the colour of the face)");
        }

        private static void ApplyNight(string key, Material m, float level)
        {
            if (m == null)
                return;

            // SetVector, not SetColor: a colour is converted from gamma to linear light, which would turn a factor of 2 into one of about 4.8. The factor is on the face's own colour (white
            // is 1), so the picture keeps its proportions; by day (level 0) it is 1.
            float gain = s_Matte.Contains(key) ? 1f : 1f + (NightGain - 1f) * FaceNight(level);
            if (m.HasProperty("_BaseColor"))
                m.SetVector("_BaseColor", new Vector4(gain, gain, gain, 1f));

            if (!m.HasProperty("_EmissiveColor"))
                return;

            // The glow, if there is one: linear nits, in the colour the face has on average (white when it is not known); a face lit from inside has its own (0.12.49).
            float nits = NightNits * level + (s_LitFaces.Contains(key) ? LitNits * FaceNight(level) : 0f);
            Vector3 tint = s_Tints.TryGetValue(key, out Vector3 found) ? found : Vector3.one;
            m.SetVector("_EmissiveColor", new Vector4(nits * tint.x, nits * tint.y, nits * tint.z, 1f));
        }

        /// <summary>Called when a city is left: the next one paints and builds again.</summary>
        internal static void Clear()
        {
            s_PendingFace = null;
            var failures = new List<Exception>();
            try { RetryCleanup(); }
            catch (Exception error) { failures.Add(error); }
            foreach (Material m in s_Faces.Values)
            {
                if (m != null)
                    DropOwned(m, failures);
            }

            foreach (Texture2D t in s_Textures.Values)
            {
                if (t != null)
                    DropOwned(t, failures);
            }

            if (s_Metal != null)
                DropOwned(s_Metal, failures);
            if (s_Concrete != null)
                DropOwned(s_Concrete, failures);
            for (int i = 0; i < s_Marking.Length; i++)
            {
                if (s_Marking[i] != null)
                    DropOwned(s_Marking[i], failures);
                s_Marking[i] = null;
            }
            if (s_FaceTemplate != null)
                DropOwned(s_FaceTemplate, failures);
            s_FaceTemplate = null;
            s_FaceTemplateEntry = null;
            for (int colour = 1; colour < s_Leds.Length; colour++)
            {
                if (s_Leds[colour] != null)
                    DropOwned(s_Leds[colour], failures);
                s_Leds[colour] = null;
            }

            try { ClearWarm(failures); }
            catch (Exception error) { failures.Add(error); }
            try { LedScreens.Clear(); }
            catch (Exception error) { failures.Add(error); }
            try { FacePainter.Reset(); }
            catch (Exception error) { failures.Add(error); }
            foreach (Texture2D flat in s_Flat.Values)
            {
                if (flat != null)
                    DropOwned(flat, failures);
            }

            s_Flat.Clear();
            s_TextureBytes = 0L;
            s_TextureCount = 0;

            s_Faces.Clear();
            s_Textures.Clear();
            s_Tints.Clear();
            s_FaceBorn.Clear();
            s_PaintFailures.Clear();
            s_Said.Clear();
            s_Matte.Clear();
            s_LitFaces.Clear();
            s_Metal = null;
            s_Concrete = null;
            s_Night = -1f;
            if (failures.Count != 0) throw new AggregateException("Material cleanup failed; owned resources are retained.", failures);
        }
    }
}
