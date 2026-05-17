using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSurgeon.Chat;
using BeatSurgeon.Twitch;
using BeatSurgeon.UI.Settings;
using HMUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
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

        private const string TwitchSupportUrl = "https://www.twitch.tv/phoenixblaze0";
        private const string PatreonSupportUrl = "https://www.patreon.com/PhoenixBlaze0";

        private static readonly Sprite PatreonSupportSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.Patreon-Logo.png");
        private static readonly Sprite TwitchSupportSprite =
            LoadEmbeddedSprite("BeatSurgeon.Assets.Twitch-Logo.png");

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
                Gameplay.RankedMapDetectionService.Instance.OnSettingsChanged();
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
                Gameplay.RankedMapDetectionService.Instance.OnSettingsChanged();
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
                Gameplay.RankedMapDetectionService.Instance.OnSettingsChanged();
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
                Gameplay.RankedMapDetectionService.Instance.OnSettingsChanged();
                NotifyPropertyChanged(nameof(RankedAccSaber));
            }
        }

        // === Bomb Font Dropdown ===

        private List<object> _bombFontOptionsList = new List<object> { Gameplay.FontBundleLoader.DefaultSelectionValue };

        [UIValue("bomb_font_options")]
        public List<object> BombFontOptions => _bombFontOptionsList;

        [UIValue("bomb_font_selected")]
        public string BombFontSelected
        {
            get => Gameplay.FontBundleLoader.GetSelectedBombFontOption();
            set
            {
                Gameplay.FontBundleLoader.SetSelectedBombFontOption(value);
                NotifyPropertyChanged(nameof(BombFontSelected));
                UpdateFontPreview();
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

        [UIComponent("bomb-font-dropdown")]
        private DropDownListSetting _bombFontDropdown;

        [UIComponent("bomb-font-preview")]
        private TextMeshProUGUI _bombFontPreviewText;



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
            _ = RefreshFontDropdownAsync();

            // Subscribe to auth events for reauth notification display
            if (firstActivation)
            {
                TwitchAuthManager.Instance.OnReauthRequired += HandleSupportStateChanged;
                TwitchAuthManager.Instance.OnTokensUpdated += HandleSupportStateChanged;
                TwitchAuthManager.Instance.OnIdentityUpdated += HandleSupportStateChanged;
                EntitlementsState.Changed += HandleSupportStateChanged;

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

                ConfigureSupportPlatformLogo(patreonSupportLogo);
                ConfigureSupportPlatformLogo(twitchSupportLogo);

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
            ApplySupportPlatformLogos();
            BeginSupportPlatformLogoLoads();
            RefreshSupporterUiState();
            

        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            if (removedFromHierarchy && ReferenceEquals(ActiveInstance, this))
            {
                ActiveInstance = null;
                // Unsubscribe from auth events
                TwitchAuthManager.Instance.OnReauthRequired -= HandleSupportStateChanged;
                TwitchAuthManager.Instance.OnTokensUpdated -= HandleSupportStateChanged;
                TwitchAuthManager.Instance.OnIdentityUpdated -= HandleSupportStateChanged;
                EntitlementsState.Changed -= HandleSupportStateChanged;
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
            NotifyPropertyChanged(nameof(SupportButtonText));
            NotifyPropertyChanged(nameof(SupportButtonHoverHint));
            UpdateSupportUI();
            NotifyPropertyChanged(nameof(BitEffectEnabled));
            NotifyPropertyChanged(nameof(SubEffectsEnabled));
            NotifyPropertyChanged(nameof(SubEffectsToggleInteractable));
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

        private void UpdateFontPreview()
        {
            if (_bombFontPreviewText == null) return;
            var font = Gameplay.FontBundleLoader.BombUsernameFont;
            if (font != null)
            {
                _bombFontPreviewText.font = font;
                _bombFontPreviewText.fontMaterial = font.material;
                _bombFontPreviewText.SetAllDirty();
                _bombFontPreviewText.ForceMeshUpdate(true);
            }
        }

        private async Task RefreshFontDropdownAsync()
        {
            await Gameplay.FontBundleLoader.EnsureLoadedAsync();
            await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
            {
                var options = Gameplay.FontBundleLoader.GetBombFontOptions();
                _bombFontOptionsList.Clear();
                foreach (string opt in options)
                    _bombFontOptionsList.Add(opt);

                if (_bombFontDropdown != null)
                {
                    _bombFontDropdown.Values = _bombFontOptionsList;
                    _bombFontDropdown.UpdateChoices();
                    _bombFontDropdown.Value = Gameplay.FontBundleLoader.GetSelectedBombFontOption();
                }

                NotifyPropertyChanged(nameof(BombFontSelected));
                UpdateFontPreview();
            });
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

        [UIValue("subEffectsToggleInteractable")]
        public bool SubEffectsToggleInteractable => SubscriberEffectAccessController.IsToggleInteractable;

        [UIAction("OnSubEffectsChanged")]
        private void OnSubEffectsChanged(bool value)
        {
            SubscriberEffectAccessController.ApplyManualToggle(value);
            NotifyPropertyChanged(nameof(SubEffectsEnabled));
            NotifyPropertyChanged(nameof(SubEffectsToggleInteractable));
            _ = TwitchEventSubClient.Instance.RefreshSubscriptionsAsync();
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



        // --- Twitch Integration Section ---

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

            int tier = (int)SupporterState.CurrentTier;
            if (tier <= 0)
            {
                tier = Plugin.Settings.CachedSupporterTier;
            }

            if (tier > 0)
            {
                TwitchStatusText = $"<color=#44FF44>Connected</color> <color=#00FF99>• Supporter Verified (Tier {tier})</color>";
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

        [UIComponent("support-modal")]
        private ModalView _supportModal;

        [UIComponent("patreon-support-button")]
        private Button patreonSupportButton;

        [UIComponent("patreon-support-logo")]
        private Image patreonSupportLogo;

        [UIComponent("twitch-support-button")]
        private Button twitchSupportButton;

        [UIComponent("twitch-support-logo")]
        private Image twitchSupportLogo;

        [UIComponent("supporter-text")]
        private TMP_Text _supporterText;

        [UIComponent("supporter-tab")]
        private Tab _supporterTab;

        [UIValue("supporterTabVisible")]
        public bool SupporterTabVisible => SupporterState.CurrentTier != SupporterTier.None;

        [UIValue("supportButtonText")]
        public string SupportButtonText => "Support this project 💙";

        [UIValue("supportButtonHoverHint")]
        public string SupportButtonHoverHint => "Show Twitch support options and verify Patreon access";




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

        private void ConfigureSupportPlatformLogo(Image icon)
        {
            if (icon == null)
            {
                return;
            }

            var rectTransform = icon.rectTransform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(16f, 16f);
        }

        private void ApplySupportPlatformLogos()
        {
            if (patreonSupportButton != null)
            {
                BeatSaberUI.SetButtonText(patreonSupportButton, string.Empty);
            }

            if (twitchSupportButton != null)
            {
                BeatSaberUI.SetButtonText(twitchSupportButton, string.Empty);
            }

            ApplySupportPlatformLogo(patreonSupportLogo, PatreonSupportSprite);
            ApplySupportPlatformLogo(twitchSupportLogo, TwitchSupportSprite);
        }

        private void ApplySupportPlatformLogo(Image icon, Sprite sprite)
        {
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.gameObject.SetActive(sprite != null);
            }
        }

        private void BeginSupportPlatformLogoLoads()
        {
            // Sprites are pre-loaded from embedded assets at class init time; just apply them.
            ApplySupportPlatformLogos();
        }




        [UIValue("subscribeButtonText")]
        public string SubscribeButtonText
        {
            get
            {
                if (SupporterTabVisible)
                {
                    return "<color=#00FF00>✓ Supporter Features Unlocked ♡</color>";
                }
                else
                {
                    return "<color=#0099FF>Support the Project ♡ </color>";
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

        [UIAction("OnSupportButtonClicked")]
        private void OnSupportButtonClicked()
        {
            if (SupporterTabVisible)
            {
                LogUtils.Debug(() => "User is already subscribed!");
                return;
            }

            if (_supportModal == null)
            {
                Plugin.Log.Warn("Support modal was null when trying to show it.");
                return;
            }

            _supportModal.Show(true);
        }

        [UIAction("OnSupportModalCloseClicked")]
        private void OnSupportModalCloseClicked()
        {
            if (_supportModal == null)
            {
                return;
            }

            _supportModal.Hide(true);
        }

        [UIAction("OnSupportPatreonClicked")]
        private void OnSupportPatreonClicked()
        {
            _ = ConnectPatreonAsync();
        }

        [UIAction("OnSupportTwitchClicked")]
        private void OnSupportTwitchClicked()
        {
            OpenSupportUrl(TwitchSupportUrl, "Twitch");
        }

        private void OpenSupportUrl(string url, string platformName)
        {
            LogUtils.Debug(() => $"Opening {platformName} support page...");
            try
            {
                Application.OpenURL(url);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to open {platformName} link: {ex.Message}");
            }
        }

        private async Task ConnectPatreonAsync()
        {
            try
            {
                if (!PatreonAuthManager.Instance.IsAuthenticated || PatreonAuthManager.Instance.IsReauthRequired)
                {
                    await PatreonAuthManager.Instance.InitiateLogin().ConfigureAwait(false);
                }
                else
                {
                    await PatreonAuthManager.Instance.EnsureReadyAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("Patreon verification failed: " + ex.Message);
            }
            finally
            {
                await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    RefreshSupporterUiState();
                    if (SupporterTabVisible && _supportModal != null)
                    {
                        _supportModal.Hide(true);
                    }
                });
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


