# Working in this repository

BepInEx 5 mods for Graveyard Keeper 2 (Unity 6, Mono). One project per mod. Read the mod's `DESIGN.md`
before changing it.

## Principles

1. **Smallest readable code wins.** Prefer deleting to adding. No clever one-liners, no dense LINQ where a
   loop is clearer. If a change adds lines, say how many and why.
2. **Reuse the game's own logic** before writing new logic. A vanilla method that already does the job
   (moving items, filtering containers, drawing a widget) beats a reimplementation.
3. **No diagnostics, debug or exploration code in a shipped mod.** Throwaway builds for finding facts are
   fine. They are never merged.
4. **Few config options, plain defaults, no value ranges.** Invalid values fall back at the point of use.
   Every option applies while the game runs. Descriptions are written for players, in plain words.
5. **Quiet logs.** One Info line per user action. Warnings only for real failures, each logged once.

## Working rules

6. **A question gets an answer, not code.** "What is the best way to..." asks for approach, size and the
   decisions to make. Change the repository only on an explicit request.
7. **Verify every claim about the game** in `decompiled/` and cite file and line. Data that lives in game
   assets and not in code is stated as unverified until it is checked in game.
8. **Say plainly what has not been run in game.** Nothing is released untested.
9. **Branch and pull request for every change** to `main`. No force-push, no history rewrite.
10. **A separate reviewer looks at each pull request** before merge. Apply the findings that make sense and
    push back on the rest with a reason.
11. **Plan and implementation are separate steps.** A plan is a file in the repository that stands on its
    own for whoever implements it.

## Conventions

- Plugin GUID `com.caiotoon.gk2.<mod>`, target `netstandard2.1`, references taken from the game folder.
- Shared build files at the root: `Directory.Build.props` (paths, compiler settings) and
  `Directory.Build.targets` (zip package, optional direct deploy).
- `dotnet build -c Release` writes `dist/<Mod>-<Version>.zip`, laid out from the game root. Players and the
  owner install it through Vortex. Copying into the game folder needs `-p:DeployToGame=true`.
- `decompiled/` is generated with `tools/decompile.ps1` and is never committed. No game code or game assets
  go into this repository.
- Mod page material (description text, images) is kept outside git.
- A version bump changes the project file and the `BepInPlugin` attribute together.
- A behaviour change updates the mod's `README.md`, its `DESIGN.md` and the mod table in the root
  `README.md` in the same pull request.

## Input pattern

- Keyboard: a BepInEx `KeyboardShortcut` setting, tested as "main key went down and modifiers are held".
  `KeyboardShortcut.IsDown()` is not used because it ignores a press while any other key is held.
- Controller: a setting that names the game's own actions (`GameKey` field names) as a chord, last name
  pressed and the earlier ones held, read through `LazyInput`. After acting, every action raised by the same
  physical button is cleared so the game does not also react.
- Both are checked in a prefix on `PlayerInputHandler.UpdateInput()`, which the game only calls while the
  player is free to act.
- Pitfall: `Enumeration` types (`GameKey`, `GamepadButton`) overload `==` and `!=` to return false when
  either side is null. Test null with `(object)x == null` and compare by `.value`.

## Release checklist

1. Build Release with no warnings.
2. Install the zip through Vortex and test in game, keyboard and controller, including the mod's invariants.
3. Pull request, review, merge.
4. Rebuild on `main`, upload the zip to the mod page with the changelog line, update the page description.
