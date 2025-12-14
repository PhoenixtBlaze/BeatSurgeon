using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using SaberSurgeon.Chat;
using SaberSurgeon.Twitch;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


namespace SaberSurgeon.UI.Controllers
{
    [ViewDefinition("SaberSurgeon.UI.Views.SaberSurgeonSettings.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\SaberSurgeonSettings.bsml")]
    public class SaberSurgeonViewController : BSMLAutomaticViewController
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
                        Plugin.Log.Error($"[SaberSurgeon] Failed to find embedded resource '{resourcePath}'");
                        return null;
                    }

                    var bytes = new byte[stream.Length];
                    _ = stream.Read(bytes, 0, bytes.Length);

                    var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (!tex.LoadImage(bytes, markNonReadable: false))
                    {
                        Plugin.Log.Error($"[SaberSurgeon] Failed to decode texture from '{resourcePath}'");
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
                    sprite.name = "SaberSurgeonRainbowIcon";

                    return sprite;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.Error($"[SaberSurgeon] Exception loading embedded sprite '{resourcePath}': {ex}");
                return null;
            }
        }


        // Loaded once from embedded resource
        private static readonly Sprite RainbowOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.Rainbow.png");

        private static readonly Sprite RainbowOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.RainbowGB.png");

        // DA icons (off = DA, on = DAGB)
        private static readonly Sprite DAOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.DA.png");

        private static readonly Sprite DAOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.DAGB.png");

        // Ghost icons (off = GhostNotes, on = GhostNotesGB)
        private static readonly Sprite GhostOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.GhostNotes.png");

        private static readonly Sprite GhostOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.GhostNotesGB.png");

        //Button Icons ((off = Bomb, on = BombGB)
        private static readonly Sprite BombOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.Bomb.png");

        private static readonly Sprite BombOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.BombGB.png");

        // Faster icons (off = FasterSong, on = FasterSongGB)
        private static readonly Sprite FasterOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.FasterSong.png");
        private static readonly Sprite FasterOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.FasterSongGB.png");

        // SuperFast icons
        private static readonly Sprite SuperFastOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.SuperFastSong.png");
        private static readonly Sprite SuperFastOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.SuperFastSongGB.png");

        // Slower icons
        private static readonly Sprite SlowerOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.SlowerSong.png");
        private static readonly Sprite SlowerOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.SlowerSongGB.png");

        // Flashbang icons (off = Flashbang, on = FlashbangGB)
        private static readonly Sprite FlashbangOffSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.Flashbang.png");
        private static readonly Sprite FlashbangOnSprite =
            LoadEmbeddedSprite("SaberSurgeon.Assets.FlashbangGB.png");



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
                Plugin.Log.Info($"Slider changed → PlayTime = {_playTime} minutes ");
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
            Plugin.Log.Info($"SaberSurgeon: Flashbang command enabled = {FlashbangEnabled}");
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
            Plugin.Log.Info($"SaberSurgeon: Slower command enabled = {SlowerEnabled}");
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
            Plugin.Log.Info($"SaberSurgeon: SuperFast command enabled = {SuperFastEnabled}");
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
            Plugin.Log.Info($"SaberSurgeon: Faster command enabled = {FasterEnabled}");
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
            Plugin.Log.Info($"SaberSurgeon: Bomb command enabled = {BombEnabled}");
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
            Plugin.Log.Info($"SaberSurgeon: Disappearing command enabled = {DisappearingEnabled}");
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
            Plugin.Log.Info($"SaberSurgeon: Ghost command enabled = {GhostEnabled}");
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
            Plugin.Log.Info(
                $"UI: RefreshTwitchStatusText IsAuthenticated={TwitchAuthManager.Instance.IsAuthenticated}, " +
                $"Tier={Plugin.Settings.CachedSupporterTier}");

            if (!IsTwitchConnected())
            {
                TwitchStatusText = "<color=#FF4444>Not connected</color>";
                return;
            }

            var name = TwitchApiClient.Instance.BroadcasterName
                       ?? Plugin.Settings.CachedBroadcasterLogin
                       ?? "Unknown";

            int tier = Plugin.Settings.CachedSupporterTier;

            if (tier > 0)
                TwitchStatusText = $"<color=#44FF44>Connected (Tier {tier})</color>";
            else
                TwitchStatusText = "<color=#44FF44>Connected</color>";
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
            Plugin.Log.Info($"SaberSurgeon: Rainbow command enabled = {RainbowEnabled}");
        }

        [UIAction("OnStartPlayPressed")]
        private void OnStartPlayPressed()
        {
            Plugin.Log.Info("SaberSurgeon: Start/Play button pressed!");
            Plugin.Log.Info($"Timer set to: {PlayTime} minutes");

            var gameplayManager = SaberSurgeon.Gameplay.GameplayManager.GetInstance();

            if (gameplayManager.IsPlaying())
            {
                gameplayManager.StopEndlessMode();
                Plugin.Log.Info("SaberSurgeon: Stopped endless mode");
                ChatManager.GetInstance().SendChatMessage("Saber Surgeon session ended!");
            }
            else
            {
                gameplayManager.StartEndlessMode(PlayTime);
                Plugin.Log.Info($"SaberSurgeon: Started endless mode for {PlayTime} minutes");
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
    }

}

