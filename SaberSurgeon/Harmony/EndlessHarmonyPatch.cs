using System;
using System.Linq;
using HarmonyLib;


namespace SaberSurgeon.HarmonyPatches
{
    [HarmonyPatch(typeof(MenuTransitionsHelper))]
    internal static class EndlessHarmonyPatch
    {

        private const float ChainFadeDurationSeconds = 0.6f; // try 0.4–1.0

        // Intercept the standard-level finish handler that normally pops back to menu.

        // Signature from your 1.40.8 decompile:

        // private void HandleMainGameSceneDidFinish(StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults)

        [HarmonyPrefix]
        [HarmonyPatch("HandleMainGameSceneDidFinish")]
        private static bool Prefix(
            MenuTransitionsHelper __instance,
            StandardLevelScenesTransitionSetupDataSO standardLevelScenesTransitionSetupData,
            LevelCompletionResults levelCompletionResults)
        {
            var gm = Gameplay.GameplayManager.GetInstance();
            // Replace gameplay scenes with a fade instead of popping to menu.
            float fade = ChainFadeDurationSeconds;

            // Only chain if endless is still running, time remains, and user didn't quit to menu.
            if (!gm.IsPlaying() ||
                gm.GetRemainingTime() <= 0f ||
                levelCompletionResults.levelEndAction == LevelCompletionResults.LevelEndAction.Quit)
                return true; // run original: pops to menu then invokes finished callback

            try
            {
                // Let the existing "level finished" callback run so GameplayManager updates its state.
                var finishedCBField = AccessTools.Field(typeof(MenuTransitionsHelper), "_standardLevelFinishedCallback");
                var finishedCB = (Action<StandardLevelScenesTransitionSetupDataSO, LevelCompletionResults>)finishedCBField.GetValue(__instance);
                finishedCB?.Invoke(standardLevelScenesTransitionSetupData, levelCompletionResults);

                // Ask GameplayManager for the next target beatmap + captured settings.
                if (!gm.TryPrepareNextChain(
                        out BeatmapLevel nextLevel,
                        out BeatmapKey nextKey,
                        out GameplayModifiers modifiers,
                        out PlayerSpecificSettings playerSettings,
                        out ColorScheme color,
                        out EnvironmentsListModel envs))
                {
                    Plugin.Log.Warn("EndlessHarmonyPatch: No next level available; allowing menu pop.");
                    return true; // run original to return to menu safely
                }

                // Pull required private services/fields from MenuTransitionsHelper to re-init the setup data.
                var audioLoader = (AudioClipAsyncLoader)AccessTools
                    .Field(typeof(MenuTransitionsHelper), "_audioClipAsyncLoader")
                    .GetValue(__instance);

                var settingsMgr = (SettingsManager)AccessTools
                    .Field(typeof(MenuTransitionsHelper), "_settingsManager")
                    .GetValue(__instance);

                var dataLoader = (BeatmapDataLoader)AccessTools
                    .Field(typeof(MenuTransitionsHelper), "_beatmapDataLoader")
                    .GetValue(__instance);

                var entitlement = (BeatmapLevelsEntitlementModel)AccessTools
                    .Field(typeof(MenuTransitionsHelper), "_beatmapLevelsEntitlementModel")
                    .GetValue(__instance);

                var levelsModel = (BeatmapLevelsModel)AccessTools
                    .Field(typeof(MenuTransitionsHelper), "_beatmapLevelsModel")
                    .GetValue(__instance);

                var scenesMgr = (GameScenesManager)AccessTools
                    .Field(typeof(MenuTransitionsHelper), "_gameScenesManager")
                    .GetValue(__instance);

                // Reinitialize the same StandardLevel setup data for the next map (matches decompiled Init signature).
                standardLevelScenesTransitionSetupData.Init(
                    gameMode: "Solo",
                    in nextKey,
                    nextLevel,
                    overrideEnvironmentSettings: null,
                    playerOverrideColorScheme: color,
                    playerOverrideLightshowColors: false,
                    beatmapOverrideColorScheme: null,
                    gameplayModifiers: modifiers,
                    playerSpecificSettings: playerSettings,
                    practiceSettings: null,
                    environmentsListModel: envs,
                    audioClipAsyncLoader: audioLoader,
                    settingsManager: settingsMgr,
                    backButtonText: "Menu",
                    useTestNoteCutSoundEffects: false,
                    startPaused: false,
                    beatmapLevelsModel: levelsModel,
                    beatmapDataLoader: dataLoader,
                    beatmapLevelsEntitlementModel: entitlement,
                    recordingToolData: null
                );

                // Replace gameplay scenes with a fade instead of popping to menu.
                Plugin.Log.Info($"EndlessHarmonyPatch: Chaining to next map with fade={fade:0.00}s");
                scenesMgr.ReplaceScenes(standardLevelScenesTransitionSetupData, null, fade);

                // Skip original handler (prevents PopScenes-to-menu).
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"EndlessHarmonyPatch: Exception while chaining:\n{ex}");
                // Fall back to original behavior (menu pop) if anything goes wrong.
                return true;
            }
        }
    }
}
