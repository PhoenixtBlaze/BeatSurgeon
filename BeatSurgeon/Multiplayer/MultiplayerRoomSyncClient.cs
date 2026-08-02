using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Utils;
using Newtonsoft.Json;
using UnityEngine;
using Zenject;

namespace BeatSurgeon
{
    internal sealed class MultiplayerRoomSyncClient : IInitializable, IDisposable, ITickable
    {
        private sealed class HostStateMessage
        {
            [JsonProperty("type")] internal string Type { get; set; }
            [JsonProperty("room_code")] internal string RoomCode { get; set; }
            [JsonProperty("active_command")] internal string ActiveCommand { get; set; }
            // Additive: older servers omit this; used to re-fire identical one-shots.
            [JsonProperty("active_command_token")] internal string ActiveCommandToken { get; set; }
            [JsonProperty("active_command_user")] internal string ActiveCommandUser { get; set; }
            [JsonProperty("control")] internal bool? Control { get; set; }
            // Additive: host-only cooldown snapshot; omitted by hosts that haven't sent one yet.
            [JsonProperty("cooldowns")] internal HostCooldownsMessage Cooldowns { get; set; }
        }

        private sealed class HostCooldownsMessage
        {
            [JsonProperty("per_command_enabled")] internal bool PerCommandEnabled { get; set; } = true;
            [JsonProperty("values")] internal Dictionary<string, double> Values { get; set; }
        }

        /// <summary>
        /// A host-synced effect awaiting reliable local application. Retried (with backoff) until
        /// CommandHandler reports success or a non-retryable rejection, or the room is left.
        /// </summary>
        private sealed class PendingSyncEntry
        {
            internal string Command;
            internal string SenderName;
            internal string Token;
            internal int AttemptCount;
            internal float NextAttemptRealtime;
            internal Task<CommandExecutionResult> InFlightTask;
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("MultiplayerRoomSyncClient");
        private static MultiplayerRoomSyncClient _instance;

        private const float RetryBackoffSeconds = 0.5f;
        private const int MaxRecentTokens = 32;

        private readonly List<PendingSyncEntry> _pendingSync = new List<PendingSyncEntry>();
        private readonly object _pendingLock = new object();

        // Bounded history of recently-applied one-shot tokens so a redelivered/resent copy of the
        // same host event (e.g. the host's POST-failure resend path) is not re-applied.
        private readonly HashSet<string> _recentAppliedTokens = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _recentAppliedTokenOrder = new Queue<string>();

        // Fallback dedupe for hosts that never attach a one-shot token (no nonce at all) - keeps
        // legacy sticky-style heartbeats from being reapplied every message.
        private string _lastAppliedLegacyCommandKey;
        private string _lastAppliedLegacyUserKey;

        private readonly object _connectionLock = new object();

        private const string WsBaseUrl = "wss://phoenixblaze0.duckdns.org/mp";
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private string _connectedRoomCode;

        internal static void Init() => _instance?.Initialize();
        internal static void DisposeStatic() => _instance?.Dispose();
        internal static void RefreshConnectionState() => _instance?.OnRoomMaybeChanged();

        private static bool MultiplayerEffectsEnabled =>
            PluginConfig.Instance?.MultiplayerEffectsEnabled ?? true;

        [Inject]
        public MultiplayerRoomSyncClient()
        {
            _instance = this;
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize");
            SceneHelper.Init();
            MultiplayerStateClient.Init();
            SceneHelper.MpPlusInRoomChanged += OnMpPlusInRoomChanged;
            SceneHelper.MpPlusRoomInfoChanged += OnRoomMaybeChanged;
            OnRoomMaybeChanged();
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose");
            SceneHelper.MpPlusInRoomChanged -= OnMpPlusInRoomChanged;
            SceneHelper.MpPlusRoomInfoChanged -= OnRoomMaybeChanged;
            Disconnect();
        }

        public void Tick()
        {
            List<PendingSyncEntry> snapshot;
            lock (_pendingLock)
            {
                if (_pendingSync.Count == 0) return;
                snapshot = new List<PendingSyncEntry>(_pendingSync);
            }

            float now = Time.realtimeSinceStartup;

            foreach (PendingSyncEntry entry in snapshot)
            {
                if (entry.InFlightTask != null)
                {
                    if (!entry.InFlightTask.IsCompleted) continue;

                    Task<CommandExecutionResult> completedTask = entry.InFlightTask;
                    entry.InFlightTask = null;

                    if (completedTask.IsFaulted)
                    {
                        _log.Exception(completedTask.Exception, "MultiplayerSync apply command=" + entry.Command);
                        entry.AttemptCount++;
                        entry.NextAttemptRealtime = now + RetryBackoffSeconds;
                        continue;
                    }

                    CommandExecutionResult result = completedTask.Result;
                    if (result.Executed)
                    {
                        _log.MultiplayerSync(
                            "Applied",
                            "command=" + entry.Command + " token=" + entry.Token + " attempts=" + entry.AttemptCount);
                        RemovePending(entry);
                        continue;
                    }

                    // Only retry transient local gates (typically not-in-map yet via ProcessorRejected).
                    // RankedMap is intentional client policy — do not spin forever on a ranked map.
                    // Unknown/disabled/permission/execution errors are also non-retryable.
                    bool retryable = result.Reason == CommandRejectReason.ProcessorRejected;

                    if (!retryable)
                    {
                        _log.MultiplayerSync(
                            "Dropped",
                            "command=" + entry.Command + " reason=" + result.Reason + " attempts=" + entry.AttemptCount);
                        RemovePending(entry);
                        continue;
                    }

                    entry.AttemptCount++;
                    entry.NextAttemptRealtime = now + RetryBackoffSeconds;
                    continue;
                }

                if (now < entry.NextAttemptRealtime) continue;

                if (!MultiplayerEffectsEnabled
                    || !SceneHelper.MpPlusInRoom
                    || SceneHelper.MpPlusIsHost
                    || MultiplayerStateClient.GetLocalControl())
                {
                    // No longer applicable to this client (left room, became host, or has local
                    // control) - retrying would never succeed; drop rather than retry forever.
                    _log.MultiplayerSync("Dropped", "command=" + entry.Command + " reason=NoLongerApplicable");
                    RemovePending(entry);
                    continue;
                }

                entry.InFlightTask = ApplySyncEntryAsync(entry);
            }
        }

        private static Task<CommandExecutionResult> ApplySyncEntryAsync(PendingSyncEntry entry)
        {
            string messageText = entry.Command.StartsWith("!", StringComparison.Ordinal)
                ? entry.Command
                : "!" + entry.Command;

            var ctx = new ChatContext
            {
                // Prefer host-published requester; keep RoomHost only when name was not synced.
                SenderName = string.IsNullOrWhiteSpace(entry.SenderName) ? "RoomHost" : entry.SenderName,
                MessageText = messageText,
                IsBroadcaster = true,
                IsModerator = true,
                IsSubscriber = true,
                IsChannelPoint = true,
                TriggerSource = TriggerSource.MultiplayerSync
            };

            return CommandHandler.Instance.HandleMessageAsync(ctx, TriggerSource.MultiplayerSync, CancellationToken.None);
        }

        private void RemovePending(PendingSyncEntry entry)
        {
            lock (_pendingLock)
            {
                _pendingSync.Remove(entry);
            }
        }

        private void OnMpPlusInRoomChanged(bool _)
        {
            OnRoomMaybeChanged();
        }

        private void OnRoomMaybeChanged()
        {
            if (!MultiplayerEffectsEnabled)
            {
                Disconnect();
                return;
            }

            if (!SceneHelper.MpPlusInRoom)
            {
                Disconnect();
                return;
            }

            string roomCode = (SceneHelper.MpPlusRoomCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                Disconnect();
                return;
            }

            lock (_connectionLock)
            {
                if (string.Equals(_connectedRoomCode, roomCode, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            Disconnect();
            Connect(roomCode);
        }

        private void Connect(string roomCode)
        {
            lock (_connectionLock)
            {
                _connectedRoomCode = roomCode;
                _cts = new CancellationTokenSource();
                _ws = new ClientWebSocket();
                _receiveTask = Task.Run(() => ReceiveLoopAsync(roomCode, _cts.Token), _cts.Token);
            }
        }

        private void Disconnect()
        {
            CancellationTokenSource cts;
            ClientWebSocket ws;
            Task receiveTask;

            lock (_connectionLock)
            {
                _connectedRoomCode = null;
                cts = _cts;
                ws = _ws;
                receiveTask = _receiveTask;
                _cts = null;
                _ws = null;
                _receiveTask = null;
            }

            try { cts?.Cancel(); } catch { }
            try { ws?.Dispose(); } catch { }
            try { cts?.Dispose(); } catch { }
            try { receiveTask?.Wait(300); } catch { }

            lock (_pendingLock)
            {
                _pendingSync.Clear();
                _recentAppliedTokens.Clear();
                _recentAppliedTokenOrder.Clear();
            }

            _lastAppliedLegacyCommandKey = null;
            _lastAppliedLegacyUserKey = null;

            // Leaving the room (as host or client) invalidates any host-scoped state: a fresh
            // room/host session must start with an empty duration-effect cap and no stale
            // host-cooldown overrides carried over from the previous room.
            MultiplayerHostCooldownBridge.Clear();
            MultiplayerEffectPublisher.ClearDurationTracking();

            _log.Info("Pending sync queue cleared on disconnect");
        }

        private async Task ReceiveLoopAsync(string roomCode, CancellationToken ct)
        {
            try
            {
                Uri uri = new Uri(WsBaseUrl + "?room_code=" + Uri.EscapeDataString(roomCode));
                await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
                _log.Lifecycle("Multiplayer.Connected");

                byte[] buffer = new byte[64 * 1024];
                while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    int count = 0;
                    WebSocketReceiveResult result;
                    do
                    {
                        var seg = new ArraySegment<byte>(buffer, count, buffer.Length - count);
                        result = await _ws.ReceiveAsync(seg, ct).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _log.Lifecycle("Multiplayer.Disconnected", "reason=CloseFrame");
                            return;
                        }
                        count += result.Count;
                    } while (!result.EndOfMessage);

                    string json = Encoding.UTF8.GetString(buffer, 0, count);
                    HostStateMessage msg = null;
                    try
                    {
                        msg = JsonConvert.DeserializeObject<HostStateMessage>(json);
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex, "Receive parse");
                    }

                    if (msg == null || !string.Equals(msg.Type, "host_state", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (msg.Cooldowns != null)
                    {
                        MultiplayerHostCooldownBridge.ApplyFromHost(msg.Cooldowns.PerCommandEnabled, msg.Cooldowns.Values);
                    }

                    // Heartbeats / control-only updates carry no active_command - cooldowns above
                    // were still processed, but there is no effect to apply.
                    if (string.IsNullOrWhiteSpace(msg.ActiveCommand))
                        continue;

                    // Newer servers put playable text in active_command and uniqueness in
                    // active_command_token. Older servers (or mid-rollout) may still embed #mp
                    // in active_command - StripOneShotNonce keeps both shapes working.
                    string commandKey = MultiplayerEffectPublisher.StripOneShotNonce(msg.ActiveCommand.Trim());
                    if (string.IsNullOrWhiteSpace(commandKey))
                        continue;

                    string tokenKey = string.IsNullOrWhiteSpace(msg.ActiveCommandToken)
                        ? string.Empty
                        : msg.ActiveCommandToken.Trim();
                    // Legacy fallback: if token omitted but raw still had #mp, keep raw for dedupe.
                    if (tokenKey.Length == 0
                        && !string.Equals(commandKey, msg.ActiveCommand.Trim(), StringComparison.Ordinal))
                    {
                        tokenKey = msg.ActiveCommand.Trim();
                    }

                    string userKey = string.IsNullOrWhiteSpace(msg.ActiveCommandUser)
                        ? string.Empty
                        : msg.ActiveCommandUser.Trim();

                    if (!TryAcceptForApply(commandKey, tokenKey, userKey))
                        continue;

                    EnqueuePendingSync(commandKey, msg.ActiveCommandUser, tokenKey);
                }
            }
            catch (OperationCanceledException)
            {
                _log.Lifecycle("Multiplayer.Disconnected", "reason=Cancelled");
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "ReceiveLoopAsync");
            }
        }

        /// <summary>
        /// Deduplicates by one-shot token only (per product rule) - a distinct token is always
        /// accepted even if the command/user text repeats. Falls back to last-value comparison
        /// only for the rare host that sends no token at all.
        /// </summary>
        private bool TryAcceptForApply(string commandKey, string tokenKey, string userKey)
        {
            if (tokenKey.Length > 0)
            {
                lock (_pendingLock)
                {
                    if (_recentAppliedTokens.Contains(tokenKey))
                    {
                        return false;
                    }

                    _recentAppliedTokens.Add(tokenKey);
                    _recentAppliedTokenOrder.Enqueue(tokenKey);
                    while (_recentAppliedTokenOrder.Count > MaxRecentTokens)
                    {
                        string oldest = _recentAppliedTokenOrder.Dequeue();
                        _recentAppliedTokens.Remove(oldest);
                    }
                }

                return true;
            }

            if (string.Equals(_lastAppliedLegacyCommandKey, commandKey, StringComparison.Ordinal)
                && string.Equals(_lastAppliedLegacyUserKey, userKey, StringComparison.Ordinal))
            {
                return false;
            }

            _lastAppliedLegacyCommandKey = commandKey;
            _lastAppliedLegacyUserKey = userKey;
            return true;
        }

        private void EnqueuePendingSync(string command, string senderName, string token)
        {
            if (!MultiplayerEffectsEnabled) return;

            var entry = new PendingSyncEntry
            {
                Command = command,
                SenderName = string.IsNullOrWhiteSpace(senderName) ? null : senderName.Trim(),
                Token = token,
                AttemptCount = 0,
                NextAttemptRealtime = 0f,
            };

            int pendingCount;
            lock (_pendingLock)
            {
                _pendingSync.Add(entry);
                pendingCount = _pendingSync.Count;
            }

            _log.MultiplayerSync("Queued", "command=" + command + " token=" + token + " pendingCount=" + pendingCount);
        }
    }
}
