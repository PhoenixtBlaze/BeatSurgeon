using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Shared travel timing for cut-effect text (and matching canvas-bound VFX) moving toward
    /// follower canvas Start. Controlled by <see cref="PluginConfig.TextMovementSpeed"/> in seconds.
    /// </summary>
    internal static class FlyingTextMotionSettings
    {
        internal const float DefaultTravelSeconds = 4.0f;
        internal const float MinTravelSeconds = 0.5f;
        internal const float MaxTravelSeconds = 20f;

        internal static float ResolveTravelSeconds()
        {
            float travelSeconds = Plugin.Settings?.TextMovementSpeed ?? DefaultTravelSeconds;
            return Mathf.Clamp(travelSeconds, MinTravelSeconds, MaxTravelSeconds);
        }
    }
}
