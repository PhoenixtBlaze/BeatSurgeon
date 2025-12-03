using System;
using System.Collections.Generic;
using System.Globalization;
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



        // Settings controlled by the SaberSurgeon menu

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
            RegisterCommand("help", HandleHelpCommand);
            RegisterCommand("test", HandleTestCommand);
            RegisterCommand("ping", HandlePingCommand);
            RegisterCommand("bsr", HandleBsrCommand);
            RegisterCommand("rainbow", HandleRainbowCommand);
            RegisterCommand("ghost", HandleGhostCommand);
            RegisterCommand("disappear", HandleDisappearingArrowsCommand);
            RegisterCommand("bomb", HandleBombCommand);
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
                if (!_commands.ContainsKey(commandName))
                {
                    Plugin.Log.Debug($"CommandHandler: Unknown command: !{commandName}");
                    return;
                }

                // NEW: owner bypass (no cooldown for phoenixblaze0)
                bool isadmin = string.Equals(
                    context?.SenderName,
                    "phoenixblaze0",
                    StringComparison.OrdinalIgnoreCase);

                // Cooldown check
                if (!isadmin && IsCommandOnCooldown(commandName, out TimeSpan remainingTime))
                {
                    Plugin.Log.Info(
                        $"CommandHandler: !{commandName} on cooldown for {remainingTime.TotalSeconds:F0} more seconds");
                    ChatManager.GetInstance().SendChatMessage(
                        $"!Command !{commandName} is on cooldown. Try again in {remainingTime.TotalSeconds:F0} seconds.");
                    return;
                }

                Plugin.Log.Info(
                    $"CommandHandler: Executing !{commandName} from {context.SenderName} " +
                    $"(Mod={context.IsModerator}, VIP={context.IsVip}, Sub={context.IsSubscriber}, Bits={context.Bits})");

                var handler = _commands[commandName];

                // NEW: handler returns true only if effect actually triggered / command “succeeded”
                bool succeeded = handler != null && handler(context, messageText);

                if (succeeded && !isadmin)
                {
                    SetCommandCooldown(commandName);
                }

            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"CommandHandler: Error processing command: {ex.Message}");
            }
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
            double seconds;

            if (GlobalCooldownEnabled)
            {
                seconds = GlobalCooldownSeconds;
            }
            else if (commandName.Equals("rainbow", StringComparison.OrdinalIgnoreCase) && RainbowCooldownEnabled)
            {
                seconds = RainbowCooldownSeconds;
            }
            else if (commandName.Equals("ghost", StringComparison.OrdinalIgnoreCase) && GhostCooldownEnabled)
            {
                seconds = GhostCooldownSeconds;
            }
            else if (commandName.Equals("bomb", StringComparison.OrdinalIgnoreCase) && BombCooldownEnabled)
            {
                seconds = BombCooldownSeconds;
            }
            else
            {
                seconds = 60f;
            }

            var cooldownEnd = DateTime.UtcNow.AddSeconds(seconds);
            _commandCooldowns[commandName] = cooldownEnd;
            Plugin.Log.Debug($"CommandHandler: Set cooldown for !{commandName} until {cooldownEnd:HH:mm:ss}");
        }

        /// <summary>Helper to log and send a chat message.</summary>
        private void SendResponse(string logMessage, string chatMessage)
        {
            Plugin.Log.Info(logMessage);
            ChatManager.GetInstance().SendChatMessage(chatMessage);
        }

        // ===== Command handlers =====

        private bool HandleSurgeonCommand(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;

            SendResponse(
                $"Surgeon command executed by {ctx?.SenderName ?? "Unknown"}",
                "!SaberSurgeon mod is active");

            // This command always “does something”, so treat as success
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
            if (ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Bomb denied: not privileged",
                    "!!You must be a sub or mod to use !bomb.");
                return false;
            }

            string name = ctx?.SenderName ?? "Unknown";

            bool armed = Gameplay.BombManager.Instance.ArmBomb(name);
            if (!armed)
            {
                SendResponse(
                    "Bomb ignored: not in a map",
                    "!!Bombs can only be used while you are playing a song.");
                return false;
            }

            SendResponse(
                $"Bomb armed by {name}",
                $"!!Bomb armed! Next cube might be a bomb from {name}…");

            return true; // arm succeeded → apply cooldown
        }


        private bool HandleDisappearingArrowsCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!DisappearEnabled)
            {
                SendResponse(
                    "DisappearingArrows command disabled via menu",
                    "!!Disappearing Arrows command is currently disabled in the Saber Surgeon settings.");
                return false; // no cooldown
            }

            var ctx = ctxObj as ChatContext;

            // Optional privilege gating
            if (ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "DisappearingArrows denied: not privileged",
                    "!!You must be a sub or mod to use !disappear.");
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
                    "!!Disappearing arrows can only be used while you are playing a song.");
                return false; // no cooldown
            }

            SendResponse(
                $"Disappearing arrows started for 30s (requested by {ctx?.SenderName ?? "Unknown"})",
                "!!Disappearing arrows enabled for 30 seconds!");

            return true; // effect actually started → apply cooldown
        }



        private bool HandleGhostCommand(object ctxObj, string fullCommand)
        {
            // Respect menu toggle
            if (!GhostEnabled)
            {
                SendResponse(
                    "Ghost command disabled via menu",
                    "!!Ghost notes command is currently disabled in the Saber Surgeon settings.");
                return false;
            }

            var ctx = ctxObj as ChatContext;

            // Optional privilege gating
            if (ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Ghost denied: not privileged",
                    "!!You must be a sub or mod to use !ghost.");
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
                "!!Ghost notes enabled for 30 seconds!");

            return true;
        }



        private bool HandleHelpCommand(object ctxObj, string fullCommand)
        {
            Plugin.Log.Info("Available Commands:");
            Plugin.Log.Info("!surgeon - Surgeon status / info");
            Plugin.Log.Info("!help    - Show this message");
            Plugin.Log.Info("!test    - Test command");
            Plugin.Log.Info("!ping    - Ping/Pong");
            Plugin.Log.Info("!bsr     - Queue a map by code");

            ChatManager.GetInstance().SendChatMessage(
                "Commands: !surgeon !help !test !ping !bsr de>");

            // Help always succeeds
            return true;
        }

        private bool HandleTestCommand(object ctxObj, string fullCommand)
        {
            SendResponse(
                $"Test command executed: {fullCommand}",
                $"!!Test successful: {fullCommand}");

            return true;
        }

        private bool HandlePingCommand(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;

            if (ctx != null && !(ctx.IsModerator || ctx.IsSubscriber || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Ping denied: not privileged",
                    "!!You must be a sub or mod to use this command.");

                return false; // no cooldown
            }

            SendResponse("PONG!", "Pong!");
            return true; // success → cooldown applies
        }


        private bool HandleRainbowCommand(object ctxObj, string fullCommand)
        {
            // Respect the menu toggle
            if (!RainbowEnabled)
            {
                SendResponse(
                    "Rainbow command is disabled via menu",
                    "!!Rainbow command is currently disabled in the Saber Surgeon settings.");
                return false;
            }

            var ctx = ctxObj as ChatContext;

            // Optional: privilege gating
            if (ctx != null && !(ctx.IsSubscriber || ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Rainbow denied: not privileged",
                    "!!You must be a sub or mod to use !rainbow.");
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
                "!!Starting rainbow notes for 30 seconds!");

            return true;
        }



        private bool HandleBsrCommand(object ctxObj, string fullCommand)
        {
            var ctx = ctxObj as ChatContext;

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
            }
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
