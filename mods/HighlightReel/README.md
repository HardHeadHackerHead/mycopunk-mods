# 📸 Quad's Highlight Reel

**Your best moments, caught for you: boss kills, multi-kills, close calls and comebacks are photographed as they happen and shown as a highlight reel on the mission reward screen. Point at one to see it big. They're saved to your Pictures folder too.**

![version: 0.1.0](https://img.shields.io/badge/version-0.1.0-3b6e8f?style=flat-square)  ![multiplayer: only you](https://img.shields.io/badge/multiplayer-only%20you-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

[Source code](https://github.com/HardHeadHackerHead/mycopunk-mods/tree/main/mods/HighlightReel)  ·  [Report a bug](https://github.com/HardHeadHackerHead/mycopunk-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Highlight%20Reel%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/EwTjQxEZPy)  [![Buy me a coffee](https://img.shields.io/badge/Buy_me_a-coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/quadh)

<!-- GITHUB-ONLY -->**Install:** see [Install](../../README.md#-install) on the main page: BepInEx, then this mod's DLL in `BepInEx\plugins` (or `BepInEx\scripts` with ScriptEngine).<!-- END GITHUB-ONLY -->

## ✨ Features

You don't have to remember to take screenshots: the mod watches for the moments worth keeping and takes the picture for you, a split second after, so the hit is in it.

### What it catches

| Moment | Caption |
|---|---|
| You kill a boss | **BOSS DOWN** and its name |
| You kill an Abomination | **ABOMINATION DOWN** |
| 3 kills or more within 2 seconds | **TRIPLE KILL**, **QUAD KILL**, **RAMPAGE ×6**... (one picture, its caption going up as the streak does) |
| Your health dips below 15% and you live through it | **CLOSE CALL** (kept only if you're still up a few seconds later) |
| You're brought back up | **BACK IN THE FIGHT** |

### How it works

- **On the reward screen.** After the mission, under your XP, a **HIGHLIGHTS** strip shows the mission's pictures in the order they happened, each framed in its colour: gold for bosses, cyan for multi-kills and close calls.
- **Point at one to see it big.** It opens over the middle of the screen, with its caption, until you point away.
- **Saved for you.** Each picture is also saved as a PNG in your **Pictures** folder, under **Mycopunk Highlights** (or a folder of your own, or not at all: see the settings).
- **The best six.** A mission keeps up to six (bosses first when there are more), and a new mission starts a new reel.
- **No stutter.** The pictures are taken on the graphics card and saved in the background, so play doesn't hitch.

## 📸 Screenshots

**The reward screen's highlight reel** (from the mod's test, in the hub: in a mission, they're your fights)

<img src="images/1.jpg" alt="The XP column with a HIGHLIGHTS strip of three captioned pictures: BOSS DOWN: TEST, TRIPLE KILL, CLOSE CALL" width="100%">

**Pointing at one shows it big**

<img src="images/2.jpg" alt="A highlight shown big over the reward screen, captioned TRIPLE KILL" width="100%">

## 👥 Multiplayer

**Who needs it:** Only you. It photographs your own screen and your own moments.

## ⚙️ Settings

In `BepInEx\config\quadslab.mycopunk.highlightreel.cfg`, made the first time the game runs with the mod. r2modman and Gale edit it under **Config editor**.

**Moments**

| Setting | Default | What it does |
|---|---|---|
| `Bosses` | `true` | A picture when you kill a boss or an Abomination. |
| `MultiKills` | `true` | A picture when you kill several enemies in quick succession. |
| `MultiKillCount` | `3` | How many kills make a multi-kill. |
| `MultiKillWindow` | `2` | Seconds between kills that still count as one multi-kill. |
| `CloseCalls` | `true` | A picture when your health dips very low and you live through it. |
| `CloseCallHealth` | `0.15` | How low is "very low" (0.15 = 15% of your health). |
| `Revives` | `true` | A picture when you're brought back up. |

**Reel**

| Setting | Default | What it does |
|---|---|---|
| `MaxShots` | `6` | How many pictures a mission keeps (the best ones, bosses first). |
| `SaveToDisk` | `true` | Also save each picture as a PNG. |
| `Folder` | (empty) | Where to save them; empty means your Pictures folder, in "Mycopunk Highlights". |

**Debug**

| Setting | Default | What it does |
|---|---|---|
| `TestKey` | `false` | **F8** in the hub takes three sample pictures and opens the reward screen with them (never saved; only in the hub). |

A moment it should have caught, or one it shouldn't have? [Tell us](https://github.com/HardHeadHackerHead/mycopunk-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Highlight%20Reel%5D%20).

## 🤖 Made with AI

Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game. Its cover and icon were made with AI image generation; the screenshots are from the game (the mod's test, in the hub).

## 💬 Join us on Discord

<a href="https://discord.gg/EwTjQxEZPy"><img src="https://raw.githubusercontent.com/HardHeadHackerHead/mycopunk-mods/main/docs/images/discord-banner.jpg" alt="Join the Quads Lab Discord" width="100%"></a>

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

<a href="https://buymeacoffee.com/quadh"><img src="https://raw.githubusercontent.com/HardHeadHackerHead/mycopunk-mods/main/docs/images/support-banner.jpg" alt="Support my mods: buy me a coffee" width="100%"></a>

Every Quads Lab mod is free, and it stays that way. If one of them made your game better and you'd like to say thanks, a coffee
helps me make new mods and keep improving the ones you already play. It's 100% optional, and always appreciated!

**[☕ Buy me a coffee →](https://buymeacoffee.com/quadh)**

## 📜 Recent changes

<!-- RECENT-CHANGES -->
- **0.1.0** First release: pictures of your best moments (bosses, multi-kills, close calls, revives) on the mission reward screen, big when you point at them, and saved to your Pictures folder.
<!-- END RECENT-CHANGES -->

[Every change](CHANGELOG.md)

Unofficial fan mod, not affiliated with Pigeons at Play. License: source-available (see [LICENSE.md](LICENSE.md)).
