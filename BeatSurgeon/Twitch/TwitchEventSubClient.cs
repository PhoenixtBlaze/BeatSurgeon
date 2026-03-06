using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;
using Newtonsoft.Json.Linq;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class TwitchEventSubClient : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("TwitchEventSubClient");
        private static readonly TimeSpan[] BackoffDelays =
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        };

        internal sealed class ChannelPointRedemption
        {
            internal string RedemptionId;
            internal string RewardId;
            internal string RewardTitle;
            internal string UserName;
            internal string UserId;
            internal string UserInput;
        }

        internal static string CurrentSessionId { get; private set; }

        private static TwitchEventSubClient _instance;
        internal static TwitchEventSubClient Instance =>
            _instance ?? (_instance = new TwitchEventSubClient(
                TwitchAuthManager.Instance,
                TwitchApiClient.Instance));

        private readonly TwitchAuthManager _authManager;
        private readonly TwitchApiClient _apiClient;

        private readonly Dictionary<string, string> _rewardSubscriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _pendingRewardIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _subscriptionLock = new SemaphoreSlim(1, 1);
        private readonly object _stateLock = new object();

        private CancellationTokenSource _cts;
        private Task _receiveLoop;
        private volatile bool _isConnected;

        internal event Action<ChannelPointRedemption> OnChannelPointRedeemed;

        internal bool IsConnected => _isConnected;

        [Inject]
        public TwitchEventSubClient(
            TwitchAuthManager authManager,
            TwitchApiClient apiClient)
        {
            _instance = this;
            _authManager = authManager;
            _apiClient = apiClient;
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize");
            _authManager.OnAuthReady += HandleAuthReady;

            if (PluginConfig.Instance != null && PluginConfig.Instance.HasValidToken)
            {
                StartReceiveLoop();
            }
            else
            {
                _log.TwitchState("DeferredConnect", "WaitingForAuthReady");
            }
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose - cancelling EventSub receive loop");
            _authManager.OnAuthReady -= HandleAuthReady;

            try
            {
                _cts?.Cancel();
                _receiveLoop?.Wait(TimeSpan.FromSeconds(5));
                _log.Lifecycle("Dispose - receive loop stopped");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Dispose");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _receiveLoop = null;
                _isConnected = false;
            }
        }

        internal Task ConnectAsync()
        {
            StartReceiveLoop();

            return Task.CompletedTask;
        }

        internal void Shutdown() => Dispose();

        private void HandleAuthReady()
        {
            _log.TwitchState("AuthReady", "Starting EventSub receive loop");
            StartReceiveLoop();
        }

        private void StartReceiveLoop()
        {
            if (_cts != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _receiveLoop = Task.Run(() => RunWithReconnectAsync(_cts.Token), _cts.Token);
        }

        private async Task RunWithReconnectAsync(CancellationToken ct)
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _log.TwitchState("Connecting", "Attempt=" + (attempt + 1));
                    await ConnectAndReceiveAsync(ct).ConfigureAwait(false);
                    attempt = 0;
                }
                catch (OperationCanceledException)
                {
                    _log.TwitchState("ConnectLoop cancelled - shutting down");
                    break;
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "EventSub receive loop (attempt " + (attempt + 1) + ")");
                }

                if (!ct.IsCancellationRequested)
                {
                    TimeSpan delay = BackoffDelays[Math.Min(attempt, BackoffDelays.Length - 1)];
                    _log.TwitchState("WaitingBeforeReconnect", "Delay=" + delay.TotalSeconds + "s");
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    attempt++;
                }
            }
        }

        internal async Task SubscribeToRewardAsync(
            string rewardId,
            string channelUserId,
            CancellationToken ct = default(CancellationToken))
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_rewardSubscriptions.ContainsKey(rewardId))
                {
                    _log.EventSub(rewardId, "AlreadySubscribed - skipping duplicate");
                    return;
                }

                if (string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    _log.EventSub(rewardId, "NoSessionYet - queued for subscription after session_welcome");
                    _pendingRewardIds.Add(rewardId);
                    return;
                }

                _log.EventSub(rewardId, "Subscribing", "channelUserId=" + channelUserId);
                string subscriptionId = await _apiClient.CreateEventSubSubscriptionAsync(
                    type: "channel.channel_points_custom_reward_redemption.add",
                    version: "1",
                    condition: new Dictionary<string, string>
                    {
                        { "broadcaster_user_id", channelUserId },
                        { "reward_id", rewardId }
                    },
                    ct: ct).ConfigureAwait(false);

                _rewardSubscriptions[rewardId] = subscriptionId;
                _log.EventSub(rewardId, "SubscribedOK", "subscriptionId=" + subscriptionId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || (_cts != null && _cts.IsCancellationRequested))
            {
                // Genuine shutdown/user cancellation - propagate without re-queuing.
                throw;
            }
            catch (Exception ex)
            {
                // Transient failure (TCP disruption, stale session, etc.).
                // Re-queue so the next session_welcome automatically retries the subscribe.
                _log.Warn("SubscribeToRewardAsync rewardId=" + rewardId + " failed transiently (" + ex.GetType().Name + ") - re-queuing for next session_welcome");
                _pendingRewardIds.Add(rewardId);
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        internal async Task UnsubscribeFromRewardAsync(
            string rewardId,
            CancellationToken ct = default(CancellationToken))
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!_rewardSubscriptions.TryGetValue(rewardId, out string subscriptionId))
                {
                    _log.EventSub(rewardId, "UnsubscribeNoOp - not subscribed");
                    return;
                }

                _log.EventSub(rewardId, "Unsubscribing", "subscriptionId=" + subscriptionId);
                await _apiClient.DeleteEventSubSubscriptionAsync(subscriptionId, ct).ConfigureAwait(false);
                _rewardSubscriptions.Remove(rewardId);
                _log.EventSub(rewardId, "UnsubscribedOK");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "UnsubscribeFromRewardAsync rewardId=" + rewardId);
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        internal async Task EnsureChannelPointSubscriptionsAsync(IEnumerable<string> rewardIds)
        {
            if (rewardIds == null)
            {
                return;
            }

            string channelUserId = await _authManager.GetChannelUserIdAsync(_cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            foreach (string rewardId in rewardIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                await SubscribeToRewardAsync(rewardId, channelUserId, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task ConnectAndReceiveAsync(CancellationToken ct)
        {
            using (var ws = new ClientWebSocket())
            {
                // Twitch EventSub WebSocket does not authenticate at the WS-connect level.
                // Auth happens only when creating subscriptions via REST. No header needed here.
                _log.TwitchState("WebSocket.Connecting", "wss://eventsub.wss.twitch.tv/ws");
                await ws.ConnectAsync(new Uri("wss://eventsub.wss.twitch.tv/ws"), ct).ConfigureAwait(false);

                lock (_stateLock)
                {
                    _isConnected = true;
                }

                _log.TwitchState("WebSocket.Connected");

                var buffer = new byte[16 * 1024];
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    var messageBuilder = new StringBuilder();
                    try
                    {
                        do
                        {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                _log.TwitchState("WebSocket.CloseReceived", "Status=" + result.CloseStatus + " Desc=" + result.CloseStatusDescription);
                                return;
                            }

                            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        while (!result.EndOfMessage);
                    }
                    catch (WebSocketException wsEx)
                    {
                        _log.Warn("WebSocket receive error: " + wsEx.Message + " - reconnecting");
                        break;
                    }

                    try
                    {
                        string message = messageBuilder.ToString();
                        _log.Debug("WebSocket.MessageReceived bytes=" + message.Length);
                        await ProcessMessageAsync(message, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex, "ProcessMessageAsync - message dropped");
                    }
                }
            }

            lock (_stateLock)
            {
                _isConnected = false;
            }

            _log.TwitchState("WebSocket.Disconnected", "State=Closed");
        }

        private async Task ProcessMessageAsync(string message, CancellationToken ct)
        {
            JObject json = JObject.Parse(message);
            string messageType = json["metadata"]?["message_type"]?.ToString() ?? string.Empty;

            switch (messageType)
            {
                case "session_welcome":
                    CurrentSessionId = json["payload"]?["session"]?["id"]?.ToString();
                    _log.TwitchState("SessionWelcome", "sessionId=" + CurrentSessionId);
                    await ResubscribeAllAsync(ct).ConfigureAwait(false);
                    break;

                case "session_keepalive":
                    _log.Debug("SessionKeepalive");
                    break;

                case "session_reconnect":
                    // Twitch is asking us to reconnect. Close immediately so RunWithReconnectAsync
                    // picks up and reconnects to the standard endpoint without waiting 30s for
                    // Twitch to force-close the old socket.
                    string reconnectUrl = json["payload"]?["session"]?["reconnect_url"]?.ToString();
                    _log.TwitchState("SessionReconnectRequested", "reconnect_url=" + reconnectUrl + " - closing to trigger fast reconnect");
                    return;

                case "notification":
                    await HandleNotificationAsync(json, ct).ConfigureAwait(false);
                    break;

                default:
                    _log.Debug("Unhandled message_type=" + messageType);
                    break;
            }
        }

        private Task HandleNotificationAsync(JObject json, CancellationToken ct)
        {
            string subscriptionType = json["metadata"]?["subscription_type"]?.ToString();
            if (!string.Equals(subscriptionType, "channel.channel_points_custom_reward_redemption.add", StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            JToken payload = json["payload"]?["event"];
            if (payload == null)
            {
                return Task.CompletedTask;
            }

            string rewardId = payload["reward"]?["id"]?.ToString();
            string redemptionId = payload["id"]?.ToString();
            string user = payload["user_login"]?.ToString() ?? payload["user_name"]?.ToString();
            _log.ChannelPoint(rewardId ?? "UNKNOWN", "RedemptionReceived", "redemptionId=" + redemptionId + " user=" + user);

            try
            {
                OnChannelPointRedeemed?.Invoke(new ChannelPointRedemption
                {
                    RedemptionId = redemptionId ?? string.Empty,
                    RewardId = rewardId ?? string.Empty,
                    RewardTitle = payload["reward"]?["title"]?.ToString() ?? string.Empty,
                    UserName = payload["user_name"]?.ToString() ?? "Unknown",
                    UserId = payload["user_id"]?.ToString() ?? string.Empty,
                    UserInput = payload["user_input"]?.ToString() ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnChannelPointRedeemed invoke");
            }

            return Task.CompletedTask;
        }

        private async Task ResubscribeAllAsync(CancellationToken ct)
        {
            var localSnapshot = new Dictionary<string, string>(_rewardSubscriptions, StringComparer.Ordinal);
            var pending = new HashSet<string>(_pendingRewardIds, StringComparer.Ordinal);

            _log.Info("Resubscribing " + localSnapshot.Count + " known rewards + " + pending.Count + " pending after session_welcome");
            _rewardSubscriptions.Clear();
            _pendingRewardIds.Clear();

            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);

            // Re-subscribe rewards that were previously active.
            foreach (var kvp in localSnapshot)
            {
                await SubscribeToRewardAsync(kvp.Key, channelUserId, ct).ConfigureAwait(false);
            }

            // Subscribe rewards that were queued because no session existed yet.
            foreach (string rewardId in pending)
            {
                if (!_rewardSubscriptions.ContainsKey(rewardId))
                {
                    await SubscribeToRewardAsync(rewardId, channelUserId, ct).ConfigureAwait(false);
                }
            }

            // Bootstrap from PluginConfig when neither cache nor queue has entries.
            // This handles fresh startup where the WS connects before the UI tab is ever opened.
            // Without this, Twitch closes the connection with 4003 (connection unused) every ~10s.
            if (localSnapshot.Count == 0 && pending.Count == 0)
            {
                await BootstrapConfigRewardSubscriptionsAsync(channelUserId, ct).ConfigureAwait(false);
            }

            _log.Info("Resubscription complete");
        }

        private async Task BootstrapConfigRewardSubscriptionsAsync(string channelUserId, CancellationToken ct)
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null) return;

            var ids = new List<string>(8);
            if (cfg.CpRainbowEnabled && !string.IsNullOrWhiteSpace(cfg.CpRainbowRewardId)) ids.Add(cfg.CpRainbowRewardId);
            if (cfg.CpDisappearEnabled && !string.IsNullOrWhiteSpace(cfg.CpDisappearRewardId)) ids.Add(cfg.CpDisappearRewardId);
            if (cfg.CpGhostEnabled && !string.IsNullOrWhiteSpace(cfg.CpGhostRewardId)) ids.Add(cfg.CpGhostRewardId);
            if (cfg.CpBombEnabled && !string.IsNullOrWhiteSpace(cfg.CpBombRewardId)) ids.Add(cfg.CpBombRewardId);
            if (cfg.CpFasterEnabled && !string.IsNullOrWhiteSpace(cfg.CpFasterRewardId)) ids.Add(cfg.CpFasterRewardId);
            if (cfg.CpSuperFastEnabled && !string.IsNullOrWhiteSpace(cfg.CpSuperFastRewardId)) ids.Add(cfg.CpSuperFastRewardId);
            if (cfg.CpSlowerEnabled && !string.IsNullOrWhiteSpace(cfg.CpSlowerRewardId)) ids.Add(cfg.CpSlowerRewardId);
            if (cfg.CpFlashbangEnabled && !string.IsNullOrWhiteSpace(cfg.CpFlashbangRewardId)) ids.Add(cfg.CpFlashbangRewardId);

            if (ids.Count == 0) return;

            _log.Info("Bootstrapping " + ids.Count + " EventSub subscriptions from PluginConfig");
            foreach (string rewardId in ids)
            {
                if (!_rewardSubscriptions.ContainsKey(rewardId))
                {
                    await SubscribeToRewardAsync(rewardId, channelUserId, ct).ConfigureAwait(false);
                }
            }
        }
    }
}
