# Builds every mod in mods\. Throws on failure.
#   .\scripts\build.ps1 [-GameDir <game folder>] [-Deploy] [-PluginsDir <BepInEx\plugins folder>] [-Mod <Name>]
#   -GameDir:    the game (its Mycopunk_Data\Managed folder); default: $env:MYCO_GAMEDIR, else the Steam default below
#   -Deploy:     also copy each mod into BepInEx\plugins\<Name>\ for testing
#   -PluginsDir: the BepInEx\plugins folder -Deploy copies into; default: the game's own. For a mod manager profile, e.g.
#                "$env:APPDATA\r2modmanPlus-local\Mycopunk\profiles\Default\BepInEx\plugins"
#   -Mod:        only this mod (its folder name under mods\)
param([string]$GameDir = "", [switch]$Deploy, [string]$PluginsDir = "", [string]$Mod = "")
$root = Split-Path $PSScriptRoot -Parent
if (-not $GameDir) { $GameDir = $(if ($env:MYCO_GAMEDIR) { $env:MYCO_GAMEDIR } else { "D:\SteamLibrary\steamapps\common\Mycopunk" }) }
if (-not (Test-Path (Join-Path $GameDir "Mycopunk_Data\Managed\Assembly-CSharp.dll"))) { throw "Mycopunk not found at ${GameDir}: pass -GameDir or set MYCO_GAMEDIR" }
$props = @("-p:GameDir=$GameDir", "-p:DeployMods=$(if ($Deploy) { 'true' } else { 'false' })")
if ($PluginsDir) { $props += "-p:PluginsDir=$PluginsDir" }
if ($Deploy -and -not $PluginsDir -and -not (Test-Path (Join-Path $GameDir "BepInEx"))) {
    throw "BepInEx isn't installed in $GameDir (install BepInExPack_Mycopunk), or pass -PluginsDir for a mod manager profile"
}
$projects = Get-ChildItem (Join-Path $root "mods") -Recurse -Filter "*.csproj" | Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" }
if ($Mod) { $projects = $projects | Where-Object { $_.BaseName -eq $Mod }; if (-not $projects) { throw "no mod named $Mod in mods\" } }
foreach ($proj in $projects) {
    $out = dotnet build $proj.FullName -c Release -nologo @props 2>&1
    $errors = $out | Select-String " error " | Select-Object -Unique
    if ($LASTEXITCODE -ne 0 -or $errors) {
        $errors | ForEach-Object { $_.Line }
        throw "build failed: $($proj.Name)"
    }
    "built $($proj.BaseName)$(if ($Deploy) { ' (deployed)' })"
}
"build ok"
