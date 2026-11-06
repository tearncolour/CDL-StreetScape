using System;
using System.Collections.Generic;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>One keyboard binding of a game action as the game's input manager lists it: the path is "&lt;Keyboard&gt;/comma", a modifier is named by its own path ("&lt;Keyboard&gt;/ctrl").</summary>
    public readonly struct GameKeyBinding
    {
        public readonly string Map, Action, Path;
        public readonly string[] Modifiers;

        public GameKeyBinding(string map, string action, string path, params string[] modifiers)
        {
            Map = map ?? "";
            Action = action ?? "";
            Path = path ?? "";
            Modifiers = modifiers ?? new string[0];
        }
    }

    /// <summary>A key the placement tool reads itself (Keyboard.current: the game's input masking does not reach it). WithCtrl: only with Ctrl held, so a game action on a bare key is not in its way.</summary>
    public readonly struct ToolKey
    {
        public readonly string Path;
        public readonly bool WithCtrl;

        public ToolKey(string path, bool withCtrl = false)
        {
            Path = path;
            WithCtrl = withCtrl;
        }
    }

    /// <summary>A game action by the names the input manager finds it by.</summary>
    public readonly struct GameActionRef : IEquatable<GameActionRef>, IComparable<GameActionRef>
    {
        public readonly string Map, Action;

        public GameActionRef(string map, string action)
        {
            Map = map;
            Action = action;
        }

        public bool Equals(GameActionRef other) => Map == other.Map && Action == other.Action;

        public override bool Equals(object obj) => obj is GameActionRef other && Equals(other);

        public override int GetHashCode() => (Map ?? "").GetHashCode() * 31 + (Action ?? "").GetHashCode();

        public int CompareTo(GameActionRef other)
        {
            int map = string.CompareOrdinal(Map, other.Map);
            return map != 0 ? map : string.CompareOrdinal(Action, other.Action);
        }

        public override string ToString() => Map + "/" + Action;
    }

    /// <summary>What to hold back from the game while a placement tool is the active tool, worked out from the game's own bindings (ShortcutPolicy.Plan).</summary>
    public sealed class ShortcutPlan
    {
        /// <summary>Held back (keyboard only) the whole time the tool is active.</summary>
        public readonly List<GameActionRef> Held = new List<GameActionRef>();

        /// <summary>Camera actions that share a key with the tool: held back only while one of CameraKeys is down, so the other camera keys work in between.</summary>
        public readonly List<GameActionRef> CameraHeld = new List<GameActionRef>();

        public readonly List<string> CameraKeys = new List<string>();

        /// <summary>For the log: each held action with the keys that put it there ("Shortcuts/Economy Panel &lt;Keyboard&gt;/e"), and what was left alone in the Shortcuts map.</summary>
        public readonly List<string> HeldNotes = new List<string>(), CameraNotes = new List<string>(), KeptNotes = new List<string>(), LeftNotes = new List<string>();

        public readonly SortedDictionary<string, int> ActionsPerMap = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public int BindingsSeen, BindingsOnToolKeys;
    }

    /// <summary>
    /// Which of the game's keyboard shortcuts to hold back while a placement tool is the active tool (the owner, 2 Nov: "放置时应该屏蔽游戏其他快捷键"). The tool reads its keys from the keyboard itself, so
    /// the game's own actions bound to the same keys fire as well (the arrow keys move the camera while a board is nudged); and the game's shortcuts (panels, info views) answer to keys typed for the tool.
    /// Pure: the game part (Tool/ShortcutBarrier.cs) lists the bindings and makes the barriers.
    /// <list type="bullet">
    /// <item>Shortcuts map: every action that has a keyboard binding is held back, but pause and speed, saving and the screenshot (<see cref="KeepsInShortcuts"/>): time control is wanted while placing, and
    /// nothing the tool reads is on those keys. A kept action that is on a tool key (rebound by the player) is held back all the same.</item>
    /// <item>Tool map: only an action on a tool key. Its other actions are switched on by the tools that ask for them and this one asks for none; the tool's own apply, secondary apply and cancel are never held.</item>
    /// <item>Camera map: never as a whole (the player looks around while placing); an action on a tool key is held back only while that key is down.</item>
    /// <item>Every other map (Photo mode, Editor, Debug, a mod's): an action on a tool key is held back, 0.12.76: the first version left whole maps alone and the owner's > key (the tool's kerb distance)
    /// still opened the photo mode. The exceptions are the focus keys of Navigation and Menu (the arrows, Enter, Backspace move the focus of a screen: left alone, and listed in the log), and the maps that
    /// belong to no game in progress (Splash screen, Engagement, Benchmark).</item>
    /// </list>
    /// </summary>
    public static class ShortcutPolicy
    {
        public const string ShortcutsMap = "Shortcuts", ToolMap = "Tool", CameraMap = "Camera";

        private const string Prefix = "<Keyboard>/", CtrlPath = "<Keyboard>/ctrl";

        /// <summary>The keys PlacementTool reads (ReadKeys, ReadArrows, the polygon's Enter and Backspace); Shift, Alt and Ctrl are modifiers there and are left to the game.</summary>
        public static readonly ToolKey[] ToolKeys =
        {
            new ToolKey(Prefix + "leftBracket"), new ToolKey(Prefix + "rightBracket"), new ToolKey(Prefix + "backslash"),
            new ToolKey(Prefix + "semicolon"), new ToolKey(Prefix + "quote"), new ToolKey(Prefix + "comma"), new ToolKey(Prefix + "period"),
            new ToolKey(Prefix + "leftArrow"), new ToolKey(Prefix + "rightArrow"), new ToolKey(Prefix + "upArrow"), new ToolKey(Prefix + "downArrow"),
            new ToolKey(Prefix + "numpad4"), new ToolKey(Prefix + "numpad6"), new ToolKey(Prefix + "numpad8"), new ToolKey(Prefix + "numpad2"),
            new ToolKey(Prefix + "numpadPlus"), new ToolKey(Prefix + "numpadMinus"),
            new ToolKey(Prefix + "enter"), new ToolKey(Prefix + "numpadEnter"), new ToolKey(Prefix + "backspace"),
            new ToolKey(Prefix + "z", true), new ToolKey(Prefix + "y", true),
            new ToolKey(Prefix + "minus"), new ToolKey(Prefix + "equals"), new ToolKey(Prefix + "slash")
        };

        /// <summary>The tool's own actions in the Tool map: the clicks and Esc.</summary>
        private static readonly string[] s_OwnToolActions = { "Apply", "Secondary Apply", "Cancel", "Default Tool" };

        /// <summary>Words of the Shortcuts actions that stay: pause and the speeds, saving, the screenshot.</summary>
        private static readonly string[] s_KeptWords = { "pause", "speed", "simulation", "screenshot", "save" };

        /// <summary>Whether the action of the Shortcuts map is one the player keeps while placing.</summary>
        public static bool KeepsInShortcuts(string action)
        {
            if (string.IsNullOrEmpty(action))
                return false;
            foreach (string word in s_KeptWords)
            {
                if (action.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>Whether a binding is on one of the tool's keys; a key that needs Ctrl is in the way only of a binding with Ctrl.</summary>
        public static bool OnToolKey(GameKeyBinding binding, IReadOnlyList<ToolKey> toolKeys)
        {
            foreach (ToolKey key in toolKeys)
            {
                if (!string.Equals(binding.Path, key.Path, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!key.WithCtrl || HasModifier(binding, CtrlPath))
                    return true;
            }

            return false;
        }

        private static bool HasModifier(GameKeyBinding binding, string modifier)
        {
            foreach (string m in binding.Modifiers)
            {
                if (string.Equals(m, modifier, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Works out what to hold back from the keyboard bindings the game has (gamepad and mouse bindings are not in the list: they are never touched).</summary>
        public static ShortcutPlan Plan(IEnumerable<GameKeyBinding> bindings, IReadOnlyList<ToolKey> toolKeys = null)
        {
            toolKeys = toolKeys ?? ToolKeys;
            var plan = new ShortcutPlan();
            var held = new Dictionary<GameActionRef, List<string>>();
            var camera = new Dictionary<GameActionRef, List<string>>();
            var kept = new Dictionary<GameActionRef, List<string>>();
            var left = new Dictionary<GameActionRef, List<string>>();
            var seenActions = new HashSet<GameActionRef>();
            var cameraKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameKeyBinding binding in bindings ?? new GameKeyBinding[0])
            {
                if (string.IsNullOrEmpty(binding.Path) || string.IsNullOrEmpty(binding.Map) || string.IsNullOrEmpty(binding.Action))
                    continue;
                plan.BindingsSeen++;
                var action = new GameActionRef(binding.Map, binding.Action);
                if (seenActions.Add(action))
                {
                    plan.ActionsPerMap.TryGetValue(binding.Map, out int count);
                    plan.ActionsPerMap[binding.Map] = count + 1;
                }

                bool onKey = OnToolKey(binding, toolKeys);
                if (onKey)
                    plan.BindingsOnToolKeys++;
                string note = binding.Path + (binding.Modifiers.Length > 0 ? " with " + string.Join("+", binding.Modifiers) : "");
                switch (binding.Map)
                {
                    case CameraMap:
                        if (onKey)
                        {
                            Add(camera, action, note);
                            cameraKeys.Add(binding.Path);
                        }

                        break;
                    case ToolMap:
                        if (onKey && Array.IndexOf(s_OwnToolActions, binding.Action) < 0)
                            Add(held, action, note);
                        break;
                    case ShortcutsMap:
                        if (onKey || !KeepsInShortcuts(binding.Action))
                            Add(held, action, note);
                        else
                            Add(kept, action, note);
                        break;
                    default:
                        if (!onKey || IsInertMap(binding.Map))
                            break;
                        if (IsFocusMap(binding.Map) && IsFocusKey(binding.Path))
                            Add(left, action, note);
                        else
                            Add(held, action, note);
                        break;
                }
            }

            foreach (GameActionRef action in Sorted(held.Keys))
            {
                plan.Held.Add(action);
                plan.HeldNotes.Add(action + " " + string.Join(", ", held[action]));
            }

            foreach (GameActionRef action in Sorted(camera.Keys))
            {
                plan.CameraHeld.Add(action);
                plan.CameraNotes.Add(action + " " + string.Join(", ", camera[action]));
            }

            foreach (GameActionRef action in Sorted(left.Keys))
            {
                if (!held.ContainsKey(action))
                    plan.LeftNotes.Add(action + " " + string.Join(", ", left[action]));
            }

            foreach (GameActionRef action in Sorted(kept.Keys))
            {
                // A kept action that was also put in the held list by another binding of its own is held, not kept.
                if (!held.ContainsKey(action))
                    plan.KeptNotes.Add(action + " " + string.Join(", ", kept[action]));
            }

            plan.CameraKeys.AddRange(cameraKeys);
            plan.CameraKeys.Sort(StringComparer.OrdinalIgnoreCase);
            return plan;
        }

        /// <summary>The maps of screens that are not a game in progress: nothing of them is in the tool's way.</summary>
        private static bool IsInertMap(string map) => map == "Splash screen" || map == "Engagement" || map == "Benchmark";

        /// <summary>The maps that move the focus of the game's screens by the keyboard.</summary>
        private static bool IsFocusMap(string map) => map == "Navigation" || map == "Menu";

        private static bool IsFocusKey(string path) =>
            string.Equals(path, Prefix + "leftArrow", StringComparison.OrdinalIgnoreCase) || string.Equals(path, Prefix + "rightArrow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, Prefix + "upArrow", StringComparison.OrdinalIgnoreCase) || string.Equals(path, Prefix + "downArrow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, Prefix + "enter", StringComparison.OrdinalIgnoreCase) || string.Equals(path, Prefix + "numpadEnter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, Prefix + "backspace", StringComparison.OrdinalIgnoreCase);

        private static void Add(Dictionary<GameActionRef, List<string>> map, GameActionRef action, string note)
        {
            if (!map.TryGetValue(action, out List<string> notes))
                map[action] = notes = new List<string>();
            if (!notes.Contains(note))
                notes.Add(note);
        }

        private static List<GameActionRef> Sorted(IEnumerable<GameActionRef> actions)
        {
            var list = new List<GameActionRef>(actions);
            list.Sort();
            return list;
        }

        /// <summary>Whether a camera key that is in the tool's way is down: the camera actions of the plan are held back while this is true.</summary>
        public static bool CameraKeyDown(ShortcutPlan plan, Func<string, bool> isDown)
        {
            if (plan == null || isDown == null)
                return false;
            foreach (string path in plan.CameraKeys)
            {
                if (isDown(path))
                    return true;
            }

            return false;
        }

        /// <summary>The text for the log, once a session: what is held back and why, what was left in the Shortcuts map, how many actions each map has.</summary>
        public static string Describe(ShortcutPlan plan, int maxLines = 160)
        {
            var text = new StringBuilder();
            text.Append("Shortcuts: while a placement tool is active the game's keyboard shortcuts are held back (mouse and gamepad untouched): ")
                .Append(plan.Held.Count).Append(" action(s) all the time, ").Append(plan.CameraHeld.Count).Append(" camera action(s) while ")
                .Append(plan.CameraKeys.Count > 0 ? string.Join(" ", plan.CameraKeys) : "no key").Append(" is down; ")
                .Append(plan.BindingsSeen).Append(" keyboard binding(s) seen, ").Append(plan.BindingsOnToolKeys).Append(" on the tool's keys; actions with a keyboard binding per map:");
            foreach (KeyValuePair<string, int> map in plan.ActionsPerMap)
                text.Append(' ').Append(map.Key).Append('=').Append(map.Value);
            int lines = 0;
            foreach (KeyValuePair<string, List<string>> group in new[]
            {
                new KeyValuePair<string, List<string>>("held", plan.HeldNotes),
                new KeyValuePair<string, List<string>>("camera, while its key is down", plan.CameraNotes),
                new KeyValuePair<string, List<string>>("kept in the Shortcuts map", plan.KeptNotes),
                new KeyValuePair<string, List<string>>("on a tool key and left alone (moves the focus of a screen)", plan.LeftNotes)
            })
            {
                foreach (string note in group.Value)
                {
                    if (lines++ >= maxLines)
                        return text.Append("\n  ... (more not listed)").ToString();
                    text.Append("\n  ").Append(group.Key).Append(": ").Append(note);
                }
            }

            return text.ToString();
        }
    }
}
