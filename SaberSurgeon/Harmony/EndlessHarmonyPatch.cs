using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace SaberSurgeon.HarmonyPatches
{

    // ==============================================================================================
    // PATCH 1: PlayFirstSubmitLater
    // Blocks the "Finish" event so ScoreSaber/BeatLeader never hear about the map ending until we say so.
    // ==============================================================================================
    [HarmonyPatch(typeof(StandardLevelScenesTransitionSetupDataSO), "Finish")]
    internal static class PlayFirstSubmitLaterPatch
    {
        private static bool _pauseGateActive;
        private static bool _bypassPauseGateCall;

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

            var s = Plugin.Settings;
            if (s == null) return false;
            if (!s.PlayFirstSubmitLaterEnabled || !s.AutoPauseOnMapEnd) return false;

            var gm = Gameplay.GameplayManager.GetInstance();
            // If Endless Mode is running and actively chaining, we DO NOT pause-gate.
            // Endless mode handles its own transitions.
            if (gm != null && gm.IsPlaying() && gm.GetRemainingTime() > 0f) return false;

            if (results.levelEndAction == LevelCompletionResults.LevelEndAction.Quit ||
                results.levelEndAction == LevelCompletionResults.LevelEndAction.Restart ||
                results.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
            {
                return false;
            }

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
            _continueDelegate = _resumeFinishedDelegate = _menuDelegate = _restartDelegate = null;
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
                // Re-call Finish(). This time bypass is true, so it falls through to original code.
                _pendingSetup.Finish(_pendingResults);
            }
            finally
            {
                _bypassPauseGateCall = false;
                ClearPauseGate();
            }
        }

        private static void HandleAbort()
        {
            // If aborted, we just leave the pause gate active/cleared and let the game handle the Menu/Restart action
            // normally triggered by the PauseMenu itself.
            ClearPauseGate();
        }

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

                        var continueButton = _pauseMenuManager.transform.Find("Wrapper/Container/Buttons/ContinueButton")?.GetComponent<Button>();
                        if (continueButton && EventSystem.current != null)
                            EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
                    }
                    else
                    {
                        ClearPauseGate();
                        return true;
                    }

                    return false; // STOP execution.
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


    // ==============================================================================================
    // PATCH 2: Endless Mode Chaining
    // Handles replacing scenes for endless mode when the level is "officially" finished.
    // ==============================================================================================
    [HarmonyPatch(typeof(MenuTransitionsHelper), "HandleMainGameSceneDidFinish")]
    internal static class EndlessHarmonyPatch
    {
        private const float ChainFadeDurationSeconds = 1.0f;

        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(MenuTransitionsHelper __instance, StandardLevelScenesTransitionSetupDataSO standardLevelScenesTransitionSetupData, LevelCompletionResults levelCompletionResults)
        {
            var gm = Gameplay.GameplayManager.GetInstance();
            if (gm == null || !gm.IsPlaying() || gm.GetRemainingTime() <= 0f) return true;

            // If we failed or quit, don't chain
            if (levelCompletionResults.levelEndAction == LevelCompletionResults.LevelEndAction.Quit ||
                levelCompletionResults.levelEndAction == LevelCompletionResults.LevelEndAction.Restart ||
                levelCompletionResults.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed)
            {
                return true;
            }

            try
            {
                // We manually fire the finish callback so other mods (that hook this) know the level is done.
                // Note: PlayFirstSubmitLater above might have delayed this call, but now we are here.
                var finishedCBField = AccessTools.Field(typeof(MenuTransitionsHelper), "_standardLevelFinishedCallback");
                var finishedCB = finishedCBField.GetValue(__instance);
                (finishedCB as Delegate)?.DynamicInvoke(standardLevelScenesTransitionSetupData, levelCompletionResults);

                if (!gm.TryPrepareNextChain(out var nextLevel, out var nextKey, out var modifiers, out var playerSettings, out var color, out var envs))
                {
                    return true;
                }

                ReplaceScenes(__instance, nextLevel, nextKey, modifiers, playerSettings, color, envs);
                return false; // STOP the game from going to the menu/results screen
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

        internal static void ReplaceScenes(MenuTransitionsHelper helper, BeatmapLevel nextLevel, BeatmapKey nextKey, GameplayModifiers modifiers, PlayerSpecificSettings playerSettings, ColorScheme color, EnvironmentsListModel envs, float fade = ChainFadeDurationSeconds)
        {
            // (Same implementation as before)
            // ... Copy your ReplaceScenes implementation here ...
            // For brevity, I am assuming you have the method body from previous steps.
            // Just paste the method body from the previous "EndlessHarmonyPatch" here.
            if (helper == null || nextLevel == null || envs == null) return;
            ClearPreviousPauseState();

            // Reflection helpers
            FieldInfo FindField(Type t, params string[] names)
            {
                foreach (var n in names)
                {
                    var f = AccessTools.Field(t, n);
                    if (f != null) return f;
                }
                return null;
            }

            T GetFieldValue<T>(object instance, params string[] names) where T : class
            {
                if (instance == null) return null;
                var f = FindField(instance.GetType(), names);
                return f?.GetValue(instance) as T;
            }

            var scenesMgr = GetFieldValue<GameScenesManager>(helper, "gameScenesManager", "_gameScenesManager");
            var audioLoader = GetFieldValue<AudioClipAsyncLoader>(helper, "audioClipAsyncLoader", "_audioClipAsyncLoader");
            var settingsMgr = GetFieldValue<SettingsManager>(helper, "settingsManager", "_settingsManager");
            var dataLoader = GetFieldValue<BeatmapDataLoader>(helper, "beatmapDataLoader", "_beatmapDataLoader");
            var entitlement = GetFieldValue<BeatmapLevelsEntitlementModel>(helper, "beatmapLevelsEntitlementModel", "_beatmapLevelsEntitlementModel");
            var levelsModel = GetFieldValue<BeatmapLevelsModel>(helper, "beatmapLevelsModel", "_beatmapLevelsModel");

            if (scenesMgr == null) return;

            var existingSetup = GetFieldValue<StandardLevelScenesTransitionSetupDataSO>(
                helper, "standardLevelScenesTransitionSetupData", "_standardLevelScenesTransitionSetupData");

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
                gameMode: "Solo",
                beatmapKey: nextKey,
                beatmapLevel: nextLevel,
                overrideEnvironmentSettings: null,
                playerOverrideColorScheme: color,
                playerOverrideLightshowColors: false,
                beatmapOverrideColorScheme: null,
                gameplayModifiers: modifiers,
                playerSpecificSettings: playerSettings,
                practiceSettings: null,
                environmentsListModel: envs,
                audioClipAsyncLoader: audioLoader,
                beatmapDataLoader: dataLoader,
                settingsManager: settingsMgr,
                backButtonText: "Menu",
                beatmapLevelsModel: levelsModel,
                beatmapLevelsEntitlementModel: entitlement,
                useTestNoteCutSoundEffects: false,
                startPaused: false,
                recordingToolData: null
            );

            Plugin.Log.Info($"EndlessHarmonyPatch: Replacing scenes -> {nextLevel.songName}");
            scenesMgr.ReplaceScenes(newSetup, null, fade);
        }
    }
}
