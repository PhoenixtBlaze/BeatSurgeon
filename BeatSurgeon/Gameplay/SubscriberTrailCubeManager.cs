using System;
using System.Collections.Generic;
using BeatSurgeon.Utils;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal sealed class SubscriberTrailCubeManager : MonoBehaviour
    {
        private class QueuedEntry
        {
            internal QueuedEntry(string requesterName)
            {
                RequesterName = NormalizeRequesterName(requesterName);
            }

            internal string RequesterName { get; private set; }
        }

        private sealed class ActiveEntry : QueuedEntry
        {
            internal ActiveEntry(GameNoteController controller, string requesterName, GameObject visualRoot)
                : base(requesterName)
            {
                Controller = controller;
                VisualRoot = visualRoot;
            }

            internal GameNoteController Controller { get; private set; }
            internal GameObject VisualRoot { get; private set; }
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("SubscriberTrailCubeManager");
        private const int MaxPendingEntries = 1024;
        private const int MaxPoolSize = 128;
        private static readonly Vector3 WarmActivationOffset = new Vector3(0f, -2048f, 0f);
        internal const int RecommendedWarmPoolSize = 8;

        private static SubscriberTrailCubeManager _instance;
        private static GameObject _go;

        private readonly LinkedList<QueuedEntry> _pendingEntries = new LinkedList<QueuedEntry>();
        private readonly Dictionary<GameNoteController, ActiveEntry> _activeEntries = new Dictionary<GameNoteController, ActiveEntry>();
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        private GameplayManager _gameplayManager;
        private GameObject _templateRoot;

        internal static SubscriberTrailCubeManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeon_SubscriberTrailCubeManager_GO");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<SubscriberTrailCubeManager>();
                }

                return _instance;
            }
        }

        internal bool HasPendingNotes => _pendingEntries.Count > 0;

        internal bool EnsureWarmPoolSize(int desiredPoolSize)
        {
            desiredPoolSize = Mathf.Clamp(desiredPoolSize, 0, MaxPoolSize);
            if (desiredPoolSize <= 0)
            {
                return true;
            }

            if (!EnsureTemplate())
            {
                return false;
            }

            while (_pool.Count < desiredPoolSize)
            {
                GameObject clone = CreatePooledInstance(warmActivate: true);
                _pool.Enqueue(clone);
            }

            return true;
        }

        internal bool QueueNotes(int requestedCount, string requesterName)
        {
            if (!IsInMap())
            {
                _log.Warn("Subscriber trail cubes ignored because gameplay is not active.");
                return false;
            }

            int normalizedCount = Mathf.Max(0, requestedCount);
            if (normalizedCount <= 0)
            {
                return false;
            }

            int availableSlots = Mathf.Max(0, MaxPendingEntries - (_pendingEntries.Count + _activeEntries.Count));
            if (availableSlots <= 0)
            {
                _log.Warn("Subscriber trail cube queue is full.");
                return false;
            }

            int enqueuedCount = Mathf.Min(normalizedCount, availableSlots);
            string normalizedRequesterName = NormalizeRequesterName(requesterName);
            for (int index = 0; index < enqueuedCount; index++)
            {
                _pendingEntries.AddLast(new QueuedEntry(normalizedRequesterName));
            }

            _log.Info(
                "Queued subscriber trail cubes requester="
                + normalizedRequesterName
                + " requested="
                + normalizedCount
                + " enqueued="
                + enqueuedCount
                + " pending="
                + _pendingEntries.Count
                + " active="
                + _activeEntries.Count);
            return enqueuedCount > 0;
        }

        internal bool TryMarkAndAttach(GameNoteController controller)
        {
            if (controller == null)
            {
                return false;
            }

            NoteData noteData = controller.noteData;
            if (!BombManager.IsEligibleBombNote(noteData))
            {
                return false;
            }

            if (_activeEntries.ContainsKey(controller))
            {
                return true;
            }

            if (_pendingEntries.First == null)
            {
                return false;
            }

            if (BombManager.IsBombWindowActive && BombManager.Instance.IsNoteMarkedAsBomb(noteData))
            {
                return false;
            }

            if (!EnsureTemplate())
            {
                _log.Warn("SubscriberTrailCubeManager: TrailCube template not available.");
                return false;
            }

            GameObject visualRoot = GetOrCreatePooledInstance();
            if (visualRoot == null)
            {
                return false;
            }

            QueuedEntry queued = _pendingEntries.First.Value;
            _pendingEntries.RemoveFirst();

            try
            {
                Transform parent = controller.noteTransform != null ? controller.noteTransform : controller.transform;
                visualRoot.transform.SetParent(parent, false);
                SetLayerRecursively(visualRoot, controller.gameObject.layer);
                visualRoot.SetActive(true);

                _activeEntries[controller] = new ActiveEntry(controller, queued.RequesterName, visualRoot);
                return true;
            }
            catch (Exception ex)
            {
                _pendingEntries.AddFirst(new QueuedEntry(queued.RequesterName));
                CleanupVisual(visualRoot);
                _log.Warn("SubscriberTrailCubeManager: attach failed: " + ex.Message);
                return false;
            }
        }

        internal bool TryConsumeMarkedNote(GameNoteController controller, out string requesterName)
        {
            requesterName = "Unknown";
            if (controller == null)
            {
                return false;
            }

            if (!_activeEntries.TryGetValue(controller, out ActiveEntry activeEntry))
            {
                return false;
            }

            _activeEntries.Remove(controller);
            requesterName = activeEntry.RequesterName;
            CleanupVisual(activeEntry.VisualRoot);
            return true;
        }

        internal bool TryRequeueMarkedNote(GameNoteController controller, string reason)
        {
            if (controller == null)
            {
                return false;
            }

            if (!_activeEntries.TryGetValue(controller, out ActiveEntry activeEntry))
            {
                return false;
            }

            _activeEntries.Remove(controller);
            _pendingEntries.AddFirst(new QueuedEntry(activeEntry.RequesterName));
            CleanupVisual(activeEntry.VisualRoot);
            _log.Debug(
                "Requeued subscriber TrailCube requester="
                + activeEntry.RequesterName
                + " reason="
                + (string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason)
                + " pending="
                + _pendingEntries.Count
                + " active="
                + _activeEntries.Count);
            return true;
        }

        private void Update()
        {
            if (!IsInMap() && (_pendingEntries.Count > 0 || _activeEntries.Count > 0))
            {
                ClearTransientGameplayState("SceneChanged");
            }
        }

        private void ClearTransientGameplayState(string reason)
        {
            foreach (ActiveEntry activeEntry in _activeEntries.Values)
            {
                CleanupVisual(activeEntry.VisualRoot);
            }

            _activeEntries.Clear();
            _pendingEntries.Clear();

            while (_pool.Count > 0)
            {
                GameObject pooled = _pool.Dequeue();
                if (pooled != null)
                {
                    UnityEngine.Object.Destroy(pooled);
                }
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                _log.Info("Cleared subscriber TrailCube state | reason=" + reason);
            }
        }

        private bool EnsureTemplate()
        {
            if (_templateRoot != null)
            {
                return true;
            }

            _templateRoot = SurgeonEffectsBundleService.GetSubscriberTrailCubeTemplate();
            return _templateRoot != null;
        }

        private GameObject GetOrCreatePooledInstance()
        {
            while (_pool.Count > 0)
            {
                GameObject pooled = _pool.Dequeue();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            return CreatePooledInstance(warmActivate: false);
        }

        private GameObject CreatePooledInstance(bool warmActivate)
        {
            GameObject clone = UnityEngine.Object.Instantiate(_templateRoot);
            UnityEngine.Object.DontDestroyOnLoad(clone);
            clone.SetActive(false);

            if (warmActivate)
            {
                WarmActivateVisual(clone);
            }

            return clone;
        }

        private static void WarmActivateVisual(GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            Transform transform = visualRoot.transform;
            Transform originalParent = transform.parent;
            Vector3 originalLocalPosition = transform.localPosition;
            Quaternion originalLocalRotation = transform.localRotation;
            Vector3 originalLocalScale = transform.localScale;

            try
            {
                transform.SetParent(null, false);
                transform.position = WarmActivationOffset;
                transform.rotation = Quaternion.identity;
                visualRoot.SetActive(true);

                foreach (TrailRenderer trailRenderer in visualRoot.GetComponentsInChildren<TrailRenderer>(true))
                {
                    trailRenderer.Clear();
                }

                foreach (ParticleSystem particleSystem in visualRoot.GetComponentsInChildren<ParticleSystem>(true))
                {
                    try
                    {
                        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        particleSystem.Simulate(0.05f, true, true, true);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                foreach (TrailRenderer trailRenderer in visualRoot.GetComponentsInChildren<TrailRenderer>(true))
                {
                    trailRenderer.Clear();
                }

                foreach (ParticleSystem particleSystem in visualRoot.GetComponentsInChildren<ParticleSystem>(true))
                {
                    try
                    {
                        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    catch
                    {
                    }
                }

                visualRoot.SetActive(false);
                transform.SetParent(originalParent, false);
                transform.localPosition = originalLocalPosition;
                transform.localRotation = originalLocalRotation;
                transform.localScale = originalLocalScale;
            }
        }

        private void CleanupVisual(GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            visualRoot.transform.SetParent(null, false);
            visualRoot.SetActive(false);

            if (_pool.Count < MaxPoolSize)
            {
                _pool.Enqueue(visualRoot);
            }
            else
            {
                UnityEngine.Object.Destroy(visualRoot);
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

        private static string NormalizeRequesterName(string requesterName)
        {
            return string.IsNullOrWhiteSpace(requesterName) ? "Unknown" : requesterName.Trim();
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }
        }
    }
}