using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.UI.Controllers;
using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using IPA.Utilities.Async;



namespace BeatSurgeon.UI.Settings
{
    // NotifiableBase implements INotifyPropertyChanged for "~Value" bindings like "~TwitchStatusText"
    // so the UI updates when the property changes.
    internal sealed class SurgeonGameplaySetupHost : NotifiableBase
    {

        // ===== Twitch Channel Point settings =====


        private bool _hasInitializedRewards = false;
        private bool _twitchStatusEventsHooked;

        private bool _rainbowCpEnabled;
        private Color _rainbowCpBackgroundColor = Color.white;
        private int _rainbowCpCost = 500;
        private string _rainbowCpCostText = "500";
        private int _rainbowCpCooldownSeconds = 0;


        private bool _daCpEnabled;
        private int _daCpCost = 500;
        private string _daCpCostText = "500";
        private int _daCpCooldownSeconds = 0;
        private Color _daCpBackgroundColor = Color.white;

        private bool _ghostCpEnabled;
        private int _ghostCpCost = 500;
        private string _ghostCpCostText = "500";
        private int _ghostCpCooldownSeconds = 0;
        private Color _ghostCpBackgroundColor = Color.white;

        private bool _bombCpEnabled;
        private int _bombCpCost = 500;
        private string _bombCpCostText = "500";
        private int _bombCpCooldownSeconds = 0;
        private Color _bombCpBackgroundColor = Color.white;

        private bool _fasterCpEnabled;
        private int _fasterCpCost = 500;
        private string _fasterCpCostText = "500";
        private int _fasterCpCooldownSeconds = 0;
        private Color _fasterCpBackgroundColor = Color.white;

        private bool _superFastCpEnabled;
        private int _superFastCpCost = 500;
        private string _superFastCpCostText = "500";
        private int _superFastCpCooldownSeconds = 0;
        private Color _superFastCpBackgroundColor = Color.white;

        private bool _slowerCpEnabled;
        private int _slowerCpCost = 500;
        private string _slowerCpCostText = "500";
        private int _slowerCpCooldownSeconds = 0;
        private Color _slowerCpBackgroundColor = Color.white;

        private bool _flashbangCpEnabled;
        private int _flashbangCpCost = 500;
        private string _flashbangCpCostText = "500";
        private int _flashbangCpCooldownSeconds = 0;
        private Color _flashbangCpBackgroundColor = Color.white;



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

        // --- Embedded icon sprites (same resource names used by BeatSurgeonViewController) ---
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

        private static readonly Sprite RainbowOffSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.Rainbow.png");
        private static readonly Sprite RainbowOnSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.RainbowGB.png");

        private static readonly Sprite DAOffSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.DA.png");
        private static readonly Sprite DAOnSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.DAGB.png");

        private static readonly Sprite GhostOffSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.GhostNotes.png");
        private static readonly Sprite GhostOnSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.GhostNotesGB.png");

        private static readonly Sprite BombOffSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.Bomb.png");
        private static readonly Sprite BombOnSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.BombGB.png");

        private static readonly Sprite FasterOffSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.FasterSong.png");
        private static readonly Sprite FasterOnSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.FasterSongGB.png");

        private static readonly Sprite SuperFastOffSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.SuperFastSong.png");
        private static readonly Sprite SuperFastOnSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.SuperFastSongGB.png");

        private static readonly Sprite SlowerOffSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.SlowerSong.png");
        private static readonly Sprite SlowerOnSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.SlowerSongGB.png");

        private static readonly Sprite FlashbangOffSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.Flashbang.png");
        private static readonly Sprite FlashbangOnSprite = LoadEmbeddedSprite("BeatSurgeon.Assets.FlashbangGB.png");

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


            LoadCpFromConfig();
            // Initialize visuals for the command buttons.

            UpdateAllCommandButtonVisuals();
            if (!_hasInitializedRewards)
            {
                _hasInitializedRewards = true;
                _ = SyncAllEnabledRewardsOnStartup();
            }

            NotifyPropertyChanged(nameof(RainbowCpEnabled));
            NotifyPropertyChanged(nameof(RainbowCpCostText));
            NotifyPropertyChanged(nameof(RainbowCpCooldownSeconds));
            NotifyPropertyChanged(nameof(RainbowCpBackgroundColor));

            NotifyPropertyChanged(nameof(GhostCpEnabled));
            NotifyPropertyChanged(nameof(GhostCpCostText));
            NotifyPropertyChanged(nameof(GhostCpCooldownSeconds));
            NotifyPropertyChanged(nameof(GhostCpBackgroundColor));

            NotifyPropertyChanged(nameof(BombCpEnabled));
            NotifyPropertyChanged(nameof(BombCpCostText));
            NotifyPropertyChanged(nameof(BombCpCooldownSeconds));
            NotifyPropertyChanged(nameof(BombCpBackgroundColor));

            NotifyPropertyChanged(nameof(DaCpEnabled));
            NotifyPropertyChanged(nameof(DaCpCostText));
            NotifyPropertyChanged(nameof(DaCpCooldownSeconds));
            NotifyPropertyChanged(nameof(DaCpBackgroundColor));

            NotifyPropertyChanged(nameof(FasterCpEnabled));
            NotifyPropertyChanged(nameof(FasterCpCostText));
            NotifyPropertyChanged(nameof(FasterCpCooldownSeconds));
            NotifyPropertyChanged(nameof(FasterCpBackgroundColor));

            NotifyPropertyChanged(nameof(SuperFastCpEnabled));
            NotifyPropertyChanged(nameof(SuperFastCpCostText));
            NotifyPropertyChanged(nameof(SuperFastCpCooldownSeconds));
            NotifyPropertyChanged(nameof(SuperFastCpBackgroundColor));

            NotifyPropertyChanged(nameof(SlowerCpEnabled));
            NotifyPropertyChanged(nameof(SlowerCpCostText));
            NotifyPropertyChanged(nameof(SlowerCpCooldownSeconds));
            NotifyPropertyChanged(nameof(SlowerCpBackgroundColor));

            NotifyPropertyChanged(nameof(FlashbangCpEnabled));
            NotifyPropertyChanged(nameof(FlashbangCpCostText));
            NotifyPropertyChanged(nameof(FlashbangCpCooldownSeconds));
            NotifyPropertyChanged(nameof(FlashbangCpBackgroundColor));

            UpdateTwitchCpButtonVisuals();
            // Initialize status text and supporter UI visibility.
            InitTwitchIconsIndependent();
            HookTwitchStatusEvents();
            RefreshTwitchStatusText();
            UpdateSupporterUi();
        }


        /// <summary>
        /// On first activation, sync all enabled rewards with Twitch.
        /// This ensures rewards that were enabled before game restart are actually live.
        /// </summary>
        private async Task SyncAllEnabledRewardsOnStartup()
        {
            var cfg = Plugin.Settings;
            if (cfg == null) return;

            if (!TwitchAuthManager.Instance.IsAuthenticated)
            {
                Plugin.Log.Info("SurgeonGameplaySetupHost: Not authenticated with backend - skipping CP reward sync on startup");
                return;
            }

            Plugin.Log.Info("SurgeonGameplaySetupHost: Syncing all enabled channel point rewards...");

            try
            {
                // Use a timeout for the entire operation
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

                // Startup reconciliation: if Twitch cost differs, trust Twitch and update local config first.
                try
                {
                    var twitchRewards = await TwitchChannelPointsManager.Instance.GetManageableRewardsAsync(cts.Token);
                    ApplyStartupCostFromTwitch(
                        twitchRewards, "rainbow", cfg.CpRainbowEnabled,
                        () => cfg.CpRainbowRewardId, id => cfg.CpRainbowRewardId = id,
                        () => cfg.CpRainbowCost, cost => cfg.CpRainbowCost = cost);
                    ApplyStartupCostFromTwitch(
                        twitchRewards, "disappear", cfg.CpDisappearEnabled,
                        () => cfg.CpDisappearRewardId, id => cfg.CpDisappearRewardId = id,
                        () => cfg.CpDisappearCost, cost => cfg.CpDisappearCost = cost);
                    ApplyStartupCostFromTwitch(
                        twitchRewards, "ghost", cfg.CpGhostEnabled,
                        () => cfg.CpGhostRewardId, id => cfg.CpGhostRewardId = id,
                        () => cfg.CpGhostCost, cost => cfg.CpGhostCost = cost);
                    ApplyStartupCostFromTwitch(
                        twitchRewards, "bomb", cfg.CpBombEnabled,
                        () => cfg.CpBombRewardId, id => cfg.CpBombRewardId = id,
                        () => cfg.CpBombCost, cost => cfg.CpBombCost = cost);
                    ApplyStartupCostFromTwitch(
                        twitchRewards, "faster", cfg.CpFasterEnabled,
                        () => cfg.CpFasterRewardId, id => cfg.CpFasterRewardId = id,
                        () => cfg.CpFasterCost, cost => cfg.CpFasterCost = cost);
                    ApplyStartupCostFromTwitch(
                        twitchRewards, "superfast", cfg.CpSuperFastEnabled,
                        () => cfg.CpSuperFastRewardId, id => cfg.CpSuperFastRewardId = id,
                        () => cfg.CpSuperFastCost, cost => cfg.CpSuperFastCost = cost);
                    ApplyStartupCostFromTwitch(
                        twitchRewards, "slower", cfg.CpSlowerEnabled,
                        () => cfg.CpSlowerRewardId, id => cfg.CpSlowerRewardId = id,
                        () => cfg.CpSlowerCost, cost => cfg.CpSlowerCost = cost);
                    ApplyStartupCostFromTwitch(
                        twitchRewards, "flashbang", cfg.CpFlashbangEnabled,
                        () => cfg.CpFlashbangRewardId, id => cfg.CpFlashbangRewardId = id,
                        () => cfg.CpFlashbangCost, cost => cfg.CpFlashbangCost = cost);

                    NotifyPropertyChanged(string.Empty);
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warn("SurgeonGameplaySetupHost: Startup Twitch cost reconciliation skipped: " + ex.Message);
                }

                // Sync each enabled reward
                if (cfg.CpRainbowEnabled)
                {
                    LogUtils.Debug(() => "Syncing Rainbow reward...");
                    var t = GetRewardText("rainbow");
                    await SyncRewardAsync("rainbow", t.Title, t.Prompt, cfg.CpRainbowCost,
                        cfg.CpRainbowCooldownSeconds, true,
                        () => cfg.CpRainbowRewardId, id => cfg.CpRainbowRewardId = id,
                        t.BgHex, cts.Token);
                }

                if (cfg.CpDisappearEnabled)
                {
                    LogUtils.Debug(() => "Syncing Disappear reward...");
                    var t = GetRewardText("disappear");
                    await SyncRewardAsync("disappear", t.Title, t.Prompt, cfg.CpDisappearCost,
                        cfg.CpDisappearCooldownSeconds, true,
                        () => cfg.CpDisappearRewardId, id => cfg.CpDisappearRewardId = id,
                        t.BgHex, cts.Token);
                }

                if (cfg.CpGhostEnabled)
                {
                    LogUtils.Debug(() => "Syncing Ghost reward...");
                    var t = GetRewardText("ghost");
                    await SyncRewardAsync("ghost", t.Title, t.Prompt, cfg.CpGhostCost,
                        cfg.CpGhostCooldownSeconds, true,
                        () => cfg.CpGhostRewardId, id => cfg.CpGhostRewardId = id,
                        t.BgHex, cts.Token);
                }

                if (cfg.CpBombEnabled)
                {
                    LogUtils.Debug(() => "Syncing Bomb reward...");
                    var t = GetRewardText("bomb");
                    await SyncRewardAsync("bomb", t.Title, t.Prompt, cfg.CpBombCost,
                        cfg.CpBombCooldownSeconds, true,
                        () => cfg.CpBombRewardId, id => cfg.CpBombRewardId = id,
                        t.BgHex, cts.Token);
                }

                if (cfg.CpFasterEnabled)
                {
                    LogUtils.Debug(() => "Syncing Faster reward...");
                    var t = GetRewardText("faster");
                    await SyncRewardAsync("faster", t.Title, t.Prompt, cfg.CpFasterCost,
                        cfg.CpFasterCooldownSeconds, true,
                        () => cfg.CpFasterRewardId, id => cfg.CpFasterRewardId = id,
                        t.BgHex, cts.Token);
                }

                if (cfg.CpSuperFastEnabled)
                {
                    LogUtils.Debug(() => "Syncing SuperFast reward...");
                    var t = GetRewardText("superfast");
                    await SyncRewardAsync("superfast", t.Title, t.Prompt, cfg.CpSuperFastCost,
                        cfg.CpSuperFastCooldownSeconds, true,
                        () => cfg.CpSuperFastRewardId, id => cfg.CpSuperFastRewardId = id,
                        t.BgHex, cts.Token);
                }

                if (cfg.CpSlowerEnabled)
                {
                    LogUtils.Debug(() => "Syncing Slower reward...");
                    var t = GetRewardText("slower");
                    await SyncRewardAsync("slower", t.Title, t.Prompt, cfg.CpSlowerCost,
                        cfg.CpSlowerCooldownSeconds, true,
                        () => cfg.CpSlowerRewardId, id => cfg.CpSlowerRewardId = id,
                        t.BgHex, cts.Token);
                }

                if (cfg.CpFlashbangEnabled)
                {
                    LogUtils.Debug(() => "Syncing Flashbang reward...");
                    var t = GetRewardText("flashbang");
                    await SyncRewardAsync("flashbang", t.Title, t.Prompt, cfg.CpFlashbangCost,
                        cfg.CpFlashbangCooldownSeconds, true,
                        () => cfg.CpFlashbangRewardId, id => cfg.CpFlashbangRewardId = id,
                        t.BgHex, cts.Token);
                }

                Plugin.Log.Info("SurgeonGameplaySetupHost: All enabled rewards synced successfully!");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"SurgeonGameplaySetupHost: Failed to sync rewards on startup: {ex.Message}");
            }
        }


        private void RefreshCpModalValues(string key)
        {
            LoadCpFromConfig();

            // Force BSML-bound controls to pull the latest values
            NotifyCpChanged(key);

            switch (key)
            {
                case "rainbow":
                    NotifyPropertyChanged(nameof(RainbowCpCostText));
                    NotifyPropertyChanged(nameof(RainbowCpCooldownSeconds));
                    NotifyPropertyChanged(nameof(RainbowCpBackgroundColor));
                    break;

                case "disappear":
                    NotifyPropertyChanged(nameof(DaCpCostText));
                    NotifyPropertyChanged(nameof(DaCpCooldownSeconds));
                    NotifyPropertyChanged(nameof(DaCpBackgroundColor));
                    break;

                case "ghost":
                    NotifyPropertyChanged(nameof(GhostCpCostText));
                    NotifyPropertyChanged(nameof(GhostCpCooldownSeconds));
                    NotifyPropertyChanged(nameof(GhostCpBackgroundColor));
                    break;

                case "bomb":
                    NotifyPropertyChanged(nameof(BombCpCostText));
                    NotifyPropertyChanged(nameof(BombCpCooldownSeconds));
                    NotifyPropertyChanged(nameof(BombCpBackgroundColor));
                    break;

                case "faster":
                    NotifyPropertyChanged(nameof(FasterCpCostText));
                    NotifyPropertyChanged(nameof(FasterCpCooldownSeconds));
                    NotifyPropertyChanged(nameof(FasterCpBackgroundColor));
                    break;

                case "superfast":
                    NotifyPropertyChanged(nameof(SuperFastCpCostText));
                    NotifyPropertyChanged(nameof(SuperFastCpCooldownSeconds));
                    NotifyPropertyChanged(nameof(SuperFastCpBackgroundColor));
                    break;

                case "slower":
                    NotifyPropertyChanged(nameof(SlowerCpCostText));
                    NotifyPropertyChanged(nameof(SlowerCpCooldownSeconds));
                    NotifyPropertyChanged(nameof(SlowerCpBackgroundColor));
                    break;

                case "flashbang":
                    NotifyPropertyChanged(nameof(FlashbangCpCostText));
                    NotifyPropertyChanged(nameof(FlashbangCpCooldownSeconds));
                    NotifyPropertyChanged(nameof(FlashbangCpBackgroundColor));
                    break;
            }
        }

        private static (string Title, string Prompt, string BgHex) GetRewardText(string key)
        {
            switch (key)
            {
                case "rainbow":
                    return (
                        "Beat Saber : Rainbow Notes",
                        $"(Only works if playing a map) Turns notes RGB for {CommandRuntimeSettings.RainbowEffectSeconds:F0} seconds.",
                        ToHex(Instance._rainbowCpBackgroundColor)
                    );

                case "disappear":
                    return (
                        "Beat Saber : Disappearing Arrows",
                        $"(Only works if playing a map) Enables Disappearing Arrows for {CommandRuntimeSettings.DisappearEffectSeconds:F0} seconds.",
                        ToHex(Instance._daCpBackgroundColor)
                    );

                case "ghost":
                    return (
                        "Beat Saber : Ghost Notes",
                        $"(Only works if playing a map) Enables Ghost Notes for {CommandRuntimeSettings.GhostEffectSeconds:F0} seconds.",
                        ToHex(Instance._ghostCpBackgroundColor)
                    );

                case "bomb":
                    return (
                        "Beat Saber : Bomb Note",
                        $" (Only works if playing a map) Turns a random Note into a Bomb until cut (Does not effect score)",
                        ToHex(Instance._bombCpBackgroundColor)
                    );

                case "faster":
                    return (
                        "Beat Saber : Faster Song",
                        $"(Only works if playing a map) Enables Faster Song for {CommandRuntimeSettings.SpeedEffectSeconds:F0} seconds. Will disable score submission",
                        ToHex(Instance._fasterCpBackgroundColor)
                    );

                case "superfast":
                    return (
                        "Beat Saber : SuperFast Song",
                        $" (Only works if playing a map) Enables SuperFast Song for {CommandRuntimeSettings.SpeedEffectSeconds:F0} seconds. Will disable score submission",
                        ToHex(Instance._superFastCpBackgroundColor)
                    );

                case "slower":
                    return (
                        "Beat Saber : Slower Song",
                        $"(Only works if playing a map) Enables Slower song for {CommandRuntimeSettings.SpeedEffectSeconds:F0} seconds. Will disable score submission",
                        ToHex(Instance._slowerCpBackgroundColor)
                    );

                case "flashbang":
                    return (
                        "Beat Saber : Flashbang",
                        $"(Only works if playing a map) Deploy an Environmental Flashbang that fades over {CommandRuntimeSettings.FlashbangFadeSeconds:F0} seconds.",
                        ToHex(Instance._flashbangCpBackgroundColor)
                    );

            }

            return ($"Beat Saber : {key}", $"Triggers {key}.", null);
        }

        private static JObject FindManageableReward(JArray rewards, string storedId, string expectedTitle)
        {
            if (rewards == null) return null;

            if (!string.IsNullOrWhiteSpace(storedId))
            {
                foreach (var r in rewards)
                {
                    if (string.Equals(r?["id"]?.ToString(), storedId, StringComparison.Ordinal))
                        return r as JObject;
                }
            }

            foreach (var r in rewards)
            {
                if (string.Equals(r?["title"]?.ToString(), expectedTitle, StringComparison.Ordinal))
                    return r as JObject;
            }

            return null;
        }

        private void ApplyStartupCostFromTwitch(
            JArray twitchRewards,
            string key,
            bool enabled,
            Func<string> getId,
            Action<string> setId,
            Func<int> getLocalCost,
            Action<int> setLocalCost)
        {
            if (!enabled || twitchRewards == null) return;

            var text = GetRewardText(key);
            var reward = FindManageableReward(twitchRewards, getId?.Invoke(), text.Title);
            if (reward == null) return;

            string twitchRewardId = reward["id"]?.ToString();
            if (!string.IsNullOrWhiteSpace(twitchRewardId))
                setId?.Invoke(twitchRewardId);

            int? twitchCost = reward["cost"]?.Value<int?>();
            if (!twitchCost.HasValue || twitchCost.Value <= 0) return;

            int localCost = getLocalCost != null ? getLocalCost() : 0;
            if (twitchCost.Value == localCost) return;

            Plugin.Log.Info(
                $"SurgeonGameplaySetupHost: Startup cost sync for '{key}' local={localCost} twitch={twitchCost.Value}.");
            setLocalCost?.Invoke(twitchCost.Value);
            UpdateLocalCpCostCache(key, twitchCost.Value);
        }

        private void UpdateLocalCpCostCache(string key, int cost)
        {
            int safeCost = Math.Max(1, cost);
            string text = safeCost.ToString();

            switch (key)
            {
                case "rainbow":
                    _rainbowCpCost = safeCost;
                    _rainbowCpCostText = text;
                    break;
                case "disappear":
                    _daCpCost = safeCost;
                    _daCpCostText = text;
                    break;
                case "ghost":
                    _ghostCpCost = safeCost;
                    _ghostCpCostText = text;
                    break;
                case "bomb":
                    _bombCpCost = safeCost;
                    _bombCpCostText = text;
                    break;
                case "faster":
                    _fasterCpCost = safeCost;
                    _fasterCpCostText = text;
                    break;
                case "superfast":
                    _superFastCpCost = safeCost;
                    _superFastCpCostText = text;
                    break;
                case "slower":
                    _slowerCpCost = safeCost;
                    _slowerCpCostText = text;
                    break;
                case "flashbang":
                    _flashbangCpCost = safeCost;
                    _flashbangCpCostText = text;
                    break;
            }
        }

        private static int ParseRewardCooldownSeconds(JObject reward)
        {
            var cooldownObj = reward?["global_cooldown_setting"] as JObject;
            bool cooldownEnabled = cooldownObj?["is_enabled"]?.Value<bool?>() ?? false;
            if (!cooldownEnabled)
            {
                return 0;
            }

            int cooldown = cooldownObj?["global_cooldown_seconds"]?.Value<int?>() ?? 0;
            return Math.Max(0, cooldown);
        }

        private static Color ParseRewardBackgroundColor(JObject reward, Color fallback)
        {
            string hex = reward?["background_color"]?.ToString();
            if (string.IsNullOrWhiteSpace(hex))
            {
                return fallback;
            }

            Color parsed;
            if (!ColorUtility.TryParseHtmlString(hex, out parsed))
            {
                return fallback;
            }

            return parsed;
        }

        private void ApplyRewardFromTwitchToLocalState(string key, JObject reward)
        {
            if (reward == null) return;

            string id = reward["id"]?.ToString() ?? string.Empty;
            // Note: is_enabled is intentionally NOT read here. The local config is the canonical
            // source of truth for whether the user wants a reward enabled. Reading it from Twitch
            // would overwrite the user's intent with a potentially stale Twitch state (e.g., rewards
            // are disabled on quit by DisableAllOwnedRewardsAsync and re-enabled at next startup).
            int cost = Math.Max(1, reward["cost"]?.Value<int?>() ?? 500);
            int cooldown = ParseRewardCooldownSeconds(reward);

            var cfg = Plugin.Settings;
            if (cfg == null) return;

            switch (key)
            {
                case "rainbow":
                    cfg.CpRainbowRewardId = id;
                    cfg.CpRainbowCost = cost;
                    cfg.CpRainbowCooldownSeconds = cooldown;
                    cfg.CpRainbowBackgroundColor = ParseRewardBackgroundColor(reward, cfg.CpRainbowBackgroundColor);

                    _rainbowCpCost = cfg.CpRainbowCost;
                    _rainbowCpCostText = cfg.CpRainbowCost.ToString();
                    _rainbowCpCooldownSeconds = cfg.CpRainbowCooldownSeconds;
                    _rainbowCpBackgroundColor = cfg.CpRainbowBackgroundColor;
                    break;

                case "disappear":
                    cfg.CpDisappearRewardId = id;
                    cfg.CpDisappearCost = cost;
                    cfg.CpDisappearCooldownSeconds = cooldown;
                    cfg.CpDisappearBackgroundColor = ParseRewardBackgroundColor(reward, cfg.CpDisappearBackgroundColor);

                    _daCpCost = cfg.CpDisappearCost;
                    _daCpCostText = cfg.CpDisappearCost.ToString();
                    _daCpCooldownSeconds = cfg.CpDisappearCooldownSeconds;
                    _daCpBackgroundColor = cfg.CpDisappearBackgroundColor;
                    break;

                case "ghost":
                    cfg.CpGhostRewardId = id;
                    cfg.CpGhostCost = cost;
                    cfg.CpGhostCooldownSeconds = cooldown;
                    cfg.CpGhostBackgroundColor = ParseRewardBackgroundColor(reward, cfg.CpGhostBackgroundColor);

                    _ghostCpCost = cfg.CpGhostCost;
                    _ghostCpCostText = cfg.CpGhostCost.ToString();
                    _ghostCpCooldownSeconds = cfg.CpGhostCooldownSeconds;
                    _ghostCpBackgroundColor = cfg.CpGhostBackgroundColor;
                    break;

                case "bomb":
                    cfg.CpBombRewardId = id;
                    cfg.CpBombCost = cost;
                    cfg.CpBombCooldownSeconds = cooldown;
                    cfg.CpBombBackgroundColor = ParseRewardBackgroundColor(reward, cfg.CpBombBackgroundColor);

                    _bombCpCost = cfg.CpBombCost;
                    _bombCpCostText = cfg.CpBombCost.ToString();
                    _bombCpCooldownSeconds = cfg.CpBombCooldownSeconds;
                    _bombCpBackgroundColor = cfg.CpBombBackgroundColor;
                    break;

                case "faster":
                    cfg.CpFasterRewardId = id;
                    cfg.CpFasterCost = cost;
                    cfg.CpFasterCooldownSeconds = cooldown;
                    cfg.CpFasterBackgroundColor = ParseRewardBackgroundColor(reward, cfg.CpFasterBackgroundColor);

                    _fasterCpCost = cfg.CpFasterCost;
                    _fasterCpCostText = cfg.CpFasterCost.ToString();
                    _fasterCpCooldownSeconds = cfg.CpFasterCooldownSeconds;
                    _fasterCpBackgroundColor = cfg.CpFasterBackgroundColor;
                    break;

                case "superfast":
                    cfg.CpSuperFastRewardId = id;
                    cfg.CpSuperFastCost = cost;
                    cfg.CpSuperFastCooldownSeconds = cooldown;
                    cfg.CpSuperFastBackgroundColor = ParseRewardBackgroundColor(reward, cfg.CpSuperFastBackgroundColor);

                    _superFastCpCost = cfg.CpSuperFastCost;
                    _superFastCpCostText = cfg.CpSuperFastCost.ToString();
                    _superFastCpCooldownSeconds = cfg.CpSuperFastCooldownSeconds;
                    _superFastCpBackgroundColor = cfg.CpSuperFastBackgroundColor;
                    break;

                case "slower":
                    cfg.CpSlowerRewardId = id;
                    cfg.CpSlowerCost = cost;
                    cfg.CpSlowerCooldownSeconds = cooldown;
                    cfg.CpSlowerBackgroundColor = ParseRewardBackgroundColor(reward, cfg.CpSlowerBackgroundColor);

                    _slowerCpCost = cfg.CpSlowerCost;
                    _slowerCpCostText = cfg.CpSlowerCost.ToString();
                    _slowerCpCooldownSeconds = cfg.CpSlowerCooldownSeconds;
                    _slowerCpBackgroundColor = cfg.CpSlowerBackgroundColor;
                    break;

                case "flashbang":
                    cfg.CpFlashbangRewardId = id;
                    cfg.CpFlashbangCost = cost;
                    cfg.CpFlashbangCooldownSeconds = cooldown;
                    cfg.CpFlashbangBackgroundColor = ParseRewardBackgroundColor(reward, cfg.CpFlashbangBackgroundColor);

                    _flashbangCpCost = cfg.CpFlashbangCost;
                    _flashbangCpCostText = cfg.CpFlashbangCost.ToString();
                    _flashbangCpCooldownSeconds = cfg.CpFlashbangCooldownSeconds;
                    _flashbangCpBackgroundColor = cfg.CpFlashbangBackgroundColor;
                    break;
            }
        }

        private string GetStoredRewardIdForKey(string key)
        {
            var cfg = Plugin.Settings;
            if (cfg == null) return string.Empty;

            switch (key)
            {
                case "rainbow": return cfg.CpRainbowRewardId;
                case "disappear": return cfg.CpDisappearRewardId;
                case "ghost": return cfg.CpGhostRewardId;
                case "bomb": return cfg.CpBombRewardId;
                case "faster": return cfg.CpFasterRewardId;
                case "superfast": return cfg.CpSuperFastRewardId;
                case "slower": return cfg.CpSlowerRewardId;
                case "flashbang": return cfg.CpFlashbangRewardId;
                default: return string.Empty;
            }
        }

        private async Task RefreshCpFromTwitchAsync(string key)
        {
            // Throttle repeated refreshes for the same key to avoid excessive GETs
            lock (_refreshTimesLock)
            {
                if (_lastRefreshTimes.TryGetValue(key, out var last) && (DateTime.UtcNow - last).TotalSeconds < 5)
                {
                    LogUtils.Debug(() => $"RefreshCpFromTwitchAsync: Skipping refresh for {key} (last refresh {DateTime.UtcNow - last} ago)");
                    return;
                }
                _lastRefreshTimes[key] = DateTime.UtcNow;
            }

             try
             {
                 using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                 {
                     var rewards = await TwitchChannelPointsManager.Instance.GetManageableRewardsAsync(cts.Token);
                     var text = GetRewardText(key);
                     var reward = FindManageableReward(rewards, GetStoredRewardIdForKey(key), text.Title);

                     if (reward != null)
                     {
                         ApplyRewardFromTwitchToLocalState(key, reward);
                         RefreshCpModalValues(key);
                         NotifyCpChanged(key);
                         UpdateTwitchCpButtonVisuals();
                     }
                 }
             }
             catch (Exception ex)
             {
                 Plugin.Log.Warn("RefreshCpFromTwitchAsync(" + key + ") failed: " + ex.Message);
             }
         }


        private void NotifyCpChanged(string key)
        {
            switch (key)
            {
                case "rainbow": NotifyPropertyChanged(nameof(RainbowCpEnabled)); break;
                case "disappear": NotifyPropertyChanged(nameof(DaCpEnabled)); break;
                case "ghost": NotifyPropertyChanged(nameof(GhostCpEnabled)); break;
                case "bomb": NotifyPropertyChanged(nameof(BombCpEnabled)); break;
                case "faster": NotifyPropertyChanged(nameof(FasterCpEnabled)); break;
                case "superfast": NotifyPropertyChanged(nameof(SuperFastCpEnabled)); break;
                case "slower": NotifyPropertyChanged(nameof(SlowerCpEnabled)); break;
                case "flashbang": NotifyPropertyChanged(nameof(FlashbangCpEnabled)); break;
            }
        }

        private async Task ToggleCpAsync(string key)
        {
            try
            {
                if (!TwitchAuthManager.Instance.IsAuthenticated)
                {
                    Plugin.Log.Warn("[SurgeonGameplaySetupHost] ToggleCpAsync: not authenticated with BeatSurgeon backend - CP buttons require authentication");
                    return;
                }

                var cfg = Plugin.Settings;
                if (cfg == null) return;

                bool enabled;
                int cost;
                int cooldown;
                Func<string> getId;
                Action<string> setId;

                switch (key)
                {
                    case "rainbow":
                        _rainbowCpEnabled = !_rainbowCpEnabled;
                        cfg.CpRainbowEnabled = _rainbowCpEnabled;

                        enabled = _rainbowCpEnabled;
                        cost = _rainbowCpCost;
                        cooldown = _rainbowCpCooldownSeconds;
                        getId = () => cfg.CpRainbowRewardId;
                        setId = id => cfg.CpRainbowRewardId = id;
                        break;

                    case "disappear":
                        _daCpEnabled = !_daCpEnabled;
                        cfg.CpDisappearEnabled = _daCpEnabled;

                        enabled = _daCpEnabled;
                        cost = _daCpCost;
                        cooldown = _daCpCooldownSeconds;
                        getId = () => cfg.CpDisappearRewardId;
                        setId = id => cfg.CpDisappearRewardId = id;
                        break;

                    case "ghost":
                        _ghostCpEnabled = !_ghostCpEnabled;
                        cfg.CpGhostEnabled = _ghostCpEnabled;

                        enabled = _ghostCpEnabled;
                        cost = _ghostCpCost;
                        cooldown = _ghostCpCooldownSeconds;
                        getId = () => cfg.CpGhostRewardId;
                        setId = id => cfg.CpGhostRewardId = id;
                        break;

                    case "bomb":
                        _bombCpEnabled = !_bombCpEnabled;
                        cfg.CpBombEnabled = _bombCpEnabled;

                        enabled = _bombCpEnabled;
                        cost = _bombCpCost;
                        cooldown = _bombCpCooldownSeconds;
                        getId = () => cfg.CpBombRewardId;
                        setId = id => cfg.CpBombRewardId = id;
                        break;

                    case "faster":
                        _fasterCpEnabled = !_fasterCpEnabled;
                        cfg.CpFasterEnabled = _fasterCpEnabled;

                        enabled = _fasterCpEnabled;
                        cost = _fasterCpCost;
                        cooldown = _fasterCpCooldownSeconds;
                        getId = () => cfg.CpFasterRewardId;
                        setId = id => cfg.CpFasterRewardId = id;
                        break;

                    case "superfast":
                        _superFastCpEnabled = !_superFastCpEnabled;
                        cfg.CpSuperFastEnabled = _superFastCpEnabled;

                        enabled = _superFastCpEnabled;
                        cost = _superFastCpCost;
                        cooldown = _superFastCpCooldownSeconds;
                        getId = () => cfg.CpSuperFastRewardId;
                        setId = id => cfg.CpSuperFastRewardId = id;
                        break;

                    case "slower":
                        _slowerCpEnabled = !_slowerCpEnabled;
                        cfg.CpSlowerEnabled = _slowerCpEnabled;

                        enabled = _slowerCpEnabled;
                        cost = _slowerCpCost;
                        cooldown = _slowerCpCooldownSeconds;
                        getId = () => cfg.CpSlowerRewardId;
                        setId = id => cfg.CpSlowerRewardId = id;
                        break;

                    case "flashbang":
                        _flashbangCpEnabled = !_flashbangCpEnabled;
                        cfg.CpFlashbangEnabled = _flashbangCpEnabled;

                        enabled = _flashbangCpEnabled;
                        cost = _flashbangCpCost;
                        cooldown = _flashbangCpCooldownSeconds;
                        getId = () => cfg.CpFlashbangRewardId;
                        setId = id => cfg.CpFlashbangRewardId = id;
                        break;

                    default:
                        return;
                }

                var t = GetRewardText(key);

                await SyncRewardAsync(
                    key: key,
                    title: t.Title,
                    prompt: t.Prompt,
                    cost: cost,
                    cooldown: cooldown,
                    enabled: enabled,
                    getId: getId,
                    setId: setId,
                    backgroundColorHex: t.BgHex
                );

                NotifyCpChanged(key);
                UpdateTwitchCpButtonVisuals();
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("ToggleCpAsync failed: " + ex.Message);
            }
        }


        private async Task SyncRewardAsync(
        string key,
        string title,
        string prompt,
        int cost,
        int cooldown,
        bool enabled,
        Func<string> getId,
        Action<string> setId,
        string backgroundColorHex = null,
        CancellationToken externalCt = default)
        {
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt, timeoutCts.Token))
            {
                int effectiveCooldown = CommandRuntimeSettings.GetCooldownSecondsForRewardKey(key);
                var id = await TwitchChannelPointsManager.Instance.EnsureRewardAsync(
                    new TwitchChannelPointsManager.RewardSpec
                    {
                        Key = key,
                        Title = title,
                        Prompt = prompt,
                        Cost = Math.Max(1, cost),
                        CooldownSeconds = Math.Max(0, effectiveCooldown),
                        BackgroundColorHex = backgroundColorHex,
                    },
                    storedRewardId: getId?.Invoke() ?? "",
                    saveRewardId: setId,
                    enabled: enabled,
                    ct: linkedCts.Token);

                setId?.Invoke(id);

                // EventSub subscription is managed inside TwitchChannelPointsManager.EnsureRewardAsync;
                // calling ResubscribeRewardsAsync here would always hit "AlreadySubscribed" - no-op removed.
            }
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


            _rainbowCpBackgroundColor = cfg.CpRainbowBackgroundColor;
            _daCpBackgroundColor = cfg.CpDisappearBackgroundColor;
            _ghostCpBackgroundColor = cfg.CpGhostBackgroundColor;
            _bombCpBackgroundColor = cfg.CpBombBackgroundColor;
            _fasterCpBackgroundColor = cfg.CpFasterBackgroundColor;
            _superFastCpBackgroundColor = cfg.CpSuperFastBackgroundColor;
            _slowerCpBackgroundColor = cfg.CpSlowerBackgroundColor;
            _flashbangCpBackgroundColor = cfg.CpFlashbangBackgroundColor;

            // Force BSML to refresh bindings:
            NotifyPropertyChanged(string.Empty);

            // Optional, but keeps the icon buttons consistent too:
            UpdateTwitchCpButtonVisuals();
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
            get => CommandRuntimeSettings.RainbowEnabled;
            set
            {
                CommandRuntimeSettings.RainbowEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.RainbowEnabled = value;
                UpdateRainbowButtonVisual();
                BeatSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool DisappearingEnabled
        {
            get => CommandRuntimeSettings.DisappearEnabled;
            set
            {
                CommandRuntimeSettings.DisappearEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.DisappearEnabled = value;
                UpdateDAButtonVisual();
                BeatSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool GhostEnabled
        {
            get => CommandRuntimeSettings.GhostEnabled;
            set
            {
                CommandRuntimeSettings.GhostEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.GhostEnabled = value;
                UpdateGhostButtonVisual();
                BeatSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool BombEnabled
        {
            get => CommandRuntimeSettings.BombEnabled;
            set
            {
                CommandRuntimeSettings.BombEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.BombEnabled = value;
                UpdateBombButtonVisual();
                BeatSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool FasterEnabled
        {
            get => CommandRuntimeSettings.FasterEnabled;
            set
            {
                CommandRuntimeSettings.FasterEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.FasterEnabled = value;
                UpdateFasterButtonVisual();
                BeatSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool SuperFastEnabled
        {
            get => CommandRuntimeSettings.SuperFastEnabled;
            set
            {
                CommandRuntimeSettings.SuperFastEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.SuperFastEnabled = value;
                UpdateSuperFastButtonVisual();
                BeatSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool SlowerEnabled
        {
            get => CommandRuntimeSettings.SlowerEnabled;
            set
            {
                CommandRuntimeSettings.SlowerEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.SlowerEnabled = value;
                UpdateSlowerButtonVisual();
                BeatSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }

        private bool FlashbangEnabled
        {
            get => CommandRuntimeSettings.FlashbangEnabled;
            set
            {
                CommandRuntimeSettings.FlashbangEnabled = value;
                if (Plugin.Settings != null) Plugin.Settings.FlashbangEnabled = value;
                UpdateFlashbangButtonVisual();
                BeatSurgeonViewController.RefreshCommandUiFromExternal();

            }
        }



        // --- Surgeon Commands tab button handlers
        [UIAction("OnRainbowButtonClicked")]
        private void OnRainbowButtonClicked()
        {
            RainbowEnabled = !RainbowEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Rainbow command enabled = {RainbowEnabled}");
        }

        [UIAction("OnDAButtonClicked")]
        private void OnDAButtonClicked()
        {
            DisappearingEnabled = !DisappearingEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Disappearing Arrows command enabled = {DisappearingEnabled}");
        }

        [UIAction("OnGhostButtonClicked")]
        private void OnGhostButtonClicked()
        {
            GhostEnabled = !GhostEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Ghost command enabled = {GhostEnabled}");
        }

        [UIAction("OnBombButtonClicked")]
        private void OnBombButtonClicked()
        {
            BombEnabled = !BombEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Bomb command enabled = {BombEnabled}");
        }

        [UIAction("OnFasterButtonClicked")]
        private void OnFasterButtonClicked()
        {
            FasterEnabled = !FasterEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Faster command enabled = {FasterEnabled}");
        }

        [UIAction("OnSFastButtonClicked")]
        private void OnSFastButtonClicked()
        {
            SuperFastEnabled = !SuperFastEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: SuperFast command enabled = {SuperFastEnabled}");
        }

        [UIAction("OnSlowerButtonClicked")]
        private void OnSlowerButtonClicked()
        {
            SlowerEnabled = !SlowerEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Slower command enabled = {SlowerEnabled}");
        }

        [UIAction("OnFlashbangButtonClicked")]
        private void OnFlashbangButtonClicked()
        {
            FlashbangEnabled = !FlashbangEnabled;
            LogUtils.Debug(() => $"BeatSurgeon: Flashbang command enabled = {FlashbangEnabled}");
        }


        public static void RefreshGameplaySetupIcons()
        {
            // Safe even if the GameplaySetup UI is not currently open,
            // because all UIComponent references will be null.
            Instance?.UpdateAllCommandButtonVisuals();
        }

        private static readonly TimeSpan RewardSyncDebounce = TimeSpan.FromMilliseconds(500);

        private readonly object _rewardDebounceLock = new object();
        private readonly System.Collections.Generic.Dictionary<string, CancellationTokenSource> _rewardDebounce =
            new System.Collections.Generic.Dictionary<string, CancellationTokenSource>();

        // Optional but strongly recommended: serialize Helix reward writes (see next section)
        private readonly SemaphoreSlim _rewardSyncGate = new SemaphoreSlim(1, 1);

        // When true we are refreshing modal values from Twitch/config and should not
        // trigger outgoing syncs back to Twitch. This prevents opening a modal from
        // immediately disabling/enabling rewards or firing duplicate requests.
        private volatile bool _isModalRefreshing;

        // Simple per-key debounce for RefreshCpFromTwitchAsync to avoid hammering Twitch
        private readonly object _refreshTimesLock = new object();
        private readonly System.Collections.Generic.Dictionary<string, DateTime> _lastRefreshTimes =
            new System.Collections.Generic.Dictionary<string, DateTime>(StringComparer.Ordinal);

        private void CancelPendingRewardSync(string key)
        {
            lock (_rewardDebounceLock)
            {
                if (_rewardDebounce.TryGetValue(key, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                    _rewardDebounce.Remove(key);
                }
            }
        }

        private bool TryGetCurrentRewardState(
            string key,
            out bool enabled,
            out int cost,
            out int cooldown,
            out Func<string> getId,
            out Action<string> setId)
        {
            enabled = false;
            cost = 0;
            cooldown = 0;
            getId = null;
            setId = null;

            var cfg = Plugin.Settings;
            if (cfg == null) return false;

            switch (key)
            {
                case "rainbow":
                    enabled = _rainbowCpEnabled;
                    cost = _rainbowCpCost;
                    cooldown = _rainbowCpCooldownSeconds;
                    getId = () => cfg.CpRainbowRewardId;
                    setId = id => cfg.CpRainbowRewardId = id;
                    return true;

                case "disappear":
                    enabled = _daCpEnabled;
                    cost = _daCpCost;
                    cooldown = _daCpCooldownSeconds;
                    getId = () => cfg.CpDisappearRewardId;
                    setId = id => cfg.CpDisappearRewardId = id;
                    return true;

                case "ghost":
                    enabled = _ghostCpEnabled;
                    cost = _ghostCpCost;
                    cooldown = _ghostCpCooldownSeconds;
                    getId = () => cfg.CpGhostRewardId;
                    setId = id => cfg.CpGhostRewardId = id;
                    return true;

                case "bomb":
                    enabled = _bombCpEnabled;
                    cost = _bombCpCost;
                    cooldown = _bombCpCooldownSeconds;
                    getId = () => cfg.CpBombRewardId;
                    setId = id => cfg.CpBombRewardId = id;
                    return true;

                case "faster":
                    enabled = _fasterCpEnabled;
                    cost = _fasterCpCost;
                    cooldown = _fasterCpCooldownSeconds;
                    getId = () => cfg.CpFasterRewardId;
                    setId = id => cfg.CpFasterRewardId = id;
                    return true;

                case "superfast":
                    enabled = _superFastCpEnabled;
                    cost = _superFastCpCost;
                    cooldown = _superFastCpCooldownSeconds;
                    getId = () => cfg.CpSuperFastRewardId;
                    setId = id => cfg.CpSuperFastRewardId = id;
                    return true;

                case "slower":
                    enabled = _slowerCpEnabled;
                    cost = _slowerCpCost;
                    cooldown = _slowerCpCooldownSeconds;
                    getId = () => cfg.CpSlowerRewardId;
                    setId = id => cfg.CpSlowerRewardId = id;
                    return true;

                case "flashbang":
                    enabled = _flashbangCpEnabled;
                    cost = _flashbangCpCost;
                    cooldown = _flashbangCpCooldownSeconds;
                    getId = () => cfg.CpFlashbangRewardId;
                    setId = id => cfg.CpFlashbangRewardId = id;
                    return true;

                default:
                    return false;
            }
        }

        private async Task SyncCurrentKeyNowAsync(string key)
        {
            if (!TryGetCurrentRewardState(key, out var enabled, out var cost, out var cooldown, out var getId, out var setId))
            {
                return;
            }

            CancelPendingRewardSync(key);

            await _rewardSyncGate.WaitAsync();
            try
            {
                var t = GetRewardText(key);
                await SyncRewardAsync(
                    key: key,
                    title: t.Title,
                    prompt: t.Prompt,
                    cost: cost,
                    cooldown: cooldown,
                    enabled: enabled,
                    getId: getId,
                    setId: setId,
                    backgroundColorHex: t.BgHex);
            }
            finally
            {
                _rewardSyncGate.Release();
            }

            await RefreshCpFromTwitchAsync(key);
        }

        private async Task DebouncedRewardSyncWorkerAsync(
            string key,
            int cost,
            int cooldown,
            bool enabled,
            Func<string> getId,
            Action<string> setId,
            string logPrefix,
            CancellationToken ct)
        {
            try
            {
                Plugin.Log.Info($"SurgeonGameplaySetupHost: Queuing update for '{key}' cost={cost} cooldown={cooldown} enabled={enabled}");
                await Task.Delay(RewardSyncDebounce, ct);

                await _rewardSyncGate.WaitAsync(ct);
                try
                {
                    var t = GetRewardText(key);
                    await SyncRewardAsync(
                        key: key,
                        title: t.Title,
                        prompt: t.Prompt,
                        cost: cost,
                        cooldown: cooldown,
                        enabled: enabled,
                        getId: getId,
                        setId: setId,
                        backgroundColorHex: t.BgHex,
                        externalCt: ct);
                }
                finally
                {
                    _rewardSyncGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when user keeps typing / adjusting.
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"{logPrefix} CP Debounced sync failed: {ex.Message}");
            }
            finally
            {
                lock (_rewardDebounceLock)
                {
                    if (_rewardDebounce.TryGetValue(key, out var existing) && existing != null && existing.Token == ct)
                    {
                        existing.Dispose();
                        _rewardDebounce.Remove(key);
                    }
                }
            }
        }

        private void QueueRewardSync(
         string key,
         int cost,
         int cooldown,
         bool enabled,
         Func<string> getId,
         Action<string> setId,
         string logPrefix)
         {
            // If we're currently refreshing modal values, avoid queuing updates back to Twitch.
            // Opening the modal should not cause state changes on Twitch by itself.
            if (_isModalRefreshing)
            {
                LogUtils.Debug(() => $"QueueRewardSync: skipping queue for {key} because modal refresh is active");
                return;
            }

             CancellationTokenSource cts;

             lock (_rewardDebounceLock)
             {
                 if (_rewardDebounce.TryGetValue(key, out var oldCts))
                 {
                     oldCts.Cancel();
                     oldCts.Dispose();
                 }

                 cts = new CancellationTokenSource();
                 _rewardDebounce[key] = cts;
             }

             _ = DebouncedRewardSyncWorkerAsync(
                 key, cost, cooldown, enabled, getId, setId, logPrefix, cts.Token);
         }



        // --- UIActions to open/close modals ---
        [UIAction("OpenTwitchRainbowModal")]
        private void OpenTwitchRainbowModal()
        {
            _isModalRefreshing = true;
            try
            {
                LoadCpFromConfig();
                RefreshCpModalValues("rainbow");
                _twitchRainbowModal?.Show(true);

                _ = Task.Run(async () =>
                {
                    try { await RefreshCpFromTwitchAsync("rainbow").ConfigureAwait(false); }
                    catch { }
                    finally { _isModalRefreshing = false; }
                });
            }
            catch
            {
                _isModalRefreshing = false;
                throw;
            }
        }
        [UIAction("CloseTwitchRainbowModal")]
        private void CloseTwitchRainbowModal()
        {
            _twitchRainbowModal?.Hide(true);
            _ = SyncCurrentKeyNowAsync("rainbow");
        }



        [UIAction("OpenTwitchGhostModal")]
        private void OpenTwitchGhostModal()
        {
            _isModalRefreshing = true;
            try
            {
                LoadCpFromConfig();
                RefreshCpModalValues("ghost");
                _twitchGhostModal?.Show(true);

                _ = Task.Run(async () =>
                {
                    try { await RefreshCpFromTwitchAsync("ghost").ConfigureAwait(false); }
                    catch { }
                    finally { _isModalRefreshing = false; }
                });
            }
            catch
            {
                _isModalRefreshing = false;
                throw;
            }
        }

        [UIAction("CloseTwitchGhostModal")] 
        private void CloseTwitchGhostModal() 
        { 
            _twitchGhostModal?.Hide(true); 
            _ = SyncCurrentKeyNowAsync("ghost");
        }



        [UIAction("OpenTwitchBombModal")]
        private void OpenTwitchBombModal()
        {
            _isModalRefreshing = true;
            try
            {
                LoadCpFromConfig();
                RefreshCpModalValues("bomb");
                _twitchBombModal?.Show(true);

                _ = Task.Run(async () =>
                {
                    try { await RefreshCpFromTwitchAsync("bomb").ConfigureAwait(false); }
                    catch { }
                    finally { _isModalRefreshing = false; }
                });
            }
            catch
            {
                _isModalRefreshing = false;
                throw;
            }
        }
        [UIAction("CloseTwitchBombModal")]
        private void CloseTwitchBombModal()
        {
            _twitchBombModal?.Hide(true);
            _ = SyncCurrentKeyNowAsync("bomb");
        }



        [UIAction("OpenTwitchDAModal")]
        private void OpenTwitchDAModal()
        {
            _isModalRefreshing = true;
            try
            {
                LoadCpFromConfig();
                RefreshCpModalValues("disappear");
                _twitchDAModal?.Show(true);

                _ = Task.Run(async () =>
                {
                    try { await RefreshCpFromTwitchAsync("disappear").ConfigureAwait(false); }
                    catch { }
                    finally { _isModalRefreshing = false; }
                });
            }
            catch
            {
                _isModalRefreshing = false;
                throw;
            }
        }
        [UIAction("CloseTwitchDAModal")]
        private void CloseTwitchDAModal()
        {
            _twitchDAModal?.Hide(true);
            _ = SyncCurrentKeyNowAsync("disappear");
        }



        [UIAction("OpenTwitchFasterModal")] 
        private void OpenTwitchFasterModal()
        {
            _isModalRefreshing = true;
            try
            {
                LoadCpFromConfig();
                RefreshCpModalValues("faster");
                _twitchFasterModal?.Show(true);

                _ = Task.Run(async () =>
                {
                    try { await RefreshCpFromTwitchAsync("faster").ConfigureAwait(false); }
                    catch { }
                    finally { _isModalRefreshing = false; }
                });
            }
            catch
            {
                _isModalRefreshing = false;
                throw;
            }
        }
        [UIAction("CloseTwitchFasterModal")]
        private void CloseTwitchFasterModal()
        {
            _twitchFasterModal?.Hide(true);
            _ = SyncCurrentKeyNowAsync("faster");
        }



        [UIAction("OpenTwitchSuperFastModal")]
        private void OpenTwitchSuperFastModal()
        {
            _isModalRefreshing = true;
            try
            {
                LoadCpFromConfig();
                RefreshCpModalValues("superfast");
                _twitchSuperFastModal?.Show(true);

                _ = Task.Run(async () =>
                {
                    try { await RefreshCpFromTwitchAsync("superfast").ConfigureAwait(false); }
                    catch { }
                    finally { _isModalRefreshing = false; }
                });
            }
            catch
            {
                _isModalRefreshing = false;
                throw;
            }
        }
        [UIAction("CloseTwitchSuperFastModal")]
        private void CloseTwitchSuperFastModal()
        {
            _twitchSuperFastModal?.Hide(true);
            _ = SyncCurrentKeyNowAsync("superfast");
        }



        [UIAction("OpenTwitchSlowerModal")]
        private void OpenTwitchSlowerModal()
        {
            _isModalRefreshing = true;
            try
            {
                LoadCpFromConfig();
                RefreshCpModalValues("slower");
                _twitchSlowerModal?.Show(true);

                _ = Task.Run(async () =>
                {
                    try { await RefreshCpFromTwitchAsync("slower").ConfigureAwait(false); }
                    catch { }
                    finally { _isModalRefreshing = false; }
                });
            }
            catch
            {
                _isModalRefreshing = false;
                throw;
            }
        }
        [UIAction("CloseTwitchSlowerModal")]
        private void CloseTwitchSlowerModal()
        {
            _twitchSlowerModal?.Hide(true);
            _ = SyncCurrentKeyNowAsync("slower");
        }



        [UIAction("OpenTwitchFlashbangModal")]
        private void OpenTwitchFlashbangModal()
        {
            _isModalRefreshing = true;
            try
            {
                LoadCpFromConfig();
                RefreshCpModalValues("flashbang");
                _twitchFlashbangModal?.Show(true);

                _ = Task.Run(async () =>
                {
                    try { await RefreshCpFromTwitchAsync("flashbang").ConfigureAwait(false); }
                    catch { }
                    finally { _isModalRefreshing = false; }
                });
            }
            catch
            {
                _isModalRefreshing = false;
                throw;
            }
        }
        [UIAction("CloseTwitchFlashbangModal")]
        private void CloseTwitchFlashbangModal()
        {
            _twitchFlashbangModal?.Hide(true);
            _ = SyncCurrentKeyNowAsync("flashbang");
        }


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


        /*
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

        */

        internal static void SetTwitchStatusFromBeatSurgeon(string statusText)
        {
            if (Instance == null) return;
            Instance.TwitchStatusText = statusText;
        }


        private void RefreshTwitchStatusText()
        {
            if (Plugin.Settings?.TwitchReauthRequired == true)
            {
                TwitchStatusText = "<color=#FFFF44>Please Reauthorize</color>";
                return;
            }

            if (!TwitchAuthManager.Instance.IsAuthenticated)
            {
                TwitchStatusText = "<color=#FF4444>Not connected</color>";
                return;
            }

            int tier = Plugin.Settings?.CachedSupporterTier ?? 0;
            TwitchStatusText = tier > 0
                ? $"<color=#44FF44>Connected (Tier {tier})</color>"
                : "<color=#44FF44>Connected</color>";
        }

        private void HookTwitchStatusEvents()
        {
            if (_twitchStatusEventsHooked)
            {
                return;
            }

            TwitchAuthManager.Instance.OnReauthRequired += OnTwitchStatusEvent;
            TwitchAuthManager.Instance.OnTokensUpdated += OnTwitchStatusEvent;
            _twitchStatusEventsHooked = true;
        }

        private void OnTwitchStatusEvent()
        {
            _ = UnityMainThreadTaskScheduler.Factory.StartNew(RefreshTwitchStatusText);
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
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpRainbowEnabled = value;
                    QueueRewardSync("rainbow", _rainbowCpCost, _rainbowCpCooldownSeconds, _rainbowCpEnabled,
                        () => cfg.CpRainbowRewardId,
                        id => cfg.CpRainbowRewardId = id,
                        "Rainbow");
                }

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
                if (cfg != null && !_isModalRefreshing) cfg.CpRainbowBackgroundColor = value;
                if (cfg != null && _rainbowCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("rainbow", _rainbowCpCost, _rainbowCpCooldownSeconds, _rainbowCpEnabled,
                        () => cfg.CpRainbowRewardId,
                        id => cfg.CpRainbowRewardId = id,
                        "Rainbow");
                }


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
                    if (cfg != null && !_isModalRefreshing) cfg.CpRainbowCost = _rainbowCpCost;

                    if (cfg != null && _rainbowCpEnabled && !_isModalRefreshing)
                    {
                        QueueRewardSync("rainbow", _rainbowCpCost, _rainbowCpCooldownSeconds, _rainbowCpEnabled,
                            () => cfg.CpRainbowRewardId,
                            id => cfg.CpRainbowRewardId = id,
                            "Rainbow");
                    }

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
                if (cfg != null && !_isModalRefreshing) cfg.CpRainbowCooldownSeconds = _rainbowCpCooldownSeconds;

                if (cfg != null && _rainbowCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("rainbow", _rainbowCpCost, _rainbowCpCooldownSeconds, _rainbowCpEnabled,
                        () => cfg.CpRainbowRewardId,
                        id => cfg.CpRainbowRewardId = id,
                        "Rainbow");
                }


                NotifyPropertyChanged();
            }
        }

        [UIValue("daCpEnabled")]
        public bool DaCpEnabled
        {
            get => _daCpEnabled;
            set
            {
                if (_daCpEnabled == value) return;
                _daCpEnabled = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpDisappearEnabled = value;
                    QueueRewardSync("disappear", _daCpCost, _daCpCooldownSeconds, _daCpEnabled,
                        () => cfg.CpDisappearRewardId,
                        id => cfg.CpDisappearRewardId = id,
                        "DA");
                }

                UpdateTwitchCpButtonVisuals();
                NotifyPropertyChanged();
            }
        }

        [UIValue("daCpBackgroundColor")]
        public Color DaCpBackgroundColor
        {
            get => _daCpBackgroundColor;
            set
            {
                if (_daCpBackgroundColor == value) return;
                _daCpBackgroundColor = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpDisappearBackgroundColor = value;

                if (cfg != null && _daCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("disappear", _daCpCost, _daCpCooldownSeconds, _daCpEnabled,
                        () => cfg.CpDisappearRewardId,
                        id => cfg.CpDisappearRewardId = id,
                        "DA");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("daCpCostText")]
        public string DaCpCostText
        {
            get => _daCpCostText;
            set
            {
                if (_daCpCostText == value) return;
                _daCpCostText = value ?? string.Empty;

                if (int.TryParse(_daCpCostText, out var parsed) && parsed > 0)
                    _daCpCost = parsed;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpDisappearCost = _daCpCost;
                    if (_daCpEnabled)
                    {
                        QueueRewardSync("disappear", _daCpCost, _daCpCooldownSeconds, _daCpEnabled,
                            () => cfg.CpDisappearRewardId,
                            id => cfg.CpDisappearRewardId = id,
                            "DA");
                    }
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("daCpCooldownSeconds")]
        public int DaCpCooldownSeconds
        {
            get => _daCpCooldownSeconds;
            set
            {
                _daCpCooldownSeconds = Math.Max(0, value);

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpDisappearCooldownSeconds = _daCpCooldownSeconds;

                if (cfg != null && _daCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("disappear", _daCpCost, _daCpCooldownSeconds, _daCpEnabled,
                        () => cfg.CpDisappearRewardId,
                        id => cfg.CpDisappearRewardId = id,
                        "DA");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("ghostCpEnabled")]
        public bool GhostCpEnabled
        {
            get => _ghostCpEnabled;
            set
            {
                if (_ghostCpEnabled == value) return;
                _ghostCpEnabled = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpGhostEnabled = value;
                    QueueRewardSync("ghost", _ghostCpCost, _ghostCpCooldownSeconds, _ghostCpEnabled,
                        () => cfg.CpGhostRewardId,
                        id => cfg.CpGhostRewardId = id,
                        "Ghost");
                }

                UpdateTwitchCpButtonVisuals();
                NotifyPropertyChanged();
            }
        }

        [UIValue("ghostCpBackgroundColor")]
        public Color GhostCpBackgroundColor
        {
            get => _ghostCpBackgroundColor;
            set
            {
                if (_ghostCpBackgroundColor == value) return;
                _ghostCpBackgroundColor = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpGhostBackgroundColor = value;

                if (cfg != null && _ghostCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("ghost", _ghostCpCost, _ghostCpCooldownSeconds, _ghostCpEnabled,
                        () => cfg.CpGhostRewardId,
                        id => cfg.CpGhostRewardId = id,
                        "Ghost");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("ghostCpCostText")]
        public string GhostCpCostText
        {
            get => _ghostCpCostText;
            set
            {
                if (_ghostCpCostText == value) return;
                _ghostCpCostText = value ?? string.Empty;

                if (int.TryParse(_ghostCpCostText, out var parsed) && parsed > 0)
                    _ghostCpCost = parsed;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpGhostCost = _ghostCpCost;
                    if (_ghostCpEnabled)
                    {
                        QueueRewardSync("ghost", _ghostCpCost, _ghostCpCooldownSeconds, _ghostCpEnabled,
                            () => cfg.CpGhostRewardId,
                            id => cfg.CpGhostRewardId = id,
                            "Ghost");
                    }
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("ghostCpCooldownSeconds")]
        public int GhostCpCooldownSeconds
        {
            get => _ghostCpCooldownSeconds;
            set
            {
                _ghostCpCooldownSeconds = Math.Max(0, value);

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpGhostCooldownSeconds = _ghostCpCooldownSeconds;

                if (cfg != null && _ghostCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("ghost", _ghostCpCost, _ghostCpCooldownSeconds, _ghostCpEnabled,
                        () => cfg.CpGhostRewardId,
                        id => cfg.CpGhostRewardId = id,
                        "Ghost");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("bombCpEnabled")]
        public bool BombCpEnabled
        {
            get => _bombCpEnabled;
            set
            {
                if (_bombCpEnabled == value) return;
                _bombCpEnabled = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpBombEnabled = value;
                    QueueRewardSync("bomb", _bombCpCost, _bombCpCooldownSeconds, _bombCpEnabled,
                        () => cfg.CpBombRewardId,
                        id => cfg.CpBombRewardId = id,
                        "Bomb");
                }

                UpdateTwitchCpButtonVisuals();
                NotifyPropertyChanged();
            }
        }

        [UIValue("bombCpBackgroundColor")]
        public Color BombCpBackgroundColor
        {
            get => _bombCpBackgroundColor;
            set
            {
                if (_bombCpBackgroundColor == value) return;
                _bombCpBackgroundColor = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpBombBackgroundColor = value;

                if (cfg != null && _bombCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("bomb", _bombCpCost, _bombCpCooldownSeconds, _bombCpEnabled,
                        () => cfg.CpBombRewardId,
                        id => cfg.CpBombRewardId = id,
                        "Bomb");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("bombCpCostText")]
        public string BombCpCostText
        {
            get => _bombCpCostText;
            set
            {
                if (_bombCpCostText == value) return;
                _bombCpCostText = value ?? string.Empty;

                if (int.TryParse(_bombCpCostText, out var parsed) && parsed > 0)
                    _bombCpCost = parsed;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpBombCost = _bombCpCost;
                    if (_bombCpEnabled)
                    {
                        QueueRewardSync("bomb", _bombCpCost, _bombCpCooldownSeconds, _bombCpEnabled,
                            () => cfg.CpBombRewardId,
                            id => cfg.CpBombRewardId = id,
                            "Bomb");
                    }
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("bombCpCooldownSeconds")]
        public int BombCpCooldownSeconds
        {
            get => _bombCpCooldownSeconds;
            set
            {
                _bombCpCooldownSeconds = Math.Max(0, value);

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpBombCooldownSeconds = _bombCpCooldownSeconds;

                if (cfg != null && _bombCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("bomb", _bombCpCost, _bombCpCooldownSeconds, _bombCpEnabled,
                        () => cfg.CpBombRewardId,
                        id => cfg.CpBombRewardId = id,
                        "Bomb");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("fasterCpEnabled")]
        public bool FasterCpEnabled
        {
            get => _fasterCpEnabled;
            set
            {
                if (_fasterCpEnabled == value) return;
                _fasterCpEnabled = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpFasterEnabled = value;
                    QueueRewardSync("faster", _fasterCpCost, _fasterCpCooldownSeconds, _fasterCpEnabled,
                        () => cfg.CpFasterRewardId,
                        id => cfg.CpFasterRewardId = id,
                        "Faster");
                }

                UpdateTwitchCpButtonVisuals();
                NotifyPropertyChanged();
            }
        }

        [UIValue("fasterCpBackgroundColor")]
        public Color FasterCpBackgroundColor
        {
            get => _fasterCpBackgroundColor;
            set
            {
                if (_fasterCpBackgroundColor == value) return;
                _fasterCpBackgroundColor = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpFasterBackgroundColor = value;

                if (cfg != null && _fasterCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("faster", _fasterCpCost, _fasterCpCooldownSeconds, _fasterCpEnabled,
                        () => cfg.CpFasterRewardId,
                        id => cfg.CpFasterRewardId = id,
                        "Faster");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("fasterCpCostText")]
        public string FasterCpCostText
        {
            get => _fasterCpCostText;
            set
            {
                if (_fasterCpCostText == value) return;
                _fasterCpCostText = value ?? string.Empty;

                if (int.TryParse(_fasterCpCostText, out var parsed) && parsed > 0)
                    _fasterCpCost = parsed;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpFasterCost = _fasterCpCost;
                    if (_fasterCpEnabled)
                    {
                        QueueRewardSync("faster", _fasterCpCost, _fasterCpCooldownSeconds, _fasterCpEnabled,
                            () => cfg.CpFasterRewardId,
                            id => cfg.CpFasterRewardId = id,
                            "Faster");
                    }
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("fasterCpCooldownSeconds")]
        public int FasterCpCooldownSeconds
        {
            get => _fasterCpCooldownSeconds;
            set
            {
                _fasterCpCooldownSeconds = Math.Max(0, value);

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpFasterCooldownSeconds = _fasterCpCooldownSeconds;

                if (cfg != null && _fasterCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("faster", _fasterCpCost, _fasterCpCooldownSeconds, _fasterCpEnabled,
                        () => cfg.CpFasterRewardId,
                        id => cfg.CpFasterRewardId = id,
                        "Faster");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("superFastCpEnabled")]
        public bool SuperFastCpEnabled
        {
            get => _superFastCpEnabled;
            set
            {
                if (_superFastCpEnabled == value) return;
                _superFastCpEnabled = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpSuperFastEnabled = value;
                    QueueRewardSync("superfast", _superFastCpCost, _superFastCpCooldownSeconds, _superFastCpEnabled,
                        () => cfg.CpSuperFastRewardId,
                        id => cfg.CpSuperFastRewardId = id,
                        "SuperFast");
                }

                UpdateTwitchCpButtonVisuals();
                NotifyPropertyChanged();
            }
        }

        [UIValue("superFastCpBackgroundColor")]
        public Color SuperFastCpBackgroundColor
        {
            get => _superFastCpBackgroundColor;
            set
            {
                if (_superFastCpBackgroundColor == value) return;
                _superFastCpBackgroundColor = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpSuperFastBackgroundColor = value;

                if (cfg != null && _superFastCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("superfast", _superFastCpCost, _superFastCpCooldownSeconds, _superFastCpEnabled,
                        () => cfg.CpSuperFastRewardId,
                        id => cfg.CpSuperFastRewardId = id,
                        "SuperFast");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("superFastCpCostText")]
        public string SuperFastCpCostText
        {
            get => _superFastCpCostText;
            set
            {
                if (_superFastCpCostText == value) return;
                _superFastCpCostText = value ?? string.Empty;

                if (int.TryParse(_superFastCpCostText, out var parsed) && parsed > 0)
                    _superFastCpCost = parsed;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpSuperFastCost = _superFastCpCost;
                    if (_superFastCpEnabled)
                    {
                        QueueRewardSync("superfast", _superFastCpCost, _superFastCpCooldownSeconds, _superFastCpEnabled,
                            () => cfg.CpSuperFastRewardId,
                            id => cfg.CpSuperFastRewardId = id,
                            "SuperFast");
                    }
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("superFastCpCooldownSeconds")]
        public int SuperFastCpCooldownSeconds
        {
            get => _superFastCpCooldownSeconds;
            set
            {
                _superFastCpCooldownSeconds = Math.Max(0, value);

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpSuperFastCooldownSeconds = _superFastCpCooldownSeconds;

                if (cfg != null && _superFastCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("superfast", _superFastCpCost, _superFastCpCooldownSeconds, _superFastCpEnabled,
                        () => cfg.CpSuperFastRewardId,
                        id => cfg.CpSuperFastRewardId = id,
                        "SuperFast");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("slowerCpEnabled")]
        public bool SlowerCpEnabled
        {
            get => _slowerCpEnabled;
            set
            {
                if (_slowerCpEnabled == value) return;
                _slowerCpEnabled = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpSlowerEnabled = value;
                    QueueRewardSync("slower", _slowerCpCost, _slowerCpCooldownSeconds, _slowerCpEnabled,
                        () => cfg.CpSlowerRewardId,
                        id => cfg.CpSlowerRewardId = id,
                        "Slower");
                }

                UpdateTwitchCpButtonVisuals();
                NotifyPropertyChanged();
            }
        }

        [UIValue("slowerCpBackgroundColor")]
        public Color SlowerCpBackgroundColor
        {
            get => _slowerCpBackgroundColor;
            set
            {
                if (_slowerCpBackgroundColor == value) return;
                _slowerCpBackgroundColor = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpSlowerBackgroundColor = value;

                if (cfg != null && _slowerCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("slower", _slowerCpCost, _slowerCpCooldownSeconds, _slowerCpEnabled,
                        () => cfg.CpSlowerRewardId,
                        id => cfg.CpSlowerRewardId = id,
                        "Slower");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("slowerCpCostText")]
        public string SlowerCpCostText
        {
            get => _slowerCpCostText;
            set
            {
                if (_slowerCpCostText == value) return;
                _slowerCpCostText = value ?? string.Empty;

                if (int.TryParse(_slowerCpCostText, out var parsed) && parsed > 0)
                    _slowerCpCost = parsed;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpSlowerCost = _slowerCpCost;
                    if (_slowerCpEnabled)
                    {
                        QueueRewardSync("slower", _slowerCpCost, _slowerCpCooldownSeconds, _slowerCpEnabled,
                            () => cfg.CpSlowerRewardId,
                            id => cfg.CpSlowerRewardId = id,
                            "Slower");
                    }
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("slowerCpCooldownSeconds")]
        public int SlowerCpCooldownSeconds
        {
            get => _slowerCpCooldownSeconds;
            set
            {
                _slowerCpCooldownSeconds = Math.Max(0, value);

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpSlowerCooldownSeconds = _slowerCpCooldownSeconds;

                if (cfg != null && _slowerCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("slower", _slowerCpCost, _slowerCpCooldownSeconds, _slowerCpEnabled,
                        () => cfg.CpSlowerRewardId,
                        id => cfg.CpSlowerRewardId = id,
                        "Slower");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("flashbangCpEnabled")]
        public bool FlashbangCpEnabled
        {
            get => _flashbangCpEnabled;
            set
            {
                if (_flashbangCpEnabled == value) return;
                _flashbangCpEnabled = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpFlashbangEnabled = value;
                    QueueRewardSync("flashbang", _flashbangCpCost, _flashbangCpCooldownSeconds, _flashbangCpEnabled,
                        () => cfg.CpFlashbangRewardId,
                        id => cfg.CpFlashbangRewardId = id,
                        "Flashbang");
                }

                UpdateTwitchCpButtonVisuals();
                NotifyPropertyChanged();
            }
        }

        [UIValue("flashbangCpBackgroundColor")]
        public Color FlashbangCpBackgroundColor
        {
            get => _flashbangCpBackgroundColor;
            set
            {
                if (_flashbangCpBackgroundColor == value) return;
                _flashbangCpBackgroundColor = value;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpFlashbangBackgroundColor = value;

                if (cfg != null && _flashbangCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("flashbang", _flashbangCpCost, _flashbangCpCooldownSeconds, _flashbangCpEnabled,
                        () => cfg.CpFlashbangRewardId,
                        id => cfg.CpFlashbangRewardId = id,
                        "Flashbang");
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("flashbangCpCostText")]
        public string FlashbangCpCostText
        {
            get => _flashbangCpCostText;
            set
            {
                if (_flashbangCpCostText == value) return;
                _flashbangCpCostText = value ?? string.Empty;

                if (int.TryParse(_flashbangCpCostText, out var parsed) && parsed > 0)
                    _flashbangCpCost = parsed;

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing)
                {
                    cfg.CpFlashbangCost = _flashbangCpCost;
                    if (_flashbangCpEnabled)
                    {
                        QueueRewardSync("flashbang", _flashbangCpCost, _flashbangCpCooldownSeconds, _flashbangCpEnabled,
                            () => cfg.CpFlashbangRewardId,
                            id => cfg.CpFlashbangRewardId = id,
                            "Flashbang");
                    }
                }

                NotifyPropertyChanged();
            }
        }

        [UIValue("flashbangCpCooldownSeconds")]
        public int FlashbangCpCooldownSeconds
        {
            get => _flashbangCpCooldownSeconds;
            set
            {
                _flashbangCpCooldownSeconds = Math.Max(0, value);

                var cfg = Plugin.Settings;
                if (cfg != null && !_isModalRefreshing) cfg.CpFlashbangCooldownSeconds = _flashbangCpCooldownSeconds;

                if (cfg != null && _flashbangCpEnabled && !_isModalRefreshing)
                {
                    QueueRewardSync("flashbang", _flashbangCpCost, _flashbangCpCooldownSeconds, _flashbangCpEnabled,
                        () => cfg.CpFlashbangRewardId,
                        id => cfg.CpFlashbangRewardId = id,
                        "Flashbang");
                }

                NotifyPropertyChanged();
            }
        }

    }
}
