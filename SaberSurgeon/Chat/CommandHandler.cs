using SaberSurgeon.Gameplay;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace SaberSurgeon.Chat
{
    public class CommandHandler
    {
        private static CommandHandler _instance;
        public static CommandHandler Instance => _instance ?? (_instance = new CommandHandler());

        private readonly Dictionary<string, CommandDelegate> _commands;
        private readonly Dictionary<string, DateTime> _commandCooldowns;
        private readonly TimeSpan _cooldownDuration = TimeSpan.FromMinutes(1);
        private bool _isInitialized = false;
        private delegate bool CommandDelegate(object ctxObj, string fullCommand);

        //Add lock object for thread-safe cooldown tracking
        //private static readonly object _cooldownDictLock = new object();
        //private static readonly Dictionary<string, float> _cooldownDict = new Dictionary<string, float>();


        // Bomb command keyword (without leading '!')
        public static string BombCommandName { get; set; } = "bomb";

        // Settings controlled by the SaberSurgeon menu
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
        public static float FasterCooldownSeconds { get; set; } = 60f;

        // SuperFast command
        public static bool SuperFastEnabled { get; set; } = false;
        public static float SuperFastCooldownSeconds { get; set; } = 60f;

        // Slower command
        public static bool SlowerEnabled { get; set; } = true;
        public static float SlowerCooldownSeconds { get; set; } = 60f;

        // Speed command exclusivity
        public static bool SpeedExclusiveEnabled { get; set; } = true;


        // Flashbang command toggle + cooldown
        public static bool FlashbangEnabled { get; set; } = true;
        public static float FlashbangCooldownSeconds { get; set; } = 60f;


        public static bool SongRequestsEnabled { get; set; } = true;
        public static bool RequestAllowSpecificDifficulty { get; set; } = true;
        public static bool RequestAllowSpecificTime { get; set; } = true;





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


        private CommandHandler()
        {
            _commands = new Dictionary<string, CommandDelegate>(StringComparer.OrdinalIgnoreCase);
            _commandCooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        public void Initialize()
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

                RainbowCooldownSeconds = cfg.RainbowCooldownSeconds;
                DisappearCooldownSeconds = cfg.DisappearCooldownSeconds;
                GhostCooldownSeconds = cfg.GhostCooldownSeconds;
                BombCooldownSeconds = cfg.BombCooldownSeconds;
                FasterCooldownSeconds = cfg.FasterCooldownSeconds;
                SuperFastCooldownSeconds = cfg.SuperFastCooldownSeconds;
                SlowerCooldownSeconds = cfg.SlowerCooldownSeconds;
                FlashbangCooldownSeconds = cfg.FlashbangCooldownSeconds;

                _isInitialized = true;
                Plugin.Log.Info($"CommandHandler: Ready! ({_commands.Count} commands registered)");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"CommandHandler: Exception during initialization: {ex}");
            }
        }

        private void RegisterCommands()
        {
            RegisterCommand("surgeon", HandleSurgeonCommand);
            RegisterCommand("bsr", HandleBsrCommand);
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
            RegisterCommand("sr", HandleSrCommand);
            RegisterCommand("bsr", HandleSrCommand);

        }

        private void RegisterCommand(string name, CommandDelegate handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null)
                return;

            _commands[name.ToLower()] = handler;
            Plugin.Log.Info($"CommandHandler: Registered !{name}");
        }

        /// <summary>
        /// Entry point called by ChatManager when a chat line starting with '!' is received.
        /// </summary>
        public void ProcessCommand(string messageText, ChatContext context)
        {
            try
            {
                if (string.IsNullOrEmpty(messageText) || !messageText.StartsWith("!"))
                    return;

                var parts = messageText.Substring(1)
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return;

                var commandName = parts[0].ToLower();

                // Map custom bomb alias to the internal "bomb" command
                if (!string.Equals(BombCommandName, "bomb", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(commandName, BombCommandName, StringComparison.OrdinalIgnoreCase))
                {
                    commandName = "bomb";
                }

                if (!_commands.ContainsKey(commandName))
                {
                    Plugin.Log.Debug($"CommandHandler: Unknown command: !{commandName}");
                    return;
                }

                // owner bypass
                bool isadmin = string.Equals(
                    context?.SenderName,
                    "phoenixblaze0",
                    StringComparison.OrdinalIgnoreCase);

                bool skipCooldown = IsCommandExcludedFromCooldown(commandName);

                // Cooldown check
                if (!isadmin && IsCommandOnCooldown(commandName, out TimeSpan remainingTime))
                {
                    Plugin.Log.Info(
                        $"CommandHandler: !{commandName} on cooldown for {remainingTime.TotalSeconds:F0} more seconds");
                    ChatManager.GetInstance().SendChatMessage(
                        $"!Command !{commandName} is on cooldown. Try again in {remainingTime.TotalSeconds:F0} seconds.");
                    return;
                }

                // Optional: log which backend this message came from
                string sourceLabel = context?.Source.ToString() ?? "Unknown";

                Plugin.Log.Info(
                    $"CommandHandler: Executing !{commandName} from {context.SenderName} " +
                    $"(Source={sourceLabel}, Mod={context.IsModerator}, VIP={context.IsVip}, " +
                    $"Sub={context.IsSubscriber}, Bits={context.Bits})");

                var handler = _commands[commandName];

                // handler returns true only if effect actually triggered / command “succeeded”
                bool succeeded = handler != null && handler(context, messageText);

                if (succeeded && !isadmin && !skipCooldown)
                {
                    SetCommandCooldown(commandName);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"CommandHandler: Error processing command:  {ex.Message}");
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
                    null);
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
                bool startedRainbow = Gameplay.RainbowManager.Instance.StartRainbow(30f);
                if (!startedRainbow)
                {
                    SendResponse(
                        "NoteColor-rainbow ignored: not in a map",
                        "!!Rainbow can only be used while you are playing a song.");
                    return false;
                }

                SendResponse(
                    $"NoteColor rainbow mode started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                    null);
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
                        seconds = RainbowCooldownSeconds;
                        break;
                    case "ghost":
                        seconds = GhostCooldownSeconds;
                        break;
                    case "disappear":
                        seconds = DisappearCooldownSeconds;
                        break;
                    case "bomb":
                        seconds = BombCooldownSeconds;
                        break;
                    case "faster":
                        seconds = FasterCooldownSeconds;
                        break;
                    case "superfast":
                        seconds = SuperFastCooldownSeconds;
                        break;
                    case "slower":
                        seconds = SlowerCooldownSeconds;
                        break;
                    case "flashbang":
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

        // ===== Command handlers =====

        private bool HandleSurgeonCommand(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;

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

            return $"!SaberSurgeon v{version} | Enabled Commands: {commandsPart} | {noteColorHelp}";
        }




        private bool HandleFlashbangCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!FlashbangEnabled)
            {
                SendResponse(
                    "Flashbang command disabled via menu",
                    null);
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
            bool started = Gameplay.FlashbangManager.Instance.TriggerFlashbang(25f, 1f, 3f);
            if (!started)
            {
                SendResponse(
                    "Flashbang ignored: not in a map",
                    "!!Flashbang can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Flashbang triggered (requested by {ctx?.SenderName ?? "Unknown"})",
                null);
            return true; // success → cooldown applies
        }



        private bool HandleFasterCommand(object ctxObj, string fullCommand)
        {
            if (!FasterEnabled)
            {
                SendResponse(
                    "Faster command is disabled via menu",
                    null);
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
                1.2f,          // +20%
                30f,
                "SaberSurgeon Faster");

            if (!started)
            {
                SendResponse(
                    "Faster ignored: not in a map",
                    "!!Faster can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Faster started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                null);
            return true;
        }


        private bool HandleSuperFastCommand(object ctxObj, string fullCommand)
        {
            if (!SuperFastEnabled)
            {
                SendResponse(
                    "SuperFast command is disabled via menu",
                    null);
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
                1.5f,          // +50%
                30f,
                "SaberSurgeon SuperFast");

            if (!started)
            {
                SendResponse(
                    "SuperFast ignored: not in a map",
                    null);
                return false;
            }

            SendResponse(
                $"SuperFast started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                null);
            return true;
        }


        private bool HandleSlowerCommand(object ctxObj, string fullCommand)
        {
            if (!SlowerEnabled)
            {
                SendResponse(
                    "Slower command is disabled via menu",
                    null);
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
                0.85f,         // -15%
                30f,
                "SaberSurgeon Slower");

            if (!started)
            {
                SendResponse(
                    "Slower ignored: not in a map",
                    "!!Slower can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Slower started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                null);
            return true;
        }



        private bool HandleBombCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!BombEnabled)
            {
                SendResponse(
                    "Bomb command disabled via menu", null);
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
                    null);
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

            // Block if ghost is already active
            if (Gameplay.GhostNotesManager.GhostActive)
            {
                SendResponse(
                    "DisappearingArrows blocked: ghost notes already active",
                    "!!Cannot enable Disappearing Arrows while Ghost notes is active. Please try again after it ends.");
                return false; // no cooldown
            }

            bool started = Gameplay.DisappearingArrowsManager.Instance.StartDisappearingArrows(30f);
            if (!started)
            {
                SendResponse(
                    "DisappearingArrows ignored: not in a map",
                    null);
                return false; // no cooldown
            }

            SendResponse(
                $"Disappearing arrows started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                null);

            return true; // effect actually started → apply cooldown
        }



        private bool HandleGhostCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!GhostEnabled)
            {
                SendResponse(
                    "Ghost command disabled via menu",
                    null);
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

            bool started = Gameplay.GhostNotesManager.Instance.StartGhost(30f, ctx?.SenderName);
            if (!started)
            {
                SendResponse(
                    "Ghost ignored: not in a map",
                    "!!Ghost notes can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Ghost notes started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                null);

            return true;
        }



        

       


        private bool HandleRainbowCommand(object ctxObj, string fullCommand)
        {
            // Respect the menu toggle
            if (!RainbowEnabled)
            {
                SendResponse(
                    "Rainbow command is disabled via menu",
                    null);
                return false;
            }

            var ctx = ctxObj as ChatContext;

            // Optional: privilege gating
            if (ctx == null) //(ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Rainbow denied: not privileged",
                    null);
                return false;
            }

            bool started = Gameplay.RainbowManager.Instance.StartRainbow(30f);
            if (!started)
            {
                SendResponse(
                    "Rainbow ignored: not in a map",
                    "!!Rainbow can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Rainbow started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                null);

            return true;
        }


        private bool HandleSrCommand(object ctxObj, string fullCommand)
        {
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
                SendResponse($"SR rejected: {reject}", $"!!Request rejected: {reject}");
                return false;
            }

            SendResponse($"SR queued: {code} by {ctx.SenderName}", $"!!@{ctx.SenderName} queued: {code}");
            return true;

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
            return true;
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

                Gameplay.GameplayManager.GetInstance()
                    .QueueSongRequest(bsrCode, requesterName);

                SendResponse(
                    $"BSR request: {bsrCode} from {requesterName}",
                    $"!!@{requesterName} Song requested: !bsr {bsrCode}");

                return true; // success
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"CommandHandler: Error in HandleBsrCommand: {ex.Message}");
                return false; // treat error as failure → no cooldown
            }*/
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
