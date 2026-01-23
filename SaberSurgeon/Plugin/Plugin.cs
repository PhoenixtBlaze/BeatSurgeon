using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.GameplaySetup;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberMarkupLanguage.Settings;
using BeatSaberMarkupLanguage.Util;
using BS_Utils.Utilities;
using HarmonyLib;
using IPA;
using IPA.Config.Stores;
using IPA.Logging;
using BeatSurgeon.Chat;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Twitch;
using BeatSurgeon.UI.FlowCoordinators;
using BeatSurgeon.UI.Settings;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using System.IO;
using System.Collections.Generic;




namespace BeatSurgeon
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Plugin Instance { get; private set; }
        internal static IPA.Logging.Logger Log { get; private set; }

        private bool pfslTabRegisteredThisMenu = false;
        private bool surgeonTabRegisteredThisMenu = false;


        // Raw BSIPA config object (kept for compatibility if ever need it)
        internal static IPA.Config.Config Configuration { get; private set; }

        // Strongly-typed settings wrapper backed by Configuration

        internal static PluginConfig Settings { get; private set; }

        private bool _menuButtonRegisteredThisMenu = false;
        private TwitchEventSubClient _eventSubClient;
        private MenuButton _menuButton;
        private BeatSurgeonFlowCoordinator _flowCoordinator;
        //private BeatSurgeon.UI.FloatingChatOverlay _floatingChatOverlay;


        [Init]
        public void Init(IPA.Logging.Logger logger, IPA.Config.Config config)
        {
            EnsureEmbeddedLibrariesLoaded();
            Log = logger;
            Instance = this;
            Settings = config.Generated<PluginConfig>();
            PluginConfig.Instance = Settings;
            Log.Info("BeatSurgeon: Init");

            // Ensure stable per-install MP client id
            if (string.IsNullOrWhiteSpace(Settings.MpClientId))
            {
                Settings.MpClientId = Guid.NewGuid().ToString("N"); // 32 hex chars
                Log.Info($"BeatSurgeon: Generated MpClientId={Settings.MpClientId}");
            }
        }

        [OnStart]
        public void OnApplicationStart()
        {
            Log.Info("BeatSurgeon: OnApplicationStart");

            // Start font bundle load as early as possible
            BeatSurgeon.Gameplay.FontBundleLoader.CopyBundleFromPluginFolderIfMissing();
            _ = BeatSurgeon.Gameplay.FontBundleLoader.EnsureLoadedAsync();

            SceneHelper.Init();

            BSEvents.menuSceneActive += OnMenuSceneActive;

            MultiplayerStateClient.Init();
            MultiplayerRoomSyncClient.Init();

            // Bind AudioTimeSyncController (unchanged)
            var audio = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>()
                .FirstOrDefault();
            if (audio != null)
            {
                BeatSurgeon.Gameplay.GhostVisualController.Audio = audio;
                Log.Info("GhostVisualController: bound AudioTimeSyncController ");
            }

            // 1) Auth first – loads tokens and may kick off Helix fetch
            BeatSurgeon.Twitch.TwitchAuthManager.Instance.Initialize();

            // 2) Then chat manager (so it can see CachedBroadcasterId if available)
            InitializeChatIntegration();

            // ADD THIS LINE - Initialize EventSub and channel point executor
            InitializeTwitchEventSub();

            // 3) Gameplay manager
            InitializeGameplayManager();

            _ = BeatSurgeon.Gameplay.PlayFirstSubmitLaterManager.Instance;

            // Harmony patches - patch ALL BeatSurgeon harmony classes
            try
            {
                var harmony = new Harmony("BeatSurgeon");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info("BeatSurgeon: All Harmony patches applied");
            }
            catch (Exception ex)
            {
                Log.Error($"BeatSurgeon: Harmony patch error: {ex}");
            }
        }



        private bool _pfslTabRegistered;
        private void OnMenuSceneActive()
        {
            Log.Info("BeatSurgeon : menuSceneActive");
            _menuButtonRegisteredThisMenu = false;
            surgeonTabRegisteredThisMenu = false;
            pfslTabRegisteredThisMenu = false;

            
            // Run a small coroutine on the game’s main thread
            CoroutineHost.Instance.StartCoroutine(RegisterMenuButtonWhenReady());
            CoroutineHost.Instance.StartCoroutine(RegisterPfslGameplaySetupTabWhenReady());
            CoroutineHost.Instance.StartCoroutine(RegisterSurgeonGameplaySetupTabWhenReady());

            if (_pfslTabRegistered) return;

            // Ensure your standalone PFSL runtime module exists if you want it initialized early
            _ = Gameplay.PlayFirstSubmitLaterManager.Instance;
            _pfslTabRegistered = true;
        }


        private static bool _embeddedLibsHooked;

        private static void EnsureEmbeddedLibrariesLoaded()
        {
            if (_embeddedLibsHooked) return;
            _embeddedLibsHooked = true;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    var requested = new AssemblyName(args.Name).Name + ".dll";

                    // Only handle Chaos.NaCl (avoid interfering with other mods)
                    if (!string.Equals(requested, "Chaos.NaCl.dll", StringComparison.OrdinalIgnoreCase))
                        return null;

                    var asm = Assembly.GetExecutingAssembly();
                    var names = asm.GetManifestResourceNames();

                    // Find an embedded resource that ends with "Chaos.NaCl.dll"
                    string resourceName = null;
                    foreach (var n in names)
                    {
                        if (n.EndsWith("Chaos.NaCl.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            resourceName = n;
                            break;
                        }
                    }

                    if (resourceName == null)
                        return null;

                    using (var stream = asm.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null) return null;
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            return Assembly.Load(ms.ToArray());
                        }
                    }
                }
                catch
                {
                    return null;
                }
            };
        }


        private IEnumerator RegisterSurgeonGameplaySetupTabWhenReady()
        {
            while (!surgeonTabRegisteredThisMenu)
            {
                yield return null;

                try
                {
                    var gs = BeatSaberMarkupLanguage.GameplaySetup.GameplaySetup.Instance;
                    if (gs == null) continue;

                    gs.AddTab(
                        "Surgeon",
                        "BeatSurgeon.UI.Views.SurgeonGameplaySetup.bsml",
                        BeatSurgeon.UI.Settings.SurgeonGameplaySetupHost.Instance
                    );

                    surgeonTabRegisteredThisMenu = true;
                    Log.Info("Surgeon GameplaySetup tab registered delayed");
                }
                catch (InvalidOperationException ex)
                {
                    Log.Debug($"Surgeon GameplaySetup not ready yet: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed registering Surgeon GameplaySetup tab: {ex}");
                    yield break;
                }
            }
        }

        private IEnumerator RegisterPfslGameplaySetupTabWhenReady()
        {
            while (!pfslTabRegisteredThisMenu)
            {
                // Wait a frame so Zenject/BSML can finish installing menu bindings
                yield return null;

                try
                {
                    // This line is safe to run early; it just ensures your module exists
                    _ = PlayFirstSubmitLaterManager.Instance;

                    // If BSML isn't ready, the next call may throw InvalidOperationException.
                    var gs = BeatSaberMarkupLanguage.GameplaySetup.GameplaySetup.Instance;
                    if (gs == null) continue;

                    gs.AddTab(
                        "Submit Later",
                        "BeatSurgeon.UI.Views.PlayFirstSubmitLaterGameplaySetup.bsml",
                        BeatSurgeon.UI.Settings.PlayFirstSubmitLaterSettingsHost.Instance
                    );

                    pfslTabRegisteredThisMenu = true;
                    Log.Info("PlayFirstSubmitLater: GameplaySetup tab registered (delayed)");
                }
                catch (InvalidOperationException ex)
                {
                    // This matches your existing “too early” handling style for MenuButtons
                    Log.Debug("PlayFirstSubmitLater: GameplaySetup not ready yet: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error("PlayFirstSubmitLater: Failed registering GameplaySetup tab: " + ex);
                    yield break;
                }
            }
        }
        

        private IEnumerator RegisterMenuButtonWhenReady()
        {
            int retries = 0;
            const int MaxRetries = 300; // ~5-10 seconds depending on framerate

            while (!_menuButtonRegisteredThisMenu && retries < MaxRetries)
            {
                retries++;
                yield return null; // Wait one frame

                try
                {
                    if (MenuButtons.Instance == null) continue;

                    if (_menuButton == null)
                        _menuButton = new MenuButton("Beat Surgeon", "Open BeatSurgeon settings", ShowFlow);

                    MenuButtons.Instance.RegisterButton(_menuButton);
                    _menuButtonRegisteredThisMenu = true;
                    Log.Info("BeatSurgeon: Menu button registered.");
                }
                catch (Exception ex)
                {
                    Log.Error($"BeatSurgeon: Error registering button: {ex.Message}");
                    yield break; // Stop trying on error
                }
            }

            if (!_menuButtonRegisteredThisMenu)
            {
                Log.Warn("BeatSurgeon: Timed out waiting for MenuButtons.Instance");
            }
        }

        /// <summary>
        /// Initialize Twitch EventSub connection and channel point executor
        /// </summary>
        private void InitializeTwitchEventSub()
        {
            try
            {
                Log.Info("BeatSurgeon: Initializing Twitch EventSub...");

                // Fire and forget: connect in background
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        // Wait for auth to be ready
                        await TwitchAuthManager.Instance.EnsureReadyAsync();

                        string token = TwitchAuthManager.Instance.GetAccessToken();
                        string clientId = TwitchAuthManager.Instance.ClientId;
                        string broadcasterId = TwitchAuthManager.Instance.BroadcasterId;
                        string botUserId = TwitchAuthManager.Instance.BotUserId;

                        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(broadcasterId))
                        {
                            Log.Warn("BeatSurgeon: Cannot initialize EventSub - missing auth credentials");
                            return;
                        }

                        // Create EventSub client
                        _eventSubClient = new TwitchEventSubClient(token, clientId, broadcasterId, botUserId);

                        // Initialize the command executor to handle redemptions
                        ChannelPointCommandExecutor.Instance.Initialize(_eventSubClient);

                        // Connect to Twitch WebSocket
                        await _eventSubClient.ConnectAsync();

                        Log.Info("BeatSurgeon: Twitch EventSub connected and channel point executor initialized!");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"BeatSurgeon: Failed to initialize EventSub: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"BeatSurgeon: Exception in InitializeTwitchEventSub: {ex.Message}");
            }
        }

        private void ShowFlow()
        {
            Log.Info("BeatSurgeon: ShowFlow called");

            if (_flowCoordinator == null)
            {
                _flowCoordinator = BeatSaberUI.CreateFlowCoordinator<BeatSurgeonFlowCoordinator>();
            }

            BeatSaberUI.MainFlowCoordinator?.PresentFlowCoordinator(_flowCoordinator);
        }

        /// <summary>
        /// Initialize chat integration with ChatPlexSDK
        /// </summary>
        private void InitializeChatIntegration()
        {
            try
            {
                
                Log.Info("BeatSurgeon: Initializing chat integration...");
                

                // Get ChatManager instance and initialize
                var chatManager = ChatManager.GetInstance();
                chatManager.Initialize();

                // Initialize command handler
                CommandHandler.Instance.Initialize();

                
                Log.Info("BeatSurgeon: Chat integration setup complete!");
                
            }
            catch (Exception ex)
            {
                
                Log.Error($"BeatSurgeon: Exception in InitializeChatIntegration!");
                Log.Error($"  Message: {ex.Message}");
                Log.Error($"  Stack: {ex.StackTrace}");
                
            }
        }

        private void InitializeGameplayManager()
        {
            try
            {
                Plugin.Log.Info("BeatSurgeon: Initializing gameplay manager...");
                var gameplayManager = Gameplay.GameplayManager.GetInstance();
                Plugin.Log.Info("BeatSurgeon: Gameplay manager initialized!");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"BeatSurgeon: Exception initializing gameplay manager: {ex.Message}");
            }
        }


        [OnExit]
        public void OnApplicationQuit()
        {
            Log.Info("BeatSurgeon: OnApplicationQuit");

            try
            {
                SceneHelper.Dispose();
                TwitchApiClient.ClearCache();
                BSEvents.menuSceneActive -= OnMenuSceneActive;
                MultiplayerRoomSyncClient.Dispose();
                BSMLSettings.Instance.RemoveSettingsMenu(PlayFirstSubmitLaterSettingsHost.Instance);
                CommandHandler.Instance.Shutdown();
                ChatManager.GetInstance().Shutdown();
                Gameplay.GameplayManager.GetInstance().Shutdown();
                ChannelPointCommandExecutor.Instance.Shutdown();
                if (_eventSubClient != null)
                {
                    _eventSubClient.Shutdown();
                    _eventSubClient = null;
                }

                Log.Info("BeatSurgeon: Chat integration shut down");
            }
            catch (Exception ex)
            {
                Log.Error($"BeatSurgeon: Error during shutdown: {ex}");
            }
            try
            {
                // Disable all CP rewards when exiting so they don't remain redeemable.
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var cfg = Plugin.Settings;
                        if (cfg == null) return;

                        var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));

                        if (cfg.CpRainbowEnabled) await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpRainbowRewardId, false, cts.Token);
                        if (cfg.CpDisappearEnabled) await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpDisappearRewardId, false, cts.Token);
                        if (cfg.CpGhostEnabled) await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpGhostRewardId, false, cts.Token);
                        if (cfg.CpBombEnabled) await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpBombRewardId, false, cts.Token);
                        if (cfg.CpFasterEnabled) await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpFasterRewardId, false, cts.Token);
                        if (cfg.CpSuperFastEnabled) await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpSuperFastRewardId, false, cts.Token);
                        if (cfg.CpSlowerEnabled) await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpSlowerRewardId, false, cts.Token);
                        if (cfg.CpFlashbangEnabled) await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpFlashbangRewardId, false, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Warn("CP disable-on-exit failed: " + ex.Message);
                    }
                });
            }
            catch { }


            try
            {
                if (_menuButton != null && MenuButtons.Instance != null)
                {
                    MenuButtons.Instance.UnregisterButton(_menuButton);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"BeatSurgeon: Error unregistering menu button: {ex}");
            }

            
        }
    }
}