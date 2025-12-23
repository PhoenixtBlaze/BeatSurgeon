# Saber Surgeon

Saber Surgeon is a Beat Saber mod that lets your Twitch chat directly interact with your gameplay in real time. Viewers can trigger rainbow notes, custom note colors, disappearing arrows, ghost notes, bomb "pranks", flashbangs, and speed changes using simple chat commands, while you maintain full control over what is allowed and how often effects can occur. Extra features That I really wanted to have in the new version of the game like submit later added to the mod where mod will pause at the end of a solo map to let player decide if they want to submit the score or not.

## Screenshots

### Mod screenshots
<img width="1107" height="766" alt="image" src="https://github.com/user-attachments/assets/4adcdad5-16a6-4e0c-b297-5e9194d1a424" />

<img width="1337" height="803" alt="image" src="https://github.com/user-attachments/assets/b1d54b4e-3a22-465d-9562-88b24db369f7" />

### Twitch Tab Screenshots
<img width="886" height="697" alt="image" src="https://github.com/user-attachments/assets/59105a81-11ba-4bba-b55e-d8a9883d18a6" />

### Supporter Features Screenshots
<img width="1031" height="594" alt="image" src="https://github.com/user-attachments/assets/53faa9a6-3e0b-436c-8569-f34e29474712" />

### Playfirst submit later screenshots
<img width="827" height="633" alt="image" src="https://github.com/user-attachments/assets/e2bd5df7-8fc1-4f12-b2e2-ee48607c09f1" />



## Features

### Core Gameplay Effects
- **`!rainbow`** – Enable rainbow / RGB note colors for a limited time
- **`!notecolor <color1> <color2>`** – Set custom left and right saber note colors (supports names like `red`, `blue` or hex like `#FF0000`; also supports `!notecolor rainbow rainbow`)
- **`!ghost`** – Ghost notes effect: cubes disappear and only arrows show briefly before hiding
- **`!disappear`** – Disappearing arrows on normal notes
- **`!bomb` (or custom alias)** – Arms the next cube as a "bomb" that explodes and displays the viewer's name when cut
- **`!faster`, `!superfast`, `!slower`** – Change song speed for a short duration
  - `!faster` – +20% speed
  - `!superfast` – +50% speed  
  - `!slower` – -15% speed
  - Optional "speed exclusivity" mode: only one speed command active at a time
- **`!flashbang`** – Briefly overexposes lights for a flashbang effect

### Moderator Command Management (NEW)
- **`!surgeon disable`** (Mods/Broadcaster only) – Disable ALL active commands globally at once
- **`!surgeon enable`** (Mods/Broadcaster only) – Re-enable all previously enabled commands
- **`!surgeon <command> disable`** (Mods/Broadcaster only) – Disable a specific command (e.g., `!surgeon rainbow disable`)
- **`!surgeon <command> enable`** (Mods/Broadcaster only) – Enable a specific command (e.g., `!surgeon bomb enable`)
  - Supports: `rainbow`, `notecolor`, `disappear`, `ghost`, `bomb`, `faster`, `superfast`, `slower`, `flashbang`

### Utility Commands
- **`!surgeon`** – Display mod status and available commands (shows `[GLOBALLY DISABLED]` if all commands are off)

### Configuration & Control
- **Per-command toggling** in the mod UI
- **Global and per-command cooldowns** (configurable)
- **Custom bomb command alias** – Change `!bomb` to anything you want (e.g., `!boop`)
- **Speed exclusivity mode** – Prevent multiple speed effects from stacking
- **Moderator-only commands** – Enable/disable controls restricted to mods and broadcaster
- **State persistence** – All settings saved to JSON config

## Requirements

- **Beat Saber** (PC – Steam or Oculus)
- **[BeatSaberPlus](https://github.com/hardcpp/BeatSaberPlus)** with Chat integration enabled (provides ChatPlex chat service)
- **BSIPA** 4.3.6+
- **BeatSaberMarkupLanguage (BSML)** 1.12.5+

## Installation

1. Ensure **BeatSaberPlus** and **ChatPlexSDK_BS** are installed
2. Place `SaberSurgeon.dll` in your `Beat Saber/Plugins/` directory
3. Launch Beat Saber and configure your Twitch connection in BeatSaberPlus settings
4. Access SaberSurgeon settings from the in-game menu

## Usage Guide

### Twitch Chat Integration

Saber Surgeon uses **ChatPlexSDK's chat service** (BeatSaberPlus integration) rather than direct Twitch IRC connection.

**Setup:**
1. Configure your Twitch connection inside ChatPlex / BeatSaberPlus as you normally would
2. Saber Surgeon automatically subscribes to incoming chat messages when ChatPlex is ready
3. Any chat line starting with `!` is processed by the command handler

### Viewer Commands

| Command | Effect | Duration | Cooldown |
|---------|--------|----------|----------|
| `!rainbow` | Rainbow note colors | 30 seconds | Configurable (default 60s) |
| `!notecolor red blue` | Custom left/right note colors (names or hex) | 30 seconds | Shared with !rainbow (default 60s) |
| `!ghost` | Ghost notes appear | 30 seconds | Configurable (default 60s) |
| `!disappear` | Disappearing arrows | 30 seconds | Configurable (default 60s) |
| `!bomb` | Next cube is a bomb (shows viewer's name on cut) | Until hit | Configurable (default 60s) |
| `!faster` | +20% song speed | 30 seconds | Configurable (default 60s) |
| `!superfast` | +50% song speed | 30 seconds | Configurable (default 60s) |
| `!slower` | -15% song speed | 30 seconds | Configurable (default 60s) |
| `!flashbang` | Overexpose lights for flashbang effect | Instant (1s peak + 3s fade) | Configurable (default 60s) |
| `!surgeon` | Show mod status and enabled commands | – | No cooldown |

**Cooldown System:**
- **Global Cooldown (Default):** All commands share the same 60-second cooldown
- **Per-Command Cooldowns (Optional):** Each command has its own configurable cooldown timer

### Moderator-Only Commands (Mods & Broadcaster)

| Command | Effect | Response (if unauthorized) |
|---------|--------|---------------------------|
| `!surgeon disable` | Disable ALL active commands globally | "Sorry {username}, !surgeon disable is a mods only command" |
| `!surgeon enable` | Re-enable all previously enabled commands | "Sorry {username}, !surgeon enable is a mods only command" |
| `!surgeon <command> disable` | Disable a specific command | "Sorry {username}, !surgeon {command} is a mods only command" |
| `!surgeon <command> enable` | Enable a specific command | "Sorry {username}, !surgeon {command} is a mods only command" |

**Supported commands for individual control:** `rainbow`, `notecolor`, `disappear`, `ghost`, `bomb`, `faster`, `superfast`, `slower`, `flashbang`

**Examples:**
- `!surgeon rainbow disable` – Disable only !rainbow
- `!surgeon bomb enable` – Enable only !bomb
- `!surgeon disable` – Disable all commands at once
- `!surgeon enable` – Restore all previously enabled commands

**Note:** Regular viewers attempting moderator commands receive the message: `"Sorry {username}, !surgeon {command} is a mods only command"`

### Speed Command Exclusivity

When **"Speed Exclusivity"** is enabled in settings:
- Only ONE speed effect (`!faster`, `!superfast`, or `!slower`) can be active at a time
- Starting a new speed command cancels any currently active speed effect
- Prevents stacking/conflicts between speed commands

### Custom Bomb Alias

Customize the bomb command name via the SaberSurgeon settings:
- Default: `!bomb`
- Example: Change to `!boop` for `!boop` command
- Saved across sessions

### In-Game Configuration

Access via the SaberSurgeon menu in Beat Saber settings:

#### Cooldowns Panel
- **Global cooldown toggle** – Apply same cooldown to all commands
- **Global cooldown seconds** – Cooldown duration when global mode is on
- **Use per-command cooldowns** – Enable individual cooldown timers for each command
- **Per-command sliders** – Adjust cooldown for: Rainbow, Disappear, Ghost, Bomb, Faster, SuperFast, Slower, Flashbang
- **Speed exclusivity toggle** – Only one speed command active at a time

#### Settings Panel
- **Custom bomb command name** – Change `!bomb` to a custom alias (e.g., `!boop`, `!prank`)
- **Command toggles** – Enable/disable individual commands in the UI
- **Song requests** – Enable/disable viewer song requests (BSR)

## Advanced Features

### Custom Note Colors
Use hex color codes or color names:
!notecolor red blue # Named colors
!notecolor #FF0000 #0000FF # Hex codes
!notecolor rainbow rainbow # Trigger full rainbow mode


### Speed Exclusivity
When enabled, using `!faster` will cancel `!slower` or `!superfast` if they're active (and vice versa). Prevents stacking of speed effects.

### Bomb Alias
Configure the bomb command name in the UI. If set to `boop`, viewers type `!boop` instead of `!bomb`.

### State Management
- All settings persisted to JSON config file
- Enable/disable states preserved across game sessions
- Global disable state is saved and can be restored

## Creator note & support

I’m still pretty new to Beat Saber modding, and Saber Surgeon is something I’m actively learning and improving as I go.

I’m currently building and supporting this mod full-time, and I plan to keep updating it with more fun features over time.

Right now, some parts of the mod use a “brute force” approach to get the gameplay effects working reliably; the plan is to tune/optimize and clean things up in later updates.

If you enjoy the mod and want to support ongoing development, any help (feedback, bug reports, testing, or financial support) is hugely appreciated.


## Version History

- **0.2.0** – Added moderator command management (!surgeon enable/disable), per-command enable/disable, submit later, custom note colors, improved permissions system, Various stability fixes
- **0.1.0** – Initial release with rainbow, ghost, disappear, bomb, speed, flashbang effects

## License

Copyright © PhoenixBlaze0 2025. All rights reserved. No modifications without explicit permission.

## Credits

- Inspired by [GameplayModifiersPlus](https://github.com/Kylemc1413/GameplayModifiersPlus) by Kylemc1413
- Inspired by [StreamPartyCommands](https://github.com/denpadokei/StreamPartyCommand)
- Built with [BSML](https://github.com/monkeymanboy/BeatSaberMarkupLanguage)
- Uses [ChatPlexSDK_BS](https://github.com/hardcpp/BeatSaberPlus) for Twitch integration

---

**Enjoy making your streams chaotic with SaberSurgeon!**
