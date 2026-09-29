# QuickStash design

What the mod does and how to configure it is in [README.md](README.md). This file records how it is built
and why, so a change can be judged against the original reasoning.

## Flow

```
PlayerInputHandler.UpdateInput (prefix)      StashInput.cs
  keyboard StashKey or controller chord
        |
      gates                                   Plugin.cs
        |
  find target containers                      ContainerFinder.cs
        |
  vanilla move per container                  Stasher.cs
        |
  sound, notification, one log line           Plugin.cs
  bubble above each receiving container       StashBubbles.cs, StashBubbleWidget.cs
```

## Invariants

These must hold after every change.

- The source is the backpack (`PlayerData.inventory`), with bag contents when `IncludeBagContents` is on.
  The toolbelt, equipped gear and the carried overhead item are never touched.
- Items pinned to the hotbar (`PlayerData.pinnedItems`) are not moved while `KeepHotbarItems` is on.
- The chest window's own "move all similar" button behaves exactly as vanilla.
- Only stackable items move, and only into a container that already holds the same item id.
- Nothing fires while a window is open, while the game is paused, in the main menu, or while the player is
  not free to act (cutscenes, dialogs, work, ladder, build, planting, attack).
- In co-op only the host can stash.
- A stash that cannot run safely moves nothing: an item without a definition aborts before the first move.

## Decisions

| Decision | Reason |
|---|---|
| Move with the vanilla `Inventory.TakeAllItemsExistingInMeFromOtherInventory` | It is the chest window's "move all similar" button. Stack limits and container filters are enforced by the game, and players already know the rule |
| Targets are the zone's `MultiInventoryWgoDatas` when `zone.IsContainer` | Same list the chest window shows as zone storage |
| Radius scan when there is no container zone | `SimpleNotContainer` zones are assigned to the player but have empty lists |
| Hidden and temporary objects are skipped in the zone path | Deliberate deviation from vanilla: items should not land in a container the player cannot see |
| Sort by distance, then drop duplicates by inventory reference | Several objects can share one inventory; the nearest one carries the bubble |
| Default key `G` | `Q` is Quest Tree and `R` is Rotate and Change Weapon in the game's default bindings |
| Keyboard stays a BepInEx shortcut | Registering it in the game's binding table would save our entry into the game's own settings |
| Controller chord names game actions, default `RightTrigger+LeftStick` | Same style as other GK2 mods. A chord avoids buttons the game or other mods already use |
| Trigger in a prefix on `PlayerInputHandler.UpdateInput()` | The game calls it only while the player is free to act, and the press can be cleared before the game reads it |
| Gates kept: window open, paused, input active, game loaded | Not implied by the trigger point: non-modal windows, windows excluded from pause bookkeeping, the bug report window, the main menu |
| Gates run before the controller press is cleared | A press is only taken from the game when a stash actually runs |
| Hotbar items kept by a postfix on private `Item.CollectUniqueItemIds` that removes the pinned ids | The vanilla loop moves an id only when that set holds it, so the game still does all the moving. The postfix acts only while a stash runs (a static field set and cleared in `finally`), so other callers see vanilla results |
| Missing `CollectUniqueItemIds` logs one warning and skips the patch | The stash keeps working; pinned items then move like any other item |
| What moved is read from a backpack snapshot around each call | The vanilla call does not report counts |
| Bubble is a runtime clone of the `UICraftHintWidget` prefab | Same frame and cell as the workstation craft hint. Item icons come from `EasySpritesCollection`, which TextMeshPro sprite tags do not cover |
| No fallback bubble | The template path works; on failure one warning is logged and no bubble is shown |
| Count hidden on bubble cells by default | The scaled font renders poorly |
| Config reloaded by polling the file's last-write time once a second | BepInEx 5 never re-reads its file. Polling is a few lines and works on Mono |
| No diagnostics, dry run or debug logging | Removed on purpose to keep the code small |

## Fragile points on a game update

- Reflection: private fields of `UICraftHintWidget` (`craftResultItem`, `progessCellContainer`,
  `progressBarWidget`, `zombieProgressBar`, `layoutElement`, `canvasGroup`, `defaultLayoutSize`,
  `completionProgressCellsParent`) and `LazyWidgetPrefabContainer.widgets`.
- Patched methods: `Wgo.GetWidgetData()`, `PlayerInputHandler.UpdateInput()` and the private
  `Item.CollectUniqueItemIds(bool)`. Read field: `PlayerData.pinnedItems`.
- Game action names used by the default chord: `RightTrigger`, `LeftStick`.
- The reasoning for each removed gate depends on `FreePlayerState.IsActive`, `SSM.CustomUpdate` and
  `LazyWindowsStackController`.

## Known limits

- An item moves only if the target already holds that exact id. Star quality is part of the id.
- A hotbar item is kept by its exact id, as the game's hotbar counts it (`UIHotBarWidget.cs:89`).
- A widget that reads the chord's button in its own update, before the player code runs, can still see the
  press for one frame.
- The stash does not fire with a seed or fertilizer in hand or while working at a station, because the
  game is then in another player state. Accepted to keep a single trigger point.
- Held buttons of a chord still perform their game action. `LeftTrigger` (L2) is Attack Focus, which leaves
  the free state when a weapon is equipped, so a chord holding it cannot complete.
