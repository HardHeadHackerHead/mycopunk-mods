# Starts a new mod in mods\<Name>\ with everything a release needs: the project, a plugin, README, CHANGELOG, LICENSE and
# store.json. Then: write it, add icon.png (256 x 256), build with .\scripts\build.ps1 -Mod <Name> -Deploy, and add it to the
# table in the main README.
#   .\scripts\new-mod.ps1 -Name RoachardRadio -Title "Roachard Radio" -Description "What it does, in the words players search for."
param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Title, [string]$Description = "TODO: what it does")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ($Name -notmatch '^[A-Za-z][A-Za-z0-9]*$') { throw "-Name: letters and digits only, like RoachardRadio" }
$dir = Join-Path $root "mods\$Name"
if (Test-Path $dir) { throw "mods\$Name already exists" }
New-Item -ItemType Directory $dir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding $false
function Write-File($file, $text) { [IO.File]::WriteAllText((Join-Path $dir $file), $text.Replace("`r`n", "`n"), $utf8) }

Write-File "$Name.csproj" @"
<Project Sdk="Microsoft.NET.Sdk">
  <!-- $Title. The shared settings and references are in ..\Directory.Build.props; add any other game DLL it needs here:
  <ItemGroup>
    <Reference Include="`$(Managed)\Unity.TextMeshPro.dll" Private="false" />
  </ItemGroup>
  -->
</Project>
"@

$guid = "quadslab.mycopunk." + $Name.ToLowerInvariant()
Write-File "Plugin.cs" @"
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace $Name
{
    [BepInPlugin(Guid, "$Title", Version)]
    [BepInProcess("Mycopunk.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "$guid";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("$Title " + Version + " loaded");
        }
    }
}
"@

Write-File "CHANGELOG.md" @"
## 0.1.0

- First release.
"@

Copy-Item (Join-Path $root "LICENSE.md") (Join-Path $dir "LICENSE.md")

$storeName = ($Title -replace '[^A-Za-z0-9 ]', '').Trim() -replace '\s+', '_'
$store = [ordered]@{
    team = "Quads_Lab"; name = $storeName; description = $Description
    website = "https://github.com/HardHeadHackerHead/mycopunk-mods/tree/main/mods/$Name"
    dependencies = @("BepInEx-BepInExPack_Mycopunk-5.4.2403")
    files = @("mods/$Name/bin/Release/netstandard2.1/$Name.dll"); plugin_folder = $Name; icon = "mods/$Name/icon.png"
    thunderstore = [ordered]@{ community = "mycopunk"; categories = @("client", "mods") }
}
Write-File "store.json" (($store | ConvertTo-Json -Depth 5) + "`n")

$readme = Get-Content (Join-Path $root "mods\NoBlasphemy\README.md") -Raw
$common = $readme.Substring($readme.IndexOf("## 🤖 Made with AI"))
$common = $common -replace "(?s)<!-- RECENT-CHANGES -->.*?<!-- END RECENT-CHANGES -->", "<!-- RECENT-CHANGES -->`n- **0.1.0** First release.`n<!-- END RECENT-CHANGES -->"
$titleUrl = [uri]::EscapeDataString($Title)
Write-File "README.md" @"
# $Title

![version: 0.1.0](https://img.shields.io/badge/version-0.1.0-3b6e8f?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

[Source code](https://github.com/HardHeadHackerHead/mycopunk-mods/tree/main/mods/$Name)  ·  [Report a bug](https://github.com/HardHeadHackerHead/mycopunk-mods/issues/new?template=bug.yml&title=%5B$titleUrl%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/EwTjQxEZPy)  [![Buy me a coffee](https://img.shields.io/badge/Buy_me_a-coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/quadh)

TODO: what it does, its keys and settings (``BepInEx\config\$guid.cfg``).

$common
"@
"mods\$Name ready. Next: write it, add mods\$Name\icon.png (256 x 256), .\scripts\build.ps1 -Mod $Name -Deploy, and a row in README.md's table."
