using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The game's shader for this mod's own materials (Geo/GameShader.cs): which families move at which mode, which mode is wanted, what the template must have, what a plain material becomes, and what a
    /// change of mode does to the materials that exist. How the game's shader looks, and whether its decals leave the posts alone, is for the game; the swap of a real Material is in Render/ and is not here.
    /// </summary>
    internal static class GameShaderTests
    {
        public static void RunAll()
        {
            Program.Run("game shader: which families move at which mode", Routing);
            Program.Run("game shader: the mode that is wanted (experiment, preferences, default)", Policy);
            Program.Run("game shader: what the template must have, and the mode it allows", Needs);
            Program.Run("game shader: the maps of a plain material", Look);
            Program.Run("game shader: the preferences' gameShader line", Preference);
            Program.Run("game shader: the plan of a change of mode", Swap);
            Program.Run("game shader: the experiment file's gameShader line", Experiment);
        }

        private static void Routing()
        {
            // The table in full: [family][mode Off, Plain, Faces, Lit] for a face that is not lit, and for one that is lit.
            var expected = new Dictionary<string, bool[]>
            {
                ["solid"] = new[] { false, true, true, true },
                ["metal"] = new[] { false, true, true, true },
                ["concrete"] = new[] { false, true, true, true },
                ["marking"] = new[] { false, true, true, true },
                ["face"] = new[] { false, false, true, true },
                ["glow"] = new[] { false, false, false, true },
                ["led"] = new[] { false, false, false, false },
                ["detail"] = new[] { false, false, false, false },
                ["glass"] = new[] { false, false, false, false },
                ["other"] = new[] { false, false, false, false },
                [""] = new[] { false, false, false, false }
            };
            foreach (KeyValuePair<string, bool[]> pair in expected)
            {
                for (int mode = 0; mode < 4; mode++)
                    Program.Check(GameShaderRoute.Moves((GameShaderMode)mode, pair.Key, false) == pair.Value[mode], $"'{pair.Key}' at mode {mode}: {pair.Value[mode]}");
            }

            bool[] litFace = { false, false, false, true };
            for (int mode = 0; mode < 4; mode++)
                Program.Check(GameShaderRoute.Moves((GameShaderMode)mode, "face", true) == litFace[mode], "a face lit from inside moves only at lit, not at mode " + mode);
            Program.Check(!GameShaderRoute.Moves(GameShaderMode.Lit, null, false), "no family stays");
            foreach (string family in new[] { "solid", "metal", "concrete", "marking" })
                Program.Check(GameShaderRoute.Moves(GameShaderMode.Plain, family, true) == GameShaderRoute.Moves(GameShaderMode.Plain, family, false), family + " does not depend on lit");

            // The names MakeLit is called with (TsMaterials.cs) and the family each is routed as: a name that came out as another word would stay on HDRP/Lit without a sound.
            var names = new Dictionary<string, string>
            {
                ["CDLTS solid PoleLightGrey"] = "solid", ["CDLTS solid Steel"] = "solid", ["CDLTS metal"] = "metal", ["CDLTS concrete"] = "concrete", ["CDLTS marking yellow"] = "marking",
                ["CDLTS face template"] = "face", ["CDLTS led 3"] = "led", ["CDLTS glow GlowRed"] = "glow", ["CDLTS detail RoadPaint White"] = "detail", ["CDLTS glass Glass"] = "glass",
                ["Default"] = "Default", [""] = ""
            };
            foreach (KeyValuePair<string, string> pair in names)
                Program.Check(GameShaderRoute.FamilyOf(pair.Key) == pair.Value, $"'{pair.Key}' is '{pair.Value}': '{GameShaderRoute.FamilyOf(pair.Key)}'");
            Program.Check(GameShaderRoute.FamilyOf(null) == "other", "no name: other");
            Program.Check(GameShaderRoute.Moves(GameShaderMode.Plain, GameShaderRoute.FamilyOf("CDLTS solid PoleLightGrey"), false) && !GameShaderRoute.Moves(GameShaderMode.Lit, GameShaderRoute.FamilyOf("CDLTS detail RoadPaint White"), false), "a pole's material moves at plain, a detail's never");

            Program.Check(GameShaderRoute.Name(GameShaderMode.Faces) == "faces" && GameShaderRoute.Name(GameShaderMode.Off) == "off", "names are lower case");
            foreach (GameShaderMode mode in new[] { GameShaderMode.Off, GameShaderMode.Plain, GameShaderMode.Faces, GameShaderMode.Lit })
            {
                Program.Check(GameShaderRoute.TryParse(GameShaderRoute.Name(mode), out GameShaderMode back) && back == mode, "a name reads back: " + mode);
                Program.Check(GameShaderRoute.TryParse(GameShaderRoute.Name(mode).ToUpperInvariant(), out back) && back == mode, "any case: " + mode);
            }

            Program.Check(!GameShaderRoute.TryParse("on", out _) && !GameShaderRoute.TryParse("all", out _) && !GameShaderRoute.TryParse("", out _) && !GameShaderRoute.TryParse(null, out _) && !GameShaderRoute.TryParse("1", out _), "only the four names");
        }

        private static void Policy()
        {
            Program.Check(GameShaderPolicy.DefaultMode == GameShaderMode.Lit, "a file with no line gets everything on the game's shader, what glows too (0.12.90: the text light box was painted over by a building's decal on HDRP/Lit)");
            Program.Check(GameShaderPolicy.OnMode == GameShaderPolicy.DefaultMode, "on in the preferences is the default");
            Program.Check(GameShaderPolicy.Resolve(null, null) == GameShaderMode.Lit, "nothing says: the default");
            Program.Check(GameShaderPolicy.Resolve(FeatureState.On, null) == GameShaderMode.Lit, "preferences on: lit");
            Program.Check(GameShaderPolicy.Resolve(FeatureState.Off, null) == GameShaderMode.Off && GameShaderPolicy.Resolve(FeatureState.Off, null) != GameShaderPolicy.DefaultMode, "preferences off: off, which is not the default");
            Program.Check(GameShaderPolicy.Resolve(FeatureState.Shadow, null) == GameShaderPolicy.DefaultMode && GameShaderPolicy.Resolve(FeatureState.Log, null) == GameShaderPolicy.DefaultMode, "shadow and log read as no line");
            foreach (GameShaderMode experiment in new[] { GameShaderMode.Off, GameShaderMode.Plain, GameShaderMode.Faces, GameShaderMode.Lit })
            {
                foreach (FeatureState? pref in new FeatureState?[] { null, FeatureState.On, FeatureState.Off, FeatureState.Log })
                    Program.Check(GameShaderPolicy.Resolve(pref, experiment) == experiment, "the experiment file wins: " + experiment + " over " + (pref?.ToString() ?? "no line"));
            }
        }

        private static void Preference()
        {
            // The line is never written by default: a file made before keeps no line, so the default of the code has to be the one that is right for it.
            Preferences none = Preferences.Parse("{}", out _);
            Program.Check(none.GameShaderLine == null && !none.ToText().Contains("gameShader") && !new Preferences().ToText().Contains("gameShader"), "no gameShader line by default, and none written");
            Program.Check(Preferences.Parse("{\"features\":{\"gameShader\":\"on\"}}", out _).GameShaderLine == FeatureState.On && Preferences.Parse("{\"features\":{\"gameShader\":\"off\"}}", out _).GameShaderLine == FeatureState.Off, "on and off are read");
            foreach (string word in new[] { "shadow", "log", "banana", "ON", "" })
                Program.Check(Preferences.Parse("{\"features\":{\"gameShader\":\"" + word + "\"}}", out _).GameShaderLine == null, "'" + word + "' reads as no line");
            Program.Check(Preferences.Parse("{\"features\":{\"gameShader\":3}}", out _).GameShaderLine == null && Preferences.Parse("{\"features\":5}", out _).GameShaderLine == null, "a number and a features that is no object read as no line");
            Preferences set = Preferences.Parse("{\"features\":{\"roadFollow\":\"off\"}}", out _);
            set.SetFeature(Preferences.GameShaderKey, FeatureState.On);
            Preferences back = Preferences.Parse(set.ToText(), out _);
            Program.Check(back.GameShaderLine == FeatureState.On && back.RoadFollow == FeatureState.Off, "a gameShader line survives writing and reading, beside the others");
            Program.Check(Preferences.GameShaderKey == "gameShader", "the key is the one the owner types");
            Program.Check(GameShaderPolicy.Resolve(back.GameShaderLine, null) == GameShaderMode.Lit && GameShaderPolicy.Resolve(none.GameShaderLine, null) == GameShaderMode.Lit && GameShaderPolicy.Resolve(Preferences.Parse("{\"features\":{\"gameShader\":\"off\"}}", out _).GameShaderLine, null) == GameShaderMode.Off, "the line gives the mode, and off turns it off");
        }

        private static void Needs()
        {
            var all = new HashSet<string>(GameShaderNeeds.Required);
            foreach (string name in GameShaderNeeds.Emission)
                all.Add(name);
            Program.Check(GameShaderNeeds.Missing(GameShaderMode.Lit, all.Contains).Count == 0, "a template with everything misses nothing");
            Program.Check(GameShaderNeeds.Required.Length == 5 && System.Array.IndexOf(GameShaderNeeds.Required, "colossal_DecalLayerMask") >= 0, "the decal layer is required: without it moving would change no decal");
            Program.Check(System.Array.IndexOf(GameShaderNeeds.Required, "_MaskMap") >= 0 && System.Array.IndexOf(GameShaderNeeds.Required, "_NormalMap") >= 0 && System.Array.IndexOf(GameShaderNeeds.Required, "_BaseColorMap") >= 0 && System.Array.IndexOf(GameShaderNeeds.Required, "_BaseColor") >= 0, "the detail recipe's four");
            Program.Check(GameShaderNeeds.Emission.Length == 2 && System.Array.IndexOf(GameShaderNeeds.Emission, "_EmissiveColor") >= 0 && System.Array.IndexOf(GameShaderNeeds.Emission, "_EmissiveColorMap") >= 0, "the emission pair");

            Program.Check(GameShaderNeeds.Missing(GameShaderMode.Off, name => false).Count == 0, "off needs nothing");
            Program.Check(GameShaderNeeds.Missing(GameShaderMode.Plain, name => false).Count == 5, "plain names every required property it lacks");
            Program.Check(GameShaderNeeds.Missing(GameShaderMode.Faces, name => false).Count == 5 && GameShaderNeeds.Missing(GameShaderMode.Lit, name => false).Count == 7, "lit adds the emission pair");
            var noEmission = new HashSet<string>(GameShaderNeeds.Required);
            Program.Check(GameShaderNeeds.Missing(GameShaderMode.Faces, noEmission.Contains).Count == 0, "faces needs no emission");
            List<string> lack = GameShaderNeeds.Missing(GameShaderMode.Lit, noEmission.Contains);
            Program.Check(lack.Count == 2 && lack[0] == "_EmissiveColor" && lack[1] == "_EmissiveColorMap", "lit without emission names both: " + string.Join(",", lack));
            var noLayer = new HashSet<string>(all);
            noLayer.Remove("colossal_DecalLayerMask");
            List<string> layer = GameShaderNeeds.Missing(GameShaderMode.Plain, noLayer.Contains);
            Program.Check(layer.Count == 1 && layer[0] == "colossal_DecalLayerMask", "the missing layer is named");

            Program.Check(GameShaderNeeds.Allowed(GameShaderMode.Lit, all.Contains) == GameShaderMode.Lit && GameShaderNeeds.Allowed(GameShaderMode.Faces, all.Contains) == GameShaderMode.Faces && GameShaderNeeds.Allowed(GameShaderMode.Plain, all.Contains) == GameShaderMode.Plain, "a full template allows what is wanted");
            Program.Check(GameShaderNeeds.Allowed(GameShaderMode.Off, all.Contains) == GameShaderMode.Off, "off stays off");
            Program.Check(GameShaderNeeds.Allowed(GameShaderMode.Lit, noEmission.Contains) == GameShaderMode.Faces, "no emission: lit is capped at faces");
            Program.Check(GameShaderNeeds.Allowed(GameShaderMode.Plain, noEmission.Contains) == GameShaderMode.Plain && GameShaderNeeds.Allowed(GameShaderMode.Faces, noEmission.Contains) == GameShaderMode.Faces, "no emission does not touch plain or faces");
            foreach (GameShaderMode wanted in new[] { GameShaderMode.Plain, GameShaderMode.Faces, GameShaderMode.Lit })
            {
                Program.Check(GameShaderNeeds.Allowed(wanted, noLayer.Contains) == GameShaderMode.Off, "no decal layer: off, whatever is wanted: " + wanted);
                Program.Check(GameShaderNeeds.Allowed(wanted, name => false) == GameShaderMode.Off, "no template: off: " + wanted);
            }
        }

        private static void Look()
        {
            GameShaderLook.MaskBytes(0.05f, 0.42f, out byte metal, out byte smooth);
            Program.Check(metal == 13 && smooth == 107, $"the support's powder coat 0.05 / 0.42 is {metal} / {smooth}");
            GameShaderLook.MaskBytes(0.3f, 0.45f, out metal, out smooth);
            Program.Check(metal == 76 && smooth == 115, $"the steel 0.3 / 0.45 is {metal} / {smooth} (76.5 goes to the even 76)");
            GameShaderLook.MaskBytes(0f, 0.55f, out metal, out smooth);
            Program.Check(metal == 0 && smooth == 140, $"a face 0 / 0.55 is {metal} / {smooth}");
            GameShaderLook.MaskBytes(1f, 1f, out metal, out smooth);
            Program.Check(metal == 255 && smooth == 255, "full");
            GameShaderLook.MaskBytes(-0.5f, 7f, out metal, out smooth);
            Program.Check(metal == 0 && smooth == 255, "clamped");
            Program.Check(GameShaderLook.ToByte(0.5f) == 128 && GameShaderLook.ToByte(1.5f / 255f) == 2 && GameShaderLook.ToByte(2.5f / 255f) == 2, "ties go to the even byte");

            GameShaderLook.ColourBytes(0.4397f, 0.4508f, 0.5210f, out byte r, out byte g, out byte b);
            Program.Check(r == 112 && g == 115 && b == 133, $"the light grey of a pole is {r},{g},{b}: the same numbers HDRP/Lit was given, not their gamma form");
            GameShaderLook.ColourBytes(0f, 1f, 2f, out r, out g, out b);
            Program.Check(r == 0 && g == 255 && b == 255, "colour bytes are clamped");
        }

        private static GameShaderEntry E(string family, bool lit, bool onGame) => new GameShaderEntry { Family = family, Lit = lit, OnGame = onGame };

        private static void Swap()
        {
            var entries = new List<GameShaderEntry>
            {
                E("solid", false, false), E("metal", false, false), E("face", false, false), E("face", true, false), E("glow", false, false),
                E("led", false, false), E("detail", false, false), E("glass", false, false), E("concrete", false, false), E("marking", false, false)
            };
            var toGame = new List<int>();
            var toHdrp = new List<int>();
            GameShaderSwap.Plan(GameShaderMode.Off, entries, toGame, toHdrp);
            Program.Check(toGame.Count == 0 && toHdrp.Count == 0, "off with everything on HDRP/Lit: nothing");

            GameShaderSwap.Plan(GameShaderMode.Plain, entries, toGame, toHdrp);
            Program.Check(string.Join(",", toGame) == "0,1,8,9" && toHdrp.Count == 0, "plain: solid, metal, concrete, marking: " + string.Join(",", toGame));
            toGame.Clear();
            GameShaderSwap.Plan(GameShaderMode.Faces, entries, toGame, toHdrp);
            Program.Check(string.Join(",", toGame) == "0,1,2,8,9", "faces adds the face that is not lit: " + string.Join(",", toGame));
            toGame.Clear();
            GameShaderSwap.Plan(GameShaderMode.Lit, entries, toGame, toHdrp);
            Program.Check(string.Join(",", toGame) == "0,1,2,3,4,8,9" && toHdrp.Count == 0, "lit adds the lit face and the glow, and never the LED dots (0.12.92), the detail or the glass: " + string.Join(",", toGame));

            // After the plan the materials are on the game's shader: a second plan is empty, and going down puts back exactly the ones above the new mode.
            var moved = new List<GameShaderEntry>();
            for (int i = 0; i < entries.Count; i++)
                moved.Add(E(entries[i].Family, entries[i].Lit, toGame.Contains(i)));
            toGame.Clear();
            GameShaderSwap.Plan(GameShaderMode.Lit, moved, toGame, toHdrp);
            Program.Check(toGame.Count == 0 && toHdrp.Count == 0, "the same mode again changes nothing");
            GameShaderSwap.Plan(GameShaderMode.Faces, moved, toGame, toHdrp);
            Program.Check(toGame.Count == 0 && string.Join(",", toHdrp) == "3,4", "from lit down to faces: the lit face and the glow go back: " + string.Join(",", toHdrp));
            toHdrp.Clear();
            GameShaderSwap.Plan(GameShaderMode.Off, moved, toGame, toHdrp);
            Program.Check(toGame.Count == 0 && string.Join(",", toHdrp) == "0,1,2,3,4,8,9", "off puts back everything that was moved and nothing else: " + string.Join(",", toHdrp));

            // A material that was swapped already is left alone when it is wanted, and the detail that is not ours to move is never planned in either direction, even marked as on the game's shader.
            var odd = new List<GameShaderEntry> { E("detail", false, true), E("solid", false, true), E("solid", false, false) };
            toGame.Clear();
            toHdrp.Clear();
            GameShaderSwap.Plan(GameShaderMode.Plain, odd, toGame, toHdrp);
            Program.Check(string.Join(",", toGame) == "2" && string.Join(",", toHdrp) == "0", "a family that does not move is put back if it was ever marked moved: " + string.Join(",", toGame) + "/" + string.Join(",", toHdrp));

            toGame.Clear();
            toHdrp.Clear();
            GameShaderSwap.Plan(GameShaderMode.Lit, new List<GameShaderEntry>(), toGame, toHdrp);
            Program.Check(toGame.Count == 0 && toHdrp.Count == 0, "no materials, no plan");

            string line = GameShaderSwap.Describe(GameShaderMode.Faces, entries, new List<int> { 0, 1, 2, 8 }, new List<int>());
            Program.Check(line == "game shader faces: 4 (1 concrete, 1 face, 1 metal, 1 solid) to the game's shader, 0 back to HDRP/Lit", "the log line: " + line);
            line = GameShaderSwap.Describe(GameShaderMode.Off, entries, new List<int>(), new List<int> { 0, 0, 3 });
            Program.Check(line == "game shader off: 0 to the game's shader, 3 (1 face, 2 solid) back to HDRP/Lit", "the log line counts a family twice: " + line);
        }

        private static void Experiment()
        {
            var problems = new List<string>();
            foreach (GameShaderMode mode in new[] { GameShaderMode.Off, GameShaderMode.Plain, GameShaderMode.Faces, GameShaderMode.Lit })
            {
                MaterialExperiment e = MaterialExperiment.Parse("gameShader = " + GameShaderRoute.Name(mode), problems);
                Program.Check(e.GameShader == mode && problems.Count == 0 && !e.IsEmpty, "gameShader " + mode + " is read and is not an empty experiment");
            }

            Program.Check(MaterialExperiment.Parse("GAMESHADER = FACES", null).GameShader == GameShaderMode.Faces, "key and value in any case");
            Program.Check(MaterialExperiment.Parse("gameShader = off", null).IsEmpty == false && MaterialExperiment.None.GameShader == null && MaterialExperiment.None.IsEmpty, "off is a setting, not the absence of one");
            problems.Clear();
            MaterialExperiment bad = MaterialExperiment.Parse("gameShader = on", problems);
            Program.Check(!bad.GameShader.HasValue && problems.Count == 1 && problems[0].StartsWith("line 1:") && problems[0].Contains("gameShader") && problems[0].Contains("off, plain, faces or lit"), "'on' is not a mode: " + string.Join("|", problems));
            Program.Check(MaterialExperiment.Parse("gameShader = plain\ngameShader = lit", null).GameShader == GameShaderMode.Lit, "the last line wins");
            Program.Check(MaterialExperiment.Parse("gameShader = plain\ngameShader = banana", null).GameShader == GameShaderMode.Plain, "a wrong later line leaves the earlier one");

            MaterialExperiment both = MaterialExperiment.Parse("queue = 2000\ngameShader = faces # the poles\ndecalMask = none", null);
            Program.Check(both.GameShader == GameShaderMode.Faces && both.Queue == 2000 && both.DecalMask == 0, "beside the other keys and a comment");
            Program.Check(both.Describe() == "decalMask none (0x0), queue 2000, gameShader faces", "described: " + both.Describe());
            Program.Check(MaterialExperiment.Parse("gameShader = lit", null).Describe() == "gameShader lit", "described alone");

            Program.Check(MaterialExperiment.Parse("gameShader = plain", null).Equals(MaterialExperiment.Parse("gameShader = plain", null)) && !MaterialExperiment.Parse("gameShader = plain", null).Equals(MaterialExperiment.Parse("gameShader = faces", null)), "two experiments differ by their mode");
            Program.Check(!MaterialExperiment.Parse("gameShader = off", null).Equals(MaterialExperiment.None), "off differs from no line (the file changed: the mode is taken from the preferences again when the line goes)");
            Program.Check(MaterialExperiment.Parse("gameShader = plain", null).GetHashCode() != MaterialExperiment.Parse("gameShader = lit", null).GetHashCode(), "the hash sees the mode");
        }
    }
}
