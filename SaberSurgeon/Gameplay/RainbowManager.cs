using System.Collections;
using SaberSurgeon.Chat;
using UnityEngine;

namespace SaberSurgeon.Gameplay
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

        public static RainbowManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("SaberSurgeon_RainbowManager_GO");
                    Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<RainbowManager>();
                    Plugin.Log.Info("RainbowManager: Created new instance");
                }
                return _instance;
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
            Plugin.Log.Info($"RainbowManager: Rainbow enabled for {durationSeconds:F1}s");

            float elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            RainbowActive = false;
            _rainbowCoroutine = null;
            Plugin.Log.Info("RainbowManager: Rainbow finished");
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
