# No Blasphemy

![version: 1.0.0](https://img.shields.io/badge/version-1.0.0-3b6e8f?style=flat-square)  ![multiplayer: only you](https://img.shields.io/badge/multiplayer-only%20you-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

[Source code](https://github.com/HardHeadHackerHead/mycopunk-mods/tree/main/mods/NoBlasphemy)  ·  [Report a bug](https://github.com/HardHeadHackerHead/mycopunk-mods/issues/new?template=bug.yml&title=%5BNo%20Blasphemy%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/EwTjQxEZPy)  [![Buy me a coffee](https://img.shields.io/badge/Buy_me_a-coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/quadh)

Takes **God's name used in vain** out of Mycopunk's dialogue. The crew, Roachard, Hunk and the rest stop using the Lord's
name as a curse or an exclamation, in what you hear and in what you read.

- **Subtitles are cleaned.** The name becomes a harmless word: "oh my G--!" reads "oh my gosh!", "thank G--" reads
  "thank goodness", and the G-- d--- curse reads "dang".
- **Those spoken lines go quiet.** A recording can't lose a single word, so a line that uses the name is silenced and only
  its cleaned subtitle shows. Conversations and mission chatter carry on as normal around it.
- **Respectful lines stay.** When a character speaks of God sincerely (the Wrangler's line about faith), it's left as it is.

It finds the lines by their words, so new dialogue from game updates is covered too. Right now that's 21 lines across the
crew, mission control and the side characters. Other rough language (the milder swearing) is left alone: this mod is only
about God's name.

**Multiplayer:** client only. It changes only what you hear and read, so only you need it, and you can play with anyone.

## Settings

In `BepInEx\config\quadslab.mycopunk.noblasphemy.cfg` (or your mod manager's config editor), after the first start:

| Setting | Default | What it does |
|---|---|---|
| `General / MuteVoice` | `true` | Silence the spoken lines. Off: you'd still hear them, with clean subtitles. |
| `General / CleanSubtitles` | `true` | Clean the subtitles (English). |
| `Lines / Keep` | `exch_faith_5` | Line IDs to leave alone, comma-separated. |
| `Lines / Extra` | (empty) | More line IDs to clean and silence, for a line the word list misses. |
| `Debug / LogLines` | `false` | Write each changed line, with its ID, to `BepInEx\LogOutput.log`. |

Subtitles in other languages: the voice lines are still silenced, but only the English text is rewritten.

Heard one we missed? [Tell us](https://github.com/HardHeadHackerHead/mycopunk-mods/issues/new?template=bug.yml&title=%5BNo%20Blasphemy%5D%20)
which character said it and roughly when, and we'll add it.

<!-- GITHUB-ONLY -->
## Install

With a mod manager (**r2modman** or **Gale**): find **No Blasphemy** under Mycopunk and install it; it brings BepInEx with it.

By hand: install [BepInExPack for Mycopunk](https://thunderstore.io/c/mycopunk/p/BepInEx/BepInExPack_Mycopunk/), then
unzip the release zip into the game's `BepInEx\plugins\` folder, so you get `BepInEx\plugins\NoBlasphemy\NoBlasphemy.dll`.
<!-- END GITHUB-ONLY -->

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
