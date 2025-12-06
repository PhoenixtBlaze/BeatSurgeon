using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using IPA.Loader;
using UnityEngine;

namespace SaberSurgeon.Chat
{
    public class ChatManager : MonoBehaviour
    {
        private static ChatManager _instance;
        private static GameObject _persistentGO;

        private bool _isInitialized = false;
        private Assembly _chatPlexAssembly;
        private int _retryCount = 0;
        private const int MAX_RETRIES = 60;

        private object _chatService;
        private MethodInfo _broadcastMessageMethod;

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

            Plugin.Log.Info("ChatManager: Starting initialization sequence...");
            StartCoroutine(WaitForChatPlexAndInitialize());
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

                if (_broadcastMessageMethod == null)
                {
                    Plugin.Log.Warn("ChatManager: Cannot send message - BroadcastMessage method not available");
                    return;
                }

                // Call CP_SDK.Chat.Service.BroadcastMessage(string message)
                _broadcastMessageMethod.Invoke(null, new object[] { message });
                Plugin.Log.Info($"ChatManager: Sent to chat: {message}");
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

                    // Common ChatPlex/Twitch property names for bits
                    Bits = GetInt(message, "Bits", "BitsAmount", "CheerAmount")
                };

                // Minimal log – good for debugging, not spammy
                Plugin.Log.Info($"CHAT MESSAGE RECEIVED: {ctx.SenderName} (Mod={ctx.IsModerator}, VIP={ctx.IsVip}, Sub={ctx.IsSubscriber}, Bits={ctx.Bits})");

                // Commands handled by SaberSurgeon
                if (ctx.MessageText.StartsWith("!") && !ctx.MessageText.StartsWith("!!"))
                {
                    CommandHandler.Instance.ProcessCommand(ctx.MessageText, ctx);
                }

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

        public void Shutdown()
        {
            Plugin.Log.Info("ChatManager: Shutting down...");
            _isInitialized = false;
            _chatService = null;
            _broadcastMessageMethod = null;
        }
    }
}