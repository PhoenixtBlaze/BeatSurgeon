using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Twitch;
using BeatSurgeon.Utils;
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
        private Task _receiveTask;
        private Task _commandDispatchTask;
        private TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;
        private string _channelName;
        private int _queuedCommands;

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
            if (_authManager.IsAuthenticated)
            {
                StartIrcAsync();
            }
        }

        private void OnTokensUpdatedHandler()
        {
            _log.Auth("OnTokensUpdated - restarting IRC to use fresh token");
            // Cancel the current receive loop so it reconnects with the new token
            try
            {
                _cts?.Cancel();
                StartIrcAsync();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "OnTokensUpdatedHandler.Cancel");
            }
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose - stopping IRC client");
            _authManager.OnAuthReady -= StartIrcAsync;
            _authManager.OnTokensUpdated -= OnTokensUpdatedHandler;

            try
            {
                _cts?.Cancel();
                _commandQueueSignal.Release();
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Dispose.Cancel");
            }

            try
            {
                _tcpClient?.Close();
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
            _commandDispatchTask = Task.Run(() => CommandDispatchLoopAsync(_cts.Token), _cts.Token);
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
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync("irc.chat.twitch.tv", 6667).ConfigureAwait(false);

            NetworkStream stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { NewLine = "\r\n", AutoFlush = true };

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

                    // ReadLineAsync doesn't support cancellation, so we need to wrap it with a timeout
                    var readTask = _reader.ReadLineAsync();
                    var delayTask = Task.Delay(TimeSpan.FromSeconds(90), ct);
                    var completedTask = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);

                    if (completedTask == delayTask)
                    {
                        _log.Warn("IRC read timeout (90s) - connection is unresponsive");
                        break;
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

                // Fallback to API if IRC failed or not available
                string broadcasterId = await _authManager.GetChannelUserIdAsync(ct).ConfigureAwait(false);
                string senderId = _authManager.BotUserId ?? broadcasterId;
                await _apiClient.SendChatMessageAsync(broadcasterId, senderId, message, ct).ConfigureAwait(false);
                _log.Debug("Chat message sent via API");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "SendChatMessageAsync failed for both IRC and API");
            }
        }
    }
}
