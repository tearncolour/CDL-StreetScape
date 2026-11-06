using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>The state of one step of the wiring plan (DESIGN-wiring-plan.md section 2): off, a shadow run beside the old path, log only, or on.</summary>
    public enum FeatureState
    {
        Off,
        Shadow,
        Log,
        On
    }

    /// <summary>How a preferences file was found (Preferences.Load).</summary>
    public enum PreferencesRead
    {
        Read,
        Missing,

        /// <summary>Not readable or not a JSON object: the defaults are used, and the file is left alone (Save refuses).</summary>
        Damaged
    }

    /// <summary>
    /// The mod's one small file of choices, ModsData/CDLC.TS/preferences.json (0.12.74): the profile and the state of each step of the wiring plan. Read once at load and written only when something was
    /// changed. Whatever else is in the file (keys of a newer mod, a state this mod does not know) is kept as it is when the file is written again, and a state it cannot read is the step's default.
    /// Pure: the text and the file are the only things it touches.
    /// </summary>
    public sealed class Preferences
    {
        public const string FileName = "preferences.json";
        public const string ProfileKey = "profile";
        public const string FeaturesKey = "features";
        public const string RoadFollowKey = "roadFollow";
        public const string AreaFitWorkerKey = "areaFitWorker";
        public const string PlaceShortcutsKey = "placeShortcuts";
        public const string PictureCutKey = "pictureCut";
        public const string AdaptiveSamplingKey = "adaptiveSampling";
        public const string TextBoxKey = "textBox";
        public const string StyleProfilesKey = "styleProfiles";
        public const string StandBoxKey = "standBox";
        public const string SystemFontsKey = "systemFonts";
        public const string TextSupportsKey = "textSupports";
        public const string LoadWarmKey = "loadWarm";
        public const string GameShaderKey = "gameShader";
        public const string KerbTabKey = "kerbTab";
        public const string ViewDistanceKey = "viewDistance";
        public const string DefaultProfile = "plain";

        private JsonValue m_Root = DefaultRoot();

        /// <summary>Something was set since the file was read or written.</summary>
        public bool Changed { get; private set; }

        /// <summary>The file could not be read as preferences: it is not written over.</summary>
        public bool Protected { get; private set; }

        public string Profile
        {
            get
            {
                JsonValue value = m_Root.Get(ProfileKey);
                return value != null && value.Kind == JsonKind.String && value.Text.Length > 0 ? value.Text : DefaultProfile;
            }
            set => Put(m_Root, ProfileKey, Str(value));
        }

        /// <summary>
        /// How far the signs and markings are drawn (0.12.103, m): the far limit of the tiles (StreamOptions.WithFar). A number within StreamOptions's range as it is; one outside it is brought to the
        /// range; anything else, or nothing, is the default. Not written into a new file: the panel's display settings write it when the player sets it.
        /// </summary>
        public float ViewDistance
        {
            get
            {
                JsonValue value = m_Root.Get(ViewDistanceKey);
                return value != null && value.Kind == JsonKind.Number ? StreamOptions.ClampFar((float)value.Number) : StreamOptions.DefaultFarMetres;
            }
            set
            {
                float metres = StreamOptions.ClampFar(value);
                JsonValue existing = m_Root.Get(ViewDistanceKey);
                if (existing != null && existing.Kind == JsonKind.Number && Math.Abs(existing.Number - metres) < 1e-6)
                    return;
                Replace(m_Root, ViewDistanceKey, new JsonValue { Kind = JsonKind.Number, Number = metres });
                Changed = true;
            }
        }

        /// <summary>The step's state; the fallback when the file has none for it or one this mod does not know.</summary>
        public FeatureState Feature(string key, FeatureState fallback)
        {
            JsonValue value = m_Root.Get(FeaturesKey)?.Get(key);
            return value != null && value.Kind == JsonKind.String && TryState(value.Text, out FeatureState state) ? state : fallback;
        }

        public void SetFeature(string key, FeatureState state)
        {
            JsonValue features = m_Root.Get(FeaturesKey);
            if (features == null || features.Kind != JsonKind.Object)
            {
                features = new JsonValue { Kind = JsonKind.Object };
                Replace(m_Root, FeaturesKey, features);
            }

            Put(features, key, Str(StateName(state)));
        }

        /// <summary>Road follow (0.12.82: on by default; a file that says log only looks and counts, off does nothing).</summary>
        public FeatureState RoadFollow => Feature(RoadFollowKey, FeatureState.On);

        /// <summary>Where the area fits are cut (0.12.74): on a worker unless the file says off (the main thread as before) or shadow (both, compared); log reads as on.</summary>
        public FeatureState AreaFitWorker => Feature(AreaFitWorkerKey, FeatureState.On);

        /// <summary>The game's keyboard shortcuts held back while a placement tool is active: on unless the file says off; any other state reads as on.</summary>
        public FeatureState PlaceShortcuts => Feature(PlaceShortcutsKey, FeatureState.On);
        /// <summary>How a custom picture's board is cut in cut mode (0.12.74): off as 0.12.72 did; shadow (the default, and log) that way, with the cut from the picture's contour made off the building thread and compared; on from the contour.</summary>
        public FeatureState PictureCut => Feature(PictureCutKey, FeatureState.Shadow);
        /// <summary>The sampler of the roads' surface (0.12.75): off the fixed 16 steps, shadow the adaptive sampler is made beside them and compared (the default), on its bands are used; log reads as shadow.</summary>
        public FeatureState AdaptiveSampling
        {
            get
            {
                FeatureState state = Feature(AdaptiveSamplingKey, FeatureState.Shadow);
                return state == FeatureState.Log ? FeatureState.Shadow : state;
            }
        }

        /// <summary>The text light box (0.12.75, form 1: channel letters) is in the catalogue: on unless the file says off; any other state reads as on. Boxes already placed are drawn either way.</summary>
        public FeatureState TextBox => Feature(TextBoxKey, FeatureState.On);

        /// <summary>The support style profiles (0.12.75): on unless the file says off (the plain profile whatever was chosen, and no row in the panel); any other state reads as on.</summary>
        public FeatureState StyleProfiles => Feature(StyleProfilesKey, FeatureState.On) == FeatureState.Off ? FeatureState.Off : FeatureState.On;

        /// <summary>The standing light box (0.12.76) is in the catalogue: on unless the file says off; any other state reads as on. Boxes already placed are drawn either way.</summary>
        public FeatureState StandBox => Feature(StandBoxKey, FeatureState.On) == FeatureState.Off ? FeatureState.Off : FeatureState.On;
        /// <summary>The system's fonts in the text light box's font list (0.12.76): on unless the file says off; any other state reads as on.</summary>
        public FeatureState SystemFonts => Feature(SystemFontsKey, FeatureState.On) == FeatureState.Off ? FeatureState.Off : FeatureState.On;
        /// <summary>What the text light box's supports do (0.12.76): a gantry for a long text, channels behind its rails, a frame on a wall or a roof, an arc on a curved wall; on unless the file says off (then as 0.12.75 had it).</summary>
        public FeatureState TextSupports => Feature(TextSupportsKey, FeatureState.On) == FeatureState.Off ? FeatureState.Off : FeatureState.On;

        /// <summary>The kerb styles as the members of the mod's tab in the Roads menu (0.12.85): on unless the file says off (then they stay in the template's group and the tab is not listed). Not written into a new file.</summary>
        public FeatureState KerbTab => Feature(KerbTabKey, FeatureState.On) == FeatureState.Off ? FeatureState.Off : FeatureState.On;

        /// <summary>The first-time work done under the loading screen (0.12.78: the items put back early, bigger budgets, a hold on the loading screen, a warm-up): on unless the file says off; any other state reads as on.</summary>
        public FeatureState LoadWarm => Feature(LoadWarmKey, FeatureState.On) == FeatureState.Off ? FeatureState.Off : FeatureState.On;

        /// <summary>
        /// The gameShader line (0.12.83: this mod's materials drawn with the game's shader, GameShaderPolicy): on or off; null when the file has none or another state. The line is never written by default:
        /// a file made before keeps no line, so the default of the code has to be the one that is right for it.
        /// </summary>
        public FeatureState? GameShaderLine
        {
            get
            {
                FeatureState state = Feature(GameShaderKey, FeatureState.Shadow);
                return state == FeatureState.On || state == FeatureState.Off ? state : (FeatureState?)null;
            }
        }

        public static string StateName(FeatureState state) => state.ToString().ToLowerInvariant();

        public static bool TryState(string name, out FeatureState state)
        {
            foreach (FeatureState each in (FeatureState[])Enum.GetValues(typeof(FeatureState)))
            {
                if (string.Equals(StateName(each), name, StringComparison.Ordinal))
                {
                    state = each;
                    return true;
                }
            }

            state = FeatureState.Off;
            return false;
        }

        /// <summary>The preferences of a text; the defaults, and what is wrong, when it is not a JSON object.</summary>
        public static Preferences Parse(string text, out string problem)
        {
            var preferences = new Preferences();
            if (!MiniJson.TryParse(text, out JsonValue root, out string error))
            {
                problem = "not JSON (" + error + ")";
                preferences.Protected = true;
                return preferences;
            }

            if (root.Kind != JsonKind.Object)
            {
                problem = "not a JSON object";
                preferences.Protected = true;
                return preferences;
            }

            problem = null;
            preferences.m_Root = root;
            return preferences;
        }

        /// <summary>The file's preferences: the defaults when it is not there; the defaults, protected from being written over, when it is damaged.</summary>
        public static Preferences Load(string path, out PreferencesRead how, out string problem)
        {
            problem = null;
            if (!File.Exists(path))
            {
                how = PreferencesRead.Missing;
                return new Preferences();
            }

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                how = PreferencesRead.Damaged;
                problem = "not readable (" + ex.Message + ")";
                return new Preferences { Protected = true };
            }

            Preferences preferences = Parse(text, out problem);
            how = preferences.Protected ? PreferencesRead.Damaged : PreferencesRead.Read;
            return preferences;
        }

        /// <summary>The file's text: two-space indent, the members in the order they were read, anything unknown kept.</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            Write(text, m_Root, 0);
            text.Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// Writes the file (the folder is made): to a file beside it first and then in its place, so that a crash leaves the old file or the new one and never half of one. False when the preferences are
        /// protected (the file was damaged and is left for the player to look at).
        /// </summary>
        public bool Save(string path)
        {
            if (Protected)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, ToText(), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
            Changed = false;
            return true;
        }

        private static JsonValue DefaultRoot()
        {
            var features = new JsonValue { Kind = JsonKind.Object };
            features.Members.Add(new KeyValuePair<string, JsonValue>(RoadFollowKey, Str(StateName(FeatureState.On))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(AreaFitWorkerKey, Str(StateName(FeatureState.On))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(PlaceShortcutsKey, Str(StateName(FeatureState.On))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(PictureCutKey, Str(StateName(FeatureState.Shadow))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(AdaptiveSamplingKey, Str(StateName(FeatureState.Shadow))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(TextBoxKey, Str(StateName(FeatureState.On))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(StyleProfilesKey, Str(StateName(FeatureState.On))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(StandBoxKey, Str(StateName(FeatureState.On))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(SystemFontsKey, Str(StateName(FeatureState.On))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(TextSupportsKey, Str(StateName(FeatureState.On))));
            features.Members.Add(new KeyValuePair<string, JsonValue>(LoadWarmKey, Str(StateName(FeatureState.On))));
            var root = new JsonValue { Kind = JsonKind.Object };
            root.Members.Add(new KeyValuePair<string, JsonValue>(ProfileKey, Str(DefaultProfile)));
            root.Members.Add(new KeyValuePair<string, JsonValue>(FeaturesKey, features));
            return root;
        }

        private static JsonValue Str(string text) => new JsonValue { Kind = JsonKind.String, Text = text ?? "" };

        /// <summary>Sets a string member of an object; a value the file already holds is not a change.</summary>
        private void Put(JsonValue owner, string key, JsonValue value)
        {
            JsonValue existing = owner.Get(key);
            if (existing != null && existing.Kind == JsonKind.String && existing.Text == value.Text)
                return;
            Replace(owner, key, value);
            Changed = true;
        }

        private static void Replace(JsonValue owner, string key, JsonValue value)
        {
            for (int i = owner.Members.Count - 1; i >= 0; i--)
            {
                if (owner.Members[i].Key == key)
                {
                    owner.Members[i] = new KeyValuePair<string, JsonValue>(key, value);
                    return;
                }
            }

            owner.Members.Add(new KeyValuePair<string, JsonValue>(key, value));
        }

        private static void Write(StringBuilder text, JsonValue value, int depth)
        {
            switch (value.Kind)
            {
                case JsonKind.Object:
                    if (value.Members.Count == 0)
                    {
                        text.Append("{}");
                        return;
                    }

                    text.Append("{\n");
                    for (int i = 0; i < value.Members.Count; i++)
                    {
                        text.Append(' ', 2 * (depth + 1));
                        JsonWriter.AppendQuoted(text, value.Members[i].Key);
                        text.Append(": ");
                        Write(text, value.Members[i].Value, depth + 1);
                        text.Append(i + 1 < value.Members.Count ? ",\n" : "\n");
                    }

                    text.Append(' ', 2 * depth).Append('}');
                    return;
                case JsonKind.Array:
                    text.Append('[');
                    for (int i = 0; i < value.Items.Count; i++)
                    {
                        if (i > 0)
                            text.Append(", ");
                        Write(text, value.Items[i], depth + 1);
                    }

                    text.Append(']');
                    return;
                case JsonKind.String:
                    JsonWriter.AppendQuoted(text, value.Text);
                    return;
                case JsonKind.Number:
                    text.Append(value.Number.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case JsonKind.Bool:
                    text.Append(value.Bool ? "true" : "false");
                    return;
                default:
                    text.Append("null");
                    return;
            }
        }
    }
}
