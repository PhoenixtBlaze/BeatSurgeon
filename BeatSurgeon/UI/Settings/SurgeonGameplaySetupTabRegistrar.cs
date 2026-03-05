using System;
using System.Collections;
using BeatSaberMarkupLanguage.GameplaySetup;
using BS_Utils.Utilities;
using BeatSurgeon.Gameplay;
using BeatSurgeon.Utils;
using UnityEngine;
using Zenject;

namespace BeatSurgeon.UI.Settings
{
    internal sealed class SurgeonGameplaySetupTabRegistrar : IInitializable, IDisposable
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("SurgeonGameplaySetupTabRegistrar");
        private const string TabName = "Surgeon";
        private const string ResourcePath = "BeatSurgeon.UI.Views.SurgeonGameplaySetup.bsml";

        private Coroutine _registerRoutine;
        private bool _tabRegistered;

        public void Initialize()
        {
            _log.Lifecycle("Initialize - scheduling GameplaySetup tab registration");
            BSEvents.menuSceneActive += OnMenuSceneActive;
            ScheduleRegister();
        }

        public void Dispose()
        {
            _log.Lifecycle("Dispose - removing GameplaySetup tab");
            BSEvents.menuSceneActive -= OnMenuSceneActive;

            if (_registerRoutine != null)
            {
                CoroutineHost.Instance.StopCoroutine(_registerRoutine);
                _registerRoutine = null;
            }

            if (!_tabRegistered)
            {
                return;
            }

            try
            {
                GameplaySetup.Instance?.RemoveTab(TabName);
            }
            catch (Exception ex)
            {
                _log.Exception(ex, "RemoveTab");
            }

            _tabRegistered = false;
        }

        private void OnMenuSceneActive()
        {
            _log.Debug("menuSceneActive - scheduling GameplaySetup tab registration");
            ScheduleRegister();
        }

        private void ScheduleRegister()
        {
            if (_registerRoutine != null)
            {
                CoroutineHost.Instance.StopCoroutine(_registerRoutine);
                _registerRoutine = null;
            }

            _registerRoutine = CoroutineHost.Instance.StartCoroutine(RegisterWhenReady());
        }

        private IEnumerator RegisterWhenReady()
        {
            const int maxRetries = 600;
            int retries = 0;

            while (retries++ < maxRetries)
            {
                GameplaySetup setup = GameplaySetup.Instance;
                if (setup != null)
                {
                    try
                    {
                        if (_tabRegistered)
                        {
                            try { setup.RemoveTab(TabName); } catch { }
                            _tabRegistered = false;
                        }

                        setup.AddTab(TabName, ResourcePath, SurgeonGameplaySetupHost.Instance);
                        _tabRegistered = true;
                        _registerRoutine = null;
                        _log.Info("Tab '" + TabName + "' registered");
                        yield break;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _log.Debug("GameplaySetup not ready yet: " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        _log.Exception(ex, "RegisterWhenReady");
                        _registerRoutine = null;
                        yield break;
                    }
                }

                yield return null;
            }

            _log.Warn("Timed out waiting to register GameplaySetup tab '" + TabName + "'");
            _registerRoutine = null;
        }
    }
}
