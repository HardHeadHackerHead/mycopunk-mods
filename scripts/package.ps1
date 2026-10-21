# Builds each mod's manual-install download for a GitHub release:
#   build\<Name>-<ver>.zip    unzip into the game's BepInEx\plugins\ (you get BepInEx\plugins\<Name>\<Name>.dll)
#   .\scripts\package.ps1 [-GameDir ...] [-Mod <Name>]
# The mod-site (Thunderstore) packages are python scripts\store.py's.
param([string]$GameDir = "", [string]$Mod = "")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot "build.ps1") -GameDir $GameDir -Mod $Mod
$out = Join-Path $root "build"

foreach ($dir in Get-ChildItem (Join-Path $root "mods") -Directory) {
    $name = $dir.Name
    if ($Mod -and $name -ne $Mod) { continue }
    $dll = Join-Path $dir.FullName "bin\Release\netstandard2.1\$name.dll"
    if (-not (Test-Path $dll)) { "skipped $name (no $name.dll)"; continue }
    # the version: a Version constant, or [BepInPlugin(..., "1.0.0")]
    $src = (Get-ChildItem $dir.FullName -Filter "*.cs" | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    $m = [regex]::Match($src, 'const string Version = "([^"]+)"')
    if (-not $m.Success) { $m = [regex]::Match($src, 'BepInPlugin\([^)]*"(\d+\.\d+\.\d+)"\)') }
    if (-not $m.Success) { throw "$name has no version ([BepInPlugin(..., ""1.0.0"")] or a Version constant)" }
    $version = $m.Groups[1].Value
    $license = Join-Path $dir.FullName "LICENSE.md"
    if (-not (Test-Path $license)) { throw "$name has no LICENSE.md: every mod carries its license" }
    $stage = Join-Path $out "package-mods\$name"
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force "$stage\$name" | Out-Null
    Copy-Item $dll "$stage\$name\"
    foreach ($doc in "README.md", "CHANGELOG.md", "LICENSE.md") { if (Test-Path (Join-Path $dir.FullName $doc)) { Copy-Item (Join-Path $dir.FullName $doc) "$stage\$name\" } }
    $zip = Join-Path $out "$name-$version.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path "$stage\*" -DestinationPath $zip
    "{0}  ({1} KB)" -f $zip, [int]((Get-Item $zip).Length / 1KB)
}
