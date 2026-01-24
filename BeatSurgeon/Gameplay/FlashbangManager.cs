using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BeatSurgeon.Chat;


namespace BeatSurgeon.Gameplay
{
    public class FlashbangManager : MonoBehaviour
    {
        private static FlashbangManager _instance;
        private static GameObject _go;

        // Environment lights we will affect
        private readonly List<TubeBloomPrePassLight> _lights = new List<TubeBloomPrePassLight>();
        private readonly List<Color> _baseColors = new List<Color>();
        private readonly List<float> _baseBloomMults = new List<float>();

        private Coroutine _flashCoroutine;

        public static bool FlashbangActive { get; private set; }

        public static FlashbangManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_FlashbangManager_GO");
                    Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<FlashbangManager>();
                    Plugin.Log.Info("FlashbangManager: Created new instance");
                }
                return _instance;
            }
        }

        /// <summary>
        /// Triggers a flashbang:
        /// - for holdSeconds, all environment tube lights are boosted by intensityMultiplier
        ///   and forced to white
        /// - over fadeSeconds, they fade back to original color and brightness.
        /// </summary>
        public bool TriggerFlashbang(float intensityMultiplier, float holdSeconds, float fadeSeconds)
        {
            // Require being in a level (same pattern as other managers)
            var controllers = Resources.FindObjectsOfTypeAll<BeatmapObjectSpawnController>();
            if (controllers == null || controllers.Length == 0)
            {
                Plugin.Log.Warn("FlashbangManager: Not in a map (no BeatmapObjectSpawnController).");
                return false;
            }

            // Stop any existing flash and restore before starting a new one
            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
                _flashCoroutine = null;
                RestoreLights();
            }

            CacheLights();
            if (_lights.Count == 0)
            {
                Plugin.Log.Warn("FlashbangManager: No TubeBloomPrePassLight instances found to flash.");
                return false;
            }

            MultiplayerStateClient.SetActiveCommand("flashbang");
            _flashCoroutine = StartCoroutine(FlashRoutine(intensityMultiplier, holdSeconds, fadeSeconds));
            return true;
        }

        private void CacheLights()
        {
            _lights.Clear();
            _baseColors.Clear();
            _baseBloomMults.Clear();

            // Grab all environment tube lights in the current scene
            var allLights = Resources.FindObjectsOfTypeAll<TubeBloomPrePassLight>();
            foreach (var light in allLights)
            {
                if (light == null || !light.isActiveAndEnabled)
                    continue;

                _lights.Add(light);
                _baseColors.Add(light.color);                      // original color
                _baseBloomMults.Add(light.bloomFogIntensityMultiplier); // original brightness multiplier
            }

            Plugin.Log.Info($"FlashbangManager: Cached {_lights.Count} TubeBloomPrePassLight(s).");
        }

        private IEnumerator FlashRoutine(float intensityMultiplier, float holdSeconds, float fadeSeconds)
        {
            FlashbangActive = true;
            //ChatManager.GetInstance().SendChatMessage("!!FLASHBANG! Shield your eyes!");

            // 1) Initial blast: force white & boost brightness
            for (int i = 0; i < _lights.Count; i++)
            {
                var light = _lights[i];
                if (light == null) continue;

                light.color = Color.white;
                light.bloomFogIntensityMultiplier = _baseBloomMults[i] * intensityMultiplier;
                light.Refresh(); // apply immediately
            }

            float elapsed = 0f;
            while (elapsed < holdSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // 2) Fade back to original over fadeSeconds
            elapsed = 0f;
            fadeSeconds = Mathf.Max(fadeSeconds, 0.001f);

            while (elapsed < fadeSeconds)
            {
                float t = elapsed / fadeSeconds;

                for (int i = 0; i < _lights.Count; i++)
                {
                    var light = _lights[i];
                    if (light == null) continue;

                    // Color: white -> original color
                    Color startColor = Color.white;
                    Color endColor = _baseColors[i];
                    light.color = Color.Lerp(startColor, endColor, t);

                    // Brightness: boosted -> original
                    float startMult = _baseBloomMults[i] * intensityMultiplier;
                    float endMult = _baseBloomMults[i];
                    light.bloomFogIntensityMultiplier = Mathf.Lerp(startMult, endMult, t);

                    light.Refresh();
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // 3) Hard restore to be safe
            RestoreLights();

            FlashbangActive = false;
            MultiplayerStateClient.SetActiveCommand(null);
            _flashCoroutine = null;
            Plugin.Log.Info("FlashbangManager: Flashbang finished.");
        }

        private void RestoreLights()
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                var light = _lights[i];
                if (light == null) continue;

                light.color = _baseColors[i];
                light.bloomFogIntensityMultiplier = _baseBloomMults[i];
                light.Refresh();
            }

            _lights.Clear();
            _baseColors.Clear();
            _baseBloomMults.Clear();
        }
    }
}
