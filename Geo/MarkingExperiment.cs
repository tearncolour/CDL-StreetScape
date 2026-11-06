using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The experiment file of the road paint (ModsData/CDLC.TS/marking-experiment.txt, 0.12.83): lines of "key = value" that change how the paint of the markings looks while the game runs, so that a
    /// player can compare without a restart; deleting the file puts the defaults back. Keys: markingNormals (on or off: the paint's normals follow the ground or are straight up), markingGrain (off,
    /// or 0 to 4: how strong the grain of the paint's baked normal map is, 1 as baked, 0 flat) and markingSmoothness (0 to 1: the mean smoothness of the paint). The last of two lines of a key wins;
    /// # starts a comment. A file of its own, beside material-experiment.txt, so that the two readers do not meet. Pure.
    /// </summary>
    public sealed class MarkingExperiment : IEquatable<MarkingExperiment>
    {
        public static readonly MarkingExperiment None = new MarkingExperiment();

        /// <summary>The strongest grain the file takes (times the baked one): enough to see what the grain is.</summary>
        public const float MaxGrain = 4f;

        public bool? Normals;
        public float? Grain;
        public float? Smoothness;

        public bool IsEmpty => !Normals.HasValue && !Grain.HasValue && !Smoothness.HasValue;

        /// <summary>The smoothness of the plain HDRP/Lit paint (TsMaterials.Marking), the paint of a game without the baked detail: what markingSmoothness replaces and its removal puts back.</summary>
        public const float PlainSmoothness = 0.28f;

        /// <summary>Whether the paint's normals follow the ground: they do unless the file says off.</summary>
        public bool NormalsOn => Normals ?? true;

        /// <summary>How many times the baked grain the paint's normal map has: 1 unless the file says otherwise.</summary>
        public float GrainScale => Grain ?? 1f;

        /// <summary>Whether the paint's baked maps are to be replaced: the grain is not the baked one, or the smoothness is set.</summary>
        public bool ChangesMaps => GrainScale != 1f || Smoothness.HasValue;

        /// <summary>The smoothness of the plain paint: the file's, or its own.</summary>
        public float PlainSmooth => Smoothness ?? PlainSmoothness;

        /// <summary>Reads the text; a line that does not parse is left out and named in problems (the others still count).</summary>
        public static MarkingExperiment Parse(string text, List<string> problems)
        {
            var result = new MarkingExperiment();
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

        private static bool Number(string value, float low, float high, out float number)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !float.IsNaN(number) && number >= low && number <= high;
        }

        private static bool Assign(MarkingExperiment e, string key, string value, out string why)
        {
            why = null;
            switch (key.ToLowerInvariant())
            {
                case "markingnormals":
                    if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
                        e.Normals = true;
                    else if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
                        e.Normals = false;
                    else
                    {
                        why = "markingNormals '" + value + "' is not on or off";
                        return false;
                    }

                    return true;
                case "markinggrain":
                    if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        e.Grain = 0f;
                        return true;
                    }

                    if (!Number(value, 0f, MaxGrain, out float grain))
                    {
                        why = "markingGrain '" + value + "' is not off or a number from 0 to " + MaxGrain.ToString(CultureInfo.InvariantCulture);
                        return false;
                    }

                    e.Grain = grain;
                    return true;
                case "markingsmoothness":
                    if (!Number(value, 0f, 1f, out float smoothness))
                    {
                        why = "markingSmoothness '" + value + "' is not a number from 0 to 1";
                        return false;
                    }

                    e.Smoothness = smoothness;
                    return true;
                default:
                    why = "unknown key '" + key + "'";
                    return false;
            }
        }

        public bool Equals(MarkingExperiment other) => other != null && Normals == other.Normals && Grain == other.Grain && Smoothness == other.Smoothness;

        public override bool Equals(object obj) => Equals(obj as MarkingExperiment);

        public override int GetHashCode() => (Normals.HasValue ? (Normals.Value ? 1 : 2) : 0) ^ (Grain ?? -1f).GetHashCode() ^ ((Smoothness ?? -1f).GetHashCode() << 1);

        /// <summary>For the log: what the file says, or that it says nothing.</summary>
        public string Describe()
        {
            if (IsEmpty)
                return "nothing (the defaults)";
            var parts = new List<string>();
            if (Normals.HasValue)
                parts.Add("normals " + (Normals.Value ? "on" : "off"));
            if (Grain.HasValue)
                parts.Add("grain " + Grain.Value.ToString("0.##", CultureInfo.InvariantCulture));
            if (Smoothness.HasValue)
                parts.Add("smoothness " + Smoothness.Value.ToString("0.##", CultureInfo.InvariantCulture));
            return string.Join(", ", parts);
        }

        /// <summary>The commented example written beside the file, once.</summary>
        public static string Example()
        {
            var text = new StringBuilder();
            text.Append("# marking-experiment.example.txt: copy this file to marking-experiment.txt (same folder) and take the # off a line to try it; the game looks at the file once a second.\r\n");
            text.Append("# Deleting marking-experiment.txt puts every default back. The log (CDLC.TS.Mod.log) says what each change did: 'Materials: marking-experiment.txt changed'.\r\n");
            text.Append("# markingNormals = off   # on (the default): the paint's normals follow the ground it lies on; off: straight up as before 0.12.83. The tiles are built again.\r\n");
            text.Append("# markingGrain = 2       # how strong the grain of the paint's baked normal map is: off or 0 to 4, 1 as baked (the default). The paint of the game's own shader only.\r\n");
            text.Append("# markingSmoothness = 0.1  # the mean smoothness of the paint, 0 to 1. The default is the baked 0.20 (0.28 for the plain paint of a game without the baked detail).\r\n");
            return text.ToString();
        }
    }
}
