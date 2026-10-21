<div align="center">

<img src="mods/NoBlasphemy/icon.png" alt="No Blasphemy: a speech bubble with a halo" width="160">

### Mods for Mycopunk, by Quads Lab.

[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/EwTjQxEZPy)  [![Buy me a coffee](https://img.shields.io/badge/Buy_me_a-coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/quadh)

</div>

My public mods for [Mycopunk](https://store.steampowered.com/app/3247750/Mycopunk/), the co-op shooter by Pigeons at Play.

> **Unofficial fan mods.** Not affiliated with or endorsed by Pigeons at Play.

## The mods

| Mod | What it does | Needs |
|---|---|---|
| [**No Blasphemy**](mods/NoBlasphemy) | Takes God's name used in vain out of the dialogue: the subtitles are cleaned, and those spoken lines go quiet. Respectful lines stay. | Nothing else. Client only: only you need it |

Each mod's page has its settings and changelog.

## Install

The easiest way is a mod manager, **r2modman** or **Gale** ([Thunderstore](https://thunderstore.io/c/mycopunk/)): install
the mod and it brings BepInEx with it.

By hand:

1. Install [BepInExPack for Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/) in the game folder.
2. Download the mod's zip from the [releases](https://github.com/HardHeadHackerHead/mycopunk-mods/releases) and unzip it
   into the game's `BepInEx\plugins\` folder, so you get `BepInEx\plugins\<Mod>\<Mod>.dll`.

Settings are in `BepInEx\config\` after the first start.

## Building

You need the .NET SDK and the game. BepInEx and HarmonyX come from NuGet, so nothing has to be installed in the game to build.

```powershell
.\scripts\build.ps1                                   # builds every mod in mods\
.\scripts\build.ps1 -Mod NoBlasphemy -Deploy          # ...one mod, copied into the game's BepInEx\plugins\<Name>\
.\scripts\build.ps1 -Deploy -PluginsDir "<profile>\BepInEx\plugins"    # ...or into a mod manager profile
.\scripts\package.ps1                                 # manual-install zips in build\, for a GitHub release
python scripts\store.py NoBlasphemy                   # the Thunderstore package in build\store\, checked
.\scripts\new-mod.ps1 -Name MyMod -Title "My Mod"     # starts a new mod in mods\MyMod\
```

The game folder defaults to `D:\SteamLibrary\steamapps\common\Mycopunk`; pass `-GameDir` (or set `MYCO_GAMEDIR`) for
yours. Shared build settings are in `mods\Directory.Build.props`, so a mod's own `.csproj` is a few lines.

## License

**Source-available, not open source**: see [LICENSE.md](LICENSE.md) (each mod carries a copy). You may read the code, play,
build it for yourself, contribute, and share the official releases unmodified and free. You may not release modified
versions, copy the code into your own, or sell it, without written permission.

## 🤖 Made with AI

Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game.

## Credits

- **Mycopunk** by [Pigeons at Play](https://store.steampowered.com/app/3247750/Mycopunk/). All game content, names and
  artwork belong to them.
- BepInEx and HarmonyX by their respective authors.
- Made with the help of Claude (Anthropic).

## 💬 Join us on Discord

<a href="https://discord.gg/EwTjQxEZPy"><img src="docs/images/discord-banner.jpg" alt="Join the Quads Lab Discord" width="100%"></a>

Come hang out with the people who make and play these mods. In the **Quads Lab Discord** you can:

- 🛠️ **Get help** and report bugs straight to us
- 👀 **See new mods first**, and help test them before they're released
- 🗳️ **Suggest ideas** and vote on what we make next
- 🎮 **Find people to play with**, and share your adventures
- 🤖 **Talk AI coding and vibe coding** with people building things with AI, the way these mods are made

**[Join the Quads Lab Discord →](https://discord.gg/EwTjQxEZPy)**

## 🧪 About Quads Lab

Quads Lab is where **Quad H** ([HardHeadHackerHead](https://github.com/HardHeadHackerHead)) builds software with AI, of every kind:
these Mycopunk mods are one of its projects. Each mod is designed by a player, written with [Claude Code](https://claude.com/claude-code)
and tested in the game.

- 🌐 [quadslab.io](https://quadslab.io)
- 🧰 [All my Mycopunk mods](https://github.com/HardHeadHackerHead/mycopunk-mods)
- 💬 [Discord](https://discord.gg/EwTjQxEZPy)
- ☕ [Buy me a coffee](https://buymeacoffee.com/quadh)
- 🐞 Found a bug? [Open an issue on GitHub](https://github.com/HardHeadHackerHead/mycopunk-mods/issues/new/choose)

## ☕ Support my mods

<a href="https://buymeacoffee.com/quadh"><img src="docs/images/support-banner.jpg" alt="Support my mods: buy me a coffee" width="100%"></a>

Every Quads Lab mod is free, and it stays that way. If one of them made your game better and you'd like to say thanks, a coffee
helps me make new mods and keep improving the ones you already play. It's 100% optional, and always appreciated!

**[☕ Buy me a coffee →](https://buymeacoffee.com/quadh)**
