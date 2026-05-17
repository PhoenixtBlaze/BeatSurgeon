# Beat Surgeon

**Beat Surgeon** is a Beat Saber mod that empowers your Twitch chat to directly interact with your gameplay in real time. It turns your stream into a collaborative (and chaotic) experience, allowing viewers to trigger visual effects like Rainbow notes, Flashbang, Bombs, Disappearing Arrows, Ghost Notes and Speed modifiers using simple chat commands or Channel Point Redeems. All while letting you maintain full control over cooldowns and if command is enabled or not.

## What this mod does

This mod bridges Twitch chat with Beat Saber's gameplay engine. Viewers can type commands to instantly trigger effects such as:

*   **Rainbow Notes & Custom Colors:** Randomize note colors or set specific RGB values on the fly.
*   **Visual Challenges:** Trigger Ghost Notes, Disappearing Arrows, or a blinding Flashbang effect.
*   **Bombs:** Arms the next note as a "bomb" and makes it look like one that displays either the viewer's name or a custom `!bmsg` message when cut.
*   **Speed Modifiers:** Temporarily speed up (`!faster`, `!superfast`) or slow down (`!slower`) the song.

> **⚠️ A Note from the Developer**
>
> I am developing and supporting **Beat Surgeon** full-time to bring more fun, interactive features to the Beat Saber community. 
>
> Please be aware that I am still learning the intricacies of Beat Saber modding. The current version has been tested to make all these new and old effects work reliably and without causing any performance issues or conflicts with other mods, I plan to refine all that it does and plan for better features in future updates. 
>
> As this is one of my full-time focuses so, **any support to help me keep going is highly appreciated!** Your feedback and support allow me to continue tuning the mod and adding new features.

---

## Features

### Core Gameplay Effects
*   **Rainbow Mode (`!rainbow`)**: Instantly cycles note colors through the RGB spectrum for a vibrant, chaotic look.
*   **Custom Note Colors (`!notecolor`)**: Allows chat to set specific left and right saber colors using color names (e.g., `red`, `blue`) or hex codes.
*   **Ghost Notes (`!ghost`)**: Hides the cube mesh, leaving only the arrows visible for a short duration.
*   **Disappearing Arrows (`!disappear`)**: Hides the directional arrows and dots on notes, forcing players to rely on instinct.
*   **Bombs (`!bomb`, `!bmsg <text>`)**: Transforms the next spawnable note into a bomb. If the player cuts it, the viewer's name is displayed, or the custom `!bmsg` text if one was provided.
*   **Speed Control**: Viewers can temporarily alter song speed:
    *   **`!faster`**: Increases speed by 20%.
    *   **`!superfast`**: Increases speed by 50%.
    *   **`!slower`**: Decreases speed by 15%.
    *   *Includes an optional "Speed Exclusivity" mode to prevent multiple speed effects from stacking.*
*   **Flashbang (`!flashbang`)**: Triggers an intense, momentary overexposure of the game's lighting system to simulate a flashbang.

### Supporter-Exclusive Commands
These commands are unlocked automatically when the mod detects an active Twitch Subscription or Patreon support tier. No manual setup is needed — connect to the Beat Surgeon backend and your status is verified instantly.

*   **Glitter (`!glitter <bits>`)**: Triggered by Twitch Bits or chat (when Bit Effect is enabled). Spawns a glitter particle burst on notes. Bit amount is accepted as a parameter; chat usage is hard-capped at 10,000 bits equivalent. Has its own cooldown (default: 10s).
*   **Subscriber Message (`!smsg <text>`)**: Available to Twitch **subscribers, moderators, and the broadcaster**. Displays a custom in-game message effect (up to 100 characters). Has its own cooldown (default: 60s).
*   **Follower Message (`!fmsg <text>`)**: Available to followers. Displays a custom in-game message effect (up to 100 characters). Requires the Follow Effects backend to be authorized.

### Robust Moderation & Control
*   **Global Disable/Enable**: Moderators can instantly shut down all mod interactivity with `!surgeon disable` (and restore it with `!surgeon enable`) if things get too chaotic.
*   **Granular Command Control**: Moderators can disable specific problematic commands (e.g., `!surgeon bomb disable`) without stopping the entire mod.
*   **Cooldown Management**:
    *   **Global Cooldowns**: Enforce a universal wait time between *any* command usage.
    *   **Per-Command Cooldowns**: Set specific timers for individual effects (e.g., allow `!rainbow` often, but restrict `!superfast`).
*   **Permission Gating**: Restrict all gameplay commands to specific audience groups — Everyone (default), VIPs only, or Subscribers only. Configured in the surgeon settings tab.
*   **Custom Aliases**: Rename commands like `!bomb` to fit your channel's theme (e.g., `!boop` or `!prank`).
*   **Ranked Map Auto-Protection**: When Beat Surgeon detects a ranked map (ScoreSaber, BeatLeader, or AccSaber), all commands are automatically blocked to protect your score. A chat notification is sent when this triggers. Individually configurable per leaderboard in the mod settings.

---

## In-Game Settings

Beat Surgeon has two settings locations: the **Mod Settings menu** (accessible from the main menu left panel), and the **Surgeon Tab** in the Gameplay Setup screen (accessible in the lobby and multiplayer).


---

### Mod Settings Menu

The Mod Settings menu contains four active tabs:

---

#### 1. Surgeon Commands Tab
Enable or disable individual commands using visual icon toggle buttons. Enabled commands are highlighted in color; disabled commands are grayed out.

*   **Rainbow / Note Color** — `!rainbow` and `!notecolor`
*   **Disappearing Arrows** — `!disappear`
*   **Ghost Notes** — `!ghost`
*   **Bomb** — `!bomb` / `!bmsg`
*   **Faster Song** — `!faster`
*   **SuperFast Song** — `!superfast`
*   **Slower Song** — `!slower`
*   **Flashbang** — `!flashbang`

<img width="1067" height="655" alt="image" src="https://github.com/user-attachments/assets/d7d61494-fd69-41b0-bb61-fe48f2a79292" />

#### 2. Twitch Tab
Manage your Twitch connection and control who is allowed to use chat commands.

*   **Twitch Status:** Live read-only display of your current connection state (e.g. `Connected`, `Connected • Supporter Verified (Tier 1)`, or `Not connected`).
*   **Connect Twitch Button:** Opens a browser window to link your Twitch account to the Beat Surgeon backend. Required for Channel Points and Supporter feature verification.
*   **Who can use `!` commands?** — Three independent toggles to gate command access:
    *   **Allow Everyone** — Any viewer can use commands.
    *   **Allow VIPs** — Only VIP users can use commands.
    *   **Allow Subscribers** — Only subscribers can use commands.

> **Note:** These toggles are independent, so you can allow both VIPs and Subscribers while blocking everyone else, for example.

<img width="1366" height="738" alt="image" src="https://github.com/user-attachments/assets/2851e361-25b1-4b23-aab0-2f3ff31fda2c" />

#### 3. Surgeon Settings Tab
General mod behaviour and ranked map protection settings.

*   **Enable Surgeon Multiplayer Effects** — When enabled, Beat Surgeon applies the same commands and effects to your game that your Multiplayer+ lobby leader receives.
*   **Auto-Disable everything on Ranked maps** — When enabled, all commands and Channel Point rewards are automatically stopped and blocked whenever a ranked map is detected. Individually configurable per leaderboard:
    *   **BeatLeader** — Detect BeatLeader ranked maps for auto-disable.
    *   **ScoreSaber** — Detect ScoreSaber ranked maps for auto-disable.
    *   **AccSaber (reloaded)** — Detect AccSaber Reloaded ranked maps for auto-disable.
    *(The per-leaderboard toggles are only visible when the main auto-disable toggle is enabled.)*

<img width="1331" height="732" alt="image" src="https://github.com/user-attachments/assets/f051bf29-ddb0-499d-8d30-1678c87c2567" />


#### 4. Supporter Tab *(Unlocks automatically when supporter status is verified)*
This tab becomes available once the mod detects an active **Twitch Subscription** or **Patreon** supporter tier via the backend. No manual activation is required.

<!-- SCREENSHOT: Add a screenshot of the Supporter Tab here -->

*   **Bit Effect** — Enable supporter-only Bits and Cheer effects and the `!glitter` command.
*   **Sub Effects** — Enable supporter-only Subscriber effects and commands for when viewers subscribe. *(Toggle is interactable only when backend authorization is confirmed.)*
*   **Follow Effects** — Enable supporter-only follower message effects and the `!fmsg` command for when viewers follow. *(Toggle is interactable only when Follow Effects backend authorization is confirmed.)*
*   **Surgeon Fonts** — Select the font style Beat Surgeon uses for all in-game text effects (e.g., bomb cut text, message effects). Includes a live preview of the chosen font.

<img width="1402" height="745" alt="image" src="https://github.com/user-attachments/assets/ea47994d-44ff-4a54-a4a4-8a93c2c59b2f" />

---


### Cooldowns Panel
Control how often chat can trigger effects to balance chaos with playability.

*   **Global Cooldown:**
    *   **Toggle:** When enabled, triggering *any* command puts *all* other commands on cooldown.
    *   **Duration:** Set the universal wait time (default: 60s).
*   **Per-Command Cooldowns:**
    *   **Toggle:** Switch to granular control where each command runs on its own timer.
    *   **Sliders:** Adjust individual cooldowns for `Rainbow`, `Ghost`, `Disappear`, `Bomb`, `Faster`, `SuperFast`, `Slower`, and `Flashbang`.
*   **Speed Exclusivity:**
    *   **Toggle:** When enabled, prevents speed modifiers from stacking. Activating `!faster` will automatically cancel an active `!slower` or `!superfast` effect, ensuring the song remains playable.

*   **Custom Bomb Alias:** Change the default `!bomb` command to something unique for your channel (e.g., set it to `!boop` or `!plur`).

<img width="1337" height="803" alt="Cooldowns Panel" src="https://github.com/user-attachments/assets/b1d54b4e-3a22-465d-9562-88b24db369f7" />

---

### Surgeon Gameplay Tab
Quick access to command and channel point controls directly from the gameplay setup screen. Perfect for adjusting settings on the fly before starting a map or in multiplayer.

This tab contains two sub-tabs:

#### **Surgeon Commands Sub-Tab**
Enable or disable individual chat commands and effects with visual icon toggles.

<img width="922" height="774" alt="image" src="https://github.com/user-attachments/assets/681596ed-5e37-4656-a30c-d701f5ac1779" />


*   **Command Toggles:** Click each icon button to enable/disable that specific command:
*   **Visual Feedback:** Enabled commands show in color with a highlighted icon; disabled commands appear grayed out.


#### **Twitch Sub-Tab**
Configure Twitch Channel Point rewards for each effect without leaving the lobby if in one.
**Channel Points are completely Individual now and work even without command being enabled as long as your channel point is enabled**
   
<img width="1023" height="716" alt="image" src="https://github.com/user-attachments/assets/8cd0606c-0aae-4fde-98c3-2f0f37e83250" />   

*   **Connection Status:** Real-time display showing your Twitch connection state. When you are a verified supporter, this will also show your supporter tier (e.g., `Connected • Supporter Verified (Tier 1)`).

<img width="980" height="712" alt="image" src="https://github.com/user-attachments/assets/1a1c3647-39bb-4340-9df5-366e90cc3986" />

*   **Channel Point Configuration:** Click any effect icon to open its settings modal:
    *   **Enable as Channel Point:** Toggle to create/enable or disable the reward on your Twitch channel.
    *   **Cost:** Set the channel point price for the reward (numeric input).
    *   **Cooldown:** Define per-user cooldown in seconds (0-3600).
    *   **Background Color:** Customize the reward's appearance on Twitch with a color picker.

*   **Channel Points are Refunded *automatically* if the effect did not apply for any reason**.
---

## Supporter Exclusive Features

To say thank you to those who support the development of Beat Surgeon, exclusive features, commands, and customization options are available for **Supporters**.

**Note:** To activate these benefits, you must connect to the **Beat Surgeon Backend** via the Twitch tab in the mod settings (see *Twitch Chat Setup* below). The mod verifies your supporter status securely. Supporter status is detected for both **Twitch Subscribers** and **Patreon supporters**.

Once verified:
- The **Supporter Tab** unlocks automatically in the mod settings menu.
- **Edit Visuals** buttons appear throughout the Cooldowns screen for supported effects.
- Supporter-exclusive commands (`!glitter`, `!smsg`, `!fmsg`) become active in chat.
- The "Support Beat Surgeon" button at the bottom of the settings screen is replaced with a **"Supporter features unlocked 💙"** confirmation — no restart needed.

> If you don't see the Edit Visuals buttons or the Supporter Tab after connecting, exit and re-select Beat Surgeon in the mods tab to refresh the UI.

---

### Supporter-Exclusive Commands

These commands are unlocked and configurable once supporter status is verified.

| Command | Toggle in Settings | Who Can Use | Description |
| :--- | :--- | :--- | :--- |
| **`!glitter <bits>`** | **Bit Effect** | All chat (when Bit Effect enabled) / Bit Events | Spawns a glitter particle burst on notes. Accepts a bit amount as a parameter; chat usage is capped at 10,000. Has its own cooldown (default: 10s). |
| **`!smsg <text>`** | **Sub Effects** | Subscribers, Moderators, Broadcaster | Displays a custom in-game message effect (up to 100 characters). Triggered also when viewers subscribe. Has its own cooldown (default: 60s). |
| **`!fmsg <text>`** | **Follow Effects** | Followers | Displays a custom in-game message effect (up to 100 characters). Triggered also when viewers follow. Requires Follow Effects backend authorization. |

---

### Supporter-Exclusive Visual Customizations

Each effect below gains an **Edit Visuals** button in the Cooldowns settings screen once you are authenticated with the backend. Effects without a visuals modal (Faster, SuperFast, Slower) have no Edit Visuals button.

#### **Bomb Visuals**
- **Spawn Distance:** How far in front of the player the bomb cut text appears (2–20 units, increment 0.5).
- **Start Colour:** The beginning color of the gradient on the bomb cut text (animates from Start → End over time).
- **End Colour:** The ending color of the gradient on the bomb cut text (default is blue fading to white).

<img width="991" height="515" alt="image" src="https://github.com/user-attachments/assets/297e8ddb-9fb5-4be0-90fd-ac9741112123" />


#### **Rainbow Visuals**
- **Cycle Speed:** Controls how fast the rainbow color gradient cycles across notes (0.01–5; lower = slower). Includes a live note preview panel in the modal.

<img width="997" height="549" alt="image" src="https://github.com/user-attachments/assets/ffe205ae-adbe-40ef-8670-1e1de00d5cec" />


#### **Disappearing Arrows Visuals**
- **Fade Duration (ms):** How long before arrows fade from notes (0.1–5, increment 0.1). Default is 0.30.

<img width="859" height="508" alt="image" src="https://github.com/user-attachments/assets/555270bb-012d-45ab-a71d-7dd02d55e875" />


#### **Ghost Notes Visuals**
- No active settings yet — placeholder for future customisation.

#### **Flashbang Visuals**
- **Brightness Multiplier:** Controls the intensity of the flashbang effect (1–200, increment 5; higher = brighter). Default is 91.

<img width="929" height="548" alt="image" src="https://github.com/user-attachments/assets/1b1f132c-5e9b-41e3-8a75-ecb4007930f4" />


---

### Surgeon Fonts *(Supporter Tab)*
A global font selector applies to **all** in-game text effects Beat Surgeon renders (bomb cut text, message effects, etc.).

- **Font Style:** Dropdown to select from available font options. Includes a live **Preview Text** display below the dropdown so you can see the font before confirming.

<img width="686" height="193" alt="image" src="https://github.com/user-attachments/assets/4330f791-b3e7-4b29-a829-4c798691aba2" />


---

**More exclusive features and additional effects are planned for future updates, so please check back soon!**

---

## Commands

### Viewer Commands

| Command | Description | Duration | Default Cooldown |
| :--- | :--- | :--- | :--- |
| **`!rainbow`** | Activates Rainbow Mode (cycling note colors). | 30s | 60s |
| **`!notecolor <left> <right>`** | Sets custom note colors. Accepts names (`red`) or hex (`#FF0000`).<br>Example: `!notecolor red blue` or `!notecolor #FF007F #00FF00` | 30s | 60s |
| **`!ghost`** | Activates Ghost Notes (invisible cubes, visible arrows). | 30s | 60s |
| **`!disappear`** | Activates Disappearing Arrows (visible cubes, invisible arrows). | 30s | 60s |
| **`!bomb`** | Arms the next note as a bomb. Displays viewer name on cut. *(Alias customizable)* | Until hit | 1s |
| **`!bmsg <text>`** | Arms the next note as a bomb. Displays up to 70 characters of custom text on cut, or falls back to the viewer name if no text is supplied. Shares the same cooldown as `!bomb`. | Until hit | 1s |
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
To enable **Supporter Benefits** and to unlock Edit Visuals buttons, you must authenticate with the Beat Surgeon backend.

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



*   **Why is this needed?** There is a ChatPlex connection that handles *reading chat* if you dont want to use BeatSurgeon Backend, but the Beat Surgeon backend is required to use Channelpoint features and to safely verify *subscription status* for unlocking supporter features.
*   Once you are connected to BeatSurgeon's Backend, you should see `Edit Visuals` button in the Cooldown settings menu
*   If you dont see the `Edit Visuals` button, Please go out of the menu and reselect Beat surgeon in the mods tab to see it.
  
<img width="1230" height="524" alt="image" src="https://github.com/user-attachments/assets/f8d065ca-f955-4f8a-969a-98bd30b3faf3" />

---

### Unlocking Supporter Features

Supporter features unlock automatically once the mod verifies your status via the Beat Surgeon backend. There are two supported platforms: **Twitch Subscription** and **Patreon**. Each has its own in-game flow described below.

Once verified on either platform:
- The **Supporter Tab** becomes visible in the mod settings menu.
- **Edit Visuals** buttons appear throughout the Cooldowns screen.
- Supporter-exclusive commands (`!glitter`, `!smsg`, `!fmsg`) become active.
- The **"Support this project 💙"** button at the bottom of the settings screen is replaced by a **"Supporter features unlocked 💙"** confirmation text automatically.

---

### Option 1: Twitch Subscription

**Requirement:** An active subscription to [twitch.tv/phoenixblaze0](https://www.twitch.tv/phoenixblaze0) and follow backend connection steps.


> **Note:** If you were already connected to Twitch but are now seeing `Please Reauthorize` in the status text, click **Connect Twitch** again to refresh your token.

---

### Option 2: Patreon

**Requirement:** An active pledge on the [Beat Surgeon Patreon](https://www.patreon.com/PhoenixBlaze0).

1. Open Beat Saber and navigate to **Beat Surgeon** in the Mod Settings menu.
2. At the bottom of the settings screen, click the **"Support this project 💙"** button.
3. A **Support Beat Surgeon** screen will appear, showing two platform options (Patreon and Twitch logos).

<img width="693" height="529" alt="image" src="https://github.com/user-attachments/assets/4da723a3-34b2-4894-9192-277762a417e3" />

4. Click the **Patreon** button in the modal.
6. A browser window will open, login and authorize the mod with your Patreon account to verify your pledge status.
<img width="742" height="745" alt="image" src="https://github.com/user-attachments/assets/06728bac-230e-4f8c-a795-e0a926334d55" />
<img width="441" height="752" alt="image" src="https://github.com/user-attachments/assets/1a0c4430-e300-43c7-ba33-01613325b7e5" />

7. Once authorization completes, the modal will close automatically and the **Supporter Tab** will unlock in the settings.

> **Note:** If your Patreon pledge is active but verification fails, the Patreon button will attempt to re-authorize automatically on the next click. If problems persist, ensure your Patreon account has an active pledge before retrying. If nothing works after that, please feel free to message me on discord to resolve issues.

---

> Both flows verify your status against the Beat Surgeon backend securely. Your subscription or pledge data is only used to verify supporter tier nothing is stored.

## Notes / Current Status (Work in Progress)

**Beat Surgeon** is currently in active development. While the core features listed above are fully functional, please keep the following in mind:

*   **Disabled Features:** You may see references to "Song Requests" or "Endless Mode" in the code. These features are currently **disabled/commented out** while they undergo major refactoring and testing. They will be reintroduced in a future update once they meet stability standards.
*   **Compatibility:** This mod is tested primarily on the Beat Saber v1.40.8. Compatibility with other major gameplay mods (like Noodle Extensions or Chroma) is generally fine, but visual conflicts can occasionally occur when multiple mods try to control notes and their visuals simultaneously. Please let me know if you find any of them so they can be patched.

---

## Support Development

I am working on **Beat Surgeon** full-time to create the best possible interactive experience for Beat Saber streamers. As a solo developer still mastering the Beat Saber codebase, this project is a labor of love—and a significant amount of time investment.

If you enjoy the chaos this mod brings to your streams and want to support its continued development, optimization, and new features, please consider supporting me:

*   **[Subscribe on Twitch and Unlock Supporter Benefits](http://twitch.tv/phoenixblaze0)**
*   **[Subscribe on Patreon and Unlock Supporter Benefits](patreon.com/PhoenixBlaze0)**
*   **[Donate via PayPal](https://paypal.me/PhoenixBlaze0)**

**Your support directly helps me:**
*   Dedicate time to crefinind features and fixing bugs.
*   Re-enable and finish complex features like **Endless Mode** and **Dynamic Block/ BeatMap Insertions**.
*   Keep the mod updated for new Beat Saber versions.

Thank you for helping me keep the lights on and the sabers swinging!

---

## Version History

*   **v1.1.1** (Upcoming)
    *   **Bug Fixes** Fixed Ranked Message Spam when restarting a ranked map.
    *   **Bug Fixes** Fixed Command loop restarting the effects and loopping.
    *   **Bug Fixes** Fixed Channel point cooldowns reseting when opeaning the UI.
    *   **Bug Fixes** Fixed Channel Point cooldowns not applying proper cooldowns on twitch.
    *   **New Feature:** Added Supporter Tab in the Surgeon gameplay setup view — unlocks automatically when Twitch Subscription or Patreon supporter tier is verified via the backend.
    *   **New Feature:** Added `!glitter <bits>` command — spawns a glitter particle burst on notes, triggered by Bit Events or chat (when Bit Effect is enabled, capped at 10,000 bits for command).
    *   **New Feature:** Added `!smsg <text>` command — subscriber-exclusive in-game message effect (up to 100 characters; available to subscribers, moderators, and broadcaster).
    *   **New Feature:** Added `!fmsg <text>` command — follower in-game message effect (up to 100 characters; requires Follow Effects backend authorization).
    *   **New Feature:** Added Patreon backend support tier verification alongside Twitch subscription checking.
    *   **New Feature:** Connection status in the Twitch sub-tab now displays verified supporter tier (e.g., `Connected • Supporter Verified (Tier 1)`).
    *   **New Feature:** Subscribe button in the Twitch sub-tab is automatically replaced with supporter status text once verification is confirmed.
    *   **New Feature:** Added ranked map auto-protection — all commands/effects are blocked on ScoreSaber, BeatLeader, and AccSaber ranked maps, individually configurable.
    *   **New Feature:** Added permission gating — commands can be restricted to Everyone (default), VIPs only, or Subscribers only.

*   **v1.0.0** (Current)
    *   **Major Update:** Reworked almost the entire mod architecture (chat/backend/Twitch systems) for stability and long-term maintainability.
    *   **Performance Fix:** Full performance pass to reduce/remove frame-drop issues during gameplay.
    *   **New Feature:** Added modular command-processor pipeline for gameplay effects and moderation handling.
    *   **New Feature:** Added/updated Twitch EventSub + Channel Point backend flow (per-reward subscriptions, safer resubscribe behavior, better redemption routing).
    *   **New Feature:** Added ranked-map detection and auto-disable safeguards for score-impacting features.
    *   **New Feature:** Added dedicated `!notecolor` command processor support.
    *   **Fix:** Improved score-submission disable text/state handling.
    *   **Fix:** Multiple Channel Point/EventSub reliability fixes (duplicate handling, quit/shutdown safety, and command routing).
    *   **Fix:** Fixed Endless mode replies and improved logging/rainbow visual consistency.

*   **v0.3.0**
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
    *   Core effects: `!rainbow`, `!ghost`, `!disappear`, `!bomb`, `!bmsg`, `!flashbang`.
    *   Basic speed commands (`!faster`, `!slower`).
    *   Basic configuration UI.

---

## License

**Copyright © PhoenixBlaze0 2025-26**

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
    *   **[StreamPartyCommands](https://github.com/denpadokei/StreamPartyCommand)** – For ideas on interactive viewer commands like bombs.

---

**Enjoy making your streams chaotic with Beat Surgeon!**
