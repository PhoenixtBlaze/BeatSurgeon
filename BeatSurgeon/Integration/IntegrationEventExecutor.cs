using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BeatSurgeon.Integration
{
    internal sealed class IntegrationEventExecutor
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("IntegrationEventExecutor");

        private readonly GameplayManager _gameplayManager;
        private readonly DeferredEventQueue _deferredEventQueue;
        private readonly AutomaticEffectDedupService _dedupService;
        private readonly IntegrationCommandExecutor _commandExecutor;

        [Inject]
        public IntegrationEventExecutor(
            GameplayManager gameplayManager,
            DeferredEventQueue deferredEventQueue,
            AutomaticEffectDedupService dedupService,
            IntegrationCommandExecutor commandExecutor)
        {
            _gameplayManager = gameplayManager;
            _deferredEventQueue = deferredEventQueue;
            _dedupService = dedupService;
            _commandExecutor = commandExecutor;
        }

        internal async Task<IntegrationCommandResult> ExecuteRaiseAsync(
            IntegrationInboundMessage inbound,
            CancellationToken ct)
        {
            string messageId = inbound?.Id ?? string.Empty;
            JObject payload = inbound?.Payload ?? new JObject();
            string eventName = (payload.Value<string>("name") ?? string.Empty).Trim().ToLowerInvariant();
            JObject data = payload["data"] as JObject ?? new JObject();
            string userName = ResolveViewerName(data);

            IntegrationApiLog.EventRaise(messageId, eventName, userName, "processing");

            switch (eventName)
            {
                case "follow.received":
                    return await HandleFollowReceivedAsync(messageId, data, ct).ConfigureAwait(false);
                case "subscription.received":
                    return await HandleSubscriptionReceivedAsync(messageId, data, ct).ConfigureAwait(false);
                case "cheer.received":
                    return await HandleCheerReceivedAsync(messageId, data, ct).ConfigureAwait(false);
                case "sabotage.requested":
                    return await HandleSabotageRequestedAsync(messageId, data, ct).ConfigureAwait(false);
                default:
                    LogEventRejected(messageId, string.Empty, IntegrationRejectReason.InvalidMessage, "Unsupported event name.");
                    return IntegrationCommandResult.FromRejected(
                        string.Empty,
                        IntegrationRejectReason.InvalidMessage,
                        "Unsupported event name.");
            }
        }

        private async Task<IntegrationCommandResult> HandleFollowReceivedAsync(string messageId, JObject data, CancellationToken ct)
        {
            IntegrationViewerPayload viewer = data["viewer"]?.ToObject<IntegrationViewerPayload>()
                ?? new IntegrationViewerPayload();
            string displayText = data.Value<string>("displayText");
            if (string.IsNullOrWhiteSpace(displayText))
            {
                string displayName = ResolveDisplayName(viewer);
                displayText = displayName + " is now Following!";
            }

            string dedupKey = AutomaticEffectDedupService.BuildFollowKey(viewer.Service, viewer.Id, viewer.Name);
            if (!_dedupService.TryClaim(dedupKey, AutomaticEffectOrigin.IntegrationApi))
            {
                IntegrationApiLog.DedupBlocked("event.raise", dedupKey, "IntegrationApi");
                LogEventRejected(
                    messageId,
                    "fmsg",
                    IntegrationRejectReason.DuplicateNativeEffect,
                    "Follow effect already handled by Beat Surgeon native pipeline.");
                return IntegrationCommandResult.FromRejected(
                    "fmsg",
                    IntegrationRejectReason.DuplicateNativeEffect,
                    "Follow effect already handled by Beat Surgeon native pipeline.");
            }

            string deferredReason = GetDeferredReason();
            if (deferredReason != null)
            {
                _deferredEventQueue.Enqueue(new DeferredEventEntry(
                    EventKind.Follow,
                    ResolveDisplayName(viewer),
                    0,
                    DateTime.UtcNow));
                IntegrationApiLog.EventRaise(messageId, "follow.received", ResolveDisplayName(viewer), "deferred=" + deferredReason);
                LogEventAccepted(messageId, "fmsg", "deferred");
                return IntegrationCommandResult.FromAccepted("fmsg");
            }

            try
            {
                await FollowEffectAccessController.EnsureAutomaticEffectAuthorizedAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("Integration follow effect rejected: " + ex.Message);
                LogEventRejected(messageId, "fmsg", IntegrationRejectReason.InsufficientEntitlement, ex.Message);
                return IntegrationCommandResult.FromRejected("fmsg", IntegrationRejectReason.InsufficientEntitlement, ex.Message);
            }

            var ctx = IntegrationApiProtocol.BuildChatContext(viewer, "!fmsg " + displayText, TriggerSource.ExternalIntegration);
            try
            {
                await _gameplayManager.ApplyFollowerMessageAsync(ctx, displayText, ct).ConfigureAwait(false);
                LogEventAccepted(messageId, "fmsg", "applied");
                return IntegrationCommandResult.FromAccepted("fmsg");
            }
            catch (Exception ex)
            {
                _log.Warn("Integration follow effect failed: " + ex.Message);
                LogEventRejected(messageId, "fmsg", IntegrationRejectReason.ExecutionFailed, ex.Message);
                return IntegrationCommandResult.FromRejected("fmsg", IntegrationRejectReason.ExecutionFailed, ex.Message);
            }
        }

        private async Task<IntegrationCommandResult> HandleSubscriptionReceivedAsync(string messageId, JObject data, CancellationToken ct)
        {
            IntegrationViewerPayload buyer = data["buyer"]?.ToObject<IntegrationViewerPayload>()
                ?? data["viewer"]?.ToObject<IntegrationViewerPayload>()
                ?? new IntegrationViewerPayload();

            string eventKind = NormalizeSubscriptionKind(data.Value<string>("context"));
            if (string.Equals(eventKind, "subend", StringComparison.OrdinalIgnoreCase))
            {
                _log.Info("Integration subscription event skipped (subend is not celebratory)");
                LogEventRejected(messageId, "smsg", IntegrationRejectReason.InvalidMessage, "Subscription end does not trigger celebratory smsg.");
                return IntegrationCommandResult.FromRejected(
                    "smsg",
                    IntegrationRejectReason.InvalidMessage,
                    "Subscription end does not trigger celebratory smsg.");
            }

            string tier = data.Value<string>("tier") ?? data.Value<string>("subscriptionTier") ?? "1000";
            int cumulativeMonths = data.Value<int?>("consecutiveMonths") ?? data.Value<int?>("cumulativeMonths") ?? 0;
            int giftCount = data.Value<int?>("giftCount") ?? 1;
            string tierLabel = SubscriberEventCoordinator.TierToLabel(tier);

            string dedupKey = AutomaticEffectDedupService.BuildSubscriptionKey(
                buyer.Service,
                buyer.Id,
                buyer.Name,
                eventKind);

            if (!_dedupService.TryClaim(dedupKey, AutomaticEffectOrigin.IntegrationApi))
            {
                IntegrationApiLog.DedupBlocked("event.raise", dedupKey, "IntegrationApi");
                LogEventRejected(
                    messageId,
                    "smsg",
                    IntegrationRejectReason.DuplicateNativeEffect,
                    "Subscription effect already handled by Beat Surgeon native pipeline.");
                return IntegrationCommandResult.FromRejected(
                    "smsg",
                    IntegrationRejectReason.DuplicateNativeEffect,
                    "Subscription effect already handled by Beat Surgeon native pipeline.");
            }

            string displayName = ResolveDisplayName(buyer);
            string deferredReason = GetDeferredReason();
            if (deferredReason != null)
            {
                _deferredEventQueue.Enqueue(new DeferredEventEntry(
                    EventKind.Subscription,
                    displayName,
                    DateTime.UtcNow,
                    tierLabel,
                    cumulativeMonths,
                    giftCount,
                    eventKind));
                IntegrationApiLog.EventRaise(messageId, "subscription.received", displayName, "deferred=" + deferredReason);
                LogEventAccepted(messageId, "smsg", "deferred");
                return IntegrationCommandResult.FromAccepted("smsg");
            }

            try
            {
                await SubscriberEffectAccessController.EnsureAutomaticEffectAuthorizedAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("Integration subscription effect rejected: " + ex.Message);
                LogEventRejected(messageId, "smsg", IntegrationRejectReason.InsufficientEntitlement, ex.Message);
                return IntegrationCommandResult.FromRejected("smsg", IntegrationRejectReason.InsufficientEntitlement, ex.Message);
            }

            string displayText = SubscriberEventCoordinator.BuildDisplayText(tierLabel, cumulativeMonths, giftCount, eventKind);
            var ctx = IntegrationApiProtocol.BuildChatContext(buyer, "!smsg " + displayText, TriggerSource.ExternalIntegration);

            try
            {
                await _gameplayManager.ApplySubscriberMessageAsync(
                    ctx,
                    displayText,
                    ct,
                    SubscriberEventCoordinator.GetTrailCubeCount(cumulativeMonths, eventKind))
                    .ConfigureAwait(false);
                LogEventAccepted(messageId, "smsg", "applied");
                return IntegrationCommandResult.FromAccepted("smsg");
            }
            catch (Exception ex)
            {
                _log.Warn("Integration subscription effect failed: " + ex.Message);
                LogEventRejected(messageId, "smsg", IntegrationRejectReason.ExecutionFailed, ex.Message);
                return IntegrationCommandResult.FromRejected("smsg", IntegrationRejectReason.ExecutionFailed, ex.Message);
            }
        }

        private async Task<IntegrationCommandResult> HandleCheerReceivedAsync(string messageId, JObject data, CancellationToken ct)
        {
            IntegrationViewerPayload viewer = data["viewer"]?.ToObject<IntegrationViewerPayload>()
                ?? new IntegrationViewerPayload();
            int amount = data.Value<int?>("amount") ?? 0;
            if (amount <= 0)
            {
                LogEventRejected(messageId, "glitter", IntegrationRejectReason.InvalidMessage, "Cheer amount must be greater than zero.");
                return IntegrationCommandResult.FromRejected(
                    "glitter",
                    IntegrationRejectReason.InvalidMessage,
                    "Cheer amount must be greater than zero.");
            }

            string dedupKey = AutomaticEffectDedupService.BuildCheerKey(viewer.Service, viewer.Id, viewer.Name, amount);
            if (!_dedupService.TryClaim(dedupKey, AutomaticEffectOrigin.IntegrationApi))
            {
                IntegrationApiLog.DedupBlocked("event.raise", dedupKey, "IntegrationApi");
                LogEventRejected(
                    messageId,
                    "glitter",
                    IntegrationRejectReason.DuplicateNativeEffect,
                    "Cheer effect already handled by Beat Surgeon native pipeline.");
                return IntegrationCommandResult.FromRejected(
                    "glitter",
                    IntegrationRejectReason.DuplicateNativeEffect,
                    "Cheer effect already handled by Beat Surgeon native pipeline.");
            }

            string deferredReason = GetDeferredReason();
            if (deferredReason != null)
            {
                _deferredEventQueue.Enqueue(new DeferredEventEntry(
                    EventKind.Bits,
                    ResolveDisplayName(viewer),
                    amount,
                    DateTime.UtcNow));
                IntegrationApiLog.EventRaise(messageId, "cheer.received", ResolveDisplayName(viewer), "amount=" + amount + " deferred=" + deferredReason);
                LogEventAccepted(messageId, "glitter", "deferred");
                return IntegrationCommandResult.FromAccepted("glitter");
            }

            try
            {
                await BitEffectAccessController.EnsureAutomaticEffectAuthorizedAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("Integration cheer effect rejected: " + ex.Message);
                LogEventRejected(messageId, "glitter", IntegrationRejectReason.InsufficientEntitlement, ex.Message);
                return IntegrationCommandResult.FromRejected("glitter", IntegrationRejectReason.InsufficientEntitlement, ex.Message);
            }

            var ctx = IntegrationApiProtocol.BuildChatContext(viewer, "!glitter " + amount, TriggerSource.BitEvent);
            ctx.Bits = amount;

            try
            {
                await _gameplayManager.ApplyGlitterAsync(ctx, amount, ct).ConfigureAwait(false);
                LogEventAccepted(messageId, "glitter", "applied amount=" + amount);
                return IntegrationCommandResult.FromAccepted("glitter");
            }
            catch (Exception ex)
            {
                _log.Warn("Integration cheer effect failed: " + ex.Message);
                LogEventRejected(messageId, "glitter", IntegrationRejectReason.ExecutionFailed, ex.Message);
                return IntegrationCommandResult.FromRejected("glitter", IntegrationRejectReason.ExecutionFailed, ex.Message);
            }
        }

        private async Task<IntegrationCommandResult> HandleSabotageRequestedAsync(string messageId, JObject data, CancellationToken ct)
        {
            IntegrationViewerPayload viewer = data["viewer"]?.ToObject<IntegrationViewerPayload>()
                ?? new IntegrationViewerPayload();
            string message = data.Value<string>("message");
            string command = string.IsNullOrWhiteSpace(message) ? "!bomb" : "!bmsg " + message.Trim();

            IntegrationApiLog.EventRaise(messageId, "sabotage.requested", ResolveDisplayName(viewer), "command=" + command);

            var inbound = new IntegrationInboundMessage
            {
                Id = messageId,
                MessageType = IntegrationMessageType.CommandInvoke,
                Payload = new JObject
                {
                    ["command"] = command,
                    ["viewer"] = JObject.FromObject(viewer)
                }
            };

            return await _commandExecutor.ExecuteInvokeAsync(inbound, ct).ConfigureAwait(false);
        }

        private static void LogEventAccepted(string messageId, string eventKey, string detail)
        {
            IntegrationApiLog.EventResult(messageId, true, eventKey, "None", detail);
        }

        private static void LogEventRejected(string messageId, string eventKey, IntegrationRejectReason reason, string message)
        {
            IntegrationApiLog.EventResult(messageId, false, eventKey, reason.ToString(), message);
        }

        private static string ResolveViewerName(JObject data)
        {
            if (data == null)
            {
                return "Unknown";
            }

            IntegrationViewerPayload viewer = data["viewer"]?.ToObject<IntegrationViewerPayload>()
                ?? data["buyer"]?.ToObject<IntegrationViewerPayload>();
            return ResolveDisplayName(viewer);
        }

        private string GetDeferredReason()
        {
            if (_deferredEventQueue == null)
            {
                return null;
            }

            if (!_gameplayManager.IsInMap)
            {
                return "not in gameplay";
            }

            return RankedMapDetectionService.Instance.IsCurrentMapRankedOrChecking
                ? "ranked gameplay is active or still checking"
                : null;
        }

        private static string ResolveDisplayName(IntegrationViewerPayload viewer)
        {
            if (!string.IsNullOrWhiteSpace(viewer?.Name))
            {
                return viewer.Name.Trim();
            }

            return "Someone";
        }

        private static string NormalizeSubscriptionKind(string raw)
        {
            switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "resubscription":
                case "resub":
                    return "resub";
                case "giftsubscription":
                case "giftsub":
                    return "giftsub";
                case "subend":
                case "subscriptionend":
                    return "subend";
                case "newsubscription":
                case "sub":
                default:
                    return "sub";
            }
        }
    }
}
