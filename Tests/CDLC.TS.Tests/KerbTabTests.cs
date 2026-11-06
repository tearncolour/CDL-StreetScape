using System;
using System.IO;
using System.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The mod's tab in the Roads menu (0.12.85): the kerb styles are its members in their own order, its icon is the CDL MOD badge, the marker is gone, the preference that puts the upgrades back.</summary>
    internal static class KerbTabTests
    {
        public static void RunAll()
        {
            Program.Run("kerb tab: the order of the kerb styles in the tab and in the template's group", Order);
            Program.Run("kerb tab: the tab's icon is a 128 px picture of its own, the right-hand button keeps the old one", Icon);
            Program.Run("kerb tab: the preference kerbTab is on unless the file says off, and is not written into a new file", Preference);
            Program.Run("kerb tab: the marker prefab is gone from the sources", NoMarker);
        }

        private static void Order()
        {
            int last = int.MinValue;
            foreach (KerbStyle style in Kerbs.Styles)
            {
                int inTab = KerbTab.Priority(style, 70, true);
                Program.Check(inTab > last, style + ": the styles follow each other in the tab, in the order of Kerbs.Styles");
                Program.Check(inTab == KerbTab.Priority(style, 5, true) && inTab == KerbTab.Priority(style, 9000, true), style + ": in the tab the template's priority has no say");
                Program.Check(inTab == (int)style, style + ": in the tab the priority is the style's own number");
                last = inTab;
            }

            foreach (KerbStyle style in Kerbs.Styles)
                Program.Check(KerbTab.Priority(style, 70, false) == 70 + (int)style && KerbTab.Priority(style, 0, false) == (int)style, style + ": in the template's group the priority is the template's and the style's, as in 0.12.84");
            Program.Check(KerbTab.Priority(KerbStyle.Raised, 70, true) != KerbTab.Priority(KerbStyle.Raised, 70, false), "the two places give different priorities");
        }

        private static void Icon()
        {
            string root = TextBudgetTests.Root();
            Program.Check(root != null, "the repository's root is found");
            if (root == null)
                return;
            Program.Check(KerbTab.IconFile == "cdl-logo.png", "the tab's icon is the logo's file");
            string path = Path.Combine(root, "Icons", KerbTab.IconFile);
            Program.Check(File.Exists(path), "the icon is in the Icons folder, which is packaged and served as the host cdlcts-icons");
            if (!File.Exists(path))
                return;
            byte[] bytes = File.ReadAllBytes(path);
            Program.Check(bytes.Length > 1000 && bytes.Length < 20000, "a small file: " + bytes.Length + " bytes");
            bool png = bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G' && Encoding.ASCII.GetString(bytes, 12, 4) == "IHDR";
            Program.Check(png, "a PNG");
            if (png)
            {
                int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19], height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                Program.Check(width == 128 && height == 128, "128 x 128 as the other tab icons: " + width + " x " + height);
                Program.Check(bytes[25] == 3, "a palette picture, as the other tab icons are packed (colour type " + bytes[25] + ")");
            }

            Program.Check(KerbTab.IconFile != "streetscape.png", "not the picture of the tree and the signpost that the tab had");
            string script = File.ReadAllText(Path.Combine(root, "UI", "src", "ts-ui.js"), Encoding.UTF8);
            Program.Check(script.Contains("button: \"coui://cdlcts-icons/streetscape.png\""), "the button in the right-hand column keeps its own icon: only the tab's logo was changed");
            Program.Check(File.Exists(Path.Combine(root, "Icons", "streetscape.png")), "and its file stays");
        }

        private static void Preference()
        {
            Program.Check(Preferences.KerbTabKey == "kerbTab", "the key is the one the owner types");
            Program.Check(Preferences.Parse("{}", out _).KerbTab == FeatureState.On, "no line: on");
            Program.Check(Preferences.Parse("{\"features\":{\"kerbTab\":\"off\"}}", out _).KerbTab == FeatureState.Off, "off is off");
            Program.Check(Preferences.Parse("{\"features\":{\"kerbTab\":\"on\"}}", out _).KerbTab == FeatureState.On, "on is on");
            foreach (string other in new[] { "log", "shadow", "banana", "OFF" })
                Program.Check(Preferences.Parse("{\"features\":{\"kerbTab\":\"" + other + "\"}}", out _).KerbTab == FeatureState.On, "'" + other + "' is no off: on");
            Program.Check(Preferences.Parse("{\"features\":5}", out _).KerbTab == FeatureState.On, "features that is not an object says nothing: on");
            Program.Check(!new Preferences().ToText().Contains("kerbTab"), "a file made with the defaults has no kerbTab line: a file made before keeps none and gets the default of the code");

            Preferences set = Preferences.Parse("{\"features\":{\"roadFollow\":\"log\"}}", out _);
            set.SetFeature(Preferences.KerbTabKey, FeatureState.Off);
            Preferences back = Preferences.Parse(set.ToText(), out _);
            Program.Check(back.KerbTab == FeatureState.Off && back.RoadFollow == FeatureState.Log, "the line survives writing and reading, beside the others");
        }

        private static void NoMarker()
        {
            string root = TextBudgetTests.Root();
            Program.Check(root != null, "the repository's root is found");
            if (root == null)
                return;
            int files = 0;
            foreach (string folder in new[] { "Systems", "Render", "Geo", "Tool" })
            {
                foreach (string file in Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
                {
                    files++;
                    string text = File.ReadAllText(file, Encoding.UTF8);
                    Program.Check(!text.Contains("TsMarkerPrefab") && !text.Contains("China Pack Marker"), Path.GetFileName(file) + ": no marker prefab of the tab");
                }
            }

            Program.Check(files > 100, "the sources were read: " + files + " files");
        }
    }
}
