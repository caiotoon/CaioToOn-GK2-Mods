using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace QuickStash
{
    /// <summary>
    /// Reads the stash triggers where the game reads the player's own input: a prefix on PlayerInputHandler.UpdateInput(),
    /// which SSM.CustomUpdate only reaches while FreePlayerState is current and controls are enabled.
    /// </summary>
    internal static class StashInput
    {
        private static string resolvedText;   // GamepadStash value the two fields below were resolved from
        private static GameKey pressKey;      // null: controller stash is off
        private static List<GameKey> holdKeys = new List<GameKey>();

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerInputHandler), nameof(PlayerInputHandler.UpdateInput))]
        private static void UpdateInputPrefix()
        {
            try
            {
                if (IsKeyboardDown(Plugin.StashKey.Value))
                {
                    Plugin.TryStash();
                }
                else if (IsGamepadChordDown())
                {
                    SwallowPress();   // first, so a failing stash cannot leave the press to the game
                    Plugin.TryStash();
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Stash failed: " + e); }
        }

        /// <summary>
        /// BepInEx's KeyboardShortcut.IsDown() returns false while any unrelated key is held (e.g. W while walking),
        /// so the shortcut is tested manually: main key just pressed and every modifier held.
        /// </summary>
        private static bool IsKeyboardDown(KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        private static bool IsGamepadChordDown()
        {
            if (!LazyInput.IsInitialized || !LazyInput.IsGamepadActive) return false;
            if (resolvedText != Plugin.GamepadStash.Value) Resolve(Plugin.GamepadStash.Value);

            // Enumeration's == returns false when either side is null, so null is tested on the reference
            return (object)pressKey != null && LazyInput.GetKeyDown(pressKey) && holdKeys.All(LazyInput.GetKey);
        }

        /// <summary>
        /// "Hold+Hold+Press": names of public static GameKey fields. A problem logs one warning and leaves the
        /// controller stash off until the setting changes.
        /// </summary>
        private static void Resolve(string text)
        {
            resolvedText = text;
            pressKey = null;
            if (string.IsNullOrWhiteSpace(text)) return;

            var keys = new List<GameKey>();
            foreach (string part in text.Split('+'))
            {
                string name = part.Trim();
                FieldInfo field = typeof(GameKey).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                var key = field?.GetValue(null) as GameKey;
                if ((object)key == null) { Warn("'" + name + "' is not a game action"); return; }
                if (key.value == GameKey.None.value) return;
                if (!LazyInput.GameBindings.gamepadBindings.Any(b => b.gameKey.value == key.value)) { Warn("no controller button raises '" + name + "'"); return; }
                keys.Add(key);
            }
            pressKey = keys[keys.Count - 1];
            holdKeys = keys.Take(keys.Count - 1).ToList();
        }

        private static void Warn(string problem)
        {
            Plugin.Log.LogWarning("GamepadStash = " + resolvedText + ": " + problem + ". Controller stash is off until the setting changes.");
        }

        /// <summary>
        /// Keeps the game from reacting to the pressed button: every action raised by the same physical button (its other
        /// bindings and their aliases) is cleared for this frame and ignored until release. Held keys are left alone.
        /// </summary>
        private static void SwallowPress()
        {
            GameBindings bindings = LazyInput.GameBindings;
            var buttons = new HashSet<int>();
            foreach (GamepadBinding b in bindings.gamepadBindings)
                if (b.gameKey.value == pressKey.value) buttons.Add(b.gamepadButton.value);

            foreach (GamepadBinding b in bindings.gamepadBindings)
            {
                if (!buttons.Contains(b.gamepadButton.value)) continue;
                Ignore(b.gameKey);
                foreach (BindingAlias alias in bindings.bindingAliases)
                    if (alias.gameKey1.value == b.gameKey.value) Ignore(alias.gameKey2);
            }
        }

        private static void Ignore(GameKey key)
        {
            LazyInput.ClearKeyDown(key);
            LazyInput.WaitForRelease(key);
        }
    }
}
