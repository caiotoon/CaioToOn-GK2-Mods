using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace QuickStash
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.caiotoon.gk2.quickstash";
        public const string PluginName = "QuickStash";
        public const string PluginVersion = "0.8.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyboardShortcut> StashKey;
        internal static ConfigEntry<bool> PlaySound;
        internal static ConfigEntry<bool> IncludeBagContents;
        internal static ConfigEntry<bool> ShowBubbles;
        internal static ConfigEntry<float> BubbleSeconds;
        internal static ConfigEntry<float> BubbleScale;
        internal static ConfigEntry<int> MaxBubbleItems;
        internal static ConfigEntry<bool> ShowBubbleCount;
        internal static ConfigEntry<int> BubbleColumns;
        internal static ConfigEntry<float> BubbleSpacing;
        internal static ConfigEntry<float> FallbackRadius;
        internal static ConfigEntry<bool> IncludeConveyorChests;

        // BepInEx 5 never re-reads its file, so its timestamp is polled once a second
        private DateTime configStamp;
        private float nextConfigCheck;

        private void Awake()
        {
            Log = Logger;

            StashKey = Config.Bind("Keys", "StashKey", new KeyboardShortcut(KeyCode.G),
                "Moves every stackable backpack item that already exists in a nearby container into that container (vanilla 'move all similar' per container).");

            PlaySound = Config.Bind("Behaviour", "PlaySound", true,
                "Play 'item_put' after a stash, 'gui_click' when nothing moved.");
            IncludeBagContents = Config.Bind("Behaviour", "IncludeBagContents", true,
                "Also stash stackables stored inside bags in the backpack (e.g. the farming bag). Items are never put into bags inside containers.");
            ShowBubbles = Config.Bind("Behaviour", "ShowBubbles", true,
                "After a stash, show a bubble above each receiving container listing what went in (item icon + count).");
            BubbleSeconds = Config.Bind("Behaviour", "BubbleSeconds", 2f,
                "How long the stash bubble stays visible (real seconds, unaffected by pause). Values <= 0 fall back to 2.");
            BubbleScale = Config.Bind("Behaviour", "BubbleScale", 1f,
                "Size of each stash bubble cell relative to the workstation craft-hint bubble (1 = identical). Values <= 0 fall back to 1.");
            MaxBubbleItems = Config.Bind("Behaviour", "MaxBubbleItems", 4,
                "Maximum number of item cells per container bubble (largest deliveries first). Values < 1 are treated as 1.");
            ShowBubbleCount = Config.Bind("Behaviour", "ShowBubbleCount", false,
                "Show the item count on each bubble cell. Off by default because the scaled font looks rough.");
            BubbleColumns = Config.Bind("Behaviour", "BubbleColumns", 0,
                "Cells per row in a container bubble. 0 or less = auto: 2 columns when BubbleScale <= 0.5, else 1.");
            BubbleSpacing = Config.Bind("Behaviour", "BubbleSpacing", -1f,
                "Gap between cells in unscaled pixels (it is multiplied by BubbleScale). Negative = auto: the bubble's own layout spacing, or 2 if unknown.");

            FallbackRadius = Config.Bind("Discovery", "FallbackRadius", 12f,
                "World units. Used to find containers when the player is not inside a container zone (no zone, or a zone of type SimpleNotContainer).");
            IncludeConveyorChests = Config.Bind("Discovery", "IncludeConveyorChests", false,
                "Treat conveyor chests (conveyorType Chest/ChestOut) as stash targets.");

            Log.LogInfo("QuickStash loaded (stash key: " + StashKey.Value + ")");
            Harmony.CreateAndPatchAll(typeof(StashBubbles), PluginGuid);
        }

        private void Update()
        {
            PollConfigReload();
            StashBubbles.Tick();

            if (!IsShortcutDown(StashKey.Value)) return;

            try { TryStash(); }
            catch (Exception e) { Log.LogError("Stash failed: " + e); }
        }

        /// <summary>
        /// BepInEx's KeyboardShortcut.IsDown() returns false while any unrelated key is held (e.g. W while walking),
        /// so the shortcut is tested manually: main key just pressed and every modifier held.
        /// </summary>
        private static bool IsShortcutDown(KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        private void PollConfigReload()
        {
            if (Time.unscaledTime < nextConfigCheck) return;
            nextConfigCheck = Time.unscaledTime + 1f;

            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(Config.ConfigFilePath);
                if (stamp == configStamp) return;
                Config.Reload();
                configStamp = stamp;
            }
            catch (IOException) { }   // file mid-write: the stamp is not stored, so the next poll retries
        }

        private void TryStash()
        {
            if (!CanStash()) return;
            if (LazyNetwork.IsInitialized && LazyNetwork.NetworkManager.IsCoopGame && !LazyNetwork.NetworkManager.IsHost)
            {
                Log.LogWarning("QuickStash only works for the host in co-op (inventory changes are not replicated from clients).");
                return;
            }

            PlayerData pd = MainGame.PlayerData;
            var targets = ContainerFinder.FindTargets(pd, FallbackRadius.Value, IncludeConveyorChests.Value);
            StashResult result = Stasher.Execute(pd.inventory, targets, IncludeBagContents.Value);
            if (result.AbortReason != null)
            {
                Log.LogWarning("Stash aborted: " + result.AbortReason);
                return;
            }
            Log.LogInfo(Stasher.Describe(result));

            ShowFeedback(pd, result);
        }

        private static void ShowFeedback(PlayerData pd, StashResult result)
        {
            int moved = result.TotalItems;
            if (PlaySound.Value) LazyAudio.PlayAndForget(moved > 0 ? "item_put" : "gui_click");
            if (moved == 0) return;

            // vanilla does this after UI item moves (InventoryUIItemMoveOpHandler.TryMoveItem)
            if (pd.HasInteractingItem) pd.UpdateInteractingItem();
            // LLBase.L returns the key itself when it is unknown, so plain text works as the "locale"
            LazySingleton<UINotificator>.Instance.ShowSimpleTextNotification("Stashed " + moved + " item" + (moved == 1 ? "" : "s"));

            if (!ShowBubbles.Value) return;
            try
            {
                foreach (Delivery d in result.Deliveries)
                    StashBubbles.Show(d.Container, d.Items, BubbleSeconds.Value, MaxBubbleItems.Value);
            }
            catch (Exception e) { Log.LogWarning("Stash bubbles failed: " + e.Message); }
        }

        /// <summary>Game loaded, controls enabled, not paused, input active, no window open.</summary>
        private static bool CanStash()
        {
            if (!IsGameLoaded()) return false;
            if (!MainGame.PlayerController.IsControlsEnabled) return false;
            if (MainGame.IsGamePaused) return false;
            if (!LazyInput.IsInputActive()) return false;
            // every shown LazyWindow is on the stack: only LazyWindow.ShowWindow / HideWindow change both
            return LazyWindowsStackController.ActiveWindow == null && !LazyWindowsStackController.HasAnyModalWindowOpened;
        }

        /// <summary>MainGame.PlayerData survives GoToMainMenu; the player's current scene does not.</summary>
        private static bool IsGameLoaded()
        {
            if (MainGame.Instance == null || MainGame.PlayerData == null) return false;
            PlayerController pc = MainGame.PlayerController;
            return pc != null && pc.TryGetCurrentGameScene(out _);
        }
    }
}
