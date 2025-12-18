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
using SaberSurgeon.Chat;
using SaberSurgeon.Gameplay;
using SaberSurgeon.UI.FlowCoordinators;
using SaberSurgeon.UI.Settings;
using System;
using System.Collections;
using System.Linq;
using UnityEngine;



namespace SaberSurgeon
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Plugin Instance { get; private set; }
        internal static IPA.Logging.Logger Log { get; private set; }

        private bool pfslTabRegisteredThisMenu = false;

        // Raw BSIPA config object (kept for compatibility if ever need it)
        internal static IPA.Config.Config Configuration { get; private set; }

        // Strongly-typed settings wrapper backed by Configuration

        internal static PluginConfig Settings { get; private set; }

        private bool _menuButtonRegisteredThisMenu = false;

        private MenuButton _menuButton;
        private SaberSurgeonFlowCoordinator _flowCoordinator;
        //private SaberSurgeon.UI.FloatingChatOverlay _floatingChatOverlay;


        [Init]
        public void Init(IPA.Logging.Logger logger, IPA.Config.Config config)
        {
            Log = logger;
            Instance = this;
            Settings = config.Generated<PluginConfig>();
            PluginConfig.Instance = Settings;
            Log.Info("SaberSurgeon: Init");

        }

        [OnStart]
        public void OnApplicationStart()
        {
            Log.Info("SaberSurgeon: OnApplicationStart");

            BSEvents.menuSceneActive += OnMenuSceneActive;

            // Bind AudioTimeSyncController (unchanged)
            var audio = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>()
                .FirstOrDefault();
            if (audio != null)
            {
                SaberSurgeon.Gameplay.GhostVisualController.Audio = audio;
                Log.Info("GhostVisualController: bound AudioTimeSyncController ");
            }

            // 1) Auth first – loads tokens and may kick off Helix fetch
            SaberSurgeon.Twitch.TwitchAuthManager.Instance.Initialize();

            // 2) Then chat manager (so it can see CachedBroadcasterId if available)
            InitializeChatIntegration();

            
            // 3) Gameplay manager
            InitializeGameplayManager();

            _ = SaberSurgeon.Gameplay.PlayFirstSubmitLaterManager.Instance;


            // Harmony patch (unchanged)
            try
            {
                var h = new Harmony("SaberSurgeon.Endless");
                h.PatchAll(typeof(SaberSurgeon.HarmonyPatches.EndlessHarmonyPatch).Assembly);
                Log.Info("SaberSurgeon: EndlessHarmonyPatch applied");
            }
            catch (Exception ex)
            {
                Log.Error($"SaberSurgeon: Harmony patch error: {ex}");
            }


        }


        private bool _pfslTabRegistered;
        private void OnMenuSceneActive()
        {
            Log.Info("SaberSurgeon : menuSceneActive");
            _menuButtonRegisteredThisMenu = false;
            pfslTabRegisteredThisMenu = false;

            
            // Run a small coroutine on the game’s main thread
            CoroutineHost.Instance.StartCoroutine(RegisterMenuButtonWhenReady());
            CoroutineHost.Instance.StartCoroutine(RegisterPfslGameplaySetupTabWhenReady());

            if (_pfslTabRegistered) return;

            // Ensure your standalone PFSL runtime module exists if you want it initialized early
            _ = Gameplay.PlayFirstSubmitLaterManager.Instance;
            _pfslTabRegistered = true;
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
                        "SaberSurgeon.UI.Views.PlayFirstSubmitLaterGameplaySetup.bsml",
                        SaberSurgeon.UI.Settings.PlayFirstSubmitLaterSettingsHost.Instance
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
            // Try once per frame until MenuButtons is ready or we succeed
            while (!_menuButtonRegisteredThisMenu)
            {
                // Wait one frame to give Zenject / BSML time to finish installing
                yield return null;

                try
                {
                    // If BSML isn't fully initialized, this may throw – we just loop again
                    if (MenuButtons.Instance == null)
                        continue;

                    if (_menuButton == null)
                        _menuButton = new MenuButton("Saber Surgeon", "Open SaberSurgeon settings", ShowFlow);

                    MenuButtons.Instance.RegisterButton(_menuButton);
                    _menuButtonRegisteredThisMenu = true;

                    Log.Info("SaberSurgeon: Menu button registered + (delayed)");

                    /*
                     
                    // Create floating chat overlay once, when BSML is definitely ready
                    if (_floatingChatOverlay == null)
                        _floatingChatOverlay = SaberSurgeon.UI.FloatingChatOverlay.Create(); 
                    */
                }
                catch (System.InvalidOperationException ex)
                {
                    // "Tried getting MenuButtons too early!" – ignore and try again next frame
                    Log.Debug($"SaberSurgeon: MenuButtons not ready yet: {ex.Message}");
                }
                catch (System.Exception ex)
                {
                    // Any unexpected error: log and stop trying this menu
                    Log.Error($"SaberSurgeon: Unexpected error registering menu button: {ex}");
                    yield break;
                }
            }
        }


        private void ShowFlow()
        {
            Log.Info("SaberSurgeon: ShowFlow called");

            if (_flowCoordinator == null)
            {
                _flowCoordinator = BeatSaberUI.CreateFlowCoordinator<SaberSurgeonFlowCoordinator>();
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
                
                Log.Info("SaberSurgeon: Initializing chat integration...");
                

                // Get ChatManager instance and initialize
                var chatManager = ChatManager.GetInstance();
                chatManager.Initialize();

                // Initialize command handler
                CommandHandler.Instance.Initialize();

                
                Log.Info("SaberSurgeon: Chat integration setup complete!");
                
            }
            catch (Exception ex)
            {
                
                Log.Error($"SaberSurgeon: Exception in InitializeChatIntegration!");
                Log.Error($"  Message: {ex.Message}");
                Log.Error($"  Stack: {ex.StackTrace}");
                
            }
        }

        private void InitializeGameplayManager()
        {
            try
            {
                Plugin.Log.Info("SaberSurgeon: Initializing gameplay manager...");
                var gameplayManager = Gameplay.GameplayManager.GetInstance();
                Plugin.Log.Info("SaberSurgeon: Gameplay manager initialized!");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"SaberSurgeon: Exception initializing gameplay manager: {ex.Message}");
            }
        }


        [OnExit]
        public void OnApplicationQuit()
        {
            Log.Info("SaberSurgeon: OnApplicationQuit");

            try
            {
                BSEvents.menuSceneActive -= OnMenuSceneActive;
                //BSMLSettings.Instance.RemoveSettingsMenu(PlayFirstSubmitLaterSettingsHost.Instance);
                CommandHandler.Instance.Shutdown();
                ChatManager.GetInstance().Shutdown();
                Gameplay.GameplayManager.GetInstance().Shutdown();
                Log.Info("SaberSurgeon: Chat integration shut down");
            }
            catch (Exception ex)
            {
                Log.Error($"SaberSurgeon: Error during shutdown: {ex}");
            }

            try
            {
                if (_menuButton != null && MenuButtons.Instance != null)
                {
                    MenuButtons.Instance.UnregisterButton(_menuButton);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"SaberSurgeon: Error unregistering menu button: {ex}");
            }

            
        }
    }
}