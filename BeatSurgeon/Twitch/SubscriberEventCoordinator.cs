using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Integration;
using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class SubscriberEventCoordinator : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SubscriberEventCoordinator");

        private readonly TwitchEventSubClient _eventSubClient;
        private readonly GameplayManager _gameplayManager;
        private readonly DeferredEventQueue _deferredEventQueue;
        private readonly AutomaticEffectDedupService _dedupService;

        [Inject]
        public SubscriberEventCoordinator(
            TwitchEventSubClient eventSubClient,
            GameplayManager gameplayManager,
            DeferredEventQueue deferredEventQueue,
            AutomaticEffectDedupService dedupService)
        {
            _eventSubClient = eventSubClient;
            _gameplayManager = gameplayManager;
            _deferredEventQueue = deferredEventQueue;
            _dedupService = dedupService;
        }

        public void Initialize()
        {
            _eventSubClient.OnSubscriptionReceived += HandleSubscriptionReceived;
        }

        public void Dispose()
        {
            _eventSubClient.OnSubscriptionReceived -= HandleSubscriptionReceived;
        }

        private void HandleSubscriptionReceived(TwitchEventSubClient.SubscriberNotification notification)
        {
            _ = HandleSubscriptionReceivedAsync(notification);
        }

        private async Task HandleSubscriptionReceivedAsync(TwitchEventSubClient.SubscriberNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            string displayName = GetDisplayName(notification);

            string tierLabel = TierToLabel(notification.Tier);
            string eventKind = string.IsNullOrWhiteSpace(notification.EventSubKind) ? "sub" : notification.EventSubKind;

            if (string.Equals(eventKind, "subend", StringComparison.OrdinalIgnoreCase))
            {
                _log.Info("Subscription event skipped (subend is not celebratory) user=" + displayName);
                return;
            }

            string dedupKey = AutomaticEffectDedupService.BuildSubscriptionKey(
                "twitch",
                notification.UserId,
                displayName,
                eventKind);

            if (!_dedupService.TryClaim(dedupKey, AutomaticEffectOrigin.EventSub))
            {
                _log.Info("Subscription event skipped — duplicate automatic effect for " + displayName + " kind=" + eventKind + ".");
                return;
            }

            string deferredReason = GetDeferredReason();
            if (deferredReason != null)
            {
                _deferredEventQueue.Enqueue(new DeferredEventEntry(
                    EventKind.Subscription,
                    displayName,
                    DateTime.UtcNow,
                    tierLabel,
                    notification.CumulativeMonths,
                    notification.GiftCount,
                    notification.EventSubKind,
                    notification.DurationMonths));
                _log.Info("[BeatSurgeon] Subscription event deferred for " + displayName + " — " + deferredReason + ".");
                return;
            }

            try
            {
                await SubscriberEffectAccessController.EnsureAutomaticEffectAuthorizedAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("Subscriber effect rejected: " + ex.Message);
                return;
            }

            string displayText = BuildDisplayText(tierLabel, notification.CumulativeMonths, notification.GiftCount, notification.EventSubKind);

            var ctx = new ChatContext
            {
                SenderName = displayName,
                MessageText = "!smsg " + displayText,
                Source = ChatSource.NativeTwitch,
                TriggerSource = TriggerSource.AutomaticEvent
            };

            try
            {
                await _gameplayManager.ApplySubscriberMessageAsync(
                    ctx,
                    displayText,
                    CancellationToken.None,
                    GetTrailCubeCount(
                        notification.Tier,
                        notification.EventSubKind,
                        notification.GiftCount,
                        notification.DurationMonths))
                    .ConfigureAwait(false);
                _log.Info("Applied subscription-triggered subscriber message for user=" + displayName + " kind=" + eventKind);
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to apply subscription-triggered subscriber message: " + ex.Message);
            }
        }

        internal static string GetDisplayName(TwitchEventSubClient.SubscriberNotification notification)
        {
            if (notification == null)
            {
                return "Someone";
            }

            if (!string.IsNullOrWhiteSpace(notification.UserName))
            {
                return notification.UserName;
            }

            if (!string.IsNullOrWhiteSpace(notification.UserLogin))
            {
                return notification.UserLogin;
            }

            return notification.IsAnonymous ? "Anonymous" : "Someone";
        }

        internal static string BuildDisplayText(string tierLabel, int cumulativeMonths, int giftCount, string eventSubKind)
        {
            string normalizedTierLabel = string.IsNullOrWhiteSpace(tierLabel) ? "Tier 1" : tierLabel;

            switch (eventSubKind)
            {
                case "resub":
                    return string.Equals(normalizedTierLabel, "Prime", StringComparison.OrdinalIgnoreCase)
                        ? "Resubscribed With Prime" + FormatMonthsClause(cumulativeMonths) + "!"
                        : "Resubscribed at " + normalizedTierLabel + FormatMonthsClause(cumulativeMonths) + "!";
                case "giftsub":
                {
                    int normalizedGiftCount = giftCount > 0 ? giftCount : 1;
                    return normalizedGiftCount == 1
                        ? "Gifted a " + normalizedTierLabel + " Sub!"
                        : "Gifted " + normalizedGiftCount + " " + normalizedTierLabel + " Subs!";
                }
                case "subend":
                    return string.Equals(normalizedTierLabel, "Prime", StringComparison.OrdinalIgnoreCase)
                        ? "Prime Subscription Ended."
                        : normalizedTierLabel + " Subscription Ended.";
                default:
                    return string.Equals(normalizedTierLabel, "Prime", StringComparison.OrdinalIgnoreCase)
                        ? "Subscribed With Prime!"
                        : "Subscribed at " + normalizedTierLabel + "!";
            }
        }

        private static string FormatMonthsClause(int cumulativeMonths)
        {
            if (cumulativeMonths <= 0)
            {
                return string.Empty;
            }

            return " for " + cumulativeMonths + " " + (cumulativeMonths == 1 ? "Month" : "Months");
        }

        internal static int GetTrailCubeCount(string tier, string eventSubKind, int giftCount, int durationMonths)
        {
            string kind = (eventSubKind ?? string.Empty).Trim().ToLowerInvariant();
            switch (kind)
            {
                case "giftsub":
                    return RegularSubNotes * Math.Max(1, giftCount);
                case "resub":
                    return IsPrimeTier(tier) ? PrimeSubNotes : RegularSubNotes;
                case "sub":
                {
                    int baseNotes = IsPrimeTier(tier) ? PrimeSubNotes : RegularSubNotes;
                    int normalizedDurationMonths = Math.Max(0, durationMonths);
                    if (normalizedDurationMonths <= 1)
                    {
                        return baseNotes;
                    }

                    return baseNotes + (ExtraNotesPerMonthBeyondFirst * (normalizedDurationMonths - 1));
                }
                default:
                    return 0;
            }
        }

        private const int RegularSubNotes = 5;
        private const int PrimeSubNotes = 10;
        private const int ExtraNotesPerMonthBeyondFirst = 5;

        internal static bool IsPrimeTier(string tierOrLabel)
        {
            if (string.IsNullOrWhiteSpace(tierOrLabel))
            {
                return false;
            }

            string normalized = tierOrLabel.Trim().ToLowerInvariant();
            return normalized == "prime" || normalized.Contains("prime");
        }

        internal static string TierToLabel(string tier)
        {
            switch (tier)
            {
                case "2000": return "Tier 2";
                case "3000": return "Tier 3";
                case "prime": return "Prime";
                default: return "Tier 1";
            }
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
    }
}
