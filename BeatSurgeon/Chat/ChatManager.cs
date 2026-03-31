using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;
using CP_SDK.Chat.Interfaces;
using Zenject;

namespace BeatSurgeon.Chat
{
    internal enum ChatBackend
    {
        None,
        Irc,
        ChatPlex
    }

    internal sealed class ChatManager : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("ChatManager");
        private static ChatManager _instance;

        private readonly TwitchAuthManager _authManager;
        private readonly TwitchApiClient _apiClient;
        private readonly CommandHandler _commandHandler;
        private readonly ConcurrentQueue<ChatContext> _commandQueue = new ConcurrentQueue<ChatContext>();
        private readonly SemaphoreSlim _commandQueueSignal;
        private readonly TimeSpan _commandDispatchInterval;
        private readonly int _maxQueuedCommands;

        private CancellationTokenSource _cts;
        private CancellationTokenSource _dispatchCts;  // Separate from IRC - always running
        private Task _receiveTask;
        private Task _commandDispatchTask;
        private TcpClient _tcpClient;
        private SslStream _sslStream;
        private StreamReader _reader;
        private StreamWriter _writer;
        private string _channelName;
        private int _queuedCommands;
        private bool _chatPlexAcquired;

        internal event Action<ChatContext> OnChatMessageReceived;
        internal event Action<string, int> OnSubscriptionReceived;
        internal event Action<string> OnFollowReceived;
        internal event Action<string, int> OnRaidReceived;

        internal ChatBackend ActiveBackend { get; private set; } = ChatBackend.None;
        internal bool ChatEnabled { get; set; } = true;

        internal static ChatManager GetInstance() =>
            _instance ?? (_instance = new ChatManager(
                TwitchAuthManager.Instance,
                TwitchApiClient.Instance,
                CommandHandler.Instance));

        [Inject]
        public ChatManager(TwitchAuthManager authManager, TwitchApiClient apiClient, CommandHandler commandHandler)
        {
            _instance = this;
            _authManager = authManager;
            _apiClient = apiClient;
            _commandHandler = commandHandler;

            int maxPerSecond = Math.Max(1, PluginConfig.Instance?.MaxCommandsPerSecond ?? 3);
            _commandDispatchInterval = TimeSpan.FromSeconds(1d / maxPerSecond);
            _maxQueuedCommands = Math.Max(32, maxPerSecond * 30);
            _commandQueueSignal = new SemaphoreSlim(0);
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize - subscribing to auth events");
            _authManager.OnAuthReady += StartIrcAsync;
            _authManager.OnTokensUpdated += OnTokensUpdatedHandler;

            // Start the command dispatch loop immediately so ChatPlex commands are
            // processed even when the user is not authenticated with BeatSurgeon's backend.
            _dispatchCts = new CancellationTokenSource();
            _commandDispatchTask = Task.Run(() => CommandDispatchLoopAsync(_dispatchCts.Token), _dispatchCts.Token);

            // Always try to hook ChatPlex so non-authenticated users can still receive
            // commands and send replies via BSPlus chat integration.
            TryAcquireChatPlex();

            if (_authManager.IsAuthenticated)
            {
                StartIrcAsync();
            }
        }

        private async void OnTokensUpdatedHandler()
        {
            _log.Auth("OnTokensUpdated - restarting IRC to use fresh token");

            try
            {
                // Request cancellation of the current receive loop if any
                CancellationTokenSource oldCts = null;
                Task oldReceiveTask = null;
                lock (this)
                {
                    oldCts = _cts;
                    oldReceiveTask = _receiveTask;
                }

                try
                {
                    oldCts?.Cancel();
                }
                catch (Exception) { }

                // If there was a running receive task, wait for it to finish to avoid
                // racing disposal of the underlying streams with a new connection attempt.
                if (oldReceiveTask != null)
                {
                    try
                    {
                        await oldReceiveTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex, "OnTokensUpdatedHandler.awaitReceiveTask");
                    }
                }

                // Start a fresh IRC connect loop
                StartIrcAsync();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnTokensUpdatedHandler");
            }
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose - stopping IRC client");
            _authManager.OnAuthReady -= StartIrcAsync;
            _authManager.OnTokensUpdated -= OnTokensUpdatedHandler;

            if (_chatPlexAcquired)
            {
                try
                {
                    CP_SDK.Chat.Service.Discrete_OnTextMessageReceived -= OnChatPlexMessageReceived;
                    CP_SDK.Chat.Service.Release();
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "Dispose.ChatPlexRelease");
                }
                _chatPlexAcquired = false;
            }

            try
            {
                _cts?.Cancel();
                _dispatchCts?.Cancel();
                _commandQueueSignal.Release();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Dispose.Cancel");
            }

            try
            {
                _tcpClient?.Close();
                _sslStream?.Dispose();
                _reader?.Dispose();
                _writer?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Dispose.Cleanup");
            }

            _log.Info("IRC client stopped");
        }

        internal void Shutdown() => Dispose();

        private void StartIrcAsync()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ConnectLoopAsync(_cts.Token), _cts.Token);
            // Note: _commandDispatchTask is started once in Initialize() and runs independently.
        }

        private void TryAcquireChatPlex()
        {
            if (!(PluginConfig.Instance?.AllowChatPlexFallback ?? true))
            {
                _log.Info("ChatPlex fallback disabled in settings - skipping");
                return;
            }

            try
            {
                CP_SDK.Chat.Service.Acquire();
                CP_SDK.Chat.Service.Discrete_OnTextMessageReceived += OnChatPlexMessageReceived;
                _chatPlexAcquired = true;

                // If not yet authenticated with BeatSurgeon backend, set ChatPlex as active backend
                if (!_authManager.IsAuthenticated)
                    ActiveBackend = ChatBackend.ChatPlex;

                _log.Info("ChatPlex acquired - will be used when IRC is not connected");
            }
            catch (Exception ex)
            {
                _log.Warn("Failed to acquire ChatPlex: " + ex.Message);
            }
        }

        private void OnChatPlexMessageReceived(IChatService service, IChatMessage message)
        {
            // Only process via ChatPlex when IRC is not the active backend
            if (ActiveBackend == ChatBackend.Irc)
                return;

            if (!ChatEnabled || message == null || message.IsSystemMessage)
                return;

            var sender = message.Sender;
            if (sender == null)
                return;

            var ctx = new ChatContext
            {
                SenderName = sender.UserName,
                MessageText = message.Message,
                IsModerator = sender.IsModerator,
                IsSubscriber = sender.IsSubscriber,
                IsVip = sender.IsVip,
                IsBroadcaster = sender.IsBroadcaster,
                Source = ChatSource.ChatPlex,
                RawService = service,
                RawMessage = message
            };

            _log.Info("ChatPlex.MessageReceived user=" + ctx.Username + " cmd=" + ctx.Command);
            OnChatMessageReceived?.Invoke(ctx);
            _ = DispatchMessageAsync(ctx, CancellationToken.None);
        }

        private async Task ConnectLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_authManager.IsReauthRequired)
                    {
                        _log.TwitchState("IRC.Paused", "reason=ReauthRequired");
                        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    }

                    await ConnectAsync(ct).ConfigureAwait(false);
                    await ReceiveLoopAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "IRC connect/receive loop");
                }

                // IRC dropped - fall back to ChatPlex while we wait to reconnect
                if (ActiveBackend == ChatBackend.Irc)
                {
                    ActiveBackend = _chatPlexAcquired ? ChatBackend.ChatPlex : ChatBackend.None;
                    if (ActiveBackend == ChatBackend.ChatPlex)
                        _log.Info("IRC disconnected - falling back to ChatPlex for incoming messages");
                }

                if (!ct.IsCancellationRequested)
                {
                    _log.TwitchState("IRC.Disconnected", "reason=ReconnectDelay");
                    await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                }
            }
        }

        private async Task ConnectAsync(CancellationToken ct)
        {
            string token = await _authManager.GetAccessTokenAsync(ct).ConfigureAwait(false);
            _channelName = string.IsNullOrWhiteSpace(_authManager.BroadcasterLogin)
                ? PluginConfig.Instance.CachedBroadcasterLogin
                : _authManager.BroadcasterLogin;

            if (string.IsNullOrWhiteSpace(_channelName))
            {
                throw new InvalidOperationException("Cannot connect IRC: broadcaster login is unknown.");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Cannot connect IRC: OAuth token is empty. Check TwitchAuthManager authentication status.");
            }

            _log.TwitchState("IRC.Connecting", "channel=" + _channelName);

            _tcpClient?.Close();
            _sslStream?.Dispose();
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync("irc.chat.twitch.tv", 6697).ConfigureAwait(false);

            NetworkStream netStream = _tcpClient.GetStream();
            _sslStream = new SslStream(netStream, leaveInnerStreamOpen: false);
            await _sslStream.AuthenticateAsClientAsync("irc.chat.twitch.tv").ConfigureAwait(false);
            _reader = new StreamReader(_sslStream);
            _writer = new StreamWriter(_sslStream) { NewLine = "\r\n", AutoFlush = true };

            await _writer.WriteLineAsync("PASS oauth:" + token).ConfigureAwait(false);
            await _writer.WriteLineAsync("NICK " + _channelName).ConfigureAwait(false);
            await _writer.WriteLineAsync("CAP REQ :twitch.tv/tags twitch.tv/commands").ConfigureAwait(false);
            await _writer.WriteLineAsync("JOIN #" + _channelName).ConfigureAwait(false);

            ActiveBackend = ChatBackend.Irc;
            _log.TwitchState("IRC.Connected", "channel=" + _channelName);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            bool authenticationComplete = false;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_tcpClient == null || !_tcpClient.Connected)
                    {
                        _log.Debug("IRC connection lost - breaking receive loop");
                        break;
                    }

                    if (_reader == null)
                    {
                        _log.Debug("IRC stream reader is null - breaking receive loop");
                        break;
                    }

                    // ReadLineAsync doesn't support cancellation; wrap with a 60s idle timeout.
                    // If no data arrives, we send a proactive PING to verify the connection is alive.
                    var readTask = _reader.ReadLineAsync();
                    var idleTask = Task.Delay(TimeSpan.FromSeconds(60), ct);
                    var completedTask = await Task.WhenAny(readTask, idleTask).ConfigureAwait(false);

                    if (completedTask == idleTask)
                    {
                        // No data for 60s - send a proactive PING to check if the server is alive.
                        try
                        {
                            if (_writer != null)
                            {
                                await _writer.WriteLineAsync("PING :tmi.twitch.tv").ConfigureAwait(false);
                                _log.Debug("Sent proactive PING - waiting 30s for server response");
                            }
                        }
                        catch (Exception pingEx)
                        {
                            _log.Exception(pingEx, "Failed to send proactive PING");
                            break;
                        }

                        // The PONG (or next IRC message) will arrive on the existing readTask.
                        // Give the server up to 30s to respond before treating it as dead.
                        await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(30), ct)).ConfigureAwait(false);
                        if (!readTask.IsCompleted)
                        {
                            _log.Warn("IRC PONG not received within 30s after PING - reconnecting");
                            break;
                        }
                        // Server responded - fall through to process the line normally.
                    }

                    string rawLine = await readTask.ConfigureAwait(false);
                    if (rawLine == null)
                    {
                        _log.Debug("IRC stream ended (ReadLineAsync returned null)");
                        break;
                    }

                    // CRITICAL: Check for IRC authentication failure responses BEFORE parsing as PRIVMSG
                    // Twitch IRC may send these early in the connection sequence
                    if (rawLine.Contains(":Login unsuccessful") || rawLine.Contains("Login unsuccessful"))
                    {
                        _authManager.MarkReauthRequired("IRC login unsuccessful");
                        _log.Exception(new InvalidOperationException(
                            "Twitch IRC authentication failed. The OAuth token may be invalid, expired, or lack required chat scopes (chat:read, chat:edit). Response: " + rawLine),
                            "IRC auth failure");
                        throw new InvalidOperationException("IRC authentication rejected by Twitch: " + rawLine);
                    }

                    if (rawLine.StartsWith("PING ", StringComparison.Ordinal))
                    {
                        try
                        {
                            if (_writer != null)
                            {
                                await _writer.WriteLineAsync(rawLine.Replace("PING", "PONG")).ConfigureAwait(false);
                                _log.Debug("Sent PONG response");
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.Exception(ex, "Failed to send PONG response");
                            throw;
                        }
                        continue;
                    }

                    // Track successful authentication once we can handle PRIVMSG
                    if (!authenticationComplete && rawLine.Contains(":tmi.twitch.tv") && rawLine.Contains("366"))
                    {
                        // 366 is RPL_ENDOFNAMES, indicates successful channel join = authenticated
                        authenticationComplete = true;
                        _log.Info("IRC.AuthenticationSucceeded");
                    }

                    if (!TryParsePrivMsg(rawLine, out ChatContext ctx))
                    {
                        continue;
                    }

                    _log.Info("IRC.MessageReceived user=" + ctx.Username + " cmd=" + ctx.Command + " msg=" + ctx.MessageText);
                    OnChatMessageReceived?.Invoke(ctx);
                    await DispatchMessageAsync(ctx, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _log.Debug("IRC receive loop cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "IRC receive loop error");
                    break;
                }
            }
        }

        private Task DispatchMessageAsync(ChatContext ctx, CancellationToken ct)
        {
            if (!ChatEnabled || ctx == null)
            {
                return Task.CompletedTask;
            }

            if (!ChatContext.TryExtractFirstCommandToken(ctx.MessageText, out _))
            {
                return Task.CompletedTask;
            }

            int queued = Interlocked.Increment(ref _queuedCommands);
            if (queued > _maxQueuedCommands)
            {
                Interlocked.Decrement(ref _queuedCommands);
                _log.Warn("CommandQueueFull - rejecting command from user=" + ctx.Username + " queueSize=" + queued);
                return Task.CompletedTask;
            }

            _commandQueue.Enqueue(ctx);
            try
            {
                _commandQueueSignal.Release();
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Decrement(ref _queuedCommands);
            }

            return Task.CompletedTask;
        }

        private async Task CommandDispatchLoopAsync(CancellationToken ct)
        {
            DateTime nextAllowedAt = DateTime.UtcNow;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _commandQueueSignal.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                while (_commandQueue.TryDequeue(out ChatContext queuedCtx))
                {
                    Interlocked.Decrement(ref _queuedCommands);

                    if (!ChatEnabled || queuedCtx == null)
                    {
                        continue;
                    }

                    TimeSpan wait = nextAllowedAt - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        try
                        {
                            await Task.Delay(wait, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }

                    try
                    {
                        await _commandHandler.HandleMessageAsync(queuedCtx, TriggerSource.Chat, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex, "CommandDispatchLoopAsync");
                    }

                    nextAllowedAt = DateTime.UtcNow + _commandDispatchInterval;
                }
            }
        }

        private static bool TryParsePrivMsg(string raw, out ChatContext ctx)
        {
            ctx = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string tags = string.Empty;
            string working = raw;

            if (working.StartsWith("@", StringComparison.Ordinal))
            {
                int firstSpace = working.IndexOf(' ');
                if (firstSpace <= 0) return false;
                tags = working.Substring(1, firstSpace - 1);
                working = working.Substring(firstSpace + 1);
            }

            int privMsgIndex = working.IndexOf(" PRIVMSG ", StringComparison.Ordinal);
            if (privMsgIndex < 0) return false;

            int nickStart = working.StartsWith(":") ? 1 : 0;
            int nickEnd = working.IndexOf('!');
            if (nickEnd <= nickStart) return false;

            string username = working.Substring(nickStart, nickEnd - nickStart);
            int msgSplit = working.IndexOf(" :", StringComparison.Ordinal);
            if (msgSplit < 0) return false;
            string message = working.Substring(msgSplit + 2);
            if (string.IsNullOrWhiteSpace(message) || !ChatContext.TryExtractFirstCommandToken(message, out _)) return false;

            bool isMod = tags.Contains("mod=1");
            bool isSub = tags.Contains("subscriber=1");
            bool isVip = tags.Contains("vip=1");
            bool isBroadcaster = tags.Contains("badges=broadcaster/");

            ctx = new ChatContext
            {
                SenderName = username,
                MessageText = message,
                IsModerator = isMod,
                IsSubscriber = isSub,
                IsVip = isVip,
                IsBroadcaster = isBroadcaster,
                Source = ChatSource.NativeTwitch
            };

            return true;
        }

        internal void SendChatMessage(string message)
        {
            _ = SendChatMessageAsync(message, CancellationToken.None);
        }

        private async Task SendChatMessageAsync(string message, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                // Try to send via IRC if connection is available
                if (_writer != null && _tcpClient != null && _tcpClient.Connected)
                {
                    try
                    {
                        await _writer.WriteLineAsync("PRIVMSG #" + _channelName + " :" + message).ConfigureAwait(false);
                        _log.Debug("Chat message sent via IRC");
                        return;
                    }
                    catch (Exception ircEx)
                    {
                        _log.Warn("IRC send failed, falling back to API: " + ircEx.Message);
                        // Continue to API fallback
                    }
                }

                // ChatPlex broadcast fallback (works without BeatSurgeon auth)
                if (_chatPlexAcquired)
                {
                    try
                    {
                        CP_SDK.Chat.Service.BroadcastMessage(message);
                        _log.Debug("Chat message sent via ChatPlex");
                        return;
                    }
                    catch (Exception cpEx)
                    {
                        _log.Warn("ChatPlex send failed, trying API: " + cpEx.Message);
                    }
                }

                // Final fallback: Twitch API (requires BeatSurgeon authentication)
                if (_authManager.IsAuthenticated)
                {
                    string broadcasterId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
                    string senderId = _authManager.BotUserId ?? broadcasterId;
                    await _apiClient.SendChatMessageAsync(broadcasterId, senderId, message, ct).ConfigureAwait(false);
                    _log.Debug("Chat message sent via API");
                }
                else
                {
                    _log.Warn("SendChatMessageAsync: no IRC, no ChatPlex, and not authenticated - message dropped");
                }
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "SendChatMessageAsync failed");
            }
        }
    }
}
