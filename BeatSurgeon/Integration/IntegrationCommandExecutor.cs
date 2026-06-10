using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BeatSurgeon.Integration
{
    internal sealed class IntegrationCommandExecutor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("IntegrationCommandExecutor");
        private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(IntegrationApiConstants.CommandDedupWindowSeconds);

        private readonly GameplayManager _gameplayManager;
        private readonly CommandHandler _commandHandler;
        private readonly ConcurrentDictionary<string, DateTime> _processedInvocations =
            new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        [Inject]
        public IntegrationCommandExecutor(GameplayManager gameplayManager, CommandHandler commandHandler)
        {
            _gameplayManager = gameplayManager;
            _commandHandler = commandHandler;
        }

        internal async Task<IntegrationCommandResult> ExecuteInvokeAsync(
            IntegrationInboundMessage inbound,
            CancellationToken ct)
        {
            CleanupStaleDedupEntries();

            JObject payload = inbound?.Payload ?? new JObject();
            string command = payload.Value<string>("command") ?? string.Empty;
            string messageId = inbound?.Id ?? string.Empty;

            if (string.IsNullOrWhiteSpace(command))
            {
                IntegrationApiLog.CommandResult(messageId, false, string.Empty, IntegrationRejectReason.InvalidMessage.ToString(), "Command was missing.");
                return IntegrationCommandResult.FromRejected(
                    string.Empty,
                    IntegrationRejectReason.InvalidMessage,
                    "Command was missing.");
            }

            IntegrationViewerPayload viewer = payload["viewer"]?.ToObject<IntegrationViewerPayload>()
                ?? new IntegrationViewerPayload();
            string userName = string.IsNullOrWhiteSpace(viewer.Name) ? "Unknown" : viewer.Name.Trim();
            IntegrationApiLog.CommandInvoke(messageId, userName, command);

            if (!string.IsNullOrWhiteSpace(messageId) && !_processedInvocations.TryAdd(messageId, DateTime.UtcNow))
            {
                IntegrationApiLog.DedupBlocked("command.invoke", messageId, "IntegrationApi");
                IntegrationApiLog.CommandResult(
                    messageId,
                    false,
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.Duplicate.ToString(),
                    "Duplicate command invocation id.");
                return IntegrationCommandResult.FromRejected(
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.Duplicate,
                    "Duplicate command invocation id.");
            }

            var ctx = IntegrationApiProtocol.BuildChatContext(viewer, command, TriggerSource.ExternalIntegration);

            PluginConfig config = PluginConfig.Instance;
            if (config != null && config.IntegrationApiRespectChatPermissions
                && !CommandRuntimeSettings.IsChatCommandAllowed(ctx))
            {
                IntegrationApiLog.CommandResult(
                    messageId,
                    false,
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.InsufficientPermission.ToString(),
                    "Viewer does not have permission for this command.");
                return IntegrationCommandResult.FromRejected(
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.InsufficientPermission,
                    "Viewer does not have permission for this command.");
            }

            if (!_gameplayManager.IsInMap)
            {
                IntegrationApiLog.CommandResult(
                    messageId,
                    false,
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.NotInMap.ToString(),
                    "Beat Surgeon is not currently in gameplay.");
                return IntegrationCommandResult.FromRejected(
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.NotInMap,
                    "Beat Surgeon is not currently in gameplay.");
            }

            if (RankedMapDetectionService.Instance.IsCurrentMapRankedOrChecking)
            {
                IntegrationApiLog.CommandResult(
                    messageId,
                    false,
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.RankedMap.ToString(),
                    "Ranked gameplay is active or still being checked.");
                return IntegrationCommandResult.FromRejected(
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.RankedMap,
                    "Ranked gameplay is active or still being checked.");
            }

            try
            {
                CommandExecutionResult result = await _commandHandler
                    .HandleMessageAsync(ctx, TriggerSource.ExternalIntegration, ct)
                    .ConfigureAwait(false);

                if (result != null && result.Executed)
                {
                    IntegrationApiLog.CommandResult(messageId, true, result.CommandKey, "None", "Command accepted.");
                    _log.Info("Integration command accepted user=" + ctx.Username + " command=" + command);
                    return IntegrationCommandResult.FromAccepted(result.CommandKey);
                }

                IntegrationRejectReason reason = result == null
                    ? IntegrationRejectReason.ExecutionFailed
                    : IntegrationApiProtocol.MapCommandRejectReason(result.Reason, null);

                string detail = result == null
                    ? "Command execution returned no result."
                    : BuildRejectDetail(result);

                IntegrationApiLog.CommandResult(messageId, false, result?.CommandKey ?? string.Empty, reason.ToString(), detail);
                _log.Warn("Integration command rejected user=" + ctx.Username + " command=" + command + " reason=" + reason);
                return IntegrationCommandResult.FromRejected(result?.CommandKey ?? string.Empty, reason, detail, result?.CooldownRemaining);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "ExecuteInvokeAsync command=" + command + " user=" + ctx.Username);
                return IntegrationCommandResult.FromRejected(
                    CommandRuntimeSettings.CanonicalizeCommandKey(command),
                    IntegrationRejectReason.ExecutionFailed,
                    ex.Message);
            }
        }

        private static string BuildRejectDetail(CommandExecutionResult result)
        {
            if (result.Reason == CommandRejectReason.OnCooldown && result.CooldownRemaining.HasValue)
            {
                return "Command is on cooldown for " + Math.Ceiling(result.CooldownRemaining.Value.TotalSeconds) + " seconds.";
            }

            return result.Reason.ToString();
        }

        private void CleanupStaleDedupEntries()
        {
            DateTime cutoff = DateTime.UtcNow - DedupWindow - TimeSpan.FromSeconds(5);
            foreach (string key in _processedInvocations.Keys)
            {
                if (_processedInvocations.TryGetValue(key, out DateTime timestamp) && timestamp < cutoff)
                {
                    _processedInvocations.TryRemove(key, out _);
                }
            }
        }
    }

    internal sealed class IntegrationCommandResult
    {
        internal bool Accepted { get; private set; }
        internal string CommandKey { get; private set; } = string.Empty;
        internal IntegrationRejectReason Reason { get; private set; } = IntegrationRejectReason.None;
        internal string Message { get; private set; } = string.Empty;
        internal TimeSpan? CooldownRemaining { get; private set; }

        internal static IntegrationCommandResult FromAccepted(string commandKey)
        {
            return new IntegrationCommandResult
            {
                Accepted = true,
                CommandKey = commandKey ?? string.Empty,
                Reason = IntegrationRejectReason.None,
                Message = "Command accepted."
            };
        }

        internal static IntegrationCommandResult FromRejected(
            string commandKey,
            IntegrationRejectReason reason,
            string message,
            TimeSpan? cooldownRemaining = null)
        {
            return new IntegrationCommandResult
            {
                Accepted = false,
                CommandKey = commandKey ?? string.Empty,
                Reason = reason,
                Message = message ?? string.Empty,
                CooldownRemaining = cooldownRemaining
            };
        }
    }
}
