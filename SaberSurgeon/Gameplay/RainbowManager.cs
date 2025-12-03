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

        public static bool RainbowActive { get; private set; }

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
        /// Enable rainbow mode for durationSeconds. Returns true if it could start.
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

            if (_rainbowCoroutine != null)
            {
                StopCoroutine(_rainbowCoroutine);
                _rainbowCoroutine = null;
            }

            _rainbowCoroutine = StartCoroutine(RainbowCoroutine(durationSeconds));
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
    }
}
