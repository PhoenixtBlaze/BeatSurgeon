# Saber Surgeon

Saber Surgeon is a Beat Saber mod inspired by [GameplayModifiersPlus](https://github.com/Kylemc1413/GameplayModifiersPlus) and [StreamPartyCommands](https://github.com/denpadokei/StreamPartyCommand) it lets your Twitch chat directly mess with your gameplay in real time. Viewers can trigger rainbow notes, disappearing arrows, ghost notes, bomb “pranks”, flashbangs, and speed changes using simple chat commands, while you keep full control over what is allowed and how often it can happen.



## Screenshots

<img width="896" height="573" alt="image" src="https://github.com/user-attachments/assets/83886575-e008-442e-97e0-5237f45d5129" />




<img width="951" height="668" alt="image" src="https://github.com/user-attachments/assets/08f26977-cb2c-48e8-b793-c46347b69a11" />




## Features

- **Twitch chat integration via ChatPlexSDK**
  - Hooks into ChatPlex’s chat service and subscribes to messages and loading events at game start.

- **Gameplay “surgeon” tools for chat**
  - `!rainbow` – enable rainbow / RGB note colors for a limited time.
  - `!ghost` – ghost notes: cubes disappear and only arrows show briefly before hiding themselves.
  - `!disappear` – disappearing arrows on normal notes.
  - `!bomb` (or a custom alias) – arms the next cube as a “bomb” that explodes and shows the viewer’s name when cut.
  - `!faster`, `!superfast`, `!slower` – change song speed for a short duration, with an option to allow only one speed command at a time.
  - `!flashbang` – briefly overexposes lights for a flashbang effect.




## Requirements

- Beat Saber on PC (Steam or Oculus).
- [BeatSaberPlus](https://github.com/hardcpp/BeatSaberPlus) installed and Chat integration enabled so the ChatPlex chat service is available.



## Installation

-To Be Decided


## Usage

### Connecting Twitch chat

Saber Surgeon uses ChatPlexSDK’s chat service rather than talking to Twitch IRC directly.

- Configure your Twitch connection inside ChatPlex / BSPlus as you normally would.
- Saber Surgeon waits for the ChatPlex chat service to be ready, then subscribes to incoming text messages and exposes a send API for responses.
- Any chat line starting with `!` is forwarded to `CommandHandler.ProcessCommand`, which decides what to do.

### Viewer commands

By default, Saber Surgeon responds to:

- `!surgeon` – simple “mod is active” status message.
- `!help` – prints a short list of available commands.
- `!rainbow` – enables rainbow / RGB note colors for 30 seconds.
- `!ghost` – enables ghost notes for 30 seconds (blocks if certain other effects are active).
- `!disappear` – enables disappearing arrows for 30 seconds.
- `!bomb` (and its alias) – arms the next eligible note as a fake bomb that explodes and shows the viewer’s name when cut.
- `!faster`, `!superfast`, `!slower` – temporarily change song speed; can be configured so only one speed effect is active at a time.
- `!flashbang` – triggers a short flashbang light effect.
- `!ping`, `!test`, `!bsr` – utility or test commands.

Each command can have its own cooldown and can be individually enabled or disabled.

### Host configuration

All configuration is available via the in‑game UI:

- **Cooldowns panel**
  - Global cooldown toggle and seconds.
  - “Use per‑command cooldowns” toggle:
    - Off → all commands use the global cooldown.
    - On → each command uses its own seconds value.
  - Per‑command sliders for:
    - Rainbow, Disappear, Ghost, Bomb, Faster, SuperFast, Slower, Flashbang.
  - Speed exclusivity toggle (“ Make it so that only one speed command at a time can be active ”).

- **Custom bomb command name**
  - The Bomb row includes a text field where you can change the bomb command (for example, `!bomb` → `!boop`).
  - Whatever you enter is saved and used at runtime
