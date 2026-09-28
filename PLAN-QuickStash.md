# QuickStash — GK2 BepInEx plugin plan

Press a key (default `Q`) → every stackable item in the player backpack that already
exists in a nearby container gets moved into that container. No UI.

## 1. Environment (verified 2026-09-26)

| Item | Value |
|---|---|
| Game dir | `C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2` |
| Engine | Unity `6000.3.9` (Unity 6), **Mono** backend, legacy Input module present |
| Mod loader | **BepInEx 5.4.23.5** already installed (Doorstop 4.5.0), console disabled in `BepInEx/config/BepInEx.cfg` |
| Existing plugin | `BepInEx/plugins/RecipePin.dll` ("Pin My Recipe 1.4.0", guid `br.pedro.gk2.recipepin`) — useful reference for csproj refs |
| Game code | `GraveyardKeeper2_Data/Managed/Assembly-CSharp.dll` (4 MB), `LazyBearTechnology.dll`, `Assembly-CSharp-firstpass.dll` |
| Input | Rewired (`Rewired_Core.dll`) wrapped by `LazyBearTechnology.LazyInput` + `GameKey` enumeration |
| Netcode | Unity Netcode present, co-op is host/client (`UNetworkManager.IsCoopGame`). Inventory ops are **not** replicated by commands → singleplayer/host only |
| Native mods | Only language / voice-over packs (`%LocalLow%\Lazy Bear Games\Graveyard Keeper 2\Mods`) → BepInEx is required |
| Decompiler | `ilspycmd 9.1` available (`~/.dotnet/tools`) |
| .NET SDK | **none installed** (runtimes 6/8/9 only). `winget install Microsoft.DotNet.SDK.8` (8.0.425 available) |

Decompile (regenerate any time, ~1 min):

```powershell
$M = "C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2\GraveyardKeeper2_Data\Managed"
foreach ($a in "Assembly-CSharp","LazyBearTechnology","Assembly-CSharp-firstpass") {
  ilspycmd --disable-updatecheck -p -o "decompiled\$a" -r $M "$M\$a.dll"
}
```

## 2. Game model (from decompiled source)

| Concept | Type / member | Notes |
|---|---|---|
| Player data | `MainGame.PlayerData` (`PlayerData`) | `inventory` (backpack), `toolBeltInventory`, `interactingItem`, overhead items, `currentGameSceneId`, `position.Value` |
| Player controller | `MainGame.PlayerController` | `IsControlsEnabled` gate |
| Current area | `PlayerData.CurrentWorldZoneData` (`WorldZoneData`) | Set by trigger collider (`PlayerPhysicalBody`). **Can be null** between zones |
| Containers in area | `WorldZoneData.MultiInventoryWgoDatas` / `wgoDataList` | Filter = `Definition.inventorySize != 0 && Definition.OpenInMultiInventory` — exactly what the chest window's "storage" panel shows (`new MultiInventory(zone, excludeWgo)`) |
| All world objects | `MainGame.WorldData.Cache.wgoDataByUidCache` (`Dictionary<Guid, WgoData>`) | For radius fallback. Filter `WorldId == currentGameSceneId`, `!IsHidden`, `!isTempObject` |
| World object | `WgoData` | `Inventory` (type `Inventory`), `Definition` (`WGODef`), `Position`, `UniqueId`, `IsInteractable`, `WorldZoneData` |
| Container def | `WGODef` | `inventorySize`, `inventoryWhiteList`, `inventoryBlackList`, `OpenInMultiInventory`, `conveyorType`, `refToOtherWgoInventory` (shared inventories → dedupe) |
| Inventory | `Inventory` → `.Data` is an `Item` holding `List<Item> Inventory` | `AddItemToInventory`, `RemoveItemById`, `CanAddItemToInventory`, `TakeAllItemsExistingInMeFromOtherInventory(other, ignoreMyBags, ignoreOtherBags)` |
| Item | `Item` | `id`, `Count`, `IsBag`, `Definition` (`ItemDef`: `stackCount`, `isTool`, `isWeapon`, `isBag`, `isQuestItem`, `CanItemBeEquipped()`) |
| Built-in "move similar" | `UIBaseChestWindowData.OnMoveAllSimilarItemFromPlayerToChest` → `chestInv.TakeAllItemsExistingInMeFromOtherInventory(playerInv)` bound to `GameKey.MoveAllItemsFromPlayer` | **Reuse this algorithm.** Only moves `stackCount > 1` items whose id already exists in target |
| Defs registry | `GameBalance.Me.wgoDefs`, `GameBalance.Me.itemDefs` | For the discovery dump |
| Input | `LazyInput.GetKeyDown(GameKey)`, `LazyInput.IsInputActive()`, `LazyInput.IsGamepadActive` | Our hotkey uses BepInEx `KeyboardShortcut` (legacy `Input`) |
| Keyboard bindings | `LazyInput.GameBindings.keyBindings` (`List<KeyBinding>`: `gameKey`, `keyCode`, `additionalKeyCodes`), polled by `KeyboardController` via legacy `Input`. **Not Rewired** (Rewired = gamepad only). Saved in PlayerPrefs `settings` JSON (`keyboardKeybindings`) | Decoded 2026-09-26 defaults: W/S/A/D move, E Interaction, F Action, R Rotate+ChangeWeapon, T TechTree, I Inventory, P Inspirations, M Map, **Q QuestTree**, Tab CharacterWindow, 1-4 hotbar, Space Attack/Select, LShift AttackFocus, Esc menu. **Free letters:** G H J K L Z X C V B N O U Y. F9 free (game uses F1, F5, F10-F12). Other mods: MoveBuilding uses Tab (CharacterWindow). |
| Windows | `LazyUI.GetAllWindows()` → `LazyWindow.IsShown` | Skip stash when any window is open |
| Feedback | `LazyAudio.PlayAndForget("item_put")`, `LazySingleton<UINotificator>.Instance.ShowSimpleTextNotification(localeKey)` | |
| Pause | `MainGame.IsGamePaused` | |

## 3. Design

- **Source** = `PlayerData.inventory` (backpack) only. **Decided 2026-09-26:** never `toolBeltInventory` (equipped tools), never equipped gear, never the overhead carried item.
- **Targets** = zone containers (above filter), sorted by distance to player. Fallback when zone is null: radius scan (config, default 12 units).
- **Strategy** (config enum):
  1. `SimilarOnly` *(default)* — per container: `container.Inventory.TakeAllItemsExistingInMeFromOtherInventory(player.inventory, true, true)`. Matches vanilla button semantics; white/black lists enforced by `AddItemToInventory` (verify in Phase 2).
  2. `SimilarThenAny` — pass 1 as above, pass 2 dumps remaining stackables into any container with space.
- **Skip rules** (config, all default on): bags themselves, `isQuestItem`, `isTool`/`isWeapon`, hotbar-pinned ids.
- **Gates**: game loaded (`MainGame.PlayerData != null`), `PlayerController.IsControlsEnabled`, `!MainGame.IsGamePaused`, `LazyInput.IsInputActive()`, no `LazyWindow.IsShown`, co-op → host only.
- **Feedback**: `item_put` sound + BepInEx log line `Stashed N items into M containers`; notification if a locale-less text path exists (check Phase 3).
- **No Harmony patches needed** for v1. Everything is reachable through public API from a `MonoBehaviour.Update` poll.

## 4. Project layout

```
GraveyardKeeper2Mods/
  GraveyardKeeper2Mods.sln
  Directory.Build.props          GameDir / BepInExDir props, shared refs, LangVersion latest
  .gitignore                     bin/ obj/ decompiled/
  tools/decompile.ps1            ilspycmd → decompiled/
  QuickStash/
    QuickStash.csproj            netstandard2.1, refs: BepInEx.dll, 0Harmony.dll, UnityEngine*.dll,
                                 Assembly-CSharp.dll, LazyBearTechnology.dll; post-build copy →
                                 $(GameDir)\BepInEx\plugins\QuickStash\
    Plugin.cs                    BaseUnityPlugin: config, Update() hotkey poll
    ContainerFinder.cs           zone + radius discovery, dedupe shared inventories
    Stasher.cs                   strategies, skip rules, result struct
    Diagnostics.cs               dump to BepInEx/QuickStash-dump.txt
    README.md
  PLAN-QuickStash.md
```

## 5. Phases (each ends with a build + in-game check)

| # | Goal | Deliverable | Done when |
|---|---|---|---|
| 0 | Toolchain | .NET 8 SDK, sln/csproj, post-build copy, BepInEx console on, decompile script, git init | Empty plugin logs `QuickStash loaded` in `BepInEx/LogOutput.log` |
| 1 | **Container discovery** | Debug key `F9` dumps: scene id, position, current zone id/type/rect; every zone WGO with inventory (id, uid, dist, size, OpenInMultiInventory, white/black list, item count); radius-scan result side by side; all `wgoDefs` with `inventorySize>0` grouped by `OpenInMultiInventory`; Rewired keyboard map (is `Q` taken?) | Dump list == chest window "storage" panel in ≥4 areas (yard, church, morgue, garden). `Q` conflict known |
| 2 | **Core stash** | `SimilarOnly` on `F9`; per-container log of moved items | Item totals conserved (before == after); white/black-listed containers respected; bags + toolbelt untouched; survives save/load |
| 3 | **Hotkey + UX** | Configurable key, **default `G`** (Q = QuestTree, R = Rotate/ChangeWeapon, decided 2026-09-26), all gates, sound, config: radius, conveyor toggle, dry-run. Skip rules dropped: vanilla `TakeAll…` only moves stackables, so tools/weapons/bags never move | Works in normal play; never fires with a window open or while paused |
| 4 | Extras (optional) | `SimilarThenAny`; gamepad binding via Rewired; conveyor chests toggle; packaging (README, manifest, Nexus/Thunderstore zip) | |

## 6. Risks / open questions

| Risk | Mitigation |
|---|---|
| "Items in my hand" ambiguity | **Resolved:** backpack only. Toolbelt (equipped tools) and overhead item are excluded, not optional. |
| `Q` already bound in Rewired | Phase 1 dump. Default may become `Q` anyway since BepInEx key polling bypasses Rewired; conflict only matters if the game action also fires |
| `TakeAll…` moves only `stackCount > 1` items | Intended (mirrors vanilla). Non-stackables via `SimilarThenAny` if wanted |
| White/black lists enforced only in UI? | **Resolved (source check):** `Item.CanAddItemCountToInventory` enforces `WhiteListFilter/BlackListFilterSerializedItemProperty`, so vanilla `TakeAll…` respects them. Still confirm once in-game (Phase 2). |
| Zone null (player between zones) | Radius fallback |
| `refToOtherWgoInventory` shared inventories | Dedupe containers by `Inventory` reference |
| Conveyor chests (`conveyorType Chest/ChestOut`) | Config toggle, off by default |
| Co-op desync | Host-only guard, log warning on client |
| HUD/chest UI not refreshing | Skipped while windows open; `Inventory.OnItemsAdd/Remove` events fire from `AddItemToInventory` |
