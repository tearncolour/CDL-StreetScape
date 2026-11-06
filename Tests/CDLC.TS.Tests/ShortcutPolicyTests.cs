using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The game shortcuts held back while a placement tool is active (ShortcutPolicy; the owner, 2 Nov: "放置时应该屏蔽游戏其他快捷键"): a fixture that has the shape of the game's maps.</summary>
    internal static class ShortcutPolicyTests
    {
        private const string K = "<Keyboard>/", Ctrl = "<Keyboard>/ctrl", Shift = "<Keyboard>/shift";

        public static void RunAll()
        {
            Program.Run("shortcuts: the Shortcuts map is held back, but not pause, speed, saving and the screenshot", ShortcutsMap);
            Program.Run("shortcuts: a kept action that is on a tool key is held back all the same", KeptOnToolKey);
            Program.Run("shortcuts: the Tool map is held only where it is on a tool key, and its own actions never", ToolMap);
            Program.Run("shortcuts: Ctrl+Z and Ctrl+Y are the tool's, a bare Z is not", CtrlKeys);
            Program.Run("shortcuts: a camera action on a tool key is held only while the key is down", CameraMap);
            Program.Run("shortcuts: the game's own screens are left alone", ScreenMaps);
            Program.Run("shortcuts: other maps, case, malformed bindings and the order", Others);
            Program.Run("shortcuts: the tool's keys", Keys);
            Program.Run("shortcuts: the text for the log", Describe);
        }

        private static GameKeyBinding B(string map, string action, string path, params string[] modifiers) => new GameKeyBinding(map, action, path, modifiers);

        /// <summary>What the game's keyboard bindings look like, as far as the owner's logs and the game's code tell: the Shortcuts map with panels, info views, pause and speeds; Camera/Move on WASD and arrows.</summary>
        private static List<GameKeyBinding> Game() => new List<GameKeyBinding>
        {
            B("Shortcuts", "Economy Panel", K + "e"), B("Shortcuts", "Info View", K + "i"), B("Shortcuts", "Statistic Panel", K + "s", Ctrl),
            B("Shortcuts", "City Information Panel", K + "c"), B("Shortcuts", "Transportation Overview Panel", K + "t"),
            B("Shortcuts", "Pause Simulation", K + "space"), B("Shortcuts", "Speed 2", K + "2"), B("Shortcuts", "Quick Save", K + "f5"), B("Shortcuts", "Screenshot", K + "f12"),
            B("Camera", "Move", K + "w"), B("Camera", "Move", K + "a"), B("Camera", "Move", K + "s"), B("Camera", "Move", K + "d"),
            B("Camera", "Move", K + "upArrow"), B("Camera", "Move", K + "downArrow"), B("Camera", "Move", K + "leftArrow"), B("Camera", "Move", K + "rightArrow"),
            B("Camera", "Rotate", K + "q"), B("Camera", "Zoom", K + "pageUp"), B("Camera", "Move Fast", K + "leftShift"),
            B("Tool", "Apply", K + "enter"), B("Tool", "Secondary Apply", K + "numpadEnter"), B("Tool", "Cancel", K + "escape"), B("Tool", "Default Tool", K + "q"),
            B("Tool", "Rotate Object", K + "leftBracket"), B("Tool", "Elevation Up", K + "pageUp"), B("Tool", "Undo", K + "z", Ctrl), B("Tool", "Zoom To", K + "z"),
            B("Navigation", "Submit", K + "enter"), B("Navigation", "Back", K + "backspace"), B("Menu", "Options", K + "escape"), B("Photo mode", "Toggle", K + "period")
        };

        private static bool Held(ShortcutPlan plan, string map, string action) => plan.Held.Contains(new GameActionRef(map, action));

        private static void ShortcutsMap()
        {
            ShortcutPlan plan = ShortcutPolicy.Plan(Game());
            foreach (string action in new[] { "Economy Panel", "Info View", "Statistic Panel", "City Information Panel", "Transportation Overview Panel" })
                Program.Check(Held(plan, "Shortcuts", action), action + " is held back");
            foreach (string action in new[] { "Pause Simulation", "Speed 2", "Quick Save", "Screenshot" })
            {
                Program.Check(!Held(plan, "Shortcuts", action), action + " is kept");
                Program.Check(plan.KeptNotes.Any(n => n.StartsWith("Shortcuts/" + action + " ")), action + " is in the notes of what was kept");
            }

            Program.Check(ShortcutPolicy.KeepsInShortcuts("PAUSE") && ShortcutPolicy.KeepsInShortcuts("Game SPEED 3") && ShortcutPolicy.KeepsInShortcuts("QuickSave") && ShortcutPolicy.KeepsInShortcuts("SCREENSHOT") && ShortcutPolicy.KeepsInShortcuts("Simulation Step"), "each word is found in any case");
            Program.Check(!ShortcutPolicy.KeepsInShortcuts("Economy Panel") && !ShortcutPolicy.KeepsInShortcuts("") && !ShortcutPolicy.KeepsInShortcuts(null), "a panel, an empty and a missing name are not kept");
            var onlyKept = ShortcutPolicy.Plan(new[] { B("Shortcuts", "Pause Simulation", K + "space") });
            Program.Check(onlyKept.Held.Count == 0 && onlyKept.KeptNotes.Count == 1, "a map that only has kept actions holds nothing");
        }

        private static void KeptOnToolKey()
        {
            ShortcutPlan plan = ShortcutPolicy.Plan(new[]
            {
                B("Shortcuts", "Pause Simulation", K + "space"), B("Shortcuts", "Pause Simulation", K + "comma"),
                B("Shortcuts", "Speed 3", K + "period")
            });
            Program.Check(Held(plan, "Shortcuts", "Pause Simulation") && Held(plan, "Shortcuts", "Speed 3"), "pause on the comma key and a speed on the period key are held back: the tool's keys come first");
            Program.Check(plan.KeptNotes.Count == 0, "and they are not listed as kept, though one of them has a binding of its own on space");
        }

        private static void ToolMap()
        {
            ShortcutPlan plan = ShortcutPolicy.Plan(Game());
            Program.Check(Held(plan, "Tool", "Rotate Object"), "a tool action on the bracket key is held back");
            Program.Check(!Held(plan, "Tool", "Elevation Up") && !Held(plan, "Tool", "Default Tool"), "a tool action on other keys is left");
            foreach (string own in new[] { "Apply", "Secondary Apply", "Cancel", "Default Tool" })
                Program.Check(!Held(plan, "Tool", own), "the tool's own " + own + " is not held back");
            Program.Check(!Held(plan, "Tool", "Apply"), "Apply stays even though the game binds it to Enter, which the polygon's confirmation reads");
            ShortcutPlan own2 = ShortcutPolicy.Plan(new[] { B("Tool", "Cancel", K + "backspace"), B("Tool", "Secondary Apply", K + "comma"), B("Tool", "Default Tool", K + "period"), B("Tool", "Apply", K + "semicolon") });
            Program.Check(own2.Held.Count == 0, "the tool's own actions are never held, whatever key they are on");
        }

        private static void CtrlKeys()
        {
            ShortcutPlan plan = ShortcutPolicy.Plan(Game());
            Program.Check(Held(plan, "Tool", "Undo"), "an action on Ctrl+Z is held back: the tool undoes by itself");
            Program.Check(!Held(plan, "Tool", "Zoom To"), "an action on a bare Z is not in the tool's way");
            Program.Check(ShortcutPolicy.OnToolKey(B("Tool", "x", K + "y", Ctrl, Shift), ShortcutPolicy.ToolKeys), "Ctrl+Shift+Y is on the tool's key");
            Program.Check(!ShortcutPolicy.OnToolKey(B("Tool", "x", K + "y", Shift), ShortcutPolicy.ToolKeys), "Shift+Y is not");
            Program.Check(ShortcutPolicy.OnToolKey(B("Tool", "x", K + "upArrow", Shift), ShortcutPolicy.ToolKeys), "an arrow key with Shift is the tool's arrow key");
            Program.Check(ShortcutPolicy.OnToolKey(B("Tool", "x", K + "COMMA", "<KEYBOARD>/CTRL"), ShortcutPolicy.ToolKeys), "the paths are compared in any case");
            Program.Check(ShortcutPolicy.OnToolKey(B("Tool", "x", K + "Z", "<KEYBOARD>/CTRL"), ShortcutPolicy.ToolKeys), "and so is the modifier of a key that needs Ctrl");
        }

        private static void CameraMap()
        {
            ShortcutPlan plan = ShortcutPolicy.Plan(Game());
            Program.Check(plan.CameraHeld.Count == 1 && plan.CameraHeld[0].Equals(new GameActionRef("Camera", "Move")), "only Move shares keys with the tool (the arrow keys)");
            Program.Check(!Held(plan, "Camera", "Move") && !Held(plan, "Camera", "Rotate") && !Held(plan, "Camera", "Zoom") && !Held(plan, "Camera", "Move Fast"), "no camera action is held all the time");
            Program.Check(string.Join(",", plan.CameraKeys) == K + "downArrow," + K + "leftArrow," + K + "rightArrow," + K + "upArrow", "the keys that hold it are the four arrows, sorted: " + string.Join(",", plan.CameraKeys));
            Program.Check(plan.CameraNotes.Count == 1 && plan.CameraNotes[0].Contains(K + "upArrow"), "the note names the keys");

            var down = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Program.Check(!ShortcutPolicy.CameraKeyDown(plan, down.Contains), "no key down: not held");
            down.Add(K + "w");
            Program.Check(!ShortcutPolicy.CameraKeyDown(plan, down.Contains), "W is down: the camera keeps its other keys");
            down.Add(K + "LeftArrow");
            Program.Check(ShortcutPolicy.CameraKeyDown(plan, down.Contains), "an arrow is down: held");
            Program.Check(!ShortcutPolicy.CameraKeyDown(null, down.Contains) && !ShortcutPolicy.CameraKeyDown(plan, null), "a missing plan or reader holds nothing");
            Program.Check(ShortcutPolicy.Plan(new[] { B("Camera", "Move", K + "w") }).CameraKeys.Count == 0, "a camera without a shared key has no keys to wait for");
        }

        private static void ScreenMaps()
        {
            ShortcutPlan plan = ShortcutPolicy.Plan(Game());
            // The owner's report of 2 Nov: the > key (period, the tool's kerb distance) still opened the photo mode: an action on a tool key is held in EVERY map that is a game in progress.
            Program.Check(Held(plan, "Photo mode", "Toggle"), "the photo mode's key on the period key is held");
            Program.Check(!Held(plan, "Navigation", "Submit") && !Held(plan, "Navigation", "Back") && !Held(plan, "Menu", "Options"), "the focus keys of Navigation (Enter, Backspace) and Esc in Menu are left alone");
            Program.Check(plan.LeftNotes.Any(n => n.StartsWith("Navigation/Submit")) && plan.LeftNotes.Any(n => n.StartsWith("Navigation/Back")) && !plan.LeftNotes.Any(n => n.StartsWith("Menu/")), "what was left on a tool key is listed: the two focus keys, and not Esc (not a tool key)");
            foreach (string map in new[] { "Photo mode", "Editor", "Debug", "Navigation", "Menu" })
            {
                ShortcutPlan one = ShortcutPolicy.Plan(new[] { B(map, "A", K + "comma"), B(map, "Elsewhere", K + "f9") });
                Program.Check(Held(one, map, "A") && !Held(one, map, "Elsewhere") && one.CameraHeld.Count == 0, map + ": an action on the comma key is held, one on another key is not");
            }

            foreach (string map in new[] { "Splash screen", "Engagement", "Benchmark" })
            {
                ShortcutPlan one = ShortcutPolicy.Plan(new[] { B(map, "A", K + "comma") });
                Program.Check(one.Held.Count == 0 && one.CameraHeld.Count == 0, map + " belongs to no game in progress: left alone");
            }

            foreach (string key in new[] { "leftArrow", "rightArrow", "upArrow", "downArrow", "enter", "numpadEnter", "backspace" })
            {
                ShortcutPlan focus = ShortcutPolicy.Plan(new[] { B("Navigation", "Move Focus", K + key), B("Menu", "Move Focus", K + key) });
                Program.Check(focus.Held.Count == 0 && focus.LeftNotes.Count == 2, key + " in Navigation and Menu moves the focus of a screen: left alone, listed");
            }

            foreach (string key in new[] { "comma", "period", "semicolon", "quote", "leftBracket", "rightBracket", "backslash", "numpadPlus" })
            {
                ShortcutPlan nav = ShortcutPolicy.Plan(new[] { B("Navigation", "X", K + key) });
                Program.Check(Held(nav, "Navigation", "X"), key + " in Navigation is no focus key: held");
            }

            Program.Check(ShortcutPolicy.Describe(plan).Contains("left alone (moves the focus of a screen): Navigation/Submit"), "the log lists what was left on a tool key");
        }

        private static void Others()
        {
            ShortcutPlan plan = ShortcutPolicy.Plan(new[]
            {
                B("Mod Map", "On Tool Key", K + "semicolon"), B("Mod Map", "Elsewhere", K + "f9"),
                B("Shortcuts", "Economy Panel", K + "e"), B("", "No Map", K + "comma"), B("Shortcuts", "", K + "e"), B("Shortcuts", "No Path", ""), B(null, null, null)
            });
            Program.Check(Held(plan, "Mod Map", "On Tool Key") && !Held(plan, "Mod Map", "Elsewhere"), "a map the code does not know is held where it is in the way and nowhere else");
            Program.Check(plan.BindingsSeen == 3 && plan.BindingsOnToolKeys == 1, $"bindings without a map, action or path are not counted: {plan.BindingsSeen}, {plan.BindingsOnToolKeys}");

            List<GameKeyBinding> all = Game();
            ShortcutPlan a = ShortcutPolicy.Plan(all);
            var shuffled = new List<GameKeyBinding>(all);
            shuffled.Reverse();
            ShortcutPlan b = ShortcutPolicy.Plan(shuffled);
            Program.Check(a.Held.SequenceEqual(b.Held) && a.CameraHeld.SequenceEqual(b.CameraHeld) && a.CameraKeys.SequenceEqual(b.CameraKeys), "the order of the bindings does not change the plan");
            Program.Check(a.HeldNotes.SequenceEqual(b.HeldNotes) && a.KeptNotes.SequenceEqual(b.KeptNotes), "nor the notes");
            Program.Check(a.Held.Select(x => x.ToString()).SequenceEqual(a.Held.Select(x => x.ToString()).OrderBy(x => x, StringComparer.Ordinal)), "the held actions are in order");
            Program.Check(ShortcutPolicy.Plan(null).Held.Count == 0 && ShortcutPolicy.Plan(new GameKeyBinding[0]).BindingsSeen == 0, "no bindings, nothing held");
            ShortcutPlan custom = ShortcutPolicy.Plan(new[] { B("Shortcuts", "Economy Panel", K + "e"), B("Tool", "A", K + "f9") }, new[] { new ToolKey(K + "f9") });
            Program.Check(Held(custom, "Tool", "A") && custom.BindingsOnToolKeys == 1, "the tool's keys can be given");
            ShortcutPlan twice = ShortcutPolicy.Plan(new[] { B("Shortcuts", "Economy Panel", K + "e"), B("Shortcuts", "Economy Panel", K + "e"), B("Shortcuts", "Economy Panel", K + "i") });
            Program.Check(twice.Held.Count == 1 && twice.ActionsPerMap["Shortcuts"] == 1, "an action with three bindings is held once and counted once");
            Program.Check(twice.HeldNotes.Count == 1 && twice.HeldNotes[0] == "Shortcuts/Economy Panel " + K + "e, " + K + "i", "and its note has the two keys once each: " + twice.HeldNotes.FirstOrDefault());
        }

        private static void Keys()
        {
            var paths = new HashSet<string>(ShortcutPolicy.ToolKeys.Select(k => k.Path), StringComparer.OrdinalIgnoreCase);
            foreach (string key in new[] { "comma", "period", "semicolon", "quote", "leftBracket", "rightBracket", "backslash", "leftArrow", "rightArrow", "upArrow", "downArrow", "numpad4", "numpad6", "numpad8", "numpad2", "numpadPlus", "numpadMinus", "enter", "numpadEnter", "backspace", "z", "y" })
                Program.Check(paths.Contains(K + key), key + " is a key of the tool");
            foreach (string modifier in new[] { "shift", "ctrl", "alt", "leftShift", "rightShift", "leftCtrl", "rightCtrl", "leftAlt", "rightAlt" })
                Program.Check(!paths.Contains(K + modifier), modifier + " is a modifier of the tool's keys, not a key of its own");
            Program.Check(ShortcutPolicy.ToolKeys.Where(k => k.WithCtrl).Select(k => k.Path).OrderBy(x => x).SequenceEqual(new[] { K + "y", K + "z" }), "only Z and Y need Ctrl");
            Program.Check(paths.Count == ShortcutPolicy.ToolKeys.Length, "no key is listed twice");
        }

        private static void Describe()
        {
            string text = ShortcutPolicy.Describe(ShortcutPolicy.Plan(Game()));
            Program.Check(text.Contains("held: Shortcuts/Economy Panel " + K + "e"), "a held action is listed with its key");
            Program.Check(text.Contains("held: Shortcuts/Statistic Panel " + K + "s with " + Ctrl), "a modifier is listed too");
            Program.Check(text.Contains("camera, while its key is down: Camera/Move"), "the camera action is listed");
            Program.Check(text.Contains("kept in the Shortcuts map: Shortcuts/Pause Simulation " + K + "space"), "what was kept is listed");
            Program.Check(text.Contains("Shortcuts=") && text.Contains("Camera=") && text.Contains("Tool="), "the number of actions of each map is there");
            Program.Check(text.Contains("1 camera action(s) while " + K + "downArrow"), "the keys the camera waits for are in the first line");
            string cut = ShortcutPolicy.Describe(ShortcutPolicy.Plan(Game()), 3);
            Program.Check(cut.Split('\n').Length == 5 && cut.EndsWith("(more not listed)"), "a long list is cut: " + cut.Split('\n').Length + " lines");
            Program.Check(ShortcutPolicy.Describe(ShortcutPolicy.Plan(null)).Split('\n').Length == 1, "an empty plan is one line");
        }
    }
}
