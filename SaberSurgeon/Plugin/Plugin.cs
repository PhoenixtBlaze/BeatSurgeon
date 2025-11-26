using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberMarkupLanguage.Util;
using IPA;
using IPA.Config.Stores;
using IPA.Logging;
using SaberSurgeon.UI.FlowCoordinators;
using SaberSurgeon.Chat;

namespace SaberSurgeon
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Plugin Instance { get; private set; }
        internal static Logger Log { get; private set; }
        internal static IPA.Config.Config Configuration { get; private set; }

        private MenuButton _menuButton;
        private SaberSurgeonFlowCoordinator _flowCoordinator;

        [Init]
        public void Init(Logger logger, IPA.Config.Config config)
        {
            Log = logger;
            Instance = this;
            Configuration = config;
            Log.Info("SaberSurgeon: Init");
        }

        [OnStart]
        public void OnApplicationStart()
        {
            Log.Info("SaberSurgeon: OnApplicationStart");
            MainMenuAwaiter.MainMenuInitializing += OnMainMenuInitializing;

            // Initialize chat integration
            InitializeChatIntegration();

            // Initialize gameplay manager
            InitializeGameplayManager();
        }

        private void OnMainMenuInitializing()
        {
            try
            {
                Log.Info("SaberSurgeon: MainMenuInitializing");

                if (MenuButtons.Instance == null)
                {
                    Log.Warn("MenuButtons.Instance is null");
                    return;
                }

                _menuButton = new MenuButton("Saber Surgeon", "Open SaberSurgeon settings", ShowFlow);
                MenuButtons.Instance.RegisterButton(_menuButton);
                Log.Info("SaberSurgeon: Menu button registered");
            }
            catch (Exception ex)
            {
                Log.Critical($"SaberSurgeon: Exception in OnMainMenuInitializing: {ex}");
            }
            finally
            {
                MainMenuAwaiter.MainMenuInitializing -= OnMainMenuInitializing;
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

            MainMenuAwaiter.MainMenuInitializing -= OnMainMenuInitializing;
        }
    }
}