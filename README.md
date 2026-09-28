# CaioToOn GK2 Mods

BepInEx 5 mods for **Graveyard Keeper 2** (Unity 6, Mono). Each mod is its own project and
builds to its own plugin folder.

| Mod | What it does | Default key |
|---|---|---|
| [QuickStash](QuickStash/README.md) | One key moves stackable backpack items into nearby containers that already hold them | `G`, gamepad `R2 + L3` |

## Layout

```
Directory.Build.props   shared MSBuild props: GameDir, ManagedDir, BepInExDir, compiler settings
Directory.Build.targets shared build steps: zip package into dist/, optional direct deploy
GraveyardKeeper2Mods.sln
tools/decompile.ps1     regenerates decompiled/ (git-ignored) from the game assemblies with ilspycmd
<Mod>/<Mod>.csproj      one project per mod; post-build copies the DLL to <game>\BepInEx\plugins\<Mod>\
```

## Requirements

- Graveyard Keeper 2 (Steam) with [BepInEx 5.4.23+](https://github.com/BepInEx/BepInEx/releases) installed in the game folder
- .NET SDK 8 (`winget install Microsoft.DotNet.SDK.8`)
- Optional: `ilspycmd` (`dotnet tool install -g ilspycmd`) to regenerate `decompiled/`

## Build

```powershell
dotnet build -c Release                 # all mods
dotnet build -c Release QuickStash      # one mod
```

If the game is not in the default Steam folder, pass the path:

```powershell
dotnet build -c Release -p:GameDir="D:\Games\Graveyard Keeper 2"
```

Each build writes `dist\<Mod>-<Version>.zip`, laid out from the game root
(`BepInEx\plugins\<Mod>\<Mod>.dll`).

### Install with Vortex

Mods tab → **Install From File** → pick `dist\<Mod>-<Version>.zip` → Enable → Deploy. To update, install
the new zip and choose "Replace".

### Direct deploy (no Vortex)

```powershell
dotnet build -c Release -p:DeployToGame=true
```

copies the DLL straight into `$(GameDir)\BepInEx\plugins\<Mod>\`. Close the game first if the copy reports
the DLL is in use.

If BepInEx is not deployed in the game folder at build time, point the compiler at any copy of its `core`
folder: `-p:BepInExCoreDir="<path>\BepInEx\core"`.

## Adding a mod

1. `dotnet new classlib -n <Mod> -f netstandard2.1`, then replace the csproj with a copy of
   `QuickStash/QuickStash.csproj` and adjust `AssemblyName`, `RootNamespace`, `Version`, `PluginOutDir`.
2. `dotnet sln add <Mod>/<Mod>.csproj`.
3. Plugin GUID convention: `com.caiotoon.gk2.<mod>`.

## Game facts worth knowing

- Keyboard bindings are the game's own `GameBindings` list (not Rewired). Free letters as of Sept 2026:
  G H J K L Z X C V B N O U Y. `Q` is Quest Tree, `R` is Rotate / Change Weapon.
- `PlayerData.CurrentWorldZoneData` is the "area"; its containers with `OpenInMultiInventory` are what the
  chest window shows as zone storage.
- Item icons come from `EasySpritesCollection.GetSprite(ItemDef.iconId)`; TextMeshPro sprite tags only
  cover a few HUD glyphs.

## License

[MIT](LICENSE). Graveyard Keeper 2 and its assets belong to Lazy Bear Games; this repository contains no game code or assets apart from screenshots used for the mod page.
