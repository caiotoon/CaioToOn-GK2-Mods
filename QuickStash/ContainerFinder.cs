using System.Collections.Generic;
using UnityEngine;

namespace QuickStash
{
    /// <summary>A stash target: a world object with an inventory, with its distance to the player.</summary>
    internal sealed class ContainerInfo
    {
        public WgoData Wgo;
        public float Distance;

        public string Id => Wgo.id;
        public string Uid => Wgo.UniqueId != null ? Wgo.UniqueId.ToString() : "-";
    }

    internal static class ContainerFinder
    {
        private static bool HasInventory(WgoData w)
        {
            return w != null && w.Definition != null && w.Definition.inventorySize != 0;
        }

        private static bool IsConveyorChest(WGODef d)
        {
            return d != null && (d.conveyorType == ConveyorElementType.Chest || d.conveyorType == ConveyorElementType.ChestOut);
        }

        private static float Distance(WgoData w, Vector3 playerPos)
        {
            return Vector3.Distance(w.Position, playerPos);
        }

        /// <summary>
        /// Zone containers as cached by the game (WorldZoneData.MultiInventoryWgoDatas, filled in PrepareForGame).
        /// May be null before PrepareForGame ran; then this returns an empty list.
        /// </summary>
        private static List<WgoData> ZoneWgosPrepared(WorldZoneData zone)
        {
            var list = new List<WgoData>();
            var src = zone != null ? zone.MultiInventoryWgoDatas : null;
            if (src == null) return list;
            foreach (var w in src)
                if (HasInventory(w)) list.Add(w);
            return list;
        }

        /// <summary>
        /// Zone containers resolved from WorldZoneData.wgoDataList through the world cache
        /// (same path as <c>new MultiInventory(zone)</c>).
        /// </summary>
        private static List<WgoData> ZoneWgosFromGuids(WorldZoneData zone)
        {
            var list = new List<WgoData>();
            if (zone == null || zone.wgoDataList == null || MainGame.Instance == null) return list;
            var world = MainGame.WorldData;
            if (world == null) return list;
            foreach (var sguid in zone.wgoDataList)
            {
                var w = world.GetWgoData(sguid);
                if (HasInventory(w)) list.Add(w);
            }
            return list;
        }

        /// <summary>All visible, non-temporary containers in the player's scene within <paramref name="radius"/>.</summary>
        private static List<WgoData> RadiusWgos(string sceneId, Vector3 playerPos, float radius)
        {
            var list = new List<WgoData>();
            if (MainGame.Instance == null || MainGame.WorldData == null) return list;
            var cache = MainGame.WorldData.Cache;
            if (cache == null || cache.wgoDataByUidCache == null) return list;

            foreach (var w in new List<WgoData>(cache.wgoDataByUidCache.Values))
            {
                if (w == null || w.IsHidden || w.isTempObject) continue;
                if (w.WorldId != sceneId) continue;
                if (!HasInventory(w)) continue;
                if (Distance(w, playerPos) > radius) continue;
                list.Add(w);
            }
            return list;
        }

        /// <summary>
        /// Stash targets: containers of the player's current zone with <c>OpenInMultiInventory</c> (the chest window's
        /// "storage" panel filter), deduplicated by shared inventory (<c>refToOtherWgoInventory</c>), nearest first.
        /// The zone is used only when it is a container zone (<c>WorldZoneData.IsContainer</c>): SimpleNotContainer zones are
        /// assigned as CurrentWorldZoneData too but PrepareForGame clears their lists. Otherwise, or with no zone, the
        /// radius scan is used.
        /// </summary>
        public static List<ContainerInfo> FindTargets(PlayerData pd, float fallbackRadius, bool includeConveyorChests)
        {
            Vector3 pos = pd.position.Value;
            WorldZoneData zone = pd.CurrentWorldZoneData;
            List<WgoData> candidates;

            if (zone != null && zone.IsContainer)
            {
                candidates = ZoneWgosPrepared(zone);
                if (candidates.Count == 0) candidates = ZoneWgosFromGuids(zone);
            }
            else
            {
                candidates = RadiusWgos(pd.currentGameSceneId, pos, fallbackRadius);
            }

            // nearest first BEFORE the shared-inventory dedup, so the retained container (bubble anchor, log id) is the closest one
            var sorted = new List<ContainerInfo>();
            foreach (var w in candidates)
            {
                if (!HasInventory(w)) continue;
                if (!w.Definition.OpenInMultiInventory) continue;
                // Deliberate deviation from vanilla MultiInventory(zone): hidden/temporary objects are skipped here as in the radius scan.
                if (w.IsHidden || w.isTempObject) continue;
                if (!includeConveyorChests && IsConveyorChest(w.Definition)) continue;
                sorted.Add(new ContainerInfo { Wgo = w, Distance = Distance(w, pos) });
            }
            sorted.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            var seen = new HashSet<Inventory>();   // Inventory does not override equality: reference semantics
            var result = new List<ContainerInfo>();
            foreach (var c in sorted)
            {
                var inv = c.Wgo.Inventory;
                if (inv == null || !seen.Add(inv)) continue;
                result.Add(c);
            }
            return result;
        }
    }
}
