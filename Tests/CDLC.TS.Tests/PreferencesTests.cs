using System;
using System.IO;
using System.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The preferences file (0.12.74): defaults, what is kept, what is a change, a damaged file, the atomic write.</summary>
    internal static class PreferencesTests
    {
        public static void RunAll()
        {
            Program.Run("preferences: the defaults when the file says nothing", Defaults);
            Program.Run("preferences: what the file says is read; a state this mod does not know is the step's default", Reads);
            Program.Run("preferences: unknown keys and their order are kept when the file is written again", KeepsUnknown);
            Program.Run("preferences: setting what the file holds is no change, setting another is, a save clears it", Changes);
            Program.Run("preferences: a damaged text is the defaults, protected, and never written over", Damaged);
            Program.Run("preferences: a missing file is made with the defaults and reads back the same", MissingIsMade);
            Program.Run("preferences: the write leaves no temporary file, replaces the old one and has no byte order mark", AtomicWrite);
            Program.Run("preferences: a file with a byte order mark and one that is not readable as a file", Bom);
            Program.Run("preferences: the text is the same after a round trip, with escapes", RoundTrip);
            Program.Run("preferences: the steps' states, and which of them do any work", States);
            Program.Run("preferences: the draw distance of the signs and markings (0.12.103) is read, brought into range, written and kept", ViewDistance);
        }

        private static string Folder()
        {
            string folder = Path.Combine(System.AppContext.BaseDirectory, "tmp", "cdlc-ts-prefs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void Defaults()
        {
            Preferences p = Preferences.Parse("{}", out string problem);
            Program.Check(problem == null && !p.Protected && !p.Changed, "an empty object is a good file with no change");
            Program.Check(p.Profile == "plain", "the profile is plain");
            Program.Check(p.RoadFollow == FeatureState.On, "road follow is on");
            Program.Check(p.AreaFitWorker == FeatureState.On, "the area fit worker is on");
            Program.Check(p.LoadWarm == FeatureState.On && Preferences.Parse("{\"features\":{\"loadWarm\":\"off\"}}", out _).LoadWarm == FeatureState.Off && Preferences.Parse("{\"features\":{\"loadWarm\":\"x\"}}", out _).LoadWarm == FeatureState.On, "the load warm-up is on, off when the file says off, on for anything else");
            Program.Check(p.PlaceShortcuts == FeatureState.On, "the placement shortcuts are held back by default");
            Program.Check(Preferences.Parse("{\"features\":{\"placeShortcuts\":\"off\"}}", out _).PlaceShortcuts == FeatureState.Off && Preferences.Parse("{\"features\":{\"placeShortcuts\":\"x\"}}", out _).PlaceShortcuts == FeatureState.On, "off turns it off, anything that is not a state is on");
            Program.Check(p.TextBox == FeatureState.On && Preferences.Parse("{\"features\":{\"textBox\":\"off\"}}", out _).TextBox == FeatureState.Off && Preferences.Parse("{\"features\":{\"textBox\":\"x\"}}", out _).TextBox == FeatureState.On, "the text box is on unless the file says off, and anything that is not a state is on");
            Program.Check(p.PictureCut == FeatureState.Shadow, "the picture cut is shadow");
            foreach (FeatureState state in new[] { FeatureState.Off, FeatureState.Shadow, FeatureState.Log, FeatureState.On })
                Program.Check(Preferences.Parse("{\"features\":{\"pictureCut\":\"" + Preferences.StateName(state) + "\"}}", out _).PictureCut == state, "the picture cut is read: " + state);
            Program.Check(Preferences.Parse("{\"features\":{\"pictureCut\":\"maybe\"}}", out _).PictureCut == FeatureState.Shadow && Preferences.Parse("{\"features\":{\"pictureCut\":7}}", out _).PictureCut == FeatureState.Shadow, "a state that is not one is the default");
            Program.Check(p.AdaptiveSampling == FeatureState.Shadow, "the adaptive sampler is in the shadow");
            foreach ((string text, FeatureState expected) in new[] { ("off", FeatureState.Off), ("shadow", FeatureState.Shadow), ("on", FeatureState.On), ("log", FeatureState.Shadow), ("x", FeatureState.Shadow), ("ON", FeatureState.Shadow) })
                Program.Check(Preferences.Parse("{\"features\":{\"adaptiveSampling\":\"" + text + "\"}}", out _).AdaptiveSampling == expected, "adaptiveSampling " + text + " is " + expected);
            Program.Check(Preferences.Parse("{\"features\":{\"areaFitWorker\":\"off\"}}", out _).AreaFitWorker == FeatureState.Off && Preferences.Parse("{\"features\":{\"areaFitWorker\":\"shadow\"}}", out _).AreaFitWorker == FeatureState.Shadow && Preferences.Parse("{\"features\":{\"areaFitWorker\":\"x\"}}", out _).AreaFitWorker == FeatureState.On, "the worker's state is read: off, shadow, and on for anything else");
            Program.Check(p.Feature("adaptiveSampling", FeatureState.Shadow) == FeatureState.Shadow, "a step the file does not mention is the fallback the caller gives");
            Program.Check(p.StandBox == FeatureState.On, "the standing light box is on");
            Program.Check(Preferences.Parse("{\"features\":{\"standBox\":\"off\"}}", out _).StandBox == FeatureState.Off && Preferences.Parse("{\"features\":{\"standBox\":\"x\"}}", out _).StandBox == FeatureState.On, "off turns it off, anything that is not a state is on");
            var fresh = new Preferences();
            Program.Check(fresh.ToText() == "{\n  \"profile\": \"plain\",\n  \"features\": {\n    \"roadFollow\": \"on\",\n    \"areaFitWorker\": \"on\",\n    \"placeShortcuts\": \"on\",\n    \"pictureCut\": \"shadow\",\n    \"adaptiveSampling\": \"shadow\",\n    \"textBox\": \"on\",\n    \"styleProfiles\": \"on\",\n    \"standBox\": \"on\",\n    \"systemFonts\": \"on\",\n    \"textSupports\": \"on\",\n    \"loadWarm\": \"on\"\n  }\n}\n", "the defaults' text is the file a player finds");
        }

        private static void ViewDistance()
        {
            Preferences none = Preferences.Parse("{}", out _);
            Program.Check(none.ViewDistance == 700f && !none.Changed, "a file that says nothing: 700 m, no change");
            Program.Check(new Preferences().ViewDistance == 700f && !new Preferences().ToText().Contains("viewDistance"), "a new file has the default and does not write it");
            Program.Check(Preferences.Parse("{\"viewDistance\":1500}", out _).ViewDistance == 1500f, "a number in the file is read");
            Program.Check(Preferences.Parse("{\"viewDistance\":1234.5}", out _).ViewDistance == 1234.5f, "any number in the range is read as it is (the panel's steps are its own)");
            Program.Check(Preferences.Parse("{\"viewDistance\":50}", out _).ViewDistance == 300f && Preferences.Parse("{\"viewDistance\":99999}", out _).ViewDistance == 3000f && Preferences.Parse("{\"viewDistance\":-4}", out _).ViewDistance == 300f, "outside the range: brought to it");
            foreach (string odd in new[] { "\"far\"", "true", "null", "[1500]", "{\"m\":1500}", "\"1500\"" })
                Program.Check(Preferences.Parse("{\"viewDistance\":" + odd + "}", out _).ViewDistance == 700f, "not a number (" + odd + "): the default");
            Program.Check(Preferences.Parse("{\"features\":{\"viewDistance\":1500}}", out _).ViewDistance == 700f, "the key belongs at the top, as the profile does; in the steps it says nothing");

            // Setting it: a change only when it is another number, in range, kept with everything else in the file.
            Preferences p = Preferences.Parse("{\"zeta\":[1],\"profile\":\"shanghai\",\"viewDistance\":1000,\"features\":{\"roadFollow\":\"log\"}}", out _);
            p.ViewDistance = 1000f;
            Program.Check(!p.Changed, "setting what the file holds is no change");
            p.ViewDistance = 2000f;
            Program.Check(p.Changed && p.ViewDistance == 2000f, "setting another is a change");
            string text = p.ToText();
            Preferences back = Preferences.Parse(text, out _);
            Program.Check(back.ViewDistance == 2000f && back.Profile == "shanghai" && back.RoadFollow == FeatureState.Log && text.Contains("\"zeta\""), "read back with the profile, the states and the unknown key");
            Program.Check(text.IndexOf("\"profile\"", StringComparison.Ordinal) < text.IndexOf("\"viewDistance\"", StringComparison.Ordinal) && text.IndexOf("\"viewDistance\"", StringComparison.Ordinal) < text.IndexOf("\"features\"", StringComparison.Ordinal), "the member stays where it was in the file");
            Program.Check(CountOf(text, "\"viewDistance\"") == 1, "and is written once");
            p.ViewDistance = 99999f;
            Program.Check(p.ViewDistance == 3000f && p.ToText().Contains("\"viewDistance\": 3000") && !p.ToText().Contains("99999"), "a distance out of range is written as the most");
            p.ViewDistance = float.NaN;
            Program.Check(p.ViewDistance == 700f && p.ToText().Contains("\"viewDistance\": 700"), "not a number is written as the default");

            // A file with none gets the member on the first set, at the end; a damaged file is never written.
            Preferences fresh = new Preferences();
            fresh.ViewDistance = 1500f;
            Program.Check(fresh.Changed && fresh.ViewDistance == 1500f && Preferences.Parse(fresh.ToText(), out _).ViewDistance == 1500f, "a new file takes it");
            Preferences broken = Preferences.Parse("{not json", out _);
            Program.Check(broken.Protected && !broken.Save(Path.Combine(Folder(), "x.json")), "a damaged file is not written over");
            string folder = Folder();
            try
            {
                string path = Path.Combine(folder, Preferences.FileName);
                Program.Check(fresh.Save(path) && Preferences.Load(path, out _, out _).ViewDistance == 1500f, "saved and read from the file");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static int CountOf(string text, string part)
        {
            int count = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
                count++;
            return count;
        }

        private static void Reads()
        {
            Preferences p = Preferences.Parse("{\"profile\":\"shanghai\",\"features\":{\"roadFollow\":\"off\",\"adaptiveSampling\":\"shadow\"}}", out _);
            Program.Check(p.Profile == "shanghai" && p.RoadFollow == FeatureState.Off && p.Feature("adaptiveSampling", FeatureState.Off) == FeatureState.Shadow, "the profile and two states are read");
            Preferences odd = Preferences.Parse("{\"profile\":3,\"features\":{\"roadFollow\":\"banana\"}}", out _);
            Program.Check(odd.Profile == "plain" && odd.RoadFollow == FeatureState.On, "a profile that is not text and a state that is not one are the defaults");
            Preferences wrong = Preferences.Parse("{\"features\":5}", out _);
            Program.Check(wrong.RoadFollow == FeatureState.On, "features that is not an object says nothing");
            Preferences loud = Preferences.Parse("{\"features\":{\"roadFollow\":\"OFF\"}}", out _);
            Program.Check(loud.RoadFollow == FeatureState.On, "the names are lower case: OFF is not one");
            Program.Check(Preferences.Parse("{\"features\":{\"roadFollow\":\"log\"}}", out _).RoadFollow == FeatureState.Log, "a file that says log keeps it");
        }

        private static void KeepsUnknown()
        {
            Preferences p = Preferences.Parse("{\"zeta\":[1,2,{\"a\":null}],\"profile\":\"plain\",\"features\":{\"future\":\"maybe\",\"roadFollow\":\"log\",\"n\":7},\"omega\":true}", out _);
            p.SetFeature(Preferences.RoadFollowKey, FeatureState.Off);
            p.Profile = "hangzhou";
            string text = p.ToText();
            Program.Check(text.IndexOf("\"zeta\"", StringComparison.Ordinal) < text.IndexOf("\"profile\"", StringComparison.Ordinal) && text.IndexOf("\"profile\"", StringComparison.Ordinal) < text.IndexOf("\"features\"", StringComparison.Ordinal)
                && text.IndexOf("\"features\"", StringComparison.Ordinal) < text.IndexOf("\"omega\"", StringComparison.Ordinal), "the members are in the order they were read");
            Program.Check(text.Contains("\"future\": \"maybe\"") && text.Contains("\"n\": 7") && text.Contains("\"omega\": true") && text.Contains("\"a\": null"), "unknown keys and their values are kept");
            Preferences back = Preferences.Parse(text, out _);
            Program.Check(back.Profile == "hangzhou" && back.RoadFollow == FeatureState.Off && back.Feature("future", FeatureState.On) == FeatureState.On, "what was set is read back; an unknown state is still the fallback");
            Preferences added = Preferences.Parse("{\"features\":5}", out _);
            added.SetFeature("adaptiveSampling", FeatureState.Shadow);
            Program.Check(added.Feature("adaptiveSampling", FeatureState.Off) == FeatureState.Shadow && Preferences.Parse(added.ToText(), out _).Feature("adaptiveSampling", FeatureState.Off) == FeatureState.Shadow, "features that was not an object is made one when a step is set");
        }

        private static void Changes()
        {
            Preferences p = Preferences.Parse("{\"profile\":\"plain\",\"features\":{\"roadFollow\":\"log\"}}", out _);
            p.SetFeature(Preferences.RoadFollowKey, FeatureState.Log);
            p.Profile = "plain";
            Program.Check(!p.Changed, "setting what the file holds is no change");
            p.SetFeature(Preferences.RoadFollowKey, FeatureState.Off);
            Program.Check(p.Changed && p.RoadFollow == FeatureState.Off, "setting another state is a change");
            string folder = Folder();
            try
            {
                string path = Path.Combine(folder, Preferences.FileName);
                Program.Check(p.Save(path) && !p.Changed, "a save writes and clears the change");
                Program.Check(Preferences.Load(path, out PreferencesRead how, out _).RoadFollow == FeatureState.Off && how == PreferencesRead.Read, "and the file has it");
                Preferences q = Preferences.Parse("{}", out _);
                q.Profile = "plain";
                Program.Check(q.Changed, "a profile the file did not say, set to the default, is written: the file then says what is used");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void Damaged()
        {
            foreach (string text in new[] { "", "   ", "not json", "[1,2]", "{\"profile\":", "\"text\"", "null" })
            {
                Preferences p = Preferences.Parse(text, out string problem);
                Program.Check(p.Protected && problem != null && p.Profile == "plain" && p.RoadFollow == FeatureState.On, "'" + text + "' is the defaults and protected");
            }

            string folder = Folder();
            try
            {
                string path = Path.Combine(folder, Preferences.FileName);
                File.WriteAllText(path, "{ broken");
                Preferences p = Preferences.Load(path, out PreferencesRead how, out string problem);
                Program.Check(how == PreferencesRead.Damaged && problem != null && p.Protected, "a damaged file is reported as damaged");
                p.SetFeature(Preferences.RoadFollowKey, FeatureState.Off);
                Program.Check(!p.Save(path) && File.ReadAllText(path) == "{ broken" && !File.Exists(path + ".tmp"), "and is not written over");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void MissingIsMade()
        {
            string folder = Folder();
            try
            {
                string path = Path.Combine(folder, "ModsData", "CDLC.TS", Preferences.FileName);
                Preferences p = Preferences.Load(path, out PreferencesRead how, out string problem);
                Program.Check(how == PreferencesRead.Missing && problem == null && !p.Protected, "no file: missing, not damaged");
                Program.Check(p.Save(path) && File.Exists(path), "saving makes the folders and the file");
                Preferences back = Preferences.Load(path, out how, out _);
                Program.Check(how == PreferencesRead.Read && back.Profile == "plain" && back.RoadFollow == FeatureState.On && back.ToText() == p.ToText(), "and it reads back as the defaults");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void AtomicWrite()
        {
            string folder = Folder();
            try
            {
                string path = Path.Combine(folder, Preferences.FileName);
                Preferences p = new Preferences();
                p.Save(path);
                p.SetFeature(Preferences.RoadFollowKey, FeatureState.Off);
                p.Save(path);
                Program.Check(!File.Exists(path + ".tmp"), "no temporary file is left");
                byte[] bytes = File.ReadAllBytes(path);
                Program.Check(bytes.Length > 3 && !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "no byte order mark");
                Program.Check(Preferences.Load(path, out _, out _).RoadFollow == FeatureState.Off, "the second write is in place of the first");
                File.WriteAllText(path + ".tmp", "left by a crash");
                p.SetFeature(Preferences.RoadFollowKey, FeatureState.On);
                Program.Check(p.Save(path) && !File.Exists(path + ".tmp") && Preferences.Load(path, out _, out _).RoadFollow == FeatureState.On, "a temporary file a crash left does not stop the next write");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void Bom()
        {
            string folder = Folder();
            try
            {
                string path = Path.Combine(folder, Preferences.FileName);
                File.WriteAllText(path, "{\"profile\":\"beijing\"}", new UTF8Encoding(true));
                Program.Check(Preferences.Load(path, out PreferencesRead how, out _).Profile == "beijing" && how == PreferencesRead.Read, "an editor that writes a byte order mark does not damage the file");
                Preferences directory = Preferences.Load(folder, out how, out _);
                Program.Check(how == PreferencesRead.Missing || how == PreferencesRead.Damaged, "a folder where the file should be is not read as preferences");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static void RoundTrip()
        {
            var p = new Preferences();
            p.Profile = "a \"quoted\" \\ name\n上海";
            string text = p.ToText();
            Preferences back = Preferences.Parse(text, out string problem);
            Program.Check(problem == null && back.Profile == p.Profile && back.ToText() == text, "escapes and Chinese survive, and the text is stable");
        }

        private static void States()
        {
            foreach (FeatureState state in (FeatureState[])Enum.GetValues(typeof(FeatureState)))
            {
                Program.Check(Preferences.TryState(Preferences.StateName(state), out FeatureState back) && back == state, "the name of " + state + " reads back");
                Program.Check(FollowGate.Enabled(state) == (state != FeatureState.Off), state + ": only off does no work");
            }

            Program.Check(!Preferences.TryState("", out _) && !Preferences.TryState("Log", out _) && !Preferences.TryState("true", out _), "other texts are no state");
        }
    }
}
