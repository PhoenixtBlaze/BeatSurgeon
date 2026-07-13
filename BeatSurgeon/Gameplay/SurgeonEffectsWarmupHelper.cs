namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Shared constants for surgeoneffects menu preload timing.
    /// Pool warmup stays on gameplay entry so VR stereo prep uses in-map references.
    /// </summary>
    internal static class SurgeonEffectsWarmupHelper
    {
        internal const float MenuIdleDelaySeconds = 1.5f;

        internal static void ResetMenuWarmupState()
        {
        }
    }
}
