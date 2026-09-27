using System.Collections.Generic;
using System.Text;

namespace QuickStash
{
    internal sealed class MoveEntry
    {
        public string ContainerId;
        public string ContainerUid;
        public WgoData Container;
        public string ItemId;
        public int Count;
        /// <summary>The stack came out of a bag inside the backpack.</summary>
        public bool FromBag;
    }

    internal sealed class StashResult
    {
        public int TargetCount;
        public readonly List<MoveEntry> Moves = new List<MoveEntry>();

        /// <summary>Set when the run refused to start (nothing was moved); <see cref="AbortReason"/> says why.</summary>
        public bool Aborted;
        public string AbortReason;

        public int TotalItems
        {
            get { int n = 0; foreach (var m in Moves) n += m.Count; return n; }
        }

        public int ContainersTouched
        {
            get
            {
                var set = new HashSet<string>();
                foreach (var m in Moves) set.Add(m.ContainerUid);
                return set.Count;
            }
        }
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
        /// <summary>One vanilla call per target, in the given (distance) order. Moves are derived from a backpack id->count snapshot around each call.</summary>
        public static StashResult Execute(Inventory backpack, IList<ContainerInfo> targets, bool includeBagContents)
        {
            var result = new StashResult { TargetCount = targets != null ? targets.Count : 0 };
            if (backpack == null || backpack.Data == null || targets == null) return result;

            // Vanilla Item.TakeAllItemsExistingInMeFromOtherInventory dereferences item.Definition.stackCount unguarded (Item.cs ~1024);
            // an item without a definition would throw mid-loop after earlier containers already received items. Refuse up front.
            string undefined = ItemsWithoutDefinition(backpack.Data.Inventory, includeBagContents);
            if (undefined != null)
            {
                result.Aborted = true;
                result.AbortReason = "backpack contains item(s) without a definition: " + undefined + " (nothing was moved)";
                return result;
            }

            foreach (var c in targets)
            {
                var inv = c.Wgo != null ? c.Wgo.Inventory : null;
                if (inv == null || inv.Data == null || ReferenceEquals(inv, backpack)) continue;

                var topBefore = CountById(backpack.Data.Inventory, false);
                var bagBefore = CountById(backpack.Data.Inventory, true);

                inv.TakeAllItemsExistingInMeFromOtherInventory(backpack, ignoreMyBags: true, ignoreOtherBags: !includeBagContents);

                AddDiff(result, c, topBefore, CountById(backpack.Data.Inventory, false), false);
                AddDiff(result, c, bagBefore, CountById(backpack.Data.Inventory, true), true);
            }
            return result;
        }

        private static void AddDiff(StashResult result, ContainerInfo c, Dictionary<string, int> before, Dictionary<string, int> after, bool fromBag)
        {
            foreach (var kv in before)
            {
                int left;
                after.TryGetValue(kv.Key, out left);
                int moved = kv.Value - left;
                if (moved > 0)
                    result.Moves.Add(new MoveEntry { ContainerId = c.Id, ContainerUid = c.Uid, Container = c.Wgo, ItemId = kv.Key, Count = moved, FromBag = fromBag });
            }
        }

        /// <summary>"Stashed N items into M containers: chest&lt;-blood x1, fat x2[bag]; shed&lt;-firewood x4" or "Nothing to stash".</summary>
        public static string Describe(StashResult r)
        {
            if (r.Moves.Count == 0) return "Nothing to stash";

            var sb = new StringBuilder();
            sb.Append("Stashed ").Append(r.TotalItems).Append(" items into ").Append(r.ContainersTouched).Append(" containers: ");

            string cur = null;
            bool firstContainer = true, firstItem = true;
            foreach (var m in r.Moves)
            {
                if (m.ContainerUid != cur)
                {
                    cur = m.ContainerUid;
                    if (!firstContainer) sb.Append("; ");
                    firstContainer = false;
                    firstItem = true;
                    sb.Append(m.ContainerId).Append("<-");
                }
                if (!firstItem) sb.Append(", ");
                firstItem = false;
                sb.Append(m.ItemId).Append(" x").Append(m.Count);
                if (m.FromBag) sb.Append("[bag]");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Comma-separated ids of backpack items (top-level, and bag contents when included) whose Definition is null; null when none.</summary>
        private static string ItemsWithoutDefinition(List<Item> items, bool includeBagContents)
        {
            List<string> broken = null;
            foreach (var it in items)
            {
                if (it == null) continue;
                if (it.Definition == null) { (broken ?? (broken = new List<string>())).Add(it.id ?? "(null id)"); continue; }   // IsBag needs the definition
                if (!includeBagContents || !it.IsBag || it.Inventory == null) continue;
                foreach (var inner in it.Inventory)
                    if (inner != null && inner.Definition == null) (broken ?? (broken = new List<string>())).Add((inner.id ?? "(null id)") + " (in " + it.id + ")");
            }
            return broken == null ? null : string.Join(", ", broken);
        }

        /// <summary>id -> count over the top-level stacks (<paramref name="bagContents"/> false) or over the contents of top-level bags (true).</summary>
        private static Dictionary<string, int> CountById(List<Item> items, bool bagContents)
        {
            var d = new Dictionary<string, int>();
            foreach (var it in items)
            {
                if (it == null || it.IsEmpty) continue;
                if (!bagContents) { Add(d, it.id, it.Count); continue; }
                if (!it.IsBag || it.Inventory == null) continue;
                foreach (var inner in it.Inventory)
                    if (inner != null && !inner.IsEmpty) Add(d, inner.id, inner.Count);
            }
            return d;
        }

        private static void Add(Dictionary<string, int> d, string id, int count)
        {
            int n;
            d.TryGetValue(id, out n);
            d[id] = n + count;
        }
    }
}
