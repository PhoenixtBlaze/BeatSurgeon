namespace BeatSurgeon.Twitch
{
    public enum SupporterTier
    {
        None = 0,
        Tier1 = 1,
        Tier2 = 2,
        Tier3 = 3
    }

    public static class SupporterState
    {
        public static SupporterTier CurrentTier { get; set; } = SupporterTier.None;
    }
}
