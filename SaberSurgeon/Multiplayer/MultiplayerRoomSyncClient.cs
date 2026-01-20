using Newtonsoft.Json;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatSurgeon
{
    internal static class MultiplayerRoomSyncClient
    {
        // Must match server WS endpoint path and domain
        private const string WsBaseUrl = "wss://phoenixblaze0.duckdns.org/mp";

        private static readonly object _lock = new object();
        private static ClientWebSocket _ws;
        private static CancellationTokenSource _cts;
        private static Task _recvTask;

        private static string _connectedRoomCode;
        private static readonly ConcurrentQueue<string> _pendingCommands = new ConcurrentQueue<string>();
        private static Coroutine _pumpCoroutine;

        private class HostStateMessage
        {
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("room_code")] public string RoomCode { get; set; }
            [JsonProperty("active_command")] public string ActiveCommand { get; set; }
            [JsonProperty("control")] public bool? Control { get; set; }
        }

        public static void Init()
        {
            SceneHelper.Init();
            SceneHelper.MpPlusInRoomChanged += _ => OnRoomMaybeChanged();
            SceneHelper.MpPlusRoomInfoChanged += OnRoomMaybeChanged;

            OnRoomMaybeChanged();
            if (_pumpCoroutine == null)
                _pumpCoroutine = CoroutineHost.Instance.StartCoroutine(PumpMainThread());
        }

        public static void Dispose()
        {
            lock (_lock)
            {
                _connectedRoomCode = null;
            }

            try { _cts?.Cancel(); } catch { }
            try { _ws?.Dispose(); } catch { }
            _ws = null;
            _cts = null;
            _recvTask = null;

            if (_pumpCoroutine != null)
            {
                CoroutineHost.Instance.StopCoroutine(_pumpCoroutine);
                _pumpCoroutine = null;
            }
        }

        private static void OnRoomMaybeChanged()
        {
            // Only connect if actually in a room with a code
            if (!SceneHelper.MpPlusInRoom) { Disconnect(); return; }

            var room = (SceneHelper.MpPlusRoomCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(room)) { Disconnect(); return; }

            lock (_lock)
            {
                if (string.Equals(_connectedRoomCode, room, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            Disconnect();
            Connect(room);
        }

        private static void Connect(string roomCode)
        {
            lock (_lock)
            {
                _connectedRoomCode = roomCode;
                _cts = new CancellationTokenSource();
                _ws = new ClientWebSocket();
                _recvTask = Task.Run(() => ReceiveLoopAsync(roomCode, _cts.Token));
            }
        }

        private static void Disconnect()
        {
            lock (_lock)
            {
                _connectedRoomCode = null;
                try { _cts?.Cancel(); } catch { }
                _cts = null;

                try { _ws?.Dispose(); } catch { }
                _ws = null;
                _recvTask = null;
            }
        }

        private static async Task ReceiveLoopAsync(string roomCode, CancellationToken ct)
        {
            try
            {
                var uri = new Uri($"{WsBaseUrl}?room_code={Uri.EscapeDataString(roomCode)}");

                var cid = PluginConfig.Instance?.MpClientId;
                if (!string.IsNullOrWhiteSpace(cid))
                    _ws.Options.SetRequestHeader("X-MP-Client-Id", cid);

                // Optional but useful for server logging/identity fallback:
                _ws.Options.SetRequestHeader("X-Client-App", "BeatSurgeon-BS");

                await _ws.ConnectAsync(uri, ct);

                var buffer = new byte[64 * 1024];

                while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    int count = 0;
                    WebSocketReceiveResult result;

                    do
                    {
                        var seg = new ArraySegment<byte>(buffer, count, buffer.Length - count);
                        result = await _ws.ReceiveAsync(seg, ct);

                        if (result.MessageType == WebSocketMessageType.Close)
                            return;

                        count += result.Count;
                        if (count >= buffer.Length)
                            throw new Exception("WS message too large");
                    }
                    while (!result.EndOfMessage);

                    var json = Encoding.UTF8.GetString(buffer, 0, count);
                    HostStateMessage msg = null;

                    try { msg = JsonConvert.DeserializeObject<HostStateMessage>(json); }
                    catch { continue; }

                    if (msg == null) continue;
                    if (!string.Equals(msg.Type, "host_state", StringComparison.OrdinalIgnoreCase)) continue;

                    var cmd = (msg.ActiveCommand ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(cmd)) continue;

                    _pendingCommands.Enqueue(cmd);
                }

            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                    return; // normal shutdown

                Plugin.Log.Warn($"[MultiplayerRoomSyncClient] WS loop failed: {ex.Message}");
            }

        }

        private static System.Collections.IEnumerator PumpMainThread()
        {
            while (true)
            {
                while (_pendingCommands.TryDequeue(out var cmd))
                {
                    // Requirement: only followers should apply
                    if (!SceneHelper.MpPlusInRoom) continue;
                    if (SceneHelper.MpPlusIsHost) continue;

                    // Requirement: apply only when control == false (local follower)
                    if (MultiplayerStateClient.GetLocalControl()) continue;

                    // Execute locally, bypassing cooldown/permission checks
                    CommandHandler.Instance.ExecuteSyncedCommand(cmd);
                }

                yield return null;
            }
        }
    }
}
