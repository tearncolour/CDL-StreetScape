using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using Game.Input;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CDLC.TS
{
    /// <summary>
    /// Holds the game's keyboard shortcuts back while a placement tool is the active tool (ShortcutPolicy says which). Two InputBarriers with a keyboard mask, made when the tool starts and disposed when
    /// it stops, so nothing outlives the tool and a rebinding made in between is read again at the next start: the first holds actions back all the time, the second the camera actions that share a key with
    /// the tool, only while that key is down. Fails open: any exception releases both and switches this off for the session, because a barrier that stayed on would lock the player's shortcuts.
    /// Main thread. The game's side of this is read from the game's code and checked only by the log line of the first start.
    /// </summary>
    internal static class ShortcutBarrier
    {
        // The maps the policy looks at; the game has no list of its maps that a mod may read.
        private static readonly string[] s_Maps =
        {
            InputManager.kNavigationMap, InputManager.kMenuMap, InputManager.kCameraMap, InputManager.kToolMap, InputManager.kShortcutsMap,
            InputManager.kPhotoModeMap, InputManager.kEditorMap, InputManager.kDebugMap
        };

        private static readonly Func<string, bool> s_IsDown = IsDown;
        private static InputBarrier s_Held, s_Camera;
        private static ShortcutPlan s_Plan;
        private static Keyboard s_Keyboard;
        private static bool s_Failed, s_Logged;

        internal static bool Active => s_Held != null || s_Camera != null;

        /// <summary>The tool started in a game: hold the shortcuts back (nothing when the player switched it off in the preferences or it failed before).</summary>
        internal static void Begin()
        {
            if (s_Failed || Active || PreferencesStore.Current.PlaceShortcuts == FeatureState.Off)
                return;
            try
            {
                InputManager input = InputManager.instance;
                if (input == null)
                    return;
                ShortcutPlan plan = ShortcutPolicy.Plan(ReadBindings(input));
                List<ProxyAction> held = Resolve(input, plan.Held), camera = Resolve(input, plan.CameraHeld);
                if (held.Count > 0)
                    s_Held = new InputBarrier("CDLC.TS.PlaceShortcuts", held, InputManager.DeviceType.Keyboard, true);
                if (camera.Count > 0 && plan.CameraKeys.Count > 0)
                    s_Camera = new InputBarrier("CDLC.TS.PlaceCameraKeys", camera, InputManager.DeviceType.Keyboard, false);
                s_Plan = plan;
                if (!s_Logged)
                {
                    s_Logged = true;
                    Mod.log.Info(ShortcutPolicy.Describe(plan));
                }
            }
            catch (Exception ex)
            {
                Fail("could not be set up", ex);
            }
        }

        /// <summary>Every frame the tool runs: the camera actions on the tool's keys are held back while one of those keys is down.</summary>
        internal static void Tick()
        {
            if (s_Camera == null || s_Plan == null)
                return;
            try
            {
                s_Keyboard = Keyboard.current;
                bool down = ShortcutPolicy.CameraKeyDown(s_Plan, s_IsDown);
                if (s_Camera.blocked != down)
                    s_Camera.blocked = down;
            }
            catch (Exception ex)
            {
                Fail("could not be switched", ex);
            }
        }

        /// <summary>The tool stopped, the city was left or the mod is disposed: everything is given back.</summary>
        internal static void End()
        {
            Release(ref s_Held);
            Release(ref s_Camera);
            s_Plan = null;
            s_Keyboard = null;
        }

        private static void Fail(string what, Exception ex)
        {
            s_Failed = true;
            End();
            Mod.log.Warn($"Shortcuts: the hold on the game's shortcuts {what} ({ex.GetType().Name}: {ex.Message}); they are left alone for the rest of the session.");
        }

        private static void Release(ref InputBarrier barrier)
        {
            InputBarrier old = barrier;
            barrier = null;
            if (old == null)
                return;
            // Both are tried: a fault in the first must not leave the barrier registered.
            try
            {
                old.blocked = false;
            }
            catch (Exception ex)
            {
                s_Failed = true;
                Mod.log.Warn($"Shortcuts: a barrier could not be unblocked ({ex.GetType().Name}: {ex.Message}).");
            }

            try
            {
                old.Dispose();
            }
            catch (Exception ex)
            {
                s_Failed = true;
                Mod.log.Warn($"Shortcuts: a barrier could not be disposed ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static bool IsDown(string path)
        {
            int slash = path.LastIndexOf('/');
            var key = s_Keyboard?.TryGetChildControl(slash >= 0 ? path.Substring(slash + 1) : path) as ButtonControl;
            return key != null && key.isPressed;
        }

        private static List<GameKeyBinding> ReadBindings(InputManager input)
        {
            var list = new List<GameKeyBinding>();
            foreach (string name in s_Maps)
            {
                if (!input.TryFindActionMap(name, out ProxyActionMap map))
                    continue;
                foreach (ProxyAction action in map.actions.Values)
                {
                    foreach (ProxyBinding binding in action.bindings)
                    {
                        if (!binding.isKeyboard || !binding.isSet)
                            continue;
                        var modifiers = new string[binding.modifiers.Count];
                        for (int i = 0; i < modifiers.Length; i++)
                            modifiers[i] = binding.modifiers[i].m_Path;
                        list.Add(new GameKeyBinding(map.name, action.name, binding.path, modifiers));
                    }
                }
            }

            return list;
        }

        private static List<ProxyAction> Resolve(InputManager input, List<GameActionRef> refs)
        {
            var list = new List<ProxyAction>(refs.Count);
            foreach (GameActionRef reference in refs)
            {
                ProxyAction action = input.FindAction(reference.Map, reference.Action);
                if (action != null)
                    list.Add(action);
            }

            return list;
        }
    }
}
