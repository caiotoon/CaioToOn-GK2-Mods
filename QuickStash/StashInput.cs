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
        private static string resolvedText;   // GamepadStash value the chord was resolved from
        private static List<GameKey> chord;   // held actions, then the pressed one; null: controller stash is off

        [HarmonyPrefix, HarmonyPatch(typeof(PlayerInputHandler), nameof(PlayerInputHandler.UpdateInput))]
        private static void UpdateInputPrefix()
        {
            try
            {
                bool keyboard = IsKeyboardDown(Plugin.StashKey.Value);
                if (!keyboard && !IsGamepadChordDown()) return;
                if (!Plugin.CanStash()) return;

                if (!keyboard) SwallowPress();   // only a press that starts a stash is kept from the game
                Plugin.Stash();
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
            if (!LazyInput.IsGamepadActive) return false;
            if (resolvedText != Plugin.GamepadStash.Value) Resolve(Plugin.GamepadStash.Value);
            if (chord == null) return false;

            int last = chord.Count - 1;
            return LazyInput.GetKeyDown(chord[last]) && chord.Take(last).All(LazyInput.GetKey);
        }

        /// <summary>
        /// "Hold+Hold+Press": names of public static GameKey fields. A problem logs one warning and leaves the
        /// controller stash off until the setting changes.
        /// </summary>
        private static void Resolve(string text)
        {
            resolvedText = text;
            chord = null;
            if (string.IsNullOrWhiteSpace(text)) return;

            var keys = new List<GameKey>();
            foreach (string part in text.Split('+'))
            {
                string name = part.Trim();
                FieldInfo field = typeof(GameKey).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                var key = field?.GetValue(null) as GameKey;
                // Enumeration's == returns false when either side is null, so null is tested on the reference
                if ((object)key == null) { Warn("'" + name + "' is not a game action"); return; }
                if (key.value == GameKey.None.value) return;
                if (!LazyInput.GameBindings.gamepadBindings.Any(b => b.gameKey.value == key.value)) { Warn("no controller button raises '" + name + "'"); return; }
                keys.Add(key);
            }
            chord = keys;
        }

        private static void Warn(string problem)
        {
            Plugin.Log.LogWarning("GamepadStash = " + resolvedText + ": " + problem + ". Controller stash is off until the setting changes.");
        }

        /// <summary>
        /// Keeps the game from reacting to the pressed button: every action raised by the same physical button is
        /// cleared for this frame and ignored until release. Held buttons are left alone.
        /// </summary>
        private static void SwallowPress()
        {
            List<GamepadBinding> bindings = LazyInput.GameBindings.gamepadBindings;
            GameKey pressed = chord[chord.Count - 1];
            GamepadButton button = bindings.Find(b => b.gameKey.value == pressed.value).gamepadButton;
            foreach (GamepadBinding b in bindings.Where(b => b.gamepadButton.value == button.value))
            {
                LazyInput.ClearKeyDown(b.gameKey);
                LazyInput.WaitForRelease(b.gameKey);
            }
        }
    }
}
