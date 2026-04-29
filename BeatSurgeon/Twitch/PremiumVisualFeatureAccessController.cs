using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Twitch
{
    internal enum PremiumVisualFeature
    {
        BitEffect,
        FollowEffect
    }

    internal static class PremiumVisualFeatureAccessController
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("PremiumVisualAccess");
        private static readonly PremiumVisualFeature[] AllFeatures =
        {
            PremiumVisualFeature.BitEffect,
            PremiumVisualFeature.FollowEffect
        };

        internal static void ApplyManualToggle(PremiumVisualFeature feature, bool enabled)
        {
            PluginConfig config = PluginConfig.Instance;
            if (config == null)
            {
                return;
            }

            string broadcasterId = GetCurrentBroadcasterId();
            bool hasAccess = HasAuthenticatedVisualsAccess();

            if (!enabled)
            {
                SetEnabled(config, feature, false);
                if (hasAccess && !string.IsNullOrWhiteSpace(broadcasterId))
                {
                    SetManualDisabledBroadcasterId(config, feature, broadcasterId);
                }

                return;
            }

            if (!hasAccess)
            {
                SyncConfigEnabledState(feature);
                return;
            }

            SetEnabled(config, feature, true);
            if (!string.IsNullOrWhiteSpace(broadcasterId) &&
                string.Equals(GetManualDisabledBroadcasterId(config, feature), broadcasterId, StringComparison.Ordinal))
            {
                SetManualDisabledBroadcasterId(config, feature, string.Empty);
            }
        }

        internal static void SyncConfigEnabledState(PremiumVisualFeature feature)
        {
            PluginConfig config = PluginConfig.Instance;
            if (config == null)
            {
                return;
            }

            if (!HasAuthenticatedVisualsAccess())
            {
                return;
            }

            string broadcasterId = GetCurrentBroadcasterId();
            bool manuallyDisabledForCurrentBroadcaster =
                !string.IsNullOrWhiteSpace(broadcasterId) &&
                string.Equals(GetManualDisabledBroadcasterId(config, feature), broadcasterId, StringComparison.Ordinal);

            SetEnabled(config, feature, !manuallyDisabledForCurrentBroadcaster);
        }

        internal static void SyncAllConfigEnabledStates()
        {
            for (int index = 0; index < AllFeatures.Length; index++)
            {
                SyncConfigEnabledState(AllFeatures[index]);
            }
        }

        internal static bool IsToggleInteractable(PremiumVisualFeature feature)
        {
            return HasAuthenticatedVisualsAccess();
        }

        internal static bool ShouldMaintainSubscription(PremiumVisualFeature feature)
        {
            if (feature != PremiumVisualFeature.FollowEffect)
            {
                return false;
            }

            PluginConfig config = PluginConfig.Instance;
            return config != null
                && HasAuthenticatedVisualsAccess()
                && config.FollowEffectsEnabled
                && !string.IsNullOrWhiteSpace(GetCurrentBroadcasterId());
        }

        internal static async Task EnsureAuthorizedAsync(
            PremiumVisualFeature feature,
            string featureDisplayName,
            bool requiresToggle,
            CancellationToken ct)
        {
            PluginConfig config = PluginConfig.Instance;
            if (config == null)
            {
                throw new InvalidOperationException(featureDisplayName + " are unavailable because the plugin configuration is not ready.");
            }

            if (!TwitchAuthManager.Instance.IsAuthenticated || TwitchAuthManager.Instance.IsReauthRequired)
            {
                throw new InvalidOperationException(featureDisplayName + " require a logged-in Twitch account.");
            }

            bool allowed = HasAuthenticatedVisualsAccess();
            if (!allowed)
            {
                try
                {
                    allowed = await TwitchApiClient.Instance.CheckVisualsPermissionAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warn(featureDisplayName + " permission refresh failed: " + ex.Message);
                }
            }

            SyncAllConfigEnabledStates();
            if (!allowed || !HasAuthenticatedVisualsAccess())
            {
                throw new InvalidOperationException(featureDisplayName + " require an active Tier 1+ entitlement.");
            }

            if (requiresToggle && !GetEnabled(config, feature))
            {
                throw new InvalidOperationException(featureDisplayName + " are disabled in the Supporter tab.");
            }
        }

        internal static bool HasAuthenticatedVisualsAccess()
        {
            return TwitchAuthManager.Instance.IsAuthenticated
                && !TwitchAuthManager.Instance.IsReauthRequired
                && EntitlementsState.HasVisualsAccess;
        }

        internal static string GetCurrentBroadcasterId()
        {
            string runtimeId = TwitchAuthManager.Instance.BroadcasterId;
            if (!string.IsNullOrWhiteSpace(runtimeId))
            {
                return runtimeId;
            }

            return PluginConfig.Instance?.CachedBroadcasterId ?? string.Empty;
        }

        private static bool GetEnabled(PluginConfig config, PremiumVisualFeature feature)
        {
            switch (feature)
            {
                case PremiumVisualFeature.BitEffect:
                    return config.BitEffectEnabled;
                case PremiumVisualFeature.FollowEffect:
                    return config.FollowEffectsEnabled;
                default:
                    return false;
            }
        }

        private static void SetEnabled(PluginConfig config, PremiumVisualFeature feature, bool enabled)
        {
            switch (feature)
            {
                case PremiumVisualFeature.BitEffect:
                    config.BitEffectEnabled = enabled;
                    break;
                case PremiumVisualFeature.FollowEffect:
                    config.FollowEffectsEnabled = enabled;
                    break;
            }
        }

        private static string GetManualDisabledBroadcasterId(PluginConfig config, PremiumVisualFeature feature)
        {
            switch (feature)
            {
                case PremiumVisualFeature.BitEffect:
                    return config.BitEffectManualDisabledBroadcasterId ?? string.Empty;
                case PremiumVisualFeature.FollowEffect:
                    return config.FollowEffectManualDisabledBroadcasterId ?? string.Empty;
                default:
                    return string.Empty;
            }
        }

        private static void SetManualDisabledBroadcasterId(PluginConfig config, PremiumVisualFeature feature, string broadcasterId)
        {
            switch (feature)
            {
                case PremiumVisualFeature.BitEffect:
                    config.BitEffectManualDisabledBroadcasterId = broadcasterId ?? string.Empty;
                    break;
                case PremiumVisualFeature.FollowEffect:
                    config.FollowEffectManualDisabledBroadcasterId = broadcasterId ?? string.Empty;
                    break;
            }
        }
    }
}