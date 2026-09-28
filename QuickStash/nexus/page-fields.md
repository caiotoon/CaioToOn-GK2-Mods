# Nexus page fields for QuickStash

Game: Graveyard Keeper 2 (`https://www.nexusmods.com/graveyardkeeper2`)

| Field | Value |
|---|---|
| Mod name | QuickStash |
| Version | 0.9.0 |
| Category | Gameplay (or "User Interface" if Gameplay is not offered) |
| Language | English |
| Author | caiotoon |
| Brief overview (max 350 chars) | Press G to stash: every stackable item in your backpack, bags included, that a nearby container already holds is moved into it. No windows to open. A bubble above each chest shows what went in. Configurable key, bubble size and columns. Requires BepInEx 5. |
| Detailed description | paste `description.bbcode` |
| Tags | Gameplay, Quality of Life, Inventory, BepInEx |
| Requirements | Add BepInEx through the Nexus requirements field (not in the description) |

## File upload

| Field | Value |
|---|---|
| File | `dist/QuickStash-0.9.0.zip` |
| File name | QuickStash |
| File version | 0.9.0 |
| Category | Main Files |
| Description | Initial release. Extract into the game folder or install with Vortex. |
| Vortex / mod manager download | enabled |

## Images (at least one is required)

0. `images/banner.png`: header art with title (primary image).
1. `images/shot-1-press-g.png`: bubble above a chest right after a stash.
2. Two-column bubble at `BubbleScale = 0.5`.
3. "Stashed N items" notification on screen.
4. Optional: before/after of the backpack.

## Changelog

### 0.9.0
- Controller support: hold R2 and press L3, configurable.

### 0.8.0
- Initial public release.

## After the page exists: automated updates

The Nexus upload API (beta) can update a file on an existing page. It needs:
- an API key: Nexus site → account settings → API keys,
- the file ID: mod page → Files tab → "API Info" on the main file.

Store the key as the repository secret `NEXUSMODS_API_KEY`, then a release workflow can use
`Nexus-Mods/upload-action` with the file ID, `dist/QuickStash-<version>.zip` and the version string.
