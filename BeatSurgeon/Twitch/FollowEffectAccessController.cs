using System.Threading;
using System.Threading.Tasks;

namespace BeatSurgeon.Twitch
{
    internal static class FollowEffectAccessController
    {
        internal static bool IsToggleInteractable =>
            PremiumVisualFeatureAccessController.IsToggleInteractable(PremiumVisualFeature.FollowEffect);

        internal static bool ShouldMaintainSubscription() =>
            PremiumVisualFeatureAccessController.ShouldMaintainSubscription(PremiumVisualFeature.FollowEffect);

        internal static void ApplyManualToggle(bool enabled)
        {
            PremiumVisualFeatureAccessController.ApplyManualToggle(PremiumVisualFeature.FollowEffect, enabled);
        }

        internal static void SyncConfigEnabledState()
        {
            PremiumVisualFeatureAccessController.SyncConfigEnabledState(PremiumVisualFeature.FollowEffect);
        }

        internal static Task EnsureAuthorizedAsync(CancellationToken ct)
        {
            return PremiumVisualFeatureAccessController.EnsureAuthorizedAsync(
                PremiumVisualFeature.FollowEffect,
                "Follow effects",
                requiresToggle: true,
                ct: ct);
        }
    }
}