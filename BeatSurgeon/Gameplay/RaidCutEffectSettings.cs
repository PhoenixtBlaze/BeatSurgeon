using System;

namespace BeatSurgeon.Gameplay
{
    internal static class RaidCutEffectSettings
    {
        internal const string DefaultOption = "Default";

        internal static readonly string[] AllOptions =
        {
            DefaultOption
        };

        internal static string GetSelectedOption()
        {
            return NormalizeOption(BeatSurgeon.Plugin.Settings?.RaidCutEffectType);
        }

        internal static void SetSelectedOption(string value)
        {
            PersistSelection(NormalizeOption(value));
        }

        internal static string NormalizeOption(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultOption;
            }

            if (int.TryParse(value, out int index)
                && index >= 0
                && index < AllOptions.Length)
            {
                return AllOptions[index];
            }

            foreach (string option in AllOptions)
            {
                if (string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return DefaultOption;
        }

        internal static void MigrateLoadedConfigOnLoad()
        {
            if (BeatSurgeon.Plugin.Settings == null)
            {
                return;
            }

            string raw = BeatSurgeon.Plugin.Settings.RaidCutEffectType;
            string normalized = NormalizeOption(raw);
            if (!string.Equals(raw, normalized, StringComparison.Ordinal))
            {
                PersistSelection(normalized);
            }
        }

        private static void PersistSelection(string normalized)
        {
            if (BeatSurgeon.Plugin.Settings == null)
            {
                return;
            }

            if (string.Equals(BeatSurgeon.Plugin.Settings.RaidCutEffectType, normalized, StringComparison.Ordinal))
            {
                return;
            }

            BeatSurgeon.Plugin.Settings.RaidCutEffectType = normalized;
            BeatSurgeon.Plugin.Settings.Changed();
        }
    }
}
