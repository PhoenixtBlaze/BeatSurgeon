using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

using BeatSurgeon.Gameplay; // for CoroutineHost

namespace BeatSurgeon
{
    /// <summary>
    /// Sends multiplayer state (room code, host flag, active command, control)
    /// to the backend server whenever it changes.
    /// </summary>
    internal static class MultiplayerStateClient
    {
        private const string EndpointUrl = "https://phoenixblaze0.duckdns.org/multiplayer-state";

        /// <summary>
        /// Represents the payload we send to the server.
        /// </summary>
        [Serializable]
        private class MultiplayerStatePayload
        {
            [JsonProperty("room_code")]
            public string RoomCode { get; set; }

            [JsonProperty("is_host")]
            public bool IsHost { get; set; }

            /// <summary>
            /// Name/identifier of the currently active redeem/command, or null/empty if none.
            /// </summary>
            [JsonProperty("active_command")]
            public string ActiveCommand { get; set; }

            /// <summary>
            /// Arbitrary control flag (true/false) you can set from gameplay logic.
            /// </summary>
            [JsonProperty("control")]
            public bool Control { get; set; }
        }
        private static string _activeCommand;
        private static bool _control;
        private static readonly object _lock = new object();
        private static MultiplayerStatePayload _lastSentState;
        private static bool _isSending;
        private const float HostHeartbeatSeconds = 5f;
        private static Coroutine _heartbeatCoroutine;
        private static bool _resendRequested;


        public static void Init()
        {
            // Ensure SceneHelper is running its poller
            SceneHelper.Init();

            SceneHelper.MpPlusInRoomChanged += _ => OnMpPlusChanged();
            SceneHelper.MpPlusRoomInfoChanged += OnMpPlusChanged;

            if (_heartbeatCoroutine == null)
                _heartbeatCoroutine = CoroutineHost.Instance.StartCoroutine(HostHeartbeatCoroutine());

            // Push initial state once
            OnMpPlusChanged();
        }

        public static bool GetLocalControl()
        {
            return _control;
        }


        private static void OnMpPlusChanged()
        {
            bool inRoom = SceneHelper.MpPlusInRoom;
            string roomCode = inRoom ? (SceneHelper.MpPlusRoomCode ?? string.Empty) : string.Empty;
            bool isHost = inRoom && SceneHelper.MpPlusIsHost;

            bool canControl =
                inRoom &&
                SceneHelper.MpPlusIsHost &&
                !string.IsNullOrWhiteSpace(SceneHelper.MpPlusRoomCode);

            bool controlToSend = canControl && _control;

            // If not in room, also force host false + room_code empty (matches your existing behavior).
            if (!inRoom)
            {
                roomCode = string.Empty;
                isHost = false;
                controlToSend = false;
            }

            UpdateState(roomCode, isHost, _activeCommand, controlToSend);
        }


        // Call these from your redeem/effect system:
        public static void SetActiveCommand(string command)
        {
            _activeCommand = string.IsNullOrWhiteSpace(command) ? null : command;
            OnMpPlusChanged();
        }

        public static void SetControl(bool value)
        {
            _control = value;
            OnMpPlusChanged();
        }


        /// <summary>
        /// Call this from your other code whenever any part of the state changes.
        /// </summary>
        /// <param name="roomCode">MP+ room code (e.g. "Q35UP"), or null/empty if not in room.</param>
        /// <param name="isHost">True if this player is the host/party owner.</param>
        /// <param name="activeCommand">
        /// Currently active redeem/command id (e.g. "rainbow", "ghost", "bomb"),
        /// or null/empty if no effect is active.
        /// </param>
        /// <param name="control">Your custom control flag.</param>
        /// 
        public static void UpdateState(string roomCode, bool isHost, string activeCommand, bool control, bool forceSend = false)
        {
            var payload = new MultiplayerStatePayload
            {
                RoomCode = roomCode ?? string.Empty,
                IsHost = isHost,
                ActiveCommand = string.IsNullOrWhiteSpace(activeCommand) ? null : activeCommand,
                Control = control
            };

            lock (_lock)
            {
                if (!forceSend && IsSameAsLast(payload))
                    return;

                _lastSentState = payload;

                if (_isSending)
                {
                    _resendRequested = true;
                    return;
                }

                _isSending = true;
                _resendRequested = false;
                CoroutineHost.Instance.StartCoroutine(SendStateCoroutine(payload));
            }
        }

        private static bool IsSameAsLast(MultiplayerStatePayload current)
        {
            if (_lastSentState == null) return false;

            return
                string.Equals(_lastSentState.RoomCode, current.RoomCode, StringComparison.Ordinal) &&
                _lastSentState.IsHost == current.IsHost &&
                string.Equals(_lastSentState.ActiveCommand, current.ActiveCommand, StringComparison.Ordinal) &&
                _lastSentState.Control == current.Control;
        }

        private static IEnumerator HostHeartbeatCoroutine()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(HostHeartbeatSeconds);

                if (!SceneHelper.MpPlusInRoom) continue;
                if (!SceneHelper.MpPlusIsHost) continue;

                var roomCode = (SceneHelper.MpPlusRoomCode ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(roomCode)) continue;

                // Recompute same logic as OnMpPlusChanged so we don't send invalid control while not host/etc.
                bool canControl = SceneHelper.MpPlusIsHost && !string.IsNullOrWhiteSpace(SceneHelper.MpPlusRoomCode);
                bool controlToSend = canControl && _control;

                UpdateState(roomCode, true, _activeCommand, controlToSend, forceSend: true);
            }
        }


        private static IEnumerator SendStateCoroutine(MultiplayerStatePayload payload)
        {
            string json;
            try
            {
                json = JsonConvert.SerializeObject(payload);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[MultiplayerStateClient] JSON serialize failed: {ex}"); // Plugin.Log exists in Plugin.cs [file:36]
                lock (_lock) { _isSending = false; }
                yield break;
            }

            using (var request = new UnityWebRequest(EndpointUrl, UnityWebRequest.kHttpVerbPOST))
            {
                byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.redirectLimit = 0; // debug: do not follow redirects (avoids rewind path)

                // Optional: identify the game/app to your server logs
                request.SetRequestHeader("X-Client-App", "SaberSurgeon-BS");

                var cid = PluginConfig.Instance?.MpClientId;
                if (!string.IsNullOrWhiteSpace(cid))
                    request.SetRequestHeader("X-MP-Client-Id", cid);


                Plugin.Log.Debug($"[MultiplayerStateClient] POST {EndpointUrl} body={json}");

                request.timeout = 10; // seconds

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
                {
                    Plugin.Log.Warn($"[MultiplayerStateClient] POST failed: {request.result} {request.responseCode} {request.error}");
                    yield return new WaitForSecondsRealtime(2f); // basic backoff
                }
                else
                {
                    Plugin.Log.Debug(
                        $"[MultiplayerStateClient] POST OK: {request.responseCode} resp={request.downloadHandler.text}");
                }
            }

            lock (_lock)
            {
                _isSending = false;
            }

            MultiplayerStatePayload next = null;
            bool startNext = false;

            lock (_lock)
            {
                if (_resendRequested)
                {
                    _resendRequested = false;
                    next = _lastSentState;
                    startNext = next != null;
                }
                else
                {
                    _isSending = false;
                }

                if (startNext)
                    _isSending = true;
            }

            if (startNext)
                CoroutineHost.Instance.StartCoroutine(SendStateCoroutine(next));

        }
    }
}
