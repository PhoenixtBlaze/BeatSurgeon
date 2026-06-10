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
    internal sealed class FollowEventCoordinator : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("FollowEventCoordinator");

        private readonly TwitchEventSubClient _eventSubClient;
        private readonly GameplayManager _gameplayManager;
        private readonly DeferredEventQueue _deferredEventQueue;
        private readonly AutomaticEffectDedupService _dedupService;

        [Inject]
        public FollowEventCoordinator(
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

            string dedupKey = AutomaticEffectDedupService.BuildFollowKey("twitch", notification.UserId, displayName);
            if (!_dedupService.TryClaim(dedupKey, AutomaticEffectOrigin.EventSub))
            {
                _log.Debug("Follow event skipped — duplicate automatic effect for " + displayName + ".");
                return;
            }

            string deferredReason = GetDeferredReason();
            if (deferredReason != null)
            {
                _deferredEventQueue.Enqueue(new DeferredEventEntry(
                    EventKind.Follow,
                    displayName,
                    0,
                    DateTime.UtcNow));
                _log.Debug("[BeatSurgeon] Follow event deferred for " + displayName + " — " + deferredReason + ".");
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