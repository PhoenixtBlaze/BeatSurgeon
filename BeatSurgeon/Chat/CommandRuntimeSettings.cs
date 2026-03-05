using System;
using BeatSurgeon.Twitch;

namespace BeatSurgeon.Chat
{
    /// <summary>
    /// Centralized runtime command settings and constants.
    /// Keeps CommandHandler focused on routing/execution.
    /// </summary>
    /// 
    internal static class CommandRuntimeSettings
    {
        internal const float RainbowEffectSeconds = 30f;
        internal const float GhostEffectSeconds = 30f;
        internal const float DisappearEffectSeconds = 30f;
        internal const float SpeedEffectSeconds = 30f;
        internal const float FasterMultiplier = 1.2f;
        internal const float SuperFastMultiplier = 1.5f;
        internal const float SlowerMultiplier = 0.85f;
        internal const float FlashbangHoldSeconds = 1f;
        internal const float FlashbangFadeSeconds = 3f;

        internal static float FlashbangIntensityMultiplier =>
            EntitlementsState.HasVisualsAccess
                ? (PluginConfig.Instance?.FlashbangBrightnessMultiplier ?? 90f)
                : 90f;

        internal static string BombCommandName
        {
            get => PluginConfig.Instance?.BombCommandName ?? "bomb";
            set
            {
                if (PluginConfig.Instance != null)
                {
                    PluginConfig.Instance.BombCommandName = (value ?? "bomb").Trim();
                }
            }
        }

        internal static bool PerCommandCooldownsEnabled
        {
            get => PluginConfig.Instance?.PerCommandCooldownsEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.PerCommandCooldownsEnabled = value; }
        }

        internal static bool GlobalCooldownEnabled
        {
            get => PluginConfig.Instance?.GlobalCooldownEnabled ?? false;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.GlobalCooldownEnabled = value; }
        }

        internal static float GlobalCooldownSeconds
        {
            get => PluginConfig.Instance?.GlobalCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.GlobalCooldownSeconds = value; }
        }

        internal static bool RainbowEnabled
        {
            get => PluginConfig.Instance?.RainbowEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.RainbowEnabled = value; }
        }

        internal static bool DisappearEnabled
        {
            get => PluginConfig.Instance?.DisappearEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.DisappearEnabled = value; }
        }

        internal static bool GhostEnabled
        {
            get => PluginConfig.Instance?.GhostEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.GhostEnabled = value; }
        }

        internal static bool BombEnabled
        {
            get => PluginConfig.Instance?.BombEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.BombEnabled = value; }
        }

        internal static bool FasterEnabled
        {
            get => PluginConfig.Instance?.FasterEnabled ?? false;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.FasterEnabled = value; }
        }

        internal static bool SuperFastEnabled
        {
            get => PluginConfig.Instance?.SuperFastEnabled ?? false;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.SuperFastEnabled = value; }
        }

        internal static bool SlowerEnabled
        {
            get => PluginConfig.Instance?.SlowerEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.SlowerEnabled = value; }
        }

        internal static bool FlashbangEnabled
        {
            get => PluginConfig.Instance?.FlashbangEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.FlashbangEnabled = value; }
        }

        internal static bool SongRequestsEnabled
        {
            get => PluginConfig.Instance?.SongRequestsEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.SongRequestsEnabled = value; }
        }

        internal static bool RequestAllowSpecificDifficulty
        {
            get => PluginConfig.Instance?.RequestAllowSpecificDifficulty ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.RequestAllowSpecificDifficulty = value; }
        }

        internal static bool RequestAllowSpecificTime
        {
            get => PluginConfig.Instance?.RequestAllowSpecificTime ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.RequestAllowSpecificTime = value; }
        }

        internal static float RainbowCooldownSeconds
        {
            get => PluginConfig.Instance?.RainbowCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.RainbowCooldownSeconds = value; }
        }

        internal static float DisappearCooldownSeconds
        {
            get => PluginConfig.Instance?.DisappearCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.DisappearCooldownSeconds = value; }
        }

        internal static float GhostCooldownSeconds
        {
            get => PluginConfig.Instance?.GhostCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.GhostCooldownSeconds = value; }
        }

        internal static float BombCooldownSeconds
        {
            get => PluginConfig.Instance?.BombCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.BombCooldownSeconds = value; }
        }

        internal static float FasterCooldownSeconds
        {
            get => PluginConfig.Instance?.FasterCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.FasterCooldownSeconds = value; }
        }

        internal static float SuperFastCooldownSeconds
        {
            get => PluginConfig.Instance?.SuperFastCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.SuperFastCooldownSeconds = value; }
        }

        internal static float SlowerCooldownSeconds
        {
            get => PluginConfig.Instance?.SlowerCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.SlowerCooldownSeconds = value; }
        }

        internal static float FlashbangCooldownSeconds
        {
            get => PluginConfig.Instance?.FlashbangCooldownSeconds ?? 60f;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.FlashbangCooldownSeconds = value; }
        }

        internal static bool SpeedExclusiveEnabled
        {
            get => PluginConfig.Instance?.SpeedExclusiveEnabled ?? true;
            set { if (PluginConfig.Instance != null) PluginConfig.Instance.SpeedExclusiveEnabled = value; }
        }

        internal static string NormalizeCommand(string command)
        {
            string normalized = (command ?? string.Empty).Trim().ToLowerInvariant();
            if (!normalized.StartsWith("!"))
            {
                return normalized;
            }

            string bombAlias = "!" + (BombCommandName ?? "bomb").Trim().ToLowerInvariant();
            if (!string.Equals(bombAlias, "!bomb", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(normalized, bombAlias, StringComparison.OrdinalIgnoreCase))
            {
                return "!bomb";
            }

            return normalized;
        }

        internal static string CanonicalizeCommandKey(string commandOrKey)
        {
            string normalized = NormalizeCommand(commandOrKey);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            string key = normalized.Trim().TrimStart('!').ToLowerInvariant();
            string bombAlias = (BombCommandName ?? "bomb").Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(bombAlias) && string.Equals(key, bombAlias, StringComparison.OrdinalIgnoreCase))
            {
                key = "bomb";
            }

            switch (key)
            {
                case "rainbownotes":
                case "notecolor":
                case "notecolour":
                    return "rainbow";
                case "disappearingarrows":
                    return "disappear";
                case "ghostnotes":
                    return "ghost";
                default:
                    return key;
            }
        }

        internal static bool IsCommandEnabled(string normalizedCommand)
        {
            switch (normalizedCommand)
            {
                case "!rainbow":
                case "!rainbownotes":
                case "!notecolor":
                case "!notecolour":
                    return RainbowEnabled;
                case "!disappear":
                case "!disappearingarrows":
                    return DisappearEnabled;
                case "!ghost":
                case "!ghostnotes":
                    return GhostEnabled;
                case "!bomb":
                    return BombEnabled;
                case "!faster":
                    return FasterEnabled;
                case "!superfast":
                    return SuperFastEnabled;
                case "!slower":
                    return SlowerEnabled;
                case "!flashbang":
                    return FlashbangEnabled;
                    /*
                case "!sr":
                case "!bsr":
                    return SongRequestsEnabled;
                    */
                default:
                    return true;
            }
        }

        internal static bool IsCooldownExempt(string normalizedCommand)
        {
            switch ((normalizedCommand ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "!sr":
                case "!bsr":
                    return true;
                default:
                    return false;
            }
        }

        internal static double GetCooldownSeconds(string commandName)
        {
            switch ((commandName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "rainbow":
                case "rainbownotes":
                case "notecolor":
                case "notecolour":
                    return RainbowCooldownSeconds;
                case "disappear":
                case "disappearingarrows":
                    return DisappearCooldownSeconds;
                case "ghost":
                case "ghostnotes":
                    return GhostCooldownSeconds;
                case "bomb":
                    return BombCooldownSeconds;
                case "faster":
                    return FasterCooldownSeconds;
                case "superfast":
                    return SuperFastCooldownSeconds;
                case "slower":
                    return SlowerCooldownSeconds;
                case "flashbang":
                    return FlashbangCooldownSeconds;
                default:
                    return 0;
            }
        }

        internal static int GetCooldownSecondsForRewardKey(string rewardKey)
        {
            switch ((rewardKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "rainbow":
                    return Math.Max(0, (int)Math.Round(RainbowCooldownSeconds));
                case "disappear":
                    return Math.Max(0, (int)Math.Round(DisappearCooldownSeconds));
                case "ghost":
                    return Math.Max(0, (int)Math.Round(GhostCooldownSeconds));
                case "bomb":
                    return Math.Max(0, (int)Math.Round(BombCooldownSeconds));
                case "faster":
                    return Math.Max(0, (int)Math.Round(FasterCooldownSeconds));
                case "superfast":
                    return Math.Max(0, (int)Math.Round(SuperFastCooldownSeconds));
                case "slower":
                    return Math.Max(0, (int)Math.Round(SlowerCooldownSeconds));
                case "flashbang":
                    return Math.Max(0, (int)Math.Round(FlashbangCooldownSeconds));
                default:
                    return 0;
            }
        }
    }
}
