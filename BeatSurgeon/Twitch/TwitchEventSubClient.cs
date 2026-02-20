using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BeatSurgeon.Chat;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatSurgeon.Twitch
{
    /// <summary>
    /// Direct Twitch EventSub WebSocket client (no backend needed).
    /// - Connects to wss://eventsub.wss.twitch.tv/ws
    /// - On session_welcome: receives session_id
    /// - Creates subscriptions via Helix POST /helix/eventsub/subscriptions using transport=websocket + session_id
    /// - Receives notifications for chat/follow/sub/raid and emits C# events for ChatManager
    /// </summary>
    public class TwitchEventSubClient
    {
        private const string EventSubWsUrl = "wss://eventsub.wss.twitch.tv/ws";
        private const string HelixBase = "https://api.twitch.tv/helix";
        private const int ReceiveBufferSize = 32 * 1024;

        private readonly string _userAccessToken;
        private readonly string _clientId;
        private readonly string _broadcasterId;
        private readonly string _botUserId;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;

        private string _sessionId;
        private string _reconnectUrl;
        private bool _isConnecting;
        private bool _isConnected;

        // Reconnect/backoff state
        private int _reconnectAttempts = 0;
        private const int MaxReconnectAttempts = 10;          // safety cap
        private const int BaseReconnectDelaySeconds = 2;      // 2, 4, 8, ... up to 60
        private bool _allowReconnect = true;                  // disabled on Shutdown()


        // Track subscription attempts so we don't spam
        private readonly HashSet<string> _subscribedTypes = new HashSet<string>();

        // Reuse one HttpClient per instance (fine for a mod)
        private readonly HttpClient _http = new HttpClient();

        // Events (match ChatManager wiring)
        public event Action<ChatContext> OnChatMessage;
        public event Action<string> OnFollow;
        public event Action<string, int> OnSubscription;
        public event Action<string, int> OnRaid;
        public sealed class ChannelPointRedemption
        {
            public string RedemptionId;      // ADD THIS LINE
            public string RewardId;
            public string RewardTitle;
            public string UserName;
            public string UserId;
            public string UserInput;
        }


        public event Action<ChannelPointRedemption> OnChannelPointRedeemed;


        public bool IsConnected => _isConnected && _ws != null && _ws.State == WebSocketState.Open;
        public bool HasSession => !string.IsNullOrEmpty(_sessionId);

        public TwitchEventSubClient(string userAccessToken, string clientId, string broadcasterId, string botUserId)
        {
            _userAccessToken = userAccessToken;
            _clientId = clientId;
            _broadcasterId = broadcasterId;
            _botUserId = botUserId;

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Client-Id", _clientId);
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_userAccessToken}");
        }

        /// <summary>
        /// Unity coroutine wrapper (your ChatManager already expects a coroutine).
        /// </summary>
        public IEnumerator ConnectCoroutine()
        {
            if (_isConnecting || IsConnected)
                yield break;

            _isConnecting = true;

            var task = ConnectAsync();
            while (!task.IsCompleted)
                yield return null;

            _isConnecting = false;

            if (task.IsFaulted)
            {
                Plugin.Log.Error("TwitchEventSubClient: ConnectAsync faulted: " + task.Exception?.GetBaseException().Message);
            }
        }

        public async Task ConnectAsync()
        {
            Shutdown(); // clean any prior state

            _allowReconnect = true;
            _reconnectAttempts = 0;

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            _sessionId = null;
            _reconnectUrl = null;
            _subscribedTypes.Clear();

            LogUtils.Debug(() => $"TwitchEventSubClient: Connecting to {EventSubWsUrl}");
            await _ws.ConnectAsync(new Uri(EventSubWsUrl), _cts.Token);
            _isConnected = true;
            LogUtils.Debug(() => "TwitchEventSubClient: WebSocket connected");

            _ = Task.Run(ListenLoopAsync);
        }


        private async Task ListenLoopAsync()
        {
            var buffer = new byte[ReceiveBufferSize];
            var builder = new StringBuilder();

            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (result.Count > 0)
                    {
                        builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }

                    if (result.EndOfMessage)
                    {
                        var json = builder.ToString();
                        builder.Clear();

                        if (!string.IsNullOrEmpty(json))
                            HandleWsMessage(json);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal on shutdown
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("TwitchEventSubClient: ListenLoop exception: " + ex.Message);
            }
            finally
            {
                _isConnected = false;
                Plugin.Log.Warn("TwitchEventSubClient: WebSocket disconnected");

                // If shutdown was requested, do NOT auto-reconnect
                if (_allowReconnect)
                {
                    // Prefer Twitch-provided reconnect URL; otherwise fall back to the default WS URL
                    string targetUrl = !string.IsNullOrEmpty(_reconnectUrl)
                        ? _reconnectUrl
                        : EventSubWsUrl;

                    _reconnectUrl = null;

                    // Fire-and-forget backoff loop
                    _ = Task.Run(() => ReconnectWithBackoffAsync(targetUrl));
                }
            }
        }

        private async Task ReconnectWithBackoffAsync(string url)
        {
            while (_allowReconnect)
            {
                if (_reconnectAttempts >= MaxReconnectAttempts)
                {
                    Plugin.Log.Error($"TwitchEventSubClient: Reconnect aborted after {_reconnectAttempts} attempts.");
                    // At this point ChatManager will see IsConnected == false and keep using ChatPlex if that backend is active.
                    return;
                }

                int delaySeconds = (int)Math.Min(
                    BaseReconnectDelaySeconds * Math.Pow(2, _reconnectAttempts), // 2,4,8,16,32,64...
                    60
                );

                if (_reconnectAttempts > 0)
                {
                    Plugin.Log.Warn(
                        $"TwitchEventSubClient: Waiting {delaySeconds}s before reconnect attempt #{_reconnectAttempts + 1}..."
                    );
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }

                try
                {
                    await ReconnectAsync(url);
                    _reconnectAttempts = 0;
                    return; // success -> ListenLoopAsync will be started by ReconnectAsync
                }
                catch (OperationCanceledException)
                {
                    // Cancellation from outside – treat as intentional shutdown.
                    return;
                }
                catch (Exception ex)
                {
                    _reconnectAttempts++;
                    Plugin.Log.Error($"TwitchEventSubClient: Reconnect attempt failed: {ex.Message}");
                    // Loop and try again with longer delay
                }
            }
        }


        private void HandleWsMessage(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                string messageType = (string)obj["metadata"]?["message_type"];

                switch (messageType)
                {
                    case "session_welcome":
                        HandleSessionWelcome(obj);
                        break;

                    case "session_keepalive":
                        // no action needed
                        break;

                    case "notification":
                        HandleNotification(obj);
                        break;

                    case "session_reconnect":
                        HandleSessionReconnect(obj);
                        break;

                    case "revocation":
                        HandleRevocation(obj);
                        break;

                    default:
                        Plugin.Log.Debug($"TwitchEventSubClient: Unknown message_type={messageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"TwitchEventSubClient: Failed parsing WS message: {ex}");
                Plugin.Log.Error($"TwitchEventSubClient: Offending JSON (first 2000): {json?.Substring(0, Math.Min(2000, json.Length))}");
            }

        }

        private void HandleSessionWelcome(JObject obj)
        {
            _sessionId = (string)obj["payload"]?["session"]?["id"];
            int keepaliveTimeout = (int?)obj["payload"]?["session"]?["keepalive_timeout_seconds"] ?? 0;

            LogUtils.Debug(() => $"TwitchEventSubClient: session_welcome session_id={_sessionId}, keepalive_timeout_seconds={keepaliveTimeout}");

            // IMPORTANT: Twitch expects you to subscribe shortly after welcome (about 10s by default) [web docs] .
            _ = Task.Run(async () =>
            {
                try
                {
                    // Pick the event types you want. These match your old backend WS events.

                    // Do not subscribe broadly to all channel point redemptions. Subscribe per reward id below.
                    await EnsureSubscriptionAsync("channel.chat.message", "1");
                    await EnsureSubscriptionAsync("channel.follow", "2");
                    await EnsureSubscriptionAsync("channel.subscribe", "1");
                    await EnsureSubscriptionAsync("channel.raid", "1");

                    // Subscribe per configured reward id so we only receive events for rewards we own
                    var cfg = Plugin.Settings;
                    var rewardIds = new List<string>
                    {
                        cfg.CpRainbowEnabled    ? cfg.CpRainbowRewardId    : null,
                        cfg.CpDisappearEnabled  ? cfg.CpDisappearRewardId  : null,
                        cfg.CpGhostEnabled      ? cfg.CpGhostRewardId      : null,
                        cfg.CpBombEnabled       ? cfg.CpBombRewardId       : null,
                        cfg.CpFasterEnabled     ? cfg.CpFasterRewardId     : null,
                        cfg.CpSuperFastEnabled  ? cfg.CpSuperFastRewardId  : null,
                        cfg.CpSlowerEnabled     ? cfg.CpSlowerRewardId     : null,
                        cfg.CpFlashbangEnabled  ? cfg.CpFlashbangRewardId  : null,
                    }.Where(id => !string.IsNullOrWhiteSpace(id));

                    await EnsureChannelPointSubscriptionsAsync(rewardIds);

                }
                catch (Exception ex)
                {
                    Plugin.Log.Error("TwitchEventSubClient: Subscription batch failed: " + ex.Message);
                }
            });
        }

        private void HandleSessionReconnect(JObject obj)
        {
            _reconnectUrl = (string)obj["payload"]?["session"]?["reconnect_url"];
            Plugin.Log.Warn($"TwitchEventSubClient: session_reconnect reconnect_url={_reconnectUrl}");
        }

        private void HandleRevocation(JObject obj)
        {
            string subType = (string)obj["metadata"]?["subscription_type"];
            string status = (string)obj["payload"]?["subscription"]?["status"];
            Plugin.Log.Warn($"TwitchEventSubClient: revocation type={subType} status={status}");
        }

        private async Task ReconnectAsync(string reconnectUrl)
        {
            Plugin.Log.Warn($"TwitchEventSubClient: Reconnecting to {reconnectUrl}");

            // Close old ws
            try { _cts?.Cancel(); } catch { }
            try { _ws?.Dispose(); } catch { }

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            // After reconnect, Twitch will send a new welcome with a new session_id, so we must re-subscribe.
            _sessionId = null;
            _subscribedTypes.Clear();

            await _ws.ConnectAsync(new Uri(reconnectUrl), _cts.Token);

            _isConnected = true;
            LogUtils.Debug(() => "TwitchEventSubClient: WebSocket reconnected");

            _ = Task.Run(ListenLoopAsync);
        }

        /// <summary>
        /// Creates subscription if we haven't already created it for this session.
        /// </summary>
        private async Task EnsureSubscriptionAsync(string type, string version)
        {
            if (string.IsNullOrEmpty(_sessionId))
                throw new InvalidOperationException("Cannot subscribe without session_id");

            string key = $"{type}:{version}";
            if (_subscribedTypes.Contains(key))
                return;

            var payload = BuildSubscriptionPayload(type, version);
            string body = JsonConvert.SerializeObject(payload);

            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{HelixBase}/eventsub/subscriptions", content);

            string respBody = await resp.Content.ReadAsStringAsync();

            if (resp.IsSuccessStatusCode)
            {
                _subscribedTypes.Add(key);
                LogUtils.Debug(() => $"TwitchEventSubClient: Subscribed OK -> {type} v{version}");
            }
            else
            {
                Plugin.Log.Warn($"TwitchEventSubClient: Subscribe failed -> {type} v{version} ({(int)resp.StatusCode}) {respBody}");
            }
        }

        /// <summary>
        /// IMPORTANT: Conditions differ by subscription type.
        /// - channel.chat.message requires broadcaster_user_id + user_id [Twitch docs]
        /// - channel.follow v2 requires broadcaster_user_id + moderator_user_id [Twitch docs]
        /// </summary>
        private object BuildSubscriptionPayload(string type, string version)
        {
            if (type == "channel.chat.message")
            {
                return new
                {
                    type,
                    version,
                    condition = new
                    {
                        broadcaster_user_id = _broadcasterId,
                        user_id = _botUserId
                    },
                    transport = new
                    {
                        method = "websocket",
                        session_id = _sessionId
                    }
                };
            }

            if (type == "channel.subscribe")
            {
                return new
                {
                    type,
                    version,
                    condition = new
                    {
                        broadcaster_user_id = _broadcasterId
                    },
                    transport = new
                    {
                        method = "websocket",
                        session_id = _sessionId
                    }
                };
            }

            if (type == "channel.raid")
            {
                return new
                {
                    type,
                    version,
                    condition = new
                    {
                        to_broadcaster_user_id = _broadcasterId
                    },
                    transport = new
                    {
                        method = "websocket",
                        session_id = _sessionId
                    }
                };
            }


            if (type == "channel.follow")
            {
                return new
                {
                    type,
                    version, // v2 recommended for follows
                    condition = new
                    {
                        broadcaster_user_id = _broadcasterId,
                        moderator_user_id = _botUserId
                    },
                    transport = new
                    {
                        method = "websocket",
                        session_id = _sessionId
                    }
                };
            }

            // Removed broad channel points subscription case to avoid accidental non-filtered subscriptions.

            // These are common broadcaster-scoped events (simple condition)
            // If Twitch ever requires more fields for a type, add a dedicated case like above.
            return new
            {
                type,
                version,
                condition = new
                {
                    broadcaster_user_id = _broadcasterId
                },
                transport = new
                {
                    method = "websocket",
                    session_id = _sessionId
                }
            };
        }

        private void HandleNotification(JObject obj)
        {
            string subType = (string)obj["metadata"]?["subscription_type"];
            var eventData = obj["payload"]?["event"];
            if (eventData == null)
                return;

            switch (subType)
            {
                case "channel.chat.message":
                    HandleChatMessage(eventData);
                    break;

                case "channel.follow":
                    HandleFollow(eventData);
                    break;

                case "channel.subscribe":
                    HandleSubscribe(eventData);
                    break;

                case "channel.raid":
                    HandleRaid(eventData);
                    break;

                case "channel.channel_points_custom_reward_redemption.add":
                    HandleChannelPointRedemption(eventData);
                    break;

                default:
                    // Keep quiet to avoid log spam
                    break;
            }
        }

        private void HandleChatMessage(JToken eventData)
        {
            string senderName = (string)eventData["chatter_user_name"] ?? "Unknown";

            // message is an object; still safer to cast
            var messageObj = eventData["message"] as JObject;
            string messageText = (string)messageObj?["text"] ?? string.Empty;

            string chatterId = (string)eventData["chatter_user_id"];

            // IMPORTANT: cheer can be null (JValue), so cast to JObject first
            var cheerObj = eventData["cheer"] as JObject;
            int bits = (int?)cheerObj?["bits"] ?? 0;

            var badges = eventData["badges"] as JArray;
            bool HasBadge(string setId) =>
                badges != null && badges.Any(b =>
                    string.Equals((string)b?["set_id"], setId, StringComparison.OrdinalIgnoreCase));

            var ctx = new ChatContext
            {
                SenderName = senderName,
                MessageText = messageText,
                IsBroadcaster = (!string.IsNullOrEmpty(chatterId) && chatterId == _broadcasterId) || HasBadge("broadcaster"),
                IsModerator = HasBadge("moderator"),
                IsVip = HasBadge("vip"),
                IsSubscriber = HasBadge("subscriber") || HasBadge("founder"),
                Bits = bits,
                Source = ChatSource.NativeTwitch
            };

            OnChatMessage?.Invoke(ctx);
        }




        private void HandleFollow(JToken eventData)
        {
            // v2 follow event typically includes user_name of follower
            string userName = (string)eventData["user_name"] ?? "Unknown";
            OnFollow?.Invoke(userName);
        }

        private void HandleSubscribe(JToken eventData)
        {
            string userName = (string)eventData["user_name"] ?? "Unknown";
            string tier = (string)eventData["tier"] ?? "1000";

            int tierNum = 1;
            if (tier == "2000") tierNum = 2;
            else if (tier == "3000") tierNum = 3;

            OnSubscription?.Invoke(userName, tierNum);
        }

        private void HandleRaid(JToken eventData)
        {
            string raiderName = (string)eventData["from_broadcaster_user_name"] ?? "Unknown";
            int viewers = (int?)eventData["viewers"] ?? 0;

            OnRaid?.Invoke(raiderName, viewers);
        }

        private void HandleChannelPointRedemption(JToken eventData)
        {
            try
            {
                // ADD THIS LINE - Extract redemption ID
                string redemptionId = (string)eventData["id"] ?? "";

                string userName = (string)eventData["user_name"] ?? "Unknown";
                string userId = (string)eventData["user_id"] ?? "";
                string userInput = (string)eventData["user_input"] ?? "";

                string rewardId = (string)eventData["reward"]?["id"] ?? "";
                string rewardTitle = (string)eventData["reward"]?["title"] ?? "";

                if (string.IsNullOrWhiteSpace(rewardId))
                    return;

                OnChannelPointRedeemed?.Invoke(new ChannelPointRedemption
                {
                    RedemptionId = redemptionId,  // ADD THIS LINE
                    RewardId = rewardId,
                    RewardTitle = rewardTitle,
                    UserName = userName,
                    UserId = userId,
                    UserInput = userInput
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("TwitchEventSubClient: Failed parsing channel points redemption: " + ex.Message);
            }
        }



        /// <summary>
        /// Replacement for TwitchEventClient.SendChatMessage(): send via Helix.
        /// NOTE: This requires proper scopes and parameters; ChatManager can call this.
        /// </summary>
        public async Task<bool> SendChatMessageAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            // Helix endpoint: Send Chat Message (Twitch docs)
            // We keep it simple: send as the bot user to the broadcaster's channel.
            var payload = new
            {
                broadcaster_id = _broadcasterId,
                sender_id = _botUserId,
                message = message
            };

            string body = JsonConvert.SerializeObject(payload);
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var resp = await _http.PostAsync($"{HelixBase}/chat/messages", content);
            if (resp.IsSuccessStatusCode)
                return true;

            string respBody = await resp.Content.ReadAsStringAsync();
            Plugin.Log.Warn($"TwitchEventSubClient: SendChatMessage failed ({(int)resp.StatusCode}) {respBody}");
            return false;
        }

        public void Shutdown()
        {
            LogUtils.Debug(() => "TwitchEventSubClient: Shutdown requested");
            _allowReconnect = false;
            _isConnected = false;

            try { _cts?.Cancel(); } catch { }
            try
            {
                if (_ws != null)
                {
                    try { _ws.Dispose(); } catch { }
                }
                _ws = null;
            }
            catch { }

            try { _cts?.Dispose(); } catch { }
            _cts = null;

            _sessionId = null;
            _reconnectUrl = null;
            _subscribedTypes.Clear();
        }

        // New: ensure we subscribe once per configured reward id so we only receive redemptions for rewards we own
        public async Task EnsureChannelPointSubscriptionsAsync(IEnumerable<string> rewardIds)
        {
            if (rewardIds == null) return;
            foreach (var rewardId in rewardIds)
            {
                if (string.IsNullOrWhiteSpace(rewardId)) continue;
                string key = $"channel.channel_points_custom_reward_redemption.add::1::{rewardId}";
                if (_subscribedTypes.Contains(key)) continue;

                var payload = new
                {
                    type = "channel.channel_points_custom_reward_redemption.add",
                    version = "1",
                    condition = new
                    {
                        broadcaster_user_id = _broadcasterId,
                        reward_id = rewardId
                    },
                    transport = new { method = "websocket", session_id = _sessionId }
                };

                string body = JsonConvert.SerializeObject(payload);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var resp = await _http.PostAsync($"{HelixBase}/eventsub/subscriptions", content);
                string respBody = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    _subscribedTypes.Add(key);
                    Plugin.Log.Info($"TwitchEventSubClient Subscribed to CP reward_id={rewardId}");
                }
                else
                {
                    Plugin.Log.Warn($"TwitchEventSubClient CP subscribe failed reward_id={rewardId} {(int)resp.StatusCode} {respBody}");
                }
            }
        }
    }
}