using System;

namespace BeatSurgeon.Gameplay
{
    internal static class BombExplosionEffectSettings
    {
        internal const string SparksOption = "Sparks";
        internal const string HeartsOption = "Hearts";
        internal const string FlamesOption = "Flames";
        internal const string LightningOption = "Lightning";
        internal const string ShockwaveOption = "Shockwave";

        internal static readonly string[] AllOptions =
        {
            SparksOption,
            HeartsOption,
            FlamesOption,
            LightningOption,
            ShockwaveOption
        };

        internal static string GetSelectedOption()
        {
            return NormalizeOption(BeatSurgeon.Plugin.Settings?.BombExplosionEffectType);
        }

        internal static void SetSelectedOption(string value)
        {
            PersistSelection(NormalizeOption(value));
        }

        internal static string NormalizeOption(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return SparksOption;
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

            return SparksOption;
        }

        internal static string GetEmitterNameForOption(string option)
        {
            switch (NormalizeOption(option))
            {
                case HeartsOption:
                    return BundleRegistry.SurgeonExplosionRefs.HeartEmitterName;
                case FlamesOption:
                    return BundleRegistry.SurgeonExplosionRefs.FlameEmitterName;
                case LightningOption:
                    return BundleRegistry.SurgeonExplosionRefs.LightningEmitterName;
                case ShockwaveOption:
                    return BundleRegistry.SurgeonExplosionRefs.ShockwaveCompositeEmitterName;
                case SparksOption:
                default:
                    return BundleRegistry.SurgeonExplosionRefs.SparkEmitterName;
            }
        }

        internal static void MigrateLoadedConfigOnLoad()
        {
            if (BeatSurgeon.Plugin.Settings == null)
            {
                return;
            }

            string raw = BeatSurgeon.Plugin.Settings.BombExplosionEffectType;
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

            if (string.Equals(BeatSurgeon.Plugin.Settings.BombExplosionEffectType, normalized, StringComparison.Ordinal))
            {
                return;
            }

            BeatSurgeon.Plugin.Settings.BombExplosionEffectType = normalized;
            BeatSurgeon.Plugin.Settings.Changed();
        }
    }
}
