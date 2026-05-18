using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
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

        [Inject]
        public SubscriberEventCoordinator(TwitchEventSubClient eventSubClient, GameplayManager gameplayManager, DeferredEventQueue deferredEventQueue)
        {
            _eventSubClient = eventSubClient;
            _gameplayManager = gameplayManager;
            _deferredEventQueue = deferredEventQueue;
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
                    notification.EventSubKind));
                _log.Debug("Subscription event deferred for " + displayName + " — " + deferredReason + ".");
                return;
            }

            try
            {
                await SubscriberEffectAccessController.EnsureAuthorizedAsync(CancellationToken.None).ConfigureAwait(false);
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
                TriggerSource = TriggerSource.Chat
            };

            try
            {
                await _gameplayManager.ApplySubscriberMessageAsync(
                    ctx,
                    displayText,
                    CancellationToken.None,
                    GetTrailCubeCount(notification.CumulativeMonths, notification.EventSubKind))
                    .ConfigureAwait(false);
                _log.Info("Applied subscription-triggered subscriber message for user=" + displayName);
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

        internal static int GetTrailCubeCount(int cumulativeMonths, string eventSubKind)
        {
            switch (eventSubKind)
            {
                case "resub":
                    return 5 * Math.Max(1, cumulativeMonths);
                case "sub":
                    return 5;
                default:
                    return 0;
            }
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
