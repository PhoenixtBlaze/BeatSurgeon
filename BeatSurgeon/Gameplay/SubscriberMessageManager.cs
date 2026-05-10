using System.Collections;
using System.Collections.Generic;
using BeatSurgeon.Utils;
using TMPro;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal sealed class SubscriberMessageManager : MonoBehaviour
    {
        private sealed class SubscriberMessageRequest
        {
            internal SubscriberMessageRequest(string requesterName, string displayText)
            {
                RequesterName = string.IsNullOrWhiteSpace(requesterName) ? "Unknown" : requesterName;
                DisplayText = displayText ?? string.Empty;
            }

            internal string RequesterName { get; private set; }
            internal string DisplayText { get; private set; }
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("SubscriberMessageManager");
        private const int MaxQueuedMessages = 10;
        private const float MessageDisplaySeconds = 10f;
        private static SubscriberMessageManager _instance;
        private static GameObject _go;

        private readonly Queue<SubscriberMessageRequest> _pendingMessages = new Queue<SubscriberMessageRequest>();
        private GameplayManager _gameplayManager;
        private GameObject _activeCanvasInstance;
        private TextMeshProUGUI _userNameText;
        private TextMeshProUGUI _messageText;
        private Coroutine _playbackRoutine;

        internal static SubscriberMessageManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_SubscriberMessageManager_GO");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<SubscriberMessageManager>();
                }

                return _instance;
            }
        }

        internal static void ClearForSceneExit()
        {
            if (_instance != null)
            {
                _instance.ClearTransientGameplayState();
            }
        }

        internal bool EnqueueMessage(string requesterName, string displayText)
        {
            if (!IsInMap())
            {
                _log.Warn("Subscriber message ignored because gameplay is not active.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayText))
            {
                _log.Warn("Subscriber message ignored because display text was empty.");
                return false;
            }

            if (_pendingMessages.Count >= MaxQueuedMessages)
            {
                _log.Warn("Subscriber message queue is full.");
                return false;
            }

            if (!EnsureCanvasInstance())
            {
                return false;
            }

            var request = new SubscriberMessageRequest(requesterName, displayText.Trim());
            _pendingMessages.Enqueue(request);
            _log.Info(
                "Queued subscriber message requestedBy="
                + request.RequesterName
                + " pending="
                + _pendingMessages.Count
                + " displayText="
                + request.DisplayText);

            if (_playbackRoutine == null)
            {
                _playbackRoutine = StartCoroutine(ProcessQueue());
            }

            return true;
        }

        private void Update()
        {
            if (!IsInMap() && (_activeCanvasInstance != null || _playbackRoutine != null || _pendingMessages.Count > 0))
            {
                ClearTransientGameplayState();
            }
        }

        private IEnumerator ProcessQueue()
        {
            while (_pendingMessages.Count > 0)
            {
                if (!IsInMap())
                {
                    ClearTransientGameplayState();
                    yield break;
                }

                if (!EnsureCanvasInstance())
                {
                    ClearTransientGameplayState();
                    yield break;
                }

                SubscriberMessageRequest request = _pendingMessages.Dequeue();
                ShowMessage(request.RequesterName, request.DisplayText);

                yield return new WaitForSeconds(MessageDisplaySeconds);

                if (_activeCanvasInstance != null)
                {
                    _activeCanvasInstance.SetActive(false);
                }
            }

            if (_activeCanvasInstance != null)
            {
                _activeCanvasInstance.SetActive(false);
            }

            _playbackRoutine = null;
        }

        private void ShowMessage(string userName, string displayText)
        {
            if (_activeCanvasInstance == null)
            {
                return;
            }

            // Re-apply the selected font each time so changes to surgeon font selection are picked up
            // and so we have a guaranteed font application in case PrepareCanvasInstance ran before
            // the font bundle was fully loaded.
            TMP_FontAsset currentFont = FontBundleLoader.BombUsernameFont;
            if (_userNameText != null)
            {
                if (currentFont != null && _userNameText.font != currentFont)
                {
                    FontBundleLoader.TryApplySelectedBombFont(_userNameText, null, cloneMaterial: true);
                }
                _userNameText.text = userName;
            }

            if (_messageText != null)
            {
                if (currentFont != null && _messageText.font != currentFont)
                {
                    FontBundleLoader.TryApplySelectedBombFont(_messageText, null, cloneMaterial: true);
                }
                _messageText.text = displayText;
            }

            _activeCanvasInstance.SetActive(true);
        }

        private bool EnsureCanvasInstance()
        {
            if (_activeCanvasInstance != null && (_userNameText != null || _messageText != null))
            {
                return true;
            }

            DestroyCanvasInstance();

            GameObject templateRoot = SurgeonEffectsBundleService.GetSubscriberCanvasTemplate();
            if (templateRoot == null)
            {
                Plugin.Log.Warn("SubscriberMessageManager: subscriber canvas template could not be loaded.");
                return false;
            }

            _activeCanvasInstance = UnityEngine.Object.Instantiate(templateRoot);
            UnityEngine.Object.DontDestroyOnLoad(_activeCanvasInstance);
            _activeCanvasInstance.name = "BeatSurgeonSubscriberMessageCanvas";

            PrepareCanvasInstance();

            bool ready = _userNameText != null || _messageText != null;
            if (!ready)
            {
                Plugin.Log.Warn("SubscriberMessageManager: subscriber canvas instance did not expose UserName or Message text fields.");
                DestroyCanvasInstance();
            }

            return ready;
        }

        private void PrepareCanvasInstance()
        {
            if (_activeCanvasInstance == null)
            {
                return;
            }

            foreach (TextMeshProUGUI tmp in _activeCanvasInstance.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.name == BundleRegistry.TwitchControllerRefs.SubscriberUserNameTextName)
                {
                    _userNameText = tmp;
                    FontBundleLoader.TryApplySelectedBombFont(_userNameText, null, cloneMaterial: true);
                }
                else if (tmp.name == BundleRegistry.TwitchControllerRefs.SubscriberMessageTextName)
                {
                    _messageText = tmp;
                    FontBundleLoader.TryApplySelectedBombFont(_messageText, null, cloneMaterial: true);
                }
            }

            Canvas canvas = _activeCanvasInstance.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null && Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }

            _activeCanvasInstance.SetActive(false);
        }

        private void ClearTransientGameplayState()
        {
            _pendingMessages.Clear();

            if (_playbackRoutine != null)
            {
                StopCoroutine(_playbackRoutine);
                _playbackRoutine = null;
            }

            DestroyCanvasInstance();
        }

        private void DestroyCanvasInstance()
        {
            _userNameText = null;
            _messageText = null;

            if (_activeCanvasInstance != null)
            {
                UnityEngine.Object.Destroy(_activeCanvasInstance);
                _activeCanvasInstance = null;
            }
        }

        private bool IsInMap()
        {
            if (_gameplayManager == null)
            {
                _gameplayManager = GameplayManager.GetInstance();
            }

            return _gameplayManager != null && _gameplayManager.IsInMap;
        }
    }
}
