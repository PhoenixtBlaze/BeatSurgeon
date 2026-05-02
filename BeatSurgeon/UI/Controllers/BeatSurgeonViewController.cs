using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.UI.Settings;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace BeatSurgeon.UI.Controllers
{
    [ViewDefinition("BeatSurgeon.UI.Views.BeatSurgeonSettings.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\BeatSurgeonSettings.bsml")]
    public class BeatSurgeonViewController : BSMLAutomaticViewController
    {

        private static Sprite LoadEmbeddedSprite(string resourcePath)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourcePath))
                {
                    if (stream == null)
                    {
                        Plugin.Log.Error($"[BeatSurgeon] Failed to find embedded resource '{resourcePath}'");
                        return null;
                    }

                    var bytes = new byte[stream.Length];
                    _ = stream.Read(bytes, 0, bytes.Length);

                    var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (!tex.LoadImage(bytes, markNonReadable: false))
                    {
                        Plugin.Log.Error($"[BeatSurgeon] Failed to decode texture from '{resourcePath}'");
                        return null;
                    }

                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;

                    var sprite = Sprite.Create(
                        tex,
                        new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    sprite.name = "BeatSurgeonRainbowIcon";

                    return sprite;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.Error($"[BeatSurgeon] Exception loading embedded sprite '{resourcePath}': {ex}");
                return null;
            }
        }


        // Loaded once from embedded resource
        private static readonly Sprite RainbowOffSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.Rainbow.png");

        private static readonly Sprite RainbowOnSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.RainbowGB.png");

        // DA icons (off = DA, on = DAGB)
        private static readonly Sprite DAOffSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.DA.png");

        private static readonly Sprite DAOnSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.DAGB.png");

        // Ghost icons (off = GhostNotes, on = GhostNotesGB)
        private static readonly Sprite GhostOffSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.GhostNotes.png");

        private static readonly Sprite GhostOnSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.GhostNotesGB.png");

        //Button Icons ((off = Bomb, on = BombGB)
        private static readonly Sprite BombOffSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.Bomb.png");

        private static readonly Sprite BombOnSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.BombGB.png");

        // Faster icons (off = FasterSong, on = FasterSongGB)
        private static readonly Sprite FasterOffSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.FasterSong.png");
        private static readonly Sprite FasterOnSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.FasterSongGB.png");

        // SuperFast icons
        private static readonly Sprite SuperFastOffSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.SuperFastSong.png");
        private static readonly Sprite SuperFastOnSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.SuperFastSongGB.png");

        // Slower icons
        private static readonly Sprite SlowerOffSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.SlowerSong.png");
        private static readonly Sprite SlowerOnSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.SlowerSongGB.png");

        // Flashbang icons (off = Flashbang, on = FlashbangGB)
        private static readonly Sprite FlashbangOffSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.Flashbang.png");
        private static readonly Sprite FlashbangOnSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.FlashbangGB.png");

        public static BeatSurgeonViewController ActiveInstance { get; private set; }



        private string _twitchChannel = string.Empty;
        private string _twitchStatusText = "<color=#FF4444>Not connected</color>";


        // === Play time slider ===

        [UIValue("playTime")]
        public float PlayTime
        {
            get => _playTime;
            set
            {
                _playTime = value;
                NotifyPropertyChanged(nameof(PlayTime));
                LogUtils.Debug(() => $"Slider changed → PlayTime = {_playTime} minutes ");
            }
        }

        private float _playTime = 60f;

        // === Rainbow enabled flag (backed by CommandHandler) ===

        [UIValue("rainbowenabled")]
        public bool RainbowEnabled
        {
            get => CommandRuntimeSettings.RainbowEnabled;
            set
            {
                CommandRuntimeSettings.RainbowEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.RainbowEnabled = value;
                NotifyPropertyChanged(nameof(RainbowEnabled));
                SurgeonGameplaySetupHost.RefreshGameplaySetupIcons();
                UpdateRainbowButtonVisual();
            }
        }

        [UIComponent("rainbowbutton")]
        private Button rainbowButton;

        [UIComponent("rainbowicon")]
        private Image rainbowIcon;

        private Image rainbowButtonImage;


        // Menu toggle bound to CommandHandler.DisappearingEnabled
        [UIValue("da_enabled")]
        public bool DisappearingEnabled
        {
            get => CommandRuntimeSettings.DisappearEnabled;
            set
            {
                CommandRuntimeSettings.DisappearEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.DisappearEnabled = value;
                NotifyPropertyChanged(nameof(DisappearingEnabled));
                UpdateDAButtonVisual();
                SurgeonGameplaySetupHost.RefreshGameplaySetupIcons();

            }
        }

        [UIComponent("dabutton")]
        private Button daButton;

        [UIComponent("daicon")]
        private Image daIcon;

        private Image daButtonImage;





        [UIValue("ghost_enabled")]
        public bool GhostEnabled
        {
            get => CommandRuntimeSettings.GhostEnabled;
            set
            {
                CommandRuntimeSettings.GhostEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.GhostEnabled = value;
                NotifyPropertyChanged(nameof(GhostEnabled));
                UpdateGhostButtonVisual();
                SurgeonGameplaySetupHost.RefreshGameplaySetupIcons();

            }
        }

        [UIComponent("ghostbutton")]
        private Button ghostButton;

        [UIComponent("ghosticon")]
        private Image ghostIcon;

        private Image ghostButtonImage;



        [UIValue("bomb_enabled")]
        public bool BombEnabled
        {
            get => CommandRuntimeSettings.BombEnabled;
            set
            {
                CommandRuntimeSettings.BombEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.BombEnabled = value;
                NotifyPropertyChanged(nameof(BombEnabled));
                UpdateBombButtonVisual();
                SurgeonGameplaySetupHost.RefreshGameplaySetupIcons();

            }
        }

        [UIComponent("bombbutton")]
        private Button bombButton;

        [UIComponent("bombicon")]
        private Image bombIcon;
        private Image bombButtonImage;


        [UIValue("faster_enabled")]
        public bool FasterEnabled
        {
            get => CommandRuntimeSettings.FasterEnabled;
            set
            {
                CommandRuntimeSettings.FasterEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.FasterEnabled = value;
                NotifyPropertyChanged(nameof(FasterEnabled));
                UpdateFasterButtonVisual();
                SurgeonGameplaySetupHost.RefreshGameplaySetupIcons();

            }
        }

        [UIComponent("fasterbutton")]
        private Button fasterButton;

        [UIComponent("fastericon")]
        private Image fasterIcon;

        private Image fasterButtonImage;



        [UIValue("superfast_enabled")]
        public bool SuperFastEnabled
        {
            get => CommandRuntimeSettings.SuperFastEnabled;
            set
            {
                CommandRuntimeSettings.SuperFastEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.SuperFastEnabled = value;
                NotifyPropertyChanged(nameof(SuperFastEnabled));
                UpdateSuperFastButtonVisual();
                SurgeonGameplaySetupHost.RefreshGameplaySetupIcons();

            }
        }

        [UIComponent("superfastbutton")]
        private Button superFastButton;

        [UIComponent("superfasticon")]
        private Image superFastIcon;

        private Image superFastButtonImage;


        [UIValue("slower_enabled")]
        public bool SlowerEnabled
        {
            get => CommandRuntimeSettings.SlowerEnabled;
            set
            {
                CommandRuntimeSettings.SlowerEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.SlowerEnabled = value;
                NotifyPropertyChanged(nameof(SlowerEnabled));
                UpdateSlowerButtonVisual();
                SurgeonGameplaySetupHost.RefreshGameplaySetupIcons();

            }
        }

        [UIComponent("slowerbutton")]
        private Button slowerButton;

        [UIComponent("slowericon")]
        private Image slowerIcon;

        private Image slowerButtonImage;


        [UIValue("rankedAutoDisable")]
        public bool RankedAutoDisable
        {
            get => Plugin.Settings?.DisableOnRanked ?? true;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.DisableOnRanked = value;
                NotifyPropertyChanged(nameof(RankedAutoDisable));
                LogUtils.Debug(() => $"BeatSurgeon: Auto-disable on ranked = {value}");
            }
        }

        [UIValue("SSAutoDisable")]
        public bool RankedSS
        {
            get => Plugin.Settings?.DisableOnRankedSS ?? true;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.DisableOnRankedSS = value;
                NotifyPropertyChanged(nameof(RankedSS));
            }
        }

        [UIValue("BLAutoDisable")]
        public bool RankedBL
        {
            get => Plugin.Settings?.DisableOnRankedBL ?? true;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.DisableOnRankedBL = value;
                NotifyPropertyChanged(nameof(RankedBL));
            }
        }

        [UIValue("AccSaberAutoDisable")]
        public bool RankedAccSaber
        {
            get => Plugin.Settings?.DisableOnRankedAccSaber ?? true;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.DisableOnRankedAccSaber = value;
                NotifyPropertyChanged(nameof(RankedAccSaber));
            }
        }

        [UIValue("multiplayerEnable")]
        public bool MultiplayerEnable
        {
            get => Plugin.Settings?.MultiplayerEffectsEnabled ?? true;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.MultiplayerEffectsEnabled = value;
                NotifyPropertyChanged(nameof(MultiplayerEnable));
                global::BeatSurgeon.MultiplayerRoomSyncClient.RefreshConnectionState();
                LogUtils.Debug(() => $"BeatSurgeon: Multiplayer effects enabled = {value}");
            }
        }

        [UIValue("flashbang_enabled")]
        public bool FlashbangEnabled
        {
            get => CommandRuntimeSettings.FlashbangEnabled;
            set
            {
                CommandRuntimeSettings.FlashbangEnabled = value;
                if (Plugin.Settings != null)
                    Plugin.Settings.FlashbangEnabled = value;
                NotifyPropertyChanged(nameof(FlashbangEnabled));
                UpdateFlashbangButtonVisual();
                SurgeonGameplaySetupHost.RefreshGameplaySetupIcons();

            }
        }

        [UIComponent("flashbangbutton")]
        private Button flashbangButton;

        [UIComponent("flashbangicon")]
        private Image flashbangIcon;

        private Image flashbangButtonImage;



        // === UI components from BSML ===


        // Colors for OFF/ON states to mimic modifier highlight
        private readonly Color offColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        private readonly Color onColor = new Color(0.18f, 0.7f, 1f, 1f);


        // === Lifecycle / visual updates ===


        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            ActiveInstance = this;

            RefreshSupporterUiState();
            StartBombFontPreview();

            // Subscribe to auth events for reauth notification display
            if (firstActivation)
            {
                TwitchAuthManager.Instance.OnReauthRequired += HandleSupportStateChanged;
                TwitchAuthManager.Instance.OnTokensUpdated += HandleSupportStateChanged;
                TwitchAuthManager.Instance.OnIdentityUpdated += HandleSupportStateChanged;

                if (rainbowButton != null)
                {
                    // Clear built-in label – icon-only button
                    BeatSaberUI.SetButtonText(rainbowButton, string.Empty);
                    rainbowButtonImage = rainbowButton.GetComponent<Image>();
                }

                if (rainbowIcon != null)
                {
                    // Make the image a child of the button so it sits centered on it
                    //rainbowIcon.transform.SetParent(rainbowButton.transform, worldPositionStays: false);

                    var rt = rainbowIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f); // tweak size to taste
                }
                if (daButton != null)
                {
                    BeatSaberUI.SetButtonText(daButton, string.Empty);
                    daButtonImage = daButton.GetComponent<Image>();
                }

                if (daIcon != null)
                {
                    //daIcon.transform.SetParent(daButton.transform, worldPositionStays: false);

                    var rt = daIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }

                if (ghostButton != null)
                {
                    BeatSaberUI.SetButtonText(ghostButton, string.Empty);
                    ghostButtonImage = ghostButton.GetComponent<Image>();
                }

                if (ghostIcon != null)
                {
                    ghostIcon.transform.SetParent(ghostButton.transform, worldPositionStays: false);
                    var rt = ghostIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }
                if (bombButton != null)
                {
                    BeatSaberUI.SetButtonText(bombButton, string.Empty);
                    bombButtonImage = bombButton.GetComponent<Image>();
                }

                if (bombIcon != null)
                {
                    var rt = bombIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }
                if (fasterButton != null)
                {
                    BeatSaberUI.SetButtonText(fasterButton, string.Empty);
                    fasterButtonImage = fasterButton.GetComponent<Image>();
                }

                if (fasterIcon != null)
                {
                    var rt = fasterIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }
                if (superFastButton != null)
                {
                    BeatSaberUI.SetButtonText(superFastButton, string.Empty);
                    superFastButtonImage = superFastButton.GetComponent<Image>();
                }

                if (superFastIcon != null)
                {
                    var rt = superFastIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }
                if (slowerButton != null)
                {
                    BeatSaberUI.SetButtonText(slowerButton, string.Empty);
                    slowerButtonImage = slowerButton.GetComponent<Image>();
                }

                if (slowerIcon != null)
                {
                    var rt = slowerIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }
                if (flashbangButton != null)
                {
                    BeatSaberUI.SetButtonText(flashbangButton, string.Empty);
                    flashbangButtonImage = flashbangButton.GetComponent<Image>();
                }

                if (flashbangIcon != null)
                {
                    var rt = flashbangIcon.rectTransform;
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(12f, 12f);
                }
                TwitchApiClient.OnSubscriberStatusChanged += HandleSupportStateChanged;

            }

            // Apply correct sprite & color for current state

            UpdateRainbowButtonVisual();
            UpdateGhostButtonVisual();
            UpdateBombButtonVisual();
            UpdateDAButtonVisual();
            UpdateFasterButtonVisual();
            UpdateSuperFastButtonVisual();
            UpdateSlowerButtonVisual();
            UpdateFlashbangButtonVisual();
            RefreshSupporterUiState();
            

        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            if (removedFromHierarchy && ReferenceEquals(ActiveInstance, this))
            {
                ActiveInstance = null;
                StopBombFontPreview();
                // Unsubscribe from auth events
                TwitchAuthManager.Instance.OnReauthRequired -= HandleSupportStateChanged;
                TwitchAuthManager.Instance.OnTokensUpdated -= HandleSupportStateChanged;
                TwitchAuthManager.Instance.OnIdentityUpdated -= HandleSupportStateChanged;
                TwitchApiClient.OnSubscriberStatusChanged -= HandleSupportStateChanged;
            }

            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }


        public static void RefreshCommandUiFromExternal()
        {
            var vc = ActiveInstance;
            if (vc == null) return;

            // Re-evaluate BSML bindings
            vc.NotifyPropertyChanged(nameof(RainbowEnabled));
            vc.NotifyPropertyChanged(nameof(DisappearingEnabled));
            vc.NotifyPropertyChanged(nameof(GhostEnabled));
            vc.NotifyPropertyChanged(nameof(BombEnabled));
            vc.NotifyPropertyChanged(nameof(FasterEnabled));
            vc.NotifyPropertyChanged(nameof(SuperFastEnabled));
            vc.NotifyPropertyChanged(nameof(SlowerEnabled));
            vc.NotifyPropertyChanged(nameof(FlashbangEnabled));

            // Repaint icons/backgrounds
            vc.UpdateRainbowButtonVisual();
            vc.UpdateDAButtonVisual();
            vc.UpdateGhostButtonVisual();
            vc.UpdateBombButtonVisual();
            vc.UpdateFasterButtonVisual();
            vc.UpdateSuperFastButtonVisual();
            vc.UpdateSlowerButtonVisual();
            vc.UpdateFlashbangButtonVisual();
        }

        public static void RefreshSupporterUiFromExternal()
        {
            var vc = ActiveInstance;
            if (vc == null) return;

            vc.RefreshSupporterUiState();
        }

        private void HandleSupportStateChanged()
        {
            _ = IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(RefreshSupporterUiState);
        }

        private void RefreshSupporterUiState()
        {
            RefreshTwitchStatusText();
            NotifyPropertyChanged(nameof(SupporterTabVisible));
            UpdateSupportUI();
            NotifyPropertyChanged(nameof(BitEffectEnabled));
            NotifyPropertyChanged(nameof(FollowEffectsEnabled));
            NotifyPropertyChanged(nameof(FollowEffectsToggleInteractable));
            NotifyPropertyChanged(nameof(SubscribeButtonText));
        }



        private void UpdateFlashbangButtonVisual()
        {
            if (flashbangIcon != null)
            {
                var sprite = FlashbangEnabled ? FlashbangOnSprite : FlashbangOffSprite;
                if (sprite != null)
                    flashbangIcon.sprite = sprite;
            }

            if (flashbangButtonImage != null)
                flashbangButtonImage.color = FlashbangEnabled ? onColor : offColor;
        }

        [UIAction("OnFlashbangButtonClicked")]
        private void OnFlashbangButtonClicked()
        {
            FlashbangEnabled = !FlashbangEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Flashbang command enabled = {FlashbangEnabled}");
        }


        private void UpdateSlowerButtonVisual()
        {
            if (slowerIcon != null)
            {
                var sprite = SlowerEnabled ? SlowerOnSprite : SlowerOffSprite;
                if (sprite != null)
                    slowerIcon.sprite = sprite;
            }

            if (slowerButtonImage != null)
                slowerButtonImage.color = SlowerEnabled ? onColor : offColor;
        }

        [UIAction("OnSlowerButtonClicked")]
        private void OnSlowerButtonClicked()
        {
            SlowerEnabled = !SlowerEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Slower command enabled = {SlowerEnabled}");
        }


        private void UpdateSuperFastButtonVisual()
        {
            if (superFastIcon != null)
            {
                var sprite = SuperFastEnabled ? SuperFastOnSprite : SuperFastOffSprite;
                if (sprite != null)
                    superFastIcon.sprite = sprite;
            }

            if (superFastButtonImage != null)
                superFastButtonImage.color = SuperFastEnabled ? onColor : offColor;
        }

        [UIAction("OnSFastButtonClicked")]
        private void OnSFastButtonClicked()
        {
            SuperFastEnabled = !SuperFastEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: SuperFast command enabled = {SuperFastEnabled}");
        }


        private void UpdateFasterButtonVisual()
        {
            if (fasterIcon != null)
            {
                var sprite = FasterEnabled ? FasterOnSprite : FasterOffSprite;
                if (sprite != null)
                    fasterIcon.sprite = sprite;
            }

            if (fasterButtonImage != null)
                fasterButtonImage.color = FasterEnabled ? onColor : offColor;
        }

        [UIAction("OnFasterButtonClicked")]
        private void OnFasterButtonClicked()
        {
            FasterEnabled = !FasterEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Faster command enabled = {FasterEnabled}");
        }


        private void UpdateBombButtonVisual()
        {
            if (bombIcon != null)
            {
                var sprite = BombEnabled ? BombOnSprite : BombOffSprite;
                if (sprite != null)
                    bombIcon.sprite = sprite;
            }

            if (bombButtonImage != null)
                bombButtonImage.color = BombEnabled ? onColor : offColor;
        }

        [UIAction("OnBombButtonClicked")]
        private void OnBombButtonClicked()
        {
            BombEnabled = !BombEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Bomb command enabled = {BombEnabled}");
        }



        private void UpdateDAButtonVisual()
        {
            if (daIcon != null)
            {
                var sprite = DisappearingEnabled ? DAOnSprite : DAOffSprite;
                if (sprite != null)
                    daIcon.sprite = sprite;
            }

            if (daButtonImage != null)
                daButtonImage.color = DisappearingEnabled ? onColor : offColor;
        }

        [UIAction("OnDAButtonClicked")]
        private void OnDAButtonClicked()
        {
            DisappearingEnabled = !DisappearingEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Disappearing command enabled = {DisappearingEnabled}");
        }




        private void UpdateRainbowButtonVisual()
        {
            // Swap icon sprite based on enabled state
            if (rainbowIcon != null)
            {
                var sprite = RainbowEnabled ? RainbowOnSprite : RainbowOffSprite;
                if (sprite != null)
                    rainbowIcon.sprite = sprite;
            }

            // Optional: still change background color for extra feedback
            if (rainbowButtonImage != null)
                rainbowButtonImage.color = RainbowEnabled ? onColor : offColor;
        }



        private void UpdateGhostButtonVisual()
        {
            if (ghostIcon != null)
            {
                var sprite = GhostEnabled ? GhostOnSprite : GhostOffSprite;
                if (sprite != null)
                    ghostIcon.sprite = sprite;
            }

            if (ghostButtonImage != null)
                ghostButtonImage.color = GhostEnabled ? onColor : offColor;
        }

        [UIAction("OnGhostButtonClicked")]
        private void OnGhostButtonClicked()
        {
            GhostEnabled = !GhostEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Ghost command enabled = {GhostEnabled}");
        }




        // ── Supporter Tab ────────────────────────────────────────── 

        [UIValue("BitEffectEnabled")]
        public bool BitEffectEnabled
        {
            get => PluginConfig.Instance.BitEffectEnabled;
            set => PluginConfig.Instance.BitEffectEnabled = value;
        }

        [UIAction("OnBitEffectChanged")]
        private void OnBitEffectChanged(bool value)
        {
            BitEffectAccessController.ApplyManualToggle(value);
            NotifyPropertyChanged(nameof(BitEffectEnabled));
        }

        [UIValue("SubEffectsEnabled")]
        public bool SubEffectsEnabled
        {
            get => PluginConfig.Instance.SubEffectsEnabled;
            set => PluginConfig.Instance.SubEffectsEnabled = value;
        }

        [UIAction("OnSubEffectsChanged")]
        private void OnSubEffectsChanged(bool value)
        {
            PluginConfig.Instance.SubEffectsEnabled = value;
        }

        [UIValue("FollowEffectsEnabled")]
        public bool FollowEffectsEnabled
        {
            get => PluginConfig.Instance.FollowEffectsEnabled;
            set => PluginConfig.Instance.FollowEffectsEnabled = value;
        }

        [UIValue("followEffectsToggleInteractable")]
        public bool FollowEffectsToggleInteractable => FollowEffectAccessController.IsToggleInteractable;

        [UIAction("OnFollowEffectsChanged")]
        private void OnFollowEffectsChanged(bool value)
        {
            FollowEffectAccessController.ApplyManualToggle(value);
            NotifyPropertyChanged(nameof(FollowEffectsEnabled));
            NotifyPropertyChanged(nameof(FollowEffectsToggleInteractable));
            _ = TwitchEventSubClient.Instance.RefreshSubscriptionsAsync();
        }


        // ==================== Bomb Font Selection (Supporter Tab) ====================

        [UIComponent("bomb-font-dropdown")]
        private DropDownListSetting _bombFontDropdown;

        [UIComponent("bomb-font-preview")]
        private TMP_Text _bombFontPreview;

        private Coroutine _bombFontPreviewCoroutine;

        [UIValue("bomb_font_options")]
        public List<object> BombFontOptions
        {
            get
            {
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
                StopBombFontPreview();
                FontBundleLoader.SetSelectedBombFontOption(value);
                NotifyPropertyChanged(nameof(BombFontSelected));
                StartCoroutine(ApplyBombFontChangeDelayed());
            }
        }

        private IEnumerator ApplyBombFontChangeDelayed()
        {
            if (_bombFontPreview != null)
                _bombFontPreview.enabled = false;

            yield return null;
            yield return null;

            ApplyBombFontPreviewStatic();

            if (_bombFontPreview != null)
                _bombFontPreview.enabled = true;

            if (_bombFontPreview != null && _bombFontPreview.gameObject.activeInHierarchy)
                _bombFontPreviewCoroutine = StartCoroutine(BombFontPreviewRoutine());
        }

        private void StartBombFontPreview()
        {
            StopBombFontPreview();

            if (_bombFontPreview == null)
                return;

            var task = FontBundleLoader.EnsureLoadedAsync();
            StartCoroutine(RefreshBombFontDropdownCoroutine(task));
        }

        private IEnumerator RefreshBombFontDropdownCoroutine(System.Threading.Tasks.Task task)
        {
            while (!task.IsCompleted) yield return null;

            NotifyPropertyChanged(nameof(BombFontOptions));
            NotifyPropertyChanged(nameof(BombFontSelected));

            if (_bombFontDropdown != null)
            {
                _bombFontDropdown.Values = BombFontOptions;
                _bombFontDropdown.UpdateChoices();
                _bombFontDropdown.ReceiveValue();
            }

            ApplyBombFontPreviewStatic();
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

            if (!FontBundleLoader.TryApplySelectedBombFont(_bombFontPreview))
                return;

            _bombFontPreview.text = "PreviewUsername";
            _bombFontPreview.outlineWidth = 0.2f;
            _bombFontPreview.outlineColor = Color.black;
            _bombFontPreview.SetAllDirty();
            _bombFontPreview.ForceMeshUpdate();
        }

        private IEnumerator BombFontPreviewRoutine()
        {
            const float cycleSeconds = 2.0f;

            while (_bombFontPreview != null && _bombFontPreview.gameObject.activeInHierarchy)
            {
                float t = Mathf.PingPong(Time.unscaledTime / cycleSeconds, 1f);
                Color c = Color.Lerp(
                    Plugin.Settings?.BombGradientStart ?? Color.yellow,
                    Plugin.Settings?.BombGradientEnd ?? Color.red,
                    t);
                c.a = 1f;
                _bombFontPreview.color = c;
                yield return null;
            }

            _bombFontPreviewCoroutine = null;
        }




        [UIValue("AllowEveryoneCommands")]
        public bool AllowEveryoneCommands
        {
            get => Plugin.Settings?.AllowEveryoneCommands ?? true;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.AllowEveryoneCommands = value;
                NotifyPropertyChanged(nameof(AllowEveryoneCommands));
            }
        }

        [UIAction("OnAllowEveryoneChanged")]
        private void OnAllowEveryoneChanged(bool value)
        {
            AllowEveryoneCommands = value;
        }

        [UIValue("AllowVIPCommands")]
        public bool AllowVIPCommands
        {
            get => Plugin.Settings?.AllowVIPCommands ?? false;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.AllowVIPCommands = value;
                NotifyPropertyChanged(nameof(AllowVIPCommands));
            }
        }

        [UIAction("OnAllowVIPChanged")]
        private void OnAllowVIPChanged(bool value)
        {
            AllowVIPCommands = value;
        }

        [UIValue("AllowSubscriberCommands")]
        public bool AllowSubscriberCommands
        {
            get => Plugin.Settings?.AllowSubscriberCommands ?? false;
            set
            {
                if (Plugin.Settings != null)
                    Plugin.Settings.AllowSubscriberCommands = value;
                NotifyPropertyChanged(nameof(AllowSubscriberCommands));
            }
        }

        [UIAction("OnAllowSubscriberChanged")]
        private void OnAllowSubscriberChanged(bool value)
        {
            AllowSubscriberCommands = value;
        }

        [UIValue("TwitchStatusText")]
        public string TwitchStatusText
        {
            get => _twitchStatusText;
            private set
            {
                _twitchStatusText = value;
                NotifyPropertyChanged(nameof(TwitchStatusText));
            }
        }

        private bool IsTwitchConnected()
        {
            return TwitchAuthManager.Instance.IsAuthenticated;
        }


        private void RefreshTwitchStatusText()
        {
            LogUtils.Debug(() => 
                $"UI: RefreshTwitchStatusText IsAuthenticated={TwitchAuthManager.Instance.IsAuthenticated}, " +
                $"Tier={Plugin.Settings.CachedSupporterTier}");

            if (Plugin.Settings?.TwitchReauthRequired == true)
            {
                TwitchStatusText = "<color=#FFFF44>Please Reauthorize</color>";
                SurgeonGameplaySetupHost.SetTwitchStatusFromBeatSurgeon(TwitchStatusText);
                return;
            }

            if (!IsTwitchConnected())
            {
                TwitchStatusText = "<color=#FF4444>Not connected</color>";
                SurgeonGameplaySetupHost.SetTwitchStatusFromBeatSurgeon(TwitchStatusText);
                return;
            }

            var name = TwitchApiClient.Instance.BroadcasterName
                       ?? Plugin.Settings.CachedBroadcasterLogin
                       ?? "Unknown";

            int tier = (int)SupporterState.CurrentTier;
            if (tier <= 0)
            {
                tier = Plugin.Settings.CachedSupporterTier;
            }

            if (tier > 0)
            {
                TwitchStatusText = $"<color=#44FF44>Connected (Tier {tier})</color>";
                SurgeonGameplaySetupHost.SetTwitchStatusFromBeatSurgeon(TwitchStatusText);
            }
            else
            {
                TwitchStatusText = "<color=#44FF44>Connected</color>";
                SurgeonGameplaySetupHost.SetTwitchStatusFromBeatSurgeon(TwitchStatusText);
            }
        }





        [UIAction("OnConnectTwitchClicked")]
        private void OnConnectTwitchClicked()
        {
            _ = ConnectTwitchAsync();
        }

        private async Task ConnectTwitchAsync()
        {
            TwitchStatusText = "<color=#FFFF44>Opening browser...</color>";
            await TwitchAuthManager.Instance.InitiateLogin();

            // Wait up to ~10 seconds for auth + Helix to complete
            const int maxWaitMs = 10000;
            int waited = 0;
            const int step = 500;

            while (waited < maxWaitMs && !TwitchAuthManager.Instance.IsAuthenticated)
            {
                await Task.Delay(step);
                waited += step;
            }

            // Give a bit of extra time for FetchBroadcasterAndSupportInfo to fill tier/name
            await Task.Delay(1000);

            RefreshTwitchStatusText();
        }



        // --- Endless Mode Section ---


        [UIAction("OnRainbowButtonClicked")]
        private void OnRainbowButtonClicked()
        {
            RainbowEnabled = !RainbowEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Rainbow command enabled = {RainbowEnabled}");
        }

        [UIAction("OnStartPlayPressed")]
        private void OnStartPlayPressed()
        {
            LogUtils.Debug(() => "BeatSurgeon: Start/Play button pressed!");
            LogUtils.Debug(() => $"Timer set to: {PlayTime} minutes");
            // Endless mode is intentionally disabled for now.
            // var gameplayManager = BeatSurgeon.Gameplay.GameplayManager.GetInstance();
            // if (gameplayManager.IsPlaying())
            // {
            //     gameplayManager.StopEndlessMode();
            //     LogUtils.Debug(() => "BeatSurgeon: Stopped endless mode");
            //     ChatManager.GetInstance().SendChatMessage("Saber Surgeon session ended!");
            // }
            // else
            // {
            //     gameplayManager.StartEndlessMode(PlayTime);
            //     LogUtils.Debug(() => $"BeatSurgeon: Started endless mode for {PlayTime} minutes");
            //     ChatManager.GetInstance().SendChatMessage(
            //         $"Saber Surgeon started! Playing for {PlayTime} minutes. Request songs with !bsr <code>");
            // }
            ChatManager.GetInstance().SendChatMessage("Endless mode is currently disabled.");
        }


        // --- Song Request Settings ---

        [UIValue("songRequestsEnabled")]
        public bool SongRequestsEnabled
        {
            get => Plugin.Settings?.SongRequestsEnabled ?? true;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.SongRequestsEnabled = value;
                NotifyPropertyChanged(nameof(SongRequestsEnabled));
            }
        }

        [UIValue("requestAllowSpecificDifficulty")]
        public bool RequestAllowSpecificDifficulty
        {
            get => Plugin.Settings?.RequestAllowSpecificDifficulty ?? true;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.RequestAllowSpecificDifficulty = value;
                NotifyPropertyChanged(nameof(RequestAllowSpecificDifficulty));
            }
        }

        [UIValue("requestAllowSpecificTime")]
        public bool RequestAllowSpecificTime
        {
            get => Plugin.Settings?.RequestAllowSpecificTime ?? true;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.RequestAllowSpecificTime = value;
                NotifyPropertyChanged(nameof(RequestAllowSpecificTime));
            }
        }

        // BSML slider-setting works best with float, so wrap int settings as float in UI.

        [UIValue("queueSizeLimit")]
        public float QueueSizeLimit
        {
            get => Plugin.Settings?.QueueSizeLimit ?? 20;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.QueueSizeLimit = Mathf.RoundToInt(value);
                NotifyPropertyChanged(nameof(QueueSizeLimit));
            }
        }

        [UIValue("requeueLimit")]
        public float RequeueLimit
        {
            get => Plugin.Settings?.RequeueLimit ?? 10;
            set
            {
                if (Plugin.Settings != null) Plugin.Settings.RequeueLimit = Mathf.RoundToInt(value);
                NotifyPropertyChanged(nameof(RequeueLimit));
            }
        }








        // Footer text properties


        [UIComponent("support-button")]
        private Button _supportButton;

        [UIComponent("supporter-text")]
        private TMP_Text _supporterText;

        [UIComponent("supporter-tab")]
        private Tab _supporterTab;

        [UIValue("supporterTabVisible")]
        public bool SupporterTabVisible => PremiumVisualFeatureAccessController.HasAuthenticatedVisualsAccess();




        private void UpdateSupportUI()
        {
            bool isSupporter = SupporterTabVisible;

            if (_supporterTab != null)
                _supporterTab.IsVisible = isSupporter;

            if (_supportButton != null)
                _supportButton.gameObject.SetActive(!isSupporter);

            if (_supporterText != null)
                _supporterText.gameObject.SetActive(isSupporter);
        }




        [UIValue("subscribeButtonText")]
        public string SubscribeButtonText
        {
            get
            {
                if (SupporterTabVisible)
                {
                    // Subscriber - show unlocked message in green
                    return "<color=#00FF00>✓ Subscriber Features Unlocked ♡</color>";
                }
                else
                {
                    // Non-subscriber - show subscribe prompt in blue
                    return "<color=#0099FF>Subscribe to Support ♡ </color>";
                }
            }
        }

        // Called when Documentation button clicked
        [UIAction("OnDocumentationClicked")]
        private void OnDocumentationClicked()
        {
            LogUtils.Debug(() => "Opening GitHub documentation...");
            try
            {
                // Open GitHub repo in default browser
                System.Diagnostics.Process.Start("https://github.com/PhoenixtBlaze/SaberSurgeon");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to open documentation link: {ex.Message}");
            }
        }

        // Called when Subscribe button clicked
        [UIAction("OnSubscribeClicked")]
        private void OnSubscribeClicked()
        {
            if (SupporterTabVisible)
            {
                LogUtils.Debug(() => "User is already subscribed!");
                return;
            }

            LogUtils.Debug(() => "Opening Twitch channel for subscription...");
            try
            {
                // Open your Twitch channel in default browser
                System.Diagnostics.Process.Start("https://www.twitch.tv/phoenixblaze0");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to open Twitch link: {ex.Message}");
            }
        }

        // Update subscription button when status changes
        private void RefreshSubscriptionStatus()
        {
            // This will force the UI to re-evaluate SubscribeButtonText
            NotifyPropertyChanged(nameof(SubscribeButtonText));
        }

        //Call this when Twitch authentication succeeds or tier changes
        public void OnTwitchStatusUpdated()
        {
            RefreshSubscriptionStatus();
        }

    }

}


