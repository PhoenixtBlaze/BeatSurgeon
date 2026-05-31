using BeatSurgeon.Utils;
using CP_SDK.Chat.Interfaces;
using System;

namespace BeatSurgeon.Chat
{
    /// <summary>
    /// Interface exposed to ChatManager — contains NO CP_SDK types so Zenject's
    /// ReflectionTypeAnalyzer never touches ChatPlexSDK_BS when scanning ChatManager.
    /// </summary>
    internal interface IChatPlexBridge
    {
        void Release();
        void BroadcastMessage(string message);
    }

    /// <summary>
    /// All CP_SDK code lives here. ChatManager never references this class directly
    /// (only via IChatPlexBridge), and this class is never registered with Zenject,
    /// so it is never scanned by ReflectionTypeAnalyzer.
    /// Instantiated only after an assembly-presence check in ChatManager confirms
    /// ChatPlexSDK_BS is loaded.
    /// </summary>
    internal sealed class ChatPlexBridge : IChatPlexBridge
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("ChatPlexBridge");

        private readonly ChatManager _owner;
        private readonly Action<IChatService, IChatMessage> _handler;

        internal ChatPlexBridge(ChatManager owner)
        {
            _owner = owner;
            _handler = OnMessageReceived;
            CP_SDK.Chat.Service.Acquire();
            CP_SDK.Chat.Service.Discrete_OnTextMessageReceived += _handler;
        }

        public void Release()
        {
            CP_SDK.Chat.Service.Discrete_OnTextMessageReceived -= _handler;
            CP_SDK.Chat.Service.Release();
        }

        public void BroadcastMessage(string message)
        {
            CP_SDK.Chat.Service.BroadcastMessage(message);
        }

        private void OnMessageReceived(IChatService service, IChatMessage message)
        {
            if (message == null || message.IsSystemMessage)
                return;

            var sender = message.Sender;
            if (sender == null)
                return;

            _owner.HandleChatPlexMessage(
                sender.UserName,
                message.Message,
                sender.IsModerator,
                sender.IsVip,
                sender.IsSubscriber,
                sender.IsBroadcaster,
                service,
                message);
        }
    }
}
