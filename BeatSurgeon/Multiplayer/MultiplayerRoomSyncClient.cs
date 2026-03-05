using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;
using BeatSurgeon.Utils;
using Newtonsoft.Json;
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
            [JsonProperty("control")] internal bool? Control { get; set; }
        }

        private sealed class SyncMessage
        {
            internal string Type { get; set; }
            internal string TargetPlayerId { get; set; }
            internal string Command { get; set; }
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("MultiplayerRoomSyncClient");
        private static MultiplayerRoomSyncClient _instance;

        private readonly System.Collections.Generic.Queue<SyncMessage> _pendingBatch =
            new System.Collections.Generic.Queue<SyncMessage>();
        private readonly object _batchLock = new object();
        private readonly object _connectionLock = new object();

        private const string WsBaseUrl = "wss://phoenixblaze0.duckdns.org/mp";
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private string _connectedRoomCode;

        internal static void Init() => _instance?.Initialize();
        internal static void DisposeStatic() => _instance?.Dispose();

        [Inject]
        public MultiplayerRoomSyncClient()
        {
            _instance = this;
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize");
            SceneHelper.Init();
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

            lock (_batchLock)
            {
                _pendingBatch.Clear();
            }
        }

        public void Tick()
        {
            SyncMessage[] batch;
            lock (_batchLock)
            {
                if (_pendingBatch.Count == 0) return;
                batch = _pendingBatch.ToArray();
                _pendingBatch.Clear();
            }

            _log.MultiplayerSync("BatchFlush", "messageCount=" + batch.Length);
            foreach (SyncMessage msg in batch)
            {
                try
                {
                    SendSyncMessage(msg);
                    _log.MultiplayerSync("MessageSent", "type=" + msg.Type + " target=" + msg.TargetPlayerId);
                }
                catch (Exception ex)
                {
                    _log.Exception(ex, "SendSyncMessage type=" + msg.Type);
                }
            }
        }

        internal void EnqueueSync(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            var message = new SyncMessage { Type = "host_state", Command = command };
            lock (_batchLock)
            {
                _pendingBatch.Enqueue(message);
                _log.MultiplayerSync("Enqueued", "type=" + message.Type + " pendingCount=" + _pendingBatch.Count);
            }
        }

        private void OnMpPlusInRoomChanged(bool _)
        {
            OnRoomMaybeChanged();
        }

        private void OnRoomMaybeChanged()
        {
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

            lock (_batchLock)
            {
                _pendingBatch.Clear();
            }
            _log.Info("Pending sync batch cleared on disconnect");
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

                    if (string.IsNullOrWhiteSpace(msg.ActiveCommand))
                        continue;

                    EnqueueSync(msg.ActiveCommand);
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
            finally
            {
                lock (_batchLock) { _pendingBatch.Clear(); }
            }
        }

        private void SendSyncMessage(SyncMessage msg)
        {
            if (!SceneHelper.MpPlusInRoom) return;
            if (SceneHelper.MpPlusIsHost) return;
            if (MultiplayerStateClient.GetLocalControl()) return;
            if (string.IsNullOrWhiteSpace(msg.Command)) return;

            var ctx = new ChatContext
            {
                SenderName = "RoomHost",
                MessageText = msg.Command.StartsWith("!") ? msg.Command : "!" + msg.Command,
                IsBroadcaster = true,
                IsModerator = true,
                IsSubscriber = true,
                IsChannelPoint = true
            };

            _ = CommandHandler.Instance.HandleMessageAsync(ctx, CancellationToken.None);
        }
    }
}
