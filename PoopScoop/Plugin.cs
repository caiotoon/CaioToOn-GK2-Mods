using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace PoopScoop
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.caiotoon.gk2.poopscoop";
        public const string PluginName = "PoopScoop";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("PoopScoop loaded");
            var harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(Scoop));
            try
            {
                harmony.PatchAll(typeof(ScoopGamepad));
            }
            catch (Exception e) { Log.LogWarning("Controller support is off, right-click still works: " + e.Message); }
        }
    }
}
