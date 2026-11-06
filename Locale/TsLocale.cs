using System;
using System.Collections.Generic;
using System.IO;
using Colossal.Localization;
using Game.SceneFlow;
using Newtonsoft.Json;

namespace CDLC.TS
{
    /// <summary>
    /// All texts are "CDLC.TS.*" keys (plus the options page keys) in lang/&lt;locale&gt;.json, registered with the game's
    /// localization so language packs can translate or override them. en-US and zh-HANS are embedded; lang/*.json next to
    /// the dll is loaded on top.
    /// </summary>
    internal static class TsLocale
    {
        internal const string kPrefix = "CDLC.TS.";
        private const string kResourcePrefix = "CDLC.TS.lang.";
        private static readonly List<(string locale, MemorySource source)> s_Registered = new List<(string, MemorySource)>();
        private static Dictionary<string, string> s_English = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static void Register(string modDirectory)
        {
            var locales = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            System.Reflection.Assembly assembly = typeof(TsLocale).Assembly;
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(kResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.Ordinal))
                    continue;

                string locale = resource.Substring(kResourcePrefix.Length, resource.Length - kResourcePrefix.Length - ".json".Length);
                using (Stream stream = assembly.GetManifestResourceStream(resource))
                using (var reader = new StreamReader(stream))
                    Merge(locales, locale, reader.ReadToEnd());
            }

            if (!string.IsNullOrEmpty(modDirectory))
            {
                string folder = Path.Combine(modDirectory, "lang");
                if (Directory.Exists(folder))
                {
                    foreach (string file in Directory.GetFiles(folder, "*.json"))
                    {
                        try
                        {
                            Merge(locales, Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
                        }
                        catch (Exception ex)
                        {
                            Mod.log.Warn($"Language file {file} skipped: {ex.Message}");
                        }
                    }
                }
            }

            if (locales.TryGetValue("en-US", out Dictionary<string, string> english))
                s_English = english;

            LocalizationManager manager = GameManager.instance?.localizationManager;
            if (manager == null)
                return;

            foreach (KeyValuePair<string, Dictionary<string, string>> locale in locales)
            {
                var source = new MemorySource(locale.Value);
                manager.AddSource(locale.Key, source);
                s_Registered.Add((locale.Key, source));
            }

            Mod.log.Info($"Registered StreetScape texts for {locales.Count} locale(s).");
        }

        private static void Merge(Dictionary<string, Dictionary<string, string>> locales, string locale, string json)
        {
            Dictionary<string, string> entries = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (entries == null)
                return;

            if (!locales.TryGetValue(locale, out Dictionary<string, string> target))
            {
                target = new Dictionary<string, string>(StringComparer.Ordinal);
                locales[locale] = target;
            }

            foreach (KeyValuePair<string, string> entry in entries)
                target[entry.Key] = entry.Value;
        }

        internal static void Unregister()
        {
            LocalizationManager manager = GameManager.instance?.localizationManager;
            if (manager != null)
            {
                foreach ((string locale, MemorySource source) in s_Registered)
                    manager.RemoveSource(locale, source);
            }

            s_Registered.Clear();
        }

        internal static void ReportMissing(IEnumerable<string> keys)
        {
            var missing = new List<string>();
            foreach (string key in keys)
            {
                if (!s_English.ContainsKey(key))
                    missing.Add(key);
            }

            if (missing.Count > 0)
                Mod.log.Warn($"Missing English texts: {string.Join(", ", missing)}");
        }
    }
}
