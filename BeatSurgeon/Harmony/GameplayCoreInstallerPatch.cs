using System;
using HarmonyLib;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.HarmonyPatches
{
    /// <summary>
    /// Confirms (or starts) the ranked check the moment GameCore finishes installing its
    /// Zenject bindings. If the menu pre-check already ran for the same hash this is a
    /// cheap no-op. If the player clicked Play before a menu event fired, this starts the
    /// check from scratch — commands are blocked until the result arrives.
    /// </summary>
    [HarmonyPatch(typeof(GameplayCoreInstaller), "InstallBindings")]
    internal static class GameplayCoreInstallerPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("GameplayCoreInstallerPatch");

        static void Postfix(GameplayCoreInstaller __instance)
        {
            try
            {
                var sceneSetupData = Traverse.Create(__instance)
                    .Field("_sceneSetupData")
                    .GetValue<GameplayCoreSceneSetupData>();

                if (sceneSetupData == null)
                {
                    _log.Warn("_sceneSetupData was null – cannot start ranked confirmation check");
                    return;
                }

                RankedMapDetectionService.Instance.OnGameCoreLoaded(sceneSetupData.beatmapKey);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }
    }
}
