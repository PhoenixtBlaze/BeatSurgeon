using SaberSurgeon.Chat;
using System.Collections.Generic;
using UnityEngine;

namespace SaberSurgeon.Gameplay
{
    public class BombManager : MonoBehaviour
    {
        private static BombManager _instance;
        private static GameObject _go;

        private NoteData _activeBombNote;
        private float _activeBombSetTime;

        // Tweak these two values to control how often it reappears
        private const float BombMissTimeoutSeconds = 8.0f; // if not cut after this, treat as missed
        private const float BombRearmDelaySeconds = 0.25f; // rearm shortly after clearing
        private float _nextRearmTime;


        // --- State ---
        public static bool BombArmed { get; private set; }
        public static string CurrentBomberName { get; private set; } = "Unknown";
        public static float BombWindowEndTime { get; private set; }
        public static bool BombConsumed { get; private set; }

        // --- Collections ---

        // Tracks which NoteData is the "Bomb"
        private readonly Dictionary<NoteData, string> _bombNotes = new Dictionary<NoteData, string>();

        // NEW: Tracks ACTIVE visuals so we can clear them without FindObjectsOfTypeAll
        private readonly Dictionary<GameNoteController, (bool cubeEnabled, bool circleEnabled)> _activeBombVisuals = new Dictionary<GameNoteController, (bool, bool)>();
        
        // Pending bomb requests; each entry must eventually become exactly one cut bomb.
        private readonly Queue<string> _pendingBombers = new Queue<string>();


        public static bool IsBombWindowActive
        {
            get
            {
                if (_instance == null) return false;
                // Active mapping OR pending requests means we should keep trying to spawn a bomb.
                return _instance._bombNotes.Count > 0 || _instance._pendingBombers.Count > 0;
            }
        }


        public static BombManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("SaberSurgeon_BombManager_GO");
                    Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<BombManager>();
                    LogUtils.Debug("BombManager: Created new instance");
                }
                return _instance;
            }
        }

        public bool ArmBomb(string bomberName, float durationSeconds)
        {
            var inMap = UnityEngine.Object.FindObjectOfType<BeatmapObjectSpawnController>() != null;
            if (!inMap)
            {
                LogUtils.Debug("BombManager: Not in a map.");
                return false;
            }


            string name = string.IsNullOrEmpty(bomberName) ? "Unknown" : bomberName;
            _pendingBombers.Enqueue(name);

            // “Armed” now just means: we have something pending and can attach to the next note.
            BombArmed = true;
            BombConsumed = false;

            LogUtils.Debug($"BombManager: Enqueued bomb for {name} (queue={_pendingBombers.Count})");
            return true;
        }

        public bool MarkNoteAsBomb(NoteData noteData)
        {
            if (noteData == null) return false;

            // Only one active bomb note at a time in your current design.
            if (_bombNotes.Count > 0) return false;

            // No pending requests => do nothing.
            if (_pendingBombers.Count == 0) return false;

            // Respect your rearm delay.
            if (Time.time < _nextRearmTime) return false;

            string bomber = _pendingBombers.Peek(); // DO NOT Dequeue yet (only dequeue on successful cut)

            _bombNotes[noteData] = bomber;
            _activeBombNote = noteData;
            _activeBombSetTime = Time.time;

            CurrentBomberName = bomber;
            BombArmed = false;

            LogUtils.Debug($"BombManager: Marked note at {noteData.time:F3} as bomb for {bomber} (queue={_pendingBombers.Count})");
            return true;
        }




        // NEW: Call this from BombNotePatch to track the visual
        public void RegisterBombVisual(GameNoteController controller)
        {
            if (controller == null || _activeBombVisuals.ContainsKey(controller))
                return;

            var t = controller.transform;
            var noteCube = t.Find("NoteCube");
            bool cubeEnabled = noteCube?.GetComponent<MeshRenderer>()?.enabled ?? false;

            var circle = noteCube?.Find("NoteCircleGlow");
            bool circleEnabled = circle?.GetComponent<MeshRenderer>()?.enabled ?? false;

            _activeBombVisuals[controller] = (cubeEnabled, circleEnabled);
        }


        public bool TryConsumeBomb(NoteData noteData, out string bomber)
        {
            bomber = null;
            if (noteData == null) return false;
            if (!_bombNotes.TryGetValue(noteData, out bomber)) return false;

            _bombNotes.Remove(noteData);
            _activeBombNote = null;
            _activeBombSetTime = 0f;

            // Consume exactly one queued request (the one we attached).
            if (_pendingBombers.Count > 0 && string.Equals(_pendingBombers.Peek(), bomber))
                _pendingBombers.Dequeue();

            BombConsumed = true;
            BombArmed = _pendingBombers.Count > 0;

            LogUtils.Debug($"BombManager: Bomb cut by {bomber}! (queue={_pendingBombers.Count})");
            return true;
        }


        // OPTIMIZED: Clears only known active visuals
        public void ClearBombVisuals()
        {
            LogUtils.Debug($"BombManager: Clearing {_activeBombVisuals.Count} active bomb visuals...");

            // Snapshot the dictionary entries so we can safely clear afterward
            var toClear = new List<KeyValuePair<GameNoteController, (bool cubeEnabled, bool circleEnabled)>>(_activeBombVisuals);

            foreach (var kvp in toClear)
            {
                var note = kvp.Key;
                var state = kvp.Value;
                if (note == null) continue;

                var t = note.transform;

                var bombVisual = t.Find("SaberSurgeon_BombVisual");
                if (bombVisual != null) Destroy(bombVisual.gameObject);

                var noteCube = t.Find("NoteCube");
                if (noteCube != null)
                {
                    var cubeMr = noteCube.GetComponent<MeshRenderer>();
                    if (cubeMr != null) cubeMr.enabled = state.cubeEnabled;

                    var circle = noteCube.Find("NoteCircleGlow");
                    if (circle != null)
                    {
                        var circleMr = circle.GetComponent<MeshRenderer>();
                        if (circleMr != null) circleMr.enabled = state.circleEnabled;
                    }
                }
            }

            _activeBombVisuals.Clear();
            LogUtils.Debug("BombManager: Visuals cleared.");
        }

        private bool IsInMap()
        {
            return UnityEngine.Object.FindObjectOfType<BeatmapObjectSpawnController>() != null;
        }

        private void ClearTransientGameplayState()
        {
            _bombNotes.Clear();
            _activeBombNote = null;
            _activeBombSetTime = 0f;
            _nextRearmTime = 0f;

            BombConsumed = false;
            BombArmed = _pendingBombers.Count > 0;   // keep queue-driven
            CurrentBomberName = "Unknown";

            ClearBombVisuals();
        }

        private void Update()
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
                    LogUtils.Debug("BombManager: Bomb not cut in time; clearing and rearming");
                    _bombNotes.Clear();
                    _activeBombNote = null;
                    ClearBombVisuals();
                    BombArmed = true;
                    _nextRearmTime = Time.time + BombRearmDelaySeconds;
                }
            }
        }



        public void Shutdown()
        {
            BombArmed = false;
            CurrentBomberName = "Unknown";
            _bombNotes.Clear();
            _activeBombVisuals.Clear();
        }
    }
}
