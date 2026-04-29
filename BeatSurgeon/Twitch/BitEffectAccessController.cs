using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Twitch
{
    internal static class BitEffectAccessController
    {
        internal static void ApplyManualToggle(bool enabled)
        {
            PremiumVisualFeatureAccessController.ApplyManualToggle(PremiumVisualFeature.BitEffect, enabled);
        }

        internal static void SyncConfigEnabledState()
        {
            PremiumVisualFeatureAccessController.SyncConfigEnabledState(PremiumVisualFeature.BitEffect);
        }

        internal static async Task EnsureAuthorizedAsync(CancellationToken ct)
        {
            await PremiumVisualFeatureAccessController.EnsureAuthorizedAsync(
                PremiumVisualFeature.BitEffect,
                "Bit effects",
                requiresToggle: true,
                ct: ct).ConfigureAwait(false);
        }
    }
}