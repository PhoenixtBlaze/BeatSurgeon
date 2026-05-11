using System;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Twitch
{
    internal enum EntitlementProvider
    {
        None = 0,
        Twitch = 1,
        Patreon = 2
    }

    internal struct EntitlementsSnapshot
    {
        internal SupporterTier Tier;
        internal DateTime ExpiresAtUtc;
        internal string SignedEntitlementToken;

        internal bool IsValid =>
            Tier != SupporterTier.None &&
            !string.IsNullOrEmpty(SignedEntitlementToken) &&
            DateTime.UtcNow < ExpiresAtUtc;
    }

    internal static class EntitlementsState
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("EntitlementsState");
        private static readonly EntitlementsSnapshot EmptySnapshot = new EntitlementsSnapshot
        {
            Tier = SupporterTier.None,
            ExpiresAtUtc = DateTime.MinValue,
            SignedEntitlementToken = null
        };

        internal static event Action Changed;

        private static EntitlementsSnapshot _twitch = EmptySnapshot;
        private static EntitlementsSnapshot _patreon = EmptySnapshot;
        private static EntitlementsSnapshot _current = EmptySnapshot;
        private static EntitlementProvider _currentProvider = EntitlementProvider.None;

        internal static EntitlementsSnapshot Current => _current;
        internal static EntitlementProvider CurrentProvider => _currentProvider;

        internal static EntitlementsSnapshot Get(EntitlementProvider provider)
        {
            switch (provider)
            {
                case EntitlementProvider.Twitch:
                    return _twitch;
                case EntitlementProvider.Patreon:
                    return _patreon;
                default:
                    return EmptySnapshot;
            }
        }

        internal static bool HasVisualsAccess
        {
            get
            {
                EntitlementsSnapshot snapshot = _current;
                return snapshot.IsValid && snapshot.Tier >= SupporterTier.Tier1;
            }
        }

        internal static bool HasVisualsAccessFor(EntitlementProvider provider)
        {
            EntitlementsSnapshot snapshot = Get(provider);
            return snapshot.IsValid && snapshot.Tier >= SupporterTier.Tier1;
        }

        internal static void Set(EntitlementProvider provider, EntitlementsSnapshot snapshot)
        {
            switch (provider)
            {
                case EntitlementProvider.Twitch:
                    _twitch = snapshot;
                    break;
                case EntitlementProvider.Patreon:
                    _patreon = snapshot;
                    break;
                default:
                    return;
            }

            RecomputeEffectiveState();
        }

        internal static void Clear(EntitlementProvider provider)
        {
            Set(provider, EmptySnapshot);
        }

        internal static void ClearAll()
        {
            _twitch = EmptySnapshot;
            _patreon = EmptySnapshot;
            RecomputeEffectiveState();
        }

        private static void RecomputeEffectiveState()
        {
            EntitlementProvider effectiveProvider = EntitlementProvider.None;
            EntitlementsSnapshot effectiveSnapshot = EmptySnapshot;

            ChooseEffectiveSnapshot(ref effectiveProvider, ref effectiveSnapshot, EntitlementProvider.Twitch, _twitch);
            ChooseEffectiveSnapshot(ref effectiveProvider, ref effectiveSnapshot, EntitlementProvider.Patreon, _patreon);

            bool changed = _currentProvider != effectiveProvider ||
                           _current.Tier != effectiveSnapshot.Tier ||
                           _current.ExpiresAtUtc != effectiveSnapshot.ExpiresAtUtc ||
                           !string.Equals(_current.SignedEntitlementToken, effectiveSnapshot.SignedEntitlementToken, StringComparison.Ordinal);

            _currentProvider = effectiveProvider;
            _current = effectiveSnapshot;
            SupporterState.CurrentTier = effectiveSnapshot.Tier;

            if (PluginConfig.Instance != null)
            {
                PluginConfig.Instance.CachedSupporterTier = (int)effectiveSnapshot.Tier;
            }

            if (changed)
            {
                _log.Info("EntitlementsState changed -> Provider=" + effectiveProvider + " Tier=" + effectiveSnapshot.Tier + " ExpiresAt=" + effectiveSnapshot.ExpiresAtUtc.ToString("u"));
                Changed?.Invoke();
            }
        }

        private static void ChooseEffectiveSnapshot(
            ref EntitlementProvider effectiveProvider,
            ref EntitlementsSnapshot effectiveSnapshot,
            EntitlementProvider candidateProvider,
            EntitlementsSnapshot candidateSnapshot)
        {
            if (!candidateSnapshot.IsValid)
            {
                return;
            }

            if (!effectiveSnapshot.IsValid ||
                candidateSnapshot.Tier > effectiveSnapshot.Tier ||
                (candidateSnapshot.Tier == effectiveSnapshot.Tier && candidateSnapshot.ExpiresAtUtc > effectiveSnapshot.ExpiresAtUtc))
            {
                effectiveProvider = candidateProvider;
                effectiveSnapshot = candidateSnapshot;
            }
        }
    }
}
