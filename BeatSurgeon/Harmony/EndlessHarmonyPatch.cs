using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using BeatSurgeon.Gameplay;
using BeatSurgeon;

namespace BeatSurgeon.HarmonyPatches
{
    /*
    // PATCH 1: PlayFirstSubmitLater
    // Blocks the Finish event so ScoreSaber/BeatLeader never hear about the map ending until we say so.
    [HarmonyPatch(typeof(StandardLevelScenesTransitionSetupDataSO), "Finish")]
    internal static class PlayFirstSubmitLaterPatch
    {
        private static bool _pauseGateActive = false;
        private static bool _bypassPauseGateCall = false;

        private static Action _continueDelegate;
        private static Action _resumeFinishedDelegate;
        private static Action _menuDelegate;
        private static Action _restartDelegate;

        private static StandardLevelScenesTransitionSetupDataSO _pendingSetup;
        private static LevelCompletionResults _pendingResults;
        private static PauseMenuManager _pauseMenuManager;

        private static bool ShouldPauseGate(LevelCompletionResults results)
        {
            if (results == null) return false;

            // 1) Native Multiplayer check (keep as-is)
            if (BS_Utils.Plugin.LevelData.Mode == BS_Utils.Gameplay.Mode.Multiplayer)
                return false;

            // 2) MP+ room check (NEW)
            if (SceneHelper.MpPlusInRoom)
            {
                Plugin.Log.Info("[PlayFirstSubmitLater] MP+ room detected. Skipping auto-pause.");
                return false;
            }


            var s = Plugin.Settings;
            if (s == null) return false;

            // Global Disable Check
            if (!s.PlayFirstSubmitLaterEnabled || !s.AutoPauseOnMapEnd) return false;

            // Endless Mode Check
            var gm = Gameplay.GameplayManager.GetInstance();
            if (gm != null && gm.IsPlaying() && gm.GetRemainingTime() > 0f) return false;

            // Don't pause if user Quit or Restarted manually
            if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Quit ||
                results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart)
                return false;

            // Don't pause on Failure
            if (results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
                return false;

            return true;
        }

        private static void CleanupPauseListeners()
        {
            if (_pauseMenuManager != null)
            {
                if (_continueDelegate != null) _pauseMenuManager.didPressContinueButtonEvent -= _continueDelegate;
                if (_resumeFinishedDelegate != null) _pauseMenuManager.didFinishResumeAnimationEvent -= _resumeFinishedDelegate;
                if (_menuDelegate != null) _pauseMenuManager.didPressMenuButtonEvent -= _menuDelegate;
                if (_restartDelegate != null) _pauseMenuManager.didPressRestartButtonEvent -= _restartDelegate;
            }
            _continueDelegate = null;
            _resumeFinishedDelegate = null;
            _menuDelegate = null;
            _restartDelegate = null;
            _pauseMenuManager = null;
        }

        private static void ClearPauseGate()
        {
            CleanupPauseListeners();
            _pauseGateActive = false;
            _pendingSetup = null;
            _pendingResults = null;
        }

        private static void HandleContinuePressed() { }

        private static void HandleResumeFinished()
        {
            if (_pendingSetup == null)
            {
                ClearPauseGate();
                return;
            }

            try
            {
                _bypassPauseGateCall = true;
                _pendingSetup.Finish(_pendingResults);
            }
            finally
            {
                _bypassPauseGateCall = false;
                ClearPauseGate();
            }
        }

        private static void HandleAbort() { ClearPauseGate(); }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(StandardLevelScenesTransitionSetupDataSO __instance, LevelCompletionResults levelCompletionResults)
        {
            try
            {
                if (__instance == null || levelCompletionResults == null) return true;
                if (_bypassPauseGateCall) return true;

                if (!_pauseGateActive && ShouldPauseGate(levelCompletionResults))
                {
                    _pauseGateActive = true;
                    _pendingSetup = __instance;
                    _pendingResults = levelCompletionResults;

                    CleanupPauseListeners();

                    var freshManager = Resources.FindObjectsOfTypeAll<PauseMenuManager>().FirstOrDefault();
                    var pauseController = Resources.FindObjectsOfTypeAll<PauseController>().FirstOrDefault();

                    if (freshManager != null && pauseController != null)
                    {
                        _pauseMenuManager = freshManager;

                        _continueDelegate = HandleContinuePressed;
                        _resumeFinishedDelegate = HandleResumeFinished;
                        _menuDelegate = HandleAbort;
                        _restartDelegate = HandleAbort;

                        _pauseMenuManager.didPressContinueButtonEvent += _continueDelegate;
                        _pauseMenuManager.didFinishResumeAnimationEvent += _resumeFinishedDelegate;
                        _pauseMenuManager.didPressMenuButtonEvent += _menuDelegate;
                        _pauseMenuManager.didPressRestartButtonEvent += _restartDelegate;

                        pauseController.Pause();
                        _pauseMenuManager.ShowMenu();

                        Plugin.Log.Info("PlayFirstSubmitLater: Blocked score submission. Paused.");

                        var allButtons = _pauseMenuManager.GetComponentsInChildren<Button>(true);
                        var continueButton = allButtons.FirstOrDefault(b => b.name == "ContinueButton");

                        if (continueButton && EventSystem.current != null)
                        {
                            EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
                        }
                    }
                    else
                    {
                        ClearPauseGate();
                        return true;
                    }

                    return false; // Stop original execution
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"PlayFirstSubmitLater: Error in Prefix: {ex}");
                return true;
            }
        }
    }
    */

    // PATCH 2: Endless Mode Chaining
    [HarmonyPatch(typeof(MenuTransitionsHelper), "HandleMainGameSceneDidFinish")]
    internal static class EndlessHarmonyPatch
    {
        private const float ChainFadeDurationSeconds = 1.0f;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(
            MenuTransitionsHelper __instance,
            StandardLevelScenesTransitionSetupDataSO standardLevelScenesTransitionSetupData,
            LevelCompletionResults levelCompletionResults)
        {
            var gm = Gameplay.GameplayManager.GetInstance();
            if (gm == null || !gm.IsPlaying() || gm.GetRemainingTime() <= 0f) return true;

            if (levelCompletionResults.levelEndAction == LevelCompletionResults.LevelEndAction.Quit ||
                levelCompletionResults.levelEndAction == LevelCompletionResults.LevelEndAction.Restart ||
                levelCompletionResults.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
            {
                return true;
            }

            try
            {
                var finishedCBField = AccessTools.Field(typeof(MenuTransitionsHelper), "_standardLevelFinishedCallback");
                var finishedCB = finishedCBField.GetValue(__instance);
                (finishedCB as Delegate)?.DynamicInvoke(standardLevelScenesTransitionSetupData, levelCompletionResults);

                if (!gm.TryPrepareNextChain(out var nextLevel, out var nextKey, out var modifiers, out var playerSettings, out var color, out var envs))
                {
                    return true;
                }

                ReplaceScenes(__instance, nextLevel, nextKey, modifiers, playerSettings, color, envs);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"EndlessHarmonyPatch: Error chaining: {ex}");
                return true;
            }
        }

        private static void ClearPreviousPauseState()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        internal static void ReplaceScenes(
            MenuTransitionsHelper helper,
            BeatmapLevel nextLevel,
            BeatmapKey nextKey,
            GameplayModifiers modifiers,
            PlayerSpecificSettings playerSettings,
            ColorScheme color,
            EnvironmentsListModel envs,
            float fade = ChainFadeDurationSeconds)
        {
            if (helper == null || nextLevel == null || envs == null) return;
            ClearPreviousPauseState();

            // Reflection Helpers
            FieldInfo FindField(Type t, params string[] names)
            {
                foreach (var n in names) { var f = AccessTools.Field(t, n); if (f != null) return f; }
                return null;
            }
            T GetFieldValue<T>(object instance, params string[] names) where T : class
            {
                if (instance == null) return null;
                var f = FindField(instance.GetType(), names);
                return f?.GetValue(instance) as T;
            }

            var scenesMgr = GetFieldValue<GameScenesManager>(helper, "_gameScenesManager", "gameScenesManager");
            var audioLoader = GetFieldValue<AudioClipAsyncLoader>(helper, "_audioClipAsyncLoader", "audioClipAsyncLoader");
            var settingsMgr = GetFieldValue<SettingsManager>(helper, "_settingsManager", "settingsManager");
            var dataLoader = GetFieldValue<BeatmapDataLoader>(helper, "_beatmapDataLoader", "beatmapDataLoader");
            var entitlement = GetFieldValue<BeatmapLevelsEntitlementModel>(helper, "_beatmapLevelsEntitlementModel", "beatmapLevelsEntitlementModel");
            var levelsModel = GetFieldValue<BeatmapLevelsModel>(helper, "_beatmapLevelsModel", "beatmapLevelsModel");

            if (scenesMgr == null) return;

            var existingSetup = GetFieldValue<StandardLevelScenesTransitionSetupDataSO>(helper, "_standardLevelScenesTransitionSetupData", "standardLevelScenesTransitionSetupData");
            if (existingSetup == null) return;

            var stdGameplayInfoField = FindField(typeof(StandardLevelScenesTransitionSetupDataSO), "_standardGameplaySceneInfo", "standardGameplaySceneInfo");
            var gameCoreInfoField = FindField(typeof(StandardLevelScenesTransitionSetupDataSO), "_gameCoreSceneInfo", "gameCoreSceneInfo");

            if (stdGameplayInfoField == null || gameCoreInfoField == null) return;

            var existingStdGameplayInfo = stdGameplayInfoField.GetValue(existingSetup) as SceneInfo;
            var existingGameCoreInfo = gameCoreInfoField.GetValue(existingSetup) as SceneInfo;

            var newSetup = ScriptableObject.CreateInstance<StandardLevelScenesTransitionSetupDataSO>();

            stdGameplayInfoField.SetValue(newSetup, existingStdGameplayInfo);
            gameCoreInfoField.SetValue(newSetup, existingGameCoreInfo);

            newSetup.Init(
                "Solo", nextKey, nextLevel, null, color, false, null, modifiers, playerSettings, null, envs,
                audioLoader, dataLoader, settingsMgr, "Menu", levelsModel, entitlement, false, false, null
            );

            LogUtils.Debug(() => $"EndlessHarmonyPatch: Replacing scenes -> {nextLevel.songName}");
            scenesMgr.ReplaceScenes(newSetup, null, fade);
        }
    }
}
