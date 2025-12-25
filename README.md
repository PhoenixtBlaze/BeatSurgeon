# Saber Surgeon

**Saber Surgeon** is a Beat Saber mod that empowers your Twitch chat to directly interact with your gameplay in real time. It turns your stream into a collaborative (and chaotic) experience by allowing viewers to trigger visual effects, "pranks," and modifiers using simple chat commands—all while you maintain full control over cooldowns and permissions.

## What this mod does

This mod bridges Twitch chat with Beat Saber's gameplay engine. Viewers can type commands to instantly trigger effects such as:

*   **Rainbow Notes & Custom Colors:** Randomize note colors or set specific RGB values on the fly.
*   **Visual Challenges:** Trigger Ghost Notes, Disappearing Arrows, or a blinding Flashbang effect.
*   **Bomb Pranks:** Arm the next note as a "bomb" that displays the viewer's name when cut.
*   **Speed Modifiers:** Temporarily speed up (`!faster`, `!superfast`) or slow down (`!slower`) the song.

> **⚠️ A Note from the Developer**
>
> I am developing and supporting **Saber Surgeon** full-time to bring more fun, interactive features to the Beat Saber community. 
>
> Please be aware that I am still learning the intricacies of Beat Saber modding. The current version uses a "brute force" approach to ensure these effects work reliably, but I plan to refine the code and optimize performance in future updates. 
>
> As this is my full-time focus, **any support to help me keep going is highly appreciated!** Your feedback and support allow me to continue tuning the mod and adding new features.

---

## Features

### Core Gameplay Effects
*   **Rainbow Mode (`!rainbow`)**: Instantly cycles note colors through the RGB spectrum for a vibrant, chaotic look.
*   **Custom Note Colors (`!notecolor`)**: Allows chat to set specific left and right saber colors using color names (e.g., `red`, `blue`) or hex codes.
*   **Ghost Notes (`!ghost`)**: Hides the cube mesh, leaving only the arrows visible for a short duration.
*   **Disappearing Arrows (`!disappear`)**: Hides the directional arrows on notes, forcing players to rely on instinct.
*   **Bomb Pranks (`!bomb`)**: Transforms the next spawnable note into a bomb. If the player cuts it, the viewer's name is displayed as the "culprit." (Currently the bomb does not repeat if you miss cutting the bomb which will be fixed in future updates.)
*   **Speed Control**: Viewers can temporarily alter song speed:
    *   **`!faster`**: Increases speed by 20%.
    *   **`!superfast`**: Increases speed by 50%.
    *   **`!slower`**: Decreases speed by 15%.
    *   *Includes an optional "Speed Exclusivity" mode to prevent multiple speed effects from stacking.*
*   **Flashbang (`!flashbang`)**: Triggers an intense, momentary overexposure of the game's lighting system to simulate a flashbang.

### Robust Moderation & Control
*   **Global Disable/Enable**: Moderators can instantly shut down all mod interactivity with `!surgeon disable` (and restore it with `!surgeon enable`) if things get too chaotic.
*   **Granular Command Control**: Moderators can disable specific problematic commands (e.g., `!surgeon bomb disable`) without stopping the entire mod.
*   **Cooldown Management**:
    *   **Global Cooldowns**: Enforce a universal wait time between *any* command usage.
    *   **Per-Command Cooldowns**: Set specific timers for individual effects (e.g., allow `!rainbow` often, but restrict `!superfast`).
*   **Custom Aliases**: Rename commands like `!bomb` to fit your channel's theme (e.g., `!boop` or `!prank`).

---

## In-Game Settings

You can fully configure **Saber Surgeon** from within Beat Saber using the mod settings menu. The configuration is split into two main tabs:

### 1. Settings Panel
*   **Command Toggles:** Individually enable or disable specific commands (e.g., if you want `!rainbow` enabled but don't want `!flashbangs`).
*   **Global Disable State:** View the current state of the global kill-switch (Enabled/Disabled).

<img width="1107" height="766" alt="Mod Setting Screen" src="https://github.com/user-attachments/assets/4adcdad5-16a6-4e0c-b297-5e9194d1a424" />

### 2. Cooldowns Panel
Control how often chat can trigger effects to balance chaos with playability.

*   **Global Cooldown:**
    *   **Toggle:** When enabled, triggering *any* command puts *all* other commands on cooldown.
    *   **Duration:** Set the universal wait time (default: 60s).
*   **Per-Command Cooldowns:**
    *   **Toggle:** Switch to granular control where each command runs on its own timer.
    *   **Sliders:** Adjust individual cooldowns for `Rainbow`, `Ghost`, `Disappear`, `Bomb`, `Faster`, `SuperFast`, `Slower`, and `Flashbang`.
*   **Speed Exclusivity:**
    *   **Toggle:** When enabled, prevents speed modifiers from stacking. Activating `!faster` will automatically cancel an active `!slower` or `!superfast` effect, ensuring the song remains playable.

*   **Custom Bomb Alias:** Change the default `!bomb` command to something unique for your channel (e.g., set it to `!boop` or `!prank`).

<img width="1337" height="803" alt="Cooldowns Panel" src="https://github.com/user-attachments/assets/b1d54b4e-3a22-465d-9562-88b24db369f7" />

---

## Supporter Benefits

To say thank you to those who support the development of Saber Surgeon, I've added exclusive customization options for **Supporters**!

**Note:** To activate these benefits, you must connect to the **Saber Surgeon Backend** via the Twitch tab in the mod settings (see *Twitch Chat Setup* below). This allows the mod to verify your Twitch subscription status securely.

If you are a supporter (currently via **Twitch Subscription**), you gain access to the **Edit Visuals Button** in the cooldown settings page, which currently allows you to customize the **Bomb Text** effect:

*   **Custom Bomb Text Color:** Change the color of the text that appears when a bomb is cut (default is blue fading to white).
*   **Custom Bomb Fonts:** Choose from a variety of unique fonts for the bomb message to make it stand out even more.
*   **Exclusive Effects:** Tier 2 and higher supporters can request custom fonts or effects to be added in future updates!

**More exclusive cosmetic features are planned for future updates, so please check back soon!**

<img width="1031" height="594" alt="Supporter Features" src="https://github.com/user-attachments/assets/53faa9a6-3e0b-436c-8569-f34e29474712" />

---

## Commands

### Viewer Commands

| Command | Description | Duration | Default Cooldown |
| :--- | :--- | :--- | :--- |
| **`!rainbow`** | Activates Rainbow Mode (cycling note colors). | 30s | 60s |
| **`!notecolor <left> <right>`** | Sets custom note colors. Accepts names (`red`) or hex (`#FF0000`).<br>Example: `!notecolor red blue` or `!notecolor #FF007F #00FF00` | 30s | 60s |
| **`!ghost`** | Activates Ghost Notes (invisible cubes, visible arrows). | 30s | 60s |
| **`!disappear`** | Activates Disappearing Arrows (visible cubes, invisible arrows). | 30s | 60s |
| **`!bomb`** | Arms the next note as a bomb. Displays viewer name on cut. *(Alias customizable)* | Until hit | 60s |
| **`!faster`** | Increases song speed by 20%. | 30s | 60s |
| **`!superfast`** | Increases song speed by 50%. | 30s | 60s |
| **`!slower`** | Decreases song speed by 15%. | 30s | 60s |
| **`!flashbang`** | Triggers a blinding light effect. | Instant | 60s |
| **`!surgeon`** | Displays current mod status and list of enabled commands. | N/A | None |

### Moderator Commands
*Restricted to Broadcasters and Moderators only.*

| Command | Action | Description |
| :--- | :--- | :--- |
| **`!surgeon disable`** | **Global Disable** | Instantly disables ALL commands. Useful for serious attempts or if chat spams too much. |
| **`!surgeon enable`** | **Global Enable** | Re-enables all commands that were previously active. |
| **`!surgeon <cmd> disable`** | **Disable Specific** | Disables a single command type (e.g., `!surgeon bomb disable`). |
| **`!surgeon <cmd> enable`** | **Enable Specific** | Re-enables a single command type (e.g., `!surgeon bomb enable`). |

*Supported command names for specific enable/disable: `rainbow`, `notecolor`, `disappear`, `ghost`, `bomb`, `faster`, `superfast`, `slower`, `flashbang`.*

---


## Requirements

To use Saber Surgeon, you need a PC version of Beat Saber (Steam or Oculus) and the following dependencies:

*   **[Beat Saber](https://beatsaber.com/)** (PC VR)
*   **[BeatSaberPlus](https://github.com/hardcpp/BeatSaberPlus)**
    *   *Crucial:* This mod relies on the **ChatPlexSDK** provided by BeatSaberPlus for Twitch connectivity. You must have BeatSaberPlus installed and your Twitch account connected within it.
*   **[BSIPA](https://github.com/bsmg/BeatSaber-IPA-Reloaded)** (v4.3.6 or later)
*   **[BeatSaberMarkupLanguage (BSML)](https://github.com/monkeymanboy/BeatSaberMarkupLanguage)** (v1.12.5 or later)

---

## Installation

1.  **Install Dependencies:**
    *   (Necessary if you dont use SaberSurgeon Twitch backend) Make sure you have installed **BeatSaberPlus** and successfully connected your Twitch account in its setup menu.
    *   Ensure **BSML** and **BSIPA** are installed (usually handled automatically by ModAssistant).

2.  **Download & Install:**
    *   Download the latest `SaberSurgeon.zip` from the [Releases page](https://github.com/PhoenixtBlaze/SaberSurgeon/releases).
    *   Once you extract the zip file there will be 2 folders `Plugins` and `UserData`. 
    *   Copy and paste both the folders in your beat saber directory (typically `C:\Program Files (x86)\Steam\steamapps\common\Beat Saber\`)

3.  **Launch & Verify:**
    *   Launch Beat Saber.
    *   Look for the **Saber Surgeon** Button in the Mod Settings menu on the left side of the main screen.

---

## Twitch Chat & Backend Setup

Saber Surgeon requires two simple steps to get fully up and running:

### 1. Chat Connection (Basic)
The mod leverages your existing **BeatSaberPlus (ChatPlex)** connection. 
*   **How to:** Simply ensure **BeatSaberPlus** is installed and you are logged into Twitch within its settings.
*   **Result:** The mod will automatically listen to your chat for basic commands that start with `!`.

### 2. Backend Connection (Advanced/Supporter)
To enable **Supporter Benefits** (checking your Twitch Subscription status), you must authenticate with the Saber Surgeon backend.

*   **How to:**
    1.  Open the **Saber Surgeon Settings** in-game.
    2.  Navigate to the **Twitch Tab**.
    3.  Click the **"Connect to Twitch"** button.
    4.  This will open a browser window to authorize the mod securely via Twitch.
*   **Why is this needed?** The ChatPlex connection handles *reading chat*, but the Saber Surgeon backend is required to safely verify *subscription status* for unlocking custom fonts and colors.
*   Once you are connected to SaberSurgeon's Backend, you should see `Edit Visuals` button in the Cooldown settings menu
*   If you dont see the `Edit Visuals` button, Please go out of the menu and reselect saber surgeon in the mods tab to see it.

<img width="886" height="697" alt="Twitch Settings" src="https://github.com/user-attachments/assets/59105a81-11ba-4bba-b55e-d8a9883d18a6" />
---

## Notes / Current Status (Work in Progress)

**Saber Surgeon** is currently in active beta development. While the core features listed above are fully functional, please keep the following in mind:

*   **Brute Force Approach:** Some effects (like material swaps for Rainbow notes or Bomb visual overrides) currently use a "brute force" method to ensure they apply correctly over the base game. This may result in minor performance overhead on lower-end systems, though it is generally stable. Optimization is a priority for future updates.
*   **Disabled Features:** You may see references to "Song Requests" or "Submit Later" in the code or older discussions. These features are currently **disabled/commented out** while they undergo major refactoring and testing. They will be reintroduced in a future update once they meet stability standards.
*   **Compatibility:** This mod is tested primarily on the latest Steam version of Beat Saber. Compatibility with other major gameplay mods (like Noodle Extensions or Chroma) is generally fine, but visual conflicts can occasionally occur when multiple mods try to control note colors simultaneously.

---

## Support Development

I am working on **Saber Surgeon** full-time to create the best possible interactive experience for Beat Saber streamers. As a solo developer still mastering the Beat Saber codebase, this project is a labor of love—and a significant time investment.

If you enjoy the chaos this mod brings to your streams and want to support its continued development, optimization, and new features, please consider supporting me:

*   **[Subscribe on Twitch and Unlock Supporter Benefits](http://twitch.tv/phoenixblaze0)**
*   **[Donate via PayPal](https://paypal.me/PhoenixBlaze0)**

**Your support directly helps me:**
*   Dedicate time to cleaning up the "brute force" code for better performance.
*   Re-enable and finish complex features like **Song Requests** and **Play First, Submit Later**.
*   Keep the mod updated for new Beat Saber versions.

Thank you for helping me keep the lights on and the sabers swinging!

---

## Version History

*   **v0.2.0** (Current)
    *   **New Feature:** Added moderator command management (`!surgeon disable`/`enable`) for global and per-command control.
    *   **New Feature:** Added `!notecolor` command for custom chat-specified RGB values.
    *   **New Feature:** Added **Speed Exclusivity** setting to prevent stacking speed modifiers.
    *   **Cleanup:** Temporarily removed "Play First Submit Later" and "Song Request" systems for refactoring.
    *   **Fix:** Various stability improvements for asset loading and material handling.

*   **v0.1.0**
    *   Initial release.
    *   Core effects: `!rainbow`, `!ghost`, `!disappear`, `!bomb`, `!flashbang`.
    *   Basic speed commands (`!faster`, `!slower`).
    *   Basic configuration UI.

---

## License

**Copyright © PhoenixBlaze0 2025**

This project is proprietary. All rights reserved.

*   You **may** download and use this mod for personal gameplay and streaming.
*   You **may not** modify or redistribute without explicit written permission from the author.
*   You **may not** re-upload this mod to other platforms or claim it as your own.

---

## Credits

**Saber Surgeon** wouldn't exist without the inspiration and tools provided by the amazing Beat Saber modding community.

*   **Development & Design:** [PhoenixBlaze0](https://github.com/PhoenixBlaze0)
*   **Twitch Integration:** Powered by **[ChatPlexSDK](https://github.com/hardcpp/BeatSaberPlus)** (part of BeatSaberPlus).
*   **UI Framework:** Built using **[BeatSaberMarkupLanguage (BSML)](https://github.com/monkeymanboy/BeatSaberMarkupLanguage)**.
*   **Inspiration:**
    *   **[GameplayModifiersPlus](https://github.com/Kylemc1413/GameplayModifiersPlus)** by Kylemc1413 – The original inspiration for chat-controlled modifiers.
    *   **[StreamPartyCommands](https://github.com/denpadokei/StreamPartyCommand)** – For ideas on interactive viewer commands.

---

**Enjoy making your streams chaotic with Saber Surgeon!**
