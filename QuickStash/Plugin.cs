using System;
using System.IO;
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
        public const string PluginVersion = "0.10.0";

        private const string GamepadStashHelp =
            "Controller chord: names of the game's own actions joined with '+'. The last one must be pressed, the ones before it " +
            "must be held. A single name is allowed. None or empty disables. Default: hold R2 and press L3 (left stick click).\n" +
            "Buttons:\n" +
            "  R2 / RT  right trigger      = RightTrigger\n" +
            "  L2 / LT  left trigger       = LeftTrigger\n" +
            "  R1 / RB  right bumper       = NextTab\n" +
            "  L1 / LB  left bumper        = PrevTab\n" +
            "  R3       right stick click  = RightStick\n" +
            "  L3       left stick click   = LeftStick\n" +
            "  D-pad                       = DpadUp, DpadDown, DpadLeft, DpadRight\n" +
            "Other buttons are named by the game action they perform, for example Interaction, Action, Inventory.\n" +
            "Held buttons still do their normal game action, so avoid LeftTrigger (L2) as a held button: it is Attack Focus.\n" +
            "Example: RightTrigger+LeftStick = hold R2, press L3.";

        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyboardShortcut> StashKey;
        internal static ConfigEntry<string> GamepadStash;
        internal static ConfigEntry<bool> PlaySound;
        internal static ConfigEntry<bool> IncludeBagContents;
        internal static ConfigEntry<bool> KeepHotbarItems;
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
            GamepadStash = Config.Bind("Keys", "GamepadStash", "RightTrigger+LeftStick", GamepadStashHelp);

            PlaySound = Config.Bind("Behaviour", "PlaySound", true,
                "Play 'item_put' after a stash, 'gui_click' when nothing moved.");
            IncludeBagContents = Config.Bind("Behaviour", "IncludeBagContents", true,
                "Also stash stackables stored inside bags in the backpack (e.g. the farming bag). Items are never put into bags inside containers.");
            KeepHotbarItems = Config.Bind("Behaviour", "KeepHotbarItems", true,
                "Items pinned to the hotbar stay in your backpack. Turn off to stash them like any other item.");
            ShowBubbles = Config.Bind("Behaviour", "ShowBubbles", true,
                "After a stash, show a bubble above each receiving container listing what went in (item icon + count).");
            BubbleSeconds = Config.Bind("Behaviour", "BubbleSeconds", 2f,
                "How long the stash bubble stays visible (real seconds, unaffected by pause). Values <= 0 fall back to 2.");
            BubbleScale = Config.Bind("Behaviour", "BubbleScale", 0.6f,
                "Size of each stash bubble cell relative to the workstation craft-hint bubble (1 = identical). Values <= 0 fall back to 1.");
            MaxBubbleItems = Config.Bind("Behaviour", "MaxBubbleItems", 4,
                "Maximum number of item cells per container bubble (largest deliveries first). Values < 1 are treated as 1.");
            ShowBubbleCount = Config.Bind("Behaviour", "ShowBubbleCount", false,
                "Show the item count on each bubble cell. Off by default because the scaled font looks rough.");
            BubbleColumns = Config.Bind("Behaviour", "BubbleColumns", 2,
                "Cells per row in a container bubble. 0 or less = auto: 2 columns when BubbleScale <= 0.5, else 1.");
            BubbleSpacing = Config.Bind("Behaviour", "BubbleSpacing", 4f,
                "Gap between cells in unscaled pixels (it is multiplied by BubbleScale). Negative = auto: the bubble's own layout spacing, or 2 if unknown.");

            FallbackRadius = Config.Bind("Discovery", "FallbackRadius", 12f,
                "World units. Used to find containers when the player is not inside a container zone (no zone, or a zone of type SimpleNotContainer).");
            IncludeConveyorChests = Config.Bind("Discovery", "IncludeConveyorChests", false,
                "Treat conveyor chests (conveyorType Chest/ChestOut) as stash targets.");

            Log.LogInfo("QuickStash loaded (stash key: " + StashKey.Value + ", controller: " + GamepadStash.Value + ")");
            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(StashBubbles));
            harmony.PatchAll(typeof(StashInput));
            Stasher.PatchKeepFilter(harmony);
        }

        private void Update()
        {
            PollConfigReload();
            StashBubbles.Tick();
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

        internal static void Stash()
        {
            PlayerData pd = MainGame.PlayerData;
            var targets = ContainerFinder.FindTargets(pd, FallbackRadius.Value, IncludeConveyorChests.Value);
            StashResult result = Stasher.Execute(pd.inventory, targets, IncludeBagContents.Value, KeepHotbarItems.Value ? pd.pinnedItems : null);
            if (result.AbortReason != null)
            {
                Log.LogWarning("Stash aborted: " + result.AbortReason);
                return;
            }
            Log.LogInfo(Stasher.Describe(result));

            ShowFeedback(result);
        }

        private static void ShowFeedback(StashResult result)
        {
            int moved = result.TotalItems;
            if (PlaySound.Value) LazyAudio.PlayAndForget(moved > 0 ? "item_put" : "gui_click");
            if (moved == 0) return;

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

        /// <summary>
        /// Called from PlayerInputHandler.UpdateInput, which the game only runs with controls enabled
        /// (SSM.CustomUpdate, FreePlayerState.IsActive). The gates below are not implied by that.
        /// </summary>
        internal static bool CanStash()
        {
            if (!IsGameLoaded()) return false;               // the main menu never takes control from the player
            if (MainGame.IsGamePaused) return false;         // fishing/credits windows skip the unpause, so a pause can outlive the taken control
            if (!LazyInput.IsInputActive()) return false;    // while off, LazyInput keeps reporting the last presses
            // only modal windows take control; every shown LazyWindow (modal or not) is on the stack
            if (LazyWindowsStackController.ActiveWindow != null) return false;

            bool coopClient = LazyNetwork.IsInitialized && LazyNetwork.NetworkManager.IsCoopGame && !LazyNetwork.NetworkManager.IsHost;
            if (coopClient) Log.LogWarning("QuickStash only works for the host in co-op (inventory changes are not replicated from clients).");
            return !coopClient;
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
