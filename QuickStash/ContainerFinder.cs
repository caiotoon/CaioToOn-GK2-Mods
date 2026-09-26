using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace QuickStash
{
    /// <summary>A world object with an inventory, as seen from the player.</summary>
    internal sealed class ContainerInfo
    {
        public WgoData Wgo;
        public float Distance;
        /// <summary>Listed by the player's current WorldZoneData.</summary>
        public bool InZone;
        /// <summary>Found by the radius scan over the world cache.</summary>
        public bool InRadius;

        public string Id => Wgo.id;
        public string Uid => Wgo.UniqueId != null ? Wgo.UniqueId.ToString() : "-";
        public WGODef Def => Wgo.Definition;
    }

    internal static class ContainerFinder
    {
        public static bool HasInventory(WgoData w)
        {
            return w != null && w.Definition != null && w.Definition.inventorySize != 0;
        }

        public static bool IsConveyorChest(WGODef d)
        {
            return d != null && (d.conveyorType == ConveyorElementType.Chest || d.conveyorType == ConveyorElementType.ChestOut);
        }

        public static float Distance(WgoData w, Vector3 playerPos)
        {
            return Vector3.Distance(w.Position, playerPos);
        }

        /// <summary>
        /// Zone containers as cached by the game (WorldZoneData.MultiInventoryWgoDatas, filled in PrepareForGame).
        /// May be null before PrepareForGame ran; then this returns an empty list.
        /// </summary>
        public static List<WgoData> ZoneWgosPrepared(WorldZoneData zone)
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
        public static List<WgoData> ZoneWgosFromGuids(WorldZoneData zone)
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
        public static List<WgoData> RadiusWgos(string sceneId, Vector3 playerPos, float radius)
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
        /// Stash targets: zone containers with <c>OpenInMultiInventory</c> (the chest window's "storage" panel filter),
        /// deduplicated by shared inventory (<c>refToOtherWgoInventory</c>), sorted by distance.
        /// Falls back to a radius scan when the player is not inside a zone.
        /// </summary>
        public static List<ContainerInfo> FindTargets(PlayerData pd, float fallbackRadius, bool includeConveyorChests, out string source)
        {
            Vector3 pos = pd.position.Value;
            WorldZoneData zone = pd.CurrentWorldZoneData;
            List<WgoData> candidates;
            bool fromZone;

            if (zone != null)
            {
                fromZone = true;
                candidates = ZoneWgosPrepared(zone);
                source = "zone.MultiInventoryWgoDatas";
                if (candidates.Count == 0)
                {
                    candidates = ZoneWgosFromGuids(zone);
                    source = "zone.wgoDataList";
                }
            }
            else
            {
                fromZone = false;
                candidates = RadiusWgos(pd.currentGameSceneId, pos, fallbackRadius);
                source = "radius " + fallbackRadius;
            }

            var seen = new HashSet<Inventory>(RefEq<Inventory>.Instance);
            var result = new List<ContainerInfo>();
            foreach (var w in candidates)
            {
                if (!HasInventory(w)) continue;
                if (!w.Definition.OpenInMultiInventory) continue;
                if (!includeConveyorChests && IsConveyorChest(w.Definition)) continue;
                var inv = w.Inventory;
                if (inv == null || !seen.Add(inv)) continue;
                result.Add(new ContainerInfo { Wgo = w, Distance = Distance(w, pos), InZone = fromZone, InRadius = !fromZone });
            }
            result.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return result;
        }

        private sealed class RefEq<T> : IEqualityComparer<T> where T : class
        {
            public static readonly RefEq<T> Instance = new RefEq<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
