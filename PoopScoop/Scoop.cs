using System;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace PoopScoop
{
    /// <summary>Right-click on a filled fertilizer slot takes the fertilizer off the bed and gives the item back.</summary>
    internal static class Scoop
    {
        private static readonly AccessTools.FieldRef<UIGardenBedSlot, UIItemCell> SlotCell =
            AccessTools.FieldRefAccess<UIGardenBedSlot, UIItemCell>("itemCell");
        private static readonly AccessTools.FieldRef<LazyWidget<UIGardenBedWindowData>, UIGardenBedWindowData> WindowData =
            AccessTools.FieldRefAccess<LazyWidget<UIGardenBedWindowData>, UIGardenBedWindowData>("data");

        private static readonly HashSet<string> warned = new HashSet<string>();

        // UpdatePerks redraws every slot on each Redraw. allowInteraction is false while the bed grows or a
        // gardener order is assigned. DrawFertilizerSlot keeps old cell callbacks, so this one is always overwritten.
        [HarmonyPostfix, HarmonyPatch(typeof(UIGardenBedSlot), nameof(UIGardenBedSlot.DrawFertilizerSlot))]
        private static void HookRightClick(UIGardenBedSlot __instance, bool allowInteraction)
        {
            SlotCell(__instance).OnItemCellPress2 = allowInteraction ? _ => TryScoop(__instance) : (Action<UIItemCell>)null;
        }

        internal static bool TryScoop(UIGardenBedSlot slot)
        {
            PerkData perk = slot.PerkData;
            UIGardenBedWindow window = slot.GetComponentInParent<UIGardenBedWindow>();
            UIGardenBedWindowData data = window == null ? null : WindowData(window);
            if (perk == null || data == null) return false;

            // same gate as the vanilla slots (UIGardenBedWindow.UpdatePerks)
            WgoData bed = data.WgoData;
            if (data.IsGrowing || GardenInteractionHandler.HasAssignedGardenOrder(bed)) return false;

            Item refund = RefundFor(perk, bed);
            if (refund == null)
            {
                if (warned.Add(perk.Definition.id))
                    Plugin.Log.LogWarning("Cannot tell which item " + perk.Definition.id + " came from, so it stays on the bed.");
                return false;
            }

            bed.RemovePerk(perk);
            // vanilla leaves the slot number behind, and a gardener re-applying this fertilizer would then skip the slot
            bed.SetGameRes("perk_fertilize_" + perk.Definition.id, 0);
            Give(refund);

            data.UpdateData();
            window.Redraw();
            Plugin.Log.LogInfo("Scooped " + refund.id + " x" + refund.Count + " from " + bed.id);
            return true;
        }

        /// <summary>What applying took: needItems[0] of the fertilizer craft (GardenInteractionHandler.FormNeedItems), counted for this bed.</summary>
        private static Item RefundFor(PerkData perk, WgoData bed)
        {
            string itemId = perk.Definition.fertilizerItemId;
            if (string.IsNullOrEmpty(itemId)) return null;

            CraftDefBase craft = GardenInteractionHandler.TryFindGardenCraft(new Item(itemId), bed, logWarning: false);
            if (craft == null || craft.needItems.Count == 0) return null;
            return new Item(itemId, craft.needItems[0].GetCount(bed));
        }

        /// <summary>Into the backpack, the rest on the ground in front of the player (PlayerOrderExecutor.AddItem).</summary>
        private static void Give(Item item)
        {
            PlayerData player = MainGame.PlayerData;
            int rest = item.Count - new MultiInventory(player.Inventory).AddItem(item);
            if (rest <= 0) return;

            Vector3 pos = player.position.Value + new Vector3(player.Direction.x, 0f, player.Direction.y);
            MainGame.Instance.dropSystem.DropItem(new Item(item.id, rest), player.currentGameSceneId, pos);
        }
    }

    /// <summary>
    /// The garden bed window maps no secondary press for controllers. Chests use ItemMove for theirs
    /// (UIBaseChestWindow.GetGameKeyDelegates), so the bed gets the same button.
    /// </summary>
    internal static class ScoopGamepad
    {
        // the method lives on the generic base and is shared by other windows, so only the bed window is touched
        [HarmonyPostfix, HarmonyPatch(typeof(LazyWindow<UIGardenBedWindowData>), "GetGameKeyDelegates")]
        private static void AddKey(object __instance, Dictionary<GameKey, Func<bool>> __result)
        {
            if (!(__instance is UIGardenBedWindow window) || __result.ContainsKey(GameKey.ItemMove)) return;
            __result.Add(GameKey.ItemMove, () => ScoopFocused(window));
        }

        private static bool ScoopFocused(UIGardenBedWindow window)
        {
            // what the protected LazyWindow.GamepadNavigationController property returns
            GamepadNavigationController navigation = window.GetComponent<GamepadNavigationController>();
            GamepadNavigationItem focused = navigation == null ? null : navigation.FocusedItem;
            UIGardenBedSlot slot = focused == null ? null : focused.GetComponentInParent<UIGardenBedSlot>();
            return slot != null && slot.PerkData != null && Scoop.TryScoop(slot);
        }
    }
}
