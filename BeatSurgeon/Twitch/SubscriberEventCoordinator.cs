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

            string displayName = string.IsNullOrWhiteSpace(notification.UserName)
                ? (string.IsNullOrWhiteSpace(notification.UserLogin) ? "Someone" : notification.UserLogin)
                : notification.UserName;

            string tierLabel = TierToLabel(notification.Tier);

            if (!_gameplayManager.IsInMap)
            {
                _deferredEventQueue.Enqueue(new DeferredEventEntry(
                    EventKind.Subscription,
                    displayName,
                    DateTime.UtcNow,
                    tierLabel,
                    notification.CumulativeMonths,
                    notification.EventSubKind));
                _log.Debug("Subscription event deferred for " + displayName + " — not in gameplay.");
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

            string displayText = BuildDisplayText(displayName, tierLabel, notification.CumulativeMonths, notification.EventSubKind);

            var ctx = new ChatContext
            {
                SenderName = displayName,
                MessageText = "!smsg " + displayText,
                Source = ChatSource.NativeTwitch,
                TriggerSource = TriggerSource.Chat
            };

            try
            {
                await _gameplayManager.ApplySubscriberMessageAsync(ctx, displayText, CancellationToken.None).ConfigureAwait(false);
                _log.Info("Applied subscription-triggered subscriber message for user=" + displayName);
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to apply subscription-triggered subscriber message: " + ex.Message);
            }
        }

        internal static string BuildDisplayText(string displayName, string tierLabel, int cumulativeMonths, string eventSubKind)
        {
            switch (eventSubKind)
            {
                case "resub":
                    return displayName + " resubscribed for " + cumulativeMonths + " months at " + tierLabel + "!";
                case "giftsub":
                    return displayName + " gifted a sub at " + tierLabel + "!";
                default:
                    return displayName + " just subscribed at " + tierLabel + "!";
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
    }
}
