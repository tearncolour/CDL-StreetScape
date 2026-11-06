using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// How much of this mod's own HDRP/Lit materials is drawn with the game's shader-graph material (BH/SG_DefaultShader, the one the baked-detail barriers already use) instead (0.12.83). The game's
    /// decals decide what they land on by that shader's property colossal_DecalLayerMask, which HDRP/Lit has not: a barrier on the game's shader with the layer Vehicles stood clean in the paving of a
    /// lot where the poles, the frames and the faces on HDRP/Lit were painted (the owner, 3 Nov). Each step contains the one before it.
    /// </summary>
    public enum GameShaderMode
    {
        /// <summary>HDRP/Lit as always.</summary>
        Off = 0,

        /// <summary>The plain solids, the steel, the concrete and the markings: the posts, beams and frames.</summary>
        Plain = 1,

        /// <summary>And the faces of the boards that are not lit from inside.</summary>
        Faces = 2,

        /// <summary>
        /// And what glows: the faces lit from inside, the lanterns and neon. Not the LED dots (0.12.92): the game's graph does not read an emission in nits as HDRP/Lit does, and the owner's game showed it
        /// (5 Nov, by day): the same 6000 nits were blown out into a huge bloom on the game's shader and gave no glow at all on HDRP/Lit, so the dots stay on HDRP/Lit, where the number is a real luminance.
        /// </summary>
        Lit = 3
    }

    /// <summary>Which of this mod's material families go to the game's shader at each step. Pure.</summary>
    public static class GameShaderRoute
    {
        /// <summary>
        /// Whether a material of the family (the word of TsMaterials.FamilyOf: solid, metal, concrete, marking, face, led, glow) is drawn with the game's shader at the mode. Lit is a face lit from inside. The baked
        /// details and the glass are the game's materials already and are never moved; a family that is not known stays.
        /// </summary>
        public static bool Moves(GameShaderMode mode, string family, bool lit)
        {
            switch (family)
            {
                case "solid":
                case "metal":
                case "concrete":
                case "marking":
                    return mode >= GameShaderMode.Plain;
                case "face":
                    return mode >= (lit ? GameShaderMode.Lit : GameShaderMode.Faces);
                case "glow":
                    return mode >= GameShaderMode.Lit;
                // The LED dots stay on HDRP/Lit at every mode (see GameShaderMode.Lit).
                case "led":
                    return false;
                default:
                    return false;
            }
        }

        public static string Name(GameShaderMode mode) => mode.ToString().ToLowerInvariant();

        /// <summary>The family of a material made by MakeLit, for the audit and the route: the word after "CDLTS " in its name (solid, metal, concrete, marking, face, led, glow); a name that does not start so is its first word.</summary>
        public static string FamilyOf(string name)
        {
            string rest = name != null && name.StartsWith("CDLTS ", StringComparison.Ordinal) ? name.Substring(6) : name ?? "other";
            int space = rest.IndexOf(' ');
            return space > 0 ? rest.Substring(0, space) : rest;
        }

        /// <summary>The mode a word of the experiment file names: off, plain, faces or lit, any case.</summary>
        public static bool TryParse(string text, out GameShaderMode mode)
        {
            foreach (GameShaderMode each in (GameShaderMode[])Enum.GetValues(typeof(GameShaderMode)))
            {
                if (string.Equals(Name(each), text, StringComparison.OrdinalIgnoreCase))
                {
                    mode = each;
                    return true;
                }
            }

            mode = GameShaderMode.Off;
            return false;
        }
    }

    /// <summary>Which mode is wanted: the experiment file, else the preferences' gameShader, else the default. Pure.</summary>
    public static class GameShaderPolicy
    {
        /// <summary>
        /// What a file with no gameShader line gets: everything, what glows included (0.12.90). 0.12.83 made it faces (the owner asked for it on before anyone had seen the look of the game's shader on the
        /// posts), and left what glows on HDRP/Lit, where the building's paving decal still painted the glowing text light box; on 4 Nov he set `gameShader = lit` in the experiment file and said the material
        /// was right ("材质没问题了"). A player turns it off with "gameShader": "off" in preferences.json or the experiment file's gameShader line, or down to faces or plain there.
        /// </summary>
        public const GameShaderMode DefaultMode = GameShaderMode.Lit;

        /// <summary>What "on" in the preferences means: the same as the default.</summary>
        public const GameShaderMode OnMode = GameShaderMode.Lit;

        /// <summary>The preference is null when the file has no such line; any state but on and off reads as no line.</summary>
        public static GameShaderMode Resolve(FeatureState? preference, GameShaderMode? experiment)
        {
            if (experiment.HasValue)
                return experiment.Value;
            if (preference == FeatureState.On)
                return OnMode;
            return preference == FeatureState.Off ? GameShaderMode.Off : DefaultMode;
        }
    }

    /// <summary>What the game's template material must have for the mode to work at all, and what only the glowing step needs. Pure: the engine asks Material.HasProperty through a delegate.</summary>
    public static class GameShaderNeeds
    {
        /// <summary>The properties of the baked-detail recipe, and the decal layer, without which moving a material would only change its look and not its decals.</summary>
        public static readonly string[] Required = { "_BaseColor", "_BaseColorMap", "_MaskMap", "_NormalMap", "colossal_DecalLayerMask" };

        /// <summary>The emission the glowing families write (_EmissiveColor, as on HDRP/Lit) and the map that lets it through (Write Everywhere sets a white one on this shader).</summary>
        public static readonly string[] Emission = { "_EmissiveColor", "_EmissiveColorMap" };

        /// <summary>The names the template lacks of the required ones (and, for the glowing step, of the emission ones).</summary>
        public static List<string> Missing(GameShaderMode mode, Func<string, bool> has)
        {
            var missing = new List<string>();
            if (mode == GameShaderMode.Off)
                return missing;
            foreach (string name in Required)
            {
                if (!has(name))
                    missing.Add(name);
            }

            if (mode == GameShaderMode.Lit)
            {
                foreach (string name in Emission)
                {
                    if (!has(name))
                        missing.Add(name);
                }
            }

            return missing;
        }

        /// <summary>
        /// The mode the template allows: the wanted one; faces when the glowing step lacks its emission; off when a required property is missing (a missing template is the caller's: it passes a has that says no).
        /// </summary>
        public static GameShaderMode Allowed(GameShaderMode wanted, Func<string, bool> has)
        {
            if (wanted == GameShaderMode.Off || Missing(GameShaderMode.Plain, has).Count > 0)
                return GameShaderMode.Off;
            return wanted == GameShaderMode.Lit && Missing(GameShaderMode.Lit, has).Count > 0 ? GameShaderMode.Faces : wanted;
        }
    }

    /// <summary>What a plain material becomes on the game's shader: the maps that carry what HDRP/Lit had as scalars. Pure.</summary>
    public static class GameShaderLook
    {
        /// <summary>
        /// The mask map of a plain material, as the baked details write theirs (DetailPixels.Maps): metallic in R, the clear coat 0 in G, 0 in B, smoothness in A, in bytes (rounded to the nearest, ties to even). The
        /// shader's own scalars _Metallic and _Smoothness are left as the template has them, as the details leave them.
        /// </summary>
        public static void MaskBytes(float metallic, float smoothness, out byte metal, out byte smooth)
        {
            metal = ToByte(metallic);
            smooth = ToByte(smoothness);
        }

        /// <summary>A colour value 0 to 1 as a byte, ties to even (as Mathf.RoundToInt), clamped.</summary>
        public static byte ToByte(float value) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value * 255f)));

        /// <summary>
        /// The bytes of the colour map of a plain material: the colour HDRP/Lit was given (a Color property, which Unity takes as gamma and hands the shader as linear light) as the same numbers in an sRGB map,
        /// so that the game's shader reads the same linear light and the post keeps the colour it had. (The baked details bake the colour's gamma form instead: their linear light is the colour itself, which is
        /// brighter than HDRP/Lit's for the same number if Unity converts a Color property; not seen in the game.)
        /// </summary>
        public static void ColourBytes(float r, float g, float b, out byte red, out byte green, out byte blue)
        {
            red = ToByte(r);
            green = ToByte(g);
            blue = ToByte(b);
        }
    }

    /// <summary>One material of the registry as the plan sees it: its family, whether it is lit from inside, and whether it is on the game's shader now.</summary>
    public struct GameShaderEntry
    {
        public string Family;
        public bool Lit;
        public bool OnGame;
    }

    /// <summary>What a change of mode does to the materials that exist: which go to the game's shader and which back to HDRP/Lit. Pure.</summary>
    public static class GameShaderSwap
    {
        public static void Plan(GameShaderMode mode, IList<GameShaderEntry> entries, List<int> toGame, List<int> toHdrp)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                GameShaderEntry e = entries[i];
                bool want = GameShaderRoute.Moves(mode, e.Family, e.Lit);
                if (want && !e.OnGame)
                    toGame.Add(i);
                else if (!want && e.OnGame)
                    toHdrp.Add(i);
            }
        }

        /// <summary>One line for the log: how many went each way, by family.</summary>
        public static string Describe(GameShaderMode mode, IList<GameShaderEntry> entries, IList<int> toGame, IList<int> toHdrp)
        {
            return $"game shader {GameShaderRoute.Name(mode)}: {Count(entries, toGame)} to the game's shader, {Count(entries, toHdrp)} back to HDRP/Lit";
        }

        private static string Count(IList<GameShaderEntry> entries, IList<int> which)
        {
            if (which.Count == 0)
                return "0";
            var families = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (int i in which)
            {
                string family = entries[i].Family ?? "?";
                families[family] = (families.TryGetValue(family, out int n) ? n : 0) + 1;
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in families)
                parts.Add(pair.Value + " " + pair.Key);
            return which.Count + " (" + string.Join(", ", parts) + ")";
        }
    }
}
