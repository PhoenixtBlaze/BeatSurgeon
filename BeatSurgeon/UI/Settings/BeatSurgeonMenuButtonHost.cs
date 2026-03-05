using System;
using System.Collections;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSurgeon.Gameplay;
using BeatSurgeon.UI.FlowCoordinators;
using BeatSurgeon.Utils;
using UnityEngine;
using Zenject;

namespace BeatSurgeon.UI.Settings
{
    internal sealed class BeatSurgeonMenuButtonHost : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("MenuButtonHost");
        private readonly DiContainer _container;

        private MenuButton _menuButton;
        private BeatSurgeonFlowCoordinator _flowCoordinator;
        private Coroutine _registerRoutine;

        public BeatSurgeonMenuButtonHost(DiContainer container)
        {
            _container = container;
        }

        public void Initialize()
        {
            _log.Lifecycle("Initialize - scheduling menu button registration");

            if (_menuButton == null)
                _menuButton = new MenuButton("Beat Surgeon", "Open BeatSurgeon settings", ShowFlow);

            _registerRoutine = CoroutineHost.Instance.StartCoroutine(RegisterMenuButtonWhenReady());
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose - unregistering menu button");

            if (_registerRoutine != null)
            {
                CoroutineHost.Instance.StopCoroutine(_registerRoutine);
                _registerRoutine = null;
            }

            try
            {
                if (_menuButton != null && MenuButtons.Instance != null)
                    MenuButtons.Instance.UnregisterButton(_menuButton);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "UnregisterButton");
            }
        }

        private IEnumerator RegisterMenuButtonWhenReady()
        {
            const int maxRetries = 600;
            int retries = 0;

            while (retries++ < maxRetries)
            {
                if (MenuButtons.Instance != null)
                {
                    MenuButtons.Instance.RegisterButton(_menuButton);
                    _log.Info("BeatSurgeon menu button registered");
                    _registerRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _log.Warn("Timed out waiting for MenuButtons.Instance; menu button not registered");
            _registerRoutine = null;
        }

        private void ShowFlow()
        {
            try
            {
                if (_flowCoordinator == null)
                {
                    _flowCoordinator = BeatSaberUI.CreateFlowCoordinator<BeatSurgeonFlowCoordinator>();
                    _container.Inject(_flowCoordinator);
                }

                if (BeatSaberUI.MainFlowCoordinator == null)
                {
                    _log.Warn("MainFlowCoordinator is null; cannot present BeatSurgeon flow");
                    return;
                }

                BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(_flowCoordinator);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "ShowFlow");
            }
        }
    }
}
