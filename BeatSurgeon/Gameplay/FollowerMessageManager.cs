using System;
using System.Collections;
using System.Collections.Generic;
using BeatSurgeon.Utils;
using TMPro;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal sealed class FollowerMessageManager : MonoBehaviour
    {
        private sealed class FollowerMessageRequest
        {
            internal FollowerMessageRequest(string requesterName, string displayText)
            {
                RequesterName = string.IsNullOrWhiteSpace(requesterName) ? "Unknown" : requesterName;
                DisplayText = displayText ?? string.Empty;
            }

            internal string RequesterName { get; private set; }
            internal string DisplayText { get; private set; }
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("FollowerMessageManager");
        private const int MaxQueuedMessages = 10;
        private const float MessageTravelSeconds = 15f;
        private static FollowerMessageManager _instance;
        private static GameObject _go;

        private readonly Queue<FollowerMessageRequest> _pendingMessages = new Queue<FollowerMessageRequest>();
        private GameplayManager _gameplayManager;
        private GameObject _activeCanvasInstance;
        private Transform _lineAnchor;
        private Transform _startAnchor;
        private Transform _endAnchor;
        private Coroutine _playbackRoutine;
        private TextMeshProUGUI _activeMessageText;

        internal static FollowerMessageManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_FollowerMessageManager_GO");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<FollowerMessageManager>();
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
                _log.Warn("Follower message ignored because gameplay is not active.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(displayText))
            {
                _log.Warn("Follower message ignored because display text was empty.");
                return false;
            }

            if (_pendingMessages.Count >= MaxQueuedMessages)
            {
                _log.Warn("Follower message queue is full.");
                return false;
            }

            if (!EnsureCanvasInstance())
            {
                return false;
            }

            var request = new FollowerMessageRequest(requesterName, displayText.Trim());
            _pendingMessages.Enqueue(request);
            _log.Info(
                "Queued follower message requestedBy="
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
            if (!IsInMap() && (_activeCanvasInstance != null || _activeMessageText != null || _playbackRoutine != null || _pendingMessages.Count > 0))
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

                FollowerMessageRequest request = _pendingMessages.Dequeue();
                if (_activeCanvasInstance != null)
                {
                    _activeCanvasInstance.SetActive(true);
                }

                _activeMessageText = CreateMessageText(request.DisplayText);
                if (_activeMessageText == null)
                {
                    continue;
                }

                yield return AnimateMessage(_activeMessageText.rectTransform);

                DestroyActiveMessageText();
            }

            if (_activeCanvasInstance != null)
            {
                _activeCanvasInstance.SetActive(false);
            }

            _playbackRoutine = null;
        }

        private IEnumerator AnimateMessage(RectTransform messageRect)
        {
            if (messageRect == null || _activeCanvasInstance == null || _endAnchor == null || _startAnchor == null)
            {
                yield break;
            }

            Vector3 startingWorldPosition = _endAnchor.position;
            Vector3 endingWorldPosition = _startAnchor.position;
            messageRect.position = startingWorldPosition;
            messageRect.localRotation = Quaternion.identity;
            messageRect.localScale = Vector3.one;

            float elapsed = 0f;
            while (elapsed < MessageTravelSeconds)
            {
                if (!IsInMap() || messageRect == null || _activeCanvasInstance == null)
                {
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, MessageTravelSeconds));
                messageRect.position = Vector3.Lerp(startingWorldPosition, endingWorldPosition, t);

                yield return null;
            }

            messageRect.position = endingWorldPosition;
        }

        private TextMeshProUGUI CreateMessageText(string displayText)
        {
            if (_activeCanvasInstance == null)
            {
                return null;
            }

            GameObject textGo = new GameObject("FollowerMessageText", typeof(RectTransform));
            textGo.transform.SetParent(_activeCanvasInstance.transform, false);

            RectTransform rectTransform = textGo.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;

            float maxTextWidth = 320f;
            float maxTextHeight = 160f;
            RectTransform canvasRect = _activeCanvasInstance.transform as RectTransform;
            if (canvasRect != null)
            {
                float width = canvasRect.rect.width;
                if (width <= 0f)
                {
                    width = canvasRect.sizeDelta.x;
                }

                float height = canvasRect.rect.height;
                if (height <= 0f)
                {
                    height = canvasRect.sizeDelta.y;
                }

                maxTextWidth = Mathf.Clamp(width > 0f ? width * 0.9f : maxTextWidth, 260f, 420f);
                maxTextHeight = Mathf.Clamp(height > 0f ? Mathf.Max(height * 1.15f, maxTextHeight) : maxTextHeight, 96f, 180f);
            }

            rectTransform.sizeDelta = new Vector2(maxTextWidth, maxTextHeight);

            textGo.AddComponent<CanvasRenderer>();
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.text = displayText;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = true;
            text.margin = new Vector4(10f, 6f, 10f, 6f);
            text.fontSizeMin = 10f;
            text.fontSizeMax = 150f;
            text.fontSize = 150f;
            text.color = Color.white;

            if (!FontBundleLoader.TryApplySelectedBombFont(text, null, cloneMaterial: true))
            {
                Plugin.Log.Warn("FollowerMessageManager: failed to apply bomb font to follower message text.");
                UnityEngine.Object.Destroy(textGo);
                return null;
            }

            text.outlineWidth = 0.2f;
            text.outlineColor = Color.black;
            Vector2 preferredSize = text.GetPreferredValues(
                displayText,
                Mathf.Max(64f, maxTextWidth - (text.margin.x + text.margin.z)),
                0f);
            rectTransform.sizeDelta = new Vector2(
                maxTextWidth,
                Mathf.Clamp(preferredSize.y + text.margin.y + text.margin.w + 10f, 96f, maxTextHeight));
            text.SetAllDirty();
            text.ForceMeshUpdate();
            return text;
        }

        private bool EnsureCanvasInstance()
        {
            if (_activeCanvasInstance != null && _startAnchor != null && _endAnchor != null)
            {
                return true;
            }

            DestroyCanvasInstance();

            GameObject templateRoot = SurgeonEffectsBundleService.GetFollowerCanvasTemplate();
            if (templateRoot == null)
            {
                Plugin.Log.Warn("FollowerMessageManager: follower canvas template could not be loaded.");
                return false;
            }

            _activeCanvasInstance = UnityEngine.Object.Instantiate(templateRoot);
            UnityEngine.Object.DontDestroyOnLoad(_activeCanvasInstance);
            _activeCanvasInstance.name = "BeatSurgeonFollowerMessageCanvas";

            PrepareCanvasInstance();

            bool ready = _endAnchor != null && _startAnchor != null;
            if (!ready)
            {
                Plugin.Log.Warn("FollowerMessageManager: follower canvas instance did not expose End and Start anchors.");
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

            Transform lineTransform = _activeCanvasInstance.transform.Find(BundleRegistry.TwitchControllerRefs.FollowerLineName)
                ?? FindDescendantByNormalizedName(_activeCanvasInstance.transform, BundleRegistry.TwitchControllerRefs.FollowerLineName);
            _lineAnchor = lineTransform;
            if (lineTransform != null)
            {
                lineTransform.gameObject.SetActive(false);
            }

            _startAnchor = _activeCanvasInstance.transform.Find(BundleRegistry.TwitchControllerRefs.FollowerStartName)
                ?? FindDescendantByNormalizedName(_activeCanvasInstance.transform, BundleRegistry.TwitchControllerRefs.FollowerStartName);
            _endAnchor = _activeCanvasInstance.transform.Find(BundleRegistry.TwitchControllerRefs.FollowerEndName)
                ?? FindDescendantByNormalizedName(_activeCanvasInstance.transform, BundleRegistry.TwitchControllerRefs.FollowerEndName);

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

            DestroyActiveMessageText();
            DestroyCanvasInstance();
        }

        private void DestroyActiveMessageText()
        {
            if (_activeMessageText != null)
            {
                UnityEngine.Object.Destroy(_activeMessageText.gameObject);
                _activeMessageText = null;
            }
        }

        private void DestroyCanvasInstance()
        {
            _lineAnchor = null;
            _startAnchor = null;
            _endAnchor = null;

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

        private static Transform FindDescendantByNormalizedName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }

            string normalizedTarget = NormalizeSelectionToken(targetName);
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (NormalizeSelectionToken(transform.name) == normalizedTarget)
                {
                    return transform;
                }
            }

            return null;
        }

        private static string NormalizeSelectionToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        }
    }
}