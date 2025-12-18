using IPA.Loader;
using SaberSurgeon.Twitch;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace SaberSurgeon.Chat
{
    public enum ChatBackend
    {
        None,
        NativeTwitch,
        ChatPlex
    }

    public class ChatManager : MonoBehaviour
    {
        private static ChatManager _instance;
        private static GameObject _persistentGO;

        private bool _isInitialized = false;
        private Assembly _chatPlexAssembly;
        private int _retryCount = 0;
        private const int MAX_RETRIES = 60;

        private bool _isGraphicsDeviceStable = true;

        private object _chatService;
        private MethodInfo _broadcastMessageMethod;

        private ChatBackend _activeBackend = ChatBackend.None;
        
        // new class, see below
        private TwitchEventSubClient _eventSubClient;

        public ChatBackend ActiveBackend => _activeBackend;

        public event Action<ChatContext> OnChatMessageReceived;
        public event Action<string, int> OnSubscriptionReceived;
        public event Action<string> OnFollowReceived;
        public event Action<string, int> OnRaidReceived;

        private readonly object _queueLock = new object();
        private readonly Queue<ChatContext> _pendingMessages = new Queue<ChatContext>();

        public bool ChatEnabled { get; set; } = true;

        private void UpdateGraphicsDeviceState()
        {
            bool deviceNowStable = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

            if (deviceNowStable != _isGraphicsDeviceStable)
            {
                _isGraphicsDeviceStable = deviceNowStable;
                Plugin.Log.Warn($"ChatManager: Graphics device became {(deviceNowStable ? "stable" : "UNSTABLE")}");
            }
        }



        public static ChatManager GetInstance()
        {
            if (_instance == null)
            {
                _persistentGO = new GameObject("SaberSurgeon_ChatManager_GO");
                DontDestroyOnLoad(_persistentGO);
                _instance = _persistentGO.AddComponent<ChatManager>();
                Plugin.Log.Info("ChatManager: Created new instance");
            }
            return _instance;
        }

        public void Initialize()
        {
           

            if (_isInitialized)
            {
                Plugin.Log.Warn("ChatManager: Already initialized!");
                return;
            }

            if (_isInitialized)
            {
                Plugin.Log.Warn("ChatManager: Already initialized!");
                return;
            }

            var cfg = Plugin.Settings ?? new PluginConfig();
            StartCoroutine(InitializeBackendsCoroutine(cfg));
        }

        private IEnumerator InitializeBackendsCoroutine(PluginConfig cfg)
        {

            var auth = TwitchAuthManager.Instance;
            // Wait up to ~10 seconds for refresh + identity
            int tries = 0;
            while (tries++ < 100)
            {
                bool ready = auth.IsAuthenticated
                    && !string.IsNullOrEmpty(auth.BroadcasterId)
                    && !string.IsNullOrEmpty(auth.BotUserId);

                System.Threading.Tasks.Task<bool> ensureTask = null;

                if (!ready)
                {
                    try
                    {
                        ensureTask = auth.EnsureReadyAsync();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warn("ChatManager: EnsureReadyAsync start failed: " + ex.Message);
                    }
                }

                if (ensureTask != null)
                {
                    while (!ensureTask.IsCompleted)
                        yield return null;

                    // If it faulted, treat as not-ready and retry
                    if (ensureTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                        ready = ensureTask.Result;
                    else
                        ready = false;
                }

                if (ready)
                    break;

                yield return new WaitForSeconds(0.1f);
            }

            string userAccessToken = auth.GetAccessToken();
            string clientId = auth.ClientId;
            string broadcasterId = auth.BroadcasterId;
            string botUserId = auth.BotUserId;

            // Try EventSub WebSocket first (direct to Twitch, no backend server)
            if (!string.IsNullOrEmpty(userAccessToken)
                && !string.IsNullOrEmpty(clientId)
                && !string.IsNullOrEmpty(broadcasterId)
                && !string.IsNullOrEmpty(botUserId))
            {
                Plugin.Log.Info("ChatManager: Initializing Twitch EventSub WebSocket...");

                _eventSubClient = new TwitchEventSubClient(
                    userAccessToken,
                    clientId,
                    broadcasterId,
                    botUserId
                );

                // NOTE: your TwitchEventSubClient events must match these signatures (see next section)
                _eventSubClient.OnChatMessage += ctx => EnqueueChatMessage(ctx);
                _eventSubClient.OnFollow += user => OnFollowReceived?.Invoke(user);
                _eventSubClient.OnSubscription += (user, tier) => OnSubscriptionReceived?.Invoke(user, tier);
                _eventSubClient.OnRaid += (raider, viewers) => OnRaidReceived?.Invoke(raider, viewers);

                yield return StartCoroutine(_eventSubClient.ConnectCoroutine());

                if (_eventSubClient.IsConnected)
                {
                    _activeBackend = ChatBackend.NativeTwitch;
                    _isInitialized = true;
                    cfg.BackendStatus = "EventSub WebSocket";
                    yield break;
                }

                Plugin.Log.Warn("ChatManager: EventSub WebSocket failed, falling back to ChatPlex");
            }


            // 2) Fallback to ChatPlex
            if (Plugin.Settings.AllowChatPlexFallback)
            {
                Plugin.Log.Info("ChatManager: Falling back to ChatPlex backend...");
                
                // yield return StartCoroutine(WaitForChatPlexAndInitialize());
                yield return WaitForChatPlexAndInitialize();

                if (_isInitialized)
                {
                    _activeBackend = ChatBackend.ChatPlex;
                    Plugin.Settings.BackendStatus = "ChatPlex";
                    Plugin.Log.Info("ChatManager: ChatPlex backend initialized");
                    yield break;
                }
            }

            Plugin.Log.Error("ChatManager: No chat backend available.");
        }

        private IEnumerator WaitForChatPlexAndInitialize()
        {
            Plugin.Log.Info("ChatManager: Waiting for ChatPlexSDK to fully initialize...");

            while (_retryCount < MAX_RETRIES)
            {
                _retryCount++;

                bool isReady = IsChatPlexReady();

                if (isReady)
                {
                    Plugin.Log.Info($"ChatManager: ChatPlexSDK IS READY! (attempt {_retryCount}/{MAX_RETRIES})");
                    yield return new WaitForSeconds(0.5f);
                    InitializeWhenReady();
                    yield break;
                }

                yield return new WaitForSeconds(1f);
            }

            Plugin.Log.Error($"ChatManager: TIMEOUT after {MAX_RETRIES} seconds!");
        }

        private bool IsChatPlexReady()
        {
            try
            {
                var chatPlexPlugin = PluginManager.GetPluginFromId("ChatPlexSDK_BS");
                if (chatPlexPlugin == null)
                    return false;

                if (_chatPlexAssembly == null)
                {
                    _chatPlexAssembly = Assembly.Load("ChatPlexSDK_BS");
                }

                if (_chatPlexAssembly == null)
                    return false;

                var serviceType = _chatPlexAssembly.GetTypes()
                    .FirstOrDefault(t => t.FullName == "CP_SDK.Chat.Service");

                if (serviceType == null)
                    return false;

                var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                return methods.Any(m => m.Name == "add_Discrete_OnTextMessageReceived");
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug($"ChatManager: Check failed: {ex.Message}");
                return false;
            }
        }

        

        private void InitializeWhenReady()
        {
            if (_isInitialized)
                return;

            try
            {
                Plugin.Log.Info("ChatManager: NOW INITIALIZING WITH CHATPLEX!");

                var serviceType = _chatPlexAssembly.GetTypes()
                    .FirstOrDefault(t => t.FullName == "CP_SDK.Chat.Service");

                if (serviceType == null)
                {
                    Plugin.Log.Error("ChatManager: Service type not found!");
                    return;
                }

                // Get Multiplexer for receiving AND sending messages
                var multiplexerProp = serviceType.GetProperty("Multiplexer", BindingFlags.Public | BindingFlags.Static);
                if (multiplexerProp != null)
                {
                    _chatService = multiplexerProp.GetValue(null);
                    Plugin.Log.Info("ChatManager: Got Multiplexer service reference");
                }


                var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                

                // Find BroadcastMessage method on Service
                var broadcastMethod = methods.FirstOrDefault(m => m.Name == "BroadcastMessage");
                if (broadcastMethod != null)
                {
                    Plugin.Log.Info("ChatManager: Found BroadcastMessage method on Service");
                    _broadcastMessageMethod = broadcastMethod;
                }

                // Subscribe to add_Discrete_OnTextMessageReceived 
                var addTextMessageMethod = methods.FirstOrDefault(m => m.Name == "add_Discrete_OnTextMessageReceived");
                if (addTextMessageMethod != null)
                {
                    try
                    {
                        var parameters = addTextMessageMethod.GetParameters();
                        if (parameters.Length > 0)
                        {
                            var delegateType = parameters[0].ParameterType;
                            var handlerMethod = GetType().GetMethod("HandleChatMessage", BindingFlags.NonPublic | BindingFlags.Instance);
                            var handler = Delegate.CreateDelegate(delegateType, this, handlerMethod, false);

                            if (handler != null)
                            {
                                addTextMessageMethod.Invoke(null, new object[] { handler });
                                Plugin.Log.Info("ChatManager: Successfully subscribed to OnTextMessageReceived");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"ChatManager: Failed to subscribe to messages: {ex.Message}");
                    }
                }

                // Subscribe to OnLoadingStateChanged
                var addLoadingStateMethod = methods.FirstOrDefault(m => m.Name == "add_OnLoadingStateChanged");
                if (addLoadingStateMethod != null)
                {
                    try
                    {
                        Action<bool> handler = (isLoading) => HandleLoadingStateChanged(isLoading);
                        addLoadingStateMethod.Invoke(null, new object[] { handler });
                        Plugin.Log.Info("ChatManager: Successfully subscribed to OnLoadingStateChanged");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"ChatManager: Failed to subscribe to loading state: {ex.Message}");
                    }
                }

                _isInitialized = true;
                Plugin.Log.Info("ChatManager: FULLY INITIALIZED AND READY!");

                if (_broadcastMessageMethod != null)
                {
                    Plugin.Log.Info("ChatManager: Message sending is ENABLED via ChatPlexSDK");
                }
                else
                {
                    Plugin.Log.Warn("ChatManager: Message sending NOT available");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"ChatManager: Critical error: {ex.Message}");
                Plugin.Log.Error($"  Stack: {ex.StackTrace}");
            }
        }

        private void HookNativeEvents(TwitchEventSubClient client)
        {
            client.OnChatMessage += ctx =>
            {
                Plugin.Log.Info($"NATIVE CHAT: {ctx.SenderName}: {ctx.MessageText}");
                EnqueueChatMessage(ctx);
            };

            client.OnFollow += user =>
            {
                Plugin.Log.Info($"Follow: {user}");
                OnFollowReceived?.Invoke(user);
            };

            client.OnSubscription += (user, tier) =>
            {
                Plugin.Log.Info($"Sub: {user} Tier={tier}");
                OnSubscriptionReceived?.Invoke(user, tier);
            };

            client.OnRaid += (raider, viewers) =>
            {
                Plugin.Log.Info($"Raid: {raider} ({viewers} viewers)");
                OnRaidReceived?.Invoke(raider, viewers);
            };
        }




        private void DispatchChatMessage(ChatContext ctx)
        {
            if (ctx == null || string.IsNullOrEmpty(ctx.MessageText))
                return;

            // Notify overlay / any other listeners
            OnChatMessageReceived?.Invoke(ctx);

            // Existing command logic
            if (ctx.MessageText.StartsWith("!") && !ctx.MessageText.StartsWith("!!"))
                CommandHandler.Instance.ProcessCommand(ctx.MessageText, ctx);
        }




        /// <summary>
        /// Send a message to Twitch chat using ChatPlexSDK
        /// </summary>
        public void SendChatMessage(string message)
        {
            try
            {
                if (!_isInitialized)
                {
                    Plugin.Log.Warn("ChatManager: Cannot send message - not initialized");
                    return;
                }

                // Prefer native Twitch if it's the active backend
                if (_activeBackend == ChatBackend.NativeTwitch && _eventSubClient != null && _eventSubClient.IsConnected)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        bool ok = await _eventSubClient.SendChatMessageAsync(message);
                        Plugin.Log.Info("ChatManager: Sent via Helix chat API ok=" + ok);
                    });
                    return;
                }


                // Fallback → ChatPlex BroadcastMessage
                if (_broadcastMessageMethod == null)
                {
                    Plugin.Log.Warn("ChatManager: Cannot send message - BroadcastMessage method not available");
                    return;
                }

                _broadcastMessageMethod.Invoke(null, new object[] { message });
                Plugin.Log.Info($"ChatManager: Sent to ChatPlex: {message}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"ChatManager: Error sending message: {ex.Message}");
            }
        }

        private void HandleChatMessage(object service, object message)
        {
            try
            {
                if (message == null)
                {
                    Plugin.Log.Warn("ChatManager: Received null message");
                    return;
                }

                // --- Basic sender name ---
                var sender = GetPropertyValue(message, "Sender") ??
                             GetPropertyValue(message, "User") ??
                             GetPropertyValue(message, "Author");

                string senderName = "Unknown";
                if (sender != null)
                {
                    var senderNameObj = GetPropertyValue(sender, "DisplayName") ??
                                        GetPropertyValue(sender, "UserName") ??
                                        GetPropertyValue(sender, "Name");
                    senderName = senderNameObj?.ToString() ?? "Unknown";
                }

                // --- Message text ---
                var messageTextObj = GetPropertyValue(message, "Message") ??
                                     GetPropertyValue(message, "Text") ??
                                     GetPropertyValue(message, "Content");
                string messageText = messageTextObj?.ToString() ?? "";

                // --- Helpers for typed properties ---
                bool GetBool(object obj, params string[] names)
                {
                    if (obj == null) return false;
                    foreach (var n in names)
                    {
                        var v = GetPropertyValue(obj, n);
                        if (v is bool b) return b;
                    }
                    return false;
                }

                int GetInt(object obj, params string[] names)
                {
                    if (obj == null) return 0;
                    foreach (var n in names)
                    {
                        var v = GetPropertyValue(obj, n);
                        if (v is int i) return i;
                        if (v is long l) return (int)l;
                    }
                    return 0;
                }

                // --- Build ChatContext with roles + bits ---
                var ctx = new ChatContext
                {
                    SenderName = senderName,
                    MessageText = messageText,
                    RawService = service,
                    RawMessage = message,

                    IsModerator = GetBool(sender, "IsModerator", "Moderator", "IsMod"),
                    IsVip = GetBool(sender, "IsVip", "VIP"),
                    IsSubscriber = GetBool(sender, "IsSubscriber", "Subscriber", "IsSub"),
                    IsBroadcaster = GetBool(sender, "IsBroadcaster", "Broadcaster"),

                    Bits = GetInt(message, "Bits", "BitsAmount", "CheerAmount"),
                    Source = ChatSource.ChatPlex
                };

                // Minimal log – good for debugging, not spammy
                Plugin.Log.Info($"CHAT MESSAGE RECEIVED: {ctx.SenderName} (Mod={ctx.IsModerator}, VIP={ctx.IsVip}, Sub={ctx.IsSubscriber}, Bits={ctx.Bits})");

                // Pass through unified dispatcher used by both backends
                DispatchChatMessage(ctx);

                // Non-command messages: no extra work here; follows/subs/channel points handled by Streamer.bot.
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"ChatManager: Error handling message: {ex.Message}");
            }
        }


        private void HandleLoadingStateChanged(bool isLoading)
        {
            try
            {
                string state = isLoading ? "LOADING" : "READY";
                Plugin.Log.Debug($"ChatManager: ChatPlex loading state changed: {state}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"ChatManager: Error in HandleLoadingStateChanged: {ex.Message}");
            }
        }

        private object GetPropertyValue(object obj, string propertyName)
        {
            try
            {
                if (obj == null)
                    return null;

                var prop = obj.GetType().GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                return prop?.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }



        private void EnqueueChatMessage(ChatContext ctx)
        {
            if (ctx == null)
                return;

            lock (_queueLock)
            {
                _pendingMessages.Enqueue(ctx);
            }
        }

        private void Update()
        {
            UpdateGraphicsDeviceState();

            // Process queued messages on the main thread
            while (true)
            {
                ChatContext ctx = null;
                lock (_queueLock)
                {
                    if (_pendingMessages.Count == 0)
                        break;
                    ctx = _pendingMessages.Dequeue();
                }

                if (ctx != null && _isGraphicsDeviceStable)
                {
                    try
                    {
                        DispatchChatMessage(ctx);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"ChatManager: Error dispatching message: {ex.Message}");
                    }
                }
                else if (ctx != null && !_isGraphicsDeviceStable)
                {
                    // Re-queue for next frame
                    lock (_queueLock)
                    {
                        _pendingMessages.Enqueue(ctx);
                    }
                }
            }
        }




        public void Shutdown()
        {
            Plugin.Log.Info("ChatManager: Shutting down...");
            StopAllCoroutines();

            try
            {
                _eventSubClient?.Shutdown();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("ChatManager: Error shutting down EventSub client: " + ex.Message);
            }

            _eventSubClient = null;
            _activeBackend = ChatBackend.None;

            _isInitialized = false;
            _chatService = null;
            _broadcastMessageMethod = null;
        }

    }
}