using HarmonyLib;
using BeatSurgeon.Gameplay;

namespace BeatSurgeon.HarmonyPatches
{
    /// <summary>
    /// Patches ResultsViewController.SetDataToUI to ensure the "Score Submission Disabled by: ..."
    /// banner from BSUtils reflects exactly the speed effects used during THIS map run only.
    ///
    /// Strategy: always remove all speed keys first (prevents leakage from prior maps), then
    /// re-add only the keys that were actually used this run so BSUtils renders them correctly.
    /// A Postfix scrubs the keys again so they cannot survive to the next map's results screen.
    /// </summary>
    [HarmonyPatch(typeof(ResultsViewController), nameof(ResultsViewController.SetDataToUI), MethodType.Normal)]
    [HarmonyPriority(Priority.High)]
    internal static class ResultsViewControllerScoreSubmissionPatch
    {
        private static void Prefix()
        {
            // Step 1: Remove all speed keys to prevent prior-map bleed-over.
            FasterSongManager.RemoveSpeedSubmissionKeys();

            // Step 2: Re-add only the keys actually used this run so BSUtils shows the correct lines.
            foreach (string key in FasterSongManager.KeysUsedThisRun)
                BS_Utils.Gameplay.ScoreSubmission.ProlongedDisableSubmission(key);
        }

        private static void Postfix()
        {
            // BSUtils has already read and displayed the keys in its postfix.
            // Scrub them now so they don't leak into the next results screen.
            FasterSongManager.RemoveSpeedSubmissionKeys();
        }
    }
}
