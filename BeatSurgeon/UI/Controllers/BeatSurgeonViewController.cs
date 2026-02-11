using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using BeatSurgeon.Chat;
using BeatSurgeon.Twitch;
using BeatSurgeon.UI.Settings;
using System;
using System.IO;
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
            get => CommandHandler.RainbowEnabled;
            set
            {
                CommandHandler.RainbowEnabled = value;
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
            get => CommandHandler.DisappearEnabled;
            set
            {
                CommandHandler.DisappearEnabled = value;
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
            get => CommandHandler.GhostEnabled;
            set
            {
                CommandHandler.GhostEnabled = value;
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
            get => CommandHandler.BombEnabled;
            set
            {
                CommandHandler.BombEnabled = value;
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
            get => CommandHandler.FasterEnabled;
            set
            {
                CommandHandler.FasterEnabled = value;
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
            get => CommandHandler.SuperFastEnabled;
            set
            {
                CommandHandler.SuperFastEnabled = value;
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
            get => CommandHandler.SlowerEnabled;
            set
            {
                CommandHandler.SlowerEnabled = value;
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


        [UIValue("flashbang_enabled")]
        public bool FlashbangEnabled
        {
            get => CommandHandler.FlashbangEnabled;
            set
            {
                CommandHandler.FlashbangEnabled = value;
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

            UpdateSupportUI();

            if (firstActivation)
            {
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
                TwitchApiClient.OnSubscriberStatusChanged += UpdateSupportUI;

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
            RefreshTwitchStatusText();
            

        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            if (removedFromHierarchy && ReferenceEquals(ActiveInstance, this))
                ActiveInstance = null;

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









        // --- Twitch Integration Section ---

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

            int tier = Plugin.Settings.CachedSupporterTier;

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

            // Restart chat so it can pick up CachedBroadcasterId and use native WS
            var chatManager = ChatManager.GetInstance();
            chatManager.Shutdown();
            chatManager.Initialize();
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

            var gameplayManager = BeatSurgeon.Gameplay.GameplayManager.GetInstance();

            if (gameplayManager.IsPlaying())
            {
                gameplayManager.StopEndlessMode();
                LogUtils.Debug(() => "BeatSurgeon: Stopped endless mode");
                ChatManager.GetInstance().SendChatMessage("Saber Surgeon session ended!");
            }
            else
            {
                gameplayManager.StartEndlessMode(PlayTime);
                LogUtils.Debug(() => $"BeatSurgeon: Started endless mode for {PlayTime} minutes");
                ChatManager.GetInstance().SendChatMessage(
                    $"Saber Surgeon started! Playing for {PlayTime} minutes. Request songs with !bsr <code>");
            }
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




        private void UpdateSupportUI()
        {
            bool isAuthenticated = TwitchAuthManager.Instance.IsAuthenticated;

            bool isSupporter =
                isAuthenticated &&
                (
                    SupporterState.CurrentTier != SupporterTier.None ||
                    (Plugin.Settings?.CachedSupporterTier ?? 0) > 0
                );

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
                // Check if user is subscribed
                int supporterTier = Plugin.Settings?.CachedSupporterTier ?? 0;

                if (supporterTier > 0)
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
            int supporterTier = Plugin.Settings?.CachedSupporterTier ?? 0;

            if (supporterTier > 0)
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

