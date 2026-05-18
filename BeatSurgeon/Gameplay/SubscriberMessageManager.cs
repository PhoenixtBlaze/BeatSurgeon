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
        private static readonly Vector3 SubscriberCanvasWorldPosition = new Vector3(0f, 2.2f, 6.556407f);
        private static readonly Vector3 SubscriberCanvasWorldEuler = new Vector3(-21.616f, 0f, 0f);
        private static readonly Vector3 SubscriberCanvasWorldScale = new Vector3(0.01f, 0.01f, 0.01f);
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

        internal bool Prewarm()
        {
            if (!EnsureCanvasInstance())
            {
                return false;
            }

            ShowMessage("Warmup", "Warmup");
            if (_userNameText != null)
            {
                _userNameText.ForceMeshUpdate();
            }

            if (_messageText != null)
            {
                _messageText.ForceMeshUpdate();
            }

            if (_activeCanvasInstance != null)
            {
                _activeCanvasInstance.SetActive(false);
            }

            return true;
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

            // Activate briefly so TMP components on newly created children can fully initialize
            // their internal material state (m_sharedMaterial) before we set outline properties.
            // SetActive(false) is called at the end of this method.
            _activeCanvasInstance.SetActive(true);

            _activeCanvasInstance.transform.SetPositionAndRotation(
                SubscriberCanvasWorldPosition,
                Quaternion.Euler(SubscriberCanvasWorldEuler));
            _activeCanvasInstance.transform.localScale = SubscriberCanvasWorldScale;

            _log.Info("SubscriberCanvas runtime pos=" + _activeCanvasInstance.transform.position
                + " euler=" + _activeCanvasInstance.transform.eulerAngles);

            // Ensure the canvas rect is large enough to contain the text fields we create below.
            RectTransform canvasRect = _activeCanvasInstance.GetComponent<RectTransform>();
            if (canvasRect != null && (canvasRect.sizeDelta.x < 400f || canvasRect.sizeDelta.y < 200f))
            {
                canvasRect.sizeDelta = new Vector2(800f, 450f);
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

            // The prefab SubscriberCanvas has no pre-built TMP children; create them dynamically.
            if (_userNameText == null)
            {
                _userNameText = CreateTextField(
                    BundleRegistry.TwitchControllerRefs.SubscriberUserNameTextName,
                    anchoredPos: new Vector2(0f, 100f),
                    sizeDelta: new Vector2(740f, 80f),
                    maxFontSize: 80f,
                    bold: true);
            }

            if (_messageText == null)
            {
                _messageText = CreateTextField(
                    BundleRegistry.TwitchControllerRefs.SubscriberMessageTextName,
                    anchoredPos: new Vector2(0f, -40f),
                    sizeDelta: new Vector2(740f, 200f),
                    maxFontSize: 150f,
                    bold: false);
            }

            _activeCanvasInstance.SetActive(false);
        }

        private TextMeshProUGUI CreateTextField(string childName, Vector2 anchoredPos, Vector2 sizeDelta, float maxFontSize, bool bold)
        {
            if (_activeCanvasInstance == null)
            {
                return null;
            }

            var go = new GameObject(childName, typeof(RectTransform));
            go.transform.SetParent(_activeCanvasInstance.transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPos;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;

            go.AddComponent<CanvasRenderer>();
            var text = go.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = true;
            text.margin = new Vector4(10f, 4f, 10f, 4f);
            text.fontSizeMin = 10f;
            text.fontSizeMax = maxFontSize;
            text.fontSize = maxFontSize;
            text.color = Color.white;
            if (bold)
            {
                text.fontStyle = FontStyles.Bold;
            }

            // Apply font before setting outline — TMP needs a non-null material to clone for the outline instance.
            FontBundleLoader.TryApplySelectedBombFont(text, null, cloneMaterial: true);

            var _mat = text.fontSharedMaterial ?? text.fontMaterial;
            if (text.font != null && _mat != null)
            {
                text.outlineWidth = 0.2f;
                text.outlineColor = Color.black;
            }

            return text;
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
