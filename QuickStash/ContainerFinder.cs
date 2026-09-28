using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuickStash
{
    internal static class ContainerFinder
    {
        /// <summary>
        /// Stash targets, nearest first. Inside a container zone (<c>WorldZoneData.IsContainer</c>): the zone's containers, as the
        /// chest window's "storage" panel lists them. Otherwise (no zone, or a SimpleNotContainer zone, whose lists PrepareForGame
        /// clears): every container of the player's scene within <paramref name="fallbackRadius"/>.
        /// </summary>
        public static List<WgoData> FindTargets(PlayerData pd, float fallbackRadius, bool includeConveyorChests)
        {
            Vector3 pos = pd.position.Value;
            WorldZoneData zone = pd.CurrentWorldZoneData;

            IEnumerable<WgoData> candidates;
            if (zone != null && zone.IsContainer && zone.MultiInventoryWgoDatas != null)
                candidates = zone.MultiInventoryWgoDatas;
            else
                candidates = MainGame.WorldData.Cache.wgoDataByUidCache.Values
                    .Where(w => w.WorldId == pd.currentGameSceneId && Vector3.Distance(w.Position, pos) <= fallbackRadius);

            var nearestFirst = candidates
                .Where(w => IsStashTarget(w, includeConveyorChests))
                .OrderBy(w => Vector3.Distance(w.Position, pos));

            // shared inventories (refToOtherWgoInventory) are kept once: sorted first, so the nearest container wins
            var seen = new HashSet<Inventory>();   // Inventory does not override equality: reference semantics
            var targets = new List<WgoData>();
            foreach (WgoData w in nearestFirst)
                if (seen.Add(w.Inventory)) targets.Add(w);
            return targets;
        }

        private static bool IsStashTarget(WgoData w, bool includeConveyorChests)
        {
            WGODef def = w.Definition;
            if (def == null || def.inventorySize == 0 || !def.OpenInMultiInventory) return false;
            // Deliberate deviation from vanilla MultiInventory(zone): hidden/temporary objects are skipped
            if (w.IsHidden || w.isTempObject || w.Inventory == null) return false;

            bool isConveyorChest = def.conveyorType == ConveyorElementType.Chest || def.conveyorType == ConveyorElementType.ChestOut;
            return includeConveyorChests || !isConveyorChest;
        }
    }
}
