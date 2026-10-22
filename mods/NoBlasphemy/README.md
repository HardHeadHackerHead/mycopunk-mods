# 🙏 No Blasphemy

<img src="icon.png" alt="No Blasphemy" width="200">

**Clean dialogue for Mycopunk: takes God's name used in vain out of what the characters say. The subtitles are cleaned, and those spoken lines go quiet. Lines that speak of God with respect stay.**

![version: 1.0.0](https://img.shields.io/badge/version-1.0.0-3b6e8f?style=flat-square)  ![multiplayer: only you](https://img.shields.io/badge/multiplayer-only%20you-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

[Source code](https://github.com/HardHeadHackerHead/mycopunk-mods/tree/main/mods/NoBlasphemy)  ·  [Report a bug](https://github.com/HardHeadHackerHead/mycopunk-mods/issues/new?template=bug.yml&title=%5BNo%20Blasphemy%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/EwTjQxEZPy)  [![Buy me a coffee](https://img.shields.io/badge/Buy_me_a-coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/quadh)

<!-- GITHUB-ONLY -->**Install:** see [Install](../../README.md#-install) on the main page: BepInEx, then this mod's DLL in `BepInEx\plugins` (or `BepInEx\scripts` with ScriptEngine).<!-- END GITHUB-ONLY -->

## ✨ Features

The crew, Roachard, Hunk and the rest stop using the Lord's name as a curse or an exclamation, in what you hear and in what you read.

### How it works

- **Subtitles are cleaned.** The name becomes a harmless word: "oh my G--!" reads "oh my gosh!", "thank G--" reads "thank goodness", and the G-- d--- curse reads "dang".
- **Those spoken lines go quiet.** A recording can't lose a single word, so a line that uses the name is silenced and only its cleaned subtitle shows. Conversations and mission chatter carry on around it at the normal pace.
- **Respectful lines stay.** When a character speaks of God sincerely (the Wrangler's line about faith), it's left as it is.
- **New dialogue is covered too.** The mod finds the lines by their words, so lines added by game updates are caught. Right now that's 21 lines across the crew, mission control and the side characters.
- **Only God's name.** The milder swearing is left alone.

## 👥 Multiplayer

**Who needs it:** Only you. It changes only what you hear and read, so you can play with anyone, with or without it.

## 🌍 Languages

The voice lines are silenced whatever language the game is in. The subtitles are rewritten in **English**; in other languages the translated subtitle shows as the game has it.

## ⚙️ Settings

In `BepInEx\config\quadslab.mycopunk.noblasphemy.cfg`, made the first time the game runs with the mod. r2modman and Gale edit it under **Config editor**.

**General**

| Setting | Default | What it does |
|---|---|---|
| `MuteVoice` | `true` | Silence the spoken lines. Off: you'd still hear them, with clean subtitles. |
| `CleanSubtitles` | `true` | Clean the subtitles (English). |

**Lines**

| Setting | Default | What it does |
|---|---|---|
| `Keep` | `exch_faith_5` | Line IDs to leave alone, comma-separated. |
| `Extra` | (empty) | More line IDs to clean and silence, for a line the word list misses. |

**Debug**

| Setting | Default | What it does |
|---|---|---|
| `LogLines` | `false` | Write each changed line, with its ID, to `BepInEx\LogOutput.log`, and each line as it's silenced. |
| `TestKey` | `false` | **F8** in a game plays the next changed line, as mission control would say it: you see its cleaned subtitle and hear nothing. |

Heard one we missed? [Tell us](https://github.com/HardHeadHackerHead/mycopunk-mods/issues/new?template=bug.yml&title=%5BNo%20Blasphemy%5D%20)
which character said it and roughly when, and we'll add it.

## 🤖 Made with AI

Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game.

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
- **1.0.0** First release: God's name used in vain taken out of the dialogue, cleaned in the subtitles and silenced in the voice lines.
<!-- END RECENT-CHANGES -->

[Every change](CHANGELOG.md)

Unofficial fan mod, not affiliated with Pigeons at Play. License: source-available (see [LICENSE.md](LICENSE.md)).
