# Beat Surgeon

**Beat Surgeon** is a Beat Saber mod that empowers your Twitch chat to directly interact with your gameplay in real time. It turns your stream into a collaborative (and chaotic) experience by allowing viewers to trigger visual effects like rainbow, flashbang, note bombs and modifiers using simple chat commands or Channel Point Redeems—all while letting you maintain full control over cooldowns and if command is enabled or not As a bonus i've also added my own custom version of Play First Submit Later which pauses the map at the end.

## What this mod does

This mod bridges Twitch chat with Beat Saber's gameplay engine. Viewers can type commands to instantly trigger effects such as:

*   **Rainbow Notes & Custom Colors:** Randomize note colors or set specific RGB values on the fly.
*   **Visual Challenges:** Trigger Ghost Notes, Disappearing Arrows, or a blinding Flashbang effect.
*   **Bomb Pranks:** Arm the next note as a "bomb" that displays the viewer's name when cut.
*   **Speed Modifiers:** Temporarily speed up (`!faster`, `!superfast`) or slow down (`!slower`) the song.

> **⚠️ A Note from the Developer**
>
> I am developing and supporting **Beat Surgeon** full-time to bring more fun, interactive features to the Beat Saber community. 
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

You can fully configure **Beat Surgeon** from within Beat Saber using the mod settings menu. The configuration is split into two main tabs:

### 1. Settings Panel
*   **Command Toggles:** Individually enable or disable specific commands (e.g., if you want `!rainbow` enabled but don't want `!flashbangs`).
*   **Global Disable State:** View the current state of the global kill-switch (Enabled/Disabled).

<img width="1052" height="710" alt="image" src="https://github.com/user-attachments/assets/447ca616-7c2d-4ee1-ba0c-d945ca26c7ff" />


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


### 3. Surgeon Tab
Quick access to command and channel point controls directly from the gameplay setup screen—perfect for adjusting settings on the fly before starting a map.

This tab contains two sub-tabs:

#### **Surgeon Commands Sub-Tab**
Enable or disable individual chat commands and effects with visual icon toggles.

<img width="922" height="774" alt="image" src="https://github.com/user-attachments/assets/681596ed-5e37-4656-a30c-d701f5ac1779" />


*   **Command Toggles:** Click each icon button to enable/disable that specific command:
    *   `!rainbow` / `!notecolor` - RGB note colors
    *   `!disappear` - Disappearing arrows
    *   `!ghost` - Transparent notes
    *   `!bomb` - Convert random note to bomb
    *   `!faster` - Increase song speed
    *   `!superfast` - Dramatically increase song speed
    *   `!slower` - Decrease song speed
    *   `!flashbang` - Environmental flash effect
*   **Visual Feedback:** Enabled commands show in color with a highlighted icon; disabled commands appear grayed out.


#### **Twitch Sub-Tab**
Configure Twitch Channel Point rewards for each effect without leaving the game.
**For Channel Points to work Commands need to be enabled**
   
<img width="1023" height="716" alt="image" src="https://github.com/user-attachments/assets/8cd0606c-0aae-4fde-98c3-2f0f37e83250" />   

*   **Connection Status:** Real-time display showing your Twitch connection state.

<img width="980" height="712" alt="image" src="https://github.com/user-attachments/assets/1a1c3647-39bb-4340-9df5-366e90cc3986" />

*   **Channel Point Configuration:** Click any effect icon to open its settings modal:
    *   **Enable as Channel Point:** Toggle to create/enable or disable the reward on your Twitch channel.
    *   **Cost:** Set the channel point price for the reward (numeric input).
    *   **Cooldown:** Define per-user cooldown in seconds (0-3600).
    *   **Background Color:** Customize the reward's appearance on Twitch with a color picker.
      
---

## Supporter Exclusive Features

To say thank you to those who support the development of Beat Surgeon, I've added exclusive customization options for **Supporters**!

**Note:** To activate these benefits, you must connect to the **Beat Surgeon Backend** via the Twitch tab in the mod settings (see *Twitch Chat Setup* below). This allows the mod to verify your Twitch subscription status securely.

If you are a supporter (currently via **Twitch Subscription**), you gain access to **Edit Visuals buttons** throughout the mod. Each effect that supports visual customization will have its own **Edit Visuals button**, allowing you to personalize how the effect looks to match your style.

### Current supporter-exclusive customization features:

#### **Bomb Text Effect**
- **Custom Bomb Text Color, Height and Width:** Change the color of the text that appears when a bomb is cut (default is blue fading to white)
- **Custom Bomb Fonts:** Choose from a variety of unique fonts for the bomb message to make it stand out even more
- **Custom Explosion Effects:** Coming soon...

#### **Rainbow Effects**

- **Cycle Speed:** Change how fast the Gradient in rainbow changes if enabled. (lower = slower)

#### **Flashbang Effects**
- **Brightness Multiplier:** Changed how bright your flashbang is. Default is 100 

  
---

**More exclusive cosmetic features and additional effects with Edit Visuals buttons are planned for future updates, so please check back soon!**

**Current supporter exclusive cosmetic features are displayed below:**

<img width="1128" height="762" alt="image" src="https://github.com/user-attachments/assets/2abf4c51-6721-4fe9-9af4-0d8d5fe24a11" />

<img width="1190" height="731" alt="image" src="https://github.com/user-attachments/assets/cab6be8d-bcc9-43dd-ad21-cc8a9b5166fd" />

<img width="881" height="513" alt="image" src="https://github.com/user-attachments/assets/e9268b32-766a-45fa-997f-ccd3f8255a59" />

<img width="1044" height="645" alt="image" src="https://github.com/user-attachments/assets/a7988430-868a-432c-8f7b-6f558d5f805d" />


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

To use Beat Surgeon, you need a PC version of Beat Saber (Steam or Oculus) and the following dependencies:

*   **[Beat Saber](https://beatsaber.com/)** (PC VR)
*   **[AssetBundleLoadingTools](https://github.com/nicoco007/AssetBundleLoadingTools)**
*   **[BSIPA](https://github.com/bsmg/BeatSaber-IPA-Reloaded)** (v4.3.6 or later)
*   **[BeatSaberMarkupLanguage (BSML)](https://github.com/monkeymanboy/BeatSaberMarkupLanguage)** (v1.12.5 or later)

---

## Installation

1.  **Install Dependencies:**
    *   (Necessary if you dont use BeatSurgeon Twitch backend) Make sure you have installed **BeatSaberPlus** and successfully connected your Twitch account in its setup menu.
    *   Ensure **BSML** and **BSIPA** are installed (usually handled automatically by ModAssistant).

2.  **Download & Install:**
    *   Download the latest `BeatSurgeon.zip` from the [Releases page](https://github.com/PhoenixtBlaze/BeatSurgeon/releases).
    *   Once you extract the zip file there will be 2 folders `Plugins` and `UserData`. 
    *   Copy and paste both the folders in your beat saber directory (typically `C:\Program Files (x86)\Steam\steamapps\common\Beat Saber\`)

3.  **Launch & Verify:**
    *   Launch Beat Saber.
    *   Look for the **Beat Surgeon** Button in the Mod Settings menu on the left side of the main screen.

---

## Twitch Chat & Backend Setup

Beat Surgeon requires two simple steps to get fully up and running:

### 1. Chat Connection (Basic)
The mod leverages your existing **BeatSaberPlus (ChatPlex)** connection. 
*   **How to:** Simply ensure **BeatSaberPlus** is installed and you are logged into Twitch within its settings.
*   **Result:** The mod will automatically listen to your chat for basic commands that start with `!`.

### 2. Backend Connection (Advanced/Supporter)
To enable **Supporter Benefits** (checking your Twitch Subscription status), you must authenticate with the Beat Surgeon backend.

*   **How to:**
    1. Open the **Beat Surgeon Settings** in-game.
       <img width="912" height="607" alt="image" src="https://github.com/user-attachments/assets/fc878f98-0b71-4306-9e55-1c1f644ae680" />
    2. Navigate to the **Twitch Tab** in Surgeon Menu.
       <img width="727" height="552" alt="image" src="https://github.com/user-attachments/assets/39496f7f-7c59-45b6-bf31-6a963dd3b371" />
    3. Click the **"Connect to Twitch"** button.
    4. This will open a browser window to authorize the mod securely via Twitch.
       <img width="1912" height="942" alt="image" src="https://github.com/user-attachments/assets/7a19eeb9-1de5-44b4-86de-d51bc99d3c7c" />
    5. You can Close the browser page once you see this page.
       <img width="1304" height="320" alt="image" src="https://github.com/user-attachments/assets/a7a3b7c3-b28d-4263-b63b-53c8ab3989df" />



*   **Why is this needed?** There is a ChatPlex connection that handles *reading chat* if you dont want to use BeatSurgeon Backend, but the Beat Surgeon backend is required to safely verify *subscription status* for unlocking custom fonts and colors.
*   Once you are connected to BeatSurgeon's Backend, you should see `Edit Visuals` button in the Cooldown settings menu
*   If you dont see the `Edit Visuals` button, Please go out of the menu and reselect Beat surgeon in the mods tab to see it.
  
<img width="1230" height="524" alt="image" src="https://github.com/user-attachments/assets/f8d065ca-f955-4f8a-969a-98bd30b3faf3" />



## Notes / Current Status (Work in Progress)

**Beat Surgeon** is currently in active beta development. While the core features listed above are fully functional, please keep the following in mind:

*   **Brute Force Approach:** Some effects (like material swaps for Rainbow notes or Bomb visual overrides) currently use a "brute force" method to ensure they apply correctly over the base game. This may result in minor performance overhead on lower-end systems, though it is generally stable. Optimization is a priority for future updates.
*   **Disabled Features:** You may see references to "Song Requests" or "Submit Later" in the code or older discussions. These features are currently **disabled/commented out** while they undergo major refactoring and testing. They will be reintroduced in a future update once they meet stability standards.
*   **Compatibility:** This mod is tested primarily on the Beat Saber v1.40.8. Compatibility with other major gameplay mods (like Noodle Extensions or Chroma) is generally fine, but visual conflicts can occasionally occur when multiple mods try to control note colors simultaneously Please let me know if you find any of them so they can be patched.

---

## Support Development

I am working on **Beat Surgeon** full-time to create the best possible interactive experience for Beat Saber streamers. As a solo developer still mastering the Beat Saber codebase, this project is a labor of love—and a significant time investment.

If you enjoy the chaos this mod brings to your streams and want to support its continued development, optimization, and new features, please consider supporting me:

*   **[Subscribe on Twitch and Unlock Supporter Benefits](http://twitch.tv/phoenixblaze0)**
*   **[Donate via PayPal](https://paypal.me/PhoenixBlaze0)**

**Your support directly helps me:**
*   Dedicate time to cleaning up the "brute force" code for better performance.
*   Re-enable and finish complex features like **Endless Mode** and **Dynamic Block/ BeatMap Insertions**.
*   Keep the mod updated for new Beat Saber versions.

Thank you for helping me keep the lights on and the sabers swinging!

---

## Version History

*   **v0.3.0** (Current)
    *   **Removed Feature:** Local subscription checking methods
    *   **Fixed Feature:** Various improvements for bomb visuals and multithreading.
    *   **Fixed Feature:** Moderator command management as well as Mod/VIP detection.
    *   **New Feature:** Added entitlement-based subscription checking system with auto-refreshing tokens
    *   **(Still in Testing) New Feature:** Multiplayer+ support for anyone using Beat Surgeon in the lobby.
    *   **New Feature:** Added Twitch EventSub connection to let the mod manage channel point rewards
    *   **New Feature:** Added Submit Later tab in the Gameplay Setup view.
    *   **New Feature:** Added Surgeon tab in the Gameplay Setup view (now lets you enable/disable commands and channel points in multiplayer through UI)
    

*   **v0.2.0**
    *   **New Feature:** Added moderator command management (`!surgeon disable`/`enable`) for global and per-command control.
    *   **New Feature:** Added `!notecolor` command for custom chat-specified RGB values.
    *   **New Feature:** Added **Speed Exclusivity** setting to prevent stacking speed modifiers.
    *   **Cleanup:** Temporarily removed "Play First, Submit Later" and "Song Request" systems for refactoring.
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

**Beat Surgeon** wouldn't exist without the inspiration and tools provided by the amazing Beat Saber modding community.

*   **Development & Design:** [PhoenixBlaze0](https://github.com/PhoenixBlaze0)
*   **UI Framework:** Built using **[BeatSaberMarkupLanguage (BSML)](https://github.com/monkeymanboy/BeatSaberMarkupLanguage)**.
*   **Inspirations:**
    *   **[GameplayModifiersPlus](https://github.com/Kylemc1413/GameplayModifiersPlus)** by Kylemc1413 – The original inspiration for chat-controlled modifiers.
    *   **[StreamPartyCommands](https://github.com/denpadokei/StreamPartyCommand)** – For ideas on interactive viewer commands.

---

**Enjoy making your streams chaotic with Beat Surgeon!**
