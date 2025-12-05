using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberMarkupLanguage.Util;
using HarmonyLib;
using IPA;
using IPA.Config.Stores;
using IPA.Logging;
using SaberSurgeon.Chat;
using System.Collections;
using SaberSurgeon.UI.FlowCoordinators;
using System;
using System.Linq;
using UnityEngine;
using BS_Utils.Utilities;
using SaberSurgeon.Gameplay;



namespace SaberSurgeon
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Plugin Instance { get; private set; }
        internal static IPA.Logging.Logger Log { get; private set; }

        // Raw BSIPA config object (kept for compatibility if ever need it)
        internal static IPA.Config.Config Configuration { get; private set; }

        // Strongly-typed settings wrapper backed by Configuration

        internal static PluginConfig Settings { get; private set; }

        private bool _menuButtonRegisteredThisMenu = false;

        private MenuButton _menuButton;
        private SaberSurgeonFlowCoordinator _flowCoordinator;

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
            

            // Find the AudioTimeSyncController once when gameplay scene loads
            var audio = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>()
                                 .FirstOrDefault();
            if (audio != null)
            {
                SaberSurgeon.Gameplay.GhostVisualController.Audio = audio;
                Log.Info("GhostVisualController: bound AudioTimeSyncController ");
            }

            // Initialize chat integration
            InitializeChatIntegration();

            // Initialize gameplay manager
            InitializeGameplayManager();

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


        private void OnMenuSceneActive()
        {
            Log.Info("SaberSurgeon : menuSceneActive");
            _menuButtonRegisteredThisMenu = false;

            // Run a small coroutine on the game’s main thread
            CoroutineHost.Instance.StartCoroutine(RegisterMenuButtonWhenReady());
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

                    Log.Info("SaberSurgeon: Menu button registered (delayed)");
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