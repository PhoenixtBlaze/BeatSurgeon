using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class FollowEventCoordinator : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("FollowEventCoordinator");

        private readonly TwitchEventSubClient _eventSubClient;
        private readonly GameplayManager _gameplayManager;
        private readonly DeferredEventQueue _deferredEventQueue;

        [Inject]
        public FollowEventCoordinator(TwitchEventSubClient eventSubClient, GameplayManager gameplayManager, DeferredEventQueue deferredEventQueue)
        {
            _eventSubClient = eventSubClient;
            _gameplayManager = gameplayManager;
            _deferredEventQueue = deferredEventQueue;
        }

        public void Initialize()
        {
            _eventSubClient.OnFollowReceived += HandleFollowReceived;
        }

        public void Dispose()
        {
            _eventSubClient.OnFollowReceived -= HandleFollowReceived;
        }

        private void HandleFollowReceived(TwitchEventSubClient.FollowNotification notification)
        {
            _ = HandleFollowReceivedAsync(notification);
        }

        private async Task HandleFollowReceivedAsync(TwitchEventSubClient.FollowNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(notification.UserName)
                ? (string.IsNullOrWhiteSpace(notification.UserLogin) ? "Someone" : notification.UserLogin)
                : notification.UserName;

            if (!_gameplayManager.IsInMap)
            {
                _deferredEventQueue.Enqueue(new DeferredEventEntry(
                    EventKind.Follow,
                    displayName,
                    0,
                    DateTime.UtcNow));
                _log.Debug("[BeatSurgeon] Follow event deferred for " + displayName + " — not in gameplay.");
                return;
            }

            try
            {
                await FollowEffectAccessController.EnsureAuthorizedAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("Follow effect rejected: " + ex.Message);
                return;
            }

            string displayText = displayName + " is now Following!";

            var ctx = new ChatContext
            {
                SenderName = displayName,
                MessageText = "!fmsg " + displayText,
                Source = ChatSource.NativeTwitch,
                TriggerSource = TriggerSource.Chat
            };

            try
            {
                await _gameplayManager.ApplyFollowerMessageAsync(ctx, displayText, CancellationToken.None).ConfigureAwait(false);
                _log.Info("Applied follow-triggered follower message for user=" + displayName);
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to apply follow-triggered follower message: " + ex.Message);
            }
        }
    }
}