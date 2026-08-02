using System;
using BeatSurgeon.Gameplay;

namespace BeatSurgeon
{
    /// <summary>
    /// Checks whether a long-running effect is already active locally. Used by CommandHandler to
    /// reject a Multiplayer+ client's own chat/CP from re-running a duration effect that is already
    /// running. Bit cheers and EventSub follow/sub (AutomaticEvent) are intentionally not gated
    /// here so MP clients still play full local glitter/follow/sub visuals from their own chat.
    /// </summary>
    internal static class MultiplayerLocalEffectGate
    {
        internal static bool IsEffectAlreadyActive(string commandKey)
        {
            if (string.IsNullOrWhiteSpace(commandKey))
            {
                return false;
            }

            switch (commandKey.Trim().ToLowerInvariant())
            {
                case "rainbow":
                case "notecolor":
                    return RainbowManager.RainbowActive || RainbowManager.NoteColorActive;
                case "ghost":
                    return GhostNotesManager.GhostActive;
                case "disappear":
                    return DisappearingArrowsManager.DisappearingActive;
                case "faster":
                case "superfast":
                case "slower":
                    return FasterSongManager.IsAnyActive;
                case "flashbang":
                    return FlashbangManager.FlashbangActive;
                default:
                    // Instant effects (bomb/glitter/raid/fmsg/smsg/subcubes) are not blocked —
                    // chat/CP may still be limited by cooldowns, and EventSub/bits are ungated.
                    return false;
            }
        }
    }
}
