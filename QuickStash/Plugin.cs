using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using LazyBearTechnology;
using UnityEngine;

namespace QuickStash
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.caiotoon.gk2.quickstash";
        public const string PluginName = "QuickStash";
        public const string PluginVersion = "0.6.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyboardShortcut> StashKey;
        internal static ConfigEntry<KeyboardShortcut> DiagnosticsKey;
        internal static ConfigEntry<bool> DryRun;
        internal static ConfigEntry<bool> PlaySound;
        internal static ConfigEntry<bool> IncludeBagContents;
        internal static ConfigEntry<bool> ShowBubbles;
        internal static ConfigEntry<float> BubbleSeconds;
        internal static ConfigEntry<float> BubbleScale;
        internal static ConfigEntry<int> MaxBubbleItems;
        internal static ConfigEntry<bool> ShowBubbleCount;
        internal static ConfigEntry<int> BubbleColumns;
        internal static ConfigEntry<float> BubbleSpacing;

        /// <summary>Appended to every setting that is re-read from disk on each key press.</summary>
        private const string LiveNote = "Applies on the next StashKey/DiagnosticsKey press (the file is re-read then; no restart needed).";

        /// <summary>Time.unscaledTime of the last Config.Reload(); reloads are throttled to one per 2 s.</summary>
        private float lastConfigReload = -100f;
        internal static ConfigEntry<float> FallbackRadius;
        internal static ConfigEntry<bool> IncludeConveyorChests;

        private void Awake()
        {
            Log = Logger;

            StashKey = Config.Bind("Keys", "StashKey", new KeyboardShortcut(KeyCode.G),
                "Moves every stackable backpack item that already exists in a nearby container into that container (vanilla 'move all similar' per container).");
            DiagnosticsKey = Config.Bind("Keys", "DiagnosticsKey", new KeyboardShortcut(KeyCode.F9),
                "Writes BepInEx/QuickStash-dump.txt (+ a timestamped copy): containers around the player, backpack, key bindings, dry-run stash plan. Never moves anything.");

            DryRun = Config.Bind("Behaviour", "DryRun", false,
                "When true, StashKey only logs what would be moved.");
            PlaySound = Config.Bind("Behaviour", "PlaySound", true,
                "Play 'item_put' after a stash, 'gui_click' when nothing moved.");
            IncludeBagContents = Config.Bind("Behaviour", "IncludeBagContents", true,
                "Also stash stackables stored inside bags in the backpack (e.g. the farming bag). Items are never put into bags inside containers.");
            ShowBubbles = Config.Bind("Behaviour", "ShowBubbles", true,
                "After a stash, show a bubble above each receiving container listing what went in (item icon + count). Requires a game restart to change.");
            BubbleSeconds = Config.Bind("Behaviour", "BubbleSeconds", 2f,
                "How long the stash bubble stays visible (real seconds, unaffected by pause). Values <= 0 fall back to 2. " + LiveNote);
            BubbleScale = Config.Bind("Behaviour", "BubbleScale", 1f,
                "Size of each stash bubble cell relative to the workstation craft-hint bubble (1 = identical). Values <= 0 fall back to 1. " + LiveNote);
            MaxBubbleItems = Config.Bind("Behaviour", "MaxBubbleItems", 4,
                "Maximum number of item cells per container bubble (largest deliveries first). Values < 1 are treated as 1. " + LiveNote);
            ShowBubbleCount = Config.Bind("Behaviour", "ShowBubbleCount", false,
                "Show the item count on each bubble cell. Off by default because the scaled font looks rough. " + LiveNote);
            BubbleColumns = Config.Bind("Behaviour", "BubbleColumns", 0,
                "Cells per row in a container bubble. 0 = auto: 2 columns when BubbleScale <= 0.5, else 1. " + LiveNote);
            BubbleSpacing = Config.Bind("Behaviour", "BubbleSpacing", -1f,
                "Gap between cells in unscaled pixels (it is multiplied by BubbleScale). Negative = auto: the bubble's own layout spacing, or 2 if unknown. " + LiveNote);

            FallbackRadius = Config.Bind("Discovery", "FallbackRadius", 12f,
                "World units. Used to find containers when the player is not inside a world zone.");
            IncludeConveyorChests = Config.Bind("Discovery", "IncludeConveyorChests", false,
                "Treat conveyor chests (conveyorType Chest/ChestOut) as stash targets.");

            if (ShowBubbles.Value)
            {
                string err;
                if (StashBubbles.TryPatch(PluginGuid, out err)) Log.LogInfo("Bubble patch applied (Wgo.GetWidgetData postfix)");
                else Log.LogWarning("Bubble patch failed, bubbles disabled: " + err);
            }

            Log.LogInfo("QuickStash loaded  (stash: " + StashKey.Value + ", diagnostics: " + DiagnosticsKey.Value + ", dryRun: " + DryRun.Value
                        + ", bubbles: " + (StashBubbles.IsPatched ? BubbleSeconds.Value + "s" : "off") + ")");
        }

        private void Update()
        {
            if (MainGame.PlayerData == null) return;

            if (StashBubbles.IsPatched) StashBubbles.Tick();

            bool diagnostics = DiagnosticsKey.Value.IsDown();
            bool stash = StashKey.Value.IsDown();
            if (!diagnostics && !stash) return;

            ReloadConfigThrottled();   // BepInEx 5 does not watch the file; pick up edits made while the game runs

            if (diagnostics)
            {
                try { Log.LogInfo(Diagnostics.WriteDump()); }
                catch (Exception e) { Log.LogError("Dump failed: " + e); }
            }

            if (stash)
            {
                try { TryStash(); }
                catch (Exception e) { Log.LogError("Stash failed: " + e); }
            }
        }

        private void ReloadConfigThrottled()
        {
            if (Time.unscaledTime - lastConfigReload < 2f) return;
            lastConfigReload = Time.unscaledTime;
            try { Config.Reload(); }
            catch (Exception e) { Log.LogDebug("Config reload failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ stash

        private void TryStash()
        {
            string reason;
            if (!CanStash(out reason))
            {
                Log.LogDebug("Stash skipped: " + reason);
                return;
            }

            var pd = MainGame.PlayerData;
            string source;
            var targets = ContainerFinder.FindTargets(pd, FallbackRadius.Value, IncludeConveyorChests.Value, out source);
            var result = Stasher.Run(pd.inventory, targets, DryRun.Value, IncludeBagContents.Value);
            Log.LogInfo(Stasher.Describe(result, source));

            if (result.DryRun) return;
            if (result.ConservationChecked && !result.ConservationOk)
                Log.LogWarning("Item totals changed by " + (result.TotalAfter - result.TotalBefore) + " during stash; please report this with the dump.");

            AfterStash(pd, result);
        }

        private static void AfterStash(PlayerData pd, StashResult result)
        {
            if (result.TotalItems > 0)
            {
                // vanilla does this after UI item moves (InventoryUIItemMoveOpHandler.TryMoveItem)
                try { if (pd.HasInteractingItem) pd.UpdateInteractingItem(); }
                catch (Exception e) { Log.LogWarning("UpdateInteractingItem failed: " + e.Message); }

                if (PlaySound.Value) PlaySoundSafe("item_put");
                ShowNotification("Stashed " + result.TotalItems + " item" + (result.TotalItems == 1 ? "" : "s"));
                if (StashBubbles.IsPatched) ShowBubbles_(result);
            }
            else if (PlaySound.Value)
            {
                PlaySoundSafe("gui_click");
            }
        }

        /// <summary>One bubble per receiving container, listing the moved item ids and counts.</summary>
        private static void ShowBubbles_(StashResult result)
        {
            try
            {
                var perContainer = new Dictionary<string, List<KeyValuePair<string, int>>>();
                var containers = new Dictionary<string, WgoData>();
                foreach (var m in result.Moves)
                {
                    if (m.Container == null || m.Count <= 0) continue;
                    List<KeyValuePair<string, int>> list;
                    if (!perContainer.TryGetValue(m.ContainerUid, out list))
                    {
                        perContainer[m.ContainerUid] = list = new List<KeyValuePair<string, int>>();
                        containers[m.ContainerUid] = m.Container;
                    }
                    list.Add(new KeyValuePair<string, int>(m.ItemId, m.Count));
                }
                foreach (var kv in perContainer)
                    StashBubbles.Show(containers[kv.Key], kv.Value, BubbleSeconds.Value > 0f ? BubbleSeconds.Value : 2f, Math.Max(1, MaxBubbleItems.Value));
            }
            catch (Exception e)
            {
                Log.LogDebug("bubbles failed: " + e.Message);
            }
        }

        private static void PlaySoundSafe(string id)
        {
            try { LazyAudio.PlayAndForget(id); }
            catch (Exception e) { Log.LogDebug("sound '" + id + "' failed: " + e.Message); }
        }

        private static FieldInfo currentLangField;

        /// <summary>
        /// UINotificator.ShowSimpleTextNotification(locale) shows LLBase.L(locale); LLBase.L returns the key itself when
        /// unknown, so we inject our text into the loaded language table (LLBase.currentLang.dictionary + idsToMetaInfo).
        /// </summary>
        private static void ShowNotification(string text)
        {
            const string key = "quickstash_stashed";
            try
            {
                if (currentLangField == null)
                    currentLangField = typeof(LLBase).GetField("currentLang", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var lang = currentLangField != null ? currentLangField.GetValue(null) as LLBase : null;
                if (lang == null || lang.dictionary == null || lang.idsToMetaInfo == null) return;

                lang.dictionary[key] = text;
                if (!lang.idsToMetaInfo.ContainsKey(key)) lang.idsToMetaInfo[key] = new NestedLocalesMetaInfo();

                LazySingleton<UINotificator>.Instance.ShowSimpleTextNotification(key);
            }
            catch (Exception e)
            {
                Log.LogDebug("notification failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ gates

        /// <summary>All gates must pass. <paramref name="reason"/> names the first failing one.</summary>
        internal static bool CanStash(out string reason)
        {
            if (MainGame.Instance == null || MainGame.PlayerData == null) { reason = "game not loaded"; return false; }

            var pc = MainGame.PlayerController;
            if (pc == null) { reason = "no PlayerController"; return false; }
            if (!pc.IsControlsEnabled) { reason = "PlayerController.IsControlsEnabled == false"; return false; }
            if (MainGame.IsGamePaused) { reason = "game paused"; return false; }
            if (!LazyInput.IsInputActive()) { reason = "LazyInput.IsInputActive() == false"; return false; }

            string window;
            if (IsAnyWindowOpen(out window)) { reason = "window open: " + window; return false; }

            if (IsCoopClient())
            {
                Log.LogWarning("QuickStash only works for the host in co-op (inventory changes are not replicated from clients).");
                reason = "co-op client";
                return false;
            }

            reason = null;
            return true;
        }

        private static readonly Dictionary<Type, PropertyInfo> isShownCache = new Dictionary<Type, PropertyInfo>();

        /// <summary>LazyWindow&lt;T&gt;.IsShown lives on a generic base, so it is read via reflection per concrete type.</summary>
        internal static bool IsWindowShown(LazyWidgetBase w)
        {
            if (w == null) return false;
            Type t = w.GetType();
            PropertyInfo p;
            if (!isShownCache.TryGetValue(t, out p))
            {
                p = t.GetProperty("IsShown", BindingFlags.Instance | BindingFlags.Public);
                if (p != null && p.PropertyType != typeof(bool)) p = null;
                isShownCache[t] = p;
            }
            return p != null && (bool)p.GetValue(w, null);
        }

        internal static bool IsAnyWindowOpen(out string name)
        {
            name = null;
            try
            {
                var active = LazyWindowsStackController.ActiveWindow;
                if (active != null) { name = active.GetType().Name + " (ActiveWindow)"; return true; }
                if (LazyWindowsStackController.HasAnyModalWindowOpened) { name = "modal window"; return true; }
                if (!LazyUI.IsInitialized) return false;

                foreach (var w in LazyUI.GetAllWindows())
                {
                    if (w == null) continue;
                    if (LazyWindowsStackController.IsWindowOpened(w)) { name = w.GetType().Name + " (in stack)"; return true; }
                    if (IsWindowShown(w)) { name = w.GetType().Name + " (IsShown)"; return true; }
                }
                return false;
            }
            catch (Exception e)
            {
                name = "window check failed: " + e.Message;
                return true; // fail closed
            }
        }

        internal static bool IsCoopClient()
        {
            try
            {
                if (!LazyNetwork.IsInitialized) return false;
                var nm = LazyNetwork.NetworkManager;
                return nm != null && nm.IsCoopGame && !nm.IsHost;
            }
            catch (Exception e)
            {
                Log.LogDebug("co-op check failed: " + e.Message);
                return false;
            }
        }
    }
}
