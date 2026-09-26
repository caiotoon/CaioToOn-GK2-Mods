using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace QuickStash
{
    /// <summary>
    /// "What just went in" bubbles. A registry of recent stash deliveries per container (keyed by WgoData.UniqueId.Guid,
    /// expiry on <see cref="Time.unscaledTime"/> so pausing does not stretch them) plus a Harmony postfix on
    /// <c>Wgo.GetWidgetData()</c> that appends our widgets. The game's own bubble pipeline
    /// (Wgo.DrawWidgets -> WgoBubbleDisplayHandler -> UIObjectBubbleManager) rebuilds the bubble when the widget list
    /// changes and hides it when the list becomes empty.
    ///
    /// Widget paths, decided on the first stash in-game: <see cref="Mode.Template"/> = our <c>StashBubbleWidget</c>
    /// (one grid per container of craft-hint-framed item cells); fallback <see cref="Mode.NeedItems"/> = <c>NeedItemsWidgetData</c>
    /// (3 cells per widget, "n/n" counter, unframed); last resort <see cref="Mode.HintRows"/> = <c>UIInteractionHintWidgetData</c>.
    /// </summary>
    internal static class StashBubbles
    {
        internal enum Mode { Unknown, Template, NeedItems, HintRows }

        internal sealed class Entry
        {
            public float ExpiresAt;
            public readonly List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>();
            public List<LazyWidgetDataBase> Widgets;
        }

        private static readonly Dictionary<Guid, Entry> entries = new Dictionary<Guid, Entry>();
        private static Harmony harmony;

        public static bool IsPatched => harmony != null;
        public static int LiveCount => entries.Count;
        public static Mode ActiveMode { get; private set; } = Mode.Unknown;

        // ------------------------------------------------------------------ patching

        public static bool TryPatch(string harmonyId, out string error)
        {
            error = null;
            if (harmony != null) return true;
            try
            {
                MethodInfo original = typeof(Wgo).GetMethod("GetWidgetData", BindingFlags.Instance | BindingFlags.Public);
                if (original == null) { error = "Wgo.GetWidgetData not found"; return false; }
                MethodInfo postfix = typeof(StashBubbles).GetMethod(nameof(GetWidgetDataPostfix), BindingFlags.Static | BindingFlags.NonPublic);

                var h = new Harmony(harmonyId);
                h.Patch(original, postfix: new HarmonyMethod(postfix));
                harmony = h;
                return true;
            }
            catch (Exception e)
            {
                error = e.ToString();
                return false;
            }
        }

        // ------------------------------------------------------------------ registry

        /// <summary>Records what <paramref name="wgo"/> received (aggregated per item id, largest count first, capped) and redraws its bubble.</summary>
        public static void Show(WgoData wgo, IEnumerable<KeyValuePair<string, int>> moved, float seconds, int maxItems)
        {
            if (!IsPatched || wgo == null || wgo.UniqueId == null || moved == null) return;

            if (seconds <= 0f) seconds = 2f;
            var entry = new Entry { ExpiresAt = Time.unscaledTime + seconds };
            var index = new Dictionary<string, int>();
            foreach (var kv in moved)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0) continue;
                int i;
                if (index.TryGetValue(kv.Key, out i))
                    entry.Items[i] = new KeyValuePair<string, int>(kv.Key, entry.Items[i].Value + kv.Value);
                else
                {
                    index[kv.Key] = entry.Items.Count;
                    entry.Items.Add(kv);
                }
            }
            if (entry.Items.Count == 0) return;

            // largest deliveries first, stable for equal counts
            var ordered = new List<KeyValuePair<string, int>>(entry.Items);
            for (int a = 1; a < ordered.Count; a++)
                for (int b = a; b > 0 && ordered[b].Value > ordered[b - 1].Value; b--)
                {
                    var t = ordered[b]; ordered[b] = ordered[b - 1]; ordered[b - 1] = t;
                }
            if (maxItems < 1) maxItems = 1;
            if (ordered.Count > maxItems) ordered.RemoveRange(maxItems, ordered.Count - maxItems);
            entry.Items.Clear();
            entry.Items.AddRange(ordered);

            EnsureMode();
            if (ActiveMode == Mode.Unknown) return;

            try { entry.Widgets = BuildWidgets(entry, wgo); }
            catch (Exception e) { Plugin.Log.LogWarning("bubble widgets could not be built: " + e.Message); return; }
            if (entry.Widgets.Count == 0) return;

            entries[wgo.UniqueId.Guid] = entry;   // a second stash while live replaces counts and expiry
            Redraw(wgo.UniqueId);
        }

        /// <summary>Call every frame. Removes expired entries and redraws their bubbles so our widgets disappear.</summary>
        public static void Tick()
        {
            if (entries.Count == 0) return;
            float now = Time.unscaledTime;
            List<Guid> expired = null;
            foreach (var kv in entries)
            {
                if (kv.Value.ExpiresAt <= now)
                    (expired ?? (expired = new List<Guid>())).Add(kv.Key);
            }
            if (expired == null) return;
            foreach (var g in expired)
            {
                entries.Remove(g);
                Redraw(new SGuid(g));
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
            catch (Exception e)
            {
                Plugin.Log.LogDebug("bubble redraw failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ harmony postfix

        // ReSharper disable InconsistentNaming
        private static void GetWidgetDataPostfix(Wgo __instance, ref List<LazyWidgetDataBase> __result)
        {
            try
            {
                if (entries.Count == 0 || __result == null || __instance == null) return;
                WgoData data = __instance.Data;
                if (data == null || data.IsHidden || data.UniqueId == null) return;   // original returned early with an empty list

                Entry entry;
                if (!entries.TryGetValue(data.UniqueId.Guid, out entry)) return;
                if (entry.ExpiresAt <= Time.unscaledTime || entry.Widgets == null) return;   // Tick() will remove it

                __result.AddRange(entry.Widgets);
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug("bubble postfix failed: " + e.Message);
            }
        }
        // ReSharper restore InconsistentNaming

        // ------------------------------------------------------------------ widgets

        /// <summary>
        /// Decides the widget path once, in-game (never at plugin load: LazyWidgetPrefabContainer is a LazySingleton whose
        /// table is filled by its own Init(); touching it early would create an empty instance and break every game bubble).
        /// </summary>
        private static void EnsureMode()
        {
            if (ActiveMode != Mode.Unknown) return;
            if (UIObjectBubbleManager.Instance == null) return;   // UI not up yet; try again next time

            if (StashBubbleTemplate.EnsureBuilt())
            {
                ActiveMode = Mode.Template;
                Plugin.Log.LogInfo("Bubble widget path: Template (craft-hint frame + item cell)");
                return;
            }
            if (StashBubbleTemplate.Status == "not built yet") return;   // manager exists but template not attempted (should not happen); retry later

            try
            {
                LazyWidgetPrefabContainer.GetPrefabFromDataObject(new NeedItemsWidgetData(new List<NeedItemData>(), new MultiInventory(), isActive: true));
                ActiveMode = Mode.NeedItems;
            }
            catch (Exception e)
            {
                if (e.Message != null && e.Message.StartsWith("Cannot find prefab"))
                    ActiveMode = Mode.HintRows;
                else
                {
                    Plugin.Log.LogDebug("bubble prefab probe inconclusive: " + e.Message);
                    return;
                }
            }
            Plugin.Log.LogInfo("Bubble widget path: " + ActiveMode + " (fallback; template " + StashBubbleTemplate.Status + ")");
        }

        private static List<LazyWidgetDataBase> BuildWidgets(Entry entry, WgoData wgo)
        {
            var list = new List<LazyWidgetDataBase>();
            switch (ActiveMode)
            {
                case Mode.Template:
                {
                    var cells = new List<StashBubbleCell>();
                    foreach (var kv in entry.Items)
                        cells.Add(new StashBubbleCell(kv.Key, kv.Value, StarQuality(kv.Key)));
                    list.Add(new StashBubbleWidgetData(cells));   // one grid widget per container
                    break;
                }

                case Mode.NeedItems:
                    for (int i = 0; i < entry.Items.Count; i += 3)
                    {
                        var needs = new List<NeedItemData>();
                        // Temp inventory holding exactly the moved items, so UIItemCell renders "<moved>/<moved>" in the normal (satisfied) style.
                        var have = new Inventory("inventory", 1, autoExpand: true);
                        for (int j = i; j < Math.Min(i + 3, entry.Items.Count); j++)
                        {
                            needs.Add(new NeedItemData(entry.Items[j].Key, entry.Items[j].Value));
                            have.AddItemToInventory(new Item(entry.Items[j].Key, entry.Items[j].Value));
                        }
                        list.Add(new NeedItemsWidgetData(needs, new MultiInventory(have), isActive: true, wgo));
                    }
                    break;

                case Mode.HintRows:
                    for (int i = 0; i < entry.Items.Count; i += 2)
                    {
                        var rows = new List<UIInteractionHintRowWidgetData>();
                        for (int j = i; j < Math.Min(i + 2, entry.Items.Count); j++)
                            rows.Add(new UIInteractionHintRowWidgetData(new InteractionInfo("+" + entry.Items[j].Value, IconId(entry.Items[j].Key))));
                        list.Add(new UIInteractionHintWidgetData(rows));
                    }
                    break;
            }
            return list;
        }

        /// <summary>Star quality of a star-type item (drawn as the item_star_N badge), else -1.</summary>
        private static int StarQuality(string itemId)
        {
            try
            {
                var def = GameBalance.Me.GetDataOrNull<ItemDef>(itemId);
                if (def != null && def.qualityType == ItemDef.QualityType.Star) return def.quality;
            }
            catch { }
            return -1;
        }

        /// <summary>Sprite name in EasySpritesCollection, as UIItemCell uses it (ItemDef.iconId); falls back to the item id.</summary>
        public static string IconId(string itemId)
        {
            try
            {
                var def = GameBalance.Me.GetDataOrNull<ItemDef>(itemId);
                if (def != null && !string.IsNullOrEmpty(def.iconId)) return def.iconId;
            }
            catch { }
            return itemId;
        }

        /// <summary>For the diagnostics dump: whether the inventory-cell sprite exists.</summary>
        public static string DescribeIcon(string itemId)
        {
            string icon = IconId(itemId);
            string has;
            try { has = LazySingletonSO<EasySpritesCollection>.Instance.HasSprite(icon) ? "yes" : "NO"; }
            catch (Exception e) { has = "? (" + e.GetType().Name + ")"; }
            return "iconId=" + icon.PadRight(24) + " easySprite=" + has;
        }
    }
}
