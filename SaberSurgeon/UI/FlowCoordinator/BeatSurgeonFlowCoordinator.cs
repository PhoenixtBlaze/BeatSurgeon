using HMUI;
using BeatSaberMarkupLanguage;
using BeatSurgeon.UI.Controllers;
using UnityEngine;
using Zenject;
using BeatSurgeon.Gameplay;

namespace BeatSurgeon.UI.FlowCoordinators
{
    public class BeatSurgeonFlowCoordinator : FlowCoordinator
    {
        private BeatSurgeonViewController _viewController;
        private BeatSurgeonCooldownViewController _cooldownViewController;

        [Inject] private GameplaySetupViewController _gameplaySetupViewController;
        [Inject] private MenuTransitionsHelper _menuTransitionsHelper;
        [Inject] private EnvironmentsListModel _environmentsListModel;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("Beat Surgeon");
                showBackButton = true;

                _viewController = BeatSaberUI.CreateViewController<BeatSurgeonViewController>();
                _cooldownViewController = BeatSaberUI.CreateViewController<BeatSurgeonCooldownViewController>();

                GameplayManager.GetInstance().SetDependencies(_menuTransitionsHelper, _environmentsListModel);
            }

            if (addedToHierarchy)
            {
                _gameplaySetupViewController.Setup(
                    showModifiers: true,
                    showEnvironmentOverrideSettings: true,
                    showColorSchemesSettings: true,
                    showMultiplayer: false,
                    playerSettingsPanelLayout: PlayerSettingsPanelController.PlayerSettingsPanelLayout.Singleplayer
                );

                // center = BeatSurgeon, left = gameplay setup, right = cooldowns
                ProvideInitialViewControllers(
                    _viewController,
                    _gameplaySetupViewController,
                    _cooldownViewController
                );
            }
        }
        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }
    }
}
