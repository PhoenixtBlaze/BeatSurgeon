using HMUI;
using BeatSaberMarkupLanguage;
using SaberSurgeon.UI.Controllers;
using UnityEngine;
using Zenject;
using SaberSurgeon.Gameplay;

namespace SaberSurgeon.UI.FlowCoordinators
{
    public class SaberSurgeonFlowCoordinator : FlowCoordinator
    {
        private SaberSurgeonViewController _viewController;

        [Inject] private GameplaySetupViewController _gameplaySetupViewController;
        [Inject] private MenuTransitionsHelper _menuTransitionsHelper;
        [Inject] private EnvironmentsListModel _environmentsListModel;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("Saber Surgeon");
                showBackButton = true;

                _viewController = BeatSaberUI.CreateViewController<SaberSurgeonViewController>();

                // NEW: Inject dependencies into GameplayManager
                GameplayManager.GetInstance().SetDependencies(_menuTransitionsHelper, _environmentsListModel);
            }

            if (addedToHierarchy)
            {
                // Important: configure which tabs to show, just like Shaffuru does
                _gameplaySetupViewController.Setup(
                    true,  // enableModifiers
                    true,  // enablePlayerSettings
                    true,  // enableEnvironmentOverrides
                    true, // enableColorSchemes (set true if you want color schemes)
                    
                    PlayerSettingsPanelController.PlayerSettingsPanelLayout.Singleplayer
                );

                ProvideInitialViewControllers(
                    _viewController,
                    _gameplaySetupViewController,
                    null
                );
            }
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }
    }
}
