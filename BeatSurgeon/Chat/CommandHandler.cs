using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat.Processors;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Chat
{
    internal enum CommandRejectReason
    {
        None,
        NoCommandToken,
        EmptyCommand,
        UnknownCommand,
        GlobalDisabled,
        CommandDisabled,
        InsufficientPermission,
        OnCooldown,
        ProcessorRejected,
        ExecutionFailed,
        Cancelled,
        RankedMap
    }

    internal sealed class CommandExecutionResult
    {
        internal bool Matched { get; private set; }
        internal bool Executed { get; private set; }
        internal string CommandKey { get; private set; }
        internal CommandRejectReason Reason { get; private set; }
        internal TimeSpan? CooldownRemaining { get; private set; }
        internal TriggerSource Source { get; private set; }

        private CommandExecutionResult()
        {
        }

        internal static CommandExecutionResult NotMatched(TriggerSource source, CommandRejectReason reason)
        {
            return new CommandExecutionResult
            {
                Matched = false,
                Executed = false,
                CommandKey = string.Empty,
                Reason = reason,
                CooldownRemaining = null,
                Source = source
            };
        }

        internal static CommandExecutionResult Rejected(string commandKey, TriggerSource source, CommandRejectReason reason, TimeSpan? remaining = null)
        {
            return new CommandExecutionResult
            {
                Matched = true,
                Executed = false,
                CommandKey = commandKey ?? string.Empty,
                Reason = reason,
                CooldownRemaining = remaining,
                Source = source
            };
        }

        internal static CommandExecutionResult Success(string commandKey, TriggerSource source)
        {
            return new CommandExecutionResult
            {
                Matched = true,
                Executed = true,
                CommandKey = commandKey ?? string.Empty,
                Reason = CommandRejectReason.None,
                CooldownRemaining = null,
                Source = source
            };
        }
    }

    internal interface ICommandCooldownService
    {
        bool TryGetCooldownRemaining(string commandKey, out TimeSpan remaining);
        void ApplyCooldown(string normalizedCommand);
        void Reset();
    }

    internal sealed class CommandCooldownService : ICommandCooldownService
    {
        private readonly ConcurrentDictionary<string, DateTime> _cooldowns =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public bool TryGetCooldownRemaining(string commandKey, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            DateTime now = DateTime.UtcNow;

            if (!CommandRuntimeSettings.PerCommandCooldownsEnabled)
            {
                return false;
            }

            string canonicalKey = CommandRuntimeSettings.CanonicalizeCommandKey(commandKey);
            if (string.IsNullOrWhiteSpace(canonicalKey))
            {
                return false;
            }

            if (_cooldowns.TryGetValue(canonicalKey, out DateTime until) && until > now)
            {
                remaining = until - now;
                return true;
            }

            return false;
        }

        public void ApplyCooldown(string normalizedCommand)
        {
            string canonicalKey = CommandRuntimeSettings.CanonicalizeCommandKey(normalizedCommand);
            if (string.IsNullOrWhiteSpace(canonicalKey) || CommandRuntimeSettings.IsCooldownExempt("!" + canonicalKey))
            {
                return;
            }

            if (!CommandRuntimeSettings.PerCommandCooldownsEnabled)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            double perCommand = CommandRuntimeSettings.GetCooldownSeconds(canonicalKey);
            if (perCommand > 0)
            {
                _cooldowns[canonicalKey] = now.AddSeconds(perCommand);
            }
        }

        public void Reset()
        {
            _cooldowns.Clear();
        }
    }

    internal sealed class CommandHandler : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("CommandHandler");

        private static CommandHandler _instance;
        private readonly object _surgeonLock = new object();
        private readonly IEnumerable<ICommandProcessor> _processors;
        private readonly ICommandCooldownService _cooldownService;
        private readonly Dictionary<string, bool> _commandStateBeforeDisable =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, ICommandProcessor> _lookup;

        internal static CommandHandler Instance =>
            _instance ?? (_instance = new CommandHandler(Array.Empty<ICommandProcessor>(), true));

        internal static bool GlobalDisableActive { get; private set; }

        [Inject]
        public CommandHandler(IEnumerable<ICommandProcessor> processors)
        {
            _instance = this;
            _processors = processors ?? Array.Empty<ICommandProcessor>();
            _cooldownService = new CommandCooldownService();
        }

        private CommandHandler(IEnumerable<ICommandProcessor> processors, bool _)
        {
            _processors = processors ?? Array.Empty<ICommandProcessor>();
            _cooldownService = new CommandCooldownService();
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize - building command lookup table");
            _lookup = new Dictionary<string, ICommandProcessor>(StringComparer.OrdinalIgnoreCase);

            foreach (ICommandProcessor processor in _processors)
            {
                foreach (string cmd in processor.HandledCommands)
                {
                    if (_lookup.ContainsKey(cmd))
                    {
                        _log.Warn("Duplicate command keyword '" + cmd + "' from " + processor.GetType().Name + " - skipping");
                        continue;
                    }

                    _lookup[cmd] = processor;
                    _log.Debug("Registered command '" + cmd + "' -> " + processor.GetType().Name);
                }
            }

            if (CommandRuntimeSettings.GlobalCooldownEnabled)
            {
                _log.Warn("Global cooldown is disabled at runtime; using per-command cooldowns.");
                CommandRuntimeSettings.GlobalCooldownEnabled = false;
            }

            if (!CommandRuntimeSettings.PerCommandCooldownsEnabled)
            {
                CommandRuntimeSettings.PerCommandCooldownsEnabled = true;
            }

            _log.Info("Command lookup table built: " + _lookup.Count + " commands across " + _processors.Count() + " processors");
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose");
            _lookup?.Clear();
            _cooldownService.Reset();
        }

        internal Task<CommandExecutionResult> HandleMessageAsync(ChatContext ctx, CancellationToken ct)
        {
            return HandleMessageAsync(ctx, TriggerSource.Chat, ct);
        }

        internal async Task<CommandExecutionResult> HandleMessageAsync(ChatContext ctx, TriggerSource source, CancellationToken ct)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(ctx.MessageText))
            {
                return CommandExecutionResult.NotMatched(source, CommandRejectReason.NoCommandToken);
            }

            if (!ChatContext.TryExtractFirstCommandToken(ctx.MessageText, out string rawCommand, out int commandStart, out int commandLength))
            {
                return CommandExecutionResult.NotMatched(source, CommandRejectReason.NoCommandToken);
            }

            string normalized = CommandRuntimeSettings.NormalizeCommand(rawCommand);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                _log.Warn("Empty command from user=" + ctx.Username + " msgText=" + ctx.MessageText);
                return CommandExecutionResult.NotMatched(source, CommandRejectReason.EmptyCommand);
            }

            string commandKey = CommandRuntimeSettings.CanonicalizeCommandKey(normalized);
            ctx.TriggerSource = source;
            ctx.MessageText = RewriteMessageWithNormalizedCommand(ctx.MessageText, normalized, commandStart, commandLength);

            _log.Info("HandleMessageAsync: user=" + ctx.Username + " normalized=" + normalized);

            if (source == TriggerSource.Chat && TryHandleSurgeonCommand(ctx, normalized))
            {
                _log.Info("SurgeonCommand handled successfully");
                return CommandExecutionResult.Success(commandKey, source);
            }

            if (source == TriggerSource.Chat && GlobalDisableActive)
            {
                _log.Command(ctx.Username, normalized, false, "GlobalDisabled");
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.GlobalDisabled);
            }

            if (source == TriggerSource.Chat && !CommandRuntimeSettings.IsCommandEnabled(normalized))
            {
                _log.Command(ctx.Username, normalized, false, "CommandDisabled");
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.CommandDisabled);
            }

            if (RankedMapDetectionService.Instance.IsCurrentMapRankedOrChecking)
            {
                _log.Command(ctx.Username, normalized, false, "RankedMapBlocked");
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.RankedMap);
            }

            if (_lookup == null || !_lookup.TryGetValue(normalized, out ICommandProcessor processor))
            {
                _log.Warn("Unknown command '" + normalized + "' from user=" + ctx.Username + " available=" + (_lookup?.Count ?? 0));
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.UnknownCommand);
            }

            if (source == TriggerSource.Chat && !CommandRuntimeSettings.IsChatCommandAllowed(ctx))
            {
                _log.Command(ctx.Username, normalized, false, "InsufficientPermission");
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.InsufficientPermission);
            }

            if (!CommandRuntimeSettings.IsCooldownExempt(normalized) &&
                _cooldownService.TryGetCooldownRemaining(commandKey, out TimeSpan remaining))
            {
                _log.Command(ctx.Username, normalized, false, "OnCooldown");
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.OnCooldown, remaining);
            }

            ctx.CooldownChecker = key => _cooldownService.TryGetCooldownRemaining(key, out _);
            _log.Info("Routing command '" + normalized + "' from user=" + ctx.Username + " to " + processor.GetType().Name);

            try
            {
                if (!processor.CanHandle(ctx))
                {
                    _log.Warn("Processor " + processor.GetType().Name + " said it cannot handle the command");
                    return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.ProcessorRejected);
                }

                await processor.ExecuteAsync(ctx, ct).ConfigureAwait(false);
                _cooldownService.ApplyCooldown(normalized);
                _log.Info("Command '" + normalized + "' executed successfully by user=" + ctx.Username);
                return CommandExecutionResult.Success(commandKey, source);
            }
            catch (InvalidOperationException ex)
            {
                _log.Warn("Command '" + normalized + "' rejected: " + ex.Message);
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.ProcessorRejected);
            }
            catch (OperationCanceledException)
            {
                _log.Warn("Command '" + normalized + "' execution was cancelled");
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.Cancelled);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "HandleMessageAsync cmd=" + normalized + " user=" + ctx.Username);
                return CommandExecutionResult.Rejected(commandKey, source, CommandRejectReason.ExecutionFailed);
            }
        }

        private bool TryHandleSurgeonCommand(ChatContext ctx, string normalizedCommand)
        {
            if (!string.Equals(normalizedCommand, "!surgeon", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fullCommand = ctx?.MessageText ?? string.Empty;
            string[] parts = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                string subcommand = parts[1].ToLowerInvariant();

                if (subcommand == "disable")
                {
                    return HandleSurgeonDisable(ctx, fullCommand);
                }

                if (subcommand == "enable")
                {
                    return HandleSurgeonEnable(ctx, fullCommand);
                }

                if (parts.Length >= 3)
                {
                    string action = parts[2].ToLowerInvariant();
                    if (action == "enable" || action == "disable" || action == "on" || action == "off")
                    {
                        return HandleSurgeonCommandToggle(ctx, fullCommand);
                    }
                }
            }

            SendResponse(
                "Surgeon command executed by " + (ctx?.SenderName ?? "Unknown"),
                BuildSurgeonStatusMessage());
            return true;
        }

        private string BuildSurgeonStatusMessage()
        {
            var enabled = new List<string>();

            if (CommandRuntimeSettings.BombEnabled)
            {
                string alias = (CommandRuntimeSettings.BombCommandName ?? "bomb").Trim();
                if (string.IsNullOrWhiteSpace(alias))
                {
                    alias = "bomb";
                }

                if (!alias.Equals("bomb", StringComparison.OrdinalIgnoreCase))
                {
                    enabled.Add("!bomb | !bmsg <text> (bomb alias !" + alias + ")");
                }
                else
                {
                    enabled.Add("!bomb | !bmsg <text>");
                }
            }

            if (CommandRuntimeSettings.RainbowEnabled)
            {
                enabled.Add("!rainbow");
                enabled.Add("!notecolor");
            }

            if (CommandRuntimeSettings.DisappearEnabled) enabled.Add("!disappear");
            if (CommandRuntimeSettings.GhostEnabled) enabled.Add("!ghost");
            if (CommandRuntimeSettings.FasterEnabled) enabled.Add("!faster");
            if (CommandRuntimeSettings.SuperFastEnabled) enabled.Add("!superfast");
            if (CommandRuntimeSettings.SlowerEnabled) enabled.Add("!slower");
            if (CommandRuntimeSettings.FlashbangEnabled) enabled.Add("!flashbang");

            string version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";
            string globalStatus = GlobalDisableActive ? " [GLOBALLY DISABLED]" : string.Empty;

            string commandsPart = enabled.Count > 0
                ? "Enabled commands include " + string.Join(" | ", enabled)
                : "No commands are enabled in the menu";

            string message = "BeatSurgeon v" + version + globalStatus + " | " + commandsPart;

            if (CommandRuntimeSettings.RainbowEnabled)
            {
                message +=
                    " | Note color usage is !notecolor <left> <right> (names or hex). " +
                    "Example commands include !notecolor red blue, !notecolor #FF0000 #0000FF, or !notecolor rainbow rainbow.";
            }

            return message;
        }

        private bool HandleSurgeonDisable(ChatContext ctx, string fullCommand)
        {
            if (ctx != null && !(ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Permission denied: " + ctx.SenderName + " attempted !surgeon disable",
                    "Sorry " + ctx.SenderName + ", the !surgeon disable command is mods only.");
                return false;
            }

            lock (_surgeonLock)
            {
                if (!GlobalDisableActive)
                {
                    _commandStateBeforeDisable.Clear();
                    _commandStateBeforeDisable["rainbow"] = CommandRuntimeSettings.RainbowEnabled;
                    _commandStateBeforeDisable["disappear"] = CommandRuntimeSettings.DisappearEnabled;
                    _commandStateBeforeDisable["ghost"] = CommandRuntimeSettings.GhostEnabled;
                    _commandStateBeforeDisable["bomb"] = CommandRuntimeSettings.BombEnabled;
                    _commandStateBeforeDisable["faster"] = CommandRuntimeSettings.FasterEnabled;
                    _commandStateBeforeDisable["superfast"] = CommandRuntimeSettings.SuperFastEnabled;
                    _commandStateBeforeDisable["slower"] = CommandRuntimeSettings.SlowerEnabled;
                    _commandStateBeforeDisable["flashbang"] = CommandRuntimeSettings.FlashbangEnabled;

                    CommandRuntimeSettings.RainbowEnabled = false;
                    CommandRuntimeSettings.DisappearEnabled = false;
                    CommandRuntimeSettings.GhostEnabled = false;
                    CommandRuntimeSettings.BombEnabled = false;
                    CommandRuntimeSettings.FasterEnabled = false;
                    CommandRuntimeSettings.SuperFastEnabled = false;
                    CommandRuntimeSettings.SlowerEnabled = false;
                    CommandRuntimeSettings.FlashbangEnabled = false;

                    GlobalDisableActive = true;
                    SendResponse("Global Disable Activated", "All Surgeon commands are now disabled.");
                }
                else
                {
                    SendResponse("Already Disabled", "Surgeon is already disabled.");
                }
            }

            return true;
        }

        private bool HandleSurgeonEnable(ChatContext ctx, string fullCommand)
        {
            if (ctx != null && !(ctx.IsModerator || ctx.IsBroadcaster))
            {
                SendResponse(
                    "Permission denied: " + ctx.SenderName + " attempted !surgeon enable",
                    "Sorry " + ctx.SenderName + ", the !surgeon enable command is mods only.");
                return false;
            }

            if (!GlobalDisableActive)
            {
                SendResponse(
                    "Global disable not active",
                    "No commands are currently disabled. To disable all commands, use !surgeon disable.");
                return false;
            }

            if (_commandStateBeforeDisable.ContainsKey("rainbow"))
                CommandRuntimeSettings.RainbowEnabled = _commandStateBeforeDisable["rainbow"];
            if (_commandStateBeforeDisable.ContainsKey("disappear"))
                CommandRuntimeSettings.DisappearEnabled = _commandStateBeforeDisable["disappear"];
            if (_commandStateBeforeDisable.ContainsKey("ghost"))
                CommandRuntimeSettings.GhostEnabled = _commandStateBeforeDisable["ghost"];
            if (_commandStateBeforeDisable.ContainsKey("bomb"))
                CommandRuntimeSettings.BombEnabled = _commandStateBeforeDisable["bomb"];
            if (_commandStateBeforeDisable.ContainsKey("faster"))
                CommandRuntimeSettings.FasterEnabled = _commandStateBeforeDisable["faster"];
            if (_commandStateBeforeDisable.ContainsKey("superfast"))
                CommandRuntimeSettings.SuperFastEnabled = _commandStateBeforeDisable["superfast"];
            if (_commandStateBeforeDisable.ContainsKey("slower"))
                CommandRuntimeSettings.SlowerEnabled = _commandStateBeforeDisable["slower"];
            if (_commandStateBeforeDisable.ContainsKey("flashbang"))
                CommandRuntimeSettings.FlashbangEnabled = _commandStateBeforeDisable["flashbang"];

            _commandStateBeforeDisable.Clear();
            GlobalDisableActive = false;

            SendResponse(
                "Global enable activated by " + (ctx?.SenderName ?? "Unknown"),
                "Surgeon is now enabled.");

            _log.Info("[CommandHandler] Global enable activated by " + ctx?.SenderName);
            return true;
        }

        private bool HandleSurgeonCommandToggle(ChatContext ctx, string fullCommand)
        {
            if (ctx != null && !(ctx.IsModerator || ctx.IsBroadcaster))
            {
                var parts = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string firstCmd = parts.Length >= 2 ? parts[1].ToLowerInvariant() : "unknown";
                SendResponse(
                    "Permission denied: " + ctx.SenderName + " attempted !surgeon " + firstCmd,
                    "Sorry " + ctx.SenderName + ", the !surgeon " + firstCmd + " command is mods only.");
                return false;
            }

            var partsParse = fullCommand.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (partsParse.Length < 3)
            {
                SendResponse(
                    "Surgeon toggle: bad syntax",
                    "To toggle a Surgeon command, use !surgeon <command> <enable|disable>. For example, use !surgeon rainbow disable.");
                return false;
            }

            string targetCommand = partsParse[1].ToLowerInvariant();
            string action = partsParse[2].ToLowerInvariant();
            bool enable;

            if (action == "enable" || action == "on")
                enable = true;
            else if (action == "disable" || action == "off")
                enable = false;
            else
            {
                SendResponse(
                    "Surgeon toggle: invalid action",
                    "!!Action must be 'enable' or 'disable', not '" + action + "'");
                return false;
            }

            switch (targetCommand)
            {
                case "rainbow":
                case "notecolor":
                case "notecolour":
                    CommandRuntimeSettings.RainbowEnabled = enable;
                    targetCommand = "rainbow";
                    break;

                case "disappear":
                case "disappearingarrows":
                    CommandRuntimeSettings.DisappearEnabled = enable;
                    targetCommand = "disappear";
                    break;

                case "ghost":
                case "ghostnotes":
                    CommandRuntimeSettings.GhostEnabled = enable;
                    targetCommand = "ghost";
                    break;

                case "bomb":
                    CommandRuntimeSettings.BombEnabled = enable;
                    break;

                case "faster":
                    CommandRuntimeSettings.FasterEnabled = enable;
                    break;

                case "superfast":
                case "super":
                    CommandRuntimeSettings.SuperFastEnabled = enable;
                    targetCommand = "superfast";
                    break;

                case "slower":
                    CommandRuntimeSettings.SlowerEnabled = enable;
                    break;

                case "flashbang":
                case "flash":
                    CommandRuntimeSettings.FlashbangEnabled = enable;
                    targetCommand = "flashbang";
                    break;

                default:
                    SendResponse(
                        "Surgeon toggle: unknown command '" + targetCommand + "'",
                        "Unknown command: " + targetCommand + ". To change a command state, use !surgeon <rainbow|disappear|ghost|bomb|faster|superfast|slower|flashbang> <enable|disable>.");
                    return false;
            }

            string newStatus = enable ? "enabled" : "disabled";
            SendResponse(
                "Surgeon: !" + targetCommand + " " + newStatus + " by " + (ctx?.SenderName ?? "Unknown"),
                "BeatSurgeon set the !" + targetCommand + " command to " + newStatus + ".");

            _log.Info("[CommandHandler] !" + targetCommand + " toggled to " + newStatus + " by " + ctx?.SenderName);
            return true;
        }

        private void SendResponse(string logMessage, string chatMessage)
        {
            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                _log.Info(logMessage);
            }

            if (!string.IsNullOrWhiteSpace(chatMessage))
            {
                ChatManager.GetInstance()?.SendMutedChatMessage(chatMessage);
            }
        }

        private static string RewriteMessageWithNormalizedCommand(string original, string normalizedCommand, int commandStart, int commandLength)
        {
            if (string.IsNullOrWhiteSpace(original))
            {
                return normalizedCommand;
            }

            if (commandStart < 0 || commandLength <= 0 || commandStart >= original.Length)
            {
                return normalizedCommand;
            }

            int suffixStart = commandStart + commandLength;
            if (suffixStart > original.Length)
            {
                suffixStart = original.Length;
            }

            return normalizedCommand + original.Substring(suffixStart);
        }

    }
}
