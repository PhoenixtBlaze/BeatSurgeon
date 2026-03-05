using BeatSurgeon.Utils;

namespace BeatSurgeon.Twitch
{
    internal enum SupporterTier
    {
        None = 0,
        Tier1 = 1,
        Tier2 = 2,
        Tier3 = 3
    }

    internal static class SupporterState
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SupporterState");
        private static SupporterTier _currentTier = SupporterTier.None;

        internal static SupporterTier CurrentTier
        {
            get => _currentTier;
            set
            {
                if (_currentTier == value) return;
                _currentTier = value;
                _log.Info("SupporterState.CurrentTier changed -> " + value);
            }
        }
    }
}
