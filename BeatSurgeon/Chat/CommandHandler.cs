using BeatSurgeon.Gameplay;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BeatSurgeon.Chat
{
    public class CommandHandler
    {
        private static CommandHandler _instance;

        private readonly object _lock = new object();

        public static CommandHandler Instance => _instance ?? (_instance = new CommandHandler());

        private readonly Dictionary<string, CommandDelegate> _commands;
        private readonly Dictionary<string, DateTime> _commandCooldowns;
        private readonly TimeSpan _cooldownDuration = TimeSpan.FromMinutes(1);
        private bool _isInitialized = false;
        private delegate bool CommandDelegate(object ctxObj, string fullCommand);

        // Bomb command keyword (without leading '!')
        public static string BombCommandName { get; set; } = "bomb";

        // Settings controlled by the BeatSurgeon menu
        public static bool PerCommandCooldownsEnabled { get; set; } = false;

        // Global cooldown (applies to all commands if enabled)
        public static bool GlobalCooldownEnabled { get; set; } = true;
        public static float GlobalCooldownSeconds { get; set; } = 60f;

        // Rainbow command toggle
        public static bool RainbowEnabled { get; set; } = true;

        // Rainbow cooldowns 
        public static bool RainbowCooldownEnabled { get; set; } = true;
        public static float RainbowCooldownSeconds { get; set; } = 60f;

        // Disappearing arrows command toggle
        public static bool DisappearEnabled { get; set; } = true;

        // Disappearing arrows cooldown
        public static bool DisappearCooldownEnabled { get; set; } = true;
        public static float DisappearCooldownSeconds { get; set; } = 60f;

        //Ghost command toggle + per-command cooldown
        public static bool GhostEnabled { get; set; } = true;
        public static bool GhostCooldownEnabled { get; set; } = true;
        public static float GhostCooldownSeconds { get; set; } = 60f;

        // Bomb command toggle + cooldown
        public static bool BombEnabled { get; set; } = true;
        public static bool BombCooldownEnabled { get; set; } = true;
        public static float BombCooldownSeconds { get; set; } = 60f;

        // Faster command toggle + cooldown
        public static bool FasterEnabled { get; set; } = false;
        public static bool FasterCooldownEnabled { get; set; } = true;
        public static float FasterCooldownSeconds { get; set; } = 60f;

        // SuperFast command
        public static bool SuperFastEnabled { get; set; } = false;
        public static bool SuperFastCooldownEnabled { get; set; } = true;
        public static float SuperFastCooldownSeconds { get; set; } = 60f;

        // Slower command
        public static bool SlowerEnabled { get; set; } = true;
        public static bool SlowerCooldownEnabled { get; set; } = true;
        public static float SlowerCooldownSeconds { get; set; } = 60f;

        // Speed command exclusivity
        public static bool SpeedExclusiveEnabled { get; set; } = true;


        // Flashbang command toggle + cooldown
        public static bool FlashbangEnabled { get; set; } = true;
        public static bool FlashbangCooldownEnabled { get; set; } = true;
        public static float FlashbangCooldownSeconds { get; set; } = 60f;


        public static bool SongRequestsEnabled { get; set; } = true;
        public static bool RequestAllowSpecificDifficulty { get; set; } = true;
        public static bool RequestAllowSpecificTime { get; set; } = true;


        // ===== GLOBAL ENABLE/DISABLE SYSTEM =====
        /// <summary>
        /// Stores the state of each command before global disable.
        /// Key: command name (lowercase), Value: was enabled?
        /// </summary>
        private static readonly Dictionary<string, bool> _commandStateBeforeDisable =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// When true, all commands are disabled globally (except !surgeon enable/disable).
        /// </summary>
        public static bool GlobalDisableActive { get; private set; } = false;




        // Commands that never use cooldowns (not checked, not set)
        private static readonly HashSet<string> NoCooldownCommands =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                
                "surgeon",
                "bsr",
                "sr"
                        // add "test" or others here
            };

        private static bool IsCommandExcludedFromCooldown(string commandName) =>
            NoCooldownCommands.Contains(commandName);


        // Effect durations (seconds)
        public const float RainbowEffectSeconds = 30f;
        public const float GhostEffectSeconds = 30f;
        public const float DisappearEffectSeconds = 30f;

        public const float SpeedEffectSeconds = 30f;
        public const float FasterMultiplier = 1.2f;
        public const float SuperFastMultiplier = 1.5f;
        public const float SlowerMultiplier = 0.85f;

        public const float FlashbangHoldSeconds = 1f;
        public const float FlashbangFadeSeconds = 3f;
        public static float FlashbangIntensityMultiplier
        {
            get => Plugin.Settings?.FlashbangBrightnessMultiplier ?? 90f;
        }


        private CommandHandler()
        {
            _commands = new Dictionary<string, CommandDelegate>(StringComparer.OrdinalIgnoreCase);
            _commandCooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        public void Initialize()
        {
            lock (_lock)
            {

                if (_isInitialized)
                {
                    Plugin.Log.Warn("CommandHandler: Already initialized!");
                    return;
                }

                try
                {
                    Plugin.Log.Info("CommandHandler: Initializing...");
                    RegisterCommands();

                    var cfg = Plugin.Settings ?? new PluginConfig();

                    BombCommandName = cfg.BombCommandName;

                    SongRequestsEnabled = cfg.SongRequestsEnabled;
                    RequestAllowSpecificDifficulty = cfg.RequestAllowSpecificDifficulty;
                    RequestAllowSpecificTime = cfg.RequestAllowSpecificTime;

                    // Global switches
                    GlobalCooldownEnabled = cfg.GlobalCooldownEnabled;
                    PerCommandCooldownsEnabled = cfg.PerCommandCooldownsEnabled;
                    GlobalCooldownSeconds = cfg.GlobalCooldownSeconds;

                    // Toggles
                    RainbowEnabled = cfg.RainbowEnabled;
                    DisappearEnabled = cfg.DisappearEnabled;
                    GhostEnabled = cfg.GhostEnabled;
                    BombEnabled = cfg.BombEnabled;
                    FasterEnabled = cfg.FasterEnabled;
                    SuperFastEnabled = cfg.SuperFastEnabled;
                    SlowerEnabled = cfg.SlowerEnabled;
                    FlashbangEnabled = cfg.FlashbangEnabled;
                    SpeedExclusiveEnabled = cfg.SpeedExclusiveEnabled;

                    // Cooldowns
                    RainbowCooldownEnabled = cfg.RainbowCooldownSeconds > 0f; // if you want a flag
                    DisappearCooldownEnabled = cfg.DisappearCooldownSeconds > 0f;
                    GhostCooldownEnabled = cfg.GhostCooldownSeconds > 0f;
                    BombCooldownEnabled = cfg.BombCooldownSeconds > 0f;
                    FasterCooldownEnabled = cfg.FasterCooldownSeconds > 0f;
                    SuperFastCooldownEnabled = cfg.SuperFastCooldownSeconds > 0f;
                    SlowerCooldownEnabled = cfg.SlowerCooldownSeconds > 0f;
                    FlashbangCooldownEnabled = cfg.FlashbangCooldownSeconds > 0f;

                    RainbowCooldownSeconds = cfg.RainbowCooldownSeconds;
                    DisappearCooldownSeconds = cfg.DisappearCooldownSeconds;
                    GhostCooldownSeconds = cfg.GhostCooldownSeconds;
                    BombCooldownSeconds = cfg.BombCooldownSeconds;
                    FasterCooldownSeconds = cfg.FasterCooldownSeconds;
                    SuperFastCooldownSeconds = cfg.SuperFastCooldownSeconds;
                    SlowerCooldownSeconds = cfg.SlowerCooldownSeconds;
                    FlashbangCooldownSeconds = cfg.FlashbangCooldownSeconds;

                    Gameplay.RainbowManager.RainbowCycleSpeed = cfg.RainbowCycleSpeed;

                    _isInitialized = true;
                    Plugin.Log.Info($"CommandHandler: Ready! ({_commands.Count} commands registered)");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"CommandHandler: Exception during initialization: {ex}");
                }
            }
        }

        private void RegisterCommands()
        {
            RegisterCommand("surgeon", HandleSurgeonCommand);
            RegisterCommand("rainbow", HandleRainbowCommand);
            RegisterCommand("ghost", HandleGhostCommand);
            RegisterCommand("disappear", HandleDisappearingArrowsCommand);
            RegisterCommand("bomb", HandleBombCommand);
            RegisterCommand("faster", HandleFasterCommand);
            RegisterCommand("superfast", HandleSuperFastCommand);
            RegisterCommand("slower", HandleSlowerCommand);
            RegisterCommand("notecolor", HandleNoteColorCommand);
            RegisterCommand("notecolour", HandleNoteColorCommand);
            RegisterCommand("flashbang", HandleFlashbangCommand);
            // Intentionally do not register !sr/!bsr here.
            // Song request commands are safely disabled in this build.

        }

        private void RegisterCommand(string name, CommandDelegate handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null)
                return;

            lock (_lock) // Lock writing to _commands
            {
                _commands[name.ToLower()] = handler;
            }
            Plugin.Log.Info($"CommandHandler: Registered !{name}");
        }

        public bool ExecuteSyncedCommand(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return false;

            string name = commandName.Trim();
            if (name.StartsWith("!")) name = name.Substring(1);
            name = name.ToLowerInvariant();

            // Apply bomb alias mapping (same behavior as ProcessCommand)
            string bombCmdName = BombCommandName;
            if (!string.Equals(bombCmdName, "bomb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(name, bombCmdName, StringComparison.OrdinalIgnoreCase))
            {
                name = "bomb";
            }

            CommandDelegate handler;
            lock (_lock)
            {
                if (!_commands.TryGetValue(name, out handler) || handler == null)
                    return false;
            }

            // Provide a non-null context so existing handlers don’t reject it
            var ctx = new ChatContext
            {
                SenderName = "RoomHost",
                MessageText = "!" + name,
                IsBroadcaster = true,
                IsModerator = true,
                IsSubscriber = true,
                Source = ChatSource.Unknown,
                IsChannelPoint = true
            };

            try
            {
                return handler(ctx, "!" + name);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"ExecuteSyncedCommand failed for !{name}: {ex}");
                return false;
            }
        }


        /// <summary>
        /// Entry point called by ChatManager when a chat line starting with '!' is received.
        /// </summary>
        public bool ProcessCommand(string messageText, ChatContext context)
        {
            try
            {
                if (string.IsNullOrEmpty(messageText) || !messageText.StartsWith("!"))
                    return false;

                var parts = messageText.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return false;

                var commandName = parts[0].ToLower();

                bool isChannelPoint = context != null && context.IsChannelPoint;
                // 1. Thread-safe alias check
                // We capture local copies of settings to avoid threading issues if settings change
                string bombCmdName = BombCommandName;
                if (!string.Equals(bombCmdName, "bomb", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(commandName, bombCmdName, StringComparison.OrdinalIgnoreCase))
                {
                    commandName = "bomb";
                }

                // 2. Thread-safe Command Lookup
                CommandDelegate handler = null;
                lock (_lock)
                {
                    if (!_commands.ContainsKey(commandName))
                    {
                        // Optional: Log debug only to avoid spam
                        // Plugin.Log.Debug($"CommandHandler: Unknown command: !{commandName}");
                        return false;
                    }
                    handler = _commands[commandName];
                }

                // 3. Permission & Cooldown Checks
                bool isadmin = !isChannelPoint && string.Equals(context?.SenderName, "phoenixblaze0", StringComparison.OrdinalIgnoreCase);
                bool skipCooldown = IsCommandExcludedFromCooldown(commandName);

                bool onCooldown = false;
                TimeSpan remainingTime = TimeSpan.Zero;

                // Lock only the cooldown check
                lock (_lock)
                {
                    onCooldown = IsCommandOnCooldown(commandName, out remainingTime);
                }

                if (!isadmin && onCooldown)
                {
                    Plugin.Log.Info($"CommandHandler: !{commandName} on cooldown for {remainingTime.TotalSeconds:F0}s");
                    ChatManager.GetInstance().SendChatMessage($"!Command !{commandName} is on cooldown. Try again in {remainingTime.TotalSeconds:F0}s.");
                    return false;
                }

                if (!isChannelPoint)
                {
                    var cfg = Plugin.Settings;
                    if (cfg != null)
                    {
                        bool cpBlocks =
                            (commandName == "rainbow" || commandName == "notecolor" || commandName == "notecolour") ? cfg.CpRainbowEnabled :
                            (commandName == "disappear") ? cfg.CpDisappearEnabled :
                            (commandName == "ghost") ? cfg.CpGhostEnabled :
                            (commandName == "bomb") ? cfg.CpBombEnabled :
                            (commandName == "faster") ? cfg.CpFasterEnabled :
                            (commandName == "superfast") ? cfg.CpSuperFastEnabled :
                            (commandName == "slower") ? cfg.CpSlowerEnabled :
                            (commandName == "flashbang") ? cfg.CpFlashbangEnabled :
                            false;

                        if (cpBlocks && !string.Equals(commandName, "surgeon", StringComparison.OrdinalIgnoreCase))
                        {
                            ChatManager.GetInstance().SendChatMessage($"!!Use Channel Points for !{commandName} (chat command disabled).");
                            return false;
                        }
                    }
                }

                // Log execution
                string sourceLabel = context?.Source.ToString() ?? "Unknown";
                Plugin.Log.Info($"CommandHandler: Executing !{commandName} from {context.SenderName} (Source={sourceLabel})");

                // 4. Execute Handler (OUTSIDE THE LOCK)
                // This prevents the entire chat system from freezing if a command takes time
                bool succeeded = false;
                try
                {
                    if (handler != null)
                    {
                        succeeded = handler(context, messageText);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.Error($"CommandHandler: Error executing command !{commandName}: {ex}");
                    return false;
                }

                // 5. Apply Cooldown (Thread-safe)
                if (succeeded && !isadmin && !skipCooldown)
                {
                    lock (_lock)
                    {
                        SetCommandCooldown(commandName);
                    }
                }

                return succeeded;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"CommandHandler: Critical error in ProcessCommand: {ex.Message}");
                return false;
            }
        }





        private bool IsOtherSpeedEffectActive(string thisKey)
        {
            var mgr = Gameplay.FasterSongManager.Instance;
            if (!SpeedExclusiveEnabled)
                return false;

            if (!mgr.IsActive)
                return false;

            // Another speed effect is active and it's not this one
            return !string.Equals(mgr.ActiveEffectKey, thisKey, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseColorString(string input, out Color color)
        {
            color = Color.white;

            if (string.IsNullOrEmpty(input))
                return false;

            string s = input.Trim();

            // Unity supports CSS color names and #RRGGBB / #RRGGBBAA via TryParseHtmlString
            if (ColorUtility.TryParseHtmlString(s, out color))
                return true;

            // Allow hex without leading '#'
            if (!s.StartsWith("#") && ColorUtility.TryParseHtmlString("#" + s, out color))
                return true;

            return false;
        }

        private bool HandleNoteColorCommand(object ctxObj, string fullCommand)
        {
            // Share the same enable/disable toggle as !rainbow
            if (!RainbowEnabled)
            {
                SendResponse(
                    "NoteColor command is disabled via menu",
                    "Command Disabled");
                return false;
            }

            var ctx = ctxObj as ChatContext;

            // Same privilege gating as !rainbow
            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "NoteColor denied: not privileged",
                    null);
                return false;
            }

            // Parse arguments: !notecolor <left> <right>
            var parts = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                SendResponse(
                    "NoteColor bad syntax",
                    "!!Usage: !notecolor <leftColor> <rightColor> (names like 'red' or hex like #FF0000).");
                return false;
            }

            string leftArg = parts[1];
            string rightArg = parts[2];

            // Special case: !notecolor rainbow rainbow → just run the normal rainbow effect
            if (leftArg.Equals("rainbow", StringComparison.OrdinalIgnoreCase) &&
                rightArg.Equals("rainbow", StringComparison.OrdinalIgnoreCase))
            {
                bool startedRainbow = Gameplay.RainbowManager.Instance.StartRainbow(RainbowEffectSeconds);
                if (!startedRainbow)
                {
                    SendResponse(
                        "NoteColor-rainbow ignored: not in a map",
                        "!!Rainbow can only be used while you are playing a song.");
                    return false;
                }

                SendResponse(
                    $"NoteColor rainbow mode started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                    "!!Rainbow Started");
                return true;
            }

            // Otherwise: fixed left/right colors
            if (!TryParseColorString(leftArg, out Color leftColor) ||
                !TryParseColorString(rightArg, out Color rightColor))
            {
                SendResponse(
                    "NoteColor invalid color input",
                    "!!Could not parse one of the colors. Use names like 'red' or hex like #FF0000.");
                return false;
            }

            bool started = Gameplay.RainbowManager.Instance.StartNoteColor(leftColor, rightColor, 30f);
            if (!started)
            {
                SendResponse(
                    "NoteColor ignored: not in a map",
                    "!!NoteColor can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"NoteColor started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                $"!!Note colors changed: left={leftArg}, right={rightArg} for 30 seconds!");
            return true;
        }




        /// <summary>Check if a command is currently on cooldown.</summary>
        private bool IsCommandOnCooldown(string commandName, out TimeSpan remainingTime)
        {
            remainingTime = TimeSpan.Zero;

            if (!_commandCooldowns.TryGetValue(commandName, out var cooldownEnd))
                return false;

            var now = DateTime.UtcNow;
            if (now < cooldownEnd)
            {
                remainingTime = cooldownEnd - now;
                return true;
            }

            // Cooldown expired, remove it
            _commandCooldowns.Remove(commandName);
            return false;
        }

        /// <summary>Set cooldown for a command.</summary>
        private void SetCommandCooldown(string commandName)
        {
            if (IsCommandExcludedFromCooldown(commandName))
                return; // !help/!ping/!surgeon etc.

            if (!GlobalCooldownEnabled)
                return; // cooldown system globally disabled

            // Base: global cooldown for everything
            double seconds = GlobalCooldownSeconds;

            // If per-command cooldowns are enabled, override for specific commands
            if (PerCommandCooldownsEnabled)
            {
                switch (commandName.ToLowerInvariant())
                {
                    case "rainbow":
                    case "notecolor":
                    case "notecolour":
                        if (!RainbowCooldownEnabled) return;
                        seconds = RainbowCooldownSeconds;
                        break;
                    case "ghost":
                        if (!GhostCooldownEnabled) return;
                        seconds = GhostCooldownSeconds;
                        break;
                    case "disappear":
                        if (!DisappearCooldownEnabled) return;
                        seconds = DisappearCooldownSeconds;
                        break;
                    case "bomb":
                        if (!BombCooldownEnabled) return;
                        seconds = BombCooldownSeconds;
                        break;
                    case "faster":
                        if (!FasterCooldownEnabled) return;
                        seconds = FasterCooldownSeconds;
                        break;
                    case "superfast":
                        if (!SuperFastCooldownEnabled) return;
                        seconds = SuperFastCooldownSeconds;
                        break;
                    case "slower":
                        if (!SlowerCooldownEnabled) return;
                        seconds = SlowerCooldownSeconds;
                        break;
                    case "flashbang":
                        if (!FlashbangCooldownEnabled) return;
                        seconds = FlashbangCooldownSeconds;
                        break;
                }
            }

            if (seconds <= 0.0)
                return; // 0 seconds = effectively no cooldown

            var cooldownEnd = DateTime.UtcNow.AddSeconds(seconds);
            _commandCooldowns[commandName] = cooldownEnd;
            Plugin.Log.Debug($"[CommandHandler] Set cooldown for !{commandName} ({seconds}s) until {cooldownEnd:HH:mm:ss}");
        }

        /// <summary>Helper to log and send a chat message.</summary>
        private void SendResponse(string logMessage, string chatMessage)
        {
            Plugin.Log.Info(logMessage);
            if (!string.IsNullOrWhiteSpace(chatMessage))
                ChatManager.GetInstance().SendChatMessage(chatMessage);
        }

        /// <summary>
        /// Returns the applicable cooldown duration in seconds for a command,
        /// using the exact same logic as SetCommandCooldown but without applying it.
        /// Returns 0 if cooldowns are disabled or the command is excluded.
        /// </summary>
        public static double GetCooldownSeconds(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName)) return 0.0;
            if (IsCommandExcludedFromCooldown(commandName)) return 0.0;
            if (!GlobalCooldownEnabled) return 0.0;  // master switch

            double seconds = GlobalCooldownSeconds;

            if (PerCommandCooldownsEnabled)
            {
                switch (commandName.ToLowerInvariant())
                {
                    case "rainbow":
                    case "notecolor":
                    case "notecolour":
                        if (!RainbowCooldownEnabled) return 0.0;
                        seconds = RainbowCooldownSeconds;
                        break;
                    case "ghost":
                        if (!GhostCooldownEnabled) return 0.0;
                        seconds = GhostCooldownSeconds;
                        break;
                    case "disappear":
                        if (!DisappearCooldownEnabled) return 0.0;
                        seconds = DisappearCooldownSeconds;
                        break;
                    case "bomb":
                        if (!BombCooldownEnabled) return 0.0;
                        seconds = BombCooldownSeconds;
                        break;
                    case "faster":
                        if (!FasterCooldownEnabled) return 0.0;
                        seconds = FasterCooldownSeconds;
                        break;
                    case "superfast":
                        if (!SuperFastCooldownEnabled) return 0.0;
                        seconds = SuperFastCooldownSeconds;
                        break;
                    case "slower":
                        if (!SlowerCooldownEnabled) return 0.0;
                        seconds = SlowerCooldownSeconds;
                        break;
                    case "flashbang":
                        if (!FlashbangCooldownEnabled) return 0.0;
                        seconds = FlashbangCooldownSeconds;
                        break;
                }
            }

            return seconds;
        }

        // ===== Command handlers =====

        private bool HandleSurgeonCommand(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;
            var parts = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Check for subcommands: !surgeon disable, !surgeon enable, !surgeon <cmd> <enable|disable>
            if (parts.Length >= 2)
            {
                string subcommand = parts[1].ToLowerInvariant();

                if (subcommand == "disable")
                {
                    return HandleSurgeonDisable(ctxObj, fullCommand);
                }
                else if (subcommand == "enable")
                {
                    return HandleSurgeonEnable(ctxObj, fullCommand);
                }
                else if (parts.Length >= 3 && (parts[2].ToLowerInvariant() == "enable" ||
                                                parts[2].ToLowerInvariant() == "disable" ||
                                                parts[2].ToLowerInvariant() == "on" ||
                                                parts[2].ToLowerInvariant() == "off"))
                {
                    // Per-command toggle: !surgeon <cmd> <enable|disable>
                    return HandleSurgeonCommandToggle(ctxObj, fullCommand);
                }
            }

            // Default: show status
            string msg = BuildSurgeonStatusMessage();
            Plugin.Log.Info($"Surgeon command executed by {ctx?.SenderName ?? "Unknown"}");
            ChatManager.GetInstance().SendChatMessage(msg);
            return true;
        }


        private string BuildSurgeonStatusMessage()
        {
            var enabled = new List<string>();

            // Bomb + alias support (the only alias currently implemented)
            if (BombEnabled)
            {
                string alias = (BombCommandName ?? "bomb").Trim();
                if (string.IsNullOrWhiteSpace(alias)) alias = "bomb";

                // Show both canonical + alias if alias differs
                if (!alias.Equals("bomb", StringComparison.OrdinalIgnoreCase))
                    enabled.Add($"!bomb (alias !{alias})");
                else
                    enabled.Add("!bomb");
            }

            // Rainbow + NoteColor share the same toggle in your code
            if (RainbowEnabled)
            {
                enabled.Add("!rainbow");
                enabled.Add("!notecolor");
            }

            if (DisappearEnabled) enabled.Add("!disappear");
            if (GhostEnabled) enabled.Add("!ghost");
            if (FasterEnabled) enabled.Add("!faster");
            if (SuperFastEnabled) enabled.Add("!superfast");
            if (SlowerEnabled) enabled.Add("!slower");
            if (FlashbangEnabled) enabled.Add("!flashbang");

            string commandsPart = enabled.Count > 0
                ? string.Join(" | ", enabled)
                : "(no commands enabled in menu)";

            string noteColorHelp = "";
            if (RainbowEnabled)
            {
                // Keep it compact so it fits in one chat line
                noteColorHelp =
                    " || NoteColor: !notecolor <left> <right> (names or hex). " +
                    "Examples: !notecolor red blue, !notecolor #FF0000 #0000FF, or !notecolor rainbow rainbow";
            }

            string backend = ChatManager.GetInstance()?.ActiveBackend.ToString() ?? "Unknown";
            string version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";
            string globalStatus = GlobalDisableActive ? " [GLOBALLY DISABLED]" : "";


            return $"!BeatSurgeon v{version}{globalStatus} | Enabled Commands: {commandsPart} | {noteColorHelp}";

        }




        private bool HandleFlashbangCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!FlashbangEnabled)
            {
                SendResponse(
                    "Flashbang command disabled via menu",
                    "Command Disabled");
                return false;
            }

            if (GlobalDisableActive)
            {
                SendResponse(
                    "Flashbang blocked: global disable active",
                    "!!Flashbang is blocked by global disable. Use !surgeon enable to restore commands.");
                return false;
            }

            var ctx = ctxObj as ChatContext;

            // Optional privilege gating: subs/mods/broadcaster only (same as !bomb / !rainbow)
            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Flashbang denied: not privileged",
                    null);
                return false;
            }

            // 2500% (x25) for 1s, then fade 3s
            bool started = Gameplay.FlashbangManager.Instance.TriggerFlashbang(
                FlashbangIntensityMultiplier,
                FlashbangHoldSeconds,
                FlashbangFadeSeconds);
            if (!started)
            {
                SendResponse(
                    "Flashbang ignored: not in a map",
                    "!!Flashbang can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Flashbang triggered (requested by {ctx?.SenderName ?? "Unknown"})",
                "!!Flashbang Warning");
            return true; // success → cooldown applies
        }



        private bool HandleFasterCommand(object ctxObj, string fullCommand)
        {
            if (!FasterEnabled)
            {
                SendResponse(
                    "Faster command is disabled via menu",
                    "Command Disabled");
                return false;
            }

            if (GlobalDisableActive)
            {
                SendResponse(
                    "Faster blocked: global disable active",
                    "!!Faster is blocked by global disable. Use !surgeon enable to restore commands.");
                return false;
            }

            var ctx = ctxObj as ChatContext;

            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Faster denied: not privileged",
                    null);
                return false;
            }

            if (IsOtherSpeedEffectActive("faster"))
            {
                SendResponse(
                    "Faster blocked: another speed effect is already active",
                    "!!Another speed command is already active. Wait for it to end before using !faster.");
                return false;
            }

            bool started = Gameplay.FasterSongManager.Instance.StartSpeedEffect(
                "faster",
                FasterMultiplier,
                SpeedEffectSeconds,
                "BeatSurgeon Faster");

            if (!started)
            {
                SendResponse(
                    "Faster ignored: not in a map",
                    "!!Faster can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Faster started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                "!!Starting Faster Song");
            return true;
        }


        private bool HandleSuperFastCommand(object ctxObj, string fullCommand)
        {
            if (!SuperFastEnabled)
            {
                SendResponse(
                    "SuperFast command is disabled via menu",
                    "Command Disabled");
                return false;
            }

            if (GlobalDisableActive)
            {
                SendResponse(
                    "SuperFast blocked: global disable active",
                    "!!SuperFast is blocked by global disable. Use !surgeon enable to restore commands.");
                return false;
            }

            var ctx = ctxObj as ChatContext;

            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "SuperFast denied: not privileged",
                    null);
                return false;
            }

            if (IsOtherSpeedEffectActive("superfast"))
            {
                SendResponse(
                    "SuperFast blocked: another speed effect is already active",
                    "!!Another speed command is already active. Wait for it to end before using !superfast.");
                return false;
            }

            bool started = Gameplay.FasterSongManager.Instance.StartSpeedEffect(
                "superfast",
                SuperFastMultiplier,          // +50%
                SpeedEffectSeconds,
                "BeatSurgeon SuperFast");

            if (!started)
            {
                SendResponse(
                    "SuperFast ignored: not in a map",
                    null);
                return false;
            }

            SendResponse(
                $"SuperFast started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                "!!Starting SuperFast Song");
            return true;
        }


        private bool HandleSlowerCommand(object ctxObj, string fullCommand)
        {
            if (!SlowerEnabled)
            {
                SendResponse(
                    "Slower command is disabled via menu",
                    "Command Disabled");
                return false;
            }
            if (GlobalDisableActive)
            {
                SendResponse(
                    "Slower blocked: global disable active",
                    "!!Slower is blocked by global disable.");
                return false;
            }
            var ctx = ctxObj as ChatContext;

            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Slower denied: not privileged",
                    null);
                return false;
            }

            if (IsOtherSpeedEffectActive("slower"))
            {
                SendResponse(
                    "Slower blocked: another speed effect is already active",
                    "!!Another speed command is already active. Wait for it to end before using !slower.");
                return false;
            }

            bool started = Gameplay.FasterSongManager.Instance.StartSpeedEffect(
                "slower",
                SlowerMultiplier,         // -15%
                SpeedEffectSeconds,
                "BeatSurgeon Slower");

            if (!started)
            {
                SendResponse(
                    "Slower ignored: not in a map",
                    "!!Slower can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Slower started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                "!!Starting Slower Song");
            return true;
        }



        private bool HandleBombCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!BombEnabled)
            {
                SendResponse(
                    "Bomb command disabled via menu", "Command Disabled");
                return false;
            }

            var ctx = ctxObj as ChatContext;

            // Optional privilege gating: subs/mods/broadcaster only
            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Bomb denied: not privileged",
                    null);
                return false;
            }
            if (GlobalDisableActive)
            {
                SendResponse(
                    "Bomb blocked: global disable active",
                    "!!Bombs are blocked by global disable.");
                return false;
            }
            string name = ctx?.SenderName ?? "Unknown";

            bool armed = Gameplay.BombManager.Instance.ArmBomb(name, BombCooldownSeconds);
            if (!armed)
            {
                SendResponse(
                    "Bomb ignored: not in a map",
                    "!!Bombs can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Bomb armed by {name}",
                null);

            return true; // arm succeeded → apply cooldown
        }


        private bool HandleDisappearingArrowsCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!DisappearEnabled)
            {
                SendResponse(
                    "DisappearingArrows command disabled via menu",
                    "Command Disabled");
                return false; // no cooldown
            }

            var ctx = ctxObj as ChatContext;

            // Optional privilege gating
            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "DisappearingArrows denied: not privileged",
                    null);
                return false; // no cooldown
            }
            if (GlobalDisableActive)
            {
                SendResponse(
                    "Disappear blocked: global disable active",
                    "!!Disappear is blocked by global disable.");
                return false;
            }

            // Block if ghost is already active
            if (Gameplay.GhostNotesManager.GhostActive)
            {
                SendResponse(
                    "DisappearingArrows blocked: ghost notes already active",
                    "!!Cannot enable Disappearing Arrows while Ghost notes is active. Please try again after it ends.");
                return false; // no cooldown
            }

            bool started = Gameplay.DisappearingArrowsManager.Instance.StartDisappearingArrows(DisappearEffectSeconds);
            if (!started)
            {
                SendResponse(
                    "DisappearingArrows ignored: not in a map",
                    null);
                return false; // no cooldown
            }

            SendResponse(
                $"Disappearing arrows started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                "!!Who needs to see arrows anyway!");

            return true; // effect actually started → apply cooldown
        }



        private bool HandleGhostCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!GhostEnabled)
            {
                SendResponse(
                    "Ghost command disabled via menu",
                    "Command Disabled");
                return false;
            }
            if (GlobalDisableActive)
            {
                SendResponse(
                    "Ghost blocked: global disable active",
                    "!!Ghost is blocked by global disable.");
                return false;
            }

            var ctx = ctxObj as ChatContext;

            // Optional privilege gating
            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Ghost denied: not privileged",
                    null);
                return false;
            }

            // Block if Disappearing Arrows is active
            if (Gameplay.DisappearingArrowsManager.DisappearingActive)
            {
                SendResponse(
                    "Ghost blocked: disappearing arrows already active",
                    "!!Cannot enable Ghost notes while Disappearing Arrows is active. Please try again after it ends.");
                return false;
            }

            bool started = Gameplay.GhostNotesManager.Instance.StartGhost(GhostEffectSeconds, ctx?.SenderName);
            if (!started)
            {
                SendResponse(
                    "Ghost ignored: not in a map",
                    "!!Ghost notes can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Ghost notes started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                "!!Blocks? What Blocks? There's only Arrows!");

            return true;
        }








        private bool HandleRainbowCommand(object ctxObj, string fullCommand)
        {
            if (!RainbowEnabled)
            {
                SendResponse("Rainbow command is disabled via menu", "Command Disabled");
                return false;
            }

            if (GlobalDisableActive)
            {
                SendResponse("Rainbow blocked: global disable active", "!!Rainbow is blocked by global disable.");
                return false;
            }

            var ctx = ctxObj as ChatContext;
            bool isChannelPoint = ctx != null && ctx.IsChannelPoint;

            // Optional privilege gating for chat-only.
            if (!isChannelPoint)
            {
                if (ctx == null || (!ctx.IsSubscriber && !ctx.IsModerator && !ctx.IsBroadcaster))
                {
                    SendResponse("Rainbow denied: not privileged", null);
                    return false;
                }
            }

            bool started = Gameplay.RainbowManager.Instance.StartRainbow(RainbowEffectSeconds);
            if (!started)
            {
                SendResponse("Rainbow ignored: not in a map", "!!Rainbow can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Rainbow started for 30s requested by {ctx?.SenderName ?? "Unknown"}",
                "!!All The Colours!"
            );
            return true;
        }




        private bool HandleSrCommand(object ctxObj, string fullCommand)
        {
            /*
            if (!SongRequestsEnabled)
            {
                SendResponse("SR command disabled via menu", "!!Song requests are disabled.");
                return false;
            }

            var ctx = ctxObj as ChatContext;
            if (ctx == null)
                return false;

            var parts = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                SendResponse("SR bad syntax", "!!Usage: !sr <bsrCode> [diff] [time or range]");
                return false;
            }

            string code = parts[1].Trim();

            BeatmapDifficulty? diff = null;
            float? start = null;
            float? length = null;
            float? switchAfter = null;

            // Parse extra args: diff / time / now
            for (int i = 2; i < parts.Length; i++)
            {
                string arg = parts[i].Trim();

                // "now" means: switch ASAP when this request becomes playable
                if (arg.Equals("now", StringComparison.OrdinalIgnoreCase))
                {
                    switchAfter = 0f;
                    continue;
                }

                if (RequestAllowSpecificDifficulty && diff == null && TryParseDifficulty(arg, out var parsedDiff))
                {
                    diff = parsedDiff;
                    continue;
                }

                if (RequestAllowSpecificTime && start == null && TryParseTimeOrRange(arg, out var s, out var len))
                {
                    start = s;
                    length = len;

                    // Your existing behavior: a single time "m:ss" sets SwitchAfterSeconds
                    if (len == null && switchAfter == null)
                        switchAfter = s;

                    continue;
                }
            }

            string reject;
            bool ok = Gameplay.GameplayManager.GetInstance().TryQueueSongRequest(
                code,
                ctx.SenderName,
                diff,
                start,
                switchAfter,
                length,
                out reject
            );

            if (!ok)
            {
                //SendResponse($"SR rejected: {reject}", $"!!Request rejected: {reject}");
                return false;
            }

            SendResponse($"SR queued: {code} by {ctx.SenderName}", $"!!@{ctx.SenderName} queued: {code}");
            */
            return false;

        }

        private bool TryParseDifficulty(string s, out BeatmapDifficulty diff)
        {
            diff = BeatmapDifficulty.Normal;
            if (string.IsNullOrWhiteSpace(s)) return false;

            s = s.Trim().ToLowerInvariant();
            switch (s)
            {
                case "easy":
                case "eas": diff = BeatmapDifficulty.Easy; return true;

                case "normal":
                case "norm":
                case "n": diff = BeatmapDifficulty.Normal; return true;

                case "hard":
                case "h": diff = BeatmapDifficulty.Hard; return true;

                case "expert":
                case "ex":
                case "x": diff = BeatmapDifficulty.Expert; return true;

                case "expertplus":
                case "expert+":
                case "ex+":
                case "xp":
                case "e+": diff = BeatmapDifficulty.ExpertPlus; return true;
            }

            return false;
        }

        private bool TryParseTimeOrRange(string s, out float startSeconds, out float? lengthSeconds)
        {
            startSeconds = 0f;
            lengthSeconds = null;

            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            // Range: "m:ss-m:ss"
            int dash = s.IndexOf('-');
            if (dash > 0)
            {
                var a = s.Substring(0, dash);
                var b = s.Substring(dash + 1);
                if (!TryParseTime(a, out float start)) return false;
                if (!TryParseTime(b, out float end)) return false;
                if (end <= start) return false;

                startSeconds = start;
                lengthSeconds = end - start;
                return true;
            }

            // Single time: "m:ss"
            if (TryParseTime(s, out float single))
            {
                startSeconds = single;
                lengthSeconds = null;
                return true;
            }

            return false;
        }

        private bool TryParseTime(string s, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrWhiteSpace(s)) return false;

            // Accept "m:ss" or "mm:ss"
            var parts = s.Split(':');
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sec)) return false;
            if (m < 0 || sec < 0 || sec >= 60) return false;

            seconds = (m * 60) + sec;
            return true;
        }

        private bool HandleBsrCommand(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;
            return false;
            /*
            try
            {
                var parts = fullCommand.Split(
                    new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    SendResponse(
                        "BSR command needs code",
                        "!!Usage: !bsr de> (example: !bsr 25f)");
                    return false; // no cooldown on bad syntax
                }

                string bsrCode = parts[1].Trim();
                string requesterName = ctx?.SenderName ?? "Unknown";

                string rejectReason;
                bool queued = Gameplay.GameplayManager.GetInstance().TryQueueSongRequest(
                    bsrCode,
                    requesterName,
                    requestedDifficulty: null,
                    startTimeSeconds: null,
                    switchAfterSeconds: null,
                    segmentLengthSeconds: null,
                    rejectReason: out rejectReason);

                if (!queued)
                {
                    SendResponse(
                        $"BSR rejected: {rejectReason}",
                        $"!!Request rejected: {rejectReason}");
                    return false;
                }

                SendResponse(
                    $"BSR request: {bsrCode} from {requesterName}",
                    $"!!@{requesterName} Song requested: !bsr {bsrCode}");

                return true; // success
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"CommandHandler: Error in HandleBsrCommand: {ex.Message}");
                return false; // treat error as failure → no cooldown
            } */
        }

        // ===== GLOBAL ENABLE/DISABLE HANDLERS =====

        /// <summary>
        /// Handle !surgeon disable - disables all active commands globally
        /// </summary>
        /// <summary>
        /// Handle !surgeon disable - disables all active commands globally (MOD-ONLY)
        /// </summary>
        private bool HandleSurgeonDisable(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;

            // MOD-ONLY permission check
            if (ctx != null && !(ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    $"Permission denied: {ctx.SenderName} attempted !surgeon disable",
                    $"Sorry {ctx.SenderName}, !surgeon disable is a mods only command");
                return false;
            }

            lock (_lock)
            {
                // FIX: Only save state if we aren't ALREADY disabled, otherwise we overwrite valid state with "false"
                if (!GlobalDisableActive)
                {
                    _commandStateBeforeDisable.Clear();
                    _commandStateBeforeDisable["rainbow"] = RainbowEnabled;
                    _commandStateBeforeDisable["disappear"] = DisappearEnabled;
                    _commandStateBeforeDisable["ghost"] = GhostEnabled;
                    _commandStateBeforeDisable["bomb"] = BombEnabled;
                    _commandStateBeforeDisable["faster"] = FasterEnabled;
                    _commandStateBeforeDisable["superfast"] = SuperFastEnabled;
                    _commandStateBeforeDisable["slower"] = SlowerEnabled;
                    _commandStateBeforeDisable["flashbang"] = FlashbangEnabled;

                    // Now disable
                    RainbowEnabled = false;
                    DisappearEnabled = false;
                    GhostEnabled = false;
                    BombEnabled = false;
                    FasterEnabled = false;
                    SuperFastEnabled = false;
                    SlowerEnabled = false;
                    FlashbangEnabled = false;

                    GlobalDisableActive = true;
                    SendResponse("Global Disable Activated", "!!All Surgeon commands disabled.");
                }
                else
                {
                    SendResponse("Already Disabled", "!!Surgeon is already disabled.");
                }
            }
            return true;
        }


        /// <summary>
        /// Handle !surgeon enable - restores all commands to their pre-disable state (MOD-ONLY)
        /// </summary>
        private bool HandleSurgeonEnable(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;

            // MOD-ONLY permission check
            if (ctx != null && !(ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    $"Permission denied: {ctx.SenderName} attempted !surgeon enable",
                    $"Sorry {ctx.SenderName}, !surgeon enable is a mods only command");
                return false;
            }

            if (!GlobalDisableActive)
            {
                SendResponse(
                    "Global disable not active",
                    "!!No commands are currently disabled. Use !surgeon disable to disable all.");
                return false;
            }

            // Restore from saved state
            if (_commandStateBeforeDisable.ContainsKey("rainbow"))
                RainbowEnabled = _commandStateBeforeDisable["rainbow"];
            if (_commandStateBeforeDisable.ContainsKey("disappear"))
                DisappearEnabled = _commandStateBeforeDisable["disappear"];
            if (_commandStateBeforeDisable.ContainsKey("ghost"))
                GhostEnabled = _commandStateBeforeDisable["ghost"];
            if (_commandStateBeforeDisable.ContainsKey("bomb"))
                BombEnabled = _commandStateBeforeDisable["bomb"];
            if (_commandStateBeforeDisable.ContainsKey("faster"))
                FasterEnabled = _commandStateBeforeDisable["faster"];
            if (_commandStateBeforeDisable.ContainsKey("superfast"))
                SuperFastEnabled = _commandStateBeforeDisable["superfast"];
            if (_commandStateBeforeDisable.ContainsKey("slower"))
                SlowerEnabled = _commandStateBeforeDisable["slower"];
            if (_commandStateBeforeDisable.ContainsKey("flashbang"))
                FlashbangEnabled = _commandStateBeforeDisable["flashbang"];

            _commandStateBeforeDisable.Clear();
            GlobalDisableActive = false;

            SendResponse(
                $"Global enable activated by {ctx?.SenderName ?? "Unknown"}",
                $"!!Surgeon Enabled");

            Plugin.Log.Info($"[CommandHandler] Global enable activated by {ctx?.SenderName}");
            return true;
        }


        /// <summary>
        /// Handle per-command enable/disable: !surgeon rainbow disable, !surgeon bomb enable, etc.
        /// </summary>
        /// <summary>
        /// Handle per-command enable/disable: !surgeon rainbow disable, !surgeon bomb enable, etc. (MOD-ONLY)
        /// </summary>
        /// <summary>
        /// Handle per-command enable/disable: !surgeon rainbow disable, !surgeon bomb enable, etc. (MOD-ONLY)
        /// </summary>
        private bool HandleSurgeonCommandToggle(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;

            // MOD-ONLY permission check
            if (ctx != null && !(ctx.IsModerator || ctx.IsBroadcaster))
            {
                var parts = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string firstCmd = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "unknown";

                SendResponse(
                    $"Permission denied: {ctx.SenderName} attempted !surgeon {firstCmd}",
                    $"Sorry {ctx.SenderName}, !surgeon {firstCmd} is a mods only command");
                return false;
            }

            var parts_parse = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Expected: !surgeon <targetCommand> <enable|disable>
            if (parts_parse.Length < 3)
            {
                SendResponse(
                    "Surgeon toggle: bad syntax",
                    "!!Usage: !surgeon <command> <enable|disable>. Example: !surgeon rainbow disable");
                return false;
            }

            string targetCommand = parts_parse[1].ToLowerInvariant();
            string action = parts_parse[2].ToLowerInvariant();

            bool enable = false;
            if (action == "enable" || action == "on")
                enable = true;
            else if (action == "disable" || action == "off")
                enable = false;
            else
            {
                SendResponse(
                    "Surgeon toggle: invalid action",
                    $"!!Action must be 'enable' or 'disable', not '{action}'");
                return false;
            }

            // Apply the toggle
            string statusBefore = "";
            bool found = true;

            switch (targetCommand)
            {
                case "rainbow":
                case "notecolor":
                case "notecolour":
                    statusBefore = RainbowEnabled ? "enabled" : "disabled";
                    RainbowEnabled = enable;
                    break;

                case "disappear":
                case "disappearingarrows":
                    statusBefore = DisappearEnabled ? "enabled" : "disabled";
                    DisappearEnabled = enable;
                    break;

                case "ghost":
                case "ghostnotes":
                    statusBefore = GhostEnabled ? "enabled" : "disabled";
                    GhostEnabled = enable;
                    break;

                case "bomb":
                    statusBefore = BombEnabled ? "enabled" : "disabled";
                    BombEnabled = enable;
                    break;

                case "faster":
                    statusBefore = FasterEnabled ? "enabled" : "disabled";
                    FasterEnabled = enable;
                    break;

                case "superfast":
                case "super":
                    statusBefore = SuperFastEnabled ? "enabled" : "disabled";
                    SuperFastEnabled = enable;
                    break;

                case "slower":
                    statusBefore = SlowerEnabled ? "enabled" : "disabled";
                    SlowerEnabled = enable;
                    break;

                case "flashbang":
                case "flash":
                    statusBefore = FlashbangEnabled ? "enabled" : "disabled";
                    FlashbangEnabled = enable;
                    break;

                default:
                    SendResponse(
                        $"Surgeon toggle: unknown command '{targetCommand}'",
                        $"!!Unknown command: {targetCommand}. Try: !surgeon <rainbow|disappear|ghost|bomb|faster|superfast|slower|flashbang> <enable|disable>");
                    return false;
            }

            if (!found)
            {
                SendResponse(
                    $"Surgeon toggle: command not found '{targetCommand}'",
                    $"!!Command {targetCommand} not recognized.");
                return false;
            }

            string newStatus = enable ? "enabled" : "disabled";
            SendResponse(
                $"Surgeon: !{targetCommand} {newStatus} by {ctx?.SenderName ?? "Unknown"}",
                $"!!BeatSurgeon: !{targetCommand} is now {newStatus}");

            Plugin.Log.Info($"[CommandHandler] !{targetCommand} toggled to {newStatus} by {ctx?.SenderName}");
            return true;
        }




        public void Shutdown()
        {
            Plugin.Log.Info("CommandHandler: Shutting down...");
            _commands.Clear();
            _commandCooldowns.Clear();
            _isInitialized = false;
        }
    }
}
