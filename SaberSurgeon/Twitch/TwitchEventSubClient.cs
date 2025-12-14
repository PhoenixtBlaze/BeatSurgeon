using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SaberSurgeon.Chat;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SaberSurgeon.Twitch
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

        // Track subscription attempts so we don't spam
        private readonly HashSet<string> _subscribedTypes = new HashSet<string>();

        // Reuse one HttpClient per instance (fine for a mod)
        private readonly HttpClient _http = new HttpClient();

        // Events (match ChatManager wiring)
        public event Action<ChatContext> OnChatMessage;
        public event Action<string> OnFollow;
        public event Action<string, int> OnSubscription;
        public event Action<string, int> OnRaid;

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

            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            _sessionId = null;
            _reconnectUrl = null;
            _subscribedTypes.Clear();

            Plugin.Log.Info($"TwitchEventSubClient: Connecting to {EventSubWsUrl}");

            await _ws.ConnectAsync(new Uri(EventSubWsUrl), _cts.Token);

            _isConnected = true;
            Plugin.Log.Info("TwitchEventSubClient: WebSocket connected");

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

                // If Twitch told us to reconnect, do it
                if (!string.IsNullOrEmpty(_reconnectUrl))
                {
                    try
                    {
                        string url = _reconnectUrl;
                        _reconnectUrl = null;
                        await ReconnectAsync(url);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error("TwitchEventSubClient: Reconnect failed: " + ex.Message);
                    }
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
                Plugin.Log.Error($"TwitchEventSubClient: Failed parsing WS message: {ex.Message}");
            }
        }

        private void HandleSessionWelcome(JObject obj)
        {
            _sessionId = (string)obj["payload"]?["session"]?["id"];
            int keepaliveTimeout = (int?)obj["payload"]?["session"]?["keepalive_timeout_seconds"] ?? 0;

            Plugin.Log.Info($"TwitchEventSubClient: session_welcome session_id={_sessionId}, keepalive_timeout_seconds={keepaliveTimeout}");

            // IMPORTANT: Twitch expects you to subscribe shortly after welcome (about 10s by default) [web docs].
            _ = Task.Run(async () =>
            {
                try
                {
                    // Pick the event types you want. These match your old backend WS events.
                    await EnsureSubscriptionAsync("channel.chat.message", "1");
                    await EnsureSubscriptionAsync("channel.follow", "2");
                    await EnsureSubscriptionAsync("channel.subscribe", "1");
                    await EnsureSubscriptionAsync("channel.raid", "1");
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
            Plugin.Log.Info("TwitchEventSubClient: WebSocket reconnected");

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
                Plugin.Log.Info($"TwitchEventSubClient: Subscribed OK -> {type} v{version}");
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

                default:
                    // Keep quiet to avoid log spam
                    break;
            }
        }

        private void HandleChatMessage(JToken eventData)
        {
            string senderName = (string)eventData["chatter_user_name"] ?? "Unknown";
            string messageText = (string)eventData["message"]?["text"] ?? string.Empty;
            string chatterId = (string)eventData["chatter_user_id"];

            var ctx = new ChatContext
            {
                SenderName = senderName,
                MessageText = messageText,
                IsBroadcaster = chatterId == _broadcasterId,
                // Role flags are not reliably present without extra Helix calls; keep false here.
                IsModerator = false,
                IsSubscriber = false,
                IsVip = false,
                Bits = 0,
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
            _isConnected = false;

            try { _cts?.Cancel(); } catch { }

            try
            {
                if (_ws != null)
                {
                    try { _ws.Dispose(); } catch { }
                    _ws = null;
                }
            }
            catch { }

            try { _cts?.Dispose(); } catch { }
            _cts = null;

            _sessionId = null;
            _reconnectUrl = null;
            _subscribedTypes.Clear();
        }
    }
}
