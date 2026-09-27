# Regenerates decompiled/ from the game's managed assemblies (~1 min). Requires ilspycmd on PATH.
param(
  [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Graveyard Keeper 2"
)
$ErrorActionPreference = "Stop"
$M = Join-Path $GameDir "GraveyardKeeper2_Data\Managed"
$Root = Split-Path -Parent $PSScriptRoot
foreach ($a in "Assembly-CSharp", "LazyBearTechnology", "Assembly-CSharp-firstpass") {
  $out = Join-Path $Root "decompiled\$a"
  Write-Host "Decompiling $a -> $out"
  ilspycmd --disable-updatecheck -p -o $out -r $M (Join-Path $M "$a.dll")
}
