using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Twitch
{
    internal enum PremiumVisualFeature
    {
        BitEffect,
        FollowEffect,
        SubscriberEffect
    }

    internal static class PremiumVisualFeatureAccessController
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("PremiumVisualAccess");
        private static readonly PremiumVisualFeature[] AllFeatures =
        {
            PremiumVisualFeature.BitEffect,
            PremiumVisualFeature.FollowEffect,
            PremiumVisualFeature.SubscriberEffect
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
            if (feature == PremiumVisualFeature.FollowEffect)
            {
                PluginConfig config = PluginConfig.Instance;
                return config != null
                    && HasAuthenticatedVisualsAccess()
                    && config.FollowEffectsEnabled
                    && !string.IsNullOrWhiteSpace(GetCurrentBroadcasterId());
            }

            if (feature == PremiumVisualFeature.SubscriberEffect)
            {
                PluginConfig config = PluginConfig.Instance;
                return config != null
                    && HasAuthenticatedVisualsAccess()
                    && config.SubEffectsEnabled
                    && !string.IsNullOrWhiteSpace(GetCurrentBroadcasterId());
            }

            return false;
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
                allowed = await RefreshVisualsPermissionAsync(ct).ConfigureAwait(false);
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

        internal static async Task<bool> RefreshVisualsPermissionAsync(CancellationToken ct)
        {
            if (!TwitchAuthManager.Instance.IsAuthenticated || TwitchAuthManager.Instance.IsReauthRequired)
            {
                return false;
            }

            EntitlementProvider currentProvider = EntitlementsState.CurrentProvider;
            if (await TryCheckVisualsPermissionAsync(currentProvider, ct).ConfigureAwait(false))
            {
                SyncAllConfigEnabledStates();
                return HasAuthenticatedVisualsAccess();
            }

            if (currentProvider != EntitlementProvider.Patreon &&
                await TryCheckVisualsPermissionAsync(EntitlementProvider.Patreon, ct).ConfigureAwait(false))
            {
                SyncAllConfigEnabledStates();
                return HasAuthenticatedVisualsAccess();
            }

            if (currentProvider != EntitlementProvider.Twitch &&
                await TryCheckVisualsPermissionAsync(EntitlementProvider.Twitch, ct).ConfigureAwait(false))
            {
                SyncAllConfigEnabledStates();
                return HasAuthenticatedVisualsAccess();
            }

            SyncAllConfigEnabledStates();
            return HasAuthenticatedVisualsAccess();
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

        private static async Task<bool> TryCheckVisualsPermissionAsync(EntitlementProvider provider, CancellationToken ct)
        {
            try
            {
                switch (provider)
                {
                    case EntitlementProvider.Patreon:
                        if (!PatreonAuthManager.Instance.IsAuthenticated || PatreonAuthManager.Instance.IsReauthRequired)
                        {
                            return false;
                        }

                        return await PatreonApiClient.Instance.CheckVisualsPermissionAsync(ct).ConfigureAwait(false);
                    case EntitlementProvider.Twitch:
                        if (!TwitchAuthManager.Instance.IsAuthenticated || TwitchAuthManager.Instance.IsReauthRequired)
                        {
                            return false;
                        }

                        return await TwitchApiClient.Instance.CheckVisualsPermissionAsync(ct).ConfigureAwait(false);
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _log.Warn("Visuals permission refresh failed for " + provider + ": " + ex.Message);
                return false;
            }
        }

        private static bool GetEnabled(PluginConfig config, PremiumVisualFeature feature)
        {
            switch (feature)
            {
                case PremiumVisualFeature.BitEffect:
                    return config.BitEffectEnabled;
                case PremiumVisualFeature.FollowEffect:
                    return config.FollowEffectsEnabled;
                case PremiumVisualFeature.SubscriberEffect:
                    return config.SubEffectsEnabled;
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
                case PremiumVisualFeature.SubscriberEffect:
                    config.SubEffectsEnabled = enabled;
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
                case PremiumVisualFeature.SubscriberEffect:
                    return config.SubEffectManualDisabledBroadcasterId ?? string.Empty;
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
                case PremiumVisualFeature.SubscriberEffect:
                    config.SubEffectManualDisabledBroadcasterId = broadcasterId ?? string.Empty;
                    break;
            }
        }
    }
}