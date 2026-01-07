using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using SaberSurgeon.Chat;
using SaberSurgeon.Twitch;
using SaberSurgeon.UI.Controllers;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SaberSurgeon.UI.Settings
{
    // NotifiableBase implements INotifyPropertyChanged for "~Value" bindings like "~TwitchStatusText"
    // so the UI updates when the property changes.
    internal sealed class SurgeonGameplaySetupHost : NotifiableBase
    {

        // ===== Twitch Channel Point settings (Rainbow) =====

        private bool _rainbowCpEnabled;
        private Color _rainbowCpBackgroundColor = Color.white;

        private int _rainbowCpCost = 500;
        private string _rainbowCpCostText = "500";
        private int _rainbowCpCooldownSeconds = 0;


        private bool _daCpEnabled;
        private int _daCpCost = 500;
        private string _daCpCostText = "500";
        private int _daCpCooldownSeconds = 0;

        private bool _ghostCpEnabled;
        private int _ghostCpCost = 500;
        private string _ghostCpCostText = "500";
        private int _ghostCpCooldownSeconds = 0;

        private bool _bombCpEnabled;
        private int _bombCpCost = 500;
        private string _bombCpCostText = "500";
        private int _bombCpCooldownSeconds = 0;

        private bool _fasterCpEnabled;
        private int _fasterCpCost = 500;
        private string _fasterCpCostText = "500";
        private int _fasterCpCooldownSeconds = 0;

        private bool _superFastCpEnabled;
        private int _superFastCpCost = 500;
        private string _superFastCpCostText = "500";
        private int _superFastCpCooldownSeconds = 0;

        private bool _slowerCpEnabled;
        private int _slowerCpCost = 500;
        private string _slowerCpCostText = "500";
        private int _slowerCpCooldownSeconds = 0;

        private bool _flashbangCpEnabled;
        private int _flashbangCpCost = 500;
        private string _flashbangCpCostText = "500";
        private int _flashbangCpCooldownSeconds = 0;



        private static SurgeonGameplaySetupHost _instance;
        public static SurgeonGameplaySetupHost Instance
        {
            get
            {
                if (_instance == null) _instance = new SurgeonGameplaySetupHost();
                return _instance;
            }
        }

        private SurgeonGameplaySetupHost() { }

        [UIParams]
        private BSMLParserParams _parserParams;

        // --- Embedded icon sprites (same resource names used by SaberSurgeonViewController) ---
        private static Sprite LoadEmbeddedSprite(string resourcePath)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var stream = asm.GetManifestResourceStream(resourcePath);

                if (stream == null)
                {
                    Plugin.Log.Error($"[SurgeonGameplaySetupHost] Missing embedded sprite: {resourcePath}");
                    return null;
                }

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);

                var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!tex.LoadImage(bytes, markNonReadable: false))
                {
                    Plugin.Log.Error($"[SurgeonGameplaySetupHost] Failed to decode texture: {resourcePath}");
                    return null;
                }

                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;

                return Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[SurgeonGameplaySetupHost] Exception loading sprite {resourcePath}: {ex}");
                return null;
            }
        }

        private static readonly Sprite RainbowOffSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.Rainbow.png");
        private static readonly Sprite RainbowOnSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.RainbowGB.png");

        private static readonly Sprite DAOffSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.DA.png");
        private static readonly Sprite DAOnSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.DAGB.png");

        private static readonly Sprite GhostOffSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.GhostNotes.png");
        private static readonly Sprite GhostOnSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.GhostNotesGB.png");

        private static readonly Sprite BombOffSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.Bomb.png");
        private static readonly Sprite BombOnSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.BombGB.png");

        private static readonly Sprite FasterOffSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.FasterSong.png");
        private static readonly Sprite FasterOnSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.FasterSongGB.png");

        private static readonly Sprite SuperFastOffSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.SuperFastSong.png");
        private static readonly Sprite SuperFastOnSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.SuperFastSongGB.png");

        private static readonly Sprite SlowerOffSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.SlowerSong.png");
        private static readonly Sprite SlowerOnSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.SlowerSongGB.png");

        private static readonly Sprite FlashbangOffSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.Flashbang.png");
        private static readonly Sprite FlashbangOnSprite = LoadEmbeddedSprite("SaberSurgeon.Assets.FlashbangGB.png");

        private readonly Color offColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        private readonly Color onColor = new Color(0.18f, 0.7f, 1f, 1f);

        private static string ToHex(Color c)
        {
            byte r = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f);
            byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f);
            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
            return $"#{r:X2}{g:X2}{b:X2}";
        }


        // --- UIComponents (must match ids in your GameplaySetup BSML) ---
        [UIComponent("rainbowbutton")] private Button _rainbowButton;
        [UIComponent("rainbowicon")] private Image _rainbowIcon;

        [UIComponent("dabutton")] private Button _daButton;
        [UIComponent("daicon")] private Image _daIcon;

        [UIComponent("ghostbutton")] private Button _ghostButton;
        [UIComponent("ghosticon")] private Image _ghostIcon;

        [UIComponent("bombbutton")] private Button _bombButton;
        [UIComponent("bombicon")] private Image _bombIcon;

        [UIComponent("fasterbutton")] private Button _fasterButton;
        [UIComponent("fastericon")] private Image _fasterIcon;

        [UIComponent("superfastbutton")] private Button _superFastButton;
        [UIComponent("superfasticon")] private Image _superFastIcon;

        [UIComponent("slowerbutton")] private Button _slowerButton;
        [UIComponent("slowericon")] private Image _slowerIcon;

        [UIComponent("flashbangbutton")] private Button _flashbangButton;
        [UIComponent("flashbangicon")] private Image _flashbangIcon;

        [UIComponent("support-button")] private Button _supportButton;
        [UIComponent("supporter-text")] private TextMeshProUGUI _supporterText;


        // --- Twitch modal buttons (icons) ---
        [UIComponent("twitch-rainbow-button")] private Button _twitchRainbowButton;
        [UIComponent("twitch-rainbow-icon")] private Image _twitchRainbowIcon;

        [UIComponent("twitch-ghost-button")] private Button _twitchGhostButton;
        [UIComponent("twitch-ghost-icon")] private Image _twitchGhostIcon;

        [UIComponent("twitch-bomb-button")] private Button _twitchBombButton;
        [UIComponent("twitch-bomb-icon")] private Image _twitchBombIcon;

        [UIComponent("twitch-da-button")] private Button _twitchDAButton;
        [UIComponent("twitch-da-icon")] private Image _twitchDAIcon;

        [UIComponent("twitch-faster-button")] private Button _twitchFasterButton;
        [UIComponent("twitch-faster-icon")] private Image _twitchFasterIcon;

        [UIComponent("twitch-superfast-button")] private Button _twitchSuperFastButton;
        [UIComponent("twitch-superfast-icon")] private Image _twitchSuperFastIcon;

        [UIComponent("twitch-slower-button")] private Button _twitchSlowerButton;
        [UIComponent("twitch-slower-icon")] private Image _twitchSlowerIcon;

        [UIComponent("twitch-flashbang-button")] private Button _twitchFlashbangButton;
        [UIComponent("twitch-flashbang-icon")] private Image _twitchFlashbangIcon;

        // --- Twitch modals ---
        [UIComponent("twitch-rainbow-modal")] private ModalView _twitchRainbowModal;
        [UIComponent("twitch-ghost-modal")] private ModalView _twitchGhostModal;
        [UIComponent("twitch-bomb-modal")] private ModalView _twitchBombModal;
        [UIComponent("twitch-da-modal")] private ModalView _twitchDAModal;
        [UIComponent("twitch-faster-modal")] private ModalView _twitchFasterModal;
        [UIComponent("twitch-superfast-modal")] private ModalView _twitchSuperFastModal;
        [UIComponent("twitch-slower-modal")] private ModalView _twitchSlowerModal;
        [UIComponent("twitch-flashbang-modal")] private ModalView _twitchFlashbangModal;



        //--

        [UIAction("OnTwitchRainbowButtonClicked")] private void OnTwitchRainbowButtonClicked() => _ = ToggleCpAsync("rainbow");
        [UIAction("OnTwitchDAButtonClicked")] private void OnTwitchDAButtonClicked() => _ = ToggleCpAsync("disappear");
        [UIAction("OnTwitchGhostButtonClicked")] private void OnTwitchGhostButtonClicked() => _ = ToggleCpAsync("ghost");
        [UIAction("OnTwitchBombButtonClicked")] private void OnTwitchBombButtonClicked() => _ = ToggleCpAsync("bomb");
        [UIAction("OnTwitchFasterButtonClicked")] private void OnTwitchFasterButtonClicked() => _ = ToggleCpAsync("faster");
        [UIAction("OnTwitchSuperFastButtonClicked")] private void OnTwitchSuperFastButtonClicked() => _ = ToggleCpAsync("superfast");
        [UIAction("OnTwitchSlowerButtonClicked")] private void OnTwitchSlowerButtonClicked() => _ = ToggleCpAsync("slower");
        [UIAction("OnTwitchFlashbangButtonClicked")] private void OnTwitchFlashbangButtonClicked() => _ = ToggleCpAsync("flashbang");




        // --- Called after BSML has assigned UIComponent fields ---
        [UIAction("#post-parse")]
        public void PostParse()
        {
            // Initialize tab-selectors (outer + nested) so child tabs are selectable.
            EnsureAllTabSelectorsSetup();

            // Hide button text elements to show only icons.
            HideButtonText(_rainbowButton);
            HideButtonText(_daButton);
            HideButtonText(_ghostButton);
            HideButtonText(_bombButton);
            HideButtonText(_fasterButton);
            HideButtonText(_superFastButton);
            HideButtonText(_slowerButton);
            HideButtonText(_flashbangButton);
            HideButtonText(_twitchRainbowButton);
            HideButtonText(_twitchDAButton);
            HideButtonText(_twitchGhostButton);
            HideButtonText(_twitchBombButton);
            HideButtonText(_twitchFasterButton);
            HideButtonText(_twitchSuperFastButton);
            HideButtonText(_twitchSlowerButton);
            HideButtonText(_twitchFlashbangButton);
            HideButtonText(_twitchSlowerButton);


            // Initialize visuals for the command buttons.
            UpdateAllCommandButtonVisuals();
            LoadCpFromConfig();
            UpdateTwitchCpButtonVisuals();
            // Initialize status text and supporter UI visibility.
            InitTwitchIconsIndependent();
            RefreshTwitchStatusText();
            UpdateSupporterUi();
        }

        private async Task ToggleCpAsync(string key)
        {
            try
            {
                var cfg = Plugin.Settings;
                if (cfg == null)
                    return;

                // flip local + persist + sync Helix reward
                switch (key)
                {
                    case "rainbow":
                        _rainbowCpEnabled = !_rainbowCpEnabled;
                        cfg.CpRainbowEnabled = _rainbowCpEnabled;
                        await SyncRewardAsync(
                            title: "SaberSurgeon: Rainbow",
                            prompt: "Triggers Rainbow",
                            cost: _rainbowCpCost,
                            cooldown: _rainbowCpCooldownSeconds,
                            enabled: _rainbowCpEnabled,
                            getId: () => cfg.CpRainbowRewardId,
                            setId: id => cfg.CpRainbowRewardId = id
                        );
                        break;

                    case "disappear":
                        _daCpEnabled = !_daCpEnabled;
                        cfg.CpDisappearEnabled = _daCpEnabled;
                        await SyncRewardAsync(
                            "SaberSurgeon: Disappear",
                            "Triggers Disappearing Arrows",
                            _daCpCost,
                            _daCpCooldownSeconds,
                            _daCpEnabled,
                            () => cfg.CpDisappearRewardId,
                            id => cfg.CpDisappearRewardId = id
                        );
                        break;

                    case "ghost":
                        _ghostCpEnabled = !_ghostCpEnabled;
                        cfg.CpGhostEnabled = _ghostCpEnabled;
                        await SyncRewardAsync(
                            "SaberSurgeon: Ghost Notes",
                            "Triggers Ghost Notes",
                            _ghostCpCost,
                            _ghostCpCooldownSeconds,
                            _ghostCpEnabled,
                            () => cfg.CpGhostRewardId,
                            id => cfg.CpGhostRewardId = id
                        );
                        break;

                    case "bomb":
                        _bombCpEnabled = !_bombCpEnabled;
                        cfg.CpBombEnabled = _bombCpEnabled;
                        await SyncRewardAsync(
                            "SaberSurgeon: Bomb",
                            "Triggers Bomb",
                            _bombCpCost,
                            _bombCpCooldownSeconds,
                            _bombCpEnabled,
                            () => cfg.CpBombRewardId,
                            id => cfg.CpBombRewardId = id
                        );
                        break;

                    case "faster":
                        _fasterCpEnabled = !_fasterCpEnabled;
                        cfg.CpFasterEnabled = _fasterCpEnabled;
                        await SyncRewardAsync(
                            "SaberSurgeon: Faster",
                            "Triggers Faster Song",
                            _fasterCpCost,
                            _fasterCpCooldownSeconds,
                            _fasterCpEnabled,
                            () => cfg.CpFasterRewardId,
                            id => cfg.CpFasterRewardId = id
                        );
                        break;

                    case "superfast":
                        _superFastCpEnabled = !_superFastCpEnabled;
                        cfg.CpSuperFastEnabled = _superFastCpEnabled;
                        await SyncRewardAsync(
                            "SaberSurgeon: SuperFast",
                            "Triggers SuperFast Song",
                            _superFastCpCost,
                            _superFastCpCooldownSeconds,
                            _superFastCpEnabled,
                            () => cfg.CpSuperFastRewardId,
                            id => cfg.CpSuperFastRewardId = id
                        );
                        break;

                    case "slower":
                        _slowerCpEnabled = !_slowerCpEnabled;
                        cfg.CpSlowerEnabled = _slowerCpEnabled;
                        await SyncRewardAsync(
                            "SaberSurgeon: Slower",
                            "Triggers Slower Song",
                            _slowerCpCost,
                            _slowerCpCooldownSeconds,
                            _slowerCpEnabled,
                            () => cfg.CpSlowerRewardId,
                            id => cfg.CpSlowerRewardId = id
                        );
                        break;

                    case "flashbang":
                        _flashbangCpEnabled = !_flashbangCpEnabled;
                        cfg.CpFlashbangEnabled = _flashbangCpEnabled;
                        await SyncRewardAsync(
                            "SaberSurgeon: Flashbang",
                            "Triggers Flashbang",
                            _flashbangCpCost,
                            _flashbangCpCooldownSeconds,
                            _flashbangCpEnabled,
                            () => cfg.CpFlashbangRewardId,
                            id => cfg.CpFlashbangRewardId = id
                        );
                        break;
                }

                NotifyPropertyChanged(nameof(RainbowCpEnabled));
                UpdateTwitchCpButtonVisuals();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("ToggleCpAsync failed: " + ex.Message);
            }
        }

        private async Task SyncRewardAsync(
        string title,
        string prompt,
        int cost,
        int cooldown,
        bool enabled,
        Func<string> getId,
        Action<string> setId,
        string backgroundColorHex = null)
        {
                var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

                var id = await TwitchChannelPointsManager.Instance.EnsureRewardAsync(
                    new TwitchChannelPointsManager.RewardSpec
                    {
                        Title = title,
                        Prompt = prompt,
                        Cost = cost,
                        CooldownSeconds = cooldown,
                        BackgroundColorHex = backgroundColorHex
                    },
                    storedRewardId: getId?.Invoke() ?? "",
                    saveRewardId: setId,
                    enabled: enabled,
                    ct: ct
                );

                setId?.Invoke(id);
        }




        private void LoadCpFromConfig()
        {
            var cfg = Plugin.Settings;
            if (cfg == null) return;

            _rainbowCpEnabled = cfg.CpRainbowEnabled;
            _rainbowCpCost = cfg.CpRainbowCost;
            _rainbowCpCostText = _rainbowCpCost.ToString();
            _rainbowCpCooldownSeconds = cfg.CpRainbowCooldownSeconds;

            _daCpEnabled = cfg.CpDisappearEnabled;
            _daCpCost = cfg.CpDisappearCost;
            _daCpCostText = _daCpCost.ToString();
            _daCpCooldownSeconds = cfg.CpDisappearCooldownSeconds;

            _ghostCpEnabled = cfg.CpGhostEnabled;
            _ghostCpCost = cfg.CpGhostCost;
            _ghostCpCostText = _ghostCpCost.ToString();
            _ghostCpCooldownSeconds = cfg.CpGhostCooldownSeconds;

            _bombCpEnabled = cfg.CpBombEnabled;
            _bombCpCost = cfg.CpBombCost;
            _bombCpCostText = _bombCpCost.ToString();
            _bombCpCooldownSeconds = cfg.CpBombCooldownSeconds;

            _fasterCpEnabled = cfg.CpFasterEnabled;
            _fasterCpCost = cfg.CpFasterCost;
            _fasterCpCostText = _fasterCpCost.ToString();
            _fasterCpCooldownSeconds = cfg.CpFasterCooldownSeconds;

            _superFastCpEnabled = cfg.CpSuperFastEnabled;
            _superFastCpCost = cfg.CpSuperFastCost;
            _superFastCpCostText = _superFastCpCost.ToString();
            _superFastCpCooldownSeconds = cfg.CpSuperFastCooldownSeconds;

            _slowerCpEnabled = cfg.CpSlowerEnabled;
            _slowerCpCost = cfg.CpSlowerCost;
            _slowerCpCostText = _slowerCpCost.ToString();
            _slowerCpCooldownSeconds = cfg.CpSlowerCooldownSeconds;

            _flashbangCpEnabled = cfg.CpFlashbangEnabled;
            _flashbangCpCost = cfg.CpFlashbangCost;
            _flashbangCpCostText = _flashbangCpCost.ToString();
            _flashbangCpCooldownSeconds = cfg.CpFlashbangCooldownSeconds;
        }

        private void UpdateTwitchCpButtonVisuals()
        {
            // Use GB sprite when CP is enabled for that command.
            SetButtonVisual(_twitchRainbowButton, _twitchRainbowIcon, _rainbowCpEnabled, RainbowOnSprite, RainbowOffSprite);
            SetButtonVisual(_twitchDAButton, _twitchDAIcon, _daCpEnabled, DAOnSprite, DAOffSprite);
            SetButtonVisual(_twitchGhostButton, _twitchGhostIcon, _ghostCpEnabled, GhostOnSprite, GhostOffSprite);
            SetButtonVisual(_twitchBombButton, _twitchBombIcon, _bombCpEnabled, BombOnSprite, BombOffSprite);
            SetButtonVisual(_twitchFasterButton, _twitchFasterIcon, _fasterCpEnabled, FasterOnSprite, FasterOffSprite);
            SetButtonVisual(_twitchSuperFastButton, _twitchSuperFastIcon, _superFastCpEnabled, SuperFastOnSprite, SuperFastOffSprite);
            SetButtonVisual(_twitchSlowerButton, _twitchSlowerIcon, _slowerCpEnabled, SlowerOnSprite, SlowerOffSprite);
            SetButtonVisual(_twitchFlashbangButton, _twitchFlashbangIcon, _flashbangCpEnabled, FlashbangOnSprite, FlashbangOffSprite);
        }





        private static void HideButtonText(Button b)
        {
            var t = b != null ? b.transform.Find("Content/Text") : null;
            if (t != null) t.gameObject.SetActive(false);
        }



        private void InitTwitchIconsIndependent()
        {
            UpdateTwitchCpButtonVisuals();
        }



        private void EnsureAllTabSelectorsSetup()
        {
            // Find a transform that is guaranteed to exist in this view.
            // (support button is outside the tabs in your BSML; if missing, fall back to any command button.)
            var root = _supportButton != null
                ? _supportButton.transform
                : (_rainbowButton != null ? _rainbowButton.transform : null);

            if (root == null) return;

            // This only searches within this BSML view hierarchy, not the whole scene.
            foreach (var selector in root.GetComponentsInChildren<TabSelector>(true))
            {
                selector.Setup();
            }
        }

        // --- State (match existing behavior) ---
        private bool RainbowEnabled
        {
            get => CommandHandler.RainbowEnabled;
            set
            {
                CommandHandler.RainbowEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.RainbowEnabled = value;
                UpdateRainbowButtonVisual();
                SaberSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool DisappearingEnabled
        {
            get => CommandHandler.DisappearEnabled;
            set
            {
                CommandHandler.DisappearEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.DisappearEnabled = value;
                UpdateDAButtonVisual();
                SaberSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool GhostEnabled
        {
            get => CommandHandler.GhostEnabled;
            set
            {
                CommandHandler.GhostEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.GhostEnabled = value;
                UpdateGhostButtonVisual();
                SaberSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool BombEnabled
        {
            get => CommandHandler.BombEnabled;
            set
            {
                CommandHandler.BombEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.BombEnabled = value;
                UpdateBombButtonVisual();
                SaberSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool FasterEnabled
        {
            get => CommandHandler.FasterEnabled;
            set
            {
                CommandHandler.FasterEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.FasterEnabled = value;
                UpdateFasterButtonVisual();
                SaberSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool SuperFastEnabled
        {
            get => CommandHandler.SuperFastEnabled;
            set
            {
                CommandHandler.SuperFastEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.SuperFastEnabled = value;
                UpdateSuperFastButtonVisual();
                SaberSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool SlowerEnabled
        {
            get => CommandHandler.SlowerEnabled;
            set
            {
                CommandHandler.SlowerEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.SlowerEnabled = value;
                UpdateSlowerButtonVisual();
                SaberSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool FlashbangEnabled
        {
            get => CommandHandler.FlashbangEnabled;
            set
            {
                CommandHandler.FlashbangEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.FlashbangEnabled = value;
                UpdateFlashbangButtonVisual();
                SaberSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }



        // --- Surgeon Commands tab button handlers
        [UIAction("OnRainbowButtonClicked")]
        private void OnRainbowButtonClicked()
        {
            RainbowEnabled = !RainbowEnabled;
            LogUtils.Debug(() => $"SaberSurgeon: Rainbow command enabled = {RainbowEnabled}");
        }

        [UIAction("OnDAButtonClicked")]
        private void OnDAButtonClicked()
        {
            DisappearingEnabled = !DisappearingEnabled;
            LogUtils.Debug(() => $"SaberSurgeon: Disappearing Arrows command enabled = {DisappearingEnabled}");
        }

        [UIAction("OnGhostButtonClicked")]
        private void OnGhostButtonClicked()
        {
            GhostEnabled = !GhostEnabled;
            LogUtils.Debug(() => $"SaberSurgeon: Ghost command enabled = {GhostEnabled}");
        }

        [UIAction("OnBombButtonClicked")]
        private void OnBombButtonClicked()
        {
            BombEnabled = !BombEnabled;
            LogUtils.Debug(() => $"SaberSurgeon: Bomb command enabled = {BombEnabled}");
        }

        [UIAction("OnFasterButtonClicked")]
        private void OnFasterButtonClicked()
        {
            FasterEnabled = !FasterEnabled;
            LogUtils.Debug(() => $"SaberSurgeon: Faster command enabled = {FasterEnabled}");
        }

        [UIAction("OnSFastButtonClicked")]
        private void OnSFastButtonClicked()
        {
            SuperFastEnabled = !SuperFastEnabled;
            LogUtils.Debug(() => $"SaberSurgeon: SuperFast command enabled = {SuperFastEnabled}");
        }

        [UIAction("OnSlowerButtonClicked")]
        private void OnSlowerButtonClicked()
        {
            SlowerEnabled = !SlowerEnabled;
            LogUtils.Debug(() => $"SaberSurgeon: Slower command enabled = {SlowerEnabled}");
        }

        [UIAction("OnFlashbangButtonClicked")]
        private void OnFlashbangButtonClicked()
        {
            FlashbangEnabled = !FlashbangEnabled;
            LogUtils.Debug(() => $"SaberSurgeon: Flashbang command enabled = {FlashbangEnabled}");
        }


        public static void RefreshGameplaySetupIcons()
        {
            // Safe even if the GameplaySetup UI is not currently open,
            // because all UIComponent references will be null.
            Instance?.UpdateAllCommandButtonVisuals();
        }




        // --- UIActions to open/close modals ---
        [UIAction("OpenTwitchRainbowModal")] private void OpenTwitchRainbowModal() => _twitchRainbowModal?.Show(true);
        [UIAction("CloseTwitchRainbowModal")] private void CloseTwitchRainbowModal() => _twitchRainbowModal?.Hide(true);

        [UIAction("OpenTwitchGhostModal")] private void OpenTwitchGhostModal() => _twitchGhostModal?.Show(true);
        [UIAction("CloseTwitchGhostModal")] private void CloseTwitchGhostModal() => _twitchGhostModal?.Hide(true);

        [UIAction("OpenTwitchBombModal")] private void OpenTwitchBombModal() => _twitchBombModal?.Show(true);
        [UIAction("CloseTwitchBombModal")] private void CloseTwitchBombModal() => _twitchBombModal?.Hide(true);

        [UIAction("OpenTwitchDAModal")] private void OpenTwitchDAModal() => _twitchDAModal?.Show(true);
        [UIAction("CloseTwitchDAModal")] private void CloseTwitchDAModal() => _twitchDAModal?.Hide(true);

        [UIAction("OpenTwitchFasterModal")] private void OpenTwitchFasterModal() => _twitchFasterModal?.Show(true);
        [UIAction("CloseTwitchFasterModal")] private void CloseTwitchFasterModal() => _twitchFasterModal?.Hide(true);

        [UIAction("OpenTwitchSuperFastModal")] private void OpenTwitchSuperFastModal() => _twitchSuperFastModal?.Show(true);
        [UIAction("CloseTwitchSuperFastModal")] private void CloseTwitchSuperFastModal() => _twitchSuperFastModal?.Hide(true);

        [UIAction("OpenTwitchSlowerModal")] private void OpenTwitchSlowerModal() => _twitchSlowerModal?.Show(true);
        [UIAction("CloseTwitchSlowerModal")] private void CloseTwitchSlowerModal() => _twitchSlowerModal?.Hide(true);

        [UIAction("OpenTwitchFlashbangModal")] private void OpenTwitchFlashbangModal() => _twitchFlashbangModal?.Show(true);
        [UIAction("CloseTwitchFlashbangModal")] private void CloseTwitchFlashbangModal() => _twitchFlashbangModal?.Hide(true);


        // --- Twitch UI bits (your BSML uses "~TwitchStatusText") ---
        private string _twitchStatusText = "Not connected";

        [UIValue("TwitchStatusText")]
        public string TwitchStatusText
        {
            get => _twitchStatusText;
            private set
            {
                if (_twitchStatusText == value) return;
                _twitchStatusText = value;
                NotifyPropertyChanged();
            }
        }

        // Keep this action even if your current BSML doesn’t expose a button for it yet.
        [UIAction("OnConnectTwitchClicked")]
        private void OnConnectTwitchClicked() => _ = ConnectTwitchAsync();

        private async Task ConnectTwitchAsync()
        {
            TwitchStatusText = "Opening browser...";
            await TwitchAuthManager.Instance.InitiateLogin();

            // Small delay so auth state has time to update before we refresh text.
            await Task.Delay(1000);

            RefreshTwitchStatusText();

            var chatManager = ChatManager.GetInstance();
            chatManager.Shutdown();
            chatManager.Initialize();
        }

        private void RefreshTwitchStatusText()
        {
            if (Plugin.Settings?.TwitchReauthRequired == true)
            {
                TwitchStatusText = "Please Reauthorize";
                return;
            }

            if (!TwitchAuthManager.Instance.IsAuthenticated)
            {
                TwitchStatusText = "Not connected";
                return;
            }

            var name = TwitchApiClient.Instance.BroadcasterName
                       ?? Plugin.Settings?.CachedBroadcasterLogin
                       ?? "Unknown";

            TwitchStatusText = $"Connected ({name})";
        }

        // --- Supporter UI ---
        private void UpdateSupporterUi()
        {
            var isSupporter = SupporterState.CurrentTier != SupporterTier.None;

            if (_supportButton != null)
                _supportButton.gameObject.SetActive(!isSupporter);

            if (_supporterText != null)
                _supporterText.gameObject.SetActive(isSupporter);
        }

        [UIAction("OnSubscribeClicked")]
        private void OnSubscribeClicked()
        {
            try
            {
                System.Diagnostics.Process.Start("https://www.twitch.tv/phoenixblaze0");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to open Twitch link: {ex.Message}");
            }
        }

        // --- Visual updates ---
        private void SetButtonVisual(Button button, Image icon, bool enabled, Sprite onSprite, Sprite offSprite)
        {
            if (icon != null)
                icon.sprite = enabled ? onSprite : offSprite;

            // Tint the button background (the button root has an Image in BS UI prefabs).
            var bg = button != null ? button.GetComponent<Image>() : null;
            if (bg != null)
                bg.color = enabled ? onColor : offColor;
        }

        private void UpdateAllCommandButtonVisuals()
        {
            UpdateRainbowButtonVisual();
            UpdateDAButtonVisual();
            UpdateGhostButtonVisual();
            UpdateBombButtonVisual();
            UpdateFasterButtonVisual();
            UpdateSuperFastButtonVisual();
            UpdateSlowerButtonVisual();
            UpdateFlashbangButtonVisual();
        }

        private void UpdateRainbowButtonVisual()
        {
            SetButtonVisual(_rainbowButton, _rainbowIcon, RainbowEnabled, RainbowOnSprite, RainbowOffSprite);
        }

        private void UpdateDAButtonVisual()
        {
            SetButtonVisual(_daButton, _daIcon, DisappearingEnabled, DAOnSprite, DAOffSprite);
        }

        private void UpdateGhostButtonVisual()
        {
            SetButtonVisual(_ghostButton, _ghostIcon, GhostEnabled, GhostOnSprite, GhostOffSprite);
        }

        private void UpdateBombButtonVisual()
        {
            SetButtonVisual(_bombButton, _bombIcon, BombEnabled, BombOnSprite, BombOffSprite);
        }

        private void UpdateFasterButtonVisual()
        {
            SetButtonVisual(_fasterButton, _fasterIcon, FasterEnabled, FasterOnSprite, FasterOffSprite);
        }

        private void UpdateSuperFastButtonVisual()
        {
            SetButtonVisual(_superFastButton, _superFastIcon, SuperFastEnabled, SuperFastOnSprite, SuperFastOffSprite);
        }

        private void UpdateSlowerButtonVisual()
        {
            SetButtonVisual(_slowerButton, _slowerIcon, SlowerEnabled, SlowerOnSprite, SlowerOffSprite);
        }

        private void UpdateFlashbangButtonVisual()
        {
            SetButtonVisual(_flashbangButton, _flashbangIcon, FlashbangEnabled, FlashbangOnSprite, FlashbangOffSprite);
        }


        [UIValue("rainbowCpEnabled")]
        public bool RainbowCpEnabled
        {
            get => _rainbowCpEnabled;
            set
            {
                if (_rainbowCpEnabled == value) return;
                _rainbowCpEnabled = value;

                var cfg = Plugin.Settings;
                if (cfg != null) cfg.CpRainbowEnabled = value;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SyncRewardAsync(
                            title: "SaberSurgeon: Rainbow",
                            prompt: "Triggers Rainbow",
                            cost: _rainbowCpCost,
                            cooldown: _rainbowCpCooldownSeconds,
                            enabled: _rainbowCpEnabled,
                            getId: () => cfg?.CpRainbowRewardId ?? "",
                            setId: id => { if (cfg != null) cfg.CpRainbowRewardId = id; }
                        );
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warn("Rainbow CP SyncRewardAsync failed: " + ex);
                    }
                });

                UpdateTwitchCpButtonVisuals();
                NotifyPropertyChanged();
            }
        }


        [UIValue("rainbowCpBackgroundColor")]
        public Color RainbowCpBackgroundColor
        {
            get => _rainbowCpBackgroundColor;
            set
            {
                if (_rainbowCpBackgroundColor == value) return;
                _rainbowCpBackgroundColor = value;

                var cfg = Plugin.Settings;
                if (cfg != null) cfg.CpRainbowBackgroundColor = value;

                NotifyPropertyChanged();
            }
        }


        [UIValue("rainbowCpCostText")]
        public string RainbowCpCostText
        {
            get => _rainbowCpCostText;
            set
            {
                if (_rainbowCpCostText == value) return;

                // Keep the raw text so the UI shows what was typed.
                _rainbowCpCostText = value ?? string.Empty;

                // Parse numbers only; if parse fails, keep previous _rainbowCpCost.
                if (int.TryParse(_rainbowCpCostText, out var parsed) && parsed > 0)
                {
                    _rainbowCpCost = parsed;
                    var cfg = Plugin.Settings;
                    if (cfg != null) cfg.CpRainbowCost = _rainbowCpCost;
                }


                NotifyPropertyChanged();
            }
        }

        [UIValue("rainbowCpCooldownSeconds")]
        public int RainbowCpCooldownSeconds
        {
            get => _rainbowCpCooldownSeconds;
            set
            {
                _rainbowCpCooldownSeconds = Math.Max(0, value);

                var cfg = Plugin.Settings;
                if (cfg != null) cfg.CpRainbowCooldownSeconds = _rainbowCpCooldownSeconds;

                if (cfg != null && _rainbowCpEnabled)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SyncRewardAsync(
                                title: "SaberSurgeon: Rainbow",
                                prompt: "Triggers Rainbow",
                                cost: _rainbowCpCost,
                                cooldown: _rainbowCpCooldownSeconds,
                                enabled: _rainbowCpEnabled,
                                getId: () => cfg.CpRainbowRewardId,
                                setId: id => cfg.CpRainbowRewardId = id,
                                backgroundColorHex: ToHex(_rainbowCpBackgroundColor)
                            );
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Warn("Rainbow CP SyncRewardAsync (cooldown change) failed: " + ex);
                        }
                    });
                }

                NotifyPropertyChanged();
            }
        }
    }
}