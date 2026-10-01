# Poop Scoop design

What the mod does is in [README.md](README.md). This file records how it is built and why. Paths are under
`decompiled/`.

## Flow

```
UIGardenBedWindow.UpdatePerks -> UIGardenBedSlot.DrawFertilizerSlot (postfix)    Scoop.cs
  right-click on the slot's cell (OnItemCellPress2)
LazyWindow<UIGardenBedWindowData>.GetGameKeyDelegates (postfix, bed window only)  Scoop.cs
  controller ItemMove on the focused slot
        |
  Scoop.TryScoop: gate, refund amount, remove perk, clear slot res, give item, redraw, one log line
```

## Invariants

- Nothing changes while the bed grows or has an assigned gardener order. Same gate as vanilla
  (`UIGardenBedWindow.cs:444`).
- A fertilizer is only removed when its item and amount are known. Otherwise it stays and one warning is
  logged per perk id. Nothing is ever lost.
- The refund equals what applying took, so applying and removing again creates no items.

## Facts it relies on

| Fact | Source |
|---|---|
| A fertilizer on a bed is a `perk_fertilize_*` perk on the bed's `WgoData` | `PerkDef.cs:93`, `UIGardenBedWindowData.cs:31-37` |
| Applying consumes the item at once: `TryApplyFertilizer` → `ProcessInstantCraft` → `RemoveCraftRequirements` | `GardenInteractionHandler.cs:134-155`, `CraftComponent.cs:770-776`, `CraftElementBase.cs:526-529` |
| Amount used = `needItems[0].GetCount(bed)` of the fertilizer's garden craft | `GardenInteractionHandler.cs:211-217`, `MultiInventory.cs:321` |
| Item behind a perk = `PerkDef.fertilizerItemId` | `PerkDef.cs:71`, `UIGardenBedWindow.cs:549-560` |
| Slot index lives in game res `"perk_fertilize_" + perk id` on the bed, 0 = none. Vanilla removal leaves it set | `UIGardenBedWindow.cs:471-484`, `:509`, `:517-520` |
| A stale slot res makes the gardener path treat a re-applied perk as already placed | `GardenInteractionHandler.cs:253-285` |
| Mouse right-click on a cell fires `UIItemCell.OnItemCellPress2`. `DrawFertilizerSlot` does not clear callbacks | `UIItemCell.cs:760-769`, `UIGardenBedSlot.cs:48-65` |
| Controllers reach a secondary press only through a window `GameKey` mapping. Chests use `ItemMove` | `UIBaseChestWindow.cs:65-94`, `LazyWindow.cs:233-251` |

## Decisions

| Decision | Reason |
|---|---|
| Refund the item | Applying already took it, so removing without a refund would lose it |
| No confirmation | Vanilla replace does not confirm either, and with the refund a misclick costs nothing |
| Clear the slot res on removal | Prevents the gardener path from skipping the slot when the fertilizer is applied again |
| Backpack first, then drop in front of the player | Same drop spot as `PlayerOrderExecutor.AddItem` |
| Controller patch applied separately | It patches a generic base method. If Harmony fails there, right-click still works |
| No config | Nothing to tune |
