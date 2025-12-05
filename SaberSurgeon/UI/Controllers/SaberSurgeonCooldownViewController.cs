using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using SaberSurgeon.Chat;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;
using TMPro;
using System.IO;


namespace SaberSurgeon.UI.Controllers
{
    [ViewDefinition("SaberSurgeon.UI.Views.SaberSurgeonCooldowns.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\SaberSurgeonCooldowns.bsml")]
    public class SaberSurgeonCooldownViewController : BSMLAutomaticViewController
    {


        // === Cooldown bindings ===

        [UIValue("global_cd_enabled")]
        public bool GlobalCooldownEnabled
        {
            get => CommandHandler.GlobalCooldownEnabled;
            set
            {
                CommandHandler.GlobalCooldownEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.GlobalCooldownEnabled = value;

                NotifyPropertyChanged(nameof(GlobalCooldownEnabled));
            }
        }

        [UIValue("global_cd_seconds")]
        public float GlobalCooldownSeconds
        {
            get => CommandHandler.GlobalCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.GlobalCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.GlobalCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(GlobalCooldownSeconds));
            }
        }

        [UIValue("per_command_cd_enabled")]
        public bool PerCommandCooldownsEnabled
        {
            get => CommandHandler.PerCommandCooldownsEnabled;
            set
            {
                CommandHandler.PerCommandCooldownsEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.PerCommandCooldownsEnabled = value;

                NotifyPropertyChanged(nameof(PerCommandCooldownsEnabled));
            }
        }

        [UIValue("faster_cd_seconds")]
        public float FasterCooldownSeconds
        {
            get => CommandHandler.FasterCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.FasterCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.FasterCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(FasterCooldownSeconds));
            }
        }

        [UIValue("rainbow_cd_seconds")]
        public float RainbowCooldownSeconds
        {
            get => CommandHandler.RainbowCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.RainbowCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.RainbowCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(RainbowCooldownSeconds));
            }
        }

        [UIValue("ghost_cd_seconds")]
        public float GhostCooldownSeconds
        {
            get => CommandHandler.GhostCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.GhostCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.GhostCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(GhostCooldownSeconds));
            }
        }

        [UIValue("disappear_cd_seconds")]
        public float DisappearCooldownSeconds
        {
            get => CommandHandler.DisappearCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.DisappearCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.DisappearCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(DisappearCooldownSeconds));
            }
        }

        [UIValue("bomb_cd_seconds")]
        public float BombCooldownSeconds
        {
            get => CommandHandler.BombCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.BombCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.BombCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(BombCooldownSeconds));
            }
        }

        [UIValue("superfast_cd_seconds")]
        public float SuperFastCooldownSeconds
        {
            get => CommandHandler.SuperFastCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.SuperFastCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.SuperFastCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(SuperFastCooldownSeconds));
            }
        }

        [UIValue("slower_cd_seconds")]
        public float SlowerCooldownSeconds
        {
            get => CommandHandler.SlowerCooldownSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, 300f);
                CommandHandler.SlowerCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.SlowerCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(SlowerCooldownSeconds));
            }
        }

        [UIValue("speed_exclusive_enabled")]
        public bool SpeedExclusiveEnabled
        {
            get => CommandHandler.SpeedExclusiveEnabled;
            set
            {
                CommandHandler.SpeedExclusiveEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.SpeedExclusiveEnabled = value;

                NotifyPropertyChanged(nameof(SpeedExclusiveEnabled));
            }
        }

        [UIValue("flashbang_cd_seconds")]
        public int FlashbangCooldownSeconds
        {
            get => (int)CommandHandler.FlashbangCooldownSeconds;
            set
            {
                int clamped = Mathf.Clamp(value, 0, 300);
                CommandHandler.FlashbangCooldownSeconds = clamped;
                if (Plugin.Settings != null)
                    Plugin.Settings.FlashbangCooldownSeconds = clamped;

                NotifyPropertyChanged(nameof(FlashbangCooldownSeconds));
            }
        }



    }
}
