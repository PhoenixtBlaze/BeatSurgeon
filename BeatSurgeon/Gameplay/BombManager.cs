using BeatSurgeon.Chat;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace BeatSurgeon.Gameplay
{
    public class BombManager : MonoBehaviour
    {
        internal sealed class BombRequest
        {
            internal BombRequest(string requesterName, string displayText)
            {
                RequesterName = string.IsNullOrWhiteSpace(requesterName) ? "Unknown" : requesterName;
                DisplayText = string.IsNullOrWhiteSpace(displayText) ? RequesterName : displayText;
            }

            internal string RequesterName { get; private set; }
            internal string DisplayText { get; private set; }
        }

        private static BombManager _instance;
        private static GameObject _go;
        private static readonly ProfilerMarker UpdateProfiler = new ProfilerMarker("BeatSurgeon.BombManager.Update");

        private NoteData _activeBombNote;
        private float _activeBombSetTime;
        private GameplayManager _gameplayManager;

        // Tweak these two values to control how often it reappears
        public const float BombMissTimeoutSeconds = 3.0f; // if not cut after this, treat as missed
        private const float BombRearmDelaySeconds = 0.25f; // rearm shortly after clearing
        private float _nextRearmTime;


        // --- State ---
        public static bool BombArmed { get; private set; }
        public static string CurrentBomberName { get; private set; } = "Unknown";
        public static float BombWindowEndTime { get; private set; }
        public static bool BombConsumed { get; private set; }

        // --- Collections ---

        // Tracks which NoteData is the "Bomb"
        private readonly Dictionary<NoteData, BombRequest> _bombNotes = new Dictionary<NoteData, BombRequest>();

        private struct BombVisualState
        {
            public BombVisualInstance Visual;   // <-- CHANGE THISS
            public MeshRenderer CubeRenderer;
            public MeshRenderer CircleRenderer;
            public bool CubeWasEnabled;
            public bool CircleWasEnabled;
        }

        private readonly Dictionary<GameNoteController, BombVisualState> _activeBombVisuals
            = new Dictionary<GameNoteController, BombVisualState>();

        // Pending bomb requests; each entry must eventually become exactly one cut bomb.
        private readonly Queue<BombRequest> _pendingBombRequests = new Queue<BombRequest>();


        public static bool IsBombWindowActive
        {
            get
            {
                if (_instance == null) return false;
                // Active mapping OR pending requests means we should keep trying to spawn a bomb.
                return _instance._bombNotes.Count > 0 || _instance._pendingBombRequests.Count > 0;
            }
        }


        public static BombManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_BombManager_GO");
                    Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<BombManager>();

                    LogUtils.Debug(() => "BombManager: Created new instance");
                }
                return _instance;
            }
        }

        public bool ArmBomb(string bomberName, float durationSeconds)
        {
            return ArmBomb(bomberName, null, durationSeconds);
        }

        public bool ArmBomb(string requesterName, string displayText, float durationSeconds)
        {
            var inMap = IsInMap();
            if (!inMap)
            {
                LogUtils.Debug(() => $"BombManager: Not in a map.");
                return false;
            }

            BombRequest bombRequest = new BombRequest(requesterName, displayText);
            _pendingBombRequests.Enqueue(bombRequest);

            // “Armed” now just means: we have something pending and can attach to the next note.
            BombArmed = true;
            BombConsumed = false;

            MultiplayerStateClient.SetActiveCommand("bomb");
            string requesterNameLocal = bombRequest.RequesterName;
            string displayTextLocal = bombRequest.DisplayText;
            int queueCount = _pendingBombRequests.Count;
            LogUtils.Debug(() => $"BombManager: Enqueued bomb for {requesterNameLocal} (display='{displayTextLocal}', queue={queueCount})");
            return true;
        }

        internal static bool IsEligibleBombNote(NoteData noteData)
        {
            return noteData != null
                && noteData.colorType != ColorType.None
                && noteData.gameplayType == NoteData.GameplayType.Normal;
        }

        public bool MarkNoteAsBomb(NoteData noteData)
        {
            if (!IsEligibleBombNote(noteData)) return false;

            // Only one active bomb note at a time in your current design.
            if (_bombNotes.Count > 0) return false;

            // No pending requests => do nothing.
            if (_pendingBombRequests.Count == 0) return false;

            // Respect your rearm delay.
            if (Time.time < _nextRearmTime) return false;

            BombRequest bombRequest = _pendingBombRequests.Peek(); // DO NOT Dequeue yet (only dequeue on successful cut)

            _bombNotes[noteData] = bombRequest;
            _activeBombNote = noteData;
            _activeBombSetTime = Time.time;

            CurrentBomberName = bombRequest.RequesterName;
            BombArmed = false;

            string requesterNameLocal = bombRequest.RequesterName;
            string displayTextLocal = bombRequest.DisplayText;
            int queueCount = _pendingBombRequests.Count;
            LogUtils.Debug(() => $"BombManager: Marked note at {noteData.time:F3} as bomb for {requesterNameLocal} (display='{displayTextLocal}', queue={queueCount})");
            return true;
        }




        // NEW: Call this from BombNotePatch to track the visual
        internal void RegisterBombVisual(
            GameNoteController controller,
            BombVisualInstance visual,
            MeshRenderer cubeRenderer,
            MeshRenderer circleRenderer,
            bool cubeWasEnabled,
            bool circleWasEnabled)
        {
            if (controller == null || _activeBombVisuals.ContainsKey(controller))
                return;

            _activeBombVisuals[controller] = new BombVisualState
            {
                Visual = visual,
                CubeRenderer = cubeRenderer,
                CircleRenderer = circleRenderer,
                CubeWasEnabled = cubeWasEnabled,
                CircleWasEnabled = circleWasEnabled
            };
        }



        public bool TryConsumeBomb(NoteData noteData, out string bomber)
        {
            bomber = null;
            if (!TryConsumeBomb(noteData, out BombRequest bombRequest))
            {
                return false;
            }

            bomber = bombRequest.RequesterName;
            return true;
        }

        internal bool TryConsumeBomb(NoteData noteData, out BombRequest bombRequest)
        {
            bombRequest = null;
            if (noteData == null) return false;
            if (!_bombNotes.TryGetValue(noteData, out bombRequest)) return false;

            _bombNotes.Remove(noteData);
            _activeBombNote = null;
            _activeBombSetTime = 0f;

            // Consume exactly one queued request (the one we attached).
            if (_pendingBombRequests.Count > 0 && ReferenceEquals(_pendingBombRequests.Peek(), bombRequest))
                _pendingBombRequests.Dequeue();

            BombConsumed = true;
            BombArmed = _pendingBombRequests.Count > 0;

            string requesterNameLocal = bombRequest.RequesterName;
            string displayTextLocal = bombRequest.DisplayText;
            int queueCount = _pendingBombRequests.Count;

            if (_pendingBombRequests.Count == 0 && _bombNotes.Count == 0)
                MultiplayerStateClient.SetActiveCommand(null);

            LogUtils.Debug(() => $"BombManager: Bomb cut by {requesterNameLocal}! (display='{displayTextLocal}', queue={queueCount})");
            return true;
        }

        internal bool IsNoteMarkedAsBomb(NoteData noteData)
        {
            return noteData != null && _bombNotes.ContainsKey(noteData);
        }


        // OPTIMIZED: Clears only known active visuals
        public void ClearBombVisuals()
        {
            LogUtils.Debug(() => $"BombManager: Clearing {_activeBombVisuals.Count} active bomb visuals...");

            foreach (var kvp in _activeBombVisuals)
            {
                var state = kvp.Value;

                // Return pooled visual (no Transform.Find needed)
                if (state.Visual != null)
                    BombVisualPool.Instance.Return(state.Visual);

                // Restore renderers (no Transform.Find needed)
                if (state.CubeRenderer != null)
                    state.CubeRenderer.enabled = state.CubeWasEnabled;

                if (state.CircleRenderer != null)
                    state.CircleRenderer.enabled = state.CircleWasEnabled;
            }

            _activeBombVisuals.Clear();
            LogUtils.Debug(() => "BombManager: Visuals cleared.");
        }


        private bool IsInMap()
        {
            if (_gameplayManager == null)
            {
                _gameplayManager = GameplayManager.GetInstance();
            }

            return _gameplayManager != null && _gameplayManager.IsInMap;
        }

        private void ClearTransientGameplayState()
        {
            _bombNotes.Clear();
            _activeBombNote = null;
            _activeBombSetTime = 0f;
            _nextRearmTime = 0f;

            BombConsumed = false;
            BombArmed = _pendingBombRequests.Count > 0;   // keep queue-driven
            CurrentBomberName = "Unknown";

            ClearBombVisuals();
        }

        private void Update()
        {
            using (UpdateProfiler.Auto())
            {
                // If gameplay ended / not in a map anymore: clear ONLY per-map state.
                if (!IsInMap())
                {
                    if (_bombNotes.Count > 0 || _activeBombNote != null || _activeBombVisuals.Count > 0)
                        ClearTransientGameplayState();

                    return;
                }

                // If there is an active bomb note and it's not cut soon, treat as missed and rearm
                if (!BombConsumed && _activeBombNote != null && _bombNotes.Count > 0)
                {
                    if (Time.time - _activeBombSetTime >= BombMissTimeoutSeconds)
                    {
                        LogUtils.Debug(() => "BombManager: Bomb not cut in time; clearing and rearming");
                        _bombNotes.Clear();
                        _activeBombNote = null;
                        ClearBombVisuals();
                        BombArmed = _pendingBombRequests.Count > 0;
                        _nextRearmTime = Time.time + BombRearmDelaySeconds;
                    }
                }
            }
        }



        public void Shutdown()
        {
            BombArmed = false;
            CurrentBomberName = "Unknown";
            _bombNotes.Clear();
            _pendingBombRequests.Clear();
            _activeBombVisuals.Clear();
        }
    }
}
