using BeatSurgeon.HarmonyPatches;
using BS_Utils.Gameplay;
using BS_Utils.Utilities;
using System.Collections;
using System.Linq;
using UnityEngine;
using BSUtils = BS_Utils.Gameplay; // explicit alias

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Handles the !faster, !superfast, !slower effects
    /// - Enables a timeScale multiplier via FasterSongPatch
    /// - Disables score submission for the current run via BS_Utils
    /// - Automatically cleans up submission keys between maps
    /// </summary>
    public class FasterSongManager : MonoBehaviour
    {
        private static FasterSongManager _instance;

        // Submission keys for each effect
        private const string FASTER_KEY = "BeatSurgeon: Faster";
        private const string SUPERFAST_KEY = "BeatSurgeon: SuperFast";
        private const string SLOWER_KEY = "BeatSurgeon: Slower";

        public static FasterSongManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("BeatSurgeonFasterSongManager");
                    Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<FasterSongManager>();
                }
                return _instance;
            }
        }

        private AudioTimeSyncController _audio;
        private bool _active = false;
        private Coroutine _routine;
        private string _activeEffectKey;
        private string _currentSubmissionKey; // Track which key we used

        public bool IsActive => _active;
        public string ActiveEffectKey => _activeEffectKey;

        private void OnEnable()
        {
            // Hook BS_Utils gameSceneLoaded event - fires when entering a map
            BSEvents.gameSceneLoaded += OnGameSceneLoaded;
        }

        private void OnDisable()
        {
            BSEvents.gameSceneLoaded -= OnGameSceneLoaded;
        }

        /// <summary>
        /// Called by BS_Utils when a new map starts - this is the perfect time to clean up old keys
        /// </summary>
        private void OnGameSceneLoaded()
        {
            // If no speed effect is currently active, ensure all our keys are removed
            if (!_active)
            {
                CleanupAllSpeedKeys();
                LogUtils.Debug(() => "FasterSongManager: Cleaned up submission keys (new map started, no effect active)");
            }
        }

        /// <summary>
        /// Generic speed effect used by !faster, !superfast, !slower.
        /// </summary>
        public bool StartSpeedEffect(string effectKey, float multiplier, float duration, string submissionReason)
        {
            if (_audio == null)
            {
                _audio = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>()
                    .FirstOrDefault(a => a.isActiveAndEnabled);
            }

            if (_audio == null)
            {
                Plugin.Log?.Warn("FasterSongManager: No AudioTimeSyncController found (not in a map?)");
                return false;
            }

            // First activation
            if (!_active)
            {
                FasterSongPatch.Multiplier = multiplier;
                _active = true;
                _activeEffectKey = effectKey;

                // Store which key we're using for this effect
                _currentSubmissionKey = submissionReason;

                // Disable score submission for this run
                BS_Utils.Gameplay.ScoreSubmission.DisableSubmission(submissionReason);

                Plugin.Log?.Info($"FasterSongManager: Score submission DISABLED with key: {submissionReason}");
            }
            else
            {
                // Already active - just change speed and mark the new effect
                FasterSongPatch.Multiplier = multiplier;
                _activeEffectKey = effectKey;
            }

            MultiplayerStateClient.SetActiveCommand(effectKey); // "faster" / "superfast" / "slower"

            // Reset/extend timer
            if (_routine != null)
            {
                StopCoroutine(_routine);
            }
            _routine = StartCoroutine(SpeedRoutine(duration));

            Plugin.Log?.Info($"FasterSongManager: Speed effect '{effectKey}' enabled (x{multiplier}) for {duration} seconds.");
            return true;
        }

        private IEnumerator SpeedRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);

            // Effect ended - reset multiplier
            FasterSongPatch.Multiplier = 1.0f;
            _active = false;
            _activeEffectKey = null;
            _routine = null;

            MultiplayerStateClient.SetActiveCommand(null);

            // IMPORTANT: Remove the prolonged disable key when effect ends
            // This allows scores to submit on the NEXT map if no speed effect is active
            if (!string.IsNullOrEmpty(_currentSubmissionKey))
            {
                BS_Utils.Gameplay.ScoreSubmission.RemoveProlongedDisable(_currentSubmissionKey);
                Plugin.Log?.Info($"FasterSongManager: Speed effect ended, removed submission key: {_currentSubmissionKey}");
                _currentSubmissionKey = null;
            }

            Plugin.Log?.Info("FasterSongManager: Speed effect disabled, multiplier reset.");
        }

        /// <summary>
        /// Cleanup helper - removes all possible speed effect submission keys
        /// Called at map start if no effect is currently running
        /// </summary>
        private void CleanupAllSpeedKeys()
        {
            BS_Utils.Gameplay.ScoreSubmission.RemoveProlongedDisable(FASTER_KEY);
            BS_Utils.Gameplay.ScoreSubmission.RemoveProlongedDisable(SUPERFAST_KEY);
            BS_Utils.Gameplay.ScoreSubmission.RemoveProlongedDisable(SLOWER_KEY);
        }

        private void OnDestroy()
        {
            // Final cleanup on shutdown
            CleanupAllSpeedKeys();
            _instance = null;
        }
    }
}
