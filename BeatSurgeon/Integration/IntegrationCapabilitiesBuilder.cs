using System;
using System.Collections.Generic;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;

namespace BeatSurgeon.Integration
{
    internal static class IntegrationCapabilitiesBuilder
    {
        internal static IntegrationHandshakeSnapshot BuildSnapshot()
        {
            PluginConfig config = PluginConfig.Instance;
            bool hasVisualsAccess = PremiumVisualFeatureAccessController.HasAuthenticatedVisualsAccess();
            EntitlementsSnapshot entitlement = EntitlementsState.Current;

            var standardCommands = new List<string>();
            if (config != null)
            {
                if (CommandRuntimeSettings.RainbowEnabled)
                {
                    standardCommands.Add("!rainbow");
                    standardCommands.Add("!notecolor");
                }

                if (CommandRuntimeSettings.DisappearEnabled)
                {
                    standardCommands.Add("!disappear");
                }

                if (CommandRuntimeSettings.GhostEnabled)
                {
                    standardCommands.Add("!ghost");
                }

                if (CommandRuntimeSettings.BombEnabled)
                {
                    standardCommands.Add("!" + (CommandRuntimeSettings.BombCommandName ?? "bomb").Trim().ToLowerInvariant());
                    standardCommands.Add("!bmsg");
                }

                if (CommandRuntimeSettings.FasterEnabled)
                {
                    standardCommands.Add("!faster");
                }

                if (CommandRuntimeSettings.SuperFastEnabled)
                {
                    standardCommands.Add("!superfast");
                }

                if (CommandRuntimeSettings.SlowerEnabled)
                {
                    standardCommands.Add("!slower");
                }

                if (CommandRuntimeSettings.FlashbangEnabled)
                {
                    standardCommands.Add("!flashbang");
                }
            }

            var supporterCommands = new List<string>();
            if (hasVisualsAccess && config != null)
            {
                if (config.BitEffectEnabled)
                {
                    supporterCommands.Add("!glitter");
                }

                if (config.FollowEffectsEnabled)
                {
                    supporterCommands.Add("!fmsg");
                }

                if (config.SubEffectsEnabled)
                {
                    supporterCommands.Add("!smsg");
                }

                if (config.RaidEffectsEnabled)
                {
                    supporterCommands.Add("!raid");
                }
            }

            GameplayManager gameplay = GameplayManager.GetInstance();
            return new IntegrationHandshakeSnapshot
            {
                ServerVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown",
                HasVisualsAccess = hasVisualsAccess,
                SupporterTier = (int)entitlement.Tier,
                EntitlementProvider = EntitlementsState.CurrentProvider.ToString(),
                InMap = gameplay?.IsInMap ?? false,
                RankedBlocked = RankedMapDetectionService.Instance.IsCurrentMapRankedOrChecking,
                GlobalDisabled = CommandHandler.GlobalDisableActive,
                StandardCommands = standardCommands.ToArray(),
                SupporterCommands = supporterCommands.ToArray(),
                ConnectedClients = 0,
                MaxClients = IntegrationApiConstants.MaxClients
            };
        }
    }

    internal sealed class IntegrationHandshakeSnapshot
    {
        internal string ServerVersion { get; set; } = string.Empty;
        internal bool HasVisualsAccess { get; set; }
        internal int SupporterTier { get; set; }
        internal string EntitlementProvider { get; set; } = "None";
        internal bool InMap { get; set; }
        internal bool RankedBlocked { get; set; }
        internal bool GlobalDisabled { get; set; }
        internal string[] StandardCommands { get; set; } = Array.Empty<string>();
        internal string[] SupporterCommands { get; set; } = Array.Empty<string>();
        internal int ConnectedClients { get; set; }
        internal int MaxClients { get; set; }
    }
}
