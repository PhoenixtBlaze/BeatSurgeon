using System;

namespace BeatSurgeon.Twitch
{
    public struct EntitlementsSnapshot
    {
        public SupporterTier Tier;
        public DateTime ExpiresAtUtc;

        // Store the signed token that *proved* this snapshot.
        // Server expects this token for premium endpoints.
        public string SignedEntitlementToken;

        public bool IsValid =>
            Tier != SupporterTier.None &&
            !string.IsNullOrEmpty(SignedEntitlementToken) &&
            DateTime.UtcNow < ExpiresAtUtc;
    }

    public static class EntitlementsState
    {
        public static event Action Changed;

        public static EntitlementsSnapshot Current { get; private set; } =
            new EntitlementsSnapshot
            {
                Tier = SupporterTier.None,
                ExpiresAtUtc = DateTime.MinValue,
                SignedEntitlementToken = null
            };

        public static void Set(EntitlementsSnapshot snapshot)
        {
            Current = snapshot;

            // Keep your existing mirror, but this file cannot verify SupporterState exists.
            SupporterState.CurrentTier = snapshot.Tier;

            Changed?.Invoke();
        }

        public static void Clear()
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
