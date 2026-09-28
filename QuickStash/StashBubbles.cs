using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace QuickStash
{
    /// <summary>
    /// "What just went in" bubbles. A registry of recent stash deliveries per container (expiry on
    /// <see cref="Time.unscaledTime"/> so pausing does not stretch them) plus a Harmony postfix on
    /// <c>Wgo.GetWidgetData()</c> that appends our widget. The game's own bubble pipeline
    /// (Wgo.DrawWidgets -> WgoBubbleDisplayHandler -> UIObjectBubbleManager) rebuilds the bubble when the widget list
    /// changes and hides it when the list becomes empty.
    /// </summary>
    internal static class StashBubbles
    {
        // SGuid equality and hash use its id string
        private static readonly Dictionary<SGuid, StashBubbleWidgetData> entries = new Dictionary<SGuid, StashBubbleWidgetData>();

        /// <summary>Records what <paramref name="wgo"/> received (largest count first, capped) and redraws its bubble.</summary>
        public static void Show(WgoData wgo, Dictionary<string, int> moved, float seconds, int maxItems)
        {
            if (!StashBubbleTemplate.EnsureBuilt()) return;
            if (seconds <= 0f) seconds = 2f;
            if (maxItems < 1) maxItems = 1;

            var largestFirst = moved.OrderByDescending(kv => kv.Value).Take(maxItems);
            // a second stash while live replaces counts and expiry
            entries[wgo.UniqueId] = new StashBubbleWidgetData
            {
                Items = largestFirst.Select(kv => new Item(kv.Key, kv.Value)).ToList(),
                ExpiresAt = Time.unscaledTime + seconds
            };
            Redraw(wgo.UniqueId);
        }

        /// <summary>Call every frame. Removes expired entries and redraws their bubbles so our widgets disappear.</summary>
        public static void Tick()
        {
            if (entries.Count == 0) return;

            var expired = entries.Where(kv => kv.Value.ExpiresAt <= Time.unscaledTime).Select(kv => kv.Key).ToList();
            foreach (SGuid uid in expired)
            {
                entries.Remove(uid);
                Redraw(uid);
            }
        }

        /// <summary>Asks the view to re-request its bubble (queued; the manager flushes it on its next update).</summary>
        private static void Redraw(SGuid uid)
        {
            try
            {
                Wgo view = GameScene.GetWgoViewGlobal(uid);
                if (view != null && !view.IsDespawning) view.DrawWidgets();   // DrawWidgets checks isVisible itself
            }
            catch { }
        }

        // ReSharper disable InconsistentNaming
        /// <summary>Runs inside the game's bubble pipeline: must never throw.</summary>
        [HarmonyPostfix, HarmonyPatch(typeof(Wgo), nameof(Wgo.GetWidgetData))]
        private static void GetWidgetDataPostfix(Wgo __instance, List<LazyWidgetDataBase> __result)
        {
            try
            {
                if (entries.Count == 0) return;
                WgoData data = __instance.Data;
                if (data == null || data.IsHidden) return;   // original returned early with an empty list

                if (entries.TryGetValue(data.UniqueId, out StashBubbleWidgetData widget)) __result.Add(widget);
            }
            catch { }
        }
        // ReSharper restore InconsistentNaming
    }
}
