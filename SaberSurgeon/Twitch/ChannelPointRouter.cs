using System;

namespace SaberSurgeon.Twitch
{
    internal static class ChannelPointRouter
    {
        public static string TryBuildCommandFromReward(global::SaberSurgeon.Twitch.TwitchEventSubClient.ChannelPointRedemption r)
        {
            if (r == null) return null;

            var cfg = global::SaberSurgeon.Plugin.Settings;
            if (cfg == null) return null;

            bool MatchId(string expectedId) =>
    !string.IsNullOrEmpty(expectedId) && r.RewardId == expectedId;

            if (cfg.CpRainbowEnabled && MatchId(cfg.CpRainbowRewardId ?? "")) return "!rainbow";
            if (cfg.CpDisappearEnabled && MatchId(cfg.CpDisappearRewardId ?? "")) return "!disappear";
            if (cfg.CpGhostEnabled && MatchId(cfg.CpGhostRewardId ?? "")) return "!ghost";
            if (cfg.CpBombEnabled && MatchId(cfg.CpBombRewardId ?? "")) return "!" + (global::SaberSurgeon.Chat.CommandHandler.BombCommandName ?? "bomb");
            if (cfg.CpFasterEnabled && MatchId(cfg.CpFasterRewardId ?? "")) return "!faster";
            if (cfg.CpSuperFastEnabled && MatchId(cfg.CpSuperFastRewardId ?? "")) return "!superfast";
            if (cfg.CpSlowerEnabled && MatchId(cfg.CpSlowerRewardId ?? "")) return "!slower";
            if (cfg.CpFlashbangEnabled && MatchId(cfg.CpFlashbangRewardId ?? "")) return "!flashbang";


            return null;
        }
    }
}
