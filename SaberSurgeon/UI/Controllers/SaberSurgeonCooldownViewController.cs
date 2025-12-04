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
                NotifyPropertyChanged(nameof(GlobalCooldownEnabled));
            }
        }

        [UIValue("global_cd_seconds")]
        public float GlobalCooldownSeconds
        {
            get => CommandHandler.GlobalCooldownSeconds;
            set
            {
                CommandHandler.GlobalCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
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
                NotifyPropertyChanged(nameof(PerCommandCooldownsEnabled));
            }
        }


        [UIValue("faster_cd_seconds")]
        public float FasterCooldownSeconds
        {
            get => CommandHandler.FasterCooldownSeconds;
            set
            {
                CommandHandler.FasterCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(FasterCooldownSeconds));
            }
        }


        [UIValue("rainbow_cd_seconds")]
        public float RainbowCooldownSeconds
        {
            get => CommandHandler.RainbowCooldownSeconds;
            set
            {
                CommandHandler.RainbowCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(RainbowCooldownSeconds));
            }
        }

        [UIValue("ghost_cd_seconds")]
        public float GhostCooldownSeconds
        {
            get => CommandHandler.GhostCooldownSeconds;
            set
            {
                CommandHandler.GhostCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(GhostCooldownSeconds));
            }
        }

        [UIValue("disappear_cd_seconds")]
        public float DisappearCooldownSeconds
        {
            get => CommandHandler.DisappearCooldownSeconds;
            set
            {
                CommandHandler.DisappearCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(DisappearCooldownSeconds));
            }
        }

        [UIValue("bomb_cd_seconds")]
        public float BombCooldownSeconds
        {
            get => CommandHandler.BombCooldownSeconds;
            set
            {
                CommandHandler.BombCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(BombCooldownSeconds));
            }
        }



        [UIValue("superfast_cd_seconds")]
        public float SuperFastCooldownSeconds
        {
            get => CommandHandler.SuperFastCooldownSeconds;
            set
            {
                CommandHandler.SuperFastCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
                NotifyPropertyChanged(nameof(SuperFastCooldownSeconds));
            }
        }

        [UIValue("slower_cd_seconds")]
        public float SlowerCooldownSeconds
        {
            get => CommandHandler.SlowerCooldownSeconds;
            set
            {
                CommandHandler.SlowerCooldownSeconds = Mathf.Clamp(value, 0f, 300f);
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
                NotifyPropertyChanged(nameof(SpeedExclusiveEnabled));
            }
        }

        [UIValue("flashbang_cd_seconds")]
        public int FlashbangCooldownSeconds
        {
            get => (int)CommandHandler.FlashbangCooldownSeconds;
            set
            {
                CommandHandler.FlashbangCooldownSeconds = Mathf.Clamp(value, 0, 300);
                NotifyPropertyChanged(nameof(FlashbangCooldownSeconds));
            }
        }


    }
}
