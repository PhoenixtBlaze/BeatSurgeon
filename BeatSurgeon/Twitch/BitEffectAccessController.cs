using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Chat;

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

        internal static async Task EnsureAuthorizedAsync(ChatContext ctx, CancellationToken ct)
        {
            await PremiumVisualFeatureAccessController.EnsureAuthorizedAsync(
                PremiumVisualFeature.BitEffect,
                "Bit effects",
                requiresToggle: true,
                ctx: ctx,
                ct: ct).ConfigureAwait(false);
        }

        internal static Task EnsureAutomaticEffectAuthorizedAsync(CancellationToken ct)
        {
            return PremiumVisualFeatureAccessController.EnsureAutomaticEffectAuthorizedAsync(
                PremiumVisualFeature.BitEffect,
                "Bit effects",
                requiresToggle: true,
                ct: ct);
        }
    }
}