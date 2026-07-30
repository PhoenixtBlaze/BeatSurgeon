using System.Threading;
using System.Threading.Tasks;

namespace BeatSurgeon.Twitch
{
    internal static class RaidEffectAccessController
    {
        internal static bool IsToggleInteractable =>
            PremiumVisualFeatureAccessController.IsToggleInteractable(PremiumVisualFeature.RaidEffect);

        internal static bool ShouldMaintainSubscription() =>
            PremiumVisualFeatureAccessController.ShouldMaintainSubscription(PremiumVisualFeature.RaidEffect);

        internal static void ApplyManualToggle(bool enabled)
        {
            PremiumVisualFeatureAccessController.ApplyManualToggle(PremiumVisualFeature.RaidEffect, enabled);
        }

        internal static void SyncConfigEnabledState()
        {
            PremiumVisualFeatureAccessController.SyncConfigEnabledState(PremiumVisualFeature.RaidEffect);
        }

        internal static Task EnsureAuthorizedAsync(CancellationToken ct)
        {
            return PremiumVisualFeatureAccessController.EnsureAuthorizedAsync(
                PremiumVisualFeature.RaidEffect,
                "Raid effects",
                requiresToggle: true,
                ct: ct);
        }

        internal static Task EnsureAutomaticEffectAuthorizedAsync(CancellationToken ct)
        {
            return PremiumVisualFeatureAccessController.EnsureAutomaticEffectAuthorizedAsync(
                PremiumVisualFeature.RaidEffect,
                "Raid effects",
                requiresToggle: true,
                ct: ct);
        }
    }
}
