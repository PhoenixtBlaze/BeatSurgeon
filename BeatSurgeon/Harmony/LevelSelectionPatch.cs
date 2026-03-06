using System;
using HarmonyLib;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;

namespace BeatSurgeon.HarmonyPatches
{
    /// <summary>
    /// Fires a ranked pre-check the moment the player changes difficulty in the level-detail
    /// menu. Gives the BeatLeader / ScoreSaber API calls a head-start so results are usually
    /// ready before the GameCore scene even loads.
    /// </summary>
    [HarmonyPatch(typeof(StandardLevelDetailViewController), "HandleDidChangeDifficultyBeatmap")]
    internal static class LevelSelectionPatch
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("LevelSelectionPatch");

        static void Postfix(StandardLevelDetailViewController __instance)
        {
            try
            {
                RankedMapDetectionService.Instance.StartPreCheck(__instance.beatmapKey);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "Postfix");
            }
        }
    }
}
