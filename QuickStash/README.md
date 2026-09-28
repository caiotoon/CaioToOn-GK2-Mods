# QuickStash

Press one key (default `G`, or hold R2 and press L3 on a controller) and every stackable item in your
backpack that already exists in a nearby container is moved into that container. Same rule as the game's own "move all similar items" button in the
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
- Controller support: hold R2 and press L3 (left stick click). The chord is configurable, and the button
  press that completes it is not passed on to the game.
- Never fires while a window is open, the game is paused, in the main menu, or while controls are disabled
  (cutscenes, dialogs). Host only in co-op.

## Install

Build (see the repo README) or drop `QuickStash.dll` into `<game>\BepInEx\plugins\QuickStash\`.

## Config

`<game>\BepInEx\config\com.caiotoon.gk2.quickstash.cfg`. All options are live: the file's timestamp is
checked once a second while the game runs, so edits (key rebinds included) apply without restarting.

| Section | Key | Default | Meaning |
|---|---|---|---|
| Keys | `StashKey` | `G` | Stash hotkey (Unity KeyCode, modifiers allowed: `G + LeftShift`). Works while other keys are held, e.g. while walking |
| Keys | `GamepadStash` | `RightTrigger+LeftStick` | Controller chord: game action names joined with `+`. The last one is pressed, the ones before it are held; a single name is allowed. `None` or empty disables |
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

Button names for `GamepadStash` (the same legend is written into the cfg file):

| Button | Name |
|---|---|
| R2 / RT, right trigger | `RightTrigger` |
| L2 / LT, left trigger | `LeftTrigger` |
| R1 / RB, right bumper | `RightBumper` |
| R3, right stick click | `RightStick` |
| L3, left stick click | `LeftStick` |
| D-pad | `DpadUp`, `DpadDown`, `DpadLeft`, `DpadRight` |

Other buttons are named by the game action they perform, for example `Interaction`, `Action`, `Inventory`.
Example: `RightTrigger+LeftStick` = hold R2, press L3.

## How it works

- Discovery: `PlayerData.CurrentWorldZoneData.MultiInventoryWgoDatas` when the zone `IsContainer`, otherwise
  the objects of the player's scene within `FallbackRadius` (`WorldData.Cache.wgoDataByUidCache`). Both are
  filtered by `WGODef.inventorySize != 0 && OpenInMultiInventory`, hidden/temporary objects skipped, sorted
  by distance and then deduplicated by inventory reference (nearest wins).
- Triggers are read in a Harmony prefix on `PlayerInputHandler.UpdateInput()`, which the game only calls
  while the player is free to act (`SSM.CustomUpdate` -> `FreePlayerState.Update`, active only with controls
  enabled). Keyboard: `Input.GetKeyDown` + held modifiers (BepInEx's `KeyboardShortcut.IsDown` ignores presses
  while any other key is held). Controller: `LazyInput.GetKeyDown` on the last action of the chord while
  `LazyInput.GetKey` holds for the others; then every action raised by the same physical button is cleared
  and ignored until release, so the game does not react to it.
- The config file's last-write time is polled once a second and the file is reloaded when it changed.
- Move: `Inventory.TakeAllItemsExistingInMeFromOtherInventory(backpack, ignoreMyBags: true,
  ignoreOtherBags: !IncludeBagContents)` per container. What each container received is read from a backpack
  snapshot around its call and logged as one line:
  `Stashed 7 items into 2 containers: chest<-blood x1, fat x2; firewood_shed<-firewood x4`, or
  `Nothing to stash`.
- Bubble: Harmony postfix on `Wgo.GetWidgetData()` appends a custom widget built at runtime from the game's
  `UICraftHintWidget` prefab (frame + `UIItemCell`, progress parts removed), registered in
  `LazyWidgetPrefabContainer`. If that template cannot be built, one warning is logged and no bubble is shown.
