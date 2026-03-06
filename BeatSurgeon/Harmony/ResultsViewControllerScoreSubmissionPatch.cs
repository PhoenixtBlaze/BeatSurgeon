using HarmonyLib;
using BeatSurgeon.Gameplay;

namespace BeatSurgeon.HarmonyPatches
{
    /// <summary>
    /// Patches ResultsViewController.SetDataToUI with a prefix that runs BEFORE the
    /// BSUtils postfix which appends the "Score Submission Disabled by: ..." message.
    ///
    /// If no speed effect (faster / superfast / slower) was applied during the current
    /// map run, we remove the prolonged-disable keys from BSUtils so the message does
    /// not bleed over to results screens of maps that had no effects applied.
    ///
    /// Priority.High ensures our prefix executes before any other plugin's prefix on
    /// the same method and, more importantly, before the original and BSUtils postfix.
    /// </summary>
    [HarmonyPatch(typeof(ResultsViewController), nameof(ResultsViewController.SetDataToUI), MethodType.Normal)]
    [HarmonyPriority(Priority.High)]
    internal static class ResultsViewControllerScoreSubmissionPatch
    {
        private static void Prefix()
        {
            // If a speed effect was active during this run, leave BSUtils state alone so
            // the message shows correctly on THIS results screen.
            if (FasterSongManager.WasActiveThisRun)
                return;

            // No speed effect this run – scrub the speed-effect keys from BSUtils so the
            // "Score Submission Disabled" banner does not appear for a clean map.
            FasterSongManager.RemoveSpeedSubmissionKeys();
        }
    }
}
