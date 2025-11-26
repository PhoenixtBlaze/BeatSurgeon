using System;
using System.Collections.Generic;

namespace SaberSurgeon.Chat
{
    public class CommandHandler
    {
        private static CommandHandler _instance;
        public static CommandHandler Instance => _instance ?? (_instance = new CommandHandler());

        private readonly Dictionary<string, Action<object, string>> _commands;
        private readonly Dictionary<string, DateTime> _commandCooldowns;
        private readonly TimeSpan _cooldownDuration = TimeSpan.FromMinutes(1);
        private bool _isInitialized = false;

        private CommandHandler()
        {
            _commands = new Dictionary<string, Action<object, string>>(StringComparer.OrdinalIgnoreCase);
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
        }

        public void RegisterCommand(string name, Action<object, string> handler)
        {
            if (string.IsNullOrEmpty(name) || handler == null)
                return;

            _commands[name.ToLower()] = handler;
            Plugin.Log.Info($"CommandHandler: Registered !{name}");
        }

        public void ProcessCommand(string messageText, string senderName, object message)
        {
            try
            {
                if (string.IsNullOrEmpty(messageText) || !messageText.StartsWith("!"))
                    return;

                var parts = messageText.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return;

                var commandName = parts[0].ToLower();

                if (!_commands.ContainsKey(commandName))
                {
                    Plugin.Log.Debug($"CommandHandler: Unknown command: !{commandName}");
                    return;
                }

                // Check cooldown
                if (IsCommandOnCooldown(commandName, out TimeSpan remainingTime))
                {
                    Plugin.Log.Info($"CommandHandler: !{commandName} on cooldown for {remainingTime.TotalSeconds:F0} more seconds");
                    ChatManager.GetInstance().SendChatMessage(
                        $"Command !{commandName} is on cooldown. Try again in {remainingTime.TotalSeconds:F0} seconds."
                    );
                    return;
                }

                // Execute command
                Plugin.Log.Info($"CommandHandler: Executing !{commandName} from {senderName}");
                var handler = _commands[commandName];
                handler?.Invoke(message, messageText);

                // Set cooldown
                SetCommandCooldown(commandName);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"CommandHandler: Error processing command: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a command is currently on cooldown
        /// </summary>
        private bool IsCommandOnCooldown(string commandName, out TimeSpan remainingTime)
        {
            remainingTime = TimeSpan.Zero;

            if (!_commandCooldowns.ContainsKey(commandName))
                return false;

            var cooldownEnd = _commandCooldowns[commandName];
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

        /// <summary>
        /// Set cooldown for a command
        /// </summary>
        private void SetCommandCooldown(string commandName)
        {
            var cooldownEnd = DateTime.UtcNow.Add(_cooldownDuration);
            _commandCooldowns[commandName] = cooldownEnd;
            Plugin.Log.Debug($"CommandHandler: Set cooldown for !{commandName} until {cooldownEnd:HH:mm:ss}");
        }

        /// <summary>
        /// Helper to log and send chat message
        /// </summary>
        private void SendResponse(string logMessage, string chatMessage)
        {
            Plugin.Log.Info(logMessage);
            ChatManager.GetInstance().SendChatMessage(chatMessage);
        }

        private void HandleSurgeonCommand(object message, string fullCommand)
        {
            SendResponse(
                "Surgeon command executed",
                "SaberSurgeon mod is active"
            );
        }

        private void HandleHelpCommand(object message, string fullCommand)
        {
            Plugin.Log.Info("Available Commands:");
            Plugin.Log.Info("!surgeon - Surgeon command");
            Plugin.Log.Info("!help - Show this message");
            Plugin.Log.Info("!test - Test command");
            Plugin.Log.Info("!ping - Pong!");

            ChatManager.GetInstance().SendChatMessage("Commands: !surgeon !help !test !ping");
        }

        private void HandleTestCommand(object message, string fullCommand)
        {
            SendResponse(
                $"Test command executed: {fullCommand}",
                $"Test successful: {fullCommand}"
            );
        }

        private void HandlePingCommand(object message, string fullCommand)
        {
            SendResponse("PONG!", "Pong!");
        }

        private void HandleBsrCommand(object message, string fullCommand)
        {
            try
            {
                var parts = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                {
                    SendResponse(
                        "BSR command needs code",
                        "Usage: !bsr <code> (example: !bsr 25f)"
                    );
                    return;
                }

                string bsrCode = parts[1].Trim();

                // Extract requester name from message
                var sender = GetPropertyValue(message, "Sender") ??
                            GetPropertyValue(message, "User") ??
                            GetPropertyValue(message, "Author");

                string requesterName = "Unknown";
                if (sender != null)
                {
                    var senderNameObj = GetPropertyValue(sender, "DisplayName") ??
                                       GetPropertyValue(sender, "UserName") ??
                                       GetPropertyValue(sender, "Name");
                    requesterName = senderNameObj?.ToString() ?? "Unknown";
                }

                // Queue the request
                Gameplay.GameplayManager.GetInstance().QueueSongRequest(bsrCode, requesterName);

                SendResponse(
                    $"BSR request: {bsrCode} from {requesterName}",
                    $"@{requesterName} Song requested: !bsr {bsrCode}"
                );
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"CommandHandler: Error in HandleBsrCommand: {ex.Message}");
            }
        }

        // Add helper method (if not already present):
        private object GetPropertyValue(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return null;

                var prop = obj.GetType().GetProperty(propertyName,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase);
                return prop?.GetValue(obj);
            }
            catch
            {
                return null;
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
