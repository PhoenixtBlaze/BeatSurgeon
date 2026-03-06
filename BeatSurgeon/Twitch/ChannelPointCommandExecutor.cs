using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class ChannelPointCommandExecutor : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("ChannelPointCommandExecutor");
        private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(30);
        private static ChannelPointCommandExecutor _instance;

        private readonly ChannelPointRouter _router;
        private readonly GameplayManager _gameplayManager;
        private readonly TwitchEventSubClient _eventSubClient;
        private readonly TwitchChannelPointsManager _channelPointsManager;
        private readonly CommandHandler _commandHandler;

        private readonly ConcurrentDictionary<string, DateTime> _processedRedemptions =
            new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        private Timer _cleanupTimer;
        private CancellationTokenSource _cts;

        internal static ChannelPointCommandExecutor Instance =>
            _instance ?? (_instance = new ChannelPointCommandExecutor(
                new ChannelPointRouter(),
                GameplayManager.GetInstance(),
                TwitchEventSubClient.Instance,
                TwitchChannelPointsManager.Instance,
                CommandHandler.Instance));

        [Inject]
        public ChannelPointCommandExecutor(
            ChannelPointRouter router,
            GameplayManager gameplayManager,
            TwitchEventSubClient eventSubClient,
            TwitchChannelPointsManager channelPointsManager,
            CommandHandler commandHandler)
        {
            _instance = this;
            _router = router;
            _gameplayManager = gameplayManager;
            _eventSubClient = eventSubClient;
            _channelPointsManager = channelPointsManager;
            _commandHandler = commandHandler;
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize - starting deduplication cleanup timer");
            _cts = new CancellationTokenSource();
            _cleanupTimer = new Timer(
                _ => CleanupStaleDedupEntries(),
                state: null,
                dueTime: TimeSpan.FromSeconds(60),
                period: TimeSpan.FromSeconds(60));

            _eventSubClient.OnChannelPointRedeemed += HandleRedemption;
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose");
            _eventSubClient.OnChannelPointRedeemed -= HandleRedemption;
            _cleanupTimer?.Dispose();
            _processedRedemptions.Clear();
            _cts?.Cancel();
            _cts?.Dispose();
        }

        internal void Shutdown() => Dispose();

        internal void Initialize(TwitchEventSubClient newClient)
        {
            _eventSubClient.OnChannelPointRedeemed -= HandleRedemption;
            if (newClient != null)
            {
                newClient.OnChannelPointRedeemed += HandleRedemption;
            }
        }

        internal async Task ResubscribeRewardsAsync(params string[] rewardIds)
        {
            if (rewardIds == null || rewardIds.Length == 0) return;
            await _eventSubClient.EnsureChannelPointSubscriptionsAsync(rewardIds).ConfigureAwait(false);
        }

        private async void HandleRedemption(TwitchEventSubClient.ChannelPointRedemption redemption)
        {
            if (redemption == null) return;

            try
            {
                await ExecuteRedemptionAsync(
                    redemption.RewardId,
                    redemption.RedemptionId,
                    redemption.UserId,
                    redemption.UserName,
                    redemption.UserInput,
                    _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "HandleRedemption");
            }
        }

        private void CleanupStaleDedupEntries()
        {
            DateTime cutoff = DateTime.UtcNow - DeduplicationWindow - TimeSpan.FromSeconds(5);
            int removed = 0;

            foreach (string key in _processedRedemptions.Keys)
            {
                if (_processedRedemptions.TryGetValue(key, out DateTime ts) && ts < cutoff)
                {
                    _processedRedemptions.TryRemove(key, out _);
                    removed++;
                }
            }

            if (removed > 0)
            {
                _log.Debug("Deduplication cleanup: removed " + removed + " stale entries");
            }
        }

        internal async Task ExecuteRedemptionAsync(
            string rewardId,
            string redemptionId,
            string userId,
            string userLogin,
            string userInput,
            CancellationToken ct = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(redemptionId))
            {
                _log.Warn("ExecuteRedemptionAsync called with empty redemption id");
                return;
            }

            if (!_processedRedemptions.TryAdd(redemptionId, DateTime.UtcNow))
            {
                _log.ChannelPoint(rewardId, "DuplicateRejected", "redemptionId=" + redemptionId + " user=" + userLogin);
                return;
            }

            _log.ChannelPoint(rewardId, "RedemptionReceived", "redemptionId=" + redemptionId + " user=" + userLogin + " input=" + userInput);

            if (!_gameplayManager.IsInMap)
            {
                _log.ChannelPoint(rewardId, "RejectedNotInMap", "redemptionId=" + redemptionId + " user=" + userLogin);
                await TryRefundAsync(rewardId, redemptionId, userLogin, null, "NotInMap", ct).ConfigureAwait(false);
                return;
            }

            if (RankedMapDetectionService.Instance.IsCurrentMapRankedOrChecking)
            {
                _log.ChannelPoint(rewardId, "RejectedRankedMap", "user=" + userLogin);
                await TryRefundAsync(rewardId, redemptionId, userLogin, null, "RankedMap", ct).ConfigureAwait(false);
                return;
            }

            if (!_router.TryGetCommand(rewardId, out string command))
            {
                _log.Warn("No command mapped for rewardId=" + rewardId + " - reward exists in EventSub but not in router");
                await TryRefundAsync(rewardId, redemptionId, userLogin, null, "RouteMissing", ct).ConfigureAwait(false);
                return;
            }

            var ctx = new ChatContext
            {
                SenderName = string.IsNullOrWhiteSpace(userLogin) ? "Unknown" : userLogin,
                MessageText = command,
                Source = ChatSource.NativeTwitch,
                TriggerSource = TriggerSource.ChannelPoints,
                IsChannelPoint = true,
                IsSubscriber = true
            };

            _log.ChannelPoint(rewardId, "Dispatching", "command=" + command + " user=" + userLogin);
            try
            {
                CommandExecutionResult result = await _commandHandler
                    .HandleMessageAsync(ctx, TriggerSource.ChannelPoints, ct)
                    .ConfigureAwait(false);

                if (result != null && result.Executed)
                {
                    _log.ChannelPoint(rewardId, "DispatchedOK", "command=" + command + " user=" + userLogin);
                    await TryFulfillAsync(rewardId, redemptionId, ct).ConfigureAwait(false);
                    return;
                }

                string reason = result == null
                    ? "UnknownFailure"
                    : result.Reason.ToString();
                _log.ChannelPoint(rewardId, "Rejected", "command=" + command + " user=" + userLogin + " reason=" + reason);
                await TryRefundAsync(rewardId, redemptionId, userLogin, command, reason, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "ExecuteRedemptionAsync rewardId=" + rewardId + " user=" + userLogin);
                await TryRefundAsync(rewardId, redemptionId, userLogin, command, "ExecutionFailed", ct).ConfigureAwait(false);
            }
        }

        private async Task TryFulfillAsync(string rewardId, string redemptionId, CancellationToken ct)
        {
            try
            {
                await _channelPointsManager.FulfillRedemptionAsync(rewardId, redemptionId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "TryFulfillAsync");
            }
        }

        private async Task TryRefundAsync(
            string rewardId,
            string redemptionId,
            string userLogin,
            string command,
            string reason,
            CancellationToken ct)
        {
            try
            {
                await _channelPointsManager.RefundRedemptionAsync(rewardId, redemptionId, ct).ConfigureAwait(false);
                string user = string.IsNullOrWhiteSpace(userLogin) ? "viewer" : userLogin;
                string cmd = string.IsNullOrWhiteSpace(command) ? "channel point redemption" : command;
                string suffix = string.IsNullOrWhiteSpace(reason) ? string.Empty : " (" + reason + ")";
                ChatManager.GetInstance()?.SendChatMessage("!!Refunded " + user + " for " + cmd + suffix + ".");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "TryRefundAsync");
            }
        }
    }
}
