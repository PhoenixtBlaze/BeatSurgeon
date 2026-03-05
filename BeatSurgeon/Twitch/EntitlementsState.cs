using System;
using BeatSurgeon.Utils;

namespace BeatSurgeon.Twitch
{
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

        internal static event Action Changed;

        private static EntitlementsSnapshot _current = new EntitlementsSnapshot
        {
            Tier = SupporterTier.None,
            ExpiresAtUtc = DateTime.MinValue,
            SignedEntitlementToken = null
        };

        internal static EntitlementsSnapshot Current => _current;

        internal static bool HasVisualsAccess
        {
            get
            {
                EntitlementsSnapshot snapshot = _current;
                return snapshot.IsValid && snapshot.Tier >= SupporterTier.Tier1;
            }
        }

        internal static void Set(EntitlementsSnapshot snapshot)
        {
            bool changed = _current.Tier != snapshot.Tier ||
                           _current.ExpiresAtUtc != snapshot.ExpiresAtUtc ||
                           !string.Equals(_current.SignedEntitlementToken, snapshot.SignedEntitlementToken, StringComparison.Ordinal);

            _current = snapshot;
            SupporterState.CurrentTier = snapshot.Tier;

            if (changed)
            {
                _log.Info("EntitlementsState changed -> Tier=" + snapshot.Tier + " ExpiresAt=" + snapshot.ExpiresAtUtc.ToString("u"));
                Changed?.Invoke();
            }
        }

        internal static void Clear()
        {
            Set(new EntitlementsSnapshot
            {
                Tier = SupporterTier.None,
                ExpiresAtUtc = DateTime.MinValue,
                SignedEntitlementToken = null
            });
        }
    }
}
