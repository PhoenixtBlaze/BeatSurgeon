using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI; // for ModalView
using SaberSurgeon.Chat;
using SaberSurgeon.Twitch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


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


        [UIValue("bomb_command")]
        public string BombCommand
        {
            get
            {
                // Show with leading '!'
                string name = CommandHandler.BombCommandName;
                if (string.IsNullOrWhiteSpace(name))
                    name = "bomb";
                return "!" + name;
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                // Strip spaces and leading '!'
                string cleaned = value.Trim();
                if (cleaned.StartsWith("!"))
                    cleaned = cleaned.Substring(1);

                cleaned = cleaned.ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(cleaned))
                    return;

                // Update runtime behavior
                CommandHandler.BombCommandName = cleaned;

                // Persist to config
                if (Plugin.Settings != null)
                    Plugin.Settings.BombCommandName = cleaned;

                NotifyPropertyChanged(nameof(BombCommand));
            }
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            UpdateBombVisualsButtonVisibility();
        }

        private void UpdateBombVisualsButtonVisibility()
        {
            if (_bombEditButton != null)
            {
                bool allowed = ShowBombVisualsButton;
                _bombEditButton.gameObject.SetActive(allowed);
            }
        }



        [UIComponent("bomb-visuals-modal")]
        private ModalView _bombVisualsModal;

        [UIComponent("bomb-edit-button")]
        private UnityEngine.UI.Button _bombEditButton;


        [UIValue("show_bomb_visuals_button")]
        public bool ShowBombVisualsButton
        {
            get
            {
                // 1. Must be authenticated with your backend
                bool backendConnected = TwitchAuthManager.Instance.IsAuthenticated;

                // 2. Must be at least Tier1 supporter to your channel
                bool isSupporter =
                    SupporterState.CurrentTier != SupporterTier.None ||
                    (Plugin.Settings?.CachedSupporterTier ?? 0) > 0;

                return backendConnected && isSupporter;
            }
        }


        [UIAction("OnBombEditVisualsClicked")]
        private void OnBombEditVisualsClicked()
        {
            if (!ShowBombVisualsButton)
            {
                Plugin.Log.Warn("Bomb visuals clicked while not authorized (no backend or no sub).");
                return;
            }

            if (_bombVisualsModal != null)
            { 
                _bombVisualsModal.Show(true);
            }
            else
            {
                Plugin.Log.Warn("Bomb visuals modal was null when trying to show it.");
            }
        }

        [UIValue("bomb_text_height")]
        public float BombTextHeight
        {
            get => Plugin.Settings?.BombTextHeight ?? 1.0f;
            set
            {
                float clamped = Mathf.Clamp(value, 0.5f, 5f);
                if (Plugin.Settings != null)
                    Plugin.Settings.BombTextHeight = clamped;
                NotifyPropertyChanged(nameof(BombTextHeight));
            }
        }

        [UIValue("bomb_text_width")]
        public float BombTextWidth
        {
            get => Plugin.Settings?.BombTextWidth ?? 1.0f;
            set
            {
                float clamped = Mathf.Clamp(value, 0.5f, 5f);
                if (Plugin.Settings != null)
                    Plugin.Settings.BombTextWidth = clamped;
                NotifyPropertyChanged(nameof(BombTextWidth));
            }
        }

        [UIValue("bomb_spawn_distance")]
        public float BombSpawnDistance
        {
            get => Plugin.Settings?.BombSpawnDistance ?? 10.0f;
            set
            {
                float clamped = Mathf.Clamp(value, 2f, 20f);
                if (Plugin.Settings != null)
                    Plugin.Settings.BombSpawnDistance = clamped;
                NotifyPropertyChanged(nameof(BombSpawnDistance));
            }
        }


        // Dropdown options for bomb font
        [UIValue("bomb_font_options")]
        private List<object> BombFontOptions { get; } = new object[]
        {
        "Default"
        }.ToList();

        // Currently selected font
        [UIValue("bomb_font_selected")]
        public string BombFontSelected
        {
            get => Plugin.Settings?.BombFontType ?? "Default";
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.BombFontType = value;
                NotifyPropertyChanged(nameof(BombFontSelected));
            }
        }



        [UIAction("CloseBombVisuals")]
        private void CloseBombVisuals()
        {
            if (_bombVisualsModal != null)
            {
                _bombVisualsModal.Hide(true);
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

        // Text field backing the string-setting
        [UIValue("rainbow_command")]
        public string RainbowCommand
        {
            get => "!rainbow";          // default shown in the text box
            set
            {
                // If you want it editable, validate and store somewhere:
                // e.g. in Plugin.Settings.RainbowCommand
                //if (string.IsNullOrWhiteSpace(value))
                //    return;

                // Example: keep it without leading '!' and force lowercase
                //string cleaned = value.Trim();

                // Store if you have a setting:
                // Plugin.Settings.RainbowCommand = cleaned;

                NotifyPropertyChanged(nameof(RainbowCommand));
            }
        }

        // Button click handler
        [UIAction("OnRainbowEditVisualsClicked")]
        private void OnRainbowEditVisualsClicked()
        {
            // Open your visuals editor, or just log for now
            Plugin.Log.Info("Rainbow Edit Visuals button clicked");
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
