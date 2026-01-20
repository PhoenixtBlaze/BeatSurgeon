using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSaberMarkupLanguage.Components.Settings;
using HMUI;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using IPA.Utilities.Async; // for UnityMainThreadTaskScheduler



namespace BeatSurgeon.UI.Controllers
{
    [ViewDefinition("BeatSurgeon.UI.Views.BeatSurgeonCooldowns.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\BeatSurgeonCooldowns.bsml")]
    public class BeatSurgeonCooldownViewController : BSMLAutomaticViewController
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

            if (firstActivation)
            {
                TwitchApiClient.OnSubscriberStatusChanged += HandleSubscriberStatusChanged;
            }

            UpdateBombVisualsButtonVisibility();
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);

            if (removedFromHierarchy)
            {
                TwitchApiClient.OnSubscriberStatusChanged -= HandleSubscriberStatusChanged;
            }
        }

        private void HandleSubscriberStatusChanged()
        {
            // Ensure UI work runs on Unity’s main thread.
            UnityMainThreadTaskScheduler.Factory.StartNew(() =>
            {
                UpdateBombVisualsButtonVisibility();
                // Optional – if you ever bind `show_bomb_visuals_button` directly in BSML:
                // NotifyPropertyChanged(nameof(ShowBombVisualsButton));
            });
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
            _ = OnBombEditVisualsClickedAsync();
        }

        private async System.Threading.Tasks.Task OnBombEditVisualsClickedAsync()
        {
            // Fast local hint (still useful), but server is authoritative
            if (!TwitchAuthManager.Instance.IsAuthenticated)
            {
                Plugin.Log.Warn("Bomb visuals denied: not authenticated.");
                return;
            }

            bool allowed = false;
            try
            {
                allowed = await TwitchApiClient.Instance.CheckVisualsPermissionAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("Bomb visuals permission check failed: " + ex.Message);
                allowed = false;
            }

            if (!allowed)
            {
                Plugin.Log.Warn("Bomb visuals denied by server (not Tier1+ or invalid entitlement).");
                return;
            }

            // Show modal on Unity main thread
            await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    if (_bombVisualsModal != null)
                    {
                        _bombVisualsModal.Show(true);
                        StartCoroutine(RefreshBombFontDropdown());
                        StartBombFontPreview();
                    }
                    else
                    {
                        Plugin.Log.Warn("Bomb visuals modal was null when trying to show it.");
                    }
                });
        }


        [UIComponent("bomb-font-preview")]
        private TMP_Text _bombFontPreview;

        private Coroutine _bombFontPreviewCoroutine;

        [UIComponent("bomb-font-dropdown")]
        private DropDownListSetting _bombFontDropdown;


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


        // *** Font Selection Logic ***

        [UIValue("bomb_font_options")]
        public List<object> BombFontOptions
        {
            get
            {
                // Pull dynamic options from FontBundleLoader
                // BSML expects List<object> for dropdown choices
                var options = FontBundleLoader.GetBombFontOptions();
                return options.Cast<object>().ToList();
            }
        }

        [UIValue("bomb_font_selected")]
        public string BombFontSelected
        {
            get => FontBundleLoader.GetSelectedBombFontOption();
            set
            {
                FontBundleLoader.SetSelectedBombFontOption(value);
                NotifyPropertyChanged(nameof(BombFontSelected));
                ApplyBombFontPreviewStatic();
            }
        }

        // *** Gradient Color Logic ***

        [UIValue("bomb_gradient_start")]
        public Color BombGradientStart
        {
            get => Plugin.Settings?.BombGradientStart ?? Color.yellow;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.BombGradientStart = value;
                NotifyPropertyChanged(nameof(BombGradientStart));
            }
        }

        [UIValue("bomb_gradient_end")]
        public Color BombGradientEnd
        {
            get => Plugin.Settings?.BombGradientEnd ?? Color.red;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.BombGradientEnd = value;
                NotifyPropertyChanged(nameof(BombGradientEnd));
            }
        }

        /*

        [UIValue("bomb_fireworks_texture_options")]
        public List<object> BombFireworksTextureOptions
        {
            get
            {
                var options = FireworksExplosionPool.GetAvailableTextureTypes();
                if (options == null || options.Count == 0)
                    return new List<object> { "Sparkle" }; // Fallback
                return options.Cast<object>().ToList();
            }
        }

        [UIValue("bomb_fireworks_texture")]
        public string BombFireworksTexture
        {
            get => Plugin.Settings?.BombFireworksTextureType ?? "Sparkle";
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                FireworksExplosionPool.SetTextureType(value);

                if (Plugin.Settings != null)
                    Plugin.Settings.BombFireworksTextureType = value;

                NotifyPropertyChanged(nameof(BombFireworksTexture));
            }
        }

        */



        private IEnumerator RefreshBombFontDropdown()
        {
            // If you don’t care about hot-swapping bundles during runtime, you can use EnsureLoadedAsync() instead.
            var task = FontBundleLoader.ReloadAsync();
            while (!task.IsCompleted) yield return null;

            // Update BSML-bound properties
            NotifyPropertyChanged(nameof(BombFontOptions));
            NotifyPropertyChanged(nameof(BombFontSelected));

            // Force the dropdown to rebuild its UI list
            if (_bombFontDropdown != null)
            {
                _bombFontDropdown.Values = BombFontOptions;
                _bombFontDropdown.UpdateChoices();
                _bombFontDropdown.ReceiveValue();
            }

            ApplyBombFontPreviewStatic();
        }

        private void StartBombFontPreview()
        {
            StopBombFontPreview();

            if (_bombFontPreview == null)
                return;

            ApplyBombFontPreviewStatic(); // apply font + scale immediately
            _bombFontPreviewCoroutine = StartCoroutine(BombFontPreviewRoutine());
        }

        private void StopBombFontPreview()
        {
            if (_bombFontPreviewCoroutine != null)
            {
                StopCoroutine(_bombFontPreviewCoroutine);
                _bombFontPreviewCoroutine = null;
            }
        }

        private void ApplyBombFontPreviewStatic()
        {
            if (_bombFontPreview == null)
                return;

            // sample text
            _bombFontPreview.text = "PreviewUsername";

            // apply selected bomb font (loaded/selected by FontBundleLoader)
            var font = BeatSurgeon.Gameplay.FontBundleLoader.BombUsernameFont;
            if (font != null)
            {
                _bombFontPreview.font = font;
                _bombFontPreview.fontSharedMaterial = font.material;
            }

            // mimic gameplay “shape” controls
            _bombFontPreview.rectTransform.localScale = new Vector3(BombTextWidth, BombTextHeight, 1f);

            // optional styling similar to your in-game text
            _bombFontPreview.outlineWidth = 0.2f;
            _bombFontPreview.outlineColor = Color.black;
        }

        private IEnumerator BombFontPreviewRoutine()
        {
            // If you want it to feel like your in-game 2s flight, keep this at 2.0
            const float cycleSeconds = 2.0f;

            while (_bombFontPreview != null && _bombFontPreview.gameObject.activeInHierarchy)
            {
                // 0..1..0..1...
                float t = Mathf.PingPong(Time.unscaledTime / cycleSeconds, 1f);

                // use your existing settings as the gradient endpoints
                Color c = Color.Lerp(BombGradientStart, BombGradientEnd, t);
                c.a = 1f;

                _bombFontPreview.color = c;

                // If user changes options while it’s open, keep it in sync.
                // (Cheap enough to do every frame)
                ApplyBombFontPreviewStatic();

                yield return null;
            }

            _bombFontPreviewCoroutine = null;
        }



        [UIAction("CloseBombVisuals")]
        private void CloseBombVisuals()
        {
            if (_bombVisualsModal != null)
                _bombVisualsModal.Hide(true);

            StopBombFontPreview();
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
