using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace QuickStash
{
    /// <summary>What one container received: item id -> moved count.</summary>
    internal sealed class Delivery
    {
        public WgoData Container;
        public readonly Dictionary<string, int> Items = new Dictionary<string, int>();
    }

    internal sealed class StashResult
    {
        public readonly List<Delivery> Deliveries = new List<Delivery>();

        /// <summary>Non-null when the run refused to start (nothing was moved).</summary>
        public string AbortReason;

        public int TotalItems => Deliveries.Sum(d => d.Items.Values.Sum());
    }

    /// <summary>
    /// The vanilla chest button, applied to every target:
    /// <c>Inventory.TakeAllItemsExistingInMeFromOtherInventory(player.inventory, ignoreMyBags: true, ignoreOtherBags)</c>
    /// walks the backpack backwards; a top-level stack with <c>stackCount &gt; 1</c> whose id exists top-level in the container is
    /// moved (capacity and white/black lists enforced inside <c>Item.CanAddItemCountToInventory</c>); a bag met on the way has its
    /// contents walked with the same rule when <c>ignoreOtherBags</c> is false. Items are never put into bags inside the container.
    /// </summary>
    internal static class Stasher
    {
        // Item ids the vanilla call must leave in the backpack; only set while Execute runs, so the chest window is unaffected
        private static string[] keepIds;

        /// <summary>
        /// Hides <see cref="keepIds"/> from the container's id set (private <c>Item.CollectUniqueItemIds</c>, Item.cs ~1061), which the
        /// vanilla move loop checks before moving a stack. If a game update removes the method, the patch is skipped and every id moves.
        /// </summary>
        public static void PatchKeepFilter(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(Item), "CollectUniqueItemIds", new[] { typeof(bool) });
            if (target == null || target.ReturnType != typeof(HashSet<string>))
            {
                Plugin.Log.LogWarning("Item.CollectUniqueItemIds not found: hotbar items will not be kept out of the stash.");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(Stasher), nameof(HideKeptIds)));
        }

        private static void HideKeptIds(HashSet<string> __result)
        {
            if (keepIds == null) return;
            foreach (string id in keepIds) __result.Remove(id);
        }

        /// <summary>One vanilla call per target, in the given (distance) order. Deliveries are derived from a backpack id->count snapshot around each call.</summary>
        public static StashResult Execute(Inventory backpack, List<WgoData> targets, bool includeBagContents, string[] keep)
        {
            var result = new StashResult();

            // Vanilla Item.TakeAllItemsExistingInMeFromOtherInventory dereferences item.Definition.stackCount unguarded (Item.cs ~1024);
            // an item without a definition would throw mid-loop after earlier containers already received items. Refuse up front.
            var undefined = Stacks(backpack, includeBagContents).Where(it => it.Definition == null).Select(it => it.id).ToList();
            if (undefined.Count > 0)
            {
                result.AbortReason = "backpack contains item(s) without a definition: " + string.Join(", ", undefined) + " (nothing was moved)";
                return result;
            }

            keepIds = keep;
            try
            {
                foreach (WgoData target in targets)
                {
                    var before = CountById(backpack, includeBagContents);
                    target.Inventory.TakeAllItemsExistingInMeFromOtherInventory(backpack, ignoreMyBags: true, ignoreOtherBags: !includeBagContents);
                    var after = CountById(backpack, includeBagContents);

                    var delivery = new Delivery { Container = target };
                    foreach (var kv in before)
                    {
                        after.TryGetValue(kv.Key, out int left);
                        if (kv.Value > left) delivery.Items[kv.Key] = kv.Value - left;
                    }
                    if (delivery.Items.Count > 0) result.Deliveries.Add(delivery);
                }
            }
            finally
            {
                keepIds = null;
            }
            return result;
        }

        /// <summary>"Stashed N items into M containers: chest&lt;-blood x1, fat x2; shed&lt;-firewood x4" or "Nothing to stash".</summary>
        public static string Describe(StashResult r)
        {
            if (r.Deliveries.Count == 0) return "Nothing to stash";

            var perContainer = new List<string>();
            foreach (Delivery d in r.Deliveries)
                perContainer.Add(d.Container.id + "<-" + string.Join(", ", d.Items.Select(i => i.Key + " x" + i.Value)));
            return "Stashed " + r.TotalItems + " items into " + r.Deliveries.Count + " containers: " + string.Join("; ", perContainer);
        }

        /// <summary>The backpack's top-level stacks, plus the contents of its bags when included.</summary>
        private static IEnumerable<Item> Stacks(Inventory backpack, bool includeBagContents)
        {
            foreach (Item it in backpack.Data.Inventory)
            {
                if (it == null) continue;
                yield return it;
                if (!includeBagContents || it.Definition == null || !it.IsBag) continue;   // IsBag needs the definition
                foreach (Item inner in it.Inventory)
                    if (inner != null) yield return inner;
            }
        }

        private static Dictionary<string, int> CountById(Inventory backpack, bool includeBagContents)
        {
            var counts = new Dictionary<string, int>();
            foreach (Item it in Stacks(backpack, includeBagContents))
            {
                if (it.IsEmpty) continue;
                counts.TryGetValue(it.id, out int n);
                counts[it.id] = n + it.Count;
            }
            return counts;
        }
    }
}
