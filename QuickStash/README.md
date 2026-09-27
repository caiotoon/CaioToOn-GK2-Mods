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

`<game>\BepInEx\config\com.caiotoon.gk2.quickstash.cfg`. The file is watched: edits apply within a second
while the game runs (key rebinds included), except `ShowBubbles`, which needs a restart.

| Section | Key | Default | Meaning |
|---|---|---|---|
| Keys | `StashKey` | `G` | Stash hotkey (Unity KeyCode, modifiers allowed: `G + LeftShift`). Works while other keys are held, e.g. while walking |
| Keys | `DiagnosticsKey` | `F9` | Writes `BepInEx\QuickStash-dump.txt`. Never moves anything |
| Behaviour | `DryRun` | false | Stash key only logs what would move |
| Behaviour | `PlaySound` | true | Sounds on stash / nothing to stash |
| Behaviour | `IncludeBagContents` | true | Also stash from bags inside the backpack |
| Behaviour | `ShowBubbles` | true | Bubble above receiving containers (restart to change) |
| Behaviour | `BubbleSeconds` | 2 | Bubble duration in real seconds |
| Behaviour | `BubbleScale` | 1 | 1 = craft-hint size |
| Behaviour | `BubbleColumns` | 0 | 0 or less = auto: 2 columns when scale ≤ 0.5, else 1 |
| Behaviour | `BubbleSpacing` | -1 | Gap between cells in unscaled px. Negative = game default |
| Behaviour | `MaxBubbleItems` | 4 | Cells per container, largest counts first |
| Behaviour | `ShowBubbleCount` | false | Count number on cells (scaled font looks rough) |
| Discovery | `FallbackRadius` | 12 | World units, used when not inside a container zone |
| Discovery | `IncludeConveyorChests` | false | Treat conveyor chests as targets |

## Diagnostics

`F9` writes a dump (latest plus a timestamped copy per zone) with: player position and zone, every
container in the zone and within radius with filters and contents, all container definitions, backpack and
toolbelt contents, game key bindings and collisions with the configured keys, the dry-run stash plan with
reasons for unmoved stacks, the stash gates, and the last bubble layout.

## How it works

- Discovery: `PlayerData.CurrentWorldZoneData.MultiInventoryWgoDatas` (fallback `wgoDataList`) when the zone
  `IsContainer`, filtered by `WGODef.inventorySize != 0 && OpenInMultiInventory`, hidden/temporary objects
  skipped, sorted by distance and then deduplicated by inventory reference (nearest wins). Radius fallback
  scans `WorldData.Cache.wgoDataByUidCache`.
- Hotkeys are tested with `Input.GetKeyDown` + held modifiers (BepInEx's `KeyboardShortcut.IsDown` ignores
  presses while any other key is held). The config file is watched with a `FileSystemWatcher` and reloaded
  half a second after the last change.
- Move: `Inventory.TakeAllItemsExistingInMeFromOtherInventory(backpack, ignoreMyBags: true,
  ignoreOtherBags: !IncludeBagContents)` per container. Item totals are checked before and after.
- Bubble: Harmony postfix on `Wgo.GetWidgetData()` appends a custom widget built at runtime from the game's
  `UICraftHintWidget` prefab (frame + `UIItemCell`, progress parts removed), registered in
  `LazyWidgetPrefabContainer`.
