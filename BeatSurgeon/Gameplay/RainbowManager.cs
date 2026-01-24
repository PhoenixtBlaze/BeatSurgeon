using System.Collections;
using System.Collections.Generic;
using BeatSurgeon.Chat;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    public class RainbowManager : MonoBehaviour
    {
        private static RainbowManager _instance;
        private static GameObject _go;

        private Coroutine _rainbowCoroutine;
        private Coroutine _noteColorCoroutine;

        // Random per-note rainbow mode
        public static bool RainbowActive { get; private set; }

        // Fixed left/right color override mode
        public static bool NoteColorActive { get; private set; }
        public static Color LeftColor { get; private set; }
        public static Color RightColor { get; private set; }

        // NEW: Rainbow cycling configuration
        public static float RainbowCycleSpeed { get; set; } = 0.5f; // Full spectrum cycles per second

        // NEW: Global current hue for all notes
        private static float _currentLeftHue = 0f;
        private static float _currentRightHue = 0.5f; // Opposite side of spectrum

        // NEW: Track active notes for continuous color updates
        private readonly Dictionary<ColorNoteVisuals, NoteRainbowData> _activeNotes
            = new Dictionary<ColorNoteVisuals, NoteRainbowData>();

        // NEW: Data for each rainbow note
        private class NoteRainbowData
        {
            public ColorType ColorType;
            public MaterialPropertyBlockController[] Controllers;
            public int ColorId;
            public float DefaultAlpha;
        }

        public static RainbowManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_RainbowManager_GO");
                    Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<RainbowManager>();
                    Plugin.Log.Info("RainbowManager: Created new instance");
                }
                return _instance;
            }
        }

        /// <summary>
        /// Register a note for continuous rainbow color updates.
        /// </summary>
        public void RegisterNote(
            ColorNoteVisuals visual,
            ColorType colorType,
            MaterialPropertyBlockController[] controllers,
            int colorId,
            float defaultAlpha)
        {
            if (visual == null || controllers == null)
                return;

            _activeNotes[visual] = new NoteRainbowData
            {
                ColorType = colorType,
                Controllers = controllers,
                ColorId = colorId,
                DefaultAlpha = defaultAlpha
            };
        }

        /// <summary>
        /// Unregister a note when it's destroyed or cut.
        /// </summary>
        public void UnregisterNote(ColorNoteVisuals visual)
        {
            if (visual != null)
                _activeNotes.Remove(visual);
        }

        private void Update()
        {
            if (!RainbowActive || _activeNotes.Count == 0)
                return;

            // Calculate GLOBAL hue for all notes based on time
            float cycleProgress = (Time.time * RainbowCycleSpeed) % 1f; // 0-1 repeating

            // Left hand: starts at red (0), cycles through spectrum
            _currentLeftHue = cycleProgress;

            // Right hand: opposite side of spectrum (180° apart)
            _currentRightHue = (cycleProgress + 0.5f) % 1f;

            // Create the two rainbow colors (one for each hand)
            Color leftRainbowColor = Color.HSVToRGB(_currentLeftHue, 0.85f, 1f);
            Color rightRainbowColor = Color.HSVToRGB(_currentRightHue, 0.85f, 1f);

            // Update all active notes with their respective hand color
            foreach (var kvp in _activeNotes)
            {
                var visual = kvp.Key;
                var data = kvp.Value;

                if (visual == null || data.Controllers == null)
                    continue;

                // Pick color based on which hand this note belongs to
                Color noteColor = (data.ColorType == ColorType.ColorA)
                    ? leftRainbowColor
                    : rightRainbowColor;

                // Apply to all material property block controllers
                foreach (var controller in data.Controllers)
                {
                    if (controller == null)
                        continue;

                    try
                    {
                        var mpb = controller.materialPropertyBlock;
                        if (mpb != null)
                        {
                            mpb.SetColor(data.ColorId, noteColor.ColorWithAlpha(data.DefaultAlpha));
                            controller.ApplyChanges();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Plugin.Log.Error($"RainbowManager: Error updating note color: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Enable random rainbow mode for durationSeconds.
        /// </summary>
        public bool StartRainbow(float durationSeconds)
        {
            // Optional: require being in a map (notes exist)
            var inMap = Resources.FindObjectsOfTypeAll<BeatmapObjectSpawnController>().Length > 0;
            if (!inMap)
            {
                Plugin.Log.Warn("RainbowManager: Not in a map (no BeatmapObjectSpawnController).");
                return false;
            }

            // Stop any fixed-color override
            if (_noteColorCoroutine != null)
            {
                StopCoroutine(_noteColorCoroutine);
                _noteColorCoroutine = null;
            }
            NoteColorActive = false;

            if (_rainbowCoroutine != null)
            {
                StopCoroutine(_rainbowCoroutine);
                _rainbowCoroutine = null;
            }

            // Reset hue to starting position
            _currentLeftHue = 0f;
            _currentRightHue = 0.5f;

            MultiplayerStateClient.SetActiveCommand("rainbow");
            _rainbowCoroutine = StartCoroutine(RainbowCoroutine(durationSeconds));
            return true;
        }

        /// <summary>
        /// Enable fixed left/right note colors for durationSeconds.
        /// </summary>
        public bool StartNoteColor(Color left, Color right, float durationSeconds)
        {
            // Optional: require being in a map (notes exist)
            var inMap = Resources.FindObjectsOfTypeAll<BeatmapObjectSpawnController>().Length > 0;
            if (!inMap)
            {
                Plugin.Log.Warn("RainbowManager: Not in a map (no BeatmapObjectSpawnController).");
                return false;
            }

            // Stop random rainbow mode
            if (_rainbowCoroutine != null)
            {
                StopCoroutine(_rainbowCoroutine);
                _rainbowCoroutine = null;
            }
            RainbowActive = false;

            if (_noteColorCoroutine != null)
            {
                StopCoroutine(_noteColorCoroutine);
                _noteColorCoroutine = null;
            }

            LeftColor = left;
            RightColor = right;

            _noteColorCoroutine = StartCoroutine(NoteColorCoroutine(durationSeconds));
            return true;
        }

        private IEnumerator RainbowCoroutine(float durationSeconds)
        {
            RainbowActive = true;
            Plugin.Log.Info($"RainbowManager: Rainbow enabled for {durationSeconds:F1}s (CycleSpeed={RainbowCycleSpeed})");

            float elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            RainbowActive = false;
            _activeNotes.Clear(); // Clean up tracked notes
            _rainbowCoroutine = null;
            Plugin.Log.Info("RainbowManager: Rainbow finished");
            MultiplayerStateClient.SetActiveCommand(null);
            ChatManager.GetInstance().SendChatMessage("!!Rainbow notes effect has ended.");
        }

        private IEnumerator NoteColorCoroutine(float durationSeconds)
        {
            NoteColorActive = true;
            Plugin.Log.Info($"RainbowManager: NoteColor override enabled for {durationSeconds:F1}s " +
                            $"(Left={LeftColor}, Right={RightColor})");

            float elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            NoteColorActive = false;
            _noteColorCoroutine = null;
            Plugin.Log.Info("RainbowManager: NoteColor override finished");
            ChatManager.GetInstance().SendChatMessage("!!NoteColor effect has ended.");
        }
    }
}
