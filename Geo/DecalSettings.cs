using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The decal layers of the game (Game.Rendering.DecalLayers, a flags enum) as numbers, for the pure code and for the experiment file. The game keeps a material's mask in the float property
    /// colossal_DecalLayerMask as the float's bits (math.asfloat of the int), so a float read back is read with FromFloat: 0x20 is not 32.0.
    /// </summary>
    public static class DecalMasks
    {
        public const int Terrain = 1, Roads = 2, Buildings = 4, Vehicles = 8, Creatures = 0x10, Other = 0x20;

        private static readonly (string Name, int Bit)[] kNames =
        {
            ("Terrain", Terrain), ("Roads", Roads), ("Buildings", Buildings), ("Vehicles", Vehicles), ("Creatures", Creatures), ("Other", Other)
        };

        /// <summary>
        /// The rendering layer mask of a renderer whose HDRP/Lit material takes the given decal layers. HDRP has no decal property on that shader: its depth prepass writes the renderer's mask into the
        /// decal layer buffer, bits 0 to 7 for the light layers and 8 to 15 for the decal layers (HDRenderPipeline sets 257, bit 0 and bit 8, as the default mask), and a decal clips where its layers
        /// and the buffer's share no bit. The game's layer flags are those bits (DecalLayers.Terrain is decal layer 0). Every other bit stays set, as the game's own batches have them (a flag above the
        /// eighth lands in the bits that are set already).
        /// </summary>
        public static uint RenderingLayerFor(int decalLayers) => 0xFFFF00FFu | ((uint)decalLayers << 8);

        /// <summary>A rendering layer mask for the log: the number and the decal layers in its bits 8 to 15.</summary>
        public static string DescribeRendering(uint mask) => Hex(unchecked((int)mask)) + " (decal layers " + Describe((int)((mask >> 8) & 0xFF)) + ")";

        public static float ToFloat(int mask) => BitConverter.ToSingle(BitConverter.GetBytes(mask), 0);

        public static int FromFloat(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        public static string Hex(int mask) => "0x" + mask.ToString("x", CultureInfo.InvariantCulture);

        /// <summary>A mask for a person: "Buildings|Other (0x24)", "none (0x0)", "0x40" for a bit that is no layer of the game, "0x3f800000 (float bits)" for what a mod stored as a float.</summary>
        public static string Describe(int mask)
        {
            if (mask == 0)
                return "none (0x0)";
            var names = new List<string>();
            int rest = mask;
            foreach ((string name, int bit) in kNames)
            {
                if ((rest & bit) != 0)
                {
                    names.Add(name);
                    rest &= ~bit;
                }
            }

            if (names.Count == 0)
                return Hex(mask) + (mask < 0 || mask > 0xFFFF ? " (not a layer: a float's bits?)" : "");
            if (rest != 0)
                names.Add(Hex(rest));
            return string.Join("|", names) + " (" + Hex(mask) + ")";
        }

        /// <summary>
        /// A mask written as layer names, numbers or both joined by | + or ,: "Vehicles", "Buildings|Other", "0x40", "8", "none". Case does not matter. False for anything else (the experiment file
        /// says which line).
        /// </summary>
        public static bool TryParse(string text, out int mask)
        {
            mask = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            foreach (string raw in text.Split('|', '+', ','))
            {
                string token = raw.Trim();
                if (token.Length == 0)
                    return false;
                if (string.Equals(token, "none", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
                {
                    mask |= Terrain | Roads | Buildings | Vehicles | Creatures | Other;
                    continue;
                }

                int bit = 0;
                foreach ((string name, int value) in kNames)
                {
                    if (string.Equals(name, token, StringComparison.OrdinalIgnoreCase))
                        bit = value;
                }

                if (bit == 0 && !TryNumber(token, out bit))
                    return false;
                mask |= bit;
            }

            return true;
        }

        internal static bool TryNumber(string token, out int value)
        {
            bool hex = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (hex)
            {
                bool ok = uint.TryParse(token.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint bits);
                value = unchecked((int)bits);
                return ok;
            }

            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>
    /// What this mod's materials say about decals (0.12.80). A sign, a post or a barrier is an object, not a surface the game's decals are for. The game's own mechanism is the material property
    /// colossal_DecalLayerMask (the layers an object is on; a decal lists the layers it lands on) and, in HDRP, _SupportDecals, which ValidateMaterial turns into the keyword _DISABLE_DECALS.
    /// Until 0.12.79 every material was on the layer Other (0x20); 0.12.80 moved the materials that have the property to Vehicles. That reached only the shader graphs (the baked details, the
    /// glass): HDRP/Lit, which draws the posts, the frames and the faces, has no such property, takes its decal layers from the renderer's rendering layer mask, and every renderer was made with
    /// all bits set, so the game's decals (a prefab's DecalProperties lists Terrain, Roads and Buildings, a surface's RenderedArea Terrain) landed on it (the owner's pole, 3 Nov, 0.12.81).
    /// The census of the game's materials says little about which layers its decals list: the batch system overwrites the property from the prefab's own mask (and a surface asset keeps it as a number, the census' 1.0, 3.0 and 2.0). The mask is only the second line:
    /// _SupportDecals 0 with the keyword is the first, and the keyword is checked after ValidateMaterial (DisableKeywordWanted).
    /// </summary>
    public static class DecalPolicy
    {
        /// <summary>The layer of every material of this mod that has the property: the one the game gives its vehicles, which no prefab of the game lists by default.</summary>
        public const int ObjectMask = DecalMasks.Vehicles;

        /// <summary>
        /// The decal layers the renderers of this mod take through their rendering layer mask: all eight, as 0.12.81 (every bit set). HDRP's own decal layers are the bits 8 to 15, but a renderer with those
        /// at 0 (0xff, tried by the owner on 3 Nov) still took the paving, so the game's decal shader either reads other bits or does not test the receiver: the default waits for the owner's tests.
        /// </summary>
        public const int ObjectRenderingDecals = 0xFF;

        public static readonly uint ObjectRenderingLayer = DecalMasks.RenderingLayerFor(ObjectRenderingDecals);

        /// <summary>Whether the materials take the game's decals: they do not.</summary>
        public const float SupportDecals = 0f;

        public const string DisableKeyword = "_DISABLE_DECALS";

        /// <summary>
        /// What the keyword _DISABLE_DECALS must be on a material: on when it does not support decals, null (leave it) when the material has no _SupportDecals (the keyword is not its to
        /// decide). The experiment's disableDecals overrides.
        /// </summary>
        public static bool? DisableKeywordWanted(bool hasSupportProperty, float supportDecals, bool? forced) =>
            forced ?? (hasSupportProperty ? supportDecals == 0f : (bool?)null);
    }

    /// <summary>
    /// The experiment file (ModsData/CDLC.TS/material-experiment.txt): lines of "key = value" that change the decal settings of every material of this mod while the game runs, to find which one
    /// removes a wrong texture on a sign; deleting the file puts the defaults back. Keys: decalMask (layer names, 0x40, 8 or none, joined by |), supportDecals (0 or 1), disableDecals
    /// (on or off: the keyword), queue (1000 to 5000), renderingLayer (a number, 0x..., all or none): the renderers' whole rendering layer mask, renderingDecals (layer names, 0x40, 8 or none): the
    /// decal layers the renderers take, every other bit as the game's batches have it; gameShader (off, plain, faces or lit): how much of this mod's HDRP/Lit materials is drawn with the game's shader
    /// (GameShaderMode). The last of the two lines wins. # starts a comment. Pure.
    /// </summary>
    public sealed class MaterialExperiment : IEquatable<MaterialExperiment>
    {
        public static readonly MaterialExperiment None = new MaterialExperiment();

        public int? DecalMask;
        public float? SupportDecals;
        public bool? DisableDecals;
        public int? Queue;
        public uint? RenderingLayer;
        public GameShaderMode? GameShader;

        /// <summary>The ceiling of a near-white surface's light (AlbedoCap), AlbedoCap.Lowest to Highest.</summary>
        public float? WhiteMax;

        public bool IsEmpty => !DecalMask.HasValue && !SupportDecals.HasValue && !DisableDecals.HasValue && !Queue.HasValue && !RenderingLayer.HasValue && !GameShader.HasValue && !WhiteMax.HasValue;

        /// <summary>Reads the text; a line that does not parse is left out and named in problems (the others still count); a key given twice takes its last line.</summary>
        public static MaterialExperiment Parse(string text, List<string> problems)
        {
            var result = new MaterialExperiment();
            if (text == null)
                return result;
            int number = 0;
            foreach (string raw in text.Split('\n'))
            {
                number++;
                string line = raw.Trim();
                int hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash).Trim();
                if (line.Length == 0)
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    problems?.Add($"line {number}: no key = value");
                    continue;
                }

                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (!Assign(result, key, value, out string why))
                    problems?.Add($"line {number}: {why}");
            }

            return result;
        }

        private static bool Assign(MaterialExperiment e, string key, string value, out string why)
        {
            why = null;
            switch (key.ToLowerInvariant())
            {
                case "decalmask":
                    if (!DecalMasks.TryParse(value, out int mask))
                    {
                        why = "decalMask '" + value + "' is not layer names, a number or none";
                        return false;
                    }

                    e.DecalMask = mask;
                    return true;
                case "supportdecals":
                    if (!TryFlag(value, out bool on))
                    {
                        why = "supportDecals '" + value + "' is not 0 or 1";
                        return false;
                    }

                    e.SupportDecals = on ? 1f : 0f;
                    return true;
                case "disabledecals":
                    if (!TryFlag(value, out bool disable))
                    {
                        why = "disableDecals '" + value + "' is not on or off";
                        return false;
                    }

                    e.DisableDecals = disable;
                    return true;
                case "queue":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int queue) || queue < 1000 || queue > 5000)
                    {
                        why = "queue '" + value + "' is not a number from 1000 to 5000";
                        return false;
                    }

                    e.Queue = queue;
                    return true;
                case "renderinglayer":
                    if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        e.RenderingLayer = uint.MaxValue;
                        return true;
                    }

                    if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        e.RenderingLayer = 0u;
                        return true;
                    }

                    if (!DecalMasks.TryNumber(value, out int layer))
                    {
                        why = "renderingLayer '" + value + "' is not a number, all or none";
                        return false;
                    }

                    e.RenderingLayer = unchecked((uint)layer);
                    return true;
                case "renderingdecals":
                    if (!DecalMasks.TryParse(value, out int decals) || (decals & ~0xFF) != 0)
                    {
                        why = "renderingDecals '" + value + "' is not layer names, a number up to 0xff or none";
                        return false;
                    }

                    e.RenderingLayer = DecalMasks.RenderingLayerFor(decals);
                    return true;
                case "gameshader":
                    if (!GameShaderRoute.TryParse(value, out GameShaderMode shader))
                    {
                        why = "gameShader '" + value + "' is not off, plain, faces or lit";
                        return false;
                    }

                    e.GameShader = shader;
                    return true;
                case "whitemax":
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float white) || float.IsNaN(white) || white < AlbedoCap.Lowest || white > AlbedoCap.Highest)
                    {
                        why = "whiteMax '" + value + "' is not a number from " + AlbedoCap.Lowest.ToString("0.0#", CultureInfo.InvariantCulture) + " to " + AlbedoCap.Highest.ToString("0.0#", CultureInfo.InvariantCulture);
                        return false;
                    }

                    e.WhiteMax = white;
                    return true;
                default:
                    why = "unknown key '" + key + "'";
                    return false;
            }
        }

        private static bool TryFlag(string value, out bool on)
        {
            switch (value.ToLowerInvariant())
            {
                case "1":
                case "on":
                case "true":
                case "yes":
                    on = true;
                    return true;
                case "0":
                case "off":
                case "false":
                case "no":
                    on = false;
                    return true;
                default:
                    on = false;
                    return false;
            }
        }

        public bool Equals(MaterialExperiment other) =>
            other != null && DecalMask == other.DecalMask && SupportDecals == other.SupportDecals && DisableDecals == other.DisableDecals && Queue == other.Queue && RenderingLayer == other.RenderingLayer && GameShader == other.GameShader && WhiteMax == other.WhiteMax;

        public override bool Equals(object obj) => Equals(obj as MaterialExperiment);

        public override int GetHashCode() => (DecalMask ?? -1) ^ (Queue ?? 0) ^ (int)(RenderingLayer ?? 0u) ^ ((int?)GameShader ?? -1) ^ (int)((WhiteMax ?? 0f) * 1000f);

        /// <summary>For the log: what is set, "nothing" when the defaults are back.</summary>
        public string Describe()
        {
            if (IsEmpty)
                return "nothing (the defaults)";
            var parts = new List<string>();
            if (DecalMask.HasValue)
                parts.Add("decalMask " + DecalMasks.Describe(DecalMask.Value));
            if (SupportDecals.HasValue)
                parts.Add("supportDecals " + SupportDecals.Value.ToString("0", CultureInfo.InvariantCulture));
            if (DisableDecals.HasValue)
                parts.Add("disableDecals " + (DisableDecals.Value ? "on" : "off"));
            if (Queue.HasValue)
                parts.Add("queue " + Queue.Value.ToString(CultureInfo.InvariantCulture));
            if (RenderingLayer.HasValue)
                parts.Add("renderingLayer " + DecalMasks.DescribeRendering(RenderingLayer.Value));
            if (GameShader.HasValue)
                parts.Add("gameShader " + GameShaderRoute.Name(GameShader.Value));
            if (WhiteMax.HasValue)
                parts.Add("whiteMax " + WhiteMax.Value.ToString("0.0#", CultureInfo.InvariantCulture));
            return string.Join(", ", parts);
        }
    }

    /// <summary>What a material says about decals, as read back from the engine (a snapshot is compared by its key).</summary>
    public struct DecalSnapshot
    {
        public string Shader;
        public int Queue;
        public bool HasSupport, HasMask, KeywordOn, Vt;
        public float Support;
        public int Mask;
        public string Keywords;

        public string Key => $"{Shader}|{Queue}|{(HasSupport ? Support.ToString("0.##", CultureInfo.InvariantCulture) : "-")}|{(HasMask ? Mask.ToString(CultureInfo.InvariantCulture) : "-")}|{KeywordOn}|{Vt}";

        public override string ToString() =>
            $"{Shader}, queue {Queue}, _SupportDecals {(HasSupport ? Support.ToString("0.##", CultureInfo.InvariantCulture) : "none")}, layer {(HasMask ? DecalMasks.Describe(Mask) : "none (no property)")}, "
            + $"{DecalPolicy.DisableKeyword} {(KeywordOn ? "on" : "off")}, VT {(Vt ? "on" : "off")}, keywords [{Keywords}]";
    }

    /// <summary>
    /// The audit of the materials this mod makes (0.12.80): per family (solid, face, detail, glass ...) a line for each different state seen, a few at most, and a count of the rest, so that the
    /// log says what the game was given without a line per material. Pure.
    /// </summary>
    public sealed class DecalAudit
    {
        private const int kLinesPerFamily = 3, kLines = 24;

        private sealed class Family
        {
            public int Made;
            public readonly Dictionary<string, int> States = new Dictionary<string, int>();
        }

        private readonly Dictionary<string, Family> m_Families = new Dictionary<string, Family>();
        private int m_Lines;

        /// <summary>Counts one material; the line to log when this is a state of the family not seen before (and the caps allow it), else null.</summary>
        public string Note(string family, DecalSnapshot state)
        {
            if (!m_Families.TryGetValue(family, out Family f))
                m_Families[family] = f = new Family();
            f.Made++;
            string key = state.Key;
            bool isNew = !f.States.ContainsKey(key);
            f.States[key] = (isNew ? 0 : f.States[key]) + 1;
            if (!isNew || f.States.Count > kLinesPerFamily || m_Lines >= kLines)
                return null;
            m_Lines++;
            return $"Materials: decal audit, {family}: {state}";
        }

        /// <summary>One line: how many of each family were made and in how many states, in the order the families were first seen.</summary>
        public string Summary()
        {
            var sb = new StringBuilder("Materials: decal audit summary: ");
            bool first = true;
            foreach (KeyValuePair<string, Family> pair in m_Families)
            {
                sb.Append(first ? "" : "; ").Append(pair.Key).Append(' ').Append(pair.Value.Made).Append(" made in ").Append(pair.Value.States.Count).Append(pair.Value.States.Count == 1 ? " state" : " states");
                first = false;
            }

            return first ? "Materials: decal audit summary: none made" : sb.ToString();
        }

        public int Families => m_Families.Count;

        public void Clear()
        {
            m_Families.Clear();
            m_Lines = 0;
        }
    }

    /// <summary>The census of the game's decal materials: which layers they land on, so that the log says which ones list Other (the layer of this mod's objects until 0.12.79) and which list Vehicles. Pure.</summary>
    public static class DecalCensus
    {
        /// <summary>One decal material: its mask as int bits, or null when it has no colossal_DecalLayerMask.</summary>
        public struct Item
        {
            public int? Mask;
        }

        /// <summary>"Material census: N decal material(s): 0x24 Buildings|Other x224, ...; listing Other x, Vehicles y, 0x40 z, no mask w" (at most 8 masks, the commonest first).</summary>
        public static string Format(IList<Item> decals)
        {
            var counts = new Dictionary<int, int>();
            int none = 0, other = 0, vehicles = 0, spare = 0;
            foreach (Item d in decals)
            {
                if (!d.Mask.HasValue)
                {
                    none++;
                    continue;
                }

                int m = d.Mask.Value;
                counts[m] = (counts.TryGetValue(m, out int n) ? n : 0) + 1;
                other += (m & DecalMasks.Other) != 0 ? 1 : 0;
                vehicles += (m & DecalMasks.Vehicles) != 0 ? 1 : 0;
                spare += (m & 0x40) != 0 ? 1 : 0;
            }

            var list = new List<KeyValuePair<int, int>>(counts);
            list.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));
            var sb = new StringBuilder($"Materials: census of the game's decals: {decals.Count} material(s): ");
            for (int i = 0; i < list.Count && i < 8; i++)
                sb.Append(i == 0 ? "" : ", ").Append(DecalMasks.Describe(list[i].Key)).Append(" x").Append(list[i].Value);
            if (list.Count > 8)
                sb.Append(", and ").Append(list.Count - 8).Append(" more mask(s)");
            sb.Append($"; listing Other {other}, Vehicles {vehicles}, 0x40 {spare}; no mask property {none}.");
            return sb.ToString();
        }
    }
}
