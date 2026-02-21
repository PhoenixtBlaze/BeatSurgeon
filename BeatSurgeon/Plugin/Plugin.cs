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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;



namespace BeatSurgeon
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        static Plugin()
        {
            // Hook AssemblyResolve BEFORE IPA tries to load any dependencies
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var requested = new AssemblyName(args.Name);

                // Only handle Chaos.NaCl
                if (!requested.Name.Equals("Chaos.NaCl", StringComparison.OrdinalIgnoreCase))
                    return null;

                var asm = Assembly.GetExecutingAssembly();
                var names = asm.GetManifestResourceNames();

                // Log all embedded resources for debugging
                if (Log != null)
                {
                    Log.Debug($"BeatSurgeon: Looking for Chaos.NaCl.dll in embedded resources:");
                    foreach (var n in names)
                        Log.Debug($"  - {n}");
                }

                // Find embedded resource
                string resourceName = names.FirstOrDefault(n =>
                    n.EndsWith("Chaos.NaCl.dll", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    if (Log != null)
                        Log.Error("BeatSurgeon: Chaos.NaCl.dll not found in embedded resources!");
                    return null;
                }

                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;

                    byte[] assemblyData = new byte[stream.Length];
                    stream.Read(assemblyData, 0, assemblyData.Length);

                    var loaded = Assembly.Load(assemblyData);
                    if (Log != null)
                        Log.Info($"BeatSurgeon: Loaded Chaos.NaCl.dll from embedded resource '{resourceName}'");
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.Error($"BeatSurgeon: Failed to load Chaos.NaCl: {ex.Message}");
                return null;
            }
        }
        internal static Plugin Instance { get; private set; }
        internal static IPA.Logging.Logger Log { get; private set; }

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

            // ADD: register quitting handler so we can synchronously disable rewards on quit
            try
            {
                UnityEngine.Application.quitting += OnApplicationQuitting;
            }
            catch (Exception ex)
            {
                Log.Warn($"BeatSurgeon: Failed to register Application.quitting handler: {ex.Message}");
            }
        }

        private void OnApplicationQuitting()
        {
            // Cancel all pending reward cooldown re-enable tasks FIRST,
            // so they don't re-enable rewards after DisableAllRewardsOnQuitAsync disables them.
            ChannelPointCommandExecutor.Instance.Shutdown();

            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                Exception workerException = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        DisableAllRewardsOnQuitAsync(cts.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        workerException = ex;
                    }
                });

                worker.IsBackground = true;
                worker.Start();

                if (!worker.Join(8000))
                {
                    try { cts.Cancel(); } catch { }
                    Log.Warn("BeatSurgeon: DisableAllRewardsOnQuitAsync timed out.");
                }
                else if (workerException != null)
                {
                    Log.Warn($"BeatSurgeon: DisableAllRewards on quit failed: {workerException.Message}");
                }
            }
            catch (Exception ex) { Log.Warn($"BeatSurgeon: DisableAllRewards on quit failed: {ex.Message}"); }
            finally
            {
                try { cts.Dispose(); } catch { }
            }
        }


        private async Task DisableAllRewardsOnQuitAsync(System.Threading.CancellationToken ct)
        {
            var cfg = Plugin.Settings;
            if (cfg == null) return;

            // Attempt to disable each reward individually and log failures rather than short-circuiting
            try
            {
                if (cfg.CpRainbowEnabled   && !string.IsNullOrEmpty(cfg.CpRainbowRewardId))
                {
                    try { await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpRainbowRewardId,   false, ct); Log.Info("BeatSurgeon: Disabled Rainbow CP reward on quit."); }
                    catch (Exception ex) { Log.Warn($"BeatSurgeon: Failed disabling Rainbow CP reward on quit: {ex.Message}"); }
                }

                if (cfg.CpDisappearEnabled && !string.IsNullOrEmpty(cfg.CpDisappearRewardId))
                {
                    try { await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpDisappearRewardId, false, ct); Log.Info("BeatSurgeon: Disabled Disappear CP reward on quit."); }
                    catch (Exception ex) { Log.Warn($"BeatSurgeon: Failed disabling Disappear CP reward on quit: {ex.Message}"); }
                }

                if (cfg.CpGhostEnabled     && !string.IsNullOrEmpty(cfg.CpGhostRewardId))
                {
                    try { await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpGhostRewardId,     false, ct); Log.Info("BeatSurgeon: Disabled Ghost CP reward on quit."); }
                    catch (Exception ex) { Log.Warn($"BeatSurgeon: Failed disabling Ghost CP reward on quit: {ex.Message}"); }
                }

                if (cfg.CpBombEnabled      && !string.IsNullOrEmpty(cfg.CpBombRewardId))
                {
                    try { await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpBombRewardId,      false, ct); Log.Info("BeatSurgeon: Disabled Bomb CP reward on quit."); }
                    catch (Exception ex) { Log.Warn($"BeatSurgeon: Failed disabling Bomb CP reward on quit: {ex.Message}"); }
                }

                if (cfg.CpFasterEnabled    && !string.IsNullOrEmpty(cfg.CpFasterRewardId))
                {
                    try { await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpFasterRewardId,    false, ct); Log.Info("BeatSurgeon: Disabled Faster CP reward on quit."); }
                    catch (Exception ex) { Log.Warn($"BeatSurgeon: Failed disabling Faster CP reward on quit: {ex.Message}"); }
                }

                if (cfg.CpSuperFastEnabled && !string.IsNullOrEmpty(cfg.CpSuperFastRewardId))
                {
                    try { await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpSuperFastRewardId, false, ct); Log.Info("BeatSurgeon: Disabled SuperFast CP reward on quit."); }
                    catch (Exception ex) { Log.Warn($"BeatSurgeon: Failed disabling SuperFast CP reward on quit: {ex.Message}"); }
                }

                if (cfg.CpSlowerEnabled    && !string.IsNullOrEmpty(cfg.CpSlowerRewardId))
                {
                    try { await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpSlowerRewardId,    false, ct); Log.Info("BeatSurgeon: Disabled Slower CP reward on quit."); }
                    catch (Exception ex) { Log.Warn($"BeatSurgeon: Failed disabling Slower CP reward on quit: {ex.Message}"); }
                }

                if (cfg.CpFlashbangEnabled && !string.IsNullOrEmpty(cfg.CpFlashbangRewardId))
                {
                    try { await TwitchChannelPointsManager.Instance.SetRewardEnabledAsync(cfg.CpFlashbangRewardId, false, ct); Log.Info("BeatSurgeon: Disabled Flashbang CP reward on quit."); }
                    catch (Exception ex) { Log.Warn($"BeatSurgeon: Failed disabling Flashbang CP reward on quit: {ex.Message}"); }
                }

                Log.Info("BeatSurgeon All CP rewards disable attempts completed on quit.");
            }
            catch (Exception ex)
            {
                Log.Warn($"BeatSurgeon DisableAllRewardsOnQuitAsync failed: {ex.Message}");
            }
        }


        
        internal async Task SubscribeToRewardAsync(string rewardId)
        {
            if (_eventSubClient == null || string.IsNullOrWhiteSpace(rewardId)) return;
            try
            {
                await _eventSubClient.EnsureChannelPointSubscriptionsAsync(
                    new[] { rewardId });
            }
            catch (Exception ex)
            {
                Log.Warn($"BeatSurgeon SubscribeToReward failed for {rewardId}: {ex.Message}");
            }
        }

        private void OnMenuSceneActive()
        {
            Log.Info("BeatSurgeon : menuSceneActive");
            _menuButtonRegisteredThisMenu = false;
            surgeonTabRegisteredThisMenu = false;

            
            // Run a small coroutine on the game’s main thread
            CoroutineHost.Instance.StartCoroutine(RegisterMenuButtonWhenReady());
            CoroutineHost.Instance.StartCoroutine(RegisterSurgeonGameplaySetupTabWhenReady());

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
                LogUtils.Debug(() => "BeatSurgeon: Initializing gameplay manager...");
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

            // Removed the previous fire-and-forget Disable CP block - Application.quitting handles it synchronously now.

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
