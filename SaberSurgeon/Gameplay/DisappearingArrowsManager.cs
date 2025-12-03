// File: SaberSurgeon/Gameplay/DisappearingArrowsManager.cs
using System.Collections;
using UnityEngine;
using SaberSurgeon.Chat;

namespace SaberSurgeon.Gameplay
{
    public class DisappearingArrowsManager : MonoBehaviour
    {
        private static DisappearingArrowsManager _instance;
        private static GameObject _go;
        private Coroutine _daCoroutine;

        public static bool DisappearingActive { get; private set; }

        public static DisappearingArrowsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("SaberSurgeonDisappearingArrowsManagerGO");
                    Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<DisappearingArrowsManager>();
                    Plugin.Log.Info("DisappearingArrowsManager: Created new instance");
                }
                return _instance;
            }
        }

        /// <summary>Enable disappearing arrows for durationSeconds. Returns false if not in a map.</summary>
        public bool StartDisappearingArrows(float durationSeconds)
        {
            // Require being in a map (same pattern as RainbowManager)
            var inMap = Resources.FindObjectsOfTypeAll<BeatmapObjectSpawnController>().Length > 0;
            if (!inMap)
            {
                Plugin.Log.Warn("DisappearingArrowsManager: Not in a map, no BeatmapObjectSpawnController.");
                return false;
            }

            if (_daCoroutine != null)
            {
                StopCoroutine(_daCoroutine);
                _daCoroutine = null;
            }

            _daCoroutine = StartCoroutine(DisappearingCoroutine(durationSeconds));
            return true;
        }

        private IEnumerator DisappearingCoroutine(float durationSeconds)
        {
            DisappearingActive = true;
            Plugin.Log.Info($"DisappearingArrowsManager: Disappearing arrows enabled for {durationSeconds:F1}s");

            float elapsed = 0f;
            while (elapsed < durationSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            DisappearingActive = false;
            _daCoroutine = null;
            Plugin.Log.Info("DisappearingArrowsManager: Disappearing arrows finished");
            ChatManager.GetInstance().SendChatMessage("!!Disappearing arrows effect has ended.");
        }
    }
}
