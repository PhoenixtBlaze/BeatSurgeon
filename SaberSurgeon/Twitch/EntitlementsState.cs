using System;

namespace SaberSurgeon.Twitch
{
    public struct EntitlementsSnapshot
    {
        public SupporterTier Tier;
        public DateTime ExpiresAtUtc;
        public bool IsValid => Tier != SupporterTier.None && DateTime.UtcNow < ExpiresAtUtc;
    }

    public static class EntitlementsState
    {
        public static event Action Changed;

        public static EntitlementsSnapshot Current { get; private set; } =
            new EntitlementsSnapshot { Tier = SupporterTier.None, ExpiresAtUtc = DateTime.MinValue };

        public static void Set(EntitlementsSnapshot snapshot)
        {
            Current = snapshot;
            SupporterState.CurrentTier = snapshot.Tier; // optional: keep existing tier mirror
            Changed?.Invoke();
        }

        public static void Clear()
        {
            Set(new EntitlementsSnapshot { Tier = SupporterTier.None, ExpiresAtUtc = DateTime.MinValue });
        }
    }
}
