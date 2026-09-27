using System;
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

        public override string ToString() => ContainerId + " <- " + ItemId + " x " + Count + (FromBag ? "[bag]" : "");
    }

    internal sealed class StashResult
    {
        public bool DryRun;
        public int TargetCount;
        public readonly List<MoveEntry> Moves = new List<MoveEntry>();

        /// <summary>Set when the real run refused to start (nothing was moved); <see cref="AbortReason"/> says why.</summary>
        public bool Aborted;
        public string AbortReason;

        /// <summary>Item totals of backpack (top-level + bag contents) + all targets (top-level) before/after a real run; -1 when not measured.</summary>
        public int TotalBefore = -1;
        public int TotalAfter = -1;

        public bool ConservationChecked => TotalBefore >= 0;
        public bool ConservationOk => TotalBefore == TotalAfter;

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
    /// "SimilarOnly" strategy = the vanilla chest button
    /// <c>Inventory.TakeAllItemsExistingInMeFromOtherInventory(player.inventory, ignoreMyBags: true, ignoreOtherBags)</c>:
    /// walks the backpack backwards; a top-level stack with <c>stackCount &gt; 1</c> whose id exists top-level in the container is
    /// moved (capacity and white/black lists enforced inside <c>Item.CanAddItemCountToInventory</c>); a bag met on the way has its
    /// contents walked backwards with the same rule when <c>ignoreOtherBags</c> is false. Items are never put into bags inside the container.
    /// </summary>
    internal static class Stasher
    {
        public static StashResult Run(Inventory backpack, IList<ContainerInfo> targets, bool dryRun, bool includeBagContents)
        {
            return dryRun ? Plan(backpack, targets, includeBagContents) : Execute(backpack, targets, includeBagContents);
        }

        // ------------------------------------------------------------------ dry run

        /// <summary>Simulates the moves without touching any inventory.</summary>
        public static StashResult Plan(Inventory backpack, IList<ContainerInfo> targets, bool includeBagContents)
        {
            var result = new StashResult { DryRun = true, TargetCount = targets != null ? targets.Count : 0 };
            if (backpack == null || backpack.Data == null || targets == null) return result;

            List<Item> src = backpack.Data.Inventory;
            var remaining = new Dictionary<Item, int>();   // Item does not override equality: reference semantics per stack

            foreach (var c in targets)
            {
                var inv = c.Wgo != null ? c.Wgo.Inventory : null;
                var sim = new ContainerSim(inv != null ? inv.Data : null);
                if (!sim.IsValid) continue;

                for (int i = src.Count - 1; i >= 0; i--)                       // vanilla walks the source backwards
                {
                    Item item = src[i];
                    if (item == null || item.IsEmpty || item.Definition == null) continue;

                    if (item.Definition.stackCount > 1)
                    {
                        SimulateMove(sim, c, item, false, remaining, result);
                    }
                    else if (includeBagContents && item.IsBag && item.Inventory != null)
                    {
                        for (int j = item.Inventory.Count - 1; j >= 0; j--)
                        {
                            Item inner = item.Inventory[j];
                            if (inner == null || inner.IsEmpty || inner.Definition == null || inner.Definition.stackCount <= 1) continue;
                            SimulateMove(sim, c, inner, true, remaining, result);
                        }
                    }
                }
            }
            return result;
        }

        private static void SimulateMove(ContainerSim sim, ContainerInfo c, Item item, bool fromBag, Dictionary<Item, int> remaining, StashResult result)
        {
            int left;
            if (!remaining.TryGetValue(item, out left)) left = item.Count;
            if (left <= 0 || !sim.HasId(item.id)) return;

            int n = sim.TryAdd(item.Definition, left);
            if (n <= 0) return;
            remaining[item] = left - n;
            result.Moves.Add(new MoveEntry { ContainerId = c.Id, ContainerUid = c.Uid, Container = c.Wgo, ItemId = item.id, Count = n, FromBag = fromBag });
        }

        /// <summary>For the dump: why each stackable backpack id is not (fully) in the plan.</summary>
        public static List<string> ExplainUnmoved(Inventory backpack, IList<ContainerInfo> targets, StashResult plan, bool includeBagContents)
        {
            var lines = new List<string>();
            if (backpack == null || backpack.Data == null || backpack.Data.Inventory == null) return lines;

            var totals = new Dictionary<string, int>();
            var order = new List<string>();
            var nonStackable = new List<string>();
            var skippedInBags = new List<string>();
            foreach (var it in backpack.Data.Inventory)
            {
                if (it == null || it.IsEmpty || it.Definition == null) continue;
                if (it.Definition.stackCount > 1) { AddTotal(totals, order, it.id, it.Count); continue; }
                nonStackable.Add(it.id);
                if (it.IsBag && it.Inventory != null)
                {
                    foreach (var inner in it.Inventory)
                    {
                        if (inner == null || inner.IsEmpty || inner.Definition == null || inner.Definition.stackCount <= 1) continue;
                        if (includeBagContents) AddTotal(totals, order, inner.id, inner.Count);
                        else skippedInBags.Add(inner.id + " x " + inner.Count + " (in " + it.id + ")");
                    }
                }
            }

            var planned = new Dictionary<string, int>();
            foreach (var m in plan.Moves)
            {
                int n;
                planned.TryGetValue(m.ItemId, out n);
                planned[m.ItemId] = n + m.Count;
            }

            foreach (var id in order)
            {
                int p;
                planned.TryGetValue(id, out p);
                int total = totals[id];
                if (p >= total) continue;

                var holders = new List<string>();
                if (targets != null)
                {
                    foreach (var c in targets)
                    {
                        var data = c.Wgo != null && c.Wgo.Inventory != null ? c.Wgo.Inventory.Data : null;
                        if (data == null || data.Inventory == null) continue;
                        foreach (var it in data.Inventory)
                            if (it != null && it.id == id) { holders.Add(c.Id); break; }
                    }
                }

                string head = (id + " x " + (total - p)).PadRight(30) + (p > 0 ? "(partial: " + p + "/" + total + " planned) " : "");
                string why = holders.Count == 0
                    ? "no target contains this id"
                    : "target(s) " + string.Join(",", holders) + " contain it but have no room (or filter it out)";
                lines.Add(head + why);
            }

            if (skippedInBags.Count > 0)
                lines.Add("inside bags, skipped because IncludeBagContents=false: " + string.Join(", ", skippedInBags));
            if (nonStackable.Count > 0)
                lines.Add("stackCount=1, vanilla never moves these: " + string.Join(", ", nonStackable));
            return lines;
        }

        private static void AddTotal(Dictionary<string, int> totals, List<string> order, string id, int count)
        {
            if (!totals.ContainsKey(id)) { totals[id] = 0; order.Add(id); }
            totals[id] += count;
        }

        // ------------------------------------------------------------------ real run

        /// <summary>Real move via the vanilla API, one call per target, in the given (distance) order.</summary>
        public static StashResult Execute(Inventory backpack, IList<ContainerInfo> targets, bool includeBagContents)
        {
            var result = new StashResult { DryRun = false, TargetCount = targets != null ? targets.Count : 0 };
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

            result.TotalBefore = TotalItems(backpack, targets);
            foreach (var c in targets)
            {
                var inv = c.Wgo != null ? c.Wgo.Inventory : null;
                if (inv == null || inv.Data == null || ReferenceEquals(inv, backpack)) continue;

                var topBefore = CountById(backpack.Data.Inventory, false);
                var bagBefore = CountById(backpack.Data.Inventory, true);

                inv.TakeAllItemsExistingInMeFromOtherInventory(backpack, ignoreMyBags: true, ignoreOtherBags: !includeBagContents);

                var topAfter = CountById(backpack.Data.Inventory, false);
                var bagAfter = CountById(backpack.Data.Inventory, true);

                AddDiff(result, c, topBefore, topAfter, false);
                AddDiff(result, c, bagBefore, bagAfter, true);
            }
            result.TotalAfter = TotalItems(backpack, targets);
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

        public static string Describe(StashResult r, string source)
        {
            string prefix = r.DryRun ? "[DryRun] " : "";
            if (r.Moves.Count == 0)
                return prefix + "Nothing to stash (targets=" + r.TargetCount + ", " + source + ")";

            var sb = new StringBuilder();
            sb.Append(prefix).Append(r.DryRun ? "Would stash " : "Stashed ")
              .Append(r.TotalItems).Append(" items (").Append(r.Moves.Count).Append(" stacks) into ")
              .Append(r.ContainersTouched).Append(" containers: ");

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

            if (r.ConservationChecked)
                sb.Append(r.ConservationOk
                    ? "  [items conserved: " + r.TotalBefore + "]"
                    : "  [CONSERVATION MISMATCH before=" + r.TotalBefore + " after=" + r.TotalAfter + "]");
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

        private static int SumCounts(List<Item> items, bool includeBagContents)
        {
            int n = 0;
            foreach (var it in items)
            {
                if (it == null || it.IsEmpty) continue;
                n += it.Count;
                if (includeBagContents && it.IsBag && it.Inventory != null)
                    foreach (var inner in it.Inventory) if (inner != null && !inner.IsEmpty) n += inner.Count;
            }
            return n;
        }

        /// <summary>Backpack (top-level + bag contents) + targets (top-level; container bags are never touched with ignoreMyBags: true).</summary>
        private static int TotalItems(Inventory backpack, IList<ContainerInfo> targets)
        {
            int n = SumCounts(backpack.Data.Inventory, true);
            foreach (var c in targets)
            {
                var inv = c.Wgo != null ? c.Wgo.Inventory : null;
                if (inv == null || inv.Data == null || inv.Data.Inventory == null || ReferenceEquals(inv, backpack)) continue;
                n += SumCounts(inv.Data.Inventory, false);
            }
            return n;
        }

        /// <summary>
        /// Capacity model of one container for the dry run. Empty slots and partial stacks consumed by earlier
        /// simulated adds are tracked so later items see the reduced space, like the real sequential adds would.
        /// </summary>
        private sealed class ContainerSim
        {
            private readonly Item data;
            private readonly HashSet<string> ids = new HashSet<string>();
            private readonly Dictionary<string, int> stackRoom = new Dictionary<string, int>();
            private int emptySlots = -1;

            public bool IsValid => data != null && data.Inventory != null;

            public ContainerSim(Item data)
            {
                this.data = data;
                if (!IsValid) return;
                // Item.CollectUniqueItemIds(ignoreBags: true) -> top-level ids only
                foreach (var it in data.Inventory)
                    if (it != null) ids.Add(it.id);
            }

            public bool HasId(string id) => ids.Contains(id);

            /// <summary>Returns how many of <paramref name="count"/> would fit; updates the simulated state.</summary>
            public int TryAdd(ItemDef def, int count)
            {
                int stack = def.stackCount;
                if (stack <= 0 || count <= 0) return 0;

                // Real capacity right now: white/black lists, partial stacks and empty slots (ignoreAllBags: true).
                int realTotal = data.CanAddItemCountToInventory(def, int.MaxValue, considerEmptySlots: true, ignoredBag: null, ignoreAllBags: true);
                if (realTotal <= 0) return 0;

                int realRoomInStacks = 0;
                foreach (var it in data.Inventory)
                    if (it != null && it.id == def.id) realRoomInStacks += Math.Max(0, stack - it.Count);

                if (emptySlots < 0)
                    emptySlots = Math.Max(0, (realTotal - realRoomInStacks) / stack);

                int room;
                if (!stackRoom.TryGetValue(def.id, out room)) room = realRoomInStacks;

                int n = Math.Min(count, room + emptySlots * stack);
                if (n <= 0) return 0;

                int fromStacks = Math.Min(n, room);
                room -= fromStacks;
                int rest = n - fromStacks;
                if (rest > 0)
                {
                    int newSlots = (rest + stack - 1) / stack;
                    emptySlots -= newSlots;
                    room += newSlots * stack - rest;
                }
                stackRoom[def.id] = room;
                return n;
            }
        }
    }
}
