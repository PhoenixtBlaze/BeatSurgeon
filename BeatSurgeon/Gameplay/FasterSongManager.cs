using System.Collections;
using System.Linq;
using BS_Utils.Gameplay;
using BeatSurgeon.HarmonyPatches;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Handles the !faster effect:
    /// - Enables a timeScale multiplier via FasterSongPatch
    /// - Disables score submission for the current run via BS_Utils
    /// </summary>
    public class FasterSongManager : MonoBehaviour
    {
        private static FasterSongManager _instance;
        public static FasterSongManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("BeatSurgeon_FasterSongManager");
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
        public bool IsActive => _active;
        public string ActiveEffectKey => _activeEffectKey;

        /// <summary>
        /// Generic speed effect used by !faster and !superfast.
        /// multiplier: scale applied to AudioTimeSyncController._timeScale.
        /// duration: seconds in real time.
        /// submissionReason: string passed to BS_Utils to disable score submission.
        /// </summary>
        public bool StartSpeedEffect(string effectKey, float multiplier, float duration, string submissionReason)
        {
            if (_audio == null)
            {
                _audio = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>()
                                  .FirstOrDefault(a => a.isActiveAndEnabled);
                if (_audio == null)
                {
                    Plugin.Log.Warn("[FasterSongManager] No AudioTimeSyncController found – not in a map?");
                    return false;
                }
            }

            // First activation
            if (!_active)
            {
                FasterSongPatch.Multiplier = multiplier;
                _active = true;
                _activeEffectKey = effectKey;

                // Disable score submission for this run
                ScoreSubmission.DisableSubmission(submissionReason);
            }
            else
            {
                // Already active: just change speed and mark the new effect
                FasterSongPatch.Multiplier = multiplier;
                _activeEffectKey = effectKey;

            }

            MultiplayerStateClient.SetActiveCommand(effectKey); // "faster" / "superfast" / "slower"
            // Reset / extend timer
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = StartCoroutine(SpeedRoutine(duration));

            Plugin.Log.Info($"[FasterSongManager] Speed effect '{effectKey}' enabled: x{multiplier} for {duration} seconds.");
            return true;
        }

        // Optional backwards-compatible wrapper for old !faster calls
        
        private IEnumerator SpeedRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);

            FasterSongPatch.Multiplier = 1.0f;

            _active = false;
            _activeEffectKey = null;
            _routine = null;
            MultiplayerStateClient.SetActiveCommand(null);
            Plugin.Log.Info("[FasterSongManager] Speed effect disabled (multiplier reset).");
        }
    }
}
