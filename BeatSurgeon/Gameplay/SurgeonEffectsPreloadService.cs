using System;
using System.Collections;
using BeatSurgeon.Utils;
using BS_Utils.Utilities;
using UnityEngine;
using Zenject;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Loads surgeoneffects and builds gameplay templates while the player is in the main menu
    /// so first-map effects do not hitch during gameplay.
    /// </summary>
    internal sealed class SurgeonEffectsPreloadService : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SurgeonEffectsPreload");
        private Coroutine _preloadRoutine;

        public void Initialize()
        {
            _log.Lifecycle("Initialize - scheduling surgeoneffects menu preload");
            BSEvents.menuSceneActive += OnMenuSceneActive;
            SchedulePreload();
        }

        public void Dispose()
        {
            BSEvents.menuSceneActive -= OnMenuSceneActive;
            StopPreloadRoutine();
        }

        private void OnMenuSceneActive()
        {
            if (SurgeonEffectsBundleService.AreMenuAssetsPreloaded)
            {
                return;
            }

            _log.Debug("menuSceneActive - scheduling surgeoneffects preload");
            SchedulePreload();
        }

        private void SchedulePreload()
        {
            StopPreloadRoutine();
            _preloadRoutine = CoroutineHost.Instance.StartCoroutine(MenuPreloadCoroutine());
        }

        private void StopPreloadRoutine()
        {
            if (_preloadRoutine == null)
            {
                return;
            }

            CoroutineHost.Instance.StopCoroutine(_preloadRoutine);
            _preloadRoutine = null;
        }

        private IEnumerator MenuPreloadCoroutine()
        {
            if (SurgeonEffectsBundleService.AreMenuAssetsPreloaded)
            {
                _preloadRoutine = null;
                yield break;
            }

            _log.Info("Starting surgeoneffects menu preload after idle delay.");
            yield return new WaitForSeconds(SurgeonEffectsWarmupHelper.MenuIdleDelaySeconds);
            yield return SurgeonEffectsBundleService.PreloadMenuAssetsCoroutine();
            _preloadRoutine = null;

            if (SurgeonEffectsBundleService.AreMenuAssetsPreloaded)
            {
                _log.Info("Surgeoneffects menu preload finished.");
            }
            else
            {
                _log.Warn("Surgeoneffects menu preload did not complete; gameplay may load assets on demand.");
            }
        }
    }
}
