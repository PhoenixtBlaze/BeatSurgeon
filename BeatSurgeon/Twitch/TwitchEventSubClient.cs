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
                    throw new InvalidOperationException("Cannot create EventSub reward subscription before session_welcome.");
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
            catch (Exception ex)
            {
                _log.Exception(ex, "SubscribeToRewardAsync rewardId=" + rewardId);
                throw;
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
                string token = await _authManager.GetAccessTokenAsync(ct).ConfigureAwait(false);
                ws.Options.SetRequestHeader("Authorization", "Bearer " + token);

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
                    _log.TwitchState("SessionReconnectRequested");
                    break;

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
            _log.Info("Resubscribing " + localSnapshot.Count + " known rewards after reconnect");
            _rewardSubscriptions.Clear();
            string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
            foreach (var kvp in localSnapshot)
            {
                await SubscribeToRewardAsync(kvp.Key, channelUserId, ct).ConfigureAwait(false);
            }
            _log.Info("Resubscription complete");
        }
    }
}
