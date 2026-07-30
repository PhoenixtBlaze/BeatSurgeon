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
    internal sealed class RaidEventCoordinator : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("RaidEventCoordinator");

        private readonly TwitchEventSubClient _eventSubClient;
        private readonly GameplayManager _gameplayManager;
        private readonly DeferredEventQueue _deferredEventQueue;
        private readonly AutomaticEffectDedupService _dedupService;

        [Inject]
        public RaidEventCoordinator(
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
            _eventSubClient.OnRaidReceived += HandleRaidReceived;
        }

        public void Dispose()
        {
            _eventSubClient.OnRaidReceived -= HandleRaidReceived;
        }

        private void HandleRaidReceived(TwitchEventSubClient.RaidNotification notification)
        {
            _ = HandleRaidReceivedAsync(notification);
        }

        private async Task HandleRaidReceivedAsync(TwitchEventSubClient.RaidNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(notification.FromBroadcasterUserName)
                ? (string.IsNullOrWhiteSpace(notification.FromBroadcasterUserLogin) ? "Someone" : notification.FromBroadcasterUserLogin)
                : notification.FromBroadcasterUserName;

            int noteCount = RaidFountainNoteManager.ClampNoteCount(notification.Viewers);
            string dedupKey = AutomaticEffectDedupService.BuildRaidKey(
                "twitch",
                notification.FromBroadcasterUserId,
                displayName,
                noteCount);
            if (!_dedupService.TryClaim(dedupKey, AutomaticEffectOrigin.EventSub))
            {
                _log.Debug("Raid event skipped — duplicate automatic effect for " + displayName + ".");
                return;
            }

            string deferredReason = GetDeferredReason();
            if (deferredReason != null)
            {
                _deferredEventQueue.Enqueue(new DeferredEventEntry(
                    EventKind.Raid,
                    displayName,
                    noteCount,
                    DateTime.UtcNow));
                _log.Debug("[BeatSurgeon] Raid event deferred for " + displayName + " notes=" + noteCount + " — " + deferredReason + ".");
                return;
            }

            try
            {
                await RaidEffectAccessController.EnsureAutomaticEffectAuthorizedAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("Raid effect rejected: " + ex.Message);
                return;
            }

            var ctx = new ChatContext
            {
                SenderName = displayName,
                MessageText = "!raid " + noteCount,
                Source = ChatSource.NativeTwitch,
                TriggerSource = TriggerSource.Chat
            };

            try
            {
                await _gameplayManager.ApplyRaidEffectAsync(ctx, displayName, noteCount, CancellationToken.None).ConfigureAwait(false);
                _log.Info(
                    "Applied raid-triggered fountain for raider="
                    + displayName
                    + " viewers="
                    + notification.Viewers
                    + " notes="
                    + noteCount);
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to apply raid-triggered fountain: " + ex.Message);
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
