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

        internal sealed class FollowNotification
        {
            internal string EventId;
            internal string UserId;
            internal string UserLogin;
            internal string UserName;
        }

        internal sealed class SubscriberNotification
        {
            internal string UserId;
            internal string UserLogin;
            internal string UserName;
            internal string Tier;
            internal int CumulativeMonths;
            internal int GiftCount;
            internal bool IsGift;
            internal bool IsAnonymous;
            internal string EventSubKind;  // "sub" | "resub" | "giftsub" | "subend"
        }

        internal sealed class RaidNotification
        {
            internal string EventId;
            internal string FromBroadcasterUserId;
            internal string FromBroadcasterUserLogin;
            internal string FromBroadcasterUserName;
            internal string ToBroadcasterUserId;
            internal int Viewers;
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
        private readonly Queue<string> _recentFollowEventIds = new Queue<string>();
        private readonly HashSet<string> _recentFollowEventIdSet = new HashSet<string>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _subscriptionLock = new SemaphoreSlim(1, 1);
        private readonly object _stateLock = new object();
        private string _followSubscriptionId = string.Empty;
        private bool _pendingFollowSubscription;
        private string _subscribeSubId = string.Empty;
        private string _subscribeResubId = string.Empty;
        private string _subscribeGiftId = string.Empty;
        private string _subscribeEndId = string.Empty;
        private string _subscribeChatNotificationId = string.Empty;
        private bool _pendingSubscribeSubscriptions;
        private string _raidSubscriptionId = string.Empty;
        private bool _pendingRaidSubscription;

        private CancellationTokenSource _cts;
        private Task _receiveLoop;
        private volatile bool _isConnected;

        internal event Action<ChannelPointRedemption> OnChannelPointRedeemed;
        internal event Action<FollowNotification> OnFollowReceived;
        internal event Action<SubscriberNotification> OnSubscriptionReceived;
        internal event Action<RaidNotification> OnRaidReceived;

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
            _authManager.OnTokensUpdated += HandleAuthStateChanged;
            _authManager.OnIdentityUpdated += HandleAuthStateChanged;
            _authManager.OnReauthRequired += HandleAuthStateChanged;
            EntitlementsState.Changed += HandleSupporterStateChanged;

            if (PluginConfig.Instance != null && PluginConfig.Instance.HasValidToken)
            {
                if (ShouldKeepConnectionAlive())
                {
                    StartReceiveLoop();
                }
                else
                {
                    _log.TwitchState("DeferredConnect", "WaitingForConfiguredSubscriptions");
                }
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
            _authManager.OnTokensUpdated -= HandleAuthStateChanged;
            _authManager.OnIdentityUpdated -= HandleAuthStateChanged;
            _authManager.OnReauthRequired -= HandleAuthStateChanged;
            EntitlementsState.Changed -= HandleSupporterStateChanged;

            try
            {
                StopReceiveLoopAsync().GetAwaiter().GetResult();
                _log.Lifecycle("Dispose - receive loop stopped");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Dispose");
            }
            finally
            {
                _cts = null;
                _receiveLoop = null;
                _isConnected = false;
                _followSubscriptionId = string.Empty;
                _pendingFollowSubscription = false;
                _recentFollowEventIds.Clear();
                _recentFollowEventIdSet.Clear();
                _subscribeSubId = string.Empty;
                _subscribeResubId = string.Empty;
                _subscribeGiftId = string.Empty;
                _subscribeEndId = string.Empty;
                _subscribeChatNotificationId = string.Empty;
                _pendingSubscribeSubscriptions = false;
                _raidSubscriptionId = string.Empty;
                _pendingRaidSubscription = false;
            }
        }

        internal Task ConnectAsync()
        {
            StartReceiveLoop();

            return Task.CompletedTask;
        }

        internal void Shutdown() => Dispose();

        internal async Task RefreshSubscriptionsAsync(CancellationToken ct = default(CancellationToken))
        {
            bool shouldFollow = ShouldSubscribeToFollowEffects();
            bool shouldSubscriber = ShouldSubscribeToSubscriberEffects();
            bool shouldRaid = ShouldSubscribeToRaidEffects();
            bool shouldKeepAlive = HasConfiguredRewardSubscriptions() || shouldFollow || shouldSubscriber || shouldRaid;

            if (!shouldKeepAlive)
            {
                await RemoveFollowSubscriptionAsync(ct).ConfigureAwait(false);
                await RemoveSubscribeSubscriptionsAsync(ct).ConfigureAwait(false);
                await RemoveRaidSubscriptionAsync(ct).ConfigureAwait(false);
                await StopReceiveLoopAsync().ConfigureAwait(false);
                _log.TwitchState("SubscriptionsRefresh", "NoConfiguredSubscriptions");
                return;
            }

            StartReceiveLoop();

            if (!shouldFollow)
            {
                await RemoveFollowSubscriptionAsync(ct).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
                    await EnsureFollowSubscriptionAsync(channelUserId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warn("RefreshSubscriptionsAsync follow refresh failed: " + ex.Message);
                }
            }

            if (!shouldSubscriber)
            {
                await RemoveSubscribeSubscriptionsAsync(ct).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
                    await EnsureSubscribeSubscriptionsAsync(channelUserId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warn("RefreshSubscriptionsAsync subscriber refresh failed: " + ex.Message);
                }
            }

            if (!shouldRaid)
            {
                await RemoveRaidSubscriptionAsync(ct).ConfigureAwait(false);
            }
            else
            {
                try
                {
                    string channelUserId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
                    await EnsureRaidSubscriptionAsync(channelUserId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warn("RefreshSubscriptionsAsync raid refresh failed: " + ex.Message);
                }
            }
        }

        private void HandleAuthReady()
        {
            _ = RefreshSubscriptionsAsync();
        }

        private void HandleAuthStateChanged()
        {
            _ = RefreshSubscriptionsAsync();
        }

        private void HandleSupporterStateChanged()
        {
            _ = RefreshSubscriptionsAsync();
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

        private async Task StopReceiveLoopAsync()
        {
            CancellationTokenSource cts;
            Task receiveLoop;

            lock (_stateLock)
            {
                cts = _cts;
                receiveLoop = _receiveLoop;
                _cts = null;
                _receiveLoop = null;
                _isConnected = false;
            }

            CurrentSessionId = null;

            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "StopReceiveLoopAsync.Cancel");
            }

            if (receiveLoop != null)
            {
                try
                {
                    await receiveLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "StopReceiveLoopAsync.Wait");
                }
            }

            cts.Dispose();
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
                    _pendingRewardIds.Add(rewardId);
                    _log.EventSub(rewardId, "NoSessionYet - queued for subscription after session_welcome");
                    StartReceiveLoop();
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
                                break;
                            }

                            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        }
                        while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }
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

            CurrentSessionId = null;

            _log.TwitchState("WebSocket.Disconnected", "State=Closed");
        }

        private static bool HasConfiguredRewardSubscriptions()
        {
            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                return false;
            }

            return (cfg.CpRainbowEnabled && !string.IsNullOrWhiteSpace(cfg.CpRainbowRewardId))
                || (cfg.CpDisappearEnabled && !string.IsNullOrWhiteSpace(cfg.CpDisappearRewardId))
                || (cfg.CpGhostEnabled && !string.IsNullOrWhiteSpace(cfg.CpGhostRewardId))
                || (cfg.CpBombEnabled && !string.IsNullOrWhiteSpace(cfg.CpBombRewardId))
                || (cfg.CpFasterEnabled && !string.IsNullOrWhiteSpace(cfg.CpFasterRewardId))
                || (cfg.CpSuperFastEnabled && !string.IsNullOrWhiteSpace(cfg.CpSuperFastRewardId))
                || (cfg.CpSlowerEnabled && !string.IsNullOrWhiteSpace(cfg.CpSlowerRewardId))
                || (cfg.CpFlashbangEnabled && !string.IsNullOrWhiteSpace(cfg.CpFlashbangRewardId));
        }

        private static bool ShouldSubscribeToFollowEffects()
        {
            return FollowEffectAccessController.ShouldMaintainSubscription();
        }

        private static bool ShouldSubscribeToSubscriberEffects()
        {
            return SubscriberEffectAccessController.ShouldMaintainSubscription();
        }

        private static bool ShouldSubscribeToRaidEffects()
        {
            return RaidEffectAccessController.ShouldMaintainSubscription();
        }

        private static bool ShouldKeepConnectionAlive()
        {
            return HasConfiguredRewardSubscriptions()
                || ShouldSubscribeToFollowEffects()
                || ShouldSubscribeToSubscriberEffects()
                || ShouldSubscribeToRaidEffects();
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
            if (string.Equals(subscriptionType, "channel.follow", StringComparison.Ordinal))
            {
                HandleFollowNotification(json);
                return Task.CompletedTask;
            }

            if (string.Equals(subscriptionType, "channel.raid", StringComparison.Ordinal))
            {
                HandleRaidNotification(json);
                return Task.CompletedTask;
            }

            if (string.Equals(subscriptionType, "channel.subscribe", StringComparison.Ordinal))
            {
                HandleSubscribeNotification(json);
                return Task.CompletedTask;
            }

            if (string.Equals(subscriptionType, "channel.subscription.message", StringComparison.Ordinal))
            {
                HandleResubNotification(json);
                return Task.CompletedTask;
            }

            if (string.Equals(subscriptionType, "channel.subscription.gift", StringComparison.Ordinal))
            {
                HandleGiftSubNotification(json);
                return Task.CompletedTask;
            }

            if (string.Equals(subscriptionType, "channel.subscription.end", StringComparison.Ordinal))
            {
                HandleSubscriptionEndNotification(json);
                return Task.CompletedTask;
            }

            if (string.Equals(subscriptionType, "channel.chat.notification", StringComparison.Ordinal))
            {
                HandleChatNotification(json);
                return Task.CompletedTask;
            }

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

        private void HandleFollowNotification(JObject json)
        {
            JToken payload = json["payload"]?["event"];
            if (payload == null)
            {
                return;
            }

            string eventId = json["metadata"]?["message_id"]?.ToString();
            if (string.IsNullOrWhiteSpace(eventId))
            {
                eventId = (payload["user_id"]?.ToString() ?? string.Empty)
                    + "|"
                    + (payload["followed_at"]?.ToString() ?? string.Empty);
            }

            if (!TrackFollowEventId(eventId))
            {
                _log.Debug("Follow notification duplicate ignored eventId=" + eventId);
                return;
            }

            var notification = new FollowNotification
            {
                EventId = eventId,
                UserId = payload["user_id"]?.ToString() ?? string.Empty,
                UserLogin = payload["user_login"]?.ToString() ?? string.Empty,
                UserName = payload["user_name"]?.ToString() ?? string.Empty
            };

            _log.Info(
                "Follow notification received user="
                + (string.IsNullOrWhiteSpace(notification.UserName) ? notification.UserLogin : notification.UserName)
                + " userId="
                + notification.UserId);

            try
            {
                OnFollowReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnFollowReceived invoke");
            }
        }

        private void HandleRaidNotification(JObject json)
        {
            JToken payload = json["payload"]?["event"];
            if (payload == null)
            {
                return;
            }

            string eventId = json["metadata"]?["message_id"]?.ToString();
            if (string.IsNullOrWhiteSpace(eventId))
            {
                eventId = (payload["from_broadcaster_user_id"]?.ToString() ?? string.Empty)
                    + "|"
                    + (payload["to_broadcaster_user_id"]?.ToString() ?? string.Empty)
                    + "|"
                    + (payload["viewers"]?.ToString() ?? string.Empty);
            }

            int viewers = 0;
            try
            {
                viewers = payload["viewers"]?.Value<int>() ?? 0;
            }
            catch
            {
                int.TryParse(payload["viewers"]?.ToString(), out viewers);
            }

            var notification = new RaidNotification
            {
                EventId = eventId,
                FromBroadcasterUserId = payload["from_broadcaster_user_id"]?.ToString() ?? string.Empty,
                FromBroadcasterUserLogin = payload["from_broadcaster_user_login"]?.ToString() ?? string.Empty,
                FromBroadcasterUserName = payload["from_broadcaster_user_name"]?.ToString() ?? string.Empty,
                ToBroadcasterUserId = payload["to_broadcaster_user_id"]?.ToString() ?? string.Empty,
                Viewers = Math.Max(0, viewers)
            };

            _log.Info(
                "Raid notification received from="
                + (string.IsNullOrWhiteSpace(notification.FromBroadcasterUserName)
                    ? notification.FromBroadcasterUserLogin
                    : notification.FromBroadcasterUserName)
                + " viewers="
                + notification.Viewers
                + " fromId="
                + notification.FromBroadcasterUserId);

            try
            {
                OnRaidReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnRaidReceived invoke");
            }
        }

        private void HandleSubscribeNotification(JObject json)
        {
            JToken payload = json["payload"]?["event"];
            if (payload == null)
            {
                return;
            }

            bool isGift = ReadBool(payload["is_gift"]);
            if (isGift)
            {
                // Gift recipients also get channel.subscribe; celebration comes from channel.subscription.gift / chat.notification.
                _log.Info(
                    "Subscribe notification skipped (is_gift=true) user="
                    + (payload["user_name"]?.ToString() ?? payload["user_login"]?.ToString() ?? "unknown")
                    + " — gift path handles celebration");
                return;
            }

            var notification = new SubscriberNotification
            {
                UserId = payload["user_id"]?.ToString() ?? string.Empty,
                UserLogin = payload["user_login"]?.ToString() ?? string.Empty,
                UserName = payload["user_name"]?.ToString() ?? string.Empty,
                Tier = payload["tier"]?.ToString() ?? "1000",
                CumulativeMonths = 0,
                GiftCount = 0,
                IsGift = false,
                IsAnonymous = false,
                EventSubKind = "sub"
            };

            _log.Info("Subscribe notification received kind=sub user=" + (string.IsNullOrWhiteSpace(notification.UserName) ? notification.UserLogin : notification.UserName));

            try
            {
                OnSubscriptionReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnSubscriptionReceived invoke (sub)");
            }
        }

        private void HandleResubNotification(JObject json)
        {
            JToken payload = json["payload"]?["event"];
            if (payload == null)
            {
                return;
            }

            var notification = new SubscriberNotification
            {
                UserId = payload["user_id"]?.ToString() ?? string.Empty,
                UserLogin = payload["user_login"]?.ToString() ?? string.Empty,
                UserName = payload["user_name"]?.ToString() ?? string.Empty,
                Tier = payload["tier"]?.ToString() ?? "1000",
                CumulativeMonths = payload["cumulative_months"]?.Value<int>() ?? 0,
                GiftCount = 0,
                IsGift = false,
                IsAnonymous = false,
                EventSubKind = "resub"
            };

            _log.Info(
                "Resub notification received kind=resub source=channel.subscription.message user="
                + (string.IsNullOrWhiteSpace(notification.UserName) ? notification.UserLogin : notification.UserName)
                + " months="
                + notification.CumulativeMonths);

            try
            {
                OnSubscriptionReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnSubscriptionReceived invoke (resub)");
            }
        }

        private void HandleGiftSubNotification(JObject json)
        {
            JToken payload = json["payload"]?["event"];
            if (payload == null)
            {
                return;
            }

            bool isAnonymous = ReadBool(payload["is_anonymous"]);
            string userName = isAnonymous ? "Anonymous" : (payload["user_name"]?.ToString() ?? string.Empty);
            string userLogin = isAnonymous ? "Anonymous" : (payload["user_login"]?.ToString() ?? string.Empty);

            var notification = new SubscriberNotification
            {
                UserId = payload["user_id"]?.ToString() ?? string.Empty,
                UserLogin = userLogin,
                UserName = userName,
                Tier = payload["tier"]?.ToString() ?? "1000",
                CumulativeMonths = 0,
                GiftCount = payload["total"]?.Value<int>() ?? 1,
                IsGift = true,
                IsAnonymous = isAnonymous,
                EventSubKind = "giftsub"
            };

            _log.Info(
                "GiftSub notification received kind=giftsub source=channel.subscription.gift gifter="
                + (string.IsNullOrWhiteSpace(notification.UserName) ? notification.UserLogin : notification.UserName)
                + " total="
                + notification.GiftCount);

            try
            {
                OnSubscriptionReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnSubscriptionReceived invoke (giftsub)");
            }
        }

        private void HandleSubscriptionEndNotification(JObject json)
        {
            JToken payload = json["payload"]?["event"];
            if (payload == null)
            {
                return;
            }

            string user = payload["user_name"]?.ToString() ?? payload["user_login"]?.ToString() ?? "unknown";
            // Subscription end is not a celebratory chat-notice equivalent; do not fire !smsg.
            _log.Info("Subscription end notification received (celebratory smsg skipped) user=" + user);
        }

        private void HandleChatNotification(JObject json)
        {
            JToken payload = json["payload"]?["event"];
            if (payload == null)
            {
                return;
            }

            string noticeType = payload["notice_type"]?.ToString() ?? string.Empty;
            string normalizedNotice = noticeType.Trim().ToLowerInvariant();

            // Individual gift recipients spam once per gifted user; gifter celebration uses
            // community_sub_gift / channel.subscription.gift instead.
            if (normalizedNotice == "sub_gift" || normalizedNotice == "shared_chat_sub_gift")
            {
                _log.Info(
                    "Chat notification skipped notice_type="
                    + noticeType
                    + " — individual gift recipient (gifter path handles celebration)");
                return;
            }

            string eventKind;
            JToken detail;
            switch (normalizedNotice)
            {
                case "sub":
                    eventKind = "sub";
                    detail = payload["sub"];
                    break;
                case "shared_chat_sub":
                    eventKind = "sub";
                    detail = payload["shared_chat_sub"];
                    break;
                case "resub":
                    eventKind = "resub";
                    detail = payload["resub"];
                    break;
                case "shared_chat_resub":
                    eventKind = "resub";
                    detail = payload["shared_chat_resub"];
                    break;
                case "community_sub_gift":
                    eventKind = "giftsub";
                    detail = payload["community_sub_gift"];
                    break;
                case "shared_chat_community_sub_gift":
                    eventKind = "giftsub";
                    detail = payload["shared_chat_community_sub_gift"];
                    break;
                default:
                    _log.Debug("Chat notification ignored notice_type=" + noticeType);
                    return;
            }

            if (detail == null || detail.Type == JTokenType.Null)
            {
                _log.Warn("Chat notification missing detail object notice_type=" + noticeType);
                return;
            }

            // Gifted sub/resub share notices still carry is_gift=true; skip to avoid gift double-fire.
            if ((eventKind == "sub" || eventKind == "resub") && ReadBool(detail["is_gift"]))
            {
                _log.Info(
                    "Chat notification skipped notice_type="
                    + noticeType
                    + " is_gift=true — gift path handles celebration");
                return;
            }

            bool isAnonymous = ReadBool(payload["chatter_is_anonymous"]);
            string userName = isAnonymous ? "Anonymous" : (payload["chatter_user_name"]?.ToString() ?? string.Empty);
            string userLogin = isAnonymous ? "Anonymous" : (payload["chatter_user_login"]?.ToString() ?? string.Empty);
            string userId = isAnonymous ? string.Empty : (payload["chatter_user_id"]?.ToString() ?? string.Empty);

            string tier = detail["sub_plan"]?.ToString()
                ?? detail["tier"]?.ToString()
                ?? "1000";

            int cumulativeMonths = detail["cumulative_months"]?.Value<int>() ?? 0;
            int giftCount = eventKind == "giftsub"
                ? (detail["total"]?.Value<int>() ?? 1)
                : 0;

            var notification = new SubscriberNotification
            {
                UserId = userId,
                UserLogin = userLogin,
                UserName = userName,
                Tier = tier,
                CumulativeMonths = cumulativeMonths,
                GiftCount = giftCount,
                IsGift = eventKind == "giftsub",
                IsAnonymous = isAnonymous,
                EventSubKind = eventKind
            };

            _log.Info(
                "Chat notification received kind="
                + eventKind
                + " notice_type="
                + noticeType
                + " user="
                + (string.IsNullOrWhiteSpace(notification.UserName) ? notification.UserLogin : notification.UserName)
                + (eventKind == "resub" ? " months=" + notification.CumulativeMonths : string.Empty)
                + (eventKind == "giftsub" ? " total=" + notification.GiftCount : string.Empty));

            try
            {
                OnSubscriptionReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnSubscriptionReceived invoke (chat.notification/" + eventKind + ")");
            }
        }

        private static bool ReadBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            try
            {
                return token.Value<bool>();
            }
            catch
            {
                return string.Equals(token.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        private async Task ResubscribeAllAsync(CancellationToken ct)
        {
            var localSnapshot = new Dictionary<string, string>(_rewardSubscriptions, StringComparer.Ordinal);
            var pending = new HashSet<string>(_pendingRewardIds, StringComparer.Ordinal);
            _log.Info("Resubscribing " + localSnapshot.Count + " known rewards + " + pending.Count + " pending after session_welcome");
            _rewardSubscriptions.Clear();
            _pendingRewardIds.Clear();
            _followSubscriptionId = string.Empty;
            _pendingFollowSubscription = false;
            _subscribeSubId = string.Empty;
            _subscribeResubId = string.Empty;
            _subscribeGiftId = string.Empty;
            _subscribeEndId = string.Empty;
            _subscribeChatNotificationId = string.Empty;
            _pendingSubscribeSubscriptions = false;
            _raidSubscriptionId = string.Empty;
            _pendingRaidSubscription = false;

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

            if (ShouldSubscribeToFollowEffects())
            {
                await EnsureFollowSubscriptionAsync(channelUserId, ct).ConfigureAwait(false);
            }

            if (ShouldSubscribeToSubscriberEffects())
            {
                await EnsureSubscribeSubscriptionsAsync(channelUserId, ct).ConfigureAwait(false);
            }

            if (ShouldSubscribeToRaidEffects())
            {
                await EnsureRaidSubscriptionAsync(channelUserId, ct).ConfigureAwait(false);
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

        private async Task EnsureFollowSubscriptionAsync(string channelUserId, CancellationToken ct)
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_followSubscriptionId.Length > 0)
                {
                    return;
                }

                if (!ShouldSubscribeToFollowEffects())
                {
                    _pendingFollowSubscription = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    _pendingFollowSubscription = true;
                    _log.TwitchState("FollowSubscriptionDeferred", "WaitingForSessionWelcome");
                    StartReceiveLoop();
                    return;
                }

                _log.TwitchState("FollowSubscription", "Subscribing broadcasterUserId=" + channelUserId);
                _followSubscriptionId = await _apiClient.CreateEventSubSubscriptionAsync(
                    type: "channel.follow",
                    version: "2",
                    condition: new Dictionary<string, string>
                    {
                        { "broadcaster_user_id", channelUserId },
                        { "moderator_user_id", channelUserId }
                    },
                    ct: ct).ConfigureAwait(false);

                _pendingFollowSubscription = false;
                _log.TwitchState("FollowSubscription", "Subscribed subscriptionId=" + _followSubscriptionId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || (_cts != null && _cts.IsCancellationRequested))
            {
                throw;
            }
            catch (Exception ex)
            {
                _pendingFollowSubscription = true;
                _log.Warn("EnsureFollowSubscriptionAsync failed transiently (" + ex.GetType().Name + ") - re-queuing for next session_welcome");
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        private async Task RemoveFollowSubscriptionAsync(CancellationToken ct)
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _pendingFollowSubscription = false;

                if (string.IsNullOrWhiteSpace(_followSubscriptionId))
                {
                    return;
                }

                string subscriptionId = _followSubscriptionId;
                _followSubscriptionId = string.Empty;

                if (string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    return;
                }

                _log.TwitchState("FollowSubscription", "Unsubscribing subscriptionId=" + subscriptionId);
                await _apiClient.DeleteEventSubSubscriptionAsync(subscriptionId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RemoveFollowSubscriptionAsync");
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        private async Task EnsureRaidSubscriptionAsync(string channelUserId, CancellationToken ct)
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_raidSubscriptionId.Length > 0)
                {
                    return;
                }

                if (!ShouldSubscribeToRaidEffects())
                {
                    _pendingRaidSubscription = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    _pendingRaidSubscription = true;
                    _log.TwitchState("RaidSubscriptionDeferred", "WaitingForSessionWelcome");
                    StartReceiveLoop();
                    return;
                }

                // Twitch channel.raid uses to_broadcaster_user_id when listening for inbound raids.
                // No additional OAuth scope is required beyond a valid user access token for the broadcaster.
                _log.TwitchState("RaidSubscription", "Subscribing to_broadcaster_user_id=" + channelUserId);
                bool created = await TryCreateSubscribeTopicAsync(
                    "channel.raid",
                    "1",
                    new Dictionary<string, string> { { "to_broadcaster_user_id", channelUserId } },
                    () => _raidSubscriptionId,
                    id => _raidSubscriptionId = id,
                    channelUserId,
                    ct).ConfigureAwait(false);

                _pendingRaidSubscription = !created;
                if (created)
                {
                    _log.TwitchState("RaidSubscription", "Subscribed subscriptionId=" + _raidSubscriptionId);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || (_cts != null && _cts.IsCancellationRequested))
            {
                throw;
            }
            catch (Exception ex)
            {
                _pendingRaidSubscription = true;
                _log.Warn("EnsureRaidSubscriptionAsync failed transiently (" + ex.GetType().Name + ") - re-queuing for next session_welcome");
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        private async Task RemoveRaidSubscriptionAsync(CancellationToken ct)
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _pendingRaidSubscription = false;

                if (string.IsNullOrWhiteSpace(_raidSubscriptionId))
                {
                    return;
                }

                string subscriptionId = _raidSubscriptionId;
                _raidSubscriptionId = string.Empty;

                if (string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    return;
                }

                _log.TwitchState("RaidSubscription", "Unsubscribing subscriptionId=" + subscriptionId);
                await _apiClient.DeleteEventSubSubscriptionAsync(subscriptionId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RemoveRaidSubscriptionAsync");
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        private async Task EnsureSubscribeSubscriptionsAsync(string channelUserId, CancellationToken ct)
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_subscribeSubId.Length > 0
                    && _subscribeResubId.Length > 0
                    && _subscribeGiftId.Length > 0
                    && _subscribeEndId.Length > 0
                    && _subscribeChatNotificationId.Length > 0)
                {
                    return;
                }

                if (!ShouldSubscribeToSubscriberEffects())
                {
                    _pendingSubscribeSubscriptions = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    _pendingSubscribeSubscriptions = true;
                    _log.TwitchState("SubscribeSubscriptionsDeferred", "WaitingForSessionWelcome");
                    StartReceiveLoop();
                    return;
                }

                var condition = new Dictionary<string, string> { { "broadcaster_user_id", channelUserId } };
                var chatNotificationCondition = new Dictionary<string, string>
                {
                    { "broadcaster_user_id", channelUserId },
                    { "user_id", channelUserId }
                };

                bool anyFailed = false;

                // Create independently so one topic failure does not block the others.
                anyFailed |= !await TryCreateSubscribeTopicAsync(
                    "channel.subscribe",
                    "1",
                    condition,
                    () => _subscribeSubId,
                    id => _subscribeSubId = id,
                    channelUserId,
                    ct).ConfigureAwait(false);

                anyFailed |= !await TryCreateSubscribeTopicAsync(
                    "channel.subscription.message",
                    "1",
                    condition,
                    () => _subscribeResubId,
                    id => _subscribeResubId = id,
                    channelUserId,
                    ct).ConfigureAwait(false);

                anyFailed |= !await TryCreateSubscribeTopicAsync(
                    "channel.subscription.gift",
                    "1",
                    condition,
                    () => _subscribeGiftId,
                    id => _subscribeGiftId = id,
                    channelUserId,
                    ct).ConfigureAwait(false);

                anyFailed |= !await TryCreateSubscribeTopicAsync(
                    "channel.subscription.end",
                    "1",
                    condition,
                    () => _subscribeEndId,
                    id => _subscribeEndId = id,
                    channelUserId,
                    ct).ConfigureAwait(false);

                anyFailed |= !await TryCreateSubscribeTopicAsync(
                    "channel.chat.notification",
                    "1",
                    chatNotificationCondition,
                    () => _subscribeChatNotificationId,
                    id => _subscribeChatNotificationId = id,
                    channelUserId,
                    ct).ConfigureAwait(false);

                _pendingSubscribeSubscriptions = anyFailed;
                _log.TwitchState(
                    "SubscribeSubscriptions",
                    "Subscribed subId="
                    + _subscribeSubId
                    + " resubId="
                    + _subscribeResubId
                    + " giftId="
                    + _subscribeGiftId
                    + " endId="
                    + _subscribeEndId
                    + " chatNotificationId="
                    + _subscribeChatNotificationId
                    + " pendingRetry="
                    + anyFailed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || (_cts != null && _cts.IsCancellationRequested))
            {
                throw;
            }
            catch (Exception ex)
            {
                _pendingSubscribeSubscriptions = true;
                _log.Warn("EnsureSubscribeSubscriptionsAsync failed transiently (" + ex.GetType().Name + ") - re-queuing for next session_welcome");
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        private async Task<bool> TryCreateSubscribeTopicAsync(
            string type,
            string version,
            Dictionary<string, string> condition,
            Func<string> getId,
            Action<string> setId,
            string channelUserId,
            CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(getId()))
            {
                return true;
            }

            try
            {
                _log.TwitchState("SubscribeSubscription", "Subscribing " + type + " broadcasterUserId=" + channelUserId);
                string id = await _apiClient.CreateEventSubSubscriptionAsync(
                    type: type,
                    version: version,
                    condition: condition,
                    ct: ct).ConfigureAwait(false);
                setId(id ?? string.Empty);
                if (string.IsNullOrWhiteSpace(getId()))
                {
                    _log.Warn("SubscribeSubscription returned empty id for type=" + type);
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested || (_cts != null && _cts.IsCancellationRequested))
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn(
                    "SubscribeSubscription failed type="
                    + type
                    + " ("
                    + ex.GetType().Name
                    + "): "
                    + ex.Message
                    + " - will retry on next session_welcome");
                return false;
            }
        }

        private async Task RemoveSubscribeSubscriptionsAsync(CancellationToken ct)
        {
            await _subscriptionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _pendingSubscribeSubscriptions = false;

                if (!string.IsNullOrWhiteSpace(CurrentSessionId))
                {
                    if (_subscribeSubId.Length > 0)
                    {
                        string id = _subscribeSubId;
                        _subscribeSubId = string.Empty;
                        try { await _apiClient.DeleteEventSubSubscriptionAsync(id, ct).ConfigureAwait(false); } catch { }
                    }

                    if (_subscribeResubId.Length > 0)
                    {
                        string id = _subscribeResubId;
                        _subscribeResubId = string.Empty;
                        try { await _apiClient.DeleteEventSubSubscriptionAsync(id, ct).ConfigureAwait(false); } catch { }
                    }

                    if (_subscribeGiftId.Length > 0)
                    {
                        string id = _subscribeGiftId;
                        _subscribeGiftId = string.Empty;
                        try { await _apiClient.DeleteEventSubSubscriptionAsync(id, ct).ConfigureAwait(false); } catch { }
                    }

                    if (_subscribeEndId.Length > 0)
                    {
                        string id = _subscribeEndId;
                        _subscribeEndId = string.Empty;
                        try { await _apiClient.DeleteEventSubSubscriptionAsync(id, ct).ConfigureAwait(false); } catch { }
                    }

                    if (_subscribeChatNotificationId.Length > 0)
                    {
                        string id = _subscribeChatNotificationId;
                        _subscribeChatNotificationId = string.Empty;
                        try { await _apiClient.DeleteEventSubSubscriptionAsync(id, ct).ConfigureAwait(false); } catch { }
                    }
                }
                else
                {
                    _subscribeSubId = string.Empty;
                    _subscribeResubId = string.Empty;
                    _subscribeGiftId = string.Empty;
                    _subscribeEndId = string.Empty;
                    _subscribeChatNotificationId = string.Empty;
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RemoveSubscribeSubscriptionsAsync");
            }
            finally
            {
                _subscriptionLock.Release();
            }
        }

        private bool TrackFollowEventId(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return true;
            }

            if (_recentFollowEventIdSet.Contains(eventId))
            {
                return false;
            }

            _recentFollowEventIdSet.Add(eventId);
            _recentFollowEventIds.Enqueue(eventId);

            while (_recentFollowEventIds.Count > 64)
            {
                string expiredId = _recentFollowEventIds.Dequeue();
                _recentFollowEventIdSet.Remove(expiredId);
            }

            return true;
        }
    }
}
