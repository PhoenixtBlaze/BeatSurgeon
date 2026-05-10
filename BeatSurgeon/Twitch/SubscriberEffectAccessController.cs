using System.Threading;
using System.Threading.Tasks;

namespace BeatSurgeon.Twitch
{
    internal static class SubscriberEffectAccessController
    {
        internal static bool IsToggleInteractable =>
            PremiumVisualFeatureAccessController.IsToggleInteractable(PremiumVisualFeature.SubscriberEffect);

        internal static bool ShouldMaintainSubscription() =>
            PremiumVisualFeatureAccessController.ShouldMaintainSubscription(PremiumVisualFeature.SubscriberEffect);

        internal static void ApplyManualToggle(bool enabled)
        {
            PremiumVisualFeatureAccessController.ApplyManualToggle(PremiumVisualFeature.SubscriberEffect, enabled);
        }

        internal static void SyncConfigEnabledState()
        {
            PremiumVisualFeatureAccessController.SyncConfigEnabledState(PremiumVisualFeature.SubscriberEffect);
        }

        internal static Task EnsureAuthorizedAsync(CancellationToken ct)
        {
            return PremiumVisualFeatureAccessController.EnsureAuthorizedAsync(
                PremiumVisualFeature.SubscriberEffect,
                "Subscriber effects",
                requiresToggle: true,
                ct: ct);
        }
    }
}
