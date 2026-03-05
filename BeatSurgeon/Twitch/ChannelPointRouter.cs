using System;
using BeatSurgeon.Chat;
using BeatSurgeon.Utils;
using Zenject;

namespace BeatSurgeon.Twitch
{
    internal sealed class ChannelPointRouter
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("ChannelPointRouter");
        private static ChannelPointRouter _instance;

        [Inject]
        public ChannelPointRouter()
        {
            _instance = this;
        }

        internal bool TryGetCommand(string rewardId, out string command)
        {
            command = null;
            if (string.IsNullOrWhiteSpace(rewardId))
            {
                _log.Warn("TryGetCommand called with empty rewardId");
                return false;
            }

            PluginConfig cfg = PluginConfig.Instance;
            if (cfg == null)
            {
                _log.Warn("TryGetCommand: PluginConfig unavailable");
                return false;
            }

            bool Match(string expected) => !string.IsNullOrWhiteSpace(expected) && string.Equals(rewardId, expected, StringComparison.Ordinal);

            if (cfg.CpRainbowEnabled && Match(cfg.CpRainbowRewardId)) command = "!rainbow";
            else if (cfg.CpDisappearEnabled && Match(cfg.CpDisappearRewardId)) command = "!disappear";
            else if (cfg.CpGhostEnabled && Match(cfg.CpGhostRewardId)) command = "!ghost";
            else if (cfg.CpBombEnabled && Match(cfg.CpBombRewardId)) command = "!" + (CommandRuntimeSettings.BombCommandName ?? "bomb");
            else if (cfg.CpFasterEnabled && Match(cfg.CpFasterRewardId)) command = "!faster";
            else if (cfg.CpSuperFastEnabled && Match(cfg.CpSuperFastRewardId)) command = "!superfast";
            else if (cfg.CpSlowerEnabled && Match(cfg.CpSlowerRewardId)) command = "!slower";
            else if (cfg.CpFlashbangEnabled && Match(cfg.CpFlashbangRewardId)) command = "!flashbang";

            if (command == null)
            {
                _log.Debug("Route miss rewardId=" + rewardId);
                return false;
            }

            _log.Debug("Route hit rewardId=" + rewardId + " command=" + command);
            return true;
        }

        internal static string TryBuildCommandFromReward(TwitchEventSubClient.ChannelPointRedemption redemption)
        {
            if (redemption == null)
            {
                return null;
            }

            if (_instance == null)
            {
                _instance = new ChannelPointRouter();
            }

            return _instance.TryGetCommand(redemption.RewardId, out string command) ? command : null;
        }
    }
}

