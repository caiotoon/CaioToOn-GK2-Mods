# QuickStash

Press one key (default `G`) and every stackable item in your backpack that already exists in a nearby
container is moved into that container. Same rule as the game's own "move all similar items" button in the
chest window, applied to every container in the current zone without opening anything.

- Source: backpack only, including items inside bags (farming bag etc.). Toolbelt, equipped gear and the
  carried overhead item are never touched.
- Targets: containers in the player's current world zone (what the chest window lists as storage), nearest
  first. When not inside a container zone (no zone, or a zone of type SimpleNotContainer), containers within
  `FallbackRadius` are used.
- Only stackables move, so tools, weapons and bags themselves never move. White/black-listed containers are
  respected (vanilla check).
- Feedback: `item_put` sound, on-screen `Stashed N items`, and a bubble above each receiving container
  showing the items that went in (same frame as the workstation craft hint).
- Never fires while a window is open, the game is paused, or controls are disabled. Host only in co-op.

## Install

Build (see the repo README) or drop `QuickStash.dll` into `<game>\BepInEx\plugins\QuickStash\`.

## Config

`<game>\BepInEx\config\com.caiotoon.gk2.quickstash.cfg`. All options are live: the file's timestamp is
checked once a second while the game runs, so edits (key rebinds included) apply without restarting.

| Section | Key | Default | Meaning |
|---|---|---|---|
| Keys | `StashKey` | `G` | Stash hotkey (Unity KeyCode, modifiers allowed: `G + LeftShift`). Works while other keys are held, e.g. while walking |
| Behaviour | `PlaySound` | true | Sounds on stash / nothing to stash |
| Behaviour | `IncludeBagContents` | true | Also stash from bags inside the backpack |
| Behaviour | `ShowBubbles` | true | Bubble above receiving containers |
| Behaviour | `BubbleSeconds` | 2 | Bubble duration in real seconds |
| Behaviour | `BubbleScale` | 0.6 | 1 = craft-hint size |
| Behaviour | `BubbleColumns` | 2 | 0 or less = auto: 2 columns when scale ≤ 0.5, else 1 |
| Behaviour | `BubbleSpacing` | 4 | Gap between cells in unscaled px. Negative = game default |
| Behaviour | `MaxBubbleItems` | 4 | Cells per container, largest counts first |
| Behaviour | `ShowBubbleCount` | false | Count number on cells (scaled font looks rough) |
| Discovery | `FallbackRadius` | 12 | World units, used when not inside a container zone |
| Discovery | `IncludeConveyorChests` | false | Treat conveyor chests as targets |

## How it works

- Discovery: `PlayerData.CurrentWorldZoneData.MultiInventoryWgoDatas` when the zone `IsContainer`, otherwise
  the objects of the player's scene within `FallbackRadius` (`WorldData.Cache.wgoDataByUidCache`). Both are
  filtered by `WGODef.inventorySize != 0 && OpenInMultiInventory`, hidden/temporary objects skipped, sorted
  by distance and then deduplicated by inventory reference (nearest wins).
- The hotkey is tested with `Input.GetKeyDown` + held modifiers (BepInEx's `KeyboardShortcut.IsDown` ignores
  presses while any other key is held). The config file's last-write time is polled once a second and the
  file is reloaded when it changed.
- Move: `Inventory.TakeAllItemsExistingInMeFromOtherInventory(backpack, ignoreMyBags: true,
  ignoreOtherBags: !IncludeBagContents)` per container. What each container received is read from a backpack
  snapshot around its call and logged as one line:
  `Stashed 7 items into 2 containers: chest<-blood x1, fat x2; firewood_shed<-firewood x4`, or
  `Nothing to stash`.
- Bubble: Harmony postfix on `Wgo.GetWidgetData()` appends a custom widget built at runtime from the game's
  `UICraftHintWidget` prefab (frame + `UIItemCell`, progress parts removed), registered in
  `LazyWidgetPrefabContainer`. If that template cannot be built, one warning is logged and no bubble is shown.
